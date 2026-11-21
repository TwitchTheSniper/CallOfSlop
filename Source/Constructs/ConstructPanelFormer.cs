using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Panel Former (docs/sandbox/constructs/production.md "The Panel Former"): a machine of the Extruder family that forms named panels
/// (HullLoft.Panels.cs: a player's <c>Formed Panel (...)</c>, or one of the vehicle kit's <c>Hull Loft (...)</c> parts) by work order only,
/// each from one <b>blank</b> (Sean, 2026-11-21): a flat extruded sheet of the panel's material and thickness, at least the panel's flat size
/// (its crown and rake unrolled) and at most <see cref="OverMost"/> over it each way (<see cref="BlankFits"/>). It has no stock: a blank comes
/// in by conveyor (or a Loader Arm) through Piece in and lies on its bed (<see cref="CStore.JobAmount"/>: the blank's units, its material in
/// <see cref="CStore.JobMaterial"/>, <see cref="CStore.OnBed"/> on the wire), as a Laser Cutter's does. A panel takes a timed stroke; done,
/// it rides out of Piece out on a conveyor, or drops on the bed, and the trim goes into its offcut bin (the store's own bulk; a chute empties it).
/// </summary>
public static class ConstructPanelFormer
{
    public const float OverMost = 1.25f; // a blank fits a panel up to this much over its flat size, each way (the trim is the offcut)

    public const float PinTravel = 2; // m: how far a Bed Block's pins rise with no masts, and how much more each level of corner masts adds
    public const int MastLevelsMost = 1; // levels of Mast Blocks on the bed's corners that deepen the stroke (4 m: past the deepest panel, HullLoft's 80 cm crown and 2 m rake)

    /// <summary>
    /// How deep a panel (crown and rake) the bed forms: <see cref="PinTravel"/>, and as much again for each level of Mast Blocks standing on
    /// all four of the bed's corner cells (the shortest corner column counts, as an oven's masts do), up to <see cref="MastLevelsMost"/>.
    /// </summary>
    public static float PinDepth(Construct c, in CMachineAsm asm) => PinTravel * (1 + Math.Min(MastLevelsMost, MachineBlocks.CornerColumns(c, asm)));

    /// <summary>A panel's depth: the shortest of its bounds' three sides.</summary>
    public static float Depth(ConstructPartDef panel) =>
        MathF.Min(panel.BoundsMax.X - panel.BoundsMin.X, MathF.Min(panel.BoundsMax.Y - panel.BoundsMin.Y, panel.BoundsMax.Z - panel.BoundsMin.Z));
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

