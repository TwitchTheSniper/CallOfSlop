using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The kill streaks. Loadouts save them by name, but packets send the byte, so new ones are appended. Ids must stay
/// below 17: they're bits of <see cref="StreakState.Ready"/> (a ushort), and kill-feed ids have room for 24 (see <see cref="KillStreaks.KillId"/>).
/// </summary>
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

    // Added later (KillStreaksMore.cs): appended so packet bytes never move.
    SmokeScreen,
    BallisticVests,
    HunterDrone,
    SupplyDrop,
    NapalmStrike,
    EmpBlast,
    Gunship,
    Juggernaut,
}

/// <summary>How a streak is called in.</summary>
public enum StreakAim : byte
{
    /// <summary>Straight away when you press its key.</summary>
    Instant,
    /// <summary>Look at a spot and press fire (Mortar Barrage).</summary>
    Spot,
    /// <summary>Look at a spot and press fire: the run passes over it along the way you're facing (Strafe Run, Napalm Strike), or a wall goes up across it (Smoke Screen).</summary>
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

    /// <summary>Turrets and drones: hit points (0 for streaks that aren't a world object).</summary>
    public int Health;

    /// <summary>
    /// Turrets and drones: multipliers on the damage they take from bullets (and melee) and from explosions
    /// (grenades, strikes, exploding props). Never a headshot multiplier: machines have no head.
    /// </summary>
    public float BulletDamageMult = 1f, ExplosiveDamageMult = 1f;

    /// <summary>Strikes: blast knockback (m/s added at each impact's centre, falling off to nothing at its radius; Knockback.cs). 0 = no push.</summary>
    public float Knockback;
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
    public ushort Ready;

    /// <summary>
    /// Kills stored towards each streak, indexed by <see cref="StreakId"/>. Each streak fills on its own: using one empties
    /// it so it can be earned again with fresh kills; dying empties the ones not earned yet.
    /// </summary>
    public readonly byte[] Progress = new byte[KillStreaks.MaxIds];

    /// <summary>
    /// Armour from Ballistic Vests or the Juggernaut suit (server-owned, sent in Msg.StreakState). It soaks damage before
    /// health and is gone on death. <see cref="ArmorHeavy"/>: the Juggernaut's (stops every bullet and blast, slows you);
    /// otherwise a vest's (body-shot bullets only).
    /// </summary>
    public int Armor;
    public bool ArmorHeavy;

    /// <summary>Server: bots pick random streaks once.</summary>
    public bool BotPicked;

    public static ushort Bit(StreakId id) => id == StreakId.None || (int)id >= KillStreaks.MaxIds ? (ushort)0 : (ushort)(1 << ((int)id - 1));

    public bool IsReady(StreakId id) => (Ready & Bit(id)) != 0;

    /// <summary>Kills stored towards this streak (its full cost once it's ready).</summary>
    public int ProgressOf(StreakId id) => id == StreakId.None || (int)id >= Progress.Length ? 0 : Progress[(int)id];

    /// <summary>Empties every streak that isn't earned yet (on death and spawn).</summary>
    public void ClearUnearned()
    {
        for (var i = 1; i < Progress.Length; i++)
            if ((Ready & Bit((StreakId)i)) == 0)
                Progress[i] = 0;
    }

