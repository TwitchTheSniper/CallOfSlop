using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    // ---- Foliage ----------------------------------------------------------------------------------

    /// <summary>
    /// The "foliage" map section: placement is deterministic and survives a save/load round trip, only tree trunks are
    /// solid (bullets pass through bushes but not trunks), the quality levels keep the expected nested subsets, the
    /// renderer's chunks cover every kept plant, and bad sections give readable errors.
    /// </summary>
    private static void CheckFoliage()
    {
        var map = Maps.Find("Foliage Test");
        if (map == null)
        {
            Fail("Maps/Dev/FoliageTest.json ('Foliage Test') didn't load");
            return;
        }
        var all = map.FoliageInstances;
        var byType = Enumerable.Range(0, FoliageMeshes.TypeCount).Select(t => all.Count(f => (int)f.Type == t)).ToArray();
        Line($"{map.Name}: {all.Length} plants (grass {byType[0]}, bushes {byType[1]}, shrubs {byType[2]}, trees {byType[3]}, pines {byType[4]}), "
            + $"{map.TrunkCount} trunk boxes, fingerprint {map.ContentHash:x8}");
        if (byType.Any(n => n == 0))
            Fail("the foliage test map should have every plant type");

        // Deterministic: building the same file again gives bit-identical plants and trunks, and it survives a round trip.
        var json = MapFile.ToJson(map);
        var sw = Stopwatch.StartNew();
        var again = MapFile.Load(json, "Foliage Test (round trip)");
        var buildMs = sw.Elapsed.TotalMilliseconds;
        again.Validate();
        if (!again.FoliageInstances.AsSpan().SequenceEqual(all))
            Fail("foliage placement differs after a save/load round trip");
        if (again.ContentHash != map.ContentHash || again.FoliageHash != map.FoliageHash || map.FoliageHash == 0)
            Fail($"foliage fingerprint not stable ({map.ContentHash:x8} vs {again.ContentHash:x8})");
        if (MapFile.ToJson(again) != json)
            Fail("the foliage section isn't written back the same way it was read");
        Line($"round trip: identical ({again.FoliageInstances.Length} plants rebuilt in {buildMs:0.0} ms)");

        // The fingerprint covers the plants: a different seed is a different map online.
        var reseeded = MapFile.Load(json.Replace("\"seed\": 7,", "\"seed\": 8,"), "Foliage Test (reseeded)");
        if (reseeded.ContentHash == map.ContentHash)
            Fail("changing the foliage seed didn't change the map fingerprint");
        // Maps without foliage keep their old fingerprint (no trunks, no foliage hash).
        foreach (var m in Maps.All.Where(m => m.FoliageSection == null))
            if (m.FoliageHash != 0 || m.TrunkCount != 0 || m.FoliageInstances.Length != 0)
                Fail($"{m.Name}: has foliage data without a foliage section");

        // Only trunks are solid: one box per tree, and nothing else was added to the map's boxes.
        var trees = all.Where(f => Foliage.IsTree(f.Type)).ToArray();
        if (map.TrunkCount != trees.Length || map.TrunkStart + map.TrunkCount != map.Solid.Count)
            Fail($"{trees.Length} trees but {map.TrunkCount} trunk boxes (start {map.TrunkStart} of {map.Solid.Count})");
        var noFoliage = MapFile.Load(json[..json.IndexOf(",\n  \"foliage\"", StringComparison.Ordinal)] + "\n}\n", "Foliage Test (no foliage)");
        if (noFoliage.Solid.Count + trees.Length != map.Solid.Count)
            Fail($"foliage added {map.Solid.Count - noFoliage.Solid.Count} boxes for {trees.Length} trees");
        for (var i = 0; i < trees.Length; i++)
            if (map.Solid[map.TrunkStart + i] != trees[i].TrunkBox)
            {
                Fail($"trunk box {i} doesn't match its tree");
                break;
            }

        // Bullets: a shot at a trunk stops at its bark; a shot through a bush (away from trunks) goes straight through.
        var previous = World.Map;
        World.SetMap(map);
        try
        {
            var trunkMisses = 0;
            foreach (var tree in trees)
            {
                var b = tree.TrunkBox;
                var from = new Vector3(b.Center.X - 4f, 1.2f, b.Center.Z);
                var hit = World.RaycastWorld(from, Vector3.UnitX, 8f);
                if (hit > 4f - b.Half.X + 0.01f)
                    trunkMisses++;
            }
            if (trunkMisses > 0)
                Fail($"{trunkMisses} of {trees.Length} trunks didn't stop a bullet");

            int bushes = 0, blockedBushes = 0, blockedPlayers = 0;
            foreach (var f in all.Where(f => f.Type is FoliageType.Bush or FoliageType.Shrub))
            {
                if (trees.Any(t => Vector2.Distance(new Vector2(t.Position.X, t.Position.Z), new Vector2(f.Position.X, f.Position.Z)) < 2.5f))
                    continue;
                var from = new Vector3(f.Position.X - 1.5f, 0.5f, f.Position.Z);
                var expected = RaycastWithout(map, from, Vector3.UnitX, 3f, map.TrunkStart);
                if (expected < 3f)
                    continue; // something else (a crate) is in the way; not this test's business
                bushes++;
                if (World.RaycastWorld(from, Vector3.UnitX, 3f) < 3f)
                    blockedBushes++;
                if (map.IsBlocked(f.Position) != noFoliage.IsBlocked(f.Position))
                    blockedPlayers++;
            }
            Line($"bullets: all {trees.Length} trunks stop shots; {bushes - blockedBushes} of {bushes} bushes/shrubs let shots through");
            if (bushes < 50)
                Fail($"only {bushes} bushes/shrubs could be shot through (expected plenty on the test map)");
            if (blockedBushes > 0)
                Fail($"{blockedBushes} bushes or shrubs stopped a bullet (foliage must be cosmetic)");
            if (blockedPlayers > 0)
                Fail($"{blockedPlayers} bushes or shrubs block a standing player (only trunks are solid)");
        }
        finally
        {
            World.SetMap(previous);
        }

        // Quality levels: nested subsets with the expected sizes, trees always kept, Off keeps nothing.
        var counts = new Dictionary<FoliageQuality, int[]>();
        foreach (var q in Enum.GetValues<FoliageQuality>())
            counts[q] = Foliage.CountFor(all, q);
        Line("kept per quality (grass/bush/shrub/tree/pine): " + string.Join("; ", counts.Select(kv => $"{kv.Key} {string.Join("/", kv.Value)} = {kv.Value.Sum()}")));
        if (counts[FoliageQuality.Off].Sum() != 0)
            Fail("quality Off keeps plants");
        if (!counts[FoliageQuality.High].SequenceEqual(byType))
            Fail("quality High doesn't keep every plant");
        foreach (var f in all)
            if (Foliage.Kept(f, FoliageQuality.Low) && !Foliage.Kept(f, FoliageQuality.Medium) || Foliage.Kept(f, FoliageQuality.Medium) && !Foliage.Kept(f, FoliageQuality.High))
            {
                Fail("quality levels aren't nested subsets");
                break;
            }
        foreach (var q in new[] { FoliageQuality.Low, FoliageQuality.Medium })
        {
            for (var t = 0; t < FoliageMeshes.TypeCount; t++)
            {
                var type = (FoliageType)t;
                var expect = Foliage.DensityFor(type, q) * byType[t];
                var items = map.FoliageSection!.Items.Count(i => i.Type == type); // hand-placed plants are always kept
                if (MathF.Abs(counts[q][t] - expect) > MathF.Max(0.06f * byType[t], 3f) + items)
                    Fail($"{q}: {counts[q][t]} {type} kept, expected about {expect:0}");
            }
        }

        // Renderer data: the chunks cover every kept plant exactly once, sorted by type, and their bounds contain them.
        foreach (var q in new[] { FoliageQuality.Low, FoliageQuality.Medium, FoliageQuality.High })
        {
            var b = FoliageBatches.Build(all, map.HalfX, map.HalfZ, q);
            var covered = 0;
            var ok = b.Count == counts[q].Sum() && b.Instances.Length == b.Count * FoliageBatches.FloatsPerInstance;
            for (var i = 0; i < b.Chunks.Length && ok; i++)
            {
                var c = b.Chunks[i];
                ok &= c.Start == covered && c.Count > 0 && (i == 0 || b.Chunks[i - 1].Type <= c.Type);
                for (var k = c.Start; k < c.Start + c.Count && ok; k++)
                {
                    var p = new Vector3(b.Instances[k * 8], b.Instances[k * 8 + 1], b.Instances[k * 8 + 2]);
                    ok &= p.X >= c.Min.X && p.X <= c.Max.X && p.Y >= c.Min.Y && p.Y <= c.Max.Y && p.Z >= c.Min.Z && p.Z <= c.Max.Z;
                }
                covered += c.Count;
            }
            ok &= covered == b.Count;
            for (var t = 0; t < FoliageMeshes.TypeCount && ok; t++)
                ok &= b.TypeCount[t] == counts[q][t];
            if (!ok)
                Fail($"{q}: renderer chunks don't cover the kept plants correctly");
            else
                Line($"{q}: {b.Count} plants in {b.Chunks.Length} chunks, {b.Instances.Length * 4 / 1024} KB of instance data");
        }
        if (FoliageBatches.Build(all, map.HalfX, map.HalfZ, FoliageQuality.Off).Count != 0)
            Fail("quality Off builds renderer data");
        Line("meshes (triangles near/far): " + string.Join(", ", Enumerable.Range(0, FoliageMeshes.TypeCount)
            .Select(t => $"{(FoliageType)t} {FoliageMeshes.Triangles((FoliageType)t, 0)}/{FoliageMeshes.Triangles((FoliageType)t, 1)}")));
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
            if (FoliageMeshes.Triangles((FoliageType)t, 1) > FoliageMeshes.Triangles((FoliageType)t, 0) || FoliageMeshes.Triangles((FoliageType)t, 1) == 0)
                Fail($"{(FoliageType)t}: far mesh isn't cheaper than the near one");

        // Hand-placed trees stand exactly where the file says, with a trunk.
        foreach (var item in map.FoliageSection!.Items.Where(i => Foliage.IsTree(i.Type)))
            if (!map.Solid.Skip(map.TrunkStart).Any(b => MathF.Abs(b.Center.X - item.X) < 0.001f && MathF.Abs(b.Center.Z - item.Z) < 0.001f))
                Fail($"hand-placed {item.Type} at ({item.X}, {item.Z}) has no trunk");

        // Plants keep out of paths and boxes.
        var spec = map.FoliageSection!;
        var onPath = all.Count(f => f.Rand > 0 && spec.Paths.Any(p => DistanceToPath(p, f.Position.X, f.Position.Z) < p.Width / 2));
        if (onPath > 0)
            Fail($"{onPath} scattered plants on a path");

        // Bad sections give readable errors.
        foreach (var (bad, expect) in new[]
                 {
                     ("{ \"regions\": [ { \"type\": \"palm\", \"density\": 1, \"rect\": [0, 0, 5, 5] } ] }", "unknown type"),
                     ("{ \"regions\": [ { \"type\": \"grass\", \"rect\": [0, 0, 5, 5] } ] }", "density"),
                     ("{ \"regions\": [ { \"type\": \"grass\", \"density\": 1 } ] }", "area"),
                     ("{ \"regions\": [ { \"type\": \"grass\", \"density\": 1, \"rect\": [0, 0, 5, 5], \"heigth\": 2 } ] }", "unknown field"),
                     ("{ \"items\": [ { \"type\": \"tree\", \"x\": 1, \"z\": 1, \"scale\": 2 } ] }", "height"),
                 })
        {
            var file = $"{{ \"name\": \"X\", \"halfX\": 20, \"halfZ\": 20, \"ops\": [ {{ \"op\": \"Spawn\", \"x\": 0, \"z\": 0 }} ], \"foliage\": {bad} }}";
            try
            {
                MapFile.Load(file, "bad.json");
                Fail($"a broken foliage section loaded without complaint (expected '{expect}')");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
            {
            }
            catch (Exception ex)
            {
                Fail($"broken foliage section gave the wrong error: {ex.Message}");
            }
        }
    }

    /// <summary>Like <see cref="World.RaycastWorld"/>, but only against the map's boxes before <paramref name="end"/>.</summary>
    private static float RaycastWithout(Map map, Vector3 origin, Vector3 dir, float maxDist, int end)
    {
        var best = maxDist;
        for (var i = 0; i < end && i < map.Solid.Count; i++)
            if (map.Solid[i].Raycast(origin, dir, best) is { } t && t < best)
                best = t;
        return best;
    }

    private static float DistanceToPath(FoliagePath p, float x, float z)
    {
        var best = float.MaxValue;
        var pts = p.Points;
        for (var i = 0; i + 3 < pts.Length; i += 2)
        {
            float ax = pts[i], az = pts[i + 1], dx = pts[i + 2] - ax, dz = pts[i + 3] - az;
            var len2 = dx * dx + dz * dz;
            var t = len2 > 0 ? Math.Clamp(((x - ax) * dx + (z - az) * dz) / len2, 0f, 1f) : 0f;
            best = MathF.Min(best, MathF.Sqrt(MathF.Pow(ax + dx * t - x, 2) + MathF.Pow(az + dz * t - z, 2)));
        }
        return best;
    }
}
