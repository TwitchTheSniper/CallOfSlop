using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>What a bot is busy with right now (decided each tick, see Game.BotDecide).</summary>
public enum BotMode : byte
{
    Roam, // heading for a goal that depends on its personality (hunt, flank, perch, anchor...)
    Engage, // an enemy is in sight
    Cover, // breaking line of sight to reload or because it's hurt
    Investigate, // checking a last-seen spot or gunfire it heard
    Objective, // the game mode's objective
    Squad, // Insane: following its squad's order (Source/Game/Bots/Squads)
}

/// <summary>Per-bot AI state. Bots live only on the server and play through the same input commands as people.</summary>
public sealed partial class BotBrain
{
    public readonly BotDifficulty Skill;
    public readonly BotPersonality Personality;
    public readonly BotProfile Profile;
    public readonly int Seed; // per-bot randomness for route choices

    public BotBrain(BotDifficulty skill, BotPersonality personality = BotPersonality.Tactician, int seed = 0)
    {
        Skill = skill;
        Personality = personality;
        Profile = BotPersonalities.Get(personality);
        Seed = seed;
        EdgePenaltyFn = EdgePenalty;
    }

    /// <summary>A Workbench Range practice dummy: stands at <see cref="DummyHome"/>, never moves or shoots (GameZombies.cs).</summary>
    public bool Dummy;

    public Vector3 DummyHome;
    public bool DummyPlaced;

    // ---- Perception ----
    public byte Target = byte.MaxValue;
    public double TargetSince;
    public double NextScan;
    public Vector3 LastSeenAt;
    public double LastSeenTime = -100;
    public Vector3 HeardAt;
    public double HeardTime = -100;
    public int LastHealth = World.MaxHealth;
    public double HurtTime = -100;

    // ---- Aim and weapon ----
    public float AimErrYaw, AimErrPitch;
    public int Ammo;
    public double ReloadUntil;
    public double NextFire;
    public int BurstLeft;
    public bool CoverReload; // this reload should happen behind cover
    public byte MeleeTarget = byte.MaxValue; // who we've been within melee reach of (Melee.cs: BotTryMelee)
    public double MeleeCloseSince; // ...since then (the wind-up before a swing)
    public double MeleeCheckedAt = -100;

    // ---- Decisions ----
    public BotMode Mode;
    public Vector3 Waypoint; // the current roaming goal (Zero = pick a new one; Match/MapVote reset it on a new map)
    public double GoalUntil; // re-pick the roaming goal after this
    public Vector3 InvestigateAt;
    public Vector3 CoverSpot;
    public double CoverUntil;
    public double NextCoverSearch;
    public double HoldUntil; // marksmen and anchors stay put at their spot until then
    public Vector3 HoldAnchor; // the spot being held (they shuffle around it)
    public double ShuffleAt;
    public bool ObjectiveMinded; // rolled each life from the personality's objective weight
    public Vector3? Objective;
    public double NextObjectiveCheck;
    public Vector3? ForcedGoal; // self-test: roam to exactly here

    // ---- Movement ----
    public float StrafeDir = 1;
    public double StrafeUntil;
    public Vector3 StuckCheckPos;
    public double StuckCheckTime;
    public int StuckCount;
    public int StuckCheckNode = -1; // the path node being walked to at the last stuck check (-1: none)
    public float StuckCheckAim; // flat distance to it then
    public Vector3 FollowAim; // where path following last steered (BotFollow), the node it heads for and when
    public int FollowNode = -1;
    public double FollowTime = -100;
    public int SlopeStalls; // times this bot made no progress up a slope while moving (self-test statistics)
    public bool JumpNext;
    public double CrouchUntil;
    public double NextGlance, GlanceUntil;
    public float GlanceYaw;
    public double NextCoverCrouchCheck;
    public bool CrouchCover;
    public uint Seq;
    public uint LifeTick = uint.MaxValue; // SpawnTick of the life the per-life state belongs to

    // ---- Path ----
    public readonly int[] Path = new int[128];
    public int PathLen, PathIdx;
    public int CurEdge = -1; // edge being walked (towards Path[PathIdx]), -1 when heading for the first node
    public Vector3 PathGoal;
    public bool NeedPath = true;
    public double NextPathTime;
    public double NextShortcut;
    public NavGraph? PathGraph;
    public int BlockedEdge = -1;
    public double BlockedUntil;

    // Inputs to EdgePenalty for the search in progress (set just before each search).
    internal NavGraph? SearchGraph;
    internal double SearchNow;
    internal Vector3 FlankFrom, FlankTo;
    internal float FlankWidth;
    internal float[]? Hazard; // extra cost per nav node (landmines nearby; Game.MineHazard), or null
    internal readonly Func<int, int, int, float> EdgePenaltyFn;

