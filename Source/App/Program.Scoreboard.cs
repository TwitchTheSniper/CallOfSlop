namespace CallOfSlop;

/// <summary>The scoreboard's smoke-test step (map 0, after the name tags).</summary>
public static unsafe partial class Program
{
    private static int _smokeScoreStep;
    private static double _smokeScoreStart;

    /// <summary>
    /// Smoke step: holds the scoreboard (Tab) open for a moment and screenshots it (smoke_scoreboard.bmp): the Level column
    /// (prestige, level, hat; BOT for bots) and no health column. Logs the column headers.
    /// </summary>
    private static void SmokeScoreboard(double now)
    {
        switch (_smokeScoreStep)
        {
            case 0:
                _smokeScoreStart = now;
                Ui.SmokeScoreboard = true;
                _smokeScoreStep = 1;
                break;
            case 1 when now - _smokeScoreStart > 0.5:
                SmokeShot("scoreboard");
                var compact = _game.Players.Count > Ui.BigScoreboardFrom;
                Console.WriteLine($"[smoke] scoreboard: {_game.Players.Count} players ({(compact ? "compact" : "full")}), columns "
                    + string.Join(" | ", ScoreboardModel.Headers(_game.Module, compact)));
                _smokeScoreStep = 2;
                break;
            case 2 when now - _smokeScoreStart > 0.7:
                Ui.SmokeScoreboard = false;
                _smokeScoreStep = 3;
                break;
        }
    }
}
