using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The flicker of flickering lights (flames, a dodgy bulb; docs/render/LIGHTING.md "Flicker"), shared by the light bake
/// (<see cref="LightBaker"/>), the scene shaders (<see cref="Lighting.Glsl"/>) and the prop shader's flame parts
/// (PropRenderer), so a torch's flame and the light it throws move together.
/// <para>
/// Each light has its own seed (<see cref="MapLight.Seed"/>: from its position, or from its prop copy and its number in the
/// prop), and from it its own phase, strength and pace (<see cref="Params"/>). Its brightness wavers by
/// <c>Re(z e^(i w1 t)) (1 - mix) + Re(z^2/|z| e^(i w2 t)) mix</c> around its average: a slow wave and a quicker one at twice
/// the phase, with <c>z</c> = strength x e^(i phase). Both clocks are whole cycles per <see cref="PropAnim.TimeWrap"/>,
/// so the shader time's wrap never jumps.
/// </para>
/// <para>
/// The bake keeps, per cell, the luminance-weighted sum of the lights' <c>z</c> (over all the light reaching the cell) and
/// their average pace. Sums of phasors filter linearly, so where several flames light one cell they blend smoothly, and
/// between two cells nothing pops (the old bake kept the phase of the brightest flame alone, and filtering a phase
/// swept through every phase in between). The average dimming (the old flicker's mean, <see cref="Dip"/> of the
/// flickering share) is unchanged, so lit maps are as bright on average as before.
/// </para>
/// </summary>
public static class LightFlicker
{
    /// <summary>The slow wave: 2048 cycles per 4096 s of shader time (0.5 Hz; the old flicker's main wave was 1.16 Hz).</summary>
    public const float Cycles1 = 2048f / (float)PropAnim.TimeWrap;

    /// <summary>The quicker wave: 5325 cycles per 4096 s (about 1.3 Hz).</summary>
    public const float Cycles2 = 5325f / (float)PropAnim.TimeWrap;

    /// <summary>The average dimming of a flickering light's share (as before: the old wave's mean).</summary>
    public const float Dip = 0.3f;

    /// <summary>A flickering light's swing at "flickerAmount" 1 (before its own 0.7-1.3 jitter): +-15% of its light (the old one swung +-30%).</summary>
    public const float Swing = 0.15f;

    /// <summary>The largest |z| the bake stores (snorm-like bytes: 128 is zero).</summary>
    public const float ZRange = 0.6f;

    public const float MinSpeed = 0.25f, MaxSpeed = 4f, MaxAmount = 3f;

    /// <summary>A light's seed from its position (lights placed by map ops), 0..1.</summary>
    public static float SeedOf(Vector3 pos)
    {
        var x = MathF.Floor(pos.X * 8f + 0.5f);
        var y = MathF.Floor(pos.Y * 8f + 0.5f);
        var z = MathF.Floor(pos.Z * 8f + 0.5f);
        return PropAnim.Fract(52.9829189f * PropAnim.Fract(x * 0.06711056f + z * 0.00583715f + y * 0.0127391f));
    }

    /// <summary>The seed of light number <paramref name="index"/> on a prop copy whose seed is <paramref name="propSeed"/> (<see cref="PropAnim.Seed"/>); the shader's flame parts use the same.</summary>
    public static float PropLightSeed(float propSeed, int index) => PropAnim.Fract(propSeed + index * 0.618034f);

    /// <summary>A light's phasor (strength x e^(i phase)) and pace (0: all the slow wave, 1: all the quick one). <see cref="Glsl"/>'s FlameParams.</summary>
    public static (Vector2 Z, float Mix) Params(float seed, float speed, float amount)
    {
        var phase = MathF.Tau * seed;
        var strength = Swing * amount * (0.7f + 0.6f * PropAnim.Fract(seed * 13.71f + 0.29f));
        var mix = Math.Clamp(0.35f + 0.35f * MathF.Log2(MathF.Max(speed, 1e-3f)) + 0.3f * (PropAnim.Fract(seed * 5.17f + 0.61f) - 0.5f), 0f, 1f);
        return (new Vector2(MathF.Cos(phase), MathF.Sin(phase)) * strength, mix);
    }

    /// <summary>The flicker at shader time <paramref name="t"/> for a phasor and pace (a light's own, or a cell's blend): a share of the light, around 0. <see cref="Glsl"/>'s FlameWave.</summary>
    public static float Wave(Vector2 z, float mix, float t)
    {
        var r = z.Length();
        var z2 = new Vector2(z.X * z.X - z.Y * z.Y, 2f * z.X * z.Y) / MathF.Max(r, 1e-5f);
        var a = MathF.Tau * PropAnim.Fract(t * Cycles1);
        var b = MathF.Tau * PropAnim.Fract(t * Cycles2);
        var slow = z.X * MathF.Cos(a) - z.Y * MathF.Sin(a);
        var quick = z2.X * MathF.Cos(b) - z2.Y * MathF.Sin(b);
        return slow + (quick - slow) * mix;
    }

    /// <summary>A phasor component as the bake stores it (a byte, 128 = 0).</summary>
    public static byte ZByte(float v) => (byte)Math.Clamp(MathF.Round(128f + v / ZRange * 127f), 1f, 255f);

    /// <summary>The phasor component a stored (and filtered, 0..1) value stands for.</summary>
    public static float ZOf(float stored) => (stored * 255f - 128f) / 127f * ZRange;

    /// <summary>The GLSL twins of <see cref="Params"/> and <see cref="Wave"/>, included by the scene and prop shaders.</summary>
    public const string Glsl = """

        // ---- Light flicker (Source/Maps/LightFlicker.cs: keep the two in step) ----
        vec3 FlameParams(float seed, float speed, float amount)
        {
            float phase = 6.2831853 * seed;
            float strength = 0.15 * amount * (0.7 + 0.6 * fract(seed * 13.71 + 0.29));
            float m = clamp(0.35 + 0.35 * log2(max(speed, 1e-3)) + 0.3 * (fract(seed * 5.17 + 0.61) - 0.5), 0.0, 1.0);
            return vec3(cos(phase) * strength, sin(phase) * strength, m);
        }

        float FlameWave(vec2 z, float m, float t)
        {
            float r = length(z);
            vec2 z2 = vec2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) / max(r, 1e-5);
            float a = 6.2831853 * fract(t * 0.5);
            float b = 6.2831853 * fract(t * 1.300048828125);
            float slow = z.x * cos(a) - z.y * sin(a);
            float quick = z2.x * cos(b) - z2.y * sin(b);
            return mix(slow, quick, m);
        }

        """;
}
