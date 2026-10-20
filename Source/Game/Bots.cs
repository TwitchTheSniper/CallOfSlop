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
}

/// <summary>Per-bot AI state. Bots live only on the server and play through the same input commands as people.</summary>
public sealed class BotBrain
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
    public double Reaction => Skill switch { BotDifficulty.Easy => 0.65, BotDifficulty.Normal => 0.38, _ => 0.2 };
    public float AimError => Skill switch { BotDifficulty.Easy => 0.14f, BotDifficulty.Normal => 0.075f, _ => 0.03f }; // radians
    public float TurnSpeed => Skill switch { BotDifficulty.Easy => 3.5f, BotDifficulty.Normal => 6f, _ => 10f }; // rad/s
    public float Settle => Skill switch { BotDifficulty.Easy => 1.2f, BotDifficulty.Normal => 2.4f, _ => 4.5f }; // aim error decay /s
    public double TriggerDelay => Skill switch { BotDifficulty.Easy => 0.25, BotDifficulty.Normal => 0.12, _ => 0.05 }; // extra delay on semi-autos
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
        var bot = Players.Values.FirstOrDefault(p => p.Bot != null);
        if (bot == null)
            return false;
        Players.Remove(bot.Id);
        Log($"{bot.Name} left to make room");
        return true;
    }

    private void PickBotLoadout(Player bot) => PickBotLoadout(bot, bot.Bot?.Profile ?? BotPersonalities.Get(BotPersonality.Chaotic));

    /// <summary>Bots run a couple of random attachments too (no optics: they don't need them).</summary>
    private uint RandomKit(WeaponDef w)
    {
        uint kit = 0;
        foreach (var a in Attachments.For(w))
            if (a.Slot != AttachSlot.Optic && Attachments.Slot(kit, a.Slot) == 0 && _rng.NextDouble() < 0.25)
                kit = Attachments.With(kit, a.Slot, a.Index);
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

    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        var d = to - from;
        var dist = d.Length();
        if (dist < 0.01f)
            return true;
        if (World.RaycastWorld(from, d / dist, dist) < dist - 0.05f)
            return false;
        return !SmokeBlocks(from, to, Brutal.GlfwApi.Glfw.Time); // bots can't see through smoke either
    }

    /// <summary>One simulation tick of bot thinking: perceive, decide, steer, aim, shoot, move.</summary>
    private void BotTick(Player p, double now)
    {
        var b = p.Bot!;
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
        BotTickShared();
        var nav = NavGraph.Current;
        if (b.PathGraph != nav)
        {
            b.PathGraph = nav;
            b.ClearPath();
            b.Waypoint = Vector3.Zero;
        }

        var target = BotPerceive(p, b, now, out var visible);
        BotDecide(p, b, target, visible, def, now);
        var steer = BotSteer(p, b, target, visible, def, now);
        var aim = BotAim(p, b, target, visible, def, steer, now);
        BotShoot(p, b, target, visible, def, aim, now);
        BotGrenade(p, b, target, visible, aim.Distance, now);
        var buttons = BotButtons(p, b, visible, def, steer, aim, now);

        var cmd = new InputCmd
        {
            Seq = ++b.Seq,
            Buttons = buttons,
            Yaw = aim.Yaw,
            Pitch = Math.Clamp(aim.Pitch, -1.5f, 1.5f),
            Weapon = p.Weapon,
            SpeedPct = (byte)Math.Clamp((int)(def.Mobility * (aim.Ads ? World.AdsSpeedMult : 1f) * 100), 1, 200),
        };
        ApplyCmd(p, cmd);
        b.LastHealth = p.Health;
    }

    private void BotReload(Player p, WeaponDef def, double now)
    {
        var b = p.Bot!;
        b.ReloadUntil = now + def.Reload * StreakReloadMult(p, now);
        b.BurstLeft = 0;
        Audio.Reload(def, p.RenderEye);
        Net.Broadcast(ReloadFx(p.Id, def.Id), PacketReliability.Unreliable);
    }

    /// <summary>Bot fighting statistics for this session (server only; read by the self-test).</summary>
    public readonly BotStats BotStats = new();

    /// <summary>Called by DamagePlayer for every kill: bookkeeping and reactions.</summary>
    private void BotNoteKill(Player killer, Player victim, bool head, byte killId, double now)
    {
        if (killer.Bot is not { } b || killer == victim)
            return;
        // Marksmen move on after a kill instead of sitting on a spot everyone now knows about.
        if (b.Personality == BotPersonality.Marksman)
        {
            b.HoldUntil = 0;
            b.GoalUntil = 0;
        }
        if (killId >= MeleeKillId)
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
        _w.Begin(Msg.Roster).U8((byte)Players.Count);
        foreach (var p in Players.Values)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(p.Name);
            _w.U8(p.Id).U8(p.Bot != null ? (byte)1 : (byte)0).U8((byte)bytes.Length);
            foreach (var c in bytes)
                _w.U8(c);
        }
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
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
            _names[id] = name;
            if (Players.TryGetValue(id, out var p))
                p.Name = name;
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
