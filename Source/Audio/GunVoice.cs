using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A weapon file's optional "voice" block: pins parts of the weapon's sonic identity (<see cref="GunVoice"/>) instead
/// of deriving them. Every field is optional; see Weapons/README.md.
/// </summary>
public sealed class VoiceSpec
{
    /// <summary>Replaces the name hash every derived value comes from (a different seed = a different-sounding gun).</summary>
    public int? Seed;

    /// <summary>The main recorded firearm (a "gun" of the weapon's sample set, e.g. "Walther PPQ").</summary>
    public string? Recording;

    /// <summary>The second recorded firearm, mixed into the round-robin.</summary>
    public string? SecondRecording;

    /// <summary>Extra playback pitch on top of the caliber's (0.85-1.15).</summary>
    public float? Pitch;

    /// <summary>Tone tilt, -1 dark to +1 bright.</summary>
    public float? Tone;

    /// <summary>Body resonance amount, 0-1 (the receiver / barrel ring under the blast).</summary>
    public float? Body;

    /// <summary>Body resonance frequency in Hz (80-1500).</summary>
    public float? BodyHz;

    /// <summary>Transient (crack) emphasis, 0-1.</summary>
    public float? Crack;

    /// <summary>Mechanism click level, 0-1 (0 = none).</summary>
    public float? Click;

    /// <summary>Body length / dryness multiplier, 0.5-1.5 (lower = drier and shorter).</summary>
    public float? Tail;

    private static readonly string[] Keys = ["seed", "recording", "secondRecording", "pitch", "tone", "body", "bodyHz", "crack", "click", "tail"];

    /// <summary>Reads a "voice" block. Throws <see cref="WeaponFileException"/> on unknown keys or values out of range.</summary>
    public static VoiceSpec Read(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new WeaponFileException($"{where}: expected an object like {{ \"pitch\": 1.04, \"tone\": -0.3 }}");
        var v = new VoiceSpec();
        float Num(JsonElement x, string key, float min, float max)
        {
            var f = WeaponFile.Number(x, $"{where} \"{key}\"");
            return f >= min && f <= max ? f : throw new WeaponFileException($"{where}: \"{key}\" is {WeaponFile.Num(f)}, but must be between {WeaponFile.Num(min)} and {WeaponFile.Num(max)}");
        }
        foreach (var p in e.EnumerateObject())
            switch (p.Name)
            {
                case "seed": v.Seed = WeaponFile.Int(p.Value, $"{where} \"seed\""); break;
                case "recording": v.Recording = WeaponFile.Text(p.Value, $"{where} \"recording\""); break;
                case "secondRecording": v.SecondRecording = WeaponFile.Text(p.Value, $"{where} \"secondRecording\""); break;
                case "pitch": v.Pitch = Num(p.Value, p.Name, 0.85f, 1.15f); break;
                case "tone": v.Tone = Num(p.Value, p.Name, -1f, 1f); break;
                case "body": v.Body = Num(p.Value, p.Name, 0f, 1f); break;
                case "bodyHz": v.BodyHz = Num(p.Value, p.Name, 80f, 1500f); break;
                case "crack": v.Crack = Num(p.Value, p.Name, 0f, 1f); break;
                case "click": v.Click = Num(p.Value, p.Name, 0f, 1f); break;
                case "tail": v.Tail = Num(p.Value, p.Name, 0.5f, 1.5f); break;
                default: throw new WeaponFileException($"{where}: unknown key \"{p.Name}\". Known: {string.Join(", ", Keys)}");
            }
        return v;
    }

