using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The PRESTIGE button under the level bar in the lobby, and its confirmation dialog (Progression.cs, Cosmetics.cs).
/// Only enabled at max level with a prestige left; the dialog says what resets and which hat you get.
/// </summary>
public sealed partial class Ui
{
    private const string PrestigePopup = "Prestige?##prestige";

    /// <summary>The level heading's prestige prefix: "P2  " (empty before the first prestige).</summary>
    private static string PrestigePrefix(Progress p) => p.Prestige > 0 ? $"P{p.Prestige}  " : "";

    private void PrestigeButton()
    {
        var p = Progress;
        var can = p.CanPrestige;
        ImGui.BeginDisabled(!can);
        var label = p.Prestige >= Cosmetics.MaxPrestige ? "PRESTIGE (MAX)" : $"PRESTIGE {p.Prestige + 1}";
        if (ImGui.Button(label, new float2(180 * _scale, 30 * _scale)))
            ImGui.OpenPopup(PrestigePopup);
        ImGui.EndDisabled();
        ImGui.SameLine();
        var next = Cosmetics.Get(Cosmetics.HatForPrestige(p.Prestige + 1));
        ImGuiSafe.TextColored(can ? Orange : Dim,
            p.Prestige >= Cosmetics.MaxPrestige ? "Max prestige: every hat unlocked."
            : can ? $"Reset to level 1 for the {next?.Name ?? "next hat"}."
            : $"Reach level {CallOfSlop.Progress.MaxLevel} to prestige (unlocks the {next?.Name ?? "next hat"}).");

        ImGui.SetNextWindowSize(new float2(520 * _scale, 0));
        if (ImGui.BeginPopupModal(PrestigePopup, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoResize))
        {
            ImGui.PushFont(default, 20);
            ImGuiSafe.TextColored(Orange, $"PRESTIGE {p.Prestige + 1}");
            ImGui.PopFont();
            ImGui.PushTextWrapPos(0);
            ImGuiSafe.TextWrapped(p.PrestigeSummary());
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            if (ImGui.Button("Yes, prestige"u8, new float2(160 * _scale, 30 * _scale)))
            {
                if (p.DoPrestige(Environment.TickCount64 / 1000.0))
                {
                    LoadoutStore.Selected = 0; // one loadout slot at level 1 (locked picks fall back to defaults when used)
                    LoadoutStore.Save();
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"u8, new float2(120 * _scale, 30 * _scale)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
