using System.Runtime.InteropServices;

namespace CallOfSlop.Platform.Steam;

/// <summary>A Steam networking connection's state (ESteamNetworkingConnectionState, without the binding's names).</summary>
public enum SteamConnState
{
    None = 0,
    Connecting = 1,
    FindingRoute = 2,
    Connected = 3,
    ClosedByPeer = 4,
    ProblemDetectedLocally = 5,
}

/// <summary>A connection changed state (SteamNetConnectionStatusChangedCallback_t): the transport accepts, connects, or drops.</summary>
public readonly record struct SteamConnectionChange(uint Conn, ulong RemoteSteamId, SteamConnState State, SteamConnState OldState, int EndReason, string EndDebug, uint ListenSocket);

/// <summary>A received message: the connection, the peer's Steam ID and the bytes (valid during the call only).</summary>
public delegate void SteamMessageHandler(uint conn, ulong steamId, ReadOnlySpan<byte> data);

/// <summary>The lobby kinds the host can pick (ELobbyType): who finds and joins it.</summary>
public enum SteamLobbyKind : byte
{
    Private = 0, // invite only
    FriendsOnly = 1, // friends see it in the overlay and the browser
    Public = 2, // in everyone's server browser
}

/// <summary>
/// Phase 2 of the Steam migration: networking sockets (P2P by Steam ID through Valve's relays), lobbies, Rich Presence
/// and auth sessions. Handles are plain integers (0 is invalid) and results come back as events raised from
/// <see cref="ISteamBackend.RunCallbacks"/>, so the transport and the lobby layer never see the binding. Every member is
/// safe before Init: false / 0 / "" and no event.
/// </summary>
public partial interface ISteamBackend
{
    // ---- Networking sockets ----
    /// <summary>Starts the relay network (SDR) warming up: call once early so the first connection is quick.</summary>
    void InitRelayNetworkAccess();
    /// <summary>Listens for P2P connections on a virtual port; 0 when Steam cannot.</summary>
    uint CreateListenSocketP2P(int virtualPort);
    bool CloseListenSocket(uint socket);
    /// <summary>Begins connecting to a Steam user's listen socket; 0 when Steam cannot. Progress comes as <see cref="ConnectionChanged"/>.</summary>
    uint ConnectP2P(ulong steamId, int virtualPort);
    bool AcceptConnection(uint conn);
    bool CloseConnection(uint conn, int reason, string debug, bool linger);
    uint CreatePollGroup();
    bool DestroyPollGroup(uint pollGroup);
    bool SetConnectionPollGroup(uint conn, uint pollGroup);
    bool ConfigureConnectionLanes(uint conn, int[] priorities, ushort[] weights);
    /// <summary>Queues one message on a lane with Steam's send flags (reliable / unreliable, Nagle or not).</summary>
    bool SendMessage(uint conn, ReadOnlySpan<byte> data, int sendFlags, int lane);
    /// <summary>Delivers what arrived on a poll group (when not 0) or on one connection; returns how many.</summary>
    int ReceiveMessages(uint pollGroup, uint conn, SteamMessageHandler handler, int max);
    bool ConnectionStats(uint conn, out NetConnectionStats stats);
    /// <summary>Fake lag and loss on everything sent (testing); false when not supported.</summary>
    bool SetFakeNetwork(int lagMs, float lossPercent);
    event Action<SteamConnectionChange>? ConnectionChanged;

