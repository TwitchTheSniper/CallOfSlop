using System.Diagnostics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Settings > Audio "Frenzy gun sound": how much your gun's sound changes on a kill spree.</summary>
public enum FrenzyGunSoundMode : byte
{
    /// <summary>Everything: the class's layer, the driven (saturated, sub-heavy) shot, the pitch shift and the stereo spread at the top levels.</summary>
    Full,

    /// <summary>The class's layer at half strength and a gentler pitch shift; the gun's own shot stays clean and centred.</summary>
    Subtle,

    /// <summary>Your gun always sounds as it does outside a frenzy.</summary>
    Off,
}

/// <summary>The one sound that makes a class's frenzy gunfire unlike the others (on top of its thump, clacks, drive and echo).</summary>
public enum FrenzySoundSignature : byte
{
    /// <summary>Pistol: a ricochet whine, then a twangy western bass note and a spur ring.</summary>
    OutlawTwang,

    /// <summary>SMG: a buzzy square-wave burst with chiptune blips.</summary>
    SmgBuzz,

    /// <summary>Assault Rifle: a distorted hardbass kick, an off-beat hat and a "donk".</summary>
    HardbassThump,

    /// <summary>DMR: a marching snare with a rim shot, flammed at the top.</summary>
    MarchSnare,

    /// <summary>Sniper: a supersonic whip crack and a cinematic braam that swells with the level.</summary>
    WhipCrack,

    /// <summary>Shotgun: a gabber kick, doubled, then tripled.</summary>
    BoomDouble,

    /// <summary>LMG: an industrial clang, grinding metal and a belt rasp.</summary>
    GrindClank,

    /// <summary>Special: bit-crushed stutter repeats.</summary>
    GlitchStutter,
}

/// <summary>
/// One weapon class's frenzy gun sound (the "sound" section of its entry in Weapons/Frenzy/classes.json). Amounts are at
/// full intensity; <see cref="Levels"/> scales them per frenzy level.
/// </summary>
public sealed class FrenzySoundDef
{
    public FrenzySoundSignature Signature;
    public string About = "";

    /// <summary>Intensity (0-1) at frenzy levels 1-6: six numbers that never go down.</summary>
    public float[] Levels = [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f];

    /// <summary>The class layer's loudness relative to the gun's own shot (0-0.6).</summary>
    public float Layer = 0.4f;

    /// <summary>How much of the driven copy (saturated, low end boosted) crossfades in over the clean shot (0-0.6).</summary>
    public float Drive = 0.3f;

    /// <summary>Sub thump in the layer (0-1).</summary>
    public float Sub = 0.4f;

    /// <summary>Mechanical clacks in the layer (0-1).</summary>
    public float Clack = 0.3f;

    /// <summary>Echo and tail extension of the layer (0-1).</summary>
    public float Tail = 0.4f;

    /// <summary>Stereo spread of the layer at levels 5-6 (0-1; Full setting only).</summary>
    public float Width = 0.4f;

    /// <summary>Playback rate of your gun at full intensity (0.9-1.1; below 1 lower and heavier, above 1 snappier).</summary>
    public float Pitch = 1f;
}

/// <summary>What one of your shots plays at a frenzy level: gains relative to the normal shot, the layer tier, pitch and spread.</summary>
public readonly record struct FrenzyShotPlan(float BaseGain, float DriveGain, float LayerGain, int Tier, float Pitch, float Spread)
{
    /// <summary>Anything to add to the normal shot.</summary>
    public bool Active => LayerGain > 0f || DriveGain > 0f || Pitch != 1f;
}

/// <summary>The baked frenzy buffers: per class the layer tiers and variants, per weapon the driven copy, and their loudness.</summary>
public sealed class FrenzyGunBank
{
    /// <summary>[class][tier][variant] 16-bit mono PCM.</summary>
    public required short[][][][] Layers;

