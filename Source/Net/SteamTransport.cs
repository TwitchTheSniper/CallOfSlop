using Brutal.RakNetApi;
using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// The Steam transport (Phase 2 of the Steam migration): Steam networking sockets, peer to peer by Steam ID, through
/// Valve's relay network when a direct route is not there (no port forwarding, UPnP or STUN needed). The host listens
/// on virtual port 0 (<c>CreateListenSocketP2P</c>) with one poll group for every client; a client <c>ConnectP2P</c>s
/// to the host's Steam ID. Each connection carries three lanes (<see cref="NetLanes"/>): reliable gameplay, unreliable
/// snapshots sent at once, and chat / voice / bulk. Connections are <see cref="RakNetGUID"/>s whose <c>G</c> is the
/// peer's Steam ID, and connection events become RakNet's one-byte system packets, so the game runs unchanged.
/// Everything goes through <see cref="ISteamBackend"/>: the self-test runs this transport on the recorded backend.
/// </summary>
public sealed class SteamTransport : INetTransport
{
    public const int VirtualPort = 0;
    /// <summary>Steam's largest message (k_cbMaxSteamNetworkingSocketsMessageSizeSend); the game's are a few KB at most.</summary>
    public const int MaxMessageSize = 512 * 1024;
    /// <summary>Our close reasons (k_ESteamNetConnectionEnd_App_Min + n), shown in the peer's end debug string.</summary>
    public const int EndKicked = 1000, EndFull = 1001, EndDuplicate = 1002, EndShutdown = 1003, EndBanned = 1004;

    private sealed class Conn
    {
        public uint Handle;
        public ulong SteamId;
        public bool Connected;
        /// <summary>The peer closed or the route broke: what it sent before still waits in Steam's queue, so the
        /// connection is drained once more, then the game hears the event and the handle is released.</summary>
        public byte Closing;
    }

    private readonly ISteamBackend _steam;
    private readonly int _maxClients;
    private readonly uint _listen, _pollGroup;
    private uint _clientConn;
    private readonly Dictionary<uint, Conn> _conns = new();
    private readonly Dictionary<ulong, Conn> _byId = new();
    private readonly List<Conn> _connected = new();
    private readonly Queue<(ulong SteamId, byte Event)> _events = new();
    private readonly List<Conn> _closing = new();
    private readonly SteamMessageHandler _onMessage;
    private NetPeer.PacketHandler? _handler;
    private bool _disposed;

    public bool IsServer { get; }
    public string Kind => "steam";
    public RakNetGUID ServerGuid { get; private set; } = RakNetGUID.UNASSIGNED_RAKNET_GUID;
    public int ConnectionCount => _connected.Count;
    /// <summary>The host's Steam ID this client connects to (0 on the host).</summary>
    public ulong HostSteamId { get; }
    /// <summary>Why the last connection ended, for the status line ("" while fine).</summary>
    public string LastEnd { get; private set; } = "";

    private SteamTransport(ISteamBackend steam, bool server, int maxClients, ulong hostSteamId)
    {
        _steam = steam;
        IsServer = server;
        _maxClients = maxClients;
        HostSteamId = hostSteamId;
        _onMessage = OnMessage;
        _steam.ConnectionChanged += OnConnectionChanged;
        _steam.InitRelayNetworkAccess();
        if (server)
        {
            _listen = _steam.CreateListenSocketP2P(VirtualPort);
            _pollGroup = _steam.CreatePollGroup();
            if (_listen == 0 || _pollGroup == 0)
            {
                Dispose();
                throw new InvalidOperationException("Steam: could not open a listen socket (is Steam running?)");
            }
        }
        else
        {
            _clientConn = _steam.ConnectP2P(hostSteamId, VirtualPort);
            if (_clientConn == 0)
            {
                Dispose();
                throw new InvalidOperationException($"Steam: could not start connecting to {hostSteamId}");
            }
            var c = new Conn { Handle = _clientConn, SteamId = hostSteamId };
            _conns[_clientConn] = c;
            _byId[hostSteamId] = c;
            ServerGuid = GuidOf(hostSteamId);
        }
    }

