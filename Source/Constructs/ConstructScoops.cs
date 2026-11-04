using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>A scoop part's numbers (its file's <c>storage.scoop</c>, in the part's frame; docs/sandbox/CONSTRUCTS.md "Scoops").</summary>
public sealed class ScoopDef
{
    public Vector3 EdgeA, EdgeB; // the ends of the cutting edge
    public Vector3 RimA, RimB; // the ends of the mouth's far side (the edge is its near side): material leaves over the lowest of these
    public Vector3 EdgeDir; // the way the edge cuts (unit)
    public Vector3 Mouth; // the open side (unit): tipped past SpillCos against the world's up, the contents leave
    public float Width; // the edge's length (m)
    public float Reach; // how deep the edge counts as cutting (m)
    public float CutForce; // what the ground holds it back with (N, on soft dirt): under this it stands on the ground, over it the ground gives way
    public float BiteRadius; // one bite's ball (m)
    public int Bites = 1; // balls side by side along the edge at a bite
    public bool Flat; // shallow bites (DigKind.Crater) rather than round ones (DigKind.Scoop)
    public float BiteVolume; // the swept ground (m3) that makes one bite
    public float SpillCos; // the cosine of the spill angle
    public float PourRate; // units a second, tipped well past the spill angle
    public float StoneRate = 1; // how much of its speed it keeps in stone
    public readonly float[] Keep = [1f, 1f, 1f]; // the share of each GroundKind it holds on to (a skeleton bucket lets fines through)
}

/// <summary>One scoop part of a construct, server only: rebuilt with the construct, never saved (only its store is).</summary>
public struct CScoop
{
    public ushort Part;
    public bool Engaged; // the edge is in the ground: its boxes stay out of the ground's contacts, and one capped row stands for them (ConstructPhysics)
    public bool HasLast;
    public Vector3 Last; // the edge's middle a tick ago: the real motion, whatever moved it (a joint, the body, a vehicle)
    public Vector3 Row, Up, Hold; // where the ground's hold acts, and which way; which way it last held
    public float Cap, Drag; // that hold (N), and what it drags with sideways (N)
    public float Cut; // swept ground waiting for a bite (m3)
    public float Pour; // units on their way out
    public float Cool, Linger; // to the next bite; how long it still shows as digging
    public bool Stone; // its last bite was in stone
    public float Hard; // it found nothing it can cut: its boxes meet the ground for this long
}

/// <summary>What scoops need of the ground (the game; null in a world with none, and no scoop digs or pours onto it).</summary>
public interface IScoopGround
{
    /// <summary>Does ground get dug in this game on this map? Off: scoops are solid, and what they tip out is left as bundles.</summary>
    bool Live { get; }

    /// <summary>Is there room in the terrain log?</summary>
    bool LogRoom { get; }

    float Measure(in DigOp op, Span<float> byKind);

    /// <summary>Applies an op (logged, sent, wakes what rests nearby). Returns the columns it changed.</summary>
    int Dig(DigOp op);

    /// <summary>May this fill go here: nobody buried, not too high?</summary>
    bool MayFill(in DigOp op);
}

public sealed partial class Construct
{
    public const int MaxScoops = 32;
    public CScoop[]? Scoops; // (made the first time a construct has one)
    public int ScoopCount;

    /// <summary>Finds the scoops (from the segments' rebuild; no allocation after a construct's first).</summary>
    private void RebuildScoops()
    {
        ScoopCount = 0;
        for (var i = 0; i < PartCount && ScoopCount < MaxScoops; i++)
            if (Parts[i].D.IsScoop)
            {
                Scoops ??= new CScoop[MaxScoops];
                Scoops[ScoopCount++] = new CScoop { Part = (ushort)i };
            }
    }

    /// <summary>Is this part a scoop with its edge in the ground (the ground's contacts leave it alone)?</summary>
    internal bool ScoopEngaged(int part)
    {
        for (var k = 0; k < ScoopCount; k++)
            if (Scoops![k].Part == part)
                return Scoops[k].Engaged;
        return false;
    }
}

public sealed partial class ConstructWorld
{
    public IScoopGround? ScoopGround;
    internal int ScoopTicks, ScoopOps;

