namespace CallOfSlop;

/// <summary>
/// Test Range: free-for-all with no win condition. No score limit, no time limit, no match end, no results screen, no
/// map vote: you keep shooting bots (who respawn forever) on the same map until the host changes it from the menu or
/// leaves. Kills and deaths still count on the scoreboard, and kill XP and unlocks apply as in any match (there is no
/// end-of-match XP, since the match never ends). Plays on every normal map; bots fight and gear up as in free-for-all.
/// </summary>
public sealed class TestRangeModule : GameModeModule
{
    public override GameMode Id => GameMode.TestRange;
    public override string Name => "Test Range";
    public override string Short => "TEST";
    public override string Rules(int limit = 0) => "Free-for-all with no win condition: no score or time limit, the map stays until the host changes it.";
    public override string Description => "Shoot bots for as long as you like. Nothing ends the match or changes the map.";
    public override bool UsesMapVote => false; // (the match never ends, so a vote never opens anyway)
    public override bool HasTimeLimit => false;
    public override bool Endless => true; // Match.EndMatch refuses to end it, whatever asks

    // ScoreRule stays null: no score limit (ScoreLimits.For gives 0).

    /// <summary>No win check and no clock: nothing here ever calls EndMatch.</summary>
    public override void ServerTickPlaying(Game game, double now)
    {
    }

    public override string GoalText(Game game) => "Test Range: no score or time limit - shoot bots as long as you like";

    private int _barKills = -1, _barDeaths = -1;
    private string _barSub = NoLimitText;
    private const string NoLimitText = "No score or time limit";

    /// <summary>The match bar: "TEST RANGE" and your kills and deaths (the text is rebuilt only when they change).</summary>
    public override (string Big, string Sub)? ScoreBar(Game game)
    {
        if (game.Local is not { } me)
            return ("TEST RANGE", NoLimitText);
        if (me.Kills != _barKills || me.Deaths != _barDeaths)
        {
            _barKills = me.Kills;
            _barDeaths = me.Deaths;
            _barSub = $"You: {me.Kills} kills, {me.Deaths} deaths - no limit";
        }
        return ("TEST RANGE", _barSub);
    }

    public override string? SelfTestVerify(Game game) =>
        game.Phase == MatchPhase.Ended ? $"the match ended: {game.MatchResult}" : game.ScoreLimit != 0 ? $"a score limit of {game.ScoreLimit}" : null;
}