    /// <summary>RMS of each layer over <see cref="GunSamples.LoudnessWindow"/>.</summary>
    public required float[][][] LayerRms;

    /// <summary>[weapon] the driven copy of the weapon's first shot variant.</summary>
    public required short[][] Drive;

    /// <summary>[weapon] RMS of the driven copy and of the weapon's normal shot (first variant) over the loudness window.</summary>
    public required float[] DriveRms, ShotRms;

    public double BakeMs;
    public int Count;
    public long Bytes;
}

/// <summary>
/// Per-weapon-class frenzy gun sound: as your frenzy level rises (1-6), your own gun gains the character of your frenzy
/// class (the class of the weapon that got your most recent kill, like the frenzy visuals and music): a class layer
/// (<see cref="FrenzySoundSignature"/> plus sub thump, mechanical clacks and echo, three tiers baked at load), a driven
/// copy of your shot crossfaded in (saturation, more low end), a small pitch shift and, at levels 5-6, the layer spread
/// left and right. Everything is baked at start-up; a shot only picks buffers and gains (no allocation, no DSP), on its own
/// small voice pool so it never steals gunfire voices. Your normal shot is unchanged, so the frenzy music's level-6
/// loudness reference (<see cref="FrenzyLayer.SfxReferenceRms"/>) still holds; a level-6 shot is at most
/// <see cref="MaxLoudnessRatio"/> times as loud. Other players' guns don't change, so enemies stay recognisable by ear.
/// </summary>
public static class FrenzyGunSound
{
    /// <summary>Layer tiers: levels 1-2, 3-4 and 5-6.</summary>
    public const int Tiers = 3;

    /// <summary>Layer variants per tier (two decorrelated takes: alternated, or played left and right when spread).</summary>
    public const int Variants = 2;

    /// <summary>What <see cref="FrenzyGunSoundMode.Subtle"/> keeps of the layer and pitch shift.</summary>
    public const float SubtleScale = 0.5f;

    /// <summary>How much of the driven copy's gain is taken off the clean shot (the rest adds a little loudness).</summary>
    public const float DriveCrossfade = 0.6f;

    /// <summary>Layer gain on a suppressed gun (it stays quiet).</summary>
    public const float SuppressedLayer = 0.5f;

    /// <summary>The loudest a level-6 shot may be next to the normal shot (RMS over the loudness window; checked by the self-test).</summary>
    public const float MaxLoudnessRatio = 1.41f;

    /// <summary>The loudness the class layers are baked at (RMS over the loudness window, before the limiter).</summary>
    public const float LayerRmsTarget = 0.25f;

    /// <summary>The driven copy is this long at most (the blast and body; the clean shot carries the tail).</summary>
    public const float DriveSeconds = 0.32f;

    /// <summary>The layer tier for a frenzy level.</summary>
    public static int TierOf(int level) => level <= 2 ? 0 : level <= 4 ? 1 : 2;

    public static readonly FrenzyShotPlan Idle = new(1f, 0f, 0f, 0, 1f, 0f);

    private static FrenzySoundDef[]? _defs;
    private static readonly object Lock = new();

    /// <summary>Problems in the "sound" sections the last time they were read.</summary>
    public static readonly List<string> Notes = new();

    /// <summary>Every class's sound, indexed by <see cref="WeaponClass"/> (the data file over the built-in defaults).</summary>
    public static IReadOnlyList<FrenzySoundDef> All
    {
        get
        {
            var d = _defs;
            if (d != null)
                return d;
            lock (Lock)
            {
                if (_defs == null)
                {
                    Notes.Clear();
                    _defs = Parse(FrenzyClasses.All, Notes);
                    foreach (var n in Notes)
                        Console.WriteLine($"[frenzygun] classes.json: {n}");
                }
                return _defs;
            }
        }
    }

    /// <summary>The sound for a class (an unknown value gets Special's).</summary>
    public static FrenzySoundDef Get(WeaponClass c)
    {
        var all = All;
        var i = (int)c;
        return i >= 0 && i < all.Count ? all[i] : all[^1];
    }

