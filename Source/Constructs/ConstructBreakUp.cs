using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Break-up (docs/sandbox/design/capital-battles.md): a big body snaps in two where its structure has been shot away. Across
/// its long axis, every <see cref="Slice"/> metres, a plane's strength is what still holds it there: each part that spans it
/// and each link that crosses it, by the health of a breach-sized patch of it (<see cref="Patch"/>: a 60 m hull plate's whole
/// health would make it never give way to a hole a few metres wide), less the damage done within a slice of the plane
/// (<see cref="ConstructWorld.BreakHurt"/>, where each hit landed). The first time the body is hurt that is its base. When a
/// plane is down to <see cref="SnapShare"/> of its base, with enough of the body either side, the body
/// splits there (a wreck goes off with the far side, the root's side stays) and secondary blasts go off along the break
/// over the next ticks. A part never cut off from the root still breaks away: the snap is the structure's, not the links'.
/// Server only (damage is the server's); a client sees the split as any other (a structure change and a new body).
/// Bounded: a body is looked at only after a hit, at most every <see cref="Every"/> ticks, in O(parts + links); one snap a tick.
/// </summary>
public static class ConstructBreakUp
{
    public const int MinParts = 1024; // bodies this big can break up (a capital ship); smaller ones break by their links alone

    /// <summary>A body that breaks up by planes and sheds small parts as scrap: capital-sized and free (an anchored base keeps its parts to salvage).</summary>
    public static bool Big(Construct c) => c.PartCount >= MinParts && !c.Anchored;

    /// <summary>A part that may go as scrap drawn only: light, plain (no logic, no seat in use, nothing that goes off), so nothing that works is lost unseen.</summary>
    public static bool Scrappable(Construct c, int part)
    {
        var d = c.DefOf(part);
        return d.Mass < ScrapMass && d.Logic == CLogic.None && d.Explosive == null && c.Parts[part].SitterPlus1 == 0;
    }
    public const float Slice = 8f; // metres between the planes looked at
    public const float SnapShare = 0.35f; // a plane snaps at this share of its base strength
    public const float MinBase = 5000f; // ...if it had at least this much health across it (a plane through a thin neck isn't one)
    public const int MinSide = 160; // parts either side of it at least (no slivers off the break)
    public const int Every = 6; // ticks between looks at a body that was hurt
    public const int Blasts = 6; // secondary blasts along a break
    public const float BlastRadius = 7f, BlastDamage = 350f;

    public const int ScrapParts = 8; // ...and a piece of at most this many light parts that it leaves hanging
    public const float ScrapMass = 4000f; // kg: a part lighter than this shot off a body this big goes as scrap drawn only (CFireKind.Scrap), not a wreck body
    public const float BreachArea = 4f; // square metres: a part's strength at a plane is the health of this much of it

    public static bool On = true;
    public static int Snaps; // (a count, for the check)

    /// <summary>A part's health left, 0 to 1 (a wrecked one holds nothing).</summary>
    private static float Held(in CPart p) => p.Broken || p.D.Health <= 0 ? 0f : Math.Clamp(p.Health / p.D.Health, 0f, 1f);

    /// <summary>The health of a breach-sized patch of a part (all of a small one).</summary>
    public static float Patch(ConstructPartDef d)
    {
        var e = d.BoundsMax - d.BoundsMin;
        var area = MathF.Max(e.X * e.Y, MathF.Max(e.Y * e.Z, e.X * e.Z)); // (its biggest face)
        return d.Health * MathF.Min(1f, BreachArea / MathF.Max(0.01f, area));
    }

