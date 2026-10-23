using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Bot lag compensation": shots resolved at the current server tick (bots, turrets, bot melee)
/// rewind every target correctly whatever the update order. Each player's history slot for a tick is only written once
/// they have been simulated that tick, so the slot read mid-tick for someone later in the order used to be the one from
/// <see cref="Player.HistorySize"/> ticks ago, and bots missed players who joined after them. Two short checks: the
/// rewind's stale-slot guard on its own, and a Hard bot against a strafing target for three seconds with the target
/// joining after the bot, then before it: the hit rates must match. No long matches.
/// </summary>
public static partial class SelfTest
{
    private static void CheckBotLagComp()
    {
        try
        {
            CheckLagCompSlots();
            var later = LagCompDuel(targetJoinsFirst: false);
            var earlier = LagCompDuel(targetJoinsFirst: true);
            var note = $"bot v strafing target, 12 m, 3 s: target joined after the bot {later.Hits}/{later.Shots} hits, before it {earlier.Hits}/{earlier.Shots}";
            Line(note);
            Line("::warning title=Bot lag compensation (results)::" + note);
            if (later.Shots < 5 || earlier.Shots < 5)
                Fail($"the bot hardly shot (after {later.Shots}, before {earlier.Shots} shots) [TEMP: {DuelInfo}]");
            else
            {
                var a = (double)later.Hits / later.Shots;
                var b = (double)earlier.Hits / earlier.Shots;
                if (b < 0.25)
                    Fail($"the bot hits a target that joined first only {b:P0} of the time (expected most shots)");
                if (Math.Abs(a - b) > 0.2)
                    Fail($"the bot's hit rate depends on join order: {a:P0} on a later joiner, {b:P0} on an earlier one");
            }
        }
        finally
        {
            Game.SeedNextGame = null;
            World.SetMap(Maps.All[0]);
        }
    }

