using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The pre-match lobby room (server-authoritative; protocol 32). When hosting, everyone lands in a menu-style lobby
/// before the map is played: the player list (name, rank and hat, team, ping, ready, host), the teams, the map, mode and
/// pack with the host's settings, the loadout screen and a chat. The map named in the lobby is already loaded (the
/// lobby draws it behind the menu), so the start is quick.
///   - Phase <see cref="MatchPhase.Lobby"/>: nobody is in the map (everyone waits to deploy), bots don't think, nobody
///     can be hurt. People join straight into the lobby; people who join during a match skip it (as before).
///   - Everyone readies up. The host's START (when every other person is ready) or FORCE START (any time) begins a
///     <see cref="LobbyCountdownSeconds"/> countdown; with the host's auto-start on it also begins by itself once
///     everyone is ready, or that many seconds after the first person readied. Someone un-readying cancels it (not a
///     forced one; the host can cancel any). Disconnects never stall it: only the people still here count.
///   - At zero the match starts: everyone spawns and the normal 5 s countdown runs (the lobby replaces the first
///     warmup: everyone already readied up). Without the lobby (single player, or "Return to lobby between matches"
///     off) matches keep the in-map warmup as before.
///   - The host changes the map, mode, pack, score limit, time limit, team rules, bots and auto-start from the lobby;
///     the lobby state (<see cref="Msg.LobbyState"/>) tells everyone at once. Maps too small for the people here
///     can't be picked (the same rule as the map vote, <see cref="LobbyMapAllowed"/>).
///   - "Return to lobby between matches" (host setting, default off): after the results screen and the map vote,
///     everyone goes back to the lobby instead of straight into the next match's warmup.
/// Single player never shows the lobby: its menu page already has every setting, and the in-map warmup starts it.
/// </summary>
public sealed partial class Game
{
    /// <summary>The lobby's countdown once the host starts (or auto-start kicks in).</summary>
    public const double LobbyCountdownSeconds = 5;

    /// <summary>Time limits the host can pick (minutes).</summary>
    public static readonly int[] MatchMinuteChoices = [5, 10, 15, 20, 30];

    /// <summary>Program turns this on when hosting: the session opens in the lobby room (single player never does).</summary>
    public bool LobbyEnabled;

    /// <summary>Host setting: after the results screen and the map vote, go back to the lobby room (off: straight to the next warmup).</summary>
    public bool ReturnToLobby;

    private int _matchMinutes = (int)(MatchSeconds / 60);

    /// <summary>Host setting: the time limit in minutes (1-60; 10 by default). Replicated in the lobby state.</summary>
    public int MatchMinutes
    {
        get => _matchMinutes;
        set => _matchMinutes = Math.Clamp(value, 1, 60);
    }

    /// <summary>The time limit of a match in seconds (<see cref="MatchMinutes"/>).</summary>
    public double MatchLength => _matchMinutes * 60.0;

    /// <summary>In the lobby room (before the map is played).</summary>
    public bool InLobby => Phase == MatchPhase.Lobby;

    /// <summary>Seconds left on the lobby's countdown (0: not counting). Clients get it from the snapshots' time left.</summary>
    public double LobbyCountdown => InLobby ? TimeLeft : 0;

    /// <summary>The lobby's countdown was forced by the host (un-readying doesn't cancel it).</summary>
    public bool LobbyForced { get; private set; }

    /// <summary>Bumped on every lobby change (settings, map); the lobby screen rebuilds its text when it moves.</summary>
    public ushort LobbyVersion { get; private set; }

    /// <summary>The bots' difficulty (what new bots get; the lobby shows and changes it).</summary>
    public BotDifficulty BotSkill => _botSkill;

    /// <summary>The host's player id (the host is always player 0 on its own server).</summary>
    public const byte HostId = 0;

    /// <summary>Round-trip time to each player in ms, by player id (the host's own is 0; bots 0). From the lobby state on clients.</summary>
    public readonly ushort[] PingMs = new ushort[MaxPlayers];

    private double _lobbyCountdownUntil;
    private double _lobbyAutoAt;
    private double _nextLobbyState;
    private bool _lobbyDirty;

    // ---- Rules (shared with the screens and the self-test) ----------------------------------------------------

    /// <summary>
    /// The lobby's map-fit rule (the same as the vote's, <see cref="FitForPeople"/>): a map can be picked when the
    /// current mode plays on it and its player limit in that mode takes every person here (bots don't count; they
    /// leave or come back to fit, as on any map change).
    /// </summary>
    public static bool LobbyMapAllowed(Map map, int humans, Func<Map, int> limit, Func<Map, bool> modeFits) =>
        modeFits(map) && limit(map) >= humans;

