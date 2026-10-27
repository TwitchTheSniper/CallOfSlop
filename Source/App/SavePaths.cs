using System.Diagnostics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Where the player's own files live (progress, loadouts, settings, workbench builds, audio mixes), so they carry over
/// from one build to the next. Game data (Maps, Weapons, Props, classes.json, hats.json...) stays next to the game.
/// <list type="bullet">
/// <item>Normally: <c>%APPDATA%\CallOfSlop\</c> on Windows, <c>~/Library/Application Support/CallOfSlop/</c> on macOS,
/// <c>~/.local/share/CallOfSlop/</c> on Linux (CI and dev).</item>
/// <item><c>portable.txt</c> next to the game: saves stay next to the game (USB sticks, dev copies).</item>
/// <item><c>--save-dir &lt;path&gt;</c> on the command line: saves go there (wins over both).</item>
/// </list>
/// On startup, a save file that isn't in the save folder yet is COPIED from next to the game (older builds kept them
/// there), or, on the very first run, from the most recent other Call of Slop build beside this one. Originals are never
/// moved or deleted. Writes are atomic (temp file + replace) and keep the previous save as <c>NAME.bak</c>; a damaged save
/// loads from its backup. A save written by a newer build (a higher <c>version=</c> line) is never overwritten.
/// </summary>
public static class SavePaths
{
    public const string AppFolderName = "CallOfSlop";

    /// <summary>A file with this name next to the game keeps the saves next to the game.</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>Command-line flag: <c>--save-dir &lt;path&gt;</c> (or <c>--save-dir=&lt;path&gt;</c>).</summary>
    public const string SaveDirFlag = "--save-dir";

    /// <summary>The player's own files (copied over by the migration). Everything else next to the game is game data.</summary>
    public static readonly string[] SaveFiles = ["progress.txt", "loadout.txt", "settings.txt", "workbench.txt", "audio_mix.json", AppearanceStore.FileName];

    // Also in the save folder but not save files (never migrated; safe to delete): update_check.txt (UpdateCache.cs) and
    // update.log (what the updater's cleanup removed, UpdateCleanup.cs), the Cache folder (cached nav graphs, BotNavBake.cs) and the Logs folder (stuck-spot reports, StuckSpots.cs).
    // The package script still strips them.

    /// <summary>The player's own folders (copied over file by file).</summary>
    public static readonly string[] SaveFolders = ["AudioMixes"];

    /// <summary>The game's own folder (game data, logs; and the saves in portable mode).</summary>
    public static string ExeFolder => AppContext.BaseDirectory;

    private static string? _folder;

    /// <summary>The save folder. Before <see cref="Init"/> it's the platform default (or portable / exe folder).</summary>
    public static string Folder => _folder ??= Resolve([], ExeFolder, OperatingSystem.IsWindows(), RoamingData(), LocalData()).Folder;

    /// <summary>How <see cref="Folder"/> was chosen: "user", "portable", "save-dir", "fallback", "smoke sandbox", "self-test".</summary>
    public static string Mode { get; private set; } = "user";

