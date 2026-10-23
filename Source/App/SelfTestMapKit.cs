using System.Diagnostics;
using System.Numerics;
using CallOfSlop.MapKit;

namespace CallOfSlop;

/// <summary>
/// "Map kit" self-test (Source/MapKit): every shipped map file round-trips through the kit's JSON tree (parse, write,
/// parse: the same tree, and the written text loads to the same map and fingerprint; Python-generated files come back
/// byte for byte); every registered generator's output is the committed file's map (same fingerprint, ops, spawns,
/// objectives); <see cref="MapChecks.Check"/> passes on the twelve Space maps (agreeing with their bespoke sections: no
/// failures, routes to every objective, fair walks); the kit's op validation refuses what the loader would; the symmetry
/// twins turn and mirror every op kind the way the Python kits did; the structures keep the movers' rules; the plan
/// writer makes a PNG. No bot matches.
/// </summary>
public static partial class SelfTest
{
    private static void CheckMapKit()
    {
        MapKitRoundTrips();
        MapKitGenerators();
        MapKitSymmetry();
        MapKitRules();
        MapKitSpaceChecks();
    }

    private static void MapKitRoundTrips()
    {
        int files = 0, bytes = 0;
        var sw = Stopwatch.StartNew();
        foreach (var m in Maps.All)
        {
            if (m.SourceFile == null)
                continue;
            var path = Path.Combine(Maps.Folder, m.SourceFile);
            if (!File.Exists(path))
                continue;
            files++;
            var text = File.ReadAllText(path);
            JNode tree;
            try
            {
                tree = JNode.Parse(text);
            }
            catch (Exception ex)
            {
                Fail($"map kit: {m.SourceFile} doesn't parse into the kit's JSON tree: {ex.Message}");
                continue;
            }
            var written = tree.Write();
            if (written == text)
                bytes++;
            if (!JNode.Parse(written).SameAs(tree))
                Fail($"map kit: {m.SourceFile} isn't the same tree after a write and a parse");
            try
            {
                var again = MapFile.Load(written, m.SourceFile);
                again.Validate();
                if (again.ContentHash != m.ContentHash || again.Ops.Count != m.Ops.Count || again.Spawns.Count != m.Spawns.Count)
                    Fail($"map kit: {m.SourceFile} written back by the kit loads differently (fingerprint {m.ContentHash:x8} -> {again.ContentHash:x8}, {m.Ops.Count} -> {again.Ops.Count} ops)");
            }
            catch (MapFileException ex)
            {
                Fail($"map kit: {m.SourceFile} written back by the kit doesn't load: {ex.Message}");
            }
        }
        Line($"map kit: {files} map files round-trip through the JSON tree ({bytes} byte for byte, the rest in different spelling), {sw.Elapsed.TotalSeconds:0.0} s");
        if (files == 0)
            Fail("map kit: no map files to round-trip");
        // Python float spelling.
        var spellings = new (double V, string S)[] { (1, "1.0"), (0, "0.0"), (-4, "-4.0"), (42.2, "42.2"), (0.001, "0.001"), (1e-5, "1e-05"), (1.5e17, "1.5e+17"), (-0.0, "-0.0"), (5.800000000000001, "5.800000000000001") };
        foreach (var (v, s) in spellings)
            if (JNode.PyFloat(v) != s)
                Fail($"map kit: {v:R} is written as {JNode.PyFloat(v)}, Python writes {s}");
    }

