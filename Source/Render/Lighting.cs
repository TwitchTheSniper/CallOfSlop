using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The scene's one light setup, shared by every renderer so boxes, foliage (and later terrain) are lit the
/// same way: a sun (direction + colour), hemisphere ambient (sky light from above, ground bounce from below,
/// tinted a little by the map's sky colour) and linear distance fog towards the sky colour.
/// All of it is cheap per-pixel maths, no shadow maps.
///
/// Shaders include <see cref="Glsl"/> right after their #version line and call <see cref="Upload"/> once after
/// linking (uniform values stay with the program). Per-frame values (camera position, fog colour and range)
/// are set by the renderer that owns the program.
/// </summary>
public static unsafe class Lighting
{
    /// <summary>Unit vector pointing <em>towards</em> the sun (high in the sky, from the north-west).</summary>
    public static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.4f, 0.8f, -0.3f));

    /// <summary>Sunlight colour, slightly warm. Multiplied by <see cref="SunStrength"/>.</summary>
    public static readonly Vector3 SunColor = new(1.0f, 0.95f, 0.86f);

    /// <summary>How bright full sun on a surface facing it is, on top of the ambient light.</summary>
    public const float SunStrength = 0.55f;

    /// <summary>Average ambient light (sides of things get about this much with no sun).</summary>
    public const float Ambient = 0.45f;

    /// <summary>Ambient from the sky (faces pointing up), a little cool.</summary>
    public static readonly Vector3 SkyAmbient = new(0.50f, 0.52f, 0.56f);

    /// <summary>Ambient bounced off the ground (faces pointing down), a little warm.</summary>
    public static readonly Vector3 GroundAmbient = new(0.40f, 0.38f, 0.34f);

    /// <summary>How far the sky ambient leans towards the map's sky colour (0 = not at all).</summary>
    public const float SkyTint = 0.2f;

    /// <summary>Fog never fully hides things: at the fog end distance colours are this much sky.</summary>
    public const float FogMax = 0.85f;

    /// <summary>
    /// GLSL shared by the scene shaders: uniforms plus ApplyLight (hemisphere ambient + wrapped sun diffuse)
    /// and ApplyFog. Paste it after "#version 330 core".
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

        // wrap: 0 = plain Lambert (solid surfaces), ~0.5 = leaves (light leaks round the edges)
        vec3 ApplyLight(vec3 albedo, vec3 n, float wrap)
        {
            vec3 sky = mix(uAmbSky, uFog * 1.15, uSkyTint);
            vec3 amb = mix(uAmbGround, sky, n.y * 0.5 + 0.5);
            float diff = max((dot(n, uSunDir) + wrap) / (1.0 + wrap), 0.0);
            return albedo * (amb + uSunColor * diff);
        }

        vec3 ApplyFog(vec3 col, vec3 world)
        {
            if (uFogRange.y <= uFogRange.x)
                return col;
            float f = clamp((distance(world, uCam) - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0);
            return mix(col, uFog, f * uFogMax);
        }
        """;

    /// <summary>Sets the constant light uniforms on <paramref name="program"/> (binds it).</summary>
    public static void Upload(GlProgram program)
    {
        program.Use();
        int Loc(ReadOnlySpan<byte> nameZ)
        {
            fixed (byte* p = nameZ)
                return Native.glGetUniformLocation(program, p);
        }
        var sun = SunColor * SunStrength;
        Native.glUniform3f(Loc("uSunDir\0"u8), SunDirection.X, SunDirection.Y, SunDirection.Z);
        Native.glUniform3f(Loc("uSunColor\0"u8), sun.X, sun.Y, sun.Z);
        Native.glUniform3f(Loc("uAmbSky\0"u8), SkyAmbient.X, SkyAmbient.Y, SkyAmbient.Z);
        Native.glUniform3f(Loc("uAmbGround\0"u8), GroundAmbient.X, GroundAmbient.Y, GroundAmbient.Z);
        Native.glUniform1f(Loc("uSkyTint\0"u8), SkyTint);
        Native.glUniform1f(Loc("uFogMax\0"u8), FogMax);
    }
}
