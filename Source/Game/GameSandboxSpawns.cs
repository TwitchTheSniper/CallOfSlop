using System.Numerics;

namespace CallOfSlop;

/// <summary>What a creative spawn makes (the inventory's Spawn tab; Msg.CAct Spawn).</summary>
public enum SbSpawnKind : byte
{
    FriendlyBot, // on the players' side: follows the one who spawned it, fights enemy bots and zombies
    EnemyBot, // hostile to players and friendly bots
    Zombies, // a group of zombies: individuals, or the crowd and mass tiers for a big one
}

/// <summary>A bot's gun from a creative spawn or a Bot Spawner: any, a weapon class, or one gun.</summary>
public enum SbLoadKind : byte
{
    Any,
    Class, // the value is a WeaponClass
    Gun, // the value is a weapon id
}

public sealed partial class Zombie
{
    /// <summary>The Sandbox: the Zombie Spawner part that made it (its construct, the part's stable tag, its index when made; 0: none). GameSandboxSpawns.cs.</summary>
    public ushort SbBody, SbTag;

    public byte SbPart;
}

/// <summary>
/// The Sandbox's Spawn category (board items dev-sandbox-spawn-npcs-sn01 and dev-sandbox-spawn-bots-zombies-sb02;
/// docs/sandbox/CONSTRUCTS.md, "Spawning bots and zombies"). Creative worlds only (<see cref="SandboxCreativeSpawns"/>):
/// <list type="bullet">
/// <item>Direct spawns from the inventory's Spawn tab (Msg.CAct Spawn): friendly or enemy bots (difficulty, a gun, how
/// many) and zombie groups (1 to <see cref="SpawnZombieMax"/>), at the aimed spot (refused with a reason in walls, mid-air
/// or on a construct). A spawn is queued and spread over ticks: a bot a tick, a few zombies a tick, and a big group straight
/// into the crowd and mass tiers of the mass horde (<see cref="HordeCrowd"/>) tens of thousands a tick.</item>
/// <item>The Bot Spawner and Zombie Spawner parts (Constructs/Parts/Spawn): settings from the configure tool (what,
/// rate, group, max alive, total, radius, start on) and data ports (inputs enabled, spawn now, rate; outputs alive,
/// spawned) for the linking tool (<see cref="ConstructPartDef.DataIn"/>).</item>
/// <item>Clear all, the host's caps on bots and zombies, and whether everyone may spawn or only the host (the default).</item>
/// </list>
/// Sides: spawned bots carry <see cref="SbFriendlyTeam"/> or <see cref="SbHostileTeam"/> as their team (replicated in the
/// mode state like any team); the Sandbox's <see cref="AreEnemies"/> reads it: people and friendly bots are one side,
/// enemy bots the other, people still free-for-all among themselves. Zombies hunt people and friendly bots
/// (<see cref="ZPrey"/>), on a stand-in Zombies map (<see cref="SandboxZMap"/>: one area, no doors or windows) with the
/// Zombies AI and horde, never the Zombies mode's rounds or points. Spawned bots never respawn: dead, they go.
/// Server-authoritative; the tick allocates nothing.
/// </summary>
public sealed partial class Game
{
    /// <summary>A spawned bot's team: the players' side, and the side against them.</summary>
    public const byte SbFriendlyTeam = 10, SbHostileTeam = 11;

    /// <summary>The most zombies one spawn can ask for (and the host's zombie cap at most).</summary>
    public const int SpawnZombieMax = StressMax;

    /// <summary>The zombie count dialog warns above this ("may be slow on this PC") rather than refusing.</summary>
    public const int SpawnZombieWarn = 100000;

    /// <summary>The count dialog's presets.</summary>
    public static readonly int[] SpawnZombiePresets = [10, 100, 1000, 10000, 100000, 500000];

    /// <summary>A group this size or smaller comes as individuals at the spot (exactly that many); bigger goes into the crowd and mass tiers.</summary>
    public const int SpawnIndividualsMax = 64;

    /// <summary>The most bots the host's cap may be (player ids are 32: the people need room).</summary>
    public const int SpawnBotCapMax = Map.HardMaxPlayers - 4;

    /// <summary>Host setting: the most spawned bots at once (direct and from spawners).</summary>
    public int SandboxBotCap = 16;

    /// <summary>Host setting: the most spawned zombies at once (individuals and the crowd).</summary>
    public int SandboxZombieCap = SpawnZombieMax;

    /// <summary>Host setting: everyone may spawn and clear (off: the host only).</summary>
    public bool SandboxSpawnAnyone;

    /// <summary>A spawn on the far side of this (m) from the player asking is refused.</summary>
    public const float SpawnReach = 150f;

    /// <summary>A spawned bot's body stays this long after it dies, then it leaves (s).</summary>
    public const double SpawnBotGone = 3.0;

    /// <summary>Per tick: bots, individual zombies, and crowd zombies added (a big group spreads over ticks: no hitch).</summary>
    public const int SpawnBotsPerTick = 1, SpawnZombiesPerTick = 8, SpawnCrowdPerTick = 10000;

    /// <summary>The spawners' setting ranges (their part files hold the same).</summary>
    public const float SpawnerRateMax = 600, SpawnerAliveMax = 64, SpawnerGroupMax = 16;

    /// <summary>Is this world creative (infinite parts): the direct spawns are on. Survival worlds' enemies come from the world.</summary>
    public bool SandboxCreativeSpawns => SandboxOn && SandboxInfiniteParts;

    private struct SbJob
    {
        public SbSpawnKind Kind;
        public BotDifficulty Skill;
        public SbLoadKind Load;
        public byte LoadValue, Mix;
        public int Left, Done;
        public Vector3 At;
        public float Radius;
        public byte Owner;
        public ushort Body, Tag;
        public byte Part;
        public bool Crowded; // a big zombie group: the crowd and mass tiers
    }

