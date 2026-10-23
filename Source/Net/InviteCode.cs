using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// What an invite code carries: the host's address and game port, the protocol version of the build that made it, and
/// (optionally) the port of a server list running on the same address (the host's own list, see <see cref="HostListing"/>).
/// </summary>
public readonly record struct Invite(IPAddress Address, ushort Port, byte Protocol, ushort ListPort = 0);

/// <summary>
/// Invite codes and links: a host's public IPv4 address, game port, protocol version and its server list's port packed
/// into 10 bytes (8 of data, 2 of checksum) and written as 16 Crockford base32 characters, "ABCD-EFGH-JKMN-PQRS", or as
/// a link, <c>callofslop://join/ABCD-EFGH-JKMN-PQRS</c>. Typing mistakes are caught by the checksum (1 in 65536 gets
/// through), and an invite from a different build says so instead of failing to connect. See docs/net/SERVER-DISCOVERY.md.
/// </summary>
public static class InviteCode
{
    /// <summary>The URL scheme the invite links use (registered per user only when the player asks, <see cref="UrlProtocol"/>).</summary>
    public const string Scheme = "callofslop";

    /// <summary>The link prefix: <c>callofslop://join/</c>.</summary>
    public const string LinkPrefix = Scheme + "://join/";

    /// <summary>Characters in a code (without the dashes).</summary>
    public const int Length = 16;

    /// <summary>The list port an invite can carry is <see cref="MasterServerApp.DefaultPort"/> + 0..254 (a byte: 0 means "no list").</summary>
    public const ushort ListPortBase = MasterServerApp.DefaultPort;

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford: no I, L, O, U
    private static readonly byte[] Salt = "CallOfSlop invite"u8.ToArray();

    /// <summary>The code for <paramref name="invite"/>, e.g. "ABCD-EFGH-JKMN-PQRS". Throws for a non-IPv4 address.</summary>
    public static string Encode(Invite invite)
    {
        var ip = invite.Address.MapToIPv4().GetAddressBytes();
        var listSlot = invite.ListPort >= ListPortBase && invite.ListPort - ListPortBase < 255 ? (byte)(invite.ListPort - ListPortBase + 1) : (byte)0;
        Span<byte> raw = stackalloc byte[10];
        ip.CopyTo(raw);
        raw[4] = (byte)invite.Port;
        raw[5] = (byte)(invite.Port >> 8);
        raw[6] = invite.Protocol;
        raw[7] = listSlot;
        Checksum(raw[..8], raw[8..]);
        // 80 bits -> 16 characters of 5 bits, most significant first.
        var sb = new StringBuilder(Length + 3);
        for (var i = 0; i < Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                sb.Append('-');
            sb.Append(Alphabet[Bits(raw, i * 5)]);
        }
        return sb.ToString();
    }

    /// <summary>The link form of a code: <c>callofslop://join/CODE</c>.</summary>
    public static string Link(string code) => LinkPrefix + code;

    /// <summary>The 5 bits at <paramref name="bit"/> (from the most significant bit of byte 0).</summary>
    private static int Bits(ReadOnlySpan<byte> raw, int bit)
    {
        var v = 0;
        for (var b = 0; b < 5; b++)
        {
            var at = bit + b;
            v = (v << 1) | ((raw[at >> 3] >> (7 - (at & 7))) & 1);
        }
        return v;
    }

