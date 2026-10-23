using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The player's audio mix (the Audio mixer debug window): a level in dB (and a mute) per <see cref="MixBus"/> and the
/// gun-mix shape values (<see cref="MixParam"/>), starting from <see cref="AudioMixDefaults"/>. Only changes local
/// playback. <see cref="Gain"/> is an array lookup (the linear gains are worked out when a value changes), so the
/// sound calls pay nothing for it; with the built-in defaults every gain is exactly 1 and every shape function returns
/// what the game computed before the mixer existed. Saved as human-readable JSON (<c>audio_mix.json</c> next to the
/// game, loaded at start-up; named presets in <c>AudioMixes/</c>).
/// </summary>
public sealed class AudioMix
{
    /// <summary>The JSON format version (written as "version").</summary>
    public const int FormatVersion = 1;

    /// <summary>The file in the save folder that is loaded at start-up.</summary>
    public const string FileName = "audio_mix.json";

    /// <summary>The folder (in the save folder) for "Save as..." presets.</summary>
    public const string PresetFolder = "AudioMixes";

    private const int Levels = (int)MixBus.COUNT, Params = (int)MixParam.COUNT;

    private readonly float[] _db = new float[Levels];
    private readonly bool[] _mute = new bool[Levels];
    private readonly float[] _gain = new float[Levels];
    private readonly float[] _param = new float[Params];
    private readonly bool _readOnly;

    /// <summary>The built-in defaults (never changes; what CI and testers hear).</summary>
    public static readonly AudioMix Defaults = new(readOnly: true);

    public AudioMix() : this(false)
    {
    }

    private AudioMix(bool readOnly)
    {
        ResetAll();
        _readOnly = readOnly;
    }

    /// <summary>Goes up on every change (the audio pushes the music's levels when it moves).</summary>
    public int Revision { get; private set; }

    // ---- Levels --------------------------------------------------------------------------

    /// <summary>The level of <paramref name="bus"/> in dB (mute aside).</summary>
    public float Db(MixBus bus) => (uint)bus < Levels ? _db[(int)bus] : 0f;

    public bool Muted(MixBus bus) => (uint)bus < Levels && _mute[(int)bus];

    /// <summary>The linear gain of <paramref name="bus"/>: 10^(dB/20), 0 when muted, exactly 1 at 0 dB (and for <see cref="MixBus.Auto"/>).</summary>
    public float Gain(MixBus bus) => (uint)bus < Levels ? _gain[(int)bus] : 1f;

    /// <summary>dB to a linear gain (0 dB is exactly 1).</summary>
    public static float DbToGain(float db) => db == 0f ? 1f : MathF.Pow(10f, db / 20f);

    /// <summary>A linear level to dB (-120 for silence).</summary>
    public static float GainToDb(float g) => g > 1e-6f ? 20f * MathF.Log10(g) : -120f;

    public void SetDb(MixBus bus, float db)
    {
        if ((uint)bus >= Levels || _readOnly || !float.IsFinite(db))
            return;
        db = Math.Clamp(db, AudioMixDefaults.MinDb, AudioMixDefaults.MaxDb);
        if (_db[(int)bus] == db)
            return;
        _db[(int)bus] = db;
        Recompute((int)bus);
    }

    public void SetMuted(MixBus bus, bool muted)
    {
        if ((uint)bus >= Levels || _readOnly || _mute[(int)bus] == muted)
            return;
        _mute[(int)bus] = muted;
        Recompute((int)bus);
    }

    private void Recompute(int i)
    {
        _gain[i] = _mute[i] ? 0f : DbToGain(_db[i]);
        Revision++;
    }

    // ---- Shape ---------------------------------------------------------------------------

    public float Param(MixParam p) => (uint)p < Params ? _param[(int)p] : 0f;

    public void SetParam(MixParam p, float value)
    {
        if ((uint)p >= Params || _readOnly || !float.IsFinite(value))
            return;
        ref readonly var e = ref AudioMixDefaults.Of(p);
        value = Math.Clamp(value, e.Min, e.Max);
        if (_param[(int)p] == value)
            return;
        _param[(int)p] = value;
        RecomputeShape();
    }

    private void RecomputeShape()
    {
        ShotFalloff = new Audio.Falloff(_param[(int)MixParam.ShotReference], _param[(int)MixParam.ShotRolloff],
            MathF.Max(_param[(int)MixParam.ShotReference], _param[(int)MixParam.ShotMax]), Thin: true);
        _farStart = _param[(int)MixParam.FarStart];
        _farSpan = MathF.Max(5f, _param[(int)MixParam.FarEnd] - _farStart);
        Revision++;
    }

    private float _farStart = 25f, _farSpan = 65f;

    /// <summary>The falloff of another player's open shot (both layers), from the Distance rows.</summary>
    internal Audio.Falloff ShotFalloff { get; private set; } = new(5f, 0.8f, 250f, Thin: true);

    /// <summary>How much of a near gunshot the distant layer replaces at <paramref name="distance"/> (smoothstep from the crossfade start to its end).</summary>
    public float FarMix(float distance)
    {
        var x = Math.Clamp((distance - _farStart) / _farSpan, 0f, 1f);
        return x * x * (3 - 2 * x);
    }