    /// <summary>Server: a bundle of spilled material, worth what went into it. False when the world has no room.</summary>
    public bool SpillBundle(byte material, int units, Vector3 at, Vector3 vel)
    {
        if (ConstructParts.Find(ConstructStorage.MaterialBundle) is not { } bundle || Spawn(bundle, at, vel) is not { } heap)
            return false;
        heap.Parts[0].Setting = material;
        heap.Parts[0].Rounds = (ushort)Math.Clamp(units, 1, ushort.MaxValue);
        heap.Parts[0].Broken = true;
        heap.Parts[0].Health = 0;
        heap.Wreck = true;
        heap.Damaged = true;
        heap.Owner = 255;
        heap.Rebuild();
        return true;
    }
}

/// <summary>
/// Scoops and buckets (docs/sandbox/CONSTRUCTS.md "Scoops"): a bin on an arm whose cutting edge digs when it is really pushed into
/// the ground. Each tick (server) the edge's motion is read from where it was, so a joint, a rail or a whole vehicle moves it the
/// same way. The ground under the edge holds it back with up to the scoop's cut force (a capped contact row): a parked scoop
/// stands on it, a push past that moves it, and swept ground adds up to bites, each measured and taken out through the usual dig
/// ops. What a bite gives lands in the part's own bin. Tipped past its angle a scoop pours over its lowest rim: into a store a
/// ray finds under it, else onto the ground as fill, else as Material Bundles. No allocation; nothing new on the wire: the
/// ground goes by <see cref="Msg.TerrainDig"/> and the state by the stores.
/// </summary>
public static class ConstructScoops
{
    public const int Every = 6; // ticks between looks at the contents (10 Hz)
    private const float Slack = 0.03f; // above the ground by this much the edge still counts as on it
    private const float EngageDepth = 0.05f; // a scoop lying flat is on its boxes until the edge is this far in
    private const float Ahead = 0.1f; // metres ahead of the edge that the ground is felt at
    private const float Feel = 0.2f; // metres along its motion that it looks for deeper ground
    private const float MinSpeed = 0.03f; // m/s: slower than this nothing is being pushed
    private const float MaxSpeed = 40f; // m/s: a jump in the pose (a warp, a teleport), not a cut
    private const float CutAlong = 0.2f; // the share of its motion that must go the way the edge points
    private const int MaxBacklog = 4; // swept ground waits for this many bites; past that the ground holds as hard as for a full scoop
    private const float BusyLinger = 0.4f;
    private const float PourReach = 80f; // metres down that a pour looks for somewhere to land
    private const float BundleEvery = 100f; // the biggest load a bundle takes from a pour
    private const int Milli = 1000; // a store's JobAmount: thousandths of a unit waiting to be whole
    private const float RayStart = 0.03f;
    private const float HardSeconds = 0.5f;
    private const float StallReach = 0.3f; // metres from the ground that a stalled joint's scoop counts as wedged in it
    private const float StallOver = 1.5f; // load against rating
    private const int MinLump = 4; // units
    private const float MinFill = 0.35f; // metres: a smaller ball misses a half-metre column's middle
    private static readonly List<Construct> _near = new(32);

    /// <summary>What <see cref="CStore.Blocked"/> says on a scoop (the look line; on the wire with the store).</summary>
    public const byte BlockNone = 0, BlockOtherGround = 1, BlockHasOre = 2, BlockLogFull = 3, PourInto = 4, PourGround = 5, PourBundles = 6, PourNowhere = 7;

    // ---- The file --------------------------------------------------------------------------------------------------

