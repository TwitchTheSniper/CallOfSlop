using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// One light setup for every scene shader (see <see cref="Lighting"/>): the key light, hemisphere ambient, fog strength
/// and the sky gradient fog fades towards. <see cref="Lighting.Default"/> is the game's classic look (maps without a sky);
/// <see cref="SkySettings.Light"/> builds one from a map's "skybox" section.
/// </summary>
/// <param name="SunDir">Unit vector towards the key light (the sun, or the moon at night).</param>
/// <param name="SunColor">Key light colour times strength.</param>
/// <param name="SkyFog">Fog takes the sky gradient's colour along each view ray (false: the flat fog colour).</param>
/// <param name="HazeDir">Towards the real sun (the horizon glow is on its side, even when the moon is the key light).</param>
/// <param name="Haze">Horizon glow colour (xyz) and strength (w).</param>
public readonly record struct LightSetup(
    Vector3 SunDir, Vector3 SunColor, Vector3 AmbSky, Vector3 AmbGround, float FogMax,
    bool SkyFog, Vector3 Zenith, Vector3 Horizon, Vector3 Ground, Vector3 HazeDir, Vector4 Haze);

/// <summary>
/// The scene's one light setup, shared by every renderer so boxes, terrain, foliage, props and meshes are lit the
/// same way: a sun (direction + colour), hemisphere ambient (sky light from above, ground bounce from below,
/// tinted a little by the map's sky colour) and linear distance fog towards the sky.
/// All of it is cheap per-pixel maths, no shadow maps.
///
/// Shaders include <see cref="Glsl"/> right after their #version line and call <see cref="Upload"/> once after
/// linking. Upload remembers the program, and <see cref="Use"/> (called when the map's sky changes, see
/// <see cref="SkyRenderer.Use"/>) re-sends the light to every remembered program, so the per-frame cost is nothing.
/// Per-frame values (camera position, fog colour and range) are set by the renderer that owns the program.
///
/// Maps without a "skybox" section get <see cref="Default"/>: exactly the constants the game always had, and fog
/// towards the flat sky colour. Maps with one get their sky's sun and ambient, and fog that fades into the sky's
/// gradient in each direction (SkyGradient below, the same maths as <see cref="SkySettings.Gradient(Vector3)"/>).
/// </summary>
public static unsafe class Lighting
{
    /// <summary>Unit vector pointing <em>towards</em> the classic sun (high in the sky, from the north-west).</summary>
    public static readonly Vector3 DefaultSunDirection = Vector3.Normalize(new Vector3(-0.4f, 0.8f, -0.3f));

    /// <summary>Classic sunlight colour, slightly warm. Multiplied by <see cref="DefaultSunStrength"/>.</summary>
    public static readonly Vector3 DefaultSunColor = new(1.0f, 0.95f, 0.86f);

    /// <summary>How bright full sun on a surface facing it is, on top of the ambient light.</summary>
    public const float DefaultSunStrength = 0.55f;

    /// <summary>Ambient from the sky (faces pointing up), a little cool.</summary>
    public static readonly Vector3 DefaultSkyAmbient = new(0.50f, 0.52f, 0.56f);

    /// <summary>Ambient bounced off the ground (faces pointing down), a little warm.</summary>
    public static readonly Vector3 DefaultGroundAmbient = new(0.40f, 0.38f, 0.34f);

    /// <summary>How far the sky ambient leans towards the hue of the map's sky colour (0 = not at all).</summary>
    public const float SkyTint = 0.2f;

    /// <summary>Fog never fully hides things: at the fog end distance colours are this much sky.</summary>
    public const float DefaultFogMax = 0.85f;

    /// <summary>The sky colour assumed before a map sets its own (the same as <see cref="Map.Sky"/>'s default).</summary>
    public static readonly Vector3 DefaultSky = new(0.36f, 0.42f, 0.50f);

    /// <summary>The classic light: what every map without a "skybox" section is lit with.</summary>
    public static readonly LightSetup Default = new(DefaultSunDirection, DefaultSunColor * DefaultSunStrength, DefaultSkyAmbient, DefaultGroundAmbient,
        DefaultFogMax, false, DefaultSky, DefaultSky, DefaultSky, DefaultSunDirection, Vector4.Zero);

    /// <summary>The light every registered program has now.</summary>
    public static LightSetup Current { get; private set; } = Default;

    /// <summary>How many times the light has changed (for logs and tests).</summary>
    public static int Changes { get; private set; }

    private static readonly List<uint> Programs = new();

