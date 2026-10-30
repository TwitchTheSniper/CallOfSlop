using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the paint tool's colour picker (board item dev-sandbox-paint-tool-pp01; "Custom..." on the tool wheel's ring
/// round the paint tool): a small window with ImGui's hue / saturation / value picker, the recent colours as swatches and
/// Use / Close buttons. Opens with the mouse free; Use puts the colour in the can (and remembers it), Close and Esc leave the
/// can as it was. Drawn without allocating (the ids are literals).
/// </summary>
public sealed partial class Ui
{
    public bool SandboxPaintPickerOpen { get; private set; }

    private float3 _ppCol = new(0.94f, 0.59f, 0.16f);
    private uint _ppResult;
    private bool _ppUse;

    private static readonly string[] PpRecentIds = ["##ppr0", "##ppr1", "##ppr2", "##ppr3", "##ppr4", "##ppr5", "##ppr6", "##ppr7"];

    /// <summary>Opens the picker on a colour (a set colour; else the can's orange).</summary>
    public void OpenSandboxPaintPicker(uint start)
    {
        SandboxPaintPickerOpen = true;
        _ppUse = false;
        if (ConstructPaint.IsSet(start))
        {
            var v = ConstructPaint.ToVector(start);
            _ppCol = new float3(v.X, v.Y, v.Z);
        }
    }

    public void CloseSandboxPaintPicker() => SandboxPaintPickerOpen = false;

    /// <summary>The picker's colour as a paint colour (the self-test sets and reads it).</summary>
    public uint SandboxPaintPickerColour
    {
        get => ConstructPaint.FromVector(new System.Numerics.Vector3(_ppCol.X, _ppCol.Y, _ppCol.Z));
        set
        {
            var v = ConstructPaint.ToVector(value);
            _ppCol = new float3(v.X, v.Y, v.Z);
        }
    }

    /// <summary>
    /// The picker window. Returns true while it stays open; <paramref name="chosen"/> is the colour when Use was pressed
    /// this frame (else 0). <paramref name="recent"/> are the can's recent colours (newest first, 0: none).
    /// </summary>
    public bool DrawSandboxPaintPicker(ReadOnlySpan<uint> recent, out uint chosen)
    {
        chosen = 0;
        if (!SandboxPaintPickerOpen)
            return false;
        var open = true;
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X * 0.5f - 130 * _scale, vp.WorkPos.Y + vp.WorkSize.Y * 0.5f - 160 * _scale), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new float2(260 * _scale, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        if (ImGui.Begin("Paint colour###sbpaintpick"u8, ref open, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.SetNextItemWidth(220 * _scale);
            ImGui.ColorPicker3("##ppcol"u8, ref _ppCol, ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel);
            ImGui.TextColored(Dim, "Recent"u8);
            var sw = ImGui.GetFrameHeight() * 0.9f;
            for (var k = 0; k < recent.Length && k < PpRecentIds.Length; k++)
            {
                if (k > 0)
                    ImGui.SameLine(0, 3 * _scale);
                var c = recent[k];
                var v = c != 0 ? ConstructPaint.ToVector(c) : new System.Numerics.Vector3(0.15f);
                ImGui.BeginDisabled(c == 0);
                if (ImGui.ColorButton(PpRecentIds[k], new float4(v.X, v.Y, v.Z, 1f), ImGuiColorEditFlags.NoTooltip, new float2(sw, sw)))
                    _ppCol = new float3(v.X, v.Y, v.Z);
                ImGui.EndDisabled();
            }
            if (ImGui.Button("Use"u8, new float2(100 * _scale, 0)))
            {
                chosen = SandboxPaintPickerColour;
                open = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Close"u8, new float2(100 * _scale, 0)))
                open = false;
        }
        ImGui.End();
        if (!open)
            SandboxPaintPickerOpen = false;
        return SandboxPaintPickerOpen;
    }
}