    /// <summary>
    /// Extra cost per edge for this bot's searches: avoids an edge it just got stuck on, adds a little per-bot noise
    /// so bots spread over different routes, and makes flankers avoid the straight line to their target.
    /// </summary>
    private float EdgePenalty(int from, int to, int edge)
    {
        if (edge == BlockedEdge && SearchNow < BlockedUntil)
            return float.PositiveInfinity;
        var g = SearchGraph!;
        var cost = g.EdgeCost[edge];
        var h = (uint)edge * 2654435761u ^ (uint)Seed;
        h ^= h >> 15;
        h *= 0x2c1b3c6du;
        h ^= h >> 12;
        var extra = cost * 0.25f * (h & 1023) / 1023f;
        if (FlankWidth > 0)
        {
            // Distance of the edge's end from the direct line: near it costs more, so the route bends out to the side.
            var p = new Vector2(g.Nodes[to].X, g.Nodes[to].Z);
            var a = new Vector2(FlankFrom.X, FlankFrom.Z);
            var ab = new Vector2(FlankTo.X, FlankTo.Z) - a;
            var t = Math.Clamp(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1f), 0f, 1f);
            var d = Vector2.Distance(p, a + ab * t);
            if (d < FlankWidth && t is > 0.15f and < 0.9f)
                extra += cost * 2.5f * (1f - d / FlankWidth);
        }
        if (Squad != null)
            extra += SquadEdgePenalty(to, cost); // Insane squads: out of the target's sight, off squadmates' routes
        if (Hazard is { } hz && (uint)to < (uint)hz.Length)
            extra += hz[to]; // a landmine next to the node: go round unless there's no other way
        return extra;
    }

    public void ClearPath()
    {
        PathLen = 0;
        PathIdx = 0;
        CurEdge = -1;
        NeedPath = true;
    }

    // Tuning per difficulty
    public double Reaction => Skill switch { BotDifficulty.Easy => 0.65, BotDifficulty.Normal => 0.38, BotDifficulty.Insane => 0.13, _ => 0.2 };
    public float AimError => Skill switch { BotDifficulty.Easy => 0.14f, BotDifficulty.Normal => 0.075f, BotDifficulty.Insane => 0.02f, _ => 0.03f }; // radians
    public float TurnSpeed => Skill switch { BotDifficulty.Easy => 3.5f, BotDifficulty.Normal => 6f, BotDifficulty.Insane => 13f, _ => 10f }; // rad/s
    public float Settle => Skill switch { BotDifficulty.Easy => 1.2f, BotDifficulty.Normal => 2.4f, BotDifficulty.Insane => 6f, _ => 4.5f }; // aim error decay /s
    public double TriggerDelay => Skill switch { BotDifficulty.Easy => 0.25, BotDifficulty.Normal => 0.12, BotDifficulty.Insane => 0.035, _ => 0.05 }; // extra delay on semi-autos
}

/// <summary>
/// Bots: the coordinator. Each tick a bot perceives (BotCombat.cs), decides what it's doing (BotTactics.cs),
/// steers along its path (BotMove.cs, over the map's NavGraph in BotNav.cs), aims and shoots (BotCombat.cs), and
/// sends the result as an ordinary InputCmd through ApplyCmd, exactly like a person. Personalities and loadouts
/// are in BotPersonality.cs; teams and objectives come from IsEnemy / ObjectiveGoal (BotObjectives).
/// </summary>
public sealed partial class Game
{
    private static readonly string[] BotNames =
        ["Ace", "Brick", "Cinder", "Dozer", "Echo", "Fang", "Grit", "Havoc", "Ion", "Jinx", "Knox", "Lynx", "Moss", "Nova"];

    /// <summary>Adds a bot (server only). Returns false if the server is full. Without a personality, the lobby gets a mix.</summary>
    public bool AddBot(BotDifficulty skill, BotPersonality? personality = null, bool quiet = false)
    {
        if (!IsServer || FreeId() is not { } id)
            return false;
        var kind = personality ?? NextPersonality();
        var used = Players.Values.Select(p => p.Name).ToList();
        var baseName = BotNames.Select(n => $"[BOT] {n}").FirstOrDefault(n => !used.Any(u => u == n || u.StartsWith(n + " ", StringComparison.Ordinal)))
                       ?? $"[BOT] {id}";
        var name = $"{baseName} ({BotPersonalities.Get(kind).Name})";
        var bot = new Player { Id = id, Name = name, Bot = new BotBrain(skill, kind, _rng.Next()) };
        Players[id] = bot;
        Spawn(bot, ServerTick, Brutal.GlfwApi.Glfw.Time);
        if (quiet)
            Console.WriteLine($"{name} joined ({skill})");
        else
            LogJoin(name, $" ({skill})");
        BroadcastRoster();
        return true;
    }

