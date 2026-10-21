using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>The rules a match is played under. The host picks one; it stays the same from match to match.</summary>
public enum GameMode : byte
{
    FreeForAll, // first to 11 kills, win by 2 (the original rules)
    TeamDeathmatch, // two teams, first team to 40 kills
    KingOfTheHill, // two teams hold a moving hill, first to 150 points
    CaptureTheFlag, // two teams, first to 3 captures
    GunGame, // everyone climbs the same weapon ladder; first kill with the last gun wins
    Zombies, // co-op (1-4) against rounds of zombies, World at War style; only on maps with a "zombies" section (ZombiesMode.cs)
}

/// <summary>Names, limits and colours for the game modes.</summary>
public static class GameModes
{
    /// <summary>The player-versus-player modes (every map plays them). Zombies is separate: see <see cref="Every"/> and <see cref="ForMap"/>.</summary>
    public static readonly GameMode[] All = [GameMode.FreeForAll, GameMode.TeamDeathmatch, GameMode.KingOfTheHill, GameMode.CaptureTheFlag, GameMode.GunGame];

    /// <summary>Every mode, Zombies included.</summary>
    public static readonly GameMode[] Every = [.. All, GameMode.Zombies];

    /// <summary>The modes a map can be played in: Zombies only on maps with a "zombies" section.</summary>
    public static GameMode[] ForMap(Map? map) => map?.Zombies != null ? Every : All;

    /// <summary>Can <paramref name="mode"/> be played on <paramref name="map"/>?</summary>
    public static bool Fits(GameMode mode, Map? map) => mode != GameMode.Zombies || map?.Zombies != null;

    /// <summary>Team Deathmatch: team kills to win with up to 8 players (4 v 4, so about 10 kills each).</summary>
    public const int TdmScoreLimit = 40;

    /// <summary>Team Deathmatch: kills to win with this many players (Auto score limit, see ScoreLimits.cs).</summary>
    public static int TdmLimitFor(int players) => ScoreLimits.For(GameMode.TeamDeathmatch, players);

    /// <summary>King of the Hill: points to win (1 point per second of holding the hill alone).</summary>
    public const int HillScoreLimit = 150;

    /// <summary>King of the Hill: the hill moves to the next zone this often.</summary>
    public const double HillMoveSeconds = 60;

    /// <summary>Capture the Flag: captures to win.</summary>
    public const int CtfCaptureLimit = 3;

    /// <summary>Capture the Flag: a dropped flag goes home on its own after this long.</summary>
    public const double FlagReturnSeconds = 20;

    /// <summary>Capture the Flag: how close (horizontally) you must get to touch a flag or your base.</summary>
    public const float FlagTouchRadius = 1.6f;

    public static readonly string[] TeamNames = ["Blue", "Red"];

    public static readonly Vector3[] TeamColors = [new(0.25f, 0.55f, 1f), new(1f, 0.3f, 0.25f)];

    public static string Name(GameMode m) => m switch
    {
        GameMode.TeamDeathmatch => "Team Deathmatch",
        GameMode.KingOfTheHill => "King of the Hill",
        GameMode.CaptureTheFlag => "Capture the Flag",
        GameMode.GunGame => "Gun Game",
        GameMode.Zombies => "Zombies",
        _ => "Free-for-all",
    };

    /// <summary>Short tag for tight spaces (server browser, HUD).</summary>
    public static string Short(GameMode m) => m switch
    {
        GameMode.TeamDeathmatch => "TDM",
        GameMode.KingOfTheHill => "KOTH",
        GameMode.CaptureTheFlag => "CTF",
        GameMode.GunGame => "GUN",
        GameMode.Zombies => "ZOM",
        _ => "FFA",
    };

    public static bool IsTeam(GameMode m) => m is GameMode.TeamDeathmatch or GameMode.KingOfTheHill or GameMode.CaptureTheFlag;

