namespace CallOfSlop;

/// <summary>Free-for-all: the original rules. First to 11 kills (scaled with the player count), win by 2, 10 minutes max.</summary>
public sealed class FreeForAllModule : GameModeModule
{
    /// <summary>The classic limit (8 players) and how it grows: measured bot matches (docs/SCORE-SCALING.md).</summary>
    public static readonly ScoreLimits.Rule Rule = new(Game.WinKills, 0.85, 1, 7);

    public override GameMode Id => GameMode.FreeForAll;
    public override string Name => "Free-for-all";
    public override string Short => "FFA";
    public override string Rules(int limit = 0) => $"First to {(limit > 0 ? limit : Game.WinKills)} kills, win by {Game.WinMargin}.";
    public override ScoreLimits.Rule? ScoreRule => Rule;

    public override string GoalText(Game game) => $"First to {game.ScoreLimit} kills, win by {Game.WinMargin}, {Game.MatchSeconds / 60:0} minutes max";

    // ServerTickPlaying: the base class's free-for-all win check. No objectives, no teams, no HUD panel.
}
