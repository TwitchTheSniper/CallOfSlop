using System.Globalization;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>Settings > Video "Story text colour": the colour of the story's words on screen (<see cref="Ui.Subtitles"/>).</summary>
public enum SubtitleColour : byte
{
    Cream, White, Yellow, Cyan, Green,
}

/// <summary>
/// Settings > Video "Story text": the size and colour of the story's words on the screen, apart from the menus' text size:
/// a story ride's caption (UiQuarry.cs), the opening's caption and subtitles (Program.Opening.cs) and what people say over
/// their heads (UiChat.cs). Read once a frame from the settings; nothing allocates.
/// </summary>
public sealed partial class Ui
{
    public const int SubtitleSizeMin = 75, SubtitleSizeMax = 250, SubtitleSizeDefault = 125;

    private float _subSize = SubtitleSizeDefault / 100f;
    private System.Numerics.Vector3 _subRgb = SubtitleRgb(SubtitleColour.Cream);

    /// <summary>Takes this frame's story text size and colour from the settings.</summary>
    public void Subtitles(Settings s)
    {
        _subSize = Math.Clamp(s.SubtitleSize, SubtitleSizeMin, SubtitleSizeMax) / 100f;
        _subRgb = SubtitleRgb(s.SubtitleColour);
    }

    /// <summary>The story text's pixel size for a line laid out at <paramref name="basePx"/> (times the UI scale and the setting).</summary>
    public float SubtitlePx(float basePx) => basePx * _scale * _subSize;

    /// <summary>The story text's colour.</summary>
    public System.Numerics.Vector3 SubtitleTint => _subRgb;

    public static System.Numerics.Vector3 SubtitleRgb(SubtitleColour c) => c switch
    {
        SubtitleColour.White => new(1f, 1f, 1f),
        SubtitleColour.Yellow => new(1f, 0.88f, 0.3f),
        SubtitleColour.Cyan => new(0.55f, 0.9f, 1f),
        SubtitleColour.Green => new(0.55f, 1f, 0.6f),
        _ => new(0.95f, 0.92f, 0.82f),
    };

    /// <summary>settings.txt's <c>story_text_size</c>: a percent, kept in range (anything unreadable: the default).</summary>
    public static int ParseSubtitleSize(string v) =>
        int.TryParse(v.TrimEnd('%'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? Math.Clamp(p, SubtitleSizeMin, SubtitleSizeMax) : SubtitleSizeDefault;

    /// <summary>A line of story text centred on the screen at <paramref name="y"/>, with a soft shadow under it.</summary>
    public void SubtitleLine(ImDrawListPtr dl, string text, float y, float basePx, float alpha = 1f)
    {
        var px = SubtitlePx(basePx);
        var font = ImGui.GetFont();
        var vp = ImGui.GetMainViewport();
        var size = ImGui.CalcTextSize(text) * (px / ImGui.GetFontSize());
        var at = new float2(vp.WorkPos.X + (vp.WorkSize.X - size.X) * 0.5f, y - size.Y * 0.5f);
        var off = MathF.Max(1f, px / 14f);
        var c = _subRgb;
        dl.AddText(font, px, at + new float2(off, off), new ImColor8(0, 0, 0, (byte)(Math.Clamp(alpha, 0, 1) * 200)), text);
        dl.AddText(font, px, at, new ImColor8((byte)(c.X * 255), (byte)(c.Y * 255), (byte)(c.Z * 255), (byte)(Math.Clamp(alpha, 0, 1) * 255)), text);
    }

    /// <summary>Settings > Video: the story text's size slider and colour choice.</summary>
    private void SubtitleControls(Settings s, float w)
    {
        ImGui.Text("Story text"u8);
        ImGui.SetNextItemWidth(w);
        ImGui.SliderInt("Story text size"u8, ref s.SubtitleSize, SubtitleSizeMin, SubtitleSizeMax, "%d%%"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.Text("Story text colour"u8);
        var col = (int)s.SubtitleColour;
        for (var i = 0; i <= (int)SubtitleColour.Green; i++)
        {
            ImGui.SameLine();
            var rgb = SubtitleRgb((SubtitleColour)i);
            ImGui.PushStyleColor(ImGuiCol.Text, new float4(rgb.X, rgb.Y, rgb.Z, 1f));
            if (ImGui.RadioButton($"{(SubtitleColour)i}##subcol", ref col, i))
                s.SubtitleColour = (SubtitleColour)col;
            ImGui.PopStyleColor();
        }
        ImGui.TextColored(Dim, "The words on screen during the story: ride captions, the opening's lines and what people say over their heads."u8);
    }
}
