using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// <c>--stress-check [--map NAME] [--wrecks N] [--wreck "A,B,..."] [--haulers K] [--hauler NAME] [--alt M] [--seconds S] [--person far|near|none] [--every S] [--no-rails]</c>:
/// what a busy, long-lived world costs, headless. N loose wrecks (copies of the named blueprints, marked as wrecks) and K uncrewed haulers are
/// put in circular orbits round the map's planet (default Caledon), each at its own height and phase, spinning a little, and the world is stepped
/// for S seconds. Every few seconds it prints the tick (mean and worst), how many bodies are awake, how many the physics stepped, and the step's
/// and physics' parts; at the end the cost per body. A person stands on the ground under the orbits (near), on the far side (far) or nowhere.
/// The numbers to compare before and after anything that makes idle or far bodies cheaper (docs/perf/LIMITS.md). A dev tool: it allocates freely.
/// </summary>
public static class StressCheck
{
    public static int Cli(string[] args)
    {
        string Arg(string name, string def) => args.SkipWhile(a => !a.Equals(name, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() ?? def;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var wrecks = int.Parse(Arg("--wrecks", "300"), inv);
        var haulers = int.Parse(Arg("--haulers", "20"), inv);
        var wreckNames = Arg("--wreck", "Cargo Pod,Cargo Rack,Limpet").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var haulerName = Arg("--hauler", "Dray Container Hauler");
        var alt = float.Parse(Arg("--alt", "1500"), inv);
        var seconds = int.Parse(Arg("--seconds", "30"), inv);
        var every = Math.Max(1, int.Parse(Arg("--every", "5"), inv));
        var person = Arg("--person", "near").ToLowerInvariant();
        var rng = new Random(int.Parse(Arg("--seed", "1"), inv));
        ConstructRails.On = !args.Contains("--no-rails"); // (free bodies on rails: ConstructRails.cs)
        if (Maps.Find(Arg("--map", "Caledon")) is not { } map)
        {
            Console.WriteLine("stress: no such map");
            return 1;
        }
        map.RestoreProps();
        World.SetMap(map);
        if (GravityFrames.Point is not { } planet)
        {
            Console.WriteLine($"stress: {map.Name} is not a planet map (no point gravity)");
            return 1;
        }
        var w = new ConstructWorld();
        var centre = planet.Center;
        // The orbits: about the planet's axis, in its equator's plane, from the ground's height under the map's origin.
        var groundR = (ConstructWorld.Ground(World.Query, 0, 0) - centre).Length();
        var axis = Vector3.Normalize(planet.Axis == Vector3.Zero ? Vector3.UnitY : planet.Axis);
        var e1 = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
        var e2 = Vector3.Cross(axis, e1);
        Console.WriteLine($"stress: {map.Name}, planet centre {centre}, ground radius {groundR:0} m, orbits {alt:0} m up and higher; {wrecks} wrecks of {string.Join(", ", wreckNames)}, {haulers} {haulerName}");

        var bps = wreckNames.Select(n => ConstructBuild.FindBlueprint(n) ?? throw new InvalidOperationException($"no blueprint {n}")).ToArray();
        var haulBp = haulers > 0 ? ConstructBuild.FindBlueprint(haulerName) ?? throw new InvalidOperationException($"no blueprint {haulerName}") : null;
        var sw = Stopwatch.StartNew();
        var bodies = new List<Construct>();
        void Place(ConstructBuild.Blueprint bp, int k, int of, bool wreck)
        {
            // Spread round the planet and over a band of heights, so few start touching.
            var r = groundR + alt + (float)rng.NextDouble() * alt;
            var phase = (k + (float)rng.NextDouble() * 0.5f) / of * MathF.Tau;
            var tilt = ((float)rng.NextDouble() - 0.5f) * 0.3f; // (a little out of the plane)
            var dir = Vector3.Normalize(e1 * MathF.Cos(phase) + e2 * MathF.Sin(phase) + axis * tilt);
            var at = centre + dir * r;
            var c = ConstructBuild.Build(w, bp, at, Quaternion.CreateFromYawPitchRoll((float)rng.NextDouble() * 6f, (float)rng.NextDouble() * 6f, 0), out var why);
            if (c == null)
            {
                Console.WriteLine($"stress: {bp.Name} not built: {why}");
                return;
            }
            var g = GravityFrames.GravityAt(c.WorldCom).Length();
            var radial = Vector3.Normalize(c.WorldCom - centre);
            var along = Vector3.Normalize(Vector3.Cross(axis, radial));
            c.Vel = along * MathF.Sqrt(g * (c.WorldCom - centre).Length()); // (a circular orbit at its height)
            c.AngVel = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.2f;
            c.Wreck = wreck;
            c.Wake();
            bodies.Add(c);
        }
        for (var k = 0; k < wrecks; k++)
            Place(bps[k % bps.Length], k, Math.Max(1, wrecks), true);
        for (var k = 0; k < haulers; k++)
            Place(haulBp!, k, Math.Max(1, haulers), false);
        var parts = bodies.Sum(b => b.PartCount);
        Console.WriteLine($"stress: {bodies.Count} bodies, {parts} parts, built in {sw.Elapsed.TotalSeconds:0.0} s; orbital speed about {bodies.FirstOrDefault()?.Vel.Length() ?? 0:0} m/s");

        var under = centre + e1 * groundR;
        w.PeopleCount = person == "none" ? 0 : 1;
        w.People[0] = person == "far" ? centre - e1 * (groundR + 2) : under + e1 * 2; // (the far side of the planet, or under the first orbits)

        var dt = World.TickDt;
        double sum = 0, worst = 0, all = 0, allWorst = 0;
        long simSum = 0;
        var ticks = 0;
        var total = seconds * World.TickRate;
        Array.Clear(w.StepMs);
        Array.Clear(ConstructPhysics.PartMs);
        var b0 = GC.GetAllocatedBytesForCurrentThread();
        for (var t = 1; t <= total; t++)
        {
            var t0 = Stopwatch.GetTimestamp();
            w.Step(World.Query, dt);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            sum += ms;
            worst = Math.Max(worst, ms);
            if (t > World.TickRate)
            {
                all += ms;
                allWorst = Math.Max(allWorst, ms); // (the first second left out: JIT)
            }
            simSum += ConstructPhysics.LastSimulated;
            ticks++;
            if (t % (every * World.TickRate) == 0 || t == total)
            {
                int awake = 0, low = 0, gone = 0;
                foreach (var b in bodies)
                {
                    if (w.Get(b.Id) == null)
                    {
                        gone++;
                        continue;
                    }
                    if (!b.Asleep && !b.Anchored)
                        awake++;
                    if ((b.WorldCom - centre).Length() < groundR + 100)
                        low++;
                }
                var sm = w.StepMs;
                var pm = ConstructPhysics.PartMs;
                Console.WriteLine(FormattableString.Invariant($"stress {t / World.TickRate} s: tick {sum / ticks:0.00} ms (worst {worst:0.0}); bodies {w.Bodies.Count}, awake {awake}, stepped {simSum / (double)ticks:0}, far {ConstructFar.LastFar}, on rails {ConstructRails.Count}, fallen low {low}, gone {gone}; step parts {string.Join(" ", sm.Select(x => (x / ticks).ToString("0.00", inv)))}; physics (list, bodies, contacts, solve, rest) {string.Join(" ", pm.Select(x => (x / ticks).ToString("0.00", inv)))}"));
                sum = worst = 0;
                simSum = 0;
                ticks = 0;
                Array.Clear(w.StepMs);
                Array.Clear(ConstructPhysics.PartMs);
            }
        }
        // What is awake, by kind, and where a tick's allocations go (one more second, marked).
        foreach (var g in w.Bodies.GroupBy(b => (b.DefOf(0).Name, b.Wreck, b.Asleep, b.Anchored, b.Framed)).OrderByDescending(g => g.Count()).Take(12))
            Console.WriteLine(FormattableString.Invariant($"stress: {g.Count()} x {g.Key.Name} wreck {g.Key.Wreck} asleep {g.Key.Asleep} anchored {g.Key.Anchored} framed {g.Key.Framed}, speed {g.Average(b => b.Vel.Length()):0}, parts {g.Average(b => b.PartCount):0}"));
        AllocProfile.On = true;
        AllocProfile.Reset();
        for (var t = 0; t < World.TickRate; t++)
        {
            AllocProfile.Mark("stress: tick start");
            w.Step(World.Query, dt);
            AllocProfile.Stop();
        }
        AllocProfile.On = false;
        Console.WriteLine($"stress: allocations a tick by section: {AllocProfile.Top(8, World.TickRate)}");
        Console.WriteLine($"stress: time a tick by section: {AllocProfile.TopTime(10, World.TickRate)}");
        var steady = total - World.TickRate;
        var mean = all / Math.Max(1, steady);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
        Console.WriteLine(FormattableString.Invariant($"stress: {bodies.Count} bodies ({parts} parts), person {person}: {mean:0.00} ms a tick (worst {allWorst:0.0}), {mean * 1000 / Math.Max(1, bodies.Count):0.0} us a body, {bytes / (double)total / 1024:0.0} KB allocated a tick; the 8 ms budget holds about {8 / Math.Max(1e-6, mean / Math.Max(1, bodies.Count)):0} such bodies"));
        return 0;
    }
}
