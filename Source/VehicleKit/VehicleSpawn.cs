using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// One generation in the game (docs/vehiclekit/VEHICLEKIT.md, "Dynamic spawn"): a generator run for a seed on a thread of its own, but
/// <b>never at the same time as the game</b>. The thread only runs inside <see cref="Step"/>, while the tick that called it waits, and
/// hands back at the next <see cref="Yield"/> once its slice is spent; so everything the game shares between constructs (the snap
/// build's caches, the weapon builder, the world statics <c>World.Enter</c> swaps) is only ever touched by one thread at a time, as in
/// the command line, and a seed writes the same text here as in <c>--gen-vehicle</c>. The kit's own steps call <see cref="Yield"/>
/// (<see cref="BlueprintScript"/>); anywhere else it costs one thread-static read.
/// </summary>
public sealed class VehicleGenJob
{
    /// <summary>How long a slice may run past its end before the game stops waiting and abandons the job (a generator stuck between yields).</summary>
    public const int StuckMs = 10000;

    public readonly IVehicleGenerator Generator;
    public readonly int Seed;

    /// <summary>Kit 3's layer seeds the request asked for (<see cref="VehicleLayers"/>); none: every layer is the seed's.</summary>
    public readonly VehicleLayers Layers;

    /// <summary>When done: the blueprint (and what it tows, when the tow isn't a committed file), or why not. Set on the game's thread.</summary>
    public ConstructBuild.Blueprint? Blueprint, Tow;
    public string Error = "";

    /// <summary>When done: the vehicle's behaviour brief (VehicleBrief.cs), its parts by index on the body the blueprint builds; null when it couldn't be made (logged).</summary>
    public VehicleBrief? Brief;

    // (what the generation thread writes; copied to the fields above only after it has handed back)
    private VehicleBrief? _brief;
    private ConstructBuild.Blueprint? _bp, _tow;
    private string _error = "";
    private bool _finished;

    /// <summary>The text's FNV-1a hash: the same seed and build give the same one on any machine (the kit allows nothing that varies).</summary>
    public ulong Hash;

    /// <summary>Which kit made it (<see cref="VehicleKitVersion"/>) and the grammar's modules, as in the blueprint's kit line; logged when it is done.</summary>
    public int Kit;

    public string Modules = "";

    /// <summary>Done (built or failed): <see cref="Step"/> returns true from then on.</summary>
    public bool Done { get; private set; }

    /// <summary>What it cost: the slices' time on the tick, the longest slice, how many.</summary>
    public double CpuMs, WorstSliceMs;
    public int Slices;

    private Thread? _thread;
    private readonly SemaphoreSlim _go = new(0, 1), _back = new(0, 1);
    private long _until;
    private bool _abandon;

    [ThreadStatic]
    private static VehicleGenJob? _current;

    /// <summary>Kit 4's role and tier the request asked for (VehicleRoles.cs); null: by shape, as before.</summary>
    public readonly RoleAsk? Role;

    /// <summary>Where it is being made for (phase 7: VehicleContext.cs); null or neutral: as every vehicle before.</summary>
    public readonly SpawnContext? Context;

    /// <summary>The spawn context's line, as in the blueprint ("desert, gravity 1.00, ...; changed ..."); "" in a neutral one.</summary>
    public string ContextNotes = "";

    public VehicleGenJob(IVehicleGenerator generator, int seed, VehicleLayers layers = default, RoleAsk? role = null, SpawnContext? context = null)
    {
        Generator = generator;
        Seed = seed;
        Layers = layers;
        Role = role;
        Context = context is { Matters: true } ? context : null;
    }

    public string Name => _made.Length > 0 ? _made : VehicleRegistry.BlueprintName(Generator, Seed) + Layers.Suffix + (Role?.Suffix ?? "") + (Context?.Suffix ?? "");

    private string _made = ""; // (the vehicle's own name once made: a kit-1 shape's role may have taken a seed derived from the one asked)

    /// <summary>Kit 4: "role raider, tier 2, shape Box Truck, role met" and what it measured, as in the kit line; empty without a role.</summary>
    public string RoleNotes = "";

    /// <summary>How far the vehicle reaches ahead of its origin and behind it (m), once made (<see cref="Vehicle.ExtentZ"/>).</summary>
    public float Front, Rear;

