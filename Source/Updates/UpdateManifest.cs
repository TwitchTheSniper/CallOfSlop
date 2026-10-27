using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The <c>update.json</c> asset of a GitHub release (written by tools/package.ps1): which build the release is and how
/// to check its zip. The game only trusts a manifest whose zip lives in this repository's own release downloads.
/// <para>
/// Platforms: the top-level url / size / sha256 are the Windows (win-x64) zip, as every older build expects. A release
/// may add <c>"platforms": {"osx-arm64": {"url", "size", "sha256"}}</c> (the Mac job in release.yml); a build picks
/// the entry for <see cref="CurrentPlatform"/> when there is one. A Mac never falls back to the top-level (Windows)
/// zip: without its own entry the release has no download for it (<see cref="NoDownloadForPlatform"/>).
/// </para>
/// </summary>
public sealed record UpdateManifest(
    Version Version,
    int Build,
    string Sha,
    int Protocol,
    string Url,
    long Size,
    string Sha256,
    string Notes,
    int MinUpdater)
{
    /// <summary>The repository the game updates from (it's public; no token is ever used).</summary>
    public const string Owner = "TwitchTheSniper", Repo = "CallOfSlop";

    /// <summary>Every download must start with this (HTTPS, github.com, this repo's release assets).</summary>
    public const string DownloadPrefix = $"https://github.com/{Owner}/{Repo}/releases/download/";

    /// <summary>The releases page (shown when something fails, and as the fallback "release page" link).</summary>
    public const string ReleasesPage = $"https://github.com/{Owner}/{Repo}/releases";

    /// <summary>The GitHub API call for the newest release (unauthenticated: 60 calls an hour per IP, so it's cached).</summary>
    public const string LatestApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    /// <summary>Biggest zip the game will download (today's build is far smaller).</summary>
    public const long MaxSize = 1536L * 1024 * 1024;

    /// <summary>The platform this build downloads for: "win-x64", "osx-arm64" or "linux-x64".</summary>
    public static string CurrentPlatform => PlatformOverride ?? (OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? MacPlatform : "linux-x64");

    /// <summary>Self-test only: plays another platform (the "Updates" section runs as Windows on every runner, "Mac release" as a Mac).</summary>
    internal static string? PlatformOverride;

    public const string MacPlatform = "osx-arm64";

    /// <summary>The start of <see cref="Parse(string, out string, string)"/>'s error when the release has no zip for the platform.</summary>
    public const string NoDownloadForPlatform = "no download for this platform";

    /// <summary>Can this game's updater install it (or must the player download it by hand)?</summary>
    public bool InstallableHere => MinUpdater <= BuildInfo.UpdaterVersion;

    /// <summary>The zip's file name (the last part of the URL).</summary>
    public string FileName => Url[(Url.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Is <paramref name="url"/> one of this repo's release assets over HTTPS, with the given extension? Anything else
    /// (another host or repo, http, user info, a port, a query, "..", backslashes) is refused.
    /// </summary>
    public static bool IsTrustedAssetUrl(string? url, string extension)
    {
        if (string.IsNullOrEmpty(url) || url.Length > 400 || url.Contains("..") || url.Contains('\\') || url.Contains('%') || url.Contains('@'))
            return false;
        if (!url.StartsWith(DownloadPrefix, StringComparison.Ordinal) || !url.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            return false;
        if (u.Scheme != Uri.UriSchemeHttps || u.Host != "github.com" || !u.IsDefaultPort || u.UserInfo.Length > 0 || u.Query.Length > 0 || u.Fragment.Length > 0)
            return false;
        // /Owner/Repo/releases/download/<tag>/<file>: exactly two parts after the prefix.
        const string path = $"/{Owner}/{Repo}/releases/download/";
        if (!u.AbsolutePath.StartsWith(path, StringComparison.Ordinal))
            return false;
        var parts = u.AbsolutePath[path.Length..].Split('/');
        return parts.Length == 2 && parts.All(p => p.Length > 0 && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'));
    }

    /// <summary>Is this a release page of this repo (the link the banner opens)?</summary>
    public static bool IsTrustedPageUrl(string? url) =>
        url != null && url.Length < 400 && (url == ReleasesPage || url.StartsWith(ReleasesPage + "/tag/", StringComparison.Ordinal))
        && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && u.Host == "github.com" && !url.Contains("..");

    /// <summary>Plain printable ASCII, single spaces, at most <paramref name="max"/> characters (release notes are shown in the menu).</summary>
    public static string CleanNotes(string? text, int max = 600)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            var c = ch is '\r' or '\n' or '\t' ? ' ' : ch;
            if (c < 32 || c > 126)
                continue;
            if (c == ' ' && (sb.Length == 0 || sb[^1] == ' '))
                continue;
            sb.Append(c);
            if (sb.Length >= max)
                break;
        }
        var s = sb.ToString().Trim();
        return s.Length >= max ? s[..(max - 3)].TrimEnd() + "..." : s;
    }

    /// <summary>Reads and checks a manifest for this build's platform. Null with a reason when it's malformed or not trustworthy.</summary>
    public static UpdateManifest? Parse(string json, out string error) => Parse(json, out error, CurrentPlatform);

    /// <summary>Reads and checks a manifest, taking the zip for <paramref name="platform"/> (see the class summary).</summary>
    public static UpdateManifest? Parse(string json, out string error, string platform)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object)
                return Bad("not a JSON object", out error);
            var version = BuildInfo.ParseRelease(Str(r, "version"));
            if (version == null)
                return Bad($"bad version '{CleanNotes(Str(r, "version"), 40)}'", out error);
            var url = Str(r, "url");
            if (!IsTrustedAssetUrl(url, ".zip"))
                return Bad("the download isn't one of this repository's GitHub release files", out error);
            var size = Long(r, "size");
            if (size <= 0 || size > MaxSize)
                return Bad($"bad size {size}", out error);
            var hash = Str(r, "sha256")?.Trim().ToLowerInvariant() ?? "";
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                return Bad("missing or malformed SHA-256", out error);
            // This platform's own zip, when the release lists one (checked exactly like the top-level one).
            if (r.TryGetProperty("platforms", out var platforms) && platforms.ValueKind == JsonValueKind.Object
                && platforms.TryGetProperty(platform, out var mine) && mine.ValueKind == JsonValueKind.Object)
            {
                url = Str(mine, "url");
                if (!IsTrustedAssetUrl(url, ".zip"))
                    return Bad($"the {platform} download isn't one of this repository's GitHub release files", out error);
                size = Long(mine, "size");
                if (size <= 0 || size > MaxSize)
                    return Bad($"bad {platform} size {size}", out error);
                hash = Str(mine, "sha256")?.Trim().ToLowerInvariant() ?? "";
                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                    return Bad($"missing or malformed {platform} SHA-256", out error);
            }
            else if (platform == MacPlatform)
                return Bad($"{NoDownloadForPlatform} (this release has no Mac download)", out error); // the top-level zip is Windows
            var sha = Str(r, "sha")?.Trim().ToLowerInvariant() ?? "";
            if (!sha.All(Uri.IsHexDigit) || sha.Length > 40)
                sha = "";
            return new UpdateManifest(version, (int)Math.Clamp(Long(r, "build"), 0, int.MaxValue), sha, (int)Math.Clamp(Long(r, "protocol"), 0, 255),
                url!, size, hash, CleanNotes(Str(r, "notes")), (int)Math.Clamp(Long(r, "minUpdater"), 0, 1000));
        }
        catch (JsonException ex)
        {
            return Bad($"not valid JSON ({ex.Message})", out error);
        }
    }

    private static UpdateManifest? Bad(string why, out string error)
    {
        error = why;
        return null;
    }

    internal static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static long Long(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : -1;
}

