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
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
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
        LifterProbe.Cost(ref LifterProbe.TakeIn, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
    }

    public const string LoadedWhy = "holds something: empty it first (a pad keeps kinds and counts, not what is in them)";
    private static ushort _loadedSaid;

    /// <summary>Why pad <paramref name="part"/> doesn't take body <paramref name="lo"/> in now (a fixed text), or null when it does. Allocates nothing.</summary>
    public static string? TakeInWhy(ConstructWorld world, Construct c, int part, CStore st, Vector3 pp, Quaternion inv, float reach, Construct lo)
    {
        var d = c.DefOf(part);
        // The hands' rule for a loose part (Construct.Loose: one whole part, not a seat, a foundation or a core, not anchored), and
        // nobody has it: not held by a station, grabbed, carried, nudged (the Nudge tool placing it), frozen by the physics gun or driven.
        if (!lo.Loose || lo == c)
            return "not loose";
        if (lo.HeldBy != 0 || lo.GrabbedBy != 255 || lo.CarriedBy != 0 || lo.NudgeBy != 255 || lo.Frozen || lo.Driver != 255)
            return "someone has it";
        if (Vector3.DistanceSquared(lo.Pos, c.Pos) > reach * reach || lo.Vel.LengthSquared() > 0.25f)
            return "far or moving";
        var local = Vector3.Transform(lo.WorldCom - pp, inv);
        if (local.X < d.RackHoldMin.X || local.X > d.RackHoldMax.X || local.Z < d.RackHoldMin.Z || local.Z > d.RackHoldMax.Z
            || local.Y < d.RackHoldMin.Y - 0.25f || local.Y > d.RackHoldMax.Y)
            return "not over the hold";
        var pd = lo.DefOf(0);
        // Not a part that holds anything (its contents would be lost: a rack holds kinds only), nor one that isn't as new (a rack
        // keeps a kind and a count, so it would come out whole, unpainted, empty and at its default settings), nor what the pad or
        // its stock target refuses.
        if (pd.IsPieceRack || pd.Lifter != null)
            return "a rack or a lifter";
        if (lo.Parts[0].Store != 0 && world.StoreOf(lo, 0) is { } held && (held.Amount > 0 || held.ItemCount > 0 || held.InAmount > 0))
            return LoadedWhy; // (lifters phase 7: an empty store part goes in as the station's hold takes one; a loaded one would lose what it holds)
        if (!AsNew(lo.Parts[0], pd))
            return "not as new";
        if (!OnDeck(lo, pd, pp, inv, d.RackHoldMin.Y))
            return "not on the deck"; // (standing beside the pad on the ground, as a placed machine does: not set down on it)
        if (ConstructConveyor.RackCheck(c, part, st, pd) is var rc and not 0)
            return rc == 3 ? "too big for the pad" : rc == 4 ? "pad full" : "the pad refuses it";
        return StockTargets.Accepts(world, c, part, st, pd) ? null : "its stock target refuses it";
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
            if (TakeInWhy(world, c, part, st, pp, inv, reach, lo) is { } why)
            {
                if (ReferenceEquals(why, LoadedWhy) && _loadedSaid != lo.Id)
                {
                    _loadedSaid = lo.Id; // (said once a body: the look runs every second)
                    Console.WriteLine($"[lifter] pad body {c.Id} part {part}: the loose {lo.DefOf(0).Name} on it {why}");
                }
                continue;
            }
            var pd = lo.DefOf(0);
            world.Remove(lo);
            if (!world.RackPut(c, part, pd))
                continue; // (checked above: not reached)
            if (world.StockTargetN > 0)
                ConstructLogisticsPlan.Arrived(world, c, part, 0, pd); // (a stock target's refill counts it, as a conveyor's delivery does)
            PadArrived(world, c, part, 0, pd); // (phase 5b: a station's large part set down on its pick pad counts for its order)
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
        if (sc.Parts[t].Station is not { } j)
            return 0;
        if (world.PadScanTick != world.LiftTick)
        {
            world.PadScanTick = world.LiftTick;
            Scan(world, server: true); // (bodies whose parts changed only: the stations step before the lifters, and a loaded world's first orders would see no arm and no pad)
        }
        if (world.PadBodies.Count == 0 || world.LiftBodies.Count == 0)
            return 0;
        var kind = Array.IndexOf(j.KindDef, def, 0, j.KindN);
        if (kind < 0)
            return 0;
        if (!ReferenceEquals(_padJob, j) || _padTick != world.LiftTick)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            PadCounts(world, sc, t, j);
            LifterProbe.Cost(ref LifterProbe.PadCount, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
        }
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
                if ((c ??= world.Get(lb.Body)) == null || l.Part >= c.PartCount || ConstructLifters.DefOf(c, l.Part) is not { } ld)
                    continue;
                if (l.Points == null)
                    FindPoints(world, c, l, ld); // (just loaded or placed: its points now, or the station's first orders miss its pads' pieces)
                if (l.PointN == 0)
                    continue;
                var mode = (int)ConstructConfig.Get(c.Parts[l.Part], c.DefOf(l.Part), CfgKey.LiftMode, 0);
                if (mode is 2 or 3)
                    continue; // (Carry only, Off: it fits nothing)
                if (!CanWorkNow(world, c, l))
                    continue; // (no power, broken, its Run held off or stuck: its pads' pieces are ordered as if it weren't there)
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
                    if (Array.IndexOf(world.PadSeen, key, 0, seen) >= 0)
                        continue;
                    if (seen < world.PadSeen.Length)
                        world.PadSeen[seen++] = key;
                    for (var q = 0; q < j.KindN; q++)
                        _padHeld[q] += ConstructCrafting.Holds(st, j.KindDef[q]);
                }
                if (any && serving < MaxServing)
                    (ServeDef[serving], ServeShoulder[serving], ServeRot[serving], serving) = (ld, shoulder, lr, serving + 1);
            }
        }
        world.PadSeenN = seen;
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

    /// <summary>Can this arm fit from its pick pads now: powered, whole, its Run not held off, not stuck with a piece?</summary>
    private static bool CanWorkNow(ConstructWorld world, Construct c, CLift l) =>
        !c.Parts[l.Part].Broken && ConstructRecycler.Powered(world, c, l.Part) && !ConstructExtruder.RunHeld(c, l.Part) && (l.State != LiftState.Stuck || l.Why == LiftWhy.Holding); // (a hand loaded from a save holds its piece only until its first look: phase 5b)

    /// <summary>
    /// Is the part as a new one is: whole, no rounds (a new ammo box or gun is empty), no paint, no text, its settings at their
    /// defaults, no fluid, no queue. Only such a part goes into a pad's hold, which keeps kinds and counts and nothing else.
    /// </summary>
    private static bool AsNew(in CPart p, ConstructPartDef d) =>
        p.Health >= d.Health * 0.999f && p.Rounds == 0 && p.Paint == 0 && p.Text == null && p.Text2 == null && p.Orders == null && p.Craft == null
        && ConstructConfig.AllDefault(p, d) && (d.Role != PartRole.FluidTank || ConstructFluid.Held(p, d) == null); // (Held reads a tank's own settings: only a tank has them)

    /// <summary>Does the piece rest on the pad: its lowest corner from 15 cm under the hold's floor to 50 cm over it (on its rails or on another piece; never below: on the ground beside)?</summary>
    private static bool OnDeck(Construct lo, ConstructPartDef pd, Vector3 padAt, Quaternion padInv, float floor)
    {
        Freeform.PartWorld(lo, 0, out var at, out var rot);
        // (each solid box's corners, not the bounds': an odd part lying on its side, a Drill Module on its bit, had a bounds corner
        // through the deck and was never taken in: lifters phase 7)
        var low = float.MaxValue;
        var n = Math.Min(pd.SolidBoxes, 64);
        for (var b = 0; b < n; b++)
        {
            var bx = pd.Boxes[b];
            for (var k = 0; k < 8; k++)
            {
                var corner = bx.Center + new Vector3((k & 1) != 0 ? bx.Half.X : -bx.Half.X, (k & 2) != 0 ? bx.Half.Y : -bx.Half.Y, (k & 4) != 0 ? bx.Half.Z : -bx.Half.Z);
                low = MathF.Min(low, Vector3.Transform(at + Vector3.Transform(corner, rot) - padAt, padInv).Y);
            }
        }
        return low > floor - 0.15f && low < floor + 0.5f;
    }
}

