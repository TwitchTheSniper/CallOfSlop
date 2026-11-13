using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// The benchmark's recorder (docs/perf/BENCHMARK.md): while a spot is being measured, every frame's time, every
/// <see cref="PerfArea"/>'s CPU milliseconds, the counters and the GPU passes are kept, and at the spot's end they are
/// summed up into one <see cref="Spot"/> (mean, median, 95th and 99th percentile, the worst frames and what was slow in
/// them). <see cref="WriteJson"/> writes a run's spots for <c>--bench-compare</c>. Fed from <see cref="Perf.EndFrame"/>:
/// not recording, that is one bool test a frame. All buffers are made at the first spot; nothing allocates per frame.
/// </summary>
public static class PerfBench
{
    /// <summary>Most frames one spot keeps (more are timed, not kept).</summary>
    public const int MaxFrames = 6000;

    /// <summary>Worst frames kept per spot, each with its slowest areas.</summary>
    public const int Worst = 3;

    /// <summary>Server ticks at least this long (ms) are traced by section while measuring (<see cref="AllocProfile.SlowTickMs"/>; 0 off: the marks then cost a bool test).</summary>
    public static double TraceTicksMs;

    /// <summary>Time every marked section of the frame while measuring (the slowest go into <see cref="Spot.FrameSections"/>; it slows the frame).</summary>
    public static bool TraceFrame;

    /// <summary>A spot is being measured (<see cref="Perf.EndFrame"/> calls <see cref="Sample"/>).</summary>
    public static bool Recording;

    private static float[]? _frame, _areas;
    private static int[]? _draws, _tris, _inst, _alloc, _allocAll;
    private static int _n, _gcFrames, _ticks;
    private static float _tickWorst;
    private static double _tickSum;
    private static int _gc0, _gc1, _gc2;
    private static readonly double[] GpuSum0 = new double[PerfGpu.Count];
    private static readonly int[] GpuSamples0 = new int[PerfGpu.Count];
    private static int _gpuFrames0;
    private static double _gpuGaps0;
    private static string _label = "";

    /// <summary>The finished spots of this run, in order.</summary>
    public static readonly List<Spot> Spots = [];

    public sealed class Spot
    {
        public string Name = "";
        public int Frames;
        public double Mean, P50, P95, P99, Max, Fps, Low1Fps;
        public double[] AreaMean = new double[Perf.Count], AreaMax = new double[Perf.Count];
        public double[] GpuMean = new double[PerfGpu.Count];
        public double GpuTotal, GpuGap;
        public int GpuFrames;
        public double Draws, Triangles, Instances, AllocBytes, AllocAllBytes;
        public int Gc0, Gc1, Gc2, GcFrames;
        public double TicksPerFrame, TickMean, TickWorst;
        public (int Index, float Ms, (int Area, float Ms)[] Top)[] WorstFrames = [];
        public Dictionary<string, double> Extra = [];

        /// <summary>Other load while measuring: cores' worth of CPU other game windows and builds used, and how many there were.</summary>
        public double OtherCores;

        public string Others = "";

        /// <summary>With <see cref="TraceTicksMs"/>: the slow ticks and their slowest sections.</summary>
        public string[] SlowTicks = [];

        /// <summary>With <see cref="TraceFrame"/>: the slowest marked sections, ms per frame.</summary>
        public string FrameSections = "";
    }

    // ---- What else was running (the numbers aren't the game's own when another window or a build shares the machine) --

    private static readonly string[] Rivals = ["CallOfSlop", "csc", "VBCSCompiler", "dotnet", "MSBuild"];
    private static double _otherCpu0;
    private static long _wall0;

    /// <summary>CPU seconds so far of the other game windows and builds, and a word on which ran (outside the measured frames: it allocates).</summary>
    private static (double Seconds, string Who) OtherLoad()
    {
        double s = 0;
        var who = new SortedDictionary<string, int>();
        var me = Environment.ProcessId;
        foreach (var name in Rivals)
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                using (p)
                {
                    if (p.Id == me)
                        continue;
                    try
                    {
                        s += p.TotalProcessorTime.TotalSeconds;
                        who[name] = who.GetValueOrDefault(name) + 1;
                    }
                    catch (Exception)
                    {
                        // (gone, or not ours to read)
                    }
                }
        return (s, string.Join(", ", who.Select(kv => $"{kv.Value} {kv.Key}")));
    }

