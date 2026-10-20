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
}

public enum Slot : byte
{
    Primary = 0,
    Secondary = 1,
}

/// <summary>
/// Stats, look and sound recipe for one weapon. Shared by client and server.
/// Weapons are loaded from files in the Weapons folder (see <see cref="WeaponFile"/> and Weapons/README.md):
/// every public field below except Id, Optic, Suppressor and Kit is a key in a weapon file.
/// </summary>
public sealed class WeaponDef
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

    // ---- Attachments (set on kitted copies, see Attachments.Apply) -------------
    public OpticKind Optic = OpticKind.Iron;
    public bool Suppressor;
    public uint Kit; // which attachments made this copy (0 = stock weapon)

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
    public Slot Slot => Class == WeaponClass.Pistol ? Slot.Secondary : Slot.Primary;
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
            var perShot = Damage * Pellets;
            if (Mode != FireMode.Burst)
                return perShot / FireInterval;
            var cycle = BurstCount * FireInterval + BurstDelay;
            return perShot * BurstCount / cycle;
        }
    }

    public int ShotsToKill(bool head) =>
        (int)Math.Ceiling(World.MaxHealth / (Damage * Pellets * (head ? HeadMult : 1f)));

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
    public static WeaponDef[] Fallback() =>
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
