using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Horde respawn" (docs/zombies/ZOMBIES-MODE.md, "Horde controller"): the chased player stops being a
/// target and comes back, and the horde must go on (another living player, or wait and resume), never freeze. Short
/// seeded scenarios on Horde Test (40 zombies in the arena, round 10), the players running loops round the dais so the
/// horde keeps walking; the zombies moving each tick are counted before and after each event.
/// </summary>
public static partial class SelfTest
{
    private static void CheckHordeRespawn()
    {
        if (Maps.Find("Horde Test") is not { Zombies: { } zm } map)
        {
            Fail("no Horde Test map");
            return;
        }
        HordeRespawnSolo(map, zm, secondWind: true);
        HordeRespawnSolo(map, zm, secondWind: false);
        HordeRespawnCoop(map, zm);
    }

    /// <summary>A loop round the dais (radius 18 m, about 6.5 m/s: faster than the horde, so it keeps walking), from angle <paramref name="phase"/>.</summary>
    private static Vector3 HordeLoop(Map map, double t, float phase)
    {
        var a = (float)(t * 0.36) + phase;
        var p = new Vector3(MathF.Sin(a) * 18f, 0, MathF.Cos(a) * 18f);
        p.Y = map.GroundHeight(p.X, p.Z);
        return p;
    }

    /// <summary>
    /// Runs <paramref name="seconds"/> of the horde with the standing players on their loops (the others stay put), and
    /// returns the mean number of zombies moving a tick (and the fewest over any half second).
    /// </summary>
    private static (double Mean, double Low) HordeRun(Game g, Map map, ref double now, double t0, double seconds, Dictionary<ushort, Vector3> prev, params Player[] runners)
    {
        var minStep = g.ZRules.WalkSpeed * World.TickDt * 0.6f;
        int sum = 0, ticks = 0, win = 0, winTicks = 0;
        var low = double.MaxValue;
        for (var t = 0.0; t < seconds - 1e-9; t += World.TickDt)
        {
            for (var i = 0; i < runners.Length; i++)
                if (g.Players.ContainsKey(runners[i].Id) && g.IsStanding(runners[i]))
                    runners[i].Teleport(HordeLoop(map, now - t0, i * MathF.PI));
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
            var moving = 0;
            foreach (var z in g.Horde)
            {
                var had = prev.TryGetValue(z.Id, out var p0);
                prev[z.Id] = z.Position;
                if (!had || !z.Alive)
                    continue;
                var d = z.Position - p0;
                d.Y = 0;
                if (d.Length() >= minStep && d.Length() < 1f)
                    moving++;
            }
            sum += moving;
            ticks++;
            win += moving;
            if (++winTicks == 30)
            {
                low = Math.Min(low, win / 30.0);
                win = winTicks = 0;
            }
        }
        return (sum / (double)Math.Max(1, ticks), low == double.MaxValue ? 0 : low);
    }

