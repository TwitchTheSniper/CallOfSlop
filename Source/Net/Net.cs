using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>Game message ids. RakNet reserves everything below ID_USER_PACKET_ENUM.</summary>
public enum Msg : byte
{
    Welcome = (byte)DefaultMessageIDTypes.UserPacketEnum, // server -> client: your player id
    Input, // client -> server: the last few input commands, redundantly (unreliable sequenced)
    Fire, // client -> server: shot origin + direction + the tick the shooter was seeing (reliable)
    State, // server -> each client: snapshot of every player + input ack (unreliable sequenced)
    Shot, // server -> all: tracer + hit result (reliable)
    Kill, // server -> all: killer / victim / weapon (reliable)
    Loadout, // client -> server: chosen primary + secondary (reliable)
    Reload, // client -> server: started reloading (for others to hear)
    ReloadFx, // server -> all: player X is reloading weapon Y
    Throw, // client -> server: throw grenade/equipment (kind, origin, direction)
    ProjSpawn, // server -> all: a projectile was thrown (id, kind, owner, position, velocity)
    Explode, // server -> all: a projectile went off (id, kind, owner, position, hit flags for the owner)
    MatchEnd, // server -> all: winner id + result text (reliable)
    MatchStart, // server -> all: a fresh match began (reliable)
    Hello, // client -> server: my name (reliable)
    Roster, // server -> all: id / bot flag / name for every player (reliable)
    Respawn, // client -> server: I'm done picking my loadout, spawn me (reliable)
    MapVote, // client -> server: my vote for the next map (option index)
    VoteState, // server -> all: the ballot (map ids) and the current tally
    Melee, // client -> server: I swung (yaw, pitch, the tick I was seeing)
    MeleeFx, // server -> all: player X swung (hit / kill / backstab flags, victim)
    StreakUse, // client -> server: use an earned kill streak (id, target spot, facing)
    StreakEvent, // server -> all: a streak was called in, or a turret/drone was destroyed
    StreakState, // server -> one client: your streak count, earned streaks and carried streaks
    StreakEnts, // server -> all: turrets and drones (with each snapshot while any exist, unreliable)
    Ready, // client -> server: I'm ready / not ready (warmup)
    ModeState, // server -> all: game mode, teams, ready flags, gun levels, scores, hill and flags (unreliable, ~10 Hz)
    ModeEvent, // server -> all: a game-mode line for the kill feed + a sound (flag taken, hill moved...)
    Suicide, // client -> server: kill me (pause menu "Kill yourself"; rate-limited, server decides)
    Full, // server -> one client: the server (this map's player limit) is full; the connection is closed after it
    PropBreak, // server -> all: a breakable prop broke (index, damage kind, where the hit came from) (reliable)
    PropState, // server -> joining client: the props broken so far this match (reliable)
    ShieldHit, // server -> all: Last Stand's shield blocked a bullet hit on player X (charges left) (reliable)
    CartUse, // client -> server: the interact key + 1 byte (0 tap, 1 hold): park / man / push my push cart, buy the weapon-slot upgrade (reliable)
    CartState, // server -> all: every push cart (owner, pushed / idle / manned, position, yaw, gun aim) with snapshots while any exist (unreliable)
    RunState, // server -> one client: per-run flags (the weapon-slot upgrade) (reliable)
    ZUse, // client -> server: Zombies: the interact key went down (1) or up (0) (reliable)
    ZEnts, // server -> all: Zombies: every zombie (id, position, yaw, state, health) with snapshots while any exist (unreliable sequenced)
    ZWorld, // server -> all: Zombies: round, doors, boards, mystery box, every player's points / downs / perks, parts on the floor (~10 Hz, unreliable)
    ZEvent, // server -> one client or all: Zombies: a feed line, an ammo refill, a banner (reliable)
    ZRun, // server -> one client: Zombies: your loose parts and scrap (reliable)
    ZBuild, // server -> all: Zombies: a built weapon went into player X's slot (the build's text) (reliable)
    ZBench, // client -> server: Zombies: a workbench change (put a build in a slot, salvage a part, craft a part) (reliable)
    ZPickup, // server -> all (and each joiner): Zombies: a dropped build appeared (where, seconds left, owner, build text) or went (picked up by X / despawned) (reliable)
    TeamPick, // client -> server: put me on Blue (0), Red (1) or Auto (255) (reliable; the server checks the rules, TeamPick.cs)
    TeamReply, // server -> one client: the answer to a team pick (accepted / moved flags, team, pick, text) (reliable)
    BuildingUse, // client -> server: the interact key at building door N (reliable; GameBuildings.cs)
    BuildingEvent, // server -> all: a building door opened / shut / took damage / broke, or a window broke (kind, index, health, from)
    BuildingState, // server -> joining client: every door's state and health and the broken windows (reliable)
    TacState, // server -> all: every tactical device (id, kind, owner, position, yaw, health, charges) with snapshots while any exist (unreliable; GameTactical.cs)
    TacEvent, // server -> all: a tactical moment: flashed / stunned, trophy intercept, device destroyed, decoy burst, motion ping, stim, placed, inserted (reliable)
}

