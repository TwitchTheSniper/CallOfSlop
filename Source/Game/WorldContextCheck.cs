using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--two-planetoids [SECONDS]</c>: a headless dev check that two server Games run in one process on different maps
/// (<see cref="WorldContext"/>, docs/space/FREE-TRAVEL.md). A Buggy drops at the pole of the Planetoid and of Bluestone,
/// first each world alone, then both ticking turn about; every body must end exactly where its lone run put it, and each
/// world must see its own map and gravity while entered. Prints one line per world and the cost of the swaps; the exit
/// code is the number of problems.
/// </summary>
public static class WorldContextCheck
{
    private sealed class Side
    {
        public required Game Game;
        public required Map Map;
        public required Planetoid Planet;
        public Construct? Buggy;
        public double Now;
        public readonly List<Vector3> Trace = new(1024), Walk = new(1024); // the buggy's centre and the player's feet, by tick
    }

    public static int Run(string[] args)
    {
        var seconds = 8.0;
        var at = Array.FindIndex(args, a => a.Equals("--two-planetoids", StringComparison.OrdinalIgnoreCase));
        if (at >= 0 && at + 1 < args.Length && double.TryParse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture, out var s))
            seconds = Math.Clamp(s, 1, 60);
        Space.StarSystems.Reload();
        if (Maps.Find("Planetoid") is not { Planetoid: not null } a || Maps.Find("Bluestone") is not { Planetoid: not null } b
            || ConstructBuild.FindBlueprint("Buggy") is not { } bp)
        {
            Console.WriteLine("[two-planetoids] the Planetoid or Bluestone map, or the Buggy blueprint, is missing");
            return 1;
        }
        var problems = new List<string>();
        var ticks = (int)(seconds * World.TickRate);

        // Each alone: the reference.
        var aloneRunA = Open(a, bp, problems);
        var aloneA = Final(aloneRunA, ticks, null, problems);
        aloneRunA.Game.Dispose();
        var aloneRunB = Open(b, bp, problems);
        var aloneB = Final(aloneRunB, ticks, null, problems);
        aloneRunB.Game.Dispose();

        // Both at once, turn about, as one process would run two planetoids.
        var ra = Open(a, bp, problems);
        var rb = Open(b, bp, problems);
        var swaps0 = WorldContext.Swaps;
        var clock = Stopwatch.StartNew();
        var both = Final(ra, ticks, rb, problems);
        var ms = clock.Elapsed.TotalMilliseconds;
        var bothB = Final(rb, 0, null, problems); // (its pose, already ticked turn about)
        var swaps = WorldContext.Swaps - swaps0;

