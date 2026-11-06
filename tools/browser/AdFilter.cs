namespace SlopBrowser;

/// <summary>
/// The optional ad blocking's list (docs/sandbox/MEDIA.md, "Ad blocking"): adhosts.txt beside the exe. It only ever says no to a request, so it can't take a page anywhere. Off, nothing here runs.
/// Blocking is each viewer's own and never leaves this PC. Addresses aren't logged, only how many were refused.
/// </summary>
public static class AdFilter
{
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> Paths = new();

    public static int Count => Hosts.Count + Paths.Count;

    /// <summary>Reads the list. A missing or unreadable file means nothing is refused, and says so through the return value.</summary>
    public static string Load(string path)
    {
        try
        {
            Parse(File.ReadLines(path));
            return $"{Hosts.Count} hosts, {Paths.Count} paths";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "no list: " + e.Message;
        }
    }

    public static void Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (line.StartsWith("path:", StringComparison.Ordinal))
                Paths.Add(line[5..]);
            else
                Hosts.Add(line);
        }
    }

    /// <summary>Is this address on the list? Its host or any domain above it; on YouTube's own hosts, its path too.</summary>
    public static bool Blocks(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http"))
            return false;
        var host = u.IdnHost.AsSpan();
        var at = host;
        while (true)
        {
            if (Hosts.GetAlternateLookup<ReadOnlySpan<char>>().Contains(at))
                return true;
            var dot = at.IndexOf('.');
            if (dot < 0)
                break;
            at = at[(dot + 1)..];
        }
        if (!(u.IdnHost.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase) || u.IdnHost.EndsWith("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)))
            return false;
        foreach (var p in Paths)
            if (u.AbsolutePath.StartsWith(p, StringComparison.Ordinal))
                return true;
        return false;
    }
}

/// <summary>One page's switch, turned by the game.</summary>
public sealed class AdSwitch(bool on)
{
    public volatile bool On = on;
    public long Refused;
}
