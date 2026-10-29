using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The RakNet transport (BRUTAL's RakNet over UDP): what every game used before Steam, and what every self-test, LAN
/// game and GitHub build still uses. Connection ids are RakNet's own GUIDs; system events are RakNet's own packets,
/// passed through as they come. See <see cref="INetTransport"/>.
/// </summary>
public sealed unsafe class RakNetTransport : INetTransport
{
    private RakPeerInstance _peer;
    public bool IsServer { get; }
    public string Kind => "raknet";
    public RakNetGUID ServerGuid { get; private set; } = RakNetGUID.UNASSIGNED_RAKNET_GUID;

    private RakNetTransport(bool isServer)
    {
        IsServer = isServer;
        _peer = RakNetLibrary.CreateInstance();
        if (_peer.IsNull())
            throw new InvalidOperationException("RakNet: CreateInstance returned null.");
    }

    public static RakNetTransport Host(ushort port, ushort maxClients)
    {
        var net = new RakNetTransport(true);
        SocketDescriptor[] sockets = [new SocketDescriptor(port)];
        var result = net._peer.Startup(maxClients, sockets);
        if (result != StartupResult.RaknetStarted)
        {
            net._peer.Dispose();
            throw new InvalidOperationException($"RakNet: failed to start server on port {port}: {result}");
        }
        net._peer.SetMaximumIncomingConnections(maxClients);
        net._peer.SetOccasionalPing(true);
        return net;
    }

    public static RakNetTransport Join(string host, ushort port)
    {
        var net = new RakNetTransport(false);
        SocketDescriptor[] sockets = [new SocketDescriptor()];
        var result = net._peer.Startup(1, sockets);
        if (result != StartupResult.RaknetStarted)
        {
            net._peer.Dispose();
            throw new InvalidOperationException($"RakNet: failed to start client: {result}");
        }
        var hostBytes = Encoding.ASCII.GetBytes(host + "\0"); // native side expects a C string
        var attempt = net._peer.Connect(hostBytes, port, ReadOnlySpan<byte>.Empty, null);
        if (attempt != ConnectionAttemptResult.ConnectionAttemptStarted)
        {
            net._peer.Dispose();
            throw new InvalidOperationException($"RakNet: could not connect to {host}:{port}: {attempt}");
        }
        return net;
    }

    /// <summary>A socket-only peer for the server browser and the server lists: no connections, just pings and adverts.</summary>
    public static RakNetTransport Discovery()
    {
        var net = new RakNetTransport(false);
        SocketDescriptor[] sockets = [new SocketDescriptor()];
        var result = net._peer.Startup(4, sockets);
        if (result != StartupResult.RaknetStarted)
        {
            net._peer.Dispose();
            throw new InvalidOperationException($"RakNet: failed to start discovery socket: {result}");
        }
        return net;
    }

    public void Poll(NetPeer.PacketHandler handler)
    {
        for (var packet = _peer.Receive(); packet != null; packet = _peer.Receive())
        {
            try
            {
                if (packet->Length > 0)
                {
                    var data = new ReadOnlySpan<byte>(packet->Data, (int)packet->Length);
                    if (!IsServer && data[0] == (byte)DefaultMessageIDTypes.ConnectionRequestAccepted)
                        ServerGuid = packet->Guid;
                    handler(packet->Guid, data);
                }
            }
            finally
            {
                _peer.DeallocatePacket(packet);
            }
        }
    }

    /// <summary>Like <see cref="Poll"/>, but hands over the sender's address (for unconnected messages: pings, adverts).</summary>
    public void PollRaw(NetPeer.RawPacketHandler handler)
    {
        for (var packet = _peer.Receive(); packet != null; packet = _peer.Receive())
        {
            try
            {
                if (packet->Length > 0)
                    handler(packet->SystemAddress, new ReadOnlySpan<byte>(packet->Data, (int)packet->Length));
            }
            finally
            {
                _peer.DeallocatePacket(packet);
            }
        }
    }

    /// <summary>Unconnected ping; the reply (ID_UNCONNECTED_PONG) carries the target's offline ping response. Use 255.255.255.255 for the LAN.</summary>
    public void PingHost(string host, ushort port)
    {
        var hostBytes = Encoding.ASCII.GetBytes(host + "\0");
        _peer.Ping(hostBytes, port, false, 0);
    }

    /// <summary>Sends an unconnected ID_ADVERTISE_SYSTEM message carrying <paramref name="data"/>.</summary>
    public void Advertise(string host, ushort port, ReadOnlySpan<byte> data)
    {
        var hostBytes = Encoding.ASCII.GetBytes(host + "\0");
        _peer.AdvertiseSystem(hostBytes, port, data, 0);
    }

    /// <summary>What this peer answers to unconnected pings (server name, map, player count...).</summary>
    public void SetOfflineInfo(ReadOnlySpan<byte> data) => _peer.SetOfflinePingResponse(data);

    public void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability) =>
        _peer.Send(data, PacketPriority.HighPriority, reliability, 0, AddressOrGUID.From(target), false);

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability) =>
        _peer.Send(data, PacketPriority.HighPriority, reliability, 0, AddressOrGUID.UnassignedGuid, true);

    public void Kick(RakNetGUID target) => _peer.CloseConnection(AddressOrGUID.From(target), true);

    public int PingTo(RakNetGUID target) => _peer.GetAveragePing(AddressOrGUID.From(target));

    public int ConnectionCount => _peer.NumberOfConnections();

    public ulong SteamIdOf(RakNetGUID target) => 0;

    public bool StatsFor(RakNetGUID target, out NetConnectionStats stats)
    {
        var ping = PingTo(target);
        stats = new NetConnectionStats { PingMs = Math.Max(0, ping), QualityLocal = -1, QualityRemote = -1, Transport = Kind };
        return ping >= 0;
    }

    public bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent)
    {
        _peer.ApplyNetworkSimulator(lossPercent / 100f, (ushort)Math.Clamp(extraPingMs, 0, 2000), (ushort)Math.Clamp(jitterMs, 0, 2000));
        return _peer.IsNetworkSimulatorActive();
    }

    public void Dispose()
    {
        if (_peer.IsNull())
            return;
        _peer.Shutdown(300);
        _peer.Dispose();
        _peer = default;
    }
}