    /// <summary>What a shot plays at <paramref name="level"/> for frenzy class <paramref name="cls"/>.</summary>
    public static FrenzyShotPlan Plan(int level, WeaponClass cls, FrenzyGunSoundMode mode) =>
        mode == FrenzyGunSoundMode.Off || level <= 0 ? Idle : Plan(level, Get(cls), mode);

    /// <summary>What a shot plays at <paramref name="level"/> with <paramref name="d"/> (pure; no allocation).</summary>
    public static FrenzyShotPlan Plan(int level, FrenzySoundDef d, FrenzyGunSoundMode mode)
    {
        if (mode == FrenzyGunSoundMode.Off || level <= 0)
            return Idle;
        var k = d.Levels[Math.Min(level, d.Levels.Length) - 1];
        var full = mode == FrenzyGunSoundMode.Full;
        if (!full)
            k *= SubtleScale;
        var drive = full ? d.Drive * k : 0f;
        var tier = TierOf(level);
        return new FrenzyShotPlan(1f - DriveCrossfade * drive, drive, d.Layer * k, tier, 1f + (d.Pitch - 1f) * k,
            full && tier == Tiers - 1 ? d.Width * k : 0f);
    }

    /// <summary>
    /// The voice gains for a shot played at <paramref name="gain"/>: the clean shot, the driven copy and each layer voice
    /// (two voices when spread), matched to the weapon's own loudness and capped at 1 (OpenAL's limit).
    /// </summary>
    public static (float Base, float Drive, float Layer) Gains(in FrenzyShotPlan p, float gain, float shotRms, float driveRms, float layerRms, bool suppressed)
    {
        var drive = suppressed || p.DriveGain <= 0f || driveRms <= 1e-6f ? 0f : MathF.Min(1f, gain * p.DriveGain * shotRms / driveRms);
        var layerGain = p.LayerGain * (suppressed ? SuppressedLayer : 1f) * (p.Spread > 0f ? 0.7071f : 1f); // two voices spread: equal power
        var layer = layerGain <= 0f || layerRms <= 1e-6f ? 0f : MathF.Min(1f, gain * layerGain * shotRms / layerRms);
        return (gain * (drive > 0f ? p.BaseGain : 1f), drive, layer);
    }

    /// <summary>The class layer's playback rate for a weapon: bigger calibers a little lower.</summary>
    public static float LayerPitch(WeaponDef w) => Math.Clamp(1f + (0.5f - w.Caliber) * 0.3f, 0.85f, 1.15f);

    // ---- Data -----------------------------------------------------------------------------------------

    /// <summary>The "sound" section of each entry over the built-in defaults; problems go to <paramref name="notes"/>.</summary>
    public static FrenzySoundDef[] Parse(IReadOnlyList<FrenzyClassEntry> entries, List<string> notes)
    {
        var classes = Enum.GetValues<WeaponClass>();
        var list = new FrenzySoundDef[classes.Length];
        for (var i = 0; i < classes.Length; i++)
            list[i] = Default(classes[i]);
        foreach (var e in entries)
            if ((int)e.Class < list.Length)
                list[(int)e.Class] = ParseSection(e.Class, e.Sound, notes);
        return list;
    }