    /// <summary>The block as JSON (only the fields that are set).</summary>
    public string ToJson()
    {
        var parts = new List<string>();
        void F(string k, float? x)
        {
            if (x is { } f)
                parts.Add($"\"{k}\": {WeaponFile.Num(f)}");
        }
        if (Seed is { } s)
            parts.Add($"\"seed\": {s.ToString(CultureInfo.InvariantCulture)}");
        if (Recording != null)
            parts.Add($"\"recording\": {WeaponFile.Str(Recording)}");
        if (SecondRecording != null)
            parts.Add($"\"secondRecording\": {WeaponFile.Str(SecondRecording)}");
        F("pitch", Pitch);
        F("tone", Tone);
        F("body", Body);
        F("bodyHz", BodyHz);
        F("crack", Crack);
        F("click", Click);
        F("tail", Tail);
        return "{ " + string.Join(", ", parts) + " }";
    }
}

/// <summary>
/// A weapon's own sonic identity, derived deterministically from its definition: a hash of its name (or the "voice"
/// block's seed), its stats (caliber, length, fire rate, brightness, tail) and its rank among the weapons on its sample
/// set (by that hash, so it doesn't depend on file order or ids). Same weapons = same identities on every client and
/// every run; two weapons on the same sample set get different main recordings or a different character (tone, body,
/// crack, length), plus their own pitch, resonance, mechanism click and synth blend, so they don't sound alike. Baked once at start-up
/// (<see cref="GunSamples"/>); nothing here runs per shot.
/// </summary>
public sealed class GunVoice
{
    /// <summary>Main and second recorded firearm (indexes into the set's <see cref="GunSampleLibrary.SampleSet.Guns"/>; -1 = none).</summary>
    public int Main = -1, Second = -1;

    /// <summary>Their names ("" when the set has none).</summary>
    public string MainName = "", SecondName = "";

    /// <summary>Extra pitch on top of the caliber's (about 0.95-1.05).</summary>
    public float Pitch = 1f;

    /// <summary>Tone tilt around 1.2 kHz, -1 dark to +1 bright.</summary>
    public float Tilt;

    /// <summary>Body resonance: a band-pass ring at <see cref="BodyHz"/> with quality <see cref="BodyQ"/>, mixed in at <see cref="BodyGain"/>.</summary>
    public float BodyHz = 300f, BodyQ = 6f, BodyGain = 0.2f;

    /// <summary>Transient emphasis (0-1): a high-passed copy of the first few milliseconds on top.</summary>
    public float Crack = 0.5f;

    /// <summary>Mechanism click: kind (0 tick, 1 double tick, 2 clack), level relative to the blast's peak (0 = none) and delay in seconds.</summary>
    public int ClickKind;

    public float ClickLevel, ClickDelay = 0.03f;

    /// <summary>Multiplier on the set's synth blend.</summary>
    public float SynthBlend = 1f;

    /// <summary>Time constant (s) of the extra decay on the body after the blast (lower = drier).</summary>
    public float Dry = 0.12f;

    /// <summary>Multiplier on the shot's length.</summary>
    public float Length = 1f;

    /// <summary>The 32-bit seed every derived value comes from.</summary>
    public uint Seed;

