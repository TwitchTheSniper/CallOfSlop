using System.Numerics;
using System.Text.Json;

namespace CallOfSlop;

/// <summary>Settings > Video "Frenzy visuals": how much of the per-class frenzy look is drawn.</summary>
public enum FrenzyVisualMode : byte
{
    /// <summary>Everything: tracers, sparks, weapon glow, screen-edge tint and the class's screen effect.</summary>
    Full,

    /// <summary>Tracer colours, weapon glow and the HUD meter, with far fewer particles, a fainter edge tint and no screen effects.</summary>
    Subtle,

    /// <summary>No frenzy visuals at all (the normal tracers; the HUD meter is hidden too).</summary>
    Off,
}

/// <summary>The one thing that makes a class's frenzy look unlike the others (on top of its colours and sparks).</summary>
public enum FrenzySignature : byte
{
    /// <summary>Pistol: a glint sweeps along the gun when it's drawn (and on each kill).</summary>
    QuickDrawGlint,

    /// <summary>SMG: speed lines streak in from the screen edges while you move fast.</summary>
    SpeedLines,

    /// <summary>Assault Rifle: heat shimmer rises off the barrel as you keep firing.</summary>
    HeatShimmer,

    /// <summary>DMR: a crisp ring of sparks bursts out where each shot lands.</summary>
    ImpactRings,

    /// <summary>Sniper: tracers leave long glowing trails, and each kill sends one slow vignette pulse.</summary>
    KillPulse,

    /// <summary>Shotgun: pellets burn, and embers drift down where they land.</summary>
    Embers,

    /// <summary>LMG: ejected brass throws heavy spark showers, and the barrel glows with sustained fire.</summary>
    BrassShower,

    /// <summary>Special: rainbow tracers with a split glitch ghost, and a slow chromatic flicker at the screen edges.</summary>
    Glitch,
}

/// <summary>
/// One weapon class's frenzy look: colours, how its tracers change, what sparks it throws and its signature effect. All
/// numbers are "at full intensity" (frenzy level 6, Frenzy visuals Full) and scale down with <see cref="FrenzyLooks.Intensity"/>.
/// Built in (<see cref="FrenzyLooks.Defaults"/>) and overridable per class from the "visual" section of
/// Weapons/Frenzy/classes.json.
/// </summary>
public sealed class FrenzyLook
{
    public WeaponClass Class;

    /// <summary>The HUD meter's label, e.g. "PISTOL".</summary>
    public string Name = "";

    /// <summary>Tracer, glow, edge tint and HUD colour.</summary>
    public Vector3 Color;

    /// <summary>Secondary colour: sparks, embers, the glitch ghost.</summary>
    public Vector3 Accent;

    /// <summary>Tracer width multiplier at full intensity (1 = unchanged).</summary>
    public float TracerWidth = 1f;

    /// <summary>Width of the soft glow sheath around each tracer, as a multiple of the tracer's width (0 = none).</summary>
    public float Sheath = 3f;

    /// <summary>Seconds the glow sheath lingers after the shot (the Sniper's long trails).</summary>
    public float TrailLife = 0.2f;

    /// <summary>Sparks thrown from the muzzle per shot.</summary>
    public int MuzzleSparks;

    /// <summary>Sparks (or embers, or ring sparks) where each tracer ends.</summary>
    public int ImpactSparks;

    /// <summary>Sparks thrown from the ejection port per shot (the LMG's brass showers).</summary>
    public int BrassSparks;

    /// <summary>Start speed of the sparks (m/s).</summary>
    public float SparkSpeed = 4f;

    /// <summary>How hard sparks fall (m/s^2, embers fall slowly).</summary>
    public float SparkGravity = 9f;

    /// <summary>Seconds a spark lives.</summary>
    public float SparkLife = 0.3f;

    /// <summary>Weapon glow strength (first and third person), 0..1.</summary>
    public float BarrelGlow = 0.5f;

    /// <summary>Screen-edge tint alpha at full intensity (capped at <see cref="FrenzyLooks.MaxEdgeAlpha"/>).</summary>
    public float EdgeAlpha = 0.12f;

    /// <summary>Tracers cycle through the rainbow (Special).</summary>
    public bool Rainbow;

    public FrenzySignature Signature;

    public FrenzyLook Clone() => (FrenzyLook)MemberwiseClone();
}

