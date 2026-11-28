using System.Numerics;

namespace CallOfSlop;

public sealed partial class ConstructWorld
{
    /// <summary>Server: each Bake Oven's kit on a lifter's hook or just set on its bed, by the oven ((body &lt;&lt; 16) | part): the kit's body. Not saved.</summary>
    internal readonly Dictionary<int, ushort> OvenKits = new(4);
    internal int OvenLooks; // oven steps so far (a kit look a second an oven)

    /// <summary>
    /// Either peer: does an oven on a construct take a rule kit's part ((body &lt;&lt; 16) | part: BakeKits.OvenServes), with the body's
    /// RebuildStamp and part count and the SealTick window it was worked out at. Per world (a world swap brings its own), not saved.
    /// </summary>
    internal readonly Dictionary<int, (int Stamp, int Parts, int Look, bool Takes)> OvenServed = new(64);
    internal readonly HashSet<int> OvenServeTold = new(64); // (what the log said, once a construct and part and way)
    internal int OvenServeTick = int.MinValue, OvenServeLeft; // (the work-outs left in this SealTick: BakeKits.ServeBudget)

    /// <summary>Server: does an oven build a part's kit split ((body, oven, part): ConstructBakeOven.PrintFor), with the oven look and body stamp it was worked out at. Not saved.</summary>
    internal readonly Dictionary<long, (int Look, int Stamp, int Station, bool Split)> OvenSplit = new(16);
    internal readonly HashSet<long> OvenSplitTold = new(16);
    internal int OvenSplitLook = int.MinValue, OvenSplitLeft; // (PrintFor's work-outs left this oven look: SplitBudget)
}

/// <summary>
/// A Bake Oven's orders (Sean, 2026-11-23: "large deck parts should be made through the extrusion chain in the factory, put together, and
/// then baked in the oven, not made in an assembler"). An oven takes an order for a part made from a kit (BakeKits.cs: the Zero Day's
/// decks). For its head order, with its bed empty and no kit of it on the way, it starts the kit's blueprint (<c>{part} kit</c>) at an
/// idle, powered Building Station on its own construct, the one whose bay is nearest its bed; the station orders the kit's extrusions
/// as for any build. When the kit is done, a Loader Arm or crane reaching both carries it from the bay onto the bed's middle
/// (ConstructLifters.CarryBody) and sets it down; the oven bakes it into the part (BakedParts.Bake knows a kit), the order counts one
/// made, and a lifter takes the part off the bed (the bed is the oven's Out point) to the station that wants it, or to storage.
/// Server, once a second an oven. Docs: production.md "Bake kits".
/// </summary>
public static partial class ConstructBakeOven
{
    private const int KitEvery = 10; // oven steps (1 s)

    /// <summary>The refusal a quiet <see cref="OrderRefusal"/> gives (say: false): no text made.</summary>
    internal const string No = "no";

