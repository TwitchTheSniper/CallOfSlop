using Brutal.ImGuiApi;

namespace CallOfSlop;

/// <summary>
/// The pause menu's HOST page (modes with <see cref="GameModeModule.HostPanelInMatch"/>: Test Range): the bot count
/// and difficulty, applied at once with APPLY (Game.SetBots, HostControls.cs), and BACK TO LOBBY, which takes everyone
/// to the lobby room without leaving, so the host can change the map, mode or bots and start again.
/// </summary>
public sealed partial class Ui
{
    private bool _pauseHost; // the pause menu shows the HOST page
    private int _hostBots;
    private BotDifficulty _hostSkill;

    private void OpenPauseHost(Game game)
    {
        _pauseHost = true;
        _hostBots = game.BotCount;
        _hostSkill = game.BotSkill;
    }

    /// <summary>Returns BackToLobby when the host pressed it, else None.</summary>
    private PauseAction DrawPauseHost(Game game)
    {
        Title("Host: bots and the lobby");
        var max = Math.Max(0, Math.Min(Game.MaxPlayers, game.MapMaxPlayers) - game.HumanCount);
        _hostBots = Math.Clamp(_hostBots, 0, max);
        ImGui.SetNextItemWidth(260 * _scale);
        ImGui.SliderInt("Bots"u8, ref _hostBots, 0, max);
        ImGuiSafe.TextColored(Dim, $"{game.BotCount} playing now ({SkillName(game.BotSkill)}); {World.Map.Name} takes {max} with {game.HumanCount} {(game.HumanCount == 1 ? "person" : "people")} here");
        DifficultyPicker(ref _hostSkill);
        var changed = _hostBots != game.BotCount || _hostSkill != game.BotSkill;
        ImGui.BeginDisabled(!changed);
        if (BigButton("APPLY (bots change now)"))
            game.SetBots(_hostBots, _hostSkill); // extras leave, newcomers spawn into the match; a new difficulty replaces every bot
        ImGui.EndDisabled();
        if (_hostSkill != game.BotSkill)
            ImGuiSafe.TextColored(Dim, "A new difficulty replaces every bot.");
        ImGui.Spacing();
        ImGui.Separator();
        var action = PauseAction.None;
        if (game.CanReturnToLobby)
        {
            if (BigButton("BACK TO LOBBY (change map, keep everyone)"))
                action = PauseAction.BackToLobby;
            ImGuiSafe.TextColored(Dim, "Everyone goes to the lobby room: pick a map, mode or bots there and start again.");
        }
        else
            ImGuiSafe.TextColored(Dim, "Single player has no lobby room: leave the match to change the map.");
        ImGui.Spacing();
        if (BigButton("BACK"))
            _pauseHost = false;
        return action;
    }
}
