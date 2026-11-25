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

    public static void Save(string json)
    {
        if (ReadOnly)
            return;
        if (!SavePaths.WriteAtomic(SavePaths.PathOf(FileName), json))
            Console.WriteLine($"[ship designer] {FileName} not written");
    }
}
