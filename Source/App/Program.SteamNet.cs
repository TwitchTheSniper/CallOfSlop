using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// Steam networking in the app (Phase 2 of the Steam migration, docs/steam/STEAM.md "Transport"):
///   - Hosting with Steam running listens both ways (<see cref="DualTransport"/>: RakNet on the game port for LAN,
///     invite codes and server lists as before; Steam sockets for friends on Steam) and makes a Steam lobby (public,
///     friends-only or invite-only: Settings "Steam lobby") whose data is the game's server info, kept fresh every
///     second with the advert. Without Steam, hosting is exactly as before.
///   - Joining over Steam goes through the lobby: an accepted overlay invite, "Join game" on a friend, a browser row
///     ("steam:lobby:ID" as the join address) or "+connect_lobby ID" on the command line all ask the lobby layer to
///     join; once it names the host, the session starts with a <see cref="SteamTransport"/> to that Steam ID.
///   - Rich Presence follows the session (menu, single player, lobby room, playing), with the lobby as the connect key.
///   - The host's Steam bans come from Settings and go back there (<see cref="BanSteam"/>).
/// </summary>
public static unsafe partial class Program
{
    private static ulong _steamJoinHost, _steamJoinLobby; // a Steam join in progress: the host to connect to, through this lobby
    private static ulong _pendingLobby; // a lobby to join as soon as the main loop runs (+connect_lobby, an invite)
    private static string _steamJoinStatus = "";

    /// <summary>After Steam started: the lobby events, and a +connect_lobby from the command line (Steam passes it when
    /// an invite is accepted while the game is not running).</summary>
    private static void InitSteamNet(string[] args)
    {
        SteamPlatform.Lobby.JoinRequested += OnSteamJoinRequested;
        SteamPlatform.Lobby.Joined += OnSteamJoined;
        _pendingLobby = SteamLobbies.LobbyOfArgs(args);
        if (_pendingLobby != 0)
            Console.WriteLine($"[steam] +connect_lobby {_pendingLobby}");
        SteamPlatform.Presence.SetMenu();
    }

    private static void ShutdownSteamNet()
    {
        SteamPlatform.Lobby.JoinRequested -= OnSteamJoinRequested;
        SteamPlatform.Lobby.Joined -= OnSteamJoined;
    }

    /// <summary>The host's network: RakNet, plus Steam sockets and a lobby when Steam is running.</summary>
    private static NetPeer HostNet(ushort port, int maxClients)
    {
        var rakNet = RakNetTransport.Host(port, (ushort)maxClients);
        if (!SteamPlatform.Available)
            return new NetPeer(rakNet);
        SteamTransport steam;
        try
        {
            steam = SteamTransport.Host(SteamPlatform.Backend, maxClients);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[steam] hosting over RakNet only: {ex.Message}");
            return new NetPeer(rakNet);
        }
        SteamPlatform.Lobby.Host((SteamLobbyKind)Math.Min(_settings.SteamLobby, (byte)2), Game.MaxPlayers);
        Console.WriteLine($"[steam] hosting over RakNet and Steam sockets ({SteamPlatform.LocalSteamId})");
        return new NetPeer(new DualTransport(rakNet, steam));
    }

    /// <summary>A joiner's network: Steam sockets to the host a lobby named, else RakNet to the address.</summary>
    private static NetPeer JoinNet()
    {
        if (_steamJoinHost != 0 && SteamPlatform.Available)
        {
            var host = _steamJoinHost;
            _steamJoinHost = 0;
            Console.WriteLine($"[steam] connecting to {host} over Steam sockets");
            return new NetPeer(SteamTransport.Join(SteamPlatform.Backend, host));
        }
        _steamJoinHost = 0;
        var net = NetPeer.Join(_settings.JoinAddress, _settings.Port);
        LearnListFromHost(_settings.JoinAddress, _settings.Port, _joinInvite); // the host's server list, for next time (Program.Listing.cs)
        return net;
    }

    private static Invite? _joinInvite;

