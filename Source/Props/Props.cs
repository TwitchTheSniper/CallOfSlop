namespace CallOfSlop;

/// <summary>
/// The prop library: every prop file in the Props folder (one JSON file per prop, sorted into category subfolders),
/// loaded on first use. Names are unique (case-insensitive) and are how map files refer to props.
/// </summary>
public static class Props
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

    public static PropDef? Find(string name)
    {
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
            Generation++;
        }
    }

    /// <summary>A short hint for an unknown prop name: the closest names in the library.</summary>
    public static string Suggest(string name)
    {
        var close = All.OrderBy(p => Distance(p.Name.ToLowerInvariant(), name.ToLowerInvariant())).Take(3).Select(p => $"\"{p.Name}\"").ToList();
        return close.Count == 0 ? $"the Props folder ({Folder}) has no props" : $"did you mean {string.Join(", ", close)}?";
    }

    private static int Distance(string a, string b)
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

    /// <summary>
    /// Loads every *.json under <paramref name="folder"/> (subfolders are categories; README and other files are ignored).
    /// Bad files and duplicate names are skipped and described in <paramref name="errors"/>.
    /// </summary>
    public static PropDef[] LoadAll(string folder, List<string> errors)
    {
        var list = new List<PropDef>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(folder, file);
                var dir = Path.GetDirectoryName(rel);
                var category = string.IsNullOrEmpty(dir) ? "Misc" : dir.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
                try
                {
                    var def = PropFile.Load(File.ReadAllText(file), rel, category);
                    if (list.FirstOrDefault(x => x.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                        throw new PropFileException($"{rel}: another prop is already called '{def.Name}' ({dup.SourceFile})");
                    list.Add(def);
                }
                catch (Exception ex) when (ex is PropFileException or IOException or UnauthorizedAccessException)
                {
                    errors.Add(ex.Message);
                    Console.WriteLine($"[props] skipped: {ex.Message}");
                }
            }
        }
        var sorted = list.OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < sorted.Length; i++)
            sorted[i].Index = i;
        Console.WriteLine($"[props] {sorted.Length} prop(s) in {sorted.Select(p => p.Category).Distinct().Count()} categories from {folder}"
                          + (errors.Count > 0 ? $", {errors.Count} file(s) skipped" : ""));
        return sorted;
    }
}