    private bool KickOneBot()
    {
        // Team modes: a bot from the bigger side, so the newcomer (who gets the smaller side) takes its place.
        var bot = Players.Values.Where(p => p.Bot != null).OrderByDescending(p => IsTeamMode && p.Team < 2 ? TeamCount(p.Team) : 0).FirstOrDefault();
        if (bot == null)
            return false;
        Players.Remove(bot.Id);
        OnModePlayerLeave(bot);
        Log($"{bot.Name} left to make room");
        return true;
    }

    private void PickBotLoadout(Player bot) => PickBotLoadout(bot, bot.Bot?.Profile ?? BotPersonalities.Get(BotPersonality.Chaotic));

    /// <summary>
    /// Bots run a couple of random attachments too (no optics: they don't need them), only ones their weapon XP at the
    /// gear level has unlocked (BotGear.cs; the draws are the same at every level, a locked one is just left off).
    /// </summary>
    private uint RandomKit(WeaponDef w, int level)
    {
        uint kit = 0;
        var list = Attachments.For(w);
        for (var i = 0; i < list.Count; i++) // indexed: no enumerator to allocate every life
        {
            var a = list[i];
            if (a.Slot != AttachSlot.Optic && Attachments.Slot(kit, a.Slot) == 0 && _rng.NextDouble() < 0.25 && BotGear.Attachment(w, a, level))
                kit = Attachments.With(kit, a.Slot, a.Index);
        }
        return kit;
    }

    private static float PreferredRange(WeaponDef w) =>
        w.Class switch
        {
            WeaponClass.Shotgun => w.Pellets > 1 ? 4f : 14f,
            WeaponClass.SMG or WeaponClass.Pistol => 9f,
            WeaponClass.Sniper => 26f,
            WeaponClass.DMR => 20f,
            _ => 14f,
        };

    /// <summary>Can a bot see from here to there? Not through walls, smoke, foliage or a cornfield (BotFoliage.cs: SightLine).</summary>
    private bool HasLineOfSight(Vector3 from, Vector3 to) => SightLine(from, to) == BotSightLine.Clear;

    /// <summary>One simulation tick of bot thinking: perceive, decide, steer, aim, shoot, move.</summary>
    private void BotTick(Player p, double now)
    {
        var b = p.Bot!;
        if (BotHeld(p, b, now))
            return; // Workbench Range practice dummies and stunned bots (GameZombies.cs)
        if (Module.BotTick(this, p, b, now))
            return; // the mode runs its own bots (Zombies: co-op bots fight zombies, ZombieBots.cs)
        if (!p.Alive)
        {
            b.Target = byte.MaxValue;
            return;
        }

        BotUseStreaks(p, now); // kill streaks (BotStreaks.cs)
        var def = p.WeaponDef;
        if (b.ReloadUntil > 0 && now >= b.ReloadUntil)
        {
            b.Ammo = def.Mag;
            b.ReloadUntil = 0;
            b.CoverReload = false;
        }
        BotTickShared(now);
        var nav = NavGraph.Current;
        if (b.PathGraph != nav)
        {
            b.PathGraph = nav;
            b.ClearPath();
            b.Waypoint = Vector3.Zero;
        }

        var target = BotPerceive(p, b, now, out var visible);
        SquadPerceive(p, b, target, visible, now); // Insane squads: report to the blackboard (Source/Game/Bots/Squads)
        BotDecide(p, b, target, visible, def, now);
        var steer = BotSteer(p, b, target, visible, def, now);
        if (BotStuckWatch(p, b, ref steer, visible, now))
            return; // stuck for too long: it killed itself, like the pause menu's "Kill yourself" (BotStuck.cs)
        BotOpenDoors(p, now); // walking into a shut building door opens it (GameBuildings.cs)
        BotBlindPlan(p, b, visible, def, now); // fire blind into foliage at an estimate? (BotFoliage.cs)
        var aim = BotAim(p, b, target, visible, def, steer, now);
        if (!BotLauncherTick(p, b, target, visible, ref aim, now)) // a launcher at an enemy kill streak or a group (BotLaunchers.cs)
        {
            BotShoot(p, b, target, visible, def, aim, now);
            BotBlindFire(p, b, def, aim, now);
        }
        SquadSuppress(p, b, visible, def, aim, now); // Insane bases of fire: suppressive fire at a last known spot
        BotGrenade(p, b, target, visible, aim.Distance, now);
        BotTactical(p, b, target, visible, aim.Distance, now); // tactical equipment (GameTactical.cs)
        var buttons = BotButtons(p, b, visible, def, steer, aim, now);

        var cmd = new InputCmd
        {
            Seq = ++b.Seq,
            Buttons = buttons,
            Yaw = aim.Yaw,
            Pitch = Math.Clamp(aim.Pitch, -1.5f, 1.5f),
            Weapon = p.Weapon,
            SpeedPct = (byte)Math.Clamp((int)(Perks.MoveSpeed(p.Perks, FrenzyBuffs.Mobility(p, def), aim.Ads) * 100), 1, 200), // Lightweight, Stalker, the LMG frenzy buff
        };
        ApplyCmd(p, cmd);
        b.LastHealth = p.Health;
    }

