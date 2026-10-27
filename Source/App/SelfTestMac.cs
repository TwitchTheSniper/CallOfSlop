using System.Diagnostics;
using System.IO.Compression;

namespace CallOfSlop;

/// <summary>
/// The Mac build's rules, checked on every runner with made-up folders (the release workflow's Mac job runs them on
/// a real Mac too): where an app bundle's data and logs are (AppFolders.cs), the Mac save folder, update.json's
/// per-platform downloads (a Mac takes only its own zip, never the Windows one), the bundle as an install root, finding,
/// unzipping and opening CallOfSlop.app, and a whole check + install as a Mac against a fake GitHub (no network,
/// nothing launched). Everything happens in temp folders.
/// </summary>
public static partial class SelfTest
{
    private static void CheckMacRelease()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-mac-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            MacFolders(temp);
            MacManifests();
            MacInstall(temp);
        }
        finally
        {
            UpdateManifest.PlatformOverride = null;
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

    private static void MacFolders(string temp)
    {
        var sep = Path.DirectorySeparatorChar;
        var app = Path.Combine(temp, "Games", "CallOfSlop", "CallOfSlop.app");
        var macOs = Path.Combine(app, "Contents", "MacOS") + sep;
        var resources = Path.Combine(app, "Contents", "Resources");
        Directory.CreateDirectory(macOs);
        if (AppFolders.BundleOf(macOs) is not { } found || !SavePaths.SameFolder(found, app))
            Fail($"mac: {macOs} isn't seen as inside {app}");
        var plain = Path.Combine(temp, "bin", "Release") + sep;
        if (AppFolders.BundleOf(plain) != null || AppFolders.BundleOf(Path.Combine(temp, "Thing.app", "MacOS") + sep) != null
            || AppFolders.BundleOf(Path.Combine(temp, "NotAnApp", "Contents", "MacOS") + sep) != null)
            Fail("mac: a folder that isn't Something.app/Contents/MacOS counted as an app bundle");

        // Data: Contents/Resources once it has the Maps folder; the program folder otherwise (and always off a bundle).
        if (AppFolders.DataFor(macOs, Directory.Exists) != macOs)
            Fail("mac: a bundle without Contents/Resources/Maps didn't fall back to the program folder");
        Directory.CreateDirectory(Path.Combine(resources, "Maps"));
        var data = AppFolders.DataFor(macOs, Directory.Exists);
        if (data != resources + sep)
            Fail($"mac: the bundle's data folder is {data}, expected {resources}{sep}");
        if (AppFolders.DataFor(plain, _ => true) != plain)
            Fail("mac: a plain build folder's data moved");
        if (AppFolders.DataFor(AppFolders.Exe, Directory.Exists) != AppFolders.Data)
            Fail($"mac: this build's data folder {AppFolders.Data} isn't the rule's answer");

        // Logs: ~/Library/Logs/CallOfSlop inside a bundle (it may be read-only), next to a plain build.
        var home = Path.Combine(temp, "home");
        var logs = AppFolders.LogsFor(macOs, home);
        if (logs != Path.Combine(home, "Library", "Logs", "CallOfSlop") + sep || AppFolders.LogsFor(plain, home) != plain || AppFolders.LogsFor(macOs, "") != macOs)
            Fail($"mac: log folders wrong (bundle {logs}, plain {AppFolders.LogsFor(plain, home)})");

        // Saves: ~/Library/Application Support/CallOfSlop (Resolve takes the platform's folder as localData).
        var support = SavePaths.MacAppSupport(home);
        var saves = SavePaths.Resolve([], macOs, windows: false, roamingData: Path.Combine(home, ".config"), localData: support);
        if (saves.Folder != Path.Combine(home, "Library", "Application Support", "CallOfSlop") || saves.Mode != "user" || SavePaths.MacAppSupport("") != "")
            Fail($"mac: the save folder is {saves.Folder} ({saves.Mode})");
        if (OperatingSystem.IsMacOS() && !SavePaths.SameFolder(SavePaths.Resolve([], plain, false, "", SavePaths.MacAppSupport(AppFolders.Home())).Folder,
                Path.Combine(AppFolders.Home(), "Library", "Application Support", "CallOfSlop")))
            Fail("mac: this Mac's save folder isn't in ~/Library/Application Support");

        // The updater: the folder holding the .app stands for the build, so the zip layout (CallOfSlop-v/CallOfSlop/CallOfSlop.app)
        // has the same install root as on Windows.
        var zipLayout = Path.Combine(temp, "Builds", "CallOfSlop-0.2026.1022.6", "CallOfSlop", "CallOfSlop.app", "Contents", "MacOS") + sep;
        Directory.CreateDirectory(zipLayout);
        if (!SavePaths.SameFolder(UpdateInstaller.GameFolder(zipLayout), Path.Combine(temp, "Builds", "CallOfSlop-0.2026.1022.6", "CallOfSlop"))
            || !SavePaths.SameFolder(UpdateInstaller.InstallRoot(zipLayout), Path.Combine(temp, "Builds"))
            || !SavePaths.SameFolder(UpdateCleanup.CurrentBuildFolder(zipLayout), Path.Combine(temp, "Builds", "CallOfSlop-0.2026.1022.6")))
            Fail($"mac: install root for a bundle is {UpdateInstaller.InstallRoot(zipLayout)}, build folder {UpdateCleanup.CurrentBuildFolder(zipLayout)}");
        if (UpdateInstaller.GameFolder(plain) != plain)
            Fail("mac: a plain build's game folder moved");
        Line($"folders: bundle data in Contents/Resources, logs in ~/Library/Logs/CallOfSlop, saves in {Path.GetRelativePath(home, saves.Folder)}, install root beside the zip folder");
    }

    /// <summary>The end of an update.json with a Mac download (appended in place of the closing brace).</summary>
    private static string MacEntry(string url, long size, string sha) =>
        ",\"platforms\":{\"osx-arm64\":{\"url\":\"" + url + "\",\"size\":" + size + ",\"sha256\":\"" + sha + "\"}}}";

    private static void MacManifests()
    {
        var hash = new string('a', 64);
        var macHash = new string('e', 64);
        var macUrl = $"{UpdateManifest.DownloadPrefix}v{UpVersion}/CallOfSlop-{UpVersion}-mac.zip";
        var plain = UpManifestJson(UpZipUrl, 1234, hash);
        string WithMac(string url, long size, string sha) =>
            plain[..^1] + MacEntry(url, size, sha);

        // Windows (and Linux) keep the top-level zip, with or without a Mac entry.
        var win = UpdateManifest.Parse(WithMac(macUrl, 999, macHash), out var err, "win-x64");
        if (win == null || win.Url != UpZipUrl || win.Size != 1234 || win.Sha256 != hash)
            Fail($"mac: a Windows build took {win?.Url} ({err})");
        var mac = UpdateManifest.Parse(WithMac(macUrl, 999, macHash), out err, UpdateManifest.MacPlatform);
        if (mac == null || mac.Url != macUrl || mac.Size != 999 || mac.Sha256 != macHash || mac.FileName != $"CallOfSlop-{UpVersion}-mac.zip")
            Fail($"mac: a Mac took {mac?.Url} ({err})");

        // A Mac never falls back to the Windows zip.
        if (UpdateManifest.Parse(plain, out err, UpdateManifest.MacPlatform) != null || !err.StartsWith(UpdateManifest.NoDownloadForPlatform, StringComparison.Ordinal))
            Fail($"mac: a Windows-only release gave a Mac a download ('{err}')");

        // The Mac entry is checked as strictly as the top-level one.
        string[] badUrls = ["https://evil.example/CallOfSlop.zip", $"{UpdateManifest.DownloadPrefix}v1/../x.zip", $"{UpdateManifest.DownloadPrefix}v1/CallOfSlop.exe", "http://github.com/x.zip"];
        var leaks = badUrls.Count(u => UpdateManifest.Parse(WithMac(u, 999, macHash), out _, UpdateManifest.MacPlatform) != null)
            + (UpdateManifest.Parse(WithMac(macUrl, 0, macHash), out _, UpdateManifest.MacPlatform) != null ? 1 : 0)
            + (UpdateManifest.Parse(WithMac(macUrl, 999, "abc"), out _, UpdateManifest.MacPlatform) != null ? 1 : 0);
        if (leaks > 0)
            Fail($"mac: {leaks} bad Mac download entries were accepted");

        // A release with both zips: each size is found by name.
        var release = ReleaseInfo.Parse($$"""{"tag_name":"v{{UpVersion}}","html_url":"{{UpdateManifest.ReleasesPage}}","assets":[{"name":"CallOfSlop-{{UpVersion}}.zip","size":1234,"browser_download_url":"{{UpZipUrl}}"},{"name":"CallOfSlop-{{UpVersion}}-mac.zip","size":999,"browser_download_url":"{{macUrl}}"},{"name":"update.json","size":300,"browser_download_url":"{{UpManifestUrl}}"}]}""", out err);
        if (release == null || release.ZipSize($"CallOfSlop-{UpVersion}.zip") != 1234 || release.ZipSize($"CallOfSlop-{UpVersion}-mac.zip") != 999 || release.ZipSize("nope.zip") != -1)
            Fail($"mac: release zip sizes wrong ({err})");
        Line($"manifests: Windows keeps the top-level zip, a Mac takes platforms.osx-arm64 ({mac?.FileName}), a Windows-only release has nothing for a Mac, {badUrls.Length + 2} bad Mac entries refused");
    }

    private static void MacInstall(string temp)
    {
        // The Mac zip's layout: CallOfSlop/CallOfSlop.app/... plus the READ ME next to the app.
        byte[] MacZip(bool executable)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Add(string name, string text, int mode)
                {
                    var e = zip.CreateEntry(name);
                    e.ExternalAttributes = mode << 16; // Unix permissions, as ditto writes them
                    using var w = new StreamWriter(e.Open());
                    w.Write(text);
                }
                Add("CallOfSlop/CallOfSlop.app/Contents/MacOS/CallOfSlop", "not really a program", executable ? 0b111_101_101 : 0b110_100_100);
                Add("CallOfSlop/CallOfSlop.app/Contents/MacOS/CallOfSlop.dll", "dll", 0b110_100_100);
                Add("CallOfSlop/CallOfSlop.app/Contents/Resources/Maps/a.json", "{}", 0b110_100_100);
                Add("CallOfSlop/CallOfSlop.app/Contents/Info.plist", "<plist/>", 0b110_100_100);
                Add("CallOfSlop/READ ME FIRST.txt", "read me", 0b110_100_100);
            }
            return ms.ToArray();
        }

        // Unzip: finds the .app, and its program is executable even from a zip that lost the bit.
        var zipPath = Path.Combine(temp, "mac.zip");
        File.WriteAllBytes(zipPath, MacZip(executable: false));
        var game = UpdateInstaller.Extract(zipPath, Path.Combine(temp, "unzipped", "CallOfSlop-0.2026.1022.7"));
        var expectApp = Path.Combine(temp, "unzipped", "CallOfSlop-0.2026.1022.7", "CallOfSlop", "CallOfSlop.app");
        if (!SavePaths.SameFolder(game, expectApp))
            Fail($"mac: the unzipped game is {game}, expected {expectApp}");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(Path.Combine(game, UpdateInstaller.AppBinary)) & UnixFileMode.UserExecute) == 0)
            Fail("mac: the unzipped app's program isn't executable");
        if (UpdateInstaller.FindGame(Path.Combine(temp, "unzipped")) != null)
            Fail("mac: FindGame looked deeper than one level");

        // The launch command: "open -n <app>" in the app's folder (never run here).
        var psi = UpdateInstaller.LaunchCommand(game);
        if (psi.FileName != "open" || psi.ArgumentList.Count != 2 || psi.ArgumentList[0] != "-n" || psi.ArgumentList[1] != game || psi.UseShellExecute)
            Fail($"mac: launch command '{psi.FileName} {string.Join(" ", psi.ArgumentList)}'");

        // A whole check + install as a Mac: the Mac zip is downloaded (not the Windows one) and opened.
        UpdateManifest.PlatformOverride = UpdateManifest.MacPlatform;
        var zip = MacZip(executable: true);
        var macUrl = $"{UpdateManifest.DownloadPrefix}v{UpVersion}/CallOfSlop-{UpVersion}-mac.zip";
        var withMac = true;
        var fake = new FakeHttp(req =>
        {
            var url = req.RequestUri!.ToString();
            var manifest = UpManifestJson(UpZipUrl, 4321, new string('b', 64));
            if (withMac)
                manifest = manifest[..^1] + MacEntry(macUrl, zip.Length, UpSha(zip));
            return Task.FromResult(url == UpdateManifest.LatestApi ? UpText(UpReleaseJson(UpManifestUrl, 4321))
                : url == UpManifestUrl ? UpText(manifest)
                : url == macUrl ? UpBytes(zip, req)
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        });
        var current = Path.Combine(temp, "games", "CallOfSlop", "CallOfSlop.app", "Contents", "MacOS");
        Directory.CreateDirectory(current);
        var launched = new List<ProcessStartInfo>();
        var up = new Updater(fake, Path.Combine(temp, "mac-check.txt"), current)
        {
            CanLaunchApp = true,
            Launcher = p =>
            {
                launched.Add(p); // never actually started
                return true;
            },
        };
        if (up.Platform != UpdateManifest.MacPlatform)
            Fail($"mac: the updater plays {up.Platform}");
        UpWait(up.Check(manual: true), "the Mac check");
        var v = up.Snapshot();
        if (v.Available?.Url != macUrl)
        {
            Fail($"mac: the Mac check found {v.Available?.Url} ({v.State} '{v.Message}')");
            return;
        }
        UpWait(up.Install(), "the Mac install");
        v = up.Snapshot();
        var target = Path.Combine(temp, "games", $"CallOfSlop-{UpVersion}", "CallOfSlop", "CallOfSlop.app");
        var opened = launched.FirstOrDefault();
        if (v.State != UpdateState.Launching || opened?.FileName != "open" || !SavePaths.SameFolder(opened.ArgumentList[^1], target) || fake.Asked.Contains(UpZipUrl))
            Fail($"mac: the Mac install ended {v.State} '{v.Message}', opened {opened?.FileName} {string.Join(" ", opened?.ArgumentList ?? [])}, Windows zip asked {fake.Asked.Contains(UpZipUrl)}");

        // A Windows-only release: a Mac is up to date (nothing to install), not failed, and downloads nothing.
        withMac = false;
        var up2 = new Updater(fake, Path.Combine(temp, "mac-check2.txt"), current);
        UpWait(up2.Check(manual: true), "the Windows-only check");
        var v2 = up2.Snapshot();
        if (v2.State != UpdateState.UpToDate || v2.Available != null || !v2.Message.Contains("no download for this computer"))
            Fail($"mac: a Windows-only release gave a Mac {v2.State} '{v2.Message}'");
        Line($"install: Mac zip unzipped to {Path.GetRelativePath(temp, target)}, 'open -n' (not run), Windows zip never fetched; a Windows-only release: {v2.State} '{v2.Message}'");
    }
}
