using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Lifters phase 5 (docs/sandbox/design/arms-and-cranes.md 3.5): a Lay-down Pad takes in a loose piece set down on its deck. A
/// one-part body at rest whose middle is over the pad's hold (put there by hand, the physics gun, or dropped there by a machine's
/// out) goes into the pad's hold as a conveyor's delivery would, about once a second. Server only. The world keeps only the bodies
/// that have a pad (<see cref="ConstructWorld.PadBodies"/>, noted when a body's parts change), never a record per body.
/// </summary>
public static partial class ConstructLifters
{
    public const int PadTakesAStep = 4; // pieces the world's pads take in one look

    /// <summary>A body whose parts changed: on the pads list while it has a Lay-down Pad (server).</summary>
    private static void NotePads(ConstructWorld world, Construct c)
    {
        c.PadStamp = c.RebuildStamp;
        c.PadPartCount = c.PartCount;
        var has = false;
        for (var i = 0; i < c.PartCount && !has; i++)
            has = c.DefOf(i).IsLayDownPad;
        var at = world.PadBodies.IndexOf(c.Id);
        if (has && at < 0)
            world.PadBodies.Add(c.Id);
        else if (!has && at >= 0)
            world.PadBodies.RemoveAt(at);
    }

    /// <summary>Server, once a second after the scan: each pad takes in the loose pieces resting on it. Allocates nothing.</summary>
    private static void PadsTakeIn(ConstructWorld world)
    {
        var taken = 0;
        for (var k = world.PadBodies.Count - 1; k >= 0 && taken < PadTakesAStep; k--)
        {
            if (world.Get(world.PadBodies[k]) is not { } c)
            {
                world.PadBodies.RemoveAt(k);
                continue;
            }
            for (var i = 0; i < c.PartCount && taken < PadTakesAStep; i++)
                if (c.DefOf(i).IsLayDownPad && !c.Parts[i].Broken)
                    taken += PadTakeIn(world, c, i, PadTakesAStep - taken);
        }
    }

    /// <summary>One pad: up to <paramref name="most"/> loose pieces over its hold go in. How many did.</summary>
    private static int PadTakeIn(ConstructWorld world, Construct c, int part, int most)
    {
        if (world.StoreOf(c, part) is not { } st)
            return 0;
        var d = c.DefOf(part);
        Freeform.PartWorld(c, part, out var pp, out var pr);
        var inv = Quaternion.Conjugate(pr);
        var reach = c.Radius + Vector3.Distance(c.Pos, pp) + 2f; // (a cheap first cut: the pad's body's sphere)
        var n = 0;
        var bodies = world.Bodies;
        for (var bi = bodies.Count - 1; bi >= 0 && n < most; bi--)
        {
            var lo = bodies[bi];
            if (lo.PartCount != 1 || lo == c || lo.Anchored || lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.Parts[0].Broken)
                continue;
            if (Vector3.DistanceSquared(lo.Pos, c.Pos) > reach * reach || lo.Vel.LengthSquared() > 0.25f)
                continue;
            var local = Vector3.Transform(lo.WorldCom - pp, inv);
            if (local.X < d.RackHoldMin.X || local.X > d.RackHoldMax.X || local.Z < d.RackHoldMin.Z || local.Z > d.RackHoldMax.Z
                || local.Y < d.RackHoldMin.Y - 0.25f || local.Y > d.RackHoldMax.Y)
                continue;
            var pd = lo.DefOf(0);
            // Not a part that holds anything (its contents would be lost: a rack holds kinds only), nor a damaged one (it would come
            // out whole), nor what the pad or its stock target refuses.
            if (lo.Parts[0].Store != 0 || pd.IsPieceRack || pd.Lifter != null || lo.Parts[0].Health < pd.Health * 0.999f)
                continue;
            if (!OnDeck(lo, pd, pp, inv, d.RackHoldMin.Y))
                continue; // (standing beside the pad on the ground, as a placed machine does: not set down on it)
            if (ConstructConveyor.RackCheck(c, part, st, pd) != 0 || !StockTargets.Accepts(world, c, part, st, pd))
                continue;
            world.Remove(lo);
            if (!world.RackPut(c, part, pd))
                continue; // (checked above: not reached)
            if (world.StockTargetN > 0)
                ConstructLogisticsPlan.Arrived(world, c, part, 0, pd); // (a stock target's refill counts it, as a conveyor's delivery does)
            world.CraftChanged(c);
            Console.WriteLine($"[lifter] pad body {c.Id} part {part}: took in a loose {pd.Name}");
            n++;
        }
        return n;
    }

