using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// The performance window (Source/Perf, Source/UI/UiPerf.cs): scopes nest as the area tree says, a scope closed out of
/// order is caught and the stack recovers, nothing is timed while off or on another thread, no allocations per frame
/// with timing on or off (scopes, render passes with the GPU timer, the frame end, the stats), the frame statistics
/// (min / avg / max / 1% low, worst frames), the GPU pass timer never reads a result before it's ready and makes no
/// queries while off, and the F9 key. Headless.
/// </summary>
public static partial class SelfTest
{
    /// <summary>A fake timestamp backend that counts its stamps; results are ready <see cref="Latency"/> frames later.</summary>
    private sealed class PerfFakeStamps(int count) : IGpuStamps
    {
        public ulong Now;
        public int Frame, Latency = 3, Stamps, Reads, EarlyReads;
        private readonly ulong[] _value = new ulong[count];
        private readonly int[] _at = Enumerable.Repeat(int.MaxValue, count).ToArray();

        public void Stamp(int index)
        {
            _value[index] = Now;
            _at[index] = Frame;
            Stamps++;
        }

        public bool Ready(int index) => Frame - _at[index] >= Latency;

        public ulong Read(int index)
        {
            Reads++;
            if (!Ready(index))
                EarlyReads++;
            return _value[index];
        }
    }

    private static void Spin(double us)
    {
        var until = Stopwatch.GetTimestamp() + (long)(us * Stopwatch.Frequency / 1e6);
        while (Stopwatch.GetTimestamp() < until)
        {
        }
    }

    /// <summary>One scripted frame touching most of the tree (the shape of the game's own frame).</summary>
    private static void PerfFrame(bool spin)
    {
        using (Perf.Measure(PerfArea.Events))
        {
        }
        using (Perf.Measure(PerfArea.Game))
        {
            using (Perf.Measure(PerfArea.NetReceive))
            {
            }
            for (var t = 0; t < 2; t++)
            {
                using var tick = Perf.Measure(PerfArea.ServerTick);
                using (Perf.Measure(PerfArea.Modes))
                using (Perf.Measure(PerfArea.Zombies))
                using (Perf.Measure(PerfArea.Horde))
                    if (spin)
                        Spin(50);
                using (Perf.Measure(PerfArea.Bots))
                    if (spin)
                        Spin(20);
                using (Perf.Measure(PerfArea.NetSend))
                {
                }
            }
            using (Perf.Measure(PerfArea.Visuals))
            {
            }
        }
        using (Perf.Measure(PerfArea.Render))
        {
            using (Perf.Pass(PerfArea.Buildings, PerfGpuPass.Buildings))
            {
            }
            using (Perf.Measure(PerfArea.Loading))
            {
            }
            using (Perf.Pass(PerfArea.Props, PerfGpuPass.Props))
                PerfCounters.Draw(36, 10);
            using (Perf.Pass(PerfArea.Sky, PerfGpuPass.Sky))
                PerfCounters.Draw(3);
            using (Perf.Pass(PerfArea.Hud, PerfGpuPass.Hud))
            {
            }
        }
        using (Perf.Measure(PerfArea.Ui))
            Perf.Gpu?.Mark(PerfGpuPass.Ui);
    }

    private static void CheckPerfWindow()
    {
        var savedOn = Perf.On;
        var savedGpu = Perf.Gpu;
        try
        {
            CheckPerfNesting();
            CheckPerfStats();
            CheckPerfGpu();
            CheckPerfAllocations();
            CheckPerfKey();
        }
        finally
        {
            Perf.Gpu = savedGpu;
            Perf.SetOn(savedOn);
            Perf.ResetForTest();
        }
    }