    private readonly List<SbJob> _sbJobs = new(32);
    private uint _sbSpawnRng = 0x5BA27F01u;
    private ZombiesMap? _sbZMap;
    private Map? _sbZMapFor;
    private bool _sbCrowdReady;
    private int _sbZombieSpawns, _sbBotSpawns;

    /// <summary>Spawned bots and zombies made in all this match (self-test, logs).</summary>
    public int SandboxZombiesSpawned => _sbZombieSpawns;

    public int SandboxBotsSpawned => _sbBotSpawns;

    /// <summary>Spawns still queued (self-test).</summary>
    public int SandboxSpawnsQueued
    {
        get
        {
            var n = 0;
            for (var i = 0; i < _sbJobs.Count; i++)
                n += _sbJobs[i].Left;
            return n;
        }
    }

    /// <summary>
    /// The Sandbox's stand-in Zombies map (<see cref="ZMap"/> in the Sandbox): one area over the whole map, one spawner
    /// (out of the ground), no doors, windows or buyables, and a crowd (so the mass horde's tiers are on). Made once a map.
    /// </summary>
    public ZombiesMap? SandboxZMap
    {
        get
        {
            var m = World.Map;
            if (m.Sandbox == null)
                return null;
            if (!ReferenceEquals(_sbZMapFor, m))
            {
                var zm = new ZombiesMap { Crowd = 1 };
                zm.Areas.Add(new ZArea { Name = "sandbox", X0 = -m.HalfX - 50, Z0 = -m.HalfZ - 50, X1 = m.HalfX + 50, Z1 = m.HalfZ + 50 });
                zm.Spawners.Add(new ZSpawner { Area = 0, At = Vector3.Zero, Window = -1 });
                _sbZMap = zm;
                _sbZMapFor = m;
                _sbCrowdReady = false;
            }
            return _sbZMap;
        }
    }

    /// <summary>Who zombies go for: anyone standing, except (the Sandbox) enemy bots, who are on the zombies' side of nobody.</summary>
    public bool ZPrey(Player p) => IsStanding(p) && !(p.Team == SbHostileTeam && Mode == GameMode.Sandbox);

    /// <summary>The Sandbox's sides (<see cref="AreEnemies"/>): people and friendly bots one side, enemy bots the other; people free-for-all among themselves.</summary>
    private static bool SandboxEnemies(Player a, Player b)
    {
        var fa = a.Team == SbFriendlyTeam;
        var fb = b.Team == SbFriendlyTeam;
        var ha = a.Team == SbHostileTeam;
        var hb = b.Team == SbHostileTeam;
        if (ha || hb)
            return ha != hb; // enemy bots against everyone else, never each other
        if (fa || fb)
            return false; // a friendly bot and a person, or two friendly bots
        return true; // two people: free-for-all as before
    }

    private uint SbNext()
    {
        var x = _sbSpawnRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _sbSpawnRng = x == 0 ? 0x9E3779B9u : x;
    }

    private float SbRand() => (SbNext() >> 8) / 16777216f;

    // ---- Lifecycle ---------------------------------------------------------------------------------------------------

    /// <summary>Server and client, every new match: no spawns queued, no zombies (the bots went with the roster).</summary>
    internal void ResetSandboxSpawns()
    {
        _sbJobs.Clear();
        _sbZombieSpawns = _sbBotSpawns = 0;
        if (SandboxOn || Horde.Count > 0 || Crowd.Total > 0)
        {
            ClearHorde();
            _crowdRound = (_crowdRound + 1) & 0xFFFF;
        }
        _sbCrowdReady = false;
    }

    // ---- The spot -----------------------------------------------------------------------------------------------------

    /// <summary>Where a spawn aimed at <paramref name="at"/> stands (<paramref name="feet"/>), or why it can't: in mid-air, inside a wall, on a construct.</summary>
    public string? SandboxSpawnSpot(Vector3 at, out Vector3 feet)
    {
        feet = at;
        if (!float.IsFinite(at.X) || !float.IsFinite(at.Y) || !float.IsFinite(at.Z))
            return "no such place";
        var up = at + new Vector3(0, 1f, 0);
        var d = World.RaycastWorld(up, -Vector3.UnitY, 3f);
        if (d >= 3f - 1e-3f)
            return "in mid-air: aim at the ground";
        feet = up - new Vector3(0, d, 0);
        if (World.Blocked(feet + new Vector3(0, 0.05f, 0)))
            return "inside a wall: aim at open ground";
        if (Constructs.Raycast(up, -Vector3.UnitY, d + 0.05f, out _))
            return "on a construct: aim at the ground beside it";
        return null;
    }

    // ---- Requests (server) --------------------------------------------------------------------------------------------

    /// <summary>Why <paramref name="p"/> may not spawn or clear, or null (creative worlds; the host, or everyone when the host allows it).</summary>
    public string? SandboxSpawnRefusal(Player p)
    {
        if (!SandboxOn)
            return "spawning is a Sandbox thing";
        if (!SandboxInfiniteParts)
            return "no spawning in a survival world: its enemies come from the world";
        if (p.Id != LocalId && !SandboxSpawnAnyone)
            return "only the host can spawn here (the host can allow everyone in the Spawn tab)";
        return null;
    }

    /// <summary>Spawned bots alive or lying there now, and bots queued.</summary>
    public int SandboxBotCount()
    {
        var n = 0;
        foreach (var p in Players.Values)
            if (p.SbSpawned)
                n++;
        for (var i = 0; i < _sbJobs.Count; i++)
            if (_sbJobs[i].Kind != SbSpawnKind.Zombies)
                n += _sbJobs[i].Left;
        return n;
    }

