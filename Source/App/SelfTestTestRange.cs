namespace CallOfSlop;

/// <summary>
/// Self-test section for the Test Range mode (Source/Game/Modes/TestRange): it registers and shows up on every normal map,
/// and a short scripted session that blows past the free-for-all score limit and the time limit never ends the match,
/// opens a map vote, goes back to the lobby or changes the map. Simulated time jumps, no long bot match.
/// </summary>
public static partial class SelfTest
{
    private static void CheckTestRange()
    {
        CheckTestRangeRegistry();
        CheckTestRangeNeverEnds();
        CheckTestRangeAlone();
    }

    private static void CheckTestRangeRegistry()
    {
        var m = GameModeRegistry.Get(GameMode.TestRange);
        Line($"{m.Name} ({m.Short}, id {(byte)m.Id}): endless {m.Endless}, clock {m.HasTimeLimit}, vote {m.UsesMapVote}, limit {ScoreLimits.For(GameMode.TestRange, 8)}, dev {m.DevOnly}");
        if (m is not TestRangeModule || !GameModeRegistry.IsKnown(GameMode.TestRange) || (byte)GameMode.TestRange != 8)
            Fail("Test Range isn't registered as mode 8");
        if (!m.Endless || m.HasTimeLimit || m.UsesMapVote || m.DevOnly || m.TeamBased || m.CoOp || !m.UsesLoadout || !m.AllowsStreaks)
            Fail("Test Range: wrong capability flags");
        if (ScoreLimits.HasLimit(GameMode.TestRange) || ScoreLimits.For(GameMode.TestRange, 8) != 0 || ScoreLimits.For(GameMode.TestRange, 32, auto: false) != 0)
            Fail("Test Range has a score limit");
        if (GameModeRegistry.All.Any(x => x.Endless != (x.Id == GameMode.TestRange)))
            Fail("only Test Range should be endless");
        if (!GameModeRegistry.Shipped.Contains(m) || !GameModes.Every.Contains(GameMode.TestRange) || !GameModes.ForMap(null).Contains(GameMode.TestRange))
            Fail("Test Range isn't offered in the menus (shipped, random map)");
        var missing = Maps.All.Where(map => map.Pack is not { InTrueRandom: false } && !GameModes.ForMap(map).Contains(GameMode.TestRange)).Select(map => map.Name).ToList();
        if (missing.Count > 0)
            Fail($"Test Range isn't offered on: {string.Join(", ", missing)}");
        // No "first to N" anywhere the player reads about it.
        using (var g = ModeGame(1, GameMode.TestRange))
        {
            var texts = new[] { m.Rules(), m.Rules(11), m.Description, g.ModeGoalText, m.ScoreBar(g)?.Big ?? "", m.ScoreBar(g)?.Sub ?? "" };
            if (texts.Any(t => t.Contains("first to", StringComparison.OrdinalIgnoreCase) || t.Contains("minutes max", StringComparison.OrdinalIgnoreCase)))
                Fail($"Test Range text mentions a limit: {string.Join(" | ", texts)}");
            if (m.ScoreBar(g)?.Big != "TEST RANGE")
                Fail($"Test Range HUD says '{m.ScoreBar(g)?.Big}', want TEST RANGE");
            if (m.ScoreboardColumns != ("Kills", "Deaths"))
                Fail("Test Range scoreboard should count kills and deaths");
        }
        var info = new ServerInfo("Range", "Courtyard", 2, 8, 1, MatchPhase.Playing, 60124, ServerInfo.ProtocolVersion, GameMode.TestRange);
        if (ServerInfo.Find(info.Encode()) != info)
            Fail("Test Range didn't survive the server info");
    }

