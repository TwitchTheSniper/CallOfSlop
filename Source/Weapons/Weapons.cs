using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace CallOfSlop;

public enum WeaponClass : byte
{
    Pistol,
    SMG,
    AssaultRifle,
    DMR,
    Sniper,
    Shotgun,
    LMG,
    Special,

    /// <summary>Rocket and grenade launchers (Weapons/Launchers): secondary slot only, fire real projectiles (Source/Game/Launchers).</summary>
    Launcher,
}

public enum FireMode : byte
{
    Semi,
    Auto,
    Burst,
}

/// <summary>Which synthesiser recipe a weapon's gunshot uses.</summary>
public enum SoundStyle : byte
{
    Gun,
    Suppressed,
    Boom,
    Zap,
    Toy,
    Pneumatic,

    /// <summary>Launchers: a rocket's backblast roar (Source/Game/Launchers).</summary>
    Rocket,

    /// <summary>Launchers: a grenade launcher's hollow "thoomp".</summary>
    Thump,
}

public enum Slot : byte
{
    Primary = 0,
    Secondary = 1,
}

/// <summary>
/// Stats, look and sound recipe for one weapon. Shared by client and server.
/// Weapons are loaded from files in the Weapons folder (see <see cref="WeaponFile"/> and Weapons/README.md):
/// every public field below except Id, Optic, Suppressor and Kit is a key in a weapon file (the Recoil* fields sit in its "recoil" block).
/// </summary>
public sealed partial class WeaponDef
{
    public WeaponDef()
    {
    }

    [SetsRequiredMembers]
    private WeaponDef(bool blank) => Name = "";

    /// <summary>An empty weapon with every default set, for the file loader to fill in.</summary>
    internal static WeaponDef Blank() => new(true);

    /// <summary>Position in <see cref="Weapons.All"/>. Local and per session: saves and loadouts go by name.</summary>
    public byte Id;
    public required string Name;
    public required WeaponClass Class;
    public string Blurb = "";

    // ---- Damage --------------------------------------------------------------
    public required int Damage; // per pellet, inside RangeM
    public float HeadMult = 2f;
    public int Pellets = 1;
    public required float RangeM; // full damage up to here...
    public float MinDamageMult = 0.6f; // ...falling linearly to this at 3x RangeM

    // ---- Handling ----------------------------------------------------------
    public required int Rpm;
    public FireMode Mode = FireMode.Auto;
    public int BurstCount = 3;
    public float BurstDelay = 0.25f; // pause after a burst
    public required int Mag;
    public required float Reload;
    public required float HipSpreadDeg;
    public float AdsSpreadMult = 0.3f;
    public float RecoilDeg = 0.7f;
    public float Mobility = 1f; // movement speed multiplier
    public float AdsZoom = 1.35f; // FOV divisor when aiming
    public float EquipTime = 0.45f;
    public float AdsTime = 0.16f; // seconds to fully aim down sights

