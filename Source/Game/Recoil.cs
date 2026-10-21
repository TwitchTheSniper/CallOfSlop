using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Marks a <see cref="WeaponDef"/> field as part of the weapon's recoil profile: it lives in the "recoil" block of a
/// weapon file under <see cref="Key"/>, and must be between <see cref="Min"/> and <see cref="Max"/>.
/// Attachments change it with the field's own name, e.g. <c>{ "stat": "recoilVertical", "mul": 0.7 }</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RecoilKeyAttribute(string key, float min, float max) : Attribute
{
    public string Key { get; } = key;
    public float Min { get; } = min;
    public float Max { get; } = max;
}

/// <summary>How far the viewmodel is kicked by recoil right now (decays back to zero). Read by Program.DrawViewModel.</summary>
public struct WeaponKick
{
    /// <summary>Metres the gun is pushed back towards the eye.</summary>
    public float Back;

    /// <summary>Radians the muzzle is flipped up.</summary>
    public float Up;

    /// <summary>Radians of roll (positive = clockwise).</summary>
    public float Roll;

    /// <summary>Metres the gun is pushed sideways (positive = right).</summary>
    public float Side;
}

/// <summary>A render-only camera offset from recoil (shake and view punch). It never changes where you actually aim.</summary>
public struct CameraKick
{
    /// <summary>Radians added to the rendered pitch (positive = up).</summary>
    public float Pitch;

    /// <summary>Radians added to the rendered yaw (positive = right).</summary>
    public float Yaw;

    /// <summary>Radians of camera roll.</summary>
    public float Roll;

    /// <summary>Fraction the field of view is widened by (0.02 = 2% wider).</summary>
    public float Fov;
}

/// <summary>
/// Per-weapon recoil: a deterministic, seeded pattern per gun (so sustained fire follows a learnable shape), plus
/// jitter, a first-shot multiplier, recovery after you stop firing, stance and aim multipliers per axis, and visual
/// camera shake, view punch and viewmodel kick. The numbers come from each weapon file's "recoil" block (see
/// Weapons/README.md); weapons without one get the defaults of their class.
/// Recoil is client-side aim only: the server validates spread and fire rate, never the recoil.
/// </summary>
public static class Recoil
{
    /// <summary>The recoil fields of <see cref="WeaponDef"/>, in declaration (and file) order.</summary>
    public static readonly FieldInfo[] Fields = typeof(WeaponDef).GetFields(BindingFlags.Public | BindingFlags.Instance)
        .Where(f => f.IsDefined(typeof(RecoilKeyAttribute)))
        .OrderBy(f => f.MetadataToken)
        .ToArray();