/// <summary>Tiny little-endian packet writer.</summary>
public sealed class PacketWriter
{
    /// <summary>
    /// The buffer is roomy so a big message (a 32-player roster, many kill-streak entities) can never overflow it;
    /// RakNet splits anything over the MTU. Messages sent many times a second keep under <see cref="Mtu"/> themselves.
    /// </summary>
    private readonly byte[] _buf = new byte[8192];

    /// <summary>The payload budget for one unsplit datagram (RakNet's MTU is ~1400 with headers; this leaves room).</summary>
    public const int Mtu = 1200;
    public int Length { get; private set; }

    public PacketWriter Begin(Msg id)
    {
        Length = 0;
        return U8((byte)id);
    }

    public PacketWriter U8(byte v)
    {
        _buf[Length++] = v;
        return this;
    }

    /// <summary>Short text: a length byte then up to 255 UTF-8 bytes.</summary>
    public PacketWriter Str(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var n = Math.Min(bytes.Length, 255);
        U8((byte)n);
        for (var i = 0; i < n; i++)
            U8(bytes[i]);
        return this;
    }

    /// <summary>Low 24 bits (attachment kits).</summary>
    public PacketWriter U24(uint v) => U8((byte)v).U8((byte)(v >> 8)).U8((byte)(v >> 16));

    public PacketWriter U16(ushort v)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(Length), v);
        Length += 2;
        return this;
    }

    public PacketWriter U32(uint v)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(Length), v);
        Length += 4;
        return this;
    }

    public PacketWriter F32(float v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_buf.AsSpan(Length), v);
        Length += 4;
        return this;
    }

    public PacketWriter Vec(Vector3 v) => F32(v.X).F32(v.Y).F32(v.Z);

    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, Length);

    /// <summary>Appends raw bytes.</summary>
    public PacketWriter Bytes(ReadOnlySpan<byte> data)
    {
        data.CopyTo(_buf.AsSpan(Length));
        Length += data.Length;
        return this;
    }

    /// <summary>Overwrites one byte already written (for counts filled in afterwards).</summary>
    public void PatchU8(int at, byte v) => _buf[at] = v;

    /// <summary>Overwrites two bytes already written (for lengths filled in afterwards).</summary>
    public void PatchU16(int at, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(at), v);
}

