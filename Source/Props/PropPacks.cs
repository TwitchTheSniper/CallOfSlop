using System.Diagnostics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The props one map can use. Maps in a pack see the pack's own props (Maps/&lt;Pack&gt;/Props) first, then the global
/// Props folder; a pack prop with the same name as a global one shadows it for that pack's maps only, and "global:Name"
/// always means the global prop. Maps outside a pack, and packs without a Props folder, use <see cref="Props.Global"/>.
/// Built once per pack on first use and cached until <see cref="Props.Reload"/>.
/// </summary>
public sealed class PropScope
{
    /// <summary>Prefix that forces the global prop or category: "global:Wooden Crate", "global:Street".</summary>
    public const string GlobalPrefix = "global:";

    /// <summary>The pack folder name (null for the global scope).</summary>
    public string? Pack { get; init; }

    /// <summary>The pack's Props folder (null for the global scope).</summary>
    public string? Folder { get; init; }

    /// <summary>The pack's own props, sorted by category then name (empty for the global scope).</summary>
    public PropDef[] Local { get; init; } = [];

    /// <summary>What the pack's maps see by plain name: the pack's props plus every global prop they don't shadow, sorted by category then name.</summary>
    public required PropDef[] Visible { get; init; }

    /// <summary>
    /// The mesh library for the pack's maps: every global prop (same order as <see cref="Props.All"/>), then the pack's
    /// props. For the global scope it is <see cref="Props.All"/> itself, so the renderer can compare references.
    /// </summary>
    public required PropDef[] Library { get; init; }

    /// <summary>Names of global props the pack shadows with its own.</summary>
    public string[] Shadowed { get; init; } = [];

    /// <summary>Time spent loading the pack's prop files (0 for the global scope).</summary>
    public double LoadMs { get; init; }

    public bool IsGlobalScope => Pack == null;

