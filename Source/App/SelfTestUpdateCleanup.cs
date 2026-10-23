namespace CallOfSlop;

/// <summary>
/// The updater's cleanup (UpdateCleanup.cs, BuildMarker.cs) in a fake install root in a temp folder: marked and unmarked
/// builds, the running build, a newer build, portable builds and builds with saves, renamed and copied markers, a link to
/// a folder outside the root, interrupted unzips and removals, downloads and temp files. Checks exactly what is removed
/// and kept, the rollback rule, re-checking before deleting, a locked or running folder (kept, removed on the next
/// run), resuming a removal that stopped half way, the update log, and that nothing outside the root changes. Never
/// touches the real install or the real saves.
/// </summary>
public static partial class SelfTest
{
    private static void CheckUpdateCleanup()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            UcMarkers(temp);
            UcPlanAndRun(temp);
            UcCleaner(temp);
            UcSettings(temp);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true); // the self-test's own temp folder
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp; never mind.
            }
        }
    }

    private static readonly DateTime UcInstalled = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A fake build: root/name/CallOfSlop/CallOfSlop.exe (+ marker unless version is null).</summary>
    private static string UcBuild(string root, string name, string? version, bool confirmed = true, string? markerFolder = null, bool exe = true, bool partial = false)
    {
        var dir = Path.Combine(root, name);
        var game = Path.Combine(dir, "CallOfSlop");
        Directory.CreateDirectory(Path.Combine(game, "Maps", "Legacy"));
        if (exe)
        {
            File.WriteAllText(Path.Combine(game, "CallOfSlop.exe"), "fake exe " + name);
            File.WriteAllText(Path.Combine(game, "CallOfSlop.dll"), new string('x', 3000)); // (a dll alone counts as a game off Windows)
        }
        else
            File.WriteAllText(Path.Combine(game, "data.bin"), new string('x', 3000));
        File.WriteAllText(Path.Combine(game, "Maps", "Legacy", "a.json"), "{}");
        if (version != null)
            BuildMarker.Write(dir, BuildInfo.ParseRelease(version)!, markerFolder ?? (partial ? name[..^UpdateCleanup.PartialSuffix.Length] : name), partial, UcInstalled,
                confirmed ? UcInstalled.AddHours(1) : null);
        return dir;
    }

    private static List<string> UcSnapshot(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f) + "|" + (File.Exists(f) ? File.ReadAllText(f) : "")).OrderBy(x => x, StringComparer.Ordinal).ToList()
            : [];

    private static void UcMarkers(string temp)
    {
        var parsed = UpdateCleanup.ParseFolderName("CallOfSlop-0.2026.1022.5-2.deleting");
        var bad = new[] { "CallOfSlop", "CallOfSlop-tester", "CallOfSlop-0.2026.1022", "CallOfSlop-0.2026.1022.5 - Copy", "CallOfSlop-0.2026.1022.5.zip", "XCallOfSlop-0.2026.1022.5", "CallOfSlop-1.2026.1022.5" }
            .Where(n => UpdateCleanup.ParseFolderName(n) != null).ToList();
        if (parsed is not { Base: "CallOfSlop-0.2026.1022.5-2", Suffix: ".deleting" } || parsed.Value.Version.ToString() != "0.2026.1022.5" || bad.Count > 0)
            Fail($"update cleanup: folder names parsed wrong ({parsed}); accepted [{string.Join(", ", bad)}]");

        // Extract writes the marker (and never unzips one from the zip).
        var dir = Path.Combine(temp, "markers");
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(dir, "u.zip");
        File.WriteAllBytes(zip, UpMakeZip(("CallOfSlop/CallOfSlop.exe", "x"), (BuildMarker.FileName, "format=1\nversion=0.2026.1001.1\nfolder=Evil\nstate=done\n"),
            ("CallOfSlop/" + BuildMarker.FileName, "forged")));
        var target = Path.Combine(dir, "CallOfSlop-0.2026.1022.7");
        UpdateInstaller.Extract(zip, target, BuildInfo.ParseRelease("0.2026.1022.7"));
        var m = BuildMarker.Read(target);
        if (m is not { Partial: false, ConfirmedUtc: null } || m.Version.ToString() != "0.2026.1022.7" || m.Folder != "CallOfSlop-0.2026.1022.7"
            || File.Exists(Path.Combine(target, "CallOfSlop", BuildMarker.FileName)))
            Fail($"update cleanup: Extract's marker is {m} (a marker from the zip must never be unzipped)");
        var second = Path.Combine(dir, "CallOfSlop-0.2026.1022.7-2");
        UpdateInstaller.Extract(zip, second); // version from the folder name
        if (BuildMarker.Read(second) is not { Folder: "CallOfSlop-0.2026.1022.7-2" })
            Fail("update cleanup: a -2 install's marker doesn't name its folder");
        if (!BuildMarker.Confirm(target, UcInstalled) || BuildMarker.Read(target)?.ConfirmedUtc != UcInstalled || BuildMarker.Read(target)?.InstalledUtc == DateTime.MinValue)
            Fail("update cleanup: Confirm didn't record the build as working");
        var plain = Path.Combine(dir, "plain");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, BuildMarker.FileName), "junk\nversion=banana\n");
        if (BuildMarker.Read(plain) != null || BuildMarker.Confirm(plain, UcInstalled) || BuildMarker.Read(Path.Combine(dir, "missing")) != null)
            Fail("update cleanup: a junk or missing marker was read as one");
        Line("markers: Extract writes one naming the folder and version (-2 too), zip-supplied markers ignored, Confirm records the first menu, junk markers refused");
    }

    private static void UcPlanAndRun(string temp)
    {
        var top = Path.Combine(temp, "plan");
        var root = Path.Combine(top, "Games");
        var outside = Path.Combine(top, "outside");
        var saves = Path.Combine(top, "saves");
        Directory.CreateDirectory(Path.Combine(outside, "deep"));
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "keep me");
        File.WriteAllText(Path.Combine(outside, "deep", "also.txt"), "keep me too");
        Directory.CreateDirectory(saves);

        var current = UcBuild(root, "CallOfSlop-0.2026.1022.1", "0.2026.1022.1");
        var exeFolder = Path.Combine(current, "CallOfSlop");
        var cur = BuildInfo.ParseRelease("0.2026.1022.1");
        if (!SavePaths.SameFolder(UpdateCleanup.CurrentBuildFolder(exeFolder), current) || !SavePaths.SameFolder(UpdateInstaller.InstallRoot(exeFolder), root))
            Fail($"update cleanup: current build folder {UpdateCleanup.CurrentBuildFolder(exeFolder)} / root {UpdateInstaller.InstallRoot(exeFolder)}");

        var a = UcBuild(root, "CallOfSlop-0.2026.1019.1", "0.2026.1019.1");
        var b = UcBuild(root, "CallOfSlop-0.2026.1020.1", "0.2026.1020.1"); // newest confirmed older one: the rollback
        var c = UcBuild(root, "CallOfSlop-0.2026.1021.1", "0.2026.1021.1", confirmed: false); // never reached its menu
        var dup = UcBuild(root, "CallOfSlop-0.2026.1022.1-2", "0.2026.1022.1");
        var newer = UcBuild(root, "CallOfSlop-0.2026.1023.1", "0.2026.1023.1");
        var portable = UcBuild(root, "CallOfSlop-0.2026.1018.1", "0.2026.1018.1");
        File.WriteAllText(Path.Combine(portable, "CallOfSlop", SavePaths.PortableMarker), "");
        File.WriteAllText(Path.Combine(portable, "CallOfSlop", "progress.txt"), "version=3\nlevel=42\n");
        var withSaves = UcBuild(root, "CallOfSlop-0.2026.1018.2", "0.2026.1018.2");
        File.WriteAllText(Path.Combine(withSaves, "CallOfSlop", "loadout.txt"), "version=1\nx=1\n");
        var mixes = UcBuild(root, "CallOfSlop-0.2026.1018.3", "0.2026.1018.3");
        Directory.CreateDirectory(Path.Combine(mixes, "CallOfSlop", "AudioMixes"));
        var tester = UcBuild(root, "CallOfSlop-tester", null);
        var unmarked = UcBuild(root, "CallOfSlop-0.2026.1017.1", null);
        var copied = UcBuild(root, "CallOfSlop-0.2026.1016.1", "0.2026.1016.1", markerFolder: "CallOfSlop-0.2026.1015.9");
        var inUse = UcBuild(root, "CallOfSlop-0.2026.1015.1", "0.2026.1015.1");
        var noExe = UcBuild(root, "CallOfSlop-0.2026.1012.1", "0.2026.1012.1", exe: false);
        var saveHome = UcBuild(root, "CallOfSlop-0.2026.1011.1", "0.2026.1011.1");
        var saveFolder = Path.Combine(saveHome, "CallOfSlop", "MySaves");
        Directory.CreateDirectory(saveFolder);
        var oldPartial = UcBuild(root, "CallOfSlop-0.2026.1014.1.partial", "0.2026.1014.1", partial: true, exe: false);
        foreach (var e in Directory.EnumerateFileSystemEntries(oldPartial, "*", SearchOption.AllDirectories).Append(oldPartial))
            if (Directory.Exists(e))
                Directory.SetLastWriteTimeUtc(e, DateTime.UtcNow.AddHours(-3));
            else
                File.SetLastWriteTimeUtc(e, DateTime.UtcNow.AddHours(-3));
        var youngPartial = UcBuild(root, "CallOfSlop-0.2026.1023.2.partial", "0.2026.1023.2", partial: true, exe: false);
        var halfDeleted = UcBuild(root, "CallOfSlop-0.2026.1013.1.deleting", "0.2026.1013.1", markerFolder: "CallOfSlop-0.2026.1013.1");
        var strayDeleting = Path.Combine(root, "CallOfSlop-0.2026.1010.1.deleting");
        Directory.CreateDirectory(strayDeleting);
        File.WriteAllText(Path.Combine(strayDeleting, "notes.txt"), "not ours");
        var oldZip = Path.Combine(root, "CallOfSlop-0.2026.1021.1.zip.download");
        File.WriteAllBytes(oldZip, new byte[5000]);
        var pausedZip = Path.Combine(root, "CallOfSlop-0.2026.1023.1.zip.download");
        File.WriteAllBytes(pausedZip, new byte[700]);
        var browserZip = Path.Combine(root, "CallOfSlop-0.2026.1021.1.zip");
        File.WriteAllBytes(browserZip, new byte[10]);
        var probe = Path.Combine(root, "callofslop-write-probe-2000000001.tmp");
        File.WriteAllBytes(probe, []);
        var otherFile = Path.Combine(root, "my notes.txt");
        File.WriteAllText(otherFile, "mine");

        // Links: a "build" in the root that is a link to a marked build outside, and a link inside a removed build.
        var outsideBuild = UcBuild(outside, "CallOfSlop-0.2026.1009.1", "0.2026.1009.1");
        var linkBuild = Path.Combine(root, "CallOfSlop-0.2026.1009.1");
        var links = 0;
        try
        {
            Directory.CreateSymbolicLink(linkBuild, outsideBuild);
            links++;
            Directory.CreateSymbolicLink(Path.Combine(a, "CallOfSlop", "linkout"), outside);
            File.CreateSymbolicLink(Path.Combine(a, "CallOfSlop", "filelink.txt"), Path.Combine(outside, "sentinel.txt"));
            links++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Line($"(link checks skipped: {ex.Message.Trim()})");
        }
        var outsideBefore = UcSnapshot(outside);
        var portableBefore = UcSnapshot(portable);

        var inUseNow = true;
        var log = Path.Combine(saves, UpdateCleanup.LogFileName);
        var logged = new List<string>();
        CleanupOptions Opts(bool keep = true) => new()
        {
            KeepPrevious = keep,
            SaveFolder = saveFolder,
            InUse = p => inUseNow && SavePaths.SameFolder(p, inUse),
            LogPath = log,
            Log = l => logged.Add(l),
            NowUtc = DateTime.UtcNow,
        };
        string Names(IEnumerable<string> paths) => string.Join(", ", paths.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));

        var expectRemove = new[] { a, c, dup, oldPartial, halfDeleted, oldZip, probe };
        var plan = UpdateCleanup.Plan(exeFolder, cur, Opts());
        if (Names(plan.Remove.Select(r => r.Path)) != Names(expectRemove))
            Fail($"update cleanup: plan removes [{Names(plan.Remove.Select(r => r.Path))}], expected [{Names(expectRemove)}]");
        var expectKept = new List<string> { current, b, newer, portable, withSaves, mixes, tester, unmarked, copied, inUse, noExe, saveHome, youngPartial, strayDeleting, pausedZip };
        if (links > 0)
            expectKept.Add(linkBuild);
        var missingKept = expectKept.Where(k => !plan.Kept.Any(x => SavePaths.SameFolder(x.Path, k))).ToList();
        if (missingKept.Count > 0)
            Fail($"update cleanup: not listed as kept (with a reason): {Names(missingKept)}");
        var listedTwice = plan.Remove.Where(r => plan.Kept.Any(k => SavePaths.SameFolder(k.Path, r.Path))).ToList();
        var outsideRoot = plan.Remove.Where(r => !SavePaths.SameFolder(Path.GetDirectoryName(r.Path)!, root)).ToList();
        if (listedTwice.Count > 0 || outsideRoot.Count > 0 || plan.Kept.Any(k => k.Why.Length == 0))
            Fail($"update cleanup: kept and removed [{Names(listedTwice.Select(r => r.Path))}], outside the root [{Names(outsideRoot.Select(r => r.Path))}]");
        var rollbackWhy = plan.Kept.FirstOrDefault(k => SavePaths.SameFolder(k.Path, b))?.Why ?? "";
        if (!rollbackWhy.Contains("rollback"))
            Fail($"update cleanup: v1020.1 kept for '{rollbackWhy}', not as the rollback");
        if (plan.Remove.First(r => r.Path == a).Bytes < 3000)
            Fail("update cleanup: sizes aren't counted");

        // Without the rollback copy the previous version goes too; a dev build (no version) removes nothing new.
        var noKeep = UpdateCleanup.Plan(exeFolder, cur, Opts(keep: false));
        var dev = UpdateCleanup.Plan(exeFolder, null, Opts());
        var resumeOnly = UpdateCleanup.Plan(exeFolder, cur, new CleanupOptions { ResumeOnly = true, SaveFolder = saveFolder, InUse = _ => false, Log = _ => { } });
        if (Names(noKeep.Remove.Select(r => r.Path)) != Names(expectRemove.Append(b)) || Names(dev.Remove.Select(r => r.Path)) != Names([halfDeleted])
            || Names(resumeOnly.Remove.Select(r => r.Path)) != Names([halfDeleted]))
            Fail($"update cleanup: no rollback [{Names(noKeep.Remove.Select(r => r.Path))}], dev build [{Names(dev.Remove.Select(r => r.Path))}], resume only [{Names(resumeOnly.Remove.Select(r => r.Path))}]");
        if (!Directory.Exists(a) || !File.Exists(oldZip))
            Fail("update cleanup: planning deleted something");

        // Re-checked before deleting: c gained a portable.txt since the plan was shown, so it stays.
        File.WriteAllText(Path.Combine(c, "CallOfSlop", SavePaths.PortableMarker), "");
        var r1 = UpdateCleanup.Execute(plan, exeFolder, cur, Opts());
        var gone = expectRemove.Where(p => p != c).ToList();
        var left = gone.Where(p => Directory.Exists(p) || File.Exists(p) || Directory.Exists(p + UpdateCleanup.DeletingSuffix)).ToList();
        var lost = expectKept.Append(c).Append(browserZip).Append(otherFile).Where(p => !Directory.Exists(p) && !File.Exists(p)).ToList();
        if (r1.Removed != gone.Count || left.Count > 0 || lost.Count > 0 || r1.Skipped.Count != 1 || r1.Failed.Count > 0)
            Fail($"update cleanup: run removed {r1.Removed} (want {gone.Count}), still there [{Names(left)}], wrongly gone [{Names(lost)}], skipped {r1.Skipped.Count}, failed {r1.Failed.Count}");
        if (!UcSnapshot(outside).SequenceEqual(outsideBefore) || !UcSnapshot(portable).SequenceEqual(portableBefore)
            || File.ReadAllText(Path.Combine(portable, "CallOfSlop", "progress.txt")) != "version=3\nlevel=42\n")
            Fail("update cleanup: something outside the root or in the portable build changed");
        if (links > 0 && (!File.Exists(Path.Combine(outsideBuild, "CallOfSlop", "CallOfSlop.exe")) || !File.Exists(Path.Combine(outside, "sentinel.txt"))))
            Fail("update cleanup: a link was followed out of the root");

        // The running-folder stand-in is released: removed on the next run, the rollback still kept.
        inUseNow = false;
        var plan2 = UpdateCleanup.Plan(exeFolder, cur, Opts());
        if (Names(plan2.Remove.Select(r => r.Path)) != Names([inUse]))
            Fail($"update cleanup: second plan removes [{Names(plan2.Remove.Select(r => r.Path))}], expected only the folder that was in use");
        var lockNote = "a real lock is checked on Windows only";
        if (OperatingSystem.IsWindows())
        {
            // A file open inside (a running exe holds its files open): Windows refuses the rename, nothing is deleted.
            using (new FileStream(Path.Combine(inUse, "CallOfSlop", "CallOfSlop.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var locked = UpdateCleanup.Execute(plan2, exeFolder, cur, Opts());
                if (locked.Removed != 0 || locked.Failed.Count != 1 || UcSnapshot(inUse).Count != UcSnapshot(b).Count)
                    Fail($"update cleanup: a folder with an open file: removed {locked.Removed}, failed {locked.Failed.Count}, files left {UcSnapshot(inUse).Count}");
            }
            lockNote = "an open file blocked the rename and nothing was deleted";
        }
        var r2 = UpdateCleanup.Execute(UpdateCleanup.Plan(exeFolder, cur, Opts()), exeFolder, cur, Opts());
        if (r2.Removed != 1 || Directory.Exists(inUse))
            Fail($"update cleanup: the released folder wasn't removed ({r2.Removed}, failed {r2.Failed.Count})");

        // A removal that stops part way keeps its marker as ".deleting" and is finished on the next run.
        var crash = UcBuild(root, "CallOfSlop-0.2026.1008.1", "0.2026.1008.1");
        for (var i = 0; i < 6; i++)
            File.WriteAllText(Path.Combine(crash, "CallOfSlop", "Maps", $"m{i}.json"), "{}");
        var crashOpts = Opts();
        crashOpts.FailAfterFiles = 3;
        var r3 = UpdateCleanup.Execute(UpdateCleanup.Plan(exeFolder, cur, Opts()), exeFolder, cur, crashOpts);
        var half = crash + UpdateCleanup.DeletingSuffix;
        var halfMarker = BuildMarker.Read(half);
        var plan4 = UpdateCleanup.Plan(exeFolder, cur, Opts());
        if (r3.Failed.Count != 1 || Directory.Exists(crash) || halfMarker == null || plan4.Remove.Count != 1 || plan4.Remove[0].Kind != CleanupKind.UnfinishedRemoval)
            Fail($"update cleanup: interrupted removal: failed {r3.Failed.Count}, marker kept {halfMarker != null}, next plan [{string.Join(", ", plan4.Remove.Select(r => r.Kind))}]");
        var r4 = UpdateCleanup.Execute(plan4, exeFolder, cur, Opts());
        if (r4.Removed != 1 || Directory.Exists(half))
            Fail("update cleanup: the interrupted removal wasn't finished");
        var final = UpdateCleanup.Plan(exeFolder, cur, Opts());
        if (final.Remove.Count != 0)
            Fail($"update cleanup: still more to remove after cleaning: {Names(final.Remove.Select(r => r.Path))}");

        // The log: one line per removal, also on the console.
        var logLines = File.Exists(log) ? File.ReadAllLines(log) : [];
        var removedLines = logLines.Count(l => l.Contains(" removed "));
        var total = r1.Removed + r2.Removed + r4.Removed;
        if (removedLines != total || logged.Count(l => l.StartsWith("[update] removed")) != total || !logLines.Any(l => l.Contains(Path.GetFileName(a)))
            || !logLines.Any(l => l.Contains("no longer safe")) || !logLines.Any(l => l.Contains("couldn't remove")))
            Fail($"update cleanup: update.log has {removedLines} removals for {total} (lines: {logLines.Length})");
        Line($"plan: removes [{Names(expectRemove)}], keeps {plan.Kept.Count} with reasons (rollback v1020.1, newer, running, portable, saves, unmarked, renamed marker, no exe, save folder inside, young unzip, unmarked .deleting, paused download{(links > 0 ? ", link" : "")})");
        Line($"run: {r1.Removed} removed, 1 re-checked and kept (gained portable.txt), the in-use stand-in removed once released ({lockNote}), an interrupted removal finished next run; {removedLines} log lines; outside the root unchanged{(links > 0 ? ", links not followed" : "")}");
    }

    private static void UcCleaner(string temp)
    {
        var root = Path.Combine(temp, "cleaner");
        var current = UcBuild(root, "CallOfSlop-0.2026.1022.3", "0.2026.1022.3", confirmed: false);
        var prev = UcBuild(root, "CallOfSlop-0.2026.1022.2", "0.2026.1022.2");
        var old = UcBuild(root, "CallOfSlop-0.2026.1022.1", "0.2026.1022.1");
        var half = UcBuild(root, "CallOfSlop-0.2026.1021.1.deleting", "0.2026.1021.1", markerFolder: "CallOfSlop-0.2026.1021.1");
        var exeFolder = Path.Combine(current, "CallOfSlop");
        var log = Path.Combine(temp, "cleaner-saves", UpdateCleanup.LogFileName);
        var delay = UpdateCleaner.AutoDelay;
        UpdateCleaner.AutoDelay = TimeSpan.Zero;
        try
        {
            static bool Wait(UpdateCleaner c)
            {
                for (var i = 0; i < 400 && c.Busy; i++)
                    Thread.Sleep(25);
                return !c.Busy;
            }
            // Off: the build is marked as working and the unfinished removal is finished, nothing else.
            var off = new UpdateCleaner(exeFolder, BuildInfo.ParseRelease("0.2026.1022.3"), log, null);
            off.MenuReached(new Settings { UpdateCleanup = false });
            off.MenuReached(new Settings { UpdateCleanup = false }); // every frame: acts once
            if (!Wait(off) || BuildMarker.Read(current)?.ConfirmedUtc == null || Directory.Exists(half) || !Directory.Exists(old))
                Fail($"update cleanup: with the option off: confirmed {BuildMarker.Read(current)?.ConfirmedUtc != null}, unfinished removal left {Directory.Exists(half)}, old build kept {Directory.Exists(old)}");
            // On: the old build goes, the previous one stays.
            var on = new UpdateCleaner(exeFolder, BuildInfo.ParseRelease("0.2026.1022.3"), log, null);
            on.MenuReached(new Settings());
            if (!Wait(on) || Directory.Exists(old) || !Directory.Exists(prev) || !Directory.Exists(current))
                Fail($"update cleanup: automatic cleanup left old {Directory.Exists(old)}, previous {Directory.Exists(prev)}, current {Directory.Exists(current)}");
            // The button: a preview first (nothing removed), then removal only when confirmed.
            var button = new UpdateCleaner(exeFolder, BuildInfo.ParseRelease("0.2026.1022.3"), log, null);
            button.Preview(keepPrevious: false);
            Wait(button);
            var preview = button.Snapshot().Preview;
            if (preview?.Remove.Count != 1 || !Directory.Exists(prev))
                Fail($"update cleanup: the preview lists {preview?.Remove.Count} items (or removed something)");
            button.RemovePreview(keepPrevious: false);
            Wait(button);
            if (Directory.Exists(prev) || !button.Snapshot().Message.StartsWith("Removed 1", StringComparison.Ordinal))
                Fail($"update cleanup: confirmed removal: '{button.Snapshot().Message}'");
            // A dev build with the option on removes nothing new.
            var again = UcBuild(root, "CallOfSlop-0.2026.1020.1", "0.2026.1020.1");
            var devCleaner = new UpdateCleaner(exeFolder, null, log, null);
            devCleaner.MenuReached(new Settings());
            Wait(devCleaner);
            if (!Directory.Exists(again))
                Fail("update cleanup: a dev build removed an old version");
        }
        finally
        {
            UpdateCleaner.AutoDelay = delay;
        }
        Line("cleaner: first menu marks the build working, option off only finishes unfinished removals, option on keeps the previous version, the button previews then removes on confirm, dev builds remove nothing");
    }

    private static void UcSettings(string temp)
    {
        var path = Path.Combine(temp, "cleanup-settings.txt");
        new Settings { UpdateCleanup = false, UpdateKeepPrevious = false }.Save(path);
        var back = Settings.Load(path);
        File.WriteAllText(path, "version=1\nname=Old\n");
        var old = Settings.Load(path);
        if (back.UpdateCleanup || back.UpdateKeepPrevious || !old.UpdateCleanup || !old.UpdateKeepPrevious)
            Fail($"update cleanup: settings round-trip {back.UpdateCleanup}/{back.UpdateKeepPrevious}, old file {old.UpdateCleanup}/{old.UpdateKeepPrevious}");
        Line("settings: update_cleanup and update_keep_previous round-trip; older settings files get both on");
    }
}
