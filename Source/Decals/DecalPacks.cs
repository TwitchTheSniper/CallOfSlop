using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// The decals one map can name. Maps in a pack see the pack's own decals (Maps/&lt;Pack&gt;/Decals/**.json, PNG images
/// next to them) first, then the global Decals folder; a pack decal with the same name as a global one shadows it for
/// that pack's maps only, and "global:Name" always means the global decal. Maps outside a pack, and packs without a
/// Decals folder, use <see cref="Decals.Global"/>. Same rules as pack props (<see cref="PropScope"/>).
/// Decals are cosmetic, so pack decals are not part of any map fingerprint.
/// </summary>
public sealed class DecalScope
{
    /// <summary>The pack folder name (null for the global scope).</summary>
    public string? Pack { get; init; }

    /// <summary>The pack's Decals folder (null for the global scope).</summary>
    public string? Folder { get; init; }

    /// <summary>The pack's own decals, sorted by category then name (empty for the global scope).</summary>
    public DecalDef[] Local { get; init; } = [];

    /// <summary>Names of global decals the pack shadows with its own.</summary>
    public string[] Shadowed { get; init; } = [];

    /// <summary>Time spent loading the pack's decal files (0 for the global scope).</summary>
    public double LoadMs { get; init; }

    public bool IsGlobalScope => Pack == null;

    /// <summary>The decal called <paramref name="name"/>: the pack's own first, then the global one ("global:" skips the pack).</summary>
    public DecalDef? Find(string name)
    {
        if (PropScope.IsGlobal(name, out var bare))
            return Decals.Find(bare);
        foreach (var d in Local)
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return Decals.Find(name);
    }

    /// <summary>A short hint for an unknown decal name: the closest names this scope can see.</summary>
    public string Suggest(string name)
    {
        if (IsGlobalScope || PropScope.IsGlobal(name, out _))
            return Decals.Suggest(PropScope.IsGlobal(name, out var bare) ? bare : name);
        var close = Local.Select(d => d.Name).Concat(Decals.All.Select(d => d.Name))
            .OrderBy(n => Props.Distance(n.ToLowerInvariant(), name.ToLowerInvariant())).Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3).Select(n => $"\"{n}\"").ToList();
        return close.Count == 0 ? "no decals in this pack or the Decals folder" : $"did you mean {string.Join(", ", close)}?";
    }
}

public static partial class Decals
{
    /// <summary>The name of a pack's own decal folder (Maps/&lt;Pack&gt;/Decals).</summary>
    public const string PackFolderName = "Decals";

    /// <summary>The most decals the global Decals folder may hold (the self-test asks for a quarter of it free).</summary>
    public const int MaxGlobal = 256;

    /// <summary>The most decals one pack's Decals folder may hold.</summary>
    public const int MaxPerPack = 128;

    private static DecalScope? _globalScope;
    private static readonly Dictionary<string, DecalScope> PackScopes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pack decals loaded so far, in load order (append-only until <see cref="Reload"/>, so atlas layers never move).</summary>
    private static readonly List<DecalDef> PackLayers = new();

    private static DecalDef[]? _atlasDefs;

    /// <summary>
    /// Goes up whenever the atlas gains layers (a pack's decals loaded) or the library reloads. The renderer repaints only
    /// the new layers on a worker thread; map decal meshes don't rebake (existing layers keep their index).
    /// </summary>
    public static int AtlasVersion { get; private set; }

    /// <summary>The scope of maps outside any pack: just the global Decals folder.</summary>
    public static DecalScope Global
    {
        get
        {
            _ = All;
            lock (LoadLock)
                return _globalScope ??= new DecalScope();
        }
    }

    /// <summary>
    /// Every decal with a layer in the atlas: <see cref="All"/> (layers 0..All.Length-1), then every pack's decals loaded so
    /// far (each pack's decals only load when one of its maps does). <see cref="DecalDef.Index"/> is the position here.
    /// </summary>
    public static DecalDef[] AtlasDefs
    {
        get
        {
            var all = All;
            lock (LoadLock)
                return _atlasDefs ??= all.Concat(PackLayers).ToArray();
        }
    }

