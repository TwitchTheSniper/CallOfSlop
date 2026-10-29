using Brutal.RakNetApi;
using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// Steam identity checks and bans (Phase 2 of the Steam migration; protocol 76, <see cref="Msg.SteamAuth"/>).
///   - A connection over the Steam transport names its peer: the host fills <see cref="Player.SteamId"/> from it when
///     the player connects (no claim to trust), and a banned Steam ID is refused on the spot.
///   - A client on Steam, once it knows the host's Steam ID (the host is player 0 in the Steam id table, or the
///     connection itself over Steam), asks Steam for a session ticket for that host and sends it (kind 0). The host,
///     when on Steam, begins an auth session with it; Steam's verdict (owns the game, not banned, not elsewhere)
///     arrives a moment later, and a bad one kicks the player with the reason (kind 1, then the connection closes).
///     A ticket from a player whose claimed id is not the connection's is refused too. A host without Steam ignores
///     tickets, and a client without Steam sends none: a game without Steam sends nothing new.
///   - The host can kick or ban by Steam ID (<see cref="KickSteam"/>, <see cref="BanSteam"/>); bans persist in
///     Settings ("steam_bans") and apply to Steam connections and verified identities.
/// </summary>
public sealed partial class Game
{
    /// <summary>Host: the banned Steam IDs (Program keeps it in Settings). Shared with the lobby refusal.</summary>
    public HashSet<ulong> SteamBans = new();

    /// <summary>Client: the host's Steam ID once known (0 until then), for the auth ticket.</summary>
    public ulong HostSteamId { get; private set; }

    private bool _ticketAsked, _ticketSent;
    private bool _authHooked;

    private void HookSteamAuth()
    {
        if (_authHooked)
            return;
        _authHooked = true;
        SteamPlatform.Auth.TicketReady += OnSteamTicketReady;
        SteamPlatform.Auth.Verdict += OnSteamVerdict;
    }

    private void UnhookSteamAuth()
    {
        if (!_authHooked)
            return;
        _authHooked = false;
        SteamPlatform.Auth.TicketReady -= OnSteamTicketReady;
        SteamPlatform.Auth.Verdict -= OnSteamVerdict;
        if (!IsServer)
            SteamPlatform.Auth.CancelTicket();
        else
            foreach (var p in Players.Values)
                if (p.SteamId != 0)
                    SteamPlatform.Auth.End(p.SteamId);
    }

    /// <summary>Host: a player connected; their Steam ID when the connection runs over Steam. False when banned (refused).</summary>
    private bool AdmitSteamConnection(RakNetGUID sender, Player p)
    {
        var id = Net.SteamIdOf(sender);
        if (id == 0)
            return true;
        if (SteamBans.Contains(id))
        {
            Players.Remove(p.Id);
            _clients.Remove(sender.G);
            RefuseSteam(sender, "banned from this host");
            Log($"Refused {id}: banned from this host");
            return false;
        }
        p.SteamId = id;
        return true;
    }

    /// <summary>Host: tells the connection why and closes it (Msg.SteamAuth kind 1).</summary>
    private void RefuseSteam(RakNetGUID who, string reason)
    {
        _w.Begin(Msg.SteamAuth).U8(1).Str(reason);
        Net.SendTo(who, _w.Span, PacketReliability.ReliableOrdered);
        Net.Kick(who);
    }

    /// <summary>Client, each tick: once the host's Steam ID is known and I am on Steam, ask for a ticket (once).</summary>
    private void TickSteamAuthClient()
    {
        if (IsServer || _ticketAsked || !SteamPlatform.Available || Net.IsOffline)
            return;
        if (HostSteamId == 0)
        {
            var fromTransport = Net.SteamIdOf(Net.ServerGuid);
            HostSteamId = fromTransport != 0 ? fromTransport : SteamIdOf(0); // the host is player 0
            if (HostSteamId == 0)
                return;
        }
        HookSteamAuth();
        _ticketAsked = true;
        if (!SteamPlatform.Auth.RequestTicket(HostSteamId))
            Log("Steam: could not get an auth ticket for the host");
    }