    /// <summary><see cref="LobbyMapAllowed(Map, int, Func{Map, int}, Func{Map, bool})"/> for the people here and the current mode.</summary>
    public bool LobbyMapAllowed(Map map) => LobbyMapAllowed(map, HumanCount, PlayerLimit, m => Module.Fits(m));

    /// <summary>Can the host pick <paramref name="mode"/> now (it plays on this map and its limit takes every person here)?</summary>
    public bool LobbyModeAllowed(GameMode mode)
    {
        var m = GameModeRegistry.Get(mode);
        return m.Fits(World.Map) && Math.Min(m.MaxPlayers(World.Map), MaxPlayers) >= HumanCount;
    }

    /// <summary>Every person here (bots are always ready) is ready, apart from <paramref name="except"/> (the host pressing START).</summary>
    public bool AllReadyExcept(byte except)
    {
        foreach (var p in Players.Values)
            if (p.Bot == null && p.Id != except && !p.Ready)
                return false;
        return true;
    }

    /// <summary>The host's START button is enabled: in the lobby, not counting yet, every other person ready.</summary>
    public bool CanLobbyStart => IsServer && InLobby && _lobbyCountdownUntil <= 0 && AllReadyExcept(LocalId);

    // ---- Server ---------------------------------------------------------------------------------------------

    /// <summary>Opens the lobby room: nobody in the map (everyone waits to deploy), ready flags cleared.</summary>
    private void BeginLobby(double now)
    {
        Phase = MatchPhase.Lobby;
        TimeLeft = 0;
        _lobbyCountdownUntil = 0;
        _lobbyAutoAt = 0;
        LobbyForced = false;
        _forceStart = false;
        foreach (var p in Players.Values)
        {
            p.Ready = false;
            PlaceWaiting(p, now); // out of the map; the match spawns everyone
        }
        MarkLobbyDirty();
    }

    /// <summary>Server, every lobby tick: auto-start, cancel on un-ready, the countdown, and the lobby state for everyone.</summary>
    private void ServerTickLobby(double now)
    {
        var (ready, humans) = ReadyCount;
        if (_lobbyCountdownUntil > 0)
        {
            if (!LobbyForced && ready < humans)
            {
                CancelLobbyCountdown("Someone isn't ready: countdown cancelled");
            }
            else if (now >= _lobbyCountdownUntil)
            {
                StartMatchFromLobby(now);
                return;
            }
        }
        else if (AutoStartSeconds > 0 && humans > 0)
        {
            if (ready == humans)
                BeginLobbyCountdown(now, false);
            else if (ready > 0)
            {
                if (_lobbyAutoAt <= 0)
                    _lobbyAutoAt = now + AutoStartSeconds;
                else if (now >= _lobbyAutoAt)
                    BeginLobbyCountdown(now, true); // the auto-start ran out: like a force start
            }
            else
                _lobbyAutoAt = 0;
        }
        TimeLeft = _lobbyCountdownUntil > 0 ? Math.Max(0, _lobbyCountdownUntil - now) : 0;
    }

    /// <summary>Server, every tick in every phase: a changed lobby state goes out at once (reliable); in the lobby, the pings once a second.</summary>
    private void ServerTickLobbyState(double now)
    {
        if (Net.IsOffline || !(_lobbyDirty || (InLobby && now >= _nextLobbyState)))
            return;
        var reliable = _lobbyDirty;
        _lobbyDirty = false;
        _nextLobbyState = now + 1;
        BroadcastLobbyState(reliable);
    }

    /// <summary>Seconds until the host's auto-start begins the countdown (0: not waiting on it).</summary>
    public double LobbyAutoStartIn => IsServer && InLobby && _lobbyAutoAt > 0 && _lobbyCountdownUntil <= 0
        ? Math.Max(0, _lobbyAutoAt - Brutal.GlfwApi.Glfw.Time) : 0;

    private void BeginLobbyCountdown(double now, bool forced)
    {
        _lobbyCountdownUntil = now + LobbyCountdownSeconds;
        _lobbyAutoAt = 0;
        LobbyForced = forced;
        TimeLeft = LobbyCountdownSeconds;
        ModeEvent(forced ? "The host started the match" : "Everyone is ready: starting", Sfx.HitTick);
        MarkLobbyDirty();
    }

