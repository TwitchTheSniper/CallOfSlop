using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// The lobby room's horde stress settings (a map whose Zombies section has "stress": Dev/HordeStress.json;
/// Source/Game/ZombiesCrowdStress.cs): the horde size (presets or a custom number) and endless waves. Host settings,
/// kept in settings.txt; the server's own (clients don't need them).
/// </summary>
public sealed partial class Ui
{
    private static readonly string[] StressSizeLabels = Game.StressSizeChoices.Select(n => $"{n:N0} zombies").ToArray();
    private int _stressCustom = 15000;

    private void DrawRoomStress(Game game, Settings s, bool host, ZombiesMap zm)
    {
        var size = game.StressSizeFor(zm);
        var idx = Array.IndexOf(Game.StressSizeChoices, game.StressSize);
        var custom = game.StressSize > 0 && idx < 0;
        var label = game.StressSize <= 0 ? $"{size:N0} (the map's own)" : custom ? $"Custom: {size:N0}" : StressSizeLabels[idx];
        ImGui.SetNextItemWidth(200 * _scale);
        if (ImGui.BeginCombo("Horde size##room"u8, label))
        {
            if (ImGui.Selectable($"{zm.Crowd:N0} (the map's own)", game.StressSize <= 0) && host)
                SetStressSize(game, s, 0);
            for (var i = 0; i < Game.StressSizeChoices.Length; i++)
                if (ImGui.Selectable(StressSizeLabels[i], Game.StressSizeChoices[i] == game.StressSize) && host)
                    SetStressSize(game, s, Game.StressSizeChoices[i]);
            if (ImGui.Selectable("Custom..."u8, custom) && host)
                SetStressSize(game, s, Math.Clamp(_stressCustom, 1, Game.StressMax));
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGuiSafe.SetItemTooltip("The whole horde, split across the three staging areas on the far ridge. They wait out a countdown, pour down into the valley, merge and come for you. Big sizes are a stress test: watch the F9 window, and the log has a summary per size.");
        if (custom && host)
        {
            ImGui.SetNextItemWidth(200 * _scale);
            _stressCustom = game.StressSize;
            if (ImGui.InputInt("Custom size##room"u8, ref _stressCustom, 1000, 10000))
                SetStressSize(game, s, Math.Clamp(_stressCustom, 1, Game.StressMax));
        }
        var endless = game.StressEndless;
        if (ImGui.Checkbox("Endless waves##room"u8, ref endless) && host)
        {
            game.StressEndless = endless;
            s.ZombieStressEndless = endless;
        }
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, endless ? $"another {size:N0} every {zm.Stress!.WaveSeconds:0} s, up to {Game.StressMax:N0} at once" : "one horde a round");
    }

    private static void SetStressSize(Game game, Settings s, int size)
    {
        game.StressSize = size;
        s.ZombieStressSize = size;
    }
}