    private static readonly Dictionary<string, FieldInfo> ByKey = Fields.ToDictionary(f => f.GetCustomAttribute<RecoilKeyAttribute>()!.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The key a recoil field has inside the "recoil" block ("RecoilVertical" -> "vertical").</summary>
    public static string Key(FieldInfo f) => f.GetCustomAttribute<RecoilKeyAttribute>()!.Key;

    // ---- Class defaults ---------------------------------------------------------------------

    /// <summary>One class's recoil character, as values for <see cref="Fields"/> in key order.</summary>
    private static readonly Dictionary<WeaponClass, Dictionary<string, float>> ClassDefaults = new()
    {
        // A learnable, mostly vertical climb with a gentle drift.
        [WeaponClass.AssaultRifle] = Profile(vertical: 1, horizontal: 0.35f, bias: 0.25f, jitter: 0.25f, firstShot: 1.15f, patternShots: 12, rampVertical: 1.2f, rampHorizontal: 1.3f,
            sway: 0.7f, swayPeriod: 14, recovery: 9, recoveryDelay: 0.1f, recoveryAmount: 0.7f, resetTime: 0.3f, crouchVertical: 0.85f, crouchHorizontal: 0.75f, proneVertical: 0.7f,
            proneHorizontal: 0.55f, adsVertical: 0.75f, adsHorizontal: 0.6f, shake: 0.12f, shakeFreq: 18, shakeTime: 0.12f, punch: 0.35f, punchFov: 1.2f, punchRoll: 0.8f,
            punchTime: 0.1f, kickBack: 0.04f, kickUp: 2.5f, kickRoll: 1.5f, kickSide: 0.004f, kickTime: 0.08f),
        // Jittery and fast: low kick per shot but all over the place.
        [WeaponClass.SMG] = Profile(vertical: 0.7f, horizontal: 0.9f, bias: 0, jitter: 1.4f, firstShot: 0.9f, patternShots: 8, rampVertical: 1, rampHorizontal: 1.4f,
            sway: 0.3f, swayPeriod: 5, recovery: 14, recoveryDelay: 0.06f, recoveryAmount: 0.75f, resetTime: 0.2f, crouchVertical: 0.9f, crouchHorizontal: 0.85f, proneVertical: 0.8f,
            proneHorizontal: 0.75f, adsVertical: 0.8f, adsHorizontal: 0.75f, shake: 0.2f, shakeFreq: 30, shakeTime: 0.08f, punch: 0.2f, punchFov: 0.6f, punchRoll: 1.2f,
            punchTime: 0.06f, kickBack: 0.025f, kickUp: 1.5f, kickRoll: 2.5f, kickSide: 0.006f, kickTime: 0.05f),
        // Lots of vertical; almost no horizontal once you get low.
        [WeaponClass.LMG] = Profile(vertical: 1.25f, horizontal: 0.5f, bias: 0.1f, jitter: 0.6f, firstShot: 1, patternShots: 20, rampVertical: 1.15f, rampHorizontal: 1.5f,
            sway: 0.6f, swayPeriod: 9, recovery: 5, recoveryDelay: 0.15f, recoveryAmount: 0.6f, resetTime: 0.35f, crouchVertical: 0.8f, crouchHorizontal: 0.08f, proneVertical: 0.6f,
            proneHorizontal: 0.03f, adsVertical: 0.85f, adsHorizontal: 0.7f, shake: 0.18f, shakeFreq: 14, shakeTime: 0.15f, punch: 0.4f, punchFov: 1, punchRoll: 0.6f,
            punchTime: 0.12f, kickBack: 0.035f, kickUp: 2, kickRoll: 1, kickSide: 0.003f, kickTime: 0.1f),
        // A big single snap, then a fast settle.
        [WeaponClass.DMR] = Profile(vertical: 1, horizontal: 0.2f, bias: 0.2f, jitter: 0.5f, firstShot: 1, patternShots: 4, rampVertical: 1.1f, rampHorizontal: 1.2f,
            sway: 0.3f, swayPeriod: 6, recovery: 16, recoveryDelay: 0.04f, recoveryAmount: 0.85f, resetTime: 0.4f, crouchVertical: 0.85f, crouchHorizontal: 0.8f, proneVertical: 0.7f,
            proneHorizontal: 0.6f, adsVertical: 0.8f, adsHorizontal: 0.7f, shake: 0.25f, shakeFreq: 22, shakeTime: 0.1f, punch: 1, punchFov: 2.5f, punchRoll: 1.5f,
            punchTime: 0.09f, kickBack: 0.07f, kickUp: 6, kickRoll: 2.5f, kickSide: 0.004f, kickTime: 0.1f),
        // A heavy kick and a strong camera punch.
        [WeaponClass.Sniper] = Profile(vertical: 1, horizontal: 0.15f, bias: 0.3f, jitter: 0.6f, firstShot: 1, patternShots: 2, rampVertical: 1, rampHorizontal: 1,
            sway: 0, swayPeriod: 4, recovery: 7, recoveryDelay: 0.12f, recoveryAmount: 0.75f, resetTime: 0.6f, crouchVertical: 0.85f, crouchHorizontal: 0.8f, proneVertical: 0.65f,
            proneHorizontal: 0.5f, adsVertical: 0.85f, adsHorizontal: 0.8f, shake: 0.5f, shakeFreq: 12, shakeTime: 0.22f, punch: 2.5f, punchFov: 4, punchRoll: 3,
            punchTime: 0.16f, kickBack: 0.12f, kickUp: 12, kickRoll: 5, kickSide: 0.006f, kickTime: 0.16f),
        // A big punch that's mostly visual, and a fast recovery.
        [WeaponClass.Shotgun] = Profile(vertical: 0.6f, horizontal: 0.25f, bias: 0, jitter: 0.8f, firstShot: 1, patternShots: 2, rampVertical: 1, rampHorizontal: 1,
            sway: 0, swayPeriod: 4, recovery: 18, recoveryDelay: 0.03f, recoveryAmount: 0.9f, resetTime: 0.4f, crouchVertical: 0.85f, crouchHorizontal: 0.85f, proneVertical: 0.7f,
            proneHorizontal: 0.7f, adsVertical: 0.85f, adsHorizontal: 0.8f, shake: 0.6f, shakeFreq: 16, shakeTime: 0.1f, punch: 2.5f, punchFov: 5, punchRoll: 3,
            punchTime: 0.08f, kickBack: 0.11f, kickUp: 10, kickRoll: 4, kickSide: 0.008f, kickTime: 0.09f),
        // A sharp flip with a quick recovery.
        [WeaponClass.Pistol] = Profile(vertical: 1.1f, horizontal: 0.3f, bias: 0.15f, jitter: 0.6f, firstShot: 1, patternShots: 3, rampVertical: 1.1f, rampHorizontal: 1,
            sway: 0.2f, swayPeriod: 4, recovery: 20, recoveryDelay: 0.03f, recoveryAmount: 0.85f, resetTime: 0.3f, crouchVertical: 0.9f, crouchHorizontal: 0.9f, proneVertical: 0.8f,
            proneHorizontal: 0.8f, adsVertical: 0.8f, adsHorizontal: 0.75f, shake: 0.12f, shakeFreq: 25, shakeTime: 0.06f, punch: 0.8f, punchFov: 1, punchRoll: 3,
            punchTime: 0.06f, kickBack: 0.03f, kickUp: 14, kickRoll: 4, kickSide: 0.004f, kickTime: 0.07f),
        // Specials each have their own block; this is a middle-of-the-road fallback for new ones.
        [WeaponClass.Special] = Profile(vertical: 0.9f, horizontal: 0.4f, bias: 0, jitter: 0.6f, firstShot: 1, patternShots: 8, rampVertical: 1.1f, rampHorizontal: 1.2f,
            sway: 0.6f, swayPeriod: 8, recovery: 10, recoveryDelay: 0.08f, recoveryAmount: 0.75f, resetTime: 0.3f, crouchVertical: 0.85f, crouchHorizontal: 0.8f, proneVertical: 0.7f,
            proneHorizontal: 0.6f, adsVertical: 0.8f, adsHorizontal: 0.7f, shake: 0.2f, shakeFreq: 20, shakeTime: 0.1f, punch: 0.6f, punchFov: 1.5f, punchRoll: 1.5f,
            punchTime: 0.1f, kickBack: 0.05f, kickUp: 4, kickRoll: 2, kickSide: 0.004f, kickTime: 0.08f),
    };

    private static Dictionary<string, float> Profile(float vertical, float horizontal, float bias, float jitter, float firstShot, float patternShots, float rampVertical,
        float rampHorizontal, float sway, float swayPeriod, float recovery, float recoveryDelay, float recoveryAmount, float resetTime, float crouchVertical,
        float crouchHorizontal, float proneVertical, float proneHorizontal, float adsVertical, float adsHorizontal, float shake, float shakeFreq, float shakeTime,
        float punch, float punchFov, float punchRoll, float punchTime, float kickBack, float kickUp, float kickRoll, float kickSide, float kickTime) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vertical"] = vertical, ["horizontal"] = horizontal, ["bias"] = bias, ["jitter"] = jitter, ["firstShot"] = firstShot, ["seed"] = 0,
            ["patternShots"] = patternShots, ["rampVertical"] = rampVertical, ["rampHorizontal"] = rampHorizontal, ["sway"] = sway, ["swayPeriod"] = swayPeriod,
            ["recovery"] = recovery, ["recoveryDelay"] = recoveryDelay, ["recoveryAmount"] = recoveryAmount, ["resetTime"] = resetTime,
            ["crouchVertical"] = crouchVertical, ["crouchHorizontal"] = crouchHorizontal, ["proneVertical"] = proneVertical, ["proneHorizontal"] = proneHorizontal,
            ["adsVertical"] = adsVertical, ["adsHorizontal"] = adsHorizontal, ["shake"] = shake, ["shakeFreq"] = shakeFreq, ["shakeTime"] = shakeTime,
            ["punch"] = punch, ["punchFov"] = punchFov, ["punchRoll"] = punchRoll, ["punchTime"] = punchTime,
            ["kickBack"] = kickBack, ["kickUp"] = kickUp, ["kickRoll"] = kickRoll, ["kickSide"] = kickSide, ["kickTime"] = kickTime,
        };

