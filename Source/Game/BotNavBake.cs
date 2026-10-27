using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Baked nav graphs (docs/perf/STREAMING.md, "Baking"): a finished graph written to a file and read back instead of being
/// built again. Two places are looked in, in order:
/// <list type="number">
/// <item>Baked/Nav next to the game: written by the release workflow (<c>CallOfSlop --bake</c>) for every shipped map.</item>
/// <item>Cache/Nav in the save folder: written by the game after it builds a graph itself (dev builds, custom maps, a map
/// the release didn't bake). Not a save file: never migrated, safe to delete, left out of the package.</item>
/// </list>
/// A file is used only if it exactly matches: the same bake format, the same build of the game (its module id, so a
/// change to the nav code never meets an old file), the same runtime and OS, the map's name and fingerprint, and a SHA-256
/// of everything the graph is built from (the map file's text, every box as built with its kind, the spawns, waypoints,
/// building nav points and links, the barrier, the solid meshes). Anything else, a damaged or cut-short file included,
/// is ignored and the graph is built live as before (and cached). The graph read back is the one the live build makes,
/// bit for bit ("Nav bake" self-test; the "Bots" section round-trips every map's graph).
/// </summary>
public static class NavBake
{
    /// <summary>Bump when the file layout changes (old files are then ignored).</summary>
    public const int FormatVersion = 1;

    private static readonly byte[] Magic = "CoSNav\r\n"u8.ToArray();

    /// <summary>Look for baked and cached graphs, and cache what is built (off in the self-test except where a section turns it on).</summary>
    public static bool Enabled = true;

    /// <summary>The release's baked graphs (null: next to the game).</summary>
    public static string? BakedFolderOverride;

    /// <summary>The cache (null: Cache/Nav in the save folder).</summary>
    public static string? CacheFolderOverride;

    public static string BakedFolder => BakedFolderOverride ?? Path.Combine(AppFolders.Data, "Baked", "Nav");

    public static string CacheFolder => CacheFolderOverride ?? Path.Combine(SavePaths.Folder, "Cache", "Nav");

    /// <summary>What the last <see cref="TryLoad"/> found ("baked", "cached", or why nothing was used), for the log and the self-test.</summary>
    public static string LastResult { get; private set; } = "";

    /// <summary>Graphs read from a bake or the cache, and graphs written to the cache, since start-up.</summary>
    public static int Loaded, Written;

