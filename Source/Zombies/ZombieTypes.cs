using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// One kind of zombie (the rules file's "types" section: Parts/Tables/ZombiesRules.json; docs/zombies/ZOMBIES-MODE.md,
/// "Zombie types"). Everything about a type is data: its health (a multiplier on the round's health, or a fixed
/// number), gait and speed, damage, reach and swing, size (the drawn body, the hit box and the body players are held off
/// scale with it), colours and voice, points, limb rules, the round it first appears, its spawn weight by round, how many
/// may be up at once, and the one special it has (a knockback, armour plates, a grab, a pounce, a wall climb, a burst, a
/// boss round). Type 0 is always the walker (the plain zombie of every round). The hard rule: no type has a ranged
/// attack of any kind; every one of them has to reach a player to hurt them (<see cref="MaxReach"/>, checked on load).
/// </summary>
public sealed class ZombieTypeDef
{
    /// <summary>At most this many types (the type index is 4 bits in Msg.ZEnts).</summary>
    public const int MaxTypes = 16;

    /// <summary>
    /// The furthest any type may reach (m, its reach multiplier x the rules' reach): a swipe's length, never a shot or a
    /// throw. A type past it is refused on load. The self-test asserts every shipped type keeps under it.
    /// </summary>
    public const float MaxReach = 2.2f;

    /// <summary>The key in the rules file ("brute") and the label players see ("Brute").</summary>
    public required string Key;

    public string Label = "";

    /// <summary>Off: the type never spawns (the bloater ships off until the developer says it isn't a ranged attack).</summary>
    public bool Enabled = true;

    /// <summary>Health: the round's health x this (the proposal), or <see cref="HealthFixed"/> when that is above 0 (the alternative: fixed per type, never scaling).</summary>
    public float HealthMult = 1f;

    public int HealthFixed;

    /// <summary>Its gait (the stride it is drawn with and the speed class it is given): walk, run, sprint, or -1 for the round's mix like a walker.</summary>
    public int Gait = -1;

    /// <summary>Its speed: the gait's speed x this.</summary>
    public float SpeedMult = 1f;

    /// <summary>A swipe's damage, reach, windup and time between swipes: the rules' x these.</summary>
    public float DamageMult = 1f, ReachMult = 1f, WindupMult = 1f, SwingMult = 1f;

    /// <summary>The body's size: width (the hit box's and the body's radius) and height (the hit box, the head's height, how high it reaches) x these.</summary>
    public float Width = 1f, Height = 1f;

    /// <summary>Its colours: skin, shirt and trousers.</summary>
    public Vector3 Skin = new(0.36f, 0.52f, 0.3f), Shirt = new(0.33f, 0.3f, 0.26f), Pants = new(0.18f, 0.2f, 0.27f);

    /// <summary>Its voice: pitch and loudness x these (a brute is deep and loud, a skitterer shrill).</summary>
    public float VoicePitch = 1f, VoiceGain = 1f;

    /// <summary>Points for killing it: the rules' kill points x this.</summary>
    public float PointsMult = 1f;

    /// <summary>Its limbs: health x this, and whether it can lose them at all.</summary>
    public float LimbHealthMult = 1f;

    public bool Severs = true;

    /// <summary>The first round it can spawn in, its spawn weight by round (0: never that round) and the most alive at once (0: no cap).</summary>
    public int FirstRound = 1;

    public RoundCurve Weight = new([(1, 1f)]);

    public int MaxAlive;

    /// <summary>Spawns in a pack: this many more of the same type come out with it (if the round and the cap allow).</summary>
    public int Pack;

    /// <summary>Boards come off this much faster at a window.</summary>
    public float TearMult = 1f;

    /// <summary>A hit throws the player back this fast (m/s; 0: none), and it shrugs off stuns and staggers.</summary>
    public float Knockback;

    public bool StunImmune;

    /// <summary>Armour: a helmet and a chest plate of this much health each (0: none); shots on the head or chest go into the plate until it comes off. Limbs are bare.</summary>
    public int Armour;

    /// <summary>A grab: a landed swipe holds the player in place for up to this long (0: none), biting for this much every second; any hit on it (or a teammate's) frees them.</summary>
    public float GrabSeconds, GrabDamage = 10f;