    /// <summary>Zombies alive now (individuals and the crowd), and zombies queued.</summary>
    public int SandboxZombieCount(bool queued = true)
    {
        var n = Crowd.Total;
        for (var i = 0; i < Horde.Count; i++)
            if (Horde[i].State != ZState.Dead)
                n++;
        if (queued)
            for (var i = 0; i < _sbJobs.Count; i++)
                if (_sbJobs[i].Kind == SbSpawnKind.Zombies)
                    n += _sbJobs[i].Left;
        return n;
    }

    private int FreeBotIds()
    {
        var n = 0;
        for (var id = 1; id < MaxPlayers; id++)
            if (!Players.ContainsKey((byte)id))
                n++;
        return Math.Max(0, n - 1); // (one id kept free for a person joining)
    }

    /// <summary>
    /// Server: a creative spawn from the Spawn tab. Checks the world, the permission, the spot and the caps, then queues it
    /// (spread over ticks). Returns why it was refused, a note when it was cut down to the cap, or null.
    /// </summary>
    public string? SandboxSpawnRequest(Player by, SbSpawnKind kind, BotDifficulty skill, SbLoadKind load, byte loadValue, byte mix, int count, Vector3 at)
    {
        if (SandboxSpawnRefusal(by) is { } no)
            return no;
        if (kind > SbSpawnKind.Zombies || skill > BotDifficulty.Insane || load > SbLoadKind.Gun)
            return "no such spawn";
        if (count < 1)
            return "nothing to spawn";
        if (SandboxSpawnSpot(at, out var feet) is { } why)
            return why;
        if (Vector3.Distance(feet, by.Position) > SpawnReach)
            return $"too far away (at most {SpawnReach:0} m)";
        string? note = null;
        if (kind == SbSpawnKind.Zombies)
        {
            count = Math.Min(count, SpawnZombieMax);
            var room = Math.Min(SandboxZombieCap, SpawnZombieMax) - SandboxZombieCount();
            if (room <= 0)
                return $"the zombie cap is reached ({SandboxZombieCap:N0})";
            if (count > room)
            {
                note = $"only {room:N0} more zombies fit under the cap ({SandboxZombieCap:N0})";
                count = room;
            }
        }
        else
        {
            var room = Math.Min(Math.Clamp(SandboxBotCap, 0, SpawnBotCapMax) - SandboxBotCount(), FreeBotIds());
            if (room <= 0)
                return $"the bot cap is reached ({SandboxBotCap})";
            if (count > room)
            {
                note = $"only {room} more bot{(room == 1 ? "" : "s")} fit";
                count = room;
            }
        }
        _sbJobs.Add(new SbJob
        {
            Kind = kind,
            Skill = skill,
            Load = load,
            LoadValue = loadValue,
            Mix = mix,
            Left = count,
            At = feet,
            Radius = kind == SbSpawnKind.Zombies ? MathF.Max(2f, MathF.Sqrt(count) * 0.45f) : 1.5f + MathF.Sqrt(count),
            Owner = by.Id,
            Crowded = kind == SbSpawnKind.Zombies && count > SpawnIndividualsMax,
        });
        return note;
    }

    /// <summary>Server: every spawned bot and zombie goes, and anything queued.</summary>
    public string? SandboxClearSpawns(Player by)
    {
        if (SandboxSpawnRefusal(by) is { } no)
            return no;
        SandboxClearAll();
        return null;
    }

    /// <summary>Server: every spawned bot and zombie goes (and the queue); the spawners' counts start again.</summary>
    public void SandboxClearAll()
    {
        _sbJobs.Clear();
        var removed = false;
        for (var id = 0; id < 256; id++)
            if (Players.TryGetValue((byte)id, out var p) && p.SbSpawned)
            {
                Players.Remove(p.Id);
                OnModePlayerLeave(p);
                removed = true;
            }
        if (removed)
            BroadcastRoster();
        ClearHorde();
        _crowdRound = (_crowdRound + 1) & 0xFFFF; // (clients get a keyframe of the empty crowd: ZombiesCrowdNet.cs)
        _zAlive = 0;
    }

    // ---- The tick (server) ----------------------------------------------------------------------------------------------

    /// <summary>Server, every Sandbox tick after the constructs: the queue, the spawners, the dead spawned bots, the zombies.</summary>
    internal void SandboxSpawnTick(double now)
    {
        if (!SandboxOn)
            return;
        using var perf = Perf.Measure(PerfArea.Zombies);
        SandboxSpawners(now);
        SandboxRunJobs(now);
        // Spawned bots never respawn: a while after they die, they go.
        var removed = false;
        for (var id = 1; id < MaxPlayers; id++)
        {
            if (!Players.TryGetValue((byte)id, out var p) || !p.SbSpawned)
                continue;
            if (p.Alive)
            {
                p.SbDeadAt = -1;
                continue;
            }
            if (p.SbDeadAt < 0)
                p.SbDeadAt = now;
            else if (now - p.SbDeadAt >= SpawnBotGone)
            {
                Players.Remove(p.Id);
                OnModePlayerLeave(p);
                removed = true;
            }
        }
        if (removed)
            BroadcastRoster();
        // The zombies: the Zombies AI and horde on the stand-in map, then constructs keep them out (and get swiped at).
        if ((Horde.Count > 0 || Crowd.Total > 0) && SandboxZMap is { } zm)
        {
            _zNow = now;
            TickHorde(zm, now); // ZombieAI.cs (the crowd and mass tiers in it: ZombiesCrowd.cs)
            if (Constructs.Bodies.Count > 0)
                SandboxZombiesVsConstructs(now);
            if (!Net.IsOffline)
            {
                SendZEnts(); // ZombiesNet.cs
                ServerSendCrowd(); // ZombiesCrowdNet.cs
            }
        }
    }

