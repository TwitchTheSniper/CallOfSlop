using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace CallOfSlop;

/// <summary>
/// Installing an update: download the zip (resuming a partial download), check its size and SHA-256 against the
/// manifest, unzip it into a NEW folder next to the current build (e.g. <c>CallOfSlop-0.2026.1022.5</c>), then start the
/// new CallOfSlop.exe. The running build is never overwritten and the old folder is never deleted. Saves live in
/// %APPDATA%\CallOfSlop (SavePaths.cs), so the new build sees them straight away.
/// <para>
/// Mark of the Web: files the game writes itself (an HttpClient download, files unzipped here) get no Zone.Identifier
/// stream, unlike a zip saved by a web browser and unzipped by Explorer, so SmartScreen doesn't stop the new exe.
/// To be sure, any Zone.Identifier stream on the downloaded zip or the unzipped files is removed
/// (<see cref="RemoveMarkOfTheWeb"/>). The download is only trusted after its SHA-256 matches the manifest.
/// </para>
/// </summary>
public static class UpdateInstaller
{
    public const string ExeName = "CallOfSlop.exe";

    /// <summary>A download that gets no data for this long fails (the partial file stays for Retry).</summary>
    public static TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The folder new builds go in: the current build's parent, or, for the tester zip layout
    /// (<c>...\CallOfSlop-&lt;version&gt;\CallOfSlop\CallOfSlop.exe</c>), the folder the zip was unzipped in.
    /// </summary>
    public static string InstallRoot(string exeFolder)
    {
        var self = Path.TrimEndingDirectorySeparator(Path.GetFullPath(exeFolder));
        var parent = Path.GetDirectoryName(self);
        if (parent == null)
            return self;
        if (Path.GetFileName(self).Equals("CallOfSlop", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(parent).StartsWith("CallOfSlop", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(parent) is { } grand)
            return grand;
        return parent;
    }

    /// <summary>Where builds go when the install root can't be written to (e.g. Program Files).</summary>
    public static string FallbackRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) is { Length: > 0 } l ? l : Path.GetTempPath(), "CallOfSlop", "Builds");

    /// <summary>A free folder name for the new build in <paramref name="root"/>: CallOfSlop-&lt;version&gt;, then -2, -3...</summary>
    public static string TargetFolder(string root, Version version)
    {
        var name = $"CallOfSlop-{version}";
        var path = Path.Combine(root, name);
        for (var i = 2; Directory.Exists(path) || File.Exists(path); i++)
            path = Path.Combine(root, $"{name}-{i}");
        return path;
    }

    /// <summary>The partial / finished download for a manifest (in the install root; deleted once unzipped).</summary>
    public static string DownloadPath(string root, UpdateManifest m) => Path.Combine(root, $"CallOfSlop-{m.Version}.zip.download");