    // ---- Lobbies (ISteamMatchmaking) ----
    /// <summary>Asks for a lobby; <see cref="LobbyCreated"/> follows with its id (0 on failure).</summary>
    bool CreateLobby(SteamLobbyKind kind, int maxMembers);
    /// <summary>Joins a lobby; <see cref="LobbyEntered"/> follows.</summary>
    bool JoinLobby(ulong lobbyId);
    void LeaveLobby(ulong lobbyId);
    bool SetLobbyData(ulong lobbyId, string key, string value);
    string GetLobbyData(ulong lobbyId, string key);
    bool SetLobbyType(ulong lobbyId, SteamLobbyKind kind);
    bool SetLobbyJoinable(ulong lobbyId, bool joinable);
    bool SetLobbyMemberLimit(ulong lobbyId, int maxMembers);
    ulong LobbyOwner(ulong lobbyId);
    int LobbyMemberCount(ulong lobbyId);
    int LobbyMemberLimit(ulong lobbyId);
    /// <summary>Asks Steam for lobbies whose data matches every filter; <see cref="LobbyListReady"/> follows.</summary>
    bool RequestLobbyList(IReadOnlyList<(string Key, string Value)> equalFilters, int maxResults);
    /// <summary>Asks for a lobby's data without joining; <see cref="LobbyDataChanged"/> follows.</summary>
    bool RequestLobbyData(ulong lobbyId);
    /// <summary>The overlay's invite dialog for a lobby.</summary>
    void ActivateInviteDialog(ulong lobbyId);
    event Action<ulong, bool>? LobbyCreated;
    event Action<ulong, bool>? LobbyEntered;
    event Action<ulong[]>? LobbyListReady;
    event Action<ulong>? LobbyDataChanged;
    /// <summary>The user accepted an invite or clicked "Join game" on a friend (GameLobbyJoinRequested_t): the lobby, the friend.</summary>
    event Action<ulong, ulong>? LobbyJoinRequested;
    /// <summary>The user joined through Rich Presence (GameRichPresenceJoinRequested_t): the connect string.</summary>
    event Action<string>? RichPresenceJoinRequested;

    // ---- Rich Presence (ISteamFriends) ----
    bool SetRichPresence(string key, string? value);
    void ClearRichPresence();

    // ---- Auth sessions (ISteamUser) ----
    /// <summary>A ticket proving this user to <paramref name="serverSteamId"/>; the handle (0 on failure). Send it
    /// after <see cref="AuthTicketReady"/> says it is valid.</summary>
    uint GetAuthSessionTicket(ulong serverSteamId, out byte[] ticket);
    void CancelAuthTicket(uint handle);
    /// <summary>Host: checks a client's ticket; 0 is OK (EBeginAuthSessionResult), and <see cref="AuthSessionResponse"/> follows with Steam's verdict.</summary>
    int BeginAuthSession(byte[] ticket, ulong steamId);
    void EndAuthSession(ulong steamId);
    event Action<uint, bool>? AuthTicketReady;
    /// <summary>Steam's verdict on a session: the user, the response (0 OK, else EAuthSessionResponse), the owner of the licence.</summary>
    event Action<ulong, int, ulong>? AuthSessionResponse;
}

public sealed unsafe partial class SteamworksBackend
{
    private Steamworks.Callback<Steamworks.SteamNetConnectionStatusChangedCallback_t>? _connCb;
    private Steamworks.Callback<Steamworks.GameLobbyJoinRequested_t>? _lobbyJoinCb;
    private Steamworks.Callback<Steamworks.GameRichPresenceJoinRequested_t>? _rpJoinCb;
    private Steamworks.Callback<Steamworks.LobbyDataUpdate_t>? _lobbyDataCb;
    private Steamworks.Callback<Steamworks.GetAuthSessionTicketResponse_t>? _ticketCb;
    private Steamworks.Callback<Steamworks.ValidateAuthTicketResponse_t>? _validateCb;
    private Steamworks.CallResult<Steamworks.LobbyCreated_t>? _createLobby;
    private Steamworks.CallResult<Steamworks.LobbyEnter_t>? _joinLobby;
    private Steamworks.CallResult<Steamworks.LobbyMatchList_t>? _lobbyList;
    private readonly IntPtr[] _received = new IntPtr[64];
    private readonly IntPtr[] _sendOne = new IntPtr[1];
    private readonly long[] _sendResult = new long[1];

    public event Action<SteamConnectionChange>? ConnectionChanged;
    public event Action<ulong, bool>? LobbyCreated;
    public event Action<ulong, bool>? LobbyEntered;
    public event Action<ulong[]>? LobbyListReady;
    public event Action<ulong>? LobbyDataChanged;
    public event Action<ulong, ulong>? LobbyJoinRequested;
    public event Action<string>? RichPresenceJoinRequested;
    public event Action<uint, bool>? AuthTicketReady;
    public event Action<ulong, int, ulong>? AuthSessionResponse;