    /// <summary>A pounce: from within this distance (m; 0: none) it leaps at the player this fast, then swipes where it lands; this many seconds between leaps. A melee lunge, not a shot.</summary>
    public float PounceRange, PounceSpeed = 9f, PounceCooldown = 4f;

    /// <summary>A wall climber: it scales a wall up to this high (m; 0: never climbs) at this speed (m/s) when its prey is above it or the way is blocked.</summary>
    public float ClimbHeight, ClimbSpeed = 2f;

    /// <summary>A burst: when it dies, or when its swipe lands, it bursts, hurting only players within this distance (m; 0: none) for this much. A melee-range burst, never a projectile.</summary>
    public float BurstRadius;

    public int BurstDamage = 50;

    /// <summary>A boss: one comes every this many rounds (0: not a boss), on top of the round's count, with a health bar on the HUD.</summary>
    public int BossEvery;

    /// <summary>The furthest it reaches (m): its reach multiplier x the rules' reach. Every type keeps under <see cref="MaxReach"/>.</summary>
    public float ReachFor(ZombiesRules r) => r.ZombieReach * ReachMult;

    /// <summary>Does it have any special at all (the self-test names what each one is)?</summary>
    public string Special => BossEvery > 0 ? "boss" : BurstRadius > 0 ? "burst" : ClimbHeight > 0 ? "climb" : PounceRange > 0 ? "pounce" : GrabSeconds > 0 ? "grab" : Armour > 0 ? "armour" : Knockback > 0 ? "knockback" : Pack > 0 ? "pack" : "none";

    /// <summary>The plain zombie (type 0 when the rules file names none).</summary>
    public static ZombieTypeDef Walker() => new() { Key = "walker", Label = "Walker" };

    /// <summary>The walker every zombie starts as (the pool's reset; a client's zombie before its type arrives).</summary>
    public static readonly ZombieTypeDef DefaultWalker = Walker();

