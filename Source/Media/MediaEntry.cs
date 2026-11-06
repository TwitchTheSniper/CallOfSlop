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
    public const string Link = "yt:", Game = "game:";
    public const int IdChars = 11;

    /// <summary>The longest line: "game:" and the longest track name there may be.</summary>
    public const int MaxChars = 48;

    public static bool IsLink(string entry) => entry.StartsWith(Link, StringComparison.Ordinal);

    public static string VideoId(string entry) => entry[Link.Length..];

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