    /// <summary>One class's "sound" section over its default (an empty or missing section keeps the default, with a note).</summary>
    public static FrenzySoundDef ParseSection(WeaponClass c, JsonElement s, List<string> notes)
    {
        var d = Default(c);
        if (s.ValueKind != JsonValueKind.Object || !s.EnumerateObject().Any())
        {
            notes.Add($"{c}: no \"sound\" section (built-in sound used)");
            return d;
        }
        if (s.TryGetProperty("signature"u8, out var sig))
        {
            if (sig.ValueKind == JsonValueKind.String && Enum.TryParse<FrenzySoundSignature>(sig.GetString(), true, out var sg) && Enum.IsDefined(sg))
                d.Signature = sg;
            else
                notes.Add($"{c}: unknown sound signature {sig} ({string.Join(", ", Enum.GetNames<FrenzySoundSignature>())})");
        }
        if (s.TryGetProperty("about"u8, out var ab) && ab.ValueKind == JsonValueKind.String)
            d.About = ab.GetString() ?? "";
        if (s.TryGetProperty("levels"u8, out var lv))
        {
            var ok = lv.ValueKind == JsonValueKind.Array && lv.GetArrayLength() == FrenzyMeterCore.MaxLevel;
            var levels = new float[FrenzyMeterCore.MaxLevel];
            if (ok)
            {
                var i = 0;
                foreach (var x in lv.EnumerateArray())
                {
                    ok &= x.ValueKind == JsonValueKind.Number && x.TryGetSingle(out levels[i]) && float.IsFinite(levels[i]) && levels[i] is >= 0f and <= 1f
                          && (i == 0 || levels[i] >= levels[i - 1]);
                    i++;
                }
            }
            if (ok)
                d.Levels = levels;
            else
                notes.Add($"{c}: \"levels\" must be {FrenzyMeterCore.MaxLevel} numbers from 0 to 1 that never go down (built-in levels used)");
        }
        float Num(string key, float value, float min, float max)
        {
            if (!s.TryGetProperty(key, out var v))
                return value;
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetSingle(out var f) || !float.IsFinite(f))
            {
                notes.Add($"{c}: \"{key}\" isn't a number");
                return value;
            }
            if (f < min || f > max)
                notes.Add($"{c}: \"{key}\" clamped to {min}..{max}");
            return Math.Clamp(f, min, max);
        }
        d.Layer = Num("layer", d.Layer, 0f, 0.6f);
        d.Drive = Num("drive", d.Drive, 0f, 0.6f);
        d.Sub = Num("sub", d.Sub, 0f, 1f);
        d.Clack = Num("clack", d.Clack, 0f, 1f);
        d.Tail = Num("tail", d.Tail, 0f, 1f);
        d.Width = Num("width", d.Width, 0f, 1f);
        d.Pitch = Num("pitch", d.Pitch, 0.9f, 1.1f);
        return d;
    }

    /// <summary>The built-in sounds (the same numbers as the shipped classes.json).</summary>
    public static FrenzySoundDef Default(WeaponClass c) => c switch
    {
        WeaponClass.Pistol => Def(FrenzySoundSignature.OutlawTwang, "Ricochet whine, twangy bass, spur ring, canyon echo", [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f], 0.38f, 0.25f, 0.25f, 0.55f, 0.55f, 0.45f, 1.03f),
        WeaponClass.SMG => Def(FrenzySoundSignature.SmgBuzz, "Buzzy square-wave bursts and chiptune blips, tight and dry", [0.2f, 0.35f, 0.5f, 0.65f, 0.82f, 1f], 0.32f, 0.3f, 0.15f, 0.35f, 0.15f, 0.5f, 1.05f),
        WeaponClass.AssaultRifle => Def(FrenzySoundSignature.HardbassThump, "Distorted hardbass kick under every shot, off-beat hat, donk", [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f], 0.4f, 0.35f, 0.7f, 0.3f, 0.3f, 0.35f, 0.98f),
        WeaponClass.DMR => Def(FrenzySoundSignature.MarchSnare, "Marching snare and rim shot, flammed at the top", [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f], 0.42f, 0.25f, 0.4f, 0.45f, 0.35f, 0.4f, 1.02f),
        WeaponClass.Sniper => Def(FrenzySoundSignature.WhipCrack, "Supersonic whip crack and a cinematic braam with a long tail", [0.1f, 0.25f, 0.4f, 0.55f, 0.78f, 1f], 0.5f, 0.3f, 0.6f, 0.2f, 0.8f, 0.6f, 0.96f),
        WeaponClass.Shotgun => Def(FrenzySoundSignature.BoomDouble, "Gabber kick boom, doubled then tripled, heavy drive", [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f], 0.48f, 0.45f, 0.8f, 0.4f, 0.3f, 0.4f, 0.97f),
        WeaponClass.LMG => Def(FrenzySoundSignature.GrindClank, "Industrial clang, grinding metal, belt rasp", [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f], 0.36f, 0.4f, 0.5f, 0.6f, 0.25f, 0.45f, 0.97f),
        _ => Def(FrenzySoundSignature.GlitchStutter, "Bit-crushed stutter repeats, wide and weird", [0.15f, 0.3f, 0.45f, 0.6f, 0.8f, 1f], 0.42f, 0.35f, 0.3f, 0.2f, 0.35f, 0.7f, 1.04f),
    };

    private static FrenzySoundDef Def(FrenzySoundSignature sig, string about, float[] levels, float layer, float drive, float sub, float clack, float tail, float width, float pitch) =>
        new() { Signature = sig, About = about, Levels = levels, Layer = layer, Drive = drive, Sub = sub, Clack = clack, Tail = tail, Width = width, Pitch = pitch };

    // ---- Bake -----------------------------------------------------------------------------------------

    /// <summary>Bakes every class's layers and every weapon's driven copy from the game's shots (parallel; no OpenAL needed).</summary>
    public static FrenzyGunBank Bake(Audio.SoundSet set)
    {
        var sw = Stopwatch.StartNew();
        var classes = Enum.GetValues<WeaponClass>();
        var defs = All;
        var n = set.Shots.Length;
        var bank = new FrenzyGunBank
        {
            Layers = new short[classes.Length][][][],
            LayerRms = new float[classes.Length][][],
            Drive = new short[n][],
            DriveRms = new float[n],
            ShotRms = new float[n],
        };
        for (var c = 0; c < classes.Length; c++)
        {
            bank.Layers[c] = new short[Tiers][][];
            bank.LayerRms[c] = new float[Tiers][];
            for (var t = 0; t < Tiers; t++)
            {
                bank.Layers[c][t] = new short[Variants][];
                bank.LayerRms[c][t] = new float[Variants];
            }
        }
        var jobs = new List<Action>();
        for (var c = 0; c < classes.Length; c++)
            for (var t = 0; t < Tiers; t++)
                for (var v = 0; v < Variants; v++)
                {
                    var (cc, tt, vv) = (c, t, v);
                    jobs.Add(() =>
                    {
                        var pcm = Synth.FrenzyLayer(classes[cc], defs[cc], tt, vv);
                        bank.Layers[cc][tt][vv] = pcm;
                        bank.LayerRms[cc][tt][vv] = Rms(pcm);
                    });
                }
        for (var w = 0; w < n; w++)
        {
            var ww = w;
            jobs.Add(() =>
            {
                var shot = set.Shots[ww][0];
                bank.ShotRms[ww] = Rms(shot);
                var drive = Synth.FrenzyDrive(shot);
                bank.Drive[ww] = drive;
                bank.DriveRms[ww] = Rms(drive);
            });
        }
        Parallel.For(0, jobs.Count, j => jobs[j]());
        foreach (var c in bank.Layers)
            foreach (var t in c)
                foreach (var v in t)
                {
                    bank.Count++;
                    bank.Bytes += v.Length * 2L;
                }
        foreach (var d in bank.Drive)
        {
            bank.Count++;
            bank.Bytes += d.Length * 2L;
        }
        bank.BakeMs = sw.Elapsed.TotalMilliseconds;
        return bank;
    }

    /// <summary>RMS of 16-bit PCM over <see cref="GunSamples.LoudnessWindow"/> (0-1 scale).</summary>
    public static float Rms(short[] pcm)
    {
        var n = Math.Min(GunSamples.LoudnessWindow, pcm.Length);
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            var x = pcm[i] / 32768.0;
            sum += x * x;
        }
        return (float)Math.Sqrt(sum / Math.Max(1, n));
    }
}

