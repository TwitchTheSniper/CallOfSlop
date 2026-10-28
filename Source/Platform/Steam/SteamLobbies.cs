using System.Globalization;

namespace CallOfSlop.Platform.Steam;

/// <summary>A Steam lobby as the server browser shows it: the lobby, its host, and the same facts a RakNet host answers pings with.</summary>
public sealed class SteamLobbyEntry
{
    public ulong LobbyId;
    public ulong HostSteamId;
    public ServerInfo Info;
    public int Members, MemberLimit;
    public SteamLobbyKind Kind;
    /// <summary>The browser's address for it ("steam:lobby:<id>"), what Settings.JoinAddress holds to join it.</summary>
    public string Address => SteamLobbies.AddressOf(LobbyId);
}

/// <summary>
/// Lobbies, invites and the lobby list (Phase 2, ISteamMatchmaking). A host on Steam makes a lobby (public, friends-only
/// or private, Settings "Steam lobby") and keeps its data as the game's <see cref="ServerInfo"/> (mode, map, players,
/// protocol, build): the server browser lists public lobbies by it, filtered to this game and protocol. Joining goes
/// through the lobby (join it, read the host's Steam ID, connect over <see cref="SteamTransport"/>), so an invite from
/// the overlay, "Join game" on a friend, "+connect_lobby ID" on the command line and a row in the browser all take the
/// same path (<see cref="JoinRequested"/> -> <see cref="Join"/> -> <see cref="Joined"/>). No host migration: when the
/// host leaves, the lobby goes.
/// </summary>
public sealed class SteamLobbies
{
    public const string GameKey = "game", GameValue = "callofslop";
    public const string ProtocolKey = "protocol", HostKey = "host", InfoKey = "info";
    public const string AddressPrefix = "steam:lobby:";

    private ISteamBackend? _steam;
    private ulong _localId;
    private readonly Dictionary<ulong, SteamLobbyEntry> _listed = new();
    private List<SteamLobbyEntry> _list = new();
    private bool _listDirty;
    private string _lastInfo = "";
    private bool _creating, _joining;
    private ulong _joiningLobby;

    /// <summary>The lobby this player is in (0: none). The host owns it; a joiner leaves it when the session ends.</summary>
    public ulong LobbyId { get; private set; }
    public bool IsOwner => LobbyId != 0 && _steam != null && _steam.LobbyOwner(LobbyId) == _localId;
    public SteamLobbyKind Kind { get; private set; }
    /// <summary>What is going on, for the lobby room and the log ("" when idle).</summary>
    public string Status { get; private set; } = "";
    /// <summary>Public lobbies of this game and protocol, as the last list said (refresh with <see cref="RequestList"/>).</summary>
    public IReadOnlyList<SteamLobbyEntry> Listed
    {
        get
        {
            if (_listDirty)
            {
                _list = _listed.Values.OrderByDescending(e => e.Info.Phase == MatchPhase.Lobby).ThenBy(e => e.Info.Name).ToList();
                _listDirty = false;
            }
            return _list;
        }
    }
    /// <summary>The list arrived or changed (the browser redraws).</summary>
    public event Action? ListChanged;
    /// <summary>Someone wants this player in a lobby: an accepted invite, "Join game" on a friend, +connect_lobby, Rich Presence.
    /// Program answers with <see cref="Join"/> (after leaving its current session).</summary>
    public event Action<ulong>? JoinRequested;
    /// <summary>A <see cref="Join"/> finished: the lobby, the host's Steam ID (0 when it failed) and a message.</summary>
    public event Action<ulong, ulong, string>? Joined;
    /// <summary>The host's lobby exists (its id) or could not be made (0).</summary>
    public event Action<ulong>? Created;

    internal void Attach(ISteamBackend steam, ulong localId)
    {
        _steam = steam;
        _localId = localId;
        steam.LobbyCreated += OnCreated;
        steam.LobbyEntered += OnEntered;
        steam.LobbyListReady += OnList;
        steam.LobbyDataChanged += OnData;
        steam.LobbyJoinRequested += OnJoinRequested;
        steam.RichPresenceJoinRequested += OnRichPresenceJoin;
    }

