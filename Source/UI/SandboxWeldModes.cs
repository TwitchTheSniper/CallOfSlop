using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's modes on the tool wheel's outer ring (board item dev-sandbox-freeform-weld-fw01, decision 25): "Socket
/// weld" (the default: the three-step socket weld, Program.SandboxSocketWeld.cs), "Freeform weld" (two bodies welded where they are:
/// Program.SandboxFreeform.cs) and "Point weld" (board item dev-point-weld-pw01: a clicked point on the piece onto a clicked
/// point on the target, face to face, then spun: Program.SandboxPointWeld.cs). Letting go on the welder itself keeps the
/// last mode. This PC's state only (nothing on the wire: the server is asked for each weld). Nothing allocates per frame.
/// </summary>
public sealed class SandboxWeldModes : IToolOptions
{
    public enum Option
    {
        Socket,
        Freeform,
        Point,
        COUNT,
    }

    private readonly RadialMenu.Item[] _options =
    {
        new() { Label = "Socket weld", Hint = "socket to socket", Icon = 0 },
        new() { Label = "Freeform weld", Hint = "where they are", Icon = 1 },
        new() { Label = "Point weld", Hint = "point to point", Icon = 2 },
    };

    public RadialMenu.Item[] Options => _options;

    /// <summary>The welder's mode (socket weld, the default).</summary>
    public Option Mode = Option.Socket;

    /// <summary>The welder welds freeform. Setting it false goes back to the default (socket weld).</summary>
    public bool Freeform
    {
        get => Mode == Option.Freeform;
        set => Mode = value ? Option.Freeform : Option.Socket;
    }

    /// <summary>The welder point-welds (board item pw01).</summary>
    public bool Point => Mode == Option.Point;

    public static readonly RadialIcon Icons = DrawIcon;
    RadialIcon? IToolOptions.Icons => Icons;

    public int CurrentOption => (int)Mode;

    public void Choose(int option)
    {
        if (option is >= 0 and < (int)Option.COUNT)
            Mode = (Option)option;
    }

    private static void DrawIcon(ImDrawListPtr dl, int icon, float2 c, float r, ImColor8 col, ImColor8 bg)
    {
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        var spark = new ImColor8(255, 170, 60, 255);
        if (icon == 0)
        {
            // Socket weld: two square plates meeting edge to edge, the seam lit.
            ImDrawListExtensions.AddRectFilled(dl, P(-0.8f, -0.35f), P(-0.05f, 0.35f), col, 0.04f * r, ImDrawFlags.None);
            ImDrawListExtensions.AddRectFilled(dl, P(0.05f, -0.35f), P(0.8f, 0.35f), col, 0.04f * r, ImDrawFlags.None);
            ImDrawListExtensions.AddLine(dl, P(0f, -0.45f), P(0f, 0.45f), spark, 0.12f * r);
            return;
        }
        if (icon == 2)
        {
            // Point weld: a plate, a crosshair dot on it and an arrow to a second plate below.
            ImDrawListExtensions.AddRectFilled(dl, P(-0.75f, -0.75f), P(0.35f, -0.4f), col, 0.04f * r, ImDrawFlags.None);
            ImDrawListExtensions.AddRectFilled(dl, P(-0.35f, 0.4f), P(0.75f, 0.75f), col, 0.04f * r, ImDrawFlags.None);
            var dot = P(-0.2f, -0.3f);
            ImDrawListExtensions.AddCircleFilled(dl, in dot, 0.14f * r, spark, 10);
            ImDrawListExtensions.AddLine(dl, P(-0.1f, -0.2f), P(0.2f, 0.3f), spark, 0.1f * r);
            return;
        }
        // Freeform weld: a plate and a second one at a slant, a bead between them.
        ImDrawListExtensions.AddRectFilled(dl, P(-0.85f, 0.15f), P(0.1f, 0.5f), col, 0.04f * r, ImDrawFlags.None);
        ImDrawListExtensions.AddLine(dl, P(0.2f, 0.1f), P(0.75f, -0.7f), col, 0.3f * r);
        var bead = P(0.12f, 0.12f);
        ImDrawListExtensions.AddCircleFilled(dl, in bead, 0.16f * r, spark, 10);
        _ = bg;
    }
}
