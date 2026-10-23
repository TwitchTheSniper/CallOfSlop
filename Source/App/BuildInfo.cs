using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// Which build of the game this is. Release builds are stamped by tools/package.ps1 (and so by the Release workflow)
/// with <c>-p:InformationalVersion=0.YYYY.MDD.N</c>: year, month * 100 + day (no leading zero, e.g. 1022 or 105) and the
/// release workflow's run number; the SDK appends <c>+&lt;git sha&gt;</c>. A plain <c>dotnet build</c> is a dev build
/// ("1.0.0+sha", the SDK default), which never counts as newer than a release.
/// </summary>
public static class BuildInfo
{
    /// <summary>The version of the in-game updater itself. A manifest asking for a newer one must be installed by hand.</summary>
    public const int UpdaterVersion = 1;

    /// <summary>The raw informational version stamped into the exe.</summary>
    public static readonly string Informational =
        typeof(BuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private static readonly (Version? Version, string Sha) Parsed = Parse(Informational);

    /// <summary>This build's release version, or null for a dev build.</summary>
    public static Version? ReleaseVersion => Parsed.Version;

    /// <summary>A release build (stamped with a 0.YYYY.MDD.N version)?</summary>
    public static bool IsRelease => Parsed.Version != null;

    /// <summary>"0.2026.1022.5", or "dev".</summary>
    public static string Version => Parsed.Version?.ToString() ?? "dev";

    /// <summary>The git commit (short), or "" when unknown.</summary>
    public static string ShortSha => Parsed.Sha.Length > 7 ? Parsed.Sha[..7] : Parsed.Sha;

    /// <summary>For the main menu corner: "v0.2026.1022.5 (abc1234)" or "dev build (abc1234)".</summary>
    public static string Label => (IsRelease ? $"v{Version}" : "dev build") + (ShortSha.Length > 0 ? $" ({ShortSha})" : "");

    /// <summary>
    /// Splits an informational version into its release version (null unless it follows the 0.YYYY.MDD.N scheme) and the
    /// git sha after the '+' (the SDK may add it more than once, joined by '.': the last hex part wins).
    /// </summary>
    public static (Version? Version, string Sha) Parse(string informational)
    {
        var plus = informational.IndexOf('+');
        var head = plus >= 0 ? informational[..plus] : informational;
        var sha = "";
        if (plus >= 0)
            foreach (var part in informational[(plus + 1)..].Split('.'))
                if (part.Length >= 7 && part.All(Uri.IsHexDigit))
                    sha = part.ToLowerInvariant();
        return (ParseRelease(head.Trim()), sha);
    }

    /// <summary>A release version in the 0.YYYY.MDD.N scheme (an optional leading 'v'), or null.</summary>
    public static Version? ParseRelease(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V'))
            t = t[1..];
        if (!System.Version.TryParse(t, out var v) || v.Major != 0 || v.Minor < 2020 || v.Minor > 2999 || v.Build < 101 || v.Build > 1231 || v.Revision < 0)
            return null;
        return v;
    }

    /// <summary>Is <paramref name="candidate"/> newer than <paramref name="current"/>? A dev build (null) is older than any release.</summary>
    public static bool IsNewer(Version candidate, Version? current) => current == null || candidate > current;
}
