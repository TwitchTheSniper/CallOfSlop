using System.Numerics;

namespace CallOfSlop;

public sealed partial class ConstructWorld
{
    /// <summary>Server: each Bake Oven's kit on a lifter's hook or just set on its bed, by the oven ((body &lt;&lt; 16) | part): the kit's body. Not saved.</summary>
    internal readonly Dictionary<int, ushort> OvenKits = new(4);
    internal int OvenLooks; // oven steps so far (a kit look a second an oven)
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

    /// <summary>Server: will this oven take the order? Null, or why not.</summary>
    public static string? OrderRefusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        if (o.IsPiece || o.Name != null)
            return $"the {d.Name} bakes parts made from kits: {ConstructCrafting.NameOf(o)} isn't one";
        var made = ConstructParts.Def(o.Part);
        if (!BakeKits.Has(made) || BakeKits.BlueprintOf(made) == null)
            return $"the {made.Name} isn't made from a kit: an oven bakes only what Constructs/BakeKits.json and Constructs/BakeKits/ list";
        if (Unbaking(c.Parts[part], d))
            return $"the {d.Name} is set to Un-bake";
        if (o.Count is < 1 or > WorkOrders.MaxCount)
            return $"an order is for 1 to {WorkOrders.MaxCount} parts";
        if (c.Parts[part].Orders is { Count: >= WorkOrders.MaxOrders })
            return $"the {d.Name}'s queue is full ({WorkOrders.MaxOrders} orders)";
        if (!MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0)
            return $"the {d.Name} has no bed: weld Oven Bed Blocks beside it";
        if (BakeKits.MinBedCells(made) is var least and > 0 && asm.W * asm.L < least)
            return $"the {made.Name} bakes on a bed of {least} cells or more (Constructs/BakeKits.json \"bigBed\"): this one has {asm.W * asm.L}";
        // The kit is built at the station nearest the oven whose bay holds it; that station orders the kit's pieces through a Line Console
        // wired into its Orders, so one with none would wait for ever (the works' line A oven took the yard's deck orders, 2026-11-23).
        var bp = BakeKits.BlueprintOf(made)!;
        int near = -1;
        var nearDist = float.MaxValue;
        for (var s = 0; s < c.PartCount; s++)
        {
            if (!c.DefOf(s).IsBuildStation || c.Parts[s].Broken || ConstructStation.QuickRefusal(bp, ConstructStation.BaySize(c, s)) != null)
                continue;
            var rolls = ConstructRollOut.RunOut(c, s) is var run and >= 0 ? ConstructRollOut.OvenOf(c, run) : -1;
            if (rolls >= 0 && rolls != part)
                continue; // (another oven's station: its kits roll to that bed, as the kit's start takes it: Kits)
            var dist = rolls == part ? -1 : Vector3.DistanceSquared(c.Parts[s].Pos, c.Parts[part].Pos);
            if (dist < nearDist)
                (near, nearDist) = (s, dist);
        }
        if (near < 0)
            return $"no Building Station on this construct has a bay that holds the {made.Name} kit (and doesn't roll its kits to another oven)";
        if (!OrdersConsole(c, near) && AnyOrdersConsole(c, bp)) // (none has one: a plain station orders by its own links, as any build)
            return $"the Building Station nearest the {d.Name} that holds the {made.Name} kit has no Line Console wired into its Orders, so it can't order the kit's pieces";
        if (OrdersConsole(c, near) && LineLacks(world, c, near, bp) is { } lacks)
            return $"nothing on the line of the Building Station nearest the {d.Name} makes the {made.Name} kit's {lacks}"; // (a deck's 10 m plates on line A's 6 m Sheet Extruder: Sean, 2026-11-23, decks on line C, walls on A and B)
        if (PadLacks(world, c, near, bp) is { } wide)
            return $"the {made.Name} kit's {wide} fits none of the pick pads of the Building Station nearest the {d.Name}"; // (a 2.75 m wall plate and a narrow line's 2 m strip pad, 2026-11-26)
        return null;
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
            var o = ConstructStation.OrderFor(pd, 1);
            var any = false;
            for (var m = 0; m < mn && !any; m++)
                any = WorkOrders.Refusal(world, c, mine[m], o) == null;
            if (!any)
                return pd.Name;
        }
        return null;
    }

    /// <summary>Has any station on the construct whose bay holds the kit a Line Console wired into its Orders (a works that orders through consoles)?</summary>
    private static bool AnyOrdersConsole(Construct c, ConstructBuild.Blueprint bp)
    {
        for (var s = 0; s < c.PartCount; s++)
            if (c.DefOf(s).IsBuildStation && !c.Parts[s].Broken && ConstructStation.QuickRefusal(bp, ConstructStation.BaySize(c, s)) == null && OrdersConsole(c, s))
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
        if (!WorkOrders.HeadOf(c.Parts[part], out var head) || head.IsPiece || head.Name != null || ConstructParts.Get(head.Part) is not { } d
            || BakeKits.BlueprintOf(d) is not { } bp)
            return;
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
            if (kit && sj!.Name.Equals(bp.Name, StringComparison.OrdinalIgnoreCase))
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
                if (dist < idleDist && ConstructStation.QuickRefusal(bp, ConstructStation.BaySize(c, s)) == null)
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
            if (ConstructStation.Start(world, c, idle, bp.Name, 0) is { } refused)
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
        if (ahead < 0 || KitAfter(c, part, under) is not { } next || ConstructStation.QuickRefusal(next, ConstructStation.BaySize(c, ahead)) != null)
            return;
        if (ConstructStation.Start(world, c, ahead, next.Name, 0) is { } no2)
            Console.WriteLine($"[oven] construct {c.Id} part {part}: the next kit ({next.Name}) can't start ahead: {no2}");
        else
            Console.WriteLine($"[oven] construct {c.Id} part {part}: the {next.Name} started ahead at the {c.DefOf(ahead).Name} {ahead} ({under} under way)");
    }

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
    private static ConstructBuild.Blueprint? KitAfter(Construct c, int part, int under)
    {
        if (c.Parts[part].Orders is not { Count: > 0, Bill: false } q)
            return null;
        for (var k = 0; k < q.Count; k++)
        {
            ref readonly var o = ref q.Items[k];
            if (o.IsPiece || o.Name != null || ConstructParts.Get(o.Part) is not { } d || BakeKits.BlueprintOf(d) is not { } bp)
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
        return KitAfter(c, oven, under + ahead); // (ahead: that many kits further on)
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
