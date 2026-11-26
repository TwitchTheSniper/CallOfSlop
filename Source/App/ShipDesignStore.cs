namespace CallOfSlop;

/// <summary>
/// The design open in the Ship Designer (main menu > SHIP DESIGNER, UI/UiShipDesigner.cs), kept in the save folder as
/// shipdesign.json after each edit, as the web page keeps it in its browser: the designer opens on it again.
/// </summary>
public static class ShipDesignStore
{
    public const string FileName = "shipdesign.json";

    /// <summary>A saved design this build could not read, kept aside before the designer writes over it (the page keeps one too).</summary>
    public const string UnreadName = "shipdesign.unread.json";

    /// <summary>Test runs set this so nothing is written.</summary>
    public static bool ReadOnly;

    public static string? Load()
    {
        try
        {
            var p = SavePaths.PathOf(FileName);
            return File.Exists(p) ? File.ReadAllText(p) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ship designer] {FileName} not read: {ex.Message}");
            return null;
        }
    }

    /// <summary>Keeps a design that could not be read in <see cref="UnreadName"/> (an older copy there is replaced).</summary>
    public static void KeepUnread(string json)
    {
        if (ReadOnly)
            return;
        if (!SavePaths.WriteAtomic(SavePaths.PathOf(UnreadName), json))
            Console.WriteLine($"[ship designer] {UnreadName} not written");
    }

    /// <summary>The players' own designs (main menu > SHIP DESIGNER, "Keep in my designs"): one file a design, named after it (SavePaths.SaveFolders).</summary>
    public const string FolderName = "ShipDesigns";

    public static string Folder => Path.Combine(SavePaths.Folder, FolderName);

    /// <summary>The shipped designs next to the exe (Constructs/Designs: the Zero Day), as Blueprints ship.</summary>
    public static string ShippedFolder => Path.Combine(AppFolders.Data, "Constructs", "Designs");

    /// <summary>A design's file name from its name: letters, digits, spaces, dashes and brackets kept, the rest dropped.</summary>
    public static string FileOf(string name)
    {
        var s = new string(name.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is ' ' or '-' or '_' or '(' or ')').ToArray()).Trim();
        return (s.Length == 0 ? "Ship" : s.Length > 60 ? s[..60] : s) + ".json";
    }

    /// <summary>Keeps a design in the players' own folder under its name (an older one of that name is replaced). False when it couldn't be written.</summary>
    public static bool Keep(string name, string json)
    {
        if (ReadOnly)
            return true;
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ship designer] {Folder} not made: {ex.Message}");
            return false;
        }
        var ok = SavePaths.WriteAtomic(Path.Combine(Folder, FileOf(name)), json);
        if (!ok)
            Console.WriteLine($"[ship designer] {FileOf(name)} not written");
        return ok;
    }

    /// <summary>A design the Creative parts menu can spawn: its name, its JSON, where it came from.</summary>
    public readonly record struct Listed(string Name, string Json, bool Shipped, bool Current);

    /// <summary>
    /// Every design the Creative parts menu lists: the shipped ones, the players' own and the one open in the designer (shipdesign.json), each
    /// read once a call (the menu asks again when it opens). A file that isn't a design is left out, logged.
    /// </summary>
    public static List<Listed> List()
    {
        var list = new List<Listed>();
        void From(string dir, bool shipped)
        {
            try
            {
                if (!Directory.Exists(dir))
                    return;
                foreach (var f in Directory.GetFiles(dir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                    if (Named(File.ReadAllText(f)) is { } d)
                        list.Add(new Listed(d.Name, d.Json, shipped, false));
                    else
                        Console.WriteLine($"[ship designer] {f}: not a ship design, not listed");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[ship designer] {dir} not read: {ex.Message}");
            }
        }
        From(ShippedFolder, true);
        From(Folder, false);
        if (Load() is { } open && Named(open) is { } cur)
            list.Add(new Listed(cur.Name, cur.Json, false, true));
        return list;
    }

    private static (string Name, string Json)? Named(string json)
    {
        try
        {
            return (ShipDesigner.ShipDesign.FromJson(json).Name, json);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or System.Text.Json.JsonException or KeyNotFoundException or ArgumentException)
        {
            Console.WriteLine($"[ship designer] a design file isn't read: {ex.Message}");
            return null;
        }
    }

    public static void Save(string json)
    {
        if (ReadOnly)
            return;
        if (!SavePaths.WriteAtomic(SavePaths.PathOf(FileName), json))
            Console.WriteLine($"[ship designer] {FileName} not written");
    }
}