/// <summary>The frenzy gun sound bakes (<see cref="FrenzyGunSound"/>), using the synth's building blocks.</summary>
public static partial class Synth
{
    /// <summary>
    /// A class's frenzy layer at <paramref name="tier"/> (0-2): its signature plus sub thump and clacks, echo and
    /// saturation that all grow with the tier. <paramref name="variant"/> changes the random detail.
    /// </summary>
    public static short[] FrenzyLayer(WeaponClass cls, FrenzySoundDef d, int tier, int variant)
    {
        var rng = new Random(4242 + (int)cls * 131 + tier * 17 + variant * 7919);
        float V(float x, float amount = 0.06f) => x * (1f + ((float)rng.NextDouble() - 0.5f) * 2 * amount);
        var k = (tier + 1) / (float)FrenzyGunSound.Tiers; // 1/3, 2/3, 1
        var tail = d.Tail * k;
        var whip = d.Signature == FrenzySoundSignature.WhipCrack;
        var b = new Buf(MathF.Min(0.3f + tail * 0.7f + (whip ? 0.5f + 0.6f * k : 0f), 1.6f));

        if (d.Sub > 0)
            Tone(b, 0, d.Sub * (0.5f + 0.5f * k), V(120), V(40), 0.035f, 0.001f, 0.06f + 0.06f * k); // sub thump
        if (d.Clack > 0)
        {
            Clunk(b, rng, V(0.02f), d.Clack * 0.6f, V(1.1f)); // mechanical clacks
            if (tier >= 1)
                Click(b, rng, V(0.048f), d.Clack * 0.5f, V(1.3f));
            if (tier >= 2)
                Click(b, rng, V(0.07f), d.Clack * 0.4f, V(0.9f));
        }

        switch (d.Signature)
        {
            case FrenzySoundSignature.OutlawTwang:
                Tone(b, V(0.004f), 0.55f, V(3400), V(1500), 0.12f + 0.06f * k, 0.002f, 0.07f + 0.08f * k); // ricochet whine
                if (tier >= 1)
                    Tone(b, 0.01f, 0.5f, V(110), V(98), 0.05f, 0.002f, 0.12f + 0.1f * k, harmonics: 0.7f); // twangy bass
                if (tier >= 2)
                    Tone(b, 0.06f, 0.3f, V(2600), V(2500), 1, 0.003f, 0.12f); // spur ring
                break;
            case FrenzySoundSignature.SmgBuzz:
                Tone(b, 0, 0.6f, V(190), V(170), 0.02f, 0.0008f, 0.03f + 0.02f * k, harmonics: 1f, length: 0.06f + 0.03f * k); // buzz
                Noise(b, rng, 0, 0.35f + 0.2f * k, 0.0005f, 0.012f, 7000, highpass: true);
                if (tier >= 1)
                    Tone(b, 0.004f, 0.35f, V(380), V(340), 0.02f, 0.0008f, 0.03f, harmonics: 1f, length: 0.06f);
                if (tier >= 2)
                    Tone(b, 0.008f, 0.25f, V(1520), V(1200), 0.015f, 0.0005f, 0.02f, harmonics: 0.6f, length: 0.05f); // chip blip
                break;
            case FrenzySoundSignature.HardbassThump:
                Tone(b, 0, 0.8f, V(170), V(50), 0.028f, 0.001f, 0.09f + 0.05f * k, harmonics: 0.4f + 0.3f * k); // distorted kick
                Noise(b, rng, 0, 0.3f, 0.0006f, 0.006f, 5000, highpass: true); // kick click
                if (tier >= 1)
                    Noise(b, rng, V(0.075f), 0.18f + 0.1f * k, 0.001f, 0.02f, 8000, highpass: true); // off-beat hat
                if (tier >= 2)
                    Tone(b, V(0.075f), 0.3f, V(55), V(52), 1, 0.004f, 0.07f, harmonics: 1f); // donk
                break;
            case FrenzySoundSignature.MarchSnare:
                Noise(b, rng, V(0.003f), 0.6f, 0.0008f, 0.05f + 0.04f * k, 4500, highpass: true); // snare wires
                Tone(b, 0.003f, 0.5f, V(240), V(185), 0.02f, 0.001f, 0.045f); // drum head
                if (tier >= 1)
                    Click(b, rng, 0.002f, 0.6f, V(0.7f)); // rim shot
                if (tier >= 2)
                {
                    Noise(b, rng, V(0.022f), 0.4f, 0.0008f, 0.05f, 4500, highpass: true); // flam
                    Tone(b, 0.022f, 0.35f, V(240), V(185), 0.02f, 0.001f, 0.04f);
                }
                break;
            case FrenzySoundSignature.WhipCrack:
                Noise(b, rng, 0, 1f, 0.0002f, 0.0025f + 0.001f * k, 9000, highpass: true); // whip crack
                Tone(b, 0.001f, 0.35f, V(6000), V(1800), 0.01f, 0.0003f, 0.01f); // its zip
                Tone(b, 0.02f, 0.45f + 0.15f * k, V(58), V(55), 1, 0.02f + 0.03f * k, 0.25f + 0.35f * k, harmonics: 0.7f, length: 0.5f + 0.9f * k); // braam
                if (tier >= 1)
                    Tone(b, 0.025f, 0.3f, V(87), V(82), 1, 0.03f, 0.25f + 0.25f * k, harmonics: 0.5f, length: 0.4f + 0.7f * k); // its fifth
                if (tier >= 2)
                    Noise(b, rng, 0.02f, 0.25f, 0.05f, 0.4f, 400, length: 1.1f); // rumble
                break;
            case FrenzySoundSignature.BoomDouble:
                Tone(b, 0, 0.9f, V(150), V(46), 0.03f, 0.001f, 0.11f, harmonics: 0.9f); // gabber kick
                Noise(b, rng, 0, 0.4f, 0.0008f, 0.02f, 2500);
                if (tier >= 1)
                    Tone(b, V(0.05f), 0.7f, V(150), V(46), 0.03f, 0.001f, 0.1f, harmonics: 1f); // the double
                if (tier >= 2)
                    Tone(b, V(0.1f), 0.55f, V(150), V(46), 0.03f, 0.001f, 0.09f, harmonics: 1f); // and the triple
                break;
            case FrenzySoundSignature.GrindClank:
                Tone(b, 0.004f, 0.5f, V(620), V(600), 1, 0.0008f, 0.07f + 0.04f * k, harmonics: 0.5f); // clang
                Tone(b, 0.004f, 0.3f, V(1590), V(1560), 1, 0.0008f, 0.05f);
                Noise(b, rng, 0.01f, 0.3f + 0.2f * k, 0.01f, 0.05f + 0.04f * k, 1300); // grind
                if (tier >= 1)
                    Noise(b, rng, 0.01f, 0.2f, 0.01f, 0.06f, 3500, highpass: true); // belt rasp
                if (tier >= 2)
                    Clunk(b, rng, V(0.06f), 0.6f, V(0.6f)); // a heavy second clank
                break;
            default: // GlitchStutter
            {
                var reps = 2 + tier;
                var gap = V(0.022f, 0.15f);
                for (var r = 0; r < reps; r++)
                {
                    var t = r * gap;
                    var g = MathF.Pow(0.75f, r);
                    Tone(b, t, 0.5f * g, V(900f + 900f * (float)rng.NextDouble(), 0.01f), V(300, 0.2f), 0.01f, 0.0005f, 0.012f, harmonics: 1f, length: gap * 0.8f);
                    Noise(b, rng, t, 0.35f * g, 0.0003f, 0.008f, 6000, highpass: true, length: gap * 0.7f);
                }
                break;
            }
        }

        Echo(b, tail, 0.7f + 1.3f * tail);
        var drive = 1f + 2.5f * d.Drive * k;
        for (var i = 0; i < b.Len; i++)
            b.S[i] = MathF.Tanh(b.S[i] * drive);
        if (d.Signature == FrenzySoundSignature.GlitchStutter)
        {
            // Bit crush: sample-and-hold and coarse steps, rougher each tier.
            var hold = 2 + 2 * tier;
            var steps = 24f - 6f * tier;
            var held = 0f;
            for (var i = 0; i < b.Len; i++)
            {
                if (i % hold == 0)
                    held = MathF.Round(b.S[i] * steps) / steps;
                b.S[i] = held;
            }
        }
        return FinishRms(b);
    }