    private void OnSteamTicketReady(ulong host, byte[] ticket)
    {
        if (IsServer || host != HostSteamId || _ticketSent || ticket.Length == 0 || ticket.Length > 2048)
            return;
        _ticketSent = true;
        _w.Begin(Msg.SteamAuth).U8(0).U16((ushort)ticket.Length).Bytes(ticket);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    private void OnSteamAuthPacket(ref PacketReader r, RakNetGUID sender)
    {
        if (!r.Has(1))
            return;
        var kind = r.U8();
        if (IsServer)
        {
            if (kind != 0 || !r.Has(2) || !_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                return;
            int len = r.U16();
            if (len == 0 || len > 2048 || !r.Has(len))
                return;
            var ticket = r.Take(len).ToArray();
            if (!SteamPlatform.Available)
                return; // a host without Steam cannot check it; the claim stays cosmetic
            var fromTransport = Net.SteamIdOf(sender);
            if (p.SteamId == 0)
                return; // no claim yet (the Steam id packet comes first on the same ordered channel)
            if (fromTransport != 0 && fromTransport != p.SteamId)
            {
                KickPlayer(p.Id, "Steam ID does not match the connection");
                return;
            }
            HookSteamAuth();
            if (!SteamPlatform.Auth.Begin(ticket, p.SteamId, out var error))
                KickPlayer(p.Id, $"Steam auth: {error}");
            return;
        }
        if (kind == 1)
        {
            var reason = r.Has(1) ? r.Text(80) : "refused by the host";
            Status = $"Removed by the host: {reason}";
            Disconnected = true;
            Log(Status, true);
        }
    }

    private void OnSteamVerdict(ulong steamId, bool ok, string why)
    {
        if (!IsServer)
            return;
        var p = Players.Values.FirstOrDefault(x => x.SteamId == steamId && x.Bot == null && x.Id != LocalId); // never the host itself
        if (p == null)
            return;
        if (ok)
        {
            Log($"Steam verified {p.Name}");
            return;
        }
        KickPlayer(p.Id, $"Steam auth: {why}");
    }

    /// <summary>Host: removes a connected player (not a bot, not the host) with a reason they see. False when there is no such player.</summary>
    public bool KickPlayer(byte id, string reason)
    {
        if (!IsServer || id == LocalId || !Players.TryGetValue(id, out var p) || p.Bot != null)
            return false;
        var guid = _clients.FirstOrDefault(kv => kv.Value.Id == id);
        if (guid.Value.Id != id)
            return false;
        _clients.Remove(guid.Key);
        if (p.SteamId != 0)
            SteamPlatform.Auth.End(p.SteamId);
        RefuseSteam(guid.Value.Guid, reason);
        var now = Brutal.GlfwApi.Glfw.Time;
        Log($"{p.Name} was removed: {reason}");
        ServerSystemChat($"{p.Name} was removed ({reason})", now);
        OnVoicePlayerLeft(p.Id);
        Players.Remove(p.Id);
        OnModePlayerLeave(p);
        BroadcastRoster();
        return true;
    }

    /// <summary>Host: kicks the player with this Steam ID. False when nobody has it.</summary>
    public bool KickSteam(ulong steamId)
    {
        var p = Players.Values.FirstOrDefault(x => x.SteamId == steamId && x.Bot == null);
        return p != null && KickPlayer(p.Id, "kicked by the host");
    }

    /// <summary>Host: bans this Steam ID (kicking them now when here). The ban list is the host's (Settings).</summary>
    public bool BanSteam(ulong steamId)
    {
        if (!IsServer || steamId == 0 || steamId == SteamPlatform.LocalSteamId)
            return false;
        SteamBans.Add(steamId);
        var p = Players.Values.FirstOrDefault(x => x.SteamId == steamId && x.Bot == null);
        if (p != null)
            KickPlayer(p.Id, "banned by the host");
        return true;
    }

    /// <summary>Host: a verified-or-claimed Steam id arrived for a player; banned ids go at once.</summary>
    private void OnSteamIdClaimed(Player p)
    {
        if (IsServer && p.SteamId != 0 && SteamBans.Contains(p.SteamId))
            KickPlayer(p.Id, "banned from this host");
    }
}