    /// <summary>The rewind uses a slot only when it was recorded at the tick asked for (or is a spawn fill in the past).</summary>
    private static void CheckLagCompSlots()
    {
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0);
        var p = game.SelfTestAddHuman();
        game.SelfTestPlace(p, new Vector3(5, 0, 5), 0);
        var now = 1.0;
        for (var i = 0; i < 140; i++) // more than one lap of the ring, so every slot holds a recorded tick
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
        }
        var t = game.ServerTick;
        var live = new Vector3(6, 0, 5);
        p.Teleport(live); // moved after the last record
        var stale = new Vector3(40, 0, 40);

        // Mid-tick: the current tick's slot still holds the entry from a lap ago. The rewind must use the live state.
        var keep = p.History[t % Player.HistorySize];
        p.History[t % Player.HistorySize] = new HistoryEntry { Position = stale, Tick = t - Player.HistorySize };
        var now0 = game.SelfTestHistoricState(p, t).Position;
        p.History[t % Player.HistorySize] = keep;

        // A recorded past tick is used as recorded; a stale past slot falls back to live; a spawn fill (tick 0) is kept.
        var past = t - 3;
        var keepPast = p.History[past % Player.HistorySize];
        p.History[past % Player.HistorySize] = new HistoryEntry { Position = new Vector3(4, 0, 5), Tick = past };
        var recorded = game.SelfTestHistoricState(p, past).Position;
        p.History[past % Player.HistorySize] = new HistoryEntry { Position = stale, Tick = past - Player.HistorySize };
        var stalePast = game.SelfTestHistoricState(p, past).Position;
        p.History[past % Player.HistorySize] = new HistoryEntry { Position = new Vector3(3, 0, 5) };
        var fill = game.SelfTestHistoricState(p, past).Position;
        p.History[past % Player.HistorySize] = keepPast;

        Line($"rewind at tick {t}: stale current slot -> {now0}, recorded past {recorded}, stale past -> {stalePast}, fill {fill}");
        if (Vector3.Distance(now0, live) > 1e-3f)
            Fail($"a stale slot for the current tick was used (got {now0}, live {live})");
        if (Vector3.Distance(recorded, new Vector3(4, 0, 5)) > 1e-3f)
            Fail($"a recorded past tick wasn't used (got {recorded})");
        if (Vector3.Distance(stalePast, live) > 1e-3f)
            Fail($"a stale past slot was used (got {stalePast})");
        if (Vector3.Distance(fill, new Vector3(3, 0, 5)) > 1e-3f)
            Fail($"a spawn/teleport fill in the past wasn't used (got {fill})");
    }

    /// <summary>
    /// After the countdown, a Hard bot held at (5, 0, 5) against a pretend person strafing 2.5 m either side of
    /// (5, 0, 17) at 4 m/s, for 3 s after 1 s to acquire. The target never dies (health is topped up every tick). Returns the bot's shots and hits.
    /// </summary>
    private static (int Shots, int Hits) LagCompDuel(bool targetJoinsFirst)
    {
        Game.SeedNextGame = 9301;
        var map = Maps.All[0];
        World.SetMap(map);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        game.Mode = GameMode.FreeForAll;
        NavGraph.Pump(double.MaxValue);
        Player? target = targetJoinsFirst ? game.SelfTestAddHuman() : null;
        var before = game.Players.Keys.ToHashSet();
        game.AddBot(BotDifficulty.Hard, BotPersonality.Anchor, quiet: true);
        var bot = game.Players.Values.First(p => p.Bot != null && !before.Contains(p.Id));
        target ??= game.SelfTestAddHuman();
        game.SelfTestPerks(bot, PerkSet.Empty);
        game.SelfTestPerks(target, PerkSet.Empty);
        bot.Primary = Weapons.DefaultPrimary.Id;
        game.SelfTestSpawn(bot);
        var now = 1.0;
        for (var t = 0.0; t < Game.CountdownSeconds + 0.5; t += World.TickDt) // past the countdown (nobody can be hurt)
        {
            now += World.TickDt;
            game.Update(now, World.TickDt);
        }
        if (game.Phase != MatchPhase.Playing)
            Fail($"lag compensation duel: still {game.Phase} after the countdown");
        var home = new Vector3(5, 0, 5);
        var centre = new Vector3(5, 0, 17);
        game.SelfTestPlace(bot, home, 0);
        game.SelfTestPlace(target, centre, MathF.PI);

        int shots = 0, hits = 0, dHold = 0, dReload = 0, dNoTarget = 0, dMode = 0, dMulti = 0, dSide = 0, dNone = 0;
        const int warm = World.TickRate, run = 3 * World.TickRate;
        for (var i = 0; i < warm + run; i++)
        {
            // Strafe: a triangle wave, 2.5 m either side, 4 m/s (moved between ticks, like a person's input would).
            var s = i * World.TickDt * 4f % 10f;
            var x = s < 5f ? s - 2.5f : 7.5f - s;
            target.Teleport(centre + new Vector3(x, 0, 0));
            target.Alive = true;
            target.Health = World.MaxHealth;
            target.ImmuneUntil = 0;
            if (Vector3.Distance(bot.Position, home) > 0.5f)
                bot.Teleport(home); // the bot holds its spot (fixed range)
            foreach (var other in game.Players.Values)
                if (other != bot && other != target && other.Alive)
                {
                    other.Alive = false; // only these two: the host's own player (wherever it spawned) stays out of it
                    other.Health = 0;
                    other.WantsRespawn = false;
                }
            var ammo = bot.Bot!.Ammo;
            now += World.TickDt;
            game.Update(now, World.TickDt);
            if (i < warm)
                continue;
            if (bot.Bot.HoldFire) dHold++;
            if (bot.Bot.ReloadUntil > 0) dReload++;
            if (bot.Bot.Target != target.Id) dNoTarget++;
            if (bot.Bot.Target == byte.MaxValue) dNone++;
            if (bot.Bot.Mode != BotMode.Engage) dMode++;
            if (bot.Bot.Ammo < ammo - 1) dMulti++;
            if (bot.Weapon != bot.Primary) dSide++;
            if (bot.Bot.Ammo == ammo - 1)
            {
                shots++;
                if (!target.Alive || target.Health < World.MaxHealth)
                    hits++;
            }
        }
        DuelInfo += $" | ticks: hold fire {dHold}, reloading {dReload}, other target {dNoTarget} (none {dNone}), not engaging {dMode}, multi-shot {dMulti}, off primary {dSide}; mode {bot.Bot!.Mode}, weapon {bot.Weapon}, deaths {bot.Deaths}, stuck kills {game.BotStuckKills}, ammo {bot.Bot.Ammo}, alive {bot.Alive}, log {game.LastStuckLog}";
        return (shots, hits);
    }

    private static string DuelInfo = "";
}