    /// <summary>
    /// Server: part <paramref name="part"/> of <paramref name="c"/> is about to take <paramref name="taken"/> at
    /// <paramref name="at"/> (world; null: its centre). Its base is taken the first time; the slice the hit is in is hurt
    /// (by a patch's health at most); the body is looked at soon.
    /// </summary>
    public static void Hit(ConstructWorld world, Construct c, int part, float taken, Vector3? at)
    {
        if (!On || !Big(c))
            return;
        if (c.BreakBase == null || c.PartCount > c.BreakParts)
            Base(c); // (parts added since the base was taken: built or mended, so the hurt so far is forgotten)
        var p = at ?? ConstructRender.PartCentre(c, part);
        var along = Vector3.Transform(p - c.Pos, Quaternion.Conjugate(c.Rot))[c.BreakAxis] - c.BreakMin;
        var slab = (int)MathF.Floor(along / Slice);
        if ((uint)slab < (uint)c.BreakHurt!.Length)
            c.BreakHurt[slab] += MathF.Min(taken, Patch(c.DefOf(part)));
        if (c.BreakDue)
            return;
        c.BreakDue = true;
        world.BreakQueue.Add(c.Id);
    }

    /// <summary>The body's long axis, its extent and each plane's strength now.</summary>
    private static void Base(Construct c)
    {
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        for (var i = 0; i < c.PartCount; i++)
        {
            c.RestBounds(i, out var lo, out var hi);
            mn = Vector3.Min(mn, lo);
            mx = Vector3.Max(mx, hi);
        }
        var size = mx - mn;
        var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        c.BreakAxis = (byte)axis;
        c.BreakMin = mn[axis];
        var planes = Math.Clamp((int)MathF.Ceiling(size[axis] / Slice) + 1, 2, 4096);
        c.BreakBase = new float[planes];
        c.BreakHurt = new float[planes];
        c.BreakParts = c.PartCount;
        using var scratch = Scratch.Begin();
        Strength(c, c.BreakBase, scratch.Take<float>(c.PartCount));
    }

    /// <summary>Each plane's strength into <paramref name="s"/> (the part centres along the axis into <paramref name="centre"/>).</summary>
    private static void Strength(Construct c, Span<float> s, Span<float> centre)
    {
        s.Clear();
        var axis = c.BreakAxis;
        var planes = s.Length;
        for (var i = 0; i < c.PartCount; i++)
        {
            c.RestBounds(i, out var lo, out var hi);
            var a = lo[axis] - c.BreakMin;
            var b = hi[axis] - c.BreakMin;
            centre[i] = (a + b) * 0.5f;
            Add(s, a, b, Held(c.Parts[i]) * Patch(c.DefOf(i)), planes);
        }
        for (var l = 0; l < c.LinkCount; l++)
        {
            ref var link = ref c.Links[l];
            if (link.A >= c.PartCount || link.B >= c.PartCount)
                continue;
            float a = centre[link.A], b = centre[link.B];
            Add(s, MathF.Min(a, b), MathF.Max(a, b), MathF.Min(Held(c.Parts[link.A]) * Patch(c.DefOf(link.A)), Held(c.Parts[link.B]) * Patch(c.DefOf(link.B))), planes);
        }
        var run = 0f;
        for (var k = 0; k < planes; k++)
        {
            run += s[k];
            s[k] = run;
        }
        if (c.BreakHurt is { } hurt)
            for (var k = 0; k < planes; k++)
                s[k] = MathF.Max(0f, s[k] - (k > 0 ? hurt[k - 1] : 0f) - hurt[k]); // (what was hit in the slice either side of it)
    }

    /// <summary>A span from <paramref name="a"/> to <paramref name="b"/> (metres along the axis) holds every plane strictly inside it with <paramref name="h"/> (a difference array).</summary>
    private static void Add(Span<float> s, float a, float b, float h, int planes)
    {
        if (h <= 0)
            return;
        var first = (int)MathF.Floor(a / Slice) + 1;
        var last = (int)MathF.Ceiling(b / Slice) - 1;
        first = Math.Max(first, 0);
        last = Math.Min(last, planes - 2);
        if (first > last)
            return;
        s[first] += h;
        s[last + 1] -= h;
    }

