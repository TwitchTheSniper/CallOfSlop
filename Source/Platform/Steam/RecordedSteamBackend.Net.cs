namespace CallOfSlop.Platform.Steam;

/// <summary>
/// A stand-in Steam network for the self-test: every <see cref="RecordedSteamBackend"/> made with the same world is a
/// Steam user on it. Listen sockets, P2P connections (both ends in this process), lobbies with data and members, auth
/// tickets and Rich Presence all live here. Messages are queued at once; callbacks (connection changes, lobby results,
/// ticket verdicts) wait for the receiving backend's next <see cref="RecordedSteamBackend.RunCallbacks"/>, as Steam's do.
/// </summary>
public sealed class RecordedSteamWorld
{
    internal readonly Dictionary<ulong, RecordedSteamBackend> Users = new();
    internal readonly Dictionary<(ulong SteamId, int Port), uint> Listening = new(); // listen socket handles per user
    internal readonly Dictionary<ulong, RecordedLobby> Lobbies = new();
    private ulong _nextLobby = 109775240000000001;
    private uint _nextHandle = 1;
    /// <summary>Drop every Nth unreliable message (0: none), to test that the game copes.</summary>
    public int DropEveryUnreliable;
    private int _unreliableCount;
    /// <summary>Tickets Steam rejects (the host gets a bad verdict for them), by the user's id.</summary>
    public readonly HashSet<ulong> BadTickets = new();

    internal uint NextHandle() => _nextHandle++;
    internal ulong NextLobbyId() => _nextLobby++;
    internal bool DropUnreliable() => DropEveryUnreliable > 0 && ++_unreliableCount % DropEveryUnreliable == 0;
}

public sealed class RecordedLobby
{
    public ulong Id;
    public ulong Owner;
    public SteamLobbyKind Kind;
    public int Limit;
    public bool Joinable = true;
    public readonly List<ulong> Members = new();
    public readonly Dictionary<string, string> Data = new();
}

public sealed partial class RecordedSteamBackend
{
    private sealed class RecConn
    {
        public uint Handle;
        public ulong Remote;
        public RecordedSteamBackend? Peer;
        public uint PeerHandle;
        public SteamConnState State;
        public uint PollGroup;
        public uint ListenSocket;
        public int Lanes = 1;
        public readonly Queue<byte[]> Inbox = new();
        public int Sent, Received;
    }

    /// <summary>The world this user lives on (its own when none was given: then it can talk to nobody).</summary>
    public RecordedSteamWorld World { get; }
    private readonly Dictionary<uint, RecConn> _recConns = new();
    private readonly HashSet<uint> _listenSockets = new();
    private readonly HashSet<uint> _pollGroups = new();
    private readonly Queue<Action> _pending = new(); // callbacks delivered on RunCallbacks
    private readonly Dictionary<uint, (ulong Server, ulong User)> _tickets = new();
    public readonly Dictionary<string, string> RichPresence = new();
    public readonly HashSet<ulong> AuthSessions = new();
    public bool RelayInitialised { get; private set; }
    public int FakeLagMs;
    public float FakeLossPercent;
    /// <summary>Connections ever accepted / made, with their lane counts (the test checks lanes were configured).</summary>
    public readonly List<(uint Handle, ulong Remote, int Lanes)> ConnectionLog = new();

    public RecordedSteamBackend() : this(new RecordedSteamWorld()) { }

    public RecordedSteamBackend(RecordedSteamWorld world)
    {
        World = world;
    }

    public event Action<SteamConnectionChange>? ConnectionChanged;
    public event Action<ulong, bool>? LobbyCreated;
    public event Action<ulong, bool>? LobbyEntered;
    public event Action<ulong[]>? LobbyListReady;
    public event Action<ulong>? LobbyDataChanged;
    public event Action<ulong, ulong>? LobbyJoinRequested;
    public event Action<string>? RichPresenceJoinRequested;
    public event Action<uint, bool>? AuthTicketReady;
    public event Action<ulong, int, ulong>? AuthSessionResponse;

    private void RegisterOnWorld() => World.Users[LocalSteamId] = this;