/// <summary>
/// Tiny little-endian packet reader. Reading past the end throws <see cref="MalformedPacketException"/> (a short or
/// garbage packet from another player), which <c>Game.OnPacketSafe</c> turns into a dropped packet and one log line.
/// </summary>
public ref struct PacketReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> _data = data;
    private int _pos = 0;

    public bool Has(int bytes) => _pos + bytes <= _data.Length;

    /// <summary>Bytes left to read.</summary>
    public int Remaining => _data.Length - _pos;

    /// <summary>The next <paramref name="bytes"/> bytes as a span (for a nested reader), skipping past them.</summary>
    public ReadOnlySpan<byte> Take(int bytes)
    {
        Need(bytes);
        var s = _data.Slice(_pos, bytes);
        _pos += bytes;
        return s;
    }

    private readonly void Need(int bytes)
    {
        if (_pos + bytes > _data.Length)
            Short(bytes, _pos, _data.Length);
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Short(int bytes, int at, int length) =>
        throw new MalformedPacketException($"the packet ended early (wanted {bytes} byte(s) at {at} of {length})");

    public byte U8()
    {
        Need(1);
        return _data[_pos++];
    }

    /// <summary>Short text (a length byte, then UTF-8). Control characters and NULs are removed; broken UTF-8 becomes U+FFFD.</summary>
    public string Str()
    {
        int n = U8();
        n = Math.Min(n, _data.Length - _pos);
        var s = Encoding.UTF8.GetString(_data.Slice(_pos, n));
        _pos += n;
        return NetSafety.StripControls(s);
    }

    /// <summary><see cref="Str"/> for showing on screen: printable ASCII, at most <paramref name="max"/> characters.</summary>
    public string Text(int max = 255) => NetSafety.CleanText(Str(), max);

    public uint U24()
    {
        uint a = U8(), b = U8(), c = U8();
        return a | (b << 8) | (c << 16);
    }

    public ushort U16()
    {
        Need(2);
        var v = BinaryPrimitives.ReadUInt16LittleEndian(_data[_pos..]);
        _pos += 2;
        return v;
    }

    public uint U32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadUInt32LittleEndian(_data[_pos..]);
        _pos += 4;
        return v;
    }

    public float F32()
    {
        Need(4);
        var v = BinaryPrimitives.ReadSingleLittleEndian(_data[_pos..]);
        _pos += 4;
        return v;
    }

    public Vector3 Vec() => new(F32(), F32(), F32());
}

/// <summary>
/// Thin wrapper around a BRUTAL RakNet peer. One instance acts either as the listen-server (host)
/// or as a client connected to it.
/// </summary>
public sealed unsafe class NetPeer : IDisposable
{
    public delegate void PacketHandler(RakNetGUID sender, ReadOnlySpan<byte> data);

    private RakPeerInstance _peer;
    public bool IsServer { get; }

    /// <summary>Single player: a server with no sockets. Every send is a no-op.</summary>
    public bool IsOffline { get; }

    public RakNetGUID ServerGuid { get; private set; } = RakNetGUID.UNASSIGNED_RAKNET_GUID;

    private NetPeer(bool isServer, bool offline = false)
    {
        IsServer = isServer;
        IsOffline = offline;
        if (offline)
            return;
        _peer = RakNetLibrary.CreateInstance();
        if (_peer.IsNull())
            throw new InvalidOperationException("RakNet: CreateInstance returned null.");
    }

    public static NetPeer Offline() => new(true, offline: true);

    /// <summary>Self-test: a client with no sockets (every send is a no-op), to feed packets to a client game.</summary>
    public static NetPeer OfflineClient() => new(false, offline: true);

    public static NetPeer Host(ushort port, ushort maxClients)
    {
        var net = new NetPeer(true);
        SocketDescriptor[] sockets = [new SocketDescriptor(port)];
        var result = net._peer.Startup(maxClients, sockets);
        if (result != StartupResult.RaknetStarted)
            throw new InvalidOperationException($"RakNet: failed to start server on port {port}: {result}");
        net._peer.SetMaximumIncomingConnections(maxClients);
        net._peer.SetOccasionalPing(true);
        return net;
    }

    public static NetPeer Join(string host, ushort port)
    {
        var net = new NetPeer(false);
        SocketDescriptor[] sockets = [new SocketDescriptor()];
        var result = net._peer.Startup(1, sockets);
        if (result != StartupResult.RaknetStarted)
            throw new InvalidOperationException($"RakNet: failed to start client: {result}");

        var hostBytes = Encoding.ASCII.GetBytes(host + "\0"); // native side expects a C string
        var attempt = net._peer.Connect(hostBytes, port, ReadOnlySpan<byte>.Empty, null);
        if (attempt != ConnectionAttemptResult.ConnectionAttemptStarted)
            throw new InvalidOperationException($"RakNet: could not connect to {host}:{port}: {attempt}");
        return net;
    }

