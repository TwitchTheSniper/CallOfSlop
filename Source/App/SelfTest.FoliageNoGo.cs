using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    // ---- Foliage no-go zones ------------------------------------------------------------------------

    private static readonly List<string> NoGoNotes = new();

    /// <summary>A result line, also collected for the section's GitHub notice.</summary>
    private static void NoGoLine(string text)
    {
        Line(text);
        NoGoNotes.Add(text);
    }

    /// <summary>Most a big map's planting may take (ms) on the CI runner, no-go rules included.</summary>
    private const double NoGoPlantBudgetMs = 1500;

    /// <summary>
    /// A flat lab with a closed building (a rooftop planter on it), an open-sided shed (half of it an "include" garden), a
    /// bridge, a cosmetic mesh roof, box trees and a mesh-built tree, intact concrete, busted brick paving, an exclude
    /// circle and a busted path. One grass region and a sparse bush region cover everything.
    /// </summary>
    private const string NoGoLabJson = """
        { "name": "No-go Lab", "halfX": 40, "halfZ": 40,
          "ops": [
            { "op": "Ground", "color": [0.34, 0.42, 0.22] },
            { "op": "Block", "x0": -30, "y0": 0, "z0": -30, "x1": -18, "y1": 3, "z1": -29.7, "color": [0.8, 0.7, 0.55] },
            { "op": "Block", "x0": -30, "y0": 0, "z0": -18.3, "x1": -25, "y1": 3, "z1": -18, "color": [0.8, 0.7, 0.55] },
            { "op": "Block", "x0": -23, "y0": 0, "z0": -18.3, "x1": -18, "y1": 3, "z1": -18, "color": [0.8, 0.7, 0.55] },
            { "op": "Block", "x0": -25, "y0": 2.2, "z0": -18.3, "x1": -23, "y1": 3, "z1": -18, "color": [0.8, 0.7, 0.55] },
            { "op": "Block", "x0": -30, "y0": 0, "z0": -29.7, "x1": -29.7, "y1": 3, "z1": -18.3, "color": [0.8, 0.7, 0.55] },
            { "op": "Block", "x0": -18.3, "y0": 0, "z0": -29.7, "x1": -18, "y1": 3, "z1": -18.3, "color": [0.8, 0.7, 0.55] },
            { "op": "Block", "x0": -30.2, "y0": 3, "z0": -30.2, "x1": -17.8, "y1": 3.3, "z1": -17.8, "color": [0.55, 0.53, 0.5] },
            { "op": "Block", "x0": -10, "y0": 0, "z0": -30, "x1": -9.7, "y1": 2.5, "z1": -29.7, "color": [0.45, 0.3, 0.2] },
            { "op": "Block", "x0": -0.3, "y0": 0, "z0": -30, "x1": 0, "y1": 2.5, "z1": -29.7, "color": [0.45, 0.3, 0.2] },
            { "op": "Block", "x0": -10, "y0": 0, "z0": -20.3, "x1": -9.7, "y1": 2.5, "z1": -20, "color": [0.45, 0.3, 0.2] },
            { "op": "Block", "x0": -0.3, "y0": 0, "z0": -20.3, "x1": 0, "y1": 2.5, "z1": -20, "color": [0.45, 0.3, 0.2] },
            { "op": "Block", "x0": -10.2, "y0": 2.5, "z0": -30.2, "x1": 0.2, "y1": 2.7, "z1": -19.8, "color": [0.45, 0.3, 0.2] },
            { "op": "Block", "x0": 10, "y0": 0, "z0": -30, "x1": 11, "y1": 3, "z1": -26, "color": [0.55, 0.53, 0.5] },
            { "op": "Block", "x0": 21, "y0": 0, "z0": -30, "x1": 22, "y1": 3, "z1": -26, "color": [0.55, 0.53, 0.5] },
            { "op": "Block", "x0": 10, "y0": 3, "z0": -30, "x1": 22, "y1": 3.4, "z1": -26, "color": [0.55, 0.53, 0.5] },
            { "op": "Block", "x0": -30, "y0": 0, "z0": 0, "x1": -15, "y1": 0.04, "z1": 12, "color": [0.55, 0.53, 0.5] },
            { "op": "Block", "x0": -10, "y0": 0, "z0": 0, "x1": 2, "y1": 0.04, "z1": 12, "color": [0.6, 0.35, 0.35] },
            { "op": "Mesh", "mesh": "box", "x": 30, "z": 30, "y": 2.6, "width": 8, "height": 0.2, "depth": 8, "color": [0.88, 0.8, 0.66], "material": "Tiles" },
            { "op": "Mesh", "mesh": "cylinder", "x": 30, "z": -10, "width": 0.5, "depth": 0.5, "height": 3.3, "segments": 6, "color": [0.3, 0.22, 0.15] },
            { "op": "Mesh", "mesh": "cone", "x": 30, "z": -10, "y": 2.4, "width": 5, "depth": 5, "height": 8, "segments": 8, "color": [0.18, 0.34, 0.16] },
            { "op": "Spawn", "x": 35, "z": 35 }, { "op": "Spawn", "x": -35, "z": 35 }, { "op": "Spawn", "x": 35, "z": -35 }, { "op": "Spawn", "x": -35, "z": -35 }
          ],
          "foliage": {
            "seed": 3,
            "paths": [ { "points": [[24, 0], [38, 0]], "width": 3, "density": 0.25 } ],
            "regions": [
              { "type": "grass", "density": 6, "rect": [-39, -39, 39, 39], "patchiness": 0 },
              { "type": "bush", "density": 0.3, "rect": [-39, -39, 39, 39], "patchiness": 0 }
            ],
            "items": [ { "type": "tree", "x": 20, "z": 20, "height": 7 }, { "type": "pine", "x": 12, "z": 28 } ],
            "surfaces": { "Brick": 0.1 },
            "exclude": [ { "shape": "circle", "center": [15, 6], "radius": 5 } ],
            "include": [
              { "shape": "rect", "center": [-2.5, -25], "halfX": 2, "halfZ": 4 },
              { "shape": "circle", "center": [-21, -21], "radius": 1.5, "onTop": true }
            ]
          }
        }
        """;

    /// <summary>
    /// The automatic no-go rules (indoors, under roofs and bridges, inside geometry, pavement allowances), explicit exclude
    /// and include zones, the per-map switch, determinism, the cost on the biggest map, every foliage map's counts before
    /// and after, and that maps without foliage keep their fingerprint.
    /// </summary>
    private static void CheckFoliageNoGo()
    {
        NoGoNotes.Clear();
        try
        {
            NoGoLab();
            NoGoMaps();
        }
        finally
        {
            Map.LegacyFoliage = false;
            if (NoGoNotes.Count > 0)
                Line("::notice title=Foliage no-go::" + string.Join("%0A", NoGoNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static Map LoadLegacy(string json, string source)
    {
        Map.LegacyFoliage = true;
        try
        {
            return MapFile.Load(json, source);
        }
        finally
        {
            Map.LegacyFoliage = false;
        }
    }

    private static void NoGoLab()
    {
        var map = MapFile.Load(NoGoLabJson, "No-go Lab");
        var legacy = LoadLegacy(NoGoLabJson, "No-go Lab (before)");
        var all = map.FoliageInstances;
        NoGoLine($"lab: {legacy.FoliageInstances.Length} plants before, {all.Length} with the rules ({map.FoliageStats})");

        static bool In(in FoliageInstance f, float x0, float z0, float x1, float z1) =>
            f.Position.X >= x0 && f.Position.X <= x1 && f.Position.Z >= z0 && f.Position.Z <= z1;
        int Count(FoliageInstance[] list, float x0, float z0, float x1, float z1, Func<FoliageInstance, bool>? also = null) =>
            list.Count(f => In(f, x0, z0, x1, z1) && (also == null || also(f)));
        bool Ground(FoliageInstance f) => f.Position.Y < 0.5f;
        bool Grass(FoliageInstance f) => f.Type == FoliageType.Grass;

        // Closed building: nothing inside (the old placement put grass all over its floor).
        var inside = Count(all, -29.7f, -29.7f, -18.3f, -18.3f, Ground);
        var insideBefore = Count(legacy.FoliageInstances, -29.7f, -29.7f, -18.3f, -18.3f, Ground);
        // Rooftop planter: plants on the roof inside the include circle, none elsewhere up there.
        var onRoof = all.Count(f => f.Position.Y > 3.2f && Vector2.Distance(new(f.Position.X, f.Position.Z), new(-21, -21)) <= 1.5f);
        var strayRoof = all.Count(f => f.Position.Y > 0.5f && Vector2.Distance(new(f.Position.X, f.Position.Z), new(-21, -21)) > 1.5f);
        NoGoLine($"closed building: {insideBefore} plants inside before, {inside} now; rooftop planter {onRoof} plants, {strayRoof} elsewhere off the ground");
        if (insideBefore < 100)
            Fail($"the lab building should have had plenty of plants inside before the rules ({insideBefore})");
        if (inside > 0)
            Fail($"{inside} plants inside the closed building");
        if (onRoof < 3 || strayRoof > 0)
            Fail($"rooftop planter: {onRoof} plants in it (expected some), {strayRoof} off the ground elsewhere");
        if (all.Any(f => f.Position.Y > 0.5f && MathF.Abs(f.Position.Y - 3.3f) > 0.01f))
            Fail("a rooftop plant isn't standing on the roof (y 3.3)");

        // Open-sided shed: nothing under its roof, except in the include garden.
        var underShed = Count(all, -10f, -30f, 0f, -20f, f => Ground(f) && !In(f, -4.5f, -29f, -0.5f, -21f));
        var garden = Count(all, -4.5f, -29f, -0.5f, -21f, Ground);
        // Bridge: nothing under the deck. Cosmetic mesh roof: nothing under it either.
        var underBridge = Count(all, 10f, -30f, 22f, -26f);
        var underMeshRoof = Count(all, 26.1f, 26.1f, 33.9f, 33.9f);
        var underMeshRoofBefore = Count(legacy.FoliageInstances, 26.1f, 26.1f, 33.9f, 33.9f);
        NoGoLine($"open shed: {underShed} under the roof, {garden} in its include garden; bridge {underBridge} under the deck; mesh roof {underMeshRoofBefore} -> {underMeshRoof}");
        if (underShed > 0)
            Fail($"{underShed} plants under the open shed's roof (outside the include zone)");
        if (garden < 30)
            Fail($"the include garden under the shed has only {garden} plants (the include should override the roof rule)");
        if (underBridge > 0)
            Fail($"{underBridge} plants under the bridge deck");
        if (underMeshRoof > 0 || underMeshRoofBefore < 100)
            Fail($"cosmetic mesh roof: {underMeshRoof} plants under it now, {underMeshRoofBefore} before (expected 0 and plenty)");

        // Trees are not roofs: grass grows under the canopies of box trees and of the mesh-built tree.
        var underTrees = 0;
        foreach (var t in map.FoliageSection!.Items)
            underTrees += all.Count(f => Grass(f) && Vector2.Distance(new(f.Position.X, f.Position.Z), new(t.X, t.Z)) is > 1.3f and < 2.4f);
        var underMeshTree = all.Count(f => Grass(f) && Vector2.Distance(new(f.Position.X, f.Position.Z), new(30, -10)) is > 0.6f and < 2.2f);
        NoGoLine($"trees: {underTrees} grass clumps under the two tree canopies, {underMeshTree} under the mesh-built tree");
        if (underTrees < 20 || underMeshTree < 20)
            Fail($"plants under trees should stay ({underTrees} under trees, {underMeshTree} under the mesh tree)");

        // Pavement: intact concrete grows nothing; busted brick ("Brick": 0.1) about a tenth of the grass, no trees.
        var concrete = Count(all, -30f, 0f, -15f, 12f);
        var brick = Count(all, -9.8f, 0.2f, 1.8f, 11.8f, Grass);
        var brickBefore = Count(legacy.FoliageInstances, -9.8f, 0.2f, 1.8f, 11.8f, Grass);
        var brickBushes = Count(all, -9.8f, 0.2f, 1.8f, 11.8f, f => f.Type == FoliageType.Bush);
        var brickFraction = brickBefore > 0 ? brick / (float)brickBefore : 0f;
        NoGoLine($"pavement: intact concrete {concrete} plants; busted brick {brick} of {brickBefore} grass clumps ({brickFraction:P0}, configured 10%), {brickBushes} bushes");
        if (concrete > 0)
            Fail($"{concrete} plants on intact concrete paving");
        if (brickBefore < 400 || brickFraction is < 0.06f or > 0.14f)
            Fail($"busted paving kept {brickFraction:P1} of its grass (configured 10%, {brick} of {brickBefore})");

        // Explicit zones: the exclude circle is bare; the busted path keeps about a quarter of its grass.
        var excluded = all.Count(f => Vector2.Distance(new(f.Position.X, f.Position.Z), new(15, 6)) < 4.9f);
        var path = Count(all, 25f, -1f, 37f, 1f, Grass);
        var beside = Count(all, 25f, 4f, 37f, 6f, Grass);
        var pathFraction = beside > 0 ? path / (float)beside : 0f;
        NoGoLine($"zones: {excluded} plants in the exclude circle; busted path {path} grass clumps vs {beside} beside it ({pathFraction:P0}, configured 25%)");
        if (excluded > 0)
            Fail($"{excluded} plants in the exclude zone");
        if (pathFraction is < 0.12f or > 0.4f)
            Fail($"a path with density 0.25 kept {pathFraction:P0} of the grass beside it");

        // Open ground away from everything is untouched: the same plants as before the rules.
        var open = Count(all, -38f, 16f, -2f, 38f);
        var openBefore = Count(legacy.FoliageInstances, -38f, 16f, -2f, 38f);
        if (open != openBefore || open < 3000)
            Fail($"open ground changed: {openBefore} plants before, {open} now (should be identical)");
        else
            NoGoLine($"open meadow unchanged: {open} plants, same as before");

        // Deterministic: a second load gives bit-identical plants; the section survives a round trip.
        var again = MapFile.Load(NoGoLabJson, "No-go Lab (again)");
        if (!again.FoliageInstances.AsSpan().SequenceEqual(all) || again.FoliageHash != map.FoliageHash)
            Fail("the no-go rules placed different plants on a second load");
        var json = MapFile.ToJson(map);
        var copy = MapFile.Load(json, "No-go Lab (round trip)");
        if (!copy.FoliageInstances.AsSpan().SequenceEqual(all) || MapFile.ToJson(copy) != json)
            Fail("the foliage zones / surfaces don't survive a save/load round trip");
        if (!json.Contains("\"exclude\"") || !json.Contains("\"onTop\": true") || !json.Contains("\"Brick\": 0.1") || !json.Contains("\"density\": 0.25"))
            Fail("the written foliage section lost its zones, surfaces or path density");

        // "auto": false switches the automatic rules off (explicit zones still apply).
        var manual = MapFile.Load(NoGoLabJson.Replace("\"seed\": 3,", "\"seed\": 3, \"auto\": false,"), "No-go Lab (auto off)");
        var manualInside = Count(manual.FoliageInstances, -29.7f, -29.7f, -18.3f, -18.3f, Ground)
            + manual.FoliageInstances.Count(f => f.Position.Y > 3.2f); // the rooftop planter (an explicit include) still applies
        var manualExcluded = manual.FoliageInstances.Count(f => Vector2.Distance(new(f.Position.X, f.Position.Z), new(15, 6)) < 4.9f);
        if (manualInside != insideBefore || manualExcluded > 0)
            Fail($"\"auto\": false: {manualInside} plants in the building (expected {insideBefore}), {manualExcluded} in the exclude zone (expected 0)");
        if (manual.FoliageHash == map.FoliageHash)
            Fail("turning the automatic rules off didn't change the foliage fingerprint");

        // Bad sections give readable errors.
        foreach (var (bad, expect) in new[]
                 {
                     ("\"exclude\": [ { \"shape\": \"circle\", \"center\": [15, 6], \"radius\": 5 } ]", "\"exclude\": [ { \"shape\": \"circle\", \"radius\": 5, \"onTop\": true } ]", "has no \"onTop\""),
                     ("\"surfaces\": { \"Brick\": 0.1 }", "\"surfaces\": { \"Asphalt\": 0 }", "unknown surface"),
                     ("\"surfaces\": { \"Brick\": 0.1 }", "\"surfaces\": { \"Brick\": 2 }", "0 (nothing grows) to 1"),
                     ("\"exclude\": [ { \"shape\": \"circle\", \"center\": [15, 6], \"radius\": 5 } ]", "\"exclude\": [ { \"shape\": \"rect\", \"center\": [0, 0] } ]", "needs \"halfX\""),
                 }.Select(t => (NoGoLabJson.Replace(t.Item1, t.Item2), t.Item3)))
        {
            try
            {
                MapFile.Load(bad, "No-go Lab (broken)");
                Fail($"a broken foliage zone loaded without complaint (expected '{expect}')");
            }
            catch (MapFileException ex) when (ex.Message.Contains(expect))
            {
            }
            catch (MapFileException ex)
            {
                Fail($"broken foliage zone gave the wrong error: {ex.Message}");
            }
        }
    }

    private static void NoGoMaps()
    {
        var total = (Before: 0, After: 0, Indoors: 0);
        foreach (var m in Maps.All.Where(m => m.FoliageSection != null))
        {
            if (m.SourceFile == null || !File.Exists(Path.Combine(Maps.Folder, m.SourceFile)))
                continue;
            var text = File.ReadAllText(Path.Combine(Maps.Folder, m.SourceFile));
            var before = LoadLegacy(text, m.SourceFile);
            var again = MapFile.Load(text, m.SourceFile);
            var st = again.FoliageStats!;
            var pack = Maps.Packs.FirstOrDefault(p => p.Maps.Contains(m))?.Name ?? "?";
            NoGoLine($"{pack}/{m.Name}: {before.FoliageInstances.Length} -> {again.FoliageInstances.Length} plants: {st}");
            if (st.BySurface.Count > 0)
                NoGoLine($"  {m.Name} pavement drops by surface: {string.Join(", ", st.BySurface.Select(kv => $"{kv.Key} {kv.Value}"))}");
            total = (total.Before + before.FoliageInstances.Length, total.After + again.FoliageInstances.Length, total.Indoors + st.Indoors);
            var third = MapFile.Load(text, m.SourceFile);
            if (!again.FoliageInstances.AsSpan().SequenceEqual(third.FoliageInstances) || again.ContentHash != third.ContentHash)
                Fail($"{m.Name}: foliage differs between two loads (fingerprint {again.ContentHash:x8} vs {third.ContentHash:x8})");
            if (again.FoliageHash != m.FoliageHash)
                Fail($"{m.Name}: the foliage loaded now differs from the one loaded at start-up ({m.FoliageInstances.Length} plants, {m.FoliageHash:x8} vs {again.FoliageInstances.Length}, {again.FoliageHash:x8})");
            if (again.FoliageInstances.Length > before.FoliageInstances.Length)
                Fail($"{m.Name}: the no-go rules added plants ({before.FoliageInstances.Length} -> {again.FoliageInstances.Length})");
            if (st.PlantMs > NoGoPlantBudgetMs)
                Fail($"{m.Name}: planting took {st.PlantMs:0} ms (budget {NoGoPlantBudgetMs:0} ms)");
        }
        NoGoLine($"all foliage maps: {total.Before} -> {total.After} plants ({total.Indoors} indoors removed)");
        NoGoLine("fingerprints now: " + string.Join(", ", Maps.All.Where(m => m.FoliageSection != null && m.SourceFile != null)
            .Select(m => $"{Path.GetFileNameWithoutExtension(m.SourceFile)} {m.ContentHash:x8}")));

        // Maps without foliage are untouched: the rules never run, and their fingerprint is the same either way.
        var plain = Maps.All.Where(m => m.FoliageSection == null && m.SourceFile != null && File.Exists(Path.Combine(Maps.Folder, m.SourceFile))).Take(6).ToList();
        foreach (var m in plain)
        {
            var legacy = LoadLegacy(File.ReadAllText(Path.Combine(Maps.Folder, m.SourceFile!)), m.SourceFile!);
            if (legacy.ContentHash != m.ContentHash || m.FoliageHash != 0 || m.FoliageStats != null)
                Fail($"{m.Name}: a map without foliage changed (fingerprint {legacy.ContentHash:x8} vs {m.ContentHash:x8})");
        }
        NoGoLine($"{plain.Count} maps without foliage checked: same fingerprints, no foliage data");
    }
}