    internal void Detach(ISteamBackend steam)
    {
        Leave();
        steam.LobbyCreated -= OnCreated;
        steam.LobbyEntered -= OnEntered;
        steam.LobbyListReady -= OnList;
        steam.LobbyDataChanged -= OnData;
        steam.LobbyJoinRequested -= OnJoinRequested;
        steam.RichPresenceJoinRequested -= OnRichPresenceJoin;
        _steam = null;
        _listed.Clear();
        _listDirty = true;
        _creating = _joining = false;
        Status = "";
    }

    public static string AddressOf(ulong lobbyId) => AddressPrefix + lobbyId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The lobby id in a browser address ("steam:lobby:109775..."), or 0.</summary>
    public static ulong LobbyOfAddress(string? address) =>
        address != null && address.StartsWith(AddressPrefix, StringComparison.OrdinalIgnoreCase) && ulong.TryParse(address.AsSpan(AddressPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;

    /// <summary>The lobby id in Steam's launch argument ("+connect_lobby 109775..." as two arguments, or one with '='), or 0.</summary>
    public static ulong LobbyOfArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("+connect_lobby", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                return id;
            if (args[i].StartsWith("+connect_lobby=", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(args[i].AsSpan("+connect_lobby=".Length), NumberStyles.None, CultureInfo.InvariantCulture, out id))
                return id;
        }
        return 0;
    }

    /// <summary>The Rich Presence connect string for a lobby (what a friend's "Join game" hands the game).</summary>
    public static string ConnectStringOf(ulong lobbyId) => "+connect_lobby " + lobbyId.ToString(CultureInfo.InvariantCulture);

    // ---- Hosting ----

    /// <summary>The host asks for a lobby; <see cref="Created"/> says when it exists. Nothing without Steam.</summary>
    public bool Host(SteamLobbyKind kind, int maxMembers)
    {
        if (_steam == null || _creating)
            return false;
        Leave();
        Kind = kind;
        _creating = _steam.CreateLobby(kind, maxMembers);
        Status = _creating ? "Making the Steam lobby..." : "Steam could not make a lobby";
        return _creating;
    }

    /// <summary>The host keeps the lobby's data as the game's server info (once a second from Game's advert tick) and its type.</summary>
    public void UpdateHostData(ServerInfo info, SteamLobbyKind kind, bool joinable)
    {
        if (_steam == null || LobbyId == 0 || !IsOwner)
            return;
        if (kind != Kind)
        {
            Kind = kind;
            _steam.SetLobbyType(LobbyId, kind);
        }
        var encoded = Convert.ToBase64String(info.Encode());
        if (encoded == _lastInfo)
            return;
        _lastInfo = encoded;
        _steam.SetLobbyData(LobbyId, InfoKey, encoded);
        _steam.SetLobbyJoinable(LobbyId, joinable);
    }

    /// <summary>The overlay's invite dialog for the host's lobby.</summary>
    public void InviteFriends()
    {
        if (_steam != null && LobbyId != 0)
            _steam.ActivateInviteDialog(LobbyId);
    }

    /// <summary>Leaves (and, for the host, ends) the lobby. Safe when not in one.</summary>
    public void Leave()
    {
        if (_steam != null && LobbyId != 0)
            _steam.LeaveLobby(LobbyId);
        LobbyId = 0;
        _lastInfo = "";
        _creating = false;
        Status = "";
    }

    private void OnCreated(ulong lobbyId, bool ok)
    {
        _creating = false;
        if (!ok || lobbyId == 0 || _steam == null)
        {
            Status = "Steam could not make a lobby";
            Created?.Invoke(0);
            return;
        }
        LobbyId = lobbyId;
        _steam.SetLobbyData(lobbyId, GameKey, GameValue);
        _steam.SetLobbyData(lobbyId, ProtocolKey, ServerInfo.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        _steam.SetLobbyData(lobbyId, HostKey, _localId.ToString(CultureInfo.InvariantCulture));
        Status = Kind switch { SteamLobbyKind.Public => "Steam lobby: public", SteamLobbyKind.FriendsOnly => "Steam lobby: friends", _ => "Steam lobby: invite only" };
        Console.WriteLine($"[steam] lobby {lobbyId} made ({Kind})");
        Created?.Invoke(lobbyId);
    }

    // ---- Joining ----

    /// <summary>Joins a lobby to find its host; <see cref="Joined"/> follows with the host's Steam ID.</summary>
    public bool Join(ulong lobbyId)
    {
        if (_steam == null || lobbyId == 0)
            return false;
        Leave();
        _joining = _steam.JoinLobby(lobbyId);
        _joiningLobby = lobbyId;
        Status = _joining ? "Joining the Steam lobby..." : "Steam could not join the lobby";
        if (!_joining)
            Joined?.Invoke(lobbyId, 0, Status);
        return _joining;
    }

    private void OnEntered(ulong lobbyId, bool ok)
    {
        if (!_joining || lobbyId != _joiningLobby || _steam == null)
            return; // the host's own entry into its new lobby, or a stale answer
        _joining = false;
        if (!ok)
        {
            Status = "Could not join the Steam lobby (full, gone, or invite only)";
            Joined?.Invoke(lobbyId, 0, Status);
            return;
        }
        LobbyId = lobbyId;
        var host = HostOf(lobbyId);
        var protocol = _steam.GetLobbyData(lobbyId, ProtocolKey);
        if (host == 0 || _steam.GetLobbyData(lobbyId, GameKey) != GameValue)
        {
            Leave();
            Status = "That Steam lobby is not a Call of Slop game";
            Joined?.Invoke(lobbyId, 0, Status);
            return;
        }
        if (protocol != ServerInfo.ProtocolVersion.ToString(CultureInfo.InvariantCulture))
        {
            Leave();
            Status = $"The host is on another build (protocol {protocol}, yours {ServerInfo.ProtocolVersion})";
            Joined?.Invoke(lobbyId, 0, Status);
            return;
        }
        Status = "Connecting to the host over Steam...";
        Joined?.Invoke(lobbyId, host, Status);
    }

    /// <summary>The host's Steam ID a lobby names (its "host" data, else its owner), or 0.</summary>
    public ulong HostOf(ulong lobbyId)
    {
        if (_steam == null)
            return 0;
        return ulong.TryParse(_steam.GetLobbyData(lobbyId, HostKey), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id != 0 ? id : _steam.LobbyOwner(lobbyId);
    }

    private void OnJoinRequested(ulong lobbyId, ulong friend)
    {
        Console.WriteLine($"[steam] join requested: lobby {lobbyId} (from {friend})");
        JoinRequested?.Invoke(lobbyId);
    }

    private void OnRichPresenceJoin(string connect)
    {
        var id = LobbyOfArgs(connect.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (id != 0)
            OnJoinRequested(id, 0);
    }

    // ---- The list (server browser) ----

    /// <summary>Asks Steam for this game's public lobbies on this protocol; <see cref="ListChanged"/> when they arrive.</summary>
    public bool RequestList()
    {
        if (_steam == null)
            return false;
        return _steam.RequestLobbyList([(GameKey, GameValue), (ProtocolKey, ServerInfo.ProtocolVersion.ToString(CultureInfo.InvariantCulture))], 100);
    }

    private void OnList(ulong[] lobbies)
    {
        if (_steam == null)
            return;
        var keep = new HashSet<ulong>(lobbies);
        foreach (var gone in _listed.Keys.Where(id => !keep.Contains(id)).ToList())
            _listed.Remove(gone);
        foreach (var id in lobbies)
            Describe(id);
        _listDirty = true;
        ListChanged?.Invoke();
    }

    private void OnData(ulong lobbyId)
    {
        if (_listed.ContainsKey(lobbyId) && Describe(lobbyId) != null)
        {
            _listDirty = true;
            ListChanged?.Invoke();
        }
    }

    /// <summary>What a lobby's data says (null when it is not a lobby of this game with readable info).</summary>
    public SteamLobbyEntry? Describe(ulong lobbyId)
    {
        if (_steam == null || _steam.GetLobbyData(lobbyId, GameKey) != GameValue)
            return null;
        ServerInfo? info = null;
        try
        {
            var encoded = _steam.GetLobbyData(lobbyId, InfoKey);
            if (encoded.Length > 0)
                info = ServerInfo.Find(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            info = null;
        }
        if (info == null)
            return null;
        if (!_listed.TryGetValue(lobbyId, out var e))
            _listed[lobbyId] = e = new SteamLobbyEntry { LobbyId = lobbyId };
        e.HostSteamId = HostOf(lobbyId);
        e.Info = info.Value;
        e.Members = _steam.LobbyMemberCount(lobbyId);
        e.MemberLimit = _steam.LobbyMemberLimit(lobbyId);
        return e;
    }
}

public static partial class SteamPlatform
{
    /// <summary>Lobbies, invites and the lobby list (Phase 2). Everything is a no-op without Steam.</summary>
    public static SteamLobbies Lobby { get; } = new();
}
