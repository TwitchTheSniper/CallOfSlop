using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Pack-local assets (Source/Maps/MapPackAssets.cs, Source/Props/PropPacks.cs): a map pack's own Props and Models
/// folders come before the global ones, a pack prop shadows a global one of the same name for that pack's maps only,
/// two packs can each have a prop of the same name, "global:" forces the global asset, pack-local assets count towards
/// the map fingerprint, every shipped map keeps the fingerprint it had before pack assets existed, and the cost.
/// </summary>
public static partial class SelfTest
{
    /// <summary>Every shipped map's fingerprint on origin/main before pack-local assets (commit 431f4e0); maps whose stairs were rebuilt
    /// for 2.1 m headroom (fix/stair-headroom) carry their new Windows values.</summary>
    private static readonly (string File, uint Hash)[] PrePackAssetFingerprints =
    [
        ("Classic/01-ProtoBox.json", 0xa83dd7a5u), ("Classic/02-Courtyard.json", 0x9b62bfadu), ("Classic/03-Crossroads.json", 0x04fab23au),
        ("Classic/04-Warehouse9.json", 0x0d1b5095u), ("Classic/05-LongShot.json", 0x9a88c69cu), ("Classic/06-Ziggurat.json", 0xd8b982f8u),
        ("Classic/07-Scrapyard.json", 0xdece740cu), ("Classic/08-Rooftops.json", 0x3601e902u), ("Classic/09-Harbor.json", 0x56f96d12u),
        ("Classic/10-MarketStreet.json", 0xb8c3562au), ("Classic/11-Quarry.json", 0xc0f1d401u), ("Classic/12-Trenches.json", 0xb1bd94b3u),
        ("Classic/13-TrainYard.json", 0xd94dde9du), ("Classic/14-OfficePark.json", 0xbf692394u), ("Classic/15-Ruins.json", 0xe93ec317u),
        ("Classic/16-Overpass.json", 0x5b03f45fu), ("Classic/17-Farmland.json", 0xfcc5a090u), ("Classic/18-Downtown.json", 0x2a9960f5u),
        ("Classic/19-Canyon.json", 0x03353c42u), ("Classic/20-Airfield.json", 0xf47d65a6u), ("Classic/21-Village.json", 0x8004499bu),
        ("Classic/22-Industrial.json", 0x87c760edu), ("Classic/23-Highlands.json", 0xe932e450u), ("Classic/24-MilitaryBase.json", 0xe6122c44u),
        ("CollisionPoint/01-Blueprint.json", 0xde9e3279u), ("CollisionPoint/02-Galleria.json", 0x429afd78u), ("CollisionPoint/03-Floodgate.json", 0xcb35e9deu),
        ("CollisionPoint/04-Trefoil.json", 0x39b08239u), ("CollisionPoint/05-Citadel.json", 0x340e65a1u), ("CollisionPoint/06-Confluence.json", 0x3aad3033u),
        ("HighGround/01-GravelPit.json", 0x1010fd2au), ("HighGround/02-Hillfort.json", 0xddcb110bu), ("HighGround/03-Riverbend.json", 0xa7be54dau),
        ("HighGround/04-AdobeMesa.json", 0xb9ca18aau), ("HighGround/05-Ridgeline.json", 0xb945fc4au), ("HighGround/06-AlpinePass.json", 0x61c5781cu),
        ("Cluttered/01-Carousel.json", 0x28a0993cu), ("Cluttered/02-CrescentBazaar.json", 0xa26b8e27u), ("Cluttered/03-Sprawl.json", 0x8a64c7d3u),
        ("Dev/FoliageTest.json", 0x8a733893u), ("Dev/TerrainTest.json", 0x6ad80326u), ("Dev/PhysicsTest.json", 0x476984bcu),
        ("Dev/PropGallery.json", 0x3e664c24u), ("Dev/PropYard.json", 0x59b1babcu), ("Dev/PropGallery2.json", 0xf6e903c4u),
        ("Dev/PropGallery3.json", 0x9cf4b336u), ("Dev/AnimatedProps.json", 0xea9b40a0u), ("Dev/MeshTest.json", 0xb7a01a63u),
        ("Dev/ShapeTest.json", 0x6b175b8cu), ("Dev/SkyTest.json", 0x5fbf936fu), ("Dev/WorkbenchRange.json", 0xaaeeb152u),
    ];

