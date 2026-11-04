using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The attach tool's modes on the tool wheel's outer ring (its code name is still the welder; board item
/// dev-sandbox-freeform-weld-fw01, decision 25). Everything that joins pieces is here, where the linking tool links what is
/// already placed: "Socket weld" (the default, in the middle of the arc so a flick straight out lands on it: the three-step
/// socket weld, Program.SandboxSocketWeld.cs), "Freeform weld" (two bodies welded where they are: Program.SandboxFreeform.cs),
/// "Point weld" (board item dev-point-weld-pw01: a clicked point on the piece onto a clicked point on the target, face to
/// face, then spun: Program.SandboxPointWeld.cs), "Rope" and "Spring" (two points: Program.SandboxConnectors.cs). Letting go
/// on the tool itself keeps the last mode. The mode is a field that nothing resets, so a pick made while another tool is out
/// is there when the server's switch arrives. This PC's state only (nothing on the wire: the server is asked for each
/// weld). Nothing allocates per frame.
/// </summary>
public sealed class SandboxWeldModes : IToolOptions
{
    public enum Option
    {
        Rope,
        Freeform,
        Socket, // (the default: the middle of the arc)
        Point,
        Spring,
        COUNT,
    }

    private readonly RadialMenu.Item[] _options =
    {
        new() { Label = "Rope", Hint = "two points: at most this far apart", Icon = 3 },
        new() { Label = "Freeform weld", Hint = "where they are", Icon = 1 },
        new() { Label = "Socket weld", Hint = "socket to socket", Icon = 0 },
        new() { Label = "Point weld", Hint = "point to point", Icon = 2 },
        new() { Label = "Spring", Hint = "two points: pushes and pulls", Icon = 4 },
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

    /// <summary>The kind the Rope and Spring modes tie, or null in the welding modes.</summary>
    public ConnectorKind? Connector => Mode switch { Option.Rope => ConnectorKind.Rope, Option.Spring => ConnectorKind.Spring, _ => null };

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
        if (icon == 3)
        {
            // Rope: a slack curve between two knots.
            var a = P(-0.75f, -0.35f);
            var c1 = P(-0.3f, 0.75f);
            var c2 = P(0.3f, 0.75f);
            var b = P(0.75f, -0.35f);
            ImDrawListExtensions.PathClear(dl);
            ImDrawListExtensions.PathLineTo(dl, in a);
            ImDrawListExtensions.PathBezierCubicCurveTo(dl, in c1, in c2, in b, 12);
            ImDrawListExtensions.PathStroke(dl, col, ImDrawFlags.None, 0.13f * r);
            ImDrawListExtensions.AddRectFilled(dl, P(-0.9f, -0.55f), P(-0.6f, -0.25f), spark, 0.04f * r, ImDrawFlags.None);
            ImDrawListExtensions.AddRectFilled(dl, P(0.6f, -0.55f), P(0.9f, -0.25f), spark, 0.04f * r, ImDrawFlags.None);
            return;
        }
        if (icon == 4)
        {
            // Spring: a zigzag between two plates.
            ImDrawListExtensions.AddRectFilled(dl, P(-0.9f, -0.5f), P(-0.75f, 0.5f), spark, 0.04f * r, ImDrawFlags.None);
            ImDrawListExtensions.AddRectFilled(dl, P(0.75f, -0.5f), P(0.9f, 0.5f), spark, 0.04f * r, ImDrawFlags.None);
            for (var i = 0; i < 6; i++)
                ImDrawListExtensions.AddLine(dl, P(-0.75f + i * 0.25f, (i & 1) == 0 ? -0.4f : 0.4f), P(-0.5f + i * 0.25f, (i & 1) == 0 ? 0.4f : -0.4f), col, 0.09f * r);
            return;
        }
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