    /// <summary>Phase 2's callbacks, hooked after Init succeeded (from <see cref="Init"/>).</summary>
    private void HookPhase2()
    {
        _connCb = Steamworks.Callback<Steamworks.SteamNetConnectionStatusChangedCallback_t>.Create(cb =>
            ConnectionChanged?.Invoke(new SteamConnectionChange(cb.m_hConn.m_HSteamNetConnection, cb.m_info.m_identityRemote.GetSteamID64(),
                (SteamConnState)cb.m_info.m_eState, (SteamConnState)cb.m_eOldState, cb.m_info.m_eEndReason, cb.m_info.m_szEndDebug ?? "",
                cb.m_info.m_hListenSocket.m_HSteamListenSocket)));
        _lobbyJoinCb = Steamworks.Callback<Steamworks.GameLobbyJoinRequested_t>.Create(cb => LobbyJoinRequested?.Invoke(cb.m_steamIDLobby.m_SteamID, cb.m_steamIDFriend.m_SteamID));
        _rpJoinCb = Steamworks.Callback<Steamworks.GameRichPresenceJoinRequested_t>.Create(cb => RichPresenceJoinRequested?.Invoke(cb.m_rgchConnect ?? ""));
        _lobbyDataCb = Steamworks.Callback<Steamworks.LobbyDataUpdate_t>.Create(cb => LobbyDataChanged?.Invoke(cb.m_ulSteamIDLobby));
        _ticketCb = Steamworks.Callback<Steamworks.GetAuthSessionTicketResponse_t>.Create(cb => AuthTicketReady?.Invoke(cb.m_hAuthTicket.m_HAuthTicket, cb.m_eResult == Steamworks.EResult.k_EResultOK));
        _validateCb = Steamworks.Callback<Steamworks.ValidateAuthTicketResponse_t>.Create(cb => AuthSessionResponse?.Invoke(cb.m_SteamID.m_SteamID, (int)cb.m_eAuthSessionResponse, cb.m_OwnerSteamID.m_SteamID));
        _createLobby = Steamworks.CallResult<Steamworks.LobbyCreated_t>.Create((cb, failed) => LobbyCreated?.Invoke(failed ? 0 : cb.m_ulSteamIDLobby, !failed && cb.m_eResult == Steamworks.EResult.k_EResultOK));
        _joinLobby = Steamworks.CallResult<Steamworks.LobbyEnter_t>.Create((cb, failed) => LobbyEntered?.Invoke(cb.m_ulSteamIDLobby, !failed && cb.m_EChatRoomEnterResponse == 1)); // k_EChatRoomEnterResponseSuccess
        _lobbyList = Steamworks.CallResult<Steamworks.LobbyMatchList_t>.Create((cb, failed) =>
        {
            var n = failed ? 0 : (int)Math.Min(cb.m_nLobbiesMatching, 200);
            var ids = new ulong[n];
            for (var i = 0; i < n; i++)
                ids[i] = Steamworks.SteamMatchmaking.GetLobbyByIndex(i).m_SteamID;
            LobbyListReady?.Invoke(ids);
        });
    }

    private void UnhookPhase2()
    {
        _connCb?.Dispose();
        _lobbyJoinCb?.Dispose();
        _rpJoinCb?.Dispose();
        _lobbyDataCb?.Dispose();
        _ticketCb?.Dispose();
        _validateCb?.Dispose();
        _createLobby?.Dispose();
        _joinLobby?.Dispose();
        _lobbyList?.Dispose();
        _connCb = null;
        _lobbyJoinCb = null;
        _rpJoinCb = null;
        _lobbyDataCb = null;
        _ticketCb = null;
        _validateCb = null;
        _createLobby = null;
        _joinLobby = null;
        _lobbyList = null;
    }

    // ---- Networking sockets ----

    public void InitRelayNetworkAccess()
    {
        if (_running)
            Try(static () => { Steamworks.SteamNetworkingUtils.InitRelayNetworkAccess(); return true; }, false);
    }

    public uint CreateListenSocketP2P(int virtualPort) =>
        !_running ? 0 : Try(() => Steamworks.SteamNetworkingSockets.CreateListenSocketP2P(virtualPort, 0, null).m_HSteamListenSocket, 0u);

    public bool CloseListenSocket(uint socket) =>
        _running && Try(() => Steamworks.SteamNetworkingSockets.CloseListenSocket(new Steamworks.HSteamListenSocket(socket)), false);

    public uint ConnectP2P(ulong steamId, int virtualPort)
    {
        if (!_running)
            return 0;
        return Try(() =>
        {
            var identity = new Steamworks.SteamNetworkingIdentity();
            identity.SetSteamID64(steamId);
            return Steamworks.SteamNetworkingSockets.ConnectP2P(ref identity, virtualPort, 0, null).m_HSteamNetConnection;
        }, 0u);
    }