    /// <summary>Server, per step: does this former's bed take <paramref name="panel"/> now? <see cref="BedRefusal"/>'s test with no text (no allocation).</summary>
    public static bool BedFits(Construct c, int part, ConstructPartDef panel)
    {
        var d = c.Parts[part].D;
        Bed(c, part, out var along, out var across, out _, out var nAcross);
        Sides(panel, out var a, out var b);
        if (d.BlockRole == BlockRole.Core)
        {
            if (along <= 0 || !MachineBlocks.TryOf(c, part, out var asm) || asm.Out == ushort.MaxValue || asm.OutCells < nAcross)
                return false;
            if (Depth(panel) > PinDepth(c, asm) + 0.005f)
                return false;
        }
        return a <= MathF.Max(along, across) + 0.005f && b <= MathF.Min(along, across) + 0.005f;
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
            var deep = Depth(panel);
            var reach = PinDepth(c, asm);
            if (deep > reach + 0.005f)
            {
                return deep > PinTravel * (1 + MastLevelsMost) + 0.005f
                    ? $"panel too deep for the pins: {deep:0.##} m of crown and rake, the deepest a bed forms is {PinTravel * (1 + MastLevelsMost):0} m"
                    : $"panel too deep for the pins: {deep:0.##} m of crown and rake, the pins rise {reach:0} m: stand a Mast Block on each of the bed's four corners for {PinTravel * (1 + MastLevelsMost):0} m";
            }
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
        if (c.Parts[part].Orders is { Count: >= WorkOrders.MaxOrders })
            return $"the {d.Name}'s queue is full ({WorkOrders.MaxOrders} orders)";
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
        if (st.OnBed != st.JobAmount > 0)
        {
            st.OnBed = st.JobAmount > 0; // (after a load: the saved job says a blank lies on the bed; the wire carries OnBed)
            st.Version++;
            world.StoreDirty.Add(st.Handle);
        }
        var held = ConstructExtruder.RunHeld(c, part);
        ConstructGatherer.SetHeld(world, st, held);
        if (held || p.Broken)
        {
            Idle(world, st, ExtrudeStall.None);
            return;
        }
        if (!WorkOrders.HeadOf(p, out var head))
        {
            st.JobTicks = 0; // (a blank on the bed stays there for the next order it fits)
            Idle(world, st, ExtrudeStall.NoOrders);
            return;
        }
        if (head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
        {
            Idle(world, st, ExtrudeStall.BadShape);
            return;
        }
        if (st.JobAmount == 0 && !BedFits(c, part, panel))
        {
            Idle(world, st, ExtrudeStall.OrderTooLong); // (no bed, no Out Table, or the panel is bigger than the bed: the look line says which)
            return;
        }
        if (st.JobAmount == 0)
        {
            Idle(world, st, ExtrudeStall.NoMaterial); // (waiting for its blank on Piece in: Lacks names it)
            return;
        }
        if (!ConstructRecycler.Powered(world, c, part))
        {
            Idle(world, st, ExtrudeStall.NoPower); // (the blank stays on the bed, the stroke where it stopped)
            return;
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
        // Done. The trim first: what the blank held over the panel goes into the offcut bin whole, or the panel waits on the bed.
        var mat = st.JobMaterial;
        var trim = Math.Max(0, st.JobAmount - HullLoft.FormedUnits(panel, mat));
        if (!BinTakes(c, part, st, mat, trim))
        {
            Idle(world, st, ExtrudeStall.OffcutFull);
            return;
        }
        // Out by the conveyor, or onto the bed as a loose part.
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
        if (trim > 0)
            world.Deposit(c, part, trim, mat);
        st.JobAmount = 0;
        st.OnBed = false;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        WorkOrders.Made(world, c, p.Orders);
        ProductionLines.Made(world, c, part, 1);
        ConstructExtruder.SetStall(world, st, ExtrudeStall.None);
        ConstructGatherer.SetBusy(world, st, false);
    }

    /// <summary>Would the offcut bin take <paramref name="n"/> units of <paramref name="mat"/> now (empty, or holding that material, with room)?</summary>
    private static bool BinTakes(Construct c, int part, CStore st, byte mat, int n) =>
        n == 0 || (st.Amount == 0 || st.Material == mat) && st.Amount + n <= ConstructStorage.Capacity(c, part);

    private static void Idle(ConstructWorld world, CStore st, ExtrudeStall why)
    {
        ConstructGatherer.SetBusy(world, st, false);
        ConstructExtruder.SetStall(world, st, why);
    }

    // ---- Blanks (Sean, 2026-11-21: "a blank sheet approximately sized for what it's trying to bend or form") --------------------

    /// <summary>A panel's flat size: its length and width unrolled (m), the blank's thickness (whole cm) and its material.</summary>
    public readonly record struct Flat(float Length, float Width, int ThickCm, byte Material);

    private static readonly Dictionary<string, Flat> _flats = new(StringComparer.Ordinal);

    /// <summary>
    /// The flat size of a formed part (read once a name and kept: what the step, the conveyor and the screens read with no allocation). A
    /// preset or designer panel: its surface measured along its length and across its width (the longest of three lines each way), its
    /// thickness its key's mm to the nearest cm (at least 1). A kit's loft, which has no key shape: its two longest sides, and the thickness
    /// its mass gives over that area. False: not a formed part, or its material has no Extrusion row.
    /// </summary>
    public static bool FlatOf(ConstructPartDef panel, out Flat f)
    {
        if (_flats.TryGetValue(panel.Name, out f))
            return f.Material != 0;
        f = MeasureFlat(panel);
        _flats[panel.Name] = f;
        return f.Material != 0;
    }

    private static Flat MeasureFlat(ConstructPartDef panel)
    {
        if (!HullLoft.IsFormedName(panel.Name) || HullLoft.IsCutName(panel.Name))
            return default;
        var mat = HullLoft.FormedMaterial(panel.Name);
        if (mat == 0 || ExtrusionTable.Current.Find(mat) is not { } row)
            return default;
        if (HullLoft.PanelOf(panel.Name) is { } sh)
        {
            HullLoft.PanelFlat(sh, out var along, out var across);
            return new Flat(along, across, Math.Clamp((int)MathF.Round(sh.Mm / 10f, MidpointRounding.AwayFromZero), Extrusion.MinSheetCm, Extrusion.SheetMaxCm), mat);
        }
        Sides(panel, out var a, out var b);
        var volume = panel.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(mat)) / MathF.Max(1, row.UnitsPerM3);
        var cm = (int)MathF.Round(volume / MathF.Max(0.01f, a * b) * 100);
        return new Flat(a, b, Math.Clamp(cm, Extrusion.MinSheetCm, Extrusion.SheetMaxCm), mat);
    }

    private static int Step(float m) => Math.Clamp((int)MathF.Ceiling(m * 100 / Extrusion.StepCm - 0.01f) * Extrusion.StepCm, Extrusion.MinSectionCm, Extrusion.MaxLengthCm);

    /// <summary>
    /// The blank a panel's order asks an Extruder for: a solid sheet (the Extruder's Sheet profile), its width across the panel's flat width,
    /// its length along the flat length, each the next 5 cm step up, the panel's thickness and material. Default when it has no flat size.
    /// </summary>
    public static ExtrusionKey BlankFor(ConstructPartDef panel)
    {
        if (!FlatOf(panel, out var f))
            return default;
        return new ExtrusionKey(ulong.MaxValue, (ushort)Math.Min(Step(f.Width), Extrusion.MaxSectionCm), (ushort)f.ThickCm, (ushort)Step(f.Length), f.Material, false, ExShape.Section);
    }

    /// <summary>Is this piece a blank for this panel? A solid sheet of its material and thickness, its two sides each from the flat size to <see cref="OverMost"/> over it (either way round).</summary>
    public static bool BlankFits(ConstructPartDef piece, ConstructPartDef panel)
    {
        if (!piece.IsExtrusion || piece.ExKey is var k && !Extrusion.IsSheet(k) || !FlatOf(panel, out var f))
            return false;
        if (k.Material != f.Material || k.H != f.ThickCm)
            return false;
        return Side(k.W, f.Width) && Side(k.L, f.Length) || Side(k.L, f.Width) && Side(k.W, f.Length);
    }

    // (a side fits from the flat size, less half a cm of rounding, to 25% over it or the next 5 cm step, whichever is more: a 20 cm panel takes a 25 cm sheet)
    private static bool Side(int cm, float m) => cm >= m * 100 - 0.5f && cm <= MathF.Max(m * 100 * OverMost, Step(m)) + 0.5f;

    /// <summary>The first open order in the former's queue whose panel this blank fits, or -1 (not only the head's: the blank that comes first is formed first).</summary>
    private static int OrderFor(Construct c, int part, ConstructPartDef piece)
    {
        if (!piece.IsExtrusion || c.Parts[part].Orders is not { Bill: false, Count: > 0 } q)
            return -1;
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Left > 0 && q.Items[k].Name is { } n && ConstructParts.Find(n) is { } panel && BlankFits(piece, panel) && BedFits(c, part, panel))
                return k;
        return -1;
    }

