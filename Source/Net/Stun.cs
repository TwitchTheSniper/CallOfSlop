using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CallOfSlop;

/// <summary>
/// A minimal STUN client (RFC 5389 Binding request) to learn this PC's public IPv4 address for invite codes, using
/// only .NET sockets. It asks two public STUN servers at once and takes the first valid answer; no answer within the
/// timeout just means "unknown" (the invite falls back to the UPnP or LAN address). Only a 20-byte request with a
/// random transaction id is sent: no name, no game data. Turned off with Settings > Multiplayer "Find my public address".
/// </summary>
public static class Stun
{
    public const uint MagicCookie = 0x2112A442;

    /// <summary>Two well-known public STUN servers (Google's and Cloudflare's); either one answering is enough.</summary>
    public static readonly (string Host, int Port)[] DefaultServers = [("stun.l.google.com", 19302), ("stun.cloudflare.com", 3478)];

    private const ushort BindingRequest = 0x0001, BindingSuccess = 0x0101;
    private const ushort AttrMappedAddress = 0x0001, AttrXorMappedAddress = 0x0020, AttrXorMappedAddressOld = 0x8020;

    /// <summary>A Binding request (20 bytes, no attributes) with the given 12-byte transaction id.</summary>
    public static byte[] BuildRequest(ReadOnlySpan<byte> txId)
    {
        var b = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(b, BindingRequest);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4), MagicCookie);
        txId[..12].CopyTo(b.AsSpan(8));
        return b;
    }

    /// <summary>
    /// The public IPv4 endpoint in a Binding success response to <paramref name="txId"/>, or null for anything else
    /// (wrong type or transaction, bad lengths, IPv6 only, truncated attributes). Never throws.
    /// </summary>
    public static IPEndPoint? ParseResponse(ReadOnlySpan<byte> msg, ReadOnlySpan<byte> txId)
    {
        if (msg.Length < 20 || txId.Length < 12)
            return null;
        if (BinaryPrimitives.ReadUInt16BigEndian(msg) != BindingSuccess)
            return null;
        int len = BinaryPrimitives.ReadUInt16BigEndian(msg[2..]);
        if (len % 4 != 0 || 20 + len > msg.Length)
            return null;
        if (BinaryPrimitives.ReadUInt32BigEndian(msg[4..]) != MagicCookie || !msg.Slice(8, 12).SequenceEqual(txId[..12]))
            return null;
        IPEndPoint? plain = null;
        var p = 20;
        var end = 20 + len;
        while (p + 4 <= end)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(msg[p..]);
            int alen = BinaryPrimitives.ReadUInt16BigEndian(msg[(p + 2)..]);
            p += 4;
            if (p + alen > end)
                return null;
            var v = msg.Slice(p, alen);
            if (alen >= 8 && v[1] == 0x01) // IPv4
            {
                var port = BinaryPrimitives.ReadUInt16BigEndian(v[2..]);
                var addr = BinaryPrimitives.ReadUInt32BigEndian(v[4..]);
                if (type is AttrXorMappedAddress or AttrXorMappedAddressOld)
                    return Endpoint(addr ^ MagicCookie, (ushort)(port ^ (MagicCookie >> 16)));
                if (type == AttrMappedAddress)
                    plain ??= Endpoint(addr, port);
            }
            p += (alen + 3) & ~3; // attributes are padded to 4 bytes
        }
        return plain;
    }

    private static IPEndPoint? Endpoint(uint addr, ushort port)
    {
        if (port == 0 || addr == 0)
            return null;
        Span<byte> ip = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(ip, addr);
        return new IPEndPoint(new IPAddress(ip), port);
    }

    /// <summary>
    /// Asks every server in <paramref name="servers"/> (sent again once halfway through) and returns the first valid
    /// answer, or null after <paramref name="timeout"/>. Never throws (DNS failures, no network: null).
    /// </summary>
    public static async Task<IPEndPoint?> QueryAsync(IReadOnlyList<(string Host, int Port)> servers, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            var tx = RandomNumberGenerator.GetBytes(12);
            var request = BuildRequest(tx);
            var targets = new List<IPEndPoint>();
            foreach (var (host, port) in servers.Take(4))
            {
                try
                {
                    var ip = IPAddress.TryParse(host, out var lit) ? lit
                        : (await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cts.Token)).FirstOrDefault();
                    if (ip != null)
                        targets.Add(new IPEndPoint(ip, port));
                }
                catch (Exception) when (!cts.IsCancellationRequested)
                {
                    // this server's name didn't resolve: try the others
                }
            }
            if (targets.Count == 0)
                return null;
            var resendAt = DateTime.UtcNow + timeout / 2;
            var resent = false;
            foreach (var t in targets)
                await udp.SendAsync(request, t, cts.Token);
            while (!cts.IsCancellationRequested)
            {
                using var slice = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                if (!resent)
                    slice.CancelAfter(resendAt - DateTime.UtcNow is var w && w > TimeSpan.Zero ? w : TimeSpan.FromMilliseconds(1));
                try
                {
                    var r = await udp.ReceiveAsync(slice.Token);
                    if (targets.Any(t => t.Equals(r.RemoteEndPoint)) && ParseResponse(r.Buffer, tx) is { } ep)
                        return ep;
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    resent = true; // one resend only (UDP loses packets)
                    foreach (var t in targets)
                        await udp.SendAsync(request, t, cts.Token);
                }
                catch (SocketException)
                {
                    // e.g. ICMP port unreachable from one server: keep waiting for the other
                }
            }
        }
        catch (Exception)
        {
            // timeout, no network
        }
        return null;
    }
}
