namespace CallOfSlop;

/// <summary>
/// Where the game's own files are: the program, its data (Maps, Weapons, Parts, Props, Baked...) and its logs.
/// <para>
/// Windows and Linux: all three are the game's folder (<see cref="AppContext.BaseDirectory"/>), exactly as before.
/// </para>
/// <para>
/// macOS, inside <c>CallOfSlop.app</c>: the program is <c>Contents/MacOS</c>, the data is <c>Contents/Resources</c> (where
/// a Mac app keeps it, so the bundle can be signed later) and the logs go to <c>~/Library/Logs/CallOfSlop</c>, because
/// a downloaded app may run from a read-only copy (App Translocation) and must never write into its own bundle.
/// A plain build folder on a Mac (dev builds, CI) behaves like Linux. Saves are <see cref="SavePaths"/>'s business.
/// </para>
/// </summary>
public static class AppFolders
{
    /// <summary>The folder holding the program (CallOfSlop.dll, the natives).</summary>
    public static string Exe => AppContext.BaseDirectory;

    private static string? _data, _logs;

    /// <summary>The game data folder (Maps, Weapons, Parts...), with a trailing separator like <see cref="AppContext.BaseDirectory"/>.</summary>
    public static string Data => _data ??= DataFor(Exe, Directory.Exists);

    /// <summary>Where callofslop.log, selftest.log and smoke screenshots go.</summary>
    public static string Logs => _logs ??= LogsFor(Exe, Home());

    /// <summary>The <c>.app</c> bundle <paramref name="exeFolder"/> is the Contents/MacOS folder of, or null.</summary>
    public static string? BundleOf(string exeFolder)
    {
        var macOs = Path.TrimEndingDirectorySeparator(exeFolder);
        if (!Path.GetFileName(macOs).Equals("MacOS", StringComparison.Ordinal))
            return null;
        var contents = Path.GetDirectoryName(macOs);
        if (contents == null || !Path.GetFileName(contents).Equals("Contents", StringComparison.Ordinal))
            return null;
        var app = Path.GetDirectoryName(contents);
        return app != null && app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? app : null;
    }

    /// <summary>The data folder for a program folder (pure: the self-test passes made-up folders).</summary>
    public static string DataFor(string exeFolder, Func<string, bool> dirExists)
    {
        if (BundleOf(exeFolder) is { } app)
        {
            var resources = Path.Combine(app, "Contents", "Resources");
            if (dirExists(Path.Combine(resources, "Maps")))
                return resources + Path.DirectorySeparatorChar;
        }
        return exeFolder;
    }

    /// <summary>The log folder for a program folder: ~/Library/Logs/CallOfSlop inside an app bundle, else the program folder.</summary>
    public static string LogsFor(string exeFolder, string home)
    {
        if (BundleOf(exeFolder) != null && home.Length > 0)
            return Path.Combine(home, "Library", "Logs", SavePaths.AppFolderName) + Path.DirectorySeparatorChar;
        return exeFolder;
    }

    /// <summary>A log file's full path (its folder is created on demand).</summary>
    public static string LogPath(string name)
    {
        var dir = Logs;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The write that follows reports it (or the log is skipped).
        }
        return Path.Combine(dir, name);
    }

    internal static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
