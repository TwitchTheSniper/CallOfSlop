using System.Globalization;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// The last update check, kept in the save folder (<c>update_check.txt</c>) so the game asks GitHub at most once every
/// <see cref="MaxAge"/> on its own (the unauthenticated API allows 60 calls an hour per address). A manual "Check for
/// updates" always asks, but not more than once every <see cref="ManualGap"/>. The ETag makes a repeat check that finds
/// nothing new a "304 Not Modified", which GitHub doesn't count against the limit.
/// </summary>
public sealed class UpdateCache
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    public static readonly TimeSpan ManualGap = TimeSpan.FromSeconds(60);

    /// <summary>When GitHub was last asked (successfully or not; UTC). MinValue: never.</summary>
    public DateTime CheckedUtc = DateTime.MinValue;

    public string ETag = "";
    public string ReleaseJson = "";
    public string ManifestJson = "";

    /// <summary>Should a check go to the network now (false: use what's cached)?</summary>
    public bool ShouldQuery(DateTime nowUtc, bool manual)
    {
        if (CheckedUtc == DateTime.MinValue || CheckedUtc > nowUtc + TimeSpan.FromMinutes(5))
            return true; // never checked, or the clock went backwards a lot
        var age = nowUtc - CheckedUtc;
        return manual ? age >= ManualGap : age >= MaxAge;
    }

    public static UpdateCache Load(string path)
    {
        var c = new UpdateCache();
        try
        {
            if (!File.Exists(path))
                return c;
            foreach (var line in File.ReadLines(path).Take(20))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var (k, v) = (line[..eq], line[(eq + 1)..]);
                switch (k)
                {
                    case "checked" when DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t):
                        c.CheckedUtc = t;
                        break;
                    case "etag": c.ETag = v.Length < 200 ? v : ""; break;
                    case "release": c.ReleaseJson = FromB64(v); break;
                    case "manifest": c.ManifestJson = FromB64(v); break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable cache is just an empty one.
        }
        return c;
    }

    public bool Save(string path) =>
        SavePaths.WriteAtomic(path, string.Join("\n",
            "# Call of Slop update check cache (safe to delete)",
            $"checked={CheckedUtc.ToString("o", CultureInfo.InvariantCulture)}",
            $"etag={ETag}",
            $"release={ToB64(ReleaseJson)}",
            $"manifest={ToB64(ManifestJson)}") + "\n");

    private static string ToB64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private static string FromB64(string s)
    {
        try
        {
            return s.Length < 400_000 ? Encoding.UTF8.GetString(Convert.FromBase64String(s)) : "";
        }
        catch (FormatException)
        {
            return "";
        }
    }
}