    private void SandboxRunJobs(double now)
    {
        if (_sbJobs.Count == 0)
            return;
        var bots = SpawnBotsPerTick;
        var zombies = SpawnZombiesPerTick;
        var crowd = SpawnCrowdPerTick;
        for (var i = 0; i < _sbJobs.Count; i++)
        {
            var j = _sbJobs[i];
            if (j.Kind == SbSpawnKind.Zombies)
            {
                if (j.Crowded)
                {
                    var n = Math.Min(j.Left, crowd);
                    var made = SandboxCrowdAdd(j.At, j.Radius, n);
                    crowd -= n;
                    j.Left -= n; // (what didn't fit is dropped: the particles and the mass were full)
                    j.Done += made;
                    _sbZombieSpawns += made;
                }
                else
                    while (j.Left > 0 && zombies > 0)
                    {
                        zombies--;
                        if (SandboxSpawnZombie(j, now) is { } z)
                        {
                            j.Done++;
                            j.Left--;
                        }
                        else
                        {
                            j.Left = 0; // (the individual tier is full: the rest are dropped)
                            break;
                        }
                    }
            }
            else
                while (j.Left > 0 && bots > 0)
                {
                    bots--;
                    j.Left--;
                    if (SandboxSpawnBot(j) != null)
                        j.Done++;
                }
            _sbJobs[i] = j;
        }
        for (var i = _sbJobs.Count - 1; i >= 0; i--)
            if (_sbJobs[i].Left <= 0)
                _sbJobs.RemoveAt(i);
    }

    /// <summary>A spot near <paramref name="at"/> within <paramref name="radius"/> a character can stand on (not in a wall, not on a construct), or <paramref name="at"/>.</summary>
    private Vector3 SbScatter(Vector3 at, float radius, float min = 0f)
    {
        for (var tries = 0; tries < 6; tries++)
        {
            var a = SbRand() * MathF.Tau;
            var r = min + MathF.Sqrt(SbRand()) * MathF.Max(0, radius - min);
            var p = at + new Vector3(MathF.Sin(a) * r, 0, MathF.Cos(a) * r);
            if (SandboxSpawnSpot(p + new Vector3(0, 0.5f, 0), out var feet) == null && MathF.Abs(feet.Y - at.Y) < 2.5f)
                return feet;
        }
        return at;
    }

    /// <summary>Server: one spawned bot (its side, difficulty and gun), at a spot near the job's, or null when there's no id free.</summary>
    private Player? SandboxSpawnBot(in SbJob j)
    {
        byte id = 0;
        for (var k = 1; k < MaxPlayers; k++)
            if (!Players.ContainsKey((byte)k))
            {
                id = (byte)k;
                break;
            }
        if (id == 0)
            return null;
        var kind = NextPersonality();
        var friendly = j.Kind == SbSpawnKind.FriendlyBot;
        var name = $"[BOT] {BotNames[id % BotNames.Length]} {id} ({(friendly ? "friend" : "enemy")})";
        var bot = new Player
        {
            Id = id,
            Name = name,
            Bot = new BotBrain(j.Skill, kind, _rng.Next()),
            Team = friendly ? SbFriendlyTeam : SbHostileTeam,
            SbSpawned = true,
            SbOwner = j.Body == 0 ? j.Owner : (byte)255,
            SbBody = j.Body,
            SbTag = j.Tag,
            SbPart = j.Part,
        };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        var feet = SbScatter(j.At, j.Radius, j.Body != 0 ? 1.2f : 0f);
        bot.Teleport(feet);
        bot.SbHome = feet;
        bot.Yaw = bot.RenderYaw = SbRand() * MathF.Tau;
        for (var i = 0; i < Player.HistorySize; i++)
            bot.History[i] = new HistoryEntry { Position = feet, Yaw = bot.Yaw };
        SandboxBotGun(bot, j.Load, j.LoadValue);
        _sbBotSpawns++;
        BroadcastRoster();
        return bot;
    }

    /// <summary>A spawned bot's gun: the class or the gun asked for (else what its personality picked).</summary>
    private void SandboxBotGun(Player bot, SbLoadKind load, byte value)
    {
        WeaponDef? gun = null;
        if (load == SbLoadKind.Gun && value < Weapons.All.Length && SpawnGunFits(Weapons.All[value]))
            gun = Weapons.All[value];
        else if (load == SbLoadKind.Class)
        {
            var n = 0;
            foreach (var w in Weapons.All)
                if (SpawnGunFits(w) && (byte)w.Class == value)
                    n++;
            if (n > 0)
            {
                var pick = (int)(SbNext() % (uint)n);
                foreach (var w in Weapons.All)
                    if (SpawnGunFits(w) && (byte)w.Class == value && pick-- == 0)
                    {
                        gun = w;
                        break;
                    }
            }
        }
        if (gun == null)
            return;
        if (gun.Slot == Slot.Primary)
        {
            bot.Primary = bot.WantPrimary = gun.Id;
            bot.PrimaryKit = bot.WantPrimaryKit = BotGear.DefaultKit(gun);
            bot.Weapon = bot.Primary;
        }
        else
        {
            bot.Secondary = bot.WantSecondary = gun.Id;
            bot.SecondaryKit = bot.WantSecondaryKit = BotGear.DefaultKit(gun);
            bot.Weapon = bot.Secondary;
        }
        if (bot.Bot is { } b)
        {
            b.Ammo = gun.Mag;
            b.ReloadUntil = 0;
        }
    }

    /// <summary>A gun the Spawn tab offers bots: a plain gun of the weapon files (not a launcher, not a Zombies build).</summary>
    public static bool SpawnGunFits(WeaponDef w) => !w.IsLauncher && w.BuildText == null && w.Class != WeaponClass.Special;