    /// <summary>The full path of a save file (the folder is created on demand).</summary>
    public static string PathOf(string name)
    {
        var dir = Folder;
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported by the write that follows.
        }
        return Path.Combine(dir, name);
    }

    private static string RoamingData() => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    // Linux: .NET maps LocalApplicationData to $XDG_DATA_HOME or ~/.local/share. macOS: ~/Library/Application Support,
    // spelled out here rather than left to the runtime's mapping (which has changed between .NET versions).
    private static string LocalData() => OperatingSystem.IsMacOS() ? MacAppSupport(AppFolders.Home())
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>~/Library/Application Support for a home folder (empty when there's no home: the exe-folder fallback).</summary>
    public static string MacAppSupport(string home) => string.IsNullOrWhiteSpace(home) ? "" : Path.Combine(home, "Library", "Application Support");

    // ---- Choosing the folder -----------------------------------------------------------

    /// <summary>The value of <c>--save-dir</c>, or null.</summary>
    public static string? SaveDirArg(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.Equals(SaveDirFlag, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Count && args[i + 1].Trim().Length > 0 ? args[i + 1].Trim() : null;
            if (a.StartsWith(SaveDirFlag + "=", StringComparison.OrdinalIgnoreCase) && a.Length > SaveDirFlag.Length + 1)
                return a[(SaveDirFlag.Length + 1)..].Trim().Trim('"');
        }
        return null;
    }

    /// <summary>The command line without <c>--save-dir</c> and its value (so the path isn't mistaken for "host"/"join" words).</summary>
    public static string[] WithoutSaveDir(IReadOnlyList<string> args)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i].Equals(SaveDirFlag, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }
            if (!args[i].StartsWith(SaveDirFlag + "=", StringComparison.OrdinalIgnoreCase))
                list.Add(args[i]);
        }
        return list.ToArray();
    }

    /// <summary>Picks the save folder (pure apart from looking for portable.txt; the self-test calls it with made-up folders).</summary>
    public static (string Folder, string Mode) Resolve(IReadOnlyList<string> args, string exeFolder, bool windows, string roamingData, string localData)
    {
        if (SaveDirArg(args) is { } custom)
            return (Full(custom), "save-dir");
        if (File.Exists(Path.Combine(exeFolder, PortableMarker)))
            return (Full(exeFolder), "portable");
        var root = windows ? roamingData : localData;
        if (string.IsNullOrWhiteSpace(root))
            return (Full(exeFolder), "fallback"); // no user profile folder: behave like the old builds
        return (Full(Path.Combine(root, AppFolderName)), "user");
    }

    private static string Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    /// <summary>Do two paths name the same folder?</summary>
    public static bool SameFolder(string a, string b) =>
        string.Equals(Full(a), Full(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Startup: picks the folder, creates it and copies older saves into it (from next to the game, or on the first run
    /// from the newest other build beside this one). Never moves or deletes anything.
    /// </summary>
    public static void Init(string[] args)
    {
        var (folder, mode) = Resolve(args, ExeFolder, OperatingSystem.IsWindows(), RoamingData(), LocalData());
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.WriteLine($"[saves] can't use {folder} ({ex.Message}); saving next to the game instead");
            (folder, mode) = (Full(ExeFolder), "fallback");
        }
        (_folder, Mode) = (folder, mode);
        Console.WriteLine($"[saves] save folder: {folder} ({mode})");
        if (SameFolder(folder, ExeFolder))
            return;
        Migrate(ExeFolder, folder, Console.WriteLine);
        if (!File.Exists(Path.Combine(folder, "progress.txt")) && FindPreviousBuild(ExeFolder) is { } previous)
        {
            Console.WriteLine($"[saves] first run: importing saves from the previous build in {previous}");
            Migrate(previous, folder, Console.WriteLine);
        }
    }

    /// <summary>Uses <paramref name="folder"/> for every save from now on (the self-test's temp folder, the smoke sandbox).</summary>
    public static void UseFolder(string folder, string mode)
    {
        Directory.CreateDirectory(folder);
        (_folder, Mode) = (Full(folder), mode);
    }

    /// <summary>
    /// Smoke test: copies the real saves (read only; nothing is migrated or written) into a temp folder and saves there,
    /// so the run sees your progress but can never change it.
    /// </summary>
    public static void UseSmokeSandbox(string[] args)
    {
        var (real, _) = Resolve(args, ExeFolder, OperatingSystem.IsWindows(), RoamingData(), LocalData());
        var sandbox = Path.Combine(Path.GetTempPath(), "CallOfSlop-smoke-saves");
        try
        {
            Directory.CreateDirectory(sandbox);
            foreach (var name in SaveFiles)
            {
                var src = File.Exists(Path.Combine(real, name)) ? Path.Combine(real, name) : Path.Combine(ExeFolder, name);
                var dst = Path.Combine(sandbox, name);
                if (File.Exists(src))
                    File.Copy(src, dst, overwrite: true); // our own temp copy, refreshed each run
                else if (File.Exists(dst))
                    File.WriteAllText(dst, ""); // stale copy from an earlier run: blank it (an empty save loads as defaults)
            }
            UseFolder(sandbox, "smoke sandbox");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            UseFolder(Path.Combine(Path.GetTempPath(), $"CallOfSlop-smoke-{Guid.NewGuid():N}"), "smoke sandbox (empty)");
            Console.WriteLine($"[saves] smoke sandbox copy failed ({ex.Message}); starting from empty saves");
        }
        Console.WriteLine($"[saves] smoke test: using a throwaway copy of your saves in {Folder}");
    }

    // ---- Migration ---------------------------------------------------------------------

    /// <summary>
    /// Copies each save file (and each file in the save folders) from <paramref name="from"/> into <paramref name="to"/>
    /// if <paramref name="to"/> doesn't have it yet. When both have it, the one in <paramref name="to"/> wins and the other
    /// is left alone. Never moves or deletes. Returns the names copied.
    /// </summary>
    public static List<string> Migrate(string from, string to, Action<string> log)
    {
        var copied = new List<string>();
        if (SameFolder(from, to))
            return copied;
        var fromLabel = Full(from);
        foreach (var name in SaveFiles)
        {
            var src = Path.Combine(from, name);
            var dst = Path.Combine(to, name);
            try
            {
                if (!File.Exists(src))
                    continue;
                if (File.Exists(dst))
                {
                    var newer = File.GetLastWriteTimeUtc(src) > File.GetLastWriteTimeUtc(dst);
                    log($"[saves] kept {name} in the save folder; the {(newer ? "newer" : "older")} copy in {fromLabel} was ignored (left untouched)");
                    continue;
                }
                Directory.CreateDirectory(to);
                File.Copy(src, dst, overwrite: false);
                copied.Add(name);
                log($"[saves] migrated {name} from {fromLabel}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log($"[saves] couldn't copy {name} from {fromLabel}: {ex.Message}");
            }
        }
        foreach (var folder in SaveFolders)
        {
            var srcDir = Path.Combine(from, folder);
            if (!Directory.Exists(srcDir))
                continue;
            int n = 0, kept = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(from, file);
                    var dst = Path.Combine(to, rel);
                    if (File.Exists(dst))
                    {
                        kept++;
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    File.Copy(file, dst, overwrite: false);
                    copied.Add(rel.Replace('\\', '/'));
                    n++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log($"[saves] couldn't copy {folder} from {fromLabel}: {ex.Message}");
            }
            if (n > 0)
                log($"[saves] migrated {folder}/ ({n} file(s)) from {fromLabel}");
            if (kept > 0)
                log($"[saves] kept {kept} file(s) in {folder}/ in the save folder; the copies in {fromLabel} were ignored");
        }
        return copied;
    }

    /// <summary>
    /// Another Call of Slop build next to this one (a sibling folder, or a sibling of the parent: tester zips unzip to
    /// CallOfSlop-date-hash\CallOfSlop\) that has a progress.txt; the one saved most recently. Null if none.
    /// </summary>
    public static string? FindPreviousBuild(string exeFolder)
    {
        var self = Full(exeFolder);
        var parent = Path.GetDirectoryName(self);
        if (parent == null)
            return null;
        var roots = new List<string> { parent };
        if (Path.GetDirectoryName(parent) is { } grand)
            roots.Add(grand);
        string? best = null;
        var bestTime = DateTime.MinValue;
        var looked = 0;
        void Consider(string dir)
        {
            looked++;
            if (SameFolder(dir, self))
                return;
            var progress = Path.Combine(dir, "progress.txt");
            if (!File.Exists(progress) || !(File.Exists(Path.Combine(dir, "CallOfSlop.dll")) || File.Exists(Path.Combine(dir, "CallOfSlop.exe"))))
                return;
            var t = File.GetLastWriteTimeUtc(progress);
            if (t > bestTime)
                (best, bestTime) = (dir, t);
        }
        try
        {
            foreach (var d in Directory.EnumerateDirectories(parent).Take(200))
                Consider(d);
            if (roots.Count > 1)
                foreach (var d in Directory.EnumerateDirectories(roots[1]).Take(200))
                {
                    if (SameFolder(d, parent))
                        continue;
                    Consider(d);
                    foreach (var dd in SafeDirs(d).Take(8))
                        if (looked < 1200)
                            Consider(dd);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unlistable folder: no import.
        }
        return best;
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return [];
        }
    }

    // ---- Reading and writing -----------------------------------------------------------

    /// <summary>The previous save, kept by every write.</summary>
    public static string BackupOf(string path) => path + ".bak";

    /// <summary>The <c>version=N</c> line (0 when there is none: files from before this line existed).</summary>
    public static int FormatOf(IEnumerable<string> lines)
    {
        foreach (var l in lines)
            if (IsVersionLine(l) && int.TryParse(l.Split('=', 2)[1].Trim(), out var v))
                return v;
        return 0;
    }

    private static bool IsVersionLine(string l) => l.StartsWith("version=", StringComparison.Ordinal) || l.StartsWith("version =", StringComparison.Ordinal);

    /// <summary>A save that's plain text with at least one key=value line (a crash mid-write leaves an empty or zero-filled file).</summary>
    public static bool LooksValid(string[]? lines) =>
        lines is { Length: > 0 } && !lines.Any(l => l.Contains('\0')) && lines.Any(l => l.IndexOf('=') > 0);

    private static string[]? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[saves] couldn't read {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads a save's lines. A damaged file is copied aside as NAME.corrupt and its backup is loaded instead. Null when
    /// there's neither a usable file nor a usable backup. A file from a newer build still loads (as well as this build
    /// can) and is marked so it isn't saved over.
    /// </summary>
    public static string[]? ReadLines(string path, int version)
    {
        var name = Path.GetFileName(path);
        var lines = TryRead(path);
        if (lines != null && !LooksValid(lines))
        {
            Console.WriteLine($"[saves] {name} is damaged; trying its backup");
            try
            {
                File.Copy(path, path + ".corrupt", overwrite: true); // kept for a bug report
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not important.
            }
            lines = null;
        }
        if (lines == null)
        {
            var bak = TryRead(BackupOf(path));
            if (LooksValid(bak))
            {
                Console.WriteLine($"[saves] loaded {name} from its backup ({Path.GetFileName(BackupOf(path))})");
                lines = bak;
            }
        }
        if (lines != null && FormatOf(lines) > version)
            Warn($"{name} was saved by a newer build of Call of Slop (format {FormatOf(lines)}, this build knows {version}). This build won't save over it: play the newer build to keep your progress.");
        return lines;
    }

    /// <summary>
    /// Saves lines atomically with a <c>version=N</c> line first, keeping the previous save as NAME.bak. Refuses (and
    /// warns once) if the file on disk was written by a newer build. False if nothing was written.
    /// </summary>
    public static bool WriteLines(string path, IEnumerable<string> lines, int version)
    {
        var name = Path.GetFileName(path);
        if (TryRead(path) is { } onDisk && LooksValid(onDisk) && FormatOf(onDisk) > version)
        {
            Warn($"{name} was saved by a newer build of Call of Slop (format {FormatOf(onDisk)}, this build knows {version}). This build won't save over it: play the newer build to keep your progress.");
            return false;
        }
        var sb = new StringBuilder();
        sb.Append("version=").Append(version).Append(Environment.NewLine);
        foreach (var l in lines)
            if (!IsVersionLine(l))
                sb.Append(l).Append(Environment.NewLine);
        return WriteAtomic(path, sb.ToString());
    }

    /// <summary>
    /// Writes a whole file atomically: to NAME.tmp first (flushed to disk), then swapped in, with the previous file
    /// kept as NAME.bak (a damaged previous file doesn't replace a good backup). A crash leaves either the old or the new
    /// save, never half of one. Other save code (e.g. audio mixes) can use this directly.
    /// </summary>
    public static bool WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(contents);
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return true;
            }
            var keepBackup = LooksValid(TryRead(path)) || !File.Exists(BackupOf(path));
            if (keepBackup)
            {
                try
                {
                    File.Replace(tmp, path, BackupOf(path), ignoreMetadataErrors: true);
                    return true;
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
                {
                    // Some drives (FAT USB sticks, network shares) can't replace: copy the backup, then move.
                    File.Copy(path, BackupOf(path), overwrite: true);
                }
            }
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.WriteLine($"[saves] couldn't save {Path.GetFileName(path)} to {Path.GetDirectoryName(path)}: {ex.Message}");
            return false;
        }
    }

    // ---- Warnings and the folder button -------------------------------------------------

    private static bool _warningShown;

    /// <summary>A save problem to show the player (newer-format save), or null. Shown in Settings > Player.</summary>
    public static string? Warning { get; private set; }

    private static readonly HashSet<string> Warned = new();

    private static void Warn(string message)
    {
        if (Warned.Add(message))
            Console.WriteLine($"[saves] WARNING: {message}");
        Warning ??= message;
    }

    /// <summary>The warning, once (for the lobby message line); null after the first call or when there's none.</summary>
    public static string? TakeWarningOnce()
    {
        if (_warningShown || Warning == null)
            return null;
        _warningShown = true;
        return Warning;
    }

    /// <summary>Self-test only: forgets warnings so a check doesn't leak into the next.</summary>
    internal static void ClearWarnings()
    {
        Warning = null;
        _warningShown = false;
        Warned.Clear();
    }

    /// <summary>Opens the save folder in Explorer (Finder on a Mac, or the desktop's file manager).</summary>
    public static void OpenFolder()
    {
        try
        {
            var dir = Folder;
            Directory.CreateDirectory(dir);
            var psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", dir) { UseShellExecute = false };
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[saves] couldn't open the save folder: {ex.Message}");
        }
    }
}
