namespace CallOfSlop;

/// <summary>
/// Self-test for the save folder (SavePaths.cs): the folder per platform, portable.txt and --save-dir, the copy-only
/// migration from next to the game (and from a previous build beside it), the both-exist rule, atomic writes with a
/// .bak and recovery from a damaged file, the newer-format guard, and every save type
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
        foreach (var name in new[] { "loadout.txt", "settings.txt", "workbench.txt" })
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
        File.WriteAllLines(Path.Combine(exe, "loadout.txt"), ["selected=0", "slot0=Courier"]);
        File.WriteAllLines(Path.Combine(exe, "workbench.txt"), ["build=1"]);
        Directory.CreateDirectory(Path.Combine(exe, "AudioMixes"));
        File.WriteAllText(Path.Combine(exe, "AudioMixes", "Loud.json"), "{}");
        File.WriteAllText(Path.Combine(exe, "classes.json"), "{}"); // game data: never copied

        var log = new List<string>();
        var copied = SavePaths.Migrate(exe, save, log.Add);
        Line($"migration copied: {string.Join(", ", copied)}");
        foreach (var l in log)
            Line("  " + l);
        if (!copied.Contains("workbench.txt") || !copied.Contains("loadout.txt") || !copied.Contains("AudioMixes/Loud.json"))
            Fail("migration didn't copy workbench.txt, loadout.txt and AudioMixes/");
        if (File.Exists(Path.Combine(save, "classes.json")) || copied.Contains("settings.txt"))
            Fail("migration copied something that isn't a save");
        if (!File.Exists(Path.Combine(exe, "loadout.txt")) || !File.Exists(Path.Combine(exe, "AudioMixes", "Loud.json")))
            Fail("migration moved or deleted an original (it must only copy)");
        if (!log.Any(l => l.StartsWith("[saves] migrated loadout.txt from ")))
            Fail("no '[saves] migrated loadout.txt from <folder>' log line");
        if (!File.ReadAllLines(Path.Combine(save, "loadout.txt")).SequenceEqual(new[] { "selected=0", "slot0=Courier" }))
            Fail("migrated loadout.txt isn't the same");

        // Both exist: the save folder's copy wins; the one next to the game is left alone.
        File.WriteAllLines(Path.Combine(exe, "loadout.txt"), ["selected=9"]);
        log.Clear();
        var again = SavePaths.Migrate(exe, save, log.Add);
        if (again.Count != 0 || File.ReadAllLines(Path.Combine(save, "loadout.txt")) is not ["selected=0", "slot0=Courier"])
            Fail("a second migration replaced the save folder's loadout");
        if (File.ReadAllLines(Path.Combine(exe, "loadout.txt")) is not ["selected=9"])
            Fail("the ignored copy next to the game was changed");
        if (!log.Any(l => l.Contains("kept loadout.txt") && l.Contains("ignored")))
            Fail("no log line about the ignored copy next to the game");
        else
            Line("both exist: " + log.First(l => l.Contains("kept loadout.txt")));

        // First run of a fresh unzip: the newest previous build beside this one is found (one without a game in it isn't).
        var old = Path.Combine(root, "builds", "old", "CallOfSlop");
        var older = Path.Combine(root, "builds", "older", "CallOfSlop");
        var junk = Path.Combine(root, "builds", "notes");
        foreach (var (dir, days, game) in new[] { (old, -1, true), (older, -5, true), (junk, 0, false) })
        {
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, SavePaths.MarkerFile);
            File.WriteAllLines(p, ["name=old"]);
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(days));
            if (game)
                File.WriteAllText(Path.Combine(dir, "CallOfSlop.dll"), "");
        }
        var found = SavePaths.FindPreviousBuild(exe);
        Line($"previous build found: {found}");
        if (found == null || !SavePaths.SameFolder(found, old))
            Fail($"the previous build wasn't found (got {found ?? "none"}, should be {old})");
    }

    // A save file through SavePaths alone (loadout.txt's name; the lines stand for any save file's: progress.txt was the
    // example until levelling went, 2026-11-17).
    private const int TestSaveVersion = 1;

    private static int? TestXp(string path) =>
        SavePaths.ReadLines(path, TestSaveVersion) is { } lines && lines.FirstOrDefault(l => l.StartsWith("xp=", StringComparison.Ordinal)) is { } l && int.TryParse(l[3..], out var v) ? v : null;

    private static void CheckSaveAtomic(string root)
    {
        var dir = Path.Combine(root, "atomic");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "loadout.txt");
        SavePaths.WriteLines(path, ["xp=100"], TestSaveVersion);
        SavePaths.WriteLines(path, ["xp=200"], TestSaveVersion);
        var lines = File.ReadAllLines(path);
        var bak = File.ReadAllLines(SavePaths.BackupOf(path));
        Line($"after two saves: {lines[0]}, {lines[1]}; backup {bak[1]}; temp left: {File.Exists(path + ".tmp")}");
        if (lines[0] != $"version={TestSaveVersion}" || !lines.Contains("xp=200"))
            Fail("the save doesn't start with its version line / lost the new value");
        if (!bak.Contains("xp=100"))
            Fail("the .bak doesn't hold the previous save");
        if (File.Exists(path + ".tmp"))
            Fail("a .tmp file was left behind");

        // A crash in an old build (or a power cut) leaves zeros or nothing: the backup loads.
        foreach (var (what, bytes) in new[] { ("zero-filled", new byte[64]), ("empty", Array.Empty<byte>()) })
        {
            File.WriteAllBytes(path, bytes);
            var xp = TestXp(path);
            Line($"{what} save: loaded {xp} from the backup, damaged copy kept: {File.Exists(path + ".corrupt")}");
            if (xp != 100)
                Fail($"a {what} save didn't load its backup ({xp}, should be 100)");
        }
        // Saving again replaces the damaged file but keeps the good backup.
        SavePaths.WriteLines(path, ["xp=300"], TestSaveVersion);
        if (TestXp(path) != 300 || !File.ReadAllLines(SavePaths.BackupOf(path)).Contains("xp=100"))
            Fail("saving over a damaged file lost the good backup");

        // Old saves (no version line) load exactly as before.
        File.WriteAllLines(path, ["xp=4321"]);
        if (TestXp(path) != 4321)
            Fail("a save without a version line didn't load");
    }

    private static void CheckSaveNewerFormat(string root)
    {
        SavePaths.ClearWarnings();
        var path = Path.Combine(root, "newer", "loadout.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string[] future = ["version=99", "xp=777", "future_thing=1"];
        File.WriteAllLines(path, future);
        var xp = TestXp(path) ?? -1;
        SavePaths.WriteLines(path, [$"xp={xp + 10}"], TestSaveVersion);
        Line($"newer-format save: loaded {xp}, file unchanged: {File.ReadAllLines(path).SequenceEqual(future)}, warning: {SavePaths.Warning != null}");
        if (xp != 777)
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
