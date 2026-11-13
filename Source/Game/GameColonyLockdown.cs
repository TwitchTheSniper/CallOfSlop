using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The penal colony's lockdown (docs/story/OPENING.md "Lockdown"; Karl, 2026-11-08): a person who hurts the colony's
/// staff (the Penal Colony cast: the warden and his guards) ends their own induction, the colony's alarm sounds, and the
/// Colony Barracks beside it turns out a squad of riot officers who hunt them down. Each person who hurts the staff while
/// it runs is wanted too; the squad fights only the wanted. A wanted person stops being wanted when they die or leave
/// (the world's death rules decide what comes next). The lockdown stands down when nobody is wanted, the squad is all
/// dead, or after <see cref="LockdownMax"/>: the alarm stops and the officers march back into the barracks and are
/// gone. The Ministry's standing falls through its own deeds (Story/Factions/Ministry.json: "Assaulted our staff",
/// "Killed our staff"). Host only, apart from the alarm, which every client hears (<see cref="Msg.ColonyAlarm"/>).
/// Nothing allocates in a steady tick.
/// </summary>
public sealed partial class Game
{
    public const int LockdownSquad = 6; // riot officers turned out
    public const double LockdownMax = 600; // s: the longest a lockdown runs
    public const double LockdownTurnOut = 0.5; // s between officers coming out
    public const double LockdownMarchBack = 60; // s: standing down, an officer who hasn't got back by then is gone anyway
    public const double LockdownAlarmEvery = 2.2; // s between siren wails (the wail is 2 s)
    public const double LockdownResend = 3; // s: the alarm's state again, for anyone who joined

    private static readonly string[] SquadNames =
        ["Riot Officer Grubb", "Riot Officer Plimsoll", "Riot Officer Hatch", "Riot Officer Dobbs", "Riot Officer Mallory", "Riot Officer Fenwick"];

    private ushort _opBarracks; // the Colony Barracks construct (0: none: the squad turns out at the gate)
    private Vector3 _ldDoor; // where the squad turns out and marches back to
    private bool _ldOn, _ldStandDown;
    private double _ldSince, _ldStandDownAt, _ldNextOut, _ldSentAt;
    private int _ldToTurnOut, _ldTurnedOut;
    private readonly Player?[] _ldWanted = new Player?[256]; // by player id: the person wanted (the Player object: a new person in the slot isn't)
    private readonly Player?[] _ldSquad = new Player?[256]; // by player id: a squad officer
    private PacketWriter? _ldW;

    // ---- Every peer: the alarm
    /// <summary>The colony's alarm is sounding (every peer).</summary>
    public bool ColonyAlarmOn { get; private set; }

    public Vector3 ColonyAlarmAt { get; private set; }
    private double _ldWailAt;

    /// <summary>Both sides, a new match (from <see cref="OpeningReset"/>): no lockdown, no barracks until one is built or loaded.</summary>
    private void LockdownReset()
    {
        _opBarracks = 0;
        _ldOn = _ldStandDown = false;
        _ldToTurnOut = _ldTurnedOut = 0;
        Array.Clear(_ldWanted);
        Array.Clear(_ldSquad);
        ColonyAlarmOn = false;
    }

    /// <summary>Is this person wanted by the lockdown now (server)?</summary>
    public bool LockdownWanted(Player p) => _ldOn && !_ldStandDown && _ldWanted[p.Id] == p;

    /// <summary>Is this bot one of the lockdown's squad (server)?</summary>
    public bool LockdownSquadOf(Player p) => _ldSquad[p.Id] == p;

    // ---- The barracks -----------------------------------------------------------------------------------------------

    /// <summary>Server, a fresh survival world (from <see cref="OpeningBuild"/>, after the colony): the barracks beside it.</summary>
    private void LockdownBuildBarracks()
    {
        if (_opColony == 0)
        {
            _ldDoor = _opGate;
            return;
        }
        if ((ColonySiteStand("Colony Barracks") ?? SandboxKitPiece("Colony Barracks", _opMiddle, 16, 60, level: 1f)) is { } b) // (its spot in the site plan, or level ground near)
        {
            _opBarracks = b.Id;
            Constructs.Events.Add($"opening: the Colony Barracks as construct {b.Id}");
        }
        else
            Log("[lockdown] no level ground for the Colony Barracks: the squad turns out at the colony's gate");
        LockdownDoor();
    }