    /// <summary>
    /// 3 bots, map vote, the lobby room and return-to-lobby all on, a 1-minute time limit: once the host starts from the
    /// lobby and play begins, one bot goes far past the
    /// free-for-all limit and the clock runs out (a time jump), and the match carries on. Free-for-all ends in the same
    /// script (the control), so the check means something.
    /// </summary>
    private static void CheckTestRangeNeverEnds()
    {
        foreach (var mode in new[] { GameMode.FreeForAll, GameMode.TestRange })
        {
            World.SetMap(Maps.Get(0));
            using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 3, mapId: 0)
            {
                MapVoteEnabled = true,
                LobbyEnabled = true, // hosting: the session opens in the lobby room
                ReturnToLobby = true, // and would go back to it after every match
                MatchMinutes = 1,
            };
            game.Mode = mode;
            var now = 0.0;
            Run(game, ref now, 0.2);
            var lobbyFirst = game.InLobby;
            game.LobbyStart(true, now); // the host's FORCE START
            Run(game, ref now, Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.5);
            if (!lobbyFirst)
                Fail($"{GameModes.Name(mode)}: a hosted session should open in the lobby room");
            var map = World.Map;
            var match = game.MatchNumber;
            var startedPlaying = game.Phase == MatchPhase.Playing;
            var ffaLimit = ScoreLimits.For(GameMode.FreeForAll, game.Players.Count);
            var star = game.Players.Values.First(p => p.Bot != null);
            star.Kills = (ushort)(ffaLimit + 5);
            Run(game, ref now, 1);
            var afterScore = game.Phase;
            now += game.MatchLength + 30; // the clock runs out
            Run(game, ref now, 1);
            var afterClock = game.Phase;
            game.DebugEndMatch(now); // even a direct end request (the smoke test's) does nothing
            Run(game, ref now, Game.IntermissionSeconds + 2); // past where the results screen would have handed over
            var ended = afterScore != MatchPhase.Playing || afterClock != MatchPhase.Playing || game.Phase != MatchPhase.Playing;
            Line($"{GameModes.Name(mode),-12} started {startedPlaying}, {star.Name} on {star.Kills} kills (FFA limit {ffaLimit}): after the score {afterScore}, after the clock {afterClock}, "
                 + $"now {game.Phase}; match {match}->{game.MatchNumber}, map {map.Name}->{World.Map.Name}, vote open {game.VoteOpen}, limit {game.ScoreLimit}, time left {game.TimeLeft:0}");
            if (!startedPlaying)
                Fail($"{GameModes.Name(mode)}: the scripted session never started playing ({game.Phase})");
            else if (mode == GameMode.FreeForAll && !ended)
                Fail("control: free-for-all past its score limit and clock should have ended");
            else if (mode == GameMode.TestRange)
            {
                if (ended || game.Winner != Game.NoWinner || game.MatchResult.Length > 0)
                    Fail($"Test Range ended a match ({afterScore}/{afterClock}/{game.Phase}: {game.MatchResult})");
                if (game.MatchNumber != match || World.Map != map || game.VoteOpen || game.InLobby)
                    Fail("Test Range started a new match, opened a vote, went back to the lobby or changed the map");
                if (game.ScoreLimit != 0)
                    Fail($"Test Range has a score limit of {game.ScoreLimit} in a match");
            }

            if (mode != GameMode.TestRange)
                continue;
            // Bots respawn and the scoreboard keeps counting.
            var me = game.Local!;
            if (!me.Alive)
            {
                game.RequestRespawn();
                Run(game, ref now, World.RespawnDelay + 0.5);
            }
            if (game.Players.Values.FirstOrDefault(p => p.Bot != null && p.Alive) is not { } victim)
            {
                Fail("Test Range: no bot alive to shoot");
                continue;
            }
            victim.ImmuneUntil = 0;
            victim.LastStandUsed = true;
            int kills = me.Kills, deaths = victim.Deaths;
            var killed = game.SelfTestDamageWith(me, victim, 100000, Weapons.DefaultPrimary.Id, now);
            Run(game, ref now, World.RespawnDelay + 1);
            var cells = game.Module.ScoreboardCells(game, me);
            Line($"Test Range kill: killed {killed}, kills {kills}->{me.Kills}, victim deaths {deaths}->{victim.Deaths}, victim back {victim.Alive}, my cells {cells.First}/{cells.Second}");
            if (!killed || me.Kills != kills + 1 || victim.Deaths != deaths + 1)
                Fail("Test Range: the kill wasn't counted");
            if (!victim.Alive)
                Fail("Test Range: a killed bot didn't respawn");
            if (game.Phase != MatchPhase.Playing)
                Fail("Test Range: the match ended after a kill");
        }
        World.SetMap(Maps.Get(0));
    }

    /// <summary>Everyone else gone (alone, no bots): still nothing ends it, clock or not.</summary>
    private static void CheckTestRangeAlone()
    {
        using var game = ModeGame(0, GameMode.TestRange);
        game.MatchMinutes = 1;
        var now = 0.0;
        Run(game, ref now, Game.CountdownSeconds + 0.3);
        now += game.MatchLength + 30;
        Run(game, ref now, 2);
        Line($"Test Range alone: {game.Phase}, match {game.MatchNumber}");
        if (game.Phase != MatchPhase.Playing)
            Fail("Test Range alone ended on its own");
    }
}
