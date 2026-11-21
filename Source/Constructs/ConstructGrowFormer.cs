using System.Numerics;

namespace CallOfSlop;

/// <summary>What a Grow Former's own settings make (its "growMakes" setting; an order makes what it asks).</summary>
public enum GrowMakes : byte { Wheel, Prop, Wing }

/// <summary>
/// The Grow Former (design page drawing 22; docs/sandbox/constructs/production.md "The Grow Former"): one block machine in place of the Wheel,
/// Fan and Airframe Formers' twelve parts. A Grow Former Core grows a wheel, a prop or a wing on its hub, by its Makes setting or by its
/// head order; Former Blocks welded on the floor beside it are its bed (MachineBlocks.cs finds the rectangle at the rebuild), and Mast
/// Blocks stacked on the Core are the column its hub rides up. What the machine makes now follows from them:
/// <list type="bullet">
/// <item>a wheel's width (along its axle, the bed's length) is at most the bed's length; its diameter at most the bed's width and the masts' height plus 2 m;</item>
/// <item>a prop's disc stands over the bed: at most twice the bed's width and twice the masts' height plus 2 m (64 m at most);</item>
/// <item>a wing's chord fits across the bed, its span grows out along the bed and on over Run-out Tables at its far end.</item>
/// </list>
/// Speed goes with the square root of the blocks in its bed (a 4-block bed is the old Small former's), push and draw with each block. A wheel
/// or a prop starts with its hub raised on its slide to clear the bed at the size it will grow to, so the piece never moves once it starts
/// (nothing new on the wire: a client finds the same from the piece's place). Everything is read from parts and links at the rebuild or the
/// 10 Hz step; nothing here allocates.
/// </summary>
public static partial class ConstructExtruder
{
    private const float HubClear = 0.1f; // m between a wheel's or a prop's rim and the bed's top

