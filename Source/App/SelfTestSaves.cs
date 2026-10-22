namespace CallOfSlop;

/// <summary>
/// Self-test for the save folder (SavePaths.cs): the folder per platform, portable.txt and --save-dir, the copy-only
/// migration from next to the game (and from a previous build beside it), the both-exist rule, atomic writes with a
/// .bak and recovery from a damaged file, the newer-format guard, read-only progress, and every save type
/// round-tripping through the save folder. Everything happens in temp folders: the whole self-test saves into
/// <see cref="SelfTestSaveFolder"/>, never %APPDATA% or the game folder.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The self-test's own save folder (set in <see cref="Run"/> before any section).</summary>
    private static readonly string SelfTestSaveFolder = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-saves-{Guid.NewGuid():N}");

    private static void CheckSaves()
    {
        // The self-test itself never saves into the real folder.
        var real = SavePaths.Resolve([], SavePaths.ExeFolder, OperatingSystem.IsWindows(),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Line($"self-test save folder: {SavePaths.Folder} ({SavePaths.Mode}); a player's would be {real.Folder} ({real.Mode})");
        if (!SavePaths.SameFolder(SavePaths.Folder, SelfTestSaveFolder) || SavePaths.SameFolder(SavePaths.Folder, real.Folder))
            Fail("the self-test isn't saving into its own temp folder");
        foreach (var name in new[] { "progress.txt", "loadout.txt", "settings.txt", "workbench.txt" })
            if (!SavePaths.PathOf(name).StartsWith(SelfTestSaveFolder, StringComparison.Ordinal))
                Fail($"{name} resolves to {SavePaths.PathOf(name)}, outside the self-test folder");

        var root = Path.Combine(Path.GetTempPath(), $"callofslop-savetest-{Guid.NewGuid():N}");
        try
        {
            CheckSaveResolve(root);
            CheckSaveMigration(root);
            CheckSaveAtomic(root);
            CheckSaveNewerFormat(root);
            CheckSaveRoundTrips();
        }
        finally
        {
            SavePaths.ClearWarnings();
            try { Directory.Delete(root, true); } catch (IOException) { /* temp folder: not important */ }
        }
    }

    private static void CheckSaveResolve(string root)
    {
        var exe = Path.Combine(root, "game");
        Directory.CreateDirectory(exe);
        var roaming = Path.Combine(root, "Roaming");
        var share = Path.Combine(root, "share");

        var win = SavePaths.Resolve([], exe, true, roaming, share);
        var nix = SavePaths.Resolve([], exe, false, roaming, share);
        var none = SavePaths.Resolve([], exe, true, "", share);
        Line($"windows: {win.Folder} ({win.Mode}); linux: {nix.Folder} ({nix.Mode}); no profile: {none.Mode}");
        if (!SavePaths.SameFolder(win.Folder, Path.Combine(roaming, "CallOfSlop")) || win.Mode != "user")
            Fail($"Windows save folder is {win.Folder} (should be <AppData>\\CallOfSlop)");
        if (!SavePaths.SameFolder(nix.Folder, Path.Combine(share, "CallOfSlop")))
            Fail($"Linux save folder is {nix.Folder} (should be ~/.local/share/CallOfSlop)");
        if (!SavePaths.SameFolder(none.Folder, exe) || none.Mode != "fallback")
            Fail("with no profile folder the saves should stay next to the game");

        var custom = Path.Combine(root, "custom saves");
        File.WriteAllText(Path.Combine(exe, SavePaths.PortableMarker), "");
        var portable = SavePaths.Resolve([], exe, true, roaming, share);
        var flag = SavePaths.Resolve(["--save-dir", custom, "host"], exe, true, roaming, share);
        var flagEq = SavePaths.Resolve([$"--save-dir={custom}"], exe, false, roaming, share);
        File.Delete(Path.Combine(exe, SavePaths.PortableMarker)); // our own temp marker
        Line($"portable.txt: {portable.Mode}; --save-dir: {flag.Mode} {flag.Folder}");
        if (!SavePaths.SameFolder(portable.Folder, exe) || portable.Mode != "portable")
            Fail("portable.txt didn't keep the saves next to the game");
        if (!SavePaths.SameFolder(flag.Folder, custom) || !SavePaths.SameFolder(flagEq.Folder, custom))
            Fail("--save-dir didn't win over portable.txt / the default");
        var plain = SavePaths.WithoutSaveDir(["--save-dir", custom, "host", "--lag=50", "--save-dir=x"]);
        if (!plain.SequenceEqual(new[] { "host", "--lag=50" }))
            Fail($"--save-dir isn't stripped from the command line: {string.Join(" ", plain)}");
    }

    private static void CheckSaveMigration(string root)
    {
        var exe = Path.Combine(root, "builds", "new", "CallOfSlop");
        var save = Path.Combine(root, "AppData", "CallOfSlop");
        Directory.CreateDirectory(exe);
        File.WriteAllLines(Path.Combine(exe, "progress.txt"), ["xp=1234", "prestige=1"]);
        File.WriteAllLines(Path.Combine(exe, "loadout.txt"), ["selected=0", "hat=fedora"]);
        Directory.CreateDirectory(Path.Combine(exe, "AudioMixes"));
        File.WriteAllText(Path.Combine(exe, "AudioMixes", "Loud.json"), "{}");
        File.WriteAllText(Path.Combine(exe, "classes.json"), "{}"); // game data: never copied

        var log = new List<string>();
        var copied = SavePaths.Migrate(exe, save, log.Add);
        Line($"migration copied: {string.Join(", ", copied)}");
        foreach (var l in log)
            Line("  " + l);
        if (!copied.Contains("progress.txt") || !copied.Contains("loadout.txt") || !copied.Contains("AudioMixes/Loud.json"))
            Fail("migration didn't copy progress.txt, loadout.txt and AudioMixes/");
        if (File.Exists(Path.Combine(save, "classes.json")) || copied.Contains("settings.txt"))
            Fail("migration copied something that isn't a save");
        if (!File.Exists(Path.Combine(exe, "progress.txt")) || !File.Exists(Path.Combine(exe, "AudioMixes", "Loud.json")))
            Fail("migration moved or deleted an original (it must only copy)");
        if (!log.Any(l => l.StartsWith("[saves] migrated progress.txt from ")))
            Fail("no '[saves] migrated progress.txt from <folder>' log line");
        if (Progress.Load(Path.Combine(save, "progress.txt")) is not { Xp: 1234, Prestige: 1 })
            Fail("migrated progress.txt doesn't load the same");

        // Both exist: the save folder's copy wins; the one next to the game is left alone.
        File.WriteAllLines(Path.Combine(exe, "progress.txt"), ["xp=99999"]);
        log.Clear();
        var again = SavePaths.Migrate(exe, save, log.Add);
        if (again.Count != 0 || Progress.Load(Path.Combine(save, "progress.txt")).Xp != 1234)
            Fail("a second migration replaced the save folder's progress");
        if (File.ReadAllLines(Path.Combine(exe, "progress.txt")) is not ["xp=99999"])
            Fail("the ignored copy next to the game was changed");
        if (!log.Any(l => l.Contains("kept progress.txt") && l.Contains("ignored")))
            Fail("no log line about the ignored copy next to the game");
        else
            Line("both exist: " + log.First(l => l.Contains("kept progress.txt")));

        // First run of a fresh unzip: the newest previous build beside this one is found (one without a game in it isn't).
        var old = Path.Combine(root, "builds", "old", "CallOfSlop");
        var older = Path.Combine(root, "builds", "older", "CallOfSlop");
        var junk = Path.Combine(root, "builds", "notes");
        foreach (var (dir, days, game) in new[] { (old, -1, true), (older, -5, true), (junk, 0, false) })
        {
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "progress.txt");
            File.WriteAllLines(p, ["xp=7"]);
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(days));
            if (game)
                File.WriteAllText(Path.Combine(dir, "CallOfSlop.dll"), "");
        }
        var found = SavePaths.FindPreviousBuild(exe);
        Line($"previous build found: {found}");
        if (found == null || !SavePaths.SameFolder(found, old))
            Fail($"the previous build wasn't found (got {found ?? "none"}, should be {old})");
    }

    private static void CheckSaveAtomic(string root)
    {
        var dir = Path.Combine(root, "atomic");
        var path = Path.Combine(dir, "progress.txt");
        new Progress { Xp = 100 }.Save(path);
        new Progress { Xp = 200 }.Save(path);
        var lines = File.ReadAllLines(path);
        var bak = File.ReadAllLines(SavePaths.BackupOf(path));
        Line($"after two saves: {lines[0]}, {lines[1]}; backup {bak[1]}; temp left: {File.Exists(path + ".tmp")}");
        if (lines[0] != $"version={Progress.SaveVersion}" || !lines.Contains("xp=200"))
            Fail("progress.txt doesn't start with its version line / lost the new XP");
        if (!bak.Contains("xp=100"))
            Fail("progress.txt.bak doesn't hold the previous save");
        if (File.Exists(path + ".tmp"))
            Fail("a .tmp file was left behind");

        // A crash in an old build (or a power cut) leaves zeros or nothing: the backup loads.
        foreach (var (what, bytes) in new[] { ("zero-filled", new byte[64]), ("empty", Array.Empty<byte>()) })
        {
            File.WriteAllBytes(path, bytes);
            var p = Progress.Load(path);
            Line($"{what} progress.txt: loaded XP {p.Xp} from the backup, damaged copy kept: {File.Exists(path + ".corrupt")}");
            if (p.Xp != 100)
                Fail($"a {what} progress.txt didn't load its backup (XP {p.Xp}, should be 100)");
        }
        // Saving again replaces the damaged file but keeps the good backup.
        new Progress { Xp = 300 }.Save(path);
        if (Progress.Load(path).Xp != 300 || !File.ReadAllLines(SavePaths.BackupOf(path)).Contains("xp=100"))
            Fail("saving over a damaged file lost the good backup");

        // Old saves (no version line) load exactly as before.
        File.WriteAllLines(path, ["xp=4321", "prestige=2", "kills=9"]);
        if (Progress.Load(path) is not { Xp: 4321, Prestige: 2, Kills: 9 })
            Fail("a save without a version line didn't load");

        // Read-only progress (the smoke test's copy, the self-test's) never writes.
        var ro = Path.Combine(dir, "readonly.txt");
        new Progress { ReadOnly = true, Xp = 5 }.Save(ro);
        if (File.Exists(ro))
            Fail("a ReadOnly progress wrote a file");
    }

    private static void CheckSaveNewerFormat(string root)
    {
        SavePaths.ClearWarnings();
        var path = Path.Combine(root, "newer", "progress.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string[] future = ["version=99", "xp=777", "future_thing=1"];
        File.WriteAllLines(path, future);
        var p = Progress.Load(path);
        p.Xp += 10;
        p.Save(path);
        Line($"newer-format save: loaded XP {p.Xp - 10}, file unchanged: {File.ReadAllLines(path).SequenceEqual(future)}, warning: {SavePaths.Warning != null}");
        if (p.Xp != 787)
            Fail("a newer-format save didn't load what this build understands");
        if (!File.ReadAllLines(path).SequenceEqual(future) || File.Exists(SavePaths.BackupOf(path)))
            Fail("an older build saved over a newer-format save");
        if (SavePaths.Warning == null || SavePaths.TakeWarningOnce() == null || SavePaths.TakeWarningOnce() != null)
            Fail("the newer-format warning isn't shown exactly once");
        SavePaths.ClearWarnings();
    }

    private static void CheckSaveRoundTrips()
    {
        // Every save type through the (self-test's temp) save folder.
        var progressPath = SavePaths.PathOf("progress.txt");
        new Progress { Xp = 4242, Prestige = 3, Kills = 17 }.Save();
        var p = Progress.Load();
        if (p is not { Xp: 4242, Prestige: 3, Kills: 17 } || !File.Exists(progressPath))
            Fail("progress didn't round-trip through the save folder");

        var settings = new Settings { Name = "SaveTest", Fov = 97 };
        settings.Save();
        var s = Settings.Load();
        if (s.Name != "SaveTest" || Math.Abs(s.Fov - 97) > 0.01f || !File.Exists(SavePaths.PathOf("settings.txt")))
            Fail("settings didn't round-trip through the save folder");

        var backup = LoadoutStore.SaveLines().ToArray();
        var wasReadOnly = LoadoutStore.ReadOnly;
        try
        {
            LoadoutStore.ReadOnly = false;
            LoadoutStore.Slots[0].Name = "Persisted";
            LoadoutStore.Save();
            LoadoutStore.Slots[0].Name = "Changed";
            LoadoutStore.Load();
            if (LoadoutStore.Slots[0].Name != "Persisted" || !File.Exists(SavePaths.PathOf("loadout.txt")))
                Fail("loadouts didn't round-trip through the save folder");
        }
        finally
        {
            LoadoutStore.ReadOnly = wasReadOnly;
            LoadoutStore.LoadLines(backup);
        }

        var buildRo = BuildStore.ReadOnly;
        try
        {
            BuildStore.ReadOnly = false;
            var want = BuildStore.Lines();
            BuildStore.Save();
            var file = SavePaths.ReadLines(SavePaths.PathOf("workbench.txt"), BuildStore.SaveVersion);
            if (file == null || !file.Skip(1).SequenceEqual(want))
                Fail("workbench builds didn't round-trip through the save folder");
        }
        finally
        {
            BuildStore.ReadOnly = buildRo;
        }
        Line($"round trips in {SavePaths.Folder}: {string.Join(", ", Directory.GetFiles(SavePaths.Folder).Select(f => Path.GetFileName(f)).Order())}");
    }
}
