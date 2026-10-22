namespace CallOfSlop;

/// <summary>The host's score limit choice (Lobby: "Score limit"). Saved in settings.txt as score_limit=auto|fixed.</summary>
public enum ScoreLimitMode : byte
{
    Auto, // scales with the number of players so bigger matches take about as long (the default)
    Fixed, // the classic limits whatever the player count: 11 kills, 40 team kills, 150 hill points, 3 captures
}

/// <summary>
/// Score limits per mode and player count. The classic limits were tuned for <see cref="BasePlayers"/> (8: you and 7
/// bots). With Auto, a match with N players (people + bots) plays to
///   limit(N) = round(Base * (n / 8) ^ Exponent) to a multiple of Step, at least Min,  n = N clamped to 2..32,
/// so the limit at 8 players is the classic one and it never goes down as N goes up. The exponents come from
/// measured bot matches (docs/SCORE-SCALING.md; the self-test's "Score scaling" section measures them again).
/// </summary>
public static class ScoreLimits
{
    /// <summary>The player count the classic (Fixed) limits were tuned for.</summary>
    public const int BasePlayers = Game.ClassicPlayers;

    /// <summary>One mode's rule: the classic limit, how fast it grows with players, its rounding step and floor.</summary>
    public readonly record struct Rule(int Base, double Exponent, int Step, int Min);

    // Measured with bot matches on three maps (docs/SCORE-SCALING.md): the limit that takes as long at N players as the
    // classic one does at 8 grows about as (N/8)^0.85 in free-for-all (more targets around, and the leader is the best
    // of more players) and (N/8)^0.8 in Team Deathmatch. King of the Hill and Capture the Flag showed no clear trend
    // (bots rarely finish them inside 10 minutes), so they keep their classic limits. Each rule lives with its mode
    // (Source/Game/Modes, GameModeModule.ScoreRule); these are the shipped ones by name.
    public static readonly Rule FreeForAll = FreeForAllModule.Rule;
    public static readonly Rule TeamDeathmatch = TeamDeathmatchModule.Rule;
    public static readonly Rule KingOfTheHill = KingOfTheHillModule.Rule;
    public static readonly Rule CaptureTheFlag = CaptureTheFlagModule.Rule;

    /// <summary>The modes that end at a score: free-for-all and the three team modes (not Gun Game or Zombies).</summary>
    public static bool HasLimit(GameMode m) => RuleFor(m) != null;

    public static Rule? RuleFor(GameMode m) => GameModeRegistry.Get(m).ScoreRule;

    /// <summary>The classic (Fixed) limit of a mode, or 0 for a mode without one.</summary>
    public static int Classic(GameMode m) => RuleFor(m)?.Base ?? 0;

    /// <summary>The limit for <paramref name="players"/> people + bots (0 for a mode without one).</summary>
    public static int For(GameMode m, int players, bool auto = true)
    {
        if (RuleFor(m) is not { } r)
            return 0;
        return auto ? Scaled(r, players) : r.Base;
    }

    /// <summary>The formula (see the class comment). Non-decreasing in <paramref name="players"/>.</summary>
    public static int Scaled(Rule r, int players)
    {
        var n = Math.Clamp(players, 2, Map.HardMaxPlayers);
        var raw = r.Base * Math.Pow(n / (double)BasePlayers, r.Exponent);
        var step = Math.Max(1, r.Step);
        var rounded = (int)Math.Round(raw / step, MidpointRounding.AwayFromZero) * step;
        return Math.Max(r.Min, rounded);
    }

    /// <summary>What a mode's score counts ("kills", "points", "captures").</summary>
    public static string Unit(GameMode m) => GameModeRegistry.Get(m).ScoreUnit;

    public static string ChoiceName(ScoreLimitMode c) => c == ScoreLimitMode.Fixed ? "Fixed" : "Auto (scales with players)";

    /// <summary>One line for the lobby: what the choice means for this mode ("First to 11 kills with 8 players, 15 with 16...").</summary>
    public static string Describe(GameMode m, ScoreLimitMode c)
    {
        if (RuleFor(m) is not { } r)
            return "";
        if (c == ScoreLimitMode.Fixed || r.Exponent <= 0)
            return $"First to {r.Base} {Unit(m)} whatever the player count.";
        return $"Grows with the player count: {Scaled(r, 4)} {Unit(m)} with 4 players, {Scaled(r, 8)} with 8, {Scaled(r, 16)} with 16, {Scaled(r, 32)} with 32.";
    }
}

/// <summary>
/// The match's score limit on the server: set from everyone present (people + bots) until play starts, then only
/// ever raised when someone joins mid-match (never lowered, so a leave can't suddenly hand anyone the win).
/// Replicated in the mode state (protocol 21).
/// </summary>
public sealed partial class Game
{
    /// <summary>Host setting: Auto (scales with players) or Fixed (the classic limits).</summary>
    public ScoreLimitMode ScoreLimitMode = ScoreLimitMode.Auto;

    /// <summary>
    /// The score that wins this match in the current mode: kills (free-for-all, Team Deathmatch), hill points or
    /// captures. 0 in Gun Game and Zombies. Clients get it from the mode state.
    /// </summary>
    public int ScoreLimit = WinKills;

    /// <summary>Self-test: above 0, the limit is this whatever the player count (e.g. int.MaxValue: matches never end at a score).</summary>
    public int ScoreLimitOverride;

    /// <summary>The limit for this many players under the host's setting and the current mode.</summary>
    public int ScoreLimitFor(int players) =>
        ScoreLimitOverride > 0 ? ScoreLimitOverride : ScoreLimits.For(Mode, players, ScoreLimitMode == ScoreLimitMode.Auto);

    /// <summary>Server, every tick: follow the player count before play; while playing, only raise it (joins).</summary>
    private void UpdateScoreLimit()
    {
        if (Phase == MatchPhase.Ended)
            return;
        var want = ScoreLimitFor(Players.Count);
        if (Phase != MatchPhase.Playing)
        {
            ScoreLimit = want;
            return;
        }
        if (want <= ScoreLimit)
            return;
        ScoreLimit = want;
        if (ScoreLimitOverride == 0)
            ModeEvent($"{Players.Count} players now: the score limit goes up to {want} {ScoreLimits.Unit(Mode)}", Sfx.HitTick);
    }

    /// <summary>Self-test: the server's limit update for one tick (as during a match).</summary>
    public void SelfTestUpdateScoreLimit() => UpdateScoreLimit();
}