    // ---- Pick pads (the user, 2026-11-13: "pick pads for larger building areas") --------------------------------------------
    // A Lay-down Pad among an arm's points is a pick pad for the stations the arm serves: a step whose part the pad holds is
    // claimed from the pad (the hold first when the arm reaches it and it has the part), and the station counts what its arms'
    // pick pads hold as held, so it doesn't order it again.

    private static bool IsPickPad(ConstructWorld world, in LiftPoint pt, out Construct b) =>
        (b = world.Get(pt.Body)!) != null && pt.Kind == LiftPointKind.Rack && pt.Part < b.PartCount && b.DefOf(pt.Part).IsLayDownPad && !b.Parts[pt.Part].Broken;

    /// <summary>Has this arm a pick pad among its points?</summary>
    private static bool HasPickPad(ConstructWorld world, CLift l)
    {
        for (var k = 0; k < l.PointN; k++)
            if (IsPickPad(world, l.Points![k], out _))
                return true;
        return false;
    }

    /// <summary>The point of this arm's first pick pad that holds a part of this kind, or -1.</summary>
    public static int PickPadHolding(ConstructWorld world, CLift l, ushort def)
    {
        for (var k = 0; k < l.PointN; k++)
            if (IsPickPad(world, l.Points![k], out var b) && world.StoreOf(b, l.Points[k].Part) is { } st && ConstructCrafting.Holds(st, def) > 0)
                return k;
        return -1;
    }