    /// <summary>Starts measuring a spot: turns the profiler on (and the GPU timer with it) and forgets the last spot's frames.</summary>
    public static void Begin(string label)
    {
        _frame ??= new float[MaxFrames];
        _areas ??= new float[MaxFrames * Perf.Count];
        _draws ??= new int[MaxFrames];
        _tris ??= new int[MaxFrames];
        _inst ??= new int[MaxFrames];
        _alloc ??= new int[MaxFrames];
        _allocAll ??= new int[MaxFrames];
        _label = label;
        _n = _gcFrames = _ticks = 0;
        _tickWorst = 0;
        _tickSum = 0;
        if (!Perf.On)
            Perf.SetOn(true); // (the first frame after this only starts the deltas: Sample skips frames before the profiler's own first)
        (_gc0, _gc1, _gc2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        if (Perf.Gpu is { } g)
        {
            Array.Copy(g.SumMs, GpuSum0, PerfGpu.Count);
            Array.Copy(g.Samples, GpuSamples0, PerfGpu.Count);
            _gpuFrames0 = g.FramesRead;
            _gpuGaps0 = g.GapsSum;
        }
        if (TraceFrame)
        {
            AllocProfile.Reset();
            AllocProfile.On = true;
        }
        else if (TraceTicksMs > 0)
        {
            AllocProfile.SlowTicks.Clear();
            AllocProfile.SlowTickMs = TraceTicksMs;
            AllocProfile.On = true;
        }
        _otherCpu0 = OtherLoad().Seconds;
        _wall0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _skip = 2; // (the frame Begin lands in, and the one after: the profiler's areas start clean)
        Recording = true;
    }

    private static int _skip;

    /// <summary>A frame has ended (<see cref="Perf.EndFrame"/>, after its rings are written): keep it.</summary>
    internal static void Sample()
    {
        if (_skip > 0)
        {
            _skip--;
            return;
        }
        if (_n >= MaxFrames || !Perf.On)
            return;
        var i = _n++;
        _frame![i] = Perf.FrameMs(0);
        var b = i * Perf.Count;
        for (var a = 0; a < Perf.Count; a++)
            _areas![b + a] = Perf.AreaMs(0, a);
        _draws![i] = PerfCounters.DrawsAt(0);
        _tris![i] = PerfCounters.TrianglesAt(0);
        _inst![i] = PerfCounters.InstancesAt(0);
        _alloc![i] = PerfCounters.AllocAt(0);
        _allocAll![i] = PerfCounters.AllocAllAt(0);
        if (PerfCounters.GcAt(0) != 0)
            _gcFrames++;
        var t = Perf.Ticks(0);
        _ticks += t;
        if (t > 0)
        {
            _tickSum += Perf.AreaMs(0, PerfArea.ServerTick);
            _tickWorst = MathF.Max(_tickWorst, Perf.TickWorstMs(0));
        }
    }

    /// <summary>Ends the spot: its summary goes into <see cref="Spots"/> and is returned. <paramref name="keepOn"/>: leave the profiler on.</summary>
    public static Spot End(bool keepOn = false)
    {
        Recording = false;
        var s = new Spot { Name = _label, Frames = _n };
        if (TraceFrame)
        {
            AllocProfile.Stop();
            AllocProfile.On = false;
            s.FrameSections = AllocProfile.TopTime(24, Math.Max(1, _n));
        }
        else if (TraceTicksMs > 0)
        {
            AllocProfile.On = false;
            AllocProfile.SlowTickMs = 0;
            s.SlowTicks = [.. AllocProfile.SlowTicks];
        }
        var (other, who) = OtherLoad();
        var wall = System.Diagnostics.Stopwatch.GetElapsedTime(_wall0).TotalSeconds;
        s.OtherCores = wall > 0 ? Math.Max(0, other - _otherCpu0) / wall : 0;
        s.Others = who;
        var n = _n;
        if (n > 0)
        {
            var sorted = _frame![..n];
            Array.Sort(sorted);
            double Q(double q) => sorted[Math.Min(n - 1, (int)(n * q))];
            double sum = 0;
            foreach (var f in sorted)
                sum += f;
            s.Mean = sum / n;
            (s.P50, s.P95, s.P99, s.Max) = (Q(0.5), Q(0.95), Q(0.99), sorted[^1]);
            s.Fps = s.Mean > 0 ? 1000 / s.Mean : 0;
            var low = Math.Max(1, n / 100); // the slowest 1% of frames, as fps
            double lowSum = 0;
            for (var k = n - low; k < n; k++)
                lowSum += sorted[k];
            s.Low1Fps = lowSum > 0 ? 1000 * low / lowSum : 0;
            for (var a = 0; a < Perf.Count; a++)
            {
                double m = 0, mx = 0;
                for (var k = 0; k < n; k++)
                {
                    var v = _areas![k * Perf.Count + a];
                    m += v;
                    if (v > mx)
                        mx = v;
                }
                s.AreaMean[a] = m / n;
                s.AreaMax[a] = mx;
            }
            double d = 0, t = 0, ins = 0, al = 0, aa = 0;
            for (var k = 0; k < n; k++)
            {
                d += _draws![k];
                t += _tris![k];
                ins += _inst![k];
                al += _alloc![k];
                aa += _allocAll![k];
            }
            (s.Draws, s.Triangles, s.Instances, s.AllocBytes, s.AllocAllBytes) = (d / n, t / n, ins / n, al / n, aa / n);
            s.TicksPerFrame = (double)_ticks / n;
            s.TickMean = _ticks > 0 ? _tickSum / _ticks : 0;
            s.TickWorst = _tickWorst;
            s.WorstFrames = WorstOf(n);
        }
        s.GcFrames = _gcFrames;
        (s.Gc0, s.Gc1, s.Gc2) = (GC.CollectionCount(0) - _gc0, GC.CollectionCount(1) - _gc1, GC.CollectionCount(2) - _gc2);
        if (Perf.Gpu is { Available: true } g)
        {
            g.Collect(); // (what is ready; never waits)
            s.GpuFrames = g.FramesRead - _gpuFrames0;
            if (s.GpuFrames > 0)
            {
                for (var p = 0; p < PerfGpu.Count; p++)
                {
                    s.GpuMean[p] = (g.SumMs[p] - GpuSum0[p]) / s.GpuFrames;
                    s.GpuTotal += s.GpuMean[p];
                }
                s.GpuGap = (g.GapsSum - _gpuGaps0) / s.GpuFrames;
            }
        }
        if (!keepOn)
            Perf.SetOn(false);
        Spots.Add(s);
        return s;
    }

    /// <summary>The worst frames, each with the areas that took longest in it (not their parents: the deepest that are over 0.1 ms).</summary>
    private static (int, float, (int, float)[])[] WorstOf(int n)
    {
        var picks = new List<(int, float, (int, float)[])>(Worst);
        var used = new bool[n];
        for (var w = 0; w < Math.Min(Worst, n); w++)
        {
            var best = -1;
            for (var k = 0; k < n; k++)
                if (!used[k] && (best < 0 || _frame![k] > _frame[best]))
                    best = k;
            used[best] = true;
            var b = best * Perf.Count;
            var top = new List<(int, float)>();
            for (var a = 0; a < Perf.Count; a++)
            {
                var v = _areas![b + a];
                if (v < 0.1f)
                    continue;
                var kids = 0f; // (an area whose children explain it is left out: the children say more)
                for (var c = 0; c < Perf.Count; c++)
                    if (Perf.Parent[c] == a)
                        kids += _areas[b + c];
                if (kids < v * 0.8f)
                    top.Add((a, v));
            }
            top.Sort((x, y) => y.Item2.CompareTo(x.Item2));
            picks.Add((best, _frame![best], top.Take(6).ToArray()));
        }
        return picks.ToArray();
    }

    /// <summary>One line for the log: the spot's frame rate, its 1% low, CPU and GPU, draws and allocations.</summary>
    public static string Line(Spot s) =>
        Inv($"[bench] {s.Name}: {s.Frames} frames, {s.Fps:0} fps (1% low {s.Low1Fps:0}), median {s.P50:0.00} ms, 99th {s.P99:0.00}, worst {s.Max:0.00}; ")
        + Inv($"CPU game {s.AreaMean[(int)PerfArea.Game]:0.00} render {s.AreaMean[(int)PerfArea.Render]:0.00} ui {s.AreaMean[(int)PerfArea.Ui]:0.00} swap {s.AreaMean[(int)PerfArea.Swap]:0.00} ms; ")
        + Inv($"GPU {(s.GpuFrames > 0 ? $"{s.GpuTotal:0.00} ms" : "n/a")}; {s.Draws:0} draws, {s.Triangles / 1000:0}k tris; {s.AllocBytes:0} B/frame, gc0 {s.Gc0}; worst tick {s.TickWorst:0.00} ms")
        + (s.OtherCores >= 0.05 ? Inv($"; NOT ALONE: {s.Others} used {s.OtherCores:0.0} cores") : "");

    private static string Inv(FormattableString f) => f.ToString(CultureInfo.InvariantCulture);

    // ---- The run's file --------------------------------------------------------------------------------------------

    /// <summary>Writes this run (its settings in <paramref name="meta"/>, then every spot) as JSON.</summary>
    public static void WriteJson(string path, JsonObject meta)
    {
        static double R(double v, int d = 3) => Math.Round(v, d);
        var spots = new JsonArray();
        foreach (var s in Spots)
        {
            var cpu = new JsonObject();
            for (var a = 0; a < Perf.Count; a++)
                if (s.AreaMean[a] >= 0.0005 || s.AreaMax[a] >= 0.05)
                    cpu[Perf.Names[a]] = new JsonObject { ["mean"] = R(s.AreaMean[a], 4), ["max"] = R(s.AreaMax[a]), ["depth"] = Perf.Depth[a] };
            var gpu = new JsonObject();
            for (var p = 0; p < PerfGpu.Count; p++)
                if (s.GpuMean[p] >= 0.0005)
                    gpu[PerfGpu.Names[p]] = R(s.GpuMean[p], 4);
            var worst = new JsonArray();
            foreach (var (index, ms, top) in s.WorstFrames)
            {
                var areas = new JsonObject();
                foreach (var (a, v) in top)
                    areas[Perf.Names[a]] = R(v);
                worst.Add(new JsonObject { ["frame"] = index, ["ms"] = R(ms), ["areas"] = areas });
            }
            var extra = new JsonObject();
            foreach (var (k, v) in s.Extra)
                extra[k] = v;
            spots.Add(new JsonObject
            {
                ["name"] = s.Name, ["frames"] = s.Frames,
                ["fps"] = R(s.Fps, 1), ["medianFps"] = s.P50 > 0 ? R(1000 / s.P50, 1) : 0, ["low1Fps"] = R(s.Low1Fps, 1),
                ["otherCores"] = R(s.OtherCores, 2), ["others"] = s.Others,
                ["frameMs"] = new JsonObject { ["mean"] = R(s.Mean), ["p50"] = R(s.P50), ["p95"] = R(s.P95), ["p99"] = R(s.P99), ["max"] = R(s.Max) },
                ["gpuMs"] = s.GpuFrames > 0 ? R(s.GpuTotal) : null, ["gpuFrames"] = s.GpuFrames, ["gpuGapMs"] = R(s.GpuGap),
                ["draws"] = R(s.Draws, 1), ["triangles"] = Math.Round(s.Triangles), ["instances"] = Math.Round(s.Instances),
                ["allocBytesPerFrame"] = Math.Round(s.AllocBytes), ["allocAllThreadsPerFrame"] = Math.Round(s.AllocAllBytes),
                ["gc"] = new JsonObject { ["gen0"] = s.Gc0, ["gen1"] = s.Gc1, ["gen2"] = s.Gc2, ["frames"] = s.GcFrames },
                ["ticks"] = new JsonObject { ["perFrame"] = R(s.TicksPerFrame), ["meanMs"] = R(s.TickMean), ["worstMs"] = R(s.TickWorst) },
                ["world"] = extra,
                ["cpu"] = cpu, ["gpu"] = gpu, ["worstFrames"] = worst,
                ["slowTicks"] = new JsonArray([.. s.SlowTicks.Select(x => (JsonNode)x)]),
                ["frameSections"] = s.FrameSections,
            });
        }
        meta["spots"] = spots;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- --bench-compare -------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>--bench-compare BASE.json NEW.json [--noise PCT]</c>: each spot side by side (frame rate, 1% low, 99th percentile,
    /// GPU, CPU render and game, draws, allocations) and the CPU areas and GPU passes that moved most. A change past the
    /// noise (default 5%) the wrong way is marked WORSE (the median frame rate; the 99th percentile at twice the noise), and spots
    /// measured while another game window or a build ran are listed. Returns the number of spots marked WORSE.
    /// </summary>
    public static int Compare(string basePath, string newPath, double noisePct, TextWriter o)
    {
        JsonNode Load(string p) => JsonNode.Parse(File.ReadAllText(p)) ?? throw new InvalidDataException($"{p}: empty");
        var a = Load(basePath);
        var b = Load(newPath);
        o.WriteLine($"base: {basePath}  ({a["build"]}, {a["when"]}, {a["gpu"]})");
        o.WriteLine($"new:  {newPath}  ({b["build"]}, {b["when"]}, {b["gpu"]})");
        foreach (var k in new[] { "map", "size", "video" })
            if (a[k]?.ToJsonString() != b[k]?.ToJsonString())
                o.WriteLine($"note: {k} differs ({a[k]?.ToJsonString()} vs {b[k]?.ToJsonString()}): the numbers aren't like for like");
        var worse = 0;
        var noise = noisePct / 100;
        string Pct(double x, double y) => x > 0 ? $"{(y - x) / x * 100,6:+0.0;-0.0;0.0}%" : "     -";
        o.WriteLine();
        o.WriteLine($"{"spot",-22} {"median fps",17} {"mean fps",17} {"99th ms",17} {"GPU ms",17} {"render ms",17} {"game ms",17} {"draws",15}");
        var busy = new List<string>();
        var bs = b["spots"]!.AsArray().ToDictionary(s => (string)s!["name"]!, s => s!);
        foreach (var sa in a["spots"]!.AsArray())
        {
            var name = (string)sa!["name"]!;
            if (!bs.TryGetValue(name, out var sb))
            {
                o.WriteLine($"{name,-22} (not in the new run)");
                continue;
            }
            double V(JsonNode s, string path)
            {
                JsonNode? n = s;
                foreach (var part in path.Split('/'))
                    n = n?[part];
                return n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
            }
            var flag = "";
            foreach (var (run, s) in new[] { ("base", sa), ("new", sb) })
                if (V(s, "otherCores") >= 0.05)
                    busy.Add($"{name} ({run}): {(string?)s["others"]} used {V(s, "otherCores"):0.0} cores");
            string Col(string path, bool higherBetter, string fmt = "0.00")
            {
                double x = V(sa, path), y = V(sb, path);
                var tol = path == "frameMs/p99" ? noise * 2 : noise; // (the slow frames are noisier than the median)
                var bad = x > 0 && (higherBetter ? y < x * (1 - tol) : y > x * (1 + tol));
                if (bad && (path == "medianFps" || path == "frameMs/p99"))
                    flag = " WORSE";
                return $"{x.ToString(fmt, CultureInfo.InvariantCulture),7}>{y.ToString(fmt, CultureInfo.InvariantCulture),-7}{(bad ? "!" : " ")}".PadRight(10) + Pct(x, y);
            }
            var line = $"{name,-22} {Col("medianFps", true, "0")} {Col("fps", true, "0")} {Col("frameMs/p99", false)} {Col("gpuMs", false)} "
                       + $"{Col("cpu/Render (CPU)/mean", false)} {Col("cpu/Game update/mean", false)} {V(sa, "draws"),6:0}>{V(sb, "draws"),-6:0}";
            if (flag.Length > 0)
                worse++;
            o.WriteLine(line + flag);
            // The parts that moved most (CPU areas and GPU passes, at least 0.02 ms and past the noise).
            var moves = new List<(string, double, double)>();
            foreach (var (group, key) in new[] { ("cpu", "mean"), ("gpu", "") })
            {
                var names = new HashSet<string>();
                if (sa[group] is JsonObject ga)
                    foreach (var kv in ga)
                        names.Add(kv.Key);
                if (sb[group] is JsonObject gb)
                    foreach (var kv in gb)
                        names.Add(kv.Key);
                foreach (var n in names)
                {
                    var path = key.Length > 0 ? $"{group}/{n}/{key}" : $"{group}/{n}";
                    double x = V(sa, path), y = V(sb, path);
                    if (Math.Abs(y - x) >= 0.02 && Math.Abs(y - x) > Math.Max(x, y) * noise)
                        moves.Add(($"{(group == "cpu" ? "CPU" : "GPU")} {n}", x, y));
                }
            }
            foreach (var (n, x, y) in moves.OrderByDescending(m => Math.Abs(m.Item3 - m.Item2)).Take(5))
                o.WriteLine($"{"",-24}{n}: {x:0.000} > {y:0.000} ms ({(y - x >= 0 ? "+" : "")}{y - x:0.000})");
        }
        o.WriteLine();
        o.WriteLine(worse == 0 ? $"no spot worse past {noisePct:0.#}% noise" : $"{worse} spot(s) worse: median fps past {noisePct:0.#}%, or the 99th percentile past {noisePct * 2:0.#}%");
        if (busy.Count > 0)
        {
            o.WriteLine("NOT ALONE: other game windows or builds ran during these spots, so their numbers mean little; run again when the machine is free:");
            foreach (var line in busy)
                o.WriteLine($"  {line}");
        }
        return worse;
    }

    /// <summary>The video settings a run was made with (they change the numbers: compare like with like).</summary>
    public static JsonObject Video(Settings s) => new()
    {
        ["materials"] = s.Materials.ToString(), ["postEffects"] = s.PostEffects.ToString(), ["particles"] = s.Particles.ToString(),
        ["foliage"] = s.FoliageQuality.ToString(), ["sky"] = s.SkyQuality.ToString(),
    };

    internal static string Stamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

}
