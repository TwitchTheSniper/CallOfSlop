using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The robot shell's link-up through this PC's player's eyes (Game.ShellLinkView, GameRobotShell.cs; Karl, 2026-11-14): the
/// chair's helmet comes down over the view from the top, its visor's edges closing in from the sides, then everything is black
/// but for a few small lamps inside it, until they open their eyes as the robot. A handful of rectangles and dots on the
/// foreground list: nothing allocates.
/// </summary>
public sealed partial class Ui
{
    private static readonly (float X, float Y, byte R, byte G, byte B, float Rate)[] LinkLamps =
    [
        (-0.30f, 0.78f, 70, 230, 120, 1.7f), (-0.27f, 0.78f, 70, 230, 120, 2.3f), (-0.24f, 0.78f, 240, 170, 60, 0.9f),
        (0.24f, 0.78f, 60, 200, 235, 3.1f), (0.27f, 0.78f, 60, 200, 235, 1.3f), (0.30f, 0.78f, 235, 70, 60, 0.6f),
        (0f, 0.22f, 60, 200, 235, 0.5f),
    ];

    private void DrawShellLink(Game game)
    {
        var now = Brutal.GlfwApi.Glfw.Time;
        if (!game.ShellLinkView(now, out var helmet, out var dark) || PauseOpen || MenuOpen)
            return; // (the Esc menu shows over it: the link-up goes on underneath)
        var size = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetForegroundDrawList();
        var black = new ImColor8(4, 5, 7, 255);
        if (helmet > 0)
        {
            var e = 1 - (1 - helmet) * (1 - helmet); // (eases in as it settles)
            var top = size.Y * e;
            var side = size.X * 0.5f * e * e;
            ImDrawListExtensions.AddRectFilled(dl, new float2(0, 0), new float2(size.X, top), black, 0, ImDrawFlags.None);
            ImDrawListExtensions.AddRectFilled(dl, new float2(0, 0), new float2(side, size.Y), black, 0, ImDrawFlags.None);
            ImDrawListExtensions.AddRectFilled(dl, new float2(size.X - side, 0), new float2(size.X, size.Y), black, 0, ImDrawFlags.None);
            if (e < 0.999f)
                ImDrawListExtensions.AddRectFilled(dl, new float2(side, top - 6 * _scale), new float2(size.X - side, top), new ImColor8(48, 54, 61, 255), 0, ImDrawFlags.None); // (the helmet's rim)
        }
        if (dark <= 0)
            return;
        var a = (byte)(Math.Clamp(dark, 0f, 1f) * 255);
        ImDrawListExtensions.AddRectFilled(dl, new float2(0, 0), size, new ImColor8(4, 5, 7, a), 0, ImDrawFlags.None);
        var r = Math.Max(2f, 2.5f * _scale);
        foreach (var l in LinkLamps)
        {
            var on = 0.35f + 0.65f * MathF.Max(0, MathF.Sin((float)now * l.Rate * MathF.Tau));
            var c = new float2(size.X * (0.5f + l.X), size.Y * l.Y);
            ImDrawListExtensions.AddCircleFilled(dl, c, r, new ImColor8(l.R, l.G, l.B, (byte)(a * on)), 8);
        }
    }
}
