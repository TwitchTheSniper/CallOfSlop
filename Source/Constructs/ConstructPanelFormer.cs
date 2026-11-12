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

    /// <summary>Seconds a panel takes on a former of speed 1: 4 s and 2 s a square metre of its largest face.</summary>
    public static float Seconds(ConstructPartDef panel)
    {
        Sides(panel, out var a, out var b);
        return 4 + 2 * a * b;
    }

    /// <summary>A panel's two longest sides (its bounds), the longest first.</summary>
    public static void Sides(ConstructPartDef panel, out float longest, out float next)
    {
        var s = panel.BoundsMax - panel.BoundsMin;
        longest = MathF.Max(s.X, MathF.Max(s.Y, s.Z));
        next = s.X + s.Y + s.Z - longest - MathF.Min(s.X, MathF.Min(s.Y, s.Z));
    }

    /// <summary>Does a former take a panel this size? (Its longest side within the machine's length, the next within its width.)</summary>
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
            if (d.IsPanelFormer && Fits(d, panel) && (best == null || d.ExtWheelMax.Y < best.ExtWheelMax.Y))
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
            return $"panel too large for this former: {a:0.##} x {b:0.##} m, the {d.Name} takes {d.ExtWheelMax.Y:0.##} x {d.ExtWheelMax.X:0.##} m";
        }
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
        var need = (int)(Seconds(panel) / MathF.Max(0.05f, d.ExtSpeed) * World.TickRate);
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
            var at = t + Vector3.Transform(p.Pos, q) + Vector3.Transform(d.ExtDieAt + new Vector3(0, size.Y * 0.5f + 0.05f, 0), turn); // (its middle over the out bed)
            if (ConstructEdits.DropLoose(world, panel, at, turn) is not { } body)
            {
                Idle(world, st, ExtrudeStall.Blocked);
                return;
            }
            body.Owner = c.Owner;
            body.Vel = c.PointVel(at);
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

    /// <summary>A part file's <c>storage.panel</c> block: <c>{speed, at, maxWidth, maxLength}</c> on a bin (what feeds it).</summary>
    public static void Parse(ConstructPartDef d, System.Text.Json.JsonElement b, string file, string bw)
    {
        if (b.ValueKind != System.Text.Json.JsonValueKind.Object || d.BulkCapacity == 0 || d.IsRecycler || d.IsGatherer || d.IsRefiner || d.IsExtruder)
            throw new ConstructPartException($"{file}: storage \"panel\" is an object (speed, at, maxWidth, maxLength) on a bin: what feeds it");
        ConstructParts.Known(b, ["speed", "at", "maxWidth", "maxLength"], $"{bw}.panel");
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
}