    /// <summary>What the Makes setting of a Grow Former asks for (its own settings and its Queue button).</summary>
    public static GrowMakes MakesSetting(in CPart p, ConstructPartDef d) =>
        d.IsGrowFormer ? (GrowMakes)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.GrowMakes, 0), 0, 2) : d.IsAirframeFormer ? GrowMakes.Wing : d.IsFanFormer ? GrowMakes.Prop : GrowMakes.Wheel;

    /// <summary>Do this machine's own settings make props now?</summary>
    public static bool FanSet(in CPart p, ConstructPartDef d) => d.IsGrowFormer ? MakesSetting(p, d) == GrowMakes.Prop : d.IsFanFormer;

    /// <summary>Do this machine's own settings make wings now?</summary>
    public static bool WingSet(in CPart p, ConstructPartDef d) => d.IsGrowFormer ? MakesSetting(p, d) == GrowMakes.Wing : d.IsAirframeFormer;

    /// <summary>Can machine kind <paramref name="d"/> make a piece of shape <paramref name="s"/> (a wheel, a prop or a wing)?</summary>
    public static bool Can(ConstructPartDef d, ExShape s) => s switch
    {
        ExShape.Wheel => d.IsWheelFormer && !d.IsPanelFormer && (d.IsGrowFormer || !d.IsFanFormer && !d.IsAirframeFormer),
        ExShape.Fan => d.IsFanFormer,
        ExShape.Wing => d.IsAirframeFormer,
        _ => false,
    };

    /// <summary>What a Grow Former works on now: the piece on its hub, else its head order, else its Makes setting.</summary>
    public static ExShape GrowShape(Construct c, int part)
    {
        ref var p = ref c.Parts[part];
        if (ProductOf(c, part) is var on and >= 0 && on < c.PartCount && c.Parts[on].D is { IsExtrusion: true } od && od.ExKey.Shape is ExShape.Wheel or ExShape.Fan or ExShape.Wing)
            return od.ExKey.Shape;
        if (WorkOrders.HeadOf(p, out var head) && FormerPresets.KeyFor(head, out var k) && k.Shape is ExShape.Wheel or ExShape.Fan or ExShape.Wing)
            return k.Shape;
        return MakesSetting(p, p.D) switch { GrowMakes.Prop => ExShape.Fan, GrowMakes.Wing => ExShape.Wing, _ => ExShape.Wheel };
    }

    /// <summary>
    /// A Grow Former's bed (m: across, along) and the masts' height on its Core (m), from its assembly. A Core with no Former Blocks has its
    /// own cell for a bed.
    /// </summary>
    public static void GrowBed(Construct c, int part, out float across, out float along, out float masts, out int blocks)
    {
        var d = c.Parts[part].D;
        var cell = d.BlockCell;
        across = along = cell;
        masts = 0;
        blocks = 1;
        if (!MachineBlocks.TryOf(c, part, out var asm))
            return;
        masts = MachineBlocks.CoreColumn(c, asm) * cell;
        if (asm.W == 0)
            return;
        across = asm.W * cell;
        along = asm.L * cell;
        blocks = asm.W * asm.L;
    }

    /// <summary>
    /// The first and the biggest piece of shape <paramref name="s"/> this machine makes (x: a wheel's width or a wing's chord; y: a wheel's
    /// or a prop's diameter, or a wing's span; m). With no construct, the most a machine of this kind makes with every block; a Grow Former on
    /// a construct, the most its blocks, masts and tables make now. Any other former, its part file's numbers.
    /// </summary>
    public static void Sizes(Construct? c, int part, ConstructPartDef d, ExShape s, out Vector2 first, out Vector2 most)
    {
        if (!d.IsGrowFormer)
        {
            (first, most) = (d.ExtWheelStart, d.ExtWheelMax);
            return;
        }
        switch (s)
        {
            case ExShape.Fan:
                (first, most) = (new Vector2(0.05f, d.ExtFanStart), new Vector2(0.05f, d.ExtFanMax));
                break;
            case ExShape.Wing:
                (first, most) = (d.ExtWingStart, d.ExtWingMax);
                break;
            default:
                (first, most) = (d.ExtWheelStart, d.ExtWheelMax);
                break;
        }
        if (c == null || part < 0)
            return;
        GrowBed(c, part, out var across, out var along, out var masts, out _);
        most = s switch
        {
            ExShape.Fan => new Vector2(most.X, MathF.Min(most.Y, MathF.Min(2 * across, 2 * masts + 2))),
            ExShape.Wing => new Vector2(MathF.Min(most.X, across), MathF.Min(most.Y, MathF.Max(along, TablesReach(c, part)))),
            _ => new Vector2(MathF.Min(most.X, along), MathF.Min(most.Y, MathF.Min(across, masts + 2))),
        };
        most = Vector2.Max(Snap(most), first); // (on the 5 cm step, and never under the first piece: a machine that small makes only that)
    }

    private static Vector2 Snap(Vector2 v) => new(MathF.Floor(v.X * 20 + 0.001f) / 20, MathF.Floor(v.Y * 20 + 0.001f) / 20);

    /// <summary>How fast a Grow Former grows (m/s): its file's speed times the square root of a quarter of its bed's blocks (a wing's span 2.5 times that).</summary>
    public static float GrowSpeed(Construct c, int part, ExShape s)
    {
        var d = c.Parts[part].D;
        if (!d.IsGrowFormer)
            return d.ExtSpeed;
        GrowBed(c, part, out _, out _, out _, out var blocks);
        return d.ExtSpeed * MathF.Sqrt(blocks / 4f) * (s == ExShape.Wing ? 2.5f : 1f) * PartKit.Generators.MachineTiers.Multipliers(MachineBlocks.TierOf(c, part)).Rate;
    }

    /// <summary>What a Grow Former's bin holds now (units): the Core's own and each bed block's share, times its tier's capacity, at most its file's bulk capacity.</summary>
    public static int GrowBinOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        var cap = PartKit.Generators.MachineTiers.Multipliers(MachineBlocks.TierOf(c, part)).Cap;
        return Math.Min(d.BulkCapacity, (int)((d.GrowBin + d.GrowBinBlock * Blocks(c, part)) * cap));
    }

    /// <summary>Where a Grow Former's hub puts a wheel or a prop <paramref name="acrossCm"/> across (part frame): raised on its slide so its rim clears the bed.</summary>
    private static Vector3 HubAt(ConstructPartDef d, ExShape s, int acrossCm)
    {
        if (!d.IsGrowFormer || s == ExShape.Wing)
            return d.ExtDieAt;
        var y = MathF.Max(d.ExtDieAt.Y, d.GrowBedTop + acrossCm / 200f + HubClear);
        return d.ExtDieAt with { Y = MathF.Round(y * 100) / 100 };
    }

    /// <summary>How far a Grow Former's hub stands raised on its slide (m; 0 with nothing on it), from the piece's place: its hub's "slide" boxes ride it.</summary>
    public static float HubRise(Construct c, int core)
    {
        ref readonly var p = ref c.Parts[core];
        var d = p.D;
        if (!d.IsGrowFormer)
            return 0;
        var on = ProductOf(c, core);
        if (on < 0 || on >= c.PartCount || c.Parts[on].Seg != p.Seg)
            return 0;
        var rel = Vector3.Transform(c.Parts[on].Pos - p.Pos, Quaternion.Conjugate(Rot24.Quat(p.Rot)));
        return MathF.Max(0, rel.Y - d.ExtDieAt.Y);
    }

    /// <summary>What would make Grow Former <paramref name="part"/> take a piece this big: "weld Former Blocks for a bed 6 m along" and the like.</summary>
    public static string GrowFix(Construct c, int part, ExShape s, float x, float y)
    {
        var d = c.Parts[part].D;
        Sizes(null, -1, d, s, out _, out var top);
        if (x > top.X + 0.001f || y > top.Y + 0.001f)
            return $"no Grow Former makes it (at most {top.X:0.##} x {top.Y:0.##} m)";
        GrowBed(c, part, out var across, out var along, out var masts, out _);
        var cell = d.BlockCell;
        var asks = new System.Text.StringBuilder();
        void Ask(string what)
        {
            if (asks.Length > 0)
                asks.Append(" and ");
            asks.Append(what);
        }
        int Cells(float m) => Math.Max(1, (int)MathF.Ceiling(m / cell - 0.001f));
        switch (s)
        {
            case ExShape.Wheel:
                if (x > along + 0.001f || y > across + 0.001f)
                    Ask($"weld Former Blocks for a bed {Cells(y)} across and {Cells(x)} along ({Cells(y) * cell:0.#} x {Cells(x) * cell:0.#} m)");
                if (y > masts + 2.001f)
                    Ask($"stack {Cells(y - 2)} Mast Blocks on the Core");
                break;
            case ExShape.Fan:
                if (y > 2 * across + 0.001f)
                    Ask($"weld Former Blocks for a bed {Cells(y / 2)} across ({Cells(y / 2) * cell:0.#} m)");
                if (y > 2 * masts + 2.001f)
                    Ask($"stack {Cells((y - 2) / 2)} Mast Blocks on the Core");
                break;
            default:
                if (x > across + 0.001f)
                    Ask($"weld Former Blocks for a bed {Cells(x)} across ({Cells(x) * cell:0.#} m)");
                if (y > MathF.Max(along, TablesReach(c, part)) + 0.001f)
                    Ask($"lay Former Blocks or Run-out Tables out to {y:0.#} m");
                break;
        }
        return asks.Length > 0 ? asks.ToString() : "it fits";
    }
}
