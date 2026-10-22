using System.Diagnostics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Recorded gunshot layers (Audio/Samples/Guns: manifest.json plus 16-bit mono WAVs made by
/// tools/audio/make_gun_samples.py). Every weapon whose class, sound style and caliber match a manifest rule gets its
/// shots baked at start-up from that set's recordings: pitched by caliber, trimmed by its tail, blended with a bit of its
/// own synthesised shot (mechanism click, thump) and loudness-matched to the synth. Anything missing (the manifest, a
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
            if (root.TryGetProperty("credits", out var cr))
                lib.Credits = cr.GetString() ?? "";
            if (root.TryGetProperty("sets", out var sets))
                foreach (var p in sets.EnumerateObject())
                {
                    var s = new SampleSet { Name = p.Name };
                    if (p.Value.TryGetProperty("about", out var a))
                        s.About = a.GetString() ?? "";
                    if (p.Value.TryGetProperty("refCaliber", out var rc))
                        s.RefCaliber = rc.GetSingle();
                    if (p.Value.TryGetProperty("synthBlend", out var sb))
                        s.SynthBlend = Math.Clamp(sb.GetSingle(), 0f, 1f);
                    lib.Sets[p.Name] = s;
                }
            if (root.TryGetProperty("rules", out var rules))
                foreach (var r in rules.EnumerateArray())
                {
                    if (!Enum.TryParse<WeaponClass>(r.GetProperty("class").GetString(), out var cls))
                    {
                        lib.Problems.Add($"rule with unknown class {r.GetProperty("class")}");
                        continue;
                    }
                    var sounds = r.TryGetProperty("sounds", out var ss)
                        ? ss.EnumerateArray().Select(e => Enum.TryParse<SoundStyle>(e.GetString(), out var st) ? st : (SoundStyle?)null).OfType<SoundStyle>().ToArray()
                        : [SoundStyle.Gun, SoundStyle.Boom, SoundStyle.Suppressed];
                    float F(string name, float def) => r.TryGetProperty(name, out var v) ? v.GetSingle() : def;
                    var set = r.GetProperty("set").GetString() ?? "";
                    if (!lib.Sets.ContainsKey(set))
                        lib.Problems.Add($"rule for {cls} names unknown set '{set}'");
                    lib.Rules.Add(new Rule(cls, sounds, F("minCaliber", float.NegativeInfinity), F("maxCaliber", float.PositiveInfinity), F("maxRpm", float.PositiveInfinity), set));
                }
            if (root.TryGetProperty("samples", out var samples))
            {
                var entries = samples.EnumerateArray().ToArray();
                var files = entries.Select(e => e.GetProperty("file").GetString() ?? "").ToArray();
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
                    var setName = e.GetProperty("set").GetString() ?? "";
                    if (!lib.Sets.TryGetValue(setName, out var set))
                    {
                        lib.Problems.Add($"{file}: unknown set '{setName}'");
                        continue;
                    }
                    var pcm = decoded[i].Pcm!;
                    if (e.TryGetProperty("gain", out var g) && Math.Abs(g.GetSingle() - 1f) > 1e-4f)
                    {
                        var k = g.GetSingle();
                        for (var j = 0; j < pcm.Length; j++)
                            pcm[j] *= k;
                    }
                    var role = e.TryGetProperty("role", out var ro) ? ro.GetString() : "shot";
                    if (role == "tail")
                        set.Tails.Add(pcm);
                    else if (role == "shot")
                    {
                        set.Shots.Add(pcm);
                        set.ShotFiles.Add(file);
                    }
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
                yield return $"[gunsamples] {s.Name} ({s.Shots.Count} shots, {s.Tails.Count} tail): "
                    + string.Join(", ", g.Select(w => $"{w.Name} x{GunSamples.PitchFor(s, w):0.00}{(w.Sound == SoundStyle.Suppressed ? " (suppressed)" : "")}"));
            }
        }
    }
}

/// <summary>Bakes a weapon's sample-based buffers (shot variants, suppressed variants, far layer) from a library.</summary>
public static class GunSamples
{
    private const int Sr = Audio.SampleRate;

    /// <summary>The loudness window: RMS over the first 250 ms is matched to the synthesised shot's.</summary>
    public const int LoudnessWindow = Sr / 4;

    /// <summary>Sample-based shots come out at this times the synthesised shot's RMS (the frenzy calibration measures the result).</summary>
    public const float LoudnessMatch = 1f;

    /// <summary>The gain that matches loudness is capped so the soft limiter never has to squash a peak more than this.</summary>
    public const float MaxPeakBeforeLimiter = 1.7f;

    public const float Ceiling = 0.97f;

    /// <summary>
    /// Playback pitch for a weapon in a set: bigger calibers than the set's reference play lower (and longer), smaller
    /// ones higher; a brighter gun a touch higher. Kept within 0.82-1.18 so the recordings stay natural.
    /// </summary>
    public static float PitchFor(GunSampleLibrary.SampleSet set, WeaponDef w) =>
        Math.Clamp(1f + (set.RefCaliber - w.Caliber) * 0.45f + (w.Bright - 0.5f) * 0.12f, 0.82f, 1.18f);

