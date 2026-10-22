using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Pack-local decals (Source/Decals/DecalPacks.cs): a map pack's own Decals folder comes before the global one, a pack
/// decal shadows a global one of the same name for that pack's maps only, "global:" forces the global decal, other packs
/// can't see them, names are unique within a scope, pack decals stay out of the fingerprint, and the atlas budget (pack
/// layers are appended and painted alone).
/// </summary>
public static partial class SelfTest
{
    private static void CheckPackDecals()
    {
        // 1. The Dev pack's example: Maps/Dev/Decals/Dev (a blue Exit Sign shadowing the global one, the Dev Pack Badge PNG).
        CheckDevPackDecals();

        // 2. Made-up packs: two packs with the same decal name, an impact decal (global only), a duplicate name, fingerprints.
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-packdecals-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckPackDecalFolders(dir);
        }
        finally
        {
            foreach (var pack in new[] { "DecalPackA", "DecalPackB" })
                Decals.ForgetPack(Path.Combine(dir, pack));
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        // 3. Atlas budget: global library, per pack, total; new pack layers paint alone and match a full repaint.
        CheckPackDecalAtlas();
    }

    private static void CheckDevPackDecals()
    {
        if (Maps.Find("Pack Decals") is not { } map)
        {
            Fail("Maps/Dev/PackDecals.json ('Pack Decals') didn't load");
            return;
        }
        var scope = map.DecalScope;
        var packSign = scope.Find("Exit Sign");
        var globalSign = scope.Find("global:Exit Sign");
        var badge = scope.Find("Dev Pack Badge");
        if (scope.Pack != "Dev" || !scope.Shadowed.Contains("Exit Sign", StringComparer.OrdinalIgnoreCase))
            Fail($"Dev decal scope: pack {scope.Pack ?? "(none)"}, shadows [{string.Join(", ", scope.Shadowed)}] (expected Dev, Exit Sign)");
        if (packSign is not { Pack: "Dev" } || globalSign is not { Pack: null } || ReferenceEquals(packSign, globalSign))
            Fail($"Dev pack: Exit Sign is {packSign?.SourceFile ?? "missing"}, global:Exit Sign is {globalSign?.SourceFile ?? "missing"}");
        if (badge is not { Pack: "Dev", ImagePath: not null })
            Fail($"Dev pack: the Dev Pack Badge (with its PNG) didn't load: {badge?.SourceFile ?? "missing"}");
        if (Decals.Find("Exit Sign") is not { Pack: null } || Decals.Find("Dev Pack Badge") != null || Decals.All.Any(d => d.Pack != null))
            Fail("the global decal library holds a pack decal (or lost its own Exit Sign)");
        // Bake: the map's atlas is the global library then just the pack decals it places (map-scoped layers).
        var mesh = DecalBaker.Bake(map);
        var layers = new HashSet<int>();
        for (var v = 0; v < mesh.VertexCount; v++)
            layers.Add((int)mesh.Vertices[v * DecalMesh.FloatsPerVertex + 8]);
        int LayerOf(DecalDef d)
        {
            if (d.Pack == null)
                return d.Index;
            var k = Array.IndexOf(mesh.PackDefs, d);
            return k < 0 ? -1 : mesh.GlobalLayers + k;
        }
        if (mesh.GlobalLayers != Decals.All.Length || mesh.PackDefs.Length != 2 || packSign == null || badge == null
            || !mesh.PackDefs.Contains(packSign) || !mesh.PackDefs.Contains(badge))
            Fail($"Pack Decals bake: pack layers [{string.Join(", ", mesh.PackDefs.Select(d => d.Name))}] after {mesh.GlobalLayers} global (expected the pack Exit Sign and the badge after {Decals.All.Length})");
        else if (globalSign != null && (!layers.Contains(LayerOf(packSign)) || !layers.Contains(LayerOf(globalSign)) || !layers.Contains(LayerOf(badge))
                 || layers.Any(l => l < 0 || l >= mesh.GlobalLayers + mesh.PackDefs.Length)))
            Fail($"Pack Decals bake: layers [{string.Join(", ", layers.Order())}] miss the pack sign {LayerOf(packSign)}, global sign {LayerOf(globalSign)} or badge {LayerOf(badge)}, or run past the map's atlas");
        if (map.PackDecalsNamed != 2)
            Fail($"Pack Decals names {map.PackDecalsNamed} different pack decals (expected 2)");
        if (mesh.Placed < mesh.Requested - 1)
            Fail($"Pack Decals: only {mesh.Placed}/{mesh.Requested} decals placed");
        // Cosmetic: no pack-asset fingerprint from decals.
        if (map.PackAssetHash != 0)
            Fail($"Pack Decals: pack decals changed the fingerprint ({map.PackAssetHash:x8}); decals are cosmetic");
        // Maps outside the Dev pack can't see its decals, and see the green Exit Sign.
        try
        {
            MapFile.Load(PackTestMap("Decal Leak Test", "{ \"op\": \"Decal\", \"decal\": \"Dev Pack Badge\", \"x\": 0, \"z\": 0 }"), "Classic/DecalLeakTest.json");
            Fail("a Classic map could use the Dev Pack Badge from the Dev pack");
        }
        catch (MapFileException ex)
        {
            if (!ex.Message.Contains("Dev Pack Badge", StringComparison.Ordinal))
                Fail($"unknown decal message doesn't name it: {ex.Message}");
        }
        var classic = MapFile.Load(PackTestMap("Decal Leak Test", "{ \"op\": \"Decal\", \"decal\": \"Exit Sign\", \"x\": 0, \"z\": 0 }"), "Classic/DecalLeakTest.json");
        if (classic.DecalScope.Find("Exit Sign") is not { Pack: null })
            Fail("a Classic map sees the Dev pack's Exit Sign");
        Line($"Dev pack decals: {scope.Local.Length} pack decal(s) ({string.Join(", ", scope.Local.Select(d => $"{d.Name} layer {d.Index}"))}), "
             + $"shadowing [{string.Join(", ", scope.Shadowed)}]; Pack Decals placed {mesh.Placed}/{mesh.Requested}; Classic maps can't see them");
    }

