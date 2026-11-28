namespace CallOfSlop;

/// <summary>
/// A Press on the block system (docs/sandbox/constructs/production.md "The Press"): the Press Core alone presses blanks up to its own
/// 0.45 m across at speed 1. Press Blocks welded to it make the bed (the largest full rectangle of blocks that holds one welded to the
/// Core: MachineBlocks.cs); on a low bed, Press Bed Blocks lie between the two long edges' rows of Press Blocks (the side frames). The
/// press is a swing lid (ConstructPressLid.cs) hinged on the frame across from the Core. The flow runs along the Core's x, from the Core
/// down the bed. The bed's width sets the daylight, the largest across a blank may be: 1.6 m with two or three blocks across, 2 m more for
/// each two blocks more (the lid's pedestal rises with the blank). The bed's width across the flow (2 m a block) is the widest side a
/// blank may have, and a blank longer than the bed is pressed in bites, one stroke's time for each bed length. Speed grows with the blocks
/// taken in. Nothing stacks on the bed (the old round dies' wall blocks): the lid lies there.
/// Everything is read from the construct's parts and links, so a client's copy agrees with the host's after a block is welded or shot off.
/// </summary>
public static partial class ConstructPress
{
    public const int DayFirstCm = 160; // the daylight a bed two or three blocks across gives (cm)
    public const int DayStepCm = 200; // each two blocks more across
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

    /// <summary>The press's lid size in steps of daylight: half its bed's blocks across (0: a Core alone, or a bed one block wide).</summary>
    public static int LidSteps(Construct c, int part) => Bed(c, part, out var asm) && asm.L >= 2 ? asm.L / 2 : 0;

    /// <summary>The daylight (cm) a press with <paramref name="steps"/> lid steps has; <paramref name="own"/> with none.</summary>
    public static int DaylightCm(int steps, int own) => steps <= 0 ? own : Math.Min(Pressed.PressMaxCm[^1], DayFirstCm + (steps - 1) * DayStepCm);

    /// <summary>The largest across (cm) this press takes now: its walls' daylight, or the Core's own with none.</summary>
    public static int MaxCm(Construct c, int part)
    {
        var d = c.Parts[part].D;
        return d.BlockRole == BlockRole.Core ? DaylightCm(LidSteps(c, part), d.PressMaxCm) : d.PressMaxCm;
    }

    /// <summary>The widest side (cm) a blank may have on this press: the bed's width, or no limit (int.MaxValue) for a Core alone.</summary>
    public static int WidthCm(Construct c, int part) =>
        Bed(c, part, out var asm) ? (int)MathF.Round(asm.L * c.Parts[part].D.BlockCell * 100) : int.MaxValue; // (across the flow: the Core's z)

    /// <summary>The lid steps a blank this many cm across needs (0: a Core alone takes it).</summary>
    public static int StepsFor(int acrossCm) => acrossCm <= Pressed.PressMaxCm[0] ? 0 : acrossCm <= DayFirstCm ? 1 : 1 + (acrossCm - DayFirstCm + DayStepCm - 1) / DayStepCm;

    /// <summary>What a blank this many cm across needs, for a bill or an order's reason: "any press", "a press bed 4 blocks across (3.6 m of daylight)".</summary>
    public static string NeedText(int acrossCm)
    {
        var w = StepsFor(acrossCm);
        return w == 0 ? "any press" : $"a press bed {2 * w} blocks across ({DaylightCm(w, 0) / 100f:0.0} m of daylight)";
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
            ? $"too big: a Press Core alone takes {day:0.00} m across, weld Press Blocks to it for a bed"
            : $"too big: this press has {day:0.0} m of daylight and a {width / 100f:0} m bed, widen the bed";
    }

    /// <summary>What the look line says of the press's size: "bed 3 x 2 (6.0 x 4.0 m), 1.60 m of daylight" (its speed is on its screen, not here).</summary>
    public static string SizeText(Construct c, int part)
    {
        if (c.Parts[part].D.BlockRole != BlockRole.Core || !MachineBlocks.TryOf(c, part, out var asm))
            return "";
        var s = asm.W == 0 ? MachineBlocks.Describe(c, asm) : $"{MachineBlocks.Describe(c, asm)}, {MaxCm(c, part) / 100f:0.00} m of daylight";
        var swing = SwingText(c, part);
        return swing.Length > 0 ? $"{s}, {swing}" : s;
    }
}