    private void CancelLobbyCountdown(string why)
    {
        _lobbyCountdownUntil = 0;
        _lobbyAutoAt = 0;
        LobbyForced = false;
        TimeLeft = 0;
        ModeEvent(why, Sfx.HitTick);
        MarkLobbyDirty();
    }

    /// <summary>Host: START (every other person ready; the host counts as ready) or FORCE START. Returns false when refused.</summary>
    public bool LobbyStart(bool force) => LobbyStart(force, Brutal.GlfwApi.Glfw.Time);

    /// <summary><see cref="LobbyStart(bool)"/> with an explicit clock (self-test).</summary>
    public bool LobbyStart(bool force, double now)
    {
        if (!IsServer || !InLobby || _lobbyCountdownUntil > 0)
            return false;
        if (!force && !AllReadyExcept(LocalId))
            return false;
        if (Local is { } me && !me.Ready)
            ServerSetReady(me, true); // pressing START means you're ready
        BeginLobbyCountdown(now, force || !AllReadyExcept(byte.MaxValue));
        return true;
    }

    /// <summary>Host: stop the lobby's countdown.</summary>
    public void LobbyCancel()
    {
        if (IsServer && InLobby && _lobbyCountdownUntil > 0)
            CancelLobbyCountdown("The host cancelled the countdown");
    }

    /// <summary>The countdown ran out: everyone spawns and the match's own countdown begins (the lobby was the warmup).</summary>
    private void StartMatchFromLobby(double now)
    {
        _lobbyCountdownUntil = 0;
        LobbyForced = false;
        Winner = NoWinner;
        MatchResult = "";
        ResetModeForMatch(respawn: false); // teams (picks kept), scores and objectives for this map and mode
        Projectiles.Clear();
        Smokes.Clear();
        Sensors.Clear();
        BeginCountdown(now);
        foreach (var p in Players.Values)
        {
            p.Kills = 0;
            p.Deaths = 0;
            Spawn(p, ServerTick, now);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
            if (p.Bot != null)
                p.Bot.Waypoint = System.Numerics.Vector3.Zero;
        }
        _w.Begin(Msg.MatchStart).U16((ushort)MatchNumber).Str(World.Map.Name).U32(World.Map.ContentHash).U8((byte)Phase);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnMatchStarted();
        MarkLobbyDirty();
    }

    private void MarkLobbyDirty()
    {
        LobbyVersion++;
        _lobbyDirty = true;
    }

    // ---- Host settings ----------------------------------------------------------------------------------------

    /// <summary>Host, in the lobby: play <paramref name="map"/> (it must fit everyone here). Bots leave or come back to fit.</summary>
    public bool LobbySetMap(Map map, bool trueRandom)
    {
        if (!IsServer || !InLobby || !LobbyMapAllowed(map))
            return false;
        TrueRandom = trueRandom;
        if (map != World.Map)
        {
            ApplyMapChange(map);
            World.Map.OpenAllDoors();
            _ = World.Map.Objectives; // a menu moment: work these out now, not in the match's first tick
            _ = World.Physics;
            ResetModeForMatch(respawn: false);
            var now = Brutal.GlfwApi.Glfw.Time;
            foreach (var p in Players.Values)
                if (p.Alive)
                    PlaceWaiting(p, now); // bots that came back for a bigger map wait too
            Log($"Map: {map.Name}", true);
        }
        MarkLobbyDirty();
        return true;
    }

    /// <summary>
    /// Host, in the lobby: a random map that fits everyone from <paramref name="pack"/> (null: True Random's pool),
    /// not the current one when there's a choice. Returns the map, or null when none fits.
    /// </summary>
    public Map? LobbyRandomMap(MapPack? pack)
    {
        var pool = pack != null ? pack.Maps : MapPack.TrueRandomPool(Maps.Packs, Maps.All);
        var fit = pool.Where(m => LobbyMapAllowed(m)).ToList();
        if (fit.Count > 1)
            fit.RemoveAll(m => m.Name == World.Map.Name);
        if (fit.Count == 0)
            return null;
        var pick = fit[_rng.Next(fit.Count)];
        return LobbySetMap(pick, pack == null) ? pick : null;
    }

    /// <summary>Host, in the lobby: change the game mode (it must play on this map and take everyone here).</summary>
    public bool LobbySetMode(GameMode mode)
    {
        if (!IsServer || !InLobby || !LobbyModeAllowed(mode))
            return false;
        Mode = mode; // resets teams and objectives
        FitPlayersToMap(); // a co-op mode takes fewer (Zombies: 4)
        var now = Brutal.GlfwApi.Glfw.Time;
        foreach (var p in Players.Values)
            if (p.Alive)
                PlaceWaiting(p, now);
        MarkLobbyDirty();
        return true;
    }