    /// <summary>Server: one zombie of a job (a walker, or the types mixed in), rising out of the ground near its spot; null when the individual tier is full.</summary>
    private Zombie? SandboxSpawnZombie(in SbJob j, double now)
    {
        if (SandboxZMap is not { } zm)
            return null;
        var alive = 0;
        for (var i = 0; i < Horde.Count; i++)
            if (Horde[i].State != ZState.Dead)
                alive++;
        if (alive >= CrowdIndividualCap)
            return null;
        _zNow = now;
        HordeSetup(zm);
        var at = SbScatter(j.At, j.Radius, j.Body != 0 ? 1.2f : 0f);
        var z = SpawnZombie(zm, 0, now, at, j.Mix == 1 ? PickZombieType(1) : 0);
        z.SbBody = j.Body;
        z.SbTag = j.Tag;
        z.SbPart = j.Part;
        _zAlive++;
        _sbZombieSpawns++;
        return z;
    }

    /// <summary>Server: the crowd tier set up for the Sandbox map (once a map; the first big group does it).</summary>
    private void SandboxCrowdSetup()
    {
        if (_sbCrowdReady && Crowd.Ready)
            return;
        var tuning = ZRules.Crowd;
        Crowd.Tuning = tuning;
        Crowd.Pace = CrowdPace.For(ZRules, tuning, 1);
        Crowd.SetGoal(false);
        Crowd.Hold = false;
        StressState = StressPhase.Off;
        var g = ConstructWorld.Ground(World.Query, 0, 0);
        Crowd.Setup(World.Map, float.IsFinite(g.Y) ? g.Y : 0f, HordeCrowd.ParticleCap);
        Crowd.Clear();
        Crowd.MassHP = CrowdZombieHealth(1);
        _crowdRound = (_crowdRound + 1) & 0xFFFF; // (clients get a keyframe)
        _sbCrowdReady = true;
        Console.WriteLine($"[sandbox] the crowd tier is set up ({Crowd.FlowCells} flow cells, {Crowd.Bytes / 1024} KB)");
    }

    /// <summary>
    /// Server: <paramref name="n"/> zombies of a big group into the crowd round its spot (within <paramref name="radius"/>):
    /// the first few thousand as particles, the rest straight into the mass tier (a count per cell: a big horde costs
    /// cells, not zombies). Returns how many went in (fewer when the particles and the mass are full).
    /// </summary>
    private int SandboxCrowdAdd(Vector3 at, float radius, int n)
    {
        SandboxCrowdSetup();
        if (!Crowd.Ready)
            return 0;
        var made = 0;
        var hp = Crowd.MassHP;
        var mass = Crowd.Tuning.Mass && Crowd.MassCount.Length > 0;
        var particles = Math.Min(4000, Crowd.X.Length);
        var hx = World.Map.HalfX - 1;
        var hz = World.Map.HalfZ - 1;
        for (var k = 0; k < n; k++)
        {
            float x = at.X, z = at.Z;
            for (var tries = 0; tries < 4; tries++)
            {
                var a = SbRand() * MathF.Tau;
                var r = MathF.Sqrt(SbRand()) * radius;
                var tx = Math.Clamp(at.X + MathF.Sin(a) * r, -hx, hx);
                var tz = Math.Clamp(at.Z + MathF.Cos(a) * r, -hz, hz);
                if (!Crowd.Walkable(tx, tz))
                    continue;
                x = tx;
                z = tz;
                break;
            }
            if (Crowd.Count < particles)
            {
                if (Crowd.Add(x, z, 0, 0, SbNext(), ServerTick, hp) >= 0)
                    made++;
            }
            else if (mass)
            {
                Crowd.AddMass(x, z);
                made++;
            }
            else if (Crowd.Add(x, z, 0, 0, SbNext(), ServerTick, hp) >= 0)
                made++;
        }
        return made;
    }

    // ---- The spawner parts (server) -----------------------------------------------------------------------------------