    /// <summary>A part's <c>storage.scoop</c> block.</summary>
    internal static void Parse(ConstructPartDef d, JsonElement b, string file, string bw)
    {
        if (b.ValueKind != JsonValueKind.Object || !d.IsBin || d.IsVolumeStore || d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: storage \"scoop\" is an object on a bin that takes \"{ConstructStorage.Any}\" material");
        ConstructParts.Known(b, ["edge", "rim", "cuts", "mouth", "reach", "cutForce", "biteRadius", "bites", "flat", "spillDegrees", "pourSeconds", "stoneRate", "keep"], $"{bw}.scoop");
        var s = new ScoopDef();
        void Pair(string key, out Vector3 x, out Vector3 y)
        {
            var e = ConstructParts.Need(b, key, $"{bw}.scoop");
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
                throw new ConstructPartException($"{file}: storage.scoop.{key} is two points");
            x = ConstructParts.Vec(e[0], file, $"storage.scoop.{key}");
            y = ConstructParts.Vec(e[1], file, $"storage.scoop.{key}");
            if (Vector3.Distance(x, y) < 0.05f)
                throw new ConstructPartException($"{file}: storage.scoop.{key}'s two points are the same");
            if (Outside(d, x) || Outside(d, y))
                throw new ConstructPartException($"{file}: storage.scoop.{key} is outside the part");
        }
        Vector3 Unit(string key)
        {
            var v = ConstructParts.Vec(ConstructParts.Need(b, key, $"{bw}.scoop"), file, $"storage.scoop.{key}");
            if (v.LengthSquared() < 0.01f)
                throw new ConstructPartException($"{file}: storage.scoop.{key} is a direction");
            return Vector3.Normalize(v);
        }
        float Number(string key, float lo, float hi)
        {
            var v = ConstructParts.Num(ConstructParts.Need(b, key, $"{bw}.scoop"), file, $"storage.scoop.{key}");
            ConstructParts.Range(v, lo, hi, file, $"storage.scoop.{key}");
            return v;
        }
        Pair("edge", out s.EdgeA, out s.EdgeB);
        Pair("rim", out s.RimA, out s.RimB);
        s.EdgeDir = Unit("cuts");
        s.Mouth = Unit("mouth");
        s.Width = Vector3.Distance(s.EdgeA, s.EdgeB);
        s.Reach = Number("reach", 0.05f, 100);
        s.CutForce = Number("cutForce", 10, 1e9f);
        s.BiteRadius = Number("biteRadius", 0.2f, 60);
        s.Bites = ConstructParts.Opt(b, "bites") is { } n ? (int)ConstructParts.Num(n, file, "storage.scoop.bites") : 1;
        ConstructParts.Range(s.Bites, 1, 8, file, "storage.scoop.bites");
        s.Flat = ConstructParts.Opt(b, "flat") is { ValueKind: JsonValueKind.True };
        s.SpillCos = MathF.Cos(Number("spillDegrees", 30, 170) * MathF.PI / 180f);
        s.PourRate = d.BulkCapacity / Number("pourSeconds", 0.2f, 60);
        s.StoneRate = ConstructParts.Opt(b, "stoneRate") is { } sr ? ConstructParts.Num(sr, file, "storage.scoop.stoneRate") : 1;
        ConstructParts.Range(s.StoneRate, 0.01f, 1, file, "storage.scoop.stoneRate");
        if (ConstructParts.Opt(b, "keep") is { } keep)
        {
            ConstructParts.Known(keep, DiggingTable.KindNames, $"{bw}.scoop.keep");
            for (var k = 0; k < s.Keep.Length; k++)
                if (ConstructParts.Opt(keep, DiggingTable.KindNames[k]) is { } v)
                {
                    s.Keep[k] = ConstructParts.Num(v, file, "storage.scoop.keep");
                    ConstructParts.Range(s.Keep[k], 0.01f, 1, file, "storage.scoop.keep");
                }
        }
        var r = s.BiteRadius;
        s.BiteVolume = s.Bites * r * r * r * (s.Flat ? 0.8f : 1.4f); // (about what the balls take out of flat ground: half of each)
        d.Scoop = s;
    }

    private static bool Outside(ConstructPartDef d, Vector3 v)
    {
        const float Slop = 0.05f;
        var lo = d.BoundsMin - new Vector3(Slop);
        var hi = d.BoundsMax + new Vector3(Slop);
        return v != Vector3.Clamp(v, lo, hi);
    }

    // ---- The step --------------------------------------------------------------------------------------------------

    public static void Step(ConstructWorld world, IWorldQuery query, float dt)
    {
        var slow = ++world.ScoopTicks >= Every;
        if (slow)
        {
            world.ScoopTicks = 0;
            world.ScoopOps = 0;
        }
        var bodies = world.Bodies;
        for (var i = 0; i < bodies.Count; i++)
        {
            var c = bodies[i];
            if (c.ScoopCount == 0 && !c.Dirty)
                continue;
            if (c.Dirty)
                c.Rebuild();
            for (var k = 0; k < c.ScoopCount; k++)
                One(world, query, c, k, slow, dt);
        }
    }

    /// <summary>A point of the part in the world.</summary>
    private static Vector3 At(in CPart p, Quaternion q, Vector3 t, Vector3 v) => t + Vector3.Transform(p.Pos + Rot24.Apply(p.Rot, v), q);

    private static Vector3 Dir(in CPart p, Quaternion q, Vector3 v) => Vector3.Transform(Rot24.Apply(p.Rot, v), q);

