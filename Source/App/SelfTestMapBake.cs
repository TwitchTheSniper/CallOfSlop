using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Baked maps (Source/Maps/MapBake.cs): every shipped map, built live and cached, reads back from its file as the very
/// map the live build made, field for field (terrain, props, clutter, buildings, destruction, gravity and
/// planets, meshes and models, sky images, Zombies, objectives...); the load says where each map came from; the
/// release's baked folder comes first; a changed map or input, another build or format, a damaged or cut-short file is
/// never used (the map is built live and cached again); and a map holding something that can't be baked is refused.
/// All in temp folders: never a real cache, and no other section reads a bake.
/// </summary>
public static partial class SelfTest
{
    private static void CheckMapBake()
    {
        var root = Path.Combine(Path.GetTempPath(), $"callofslop-mapbake-{Guid.NewGuid():N}");
        var baked = Path.Combine(root, "Baked");
        var cache = Path.Combine(root, "Cache");
        try
        {
            MapBake.BakedFolderOverride = baked;
            MapBake.CacheFolderOverride = cache;
            MapBake.Enabled = true;
            MapBake.CacheWrites = true;
            lock (MapBake.Unbakeable)
                MapBake.Unbakeable.Clear();
            MapBakeEveryMap(baked, cache);
            MapBakeInputs(root);
            MapBakeUnbakeable();
        }
        finally
        {
            MapBake.FlushWrites();
            MapBake.Enabled = false;
            MapBake.CacheWrites = false;
            MapBake.BakedFolderOverride = null;
            MapBake.CacheFolderOverride = null;
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Every shipped map: built live (and cached), read back from the cache, compared; then the refusals.</summary>
    private static void MapBakeEveryMap(string baked, string cache)
    {
        var errors = new List<string>();
        var t = Stopwatch.GetTimestamp();
        var (live, _) = Maps.LoadFolder(Maps.Folder, errors, true, Maps.BakeUse.WriteOnly);
        var liveMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        MapBake.FlushWrites();
        var liveSummary = MapBake.LastSummary;
        lock (MapBake.Unbakeable)
            foreach (var u in MapBake.Unbakeable)
                Fail($"Map bake: {u}");
        var files = Directory.Exists(cache) ? Directory.GetFiles(cache, "*.map") : [];
        long bytes = files.Sum(f => new FileInfo(f).Length);
        if (files.Length != live.Length)
            Fail($"Map bake: {live.Length} maps built but {files.Length} cached");

        t = Stopwatch.GetTimestamp();
        var (back, _) = Maps.LoadFolder(Maps.Folder, errors, true, Maps.BakeUse.ReadWrite);
        var readMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        var readSummary = MapBake.LastSummary;
        Line($"built live and cached: {liveSummary}");
        Line($"read back: {readSummary}");
        Line($"{files.Length} files, {bytes / 1e6:0.0} MB; built in {liveMs:0} ms (with the cache writes), read in {readMs:0} ms");
        if (!readSummary.Contains($" {live.Length} cached, 0 built", StringComparison.Ordinal))
            Fail($"Map bake: not every map came from the cache ({readSummary})");
        if (back.Length != live.Length)
        {
            Fail($"Map bake: {live.Length} maps live, {back.Length} read back");
            return;
        }
        if (readMs > liveMs * 0.5)
            Fail($"Map bake: reading the bakes took {readMs:0} ms, building the maps {liveMs:0} ms (a bake should load in well under half the time)");

        // Baked = live, field for field, for every map; and the same fingerprint, nav key, id and pack.
        var t2 = Stopwatch.GetTimestamp();
        var same = 0;
        for (var i = 0; i < live.Length; i++)
        {
            var a = live[i];
            var b = back[i];
            var diff = MapBake.Difference(a, b);
            if (a.Name != b.Name || a.Id != b.Id || a.Pack?.Name != b.Pack?.Name)
                diff = $"name, id or pack: {a.Name} #{a.Id} ({a.Pack?.Name}) vs {b.Name} #{b.Id} ({b.Pack?.Name})";
            else if (diff == null && a.ContentHash != b.ContentHash)
                diff = $"fingerprint {a.ContentHash:x8} vs {b.ContentHash:x8}";
            else if (diff == null && !NavBake.KeyOf(a, a.ContentHash).AsSpan().SequenceEqual(NavBake.KeyOf(b, b.ContentHash)))
                diff = "a different nav bake key";
            if (diff != null)
                Fail($"Map bake: {a.Name}: the map read back isn't the one built: {diff}");
            else
                same++;
        }
        Line($"{same} of {live.Length} maps read back exactly as built (compared in {Stopwatch.GetElapsedTime(t2).TotalSeconds:0.0} s)");

        // Every kind of map is in there.
        var kinds = new (string Kind, Func<Map, bool> Has)[]
        {
            ("terrain", m => m.Terrain != null), ("props", m => m.PropInstances.Count > 0), ("clutter", m => m.ClutterStart < m.PropInstances.Count),
            ("buildings", m => m.PlacedBuildings.Count > 0),
            ("destruction", m => m.Destruction != null), ("gravity", m => m.Gravity != null), ("foliage", m => m.FoliageInstances.Length > 0),
            ("meshes", m => m.MeshPlacements.Any(p => p.Mesh != null)), ("models", m => m.MeshPlacements.Any(p => p.Model != null)),
            ("sky image", m => m.Skybox?.Loaded != null), ("boundary", m => m.Boundary != null), ("movers", m => m.Movers.Count > 0),
            ("zombies", m => m.Zombies != null), ("decals", m => m.DecalRequests.Count > 0), ("flags", m => m.FlagPoolIds != null),
        };
        var counts = kinds.Select(k => (k.Kind, N: back.Count(k.Has))).ToArray();
        Line("kinds: " + string.Join(", ", counts.Select(c => $"{c.Kind} {c.N}")));
        foreach (var (kind, n) in counts)
            if (n == 0)
                Fail($"Map bake: no baked map with {kind} (the round trip should cover every kind)");

        var map = live.FirstOrDefault(m => m.Name == "Rooftops") ?? live[0];
        var rel = map.SourceFile!;
        var cacheFile = Path.Combine(cache, MapBake.FileName(rel));
        var good = File.ReadAllBytes(cacheFile);
        var key = MapBake.KeyOf(rel, SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Maps.Folder, rel)))), MapBake.InputsHash(Maps.Folder));

        // The release's baked folder comes before the cache.
        Directory.CreateDirectory(baked);
        File.Copy(cacheFile, Path.Combine(baked, MapBake.FileName(rel)));
        var fromBaked = MapBake.TryLoad(rel, key, map.PackDir, out var loadLog, out _, out var how);
        if (fromBaked == null || how != "baked" || MapBake.Difference(map, fromBaked) is { } d1)
            Fail($"Map bake: {map.Name} with a baked file came from: {how}");
        File.Delete(Path.Combine(baked, MapBake.FileName(rel)));

        // A file that doesn't exactly match is never used.
        byte[] Patched(Action<byte[]> change)
        {
            var copy = (byte[])good.Clone();
            change(copy);
            return copy;
        }
        var buildAt = good.AsSpan().IndexOf(Encoding.UTF8.GetBytes(NavBake.BuildId));
        var otherKey = (byte[])key.Clone();
        otherKey[5] ^= 1;
        var refusals = new (string What, byte[] File, byte[] Key, string Why)[]
        {
            ("the map or an input changed", good, otherKey, "changed"),
            ("another map file", good, key, "another map file"),
            ("another build", Patched(f => f[buildAt + 3] ^= 1), key, "another build"),
            ("another format", Patched(f => f[8] ^= 0x40), key, "another bake format"),
            ("damaged", Patched(f => f[f.Length / 2] ^= 0x10), key, "damaged"),
            ("cut short", good[..(good.Length - 100)], key, "cut short"),
            ("not a bake", Encoding.UTF8.GetBytes("{ \"name\": \"Rooftops\" }"), key, "not a map bake"),
            ("empty", [], key, "not a map bake"),
        };
        foreach (var (what, file, k, why) in refusals)
        {
            var m = MapBake.Read(file, what == "another map file" ? "Legacy/Other.json" : rel, k, map.PackDir, out _, out _, out var reason);
            if (m != null || !reason.Contains(why, StringComparison.Ordinal))
                Fail($"Map bake: a file for {what} was {(m != null ? "used" : $"refused for the wrong reason ({reason})")}");
        }
        Line($"{map.Name}: the baked folder comes first; refused: {string.Join(", ", refusals.Select(r => r.What))}");

        // A damaged cache file: that map is built live and cached again; the others still come from the cache.
        File.WriteAllBytes(cacheFile, Patched(f => f[f.Length - 20] ^= 0x01));
        var (again, _) = Maps.LoadFolder(Maps.Folder, errors, true, Maps.BakeUse.ReadWrite);
        MapBake.FlushWrites();
        var againSummary = MapBake.LastSummary;
        if (!againSummary.Contains($" {live.Length - 1} cached, 1 built", StringComparison.Ordinal) || !MapBake.CacheHas(rel, key))
            Fail($"Map bake: with a damaged cache file: {againSummary}; cached again: {MapBake.CacheHas(rel, key)}");
        else if (MapBake.Difference(map, again.First(m => m.Name == map.Name)) is { } d2)
            Fail($"Map bake: {map.Name} rebuilt after a damaged cache file differs: {d2}");
        Line($"a damaged cache file: {againSummary}");
        if (errors.Count > 0)
            Fail($"Map bake: load errors: {string.Join("; ", errors.Distinct().Take(5))}");
    }

    /// <summary>The inputs key: any file a map can read changes it; another map's file doesn't (it has its own key).</summary>
    private static void MapBakeInputs(string root)
    {
        var maps = Path.Combine(root, "Maps");
        var pack = Path.Combine(maps, "Pack");
        Directory.CreateDirectory(Path.Combine(pack, "Props"));
        File.WriteAllText(Path.Combine(pack, "a.json"), "{ \"name\": \"A\" }");
        File.WriteAllText(Path.Combine(pack, "b.json"), "{ \"name\": \"B\" }");
        File.WriteAllText(Path.Combine(pack, "sky.png"), "not really a png");
        var h0 = MapBake.InputsHash(maps);
        File.WriteAllText(Path.Combine(pack, "b.json"), "{ \"name\": \"B2\" }");
        var hOtherMap = MapBake.InputsHash(maps);
        File.WriteAllText(Path.Combine(pack, "sky.png"), "not really a png!");
        var hImage = MapBake.InputsHash(maps);
        File.WriteAllText(Path.Combine(pack, "Props", "crate.json"), "{}");
        var hProp = MapBake.InputsHash(maps);
        File.WriteAllText(Path.Combine(pack, "pack.json"), "{}");
        var hPack = MapBake.InputsHash(maps);
        var text = SHA256.HashData("{ \"name\": \"A\" }"u8);
        var text2 = SHA256.HashData("{ \"name\": \"A\" } "u8);
        var ok = h0.AsSpan().SequenceEqual(hOtherMap) && !hOtherMap.AsSpan().SequenceEqual(hImage) && !hImage.AsSpan().SequenceEqual(hProp)
                 && !hProp.AsSpan().SequenceEqual(hPack)
                 && !MapBake.KeyOf("Pack/a.json", text, h0).AsSpan().SequenceEqual(MapBake.KeyOf("Pack/a.json", text2, h0))
                 && !MapBake.KeyOf("Pack/a.json", text, h0).AsSpan().SequenceEqual(MapBake.KeyOf("Pack/b.json", text, h0));
        Line($"inputs key: another map's file keeps it, an image, a pack prop or pack.json change it; the map's text and path are in its own key: {ok}");
        if (!ok)
            Fail("Map bake: the inputs key doesn't follow the inputs");
        if (XxHash64.Hash([]) != 0xEF46DB3751D8E999UL || XxHash64.Hash("abc"u8) != 0x44BC2CF5AD770999UL)
            Fail("Map bake: XXH64 doesn't match its reference values");
    }

    /// <summary>A map holding something that can't be baked (here a running task) is refused, not baked wrongly.</summary>
    private static void MapBakeUnbakeable()
    {
        var map = Maps.Fallback()[0];
        map.Validate();
        var field = typeof(Map).GetField("_foliageCoverTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (field == null)
        {
            Fail("Map bake: Map._foliageCoverTask is gone; pick another field to test the refusal with");
            return;
        }
        field.SetValue(map, typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(field.FieldType.GetGenericArguments()[0]).Invoke(null, [null]));
        try
        {
            MapBake.Write(map, "Test.json", new byte[32], "", "");
            Fail("Map bake: a map holding a task was baked");
        }
        catch (MapBakeException ex)
        {
            Line($"a map holding a task is refused: {ex.Message}");
        }
    }
}
