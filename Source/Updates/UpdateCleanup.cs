using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CallOfSlop;

/// <summary>What a cleanup item is.</summary>
public enum CleanupKind
{
    OldBuild, // an older build folder the updater installed
    UnfinishedRemoval, // a "CallOfSlop-<version>.deleting" folder an earlier launch didn't finish removing
    PartialInstall, // a "CallOfSlop-<version>.partial" folder from an interrupted unzip
    Download, // a "CallOfSlop-<version>.zip.download" for this version or an older one
    TempFile, // a leftover write-probe file
}

public sealed record CleanupItem(string Path, CleanupKind Kind, long Bytes, string Why);

public sealed record CleanupKept(string Path, string Why);

/// <summary>What a cleanup would remove (and what it keeps, with the reason), all inside <see cref="Root"/>.</summary>
public sealed class CleanupPlan
{
    public string Root = "";
    public string CurrentBuild = "";
    public readonly List<CleanupItem> Remove = new();
    public readonly List<CleanupKept> Kept = new();
    public long Bytes => Remove.Sum(r => r.Bytes);
}

public sealed record CleanupResult(int Removed, long Bytes, List<string> Failed, List<string> Skipped);

public sealed class CleanupOptions
{
    /// <summary>Keep the newest older build (one that reached its menu, if there is one) as a rollback copy.</summary>
    public bool KeepPrevious = true;

    /// <summary>Only finish removals an earlier launch started (".deleting" folders); nothing new is chosen.</summary>
    public bool ResumeOnly;

    public DateTime NowUtc = DateTime.UtcNow;

    /// <summary>A ".partial" unzip folder younger than this may belong to an install running right now: kept.</summary>
    public TimeSpan PartialAge = TimeSpan.FromHours(1);

    /// <summary>The save folder in use: a folder holding it is never removed.</summary>
    public string? SaveFolder;

    /// <summary>Is a Call of Slop from this folder running? (The self-test swaps in a stand-in.)</summary>
    public Func<string, bool> InUse = UpdateCleanup.RunningFrom;

    /// <summary>The update log in the save folder (null: console only).</summary>
    public string? LogPath;

    public Action<string> Log = Console.WriteLine;

    /// <summary>Self-test: throw after this many deleted files (a crash in the middle of a removal). -1: off.</summary>
    internal int FailAfterFiles = -1;
}

/// <summary>
/// Removes what the updater left behind and no longer needs, once the new build has shown it works (it reached its main
/// menu): older build folders the updater installed (keeping this one and, by default, one previous version for
/// rollback), downloads of versions already installed, interrupted unzips and leftover temp files.
/// <para>
/// Safety rules (all checked again right before anything is deleted):
/// <list type="bullet">
/// <item>Only direct children of the install root (UpdateInstaller.InstallRoot), never a link or junction, never anything
/// outside it; inside a removed folder, links are removed as links and never followed.</item>
/// <item>A build folder is removed only when it is called <c>CallOfSlop-&lt;version&gt;[-n]</c>, holds the updater's
/// <see cref="BuildMarker"/> naming that same folder and version, and has CallOfSlop.exe. Folders you unzipped yourself,
/// dev copies and builds installed by updaters older than the marker are never touched (the dialog lists them).</item>
/// <item>Never the running build, never one newer than it, never one with saves in it (portable.txt, any
/// SavePaths.SaveFiles / SaveFolders entry next to its exe, or the current save folder inside it), never one a running Call
/// of Slop was started from.</item>
/// <item>A folder is first renamed to <c>CallOfSlop-&lt;version&gt;.deleting</c> (Windows refuses while any file in it is
/// open, so a running copy is never half deleted), then emptied with its marker removed last. A removal that stops
/// half way (a crash, a locked file) still has its marker and is finished on the next launch.</item>
/// <item>Downloads are removed only for versions up to this one (a paused download of a newer release is kept for Install
/// to resume); unzip folders only when over an hour old.</item>
/// </list>
/// Every removal is logged to the console and to <c>update.log</c> in the save folder.
/// </para>
/// </summary>
public static class UpdateCleanup
{
    public const string DeletingSuffix = ".deleting";
    public const string PartialSuffix = ".partial";
    public const string LogFileName = "update.log";