    private static void MapKitGenerators()
    {
        var mapsDir = GeneratorRegistry.RepoMapsFolder();
        foreach (var g in GeneratorRegistry.All)
        {
            var rel = GeneratorRegistry.RelativeFile(g);
            Kit kit;
            try
            {
                kit = GeneratorRegistry.Run(g);
            }
            catch (MapKitException ex)
            {
                Fail($"map kit: generator {g.Name} failed: {ex.Message}");
                continue;
            }
            var text = kit.Write();
            var committedPath = Path.Combine(mapsDir, rel);
            if (!File.Exists(committedPath))
                committedPath = Path.Combine(Maps.Folder, rel);
            if (!File.Exists(committedPath))
            {
                Fail($"map kit: generator {g.Name}: no committed file {rel}");
                continue;
            }
            var committedText = File.ReadAllText(committedPath);
            var verdict = MapKitCli.Compare(text, committedPath, rel);
            Map a, b;
            try
            {
                a = MapFile.Load(committedText, rel);
                b = MapFile.Load(text, rel);
            }
            catch (MapFileException ex)
            {
                Fail($"map kit: generator {g.Name}: a version doesn't load: {ex.Message}");
                continue;
            }
            var same = a.ContentHash == b.ContentHash && a.Ops.Count == b.Ops.Count && a.Spawns.SequenceEqual(b.Spawns)
                       && a.Objectives.Hills.SequenceEqual(b.Objectives.Hills) && a.Objectives.Flags.SequenceEqual(b.Objectives.Flags);
            Line($"map kit: generator {g.Name} -> {rel}: {verdict}; {kit.Summary()}");
            if (!same)
            {
                Fail($"map kit: generator {g.Name} doesn't reproduce {rel} (fingerprint {a.ContentHash:x8} -> {b.ContentHash:x8}, {a.Ops.Count} -> {b.Ops.Count} ops)");
                MapKitExplainDifference(a, b);
            }
            else if (verdict != "identical")
                Fail($"map kit: generator {g.Name} writes {rel} in different text ({verdict}); regenerate the committed file with --gen-map \"{g.Name}\"");
        }
    }

    /// <summary>The first few places two loaded maps differ (for a failing generator).</summary>
    private static void MapKitExplainDifference(Map a, Map b)
    {
        var shown = 0;
        for (var i = 0; i < Math.Min(a.Solid.Count, b.Solid.Count) && shown < 5; i++)
            if (a.Solid[i] != b.Solid[i])
            {
                Line($"  box {i}: {a.Solid[i]} vs {b.Solid[i]}");
                shown++;
            }
        if (a.Solid.Count != b.Solid.Count)
            Line($"  {a.Solid.Count} vs {b.Solid.Count} boxes");
        for (var i = 0; i < Math.Min(a.SolidMeshes.Count, b.SolidMeshes.Count) && shown < 10; i++)
        {
            var pa = a.MeshPlacements[a.SolidMeshes[i]];
            var pb = b.MeshPlacements[b.SolidMeshes[i]];
            if (pa.Transform != pb.Transform || pa.Source != pb.Source || pa.Mesh?.Bounds != pb.Mesh?.Bounds)
            {
                Line($"  solid mesh {i} ({pa.Source}): transform {pa.Transform.Translation} vs {pb.Transform.Translation}, bounds {pa.Mesh?.Bounds} vs {pb.Mesh?.Bounds}");
                shown++;
            }
        }
        if (a.SolidMeshes.Count != b.SolidMeshes.Count)
            Line($"  {a.SolidMeshes.Count} vs {b.SolidMeshes.Count} solid meshes");
        for (var i = 0; i < Math.Min(a.Spawns.Count, b.Spawns.Count) && shown < 12; i++)
            if (a.Spawns[i] != b.Spawns[i])
            {
                Line($"  spawn {i}: {a.Spawns[i]} vs {b.Spawns[i]}");
                shown++;
            }
        for (var i = 0; i < Math.Min(a.Waypoints.Count, b.Waypoints.Count) && shown < 14; i++)
            if (a.Waypoints[i] != b.Waypoints[i])
            {
                Line($"  waypoint {i}: {a.Waypoints[i]} vs {b.Waypoints[i]}");
                shown++;
            }
        if (a.Waypoints.Count != b.Waypoints.Count)
            Line($"  {a.Waypoints.Count} vs {b.Waypoints.Count} waypoints");
        for (var t = 0; t < 2; t++)
            if (!a.Objectives.TeamSpawns[t].SequenceEqual(b.Objectives.TeamSpawns[t]))
                Line($"  team {t} spawns differ: {string.Join(" ", a.Objectives.TeamSpawns[t])} vs {string.Join(" ", b.Objectives.TeamSpawns[t])}");
        if (a.HalfX != b.HalfX || a.HalfZ != b.HalfZ)
            Line($"  size {a.HalfX} x {a.HalfZ} vs {b.HalfX} x {b.HalfZ}");
        if (a.PlanetHash != b.PlanetHash)
            Line($"  planet / solid mesh / gravity hash {a.PlanetHash:x8} vs {b.PlanetHash:x8}");
        if (a.BuildingHash != b.BuildingHash || a.FoliageHash != b.FoliageHash || a.PackAssetHash != b.PackAssetHash)
            Line($"  building / foliage / pack asset hashes {a.BuildingHash:x8} {a.FoliageHash:x8} {a.PackAssetHash:x8} vs {b.BuildingHash:x8} {b.FoliageHash:x8} {b.PackAssetHash:x8}");
        if (a.PropHash != b.PropHash)
            Line($"  prop hash {a.PropHash:x8} vs {b.PropHash:x8}");
        if (a.BoundaryHash != b.BoundaryHash)
            Line($"  boundary hash {a.BoundaryHash:x8} vs {b.BoundaryHash:x8}");
    }