    /// <summary>The distance filter's low shelf on gunshots (1 within 8 m, then an exponential fall to the bass floor).</summary>
    public float DistanceLf(float distance) =>
        Math.Clamp(MathF.Exp(-MathF.Max(0f, distance - 8f) * _param[(int)MixParam.BassCut] / 35f), MathF.Min(1f, _param[(int)MixParam.BassFloor]), 1f);

    /// <summary>The distance filter's high shelf on every positional sound (1 within 8 m, then an exponential fall to the air floor).</summary>
    public float DistanceHf(float distance) =>
        Math.Clamp(MathF.Exp(-MathF.Max(0f, distance - 8f) * _param[(int)MixParam.AirAbsorption] / 55f), MathF.Min(1f, _param[(int)MixParam.AirFloor]), 1f);

    /// <summary>The music limiter's ceiling while the frenzy plays.</summary>
    public float LimiterCeiling => _param[(int)MixParam.LimiterCeiling];

    // ---- Whole mix -------------------------------------------------------------------------

    /// <summary>Back to <see cref="AudioMixDefaults"/>.</summary>
    public void ResetAll()
    {
        if (_readOnly)
            return;
        for (var i = 0; i < Levels; i++)
        {
            _db[i] = AudioMixDefaults.Entries[i].Default;
            _mute[i] = false;
            _gain[i] = DbToGain(_db[i]);
        }
        for (var i = 0; i < Params; i++)
            _param[i] = AudioMixDefaults.Of((MixParam)i).Default;
        RecomputeShape();
    }

    public void ResetLevel(MixBus bus)
    {
        SetDb(bus, AudioMixDefaults.Of(bus).Default);
        SetMuted(bus, false);
    }

    public void ResetParam(MixParam p) => SetParam(p, AudioMixDefaults.Of(p).Default);

    public void CopyFrom(AudioMix other)
    {
        for (var i = 0; i < Levels; i++)
        {
            SetDb((MixBus)i, other._db[i]);
            SetMuted((MixBus)i, other._mute[i]);
        }
        for (var i = 0; i < Params; i++)
            SetParam((MixParam)i, other._param[i]);
    }

    /// <summary>True when every value equals the built-in default (nothing muted).</summary>
    public bool IsDefault
    {
        get
        {
            for (var i = 0; i < Levels; i++)
                if (_mute[i] || _db[i] != AudioMixDefaults.Entries[i].Default)
                    return false;
            for (var i = 0; i < Params; i++)
                if (_param[i] != AudioMixDefaults.Of((MixParam)i).Default)
                    return false;
            return true;
        }
    }

    /// <summary>How many rows differ from their default.</summary>
    public int Changed
    {
        get
        {
            var n = 0;
            for (var i = 0; i < Levels; i++)
                if (_mute[i] || _db[i] != AudioMixDefaults.Entries[i].Default)
                    n++;
            for (var i = 0; i < Params; i++)
                if (_param[i] != AudioMixDefaults.Of((MixParam)i).Default)
                    n++;
            return n;
        }
    }

    // ---- JSON -----------------------------------------------------------------------------

    /// <summary>The game build, for the JSON (<see cref="BuildInfo.Label"/>: release version or "dev build", with the commit).</summary>
    public static string GameVersion() => BuildInfo.Label;

