using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Panel Former (docs/sandbox/constructs/production.md "The Panel Former"): a machine of the Extruder family that forms named panels
/// (HullLoft.Panels.cs: a player's <c>Formed Panel (...)</c>, or one of the vehicle kit's <c>Hull Loft (...)</c> parts) from the material in
/// its bin, by work order only. Its bin is fed by chute or by hand as an Extruder's hopper, and by extruded sheet off a conveyor into its
/// Piece in, counted in whole. A panel takes a timed stroke; done, it rides out of Piece out on a conveyor, or drops on the out bed.
/// </summary>
public static class ConstructPanelFormer
{
    public const float SheetMaxCm = 10; // an extruded piece this thin or thinner is sheet a former takes off its conveyor

    public const float PinTravel = 2; // m: how far a Bed Block's pins rise, the deepest panel (crown and rake) a bed forms
    public const float SetSeconds = 4, PressSeconds = 8; // a stroke on the block bed: the pins set, then the upper platen presses

    /// <summary>
    /// Seconds a panel takes on a former of speed 1. On the block bed (a Core) the blocks work together, so every stroke is the same: the
    /// pins set and the platen presses (drawing 15). An older former's: 4 s and 2 s a square metre of its largest face.
    /// </summary>
    public static float Seconds(ConstructPartDef former, ConstructPartDef panel)
    {
        if (former.BlockRole == BlockRole.Core)
            return SetSeconds + PressSeconds;
        Sides(panel, out var a, out var b);
        return 4 + 2 * a * b;
    }

    /// <summary>
    /// The bed a former has now (m): along its length (away from the Core) and across. A Core's is its Bed Blocks' rectangle (MachineBlocks.cs,
    /// zero with none); another former's its file's. <paramref name="blocks"/>: the rectangle in blocks (along, across), 0 for a file's bed.
    /// </summary>
    public static void Bed(Construct c, int part, out float along, out float across, out int nAlong, out int nAcross)
    {
        var d = c.Parts[part].D;
        (along, across, nAlong, nAcross) = (d.ExtWheelMax.Y, d.ExtWheelMax.X, 0, 0);
        if (d.BlockRole != BlockRole.Core)
            return;
        if (!MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0)
        {
            (along, across) = (0, 0);
            return;
        }
        var lengthwise = asm.OutSide is 4 or 5; // (the bed runs from the Core along z, else along x)
        (nAlong, nAcross) = lengthwise ? (asm.L, asm.W) : (asm.W, asm.L);
        (along, across) = (nAlong * d.BlockCell, nAcross * d.BlockCell);
    }

    /// <summary>
    /// Server and client: why this former can't form <paramref name="panel"/> now, or null. A Core: no bed, no Out Table on its far end, a
    /// panel bigger than its bed (in blocks: "needs a 4 x 3 bed, this one is 3 x 2") or deeper than its pins rise.
    /// </summary>
    public static string? BedRefusal(Construct c, int part, ConstructPartDef panel)
    {
        var d = c.Parts[part].D;
        Bed(c, part, out var along, out var across, out var nAlong, out var nAcross);
        Sides(panel, out var a, out var b);
        if (d.BlockRole == BlockRole.Core)
        {
            if (along <= 0)
                return $"no bed: weld Bed Blocks to its far side, then an Out Table on the bed's end ({d.BlockCell:0} x {d.BlockCell:0} m each)";
            MachineBlocks.TryOf(c, part, out var asm);
            if (asm.Out == ushort.MaxValue || asm.OutCells < nAcross)
                return asm.Out == ushort.MaxValue
                    ? "no Out Table: weld one on the bed's far end, across from it"
                    : $"its Out Tables cover {asm.OutCells} of the bed's {nAcross} blocks across: weld more beside them";
            var deep = MathF.Min(panel.BoundsMax.X - panel.BoundsMin.X, MathF.Min(panel.BoundsMax.Y - panel.BoundsMin.Y, panel.BoundsMax.Z - panel.BoundsMin.Z));
            if (deep > PinTravel + 0.005f)
                return $"panel too deep for the pins: {deep:0.##} m of crown and rake, the pins rise {PinTravel:0} m";
        }
        if (a <= MathF.Max(along, across) + 0.005f && b <= MathF.Min(along, across) + 0.005f)
            return null;
        if (d.BlockRole != BlockRole.Core)
            return $"panel too large for this former: {a:0.##} x {b:0.##} m, the {d.Name} takes {along:0.##} x {across:0.##} m";
        var cell = d.BlockCell;
        int needA = (int)MathF.Ceiling(a / cell - 0.003f), needB = (int)MathF.Ceiling(b / cell - 0.003f);
        return $"panel too large for this bed: {a:0.##} x {b:0.##} m needs a {needA} x {needB} bed, this one is {nAlong} x {nAcross}";
    }