    /// <summary>FNV-1a of a weapon's name (UTF-8): stable across runs, clients and weapon order.</summary>
    public static uint NameHash(string name)
    {
        var h = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(name))
            h = (h ^ b) * 16777619u;
        return h;
    }

    /// <summary>A uniform 0-1 value, number <paramref name="k"/> of the stream for <paramref name="seed"/> (SplitMix-style hash).</summary>
    public static float U(uint seed, int k)
    {
        var z = seed + 0x9E3779B9u * (uint)(k + 1);
        z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
        z = (z ^ (z >> 13)) * 0xC2B2AE35u;
        z ^= z >> 16;
        return (z >> 8) / 16777216f;
    }

    /// <summary>The seed a weapon's identity comes from: the "voice" block's seed, or the hash of its name.</summary>
    public static uint SeedOf(WeaponDef w) => w.Voice?.Seed is { } s ? (uint)s * 2654435761u ^ 0x5bd1e995u : NameHash(w.Name);

    /// <summary>
    /// Characters the recorded weapons of a class take in turn by their class rank: a 3 x 3 grid of tone tilt (bright,
    /// neutral, dark) and body resonance band (a low thump, a mid knock, a high ring), so any two differ in tone or in
    /// where the gun's body rings; body length and crack vary along with them.
    /// </summary>
    private static readonly (float Tilt, float Tail, float BodyHz, float Crack)[] Characters =
    [
        (0.9f, 0.7f, 230f, 0.85f),
        (-0.9f, 1.35f, 1350f, 0.3f),
        (0f, 1.0f, 560f, 0.95f),
        (0.9f, 1.0f, 1350f, 0.5f),
        (-0.9f, 0.7f, 230f, 0.65f),
        (0f, 1.35f, 230f, 0.4f),
        (0.9f, 1.35f, 560f, 0.7f),
        (-0.9f, 0.7f, 560f, 0.8f),
        (0f, 1.0f, 1350f, 0.6f),
    ];

    /// <summary>
    /// The identity of weapon <paramref name="w"/> on sample set <paramref name="set"/>. On a set's roster (every
    /// shipped and modded weapon file) the weapons, ranked by seed, take the set's recorded firearms in turn as their
    /// main one, and every recorded weapon of a class takes its own <see cref="Characters"/> entry; small hash and stat
    /// offsets come on top. Off the roster (a weapon made at run time) everything comes from the seed.
    /// </summary>
    public static GunVoice For(WeaponDef w, GunSampleLibrary.SampleSet? set)
    {
        var spec = w.Voice;
        var seed = SeedOf(w);
        float S(float lo, float hi, int k) => lo + (hi - lo) * U(seed, k);
        var c = w.Caliber;
        var v = new GunVoice { Seed = seed };

        var guns = set?.Guns ?? [];
        var g = guns.Count;
        var ranks = (Set: 0, Class: 0);
        var onRoster = set != null && set.Roster.TryGetValue(w.Name, out ranks);
        var rank = ranks.Set;
        var turn = onRoster && g > 0 ? rank / g : -1; // how many weapons before this one got the same main firearm
        (float Tilt, float Tail, float BodyHz, float Crack) ch = onRoster
            ? Characters[ranks.Class % Characters.Length] // every recorded weapon of a class (up to 9) has its own
            : (S(-0.7f, 0.7f, 3), S(0.8f, 1.2f, 12), 230f * MathF.Pow(5.9f, S(0f, 1f, 6)), S(0.3f, 0.95f, 7));

        // Recordings: main and second firearm of the set (or the voice block's).
        if (g > 0)
        {
            v.Main = onRoster ? (rank + set!.Index) % g : (int)(U(seed, 0) * g) % g;
            if (spec?.Recording is { } r && guns.FindIndex(x => x.Equals(r, StringComparison.OrdinalIgnoreCase)) is >= 0 and var ri)
                v.Main = ri;
            if (g > 1)
            {
                var step = onRoster ? turn % (g - 1) : (int)(U(seed, 1) * (g - 1)) % (g - 1);
                v.Second = (v.Main + 1 + step) % g;
                if (spec?.SecondRecording is { } r2 && guns.FindIndex(x => x.Equals(r2, StringComparison.OrdinalIgnoreCase)) is >= 0 and var r2i && r2i != v.Main)
                    v.Second = r2i;
            }
            v.MainName = guns[v.Main];
            v.SecondName = v.Second >= 0 ? guns[v.Second] : "";
        }

        // Pitch: +-3% by hash, a longer barrel a touch lower.
        v.Pitch = spec?.Pitch ?? Math.Clamp(S(0.97f, 1.03f, 2) * (1f + (0.4f - w.Length) * 0.05f), 0.93f, 1.07f);
        // Tone: the character, nudged by the brightness stat and the hash.
        v.Tilt = spec?.Tone ?? Math.Clamp(ch.Tilt + (w.Bright - 0.55f) * 0.4f + S(-0.1f, 0.1f, 13), -1f, 1f);
        // Body resonance: the character's band, a little lower for big calibers and long guns; quality by hash.
        var hz = ch.BodyHz * (1.1f - 0.2f * c) / MathF.Pow(Math.Clamp(w.Length, 0.15f, 1.2f) / 0.4f, 0.15f) * S(0.92f, 1.08f, 4);
        v.BodyHz = spec?.BodyHz ?? Math.Clamp(hz, 110f, 1500f);
        v.BodyQ = S(4f, 7f, 5);
        v.BodyGain = (spec?.Body ?? S(0.85f, 1f, 14)) * 0.6f;
        v.Crack = spec?.Crack ?? Math.Clamp(ch.Crack + (w.Bright - 0.5f) * 0.3f + S(-0.08f, 0.08f, 15), 0f, 1f);
        // Mechanism: automatic and burst guns and pistols cycle a bolt / slide right after the shot (sooner the faster
        // they fire); manual actions (bolt, lever, pump at low rates) don't click on the shot itself.
        var cycles = w.Mode != FireMode.Semi || w.Class == WeaponClass.Pistol || w.Rpm >= 200;
        v.ClickKind = (int)(U(seed, 8) * 3) % 3;
        v.ClickLevel = (spec?.Click ?? (cycles ? S(0.35f, 1f, 9) : 0f)) * 0.16f;
        v.ClickDelay = Math.Clamp(60f / Math.Max(w.Rpm, 1) * S(0.18f, 0.32f, 10), 0.01f, 0.05f);
        v.SynthBlend = S(0.6f, 1.45f, 11);
        // Dryness: the gun's tail stat sets the base; the character (or the voice block's tail) scales it.
        var tail = spec?.Tail ?? Math.Clamp(ch.Tail + S(-0.05f, 0.05f, 16), 0.5f, 1.5f);
        v.Dry = (0.04f + 0.15f * w.Tail) * tail * MathF.Sqrt(tail); // tail^1.5: the length steps stay clearly apart
        v.Length = Math.Clamp(0.75f + 0.25f * tail, 0.6f, 1.15f);
        return v;
    }

    /// <summary>One log line: "Walther PPQ + Bersa, pitch 1.02, tone -0.31, body 410 Hz x0.18, crack 0.55, click tick 0.11 @ 24 ms, blend x1.1, dry 82 ms".</summary>
    public string Describe() =>
        $"{(MainName == "" ? "synth" : MainName + (SecondName == "" ? "" : " + " + SecondName))}, pitch {Pitch:0.000}, tone {Tilt:+0.00;-0.00}, body {BodyHz:0} Hz x{BodyGain:0.00}, "
        + $"crack {Crack:0.00}, click {(ClickLevel <= 0 ? "none" : $"{ClickNames[ClickKind]} {ClickLevel:0.00} @ {ClickDelay * 1000:0} ms")}, blend x{SynthBlend:0.00}, dry {Dry * 1000:0} ms";

    private static readonly string[] ClickNames = ["tick", "double", "clack"];

    /// <summary>
    /// Which recordings (indexes into the set's shots) a weapon's variants play, in order: main gun, second gun, main
    /// gun's other takes, then the main gun's takes again (the bake treats a repeat a little differently), so about
    /// three in four shots are the main firearm. Without a main gun (no gun names) the set's first shots in order.
    /// </summary>
    public int[] Picks(GunSampleLibrary.SampleSet set, int count)
    {
        var main = new List<int>();
        var second = new List<int>();
        for (var i = 0; i < set.Shots.Count; i++)
        {
            var g = set.ShotGun(i);
            if (Main >= 0 && g == Main)
                main.Add(i);
            else if (Second >= 0 && g == Second)
                second.Add(i);
        }
        if (main.Count == 0)
            return Enumerable.Range(0, Math.Min(count, set.Shots.Count)).ToArray();
        var order = new List<int> { main[0] };
        if (second.Count > 0 && order.Count < count)
            order.Add(second[0]);
        for (var k = 1; k < main.Count && order.Count < count; k++)
            order.Add(main[k]);
        for (var r = 0; order.Count < count; r++)
            order.Add(main[r % main.Count]); // a repeat take: the bake treats it a little differently
        return order.ToArray();
    }
}