    /// <summary>How far a point is under the ground (below 0: above it), read through <see cref="TerrainData"/> or the planetoid.</summary>
    private static float Depth(IWorldQuery query, Vector3 p)
    {
        if (query.Terrain is { } t)
            return t.GroundBelow(p.X, p.Z, p.Y) - p.Y;
        return query is PhysicsWorld { Planet: { } planet } ? -planet.Altitude(p) : float.NegativeInfinity;
    }

    private static void One(ConstructWorld world, IWorldQuery query, Construct c, int k, bool slow, float dt)
    {
        ref var s = ref c.Scoops![k];
        s.Cool = MathF.Max(0, s.Cool - dt);
        s.Linger = MathF.Max(0, s.Linger - dt);
        s.Hard = MathF.Max(0, s.Hard - dt);
        if (world.StoreOf(c, s.Part) is not { } st)
        {
            s.Engaged = false;
            return;
        }
        if (c.Asleep && (!slow || st.Amount == 0))
        {
            s.HasLast = false;
            if (st.Amount == 0 && st.Blocked != BlockNone)
                SetBlocked(world, st, BlockNone);
            return;
        }
        ref var p = ref c.Parts[s.Part];
        var d = p.D;
        c.WorldFrame(p.Seg, out var q, out var t);
        if (!c.Asleep)
            Cut(world, query, c, ref s, in p, d, st, q, t, dt);
        if (slow)
        {
            ConstructGatherer.SetBusy(world, st, s.Linger > 0);
            Tip(world, query, c, ref s, in p, d, st, q, t);
        }
    }

    // ---- Digging ---------------------------------------------------------------------------------------------------

    private static void Cut(ConstructWorld world, IWorldQuery query, Construct c, ref CScoop s, in CPart p, ConstructPartDef d, CStore st, Quaternion q, Vector3 t, float dt)
    {
        var sd = d.Scoop!;
        var a = At(p, q, t, sd.EdgeA);
        var b = At(p, q, t, sd.EdgeB);
        var mid = (a + b) * 0.5f;
        var move = s.HasLast ? mid - s.Last : Vector3.Zero;
        s.Last = mid;
        s.HasLast = true;
        var speed = move.Length() / dt;
        if (speed > MaxSpeed)
        {
            move = Vector3.Zero;
            speed = 0;
        }
        var g = world.ScoopGround;
        if (g is not { Live: true } || p.Broken || c.PartCount < 2 || s.Hard > 0)
        {
            s.Engaged = false; // (a loose scoop lies on the ground like anything else; so does a broken one, and one where ground can't be dug)
            s.Cut = 0;
            return;
        }
        var up = GravityFrames.UpAt(mid);
        var cuts = Dir(p, q, sd.EdgeDir);
        // The edge and a hand ahead of it, a little past its ends: its boxes meet the ground there, and a stalled scoop must feel it.
        var ahead = cuts * MathF.Max(Ahead, sd.Reach * 0.1f);
        var wider = (b - a) * 0.04f;
        var depth = MathF.Max(MathF.Max(Depth(query, mid), Depth(query, mid + ahead)), MathF.Max(MathF.Max(Depth(query, a), Depth(query, a - wider + ahead)), MathF.Max(Depth(query, b), Depth(query, b + wider + ahead))));
        var sunk = depth < -(sd.Reach + sd.Width) ? depth : Sunk(query, in p, d, q, t); // (well clear of the ground: nothing to feel)
        var full = st.Amount >= d.BulkCapacity;
        s.Engaged = s.Engaged ? MathF.Max(depth, sunk) > -Slack
            : MathF.Max(depth, sunk) > EngageDepth || !full && depth > -Slack && Vector3.Dot(cuts, up) < -0.1f || sunk > -StallReach && Stalled(c, p.Seg);
        if (!s.Engaged)
        {
            s.Cut = 0;
            s.Hold = up;
            if (st.Blocked is BlockOtherGround or BlockHasOre or BlockLogFull)
                SetBlocked(world, st, BlockNone);
            return;
        }
        var way = speed > 0 ? move / (speed * dt) : Vector3.Zero;
        var along = speed > 0 ? Vector3.Dot(move, cuts) / dt : 0;
        var moving = speed >= MinSpeed;
        var cutting = !full && moving && along >= CutAlong * speed;
        var into = moving && Depth(query, mid + way * Feel) > Depth(query, mid) + 0.01f; // (going deeper, not coming out)
        var table = DiggingTable.Current;
        var force = sd.CutForce * (s.Stone ? 1 + (table.ScoopHardness[(int)GroundKind.Stone] - 1) * (1 - sd.StoneRate) : 1f); // (a scoop made for stone feels it least)
        var hold = 1f;
        if (cutting)
        {
            s.Cut += sd.Width * Math.Clamp(depth + Slack, 0.02f, sd.Reach) * along * dt * (s.Stone ? sd.StoneRate : 1f);
            s.Cut = MathF.Min(s.Cut, sd.BiteVolume * MaxBacklog);
            s.Linger = BusyLinger;
            if (s.Cut >= sd.BiteVolume && s.Cool <= 0 && world.ScoopOps < table.ScoopOpsPerStep)
            {
                world.ScoopOps++;
                s.Cool = table.ScoopBiteSeconds;
                if (Bite(world, query, g, c, ref s, d, st, mid, Vector3.Normalize(b - a), way, up, depth))
                    s.Cut = MathF.Max(0, s.Cut - sd.BiteVolume);
            }
            hold = Math.Clamp(s.Cut / sd.BiteVolume, 1f, table.ScoopFullForce); // (pushed faster than it bites, the ground holds it back)
        }
        else if (into)
            hold = table.ScoopFullForce; // (a full one only pushes; so does one shoved at the ground any way but edge first)
        // The row pushes back along the motion it is resisting; coming out, it lets go. Stopped, it holds the way it last held, so a load stays stopped.
        if (moving)
            s.Hold = into ? -way : up;
        else if (full || s.Cap > force)
            hold = table.ScoopFullForce;
        s.Row = Depth(query, a) > Depth(query, b) ? a : b;
        s.Row = (s.Row + mid) * 0.5f;
        s.Up = s.Hold == Vector3.Zero ? up : s.Hold;
        s.Drag = force;
        s.Cap = force * hold;
    }