    /// <summary>Server, once a tick: the bodies hurt since they were last looked at, whose turn it is; one snap at most.</summary>
    public static void Step(ConstructWorld world, long tick)
    {
        world.StepBreakBlasts(tick);
        var q = world.BreakQueue;
        if (q.Count == 0)
            return;
        var snapped = false;
        for (var k = q.Count - 1; k >= 0; k--)
        {
            var c = world.Get(q[k]);
            if (c == null || c.BreakBase == null || !Big(c))
            {
                if (c != null)
                    c.BreakDue = false;
                q.RemoveAt(k);
                continue;
            }
            if (snapped || (tick + c.Id) % Every != 0)
                continue;
            c.BreakDue = false;
            q.RemoveAt(k);
            snapped = Look(world, c);
        }
    }

    /// <summary>The weakest plane of <paramref name="c"/> against its base: snaps there if it is past the share.</summary>
    private static bool Look(ConstructWorld world, Construct c)
    {
        var planes = c.BreakBase!.Length;
        using var scratch = Scratch.Begin();
        var s = scratch.Take<float>(planes);
        var centre = scratch.Take<float>(c.PartCount);
        Strength(c, s, centre);
        // The parts either side of each plane (by their centres), so a plane past the body's end (or near it) isn't one.
        var below = scratch.Take<int>(planes);
        below.Clear();
        for (var i = 0; i < c.PartCount; i++)
        {
            var k = Math.Clamp((int)MathF.Floor(centre[i] / Slice) + 1, 0, planes - 1);
            below[k]++;
        }
        var run = 0;
        var best = -1;
        var bestShare = SnapShare;
        for (var k = 0; k < planes; k++)
        {
            run += below[k];
            var b = c.BreakBase[k];
            if (b < MinBase || run < MinSide || c.PartCount - run < MinSide)
                continue;
            var share = s[k] / b;
            if (share < bestShare)
            {
                bestShare = share;
                best = k;
            }
        }
        if (ConstructBores.Trace)
        {
            var low = -1;
            var lowShare = 9f;
            run = 0;
            for (var k = 0; k < planes; k++)
            {
                run += below[k];
                if (c.BreakBase[k] >= MinBase && run >= MinSide && c.PartCount - run >= MinSide && s[k] / c.BreakBase[k] < lowShare)
                {
                    lowShare = s[k] / c.BreakBase[k];
                    low = k;
                }
            }
            Console.WriteLine($"  break-up look at {c.Id}: weakest plane {low} ({low * Slice:0} m) at {lowShare:0.00} of {(low >= 0 ? c.BreakBase[low] : 0):0.0}");
        }
        if (best < 0)
            return false;
        world.SnapAt(c, best * Slice, centre);
        return true;
    }
}

public sealed partial class Construct
{
    // Break-up (server; ConstructBreakUp.cs): the planes' base strength across the long axis, taken at the first hit.
    internal float[]? BreakBase, BreakHurt; // (BreakHurt: the damage each slice has taken, by where it landed)
    internal byte BreakAxis;
    internal float BreakMin;
    internal bool BreakDue;
    internal int BreakParts; // (how many parts it had when its base was taken)
}

public sealed partial class ConstructWorld
{
    internal readonly List<ushort> BreakQueue = new(64);
    internal long BreakTicks;
    private bool _scrapCutOff; // (a part shot off a capital-sized body is going: small pieces it leaves hanging go as scrap)

    /// <summary>Piece <paramref name="k"/> of <paramref name="c"/> is small (<see cref="ConstructBreakUp.ScrapParts"/> parts at most, all light): it bursts into scrap drawn only, and true; else false (it becomes a wreck).</summary>
    private bool ScrapPiece(Construct c, Span<int> comp, int k)
    {
        var n = 0;
        var mass = 0f;
        for (var i = 0; i < c.PartCount; i++)
            if (comp[i] == k)
            {
                n++;
                mass += c.DefOf(i).Mass;
                if (!ConstructBreakUp.Scrappable(c, i) || n > ConstructBreakUp.ScrapParts || mass > ConstructBreakUp.ScrapMass * 2)
                    return false;
            }
        for (var i = 0; i < c.PartCount; i++)
        {
            if (comp[i] != k)
                continue;
            if (c.Parts[i].Store != 0)
                Spill(c, i); // (what it held falls out, as from any broken container)
            c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
            c.PartBounds(i, out var mn, out var mx);
            var at = t + Vector3.Transform((mn + mx) * 0.5f, q);
            AddFire(new CFireEvent { Kind = CFireKind.Scrap, At = at, Half = Vector3.Min((mx - mn) * 0.5f, new Vector3(2.5f)), To = c.PointVel(at) });
        }
        Events.Add($"construct {c.Id}: {n} part(s) left hanging went as scrap");
        return true;
    }
    private readonly List<(long Tick, Vector3 At)> _breakBlasts = new(64);
    private readonly Random _breakRng = new(7);
    private long _breakTick;

