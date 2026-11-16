namespace CallOfSlop;

/// <summary>The rust bucket's breakdowns (Game/GameSandboxBreakdowns.cs): a part about to go, and the part going.</summary>
public static partial class Synth
{
    /// <summary>A breakdown's sound, or null for an effect this file doesn't make.</summary>
    private static short[]? BreakdownFoley(Sfx sfx, int variant)
    {
        if (sfx == Sfx.HullCreak)
            return HullCreak(variant);
        if (sfx != Sfx.PartFail)
            return null;
        var rng = new Random(9431);
        var b = new Buf(2.4f);
        // The bang: a deep clunk, a burst of low noise and a ring of the casing.
        Clunk(b, rng, 0, 1f, 0.45f);
        Noise(b, rng, 0, 0.9f, 0.002f, 0.09f, 900);
        Tone(b, 0, 0.35f, 410, 380, 0.3f, 0.001f, 0.35f);
        Tone(b, 0, 0.22f, 1130, 1090, 0.3f, 0.001f, 0.25f);
        // Bits clattering down, then the hiss of what was in the line getting out, dying away.
        for (var k = 0; k < 7; k++)
            Click(b, rng, 0.12f + 0.5f * (float)rng.NextDouble(), 0.25f + 0.2f * (float)rng.NextDouble(), 0.5f + 0.5f * (float)rng.NextDouble());
        Band(b, rng, 0.05f, 0.45f, 0.08f, 0.6f, 1800, 7000, tremolo: 9f, length: 2.3f);
        Echo(b, 0.3f, 1.2f);
        return Finish(b, 0.9f);
    }

    /// <summary>
    /// A long groan of stressed metal: a low tone whose pitch wanders, made rough by stick-slip (the amplitude pulsing at a
    /// rate that drifts), the plate's few inharmonic rings over it, and a tick or two of something giving at the end.
    /// </summary>
    private static short[] HullCreak(int variant)
    {
        var rng = new Random(6211 + variant * 131);
        var len = 1.4f + 0.4f * variant;
        var b = new Buf(len + 0.6f);
        var f0 = 70f + 25f * variant;
        double ph = 0, slip = 0;
        var wander = (float)rng.NextDouble() * MathF.Tau;
        for (var i = 0; i < (int)(len * Sr) && i < b.Len; i++)
        {
            var t = i / (float)Sr;
            var u = t / len;
            var f = f0 * (1f + 0.35f * u + 0.12f * MathF.Sin(t * 2.3f + wander) + 0.05f * MathF.Sin(t * 7.1f));
            ph += 2 * Math.PI * f / Sr;
            slip += 2 * Math.PI * (18 + 30 * u + 8 * MathF.Sin(t * 3.7f + wander)) / Sr;
            var stick = MathF.Pow(0.5f + 0.5f * (float)Math.Sin(slip), 3f); // (short pulses: the plates catching and letting go)
            var s = (float)Math.Sin(ph) + 0.5f * (float)Math.Sin(ph * 2.01) + 0.3f * (float)Math.Sin(ph * 3.03) + 0.25f * (float)(rng.NextDouble() * 2 - 1);
            var env = MathF.Min(1f, t / 0.25f) * MathF.Min(1f, (len - t) / 0.2f);
            b.S[i] += s * stick * env * 0.8f;
        }
        Tone(b, 0.1f, 0.12f, 620 + 40 * variant, 600, 1f, 0.2f, 0.6f, length: len);
        Tone(b, 0.2f, 0.08f, 1370, 1350, 1f, 0.3f, 0.5f, length: len);
        Click(b, rng, len - 0.15f, 0.35f, 0.6f);
        if (variant != 1)
            Click(b, rng, len * 0.45f, 0.2f, 0.8f);
        Echo(b, 0.4f, 1.0f);
        return Finish(b, 0.7f);
    }
}