    /// <summary>Is a joint this part hangs on pushing past its rating? Against the ground, that is a scoop wedged in the columns it dug: the ground gives way to it, or an arm could never bring a full one back out.</summary>
    private static bool Stalled(Construct c, int seg)
    {
        for (var guard = 0; seg > 0 && seg <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            ref var j = ref c.Joints[seg - 1];
            if (j.Over > StallOver)
                return true;
            seg = j.ParentSeg;
        }
        return false;
    }

    /// <summary>How deep the part goes into the ground (below 0: it is clear of it), felt at its corners, faces and middles: ground goes in half-metre columns, and a long bucket can rest on one between its corners.</summary>
    private static float Sunk(IWorldQuery query, in CPart p, ConstructPartDef d, Quaternion q, Vector3 t)
    {
        var lo = d.BoundsMin;
        var mid = (d.BoundsMin + d.BoundsMax) * 0.5f;
        var hi = d.BoundsMax;
        var most = float.NegativeInfinity;
        for (var k = 0; k < 27; k++)
        {
            var (ix, iy, iz) = (k % 3, k / 3 % 3, k / 9);
            var v = new Vector3(ix == 0 ? lo.X : ix == 1 ? mid.X : hi.X, iy == 0 ? lo.Y : iy == 1 ? mid.Y : hi.Y, iz == 0 ? lo.Z : iz == 1 ? mid.Z : hi.Z);
            most = MathF.Max(most, Depth(query, At(p, q, t, v)));
        }
        return most;
    }