    /// <summary>
    /// A layer normalised to <see cref="FrenzyGunSound.LayerRmsTarget"/> over the loudness window (so its play gain stays
    /// well under OpenAL's cap of 1 and scales evenly with the level), through the gun bakes' look-ahead limiter, with a
    /// 2 ms fade-out.
    /// </summary>
    private static short[] FinishRms(Buf b)
    {
        var win = Math.Min(b.Len, GunSamples.LoudnessWindow);
        double sum = 0;
        for (var i = 0; i < win; i++)
            sum += b.S[i] * (double)b.S[i];
        var rms = (float)Math.Sqrt(sum / Math.Max(1, win));
        var gain = MathF.Min(FrenzyGunSound.LayerRmsTarget / MathF.Max(rms, 1e-6f), 40f);
        for (var i = 0; i < b.Len; i++)
            b.S[i] = float.IsFinite(b.S[i]) ? b.S[i] * gain : 0f;
        GunSamples.Limit(b.S, GunSamples.LimiterThreshold);
        var fade = Math.Min(b.Len, Sr / 500);
        var pcm = new short[b.Len];
        for (var i = 0; i < b.Len; i++)
        {
            var f = i >= b.Len - fade ? (b.Len - i) / (float)fade : 1f;
            pcm[i] = (short)Math.Clamp((int)MathF.Round(b.S[i] * f * 32767f), -32767, 32767);
        }
        return pcm;
    }

