using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// A small, dim fps readout in the top-right corner (Settings > Video: "Show FPS counter", on by default).
/// Averaged over half a second so the number is readable; drawn on the foreground list so it sits over everything.
/// </summary>
public static class FpsCounter
{
    private static double _windowStart = -1, _lastShown;
    private static int _frames;
    private static string _text = "";

    public static void Draw(double now)
    {
        _frames++;
        if (_windowStart < 0)
            _windowStart = now;
        var span = now - _windowStart;
        if (span >= 0.5)
        {
            _lastShown = _frames / span;
            _text = $"{_lastShown:0} fps";
            _frames = 0;
            _windowStart = now;
        }
        if (_text.Length == 0)
            return;
        var size = ImGui.GetIO().DisplaySize;
        var textSize = ImGui.CalcTextSize(_text);
        var pos = new float2(size.X - textSize.X - 8, 4);
        var dl = ImGui.GetForegroundDrawList();
        dl.AddText(pos + new float2(1, 1), new ImColor8(0, 0, 0, 140), _text);
        dl.AddText(pos, new ImColor8(200, 205, 210, 170), _text);
    }
}