    /// <summary>Sets every recoil field to the defaults of the weapon's class (used for weapon files without a "recoil" block).</summary>
    public static void ApplyClassDefaults(WeaponDef w)
    {
        var values = ClassDefaults[w.Class];
        foreach (var f in Fields)
        {
            var v = values[Key(f)];
            f.SetValue(w, f.FieldType == typeof(int) ? (object)(int)v : v);
        }
    }

    // ---- File reading and writing -------------------------------------------------------------

    /// <summary>Reads a weapon file's "recoil" block over the class defaults. Unknown keys are errors, like everywhere else in weapon files.</summary>
    public static void ReadBlock(WeaponDef w, JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected {{ \"vertical\": ..., \"horizontal\": ..., ... }} (see Weapons/README.md)");
        var given = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in e.EnumerateObject())
        {
            var at = $"{where}.{p.Name}";
            if (!given.Add(p.Name))
                throw new WeaponFileException($"{at}: given twice");
            if (!ByKey.TryGetValue(p.Name, out var f))
                throw new WeaponFileException($"{where}: unknown recoil field \"{p.Name}\". Known: {string.Join(", ", Fields.Select(Key))}");
            f.SetValue(w, WeaponFile.FromJson(p.Value, f.FieldType, at));
        }
    }

    /// <summary>The weapon's "recoil" block as JSON (every field, one per line), indented for a weapon file.</summary>
    public static string BlockJson(WeaponDef w)
    {
        var sb = new StringBuilder("{\n");
        for (var i = 0; i < Fields.Length; i++)
            sb.Append("    \"").Append(Key(Fields[i])).Append("\": ").Append(WeaponFile.Fmt(Fields[i].GetValue(w))).Append(i < Fields.Length - 1 ? ",\n" : "\n");
        return sb.Append("  }").ToString();
    }