        Compare("Planetoid", aloneA, both, problems);
        Compare("Bluestone", aloneB, bothB, problems);
        Traces("Planetoid", first: aloneRunA, then: ra, problems);
        Traces("Bluestone", first: aloneRunB, then: rb, problems);
        Console.WriteLine($"[two-planetoids] {ticks} ticks each, turn about: {ms:0} ms for both worlds, {swaps} world swaps");
        foreach (var p in problems)
            Console.WriteLine($"[two-planetoids] PROBLEM: {p}");
        Console.WriteLine(problems.Count == 0 ? "[two-planetoids] ok" : $"[two-planetoids] {problems.Count} problem(s)");
        ra.Game.Dispose();
        rb.Game.Dispose();
        return problems.Count;
    }

    private static Side Open(Map map, ConstructBuild.Blueprint bp, List<string> problems)
    {
        Game.SeedNextGame = 7; // (both runs of a world draw the same numbers)
        var g = new Game(NetPeer.Offline(), new Audio(), "Keeper", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        g.Mode = GameMode.Sandbox;
        var r = new Side { Game = g, Map = map, Planet = map.Planetoid! };
        for (var i = 0; i < 600 && g.Phase != MatchPhase.Playing; i++)
            Step(r);
        World.Enter(g.Place);
        foreach (var c in g.Constructs.Bodies.ToList())
            g.Constructs.Remove(c);
        var pl = r.Planet;
        var pad = pl.GroundUnder(pl.Center + new Vector3(15, (float)pl.Radius + 400, -10));
        r.Buggy = ConstructBuild.Build(g.Constructs, bp, pad + new Vector3(0, 3f, 0), Quaternion.Identity, out var why);
        if (r.Buggy == null)
            problems.Add($"{map.Name}: the Buggy didn't build: {why}");
        if (r.Buggy is { } buggy)
        {
            buggy.Vel = new Vector3(9f, 4f, -3f); // (thrown: it tumbles and rolls for a while, not straight to sleep)
            buggy.AngVel = new Vector3(0.8f, 0f, 1.5f);
            buggy.Wake();
        }
        g.RequestRespawn(); // the keeper walks about too (the player mover on curved gravity)
        for (var i = 0; i < 300 && g.Local is not { Alive: true }; i++)
            Step(r);
        if (g.Local is not { Alive: true })
            problems.Add($"{map.Name}: the player didn't spawn");
        g.HeldButtons = Buttons.Forward | Buttons.Sprint;
        return r;
    }

    private static void Step(Side r)
    {
        r.Now += World.TickDt;
        r.Game.Update(r.Now, World.TickDt);
        if (r.Buggy is { } c)
            r.Trace.Add(c.WorldCom);
        if (r.Game.Local is { } me)
            r.Walk.Add(me.Position);
    }

    private readonly record struct Pose(Vector3 At, Quaternion Rot, Vector3 Vel, float Height, Vector3 Down);

    private static Pose Final(Side r, int ticks, Side? other, List<string> problems)
    {
        for (var i = 0; i < ticks; i++)
        {
            Step(r);
            Look(r, problems);
            if (other != null)
            {
                Step(other);
                Look(other, problems);
            }
        }
        World.Enter(r.Game.Place);
        if (r.Buggy is not { } c)
            return default;
        var com = c.WorldCom;
        var ground = r.Planet.GroundUnder(com);
        var height = Vector3.Distance(com, r.Planet.Center) - Vector3.Distance(ground, r.Planet.Center);
        var down = Vector3.Normalize(GravityFrames.GravityAt(com));
        if (other == null && ticks > 0)
            Console.WriteLine($"[two-planetoids] {r.Map.Name} alone: buggy {height:0.00} m over the ground, {c.Vel.Length():0.000} m/s, asleep {c.Asleep}");
        return new Pose(com, c.Rot, c.Vel, height, down);
    }

    /// <summary>While a world is entered, the statics are its own.</summary>
    private static void Look(Side r, List<string> problems)
    {
        if (problems.Count > 8)
            return;
        if (!ReferenceEquals(World.Map, r.Map))
            problems.Add($"{r.Map.Name}: entered, but World.Map is {World.Map.Name}");
        else if (!ReferenceEquals(GravityFrames.Base, r.Map.Gravity) || r.Map.Gravity == null)
            problems.Add($"{r.Map.Name}: entered, but the point gravity is another world's");
        else if (!World.Physics.IsFor(r.Map))
            problems.Add($"{r.Map.Name}: entered, but World.Physics is another map's");
    }

    /// <summary>Every tick of the turn-about run against the lone run: the buggy's path and the player's.</summary>
    private static void Traces(string name, Side first, Side then, List<string> problems)
    {
        static (float Worst, int Count) Worst(List<Vector3> x, List<Vector3> y)
        {
            var worst = 0f;
            var n = Math.Min(x.Count, y.Count);
            for (var i = 0; i < n; i++)
                worst = MathF.Max(worst, Vector3.Distance(x[i], y[i]));
            return (worst, n);
        }
        var (bw, bn) = Worst(first.Trace, then.Trace);
        var (pw, pn) = Worst(first.Walk, then.Walk);
        var travelled = first.Walk.Count > 1 ? Vector3.Distance(first.Walk[0], first.Walk[^1]) : 0;
        var rolled = first.Trace.Count > 1 ? Vector3.Distance(first.Trace[0], first.Trace[^1]) : 0;
        Console.WriteLine($"[two-planetoids] {name}: worst gap by tick: buggy {bw * 1000:0.000} mm over {bn} ticks ({rolled:0.0} m rolled), player {pw * 1000:0.000} mm over {pn} ticks ({travelled:0.0} m walked)");
        if (bn == 0 || pn == 0 || bn != first.Trace.Count || bn != then.Trace.Count || pn != first.Walk.Count || pn != then.Walk.Count)
            problems.Add($"{name}: the runs have different lengths (buggy {first.Trace.Count}/{then.Trace.Count}, player {first.Walk.Count}/{then.Walk.Count})");
        if (bw > 1e-4f || pw > 1e-4f)
            problems.Add($"{name}: turn about differs from alone (buggy {bw * 1000:0.000} mm, player {pw * 1000:0.000} mm)");
        if (travelled < 10)
            problems.Add($"{name}: the player hardly walked ({travelled:0.0} m)");
    }

    private static void Compare(string name, Pose alone, Pose both, List<string> problems)
    {
        var d = Vector3.Distance(alone.At, both.At);
        var dv = Vector3.Distance(alone.Vel, both.Vel);
        Console.WriteLine($"[two-planetoids] {name}: alone vs turn about: {d * 1000:0.000} mm, {dv * 1000:0.000} mm/s apart; resting {both.Height:0.00} m over its own ground");
        if (d > 1e-4f || dv > 1e-4f)
            problems.Add($"{name}: the buggy ended {d * 1000:0.000} mm from where it ended alone");
        if (both.Height is < -0.5f or > 3f)
            problems.Add($"{name}: the buggy isn't resting on its own ground ({both.Height:0.00} m)");
    }
}