    /// <summary>Server, a loaded world's opening: its barracks, or (a world from before them) a new one beside the colony.</summary>
    private void LockdownLoadBarracks(ushort id)
    {
        _opBarracks = Constructs.Get(id) != null ? id : (ushort)0;
        if (_opBarracks == 0 && _opColony != 0)
        {
            LockdownBuildBarracks(); // (a world from before the barracks: the colony gets one)
            return;
        }
        LockdownDoor();
    }

    /// <summary>Where the squad turns out: just outside the barracks on the colony's side, or the colony's gate.</summary>
    private void LockdownDoor()
    {
        _ldDoor = _opGate;
        if (Constructs.Get(_opBarracks) is not { } b)
            return;
        var mid = b.ToWorld((b.LocalMin + b.LocalMax) * 0.5f);
        var half = new Vector2(b.LocalMax.X - b.LocalMin.X, b.LocalMax.Z - b.LocalMin.Z).Length() * 0.5f;
        var to = new Vector3(_opMiddle.X - mid.X, 0, _opMiddle.Z - mid.Z);
        to = to.LengthSquared() > 0.01f ? Vector3.Normalize(to) : Vector3.UnitZ;
        var at = mid + to * (half + 2f);
        if (World.Map.Terrain != null)
            at.Y = World.Map.GroundHeight(at.X, at.Z);
        _ldDoor = SandboxSpawnSpot(at + new Vector3(0, 1f, 0), out var feet) == null ? feet : _opGate;
    }

    // ---- An assault -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server, from <see cref="DamagePlayer"/>: a story person's body was hurt (or killed) by <paramref name="by"/>. When
    /// the body is one of the colony's staff and the attacker a person: their induction ends, they are wanted, and the
    /// lockdown starts (or carries on).
    /// </summary>
    private void LockdownAssault(Player by, Player victim, double now)
    {
        if (!IsServer || !_opOn || by.Bot != null || !by.Alive || by == victim || by.Id >= MaxPlayers || !LockdownStaff(victim.Name))
            return;
        if (_tutWho[by.Id] == by && _tutState[by.Id] == TutorialState.Running)
            TutorialHear(by.Id, TutorialOp.Skip, 0); // (the induction is over: GameTutorial.cs gives back what it held)
        var wanted = _ldWanted[by.Id] == by && _ldOn && !_ldStandDown;
        _ldWanted[by.Id] = by;
        if (wanted)
            return;
        var warden = OpeningWarden();
        var script = OpScript;
        OpeningSay(by, warden is { Alive: true } ? warden : null, script.Warden.Length > 0 ? script.Warden : "The Warden",
            warden == victim ? "Assaulting the warden! Induction terminated. Guards! GUARDS!" : "Assault on a Ministry officer! Induction terminated. Sound the alarm!", now);
        Log($"[lockdown] {by.Name} hurt {victim.Name}: wanted");
        LockdownGo($"[MINISTRY MEMO] LOCKDOWN at the Penal Colony: {by.Name} assaulted {victim.Name}. The barracks is turning out.", now);
    }

    /// <summary>
    /// Server: a storylet's "lockdown" hook (GameStoryStorylets.cs): <paramref name="p"/> is wanted and the lockdown
    /// starts (or carries on), as for an assault, with <paramref name="memo"/> for everyone.
    /// </summary>
    private void LockdownStart(Player p, string memo, double now)
    {
        if (!IsServer || !_opOn || p.Bot != null || !p.Alive || p.Id >= MaxPlayers)
            return;
        var wanted = _ldWanted[p.Id] == p && _ldOn && !_ldStandDown;
        _ldWanted[p.Id] = p;
        if (!wanted)
            LockdownGo(memo, now);
    }

