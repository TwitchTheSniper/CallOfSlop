using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Part of the "Props" section: the generated gallery maps (Maps/Dev/PropGallery2.json, PropGallery3.json,
/// AnimatedProps.json, MilitaryProps.json, PropGalleryRetail.json, SpaceProps.json, PropGalleryMore.json) are exactly
/// what tools/props/gallery.py produces from the Props folders, so a new or resized prop shows up on its gallery after a
/// run of the tool and nobody edits those maps by hand. Runs the tool with --check when the source tree and a Python 3
/// are at hand (the agent container, CI, a developer's checkout); a packaged build just says so.
/// </summary>
public static partial class SelfTest
{
    private const string GalleryTool = "tools/props/gallery.py";

    private static void CheckGeneratedGalleries()
    {
        var root = FindRepoRoot();
        if (root == null)
        {
            Line($"generated galleries: {GalleryTool} not found above {AppContext.BaseDirectory} (a packaged build), check skipped");
            return;
        }
        string? output = null;
        var code = -1;
        var tried = new List<string>();
        foreach (var python in OperatingSystem.IsWindows() ? new[] { "python", "python3", "py" } : new[] { "python3", "python" })
        {
            tried.Add(python);
            try
            {
                var psi = new ProcessStartInfo(python)
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add("-I");
                psi.ArgumentList.Add(Path.Combine(root, GalleryTool));
                psi.ArgumentList.Add("--check");
                using var p = Process.Start(psi);
                if (p == null)
                    continue;
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(60_000))
                {
                    p.Kill(true);
                    Fail($"generated galleries: {python} {GalleryTool} --check didn't finish in 60 s");
                    return;
                }
                output = (stdout.Result + stderr.Result).Trim();
                code = p.ExitCode;
                break;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                // no such interpreter: try the next name
            }
        }
        if (output == null)
        {
            Line($"generated galleries: no Python 3 found ({string.Join(", ", tried)}), check skipped (run `python3 -I {GalleryTool} --check` yourself)");
            return;
        }
        var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        Line($"generated galleries: {GalleryTool} --check: {first}");
        if (code != 0)
            Fail($"the generated gallery maps don't match the Props folders: {first}. Run `python3 -I {GalleryTool}` and commit the maps it rewrites "
                 + "(then re-pin their fingerprints in SelfTestPackAssets.cs from this log)");
    }

    /// <summary>The checkout holding this build: the folder above the exe that has <see cref="GalleryTool"/>, or null.</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, GalleryTool)) && Directory.Exists(Path.Combine(dir.FullName, "Props")))
                return dir.FullName;
        return null;
    }
}
