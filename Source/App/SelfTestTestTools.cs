namespace CallOfSlop;

/// <summary>Self-test section for the testing tools: 0-bot (solo) matches and the pause menu's "Kill yourself".</summary>
public static partial class SelfTest
{
    private static void CheckTestTools()
    {
        var now = 0.0;

        // Settings: 0 bots is kept (old files with 1..7 still load as before).
        var file = Path.Combine(Path.GetTempPath(), $"cos_selftest_settings_{Environment.ProcessId}.txt");
        try
        {
            File.WriteAllText(file, "bots=0\nhost_bots=0\n");
            var zero = Settings.Load(file);
            File.WriteAllText(file, "bots=5\n");
            var five = Settings.Load(file);
            File.WriteAllText(file, "bots=-3\n");
            var negative = Settings.Load(file);
            Line($"settings: bots=0 -> {zero.Bots}, bots=5 -> {five.Bots}, bots=-3 -> {negative.Bots}");
            if (zero.Bots != 0 || zero.HostBots != 0 || five.Bots != 5 || negative.Bots != 0)
                Fail("bot count 0 doesn't survive settings.txt (or old values changed)");
        }
        finally
        {
            File.Delete(file);
        }

        // A 0-bot match: Deploy goes straight in, and a simulated minute later it's still running with the clock stopped.
        using (var game = ModeGame(0, GameMode.FreeForAll))
        {
            game.WarmupEnabled = true;
            Run(game, ref now, 1);
            var warm = game.Phase;
            game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var playing = game.Phase == MatchPhase.Playing && game.Local is { Alive: true };
            Run(game, ref now, 60);
            Line($"0 bots: {game.Players.Count} player, {warm} -> Deploy -> playing {playing}; after 60 s: {game.Phase}, {game.TimeLeft:0} s left, solo {game.SoloMatch}");
            if (warm != MatchPhase.Warmup || !playing)
                Fail("a 0-bot match didn't go from warmup to play on Deploy");
            if (game.Phase != MatchPhase.Playing || game.TimeLeft < Game.MatchSeconds - 1 || !game.SoloMatch)
                Fail("a 0-bot match ended or its clock ran while alone");

            // Kill yourself (host / single player path): a suicide, no kill credit, streak reset, then respawn.
            var me = game.Local!;
            me.Streak.Count = 2;
            var (kills, deaths) = (me.Kills, me.Deaths);
            var canBefore = game.CanRequestSuicide;
            game.RequestSuicide(now);
            var info = game.LastDeath;
            Line($"kill yourself: alive {me.Alive}, kills {kills}->{me.Kills}, deaths {deaths}->{me.Deaths}, streak {me.Streak.Count}, card {info?.Kind} '{info?.WeaponName}', can after {game.CanRequestSuicide}");
            if (!canBefore || me.Alive || me.Deaths != deaths + 1 || me.Kills != kills || me.Streak.Count != 0)
                Fail("Kill yourself didn't count as a suicide (death +1, no kill, streak reset)");
            if (info is not { Kind: KillKind.Self } || info.WeaponName != "Suicide" || Throwables.KillName(Game.SuicideKillId) != "Suicide")
                Fail($"the suicide didn't reach the death card as a self kill named 'Suicide' ({info?.Kind} '{info?.WeaponName}')");
            if (game.CanRequestSuicide)
                Fail("Kill yourself is still offered while dead");
            game.RequestRespawn();
            Run(game, ref now, World.RespawnDelay + 0.5);
            if (!me.Alive || game.Phase != MatchPhase.Playing)
                Fail("no respawn after Kill yourself (or the solo match ended)");

            // Someone joins: the clock runs again.
            var t0 = game.TimeLeft;
            var guest = game.SelfTestAddHuman();
            game.SelfTestSpawn(guest);
            Run(game, ref now, 3);
            Line($"a person joins: solo {game.SoloMatch}, clock {t0:0.0} -> {game.TimeLeft:0.0}");
            if (game.SoloMatch || game.TimeLeft > t0 - 2.5)
                Fail("the match clock didn't restart once someone joined");

            // The server handler for a client's request: rate limited to one per second, never in the warmup.
            var gd = guest.Deaths;
            var first = game.SelfTestSuicideRequest(guest, now);
            game.SelfTestSpawn(guest);
            var tooSoon = game.SelfTestSuicideRequest(guest, now + 0.5);
            var later = game.SelfTestSuicideRequest(guest, now + 1.1);
            Line($"client request: first {first}, 0.5 s later {tooSoon}, 1.1 s later {later}; guest deaths {gd}->{guest.Deaths}, host kills {me.Kills}");
            if (!first || tooSoon || !later || guest.Deaths != gd + 2 || me.Kills != kills)
                Fail("the server's suicide request handling (or its rate limit) is wrong");
        }

        // Every mode alone: nothing ends it, objectives still score but stop one short of the win.
        foreach (var mode in GameModes.All.Where(m => m != GameMode.FreeForAll))
        {
            using var game = ModeGame(0, mode);
            Run(game, ref now, Game.CountdownSeconds + 0.3); // no warmup: straight to the countdown
            game.RequestRespawn();
            Run(game, ref now, 15);
            var team = game.Local?.Team ?? 255;
            var limit = game.IsTeamMode ? game.ScoreLimit : 0; // the Auto limit for one player (ScoreLimits.cs)
            if (limit > 0)
            {
                game.TeamScore[0] = limit + 3;
                game.SelfTestModeTick(now);
            }
            var scores = (game.TeamScore[0], game.TeamScore[1]);
            var me = game.Local!;
            var level = me.GunLevel;
            var died = me.Alive && game.SelfTestSuicideRequest(me, now);
            Line($"{GameModes.Short(mode),-4} alone: {game.Phase}, team {team}, scores {scores.Item1}-{scores.Item2} (limit {limit}), suicide {died} -> scores {game.TeamScore[0]}-{game.TeamScore[1]}, gun level {level}->{me.GunLevel}");
            if (game.Phase != MatchPhase.Playing)
                Fail($"{GameModes.Name(mode)} alone ended on its own");
            if (limit > 0 && scores.Item1 != limit - 1)
                Fail($"{GameModes.Name(mode)} alone: the score wasn't held one short of the limit ({scores.Item1})");
            if (game.IsTeamMode && team > 1)
                Fail($"{GameModes.Name(mode)} alone: not on a team");
            if (!died || game.TeamScore[0] != scores.Item1 || game.TeamScore[1] != scores.Item2 || me.GunLevel != level)
                Fail($"{GameModes.Name(mode)}: a suicide changed the score or the gun level (or didn't happen)");
        }
    }
}
