using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Baked maps (docs/perf/STREAMING.md, section 7): a map as <see cref="Maps.LoadFolder(string, List{string}, bool)"/> builds
/// and validates it (every box after props, clutter and terrain, colours, spawns, waypoints, props, buildings, decals,
/// foliage, the Zombies and objective data, the recorded ops...), written to a file and read back instead of being built
/// again. Like baked nav graphs (BotNavBake.cs), two places are looked in, in order:
/// <list type="number">
/// <item>Baked/Maps next to the game: written by the release workflow (<c>CallOfSlop --bake</c>) for every shipped map.</item>
/// <item>Cache/Maps in the save folder: written in the background after the game builds a map itself (dev builds, custom
/// maps, a changed map). Not a save file: never migrated, safe to delete, left out of the package.</item>
/// </list>
/// A file is used only if it exactly matches: the bake format, this exact build of the game (module id) with its runtime
/// and OS, the map file's path and a SHA-256 of its text, and a SHA-256 of every other file a map can read while it is
/// built (the Props, Buildings, Decals, Materials and Models folders and every file in the Maps folder that isn't
/// another map: pack props and decals, models, sky images, pack.json). Anything else, a damaged or cut-short file
/// included, is ignored and the map is built live (and cached). The map read back is the live one, field for field
/// ("Map bake" self-test, every shipped map). The live build stays for "Refresh maps", --check-map, the generators,
/// destruction restores and the self-test.
/// </summary>
public static class MapBake
{
    /// <summary>Bump when the file layout changes (old files are then ignored).</summary>
    public const int FormatVersion = 2; // 2: the light grid keeps a flicker phasor and pace per cell (LightFlicker)

    private static readonly byte[] Magic = "CoSMap\r\n"u8.ToArray();

    /// <summary>Look for baked and cached maps when the game starts (off in the self-test, except in the "Map bake" section).</summary>
    public static bool Enabled = true;

    /// <summary>Cache the maps the game builds itself (off in the self-test, except in the "Map bake" section).</summary>
    public static bool CacheWrites = true;

    /// <summary><c>--bake</c>: every map built is also written here (null: not baking).</summary>
    public static string? BakeOutFolder;

    /// <summary>The release's baked maps (null: next to the game).</summary>
    public static string? BakedFolderOverride;

    /// <summary>The cache (null: Cache/Maps in the save folder).</summary>
    public static string? CacheFolderOverride;

    public static string BakedFolder => BakedFolderOverride ?? Path.Combine(AppFolders.Data, "Baked", "Maps");

    public static string CacheFolder => CacheFolderOverride ?? Path.Combine(SavePaths.Folder, "Cache", "Maps");

    /// <summary>Maps read from a bake or the cache, built live, and written to the cache, since start-up.</summary>
    public static int Baked, Cached, Built, Written;

    /// <summary>What the last load found, one line (for the log and the self-test).</summary>
    public static string LastSummary { get; private set; } = "";

    /// <summary>Maps the last load (with bakes) couldn't bake, and why (normally empty).</summary>
    public static readonly List<string> Unbakeable = new();

    // ---- Keys -------------------------------------------------------------------------------------

    /// <summary>The library folders next to the game that maps read while they are built.</summary>
    private static readonly string[] LibraryFolders = ["Props", "Buildings", "Decals", "Materials", "Models"];