    /// <summary>A join whose address names a Steam lobby ("steam:lobby:ID", from the browser): the lobby layer finds the host first.</summary>
    private static bool StartSteamJoin(string address)
    {
        var lobby = SteamLobbies.LobbyOfAddress(address);
        if (lobby == 0)
            return false;
        RequestSteamJoin(lobby);
        return true;
    }

    private static void RequestSteamJoin(ulong lobby)
    {
        EndSession("");
        if (!SteamPlatform.Available)
        {
            _lobbyMessage = "Steam is not running: that game is only reachable over Steam.";
            return;
        }
        _steamJoinLobby = lobby;
        _steamJoinStatus = "Joining the Steam lobby...";
        _lobbyMessage = _steamJoinStatus;
        if (!SteamPlatform.Lobby.Join(lobby))
            _lobbyMessage = _steamJoinStatus = "Steam could not join that lobby.";
    }

    private static void OnSteamJoinRequested(ulong lobby) => _pendingLobby = lobby; // handled in the main loop (never mid-frame)

    private static void OnSteamJoined(ulong lobby, ulong host, string message)
    {
        if (lobby != _steamJoinLobby)
            return;
        _steamJoinLobby = 0;
        _steamJoinStatus = "";
        if (host == 0)
        {
            _lobbyMessage = message;
            Console.WriteLine($"[steam] join failed: {message}");
            return;
        }
        _steamJoinHost = host;
        _lobbyMessage = "";
        StartSession(LobbyAction.Join);
    }

    /// <summary>Every frame: a pending lobby join, the host's lobby data, Rich Presence.</summary>
    private static void UpdateSteamNet()
    {
        if (!SteamPlatform.Available)
            return;
        if (_pendingLobby != 0 && _window != null)
        {
            var lobby = _pendingLobby;
            _pendingLobby = 0;
            RequestSteamJoin(lobby);
        }
        var lobbyId = SteamPlatform.Lobby.LobbyId;
        if (_session is { IsServer: true, Net.IsOffline: false } host && lobbyId != 0 && host.LastInfo is { } info)
            SteamPlatform.Lobby.UpdateHostData(info, (SteamLobbyKind)Math.Min(_settings.SteamLobby, (byte)2), joinable: true);
        // Rich Presence
        if (_session == null)
            SteamPlatform.Presence.SetMenu();
        else if (_session.Net.IsOffline)
            SteamPlatform.Presence.SetSinglePlayer(GameModes.Name(_session.Mode), World.Map.Name);
        else if (_session.InLobby)
            SteamPlatform.Presence.SetLobby(GameModes.Name(_session.Mode), World.Map.Name, _session.HumanCount, _session.MapMaxPlayers, lobbyId);
        else
            SteamPlatform.Presence.SetPlaying(GameModes.Name(_session.Mode), World.Map.Name, _session.HumanCount, _session.MapMaxPlayers,
                _session.Mode == GameMode.Zombies ? _session.ZRound : 0, lobbyId);
    }

    /// <summary>The session ended: leave (or close) the lobby, back to the menu presence.</summary>
    private static void EndSteamNet()
    {
        SteamPlatform.Lobby.Leave();
        _steamJoinHost = 0;
        if (SteamPlatform.Available)
            SteamPlatform.Presence.SetMenu();
    }

    /// <summary>The host bans a Steam ID: kicked now, refused from now on (kept in Settings).</summary>
    public static bool BanSteam(ulong steamId)
    {
        if (_session is not { IsServer: true } host || !host.BanSteam(steamId))
            return false;
        if (!_settings.SteamBans.Contains(steamId))
        {
            _settings.SteamBans.Add(steamId);
            _settings.Save();
        }
        return true;
    }

    /// <summary>The host lifts a ban.</summary>
    public static void UnbanSteam(ulong steamId)
    {
        _settings.SteamBans.Remove(steamId);
        _session?.SteamBans.Remove(steamId);
        _settings.Save();
    }

    /// <summary>The Steam join / lobby status for the main menu ("" when nothing is going on).</summary>
    public static string SteamJoinStatus => _steamJoinStatus;
}