/// <summary>
/// The per-class frenzy looks and the pure rules around them: level scaling, the setting, the edge tint and the pulse /
/// flicker clocks (all at most <see cref="MaxFlashHz"/>, so nothing strobes). No GL here, so the self-test checks it all.
/// </summary>
public static class FrenzyLooks
{
    /// <summary>No light change in the frenzy visuals repeats faster than this (accessibility: photosensitivity guidance).</summary>
    public const float MaxFlashHz = 3f;

    /// <summary>The edge tint never gets more opaque than this, at the very edge of the screen.</summary>
    public const float MaxEdgeAlpha = 0.25f;

    /// <summary>
    /// How far the edge tint reaches in, as a fraction of the screen's height (and the same distance from the sides):
    /// the middle of the screen, where you aim, is never tinted.
    /// </summary>
    public const float EdgeDepth = 0.16f;

    /// <summary>The chromatic flicker (Special) and the glitch ghost's on/off rate.</summary>
    public const float GlitchHz = 2.5f;

    /// <summary>Weapon glow breathing rate.</summary>
    public const float GlowBreathHz = 0.8f;

    /// <summary>Intensity per frenzy level 0..6: subtle at 1-2, strong at 5-6.</summary>
    public static readonly float[] LevelIntensity = [0f, 0.12f, 0.25f, 0.45f, 0.65f, 0.88f, 1f];

    /// <summary>What <see cref="FrenzyVisualMode.Subtle"/> keeps of the particle counts and the edge tint.</summary>
    public const float SubtleScale = 0.35f;

    /// <summary>Intensity for a frenzy level (clamped to 0..6), before the setting.</summary>
    public static float Intensity(int level) => LevelIntensity[Math.Clamp(level, 0, LevelIntensity.Length - 1)];

    /// <summary>Intensity for a level under a setting: Off is 0, Subtle a fraction of Full.</summary>
    public static float Intensity(int level, FrenzyVisualMode mode) => mode switch
    {
        FrenzyVisualMode.Off => 0f,
        FrenzyVisualMode.Subtle => Intensity(level) * SubtleScale,
        _ => Intensity(level),
    };

    /// <summary>
    /// How much a tracer's colour and width move towards the class look: the full level intensity in Full, and still
    /// clearly visible in Subtle (colour reads at a glance and costs nothing); 0 when Off.
    /// </summary>
    public static float TracerBlend(int level, FrenzyVisualMode mode) => mode == FrenzyVisualMode.Off ? 0f : MathF.Min(1f, Intensity(level) * (mode == FrenzyVisualMode.Subtle ? 0.8f : 1f) * 1.4f);

    /// <summary>Are the screen-space extras (speed lines, glint, chroma, kill pulse) drawn in this mode?</summary>
    public static bool ScreenEffects(FrenzyVisualMode mode) => mode == FrenzyVisualMode.Full;

    /// <summary>
    /// The edge-tint pulse rate, which follows the frenzy music's level (0..6): a slow breath at 1, a quick beat at 6,
    /// never faster than <see cref="MaxFlashHz"/>.
    /// </summary>
    public static float PulseHz(int musicLevel) => MathF.Min(0.6f + 0.25f * Math.Clamp(musicLevel, 0, 6), MaxFlashHz);

    /// <summary>A smooth 0..1 pulse at <paramref name="hz"/> (a raised sine: one rise and fall per cycle).</summary>
    public static float Pulse(double time, float hz) => 0.5f + 0.5f * MathF.Sin((float)(time * hz % 1.0 * Math.Tau));

    /// <summary>
    /// The glitch flicker: on/off at <see cref="GlitchHz"/> with short ramps (no hard edges), so at most
    /// <see cref="GlitchHz"/> light changes in each direction per second.
    /// </summary>
    public static float Flicker(double time)
    {
        var ph = (float)(time * GlitchHz % 1.0);
        // On for the first 40% of each cycle, with 10% ramps either side.
        if (ph < 0.1f)
            return ph / 0.1f;
        if (ph < 0.4f)
            return 1f;
        if (ph < 0.5f)
            return 1f - (ph - 0.4f) / 0.1f;
        return 0f;
    }