    /// <summary>The shipped types (the same numbers the rules file has): what a rules file without a "types" section gets.</summary>
    public static List<ZombieTypeDef> Defaults()
    {
        return
        [
            Walker(),
            new()
            {
                Key = "brute", Label = "Brute", HealthMult = 5f, Gait = (int)ZSpeed.Walk, SpeedMult = 0.9f, DamageMult = 1.6f, ReachMult = 1.3f, WindupMult = 1.3f, SwingMult = 1.4f,
                Width = 1.45f, Height = 1.5f, Skin = new Vector3(0.3f, 0.36f, 0.24f), Shirt = new Vector3(0.25f, 0.2f, 0.17f), Pants = new Vector3(0.14f, 0.13f, 0.15f),
                VoicePitch = 0.6f, VoiceGain = 1.5f, PointsMult = 3f, LimbHealthMult = 2.5f, FirstRound = 6, Weight = new RoundCurve([(6, 0.04f), (12, 0.1f), (25, 0.16f)]), MaxAlive = 4,
                TearMult = 3f, Knockback = 7f, StunImmune = true,
            },
            new()
            {
                Key = "skitterer", Label = "Skitterer", HealthMult = 0.4f, Gait = (int)ZSpeed.Sprint, SpeedMult = 1.05f, DamageMult = 0.5f, ReachMult = 0.9f, WindupMult = 0.7f, SwingMult = 0.7f,
                Width = 0.75f, Height = 0.7f, Skin = new Vector3(0.52f, 0.5f, 0.3f), Shirt = new Vector3(0.4f, 0.26f, 0.2f), Pants = new Vector3(0.22f, 0.18f, 0.14f),
                VoicePitch = 1.4f, VoiceGain = 0.8f, PointsMult = 0.75f, LimbHealthMult = 0.6f, FirstRound = 4, Weight = new RoundCurve([(4, 0.08f), (10, 0.2f), (20, 0.25f)]), MaxAlive = 24, Pack = 3,
            },
            new()
            {
                Key = "climber", Label = "Wall climber", HealthMult = 0.25f, Gait = (int)ZSpeed.Run, SpeedMult = 1.1f, DamageMult = 0.8f, ReachMult = 1f,
                Width = 0.85f, Height = 0.9f, Skin = new Vector3(0.45f, 0.42f, 0.5f), Shirt = new Vector3(0.2f, 0.18f, 0.24f), Pants = new Vector3(0.16f, 0.15f, 0.2f),
                VoicePitch = 1.2f, VoiceGain = 0.9f, PointsMult = 1.25f, LimbHealthMult = 0.5f, FirstRound = 8, Weight = new RoundCurve([(8, 0.05f), (15, 0.12f), (25, 0.15f)]), MaxAlive = 8,
                ClimbHeight = 14f, ClimbSpeed = 2.2f,
            },
            new()
            {
                Key = "armoured", Label = "Armoured", HealthMult = 1.5f, Gait = (int)ZSpeed.Run, SpeedMult = 0.85f, DamageMult = 1.2f, ReachMult = 1.1f, WindupMult = 1.1f,
                Width = 1.1f, Height = 1.1f, Skin = new Vector3(0.34f, 0.44f, 0.3f), Shirt = new Vector3(0.3f, 0.32f, 0.34f), Pants = new Vector3(0.2f, 0.22f, 0.24f),
                VoicePitch = 0.85f, VoiceGain = 1.1f, PointsMult = 2f, LimbHealthMult = 1.2f, FirstRound = 10, Weight = new RoundCurve([(10, 0.04f), (18, 0.1f), (30, 0.14f)]), MaxAlive = 6,
                Armour = 300,
            },
            new()
            {
                Key = "grabber", Label = "Grabber", HealthMult = 1.2f, Gait = (int)ZSpeed.Run, SpeedMult = 0.95f, DamageMult = 0.5f, ReachMult = 1.15f,
                Width = 1f, Height = 1.1f, Skin = new Vector3(0.5f, 0.42f, 0.28f), Shirt = new Vector3(0.36f, 0.22f, 0.14f), Pants = new Vector3(0.2f, 0.16f, 0.12f),
                VoicePitch = 0.9f, VoiceGain = 1.1f, PointsMult = 2f, FirstRound = 12, Weight = new RoundCurve([(12, 0.04f), (20, 0.08f), (30, 0.12f)]), MaxAlive = 4,
                GrabSeconds = 4f, GrabDamage = 12f,
            },
            new()
            {
                Key = "pouncer", Label = "Pouncer", HealthMult = 0.6f, Gait = (int)ZSpeed.Run, SpeedMult = 1.15f, DamageMult = 1.1f, ReachMult = 1.1f, WindupMult = 0.8f,
                Width = 0.9f, Height = 0.95f, Skin = new Vector3(0.3f, 0.5f, 0.42f), Shirt = new Vector3(0.18f, 0.26f, 0.22f), Pants = new Vector3(0.14f, 0.18f, 0.16f),
                VoicePitch = 1.15f, PointsMult = 1.5f, LimbHealthMult = 0.8f, FirstRound = 14, Weight = new RoundCurve([(14, 0.04f), (22, 0.08f), (30, 0.12f)]), MaxAlive = 6,
                PounceRange = 6f, PounceSpeed = 9f, PounceCooldown = 4f,
            },
            new()
            {
                Key = "bloater", Label = "Bloater", Enabled = false, HealthMult = 1.5f, Gait = (int)ZSpeed.Walk, SpeedMult = 0.9f, DamageMult = 0.8f,
                Width = 1.35f, Height = 1.1f, Skin = new Vector3(0.55f, 0.6f, 0.32f), Shirt = new Vector3(0.45f, 0.42f, 0.22f), Pants = new Vector3(0.24f, 0.22f, 0.14f),
                VoicePitch = 0.75f, VoiceGain = 1.2f, PointsMult = 2f, Severs = false, FirstRound = 10, Weight = new RoundCurve([(10, 0.04f), (20, 0.08f)]), MaxAlive = 3,
                BurstRadius = 2f, BurstDamage = 50,
            },
            new()
            {
                Key = "juggernaut", Label = "Juggernaut", HealthMult = 20f, Gait = (int)ZSpeed.Walk, SpeedMult = 0.8f, DamageMult = 2.5f, ReachMult = 1.6f, WindupMult = 1.5f, SwingMult = 1.6f,
                Width = 1.8f, Height = 2f, Skin = new Vector3(0.26f, 0.28f, 0.22f), Shirt = new Vector3(0.3f, 0.12f, 0.1f), Pants = new Vector3(0.1f, 0.1f, 0.12f),
                VoicePitch = 0.5f, VoiceGain = 2f, PointsMult = 10f, Severs = false, FirstRound = 10, Weight = new RoundCurve([(1, 0f)]), MaxAlive = 1,
                TearMult = 4f, Knockback = 9f, StunImmune = true, BossEvery = 10,
            },
        ];
    }

