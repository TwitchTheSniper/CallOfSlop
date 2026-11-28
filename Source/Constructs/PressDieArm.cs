using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A Loader Arm changes a press's die (Sean, 2026-11-26: "a die rack and a loader arm that swaps out dies on the presses, because
/// otherwise we'll need a press for every type in an area"). An arm that reaches a press's die slot and a die rack (a store whose slots
/// are labelled "Dies": the Die Rack, the Die Wall) works the swap a chute does in <see cref="ConstructPress"/>'s PressDieSwap.cs, by hand:
/// when the head order of the press's queue needs a die it doesn't hold, the arm carries the wanted die from its shelf to the press, changes
/// it for the die there and carries the old one back to a free shelf. A press takes an order for any die on a rack an arm serves it from (<see cref="ArmDieRack"/>, read by
/// <see cref="ConstructPress.DieRackOf"/>), and a press with several orders presses those its fitted die makes before it swaps
/// (<see cref="WorkOrders"/>' queue: <see cref="ConstructPress.DieFirst"/>). Server only; the look runs at the carry look's rate and
/// allocates nothing.
/// </summary>
public static partial class ConstructLifters
{
    /// <summary>A die rack: a store of shelves labelled for dies (an arm's pick and put point for dies only).</summary>
    public static bool IsDieRack(ConstructPartDef d) => !d.IsPieceRack && d.Slots.Length > 0 && d.Slots[0].Rack && d.Slots[0].Label == "Dies";

    /// <summary>A press's die slot as an arm point: a press (not a lathe) with a die socket.</summary>
    private static bool HasDieSlot(ConstructPartDef d) => d.IsPress && !d.IsLathe && d.PressDieSocket >= 0;

    /// <summary>The points on a die rack and a press's die slot a lifter reaches (PointsOn's die part).</summary>
    private static void DiePointsOn(LiftPoint[] pts, ref int n, Construct b, int i, ConstructPartDef d, LifterDef ld, Vector3 shoulder, Quaternion lr)
    {
        if (n >= pts.Length)
            return;
        var pt = new LiftPoint { Body = b.Id, Part = (ushort)i, Kind = IsDieRack(d) ? LiftPointKind.DieRack : LiftPointKind.DieSlot };
        if (PointAt(b, pt, out var at) && InReach(ld, shoulder, lr, at, SafeLift + 0.3f, out _))
            pts[n++] = pt;
    }

    /// <summary>A die point's place: the slot socket of a press, the middle of a rack's first shelf's floor (the hand's way in).</summary>
    private static bool DiePointAt(Construct b, in LiftPoint pt, ConstructPartDef d, out Vector3 at)
    {
        at = default;
        if (pt.Kind == LiftPointKind.DieSlot)
        {
            if (!HasDieSlot(d))
                return false;
            at = PartPoint(b, pt.Part, d.Sockets[d.PressDieSocket].At);
            return true;
        }
        if (!IsDieRack(d))
            return false;
        at = ShelfAt(b, pt.Part, d, 0);
        return true;
    }

    /// <summary>A rack shelf's floor middle (world).</summary>
    private static Vector3 ShelfAt(Construct b, int part, ConstructPartDef d, int slot)
    {
        var s = d.Slots[Math.Clamp(slot, 0, d.Slots.Length - 1)];
        return PartPoint(b, part, new Vector3((s.Min.X + s.Max.X) * 0.5f, s.Min.Y, (s.Min.Z + s.Max.Z) * 0.5f));
    }

    /// <summary>Is this press free for a die change: no stroke under way and its lid open?</summary>
    private static bool PressIdle(ConstructWorld world, Construct b, int press) =>
        world.StoreOf(b, press) is not { } st || st.JobTicks == 0 && st.LidCool == 0;

