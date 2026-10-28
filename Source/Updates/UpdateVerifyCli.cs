using System.IO.Compression;

namespace CallOfSlop;

/// <summary>
/// <c>CallOfSlop.exe --verify-update &lt;update.json&gt; &lt;zip&gt;</c>: the Release workflow runs this on the package it
/// just built, so a release is only published if the game's own updater accepts it: the manifest parses and points at
/// this repo, the size and SHA-256 match, the zip unzips safely into a new folder with CallOfSlop.exe in it, and no
/// personal files got in. Output goes to callofslop.log; the exit code is the number of problems.
/// <para>
/// <c>--platform osx-arm64</c> checks the Mac zip instead (release.yml's Mac job): the manifest's osx-arm64 entry, and a
/// CallOfSlop.app with an executable Contents/MacOS/CallOfSlop, the program beside it, the data in Contents/Resources
/// and READ ME FIRST.txt / NOTICE.txt next to the app.
/// </para>
/// </summary>
public static class UpdateVerifyCli
{
    /// <summary>Files that must never be in a shared build (players' saves and logs).</summary>
    public static readonly string[] PersonalFiles =
        ["settings.txt", "progress.txt", "loadout.txt", "workbench.txt", "audio_mix.json", "portable.txt", "callofslop.log", "selftest.log", "update_check.txt",
         "steam_appid.txt"]; // a developer's Steam file: a package with it would start Steam as that app (SteamPlatform.cs)

    public static int Run(string manifestFile, string zipFile, string? platform = null)
    {
        platform ??= UpdateManifest.CurrentPlatform;
        var problems = 0;
        void Problem(string s)
        {
            problems++;
            Console.WriteLine($"[verify-update] PROBLEM: {s}");
        }
        var m = UpdateManifest.Parse(File.ReadAllText(manifestFile), out var why, platform);
        if (m == null)
        {
            Problem($"update.json rejected: {why}");
            return problems;
        }
        Console.WriteLine($"[verify-update] {platform} manifest v{m.Version} build {m.Build} protocol {m.Protocol} size {m.Size} sha256 {m.Sha256} url {m.Url}");
        if (m.Protocol != ServerInfo.ProtocolVersion)
            Problem($"manifest protocol {m.Protocol}, this build has {ServerInfo.ProtocolVersion}");
        if (!m.InstallableHere)
            Problem($"manifest needs updater {m.MinUpdater}, this build has {BuildInfo.UpdaterVersion}");
        if (Path.GetFileName(zipFile) != m.FileName)
            Problem($"zip is called {Path.GetFileName(zipFile)}, the manifest's URL says {m.FileName}");
        if (UpdateInstaller.Verify(zipFile, m) is { } bad)
            Problem(bad);
        using (var zip = ZipFile.OpenRead(zipFile))
            foreach (var e in zip.Entries)
                if (PersonalFiles.Contains(e.Name, StringComparer.OrdinalIgnoreCase) || e.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    Problem($"personal file in the zip: {e.FullName}");
        var root = Path.Combine(Path.GetTempPath(), $"callofslop-verify-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var target = UpdateInstaller.TargetFolder(root, m.Version);
            var game = UpdateInstaller.Extract(zipFile, target);
            Console.WriteLine($"[verify-update] unzipped to {target}; game at {Path.GetRelativePath(root, game)}");
            var dir = Path.GetDirectoryName(game)!;
            if (game.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                CheckApp(game, m, Problem);
                foreach (var need in new[] { "NOTICE.txt", "READ ME FIRST.txt" })
                    if (!File.Exists(Path.Combine(dir, need)))
                        Problem($"{need} missing next to {UpdateInstaller.AppName}");
                return Done(problems, root);
            }
            if (platform == UpdateManifest.MacPlatform)
                Problem($"the {platform} zip has no {UpdateInstaller.AppName}");
            if (OperatingSystem.IsWindows())
            {
                // The packaged exe must carry the manifest's version (it's what the game compares after updating).
                var stamped = System.Diagnostics.FileVersionInfo.GetVersionInfo(game).ProductVersion ?? "";
                Console.WriteLine($"[verify-update] packaged exe version '{stamped}' (verifier: {BuildInfo.Label})");
                if (BuildInfo.Parse(stamped).Version != m.Version)
                    Problem($"the packaged exe says '{stamped}', the manifest v{m.Version}");
            }
            foreach (var need in new[] { "NOTICE.txt", "READ ME FIRST.txt", "CallOfSlop.dll" })
                if (!File.Exists(Path.Combine(dir, need)))
                    Problem($"{need} missing next to the exe");
            if (!Directory.Exists(Path.Combine(dir, "Maps")))
                Problem("Maps folder missing");
        }
        catch (Exception ex)
        {
            Problem($"unzip failed: {ex.Message}");
        }
        return Done(problems, root);
    }

    private static int Done(int problems, string root)
    {
        try
        {
            Directory.Delete(root, recursive: true); // our own temp folder
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp; never mind.
        }
        Console.WriteLine($"[verify-update] DONE: {problems} problem(s)");
        return problems;
    }

    /// <summary>The Mac bundle: Info.plist with this version, an executable program, the program's files and the data.</summary>
    private static void CheckApp(string app, UpdateManifest m, Action<string> problem)
    {
        var bin = Path.Combine(app, UpdateInstaller.AppBinary);
        var macOs = Path.GetDirectoryName(bin)!;
        var resources = Path.Combine(app, "Contents", "Resources");
        if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(bin) & UnixFileMode.UserExecute) == 0)
            problem($"{UpdateInstaller.AppBinary} isn't executable after unzipping");
        if (!File.Exists(Path.Combine(macOs, "CallOfSlop.dll")))
            problem("CallOfSlop.dll missing from Contents/MacOS");
        foreach (var data in new[] { "Maps", "Weapons", "Parts", "Props" })
            if (!Directory.Exists(Path.Combine(resources, data)))
                problem($"{data} folder missing from Contents/Resources");
        if (AppFolders.DataFor(macOs + Path.DirectorySeparatorChar, Directory.Exists) != resources + Path.DirectorySeparatorChar)
            problem("the game wouldn't find its data in Contents/Resources");
        var plist = Path.Combine(app, "Contents", "Info.plist");
        if (!File.Exists(plist))
        {
            problem("Contents/Info.plist missing");
            return;
        }
        var text = File.ReadAllText(plist);
        Console.WriteLine($"[verify-update] {UpdateInstaller.AppName}: Info.plist {text.Length} chars");
        if (!text.Contains($"<string>{m.Version}</string>", StringComparison.Ordinal))
            problem($"Info.plist doesn't carry v{m.Version}");
        if (!text.Contains("<string>CallOfSlop</string>", StringComparison.Ordinal))
            problem("Info.plist doesn't name CallOfSlop as the executable");
    }
}