    /// <summary>The host: listens for Steam friends (and anyone with the lobby) on virtual port 0.</summary>
    public static SteamTransport Host(ISteamBackend steam, int maxClients) => new(steam, true, maxClients, 0);

    /// <summary>A client: connects to the host's Steam ID.</summary>
    public static SteamTransport Join(ISteamBackend steam, ulong hostSteamId) => new(steam, false, 0, hostSteamId);

    public static RakNetGUID GuidOf(ulong steamId) => new() { G = steamId, SystemIndex = 0 };

    private void OnConnectionChanged(SteamConnectionChange change)
    {
        if (_disposed)
            return;
        if (IsServer)
            OnServerChange(change);
        else
            OnClientChange(change);
    }

    private void OnServerChange(SteamConnectionChange ch)
    {
        switch (ch.State)
        {
            case SteamConnState.Connecting when ch.OldState == SteamConnState.None:
            {
                if (ch.ListenSocket != _listen && ch.ListenSocket != 0)
                    return; // not ours
                if (_conns.ContainsKey(ch.Conn))
                    return;
                if (ch.RemoteSteamId == 0)
                {
                    _steam.CloseConnection(ch.Conn, EndDuplicate, "no identity", false);
                    return;
                }
                if (_byId.ContainsKey(ch.RemoteSteamId))
                {
                    _steam.CloseConnection(ch.Conn, EndDuplicate, "already connected", false);
                    return;
                }
                if (_conns.Count >= _maxClients)
                {
                    _steam.CloseConnection(ch.Conn, EndFull, "server full", false);
                    return;
                }
                if (!_steam.AcceptConnection(ch.Conn))
                {
                    _steam.CloseConnection(ch.Conn, EndFull, "accept failed", false);
                    return;
                }
                _steam.SetConnectionPollGroup(ch.Conn, _pollGroup);
                _steam.ConfigureConnectionLanes(ch.Conn, NetLanes.Priorities, NetLanes.Weights);
                var c = new Conn { Handle = ch.Conn, SteamId = ch.RemoteSteamId };
                _conns[ch.Conn] = c;
                _byId[ch.RemoteSteamId] = c;
                break;
            }
            case SteamConnState.Connected:
            {
                if (!_conns.TryGetValue(ch.Conn, out var c) || c.Connected)
                    return;
                c.Connected = true;
                _connected.Add(c);
                _events.Enqueue((c.SteamId, (byte)DefaultMessageIDTypes.NewIncomingConnection));
                break;
            }
            case SteamConnState.ClosedByPeer:
            case SteamConnState.ProblemDetectedLocally:
            {
                if (!_conns.TryGetValue(ch.Conn, out var c) || c.Closing != 0)
                    return;
                if (c.Connected)
                    BeginClosing(c, (byte)(ch.State == SteamConnState.ClosedByPeer ? DefaultMessageIDTypes.DisconnectionNotification : DefaultMessageIDTypes.ConnectionLost));
                else
                {
                    Forget(c);
                    _steam.CloseConnection(ch.Conn, 0, "", false);
                }
                Console.WriteLine($"[steam] {c.SteamId} {(ch.State == SteamConnState.ClosedByPeer ? "left" : "lost")}: {ch.EndReason} {ch.EndDebug}");
                break;
            }
        }
    }