    /// <summary>Host, in the lobby: how many bots (capped by the map) and how good. A new difficulty replaces the bots (HostControls.cs).</summary>
    public void LobbySetBots(int count, BotDifficulty skill)
    {
        if (IsServer && InLobby)
            SetBots(count, skill);
    }

    /// <summary>Host, in the lobby: the other settings (score limit, time limit, auto-start, return to lobby, frenzy buffs).</summary>
    public void LobbySetOptions(ScoreLimitMode scoreLimit, int matchMinutes, int autoStartSeconds, bool returnToLobby, bool frenzyBuffs)
    {
        if (!IsServer)
            return;
        var changed = scoreLimit != ScoreLimitMode || Math.Clamp(matchMinutes, 1, 60) != MatchMinutes || autoStartSeconds != AutoStartSeconds
                      || returnToLobby != ReturnToLobby || frenzyBuffs != FrenzyBuffsEnabled;
        if (!changed)
            return;
        ScoreLimitMode = scoreLimit;
        MatchMinutes = matchMinutes;
        AutoStartSeconds = Math.Clamp(autoStartSeconds, 0, 300);
        ReturnToLobby = returnToLobby;
        FrenzyBuffsEnabled = frenzyBuffs;
        UpdateScoreLimit();
        MarkLobbyDirty();
    }

    /// <summary>Host, in the lobby: team rules (<see cref="SetTeamRules"/>), told to everyone at once.</summary>
    public void LobbySetTeamRules(TeamSelection selection, int maxDiff)
    {
        if (!IsServer)
            return;
        SetTeamRules(selection, maxDiff);
        MarkLobbyDirty();
    }

    /// <summary>
    /// Host, in the lobby: SHUFFLE (everyone back on Auto, then a random split; bots fill the smaller side) or BALANCE
    /// (picks kept, the rest split evenly, as a new match does).
    /// </summary>
    public void LobbyShuffleTeams(bool keepPicks)
    {
        if (!IsServer || !IsTeamMode || HumansVsBots)
            return; // (Humans vs Bots: the sides are fixed)
        if (!keepPicks)
        {
            var people = Players.Values.Where(p => p.Bot == null).OrderBy(_ => _rng.Next()).ToList();
            foreach (var p in Players.Values)
                p.TeamChoice = NoTeam;
            var n = new int[2];
            foreach (var p in people)
            {
                var t = n[0] <= n[1] ? 0 : 1;
                p.Team = (byte)t;
                n[t]++;
            }
            foreach (var p in Players.Values.Where(p => p.Bot != null).OrderBy(p => p.Id))
            {
                var t = n[0] <= n[1] ? 0 : 1;
                p.Team = (byte)t;
                n[t]++;
            }
            ModeEvent("The host shuffled the teams", Sfx.HitTick);
        }
        else
        {
            AssignTeams();
            ModeEvent("The host balanced the teams", Sfx.HitTick);
        }
        MarkLobbyDirty();
    }

    // ---- Replication ------------------------------------------------------------------------------------------

    private const byte LobbyFlagOpen = 1, LobbyFlagForced = 2, LobbyFlagReturn = 4, LobbyFlagTrueRandom = 8, LobbyFlagFrenzy = 16;

    /// <summary>
    /// The lobby state (protocol 32): version, flags, the map (name + fingerprint), mode, score limit choice, time
    /// limit, bots wanted and their difficulty, auto-start, then every player's ping. About 30 bytes + 3 per player.
    /// </summary>
    public void WriteLobbyState(PacketWriter w)
    {
        var flags = (byte)((InLobby ? LobbyFlagOpen : 0) | (LobbyForced ? LobbyFlagForced : 0) | (ReturnToLobby ? LobbyFlagReturn : 0)
                           | (TrueRandom ? LobbyFlagTrueRandom : 0) | (FrenzyBuffsEnabled ? LobbyFlagFrenzy : 0));
        w.Begin(Msg.LobbyState).U16(LobbyVersion).U8(flags).Str(World.Map.Name).U32(World.Map.ContentHash)
            .U8((byte)Mode).U8((byte)ScoreLimitMode).U8((byte)MatchMinutes).U8((byte)Math.Min(WantedBots, 255)).U8((byte)_botSkill)
            .U16((ushort)Math.Clamp(AutoStartSeconds, 0, ushort.MaxValue));
        var countAt = w.Length;
        w.U8(0);
        byte n = 0;
        foreach (var p in Players.Values)
        {
            w.U8(p.Id).U16(ServerPingOf(p));
            n++;
        }
        w.PatchU8(countAt, n);
    }