    /// <summary>
    /// A SHA-256 over every file a map can read while it is built except the map files themselves: the library folders
    /// next to the game and every file in <paramref name="mapsFolder"/> that isn't a map (each file's path, length and
    /// content). One per load: a change to any of them makes every map's key new.
    /// </summary>
    public static byte[] InputsHash(string mapsFolder)
    {
        var files = new List<(string Rel, string Path)>();
        foreach (var lib in LibraryFolders)
        {
            var dir = Path.Combine(AppFolders.Data, lib);
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    files.Add((lib + "/" + Path.GetRelativePath(dir, f).Replace('\\', '/'), f));
        }
        if (Directory.Exists(mapsFolder))
            foreach (var f in Directory.GetFiles(mapsFolder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(mapsFolder, f).Replace('\\', '/');
                if (rel.StartsWith('.') || rel.Contains("/.", StringComparison.Ordinal) || Maps.IsMapFile(mapsFolder, f))
                    continue; // (hidden folders and map files: each map's own text is in its own key)
                files.Add(("Maps/" + rel, f));
            }
        files.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));
        var digests = new byte[files.Count][];
        var lengths = new long[files.Count];
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, i =>
        {
            try
            {
                var bytes = File.ReadAllBytes(files[i].Path);
                lengths[i] = bytes.Length;
                digests[i] = SHA256.HashData(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lengths[i] = -1;
                digests[i] = SHA256.HashData(Encoding.UTF8.GetBytes(ex.GetType().Name)); // (an unreadable file: a key nothing matches by accident)
            }
        });
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < files.Count; i++)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(files[i].Rel + "\n" + lengths[i] + "\n"));
            sha.AppendData(digests[i]);
        }
        return sha.GetHashAndReset();
    }

    /// <summary>The key of a map file: format, build, its path in the Maps folder, its text and the inputs (<see cref="InputsHash"/>).</summary>
    public static byte[] KeyOf(string rel, byte[] textHash, byte[] inputs)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Encoding.UTF8.GetBytes($"map {FormatVersion}|{NavBake.BuildId}|{rel.Replace('\\', '/')}|"));
        sha.AppendData(textHash);
        sha.AppendData(inputs);
        return sha.GetHashAndReset();
    }

    /// <summary>The file a map is baked or cached in (one per map file; a newer bake replaces the old file).</summary>
    public static string FileName(string rel)
    {
        rel = rel.Replace('\\', '/');
        var safe = new StringBuilder();
        foreach (var ch in rel)
            safe.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        var h = 2166136261u;
        foreach (var ch in rel.ToLowerInvariant())
            h = (h ^ ch) * 16777619u;
        return $"{safe}.{h:x8}.map";
    }

    // ---- Library roots ------------------------------------------------------------------------------

    /// <summary>
    /// The library objects a map built in pack folder <paramref name="packDir"/> may hold, by key: the global and pack
    /// props, decals, buildings, flag designs, the destruction table, and every flag variant and model loaded so far.
    /// (Nothing else is shared between maps today; an object shared some other way would be baked as each map's own copy.)
    /// </summary>
    internal static MapGraph.MapLibraryIndex IndexFor(string? packDir)
    {
        // Cached while the libraries stay the same objects and no flag variant or model is added.
        var stamp = (Props.All, Decals.All, Buildings.All, MaterialDestruction.Current, FlagPool.Designs, FlagPool.BuiltVariants().Length, MeshLibrary.LoadedModels().Length);
        var key = packDir ?? "";
        lock (Indexes)
            if (Indexes.TryGetValue(key, out var hit) && hit.Stamp.Equals(stamp))
                return hit.Index;
        var index = BuildIndex(packDir);
        lock (Indexes)
            Indexes[key] = (stamp, index);
        return index;
    }

    private static readonly Dictionary<string, (object Stamp, MapGraph.MapLibraryIndex Index)> Indexes = new(StringComparer.OrdinalIgnoreCase);

    private static MapGraph.MapLibraryIndex BuildIndex(string? packDir)
    {
        var roots = new List<(string, object)>();
        var props = Props.All;
        for (var i = 0; i < props.Length; i++)
            roots.Add(($"P/{i}", props[i]));
        var pscope = Props.ForPack(packDir);
        if (!pscope.IsGlobalScope)
            for (var i = 0; i < pscope.Local.Length; i++)
                roots.Add(($"PL/{i}", pscope.Local[i]));
        var decals = Decals.All;
        for (var i = 0; i < decals.Length; i++)
            roots.Add(($"D/{i}", decals[i]));
        var dscope = Decals.ForPack(packDir);
        if (!dscope.IsGlobalScope)
            for (var i = 0; i < dscope.Local.Length; i++)
                roots.Add(($"DL/{i}", dscope.Local[i]));
        var buildings = Buildings.All;
        for (var i = 0; i < buildings.Length; i++)
            roots.Add(($"B/{i}", buildings[i]));
        roots.Add(("MD", MaterialDestruction.Current));
        var designs = FlagPool.Designs;
        for (var i = 0; i < designs.Length; i++)
            roots.Add(($"F/{i}", designs[i]));
        var index = new MapGraph.MapLibraryIndex(roots, deep: true);
        // Flag variants and models are built on demand while maps load; they are found again by what they were built from.
        var more = new List<(string, object)>();
        foreach (var (b, id, v) in FlagPool.BuiltVariants())
            if (index.Find(b) is { } at)
                more.Add(($"FV/{at.Root}|{string.Join(",", at.Path)}|{id}", v));
        foreach (var (path, model) in MeshLibrary.LoadedModels())
            more.Add(($"MM/{RelToGame(path)}|{model.File}", model));
        index.Add(more, deep: true);
        return index;
    }

    private static string RelToGame(string path)
    {
        var rel = Path.GetRelativePath(AppFolders.Data, path);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? path : rel.Replace('\\', '/');
    }

    /// <summary>A library root by key (reader side), or null if the library has no such object.</summary>
    private static object? Root(string key, string? packDir, MapGraph.MapLibraryResolver self)
    {
        static int N(string s) => int.TryParse(s, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1;
        static T? At<T>(T[] a, int i) where T : class => (uint)i < (uint)a.Length ? a[i] : null;
        var slash = key.IndexOf('/');
        var kind = slash < 0 ? key : key[..slash];
        var rest = slash < 0 ? "" : key[(slash + 1)..];
        switch (kind)
        {
            case "P":
                return At(Props.All, N(rest));
            case "PL":
                return At(Props.ForPack(packDir).Local, N(rest));
            case "D":
                return At(Decals.All, N(rest));
            case "DL":
                return At(Decals.ForPack(packDir).Local, N(rest));
            case "B":
                return At(Buildings.All, N(rest));
            case "MD":
                return MaterialDestruction.Current;
            case "F":
                return At(FlagPool.Designs, N(rest));
            case "FV":
            {
                var parts = rest.Split('|');
                if (parts.Length != 3)
                    return null;
                var path = parts[1].Length == 0 ? [] : parts[1].Split(',').Select(N).ToArray();
                return self.Resolve(parts[0], path) is PropDef b ? FlagPool.Variant(b, parts[2]) : null;
            }
            case "MM":
            {
                var bar = rest.LastIndexOf('|');
                if (bar < 0)
                    return null;
                var file = rest[..bar];
                return MeshLibrary.Model(Path.IsPathRooted(file) ? file : Path.Combine(AppFolders.Data, file), rest[(bar + 1)..]);
            }
            default:
                return null;
        }
    }

    // ---- Writing and reading -------------------------------------------------------------------------

    /// <summary>
    /// The bake file of <paramref name="map"/> (just built and validated): header (format, build, map file, key), then a
    /// payload (the console lines its build and validation wrote, the map) and a SHA-256 of the payload. Throws
    /// <see cref="MapBakeException"/> if the map holds something that can't be baked.
    /// </summary>
    internal static byte[] Write(Map map, string rel, byte[] key, string loadLog, string validateLog, MapGraph.MapLibraryIndex? index = null, Dictionary<string, long>? sizes = null)
    {
        index ??= IndexFor(map.PackDir);
        var writer = new MapGraph.Writer(index, map);
        var graph = writer.Run();
        if (sizes != null)
            foreach (var (k, v) in writer.Sizes)
                sizes[k] = sizes.GetValueOrDefault(k) + v;
        using var payload = new MemoryStream(graph.Length + loadLog.Length * 2 + 64);
        using (var w = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(loadLog);
            w.Write(validateLog);
            w.Write(graph.Length);
            w.Write(graph);
        }
        var body = payload.ToArray();
        using var ms = new MemoryStream(body.Length + 256);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(NavBake.BuildId);
            w.Write(rel.Replace('\\', '/'));
            w.Write(key);
            w.Write(body.Length);
            w.Write(body);
            w.Write(XxHash64.Hash(body)); // (whole and undamaged: keys are SHA-256, this check only needs to be fast)
        }
        return ms.ToArray();
    }

    /// <summary>
    /// The map in <paramref name="file"/> if it was baked for exactly this map file (<paramref name="key"/> from
    /// <see cref="KeyOf"/>), loaded from pack folder <paramref name="packDir"/>; else null with the reason in
    /// <paramref name="why"/>. Any thread.
    /// </summary>
    public static Map? Read(byte[] file, string rel, byte[] key, string? packDir, out string loadLog, out string validateLog, out string why)
    {
        loadLog = validateLog = "";
        try
        {
            using var r = new BinaryReader(new MemoryStream(file, writable: false), Encoding.UTF8);
            if (!r.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                return Fail("not a map bake", out why);
            if (r.ReadInt32() != FormatVersion)
                return Fail("another bake format", out why);
            if (r.ReadString() != NavBake.BuildId)
                return Fail("baked by another build", out why);
            if (r.ReadString() != rel.Replace('\\', '/'))
                return Fail("another map file", out why);
            if (!r.ReadBytes(32).AsSpan().SequenceEqual(key))
                return Fail("the map or what it uses changed", out why);
            var n = r.ReadInt32();
            var start = (int)r.BaseStream.Position;
            if (n < 0 || n > file.Length - start - 8 || start + n + 8 != file.Length)
                return Fail("damaged or cut short", out why);
            if (BitConverter.ToUInt64(file, start + n) != XxHash64.Hash(file.AsSpan(start, n)))
                return Fail("damaged", out why);
            loadLog = r.ReadString();
            validateLog = r.ReadString();
            var g = r.ReadInt32();
            var at = (int)r.BaseStream.Position;
            if (g < 0 || at + g != start + n)
                return Fail("damaged", out why);
            MapGraph.MapLibraryResolver? resolver = null;
            resolver = new MapGraph.MapLibraryResolver(k => Root(k, packDir, resolver!));
            var map = new MapGraph.Reader(file, at, g, resolver).Run();
            map.PackDir = packDir;
            why = "ok";
            return map;
        }
        catch (Exception ex) // whatever is wrong with the file, the map is built live instead
        {
            loadLog = validateLog = "";
            return Fail($"unreadable ({ex.GetType().Name}: {ex.Message})", out why);
        }

        static Map? Fail(string reason, out string why)
        {
            why = reason;
            return null;
        }
    }

    /// <summary>A baked map for the file, then a cached one. Null: build it live (<paramref name="result"/> says why).</summary>
    public static Map? TryLoad(string rel, byte[] key, string? packDir, out string loadLog, out string validateLog, out string result)
    {
        var name = FileName(rel);
        var notes = new List<string>();
        foreach (var (folder, what) in new[] { (BakedFolder, "baked"), (CacheFolder, "cached") })
        {
            var path = Path.Combine(folder, name);
            byte[] bytes;
            try
            {
                if (!File.Exists(path))
                    continue;
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add($"{what}: {ex.Message}");
                continue;
            }
            if (Read(bytes, rel, key, packDir, out loadLog, out validateLog, out var why) is { } map)
            {
                result = what;
                return map;
            }
            notes.Add($"{what} file ignored: {why}");
        }
        loadLog = validateLog = "";
        result = notes.Count == 0 ? "none" : string.Join("; ", notes);
        return null;
    }

    /// <summary>Does the cache hold this map file's bake for exactly this key (the header only is read)?</summary>
    public static bool CacheHas(string rel, byte[] key)
    {
        try
        {
            var path = Path.Combine(CacheFolder, FileName(rel));
            if (!File.Exists(path))
                return false;
            using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            return r.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic) && r.ReadInt32() == FormatVersion && r.ReadString() == NavBake.BuildId
                   && r.ReadString() == rel.Replace('\\', '/') && r.ReadBytes(32).AsSpan().SequenceEqual(key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or FormatException)
        {
            return false;
        }
    }

    private static readonly List<Task> Pending = new();

    /// <summary>Writes a map to the cache in the background (atomically: a temp file, then a rename). Errors are logged, never thrown.</summary>
    public static void WriteCache(string rel, byte[] file)
    {
        var dir = CacheFolder;
        var task = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, FileName(rel));
                var tmp = path + "." + Environment.ProcessId + ".tmp";
                File.WriteAllBytes(tmp, file);
                File.Move(tmp, path, overwrite: true);
                Interlocked.Increment(ref Written);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[maps] couldn't cache {rel}: {ex.Message}");
            }
        });
        lock (Pending)
        {
            Pending.RemoveAll(t => t.IsCompleted);
            Pending.Add(task);
        }
    }

    /// <summary>Waits for the cache writes still running (the self-test; nothing else needs to).</summary>
    public static void FlushWrites()
    {
        Task[] all;
        lock (Pending)
            all = Pending.ToArray();
        Task.WaitAll(all);
    }

    internal static void NoteSummary(string s) => LastSummary = s;

    /// <summary>
    /// <c>--bake</c> (the release workflow), after <see cref="Maps.All"/> was built live with <see cref="BakeOutFolder"/>
    /// set: every map's bake must exist, read back with this build and equal the live map field for field. A bad file is
    /// deleted (that map then builds live for players). Returns the number of maps that failed.
    /// </summary>
    public static int CheckBakes(string folder)
    {
        var inputs = InputsHash(Maps.Folder);
        var failed = 0;
        long bytes = 0;
        foreach (var m in Maps.All)
        {
            var rel = m.SourceFile;
            if (rel == null)
                continue;
            var path = Path.Combine(folder, FileName(rel));
            string? problem;
            try
            {
                var file = File.ReadAllBytes(path);
                bytes += file.Length;
                var key = KeyOf(rel, SHA256.HashData(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(Maps.Folder, rel)))), inputs);
                var t = System.Diagnostics.Stopwatch.GetTimestamp();
                var back = Read(file, rel, key, m.PackDir, out _, out _, out var why);
                var ms = System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds;
                problem = back == null ? $"doesn't read back ({why})" : Difference(m, back);
                if (problem == null)
                    Console.WriteLine($"[bake] map {m.Name}: {file.Length / 1024} KB, read in {ms:0.0} ms");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problem = ex.Message;
            }
            if (problem == null)
                continue;
            failed++;
            Console.WriteLine($"[bake] map {m.Name}: FAILED: {problem}");
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        lock (Unbakeable)
            foreach (var u in Unbakeable)
                Console.WriteLine($"[bake] map {u}: FAILED: can't be baked");
        Console.WriteLine($"[bake] {Maps.All.Length - failed} of {Maps.All.Length} maps baked to {folder} ({bytes / 1024} KB); {failed} failed");
        return failed;
    }

    /// <summary>Compares a baked map with the live one (null: the same, field for field).</summary>
    public static string? Difference(Map live, Map baked)
    {
        var c = new MapGraph.GraphComparer(IndexFor(live.PackDir));
        c.Run(live, baked);
        if (!string.Equals(live.PackDir, baked.PackDir, StringComparison.Ordinal))
            c.Diffs.Add($"PackDir: {live.PackDir} != {baked.PackDir}");
        return c.Diffs.Count == 0 ? null : string.Join("; ", c.Diffs);
    }
}
