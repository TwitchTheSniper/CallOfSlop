using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>The "Bot gear" host setting on the Single player and Host screens (rules in Source/Game/BotGear.cs).</summary>
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
        ImGuiSafe.TextColored(Dim, s.BotGear switch
        {
            BotGearMode.Everything => "Bots carry any weapon, attachment, perk, equipment and kill streak.",
            BotGearMode.Starter => "Bots carry only what a level-1 player has.",
            _ => "Bots carry what the players' average level has unlocked (a few levels either way per bot).",
        });
    }
}
