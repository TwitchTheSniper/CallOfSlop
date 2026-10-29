using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--crowd-bench [sizes]</c> (docs/zombies/MASS-HORDE.md, "Measurements"): headless, the CPU cost of a frame's crowd
/// preparation on Horde Stress, as the host and as a client, from the player's eye on the Overlook with the real view
/// frustum and fog, at each size (25,000, 50,000 and 100,000 unless named): while the hordes stage on their pads (every
/// zombie in the mass tier, the worst case for a client) and after a few seconds of the descent. Writes
/// <c>crowdbench.log</c> next to the self-test log; nothing else is touched (the self-test's throwaway save folder).
/// </summary>
public static partial class SelfTest
{
    public static int CrowdBench(string[] args)
    {
        var arg = args.SkipWhile(a => !a.Equals("--crowd-bench", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        var sizes = arg?.Split(',').Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).ToArray() is { Length: > 0 } list ? list : [25000, 50000, 100000];
        Brutal.GlfwApi.Glfw.Init(); // (the clock only)
        SavePaths.UseFolder(SelfTestSaveFolder, "self-test");
        BlueprintStore.ReadOnly = true;
        NavBake.Enabled = false;
        MapBake.Enabled = false;
        MapBake.CacheWrites = false;
        using (_log = new StreamWriter(AppFolders.LogPath("crowdbench.log"), false))
        {
            Line($"Call of Slop crowd bench {DateTime.Now}: {Environment.ProcessorCount} logical processors, {ThreadCpu.Kind}");
            if (Maps.Find("Horde Stress") is not { Zombies.Stress: not null } map)
            {
                Line("no Horde Stress map");
                return 1;
            }
            foreach (var size in sizes)
                CrowdBenchSize(map, size);
            Line("DONE");
        }
        return 0;
    }

    private static void CrowdBenchSize(Map map, int size)
    {
        var now = 0.0;
        using var g = StressGame(map, ref now, size);
        var me = g.Local!;
        var eye = me.Position + new Vector3(0, 1.6f, 0);
        var view = Matrix4x4.CreateLookAt(eye, new Vector3(60, -4, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.1f, 16f / 9f, 0.05f, MathF.Max(map.FarPlane, 900));
        var fog = new Vector2(map.FogStart, map.FogEnd);
        var t1 = now;
        using var c = ZGame(0, ref t1, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var cw = new PacketWriter(32 * 1024);
        var cview = new Game.CrowdNetView();
        void Sync()
        {
            g.CrowdCount();
            var sends = 0;
            do
            {
                g.WriteZCrowdFor(cw, cview, me.Position);
                c.SelfTestReadCrowd(cw.Span);
                sends++;
            }
            while ((sends < g.ZRules.Crowd.NetFarEvery || cw.Length >= Game.ZCrowdMaxBytes - 64) && sends < 64);
        }
        void Measure(string moment)
        {
            // The whole game update (every server tick in it), as the main loop runs it: its allocations.
            for (var k = 0; k < 10; k++)
                g.Update(now += 1 / 60.0, 1 / 60f);
            var u0 = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 60; k++)
                g.Update(now += 1 / 60.0, 1 / 60f);
            Line($"crowd {size} {moment}: Game.Update allocated {(GC.GetAllocatedBytesForCurrentThread() - u0) / 60} B a frame over 60 frames");
            Sync();
            ZombieAnim.SetFrustum(view * proj);
            var r = BenchPrep(g, c, eye, fog, now);
            ZombieAnim.ClearFrustum();
            Line($"crowd {size} {moment}: particles {g.Crowd.Count}, mass {g.Crowd.MassTotal}, picture {c.CrowdSeen.Total}; {r}");
        }
        Measure("staging");
        // The descent: the hold lifted, a few seconds at the stress round's speed.
        var ticks = 0;
        while (g.StressState == Game.StressPhase.Staging && ticks++ < 60 * 30)
            ZTicks(g, ref now, World.TickDt);
        ZTicks(g, ref now, 8);
        Measure("descending");
    }

    /// <summary>A frame's preparation, host and client, timed over a few frames (wall clock: every thread working), and its allocations.</summary>
    private static string BenchPrep(Game host, Game client, Vector3 cam, Vector2 fog, double now)
    {
        const int frames = 30;
        string Run(Game g, string who)
        {
            var view = new CrowdView();
            var t = now;
            void Frame()
            {
                t += 1 / 60.0;
                view.Prepare(g, cam, t, fog.Y > fog.X ? fog.Y : 0f);
            }
            for (var k = 0; k < 30; k++)
                Frame(); // (warm: the agents filled, arrays grown, JIT)
            var a0 = GC.GetTotalAllocatedBytes(true);
            var cpu0 = ThreadCpu.NowMs();
            var sw = Stopwatch.StartNew();
            var worst = 0.0;
            for (var k = 0; k < frames; k++)
            {
                Frame();
                worst = Math.Max(worst, view.PrepMs);
            }
            var wall = sw.Elapsed.TotalMilliseconds / frames;
            var cpu = (ThreadCpu.NowMs() - cpu0) / frames;
            var alloc = (GC.GetTotalAllocatedBytes(true) - a0) / frames;
            var i = view.Inst;
            var a = view.Agents;
            return $"{who} {wall:0.00} ms wall (worst {worst:0.00}), {cpu:0.00} ms main-thread CPU, {i.Count[0]}/{i.Count[1]}/{i.Count[2]} near/mid/far ({i.Particles} particles, {i.Agents} agents of {a.Count}), "
                   + $"cells {a.CellsDrawn} drawn, {a.CellsCulled} out of view, {a.CellsFogged} fogged, {a.CellsHidden} behind the ground, {alloc} B/frame";
        }
        return Run(host, "host") + "; " + Run(client, "client");
    }
}