    // ---- Recoil profile (the "recoil" block of a weapon file; see Recoil.cs and Weapons/README.md) ----
    // Kicks are fractions of RecoilDeg, so RecoilDeg stays the overall scale. Missing values get class defaults.
    [RecoilKey("vertical", 0, 5)] public float RecoilVertical = 1f;
    [RecoilKey("horizontal", 0, 5)] public float RecoilHorizontal = 0.35f;
    [RecoilKey("bias", -1, 1)] public float RecoilBias = 0.25f;
    [RecoilKey("jitter", 0, 3)] public float RecoilJitter = 0.25f;
    [RecoilKey("firstShot", 0, 5)] public float RecoilFirstShot = 1.15f;
    [RecoilKey("seed", 0, 1000000)] public int RecoilSeed;
    [RecoilKey("patternShots", 1, 100)] public int RecoilPatternShots = 12;
    [RecoilKey("rampVertical", 0, 5)] public float RecoilRampVertical = 1.2f;
    [RecoilKey("rampHorizontal", 0, 5)] public float RecoilRampHorizontal = 1.3f;
    [RecoilKey("sway", 0, 3)] public float RecoilSway = 0.7f;
    [RecoilKey("swayPeriod", 2, 100)] public float RecoilSwayPeriod = 14f;
    [RecoilKey("recovery", 0, 40)] public float RecoilRecovery = 9f;
    [RecoilKey("recoveryDelay", 0, 2)] public float RecoilRecoveryDelay = 0.1f;
    [RecoilKey("recoveryAmount", 0, 1)] public float RecoilRecoveryAmount = 0.7f;
    [RecoilKey("resetTime", 0.05f, 3)] public float RecoilResetTime = 0.3f;
    [RecoilKey("crouchVertical", 0, 3)] public float RecoilCrouchVertical = 0.85f;
    [RecoilKey("crouchHorizontal", 0, 3)] public float RecoilCrouchHorizontal = 0.75f;
    [RecoilKey("proneVertical", 0, 3)] public float RecoilProneVertical = 0.7f;
    [RecoilKey("proneHorizontal", 0, 3)] public float RecoilProneHorizontal = 0.55f;
    [RecoilKey("adsVertical", 0, 3)] public float RecoilAdsVertical = 0.75f;
    [RecoilKey("adsHorizontal", 0, 3)] public float RecoilAdsHorizontal = 0.6f;
    [RecoilKey("shake", 0, 10)] public float RecoilShake = 0.12f;
    [RecoilKey("shakeFreq", 0, 80)] public float RecoilShakeFreq = 18f;
    [RecoilKey("shakeTime", 0.01f, 2)] public float RecoilShakeTime = 0.12f;
    [RecoilKey("punch", 0, 10)] public float RecoilPunch = 0.35f;
    [RecoilKey("punchFov", 0, 20)] public float RecoilPunchFov = 1.2f;
    [RecoilKey("punchRoll", 0, 20)] public float RecoilPunchRoll = 0.8f;
    [RecoilKey("punchTime", 0.01f, 2)] public float RecoilPunchTime = 0.1f;
    [RecoilKey("kickBack", 0, 0.3f)] public float RecoilKickBack = 0.04f;
    [RecoilKey("kickUp", 0, 30)] public float RecoilKickUp = 2.5f;
    [RecoilKey("kickRoll", 0, 30)] public float RecoilKickRoll = 1.5f;
    [RecoilKey("kickSide", 0, 0.1f)] public float RecoilKickSide = 0.004f;
    [RecoilKey("kickTime", 0.01f, 2)] public float RecoilKickTime = 0.08f;

    // ---- Attachments (set on kitted copies, see Attachments.Apply) -------------
    public OpticKind Optic = OpticKind.Iron;
    public bool Suppressor;
    public uint Kit; // which attachments made this copy (0 = stock weapon)

    /// <summary>The fitted optic's "sight" block (null = the default for <see cref="Optic"/>; see <see cref="Sights.Spec"/>). A property, so it isn't a stat.</summary>
    public SightSpec? Sight { get; set; }

    public WeaponDef Clone() => (WeaponDef)MemberwiseClone();

    // ---- File info (not stats; properties so they stay out of the field list) ----
    /// <summary>Sort order in the weapon list, which decides ids (the shipped weapons use 1-50; files without one get 1000).</summary>
    public int Order { get; set; } = 1000;

    /// <summary>Account level that unlocks this weapon (null: the built-in unlock table, or the max level for new weapons).</summary>
    public int? UnlockLevel { get; set; }

    /// <summary>Who made it (optional, for third-party weapons).</summary>
    public string Author { get; set; } = "";

    /// <summary>The file it was loaded from, relative to the Weapons folder (null for the built-in fallback weapons).</summary>
    public string? SourceFile { get; set; }

    /// <summary>The gun's model (the file's "model" block; see GunSpec and Weapons/README.md). Null: drawn by the per-class model.</summary>
    public GunSpec? Model { get; set; }

    // ---- Built weapons (Zombies weapon builder, Source/Zombies). Properties, so they're never weapon-file keys. ----
    // Every one is null/off on the weapons from the Weapons folder, which keeps them firing exactly as before.

    /// <summary>
    /// The barrels of a built weapon (null = one barrel at the usual muzzle: every standard weapon). Each fires its own
    /// pellets from its own muzzle with its own damage and spread multipliers; see <see cref="Game.ShotRays"/>.
    /// </summary>
    public BarrelPoint[]? MuzzlePoints { get; set; }