    /// <summary>
    /// A point where a generation may hand back to the game (between the game's own build calls, never inside one). Only the
    /// generation thread ever stops here, and only once its slice is spent. The compiler fills in the caller's file and line (constants: nothing is
    /// allocated), so a span that runs long is named by the two yields either side of it (<see cref="OverrunMs"/>).
    /// </summary>
    public static void Yield([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_current is { } j)
            j.At(file, line);
    }

    /// <summary>A slice past this (ms) is logged with the span that ran it over, by call site: a step between two yields that has grown shows here.</summary>
    public const double OverrunMs = 4;

    /// <summary>The longest span between two yields (ms) in the whole generation, and where it ran: "VehicleCore.cs:182 to VehicleCore.cs:157".</summary>
    public double WorstSpanMs;

    public string WorstSpanAt => $"{Site(_worstFrom, _worstFromLine)} to {Site(_worstTo, _worstToLine)}";

    private string _worstFrom = "", _worstTo = "";
    private int _worstFromLine, _worstToLine;

    // (the span running now: when it started, at which yield; the slice's longest so far)
    private long _spanStart, _sliceSpan, _sliceRun;
    private string _lastFile = "the start", _sliceFrom = "", _sliceTo = "";
    private int _lastLine, _sliceFromLine, _sliceToLine;
    private static int _overruns;
    private double _budgetMs;

    private void At(string file, int line)
    {
        Note(file, line, Stopwatch.GetTimestamp());
        if (Stopwatch.GetTimestamp() >= _until)
            Pause();
        _spanStart = Stopwatch.GetTimestamp();
    }

    /// <summary>A span ends at a yield (or the generation's end): the slice's longest is kept, with both ends' call sites. Allocates nothing.</summary>
    private void Note(string file, int line, long now)
    {
        var span = now - _spanStart;
        _sliceRun += span;
        if (span > _sliceSpan)
            (_sliceSpan, _sliceFrom, _sliceFromLine, _sliceTo, _sliceToLine) = (span, _lastFile, _lastLine, file, line);
        (_lastFile, _lastLine) = (file, line);
    }

    private static string Site(string file, int line) => line > 0 ? $"{Path.GetFileName(file)}:{line}" : file;

    private void Pause()
    {
        _back.Release();
        _go.Wait();
        if (_abandon)
            throw new OperationCanceledException("the generation was abandoned");
    }

