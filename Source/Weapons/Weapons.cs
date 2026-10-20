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

/// <summary>Stats, look and sound recipe for one weapon. Shared by client and server.</summary>
public sealed class WeaponDef
{
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
    public static readonly WeaponDef[] All = Build();

    public static WeaponDef Get(byte id) => id < All.Length ? All[id] : All[0];

    public static WeaponDef? Find(string name) => All.FirstOrDefault(w => w.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static readonly WeaponDef DefaultPrimary = All.First(w => w.Name == "BR-1 Prototype");
    public static readonly WeaponDef DefaultSecondary = All.First(w => w.Name == "P9 Sidearm");

    private static WeaponDef[] Build()
    {
        Vector3 steel = new(0.55f, 0.58f, 0.62f), gold = new(0.95f, 0.75f, 0.2f), red = new(0.9f, 0.2f, 0.2f),
            green = new(0.3f, 0.8f, 0.35f), blue = new(0.25f, 0.55f, 1f), orange = new(0.95f, 0.5f, 0.15f),
            purple = new(0.7f, 0.35f, 1f), tan = new(0.75f, 0.62f, 0.42f), cyan = new(0.2f, 0.9f, 0.95f);

        List<WeaponDef> list =
        [
            // =========================== PISTOLS (secondary) ===========================
            new() { Name = "P9 Sidearm", Class = WeaponClass.Pistol, Blurb = "Reliable 9mm. Does everything okay.",
                Damage = 20, Rpm = 400, Mode = FireMode.Semi, Mag = 15, Reload = 1.3f, HipSpreadDeg = 1.2f, RangeM = 16,
                Mobility = 1.05f, RecoilDeg = 0.8f, Length = 0.2f, Bulk = 0.8f, Accent = steel, Caliber = 0.25f, Bright = 0.65f, Tail = 0.35f, EquipTime = 0.3f },
            new() { Name = "Mk.23 Tactical", Class = WeaponClass.Pistol, Blurb = "Heavier .45 frame, slower but harder hitting.",
                Damage = 27, Rpm = 320, Mode = FireMode.Semi, Mag = 12, Reload = 1.5f, HipSpreadDeg = 1.0f, RangeM = 20,
                Mobility = 1.03f, RecoilDeg = 1.1f, Length = 0.24f, Bulk = 0.9f, Accent = tan, Caliber = 0.35f, Bright = 0.55f, Tail = 0.4f, EquipTime = 0.32f },
            new() { Name = "Viper .45", Class = WeaponClass.Pistol, Blurb = "Compact and punchy, small magazine.",
                Damage = 32, Rpm = 280, Mode = FireMode.Semi, Mag = 8, Reload = 1.4f, HipSpreadDeg = 1.1f, RangeM = 18,
                Mobility = 1.05f, RecoilDeg = 1.3f, Length = 0.2f, Bulk = 0.85f, Accent = green, Caliber = 0.4f, Bright = 0.6f, Tail = 0.4f, EquipTime = 0.3f },
            new() { Name = "Rattler Revolver", Class = WeaponClass.Pistol, Blurb = "Six shots, two to the head is a kill.",
                Damage = 52, Rpm = 150, Mode = FireMode.Semi, Mag = 6, Reload = 2.4f, HipSpreadDeg = 0.8f, RangeM = 26,
                Mobility = 1.0f, RecoilDeg = 2.6f, Length = 0.27f, Bulk = 0.95f, Accent = gold, Caliber = 0.6f, Bright = 0.5f, Tail = 0.6f, EquipTime = 0.38f },
            new() { Name = "Bastion Hand Cannon", Class = WeaponClass.Pistol, Blurb = "A rifle round in a pistol. Wrist not included.",
                Damage = 68, Rpm = 110, Mode = FireMode.Semi, Mag = 5, Reload = 2.6f, HipSpreadDeg = 1.0f, RangeM = 30,
                Mobility = 0.97f, RecoilDeg = 3.6f, Length = 0.3f, Bulk = 1.15f, Accent = red, Caliber = 0.75f, Bright = 0.45f, Tail = 0.75f, EquipTime = 0.45f },
            new() { Name = "Wasp Machine Pistol", Class = WeaponClass.Pistol, Blurb = "Hose at close range. Empties in a blink.",
                Damage = 13, Rpm = 1100, Mode = FireMode.Auto, Mag = 24, Reload = 1.6f, HipSpreadDeg = 2.4f, RangeM = 10,
                Mobility = 1.05f, RecoilDeg = 0.45f, Length = 0.22f, Bulk = 0.85f, Accent = gold, Caliber = 0.15f, Bright = 0.75f, Tail = 0.25f, EquipTime = 0.3f },
            new() { Name = "Stinger Burst Pistol", Class = WeaponClass.Pistol, Blurb = "Three-round burst per pull.",
                Damage = 18, Rpm = 900, Mode = FireMode.Burst, BurstCount = 3, BurstDelay = 0.22f, Mag = 18, Reload = 1.5f, HipSpreadDeg = 1.4f, RangeM = 15,
                Mobility = 1.04f, RecoilDeg = 0.6f, Length = 0.23f, Bulk = 0.85f, Accent = cyan, Caliber = 0.22f, Bright = 0.7f, Tail = 0.3f, EquipTime = 0.3f },
            new() { Name = "Hush SD", Class = WeaponClass.Pistol, Blurb = "Integrally suppressed. Quiet tracers, quiet shots.",
                Damage = 22, Rpm = 380, Mode = FireMode.Semi, Mag = 12, Reload = 1.4f, HipSpreadDeg = 1.0f, RangeM = 16,
                Mobility = 1.05f, RecoilDeg = 0.6f, Length = 0.32f, Bulk = 0.8f, Accent = new(0.3f, 0.3f, 0.32f), Sound = SoundStyle.Suppressed, Caliber = 0.25f, Bright = 0.3f, Tail = 0.2f, EquipTime = 0.32f },
            new() { Name = "Pocket Derringer", Class = WeaponClass.Pistol, Blurb = "Two barrels, two big surprises at point blank.",
                Damage = 60, Rpm = 300, Mode = FireMode.Semi, Mag = 2, Reload = 1.8f, HipSpreadDeg = 1.8f, RangeM = 8, MinDamageMult = 0.35f,
                Mobility = 1.08f, RecoilDeg = 2.8f, Length = 0.15f, Bulk = 0.75f, Accent = gold, Caliber = 0.55f, Bright = 0.4f, Tail = 0.5f, EquipTime = 0.25f },

            // =========================== SMGs ===========================
            new() { Name = "Hornet SMG", Class = WeaponClass.SMG, Blurb = "The all-rounder SMG. Smooth recoil.",
                Damage = 20, Rpm = 800, Mag = 30, Reload = 2.0f, HipSpreadDeg = 1.9f, RangeM = 14,
                Mobility = 1.08f, RecoilDeg = 0.45f, Length = 0.36f, Accent = blue, Caliber = 0.25f, Bright = 0.65f, Tail = 0.35f },
            new() { Name = "Buzzsaw", Class = WeaponClass.SMG, Blurb = "Ludicrous fire rate, sprays wide.",
                Damage = 15, Rpm = 1150, Mag = 40, Reload = 2.2f, HipSpreadDeg = 2.8f, RangeM = 11,
                Mobility = 1.08f, RecoilDeg = 0.4f, Length = 0.34f, Accent = red, Caliber = 0.18f, Bright = 0.75f, Tail = 0.3f },
            new() { Name = "Courier", Class = WeaponClass.SMG, Blurb = "Slower, accurate, reaches further than most SMGs.",
                Damage = 24, Rpm = 680, Mag = 32, Reload = 2.0f, HipSpreadDeg = 1.6f, RangeM = 18,
                Mobility = 1.06f, RecoilDeg = 0.5f, Length = 0.38f, Accent = tan, Caliber = 0.3f, Bright = 0.6f, Tail = 0.4f },
            new() { Name = "Whisper SD", Class = WeaponClass.SMG, Blurb = "Suppressed SMG for flanking.",
                Damage = 19, Rpm = 850, Mag = 30, Reload = 2.1f, HipSpreadDeg = 1.8f, RangeM = 13,
                Mobility = 1.08f, RecoilDeg = 0.35f, Length = 0.46f, Accent = new(0.3f, 0.3f, 0.32f), Sound = SoundStyle.Suppressed, Caliber = 0.25f, Bright = 0.3f, Tail = 0.2f },
            new() { Name = "Kestrel PDW", Class = WeaponClass.SMG, Blurb = "Huge 50-round magazine, long reload.",
                Damage = 18, Rpm = 900, Mag = 50, Reload = 2.7f, HipSpreadDeg = 2.1f, RangeM = 14,
                Mobility = 1.05f, RecoilDeg = 0.4f, Length = 0.4f, Bulk = 1.05f, Accent = green, Caliber = 0.2f, Bright = 0.7f, Tail = 0.35f },
            new() { Name = "Tempest", Class = WeaponClass.SMG, Blurb = "Hard-hitting .45 SMG with a kick.",
                Damage = 27, Rpm = 640, Mag = 25, Reload = 1.9f, HipSpreadDeg = 1.7f, RangeM = 16,
                Mobility = 1.05f, RecoilDeg = 0.7f, Length = 0.37f, Bulk = 1.05f, Accent = purple, Caliber = 0.38f, Bright = 0.55f, Tail = 0.4f },
            new() { Name = "Ratchet Burst SMG", Class = WeaponClass.SMG, Blurb = "Fast 3-round bursts. Rewards tracking.",
                Damage = 26, Rpm = 1100, Mode = FireMode.Burst, BurstCount = 3, BurstDelay = 0.2f, Mag = 30, Reload = 2.0f, HipSpreadDeg = 1.5f, RangeM = 18,
                Mobility = 1.06f, RecoilDeg = 0.5f, Length = 0.38f, Accent = cyan, Caliber = 0.28f, Bright = 0.7f, Tail = 0.35f },

            // =========================== ASSAULT RIFLES ===========================
            new() { Name = "BR-1 Prototype", Class = WeaponClass.AssaultRifle, Blurb = "The original. Small mag, steady and accurate.",
                Damage = 25, Rpm = 430, Mag = 15, Reload = 1.6f, HipSpreadDeg = 1.3f, RangeM = 28,
                RecoilDeg = 0.7f, Length = 0.42f, Accent = orange, Caliber = 0.45f, Bright = 0.55f, Tail = 0.5f },
            new() { Name = "Vanguard AR", Class = WeaponClass.AssaultRifle, Blurb = "Standard-issue rifle. Easy to use.",
                Damage = 24, Rpm = 650, Mag = 30, Reload = 2.2f, HipSpreadDeg = 1.5f, RangeM = 26,
                RecoilDeg = 0.6f, Length = 0.46f, Accent = steel, Caliber = 0.42f, Bright = 0.6f, Tail = 0.55f },
            new() { Name = "Sentinel", Class = WeaponClass.AssaultRifle, Blurb = "Slower, harder hitting 7.62 rifle.",
                Damage = 30, Rpm = 540, Mag = 30, Reload = 2.4f, HipSpreadDeg = 1.4f, RangeM = 30,
                Mobility = 0.97f, RecoilDeg = 0.9f, Length = 0.5f, Bulk = 1.05f, Accent = tan, Caliber = 0.55f, Bright = 0.5f, Tail = 0.6f },
            new() { Name = "Raptor", Class = WeaponClass.AssaultRifle, Blurb = "High fire rate rifle, harder to control.",
                Damage = 22, Rpm = 800, Mag = 30, Reload = 2.1f, HipSpreadDeg = 1.7f, RangeM = 24,
                Mobility = 1.0f, RecoilDeg = 0.6f, Length = 0.44f, Accent = blue, Caliber = 0.38f, Bright = 0.65f, Tail = 0.5f },
            new() { Name = "Bulldog", Class = WeaponClass.AssaultRifle, Blurb = "Heavy rounds, short magazine, big kick.",
                Damage = 34, Rpm = 470, Mag = 24, Reload = 2.5f, HipSpreadDeg = 1.5f, RangeM = 30,
                Mobility = 0.95f, RecoilDeg = 1.2f, Length = 0.47f, Bulk = 1.15f, Accent = red, Caliber = 0.62f, Bright = 0.45f, Tail = 0.65f },
            new() { Name = "Tri-Shot", Class = WeaponClass.AssaultRifle, Blurb = "3-round burst rifle. Four bursts, four kills.",
                Damage = 30, Rpm = 900, Mode = FireMode.Burst, BurstCount = 3, BurstDelay = 0.28f, Mag = 30, Reload = 2.3f, HipSpreadDeg = 1.2f, RangeM = 34,
                RecoilDeg = 0.6f, Length = 0.48f, Accent = gold, Caliber = 0.45f, Bright = 0.6f, Tail = 0.55f },
            new() { Name = "Phalanx Bullpup", Class = WeaponClass.AssaultRifle, Blurb = "Compact rifle with SMG-like handling.",
                Damage = 23, Rpm = 720, Mag = 36, Reload = 2.4f, HipSpreadDeg = 1.5f, RangeM = 24,
                Mobility = 1.03f, RecoilDeg = 0.55f, Length = 0.38f, Bulk = 1.1f, Accent = green, Caliber = 0.4f, Bright = 0.6f, Tail = 0.5f },
            new() { Name = "Specter SD", Class = WeaponClass.AssaultRifle, Blurb = "Suppressed rifle. Less range, more stealth.",
                Damage = 24, Rpm = 700, Mag = 30, Reload = 2.2f, HipSpreadDeg = 1.5f, RangeM = 20,
                RecoilDeg = 0.5f, Length = 0.56f, Accent = new(0.3f, 0.3f, 0.32f), Sound = SoundStyle.Suppressed, Caliber = 0.4f, Bright = 0.3f, Tail = 0.25f },

            // =========================== MARKSMAN RIFLES ===========================
            new() { Name = "Marksman M14", Class = WeaponClass.DMR, Blurb = "Semi-auto battle rifle. Two to the head.",
                Damage = 45, Rpm = 380, Mode = FireMode.Semi, Mag = 20, Reload = 2.6f, HipSpreadDeg = 1.6f, AdsSpreadMult = 0.12f, RangeM = 40,
                Mobility = 0.95f, RecoilDeg = 1.6f, AdsZoom = 1.8f, Length = 0.55f, Accent = tan, Caliber = 0.65f, Bright = 0.5f, Tail = 0.7f },
            new() { Name = "Longbow DMR", Class = WeaponClass.DMR, Blurb = "Slow, precise, three body shots.",
                Damage = 55, Rpm = 260, Mode = FireMode.Semi, Mag = 15, Reload = 2.8f, HipSpreadDeg = 1.8f, AdsSpreadMult = 0.08f, RangeM = 50,
                Mobility = 0.94f, RecoilDeg = 2.0f, AdsZoom = 2.2f, Length = 0.6f, Accent = green, Caliber = 0.72f, Bright = 0.45f, Tail = 0.75f },
            new() { Name = "Reaper Battle Rifle", Class = WeaponClass.DMR, Blurb = "Full-auto 7.62. Brutal but bouncy.",
                Damage = 36, Rpm = 520, Mag = 20, Reload = 2.6f, HipSpreadDeg = 1.8f, AdsSpreadMult = 0.25f, RangeM = 34,
                Mobility = 0.94f, RecoilDeg = 1.3f, AdsZoom = 1.5f, Length = 0.54f, Bulk = 1.15f, Accent = red, Caliber = 0.66f, Bright = 0.5f, Tail = 0.7f },
            new() { Name = "Talon Burst DMR", Class = WeaponClass.DMR, Blurb = "Two-round bursts at long range.",
                Damage = 38, Rpm = 720, Mode = FireMode.Burst, BurstCount = 2, BurstDelay = 0.3f, Mag = 24, Reload = 2.6f, HipSpreadDeg = 1.5f, AdsSpreadMult = 0.12f, RangeM = 44,
                Mobility = 0.95f, RecoilDeg = 1.1f, AdsZoom = 1.8f, Length = 0.56f, Accent = cyan, Caliber = 0.6f, Bright = 0.55f, Tail = 0.7f },
            new() { Name = "Hawk Semi", Class = WeaponClass.DMR, Blurb = "Lightweight marksman rifle that moves fast.",
                Damage = 48, Rpm = 320, Mode = FireMode.Semi, Mag = 10, Reload = 2.3f, HipSpreadDeg = 1.4f, AdsSpreadMult = 0.1f, RangeM = 44,
                Mobility = 1.0f, RecoilDeg = 1.5f, AdsZoom = 1.9f, Length = 0.52f, Bulk = 0.9f, Accent = blue, Caliber = 0.6f, Bright = 0.55f, Tail = 0.65f },
            new() { Name = "Warden Lever-Action", Class = WeaponClass.DMR, Blurb = "Cowboy rifle. One head shot, two to the body.",
                Damage = 68, Rpm = 120, Mode = FireMode.Semi, Mag = 8, Reload = 3.0f, HipSpreadDeg = 1.2f, AdsSpreadMult = 0.1f, RangeM = 46, HeadMult = 1.5f,
                Mobility = 0.97f, RecoilDeg = 2.6f, AdsZoom = 1.6f, Length = 0.58f, Bulk = 0.95f, Accent = gold, Caliber = 0.78f, Bright = 0.4f, Tail = 0.85f },

            // =========================== SNIPERS ===========================
            new() { Name = "Needle Bolt-Action", Class = WeaponClass.Sniper, Blurb = "Classic bolt gun. One to the head, scope required.",
                Damage = 95, Rpm = 50, Mode = FireMode.Semi, Mag = 5, Reload = 3.2f, HipSpreadDeg = 4.0f, AdsSpreadMult = 0.01f, RangeM = 120, HeadMult = 2.5f,
                Mobility = 0.9f, RecoilDeg = 3.5f, AdsZoom = 4f, EquipTime = 0.7f, Length = 0.72f, Accent = green, Caliber = 0.85f, Bright = 0.5f, Tail = 1.0f },
            new() { Name = "Leviathan .50", Class = WeaponClass.Sniper, Blurb = "Anti-materiel rifle. One shot anywhere, slow to move.",
                Damage = 140, Rpm = 40, Mode = FireMode.Semi, Mag = 5, Reload = 3.8f, HipSpreadDeg = 5.0f, AdsSpreadMult = 0.01f, RangeM = 150,
                Mobility = 0.82f, RecoilDeg = 5.5f, AdsZoom = 4.5f, EquipTime = 0.9f, Length = 0.82f, Bulk = 1.35f, Accent = red, Sound = SoundStyle.Boom, Caliber = 1.0f, Bright = 0.4f, Tail = 1.0f },
            new() { Name = "Kite Semi-Sniper", Class = WeaponClass.Sniper, Blurb = "Semi-auto sniper. Two body shots, quick follow-ups.",
                Damage = 70, Rpm = 150, Mode = FireMode.Semi, Mag = 10, Reload = 3.0f, HipSpreadDeg = 3.5f, AdsSpreadMult = 0.02f, RangeM = 100,
                Mobility = 0.9f, RecoilDeg = 2.8f, AdsZoom = 3.5f, EquipTime = 0.65f, Length = 0.68f, Bulk = 1.1f, Accent = blue, Caliber = 0.78f, Bright = 0.5f, Tail = 0.9f },
            new() { Name = "Ghost SD", Class = WeaponClass.Sniper, Blurb = "Suppressed sniper. Nobody hears it coming.",
                Damage = 88, Rpm = 55, Mode = FireMode.Semi, Mag = 6, Reload = 3.2f, HipSpreadDeg = 4.0f, AdsSpreadMult = 0.01f, RangeM = 90, HeadMult = 2.5f,
                Mobility = 0.92f, RecoilDeg = 2.5f, AdsZoom = 4f, EquipTime = 0.7f, Length = 0.86f, Accent = new(0.3f, 0.3f, 0.32f), Sound = SoundStyle.Suppressed, Caliber = 0.7f, Bright = 0.3f, Tail = 0.3f },
            new() { Name = "Railspike", Class = WeaponClass.Sniper, Blurb = "Experimental coil rifle. No drop, no falloff, no mercy.",
                Damage = 120, Rpm = 45, Mode = FireMode.Semi, Mag = 4, Reload = 3.4f, HipSpreadDeg = 3.0f, AdsSpreadMult = 0.0f, RangeM = 300,
                Mobility = 0.88f, RecoilDeg = 3.0f, AdsZoom = 4f, EquipTime = 0.8f, Length = 0.78f, Bulk = 1.2f, Accent = cyan, Sound = SoundStyle.Zap, Caliber = 0.9f, Bright = 0.8f, Tail = 0.8f },

            // =========================== SHOTGUNS ===========================
            new() { Name = "Breacher Pump", Class = WeaponClass.Shotgun, Blurb = "Pump-action. One-shot kill up close.",
                Damage = 13, Pellets = 9, Rpm = 70, Mode = FireMode.Semi, Mag = 8, Reload = 3.0f, HipSpreadDeg = 5.5f, AdsSpreadMult = 0.8f, RangeM = 7, MinDamageMult = 0.15f, HeadMult = 1.5f,
                Mobility = 1.0f, RecoilDeg = 3.0f, AdsZoom = 1.15f, Length = 0.5f, Bulk = 1.1f, Accent = tan, Caliber = 0.8f, Bright = 0.45f, Tail = 0.7f },
            new() { Name = "Thunder Auto", Class = WeaponClass.Shotgun, Blurb = "Semi-auto shotgun. Keep pulling the trigger.",
                Damage = 9, Pellets = 8, Rpm = 230, Mode = FireMode.Semi, Mag = 8, Reload = 2.8f, HipSpreadDeg = 6.0f, AdsSpreadMult = 0.8f, RangeM = 6, MinDamageMult = 0.15f, HeadMult = 1.5f,
                Mobility = 1.0f, RecoilDeg = 2.0f, AdsZoom = 1.15f, Length = 0.5f, Bulk = 1.15f, Accent = steel, Caliber = 0.7f, Bright = 0.5f, Tail = 0.6f },
            new() { Name = "Double Barrel", Class = WeaponClass.Shotgun, Blurb = "Two shells, wide spread, devastating.",
                Damage = 14, Pellets = 10, Rpm = 300, Mode = FireMode.Semi, Mag = 2, Reload = 2.2f, HipSpreadDeg = 7.0f, AdsSpreadMult = 0.85f, RangeM = 6, MinDamageMult = 0.12f, HeadMult = 1.5f,
                Mobility = 1.02f, RecoilDeg = 3.5f, AdsZoom = 1.1f, Length = 0.52f, Bulk = 1.2f, Accent = gold, Caliber = 0.9f, Bright = 0.4f, Tail = 0.8f },
            new() { Name = "Slug Pump", Class = WeaponClass.Shotgun, Blurb = "Fires single slugs. Pump-action rifle, basically.",
                Damage = 80, Rpm = 70, Mode = FireMode.Semi, Mag = 6, Reload = 3.0f, HipSpreadDeg = 1.2f, AdsSpreadMult = 0.2f, RangeM = 25, MinDamageMult = 0.5f, HeadMult = 1.5f,
                Mobility = 1.0f, RecoilDeg = 3.2f, AdsZoom = 1.4f, Length = 0.5f, Bulk = 1.1f, Accent = red, Caliber = 0.85f, Bright = 0.4f, Tail = 0.75f },
            new() { Name = "Street Sweeper", Class = WeaponClass.Shotgun, Blurb = "Full-auto drum shotgun. Chaos in a hallway.",
                Damage = 8, Pellets = 8, Rpm = 260, Mag = 20, Reload = 3.5f, HipSpreadDeg = 7.0f, AdsSpreadMult = 0.85f, RangeM = 5, MinDamageMult = 0.12f, HeadMult = 1.5f,
                Mobility = 0.95f, RecoilDeg = 1.8f, AdsZoom = 1.1f, Length = 0.48f, Bulk = 1.3f, Accent = purple, Caliber = 0.65f, Bright = 0.5f, Tail = 0.6f },
            new() { Name = "Scattergun Lever", Class = WeaponClass.Shotgun, Blurb = "Lever-action shotgun with a tight choke.",
                Damage = 11, Pellets = 9, Rpm = 110, Mode = FireMode.Semi, Mag = 6, Reload = 2.8f, HipSpreadDeg = 4.0f, AdsSpreadMult = 0.75f, RangeM = 9, MinDamageMult = 0.2f, HeadMult = 1.5f,
                Mobility = 1.0f, RecoilDeg = 2.6f, AdsZoom = 1.2f, Length = 0.52f, Accent = green, Caliber = 0.75f, Bright = 0.45f, Tail = 0.75f },

            // =========================== LMGs ===========================
            new() { Name = "Anvil LMG", Class = WeaponClass.LMG, Blurb = "100-round box. Lay down the law.",
                Damage = 28, Rpm = 600, Mag = 100, Reload = 5.0f, HipSpreadDeg = 2.2f, AdsSpreadMult = 0.3f, RangeM = 30,
                Mobility = 0.85f, RecoilDeg = 0.6f, AdsZoom = 1.4f, EquipTime = 0.8f, Length = 0.58f, Bulk = 1.4f, Accent = tan, Caliber = 0.55f, Bright = 0.5f, Tail = 0.65f },
            new() { Name = "Hailstorm", Class = WeaponClass.LMG, Blurb = "Fast-firing 150-round belt.",
                Damage = 22, Rpm = 850, Mag = 150, Reload = 5.5f, HipSpreadDeg = 2.8f, AdsSpreadMult = 0.35f, RangeM = 24,
                Mobility = 0.82f, RecoilDeg = 0.5f, AdsZoom = 1.35f, EquipTime = 0.85f, Length = 0.56f, Bulk = 1.45f, Accent = blue, Caliber = 0.45f, Bright = 0.6f, Tail = 0.6f },
            new() { Name = "Bastille", Class = WeaponClass.LMG, Blurb = "Heavy-calibre LMG. Slow, accurate, deadly.",
                Damage = 36, Rpm = 450, Mag = 80, Reload = 5.2f, HipSpreadDeg = 1.9f, AdsSpreadMult = 0.25f, RangeM = 34,
                Mobility = 0.84f, RecoilDeg = 0.9f, AdsZoom = 1.5f, EquipTime = 0.8f, Length = 0.62f, Bulk = 1.45f, Accent = red, Caliber = 0.7f, Bright = 0.45f, Tail = 0.75f },
            new() { Name = "Grinder Rotary", Class = WeaponClass.LMG, Blurb = "Rotary cannon. 1500 rpm. You will hear it.",
                Damage = 15, Rpm = 1500, Mag = 200, Reload = 7.0f, HipSpreadDeg = 3.5f, AdsSpreadMult = 0.6f, RangeM = 20,
                Mobility = 0.75f, RecoilDeg = 0.25f, AdsZoom = 1.2f, EquipTime = 1.1f, Length = 0.62f, Bulk = 1.7f, Accent = gold, Caliber = 0.35f, Bright = 0.7f, Tail = 0.5f },
            new() { Name = "Torrent", Class = WeaponClass.LMG, Blurb = "Light LMG that handles like a rifle.",
                Damage = 26, Rpm = 700, Mag = 75, Reload = 4.5f, HipSpreadDeg = 2.0f, AdsSpreadMult = 0.3f, RangeM = 28,
                Mobility = 0.9f, RecoilDeg = 0.6f, AdsZoom = 1.4f, EquipTime = 0.7f, Length = 0.52f, Bulk = 1.3f, Accent = green, Caliber = 0.5f, Bright = 0.55f, Tail = 0.6f },

            // =========================== SPECIAL ===========================
            new() { Name = "Nailgun", Class = WeaponClass.Special, Blurb = "Construction tool, repurposed. Pneumatic and quiet.",
                Damage = 17, Rpm = 1000, Mag = 60, Reload = 2.5f, HipSpreadDeg = 1.5f, RangeM = 14,
                Mobility = 1.05f, RecoilDeg = 0.2f, Length = 0.34f, Bulk = 1.2f, Accent = orange, Sound = SoundStyle.Pneumatic, Caliber = 0.3f, Bright = 0.5f, Tail = 0.2f },
            new() { Name = "Arc Carbine", Class = WeaponClass.Special, Blurb = "Energy carbine. Crackling bolts, no falloff.",
                Damage = 22, Rpm = 600, Mag = 40, Reload = 2.4f, HipSpreadDeg = 1.2f, RangeM = 60, MinDamageMult = 1f,
                Mobility = 1.0f, RecoilDeg = 0.3f, Length = 0.44f, Accent = cyan, Sound = SoundStyle.Zap, Caliber = 0.35f, Bright = 0.8f, Tail = 0.4f },
            new() { Name = "Old Faithful Musket", Class = WeaponClass.Special, Blurb = "One shot. Then a long, long reload.",
                Damage = 110, Rpm = 30, Mode = FireMode.Semi, Mag = 1, Reload = 3.0f, HipSpreadDeg = 1.0f, AdsSpreadMult = 0.3f, RangeM = 40,
                Mobility = 0.95f, RecoilDeg = 4.0f, AdsZoom = 1.5f, EquipTime = 0.6f, Length = 0.8f, Bulk = 0.9f, Accent = new(0.55f, 0.35f, 0.2f), Sound = SoundStyle.Boom, Caliber = 0.95f, Bright = 0.35f, Tail = 1.0f },
            new() { Name = "Toy Blaster", Class = WeaponClass.Special, Blurb = "Pew pew. 99 rounds of pure humiliation.",
                Damage = 6, Rpm = 1200, Mag = 99, Reload = 1.5f, HipSpreadDeg = 1.0f, RangeM = 20, HeadMult = 3f,
                Mobility = 1.12f, RecoilDeg = 0.1f, Length = 0.3f, Bulk = 1.1f, Accent = new(1f, 0.4f, 0.8f), Sound = SoundStyle.Toy, Caliber = 0.1f, Bright = 0.9f, Tail = 0.1f },
        ];

        for (var i = 0; i < list.Count; i++)
            list[i].Id = (byte)i;
        return list.ToArray();
    }

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
