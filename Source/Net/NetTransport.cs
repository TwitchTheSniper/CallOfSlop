using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// What carries the game's packets (Phase 2 of the Steam migration, docs/steam/STEAM.md "Transport"). <see cref="NetPeer"/>
/// is the game's one door to the network; underneath it one of these moves the bytes: <see cref="RakNetTransport"/>
/// (UDP through BRUTAL's RakNet, as always), <see cref="SteamTransport"/> (Steam networking sockets, peer to peer by
/// Steam ID through Valve's relays) or <see cref="DualTransport"/> (a host on Steam listens both ways). Connections are
/// named by <see cref="RakNetGUID"/> everywhere in the game, whatever the transport: RakNet's own ids, or the peer's
/// Steam ID in <see cref="RakNetGUID.G"/>. Connection events arrive as one-byte packets with RakNet's system ids
/// (<see cref="DefaultMessageIDTypes.NewIncomingConnection"/> and the rest), so <c>Game.OnPacket</c> handles every
/// transport the same way. The protocol, the message ids and the packet layout never depend on the transport.
/// </summary>
public interface INetTransport : IDisposable
{
    bool IsServer { get; }
    /// <summary>A short name for the log and the network stats ("raknet", "steam", "raknet+steam").</summary>
    string Kind { get; }
    /// <summary>Client: the server's connection id once connected (RakNet: its GUID; Steam: the host's Steam ID in G).</summary>
    RakNetGUID ServerGuid { get; }
    int ConnectionCount { get; }
    /// <summary>Hands over every packet that arrived: system events first as one-byte packets, then game messages.</summary>
    void Poll(NetPeer.PacketHandler handler);
    void SendTo(RakNetGUID target, ReadOnlySpan<byte> data, PacketReliability reliability);
    void Broadcast(ReadOnlySpan<byte> data, PacketReliability reliability);
    /// <summary>Server: drops a client after what was already sent to it goes out.</summary>
    void Kick(RakNetGUID target);
    int PingTo(RakNetGUID target);
    /// <summary>The peer's Steam ID when this connection runs over Steam, else 0 (the host fills Player.SteamId from it).</summary>
    ulong SteamIdOf(RakNetGUID target);
    /// <summary>Live connection quality for the network stats and the perf menu; false when the transport has none for this peer.</summary>
    bool StatsFor(RakNetGUID target, out NetConnectionStats stats);
    /// <summary>Fake latency and loss on everything sent (testing). False when the transport cannot.</summary>
    bool SimulateBadNetwork(int extraPingMs, int jitterMs, float lossPercent);
}

/// <summary>
/// What a connection is doing right now, for the network stats line and the perf menu (feat/perf-menu's F9 window reads
/// <see cref="NetPeer.StatsFor"/>). RakNet fills the ping only; Steam fills everything
/// (<c>ISteamNetworkingSockets.GetConnectionRealTimeStatus</c>).
/// </summary>
public struct NetConnectionStats
{
    public int PingMs;
    /// <summary>0..1: the fraction of packets that got through recently, as seen locally and as the peer sees it (-1: unknown).</summary>
    public float QualityLocal, QualityRemote;
    public float OutBytesPerSec, InBytesPerSec, OutPacketsPerSec, InPacketsPerSec;
    /// <summary>Bytes queued and not yet sent, and reliable bytes sent and not yet acknowledged.</summary>
    public int PendingUnreliable, PendingReliable, SentUnackedReliable;
    /// <summary>Microseconds a packet waits in the queue before it goes out (bandwidth limited when large).</summary>
    public long QueueTimeUs;
    /// <summary>Steam: the connection goes through a Valve relay (SDR) rather than directly.</summary>
    public bool Relayed;
    public string Transport;
}

/// <summary>
/// The lanes a Steam connection carries (<c>ConfigureConnectionLanes</c>): a slow reliable message never holds up a
/// snapshot, and voice never holds up either. RakNet has no lanes; its ordering channel is 0 for everything, as before.
/// </summary>
public enum NetLane : byte
{
    /// <summary>Reliable gameplay: welcome, roster, shots, kills, mode and lobby state changes, map logic.</summary>
    Gameplay = 0,
    /// <summary>Unreliable, no Nagle: snapshots, inputs, entity syncs; anything sent many times a second.</summary>
    Snapshots = 1,
    /// <summary>Chat lines, voice frames and bulk one-offs (prop / structure states for a joiner): lower priority.</summary>
    Bulk = 2,
}

/// <summary>Per-lane priority and weight for <c>ConfigureConnectionLanes</c>, shared by the transport and the tests.</summary>
public static class NetLanes
{
    public const int Count = 3;
    /// <summary>Lower is more urgent: snapshots first, then gameplay, then bulk.</summary>
    public static readonly int[] Priorities = [1, 0, 2];
    public static readonly ushort[] Weights = [1, 1, 1];

    /// <summary>Which lane a message rides in, from its reliability and id. Unreliable traffic is the snapshot lane;
    /// voice and chat and the big join-time states are bulk; the rest is gameplay.</summary>
    public static NetLane For(PacketReliability reliability, byte msgId)
    {
        switch ((Msg)msgId)
        {
            case Msg.VoiceUp:
            case Msg.VoiceDown:
            case Msg.ChatSay:
            case Msg.ChatLine:
            case Msg.PropState:
            case Msg.BuildingState:
            case Msg.StructState:
            case Msg.WeaponTweaks:
            case Msg.ZRun:
                return NetLane.Bulk;
        }
        return reliability is PacketReliability.Unreliable or PacketReliability.UnreliableSequenced or PacketReliability.UnreliableWithAckReceipt
            ? NetLane.Snapshots
            : NetLane.Gameplay;
    }

    /// <summary>Steam send flags for a RakNet reliability: reliable kinds are reliable (Steam keeps order within a lane;
    /// Steam's 5 ms Nagle may pack a few together), unreliable kinds go out at once (no Nagle), so a snapshot never
    /// waits for a fuller packet.</summary>
    public static int SteamSendFlags(PacketReliability reliability) => reliability switch
    {
        PacketReliability.Unreliable or PacketReliability.UnreliableSequenced or PacketReliability.UnreliableWithAckReceipt => SteamSendUnreliableNoNagle,
        _ => SteamSendReliable,
    };

    // k_nSteamNetworkingSend_* (steamnetworkingtypes.h); kept here so the game never names the binding's constants.
    public const int SteamSendUnreliableNoNagle = 1;
    public const int SteamSendReliable = 8;
}