    private void OnClientChange(SteamConnectionChange ch)
    {
        if (ch.Conn != _clientConn || !_conns.TryGetValue(ch.Conn, out var c))
            return;
        switch (ch.State)
        {
            case SteamConnState.Connected:
                if (c.Connected)
                    return;
                c.Connected = true;
                _connected.Add(c);
                _steam.ConfigureConnectionLanes(ch.Conn, NetLanes.Priorities, NetLanes.Weights);
                _events.Enqueue((c.SteamId, (byte)DefaultMessageIDTypes.ConnectionRequestAccepted));
                break;
            case SteamConnState.ClosedByPeer:
            case SteamConnState.ProblemDetectedLocally:
                if (c.Closing != 0)
                    return;
                LastEnd = $"{ch.EndReason} {ch.EndDebug}".Trim();
                BeginClosing(c, (byte)(
                    !c.Connected ? (ch.EndReason == EndFull ? DefaultMessageIDTypes.NoFreeIncomingConnections : DefaultMessageIDTypes.ConnectionAttemptFailed)
                    : ch.State == SteamConnState.ClosedByPeer ? DefaultMessageIDTypes.DisconnectionNotification : DefaultMessageIDTypes.ConnectionLost));
                Console.WriteLine($"[steam] connection to {c.SteamId} ended: {LastEnd}");
                break;
        }
    }

    /// <summary>A connection ended: no more sends to it; its last messages are drained on the next Poll, then the event goes out.</summary>
    private void BeginClosing(Conn c, byte evt)
    {
        c.Closing = evt;
        _byId.Remove(c.SteamId);
        _connected.Remove(c);
        _closing.Add(c);
    }

    private void Forget(Conn c)
    {
        _conns.Remove(c.Handle);
        _byId.Remove(c.SteamId);
        _connected.Remove(c);
    }

    public void Poll(NetPeer.PacketHandler handler)
    {
        if (_disposed)
            return;
        Span<byte> one = stackalloc byte[1];
        while (_events.TryDequeue(out var e))
        {
            one[0] = e.Event;
            handler(GuidOf(e.SteamId), one);
        }
        _handler = handler;
        try
        {
            for (var round = 0; round < 8; round++) // at most 512 messages a poll: a flood never stalls a tick
            {
                var n = IsServer ? _steam.ReceiveMessages(_pollGroup, 0, _onMessage, 64) : _clientConn == 0 ? 0 : _steam.ReceiveMessages(0, _clientConn, _onMessage, 64);
                if (n < 64)
                    break;
            }
        }
        finally
        {
            _handler = null;
        }
        while (_events.TryDequeue(out var e)) // events raised while receiving (a close during a callback)
        {
            one[0] = e.Event;
            handler(GuidOf(e.SteamId), one);
        }
        if (_closing.Count > 0)
        {
            // Ended connections: drained above (a kick's reason, a last state), now the game hears it and Steam frees the handle.
            var ended = _closing.ToArray();
            _closing.Clear();
            foreach (var c in ended)
            {
                if (!IsServer)
                    _steam.ReceiveMessages(0, c.Handle, _onMessage, 64); // the client's connection is polled by handle: once more after it closed
                _conns.Remove(c.Handle);
                _steam.CloseConnection(c.Handle, 0, "", false);
                if (!IsServer && _clientConn == c.Handle)
                    _clientConn = 0;
                one[0] = c.Closing;
                handler(GuidOf(c.SteamId), one);
            }
        }
    }

    private void OnMessage(uint conn, ulong steamId, ReadOnlySpan<byte> data)
    {
        if (!_conns.TryGetValue(conn, out var c) || !c.Connected || data.Length == 0)
            return;
        _handler?.Invoke(GuidOf(c.SteamId), data);
    }