    /// <summary>The build of the game, its runtime and OS: a bake from anything else is ignored.</summary>
    public static readonly string BuildId =
        $"{typeof(NavGraph).Assembly.ManifestModule.ModuleVersionId:N}|{RuntimeInformation.FrameworkDescription}|{RuntimeInformation.ProcessArchitecture}|"
        + (OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux");

    /// <summary>The file a map's graph is baked or cached in (one per map name; a newer graph replaces the old file).</summary>
    public static string FileName(string mapName)
    {
        var safe = new StringBuilder();
        foreach (var ch in mapName)
            safe.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        var h = 2166136261u;
        foreach (var ch in mapName.ToLowerInvariant())
            h = (h ^ ch) * 16777619u;
        return $"{safe}.{h:x8}.nav";
    }

    /// <summary>
    /// The one input of the key that changes during a match: the map's boxes as they are now (a copy, a plain memory copy).
    /// Game thread. Everything else <see cref="KeyOf(Map, uint, Box[])"/> reads is fixed once the map is built, so the
    /// hashing itself runs on a worker (BotNav.cs, PumpGraph) and no tick pays for it: it takes up to tens of ms on a big map.
    /// </summary>
    public static Box[] TakeBoxes(Map map) => map.Solid.ToArray();

    /// <summary>The key of <paramref name="map"/> now (<see cref="TakeBoxes"/> and <see cref="KeyOf(Map, uint, Box[])"/> in one go). Game thread.</summary>
    public static byte[] KeyOf(Map map, uint contentHash) => KeyOf(map, contentHash, TakeBoxes(map));

    /// <summary>
    /// The SHA-256 of everything <paramref name="map"/>'s nav graph is built from, as built (broken props whole, doors as they
    /// start). Any thread: <paramref name="boxesNow"/> is <see cref="TakeBoxes"/>'s copy (made as built here, in place), and
    /// the rest (spawns, links, box kinds, meshes, movers...) is only ever written while the map is being built.
    /// </summary>
    public static byte[] KeyOf(Map map, uint contentHash, Box[] boxesNow)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[4];
        void U(uint v)
        {
            BitConverter.TryWriteBytes(buf, v);
            sha.AppendData(buf);
        }
        void F(float f) => U(BitConverter.SingleToUInt32Bits(f));
        void V(Vector3 v)
        {
            F(v.X);
            F(v.Y);
            F(v.Z);
        }
        sha.AppendData(Encoding.UTF8.GetBytes($"nav {FormatVersion}|{BuildId}|{map.Name}|"));
        sha.AppendData(map.SourceTextHash);
        U(contentHash);
        F(map.HalfX);
        F(map.HalfZ);
        var boxes = map.AsBuiltSolid(boxesNow);
        U((uint)boxes.Length);
        sha.AppendData(MemoryMarshal.AsBytes(boxes.AsSpan()));
        var kinds = new byte[boxes.Length];
        for (var i = 0; i < boxes.Length; i++)
            kinds[i] = (byte)((map.IsPropCollider(i) ? 1 : 0) | (map.IsClipBox(i) ? 2 : 0) | (map.IsBuildingDoorBox(i) ? 4 : 0));
        sha.AppendData(kinds);
        foreach (var list in new[] { map.Spawns, map.Waypoints })
        {
            U((uint)list.Count);
            foreach (var p in list)
                V(p);
        }
        U((uint)map.NavLinks.Count);
        foreach (var (a, b) in map.NavLinks)
        {
            V(a);
            V(b);
        }
        var navPoints = map.BuildingNavPoints.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Z).ToArray();
        U((uint)navPoints.Length);
        foreach (var p in navPoints)
            V(p);
        U((uint)map.BoundaryBarriers.Count);
        sha.AppendData(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(map.BoundaryBarriers)));
        U((uint)map.SolidMeshes.Count);
        foreach (var i in map.SolidMeshes)
        {
            var mp = map.MeshPlacements[i];
            var t = mp.Transform;
            sha.AppendData(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref t, 1)));
            if (mp.Mesh != null)
                U((uint)mp.Mesh.Lods[0].Hash());
            else if (mp.Model != null)
                foreach (var part in mp.Model.Parts)
                    U((uint)mp.Model.Meshes[part.Mesh].Lods[0].Hash());
        }
        U(map.MoverHash);
        U(map.PlanetHash);
        U(map.DestructionHash);
        return sha.GetHashAndReset();
    }

    /// <summary>The graph's file: header (format, build, map name, fingerprint, key), the graph, a SHA-256 of the graph.</summary>
    public static byte[] Write(NavGraph g, Map map, byte[] key)
    {
        var payload = g.Serialize();
        using var ms = new MemoryStream(payload.Length + 256);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(BuildId);
            w.Write(map.Name);
            w.Write(map.ContentHash);
            w.Write(key);
            w.Write(payload.Length);
            w.Write(payload);
            w.Write(SHA256.HashData(payload));
        }
        return ms.ToArray();
    }

    /// <summary>
    /// The graph in <paramref name="file"/> if it was baked for exactly this map (<paramref name="key"/> from <see cref="KeyOf"/>),
    /// else null with the reason in <paramref name="why"/>. Any thread: it reads only the file and the arguments.
    /// </summary>
    public static NavGraph? Read(byte[] file, string mapName, uint contentHash, byte[] key, out string why)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(file, writable: false), Encoding.UTF8);
            if (!r.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
                return Fail("not a nav bake", out why);
            if (r.ReadInt32() != FormatVersion)
                return Fail("another bake format", out why);
            if (r.ReadString() != BuildId)
                return Fail("baked by another build", out why);
            if (r.ReadString() != mapName || r.ReadUInt32() != contentHash)
                return Fail("another map or fingerprint", out why);
            if (!r.ReadBytes(32).AsSpan().SequenceEqual(key))
                return Fail("the map changed", out why);
            var n = r.ReadInt32();
            if (n < 0 || n > file.Length)
                return Fail("damaged", out why);
            var payload = r.ReadBytes(n);
            if (payload.Length != n || !r.ReadBytes(32).AsSpan().SequenceEqual(SHA256.HashData(payload)))
                return Fail("damaged or cut short", out why);
            var g = NavGraph.Deserialize(payload);
            if (g == null || g.MapName != mapName || g.MapHash != contentHash)
                return Fail("damaged", out why);
            why = "ok";
            return g;
        }
        catch (Exception ex) // whatever is wrong with the file, the graph is built live instead
        {
            return Fail($"unreadable ({ex.GetType().Name})", out why);
        }

        static NavGraph? Fail(string reason, out string why)
        {
            why = reason;
            return null;
        }
    }

    /// <summary>A baked graph for the map, then a cached one (worker thread: files only). Null: build it live.</summary>
    public static NavGraph? TryLoad(string mapName, uint contentHash, byte[] key, out string result)
    {
        var name = FileName(mapName);
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
            if (Read(bytes, mapName, contentHash, key, out var why) is { } g)
            {
                result = what;
                return g;
            }
            notes.Add($"{what} file ignored: {why}");
        }
        result = notes.Count == 0 ? "none" : string.Join("; ", notes);
        return null;
    }

    /// <summary>Remembers what the last load found (game thread).</summary>
    internal static void Note(string result) => LastResult = result;

    /// <summary>Writes a graph to the cache (atomically: a temp file, then a rename). Worker thread; errors are logged, never thrown.</summary>
    public static void WriteCache(string mapName, byte[] file)
    {
        try
        {
            var dir = CacheFolder;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName(mapName));
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, file);
            File.Move(tmp, path, overwrite: true);
            Interlocked.Increment(ref Written);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[bots] couldn't cache the nav graph for {mapName}: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>CallOfSlop --bake DIR</c> (the release workflow): builds every shipped map live and writes it to DIR/Maps
    /// (MapBake.cs), then builds every map's nav graph live and writes it to DIR/Nav. Returns the number of bakes that failed.
    /// </summary>
    public static int BakeAll(string dir)
    {
        Enabled = false; // bake from live builds only
        MapBake.Enabled = false; // the maps too (MapBake.cs): each is built live and written to DIR/Maps as it is built
        MapBake.CacheWrites = false;
        MapBake.BakeOutFolder = Path.Combine(dir, "Maps");
        _ = Maps.All;
        var mapFailures = MapBake.CheckBakes(MapBake.BakeOutFolder);
        var outDir = Path.Combine(dir, "Nav");
        Directory.CreateDirectory(outDir);
        var failed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long bytes = 0;
        foreach (var m in Maps.All)
        {
            try
            {
                World.SetMap(m);
                var key = KeyOf(m, m.ContentHash);
                var g = NavGraph.Build(m);
                var file = Write(g, m, key);
                if (Read(file, m.Name, m.ContentHash, key, out var why) is not { } back || !back.Serialize().AsSpan().SequenceEqual(g.Serialize()))
                    throw new InvalidOperationException($"the bake doesn't read back as built ({why})");
                File.WriteAllBytes(Path.Combine(outDir, FileName(m.Name)), file);
                bytes += file.Length;
                Console.WriteLine($"[bake] {m.Name}: {g.Nodes.Length} nodes, {g.EdgeCount} edges, {g.BuildMs:0} ms, {file.Length / 1024} KB");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"[bake] {m.Name}: FAILED: {ex.Message}");
            }
        }
        Console.WriteLine($"[bake] {Maps.All.Length - failed} of {Maps.All.Length} maps baked to {outDir} in {sw.Elapsed.TotalSeconds:0.0} s ({bytes / 1024} KB); {failed} failed");
        return failed + mapFailures;
    }
}

