using System.Globalization;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The numbers the performance window shows, worked out from <see cref="Perf"/>'s history: frame time min / average /
/// max / 1% low over the whole graph, the worst frames, and per area the average and peak over the last second. All in
/// fixed arrays made once; <see cref="Update"/> allocates nothing (the window calls it a few times a second).
/// </summary>
public sealed class PerfStats
{
    public const int Worst = 3;

    public int Frames; // frames in the graph
    public float MinMs, AvgMs, MaxMs, Low1Ms, Fps, Low1Fps;

    /// <summary>The worst frames: how many frames back each is (newest first order not kept: worst first).</summary>
    public readonly int[] WorstBack = new int[Worst];

    public readonly float[] WorstMs = new float[Worst];

    /// <summary>Per area over the last second: average and peak ms per frame, and the share of frames it ran in.</summary>
    public readonly float[] AreaAvg = new float[Perf.Count], AreaMax = new float[Perf.Count];

    /// <summary>Per area: average minus its children's averages (time in the area itself).</summary>
    public readonly float[] AreaSelf = new float[Perf.Count];

    /// <summary>Frames in the last second, and the server tick figures over it.</summary>
    public int RecentFrames;

    public float TickWorstMs, TickAvgMs, TicksPerSecond, MusicMsPerSecond;

    /// <summary>Over the last second: draw calls, triangles, instances, main-thread / all-thread allocated bytes per frame.</summary>
    public float Draws, Triangles, Instances, AllocBytes, AllocAllBytes;

    /// <summary>Frames in the graph with a gen 0 / 1 / 2 collection.</summary>
    public int Gc0Frames, Gc1Frames, Gc2Frames;

    private readonly float[] _sorted = new float[Perf.History];

    /// <summary>Works everything out from the history (<paramref name="recentMs"/>: how far back "the last second" goes).</summary>
    public void Update(double recentMs = 1000)
    {
        var n = Perf.Filled;
        // The newest frame may be zero (the very first); skip zero frames.
        Frames = 0;
        double sum = 0;
        MinMs = float.MaxValue;
        MaxMs = 0;
        Array.Fill(WorstMs, 0f);
        Array.Fill(WorstBack, -1);
        Gc0Frames = Gc1Frames = Gc2Frames = 0;
        for (var b = 0; b < n; b++)
        {
            var f = Perf.FrameMs(b);
            if (f <= 0)
                continue;
            _sorted[Frames++] = f;
            sum += f;
            MinMs = MathF.Min(MinMs, f);
            MaxMs = MathF.Max(MaxMs, f);
            // Keep the worst few (insertion into a tiny sorted list).
            for (var k = 0; k < Worst; k++)
                if (f > WorstMs[k])
                {
                    for (var j = Worst - 1; j > k; j--)
                    {
                        WorstMs[j] = WorstMs[j - 1];
                        WorstBack[j] = WorstBack[j - 1];
                    }
                    WorstMs[k] = f;
                    WorstBack[k] = b;
                    break;
                }
            var gc = PerfCounters.GcAt(b);
            if ((gc & 1) != 0)
                Gc0Frames++;
            if ((gc & 2) != 0)
                Gc1Frames++;
            if ((gc & 4) != 0)
                Gc2Frames++;
        }
        if (Frames == 0)
        {
            MinMs = AvgMs = MaxMs = Low1Ms = Fps = Low1Fps = 0;
        }
        else
        {
            AvgMs = (float)(sum / Frames);
            Fps = AvgMs > 0 ? 1000f / AvgMs : 0;
            // 1% low: the average of the slowest 1% of frames (at least one), as fps.
            Array.Sort(_sorted, 0, Frames);
            var k = Math.Max(1, Frames / 100);
            double low = 0;
            for (var i = Frames - k; i < Frames; i++)
                low += _sorted[i];
            Low1Ms = (float)(low / k);
            Low1Fps = Low1Ms > 0 ? 1000f / Low1Ms : 0;
        }

        // The last second.
        Array.Clear(AreaAvg);
        Array.Clear(AreaMax);
        RecentFrames = 0;
        double span = 0, tickSum = 0, music = 0;
        var ticks = 0;
        TickWorstMs = 0;
        Draws = Triangles = Instances = AllocBytes = AllocAllBytes = 0;
        for (var b = 0; b < n && span < recentMs; b++)
        {
            var f = Perf.FrameMs(b);
            if (f <= 0)
                continue;
            span += f;
            RecentFrames++;
            for (var a = 0; a < Perf.Count; a++)
            {
                var v = Perf.AreaMs(b, a);
                AreaAvg[a] += v;
                if (v > AreaMax[a])
                    AreaMax[a] = v;
            }
            TickWorstMs = MathF.Max(TickWorstMs, Perf.TickWorstMs(b));
            ticks += Perf.Ticks(b);
            tickSum += Perf.AreaMs(b, PerfArea.ServerTick);
            music += Perf.MusicMs(b);
            Draws += PerfCounters.DrawsAt(b);
            Triangles += PerfCounters.TrianglesAt(b);
            Instances += PerfCounters.InstancesAt(b);
            AllocBytes += PerfCounters.AllocAt(b);
            AllocAllBytes += PerfCounters.AllocAllAt(b);
        }
        if (RecentFrames > 0)
        {
            for (var a = 0; a < Perf.Count; a++)
                AreaAvg[a] /= RecentFrames;
            Draws /= RecentFrames;
            Triangles /= RecentFrames;
            Instances /= RecentFrames;
            AllocBytes /= RecentFrames;
            AllocAllBytes /= RecentFrames;
        }
        TickAvgMs = ticks > 0 ? (float)(tickSum / ticks) : 0;
        TicksPerSecond = span > 0 ? (float)(ticks * 1000 / span) : 0;
        MusicMsPerSecond = span > 0 ? (float)(music * 1000 / span) : 0;
        for (var a = 0; a < Perf.Count; a++)
            AreaSelf[a] = AreaAvg[a];
        for (var a = 0; a < Perf.Count; a++)
            if (Perf.Parent[a] >= 0)
                AreaSelf[Perf.Parent[a]] -= AreaAvg[a];
        for (var a = 0; a < Perf.Count; a++)
            AreaSelf[a] = MathF.Max(0, AreaSelf[a]);
    }