    public bool AcceptConnection(uint conn) =>
        _running && Try(() => Steamworks.SteamNetworkingSockets.AcceptConnection(new Steamworks.HSteamNetConnection(conn)) == Steamworks.EResult.k_EResultOK, false);

    public bool CloseConnection(uint conn, int reason, string debug, bool linger) =>
        _running && Try(() => Steamworks.SteamNetworkingSockets.CloseConnection(new Steamworks.HSteamNetConnection(conn), reason, debug, linger), false);

    public uint CreatePollGroup() => !_running ? 0 : Try(static () => Steamworks.SteamNetworkingSockets.CreatePollGroup().m_HSteamNetPollGroup, 0u);

    public bool DestroyPollGroup(uint pollGroup) =>
        _running && Try(() => Steamworks.SteamNetworkingSockets.DestroyPollGroup(new Steamworks.HSteamNetPollGroup(pollGroup)), false);

    public bool SetConnectionPollGroup(uint conn, uint pollGroup) =>
        _running && Try(() => Steamworks.SteamNetworkingSockets.SetConnectionPollGroup(new Steamworks.HSteamNetConnection(conn), new Steamworks.HSteamNetPollGroup(pollGroup)), false);

    public bool ConfigureConnectionLanes(uint conn, int[] priorities, ushort[] weights) =>
        _running && Try(() => Steamworks.SteamNetworkingSockets.ConfigureConnectionLanes(new Steamworks.HSteamNetConnection(conn), priorities.Length, priorities, weights) == Steamworks.EResult.k_EResultOK, false);