    /// <summary>Checks every recoil number is in range. Throws <see cref="WeaponFileException"/> if not.</summary>
    public static void Validate(WeaponDef w, string source)
    {
        foreach (var f in Fields)
        {
            var a = f.GetCustomAttribute<RecoilKeyAttribute>()!;
            var v = System.Convert.ToSingle(f.GetValue(w), CultureInfo.InvariantCulture);
            if (!(v >= a.Min && v <= a.Max))
                throw new WeaponFileException($"{source}: \"recoil\".\"{a.Key}\" is {WeaponFile.Num(v)}, but must be between {WeaponFile.Num(a.Min)} and {WeaponFile.Num(a.Max)}");
        }
    }

    // ---- The pattern ----------------------------------------------------------------------------

    /// <summary>The gun's pattern seed: its "seed", or one made from its name when that's 0.</summary>
    public static uint SeedOf(WeaponDef d) => d.RecoilSeed != 0 ? (uint)d.RecoilSeed : WeaponSet.Fnv([d.Name]) | 1u;

    /// <summary>A deterministic number in [-1, 1) from a seed and two indices.</summary>
    public static float Noise(uint seed, int a, int b)
    {
        var x = seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du;
        x ^= x >> 15;
        x *= 0x2C1B3C6Du;
        x ^= x >> 12;
        x *= 0x297A2D39u;
        x ^= x >> 15;
        return (x & 0xFFFFFF) / 8388608f - 1f;
    }

