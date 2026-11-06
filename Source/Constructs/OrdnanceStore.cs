namespace CallOfSlop;

/// <summary>The player's saved bomb and missile designs (ordnance.txt in the save folder: a line each, the name, a tab, the key). Written atomically, with a .bak.</summary>
public static class OrdnanceStore
{
    public const string FileName = "ordnance.txt";

    public static readonly List<(string Name, string Key)> Designs = [];
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        var path = SavePaths.PathOf(FileName);
        if (!File.Exists(path))
            return;
        foreach (var line in File.ReadAllLines(path))
            if (line.Split('\t') is { Length: 2 } bits && bits[1].StartsWith(Ordnance.Prefix, StringComparison.Ordinal))
                Designs.Add((bits[0], bits[1]));
    }

    public static void Put(string name, string key)
    {
        EnsureLoaded();
        name = name.Replace('\t', ' ').Trim();
        if (name.Length == 0)
            return;
        var at = Designs.FindIndex(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
            Designs[at] = (name, key);
        else
            Designs.Add((name, key));
        Save();
    }

    public static void Remove(int at)
    {
        Designs.RemoveAt(at);
        Save();
    }

    private static void Save() => SavePaths.WriteAtomic(SavePaths.PathOf(FileName), string.Concat(Designs.Select(d => $"{d.Name}\t{d.Key}\n")));
}