    /// <summary>Phase 2's part of <see cref="RunCallbacks"/>: deliver what is pending.</summary>
    private void RunPhase2Callbacks()
    {
        var n = _pending.Count; // only what was pending when the pump began; a callback may queue more for next time
        for (var i = 0; i < n && _pending.TryDequeue(out var a); i++)
            a();
    }

    private void ShutdownPhase2()
    {
        foreach (var c in _recConns.Values.ToArray())
            CloseConnection(c.Handle, 1003, "shut down", false);
        foreach (var l in World.Listening.Where(kv => kv.Key.SteamId == LocalSteamId).Select(kv => kv.Key).ToArray())
            World.Listening.Remove(l);
        foreach (var lobby in World.Lobbies.Values.ToArray())
            if (lobby.Members.Contains(LocalSteamId))
                LeaveLobby(lobby.Id);
        World.Users.Remove(LocalSteamId);
        _pending.Clear();
    }

    private void Later(Action a) => _pending.Enqueue(a);

    private SteamConnectionChange Change(RecConn c, SteamConnState old, int reason = 0, string debug = "") =>
        new(c.Handle, c.Remote, c.State, old, reason, debug, c.ListenSocket);

    // ---- Networking sockets ----

    public void InitRelayNetworkAccess()
    {
        Calls.Add("InitRelayNetworkAccess");
        RelayInitialised = Running;
    }

    public uint CreateListenSocketP2P(int virtualPort)
    {
        Calls.Add($"CreateListenSocketP2P({virtualPort})");
        if (!Running || World.Listening.ContainsKey((LocalSteamId, virtualPort)))
            return 0;
        var h = World.NextHandle();
        World.Listening[(LocalSteamId, virtualPort)] = h;
        _listenSockets.Add(h);
        return h;
    }

    public bool CloseListenSocket(uint socket)
    {
        Calls.Add($"CloseListenSocket({socket})");
        if (!_listenSockets.Remove(socket))
            return false;
        foreach (var key in World.Listening.Where(kv => kv.Value == socket).Select(kv => kv.Key).ToArray())
            World.Listening.Remove(key);
        return true;
    }

    public uint ConnectP2P(ulong steamId, int virtualPort)
    {
        Calls.Add($"ConnectP2P({steamId},{virtualPort})");
        if (!Running)
            return 0;
        var mine = new RecConn { Handle = World.NextHandle(), Remote = steamId, State = SteamConnState.Connecting };
        _recConns[mine.Handle] = mine;
        if (!World.Listening.TryGetValue((steamId, virtualPort), out var listen) || !World.Users.TryGetValue(steamId, out var host) || !host.Running)
        {
            // Nobody listening: Steam reports a local problem after its timeout; here on the next pump.
            Later(() =>
            {
                if (mine.State != SteamConnState.Connecting)
                    return;
                mine.State = SteamConnState.ProblemDetectedLocally;
                ConnectionChanged?.Invoke(Change(mine, SteamConnState.Connecting, 5003, "timed out: nobody listening")); // Misc_Timeout
            });
            return mine.Handle;
        }
        var theirs = new RecConn { Handle = World.NextHandle(), Remote = LocalSteamId, Peer = this, PeerHandle = mine.Handle, State = SteamConnState.Connecting, ListenSocket = listen };
        mine.Peer = host;
        mine.PeerHandle = theirs.Handle;
        host._recConns[theirs.Handle] = theirs;
        host.Later(() => host.ConnectionChanged?.Invoke(host.Change(theirs, SteamConnState.None)));
        return mine.Handle;
    }

    public bool AcceptConnection(uint conn)
    {
        Calls.Add($"AcceptConnection({conn})");
        if (!_recConns.TryGetValue(conn, out var c) || c.State != SteamConnState.Connecting || c.Peer == null)
            return false;
        var peer = c.Peer;
        var theirs = peer._recConns.GetValueOrDefault(c.PeerHandle);
        if (theirs == null)
            return false;
        c.State = SteamConnState.Connected;
        theirs.State = SteamConnState.Connected;
        ConnectionLog.Add((c.Handle, c.Remote, 0));
        Later(() => ConnectionChanged?.Invoke(Change(c, SteamConnState.Connecting)));
        peer.Later(() => peer.ConnectionChanged?.Invoke(peer.Change(theirs, SteamConnState.Connecting)));
        return true;
    }