    public bool SendMessage(uint conn, ReadOnlySpan<byte> data, int sendFlags, int lane)
    {
        if (!_running || data.Length == 0)
            return false;
        try
        {
            if (lane == 0)
            {
                fixed (byte* p = data)
                    return Steamworks.SteamNetworkingSockets.SendMessageToConnection(new Steamworks.HSteamNetConnection(conn), (IntPtr)p, (uint)data.Length, sendFlags, out _) == Steamworks.EResult.k_EResultOK;
            }
            // A lane other than the first needs a message struct (SendMessages); Steam frees it after sending.
            var ptr = Steamworks.SteamNetworkingUtils.AllocateMessage(data.Length);
            if (ptr == IntPtr.Zero)
                return false;
            var msg = Marshal.PtrToStructure<Steamworks.SteamNetworkingMessage_t>(ptr);
            data.CopyTo(new Span<byte>((void*)msg.m_pData, data.Length));
            msg.m_conn = new Steamworks.HSteamNetConnection(conn);
            msg.m_nFlags = sendFlags;
            msg.m_idxLane = (ushort)lane;
            Marshal.StructureToPtr(msg, ptr, false);
            _sendOne[0] = ptr;
            _sendResult[0] = 0;
            Steamworks.SteamNetworkingSockets.SendMessages(1, _sendOne, _sendResult, true);
            return _sendResult[0] > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[steam] send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public int ReceiveMessages(uint pollGroup, uint conn, SteamMessageHandler handler, int max)
    {
        if (!_running)
            return 0;
        max = Math.Clamp(max, 1, _received.Length);
        int n;
        try
        {
            n = pollGroup != 0
                ? Steamworks.SteamNetworkingSockets.ReceiveMessagesOnPollGroup(new Steamworks.HSteamNetPollGroup(pollGroup), _received, max)
                : Steamworks.SteamNetworkingSockets.ReceiveMessagesOnConnection(new Steamworks.HSteamNetConnection(conn), _received, max);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[steam] receive failed: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
        if (n <= 0)
            return 0;
        for (var i = 0; i < n; i++)
        {
            var ptr = _received[i];
            try
            {
                var msg = Steamworks.SteamNetworkingMessage_t.FromIntPtr(ptr);
                if (msg.m_cbSize > 0)
                    handler(msg.m_conn.m_HSteamNetConnection, msg.m_identityPeer.GetSteamID64(), new ReadOnlySpan<byte>((void*)msg.m_pData, msg.m_cbSize));
            }
            finally
            {
                Steamworks.SteamNetworkingMessage_t.Release(ptr);
                _received[i] = IntPtr.Zero;
            }
        }
        return n;
    }

    public bool ConnectionStats(uint conn, out NetConnectionStats stats)
    {
        stats = default;
        if (!_running)
            return false;
        var status = new Steamworks.SteamNetConnectionRealTimeStatus_t();
        var lanes = new Steamworks.SteamNetConnectionRealTimeLaneStatus_t();
        var ok = Try(() => Steamworks.SteamNetworkingSockets.GetConnectionRealTimeStatus(new Steamworks.HSteamNetConnection(conn), ref status, 0, ref lanes) == Steamworks.EResult.k_EResultOK, false);
        if (!ok)
            return false;
        var relayed = Try(() => Steamworks.SteamNetworkingSockets.GetConnectionInfo(new Steamworks.HSteamNetConnection(conn), out var info) && (info.m_nFlags & 16) != 0, false); // k_nSteamNetworkConnectionInfoFlags_Relayed
        stats = new NetConnectionStats
        {
            PingMs = status.m_nPing,
            QualityLocal = status.m_flConnectionQualityLocal,
            QualityRemote = status.m_flConnectionQualityRemote,
            OutBytesPerSec = status.m_flOutBytesPerSec,
            InBytesPerSec = status.m_flInBytesPerSec,
            OutPacketsPerSec = status.m_flOutPacketsPerSec,
            InPacketsPerSec = status.m_flInPacketsPerSec,
            PendingUnreliable = status.m_cbPendingUnreliable,
            PendingReliable = status.m_cbPendingReliable,
            SentUnackedReliable = status.m_cbSentUnackedReliable,
            QueueTimeUs = status.m_usecQueueTime.m_SteamNetworkingMicroseconds,
            Relayed = relayed,
            Transport = "steam",
        };
        return true;
    }

    public bool SetFakeNetwork(int lagMs, float lossPercent)
    {
        if (!_running)
            return false;
        return Try(() =>
        {
            var lag = Math.Clamp(lagMs, 0, 2000);
            var loss = Math.Clamp(lossPercent, 0f, 100f);
            var ok = SetGlobalInt(Steamworks.ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_FakePacketLag_Send, lag);
            ok &= SetGlobalFloat(Steamworks.ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_FakePacketLoss_Send, loss);
            return ok;
        }, false);
    }

    private static bool SetGlobalInt(Steamworks.ESteamNetworkingConfigValue key, int value) =>
        Steamworks.SteamNetworkingUtils.SetConfigValue(key, Steamworks.ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero,
            Steamworks.ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, (IntPtr)(&value));

    private static bool SetGlobalFloat(Steamworks.ESteamNetworkingConfigValue key, float value) =>
        Steamworks.SteamNetworkingUtils.SetConfigValue(key, Steamworks.ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero,
            Steamworks.ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Float, (IntPtr)(&value));

    // ---- Lobbies ----

    public bool CreateLobby(SteamLobbyKind kind, int maxMembers)
    {
        if (!_running || _createLobby == null)
            return false;
        return Try(() =>
        {
            var call = Steamworks.SteamMatchmaking.CreateLobby((Steamworks.ELobbyType)(int)kind, Math.Clamp(maxMembers, 1, 250));
            if (call == Steamworks.SteamAPICall_t.Invalid)
                return false;
            _createLobby.Set(call);
            return true;
        }, false);
    }

    public bool JoinLobby(ulong lobbyId)
    {
        if (!_running || _joinLobby == null)
            return false;
        return Try(() =>
        {
            var call = Steamworks.SteamMatchmaking.JoinLobby(new Steamworks.CSteamID(lobbyId));
            if (call == Steamworks.SteamAPICall_t.Invalid)
                return false;
            _joinLobby.Set(call);
            return true;
        }, false);
    }

    public void LeaveLobby(ulong lobbyId)
    {
        if (_running)
            Try(() => { Steamworks.SteamMatchmaking.LeaveLobby(new Steamworks.CSteamID(lobbyId)); return true; }, false);
    }

    public bool SetLobbyData(ulong lobbyId, string key, string value) =>
        _running && Try(() => Steamworks.SteamMatchmaking.SetLobbyData(new Steamworks.CSteamID(lobbyId), key, value), false);

    public string GetLobbyData(ulong lobbyId, string key) =>
        !_running ? "" : Try(() => Steamworks.SteamMatchmaking.GetLobbyData(new Steamworks.CSteamID(lobbyId), key) ?? "", "");

    public bool SetLobbyType(ulong lobbyId, SteamLobbyKind kind) =>
        _running && Try(() => Steamworks.SteamMatchmaking.SetLobbyType(new Steamworks.CSteamID(lobbyId), (Steamworks.ELobbyType)(int)kind), false);

    public bool SetLobbyJoinable(ulong lobbyId, bool joinable) =>
        _running && Try(() => Steamworks.SteamMatchmaking.SetLobbyJoinable(new Steamworks.CSteamID(lobbyId), joinable), false);

    public bool SetLobbyMemberLimit(ulong lobbyId, int maxMembers) =>
        _running && Try(() => Steamworks.SteamMatchmaking.SetLobbyMemberLimit(new Steamworks.CSteamID(lobbyId), Math.Clamp(maxMembers, 1, 250)), false);

    public ulong LobbyOwner(ulong lobbyId) =>
        !_running ? 0 : Try(() => Steamworks.SteamMatchmaking.GetLobbyOwner(new Steamworks.CSteamID(lobbyId)).m_SteamID, 0ul);

    public int LobbyMemberCount(ulong lobbyId) =>
        !_running ? 0 : Try(() => Steamworks.SteamMatchmaking.GetNumLobbyMembers(new Steamworks.CSteamID(lobbyId)), 0);

    public int LobbyMemberLimit(ulong lobbyId) =>
        !_running ? 0 : Try(() => Steamworks.SteamMatchmaking.GetLobbyMemberLimit(new Steamworks.CSteamID(lobbyId)), 0);

    public bool RequestLobbyList(IReadOnlyList<(string Key, string Value)> equalFilters, int maxResults)
    {
        if (!_running || _lobbyList == null)
            return false;
        return Try(() =>
        {
            foreach (var (key, value) in equalFilters)
                Steamworks.SteamMatchmaking.AddRequestLobbyListStringFilter(key, value, Steamworks.ELobbyComparison.k_ELobbyComparisonEqual);
            Steamworks.SteamMatchmaking.AddRequestLobbyListDistanceFilter(Steamworks.ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            Steamworks.SteamMatchmaking.AddRequestLobbyListResultCountFilter(Math.Clamp(maxResults, 1, 200));
            var call = Steamworks.SteamMatchmaking.RequestLobbyList();
            if (call == Steamworks.SteamAPICall_t.Invalid)
                return false;
            _lobbyList.Set(call);
            return true;
        }, false);
    }

    public bool RequestLobbyData(ulong lobbyId) =>
        _running && Try(() => Steamworks.SteamMatchmaking.RequestLobbyData(new Steamworks.CSteamID(lobbyId)), false);

    public void ActivateInviteDialog(ulong lobbyId)
    {
        if (_running)
            Try(() => { Steamworks.SteamFriends.ActivateGameOverlayInviteDialog(new Steamworks.CSteamID(lobbyId)); return true; }, false);
    }

    // ---- Rich Presence ----

    public bool SetRichPresence(string key, string? value) =>
        _running && Try(() => Steamworks.SteamFriends.SetRichPresence(key, value), false);

    public void ClearRichPresence()
    {
        if (_running)
            Try(static () => { Steamworks.SteamFriends.ClearRichPresence(); return true; }, false);
    }

    // ---- Auth sessions ----

    public uint GetAuthSessionTicket(ulong serverSteamId, out byte[] ticket)
    {
        ticket = [];
        if (!_running)
            return 0;
        var buffer = new byte[1024];
        uint size = 0;
        var handle = Try(() =>
        {
            var identity = new Steamworks.SteamNetworkingIdentity();
            identity.SetSteamID64(serverSteamId);
            return Steamworks.SteamUser.GetAuthSessionTicket(buffer, buffer.Length, out size, ref identity).m_HAuthTicket;
        }, 0u);
        if (handle == 0 || size == 0 || size > buffer.Length)
            return 0;
        ticket = buffer[..(int)size];
        return handle;
    }

    public void CancelAuthTicket(uint handle)
    {
        if (_running && handle != 0)
            Try(() => { Steamworks.SteamUser.CancelAuthTicket(new Steamworks.HAuthTicket(handle)); return true; }, false);
    }

    public int BeginAuthSession(byte[] ticket, ulong steamId) =>
        !_running ? 1 : Try(() => (int)Steamworks.SteamUser.BeginAuthSession(ticket, ticket.Length, new Steamworks.CSteamID(steamId)), 1); // 1: InvalidTicket

    public void EndAuthSession(ulong steamId)
    {
        if (_running)
            Try(() => { Steamworks.SteamUser.EndAuthSession(new Steamworks.CSteamID(steamId)); return true; }, false);
    }
}