    public void Reset()
    {
        Count = 0;
        Ready = 0;
        Array.Clear(Progress);
        Armor = 0;
        ArmorHeavy = false;
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
public static partial class KillStreaks
{
    /// <summary>Kill-feed ids 231-238 are the first eight streaks (230 + id); 239 is melee and 240+ throwables.</summary>
    public const byte KillIdBase = 230;

    /// <summary>Streaks with ids 9 and up use kill-feed ids 214-229 (214 + id - 9). Weapon ids stay below this (<see cref="Weapons.MaxWeapons"/>).</summary>
    public const byte ExtraKillIdBase = 214;

    /// <summary>Room for streak ids (the Progress array size, and the 16 bits of Ready plus None).</summary>
    public const int MaxIds = 17;

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
    /// <summary>
    /// Sentry hit points. Was 260 (11 Vanguard AR body shots). Now 520 and bullets do 60%: a Vanguard AR (24 dmg -> 14)
    /// needs 38 hits, a full 30-round magazine and then some; explosives do 150%, so grenades and strikes are the counter.
    /// </summary>
    public const int TurretHealth = 520;
    public const float TurretBulletMult = 0.6f, TurretExplosiveMult = 1.5f;
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
            Effect = "A sweep shows every enemy on the map to you and your team through walls for 10 seconds, in their team colour.",
            Color = new(0.2f, 0.85f, 0.95f) },
        new() { Id = StreakId.CounterSweep, Name = "Counter Sweep", Kills = 4, Short = "Jam enemy intel",
            Effect = "Jams the enemy for 15 seconds: their Recon Pulses and Recon Sweeps show nothing and their HUD fills with static.",
            Color = new(0.75f, 0.45f, 1f) },
        new() { Id = StreakId.Overdrive, Name = "Overdrive", Kills = 4, Short = "Faster move and reload",
            Effect = "For 12 seconds you move 35% faster and reload twice as fast. Ends if you die.",
            Color = new(1f, 0.8f, 0.2f) },
        new() { Id = StreakId.MortarBarrage, Name = "Mortar Barrage", Kills = 5, Short = "7 shells on a spot", Aim = StreakAim.Spot,
            Effect = "Look at a spot and press fire. After a short delay 7 shells land around it, each one whistling as it comes in. Walls and roofs shield you.",
            Color = new(1f, 0.5f, 0.2f), Knockback = 10f },
        new() { Id = StreakId.SentryTurret, Name = "Sentry Turret", Kills = 6, Short = "Auto-turret, 45 s",
            Effect = "Drops an automatic turret in front of you, facing where you look. It shoots enemies in front of it for 45 seconds. Armoured: bullets do reduced damage, explosives extra.",
            Color = new(0.55f, 0.9f, 0.45f), Health = TurretHealth, BulletDamageMult = TurretBulletMult, ExplosiveDamageMult = TurretExplosiveMult },
        new() { Id = StreakId.AttackDrone, Name = "Attack Drone", Kills = 8, Short = "Circling gun drone, 40 s",
            Effect = "A drone circles the map for 40 seconds, firing bursts at any enemy it can see. It can be shot down.",
            Color = new(0.95f, 0.35f, 0.35f), Health = DroneHealth },
        new() { Id = StreakId.StrafeRun, Name = "Strafe Run", Kills = 11, Short = "Jet strafes a line", Aim = StreakAim.Line,
            Effect = "Look at a spot and press fire: a jet strafes a 50 m line through it, along the way you're facing. Roofs shield you.",
            Color = new(1f, 0.3f, 0.6f), Knockback = 7f },

        // ---- Added later (KillStreaksMore.cs has their tuning and logic) ----
        new() { Id = StreakId.SmokeScreen, Name = "Smoke Screen", Kills = 3, Short = "Wall of smoke", Aim = StreakAim.Line,
            Effect = "Look at a spot and press fire: canisters lay a 24 m wall of smoke across it, square to the way you face, for 14 seconds. Nobody sees or locks on through it, turrets and bots included.",
            Color = new(0.75f, 0.78f, 0.82f) },
        new() { Id = StreakId.BallisticVests, Name = "Ballistic Vests", Kills = 4, Short = "Armour for your team",
            Effect = "You and every living teammate get a vest that soaks 60 damage from body shots. Headshots, explosions and melee go straight through it. Lost on death.",
            Color = new(0.45f, 0.7f, 1f) },
        new() { Id = StreakId.HunterDrone, Name = "Hunter Drone", Kills = 5, Short = "Seeks and explodes",
            Effect = "A small drone flies off after the nearest enemy and dives at them, beeping faster as it comes, then explodes. Shoot it down, get under a roof, or wear Flak Jacket. Cold Blooded hides you from it.",
            Color = new(1f, 0.6f, 0.3f), Health = HunterHealth, Knockback = 9f },
        new() { Id = StreakId.SupplyDrop, Name = "Supply Drop", Kills = 6, Short = "Crate: restock and +1 kill",
            Effect = "A crate parachutes down in front of you. Whoever touches it first, friend or enemy, gets full health, grenades and equipment, and one kill toward each streak they haven't earned yet.",
            Color = new(0.95f, 0.85f, 0.4f) },
        new() { Id = StreakId.NapalmStrike, Name = "Napalm Strike", Kills = 7, Short = "Jet leaves a fire line", Aim = StreakAim.Line,
            Effect = "Look at a spot and press fire: a jet drops napalm on a 30 m line through it, along the way you face. The fire burns for 8 seconds. Roofs shield you and Flak Jacket cuts the burns.",
            Color = new(1f, 0.42f, 0.1f), Knockback = 4f },
        new() { Id = StreakId.EmpBlast, Name = "EMP Blast", Kills = 9, Short = "Kill enemy electronics",
            Effect = "Fries enemy electronics: their Hunter Drones and tactical devices are destroyed, and for 12 seconds their turrets, drones and gunships can't fire and their HUD and recon are jammed. Strikes already called still land.",
            Color = new(0.4f, 0.9f, 1f) },
        new() { Id = StreakId.Gunship, Name = "Gunship", Kills = 13, Short = "Cannon gunship, 35 s",
            Effect = "A gunship circles high over the map for 35 seconds, shelling any enemy it can see with explosive cannon rounds. Shoot it down (explosives hurt it most), hide under roofs, or wear Cold Blooded.",
            Color = new(0.7f, 0.95f, 0.35f), Health = GunshipHealth, BulletDamageMult = GunshipBulletMult, ExplosiveDamageMult = GunshipExplosiveMult, Knockback = 6f },
        new() { Id = StreakId.Juggernaut, Name = "Juggernaut", Kills = 15, Short = "400 armour, slow",
            Effect = "Suit up: 400 armour soaks every bullet and blast (explosives eat it 50% faster), but you move 25% slower. One melee hit still kills you. Lasts until the armour breaks or you die.",
            Color = new(0.9f, 0.25f, 0.25f) },
    ];

