using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie hordes" (docs/zombies/ZOMBIES-MODE.md, "Hordes" and "Horde controller"): scripted hordes of
/// 24, 100 and 300 zombies on Blackmoor Asylum: server tick cost and allocations, snapshot bytes per client, the
/// client's update and drawing cost.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombieHordes()
    {
        var now = 0.0;
        if (Maps.Find("Blackmoor Asylum") is not { Zombies: { } zm } map)
        {
            Fail("no Blackmoor Asylum map");
            return;
        }
        foreach (var n in new[] { 24, 100, 300 })
            HordeMeasure(map, zm, n, ref now);
    }

    /// <summary>A scripted horde of <paramref name="count"/> chasing the immune host through every open door: the costs.</summary>
    private static void HordeMeasure(Map map, ZombiesMap zm, int count, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        for (var d = 0; d < zm.Doors.Count; d++)
            g.OpenDoor(d, null);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
        var rng = 12345u;
        float R()
        {
            rng ^= rng << 13;
            rng ^= rng >> 17;
            rng ^= rng << 5;
            return (rng >> 8) / 16777216f;
        }
        for (var i = 0; i < count; i++)
        {
            var ai = i % zm.Areas.Count;
            var a = zm.Areas[ai];
            Vector3 p = default;
            var ok = false;
            for (var k = 0; k < 20 && !ok; k++)
            {
                p = new Vector3(a.X0 + 0.7f + R() * (a.X1 - a.X0 - 1.4f), 0, a.Z0 + 0.7f + R() * (a.Z1 - a.Z0 - 1.4f));
                p.Y = map.GroundHeight(p.X, p.Z);
                ok = a.Contains(p) && !World.Blocked(p + new Vector3(0, 0.05f, 0));
            }
            if (!ok)
                p = FreeSpot(map, a);
            ZPlace(g, zm, p, ai, (ZSpeed)(i % 3), now);
        }
        ZTicks(g, ref now, 1.0); // warm up
        g.ZAiMs = g.ZAiZombieTicks = g.ZAiTicks = 0;
        var ticks = 0;
        double sumMs = 0, worstMs = 0, sumCpu = 0;
        long bytes = 0;
        var w = new PacketWriter();
        var alloc0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 0; t < 60 * 3; t++)
        {
            now += World.TickDt;
            var c0 = ThreadCpu.NowMs();
            var t0 = Stopwatch.GetTimestamp();
            g.SelfTestZombiesTick(now);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            sumCpu += ThreadCpu.NowMs() - c0;
            sumMs += ms;
            worstMs = Math.Max(worstMs, ms);
            ticks++;
            if (g.ServerTick % World.SnapshotEvery == 0)
            {
                g.WriteZEnts(w);
                bytes += w.Length;
            }
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        var bytesPerSec = bytes / 3.0;

        // The client: reading the horde and posing every zombie.
        var ents = w.Span.ToArray();
        var w2 = new PacketWriter();
        g.WriteZWorld(w2);
        var world = w2.Span.ToArray();
        var hostNow = now;
        using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        World.SetMap(map);
        c.SelfTestReadZombies(ents, world);
        var rec = new DrawRecorder { Keep = false };
        var gfx = new Renderer { Recorder = rec };
        var readMs = ThreadCpu.TimeMs(() =>
        {
            for (var k = 0; k < 30; k++)
                c.SelfTestReadZombies(ents, world);
        }) / 30;
        void Pose()
        {
            foreach (var z in c.Horde)
            {
                c.ZombieAnimInputFor(z, hostNow, out var a);
                ZombieAnim.Targets(a, out var tg);
                ZombieAnim.Solve(a, tg, out var pose);
                ZombieAnim.Draw(gfx, pose, new Vector3(0.3f), new Vector3(0.4f, 0.5f, 0.3f), new Vector3(0.2f), false, false, true);
            }
        }
        Pose();
        rec.Reset();
        var poseMs = ThreadCpu.TimeMs(() =>
        {
            for (var k = 0; k < 10; k++)
                Pose();
        }) / 10;
        var draws = rec.Count / 10.0;
        ZLine($"horde {count}: {g.AliveZombies} alive, server tick {sumMs / ticks:0.00} ms mean ({sumCpu / ticks:0.00} ms CPU), worst {worstMs:0.00} ms, AI {g.ZAiMs / Math.Max(1, g.ZAiTicks):0.00} ms; "
              + $"{alloc / ticks} bytes allocated per tick; ZEnts {bytesPerSec / 1024:0.0} KB/s ({ents.Length} bytes, {c.Horde.Count} on the client); "
              + $"client read {readMs:0.000} ms, pose+draw {poseMs:0.00} ms ({draws:0} box draws)");
    }
}