    /// <summary>
    /// Server, every tick: each working Bot Spawner and Zombie Spawner. Enabled (its "enabled" input on, or unwired and
    /// set to start on), it spawns a group (its group size) each 60 / rate seconds (the "rate" input, when wired, instead
    /// of its setting), the first at once, while it has fewer than its max alive out and its total isn't reached; a
    /// rising "spawn now" input spawns a group at once. Its outputs: how many it has alive (the logic reads
    /// <see cref="CPart.SpawnAlive"/>) and a one-tick pulse each time it spawns.
    /// </summary>
    private void SandboxSpawners(double now)
    {
        var bodies = Constructs.Bodies;
        // How many each spawner has alive: zero them, then count every spawned bot and zombie back to its spawner.
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var c = bodies[bi];
            if (!c.HasLogic)
                continue;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].D.IsSpawner)
                    c.Parts[i].SpawnAlive = 0;
        }
        foreach (var p in Players.Values)
            if (p.SbSpawned && p.SbBody != 0 && p.Alive)
                CountBack(p.SbBody, p.SbTag, ref p.SbPart);
        for (var i = 0; i < Horde.Count; i++)
        {
            var z = Horde[i];
            if (z.SbBody != 0 && z.State != ZState.Dead)
                CountBack(z.SbBody, z.SbTag, ref z.SbPart);
        }
        var dt = World.TickDt;
        for (var bi = 0; bi < bodies.Count; bi++)
        {
            var c = bodies[bi];
            if (!c.HasLogic)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                ref var p = ref c.Parts[i];
                var d = p.D;
                if (!d.IsSpawner)
                    continue;
                p.Out1 = 0;
                if (p.Broken)
                {
                    p.State = 0;
                    continue;
                }
                var enabled = ConstructSpawners.EnabledServer(p, d);
                var was = (p.State & 1) != 0;
                var nowPulse = p.InCount1 > 0 && p.In1 >= 0.5f;
                var wasPulse = (p.State & 2) != 0;
                p.State = (byte)((enabled ? 1 : 0) | (nowPulse ? 2 : 0));
                var rate = p.InCount2 > 0 ? Math.Clamp(p.In2, 0, SpawnerRateMax) : ConstructConfig.Get(p, d, CfgKey.Rate, 6);
                var group = Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.Group, 1), 1, (int)SpawnerGroupMax);
                var maxAlive = Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.MaxAlive, 4), 1, (int)SpawnerAliveMax);
                var total = (int)ConstructConfig.Get(p, d, CfgKey.Total, 0);
                var want = 0;
                if (enabled)
                {
                    if (!was)
                        p.SpawnClock = 1; // (switched on: the first group comes at once)
                    else
                        p.SpawnClock = MathF.Min(1, p.SpawnClock + dt * rate / 60f);
                    if (p.SpawnClock >= 1 && rate > 0)
                        want = group;
                }
                if (nowPulse && !wasPulse)
                    want = Math.Max(want, group); // (a "spawn now" pulse: a group at once, enabled or not)
                if (want == 0)
                    continue;
                // The limits: max alive from it, its total, the host's caps.
                var queued = QueuedFor(c.Id, p.Tag);
                want = Math.Min(want, maxAlive - (int)p.SpawnAlive - queued);
                if (total > 0)
                    want = Math.Min(want, total - (int)p.SpawnTotal);
                var bots = d.Logic == CLogic.BotSpawner;
                want = Math.Min(want, bots
                    ? Math.Min(Math.Clamp(SandboxBotCap, 0, SpawnBotCapMax) - SandboxBotCount(), FreeBotIds())
                    : Math.Min(SandboxZombieCap, SpawnZombieMax) - SandboxZombieCount());
                if (want <= 0)
                    continue; // (full: the clock stays ready, so it spawns as soon as there's room)
                c.WorldFrame(p.Seg, out var q, out var t);
                c.PartBounds(i, out var mn, out var mx);
                var centre = t + Vector3.Transform((mn + mx) * 0.5f, q);
                var ground = ConstructWorld.Ground(World.Query, centre.X, centre.Z);
                _sbJobs.Add(new SbJob
                {
                    Kind = bots ? ConstructConfig.Get(p, d, CfgKey.Team, 0) >= 0.5f ? SbSpawnKind.EnemyBot : SbSpawnKind.FriendlyBot : SbSpawnKind.Zombies,
                    Skill = (BotDifficulty)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.Difficulty, 1), 0, (int)BotDifficulty.Insane),
                    Load = ConstructSpawners.LoadOf(ConstructConfig.Get(p, d, CfgKey.Loadout, 0), out var lv),
                    LoadValue = lv,
                    Mix = (byte)Math.Clamp((int)ConstructConfig.Get(p, d, CfgKey.Mix, 0), 0, 1),
                    Left = want,
                    At = new Vector3(centre.X, float.IsFinite(ground.Y) ? ground.Y : centre.Y, centre.Z),
                    Radius = MathF.Max(1.5f, ConstructConfig.Get(p, d, CfgKey.Radius, 4)),
                    Owner = c.Owner,
                    Body = c.Id,
                    Tag = p.Tag,
                    Part = (byte)i,
                });
                p.SpawnTotal += want;
                p.SpawnClock = 0;
                p.Out1 = 1; // (the spawned pulse: one tick)
            }
        }
    }

    /// <summary>Counts one spawned character back to its spawner part (found by its tag; the index it had is tried first).</summary>
    private void CountBack(ushort body, ushort tag, ref byte part)
    {
        if (Constructs.Get(body) is not { } c)
            return;
        if (part >= c.PartCount || c.Parts[part].Tag != tag)
        {
            var found = -1;
            for (var i = 0; i < c.PartCount && found < 0; i++)
                if (c.Parts[i].Tag == tag)
                    found = i;
            if (found < 0)
                return;
            part = (byte)found;
        }
        if (c.Parts[part].D.IsSpawner)
            c.Parts[part].SpawnAlive++;
    }

    private int QueuedFor(ushort body, ushort tag)
    {
        var n = 0;
        for (var i = 0; i < _sbJobs.Count; i++)
            if (_sbJobs[i].Body == body && _sbJobs[i].Tag == tag)
                n += _sbJobs[i].Left;
        return n;
    }

    // ---- Zombies and constructs (server) ------------------------------------------------------------------------------

    /// <summary>
    /// Server: zombies are kept out of constructs as players are (pushed out sideways, or standing on top), and one held
    /// off by a construct swipes the part in its way, at the pace and damage it swipes a building door with
    /// (<see cref="ZombieDoorDamage"/>, Bots/Doors/BotDoors.cs): doors and panels give way.
    /// </summary>
    private void SandboxZombiesVsConstructs(double now)
    {
        for (var i = 0; i < Horde.Count; i++)
        {
            var z = Horde[i];
            if (z.State is ZState.Dead or ZState.Rising)
                continue;
            var before = z.Sim.Position;
            Constructs.BlockPlayer(ref z.Sim, World.PlayerHeight, server: false);
            var push = z.Sim.Position - before;
            push.Y = 0;
            if (push.LengthSquared() < 1e-6f || now < z.NextSwing)
                continue;
            // The part it was pushed off: just the other way.
            var dir = -Vector3.Normalize(push);
            var chest = z.Sim.Position + new Vector3(0, 1.1f, 0);
            if (!Constructs.Raycast(chest, dir, World.PlayerRadius + 0.7f, out var hit))
                continue;
            z.NextSwing = now + ZRules.ZombieWindup + 0.6;
            z.Yaw = MathF.Atan2(dir.X, -dir.Z);
            SandboxShotDamage(hit, ZombieDoorDamage);
            SandboxZombieSwipes++;
        }
    }

    /// <summary>Zombie swipes at constructs this match (self-test).</summary>
    public int SandboxZombieSwipes;

    /// <summary>The Sandbox: a zombie's hit is an ordinary hit (no downs): at no health, an ordinary death (killed by the enemy).</summary>
    private bool SandboxZombieHurt(Player p, int damage, double now, Vector3? from)
    {
        if (!p.Alive || !DamageEnabled || now < p.ImmuneUntil || damage <= 0)
            return false;
        var before = p.Health;
        DamagePlayer(p, p, damage, false, PveKillId, now, DamageKind.Melee, from);
        return p.Health != before || !p.Alive;
    }

    // ---- Bots (server) ------------------------------------------------------------------------------------------------

    /// <summary>
    /// The Sandbox's bot hook (before the normal bot tick): a friendly bot with zombies near fights them as a Zombies co-op
    /// bot does (ZombieBots.cs); otherwise a friendly bot follows whoever spawned it (or holds its spawner's spot), an
    /// enemy one hunts. Returns true when it handled the bot this tick.
    /// </summary>
    internal bool SandboxBotTick(Player p, BotBrain b, double now)
    {
        if (!p.SbSpawned || !p.Alive)
            return false;
        if (p.Team == SbFriendlyTeam && Horde.Count > 0)
        {
            for (var i = 0; i < Horde.Count; i++)
            {
                var z = Horde[i];
                if (z.State != ZState.Dead && Vector3.DistanceSquared(z.Position, p.Position) < 22f * 22f)
                {
                    ZombieBotTick(p, b, now);
                    return true;
                }
            }
        }
        if (p.Team == SbFriendlyTeam && p.SbOwner != 255 && Players.TryGetValue(p.SbOwner, out var lead) && lead.Alive && lead.Bot == null)
        {
            // Beside the one who spawned it (each a little to its own side).
            var a = p.Id * 2.39996f;
            b.ForcedGoal = lead.Position + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * 3f;
        }
        else if (p.SbBody != 0 && p.Team == SbFriendlyTeam)
            b.ForcedGoal = Vector3.DistanceSquared(p.Position, p.SbHome) > 64f ? p.SbHome : null; // (a spawner's: holds near it)
        else
            b.ForcedGoal = null;
        return false;
    }

    /// <summary>
    /// The Sandbox (after a bot steers): constructs are obstacles. A box of a construct in the way of where it walks turns
    /// it along the box's side (towards its goal), and a bot held up against one for a while marks the path edge it was
    /// on as blocked and plans again (A* goes round: the per-bot blocked edge, BotMove.cs).
    /// </summary>
    private void SandboxBotAvoid(Player p, BotBrain b, ref BotIntent steer, double now)
    {
        var wish = new Vector3(steer.Wish.X, 0, steer.Wish.Z);
        var len = wish.Length();
        if (len < 1e-3f)
            return;
        wish /= len;
        var pos = p.Position;
        if (!ConstructSpawners.Obstacle(Constructs, pos, wish, 1.1f, out var c, out var away))
        {
            p.SbBlockedSince = -1;
            return;
        }
        // Along the side: whichever way round turns less from where it is going.
        var left = new Vector3(-wish.Z, 0, wish.X);
        var side = Vector3.Dot(left, away) >= 0 ? left : -left;
        var turned = Vector3.Normalize(side * 0.85f + away * 0.25f + wish * 0.2f);
        if (ConstructSpawners.Obstacle(Constructs, pos, turned, 0.9f, out _, out _))
            turned = Vector3.Normalize(-side * 0.85f + away * 0.25f + wish * 0.2f);
        steer.Wish = turned * len;
        SandboxBotDetours++;
        if (p.SbBlockedSince < 0)
            p.SbBlockedSince = now;
        else if (now - p.SbBlockedSince > 1.5 && b.CurEdge >= 0)
        {
            b.BlockedEdge = b.CurEdge;
            b.BlockedUntil = now + 8;
            b.ClearPath();
            p.SbBlockedSince = now;
            SandboxBotReplans++;
        }
    }

    /// <summary>Bots turned round a construct, and paths planned again round one (self-test).</summary>
    public int SandboxBotDetours, SandboxBotReplans;

    // ---- Actions (client and host) ------------------------------------------------------------------------------------

    /// <summary>Client (or the host): ask for a creative spawn at <paramref name="at"/>.</summary>
    public void SandboxSendSpawn(SbSpawnKind kind, BotDifficulty skill, SbLoadKind load, byte loadValue, byte mix, int count, Vector3 at)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.Spawn).U8((byte)kind).U8((byte)skill).U8((byte)load).U8(loadValue).U8(mix).U32((uint)Math.Clamp(count, 0, SpawnZombieMax)).Vec(at);
        SandboxSendBuilt(w);
    }

    /// <summary>Client (or the host): clear every spawned bot and zombie.</summary>
    public void SandboxSendClearSpawns()
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.ClearSpawns);
        SandboxSendBuilt(w);
    }

    /// <summary>Server: the Spawn and ClearSpawns actions (from SandboxAct).</summary>
    private string? SandboxSpawnAct(Player p, CActKind kind, ref PacketReader r)
    {
        if (kind == CActKind.ClearSpawns)
            return SandboxClearSpawns(p);
        var what = (SbSpawnKind)r.U8();
        var skill = (BotDifficulty)r.U8();
        var load = (SbLoadKind)r.U8();
        var value = r.U8();
        var mix = r.U8();
        var count = (int)Math.Min(r.U32(), (uint)SpawnZombieMax);
        var at = r.Vec();
        return SandboxSpawnRequest(p, what, skill, load, value, mix, count, at);
    }

    // ---- The Spawn tab's choices on this PC (UiSandboxSpawn.cs, Program.SandboxSpawn.cs) ------------------------------

    /// <summary>What the Spawn tab will spawn, and how (this PC's choice).</summary>
    public SbSpawnKind SpawnPick;

    public BotDifficulty SpawnSkill = BotDifficulty.Normal;
    public SbLoadKind SpawnLoad;
    public byte SpawnLoadValue, SpawnMix;
    public int SpawnBots = 1, SpawnZombies = 10;

    /// <summary>Placing a spawn: the next Fire spawns at the aimed spot (Aim or Esc stops).</summary>
    public bool SpawnPlacing;

    /// <summary>The ghost circle: where it would go, whether it can, and why not.</summary>
    public Vector3 SpawnGhost;

    public bool SpawnGhostShown, SpawnGhostOk;
    public string SpawnGhostWhy = "";

    /// <summary>Spawned bots in the game now (either side: their teams are replicated).</summary>
    public int SpawnedBotsNow
    {
        get
        {
            var n = 0;
            foreach (var p in Players.Values)
                if (p.Team is SbFriendlyTeam or SbHostileTeam)
                    n++;
            return n;
        }
    }

    /// <summary>Spawned zombies now (the server's count; a client's: the individuals it hears of and its picture of the crowd).</summary>
    public int SpawnedZombiesNow => IsServer ? SandboxZombieCount(queued: false) : Horde.Count + Math.Max(0, CrowdSeen.Total);

    /// <summary>How many the current pick spawns.</summary>
    public int SpawnCount => SpawnPick == SbSpawnKind.Zombies ? SpawnZombies : SpawnBots;
}