    public static StreakDef Get(StreakId id) => All[(int)id < All.Length ? (int)id : 0];

    public static bool IsValid(StreakId id) => id != StreakId.None && (int)id < All.Length;

    public static StreakDef? Find(string name) =>
        All.FirstOrDefault(s => s.Id != StreakId.None && s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Unlocked at level 1.</summary>
    public static readonly StreakId[] Starters = [StreakId.ReconSweep, StreakId.Overdrive, StreakId.MortarBarrage];

    /// <summary>The rest, in unlock order (spread evenly from level 5 to two levels before max).</summary>
    // The first four kept their old unlock level or got an earlier one when the later streaks were slotted in between.
    public static readonly StreakId[] UnlockOrder =
    [
        StreakId.CounterSweep, StreakId.SmokeScreen, StreakId.BallisticVests, StreakId.SentryTurret, StreakId.HunterDrone, StreakId.SupplyDrop,
        StreakId.NapalmStrike, StreakId.AttackDrone, StreakId.EmpBlast, StreakId.Gunship, StreakId.StrafeRun, StreakId.Juggernaut,
    ];

    public static int UnlockLevel(StreakId id)
    {
        if (id == StreakId.None || Starters.Contains(id))
            return 1;
        var i = Array.IndexOf(UnlockOrder, id);
        return i < 0 ? Progress.MaxLevel : 5 + (int)MathF.Round(i * (Progress.MaxLevel - 7) / (float)(UnlockOrder.Length - 1));
    }

    public static byte KillId(StreakId id) => (byte)id <= 8 ? (byte)(KillIdBase + (byte)id) : (byte)(ExtraKillIdBase + (byte)id - 9);

    /// <summary>Was this kill made by a streak? (Those never count toward the next streak.)</summary>
    public static bool IsStreakKill(byte killId) => killId >= KillIdBase && killId < Game.MeleeKillId || killId >= ExtraKillIdBase && killId < KillIdBase;

    public static StreakId FromKillId(byte killId) =>
        killId >= KillIdBase && killId < Game.MeleeKillId ? (StreakId)(killId - KillIdBase)
        : killId >= ExtraKillIdBase && killId < KillIdBase ? (StreakId)(killId - ExtraKillIdBase + 9) : StreakId.None;

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
        else if (id == StreakId.NapalmStrike)
        {
            var dir = World.FlatForward(yaw);
            for (var i = 0; i < NapalmHits; i++)
            {
                var off = -NapalmLength / 2 + i * NapalmLength / (NapalmHits - 1);
                var side = (Next() - 0.5f) * 1.2f;
                var p = target + dir * off + new Vector3(-dir.Z, 0, dir.X) * side;
                list.Add(new StrikeShell(NapalmDelay + (off + NapalmLength / 2) / NapalmSpeed, GroundAt(p.X, p.Z, target.Y)));
            }
        }
        else if (id == StreakId.SmokeScreen)
        {
            var across = World.FlatForward(yaw + MathF.PI / 2);
            for (var i = 0; i < SmokeScreenClouds; i++)
            {
                var off = -SmokeScreenLength / 2 + i * SmokeScreenLength / (SmokeScreenClouds - 1);
                var p = target + across * off;
                list.Add(new StrikeShell(SmokeScreenDelay + i * 0.12f + Next() * 0.1f, GroundAt(p.X, p.Z, target.Y)));
            }
        }
        return list;
    }

