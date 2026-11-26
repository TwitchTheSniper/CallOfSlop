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
        return null;
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

    /// <summary>Server, an oven's step with its hood up: its head order's kit started, carried and let go on the bed.</summary>
    private static void Kits(ConstructWorld world, Construct c, int part, in CMachineAsm asm, in Bed bed, OvenStall why, int group)
    {
        var key = c.Id << 16 | part;
        if (world.OvenKits.TryGetValue(key, out var carried))
        {
            // A kit set on the bed is let go (it was held frozen on the hook): it settles, and the oven bakes it.
            if (world.Get(carried) is not { } kb)
                world.OvenKits.Remove(key);
            else if (ConstructLifters.CarryingBody(world, carried) == null)
            {
                if (kb.HeldBy == c.Id)
                    kb.HeldBy = 0;
                ConstructEdits.Unfreeze(world, kb);
                world.OvenKits.Remove(key);
            }
            return;
        }
        if (world.OvenLooks % KitEvery != part % KitEvery)
            return;
        if (!WorkOrders.HeadOf(c.Parts[part], out var head) || head.IsPiece || head.Name != null || ConstructParts.Get(head.Part) is not { } d
            || BakeKits.BlueprintOf(d) is not { } bp)
            return;
        var bedMid = Vector3.Transform(bed.Mid(bed.Along * 0.5f), CoreFrame(c, part));
        // A kit of this order already at a station: building, or done and waiting in its bay for a lifter.
        int mine = -1, idle = -1;
        float mineDist = float.MaxValue, idleDist = float.MaxValue;
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
            if (c.Parts[s].Station is { } j && j.Name.Equals(bp.Name, StringComparison.OrdinalIgnoreCase)
                && (j.State is StationState.Costing or StationState.Building || j.State == StationState.Done && world.Get(j.Body) is { } jb && !ConstructStation.BayClearOf(world, c, s, jb)))
            {
                if (dist < mineDist)
                    (mine, mineDist) = (s, dist);
                continue;
            }
            if (dist < idleDist && ConstructStationTree.Idle(world, c, s) && ConstructRecycler.Powered(world, c, s) && ConstructStation.QuickRefusal(bp, ConstructStation.BaySize(c, s)) == null)
                (idle, idleDist) = (s, dist);
        }
        if (mine >= 0)
        {
            var mj = c.Parts[mine].Station!;
            if (mj.State != StationState.Done || why != OvenStall.Empty || world.Get(mj.Body) is not { } kit || ConstructLifters.CarryingBody(world, kit.Id) != null)
                return; // (building, or the bed isn't clear yet)
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
        if (why != OvenStall.Empty)
            return; // (one kit at a time: the bed is busy, or what is on it waits to be lifted off)
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
    /// or a quarter turn about the bed's up, whichever lies on the hearth. Null, or why it fits neither way.
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
        for (var turn = 0; turn < 2; turn++)
        {
            var r = turn == 0 ? kit.Rot : Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitY, tunnelRot), MathF.PI / 2) * kit.Rot);
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
        return why;
    }
}