public sealed partial class NavGraph
{
    /// <summary>Where this graph came from: "built", "baked" or "cached".</summary>
    public string Origin = "built";

    // The graph's state as the build leaves it (before any destruction patch), in a fixed order.
    internal byte[] Serialize()
    {
        using var ms = new MemoryStream(Nodes.Length * 24 + EdgeTo.Length * 22 + 1024);
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(MapName);
            w.Write(MapHash);
            w.Write(Spacing);
            Arr(w, Nodes);
            Arr(w, Openness);
            Arr(w, EdgeStart);
            Arr(w, EdgeTo);
            Arr(w, EdgeCost);
            Arr(w, EdgeFlags);
            Arr(w, EdgeLedge);
            Arr(w, Component);
            w.Write(MainComponent);
            Arr(w, MainNodes);
            w.Write(MantleEdges);
            w.Write(TrapNodes);
            Arr(w, _trapped);
            w.Write(_minX);
            w.Write(_minZ);
            w.Write(_cols);
            w.Write(_rows);
            Arr(w, _cellStart);
            Arr(w, _cellNodes);
            w.Write(WalkChecked);
            w.Write(WalkRejected);
            w.Write(LastResorts);
            w.Write(BuildMs);
            w.Write(BuiltEdges);
            w.Write(SpareStart);
            w.Write(SpareCount);
            w.Write(DestructionAtStart);
            Arr(w, NodeOff);
            Arr(w, ExtraHead);
            w.Write(Tainted);
        }
        return ms.ToArray();
    }

    internal static NavGraph? Deserialize(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data, writable: false), Encoding.UTF8);
        var g = new NavGraph
        {
            MapName = r.ReadString(),
            MapHash = r.ReadUInt32(),
            Spacing = r.ReadSingle(),
        };
        g.Nodes = Arr<Vector3>(r);
        g.Openness = Arr<byte>(r);
        g.EdgeStart = Arr<int>(r);
        g.EdgeTo = Arr<int>(r);
        g.EdgeCost = Arr<float>(r);
        g.EdgeFlags = Arr<byte>(r);
        g.EdgeLedge = Arr<Vector3>(r);
        g.Component = Arr<int>(r);
        g.MainComponent = r.ReadInt32();
        g.MainNodes = Arr<int>(r);
        g.MantleEdges = r.ReadInt32();
        g.TrapNodes = r.ReadInt32();
        g._trapped = Arr<bool>(r);
        g._minX = r.ReadSingle();
        g._minZ = r.ReadSingle();
        g._cols = r.ReadInt32();
        g._rows = r.ReadInt32();
        g._cellStart = Arr<int>(r);
        g._cellNodes = Arr<int>(r);
        g.WalkChecked = r.ReadInt32();
        g.WalkRejected = r.ReadInt32();
        g.LastResorts = r.ReadInt32();
        g.BuildMs = r.ReadDouble();
        g.BuiltEdges = r.ReadInt32();
        g.SpareStart = r.ReadInt32();
        g.SpareCount = r.ReadInt32();
        g.DestructionAtStart = r.ReadInt64();
        g.NodeOff = Arr<bool>(r);
        g.ExtraHead = Arr<int>(r);
        g.Tainted = r.ReadBoolean();
        if (r.BaseStream.Position != data.Length || !g.Consistent())
            return null;
        return g;
    }

    /// <summary>Do the arrays fit together (so a damaged file can never index out of range)?</summary>
    private bool Consistent()
    {
        int n = Nodes.Length, e = EdgeTo.Length;
        if (EdgeStart.Length != n + 1 || EdgeStart[0] != 0 || EdgeStart[n] != e || EdgeCost.Length != e || EdgeFlags.Length != e || EdgeLedge.Length != e
            || Openness.Length != n || Component.Length != n || BuiltEdges != e || _cols < 1 || _rows < 1 || (long)_cols * _rows > 50_000_000
            || _cellStart.Length != _cols * _rows + 1 || _cellNodes.Length != n || _cellStart[^1] != n || (_trapped.Length != 0 && _trapped.Length != n)
            || (NodeOff.Length != 0 && NodeOff.Length != n) || (ExtraHead.Length != 0 && ExtraHead.Length != n) || SpareStart < 0 || SpareCount < 0
            || SpareStart + SpareCount > n || !(Spacing > 0))
            return false;
        for (var i = 0; i < n; i++)
            if (EdgeStart[i + 1] < EdgeStart[i])
                return false;
        foreach (var t in EdgeTo)
            if ((uint)t >= (uint)n)
                return false;
        foreach (var i in MainNodes)
            if ((uint)i >= (uint)n)
                return false;
        foreach (var i in _cellNodes)
            if ((uint)i >= (uint)n)
                return false;
        for (var i = 0; i < _cols * _rows; i++)
            if (_cellStart[i + 1] < _cellStart[i])
                return false;
        return true;
    }

    /// <summary>A graph read from a file, made ready for <paramref name="map"/> (what <see cref="FinishRuntime"/> left that isn't data).</summary>
    internal void Attach(Map map, string origin)
    {
        Origin = origin;
        if (map.Destruction != null)
            _cellMap = map;
    }

    private static void Arr<T>(BinaryWriter w, T[] a) where T : unmanaged
    {
        w.Write(a.Length);
        w.Write(MemoryMarshal.AsBytes(a.AsSpan()));
    }

    private static T[] Arr<T>(BinaryReader r) where T : unmanaged
    {
        var n = r.ReadInt32();
        var size = Unsafe.SizeOf<T>();
        if (n < 0 || (long)n * size > r.BaseStream.Length - r.BaseStream.Position)
            throw new FormatException("bad array length");
        var a = new T[n];
        var bytes = MemoryMarshal.AsBytes(a.AsSpan());
        if (r.Read(bytes) != bytes.Length)
            throw new EndOfStreamException();
        return a;
    }
}