    /// <summary>Takes a bite out of the ground ahead of the edge. False when it took nothing (the look line says why).</summary>
    private static bool Bite(ConstructWorld world, IWorldQuery query, IScoopGround g, Construct c, ref CScoop s, ConstructPartDef d, CStore st, Vector3 mid, Vector3 along, Vector3 motion, Vector3 up, float depth)
    {
        var sd = d.Scoop!;
        var table = DiggingTable.Current;
        if (!g.LogRoom)
        {
            SetBlocked(world, st, BlockLogFull);
            return false;
        }
        var kind = sd.Flat ? DigKind.Crater : DigKind.Scoop;
        var r = sd.BiteRadius;
        Span<float> vol = stackalloc float[(int)GroundKind.COUNT];
        var centre = BiteAt(mid, motion, up, r);
        var probe = new DigOp(kind, centre, r).Quantised;
        if (g.Measure(in probe, vol) <= 0)
        {
            if (Depth(query, mid) > EngageDepth)
                s.Hard = HardSeconds; // (in the ground, and none of it can be cut: bedrock, the map's edge. It meets what is there as a solid)
            return false; // (else the hole is already there)
        }
        // What it holds on to, and which one that is: a scoop holds one material.
        var best = -1;
        var kept = 0f;
        for (var k = 0; k < vol.Length; k++)
        {
            var v = vol[k] * sd.Keep[k];
            kept += v;
            if (best < 0 || v > vol[best] * sd.Keep[best])
                best = k;
        }
        if (st.Amount > 0)
        {
            best = KindOf(st.Material);
            if (best < 0)
            {
                SetBlocked(world, st, BlockHasOre);
                return false;
            }
            if (vol[best] * sd.Keep[best] < kept * 0.5f)
            {
                SetBlocked(world, st, BlockOtherGround);
                return false;
            }
        }
        s.Stone = (GroundKind)best == GroundKind.Stone;
        var material = Game.MaterialOf((GroundKind)best);
        // Near the top of a load the bite shrinks to the room left.
        var room = d.BulkCapacity - st.Amount;
        var perBall = vol[best] * sd.Keep[best] * table.Units[best];
        if (perBall * sd.Bites > room)
            r = MathF.Max(0.2f, r * MathF.Cbrt(room / (perBall * sd.Bites)));
        var gained = 0f;
        for (var n = 0; n < sd.Bites; n++)
        {
            var off = (n - (sd.Bites - 1) * 0.5f) * (sd.Width / sd.Bites);
            var op = new DigOp(kind, BiteAt(mid + along * off, motion, up, r), r).Quantised;
            g.Measure(in op, vol);
            if (g.Dig(op) != 0)
                gained += vol[best] * sd.Keep[best] * table.Units[best];
        }
        if (gained <= 0)
            return false;
        st.JobAmount += (int)(gained * Milli);
        var whole = st.JobAmount / Milli;
        st.JobAmount -= whole * Milli;
        if (whole > 0)
            world.Deposit(c, s.Part, whole, material); // (past its room the rest is lost: a heaped bucket's slop)
        SetBlocked(world, st, BlockNone);
        return true;
    }

    /// <summary>Where a bite's ball sits: a little ahead of the edge, and only a little of it below: the cut is under the edge, not a radius below it.</summary>
    private static Vector3 BiteAt(Vector3 edge, Vector3 motion, Vector3 up, float r) => edge + motion * (r * 0.5f) + up * (r * 0.7f);

    /// <summary>The kind of ground a material is, or -1.</summary>
    private static int KindOf(byte material)
    {
        for (var k = 0; k < (int)GroundKind.COUNT; k++)
            if (Game.MaterialOf((GroundKind)k) == material)
                return k;
        return -1;
    }

    private static void SetBlocked(ConstructWorld world, CStore st, byte why)
    {
        if (st.Blocked == why)
            return;
        st.Blocked = why;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
    }

    // ---- Tipping ---------------------------------------------------------------------------------------------------

    private static void Tip(ConstructWorld world, IWorldQuery query, Construct c, ref CScoop s, in CPart p, ConstructPartDef d, CStore st, Quaternion q, Vector3 t)
    {
        var sd = d.Scoop!;
        var pouring = st.Blocked >= PourInto;
        if (st.Amount <= 0)
        {
            if (pouring)
                SetBlocked(world, st, BlockNone);
            s.Pour = 0;
            return;
        }
        var mouth = Dir(p, q, sd.Mouth);
        var rimMid = At(p, q, t, (sd.EdgeA + sd.EdgeB) * 0.5f);
        var gravity = ConstructGravity.GravityFor(c, rimMid);
        var pull = gravity.Length();
        var cos = pull > 0.01f ? Vector3.Dot(mouth, -gravity / pull) : 1f; // (no gravity: nothing falls out)
        if (cos >= sd.SpillCos || p.Broken)
        {
            s.Pour = 0;
            if (pouring)
                SetBlocked(world, st, BlockNone);
            return;
        }
        // Over the lowest of the mouth's rim. With that in the ground nothing can run out.
        var down = gravity / pull;
        var lip = rimMid;
        Span<Vector3> rim = [At(p, q, t, (sd.RimA + sd.RimB) * 0.5f), At(p, q, t, (sd.EdgeA + sd.RimA) * 0.5f), At(p, q, t, (sd.EdgeB + sd.RimB) * 0.5f)];
        foreach (var v in rim)
            if (Vector3.Dot(v - lip, down) > 0)
                lip = v;
        if (s.Engaged || Depth(query, lip) > -Slack)
        {
            s.Pour = 0;
            if (pouring)
                SetBlocked(world, st, BlockNone);
            return;
        }
        var past = Math.Clamp((sd.SpillCos - cos) / 0.3f, 0.2f, 1f); // (a little past the angle it dribbles; about 20 degrees past it, it runs)
        s.Pour += sd.PourRate * past * Every / World.TickRate;
        var units = Math.Min((int)s.Pour, st.Amount);
        if (units > 0)
            Into(world, query, c, ref s, d, st, lip + down * RayStart, down, units);
    }

