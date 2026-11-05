using System.Buffers;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Free travel (docs/space/FREE-TRAVEL.md): a connection to a planetoid the host runs in its own process, carried over
/// the connection a player already has to the host. A planetoid's game on the host serves over one of these (its
/// <see cref="Host"/> side, made by <see cref="OnHost"/>), and a traveller's game joins over another (<see cref="Joining"/>):
/// every packet goes up the traveller's link as <c>Msg.Relay</c> tagged with the planetoid's number, and the host's main
/// game hands it straight to that planetoid's game here, and back the same way. One port, no new connections, no NAT.
/// <para>
/// Connections are named by the player's id on the main host (<see cref="GuidFor"/>; a traveller names the planetoid by
/// its number the same way), connection events are made up as RakNet's one-byte system packets, so <c>Game.OnPacket</c>
/// sees a relayed peer as any other. Packets wait in a queue of pooled buffers between the link's receive and this game's
/// poll.
/// </para>
/// </summary>
public sealed class RelayTransport : INetTransport
{
    /// <summary>The game connected to the main host (a traveller's side) or the host's main game (a planetoid's side).</summary>
    public readonly Game Link;

    /// <summary>Client: the planetoid's number on the host. Host side: this planetoid's number.</summary>
    public readonly byte Place;

    private readonly HashSet<byte> _peers = new(8);
    private readonly List<(byte From, byte[] Buf, int Len)> _in = new(256), _drain = new(256);
    private static readonly byte[] Accepted = [(byte)DefaultMessageIDTypes.ConnectionRequestAccepted];
    private static readonly byte[] Incoming = [(byte)DefaultMessageIDTypes.NewIncomingConnection];
    private static readonly byte[] Gone = [(byte)DefaultMessageIDTypes.DisconnectionNotification];
    private bool _closed;

    private RelayTransport(Game link, bool server, byte place)
    {
        Link = link;
        IsServer = server;
        Place = place;
    }

    /// <summary>A planetoid's game on the host: travellers connect as the main game routes them here.</summary>
    public static RelayTransport OnHost(Game host, byte place) => new(host, true, place);

    /// <summary>A traveller's side: connected to planetoid number <paramref name="place"/> on the host at once.</summary>
    public static RelayTransport Joining(Game link, byte place)
    {
        var t = new RelayTransport(link, false, place);
        link.Relay = t;
        t._peers.Add(place);
        t.Queue(place, Accepted);
        link.RelayControl(Game.RelayOpen, place);
        return t;
    }

    /// <summary>A relayed peer's connection id: its player id on the main host, in a range no transport uses.</summary>
    public static RakNetGUID GuidFor(byte hostId) => new() { G = 0x7E1A_7000_0000_0000UL | hostId, SystemIndex = hostId };

    /// <summary>The main host id behind a relayed connection id (255: not one of ours).</summary>
    public static byte HostIdOf(RakNetGUID g) => (g.G & 0xFFFF_FFFF_FFFF_FF00UL) == 0x7E1A_7000_0000_0000UL ? (byte)g.G : (byte)255;

    public bool IsServer { get; }
    public string Kind => "relay";
    public RakNetGUID ServerGuid => IsServer ? RakNetGUID.UNASSIGNED_RAKNET_GUID : GuidFor(Place);
    public int ConnectionCount => _peers.Count;

    /// <summary>The travellers connected now (host side), by main host id.</summary>
    public IReadOnlyCollection<byte> Peers => _peers;

    // ---- From the link -----------------------------------------------------------------------------------------------

    /// <summary>The link heard a packet for this game from <paramref name="from"/>.</summary>
    internal void Deliver(byte from, ReadOnlySpan<byte> data)
    {
        if (_closed || data.Length == 0 || !_peers.Contains(from))
            return;
        Queue(from, data);
    }

    /// <summary>The host says <paramref name="from"/> opened a connection to this planetoid.</summary>
    internal void Opened(byte from)
    {
        if (_closed || !IsServer || !_peers.Add(from))
            return;
        Queue(from, Incoming);
    }

    /// <summary>The host says <paramref name="from"/> is gone (left, dropped, or the planetoid closed).</summary>
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
            Send(to, data, reliability);
    }

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (_closed)
            return;
        foreach (var to in _peers)
            Send(to, data, reliability);
    }

    private void Send(byte to, ReadOnlySpan<byte> data, PacketReliability how)
    {
        if (IsServer)
            Link.PlaceRelaySend(Place, to, data, how); // (the host's main game, to that traveller's connection)
        else
            Link.RelaySend(to, data, how);
    }

    private void Control(byte kind, byte other)
    {
        if (IsServer)
            Link.PlaceRelayControl(Place, kind, other);
        else
            Link.RelayControl(kind, other);
    }

    public void Kick(RakNetGUID target)
    {
        var to = HostIdOf(target);
        if (_closed || !_peers.Remove(to))
            return;
        Control(Game.RelayShut, to);
        Queue(to, Gone); // (as RakNet does: the kicked connection's notice comes back to this side too)
    }

    public int PingTo(RakNetGUID target) => IsServer
        ? Link.ClientGuid(HostIdOf(target)) is { } g ? Link.Net.PingTo(g) : 0 // (the traveller's own connection to the host)
        : Link.Net.PingTo(Link.Net.ServerGuid);

    public ulong SteamIdOf(RakNetGUID target) => 0;

    public bool StatsFor(RakNetGUID target, out NetConnectionStats stats)
    {
        stats = default;
        var at = IsServer ? Link.ClientGuid(HostIdOf(target)) : Link.Net.ServerGuid;
        if (at is not { } g || !Link.Net.StatsFor(g, out var up))
            return false;
        stats = up;
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
            Control(Game.RelayShut, to);
        _peers.Clear();
        _closed = true;
        foreach (var (_, buf, _) in _in)
            ArrayPool<byte>.Shared.Return(buf);
        _in.Clear();
        if (ReferenceEquals(Link.Relay, this))
            Link.Relay = null;
    }
}
