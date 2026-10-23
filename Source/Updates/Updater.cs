using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace CallOfSlop;

/// <summary>Where the updater is (the main menu banner and Settings > Player show it).</summary>
public enum UpdateState
{
    Idle, // not checked yet (or checks are off)
    Checking,
    UpToDate,
    Available, // a newer release: Install / Later / Skip
    Downloading,
    Installing, // verifying and unzipping
    Launching,
    Failed, // with Error; Retry and the release page link
}

/// <summary>
/// Checks GitHub for a newer release and installs it from inside the game (UpdateInstaller.cs). Everything runs on
/// background tasks: startup never waits for it, and being offline just means "couldn't check". The UI reads
/// <see cref="Snapshot"/> each frame.
/// <para>
/// Network use: one unauthenticated call to the GitHub API (the newest release) and one small download (its update.json)
/// per check; checks happen at most once per launch on their own, and not again for 6 hours (the cache in the save
/// folder), plus whenever the player presses "Check for updates" (at most once a minute).
/// </para>
/// </summary>
public sealed class Updater
{
    /// <summary>What the UI shows.</summary>
    public readonly record struct View(UpdateState State, string Message, UpdateManifest? Available, ReleaseInfo? Release, long Done, long Total,
        DateTime CheckedUtc, string? InstalledTo, bool Loud);

    private readonly object _gate = new();
    private readonly HttpClient _http;
    private readonly string _cachePath;
    private readonly string _exeFolder;
    private readonly Func<DateTime> _clock;
    private UpdateState _state = UpdateState.Idle;
    private string _message = "";
    private UpdateManifest? _available;
    private ReleaseInfo? _release;
    private long _done, _total;
    private DateTime _checkedUtc = DateTime.MinValue;
    private string? _installedTo;
    private bool _loud; // the player asked (manual check, install): failures are shown, not just logged
    private Task? _task;
    private CancellationTokenSource? _cts;

    /// <summary>Starts the new build (the self-test swaps in a fake so nothing is launched). True when it started.</summary>
    public Func<ProcessStartInfo, bool> Launcher = psi =>
    {
        using var p = Process.Start(psi);
        return p != null;
    };

    /// <summary>Can this platform start CallOfSlop.exe (Windows; the self-test turns it on with a fake <see cref="Launcher"/>)?</summary>
    public bool CanLaunchExe = OperatingSystem.IsWindows();

    /// <summary>The installed build started: the game should save and quit (Program polls it).</summary>
    public volatile bool QuitRequested;

    /// <summary>Where new builds go (null: next to this one, see UpdateInstaller.InstallRoot).</summary>
    public string? InstallRootOverride;

    /// <summary>Only for the game itself (Program creates it at startup).</summary>
    public static Updater? Shared;

    public Updater(HttpMessageHandler? handler, string cachePath, string exeFolder, Func<DateTime>? clock = null)
    {
        _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan; // each call sets its own (a short one for checks)
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CallOfSlop", BuildInfo.IsRelease ? BuildInfo.Version : "dev"));
        _cachePath = cachePath;
        _exeFolder = exeFolder;
        _clock = clock ?? (() => DateTime.UtcNow);
        var cache = UpdateCache.Load(cachePath);
        _checkedUtc = cache.CheckedUtc;
    }

    public View Snapshot()
    {
        lock (_gate)
            return new View(_state, _message, _available, _release, _done, _total, _checkedUtc, _installedTo, _loud);
    }

    /// <summary>Is a check or an install running?</summary>
    public bool Busy => _task is { IsCompleted: false };

    private void Set(UpdateState state, string message)
    {
        lock (_gate)
            (_state, _message) = (state, message);
        Console.WriteLine($"[update] {state}: {message}");
    }

    /// <summary>
    /// Startup / "Check for updates": returns at once, the check runs in the background. Startup checks use the cache
    /// when it's under 6 hours old; manual checks ask GitHub unless the last ask was under a minute ago.
    /// </summary>
    public Task Check(bool manual)
    {
        if (Busy)
            return _task!;
        _loud = manual;
        Set(UpdateState.Checking, manual ? "Checking for updates..." : "Checking for updates in the background...");
        return _task = Task.Run(() => CheckAsync(manual));
    }

