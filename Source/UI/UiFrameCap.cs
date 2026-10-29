using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>Settings > Video: the three Max FPS lists (FrameLimiter.cs). Each is Unlimited / Off, the presets, or a custom 20-1000.</summary>
public sealed partial class Ui
{
    private static readonly string[] FrameCapLabels = FrameCap.Presets.Select(f => $"{f} fps").ToArray();
    private static readonly string[] FrameCapInputIds = ["##capmaincustom", "##capmenucustom", "##capbgcustom"];
    private readonly bool[] _capCustomMode = new bool[3]; // "Custom..." picked: the number box stays up while typing
    private readonly int[] _capEdit = [100, 100, 100];

    private void DrawFrameCaps(Settings s, float w)
    {
        ImGui.BeginDisabled(s.VSync);
        FrameCapCombo(0, "Max FPS##capmain", "Unlimited", ref s.MaxFps, w,
            "The most frames a second the game draws; Unlimited draws as fast as it can. It applies with VSync off. With VSync on the monitor's refresh rate sets the pace and wins (a cap that does not divide the refresh rate would make frames uneven), so turn VSync off to use a cap. The match always runs at 60 ticks a second and your input is read straight after each wait, so a cap never slows the game down.");
        ImGui.EndDisabled();
        FrameCapCombo(1, "Max FPS in menus##capmenu", "Off", ref s.MaxFpsMenu, w,
            "A lower cap for the main menu, the lobby, the pause and loadout menus and the results screen, to save power (60 is plenty). Off: only the Max FPS above. It applies with VSync on too; the lowest cap that applies wins.");
        FrameCapCombo(2, "Max FPS in the background##capbg", "Off", ref s.MaxFpsBackground, w,
            "A cap while the game window is not focused or is minimised (30 saves the most power). A match you host keeps running at full speed underneath. Off: no extra cap. It applies with VSync on too; the lowest cap that applies wins.");
    }

    private void FrameCapCombo(int id, string label, string none, ref int value, float w, string tip)
    {
        var idx = Array.IndexOf(FrameCap.Presets, value);
        var custom = _capCustomMode[id] || (value > 0 && idx < 0);
        ImGui.SetNextItemWidth(w);
        if (ImGui.BeginCombo(label, custom ? "Custom" : value <= 0 ? none : FrameCapLabels[idx]))
        {
            if (ImGui.Selectable(none, value <= 0 && !custom))
                (value, _capCustomMode[id]) = (0, false);
            for (var i = 0; i < FrameCap.Presets.Length; i++)
                if (ImGui.Selectable(FrameCapLabels[i], i == idx && !custom))
                    (value, _capCustomMode[id]) = (FrameCap.Presets[i], false);
            if (ImGui.Selectable("Custom..."u8, custom))
            {
                _capCustomMode[id] = true;
                _capEdit[id] = value > 0 ? value : 100;
                value = FrameCap.Clamp(_capEdit[id]);
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiSafe.SetItemTooltip(tip);
        if (!custom)
            return;
        if (!ImGui.IsAnyItemActive())
            _capEdit[id] = value; // (not while the box is being typed in)
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * _scale);
        ImGui.InputInt(FrameCapInputIds[id], ref _capEdit[id], 0, 0); // (no step buttons: the edit applies when the box is left or Enter is pressed)
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            value = _capEdit[id] = FrameCap.Clamp(_capEdit[id]);
            _capCustomMode[id] = true;
        }
        ImGui.SameLine();
        ImGui.TextColored(Dim, "fps (20-1000; Enter to apply)"u8);
    }
}