    private static void CheckPerfNesting()
    {
        // The tree itself: every name, every parent before its child, depths as the parents say.
        if (Perf.Names.Length != Perf.Count || PerfGpu.Names.Length != PerfGpu.Count)
            Fail($"perf: {Perf.Names.Length} names for {Perf.Count} areas, {PerfGpu.Names.Length} for {PerfGpu.Count} GPU passes");
        for (var a = 0; a < Perf.Count; a++)
            if (Perf.Parent[a] >= a)
                Fail($"perf: area {Perf.Names[a]} comes before its parent");

        Perf.Gpu = null;
        Perf.SetOn(true);
        Perf.ResetForTest();
        PerfFrame(spin: true);
        if (Perf.OpenScopes != 0 || Perf.Misnested != 0)
            Fail($"perf: {Perf.OpenScopes} scope(s) still open, {Perf.Misnested} misnested after a clean frame");
        var seen = 0;
        for (var a = 0; a < Perf.Count; a++)
        {
            if (Perf.SeenParent[a] == -2)
                continue;
            seen++;
            // Loading is a root that can open anywhere; everything else must open under its tree parent.
            if (a != (int)PerfArea.Loading && Perf.SeenParent[a] != Perf.Parent[a])
                Fail($"perf: {Perf.Names[a]} opened under {(Perf.SeenParent[a] < 0 ? "nothing" : Perf.Names[Perf.SeenParent[a]])}, the tree says {(Perf.Parent[a] < 0 ? "a root" : Perf.Names[Perf.Parent[a]])}");
        }
        // Inclusive times: a child never exceeds its parent; the spins land where they ran.
        for (var a = 0; a < Perf.Count; a++)
            if (Perf.Parent[a] >= 0 && a != (int)PerfArea.Loading && Perf.OpenFrameMs((PerfArea)a) > Perf.OpenFrameMs((PerfArea)Perf.Parent[a]) + 1e-9)
                Fail($"perf: {Perf.Names[a]} {Perf.OpenFrameMs((PerfArea)a):0.000} ms is more than its parent's {Perf.OpenFrameMs((PerfArea)Perf.Parent[a]):0.000} ms");
        var horde = Perf.OpenFrameMs(PerfArea.Horde);
        var ticks = Perf.OpenFrameCalls(PerfArea.ServerTick);
        if (horde < 0.09 || ticks != 2 || Perf.OpenFrameCalls(PerfArea.Horde) != 2)
            Fail($"perf: horde {horde:0.000} ms in {Perf.OpenFrameCalls(PerfArea.Horde)} scopes, {ticks} ticks (want >= 0.1 ms in 2, 2 ticks)");
        Perf.EndFrame(16f);
        if (Perf.TickWorstMs(0) <= 0 || Perf.Ticks(0) != 2 || Perf.AreaMs(0, PerfArea.Horde) < 0.09f || PerfCounters.DrawsAt(0) != 2 || PerfCounters.TrianglesAt(0) != 12 * 10 + 1)
            Fail($"perf: the frame's history: worst tick {Perf.TickWorstMs(0):0.000} ms, {Perf.Ticks(0)} ticks, horde {Perf.AreaMs(0, PerfArea.Horde):0.000} ms, {PerfCounters.DrawsAt(0)} draws, {PerfCounters.TrianglesAt(0)} tris");
        Line($"nesting: {seen} areas seen, each under its tree parent; horde {horde:0.000} ms in 2 ticks, worst tick {Perf.TickWorstMs(0):0.000} ms");

        // Out of order: the outer scope ends first. Caught, and the stack unwinds so the next frame is clean.
        Perf.ResetForTest();
        var outer = Perf.Measure(PerfArea.Game);
        var inner = Perf.Measure(PerfArea.Visuals);
        outer.Dispose();
        inner.Dispose();
        var caught = Perf.Misnested;
        var open = Perf.OpenScopes;
        var leftOpen = Perf.Measure(PerfArea.Render); // left open over the frame's end
        Perf.EndFrame(16f);
        var afterEnd = Perf.Misnested;
        PerfFrame(spin: false);
        if (caught != 2 || open != 0 || afterEnd != 3 || Perf.Misnested != 3 || Perf.OpenScopes != 0)
            Fail($"perf: out-of-order scopes: {caught} caught (want 2), {open} left open; a scope open over the frame end: misnested {afterEnd} (want 3); next frame {Perf.Misnested}, {Perf.OpenScopes} open");
        leftOpen.Dispose(); // (late: ignored, counted)

        // Off: nothing timed. Another thread: nothing timed, the main thread's stack untouched.
        Perf.ResetForTest();
        Perf.SetOn(false);
        PerfFrame(spin: false);
        var offCalls = Perf.OpenFrameCalls(PerfArea.Game);
        Perf.SetOn(true);
        var mainOpen = Perf.Measure(PerfArea.Game);
        Task.Run(() =>
        {
            using (Perf.Measure(PerfArea.Bots))
            {
            }
        }).Wait();
        mainOpen.Dispose();
        if (offCalls != 0 || Perf.OpenFrameCalls(PerfArea.Bots) != 0 || Perf.Misnested != 0 || Perf.OpenFrameCalls(PerfArea.Game) != 1)
            Fail($"perf: off timed {offCalls} scope(s); a worker thread's scope counted {Perf.OpenFrameCalls(PerfArea.Bots)}, misnested {Perf.Misnested}");

        // What a scope costs (logged, not checked: wall clock on a shared box).
        const int n = 200_000;
        var t0 = Stopwatch.GetTimestamp();
        for (var i = 0; i < n; i++)
            using (Perf.Measure(PerfArea.Bots))
            {
            }
        var onNs = (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / n;
        Perf.SetOn(false);
        t0 = Stopwatch.GetTimestamp();
        for (var i = 0; i < n; i++)
            using (Perf.Measure(PerfArea.Bots))
            {
            }
        var offNs = (Stopwatch.GetTimestamp() - t0) * 1e9 / Stopwatch.Frequency / n;
        Line($"a scope costs {onNs:0.0} ns timing, {offNs:0.00} ns off");
    }

    private static void CheckPerfStats()
    {
        Perf.SetOn(true);
        Perf.ResetForTest();
        // 200 frames: 199 at 10 ms and one 50 ms hitch 30 frames ago, and a 20 ms one 100 frames ago.
        for (var i = 0; i < 200; i++)
            Perf.EndFrame(i == 169 ? 50f : i == 99 ? 20f : 10f);
        var s = new PerfStats();
        s.Update();
        var avg = (198 * 10 + 50 + 20) / 200f;
        if (s.Frames != 200 || s.MinMs != 10f || s.MaxMs != 50f || MathF.Abs(s.AvgMs - avg) > 1e-3f || s.Low1Ms != 35f
            || s.WorstBack[0] != 30 || s.WorstMs[0] != 50f || s.WorstBack[1] != 100 || s.WorstMs[1] != 20f)
            Fail($"perf stats: {s.Frames} frames, min {s.MinMs} max {s.MaxMs} avg {s.AvgMs} (want {avg}), 1% low {s.Low1Ms} (want 35: the slowest 2 of 200), worst {s.WorstMs[0]} ms {s.WorstBack[0]} back (want 50, 30), next {s.WorstMs[1]} {s.WorstBack[1]} (want 20, 100)");
        if (s.RecentFrames != 96)
            Fail($"perf stats: {s.RecentFrames} frames in the last second (want 96: 95 at 10 ms and the 50 ms one)");
        // Pause freezes the history.
        Perf.Paused = true;
        Perf.EndFrame(99f);
        Perf.Paused = false;
        if (Perf.FrameMs(0) != 10f || Perf.Filled != 200)
            Fail($"perf: a paused frame was recorded ({Perf.FrameMs(0)} ms, {Perf.Filled} frames)");
        // The ring wraps: 700 more frames keep the newest 600.
        for (var i = 0; i < 700; i++)
            Perf.EndFrame(i);
        if (Perf.Filled != Perf.History || Perf.FrameMs(0) != 699f || Perf.FrameMs(Perf.History - 1) != 100f)
            Fail($"perf: after wrapping, {Perf.Filled} frames, newest {Perf.FrameMs(0)}, oldest {Perf.FrameMs(Perf.History - 1)} (want {Perf.History}, 699, 100)");
        var report = s.Report(null, "extra line\n");
        if (!report.Contains("1% low 35.00 ms") || !report.Contains("Horde") || !report.Contains("extra line") || !report.Contains("GPU timer: unavailable"))
            Fail($"perf: the text snapshot is missing parts:\n{report}");
        Line($"stats: 200 frames avg {s.AvgMs:0.00} ms, 1% low {s.Low1Ms:0.0} ms, worst {s.WorstMs[0]:0} ms {s.WorstBack[0]} frames back; snapshot {report.Length} chars");
    }

    private static void CheckPerfGpu()
    {
        var fake = new PerfFakeStamps(GpuStampRing.QueryCount(PerfGpu.Count, PerfGpu.Ring));
        var made = 0;
        var gpu = new PerfGpu(count =>
        {
            made++;
            return count == GpuStampRing.QueryCount(PerfGpu.Count, PerfGpu.Ring) ? fake : null;
        });
        Perf.Gpu = gpu;

        // Off: no queries made, nothing stamped.
        Perf.SetOn(false);
        for (var f = 0; f < 10; f++)
        {
            gpu.BeginFrame();
            PerfFrame(spin: false);
            gpu.EndFrame();
            Perf.EndFrame(16f);
        }
        if (made != 0 || gpu.Tried || fake.Stamps != 0)
            Fail("perf GPU: queries were made while the window was closed");

        // On: passes 1 ms (buildings), 2 ms (props), 0.5 ms (sky) apart on the fake clock; results 3 frames late.
        Perf.SetOn(true);
        Perf.ResetForTest();
        var recorded = 0;
        for (var f = 0; f < 120; f++)
        {
            (fake.Frame, fake.Now) = (f, 1_000_000_000UL + (ulong)f * 20_000_000UL);
            var before = fake.Stamps;
            gpu.BeginFrame();
            using (Perf.Measure(PerfArea.Render))
            {
                fake.Now += 1_000_000;
                gpu.Mark(PerfGpuPass.Buildings);
                fake.Now += 2_000_000;
                gpu.Mark(PerfGpuPass.Props);
                fake.Now += 500_000;
                gpu.Mark(PerfGpuPass.Sky);
                gpu.Mark(PerfGpuPass.Props); // out of order: skipped and counted
            }
            gpu.EndFrame();
            if (fake.Stamps > before)
                recorded++;
            Perf.EndFrame(16f);
        }
        if (made != 1 || !gpu.Available || fake.EarlyReads > 0)
            Fail($"perf GPU: available {gpu.Available}, {fake.EarlyReads} result(s) read before they were ready (would stall)");
        var p = gpu.Samples[(int)PerfGpuPass.Props];
        if (p < recorded - PerfGpu.Ring || MathF.Abs(gpu.AvgMs[(int)PerfGpuPass.Buildings] - 1f) > 1e-3f || MathF.Abs(gpu.AvgMs[(int)PerfGpuPass.Props] - 2f) > 1e-3f
            || MathF.Abs(gpu.AvgMs[(int)PerfGpuPass.Sky] - 0.5f) > 1e-3f || gpu.Samples[(int)PerfGpuPass.Terrain] != 0 || gpu.OutOfOrder != 120)
            Fail($"perf GPU: {p} props samples of {recorded} timed frames; buildings {gpu.AvgMs[0]:0.000} props {gpu.AvgMs[2]:0.000} sky {gpu.AvgMs[6]:0.000} ms (want 1, 2, 0.5), terrain samples {gpu.Samples[1]}, out of order {gpu.OutOfOrder} (want 120)");

        // A driver that never finishes: every slot stays pending, frames go on untimed, nothing is ever read or waited for.
        PerfFakeStamps? stuck = null;
        var never = new PerfGpu(count => stuck = new PerfFakeStamps(count) { Latency = int.MaxValue });
        var timedFrames = 0;
        for (var f = 0; f < 50; f++)
        {
            never.BeginFrame();
            stuck!.Frame = f;
            var before = stuck.Stamps;
            never.Mark(PerfGpuPass.Buildings);
            never.EndFrame();
            if (stuck.Stamps > before)
                timedFrames++;
        }
        if (stuck!.Reads != 0 || timedFrames != PerfGpu.Ring || never.Readings != 0)
            Fail($"perf GPU: a driver that never finishes: {stuck.Reads} reads (want 0), {timedFrames} frames timed (want {PerfGpu.Ring}: the ring fills, then frames go untimed)");

        // No timer queries on the driver: the window says so, nothing breaks.
        var none = new PerfGpu(_ => null);
        none.BeginFrame();
        none.Mark(PerfGpuPass.Sky);
        none.EndFrame();
        if (none.Available || !none.Tried)
            Fail("perf GPU: a driver without timestamps isn't reported as unavailable");
        Line($"GPU: {recorded} of 120 frames timed, {gpu.Readings} pass readings, never early; a stuck driver: {timedFrames} frames timed, 0 reads");
        Perf.Gpu = null;
    }

    private static void CheckPerfAllocations()
    {
        var gpu = new PerfGpu(count => new PerfFakeStamps(count) { Latency = 2 });
        Perf.Gpu = gpu;
        var stats = new PerfStats();
        var frame = 0;
        void Frames()
        {
            for (var i = 0; i < 300; i++)
            {
                gpu.BeginFrame();
                PerfFrame(spin: false);
                gpu.EndFrame();
                Perf.EndFrame();
                if (++frame % 15 == 0)
                    stats.Update();
            }
        }
        Perf.SetOn(true);
        Perf.ResetForTest();
        var on = SteadyAllocBytes(Frames);
        Perf.SetOn(false);
        var off = SteadyAllocBytes(Frames);
        // The window closed: Ui.DrawPerf is one check.
        var ui = new Ui();
        var closed = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 20000; i++)
                ui.DrawPerf(null, i * 0.016, Keybinds.Unbound);
        });
        Line($"allocations over 300 frames: {on} bytes timing (scopes, GPU passes, frame ends, stats), {off} bytes off; window closed {closed} bytes over 20000 frames");
        if (on > 0 || off > 0 || closed > 0)
            Fail($"perf: {on} bytes allocated per 300 frames timing, {off} off, {closed} with the window closed (want 0)");
        if (Perf.On)
            Fail("perf: a closed window turned timing on");
        Perf.Gpu = null;
    }

    private static void CheckPerfKey()
    {
        var kd = Keybinds.Defaults;
        if (kd.Length != (int)GameAction.COUNT || kd[(int)GameAction.PerfWindow] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F9) || Duplicates(kd).Length > 0)
            Fail($"perf: the default key isn't an unshared F9 ({Duplicates(kd)})");
        // An old settings file (binds version 7): F9 added; one that already used F9 keeps it and the window starts unbound.
        var old = (int[])kd.Clone();
        old[(int)GameAction.PerfWindow] = Keybinds.Unbound;
        var moved = Keybinds.Migrate(old, 7);
        if (old[(int)GameAction.PerfWindow] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F9) || !moved.Contains(GameAction.PerfWindow) || moved.Count(a => a < GameAction.LookAtInspector) != 1) // (later versions' added keys move too)
            Fail($"perf: migrating binds version 7 gave the perf window {Keybinds.Name(old[(int)GameAction.PerfWindow])}, {moved.Count} moved");
        var taken = (int[])kd.Clone();
        taken[(int)GameAction.PerfWindow] = Keybinds.Unbound;
        taken[(int)GameAction.Melee] = Keybinds.Key(Brutal.GlfwApi.GlfwKey.F9);
        Keybinds.Migrate(taken, 7);
        if (taken[(int)GameAction.PerfWindow] != Keybinds.Unbound || taken[(int)GameAction.Melee] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F9))
            Fail("perf: migrating took F9 from an action the player had put on it");
    }
}