    /// <summary>
    /// Runs the generation for about <paramref name="budgetMs"/> on its thread while this one waits. True once it is done
    /// (<see cref="Blueprint"/> or <see cref="Error"/>). Allocates nothing once started.
    /// </summary>
    public bool Step(double budgetMs)
    {
        if (Done)
            return true;
        if (_thread == null)
        {
            _thread = new Thread(Body, 16 << 20) { IsBackground = true, Name = "Vehicle kit" };
            _thread.Start();
        }
        var start = Stopwatch.GetTimestamp();
        _until = start + (long)(budgetMs * Stopwatch.Frequency / 1000);
        _sliceSpan = _sliceRun = 0;
        _budgetMs = budgetMs;
        var gc = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
        _go.Release();
        if (!_back.Wait(StuckMs + (int)budgetMs))
        {
            // Stuck between yields: the game goes on and the job is dropped at its next yield (logged; the caller falls back).
            _abandon = true;
            _go.Release(); // (so its next yield ends it)
            Error = $"no yield in {StuckMs} ms: abandoned";
            Done = true;
            return true;
        }
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        CpuMs += ms;
        WorstSliceMs = Math.Max(WorstSliceMs, ms);
        Slices++;
        var spanMs = _sliceSpan * 1000.0 / Stopwatch.Frequency;
        if (spanMs > WorstSpanMs)
        {
            WorstSpanMs = spanMs; // (the sites are kept, not written: the tick allocates nothing)
            (_worstFrom, _worstFromLine, _worstTo, _worstToLine) = (_sliceFrom, _sliceFromLine, _sliceTo, _sliceToLine);
        }
        if (ms > OverrunMs)
            Overran(ms, spanMs, _sliceRun * 1000.0 / Stopwatch.Frequency, GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2) != gc);
        if (_finished)
        {
            Blueprint = _bp;
            Tow = _tow;
            Brief = _bp != null ? _brief : null;
            Error = _error;
            Done = true;
        }
        return Done;
    }

    private string SpanAt() => $"{Site(_sliceFrom, _sliceFromLine)} to {Site(_sliceTo, _sliceToLine)}";

    /// <summary>
    /// A slice ran past <see cref="OverrunMs"/>: logged (the first 10, then every 100th) with its longest span and that span's two yields, and whether a
    /// garbage collection ran in it. A span of its own size is a step that needs a yield inside it; a short span in a long slice is the thread waking late
    /// (a busy machine) or the collection.
    /// </summary>
    private void Overran(double ms, double spanMs, double runMs, bool collected)
    {
        if (++_overruns > 10 && _overruns % 100 != 0)
            return;
        Console.WriteLine($"[vehicles] slice {ms:0.0} ms in {Name} (budget {_budgetMs:0.0}, the kit ran {runMs:0.0} of it): longest span {spanMs:0.0} ms, {SpanAt()}{(collected ? ", a garbage collection ran" : "")} ({_overruns} so far)");
    }

    /// <summary>Drops an unfinished job: its thread wakes, unwinds from where it waited and ends, before this returns.</summary>
    public void Abandon()
    {
        if (Done || _thread == null)
            return;
        _abandon = true;
        _go.Release();
        _back.Wait(StuckMs);
    }

    private void Body()
    {
        _go.Wait();
        _current = this;
        _spanStart = Stopwatch.GetTimestamp();
        try
        {
            if (!_abandon)
                Generate();
        }
        catch (OperationCanceledException) when (_abandon)
        {
            _error = "abandoned";
        }
        catch (Exception ex)
        {
            _error = ex is VehicleKitException or ConstructPartException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
            _bp = _tow = null;
        }
        finally
        {
            Note("the end", 0, Stopwatch.GetTimestamp());
            _current = null;
            _finished = true;
            _back.Release();
        }
    }

    private void Generate()
    {
        var v = VehicleRegistry.Make(Generator, Seed, layers: Layers, role: Role, context: Context);
        Yield();
        var text = v.Text();
        ContextNotes = v.Context is { Matters: true } c ? $"{c.Detail}; {(v.ContextNotes.Count > 0 ? "changed " + string.Join(", ", v.ContextNotes) : "nothing to change")}" : "";
        Yield();
        Hash = Fnv(text);
        (Kit, Modules, RoleNotes, _made) = (v.Kit, string.Join(", ", v.Modules), string.Join(", ", v.KitNotes), v.Name);
        (Front, Rear) = v.ExtentZ(); // (a convoy spaces its members by these)
        Yield();
        var bp = ConstructBuild.ParseBlueprint(text, VehicleRegistry.FileName(Generator, Seed));
        if (bp.Tow.Length > 0 && ConstructBuild.FindBlueprint(bp.Tow) == null)
        {
            // A tow that isn't a committed file (a style that ships nothing): made by its own generator, as the check does.
            var at = bp.Tow.LastIndexOf(' ');
            if (at <= 0 || !int.TryParse(bp.Tow[(at + 1)..], out var towSeed) || VehicleRegistry.Find(bp.Tow[..at]) is not { } tg)
                throw new VehicleKitException($"it tows \"{bp.Tow}\", which is neither a blueprint nor a generator's seed");
            Yield();
            _tow = ConstructBuild.ParseBlueprint(VehicleRegistry.Run(tg, towSeed, new VehicleLayers()), VehicleRegistry.FileName(tg, towSeed));
        }
        _brief = MakeBrief(v, text);
        _bp = bp;
    }

    /// <summary>The behaviour brief (VehicleBriefMake.cs), in the generation's slices like the rest. Never a reason for the vehicle not to spawn: a failure is logged and it has none.</summary>
    private VehicleBrief? MakeBrief(Vehicle v, string text)
    {
        if (!BriefsOn)
            return null;
        try
        {
            Yield();
            return VehicleBriefMake.ForGenerated(v, text, VehicleSpawnKit.Names(Generator).Shape);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[vehicles] {v.Name}: no behaviour brief ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    /// <summary>Make a behaviour brief with each generated vehicle (on; off only to measure what it costs).</summary>
    public static bool BriefsOn = true;

    private static bool _warm;

    /// <summary>What the warm-up cost (ms), once it has run.</summary>
    public static double WarmMs { get; private set; }

    /// <summary>
    /// The first generation in a process pays for compiling the kit and the build code it calls: about a second, a few milliseconds at a time over its first
    /// ticks and up to 40 to 80 ms in one span (the first rebuild of a construct with guns). This runs a small one once, at once, on the calling thread: a Bandit Technical in a world
    /// of its own, written, hashed, read back and measured as a spawn's is, so every path a spawn takes is compiled before the first one. The server calls it
    /// when a Sandbox match starts, not on a tick in play. Nothing it makes is kept; a failure is only logged.
    /// </summary>
    public static void Warm()
    {
        if (_warm)
            return;
        _warm = true;
        var start = Stopwatch.GetTimestamp();
        try
        {
            if (VehicleRegistry.Find("Bandit Technical") is { } g && g.Seeds.Count > 0)
            {
                var v = VehicleRegistry.Make(g, g.Seeds[0]);
                var text = v.Text();
                _ = Fnv(text);
                _ = ConstructBuild.ParseBlueprint(text, VehicleRegistry.FileName(g, g.Seeds[0]));
                _ = v.Stability();
                _ = v.Fill();
            }
            if (VehicleRegistry.Find("Bandit Box Truck") is { } grammar)
                ShapeGrammar.PlanOnly(grammar, 1); // (the shape grammar's planner: a plan, nothing built)
        }
        catch (Exception ex) // (only a warm-up: never a reason for a match not to start)
        {
            Console.WriteLine($"[vehicles] warm-up failed: {ex.GetType().Name}: {ex.Message}");
        }
        WarmMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"[vehicles] warmed the kit in {WarmMs:0} ms");
    }

    public static ulong Fnv(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(text))
            h = (h ^ b) * 1099511628211UL;
        return h;
    }
}

