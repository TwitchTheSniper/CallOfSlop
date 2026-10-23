using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The updater (Source/Updates): version stamps and comparison, manifest and release parsing with the security checks,
/// the 6-hour cache and the manual-check gap, a whole check and a whole install against a fake GitHub (an in-memory
/// HttpMessageHandler: no network), resuming, hash and size rejection, unsafe zips, the launch command (never run),
/// the skip-version setting, build info in the server browser, and that a check never blocks the caller.
/// Everything happens in temp folders.
/// </summary>
public static partial class SelfTest
{
    /// <summary>A fake web server: answers each request from a function, and remembers what was asked.</summary>
    private sealed class FakeHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public readonly List<string> Asked = new();
        public readonly List<string> Ranges = new();
        public readonly List<string> ETags = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Asked)
            {
                Asked.Add(request.RequestUri!.ToString());
                Ranges.Add(request.Headers.Range?.ToString() ?? "");
                ETags.Add(request.Headers.TryGetValues("If-None-Match", out var v) ? string.Join(",", v) : "");
            }
            return answer(request);
        }
    }

    private const string UpVersion = "0.2026.1022.7";
    private static readonly string UpZipUrl = $"{UpdateManifest.DownloadPrefix}v{UpVersion}/CallOfSlop-{UpVersion}.zip";
    private static readonly string UpManifestUrl = $"{UpdateManifest.DownloadPrefix}v{UpVersion}/update.json";

    private static string UpManifestJson(string url, long size, string sha256, int minUpdater = 1, string version = UpVersion) =>
        $$"""{"format":1,"version":"{{version}}","build":7,"sha":"0123456789abcdef0123456789abcdef01234567","protocol":{{ServerInfo.ProtocolVersion}},"url":"{{url}}","size":{{size}},"sha256":"{{sha256}}","notes":"Faster bots.\nNew map!","minUpdater":{{minUpdater}}}""";

    private static string UpReleaseJson(string manifestUrl, long zipSize = -1) =>
        $$"""{"tag_name":"v{{UpVersion}}","html_url":"{{UpdateManifest.ReleasesPage}}/tag/v{{UpVersion}}","body":"Release notes","assets":[{"name":"CallOfSlop-{{UpVersion}}.zip","size":{{zipSize}},"browser_download_url":"{{UpZipUrl}}"},{"name":"update.json","size":300,"browser_download_url":"{{manifestUrl}}"}]}""";

    private static HttpResponseMessage UpText(string text, HttpStatusCode code = HttpStatusCode.OK, string? etag = null)
    {
        var r = new HttpResponseMessage(code) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        if (etag != null)
            r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return r;
    }

    private static HttpResponseMessage UpBytes(byte[] data, HttpRequestMessage req, bool honourRange = true)
    {
        if (honourRange && req.Headers.Range?.Ranges.FirstOrDefault() is { From: { } from } && from < data.Length)
        {
            var part = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[(int)from..]) };
            part.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, data.Length - 1, data.Length);
            return part;
        }
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
    }

    private static byte[] UpMakeZip(params (string Name, string Text)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(text);
            }
        return ms.ToArray();
    }

    private static string UpSha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static bool UpWait(Task t, string what)
    {
        if (t.Wait(TimeSpan.FromSeconds(20)))
            return true;
        Fail($"updates: {what} didn't finish in 20 s");
        return false;
    }

    private static void CheckUpdates()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-updates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            UpVersions();
            UpManifests();
            UpCache(temp);
            UpCheckFlow(temp);
            UpInstallFlow(temp);
            UpSettings(temp);
            UpServerBrowser();
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

    // ---- Versions ---------------------------------------------------------------------------------
    private static void UpVersions()
    {
        var (v, sha) = BuildInfo.Parse("0.2026.1022.5+0123456789abcdef0123456789abcdef01234567");
        if (v?.ToString() != "0.2026.1022.5" || sha != "0123456789abcdef0123456789abcdef01234567")
            Fail($"updates: release stamp parsed as {v} / {sha}");
        if (BuildInfo.Parse("1.0.0+abcdef0").Version != null || BuildInfo.Parse("1.0.0").Version != null || BuildInfo.Parse("").Version != null)
            Fail("updates: the SDK's default 1.0.0 (a dev build) counts as a release");
        if (BuildInfo.Parse("0.2026.105.3+abc.1234567abc").Sha != "1234567abc")
            Fail("updates: a doubled sha suffix isn't read");
        string[] bad = ["0.2026.1300.1", "0.2026.99.1", "1.2026.1022.1", "0.1999.1022.1", "0.2026.1022", "banana", "0.2026.1022.-1"];
        foreach (var b in bad)
            if (BuildInfo.ParseRelease(b) != null)
                Fail($"updates: '{b}' accepted as a release version");
        if (BuildInfo.ParseRelease("v0.2026.105.3")?.ToString() != "0.2026.105.3")
            Fail("updates: a tag with a leading v isn't read");
        var a = BuildInfo.ParseRelease("0.2026.1022.5")!;
        var newer = new[] { "0.2026.1022.6", "0.2026.1023.1", "0.2027.101.1" }.All(x => BuildInfo.IsNewer(BuildInfo.ParseRelease(x)!, a));
        var notNewer = new[] { "0.2026.1022.5", "0.2026.1022.4", "0.2026.1021.99", "0.2025.1231.500" }.All(x => !BuildInfo.IsNewer(BuildInfo.ParseRelease(x)!, a));
        var devOlder = BuildInfo.IsNewer(a, null);
        if (!newer || !notNewer || !devOlder)
            Fail($"updates: version compare wrong (newer {newer}, not newer {notNewer}, dev build older {devOlder})");
        Line($"versions: this build {BuildInfo.Label} (informational '{BuildInfo.Informational}'), release {BuildInfo.IsRelease}; compare and parse checks done, {bad.Length} bad versions refused");
    }

    // ---- Manifests and release info ----------------------------------------------------------------
    private static void UpManifests()
    {
        var hash = new string('a', 64);
        var good = UpdateManifest.Parse(UpManifestJson(UpZipUrl, 1234, hash), out var err);
        if (good is not { Size: 1234, Build: 7, MinUpdater: 1 } || good.Version.ToString() != UpVersion || good.Sha256 != hash || !good.InstallableHere)
            Fail($"updates: a good manifest didn't parse ({err})");
        if (good != null && (good.Notes != "Faster bots. New map!" || good.FileName != $"CallOfSlop-{UpVersion}.zip"))
            Fail($"updates: notes / file name wrong: '{good.Notes}' '{good.FileName}'");
        string[] badUrls =
        [
            UpZipUrl.Replace("https://", "http://"),
            UpZipUrl.Replace("TwitchTheSniper/CallOfSlop", "Someone/CallOfSlop"),
            UpZipUrl.Replace("TwitchTheSniper/CallOfSlop", "TwitchTheSniper/Other"),
            UpZipUrl.Replace("github.com", "github.com.evil.example"),
            UpZipUrl.Replace("https://github.com", "https://github.com@evil.example"),
            UpZipUrl.Replace("https://github.com", "https://github.com:8443"),
            UpZipUrl.Replace("https://github.com", "https://api.github.com"),
            UpZipUrl.Replace("https://github.com", "https://objects.githubusercontent.com"),
            UpZipUrl + "?x=1",
            UpZipUrl + "#frag",
            UpZipUrl.Replace(".zip", ".exe"),
            $"{UpdateManifest.DownloadPrefix}v1/../../../../evil/x.zip",
            $"{UpdateManifest.DownloadPrefix}v1/%2e%2e/x.zip",
            $"{UpdateManifest.DownloadPrefix}x.zip",
            $"{UpdateManifest.DownloadPrefix}v1/sub/x.zip",
            "https://github.com/TwitchTheSniper/CallOfSlop/archive/x.zip",
            "file:///C:/Windows/x.zip",
            "",
        ];
        var leaks = badUrls.Where(u => UpdateManifest.Parse(UpManifestJson(u, 1234, hash), out _) != null).ToList();
        if (leaks.Count > 0)
            Fail($"updates: manifests with untrusted URLs accepted: {string.Join(" | ", leaks)}");
        string[] badHashes = [new string('a', 63), new string('g', 64), "", new string('a', 65)];
        var hashLeaks = badHashes.Count(h => UpdateManifest.Parse(UpManifestJson(UpZipUrl, 1234, h), out _) != null);
        var sizeLeaks = new long[] { 0, -5, UpdateManifest.MaxSize + 1 }.Count(sz => UpdateManifest.Parse(UpManifestJson(UpZipUrl, sz, hash), out _) != null);
        var versionLeaks = new[] { "1.0.0", "dev", "0.2026.1022" }.Count(ver => UpdateManifest.Parse(UpManifestJson(UpZipUrl, 1234, hash, 1, ver), out _) != null);
        var junkLeaks = new[] { "", "null", "[]", "{", "{\"version\":5}", "\"text\"" }.Count(j => UpdateManifest.Parse(j, out _) != null);
        if (hashLeaks + sizeLeaks + versionLeaks + junkLeaks > 0)
            Fail($"updates: bad manifests accepted: hashes {hashLeaks}, sizes {sizeLeaks}, versions {versionLeaks}, junk {junkLeaks}");
        var future = UpdateManifest.Parse(UpManifestJson(UpZipUrl, 1234, hash, 99), out _);
        if (future is not { InstallableHere: false })
            Fail("updates: a manifest needing a newer updater is installable");
        if (UpdateManifest.CleanNotes(new string('x', 2000)).Length > 600 || UpdateManifest.CleanNotes("a\u00e9b%c\r\n d") != "ab%c d")
            Fail($"updates: notes cleaning wrong: '{UpdateManifest.CleanNotes("a\u00e9b%c\r\n d")}'");
        if (!UpdateManifest.IsTrustedPageUrl($"{UpdateManifest.ReleasesPage}/tag/v{UpVersion}") || UpdateManifest.IsTrustedPageUrl("https://evil.example/releases")
            || UpdateManifest.IsTrustedPageUrl("javascript:alert(1)"))
            Fail("updates: release page link check wrong");

        var rel = ReleaseInfo.Parse(UpReleaseJson(UpManifestUrl, 1234), out err);
        if (rel == null || rel.ManifestUrl != UpManifestUrl || rel.Tag != $"v{UpVersion}" || rel.ZipAssetSize != 1234 || !rel.PageUrl.EndsWith($"/tag/v{UpVersion}"))
            Fail($"updates: release info didn't parse ({err})");
        if (ReleaseInfo.Parse(UpReleaseJson("https://evil.example/update.json"), out _) != null
            || ReleaseInfo.Parse("""{"tag_name":"v1","assets":[]}""", out _) != null
            || ReleaseInfo.Parse("not json", out _) != null)
            Fail("updates: a release without a trusted update.json was accepted");
        Line($"manifests: good one parsed; refused {badUrls.Length} untrusted URLs, {badHashes.Length} bad hashes, 3 bad sizes, 3 bad versions, 6 junk files; release info checked");
    }

    // ---- Cache and rate limits ---------------------------------------------------------------------
    private static void UpCache(string temp)
    {
        var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var c = new UpdateCache();
        var never = c.ShouldQuery(now, false);
        c.CheckedUtc = now - TimeSpan.FromHours(1);
        var fresh = !c.ShouldQuery(now, false);
        var manualOk = c.ShouldQuery(now, true);
        c.CheckedUtc = now - TimeSpan.FromSeconds(20);
        var manualGap = !c.ShouldQuery(now, true);
        c.CheckedUtc = now - TimeSpan.FromHours(6.1);
        var stale = c.ShouldQuery(now, false);
        c.CheckedUtc = now + TimeSpan.FromDays(3);
        var clockBack = c.ShouldQuery(now, false);
        if (!never || !fresh || !manualOk || !manualGap || !stale || !clockBack)
            Fail($"updates: cache rules wrong (never {never}, 1 h fresh {fresh}, manual {manualOk}, manual gap {manualGap}, 6 h stale {stale}, clock went back {clockBack})");
        var path = Path.Combine(temp, "update_check.txt");
        var w = new UpdateCache { CheckedUtc = now, ETag = "W/\"abc\"", ReleaseJson = "{\"a\":\"line\\nbreak = x\"}", ManifestJson = "{}" };
        w.Save(path);
        var r = UpdateCache.Load(path);
        if (r.CheckedUtc != now || r.ETag != w.ETag || r.ReleaseJson != w.ReleaseJson || r.ManifestJson != "{}")
            Fail($"updates: cache didn't round-trip ({r.CheckedUtc:o} {r.ETag})");
        File.WriteAllText(path, "checked=garbage\nrelease=!!!notbase64\n");
        var junk = UpdateCache.Load(path);
        if (junk.CheckedUtc != DateTime.MinValue || junk.ReleaseJson != "")
            Fail("updates: a damaged cache didn't load as empty");
        Line("cache: 6 h between automatic checks, 60 s between manual ones, clock changes handled, round-trips, damaged cache = empty");
    }

    // ---- A whole check against a fake GitHub -----------------------------------------------------------
    private static void UpCheckFlow(string temp)
    {
        var hash = new string('b', 64);
        var mode = "ok";
        var fake = new FakeHttp(async req =>
        {
            var url = req.RequestUri!.ToString();
            if (mode == "slow")
                await Task.Delay(400);
            if (mode == "offline")
                throw new HttpRequestException("No such host is known.");
            if (mode == "ratelimit")
                return UpText("{\"message\":\"API rate limit exceeded\"}", HttpStatusCode.Forbidden);
            if (url == UpdateManifest.LatestApi)
                return req.Headers.TryGetValues("If-None-Match", out var et) && et.Contains("\"v7\"")
                    ? new HttpResponseMessage(HttpStatusCode.NotModified)
                    : UpText(UpReleaseJson(UpManifestUrl, 4321), etag: "\"v7\"");
            if (url == UpManifestUrl)
                return UpText(UpManifestJson(UpZipUrl, 4321, hash));
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var clock = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
        var cachePath = Path.Combine(temp, "check", "update_check.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var up = new Updater(fake, cachePath, temp, () => clock);

        // Startup check: returns at once (the fake answers slowly), then finds the update.
        mode = "slow";
        var sw = Stopwatch.StartNew();
        var t = up.Check(manual: false);
        var returnedMs = sw.Elapsed.TotalMilliseconds;
        var stateRightAway = up.Snapshot().State;
        if (returnedMs > 150 || stateRightAway != UpdateState.Checking)
            Fail($"updates: the startup check blocked the caller ({returnedMs:0} ms, state {stateRightAway})");
        if (!UpWait(t, "the first check"))
            return;
        var v = up.Snapshot();
        var expect = BuildInfo.IsNewer(BuildInfo.ParseRelease(UpVersion)!, BuildInfo.ReleaseVersion) ? UpdateState.Available : UpdateState.UpToDate;
        if (v.State != expect || v.Available?.Version.ToString() != UpVersion || fake.Asked.Count != 2 || v.Loud)
            Fail($"updates: first check gave {v.State} '{v.Message}' after {fake.Asked.Count} requests (expected {expect} after 2)");
        Line($"check: returned in {returnedMs:0.0} ms (fake GitHub took 400+ ms), then {v.State} '{v.Message}' after {fake.Asked.Count} requests");

        // Next launch an hour later: from the cache, no network.
        mode = "ok";
        clock += TimeSpan.FromHours(1);
        up = new Updater(fake, cachePath, temp, () => clock);
        UpWait(up.Check(manual: false), "the cached check");
        if (fake.Asked.Count != 2 || up.Snapshot().State != expect)
            Fail($"updates: a check within 6 h asked GitHub again ({fake.Asked.Count} requests) or lost the result ({up.Snapshot().State})");

        // 7 hours later: asks again with the ETag; GitHub says 304, the cached manifest is reused (one request).
        clock += TimeSpan.FromHours(6);
        UpWait(up.Check(manual: false), "the 304 check");
        if (fake.Asked.Count != 3 || fake.ETags[^1] != "\"v7\"" || up.Snapshot().State != expect)
            Fail($"updates: the 6 h re-check didn't use the ETag ({fake.Asked.Count} requests, ETag '{fake.ETags[^1]}', {up.Snapshot().State})");

        // Manual checks: at most one a minute.
        UpWait(up.Check(manual: true), "a manual check right after");
        var afterGap = fake.Asked.Count;
        clock += TimeSpan.FromMinutes(2);
        UpWait(up.Check(manual: true), "a manual check");
        if (afterGap != 3 || fake.Asked.Count != 4)
            Fail($"updates: manual check gap wrong ({afterGap} then {fake.Asked.Count} requests)");

        // Rate limited: a clean message, and the time still counts (no hammering).
        mode = "ratelimit";
        clock += TimeSpan.FromMinutes(2);
        UpWait(up.Check(manual: true), "the rate-limited check");
        v = up.Snapshot();
        if (v.State != UpdateState.Failed || !v.Message.Contains("rate limit") || !v.Loud || v.CheckedUtc != clock)
            Fail($"updates: rate limit handled as {v.State} '{v.Message}' (checked {v.CheckedUtc:o})");

        // Offline at startup: quiet (not Loud), nothing thrown.
        mode = "offline";
        clock += TimeSpan.FromHours(7);
        UpWait(up.Check(manual: false), "the offline check");
        v = up.Snapshot();
        if (v.State != UpdateState.Failed || v.Loud)
            Fail($"updates: offline startup check gave {v.State} (loud {v.Loud})");

        // A release whose tag doesn't match its manifest is refused.
        var mismatch = new FakeHttp(req => Task.FromResult(req.RequestUri!.ToString() == UpdateManifest.LatestApi
            ? UpText(UpReleaseJson(UpManifestUrl))
            : UpText(UpManifestJson(UpZipUrl, 4321, hash, 1, "0.2026.1022.8"))));
        var cache2 = Path.Combine(temp, "check2.txt");
        var up2 = new Updater(mismatch, cache2, temp, () => clock);
        UpWait(up2.Check(manual: true), "the mismatched check");
        if (up2.Snapshot().State != UpdateState.Failed)
            Fail($"updates: a tag / manifest mismatch was accepted ({up2.Snapshot().State})");
        Line($"check: cache within 6 h (no request), 304 with ETag, manual gap, rate limit ({v.Message.Length > 0}), offline quiet, tag mismatch refused; {fake.Asked.Count} fake requests in all");
    }

    // ---- A whole install against a fake GitHub -------------------------------------------------------
    private static void UpInstallFlow(string temp)
    {
        var zip = UpMakeZip(("CallOfSlop/CallOfSlop.exe", "not really an exe"), ("CallOfSlop/NOTICE.txt", "notice"), ("CallOfSlop/Maps/a.json", "{}"));
        var serveExtra = false;
        var fail500 = false;
        var fake = new FakeHttp(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url == UpdateManifest.LatestApi)
                return Task.FromResult(UpText(UpReleaseJson(UpManifestUrl, zip.Length)));
            if (url == UpManifestUrl)
                return Task.FromResult(UpText(UpManifestJson(UpZipUrl, zip.Length, UpSha(zip))));
            if (url == UpZipUrl)
                return Task.FromResult(fail500 ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : UpBytes(serveExtra ? zip.Concat(new byte[] { 1, 2, 3 }).ToArray() : zip, req));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });
        var root = Path.Combine(temp, "games");
        var current = Path.Combine(root, "CallOfSlop-old", "CallOfSlop");
        Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(current, "CallOfSlop.exe"), "old build");
        var installRoot = UpdateInstaller.InstallRoot(current);
        if (!SavePaths.SameFolder(installRoot, root) || !SavePaths.SameFolder(UpdateInstaller.InstallRoot(Path.Combine(root, "MyGame")), root))
            Fail($"updates: install root for {current} is {installRoot}, expected {root}");

        Updater NewUpdater(string name, out List<ProcessStartInfo> launched)
        {
            var list = new List<ProcessStartInfo>();
            launched = list;
            var u = new Updater(fake, Path.Combine(temp, name + ".txt"), current, () => DateTime.UtcNow)
            {
                InstallRootOverride = null, // the real rule: next to the current build's zip folder
                CanLaunchExe = true,
                Launcher = psi =>
                {
                    list.Add(psi); // never actually started
                    return true;
                },
            };
            UpWait(u.Check(manual: true), "the install's check");
            return u;
        }

        // Resume: half the zip is already downloaded; the rest comes with a Range request.
        var m = UpdateManifest.Parse(UpManifestJson(UpZipUrl, zip.Length, UpSha(zip)), out _)!;
        var part = UpdateInstaller.DownloadPath(root, m);
        File.WriteAllBytes(part, zip[..(zip.Length / 2)]);
        var up = NewUpdater("install1", out var launched);
        if (up.Snapshot().Available == null)
        {
            Fail($"updates: the install test's check found nothing ({up.Snapshot().Message})");
            return;
        }
        UpWait(up.Install(), "the install");
        var v = up.Snapshot();
        var target = Path.Combine(root, $"CallOfSlop-{UpVersion}");
        var exe = Path.Combine(target, "CallOfSlop", "CallOfSlop.exe");
        var psi = launched.FirstOrDefault();
        var rangeAsked = fake.Ranges.Any(r => r == $"bytes={zip.Length / 2}-");
        if (v.State != UpdateState.Launching || !up.QuitRequested || psi == null || Path.GetFullPath(psi.FileName) != Path.GetFullPath(exe)
            || !SavePaths.SameFolder(psi.WorkingDirectory, Path.GetDirectoryName(exe)!) || psi.ArgumentList.Count > 0 || psi.Arguments.Length > 0 || psi.UseShellExecute)
            Fail($"updates: install ended {v.State} '{v.Message}', quit {up.QuitRequested}, launch {psi?.FileName} in {psi?.WorkingDirectory}");
        if (!rangeAsked || File.Exists(part) || !File.Exists(exe) || File.ReadAllText(Path.Combine(current, "CallOfSlop.exe")) != "old build" || Directory.Exists(target + ".partial"))
            Fail($"updates: resume {rangeAsked}, download left {File.Exists(part)}, new exe {File.Exists(exe)}, old build intact {File.Exists(Path.Combine(current, "CallOfSlop.exe"))}");
        Line($"install: resumed from byte {zip.Length / 2} of {zip.Length}, verified, unzipped to {Path.GetRelativePath(temp, target)}, launch command '{Path.GetRelativePath(temp, psi?.FileName ?? "?")}' (not run), old build untouched");

        // A second install of the same version goes to a new folder (-2), never over the first.
        var up2 = NewUpdater("install2", out _);
        UpWait(up2.Install(), "the second install");
        if (!Directory.Exists(target + "-2") || up2.Snapshot().State != UpdateState.Launching)
            Fail($"updates: a second install didn't go to {Path.GetFileName(target)}-2 ({up2.Snapshot().Message})");

        // Hash mismatch: rejected, the bad download is deleted, nothing unzipped or launched.
        var badHash = UpdateManifest.Parse(UpManifestJson(UpZipUrl, zip.Length, new string('c', 64)), out _)!;
        var badRoot = Path.Combine(temp, "bad");
        Directory.CreateDirectory(badRoot);
        var badZip = UpdateInstaller.DownloadPath(badRoot, badHash);
        UpdateInstaller.DownloadAsync(new HttpClient(fake, false), badHash, badZip, (_, _) => { }, CancellationToken.None).Wait();
        var hashProblem = UpdateInstaller.Verify(badZip, badHash);
        if (hashProblem == null || !hashProblem.Contains("SHA-256"))
            Fail("updates: a zip with the wrong SHA-256 passed");

        // Same through the updater: a release whose hash is wrong never launches.
        var fakeBad = new FakeHttp(req =>
        {
            var url = req.RequestUri!.ToString();
            return Task.FromResult(url == UpdateManifest.LatestApi ? UpText(UpReleaseJson(UpManifestUrl, zip.Length))
                : url == UpManifestUrl ? UpText(UpManifestJson(UpZipUrl, zip.Length, new string('d', 64)))
                : UpBytes(zip, req));
        });
        var badCurrent = Path.Combine(temp, "bad2", "game");
        Directory.CreateDirectory(badCurrent);
        var launchedBad = 0;
        var upBad = new Updater(fakeBad, Path.Combine(temp, "bad2.txt"), badCurrent) { CanLaunchExe = true, Launcher = _ => ++launchedBad > 0 };
        UpWait(upBad.Check(manual: true), "the bad-hash check");
        UpWait(upBad.Install(), "the bad-hash install");
        var bv = upBad.Snapshot();
        var badLeft = Directory.GetFileSystemEntries(Path.Combine(temp, "bad2")).Select(f => Path.GetFileName(f)).Where(n => n != "game").ToList();
        if (bv.State != UpdateState.Failed || !bv.Message.Contains("SHA-256") || launchedBad != 0 || upBad.QuitRequested || badLeft.Count > 0)
            Fail($"updates: bad hash install ended {bv.State} '{bv.Message}', launched {launchedBad}, left behind [{string.Join(", ", badLeft)}]");

        // Too many bytes from the server, and a server error: clean failures.
        serveExtra = true;
        var upExtra = NewUpdater("extra", out var launchedExtra);
        File.Delete(UpdateInstaller.DownloadPath(root, m));
        UpWait(upExtra.Install(), "the oversized install");
        serveExtra = false;
        File.Delete(UpdateInstaller.DownloadPath(root, m)); // the oversized attempt's partial file (it would otherwise be reused)
        fail500 = true;
        var up500 = NewUpdater("e500", out var launched500);
        UpWait(up500.Install(), "the failing install");
        fail500 = false;
        if (upExtra.Snapshot().State != UpdateState.Failed || launchedExtra.Count > 0 || up500.Snapshot().State != UpdateState.Failed || launched500.Count > 0
            || !up500.Snapshot().Message.Contains("500"))
            Fail($"updates: oversized download {upExtra.Snapshot().State} '{upExtra.Snapshot().Message}', HTTP 500 {up500.Snapshot().State} '{up500.Snapshot().Message}'");

        // Unsafe zips: entries outside the folder are refused, nothing is written outside.
        var evilRoot = Path.Combine(temp, "evil");
        Directory.CreateDirectory(evilRoot);
        var refused = 0;
        foreach (var name in new[] { "../escaped.txt", "CallOfSlop/../../escaped.txt", "/abs.txt", "C:/escaped.txt", "..\\escaped.txt" })
        {
            var evilZip = Path.Combine(evilRoot, "evil.zip");
            File.WriteAllBytes(evilZip, UpMakeZip(("CallOfSlop/CallOfSlop.exe", "x"), (name, "evil")));
            try
            {
                UpdateInstaller.Extract(evilZip, Path.Combine(evilRoot, $"t{refused}"));
                Fail($"updates: a zip entry '{name}' was unzipped");
            }
            catch (IOException)
            {
                refused++;
            }
        }
        if (File.Exists(Path.Combine(temp, "escaped.txt")) || File.Exists(Path.Combine(evilRoot, "escaped.txt")))
            Fail("updates: an unsafe zip wrote outside its folder");
        var noExe = Path.Combine(evilRoot, "noexe.zip");
        File.WriteAllBytes(noExe, UpMakeZip(("CallOfSlop/readme.txt", "x")));
        try
        {
            UpdateInstaller.Extract(noExe, Path.Combine(evilRoot, "noexe"));
            Fail("updates: a zip without CallOfSlop.exe installed");
        }
        catch (IOException)
        {
            // Expected.
        }

        // Smaller pieces: the launch command, target names, MOTW removal (Windows only), the dll fallback.
        var cmd = UpdateInstaller.LaunchCommand(exe);
        if (cmd.FileName != exe || cmd.UseShellExecute || cmd.ArgumentList.Count != 0 || !SavePaths.SameFolder(cmd.WorkingDirectory, Path.GetDirectoryName(exe)!))
            Fail($"updates: launch command {cmd.FileName} {cmd.Arguments} in {cmd.WorkingDirectory}");
        var motw = 0;
        if (OperatingSystem.IsWindows())
        {
            var marked = Path.Combine(evilRoot, "marked.txt");
            File.WriteAllText(marked, "x");
            try
            {
                File.WriteAllText(marked + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
                motw = UpdateInstaller.RemoveMarkOfTheWeb(evilRoot);
                if (motw < 1 || File.Exists(marked + ":Zone.Identifier"))
                    Fail("updates: the Zone.Identifier stream wasn't removed");
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
            {
                Line($"(MOTW check skipped: {ex.Message})");
            }
        }
        Line($"install: second copy to -2, wrong SHA-256 refused (deleted, not launched), oversized and HTTP 500 downloads fail cleanly, {refused}/5 unsafe zip paths refused, no-exe zip refused, MOTW streams removed {motw}");
    }

    // ---- Settings -------------------------------------------------------------------------------
    private static void UpSettings(string temp)
    {
        var path = Path.Combine(temp, "settings.txt");
        var s = new Settings { UpdateCheck = false, UpdateSkip = UpVersion };
        s.Save(path);
        var back = Settings.Load(path);
        File.WriteAllText(path, "version=1\nname=Old\n");
        var old = Settings.Load(path);
        File.WriteAllText(path, "version=1\nupdate_skip=../../evil\nupdate_check=yes\n");
        var junk = Settings.Load(path);
        if (back.UpdateCheck || back.UpdateSkip != UpVersion || !old.UpdateCheck || old.UpdateSkip != "" || junk.UpdateSkip != "" || !junk.UpdateCheck)
            Fail($"updates: settings round-trip check {back.UpdateCheck} skip '{back.UpdateSkip}'; old file check {old.UpdateCheck} skip '{old.UpdateSkip}'; junk skip '{junk.UpdateSkip}'");
        Line("settings: update_check and update_skip round-trip; older settings files get checks on and nothing skipped");
    }

    // ---- Server browser ---------------------------------------------------------------------------
    private static void UpServerBrowser()
    {
        var info = new ServerInfo("Host", "Harbor", 3, 16, 2, MatchPhase.Playing, 60123, ServerInfo.ProtocolVersion, GameMode.FreeForAll, PhysicsMode.Swept, "", Build: UpVersion);
        var back = ServerInfo.Find([0x1c, 1, 2, .. info.Encode()]);
        var withRot = info with { Rotation = MapPack.TrueRandomName };
        var backRot = ServerInfo.Find(withRot.Encode());
        var plain = info with { Build = "" };
        if (back != info || backRot != withRot || ServerInfo.Find(plain.Encode()) != plain)
            Fail($"updates: build in the server info didn't round-trip ({back?.Build} / {backRot?.Build}/{backRot?.Rotation})");
        // An older build reads the info up to the rotation: with a build and no rotation, the rotation reads as "".
        var bytes = info.Encode();
        var rotationLengthAt = bytes.Length - (2 + UpVersion.Length) - 1;
        if (bytes[rotationLengthAt] != 0 || bytes[rotationLengthAt + 1] != (byte)'B')
            Fail("updates: the build isn't after an empty rotation (older builds would misread it)");
        var junk = ServerInfo.Find([.. plain.Encode(), 0, (byte)'B', 200, 1, 2]);
        if (junk is not { Build: "" })
            Fail("updates: a truncated build tag was read");
        var ours = BuildInfo.ParseRelease("0.2026.1022.5");
        var p = ServerInfo.ProtocolVersion;
        var cases = new (byte Proto, string Build, Version? Ours, BuildCompat Want)[]
        {
            (p, "", ours, BuildCompat.Same),
            (p, "0.2026.1022.5", ours, BuildCompat.Same),
            (p, "0.2026.1022.9", ours, BuildCompat.OtherBuild),
            ((byte)(p + 1), "", ours, BuildCompat.ServerNewer),
            ((byte)(p - 1), "", ours, BuildCompat.ServerOlder),
            ((byte)(p + 1), "0.2026.1023.1", ours, BuildCompat.ServerNewer),
            ((byte)(p - 1), "0.2026.1001.1", null, BuildCompat.ServerOlder),
            (p, "0.2026.1022.9", null, BuildCompat.Same),
        };
        var wrong = cases.Where(c => BuildCompatText.Compare(c.Proto, c.Build, p, c.Ours) != c.Want).ToList();
        if (wrong.Count > 0)
            Fail($"updates: build compare wrong for {string.Join(", ", wrong.Select(c => $"{c.Proto}/{c.Build}"))}");
        var older = BuildCompatText.Describe(BuildCompat.ServerNewer, "0.2026.1023.1");
        if (!older.StartsWith("Your build is older - update to join", StringComparison.Ordinal) || BuildCompatText.Describe(BuildCompat.Same, "") != "")
            Fail($"updates: browser text '{older}'");
        Line($"server browser: build '{back?.Build}' rides after the rotation ({2 + UpVersion.Length} bytes), {cases.Length} compare cases; e.g. \"{older}\"");
    }
}