    public void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (_disposed || data.Length == 0 || !_byId.TryGetValue(target.G, out var c))
            return;
        Send(c, data, reliability);
    }

    private void Send(Conn c, ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (data.Length > MaxMessageSize)
        {
            Console.WriteLine($"[steam] dropped a {data.Length}-byte message (Msg {data[0]}): over Steam's {MaxMessageSize}-byte limit");
            return;
        }
        _steam.SendMessage(c.Handle, data, NetLanes.SteamSendFlags(reliability), (int)NetLanes.For(reliability, data[0]));
    }

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (_disposed || data.Length == 0)
            return;
        for (var i = 0; i < _connected.Count; i++)
            Send(_connected[i], data, reliability);
    }

    public void Kick(RakNetGUID target)
    {
        if (_disposed || !_byId.TryGetValue(target.G, out var c))
            return;
        Forget(c);
        _steam.CloseConnection(c.Handle, EndKicked, "kicked by the host", true); // linger: what was sent (Msg.Full, a reason) still arrives
    }

    /// <summary>Is this Steam user connected (or connecting) here?</summary>
    public bool Knows(RakNetGUID target) => _byId.ContainsKey(target.G);

    public int PingTo(RakNetGUID target) =>
        _byId.TryGetValue(target.G, out var c) && _steam.ConnectionStats(c.Handle, out var s) ? s.PingMs : 0;

    public ulong SteamIdOf(RakNetGUID target) => _byId.ContainsKey(target.G) ? target.G : 0;

    public bool StatsFor(RakNetGUID target, out NetConnectionStats stats)
    {
        stats = default;
        return _byId.TryGetValue(target.G, out var c) && _steam.ConnectionStats(c.Handle, out stats);
    }

    public bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent) => _steam.SetFakeNetwork(extraPingMs, lossPercent);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _steam.ConnectionChanged -= OnConnectionChanged;
        foreach (var c in _conns.Values.ToArray())
            _steam.CloseConnection(c.Handle, EndShutdown, IsServer ? "the host closed the game" : "left", true);
        _conns.Clear();
        _byId.Clear();
        _connected.Clear();
        if (_pollGroup != 0)
            _steam.DestroyPollGroup(_pollGroup);
        if (_listen != 0)
            _steam.CloseListenSocket(_listen);
        _clientConn = 0;
    }
}

/// <summary>
/// A host on Steam listens both ways: RakNet on the game port (LAN, invite codes, server lists, friends without Steam
/// on the same build) and Steam sockets for friends on Steam. Each peer lives on exactly one side; sends are routed by
/// which side knows the connection, and broadcasts go to both.
/// </summary>
public sealed class DualTransport : INetTransport
{
    public RakNetTransport RakNet { get; }
    public SteamTransport Steam { get; }

    public DualTransport(RakNetTransport rakNet, SteamTransport steam)
    {
        if (!rakNet.IsServer || !steam.IsServer)
            throw new ArgumentException("a dual transport is for a host");
        RakNet = rakNet;
        Steam = steam;
    }

    public bool IsServer => true;
    public string Kind => "raknet+steam";
    public RakNetGUID ServerGuid => RakNetGUID.UNASSIGNED_RAKNET_GUID;
    public int ConnectionCount => RakNet.ConnectionCount + Steam.ConnectionCount;

    public void Poll(NetPeer.PacketHandler handler)
    {
        RakNet.Poll(handler);
        Steam.Poll(handler);
    }

    public void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (Steam.Knows(target))
            Steam.SendTo(target, data, reliability);
        else
            RakNet.SendTo(target, data, reliability);
    }

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        RakNet.Broadcast(data, reliability);
        Steam.Broadcast(data, reliability);
    }

    public void Kick(RakNetGUID target)
    {
        if (Steam.Knows(target))
            Steam.Kick(target);
        else
            RakNet.Kick(target);
    }

    public int PingTo(RakNetGUID target) => Steam.Knows(target) ? Steam.PingTo(target) : RakNet.PingTo(target);

    public ulong SteamIdOf(RakNetGUID target) => Steam.SteamIdOf(target);

    public bool StatsFor(RakNetGUID target, out NetConnectionStats stats) =>
        Steam.Knows(target) ? Steam.StatsFor(target, out stats) : RakNet.StatsFor(target, out stats);

    public bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent)
    {
        var a = RakNet.SimulateBadNetwork(extraPingMs, jitterMs, lossPercent);
        var b = Steam.SimulateBadNetwork(extraPingMs, jitterMs, lossPercent);
        return a || b;
    }

    public void Dispose()
    {
        Steam.Dispose();
        RakNet.Dispose();
    }
}
