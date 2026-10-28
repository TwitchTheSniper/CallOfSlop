using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Mass horde" (docs/zombies/MASS-HORDE.md, the spike): Mass Horde Test's crowd of 5,000, 10,000 and
/// 20,000 zombies for 4 seconds each (short and seeded): the server tick's CPU mean and worst (Windows thread cycles:
/// 10,000 must stay under the 8 ms budget), allocations, memory, the network sketch's bytes per client (per-cell
/// density deltas), promotion and demotion (the player walks off: far individuals go back to the crowd, near particles
/// come out), counts adding up every tick (crowd + alive + killed = the round's total) with kills from shots, the
/// same seed giving the same crowd, and the crowd tier off on every other map.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> MassNotes = new();

    private static void MassLine(string s)
    {
        Line(s);
        MassNotes.Add(s);
    }

    private static void CheckMassHorde()
    {
        MassNotes.Clear();
        var now = 0.0;
        if (Maps.Find("Mass Horde Test") is not { Zombies: { Crowd: > 0 } zm } map)
        {
            Fail("no Mass Horde Test map (or no \"crowd\" in its zombies section)");
            return;
        }
        MassOff(ref now);
        var rows = new List<string>();
        foreach (var n in new[] { 5000, 10000, 20000 })
            rows.Add(MassMeasure(map, zm, n, ref now));
        MassLine("table (crowd | server tick ms CPU mean / worst (Windows) | crowd ms | KB/s per client (sketch) | MB): " + string.Join("; ", rows));
        MassDeterminism(map, ref now);
        Line("::warning title=Mass horde (results)::" + string.Join("%0A", MassNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>Every other map: the crowd tier never runs (and the round flow is the old one).</summary>
    private static void MassOff(ref double now)
    {
        if (Maps.Find("Blackmoor Asylum") is not { Zombies: { } zm } map)
            return;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        g.SelfTestBeginRound(5, now);
        var total = g.SelfTestRoundTotal;
        ZTicks(g, ref now, 2.0);
        MassLine($"off: Blackmoor round 5: crowd on {g.CrowdOn}, crowd ticks {g.Crowd.Ticks}, particles {g.Crowd.Count}, round total {total}, {g.AliveZombies} alive, {g.SelfTestToSpawn} to spawn");
        if (g.CrowdOn || g.Crowd.Ticks != 0 || g.Crowd.Count != 0 || g.AliveZombies == 0 || g.SelfTestRoundTotal != total)
            Fail("the crowd tier should stay off on maps without \"crowd\" (normal spawning, no crowd ticks)");
    }

    private static string MassMeasure(Map map, ZombiesMap zm, int count, ref double clock)
    {
        var now = clock;
        var mem0 = GC.GetTotalMemory(true);
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(new Vector3(0, 0, 0));
        g.CrowdOverride = count;
        g.SelfTestBeginRound(1, now);
        var seeded = g.Crowd.Count;
        var mem = GC.GetTotalMemory(true) - mem0;
        var cpuTimer = new TickCpu();
        double sumCpu = 0, worstCpu = 0;
        var ticks = 0;
        var kills = 0;
        var bad = 0;
        var badText = "";
        long alloc = 0;
        var maxAlive = 0;
        // The network sketch: per-cell counts (4 m cells) every snapshot, sent as the cells that changed.
        const float cell = 4f;
        var (cols, rows) = g.Crowd.DensitySize(cell);
        var dens = new ushort[cols * rows];
        var prev = new ushort[cols * rows];
        long sketchBytes = 0;
        var sketchSnaps = 0;
        var keyframe = 0;
        void Check(string when)
        {
            var sum = g.Crowd.Count + g.AliveZombies + g.CrowdRoundKills;
            if (sum != g.CrowdRoundTotal || g.CrowdRoundKills < kills)
            {
                bad++;
                if (badText.Length == 0)
                    badText = $"{when}: crowd {g.Crowd.Count} + alive {g.AliveZombies} + killed {g.CrowdRoundKills} (the test shot {kills}) != {g.CrowdRoundTotal}";
            }
        }
        void Tick(int t, bool measure)
        {
            now += World.TickDt;
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            cpuTimer.Start();
            var c0 = ThreadCpu.NowMs();
            var t0 = Stopwatch.GetTimestamp();
            g.SelfTestZombiesTick(now);
            var wall = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            var cpu = ThreadCpu.NowMs() - c0;
            cpuTimer.Stop(wall, measure);
            if (measure)
            {
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                sumCpu += cpu;
                worstCpu = Math.Max(worstCpu, cpu);
                ticks++;
            }
            maxAlive = Math.Max(maxAlive, g.AliveZombies);
            Check($"tick {t}");
            // Shots: every half second the nearest few individuals die (kill credit as ever).
            if (t % 30 == 15)
            {
                var shot = 0;
                foreach (var z in g.Horde)
                    if (z.Alive && shot < 4 && ZFlat(z.Position - me.Position) < 25f)
                    {
                        g.DamageZombie(me, z, 1_000_000, true, false, now);
                        kills++;
                        shot++;
                    }
                Check($"after shots at tick {t}");
            }
            if (g.ServerTick % World.SnapshotEvery == 0 && measure)
            {
                g.Crowd.Density(cell, dens);
                var changed = 0;
                var nonzero = 0;
                for (var i = 0; i < dens.Length; i++)
                {
                    if (dens[i] != prev[i])
                        changed++;
                    if (dens[i] != 0)
                        nonzero++;
                }
                // A changed cell: its index as a gap from the last (a varint, 1 byte mostly) and the new count's delta (1 byte); 4 bytes of header.
                sketchBytes += 4 + changed * 2;
                sketchSnaps++;
                keyframe = Math.Max(keyframe, 4 + nonzero * 3);
                Array.Copy(dens, prev, dens.Length);
            }
        }
        // 1 s warm-up (the crowd closes in, the first promotions, JIT), then 3 s measured.
        for (var t = 0; t < 60; t++)
            Tick(t, false);
        g.Crowd.Density(cell, prev);
        for (var t = 60; t < 240; t++)
            Tick(t, true);
        var promoted = g.Crowd.Promoted;
        var demoted0 = g.Crowd.Demoted;
        var near = g.Crowd.Near;
        var alive = g.AliveZombies;
        var crowdMs = g.Crowd.TickMs / Math.Max(1, g.Crowd.Ticks);
        var flow = $"{g.Crowd.FlowBuilds} flow builds, {g.Crowd.FlowMs / Math.Max(1, g.Crowd.FlowBuilds):0.00} ms each";
        var meanCpu = sumCpu / Math.Max(1, ticks);
        var kbs = sketchBytes / 3.0 / 1024;
        var bytes = g.Crowd.Bytes;
        // Steady state: a one-off growth (the zombie body grid's first build when the first ones arrive) can't fail it.
        alloc = RecheckAllocBytes(alloc, () =>
        {
            for (var k = 0; k < 60; k++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
            }
        });

        // The player walks off (10,000 only): the individuals left behind go back to the crowd, near particles come out.
        var walk = "";
        if (count == 10000)
        {
            me.Teleport(new Vector3(60, 0, -15));
            var d0 = g.Crowd.Demoted;
            var p0 = g.Crowd.Promoted;
            for (var t = 240; t < 240 + 60 * 4; t++)
                Tick(t, false);
            var farLeft = g.Horde.Count(z => z.Alive && ZFlat(z.Position - me.Position) > g.Crowd.Tuning.DemoteRange + 10);
            walk = $"; walked 60 m off: {g.Crowd.Demoted - d0} demoted, {g.Crowd.Promoted - p0} promoted in 4 s, {farLeft} individuals still over {g.Crowd.Tuning.DemoteRange + 10:0} m away, {g.AliveZombies} alive";
            if (g.Crowd.Demoted - d0 < 20 || g.Crowd.Promoted - p0 < 20)
                Fail($"crowd {count}: walking off should send the far individuals back to the crowd and bring near particles out ({walk})");
        }
        MassLine($"crowd {count}: seeded {seeded} ({bytes / 1024} KB of crowd arrays, {mem / 1024 / 1024.0:0.0} MB managed for the game and crowd); server tick {meanCpu:0.00} ms CPU mean, worst {worstCpu:0.00} ms (Windows thread cycles: {cpuTimer.WorstText}), crowd {crowdMs:0.00} ms a tick, {flow}; "
              + $"{alloc} bytes allocated in the steady state; individuals {alive} (most {maxAlive}, cap {g.CrowdIndividualCap}), {near} held near, {promoted} promoted, {demoted0} demoted, {g.CrowdRoundKills} killed ({kills} by the test's shots), counts off on {bad} ticks{(badText.Length > 0 ? " (" + badText + ")" : "")}; "
              + $"network sketch {kbs:0.0} KB/s per client (4 m cells, {cols}x{rows}; keyframe {keyframe} bytes){walk}");
        if (seeded != count)
            Fail($"crowd {count}: only {seeded} seeded");
        if (alloc > 0)
            Fail($"crowd {count}: {alloc} bytes allocated in the steady state (want none per tick)");
        if (bad > 0)
            Fail($"crowd {count}: the counts didn't add up on {bad} ticks: {badText}");
        if (promoted < count / 100 || maxAlive < count / 100)
            Fail($"crowd {count}: the crowd should reach the player and fill the individual tier ({promoted} promoted, at most {maxAlive} alive)");
        if (maxAlive > g.CrowdIndividualCap)
            Fail($"crowd {count}: {maxAlive} individuals, over the cap {g.CrowdIndividualCap}");
        if (TickCpu.Available && count <= 10000 && cpuTimer.WorstMs > MaxTickWorkMs)
            Fail($"crowd {count}: a server tick did {cpuTimer.WorstMs:0.00} ms of work (budget {MaxTickWorkMs} ms): {cpuTimer.WorstDetail}");
        clock = now;
        return $"{count} | {meanCpu:0.00} / {cpuTimer.WorstText} | {crowdMs:0.00} | {kbs:0.0} | {bytes / 1024 / 1024.0:0.0}";
    }

    /// <summary>The same seed, the same crowd: two runs of 2 s with 3,000 particles end with the same digest.</summary>
    private static void MassDeterminism(Map map, ref double now)
    {
        ulong Run1(ref double t0)
        {
            using var g = ZGame(0, ref t0, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            g.Local!.ImmuneUntil = 1e9;
            g.Local.Teleport(Vector3.Zero);
            g.CrowdOverride = 3000;
            g.SelfTestBeginRound(1, t0);
            var t = t0;
            for (var k = 0; k < 120; k++)
                g.SelfTestZombiesTick(t += World.TickDt);
            t0 = t;
            var h = g.Crowd.Digest();
            foreach (var z in g.Horde)
                h = (h ^ BitConverter.SingleToUInt32Bits(z.Position.X) ^ ((ulong)z.Id << 32)) * 1099511628211UL;
            return h;
        }
        var a = Run1(ref now);
        var b = Run1(ref now);
        MassLine($"determinism: 3,000 for 2 s twice: {a:x16} / {b:x16}");
        if (a != b)
            Fail("the crowd should replay exactly from the same seed");
    }
}