    private static void CheckPackAssets()
    {
        // 1. Every shipped map still loads with the fingerprint it had before.
        var bySource = Maps.All.Where(m => m.SourceFile != null).ToDictionary(m => m.SourceFile!.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var same = 0;
        foreach (var (file, hash) in PrePackAssetFingerprints)
        {
            if (!bySource.TryGetValue(file, out var m))
                Fail($"pinned map {file} didn't load");
            else if (m.ContentHash != hash && !OperatingSystem.IsWindows())
                Line($"{file}: fingerprint {m.ContentHash:x8} here, {hash:x8} pinned on Windows (the pins are Windows values: Linux's maths library rounds some trig differently; not a failure off Windows)");
            else if (m.ContentHash != hash)
                Fail($"{file}: fingerprint {m.ContentHash:x8}, was {hash:x8} before pack assets (a shipped map changed)");
            else if (m.PackAssetHash != 0)
                Fail($"{file}: uses pack-local assets ({m.PackAssetHash:x8}) but none shipped before");
            else
                same++;
        }
        Line($"{same} of {PrePackAssetFingerprints.Length} shipped maps keep their pre-pack-asset fingerprint (Prop Gallery 3 now names \"global:Apple Crate\")");

        // 2. The Dev pack's example: Maps/Dev/Props/AppleCrate.json shadows the global Apple Crate, Maps/Dev/Models/dev_beacon.glb.
        CheckDevPackExample();

        // 3. Made-up packs: resolution order, namespacing, shadowing, global:, fingerprints, models.
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-packassets-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckPackAssetFolders(dir);
        }
        finally
        {
            foreach (var pack in new[] { "PackA", "PackB", "PackC", "PackD" })
                Props.ForgetPack(Path.Combine(dir, pack));
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        // 4. Sky images: next to the map, then the pack folder, then Maps; global: skips the pack.
        CheckPackSkyImages();

        // 5. Cost: lazy per pack, cached.
        CheckPackAssetCost();
    }

    private static void CheckDevPackExample()
    {
        if (Maps.Find("Pack Assets") is not { } map)
        {
            Fail("Maps/Dev/PackAssets.json ('Pack Assets') didn't load");
            return;
        }
        var placed = map.PropInstances.Take(map.ClutterStart).ToList(); // the hand-placed ones (the clutter below picks pack crates too)
        var pack = placed.Where(p => p.Def.Name == "Apple Crate" && p.Def.Pack == "Dev").ToList();
        var global = placed.Where(p => p.Def.Name == "Apple Crate" && p.Def.Pack == null).ToList();
        var clutter = map.PropInstances.Skip(map.ClutterStart).ToList();
        if (pack.Count != 3 || global.Count != 3)
            Fail($"Pack Assets: {pack.Count} pack Apple Crates and {global.Count} global ones (expected 3 and 3)");
        if (clutter.Count == 0 || clutter.Any(p => p.Def.Pack != "Dev"))
            Fail($"Pack Assets: the \"Dev Pack\" clutter placed {clutter.Count} props, {clutter.Count(p => p.Def.Pack != "Dev")} not from the pack");
        if (pack.Count > 0 && global.Count > 0 && (pack[0].Def.Key == global[0].Def.Key || pack[0].Def.Bounds.Half == global[0].Def.Bounds.Half))
            Fail("Pack Assets: the pack Apple Crate isn't a different prop from the global one");
        var scope = map.PropScope;
        if (scope.Pack != "Dev" || !scope.Shadowed.Contains("Apple Crate", StringComparer.OrdinalIgnoreCase))
            Fail($"Dev pack scope: pack {scope.Pack ?? "(none)"}, shadows [{string.Join(", ", scope.Shadowed)}] (expected Dev, Apple Crate)");
        if (Props.Find("Apple Crate") is not { Pack: null })
            Fail("the global library's Apple Crate is not the global one");
        if (Maps.Find("Prop Gallery 3") is { } g3 && g3.PropInstances.Any(p => p.Def.Pack != null))
            Fail("Prop Gallery 3 shows a pack prop (its Apple Crate should be global:)");
        var models = map.MeshPlacements.Where(p => p.Model != null).ToList();
        var beacons = models.Count(p => p.Source == "Models/dev_beacon.glb");
        if (beacons != 2 || !map.PackModels.Contains("models/dev_beacon.glb") || map.PackModels.Count != 1)
            Fail($"Pack Assets: {beacons} beacons placed, pack models [{string.Join(", ", map.PackModels)}] (expected 2 and the Dev beacon only; the barrel is global)");
        if (map.PackAssetHash == 0)
            Fail("Pack Assets: no pack-asset fingerprint");
        var again = Validated(MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, map.SourceFile!)), map.SourceFile!)); // pack worked out from the path, like --check-map
        var roundTrip = Validated(MapFile.Load(MapFile.ToJson(map), "Pack Assets (round trip)", map.PackDir));
        if (again.ContentHash != map.ContentHash || roundTrip.ContentHash != map.ContentHash)
            Fail($"Pack Assets: fingerprint {map.ContentHash:x8}, reloaded {again.ContentHash:x8}, round trip {roundTrip.ContentHash:x8}");
        var batches = PropBatches.Build(map, PropMeshes.Build(scope.Library));
        if (batches.SlotOf.Any(s => s < 0) || batches.Types < 2)
            Fail($"Pack Assets: {batches.SlotOf.Count(s => s < 0)} props missing from the render batches ({batches.Types} types)");
        // Maps outside the Dev pack can't see its props or models.
        foreach (var (op, what) in new[] { ("{ \"op\": \"Clutter\", \"props\": [\"Dev Pack\"], \"x0\": -5, \"z0\": -5, \"x1\": 5, \"z1\": 5, \"count\": 3 }", "the Dev Pack category"),
                     ("{ \"op\": \"Model\", \"file\": \"Models/dev_beacon.glb\", \"x\": 0, \"z\": 0 }", "the Dev beacon model") })
        {
            try
            {
                MapFile.Load(PackTestMap("Leak Test", op), "Classic/LeakTest.json");
                Fail($"a Classic map could use {what} from the Dev pack");
            }
            catch (MapFileException)
            {
            }
        }
        Line($"Dev pack example: {pack.Count} pack + {global.Count} global Apple Crates, {clutter.Count} pack clutter props, {beacons} pack models, "
             + $"fingerprint {map.ContentHash:x8} (pack assets {map.PackAssetHash:x8}); Classic maps can't see them");
    }

    /// <summary>A loaded map validated like the map list does (spawns and waypoints snapped and fixed), so fingerprints compare.</summary>
    private static Map Validated(Map m)
    {
        m.Validate();
        return m;
    }

    private static string PackTestMap(string name, params string[] ops) => $$"""
        { "name": "{{name}}", "halfX": 20, "halfZ": 20, "ops": [
          { "op": "Ground", "color": [0.4, 0.4, 0.4] },
          {{string.Join(",\n  ", ops)}},
          { "op": "RingSpawns", "count": 8, "frac": 0.8 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
        """;

    private static string PackTestProp(string name, float size, string color, string category = "Test") => $$"""
        { "format": "call-of-slop-prop/1", "name": "{{name}}", "category": "{{category}}",
          "parts": [ { "size": [{{size}}, {{size}}, {{size}}], "color": "{{color}}" } ] }
        """;

    private static void CheckPackAssetFolders(string dir)
    {
        void Write(string rel, string text)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        var beacon = Path.Combine(Maps.Folder, "Dev", "Models", "dev_beacon.glb");
        var barrel = Path.Combine(MeshLibrary.ModelsFolder, "barrel.glb");
        if (!File.Exists(beacon) || !File.Exists(barrel))
        {
            Fail("Maps/Dev/Models/dev_beacon.glb or Models/barrel.glb missing");
            return;
        }
        // PackA and PackB each have a "Test Crate" (different sizes); PackA also shadows the global Traffic Cone and Models/barrel.glb.
        Write("PackA/Props/Crate.json", PackTestProp("Test Crate", 0.6f, "red"));
        Write("PackA/Props/Street/Cone.json", PackTestProp("Traffic Cone", 1.5f, "blue", "Street"));
        Write("PackB/Props/Crate.json", PackTestProp("Test Crate", 1.2f, "green"));
        Directory.CreateDirectory(Path.Combine(dir, "PackA", "Models"));
        File.Copy(beacon, Path.Combine(dir, "PackA", "Models", "barrel.glb"));
        Write("PackA/a.json", PackTestMap("Pack A Map",
            "{ \"op\": \"Prop\", \"name\": \"Test Crate\", \"x\": -4, \"z\": 0 }",
            "{ \"op\": \"Prop\", \"name\": \"Traffic Cone\", \"x\": 0, \"z\": 0 }",
            "{ \"op\": \"Prop\", \"name\": \"global:Traffic Cone\", \"x\": 4, \"z\": 0 }",
            "{ \"op\": \"Model\", \"file\": \"Models/barrel.glb\", \"x\": -4, \"z\": 6 }",
            "{ \"op\": \"Model\", \"file\": \"global:Models/barrel.glb\", \"x\": 4, \"z\": 6 }"));
        Write("PackB/sub/b.json", PackTestMap("Pack B Map", "{ \"op\": \"Prop\", \"name\": \"Test Crate\", \"x\": -4, \"z\": 0 }",
            "{ \"op\": \"Prop\", \"name\": \"Traffic Cone\", \"x\": 0, \"z\": 0 }"));
        var errors = new List<string>();
        var (all, _) = Maps.LoadFolder(dir, errors);
        var a = all.FirstOrDefault(m => m.Name == "Pack A Map");
        var b = all.FirstOrDefault(m => m.Name == "Pack B Map");
        if (errors.Count > 0 || a == null || b == null)
        {
            Fail($"test packs: {all.Length} maps (expected 2: prop files in Props aren't maps), errors: {string.Join(" | ", errors)}");
            return;
        }
        PropDef? Def(Map m, int i) => i < m.PropInstances.Count ? m.PropInstances[i].Def : null;
        // Namespacing: each pack gets its own Test Crate.
        if (Def(a, 0) is not { Pack: "PackA" } ca || Def(b, 0) is not { Pack: "PackB" } cb || ca.Key == cb.Key || ca.Bounds.Half == cb.Bounds.Half)
            Fail($"two packs with a \"Test Crate\" clash: {Def(a, 0)?.Key} vs {Def(b, 0)?.Key}");
        // Shadowing: PackA's Traffic Cone for PackA only; global: still reaches the global one; PackB (nested map) sees the global one.
        if (Def(a, 1) is not { Pack: "PackA" } || Def(a, 2) is not { Pack: null, Name: "Traffic Cone" } || Def(b, 1) is not { Pack: null })
            Fail($"shadowing: PackA cone {Def(a, 1)?.Key}, PackA global: cone {Def(a, 2)?.Key}, PackB cone {Def(b, 1)?.Key}");
        var scopeA = a.PropScope;
        if (!scopeA.Shadowed.Contains("Traffic Cone") || scopeA.InCategory("Street").Count(p => p.Name == "Traffic Cone") != 1
            || scopeA.InCategory("Street").First(p => p.Name == "Traffic Cone").Pack != "PackA" || scopeA.InCategory("global:Street").Any(p => p.Pack != null))
            Fail("shadowing: PackA's Street category should hold its own Traffic Cone instead of the global one (and global:Street only global props)");
        // Models: the pack's Models/barrel.glb first, global: the shipped one.
        var models = a.MeshPlacements.Where(p => p.Model != null).ToList();
        if (models.Count != 2 || ReferenceEquals(models[0].Model, models[1].Model) || models[0].Model!.Triangles == models[1].Model!.Triangles
            || a.PackModels.Count != 1 || a.PackModels.Single() != "models/barrel.glb")
            Fail($"models: {models.Count} placed, pack models [{string.Join(", ", a.PackModels)}] (expected the pack barrel and the global one)");
        // The renderer's library for PackA holds every global prop plus PackA's, and every instance finds its mesh.
        var batches = PropBatches.Build(a, PropMeshes.Build(scopeA.Library));
        if (scopeA.Library.Length != Props.All.Length + 2 || batches.SlotOf.Any(s => s < 0))
            Fail($"render library for PackA: {scopeA.Library.Length} props (expected {Props.All.Length + 2}), {batches.SlotOf.Count(s => s < 0)} instances without a mesh");
        // Unknown names suggest from the pack too.
        try
        {
            MapFile.Load(PackTestMap("Typo", "{ \"op\": \"Prop\", \"name\": \"Test Crat\", \"x\": 0, \"z\": 0 }"), "PackA/typo.json", Path.Combine(dir, "PackA"));
            Fail("a misspelt pack prop loaded");
        }
        catch (MapFileException ex)
        {
            if (!ex.Message.Contains("Test Crate", StringComparison.Ordinal))
                Fail($"misspelt pack prop: the message doesn't suggest \"Test Crate\": {ex.Message}");
        }

        // Fingerprints: a client whose PackA has no Props folder (global cone instead of the pack one) gets a different
        // fingerprint; editing a pack prop or model changes it; reformatting the prop file doesn't.
        var hashA = a.ContentHash;
        var hashB = b.ContentHash;
        if (a.PackAssetHash == 0 || b.PackAssetHash == 0)
            Fail("maps using pack props have no pack-asset fingerprint");
        var packA = Path.Combine(dir, "PackA");
        var aJson = File.ReadAllText(Path.Combine(packA, "a.json"));
        Map ReloadA() => Validated(MapFile.Load(aJson, "PackA/a.json", packA));
        Write("PackC/Props/Crate.json", PackTestProp("Test Crate", 0.6f, "red"));
        Directory.CreateDirectory(Path.Combine(dir, "PackC", "Models"));
        File.Copy(beacon, Path.Combine(dir, "PackC", "Models", "barrel.glb"));
        var withoutCone = Validated(MapFile.Load(aJson, "PackC/a.json", Path.Combine(dir, "PackC"))); // same files minus the shadowing cone (the folder name doesn't count)
        foreach (var f in Directory.GetFiles(packA, "*", SearchOption.AllDirectories)) // PackD: a copy of PackA under another folder name
        {
            var to = Path.Combine(dir, "PackD", Path.GetRelativePath(packA, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        var renamed = Validated(MapFile.Load(aJson, "PackD/a.json", Path.Combine(dir, "PackD"))).ContentHash;
        if (renamed != hashA)
            Fail($"the same pack files in a folder with another name give another fingerprint ({hashA:x8} vs {renamed:x8})");
        var reformatted = PackTestProp("Test Crate", 0.6f, "red").Replace(", ", ",\n      ").Replace("\n", "\r\n");
        File.WriteAllText(Path.Combine(packA, "Props", "Crate.json"), "// reformatted\r\n" + reformatted);
        Props.ReloadPack(packA);
        var hashReformatted = ReloadA().ContentHash;
        File.WriteAllText(Path.Combine(packA, "Props", "Crate.json"), PackTestProp("Test Crate", 0.6f, "orange"));
        Props.ReloadPack(packA);
        var hashRecoloured = ReloadA().ContentHash;
        File.WriteAllText(Path.Combine(packA, "Props", "Crate.json"), PackTestProp("Test Crate", 0.6f, "red"));
        Props.ReloadPack(packA);
        var modelPath = Path.Combine(packA, "Models", "barrel.glb");
        File.Copy(barrel, modelPath, true);
        File.SetLastWriteTimeUtc(modelPath, DateTime.UtcNow.AddMinutes(1)); // the model cache reloads changed files by time stamp
        var hashNewModel = ReloadA().ContentHash;
        if (hashReformatted != hashA)
            Fail($"reformatting a pack prop file (whitespace, comments, CRLF) changed the fingerprint ({hashA:x8} -> {hashReformatted:x8})");
        if (hashRecoloured == hashA)
            Fail("recolouring a pack prop didn't change the map's fingerprint");
        if (hashNewModel == hashA)
            Fail("replacing a pack model didn't change the map's fingerprint");
        if (withoutCone.ContentHash == hashA || withoutCone.PropInstances[1].Def.Pack != null)
            Fail($"a pack without the shadowing prop gives the same fingerprint ({withoutCone.ContentHash:x8})");
        Line($"test packs: PackA/PackB each own a \"Test Crate\" ({Def(a, 0)?.Key}, {Def(b, 0)?.Key}); PackA shadows Traffic Cone and Models/barrel.glb, global: reaches the shipped ones; "
             + $"fingerprints {hashA:x8} / {hashB:x8}: reformatted file {(hashReformatted == hashA ? "same" : "CHANGED")}, recoloured prop {hashRecoloured:x8}, new model {hashNewModel:x8}, missing pack prop {withoutCone.ContentHash:x8}");
    }

    private static void CheckPackSkyImages()
    {
        string Try(string file, string source)
        {
            try
            {
                return Path.GetRelativePath(Maps.Folder, SkyImages.Resolve(file, source)).Replace('\\', '/');
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                return "not found";
            }
        }
        var next = Try("Skies/sample.hdr", "Dev/SkyTest.json");
        var nested = Try("Skies/sample.hdr", "Dev/deeper/Nested.json"); // a map in a subfolder of its pack: the pack folder next
        var forced = Try("global:Skies/sample.hdr", "Dev/SkyTest.json"); // the Maps folder only: there's no Maps/Skies
        var other = Try("Skies/sample.hdr", "Classic/X.json");
        if (next != "Dev/Skies/sample.hdr" || nested != "Dev/Skies/sample.hdr" || forced != "not found" || other != "not found")
            Fail($"sky images: next to the map {next}, nested map {nested}, global: {forced}, other pack {other}");
        else
            Line($"sky images: next to the map {next}, from a nested map {nested} (pack folder), global: skips the pack, other packs can't see it");
    }

    private static void CheckPackAssetCost()
    {
        var dev = Path.Combine(Maps.Folder, "Dev");
        var classic = Path.Combine(Maps.Folder, "Classic");
        Props.ForgetPack(dev);
        Props.ForgetPack(classic);
        var sw = Stopwatch.StartNew();
        var scope = Props.ForPack(dev);
        var cold = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var none = Props.ForPack(classic);
        var noFolder = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (var i = 0; i < 10000; i++)
            _ = Props.ForPack(dev);
        var cachedUs = sw.Elapsed.TotalMilliseconds * 1000 / 10000;
        sw.Restart();
        var map = Validated(MapFile.Load(File.ReadAllText(Path.Combine(dev, "PackAssets.json")), "Dev/PackAssets.json"));
        var loadMs = sw.Elapsed.TotalMilliseconds;
        Line($"cost: Dev pack props {scope.Local.Length} loaded in {cold:0.0} ms (cold), a pack without a Props folder {noFolder:0.00} ms, cached lookup {cachedUs:0.00} us; "
             + $"Pack Assets map loads in {loadMs:0.0} ms; {Props.LoadedPacks.Length} pack(s) with their own props loaded");
        if (!none.IsGlobalScope || scope.IsGlobalScope)
            Fail("Classic should use the global props, Dev its own scope");
        if (cold > 250 || noFolder > 20 || cachedUs > 20)
            Fail($"pack asset lookups too slow: cold {cold:0.0} ms (budget 250), no folder {noFolder:0.00} ms (20), cached {cachedUs:0.00} us (20)");
        if (map.ContentHash != Maps.Find("Pack Assets")?.ContentHash)
            Fail("Pack Assets: a fresh load after reloading the Dev pack's props gives another fingerprint");
    }
}
