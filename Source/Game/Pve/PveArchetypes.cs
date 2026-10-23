using System.Numerics;

namespace CallOfSlop;

/// <summary>The kinds of enemy soldier in the co-op mode (docs/pve/DESIGN.md, "Archetypes"). One byte on the wire: append only.</summary>
public enum PveArch : byte
{
    Rifleman,
    Rusher,
    Sniper,
    Heavy,
    Grenadier,
    Officer,
}

/// <summary>One archetype's stats: weapon, health, speed, aim, how it fights.</summary>
public sealed class PveArchDef
{
    public required PveArch Arch;
    public required string Name;

    /// <summary>The weapon (an existing weapon def, by name; the default primary if it's missing).</summary>
    public required string Weapon;

    public int Health = 100;

    /// <summary>Walking speed (m/s) and running speed when advancing or rushing.</summary>
    public float Walk = 3.2f, Run = 5.2f;

    /// <summary>Aim cone (radians, half angle) at Hard; the difficulty scales it.</summary>
    public float Spread = 0.035f;

    /// <summary>Damage multiplier on top of the difficulty's.</summary>
    public float Damage = 1f;

    /// <summary>Rounds per burst (min, max) and the pause between bursts (s).</summary>
    public int BurstMin = 3, BurstMax = 6;

    public float BurstPause = 0.6f;

    /// <summary>Furthest it engages from (m); the range it likes to fight at.</summary>
    public float MaxRange = 70f, PreferRange = 25f;

    /// <summary>Takes cover (riflemen, snipers, heavies, grenadiers, officers) or charges in (rushers).</summary>
    public bool UsesCover = true;

    /// <summary>Seconds between its grenades (0: never throws).</summary>
    public float GrenadeCooldown;

    /// <summary>Crouches to fire (heavies, snipers).</summary>
    public bool CrouchFire;

    /// <summary>Body colours: uniform, vest/accent.</summary>
    public Vector3 Uniform = new(0.36f, 0.33f, 0.27f), Accent = new(0.25f, 0.27f, 0.2f);

    private WeaponDef? _def;

    /// <summary>The weapon def (looked up once).</summary>
    public WeaponDef Def => _def ??= Weapons.Find(Weapon) ?? Weapons.DefaultPrimary;
}

/// <summary>The archetype table (tuned by hand; docs/pve/DESIGN.md has the intent of each).</summary>
public static class PveArchetypes
{
    public static readonly PveArchDef[] All =
    [
        new() { Arch = PveArch.Rifleman, Name = "Rifleman", Weapon = "Vanguard AR", Health = 100, Spread = 0.032f, BurstMin = 3, BurstMax = 6, PreferRange = 25, GrenadeCooldown = 45 },
        new()
        {
            Arch = PveArch.Rusher, Name = "Rusher", Weapon = "Breacher Pump", Health = 90, Walk = 4.5f, Run = 7.2f, Spread = 0.02f, BurstMin = 1, BurstMax = 1, BurstPause = 0.2f,
            MaxRange = 22, PreferRange = 4, UsesCover = false, Uniform = new(0.42f, 0.3f, 0.24f), Accent = new(0.55f, 0.2f, 0.15f),
        },
        new()
        {
            Arch = PveArch.Sniper, Name = "Sniper", Weapon = "Needle Bolt-Action", Health = 80, Walk = 2.8f, Run = 4.6f, Spread = 0.006f, BurstMin = 1, BurstMax = 1, BurstPause = 2.2f,
            MaxRange = 140, PreferRange = 60, CrouchFire = true, Uniform = new(0.3f, 0.36f, 0.22f), Accent = new(0.22f, 0.3f, 0.16f),
        },
        new()
        {
            Arch = PveArch.Heavy, Name = "Heavy", Weapon = "Anvil LMG", Health = 220, Walk = 2.4f, Run = 3.6f, Spread = 0.05f, BurstMin = 8, BurstMax = 16, BurstPause = 1.1f,
            MaxRange = 80, PreferRange = 30, CrouchFire = true, Damage = 0.85f, Uniform = new(0.28f, 0.28f, 0.3f), Accent = new(0.18f, 0.18f, 0.2f),
        },
        new()
        {
            Arch = PveArch.Grenadier, Name = "Grenadier", Weapon = "Hornet SMG", Health = 100, Spread = 0.045f, BurstMin = 4, BurstMax = 8, MaxRange = 45, PreferRange = 18,
            GrenadeCooldown = 9, Uniform = new(0.38f, 0.34f, 0.24f), Accent = new(0.6f, 0.45f, 0.15f),
        },
        new()
        {
            Arch = PveArch.Officer, Name = "Officer", Weapon = "Marksman M14", Health = 140, Walk = 2.8f, Run = 4.4f, Spread = 0.025f, BurstMin = 1, BurstMax = 3, BurstPause = 0.9f,
            MaxRange = 80, PreferRange = 35, GrenadeCooldown = 30, Uniform = new(0.2f, 0.22f, 0.3f), Accent = new(0.75f, 0.2f, 0.2f),
        },
    ];

    public static PveArchDef Get(PveArch a) => All[(int)a < All.Length ? (int)a : 0];

    /// <summary>An archetype by name ("rifleman"), or null.</summary>
    public static PveArchDef? Find(string name)
    {
        foreach (var d in All)
            if (d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    /// <summary>Officers buff enemies this close to them (tighter aim, quicker reactions).</summary>
    public const float OfficerAuraRadius = 15f;
}

/// <summary>The difficulty table (the lobby's Bot difficulty; docs/pve/DESIGN.md "Difficulty and player count").</summary>
public static class PveDifficulty
{
    public static float Budget(BotDifficulty d) => d switch { BotDifficulty.Easy => 0.7f, BotDifficulty.Hard => 1.2f, BotDifficulty.Insane => 1.4f, _ => 1f };

    public static float Damage(BotDifficulty d) => d switch { BotDifficulty.Easy => 0.3f, BotDifficulty.Hard => 0.6f, BotDifficulty.Insane => 0.75f, _ => 0.45f };

    public static float Spread(BotDifficulty d) => d switch { BotDifficulty.Easy => 1.6f, BotDifficulty.Hard => 1f, BotDifficulty.Insane => 0.85f, _ => 1.2f };

    public static float Reaction(BotDifficulty d) => d switch { BotDifficulty.Easy => 0.9f, BotDifficulty.Hard => 0.5f, BotDifficulty.Insane => 0.35f, _ => 0.65f };

    /// <summary>Grenade cooldown multiplier (lower: more grenades).</summary>
    public static float Grenades(BotDifficulty d) => d switch { BotDifficulty.Easy => 2f, BotDifficulty.Hard => 0.85f, BotDifficulty.Insane => 0.65f, _ => 1f };

    public static float Respawn(BotDifficulty d) => d switch { BotDifficulty.Easy => 6f, BotDifficulty.Hard => 15f, BotDifficulty.Insane => 20f, _ => 10f };

    /// <summary>Wipes allowed per mission (int.MaxValue: unlimited).</summary>
    public static int Retries(BotDifficulty d) => d switch { BotDifficulty.Hard => 5, BotDifficulty.Insane => 3, _ => int.MaxValue };
}