    /// <summary>
    /// A plain-text snapshot of everything (for the clipboard or the log): frame figures, every area, the GPU passes, the
    /// counters and <paramref name="extra"/> (the window's game counters). Allocates (only when asked for).
    /// </summary>
    public string Report(PerfGpu? gpu, string extra)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(c, $"Call of Slop perf snapshot, build {BuildInfo.Version}, {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n");
        sb.Append(c, $"frames {Frames}: avg {AvgMs:0.00} ms ({Fps:0} fps), min {MinMs:0.00}, max {MaxMs:0.00}, 1% low {Low1Ms:0.00} ms ({Low1Fps:0} fps)\n");
        sb.Append("worst frames:");
        for (var k = 0; k < Worst; k++)
            if (WorstBack[k] >= 0)
                sb.Append(c, $" {WorstMs[k]:0.0} ms ({WorstBack[k]} frames ago)");
        sb.Append('\n');
        sb.Append(c, $"server ticks: {TicksPerSecond:0}/s, avg {TickAvgMs:0.000} ms, worst {TickWorstMs:0.000} ms (budget 8 ms)\n");
        sb.Append(c, $"CPU per frame over the last {RecentFrames} frames (avg / self / peak ms):\n");
        for (var a = 0; a < Perf.Count; a++)
            sb.Append(c, $"  {new string(' ', Perf.Depth[a] * 2)}{Perf.Names[a]}: {AreaAvg[a]:0.000} / {AreaSelf[a]:0.000} / {AreaMax[a]:0.000}\n");
        sb.Append(c, $"music thread: {MusicMsPerSecond:0.0} ms per second\n");
        if (gpu is { Available: true })
        {
            sb.Append(c, $"GPU per pass (smoothed ms), total {gpu.TotalAvgMs():0.000}, plus {gpu.GapAvgMs:0.000} between passes (GPU waiting for the CPU):\n");
            for (var p = 0; p < PerfGpu.Count; p++)
                if (gpu.Samples[p] > 0)
                    sb.Append(c, $"  {PerfGpu.Names[p]}: {gpu.AvgMs[p]:0.000}\n");
        }
        else
            sb.Append("GPU timer: unavailable\n");
        sb.Append(c, $"draw calls {Draws:0}, triangles {Triangles:0}, instances {Instances:0} per frame\n");
        sb.Append(c, $"allocated per frame: {AllocBytes:0} B main thread, {AllocAllBytes:0} B all threads; heap {PerfCounters.HeapBytes / 1048576.0:0.0} MB; GC frames in graph: gen0 {Gc0Frames}, gen1 {Gc1Frames}, gen2 {Gc2Frames}\n");
        if (Perf.Misnested > 0)
            sb.Append(c, $"misnested scopes: {Perf.Misnested}\n");
        sb.Append(extra);
        return sb.ToString();
    }
}