    /// <summary>The weapon's shot variants: one per recording in its set, or just <paramref name="synth"/> without one.</summary>
    public static short[][] Shots(GunSampleLibrary lib, WeaponDef w, short[] synth)
    {
        if (w.Sound is not (SoundStyle.Gun or SoundStyle.Boom or SoundStyle.Suppressed) || lib.SetFor(w) is not { } set)
            return [synth];
        if (w.Sound == SoundStyle.Suppressed)
            return SuppressedFrom(set, w, synth);
        var pitch = PitchFor(set, w);
        var blend = w.Sound == SoundStyle.Boom ? MathF.Max(set.SynthBlend, 0.45f) : set.SynthBlend;
        var len = TrimLength(set, w, pitch);
        var synthF = ToFloat(synth);
        var outv = new short[set.Shots.Count][];
        for (var v = 0; v < outv.Length; v++)
        {
            var x = Resample(set.Shots[v], pitch * (1f + (v - 1.5f) * 0.006f), len); // a hair of extra spread between variants
            FadeTail(x, 0.35f);
            Blend(x, synthF, blend);
            outv[v] = Finish(x, Rms(synthF, LoudnessWindow) * LoudnessMatch);
        }
        return outv;
    }

    /// <summary>The weapon with a suppressor: its synthesised "thwip" with the recording's body muffled underneath.</summary>
    public static short[][] Suppressed(GunSampleLibrary lib, WeaponDef w, short[] synthSuppressed)
    {
        if (w.Sound is not (SoundStyle.Gun or SoundStyle.Boom or SoundStyle.Suppressed) || lib.SetFor(w) is not { } set)
            return [synthSuppressed];
        return SuppressedFrom(set, w, synthSuppressed);
    }

    private static short[][] SuppressedFrom(GunSampleLibrary.SampleSet set, WeaponDef w, short[] synthSuppressed)
    {
        var pitch = PitchFor(set, w) * 0.97f;
        var synthF = ToFloat(synthSuppressed);
        var len = Math.Max(synthF.Length, (int)(0.35f * Sr));
        var outv = new short[set.Shots.Count][];
        for (var v = 0; v < outv.Length; v++)
        {
            var x = Resample(set.Shots[v], pitch, len);
            LowPass(x, 900f);
            LowPass(x, 1400f);
            Decay(x, 0.07f); // a suppressor kills the blast's ring-out
            var mix = new float[len];
            var k = Rms(synthF, LoudnessWindow) / MathF.Max(Rms(x, LoudnessWindow), 1e-6f) * 0.7f;
            for (var i = 0; i < len; i++)
                mix[i] = x[i] * k + (i < synthF.Length ? synthF[i] : 0f);
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
        var pitch = PitchFor(set, w);
        var synthF = ToFloat(synthFar);
        var tail = set.Tails[0];
        var len = Math.Max(synthF.Length, (int)(tail.Length / pitch));
        var x = Resample(tail, pitch, len);
        LowPass(x, 2600f);
        var k = Rms(synthF, LoudnessWindow) / MathF.Max(Rms(x, LoudnessWindow), 1e-6f);
        for (var i = 0; i < len; i++)
            x[i] = x[i] * k + (i < synthF.Length ? synthF[i] * 0.6f : 0f);
        FadeTail(x, 0.3f);
        return Finish(x, Rms(synthF, LoudnessWindow) * LoudnessMatch);
    }

    // ---- DSP helpers ---------------------------------------------------------------------------------

    /// <summary>How long a weapon's shot runs: the recording at its pitch, shortened for guns with a short tail.</summary>
    private static int TrimLength(GunSampleLibrary.SampleSet set, WeaponDef w, float pitch)
    {
        var full = (int)(set.Shots.Min(s => s.Length) / pitch);
        return Math.Clamp((int)(full * (0.55f + 0.5f * w.Tail)), Math.Min(full, (int)(0.3f * Sr)), full);
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

    /// <summary>Scales to the target RMS (gain capped by <see cref="MaxPeakBeforeLimiter"/>), soft-limits under <see cref="Ceiling"/>, 2 ms fade-out.</summary>
    private static short[] Finish(float[] x, float targetRms)
    {
        var peak = 1e-6f;
        foreach (var v in x)
            peak = MathF.Max(peak, MathF.Abs(v));
        var gain = MathF.Min(targetRms / MathF.Max(Rms(x, LoudnessWindow), 1e-6f), MaxPeakBeforeLimiter / peak);
        const float knee = 0.75f, span = Ceiling - knee;
        var fade = Math.Min(x.Length, Sr / 500);
        var pcm = new short[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var v = x[i] * gain;
            var a = MathF.Abs(v);
            if (a > knee)
                v = MathF.CopySign(knee + span * MathF.Tanh((a - knee) / span), v);
            if (i >= x.Length - fade)
                v *= (x.Length - i) / (float)fade;
            pcm[i] = (short)Math.Clamp((int)MathF.Round(v * 32767f), -32767, 32767);
        }
        return pcm;
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
