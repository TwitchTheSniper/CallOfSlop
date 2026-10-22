namespace CallOfSlop;

/// <summary>
/// The decal library: every decal file in the Decals folder (one JSON file per decal, in category subfolders; folders
/// starting with "_", like the _Template, are skipped), loaded on first use. Names are unique (case-insensitive) and
/// are how map ops refer to decals. A decal's <see cref="DecalDef.Index"/> is its layer in the decal atlas.
/// </summary>
public static class Decals
{
    private static readonly object LoadLock = new();
    private static DecalDef[]? _all;
    private static Dictionary<string, DecalDef[]>? _impacts;

    /// <summary>Every decal, sorted by category then name.</summary>
    public static DecalDef[] All
    {
        get
        {
            var all = _all;
            if (all != null)
                return all;
            lock (LoadLock)
                return _all ??= LoadAll(Folder, LoadErrors);
        }
    }

    /// <summary>Problems found while loading decal files (bad files are skipped). Written to the log.</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>The folder decal files are loaded from (next to the game).</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Decals");

    /// <summary>Goes up by one on every <see cref="Reload"/> (the renderer rebuilds its atlas and the maps' decal meshes).</summary>
    public static int Generation { get; private set; }

    /// <summary>A decal by name, or null.</summary>
    public static DecalDef? Find(string name)
    {
        foreach (var d in All)
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    /// <summary>A short hint for an unknown decal name: the closest names in the library.</summary>
    public static string Suggest(string name)
    {
        var close = All.OrderBy(d => Props.Distance(d.Name.ToLowerInvariant(), name.ToLowerInvariant())).Take(3).Select(d => $"\"{d.Name}\"").ToList();
        return close.Count == 0 ? $"the Decals folder ({Folder}) has no decals" : $"did you mean {string.Join(", ", close)}?";
    }

    /// <summary>
    /// The impact decals for a surface material (a name from <see cref="DecalFile.ImpactMaterialNames"/>, as
    /// <see cref="ImpactDecals.SurfaceOf"/> gives it): the decals listing it, else those of its fallback
    /// (<see cref="DecalFile.ImpactFallback"/>: Asphalt to Concrete, Grass to Terrain to Dirt...), else Default's; an unknown
    /// name gets Default's. Cached; no allocation after the first call.
    /// </summary>
    public static DecalDef[] ImpactsFor(string material)
    {
        var map = _impacts ??= BuildImpacts(All);
        return map.TryGetValue(material, out var list) ? list : map.TryGetValue("Default", out var d) ? d : [];
    }

    private static Dictionary<string, DecalDef[]> BuildImpacts(DecalDef[] all)
    {
        var own = new Dictionary<string, DecalDef[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in DecalFile.ImpactMaterialNames)
            own[m] = all.Where(d => d.ImpactMaterials.Contains(m, StringComparer.OrdinalIgnoreCase)).ToArray();
        var map = new Dictionary<string, DecalDef[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in DecalFile.ImpactMaterialNames)
        {
            var at = m;
            var list = own[at];
            for (var hops = 0; list.Length == 0 && hops < 8 && DecalFile.ImpactFallback(at) is { } next; hops++)
                list = own[at = next];
            map[m] = list;
        }
        return map;
    }

    /// <summary>Re-reads the Decals folder (called by <see cref="Maps.Reload"/>, before the maps that place decals).</summary>
    public static void Reload()
    {
        lock (LoadLock)
        {
            LoadErrors.Clear();
            _all = LoadAll(Folder, LoadErrors);
            _impacts = null;
            Generation++;
        }
    }

    /// <summary>
    /// Loads every *.json under <paramref name="folder"/> (the first subfolder is the category; folders starting with "_" or
    /// "." are skipped). Bad files and duplicate names are skipped and described in <paramref name="errors"/>.
    /// </summary>
    public static DecalDef[] LoadAll(string folder, List<string> errors)
    {
        var list = new List<DecalDef>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(folder, file);
                var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Take(parts.Length - 1).Any(p => p.StartsWith('_') || p.StartsWith('.')))
                    continue; // the _Template and other parked folders
                var category = parts.Length > 1 ? parts[0] : "Misc";
                try
                {
                    var def = DecalFile.Load(File.ReadAllText(file), rel, category, Path.GetDirectoryName(file));
                    if (list.FirstOrDefault(x => x.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                        throw new DecalFileException($"{rel}: another decal is already called '{def.Name}' ({dup.SourceFile})");
                    list.Add(def);
                }
                catch (Exception ex) when (ex is DecalFileException or IOException or UnauthorizedAccessException)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[decals] skipped: {ex.Message}");
                }
            }
        }
        if (list.Count > DecalAtlas.MaxLayers)
        {
            errors.Add($"{list.Count} decals is more than the atlas holds ({DecalAtlas.MaxLayers}); the rest are skipped");
            list = list.Take(DecalAtlas.MaxLayers).ToList();
        }
        var sorted = list.OrderBy(d => d.Category, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < sorted.Length; i++)
            sorted[i].Index = i;
        Console.WriteLine($"[decals] {sorted.Length} decal(s) ({sorted.Count(d => d.IsImpact)} impacts) in {sorted.Select(d => d.Category).Distinct().Count()} categories from {folder}"
                          + (errors.Count > 0 ? $", {errors.Count} file(s) skipped" : ""));
        return sorted;
    }
}
