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
    ZRun, // server -> one client: Zombies: your loose parts (reliable; no scrap since protocol 56)
    ZBuild, // server -> all: Zombies: a built weapon went into player X's slot (the build's text) (reliable)
    ZBench, // client -> server: Zombies: a workbench change (put a build in a slot, take a build apart, a slot machine bet) (reliable)
    ZPickup, // server -> all (and each joiner): Zombies: a dropped build appeared (where, seconds left, owner, build text) or went (picked up by X / despawned) (reliable)
    TeamPick, // client -> server: put me on Blue (0), Red (1) or Auto (255) (reliable; the server checks the rules, TeamPick.cs)
    TeamReply, // server -> one client: the answer to a team pick (accepted / moved flags, team, pick, text) (reliable)
    BuildingUse, // client -> server: the interact key at building door N (reliable; GameBuildings.cs)
    BuildingEvent, // server -> all: a building door opened / shut / took damage / broke, or a window broke (kind, index, health, from)
    BuildingState, // server -> joining client: every door's state and health and the broken windows (reliable)
    TacState, // server -> all: every tactical device (id, kind, owner, position, yaw, health, charges) with snapshots while any exist (unreliable; GameTactical.cs)
    TacEvent, // server -> all: a tactical moment: flashed / stunned, trophy intercept, device destroyed, decoy burst, motion ping, stim, placed, inserted (reliable)
    Knock, // server -> one client: a blast pushed you (the input sequence it went in after, the impulse) for the prediction replay (reliable; Knockback.cs)
    RocketSpawn, // server -> all: a launcher projectile was fired (id, owner, weapon + kit, lock target, seed, position, velocity, age) (reliable; GameLaunchersNet.cs)
    RocketSync, // server -> all: guided, decoyed and stuck projectiles' positions with snapshots while any exist (unreliable)
    RocketBoom, // server -> all: a launcher projectile went off / was a dud / left the map, or its target popped flares (reliable)
    RocketDetonate, // client -> server: set off my sticky charges (reliable)
    LobbyState, // server -> all: the pre-match lobby room: settings, map, countdown flags, every player's ping (on change reliable, ~1 Hz unreliable; PreMatchLobby.cs)
    ChatSay, // client -> server: a chat line (+ flags: team, protocol 36) (reliable; the server cleans and rate-limits it, LobbyChat.cs)
    ChatLine, // server -> each player it reaches: player X said this (+ flags: team / dead / system, and X's team; protocol 36) (reliable; LobbyChat.cs)
    StructBreak, // server -> all: destructible chunks a hit broke (structure, hit kind, from, chunk ids); every PC runs the same collapse (reliable; GameDestruction.cs)
    StructState, // server -> joining client: a structure's broken chunks, a bit each (reliable)
    VoiceUp, // client -> server: one 20 ms voice frame (sequence, flags: team radio, IMA-ADPCM frame) (unreliable; GameVoice.cs)
    VoiceDown, // server -> each listener it reaches: talker X's voice frame (talker, sequence, route, frame) (unreliable; never back to the talker)
    VoiceCtl, // client -> server: my voice preferences (bit 0: I listen to voice) (reliable)
    VoiceInfo, // server -> one client: the host's voice settings (bit 0: voice chat allowed) (reliable)
    PEnts, // server -> each client: Co-op: the enemies near that client (interest-managed, 13 bytes each, 20 Hz) (unreliable sequenced; GamePveNet.cs)
    PEvent, // server -> one client or all: Co-op: a banner, kill / objective / mission XP, enemies cleared (reliable)
    ZSlotWin, // server -> all: Zombies: a slot machine paid a part or a melee build (machine, player, result, index, tier, flags): shown over the machine and in the feed (reliable; ZombiesSlotWin.cs)
    WeaponTweaks, // server -> all (on a change) and each joining client: the host's weapon tweak window set: class multipliers and per-weapon stat overrides (reliable; GameWeaponTweaks.cs, protocol 60)
    Logic, // server <-> clients: map logic (a LogicMsgKind first): entity flags, a button or lever used, a mover's clock, a sound, a notice, the joiner's full state (GameLogic.cs)
    ZCasino, // server -> all (on a change) and each joining client: Zombies: one casino table's whole state (round, seats and hands, the dealer, roulette chips, dice, the last big win) (reliable; ZombiesCasinoNet.cs, protocol 66)
    ZCasinoAct, // client -> server: Zombies: a move at a casino table (table, act, arg, amount): bet, hit, stand, double, higher, lower, cash out, roll, clear, leave (reliable; protocol 66)
    ZGrenade, // server <-> clients: Zombies built grenades (a ZGrenadeMsg first): set off my remote grenades; a lure's closing bang (reliable; GameGrenades.cs, protocol 67)
    ZDropItem, // client -> server: Zombies: drop something on purpose (kind: 0 the gun in my hands, 1 loose parts; count; the part's name) (reliable; ZombiesDropItems.cs, protocol 70)
    ZPartDrop, // server -> all (and each joiner): Zombies: a loose-part pickup appeared (id, where, time left, last round, owner, count, part) or went (id, who took it) (reliable; protocol 70)
    ZCrowd, // server -> each client: Zombies mass horde: the crowd's per-cell counts and headings that changed, or a keyframe (reliable ordered; ZombiesCrowdNet.cs, protocol 71)
    ZTrader, // server -> all (he came or went) and each joiner: Zombies: the trader (here or gone, visit number, where he stands, the round he leaves, his shelf: kind, part, price; nothing sells out) (reliable; ZombiesTrader.cs, protocol 73)
    ZTraderBuy, // client -> server: Zombies: buy C of shelf item N of visit V from the trader (no limit; at most the rules' maxPerBuy at a time) (reliable; protocol 73)
    HostBye, // server -> one client or all: why the host is closing the connection (the host closed the server, a different build) (reliable; GameHostBye.cs, protocol 74)
    SteamIds, // client -> server: my Steam ID (count 1, id, u64) after the Hello; server -> all: every Steam player's (count, then player id + u64 each), with the roster, only when someone is on Steam (reliable; GameSteam.cs, protocol 76)
    SteamAuth, // client -> server: kind 0, my Steam auth session ticket for this host (u16 length, bytes); server -> one client: kind 1, you were refused or removed (the reason), then the connection closes (reliable; GameSteamAuth.cs, protocol 76)
    CBody, // server -> all (on a change) and each joiner: Sandbox: one construct's parts, links, joints and pose, a part's health, or a construct gone (a CBodyKind first) (reliable ordered; ConstructNet.cs, protocol 77; wires, logic settings and signals: 78)
    CPose, // server -> all: Sandbox: the moving constructs' poses (tick, then id, position, turn, velocity, steering, joint angles and speeds, signals each) (unreliable sequenced, 20 Hz; protocol 77, 78)
    CAct, // client -> server: Sandbox: an action (a CActKind first): hold a tool, attach or remove a part, cancel, use (sit, stand, a door, pick up), take from the rack (or a part by id from the debug parts menu), drop; wire, unwire, a setting, a seat's keys, a blueprint from the parts menu (reliable ordered; GameSandbox.cs, protocol 77, 78, 79)
    CFire, // server -> all: Sandbox: what the construct guns did this tick: shots (body, gun part, end, what it met), blasts and cook-offs (where, how big) (reliable ordered; ConstructNet.cs, protocol 81)
}

