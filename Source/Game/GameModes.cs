using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The rules a match is played under. The host picks one; it stays the same from match to match. One byte on the wire
/// (packets, server info, settings.txt): never renumber a shipped value. Each value has a module in Source/Game/Modes
/// (registered in <see cref="GameModeRegistry"/>), which is where the mode's rules live.
/// </summary>
public enum GameMode : byte
{
    FreeForAll, // first to 11 kills, win by 2 (the original rules)
    TeamDeathmatch, // two teams, first team to 40 kills
    KingOfTheHill, // two teams hold a moving hill, first to 150 points
    CaptureTheFlag, // two teams, first to 3 captures
    GunGame, // everyone climbs the same weapon ladder; first kill with the last gun wins
    Zombies, // co-op (1-4) against rounds of zombies, World at War style; only on maps with a "zombies" section (ZombiesMode.cs)
    LastManStanding, // one life each (the template mode, Dev-only: Source/Game/Modes/README.md)
}

/// <summary>Names, limits and colours for the game modes: a thin front for <see cref="GameModeRegistry"/>, plus the shared team names and colours.</summary>
public static class GameModes
{
    /// <summary>The player-versus-player modes (every map plays them). Zombies is separate: see <see cref="Every"/> and <see cref="ForMap"/>.</summary>
    public static readonly GameMode[] All = [GameMode.FreeForAll, GameMode.TeamDeathmatch, GameMode.KingOfTheHill, GameMode.CaptureTheFlag, GameMode.GunGame];

    /// <summary>Every shipped mode, Zombies included (not the Dev-only ones: <see cref="GameModeRegistry.All"/> has those too).</summary>
    public static readonly GameMode[] Every = GameModeRegistry.Shipped.Select(m => m.Id).ToArray();

    /// <summary>The modes a map can be played in: Zombies only on maps with a "zombies" section, Dev-only modes only on dev-pack maps.</summary>
    public static GameMode[] ForMap(Map? map) => GameModeRegistry.ForMap(map).Select(m => m.Id).ToArray();

    /// <summary>Can <paramref name="mode"/> be played on <paramref name="map"/>?</summary>
    public static bool Fits(GameMode mode, Map? map) => GameModeRegistry.Get(mode).Fits(map);

    /// <summary>Team Deathmatch: team kills to win with up to 8 players (4 v 4, so about 10 kills each).</summary>
    public const int TdmScoreLimit = TeamDeathmatchModule.ScoreLimit;

    /// <summary>Team Deathmatch: kills to win with this many players (Auto score limit, see ScoreLimits.cs).</summary>
    public static int TdmLimitFor(int players) => ScoreLimits.For(GameMode.TeamDeathmatch, players);

    /// <summary>King of the Hill: points to win (1 point per second of holding the hill alone).</summary>
    public const int HillScoreLimit = KingOfTheHillModule.ScoreLimit;

    /// <summary>King of the Hill: the hill moves to the next zone this often.</summary>
    public const double HillMoveSeconds = KingOfTheHillModule.MoveSeconds;

    /// <summary>Capture the Flag: captures to win.</summary>
    public const int CtfCaptureLimit = CaptureTheFlagModule.CaptureLimit;

    /// <summary>Capture the Flag: a dropped flag goes home on its own after this long.</summary>
    public const double FlagReturnSeconds = CaptureTheFlagModule.FlagReturnSeconds;

    /// <summary>Capture the Flag: how close (horizontally) you must get to touch a flag or your base.</summary>
    public const float FlagTouchRadius = CaptureTheFlagModule.FlagTouchRadius;

    public static readonly string[] TeamNames = ["Blue", "Red"];

    public static readonly Vector3[] TeamColors = [new(0.25f, 0.55f, 1f), new(1f, 0.3f, 0.25f)];

    public static string Name(GameMode m) => GameModeRegistry.Get(m).Name;

    /// <summary>Short tag for tight spaces (server browser, HUD).</summary>
    public static string Short(GameMode m) => GameModeRegistry.Get(m).Short;

    public static bool IsTeam(GameMode m) => GameModeRegistry.Get(m).TeamBased;

    /// <summary>One line of rules for the lobby and the countdown. <paramref name="limit"/>: this match's score limit (0: the classic one).</summary>
    public static string Rules(GameMode m, int limit = 0) => GameModeRegistry.Get(m).Rules(limit);

    public static Vector3 TeamColor(byte team) => team < 2 ? TeamColors[team] : new Vector3(0.85f, 0.85f, 0.85f);

