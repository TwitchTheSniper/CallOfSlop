namespace CallOfSlop;

/// <summary>
/// The prop library: every prop file in the Props folder (one JSON file per prop, sorted into category subfolders),
/// loaded on first use. Names are unique (case-insensitive) and are how map files refer to props.
/// </summary>
public static partial class Props
{
    private static readonly object LoadLock = new();
    private static PropDef[]? _all;

    /// <summary>Every prop, sorted by category then name. <see cref="PropDef.Index"/> is the position in this list.</summary>
    public static PropDef[] All
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

    /// <summary>Problems found while loading prop files (bad files are skipped). Written to the log.</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>The folder prop files are loaded from (next to the game).</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Props");

    /// <summary>Goes up by one on every <see cref="Reload"/> (the renderer rebuilds its meshes).</summary>
    public static int Generation { get; private set; }

    /// <summary>A prop from the global Props folder by name ("global:Name" works too). Maps look props up through their <see cref="PropScope"/>.</summary>
    public static PropDef? Find(string name)
    {
        PropScope.IsGlobal(name, out name);
        foreach (var p in All)
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    /// <summary>The distinct categories, in library order.</summary>
    public static IEnumerable<string> Categories => All.Select(p => p.Category).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-reads the Props folder (called by <see cref="Maps.Reload"/>, before the maps that use the props).</summary>
    public static void Reload()
    {
        lock (LoadLock)
        {
            LoadErrors.Clear();
            _all = LoadAll(Folder, LoadErrors);
            FlagPool.Reload();
            ClearPacks();
            Generation++;
        }
    }

    /// <summary>A short hint for an unknown prop name: the closest names in the library.</summary>
    public static string Suggest(string name)
    {
        var close = All.OrderBy(p => Distance(p.Name.ToLowerInvariant(), name.ToLowerInvariant())).Take(3).Select(p => $"\"{p.Name}\"").ToList();
        return close.Count == 0 ? $"the Props folder ({Folder}) has no props" : $"did you mean {string.Join(", ", close)}?";
    }

    internal static int Distance(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cur = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = cur;
            }
        }
        return d[b.Length];
    }

    /// <summary>The "name" of a prop file without loading it fully (for ordering prefab loads).</summary>
    private static string PrefabName(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Name.Equals("name", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                    return p.Value.GetString()!.Trim();
        }
        catch (System.Text.Json.JsonException)
        {
        }
        return "";
    }

    /// <summary>
    /// Loads every *.json under <paramref name="folder"/> (subfolders are categories; README and other files are ignored).
    /// Bad files and duplicate names are skipped and described in <paramref name="errors"/>.
    /// </summary>
    public static PropDef[] LoadAll(string folder, List<string> errors)
    {
        var list = new List<PropDef>();
        if (Directory.Exists(folder))
        {
            // Two passes: plain props first, then prefabs (props that "use" others), which may use each other in any
            // file order; a prefab that uses itself round a chain is refused with the chain in the message.
            var prefabs = new List<(string File, string Rel, string Category, string Json)>();
            foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(folder, file);
                var dir = Path.GetDirectoryName(rel);
                var category = string.IsNullOrEmpty(dir) ? "Misc" : dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                if (category.Equals(FlagPool.FolderName, StringComparison.OrdinalIgnoreCase))
                    continue; // Props/Flags holds the flag pool's designs, not props (FlagPool.cs)
                try
                {
                    var json = File.ReadAllText(file);
                    if (PropFile.UsesPrefabs(json))
                    {
                        prefabs.Add((file, rel, category, json));
                        continue;
                    }
                    Add(PropFile.Load(json, rel, category), rel);
                }
                catch (Exception ex) when (ex is PropFileException or IOException or UnauthorizedAccessException)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[props] skipped: {ex.Message}");
                }
            }
            var visiting = new List<string>();
            PropDef? Resolve(string name)
            {
                if (list.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } found)
                    return found;
                // Not loaded yet: a prefab further down the list, loaded now (recursively), unless we're already inside it.
                foreach (var (file, rel, category, json) in prefabs)
                {
                    if (!PrefabName(json).Equals(name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (visiting.Any(v => v.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        throw new PropFileException($"{rel}: prop '{name}' uses itself through {string.Join(" -> ", visiting.Append(name))} (a prefab can't contain itself)");
                    visiting.Add(name);
                    try
                    {
                        var def = PropFile.Load(json, rel, category, Resolve);
                        Add(def, rel);
                        return def;
                    }
                    finally
                    {
                        visiting.RemoveAt(visiting.Count - 1);
                    }
                }
                return null;
            }
            foreach (var (file, rel, category, json) in prefabs)
            {
                var name = PrefabName(json);
                if (list.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.SourceFile == rel))
                    continue; // loaded while resolving another prefab
                try
                {
                    visiting.Clear();
                    visiting.Add(name);
                    Add(PropFile.Load(json, rel, category, Resolve), rel);
                }
                catch (PropFileException ex)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[props] skipped: {ex.Message}");
                }
            }
        }
        void Add(PropDef def, string rel)
        {
            if (list.FirstOrDefault(x => x.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                throw new PropFileException($"{rel}: another prop is already called '{def.Name}' ({dup.SourceFile})");
            list.Add(def);
        }
        var sorted = list.OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < sorted.Length; i++)
            sorted[i].Index = i;
        Console.WriteLine($"[props] {sorted.Length} prop(s) in {sorted.Select(p => p.Category).Distinct().Count()} categories from {folder}"
                          + (errors.Count > 0 ? $", {errors.Count} file(s) skipped" : ""));
        return sorted;
    }
}