    /// <summary>
    /// The kick of shot <paramref name="shot"/> (0 = first) in a spray, in degrees, before stance and aiming multipliers.
    /// The shape (ramp and side-to-side sway) depends only on the gun and the shot index, so it's the same every spray
    /// and can be learnt. The jitter part also mixes in <paramref name="spray"/>, so erratic guns stay erratic.
    /// </summary>
    public static (float Up, float Right) PatternDeg(WeaponDef d, int shot, int spray)
    {
        var seed = SeedOf(d);
        var n = Math.Max(1, d.RecoilPatternShots);
        var t = n <= 1 ? 1f : MathF.Min(shot / (n - 1f), 1f);
        var rampV = float.Lerp(1f, d.RecoilRampVertical, t);
        var rampH = float.Lerp(1f, d.RecoilRampHorizontal, t);
        var first = shot == 0 ? d.RecoilFirstShot : 1f;

        // Side-to-side sway: two seeded sine waves. It fades in over the first few shots, so a spray starts straight up.
        var period = MathF.Max(2f, d.RecoilSwayPeriod);
        var phase = (Noise(seed, 7, 0) + 1f) * MathF.PI;
        var phase2 = (Noise(seed, 11, 0) + 1f) * MathF.PI;
        var ratio = 1.7f + (Noise(seed, 13, 0) + 1f) * 0.6f;
        var wave = 0.7f * MathF.Sin(MathF.Tau * shot / period + phase) + 0.3f * MathF.Sin(MathF.Tau * shot * ratio / period + phase2);
        var fadeIn = MathF.Min(1f, shot / 4f);

        var jv = Noise(seed, shot, spray * 2 + 1);
        var jh = Noise(seed, shot, spray * 2 + 2);
        var up = d.RecoilDeg * d.RecoilVertical * rampV * first * MathF.Max(0.2f, 1f + d.RecoilJitter * 0.4f * jv);
        var right = d.RecoilDeg * d.RecoilHorizontal * rampH * first * (d.RecoilBias + d.RecoilSway * wave * fadeIn + d.RecoilJitter * jh);
        return (up, right);
    }

    /// <summary>Multipliers for (vertical, horizontal) kick from stance and aiming (<paramref name="aimT"/> 0 = hip, 1 = fully aimed).</summary>
    public static (float V, float H) Multipliers(WeaponDef d, Stance stance, float aimT)
    {
        var (sv, sh) = stance switch
        {
            Stance.Crouch => (d.RecoilCrouchVertical, d.RecoilCrouchHorizontal),
            Stance.Prone => (d.RecoilProneVertical, d.RecoilProneHorizontal),
            _ => (1f, 1f),
        };
        aimT = Math.Clamp(aimT, 0f, 1f);
        return (sv * float.Lerp(1f, d.RecoilAdsVertical, aimT), sh * float.Lerp(1f, d.RecoilAdsHorizontal, aimT));
    }

    /// <summary>Seconds without firing after which the next shot starts a new spray (shot index 0).</summary>
    public static float ResetTime(WeaponDef d) =>
        MathF.Max(d.RecoilResetTime, d.FireInterval * 1.5f + (d.Mode == FireMode.Burst ? d.BurstDelay : 0f));

