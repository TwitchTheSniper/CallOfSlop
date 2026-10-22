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
    public DecalDef[] Local
    {
        get => _local;
        init
        {
            _local = value;
            _byName = new Dictionary<string, DecalDef>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in value)
                _byName.TryAdd(d.Name, d);
        }
    }

    private readonly DecalDef[] _local = [];
    private readonly Dictionary<string, DecalDef>? _byName;

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
        if (_byName != null && _byName.TryGetValue(name, out var local))
            return local;
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

    /// <summary>
    /// The most decals one pack's Decals folder may hold. Only the pack decals a map actually places get atlas layers
    /// (painted when that map loads), so a big pack costs parse time and a little memory, not video memory.
    /// </summary>
    public const int MaxPerPack = 4096;

    /// <summary>
    /// The most different pack decals one map may use. With the global library (<see cref="MaxGlobal"/>) that stays inside
    /// the 2048 array texture layers OpenGL 4.1 guarantees (<see cref="DecalAtlas.MaxLayers"/>).
    /// </summary>
    public const int MaxPackDecalsPerMap = 1024;

    private static DecalScope? _globalScope;
    private static readonly Dictionary<string, DecalScope> PackScopes = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>Drops one pack's cached scope, so the next <see cref="ForPack"/> reads its Decals folder again.</summary>
    public static void ForgetPack(string packDir)
    {
        lock (LoadLock)
            PackScopes.Remove(Path.GetFullPath(packDir));
    }

    private static void ClearPacks()
    {
        PackScopes.Clear();
        _globalScope = null;
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
        foreach (var e in errors)
        {
            LoadErrors.Add($"{pack}/{PackFolderName}: {e}");
            Console.WriteLine($"[decals] pack {pack}: {e}");
        }
        for (var i = 0; i < local.Count; i++)
            local[i].Index = i; // its place in the pack (not an atlas layer: a map's bake gives its pack decals layers)
        var globalNames = new HashSet<string>(global.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        var shadowed = local.Where(l => globalNames.Contains(l.Name)).Select(l => l.Name).ToArray();
        foreach (var n in shadowed)
            Console.WriteLine($"[decals] pack {pack}: \"{n}\" shadows the global decal of that name for this pack's maps (\"{PropScope.GlobalPrefix}{n}\" still means the global one)");
        var ms = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[decals] pack {pack}: {local.Count} pack decal(s) from {folder}, {shadowed.Length} shadowing a global one, {ms:0.0} ms");
        return new DecalScope { Pack = pack, Folder = folder, Local = local.ToArray(), Shadowed = shadowed, LoadMs = ms };
    }
}