    public bool CloseConnection(uint conn, int reason, string debug, bool linger)
    {
        Calls.Add($"CloseConnection({conn},{reason})");
        if (!_recConns.Remove(conn, out var c))
            return false;
        if (c.Peer is { } peer && peer._recConns.TryGetValue(c.PeerHandle, out var theirs) && theirs.State is SteamConnState.Connecting or SteamConnState.Connected)
        {
            var old = theirs.State;
            theirs.State = SteamConnState.ClosedByPeer;
            theirs.Peer = null;
            peer.Later(() => peer.ConnectionChanged?.Invoke(peer.Change(theirs, old, reason, debug)));
        }
        c.Peer = null;
        return true;
    }

    public uint CreatePollGroup()
    {
        Calls.Add("CreatePollGroup");
        if (!Running)
            return 0;
        var h = World.NextHandle();
        _pollGroups.Add(h);
        return h;
    }

    public bool DestroyPollGroup(uint pollGroup)
    {
        Calls.Add($"DestroyPollGroup({pollGroup})");
        return _pollGroups.Remove(pollGroup);
    }

    public bool SetConnectionPollGroup(uint conn, uint pollGroup)
    {
        Calls.Add($"SetConnectionPollGroup({conn},{pollGroup})");
        if (!_recConns.TryGetValue(conn, out var c) || !_pollGroups.Contains(pollGroup))
            return false;
        c.PollGroup = pollGroup;
        return true;
    }

    public bool ConfigureConnectionLanes(uint conn, int[] priorities, ushort[] weights)
    {
        Calls.Add($"ConfigureConnectionLanes({conn},{priorities.Length})");
        if (!_recConns.TryGetValue(conn, out var c) || priorities.Length != weights.Length || priorities.Length == 0)
            return false;
        c.Lanes = priorities.Length;
        for (var i = 0; i < ConnectionLog.Count; i++)
            if (ConnectionLog[i].Handle == conn)
                ConnectionLog[i] = (conn, c.Remote, c.Lanes);
        return true;
    }

    public bool SendMessage(uint conn, ReadOnlySpan<byte> data, int sendFlags, int lane)
    {
        if (!_recConns.TryGetValue(conn, out var c) || c.Peer == null || data.Length == 0)
            return false;
        if (lane >= c.Lanes)
            return false;
        c.Sent++;
        var reliable = (sendFlags & 8) != 0;
        if (!reliable && (World.DropUnreliable() || (FakeLossPercent > 0 && Random.Shared.NextSingle() * 100 < FakeLossPercent)))
            return true; // lost on the way, as far as the sender knows
        if (c.Peer._recConns.TryGetValue(c.PeerHandle, out var theirs) && theirs.State == SteamConnState.Connected)
            theirs.Inbox.Enqueue(data.ToArray());
        return true;
    }

    public int ReceiveMessages(uint pollGroup, uint conn, SteamMessageHandler handler, int max)
    {
        var n = 0;
        if (pollGroup != 0)
        {
            foreach (var c in _recConns.Values)
                if (c.PollGroup == pollGroup)
                    n += Drain(c, handler, max - n);
        }
        else if (_recConns.TryGetValue(conn, out var c))
            n = Drain(c, handler, max);
        return n;
    }

    private static int Drain(RecConn c, SteamMessageHandler handler, int max)
    {
        var n = 0;
        while (n < max && c.Inbox.TryDequeue(out var m))
        {
            c.Received++;
            n++;
            handler(c.Handle, c.Remote, m);
        }
        return n;
    }

    public bool ConnectionStats(uint conn, out NetConnectionStats stats)
    {
        stats = default;
        if (!_recConns.TryGetValue(conn, out var c))
            return false;
        stats = new NetConnectionStats { PingMs = 7 + FakeLagMs, QualityLocal = 1, QualityRemote = 1, OutPacketsPerSec = c.Sent, InPacketsPerSec = c.Received, Relayed = true, Transport = "steam" };
        return true;
    }

    public bool SetFakeNetwork(int lagMs, float lossPercent)
    {
        Calls.Add($"SetFakeNetwork({lagMs},{lossPercent})");
        FakeLagMs = lagMs;
        FakeLossPercent = lossPercent;
        return Running;
    }

    // ---- Lobbies ----