    private static void MapKitSymmetry()
    {
        var k = new Kit("Kit test", "a kit self-test", 9999);
        // A full set of ops on one side...
        k.Block(1, 0, 2, 3, 1, 4, "#808080");
        k.Collider(1, 0, 2, 3, 1, 4);
        k.Mesh("box", 5, 6, 0, "#ffffff", width: 2, yaw: 30, roll: 10);
        k.Prop("Oil Drum", 7, 8, 0, 45);
        k.Decal("global:Oil Stain", 1, 2, 0, size: 2, rotation: 30);
        k.Decal("global:Hazard Stripes", 1, 2, 1.5, "wall+x", size: 1, rotation: 0);
        k.DecalScatter(["global:Rust Stain"], 0, 0, 4, 6, 3, 7);
        k.Clutter(["Oil Drum"], 0, 0, 4, 6, 2, 9);
        k.Waypoint(2, 3, 1);
        k.Spawn(0, 4, 5);
        k.Building("Small House", 20, 10, 0, 90, 3);
        k.WallX(0, 10, 5, 0.4, 0, 3, "#aaaaaa", Kit.Door(4));
        k.WallZ(0, 10, 5, 0.4, 0, 3, "#aaaaaa", Kit.Window(6));
        k.Slab(0, 0, 10, 10, 3.2, 0.3, "#999999", (2, 2, 4, 4));
        k.StairsZ(0, 1.5, 2, 1, 0, 3.2, "#777777");
        k.Ramp(1, 2, 3, 2, true, 1, "#777777");
        k.Hill(10, 0, 5);
        k.Flag(-30, 0);
        var ops = k.Ops.ToList();
        foreach (var sym in new[] { Symmetry.Turn180, Symmetry.MirrorX, Symmetry.MirrorZ })
        {
            var twins = Kit.Twins(ops, sym, 100);
            if (twins.Count != ops.Count)
                Fail($"map kit: {sym} made {twins.Count} twins of {ops.Count} ops");
            foreach (var t in twins)
                Kit.Validate(t);
            // Twinning twice under a mirror is the identity; under Turn180 too.
            var back = Kit.Twins(twins, sym, -100);
            for (var i = 0; i < Math.Min(back.Count, ops.Count); i++)
                if (!MapKitSameOp(back[i], ops[i]))
                    Fail($"map kit: {sym} twice isn't the identity for {ops[i].Str("op")}: {ops[i]} -> {back[i]}");
        }
        // The known answers: a block turned, a prop mirrored, a wall decal's side, stairs' dir.
        var turned = Kit.Twin(ops[0], Symmetry.Turn180)!;
        if (turned.Num("x0") != -3 || turned.Num("x1") != -1 || turned.Num("z0") != -4 || turned.Num("z1") != -2)
            Fail($"map kit: a block turned 180 degrees came out as {turned}");
        var prop = Kit.Twin(ops[3], Symmetry.MirrorX)!;
        if (prop.Num("x") != -7 || prop.Num("z") != 8 || prop.Num("yaw") != 315)
            Fail($"map kit: a prop mirrored in x came out as {prop}");
        var propZ = Kit.Twin(ops[3], Symmetry.MirrorZ)!;
        if (propZ.Num("x") != 7 || propZ.Num("z") != -8 || propZ.Num("yaw") != 135)
            Fail($"map kit: a prop mirrored in z came out as {propZ}");
        var wallDecal = Kit.Twin(ops[5], Symmetry.MirrorX)!;
        if (wallDecal.Str("onto") != "wall-x" || Kit.Twin(ops[5], Symmetry.MirrorZ)!.Str("onto") != "wall+x" || Kit.Twin(ops[5], Symmetry.Turn180)!.Str("onto") != "wall-x")
            Fail("map kit: a wall decal's side didn't flip the right way");
        var stairs = Kit.Twin(ops[14], Symmetry.Turn180)!;
        if (stairs.Num("dir") != -1 || stairs.Num("z") != -2 || stairs.Num("x0") != -1.5)
            Fail($"map kit: stairs turned 180 degrees came out as {stairs}");
        if (Kit.Twin(ops[14], Symmetry.MirrorX)!.Num("dir") != 1 || Kit.Twin(ops[14], Symmetry.MirrorZ)!.Num("dir") != -1)
            Fail("map kit: stairs' climbing direction flipped under the wrong mirror");
        var building = Kit.Twin(ops[10], Symmetry.MirrorZ)!;
        if (building.Num("yaw") != 90 || building.Get("variant") is not JObject v || v.Get("mirror") is not JBool { Value: true })
            Fail($"map kit: a building mirrored in z came out as {building}");
        // Symmetric(): Red's spawns, hills and flags follow Blue's; a hill on the line stays single.
        var s = new Kit("Sym test", "", 9999);
        s.Symmetric(Symmetry.Turn180, () =>
        {
            s.Spawn(0, -10, -5, 1);
            s.Hill(0, 0, 5);
            s.Hill(-20, 3, 4);
            s.Flag(-30, 0);
            s.Block(-5, 0, -5, -4, 1, -4, "#808080");
        });
        if (s.TeamSpawns[1].Count != 1 || s.TeamSpawns[1][0][0] != 10 || s.TeamSpawns[1][0][2] != 5 || s.TeamSpawns[1][0][1] != 1)
            Fail($"map kit: Symmetric didn't mirror the spawns ({string.Join("; ", s.TeamSpawns[1].Select(p => string.Join(",", p)))})");
        if (s.Hills.Count != 3 || s.Hills[2].Num("x") != 20 || s.Hills[2].Num("z") != -3 || s.Flags.Count != 2 || s.Flags[1][0] != 30 || s.Ops.Count != 4)
            Fail($"map kit: Symmetric made {s.Hills.Count} hills, {s.Flags.Count} flags, {s.Ops.Count} ops");
        var doc = s.ToDocument();
        if (doc.Items.Select(kv => kv.Key).ToList() is not ["format", "name", "description", "author", "order", "maxPlayers", "teamSpawns", "hills", "flags", "ops"])
            Fail($"map kit: the document's keys are in the wrong order: {string.Join(", ", doc.Items.Select(kv => kv.Key))}");
        Line("map kit: twins under a turn and both mirrors validate, invert and match the known answers; Symmetric() builds the other team's half");
    }

