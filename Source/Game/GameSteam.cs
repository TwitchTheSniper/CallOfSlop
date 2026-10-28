using Brutal.RakNetApi;
using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// Steam identities on the network (Phase 1 of the Steam migration; protocol 74). A client on Steam tells the server its
/// Steam ID right after its Hello (<see cref="Msg.SteamIds"/>, one entry); the server keeps every Steam player's id and
/// sends the whole table to everyone with each roster (only when at least one player is on Steam, so a game without Steam
/// sends nothing new). Clients on Steam then show those players' Steam names and avatars (SteamPlatform.Friends,
/// SteamAvatars.cs). Cosmetic only: nothing here is trusted for gameplay, and ids are never checked (auth tickets are
/// Phase 3). Phase 2's Steam transport knows the id from the connection itself and fills the same field.
/// </summary>
public sealed partial class Game
{
    /// <summary>Steam ids the roster told us, kept for players that are not in the snapshot yet (like _names).</summary>
    private readonly Dictionary<byte, ulong> _steamIds = new();

    /// <summary>Client: right after the Hello, my Steam ID when I am on Steam (nothing otherwise).</summary>
    private void SendLocalSteamId()
    {
        var id = SteamPlatform.LocalSteamId;
        if (IsServer || id == 0)
            return;
        _w.Begin(Msg.SteamIds).U8(1).U8(LocalId).U64(id);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Server: the host's own id, then every Steam player's, to everyone. Nothing when nobody is on Steam.</summary>
    private void BroadcastSteamIds()
    {
        if (!IsServer)
            return;
        if (Players.TryGetValue(LocalId, out var me) && me.Bot == null)
            me.SteamId = SteamPlatform.LocalSteamId;
        var count = 0;
        foreach (var p in Players.Values)
            if (p.SteamId != 0)
                count++;
        if (count == 0)
            return;
        _w.Begin(Msg.SteamIds).U8((byte)count);
        foreach (var p in Players.Values)
            if (p.SteamId != 0)
                _w.U8(p.Id).U64(p.SteamId);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void OnSteamIdsPacket(ref PacketReader r, RakNetGUID sender)
    {
        if (!r.Has(1))
            return;
        int count = r.U8();
        if (IsServer)
        {
            // A client's own id: one entry, and it is the sender's whatever id byte it wrote.
            if (count != 1 || !r.Has(9) || !_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                return;
            r.U8();
            var id = r.U64();
            if (id == 0 || p.SteamId == id)
                return;
            var fromTransport = Net.SteamIdOf(sender);
            if (fromTransport != 0 && fromTransport != id)
            {
                KickPlayer(p.Id, "Steam ID does not match the connection"); // GameSteamAuth.cs
                return;
            }
            p.SteamId = id;
            OnSteamIdClaimed(p); // banned ids go at once (GameSteamAuth.cs)
            BroadcastSteamIds();
            return;
        }
        for (var i = 0; i < count && r.Has(9); i++)
        {
            var who = r.U8();
            var id = r.U64();
            _steamIds[who] = id;
            if (Players.TryGetValue(who, out var p))
                p.SteamId = id;
        }
    }

    /// <summary>A player's Steam ID as the roster told it (0: not on Steam, or unknown).</summary>
    public ulong SteamIdOf(byte id) =>
        Players.TryGetValue(id, out var p) && p.SteamId != 0 ? p.SteamId : _steamIds.GetValueOrDefault(id);
}
