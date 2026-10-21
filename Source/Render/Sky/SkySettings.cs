using System.Numerics;

namespace CallOfSlop;

/// <summary>What a map's sky is drawn from.</summary>
public enum SkyType : byte
{
    /// <summary>Gradient, sun, moon, stars and clouds, all computed in the shader.</summary>
    Procedural,

    /// <summary>Six square images (+X, -X, +Y, -Y, +Z, -Z).</summary>
    Cubemap,

    /// <summary>One 2:1 panorama (PNG, or HDR for high dynamic range).</summary>
    Equirect,
}

/// <summary>Settings > Video "Sky quality".</summary>
public enum SkyQuality : byte
{
    /// <summary>Gradient, sun and moon only.</summary>
    Low,

    /// <summary>Plus clouds and stars.</summary>
    High,
}

/// <summary>
/// A map's sky (the optional "skybox" section of a map file, see Maps/README.md "Sky"). Pure data plus the maths the
/// renderer and the lighting share: where the sun is (a direction, or a time of day at a latitude), the zenith /
/// horizon / ground gradient (<see cref="Gradient"/>, mirrored by <see cref="Lighting.Glsl"/>'s SkyGradient so fog
/// fades distant things into exactly the sky behind them) and the light setup it gives every scene shader
/// (<see cref="Light"/>). Maps without a skybox don't have one of these and keep their flat sky colour.
///
/// No GL in here, so it can be checked headlessly (self-test "Sky") and moved to a scene graph later unchanged.
/// </summary>
public sealed class SkySettings
{
    /// <summary>The preset this sky started from ("" = the plain defaults); files only store what differs from it.</summary>
    public string Preset = "";

    public SkyType Type = SkyType.Procedural;

    // ---- Gradient ----------------------------------------------------------------------------------------------
    public Vector3 Zenith = new(0.27f, 0.47f, 0.76f);
    public Vector3 Horizon = new(0.70f, 0.79f, 0.88f);
    public Vector3 Ground = new(0.40f, 0.40f, 0.38f);

    // ---- Sun ---------------------------------------------------------------------------------------------------
    /// <summary>Towards the sun. Null: worked out from <see cref="TimeOfDay"/>, <see cref="Latitude"/> and <see cref="DayOfYear"/>.</summary>
    public Vector3? SunDirection;

    /// <summary>Hours, 0-24 (12 = solar noon).</summary>
    public float TimeOfDay = 13f;

    /// <summary>Degrees, -90 (south pole) to 90 (north pole).</summary>
    public float Latitude = 40f;

    /// <summary>1-366; sets the season (80 = March equinox, 172 = June solstice).</summary>
    public float DayOfYear = 80f;

    /// <summary>Degrees: turns the compass. 0 = north is -Z (and east +X).</summary>
    public float Heading;

    /// <summary>Angular diameter of the sun disc in degrees (the real sun is 0.53; games draw it bigger).</summary>
    public float SunSize = 1.6f;

    public Vector3 SunColor = new(1f, 0.95f, 0.86f);

    /// <summary>Brightness of the sunlight on surfaces and of the disc (1 = the game's usual sun).</summary>
    public float SunIntensity = 1f;

    /// <summary>Strength of the halo round the sun.</summary>
    public float SunGlow = 0.5f;

    /// <summary>Draw the sun disc (images usually have their own).</summary>
    public bool SunDisc = true;

    // ---- Moon --------------------------------------------------------------------------------------------------
    public bool Moon;

    /// <summary>Towards the moon. Null: opposite the sun, at least ~20 degrees up.</summary>
    public Vector3? MoonDirection;

    public float MoonSize = 2.6f;
    public Vector3 MoonColor = new(0.86f, 0.90f, 1.0f);

    /// <summary>Moonlight on surfaces once the sun is down (as a fraction of the usual sun).</summary>
    public float MoonIntensity = 0.45f;