    private static bool MapKitSameOp(JObject a, JObject b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var kv in a.Items)
        {
            var o = b.Get(kv.Key);
            if (o == null)
                return false;
            if (kv.Value is JNumber na && o is JNumber nb)
            {
                if (Math.Abs(na.Value - nb.Value) > 1e-6)
                    return false;
            }
            else if (kv.Value is JObject oa && o is JObject ob)
            {
                if (!MapKitSameOp(oa, ob))
                    return false;
            }
            else if (kv.Value is JArray aa && o is JArray ab)
            {
                if (aa.Count != ab.Count)
                    return false;
                for (var i = 0; i < aa.Count; i++)
                    if (aa[i] is JNumber x && ab[i] is JNumber y ? Math.Abs(x.Value - y.Value) > 1e-6 : aa[i] is JObject xo && ab[i] is JObject yo ? !MapKitSameOp(xo, yo) : !aa[i].SameAs(ab[i]))
                        return false;
            }
            else if (!kv.Value.SameAs(o))
                return false;
        }
        return true;
    }

    private static void MapKitRules()
    {
        // Validation: unknown ops, unknown and missing parameters, wrong types.
        var bad = new (string What, JObject Op)[]
        {
            ("an unknown op", new JObject(("op", "Blocky"))),
            ("an unknown parameter", new JObject(("op", "Spawn"), ("x", 1.0), ("z", 2.0), ("yaw", 3.0))),
            ("a missing parameter", new JObject(("op", "Block"), ("x0", 0.0), ("y0", 0.0), ("z0", 0.0), ("x1", 1.0), ("y1", 1.0), ("z1", 1.0))),
            ("a decimal where a whole number goes", new JObject(("op", "Clutter"), ("props", "Oil Drum"), ("x0", 0.0), ("z0", 0.0), ("x1", 1.0), ("z1", 1.0), ("count", 2.5))),
            ("text where a colour goes", new JObject(("op", "Ground"), ("color", "grey"))),
            ("a bad anchor", new JObject(("op", "Waypoint"), ("x", 1.0), ("z", 2.0), ("anchor", "sky"))),
        };
        foreach (var (what, op) in bad)
        {
            try
            {
                Kit.Validate(op);
                Fail($"map kit: validation let through {what}: {op}");
            }
            catch (MapKitException)
            {
            }
        }
        // Prop with a name is PlaceProp; Building with a name is PlaceBuilding (the loader's rule).
        Kit.Validate(new JObject(("op", "Prop"), ("name", "Oil Drum"), ("x", 1.0), ("z", 2.0), ("yaw", 90.0)));
        Kit.Validate(new JObject(("op", "Building"), ("name", "Small House"), ("at", JArray.Of(1.0, 2.0)), ("yaw", 90), ("seed", 3)));
        // The structures keep the movers' rules.
        var k = new Kit("Rules", "", 9999);
        foreach (var (what, act) in new (string, Action)[]
                 {
                     ("a 0.6 m riser", () => Structures.Stairs(k, 0, 0, 0, 3.0, 0, 1, steps: 5)),
                     ("a 0.8 m wide flight", () => Structures.Stairs(k, 0, 0, 0, 3.0, 0, 1, width: 0.8)),
                     ("a 1.7 m tread", () => Structures.Stairs(k, 0, 0, 0, 3.0, 0, 1, tread: 1.7)),
                     ("a 40 degree gangway", () => Structures.Gangway(k, 0, 0, 0, 10, 0, 8.4, 2)),
                     ("0.9 m wide StairsZ", () => k.StairsZ(0, 0.9, 0, 1, 0, 3.2, "#777777")),
                 })
        {
            try
            {
                act();
                Fail($"map kit: the structures accepted {what}");
            }
            catch (MapKitException)
            {
            }
        }
        var before = k.Ops.Count;
        var top = Structures.Stairs(k, 0, 0, 0, 3.0, 0, 1, width: 1.6);
        var steps = k.Ops.Count(o => o.Str("op") == "Collider");
        if (steps != 10 || Math.Abs(top.Y - 3.0) > 1e-9 || Math.Abs(top.Z - 3.2) > 1e-9)
            Fail($"map kit: a 3 m flight made {steps} step colliders, top at ({top.X}, {top.Y}, {top.Z})");
        Structures.Railing(k, 0, 0, 5, 10, 0, gaps: [(5, 1)]);
        var rails = k.Ops.Skip(before).Where(o => o.Str("op") == "Collider" && o.Num("y0") >= 5.44).ToList();
        if (rails.Count == 0 || rails.Any(o => Math.Abs(o.Num("y0") - 5.45) > 1e-6 || Math.Abs(o.Num("y1") - 6.1) > 1e-6) || rails.Any(o => o.Num("x0") < 4.01 && o.Num("x1") > 5.99))
            Fail($"map kit: the railing's collider band isn't 0.45-1.1 m above the deck with the gap left open ({rails.Count} pieces)");
        Structures.RampColliders(k, 0, 0, 0, 10, 0, 1.0, 2);
        var ramp = k.Ops.Skip(before).Where(o => o.Str("op") == "Collider" && o.Num("x1") - o.Num("x0") < 0.5 && o.Num("y1") <= 1.0 && o.Num("z0") == -1).ToList();
        if (ramp.Count != 25)
            Fail($"map kit: a 1 m ramp over 10 m made {ramp.Count} collider steps (wanted 25 of 4 cm)");
        // Geometry: an outline from a half, inside / area, the seeded generator.
        var outline = MapKit.Geometry.FromHalf([(0, -10), (10, -10), (10, 10), (0, 10)], Symmetry.MirrorX);
        if (outline.Count != 6 || Math.Abs(MapKit.Geometry.Area(outline) - 400) > 1e-6 || !MapKit.Geometry.Inside(outline, 5, 5) || MapKit.Geometry.Inside(outline, 15, 0))
            Fail($"map kit: an outline from a half has {outline.Count} points and area {MapKit.Geometry.Area(outline)}");
        var rng = new Rng(11);
        var first = rng.Next();
        if (Math.Abs(first - new Rng(11).Next()) > 0 || first is < 0 or >= 1)
            Fail("map kit: the seeded generator isn't deterministic");
        var kitTwin = Kit.TwinYaw(30, Symmetry.MirrorX);
        if (kitTwin != -30 || Kit.TwinYaw(30, Symmetry.MirrorZ) != 150 || Kit.TwinYaw(30, Symmetry.Turn180) != 210)
            Fail("map kit: the yaw twins are wrong");
        // A plan PNG of the first map.
        var png = Path.Combine(Path.GetTempPath(), $"callofslop-plan-{Guid.NewGuid():N}.png");
        try
        {
            Plan.Draw(Maps.All[0], png);
            var bytes = File.ReadAllBytes(png);
            var (w, h, _) = Png.Decode(bytes);
            if (w < 64 || h < 64)
                Fail($"map kit: the plan PNG is {w} x {h}");
            Line($"map kit: validation refuses what the loader would; stairs, railings and ramps keep the movers' rules; plan of {Maps.All[0].Name} is a {w} x {h} PNG ({bytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            Fail($"map kit: the plan PNG failed: {ex.Message}");
        }
        finally
        {
            try { File.Delete(png); } catch (IOException) { }
        }
    }

    /// <summary>MapChecks.Check agrees with the bespoke Space sections: every Space map passes with routes to every objective.</summary>
    private static void MapKitSpaceChecks()
    {
        var space = Maps.All.Where(m => m.Pack?.Name == "Space").ToList();
        if (space.Count < 12)
            Fail($"map kit: {space.Count} Space maps (expected 12)");
        var sw = Stopwatch.StartNew();
        var passed = 0;
        foreach (var m in space)
        {
            var r = MapChecks.Check(m, new CheckOptions { Stairs = false }); // (the Stairs section walks every flight already)
            Line($"  {r.MapName}: {(r.Passed ? "OK" : $"{r.Failures.Count} problem(s)")}, nav {r.NavNodes} nodes ({r.NavMainNodes} main) in {r.NavBuildMs:0} ms, "
                 + $"walks {string.Join(", ", r.Walks.Select(w => $"{w.Target} {w.Blue:0}/{w.Red:0}"))}");
            foreach (var f in r.Failures)
                Fail($"map kit: Check({m.Name}): {f}");
            if (r.Passed)
                passed++;
            if (r.Walks.Count < 3 && m.PointGravity == null)
                Fail($"map kit: Check({m.Name}) found only {r.Walks.Count} objectives to walk to");
        }
        Line($"map kit: MapChecks.Check passes on {passed} of {space.Count} Space maps ({sw.Elapsed.TotalSeconds:0.0} s)");
    }
}