    public bool CreateLobby(SteamLobbyKind kind, int maxMembers)
    {
        Calls.Add($"CreateLobby({kind},{maxMembers})");
        if (!Running)
            return false;
        var lobby = new RecordedLobby { Id = World.NextLobbyId(), Owner = LocalSteamId, Kind = kind, Limit = maxMembers };
        lobby.Members.Add(LocalSteamId);
        World.Lobbies[lobby.Id] = lobby;
        Later(() =>
        {
            LobbyCreated?.Invoke(lobby.Id, true);
            LobbyEntered?.Invoke(lobby.Id, true);
        });
        return true;
    }

    public bool JoinLobby(ulong lobbyId)
    {
        Calls.Add($"JoinLobby({lobbyId})");
        if (!Running)
            return false;
        Later(() =>
        {
            if (!World.Lobbies.TryGetValue(lobbyId, out var lobby) || !lobby.Joinable || lobby.Members.Count >= lobby.Limit)
            {
                LobbyEntered?.Invoke(lobbyId, false);
                return;
            }
            if (!lobby.Members.Contains(LocalSteamId))
                lobby.Members.Add(LocalSteamId);
            LobbyEntered?.Invoke(lobbyId, true);
            foreach (var m in lobby.Members)
                if (World.Users.TryGetValue(m, out var u))
                    u.Later(() => u.LobbyDataChanged?.Invoke(lobbyId));
        });
        return true;
    }

    public void LeaveLobby(ulong lobbyId)
    {
        Calls.Add($"LeaveLobby({lobbyId})");
        if (!World.Lobbies.TryGetValue(lobbyId, out var lobby) || !lobby.Members.Remove(LocalSteamId))
            return;
        if (lobby.Members.Count == 0)
            World.Lobbies.Remove(lobbyId);
        else if (lobby.Owner == LocalSteamId)
            lobby.Owner = lobby.Members[0];
    }

    public bool SetLobbyData(ulong lobbyId, string key, string value)
    {
        if (!World.Lobbies.TryGetValue(lobbyId, out var lobby) || lobby.Owner != LocalSteamId)
            return false;
        if (lobby.Data.TryGetValue(key, out var old) && old == value)
            return true;
        lobby.Data[key] = value;
        foreach (var m in lobby.Members)
            if (World.Users.TryGetValue(m, out var u))
                u.Later(() => u.LobbyDataChanged?.Invoke(lobbyId));
        return true;
    }

    public string GetLobbyData(ulong lobbyId, string key) =>
        World.Lobbies.TryGetValue(lobbyId, out var lobby) ? lobby.Data.GetValueOrDefault(key, "") : "";

    public bool SetLobbyType(ulong lobbyId, SteamLobbyKind kind)
    {
        Calls.Add($"SetLobbyType({lobbyId},{kind})");
        if (!World.Lobbies.TryGetValue(lobbyId, out var lobby) || lobby.Owner != LocalSteamId)
            return false;
        lobby.Kind = kind;
        return true;
    }

    public bool SetLobbyJoinable(ulong lobbyId, bool joinable)
    {
        Calls.Add($"SetLobbyJoinable({lobbyId},{joinable})");
        if (!World.Lobbies.TryGetValue(lobbyId, out var lobby) || lobby.Owner != LocalSteamId)
            return false;
        lobby.Joinable = joinable;
        return true;
    }

    public bool SetLobbyMemberLimit(ulong lobbyId, int maxMembers)
    {
        if (!World.Lobbies.TryGetValue(lobbyId, out var lobby) || lobby.Owner != LocalSteamId)
            return false;
        lobby.Limit = maxMembers;
        return true;
    }

    public ulong LobbyOwner(ulong lobbyId) => World.Lobbies.TryGetValue(lobbyId, out var lobby) ? lobby.Owner : 0;
    public int LobbyMemberCount(ulong lobbyId) => World.Lobbies.TryGetValue(lobbyId, out var lobby) ? lobby.Members.Count : 0;
    public int LobbyMemberLimit(ulong lobbyId) => World.Lobbies.TryGetValue(lobbyId, out var lobby) ? lobby.Limit : 0;