    private ushort ServerPingOf(Player p)
    {
        if (p.Bot != null || p.Id == LocalId)
            return 0;
        foreach (var (guid, id) in _clients.Values)
            if (id == p.Id)
                return (ushort)Math.Clamp(Net.PingTo(guid), 0, ushort.MaxValue);
        return 0;
    }

    private void BroadcastLobbyState(bool reliable)
    {
        WriteLobbyState(_w);
        Net.Broadcast(_w.Span, reliable ? PacketReliability.ReliableOrdered : PacketReliability.Unreliable);
    }

    /// <summary>Server: a newcomer gets the lobby state right after the Welcome (map, settings, pings).</summary>
    private void SendLobbyState(RakNetGUID to)
    {
        WriteLobbyState(_w);
        Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: the host's lobby state. Older versions (an unreliable copy overtaken by a change) are ignored.</summary>
    public void ReadLobbyState(ref PacketReader r)
    {
        if (IsServer || !r.Has(3))
            return;
        var version = r.U16();
        if (_lobbyStateSeen && (short)(version - LobbyVersion) < 0)
            return;
        var flags = r.U8();
        var mapName = r.Str();
        if (!r.Has(13))
            return;
        var hash = r.U32();
        var mode = (GameMode)r.U8();
        var scoreLimit = r.U8() == (byte)ScoreLimitMode.Fixed ? ScoreLimitMode.Fixed : ScoreLimitMode.Auto;
        var minutes = r.U8();
        var bots = r.U8();
        var skill = r.U8();
        var auto = r.U16();
        int n = r.U8();
        Array.Clear(PingMs);
        for (var i = 0; i < n && r.Has(3); i++)
        {
            var id = r.U8();
            var ping = r.U16();
            if (id < PingMs.Length)
                PingMs[id] = ping;
        }
        _lobbyStateSeen = true;
        if (version == LobbyVersion && _lobbyMapName == mapName)
            return; // just the pings
        LobbyVersion = version;
        LobbyForced = (flags & LobbyFlagForced) != 0;
        ReturnToLobby = (flags & LobbyFlagReturn) != 0;
        TrueRandom = (flags & LobbyFlagTrueRandom) != 0;
        FrenzyBuffsEnabled = (flags & LobbyFlagFrenzy) != 0;
        if (GameModeRegistry.IsKnown(mode))
            _mode = mode; // (clients never reset on a mode change: the server's mode state follows)
        ScoreLimitMode = scoreLimit;
        MatchMinutes = minutes;
        WantedBots = bots;
        _botSkill = Enum.IsDefined((BotDifficulty)skill) ? (BotDifficulty)skill : BotDifficulty.Normal;
        AutoStartSeconds = auto;
        if (LocalId == byte.MaxValue || mapName.Length == 0)
            return; // not welcomed yet: the Welcome names the map
        _lobbyMapName = mapName;
        if (!mapName.Equals(World.Map.Name, StringComparison.Ordinal) && UseHostMap(mapName, hash, initial: false))
        {
            _ = World.Map.Objectives; // the lobby is a loading moment: get the new map ready now
            _ = World.Physics;
        }
    }

    private bool _lobbyStateSeen;
    private string _lobbyMapName = "";

    /// <summary>Client: the map the host's last lobby state named (the self-test checks a map change arrives).</summary>
    public string LobbyMapName => _lobbyMapName;

    // ---- Smoke and self-test hooks ------------------------------------------------------------------------------

    /// <summary>Smoke test / self-test (server): open the lobby room now, or (false) leave it for the warmup.</summary>
    public void DebugLobby(bool open, double now)
    {
        if (!IsServer)
            return;
        if (open)
            BeginLobby(now);
        else if (InLobby)
        {
            StartMatchFromLobby(now);
            if (WarmupEnabled)
            {
                BeginWarmup(now);
                foreach (var p in Players.Values)
                    if (p.Bot == null)
                        PlaceWaiting(p, now); // people wait to deploy, as at the start of a session
            }
        }
    }

    /// <summary>Self-test: the server's ping table entry for a player (what the lobby state sends).</summary>
    public ushort SelfTestPingOf(Player p) => ServerPingOf(p);
}