    public static string TeamName(byte team) => team < 2 ? TeamNames[team] : "Nobody";
}

/// <summary>
/// Game modes on the server and client: the active <see cref="Module"/>, teams and the friendly-fire rule (shared by
/// every team mode), the match-flow calls into the module, and the replicated mode state (the shared part here, each
/// module's own tail through <see cref="GameModeModule.WriteState"/>). The modes themselves live in Source/Game/Modes.
/// </summary>
public sealed partial class Game
{
    public const byte NoTeam = byte.MaxValue;

    /// <summary><see cref="Winner"/> for a team win is TeamWinBase + team.</summary>
    public const byte TeamWinBase = 250;

    private GameMode _mode;

    /// <summary>This game's instance of every registered mode (the active one is <see cref="Module"/>; the others keep their state cleared).</summary>
    private readonly Dictionary<GameMode, GameModeModule> _modules = GameModeRegistry.CreateAll();

    /// <summary>The rules (set by the host; clients learn it from the mode state). Changing it on the server resets teams and objectives.</summary>
    public GameMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            _mode = GameModeRegistry.IsKnown(value) ? value : GameMode.FreeForAll;
            if (IsServer)
                ResetModeForMatch(respawn: true);
        }
    }

    /// <summary>The active mode's module: its rules and hooks.</summary>
    public GameModeModule Module => _modules[_mode];

    /// <summary>This game's instance of one mode (active or not).</summary>
    public T ModuleOf<T>() where T : GameModeModule => _modules.Values.OfType<T>().First();

    private KingOfTheHillModule? _koth;
    private CaptureTheFlagModule? _ctf;
    private KingOfTheHillModule Koth => _koth ??= ModuleOf<KingOfTheHillModule>();
    private CaptureTheFlagModule Ctf => _ctf ??= ModuleOf<CaptureTheFlagModule>();

    public bool IsTeamMode => Module.TeamBased;

    /// <summary>
    /// The colour a player is shown in on the HUD and their name tag: their team's colour in team modes, the friendly team colour
    /// (Blue) for everyone in a co-op mode (Zombies), otherwise their own <see cref="Player.Color"/>.
    /// </summary>
    public Vector3 SideColor(Player p) => Module.CoOp ? GameModes.TeamColors[0] : p.Color;

    /// <summary>Kill streaks are off in Gun Game and Zombies (<see cref="StreaksEnabled"/> follows it).</summary>
    public bool ModeAllowsStreaks => Module.AllowsStreaks;

    /// <summary>No loadout screen at all (Gun Game, Zombies, the Workbench Range): you spawn straight in with what the mode hands out.</summary>
    public bool SkipsLoadout => !Module.UsesLoadout || BuilderRange;

    /// <summary>Team score: kills (TDM), hill points (KotH) or captures (CTF).</summary>
    public readonly int[] TeamScore = new int[2];

    // King of the Hill (replicated; the state lives in KingOfTheHillModule)
    public int HillIndex => Koth.HillIndex;
    public byte HillOwner => Koth.HillOwner; // the one team on the hill, or NoTeam
    public bool HillContested => Koth.HillContested;
    public double HillMoveIn => Koth.HillMoveIn; // seconds until the hill moves

    // Capture the Flag (replicated; the state lives in CaptureTheFlagModule)
    public FlagState[] Flags => Ctf.Flags;

    private uint _lastModeTick;

    public MapObjectives Objectives => World.Map.Objectives;

    /// <summary>The hill in play (King of the Hill only).</summary>
    public HillZone? ActiveHill => Module is KingOfTheHillModule k ? k.ActiveHill(this) : null;

    public static bool InHill(Vector3 feet, HillZone hill) => KingOfTheHillModule.InHill(feet, hill);

    /// <summary>Which flag (team index) this player is carrying, or -1.</summary>
    public int CarriedFlagOf(Player p) => Ctf.CarriedFlagOf(p);

    /// <summary>Gun Game: the player furthest up the ladder (most kills breaks ties).</summary>
    public Player? GunLeader => ModuleOf<GunGameModule>().Leader(this);

    /// <summary>Are these two on opposite sides? Everyone is an enemy in free-for-all and Gun Game; teammates never are; nobody is in co-op (Zombies).</summary>
    public bool AreEnemies(Player a, Player b) => a != b && !Module.CoOp && (!IsTeamMode || a.Team > 1 || a.Team != b.Team);

    /// <summary>Did the local player (or their team) win the match that just ended?</summary>
    public bool LocalWon =>
        Winner == LocalId || (Winner is >= TeamWinBase and < TeamWinBase + 2 && Local is { } me && me.Team == Winner - TeamWinBase);

    /// <summary>The goal of the current mode, one line ("first to 11 kills..."), for the HUD and the log.</summary>
    public string ModeGoalText => Module.GoalText(this);

    /// <summary>Damage is allowed against enemies and yourself (your own grenade still hurts), never teammates.</summary>
    private bool CanDamage(Player attacker, Player victim) => attacker == victim || AreEnemies(attacker, victim);

    // ---- Teams (server) ---------------------------------------------------------------------

    /// <summary>
    /// Splits everyone into two teams for a new match: people who picked a side keep it (TeamPick.cs), then people on
    /// Auto (alternating onto the smaller side), then bots fill the smaller team. Deterministic (by player id).
    /// </summary>
    private void AssignTeams()
    {
        if (!IsTeamMode)
        {
            foreach (var p in Players.Values)
                p.Team = NoTeam;
            return;
        }
        if (HumansVsBots)
        {
            ApplyHumansVsBots(); // people on one side, bots on the other (TeamBalance.cs)
            return;
        }
        Span<int> n = stackalloc int[2];
        foreach (var p in Players.Values)
            if (p.Bot != null || TeamSelection == TeamSelection.AutoOnly)
                p.TeamChoice = NoTeam;
        // Too many picks on one side (people left, or the host lowered the limit): the newest picks there go back to Auto.
        for (var guard = 0; guard < Players.Count; guard++)
        {
            int p0 = Players.Values.Count(p => p.TeamChoice == 0), p1 = Players.Values.Count(p => p.TeamChoice == 1);
            var big = (byte)(p0 >= p1 ? 0 : 1);
            if (!TeamTooBig(big, null))
                break;
            Players.Values.Where(p => p.TeamChoice == big).OrderByDescending(p => p.Id).First().TeamChoice = NoTeam;
        }
        foreach (var p in Players.Values.Where(p => p.TeamChoice < 2))
        {
            p.Team = p.TeamChoice;
            n[p.Team]++;
        }
        foreach (var p in Players.Values.Where(p => p.TeamChoice > 1).OrderBy(p => p.Bot != null).ThenBy(p => p.Id))
        {
            var t = n[0] <= n[1] ? 0 : 1;
            p.Team = (byte)t;
            n[t]++;
        }
    }

    private int TeamCount(int team) => Players.Values.Count(p => p.Team == team);

    /// <summary>Gives a newcomer their picked team or else the smaller team (or no team outside team modes).</summary>
    private void EnsureTeam(Player p)
    {
        if (!IsTeamMode)
            p.Team = NoTeam;
        else if (p.Team > 1 && HumansVsBots)
            p.Team = HvbTeamOf(p); // TeamBalance.cs
        else if (p.Team > 1)
            p.Team = p.TeamChoice < 2 ? p.TeamChoice : (byte)(TeamCount(0) <= TeamCount(1) ? 0 : 1);
    }

    /// <summary>Spawn points for a player: their team's half of the map in team modes.</summary>
    private List<Vector3> SpawnPointsFor(Player p)
    {
        if (!IsTeamMode)
            return World.Map.Spawns;
        EnsureTeam(p);
        var list = Objectives.TeamSpawns[p.Team];
        return list.Count > 0 ? list : World.Map.Spawns;
    }

    /// <summary>
    /// The teams are 2+ apart (someone left or picked a side): move one player over and return true. A bot first (a dead
    /// one if possible), else a person on Auto who is dead or between matches. Never someone who picked their team
    /// (TeamPick.cs), never a flag carrier.
    /// </summary>
    private bool BalanceTeams(double now)
    {
        if (!AutoBalanceOn)
            return false; // the host turned balancing off, or Humans vs Bots (TeamBalance.cs)
        int a = TeamCount(0), b = TeamCount(1);
        if (Math.Abs(a - b) < 2)
            return false;
        var big = (byte)(a > b ? 0 : 1);
        var bot = Players.Values.Where(p => p.Bot != null && p.Team == big && CarriedFlagOf(p) < 0).OrderBy(p => p.Alive).FirstOrDefault()
                  ?? Players.Values.Where(p => p.Bot == null && p.TeamChoice > 1 && p.Team == big && CarriedFlagOf(p) < 0 && (!p.Alive || Phase != MatchPhase.Playing))
                      .OrderByDescending(p => p.Id).FirstOrDefault();
        if (bot == null)
            return false;
        bot.Team = (byte)(1 - big);
        if (bot.Alive && Phase != MatchPhase.Ended)
        {
            Spawn(bot, ServerTick, now);
            if (bot.Id == LocalId)
                OnLocalSpawn(bot);
        }
        ModeEvent($"{bot.Name} moved to {GameModes.TeamName(bot.Team)} to even the teams", Sfx.HitTick);
        return true;
    }

    // ---- Match flow (server) -------------------------------------------------------------------

    /// <summary>Fresh scores, teams and objectives for a new match (or a mode change). Optionally respawns everyone who's alive.</summary>
    private void ResetModeForMatch(bool respawn)
    {
        TeamScore[0] = TeamScore[1] = 0;
        ScoreLimit = ScoreLimitFor(Players.Count); // the new mode's own limit (ScoreLimits.cs)
        AssignTeams();
        foreach (var m in _modules.Values)
            m.OnReset(this); // every module clears its state (the hill, the flags, gun levels, a Zombies run...)
        if (!respawn)
            return;
        foreach (var p in Players.Values.Where(p => p.Alive).ToList())
        {
            Spawn(p, ServerTick, Brutal.GlfwApi.Glfw.Time);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
        }
    }

    /// <summary>The map changed under us (hot swap): objectives come from the new map.</summary>
    private void ResetObjectives()
    {
        foreach (var m in _modules.Values)
            m.OnMapChanged(this);
    }

    /// <summary>Client: a match started; every module resets what it shows (Zombies: doors shut, no zombies).</summary>
    private void ClientModeMatchStart()
    {
        foreach (var m in _modules.Values)
            m.OnClientMatchStart(this);
    }

    /// <summary>Server, every tick in every phase: teams, and the mode state for clients.</summary>
    private void ServerTickMode(double now)
    {
        if (IsTeamMode)
        {
            foreach (var p in Players.Values)
                if (p.Team > 1)
                    EnsureTeam(p);
            BalanceTeams(now);
        }
        if (ServerTick % (World.SnapshotEvery * 3) == 0 && !Net.IsOffline) // 10 Hz
        {
            WriteModeState(_w);
            Net.Broadcast(_w.Span, PacketReliability.Unreliable);
        }
    }

    /// <summary>Server, every tick while playing: the mode's objectives, then its win check and the time limit.</summary>
    private void ServerTickModePlaying(double now) => Module.ServerTickPlaying(this, now);

    /// <summary>Server: someone was killed (after the kill was counted and announced).</summary>
    private void OnModeKill(Player attacker, Player victim, byte killId, double now) => Module.OnKill(this, attacker, victim, killId, now);

    /// <summary>Server: a player left the match.</summary>
    private void OnModePlayerLeave(Player p) => Module.OnPlayerLeave(this, p);

    /// <summary>The mode's part of a spawn: fixed perks, no throwables, then its own hand-outs (Gun Game's ladder gun, Zombies' start gear).</summary>
    private void ApplyModeLoadout(Player p)
    {
        var m = Module;
        if (!m.UsesThrowables)
        {
            p.Grenade = p.Equipment = ThrowableKind.None;
            p.GrenadesLeft = p.EquipmentLeft = 0;
        }
        if (m.FixedPerks is { } perks)
            ApplyPerks(p, perks); // the same for everyone
        m.OnSpawn(this, p);
    }

    // ---- Packets ---------------------------------------------------------------------------------

    /// <summary>Server: a line for everyone's kill feed with a sound.</summary>
    internal void ModeEvent(string text, Sfx sound)
    {
        Log(text, true);
        Audio.Effect(sound, 0.6f);
        if (!IsServer || Net.IsOffline)
            return;
        _w.Begin(Msg.ModeEvent).U8((byte)sound).Str(text.Length > 100 ? text[..100] : text);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>
    /// The mode state (teams, ready flags, gun levels, scores, the score limit, team picks), then the active mode's own
    /// tail: its version byte, a length, and whatever its <see cref="GameModeModule.WriteState"/> wrote. Sent ~10 times a second.
    /// </summary>
    public void WriteModeState(PacketWriter w)
    {
        w.Begin(Msg.ModeState).U32(ServerTick).U8((byte)Mode).U16((ushort)TeamScore[0]).U16((ushort)TeamScore[1]).U8((byte)Players.Count);
        foreach (var p in Players.Values)
            w.U8(p.Id).U8(p.Team).U8((byte)(p.Bot != null || p.Ready ? 1 : 0)).U8(p.GunLevel);
        w.U16((ushort)Math.Min(ScoreLimit, ushort.MaxValue)); // protocol 11: the TDM limit; protocol 21: the current mode's score limit
        WriteTeamPicks(w); // protocol 20: team selection rules and picks (TeamPick.cs)
        // Protocol 22: the mode's own tail (before it, the hill and the flags were written for every mode).
        var m = Module;
        w.U8(m.StateVersion);
        var lengthAt = w.Length;
        w.U16(0);
        m.WriteState(this, w);
        w.PatchU16(lengthAt, (ushort)(w.Length - lengthAt - 2));
    }

    /// <summary>Client: applies a mode state (the message id already read). Stale packets are ignored.</summary>
    public void ReadModeState(ref PacketReader r)
    {
        if (!r.Has(10))
            return;
        var tick = r.U32();
        if (tick < _lastModeTick)
            return;
        _lastModeTick = tick;
        var mode = (GameMode)r.U8();
        _mode = GameModeRegistry.IsKnown(mode) ? mode : GameMode.FreeForAll;
        TeamScore[0] = r.U16();
        TeamScore[1] = r.U16();
        int n = r.U8();
        var keepLocalReady = Brutal.GlfwApi.Glfw.Time - _readyToggledAt < 0.6;
        for (var i = 0; i < n && r.Has(4); i++)
        {
            var id = r.U8();
            var team = r.U8();
            var ready = r.U8() != 0;
            var level = r.U8();
            if (!Players.TryGetValue(id, out var p))
                continue;
            p.Team = team;
            p.GunLevel = level;
            if (!(id == LocalId && keepLocalReady))
                p.Ready = ready;
        }
        if (r.Has(2))
            ScoreLimit = r.U16();
        ReadTeamPicks(ref r); // protocol 20 (TeamPick.cs)
        if (!r.Has(3))
            return;
        var version = r.U8();
        int length = r.U16();
        if (!r.Has(length))
            return;
        var tail = new PacketReader(r.Take(length));
        if (version == Module.StateVersion)
            Module.ReadState(this, ref tail); // another version: the tail is skipped rather than misread
    }

    private void OnModePacket(byte id, ref PacketReader r, RakNetGUID sender)
    {
        switch (id)
        {
            case (byte)Msg.ModeState when !IsServer:
                ReadModeState(ref r);
                break;
            case (byte)Msg.ModeEvent when !IsServer && r.Has(2):
            {
                var sound = (Sfx)r.U8();
                Log(r.Text(), true); // printable ASCII from the host
                if (sound < Sfx.COUNT)
                    Audio.Effect(sound, 0.6f);
                break;
            }
            case (byte)Msg.Ready when IsServer && r.Has(1):
                if (_clients.TryGetValue(sender.G, out var c) && Players.TryGetValue(c.Id, out var p))
                    ServerSetReady(p, r.U8() != 0);
                break;
        }
    }

    // ---- Self-test hooks -------------------------------------------------------------------------

    /// <summary>Self-test: run the objective logic and win check for one tick at <paramref name="now"/> (no movement, no bots).</summary>
    public void SelfTestModeTick(double now)
    {
        ServerTick++;
        if (Phase == MatchPhase.Playing)
            ServerTickModePlaying(now);
    }

    /// <summary>Self-test: damage with a given kill id (weapon id, or <see cref="MeleeKillId"/>).</summary>
    public bool SelfTestDamageWith(Player attacker, Player victim, int amount, byte killId, double now, DamageKind kind = DamageKind.Bullet) =>
        DamagePlayer(attacker, victim, amount, false, killId, now, kind);

    /// <summary>Self-test: spawn a player the normal way (team spawns, Gun Game weapon...).</summary>
    public void SelfTestSpawn(Player p) => Spawn(p, ServerTick, 0);

    /// <summary>Self-test: a pretend remote person (no network), waiting to deploy like a real joiner.</summary>
    public Player SelfTestAddHuman()
    {
        var id = FreeId() ?? throw new InvalidOperationException("server full");
        var p = new Player { Id = id, Name = $"Tester {id}" };
        Players[id] = p;
        PlaceWaiting(p, 0);
        return p;
    }
}
