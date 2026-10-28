using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Horde stress" (docs/zombies/MASS-HORDE.md, "Horde Stress"; Dev/HordeStress.json). Short and
/// seeded, at a small size (600, the crowd walking six times as fast so the whole descent fits in about a minute of
/// game time, the zombies tick alone): the three hordes seeded on their staging pads and held through the countdown,
/// descending, merging at the rally point, advancing on the player on the Overlook and coming out as full zombies
/// standing on the terrain; the counts adding up every tick; an endless wave; no allocation in the steady state. The map's own contract (loads, probes, nav) is its test block ("Map contract").
/// The big sizes are the developer's to run.
/// </summary>
public static partial class SelfTest
{
    private static void CheckHordeStress()
    {
        if (Maps.Find("Horde Stress") is not { Zombies: { Stress: { } st } zm, Terrain: { } terrain } map)
        {
            Fail("no Horde Stress map (with terrain and a zombies \"stress\" section)");
            return;
        }
        Line($"Horde Stress: {st.Staging.Count} staging areas, rally ({st.Rally.X:0}, {st.Rally.Y:0}), countdown {st.Countdown:0} s, default size {zm.Crowd}");
        if (st.Staging.Count != 3)
            Fail("Horde Stress should have three staging areas");
        var now = 0.0;
        var (countdown, waves) = (st.Countdown, st.WaveSeconds);
        st.Countdown = 2; // (shorter for the test; put back below)
        try
        {
            StressRun(map, st, terrain, ref now);
            StressEndlessWave(map, st, ref now);
        }
        finally
        {
            (st.Countdown, st.WaveSeconds) = (countdown, waves);
        }
    }

