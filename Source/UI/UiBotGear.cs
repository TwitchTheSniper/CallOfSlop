using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>The "Bot gear" host setting on the Single player screen (hosting: the lobby room's Bots tab, UiLobbyRoom.cs) (rules in Source/Game/BotGear.cs).</summary>
public sealed partial class Ui
{
    /// <summary>Bot gear: Match players / Everything / Starter, under the bot difficulty.</summary>
    private void BotGearPicker(Settings s)
    {
        var v = (int)s.BotGear;
        ImGui.Text("Bot gear"u8);
        ImGui.SameLine();
        ImGui.RadioButton("Match players##botgear"u8, ref v, (int)BotGearMode.MatchPlayers);
        ImGui.SameLine();
        ImGui.RadioButton("Everything##botgear"u8, ref v, (int)BotGearMode.Everything);
        ImGui.SameLine();
        ImGui.RadioButton("Starter##botgear"u8, ref v, (int)BotGearMode.Starter);
        s.BotGear = (BotGearMode)v;
        ImGuiSafe.TextColored(Dim, BotGearHint(s.BotGear)); // (UiLobbyRoom.cs)
    }
}