    /// <summary>Can we create files in <paramref name="dir"/>?</summary>
    public static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $"callofslop-write-probe-{Environment.ProcessId}.tmp");
            File.WriteAllBytes(probe, []);
            File.Delete(probe); // our own probe file
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Room for the download plus the unzipped build (about 3x the zip, plus a margin). Null when there's enough.</summary>
    public static string? SpaceProblem(string root, long zipSize, long alreadyDownloaded)
    {
        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
            var need = (zipSize - alreadyDownloaded) + zipSize * 3 + 64L * 1024 * 1024;
            return free >= need ? null : $"Not enough disk space: the update needs about {need / (1024 * 1024)} MB free next to the game, {free / (1024 * 1024)} MB is free.";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null; // can't tell (network drive...): try anyway, the write reports a real failure
        }
    }

    /// <summary>
    /// Downloads (or finishes downloading) the zip to <paramref name="path"/>. A partial file is resumed with an HTTP
    /// Range request; a server that ignores the range restarts it. Never writes more than the manifest's size.
    /// </summary>
    public static async Task DownloadAsync(HttpClient http, UpdateManifest m, string path, Action<long, long> progress, CancellationToken ct)
    {
        long have = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (have > m.Size)
        {
            File.Delete(path); // our own partial download, too long to be right
            have = 0;
        }
        if (have == m.Size)
        {
            progress(have, m.Size);
            return;
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, m.Url);
        if (have > 0)
            req.Headers.Range = new RangeHeaderValue(have, null);
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(path);
            throw new IOException("the partial download didn't match the server's file; it will start over");
        }
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"the download failed (HTTP {(int)resp.StatusCode})");
        var resume = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
        if (!resume)
            have = 0;
        // Only ever HTTPS (redirects to http aren't followed by HttpClient; GitHub serves assets from its own CDN).
        if (resp.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
            throw new HttpRequestException("the download was redirected away from HTTPS");
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = new FileStream(path, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        progress(have, m.Size);
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        while (true)
        {
            stall.CancelAfter(StallTimeout); // no data for this long: give up (Retry resumes)
            int n;
            try
            {
                n = await src.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"the download stalled ({have} of {m.Size} bytes); Retry continues it");
            }
            if (n == 0)
                break;
            if (have + n > m.Size)
                throw new IOException("the download is bigger than the release says");
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            have += n;
            progress(have, m.Size);
        }
        if (have != m.Size)
            throw new IOException($"the download stopped early ({have} of {m.Size} bytes); Retry continues it");
    }

    /// <summary>Checks a downloaded zip against the manifest. Null when it matches, otherwise what's wrong.</summary>
    public static string? Verify(string path, UpdateManifest m)
    {
        if (!File.Exists(path))
            return "the download is missing";
        var size = new FileInfo(path).Length;
        if (size != m.Size)
            return $"the download is {size} bytes, the release says {m.Size}";
        using var fs = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        return hash == m.Sha256 ? null : "the download's SHA-256 doesn't match the release (damaged or tampered with)";
    }

    /// <summary>
    /// Unzips into <paramref name="target"/> (via a ".partial" folder renamed at the end, so a half-unzipped build never
    /// looks finished). Refuses entries that would land outside the folder. Returns the new CallOfSlop.exe (or, on a
    /// build without one, the folder holding CallOfSlop.dll).
    /// </summary>
    public static string Extract(string zipPath, string target)
    {
        var staging = target + ".partial";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true); // our own leftover from an interrupted install
        Directory.CreateDirectory(staging);
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var e in zip.Entries)
            {
                var rel = e.FullName.Replace('\\', '/');
                if (rel.Length == 0 || rel.StartsWith('/') || rel.Contains(':') || rel.Split('/').Any(p => p == ".."))
                    throw new IOException($"the zip has a bad path: {UpdateManifest.CleanNotes(rel, 80)}");
                var dest = Path.GetFullPath(Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new IOException($"the zip has a path outside its folder: {UpdateManifest.CleanNotes(rel, 80)}");
                if (rel.EndsWith('/'))
                {
                    Directory.CreateDirectory(dest);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, overwrite: false);
            }
        }
        Directory.Move(staging, target);
        RemoveMarkOfTheWeb(target);
        return FindGame(target) ?? throw new IOException($"the update has no {ExeName}");
    }

    /// <summary>CallOfSlop.exe in the folder or one level down (the zip holds a CallOfSlop folder); dll-only builds count too.</summary>
    public static string? FindGame(string folder)
    {
        foreach (var dir in new[] { folder }.Concat(SafeDirs(folder)))
        {
            if (File.Exists(Path.Combine(dir, ExeName)))
                return Path.Combine(dir, ExeName);
        }
        foreach (var dir in new[] { folder }.Concat(SafeDirs(folder)))
            if (File.Exists(Path.Combine(dir, "CallOfSlop.dll")) && !OperatingSystem.IsWindows())
                return Path.Combine(dir, "CallOfSlop.dll");
        return null;
    }

    private static string[] SafeDirs(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Windows: deletes the Zone.Identifier stream (Mark of the Web) from a file, or from every file in a folder.
    /// Nothing to do elsewhere. Returns how many were removed.
    /// </summary>
    public static int RemoveMarkOfTheWeb(string path)
    {
        if (!OperatingSystem.IsWindows())
            return 0;
        var removed = 0;
        IEnumerable<string> files = File.Exists(path) ? new[] { path }
            : Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : Array.Empty<string>();
        foreach (var f in files)
        {
            try
            {
                var stream = f + ":Zone.Identifier";
                if (File.Exists(stream))
                {
                    File.Delete(stream);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                // Not NTFS, or not allowed: nothing to remove.
            }
        }
        return removed;
    }

    /// <summary>How the new build is started: its own exe, in its own folder, with no arguments (built here, run by the caller).</summary>
    public static ProcessStartInfo LaunchCommand(string game)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(game))!;
        if (game.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = dir, UseShellExecute = false };
            psi.ArgumentList.Add(game);
            return psi;
        }
        return new ProcessStartInfo(game) { WorkingDirectory = dir, UseShellExecute = false };
    }
}