    private void LockdownGo(string memo, double now)
    {
        ServerSystemChat(memo, now);
        if (!_ldOn)
        {
            _ldOn = true;
            _ldSince = now;
            _ldTurnedOut = 0;
            _ldToTurnOut = LockdownSquad;
            _ldNextOut = now + 1.5; // (the alarm first, then the doors)
        }
        else if (_ldStandDown)
        {
            // Standing down already: back to it, and the barracks makes up the squad.
            _ldStandDown = false;
            _ldSince = now;
            _ldToTurnOut = Math.Max(0, LockdownSquad - LockdownSquadAlive());
            _ldNextOut = now + 1.5;
        }
        LockdownAlarm(true);
    }

    /// <summary>Is this name one of the colony's staff (the opening's cast)?</summary>
    private static bool LockdownStaff(string name)
    {
        if (StoryData.People.Cast(OpeningScript.Current.Cast) is not { } cast)
            return false;
        foreach (var p in cast.People)
            if (p.Name == name)
                return true;
        return false;
    }

    private int LockdownSquadAlive()
    {
        var n = 0;
        for (var id = 1; id < MaxPlayers; id++)
            if (_ldSquad[id] is { Alive: true } b && Players.TryGetValue((byte)id, out var p) && p == b)
                n++;
        return n;
    }

    // ---- Each tick ----------------------------------------------------------------------------------------------------

    /// <summary>Server, each Sandbox tick: officers turn out, the wanted are checked, and the lockdown stands down when it's over.</summary>
    internal void LockdownTick(double now)
    {
        if (!_ldOn)
            return;
        if (_ldToTurnOut > 0 && now >= _ldNextOut)
        {
            _ldNextOut = now + LockdownTurnOut;
            if (LockdownTurnOutOne())
                _ldToTurnOut--;
            else
                _ldToTurnOut = 0; // (no ids free: the squad is what it is)
        }
        var anyWanted = false;
        for (var id = 0; id < MaxPlayers; id++)
        {
            if (_ldWanted[id] is not { } w)
                continue;
            if (!Players.TryGetValue((byte)id, out var p) || p != w || !p.Alive)
            {
                _ldWanted[id] = null;
                Log($"[lockdown] {w.Name} is {(p == w ? "dead" : "gone")}: no longer wanted");
                continue;
            }
            anyWanted = true;
        }
        var squad = 0;
        var removed = false;
        for (var id = 1; id < MaxPlayers; id++)
        {
            if (_ldSquad[id] is not { } b)
                continue;
            if (!Players.TryGetValue((byte)id, out var p) || p != b)
            {
                _ldSquad[id] = null; // (killed and taken away: GameSandboxSpawns.cs)
                continue;
            }
            if (!b.Alive)
                continue;
            squad++;
            if (_ldStandDown && (Flat2(b.Position - _ldDoor) < 2.5f * 2.5f || now - _ldStandDownAt > LockdownMarchBack))
            {
                Players.Remove(b.Id); // (back in the barracks)
                OnModePlayerLeave(b);
                _ldSquad[id] = null;
                removed = true;
                squad--;
            }
        }
        if (removed)
            BroadcastRoster();
        if (!_ldStandDown && (!anyWanted || now - _ldSince > LockdownMax || squad == 0 && _ldToTurnOut == 0 && _ldTurnedOut > 0))
        {
            _ldStandDown = true;
            _ldStandDownAt = now;
            _ldToTurnOut = 0;
            Array.Clear(_ldWanted);
            LockdownAlarm(false);
            var beaten = anyWanted && squad == 0;
            ServerSystemChat(beaten ? "[MINISTRY MEMO] The Penal Colony's riot squad is out of action. Lockdown lifted pending a review."
                : "[MINISTRY MEMO] Lockdown at the Penal Colony lifted. Officers, back to barracks.", now);
            Log($"[lockdown] stands down ({(beaten ? "no squad left" : anyWanted ? "too long" : "nobody wanted")})");
        }
        if (_ldStandDown && squad == 0 && _ldToTurnOut == 0)
            _ldOn = false;
        if (ColonyAlarmOn && now - _ldSentAt >= LockdownResend)
            LockdownAlarm(true, resend: true);
    }