/// <summary>The spawner parts' rules both sides use (GameSandboxSpawns.cs; the renderer's light).</summary>
public static class ConstructSpawners
{
    /// <summary>A Bot Spawner's "loadout" choice: 0 any gun, then the weapon classes (Pistol .. LMG).</summary>
    public static readonly string[] LoadoutNames = ["any", "pistol", "smg", "rifle", "dmr", "sniper", "shotgun", "lmg"];

    public static SbLoadKind LoadOf(float choice, out byte value)
    {
        var k = (int)MathF.Round(choice);
        value = 0;
        if (k <= 0 || k >= LoadoutNames.Length)
            return SbLoadKind.Any;
        value = (byte)(k - 1); // (the order of WeaponClass: Pistol, SMG, AssaultRifle, DMR, Sniper, Shotgun, LMG)
        return SbLoadKind.Class;
    }

    /// <summary>Server: is a spawner enabled (its "enabled" input on; unwired, its start-on setting)?</summary>
    public static bool EnabledServer(in CPart p, ConstructPartDef d) => p.InCount > 0 ? p.In >= 0.5f : ConstructConfig.Get(p, d, CfgKey.StartOn, 1) >= 0.5f;

    /// <summary>Either side (the light): enabled from the signals of what is wired into its "enabled" input, as this side knows them.</summary>
    public static bool Active(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (p.Broken)
            return false;
        var wired = false;
        for (var k = 0; k < c.WireCount; k++)
        {
            ref var w = ref c.Wires[k];
            if (w.To != i || w.ToPort != 0 || w.From >= c.PartCount)
                continue;
            wired = true;
            ref var src = ref c.Parts[w.From];
            if (!src.Broken && (w.FromPort == 1 ? src.Out1 : src.Out) >= 0.5f)
                return true;
        }
        return !wired && c.XWireIn == 0 ? ConstructConfig.Get(p, p.D, CfgKey.StartOn, 1) >= 0.5f : p.InCount > 0 && p.In >= 0.5f;
    }