    private async Task CheckAsync(bool manual)
    {
        try
        {
            var cache = UpdateCache.Load(_cachePath);
            var now = _clock();
            if (cache.ShouldQuery(now, manual))
                await QueryAsync(cache, now).ConfigureAwait(false);
            else
                Console.WriteLine($"[update] using the check from {cache.CheckedUtc:u} (GitHub is asked at most every {UpdateCache.MaxAge.TotalHours:0} h)");
            lock (_gate)
                _checkedUtc = cache.CheckedUtc;
            Evaluate(cache);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or OperationCanceledException)
        {
            Set(UpdateState.Failed, $"Couldn't check for updates ({Short(ex)}). You can play as normal; try again later.");
        }
        catch (Exception ex)
        {
            Set(UpdateState.Failed, $"Update check failed: {Short(ex)}");
        }
    }

    /// <summary>Asks GitHub (the API, then the release's update.json) and stores the answers in the cache.</summary>
    private async Task QueryAsync(UpdateCache cache, DateTime now)
    {
        cache.CheckedUtc = now; // even a failed ask counts: never hammer the API
        cache.Save(_cachePath);
        lock (_gate)
            _checkedUtc = now;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var req = new HttpRequestMessage(HttpMethod.Get, UpdateManifest.LatestApi);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (cache.ETag.Length > 0 && cache.ReleaseJson.Length > 0)
            req.Headers.TryAddWithoutValidation("If-None-Match", cache.ETag);
        using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotModified)
        {
            Console.WriteLine("[update] GitHub: no new release since the last check (304)");
            if (cache.ManifestJson.Length > 0)
                return;
        }
        else if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            (cache.ReleaseJson, cache.ManifestJson, cache.ETag) = ("", "", "");
            cache.Save(_cachePath);
            return; // no releases yet
        }
        else if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            throw new HttpRequestException("GitHub's rate limit; it resets within the hour");
        }
        else
        {
            resp.EnsureSuccessStatusCode();
            cache.ReleaseJson = await ReadCapped(resp, 300_000, cts.Token).ConfigureAwait(false);
            cache.ETag = resp.Headers.ETag?.ToString() is { Length: < 200 } etag ? etag : "";
        }
        var release = ReleaseInfo.Parse(cache.ReleaseJson, out var why);
        if (release == null)
        {
            cache.ManifestJson = "";
            cache.Save(_cachePath);
            throw new IOException(why);
        }
        using var mreq = new HttpRequestMessage(HttpMethod.Get, release.ManifestUrl);
        using var mresp = await _http.SendAsync(mreq, cts.Token).ConfigureAwait(false);
        mresp.EnsureSuccessStatusCode();
        cache.ManifestJson = await ReadCapped(mresp, 64_000, cts.Token).ConfigureAwait(false);
        cache.Save(_cachePath);
    }

    private static async Task<string> ReadCapped(HttpResponseMessage resp, int max, CancellationToken ct)
    {
        if (resp.Content.Headers.ContentLength > max)
            throw new IOException("the answer is too big");
        var s = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return s.Length <= max ? s : throw new IOException("the answer is too big");
    }

    /// <summary>Turns the cached answers into a state: up to date, or an update (with its manifest checked).</summary>
    private void Evaluate(UpdateCache cache)
    {
        if (cache.ReleaseJson.Length == 0)
        {
            Set(UpdateState.UpToDate, "No releases published yet.");
            return;
        }
        var release = ReleaseInfo.Parse(cache.ReleaseJson, out var why);
        var manifest = release == null ? null : UpdateManifest.Parse(cache.ManifestJson, out why);
        if (release == null || manifest == null)
        {
            Set(UpdateState.Failed, $"The newest release can't be used by the updater ({why}).");
            return;
        }
        if (BuildInfo.ParseRelease(release.Tag) is { } tagVersion && tagVersion != manifest.Version)
        {
            Set(UpdateState.Failed, $"The newest release's tag ({release.Tag}) doesn't match its update.json (v{manifest.Version}).");
            return;
        }
        if (release.ZipAssetName == manifest.FileName && release.ZipAssetSize > 0 && release.ZipAssetSize != manifest.Size)
        {
            Set(UpdateState.Failed, "The newest release's zip isn't the size its update.json says.");
            return;
        }
        lock (_gate)
            (_available, _release) = (manifest, release);
        if (BuildInfo.IsNewer(manifest.Version, BuildInfo.ReleaseVersion))
            Set(UpdateState.Available, $"Update available: v{manifest.Version}");
        else
            Set(UpdateState.UpToDate, $"You have the newest build (v{BuildInfo.Version}).");
    }

    /// <summary>The "Install" button: download, verify, unzip next to this build, start it. Returns at once.</summary>
    public Task Install()
    {
        UpdateManifest? m;
        lock (_gate)
            m = _available;
        if (Busy || m == null)
            return _task ?? Task.CompletedTask;
        _loud = true;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        Set(UpdateState.Downloading, $"Downloading v{m.Version}...");
        return _task = Task.Run(() => InstallAsync(m, ct));
    }

    /// <summary>Stops a download (the partial file is kept, so Install resumes it).</summary>
    public void Cancel() => _cts?.Cancel();

    private async Task InstallAsync(UpdateManifest m, CancellationToken ct)
    {
        string? zip = null;
        try
        {
            if (!m.InstallableHere)
            {
                Set(UpdateState.Failed, "This update needs a newer updater: download it from the release page.");
                return;
            }
            var root = InstallRootOverride ?? UpdateInstaller.InstallRoot(_exeFolder);
            if (!UpdateInstaller.CanWrite(root))
            {
                Console.WriteLine($"[update] can't write next to the game in {root}; installing to {UpdateInstaller.FallbackRoot()} instead");
                root = UpdateInstaller.FallbackRoot();
                if (!UpdateInstaller.CanWrite(root))
                    throw new IOException($"can't write to {root}");
            }
            zip = UpdateInstaller.DownloadPath(root, m);
            var have = File.Exists(zip) ? new FileInfo(zip).Length : 0;
            if (UpdateInstaller.SpaceProblem(root, m.Size, have) is { } space)
            {
                Set(UpdateState.Failed, space);
                return;
            }
            lock (_gate)
                (_done, _total) = (have, m.Size);
            await UpdateInstaller.DownloadAsync(_http, m, zip, (d, t) =>
            {
                lock (_gate)
                    (_done, _total) = (d, t);
            }, ct).ConfigureAwait(false);

            Set(UpdateState.Installing, "Checking the download...");
            UpdateInstaller.RemoveMarkOfTheWeb(zip);
            if (UpdateInstaller.Verify(zip, m) is { } bad)
            {
                File.Delete(zip); // our own download, and it's wrong: start over next time
                Set(UpdateState.Failed, $"The update was rejected: {bad}. Retry downloads it again.");
                return;
            }
            var target = UpdateInstaller.TargetFolder(root, m.Version);
            Set(UpdateState.Installing, $"Unzipping to {target}...");
            var game = UpdateInstaller.Extract(zip, target, m.Version);
            File.Delete(zip); // our own download, now unzipped
            lock (_gate)
                _installedTo = target;
            Launch(game, m);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Set(UpdateState.Available, "Download paused. Install continues where it stopped.");
        }
        catch (Exception ex)
        {
            Set(UpdateState.Failed, $"The update didn't install ({Short(ex)}). Retry, or get it from the release page.");
        }
    }

    private void Launch(string game, UpdateManifest m)
    {
        if (!CanLaunchExe && game.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            Set(UpdateState.Failed, $"Installed v{m.Version} in {Path.GetDirectoryName(game)}; start it yourself (this isn't Windows).");
            return;
        }
        Set(UpdateState.Launching, $"Starting v{m.Version}...");
        bool started;
        try
        {
            started = Launcher(UpdateInstaller.LaunchCommand(game));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Set(UpdateState.Failed, $"Installed v{m.Version} in {Path.GetDirectoryName(game)} but couldn't start it ({Short(ex)}). Start CallOfSlop.exe there yourself.");
            return;
        }
        if (!started)
        {
            Set(UpdateState.Failed, $"Installed v{m.Version} in {Path.GetDirectoryName(game)} but couldn't start it. Start CallOfSlop.exe there yourself.");
            return;
        }
        Console.WriteLine($"[update] started {game}; this build quits now (the new build removes old versions once it reaches its menu, see UpdateCleanup.cs)");
        QuitRequested = true;
    }

    private static string Short(Exception ex)
    {
        var m = ex is TaskCanceledException ? "timed out" : ex.Message;
        return UpdateManifest.CleanNotes(m, 160);
    }
}
