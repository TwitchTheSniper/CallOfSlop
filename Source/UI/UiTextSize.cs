using System.Globalization;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Settings > Video > Text size: how big menus, the HUD and every ImGui text are drawn. The whole UI scale is the window's
/// height scale (<see cref="TargetScale"/>: 1.2x at 1080p) times this setting, so layouts written in <c>N * _scale</c>
/// pixels grow with the text. Auto follows the monitor's own scaling (GLFW's content scale: Windows' display scaling,
/// Xft.dpi on Linux) where it asks for more than the resolution already gives, so a 1080p laptop at 150% starts at 150%
/// and a 4K desktop at 150% stays at 100% (its height already doubles the text). Text stays sharp at every size: ImGui
/// 1.92 bakes each glyph at the size it is drawn (its atlas grows in the renderer's texture protocol, ImGuiRenderer.cs),
/// so a new scale is applied between frames and sizes no longer drawn are let go a few frames later.
/// </summary>
public sealed partial class Ui
{
    public const int TextSizeMin = 75, TextSizeMax = 200, TextSizeStep = 5;

    /// <summary>The presets beside the slider (percent).</summary>
    private static readonly (string Name, int Percent)[] TextPresets =
    [
        ("Small", 85),
        ("Normal", 100),
        ("Large", 125),
        ("Extra large", 150),
    ];

    /// <summary>The player's text size (Settings.TextSize): 0 = Auto, else a percent.</summary>
    public int TextSize;

    private float _autoText = 1f; // Auto's multiplier for this monitor (NoteMonitor)
    private int _textSlider; // the slider's value (while it is dragged, applied on release)
    private bool _textDragging;
    private int _textWant = -1; // a preset button's pick, applied after the drawing
    private int _compactIn; // frames until the atlas drops the sizes a scale change left unused (0 = nothing to do)

    /// <summary>The multiplier the setting gives now: Auto's for this monitor, or the chosen percent.</summary>
    public float TextFactor => TextSize == 0 ? _autoText : TextSize / 100f;

    /// <summary>Auto's percent on this monitor (shown beside the setting).</summary>
    public int AutoTextPercent => (int)MathF.Round(_autoText * 100);

    /// <summary>A percent rounded to the slider's steps and kept in its range.</summary>
    public static int SnapTextSize(int percent) =>
        Math.Clamp((int)MathF.Round(percent / (float)TextSizeStep) * TextSizeStep, TextSizeMin, TextSizeMax);

    /// <summary>settings.txt's <c>text_size</c>: "auto" (or anything unreadable) is 0, else a percent.</summary>
    public static int ParseTextSize(string v) =>
        int.TryParse(v.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0 ? SnapTextSize(p) : 0;

    public static string TextSizeKey(int size) => size <= 0 ? "auto" : SnapTextSize(size).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the monitor's scaling for Auto: when the window opens, after a display change and when the window's content
    /// scale changes (moved to another monitor, or the scaling changed). Never per frame: a monitor's video mode is a call
    /// into the display driver (UiPlatformFrame.cs). On a Mac the window is laid out in points, which already carry the
    /// display's scaling, so Auto is 100% there.
    /// </summary>
    public void NoteMonitor(GlfwWindow window, GlfwMonitor monitor)
    {
        var f = 1f;
        if (!OperatingSystem.IsMacOS() && !monitor.IsNull())
        {
            var cs = window.ContentScale;
            var dpi = MathF.Max(cs.X, cs.Y);
            var h = monitor.VideoMode.Height;
            // What the OS asks for, over what the height scale already gives (a 1080p monitor at 100% is the reference).
            if (h > 0 && float.IsFinite(dpi) && dpi > 0)
                f = Math.Clamp(dpi / (h / 1080f), 1f, 1.5f);
            f = MathF.Round(f * 20) / 20; // (whole 5% steps, like the slider)
            Console.WriteLine($"[ui] monitor scaling {dpi * 100:0}% at {h} px high: Auto text size {f * 100:0}%");
        }
        _autoText = f;
    }

    /// <summary>Between frames (BeginFrame, before NewFrame): drops baked font sizes a scale change left unused.</summary>
    private void CompactFontsIfDue()
    {
        if (_compactIn <= 0 || --_compactIn > 0)
            return;
        ImGui.GetIO().Fonts.CompactCache(); // (unused bakes go, the texture is repacked; ImGui uploads it in the next draw data)
    }

    /// <summary>
    /// Between frames, when the scale changes: every window the player can size keeps its size in proportion to the text,
    /// kept inside the screen. A window sized once (<c>ImGuiCond.FirstUseEver</c>) keeps its pixels otherwise, so a
    /// window opened at 75% would clip its content at 200%. Child windows and auto-sized ones are sized by their callers
    /// each frame.
    /// </summary>
    private static unsafe void RescaleWindows(float ratio)
    {
        var ctx = ImGui.GetCurrentContext();
        if (ctx.IsNull() || !float.IsFinite(ratio) || ratio <= 0)
            return;
        var display = ImGui.GetIO().DisplaySize;
        if (display.X <= 0 || display.Y <= 0)
            return;
        const ImGuiWindowFlags Skip = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Tooltip | ImGuiWindowFlags.Popup;
        var windows = ctx.Windows;
        for (var i = 0; i < windows.Count; i++)
        {
            var win = windows[i];
            if (win.IsNull() || (win.Flags & Skip) != 0)
                continue;
            var size = win.SizeFull;
            size = new float2(MathF.Min(size.X * ratio, display.X), MathF.Min(size.Y * ratio, display.Y));
            win.SizeFull = size;
            win.Size = size;
            var pos = win.Pos;
            pos = new float2(Math.Clamp(pos.X, 0, MathF.Max(0, display.X - size.X)), Math.Clamp(pos.Y, 0, MathF.Max(0, display.Y - size.Y)));
            win.Pos = pos;
        }
    }

    /// <summary>The Text size control (Settings > Video). Buttons only note their pick; it is applied after the drawing.</summary>
    private void TextSizeControl(Settings s, float w)
    {
        ImGui.Text("Text size"u8);
        var auto = s.TextSize == 0;
        if (ImGui.RadioButton($"Auto ({AutoTextPercent}%)##text", auto))
            _textWant = 0;
        foreach (var (name, pct) in TextPresets)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{name}##text", !auto && s.TextSize == pct))
                _textWant = pct;
        }
        if (!_textDragging)
            _textSlider = auto ? SnapTextSize(AutoTextPercent) : s.TextSize;
        ImGui.SetNextItemWidth(w);
        ImGui.SliderInt("##textsize"u8, ref _textSlider, TextSizeMin, TextSizeMax, "%d%%"u8);
        _textDragging = ImGui.IsItemActive();
        if (ImGui.IsItemDeactivatedAfterEdit())
            _textWant = SnapTextSize(_textSlider); // on release, so the menu doesn't resize under the mouse
        ImGui.TextColored(Dim, $"{TextSizeMin}% to {TextSizeMax}%. Menus and the HUD grow with the text. Auto follows your monitor's scaling.");

        if (_textWant >= 0)
        {
            s.TextSize = _textWant == 0 ? 0 : SnapTextSize(_textWant); // (read by the next BeginFrame, which rescales between frames)
            _textWant = -1;
        }
    }
}
