namespace CallOfSlop;

/// <summary>The team picker's smoke-test step (map 0, right after the loadout overview shot).</summary>
public static unsafe partial class Program
{
    // ---- Smoke step: switch the warmup to Team Deathmatch, open the TEAMS window beside the loadout screen,
    // screenshot it (smoke_teams.bmp), then put the mode back so the rest of the smoke run is unchanged.

    private static int _teamSmoke;
    private static double _teamSmokeAt;
    private static GameMode _teamSmokeMode;

    /// <summary>One tick of the step. Returns true while it's busy (the caller waits for it).</summary>
    private static bool SmokeTeamsTick(Game? game, double now)
    {
        if (game is not { IsServer: true })
            return false;
        switch (_teamSmoke)
        {
            case 0: // let the loadout overview shot be taken first
                _teamSmokeAt = now;
                _teamSmoke = 1;
                return true;
            case 1 when now - _teamSmokeAt > 0.3:
                _teamSmokeMode = game.Mode;
                game.Mode = GameMode.TeamDeathmatch;
                Ui.SmokeShowTeams(true);
                _teamSmokeAt = now;
                _teamSmoke = 2;
                return true;
            case 2 when now - _teamSmokeAt > 0.8:
                SmokeShot("teams");
                Console.WriteLine($"[smoke] teams: TEAMS window in {GameModes.Name(game.Mode)} ({game.Phase}), selection {TeamRules.Name(game.TeamSelection)}, "
                    + $"max difference {game.MaxTeamDiff}, Blue {game.Players.Values.Count(p => p.Team == 0)} v Red {game.Players.Values.Count(p => p.Team == 1)}");
                _teamSmokeAt = now;
                _teamSmoke = 3;
                return true;
            case 3 when now - _teamSmokeAt > 0.3:
                game.Mode = _teamSmokeMode;
                Ui.SmokeShowTeams(false);
                _teamSmoke = 4;
                return false;
            case 1 or 2 or 3:
                return true;
            default:
                return false;
        }
    }
}
