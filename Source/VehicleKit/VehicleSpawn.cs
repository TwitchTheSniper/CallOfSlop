using System.Diagnostics;
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

    /// <summary>When done: the blueprint (and what it tows, when the tow isn't a committed file), or why not. Set on the game's thread.</summary>
    public ConstructBuild.Blueprint? Blueprint, Tow;
    public string Error = "";

    // (what the generation thread writes; copied to the fields above only after it has handed back)
    private ConstructBuild.Blueprint? _bp, _tow;
    private string _error = "";
    private bool _finished;

    /// <summary>The text's FNV-1a hash: the same seed and build give the same one on any machine (the kit allows nothing that varies).</summary>
    public ulong Hash;

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

    public VehicleGenJob(IVehicleGenerator generator, int seed)
    {
        Generator = generator;
        Seed = seed;
    }

    public string Name => VehicleRegistry.BlueprintName(Generator, Seed);

    /// <summary>
    /// A point where a generation may hand back to the game (between the game's own build calls, never inside one). Only the
    /// generation thread ever stops here, and only once its slice is spent.
    /// </summary>
    public static void Yield()
    {
        if (_current is { } j && Stopwatch.GetTimestamp() >= j._until)
            j.Pause();
    }

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
        if (_finished)
        {
            Blueprint = _bp;
            Tow = _tow;
            Error = _error;
            Done = true;
        }
        return Done;
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
            _current = null;
            _finished = true;
            _back.Release();
        }
    }

    private void Generate()
    {
        var v = VehicleRegistry.Make(Generator, Seed);
        var text = v.Text();
        Hash = Fnv(text);
        Yield();
        var bp = ConstructBuild.ParseBlueprint(text, VehicleRegistry.FileName(Generator, Seed));
        if (bp.Tow.Length > 0 && ConstructBuild.FindBlueprint(bp.Tow) == null)
        {
            // A tow that isn't a committed file (a style that ships nothing): made by its own generator, as the check does.
            var at = bp.Tow.LastIndexOf(' ');
            if (at <= 0 || !int.TryParse(bp.Tow[(at + 1)..], out var towSeed) || VehicleRegistry.Find(bp.Tow[..at]) is not { } tg)
                throw new VehicleKitException($"it tows \"{bp.Tow}\", which is neither a blueprint nor a generator's seed");
            Yield();
            _tow = ConstructBuild.ParseBlueprint(VehicleRegistry.Run(tg, towSeed), VehicleRegistry.FileName(tg, towSeed));
        }
        _bp = bp;
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