    private static void CheckPackDecalFolders(string dir)
    {
        void Write(string rel, string text)
        {
            var path = Path.Combine(dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        string Sign(string name, string color, string extra = "") => $$"""
            { "format": "call-of-slop-decal/1", "name": "{{name}}", "size": [1.2, 0.5], "rotation": 0, "surfaces": "all"{{extra}},
              "layers": [ { "shape": "rect", "size": [0.9, 0.8], "color": "{{color}}" } ] }
            """;
        // Both packs have a "Test Sign" (different colours); PackA also has an impact decal (global only) and a duplicate name.
        Write("DecalPackA/Decals/Signs/Sign.json", Sign("Test Sign", "red"));
        Write("DecalPackA/Decals/Signs/SignAgain.json", Sign("test sign", "blue"));
        Write("DecalPackA/Decals/Impacts/Hole.json", Sign("Test Hole", "black", ", \"impact\": { \"materials\": [\"Concrete\"] }").Replace("[1.2, 0.5]", "0.2"));
        Write("DecalPackB/Decals/Sign.json", Sign("Test Sign", "green"));
        var op = "{ \"op\": \"Decal\", \"decal\": \"Test Sign\", \"x\": 0, \"z\": 0 }";
        Write("DecalPackA/a.json", PackTestMap("Decal Pack A Map", op));
        Write("DecalPackB/b.json", PackTestMap("Decal Pack B Map", op));
        var errorsBefore = Decals.LoadErrors.Count;
        var errors = new List<string>();
        var (all, _) = Maps.LoadFolder(dir, errors);
        var a = all.FirstOrDefault(m => m.Name == "Decal Pack A Map");
        var b = all.FirstOrDefault(m => m.Name == "Decal Pack B Map");
        if (errors.Count > 0 || a == null || b == null || all.Length != 2)
        {
            Fail($"decal test packs: {all.Length} maps (expected 2: decal files in Decals aren't maps), errors: {string.Join(" | ", errors)}");
            return;
        }
        var sa = a.DecalScope;
        var sb = b.DecalScope;
        var da = sa.Find("Test Sign");
        var db = sb.Find("Test Sign");
        if (da is not { Pack: "DecalPackA" } || db is not { Pack: "DecalPackB" } || ReferenceEquals(da, db) || da.Layers[0].Color == db.Layers[0].Color)
            Fail($"two packs with a \"Test Sign\" clash: {da?.SourceFile} vs {db?.SourceFile}");
        if (sa.Local.Length != 1)
            Fail($"DecalPackA: {sa.Local.Length} decals loaded (expected 1: the duplicate name and the impact decal are skipped)");
        var newErrors = Decals.LoadErrors.Skip(errorsBefore).ToList();
        Decals.LoadErrors.RemoveRange(errorsBefore, newErrors.Count); // expected ones: don't leave them in the library's list
        if (!newErrors.Any(e => e.Contains("already called", StringComparison.Ordinal)) || !newErrors.Any(e => e.Contains("impact decals are global", StringComparison.Ordinal)))
            Fail($"DecalPackA's duplicate name / impact decal weren't reported: {string.Join(" | ", newErrors)}");
        if (sa.Find("Test Hole") != null || Decals.ImpactsFor("Concrete").Any(d => d.Pack != null))
            Fail("a pack impact decal got in");
        // Cosmetic: the same map in two packs with different decal files has the same fingerprint.
        var a2 = Validated(MapFile.Load(File.ReadAllText(Path.Combine(dir, "DecalPackA", "a.json")), "DecalPackB/a.json", Path.Combine(dir, "DecalPackB")));
        var a1 = Validated(MapFile.Load(File.ReadAllText(Path.Combine(dir, "DecalPackA", "a.json")), "DecalPackA/a.json", Path.Combine(dir, "DecalPackA")));
        if (a1.ContentHash != a2.ContentHash || a1.PackAssetHash != 0)
            Fail($"pack decals change the map fingerprint: {a1.ContentHash:x8} vs {a2.ContentHash:x8} (pack assets {a1.PackAssetHash:x8})");
        // Typos suggest pack names.
        try
        {
            MapFile.Load(PackTestMap("Typo", "{ \"op\": \"Decal\", \"decal\": \"Test Sigm\", \"x\": 0, \"z\": 0 }"), "DecalPackA/typo.json", Path.Combine(dir, "DecalPackA"));
            Fail("a misspelt pack decal loaded");
        }
        catch (MapFileException ex)
        {
            if (!ex.Message.Contains("Test Sign", StringComparison.Ordinal))
                Fail($"misspelt pack decal: the message doesn't suggest \"Test Sign\": {ex.Message}");
        }
        Line("made-up decal packs: each \"Test Sign\" its own, duplicate and impact decals skipped and reported, same fingerprint either way");
    }

    private static void CheckPackDecalAtlas()
    {
        var global = Decals.All;
        if (global.Length > Decals.MaxGlobal * 3 / 4)
            Fail($"{global.Length} global decals: the library holds {Decals.MaxGlobal}; leave a quarter free or raise MaxGlobal");
        if (Decals.MaxGlobal + Decals.MaxPackDecalsPerMap > DecalAtlas.MaxLayers || DecalAtlas.MaxLayers > 2048)
            Fail($"a map's atlas could need {Decals.MaxGlobal + Decals.MaxPackDecalsPerMap} layers; OpenGL 4.1 guarantees 2048 ({DecalAtlas.MaxLayers} allowed)");
        foreach (var p in Decals.LoadedPacks)
            if (p.Local.Length > Decals.MaxPerPack)
                Fail($"pack {p.Pack}: {p.Local.Length} decals, more than {Decals.MaxPerPack}");
        // Every shipped map's atlas: global layers plus the pack decals it places.
        var worst = (Name: "", Layers: 0);
        foreach (var m in Maps.All)
            if (m.PackDecalsNamed > Decals.MaxPackDecalsPerMap)
                Fail($"{m.Name}: {m.PackDecalsNamed} different pack decals (at most {Decals.MaxPackDecalsPerMap})");
            else if (global.Length + m.PackDecalsNamed > worst.Layers)
                worst = (m.Name, global.Length + m.PackDecalsNamed);
        // The renderer's path: global layers copied from the last build, only the map's pack decals painted.
        var dev = Maps.Find("Pack Decals");
        var pack = dev != null ? DecalBaker.Bake(dev).PackDefs : Array.Empty<DecalDef>();
        var sw = Stopwatch.StartNew();
        var first = DecalAtlas.Build(global);
        var globalMs = sw.Elapsed.TotalMilliseconds;
        var defs = global.Concat(pack).ToArray();
        sw.Restart();
        var grown = DecalAtlas.Build(defs, null, global, first, out var painted);
        var packMs = sw.Elapsed.TotalMilliseconds;
        if (painted != pack.Length || !grown.AsSpan().SequenceEqual(DecalAtlas.Build(defs)))
            Fail($"the map atlas painted {painted} layers (expected {pack.Length}) or differs from a full repaint");
        var mb = worst.Layers * DecalAtlas.BytesPerLayer * 4 / 3 / (1024.0 * 1024.0);
        Line($"decal atlas: {global.Length}/{Decals.MaxGlobal} global layers ({global.Length * DecalAtlas.BytesPerLayer / 1024} KB CPU copy) painted in {globalMs:0.0} ms; "
             + $"Pack Decals adds {pack.Length} pack layer(s) in {packMs:0.0} ms; biggest map atlas {worst.Layers} layers ({worst.Name}, {mb:0.0} MB with mipmaps); "
             + $"limits {Decals.MaxPerPack} per pack, {Decals.MaxPackDecalsPerMap} per map, {DecalAtlas.MaxLayers} layers");

        // Parse speed: a synthetic pack of 2000 decal files (temp dir) must load fast; only placed ones are painted.
        var dir = Path.Combine(Path.GetTempPath(), "callofslop-bigdecalpack-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckBigDecalPack(dir, 2000);
        }
        finally
        {
            Decals.ForgetPack(Path.Combine(dir, "BigPack"));
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void CheckBigDecalPack(string dir, int count)
    {
        var folder = Path.Combine(dir, "BigPack", Decals.PackFolderName);
        for (var i = 0; i < count; i++)
        {
            var sub = Path.Combine(folder, $"Brand{i / 100:00}");
            Directory.CreateDirectory(sub);
            var r = (i * 37 % 255) / 255f;
            File.WriteAllText(Path.Combine(sub, $"Sign{i:0000}.json"), $$"""
                { "format": "call-of-slop-decal/1", "name": "Big Sign {{i}}", "size": [1.6, 0.6], "rotation": 0, "surfaces": "all",
                  "layers": [ { "shape": "rect", "size": [0.94, 0.8], "color": [{{r.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, 0.3, 0.5] },
                              { "shape": "circle", "at": [0.25, 0.5], "radius": 0.18, "color": "white" },
                              { "shape": "line", "at": [0.5, 0.5], "to": [0.9, 0.5], "width": 0.08, "color": "white" } ] }
                """);
        }
        var sw = Stopwatch.StartNew();
        var scope = Decals.ForPack(Path.Combine(dir, "BigPack"));
        var parseMs = sw.Elapsed.TotalMilliseconds;
        if (scope.Local.Length != count || scope.Find("Big Sign 1999") is not { Pack: "BigPack" })
            Fail($"big decal pack: {scope.Local.Length}/{count} decals loaded");
        // Lookups by name are a dictionary (a map with many ops names decals often).
        sw.Restart();
        var found = 0;
        for (var i = 0; i < 20000; i++)
            if (scope.Find($"Big Sign {i % count}") != null)
                found++;
        var findMs = sw.Elapsed.TotalMilliseconds;
        // A map placing 40 of them gets 40 pack layers, not 2000.
        var ops = Enumerable.Range(0, 40).Select(i => $"{{ \"op\": \"Decal\", \"decal\": \"Big Sign {i * 50}\", \"x\": {-14 + i % 8 * 4}, \"z\": {-14 + i / 8 * 6} }}");
        var map = Validated(MapFile.Load(PackTestMap("Big Pack Map", ops.ToArray()), "BigPack/big.json", Path.Combine(dir, "BigPack")));
        var mesh = DecalBaker.Bake(map);
        if (map.PackDecalsNamed != 40 || mesh.PackDefs.Length > 40 || mesh.PackDefs.Length < 30)
            Fail($"big decal pack map: names {map.PackDecalsNamed} pack decals, atlas gets {mesh.PackDefs.Length} pack layers (expected 40 and 30-40)");
        // Too many different pack decals on one map is refused at load.
        try
        {
            var names = string.Join(", ", Enumerable.Range(0, Decals.MaxPackDecalsPerMap + 1).Select(i => $"\"Big Sign {i}\""));
            MapFile.Load(PackTestMap("Too Many", $"{{ \"op\": \"DecalScatter\", \"decals\": [{names}], \"x0\": -5, \"z0\": -5, \"x1\": 5, \"z1\": 5, \"count\": 10 }}"),
                "BigPack/many.json", Path.Combine(dir, "BigPack"));
            Fail($"a map naming {Decals.MaxPackDecalsPerMap + 1} different pack decals loaded");
        }
        catch (MapFileException)
        {
        }
        Line($"big decal pack: {count} decal files parsed in {parseMs:0} ms ({parseMs * 1000 / count:0} us each), 20000 lookups in {findMs:0.0} ms; "
             + $"a map placing {map.PackDecalsNamed} of them gets {mesh.PackDefs.Length} pack atlas layers ({mesh.Placed}/{mesh.Requested} placed)");
        if (parseMs > 3000)
            Fail($"parsing {count} decal files took {parseMs:0} ms (budget 3000 ms)");
        if (found != 20000 || findMs > 200)
            Fail($"pack decal lookups: {found}/20000 found in {findMs:0} ms");
    }
}
