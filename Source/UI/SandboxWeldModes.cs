using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's modes on the tool wheel's outer ring (board item dev-sandbox-freeform-weld-fw01, decision 25): "Socket
/// weld" (the default: held parts snap onto sockets) and "Freeform weld" (two bodies welded where they are:
/// Program.SandboxFreeform.cs). Letting go on the welder itself keeps the last mode. This PC's state only (nothing on the
/// wire: the server is asked for each weld). Nothing allocates per frame.
/// </summary>
public sealed class SandboxWeldModes : IToolOptions
{
    public enum Option
    {
        Socket,
        Freeform,
        COUNT,
    }

    private readonly RadialMenu.Item[] _options =
    {
        new() { Label = "Socket weld", Hint = "held parts onto sockets", Icon = 0 },
        new() { Label = "Freeform weld", Hint = "two bodies, where they are", Icon = 1 },
    };

    public RadialMenu.Item[] Options => _options;

    /// <summary>The welder welds freeform (false: socket weld, the default).</summary>
    public bool Freeform;

    public static readonly RadialIcon Icons = DrawIcon;
    RadialIcon? IToolOptions.Icons => Icons;

    public int CurrentOption => Freeform ? (int)Option.Freeform : (int)Option.Socket;

    public void Choose(int option) => Freeform = option == (int)Option.Freeform;

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
        // Freeform weld: a plate and a second one at a slant, a bead between them.
        ImDrawListExtensions.AddRectFilled(dl, P(-0.85f, 0.15f), P(0.1f, 0.5f), col, 0.04f * r, ImDrawFlags.None);
        ImDrawListExtensions.AddLine(dl, P(0.2f, 0.1f), P(0.75f, -0.7f), col, 0.3f * r);
        var bead = P(0.12f, 0.12f);
        ImDrawListExtensions.AddCircleFilled(dl, in bead, 0.16f * r, spark, 10);
        _ = bg;
    }
}