    /// <summary>Where something falling from the sky at (x, z) lands: the first surface below, roofs included.</summary>
    public static Vector3 GroundAt(float x, float z, float nearY)
    {
        x = Math.Clamp(x, -World.Map.HalfX + 0.5f, World.Map.HalfX - 0.5f);
        z = Math.Clamp(z, -World.Map.HalfZ + 0.5f, World.Map.HalfZ - 0.5f);
        if (World.Map.Boundary is { } boundary && !boundary.Inside(x, z, 0.5f))
        {
            var c = boundary.Clamp(new Vector2(x, z), 0.5f); // inside a shaped map
            x = c.X;
            z = c.Y;
        }
        var top = MathF.Max(nearY, 0) + 40f;
        var d = World.Query.Raycast(new Vector3(x, top, z), -Vector3.UnitY, top + 5f, QueryFilter.All);
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

    /// <summary>Server -> one client: your streak count, earned streaks, carried slots and armour (protocol 29: Ready is 16 bits, armour added).</summary>
    public static void WriteState(PacketWriter w, StreakState s) =>
        w.Begin(Msg.StreakState).U8(s.Count).U16(s.Ready).U8((byte)s.Slots.A).U8((byte)s.Slots.B).U8((byte)s.Slots.C)
            .U8((byte)s.ProgressOf(s.Slots.A)).U8((byte)s.ProgressOf(s.Slots.B)).U8((byte)s.ProgressOf(s.Slots.C))
            .U16((ushort)Math.Clamp(s.Armor, 0, 65535)).U8((byte)(s.ArmorHeavy ? 1 : 0));

    public const int StateBytes = 12;

    public static void ReadState(ref PacketReader r, StreakState s)
    {
        s.Count = r.U8();
        s.Ready = r.U16();
        var a = (StreakId)r.U8();
        var b = (StreakId)r.U8();
        var c = (StreakId)r.U8();
        Array.Clear(s.Progress);
        foreach (var id in new[] { a, b, c })
        {
            var got = r.U8();
            if (id != StreakId.None && (int)id < s.Progress.Length)
                s.Progress[(int)id] = got;
        }
        s.Slots = new StreakSet(a, b, c).Sanitized();
        s.Armor = r.U16();
        s.ArmorHeavy = (r.U8() & 1) != 0;
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
    private ushort _lastReady;
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
        if (victim.Streak.Count != 0 || victim.Streak.Armor != 0)
        {
            victim.Streak.Count = 0;
            victim.Streak.ClearUnearned();
            victim.Streak.Armor = 0; // vests and the Juggernaut suit are lost on death
            victim.Streak.ArmorHeavy = false;
            SendStreakState(victim);
        }
        // Classic rules: streak kills don't chain, suicides and kills after your own death don't count.
        if (!StreaksEnabled || attacker == victim || !attacker.Alive || KillStreaks.IsStreakKill(killId))
            return;
        var s = attacker.Streak;
        if (s.Count < 255)
            s.Count++;
        // Each streak not yet earned stores the kill; reaching its cost earns it. Ready ones wait until used.
        foreach (var id in s.Slots.Ids)
        {
            if (s.IsReady(id))
                continue;
            var kills = Perks.StreakCost(attacker.Perks, KillStreaks.Get(id).Kills); // Hardline: one less
            if (s.Progress[(int)id] < kills)
                s.Progress[(int)id]++;
            if (s.Progress[(int)id] >= kills)
                s.Ready |= StreakState.Bit(id);
        }
        SendStreakState(attacker);
    }

    /// <summary>On (re)spawn: the requested streaks come into effect. Earned streaks no longer in the loadout are dropped.</summary>
    private void StreakOnSpawn(Player p)
    {
        var s = p.Streak;
        if (p.Bot != null)
            BotStreakPick(p, s); // once, and again when the bot's gear level changes (BotGear.cs)
        s.Slots = s.Want.Sanitized();
        s.Count = 0;
        var keep = (ushort)0;
        foreach (var id in s.Slots.Ids)
            keep |= StreakState.Bit(id);
        s.Ready &= keep;
        s.ClearUnearned();
        s.Armor = 0;
        s.ArmorHeavy = false;
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
    private StreakSet RandomStreaks(int level)
    {
        // Every streak still takes its random number (the shared sequence is the same at every level); locked ones are skipped.
        var pool = KillStreaks.All.Where(s => s.Id != StreakId.None).Select(s => s.Id).OrderBy(_ => _rng.Next()).Where(id => BotGear.Streak(id, level)).Take(3).ToArray();
        StreakId At(int i) => i < pool.Length ? pool[i] : StreakId.None;
        return new StreakSet(At(0), At(1), At(2)).Sanitized();
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
        ServerTickFireZones(now); // Napalm Strike burns (KillStreaksMore.cs)
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
        FireZones.Clear();
    }

    /// <summary>Server: someone uses an earned streak. Everything is checked here; the client only asks.</summary>
    /// <summary>Kill streaks that don't fly anything over the map, so they work the same on a planet (point gravity).</summary>
    public static bool WorksOnPlanet(StreakId id) =>
        id is StreakId.ReconSweep or StreakId.CounterSweep or StreakId.Overdrive or StreakId.BallisticVests or StreakId.EmpBlast or StreakId.Juggernaut;

    private bool ServerUseStreak(Player p, StreakId id, Vector3 target, float yaw, double now)
    {
        var s = p.Streak;
        if (!StreaksEnabled || !p.Alive || Phase != MatchPhase.Playing || !KillStreaks.IsValid(id) || !s.Slots.Has(id) || !s.IsReady(id))
            return false;
        if (GravityFrames.Curved && !WorksOnPlanet(id))
            return false; // a planet: strikes, drones, turrets and drops fly and fall on flat-world paths (kept for later), so they wait
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
            if (World.Map.Boundary is { } boundary)
                target = boundary.ClampFeet(target, 0.5f);
            pos = target;
            var (radius, damage) = KillStreaks.StrikeBlast(id); // Smoke Screen: none
            if (damage > 0)
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
        else
        {
            pos = ServerUseMoreStreak(p, id, yaw, now); // the later streaks (KillStreaksMore.cs)
        }
        s.Ready &= (ushort)~StreakState.Bit(id);
        s.Progress[(int)id] = 0; // used: empty it so fresh kills earn it again
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
                var amount = BlastDamage(center, victim.Chest, radius, maxDamage) * World.BlastPlayerScale; // same kill radius at 120 health
                if (victim.Sim.Stance == Stance.Prone)
                    amount *= 0.7f;
                if ((int)MathF.Round(amount) <= 0)
                    continue;
                flags |= 1;
                if (DamagePlayer(owner, victim, (int)MathF.Round(amount), false, killId, now, DamageKind.Explosive))
                    flags |= 4;
            }
            BlastKnockback(owner, center, radius, KillStreaks.Get(KillStreaks.FromKillId(killId)).Knockback, enemiesOnly: true); // pushes the caller's enemies (Knockback.cs)
            StreakBlastEntities(owner, center, radius, maxDamage, now);
            BlastTactical(owner, center, radius, maxDamage, now); // enemy tactical devices (GameTactical.cs)
            SandboxBlast(center, radius, maxDamage); // Sandbox: construct parts in range (GameSandbox.cs)
        }
        BlastProps(owner, center, radius, maxDamage, PropDamage.Streak, now); // breakable props (GameProps.cs)
        BlastBuildings(owner, center, radius, maxDamage, PropDamage.Streak, now); // building doors and windows (GameBuildings.cs)
        BlastStructures(owner, center, radius, maxDamage, StructureHit.Streak, killId, now); // destructible buildings (GameDestruction.cs)
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
        if (dist > 0.3f && World.Query.Raycast(center, to / dist, dist, QueryFilter.All) < dist - 0.05f)
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
            case StreakId.NapalmStrike:
            case StreakId.SmokeScreen:
            {
                var plan = KillStreaks.Plan(id, pos, yaw, seed);
                foreach (var shell in plan)
                    Strikes.Add(new StrikeFx { Kind = id, Owner = owner, Pos = shell.Pos, At = now + shell.Delay });
                var last = plan.Count > 0 ? plan.Max(x => x.Delay) : 3f;
                StrikeMarkers.Add(new StrikeMarker(id, owner, pos, yaw, now, now + last + 0.5));
                OnStrikePlanned(id, owner, plan, now); // napalm fire and smoke clouds, both sides from the same plan (KillStreaksMore.cs)
                break;
            }
            case StreakId.EmpBlast:
                StreakEffects.Add(new StreakEffect(id, owner, now, now + KillStreaks.EmpSeconds));
                Audio.Effect(Sfx.EmpBurst, mine ? 0.8f : 0.65f);
                break;
            case StreakId.Juggernaut:
                StreakEffects.Add(new StreakEffect(id, owner, now, now + KillStreaks.JuggernautMaxSeconds));
                break;
        }
    }

    /// <summary>Is <paramref name="p"/> jammed by an enemy's Counter Sweep?</summary>
    public bool StreakJammed(Player? p, double now)
    {
        if (p == null)
            return false;
        foreach (var e in StreakEffects)
            if (e.Kind is StreakId.CounterSweep or StreakId.EmpBlast && now < e.Until && Players.TryGetValue(e.Owner, out var o) && StreakIsEnemy(o, p))
                return true;
        return false;
    }

    /// <summary>
    /// Is <paramref name="p"/> shown to the local player by a friendly Recon Sweep? Decided from the VIEWER's side, not the
    /// caller's: a sweep runs for the viewer when they called it or a teammate did (team modes: the whole team benefits;
    /// free-for-all: only the caller), and it shows only the viewer's enemies (<see cref="AreEnemies"/>), so nobody ever
    /// sees their own team revealed. Team data is read live each frame, so team changes and late joins show correctly.
    /// No local player (not joined yet) means nothing is revealed.
    /// </summary>
    public bool StreakSweepReveals(Player p, double now) => Local is { } me && AreEnemies(me, p) && StreakSweepRunsFor(me, now);

    /// <summary>Is a Recon Sweep called by <paramref name="viewer"/> or one of their teammates running?</summary>
    public bool StreakSweepRunsFor(Player viewer, double now)
    {
        foreach (var e in StreakEffects)
            if (e.Kind == StreakId.ReconSweep && now < e.Until && Players.TryGetValue(e.Owner, out var o) && (o == viewer || !StreakIsEnemy(o, viewer)))
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
    public float StreakReloadMult(Player? p, double now) => (p != null && OverdriveActive(p, now) ? KillStreaks.OverdriveReloadMult : 1f) * ZombieReloadMult(p); // and Zombies' Swiftload Seltzer

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
        // Overdrive and the Juggernaut suit end when their owner dies; everything else just runs out.
        // (Plain loops, not RemoveAll with a lambda: that would allocate a closure every frame.)
        for (var i = StreakEffects.Count - 1; i >= 0; i--)
        {
            var e = StreakEffects[i];
            if (now > e.Until || (e.Kind is StreakId.Overdrive or StreakId.Juggernaut && Players.TryGetValue(e.Owner, out var o) && !o.Alive)
                || !Players.ContainsKey(e.Owner))
                StreakEffects.RemoveAt(i);
        }
        for (var i = StrikeMarkers.Count - 1; i >= 0; i--)
            if (now > StrikeMarkers[i].Until)
                StrikeMarkers.RemoveAt(i);

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
            if (!s.Done && now >= s.At && s.Kind == StreakId.SmokeScreen)
            {
                s.Done = true; // a canister popping (the cloud itself is in Smokes already, see OnStrikePlanned)
                Audio.Effect3D(Sfx.SmokeHiss, s.Pos, 0.7f);
            }
            if (!s.Done && now >= s.At)
            {
                s.Done = true;
                var radius = KillStreaks.StrikeBlast(s.Kind).Radius;
                Blasts.Add(new Blast(s.Pos + new Vector3(0, 0.3f, 0), now, radius, ThrowableKind.Frag));
                var push = KillStreaks.Get(s.Kind).Knockback;
                Ragdolls.Blast(s.Pos + new Vector3(0, 0.3f, 0), radius, push, now); // corpses fly (cosmetic, Source/Render/Ragdolls.cs)
                PropDebris.Push(s.Pos + new Vector3(0, 0.3f, 0), radius, push);
                Audio.Effect3D(s.Kind == StreakId.NapalmStrike ? Sfx.NapalmIgnite : Sfx.Explosion, s.Pos, s.Kind == StreakId.MortarBarrage ? 1f : 0.75f);
                ImpactDecals.OnBlast(World.Map, s.Pos, s.Owner, now); // a scorch where it landed (Source/Decals)
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
            if (m.Kind is StreakId.StrafeRun or StreakId.NapalmStrike)
            {
                var pass = m.Born + KillStreaks.JetPassTime(m.Kind) - 0.6;
                if (now >= pass && now - dt < pass)
                    Audio.Effect3D(Sfx.JetFlyby, m.Pos + new Vector3(0, KillStreaks.StrafeAltitude * 0.5f, 0), 1f);
            }
        }

        UpdateStreakEntitiesVisual(now, dt);
        UpdateMoreStreakEffects(now, dt); // fire zones, hunter beeps, gunship drone (KillStreaksMore.cs)

        // Local: aim point while targeting, and banners for streaks you earn.
        if (Local is not { } local)
            return;
        if (StreakTargeting != StreakId.None)
        {
            if (!local.Alive || !local.Streak.IsReady(StreakTargeting) || Phase != MatchPhase.Playing)
                StreakTargeting = StreakId.None;
            var dir = LookDir(LookYaw, LookPitch);
            var d = World.Query.Raycast(local.RenderEye, dir, KillStreaks.MaxCallRange, QueryFilter.All);
            StreakAimValid = d < KillStreaks.MaxCallRange - 0.01f;
            StreakAimPoint = local.RenderEye + dir * d;
        }
        var ready = local.Streak.Ready;
        var fresh = (ushort)(ready & ~_lastReady);
        if (fresh != 0 && StreaksEnabled)
        {
            var id = FreshStreak(local.Streak.Slots.Ids, fresh);
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

    /// <summary>
    /// The last of <paramref name="ids"/> that just became ready. (Its own method: a lambda over <c>fresh</c> in
    /// UpdateStreakEffects made the compiler allocate a closure at the top of that method, every frame.)
    /// </summary>
    private static StreakId FreshStreak(IEnumerable<StreakId> ids, ushort fresh) =>
        ids.LastOrDefault(x => (fresh & StreakState.Bit(x)) != 0);

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
                else
                    OnMoreStreakEvent(e.Evt, e.Owner, e.Id, e.Pos, e.Yaw, e.Other, now); // effect ended, crate taken, streak blast (KillStreaksMore.cs)
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

}
