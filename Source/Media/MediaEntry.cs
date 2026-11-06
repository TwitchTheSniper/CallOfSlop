using System.Text;

namespace CallOfSlop;

/// <summary>
/// One line of a screen's or Music Player's list (docs/sandbox/MEDIA.md): "yt:" and an 11-character YouTube video id, or "game:" and the
/// name of one of the game's own tracks. This is the only form that is ever kept, sent or saved. A pasted URL is taken apart here, and what a
/// peer's browser gets is built from the id alone, so no text from another player can send it anywhere else. Every way in (a packet, a
/// blueprint, a world file) goes through <see cref="Clean"/>.
/// </summary>
public static class MediaEntry
{
    public const string Link = "yt:", Game = "game:", Web = "web:";
    public const int IdChars = 11;

    /// <summary>The longest line: a Browser Screen's address.</summary>
    public const int MaxChars = 300;

    public static bool IsLink(string entry) => entry.StartsWith(Link, StringComparison.Ordinal);

    public static string VideoId(string entry) => entry[Link.Length..];

    public static bool IsWeb(string entry) => entry.StartsWith(Web, StringComparison.Ordinal);

    /// <summary>A line a browser page plays: a video or a web address.</summary>
    public static bool IsPage(string entry) => IsLink(entry) || IsWeb(entry);

    public static string WebUrl(string entry) => entry[Web.Length..];

    /// <summary>The game's track of a "game:" line, or null.</summary>
    public static MusicTrack? Track(string entry)
    {
        if (!entry.StartsWith(Game, StringComparison.Ordinal))
            return null;
        foreach (var t in MusicTracks.All)
            if (entry.AsSpan(Game.Length).SequenceEqual(t.Name))
                return t;
        return null;
    }

    /// <summary>The line for one of the game's tracks.</summary>
    public static string ForTrack(MusicTrack track) => Game + track.Name;

    /// <summary>A line exactly as it may be kept, or null for anything else.</summary>
    public static string? Clean(string? entry)
    {
        if (entry == null || entry.Length > MaxChars)
            return null;
        if (IsLink(entry))
            return entry.Length == Link.Length + IdChars && IsId(entry.AsSpan(Link.Length)) ? entry : null;
        if (IsWeb(entry))
            return WebSafe(WebUrl(entry)) ? entry : null;
        return Track(entry) != null ? entry : null;
    }

    public static bool IsId(ReadOnlySpan<char> id)
    {
        if (id.Length != IdChars)
            return false;
        foreach (var ch in id)
            if (ch is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_'))
                return false;
        return true;
    }

    /// <summary>
    /// What a typed address comes to for a Browser Screen: a list line, or null with why not. Another peer's browser may open this, so it is http or https only, and never a place on the viewer's own network: localhost,
    /// a private address, a bare machine name. tools/browser/WebPage.cs (WebPolicy) checks the same again, there on everything a page loads.
    /// </summary>
    public static string? FromAddress(string text, out string why)
    {
        why = "";
        text = text.Trim();
        if (text.Length == 0)
        {
            why = "type an address first";
            return null;
        }
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            var colon = text.IndexOf(':');
            if (colon > 0 && text.IndexOf('.') is var dot && (dot < 0 || colon < dot) && !int.TryParse(text.AsSpan(colon + 1, Math.Max(0, text.IndexOf('/', colon) is var sl && sl > 0 ? sl - colon - 1 : text.Length - colon - 1)), out _))
            {
                why = "only http and https addresses open here"; // (file:, chrome:, data:, javascript:...)
                return null;
            }
            text = "https://" + text;
        }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u) || !WebSafe(u.AbsoluteUri))
        {
            why = "only public http and https addresses open here";
            return null;
        }
        return Web + u.AbsoluteUri;
    }

    private static bool WebSafe(string url)
    {
        if (url.Length > MaxChars - Web.Length || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http") || u.UserInfo.Length > 0)
            return false;
        foreach (var ch in url)
            if (ch is <= ' ' or > '~')
                return false;
        return !PrivateHost(u);
    }

    private static bool PrivateHost(Uri u)
    {
        var host = u.IdnHost.Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || !host.Contains('.') && !host.Contains(':') || host == "localhost")
            return true;
        foreach (var end in (ReadOnlySpan<string>)[".localhost", ".local", ".internal", ".lan", ".home.arpa"])
            if (host.EndsWith(end, StringComparison.Ordinal))
                return true;
        if (!System.Net.IPAddress.TryParse(host, out var ip))
            return false;
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
            return b[0] is 0 or 10 or 127 || b[0] == 100 && b[1] >= 64 && b[1] < 128 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] >= 16 && b[1] < 32 || b[0] == 192 && b[1] == 168 || b[0] >= 224;
        return System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC || b[0] == 0xFF;
    }

    /// <summary>Two web lines are the same page: their query and fragment aside, since a page's own address reaches other players without those.</summary>
    public static bool SamePage(string a, string b) =>
        a.AsSpan(0, Cut(a)).TrimEnd('/').SequenceEqual(b.AsSpan(0, Cut(b)).TrimEnd('/'));

    private static int Cut(string s)
    {
        var at = s.IndexOfAny(['?', '#']);
        return at < 0 ? s.Length : at;
    }

    /// <summary>What a pasted address comes to: a list line, or null with <paramref name="why"/> saying what is wrong with it.</summary>
    public static string? FromUrl(string text, out string why)
    {
        why = "";
        text = text.Trim();
        if (text.Length == 0)
        {
            why = "paste a YouTube link first";
            return null;
        }
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http") || u.UserInfo.Length > 0 || !u.IsDefaultPort)
        {
            why = "that isn't a link";
            return null;
        }
        var host = u.IdnHost.ToLowerInvariant();
        string? id = null;
        if (host == "youtu.be")
            id = Segment(u.AbsolutePath, 0);
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com")
        {
            var first = Segment(u.AbsolutePath, 0);
            id = first == "watch" ? Query(u.Query, "v") : first is "embed" or "shorts" or "live" or "v" ? Segment(u.AbsolutePath, 1) : null;
        }
        else
        {
            why = "only youtube.com and youtu.be links play here";
            return null;
        }
        if (id == null || !IsId(id))
        {
            why = "that link doesn't name a video";
            return null;
        }
        return Link + id;
    }

    private static string? Segment(string path, int index)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return index < parts.Length ? parts[index] : null;
    }

    private static string? Query(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair.AsSpan(0, eq).SequenceEqual(key))
                return pair[(eq + 1)..];
        }
        return null;
    }

    /// <summary>A title from a page as text fit for the panels and the HUD: ASCII on one line.</summary>
    public static string Printable(string title, int max = 60)
    {
        var b = new StringBuilder(Math.Min(title.Length, max));
        foreach (var ch in title)
            if (ch is >= ' ' and <= '~' && b.Length < max)
                b.Append(ch);
        return b.ToString().Trim();
    }
}
