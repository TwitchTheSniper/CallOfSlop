using System.Globalization;
using System.Text.Json.Nodes;

namespace CallOfSlop;

/// <summary>
/// The frame-rate benchmark (docs/perf/BENCHMARK.md). <c>--bench [SPOTS.json] [--out FILE.json] [--frames N] [--settle N]
/// [--shots] [--storm] [--trace-ticks MS]</c> turns into a window-tool run (Program.WindowShot.cs) on the spots file's world: the opening
/// skipped, VSync and caps off, then for each spot the camera flown there with the tour's <c>view</c>
/// (Program.WindowShotTour.cs), <c>--settle</c> frames to let streaming and the GPU settle, and <c>perf</c> over
/// <c>--frames</c> frames, which with <c>--bench</c> also records the profiler's whole breakdown (PerfBench.cs). After
/// every spot the run's JSON is written again (a crash keeps the spots done). <c>--bench-compare BASE NEW [--noise PCT]</c>
/// prints two runs side by side. Spots files are in tools/bench/ (found by walking up from the exe).
/// </summary>
public static partial class Program
{
    /// <summary>The run's JSON (empty: not a benchmark run).</summary>
    private static string _benchOut = "";

    private static JsonObject? _benchMeta;

    /// <summary>--bench: the window tool's arguments for the spots file (anything else on the line is kept), or the args unchanged.</summary>
    private static string[] BenchArgs(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--bench", StringComparison.OrdinalIgnoreCase));
        if (at < 0)
            return args;
        string? Val(string flag) => args.SkipWhile(a => !a.Equals(flag, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var file = at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : "glacier";
        var path = BenchSpotsFile(file);
        if (path == null)
        {
            Console.Error.WriteLine($"[bench] no spots file \"{file}\" (a path, or a name in tools/bench/)");
            Environment.Exit(2);
        }
        var spec = JsonNode.Parse(File.ReadAllText(path!))!;
        var map = (string?)spec["map"] ?? "Glacier";
        var frames = int.TryParse(Val("--frames"), out var f) ? f : (int?)spec["frames"] ?? 900;
        var settle = int.TryParse(Val("--settle"), out var st) ? st : (int?)spec["settle"] ?? 900;
        var size = Val("--size") ?? (string?)spec["size"] ?? "1600x900";
        var shots = args.Any(a => a.Equals("--shots", StringComparison.OrdinalIgnoreCase));
        var calm = (bool?)spec["calm"] ?? true;
        if (args.Any(a => a.Equals("--storm", StringComparison.OrdinalIgnoreCase)))
            calm = false;
        var stem = Path.GetFileNameWithoutExtension(path);
        _benchOut = Path.GetFullPath(Val("--out") ?? Path.Combine(BenchResults(), $"{stem}-{PerfBench.Stamp()}.json"));
        var names = new List<string>();
        var script = new List<string>();
        if ((bool?)spec["survival"] ?? true)
            script.Add("skip");
        script.Add("wait 120");
        foreach (var s in spec["spots"]!.AsArray())
        {
            var name = ((string?)s!["name"] ?? $"spot{names.Count + 1}").Replace(' ', '_').Replace(';', '_');
            var view = (string?)s["view"] ?? throw new InvalidDataException($"{path}: spot {name} has no \"view\"");
            names.Add(name);
            script.Add($"view {view}");
            script.Add($"wait {(int?)s["settle"] ?? settle}");
            script.Add($"perf {(int?)s["frames"] ?? frames} {name}");
            if (shots)
                script.Add($"shot {name}");
        }
        var shotPath = Path.ChangeExtension(_benchOut, ".png");
        var outArgs = new List<string> { "--window-shot", shotPath, "--map", map, "--uncapped", "--size", size, "--frames", "5", "--do", string.Join("; ", script) };
        if ((bool?)spec["survival"] ?? true)
            outArgs.Add("--survival");
        if (calm)
            outArgs.Add("--calm");
        if ((string?)spec["start"] is { Length: > 0 } start)
            outArgs.AddRange(["--start", start]);
        // Everything else the caller gave (--save-dir, --gl41 ...), past the bench's own flags.
        PerfBench.TraceFrame = args.Any(a => a.Equals("--trace-frame", StringComparison.OrdinalIgnoreCase));
        PerfBench.TraceTicksMs = double.TryParse(Val("--trace-ticks"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tt) ? tt : 0;
        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--out", "--frames", "--settle", "--size", "--trace-ticks" }; // (each takes a value)
        var bare = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--bench", "--shots", "--storm", "--trace-frame" };
        for (var i = 0; i < args.Length; i++)
        {
            if (i == at && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                i++;
                continue;
            }
            if (bare.Contains(args[i]))
                continue;
            if (own.Contains(args[i]))
            {
                i++;
                continue;
            }
            outArgs.Add(args[i]);
        }
        _benchMeta = new JsonObject
        {
            ["spotsFile"] = Path.GetFileName(path), ["map"] = map, ["calm"] = calm, ["size"] = size, ["frames"] = frames, ["settle"] = settle,
            ["when"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), ["build"] = BuildInfo.Label,
            ["machine"] = $"{Environment.ProcessorCount} threads, {Environment.OSVersion.VersionString}",
        };
        Console.WriteLine($"[bench] {names.Count} spots from {path} on {map}, {settle} frames to settle and {frames} measured each; results to {_benchOut}");
        return [.. outArgs];
    }

    /// <summary>A spots file: a path, or a name (with or without .json) in tools/bench/ above the exe.</summary>
    private static string? BenchSpotsFile(string name)
    {
        if (File.Exists(name))
            return Path.GetFullPath(name);
        var file = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        for (var d = new DirectoryInfo(AppFolders.Exe); d != null; d = d.Parent)
            if (Path.Combine(d.FullName, "tools", "bench", file) is var p && File.Exists(p))
                return p;
        return null;
    }

    /// <summary>Where runs go by default: bench-results/ in the repo root above the exe (ignored by git), else beside the logs.</summary>
    private static string BenchResults()
    {
        for (var d = new DirectoryInfo(AppFolders.Exe); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "tools", "bench")))
                return Path.Combine(d.FullName, "bench-results");
        return Path.Combine(Path.GetDirectoryName(AppFolders.LogPath("x"))!, "bench-results");
    }

    /// <summary>A spot was measured: its line, and the run's file written again with it.</summary>
    private static void BenchSpotDone(PerfBench.Spot s, Game? g)
    {
        if (g != null)
        {
            s.Extra["constructs"] = g.Constructs.Bodies.Count;
            s.Extra["players"] = g.Players.Count;
            s.Extra["parts"] = g.Constructs.Bodies.Sum(b => b.PartCount);
            // The construct renderer's last frame: boxes, merged chunks drawn, parts drawn one by one, bodies culled or far.
            s.Extra["cBoxes"] = ConstructRender.LastBoxes;
            s.Extra["cChunks"] = ConstructRender.LastMergedChunks;
            s.Extra["cWalked"] = ConstructRender.LastBatchWalked;
            s.Extra["cCulled"] = ConstructRender.LastCulled;
            s.Extra["cFar"] = ConstructRender.LastLod;
        }
        WsSay(PerfBench.Line(s));
        if (s.FrameSections.Length > 0)
            WsSay($"[bench] {s.Name} sections (ms a frame): {s.FrameSections}");
        foreach (var t in s.SlowTicks)
            WsSay($"[bench] {s.Name} slow {t}");
        if (_benchOut.Length == 0 || _benchMeta == null)
            return;
        _benchMeta["gpu"] = RenderCaps.Renderer;
        _benchMeta["gl"] = RenderCaps.Version;
        _benchMeta["video"] = PerfBench.Video(_settings);
        _benchMeta["window"] = $"{_settings.Width}x{_settings.Height}";
        try
        {
            PerfBench.WriteJson(_benchOut, (JsonObject)_benchMeta.DeepClone());
        }
        catch (Exception e)
        {
            WsSay($"[bench] could not write {_benchOut}: {e.Message}");
        }
    }

    /// <summary>--bench-compare BASE.json NEW.json [--noise PCT]: exit code is the number of spots worse past the noise.</summary>
    private static int BenchCompare(string[] args)
    {
        var at = Array.FindIndex(args, a => a.Equals("--bench-compare", StringComparison.OrdinalIgnoreCase));
        var files = args.Skip(at + 1).TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (files.Length != 2)
        {
            Console.Error.WriteLine("--bench-compare BASE.json NEW.json [--noise PCT]");
            return 2;
        }
        var noise = double.TryParse(args.SkipWhile(a => !a.Equals("--noise", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 5;
        using var o = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true }; // (Console.Out is the log file in this build)
        return PerfBench.Compare(files[0], files[1], noise, o);
    }
}
