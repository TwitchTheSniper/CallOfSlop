namespace CallOfSlop;

/// <summary>
/// A Press on the block system (docs/sandbox/constructs/production.md "The Press", design page drawings 17 and 17b): the Press Core alone
/// presses blanks up to its own 0.45 m across at speed 1. Press Blocks welded to it make the bed (the largest full rectangle of blocks
/// that holds one welded to the Core: MachineBlocks.cs) and, stacked on the bed's two long edges (its first and last rows along the
/// Core's z), its walls: the clamshell's shells (drawing 17b, ConstructPressShells.cs), hinged at the bed's edges, that swing shut over
/// the blank. The flow runs along the Core's x, from the Core down the bed. The walls set the daylight, the largest across a blank may
/// be: 1.6 m with one block on the bed, 2 m more for each block more, the shortest column of either wall counting. The bed's width
/// across the flow (2 m a block) is the widest side a blank may have, and a blank longer than the bed is pressed in bites, one
/// stroke's time for each bed length. Speed and draw grow with the blocks taken in.
/// Everything is read from the construct's parts and links, so a client's copy agrees with the host's after a block is welded or shot off.
/// </summary>
public static partial class ConstructPress
{
    public const int WallFirstCm = 160; // the daylight one wall block gives (cm): shells meet over it, no ram hangs under it
    public const int WallStepCm = 200; // each wall block more
    public const float BlockWatts = 180; // what each cell of the machine draws while it works (the Core counts as one)
    private const int SpeedBlocks = 5; // a press of this many cells works at speed 1; speed is the root of cells over this
    private const float MaxSpeed = 8;

    /// <summary>The Press Core's machine, when it has a bed.</summary>
    private static bool Bed(Construct c, int part, out CMachineAsm asm)
    {
        asm = default;
        return c.Parts[part].D is { IsPress: true, BlockRole: BlockRole.Core } && MachineBlocks.TryOf(c, part, out asm) && asm.W > 0;
    }

    /// <summary>The cells of the machine at work (the Core and every block but idle ones), 1 for a press off the block system.</summary>
    public static int Cells(Construct c, int part) =>
        c.Parts[part].D.BlockRole == BlockRole.Core && MachineBlocks.TryOf(c, part, out var asm) ? 1 + Math.Max(0, asm.Cells - asm.Idle) : 1;

    /// <summary>The walls' height in blocks over the bed: the shortest column along either long edge (its first and last rows along the Core's z; 0: no walls, or a bed one block wide).</summary>
    public static int Walls(Construct c, int part)
    {
        if (!Bed(c, part, out var asm) || asm.L < 2)
            return 0;
        var h = int.MaxValue;
        for (var x = asm.X0; x < asm.X0 + asm.W; x++)
            h = Math.Min(h, Math.Min(MachineBlocks.ColumnAt(c, asm, x, asm.Z0), MachineBlocks.ColumnAt(c, asm, x, asm.Z0 + asm.L - 1)));
        return Math.Max(0, h);
    }

    /// <summary>The daylight (cm) a press with <paramref name="walls"/> wall blocks has; <paramref name="own"/> with none.</summary>
    public static int DaylightCm(int walls, int own) => walls <= 0 ? own : Math.Min(Pressed.PressMaxCm[^1], WallFirstCm + (walls - 1) * WallStepCm);

    /// <summary>The largest across (cm) this press takes now: its walls' daylight, or the Core's own with none.</summary>
    public static int MaxCm(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.BlockRole == BlockRole.Core ? DaylightCm(Walls(c, part), d.PressMaxCm) : d.PressMaxCm;
    }

    /// <summary>The widest side (cm) a blank may have on this press: the bed's width, or no limit (int.MaxValue) for a Core alone.</summary>
    public static int WidthCm(Construct c, int part) =>
        Bed(c, part, out var asm) ? (int)MathF.Round(asm.L * c.Parts[part].D.BlockCell * 100) : int.MaxValue; // (across the flow: the Core's z)

    /// <summary>The wall blocks a blank this many cm across needs (0: a Core alone takes it).</summary>
    public static int WallsFor(int acrossCm) => acrossCm <= Pressed.PressMaxCm[0] ? 0 : acrossCm <= WallFirstCm ? 1 : 1 + (acrossCm - WallFirstCm + WallStepCm - 1) / WallStepCm;

    /// <summary>What a blank this many cm across needs, for a bill or an order's reason: "any press", "a press with 2 wall blocks (3.6 m of daylight)".</summary>
    public static string NeedText(int acrossCm)
    {
        var w = WallsFor(acrossCm);
        return w == 0 ? "any press" : $"a press with {w} wall block{(w == 1 ? "" : "s")} ({DaylightCm(w, 0) / 100f:0.0} m of daylight)";
    }

    /// <summary>Does this blank fit this press: across under its daylight, its wider side across its bed.</summary>
    public static bool Fits(Construct c, int part, in ExtrusionKey blank) =>
        Pressed.Across(blank) <= MaxCm(c, part) && Math.Max((int)blank.W, (int)blank.H) <= WidthCm(c, part);

    /// <summary>A stroke's bites: one for each bed length of the blank (1 with no bed).</summary>
    public static int Bites(Construct c, int part, in ExtrusionKey blank)
    {
        if (!Bed(c, part, out var asm))
            return 1;
        var bed = (int)MathF.Round(asm.W * c.Parts[part].D.BlockCell * 100); // (along the flow: the Core's x)
        return Math.Max(1, (blank.L + bed - 1) / bed);
    }

    /// <summary>The press's speed: the root of its cells over five, 1 to 8.</summary>
    public static float Speed(Construct c, int part) => Math.Clamp(MathF.Sqrt(Cells(c, part) / (float)SpeedBlocks), 1, MaxSpeed);

    /// <summary>Its work a step (thousandths: as d.RefineWork).</summary>
    private static int WorkOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.BlockRole == BlockRole.Core ? (int)MathF.Round(d.RefineWork * Speed(c, part)) : d.RefineWork;
    }

    /// <summary>What the press draws while it works (W): the Core's own, or each cell's when that is more.</summary>
    public static float PowerOf(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.BlockRole == BlockRole.Core ? MathF.Max(d.PowerDraw, Cells(c, part) * BlockWatts) : d.PowerDraw;
    }

    /// <summary>Why a blank is too big for this press, in words that name the blocks to add.</summary>
    public static string TooBig(Construct c, int part)
    {
        var day = MaxCm(c, part) / 100f;
        var width = WidthCm(c, part);
        return width == int.MaxValue
            ? $"too big: a Press Core alone takes {day:0.00} m across, weld Press Blocks to it for a bed and walls"
            : $"too big: this press has {day:0.0} m of daylight and a {width / 100f:0} m bed, add wall blocks or widen the bed";
    }

    /// <summary>What the screen and the look line say of the press's size: "bed 3 x 2, 1.6 m of daylight, speed x1.3".</summary>
    public static string SizeText(Construct c, int part)
    {
        if (c.Parts[part].D.BlockRole != BlockRole.Core || !MachineBlocks.TryOf(c, part, out var asm))
            return "";
        var s = asm.W == 0 ? MachineBlocks.Describe(c, asm) : $"{MachineBlocks.Describe(c, asm)}, {MaxCm(c, part) / 100f:0.00} m of daylight";
        var swing = SwingText(c, part);
        return swing.Length > 0 ? $"{s}, speed x{Speed(c, part):0.0}, {swing}" : $"{s}, speed x{Speed(c, part):0.0}";
    }
}