    /// <summary>
    /// The decals a map in the pack folder <paramref name="packDir"/> (an absolute path, or null for no pack) can name.
    /// A pack without a Decals folder gets <see cref="Global"/>. Loaded lazily per pack and cached until <see cref="Reload"/>.
    /// Thread-safe.
    /// </summary>
    public static DecalScope ForPack(string? packDir)
    {
        if (string.IsNullOrEmpty(packDir))
            return Global;
        var key = Path.GetFullPath(packDir);
        var all = All;
        lock (LoadLock)
        {
            if (PackScopes.TryGetValue(key, out var hit))
                return hit;
            var scope = LoadPack(key, all);
            PackScopes[key] = scope;
            return scope;
        }
    }

    /// <summary>Every pack scope loaded so far (packs with their own decals only).</summary>
    public static DecalScope[] LoadedPacks
    {
        get
        {
            lock (LoadLock)
                return PackScopes.Values.Where(s => !s.IsGlobalScope).OrderBy(s => s.Pack, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>
    /// Drops one pack's cached scope, so the next <see cref="ForPack"/> reads its Decals folder again (its old layers stay
    /// in the atlas until <see cref="Reload"/>, so maps already baked keep drawing).
    /// </summary>
    public static void ForgetPack(string packDir)
    {
        lock (LoadLock)
            PackScopes.Remove(Path.GetFullPath(packDir));
    }

    private static void ClearPacks()
    {
        PackScopes.Clear();
        PackLayers.Clear();
        _atlasDefs = null;
        _globalScope = null;
        AtlasVersion++;
    }

    private static DecalScope LoadPack(string packDir, DecalDef[] global)
    {
        var folder = Path.Combine(packDir, PackFolderName);
        if (!Directory.Exists(folder))
            return Global;
        var sw = Stopwatch.StartNew();
        var pack = Path.GetFileName(packDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var errors = new List<string>();
        var loaded = LoadAll(folder, errors, MaxPerPack);
        var local = new List<DecalDef>();
        foreach (var d in loaded)
        {
            d.Pack = pack;
            d.SourceFile = $"{pack}/{PackFolderName}/{d.SourceFile.Replace('\\', '/')}";
            if (d.IsImpact)
                errors.Add($"{d.SourceFile}: impact decals are global (put it in the Decals folder); skipped");
            else
                local.Add(d);
        }
        var room = DecalAtlas.MaxLayers - global.Length - PackLayers.Count;
        if (local.Count > room)
        {
            errors.Add($"{local.Count} pack decals but the atlas has room for {Math.Max(0, room)} more ({DecalAtlas.MaxLayers} layers); the rest are skipped");
            local = local.Take(Math.Max(0, room)).ToList();
        }
        foreach (var e in errors)
        {
            LoadErrors.Add($"{pack}/{PackFolderName}: {e}");
            Console.WriteLine($"[decals] pack {pack}: {e}");
        }
        for (var i = 0; i < local.Count; i++)
            local[i].Index = global.Length + PackLayers.Count + i;
        PackLayers.AddRange(local);
        if (local.Count > 0)
        {
            _atlasDefs = null;
            AtlasVersion++;
        }
        var shadowed = local.Where(l => global.Any(g => g.Name.Equals(l.Name, StringComparison.OrdinalIgnoreCase))).Select(l => l.Name).ToArray();
        foreach (var n in shadowed)
            Console.WriteLine($"[decals] pack {pack}: \"{n}\" shadows the global decal of that name for this pack's maps (\"{PropScope.GlobalPrefix}{n}\" still means the global one)");
        var ms = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[decals] pack {pack}: {local.Count} pack decal(s) from {folder}, {shadowed.Length} shadowing a global one, "
                          + $"atlas now {global.Length + PackLayers.Count}/{DecalAtlas.MaxLayers} layers, {ms:0.0} ms");
        return new DecalScope { Pack = pack, Folder = folder, Local = local.ToArray(), Shadowed = shadowed, LoadMs = ms };
    }
}
