using System.Text;

namespace CallOfSlop;

/// <summary>One saved world as the lobby lists it.</summary>
public sealed class WorldEntry
{
    public string Folder = ""; // its folder's name under Worlds/
    public WorldMeta Meta = new();
    public string Problem = ""; // why it can't be played here ("": it can)
    public bool FromBackup; // the main file was damaged: this is the backup's
}

/// <summary>
/// The saved worlds on disk (docs/sandbox/WORLD-SAVES.md): one folder a world under the save folder,
/// <c>Worlds/&lt;name&gt;/world.sav</c>, with <c>world.sav.bak</c> beside it. Like every save (<see cref="SavePaths"/>) it
/// goes wherever the save folder is (portable.txt, --save-dir), a write is atomic and the file before it is the backup;
/// a damaged file is kept as <c>.corrupt</c> and the backup is used.
/// </summary>
public static class WorldStore
{
    public const string RootName = "Worlds", FileName = "world.sav";
    public const int MaxNameLength = 32;

    public static string Root => Path.Combine(SavePaths.Folder, RootName);

    public static string PathOf(string folder) => Path.Combine(Root, folder, FileName);

    /// <summary>A world's name as typed: printable ASCII, tidied. Empty when nothing is left.</summary>
    public static string CleanName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
            if (c is >= ' ' and < (char)127 && sb.Length < MaxNameLength)
                sb.Append(c);
        return sb.ToString().Trim();
    }

    /// <summary>The folder a name gets: letters, digits and dashes (the name itself stays in the file).</summary>
    public static string FolderFor(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        var slug = sb.ToString().Trim('-');
        return slug.Length > 0 ? slug : "world";
    }

    /// <summary>Why a new world can't take this name, or null.</summary>
    public static string? NameRefusal(string name)
    {
        var clean = CleanName(name);
        if (clean.Length == 0)
            return "Give the world a name.";
        if (Directory.Exists(Path.Combine(Root, FolderFor(clean))))
            return "A world with that name is already saved.";
        return null;
    }

    /// <summary>Every saved world, the last played first.</summary>
    public static List<WorldEntry> List()
    {
        var list = new List<WorldEntry>();
        try
        {
            if (!Directory.Exists(Root))
                return list;
            foreach (var dir in Directory.EnumerateDirectories(Root))
                if (Peek(Path.GetFileName(dir)) is { } e)
                    list.Add(e);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[worlds] couldn't list {Root}: {ex.Message}");
        }
        list.Sort((a, b) => b.Meta.SavedUtc.CompareTo(a.Meta.SavedUtc));
        return list;
    }

    /// <summary>A world's entry from the start of its file (its backup's when the file is damaged), or null when it has neither.</summary>
    private static WorldEntry? Peek(string folder)
    {
        var path = PathOf(folder);
        var e = new WorldEntry { Folder = folder };
        var main = PeekFile(path);
        if (main.Meta != null && main.Why == null)
        {
            e.Meta = main.Meta;
            return e;
        }
        var bak = PeekFile(SavePaths.BackupOf(path));
        if (bak.Meta != null && bak.Why == null)
        {
            e.Meta = bak.Meta;
            e.FromBackup = true;
            return e;
        }
        if (main.Format > WorldFile.Format || bak.Format > WorldFile.Format)
        {
            e.Meta = new WorldMeta { Name = folder };
            e.Problem = WorldFile.NewerText(Math.Max(main.Format, bak.Format));
            return e;
        }
        if (!File.Exists(path) && !File.Exists(SavePaths.BackupOf(path)))
            return null;
        e.Meta = new WorldMeta { Name = folder };
        e.Problem = $"damaged: {main.Why ?? bak.Why}";
        return e;
    }

    private static (WorldMeta? Meta, int Format, string? Why) PeekFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return (null, 0, null);
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var head = new byte[Math.Min(f.Length, 4096)];
            f.ReadExactly(head);
            var (format, meta, _) = WorldFile.ReadHead(head);
            return (meta, format, format > WorldFile.Format ? WorldFile.NewerText(format) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or WorldFileException)
        {
            return (null, 0, ex.Message);
        }
    }

    /// <summary>
    /// A world's body. A damaged file is copied aside as <c>.corrupt</c> and its backup is used. Throws
    /// <see cref="WorldFileException"/> with a reason for the host when neither can be read.
    /// </summary>
    public static byte[] Read(string folder, out WorldMeta meta)
    {
        var path = PathOf(folder);
        string why;
        try
        {
            return WorldFile.Unpack(File.ReadAllBytes(path), out meta, out _);
        }
        catch (WorldFileException ex) when (ex.Message.StartsWith("saved by a newer", StringComparison.Ordinal))
        {
            throw;
        }
        catch (Exception ex) when (ex is WorldFileException or IOException or UnauthorizedAccessException)
        {
            why = ex.Message;
        }
        Console.WriteLine($"[worlds] {folder}: {FileName} can't be read ({why}); trying its backup");
        try
        {
            if (File.Exists(path))
                File.Copy(path, path + ".corrupt", overwrite: true); // (kept for a bug report)
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not important.
        }
        try
        {
            var body = WorldFile.Unpack(File.ReadAllBytes(SavePaths.BackupOf(path)), out meta, out _);
            Console.WriteLine($"[worlds] {folder}: loaded from its backup");
            return body;
        }
        catch (Exception ex) when (ex is WorldFileException or IOException or UnauthorizedAccessException)
        {
            throw new WorldFileException($"the save and its backup are both unreadable ({why}; {ex.Message})");
        }
    }

    /// <summary>Writes a world's file atomically, the file before it kept as the backup. Refuses to write over a newer build's save.</summary>
    public static string? Write(string folder, byte[] file)
    {
        var path = PathOf(folder);
        var old = PeekFile(path);
        if (old.Format > WorldFile.Format)
            return WorldFile.NewerText(old.Format);
        return SavePaths.WriteAtomic(path, file, previousGood: old.Meta != null && old.Why == null) ? null : "the file couldn't be written";
    }

    /// <summary>Deletes a saved world for good (the lobby asks first). Null when it went.</summary>
    public static string? Delete(string folder)
    {
        try
        {
            // (a folder name from our own listing: never a path, never above Worlds/)
            if (folder.Length == 0 || folder.Contains('/') || folder.Contains('\\') || folder.Contains(".."))
                return "not a world's folder";
            Directory.Delete(Path.Combine(Root, folder), recursive: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }
}