    /// <summary>
    /// The driven copy of a shot: its first <see cref="FrenzyGunSound.DriveSeconds"/> with the low end boosted (a
    /// two-pole low-pass at 140 Hz added back), saturated, faded out and peak-normalised.
    /// </summary>
    public static short[] FrenzyDrive(short[] shot)
    {
        var n = Math.Max(1, Math.Min(shot.Length, (int)(FrenzyGunSound.DriveSeconds * Sr)));
        var x = new float[n];
        var a = 1f - MathF.Exp(-2f * MathF.PI * 140f / Sr);
        float lp = 0, lp2 = 0, peak = 1e-6f;
        for (var i = 0; i < n; i++)
        {
            var s = i < shot.Length ? shot[i] / 32768f : 0f;
            lp += a * (s - lp);
            lp2 += a * (lp - lp2);
            x[i] = MathF.Tanh((s + 2.2f * lp2) * 2.6f);
        }
        var f0 = (int)(n * 0.6f);
        for (var i = f0; i < n; i++)
            x[i] *= 0.5f + 0.5f * MathF.Cos(MathF.PI * (i - f0) / Math.Max(1, n - f0));
        for (var i = 0; i < n; i++)
            peak = MathF.Max(peak, MathF.Abs(x[i]));
        var scale = 0.9f / peak;
        var pcm = new short[n];
        for (var i = 0; i < n; i++)
            pcm[i] = (short)Math.Clamp((int)MathF.Round(x[i] * scale * 32767f), -32767, 32767);
        return pcm;
    }
}