    /// <summary>
    /// GLSL shared by the scene shaders: uniforms plus ApplyLight (hemisphere ambient + wrapped sun diffuse),
    /// SkyGradient and ApplyFog. Paste it after "#version 330 core".
    /// </summary>
    public const string Glsl = """
        uniform vec3 uSunDir;     // towards the sun
        uniform vec3 uSunColor;   // colour * strength
        uniform vec3 uAmbSky;
        uniform vec3 uAmbGround;
        uniform float uSkyTint;
        uniform float uFogMax;
        uniform vec3 uCam;
        uniform vec3 uFog;        // fog / sky colour
        uniform vec2 uFogRange;   // start, end (metres); end <= start turns fog off
        uniform float uSkyFog;    // 1: fog takes the sky gradient's colour along the view ray (maps with a sky)
        uniform vec3 uSkyZenith;
        uniform vec3 uSkyHorizon;
        uniform vec3 uSkyGround;
        uniform vec3 uSkyHazeDir; // towards the real sun
        uniform vec4 uSkyHaze;    // horizon glow colour, strength

        // wrap: 0 = plain Lambert (solid surfaces), ~0.5 = leaves (light leaks round the edges)
        vec3 ApplyLight(vec3 albedo, vec3 n, float wrap)
        {
            // Lean the sky light towards the sky's hue (not its brightness, so dark skies don't darken everything).
            float skyLum = max(dot(uFog, vec3(0.3333)), 0.05);
            vec3 sky = mix(uAmbSky, uFog / skyLum * dot(uAmbSky, vec3(0.3333)), uSkyTint);
            vec3 amb = mix(uAmbGround, sky, n.y * 0.5 + 0.5);
            float diff = max((dot(n, uSunDir) + wrap) / (1.0 + wrap), 0.0);
            return albedo * (amb + uSunColor * diff);
        }

        // The sky's colour in direction d (unit) without sun, moon, stars or clouds (SkySettings.Gradient in C#).
        vec3 SkyGradient(vec3 d)
        {
            float y = d.y;
            vec3 c = y >= 0.0 ? mix(uSkyHorizon, uSkyZenith, sqrt(min(y, 1.0)))
                              : mix(uSkyHorizon, uSkyGround, smoothstep(0.0, 0.3, -y));
            float s = max(dot(d, uSkyHazeDir), 0.0);
            float h = uSkyHaze.w * pow(s, 6.0) * (1.0 - 0.6 * min(abs(y), 1.0));
            return mix(c, uSkyHaze.rgb, clamp(h, 0.0, 1.0));
        }

        vec3 ApplyFog(vec3 col, vec3 world)
        {
            if (uFogRange.y <= uFogRange.x)
                return col;
            vec3 v = world - uCam;
            float dist = length(v);
            float f = clamp((dist - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0);
            vec3 fogCol = uSkyFog > 0.5 ? SkyGradient(v / max(dist, 1e-4)) : uFog;
            return mix(col, fogCol, f * uFogMax);
        }
        """;

    /// <summary>Sets the light uniforms on <paramref name="program"/> (binds it) and remembers it for <see cref="Use"/>.</summary>
    public static void Upload(GlProgram program)
    {
        program.Use();
        if (!Programs.Contains(program.Id))
            Programs.Add(program.Id);
        Send(program.Id, Current);
        // A neutral sky until the first SetFog, so menus drawn before any map (gun previews) aren't tinted.
        Native.glUniform3f(Loc(program.Id, "uFog\0"u8), DefaultSky.X, DefaultSky.Y, DefaultSky.Z);
    }

    /// <summary>
    /// Makes <paramref name="light"/> the light of every scene shader. Does nothing when it already is; otherwise re-sends
    /// it to every program <see cref="Upload"/> saw (a few dozen glUniform calls) and puts the bound program back.
    /// Returns true if anything changed.
    /// </summary>
    public static bool Use(in LightSetup light)
    {
        if (light == Current)
            return false;
        Current = light;
        Changes++;
        if (Programs.Count == 0)
            return true;
        int bound;
        Native.glGetIntegerv(GL_CURRENT_PROGRAM, &bound);
        for (var i = Programs.Count - 1; i >= 0; i--)
        {
            if (!Native.glIsProgram(new GlProgram(Programs[i])))
            {
                Programs.RemoveAt(i); // deleted by its renderer
                continue;
            }
            Native.glUseProgram(new GlProgram(Programs[i]));
            Send(Programs[i], light);
        }
        Native.glUseProgram(new GlProgram((uint)bound));
        return true;
    }

    private static void Send(uint program, in LightSetup l)
    {
        Native.glUniform3f(Loc(program, "uSunDir\0"u8), l.SunDir.X, l.SunDir.Y, l.SunDir.Z);
        Native.glUniform3f(Loc(program, "uSunColor\0"u8), l.SunColor.X, l.SunColor.Y, l.SunColor.Z);
        Native.glUniform3f(Loc(program, "uAmbSky\0"u8), l.AmbSky.X, l.AmbSky.Y, l.AmbSky.Z);
        Native.glUniform3f(Loc(program, "uAmbGround\0"u8), l.AmbGround.X, l.AmbGround.Y, l.AmbGround.Z);
        Native.glUniform1f(Loc(program, "uSkyTint\0"u8), SkyTint);
        Native.glUniform1f(Loc(program, "uFogMax\0"u8), l.FogMax);
        Native.glUniform1f(Loc(program, "uSkyFog\0"u8), l.SkyFog ? 1f : 0f);
        Native.glUniform3f(Loc(program, "uSkyZenith\0"u8), l.Zenith.X, l.Zenith.Y, l.Zenith.Z);
        Native.glUniform3f(Loc(program, "uSkyHorizon\0"u8), l.Horizon.X, l.Horizon.Y, l.Horizon.Z);
        Native.glUniform3f(Loc(program, "uSkyGround\0"u8), l.Ground.X, l.Ground.Y, l.Ground.Z);
        Native.glUniform3f(Loc(program, "uSkyHazeDir\0"u8), l.HazeDir.X, l.HazeDir.Y, l.HazeDir.Z);
        Native.glUniform4f(Loc(program, "uSkyHaze\0"u8), l.Haze.X, l.Haze.Y, l.Haze.Z, l.Haze.W);
    }

    private static int Loc(uint program, ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(new GlProgram(program), p);
    }

    /// <summary>The uniform names <see cref="Glsl"/> declares (the self-test checks the shader and the C# agree).</summary>
    public static readonly string[] UniformNames =
        ["uSunDir", "uSunColor", "uAmbSky", "uAmbGround", "uSkyTint", "uFogMax", "uCam", "uFog", "uFogRange", "uSkyFog", "uSkyZenith", "uSkyHorizon", "uSkyGround", "uSkyHazeDir", "uSkyHaze"];
}