    private static Game StressGame(Map map, ref double now, int size, bool endless = false)
    {
        var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map, setup: g =>
        {
            g.StressSize = size;
            g.StressEndless = endless;
            g.StressSpeedScale = 6f;
        });
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(new Vector3(-175, map.GroundHeight(-175, 0) + 0.05f, 0)); // (the Overlook)
        g.SelfTestBeginRound(1, now);
        return g;
    }

    private static void StressRun(Map map, ZombiesStress st, TerrainData terrain, ref double clock)
    {
        var now = clock;
        const int size = 600;
        using var g = StressGame(map, ref now, size);
        var c = g.Crowd;
        int NearAt(Vector2 at, float r)
        {
            var n = 0;
            for (var k = 0; k < c.MassCount.Length; k++)
                if (c.MassCount[k] > 0 && Vector2.Distance(new Vector2(c.MassCentre(k).X, c.MassCentre(k).Z), at) <= r)
                    n += c.MassCount[k];
            for (var i = 0; i < c.Count; i++)
                if (Vector2.Distance(new Vector2(c.X[i], c.Z[i]), at) <= r)
                    n++;
            return n;
        }
        var perPad = st.Staging.Select(s => NearAt(s, 30)).ToArray();
        Line($"seeded: {c.Total} (mass {c.MassTotal}), per staging pad {string.Join("/", perPad)}, phase {g.StressState}, held {c.Hold}");
        if (c.Total != size || perPad.Any(n => n != size / 3) || g.StressState != Game.StressPhase.Staging || !c.Hold || !c.HasGoal)
            Fail($"the stress round should seed {size} zombies, {size / 3} on each staging pad, held, with the rally point as the crowd's goal");

        var bad = 0;
        var badText = "";
        void Ticks(double seconds)
        {
            for (var t = 0; t < (int)(seconds * World.TickRate); t++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                var sum = c.Total + g.AliveZombies + g.CrowdRoundKills;
                if (sum != g.CrowdRoundTotal && bad++ == 0)
                    badText = $"crowd {c.Count} + mass {c.MassTotal} + alive {g.AliveZombies} + killed {g.CrowdRoundKills} != {g.CrowdRoundTotal}";
            }
        }
        Ticks(1.5);
        var stillThere = st.Staging.Sum(s => NearAt(s, 30));
        if (stillThere != size || g.StressState != Game.StressPhase.Staging)
            Fail($"the hordes should wait on their pads through the countdown ({stillThere} of {size} still there after 1.5 s)");

        // The descent: the pads empty, the hordes meet at the rally point.
        var merged = -1.0;
        var t0 = now;
        long descentAlloc = -1;
        for (var s = 0; s < 120 && merged < 0; s++)
        {
            if (s == 4)
            {
                // Allocation in the steady state: the descent (the phases settled, nobody near the player yet).
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                Ticks(0.5);
                descentAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                continue;
            }
            Ticks(0.5);
            if (g.StressState == Game.StressPhase.Advance)
                merged = now - t0;
        }
        var left = st.Staging.Sum(s => NearAt(s, 30));
        Line($"merged after {merged:0.0} s (game time from the countdown's start, 6x speed): {g.StressNearShare * 100:0}% in the blob at the rally point, {left} left on the pads, merge {g.StressMergeSeconds:0.0} s");
        if (merged < 0 || g.StressNearShare < st.Gather || left > size / 10)
            Fail("the three hordes should descend from their pads and merge at the rally point within a minute (at 6x speed)");

        // The advance: up the west slope to the Overlook; near the player they come out as full zombies, on the ground.
        var me = g.Local!;
        float MeanDist()
        {
            double sum = 0;
            var n = 0;
            for (var k = 0; k < c.MassCount.Length; k++)
                if (c.MassCount[k] > 0)
                {
                    var m = c.MassCentre(k);
                    sum += c.MassCount[k] * Vector2.Distance(new Vector2(m.X, m.Z), new Vector2(me.Position.X, me.Position.Z));
                    n += c.MassCount[k];
                }
            for (var i = 0; i < c.Count; i++)
            {
                sum += Vector2.Distance(new Vector2(c.X[i], c.Z[i]), new Vector2(me.Position.X, me.Position.Z));
                n++;
            }
            return n > 0 ? (float)(sum / n) : 0;
        }
        var d0 = MeanDist();
        var promoted0 = c.Promoted;
        var worstOff = 0f;
        for (var s = 0; s < 120 && c.Promoted - promoted0 < 20; s++)
        {
            Ticks(0.5);
            foreach (var z in g.Horde)
                if (z.Alive)
                    worstOff = MathF.Max(worstOff, MathF.Abs(z.Position.Y - terrain.Sample(z.Position.X, z.Position.Z)));
        }
        var d1 = MeanDist();
        Line($"advance: mean distance to the player {d0:0} m -> {d1:0} m, promoted {c.Promoted - promoted0}, individuals {g.AliveZombies} at most {worstOff:0.00} m off the ground, "
             + $"allocated {descentAlloc} B over 0.5 s of the descent; conservation broken {bad} times {badText}");
        if (c.Promoted - promoted0 < 20 || d1 > d0 - 50)
            Fail("the merged horde should advance on the player on the Overlook and come out as full zombies there");
        if (worstOff > 1.5f)
            Fail($"promoted zombies should stand on the terrain (one was {worstOff:0.00} m off it)");
        if (bad > 0)
            Fail($"the counts should add up on every tick: {badText}");
        if (descentAlloc != 0)
            Fail($"a stress round's steady ticks should allocate nothing ({descentAlloc} B over 0.5 s of the descent)");

        g.StressSummary("self-test");
        clock = now;
    }

    private static void StressEndlessWave(Map map, ZombiesStress st, ref double now)
    {
        const int size = 300;
        st.WaveSeconds = 3;
        using var g = StressGame(map, ref now, size, endless: true);
        var total0 = g.CrowdRoundTotal;
        for (var t = 0; t < (int)((st.Countdown + st.WaveSeconds + 0.5) * World.TickRate); t++)
            g.SelfTestZombiesTick(now += World.TickDt);
        Line($"endless: {total0} -> {g.CrowdRoundTotal} in the round after {st.Countdown + st.WaveSeconds + 0.5:0.0} s, wave {g.StressWave}, live {g.StressLive}");
        if (g.CrowdRoundTotal != total0 + size || g.StressWave != 2 || g.StressLive + g.CrowdRoundKills != g.CrowdRoundTotal)
            Fail("endless waves should add another horde of the same size from the staging areas");
        g.StressSummary("self-test");
    }
}