public sealed partial class ConstructWorld
{
    public readonly List<ushort> PadBodies = new(8); // server: the bodies that have a Lay-down Pad (ConstructLifterPads.cs)
    public readonly uint[] PadSeen = new uint[64]; // server: the last pick pad count's pads (body << 16 | part), for the build's claims (ConstructLifterNet.cs)
    public int PadSeenN, PadScanTick = int.MinValue; // ...how many; the tick a pick pad count last ran the lifter scan
}

public static partial class ConstructLifters
{
    /// <summary>
    /// Why a lifter may not pick up loose body <paramref name="lo"/> (a fixed text), or null: the one rule for every way a lifter picks
    /// something up (a carry from a machine's out, a hand order, a cab's F). A lifter's hand keeps a kind, its health and its paint, so
    /// a piece must be a loose single part (<see cref="Construct.Loose"/>) that holds nothing that would be lost: no store contents, no
    /// rounds, no fluid, no text, no queue, its settings at their defaults. Its health and paint go with it, and only a piece as new
    /// becomes a count in a rack, a pad or a tray (<see cref="AsNewInHand"/>).
    /// </summary>
    public static string? PickWhy(ConstructWorld world, Construct lo)
    {
        if (!lo.Loose)
            return lo.PartCount != 1 ? "only a single piece: that is a construct" : "it is fixed in place";
        ref readonly var p = ref lo.Parts[0];
        var d = lo.DefOf(0);
        if (d.IsPieceRack || d.Lifter != null)
            return "a rack or a lifter is never carried";
        if (p.Store != 0 && world.StoreOf(lo, 0) is { } st && (st.Amount > 0 || st.ItemCount > 0 || st.InAmount > 0 || st.KindN > 0))
            return LoadedWhy;
        if (p.Rounds != 0 || p.Text != null || p.Text2 != null || p.Orders != null || p.Craft != null || !ConstructConfig.AllDefault(p, d)
            || d.Role == PartRole.FluidTank && ConstructFluid.Held(p, d) != null)
            return "it holds rounds, fluid, text or settings: empty or reset it first";
        return null;
    }

    /// <summary>Is the piece in the hand as new (whole, unpainted)? Only then does a rack, a pad or a tray take it as a count.</summary>
    public static bool AsNewInHand(CLift l) => l.Health >= 255 && l.Paint == 0;
}
