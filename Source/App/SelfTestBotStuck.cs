using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Bot stuck watch" (Source/Game/BotStuck.cs): short scenarios on a small lab map. A bot boxed into a pit
/// with no exit kills itself after the timeout (through the "Kill yourself" path) and respawns outside; a bot steering
/// into a wall recovers before the timeout; a bot holding the hill or a perch never kills itself; warmup, countdown and
/// the results screen never trigger it; the log line and the counters; Zombies co-op teleport; no allocations.
/// (The every-map false-positive check is in the "Bots" section's far walks and the "Matches" line.)
/// </summary>
public static partial class SelfTest
{
    private const string StuckLabJson = """
        { "name": "Stuck Lab", "halfX": 20, "halfZ": 20, "ops": [
          { "op": "Ground", "color": [0.4, 0.4, 0.4] },
          { "op": "Block", "x0": -2, "y0": 0, "z0": 1.5, "x1": 2, "y1": 4, "z1": 2, "color": "#808080" },
          { "op": "Block", "x0": -2, "y0": 0, "z0": -2, "x1": 2, "y1": 4, "z1": -1.5, "color": "#808080" },
          { "op": "Block", "x0": -2, "y0": 0, "z0": -1.5, "x1": -1.5, "y1": 4, "z1": 1.5, "color": "#808080" },
          { "op": "Block", "x0": 1.5, "y0": 0, "z0": -1.5, "x1": 2, "y1": 4, "z1": 1.5, "color": "#808080" },
          { "op": "Block", "x0": 8, "y0": 0, "z0": -5, "x1": 8.5, "y1": 4, "z1": 5, "color": "#808080" },
          { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
        """;

    /// <summary>Stuck self-kills seen in the every-map matches (real map trouble spots), for this section's annotation.</summary>
    private static readonly List<string> StuckNotes = new();

