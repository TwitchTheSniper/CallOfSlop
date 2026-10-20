using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>The kill streaks. Values are saved by name, so the order here can change freely (ids must stay below 9: they're bits of a byte).</summary>
public enum StreakId : byte
{
    None,
    ReconSweep,
    CounterSweep,
    Overdrive,
    MortarBarrage,
    SentryTurret,
    AttackDrone,
    StrafeRun,
}

/// <summary>How a streak is called in.</summary>
public enum StreakAim : byte
{
    /// <summary>Straight away when you press its key.</summary>
    Instant,
    /// <summary>Look at a spot and press fire (Mortar Barrage).</summary>
    Spot,
    /// <summary>Look at a spot and press fire: the run passes over it along the way you're facing (Strafe Run).</summary>
    Line,
}

/// <summary>What a kill streak costs and does.</summary>
public sealed class StreakDef
{
    public required StreakId Id;
    public required string Name;
    public required int Kills; // kills in one life to earn it
    public required string Short; // a few words for cards and the HUD
    public required string Effect; // the full description
    public StreakAim Aim;
    public Vector3 Color;
}

/// <summary>One shell or strafe impact of a called-in strike: when (seconds after the call) and where.</summary>
public readonly record struct StrikeShell(float Delay, Vector3 Pos);

/// <summary>The three kill streaks a loadout carries, always kept sorted by cost (cheapest first, None at the end).</summary>
public readonly record struct StreakSet(StreakId A, StreakId B, StreakId C)
{
    public static readonly StreakSet Empty = new(StreakId.None, StreakId.None, StreakId.None);

    /// <summary>What everyone starts with (and what old loadout files get).</summary>
    public static StreakSet Starter => new StreakSet(KillStreaks.Starters[0], KillStreaks.Starters[1], KillStreaks.Starters[2]).Sanitized();

    public StreakId this[int i] => i switch { 0 => A, 1 => B, _ => C };

    public bool Has(StreakId id) => id != StreakId.None && (A == id || B == id || C == id);

    /// <summary>The streaks in the set, cheapest first.</summary>
    public IEnumerable<StreakId> Ids => new[] { A, B, C }.Where(x => x != StreakId.None);

    public int Count => Ids.Count();

    /// <summary>Valid, no duplicates, sorted by kill cost (None last). Used on everything from saves and packets.</summary>
    public StreakSet Sanitized()
    {
        var ids = new[] { A, B, C }.Where(KillStreaks.IsValid).Distinct()
            .OrderBy(x => KillStreaks.Get(x).Kills).ThenBy(x => (byte)x).ToList();
        while (ids.Count < 3)
            ids.Add(StreakId.None);
        return new StreakSet(ids[0], ids[1], ids[2]);
    }

    /// <summary>
    /// Adds <paramref name="id"/>: into a free slot if there is one, otherwise in place of <paramref name="replace"/>
    /// (or of the most expensive streak if that isn't in the set). Picking one you already have changes nothing.
    /// </summary>
    public StreakSet With(StreakId id, StreakId replace = StreakId.None)
    {
        if (!KillStreaks.IsValid(id) || Has(id))
            return Sanitized();
        var list = new List<StreakId> { A, B, C };
        var free = list.IndexOf(StreakId.None);
        if (free < 0)
        {
            free = list.IndexOf(replace);
            if (free < 0)
                free = list.IndexOf(Sanitized().C);
        }
        list[free] = id;
        return new StreakSet(list[0], list[1], list[2]).Sanitized();
    }

    /// <summary>The set without <paramref name="id"/> (its slot becomes empty).</summary>
    public StreakSet Without(StreakId id) =>
        new StreakSet(A == id ? StreakId.None : A, B == id ? StreakId.None : B, C == id ? StreakId.None : C).Sanitized();

    public override string ToString() => string.Join(", ", Ids.Select(x => KillStreaks.Get(x).Name));
}

/// <summary>A player's kill streak progress. The server owns it; each client gets its own copy in Msg.StreakState.</summary>
public sealed class StreakState
{
    /// <summary>The streaks carried this life.</summary>
    public StreakSet Slots = StreakSet.Starter;

    /// <summary>Server: the streaks asked for, applied on the next spawn (or straight away in the countdown).</summary>
    public StreakSet Want = StreakSet.Starter;

    /// <summary>Kills since the last death (kills by streaks don't count).</summary>
    public byte Count;

    /// <summary>Earned and not used yet: one bit per <see cref="StreakId"/>. Earned streaks survive death until used.</summary>
    public byte Ready;

    /// <summary>Server: bots pick random streaks once.</summary>
    public bool BotPicked;

    public static byte Bit(StreakId id) => id == StreakId.None ? (byte)0 : (byte)(1 << ((int)id - 1));

    public bool IsReady(StreakId id) => (Ready & Bit(id)) != 0;

    public void Reset()
    {
        Count = 0;
        Ready = 0;
    }
}

/// <summary>A timed streak effect that both sides track: Recon Sweep, Counter Sweep or Overdrive.</summary>
public readonly record struct StreakEffect(StreakId Kind, byte Owner, double Born, double Until);

/// <summary>A visual strike impact (mortar shell or strafe hit), scheduled from the call so clients need no extra messages.</summary>
public sealed class StrikeFx
{
    public StreakId Kind;
    public byte Owner;
    public Vector3 Pos;
    public double At; // impact time
    public bool Whistled;
    public bool Done;
}

/// <summary>Where a strike was called (the red flare everyone can see) and, for a strafe run, the jet's line.</summary>
public readonly record struct StrikeMarker(StreakId Kind, byte Owner, Vector3 Pos, float Yaw, double Born, double Until);

/// <summary>Kill streak definitions, tuning and the deterministic strike planning both sides share.</summary>
public static class KillStreaks
{
    /// <summary>Kill-feed ids 230-238 are streaks (230 + id); 239 is melee and 240+ throwables.</summary>
    public const byte KillIdBase = 230;

    // ---- Tuning ---------------------------------------------------------------------
    public const float MaxCallRange = 250f;
    public const double ReconSweepSeconds = 10;
    public const double CounterSweepSeconds = 15;
    public const double OverdriveSeconds = 12;
    public const float OverdriveReloadMult = 0.5f;

    public const int MortarShells = 7;
    public const float MortarDelay = 2.4f, MortarInterval = 0.32f, MortarSpread = 5f, MortarRadius = 6.5f, MortarDamage = 160f;
    public const float WhistleLead = 1.1f; // the incoming whistle starts this long before each shell lands

    public const int StrafeHits = 12;
    public const float StrafeDelay = 2.6f, StrafeLength = 50f, StrafeSpeed = 70f, StrafeRadius = 4.2f, StrafeDamage = 110f, StrafeAltitude = 28f;

    public const double TurretSeconds = 45;
    public const int TurretHealth = 260;
    public const float TurretRange = 40f, TurretConeCos = 0.26f, TurretDamage = 12f, TurretSpread = 0.03f, TurretTurnRate = 3f;
    public const double TurretInterval = 0.13, TurretWarmup = 0.5;

    public const double DroneSeconds = 40;
    public const int DroneHealth = 200;
    public const float DroneRange = 70f, DroneDamage = 26f, DroneSpread = 0.025f, DroneSpeed = 7f;
    public const int DroneBurst = 3;
    public const double DroneBurstGap = 0.12, DroneCooldown = 1.6, DroneWindup = 0.35;

    public static readonly StreakDef[] All =
    [
        new() { Id = StreakId.None, Name = "None", Kills = 0, Short = "", Effect = "Leave this slot empty." },
        new() { Id = StreakId.ReconSweep, Name = "Recon Sweep", Kills = 3, Short = "Reveal every enemy",
            Effect = "A sweep shows every enemy on the map to you as a red silhouette through walls for 10 seconds.",
            Color = new(0.2f, 0.85f, 0.95f) },
        new() { Id = StreakId.CounterSweep, Name = "Counter Sweep", Kills = 4, Short = "Jam enemy intel",
            Effect = "Jams the enemy for 15 seconds: their Recon Pulses and Recon Sweeps show nothing and their HUD fills with static.",
            Color = new(0.75f, 0.45f, 1f) },
        new() { Id = StreakId.Overdrive, Name = "Overdrive", Kills = 4, Short = "Faster move and reload",
            Effect = "For 12 seconds you move 35% faster and reload twice as fast. Ends if you die.",
            Color = new(1f, 0.8f, 0.2f) },
        new() { Id = StreakId.MortarBarrage, Name = "Mortar Barrage", Kills = 5, Short = "7 shells on a spot", Aim = StreakAim.Spot,
            Effect = "Look at a spot and press fire. After a short delay 7 shells land around it, each one whistling as it comes in. Walls and roofs shield you.",
            Color = new(1f, 0.5f, 0.2f) },
        new() { Id = StreakId.SentryTurret, Name = "Sentry Turret", Kills = 6, Short = "Auto-turret, 45 s",
            Effect = "Drops an automatic turret in front of you, facing where you look. It shoots enemies in front of it for 45 seconds, and can be destroyed.",
            Color = new(0.55f, 0.9f, 0.45f) },
        new() { Id = StreakId.AttackDrone, Name = "Attack Drone", Kills = 8, Short = "Circling gun drone, 40 s",
            Effect = "A drone circles the map for 40 seconds, firing bursts at any enemy it can see. It can be shot down.",
            Color = new(0.95f, 0.35f, 0.35f) },
        new() { Id = StreakId.StrafeRun, Name = "Strafe Run", Kills = 11, Short = "Jet strafes a line", Aim = StreakAim.Line,
            Effect = "Look at a spot and press fire: a jet strafes a 50 m line through it, along the way you're facing. Roofs shield you.",
            Color = new(1f, 0.3f, 0.6f) },
    ];

    public static StreakDef Get(StreakId id) => All[(int)id < All.Length ? (int)id : 0];

    public static bool IsValid(StreakId id) => id != StreakId.None && (int)id < All.Length;

    public static StreakDef? Find(string name) =>
        All.FirstOrDefault(s => s.Id != StreakId.None && s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Unlocked at level 1.</summary>
    public static readonly StreakId[] Starters = [StreakId.ReconSweep, StreakId.Overdrive, StreakId.MortarBarrage];

    /// <summary>The rest, in unlock order (spread evenly from level 5 to two levels before max).</summary>
    public static readonly StreakId[] UnlockOrder = [StreakId.CounterSweep, StreakId.SentryTurret, StreakId.AttackDrone, StreakId.StrafeRun];

    public static int UnlockLevel(StreakId id)
    {
        if (id == StreakId.None || Starters.Contains(id))
            return 1;
        var i = Array.IndexOf(UnlockOrder, id);
        return i < 0 ? Progress.MaxLevel : 5 + (int)MathF.Round(i * (Progress.MaxLevel - 7) / (float)(UnlockOrder.Length - 1));
    }

    public static byte KillId(StreakId id) => (byte)(KillIdBase + (byte)id);

    /// <summary>Was this kill made by a streak? (Those never count toward the next streak.)</summary>
    public static bool IsStreakKill(byte killId) => killId >= KillIdBase && killId < Game.MeleeKillId;

    public static StreakId FromKillId(byte killId) => IsStreakKill(killId) ? (StreakId)(killId - KillIdBase) : StreakId.None;

    // ---- Strike planning (identical on server and clients) ----------------------------

    /// <summary>
    /// The impacts of a Mortar Barrage or Strafe Run, from its call: shells scattered round the spot, or hits every few
    /// metres along the line. Deterministic from the seed and the map, so the server and every client agree.
    /// </summary>
    public static List<StrikeShell> Plan(StreakId id, Vector3 target, float yaw, uint seed)
    {
        var list = new List<StrikeShell>();
        var s = seed | 1;
        float Next()
        {
            // xorshift32: same numbers everywhere
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return (s & 0xFFFFFF) / (float)0x1000000;
        }
        if (id == StreakId.MortarBarrage)
        {
            var t = MortarDelay;
            for (var i = 0; i < MortarShells; i++)
            {
                var a = Next() * MathF.Tau;
                var r = MortarSpread * MathF.Sqrt(Next());
                list.Add(new StrikeShell(t, GroundAt(target.X + MathF.Cos(a) * r, target.Z + MathF.Sin(a) * r, target.Y)));
                t += MortarInterval * (0.6f + 0.8f * Next());
            }
        }
        else if (id == StreakId.StrafeRun)
        {
            var dir = World.FlatForward(yaw);
            for (var i = 0; i < StrafeHits; i++)
            {
                var off = -StrafeLength / 2 + i * StrafeLength / (StrafeHits - 1);
                var side = (Next() - 0.5f) * 1.6f;
                var p = target + dir * off + new Vector3(-dir.Z, 0, dir.X) * side;
                list.Add(new StrikeShell(StrafeDelay + (off + StrafeLength / 2) / StrafeSpeed, GroundAt(p.X, p.Z, target.Y)));
            }
        }
        return list;
    }

    /// <summary>Where something falling from the sky at (x, z) lands: the first surface below, roofs included.</summary>
    public static Vector3 GroundAt(float x, float z, float nearY)
    {
        x = Math.Clamp(x, -World.Map.HalfX + 0.5f, World.Map.HalfX - 0.5f);
        z = Math.Clamp(z, -World.Map.HalfZ + 0.5f, World.Map.HalfZ - 0.5f);
        var top = MathF.Max(nearY, 0) + 40f;
        var d = World.RaycastWorld(new Vector3(x, top, z), -Vector3.UnitY, top + 5f);
        return new Vector3(x, MathF.Max(top - d, -1f), z);
    }

    // ---- Packets (static so the self-test can round-trip them) -------------------------

    /// <summary>Client -> server: use a streak (target and facing matter for aimed ones).</summary>
    public static void WriteUse(PacketWriter w, StreakId id, Vector3 target, float yaw) => w.Begin(Msg.StreakUse).U8((byte)id).Vec(target).F32(yaw);

    public const int UseBytes = 17;

    public static (StreakId Id, Vector3 Target, float Yaw) ReadUse(ref PacketReader r) => ((StreakId)r.U8(), r.Vec(), r.F32());

    /// <summary>Server -> all: a streak event. Event 0 = called in, 1 = streak entity destroyed (other = who did it, 255 = expired).</summary>
    public static void WriteEvent(PacketWriter w, byte evt, byte owner, StreakId id, Vector3 pos, float yaw, uint seed, byte other) =>
        w.Begin(Msg.StreakEvent).U8(evt).U8(owner).U8((byte)id).Vec(pos).F32(yaw).U32(seed).U8(other);

    public const int EventBytes = 24;

    public static (byte Evt, byte Owner, StreakId Id, Vector3 Pos, float Yaw, uint Seed, byte Other) ReadEvent(ref PacketReader r) =>
        (r.U8(), r.U8(), (StreakId)r.U8(), r.Vec(), r.F32(), r.U32(), r.U8());

    /// <summary>Server -> one client: your streak count, earned streaks and carried slots.</summary>
    public static void WriteState(PacketWriter w, StreakState s) =>
        w.Begin(Msg.StreakState).U8(s.Count).U8(s.Ready).U8((byte)s.Slots.A).U8((byte)s.Slots.B).U8((byte)s.Slots.C);

    public const int StateBytes = 5;

    public static void ReadState(ref PacketReader r, StreakState s)
    {
        s.Count = r.U8();
        s.Ready = r.U8();
        s.Slots = new StreakSet((StreakId)r.U8(), (StreakId)r.U8(), (StreakId)r.U8()).Sanitized();
    }
}

/// <summary>
/// Kill streaks in a match (server-authoritative). Each player carries three streaks from their loadout.
/// Kills since your last death count up; reaching a streak's cost earns it, and an earned streak is kept
/// (even through death) until you use it with its key. Kills made by streaks never count toward more streaks.
/// </summary>
public sealed partial class Game
{
    /// <summary>Kill streaks on or off for this match (the lead wires this to the game mode: Gun Game turns them off).</summary>
    public bool StreaksEnabled
    {
        get => _streaksSwitch && ModeAllowsStreaks; // Gun Game: off
        set => _streaksSwitch = value;
    }

    private bool _streaksSwitch = true;

    /// <summary>
    /// Is <paramref name="other"/> an enemy of a streak's owner? The ONE place streaks decide who they hurt, reveal and jam.
    /// </summary>
    /// Teams count (game modes): a streak never hurts, reveals or jams its owner's teammates.
    public bool StreakIsEnemy(Player owner, Player other) => IsEnemy(owner, other);

    /// <summary>The streaks picked in the menu (sent with the loadout).</summary>
    public StreakSet ChosenStreaks = StreakSet.Starter;

    public readonly List<StreakEffect> StreakEffects = new();
    public readonly List<StrikeFx> Strikes = new();
    public readonly List<StrikeMarker> StrikeMarkers = new();

    /// <summary>Local player: the aimed streak being targeted (look and press fire), or None.</summary>
    public StreakId StreakTargeting;

    /// <summary>Local player: where the targeted streak would land, and whether there's a spot under the crosshair.</summary>
    public Vector3 StreakAimPoint;
    public bool StreakAimValid;

    /// <summary>Banner for streaks earned and called in (yours and everyone else's).</summary>
    public string StreakBanner = "";
    public double StreakBannerTime = -100;
    public bool StreakBannerEnemy;

    private readonly List<(double At, byte Owner, Vector3 Pos, float Radius, float Damage, byte KillId)> _pendingBlasts = new();
    private int _streakMatch = -1;
    private byte _lastReady;
    private bool _streakHoldBlock;
    private double _streakSentAt = -10;

    /// <summary>The local player's streak state (server copy on the host, the replicated copy on a client).</summary>
    public StreakState? MyStreak => Local?.Streak;

    // =====================================================================
    // Server
    // =====================================================================

    /// <summary>After every kill (from DamagePlayer): the victim's streak resets, the killer's counts up.</summary>
    private void StreakOnKill(Player attacker, Player victim, byte killId, double now)
    {
        if (victim.Streak.Count != 0)
        {
            victim.Streak.Count = 0;
            SendStreakState(victim);
        }
        // Classic rules: streak kills don't chain, suicides and kills after your own death don't count.
        if (!StreaksEnabled || attacker == victim || !attacker.Alive || KillStreaks.IsStreakKill(killId))
            return;
        var s = attacker.Streak;
        if (s.Count < 255)
            s.Count++;
        foreach (var id in s.Slots.Ids)
            if (s.Count == KillStreaks.Get(id).Kills)
                s.Ready |= StreakState.Bit(id);
        SendStreakState(attacker);
    }

    /// <summary>On (re)spawn: the requested streaks come into effect. Earned streaks no longer in the loadout are dropped.</summary>
    private void StreakOnSpawn(Player p)
    {
        var s = p.Streak;
        if (p.Bot != null && !s.BotPicked)
        {
            s.Want = RandomStreaks();
            s.BotPicked = true;
        }
        s.Slots = s.Want.Sanitized();
        s.Count = 0;
        var keep = (byte)0;
        foreach (var id in s.Slots.Ids)
            keep |= StreakState.Bit(id);
        s.Ready &= keep;
        SendStreakState(p);
    }

    /// <summary>A player picked streaks: they apply on the next spawn, or straight away while dead or in the countdown.</summary>
    private void StreakRequest(Player p, StreakSet set)
    {
        p.Streak.Want = set.Sanitized();
        if (!p.Alive || Phase == MatchPhase.Countdown)
        {
            p.Streak.Slots = p.Streak.Want;
            SendStreakState(p);
        }
    }

    /// <summary>Bots carry three random streaks.</summary>
    private StreakSet RandomStreaks()
    {
        var pool = KillStreaks.All.Where(s => s.Id != StreakId.None).Select(s => s.Id).OrderBy(_ => _rng.Next()).Take(3).ToArray();
        return new StreakSet(pool[0], pool[1], pool[2]).Sanitized();
    }

    private void SendStreakState(Player p)
    {
        if (!IsServer || p.Bot != null || p.Id == LocalId)
            return;
        foreach (var (guid, id) in _clients.Values)
        {
            if (id != p.Id)
                continue;
            KillStreaks.WriteState(_w, p.Streak);
            Net.SendTo(guid, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Server, every tick: match resets, scheduled strikes, Overdrive, turrets and drones.</summary>
    private void ServerTickStreaks(double now)
    {
        if (_streakMatch != MatchNumber)
        {
            // A fresh match: everyone starts from nothing.
            _streakMatch = MatchNumber;
            foreach (var p in Players.Values)
            {
                p.Streak.Reset();
                SendStreakState(p);
            }
            ClearStreakWorld();
        }
        if (Phase != MatchPhase.Playing)
        {
            if (_pendingBlasts.Count > 0 || StreakEntities.Count > 0)
                ClearStreakWorld();
            SendStreakEntities();
            return;
        }

        for (var i = _pendingBlasts.Count - 1; i >= 0; i--)
        {
            var b = _pendingBlasts[i];
            if (now < b.At)
                continue;
            _pendingBlasts.RemoveAt(i);
            StreakBlast(b.Owner, b.Pos, b.Radius, b.Damage, b.KillId, now);
        }

        // Overdrive keeps its owner's speed boost topped up (the same boost Head Start uses, so movement prediction agrees).
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.Overdrive && now < e.Until && Players.TryGetValue(e.Owner, out var o) && o.Alive)
                o.Sim.BoostTicks = Math.Max(o.Sim.BoostTicks, (byte)20);

        ServerTickStreakEntities(now);
        if (ServerTick % World.SnapshotEvery == 0)
            SendStreakEntities();
    }

    private void ClearStreakWorld()
    {
        _pendingBlasts.Clear();
        StreakEntities.Clear();
        StreakEffects.Clear();
        Strikes.Clear();
        StrikeMarkers.Clear();
    }

    /// <summary>Server: someone uses an earned streak. Everything is checked here; the client only asks.</summary>
    private bool ServerUseStreak(Player p, StreakId id, Vector3 target, float yaw, double now)
    {
        var s = p.Streak;
        if (!StreaksEnabled || !p.Alive || Phase != MatchPhase.Playing || !KillStreaks.IsValid(id) || !s.Slots.Has(id) || !s.IsReady(id))
            return false;
        if (!float.IsFinite(yaw))
            yaw = p.Yaw;
        var def = KillStreaks.Get(id);
        var seed = (uint)_rng.Next(1, int.MaxValue);
        var pos = p.Position;
        if (def.Aim != StreakAim.Instant)
        {
            if (!float.IsFinite(target.X + target.Y + target.Z) || Vector3.Distance(target, p.Eye) > KillStreaks.MaxCallRange + 5)
                return false;
            target.X = Math.Clamp(target.X, -World.Map.HalfX, World.Map.HalfX);
            target.Z = Math.Clamp(target.Z, -World.Map.HalfZ, World.Map.HalfZ);
            pos = target;
            var radius = id == StreakId.MortarBarrage ? KillStreaks.MortarRadius : KillStreaks.StrafeRadius;
            var damage = id == StreakId.MortarBarrage ? KillStreaks.MortarDamage : KillStreaks.StrafeDamage;
            foreach (var shell in KillStreaks.Plan(id, target, yaw, seed))
                _pendingBlasts.Add((now + shell.Delay, p.Id, shell.Pos, radius, damage, KillStreaks.KillId(id)));
        }
        else if (id == StreakId.SentryTurret)
        {
            pos = SpawnTurret(p, yaw, now).Pos;
        }
        else if (id == StreakId.AttackDrone)
        {
            pos = SpawnDrone(p, now).Pos;
        }
        s.Ready &= (byte)~StreakState.Bit(id);
        SendStreakState(p);
        p.LastCombatTime = now;

        KillStreaks.WriteEvent(_w, 0, p.Id, id, pos, yaw, seed, 255);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        OnStreakCalled(p.Id, id, pos, yaw, seed, now);
        return true;
    }

    /// <summary>
    /// Server: a strike explosion. Same rules as a grenade (walls and roofs shield you, damage falls off, prone helps), but
    /// it only hurts the owner's enemies and their turrets and drones.
    /// </summary>
    private void StreakBlast(byte ownerId, Vector3 ground, float radius, float maxDamage, byte killId, double now)
    {
        var center = ground + new Vector3(0, 0.3f, 0);
        byte flags = 0;
        if (Players.TryGetValue(ownerId, out var owner))
        {
            foreach (var victim in Players.Values.ToList())
            {
                if (!victim.Alive || !StreakIsEnemy(owner, victim))
                    continue;
                var amount = BlastDamage(center, victim.Chest, radius, maxDamage);
                if (victim.Sim.Stance == Stance.Prone)
                    amount *= 0.7f;
                if ((int)MathF.Round(amount) <= 0)
                    continue;
                flags |= 1;
                if (DamagePlayer(owner, victim, (int)MathF.Round(amount), false, killId, now, DamageKind.Explosive))
                    flags |= 4;
            }
            StreakBlastEntities(owner, center, radius, maxDamage, now);
        }
        if (ownerId == LocalId && flags != 0)
            OnOwnShotConfirmed(flags, now);
    }

    /// <summary>Damage from a blast at <paramref name="center"/> to a point: 0 beyond the radius or behind a wall.</summary>
    private static float BlastDamage(Vector3 center, Vector3 point, float radius, float maxDamage)
    {
        var to = point - center;
        var dist = to.Length();
        if (dist > radius)
            return 0;
        if (dist > 0.3f && World.RaycastWorld(center, to / dist, dist) < dist - 0.05f)
            return 0;
        return maxDamage * MathF.Pow(1f - dist / radius, 1.4f);
    }

    // =====================================================================
    // Both sides
    // =====================================================================

    /// <summary>A streak was called in: banner, timed effects and the strike's visuals (the server already scheduled its damage).</summary>
    private void OnStreakCalled(byte owner, StreakId id, Vector3 pos, float yaw, uint seed, double now)
    {
        var def = KillStreaks.Get(id);
        var mine = owner == LocalId;
        var enemy = Local is { } me && Players.TryGetValue(owner, out var op) && StreakIsEnemy(op, me);
        var article = "AEIOU".Contains(def.Name[0]) ? "an" : "a";
        StreakBanner = mine ? $"You called in {article} {def.Name}" : $"{NameOf(owner)} called in {article} {def.Name}";
        StreakBannerTime = now;
        StreakBannerEnemy = enemy;
        Console.WriteLine($"[streak] {NameOf(owner)} called in {def.Name}");
        Audio.Effect(Sfx.StreakCall, mine ? 0.7f : enemy ? 0.6f : 0.4f);

        switch (id)
        {
            case StreakId.ReconSweep:
                StreakEffects.Add(new StreakEffect(id, owner, now, now + KillStreaks.ReconSweepSeconds));
                break;
            case StreakId.CounterSweep:
                StreakEffects.Add(new StreakEffect(id, owner, now, now + KillStreaks.CounterSweepSeconds));
                break;
            case StreakId.Overdrive:
                StreakEffects.Add(new StreakEffect(id, owner, now, now + KillStreaks.OverdriveSeconds));
                break;
            case StreakId.MortarBarrage:
            case StreakId.StrafeRun:
            {
                var plan = KillStreaks.Plan(id, pos, yaw, seed);
                foreach (var shell in plan)
                    Strikes.Add(new StrikeFx { Kind = id, Owner = owner, Pos = shell.Pos, At = now + shell.Delay });
                var last = plan.Count > 0 ? plan.Max(x => x.Delay) : 3f;
                StrikeMarkers.Add(new StrikeMarker(id, owner, pos, yaw, now, now + last + 0.5));
                break;
            }
        }
    }

    /// <summary>Is <paramref name="p"/> jammed by an enemy's Counter Sweep?</summary>
    public bool StreakJammed(Player? p, double now)
    {
        if (p == null)
            return false;
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.CounterSweep && now < e.Until && Players.TryGetValue(e.Owner, out var o) && StreakIsEnemy(o, p))
                return true;
        return false;
    }

    /// <summary>Is <paramref name="p"/> shown to the local player by a friendly Recon Sweep?</summary>
    public bool StreakSweepReveals(Player p, double now)
    {
        if (Local is not { } me)
            return false;
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.ReconSweep && now < e.Until && Players.TryGetValue(e.Owner, out var o) && !StreakIsEnemy(o, me) && StreakIsEnemy(o, p))
                return true;
        return false;
    }

    /// <summary>Is Overdrive running for this player?</summary>
    public bool OverdriveActive(Player p, double now)
    {
        if (!p.Alive)
            return false;
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.Overdrive && e.Owner == p.Id && now < e.Until)
                return true;
        return false;
    }

    /// <summary>Reload time multiplier (Overdrive halves it).</summary>
    public float StreakReloadMult(Player? p, double now) => p != null && OverdriveActive(p, now) ? KillStreaks.OverdriveReloadMult : 1f;

    /// <summary>Seconds left on an effect of this kind owned by <paramref name="owner"/> (0 if none).</summary>
    public double StreakEffectLeft(StreakId kind, byte owner, double now)
    {
        var left = 0.0;
        foreach (var e in StreakEffects)
            if (e.Kind == kind && e.Owner == owner)
                left = Math.Max(left, e.Until - now);
        return left;
    }

    /// <summary>Every frame: strike visuals and sounds, expiring effects, the local player's aim point and banners.</summary>
    private void UpdateStreakEffects(double now, float dt)
    {
        // Overdrive ends when its owner dies; everything else just runs out.
        StreakEffects.RemoveAll(e => now > e.Until || (e.Kind == StreakId.Overdrive && Players.TryGetValue(e.Owner, out var o) && !o.Alive)
                                     || !Players.ContainsKey(e.Owner));
        StrikeMarkers.RemoveAll(m => now > m.Until);

        foreach (var s in Strikes)
        {
            if (s.Kind == StreakId.MortarBarrage && !s.Whistled && now >= s.At - KillStreaks.WhistleLead)
            {
                s.Whistled = true;
                Audio.Effect3D(Sfx.MortarWhistle, s.Pos + new Vector3(0, 6, 0), 0.9f);
            }
            if (s.Kind == StreakId.StrafeRun && !s.Whistled && now >= s.At - 0.25)
            {
                s.Whistled = true;
                Audio.Effect3D(Sfx.TurretShot, s.Pos + new Vector3(0, 10, 0), 0.8f); // the jet's cannon
            }
            if (!s.Done && now >= s.At)
            {
                s.Done = true;
                var radius = s.Kind == StreakId.MortarBarrage ? KillStreaks.MortarRadius : KillStreaks.StrafeRadius;
                Blasts.Add(new Blast(s.Pos + new Vector3(0, 0.3f, 0), now, radius, ThrowableKind.Frag));
                Audio.Effect3D(Sfx.Explosion, s.Pos, s.Kind == StreakId.MortarBarrage ? 1f : 0.75f);
                if (Local is { } me)
                {
                    var d = Vector3.Distance(me.RenderEye, s.Pos);
                    Shake = MathF.Max(Shake, Math.Clamp(1.3f - d / 18f, 0f, 1f));
                }
            }
        }
        Strikes.RemoveAll(s => s.Done);
        foreach (var m in StrikeMarkers)
        {
            // The strafe jet's roar as it passes over.
            if (m.Kind == StreakId.StrafeRun)
            {
                var pass = m.Born + KillStreaks.StrafeDelay + KillStreaks.StrafeLength / 2 / KillStreaks.StrafeSpeed - 0.6;
                if (now >= pass && now - dt < pass)
                    Audio.Effect3D(Sfx.JetFlyby, m.Pos + new Vector3(0, KillStreaks.StrafeAltitude * 0.5f, 0), 1f);
            }
        }

        UpdateStreakEntitiesVisual(now, dt);

        // Local: aim point while targeting, and banners for streaks you earn.
        if (Local is not { } local)
            return;
        if (StreakTargeting != StreakId.None)
        {
            if (!local.Alive || !local.Streak.IsReady(StreakTargeting) || Phase != MatchPhase.Playing)
                StreakTargeting = StreakId.None;
            var dir = World.Forward(LookYaw, LookPitch);
            var d = World.RaycastWorld(local.RenderEye, dir, KillStreaks.MaxCallRange);
            StreakAimValid = d < KillStreaks.MaxCallRange - 0.01f;
            StreakAimPoint = local.RenderEye + dir * d;
        }
        var ready = local.Streak.Ready;
        var fresh = (byte)(ready & ~_lastReady);
        if (fresh != 0 && StreaksEnabled)
        {
            var id = local.Streak.Slots.Ids.LastOrDefault(x => (fresh & StreakState.Bit(x)) != 0);
            if (id != StreakId.None)
            {
                StreakBanner = $"{KillStreaks.Get(id).Name.ToUpperInvariant()} READY";
                StreakBannerTime = now;
                StreakBannerEnemy = false;
                Audio.Effect(Sfx.StreakReady, 0.75f);
            }
        }
        _lastReady = ready;
    }

    // =====================================================================
    // Local player: keys and targeting
    // =====================================================================

    /// <summary>The streak key for slot 0-2 (cheapest first). Instant streaks go at once; aimed ones start targeting (press again to cancel).</summary>
    public void StreakKey(int slot, double now)
    {
        if (!StreaksEnabled || Local is not { Alive: true } me || Phase != MatchPhase.Playing || slot is < 0 or > 2)
            return;
        var id = me.Streak.Slots[slot];
        if (id == StreakId.None)
            return;
        if (!me.Streak.IsReady(id))
        {
            Audio.Effect(Sfx.DryFire, 0.5f);
            return;
        }
        var def = KillStreaks.Get(id);
        if (def.Aim != StreakAim.Instant)
        {
            StreakTargeting = StreakTargeting == id ? StreakId.None : id;
            Audio.Effect(Sfx.HitTick, 0.5f);
            return;
        }
        StreakTargeting = StreakId.None;
        SendStreakUse(id, me.Position, LookYaw, now);
    }

    /// <summary>
    /// Called first by <see cref="UpdateTrigger"/>: while targeting, fire calls the streak in (and never shoots), aiming cancels.
    /// Returns true when the trigger was used here.
    /// </summary>
    private bool StreakTrigger(double now, bool held, bool pressed)
    {
        if (_streakHoldBlock)
        {
            if (!held)
                _streakHoldBlock = false;
            return true; // the click that called it in mustn't start shooting
        }
        if (StreakTargeting == StreakId.None)
            return false;
        if (AimHeld)
        {
            StreakTargeting = StreakId.None;
            return true;
        }
        if (pressed)
        {
            if (!StreakAimValid)
            {
                Audio.Effect(Sfx.DryFire, 0.5f);
                return true;
            }
            SendStreakUse(StreakTargeting, StreakAimPoint, LookYaw, now);
            StreakTargeting = StreakId.None;
            _streakHoldBlock = true;
        }
        return true;
    }

    private void SendStreakUse(StreakId id, Vector3 target, float yaw, double now)
    {
        if (now - _streakSentAt < 0.4)
            return;
        _streakSentAt = now;
        if (IsServer)
        {
            if (Local is { } me)
                ServerUseStreak(me, id, target, yaw, now);
            return;
        }
        KillStreaks.WriteUse(_w, id, target, yaw);
        Net.SendToServer(_w.Span, PacketReliability.ReliableOrdered);
    }

    // =====================================================================
    // Packets
    // =====================================================================

    private void OnStreakPacket(byte id, ref PacketReader r, RakNetGUID sender, double now)
    {
        switch (id)
        {
            case (byte)Msg.StreakUse when IsServer && r.Has(KillStreaks.UseBytes):
            {
                if (!_clients.TryGetValue(sender.G, out var c) || !Players.TryGetValue(c.Id, out var p))
                    break;
                var (sid, target, yaw) = KillStreaks.ReadUse(ref r);
                ServerUseStreak(p, sid, target, yaw, now);
                break;
            }
            case (byte)Msg.StreakEvent when !IsServer && r.Has(KillStreaks.EventBytes):
            {
                var e = KillStreaks.ReadEvent(ref r);
                if (e.Evt == 0)
                    OnStreakCalled(e.Owner, e.Id, e.Pos, e.Yaw, e.Seed, now);
                else if (e.Evt == 1)
                    OnStreakEntityDestroyed(e.Owner, e.Id, e.Pos, e.Other, now);
                break;
            }
            case (byte)Msg.StreakState when !IsServer && r.Has(KillStreaks.StateBytes):
                if (Local is { } me)
                    KillStreaks.ReadState(ref r, me.Streak);
                break;
            case (byte)Msg.StreakEnts when !IsServer && r.Has(5):
                ReadStreakEntities(ref r, now);
                break;
        }
    }

    /// <summary>Reads the streak bytes a client appends to its loadout packet (older clients send none: starters).</summary>
    private void ReadLoadoutStreaks(Player p, ref PacketReader r)
    {
        var set = r.Has(3) ? new StreakSet((StreakId)r.U8(), (StreakId)r.U8(), (StreakId)r.U8()) : StreakSet.Starter;
        StreakRequest(p, set);
    }

    // =====================================================================
    // Self-test hooks
    // =====================================================================

    public bool SelfTestKill(Player attacker, Player victim, byte killId, double now) =>
        DamagePlayer(attacker, victim, 1000, false, killId, now, DamageKind.Bullet);

    public bool SelfTestUseStreak(Player p, StreakId id, Vector3 target, float yaw, double now) => ServerUseStreak(p, id, target, yaw, now);

    public void SelfTestStreakTick(double now) => ServerTickStreaks(now);

    public void SelfTestStreakBlast(Player owner, Vector3 ground, float radius, float damage, double now) =>
        StreakBlast(owner.Id, ground, radius, damage, KillStreaks.KillId(StreakId.MortarBarrage), now);

    public void SelfTestStreakSpawn(Player p) => StreakOnSpawn(p);

    public void SelfTestStreakRequest(Player p, StreakSet set) => StreakRequest(p, set);

    /// <summary>Fires the player's current weapon from their eyes along a direction (no spread), like a shot from their PC.</summary>
    public void SelfTestShoot(Player shooter, Vector3 dir, double now)
    {
        shooter.ShotClock = 0;
        ServerResolveShot(shooter, shooter.WeaponDef, shooter.Eye, dir, 0, 1, ServerTick, now);
    }

    public int SelfTestPendingStrikes => _pendingBlasts.Count;

    /// <summary>
    /// Smoke test (single player): gives the local player a Recon Sweep, Mortar Barrage and Sentry Turret, then calls all
    /// three in: the turret just ahead, the mortar ~16 m in front. Returns a line for the log.
    /// </summary>
    public string SmokeCallStreaks(double now)
    {
        if (!IsServer || Local is not { Alive: true } me)
            return "local player not alive, skipped";
        me.Streak.Slots = new StreakSet(StreakId.ReconSweep, StreakId.MortarBarrage, StreakId.SentryTurret).Sanitized();
        me.Streak.Ready = 0xFF;
        LookPitch = -0.12f;
        var fwd = World.FlatForward(LookYaw);
        var turret = ServerUseStreak(me, StreakId.SentryTurret, me.Position, LookYaw, now);
        var spot = KillStreaks.GroundAt(me.Position.X + fwd.X * 16, me.Position.Z + fwd.Z * 16, me.Position.Y);
        var mortar = ServerUseStreak(me, StreakId.MortarBarrage, spot, LookYaw, now);
        var sweep = ServerUseStreak(me, StreakId.ReconSweep, me.Position, LookYaw, now);
        return $"turret {turret}, mortar {mortar} at ({spot.X:0.0}, {spot.Y:0.0}, {spot.Z:0.0}), recon sweep {sweep}, entities {StreakEntities.Count}, shells {_pendingBlasts.Count}";
    }
}