    /// <summary>Server: does this former want this piece now (its bed empty, the piece a blank for one of its orders)? A Router or an arm sends it one then.</summary>
    public static bool Wants(ConstructWorld world, Construct c, int part, ConstructPartDef piece) =>
        world.StoreOf(c, part) is { JobAmount: 0 } && OrderFor(c, part, piece) >= 0;

    /// <summary>
    /// Server: a piece at the end of a conveyor into a former's Piece in (or set in by a Loader Arm). Only a blank for one of its orders is
    /// taken, and only onto an empty bed: its order goes to the head, and the blank lies on the bed whole. Anything else waits on the conveyor.
    /// </summary>
    public static bool TakeFromConveyor(ConstructWorld world, Construct c, int part, ConstructPartDef piece)
    {
        if (world.StoreOf(c, part) is not { JobAmount: 0 } st || OrderFor(c, part, piece) is not (var at and >= 0))
            return false;
        if (at > 0)
        {
            // Its order goes to the head (the bed is empty: nothing is under way), so the blank is formed to the order it fits.
            var q = c.Parts[part].Orders!;
            var mine = q.Items[at];
            Array.Copy(q.Items, 0, q.Items, 1, at);
            q.Items[0] = mine;
            world.OrdersChanged(c);
        }
        st.JobAmount = Math.Max(1, (int)MathF.Round(piece.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(piece.ExKey.Material))));
        st.JobMaterial = piece.ExKey.Material;
        st.OnBed = true;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        return true;
    }

    /// <summary>
    /// Server: the head order is going (Cancel, Clear) with a blank on the bed and no stroke done: the blank is sheared up into the offcut bin
    /// (as much as it has room for), so it is never formed to an order it wasn't taken for.
    /// </summary>
    public static void GiveBack(ConstructWorld world, Construct c, int part)
    {
        if (world.StoreOf(c, part) is not { JobAmount: > 0 } st)
            return;
        var mat = st.JobMaterial;
        var room = st.Amount == 0 || st.Material == mat ? Math.Max(0, ConstructStorage.Capacity(c, part) - st.Amount) : 0;
        if (room > 0)
            world.Deposit(c, part, Math.Min(room, st.JobAmount), mat);
        st.JobAmount = 0;
        st.OnBed = false;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        ConstructGatherer.SetBusy(world, st, false);
    }

    /// <summary>
    /// Server: orders queued at the former itself (its Queue button) ask a linked Extruder for their blanks, as a machine order (the blank
    /// rides plainly and the former takes it for the order it fits). The maker, or null: none on its network makes that sheet.
    /// </summary>
    public static string? AskBlanks(ConstructWorld world, Construct c, int part, in WorkOrder o)
    {
        if (o.Name == null || ConstructParts.Find(o.Name) is not { } panel || BlankFor(panel) is not { W: > 0 } key)
            return null;
        var order = new WorkOrder { Key = key, Part = ConstructParts.None, Count = o.Count };
        var net = ConstructCrafting.NetOf(world, c, part);
        for (var i = 0; i < net.MachineN * 2; i++)
        {
            // (two passes: first the Extruders with a conveyor way to this former, then any: a terminal's ask ranks them the same way)
            var m = net.Machines[i % net.MachineN];
            if ((i < net.MachineN) != net.MachineWay[i % net.MachineN])
                continue;
            if (m != part && ConstructLogisticsPlan.IsSheetExtruder(c.DefOf(m)) && WorkOrders.Queue(world, c, m, order) == null)
                return c.DefOf(m).Name;
        }
        return null;
    }

    /// <summary>The head order's blank, for the screen and the look line: "1.6 x 1.55 m steel blank, 2 cm". Allocates (the text).</summary>
    public static string BlankText(ConstructPartDef panel) =>
        FlatOf(panel, out var f) ? $"{f.Length:0.##} x {f.Width:0.##} m {RecyclingTable.Current.NameOf(f.Material)} blank, {f.ThickCm} cm" : "a blank";

    /// <summary>What its look line and screen say it lacks: "needs a 1.6 x 1.55 m steel blank, 2 cm (up to 25% over), on Piece in".</summary>
    public static string Lacks(Construct c, int part, CStore st)
    {
        if (!WorkOrders.HeadOf(c.Parts[part], out var head) || head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
            return "needs its blank";
        var how = c.RoutesInto(part, RouteType.Conveyor) > 0 ? "" : ": only a conveyor or an arm into its Piece in brings it (a hand can't load the bed)";
        return $"needs a {BlankText(panel)} (up to {(OverMost - 1) * 100:0}% over each way) on Piece in{how}";
    }
    /// <summary>A part file's <c>storage.panel</c> block: <c>{speed, at, maxWidth, maxLength, blockPower}</c> on a bin: its offcut (no stock: a blank a panel, ConstructPanelFormer.TakeFromConveyor).</summary>
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        if (b.ValueKind != System.Text.Json.JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsExtruder)
            throw new ConstructPartException($"{file}: storage \"panel\" is an object (speed, at, maxWidth, maxLength) on a bin: its offcut");
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

    // The orders whose shapes the pins show (names compared by reference, read once a change): a few slots, so formers working different
    // panels in one view don't read their keys again every frame.
    private static readonly string?[] _pinName = new string?[8];
    private static readonly HullLoft.PanelShape?[] _pinShape = new HullLoft.PanelShape?[8];
    private static int _pinNext;

    private static HullLoft.PanelShape? PinShape(string name)
    {
        for (var k = 0; k < _pinName.Length; k++)
            if (ReferenceEquals(_pinName[k], name))
                return _pinShape[k];
        var at = _pinNext;
        _pinNext = (_pinNext + 1) % _pinName.Length;
        (_pinName[at], _pinShape[at]) = (name, HullLoft.PanelOf(name));
        return _pinShape[at];
    }

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
            if (PinShape(name) is { } sh)
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

    private static readonly byte _quarter = QuarterTurn();

    private static byte QuarterTurn()
    {
        for (byte q = 0; q < Rot24.Count; q++)
            if (Vector3.Distance(Rot24.Apply(q, Vector3.UnitZ), Vector3.UnitX) < 1e-3f && Vector3.Distance(Rot24.Apply(q, Vector3.UnitY), Vector3.UnitY) < 1e-3f)
                return q;
        return 0;
    }

    /// <summary>
    /// What lies on a former's bed (its look, drawn only): the blank, flat on the pins at rest, until the drawn stroke's pins have set; then
    /// the head order's panel, over the risen pins, until the platen lifts again. Its def, its turn and its origin in the Core's frame, laid
    /// with its length along the bed and centred on it. Allocates only the first time a blank's key is seen (Extrusion.Def keeps it).
    /// </summary>
    public static bool BedLook(Construct c, int part, CStore st, double now, out ConstructPartDef what, out byte rot, out Vector3 at)
    {
        (what, rot, at) = (null!, 0, default);
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!st.OnBed || d.BlockRole != BlockRole.Core || !MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0 || !WorkOrders.HeadOf(p, out var head)
            || head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
            return false;
        var ph = Phase(now);
        var formed = st.Busy && ph >= SetSeconds / CycleSeconds * 0.8f && ph < 0.92f; // (the wave is nearly up: the sheet lies bent over it)
        var def = formed ? panel : Extrusion.Def(BlankFor(panel));
        if (def == null)
            return false;
        what = def;
        rot = asm.OutSide is 0 or 1 ? _quarter : (byte)0; // (its length along the bed: z, or x on a bed that runs along x)
        var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
        var mid = new Vector3(o.X + (asm.X0 + (asm.W - 1) * 0.5f) * d.BlockCell, d.ExtDieAt.Y, o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * d.BlockCell);
        var foot = (def.BoundsMin + def.BoundsMax) * 0.5f;
        foot.Y = def.BoundsMin.Y;
        var lift = formed ? 0.12f + 0.006f : 0.004f; // (over the pins' tops: a formed panel rides on the risen wave, PinLift's 12 cm base)
        at = mid + new Vector3(0, lift, 0) - Rot24.Apply(rot, foot);
        return true;
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
