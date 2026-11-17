using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A worn robot shell through this PC's player's eyes (Game.ShellWearFrame, GameShellWear.cs; Karl, 2026-11-17): the optics' static and
/// scan lines, the processor's glitch (a few bands of the view torn sideways, a cyan and red fringe), the power cell's brown-out (the
/// view dims). Rectangles and dots on the foreground list, placed by a hash of the frame's seed: nothing allocates.
/// </summary>
public sealed partial class Ui
{
    private void DrawShellWear(Game game)
    {
        var now = Brutal.GlfwApi.Glfw.Time;
        if (!game.ShellWearFrame(now, out var v) || PauseOpen || MenuOpen)
            return;
        var size = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetForegroundDrawList();
        if (v.Static > 0)
        {
            // Scan lines, every 4 px, and specks of snow: faint when the optics are only worn.
            var lineA = (byte)(14 + 50 * v.Static);
            var step = Math.Max(3f, 4f * _scale);
            for (var y = 0f; y < size.Y; y += step)
                ImDrawListExtensions.AddRectFilled(dl, new float2(0, y), new float2(size.X, y + 1), new ImColor8(0, 0, 0, lineA), 0, ImDrawFlags.None);
            var specks = (int)(30 + 600 * v.Static * v.Static);
            var r = Math.Max(1f, 1.5f * _scale);
            for (var k = 0; k < specks; k++)
            {
                var h = WearHash(v.Seed * 7919 + k);
                var x = (h & 0xFFFF) / 65535f * size.X;
                var y = (h >> 16 & 0xFFFF) / 65535f * size.Y;
                var g = (byte)(150 + (h >> 8 & 0x63));
                ImDrawListExtensions.AddRectFilled(dl, new float2(x, y), new float2(x + r, y + r), new ImColor8(g, g, g, (byte)(40 + 120 * v.Static)), 0, ImDrawFlags.None);
            }
            if (v.Static > 0.9f)
                ImDrawListExtensions.AddRectFilled(dl, new float2(0, 0), size, new ImColor8(40, 46, 44, (byte)(140 * (v.Static - 0.9f) * 10)), 0, ImDrawFlags.None); // (failed: near blind)
        }
        if (v.Tear > 0)
        {
            // A few bands torn sideways: each a dark strip with a cyan edge one side and a red the other.
            var bands = 3 + (int)(5 * v.Tear);
            for (var k = 0; k < bands; k++)
            {
                var h = WearHash(v.Seed * 104729 + k * 31);
                var y = (h & 0xFFFF) / 65535f * size.Y;
                var hh = (4 + (h >> 16 & 0x1F)) * _scale * (0.6f + v.Tear);
                var shift = ((h >> 21 & 0x3F) - 32) * _scale * v.Tear * 1.5f;
                var a = (byte)(60 + 110 * v.Tear);
                ImDrawListExtensions.AddRectFilled(dl, new float2(0, y), new float2(size.X, y + hh), new ImColor8(10, 12, 14, (byte)(a * 0.7f)), 0, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, new float2(MathF.Max(0, shift), y), new float2(MathF.Max(0, shift) + size.X * 0.35f, y + 2 * _scale), new ImColor8(60, 220, 235, a), 0, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, new float2(size.X * 0.5f + shift, y + hh - 2 * _scale), new float2(size.X, y + hh), new ImColor8(235, 70, 60, a), 0, ImDrawFlags.None);
            }
        }
        if (v.Dim > 0)
            ImDrawListExtensions.AddRectFilled(dl, new float2(0, 0), size, new ImColor8(4, 5, 7, (byte)(255 * Math.Clamp(v.Dim, 0f, 1f))), 0, ImDrawFlags.None);
    }

    private static uint WearHash(int x)
    {
        var h = (uint)x;
        h ^= h >> 16;
        h *= 0x7feb352d;
        h ^= h >> 15;
        h *= 0x846ca68b;
        h ^= h >> 16;
        return h;
    }
}
