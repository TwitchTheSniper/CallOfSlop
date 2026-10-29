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
        CheckTestRangeHostBots();
        CheckTestRangeBackToLobby();
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
        if (GameModeRegistry.All.Any(x => x.Endless != (x.Id is GameMode.TestRange or GameMode.Sandbox)))
            Fail("only Test Range and Sandbox (Dev) should be endless");
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
        var info = new ServerInfo("Range", "Courtyard", 2, 8, 1, MatchPhase.Playing, 26124, ServerInfo.ProtocolVersion, GameMode.TestRange);
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
            var me0 = game.Local!;
            // 15+ kills each for the player and a bot, past the classic 11 and this lobby's free-for-all limit.
            var many = (ushort)Math.Max(15, ffaLimit + 5);
            star.Kills = many;
            me0.Kills = (ushort)(many + 2);
            Run(game, ref now, 1);
            var afterScore = game.Phase;
            now += Math.Max(game.MatchLength, 20 * 60) + 30; // 20 minutes pass (a clock jump, not a long run): past any time limit
            Run(game, ref now, 1);
            var afterClock = game.Phase;
            game.DebugEndMatch(now); // even a direct end request (the smoke test's) does nothing
            Run(game, ref now, Game.IntermissionSeconds + 2); // past where the results screen would have handed over
            var ended = afterScore != MatchPhase.Playing || afterClock != MatchPhase.Playing || game.Phase != MatchPhase.Playing;
            Line($"{GameModes.Name(mode),-12} started {startedPlaying}, {star.Name} on {star.Kills} kills, you on {me0.Kills} (FFA limit {ffaLimit}): after the score {afterScore}, after the clock {afterClock}, "
                 + $"now {game.Phase}; match {match}->{game.MatchNumber}, map {map.Name}->{World.Map.Name}, vote open {game.VoteOpen}, limit {game.ScoreLimit}, time left {game.TimeLeft:0}");
            if (!startedPlaying)
                Fail($"{GameModes.Name(mode)}: the scripted session never started playing ({game.Phase})");
            else if (mode == GameMode.TestRange && (me0.Kills < 15 || star.Kills < 15))
                Fail("the script should put the player and a bot past 15 kills");
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
            // All three bots can be dead at once (they kill each other): wait for one, at most one respawn delay.
            Player? victim = null;
            for (var t = 0.0; t < World.RespawnDelay + 1 && (victim = game.Players.Values.FirstOrDefault(p => p.Bot != null && p.Alive)) == null; t += 1.0 / World.TickRate)
                Run(game, ref now, 1.0 / World.TickRate);
            if (victim == null)
            {
                Fail("Test Range: no bot alive to shoot");
                continue;
            }
            // The other bots are still fighting (live, unseeded), so nothing may save the victim from this shot: no Last
            // Stand to come and none already running (a bot saved a moment ago sits on 1 HP behind a shield that eats
            // the shot), and the kill and the comeback are checked as they happen, since another bot can kill the
            // victim again any time after it is back (Proto Box is small and has no spawn protection).
            victim.ImmuneUntil = 0;
            victim.LastStandUsed = true;
            victim.ShieldCharges = 0;
            int kills = me.Kills, deaths = victim.Deaths;
            var killed = game.SelfTestDamageWith(me, victim, 100000, Weapons.DefaultPrimary.Id, now);
            int killsAfter = me.Kills, deathsAfter = victim.Deaths;
            var killedAt = now;
            var backAfter = -1.0;
            const double dt = 1.0 / World.TickRate;
            for (var t = 0.0; t < World.RespawnDelay + 1 && backAfter < 0; t += dt)
            {
                now += dt;
                game.Update(now, dt);
                if (victim.Alive)
                    backAfter = now - killedAt;
            }
            var cells = game.Module.ScoreboardCells(game, me);
            Line($"Test Range kill: killed {killed}, kills {kills}->{killsAfter}, victim deaths {deaths}->{deathsAfter}, victim back after {backAfter:0.00} s, my cells {cells.First}/{cells.Second}");
            if (!killed || killsAfter != kills + 1 || deathsAfter != deaths + 1)
                Fail("Test Range: the kill wasn't counted");
            if (backAfter < 0)
                Fail("Test Range: a killed bot didn't respawn");
            else if (backAfter < World.RespawnDelay - 0.1)
                Fail($"Test Range: a killed bot came back after {backAfter:0.00} s, before the {World.RespawnDelay} s respawn delay");
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

    /// <summary>
    /// Mid-session, while playing: the host adds bots (they spawn straight into the match), removes some, and changes the
    /// difficulty (every bot replaced), all without a phase or map change (HostControls.cs). Offline, as single player.
    /// </summary>
    private static void CheckTestRangeHostBots()
    {
        using var game = ModeGame(2, GameMode.TestRange);
        var now = 0.0;
        Run(game, ref now, Game.CountdownSeconds + 0.3);
        var match = game.MatchNumber;
        var map = World.Map;
        var skill0 = game.BotSkill;
        var max = Math.Min(Game.MaxPlayers, game.MapMaxPlayers) - game.HumanCount;
        var want = Math.Min(5, max);

        // The bots keep fighting between the checks, so a bot can be killed the moment it spawns: "spawned into the match"
        // is read straight after the call (AddBot spawns before any tick runs), and only the roster after the ticks.
        var before = game.Players.Values.Where(p => p.Bot != null).ToHashSet(); // (the two first bots may be dead right now)
        var changed = game.SetBots(want, skill0);
        var newcomers = game.Players.Values.Where(p => p.Bot != null && !before.Contains(p)).ToList();
        var spawnedAdd = before.Count + newcomers.Count(p => p.Alive); // every newcomer in the fight at once: want

        Run(game, ref now, 0.5);
        var afterAdd = (game.BotCount, Alive: spawnedAdd, Same: game.Players.Values.All(p => p.Bot == null || p.Bot.Skill == skill0));
        var fewer = game.SetBots(1, skill0);
        Run(game, ref now, 0.2);
        var afterRemove = game.BotCount;
        var newSkill = skill0 == BotDifficulty.Hard ? BotDifficulty.Easy : BotDifficulty.Hard;
        var old = game.Players.Values.Where(p => p.Bot != null).ToHashSet(); // (ids are reused: compare the players themselves)
        var replaced = game.SetBots(2, newSkill);
        var spawnedSkill = game.Players.Values.Count(p => p.Bot != null && p.Alive); // (all new bots: none has had a tick yet)
        Run(game, ref now, 0.5);
        var afterSkill = (game.BotCount, AllNew: game.Players.Values.Where(p => p.Bot != null).All(p => p.Bot!.Skill == newSkill), Kept: game.Players.Values.Count(p => p.Bot != null && old.Contains(p)),
            Alive: spawnedSkill);
        var tooMany = game.SetBots(99, newSkill);
        var capped = game.BotCount;
        Line($"host bots: 2 -> {want} ({afterAdd.BotCount}, {afterAdd.Alive} spawned, all {skill0} {afterAdd.Same}) -> 1 ({afterRemove}) -> 2 {newSkill} ({afterSkill.BotCount}, all new {afterSkill.AllNew}, {afterSkill.Kept} kept, "
             + $"{afterSkill.Alive} spawned) -> 99 capped to {capped} (max {max}); changed {changed}/{fewer}/{replaced}/{tooMany}; skill now {game.BotSkill}, wanted {game.WantedBots}; {game.Phase}, match {match}->{game.MatchNumber}");
        if (!changed || afterAdd.BotCount != want || afterAdd.Alive != want || !afterAdd.Same)
            Fail($"adding bots mid-session gave {afterAdd.BotCount} ({afterAdd.Alive} spawned at once), wanted {want} spawned into the match");
        if (!fewer || afterRemove != 1)
            Fail("removing bots mid-session didn't leave one");
        if (!replaced || afterSkill.BotCount != 2 || !afterSkill.AllNew || afterSkill.Kept != 0 || afterSkill.Alive != 2 || game.BotSkill != newSkill)
            Fail("a new difficulty mid-session didn't replace every bot");
        if (capped != max || game.WantedBots != max)
            Fail($"the bot count isn't capped by the map: {capped} of {max}");
        if (game.Phase != MatchPhase.Playing || game.MatchNumber != match || World.Map != map)
            Fail("changing bots mid-session ended the match or changed the map");
        World.SetMap(Maps.Get(0));
    }

    /// <summary>
    /// Hosting with a client: the host goes BACK TO LOBBY mid-match. Everyone lands in the lobby room, still connected
    /// (the client sees it through the usual match-start packet, no new message), the host can then change the map
    /// and start again. Single player (no lobby room) refuses it.
    /// </summary>
    private static void CheckTestRangeBackToLobby()
    {
        using (var solo = ModeGame(1, GameMode.TestRange))
        {
            var now0 = 0.0;
            Run(solo, ref now0, Game.CountdownSeconds + 0.3);
            if (solo.CanReturnToLobby || solo.ReturnToLobbyNow(now0) || solo.Phase != MatchPhase.Playing)
                Fail("single player (no lobby room) should refuse BACK TO LOBBY");
        }
        var map = FindMap("Proto Box", m => m.MaxPlayers >= 8)!;
        const ushort port = 26193;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, Game.MaxPlayers - 1), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2, mapId: map.Id)
        {
            MapVoteEnabled = true,
            LobbyEnabled = true,
        };
        server.Mode = GameMode.TestRange;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        var joined = a.Phase == MatchPhase.Lobby && server.Players.Count == 4;
        server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.7, ref now);
        var playing = server.Phase == MatchPhase.Playing && a.Phase == MatchPhase.Playing && a.Mode == GameMode.TestRange;
        var match = server.MatchNumber;
        server.Players.Values.First(p => p.Bot != null).Kills = 20;

        var back = server.ReturnToLobbyNow(now);
        Pump(server, [a], 1.0, ref now, clock);
        var room = (Server: server.Phase, Client: a.Phase);
        var inMap = server.Players.Values.Count(p => p.Alive);
        var other = FindMap("Courtyard", m => m != map && server.LobbyMapAllowed(m));
        var changed = other != null && server.LobbySetMap(other, false);
        Pump(server, [a], 0.6, ref now, clock);
        var again = server.LobbyStart(true, now);
        PumpFast(server, [a], Game.LobbyCountdownSeconds + Game.CountdownSeconds + 0.7, ref now);
        Line($"back to lobby: joined {joined}, playing {playing}, back {back} -> server {room.Server}, client {room.Client} (connected {!a.Disconnected}), {server.Players.Count} players ({a.Players.Count} on the client), "
             + $"{inMap} in the map, vote {server.VoteOpen}; map {other?.Name} applied {changed}, client sees {a.LobbyMapName}; start again {again} -> {server.Phase}/{a.Phase} on {World.Map.Name}, match {match}->{server.MatchNumber}");
        if (!joined || !playing)
            Fail("the hosted Test Range session didn't get going with a client");
        else if (!back || room != (MatchPhase.Lobby, MatchPhase.Lobby) || inMap != 0 || server.VoteOpen)
            Fail("BACK TO LOBBY didn't put everyone in the lobby room with nobody in the map");
        else if (a.Disconnected || a.Players.Count != 4 || server.Players.Count != 4)
            Fail("BACK TO LOBBY lost a player");
        else if (!changed || a.LobbyMapName != other!.Name)
            Fail("the host couldn't change the map from the lobby room after BACK TO LOBBY");
        else if (!again || server.Phase != MatchPhase.Playing || a.Phase != MatchPhase.Playing || World.Map != other || server.MatchNumber != match + 1)
            Fail("starting again from the lobby room didn't play the new map for everyone");
        World.SetMap(Maps.Get(0));
    }

    /// <summary>
    /// Pumps a loopback host and its clients through <paramref name="seconds"/> of game time in a fraction of that real
    /// time: the clock jumps 50 ms an update (the packets still travel for real, a couple of ms each). For countdowns.
    /// </summary>
    private static void PumpFast(Game server, IEnumerable<Game> clients, double seconds, ref double now)
    {
        const double dt = 0.05;
        for (var t = 0.0; t < seconds; t += dt)
        {
            now += dt;
            server.Update(now, dt);
            foreach (var c in clients)
                c.Update(now, dt);
            Thread.Sleep(2);
        }
    }
}