    /// <summary>Server: will this oven take the order? Null, or why not (<paramref name="say"/> false: <see cref="No"/>, its own checks making no text).</summary>
    public static string? OrderRefusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, in WorkOrder o, bool say = true)
    {
        if (o.IsPiece || o.Name != null && !o.IsSub && !o.IsMade || BakeKits.OvenDef(o) is not { } made)
            return !say ? No : $"the {d.Name} bakes parts made from kits: {ConstructCrafting.NameOf(o)} isn't one";
        if (!BakeKits.Bakes(made) || BakeKits.PrintOfOrder(made) == null)
            return !say ? No : $"the {made.Name} isn't made from a kit: an oven bakes only what Constructs/BakeKits.json and Constructs/BakeKits/ list";
        if (Unbaking(c.Parts[part], d))
            return !say ? No : $"the {d.Name} is set to Un-bake";
        if (o.Count is < 1 or > WorkOrders.MaxCount)
            return !say ? No : $"an order is for 1 to {WorkOrders.MaxCount} parts";
        if (c.Parts[part].Orders is { Count: >= WorkOrders.MaxOrders })
            return !say ? No : $"the {d.Name}'s queue is full ({WorkOrders.MaxOrders} orders)";
        if (!MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0)
            return !say ? No : $"the {d.Name} has no bed: weld Oven Bed Blocks beside it";
        if (BakeKits.MinBedCells(made) is var least and > 0 && asm.W * asm.L < least)
            return !say ? No : $"the {made.Name} bakes on a bed of {least} cells or more (Constructs/BakeKits.json \"bigBed\"): this one has {asm.W * asm.L}";
        if ((BakeKits.ByRule(made) || o.IsSub) && !BedFits(c, asm, BakeKits.BakeSize(made)))
            return !say ? No : $"the {made.Name} kit ({BedLacks(c, asm, BakeKits.BakeSize(made))}) fits no way in this oven's tunnel";
        // A one-piece bake (Sean, 2026-11-28: "Yes, skip the station"): an Extruder whose pieces reach the bed makes its piece, no station.
        if (o.IsSub && BakeKits.SubOf(made) is { } sk && SizeClass(c, part) > sk.Oven)
            return !say ? No : $"a sub-assembly bakes on the smallest oven that holds it ({MakerRoutes.OvenName(sk.Oven)}), never a bigger one (the split kit that wants it waits on that)";
        if (!o.IsSub && BakeKits.OnePiece(made) && DirectExtruder(world, c, part, BakeKits.KitOf(made)![0].Key) >= 0)
            return null;
        // The kit is built at the station nearest the oven whose bay holds it; that station orders the kit's pieces through a Line Console
        // wired into its Orders, so one with none would wait for ever (the works' line A oven took the yard's deck orders, 2026-11-23).
        var bp = BakeKits.PrintOfOrder(made)!;
        int near = -1;
        var nearDist = float.MaxValue;
        for (var s = 0; s < c.PartCount; s++)
        {
            if (!c.DefOf(s).IsBuildStation || c.Parts[s].Broken || !ConstructStation.QuickFits(bp, ConstructStation.BaySize(c, s)))
                continue;
            var rolls = ConstructRollOut.RunOut(c, s) is var run and >= 0 ? ConstructRollOut.OvenOf(c, run) : -1;
            if (rolls >= 0 && rolls != part)
                continue; // (another oven's station: its kits roll to that bed, as the kit's start takes it: Kits)
            var dist = rolls == part ? -1 : Vector3.DistanceSquared(c.Parts[s].Pos, c.Parts[part].Pos);
            if (dist < nearDist)
                (near, nearDist) = (s, dist);
        }
        if (near < 0)
            return !say ? No : $"no Building Station on this construct has a bay that holds the {made.Name} kit (and doesn't roll its kits to another oven)";
        if (!OrdersConsole(c, near) && AnyOrdersConsole(c, bp)) // (none has one: a plain station orders by its own links, as any build)
            return !say ? No : $"the Building Station nearest the {d.Name} that holds the {made.Name} kit has no Line Console wired into its Orders, so it can't order the kit's pieces";
        if (OrdersConsole(c, near) && LineLacks(world, c, near, bp) is { } lacks)
            return !say ? No : $"nothing on the line of the Building Station nearest the {d.Name} makes the {made.Name} kit's {lacks}"; // (a deck's 10 m plates on line A's 6 m Sheet Extruder: Sean, 2026-11-23, decks on line C, walls on A and B)
        if (PadLacks(world, c, near, bp) is { } wide)
            return !say ? No : $"the {made.Name} kit's {wide} fits none of the pick pads of the Building Station nearest the {d.Name}"; // (a 2.75 m wall plate and a narrow line's 2 m strip pad, 2026-11-26)
        return null;
    }

    /// <summary>Why a kit of these sides (m) fits no way up in the oven's tunnel (the hearth between its walls and doors, under its roof), or null.</summary>
    internal static string? BedLacks(Construct c, in CMachineAsm asm, Vector3 kit)
    {
        if (BedFits(c, asm, kit))
            return null;
        var bed = new Bed(c, asm);
        var room = bed.InMax - bed.InMin;
        return FormattableString.Invariant($"{kit.X:0.#} x {kit.Y:0.#} x {kit.Z:0.#} m, the hearth {room.X:0.#} x {room.Z:0.#} m, {room.Y:0.#} m clear");
    }

    /// <summary>Does a kit of these sides lie some way in the oven's tunnel (any of the six ways a box lies)? Allocates nothing.</summary>
    internal static bool BedFits(Construct c, in CMachineAsm asm, Vector3 kit)
    {
        var bed = new Bed(c, asm);
        var room = bed.InMax - bed.InMin; // (x across, y the clear height, z along)
        Span<float> s = [kit.X, kit.Y, kit.Z];
        for (var k = 0; k < 6; k++)
        {
            // (each of the six ways a box lies: which side goes across, up and along)
            var (a, b, l) = k switch { 0 => (0, 1, 2), 1 => (0, 2, 1), 2 => (1, 0, 2), 3 => (1, 2, 0), 4 => (2, 0, 1), _ => (2, 1, 0) };
            if (s[a] <= room.X + 0.05f && s[b] <= room.Y + 0.05f && s[l] <= room.Z + 0.05f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// How an oven ranks for an order among the ovens that take it (lower first: Sean, 2026-11-28, "utilize the different oven sizes"): its
    /// size class (<see cref="MakerRoutes.Ovens"/>), so an order goes to the smallest bed that holds it and a bigger oven takes one only when
    /// every smaller one has <see cref="SpillAt"/> more orders queued than it; then its queue, then its bed's cells. Allocates nothing.
    /// </summary>
    public static int Rank(Construct c, int m)
    {
        var score = (c.Parts[m].Orders?.Count ?? 0) * 256;
        if (MachineBlocks.TryOf(c, m, out var a) && a.W > 0)
        {
            var bed = new Bed(c, a);
            score += Math.Min(255, a.W * a.L) + MakerRoutes.OvenFor(new Vector3(bed.Across, bed.Clear, bed.Along)) * SpillAt * 256;
        }
        return score;
    }

    /// <summary>The orders more a smaller oven holds before a bigger one that also fits takes the next.</summary>
    public const int SpillAt = 4;

    /// <summary>
    /// Server: the Extruder on this construct whose pieces reach oven <paramref name="part"/>'s bed (by a conveyor onto a run the oven takes,
    /// ConstructRollOut) and that takes an order for <paramref name="key"/>, the one with the shortest queue; or -1. Allocates nothing.
    /// </summary>
    internal static int DirectExtruder(ConstructWorld world, Construct c, int part, in ExtrusionKey key)
    {
        if (!AnyRunInto(c, part))
            return -1;
        var o = new WorkOrder { Key = key with { Growing = false }, Part = ConstructParts.None, Count = 1 };
        int best = -1, bestQ = int.MaxValue;
        for (var m = 0; m < c.PartCount; m++)
        {
            var md = c.DefOf(m);
            if (!md.IsExtruder || md.IsPanelFormer || c.Parts[m].Broken || md.BlockRole is not (BlockRole.None or BlockRole.Core))
                continue;
            var q = c.Parts[m].Orders?.Count ?? 0;
            if (q < bestQ && ConstructLogisticsPlan.Leads(c, m, part) && WorkOrders.Refusal(world, c, m, o) == null && ConstructExtruder.Feeds(world, c, m, key.Material))
                (best, bestQ) = (m, q);
        }
        return best;
    }

    /// <summary>Does a conveyor run end on oven <paramref name="part"/>'s bed (the oven takes it, ConstructRollOut.OvenOf)?</summary>
    internal static bool AnyRunInto(Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].Type == RouteType.Conveyor && ConstructRollOut.OvenOf(c, k) == part)
                return true;
        return false;
    }

    /// <summary>
    /// Server, once a second an oven: its head order's one-piece kit (BakeKits.OnePiece) ordered of the Extruder whose pieces reach its bed,
    /// one or two pieces at a time; the piece rolls off its run onto the bed (ConstructRollOut.RiderOnto) and bakes into the part. False:
    /// no such Extruder (the kit is built at a station as any other).
    /// </summary>
    private static bool OnePieceFeed(ConstructWorld world, Construct c, int part, in WorkOrder head, ConstructPartDef d, bool bedKit)
    {
        var key = BakeKits.KitOf(d)![0].Key with { Growing = false };
        var ex = DirectExtruder(world, c, part, key);
        if (ex < 0)
            return false;
        // (its pieces under way: queued at any Extruder, riding the construct's conveyors, and on the bed)
        var under = bedKit ? 1 : 0;
        for (var m = 0; m < c.PartCount; m++)
            if (c.DefOf(m).IsExtruder && c.Parts[m].Orders is { Bill: false } q)
                for (var k = 0; k < q.Count; k++)
                    if (q.Items[k].IsPiece && (q.Items[k].Key with { Growing = false }) == key)
                        under += q.Items[k].Left;
        for (var i = 0; i < c.ConveyedCount; i++)
            if (ConstructParts.Get(c.Conveyed[i].Def) is { IsExtrusion: true } rd && (rd.ExKey with { Growing = false }) == key)
                under++;
        if (under >= Math.Min(2, head.Left))
            return true;
        var o = new WorkOrder { Key = key, Part = ConstructParts.None, Count = 1 };
        if (WorkOrders.Queue(world, c, ex, o) is { } no)
            Note(world, c, part, $"the {d.Name}'s piece: {no}");
        else
        {
            Note(world, c, part, "");
            Console.WriteLine($"[oven] construct {c.Id} part {part}: the {d.Name}'s one piece ordered of the {c.DefOf(ex).Name} {ex} (a one-piece bake, no station)");
        }
        return true;
    }

    /// <summary>
    /// A kit's first piece kind that none of rover station <paramref name="t"/>'s pick pads could ever hold, or null (a station with a hold,
    /// or with no pick pads, says nothing here). Its pieces come only by its pads, so such a kit would wait for ever. Allocates nothing.
    /// </summary>
    private static string? PadLacks(ConstructWorld world, Construct c, int t, ConstructBuild.Blueprint bp)
    {
        if (!c.DefOf(t).StationNoHold)
            return null;
        Span<ushort> pads = stackalloc ushort[ConstructStation.MaxOwnPads];
        var pn = ConstructStation.OwnPads(c, t, pads);
        if (pn == 0)
            return null;
        // (the pads its own lines' bed machines drop onto, when they drop onto its pads: a narrow line's pieces reach only the strip at its
        // Laser Cutter's Out Table, not the station's wide bay-side pads, so those alone say whether its pieces can lie there, 2026-11-26)
        Span<int> mine = stackalloc int[64];
        var mn = LineMachines(c, t, mine);
        Span<ushort> outs = stackalloc ushort[16];
        var on = 0;
        for (var m = 0; m < mn && on < outs.Length; m++)
        {
            // (down its out runs a few beds deep: a narrow line's console runs its Extruder and Panel Former, whose runs and a turntable field end at a Laser
            // Cutter's bed, and that cutter's Out Table butts the pad)
            var at = mine[m];
            for (var hop = 0; hop < 8 && at >= 0 && at < c.PartCount; hop++)
            {
                if (ConstructConveyor.OutPad(c, at) is >= 0 and < ushort.MaxValue and var op)
                {
                    if (ConstructStation.IsOwnPad(c, t, op) && outs[..on].IndexOf((ushort)op) < 0)
                        outs[on++] = (ushort)op;
                    break;
                }
                at = ConstructConveyor.Out(c, at) is >= 0 and var o && c.Routes[o].To < c.PartCount ? c.Routes[o].To : -1; // (a run, a turntable field's crossing, the next bed)
            }
        }
        if (on > 0)
        {
            pads = outs;
            pn = on;
        }
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            var name = bp.Steps[k].Part;
            var seen = false;
            for (var e = 0; e < k && !seen; e++)
                seen = bp.Steps[e].Part == name;
            if (seen)
                continue;
            var pd = ConstructParts.Find(name) ?? (name.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase) && Extrusion.ParseKey(name) is { } key
                ? Extrusion.Def(key with { Growing = false }) : null);
            if (pd == null || ConstructStation.IsLaid(pd))
                continue;
            var any = false;
            for (var p = 0; p < pn && !any; p++)
                any = world.StoreOf(c, pads[p]) is { } ps && !ConstructLifters.NeverHolds(c, pads[p], ps, pd);
            if (!any)
                return pd.Name;
        }
        return null;
    }

    /// <summary>The machines (not ovens) station <paramref name="t"/>'s own Line Consoles run, into <paramref name="mine"/>; their count. Allocates nothing.</summary>
    internal static int LineMachines(Construct c, int t, Span<int> mine)
    {
        Span<int> line = stackalloc int[32];
        var mn = 0;
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type != RouteType.Data || r.To != t || !r.FedEnd || r.Source >= c.PartCount || !ProductionLines.IsConsole(c.DefOf(r.Source)))
                continue;
            var m = ProductionLines.Machines(c, r.Source, line);
            for (var j = 0; j < m && mn < mine.Length; j++)
                if (!mine[..mn].Contains(line[j]) && !c.DefOf(line[j]).IsBakeOven)
                    mine[mn++] = line[j];
        }
        return mn;
    }

    /// <summary>
    /// A kit's first piece kind that no machine station <paramref name="t"/>'s Line Consoles run takes an order for (a laid part, a Hull Dress piece,
    /// comes from steel at the station), or null. Allocates nothing.
    /// </summary>
    private static string? LineLacks(ConstructWorld world, Construct c, int t, ConstructBuild.Blueprint bp)
    {
        // The machines its own Line Consoles run, not the whole network the conveyors join: on Zero Day Works II every line's runs meet,
        // so a small line's station counted another line's Extruder, whose pieces go through that line's Panel Former and never reach it
        // (2026-11-26: the 14.1 m wall plates).
        Span<int> mine = stackalloc int[64];
        var mn = LineMachines(c, t, mine);
        for (var k = 0; k < bp.Steps.Count; k++)
        {
            var name = bp.Steps[k].Part;
            var seen = false;
            for (var e = 0; e < k && !seen; e++)
                seen = bp.Steps[e].Part == name;
            if (seen)
                continue;
            // (an extrusion's def is made at run time: one no machine has made yet isn't found by name, 2026-11-26)
            var pd = ConstructParts.Find(name) ?? (name.StartsWith(Extrusion.Prefix, StringComparison.OrdinalIgnoreCase) && Extrusion.ParseKey(name) is { } key
                ? Extrusion.Def(key with { Growing = false }) : null);
            if (pd == null || ConstructStation.IsLaid(pd))
                continue;
            if (!pd.IsExtrusion && !pd.IsNamedRuntime && !pd.PhysicsOnly)
                continue; // (a rule kit's component, a Bearing or a Circuit Board: a press or stock on the station's network makes it, not its line)
            var o = ConstructStation.OrderFor(pd, 1);
            var any = false;
            for (var m = 0; m < mn && !any; m++)
                any = WorkOrders.Refusal(world, c, mine[m], o) == null
                    && (!pd.IsExtrusion || !c.DefOf(mine[m]).IsExtruder || c.DefOf(mine[m]).IsPanelFormer || pd.ExKey.Shape is not (ExShape.Section or ExShape.Round or ExShape.Pipe)
                        || ConstructExtruder.Feeds(world, c, mine[m], pd.ExKey.Material)); // (an empty Extruder nothing brings the piece's material to: phase 4 review)
            if (!any)
                return pd.Name;
        }
        return null;
    }

    /// <summary>Has any station on the construct whose bay holds the kit a Line Console wired into its Orders (a works that orders through consoles)?</summary>
    private static bool AnyOrdersConsole(Construct c, ConstructBuild.Blueprint bp)
    {
        for (var s = 0; s < c.PartCount; s++)
            if (c.DefOf(s).IsBuildStation && !c.Parts[s].Broken && ConstructStation.QuickFits(bp, ConstructStation.BaySize(c, s)) && OrdersConsole(c, s))
                return true;
        return false;
    }

    /// <summary>Is a Line Console wired into station <paramref name="t"/>'s Orders port (ConstructCrafting's network)?</summary>
    private static bool OrdersConsole(Construct c, int t)
    {
        for (var k = 0; k < c.RouteCount; k++)
        {
            ref readonly var r = ref c.Routes[k];
            if (r.Type == RouteType.Data && r.To == t && r.FedEnd && r.Source < c.PartCount && ProductionLines.IsConsole(c.DefOf(r.Source)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Server, an oven's step: its head order's kit started, carried and let go on the bed (<paramref name="open"/>: its hood up); and, at a
    /// station whose builds roll along a run onto this bed, the next kit started as soon as the station's bay is clear, whatever the bed
    /// is doing (Sean, 2026-11-27: the lines keep running; ConstructStationAhead.cs). <paramref name="bedKit"/>: an unbaked kit lies on the
    /// bed (it counts as one of the queue's kits under way).
    /// </summary>
    private static void Kits(ConstructWorld world, Construct c, int part, in CMachineAsm asm, in Bed bed, OvenStall why, bool open, bool bedKit)
    {
        var key = c.Id << 16 | part;
        ushort onWay = 0;
        if (world.OvenKits.TryGetValue(key, out var carried))
        {
            // A kit set on the bed is let go (it was held frozen on the hook): it settles, and the oven bakes it.
            if (world.Get(carried) is not { } kb)
            {
                world.OvenKits.Remove(key);
                return;
            }
            if (ConstructLifters.CarryingBody(world, carried) == null)
            {
                if (!open)
                    return;
                if (kb.HeldBy == c.Id)
                    kb.HeldBy = 0;
                ConstructEdits.Unfreeze(world, kb);
                world.OvenKits.Remove(key);
                return;
            }
            onWay = carried; // (on a hook or rolling to the bed: it is under way, and the next kit may start behind it, below)
        }
        if (world.OvenLooks % KitEvery != part % KitEvery)
            return;
        if (!WorkOrders.HeadOf(c.Parts[part], out var head) || BakeKits.OvenDef(head) is not { } d
            || BakeKits.PrintOfOrder(d) is not { } bp) // (the whole kit or a sub-kit's: its footprint picks the station; the split kit is chosen at the start, PrintFor)
            return;
        if (head.IsSub)
            world.KeepBaked(BakeKits.SubOf(d)!.Hash); // (its design goes to every peer and the save with its first kit: once)
        if (!head.IsSub && BakeKits.OnePiece(d) && onWay == 0 && OnePieceFeed(world, c, part, head, d, bedKit))
            return; // (a one-piece bake: its piece comes from an Extruder, never a station)
        var bedMid = Vector3.Transform(bed.Mid(bed.Along * 0.5f), CoreFrame(c, part));
        // A kit of this order already at a station: building, or done and waiting in its bay for a lifter.
        int mine = -1, idle = -1, ahead = -1;
        float mineDist = float.MaxValue, idleDist = float.MaxValue;
        var under = RollsTo(world, c, part, onWay) + (onWay != 0 ? 1 : 0) + (bedKit ? 1 : 0); // (the queue's kits under way: on the bed, on their way to it, at its stations)
        for (var s = 0; s < c.PartCount; s++)
        {
            if (!c.DefOf(s).IsBuildStation || c.Parts[s].Broken)
                continue;
            // A station whose kits roll out along a run to another oven's bed is that oven's; one that rolls to this bed comes first
            // (the works' lines B and C: line C's station is as near line B's oven as line B's own).
            var rolls = ConstructRollOut.RunOut(c, s) is var run and >= 0 ? ConstructRollOut.OvenOf(c, run) : -1;
            if (rolls >= 0 && rolls != part)
                continue;
            ConstructStation.Bay(c, s, out var floor, out _, out _);
            var dist = rolls == part ? -1 : Vector3.DistanceSquared(floor, bedMid);
            var sj = c.Parts[s].Station;
            var kit = sj != null && KitUnderWay(world, c, s, sj, onWay);
            if (kit && (sj!.Name.Equals(bp.Name, StringComparison.OrdinalIgnoreCase) || BakeKits.IsKitOf(sj.Name, d)))
            {
                under++;
                if (dist < mineDist)
                    (mine, mineDist) = (s, dist);
                continue;
            }
            if (kit && rolls == part)
            {
                under++; // (a later order's kit, started ahead at this bed's own station)
                continue;
            }
            if (ConstructStationTree.Idle(world, c, s) && ConstructRecycler.Powered(world, c, s))
            {
                if (dist < idleDist && ConstructStation.QuickFits(bp, ConstructStation.BaySize(c, s)))
                    (idle, idleDist) = (s, dist);
                if (rolls == part && ahead < 0)
                    ahead = s;
            }
        }
        if (mine >= 0 && open && onWay == 0 && c.Parts[mine].Station!.State == StationState.Done)
        {
            var mj = c.Parts[mine].Station!;
            if (why != OvenStall.Empty || world.Get(mj.Body) is not { } kit || ConstructLifters.CarryingBody(world, kit.Id) != null)
                return; // (the bed isn't clear yet)
            if (ConstructRollOut.RunOut(c, mine) is var way and >= 0 && ConstructRollOut.OvenOf(c, way) == part)
                return; // (its station rolls it out along the run to this bed: ConstructRollOut.FromStation)
            if (Landing(c, part, asm, bed, kit, out var toPos, out var toRot) is { } no)
            {
                Note(world, c, part, $"the {d.Name} kit doesn't fit the bed: {no}");
                return;
            }
            if (ConstructLifters.CarryBody(world, c, mine, kit, toPos, toRot, out var who, out var cant))
            {
                world.OvenKits[key] = kit.Id;
                Note(world, c, part, "");
                Console.WriteLine($"[oven] construct {c.Id} part {part}: the {d.Name} kit (body {kit.Id}) carried to its bed by the {who}");
            }
            else
                Note(world, c, part, $"the {d.Name} kit waits in its station's bay: {cant}");
            return;
        }
        if (under == 0)
        {
            // Nothing under way: the head order's kit, at the nearest idle station (one kit at a time where no station rolls to this bed).
            if (!open || why != OvenStall.Empty)
                return; // (the bed is busy, or what is on it waits to be lifted off)
            if (idle < 0)
            {
                Note(world, c, part, $"no Building Station free for the {d.Name} kit (idle, powered, its bay big enough)");
                return;
            }
            if (ConstructStation.Start(world, c, idle, (PrintFor(world, c, part, d, idle) ?? bp).Name, 0) is { } refused)
                Note(world, c, part, $"the {d.Name} kit: {refused}");
            else
            {
                Note(world, c, part, "");
                Console.WriteLine($"[oven] construct {c.Id} part {part}: the {d.Name} kit started at the {c.DefOf(idle).Name} {idle}");
            }
            return;
        }
        // Kits under way: the next order's kit starts ahead, only at a station whose builds roll onto this bed (it waits at the run's end
        // behind the one before; a crane-fed station's kit would wait in its bay and hold the station for another oven's kit).
        if (ahead < 0 || KitAfter(world, c, part, under, ahead) is not { } next || !ConstructStation.QuickFits(next, ConstructStation.BaySize(c, ahead)))
            return;
        if (ConstructStation.Start(world, c, ahead, next.Name, 0) is { } no2)
            Console.WriteLine($"[oven] construct {c.Id} part {part}: the next kit ({next.Name}) can't start ahead: {no2}");
        else
            Console.WriteLine($"[oven] construct {c.Id} part {part}: the {next.Name} started ahead at the {c.DefOf(ahead).Name} {ahead} ({under} under way)");
    }

    /// <summary>
    /// Server: the blueprint oven <paramref name="part"/> builds for an order of <paramref name="d"/> at station <paramref name="station"/>:
    /// a sub-kit's own; a split kit (BakeKits.Subs.cs: its baked sub-assemblies and leftover pieces) when every sub-kit of it is taken by
    /// another oven of a smaller size than this one that the station's orders reach (its craft network, as ConstructCrafting.Place asks) and
    /// whose bed leads to the station (ConstructLogisticsPlan.Leads: the baked piece can come to it), so the split kit never waits on its own
    /// oven, on a sub-kit queued behind a big oven's order, or on a piece nothing brings; else the whole kit. Kept a part, oven and station,
    /// worked out again every <see cref="SplitEvery"/> oven looks, at most <see cref="SplitBudget"/> work-outs a look of the world (an
    /// answer older stands meanwhile; none yet: the whole kit). Allocates nothing past each key's first.
    /// </summary>
    internal static ConstructBuild.Blueprint? PrintFor(ConstructWorld world, Construct c, int part, ConstructPartDef d, int station)
    {
        if (BakeKits.IsSub(d) || BakeKits.SplitOf(d) is not { } sk || BakeKits.SplitPrint(d) is not { } split)
            return BakeKits.PrintOfOrder(d);
        var key = (long)c.Id << 40 | (long)part << 20 | (uint)d.Id & 0xFFFFF;
        var had = world.OvenSplit.TryGetValue(key, out var got);
        if (had && got.Station == station && world.OvenLooks - got.Look < SplitEvery && got.Stamp == c.RebuildStamp)
            return got.Split ? split : BakeKits.BlueprintOf(d);
        if (world.OvenSplitLook != world.OvenLooks)
            (world.OvenSplitLook, world.OvenSplitLeft) = (world.OvenLooks, SplitBudget);
        if (world.OvenSplitLeft <= 0)
            return had && got.Station == station && got.Split ? split : BakeKits.BlueprintOf(d); // (this look's work-outs are spent)
        world.OvenSplitLeft--;
        var takes = SplitServed(world, c, part, sk, station);
        world.OvenSplit[key] = (world.OvenLooks, c.RebuildStamp, station, takes);
        if (world.OvenSplitTold.Add(key << 1 | (takes ? 1L : 0L)))
            Console.WriteLine(takes ? $"[oven] construct {c.Id} part {part}: the {d.Name} is built split at station {station} ({sk.Subs.Length} sub-kits on smaller ovens, {sk.Items.Length} kit items)"
                : $"[oven] construct {c.Id} part {part}: the {d.Name} is built whole: no smaller oven that station {station} orders from and that leads to it takes all its {sk.Subs.Length} sub-kits");
        return takes ? split : BakeKits.BlueprintOf(d);
    }

    /// <summary>Does every sub-kit of <paramref name="sk"/> go to a smaller oven than <paramref name="part"/> on station <paramref name="station"/>'s craft network whose bed leads to the station?</summary>
    private static bool SplitServed(ConstructWorld world, Construct c, int part, BakeKits.SplitKit sk, int station)
    {
        if (station < 0 || station >= c.PartCount || !c.DefOf(station).IsCraftTerminal || world.StoreOf(c, station) == null)
            return false;
        var net = ConstructCrafting.NetOf(world, c, station);
        var mine = SizeClass(c, part);
        foreach (var sub in sk.Subs)
        {
            if (BakeKits.SubOrder(sub.Key, 1) is not { } o)
                return false;
            var any = false;
            for (var i = 0; i < net.MachineN && !any; i++)
            {
                var m = net.Machines[i];
                if (m != part && c.DefOf(m).IsBakeOven && !c.Parts[m].Broken && SizeClass(c, m) < mine
                    && OrderRefusal(world, c, m, c.DefOf(m), o, say: false) == null && ConstructLogisticsPlan.Leads(c, m, station))
                    any = true;
            }
            if (!any)
                return false;
        }
        return true;
    }

    private const int SplitBudget = 4; // work-outs of PrintFor a world's oven look

    /// <summary>An oven's size class (<see cref="MakerRoutes.Ovens"/>, by its bed and tunnel; past the last: bigger), or int.MaxValue with no bed. Allocates nothing.</summary>
    internal static int SizeClass(Construct c, int m)
    {
        if (!MachineBlocks.TryOf(c, m, out var a) || a.W == 0)
            return int.MaxValue;
        var bed = new Bed(c, a);
        return MakerRoutes.OvenFor(new Vector3(bed.Across, bed.Clear, bed.Along));
    }

    private const int SplitEvery = 200; // oven looks (about 20 s of one oven)

    /// <summary>Is station <paramref name="s"/> building a kit, or done with one still in its bay (not the one <paramref name="onWay"/> to the bed)?</summary>
    private static bool KitUnderWay(ConstructWorld world, Construct c, int s, StationJob j, ushort onWay) =>
        j.Role != 1 && (j.State is StationState.Costing or StationState.Building
            || j.State == StationState.Done && j.Body != onWay && world.Get(j.Body) is { } jb && !ConstructStation.BayClearOf(world, c, s, jb));

    /// <summary>Does an unbaked kit lie on an open oven's bed (its stall says what is there)?</summary>
    internal static bool BedKit(OvenStall why) => why is not (OvenStall.Empty or OvenStall.Done or OvenStall.Failed or OvenStall.NoBed);

    /// <summary>Bodies rolling along a run onto oven <paramref name="part"/>'s bed other than <paramref name="onWay"/> (the one its OvenKits holds): kits under way too.</summary>
    private static int RollsTo(ConstructWorld world, Construct c, int part, ushort onWay)
    {
        var n = 0;
        foreach (var r in world.BodyRolls)
            if (r.Host == c.Id && r.Oven == part && r.Body != onWay)
                n++;
        return n;
    }

    /// <summary>The kit of the queue's unit after the first <paramref name="under"/> (the kits under way), in queue order; null when the queue holds no more.</summary>
    private static ConstructBuild.Blueprint? KitAfter(ConstructWorld world, Construct c, int part, int under, int station)
    {
        if (c.Parts[part].Orders is not { Count: > 0, Bill: false } q)
            return null;
        for (var k = 0; k < q.Count; k++)
        {
            ref readonly var o = ref q.Items[k];
            if (BakeKits.OvenDef(o) is not { } d || PrintFor(world, c, part, d, station) is not { } bp)
                continue;
            if (under < o.Left)
                return bp;
            under -= o.Left;
        }
        return null;
    }

    /// <summary>
    /// Server: the kit the oven that station <paramref name="t"/>'s builds roll onto bakes after every kit now under way (the one on its
    /// bed, on its way, and at its stations, this station's own build with them), or null (no such oven, or nothing more queued).
    /// The station orders its pieces ahead (ConstructStationAhead.cs). Once every 10 s a station.
    /// </summary>
    public static ConstructBuild.Blueprint? NextKitFor(ConstructWorld world, Construct c, int t, int ahead = 0)
    {
        var oven = ConstructRollOut.RunOut(c, t) is var run and >= 0 ? ConstructRollOut.OvenOf(c, run) : -1;
        if (oven < 0 || world.StoreOf(c, oven) is not { } st || c.Parts[oven].Orders is not { Count: > 0, Bill: false })
            return null;
        var phase = (OvenPhase)st.Blocked;
        var onWay = world.OvenKits.TryGetValue(c.Id << 16 | oven, out var carried) && world.Get(carried) != null ? carried : (ushort)0;
        var under = RollsTo(world, c, oven, onWay) + (onWay != 0 ? 1 : 0) + (phase is OvenPhase.Drop or OvenPhase.Heat || phase == OvenPhase.Open && BedKit((OvenStall)st.Refused) ? 1 : 0);
        for (var s = 0; s < c.PartCount; s++)
            if (c.DefOf(s).IsBuildStation && !c.Parts[s].Broken && c.Parts[s].Station is { } j && KitUnderWay(world, c, s, j, onWay)
                && ConstructRollOut.RunOut(c, s) is var r and >= 0 && ConstructRollOut.OvenOf(c, r) == oven)
                under++;
        return KitAfter(world, c, oven, under + ahead, t); // (ahead: that many kits further on)
    }

    private static void Note(ConstructWorld world, Construct c, int part, string note)
    {
        // (said once: the same note again is quiet)
        if (c.Parts[part].Orders is { } q && q.Note != note)
        {
            q.Note = note;
            world.OrdersChanged(c); // (the note rides with the queue to every peer)
            if (note.Length > 0)
                Console.WriteLine($"[oven] construct {c.Id} part {part}: {note}");
        }
    }

    /// <summary>
    /// Where a kit lands in the tunnel: its bounds' middle over the hearth's middle, its underside 2 cm over the rollers, turned as it stands
    /// or a quarter turn about the bed's up, whichever lies on the hearth. A kit that fits neither way standing is laid on its side (a quarter
    /// turn about the tunnel's across or along axis, then either way about its up): the Zero Day Stern's ring kit stood 9.3 m and was let go
    /// at a 6 m tunnel's mouth (Sean, 2026-11-27: "build it flat so that it fits in the oven"); lying it is 4.5 m. The bake takes a kit at
    /// any of the 24 grid turns (<see cref="BakeKits.Target"/>). Null, or why it fits no way.
    /// </summary>
    public static string? Landing(Construct c, int part, Construct kit, out Vector3 pos, out Quaternion rot)
    {
        (pos, rot) = (kit.Pos, kit.Rot);
        if (!MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0)
            return "no bed";
        return Landing(c, part, asm, new Bed(c, asm), kit, out pos, out rot);
    }

    private static string? Landing(Construct c, int part, in CMachineAsm asm, in Bed bed, Construct kit, out Vector3 pos, out Quaternion rot)
    {
        pos = kit.Pos;
        rot = kit.Rot;
        if (kit.Dirty)
            kit.Rebuild();
        var frame = TunnelFrame(c, part, bed);
        var tunnelRot = Quaternion.CreateFromRotationMatrix(frame with { M41 = 0, M42 = 0, M43 = 0 });
        var mid = new Vector3(bed.Across * 0.5f, bed.Roll, bed.Along * 0.5f);
        var (inLo, inHi) = (bed.InMin, bed.InMax);
        string? why = null;
        string? standing = null;
        for (var turn = 0; turn < 6; turn++)
        {
            if (turn == 2)
                standing = why; // (what keeps it from standing: said if it fits lying no way either)
            // 0, 1: standing (as it is, a quarter turn about the bed's up); 2, 3: laid over about the tunnel's across axis; 4, 5: about its along.
            var r = kit.Rot;
            if (turn >= 2)
                r = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(turn < 4 ? Vector3.UnitX : Vector3.UnitZ, tunnelRot), MathF.PI / 2) * r);
            if ((turn & 1) == 1)
                r = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitY, tunnelRot), MathF.PI / 2) * r);
            // The kit's rest box in the tunnel's frame, turned so.
            var toKit = Quaternion.Normalize(Quaternion.Conjugate(tunnelRot) * r);
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (var k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? kit.LocalMin.X : kit.LocalMax.X, (k & 2) == 0 ? kit.LocalMin.Y : kit.LocalMax.Y, (k & 4) == 0 ? kit.LocalMin.Z : kit.LocalMax.Z);
                var v = Vector3.Transform(corner, toKit);
                (lo, hi) = (Vector3.Min(lo, v), Vector3.Max(hi, v));
            }
            var shift = new Vector3(mid.X - (lo.X + hi.X) * 0.5f, mid.Y + 0.02f - lo.Y, mid.Z - (lo.Z + hi.Z) * 0.5f);
            lo += shift;
            hi += shift;
            if (hi.Y > bed.Roof)
            {
                why = $"{hi.Y - lo.Y:0.0} m tall, the tunnel clears {bed.Clear:0.0} m (its Tunnel height setting)";
                continue;
            }
            const float slack = 0.05f;
            if (lo.X < inLo.X - slack || hi.X > inHi.X + slack || lo.Z < inLo.Z - slack || hi.Z > inHi.Z + slack)
            {
                why = $"{hi.X - lo.X:0.0} x {hi.Z - lo.Z:0.0} m, the hearth {inHi.X - inLo.X:0.0} x {inHi.Z - inLo.Z:0.0} m between the walls and the doors";
                continue;
            }
            // The body's origin where the shifted box puts it: the kit's frame turned by r, moved by the shift (in the tunnel's frame).
            pos = Vector3.Transform(shift, frame);
            rot = r;
            return null;
        }
        return standing ?? why;
    }
}
