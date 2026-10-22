using System.Numerics;

namespace CallOfSlop;

/// <summary>The three perk slots of a loadout. You carry one perk from each.</summary>
public enum PerkCategory : byte
{
    Movement,
    Survival,
    Damage,
}

public enum PerkId : byte
{
    None,

    // Movement
    LowProfile,
    HeadStart,
    Parkour,
    Afterburner,

    // Survival
    FlakJacket,
    LastStand,
    Bloodthirst,
    Entrenched,

    // Damage
    PointBlank,
    Deadeye,
    Flanker,
    Opener,

    // Added later: appended so the ids already in saves and packets never change. Perks.In lists them by unlock level.
    DeadSilence, // Movement
    Ghost, // Movement
    Stalker, // Movement
    Lightweight, // Movement
    Hardline, // Survival
    Scavenger, // Survival
    ColdBlooded, // Survival
    QuickFix, // Survival
    SleightOfHand, // Damage
    SteadyAim, // Damage
    TightGrip, // Damage
    Demolitions, // Damage
}

/// <summary>What a perk does. Every perk only works in one situation, but it's very strong there.</summary>
public sealed class PerkDef
{
    public required PerkId Id;
    public required PerkCategory Category;
    public required string Name;
    public required string When; // the situation, short: shown on cards
    public required string Effect; // what it does in that situation
}

/// <summary>The perks the player is carrying: one per category (None = empty slot).</summary>
public readonly record struct PerkSet(PerkId Movement, PerkId Survival, PerkId Damage)
{
    public static readonly PerkSet Empty = new(PerkId.None, PerkId.None, PerkId.None);

    public bool Has(PerkId id) => id != PerkId.None && (Movement == id || Survival == id || Damage == id);

    public PerkId this[PerkCategory c] => c switch
    {
        PerkCategory.Movement => Movement,
        PerkCategory.Survival => Survival,
        _ => Damage,
    };

    public PerkSet With(PerkCategory c, PerkId id) => c switch
    {
        PerkCategory.Movement => this with { Movement = id },
        PerkCategory.Survival => this with { Survival = id },
        _ => this with { Damage = id },
    };

    /// <summary>Drops anything in the wrong slot (e.g. from a bad packet or an edited save file).</summary>
    public PerkSet Sanitized() => new(
        Perks.Get(Movement).Category == PerkCategory.Movement ? Movement : PerkId.None,
        Perks.Get(Survival).Category == PerkCategory.Survival ? Survival : PerkId.None,
        Perks.Get(Damage).Category == PerkCategory.Damage ? Damage : PerkId.None);
}

public static class Perks
{
    // ---- Tuning ----------------------------------------------------------------------
    public const float LowProfileSpeed = 1.8f; // crouch 50% -> 90% of walking speed, prone 22% -> 40%
    public const byte BoostTicks = 240; // Head Start / Afterburner: 4 s
    /// <summary>After an Afterburner boost ends, kills can't start another for this long (6 s). Kills during a boost don't refresh it.</summary>
    public const uint AfterburnerCooldownTicks = 360;
    public const float BoostSpeed = 1.35f;
    public const float ParkourReach = 2.3f; // highest ledge you can climb (normally 1.7 m)
    public const float ParkourJump = 1.25f; // jump speed multiplier (about 1.55 m high instead of 1.0 m)
    public const float FlakMult = 0.25f; // explosive damage taken
    public const float FlakKnockMult = 0.4f; // blast knockback taken (Knockback.cs)
    // Last Stand: the lethal hit leaves you on 1 HP and a shield blocks the next LastStandCharges bullet hits
    // (a shotgun blast is one hit however many pellets land; melee and explosions go straight through it).
    // It never saves you from a melee hit: melee kills in one hit (Melee.cs).
    // The shield runs out after LastStandShieldTime even if charges are left, so it can't stack with a full regen.
    public const byte LastStandCharges = 3;
    public const double LastStandShieldTime = 8.0;
    public const float EntrenchedMult = 0.6f; // bullet damage taken while dug in
    public const float PointBlankRange = 8f, PointBlankMult = 1.4f;
    public const float DeadeyeRange = 30f, DeadeyeMult = 1.4f;
    public const float FlankerMult = 1.6f;
    public const float OpenerMult = 1.5f;

