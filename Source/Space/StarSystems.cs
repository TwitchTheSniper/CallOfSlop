namespace CallOfSlop.Space;

/// <summary>
/// The game's star systems (every <c>Systems/*.json</c>, loaded once on first use) and which map each body is played on
/// (a body's <c>"map"</c>). A map named by a body is a bubble in that system (phase 3, <see cref="SpaceBubble"/>). Each
/// system keeps a hash of its file, so a client only follows a host whose system file is the same as its own.
/// </summary>
public static class StarSystems
{
    public readonly record struct Entry(StarSystem System, uint Hash);

    private static List<Entry>? _all;
    private static readonly List<string> _errors = [];

    /// <summary>Files that didn't load, with the reason.</summary>
    public static IReadOnlyList<string> Errors
    {
        get
        {
            _ = All;
            return _errors;
        }
    }

    public static IReadOnlyList<Entry> All => _all ??= LoadAll();

    /// <summary>Loads the folder again (tests).</summary>
    public static void Reload() => _all = null;

    private static List<Entry> LoadAll()
    {
        _errors.Clear();
        var list = new List<Entry>();
        var folder = Path.Combine(AppContext.BaseDirectory, StarSystemFile.FolderName);
        if (!Directory.Exists(folder))
            return list;
        foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var text = File.ReadAllText(f);
                list.Add(new Entry(StarSystemFile.Load(text, Path.GetFileName(f)), HashOf(text)));
            }
            catch (Exception ex) when (ex is StarSystemException or IOException)
            {
                _errors.Add(ex.Message);
                Console.WriteLine($"[space] system skipped: {ex.Message}");
            }
        }
        return list;
    }

    /// <summary>FNV-1a over a file's text (line endings ignored, so a checkout on Windows matches one on Linux).</summary>
    public static uint HashOf(string text)
    {
        var h = 2166136261u;
        foreach (var c in text)
        {
            if (c == '\r')
                continue;
            h = (h ^ c) * 16777619u;
        }
        return h;
    }

    /// <summary>The system and body a map is played on (the first file naming it, by file name), or false.</summary>
    public static bool ForMap(string map, out Entry entry, out int body)
    {
        foreach (var e in All)
            for (var b = 0; b < e.System.Count; b++)
                if (string.Equals(e.System.Bodies[b].Map, map, StringComparison.OrdinalIgnoreCase))
                {
                    entry = e;
                    body = b;
                    return true;
                }
        entry = default;
        body = -1;
        return false;
    }

    /// <summary>A system by its file name, or false.</summary>
    public static bool ByFile(string file, out Entry entry)
    {
        foreach (var e in All)
            if (string.Equals(e.System.File, file, StringComparison.OrdinalIgnoreCase))
            {
                entry = e;
                return true;
            }
        entry = default;
        return false;
    }
}