    /// <summary>Server: <paramref name="c"/> breaks at <paramref name="along"/> metres from its long axis's start: the far side from its root goes as a wreck.</summary>
    internal void SnapAt(Construct c, float along, Span<float> centre)
    {
        if (c.Dirty)
            c.Rebuild();
        using var scratch = Scratch.Begin();
        Span<int> comp = scratch.Take<int>(c.PartCount);
        var root = c.Root >= 0 && c.Root < c.PartCount ? c.Root : 0;
        var rootBelow = centre[root] < along;
        var away = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            comp[i] = centre[i] < along == rootBelow ? 0 : 1;
            away += comp[i];
        }
        if (away == 0 || away == c.PartCount)
            return;
        // The plane in the world, for the blasts: where the body's parts cross it.
        var axis = c.BreakAxis;
        var mid = Vector3.Zero;
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (MathF.Abs(centre[i] - along) < ConstructBreakUp.Slice)
            {
                mid += ConstructRender.PartCentre(c, i);
                n++;
            }
        var at = n > 0 ? mid / n : c.WorldCom;
        var axisW = Vector3.Transform(axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ, c.Rot);
        var parts = c.PartCount;
        var keepDriver = c.Driver;
        var piece = SplitOff(c, comp, 1);
        Compact(c, comp, 0);
        c.Dirty = true;
        c.Wake();
        StructureDirty.Add(c.Id);
        if (keepDriver != 255 && c.Driver == 255)
            Events.Add($"driver {keepDriver} thrown out");
        // They come apart: a push along the axis, and a slow tumble.
        var apart = (rootBelow ? 1f : -1f) * 0.6f;
        if (piece != null)
        {
            piece.Vel += axisW * apart;
            piece.AngVel += Vector3.Transform(new Vector3(0.02f, 0.035f, -0.015f), c.Rot);
        }
        c.Vel -= axisW * apart * 0.5f;
        ConstructBreakUp.Snaps++;
        c.BreakBase = c.BreakHurt = null; // (what is left starts again from how it is now: the planes by the break would otherwise read as gone)
        Events.Add($"construct {c.Id}: broke up at {along:0} m along its length ({parts} parts: {away} went as wreck {piece?.Id ?? 0})");
        // Secondary blasts along the break over the next second and a half.
        var spread = MathF.Max(4f, MathF.Pow(Math.Max(1, n), 0.5f) * 1.5f);
        for (var k = 0; k < ConstructBreakUp.Blasts && _breakBlasts.Count < _breakBlasts.Capacity; k++)
        {
            var off = new Vector3((float)_breakRng.NextDouble() - 0.5f, (float)_breakRng.NextDouble() - 0.5f, (float)_breakRng.NextDouble() - 0.5f) * spread;
            off -= axisW * Vector3.Dot(off, axisW);
            _breakBlasts.Add((_breakTick + 4 + k * 15 + _breakRng.Next(8), at + off));
        }
    }

    /// <summary>The break's blasts whose tick has come.</summary>
    internal void StepBreakBlasts(long tick)
    {
        _breakTick = tick;
        for (var k = _breakBlasts.Count - 1; k >= 0; k--)
        {
            if (_breakBlasts[k].Tick > tick)
                continue;
            var at = _breakBlasts[k].At;
            _breakBlasts.RemoveAt(k);
            ConstructWeapons.Explode(this, at, ConstructBreakUp.BlastRadius, ConstructBreakUp.BlastDamage, null, CFireKind.CookOff);
        }
    }
}
