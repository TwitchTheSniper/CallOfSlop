using System.IO.Compression;

namespace CallOfSlop;

/// <summary>
/// <c>CallOfSlop.exe --verify-update &lt;update.json&gt; &lt;zip&gt;</c>: the Release workflow runs this on the package it
/// just built, so a release is only published if the game's own updater accepts it: the manifest parses and points at
/// this repo, the size and SHA-256 match, the zip unzips safely into a new folder with CallOfSlop.exe in it, and no
/// personal files got in. Output goes to callofslop.log; the exit code is the number of problems.
/// </summary>
public static class UpdateVerifyCli
{
    /// <summary>Files that must never be in a shared build (players' saves and logs).</summary>
    public static readonly string[] PersonalFiles =
        ["settings.txt", "progress.txt", "loadout.txt", "workbench.txt", "audio_mix.json", "portable.txt", "callofslop.log", "selftest.log", "update_check.txt"];

    public static int Run(string manifestFile, string zipFile)
    {
        var problems = 0;
        void Problem(string s)
        {
            problems++;
            Console.WriteLine($"[verify-update] PROBLEM: {s}");
        }
        var m = UpdateManifest.Parse(File.ReadAllText(manifestFile), out var why);
        if (m == null)
        {
            Problem($"update.json rejected: {why}");
            return problems;
        }
        Console.WriteLine($"[verify-update] manifest v{m.Version} build {m.Build} protocol {m.Protocol} size {m.Size} sha256 {m.Sha256} url {m.Url}");
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
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true); // our own temp folder
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Temp; never mind.
            }
        }
        Console.WriteLine($"[verify-update] DONE: {problems} problem(s)");
        return problems;
    }
}
