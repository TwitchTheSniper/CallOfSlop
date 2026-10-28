namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies player scaling" (the playtest board's player-scaling item, 2026-10-27; docs/zombies/ZOMBIES-MODE.md,
/// "Player scaling"): the round sizes and alive caps for 1-4 players at several rounds from the shipped rules; a join
/// mid-round raises the round's total, the zombies still to spawn and the alive cap at once (the killed and alive kept);
/// a leave lowers them without ending the round, even when more are already out than the smaller round would send.
/// Short and seeded: fast spawning (a tweak) on the Zombies Test map, the zombies killed by script.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesPlayerScaling()
    {
        var now = 0.0;
        ScalingTable();
        ScalingJoinLeave(ref now);
        ScalingLeaveNeverEnds(ref now);
    }

    /// <summary>The shipped numbers: totals and caps for 1-4 players at rounds 1, 5, 10, 20, 30 and 40.</summary>
    private static void ScalingTable()
    {
        var r = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "rules");
        int[] rounds = [1, 5, 10, 20, 30, 40];
        for (var p = 1; p <= 4; p++)
            Line($"{p} player{(p == 1 ? "" : "s")}: totals {string.Join(" ", rounds.Select(x => $"r{x} {r.ZombieCount(x, p)}"))}; alive caps {string.Join(" ", rounds.Select(x => $"r{x} {r.MaxAliveFor(x, p)}"))}");
        Line($"  rules: countPerExtraPlayer {r.CountPerExtraPlayer}, maxAlivePerExtraPlayer {r.MaxAlivePerExtraPlayer}, maxAlive {r.MaxAlive} (code limit {Game.MaxZombies})");
        if (r.CountPerExtraPlayer != 12 || MathF.Abs(r.MaxAlivePerExtraPlayer - 0.5f) > 1e-4f || r.MaxAlive != 600 || Game.MaxZombies < r.MaxAlive)
            Fail("the shipped scaling should be 12 more zombies a round and half again the alive cap per extra player, at most 600 alive");
        // Pinned: the developer's proposed numbers (ZombiesRules.json "rounds").
        if (r.ZombieCount(10, 1) != 72 || r.ZombieCount(10, 4) != 180 || r.ZombieCount(20, 2) != 432 || r.ZombieCount(1, 4) != 15)
            Fail($"round totals: r10 solo {r.ZombieCount(10, 1)} (want 72), 4 players {r.ZombieCount(10, 4)} (180), r20 2 players {r.ZombieCount(20, 2)} (432), r1 4 players {r.ZombieCount(1, 4)} (15)");
        if (r.MaxAliveFor(1, 1) != 24 || r.MaxAliveFor(5, 1) != 40 || r.MaxAliveFor(10, 1) != 90 || r.MaxAliveFor(20, 1) != 220 || r.MaxAliveFor(30, 1) != 400 || r.MaxAliveFor(40, 1) != 600
            || r.MaxAliveFor(10, 4) != 225 || r.MaxAliveFor(20, 4) != 550 || r.MaxAliveFor(30, 2) != 600)
            Fail("alive caps should follow the curve 24/40/90/220/400/600 (rounds 1/5/10/20/30/40), x1.5 per extra player, at most 600");
        foreach (var x in rounds)
            for (var p = 2; p <= 4; p++)
                if (r.ZombieCount(x, p) <= r.ZombieCount(x, p - 1) || r.MaxAliveFor(x, p) < r.MaxAliveFor(x, p - 1) || r.MaxAliveFor(x, p) > r.MaxAlive)
                    Fail($"round {x}: {p} players should get more zombies and no smaller cap than {p - 1} (never over the hard cap)");
    }

    private static Game ScalingGame(ref double now) =>
        ZGame(0, ref now, seed: 77, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.SpawnDelay = 0.05f;
            r.SpawnDelayMin = 0.05f;
        });

    /// <summary>The zombies out this round so far (killed + alive): the round's total less those still to spawn.</summary>
    private static int ScalingOut(Game g) => g.SelfTestRoundTotal - g.SelfTestToSpawn;

    private static void KillAllZombies(Game g, Player by, double now)
    {
        foreach (var z in g.Horde)
            if (z.Alive)
                g.DamageZombie(by, z, 1_000_000, false, false, now);
    }

    /// <summary>Round 10 solo, a person joins (the total, the queue and the cap go up), then leaves again (back down).</summary>
    private static void ScalingJoinLeave(ref double now)
    {
        using var g = ScalingGame(ref now);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        g.SelfTestBeginRound(10, now);
        // Some out, a few killed.
        for (var t = 0; t < 60 * 4 && g.AliveZombies < 20; t++)
            ZTicks(g, ref now, World.TickDt);
        var k = 0;
        foreach (var z in g.Horde)
            if (z.Alive && k++ < 8)
                g.DamageZombie(me, z, 1_000_000, false, false, now);
        ZTicks(g, ref now, World.TickDt);
        var (total1, left1, cap1, out1, alive1) = (g.SelfTestRoundTotal, g.SelfTestToSpawn, g.ZAliveCap(), ScalingOut(g), g.AliveZombies);

        var joiner = g.SelfTestAddHuman();
        ZTicks(g, ref now, World.TickDt);
        var (total2, left2, cap2, out2) = (g.SelfTestRoundTotal, g.SelfTestToSpawn, g.ZAliveCap(), ScalingOut(g));

        g.SelfTestRemovePlayer(joiner);
        ZTicks(g, ref now, World.TickDt);
        var (total3, left3, cap3, out3, phase3) = (g.SelfTestRoundTotal, g.SelfTestToSpawn, g.ZAliveCap(), ScalingOut(g), g.ZPhase);
        var r = g.ZRules;
        Line($"round 10: solo total {total1}, {out1} out ({alive1} alive), {left1} to spawn, cap {cap1}; a person joins: total {total2}, {out2} out, {left2} to spawn, cap {cap2}; "
             + $"leaves: total {total3}, {out3} out, {left3} to spawn, cap {cap3}, {phase3}");
        if (total1 != r.ZombieCount(10, 1) || cap1 != r.MaxAliveFor(10, 1) || out1 < 10)
            Fail("round 10 solo should start at the solo total and cap (with some zombies out)");
        if (total2 != r.ZombieCount(10, 2) || cap2 != r.MaxAliveFor(10, 2) || cap2 <= cap1 || left2 != left1 + (total2 - total1) - (out2 - out1))
            Fail("a join mid-round should raise the round's total to the 2-player count, the zombies still to spawn by the difference and the alive cap at once");
        if (total3 != total1 || cap3 != cap1 || left3 != Math.Max(0, total3 - out3) || phase3 != ZRoundPhase.Round)
            Fail("a leave mid-round should bring the total and the cap back down (killed and alive kept) without ending the round");
    }

    /// <summary>
    /// Round 5 with a second player: more zombies out than the solo round sends, then the second player leaves: nothing
    /// more spawns, the round goes on while any are alive; with all of them dead and some still owed, one more still comes
    /// and its kill ends the round the usual way.
    /// </summary>
    private static void ScalingLeaveNeverEnds(ref double now)
    {
        using var g = ScalingGame(ref now);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var other = g.SelfTestAddHuman();
        g.SelfTestBeginRound(5, now);
        var r = g.ZRules;
        var solo = r.ZombieCount(5, 1);
        var duo = g.SelfTestRoundTotal;
        // Spawn and kill until more are out than the solo round has, with some alive.
        for (var t = 0; t < 60 * 20 && ScalingOut(g) <= solo + 2; t++)
        {
            ZTicks(g, ref now, World.TickDt);
            if (ScalingOut(g) < solo)
                KillAllZombies(g, me, now);
        }
        ZTicks(g, ref now, World.TickDt);
        var (outA, aliveA) = (ScalingOut(g), g.AliveZombies);
        g.SelfTestRemovePlayer(other);
        ZTicks(g, ref now, World.TickDt);
        var (leftB, aliveB, phaseB) = (g.SelfTestToSpawn, g.AliveZombies, g.ZPhase);
        Line($"round 5: 2 players total {duo} (solo {solo}); {outA} out ({aliveA} alive) when one leaves: {leftB} to spawn, {aliveB} alive, {phaseB}");
        if (duo != r.ZombieCount(5, 2) || outA <= solo || aliveA == 0)
            Fail("the scripted round 5 should have more zombies out than the solo count, some alive");
        if (leftB != 0 || phaseB != ZRoundPhase.Round || aliveB == 0)
            Fail("a leave with more out than the smaller round should stop the spawning and keep the round going while zombies are alive");
        KillAllZombies(g, me, now);
        ZTicks(g, ref now, World.TickDt);
        var phaseC = g.ZPhase;
        Line($"  all dead: {phaseC}");
        if (phaseC != ZRoundPhase.Break)
            Fail("with nothing left to spawn and every zombie dead, the round should end");

        // All dead and some still owed when someone leaves: one more comes, the round does not end there and then.
        using var g2 = ScalingGame(ref now);
        var me2 = g2.Local!;
        me2.ImmuneUntil = 1e9;
        var other2 = g2.SelfTestAddHuman();
        g2.SelfTestBeginRound(5, now);
        for (var t = 0; t < 60 * 20 && ScalingOut(g2) < solo + 3; t++)
        {
            ZTicks(g2, ref now, World.TickDt);
            KillAllZombies(g2, me2, now);
        }
        ZTicks(g2, ref now, World.TickDt);
        KillAllZombies(g2, me2, now);
        var (outD, ownedD, aliveD) = (ScalingOut(g2), g2.SelfTestToSpawn, g2.AliveZombies);
        g2.SelfTestRemovePlayer(other2);
        ZTicks(g2, ref now, World.TickDt);
        var (leftE, phaseE) = (g2.SelfTestToSpawn + g2.AliveZombies, g2.ZPhase);
        var ended = -1.0;
        var t0 = now;
        for (var t = 0; t < 60 * 5 && ended < 0; t++)
        {
            ZTicks(g2, ref now, World.TickDt);
            KillAllZombies(g2, me2, now);
            if (g2.ZPhase == ZRoundPhase.Break)
                ended = now - t0;
        }
        Line($"  {outD} out, {ownedD} still owed, {aliveD} alive when one leaves: {leftE} to come, {phaseE}; the round ended {(ended >= 0 ? $"{ended:0.00} s later (its last zombie killed)" : "not within 5 s")}");
        if (ownedD == 0 || aliveD != 0 || leftE != 1 || phaseE != ZRoundPhase.Round)
            Fail("a leave with every zombie dead and some still owed should keep the round going with one more to come");
        if (ended < 0)
            Fail("the round should end the usual way once that last zombie is killed");
    }
}