    /// <summary>One line of rules for the lobby and the countdown. <paramref name="limit"/>: this match's score limit (0: the classic one).</summary>
    public static string Rules(GameMode m, int limit = 0) => m switch
    {
        GameMode.TeamDeathmatch => $"Two teams. First team to {(limit > 0 ? limit : TdmScoreLimit)} kills. No friendly fire.",
        GameMode.KingOfTheHill => $"Two teams. Hold the hill alone for 1 point a second (contested: nobody scores). It moves every {HillMoveSeconds:0} s. First to {(limit > 0 ? limit : HillScoreLimit)}.",
        GameMode.CaptureTheFlag => $"Two teams. Grab the enemy flag and bring it to yours while yours is home. First to {(limit > 0 ? limit : CtfCaptureLimit)} captures.",
        GameMode.GunGame => $"Everyone gets the same gun. Each kill moves you up a weapon ({GunGame.Ladder.Length} in all); a melee kill also sets the victim back one. Win with a kill using the last gun. No loadouts: everyone has the same perks (Afterburner, Bloodthirst) and no grenades or equipment.",
        GameMode.Zombies => "Co-op for 1 to 4 players against endless rounds of zombies. Earn points, open doors, buy guns off the walls and from the mystery box, and build silly guns at the workbench. The run ends when everyone is down.",
        _ => $"First to {(limit > 0 ? limit : Game.WinKills)} kills, win by {Game.WinMargin}.",
    };

    public static Vector3 TeamColor(byte team) => team < 2 ? TeamColors[team] : new Vector3(0.85f, 0.85f, 0.85f);

    public static string TeamName(byte team) => team < 2 ? TeamNames[team] : "Nobody";
}

/// <summary>
/// Gun Game's weapon ladder: the weapons in the order Progression unlocks them (starters first), cut down to
/// <see cref="Steps"/> evenly spaced steps so a match doesn't take 50 kills. The first step is the first weapon of the
/// unlock order and the last step is the last one. Everyone plays the ladder whatever they have unlocked.
/// </summary>
public static class GunGame
{
    public const int Steps = 20;

    /// <summary>
    /// Everyone carries the same perks in Gun Game: speed and a full heal after every kill, to keep the climb moving.
    /// No damage perk, so every gun on the ladder hits exactly as hard as it normally does.
    /// </summary>
    public static readonly PerkSet Perks = new(PerkId.Afterburner, PerkId.Bloodthirst, PerkId.None);

    private static WeaponDef[]? _ladder;
    private static WeaponDef[]? _builtFrom;

    public static WeaponDef[] Ladder
    {
        get
        {
            if (_ladder == null || _builtFrom != Weapons.All)
            {
                _builtFrom = Weapons.All;
                var order = Progress.All.Where(u => u.Weapon != null).Select(u => u.Weapon!).Distinct().ToList();
                _ladder = Build(order.Count > 0 ? order : [Weapons.DefaultPrimary]);
            }
            return _ladder;
        }
    }

    /// <summary>Picks <see cref="Steps"/> evenly spaced weapons from an ordered list (all of them if there are fewer).</summary>
    public static WeaponDef[] Build(IReadOnlyList<WeaponDef> order)
    {
        if (order.Count <= Steps)
            return order.ToArray();
        var r = new WeaponDef[Steps];
        for (var i = 0; i < Steps; i++)
            r[i] = order[(int)Math.Round(i * (order.Count - 1) / (double)(Steps - 1))];
        return r;
    }

    public static int Final => Ladder.Length - 1;

    public static WeaponDef At(int level) => Ladder[Math.Clamp(level, 0, Final)];
}

/// <summary>Capture the Flag: one team's flag.</summary>
public sealed class FlagState
{
    public Vector3 Position;
    public byte Carrier = byte.MaxValue; // player id, or byte.MaxValue
    public bool Home = true;
    public double ReturnIn; // seconds until a dropped flag goes home (replicated)
    internal double ReturnAt;

    public bool Carried => Carrier != byte.MaxValue;
    public bool Dropped => !Home && !Carried;
}

/// <summary>
/// Game modes on the server and client: teams, the friendly-fire rule, objectives (hill, flags), Gun Game's ladder,
/// mode-specific win checks and the replicated mode state. Free-for-all keeps the original match rules in Match.cs.
/// </summary>
public sealed partial class Game
{
    public const byte NoTeam = byte.MaxValue;

    /// <summary><see cref="Winner"/> for a team win is TeamWinBase + team.</summary>
    public const byte TeamWinBase = 250;

    private GameMode _mode;