    private static readonly Regex FolderName = new(@"^CallOfSlop-(\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,6})(-\d{1,3})?(\.partial|\.deleting)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DownloadName = new(@"^CallOfSlop-(\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,6})\.zip\.download$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ProbeName = new(@"^callofslop-write-probe-(\d{1,10})\.tmp$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static StringComparison PathCmp => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>"CallOfSlop-0.2026.1022.5-2.deleting" -> (0.2026.1022.5, "CallOfSlop-0.2026.1022.5-2", ".deleting"). Null when it isn't a build folder name.</summary>
    public static (Version Version, string Base, string Suffix)? ParseFolderName(string name)
    {
        var m = FolderName.Match(name);
        if (!m.Success || BuildInfo.ParseRelease(m.Groups[1].Value) is not { } v)
            return null;
        var suffix = m.Groups[3].Value.ToLowerInvariant();
        return (v, name[..(name.Length - suffix.Length)], suffix);
    }

    /// <summary>The build folder this exe lives in: the install root's child that holds it.</summary>
    public static string CurrentBuildFolder(string exeFolder)
    {
        var self = Path.TrimEndingDirectorySeparator(Path.GetFullPath(UpdateInstaller.GameFolder(exeFolder))); // a Mac bundle: the folder holding the .app
        var root = UpdateInstaller.InstallRoot(self);
        return Path.GetDirectoryName(self) is { } parent && !SavePaths.SameFolder(parent, root) ? parent : self;
    }

    private static bool Inside(string path, string folder)
    {
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, PathCmp);
    }

    private static bool IsLink(FileSystemInfo fi) =>
        fi.LinkTarget != null || (fi.Exists && fi.Attributes.HasFlag(FileAttributes.ReparsePoint));

