using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Mass horde" (docs/zombies/MASS-HORDE.md, the spike): Mass Horde Test's crowd of 5,000, 10,000 and
/// 20,000 zombies for 4 seconds each (short and seeded): the server tick's CPU mean and worst (Windows thread cycles:
/// 10,000 must stay under the 8 ms budget), allocations, memory, Msg.ZCrowd's bytes per client (per-cell
/// counts and headings) read into a client, promotion and demotion (the player walks off: far individuals go back to the crowd, near particles
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
        MassLine("table (crowd | server tick ms CPU mean / worst (Windows) | crowd ms | ZCrowd KB/s per client | MB): " + string.Join("; ", rows));
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
        // The network (Msg.ZCrowd, protocol 71): what one client at the player gets, recorded to replay into a client.
        var (cols, rows) = g.Crowd.DensitySize(g.ZRules.Crowd.NetCell);
        var view = new Game.CrowdNetView();
        var cw = new PacketWriter(32 * 1024);
        var packets = new List<byte[]>();
        long netBytes = 0;
        var keyframe = 0;
        var biggest = 0;
        double netMs = 0;
        var netSends = 0;
        long netAlloc = 0;
        var lastSum = 0;
        var serverAtSend = 0;
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
            if (g.ServerTick % g.ZRules.Crowd.NetEvery == 0)
            {
                var b0 = GC.GetAllocatedBytesForCurrentThread();
                var n0 = ThreadCpu.NowMs();
                g.CrowdCount();
                g.WriteZCrowdFor(cw, view, me.Position);
                netMs += ThreadCpu.NowMs() - n0;
                netSends++;
                if (packets.Count > 0)
                    netAlloc += GC.GetAllocatedBytesForCurrentThread() - b0; // (the first: the view's arrays)
                if (packets.Count == 0)
                    keyframe = cw.Length;
                else if (measure)
                {
                    netBytes += cw.Length;
                    biggest = Math.Max(biggest, cw.Length);
                }
                packets.Add(cw.Span.ToArray());
                lastSum = 0;
                foreach (var c in view.Count)
                    lastSum += c;
                serverAtSend = g.Crowd.Count;
            }
        }
        // 1 s warm-up (the crowd closes in, the first promotions, JIT), then 3 s measured.
        for (var t = 0; t < 60; t++)
            Tick(t, false);
        for (var t = 60; t < 240; t++)
            Tick(t, true);
        var promoted = g.Crowd.Promoted;
        var demoted0 = g.Crowd.Demoted;
        var near = g.Crowd.Near;
        var alive = g.AliveZombies;
        var crowdMs = g.Crowd.TickMs / Math.Max(1, g.Crowd.Ticks);
        var flow = $"{g.Crowd.FlowBuilds} flow fields, {g.Crowd.FlowMs / Math.Max(1, g.Crowd.FlowBuilds):0.00} ms each over several ticks (the most in one tick {g.Crowd.FlowWorstMs:0.00} ms)";
        var meanCpu = sumCpu / Math.Max(1, ticks);
        var kbs = netBytes / 3.0 / 1024;
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
        // The client: every message read in order; its picture must hold what the server's record says it was sent.
        var t1 = now;
        using var c = ZGame(0, ref t1, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var readMs = ThreadCpu.TimeMs(() =>
        {
            foreach (var pk in packets)
                c.SelfTestReadCrowd(pk);
        }) / Math.Max(1, packets.Count);
        var seen = c.CrowdSeen.Total;
        // Filling the picture (every crowd zombie's spot and yaw for drawing), once: the client's cost a frame.
        var fillMs = ThreadCpu.TimeMs(() =>
        {
            var pic = c.CrowdSeen;
            var sum = 0f;
            for (var i = 0; i < pic.Count.Length; i++)
                for (var k = 0; k < pic.Count[i]; k++)
                    sum += pic.Place(i, k, now, 1.5f, out _).X;
            if (sum == 12345.678f)
                Line("(never)");
        });
        World.SetMap(map);
        var net = $"ZCrowd {kbs:0.0} KB/s per client ({netSends} sends, every {g.ZRules.Crowd.NetEvery} ticks, {g.ZRules.Crowd.NetCell:0} m cells {cols}x{rows}; keyframe {keyframe} bytes, biggest change {biggest} bytes; "
                  + $"server {netMs / Math.Max(1, netSends):0.000} ms a send, {netAlloc} bytes allocated); client read {readMs:0.000} ms a message, picture {seen} zombies (sent {lastSum}), fill {fillMs:0.00} ms";
        if (seen != lastSum || c.CrowdSeen.Reads != packets.Count)
            Fail($"crowd {count}: the client's picture should match what it was sent ({seen} vs {lastSum}, {c.CrowdSeen.Reads} of {packets.Count} messages read)");
        if (netAlloc > 0)
            Fail($"crowd {count}: writing Msg.ZCrowd allocated {netAlloc} bytes (want none)");
        if (Math.Abs(lastSum - serverAtSend) > serverAtSend / 50 + 50) // (far cells lag a send or two)
            Fail($"crowd {count}: the last ZCrowd sent {lastSum} crowd zombies, the server had {serverAtSend}");
        MassLine($"crowd {count}: seeded {seeded} ({bytes / 1024} KB of crowd arrays, {mem / 1024 / 1024.0:0.0} MB managed for the game and crowd); server tick {meanCpu:0.00} ms CPU mean, worst {worstCpu:0.00} ms (Windows thread cycles: {cpuTimer.WorstText}), crowd {crowdMs:0.00} ms a tick, {flow}; "
              + $"{alloc} bytes allocated in the steady state; individuals {alive} (most {maxAlive}, cap {g.CrowdIndividualCap}), {near} held near, {promoted} promoted, {demoted0} demoted, {g.CrowdRoundKills} killed ({kills} by the test's shots), counts off on {bad} ticks{(badText.Length > 0 ? " (" + badText + ")" : "")}; "
              + $"{net}{walk}");
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