    /// <summary>
    /// Seconds after a shot before recovery starts. Automatic and burst guns also wait out their fire interval, so
    /// recovery only happens once you stop firing, never between the shots of a spray.
    /// </summary>
    public static float RecoveryDelay(WeaponDef d) =>
        d.Mode == FireMode.Semi ? d.RecoilRecoveryDelay : MathF.Max(d.RecoilRecoveryDelay, d.FireInterval * 1.25f);

    // ---- Stats for the menus and bots -------------------------------------------------------------

    /// <summary>Average upward kick per shot over a spray, in degrees (standing, hip).</summary>
    public static float VerticalDeg(WeaponDef d) => d.RecoilDeg * d.RecoilVertical * (1f + d.RecoilRampVertical) * 0.5f;

    /// <summary>Typical sideways kick per shot, in degrees (standing, hip): drift, sway and jitter together.</summary>
    public static float HorizontalDeg(WeaponDef d) =>
        d.RecoilDeg * d.RecoilHorizontal * (1f + d.RecoilRampHorizontal) * 0.5f * (MathF.Abs(d.RecoilBias) + 0.5f * d.RecoilSway + 0.6f * d.RecoilJitter);

    /// <summary>One number for how hard the gun is to control (degrees per shot, sideways weighted extra because it's harder to pull against).</summary>
    public static float EffectiveKickDeg(WeaponDef d) => VerticalDeg(d) + 1.5f * HorizontalDeg(d);

    /// <summary>
    /// The "Control" stat bar, 0..1 (1 = no recoil). Log-scaled, so a 30% recoil cut shows as a clear step on a rifle
    /// as well as on a sniper.
    /// </summary>
    public static float ControlBar(WeaponDef d)
    {
        var k = MathF.Max(EffectiveKickDeg(d), 0.05f);
        return 1f - Math.Clamp(MathF.Log(k / 0.1f) / MathF.Log(9f / 0.1f), 0f, 1f);
    }

    /// <summary>Short text for the Control stat: average kick up and sideways.</summary>
    public static string ControlText(WeaponDef d) =>
        $"{VerticalDeg(d).ToString("0.0#", CultureInfo.InvariantCulture)} up, {HorizontalDeg(d).ToString("0.0#", CultureInfo.InvariantCulture)} side";

    /// <summary>
    /// After a kit is applied: recoil never drops below 40% of the stock gun's on either axis, whatever is stacked.
    /// </summary>
    public static void ClampKit(WeaponDef baseDef, WeaponDef def)
    {
        def.RecoilDeg = MathF.Max(def.RecoilDeg, baseDef.RecoilDeg * 0.4f);
        if (def.RecoilDeg <= 0)
            return;
        def.RecoilVertical = MathF.Max(def.RecoilVertical, baseDef.RecoilDeg * baseDef.RecoilVertical * 0.4f / def.RecoilDeg);
        def.RecoilHorizontal = MathF.Max(def.RecoilHorizontal, baseDef.RecoilDeg * baseDef.RecoilHorizontal * 0.4f / def.RecoilDeg);
    }

    /// <summary>
    /// How a bot's aim is knocked off by one shot (radians: pitch up, yaw right). Bots don't follow the pattern,
    /// but they feel the gun's character: LMGs climb, SMGs wander, snipers jump.
    /// </summary>
    public static (float Pitch, float Yaw) BotKick(WeaponDef d, Random rng)
    {
        var kick = d.RecoilDeg * MathF.PI / 180f;
        var pitch = kick * d.RecoilVertical * 0.6f;
        var wander = ((float)rng.NextDouble() * 2f - 1f) * (0.3f + 0.5f * d.RecoilSway + d.RecoilJitter);
        var yaw = kick * d.RecoilHorizontal * (d.RecoilBias + wander) * 0.6f;
        return (pitch, yaw);
    }
}