    /// <summary>Sends <paramref name="units"/> of a tipped scoop to what is under its lip.</summary>
    private static void Into(ConstructWorld world, IWorldQuery query, Construct c, ref CScoop s, ConstructPartDef d, CStore st, Vector3 from, Vector3 down, int units)
    {
        var hitConstruct = FindBelow(world, c, s.Part, from, down, out var body, out var part, out var dist);
        var mat = st.Material;
        if (hitConstruct && part >= 0)
        {
            var put = Give(world, body!, part, units, mat);
            if (put > 0)
            {
                world.Withdraw(c, s.Part, put);
                s.Pour -= put;
                SetBlocked(world, st, PourInto);
                return;
            }
        }
        var filter = new QueryFilter(QueryLayers.World);
        var hitWorld = query.Raycast(from, down, PourReach, filter, out var rh);
        if (!hitConstruct && hitWorld && rh.Shape.Kind == ShapeKind.Terrain && KindOf(mat) >= 0 && world.ScoopGround is { Live: true, LogRoom: true } g)
        {
            var lump = Math.Min(PourChunk(d), st.Amount);
            if (units < lump)
                return; // (a heap goes down a good lump at a time: a lump is one op in the log)
            var took = Fill(world, g, c, s.Part, query, from + down * rh.Distance, down, KindOf(mat), lump, st.Amount == lump);
            if (took >= 0)
            {
                s.Pour = MathF.Max(0, s.Pour - took);
                SetBlocked(world, st, PourGround);
                return;
            }
        }
        if (units < BundleLoad(d, st))
            return;
        var at = from + down * 0.2f;
        var load = Math.Min(BundleLoad(d, st), st.Amount);
        if (world.SpillBundle(mat, load, at, c.PointVel(at)))
        {
            world.Withdraw(c, s.Part, load);
            s.Pour = MathF.Max(0, s.Pour - load);
            SetBlocked(world, st, PourBundles);
        }
        else
            SetBlocked(world, st, PourNowhere); // (the world is full of bodies: it stays in the scoop)
    }

    /// <summary>A pour's lump onto the ground: an eighth of the scoop, and big enough to be more than a column's sliver.</summary>
    private static int PourChunk(ConstructPartDef d) => Math.Max(MinLump, d.BulkCapacity / 8);

    private static int BundleLoad(ConstructPartDef d, CStore st) => Math.Min(st.Amount, Math.Max(1, (int)MathF.Min(BundleEvery, d.BulkCapacity / 4f)));