    /// <summary>
    /// Reads the "types" section: an object of types by key (type 0 is "walker", added first if the file leaves it out).
    /// Every number is range-checked, and an unknown key (anything that smells of a ranged attack included) refuses the
    /// file; <see cref="CheckReach"/> keeps every type under <see cref="MaxReach"/>.
    /// </summary>
    public static List<ZombieTypeDef> Read(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: an object of zombie types by key");
        var list = new List<ZombieTypeDef>();
        foreach (var p in e.EnumerateObject())
        {
            if (p.Name == "note")
                continue;
            var key = p.Name.ToLowerInvariant();
            if (key.Length == 0 || key.Length > 24 || !key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
                throw new WeaponFileException($"{where}: a type key is 1-24 lower-case ASCII letters or digits (\"{p.Name}\")");
            if (list.Any(t => t.Key == key))
                throw new WeaponFileException($"{where}: two types called \"{key}\"");
            list.Add(ReadOne(key, p.Value, $"{where}.{p.Name}"));
        }
        if (list.Count == 0 || list[0].Key != "walker")
            list.Insert(0, Walker());
        if (list.Count > MaxTypes)
            throw new WeaponFileException($"{where}: at most {MaxTypes} types");
        var walker = list[0];
        if (walker.BossEvery > 0 || walker.Armour > 0 || walker.GrabSeconds > 0 || walker.PounceRange > 0 || walker.ClimbHeight > 0 || walker.BurstRadius > 0 || !walker.Enabled)
            throw new WeaponFileException($"{where}.walker: the walker is the plain zombie (no special, always on)");
        return list;
    }

    /// <summary>After the whole file is read (the reach comes from the "zombie" section): no type reaches past <see cref="MaxReach"/>.</summary>
    public static void CheckReach(List<ZombieTypeDef> types, ZombiesRules rules, string source)
    {
        foreach (var t in types)
            if (t.ReachFor(rules) > MaxReach)
                throw new WeaponFileException($"{source}: types.{t.Key} reaches {t.ReachFor(rules):0.00} m, past the melee limit of {MaxReach} m (no ranged attacks)");
    }

    private static ZombieTypeDef ReadOne(string key, JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: each type is an object");
        var t = new ZombieTypeDef { Key = key, Label = char.ToUpperInvariant(key[0]) + key[1..] };
        foreach (var p in e.EnumerateObject())
        {
            var w = $"{where}.{p.Name}";
            var v = p.Value;
            switch (p.Name)
            {
                case "note": break;
                case "label": t.Label = WeaponFile.Text(v, w); break;
                case "enabled": t.Enabled = Flag(v, w); break;
                case "healthMult": t.HealthMult = Real(v, w, 0.05f, 100); break;
                case "healthFixed": t.HealthFixed = Whole(v, w, 0, 1_000_000); break;
                case "gait":
                    var g = WeaponFile.Text(v, w);
                    t.Gait = g.Equals("mix", StringComparison.OrdinalIgnoreCase) ? -1
                        : Enum.TryParse<ZSpeed>(g, true, out var sp) && Enum.IsDefined(sp) ? (int)sp
                        : throw new WeaponFileException($"{w}: walk, run, sprint or mix");
                    break;
                case "speedMult": t.SpeedMult = Real(v, w, 0.1f, 3); break;
                case "damageMult": t.DamageMult = Real(v, w, 0, 10); break;
                case "reachMult": t.ReachMult = Real(v, w, 0.3f, 2); break;
                case "windupMult": t.WindupMult = Real(v, w, 0.2f, 5); break;
                case "swingMult": t.SwingMult = Real(v, w, 0.2f, 5); break;
                case "scale":
                    t.Width = t.Height = Real(v, w, 0.4f, 2.5f);
                    break;
                case "width": t.Width = Real(v, w, 0.4f, 2.5f); break;
                case "height": t.Height = Real(v, w, 0.4f, 2.5f); break;
                case "skin": t.Skin = Colour(v, w); break;
                case "shirt": t.Shirt = Colour(v, w); break;
                case "pants": t.Pants = Colour(v, w); break;
                case "voicePitch": t.VoicePitch = Real(v, w, 0.3f, 3); break;
                case "voiceGain": t.VoiceGain = Real(v, w, 0, 4); break;
                case "pointsMult": t.PointsMult = Real(v, w, 0, 100); break;
                case "limbHealthMult": t.LimbHealthMult = Real(v, w, 0.05f, 100); break;
                case "severs": t.Severs = Flag(v, w); break;
                case "firstRound": t.FirstRound = Whole(v, w, 1, 1000); break;
                case "weight": t.Weight = RoundCurve.Read(v, w, 0, 1000); break;
                case "maxAlive": t.MaxAlive = Whole(v, w, 0, Game.MaxZombies); break;
                case "pack": t.Pack = Whole(v, w, 0, 12); break;
                case "tearMult": t.TearMult = Real(v, w, 0.2f, 10); break;
                case "knockback": t.Knockback = Real(v, w, 0, 20); break;
                case "stunImmune": t.StunImmune = Flag(v, w); break;
                case "armour": t.Armour = Whole(v, w, 0, 1_000_000); break;
                case "grabSeconds": t.GrabSeconds = Real(v, w, 0, 30); break;
                case "grabDamage": t.GrabDamage = Real(v, w, 0, 500); break;
                case "pounceRange": t.PounceRange = Real(v, w, 0, 8); break;
                case "pounceSpeed": t.PounceSpeed = Real(v, w, 2, 14); break;
                case "pounceCooldown": t.PounceCooldown = Real(v, w, 0.5f, 60); break;
                case "climbHeight": t.ClimbHeight = Real(v, w, 0, 60); break;
                case "climbSpeed": t.ClimbSpeed = Real(v, w, 0.2f, 8); break;
                case "burstRadius": t.BurstRadius = Real(v, w, 0, 3); break;
                case "burstDamage": t.BurstDamage = Whole(v, w, 0, 1000); break;
                case "bossEvery": t.BossEvery = Whole(v, w, 0, 1000); break;
                default:
                    throw new WeaponFileException($"{where}: unknown type key \"{p.Name}\" (label, enabled, healthMult, healthFixed, gait, speedMult, damageMult, reachMult, windupMult, swingMult, "
                                                  + "scale, width, height, skin, shirt, pants, voicePitch, voiceGain, pointsMult, limbHealthMult, severs, firstRound, weight, maxAlive, pack, tearMult, "
                                                  + "knockback, stunImmune, armour, grabSeconds, grabDamage, pounceRange, pounceSpeed, pounceCooldown, climbHeight, climbSpeed, burstRadius, burstDamage, bossEvery; "
                                                  + "no type has a ranged attack)");
            }
        }
        if (t.Label.Length == 0 || t.Label.Length > 24 || t.Label.Any(c => c > 126))
            throw new WeaponFileException($"{where}.label: 1-24 ASCII characters");
        return t;
    }

    private static Vector3 Colour(JsonElement e, string where)
    {
        try
        {
            return MapFile.ToColour(e, where, "");
        }
        catch (MapFileException ex)
        {
            throw new WeaponFileException(ex.Message);
        }
    }

    private static int Whole(JsonElement e, string where, int min, int max)
    {
        var v = WeaponFile.Int(e, where);
        return v >= min && v <= max ? v : throw new WeaponFileException($"{where}: {v} is outside {min}-{max}");
    }

    private static bool Flag(JsonElement e, string where) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new WeaponFileException($"{where}: true or false"),
    };

    private static float Real(JsonElement e, string where, float min, float max)
    {
        var v = WeaponFile.Number(e, where);
        return v >= min && v <= max ? v : throw new WeaponFileException($"{where}: {v} is outside {min}-{max}");
    }
}
