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
    public const float BoostSpeed = 1.35f;
    public const float ParkourReach = 2.3f; // highest ledge you can climb (normally 1.7 m)
    public const float ParkourJump = 1.25f; // jump speed multiplier (about 1.55 m high instead of 1.0 m)
    public const float FlakMult = 0.25f; // explosive damage taken
    public const double LastStandImmunity = 1.5;
    public const float EntrenchedMult = 0.6f; // bullet damage taken while dug in
    public const float PointBlankRange = 8f, PointBlankMult = 1.4f;
    public const float DeadeyeRange = 30f, DeadeyeMult = 1.4f;
    public const float FlankerMult = 1.6f;
    public const float OpenerMult = 1.5f;

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
            Effect = "Every kill gives you 35% more speed for 4 seconds. Chain your kills together." },

        new() { Id = PerkId.FlakJacket, Category = PerkCategory.Survival, Name = "Flak Jacket", When = "Against grenades",
            Effect = "Take 75% less damage from explosions, including your own grenades." },
        new() { Id = PerkId.LastStand, Category = PerkCategory.Survival, Name = "Last Stand", When = "Once per life",
            Effect = "The first hit that would kill you leaves you on 1 HP instead, and you can't be hurt for 1.5 seconds." },
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
    ];

    public static PerkDef Get(PerkId id) => All[(int)id < All.Length ? (int)id : 0];

    public static PerkDef? Find(string name) =>
        All.FirstOrDefault(p => p.Id != PerkId.None && p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<PerkDef> In(PerkCategory c) => All.Where(p => p.Category == c);

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
}