/// <summary>Tiny little-endian packet writer.</summary>
public sealed class PacketWriter
{
    /// <summary>
    /// The buffer is roomy so a big message (a 32-player roster, many kill-streak entities) can never overflow it;
    /// RakNet splits anything over the MTU. Messages sent many times a second keep under <see cref="Mtu"/> themselves.
    /// </summary>
    private readonly byte[] _buf;

    /// <summary>A writer with the usual 8 KB buffer, or a bigger one for a rare big message (Msg.ZRun: every loose part kind).</summary>
    public PacketWriter(int capacity = 8192) => _buf = new byte[Math.Max(64, capacity)];

    /// <summary>The buffer's size: a message never grows past it.</summary>
    public int Capacity => _buf.Length;

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

    /// <summary>Low 24 bits (positions; attachment kits were 24 bits until protocol 46).</summary>
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

    public PacketWriter U64(ulong v)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buf.AsSpan(Length), v);
        Length += 8;
        return this;
    }

    public PacketWriter F64(double v)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(_buf.AsSpan(Length), v);
        Length += 8;
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

    public ulong U64()
    {
        Need(8);
        var v = BinaryPrimitives.ReadUInt64LittleEndian(_data[_pos..]);
        _pos += 8;
        return v;
    }

    public double F64()
    {
        Need(8);
        var v = BinaryPrimitives.ReadDoubleLittleEndian(_data[_pos..]);
        _pos += 8;
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
/// The game's one door to the network. One instance acts either as the listen-server (host) or as a client connected to
/// it; underneath, an <see cref="INetTransport"/> moves the bytes: RakNet (UDP, as always), Steam networking sockets
/// (Phase 2 of the Steam migration), or both for a host on Steam. The game never sees which: connections are
/// <see cref="RakNetGUID"/>s, events are RakNet's system message ids, and <see cref="PacketReliability"/> says how a
/// message travels. <see cref="Discovery"/> is the RakNet-only socket for pings and adverts (the server browser and
/// server lists); Steam games are found through lobbies instead (SteamLobbies.cs).
/// </summary>
public sealed class NetPeer : IDisposable
{
    public delegate void PacketHandler(RakNetGUID sender, ReadOnlySpan<byte> data);

    private readonly INetTransport? _transport;
    private readonly RakNetTransport? _raw; // Discovery only
    public bool IsServer { get; }

    /// <summary>Single player: a server with no sockets. Every send is a no-op.</summary>
    public bool IsOffline => _transport == null && _raw == null;

    /// <summary>The transport underneath ("raknet", "steam", "raknet+steam"; "offline" with none).</summary>
    public string Kind => _transport?.Kind ?? (_raw != null ? "discovery" : "offline");

    /// <summary>The transport itself, for the few places that need more than the game API (the self-test, the Steam hooks).</summary>
    public INetTransport? Transport => _transport;

    public RakNetGUID ServerGuid => _transport?.ServerGuid ?? RakNetGUID.UNASSIGNED_RAKNET_GUID;

    private NetPeer(bool isServer, INetTransport? transport, RakNetTransport? raw = null)
    {
        IsServer = isServer;
        _transport = transport;
        _raw = raw;
    }

    /// <summary>A peer over any transport (the Steam transport, a test double).</summary>
    public NetPeer(INetTransport transport) : this(transport.IsServer, transport) { }

    public static NetPeer Offline() => new(true, null);

    /// <summary>Self-test: a client with no sockets (every send is a no-op), to feed packets to a client game.</summary>
    public static NetPeer OfflineClient() => new(false, null);

    public static NetPeer Host(ushort port, ushort maxClients) => new(true, RakNetTransport.Host(port, maxClients));

    public static NetPeer Join(string host, ushort port) => new(false, RakNetTransport.Join(host, port));

    /// <summary>Drains every packet the transport has queued and hands it to <paramref name="handler"/>.</summary>
    public void Poll(PacketHandler handler)
    {
        if (_transport == null)
            return;
        _handler = handler;
        try
        {
            _transport.Poll(_counted ??= OnCounted); // one delegate, allocated once: Poll runs every tick
        }
        finally
        {
            _handler = null;
        }
    }

    private PacketHandler? _counted, _handler;

    private void OnCounted(RakNetGUID sender, ReadOnlySpan<byte> data)
    {
        BytesReceived += data.Length;
        _handler!(sender, data);
    }

    // ---- Discovery (server browser, LAN broadcast, master server) ---------------------------

    public delegate void RawPacketHandler(SystemAddress from, ReadOnlySpan<byte> data);

    /// <summary>A socket-only peer for the server browser: no connections, just pings and adverts (RakNet only).</summary>
    public static NetPeer Discovery() => new(false, null, RakNetTransport.Discovery());

    /// <summary>The RakNet socket under this peer, when there is one: the discovery socket, a RakNet game, or the RakNet
    /// side of a host on Steam. Unconnected messages (pings, adverts, the offline info) only exist there.</summary>
    private RakNetTransport? RakNetSide => _raw ?? _transport as RakNetTransport ?? (_transport as DualTransport)?.RakNet;

    /// <summary>Like <see cref="Poll"/>, but hands over the sender's address (for unconnected messages).</summary>
    public void PollRaw(RawPacketHandler handler) => RakNetSide?.PollRaw(handler);

    /// <summary>Unconnected ping; the reply (ID_UNCONNECTED_PONG) carries the target's offline ping response. Use 255.255.255.255 for the LAN.</summary>
    public void PingHost(string host, ushort port) => RakNetSide?.PingHost(host, port);

    /// <summary>Sends an unconnected ID_ADVERTISE_SYSTEM message carrying <paramref name="data"/>.</summary>
    public void Advertise(string host, ushort port, ReadOnlySpan<byte> data) => RakNetSide?.Advertise(host, port, data);

    /// <summary>What this peer answers to unconnected pings (server name, map, player count...). RakNet hosts only;
    /// a Steam host's lobby data says the same (SteamLobbies.cs).</summary>
    public void SetOfflineInfo(ReadOnlySpan<byte> data) => RakNetSide?.SetOfflineInfo(data);

    /// <summary>Game payload bytes received / sent so far (no RakNet or UDP headers; a broadcast counts once per connection).</summary>
    public long BytesReceived, BytesSent;

    public void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (_transport == null)
            return;
        BytesSent += data.Length;
        _transport.SendTo(target, data, reliability);
    }

    public void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability)
    {
        if (_transport == null)
            return;
        BytesSent += (long)data.Length * ConnectionCount;
        _transport.Broadcast(data, reliability);
    }

    /// <summary>Server: drops a client (they get a disconnection notice once what was already sent to them arrives).</summary>
    public void Kick(RakNetGUID target) => _transport?.Kick(target);

    public void SendToServer(ReadOnlySpan<byte> data, PacketReliability reliability) =>
        SendTo(ServerGuid, data, reliability);

    public int PingTo(RakNetGUID target) => _transport?.PingTo(target) ?? 0;

    public int ConnectionCount => _transport?.ConnectionCount ?? 0;

    /// <summary>The peer's Steam ID when that connection runs over Steam, else 0.</summary>
    public ulong SteamIdOf(RakNetGUID target) => _transport?.SteamIdOf(target) ?? 0;

    /// <summary>Live connection quality for one peer (the network stats line, the perf menu). False when unknown.</summary>
    public bool StatsFor(RakNetGUID target, out NetConnectionStats stats)
    {
        if (_transport != null)
            return _transport.StatsFor(target, out stats);
        stats = default;
        return false;
    }

    /// <summary>Adds fake latency/loss to everything this peer sends (testing only). Returns false if the transport cannot.</summary>
    public bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent) =>
        _transport?.SimulateBadNetwork(extraPingMs, jitterMs, lossPercent) ?? false;

    public void Dispose()
    {
        _transport?.Dispose();
        _raw?.Dispose();
    }
}