    /// <summary>A panel's two longest sides (its bounds), the longest first.</summary>
    public static void Sides(ConstructPartDef panel, out float longest, out float next)
    {
        var s = panel.BoundsMax - panel.BoundsMin;
        longest = MathF.Max(s.X, MathF.Max(s.Y, s.Z));
        next = s.X + s.Y + s.Z - longest - MathF.Min(s.X, MathF.Min(s.Y, s.Z));
    }

    /// <summary>Does a kind of former ever take a panel this size? (Its longest side within the machine's length, the next within its width: a Core's with all its Bed Blocks.)</summary>
    public static bool Fits(ConstructPartDef former, ConstructPartDef panel)
    {
        Sides(panel, out var a, out var b);
        return a <= former.ExtWheelMax.Y + 0.005f && b <= former.ExtWheelMax.X + 0.005f;
    }

    /// <summary>The smallest Panel Former that takes a panel, by name (for a bill), or null: none is big enough.</summary>
    public static string? TierFor(ConstructPartDef panel)
    {
        ConstructPartDef? best = null;
        foreach (var d in ConstructParts.All)
            if (d.IsPanelFormer && !d.IsLaserCutter && Fits(d, panel) && (best == null || d.ExtWheelMax.Y < best.ExtWheelMax.Y))
                best = d;
        return best?.Name;
    }

    /// <summary>Server: will this former take an order? Null, or why not.</summary>
    public static string? Refusal(ConstructWorld world, Construct c, int part, ConstructPartDef d, in WorkOrder o)
    {
        if (o.Name == null)
            return $"not a panel: the {d.Name} forms panels, {(o.IsPiece ? $"a {Extrusion.Label(o.Key)} is {WorkOrders.Maker(o.Key)}'s order" : $"{ConstructParts.Def(o.Part).Name} is an Assembler's order")}";
        if (o.Count is < 1 or > WorkOrders.MaxCount)
            return $"an order is for 1 to {WorkOrders.MaxCount} panels";
        if (!HullLoft.IsFormedName(o.Name) || ConstructParts.Find(o.Name) is not { } panel)
            return $"not a panel: nothing makes \"{o.Name}\"";
        if (!Fits(d, panel))
        {
            Sides(panel, out var a, out var b);
            return $"panel too large for any former: {a:0.##} x {b:0.##} m, the {d.Name} takes {d.ExtWheelMax.Y:0.##} x {d.ExtWheelMax.X:0.##} m";
        }
        if (BedRefusal(c, part, panel) is { } bed)
            return bed;
        var mat = HullLoft.FormedMaterial(o.Name);
        if (mat == 0 || ExtrusionTable.Current.Find(mat) == null)
            return $"the {d.Name} has no material for that panel";
        if (HullLoft.FormedUnits(panel, mat) > d.BulkCapacity)
            return $"that panel takes {HullLoft.FormedUnits(panel, mat)} {RecyclingTable.Current.NameOf(mat)}: more than the {d.Name}'s bin holds ({d.BulkCapacity})";
        if (c.Parts[part].Orders is { Count: >= WorkOrders.MaxOrders })
            return $"the {d.Name}'s queue is full ({WorkOrders.MaxOrders} orders)";
        if (world.StoreOf(c, part) is { Amount: > 0 } st && st.Material != mat)
            return $"the {d.Name} is fed {RecyclingTable.Current.NameOf(st.Material)}: a panel in {RecyclingTable.Current.NameOf(mat)} is another line";
        return null;
    }