    /// <summary>Drains every packet RakNet has queued and hands it to <paramref name="handler"/>.</summary>
    public void Poll(PacketHandler handler)
    {
        if (IsOffline)
            return;
        for (var packet = _peer.Receive(); packet != null; packet = _peer.Receive())
        {
            try
            {
                if (packet->Length > 0)
                {
                    BytesReceived += packet->Length;
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

    // ---- Discovery (server browser, LAN broadcast, master server) ---------------------------

    public delegate void RawPacketHandler(SystemAddress from, ReadOnlySpan<byte> data);

    /// <summary>A socket-only peer for the server browser: no connections, just pings and adverts.</summary>
    public static NetPeer Discovery()
    {
        var net = new NetPeer(false);
        SocketDescriptor[] sockets = [new SocketDescriptor()];
        var result = net._peer.Startup(4, sockets);
        if (result != StartupResult.RaknetStarted)
            throw new InvalidOperationException($"RakNet: failed to start discovery socket: {result}");
        return net;
    }

    /// <summary>Like <see cref="Poll"/>, but hands over the sender's address (for unconnected messages).</summary>
    public void PollRaw(RawPacketHandler handler)
    {
        if (IsOffline)
            return;
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
        if (IsOffline)
            return;
        var hostBytes = Encoding.ASCII.GetBytes(host + "\0");
        _peer.Ping(hostBytes, port, false, 0);
    }

    /// <summary>Sends an unconnected ID_ADVERTISE_SYSTEM message carrying <paramref name="data"/>.</summary>
    public void Advertise(string host, ushort port, ReadOnlySpan<byte> data)
    {
        if (IsOffline)
            return;
        var hostBytes = Encoding.ASCII.GetBytes(host + "\0");
        _peer.AdvertiseSystem(hostBytes, port, data, 0);
    }

    /// <summary>What this peer answers to unconnected pings (server name, map, player count...).</summary>
    public void SetOfflineInfo(ReadOnlySpan<byte> data)
    {
        if (!IsOffline)
            _peer.SetOfflinePingResponse(data);
    }

    /// <summary>Game payload bytes received / sent so far (no RakNet or UDP headers; a broadcast counts once per connection).</summary>
    public long BytesReceived, BytesSent;

    public void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (IsOffline)
            return;
        BytesSent += data.Length;
        _peer.Send(data, PacketPriority.HighPriority, reliability, 0, AddressOrGUID.From(target), false);
    }

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (IsOffline)
            return;
        BytesSent += (long)data.Length * ConnectionCount;
        _peer.Send(data, PacketPriority.HighPriority, reliability, 0, AddressOrGUID.UnassignedGuid, true);
    }

    /// <summary>Server: drops a client (they get a disconnection notice once what was already sent to them arrives).</summary>
    public void Kick(RakNetGUID target)
    {
        if (!IsOffline)
            _peer.CloseConnection(AddressOrGUID.From(target), true);
    }

    public void SendToServer(ReadOnlySpan<byte> data, PacketReliability reliability) =>
        SendTo(ServerGuid, data, reliability);

    public int PingTo(RakNetGUID target) => IsOffline ? 0 : _peer.GetAveragePing(AddressOrGUID.From(target));

    public int ConnectionCount => IsOffline ? 0 : _peer.NumberOfConnections();

    /// <summary>Adds fake latency/loss to everything this peer sends (testing only). Returns false if RakNet was built without it.</summary>
    public bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent)
    {
        if (IsOffline)
            return false;
        _peer.ApplyNetworkSimulator(lossPercent / 100f, (ushort)Math.Clamp(extraPingMs, 0, 2000), (ushort)Math.Clamp(jitterMs, 0, 2000));
        return _peer.IsNetworkSimulatorActive();
    }

    public void Dispose()
    {
        if (IsOffline || _peer.IsNull())
            return;
        _peer.Shutdown(300);
        _peer.Dispose();
    }
}
