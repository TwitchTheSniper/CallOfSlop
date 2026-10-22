namespace CallOfSlop;

/// <summary>Team Deathmatch: two teams, every enemy kill scores one for the team, first team to 40 (scaled with the player count).</summary>
public sealed class TeamDeathmatchModule : GameModeModule
{
    /// <summary>Team kills to win with up to 8 players (4 v 4, so about 10 kills each).</summary>
    public const int ScoreLimit = 40;

    public static readonly ScoreLimits.Rule Rule = new(ScoreLimit, 0.80, 5, 20);

    public override GameMode Id => GameMode.TeamDeathmatch;
    public override string Name => "Team Deathmatch";
    public override string Short => "TDM";
    public override string Rules(int limit = 0) => $"Two teams. First team to {(limit > 0 ? limit : ScoreLimit)} kills. No friendly fire.";
    public override bool TeamBased => true;
    public override ScoreLimits.Rule? ScoreRule => Rule;
    public override MapNeeds Needs => MapNeeds.TeamSpawns;

    public override void OnKill(Game game, Player attacker, Player victim, byte killId, double now)
    {
        if (attacker != victim && attacker.Team < 2)
            game.TeamScore[attacker.Team]++;
    }

    public override void ServerTickPlaying(Game game, double now) => TeamWinCheck(game, now);

    public override string? SelfTestVerify(Game game)
    {
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        return game.Phase == MatchPhase.Playing && game.TeamScore[0] + game.TeamScore[1] != kills
            ? $"team score {game.TeamScore[0] + game.TeamScore[1]} doesn't match {kills} kills (a teamkill counted?)"
            : null;
    }
}