    private static void Checksum(ReadOnlySpan<byte> data, Span<byte> into)
    {
        Span<byte> buf = stackalloc byte[Salt.Length + data.Length];
        Salt.CopyTo(buf);
        data.CopyTo(buf[Salt.Length..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(buf, hash);
        hash[..into.Length].CopyTo(into);
    }

    /// <summary>
    /// Strips a link prefix, dashes and spaces, upper-cases and maps the look-alikes Crockford allows (O -> 0, I and L -> 1).
    /// Returns null if what's left isn't made of code characters only.
    /// </summary>
    private static string? Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 200)
            return null;
        var t = text.Trim();
        foreach (var prefix in new[] { LinkPrefix, Scheme + ":join/", Scheme + "://", Scheme + ":" })
            if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                t = t[prefix.Length..];
                break;
            }
        var sb = new StringBuilder(Length);
        foreach (var raw in t)
        {
            if (raw is '-' or ' ' or '_' or '/' or '\t')
                continue;
            var c = char.ToUpperInvariant(raw) switch { 'O' => '0', 'I' or 'L' => '1', var x => x };
            if (Alphabet.IndexOf(c) < 0 || sb.Length >= 40)
                return null;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>True if <paramref name="text"/> is meant as an invite: a link, or exactly 16 code characters (dashes allowed).</summary>
    public static bool LooksLikeInvite(string? text)
    {
        if (text == null)
            return false;
        if (text.TrimStart().StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains('.') || text.Contains(':'))
            return false;
        return Normalise(text) is { Length: Length };
    }

    /// <summary>Reads a code or link. On failure <paramref name="error"/> says why, in words for the player.</summary>
    public static bool TryDecode(string? text, out Invite invite, out string error)
    {
        invite = default;
        var code = Normalise(text);
        if (code == null)
        {
            error = "That isn't an invite code (codes look like ABCD-EFGH-JKMN-PQRS).";
            return false;
        }
        if (code.Length != Length)
        {
            error = $"An invite code has {Length} letters and digits; that one has {code.Length}. Copy the whole code.";
            return false;
        }
        Span<byte> raw = stackalloc byte[10];
        for (var i = 0; i < Length; i++)
        {
            var v = Alphabet.IndexOf(code[i]);
            for (var b = 0; b < 5; b++)
            {
                var at = i * 5 + b;
                if (((v >> (4 - b)) & 1) != 0)
                    raw[at >> 3] |= (byte)(1 << (7 - (at & 7)));
            }
        }
        Span<byte> check = stackalloc byte[2];
        Checksum(raw[..8], check);
        if (!check.SequenceEqual(raw[8..]))
        {
            error = "That invite code has a typo (its check digits don't match). Copy it again, or ask for the link.";
            return false;
        }
        var ip = new IPAddress(raw[..4]);
        var port = (ushort)(raw[4] | (raw[5] << 8));
        if (raw[0] == 0 || raw[0] >= 224 || port == 0)
        {
            error = "That invite code doesn't hold a usable address.";
            return false;
        }
        invite = new Invite(ip, port, raw[6], raw[7] == 0 ? (ushort)0 : (ushort)(ListPortBase + raw[7] - 1));
        error = "";
        return true;
    }

    /// <summary>"This invite is for a different version..." when <paramref name="invite"/> came from another build, else "".</summary>
    public static string VersionProblem(Invite invite) => invite.Protocol == ServerInfo.ProtocolVersion
        ? ""
        : $"This invite is for a different version of Call of Slop (protocol {invite.Protocol}; yours is {ServerInfo.ProtocolVersion}). Everyone needs the same build.";

    /// <summary>
    /// What the Join box accepts: an invite code or link, "host", or "host:port". The result is where to connect;
    /// <paramref name="invite"/> is set when it was an invite (its list server can be learned, <see cref="ListLearner"/>).
    /// </summary>
    public static bool TryParseJoin(string? text, ushort defaultPort, out string host, out ushort port, out Invite? invite, out string error)
    {
        host = "";
        port = defaultPort;
        invite = null;
        var t = (text ?? "").Trim();
        if (t.Length == 0)
        {
            error = "Type the host's invite code or address.";
            return false;
        }
        if (LooksLikeInvite(t))
        {
            if (!TryDecode(t, out var inv, out error))
                return false;
            if (VersionProblem(inv) is { Length: > 0 } v)
            {
                error = v;
                return false;
            }
            invite = inv;
            host = inv.Address.ToString();
            port = inv.Port;
            return true;
        }
        var clean = Settings.CleanAddress(t);
        var colons = clean.Count(c => c == ':');
        if (colons > 1)
        {
            error = "IPv6 addresses aren't supported; use the host's IPv4 address or an invite code.";
            return false;
        }
        if (colons == 1)
        {
            var at = clean.IndexOf(':');
            if (!ushort.TryParse(clean[(at + 1)..], out var p) || p == 0)
            {
                error = $"'{clean[(at + 1)..]}' isn't a port number (1-65535).";
                return false;
            }
            port = p;
            clean = clean[..at];
        }
        if (clean.Length == 0)
        {
            error = "Type the host's invite code or address.";
            return false;
        }
        host = clean;
        error = "";
        return true;
    }
}