    /// <summary>Is a Call of Slop process running from inside <paramref name="folder"/>? (Processes it may not look at are skipped: the rename still refuses a folder in use.)</summary>
    public static bool RunningFrom(string folder)
    {
        Process[] procs;
        try
        {
            procs = Process.GetProcessesByName("CallOfSlop");
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
        var found = false;
        foreach (var p in procs)
        {
            try
            {
                if (!found && p.Id != Environment.ProcessId && p.MainModule?.FileName is { } exe && Inside(exe, folder))
                    found = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Exited, or not ours to look at.
            }
            finally
            {
                p.Dispose();
            }
        }
        return found;
    }

    /// <summary>Why a folder must be kept because of saves, or null. Looks at the folder and its direct subfolders (the exe's folder).</summary>
    public static string? SavesIn(string folder, string? saveFolder)
    {
        if (saveFolder != null && Inside(saveFolder, folder))
            return "your save folder is inside it";
        var dirs = new List<string> { folder };
        try
        {
            dirs.AddRange(Directory.EnumerateDirectories(folder).Take(64));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "it can't be listed";
        }
        foreach (var d in dirs)
        {
            if (File.Exists(Path.Combine(d, SavePaths.PortableMarker)))
                return "a portable build: its saves are kept next to its exe (move them yourself, then delete it)";
            foreach (var name in SavePaths.SaveFiles)
                if (File.Exists(Path.Combine(d, name)) || File.Exists(SavePaths.BackupOf(Path.Combine(d, name))))
                    return $"it has save files in it ({name})";
            foreach (var name in SavePaths.SaveFolders)
                if (Directory.Exists(Path.Combine(d, name)))
                    return $"it has save files in it ({name}/)";
        }
        return null;
    }

    /// <summary>Bytes in a folder (links not followed). Best effort.</summary>
    public static long SizeOf(string folder)
    {
        long total = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(folder));
        var seen = 0;
        while (stack.Count > 0 && seen < 200_000)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var e in dir.EnumerateFileSystemInfos())
                {
                    seen++;
                    if (IsLink(e))
                        continue;
                    if (e is FileInfo f)
                        total += f.Length;
                    else if (e is DirectoryInfo d)
                        stack.Push(d);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Count what we can.
            }
        }
        return total;
    }

    private static bool IsEmpty(string dir)
    {
        try
        {
            return !Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DateTime NewestWrite(string dir)
    {
        var t = Directory.GetLastWriteTimeUtc(dir);
        try
        {
            foreach (var e in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Take(5000))
            {
                var w = File.GetLastWriteTimeUtc(e);
                if (w > t)
                    t = w;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MaxValue; // can't tell: treat as fresh
        }
        return t;
    }

    private static bool ProcessAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return ex is not ArgumentException; // ArgumentException: no such process. Anything else: assume alive.
        }
    }

    /// <summary>
    /// Works out what may be removed next to the build in <paramref name="exeFolder"/> (version <paramref name="current"/>;
    /// null for a dev build, which removes nothing but unfinished removals). Deletes nothing.
    /// </summary>
    public static CleanupPlan Plan(string exeFolder, Version? current, CleanupOptions o)
    {
        var plan = new CleanupPlan
        {
            Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(UpdateInstaller.InstallRoot(exeFolder))),
            CurrentBuild = CurrentBuildFolder(exeFolder),
        };
        if (!Directory.Exists(plan.Root))
            return plan;
        List<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(plan.Root).EnumerateFileSystemInfos().Take(2000).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            plan.Kept.Add(new CleanupKept(plan.Root, $"the folder can't be listed ({ex.Message})"));
            return plan;
        }
        var builds = new List<(string Path, BuildMarker.Info Marker)>();
        var resumeOnly = o.ResumeOnly || current == null; // a dev build only finishes removals already started
        if (current == null && !o.ResumeOnly)
            plan.Kept.Add(new CleanupKept(plan.Root, "this is a dev build: only release builds remove old versions"));
        foreach (var e in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            var path = e.FullName;
            if (!SavePaths.SameFolder(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? "", plan.Root))
                continue; // never anything but a direct child of the root
            if (IsLink(e))
            {
                if (e.Name.StartsWith("CallOfSlop", StringComparison.OrdinalIgnoreCase))
                    plan.Kept.Add(new CleanupKept(path, "a link, not a real build folder or file"));
                continue;
            }
            if (e is FileInfo file)
            {
                if (!resumeOnly)
                    PlanFile(plan, file, current!, o);
                continue;
            }
            var parsed = ParseFolderName(e.Name);
            if (parsed == null)
            {
                if (e.Name.StartsWith("CallOfSlop", StringComparison.OrdinalIgnoreCase) && UpdateInstaller.FindGame(path) != null)
                    plan.Kept.Add(new CleanupKept(path, SavePaths.SameFolder(path, plan.CurrentBuild) ? "the build you're running" : "not installed by the updater (unzipped by hand)"));
                continue;
            }
            if (SavePaths.SameFolder(path, plan.CurrentBuild))
            {
                plan.Kept.Add(new CleanupKept(path, "the build you're running"));
                continue;
            }
            var (version, baseName, suffix) = parsed.Value;
            var marker = BuildMarker.Read(path);
            var proven = marker != null && marker.Version == version && marker.Folder.Equals(baseName, StringComparison.OrdinalIgnoreCase);
            if (suffix == DeletingSuffix)
            {
                if (proven && SavesIn(path, o.SaveFolder) is { } why)
                    plan.Kept.Add(new CleanupKept(path, why));
                else if (proven || (marker == null && IsEmpty(path)))
                    plan.Remove.Add(new CleanupItem(path, CleanupKind.UnfinishedRemoval, SizeOf(path), "an earlier removal that didn't finish"));
                else
                    plan.Kept.Add(new CleanupKept(path, "an unfinished removal without the updater's marker: delete it yourself if you don't need it"));
                continue;
            }
            if (resumeOnly)
                continue;
            if (marker == null)
            {
                plan.Kept.Add(new CleanupKept(path, "not marked as installed by the updater (unzipped by hand, or by an older updater): delete it yourself if you don't need it"));
                continue;
            }
            if (!proven)
            {
                plan.Kept.Add(new CleanupKept(path, "its updater marker names another folder (renamed or copied)"));
                continue;
            }
            if (suffix == PartialSuffix)
            {
                if (o.NowUtc - NewestWrite(path) >= o.PartialAge)
                    plan.Remove.Add(new CleanupItem(path, CleanupKind.PartialInstall, SizeOf(path), "an unzip that was interrupted"));
                else
                    plan.Kept.Add(new CleanupKept(path, "an unzip in progress (or under an hour old)"));
                continue;
            }
            if (marker.Partial)
            {
                plan.Kept.Add(new CleanupKept(path, "its marker says the unzip didn't finish"));
                continue;
            }
            if (UpdateInstaller.FindGame(path) == null)
            {
                plan.Kept.Add(new CleanupKept(path, "it has no CallOfSlop.exe"));
                continue;
            }
            if (version > current!)
            {
                plan.Kept.Add(new CleanupKept(path, $"newer than this build (v{current})"));
                continue;
            }
            if (SavesIn(path, o.SaveFolder) is { } saves)
            {
                plan.Kept.Add(new CleanupKept(path, saves));
                continue;
            }
            if (o.InUse(path))
            {
                plan.Kept.Add(new CleanupKept(path, "a Call of Slop started from it is running (tried again next launch)"));
                continue;
            }
            builds.Add((path, marker));
        }
        if (current != null && builds.Count > 0)
        {
            (string Path, BuildMarker.Info Marker)? keep = null;
            if (o.KeepPrevious)
            {
                var older = builds.Where(b => b.Marker.Version < current).OrderByDescending(b => b.Marker.Version).ThenByDescending(b => b.Marker.InstalledUtc).ToList();
                keep = older.Where(b => b.Marker.ConfirmedUtc != null).Select(b => ((string, BuildMarker.Info)?)b).FirstOrDefault()
                    ?? older.Select(b => ((string, BuildMarker.Info)?)b).FirstOrDefault();
            }
            foreach (var b in builds)
            {
                if (keep is { } k && b.Path == k.Path)
                    plan.Kept.Add(new CleanupKept(b.Path, $"the previous version (v{b.Marker.Version}), kept for rollback"));
                else
                    plan.Remove.Add(new CleanupItem(b.Path, CleanupKind.OldBuild, SizeOf(b.Path),
                        b.Marker.Version == current ? $"another copy of this version (v{current})" : $"an old version (v{b.Marker.Version})"));
            }
        }
        return plan;
    }

    private static void PlanFile(CleanupPlan plan, FileInfo file, Version current, CleanupOptions o)
    {
        if (DownloadName.Match(file.Name) is { Success: true } dm && BuildInfo.ParseRelease(dm.Groups[1].Value) is { } v)
        {
            if (v <= current)
                plan.Remove.Add(new CleanupItem(file.FullName, CleanupKind.Download, file.Length, $"a download of v{v} (this build is v{current})"));
            else
                plan.Kept.Add(new CleanupKept(file.FullName, $"a paused download of v{v}: Install continues it"));
            return;
        }
        if (ProbeName.Match(file.Name) is { Success: true } pm && file.Length == 0 && int.TryParse(pm.Groups[1].Value, out var pid)
            && pid != Environment.ProcessId && !ProcessAlive(pid))
            plan.Remove.Add(new CleanupItem(file.FullName, CleanupKind.TempFile, 0, "a temp file from an interrupted install"));
    }

    /// <summary>
    /// Removes the items of <paramref name="plan"/> that a fresh plan (made now, with the same rules) still lists, so a
    /// folder that gained saves or started running since the plan was shown is left alone. Logs every removal.
    /// </summary>
    public static CleanupResult Execute(CleanupPlan plan, string exeFolder, Version? current, CleanupOptions o)
    {
        var fresh = Plan(exeFolder, current, o);
        var still = fresh.Remove.ToDictionary(r => r.Path, r => r, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var failed = new List<string>();
        var skipped = new List<string>();
        int removed = 0;
        long bytes = 0;
        var budget = new Budget(o.FailAfterFiles);
        foreach (var item in plan.Remove)
        {
            if (!still.TryGetValue(item.Path, out var now) || now.Kind != item.Kind)
            {
                var why = fresh.Kept.FirstOrDefault(k => SavePaths.SameFolder(k.Path, item.Path))?.Why ?? "it's gone or changed";
                skipped.Add(item.Path);
                Note(o, $"kept {item.Path}: no longer safe to remove ({why})");
                continue;
            }
            try
            {
                switch (item.Kind)
                {
                    case CleanupKind.Download or CleanupKind.TempFile:
                        File.Delete(item.Path); // checked above: our own download / probe file
                        break;
                    case CleanupKind.UnfinishedRemoval:
                        DeleteTree(item.Path, budget);
                        break;
                    default:
                        var parsed = ParseFolderName(Path.GetFileName(item.Path))!.Value;
                        var target = Path.Combine(Path.GetDirectoryName(item.Path)!, parsed.Base + DeletingSuffix);
                        if (Directory.Exists(target) || File.Exists(target))
                            throw new IOException($"{Path.GetFileName(target)} is in the way");
                        try
                        {
                            Directory.Move(item.Path, target); // Windows refuses while a file inside is open: then nothing is deleted
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            throw new IOException($"it's in use ({ex.Message.Trim()}); tried again next launch", ex);
                        }
                        DeleteTree(target, budget);
                        break;
                }
                removed++;
                bytes += now.Bytes;
                Note(o, $"removed {Describe(item.Kind)} {item.Path} ({Mb(now.Bytes)}): {item.Why}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(item.Path);
                Note(o, $"couldn't remove {item.Path}: {ex.Message} (finished on a later launch)");
            }
        }
        return new CleanupResult(removed, bytes, failed, skipped);
    }

    /// <summary>Self-test fault injection: how many more files may be deleted (-1: no limit).</summary>
    private sealed class Budget(int left)
    {
        public void Spend()
        {
            if (left == 0)
                throw new IOException("stopped part way (self-test)");
            if (left > 0)
                left--;
        }
    }

    /// <summary>Empties and removes a ".deleting" build folder: links are removed, never followed; the marker goes last.</summary>
    private static void DeleteTree(string top, Budget budget)
    {
        static void Remove(FileSystemInfo e, Budget budget)
        {
            if (IsLink(e))
            {
                if (e is DirectoryInfo && OperatingSystem.IsWindows())
                    Directory.Delete(e.FullName, recursive: false); // a junction or directory link: the link only
                else
                    File.Delete(e.FullName); // the link only (never its target)
                return;
            }
            if (e is DirectoryInfo d)
            {
                foreach (var c in d.EnumerateFileSystemInfos().ToList())
                    Remove(c, budget);
                Directory.Delete(d.FullName, recursive: false);
                return;
            }
            budget.Spend();
            if (e.Attributes.HasFlag(FileAttributes.ReadOnly))
                e.Attributes &= ~FileAttributes.ReadOnly;
            File.Delete(e.FullName);
        }
        var root = new DirectoryInfo(top);
        foreach (var c in root.EnumerateFileSystemInfos().ToList())
            if (!c.Name.Equals(BuildMarker.FileName, StringComparison.OrdinalIgnoreCase))
                Remove(c, budget);
        var marker = Path.Combine(top, BuildMarker.FileName);
        if (File.Exists(marker))
        {
            budget.Spend();
            File.Delete(marker); // the proof goes last
        }
        Directory.Delete(top, recursive: false);
    }

    private static string Describe(CleanupKind k) => k switch
    {
        CleanupKind.OldBuild => "old build",
        CleanupKind.UnfinishedRemoval => "the rest of",
        CleanupKind.PartialInstall => "unfinished unzip",
        CleanupKind.Download => "download",
        _ => "temp file",
    };

    public static string Mb(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024):0.#} MB" : $"{Math.Max(0, bytes / 1024)} KB";

    private static void Note(CleanupOptions o, string text)
    {
        o.Log($"[update] {text}");
        if (o.LogPath != null)
            AppendLog(o.LogPath, text, o.NowUtc);
    }

    /// <summary>Appends a line to the update log (kept under about 200 KB: the oldest half goes).</summary>
    public static void AppendLog(string path, string text, DateTime nowUtc)
    {
        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            if (File.Exists(path) && new FileInfo(path).Length > 200_000)
            {
                var lines = File.ReadAllLines(path);
                File.WriteAllLines(path, lines.Skip(lines.Length / 2));
            }
            File.AppendAllText(path, $"{nowUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z {text}\n", new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[update] couldn't write {path}: {ex.Message}");
        }
    }
}

/// <summary>
/// The game's side of the cleanup: on reaching the main menu it marks this build as working and (with "Remove old
/// versions after updating" on) removes old versions in the background a few seconds later; unfinished removals are
/// always finished. "Clean up old versions now" previews a plan and removes it only after the player confirms.
/// </summary>
public sealed class UpdateCleaner
{
    public static UpdateCleaner? Shared;

    /// <summary>How long after reaching the menu the automatic cleanup waits (the old build is still closing).</summary>
    public static TimeSpan AutoDelay = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly string _exeFolder;
    private readonly Version? _current;
    private readonly string _logPath;
    private readonly string? _saveFolder;
    private Task? _task;
    private CleanupPlan? _preview;
    private string _message = "";
    private bool _menuReached;

    public UpdateCleaner(string exeFolder, Version? current, string logPath, string? saveFolder)
    {
        _exeFolder = exeFolder;
        _current = current;
        _logPath = logPath;
        _saveFolder = saveFolder;
    }

    public bool Busy => _task is { IsCompleted: false };

    public (CleanupPlan? Preview, string Message) Snapshot()
    {
        lock (_gate)
            return (_preview, _message);
    }

    private CleanupOptions Options(bool keepPrevious, bool resumeOnly) => new()
    {
        KeepPrevious = keepPrevious,
        ResumeOnly = resumeOnly,
        SaveFolder = _saveFolder,
        LogPath = _logPath,
        NowUtc = DateTime.UtcNow,
    };

    /// <summary>The main menu is on screen (called every frame; acts once).</summary>
    public void MenuReached(Settings s)
    {
        if (_menuReached)
            return;
        _menuReached = true;
        var build = UpdateCleanup.CurrentBuildFolder(_exeFolder);
        var marked = BuildMarker.Confirm(build, DateTime.UtcNow);
        if (s.UpdateSkip.Length > 0 && BuildInfo.ParseRelease(s.UpdateSkip) is { } skip && _current != null && skip <= _current)
        {
            Console.WriteLine($"[update] forgot the skipped version v{skip} (this build is v{_current})");
            s.UpdateSkip = "";
            s.Save();
        }
        var full = marked && _current != null && s.UpdateCleanup;
        var keep = s.UpdateKeepPrevious;
        if (Busy)
            return;
        _task = Task.Run(async () =>
        {
            await Task.Delay(AutoDelay).ConfigureAwait(false);
            try
            {
                var o = Options(keep, resumeOnly: !full);
                var plan = UpdateCleanup.Plan(_exeFolder, _current, o);
                if (plan.Remove.Count == 0)
                    return;
                var r = UpdateCleanup.Execute(plan, _exeFolder, _current, o);
                lock (_gate)
                    _message = r.Removed > 0 ? $"Removed {r.Removed} old item(s), {UpdateCleanup.Mb(r.Bytes)}." : "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[update] cleanup failed: {ex.Message}");
            }
        });
    }

    /// <summary>"Clean up old versions now": works out what would go (nothing is removed yet).</summary>
    public void Preview(bool keepPrevious)
    {
        if (Busy)
            return;
        lock (_gate)
            (_preview, _message) = (null, "Looking for old versions...");
        _task = Task.Run(() =>
        {
            try
            {
                var plan = UpdateCleanup.Plan(_exeFolder, _current, Options(keepPrevious, false));
                lock (_gate)
                    (_preview, _message) = (plan, "");
            }
            catch (Exception ex)
            {
                lock (_gate)
                    _message = $"Couldn't look for old versions: {ex.Message}";
            }
        });
    }

    /// <summary>The player confirmed the preview: remove it (each item is checked again first).</summary>
    public void RemovePreview(bool keepPrevious)
    {
        CleanupPlan? plan;
        lock (_gate)
            plan = _preview;
        if (Busy || plan == null)
            return;
        lock (_gate)
            (_preview, _message) = (null, "Removing old versions...");
        _task = Task.Run(() =>
        {
            try
            {
                var r = UpdateCleanup.Execute(plan, _exeFolder, _current, Options(keepPrevious, false));
                var text = $"Removed {r.Removed} item(s), {UpdateCleanup.Mb(r.Bytes)}.";
                if (r.Failed.Count > 0)
                    text += $" {r.Failed.Count} couldn't be removed yet (in use?); tried again next launch.";
                if (r.Skipped.Count > 0)
                    text += $" {r.Skipped.Count} kept: no longer safe to remove.";
                lock (_gate)
                    _message = text;
            }
            catch (Exception ex)
            {
                lock (_gate)
                    _message = $"Cleanup failed: {ex.Message}";
            }
        });
    }

    public void ClosePreview()
    {
        lock (_gate)
            _preview = null;
    }
}