    private static readonly List<Construct> _obstacleNear = new(256);

    /// <summary>
    /// A construct part in the way: a point <paramref name="ahead"/> m along <paramref name="dir"/> from
    /// <paramref name="feet"/> (and half way) inside one of a construct's solid part boxes (in the part's frame, a player's
    /// radius round it, from a step above the feet to head height; so a floor a bot can step onto, or a doorway between
    /// walls, is not in the way). <paramref name="away"/>: flat, from the box towards the feet.
    /// </summary>
    public static bool Obstacle(ConstructWorld w, Vector3 feet, Vector3 dir, float ahead, out Construct? hit, out Vector3 away)
    {
        hit = null;
        away = Vector3.Zero;
        var r = World.PlayerRadius + 0.1f;
        var near = w.Near(feet, ahead + 1f, _obstacleNear); // (the broadphase: the bodies round the bot)
        for (var bi = 0; bi < near.Count; bi++)
        {
            var c = near[bi];
            var reach = c.Radius + ahead + 1f;
            if (Vector3.DistanceSquared(feet, c.WorldCom) > reach * reach)
                continue;
            for (var i = 0; i < c.PartCount; i++)
            {
                c.WorldFrame(c.Parts[i].Seg, out var q, out var t);
                var iq = Quaternion.Conjugate(q);
                var boxes = c.Parts[i].D.SolidBoxes;
                for (var k = 0; k < boxes; k++)
                {
                    c.BoxOf(i, k, out var centre, out var half);
                    var grow = half + new Vector3(r, 0, r);
                    for (var s = 1; s <= 2; s++)
                    {
                        var probe = feet + dir * (ahead * s * 0.5f);
                        var l0 = Vector3.Transform(probe + new Vector3(0, World.StepHeight + 0.05f, 0) - t, iq) - centre;
                        var l1 = Vector3.Transform(probe + new Vector3(0, 1.6f, 0) - t, iq) - centre;
                        var lmin = Vector3.Min(l0, l1);
                        var lmax = Vector3.Max(l0, l1);
                        if (lmax.X < -grow.X || lmin.X > grow.X || lmax.Y < -grow.Y || lmin.Y > grow.Y || lmax.Z < -grow.Z || lmin.Z > grow.Z)
                            continue;
                        var a = feet - (t + Vector3.Transform(centre, q));
                        a.Y = 0;
                        away = a.LengthSquared() > 1e-6f ? Vector3.Normalize(a) : -dir;
                        hit = c;
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
