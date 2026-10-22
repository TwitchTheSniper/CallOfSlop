using System.Text.Json;

namespace CallOfSlop;

/// <summary>The instruments a frenzy class style can layer onto the track (see <see cref="FrenzyStyleRig"/> for the sounds).</summary>
public enum FrenzyVoice : byte
{
    Whistle, // a whistled western lead: sine with breath, a slide into each note and a late vibrato
    Twang, // a plucked, twangy bass with a quick downward bend
    Whip, // a short bright crack (whip / rim shot)
    Chip, // a square-wave chiptune lead
    BreakKick, // a short punchy breakbeat kick
    BreakSnare, // a crunchy breakbeat snare
    March, // a tight, high marching snare
    BassDrum, // a round marching bass drum
    Brass, // a bright brass section stab
    Braam, // a huge low cinematic brass "braam" with a slow swell
    Boom, // a cinematic sub boom with a long tail
    BigSnare, // a huge half-time snare (meant for the reverb)
    Gabber, // a distorted, layered gabber kick tuned to the key
    Metal, // a palm-muted distorted power-chord chug
    Anvil, // a metallic industrial clank
    Glitch, // a short square blip on a random note
}

/// <summary>Which hardbass tracks a class style picks when a frenzy starts (always tracks with steady 4/4 drops).</summary>
public enum FrenzyPool : byte
{
    Any, // every frenzy track (the classic default)
    Classic, // the original hardbass tracks (not the harder or weird ones)
    Harder, // the harder hardbass tracks (gabber kicks, sirens)
    Weird, // the weird hardbass tracks
    Fast, // 160 BPM and up
    Slow, // up to 158 BPM
}

/// <summary>
/// One instrument layer of a class style: a voice, a pattern over 16ths (any length; '.' rest or note off, '-' hold,
/// 'x' hit / root, 'X' accent, 'g' ghost, 'r' ratchet (two hits in the 16th), '0'-'9' scale degrees above the bar's
/// chord root, 'z' a random degree), the octave (semitones above the bass octave), its level, the frenzy level it
/// comes in at, a reverb send and, for the polyphonic voices, the chord it stacks (scale degrees).
/// </summary>
public sealed record FrenzyLayerSpec(FrenzyVoice Voice, string Pattern, int Octave, float Level, int From, float Reverb, int[] Chord);

/// <summary>
/// Effects a class style puts on the hardbass track itself: drive (soft clipping, 0 = off), a bit crusher (bits, 0 =
/// off; and a sample-and-hold decimation), beat-repeat stutters (a 16-step pattern: 's' repeats a 32nd, 'S' a 64th
/// through the 16th), and a slap-back echo (ms, feedback, mix; 0 ms = off).
/// </summary>
public sealed record FrenzyMusicFx(float Drive, int CrushBits, int CrushHold, string Stutter, float SlapMs, float SlapFeedback, float SlapMix)
{
    public static readonly FrenzyMusicFx None = new(0, 0, 1, "", 0, 0, 0);
}

/// <summary>
/// The music character of one weapon class while its frenzy plays (the "music" section of
/// Weapons/Frenzy/classes.json): a name, the track pool, gains on the track's own stems, effects on the track, a trim
/// that keeps level 6 as loud as every other class, and the instrument layers on top.
/// </summary>
public sealed record FrenzyStyle(string Name, string Describe, FrenzyPool Pool, float StemDrums, float StemBass, float StemLead, FrenzyMusicFx Fx, float Trim, FrenzyLayerSpec[] Layers)
{
    /// <summary>The classic frenzy: the track as it is (Assault Rifle, and every class without data).</summary>
    public static readonly FrenzyStyle Plain = new("Classic hardbass", "the hardbass track as it is", FrenzyPool.Any, 1, 1, 1, FrenzyMusicFx.None, 1, []);

    /// <summary>
    /// The tempo at frenzy levels 5 and 6 as multipliers of the track's own BPM ("tempo": { "level5", "level6" }; levels
    /// 1-4 play the track's tempo). <see cref="FrenzyLayer.LevelBpm"/> gives an already fast track a smaller share of the boost.
    /// </summary>
    public float Tempo5 { get; init; } = FrenzyLayer.DefaultTempo5;