    /// <summary>Built weapons on a "rotate" grip: the receivers (barrel groups) take turns, one per shot.</summary>
    public bool RotateGroups { get; set; }

    /// <summary>Seconds of held trigger to reach the full fire rate (miniguns, chain guns); 0 = none.</summary>
    public float SpoolTime { get; set; }

    /// <summary>Fraction of the fire rate a spool-up starts from.</summary>
    public float SpoolStart { get; set; } = 1f;

    /// <summary>No aiming down sights (hip fire only: push carts, briefcases).</summary>
    public bool NoAds { get; set; }

    /// <summary>A built weapon's body (from its parts), drawn instead of a class or "model" block body.</summary>
    public GunMesh? BuiltMesh { get; set; }

    /// <summary>Special-effect flags from a built weapon's parts (incendiary, explosive...): carried, gameplay later.</summary>
    public string[] BuildFlags { get; set; } = [];

    /// <summary>Barrels in all (1 for standard weapons).</summary>
    public int BarrelCount => MuzzlePoints?.Length ?? 1;

    /// <summary>
    /// Damage one shot puts out at close range if every projectile lands: Damage x Pellets for standard weapons; for
    /// built weapons the sum over the barrels that fire together (rotary receivers and rotating grips average their turns).
    /// </summary>
    public float ShotDamage
    {
        get
        {
            if (MuzzlePoints is not { Length: > 0 } pts)
                return Damage * Pellets;
            var groups = 0;
            foreach (var p in pts)
                groups = Math.Max(groups, p.Group + 1);
            float total = 0;
            for (var g = 0; g < groups; g++)
            {
                float sum = 0;
                var n = 0;
                var rotary = false;
                foreach (var p in pts)
                    if (p.Group == g)
                    {
                        sum += Damage * p.DamageMult * p.Pellets;
                        n++;
                        rotary |= p.Rotary;
                    }
                total += rotary && n > 0 ? sum / n : sum;
            }
            return RotateGroups && groups > 0 ? total / groups : total;
        }
    }

    // ---- Look ----------------------------------------------------------------
    public float Length = 0.42f;
    public float Bulk = 1f;
    public Vector3 Accent = new(0.95f, 0.5f, 0.15f);

    // ---- Sound ---------------------------------------------------------------
    public SoundStyle Sound = SoundStyle.Gun;
    public float Caliber = 0.5f; // 0 = small and snappy, 1 = huge and deep
    public float Bright = 0.5f; // high-frequency content of the blast
    public float Tail = 0.5f; // length of the echo

    // ---- Derived -------------------------------------------------------------
    public Slot Slot => Class is WeaponClass.Pistol or WeaponClass.Launcher ? Slot.Secondary : Slot.Primary; // launchers: secondary only
    public float FireInterval => 60f / Rpm;
    public float HipSpreadRad => HipSpreadDeg * MathF.PI / 180f;
    public float MinSpreadRad => HipSpreadRad * AdsSpreadMult * 0.5f;
    public float MaxSpreadRad => HipSpreadRad * 2.5f + 0.09f;

    public float DamageMultAt(float dist)
    {
        if (dist <= RangeM)
            return 1f;
        var t = Math.Clamp((dist - RangeM) / (RangeM * 2f), 0f, 1f);
        return float.Lerp(1f, MinDamageMult, t);
    }

    /// <summary>Sustained body-shot damage per second at close range (ignores reloads).</summary>
    public float Dps
    {
        get
        {
            var perShot = ShotDamage;
            if (Mode != FireMode.Burst)
                return perShot / FireInterval;
            var cycle = BurstCount * FireInterval + BurstDelay;
            return perShot * BurstCount / cycle;
        }
    }

    public int ShotsToKill(bool head) =>
        (int)Math.Ceiling(World.MaxHealth / (ShotDamage * (head ? HeadMult : 1f)));

    public string ClassName =>
        Class switch
        {
            WeaponClass.AssaultRifle => "Assault Rifle",
            WeaponClass.SMG => "SMG",
            WeaponClass.DMR => "Marksman",
            WeaponClass.LMG => "LMG",
            _ => Class.ToString(),
        };

    public string ModeName => Mode == FireMode.Burst ? $"{BurstCount}-round burst" : Mode == FireMode.Auto ? "Full auto" : "Semi auto";
}