    private void BotReload(Player p, WeaponDef def, double now)
    {
        var b = p.Bot!;
        b.ReloadUntil = now + def.Reload * StreakReloadMult(p, now) * Perks.ReloadMult(p.Perks) * FrenzyBuffs.Value(p, def, FrenzyStat.Reload); // Overdrive, Sleight of Hand, the LMG frenzy buff
        b.BurstLeft = 0;
        Audio.Reload(def, p.RenderEye);
        Net.Broadcast(ReloadFx(p.Id, def.Id), PacketReliability.Unreliable);
    }

    /// <summary>Bot fighting statistics for this session (server only; read by the self-test).</summary>
    public readonly BotStats BotStats = new();

    /// <summary>Called by DamagePlayer for every kill: bookkeeping and reactions.</summary>
    private void BotNoteKill(Player killer, Player victim, bool head, byte killId, double now)
    {
        SquadNoteKill(killer, victim, now); // Insane squads: callouts and trades (SquadPlanner.cs)
        if (killer.Bot is not { } b || killer == victim)
            return;
        // Marksmen move on after a kill instead of sitting on a spot everyone now knows about.
        if (b.Personality == BotPersonality.Marksman)
        {
            b.HoldUntil = 0;
            b.GoalUntil = 0;
        }
        if (killId == MeleeKillId)
            BotStats.MeleeKills++;
        if (killId >= MeleeKillId || KillStreaks.IsStreakKill(killId))
            return;
        BotStats.GunKills++;
        if (head)
            BotStats.GunHeadshots++;
    }

    // ---- Names / roster ------------------------------------------------------------

    /// <summary>Sends everyone the id -> name table (server only).</summary>
    private void BroadcastRoster()
    {
        if (!IsServer)
            return;
        RefreshHostProfile(); // the host's own prestige / level (GameCosmetics.cs)
        _w.Begin(Msg.Roster).U8((byte)Players.Count);
        foreach (var p in Players.Values)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(p.Name);
            _w.U8(p.Id).U8(p.Bot != null ? (byte)1 : (byte)0).U8((byte)bytes.Length);
            foreach (var c in bytes)
                _w.U8(c);
            WriteRosterCosmetics(_w, p); // prestige, level, hat (GameCosmetics.cs)
        }
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        RefreshBotGear(); // someone joined, left or levelled up: the bots' gear follows the lobby's level (BotGear.cs)
    }

    private void ReadRoster(ref PacketReader r)
    {
        int count = r.U8();
        for (var i = 0; i < count && r.Has(3); i++)
        {
            var id = r.U8();
            var isBot = r.U8() != 0;
            int len = r.U8();
            var chars = new char[len];
            for (var k = 0; k < len && r.Has(1); k++)
                chars[k] = (char)r.U8();
            // Bot names carry their personality, e.g. "[BOT] Brick (Rusher)", so they get a little more room.
            var name = isBot ? CleanBotName(new string(chars)) : Settings.Clean(new string(chars));
            var cosmetics = ReadRosterCosmetics(ref r, isBot); // prestige, level, hat (GameCosmetics.cs)
            _names[id] = name;
            _cosmetics[id] = cosmetics;
            if (Players.TryGetValue(id, out var p))
            {
                p.Name = name;
                ApplyCosmetics(p, cosmetics);
            }
        }
    }

    private static string CleanBotName(string name)
    {
        var s = new string(name.Where(c => c >= 32 && c < 127).Take(28).ToArray()).Trim();
        return s.Length == 0 ? "Bot" : s;
    }

    private readonly Dictionary<byte, string> _names = new();

    public string NameOf(byte id) =>
        Players.TryGetValue(id, out var p) && p.Name.Length > 0 ? p.Name : _names.GetValueOrDefault(id) ?? $"Player {id}";
}