    // Dead Silence: other players hear your steps (and jumps and landings) this much quieter, within this share of the usual range.
    public const float DeadSilenceGain = 0.25f, DeadSilenceRange = 0.5f;
    /// <summary>Bots notice an enemy outside their field of view this close (Dead Silence: <see cref="DeadSilenceBotSense"/>).</summary>
    public const float BotCloseSense = 7f, DeadSilenceBotSense = 3f;
    public const float GhostMinSpeed = 1f; // Ghost: moving faster than this (m/s, flat) hides you from Recon
    public const float StalkerAdsSpeed = 0.95f; // Stalker: speed while aiming (normally World.AdsSpeedMult, 0.7)
    public const float LightweightMinMobility = 1f; // Lightweight: your gun never slows you below this
    public const int HardlineDiscount = 1, HardlineMinKills = 2;
    public const int ScavengerGrenades = 1, ScavengerEquipment = 1; // given back per kill, up to the per-life amount
    public const double QuickFixDelay = 2.5; // Quick Fix: regen starts this long after the last damage (normally Regen.Delay, 5 s)
    public const float SleightReloadMult = 0.65f;
    public const float SteadyAimHipMult = 0.65f; // hip-fire spread (fades out as you aim down sights)
    public const float TightGripRecoilMult = 0.7f; // recoil while aiming (fades in as you aim down sights)
    public const float DemolitionsRadiusMult = 1.25f; // Frag and Impact blast radius (the damage curve stretches with it)

    public static readonly PerkDef[] All =
    [
        new() { Id = PerkId.None, Category = (PerkCategory)255, Name = "None", When = "", Effect = "Leave this slot empty." },

        new() { Id = PerkId.LowProfile, Category = PerkCategory.Movement, Name = "Low Profile", When = "While crouched or prone",
            Effect = "Move 80% faster while crouched or prone, and Recon Pulses can't reveal you while you're low." },
        new() { Id = PerkId.HeadStart, Category = PerkCategory.Movement, Name = "Head Start", When = "Right after you spawn",
            Effect = "Move 35% faster for the first 4 seconds of every life. Get back into the fight first." },
        new() { Id = PerkId.Parkour, Category = PerkCategory.Movement, Name = "Parkour", When = "Around walls, windows and roofs",
            Effect = "Reach ledges 0.6 m higher, climb twice as fast and jump about 55% higher: get onto walls and roofs nobody else can." },
        new() { Id = PerkId.Afterburner, Category = PerkCategory.Movement, Name = "Afterburner", When = "Right after a kill",
            Effect = "A kill gives you 35% more speed for 4 seconds. Kills during the boost don't extend it, and it needs 6 seconds to recharge after it ends." },

        new() { Id = PerkId.FlakJacket, Category = PerkCategory.Survival, Name = "Flak Jacket", When = "Against grenades",
            Effect = "Take 75% less damage from explosions, including your own grenades, blasts throw you 60% less far, and flashbangs and stun grenades hit you half as hard." },
        new() { Id = PerkId.LastStand, Category = PerkCategory.Survival, Name = "Last Stand", When = "Once per life",
            Effect = "The first bullet or blast that would kill you leaves you on 1 HP instead, and a shield blocks the next 3 bullet hits (a shotgun blast counts as one) for up to 8 seconds. Explosions go straight through the shield, and it never saves you from melee." },
        new() { Id = PerkId.Bloodthirst, Category = PerkCategory.Survival, Name = "Bloodthirst", When = "Right after a kill",
            Effect = "Every kill instantly heals you back to full health." },
        new() { Id = PerkId.Entrenched, Category = PerkCategory.Survival, Name = "Entrenched", When = "Crouched or prone and holding still",
            Effect = "Take 40% less bullet damage while you're crouched or prone and not moving." },

        new() { Id = PerkId.PointBlank, Category = PerkCategory.Damage, Name = "Point Blank", When = "Within 8 m",
            Effect = "Your bullets and melee hit 40% harder against enemies within 8 metres." },
        new() { Id = PerkId.Deadeye, Category = PerkCategory.Damage, Name = "Deadeye", When = "Beyond 30 m",
            Effect = "Your bullets hit 40% harder against enemies 30 metres or more away." },
        new() { Id = PerkId.Flanker, Category = PerkCategory.Damage, Name = "Flanker", When = "Shooting enemies from behind",
            Effect = "Your bullets and melee hit 60% harder when they strike an enemy in the back." },
        new() { Id = PerkId.Opener, Category = PerkCategory.Damage, Name = "Opener", When = "Against enemies on full health",
            Effect = "Your bullets and melee hit 50% harder against enemies who haven't been hurt yet. Win the first exchange." },

        // ---- Added later (same order as the enum) ----
        new() { Id = PerkId.DeadSilence, Category = PerkCategory.Movement, Name = "Dead Silence", When = "Sneaking up on enemies",
            Effect = "Everyone else hears your footsteps, jumps and landings 75% quieter and only from half as far. Bots only notice you behind them within 3 m instead of 7 m." },
        new() { Id = PerkId.Ghost, Category = PerkCategory.Movement, Name = "Ghost", When = "While on the move",
            Effect = "Recon Pulses and Recon Sweeps can't reveal you while you're moving, and Motion Sensors never ping you. Stop and recon sees you as normal." },
        new() { Id = PerkId.Stalker, Category = PerkCategory.Movement, Name = "Stalker", When = "Aiming down sights",
            Effect = "Aiming down sights barely slows you: you keep 95% of your speed instead of 70%. Strafe while you aim." },
        new() { Id = PerkId.Lightweight, Category = PerkCategory.Movement, Name = "Lightweight", When = "Carrying a heavy gun",
            Effect = "Your gun never slows you down: LMGs, snipers and other heavy guns move at full speed. No effect with light guns." },

        new() { Id = PerkId.Hardline, Category = PerkCategory.Survival, Name = "Hardline", When = "Earning kill streaks",
            Effect = "Every kill streak costs one kill less (never below 2): Recon Sweep at 2 kills, Sentry Turret at 5, Strafe Run at 10." },
        new() { Id = PerkId.Scavenger, Category = PerkCategory.Survival, Name = "Scavenger", When = "Right after a kill",
            Effect = "Every kill gives you back one grenade and one piece of equipment, up to what you spawn with." },
        new() { Id = PerkId.ColdBlooded, Category = PerkCategory.Survival, Name = "Cold Blooded", When = "Against turrets and drones",
            Effect = "Enemy Sentry Turrets and Attack Drones can't lock onto you. Their stray bullets still hurt, and players see you as normal." },
        new() { Id = PerkId.QuickFix, Category = PerkCategory.Survival, Name = "Quick Fix", When = "Between fights",
            Effect = "Your health starts coming back 2.5 seconds after you last took or dealt damage, instead of 5." },

        new() { Id = PerkId.SleightOfHand, Category = PerkCategory.Damage, Name = "Sleight of Hand", When = "While reloading",
            Effect = "Reload 35% faster. Stacks with Overdrive." },
        new() { Id = PerkId.SteadyAim, Category = PerkCategory.Damage, Name = "Steady Aim", When = "Firing from the hip",
            Effect = "Your hip-fire spread is 35% tighter. Aiming down sights is unchanged." },
        new() { Id = PerkId.TightGrip, Category = PerkCategory.Damage, Name = "Tight Grip", When = "Aiming down sights",
            Effect = "30% less recoil while aiming down sights. Hip fire kicks as usual." },
        new() { Id = PerkId.Demolitions, Category = PerkCategory.Damage, Name = "Demolitions", When = "With grenades",
            Effect = "Your Frag and Impact grenades blast 25% wider (a Frag hurts out to 11 m instead of 9 m). Damage at the centre is unchanged." },
    ];