    /// <summary>One riot officer out of the barracks door, with a rifle or an SMG. False: no player id free.</summary>
    private bool LockdownTurnOutOne()
    {
        var id = FreeNpcId();
        if (id == 0)
            return false;
        var n = _ldTurnedOut++;
        var bot = new Player
        {
            Id = id,
            Name = SquadNames[n % SquadNames.Length],
            Bot = new BotBrain(BotDifficulty.Hard, n % 2 == 0 ? BotPersonality.Rusher : BotPersonality.Tactician, _rng.Next()),
            Team = SbHostileTeam, // (but it fights only the wanted: SandboxEnemies)
            SbSpawned = true, // (never respawns: killed, it goes)
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        var feet = SbScatter(_ldDoor, 2f);
        bot.Teleport(feet);
        bot.SbHome = feet;
        var d = _opMiddle - feet;
        bot.Yaw = bot.RenderYaw = MathF.Atan2(d.X, -d.Z);
        for (var i = 0; i < Player.HistorySize; i++)
            bot.History[i] = new HistoryEntry { Position = feet, Yaw = bot.Yaw };
        SandboxBotGun(bot, SbLoadKind.Class, (byte)(n % 2 == 0 ? WeaponClass.SMG : WeaponClass.AssaultRifle));
        _ldSquad[id] = bot;
        BroadcastRoster();
        return true;
    }

    /// <summary>
    /// Server, before the bot tick (GameSandboxSpawns.cs): a squad officer heads for the nearest wanted person (the bot's own
    /// fighting does the rest when it sees them), or, standing down, marches back to the barracks. False: not one of the squad.
    /// </summary>
    internal bool LockdownSquadBot(Player p, BotBrain b, double now)
    {
        if (_ldSquad[p.Id] != p)
            return false;
        b.HoldUntil = 0;
        if (_ldStandDown)
        {
            b.Target = byte.MaxValue;
            b.HeardTime = b.LastSeenTime = -100;
            b.ForcedGoal = _ldDoor;
            return true;
        }
        Player? best = null;
        var bestD = float.MaxValue;
        for (var id = 0; id < MaxPlayers; id++)
            if (_ldWanted[id] is { Alive: true } w)
            {
                var d2 = Vector3.DistanceSquared(w.Position, p.Position);
                if (d2 < bestD)
                    (best, bestD) = (w, d2);
            }
        b.ForcedGoal = best?.Position ?? _ldDoor;
        return true;
    }

    // ---- The alarm ----------------------------------------------------------------------------------------------------

    /// <summary>Server: the alarm on or off, here and to every client (again now and then while on, for anyone who joined).</summary>
    private void LockdownAlarm(bool on, bool resend = false)
    {
        _ldSentAt = SandboxNow;
        var at = _opColony != 0 ? _opMiddle : _ldDoor;
        if (!resend)
            ColonyAlarmHeard(on, at);
        if (Net.IsOffline)
            return;
        var w = _ldW ??= new PacketWriter(24);
        w.Begin(Msg.ColonyAlarm).U8(on ? (byte)1 : (byte)0).Vec(at);
        Net.Broadcast(w.Span, resend ? PacketReliability.Unreliable : PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: a <see cref="Msg.ColonyAlarm"/> (after its id).</summary>
    private void ReadColonyAlarm(ref PacketReader r)
    {
        if (!r.Has(13))
            return;
        var on = r.U8() != 0;
        ColonyAlarmHeard(on, r.Vec());
    }

    private void ColonyAlarmHeard(bool on, Vector3 at)
    {
        if (on && !ColonyAlarmOn)
            _ldWailAt = -1;
        ColonyAlarmOn = on;
        ColonyAlarmAt = at;
    }

    /// <summary>Every frame (host and clients): the siren wails while the alarm is on.</summary>
    internal void ColonyAlarmSounds(double now)
    {
        if (!ColonyAlarmOn || !SandboxOn || now < _ldWailAt + LockdownAlarmEvery)
            return;
        _ldWailAt = now;
        Audio.Effect3D(Sfx.ColonyAlarm, ColonyAlarmAt + new Vector3(0, 4, 0), 1f);
    }
}
