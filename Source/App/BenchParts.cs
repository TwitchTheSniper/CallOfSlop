using System.Diagnostics;
using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>
/// <c>--bench-parts [N] [--no-sweep] [--net K] [--lag MS] [--loss PCT]</c>: run-time parts and part shapes over a long run
/// (RuntimeParts.cs; docs/sandbox/CONSTRUCTS.md, "Run-time parts and their slots"). A command-line check, not a self-test section.
/// Offline: N generated vehicles (every shipped shape in turn, new seeds), each built into a world and taken out again, a sweep
/// every ten, the tables' use printed as it goes (--no-sweep: how fast they fill without). Then, unless --net 0, a host and a
/// client over RakNet with the client's network made bad: K generated vehicles spawned on the host and despawned, with both
/// tables' limits lowered so freed slots are taken again during the run; each copy the client gets must name the same parts as
/// the host's, and a vehicle kept the whole time must keep its parts on both. Exit code: the problems.
/// </summary>
public static partial class SelfTest
{
    public static int BenchParts(string[] args)
    {
        string? Arg(string k) => args.SkipWhile(a => !a.Equals(k, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var n = int.TryParse(Arg("--bench-parts"), out var want) ? want : 200;
        var sweep = !args.Any(a => a.Equals("--no-sweep", StringComparison.OrdinalIgnoreCase));
        var net = int.TryParse(Arg("--net"), out var k) ? k : 40;
        var lag = int.TryParse(Arg("--lag"), out var l) ? l : 120;
        var loss = float.TryParse(Arg("--loss"), System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 3f;
        var fails = Soak(n, sweep);
        if (net > 0)
            fails += SoakOnline(net, lag, loss);
        Console.WriteLine($"[bench-parts] done: {fails} problem(s); {RuntimeParts.Line()}");
        return fails;
    }

    private static int Soak(int n, bool sweep)
    {
        var gens = VehicleRegistry.All;
        var w = new ConstructWorld();
        Console.WriteLine($"[bench-parts] {ConstructParts.All.Count} part files loaded: {RuntimeParts.Line()}");
        var clock = Stopwatch.StartNew();
        int built = 0, failed = 0, mostShapes = 0, mostDefs = 0;
        var (shapes0, defs0) = (PartShapes.Live, Extrusion.Live);
        for (var i = 0; i < n; i++)
        {
            var g = gens[i % gens.Count];
            var seed = 1000 + i / gens.Count;
            if (Spawn(w, g, seed, Vector3.Zero, out var why) is { } c)
            {
                built++;
                w.Remove(c);
            }
            else
            {
                failed++;
                Console.WriteLine($"[bench-parts] {g.Name} {seed}: {why}");
            }
            if (sweep && i % 10 == 9)
                RuntimeParts.Sweep(0);
            mostShapes = Math.Max(mostShapes, PartShapes.Live);
            mostDefs = Math.Max(mostDefs, Extrusion.Live);
            if (i % 25 == 24 || i == n - 1)
                Console.WriteLine($"[bench-parts] {i + 1} vehicles ({clock.Elapsed.TotalSeconds:0} s): {RuntimeParts.Line()}");
        }
        Console.WriteLine($"[bench-parts] offline: {built} built and taken out, {failed} not built; started at {shapes0} shapes and {defs0} run-time parts, "
                          + $"most held {mostShapes} shapes and {mostDefs} parts; {(PartShapes.Live - shapes0) / (float)Math.Max(1, n):0.0} shapes and {(Extrusion.Live - defs0) / (float)Math.Max(1, n):0.0} parts a vehicle left held");
        // Bounded: with sweeps, what is held after the last one is what the next vehicles need, nowhere near the limits.
        return sweep && (PartShapes.Live > PartShapes.Limit / 2 || Extrusion.Live > Extrusion.Limit / 2) ? 1 : 0;
    }

    // A generated vehicle built into a world (null: why).
    private static Construct? Spawn(ConstructWorld w, IVehicleGenerator g, int seed, Vector3 at, out string why)
    {
        try
        {
            var text = VehicleRegistry.Make(g, seed).Text();
            var bp = ConstructBuild.ParseBlueprint(text, $"{g.Name} {seed}");
            return ConstructBuild.BuildOnGround(w, bp, at, 0, out why);
        }
        catch (Exception ex) when (ex is VehicleKitException or ConstructPartException)
        {
            why = ex.Message;
            return null;
        }
    }

    private static int SoakOnline(int count, int lag, float loss)
    {
        Brutal.GlfwApi.Glfw.Init();
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Console.WriteLine("[bench-parts net] no yard map");
            return 1;
        }
        const ushort port = 26394;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Console.WriteLine($"[bench-parts net] simulating {lag} ms and {loss}% loss on the client: {a.Net.SimulateBadNetwork(lag, lag / 5, loss)}");
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 60 && (server.Phase != MatchPhase.Playing || a.LocalId == byte.MaxValue); wait++)
            Pump(server, [a], 0.25, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Console.WriteLine("[bench-parts net] the client didn't join");
            return 1;
        }
        // Lower limits, so the run wraps round both tables: freed slots are taken again while the kept vehicle and the client's copies live.
        RuntimeParts.Sweep(0);
        var (keepShapes, keepDefs) = (PartShapes.Limit, Extrusion.Limit);
        PartShapes.Limit = Math.Min(PartShapes.Cap, PartShapes.Count + 2000);
        Extrusion.Limit = Math.Min(Extrusion.MaxDefs, Extrusion.DefCount + 150);
        var fails = 0;
        try
        {
            var w = server.Constructs;
            var gens = VehicleRegistry.All;
            var basePos = sp.Position + new Vector3(0, 0, 30);
            bool Wait(Func<bool> ok)
            {
                for (var t = 0; t < 80 && !ok(); t++)
                    Pump(server, [a], 0.1, ref now, clock);
                return ok();
            }
            bool Same(Construct c) => a.Constructs.Get(c.Id) is { } k && k.PartCount == c.PartCount && Names(k) == Names(c);
            var kept = Spawn(w, gens[0], 2000, sp.Position + new Vector3(0, 0, -25), out var keptWhy);
            if (kept != null)
                kept.Pinned = true;
            if (kept == null || !Wait(() => Same(kept)))
            {
                Console.WriteLine($"[bench-parts net] the kept vehicle: {(kept == null ? keptWhy : "the client's copy differs")}");
                return 1;
            }
            var keptNames = Names(kept);
            int wrapsShapes = 0, wrapsDefs = 0, shown = 0;
            for (var i = 0; i < count; i++)
            {
                var g = gens[(i + 1) % gens.Count];
                var seed = 2001 + i;
                var (shapesBefore, defsBefore) = (PartShapes.Count, Extrusion.DefCount);
                if (Spawn(w, g, seed, basePos, out var why) is not { } c)
                {
                    Console.WriteLine($"[bench-parts net] {g.Name} {seed}: not built, {why}");
                    continue;
                }
                wrapsShapes += PartShapes.Count == shapesBefore && PartShapes.Count >= PartShapes.Limit ? 1 : 0;
                wrapsDefs += Extrusion.DefCount == defsBefore && Extrusion.DefCount >= Extrusion.Limit ? 1 : 0;
                var same = Wait(() => Same(c));
                if (!same)
                {
                    fails++;
                    Console.WriteLine($"[bench-parts net] {g.Name} {seed}: the client's copy {(a.Constructs.Get(c.Id) is { } k ? $"has {k.PartCount} parts, the host's {c.PartCount}, names {(Names(k) == Names(c) ? "alike" : "differ")}" : "never came")}");
                }
                w.Remove(c);
                Wait(() => a.Constructs.Get(c.Id) == null);
                RuntimeParts.Sweep(0);
                if (Names(kept) != keptNames || a.Constructs.Get(kept.Id) is not { } kc || Names(kc) != keptNames)
                {
                    fails++;
                    var freedHeld = 0;
                    for (var q = 0; q < kept.PartCount; q++)
                        freedHeld += kept.DefOf(q).Reclaimed ? 1 : 0;
                    Console.WriteLine($"[bench-parts net] after {g.Name} {seed}: the kept vehicle's parts changed: host {kept.PartCount} parts (was {keptNames.Split('|').Length}), {freedHeld} of their parts freed, alive {w.Get(kept.Id) != null} at {kept.Pos}, client {(a.Constructs.Get(kept.Id) is { } kk ? kk.PartCount + " parts" : "no copy")}");
                }
                if (shown++ % 10 == 9 || i == count - 1)
                    Console.WriteLine($"[bench-parts net] {i + 1} spawned and gone ({clock.Elapsed.TotalSeconds:0} s): {RuntimeParts.Line()}; copies {(same ? "match" : "DIFFER")}");
            }
            Console.WriteLine($"[bench-parts net] slots taken again: shapes on {wrapsShapes} spawns, run-time parts on {wrapsDefs}; "
                              + $"{fails} problem(s), {NetSafety.Dropped} packets dropped{(NetSafety.LastDrop.Length > 0 ? ": " + NetSafety.LastDrop : "")}");
            if (wrapsShapes == 0 && wrapsDefs == 0)
                Console.WriteLine("[bench-parts net] (no slot was taken again: the run didn't wrap round a table)");
        }
        finally
        {
            PartShapes.Limit = keepShapes;
            Extrusion.Limit = keepDefs;
        }
        return fails;
    }
}