    /// <summary>Strips a leading "global:" (any case). Returns true when it was there.</summary>
    public static bool IsGlobal(string name, out string bare)
    {
        if (name.StartsWith(GlobalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            bare = name[GlobalPrefix.Length..].Trim();
            return true;
        }
        bare = name;
        return false;
    }

    /// <summary>The prop called <paramref name="name"/>: the pack's own first, then the global one ("global:" skips the pack).</summary>
    public PropDef? Find(string name)
    {
        if (IsGlobal(name, out var bare))
            return Props.Find(bare);
        foreach (var p in Local)
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p;
        return Props.Find(name);
    }

    /// <summary>Every visible prop in category <paramref name="name"/> ("global:Street": the global props in it only).</summary>
    public List<PropDef> InCategory(string name)
    {
        if (IsGlobal(name, out var bare))
            return Props.All.Where(p => p.Category.Equals(bare, StringComparison.OrdinalIgnoreCase)).ToList();
        return Visible.Where(p => p.Category.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>A short hint for an unknown prop or category name: the closest visible names.</summary>
    public string Suggest(string name)
    {
        if (IsGlobalScope || IsGlobal(name, out _))
            return Props.Suggest(IsGlobal(name, out var bare) ? bare : name);
        var close = Visible.OrderBy(p => Props.Distance(p.Name.ToLowerInvariant(), name.ToLowerInvariant())).Take(3).Select(p => $"\"{p.Name}\"").ToList();
        return close.Count == 0 ? "no props in this pack or the Props folder" : $"did you mean {string.Join(", ", close)}?";
    }
}

public static partial class Props
{
    private static PropScope? _global;
    private static readonly Dictionary<string, PropScope> PackScopes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name of a pack's own prop folder (Maps/&lt;Pack&gt;/Props).</summary>
    public const string PackFolderName = "Props";

    /// <summary>The scope of maps outside any pack: just the global Props folder.</summary>
    public static PropScope Global
    {
        get
        {
            var all = All;
            var g = _global;
            if (g != null && ReferenceEquals(g.Library, all))
                return g;
            lock (LoadLock)
                return _global = new PropScope { Visible = all, Library = all };
        }
    }

    /// <summary>
    /// The props a map in the pack folder <paramref name="packDir"/> (an absolute path, or null for no pack) can use.
    /// A pack without a Props folder gets <see cref="Global"/> (one directory check, then cached). Loaded lazily per
    /// pack, cached until <see cref="Reload"/>. Thread-safe.
    /// </summary>
    public static PropScope ForPack(string? packDir)
    {
        if (string.IsNullOrEmpty(packDir))
            return Global;
        var key = Path.GetFullPath(packDir);
        lock (LoadLock)
        {
            if (PackScopes.TryGetValue(key, out var hit))
                return hit.IsGlobalScope ? Global : hit;
            var scope = LoadPack(key);
            PackScopes[key] = scope;
            return scope;
        }
    }

    /// <summary>Every pack scope loaded so far (packs with their own props only).</summary>
    public static PropScope[] LoadedPacks
    {
        get
        {
            lock (LoadLock)
                return PackScopes.Values.Where(s => !s.IsGlobalScope).OrderBy(s => s.Pack, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>Drops one pack's cached props, so the next <see cref="ForPack"/> reads its Props folder again.</summary>
    public static void ForgetPack(string packDir)
    {
        lock (LoadLock)
            PackScopes.Remove(Path.GetFullPath(packDir));
    }

    /// <summary>Re-reads one pack's Props folder (without touching the global library or other packs).</summary>
    public static PropScope ReloadPack(string packDir)
    {
        lock (LoadLock)
        {
            ForgetPack(packDir);
            return ForPack(packDir);
        }
    }

    private static void ClearPacks()
    {
        PackScopes.Clear();
        _global = null;
    }

    private static PropScope LoadPack(string packDir)
    {
        var folder = Path.Combine(packDir, PackFolderName);
        var global = All;
        if (!Directory.Exists(folder))
            return Global;
        var sw = Stopwatch.StartNew();
        var pack = Path.GetFileName(packDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var errors = new List<string>();
        var local = LoadAll(folder, errors);
        foreach (var e in errors)
            LoadErrors.Add($"{pack}/{PackFolderName}: {e}");
        foreach (var d in local)
        {
            var rel = d.SourceFile ?? "";
            d.Pack = pack;
            d.SourceFile = $"{pack}/{PackFolderName}/{rel.Replace('\\', '/')}";
            try
            {
                d.FileHash = CanonicalHash(File.ReadAllText(Path.Combine(folder, rel)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                d.FileHash = 1;
            }
        }
        var shadowed = local.Where(l => global.Any(g => g.Name.Equals(l.Name, StringComparison.OrdinalIgnoreCase))).Select(l => l.Name).ToArray();
        foreach (var n in shadowed)
            Console.WriteLine($"[props] pack {pack}: \"{n}\" shadows the global prop of that name for this pack's maps (\"{PropScope.GlobalPrefix}{n}\" still means the global one)");
        var library = global.Concat(local).ToArray();
        for (var i = 0; i < local.Length; i++)
            local[i].Index = global.Length + i;
        var visible = local.Concat(global.Where(g => !shadowed.Contains(g.Name, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        var ms = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[props] pack {pack}: {local.Length} pack prop(s) from {folder}, {shadowed.Length} shadowing a global one, {ms:0.0} ms");
        return new PropScope { Pack = pack, Folder = folder, Local = local, Visible = visible, Library = library, Shadowed = shadowed, LoadMs = ms };
    }

    /// <summary>FNV-1a of a JSON text with whitespace and comments dropped (so line endings and formatting don't count).</summary>
    public static uint CanonicalHash(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
            doc.RootElement.WriteTo(w);
        var h = 2166136261u;
        foreach (var b in ms.GetBuffer().AsSpan(0, (int)ms.Length))
        {
            h ^= b;
            h *= 16777619u;
        }
        return h == 0 ? 1 : h;
    }
}