    /// <summary>Is a hand already carrying a die to or from this press or rack point (one change at a time per press)?</summary>
    private static bool DieBound(ConstructWorld world, CLift self, ushort body, ushort part)
    {
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            for (var i = 0; i < lb.N; i++)
            {
                var o = lb.Lifts[i];
                if (o != self && o.State == LiftState.Carry && o.ToKind is LiftPointKind.DieSlot or LiftPointKind.DieRack
                    && (o.StationBody == body && o.ToPart == part || o.DieFromBody == body && o.DieFromPart == part))
                    return true;
            }
        }
        return false;
    }

    /// <summary>A die rack point in reach holding a die of <paramref name="die"/>'s family for <paramref name="press"/>: its point index and slot, else -1.</summary>
    private static int RackHolding(ConstructWorld world, CLift l, PressDie die, ConstructPartDef press, out int slot)
    {
        slot = -1;
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (pt.Kind != LiftPointKind.DieRack || world.Get(pt.Body) is not { } rb || pt.Part >= rb.PartCount || world.StoreOf(rb, pt.Part) is not { } st)
                continue;
            if ((slot = ConstructPress.DieSlot(st, die, press)) >= 0)
                return k;
        }
        return -1;
    }

    /// <summary>A die rack point in reach with a free shelf for <paramref name="d"/>: its point index and slot, else -1.</summary>
    private static int RackRoom(ConstructWorld world, CLift l, ConstructPartDef d, out int slot)
    {
        slot = -1;
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (pt.Kind != LiftPointKind.DieRack || world.Get(pt.Body) is not { } rb || pt.Part >= rb.PartCount)
                continue;
            if ((slot = ConstructPress.FreeDieSlot(world, rb, pt.Part, d)) >= 0)
                return k;
        }
        return -1;
    }

    /// <summary>
    /// Server: an idle arm's look for a die change (before its other carries: a press waiting on a die stands idle). The wanted die comes off
    /// its shelf into the hand and goes to the press; there, the die it holds is changed for it and the old one goes back in the hand to a
    /// free shelf (<see cref="PutDieSlot"/>). A die never leaves the press's body as a part: taking a part out renumbers a big site's parts,
    /// lifters, stations and chains with it. True when a carry began.
    /// </summary>
    private static bool CarryDie(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        if (l.PointN == 0)
            return false;
        Frame(c, l.Part, ld, out var lp, out var lr, out _);
        for (var k = 0; k < l.PointN; k++)
        {
            var pt = l.Points![k];
            if (pt.Kind != LiftPointKind.DieSlot || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount || b.Parts[pt.Part].Broken)
                continue;
            if (ConstructPress.DieWanted(b, pt.Part) is not { } want || !PressIdle(world, b, pt.Part) || DieBound(world, l, pt.Body, pt.Part))
                continue;
            if (ConstructPress.DieFirst(world, b, pt.Part))
                continue; // (a later order its fitted die makes, its blank in, goes first: no swap yet)
            var pd = b.DefOf(pt.Part);
            var r = RackHolding(world, l, want, pd, out var slot);
            if (r < 0)
                continue;
            var rp = l.Points[r];
            if (world.Get(rp.Body) is not { } rb || world.StoreOf(rb, rp.Part) is not { } rst || ConstructParts.Get(rst.Def[slot]) is not { } dd || !Carries(ld, dd))
                continue;
            if (ConstructPress.DieIn(b, pt.Part) is var old and >= 0 && (!Carries(ld, b.Parts[old].D) || RackRoom(world, l, b.Parts[old].D, out _) < 0 && rst.Count[slot] > 1))
                continue; // (the old die needs a shelf to go back to: the new one's, once emptied, or another)
            if (world.TakeOut(rb, rp.Part, slot) is not { } taken)
                continue;
            Freeform.PartWorld(rb, rp.Part, out _, out var rr);
            var from = ShelfAt(rb, rp.Part, rb.DefOf(rp.Part), slot) + Vector3.UnitY * ((taken.BoundsMax.Y - taken.BoundsMin.Y) * 0.5f + 0.02f);
            PointAt(b, pt, out var into);
            into.Y += (taken.BoundsMax.Y - taken.BoundsMin.Y) * 0.5f + 0.02f;
            StartCarry(world, c, l, ld, lp, lr, taken, 255, 0, from, rr, k, into);
            (l.DieFromBody, l.DieFromPart) = (rp.Body, rp.Part);
            Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: carries a {taken.Name} from the {rb.DefOf(rp.Part).Name} to the {pd.Name} (its order needs it)");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Server: a die carry's end at a press's die slot. An empty slot takes the die; a slot holding another die (a swap) has it changed for
    /// the one in the hand (the part stays where it is, its kind changes), and the old one goes in the hand to a free shelf in reach. False:
    /// the press doesn't take it now (the caller's refusal path finds the hand another taker).
    /// </summary>
    private static bool PutDieSlot(ConstructWorld world, Construct c, CLift l, LifterDef ld)
    {
        if (ConstructParts.Get(l.Def) is not { IsPressDie: true } d || world.Get(l.StationBody) is not { } b || l.ToPart >= b.PartCount)
            return false;
        var press = l.ToPart;
        var pd = b.DefOf(press);
        if (!HasDieSlot(pd) || ConstructPress.DieWanted(b, press) is not { } want || !ConstructPress.IsDieFor(d, want, pd) || !PressIdle(world, b, press))
            return false;
        var old = ConstructPress.DieIn(b, press);
        if (old < 0)
        {
            if (!ConstructPress.FitDie(world, b, press, d))
                return false;
            Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: fitted a {d.Name} into the {pd.Name}");
            DiePutDone(world, c, l, ld, b);
            return true;
        }
        var oldDef = b.Parts[old].D;
        if (l.Points == null || world.LiftTick - l.PointsAt >= PointEvery)
            FindPoints(world, c, l, ld);
        Frame(c, l.Part, ld, out var lp, out var lr, out var shoulder);
        var to = DiePutFor(world, l, ld, shoulder, lr, oldDef, -1, out var toAt, racksOnly: true);
        if (to < 0)
            return false;
        Freeform.PartWorld(b, old, out var dp, out var dr);
        var from = dp + Vector3.Transform((oldDef.BoundsMin + oldDef.BoundsMax) * 0.5f, dr);
        var health = (byte)Math.Clamp(255 * b.Parts[old].Health / MathF.Max(1, oldDef.Health), 1, 255);
        b.Parts[old].Def = d.Id; // (the die changed in place: the same slot, the same part)
        b.Parts[old].Health = d.Health;
        b.Parts[old].Paint = 0;
        b.Dirty = true;
        world.StructureDirty.Add(b.Id);
        if (world.StoreOf(b, press) is { } st)
            ConstructPress.ClearRefusal(world, st);
        b.Wake();
        world.CraftChanged(b);
        Console.WriteLine($"[lifter] body {l.Body} part {l.Part}: changed the {pd.Name}'s {oldDef.Name} for a {d.Name}; takes the {oldDef.Name} to a shelf");
        l.Moves++;
        ForgetRefused(l);
        StartCarry(world, c, l, ld, lp, lr, oldDef, health, 0, from, dr, to, toAt);
        (l.DieFromBody, l.DieFromPart) = (b.Id, (ushort)press);
        return true;
    }

    /// <summary>A die put went in: the hand goes back empty.</summary>
    private static void DiePutDone(ConstructWorld world, Construct c, CLift l, LifterDef ld, Construct b)
    {
        ForgetRefused(l);
        l.Moves++;
        l.Fitted = true;
        l.Why = LiftWhy.Carrying;
        Back(c, l, ld, null, -1);
        l.ToKind = LiftPointKind.None;
        (l.DieFromBody, l.DieFromPart) = (0, 0);
        world.CraftChanged(b);
    }

    /// <summary>Where a die in the hand goes: a press's die slot whose head order wants its family, else a rack shelf with room. -1: none.</summary>
    private static int DiePutFor(ConstructWorld world, CLift l, LifterDef ld, Vector3 shoulder, Quaternion lr, ConstructPartDef d, int skip, out Vector3 at, bool racksOnly = false)
    {
        at = default;
        for (var k = 0; k < l.PointN && !racksOnly; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || pt.Kind != LiftPointKind.DieSlot || RefusedNow(world, l, pt) || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount)
                continue;
            if (ConstructPress.DieWanted(b, pt.Part) is not { } want || !ConstructPress.IsDieFor(d, want, b.DefOf(pt.Part)) || ConstructPress.DieIn(b, pt.Part) >= 0
                || !PointAt(b, pt, out var p))
                continue;
            at = p + Vector3.UnitY * ((d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.02f);
            return k;
        }
        for (var k = 0; k < l.PointN; k++)
        {
            ref readonly var pt = ref l.Points![k];
            if (k == skip || pt.Kind != LiftPointKind.DieRack || RefusedNow(world, l, pt) || world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount
                || ConstructPress.FreeDieSlot(world, b, pt.Part, d) is not (>= 0 and var slot))
                continue;
            at = ShelfAt(b, pt.Part, b.DefOf(pt.Part), slot) + Vector3.UnitY * ((d.BoundsMax.Y - d.BoundsMin.Y) * 0.5f + 0.02f);
            if (!InReach(ld, shoulder, lr, at, WristLift(d), out _))
                continue;
            return k;
        }
        return -1;
    }

    /// <summary>A die put: into a press's slot (when it still has none and its order wants it), or onto a rack's free shelf.</summary>
    private static bool PutDie(ConstructWorld world, Construct b, LiftPointKind kind, int part, ConstructPartDef d)
    {
        if (!d.IsPressDie)
            return false;
        if (kind == LiftPointKind.DieSlot)
        {
            var pd = b.DefOf(part);
            if (!HasDieSlot(pd) || ConstructPress.DieIn(b, part) >= 0 || !ConstructPress.IsDieFor(d, ConstructPress.DieWanted(b, part) ?? PressDieOf(d), pd))
                return false;
            return ConstructPress.FitDie(world, b, part, d);
        }
        return ConstructPress.FreeDieSlot(world, b, part, d) is >= 0 and var slot && world.SetIn(b, part, slot, d) == null;
    }

    /// <summary>The table's die of a die part's family (a press with no order takes any die it fits).</summary>
    private static PressDie PressDieOf(ConstructPartDef d)
    {
        foreach (var die in PressTable.Current.Dies)
            if (die.Family == d.DieFamily)
                return die;
        return PressTable.Current.Dies[0];
    }

    /// <summary>
    /// Server: every Die Wall on a body gets one die of each family it has no die of yet, in <see cref="StockOrder"/>, on its free shelves:
    /// a creative site start's stock (GameSiteStart.cs), as its bins start full. How many it set.
    /// </summary>
    public static int StockDieWalls(ConstructWorld world, Construct c)
    {
        var set = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            var d = c.DefOf(i);
            if (!IsDieRack(d) || d.Slots.Length < 2 || world.StoreOf(c, i) is not { } st)
                continue;
            foreach (var family in StockOrder)
            {
                if (ConstructParts.Find($"{family} Die") is not { IsPressDie: true } dd || HasDie(world, c, i, dd))
                    continue;
                if (ConstructPress.FreeDieSlot(world, c, i, dd) is >= 0 and var slot && world.SetIn(c, i, slot, dd) == null)
                    set++;
            }
        }
        return set;
    }

    /// <summary>
    /// The die families a stocked wall takes, first what the Zero Day frigate's fittings and their Assembler parts are pressed on (Gear
    /// Sets, Circuit Boards, Bearings (the Ring, phase 2 of retiring the Assembler), Looms and Coils, switches and gates, housings, brackets,
    /// plates, hinges, carriages: its bill), then the rest.
    /// </summary>
    private static readonly string[] StockOrder = ["Gear", "Board", "Ring", "Cable", "Button", "Control", "Housing", "Bracket", "Plate", "Actuator", "Beam",
        "Shaft", "Spring", "Drive", "Arm", "Engine", "Piston", "Cylinder", "Tank", "Barrel", "Receiver", "Bell", "Shell"];

    /// <summary>Is a die of this kind on wall <paramref name="wall"/>'s shelves? (A family fitted in one press of the pair gets a shelf too: the other press may want it while the first holds it.)</summary>
    private static bool HasDie(ConstructWorld world, Construct c, int wall, ConstructPartDef die)
    {
        if (world.StoreOf(c, wall) is { } st)
            for (var k = 0; k < st.SlotCount; k++)
                if (st.Count[k] > 0 && st.Def[k] == die.Id)
                    return true;
        return false;
    }

    /// <summary>
    /// For order checks (not a tick): a die rack holding a die of <paramref name="die"/>'s family that a lifter reaching this press's die
    /// slot also reaches. Its body and part; false when none does.
    /// </summary>
    public static bool ArmDieRack(ConstructWorld world, Construct c, int press, PressDie die, out Construct? rackBody, out int rack)
    {
        (rackBody, rack) = (null, -1);
        var pd = c.DefOf(press);
        if (world.LiftBodies.Count == 0)
            Scan(world, server: true); // (an order before the lifters' first step: a new world, a check)
        for (var bi = 0; bi < world.LiftBodies.Count; bi++)
        {
            var lb = world.LiftBodies[bi];
            if (world.Get(lb.Body) is not { } lc)
                continue;
            for (var i = 0; i < lb.N; i++)
            {
                var l = lb.Lifts[i];
                if (l.Points == null && l.Part < lc.PartCount && DefOf(lc, l.Part) is { } ld)
                    FindPoints(world, lc, l, ld); // (its first look: the points it will find anyway)
                if (l.Points == null)
                    continue;
                var serves = false;
                for (var k = 0; k < l.PointN && !serves; k++)
                    serves = l.Points[k].Kind == LiftPointKind.DieSlot && l.Points[k].Body == c.Id && l.Points[k].Part == press;
                if (!serves)
                    continue;
                for (var k = 0; k < l.PointN; k++)
                {
                    ref readonly var pt = ref l.Points[k];
                    if (world.Get(pt.Body) is not { } b || pt.Part >= b.PartCount)
                        continue;
                    if (pt.Kind == LiftPointKind.DieRack && world.StoreOf(b, pt.Part) is { } st && ConstructPress.DieSlot(st, die, pd) >= 0)
                    {
                        (rackBody, rack) = (b, pt.Part);
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