public static class Weapons
{
    /// <summary>Weapon ids are bytes; 239 and up are melee and throwables in the kill feed.</summary>
    public const int MaxWeapons = Game.MeleeKillId;

    private static readonly object LoadLock = new();
    private static WeaponDef[]? _all;
    private static WeaponDef? _defaultPrimary, _defaultSecondary;

    /// <summary>
    /// Every weapon, loaded from the Weapons folder on first use (not as a field initialiser, so nothing
    /// depends on static initialisation order), sorted by "order" then name. Ids are positions in this list.
    /// </summary>
    public static WeaponDef[] All
    {
        get
        {
            var all = _all;
            if (all != null)
                return all;
            lock (LoadLock)
                return _all ??= LoadAll();
        }
    }

    public static WeaponDef Get(byte id) => id < All.Length ? All[id] : All[0];

    public static WeaponDef? Find(string name) => All.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The starter primary: the BR-1 Prototype, or the first primary there is if someone removed it.</summary>
    public static WeaponDef DefaultPrimary => _defaultPrimary ??= Find("BR-1 Prototype") is { Slot: Slot.Primary } w ? w : All.First(x => x.Slot == Slot.Primary);

    /// <summary>The starter secondary: the P9 Sidearm, or the first secondary there is.</summary>
    public static WeaponDef DefaultSecondary => _defaultSecondary ??= Find("P9 Sidearm") is { Slot: Slot.Secondary } w ? w : All.First(x => x.Slot == Slot.Secondary);

    /// <summary>Problems found while loading weapon files (bad files are skipped). Shown in the lobby and the log.</summary>
    public static readonly List<string> LoadErrors = new();

    /// <summary>The folder weapon files are loaded from (next to the game).</summary>
    public static string Folder => Path.Combine(AppContext.BaseDirectory, "Weapons");

    /// <summary>
    /// The weapon ids before weapons became files (ids were positions in the old code list). Old progress.txt
    /// files store weapon XP as "wx&lt;id&gt;" (and older ones "wk&lt;id&gt;"); this maps them to names. Never reorder.
    /// </summary>
    public static readonly string[] LegacyNames =
    [
        "P9 Sidearm", "Mk.23 Tactical", "Viper .45", "Rattler Revolver", "Bastion Hand Cannon", "Wasp Machine Pistol", "Stinger Burst Pistol",
        "Hush SD", "Pocket Derringer", "Hornet SMG", "Buzzsaw", "Courier", "Whisper SD", "Kestrel PDW", "Tempest", "Ratchet Burst SMG",
        "BR-1 Prototype", "Vanguard AR", "Sentinel", "Raptor", "Bulldog", "Tri-Shot", "Phalanx Bullpup", "Specter SD", "Marksman M14",
        "Longbow DMR", "Reaper Battle Rifle", "Talon Burst DMR", "Hawk Semi", "Warden Lever-Action", "Needle Bolt-Action", "Leviathan .50",
        "Kite Semi-Sniper", "Ghost SD", "Railspike", "Breacher Pump", "Thunder Auto", "Double Barrel", "Slug Pump", "Street Sweeper",
        "Scattergun Lever", "Anvil LMG", "Hailstorm", "Bastille", "Grinder Rotary", "Torrent", "Nailgun", "Arc Carbine", "Old Faithful Musket",
        "Toy Blaster",
    ];