/// <summary>
/// The local player's recoil while firing: which shot of the spray this is, how much of the climb is still to be
/// recovered, and the decaying visual shake, punch and viewmodel kick. Everything is driven by frame time with
/// exponential decays, so it behaves the same at any frame rate.
/// </summary>
public sealed class RecoilState
{
    private const float Deg = MathF.PI / 180f;

    /// <summary>Index of the next shot within the current spray (0 = a fresh spray).</summary>
    public int ShotIndex { get; private set; }

    /// <summary>How many sprays have started (salts the jitter so erratic guns don't repeat exactly).</summary>
    public int Spray { get; private set; }

    /// <summary>The viewmodel kick right now.</summary>
    public WeaponKick Kick;

    /// <summary>The render-only camera offset right now.</summary>
    public CameraKick Camera;

    private double _lastShot = -100;
    private WeaponDef? _def;
    private float _startPitch, _startYaw; // where the spray started; recovery never pulls past it
    private float _pendPitch, _pendYaw; // climb still to be recovered (radians)
    private float _shake, _shakeClock, _shakeScale = 1f;
    private float _punchPitch, _punchYaw, _punchRoll, _punchFov;

    /// <summary>Forget everything (on spawn or weapon change).</summary>
    public void Reset()
    {
        ShotIndex = 0;
        _lastShot = -100;
        _pendPitch = _pendYaw = 0;
        _shake = _punchPitch = _punchYaw = _punchRoll = _punchFov = 0;
        Kick = default;
        Camera = default;
    }

    /// <summary>
    /// One shot fired at time <paramref name="now"/>. Returns the aim kick to add (radians: pitch up, yaw right) and
    /// starts the visual shake, punch and viewmodel kick.
    /// </summary>
    public (float Pitch, float Yaw) Shot(WeaponDef d, Stance stance, float aimT, double now, float lookPitch, float lookYaw, float perkMult = 1f)
    {
        if (_def != d || now - _lastShot > Recoil.ResetTime(d))
        {
            if (_def != d)
                _pendPitch = _pendYaw = 0;
            ShotIndex = 0;
            Spray++;
            if (MathF.Abs(_pendPitch) < 0.002f && MathF.Abs(_pendYaw) < 0.002f)
            {
                _startPitch = lookPitch;
                _startYaw = lookYaw;
            }
        }
        _def = d;
        _lastShot = now;

        var (up, right) = Recoil.PatternDeg(d, ShotIndex, Spray);
        var (mv, mh) = Recoil.Multipliers(d, stance, aimT);
        var pitch = up * mv * perkMult * Deg; // perkMult: Tight Grip (Perks.RecoilMult)
        var yaw = right * mh * perkMult * Deg;
        ShotIndex = Math.Min(ShotIndex + 1, 10000);
        _pendPitch += pitch * d.RecoilRecoveryAmount;
        _pendYaw += yaw * d.RecoilRecoveryAmount;

        // Visuals: punch and shake are smaller through a magnifying optic, so a scope doesn't turn into a blur.
        var zoomDamp = float.Lerp(1f, 1f / MathF.Sqrt(MathF.Max(1f, d.AdsZoom)), Math.Clamp(aimT, 0f, 1f));
        var side = Recoil.Noise(Recoil.SeedOf(d), ShotIndex, Spray * 2 + 9) < 0 ? -1f : 1f;
        var scale = d.RecoilDeg > 0 ? 1f : 0f;
        _shake = MathF.Min(_shake + 1f, 1.6f);
        _shakeScale = zoomDamp * scale;
        _punchPitch = MathF.Min(_punchPitch + d.RecoilPunch * mv * zoomDamp * Deg, 3f * d.RecoilPunch * Deg);
        _punchYaw = Math.Clamp(_punchYaw + MathF.Sign(yaw) * d.RecoilPunch * 0.25f * mh * zoomDamp * Deg, -d.RecoilPunch * Deg, d.RecoilPunch * Deg);
        _punchRoll = Math.Clamp(_punchRoll + side * d.RecoilPunchRoll * Deg, -2f * d.RecoilPunchRoll * Deg, 2f * d.RecoilPunchRoll * Deg);
        _punchFov = MathF.Min(_punchFov + d.RecoilPunchFov * 0.01f, 2f * d.RecoilPunchFov * 0.01f);

        Kick.Back = MathF.Min(Kick.Back + d.RecoilKickBack * mv, 2.5f * d.RecoilKickBack);
        Kick.Up = MathF.Min(Kick.Up + d.RecoilKickUp * mv * Deg, 2.5f * d.RecoilKickUp * Deg);
        Kick.Roll = Math.Clamp(Kick.Roll + side * d.RecoilKickRoll * Deg, -2f * d.RecoilKickRoll * Deg, 2f * d.RecoilKickRoll * Deg);
        Kick.Side = Math.Clamp(Kick.Side + (yaw >= 0 ? 1f : -1f) * d.RecoilKickSide * mh, -2.5f * d.RecoilKickSide, 2.5f * d.RecoilKickSide);
        return (pitch, yaw);
    }