    // ---- Stars -------------------------------------------------------------------------------------------------
    /// <summary>Star brightness, 0 (none) to 2.</summary>
    public float Stars;

    // ---- Clouds ------------------------------------------------------------------------------------------------
    /// <summary>0 = clear, 1 = covered.</summary>
    public float CloudCoverage;

    /// <summary>How opaque the clouds are, 0-1.</summary>
    public float CloudDensity = 0.85f;

    /// <summary>Size of the cloud pattern in metres (on the cloud layer).</summary>
    public float CloudScale = 900f;

    /// <summary>Drift speed in metres per second.</summary>
    public float CloudSpeed = 6f;

    /// <summary>Compass heading the clouds drift towards, degrees (0 = north, 90 = east).</summary>
    public float CloudHeading = 60f;

    /// <summary>Height of the cloud layer in metres (lower = bigger, faster-looking clouds).</summary>
    public float CloudAltitude = 1500f;

    public Vector3 CloudColor = new(1f, 1f, 1f);

    /// <summary>Colour of the thick (shadowed) parts.</summary>
    public Vector3 CloudShade = new(0.64f, 0.68f, 0.74f);

    // ---- Haze, fog, ambient ------------------------------------------------------------------------------------
    /// <summary>Glow along the horizon on the sun's side (dusk orange, desert dust); fog picks it up too.</summary>
    public Vector3 HazeColor = new(1f, 0.92f, 0.78f);

    public float HazeStrength = 0.25f;

    /// <summary>Fog takes the sky's colour in each direction (true) or the flat horizon colour (false).</summary>
    public bool FogFromSky = true;

    /// <summary>How much of the sky colour fog reaches at the map's fog end distance (the game's usual is 0.85).</summary>
    public float FogMax = Lighting.DefaultFogMax;

    /// <summary>Ambient light multiplier (1 = the game's usual).</summary>
    public float Ambient = 1f;

    // ---- Images ------------------------------------------------------------------------------------------------
    /// <summary>The panorama file (equirect), relative to the map file's folder.</summary>
    public string Image = "";

    /// <summary>Cubemap faces in the order +X, -X, +Y, -Y, +Z, -Z (relative to the map file's folder).</summary>
    public string[] Faces = [];

    /// <summary>Brightness multiplier for images (for HDR images: the exposure before tone mapping).</summary>
    public float Exposure = 1f;

    /// <summary>Turns the image round the vertical axis, degrees.</summary>
    public float Rotation;

    /// <summary>The decoded image (set by the map loader for cubemap and equirect skies).</summary>
    public SkyImage? Loaded;

    /// <summary>Which gradient colours the file gave (the others come from the preset, or are measured from the image).</summary>
    public bool ZenithSet, HorizonSet, GroundSet;

    public SkySettings Clone()
    {
        var c = (SkySettings)MemberwiseClone();
        c.Faces = (string[])Faces.Clone();
        return c;
    }

    // ---- Presets -----------------------------------------------------------------------------------------------

    /// <summary>The named presets a map can start from ("skybox": { "preset": "dusk" }), in display order.</summary>
    public static readonly string[] PresetNames = ["clear", "overcast", "dusk", "night", "desert", "alpine"];