    private static void CheckBotStuck()
    {
        try
        {
            CheckBotStuckScenarios();
        }
        finally
        {
            if (StuckNotes.Count == 0)
                StuckNotes.Add("no stuck self-kills in the every-map matches");
            Line("::notice title=Bot stuck watch (results)::" + string.Join("%0A", StuckNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void CheckBotStuckScenarios()
    {
        var lab = MapFile.Load(StuckLabJson, "stuck-lab.json");
        lab.Validate();
        var now = 0.0;
        var pitCenter = new Vector3(0, 0, 0);
        var outside = new Vector3(14, 0, 3);

        // ---- A pit with no exit: warmup and countdown never trigger it, then the bot kills itself and respawns ----
        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            game.WarmupEnabled = true;
            game.HotSwapMap(lab);
            NavGraph.Pump(double.MaxValue);
            var host = game.Local!;
            var bot = game.Players.Values.First(p => p.Bot != null);
            var b = bot.Bot!;
            game.BotStuckSuicideSeconds = 1f; // as short as it goes: anything before play would trip it
            void Pit()
            {
                game.SelfTestPlace(bot, pitCenter, 0);
                b.ForcedGoal = outside;
                host.Alive = false; // out of sight, out of the fight (nothing to engage)
                host.Health = 0;
                host.WantsRespawn = false;
            }
            Run(game, ref now, 0.5);
            var warm = game.Phase;
            Pit();
            Run(game, ref now, 4);
            var warmKills = game.BotStuckKills;
            var warmSecs = b.StuckSecs;
            game.ChooseLoadout(Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, ThrowableKind.None, ThrowableKind.None, 0, 0, now);
            Run(game, ref now, 0.3);
            var countdown = game.Phase;
            Pit();
            Run(game, ref now, Game.CountdownSeconds - 0.6);
            var countKills = game.BotStuckKills;
            Run(game, ref now, 0.5); // (play starts during this: a moment with the 1 s timeout, under it)
            Line($"pit in {warm}: {warmKills} stuck kills (stuck clock {warmSecs:0.0} s); in {countdown}: {countKills}; now {game.Phase}");
            StuckNotes.Add($"pit in {warm}: {warmKills} stuck kills (stuck clock {warmSecs:0.0} s); in {countdown}: {countKills}; now {game.Phase}");
            if (warm != MatchPhase.Warmup || countdown != MatchPhase.Countdown || game.Phase != MatchPhase.Playing)
                Fail($"the stuck lab didn't go warmup -> countdown -> play ({warm}, {countdown}, {game.Phase})");
            if (warmKills != 0 || countKills != 0 || warmSecs != 0)
                Fail("the stuck watch ran during the warmup or the countdown");

            // Play: the default-scale stages at 20/40/60% of the timeout, the self-kill at 100%.
            game.BotStuckSuicideSeconds = 5f;
            Pit();
            game.SelfTestStuckWatch(bot, Vector3.Zero, true, false, now); // the clock starts now
            var (kills, deaths) = (bot.Kills, bot.Deaths);
            var start = now;
            var diedAt = -1.0;
            var maxStage = 0;
            var why = new int[8]; // the watch's verdict per tick (StuckWhy), [7]: ticks without a watch step
            while (now - start < 9 && diedAt < 0)
            {
                Run(game, ref now, 1.0 / World.TickRate);
                maxStage = Math.Max(maxStage, b.StuckStage);
                if (!bot.Alive)
                    diedAt = now - start;
                else if (b.StuckWatchTick != game.ServerTick)
                    why[7]++;
                else
                    why[(int)b.StuckLastWhy]++;
            }
            var whyText = $"watch per tick: counting {why[0]}, not playing {why[1]}, idle {why[2]}, mover {why[3]}, holding {why[4]}, progress {why[5]}, paused {why[6]}, no watch {why[7]}; "
                          + $"{bot.Name}, mode {b.Mode}, at ({bot.Position.X:0.0}, {bot.Position.Y:0.0}, {bot.Position.Z:0.0}), path {b.PathLen}, nav {NavGraph.Current.Nodes.Length} nodes, "
                          + $"hold {b.HoldUntil - now:0.0} s, objective {b.Objective?.ToString() ?? "none"}, phase {game.Phase}";
            Line(whyText);
            var log = game.LastStuckLog;
            Line($"pit in play: self-kill after {(diedAt < 0 ? "NEVER" : $"{diedAt:0.00} s")} (timeout {game.BotStuckSuicideSeconds:0} s), recovery stages reached {maxStage} (mode {b.Mode}), "
                 + $"deaths {deaths}->{bot.Deaths}, kills {kills}->{bot.Kills}, counters {game.BotStuckKills} (this map {game.StuckKillsOn(lab.Name)}, this match {game.BotStuckKillsThisMatch})");
            Line($"log: {log}");
            StuckNotes.Add($"pit: self-kill after {diedAt:0.00} s (timeout 5 s), stages {maxStage}; {log}");
            if (diedAt < 0)
                Fail($"a bot boxed into a pit with no exit didn't kill itself within 9 s (timeout 5 s); {whyText}");
            else if (diedAt < game.BotStuckSuicideSeconds - 0.2)
                Fail($"the stuck bot killed itself too early ({diedAt:0.00} s, timeout {game.BotStuckSuicideSeconds:0} s)");
            if (maxStage < 3)
                Fail($"the stuck bot didn't go through the recovery stages first (reached {maxStage})");
            if (bot.Deaths != deaths + 1 || bot.Kills != kills || host.Kills != 0 || game.BotStuckKills != 1 || game.StuckKillsOn(lab.Name) != 1)
                Fail("the stuck self-kill didn't count as one suicide (death +1, no kill credit) or the counters are off");
            if (!log.StartsWith("[bots] stuck: ", StringComparison.Ordinal) || !log.Contains(lab.Name) || !log.Contains("self-kill") || !log.Contains("("))
                Fail($"no proper [bots] stuck: log line (map, position, cause): '{log}'");
            b.ForcedGoal = null;
            Run(game, ref now, World.RespawnDelay + 0.5);
            var flat = MathF.Max(MathF.Abs(bot.Position.X), MathF.Abs(bot.Position.Z));
            Line($"respawn: alive {bot.Alive} at ({bot.Position.X:0.0}, {bot.Position.Z:0.0})");
            StuckNotes.Add($"respawn: alive {bot.Alive} at ({bot.Position.X:0.0}, {bot.Position.Z:0.0})");
            if (!bot.Alive || flat < 2.5f)
                Fail("the stuck bot didn't respawn (outside the pit) after its self-kill");

            // The second self-kill within a minute is counted but not logged again.
            // (The clock starts explicitly, as above, and the run waits up to 9 s for the self-kill: a fixed 6 s from the
            // placement left too little room on a slow runner.)
            Pit();
            game.SelfTestStuckWatch(bot, Vector3.Zero, true, false, now);
            var firstLog = game.LastStuckLog;
            var start2 = now;
            while (now - start2 < 9 && game.BotStuckKills < 2)
                Run(game, ref now, 1.0 / World.TickRate);
            if (game.BotStuckKills != 2 || !ReferenceEquals(firstLog, game.LastStuckLog))
                Fail($"a second stuck self-kill within a minute: counted {game.BotStuckKills} (want 2), logged again {!ReferenceEquals(firstLog, game.LastStuckLog)}");

            // The results screen: never.
            Run(game, ref now, World.RespawnDelay + 0.5);
            game.DebugEndMatch(now);
            var summaryReset = game.BotStuckKillsThisMatch == 0; // the end-of-match line was written and the count reset
            Pit();
            var endKills = game.BotStuckKills;
            Run(game, ref now, 4);
            Line($"results screen ({game.Phase}): stuck kills {endKills}->{game.BotStuckKills}, end-of-match summary written {summaryReset}");
            StuckNotes.Add($"results screen ({game.Phase}): stuck kills {endKills}->{game.BotStuckKills}, end-of-match summary written {summaryReset}");
            if (game.Phase != MatchPhase.Ended || game.BotStuckKills != endKills)
                Fail("the stuck watch ran on the results screen");
            if (!summaryReset)
                Fail("the end of the match didn't write the stuck summary (the per-match count wasn't reset)");
        }

        // ---- Wedged against a wall (no path planned): jump / re-plan gets it round before the timeout ----
        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            game.HotSwapMap(lab);
            NavGraph.Pump(double.MaxValue);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var host = game.Local!;
            var bot = game.Players.Values.First(p => p.Bot != null);
            var b = bot.Bot!;
            host.Alive = false;
            host.Health = 0;
            game.SelfTestPlace(bot, new Vector3(6.5f, 0, 0), MathF.PI / 2);
            var goal = new Vector3(11, 0, 0);
            b.ForcedGoal = goal;
            b.ClearPath();
            b.NextPathTime = now + 1000; // no path: it steers straight at the goal, into the wall
            var start = now;
            var took = -1.0;
            var maxStage = 0;
            while (now - start < 13 && took < 0)
            {
                Run(game, ref now, 1.0 / World.TickRate);
                maxStage = Math.Max(maxStage, b.StuckStage);
                if (Flat2(bot.Position - goal) < 1.5f)
                    took = now - start;
            }
            b.ForcedGoal = null;
            Line($"wedged on a wall: round it in {(took < 0 ? "NEVER" : $"{took:0.0} s")} (timeout {game.BotStuckSuicideSeconds:0} s), stage reached {maxStage}, stuck kills {game.BotStuckKills}");
            StuckNotes.Add($"wedged on a wall: round it in {(took < 0 ? "NEVER" : $"{took:0.0} s")} (timeout {game.BotStuckSuicideSeconds:0} s), stage reached {maxStage}, stuck kills {game.BotStuckKills}");
            if (took < 0 || game.BotStuckKills != 0)
                Fail("a bot wedged against a wall didn't recover by jumping / re-planning before the timeout");
        }

        // ---- Holding on purpose: the hill (KotH) and a perch never count, even with a short timeout ----
        Game.SeedNextGame = 4711; // seeded: the same walk to the hill every run (it was flaky unseeded)
        using (var game = ModeGame(1, GameMode.KingOfTheHill, 1))
        {
            NavGraph.Pump(double.MaxValue);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var host = game.Local!;
            var bot = game.Players.Values.First(p => p.Bot != null);
            var b = bot.Bot!;
            game.BotStuckSuicideSeconds = 6f;
            var onHill = 0.0;
            var maxSecs = 0f;
            for (var t = 0.0; t < 25; t += 1.0 / World.TickRate)
            {
                host.Alive = false;
                host.Health = 0;
                b.ObjectiveMinded = true;
                Run(game, ref now, 1.0 / World.TickRate);
                if (b.Mode == BotMode.Objective && b.Objective is { } o && Flat2(o - bot.Position) < 5f)
                    onHill += 1.0 / World.TickRate;
                maxSecs = MathF.Max(maxSecs, b.StuckSecs);
            }
            Line($"KotH: {onHill:0.0} s of 25 holding the hill, longest stuck clock {maxSecs:0.0} s, stuck kills {game.BotStuckKills} (timeout {game.BotStuckSuicideSeconds:0} s)");
            StuckNotes.Add($"KotH: {onHill:0.0} s of 25 holding the hill, longest stuck clock {maxSecs:0.0} s, stuck kills {game.BotStuckKills} (timeout {game.BotStuckSuicideSeconds:0} s)");
            if (onHill < 3)
                Fail($"the KotH bot never got to hold the hill ({onHill:0.0} s), so the check proves nothing");
            if (game.BotStuckKills != 0)
                Fail("a bot holding the KotH hill killed itself as stuck");
        }
        using (var game = ModeGame(1, GameMode.FreeForAll))
        {
            NavGraph.Pump(double.MaxValue);
            Run(game, ref now, Game.CountdownSeconds + 0.3);
            var host = game.Local!;
            var bot = game.Players.Values.First(p => p.Bot != null);
            var b = bot.Bot!;
            game.BotStuckSuicideSeconds = 4f;
            host.Alive = false;
            host.Health = 0;
            var nav = NavGraph.Current;
            var perch = nav.Nodes[nav.MainNodes[nav.MainNodes.Length / 3]];
            game.SelfTestPlace(bot, perch, 0);
            Run(game, ref now, 1.0 / World.TickRate); // (the placement starts a new life: let the bot reset for it first)
            b.HoldUntil = now + 100;
            b.HoldAnchor = perch;
            b.Waypoint = perch;
            b.GoalUntil = now + 100;
            var held = 0.0;
            var maxSecs = 0f;
            for (var t = 0.0; t < 15; t += 1.0 / World.TickRate)
            {
                Run(game, ref now, 1.0 / World.TickRate);
                if (b.HoldUntil > now)
                    held += 1.0 / World.TickRate;
                maxSecs = MathF.Max(maxSecs, b.StuckSecs);
            }
            Line($"perch ({b.Personality}): held {held:0.0} s of 15, longest stuck clock {maxSecs:0.0} s, stuck kills {game.BotStuckKills} (timeout {game.BotStuckSuicideSeconds:0} s)");
            StuckNotes.Add($"perch ({b.Personality}): held {held:0.0} s of 15, longest stuck clock {maxSecs:0.0} s, stuck kills {game.BotStuckKills} (timeout {game.BotStuckSuicideSeconds:0} s)");
            if (game.BotStuckKills != 0)
                Fail("a bot holding a perch killed itself as stuck");

            // ---- No allocations: the watch through all three recovery stages (position held still) ----
            b.Mode = BotMode.Roam;
            b.HoldUntil = 0;
            var wish = new Vector3(1, 0, 0);
            void Steps(double seconds)
            {
                for (var t = 0.0; t < seconds; t += World.TickDt)
                {
                    game.ServerTick++;
                    now += World.TickDt;
                    game.SelfTestStuckWatch(bot, wish, false, false, now);
                }
            }
            game.ServerTick++;
            game.SelfTestStuckWatch(bot, wish, true, false, now); // stationary: starts over
            Steps(game.BotStuckSuicideSeconds * 0.7); // warm (JIT)
            game.SelfTestStuckWatch(bot, wish, true, false, now);
            var before = GC.GetAllocatedBytesForCurrentThread();
            Steps(game.BotStuckSuicideSeconds * 0.9);
            var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            var stage = b.StuckStage;
            // Intentional stillness and fights don't run the clock.
            game.SelfTestStuckWatch(bot, wish, true, false, now);
            for (var i = 0; i < 300; i++)
            {
                game.ServerTick++;
                now += World.TickDt;
                game.SelfTestStuckWatch(bot, wish, i % 2 == 0, true, now);
            }
            var fightSecs = b.StuckSecs;
            Line($"watch through stage {stage}: {alloc} bytes allocated; 5 s standing still / fighting: stuck clock {fightSecs:0.00} s");
            StuckNotes.Add($"watch through stage {stage}: {alloc} bytes allocated; 5 s standing still / fighting: stuck clock {fightSecs:0.00} s");
            if (alloc != 0)
                Fail($"the stuck watch allocated {alloc} bytes");
            if (stage != 3 || !bot.Alive)
                Fail($"the stuck watch didn't reach stage 3 before the timeout (stage {stage}, alive {bot.Alive})");
            if (fightSecs > 0)
                Fail("standing still on purpose or fighting ran the stuck clock");

            // ---- Zombies co-op (the mode has no respawn): a stuck bot hops, strafes, then joins the nearest teammate ----
            game.AddBot(BotDifficulty.Normal, quiet: true);
            var mate = game.Players.Values.First(p => p.Bot != null && p != bot);
            game.SelfTestPlace(mate, perch + new Vector3(0, 0, 6), 0);
            game.SelfTestPlace(bot, perch, 0);
            var jumped = false;
            var backed = false;
            var rescues = game.BotStuckRescues;
            for (var t = 0.0; t < game.BotStuckSuicideSeconds + 0.1; t += World.TickDt)
            {
                game.ServerTick++;
                now += World.TickDt;
                var buttons = game.SelfTestZombieStuck(bot, true, Buttons.Forward, now);
                jumped |= (buttons & Buttons.Jump) != 0;
                backed |= (buttons & Buttons.Back) != 0;
            }
            var near = Vector3.Distance(bot.Position, mate.Position);
            Line($"Zombies co-op: hopped {jumped}, backed off {backed}, teleported {game.BotStuckRescues - rescues} time(s), now {near:0.0} m from its teammate, log: {game.LastStuckLog}");
            StuckNotes.Add($"Zombies co-op: hopped {jumped}, backed off {backed}, teleported {game.BotStuckRescues - rescues} time(s), now {near:0.0} m from its teammate, log: {game.LastStuckLog}");
            if (!jumped || !backed || game.BotStuckRescues != rescues + 1 || near > 0.5f || !bot.Alive)
                Fail("a stuck Zombies co-op bot didn't hop and back off, then teleport to its teammate");
        }
        World.SetMap(Maps.All[0]);
    }

    private static float Flat2(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);
}