/// <summary>The kit's side of a dynamic spawn: which generator, which seeds, and what to fall back to (docs/vehiclekit/VEHICLEKIT.md, "Dynamic spawn").</summary>
public static class VehicleSpawnKit
{
    /// <summary>Seeds the server picks, and rerolls, are in 1..<see cref="MaxSeed"/> (committed seeds are small: they never need to avoid them).</summary>
    public const int MaxSeed = 999_999;

    /// <summary>A generator by its style's and shape's names ("Bandit", "Box Truck"); any style, extras included.</summary>
    public static IVehicleGenerator? Find(string style, string shape) =>
        style.Length == 0 || shape.Length == 0 ? null : VehicleRegistry.Find($"{style} {shape}");

    /// <summary>A generator's style and shape names, from a seed's blueprint name ("Bandit Box Truck 7"): false for anything else.</summary>
    public static bool FromBlueprintName(string name, out IVehicleGenerator? g, out int seed)
    {
        g = null;
        seed = 0;
        var at = name.LastIndexOf(' ');
        if (at <= 0 || !int.TryParse(name.AsSpan(at + 1), out seed))
            return false;
        g = VehicleRegistry.Find(name[..at]);
        return g != null;
    }

    /// <summary>The style's and the shape's names of a generator.</summary>
    public static (string Style, string Shape) Names(IVehicleGenerator g) =>
        (g.Style.Name, g is VehicleShape v ? v.Shape : g.Name[(g.Style.Name.Length + 1)..]);

    /// <summary>The <paramref name="attempt"/>th reroll of a seed that failed: the same seed always rerolls the same way.</summary>
    public static int Reroll(int seed, int attempt) => 1 + (int)(Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)attempt) % MaxSeed);

    /// <summary>The next seed from a server's own sequence (<paramref name="state"/>: any start; it steps).</summary>
    public static int Next(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        return 1 + (int)(Mix(state) % MaxSeed);
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// A committed seed's blueprint of this style and shape: <paramref name="seed"/>'s own when it ships, else one of the shipped seeds
    /// chosen by it. Null when the style ships no files (Militia).
    /// </summary>
    public static ConstructBuild.Blueprint? Committed(IVehicleGenerator g, int seed)
    {
        var seeds = g.Seeds;
        if (seeds.Contains(seed) && ConstructBuild.FindBlueprint(VehicleRegistry.BlueprintName(g, seed)) is { } own)
            return own;
        for (var k = 0; k < seeds.Count; k++)
            if (ConstructBuild.FindBlueprint(VehicleRegistry.BlueprintName(g, seeds[(int)((uint)(seed + k) % (uint)seeds.Count)])) is { } bp)
                return bp;
        return null;
    }
}
