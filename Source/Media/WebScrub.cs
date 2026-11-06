namespace CallOfSlop;

/// <summary>
/// What of a web address may leave this PC (docs/sandbox/MEDIA.md, "Browser Screens"). Browser Screens share the address with its query and fragment, so a search or a watch page opens the same for everyone, but a login's
/// redirect can carry its secret there: query and fragment parameters with secret-sounding names are taken out, and so are values shaped like a JWT. It goes by names, so a secret under an innocent name gets through.
/// One file, compiled into the game and the browser helper alike.
/// </summary>
public static class WebScrub
{
    private static readonly HashSet<string> Words = new(StringComparer.Ordinal)
    {
        "token", "auth", "oauth", "session", "sess", "sid", "key", "apikey", "code", "sig", "signature", "password", "passwd", "pwd", "pass", "secret", "state", "jwt",
        "otp", "nonce", "ticket", "saml", "credential", "credentials", "bearer", "cookie", "csrf", "xsrf", "hash", "hmac",
    };

    private static readonly string[] Inside = ["token", "password", "passwd", "secret", "session", "sessid", "jwt", "apikey", "oauth", "credential"];

    /// <summary>Is a parameter's name one to keep off the wire? By whole words of it (access_token, api-key, authCode), or a telling word anywhere in it (sessionid).</summary>
    public static bool SecretName(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var w in Inside)
            if (lower.Contains(w, StringComparison.Ordinal))
                return true;
        var from = 0;
        for (var i = 1; i <= name.Length; i++)
        {
            var cut = i == name.Length || !char.IsLetterOrDigit(name[i]) || char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]);
            if (!cut)
                continue;
            var at = from;
            while (at < i && !char.IsLetterOrDigit(name[at]))
                at++;
            if (at < i && Words.Contains(name[at..i].ToLowerInvariant()))
                return true;
            from = i;
        }
        return false;
    }

    private static bool SecretValue(string value) => value.StartsWith("eyJ", StringComparison.Ordinal) && value.Length > 40; // (a JWT, under any name)

    /// <summary>The address with its secrets out. Everything else stays as it was, in its order.</summary>
    public static string Clean(string url)
    {
        var hash = url.IndexOf('#');
        var fragment = hash >= 0 ? url[(hash + 1)..] : null;
        var rest = hash >= 0 ? url[..hash] : url;
        var mark = rest.IndexOf('?');
        var query = mark >= 0 ? rest[(mark + 1)..] : null;
        var path = mark >= 0 ? rest[..mark] : rest;
        var b = new System.Text.StringBuilder(path);
        if (query != null && Pairs(query) is { Length: > 0 } q)
            b.Append('?').Append(q);
        if (fragment != null && (fragment.Contains('=') ? Pairs(fragment) : fragment) is { Length: > 0 } f)
            b.Append('#').Append(f);
        return b.ToString();
    }

    private static string Pairs(string text)
    {
        var kept = new System.Text.StringBuilder(text.Length);
        foreach (var pair in text.Split('&'))
        {
            var eq = pair.IndexOf('=');
            var name = eq >= 0 ? pair[..eq] : pair;
            if (SecretName(Uri.UnescapeDataString(name)) || eq >= 0 && SecretValue(pair[(eq + 1)..]))
                continue;
            if (kept.Length > 0)
                kept.Append('&');
            kept.Append(pair);
        }
        return kept.ToString();
    }
}