    /// <summary>Other spellings accepted for preset names.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["clear day"] = "clear",
        ["clearday"] = "clear",
        ["day"] = "clear",
        ["desert haze"] = "desert",
        ["deserthaze"] = "desert",
        ["haze"] = "desert",
        ["sunset"] = "dusk",
        ["cloudy"] = "overcast",
    };

    /// <summary>The canonical preset name for <paramref name="name"/>, or null if there is no such preset.</summary>
    public static string? PresetKey(string name)
    {
        var n = name.Trim();
        if (Aliases.TryGetValue(n, out var a))
            return a;
        return PresetNames.FirstOrDefault(p => p.Equals(n, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A fresh copy of preset <paramref name="name"/> (any spelling <see cref="PresetKey"/> accepts).</summary>
    public static SkySettings FromPreset(string name)
    {
        var key = PresetKey(name) ?? throw new ArgumentException($"no sky preset '{name}'");
        var s = new SkySettings { Preset = key };
        switch (key)
        {
            case "clear":
                // The game's usual sun (same direction as maps without a sky), fair-weather cumulus.
                s.SunDirection = Lighting.DefaultSunDirection;
                s.CloudCoverage = 0.32f;
                break;
            case "overcast":
                s.Zenith = new Vector3(0.50f, 0.53f, 0.57f);
                s.Horizon = new Vector3(0.69f, 0.71f, 0.73f);
                s.Ground = new Vector3(0.38f, 0.38f, 0.38f);
                s.TimeOfDay = 11.5f;
                s.SunIntensity = 0.45f;
                s.SunGlow = 0.25f;
                s.SunDisc = false;
                s.HazeStrength = 0f;
                s.CloudCoverage = 0.92f;
                s.CloudDensity = 0.97f;
                s.CloudScale = 1300f;
                s.CloudSpeed = 9f;
                s.CloudAltitude = 1100f;
                s.CloudColor = new Vector3(0.80f, 0.81f, 0.83f);
                s.CloudShade = new Vector3(0.50f, 0.52f, 0.56f);
                s.Ambient = 1.18f;
                break;
            case "dusk":
                s.Zenith = new Vector3(0.17f, 0.22f, 0.42f);
                s.Horizon = new Vector3(0.93f, 0.62f, 0.42f);
                s.Ground = new Vector3(0.24f, 0.20f, 0.21f);
                s.TimeOfDay = 17.65f;
                s.SunSize = 2.2f;
                s.SunColor = new Vector3(1f, 0.64f, 0.38f);
                s.SunIntensity = 1.05f;
                s.SunGlow = 0.9f;
                s.HazeColor = new Vector3(1f, 0.55f, 0.28f);
                s.HazeStrength = 0.75f;
                s.CloudCoverage = 0.34f;
                s.CloudDensity = 0.8f;
                s.CloudScale = 1100f;
                s.CloudSpeed = 4f;
                s.CloudColor = new Vector3(1f, 0.68f, 0.52f);
                s.CloudShade = new Vector3(0.38f, 0.30f, 0.38f);
                s.Stars = 0.12f;
                s.Ambient = 0.88f;
                break;
            case "night":
                s.Zenith = new Vector3(0.015f, 0.025f, 0.07f);
                s.Horizon = new Vector3(0.09f, 0.12f, 0.21f);
                s.Ground = new Vector3(0.035f, 0.035f, 0.045f);
                s.TimeOfDay = 0.5f;
                s.SunGlow = 0f;
                s.HazeStrength = 0f;
                s.Moon = true;
                s.Stars = 1f;
                s.CloudCoverage = 0.22f;
                s.CloudDensity = 0.7f;
                s.CloudColor = new Vector3(0.20f, 0.23f, 0.31f);
                s.CloudShade = new Vector3(0.07f, 0.08f, 0.12f);
                s.Ambient = 0.62f;
                s.FogMax = 0.9f;
                break;
            case "desert":
                s.Zenith = new Vector3(0.45f, 0.58f, 0.76f);
                s.Horizon = new Vector3(0.88f, 0.79f, 0.66f);
                s.Ground = new Vector3(0.56f, 0.47f, 0.38f);
                s.TimeOfDay = 14.5f;
                s.Latitude = 32f;
                s.DayOfYear = 172f;
                s.SunColor = new Vector3(1f, 0.93f, 0.80f);
                s.SunIntensity = 1.1f;
                s.SunGlow = 0.7f;
                s.HazeColor = new Vector3(1f, 0.88f, 0.68f);
                s.HazeStrength = 0.5f;
                s.CloudCoverage = 0.1f;
                s.CloudDensity = 0.55f;
                s.CloudScale = 1600f;
                s.CloudAltitude = 2500f;
                s.CloudColor = new Vector3(1f, 0.97f, 0.92f);
                s.CloudShade = new Vector3(0.80f, 0.74f, 0.68f);
                s.FogMax = 0.9f;
                break;
            case "alpine":
                s.Zenith = new Vector3(0.14f, 0.36f, 0.76f);
                s.Horizon = new Vector3(0.72f, 0.83f, 0.94f);
                s.Ground = new Vector3(0.52f, 0.56f, 0.60f);
                s.TimeOfDay = 10.5f;
                s.Latitude = 46f;
                s.DayOfYear = 200f;
                s.SunColor = new Vector3(1f, 0.97f, 0.92f);
                s.SunIntensity = 1.1f;
                s.SunSize = 1.4f;
                s.HazeStrength = 0.15f;
                s.CloudCoverage = 0.42f;
                s.CloudScale = 700f;
                s.CloudSpeed = 8f;
                s.CloudAltitude = 1200f;
                s.CloudShade = new Vector3(0.62f, 0.68f, 0.78f);
                break;
        }
        return s;
    }

    // ---- Derived values ----------------------------------------------------------------------------------------

    /// <summary>Unit vector towards the sun (from <see cref="SunDirection"/>, or the time of day).</summary>
    public Vector3 SunDir => SunDirection is { } d && d.LengthSquared() > 1e-8f ? Vector3.Normalize(d) : SunFromTime(TimeOfDay, Latitude, DayOfYear, Heading);

    /// <summary>Unit vector towards the moon.</summary>
    public Vector3 MoonDir
    {
        get
        {
            if (MoonDirection is { } d && d.LengthSquared() > 1e-8f)
                return Vector3.Normalize(d);
            var s = SunDir;
            return Vector3.Normalize(new Vector3(-s.X, MathF.Max(-s.Y, 0.36f), -s.Z));
        }
    }

    /// <summary>
    /// Where the sun is at <paramref name="hours"/> local solar time, at <paramref name="latitudeDeg"/>, on day
    /// <paramref name="dayOfYear"/>; unit vector towards it, with north = -Z and east = +X (turned by
    /// <paramref name="headingDeg"/>). Standard solar geometry: declination from the day, hour angle 15 degrees per hour.
    /// </summary>
    public static Vector3 SunFromTime(float hours, float latitudeDeg, float dayOfYear, float headingDeg)
    {
        const float deg = MathF.PI / 180f;
        var decl = -23.44f * deg * MathF.Cos(2f * MathF.PI / 365f * (dayOfYear + 10f));
        var hourAngle = (hours - 12f) * 15f * deg;
        var lat = latitudeDeg * deg;
        // Components in the local horizon frame (east, north, up).
        var east = -MathF.Cos(decl) * MathF.Sin(hourAngle);
        var north = MathF.Sin(decl) * MathF.Cos(lat) - MathF.Cos(decl) * MathF.Cos(hourAngle) * MathF.Sin(lat);
        var up = MathF.Sin(decl) * MathF.Sin(lat) + MathF.Cos(decl) * MathF.Cos(hourAngle) * MathF.Cos(lat);
        var h = headingDeg * deg;
        var northW = new Vector3(MathF.Sin(h), 0, -MathF.Cos(h));
        var eastW = new Vector3(MathF.Cos(h), 0, MathF.Sin(h));
        return Vector3.Normalize(east * eastW + north * northW + up * Vector3.UnitY);
    }

    /// <summary>The sun's elevation above the horizon in degrees.</summary>
    public float SunElevation => MathF.Asin(Math.Clamp(SunDir.Y, -1f, 1f)) * 180f / MathF.PI;

    /// <summary>The haze strength actually used: fades out as the sun goes well below the horizon.</summary>
    public float HazeNow => HazeStrength * Smooth(-0.3f, 0.0f, SunDir.Y);

    /// <summary>
    /// The sky's colour in direction <paramref name="dir"/> (unit vector) without sun disc, moon, stars or clouds: the
    /// colour fog fades towards. Must match SkyGradient in <see cref="Lighting.Glsl"/>.
    /// </summary>
    public Vector3 Gradient(Vector3 dir) => Gradient(dir, Zenith, Horizon, Ground, SunDir, HazeColor, HazeNow);

    /// <inheritdoc cref="Gradient(Vector3)"/>
    public static Vector3 Gradient(Vector3 dir, Vector3 zenith, Vector3 horizon, Vector3 ground, Vector3 hazeDir, Vector3 hazeColor, float haze)
    {
        var y = dir.Y;
        var c = y >= 0
            ? Vector3.Lerp(horizon, zenith, MathF.Sqrt(MathF.Min(y, 1f)))
            : Vector3.Lerp(horizon, ground, Smooth(0f, 0.3f, -y));
        var s = MathF.Max(Vector3.Dot(dir, hazeDir), 0f);
        var h = haze * MathF.Pow(s, 6f) * (1f - 0.6f * MathF.Min(MathF.Abs(y), 1f));
        return Vector3.Lerp(c, hazeColor, Math.Clamp(h, 0f, 1f));
    }

    private static float Smooth(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// The light every scene shader uses under this sky: the key light (the sun, or the moon at night), ambient tinted
    /// by the zenith and ground colours, and the gradient fog.
    /// </summary>
    public LightSetup Light()
    {
        var sun = SunDir;
        var day = Smooth(-0.04f, 0.10f, sun.Y); // how much of the key light is the sun
        Vector3 keyDir, keyColor;
        if (day >= 0.5f || !Moon)
        {
            keyDir = Vector3.Normalize(sun with { Y = MathF.Max(sun.Y, 0.05f) }); // a set sun still grazes the ground
            keyColor = SunColor * (SunIntensity * Lighting.DefaultSunStrength * day);
        }
        else
        {
            var m = MoonDir;
            keyDir = Vector3.Normalize(m with { Y = MathF.Max(m.Y, 0.05f) });
            keyColor = MoonColor * (MoonIntensity * Lighting.DefaultSunStrength * (1f - day));
        }
        var skyAmb = Vector3.Lerp(Lighting.DefaultSkyAmbient, Hue(Zenith, Lighting.DefaultSkyAmbient), 0.3f) * Ambient;
        var groundAmb = Vector3.Lerp(Lighting.DefaultGroundAmbient, Hue(Ground, Lighting.DefaultGroundAmbient), 0.3f) * Ambient;
        return new LightSetup(keyDir, keyColor, skyAmb, groundAmb, FogMax, FogFromSky, Zenith, Horizon, Ground, sun, new Vector4(HazeColor, HazeNow));
    }

    /// <summary><paramref name="c"/>'s hue at <paramref name="like"/>'s average brightness.</summary>
    private static Vector3 Hue(Vector3 c, Vector3 like)
    {
        var avg = MathF.Max((c.X + c.Y + c.Z) / 3f, 0.004f);
        var target = (like.X + like.Y + like.Z) / 3f;
        return c / avg * target;
    }

    /// <summary>One line for logs: type, preset and the sun.</summary>
    public string Describe()
    {
        var kind = Type switch
        {
            SkyType.Cubemap => "cubemap",
            SkyType.Equirect => Image.EndsWith(".hdr", StringComparison.OrdinalIgnoreCase) ? "equirect HDR" : "equirect",
            _ => "procedural",
        };
        return $"{kind}{(Preset.Length > 0 ? $" '{Preset}'" : "")}, sun {SunElevation:0} deg up, clouds {CloudCoverage:0.##}, stars {Stars:0.##}{(Moon ? ", moon" : "")}";
    }
}