    /// <summary>
    /// Advances recovery and the visual decays by <paramref name="dt"/> seconds. Recovery pulls the aim back towards
    /// where the spray started (only part of the way, and never past it if you already pulled down yourself).
    /// </summary>
    public void Update(float dt, double now, ref float lookPitch, ref float lookYaw)
    {
        if (_def is not { } d || dt <= 0)
            return;

        if (now - _lastShot >= Recoil.RecoveryDelay(d) && (_pendPitch != 0 || _pendYaw != 0))
        {
            // Don't recover more than the distance back to the start (the player may have pulled down already).
            var climbed = lookPitch - _startPitch;
            _pendPitch = _pendPitch > 0 ? Math.Clamp(_pendPitch, 0f, MathF.Max(0f, climbed)) : Math.Clamp(_pendPitch, MathF.Min(0f, climbed), 0f);
            var drifted = World.WrapAngle(lookYaw - _startYaw);
            _pendYaw = _pendYaw > 0 ? Math.Clamp(_pendYaw, 0f, MathF.Max(0f, drifted)) : Math.Clamp(_pendYaw, MathF.Min(0f, drifted), 0f);
            var k = 1f - MathF.Exp(-d.RecoilRecovery * dt);
            var dp = _pendPitch * k;
            var dy = _pendYaw * k;
            lookPitch -= dp;
            lookYaw = World.WrapAngle(lookYaw - dy);
            _pendPitch -= dp;
            _pendYaw -= dy;
            if (MathF.Abs(_pendPitch) < 1e-5f && MathF.Abs(_pendYaw) < 1e-5f)
                _pendPitch = _pendYaw = 0;
        }

        static float Decay(float dt, float time) => MathF.Exp(-dt / MathF.Max(0.01f, time));
        var kd = Decay(dt, d.RecoilKickTime);
        Kick.Back *= kd;
        Kick.Up *= kd;
        Kick.Roll *= kd;
        Kick.Side *= kd;
        var pd = Decay(dt, d.RecoilPunchTime);
        _punchPitch *= pd;
        _punchYaw *= pd;
        _punchRoll *= pd;
        _punchFov *= pd;
        _shake *= Decay(dt, d.RecoilShakeTime);
        _shakeClock = (_shakeClock + dt) % 1000f;

        var amp = _shake * d.RecoilShake * _shakeScale * Deg;
        var w = MathF.Tau * d.RecoilShakeFreq * _shakeClock;
        Camera.Pitch = _punchPitch + amp * MathF.Sin(w);
        Camera.Yaw = _punchYaw + amp * 0.8f * MathF.Sin(w * 1.31f + 1.7f);
        Camera.Roll = _punchRoll + amp * 0.5f * MathF.Sin(w * 0.77f + 0.6f);
        Camera.Fov = _punchFov;
    }
}
