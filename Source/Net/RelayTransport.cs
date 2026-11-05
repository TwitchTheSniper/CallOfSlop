using System.Buffers;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Free travel, step 3 (docs/space/FREE-TRAVEL.md): a connection to a planetoid kept on another player's PC, carried
/// through the host. The keeper's game hosts over one of these (server side) and a visitor's game joins over another
/// (client side); every packet goes up the player's own link to the main host (<c>Msg.Relay</c>), which passes it to the
/// other player's link, which hands it here. Nobody needs to reach anybody but the host, so no new ports, NAT punching or
/// invites: the cost is twice the trip to the host, and the host carries the planetoid's traffic.
/// <para>
/// Connections are named by the other player's id on the main host (<see cref="GuidFor"/>), connection events are made
/// up as RakNet's one-byte system packets, so <c>Game.OnPacket</c> sees a relayed peer as any other. Packets wait in a
/// queue of pooled buffers between the link's receive and this game's poll.
/// </para>
/// </summary>
public sealed class RelayTransport : INetTransport
{
    /// <summary>The game connected to the main host that carries this one's packets.</summary>
    public readonly Game Link;

    /// <summary>Client: the keeper's id on the main host.</summary>
    public readonly byte Keeper;

    private readonly HashSet<byte> _peers = new(8);
    private readonly List<(byte From, byte[] Buf, int Len)> _in = new(256), _drain = new(256);
    private static readonly byte[] Accepted = [(byte)DefaultMessageIDTypes.ConnectionRequestAccepted];
    private static readonly byte[] Incoming = [(byte)DefaultMessageIDTypes.NewIncomingConnection];
    private static readonly byte[] Gone = [(byte)DefaultMessageIDTypes.DisconnectionNotification];
    private bool _closed;

    private RelayTransport(Game link, bool server, byte keeper)
    {
        Link = link;
        IsServer = server;
        Keeper = keeper;
    }

    /// <summary>The keeper's side: visitors connect as the host routes them here.</summary>
    public static RelayTransport Hosting(Game link)
    {
        var t = new RelayTransport(link, true, 255);
        link.Relay = t;
        return t;
    }

    /// <summary>A visitor's side: connected to the keeper (main host id <paramref name="keeper"/>) at once.</summary>
    public static RelayTransport Joining(Game link, byte keeper)
    {
        var t = new RelayTransport(link, false, keeper);
        link.Relay = t;
        t._peers.Add(keeper);
        t.Queue(keeper, Accepted);
        link.RelayControl(Game.RelayOpen, keeper);
        return t;
    }

    /// <summary>A relayed peer's connection id: its player id on the main host, in a range no transport uses.</summary>
    public static RakNetGUID GuidFor(byte hostId) => new() { G = 0x7E1A_7000_0000_0000UL | hostId, SystemIndex = hostId };

    /// <summary>The main host id behind a relayed connection id (255: not one of ours).</summary>
    public static byte HostIdOf(RakNetGUID g) => (g.G & 0xFFFF_FFFF_FFFF_FF00UL) == 0x7E1A_7000_0000_0000UL ? (byte)g.G : (byte)255;

    public bool IsServer { get; }
    public string Kind => "relay";
    public RakNetGUID ServerGuid => IsServer ? RakNetGUID.UNASSIGNED_RAKNET_GUID : GuidFor(Keeper);
    public int ConnectionCount => _peers.Count;

    /// <summary>The visitors connected now (keeper side), by main host id.</summary>
    public IReadOnlyCollection<byte> Peers => _peers;

    // ---- From the link -----------------------------------------------------------------------------------------------

    /// <summary>The link heard a packet for this game from <paramref name="from"/>.</summary>
    internal void Deliver(byte from, ReadOnlySpan<byte> data)
    {
        if (_closed || data.Length == 0 || !_peers.Contains(from))
            return;
        Queue(from, data);
    }

    /// <summary>The host says <paramref name="from"/> opened a connection to this keeper.</summary>
    internal void Opened(byte from)
    {
        if (_closed || !IsServer || !_peers.Add(from))
            return;
        Queue(from, Incoming);
    }

    /// <summary>The host says <paramref name="from"/> is gone (left, dropped, or the keeper closed).</summary>
    internal void Closed(byte from)
    {
        if (_closed || !_peers.Remove(from))
            return;
        Queue(from, Gone);
    }

    private void Queue(byte from, ReadOnlySpan<byte> data)
    {
        var buf = ArrayPool<byte>.Shared.Rent(data.Length);
        data.CopyTo(buf);
        _in.Add((from, buf, data.Length));
    }

    // ---- INetTransport -----------------------------------------------------------------------------------------------

    public void Poll(NetPeer.PacketHandler handler)
    {
        if (_in.Count == 0)
            return;
        _drain.AddRange(_in);
        _in.Clear();
        try
        {
            foreach (var (from, buf, len) in _drain)
                handler(GuidFor(from), buf.AsSpan(0, len));
        }
        finally
        {
            foreach (var (_, buf, _) in _drain)
                ArrayPool<byte>.Shared.Return(buf);
            _drain.Clear();
        }
    }

    public void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        var to = HostIdOf(target);
        if (!_closed && to != 255 && _peers.Contains(to))
            Link.RelaySend(to, data, reliability);
    }

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (_closed)
            return;
        foreach (var to in _peers)
            Link.RelaySend(to, data, reliability);
    }

    public void Kick(RakNetGUID target)
    {
        var to = HostIdOf(target);
        if (_closed || !_peers.Remove(to))
            return;
        Link.RelayControl(Game.RelayShut, to);
        Queue(to, Gone); // (as RakNet does: the kicked connection's notice comes back to this side too)
    }

    public int PingTo(RakNetGUID target) => Link.Net.PingTo(Link.Net.ServerGuid) * 2; // (up to the host and down again; the far side's own leg is about the same)

    public ulong SteamIdOf(RakNetGUID target) => 0;

    public bool StatsFor(RakNetGUID target, out NetConnectionStats stats)
    {
        stats = default;
        if (!Link.Net.StatsFor(Link.Net.ServerGuid, out var up))
            return false;
        stats = up;
        stats.PingMs = up.PingMs * 2;
        stats.Relayed = true;
        stats.Transport = "relay";
        return true;
    }

    public bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent) => false; // (the link carries it: set it there)

    public void Dispose()
    {
        if (_closed)
            return;
        foreach (var to in _peers)
            Link.RelayControl(Game.RelayShut, to);
        _peers.Clear();
        _closed = true;
        foreach (var (_, buf, _) in _in)
            ArrayPool<byte>.Shared.Return(buf);
        _in.Clear();
        if (ReferenceEquals(Link.Relay, this))
            Link.Relay = null;
    }
}
