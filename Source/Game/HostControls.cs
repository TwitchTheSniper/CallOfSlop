using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The host's mid-session controls (the pause menu's HOST panel, UiHostPanel.cs; the lobby room uses the same calls):
///   - <see cref="SetBots"/>: how many bots and how good, in any phase. New bots spawn straight into a running match,
///     a new difficulty replaces every bot; everyone learns the roster from the usual <see cref="Msg.Roster"/>.
///   - <see cref="ReturnToLobbyNow()"/>: back to the lobby room without leaving, so the host can change the map, mode
///     or bots and start again, people still connected. Clients see it as a match start in the Lobby phase, the same
///     packet as "Return to lobby between matches". Test Range needs it: its match never ends on its own.
/// Nothing here adds a message or changes the wire format.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// Host (server), any phase: how many bots (capped by the map and the people here) and how good. A new difficulty
    /// replaces the bots; otherwise only the difference leaves or joins. Returns true when the roster changed.
    /// </summary>
    public bool SetBots(int count, BotDifficulty skill)
    {
        if (!IsServer)
            return false;
        count = Math.Clamp(count, 0, Math.Max(0, Math.Min(MaxPlayers, MapMaxPlayers) - HumanCount));
        var replace = skill != _botSkill;
        _botSkill = skill;
        WantedBots = count;
        var now = Brutal.GlfwApi.Glfw.Time;
        var removed = 0;
        foreach (var bot in Players.Values.Where(p => p.Bot != null).OrderByDescending(p => p.Id).ToList())
        {
            if (!replace && BotCount <= count)
                break;
            Players.Remove(bot.Id);
            OnModePlayerLeave(bot);
            removed++;
        }
        if (removed > 0)
            BroadcastRoster();
        var added = 0;
        while (BotCount < count && FreeId() != null && AddBot(skill, quiet: true))
            added++; // AddBot spawns the bot: in a running match it is in the fight at once
        if (InLobby)
            foreach (var p in Players.Values)
                if (p.Alive)
                    PlaceWaiting(p, now); // the lobby room: nobody stands in the map
        if (IsTeamMode && (removed > 0 || added > 0))
            AssignTeams();
        MarkLobbyDirty(); // the lobby state carries the bot count and difficulty
        if (!InLobby && (removed > 0 || added > 0)) // (the lobby room shows its own bot line)
            Log(replace ? $"Bots: {BotCount} ({skill})" : added > 0 ? $"{added} bot{(added == 1 ? "" : "s")} joined ({skill})" : $"{removed} bot{(removed == 1 ? "" : "s")} left", true);
        return removed > 0 || added > 0;
    }

    /// <summary>The host can go back to the lobby room right now (hosting with the room, and not already in it).</summary>
    public bool CanReturnToLobby => IsServer && LobbyEnabled && !InLobby;

    /// <summary>Host: back to the lobby room now, everyone still connected (the pause menu's BACK TO LOBBY).</summary>
    public bool ReturnToLobbyNow() => ReturnToLobbyNow(Brutal.GlfwApi.Glfw.Time);

    /// <summary><see cref="ReturnToLobbyNow()"/> with an explicit clock (self-test).</summary>
    public bool ReturnToLobbyNow(double now)
    {
        if (!CanReturnToLobby)
            return false;
        ModeEvent("The host went back to the lobby", Sfx.HitTick);
        StartNewMatch(now, toLobby: true); // no vote: the host picks the next map in the room
        return true;
    }
}
