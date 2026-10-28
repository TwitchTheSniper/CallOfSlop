using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies start round" (Source/Game/ZombiesStartRound.cs; docs/zombies/ZOMBIES-MODE.md, "Horde Test,
/// start round and test kit"): the start-round economy, a run on Horde Test starting at round 30 with the test kit
/// (points, power-up schedule, perks, slots, guns, the power), its horde filling to the round-30 cap (time to fill, tick
/// cost), the host's skip to a round, the settings file, and the settings reaching a client over loopback.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesStartRound()
    {
        if (Maps.Find("Horde Test") is not { Zombies: { } zm } map)
        {
            Fail("Maps/Dev/HordeTest.json is missing or has no \"zombies\" section");
            return;
        }
        if (!zm.TestKit || zm.Windows.Count < 20 || zm.Spawners.Count < 40)
            Fail($"Horde Test should allow the test kit and have 20+ windows and 40+ spawners (has {zm.Windows.Count} and {zm.Spawners.Count})");
        StartRoundEconomy();
        var now = 0.0;
        StartRoundRun(map, zm, ref now);
        StartRoundSettings();
        StartRoundLoopback(map);
    }

    private static void StartRoundEconomy()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "rules");
        int[] rounds = [1, 5, 10, 20, 30, 40, 50];
        var solo = rounds.Select(r => Game.StartPointsFor(rules, r, 1)).ToArray();
        var four = rounds.Select(r => Game.StartPointsFor(rules, r, 4)).ToArray();
        Line($"start points by start round (solo): {string.Join(" ", rounds.Select((r, i) => $"r{r} {solo[i]}"))}; each of 4: {string.Join(" ", rounds.Select((r, i) => $"r{r} {four[i]}"))}");
        if (solo[0] != rules.StartPoints || four[0] != rules.StartPoints || solo.Zip(solo.Skip(1)).Any(p => p.Second <= p.First) || solo[4] < 20000 || solo[4] > 200000)
            Fail("the start points should be the rules' start at round 1 and rise with the start round (round 30 solo between 20000 and 200000)");
    }

    /// <summary>Round 30 with the test kit on Horde Test: the economy and kit at the start, the fill to the cap, then a skip to round 32.</summary>
    private static void StartRoundRun(Map map, ZombiesMap zm, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 1, onMap: map, setup: s =>
        {
            s.ZStartRound = 30;
            s.ZTestKit = true;
        });
        var me = g.Local!;
        var zp = g.ZP(me);
        var allPerks = (1 << Math.Min(16, g.ZRules.Perks.Count)) - 1;
        var want = Game.StartPointsFor(g.ZRules, 30, 1);
        var slots = Enumerable.Range(0, 4).Count(i => zp.Slots[i] != null);
        Line($"Horde Test, start round 30 + test kit: round {g.ZRound} before the first, {zp.Points} points (want {want}), perks {zp.Perks:x4} (all {allPerks:x4}), slots 3-4 {me.ExtraSlots}, "
             + $"{slots} guns, melee {(zp.Slots[4] != null ? "yes" : "no")}, power {g.ZPower}; power-ups due at {g.PowerDueAt:0} ({g.PowerEarned:0} earned)");
        if (g.ZRound != 29 || zp.Points != want)
            Fail("a run started at round 30 should wait for round 30 with the round's start points");
        if (zp.Perks != allPerks || !me.ExtraSlots || slots != 4 || zp.Slots[4] == null || !g.ZPower)
            Fail("the test kit should give every perk, slots 3-4, four guns, a melee build and the power");
        if (g.PowerEarned < 100000 || g.PowerDueAt <= g.PowerEarned)
            Fail("a run started at round 30 should carry on the power-up schedule from what the skipped rounds earned");

        // The round starts after the (shortened) delay and fills to its cap.
        me.ImmuneUntil = 1e9;
        ZTicks(g, ref now, 1.2);
        var cap = g.ZAliveCap();
        double worst = 0, sum = 0;
        var ticks = 0;
        var reached = -1.0;
        var t0 = now;
        for (var t = 0; t < 60 * 40 && reached < 0; t++)
        {
            now += World.TickDt;
            var c0 = ThreadCpu.NowMs();
            g.SelfTestZombiesTick(now);
            var ms = ThreadCpu.NowMs() - c0;
            sum += ms;
            if (t >= 60)
                worst = Math.Max(worst, ms); // (after the first second: one-off JIT)
            ticks++;
            if (g.AliveZombies >= cap)
                reached = now - t0;
        }
        var mean = sum / Math.Max(1, ticks);
        var queued = g.Horde.Count(z => z.Alive && z.State == ZState.ToWindow);
        Line($"  round {g.ZRound}: cap {cap}, {g.AliveZombies} alive after {(reached >= 0 ? $"{reached:0.0} s" : "40 s (not reached)")}, {queued} outside or at windows; "
             + $"tick {mean:0.00} ms CPU mean, worst {worst:0.00} ms ({(ThreadCpu.Kind)}); {g.HordeSummary()}");
        if (g.ZRound != 30 || cap < 300)
            Fail($"round 30 should be under way with a cap of 300 or more (round {g.ZRound}, cap {cap})");
        if (reached < 0 || reached > 35)
            Fail($"Horde Test should fill to the round-30 cap of {cap} within 35 s (got {g.AliveZombies})");
        if (TickCpu.Available ? worst > 8 : mean > 4)
            Fail($"filling Horde Test at round 30 costs too much a tick ({mean:0.00} ms mean, {worst:0.00} ms worst)");

        // The host skips to round 32: the horde goes, the round starts at once, the points are raised to round 32's.
        zp.Points = 100;
        var before = zp.Points;
        var skipped = g.HostSkipToRound(32, now);
        var afterSkip = (g.ZRound, g.AliveZombies, zp.Points);
        ZTicks(g, ref now, 2.0);
        Line($"  host skip to round 32: {skipped}; round {afterSkip.ZRound}, {afterSkip.AliveZombies} alive at once, points {before} -> {afterSkip.Points}; 2 s later {g.AliveZombies} up");
        if (!skipped || afterSkip.ZRound != 32 || afterSkip.AliveZombies != 0 || afterSkip.Points != Game.StartPointsFor(g.ZRules, 32, 1) || g.AliveZombies == 0)
            Fail("the host's skip should clear the horde, start the round at once and raise the points to the round's");
    }

    private static void StartRoundSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"cos-startround-{Environment.ProcessId}.cfg");
        try
        {
            File.WriteAllLines(tmp, ["zombie_start_round=30", "zombie_test_kit=1"]);
            var s = Settings.Load(tmp);
            File.WriteAllLines(tmp, ["zombie_start_round=9999"]);
            var high = Settings.Load(tmp);
            Line($"  settings: 'zombie_start_round=30' -> {s.ZombieStartRound}, test kit {s.ZombieTestKit}; '=9999' -> {high.ZombieStartRound}; default {new Settings().ZombieStartRound}, kit {new Settings().ZombieTestKit}");
            if (s.ZombieStartRound != 30 || !s.ZombieTestKit || high.ZombieStartRound != Game.MaxStartRound || new Settings().ZombieStartRound != 1 || new Settings().ZombieTestKit)
                Fail("the start round and test kit settings should load from the settings file (and default to round 1, no kit)");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    /// <summary>A loopback host on Horde Test sets the start round and the kit in its lobby: a client sees both.</summary>
    private static void StartRoundLoopback(Map map)
    {
        const ushort port = 26281;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 3), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, mapId: map.Id);
        server.MapVoteEnabled = false;
        server.WarmupEnabled = true;
        server.LobbyEnabled = true;
        server.Mode = GameMode.Zombies;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.5, ref now, clock);
        var before = (a.ZStartRound, a.ZTestKit);
        server.ZStartRound = 30;
        server.ZTestKit = true;
        Pump(server, [a], 0.6, ref now, clock);
        Line($"  loopback: client in {a.Phase}, mode {a.Mode}; start round {before.ZStartRound} -> {a.ZStartRound}, test kit {before.ZTestKit} -> {a.ZTestKit} (on this map {a.TestKitOn})");
        if (before.ZStartRound != 1 || before.ZTestKit || a.ZStartRound != 30 || !a.ZTestKit || !a.TestKitOn)
            Fail("the host's start round and test kit should reach a client in the lobby state");
    }
}