    public bool RequestLobbyList(IReadOnlyList<(string Key, string Value)> equalFilters, int maxResults)
    {
        Calls.Add($"RequestLobbyList({equalFilters.Count})");
        if (!Running)
            return false;
        var filters = equalFilters.ToArray();
        Later(() =>
        {
            var ids = World.Lobbies.Values
                .Where(l => l.Kind == SteamLobbyKind.Public && filters.All(f => l.Data.GetValueOrDefault(f.Key, "") == f.Value))
                .Select(l => l.Id).Take(Math.Max(1, maxResults)).ToArray();
            LobbyListReady?.Invoke(ids);
        });
        return true;
    }

    public bool RequestLobbyData(ulong lobbyId)
    {
        Calls.Add($"RequestLobbyData({lobbyId})");
        if (!Running)
            return false;
        Later(() => LobbyDataChanged?.Invoke(lobbyId));
        return true;
    }

    public void ActivateInviteDialog(ulong lobbyId) => Calls.Add($"ActivateInviteDialog({lobbyId})");

    /// <summary>The test: the user accepted a friend's invite to a lobby (delivered on the next pump).</summary>
    public void RaiseLobbyJoinRequested(ulong lobbyId, ulong friend) => Later(() => LobbyJoinRequested?.Invoke(lobbyId, friend));

    /// <summary>The test: the user clicked "Join game" on a friend's Rich Presence.</summary>
    public void RaiseRichPresenceJoin(string connect) => Later(() => RichPresenceJoinRequested?.Invoke(connect));

    // ---- Rich Presence ----

    public bool SetRichPresence(string key, string? value)
    {
        if (!Running)
            return false;
        if (string.IsNullOrEmpty(value))
            RichPresence.Remove(key);
        else
            RichPresence[key] = value;
        return true;
    }

    public void ClearRichPresence()
    {
        Calls.Add("ClearRichPresence");
        RichPresence.Clear();
    }

    // ---- Auth sessions ----

    public uint GetAuthSessionTicket(ulong serverSteamId, out byte[] ticket)
    {
        Calls.Add($"GetAuthSessionTicket({serverSteamId})");
        ticket = [];
        if (!Running)
            return 0;
        var handle = World.NextHandle();
        _tickets[handle] = (serverSteamId, LocalSteamId);
        // A recorded ticket: a tag, the handle, the user and the server it is for (the host's BeginAuthSession checks them).
        ticket = new byte[24];
        BitConverter.TryWriteBytes(ticket.AsSpan(0, 4), 0x54434552u); // "RECT"
        BitConverter.TryWriteBytes(ticket.AsSpan(4, 4), handle);
        BitConverter.TryWriteBytes(ticket.AsSpan(8, 8), LocalSteamId);
        BitConverter.TryWriteBytes(ticket.AsSpan(16, 8), serverSteamId);
        Later(() => AuthTicketReady?.Invoke(handle, true));
        return handle;
    }

    public void CancelAuthTicket(uint handle)
    {
        Calls.Add($"CancelAuthTicket({handle})");
        _tickets.Remove(handle);
    }

    public int BeginAuthSession(byte[] ticket, ulong steamId)
    {
        Calls.Add($"BeginAuthSession({steamId})");
        if (!Running)
            return 1;
        if (ticket.Length != 24 || BitConverter.ToUInt32(ticket, 0) != 0x54434552u)
            return 1; // InvalidTicket
        var handle = BitConverter.ToUInt32(ticket, 4);
        var user = BitConverter.ToUInt64(ticket, 8);
        var server = BitConverter.ToUInt64(ticket, 16);
        if (user != steamId)
            return 1;
        if (server != LocalSteamId)
            return 4; // GameMismatch (it was made for someone else)
        if (AuthSessions.Contains(steamId))
            return 2; // DuplicateRequest
        if (!World.Users.TryGetValue(user, out var owner) || !owner._tickets.ContainsKey(handle))
            return 5; // ExpiredTicket (cancelled)
        AuthSessions.Add(steamId);
        var bad = World.BadTickets.Contains(user);
        Later(() => AuthSessionResponse?.Invoke(steamId, bad ? 8 : 0, steamId)); // 8: AuthTicketInvalid
        return 0;
    }

    public void EndAuthSession(ulong steamId)
    {
        Calls.Add($"EndAuthSession({steamId})");
        AuthSessions.Remove(steamId);
    }
}