    /// <summary>
    /// Solo: the raw case (Alive false for one tick: with nobody standing and no self-revive that is the run's game over,
    /// as in play) and a real down with Second Wind (nobody standing for 5 s, then back up): the horde waits, then goes on.
    /// </summary>
    private static void HordeRespawnSolo(Map map, ZombiesMap zm, bool secondWind)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var t0 = now;
        me.Teleport(HordeLoop(map, 0, 0));
        HordePlace(g, map, zm, 40, now, 777u, _ => zm.AreaIndex("Arena"));
        g.SelfTestBeginRound(10, now);
        var prev = new Dictionary<ushort, Vector3>();
        var before = HordeRun(g, map, ref now, t0, 2.5, prev, me);
        if (!secondWind)
        {
            // The raw one-tick case: the run's last player not standing is the game over (Second Wind aside), which stops
            // the horde with the match. It can't happen for one tick in play: Alive goes false only by bleeding out.
            me.Alive = false;
            HordeRun(g, map, ref now, t0, World.TickDt, prev, me);
            me.Alive = true;
            var over = g.Phase != MatchPhase.Playing;
            Line($"horde respawn, solo, Alive false for one tick: {before.Mean:0.0} moving before; the match {(over ? "ended (game over: nobody standing)" : "went on")}");
            if (!over)
                Fail("a solo run with nobody standing (and no Second Wind) should be over");
            return;
        }
        g.ZP(me).Perks |= (ushort)(1 << g.ZRules.PerkIndex("secondwind"));
        g.SelfTestDown(me, now);
        var down = HordeRun(g, map, ref now, t0, g.ZRules.SelfReviveSeconds - 0.5, prev, me);
        var playing = g.Phase == MatchPhase.Playing;
        HordeRun(g, map, ref now, t0, 1.0, prev, me); // (back up during this)
        var standing = g.IsStanding(me);
        var after2 = HordeRun(g, map, ref now, t0, 2.5, prev, me);
        var slot = g.HordeAI.SlotOf(me.Id);
        // (inside: a zombie outside on its way to a window has no target until it climbs in, and a fresh pack of
        // skitterers comes out four at once; ZombieTypes.cs)
        var onMe = g.Horde.Count(z => z.Alive && z.Window < 0 && z.Target == me.Id);
        var alive = g.Horde.Count(z => z.Alive && z.Window < 0);
        Line($"horde respawn, solo, down with Second Wind: {before.Mean:0.0} moving before (fewest {before.Low:0.0} in half a second), {down.Mean:0.0} while down (waiting), "
             + $"back up {standing}, {after2.Mean:0.0} after (fewest {after2.Low:0.0}), {onMe} of {alive} inside after the player");
        if (!playing || !standing || slot < 0)
            Fail("Second Wind should keep a solo run going and get the player back up");
        if (before.Mean < 15 || after2.Mean < before.Mean * 0.6 || after2.Low < 3 || onMe < alive - 2)
            Fail($"the horde should go after a player who got back up ({after2.Mean:0.0} moving after vs {before.Mean:0.0} before)");
    }

    /// <summary>
    /// Two players: the chased one goes down (the horde goes to the other), is revived (spreads again), goes down and
    /// bleeds out, comes back when the round ends (a new round's horde finds them), leaves and rejoins, and is missing
    /// for one tick. The horde never stalls.
    /// </summary>
    private static void HordeRespawnCoop(Map map, ZombiesMap zm)
    {
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var mate = g.SelfTestAddHuman();
        g.SelfTestSpawn(mate);
        mate.ImmuneUntil = 1e9;
        var t0 = now;
        me.Teleport(HordeLoop(map, 0, 0));
        mate.Teleport(HordeLoop(map, 0, MathF.PI));
        HordePlace(g, map, zm, 40, now, 777u, _ => zm.AreaIndex("Arena"));
        g.SelfTestBeginRound(10, now);
        var prev = new Dictionary<ushort, Vector3>();
        var results = new List<string>();
        var bad = new List<string>();
        var before = HordeRun(g, map, ref now, t0, 2.5, prev, me, mate);
        results.Add($"before {before.Mean:0.0} (fewest {before.Low:0.0})");
        if (before.Mean < 15)
            bad.Add("before");

        void Step(string name, double seconds, Func<bool>? onMeOk = null)
        {
            var r = HordeRun(g, map, ref now, t0, seconds, prev, me, mate);
            var alive = g.Horde.Count(z => z.Alive && z.Window < 0); // (inside: a fresh spawn outside has no target until it tears in, and a pack brings four at once; ZombieTypes.cs)
            var targeted = g.Horde.Count(z => z.Alive && z.Window < 0 && z.Target != byte.MaxValue);
            results.Add($"{name} {r.Mean:0.0} (fewest {r.Low:0.0}; {targeted} of {alive} inside with a target)");
            if (r.Mean < before.Mean * 0.6 || r.Low < 3 || targeted < alive - 2 || g.Phase != MatchPhase.Playing || (onMeOk != null && !onMeOk()))
                bad.Add(name);
        }

        // The host (chased by about half the horde) goes down: everyone goes for the mate; revived: they spread again.
        g.SelfTestDown(me, now);
        Step("host down", 2, () => g.Horde.All(z => !z.Alive || z.Target != me.Id));
        g.SelfTestRevive(me, now);
        Step("revived", 2, () => g.Horde.Count(z => z.Alive && z.Target == me.Id) >= 5);
        // Down again and bleeds out (Alive false for the rest of the round).
        g.SelfTestDown(me, now);
        g.ZP(me).BleedAt = now + 0.5;
        Step("bled out", 2, () => !me.Alive && g.Horde.All(z => !z.Alive || z.Target != me.Id));
        // The round ends (its zombies all dead) and the host is back; the next round's horde comes in and finds both.
        g.SelfTestEndRound(now);
        g.SelfTestRespawn(me, now);
        me.ImmuneUntil = 1e9;
        g.SelfTestBeginRound(11, now);
        HordeRun(g, map, ref now, t0, 4, prev, me, mate); // (spawns come in through the windows)
        HordePlace(g, map, zm, 30, now, 778u, _ => zm.AreaIndex("Arena"));
        Step("next round", 2, () => g.Horde.Count(z => z.Alive && z.Target == me.Id) >= 5);
        // The mate leaves (everyone goes for the host) and rejoins.
        var mateId = mate.Id;
        g.SelfTestRemovePlayer(mate);
        Step("mate left", 2, () => g.Horde.All(z => !z.Alive || z.Target != mateId));
        mate = g.SelfTestAddHuman();
        g.SelfTestSpawn(mate);
        mate.ImmuneUntil = 1e9;
        Step("mate back", 2, () => g.Horde.Count(z => z.Alive && z.Target == mate.Id) >= 5);
        // The raw case: the host not alive for one tick (the mate still standing).
        me.Alive = false;
        HordeRun(g, map, ref now, t0, World.TickDt, prev, mate);
        me.Alive = true;
        Step("host missing a tick", 2, () => g.Horde.Count(z => z.Alive && z.Target == me.Id) >= 5);
        Line($"horde respawn, two players, zombies moving a tick: {string.Join("; ", results)}");
        if (bad.Count > 0)
            Fail($"the horde should go on after a player goes down, bleeds out, respawns, leaves or rejoins (stalled or lost its targets: {string.Join(", ", bad)})");
    }
}