    /// <summary>The rules (set by the host; clients learn it from the mode state). Changing it on the server resets teams and objectives.</summary>
    public GameMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
                return;
            _mode = value;
            if (IsServer)
                ResetModeForMatch(respawn: true);
        }
    }

    public bool IsTeamMode => GameModes.IsTeam(Mode);

    /// <summary>Kill streaks are off in Gun Game (<see cref="StreaksEnabled"/> follows it).</summary>
    public bool ModeAllowsStreaks => Mode is not (GameMode.GunGame or GameMode.Zombies);

    /// <summary>Gun Game: no loadout screen at all; you spawn straight in with the ladder's gun and the shared perks.</summary>
    public bool SkipsLoadout => Mode is GameMode.GunGame or GameMode.Zombies || BuilderRange; // the Workbench Range and Zombies (start gear) have no loadout screen either

    /// <summary>Team score: kills (TDM), hill points (KotH) or captures (CTF).</summary>
    public readonly int[] TeamScore = new int[2];

    // King of the Hill (replicated)
    public int HillIndex;
    public byte HillOwner = NoTeam; // the one team on the hill, or NoTeam
    public bool HillContested;
    public double HillMoveIn; // seconds until the hill moves
    private double _hillMoveAt = -1, _hillAccum;

    // Capture the Flag (replicated)
    public readonly FlagState[] Flags = [new(), new()];

    private Player? _gunWinner;
    private uint _lastModeTick;

    public MapObjectives Objectives => World.Map.Objectives;

    /// <summary>The hill in play (King of the Hill only).</summary>
    public HillZone? ActiveHill => Mode == GameMode.KingOfTheHill && Objectives.Hills.Count > 0 ? Objectives.Hills[HillIndex % Objectives.Hills.Count] : null;

    /// <summary>Are these two on opposite sides? Everyone is an enemy in free-for-all and Gun Game; teammates never are.</summary>
    public bool AreEnemies(Player a, Player b) => a != b && Mode != GameMode.Zombies && (!IsTeamMode || a.Team > 1 || a.Team != b.Team); // Zombies: everyone's on the same side

    /// <summary>Did the local player (or their team) win the match that just ended?</summary>
    public bool LocalWon =>
        Winner == LocalId || (Winner is >= TeamWinBase and < TeamWinBase + 2 && Local is { } me && me.Team == Winner - TeamWinBase);

    /// <summary>The goal of the current mode, one line ("first to 11 kills..."), for the HUD and the log.</summary>
    public string ModeGoalText => Mode == GameMode.FreeForAll
        ? $"First to {ScoreLimit} kills, win by {WinMargin}, {MatchSeconds / 60:0} minutes max"
        : Mode == GameMode.Zombies
            ? $"{GameModes.Name(Mode)}: {GameModes.Rules(Mode)}"
            : $"{GameModes.Name(Mode)}: {GameModes.Rules(Mode, ScoreLimit)} {MatchSeconds / 60:0} minutes max.";

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
        HillIndex = 0;
        HillOwner = NoTeam;
        HillContested = false;
        _hillMoveAt = -1;
        _hillAccum = 0;
        HillMoveIn = GameModes.HillMoveSeconds;
        ResetFlags();
        _gunWinner = null;
        foreach (var p in Players.Values)
            p.GunLevel = 0;
        ScoreLimit = ScoreLimitFor(Players.Count); // the new mode's own limit (ScoreLimits.cs)
        AssignTeams();
        if (World.Map.Zombies != null || Mode == GameMode.Zombies)
            ResetZombiesRun(); // a fresh Zombies run, or a Zombies map's doors opened for other modes (ZombiesMode.cs)
        if (!respawn)
            return;
        foreach (var p in Players.Values.Where(p => p.Alive).ToList())
        {
            Spawn(p, ServerTick, Brutal.GlfwApi.Glfw.Time);
            if (p.Id == LocalId)
                OnLocalSpawn(p);
        }
    }

    private void ResetFlags()
    {
        for (var t = 0; t < 2; t++)
        {
            Flags[t].Position = Objectives.Flags[t];
            Flags[t].Carrier = byte.MaxValue;
            Flags[t].Home = true;
            Flags[t].ReturnIn = 0;
        }
    }

    /// <summary>The map changed under us (hot swap): objectives come from the new map.</summary>
    private void ResetObjectives()
    {
        HillIndex = 0;
        _hillMoveAt = -1;
        ResetFlags();
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

    /// <summary>Server, every tick while playing (not free-for-all): objectives, then the win check and the time limit.</summary>
    private void ServerTickModePlaying(double now)
    {
        if (Mode == GameMode.Zombies)
        {
            ServerTickZombies(now); // rounds, zombies, buyables, downs; the run ends when everyone is down (no time limit)
            return;
        }
        switch (Mode)
        {
            case GameMode.KingOfTheHill:
                TickHill(now);
                break;
            case GameMode.CaptureTheFlag:
                TickFlags(now);
                break;
        }

        var limit = IsTeamMode && ScoreLimit > 0 ? ScoreLimit : int.MaxValue; // ScoreLimits.cs
        if (SoloMatch && IsTeamMode)
            SoloCapScores(limit); // alone: objectives still score, but never win (TestTools.cs)
        for (var t = 0; t < 2 && IsTeamMode; t++)
        {
            if (TeamScore[t] >= limit)
            {
                EndMatch(null, $"{GameModes.TeamNames[t]} team wins {TeamScore[t]}-{TeamScore[1 - t]}", now, (byte)(TeamWinBase + t));
                return;
            }
        }
        if (Mode == GameMode.GunGame && _gunWinner is { } champ)
        {
            EndMatch(champ, $"{NameOf(champ.Id)} wins with the {GunGame.At(GunGame.Final).Name}", now);
            return;
        }

        if (TimeLeft > 0)
            return;
        if (IsTeamMode)
        {
            if (TeamScore[0] == TeamScore[1])
                EndMatch(null, $"Time! Draw at {TeamScore[0]}-{TeamScore[1]}", now);
            else
            {
                var t = TeamScore[0] > TeamScore[1] ? 0 : 1;
                EndMatch(null, $"Time! {GameModes.TeamNames[t]} team wins {TeamScore[t]}-{TeamScore[1 - t]}", now, (byte)(TeamWinBase + t));
            }
        }
        else
        {
            var best = Players.Values.OrderByDescending(p => p.GunLevel).ThenByDescending(p => p.Kills).FirstOrDefault();
            var tied = best != null && Players.Values.Count(p => p.GunLevel == best.GunLevel && p.Kills == best.Kills) > 1;
            if (best == null || tied)
                EndMatch(null, "Time! Draw", now);
            else
                EndMatch(best, $"Time! {NameOf(best.Id)} wins on weapon {best.GunLevel + 1}/{GunGame.Ladder.Length}", now);
        }
    }

    /// <summary>Server: someone was killed (after the kill was counted and announced).</summary>
    private void OnModeKill(Player attacker, Player victim, byte killId, double now)
    {
        if (attacker == victim)
            return; // own grenade: no score either way
        switch (Mode)
        {
            case GameMode.TeamDeathmatch when attacker.Team < 2:
                TeamScore[attacker.Team]++;
                break;
            case GameMode.GunGame:
            {
                if (killId == MeleeKillId && victim.GunLevel > 0)
                {
                    victim.GunLevel--;
                    ModeEvent($"{victim.Name} was knifed back to the {GunGame.At(victim.GunLevel).Name}", Sfx.MeleeHit);
                }
                var atFinal = attacker.GunLevel >= GunGame.Final;
                if (atFinal && killId == GunGame.At(attacker.GunLevel).Id)
                    _gunWinner ??= attacker;
                else if (!atFinal)
                    GunLevelUp(attacker, attacker.GunLevel + 1);
                break;
            }
        }
    }

    // ---- Gun Game ------------------------------------------------------------------------------

    /// <summary>Gun Game: the server hands out the gun and the shared perks; loadouts, grenades and equipment don't apply.</summary>
    private void ApplyModeLoadout(Player p)
    {
        if (Mode == GameMode.Zombies)
            ZombiesOnSpawn(p); // start gear, the start room (ZombiesMode.cs)
        if (Mode != GameMode.GunGame)
            return;
        GiveGunGameWeapon(p);
        p.Grenade = p.Equipment = ThrowableKind.None;
        p.GrenadesLeft = p.EquipmentLeft = 0;
        ApplyPerks(p, GunGame.Perks); // the same for everyone
    }

    private void GiveGunGameWeapon(Player p)
    {
        var w = GunGame.At(p.GunLevel);
        p.Primary = p.Secondary = w.Id;
        p.PrimaryKit = p.SecondaryKit = 0;
        p.Weapon = w.Id;
    }

    private void GunLevelUp(Player p, int level)
    {
        p.GunLevel = (byte)Math.Clamp(level, 0, GunGame.Final);
        if (p.GunLevel == GunGame.Final)
            ModeEvent($"{p.Name} is on the last gun: the {GunGame.At(p.GunLevel).Name}!", Sfx.ReconBeep);
        if (!p.Alive)
            return;
        GiveGunGameWeapon(p);
        if (p.Bot is { } brain)
        {
            brain.Ammo = p.DefFor(p.Primary).Mag;
            brain.ReloadUntil = 0;
            brain.BurstLeft = 0;
        }
        if (p.Id == LocalId)
            SyncLocalSlots(p, refill: false);
    }

    /// <summary>Gun Game: the player furthest up the ladder (most kills breaks ties).</summary>
    public Player? GunLeader => Players.Values.OrderByDescending(p => p.GunLevel).ThenByDescending(p => p.Kills).ThenBy(p => p.Id).FirstOrDefault();

    // ---- King of the Hill ------------------------------------------------------------------------

    public static bool InHill(Vector3 feet, HillZone hill)
    {
        var d = new Vector2(feet.X - hill.Center.X, feet.Z - hill.Center.Z);
        // Height window measured from the ground, so a zone on a slope covers the whole slope (flat maps: ground = 0).
        var m = World.Map;
        var y = feet.Y - m.GroundHeight(feet.X, feet.Z) - (hill.Center.Y - m.GroundHeight(hill.Center.X, hill.Center.Z));
        return d.LengthSquared() <= hill.Radius * hill.Radius && y > -1f && y < 3f;
    }

    private void TickHill(double now)
    {
        if (Objectives.Hills.Count == 0)
            return;
        if (_hillMoveAt < 0)
            _hillMoveAt = now + GameModes.HillMoveSeconds;
        if (now >= _hillMoveAt)
        {
            HillIndex = (HillIndex + 1) % Objectives.Hills.Count;
            _hillMoveAt = now + GameModes.HillMoveSeconds;
            _hillAccum = 0;
            if (Objectives.Hills.Count > 1)
                ModeEvent("The hill has moved!", Sfx.ReconBeep);
        }
        HillMoveIn = Math.Max(0, _hillMoveAt - now);
        var hill = Objectives.Hills[HillIndex % Objectives.Hills.Count];

        bool blue = false, red = false;
        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Team > 1 || !InHill(p.Position, hill))
                continue;
            if (p.Team == 0)
                blue = true;
            else
                red = true;
        }
        HillContested = blue && red;
        var owner = blue == red ? NoTeam : blue ? (byte)0 : (byte)1;
        if (owner != HillOwner && owner != NoTeam)
            ModeEvent($"{GameModes.TeamNames[owner]} team has the hill", Sfx.HitTick);
        HillOwner = owner;
        if (owner == NoTeam)
            return; // empty or contested: nobody scores
        _hillAccum += World.TickDt;
        while (_hillAccum >= 1)
        {
            _hillAccum -= 1;
            TeamScore[owner]++;
        }
    }

    // ---- Capture the Flag ------------------------------------------------------------------------

    /// <summary>Which flag (team index) this player is carrying, or -1.</summary>
    public int CarriedFlagOf(Player p)
    {
        for (var t = 0; t < 2; t++)
            if (Flags[t].Carrier == p.Id)
                return t;
        return -1;
    }

    private static bool Touches(Vector3 feet, Vector3 at)
    {
        var d = new Vector2(feet.X - at.X, feet.Z - at.Z);
        return d.LengthSquared() <= GameModes.FlagTouchRadius * GameModes.FlagTouchRadius && MathF.Abs(feet.Y - at.Y) < 2f;
    }

    private void ReturnFlag(int team, string why)
    {
        var f = Flags[team];
        f.Position = Objectives.Flags[team];
        f.Carrier = byte.MaxValue;
        f.Home = true;
        f.ReturnIn = 0;
        ModeEvent($"{GameModes.TeamNames[team]} flag {why}", Sfx.Respawn);
    }

    private void TickFlags(double now)
    {
        // Carriers carry; a carrier who died or left drops the flag where they were.
        for (var t = 0; t < 2; t++)
        {
            var f = Flags[t];
            if (f.Carried)
            {
                if (Players.TryGetValue(f.Carrier, out var c) && c.Alive)
                    f.Position = c.Position;
                else
                {
                    f.Carrier = byte.MaxValue;
                    f.ReturnAt = now + GameModes.FlagReturnSeconds;
                    ModeEvent($"{GameModes.TeamNames[t]} flag dropped", Sfx.HitTick);
                }
            }
            if (f.Dropped && now >= f.ReturnAt)
                ReturnFlag(t, "returned to base");
            f.ReturnIn = f.Dropped ? Math.Max(0, f.ReturnAt - now) : 0;
        }

        foreach (var p in Players.Values)
        {
            if (!p.Alive || p.Team > 1)
                continue;
            var own = p.Team;
            var enemy = 1 - own;
            var carrying = CarriedFlagOf(p);
            // Pick up the enemy flag (home or dropped).
            if (carrying < 0 && !Flags[enemy].Carried && Touches(p.Position, Flags[enemy].Position))
            {
                Flags[enemy].Carrier = p.Id;
                Flags[enemy].Home = false;
                carrying = enemy;
                ModeEvent($"{p.Name} took the {GameModes.TeamNames[enemy]} flag!", Sfx.ReconBeep);
            }
            // Touching your own dropped flag sends it home.
            if (Flags[own].Dropped && Touches(p.Position, Flags[own].Position))
                ReturnFlag(own, $"returned by {p.Name}");
            // Capture: bring the enemy flag to your own flag, while yours is at home.
            if (carrying == enemy && Flags[own].Home && Touches(p.Position, Objectives.Flags[own]))
            {
                TeamScore[own]++;
                Flags[enemy].Carrier = byte.MaxValue;
                Flags[enemy].Home = true;
                Flags[enemy].Position = Objectives.Flags[enemy];
                ModeEvent($"{p.Name} CAPTURED the {GameModes.TeamNames[enemy]} flag! ({GameModes.TeamNames[own]} {TeamScore[own]}-{TeamScore[enemy]})", Sfx.LevelUp);
            }
        }
    }

    // ---- Packets ---------------------------------------------------------------------------------

    /// <summary>Server: a line for everyone's kill feed with a sound.</summary>
    private void ModeEvent(string text, Sfx sound)
    {
        Log(text, true);
        Audio.Effect(sound, 0.6f);
        if (!IsServer || Net.IsOffline)
            return;
        _w.Begin(Msg.ModeEvent).U8((byte)sound).Str(text.Length > 100 ? text[..100] : text);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>The mode state (teams, ready flags, gun levels, scores, hill, flags). Sent ~10 times a second.</summary>
    public void WriteModeState(PacketWriter w)
    {
        w.Begin(Msg.ModeState).U32(ServerTick).U8((byte)Mode).U16((ushort)TeamScore[0]).U16((ushort)TeamScore[1]).U8((byte)Players.Count);
        foreach (var p in Players.Values)
            w.U8(p.Id).U8(p.Team).U8((byte)(p.Bot != null || p.Ready ? 1 : 0)).U8(p.GunLevel);
        w.U8((byte)HillIndex).U8(HillOwner).U8(HillContested ? (byte)1 : (byte)0).F32((float)HillMoveIn);
        foreach (var f in Flags)
            w.Vec(f.Position).U8(f.Carrier).U8(f.Home ? (byte)1 : (byte)0).F32((float)f.ReturnIn);
        w.U16((ushort)Math.Min(ScoreLimit, ushort.MaxValue)); // protocol 11: the TDM limit; protocol 21: the current mode's score limit
        WriteTeamPicks(w); // protocol 20: team selection rules and picks (TeamPick.cs)
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
        _mode = (GameMode)r.U8();
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
        if (!r.Has(7))
            return;
        HillIndex = r.U8();
        HillOwner = r.U8();
        HillContested = r.U8() != 0;
        HillMoveIn = r.F32();
        foreach (var f in Flags)
        {
            if (!r.Has(18))
                break;
            f.Position = r.Vec();
            f.Carrier = r.U8();
            f.Home = r.U8() != 0;
            f.ReturnIn = r.F32();
        }
        if (r.Has(2))
            ScoreLimit = r.U16();
        ReadTeamPicks(ref r); // protocol 20 (TeamPick.cs)
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