    /// <inheritdoc cref="Tempo5"/>
    public float Tempo6 { get; init; } = FrenzyLayer.DefaultTempo6;

    /// <summary>True when the style leaves the track untouched (the mixer skips it entirely).</summary>
    public bool IsPlain => Layers.Length == 0 && StemDrums == 1f && StemBass == 1f && StemLead == 1f && Trim == 1f
                           && Fx.Drive <= 0 && Fx.CrushBits <= 0 && Fx.CrushHold <= 1 && Fx.Stutter.Length == 0 && Fx.SlapMs <= 0;
}

/// <summary>Loads the "music" sections of Weapons/Frenzy/classes.json (format call-of-slop-frenzy-classes/1).</summary>
public static class FrenzyStyles
{
    public const string Format = "call-of-slop-frenzy-classes/1";

    /// <summary>The per-class data file.</summary>
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "Weapons", "Frenzy", "classes.json");

    /// <summary>Problems found by the last <see cref="Load"/> (for the log and the self-test).</summary>
    public static readonly List<string> LoadErrors = new();

    private static FrenzyStyle[]? _all;

    /// <summary>One style per <see cref="WeaponClass"/> (index = the enum value), loaded once.</summary>
    public static FrenzyStyle[] All => _all ??= Load(FilePath);

    /// <summary>The number of weapon classes.</summary>
    public static readonly int ClassCount = Enum.GetValues<WeaponClass>().Length;

    /// <summary>Reads the file; classes without a valid "music" section play <see cref="FrenzyStyle.Plain"/>.</summary>
    public static FrenzyStyle[] Load(string path)
    {
        LoadErrors.Clear();
        var styles = new FrenzyStyle[ClassCount];
        Array.Fill(styles, FrenzyStyle.Plain);
        if (!File.Exists(path))
        {
            LoadErrors.Add($"{path} not found: every class plays the classic frenzy");
            Console.WriteLine($"[frenzy] {LoadErrors[^1]}");
            return styles;
        }
        try
        {
            Parse(File.ReadAllText(path), styles);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            LoadErrors.Add($"classes.json: {ex.Message}");
        }
        foreach (var e in LoadErrors)
            Console.WriteLine($"[frenzy] music: {e}");
        return styles;
    }

    /// <summary>Parses the file's text into <paramref name="styles"/> (index = class). Tolerates the other agents' sections.</summary>
    public static void Parse(string json, FrenzyStyle[] styles)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;
        if (root.TryGetProperty("format", out var fmt) && fmt.GetString() != Format)
            LoadErrors.Add($"format \"{fmt.GetString()}\" (want {Format}); reading it anyway");
        if (!root.TryGetProperty("classes", out var classes))
        {
            LoadErrors.Add("no \"classes\"");
            return;
        }
        // Either { "Pistol": { ... }, ... } or [ { "class": "Pistol", ... }, ... ].
        var entries = new List<(string Name, JsonElement Body)>();
        if (classes.ValueKind == JsonValueKind.Object)
            foreach (var p in classes.EnumerateObject())
                entries.Add((p.Name, p.Value));
        else if (classes.ValueKind == JsonValueKind.Array)
            foreach (var e in classes.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("class", out var c) && c.ValueKind == JsonValueKind.String)
                    entries.Add((c.GetString()!, e));
        foreach (var (name, body) in entries)
        {
            if (!TryClass(name, out var wc))
            {
                LoadErrors.Add($"unknown class \"{name}\"");
                continue;
            }
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("music", out var music))
                continue;
            try
            {
                styles[(int)wc] = ParseStyle(music);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            {
                LoadErrors.Add($"{name}: {ex.Message}");
            }
        }
    }

    /// <summary>Class names as in the weapon files ("Assault Rifle" or "AssaultRifle", any case).</summary>
    public static bool TryClass(string name, out WeaponClass wc) =>
        Enum.TryParse(name.Replace(" ", "").Replace("-", ""), true, out wc) && Enum.IsDefined(wc);

    private static FrenzyStyle ParseStyle(JsonElement m)
    {
        string Str(JsonElement e, string key, string def) => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : def;
        float Num(JsonElement e, string key, float def, float min, float max) =>
            e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? Math.Clamp(v.GetSingle(), min, max) : def;

        var name = Str(m, "style", "Unnamed");
        var describe = Str(m, "describe", "");
        var poolText = Str(m, "pool", "any");
        if (!Enum.TryParse<FrenzyPool>(poolText, true, out var pool))
            throw new FormatException($"unknown pool \"{poolText}\"");
        float sd = 1, sb = 1, sl = 1;
        if (m.TryGetProperty("stems", out var stems))
        {
            sd = Num(stems, "drums", 1, 0, 1.5f);
            sb = Num(stems, "bass", 1, 0, 1.5f);
            sl = Num(stems, "lead", 1, 0.05f, 1.5f); // never 0: a muted lead stem means "skip the leads" to the renderer
        }
        var fx = FrenzyMusicFx.None;
        if (m.TryGetProperty("fx", out var f))
        {
            var stutter = Str(f, "stutter", "");
            if (stutter.Any(c => c is not ('.' or 's' or 'S')))
                throw new FormatException($"stutter pattern \"{stutter}\" may only hold '.', 's' and 'S'");
            fx = new FrenzyMusicFx(Num(f, "drive", 0, 0, 8), (int)Num(f, "crushBits", 0, 0, 16), (int)Num(f, "crushHold", 1, 1, 16), stutter,
                Num(f, "slapMs", 0, 0, 180), Num(f, "slapFeedback", 0, 0, 0.8f), Num(f, "slapMix", 0, 0, 1));
        }
        var trim = Num(m, "trim", 1, 0.2f, 3f);
        var layers = new List<FrenzyLayerSpec>();
        if (m.TryGetProperty("layers", out var ls) && ls.ValueKind == JsonValueKind.Array)
            foreach (var l in ls.EnumerateArray())
            {
                var vt = Str(l, "voice", "");
                if (!Enum.TryParse<FrenzyVoice>(vt, true, out var voice))
                    throw new FormatException($"unknown voice \"{vt}\"");
                var pattern = Str(l, "pattern", "");
                if (pattern.Length == 0 || pattern.Length > 256 || pattern.Any(c => !(c is '.' or '-' or 'x' or 'X' or 'g' or 'r' or 'z' || char.IsAsciiDigit(c))))
                    throw new FormatException($"{vt}: bad pattern \"{pattern}\"");
                var chord = new List<int>();
                if (l.TryGetProperty("chord", out var ch) && ch.ValueKind == JsonValueKind.Array)
                    foreach (var d in ch.EnumerateArray())
                        chord.Add(Math.Clamp(d.GetInt32(), -14, 14));
                if (chord.Count == 0)
                    chord.Add(0);
                if (chord.Count > SawSynth.MaxNotes)
                    throw new FormatException($"{vt}: at most {SawSynth.MaxNotes} chord notes");
                layers.Add(new FrenzyLayerSpec(voice, pattern, (int)Num(l, "octave", 0, -12, 48), Num(l, "level", 0.3f, 0, 2),
                    (int)Num(l, "from", 1, 1, FrenzyMeter.MaxLevel), Num(l, "reverb", 0, 0, 1), chord.ToArray()));
            }
        if (layers.Count > FrenzyStyleRig.MaxLayers)
            throw new FormatException($"at most {FrenzyStyleRig.MaxLayers} layers");
        float t5 = FrenzyLayer.DefaultTempo5, t6 = FrenzyLayer.DefaultTempo6;
        if (m.TryGetProperty("tempo", out var tempo))
        {
            // Always clearly faster than the level below (even at the 0.4 share a 215 BPM track gets: at least 2%).
            t5 = Num(tempo, "level5", t5, 1.05f, 1.3f);
            t6 = Num(tempo, "level6", MathF.Max(t6, t5 + 0.05f), t5 + 0.05f, 1.45f);
        }
        return new FrenzyStyle(name, describe, pool, sd, sb, sl, fx, trim, layers.ToArray()) { Tempo5 = t5, Tempo6 = t6 };
    }
}
