using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Bullet-impact variety (Decals/Impacts): every impact material has at least three variants (after fallbacks) with
    /// valid weights; the weighted pick is deterministic and matches the weights; the surface rules (asphalt, plaster,
    /// grass, sand, snow...) and their fallbacks; calibre sizing; a survey of every map's box and terrain surfaces; and the
    /// "Decal Impacts" range: each target gets its surface's marks, the same on every client, without allocating, plus a
    /// blast scorch. All CPU-side.
    /// </summary>
    private static void CheckImpactVariety()
    {
        CheckImpactSets();
        CheckImpactPickWeights();
        CheckImpactSurfaceRules();
        CheckImpactSurvey();
        CheckImpactRange();
    }

    private static void CheckImpactSets()
    {
        var impacts = Decals.All.Where(d => d.IsImpact).ToArray();
        var folder = Path.Combine(Decals.Folder, "Impacts");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json").Length : 0;
        var loaded = impacts.Count(d => d.Category.Equals("Impacts", StringComparison.OrdinalIgnoreCase));
        if (loaded != files)
            Fail($"Decals/Impacts has {files} files but {loaded} impact decals loaded from it");
        var parts = new List<string>();
        foreach (var m in DecalFile.ImpactMaterialNames)
        {
            var set = Decals.ImpactsFor(m);
            parts.Add($"{m} {set.Length}");
            if (set.Length < 3)
                Fail($"impact material {m}: {set.Length} variant(s), want at least 3 so repeated hits don't look stamped");
            if (set.Distinct().Count() != set.Length)
                Fail($"impact material {m}: a decal is listed twice");
            foreach (var d in set)
                if (!float.IsFinite(d.ImpactWeight) || d.ImpactWeight < 0.05f || d.ImpactWeight > 20f)
                    Fail($"impact decal {d.Name}: weight {d.ImpactWeight} out of range");
            // The chain of fallbacks ends (no loops).
            var at = m;
            var hops = 0;
            while (DecalFile.ImpactFallback(at) is { } next && hops++ < 10)
                at = next;
            if (hops > 8)
                Fail($"impact material {m}: its fallback chain loops");
        }
        if (Decals.ImpactsFor("Explosive").Any(d => !d.ImpactMaterials.Contains("Explosive")))
            Fail("the Explosive set should only hold scorch decals (a blast never falls back to a bullet hole)");
        if (!ReferenceEquals(Decals.ImpactsFor("Asphalt"), Decals.ImpactsFor("asphalt")))
            Fail("impact materials should be found case-insensitively from the cache");
        DecalLine($"impact variety: {impacts.Length} impact decals; per material {string.Join(", ", parts)}");
    }

    private static void CheckImpactPickWeights()
    {
        const int hits = 20000;
        var worst = 0f;
        var worstName = "";
        foreach (var m in DecalFile.ImpactMaterialNames)
        {
            var set = Decals.ImpactsFor(m);
            if (set.Length == 0)
                continue;
            var counts = new int[set.Length];
            var total = set.Sum(d => d.ImpactWeight);
            for (var i = 0; i < hits; i++)
            {
                var p = new Vector3(i * 0.137f % 80f - 40f, i % 7 * 0.31f, i * 0.0713f % 60f - 30f);
                var h = ImpactDecals.Hash(p);
                var d = ImpactDecals.PickVariant(set, h);
                if (!ReferenceEquals(d, ImpactDecals.PickVariant(set, ImpactDecals.Hash(p))))
                {
                    Fail($"impact variant pick for {m} isn't deterministic");
                    break;
                }
                counts[Array.IndexOf(set, d)]++;
            }
            for (var k = 0; k < set.Length; k++)
            {
                var want = set[k].ImpactWeight / total;
                var got = counts[k] / (float)hits;
                var off = MathF.Abs(got - want) / want;
                if (off > worst)
                {
                    worst = off;
                    worstName = $"{set[k].Name} on {m}";
                }
                if (counts[k] == 0 || off > 0.2f)
                    Fail($"impact variant {set[k].Name} on {m}: picked {got:P1} of hits, its weight says {want:P1}");
            }
        }
        DecalLine($"impact variant pick: deterministic, every variant used, shares within {worst:P0} of their weights (worst {worstName})");
    }

    private static void CheckImpactSurfaceRules()
    {
        var up = Vector3.UnitY;
        var side = Vector3.UnitZ;
        (Material M, Vector3 Color, Vector3 Normal, bool Ground, string Want)[] cases =
        [
            (Material.Concrete, new(0.55f, 0.55f, 0.53f), side, false, "Concrete"),
            (Material.Concrete, new(0.86f, 0.84f, 0.8f), side, false, "Plaster"),
            (Material.Concrete, new(0.86f, 0.84f, 0.8f), up, false, "Concrete"),
            (Material.Concrete, new(0.25f, 0.25f, 0.27f), up, false, "Asphalt"),
            (Material.Concrete, new(0.4f, 0.4f, 0.42f), up, true, "Asphalt"),
            (Material.Concrete, new(0.4f, 0.4f, 0.42f), up, false, "Concrete"),
            (Material.Concrete, new(0.25f, 0.25f, 0.27f), side, false, "Concrete"),
            (Material.Dirt, new(0.36f, 0.5f, 0.25f), up, true, "Grass"),
            (Material.Dirt, new(0.82f, 0.72f, 0.5f), up, false, "Sand"),
            (Material.Dirt, new(0.45f, 0.34f, 0.24f), up, false, "Dirt"),
            (Material.Dirt, new(0.92f, 0.93f, 0.94f), up, false, "Snow"),
            (Material.Brick, new(0.6f, 0.28f, 0.26f), side, false, "Brick"),
            (Material.Wood, new(0.58f, 0.4f, 0.24f), side, false, "Wood"),
            (Material.Metal, new(0.35f, 0.45f, 0.6f), side, false, "Metal"),
            (Material.Tiles, new(0.7f, 0.7f, 0.68f), up, false, "Tiles"),
            (Material.Grid, new(0.7f, 0.7f, 0.7f), side, false, "Grid"),
        ];
        foreach (var c in cases)
        {
            var got = ImpactDecals.Refine(c.M, c.Color, c.Normal, c.Ground);
            if (got != c.Want)
                Fail($"impact surface: {c.M} {c.Color} facing {c.Normal}{(c.Ground ? " (ground)" : "")} gives {got}, expected {c.Want}");
        }
        foreach (var name in DecalFile.ImpactMaterialNames)
            if (DecalFile.ImpactFallback(name) is { } f && !DecalFile.ImpactMaterialNames.Contains(f))
                Fail($"impact material {name} falls back to unknown {f}");

        // Calibre: light pistols leave smaller marks than heavy rifles; pellets are small; everything stays in range.
        var single = Weapons.All.Where(w => w.Pellets == 1).ToArray();
        if (single.Length >= 2)
        {
            var light = single.MinBy(w => w.Caliber)!;
            var heavy = single.MaxBy(w => w.Caliber)!;
            var sl = ImpactDecals.CalibreScale(light);
            var sh = ImpactDecals.CalibreScale(heavy);
            if (!(sl < sh) && light.Caliber < heavy.Caliber)
                Fail($"calibre sizing: {light.Name} ({sl:0.00}) should leave smaller marks than {heavy.Name} ({sh:0.00})");
            if (Weapons.All.Any(w => ImpactDecals.CalibreScale(w) is < 0.69f or > 1.36f))
                Fail("calibre sizing out of 0.7..1.35");
            DecalLine($"impact calibre sizing: {light.Name} x{sl:0.00} .. {heavy.Name} x{sh:0.00}, pellets x0.80");
        }
    }

    private static readonly Vector3[] SurveyNormals = [Vector3.UnitY, Vector3.UnitX];

    /// <summary>Every surface any map's boxes and terrain give (as the impact rules see them) has its own impact set.</summary>
    private static void CheckImpactSurvey()
    {
        var tally = new Dictionary<string, int>();
        foreach (var map in Maps.All)
        {
            for (var i = 0; i < map.Solid.Count; i++)
            {
                if (!map.IsDecalSurface(i))
                    continue;
                var b = map.Solid[i];
                foreach (var n in SurveyNormals)
                {
                    var hit = new RayHit { Point = b.Center + n * b.Half, Normal = n, Shape = new ShapeId(ShapeKind.Box, i) };
                    var s = ImpactDecals.SurfaceOf(map, hit);
                    tally[s] = tally.GetValueOrDefault(s) + 1;
                }
            }
            if (map.Terrain is { } t)
            {
                for (var j = 0; j < t.CountZ; j += 4)
                for (var i = 0; i < t.CountX; i += 4)
                {
                    var p = new Vector3(t.OriginX + i * t.Cell, 0, t.OriginZ + j * t.Cell);
                    var hit = new RayHit { Point = p, Normal = Vector3.UnitY, Shape = new ShapeId(ShapeKind.Terrain, 0) };
                    var s = ImpactDecals.SurfaceOf(map, hit);
                    tally[s] = tally.GetValueOrDefault(s) + 1;
                }
            }
        }
        foreach (var (s, n) in tally)
        {
            if (!DecalFile.ImpactMaterialNames.Contains(s))
                Fail($"impact survey: surface {s} isn't an impact material");
            else if (Decals.ImpactsFor(s).Length < 3)
                Fail($"impact survey: {n} faces on the maps are {s}, which has fewer than 3 impact decals");
        }
        DecalLine($"impact survey over {Maps.All.Length} maps (box faces up/side, terrain samples): "
                  + string.Join(", ", tally.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
    }

    /// <summary>The Dev pack's "Decal Impacts" range: one target per surface, shot like a player would.</summary>
    private static void CheckImpactRange()
    {
        var map = Maps.Find("Decal Impacts");
        if (map == null)
        {
            Fail("the Dev pack's Decal Impacts map is missing");
            return;
        }
        var previous = World.Map;
        var quality = ImpactDecals.Quality;
        try
        {
            World.SetMap(map);
            ImpactDecals.Configure(ImpactDecalQuality.High);
            var pool = ImpactDecals.Pool;
            // (eye, target point, expected surface): walls shot level from the middle, floors and terrain from above.
            (Vector3 Eye, Vector3 At, string Want)[] shots =
            [
                (new(-20, 1.5f, 0), new(-20, 1.5f, -9.6f), "Concrete"),
                (new(-14, 1.5f, 0), new(-14, 1.5f, -9.6f), "Plaster"),
                (new(-8, 1.5f, 0), new(-8, 1.5f, -9.6f), "Brick"),
                (new(-2, 1.5f, 0), new(-2, 1.5f, -9.6f), "Metal"),
                (new(4, 1.5f, 0), new(4, 1.5f, -9.6f), "Wood"),
                (new(-20, 2.5f, 3), new(-20, 0.15f, 9), "Asphalt"),
                (new(-14, 2.5f, 3), new(-14, 0.9f, 9), "Tiles"),
                (new(-6, 2.5f, 3), new(-6, 0.4f, 9), "Sand"),
                (new(2, 2.5f, 3), new(2, 0.4f, 9), "Dirt"),
                (new(10, 2.5f, 4), new(10, 1f, 9), "Grass"),
                (new(-18, 2.5f, 17), new(-18, 0, 22), "Grass"),
                (new(-6, 2.5f, 17), new(-6, 0, 22), "Dirt"),
                (new(6, 2.5f, 17), new(6, 0, 22), "Rock"),
                (new(18, 2.5f, 17), new(18, 0, 22), "Snow"),
            ];
            pool.Clear(map, 20);
            var first = new float[shots.Length * 4 * ImpactPool.FloatsPerInstance];
            var seen = new List<string>();
            for (var pass = 0; pass < 2; pass++)
            {
                pool.Clear(map, 20);
                var now = 20.0;
                var slot = 0;
                foreach (var (eye, at, want) in shots)
                {
                    var dir = Vector3.Normalize(at - eye);
                    if (!World.Query.Raycast(eye, dir, 40, QueryFilter.Shots, out var hit))
                    {
                        Fail($"Decal Impacts: the shot at the {want} target hits nothing");
                        continue;
                    }
                    var surface = ImpactDecals.SurfaceOf(map, hit);
                    if (surface != want)
                        Fail($"Decal Impacts: the {want} target reads as {surface} ({hit.Kind} {hit.Shape.Index})");
                    // Four hits a few centimetres apart: each is a mark from that surface's set.
                    for (var k = 0; k < 4; k++)
                    {
                        var offset = MathF.Abs(hit.Normal.Y) > 0.5f ? new Vector3(k * 0.07f, 0, k % 2 * 0.05f) : new Vector3(k * 0.07f, k * 0.06f, 0);
                        var to = hit.Point + offset;
                        var from = to - dir * hit.Distance;
                        if (!ImpactDecals.OnTracer(map, from, to, 3, now += 0.01, false, k == 3 ? 1.3f : 1f))
                        {
                            Fail($"Decal Impacts: no mark on the {want} target");
                            continue;
                        }
                        var layer = (int)pool.Instances[slot * ImpactPool.FloatsPerInstance + 8];
                        var def = Decals.All[layer];
                        if (!Decals.ImpactsFor(want).Contains(def))
                            Fail($"Decal Impacts: {def.Name} on the {want} target isn't a {want} impact");
                        if (pass == 0 && !seen.Contains(def.Name))
                            seen.Add(def.Name);
                        if (pass == 0)
                            Array.Copy(pool.Instances, slot * ImpactPool.FloatsPerInstance, first, slot * ImpactPool.FloatsPerInstance, ImpactPool.FloatsPerInstance);
                        else if (!pool.Instances.AsSpan(slot * ImpactPool.FloatsPerInstance, ImpactPool.FloatsPerInstance)
                                     .SequenceEqual(first.AsSpan(slot * ImpactPool.FloatsPerInstance, ImpactPool.FloatsPerInstance)))
                            Fail($"Decal Impacts: the same shots at the {want} target gave different marks (impacts must be deterministic)");
                        slot++;
                    }
                }
            }

            // A blast on the asphalt and against the brick wall leaves a scorch.
            pool.Clear(map, 30);
            var ground = ImpactDecals.OnBlast(map, new Vector3(-20, 0.3f, 9), 3, 30.1);
            var wall = ImpactDecals.OnBlast(map, new Vector3(-8, 1.2f, -9.2f), 3, 30.2);
            var scorches = 0;
            for (var s = 0; s < pool.HighWater; s++)
                if (Decals.All[(int)pool.Instances[s * ImpactPool.FloatsPerInstance + 8]].ImpactMaterials.Contains("Explosive"))
                    scorches++;
            if (!ground || !wall || scorches != 2)
                Fail($"blast scorches: ground {ground}, wall {wall}, {scorches} scorch marks (expected 2)");
            if (ImpactDecals.OnBlast(map, new Vector3(0, 12f, 0), 3, 30.3))
                Fail("a blast in mid-air left a scorch");

            // No allocation per shot (tracers and blasts), and the cost per tracer.
            var eye0 = shots[0].Eye;
            var dir0 = Vector3.Normalize(shots[0].At - eye0);
            World.Query.Raycast(eye0, dir0, 40, QueryFilter.Shots, out var h0);
            var end = eye0 + dir0 * h0.Distance;
            for (var i = 0; i < 200; i++)
                ImpactDecals.OnTracer(map, eye0, end + new Vector3(i % 20 * 0.01f, 0, 0), 5, 31 + i * 0.001, false, 1f);
            var sw = Stopwatch.StartNew();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            const int n = 4000;
            for (var i = 0; i < n; i++)
            {
                var shot = shots[i % shots.Length];
                ImpactDecals.OnTracer(map, shot.Eye, shot.At + new Vector3(i % 13 * 0.01f, 0, 0), (byte)(i % 8), 32 + i * 0.001, i % 3 == 0, 1.1f);
                if (i % 100 == 0)
                    ImpactDecals.OnBlast(map, new Vector3(-20, 0.3f, 9), 6, 32 + i * 0.001);
            }
            var us = sw.Elapsed.TotalMilliseconds * 1000 / n;
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            if (bytes > 0)
                Fail($"impact placement allocated {bytes} bytes over {n} tracers (must be allocation-free)");
            DecalLine($"Decal Impacts range: {shots.Length} targets read as expected, {seen.Count} different variants on them, deterministic, blast scorches on floor and wall; "
                      + $"{us:0.00} us per tracer (raycast + surface + pick + add), {bytes} bytes allocated over {n}");
        }
        finally
        {
            ImpactDecals.Configure(quality);
            World.SetMap(previous);
        }
    }
}