    /// <summary>Server: a station step's part taken off a pick pad: the piece's middle and turn where the hand takes it. False: it can't.</summary>
    private static bool TakeFromPad(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, int pad, ConstructPartDef d, out Vector3 at, out Quaternion rot)
    {
        at = default;
        rot = Quaternion.Identity;
        var pt = l.Points![pad];
        if (!IsPickPad(world, pt, out var b) || world.StoreOf(b, pt.Part) is not { } st || !RackSpot(b, pt.Part, ld, shoulder, lr, SafeLift, WristLift(d), out at))
            return false;
        if (!world.StationTake(b, pt.Part, st, d))
            return false;
        world.CraftChanged(b);
        at.Y += (d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.05f;
        Freeform.PartWorld(b, pt.Part, out _, out rot);
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: took a {d.Name} off the pick pad (body {b.Id} part {pt.Part}) for a station step");
        return true;
    }

    private const int MaxServing = 8, ReachLooks = 512;
    private static readonly uint[] PadSeenScratch = new uint[64];
    private static readonly LifterDef[] ServeDef = new LifterDef[MaxServing];
    private static readonly Vector3[] ServeShoulder = new Vector3[MaxServing];
    private static readonly Quaternion[] ServeRot = new Quaternion[MaxServing];
    private static int[] _padHeld = new int[16], _padReach = new int[16];
    private static StationJob? _padJob;
    private static int _padTick = int.MinValue;

    /// <summary>
    /// Server, for a station's orders: pieces of a kind on the pick pads of the arms that serve this station (within reach of its bay,
    /// Works Auto or Station only), each pad counted once, and never more than the unfitted steps of that kind that one of those arms
    /// reaches (a pad piece whose step no arm reaches is ordered as before, so the build never waits on it). Worked out once a look
    /// for all kinds (the station asks kind by kind in one call); allocates only when a job has more kinds than any before.
    /// </summary>
    public static int PickPadHolds(ConstructWorld world, Construct sc, int t, ushort def)
    {
        if (world.PadBodies.Count == 0 || world.LiftBodies.Count == 0 || sc.Parts[t].Station is not { } j)
            return 0;
        var kind = Array.IndexOf(j.KindDef, def, 0, j.KindN);
        if (kind < 0)
            return 0;
        if (!ReferenceEquals(_padJob, j) || _padTick != world.LiftTick)
            PadCounts(world, sc, t, j);
        return Math.Min(_padHeld[kind], _padReach[kind]);
    }

    private static void PadCounts(ConstructWorld world, Construct sc, int t, StationJob j)
    {
        (_padJob, _padTick) = (j, world.LiftTick);
        if (_padHeld.Length < j.KindN)
            (_padHeld, _padReach) = (new int[j.KindN * 2], new int[j.KindN * 2]);
        Array.Clear(_padHeld, 0, j.KindN);
        Array.Clear(_padReach, 0, j.KindN);
        ConstructStation.Bay(sc, t, out var floor, out _, out var bay);
        var seen = 0;
        var serving = 0;
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            Construct? c = null;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.PointN == 0 || (c ??= world.Get(lb.Body)) == null || l.Part >= c.PartCount || c.DefOf(l.Part).Lifter is not { } ld)
                    continue;
                var mode = (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0);
                if (mode is 2 or 3)
                    continue; // (Carry only, Off: it fits nothing)
                Frame(c, l.Part, ld, out _, out var lr, out var shoulder);
                if (Vector3.Distance(floor, shoulder) > ld.ReachMax + bay.Length() * 0.5f + ld.Down)
                    continue;
                var any = false;
                for (var k = 0; k < l.PointN; k++)
                {
                    var pt = l.Points![k];
                    if (!IsPickPad(world, pt, out var b) || world.StoreOf(b, pt.Part) is not { } st)
                        continue;
                    any = true;
                    var key = (uint)pt.Body << 16 | pt.Part;
                    if (Array.IndexOf(PadSeenScratch, key, 0, seen) >= 0)
                        continue;
                    if (seen < PadSeenScratch.Length)
                        PadSeenScratch[seen++] = key;
                    for (var q = 0; q < j.KindN; q++)
                        _padHeld[q] += ConstructCrafting.Holds(st, j.KindDef[q]);
                }
                if (any && serving < MaxServing)
                    (ServeDef[serving], ServeShoulder[serving], ServeRot[serving], serving) = (ld, shoulder, lr, serving + 1);
            }
        }
        if (serving == 0 || j.Plan is not { } plan || plan.At.Length != j.PartOf.Length)
            return;
        var looked = 0;
        for (var k = j.Cursor; k < j.PartOf.Length && looked < ReachLooks; k++)
        {
            if (j.PartOf[k] != -1 || j.StepKind[k] < 0)
                continue;
            looked++;
            var q = j.StepKind[k];
            if (_padHeld[q] == 0 || _padReach[q] >= _padHeld[q] || ConstructParts.Get(j.KindDef[q]) is not { } d)
                continue;
            var place = j.Pos + Vector3.Transform(plan.At[k], j.Rot);
            for (var a = 0; a < serving; a++)
                if (Carries(ServeDef[a], d) && InReach(ServeDef[a], ServeShoulder[a], ServeRot[a], place, WristLift(d), out _))
                {
                    _padReach[q]++;
                    break;
                }
        }
    }

    /// <summary>Does the piece rest on the pad's deck: its lowest corner within 15 cm of the hold's floor (not below it: on the ground beside)?</summary>
    private static bool OnDeck(Construct lo, ConstructPartDef pd, Vector3 padAt, Quaternion padInv, float floor)
    {
        Freeform.PartWorld(lo, 0, out var at, out var rot);
        var low = float.MaxValue;
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) != 0 ? pd.BoundsMax.X : pd.BoundsMin.X, (k & 2) != 0 ? pd.BoundsMax.Y : pd.BoundsMin.Y, (k & 4) != 0 ? pd.BoundsMax.Z : pd.BoundsMin.Z);
            low = MathF.Min(low, Vector3.Transform(at + Vector3.Transform(corner, rot) - padAt, padInv).Y);
        }
        return low > floor - 0.15f && low < floor + 0.15f;
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<ushort> PadBodies = new(8); // server: the bodies that have a Lay-down Pad (ConstructLifterPads.cs)
}
