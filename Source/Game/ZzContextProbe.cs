using System.Numerics;
using CallOfSlop.VehicleKit;

namespace CallOfSlop;

/// <summary>Throwaway probe (removed before finishing).</summary>
public static class ZzContextProbe
{
    public static int Run(string[] names)
    {
        foreach (var name in names)
        {
            if (Maps.Find(name) is not { } map)
            {
                Console.WriteLine($"probe: no map {name}");
                continue;
            }
            World.SetMap(map);
            var counts = new Dictionary<string, int>();
            var ex = new Dictionary<string, string>();
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var n = 0;
            for (var x = -map.HalfX * 0.9f; x <= map.HalfX * 0.9f; x += map.HalfX * 0.18f)
                for (var z = -map.HalfZ * 0.9f; z <= map.HalfZ * 0.9f; z += map.HalfZ * 0.18f)
                {
                    var g = ConstructWorld.Ground(World.Query, new Vector3(x, 0, z));
                    var c = SpawnContexts.At(g);
                    n++;
                    var k = c.Matters ? c.Label : "neutral";
                    counts[k] = counts.GetValueOrDefault(k) + 1;
                    ex.TryAdd(k, $"{c.Detail} ({c.Read}) at {g.X:0},{g.Y:0},{g.Z:0}");
                }
            var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.WriteLine($"probe {map.Name}: {n} points, {ms / n:0.000} ms each: {string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}"))}");
            foreach (var kv in ex)
                Console.WriteLine($"  {kv.Key}: {kv.Value}");
        }
        // A context through the sliced job: the same hash as a direct run.
        var ctx = new SpawnContext { Biome = Biome.Desert, Ore = 1 };
        var gen = VehicleRegistry.Find("Bandit Box Truck")!;
        var direct = VehicleRegistry.Make(gen, 4711, context: ctx).Text();
        var job = new VehicleGenJob(gen, 4711, default, null, ctx);
        while (!job.Step(2.5)) { }
        Console.WriteLine($"probe job: {job.Name}, {(job.Blueprint == null ? "FAILED " + job.Error : job.Hash == VehicleGenJob.Fnv(direct) ? "the same hash" : "A DIFFERENT HASH")} over {job.Slices} slices, worst slice {job.WorstSliceMs:0.0} ms, span {job.WorstSpanMs:0.0} ms ({job.WorstSpanAt}); {job.ContextNotes}");
        foreach (var seed in new[] { 4712, 4713, 4714 })
        {
            var j2 = new VehicleGenJob(gen, seed, default, null, ctx);
            while (!j2.Step(2.5)) { }
            Console.WriteLine($"probe warm job: {j2.Name}, {j2.CpuMs:0} ms over {j2.Slices} slices, worst slice {j2.WorstSliceMs:0.0} ms, span {j2.WorstSpanMs:0.0} ms ({j2.WorstSpanAt}); drive check so far {VehicleDriveCheck.Checked} checked, {VehicleDriveCheck.TotalMs / Math.Max(1, VehicleDriveCheck.Checked):0} ms mean");
        }
        return 0;
    }
}
