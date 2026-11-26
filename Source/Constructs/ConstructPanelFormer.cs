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

    /// <summary>A panel's depth: the shortest of its bounds' three sides.</summary>
    public static float Depth(ConstructPartDef panel) =>
        MathF.Min(panel.BoundsMax.X - panel.BoundsMin.X, MathF.Min(panel.BoundsMax.Y - panel.BoundsMin.Y, panel.BoundsMax.Z - panel.BoundsMin.Z));
    public const float StrokeSeconds = 12; // a stroke on the block bed: the jaws grip, the die rises and wraps the sheet, holds, and lets go

    /// <summary>
    /// Seconds a panel takes on a former of speed 1. On the block bed (a Core) the blocks work together, so every stroke is the same: the
    /// jaws grip the blank, the die rises and stretches it, holds and lets go (<see cref="Pose"/>). An older former's: 4 s and 2 s a square metre of its largest face.
    /// </summary>
    public static float Seconds(ConstructPartDef former, ConstructPartDef panel)
    {
        if (former.BlockRole == BlockRole.Core)
            return StrokeSeconds;
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
        }
        return a <= MathF.Max(along, across) + 0.005f && b <= MathF.Min(along, across) + 0.005f;
    }

    /// <summary>
    /// Server and client: why this former can't form <paramref name="panel"/> now, or null. A Core: no bed, no Out Table on its far end, a
    /// panel bigger than its bed (in blocks: "needs a 4 x 3 bed, this one is 3 x 2"). Any depth: the die sinks into a pocket as deep as its shape needs.
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
            return $"not a panel: the {d.Name} forms panels, {(o.IsPiece ? $"a {Extrusion.Label(o.Key)} is {WorkOrders.Maker(o.Key)}'s order" : $"{ConstructParts.Def(o.Part).Name} is {WorkOrders.CatalogueMaker(o)}'s order")}";
        if (o.Count is < 1 or > WorkOrders.MaxCount)
            return $"an order is for 1 to {WorkOrders.MaxCount} panels";
        if (HullLoft.IsSectionName(o.Name) && !HullLoft.IsPlainSectionName(o.Name))
            return HullLoft.SectionOf(o.Name, out var swhy) is { } dressed
                ? $"a dressed hull section: the {d.Name} forms its plain skin, {HullLoft.PlainOf(dressed)}; a Building Station's Dress skins job dresses it"
                : $"not a hull section: {swhy}";
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
        if (FlatOf(panel, out var flat) && (Math.Min(flat.Width, flat.Length) * 100 > Extrusion.MaxSectionCm || Math.Max(flat.Width, flat.Length) * 100 > Extrusion.MaxLengthCm))
            return $"no blank that large: the panel lies flat at {flat.Length:0.##} x {flat.Width:0.##} m, a sheet is {Extrusion.MaxSectionCm / 100} m wide at most";
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
            // (a blank whose order went another way, a terminal's cancel or the planner's drop: sheared into the offcut bin)
            if (st.JobAmount > 0 && !GiveBack(world, c, part))
            {
                Idle(world, st, ExtrudeStall.OffcutFull);
                return;
            }
            st.JobTicks = 0;
            Idle(world, st, ExtrudeStall.NoOrders);
            return;
        }
        if (head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
        {
            Idle(world, st, ExtrudeStall.BadShape);
            return;
        }
        if (st.JobAmount > 0 && st.JobTicks == 0 && !BlankServes(st, panel))
        {
            // The head isn't the order this blank was taken for (its order was cancelled at a terminal, or dropped by the planner, which
            // don't go through Cancel): never form it from the wrong blank. Into the offcut bin, and the head asks its own.
            if (!GiveBack(world, c, part))
            {
                Idle(world, st, ExtrudeStall.OffcutFull);
                return;
            }
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
                // (a hull skin lies turned, its shallowest side up: LayOf; its foot is read in that turn)
                var own = LayOf(panel);
                LaidBounds(panel, own, out var lmin, out var lmax);
                spot -= Vector3.Transform((lmin + lmax) * new Vector3(0.5f, 0, 0.5f) + new Vector3(0, lmin.Y, 0), Quaternion.Conjugate(turn) * lay);
                lay *= Rot24.Quat(own);
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
        if (HullLoft.IsSectionName(panel.Name))
        {
            // A plain hull skin: its facets unrolled side by side, its 4 cm plate (HullLoft.Dress.cs PlainJson weighs it so).
            if (HullLoft.SectionOf(panel.Name, out _) is not { Plain: true } sec)
                return default;
            HullLoft.SectionFlat(sec, out var along, out var across, out _);
            return new Flat(along, across, HullLoft.SectionSkinCm, mat);
        }
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

    private static readonly Dictionary<string, byte> _lays = new(StringComparer.Ordinal);

    /// <summary>
    /// The turn a formed part lies on the bed in (Rot24, before the bed's own): 0 for a panel (formed lying flat, its depth up); a plain hull skin
    /// (which is made standing as it sits on the hull) turned so its shallowest side is up and its longest along the bed, its outer face up so
    /// the pins rise inside its arch. Kept per name (the draw reads it every frame).
    /// </summary>
    public static byte LayOf(ConstructPartDef panel)
    {
        if (!HullLoft.IsSectionName(panel.Name))
            return 0;
        if (_lays.TryGetValue(panel.Name, out var r))
            return r;
        r = 0;
        if (HullLoft.SectionOf(panel.Name, out _) is { } sec)
        {
            HullLoft.SectionFlat(sec, out _, out _, out var outward);
            var size = panel.BoundsMax - panel.BoundsMin;
            int low = size.X <= size.Y && size.X <= size.Z ? 0 : size.Y <= size.Z ? 1 : 2;
            int high = size.X > size.Y && size.X > size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            if (high == low)
                high = (low + 2) % 3;
            Vector3 Axis(int i) => i == 0 ? Vector3.UnitX : i == 1 ? Vector3.UnitY : Vector3.UnitZ;
            var best = float.MinValue;
            for (byte q = 0; q < Rot24.Count; q++)
            {
                if (MathF.Abs(Rot24.Apply(q, Axis(low)).Y) < 0.99f || MathF.Abs(Rot24.Apply(q, Axis(high)).Z) < 0.99f)
                    continue;
                var up = Rot24.Apply(q, outward).Y;
                if (up > best)
                    (best, r) = (up, q);
            }
        }
        _lays[panel.Name] = r;
        return r;
    }

    /// <summary>A formed part's bounds turned as it lies on the bed (<see cref="LayOf"/>).</summary>
    private static void LaidBounds(ConstructPartDef panel, byte lay, out Vector3 min, out Vector3 max)
    {
        if (lay == 0)
        {
            (min, max) = (panel.BoundsMin, panel.BoundsMax);
            return;
        }
        var mid = Rot24.Apply(lay, (panel.BoundsMin + panel.BoundsMax) * 0.5f);
        var half = Rot24.ApplyHalf(lay, (panel.BoundsMax - panel.BoundsMin) * 0.5f);
        (min, max) = (mid - half, mid + half);
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
        // (the narrower side across the die, the longer one along the run: the smallest die that makes it)
        float across = MathF.Min(f.Width, f.Length), along = MathF.Max(f.Width, f.Length);
        return new ExtrusionKey(ulong.MaxValue, (ushort)Math.Min(Step(across), Extrusion.MaxSectionCm), (ushort)f.ThickCm, (ushort)Step(along), f.Material, false, ExShape.Section);
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

    /// <summary>Does any queued order's panel take this blank (a run feeding the bed asks: ConstructConveyor.PastBed)?</summary>
    internal static bool Takes(Construct c, int part, ConstructPartDef piece) => c.DefOf(part).IsLaserCutter ? ConstructLaserCutter.Takes(c, part, piece) : AnyOrderTakes(c, part, piece);

    /// <summary>Does any queued order's panel take this blank (bed or no bed)? A piece no order takes is never waited for.</summary>
    private static bool AnyOrderTakes(Construct c, int part, ConstructPartDef piece)
    {
        if (c.Parts[part].Orders is not { Count: > 0 } q)
            return false;
        for (var k = 0; k < q.Count; k++)
            if (q.Items[k].Left > 0 && q.Items[k].Name is { } n && ConstructParts.Find(n) is { } panel && BlankFits(piece, panel))
                return true;
        return false;
    }

    /// <summary>
    /// Server: a piece at Piece in that no order takes (its order cancelled after the Extruder was asked, or an older world's sheet) is
    /// sheared into the offcut bin, so it never blocks the line; it waits only while the bin can't take it.
    /// </summary>
    private static bool Scrap(ConstructWorld world, Construct c, int part, CStore st, ConstructPartDef piece)
    {
        var mat = piece.ExKey.Material;
        var units = Math.Max(1, (int)MathF.Round(piece.Mass / MathF.Max(0.01f, RecyclingTable.Current.WeightOf(mat))));
        if (!BinTakes(c, part, st, mat, units))
            return false;
        world.Deposit(c, part, units, mat);
        Console.WriteLine($"[former] {c.DefOf(part).Name} #{part}: no order takes {piece.Name}: sheared into the offcut bin ({units} units)");
        return true;
    }

    /// <summary>Server: does this former want this piece now (its bed empty, the piece a blank for one of its orders)? A Router or an arm sends it one then.</summary>
    public static bool Wants(ConstructWorld world, Construct c, int part, ConstructPartDef piece) =>
        world.StoreOf(c, part) is { JobAmount: 0 } && OrderFor(c, part, piece) >= 0;

    /// <summary>
    /// Server: a piece at the end of a conveyor into a former's Piece in (or set in by a Loader Arm). Only a blank for one of its orders is
    /// taken, and only onto an empty bed: its order goes to the head, and the blank lies on the bed whole. Anything else waits on the conveyor.
    /// </summary>
    public static bool TakeFromConveyor(ConstructWorld world, Construct c, int part, ConstructPartDef piece, bool scrap = false)
    {
        if (world.StoreOf(c, part) is not { } st)
            return false;
        if (scrap && piece.IsExtrusion && !AnyOrderTakes(c, part, piece))
            return Scrap(world, c, part, st, piece);
        if (st.JobAmount != 0 || OrderFor(c, part, piece) is not (var at and >= 0))
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
    public static bool GiveBack(ConstructWorld world, Construct c, int part)
    {
        if (world.StoreOf(c, part) is not { JobAmount: > 0 } st)
            return true;
        var mat = st.JobMaterial;
        if (!BinTakes(c, part, st, mat, st.JobAmount))
            return false; // (the bin is full or holds another material: the blank stays on the bed, the look line says "offcut bin full")
        world.Deposit(c, part, st.JobAmount, mat);
        st.JobAmount = 0;
        st.OnBed = false;
        st.JobTicks = 0;
        st.Progress = 0;
        st.Version++;
        world.StoreDirty.Add(st.Handle);
        ConstructGatherer.SetBusy(world, st, false);
        return true;
    }

    /// <summary>Could the blank on the bed (its units and material) be the blank for this panel: its material, from the panel's units to what a blank 25% over each way holds?</summary>
    private static bool BlankServes(CStore st, ConstructPartDef panel)
    {
        if (!FlatOf(panel, out var f) || f.Material != st.JobMaterial)
            return false;
        var units = HullLoft.FormedUnits(panel, f.Material);
        return st.JobAmount * 1.1f + 1 >= units && st.JobAmount <= units * OverMost * OverMost * 1.15f + 2; // (rounding either way: a blank's units round, a panel's go up; a blank holds more than the curved panel, its flat sheet in 5 cm steps)
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

    // ---- What the renderer reads (the stretch former's animation: drawn only, nothing on the server or the wire) ----------------------

    /// <summary>
    /// Where a former is in its look (Sean, 2026-11-24: the Panel Former Lab's stretch former), worked out from its store each frame:
    /// a stroke runs from when the drawing first saw it at work (or back from its Progress, joined mid-stroke); a changeover runs when the
    /// head order's panel shape is not the one the die shows. Stage 0 ready (or idle; in a stroke, the blank rolling in), 1 the jaws grip,
    /// 2 the die rises and wraps the sheet, 3 it holds, 4 it lets go and sinks, 5 the panel rolls out; 6 a changeover (<see cref="Morph"/>
    /// 0 to 1). All values 0 to 1 but <see cref="Slide"/>.
    /// </summary>
    public struct FormPose
    {
        public int Stage;
        public float U; // the stage's own progress, eased
        public float Rise; // the die: 0 at rest under the roll line, 1 its lowest point at the roll line
        public float Grip; // the jaws in use: 0 open at their ready spot, 1 closed on the sheet's edge
        public float Hold; // the jaws ride with the sheet's edge height (1) or are back down at the roll line (0)
        public float Shape; // the sheet: 0 the flat blank, 1 the formed panel (the die's shape exactly)
        public float Morph; // a changeover's progress, or -1
        public float Slide; // where the sheet's middle is along the bed: -1 at its in end (rolling in), 0 its middle, 1 its out end (rolling out)
        public DieInfo? Die, From; // the shape the die shows and, in a changeover, the one it leaves
    }

    /// <summary>
    /// The fractions of a stroke each stage ends at, as the Lab runs it: the blank rolls in, the jaws grip, the die wraps, holds, lets go and
    /// sinks, and the panel rolls out to the bed's out end, where the server hands it to the out run.
    /// </summary>
    public const float RollEnd = 0.15f, GripEnd = 0.25f, WrapEnd = 0.6f, HoldEnd = 0.7f, ReleaseEnd = 0.85f;
    public const float ChangeSeconds = 4; // a changeover between two panels' shapes: the columns drop, wave to the new shape and the jaws set
    public const float DieBelow = 0.05f; // the die's highest point at rest, under the roll line

    private static float Smooth(float t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    /// <summary>
    /// The former's pose now (client draw; also what <see cref="BedLook"/> lays the sheet by). Keeps its clocks on the store (never sent).
    /// Allocates only when a new order's shape is first read (<see cref="Die"/>).
    /// </summary>
    public static FormPose Pose(Construct c, int core, CStore st, double now)
    {
        var d = c.Parts[core].D;
        var head = WorkOrders.HeadOf(c.Parts[core], out _);
        var shaped = head && Die(c, core, out var die) ? die : null;
        if (st.StrokeAt != now)
        {
            st.StrokeAt = now;
            // The shape the die shows: a new head order's starts a changeover (at once while it works: a client that joined mid-stroke).
            if (head && (!st.FormSeen || !ReferenceEquals(shaped, st.FormDie)))
            {
                (st.FormDieFrom, st.FormDie) = (st.FormDie, shaped);
                st.FormChangeAt = !st.FormSeen || st.Busy ? -1e9 : now;
                st.FormSeen = true;
            }
            var dur = StrokeSeconds / MathF.Max(0.05f, d.ExtSpeed);
            if (st.Busy && (!st.StrokeOn || st.Progress + 10 < st.StrokeSeen))
            {
                (st.StrokeOn, st.StrokeSince) = (true, now - st.Progress / 100.0 * dur);
                st.FormRollsOut = ConstructConveyor.Out(c, core) >= 0; // (with no out run the server drops the panel on the bed's middle: it stays there)
            }
            else if (!st.Busy)
                st.StrokeOn = false;
            st.StrokeSeen = st.Progress;
            st.StrokeEnd = dur;
        }
        var pose = new FormPose { Morph = -1, Die = st.FormDie };
        if (st.StrokeOn)
        {
            var ph = (float)Math.Clamp((now - st.StrokeSince) / Math.Max(0.1, st.StrokeEnd), 0, 1);
            (pose.Stage, pose.U) = ph < RollEnd ? (0, ph / RollEnd) : ph < GripEnd ? (1, (ph - RollEnd) / (GripEnd - RollEnd))
                : ph < WrapEnd ? (2, (ph - GripEnd) / (WrapEnd - GripEnd)) : ph < HoldEnd ? (3, (ph - WrapEnd) / (HoldEnd - WrapEnd))
                : ph < ReleaseEnd ? (4, (ph - HoldEnd) / (ReleaseEnd - HoldEnd)) : (5, (ph - ReleaseEnd) / (1 - ReleaseEnd));
            pose.U = Smooth(pose.U);
            pose.Slide = pose.Stage == 0 ? pose.U - 1 : pose.Stage == 5 && st.FormRollsOut ? pose.U : 0;
        }
        else if (st.OnBed && st.Progress >= 100)
            (pose.Stage, pose.U, pose.Slide) = (5, 1, st.FormRollsOut ? 1 : 0); // (formed, waiting at the out end: its run out is full)
        else if (now - st.FormChangeAt < ChangeSeconds)
        {
            pose.Stage = 6;
            pose.Morph = Smooth((float)((now - st.FormChangeAt) / ChangeSeconds));
            pose.From = st.FormDieFrom;
        }
        var u = pose.U;
        pose.Rise = pose.Stage switch { 2 => u, 3 => 1, 4 => 1 - u, _ => 0 };
        pose.Grip = pose.Stage switch { 1 => u, 2 or 3 => 1, 4 => 1 - Smooth(Math.Min(1, u * 2)), _ => 0 };
        pose.Hold = pose.Stage switch { 2 or 3 => 1, 4 => 1 - Smooth(Math.Clamp((u - 0.5f) * 2, 0, 1)), _ => 0 };
        // The sheet takes the die's shape as the die's top passes the roll line: it is stretched over the highest point first.
        var hi = pose.Die?.High ?? 0;
        pose.Shape = pose.Stage switch
        {
            2 => hi > 0.01f ? Math.Clamp((hi - (1 - pose.Rise) * (DieBelow + hi)) / hi, 0, 1) : pose.Rise,
            3 or 4 or 5 => 1,
            _ => 0,
        };
        return pose;
    }

    /// <summary>
    /// The head order's panel on a former's bed, for its die: a height map of its top over its footprint as it lies (the key's surface
    /// sampled through <see cref="HullLoft.PanelAt"/>, so a fold's arc and upright land where the formed panel has them), its footprint's
    /// length along the bed and width across (m), its highest lift, its sheet thickness (m) and which jaws grip it: the side jaws when its
    /// shape runs across the bed (its bend lines with the roll), else the end gates. No depth cap: the die sinks into a pocket as deep as
    /// its shape needs (the Lab's cheat).
    /// </summary>
    public sealed class DieInfo
    {
        public const int Along = 48, Across = 24;
        public readonly float Length, Width, High, Thick;
        public readonly bool SideGrip;
        private readonly float[] _h; // (Along + 1) x (Across + 1) lifts over the lowest point, row by row along
        public DieInfo(float length, float width, float high, float thick, float[] h)
        {
            (Length, Width, High, Thick, _h) = (length, width, high, thick, h);
            // Which way its shape runs: the biggest change along a row (across) against the biggest along a column (along).
            float across = 0, along = 0;
            var row = Across + 1;
            for (var i = 0; i <= Along; i++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (var j = 0; j <= Across; j++)
                    (lo, hi) = (MathF.Min(lo, h[i * row + j]), MathF.Max(hi, h[i * row + j]));
                across = MathF.Max(across, hi - lo);
            }
            for (var j = 0; j <= Across; j++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (var i = 0; i <= Along; i++)
                    (lo, hi) = (MathF.Min(lo, h[i * row + j]), MathF.Max(hi, h[i * row + j]));
                along = MathF.Max(along, hi - lo);
            }
            SideGrip = across > along;
        }

        /// <summary>The panel's lift (m over its lowest point) at <paramref name="along"/> and <paramref name="across"/> m from its footprint's middle; past its edge, the edge's (the shape runs on to the jaws).</summary>
        public float LiftAt(float along, float across)
        {
            var u = Math.Clamp((along / MathF.Max(0.01f, Length) + 0.5f) * Along, 0, Along);
            var v = Math.Clamp((across / MathF.Max(0.01f, Width) + 0.5f) * Across, 0, Across);
            int i = Math.Min((int)u, Along - 1), j = Math.Min((int)v, Across - 1);
            float fu = u - i, fv = v - j;
            var row = Across + 1;
            float a = _h[i * row + j], b = _h[(i + 1) * row + j], c = _h[i * row + j + 1], d = _h[(i + 1) * row + j + 1];
            return (a * (1 - fu) + b * fu) * (1 - fv) + (c * (1 - fu) + d * fu) * fv;
        }
    }

    // The orders whose shapes the dies show (names compared by reference, read once a change): enough slots for every former working a
    // different panel in one view, so none is made again every frame.
    private static readonly string?[] _dieName = new string?[32];
    private static readonly DieInfo?[] _dieInfo = new DieInfo?[32];
    private static int _dieNext;

    /// <summary>The head order's die shape on Core <paramref name="core"/>, or false (no order, or a key with no shape: a kit's loft, a plain skin; a flat die). Allocates only when the order changes.</summary>
    public static bool Die(Construct c, int core, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DieInfo? info)
    {
        info = null;
        if (!WorkOrders.HeadOf(c.Parts[core], out var head) || head.Name is not { } name)
            return false;
        for (var k = 0; k < _dieName.Length; k++)
            if (ReferenceEquals(_dieName[k], name))
                return (info = _dieInfo[k]) != null;
        var at = _dieNext;
        _dieNext = (_dieNext + 1) % _dieName.Length;
        _dieName[at] = name;
        _dieInfo[at] = MakeDie(name);
        return (info = _dieInfo[at]) != null;
    }

    private static DieInfo? MakeDie(string name)
    {
        if (HullLoft.PanelOf(name) is not { } sh)
            return null;
        // The surface as it lies: fine samples along the sheet (a fold's arc), each dropped into the height map's cell under it (its top kept).
        const int ns = 192, nx = 48;
        var pts = new Vector3[(ns + 1) * (nx + 1)];
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, z0 = float.MaxValue, z1 = float.MinValue;
        for (var i = 0; i <= ns; i++)
            for (var j = 0; j <= nx; j++)
            {
                var q = HullLoft.PanelAt(sh, i / (float)ns, -1 + 2f * j / nx);
                pts[i * (nx + 1) + j] = q;
                (x0, x1, y0, z0, z1) = (MathF.Min(x0, q.X), MathF.Max(x1, q.X), MathF.Min(y0, q.Y), MathF.Min(z0, q.Z), MathF.Max(z1, q.Z));
            }
        float len = MathF.Max(0.05f, z1 - z0), wid = MathF.Max(0.05f, x1 - x0);
        const int ra = DieInfo.Along, rc = DieInfo.Across, row = rc + 1;
        var h = new float[(ra + 1) * row];
        Array.Fill(h, float.NaN);
        foreach (var q in pts)
        {
            int i = (int)MathF.Round((q.Z - z0) / len * ra), j = (int)MathF.Round((q.X - x0) / wid * rc);
            ref var cell = ref h[Math.Clamp(i, 0, ra) * row + Math.Clamp(j, 0, rc)];
            var lift = q.Y - y0;
            cell = float.IsNaN(cell) ? lift : MathF.Max(cell, lift);
        }
        // Cells no sample fell in (a steep slope's gaps) take their row's nearest, then their column's.
        for (var pass = 0; pass < 2; pass++)
            for (var i = 0; i <= ra; i++)
                for (var j = 0; j <= rc; j++)
                    if (float.IsNaN(h[i * row + j]))
                        for (var k = 1; k <= Math.Max(ra, rc); k++)
                        {
                            float t = float.NaN;
                            if (pass == 0)
                                t = j - k >= 0 && !float.IsNaN(h[i * row + j - k]) ? h[i * row + j - k] : j + k <= rc ? h[i * row + j + k] : float.NaN;
                            else
                                t = i - k >= 0 && !float.IsNaN(h[(i - k) * row + j]) ? h[(i - k) * row + j] : i + k <= ra ? h[(i + k) * row + j] : float.NaN;
                            if (!float.IsNaN(t))
                            {
                                h[i * row + j] = t;
                                break;
                            }
                        }
        var high = 0f;
        for (var k = 0; k < h.Length; k++)
            high = MathF.Max(high, h[k] = float.IsNaN(h[k]) ? 0 : h[k]);
        var thick = ConstructParts.Find(name) is { } panel ? Math.Max(1, (int)BlankFor(panel).H) / 100f : 0.04f;
        return new DieInfo(len, wid, high, thick, h);
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
    /// What lies on a former's bed (its look, drawn only): the blank, flat on the lanes at the roll line, while it rolls in and the jaws
    /// grip; once the rising die's top passes the roll line, the head order's panel, stretched over the die: drawn squashed toward the
    /// flat blank by <paramref name="squash"/> (the Core's frame: its height scaled by <see cref="FormPose.Shape"/>, its length and width
    /// from the blank's to its own), until it is the formed panel, its lowest point on the roll line. Its def, its turn and its origin in
    /// the Core's frame, laid with its length along the bed and centred on it. Allocates only the first time a blank's key is seen
    /// (Extrusion.Def keeps it).
    /// </summary>
    public static bool BedLook(Construct c, int part, CStore st, double now, out ConstructPartDef what, out byte rot, out Vector3 at, out Matrix4x4 squash)
    {
        (what, rot, at, squash) = (null!, 0, default, Matrix4x4.Identity);
        ref var p = ref c.Parts[part];
        var d = p.D;
        if (!st.OnBed || d.BlockRole != BlockRole.Core || !MachineBlocks.TryOf(c, part, out var asm) || asm.W == 0 || !WorkOrders.HeadOf(p, out var head)
            || head.Name == null || ConstructParts.Find(head.Name) is not { } panel)
            return false;
        var pose = Pose(c, part, st, now);
        var formed = pose.Shape > 0.002f;
        var blank = Extrusion.Def(BlankFor(panel));
        var def = formed ? panel : blank;
        if (def == null)
            return false;
        what = def;
        var own = formed ? LayOf(def) : (byte)0; // (a hull skin lies turned, its shallowest side up)
        var alongX = asm.OutSide is 0 or 1;
        var bed = alongX ? _quarter : (byte)0; // (its length along the bed: z, or x on a bed that runs along x)
        rot = own == 0 ? bed : Rot24.Compose(bed, own);
        var o = d.BlockOrigin ?? (d.BoundsMin + d.BoundsMax) * 0.5f;
        var mid = new Vector3(o.X + (asm.X0 + (asm.W - 1) * 0.5f) * d.BlockCell, d.ExtDieAt.Y, o.Z + (asm.Z0 + (asm.L - 1) * 0.5f) * d.BlockCell);
        LaidBounds(def, own, out var lmin, out var lmax);
        var foot = (lmin + lmax) * 0.5f;
        foot.Y = lmin.Y;
        // Rolling in from the bed's in end or out to its out end (the Lab's roll; the out run takes it from there).
        var roll = pose.Slide * (alongX ? asm.W : asm.L) * d.BlockCell * 0.5f;
        mid += asm.OutSide switch { 0 => new Vector3(roll, 0, 0), 1 => new Vector3(-roll, 0, 0), 4 => new Vector3(0, 0, roll), _ => new Vector3(0, 0, -roll) };
        at = mid + new Vector3(0, 0.004f, 0) - Rot24.Apply(bed, foot); // (on the lanes: 4 mm over the rollers' tops)
        if (formed && pose.Shape < 0.999f && blank != null)
        {
            // Stretched over the die: its height by the shape, its footprint from the blank's to its own.
            var ps = Rot24.ApplyHalf(rot, def.BoundsMax - def.BoundsMin);
            var bs = Rot24.ApplyHalf(bed, blank.BoundsMax - blank.BoundsMin);
            var s = pose.Shape;
            float kx = 1 + (MathF.Max(0.01f, bs.X) / MathF.Max(0.01f, ps.X) - 1) * (1 - s), kz = 1 + (MathF.Max(0.01f, bs.Z) / MathF.Max(0.01f, ps.Z) - 1) * (1 - s);
            var pivot = mid + new Vector3(0, 0.004f, 0);
            squash = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale(kx, MathF.Max(0.02f, s), kz) * Matrix4x4.CreateTranslation(pivot);
        }
        return true;
    }
}