    /// <summary>
    /// The mix as indented JSON: the format and version, the game build, the time (UTC, ISO 8601), the preset name,
    /// every level by key ({"db", "mute", "default"}) and every shape value by key.
    /// </summary>
    public string ToJson(string? name, string game, DateTime utc)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", "callofslop-audio-mix");
            w.WriteNumber("version", FormatVersion);
            w.WriteString("game", game);
            w.WriteString("saved", utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(name))
                w.WriteString("name", name);
            w.WriteStartObject("levels");
            for (var i = 0; i < Levels; i++)
            {
                w.WriteStartObject(AudioMixDefaults.Entries[i].Key);
                w.WriteNumber("db", Round(_db[i]));
                w.WriteBoolean("mute", _mute[i]);
                w.WriteNumber("default", Round(AudioMixDefaults.Entries[i].Default));
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteStartObject("shape");
            for (var i = 0; i < Params; i++)
                w.WriteNumber(AudioMixDefaults.Of((MixParam)i).Key, Round(_param[i]));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static double Round(float v) => Math.Round(v, 3);

    /// <summary>What loading a JSON mix did.</summary>
    public readonly record struct LoadReport(bool Ok, int Applied, int Unknown, int Clamped, string? Name, string? Game, string? Error)
    {
        public override string ToString() => Ok
            ? $"{Applied} value(s) applied, {Unknown} unknown key(s) ignored, {Clamped} clamped{(Name != null ? $", preset \"{Name}\"" : "")}{(Game != null ? $", saved by {Game}" : "")}"
            : $"not loaded: {Error}";
    }

    /// <summary>
    /// Loads a JSON mix (from <see cref="ToJson"/>) over the defaults: missing keys keep their default, unknown keys and
    /// values of the wrong type are ignored, out-of-range values are clamped. A level may also be a bare number (dB).
    /// On a parse error nothing changes.
    /// </summary>
    public LoadReport LoadJson(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, null, null, ex.Message);
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(false, 0, 0, 0, null, null, "not a JSON object");
            var fresh = new AudioMix();
            int applied = 0, unknown = 0, clamped = 0;
            string? name = null, game = null;
            foreach (var prop in root.EnumerateObject())
            {
                switch (prop.Name)
                {
                    case "name" when prop.Value.ValueKind == JsonValueKind.String:
                        name = prop.Value.GetString();
                        break;
                    case "game" when prop.Value.ValueKind == JsonValueKind.String:
                        game = prop.Value.GetString();
                        break;
                    case "levels" when prop.Value.ValueKind == JsonValueKind.Object:
                        foreach (var lv in prop.Value.EnumerateObject())
                        {
                            var bus = FindLevel(lv.Name);
                            if (bus == null)
                            {
                                unknown++;
                                continue;
                            }
                            float? db = null;
                            bool? mute = null;
                            if (lv.Value.ValueKind == JsonValueKind.Number)
                                db = (float)lv.Value.GetDouble();
                            else if (lv.Value.ValueKind == JsonValueKind.Object)
                            {
                                if (lv.Value.TryGetProperty("db", out var d) && d.ValueKind == JsonValueKind.Number)
                                    db = (float)d.GetDouble();
                                if (lv.Value.TryGetProperty("mute", out var m) && m.ValueKind is JsonValueKind.True or JsonValueKind.False)
                                    mute = m.GetBoolean();
                            }
                            if (db is { } v && float.IsFinite(v))
                            {
                                if (v < AudioMixDefaults.MinDb || v > AudioMixDefaults.MaxDb)
                                    clamped++;
                                fresh.SetDb(bus.Value, v);
                                applied++;
                            }
                            if (mute is { } mm)
                            {
                                fresh.SetMuted(bus.Value, mm);
                                applied++;
                            }
                        }
                        break;
                    case "shape" when prop.Value.ValueKind == JsonValueKind.Object:
                        foreach (var sv in prop.Value.EnumerateObject())
                        {
                            var p = FindParam(sv.Name);
                            if (p == null)
                            {
                                unknown++;
                                continue;
                            }
                            if (sv.Value.ValueKind != JsonValueKind.Number)
                                continue;
                            var v = (float)sv.Value.GetDouble();
                            if (!float.IsFinite(v))
                                continue;
                            ref readonly var e = ref AudioMixDefaults.Of(p.Value);
                            if (v < e.Min || v > e.Max)
                                clamped++;
                            fresh.SetParam(p.Value, v);
                            applied++;
                        }
                        break;
                    case "format" or "version" or "saved" or "game" or "name" or "levels" or "shape":
                        break;
                    default:
                        unknown++;
                        break;
                }
            }
            CopyFrom(fresh);
            return new(true, applied, unknown, clamped, name, game, null);
        }
    }

    public static MixBus? FindLevel(string key)
    {
        for (var i = 0; i < Levels; i++)
            if (AudioMixDefaults.Entries[i].Key == key)
                return (MixBus)i;
        return null;
    }

    public static MixParam? FindParam(string key)
    {
        for (var i = 0; i < Params; i++)
            if (AudioMixDefaults.Of((MixParam)i).Key == key)
                return (MixParam)i;
        return null;
    }

    // ---- Files ----------------------------------------------------------------------------

    /// <summary>audio_mix.json in the save folder (<see cref="SavePaths"/>, so it carries over to new builds).</summary>
    public static string DefaultPath => SavePaths.PathOf(FileName);

    /// <summary>The AudioMixes folder in the save folder.</summary>
    public static string PresetDir => Path.Combine(SavePaths.Folder, PresetFolder);

    /// <summary>A preset name as a safe file name (letters, digits, space, - and _; at most 40 characters).</summary>
    public static string CleanPresetName(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
            if (sb.Length < 40 && (char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '_'))
                sb.Append(c);
        return sb.ToString().Trim();
    }

    /// <summary>Writes the mix to <paramref name="path"/> (creating the folder); returns null or the error.</summary>
    public string? SaveFile(string path, string? name)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            return SavePaths.WriteAtomic(path, ToJson(name, GameVersion(), DateTime.UtcNow)) ? null : "could not write " + path;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Loads <paramref name="path"/> if it exists (a missing file changes nothing: not Ok, error "no file").</summary>
    public LoadReport LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new(false, 0, 0, 0, null, null, "no file");
            return LoadJson(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            return new(false, 0, 0, 0, null, null, ex.Message);
        }
    }

    /// <summary>The presets in <see cref="PresetDir"/> (names without .json), sorted.</summary>
    public static List<string> ListPresets()
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(PresetDir))
                foreach (var f in Directory.GetFiles(PresetDir, "*.json"))
                    list.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch (Exception)
        {
            // unreadable folder: no presets
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }
}
