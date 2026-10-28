using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Map loading cost (docs/perf/STREAMING.md): a digest of everything a map load produces, so a faster load path can be
/// proven to give exactly the same maps.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// A 64-bit digest of what loading a map built: every box (exact bits) and colour, every spawn and waypoint (as
    /// validated), every prop instance, the plant count, the fingerprint and its parts. Two loads with the same digest
    /// built the same map.
    /// </summary>
    internal static ulong MapDigest(Map m)
    {
        var h = 14695981039346656037ul;
        void U(uint v)
        {
            for (var i = 0; i < 4; i++)
            {
                h ^= (v >> (i * 8)) & 0xFF;
                h *= 1099511628211ul;
            }
        }
        void F(float f) => U(BitConverter.SingleToUInt32Bits(f));
        void V(Vector3 v)
        {
            F(v.X);
            F(v.Y);
            F(v.Z);
        }
        foreach (var ch in m.Name)
            U(ch);
        U(m.ContentHash);
        U(m.PropHash);
        U(m.FoliageHash);
        U((uint)m.Solid.Count);
        foreach (var b in m.Solid)
        {
            V(b.Center);
            V(b.Half);
        }
        foreach (var c in m.Colors)
            V(c);
        U((uint)m.Spawns.Count);
        foreach (var s in m.Spawns)
            V(s);
        U((uint)m.Waypoints.Count);
        foreach (var w in m.Waypoints)
            V(w);
        U((uint)m.PropInstances.Count);
        foreach (var p in m.PropInstances)
        {
            foreach (var ch in p.Def.Name)
                U(ch);
            V(p.Position);
            F(p.Yaw);
            F(p.Scale);
            F(p.Rand);
            U((uint)p.SolidStart);
            U((uint)p.SolidCount);
        }
        U((uint)m.FoliageInstances.Length);
        U((uint)m.ClutterStart);
        return h;
    }

    // Budgets. Generous against the CI runner's noise (the loads are measured warm, best of two), tight against the
    // quadratic paths this section exists for: those were 5-30x over (docs/perf/STREAMING.md, "Load-path fixes").
    private const double BigMapLoadBudgetMs = 2000; // any one shipped map, warm (Vuorikoski: ~0.3 s on a desktop)
    private const double AllMapsLoadBudgetMs = 15000; // every shipped map, warm, as the game loads them
    private const double ValidateBudgetMs = 150; // any one shipped map, warm (London was 0.35 s on a desktop before the fixes, 6 ms after), without its light bake
    private const double LightBakeBudgetMs = 3000; // a lit map's light bake (docs/render/LIGHTING.md; Ashgrave Cathedral: ~0.35 s on CI's 4 cores); released builds ship it baked
    private const double StressLoadBudgetMs = 600; // the 10,000 + 2,000-prop, 400 x 400 m stress map (~70 ms on a desktop; the old paths took seconds)
    private const double StressScalingBudget = 8; // 5x the props on the same yard: linear is under 5, quadratic was 20 or more
    private const int BoxCellBudget = 400; // 24 m cells of map boxes per map: the box draw calls with everything in view apart (Downtown: 57)
    private const int PropBudget = 65535; // the prop index is a U16 on the wire (GameProps.cs)

    /// <summary>
    /// "Big maps": what loading costs, so it can't creep back up. Every shipped map loads the same in parallel as one by
    /// one (digests, ids, order); each stays under a load-time budget, a box-cell budget and the prop and decal limits;
    /// the stress map (tools/perf/stress_grid.py, built here in memory) loads within its budget and scales linearly in
    /// props; a breakdown of the biggest maps is printed.
    /// </summary>
    private static void CheckBigMaps()
    {
        // 1. Parallel and one-by-one loading build the same list (warm: both after the libraries and JIT are in).
        var errors = new List<string>();
        _ = Maps.All;
        var t0 = Stopwatch.GetTimestamp();
        var (serial, _) = Maps.LoadFolder(Maps.Folder, errors, parallel: false);
        var serialMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var serialTimes = serial.ToDictionary(m => m.Name, m => m.LoadMs);
        t0 = Stopwatch.GetTimestamp();
        var (par, parPacks) = Maps.LoadFolder(Maps.Folder, errors, parallel: true);
        var parMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        if (errors.Count > 0)
            Fail($"Big maps: loading the Maps folder gave {errors.Count} error(s): {errors[0]}");
        var same = par.Length == serial.Length;
        for (var i = 0; same && i < par.Length; i++)
            if (par[i].Name != serial[i].Name || par[i].Id != serial[i].Id || MapDigest(par[i]) != MapDigest(serial[i]))
            {
                Fail($"Big maps: {par[i].Name} (#{i}) loads differently in parallel than one by one (digest {MapDigest(par[i]):x16} vs {MapDigest(serial[i]):x16})");
                same = false;
            }
        if (par.Length != Maps.All.Length || par.Zip(Maps.All).Any(x => x.First.Name != x.Second.Name || MapDigest(x.First) != MapDigest(x.Second)))
            Fail("Big maps: a fresh load of the Maps folder doesn't match Maps.All");
        Line($"{par.Length} maps in {parPacks.Length} packs: one by one {serialMs:0} ms, parallel {parMs:0} ms on {Environment.ProcessorCount} cores "
             + $"(build {Maps.LastLoadTimes.BuildMs:0} ms, libraries {Maps.LastLoadTimes.LibrariesMs:0} ms, validate {Maps.LastLoadTimes.ValidateMs:0} ms); identical: {same}");
        if (Math.Min(serialMs, parMs) > AllMapsLoadBudgetMs)
            Fail($"Big maps: loading every map took {Math.Min(serialMs, parMs):0} ms (budget {AllMapsLoadBudgetMs:0} ms)");

        // 2. Per map: load time (one by one, warm), drawn boxes, props, decals.
        foreach (var m in serial)
        {
            var twin = par.First(x => x.Name == m.Name);
            var ms = Math.Min(m.LoadMs, twin.LoadMs);
            if (ms > BigMapLoadBudgetMs)
                Fail($"Big maps: {m.Name} took {ms:0} ms to load (budget {BigMapLoadBudgetMs:0} ms)");
            // (The light bake at the end of Validate has its own budget: it is a one-off cost released builds ship baked.)
            double bakeMs = m.LightBake?.BakeMs ?? 0, twinBakeMs = twin.LightBake?.BakeMs ?? 0;
            if (Math.Min(m.ValidateMs - bakeMs, twin.ValidateMs - twinBakeMs) > ValidateBudgetMs)
                Fail($"Big maps: {m.Name} took {Math.Min(m.ValidateMs - bakeMs, twin.ValidateMs - twinBakeMs):0} ms to validate, without its light bake (budget {ValidateBudgetMs:0} ms)");
            if (Math.Min(bakeMs, twinBakeMs) > LightBakeBudgetMs)
                Fail($"Big maps: {m.Name}'s light bake took {Math.Min(bakeMs, twinBakeMs):0} ms (budget {LightBakeBudgetMs:0} ms)");
            var cells = BoxBatches.Build(m).Runs.Length;
            if (cells > BoxCellBudget)
                Fail($"Big maps: {m.Name}'s boxes fill {cells} cells (budget {BoxCellBudget}: at most that many box draw calls a frame)");
            if (m.PropInstances.Count > PropBudget)
                Fail($"Big maps: {m.Name} has {m.PropInstances.Count} props (the wire limit is {PropBudget})");
            if (m.DecalCount > MapDecalsLimit)
                Fail($"Big maps: {m.Name} has {m.DecalCount} decals (limit {MapDecalsLimit})");
        }
        Line($"Slowest validate: {string.Join(", ", serial.OrderByDescending(m => m.ValidateMs).Take(4).Select(m => $"{m.Name} {m.ValidateMs:0.0} ms"))}");
        Line($"Light bakes: {string.Join(", ", serial.Where(m => m.LightBake != null).Select(m => $"{m.Name} {m.LightBake!.BakeMs:0} ms ({m.LightBake.NX} x {m.LightBake.NY} x {m.LightBake.NZ} cells, {m.LightBake.Sources.Length} lights, {m.LightBake.Bytes / 1024} KB)"))}");
        Line("Biggest maps (warm load ms, one by one / in parallel):");
        foreach (var m in serial.OrderByDescending(m => m.LoadMs).Take(8))
            Line($"  {m.Name,-22} {m.Solid.Count,6} boxes ({DrawnBoxCount(m),4} map boxes)  {m.PropInstances.Count,5} props  {m.FoliageInstances.Length,6} plants  "
                 + $"{m.DecalCount,5} decals  {serialTimes[m.Name],5:0} / {par.First(x => x.Name == m.Name).LoadMs,5:0} ms");

        // 3. The stress map: within budget, and linear in props (the same 400 x 400 m yard with 2,000 Clutter props and
        // 400 Prop ops, then five times both).
        var small = StressLoad(2000, 400, out var smallMap);
        var big = StressLoad(10000, 2000, out var bigMap);
        var ratio = big / Math.Max(small, 1);
        Line($"Stress grid 400 x 400 m: 2,400 props {small:0} ms ({smallMap.PropInstances.Count} placed), 12,000 props {big:0} ms "
             + $"({bigMap.PropInstances.Count} placed, {bigMap.Solid.Count} boxes, {bigMap.Waypoints.Count} waypoints): x{ratio:0.0}");
        if (big > StressLoadBudgetMs)
            Fail($"Big maps: the 10,000-prop stress map took {big:0} ms to load and validate (budget {StressLoadBudgetMs:0} ms)");
        if (ratio > StressScalingBudget)
            Fail($"Big maps: 5x the props took {ratio:0.0}x as long to load (budget {StressScalingBudget}x: something is quadratic again)");
        if (bigMap.PropInstances.Count < 11000)
            Fail($"Big maps: the stress map placed only {bigMap.PropInstances.Count} of 12,000 props");
    }

    /// <summary>
    /// "Box batches" (BoxBatches, BuildingRenderer.cs): on every map, the instance buffer holds every box the old loop drew
    /// one by one, once each, with its exact centre, size, colour and texture layer, in 24 m cells whose bounds hold their
    /// boxes; and the draw calls it takes with everything in view.
    /// </summary>
    private static void CheckBoxBatches()
    {
        var worst = (Name: "", Boxes: 0, Runs: 0);
        long bytes = 0;
        foreach (var m in Maps.All)
        {
            var (data, runs) = BoxBatches.Build(m);
            var drawn = Enumerable.Range(0, m.Solid.Count).Where(m.IsDrawnBox).ToList();
            var count = runs.Sum(r => r.Count);
            if (count != drawn.Count)
            {
                Fail($"Box batches: {m.Name}: {count} instances for {drawn.Count} drawn boxes");
                continue;
            }
            // Every drawn box exactly once, as the box shader drew it.
            var want = new Dictionary<(float, float, float, float, float, float, float, float, float, float), int>();
            foreach (var i in drawn)
            {
                var b = m.Solid[i];
                var c = m.Colors[i];
                var k = (b.Center.X, b.Center.Y, b.Center.Z, b.Half.X, b.Half.Y, b.Half.Z, c.X, c.Y, c.Z, (float)(int)m.Materials[i]);
                want[k] = want.GetValueOrDefault(k) + 1;
            }
            const int f = BuildingRenderer.FloatsPerInstance;
            var ok = true;
            foreach (var r in runs)
                for (var n = r.Start; n < r.Start + r.Count && ok; n++)
                {
                    var o = n * f;
                    var k = (data[o], data[o + 1], data[o + 2], data[o + 4], data[o + 5], data[o + 6], data[o + 8], data[o + 9], data[o + 10], data[o + 7]);
                    if (data[o + 3] != 0 || data[o + 11] != 1f || !want.TryGetValue(k, out var left) || left == 0)
                    {
                        Fail($"Box batches: {m.Name}: instance {n} isn't one of the map's drawn boxes (or is there twice)");
                        ok = false;
                        break;
                    }
                    want[k] = left - 1;
                    var min = new System.Numerics.Vector3(data[o] - data[o + 4], data[o + 1] - data[o + 5], data[o + 2] - data[o + 6]);
                    var max = new System.Numerics.Vector3(data[o] + data[o + 4], data[o + 1] + data[o + 5], data[o + 2] + data[o + 6]);
                    if (min.X < r.Min.X || min.Y < r.Min.Y || min.Z < r.Min.Z || max.X > r.Max.X || max.Y > r.Max.Y || max.Z > r.Max.Z)
                    {
                        Fail($"Box batches: {m.Name}: a box sticks out of its cell's bounds (it would be culled while in view)");
                        ok = false;
                    }
                }
            bytes += (long)count * f * sizeof(float);
            // With everything in view, contiguous runs merge: one draw call.
            if (count > worst.Boxes)
                worst = (m.Name, count, runs.Length);
        }
        Line($"{Maps.All.Length} maps: every drawn box batched once, as drawn ({bytes / 1024} KB of instances in all); the most: {worst.Name} "
             + $"{worst.Boxes} boxes in {worst.Runs} cells (was {worst.Boxes} draw calls a frame; now 1 with all in view, at most {worst.Runs})");
    }

    private const int MapDecalsLimit = Map.MaxDecals;

    private static int DrawnBoxCount(Map m)
    {
        var n = 0;
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i))
                n++;
        return n;
    }

    /// <summary>Loads and validates the stress map with <paramref name="props"/> props; the best of two runs, in ms.</summary>
    private static double StressLoad(int props, int placed, out Map map)
    {
        var json = StressGridJson(props, placed, 2000, 200, 40);
        var best = double.MaxValue;
        map = null!;
        for (var run = 0; run < 2; run++)
        {
            var t = Stopwatch.GetTimestamp();
            map = MapFile.Load(json, "Stress Grid.json", "");
            map.Validate();
            best = Math.Min(best, Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        return best;
    }

    /// <summary>The stress map of tools/perf/stress_grid.py: a 2h x 2h yard, a grid of low walls, Clutter and DecalScatter
    /// ops, and <paramref name="placed"/> Prop ops (one prop each) at the lane crossings.</summary>
    internal static string StressGridJson(int props, int placed, int decals, int half, int walls)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.####", inv);
        var ops = new List<string>
        {
            "{\"op\": \"Ground\", \"color\": [0.42, 0.42, 0.44]}",
            "{\"op\": \"Perimeter\", \"height\": 6, \"color\": [0.62, 0.62, 0.64]}",
        };
        var pitch = (2.0 * half - 16) / walls;
        for (var ix = 0; ix < walls; ix++)
        for (var iz = 0; iz < walls; iz++)
        {
            var x = -half + 8 + ix * pitch + pitch / 2;
            var z = -half + 8 + iz * pitch + pitch / 2;
            var alongX = (ix + iz) % 2 == 0;
            var (w, d) = alongX ? (pitch * 0.45, 0.4) : (0.4, pitch * 0.45);
            ops.Add($"{{\"op\": \"Block\", \"x0\": {F(x - w / 2)}, \"y0\": 0, \"z0\": {F(z - d / 2)}, \"x1\": {F(x + w / 2)}, \"y1\": {F(1.2 + (ix * 7 + iz * 3) % 4 * 0.4)}, "
                    + $"\"z1\": {F(z + d / 2)}, \"color\": [{F(0.5 + ix % 3 * 0.1)}, 0.45, 0.4]}}");
        }
        string[] single = ["Wooden Crate", "Oil Drum", "Trash Can", "Traffic Cone"];
        for (var k = 0; k < placed; k++)
        {
            int ix = k % (walls + 1), iz = k / (walls + 1) % (walls + 1);
            var off = k / ((walls + 1) * (walls + 1)) * 1.3; // more than one per crossing: step along the lane
            ops.Add($"{{\"op\": \"Prop\", \"name\": \"{single[k % single.Length]}\", \"x\": {F(-half + 8 + ix * pitch + off)}, \"z\": {F(-half + 8 + iz * pitch)}, \"yaw\": {k * 37 % 360}}}");
        }
        ops.Add("{\"op\": \"RingSpawns\", \"count\": 32, \"frac\": 0.85}");
        ops.Add("{\"op\": \"AutoWaypoints\", \"spacing\": 10}");
        string[][] pools = [["Street"], ["Industrial", "Debris"], ["Furniture", "Garden"], ["Household", "Office"], ["Street", "Debris", "Industrial"]];
        var strips = Math.Max(1, (props + 1999) / 2000);
        for (int k = 0, left = props; left > 0; k++, left -= 2000)
        {
            var z0 = -half + 6 + (2.0 * half - 12) * k / strips;
            var z1 = -half + 6 + (2.0 * half - 12) * (k + 1) / strips;
            ops.Add($"{{\"op\": \"Clutter\", \"props\": [{string.Join(", ", pools[k % pools.Length].Select(p => $"\"{p}\""))}], \"x0\": {-half + 6}, \"z0\": {F(z0)}, "
                    + $"\"x1\": {half - 6}, \"z1\": {F(z1)}, \"count\": {Math.Min(2000, left)}, \"seed\": {100 + k}}}");
        }
        string[] decalNames = ["Oil Stain", "Puddle", "Floor Cracks", "Leaf Litter", "Tyre Tracks", "Dirt Patch", "Rust Stain", "Skid Marks"];
        for (int k = 0, left = decals; left > 0; k++, left -= 2000)
            ops.Add($"{{\"op\": \"DecalScatter\", \"decals\": [{string.Join(", ", decalNames.Select(n => $"\"{n}\""))}], \"x0\": {-half + 4}, \"z0\": {-half + 4}, "
                    + $"\"x1\": {half - 4}, \"z1\": {half - 4}, \"count\": {Math.Min(2000, left)}, \"seed\": {500 + k}, \"minSize\": 0.8, \"maxSize\": 2.5}}");
        return $"{{\"format\": \"call-of-slop-map/1\", \"name\": \"Stress Grid\", \"description\": \"Load-cost stress map (Big maps self-test).\", \"order\": 999, "
               + $"\"halfX\": {half}, \"halfZ\": {half}, \"maxPlayers\": 32, \"sky\": [0.36, 0.42, 0.5], \"ops\": [\n{string.Join(",\n", ops)}\n]}}";
    }
}