    /// <summary>
    /// A lump of a pour goes on the ground as a fill, on the lowest spot within reach of where it lands, so what is poured in one
    /// place spreads into a heap. Returns the units it took out of the scoop, or -1 when there is no room for it there.
    /// </summary>
    private static int Fill(ConstructWorld world, IScoopGround g, Construct c, int part, IWorldQuery query, Vector3 at, Vector3 down, int kind, int units, bool last)
    {
        var table = DiggingTable.Current;
        var volume = units / table.Units[kind];
        var r = Math.Clamp(MathF.Cbrt(volume / 1.2f), MinFill, 20f); // (a ball sunk 3/10 of its radius in leaves about 1.2 r^3 above flat ground)
        var low = at;
        Span<Vector2> ring = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
        var side = Vector3.Normalize(Vector3.Cross(down, MathF.Abs(down.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY));
        var side2 = Vector3.Cross(down, side);
        foreach (var w in ring)
        {
            var spot = at + (side * w.X + side2 * w.Y) * (r * 1.5f);
            var dip = Depth(query, spot); // (how far under the ground the same height is: below 0 the ground there is lower)
            if (dip > -2 * r && Vector3.Dot(low - at, down) < -dip)
                low = spot + down * -dip;
        }
        var kindOp = (DigKind)((int)DigKind.FillDirt + kind);
        var op = new DigOp(kindOp, low + down * (r * 0.3f), r).Quantised;
        Span<float> vol = stackalloc float[(int)GroundKind.COUNT];
        var total = g.Measure(in op, vol);
        if (total > volume)
        {
            // Never more ground than went in: a lump smaller than a column's worth waits for the next, and the very last of a load is dust.
            op = (op with { Radius = r * MathF.Cbrt(volume / total) }).Quantised;
            total = g.Measure(in op, vol);
        }
        if (total > volume || total <= 0 && !last)
            return last && g.MayFill(in op) ? Dust(world, c, part, units) : -1;
        if (total <= 0)
            return Dust(world, c, part, units);
        if (!g.MayFill(in op) || g.Dig(op) == 0)
            return -1;
        var cost = Math.Clamp((int)MathF.Ceiling(total * table.Units[kind]), 1, units);
        world.Withdraw(c, part, cost);
        return cost;
    }

    private static int Dust(ConstructWorld world, Construct c, int part, int units)
    {
        world.Withdraw(c, part, units);
        return units;
    }

    /// <summary>
    /// The first construct on a ray down from a scoop: its part and the distance. A Storage Volume's box takes what falls into it
    /// though nothing there is solid, so the box counts when the ray reaches it first. The scoop's own part is passed through; the rest
    /// of its own construct is not (a machine can load itself).
    /// </summary>
    private static bool FindBelow(ConstructWorld world, Construct c, int self, Vector3 from, Vector3 down, out Construct? body, out int part, out float dist)
    {
        body = null;
        part = -1;
        dist = PourReach;
        var o = from;
        for (var tries = 0; tries < 4; tries++)
        {
            if (!world.Raycast(o, down, dist - Vector3.Distance(o, from), out var hit))
                break;
            var d = Vector3.Distance(from, hit.Point);
            if (hit.Body == c && hit.Part == self)
            {
                o = hit.Point + down * 0.02f;
                continue;
            }
            body = hit.Body;
            part = hit.Part;
            dist = d;
            break;
        }
        var near = world.Near(from + down * (dist * 0.5f), dist * 0.5f + 1, _near);
        for (var k = 0; k < near.Count; k++)
        {
            var b = near[k];
            if (!b.HasVolumeStore)
                continue;
            for (var i = 0; i < b.PartCount; i++)
                if (b.Parts[i].D.IsVolumeStore && !b.Parts[i].Broken)
                {
                    b.WorldFrame(b.Parts[i].Seg, out var q, out var t);
                    var iq = Quaternion.Conjugate(q);
                    ConstructStoreVolume.Usable(b, i, out var min, out var max);
                    if (Geometry.RayAabb(min, max, Vector3.Transform(from - t, iq), Vector3.Transform(down, iq), dist) is { } e && e <= dist)
                    {
                        body = b;
                        part = i;
                        dist = e;
                    }
                }
        }
        return body != null;
    }

    /// <summary>Into a store a bulk material can land in (a bin, a Storage Volume, a production machine's or an Assembler's input). Returns how much went in.</summary>
    private static int Give(ConstructWorld world, Construct to, int part, int units, byte material)
    {
        if (to.Parts[part].Broken || world.StoreOf(to, part) == null)
            return 0;
        var d = to.DefOf(part);
        return d.IsAssembler ? world.FeedAssembler(to, part, units, material)
            : d.IsRefiner ? world.Feed(to, part, units, material)
            : d.IsBin && !d.IsRecycler && !d.IsGatherer && !d.IsExtruder ? world.Deposit(to, part, units, material)
            : 0;
    }

    // ---- The look line ---------------------------------------------------------------------------------------------

    /// <summary>"Digging Bucket: 12/40 dirt, digging - full: tip it out - F: take 12 dirt".</summary>
    public static string Look(ConstructPartDef d, CStore st, string? action)
    {
        var tbl = RecyclingTable.Current;
        var fill = st.Amount > 0 ? $"{st.Amount}/{d.BulkCapacity} {tbl.NameOf(st.Material)}" : $"empty, holds {d.BulkCapacity}";
        var state = st.Blocked switch
        {
            PourInto => "pouring into a store",
            PourGround => "pouring on the ground",
            PourBundles => "spilling",
            _ => st.Busy ? "digging" : null,
        };
        var problem = st.Blocked switch
        {
            BlockOtherGround => $"this ground isn't {tbl.NameOf(st.Material)}: tip it out first",
            BlockHasOre => $"it holds {tbl.NameOf(st.Material)}: tip it out first",
            BlockLogFull => "the ground has taken all the changes it can this match",
            PourNowhere => "nowhere for it to go: the world is full",
            _ => st.Amount >= d.BulkCapacity ? "full: tip it out" : null,
        };
        return SandboxKeys.Line(d.Name, SandboxKeys.Join(fill, state), problem, action);
    }
}