    public static PerkDef Get(PerkId id) => All[(int)id < All.Length ? (int)id : 0];

    public static PerkDef? Find(string name) =>
        All.FirstOrDefault(p => p.Id != PerkId.None && p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The perks of one category, starters first, then in unlock order.</summary>
    public static IEnumerable<PerkDef> In(PerkCategory c) => All.Where(p => p.Category == c).OrderBy(p => Progress.PerkLevel(p.Id));

    public static string CategoryName(PerkCategory c) => c switch
    {
        PerkCategory.Movement => "Movement",
        PerkCategory.Survival => "Survival",
        _ => "Damage",
    };

    /// <summary>Colour per category (cards, icons, HUD).</summary>
    public static Vector3 Color(PerkCategory c) => c switch
    {
        PerkCategory.Movement => new(0.35f, 0.75f, 1f),
        PerkCategory.Survival => new(0.4f, 0.95f, 0.5f),
        _ => new(1f, 0.4f, 0.35f),
    };

    /// <summary>The perks everyone starts with: one per category.</summary>
    public static readonly PerkId[] Starters = [PerkId.LowProfile, PerkId.FlakJacket, PerkId.PointBlank];

    /// <summary>
    /// Damage perks of <paramref name="attacker"/> for one hit (bullets or melee).
    /// <paramref name="dir"/> is the direction the hit travelled, <paramref name="victimYaw"/> where the victim faced.
    /// </summary>
    public static float AttackMult(PerkSet attacker, float distance, Vector3 dir, float victimYaw, int victimHealth, bool melee)
    {
        var mult = 1f;
        if (attacker.Has(PerkId.PointBlank) && distance <= PointBlankRange)
            mult *= PointBlankMult;
        if (attacker.Has(PerkId.Deadeye) && !melee && distance >= DeadeyeRange)
            mult *= DeadeyeMult;
        if (attacker.Has(PerkId.Flanker) && IsFromBehind(dir, victimYaw))
            mult *= FlankerMult;
        if (attacker.Has(PerkId.Opener) && victimHealth >= World.MaxHealth)
            mult *= OpenerMult;
        return mult;
    }

    /// <summary>Did a hit travelling along <paramref name="dir"/> strike someone facing <paramref name="victimYaw"/> in the back?</summary>
    public static bool IsFromBehind(Vector3 dir, float victimYaw)
    {
        var flat = new Vector3(dir.X, 0, dir.Z);
        if (flat.LengthSquared() < 1e-6f)
            return false;
        // The hit travels the same way the victim is facing: it came from behind them.
        return Vector3.Dot(Vector3.Normalize(flat), World.FlatForward(victimYaw)) > 0.5f;
    }

    // ---- Handling, movement and utility perks (shared by the server, bots and the local player) ----

    /// <summary>Reload time multiplier (Sleight of Hand).</summary>
    public static float ReloadMult(PerkSet p) => p.Has(PerkId.SleightOfHand) ? SleightReloadMult : 1f;

    /// <summary>Spread multiplier at aim blend <paramref name="aimT"/> (0 hip, 1 aimed): Steady Aim tightens hip fire only.</summary>
    public static float SpreadMult(PerkSet p, float aimT) =>
        p.Has(PerkId.SteadyAim) ? float.Lerp(SteadyAimHipMult, 1f, Math.Clamp(aimT, 0f, 1f)) : 1f;

    /// <summary>Recoil multiplier at aim blend <paramref name="aimT"/>: Tight Grip steadies aimed fire only.</summary>
    public static float RecoilMult(PerkSet p, float aimT) =>
        p.Has(PerkId.TightGrip) ? float.Lerp(1f, TightGripRecoilMult, Math.Clamp(aimT, 0f, 1f)) : 1f;

    /// <summary>Movement speed factor from the gun in hand and aiming (Lightweight, Stalker). Sent as the input's SpeedPct.</summary>
    public static float MoveSpeed(PerkSet p, float mobility, bool ads)
    {
        var m = p.Has(PerkId.Lightweight) ? MathF.Max(mobility, LightweightMinMobility) : mobility;
        return m * (ads ? p.Has(PerkId.Stalker) ? StalkerAdsSpeed : World.AdsSpeedMult : 1f);
    }

    /// <summary>Blast radius multiplier for the thrower's Frag and Impact grenades (Demolitions).</summary>
    public static float BlastRadiusMult(PerkSet p) => p.Has(PerkId.Demolitions) ? DemolitionsRadiusMult : 1f;

    /// <summary>Seconds without damage before health regenerates (Quick Fix shortens it).</summary>
    public static double RegenDelay(PerkSet p) => p.Has(PerkId.QuickFix) ? QuickFixDelay : Regen.Delay;

    /// <summary>How close a bot notices this enemy outside its field of view (Dead Silence makes it shorter).</summary>
    public static float BotSenseRange(PerkSet target) => target.Has(PerkId.DeadSilence) ? DeadSilenceBotSense : BotCloseSense;

    /// <summary>
    /// Is a player with these perks hidden from Recon Pulses and Sweeps right now? Low Profile: while crouched or prone.
    /// Ghost: while moving faster than <see cref="GhostMinSpeed"/>.
    /// </summary>
    public static bool HiddenFromRecon(PerkSet p, Stance stance, Vector3 velocity) =>
        p.Has(PerkId.LowProfile) && stance != Stance.Stand
        || p.Has(PerkId.Ghost) && new Vector2(velocity.X, velocity.Z).LengthSquared() > GhostMinSpeed * GhostMinSpeed;

    /// <summary>Can enemy Sentry Turrets and Attack Drones lock onto this player? (Not with Cold Blooded.)</summary>
    public static bool MachinesCanTarget(PerkSet p) => !p.Has(PerkId.ColdBlooded);

    /// <summary>Kills a streak costs for someone with these perks (Hardline: one less, never below <see cref="HardlineMinKills"/>).</summary>
    public static int StreakCost(PerkSet p, int kills) =>
        p.Has(PerkId.Hardline) ? Math.Max(Math.Min(kills, HardlineMinKills), kills - HardlineDiscount) : kills;
}
