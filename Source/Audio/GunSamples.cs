using System.Diagnostics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Recorded gunshot layers (Audio/Samples/Guns: manifest.json plus 16-bit mono WAVs made by
/// tools/audio/make_gun_samples.py). Every weapon whose class, sound style and caliber match a manifest rule gets its
/// shots baked at start-up from that set's recordings: its own pick of recorded firearms and its own treatment
/// (<see cref="GunVoice"/>: pitch, tone, body resonance, crack, click, dryness), pitched by caliber, trimmed by its tail,
/// blended with the start of its own synthesised shot (crack, thump, mechanism) and loudness-matched to the synth
/// (with the limiter held back so the transient stays sharp). Anything missing (the manifest, a
/// file, a whole set) falls back to the synthesised sound, so the game never depends on these files.
/// </summary>
public sealed class GunSampleLibrary
{
    /// <summary>The folder under the game folder that holds the manifest and the WAVs.</summary>
    public const string FolderName = "Audio/Samples/Guns";

    /// <summary>One set of recordings: near shots (2-4 variants) and mid-distance tails for the far layer.</summary>
    public sealed class SampleSet
    {
        public required string Name;
        public string About = "";
        /// <summary>The weapon caliber the set sounds right at unpitched; bigger calibers play it lower.</summary>
        public float RefCaliber = 0.5f;
        /// <summary>How much of the weapon's own synthesised shot is mixed in (at equal loudness).</summary>
        public float SynthBlend = 0.2f;
        public readonly List<float[]> Shots = new();
        public readonly List<float[]> Tails = new();
        public readonly List<string> ShotFiles = new();

        /// <summary>The recorded firearms of the near shots (the manifest's "gun", in order of first appearance).</summary>
        public readonly List<string> Guns = new();

        private readonly List<int> _shotGun = new();

        /// <summary>Which of <see cref="Guns"/> near shot <paramref name="shot"/> was recorded from (-1 = untagged).</summary>
        public int ShotGun(int shot) => shot < _shotGun.Count ? _shotGun[shot] : -1;

        /// <summary>Adds a near shot, recorded from <paramref name="gun"/> ("" = untagged).</summary>
        public void AddShot(float[] pcm, string file, string gun)
        {
            Shots.Add(pcm);
            ShotFiles.Add(file);
            var g = -1;
            if (gun.Length > 0)
            {
                g = Guns.IndexOf(gun);
                if (g < 0)
                {
                    Guns.Add(gun);
                    g = Guns.Count - 1;
                }
            }
            _shotGun.Add(g);
        }
    }

    /// <summary>A manifest rule: the first one matching a weapon picks its set.</summary>
    public sealed record Rule(WeaponClass Class, SoundStyle[] Sounds, float MinCaliber, float MaxCaliber, float MaxRpm, string Set);

    public readonly Dictionary<string, SampleSet> Sets = new(StringComparer.Ordinal);
    public readonly List<Rule> Rules = new();

    /// <summary>Missing or unreadable files and manifest problems (logged; the synth covers them).</summary>
    public readonly List<string> Problems = new();

    public string Folder { get; private set; } = "";
    public bool ManifestFound { get; private set; }
    public int FilesListed { get; private set; }
    public int FilesLoaded { get; private set; }
    public long BytesLoaded { get; private set; }
    public double LoadMs { get; private set; }
    public string Credits { get; private set; } = "";

    /// <summary>An empty library: every weapon keeps its synthesised sound.</summary>
    public static readonly GunSampleLibrary Empty = new();

    private static GunSampleLibrary? _shared;
    private static readonly object SharedLock = new();

    /// <summary>The game's library (loaded once from the game folder on first use).</summary>
    public static GunSampleLibrary Shared
    {
        get
        {
            lock (SharedLock)
                return _shared ??= Load(Path.Combine(AppContext.BaseDirectory, FolderName));
        }
    }

    /// <summary>Loads a manifest folder. Never throws: problems are listed in <see cref="Problems"/>.</summary>
    public static GunSampleLibrary Load(string folder)
    {
        var sw = Stopwatch.StartNew();
        var lib = new GunSampleLibrary { Folder = folder };
        var manifest = Path.Combine(folder, "manifest.json");
        if (!File.Exists(manifest))
        {
            lib.Problems.Add($"no {manifest}");
            lib.LoadMs = sw.Elapsed.TotalMilliseconds;
            return lib;
        }
        try
        {
            lib.ManifestFound = true;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = doc.RootElement;
            if (root.TryGetProperty("credits"u8, out var cr))
                lib.Credits = cr.GetString() ?? "";
            if (root.TryGetProperty("sets"u8, out var sets))
                foreach (var p in sets.EnumerateObject())
                {
                    var s = new SampleSet { Name = p.Name };
                    if (p.Value.TryGetProperty("about"u8, out var a))
                        s.About = a.GetString() ?? "";
                    if (p.Value.TryGetProperty("refCaliber"u8, out var rc))
                        s.RefCaliber = rc.GetSingle();
                    if (p.Value.TryGetProperty("synthBlend"u8, out var sb))
                        s.SynthBlend = Math.Clamp(sb.GetSingle(), 0f, 1f);
                    lib.Sets[p.Name] = s;
                }
            if (root.TryGetProperty("rules"u8, out var rules))
                foreach (var r in rules.EnumerateArray())
                {
                    if (!Enum.TryParse<WeaponClass>(r.GetProperty("class"u8).GetString(), out var cls))
                    {
                        lib.Problems.Add($"rule with unknown class {r.GetProperty("class"u8)}");
                        continue;
                    }
                    var sounds = r.TryGetProperty("sounds"u8, out var ss)
                        ? ss.EnumerateArray().Select(e => Enum.TryParse<SoundStyle>(e.GetString(), out var st) ? st : (SoundStyle?)null).OfType<SoundStyle>().ToArray()
                        : [SoundStyle.Gun, SoundStyle.Boom, SoundStyle.Suppressed];
                    float F(string name, float def) => r.TryGetProperty(name, out var v) ? v.GetSingle() : def;
                    var set = r.GetProperty("set"u8).GetString() ?? "";
                    if (!lib.Sets.ContainsKey(set))
                        lib.Problems.Add($"rule for {cls} names unknown set '{set}'");
                    lib.Rules.Add(new Rule(cls, sounds, F("minCaliber", float.NegativeInfinity), F("maxCaliber", float.PositiveInfinity), F("maxRpm", float.PositiveInfinity), set));
                }
            if (root.TryGetProperty("samples"u8, out var samples))
            {
                var entries = samples.EnumerateArray().ToArray();
                var files = entries.Select(e => e.GetProperty("file"u8).GetString() ?? "").ToArray();
                lib.FilesListed = entries.Length;
                // Decode in parallel (the files are small; this keeps start-up cost to a few ms).
                var decoded = new (float[]? Pcm, long Bytes, string? Error)[entries.Length];
                Parallel.For(0, files.Length, i =>
                {
                    var file = files[i];
                    var path = Path.Combine(folder, file);
                    try
                    {
                        if (file.Contains("..") || Path.IsPathRooted(file))
                            throw new InvalidDataException("bad file name");
                        if (!File.Exists(path))
                        {
                            decoded[i] = (null, 0, $"missing {file}");
                            return;
                        }
                        var bytes = File.ReadAllBytes(path);
                        var pcm = WavFile.ReadMono(bytes, Audio.SampleRate);
                        if (pcm.Length < Audio.SampleRate / 50)
                            throw new InvalidDataException("shorter than 20 ms");
                        decoded[i] = (pcm, bytes.Length, null);
                    }
                    catch (Exception ex)
                    {
                        decoded[i] = (null, 0, $"{file}: {ex.Message}");
                    }
                });
                for (var i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    var file = files[i];
                    if (decoded[i].Error is { } err)
                    {
                        lib.Problems.Add(err);
                        continue;
                    }
                    var setName = e.GetProperty("set"u8).GetString() ?? "";
                    if (!lib.Sets.TryGetValue(setName, out var set))
                    {
                        lib.Problems.Add($"{file}: unknown set '{setName}'");
                        continue;
                    }
                    var pcm = decoded[i].Pcm!;
                    if (e.TryGetProperty("gain"u8, out var g) && Math.Abs(g.GetSingle() - 1f) > 1e-4f)
                    {
                        var k = g.GetSingle();
                        for (var j = 0; j < pcm.Length; j++)
                            pcm[j] *= k;
                    }
                    var role = e.TryGetProperty("role"u8, out var ro) ? ro.GetString() : "shot";
                    if (role == "tail")
                        set.Tails.Add(pcm);
                    else if (role == "shot")
                        set.AddShot(pcm, file, e.TryGetProperty("gun"u8, out var gn) ? gn.GetString() ?? "" : "");
                    else
                    {
                        lib.Problems.Add($"{file}: role '{role}' isn't used (shot or tail)");
                        continue;
                    }
                    lib.FilesLoaded++;
                    lib.BytesLoaded += decoded[i].Bytes;
                }
            }
        }
        catch (Exception ex)
        {
            lib.Problems.Add($"manifest: {ex.Message}");
        }
        lib.LoadMs = sw.Elapsed.TotalMilliseconds;
        return lib;
    }

    /// <summary>The set a weapon uses, or null (synth only: no rule matches, or the set has no shot recordings).</summary>
    public SampleSet? SetFor(WeaponDef w)
    {
        var name = RuleSetFor(w);
        return name != null && Sets.TryGetValue(name, out var s) && s.Shots.Count > 0 ? s : null;
    }

    /// <summary>The set name the rules pick for a weapon, whether or not its files loaded.</summary>
    public string? RuleSetFor(WeaponDef w)
    {
        foreach (var r in Rules)
            if (r.Class == w.Class && r.Sounds.Contains(w.Sound) && w.Caliber >= r.MinCaliber && w.Caliber < r.MaxCaliber + 1e-6f && w.Rpm <= r.MaxRpm)
                return r.Set;
        return null;
    }

    /// <summary>One [gunsamples] line per set (which weapons use it, at what pitch) and the synth-only weapons, for the log.</summary>
    public IEnumerable<string> Describe(IEnumerable<WeaponDef> weapons)
    {
        var list = weapons.ToArray();
        foreach (var g in list.GroupBy(w => SetFor(w)?.Name ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            if (g.Key == "")
                yield return $"[gunsamples] synth only: {string.Join(", ", g.Select(w => w.Name))}";
            else
            {
                var s = Sets[g.Key];
                yield return $"[gunsamples] {s.Name} ({s.Shots.Count} shots from {s.Guns.Count} guns, {s.Tails.Count} tail): "
                    + string.Join(", ", g.Select(w => $"{w.Name} x{GunSamples.PitchFor(s, w) * GunVoice.For(w, s).Pitch:0.00}{(w.Sound == SoundStyle.Suppressed ? " (suppressed)" : "")}"));
                foreach (var wd in g)
                    yield return $"[gunvoice] {wd.Name}: {GunVoice.For(wd, s).Describe()}";
            }
        }
    }
}

/// <summary>Bakes a weapon's sample-based buffers (shot variants, suppressed variants, far layer) from a library.</summary>
public static class GunSamples
{
    private const int Sr = Audio.SampleRate;

    /// <summary>
    /// The loudness window: RMS over the first 80 ms (the blast and its body, what the ear judges a shot's loudness by)
    /// is matched to the synthesised shot's. A longer window would chase the synth's dense noise tail and over-squash
    /// the recordings, whose tails die away naturally.
    /// </summary>
    public const int LoudnessWindow = Sr * 80 / 1000;

    /// <summary>Sample-based shots come out at this times the synthesised shot's RMS (the frenzy calibration measures the result).</summary>
    public const float LoudnessMatch = 1f;

    public const float Ceiling = 0.97f;

    /// <summary>Variants of a gun with a Suppressor attachment (fewer than the open shot's one per recording: quiet and short, and it halves the bake cost).</summary>
    public const int SuppressedVariants = 2;

    /// <summary>
    /// Playback pitch for a weapon in a set: bigger calibers than the set's reference play lower (and longer), smaller
    /// ones higher; a brighter gun a touch higher. Kept within 0.82-1.18 so the recordings stay natural.
    /// </summary>
    public static float PitchFor(GunSampleLibrary.SampleSet set, WeaponDef w) =>
        Math.Clamp(1f + (set.RefCaliber - w.Caliber) * 0.45f + (w.Bright - 0.5f) * 0.12f, 0.82f, 1.18f);

    /// <summary>Baked shot variants per weapon (picked from its set's recordings by its <see cref="GunVoice"/>; a repeat take is treated a little differently).</summary>
    public const int Variants = 4;

    /// <summary>The most a near shot's peak is turned down by the limiter (dB): beyond it the bake gives up loudness to keep the transient sharp.</summary>
    public const float MaxPeakReductionDb = 3f;

    /// <summary>
    /// The synthesised layer under a recording only keeps its first <see cref="SynthWindow"/> seconds (crack, thump and
    /// mechanism; its noise tail and echo would smear the recording's attack), fading out over the same time again.
    /// Boom guns (the .50, the musket) keep more of their synthesised boom.
    /// </summary>
    public const float SynthWindow = 0.045f;

    /// <summary>The weapon's shot variants (<see cref="Variants"/> of them), or just <paramref name="synth"/> without a set.</summary>
    public static short[][] Shots(GunSampleLibrary lib, WeaponDef w, short[] synth)
    {
        if (w.Sound is not (SoundStyle.Gun or SoundStyle.Boom or SoundStyle.Suppressed) || lib.SetFor(w) is not { } set)
            return [synth];
        var voice = GunVoice.For(w, set);
        if (w.Sound == SoundStyle.Suppressed)
            return SuppressedFrom(set, w, voice, synth, Variants); // a built-in silencer is the gun's main sound: every variant
        var pitch = PitchFor(set, w) * voice.Pitch;
        var boom = w.Sound == SoundStyle.Boom;
        var blend = (boom ? MathF.Max(set.SynthBlend, 0.45f) : set.SynthBlend) * voice.SynthBlend;
        var picks = voice.Picks(set, Variants);
        var len = TrimLength(set, picks, w, voice, pitch);
        var synthF = ToFloat(synth);
        var target = Rms(synthF, LoudnessWindow) * LoudnessMatch;
        Window(synthF, boom ? SynthWindow * 3f : SynthWindow);
        var outv = new short[picks.Length][];
        for (var v = 0; v < outv.Length; v++)
        {
            var repeat = Array.IndexOf(picks, picks[v]) < v; // the same take again: nudge its treatment
            var x = Resample(set.Shots[picks[v]], pitch * (1f + (v - 1.5f) * 0.006f + (repeat ? 0.014f : 0f)), len);
            Shape(x, voice, w, repeat ? 0.75f : 1f);
            FadeTail(x, 0.35f);
            Blend(x, synthF, blend);
            outv[v] = Finish(x, target, MaxPeakReductionDb);
        }
        return outv;
    }

    /// <summary>The weapon with a suppressor: its synthesised "thwip" with the recording's body muffled underneath.</summary>
    public static short[][] Suppressed(GunSampleLibrary lib, WeaponDef w, short[] synthSuppressed)
    {
        if (w.Sound is not (SoundStyle.Gun or SoundStyle.Boom or SoundStyle.Suppressed) || lib.SetFor(w) is not { } set)
            return [synthSuppressed];
        return SuppressedFrom(set, w, GunVoice.For(w, set), synthSuppressed, SuppressedVariants);
    }

    private static short[][] SuppressedFrom(GunSampleLibrary.SampleSet set, WeaponDef w, GunVoice voice, short[] synthSuppressed, int variants)
    {
        var pitch = PitchFor(set, w) * voice.Pitch * 0.97f;
        var synthF = ToFloat(synthSuppressed);
        var len = Math.Max(synthF.Length, (int)(0.35f * Sr));
        var picks = voice.Picks(set, variants);
        var outv = new short[picks.Length][];
        for (var v = 0; v < outv.Length; v++)
        {
            var repeat = Array.IndexOf(picks, picks[v]) < v;
            var x = Resample(set.Shots[picks[v]], pitch * (repeat ? 1.014f : 1f), len);
            LowPass(x, 900f);
            LowPass(x, 1400f);
            Decay(x, 0.07f); // a suppressor kills the blast's ring-out
            Body(x, voice.BodyHz * 0.8f, voice.BodyQ, voice.BodyGain * 1.5f); // what's left is the gun's own body
            var mix = new float[len];
            var k = Rms(synthF, LoudnessWindow) / MathF.Max(Rms(x, LoudnessWindow), 1e-6f) * 0.7f;
            for (var i = 0; i < len; i++)
                mix[i] = x[i] * k + (i < synthF.Length ? synthF[i] : 0f);
            Tilt(mix, voice.Tilt * 0.6f);
            FadeTail(mix, 0.3f);
            outv[v] = Finish(mix, Rms(synthF, LoudnessWindow) * LoudnessMatch);
        }
        return outv;
    }

    /// <summary>The far layer: the set's mid-distance recording, softened, over the synthesised distant boom. Null stays null.</summary>
    public static short[]? Far(GunSampleLibrary lib, WeaponDef w, short[]? synthFar)
    {
        if (synthFar == null || lib.SetFor(w) is not { } set || set.Tails.Count == 0)
            return synthFar;
        var voice = GunVoice.For(w, set);
        var pitch = PitchFor(set, w) * voice.Pitch;
        var synthF = ToFloat(synthFar);
        var tail = set.Tails[0];
        var len = Math.Max(synthF.Length, (int)(tail.Length / pitch));
        var x = Resample(tail, pitch, len);
        LowPass(x, 2600f);
        var k = Rms(synthF, LoudnessWindow) / MathF.Max(Rms(x, LoudnessWindow), 1e-6f);
        for (var i = 0; i < len; i++)
            x[i] = x[i] * k + (i < synthF.Length ? synthF[i] * 0.6f : 0f);
        Tilt(x, voice.Tilt * 0.5f);
        FadeTail(x, 0.3f);
        return Finish(x, Rms(synthF, LoudnessWindow) * LoudnessMatch);
    }

    /// <summary>
    /// The snappy treatment and the weapon's identity on one near shot: a sharper crack (a high-passed copy of the
    /// first milliseconds on top), the body resonance, the tone tilt, a drier body (an extra decay after the blast)
    /// and the mechanism click. <paramref name="amount"/> below 1 softens it a little for a repeat take.
    /// </summary>
    public static void Shape(float[] x, GunVoice voice, WeaponDef w, float amount)
    {
        var peak = 1e-6f;
        foreach (var s in x)
            peak = MathF.Max(peak, MathF.Abs(s));
        // Crack: the top end of the first ~2 ms, emphasised (one-pole high-pass at 2.5 kHz, 1.6 ms decay).
        var hpA = MathF.Exp(-2f * MathF.PI * 2500f / Sr);
        float hpY = 0, hpX = 0;
        var crack = (0.25f + 0.75f * voice.Crack) * amount;
        var crackN = Math.Min(x.Length, (int)(0.012f * Sr));
        var crackK = -1f / (0.0016f * Sr);
        for (var i = 0; i < crackN; i++)
        {
            hpY = hpA * (hpY + x[i] - hpX);
            hpX = x[i];
            x[i] += hpY * crack * MathF.Exp(i * crackK);
        }
        Body(x, voice.BodyHz * (2f - amount), voice.BodyQ, voice.BodyGain);
        Tilt(x, voice.Tilt * amount);
        // Drier body: from a few ms after the blast (later for big calibers) an extra exponential decay.
        var hold = (int)((0.006f + 0.012f * w.Caliber) * Sr);
        var step = MathF.Exp(-1f / (voice.Dry * Sr));
        var g = 1f;
        for (var i = hold; i < x.Length; i++)
        {
            x[i] *= g;
            g *= step;
        }
        Click(x, voice, peak);
    }

    /// <summary>Adds a band-pass ring (RBJ, 0 dB peak) of the signal at <paramref name="hz"/> / <paramref name="q"/>, at <paramref name="gain"/> times its peak gain.</summary>
    private static void Body(float[] x, float hz, float q, float gain)
    {
        if (gain <= 0)
            return;
        var w0 = 2f * MathF.PI * Math.Clamp(hz, 40f, Sr * 0.45f) / Sr;
        var alpha = MathF.Sin(w0) / (2f * q);
        var a0 = 1f + alpha;
        float b0 = alpha / a0, a1 = -2f * MathF.Cos(w0) / a0, a2 = (1f - alpha) / a0;
        // Resonators need more drive the narrower they are: scale by Q so the ring's level tracks gain, not Q.
        var k = gain * MathF.Sqrt(q) * 0.6f;
        float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var xi = x[i];
            var y = b0 * (xi - x2) - a1 * y1 - a2 * y2;
            (x2, x1, y2, y1) = (x1, xi, y1, y);
            x[i] = xi + y * k;
        }
    }

    /// <summary>Tone tilt around 1.2 kHz: +1 lifts the highs about 4 dB and dips the lows 3 dB, -1 the reverse.</summary>
    private static void Tilt(float[] x, float tilt)
    {
        if (MathF.Abs(tilt) < 1e-3f)
            return;
        var a = 1f - MathF.Exp(-2f * MathF.PI * 1200f / Sr);
        float lo = 0;
        float gl = 1f - 0.3f * tilt, gh = 1f + 0.55f * tilt;
        for (var i = 0; i < x.Length; i++)
        {
            lo += a * (x[i] - lo);
            x[i] = lo * gl + (x[i] - lo) * gh;
        }
    }

    /// <summary>The voice's mechanism click (a few damped partials) at its delay, relative to <paramref name="peak"/>.</summary>
    private static void Click(float[] x, GunVoice v, float peak)
    {
        if (v.ClickLevel <= 0)
            return;
        // kind: partial frequencies (Hz) and decay (s); "double" repeats 4 ms later, softer.
        var (f1, f2, tau) = v.ClickKind switch
        {
            0 => (3300f, 5200f, 0.0012f),
            1 => (2400f, 3900f, 0.0016f),
            _ => (1150f, 2100f, 0.0028f),
        };
        var detune = 0.85f + 0.3f * GunVoice.U(v.Seed, 20);
        f1 *= detune;
        f2 *= detune;
        var hits = v.ClickKind == 1 ? 2 : 1;
        for (var h = 0; h < hits; h++)
        {
            var start = (int)((v.ClickDelay + h * 0.004f) * Sr);
            var amp = v.ClickLevel * peak * (h == 0 ? 1f : 0.6f);
            var n = Math.Min(x.Length - start, (int)(tau * 6 * Sr));
            for (var i = 0; i < n; i++)
            {
                var t = i / (float)Sr;
                x[start + i] += amp * MathF.Exp(-t / tau) * (MathF.Sin(2f * MathF.PI * f1 * t) + 0.6f * MathF.Sin(2f * MathF.PI * f2 * t + 1.3f));
            }
        }
    }

    /// <summary>Keeps the first <paramref name="seconds"/> of a buffer, then fades it out over the same time again (raised cosine).</summary>
    private static void Window(float[] x, float seconds)
    {
        var a = (int)(seconds * Sr);
        var b = a * 2;
        for (var i = a; i < x.Length; i++)
            x[i] = i >= b ? 0f : x[i] * (0.5f + 0.5f * MathF.Cos(MathF.PI * (i - a) / (b - a)));
    }

    // ---- DSP helpers ---------------------------------------------------------------------------------

    /// <summary>
    /// How long a weapon's shot runs: its shortest picked recording at its pitch, shortened for guns with a short tail
    /// and by the voice's length (the dry body is gone well before, so this mostly trims silence and room).
    /// </summary>
    private static int TrimLength(GunSampleLibrary.SampleSet set, int[] picks, WeaponDef w, GunVoice voice, float pitch)
    {
        var full = (int)(picks.Min(p => set.Shots[p].Length) / pitch);
        return Math.Clamp((int)(full * (0.5f + 0.45f * w.Tail) * voice.Length), Math.Min(full, (int)(0.25f * Sr)), full);
    }

    private static float[] ToFloat(short[] s)
    {
        var f = new float[s.Length];
        for (var i = 0; i < s.Length; i++)
            f[i] = s[i] / 32768f;
        return f;
    }

    /// <summary>Linear-interpolation resample by <paramref name="pitch"/> (above 1 = higher and shorter) into <paramref name="length"/> samples.</summary>
    public static float[] Resample(float[] src, float pitch, int length)
    {
        var o = new float[length];
        for (var i = 0; i < length; i++)
        {
            var p = i * (double)pitch;
            var j = (int)p;
            if (j + 1 >= src.Length)
                break;
            var t = (float)(p - j);
            o[i] = src[j] + (src[j + 1] - src[j]) * t;
        }
        return o;
    }

    private static void FadeTail(float[] x, float fraction)
    {
        var n = x.Length;
        var f0 = (int)(n * (1f - fraction));
        for (var i = f0; i < n; i++)
            x[i] *= 0.5f + 0.5f * MathF.Cos(MathF.PI * (i - f0) / Math.Max(1, n - f0));
    }

    private static void LowPass(float[] x, float cutoff)
    {
        var a = 1f - MathF.Exp(-2f * MathF.PI * cutoff / Sr);
        float y = 0;
        for (var i = 0; i < x.Length; i++)
        {
            y += a * (x[i] - y);
            x[i] = y;
        }
    }

    private static void Decay(float[] x, float seconds)
    {
        for (var i = 0; i < x.Length; i++)
            x[i] *= MathF.Exp(-i / (seconds * Sr));
    }

    /// <summary>Mixes the synthesised shot in at <paramref name="amount"/> of the recording's loudness.</summary>
    private static void Blend(float[] x, float[] synth, float amount)
    {
        if (amount <= 0)
            return;
        var k = amount * Rms(x, LoudnessWindow) / MathF.Max(Rms(synth, LoudnessWindow), 1e-6f);
        var n = Math.Min(x.Length, synth.Length);
        for (var i = 0; i < n; i++)
            x[i] += synth[i] * k;
    }

    public static float Rms(float[] x, int window)
    {
        var n = Math.Min(window, x.Length);
        double sum = 0;
        for (var i = 0; i < n; i++)
            sum += x[i] * (double)x[i];
        return (float)Math.Sqrt(sum / Math.Max(1, n));
    }

    /// <summary>
    /// Scales to the target RMS through a look-ahead peak limiter (1 ms attack ramp ahead of each peak, 40 ms release,
    /// so the blast's first milliseconds are turned down smoothly rather than clipped; gain at most
    /// <see cref="MaxGain"/>), a soft clip under <see cref="Ceiling"/> as a safety net, and a 2 ms fade-out.
    /// The limiter lowers the RMS, so the gain is corrected once and the limiter run again.
    /// </summary>
    private static short[] Finish(float[] x, float targetRms, float maxReductionDb = float.PositiveInfinity)
    {
        var peak = 1e-6f;
        foreach (var s in x)
            peak = MathF.Max(peak, MathF.Abs(s));
        // At most maxReductionDb of limiting on the peak: past that, loudness is traded for a sharp transient.
        var cap = MathF.Min(MaxGain, LimiterThreshold * MathF.Pow(10f, MathF.Min(maxReductionDb, 60f) / 20f) / peak);
        var gain = MathF.Min(targetRms / MathF.Max(Rms(x, LoudnessWindow), 1e-6f), cap);
        // Calibrate on the loudness window only (the limiter looks 1 ms ahead, so a 2 ms longer prefix limits exactly
        // as the whole buffer would), then limit the whole buffer once at the corrected gain.
        var probe = new float[Math.Min(x.Length, LoudnessWindow + Sr / 500)];
        for (var i = 0; i < probe.Length; i++)
            probe[i] = x[i] * gain;
        Limit(probe, LimiterThreshold);
        var got = Rms(probe, LoudnessWindow);
        if (got > 1e-6f)
            gain = MathF.Min(gain * targetRms / got, cap);
        var y = new float[x.Length];
        for (var i = 0; i < x.Length; i++)
            y[i] = x[i] * gain;
        Limit(y, LimiterThreshold);
        const float knee = 0.85f, span = Ceiling - knee;
        var fade = Math.Min(y.Length, Sr / 500);
        var pcm = new short[y.Length];
        for (var i = 0; i < y.Length; i++)
        {
            var v = y[i];
            var a = MathF.Abs(v);
            if (a > knee)
                v = MathF.CopySign(knee + span * MathF.Tanh((a - knee) / span), v);
            if (i >= y.Length - fade)
                v *= (y.Length - i) / (float)fade;
            pcm[i] = (short)Math.Clamp((int)MathF.Round(v * 32767f), -32767, 32767);
        }
        return pcm;
    }

    /// <summary>The most a bake may be turned up (keeps a quiet recording's noise floor in check).</summary>
    public const float MaxGain = 6f;

    /// <summary>The look-ahead limiter's ceiling (the soft clip above it is only a safety net).</summary>
    public const float LimiterThreshold = 0.85f;

    /// <summary>
    /// O(n) look-ahead peak limiter: the gain each sample needs, ramped down over 1 ms ahead of every peak (backward
    /// pass) and recovered over 40 ms after it (forward pass), so |x| never exceeds <paramref name="threshold"/>.
    /// </summary>
    public static void Limit(float[] x, float threshold)
    {
        var n = x.Length;
        if (n == 0)
            return;
        var g = new float[n];
        var attack = 1f / (Sr / 1000f);
        var release = 1f / (0.04f * Sr);
        var next = 1f;
        for (var i = n - 1; i >= 0; i--)
        {
            var a = MathF.Abs(x[i]);
            var need = a > threshold ? threshold / a : 1f;
            next = MathF.Min(need, next + attack);
            g[i] = next;
        }
        var prev = g[0];
        for (var i = 0; i < n; i++)
        {
            prev = MathF.Min(g[i], prev + release);
            x[i] *= prev;
        }
    }
}

/// <summary>Minimal RIFF WAVE reader: PCM 8/16/24/32-bit and float32, mono or stereo (mixed down), any rate (resampled).</summary>
public static class WavFile
{
    /// <summary>Decodes a WAV file to mono floats at <paramref name="targetRate"/>. Throws <see cref="InvalidDataException"/> on bad data.</summary>
    public static float[] ReadMono(ReadOnlySpan<byte> data, int targetRate)
    {
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("not a RIFF WAVE file");
        int format = 0, channels = 0, rate = 0, bits = 0;
        ReadOnlySpan<byte> pcm = default;
        var found = false;
        var p = 12;
        while (p + 8 <= data.Length)
        {
            var id = data.Slice(p, 4);
            var size = (int)Math.Min(BitConverter.ToUInt32(data.Slice(p + 4, 4)), (uint)(data.Length - p - 8));
            var body = data.Slice(p + 8, size);
            if (id.SequenceEqual("fmt "u8) && size >= 16)
            {
                format = BitConverter.ToUInt16(body);
                channels = BitConverter.ToUInt16(body[2..]);
                rate = BitConverter.ToInt32(body[4..]);
                bits = BitConverter.ToUInt16(body[14..]);
                if (format == 0xFFFE && size >= 26)
                    format = BitConverter.ToUInt16(body[24..]); // WAVE_FORMAT_EXTENSIBLE: the sub-format GUID starts with the tag
            }
            else if (id.SequenceEqual("data"u8))
            {
                pcm = body;
                found = true;
            }
            p += 8 + size + (size & 1);
        }
        if (channels is < 1 or > 8 || rate is < 4000 or > 384000 || !found)
            throw new InvalidDataException($"bad header ({channels} channels, {rate} Hz, data {(found ? "found" : "missing")})");
        var bytesPer = bits / 8;
        if (!(format == 1 && bits is 8 or 16 or 24 or 32 || format == 3 && bits == 32))
            throw new InvalidDataException($"unsupported format {format} / {bits}-bit");
        var frames = pcm.Length / (bytesPer * channels);
        var mono = new float[frames];
        var scale = 1f / channels;
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var s = pcm.Slice((f * channels + c) * bytesPer, bytesPer);
                sum += (format, bits) switch
                {
                    (1, 8) => (s[0] - 128) / 128f,
                    (1, 16) => BitConverter.ToInt16(s) / 32768f,
                    (1, 24) => ((s[0] << 8 | s[1] << 16 | s[2] << 24) >> 8) / 8388608f,
                    (1, 32) => BitConverter.ToInt32(s) / 2147483648f,
                    _ => BitConverter.ToSingle(s),
                };
            }
            mono[f] = sum * scale;
        }
        return rate == targetRate ? mono : GunSamples.Resample(mono, rate / (float)targetRate, (int)((long)frames * targetRate / rate));
    }

    /// <summary>Writes 16-bit PCM (mono or interleaved stereo) WAV bytes (for tests and tools).</summary>
    public static byte[] Write16(short[] samples, int rate, int channels = 1)
    {
        var data = samples.Length * 2;
        using var ms = new MemoryStream(44 + data);
        using var bw = new BinaryWriter(ms);
        bw.Write("RIFF"u8);
        bw.Write(36 + data);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(rate);
        bw.Write(rate * 2 * channels);
        bw.Write((short)(2 * channels));
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(data);
        foreach (var s in samples)
            bw.Write(s);
        bw.Flush();
        return ms.ToArray();
    }
}