    /// <summary>
    /// Every *.json in the Weapons folder and its subfolders. A bad file is skipped and reported. If a slot ends
    /// up with no weapon at all (folder missing or broken), the built-in fallback weapon for it is added.
    /// </summary>
    private static WeaponDef[] LoadAll()
    {
        var list = new List<WeaponDef>();
        if (Directory.Exists(Folder))
        {
            foreach (var file in Directory.GetFiles(Folder, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var rel = Path.GetRelativePath(Folder, file);
                if (FrenzyClasses.IsFrenzyFile(rel))
                    continue; // Weapons/Frenzy/classes.json: per-class frenzy data, not a weapon (FrenzyClasses.cs)
                try
                {
                    var w = WeaponFile.Load(File.ReadAllText(file), rel);
                    if (list.FirstOrDefault(x => x.Name.Equals(w.Name, StringComparison.OrdinalIgnoreCase)) is { } dup)
                        throw new WeaponFileException($"{rel}: another weapon is already called '{w.Name}' ({dup.SourceFile})");
                    list.Add(w);
                }
                catch (Exception ex) when (ex is WeaponFileException or IOException or UnauthorizedAccessException)
                {
                    LoadErrors.Add(ex.Message);
                    Console.WriteLine($"[weapons] skipped: {ex.Message}");
                }
            }
        }
        foreach (var fb in Fallback())
            if (!list.Any(w => w.Slot == fb.Slot))
            {
                Console.WriteLine($"[weapons] no {fb.Slot.ToString().ToLowerInvariant()} weapon files in {Folder}; using the built-in {fb.Name}");
                list.Add(fb);
            }
        var sorted = list.OrderBy(w => w.Order).ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (sorted.Count > MaxWeapons)
        {
            foreach (var extra in sorted.Skip(MaxWeapons))
                LoadErrors.Add($"{extra.SourceFile}: too many weapons (at most {MaxWeapons}); skipped");
            sorted = sorted.Take(MaxWeapons).ToList();
        }
        for (var i = 0; i < sorted.Count; i++)
            sorted[i].Id = (byte)i;
        Console.WriteLine($"[weapons] {sorted.Count} weapon(s) from {Folder}");
        return sorted.ToArray();
    }

    /// <summary>The only weapons defined in code, one per slot: used when the Weapons folder is missing or has none for a slot.</summary>
    public static WeaponDef[] Fallback()
    {
        WeaponDef[] list = FallbackStats();
        foreach (var w in list)
            Recoil.ApplyClassDefaults(w);
        return list;
    }

    private static WeaponDef[] FallbackStats() =>
    [
        new()
        {
            Name = "P9 Sidearm", Class = WeaponClass.Pistol, Blurb = "Reliable 9mm. Does everything okay.", Order = 1,
            Damage = 20, Rpm = 400, Mode = FireMode.Semi, Mag = 15, Reload = 1.3f, HipSpreadDeg = 1.2f, RangeM = 16,
            Mobility = 1.05f, RecoilDeg = 0.8f, Length = 0.2f, Bulk = 0.8f, Accent = new(0.55f, 0.58f, 0.62f), Caliber = 0.25f, Bright = 0.65f, Tail = 0.35f, EquipTime = 0.3f,
        },
        new()
        {
            Name = "BR-1 Prototype", Class = WeaponClass.AssaultRifle, Blurb = "The original. Small mag, steady and accurate.", Order = 17,
            Damage = 25, Rpm = 430, Mag = 15, Reload = 1.6f, HipSpreadDeg = 1.3f, RangeM = 28,
            RecoilDeg = 0.7f, Length = 0.42f, Accent = new(0.95f, 0.5f, 0.15f), Caliber = 0.45f, Bright = 0.55f, Tail = 0.5f,
        },
    ];

    // ---------------------------------------------------------------------
    // Deterministic spread (client prediction and server use the same seed)
    // ---------------------------------------------------------------------

    /// <summary>Direction of pellet <paramref name="pellet"/> inside a cone of half-angle <paramref name="spreadRad"/>.</summary>
    public static Vector3 SpreadDirection(Vector3 dir, float spreadRad, uint seed, int pellet)
    {
        if (spreadRad <= 0)
            return dir;
        var u = (Hash(seed, pellet * 2) & 0xFFFFFF) / 16777216f;
        var v = (Hash(seed, pellet * 2 + 1) & 0xFFFFFF) / 16777216f;
        var r = MathF.Sqrt(u) * MathF.Tan(spreadRad);
        var a = v * MathF.Tau;
        var right = Vector3.Normalize(Vector3.Cross(dir, MathF.Abs(dir.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY));
        var up = Vector3.Cross(right, dir);
        return Vector3.Normalize(dir + right * (MathF.Cos(a) * r) + up * (MathF.Sin(a) * r));
    }

    private static uint Hash(uint a, int b)
    {
        var x = a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
        x ^= x >> 15;
        x *= 0x2C1B3C6Du;
        x ^= x >> 12;
        x *= 0x297A2D39u;
        x ^= x >> 15;
        return x;
    }
}