/// <summary>What the GitHub API says about the newest release (the parts the game uses).</summary>
public sealed record ReleaseInfo(string Tag, string PageUrl, string Notes, string ManifestUrl, long ZipAssetSize, string ZipAssetName)
{
    /// <summary>Every zip asset's size by name (a release can carry a Windows and a Mac zip).</summary>
    public IReadOnlyDictionary<string, long> ZipSizes { get; init; } = new Dictionary<string, long>();

    /// <summary>The size of the release's zip called <paramref name="name"/>, or -1 when it has none.</summary>
    public long ZipSize(string name) => ZipSizes.TryGetValue(name, out var n) ? n : name == ZipAssetName ? ZipAssetSize : -1;

    /// <summary>Reads <c>GET /repos/.../releases/latest</c>. Null with a reason when there's no usable update.json asset.</summary>
    public static ReleaseInfo? Parse(string json, out string error)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object)
                return Fail("not a JSON object", out error);
            var tag = UpdateManifest.Str(r, "tag_name") ?? "";
            var page = UpdateManifest.Str(r, "html_url");
            if (!UpdateManifest.IsTrustedPageUrl(page))
                page = UpdateManifest.ReleasesPage;
            string? manifest = null, zipName = "";
            long zipSize = -1;
            var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
            if (r.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray().Take(50))
                {
                    var name = UpdateManifest.Str(a, "name") ?? "";
                    var dl = UpdateManifest.Str(a, "browser_download_url");
                    if (name == "update.json" && UpdateManifest.IsTrustedAssetUrl(dl, ".json"))
                        manifest = dl;
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && UpdateManifest.IsTrustedAssetUrl(dl, ".zip"))
                    {
                        (zipName, zipSize) = (name, UpdateManifest.Long(a, "size"));
                        sizes[name] = zipSize;
                    }
                }
            if (manifest == null)
                return Fail($"the newest release ({UpdateManifest.CleanNotes(tag, 40)}) has no update.json", out error);
            return new ReleaseInfo(UpdateManifest.CleanNotes(tag, 40), page!, UpdateManifest.CleanNotes(UpdateManifest.Str(r, "body")), manifest, zipSize, zipName)
            {
                ZipSizes = sizes,
            };
        }
        catch (JsonException ex)
        {
            return Fail($"not valid JSON ({ex.Message})", out error);
        }
    }

    private static ReleaseInfo? Fail(string why, out string error)
    {
        error = why;
        return null;
    }
}