    /// <summary>An order for a named panel (its material in the key, so the screens and the stalls read it as any order's).</summary>
    public static WorkOrder Order(string name, int count) => new()
    {
        Name = name,
        Key = new ExtrusionKey(0, 0, 0, 0, HullLoft.FormedMaterial(name), false, ExShape.Section),
        Part = ConstructParts.None,
        Count = (ushort)Math.Clamp(count, 1, WorkOrders.MaxCount),
    };

    /// <summary>The order its Queue button makes: its Preset setting at the preset's own size, in what its bin holds (steel when empty).</summary>
    public static string? FromSettings(Construct c, int part, ushort count, string mat, out WorkOrder o)
    {
        ref var p = ref c.Parts[part];
        var kind = (int)ConstructConfig.Get(p, p.D, CfgKey.Recipe, 0);
        var shape = HullLoft.PanelDefault(kind, mat);
        o = default;
        if (HullLoft.PanelRefusal(shape) is { } why)
            return why;
        o = Order(HullLoft.PanelKey(shape), count);
        return null;
    }

    /// <summary>Touch "orders.preset": the next preset (the Preset setting, as the configure tool would set it).</summary>
    public static string? NextPreset(ConstructWorld world, Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!d.IsPanelFormer || d.CfgSlot[(int)CfgKey.Recipe] < 0)
            return "this machine has no presets";
        var next = ((int)ConstructConfig.Get(p, d, CfgKey.Recipe, 0) + 1) % HullLoft.PanelPresets.Length;
        ConstructConfig.Put(ref p, d, CfgKey.Recipe, next);
        return null;
    }

    /// <summary>A step of one former (10 Hz, server; from ConstructExtruder.Step). It allocates nothing but the rider or the body a finished panel is.</summary>
    internal static void Work(ConstructWorld world, Construct c, int part, CStore st)
    {
        ref var p = ref c.Parts[part];
        var d = p.D;
        var held = ConstructExtruder.RunHeld(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (held || p.Broken)
        {
            Idle(world, st, ExtrudeStall.None);
            return;
        }
        if (!WorkOrders.HeadOf(p, out var head))
        {
            st.JobAmount = 0; // (a cancelled head's stroke is given up; what it took is not given back)
            st.JobTicks = 0;
            Idle(world, st, ExtrudeStall.NoOrders);
            return;
        }
        if (head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
        {
            Idle(world, st, ExtrudeStall.BadShape);
            return;
        }
        if (!ConstructRecycler.Powered(world, c, part))
        {
            Idle(world, st, ExtrudeStall.NoPower);
            return;
        }
        var mat = head.Key.Material;
        if (st.JobAmount == 0 && BedRefusal(c, part, panel) != null)
        {
            Idle(world, st, ExtrudeStall.OrderTooLong); // (no bed, no Out Table, or the panel is bigger than the bed: the look line says which)
            return;
        }
        if (st.JobAmount == 0)
        {
            // A new panel: its whole cost up front, so a stroke never starves half way.
            if (st.Amount > 0 && st.Material != mat)
            {
                Idle(world, st, ExtrudeStall.OrderMaterial);
                return;
            }
            var units = HullLoft.FormedUnits(panel, mat);
            if (st.Amount < units)
            {
                Idle(world, st, ExtrudeStall.NoMaterial);
                return;
            }
            world.Withdraw(c, part, units);
            st.JobAmount = units;
            st.JobTicks = 0;
            st.JobMaterial = mat;
        }
        var need = (int)(Seconds(d, panel) / MathF.Max(0.05f, d.ExtSpeed) * World.TickRate);
        if (st.JobTicks < need)
        {
            st.JobTicks += ConstructExtruder.Every;
            var pct = (byte)Math.Clamp(st.JobTicks * 100L / Math.Max(1, need), 0, 100);
            if (pct / 10 != st.Progress / 10)
            {
                st.Progress = pct; // (the screen's bar, sent in tenths)
                st.Version++;
                world.StoreDirty.Add(st.Handle);
            }
            ConstructExtruder.SetStall(world, st, ExtrudeStall.None);
            ConstructGatherer.SetBusy(world, st, true);
            return;
        }
        // Done: out by the conveyor, or onto the out bed as a loose part.
        if (ConstructConveyor.Out(c, part) is var conveyor and >= 0)
        {
            if (!ConstructConveyor.PutMade(world, c, conveyor, panel, 1, out var stall))
            {
                Idle(world, st, stall);
                return;
            }
        }
        else
        {
            c.WorldFrame(p.Seg, out var q, out var t);
            var turn = q * Rot24.Quat(p.Rot);
            var size = panel.BoundsMax - panel.BoundsMin;
            var spot = d.ExtDieAt + new Vector3(0, size.Y * 0.5f + 0.05f, 0); // (its middle over the out bed)
            var lay = turn;
            if (d.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) && asm.W > 0)
            {
                // On the block bed: its middle over the bed's middle, a little over the pins, laid along the bed's length.
                var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
                spot = new Vector3(o.X + (asm.X0 + (asm.W - 1) * 0.5f) * d.BlockCell, d.ExtDieAt.Y + 0.05f, o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * d.BlockCell);
                if (asm.OutSide is 0 or 1)
                    lay = turn * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
                spot -= Vector3.Transform((panel.BoundsMin + panel.BoundsMax) * new Vector3(0.5f, 0, 0.5f) + new Vector3(0, panel.BoundsMin.Y, 0), Quaternion.Conjugate(turn) * lay);
            }
            var at = t + Vector3.Transform(p.Pos, q) + Vector3.Transform(spot, turn);
            if (ConstructEdits.DropLoose(world, panel, at, lay) is not { } body)
            {
                Idle(world, st, ExtrudeStall.Blocked);
                return;
            }
            body.Owner = c.Owner;
            body.Vel = c.PointVel(at);
            ConstructCrafting.MadeLoose(world, c, part, panel); // (nothing carries it on: a terminal's order for it counts it here)
        }
        st.JobAmount = 0;
        st.JobTicks = 0;
        st.Progress = 0;
        WorkOrders.Made(world, c, p.Orders);
        ProductionLines.Made(world, c, part, 1);
        ConstructExtruder.SetStall(world, st, ExtrudeStall.None);
    }

    private static void Idle(ConstructWorld world, CStore st, ExtrudeStall why)
    {
        ConstructGatherer.SetBusy(world, st, false);
        ConstructExtruder.SetStall(world, st, why);
    }

    /// <summary>
    /// Server: a piece at the end of a conveyor into a former's Piece in. Extruded sheet (a section no thicker than <see cref="SheetMaxCm"/>)
    /// is counted into the bin at what it holds; anything else, another material than the bin's or more than its room waits on the conveyor.
    /// </summary>
    public static bool TakeFromConveyor(ConstructWorld world, Construct c, int part, ConstructPartDef piece)
    {
        var d = c.DefOf(part);
        if (!piece.IsExtrusion || piece.ExKey.Shape != ExShape.Section || Math.Min(piece.ExKey.W, piece.ExKey.H) > SheetMaxCm || world.StoreOf(c, part) is not { } st)
            return false;
        var mat = piece.ExKey.Material;
        var units = Math.Max(1, (int)MathF.Round(piece.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(mat))));
        if (st.Amount > 0 && st.Material != mat || st.Amount + units > d.BulkCapacity)
            return false;
        return world.Deposit(c, part, units, mat) > 0;
    }

    /// <summary>What its look line and screen say it lacks: "needs 96 steel for its panel, holds 40".</summary>
    public static string Lacks(Construct c, int part, CStore st)
    {
        if (!WorkOrders.HeadOf(c.Parts[part], out var head) || head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
            return "needs sheet";
        var names = RecyclingTable.Current;
        return $"needs {HullLoft.FormedUnits(panel, head.Key.Material)} {names.NameOf(head.Key.Material)} sheet for its panel, holds {st.Amount}";
    }

    /// <summary>A part file's <c>storage.panel</c> block: <c>{speed, at, maxWidth, maxLength, blockPower}</c> on a bin (what feeds it).</summary>
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        if (b.ValueKind != System.Text.Json.JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsExtruder)
            throw new ConstructPartException($"{file}: storage \"panel\" is an object (speed, at, maxWidth, maxLength) on a bin: what feeds it");
        ConstructParts.Known(b, ["speed", "at", "maxWidth", "maxLength", "blockPower"], $"{bw}.panel");
        if (ConstructParts.Opt(b, "blockPower") is { } bp)
        {
            d.ExtBlockPower = ConstructParts.Num(bp, file, "storage.panel.blockPower"); // (each Bed Block in its bed draws this while it works: ConstructExtruder.PowerOf)
            ConstructParts.Range(d.ExtBlockPower, 0, 1000000, file, "storage.panel.blockPower");
        }
        if (d.BulkMaterial != ConstructStorage.Any)
            throw new ConstructPartException($"{file}: a Panel Former's bin takes \"{ConstructStorage.Any}\" material");
        d.ExtSpeed = ConstructParts.Num(ConstructParts.Need(b, "speed", $"{bw}.panel"), file, "storage.panel.speed");
        ConstructParts.Range(d.ExtSpeed, 0.1f, 16, file, "storage.panel.speed");
        d.ExtDieAt = ConstructParts.Vec(ConstructParts.Need(b, "at", $"{bw}.panel"), file, "storage.panel.at");
        var w = ConstructParts.Num(ConstructParts.Need(b, "maxWidth", $"{bw}.panel"), file, "storage.panel.maxWidth");
        var l = ConstructParts.Num(ConstructParts.Need(b, "maxLength", $"{bw}.panel"), file, "storage.panel.maxLength");
        ConstructParts.Range(w, 0.2f, 32, file, "storage.panel.maxWidth");
        ConstructParts.Range(l, w, 32, file, "storage.panel.maxLength");
        d.ExtWheelMax = new Vector2(w, l);
        d.ExtWheelStart = new Vector2(0.2f, 0.2f);
        d.ExtDieSocket = -1; // (nothing grows on it: a panel is made whole)
        d.IsExtruder = d.IsWheelFormer = d.IsPanelFormer = true;
        d.DataIn = [ConstructGatherer.RunPort];
    }

    // ---- What the renderer reads (drawing 15's animations: drawn only, nothing on the server or the wire) ---------------------------

    public const float CycleSeconds = SetSeconds + PressSeconds; // one drawn stroke: the pins set (the first third), the platen presses

    /// <summary>Where a working former is in its drawn stroke, 0 to 1 (the clock's, so every client's beds move alike).</summary>
    public static float Phase(double now) => (float)(now / CycleSeconds % 1.0);

    /// <summary>The upper platen's travel at <paramref name="phase"/>, 0 (up) to 1 (down): up while the pins set, down, held, up.</summary>
    public static float PressAt(float phase)
    {
        const float set = SetSeconds / CycleSeconds;
        if (phase < set)
            return 0;
        if (phase < set + 0.1f)
            return Smooth((phase - set) / 0.1f);
        if (phase < 0.88f)
            return 1;
        return 1 - Smooth((phase - 0.88f) / 0.12f);
    }

    private static float Smooth(float t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    private static string? _pinName; // the order whose shape the pins show (its name, compared by reference: read once a change)
    private static HullLoft.PanelShape? _pinShape;

    /// <summary>
    /// How far a Bed Block's pin at <paramref name="at"/> (the body frame) stands up (m, at most <paramref name="most"/>) on Core
    /// <paramref name="core"/>'s bed now: in a wave from the Core's end while the pins set, to the height of the head order's panel over that
    /// pin (centred on the bed, its length along the bed's), held while the platen presses, then down. Allocates only when the order changes.
    /// </summary>
    public static float PinLift(Construct c, int core, Vector3 at, float most, double now)
    {
        if ((uint)core >= (uint)c.PartCount || !MachineBlocks.TryOf(c, core, out var asm) || asm.W == 0)
            return 0;
        ref readonly var p = ref c.Parts[core];
        var d = p.D;
        var cell = d.BlockCell;
        var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
        var rel = Rot24.Apply(Rot24Back(p.Rot), at - p.Pos) - o; // (the Core's frame, from its own cell's middle)
        // Along the bed (from the Core's end, 0 to 1) and across it (-1 to 1).
        float x0 = (asm.X0 - 0.5f) * cell, z0 = (asm.Z0 - 0.5f) * cell, w = asm.W * cell, l = asm.L * cell;
        float u, v, bedLong, bedWide;
        switch (asm.OutSide)
        {
            case 4: (u, v, bedLong, bedWide) = ((rel.Z - z0) / l, (rel.X - x0) / w * 2 - 1, l, w); break;
            case 5: (u, v, bedLong, bedWide) = (1 - (rel.Z - z0) / l, 1 - (rel.X - x0) / w * 2, l, w); break;
            case 0: (u, v, bedLong, bedWide) = ((rel.X - x0) / w, 1 - (rel.Z - z0) / l * 2, w, l); break;
            default: (u, v, bedLong, bedWide) = (1 - (rel.X - x0) / w, (rel.Z - z0) / l * 2 - 1, w, l); break;
        }
        // The shape: the head order's panel, laid with its length along the bed and centred on it.
        float height;
        if (WorkOrders.HeadOf(p, out var head) && head.Name is { } name)
        {
            if (!ReferenceEquals(name, _pinName))
                (_pinName, _pinShape) = (name, HullLoft.PanelOf(name));
            if (_pinShape is { } sh)
            {
                float pl = sh.L / 100f, pw = sh.Kind == HullLoft.PanelLoft ? MathF.Max(sh.W, sh.NoseW) / 100f : sh.W / 100f;
                var s = ((u - 0.5f) * bedLong + pl * 0.5f) / MathF.Max(0.01f, pl);
                var x = v * bedWide * 0.5f / MathF.Max(0.01f, pw * 0.5f);
                height = s is < 0 or > 1 || x is < -1 or > 1 ? 0.04f : 0.12f + HullLoft.PanelLift(sh, s, x); // (outside the sheet: barely up)
            }
            else
                height = 0.3f; // (a vehicle kit's loft: no key shape to read, an even rise)
        }
        else
            height = 0.3f;
        // The wave: each pin starts as the one before it is under way, from the Core's end; it holds while the platen presses.
        var ph = Phase(now);
        const float set = SetSeconds / CycleSeconds;
        var start = Math.Clamp(u, 0, 1) * set * 0.6f;
        var rise = Smooth((ph - start) / (set * 0.4f));
        if (ph > 0.9f)
            rise *= 1 - Smooth((ph - 0.9f) / 0.1f);
        return Math.Clamp(height, 0, most) * rise;
    }

    private static readonly byte[] _back = BackTurns();

    private static byte[] BackTurns()
    {
        var inv = new byte[Rot24.Count];
        for (byte r = 0; r < Rot24.Count; r++)
            for (byte q = 0; q < Rot24.Count; q++)
                if (Vector3.Distance(Rot24.Apply(q, Rot24.Apply(r, Vector3.UnitX)), Vector3.UnitX) < 1e-3f && Vector3.Distance(Rot24.Apply(q, Rot24.Apply(r, Vector3.UnitY)), Vector3.UnitY) < 1e-3f)
                {
                    inv[r] = q;
                    break;
                }
        return inv;
    }

    private static byte Rot24Back(byte r) => _back[r % Rot24.Count];
}