    /// <summary>The edge tint's alpha now (at the very edge; it fades to 0 at <see cref="EdgeDepth"/>).</summary>
    public static float EdgeAlpha(FrenzyLook look, int level, int musicLevel, FrenzyVisualMode mode, double time)
    {
        var k = Intensity(level, mode);
        if (k <= 0)
            return 0f;
        var a = MathF.Min(look.EdgeAlpha, MaxEdgeAlpha) * k;
        a *= 0.7f + 0.3f * Pulse(time, PulseHz(musicLevel));
        return MathF.Min(a, MaxEdgeAlpha);
    }

    /// <summary>The rainbow (Special): hue 0..1 to an RGB colour, full saturation.</summary>
    public static Vector3 Hue(float h)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        var x = 1f - MathF.Abs(h % 2f - 1f);
        return (int)h switch
        {
            0 => new Vector3(1, x, 0),
            1 => new Vector3(x, 1, 0),
            2 => new Vector3(0, 1, x),
            3 => new Vector3(0, x, 1),
            4 => new Vector3(x, 0, 1),
            _ => new Vector3(1, 0, x),
        };
    }

    /// <summary>The built-in look of every class (one per <see cref="WeaponClass"/>, in enum order).</summary>
    public static FrenzyLook[] Defaults() =>
    [
        new()
        {
            Class = WeaponClass.Pistol, Name = "PISTOL", Signature = FrenzySignature.QuickDrawGlint,
            Color = new(1f, 0.78f, 0.25f), Accent = new(1f, 0.92f, 0.55f),
            TracerWidth = 1.3f, Sheath = 2.5f, TrailLife = 0.18f, MuzzleSparks = 7, ImpactSparks = 3, SparkSpeed = 5f, SparkGravity = 7f, SparkLife = 0.28f,
            BarrelGlow = 0.55f, EdgeAlpha = 0.11f,
        },
        new()
        {
            Class = WeaponClass.SMG, Name = "SMG", Signature = FrenzySignature.SpeedLines,
            Color = new(0.15f, 0.95f, 1f), Accent = new(0.6f, 1f, 1f),
            TracerWidth = 1.2f, Sheath = 3f, TrailLife = 0.12f, MuzzleSparks = 2, ImpactSparks = 2, SparkSpeed = 6f, SparkGravity = 4f, SparkLife = 0.18f,
            BarrelGlow = 0.6f, EdgeAlpha = 0.12f,
        },
        new()
        {
            Class = WeaponClass.AssaultRifle, Name = "ASSAULT RIFLE", Signature = FrenzySignature.HeatShimmer,
            Color = new(1f, 0.5f, 0.12f), Accent = new(1f, 0.75f, 0.3f),
            TracerWidth = 1.35f, Sheath = 3f, TrailLife = 0.2f, MuzzleSparks = 3, ImpactSparks = 3, SparkSpeed = 4f, SparkGravity = 8f, SparkLife = 0.25f,
            BarrelGlow = 0.7f, EdgeAlpha = 0.12f,
        },
        new()
        {
            Class = WeaponClass.DMR, Name = "MARKSMAN", Signature = FrenzySignature.ImpactRings,
            Color = new(0.95f, 0.97f, 1f), Accent = new(0.75f, 0.85f, 1f),
            TracerWidth = 1.1f, Sheath = 2f, TrailLife = 0.3f, MuzzleSparks = 2, ImpactSparks = 12, SparkSpeed = 3.5f, SparkGravity = 0f, SparkLife = 0.35f,
            BarrelGlow = 0.45f, EdgeAlpha = 0.09f,
        },
        new()
        {
            Class = WeaponClass.Sniper, Name = "SNIPER", Signature = FrenzySignature.KillPulse,
            Color = new(0.55f, 0.75f, 1f), Accent = new(0.85f, 0.95f, 1f),
            TracerWidth = 1.5f, Sheath = 4f, TrailLife = 1.4f, MuzzleSparks = 3, ImpactSparks = 5, SparkSpeed = 3f, SparkGravity = 2f, SparkLife = 0.5f,
            BarrelGlow = 0.5f, EdgeAlpha = 0.1f,
        },
        new()
        {
            Class = WeaponClass.Shotgun, Name = "SHOTGUN", Signature = FrenzySignature.Embers,
            Color = new(1f, 0.28f, 0.05f), Accent = new(1f, 0.6f, 0.15f),
            TracerWidth = 1.8f, Sheath = 2.5f, TrailLife = 0.15f, MuzzleSparks = 5, ImpactSparks = 2, SparkSpeed = 1.6f, SparkGravity = 1.5f, SparkLife = 0.9f,
            BarrelGlow = 0.6f, EdgeAlpha = 0.13f,
        },
        new()
        {
            Class = WeaponClass.LMG, Name = "LMG", Signature = FrenzySignature.BrassShower,
            Color = new(1f, 0.92f, 0.6f), Accent = new(1f, 0.55f, 0.15f),
            TracerWidth = 1.3f, Sheath = 2.5f, TrailLife = 0.18f, MuzzleSparks = 1, ImpactSparks = 2, BrassSparks = 4, SparkSpeed = 3f, SparkGravity = 11f, SparkLife = 0.45f,
            BarrelGlow = 0.85f, EdgeAlpha = 0.12f,
        },
        new()
        {
            Class = WeaponClass.Special, Name = "SPECIAL", Signature = FrenzySignature.Glitch, Rainbow = true,
            Color = new(0.85f, 0.35f, 1f), Accent = new(0.2f, 1f, 0.9f),
            TracerWidth = 1.3f, Sheath = 3f, TrailLife = 0.3f, MuzzleSparks = 3, ImpactSparks = 4, SparkSpeed = 4f, SparkGravity = 0f, SparkLife = 0.3f,
            BarrelGlow = 0.6f, EdgeAlpha = 0.12f,
        },
    ];

    private static FrenzyLook[] _all = Defaults();

    /// <summary>Where the looks come from: "built in", or the data file that overrode some.</summary>
    public static string Source { get; private set; } = "built in";

    /// <summary>The look for a class (an unknown class gets the Special look).</summary>
    public static FrenzyLook Get(WeaponClass c) => (int)c < _all.Length ? _all[(int)c] : _all[^1];

    /// <summary>Every look, in class order.</summary>
    public static IReadOnlyList<FrenzyLook> All => _all;

    /// <summary>The shared per-class data file (each frenzy feature owns one section of each class entry; this owns "visual").</summary>
    public static string DataFile => Path.Combine(AppContext.BaseDirectory, "Weapons", "Frenzy", "classes.json");

    /// <summary>Loads the "visual" sections of <see cref="DataFile"/> over the built-in looks, if the file is there. Never throws.</summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(DataFile))
                return;
            var (looks, notes) = Parse(File.ReadAllText(DataFile));
            _all = looks;
            Source = "Weapons/Frenzy/classes.json";
            foreach (var n in notes)
                Console.WriteLine($"[frenzyfx] classes.json: {n}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.WriteLine($"[frenzyfx] classes.json not used ({ex.Message}); built-in looks");
        }
    }

    /// <summary>
    /// The looks with the "visual" sections of a classes.json applied over the defaults. Accepts "classes" as an object
    /// keyed by class name or as an array of entries with a "class" field. Unknown keys are ignored, numbers clamped.
    /// </summary>
    public static (FrenzyLook[] Looks, List<string> Notes) Parse(string json)
    {
        var looks = Defaults();
        var notes = new List<string>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("classes", out var classes))
            return (looks, notes);
        if (classes.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in classes.EnumerateObject())
                ApplyEntry(looks, p.Name, p.Value, notes);
        }
        else if (classes.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in classes.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("class", out var c) && c.ValueKind == JsonValueKind.String)
                    ApplyEntry(looks, c.GetString()!, e, notes);
        }
        return (looks, notes);
    }

    /// <summary>A class name as written in the data file: the enum name, ignoring case, spaces and dashes ("Assault Rifle").</summary>
    public static WeaponClass? ParseClass(string name)
    {
        var n = name.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (n.Equals("Marksman", StringComparison.OrdinalIgnoreCase))
            return WeaponClass.DMR;
        return Enum.TryParse<WeaponClass>(n, true, out var c) && Enum.IsDefined(c) ? c : null;
    }

    private static void ApplyEntry(FrenzyLook[] looks, string className, JsonElement entry, List<string> notes)
    {
        if (ParseClass(className) is not { } c)
        {
            notes.Add($"unknown class '{className}' skipped");
            return;
        }
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("visual", out var v) || v.ValueKind != JsonValueKind.Object)
            return;
        var l = looks[(int)c];
        foreach (var p in v.EnumerateObject())
        {
            var val = p.Value;
            switch (p.Name)
            {
                case "color" when Colour(val) is { } col: l.Color = col; break;
                case "accent" when Colour(val) is { } col: l.Accent = col; break;
                case "tracerWidth" when Num(val) is { } f: l.TracerWidth = Math.Clamp(f, 0.5f, 3f); break;
                case "sheath" when Num(val) is { } f: l.Sheath = Math.Clamp(f, 0f, 6f); break;
                case "trailLife" when Num(val) is { } f: l.TrailLife = Math.Clamp(f, 0.05f, 2f); break;
                case "muzzleSparks" when Num(val) is { } f: l.MuzzleSparks = (int)Math.Clamp(f, 0, 12); break;
                case "impactSparks" when Num(val) is { } f: l.ImpactSparks = (int)Math.Clamp(f, 0, 16); break;
                case "brassSparks" when Num(val) is { } f: l.BrassSparks = (int)Math.Clamp(f, 0, 8); break;
                case "sparkSpeed" when Num(val) is { } f: l.SparkSpeed = Math.Clamp(f, 0f, 12f); break;
                case "sparkGravity" when Num(val) is { } f: l.SparkGravity = Math.Clamp(f, -5f, 20f); break;
                case "sparkLife" when Num(val) is { } f: l.SparkLife = Math.Clamp(f, 0.05f, 1.5f); break;
                case "barrelGlow" when Num(val) is { } f: l.BarrelGlow = Math.Clamp(f, 0f, 1f); break;
                case "edgeAlpha" when Num(val) is { } f: l.EdgeAlpha = Math.Clamp(f, 0f, MaxEdgeAlpha); break;
                case "rainbow" when val.ValueKind is JsonValueKind.True or JsonValueKind.False: l.Rainbow = val.GetBoolean(); break;
                case "label" when val.ValueKind == JsonValueKind.String && val.GetString() is { Length: > 0 and <= 16 } s: l.Name = s.ToUpperInvariant(); break;
                case "signature":
                    break; // each class's signature effect is code; the key is documentation only
                default:
                    if (!p.Name.StartsWith('_'))
                        notes.Add($"{c}: unknown visual key '{p.Name}' ignored");
                    break;
            }
        }
    }

    private static float? Num(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out var f) && float.IsFinite(f) ? f : null;

    /// <summary>A colour as "#rrggbb" or [r, g, b] (0..1).</summary>
    private static Vector3? Colour(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String && e.GetString() is { Length: 7 } s && s[0] == '#'
            && int.TryParse(s.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return new Vector3((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
        if (e.ValueKind == JsonValueKind.Array && e.GetArrayLength() == 3)
        {
            var c = new float[3];
            var i = 0;
            foreach (var x in e.EnumerateArray())
            {
                if (Num(x) is not { } f)
                    return null;
                c[i++] = Math.Clamp(f, 0f, 1f);
            }
            return new Vector3(c[0], c[1], c[2]);
        }
        return null;
    }

    /// <summary>The built-in looks as a classes.json "visual" section per class (for the data file and the docs).</summary>
    public static string VisualJson(FrenzyLook l)
    {
        static string Hex(Vector3 c) => $"#{(int)MathF.Round(c.X * 255):x2}{(int)MathF.Round(c.Y * 255):x2}{(int)MathF.Round(c.Z * 255):x2}";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return string.Create(inv, $"{{ \"signature\": \"{l.Signature}\", \"color\": \"{Hex(l.Color)}\", \"accent\": \"{Hex(l.Accent)}\", \"tracerWidth\": {l.TracerWidth}, \"sheath\": {l.Sheath}, \"trailLife\": {l.TrailLife}, "
            + $"\"muzzleSparks\": {l.MuzzleSparks}, \"impactSparks\": {l.ImpactSparks}, \"brassSparks\": {l.BrassSparks}, \"sparkSpeed\": {l.SparkSpeed}, \"sparkGravity\": {l.SparkGravity}, "
            + $"\"sparkLife\": {l.SparkLife}, \"barrelGlow\": {l.BarrelGlow}, \"edgeAlpha\": {l.EdgeAlpha}, \"rainbow\": {(l.Rainbow ? "true" : "false")} }}");
    }
}
