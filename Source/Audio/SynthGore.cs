namespace CallOfSlop;

/// <summary>The gore sounds (Gibs.cs), synthesised like every other effect. Deterministic per variant.</summary>
public static partial class Synth
{
    /// <summary>A gore effect, variant <paramref name="v"/>; null for an effect this file doesn't make.</summary>
    private static short[]? GoreFoley(Sfx sfx, int v)
    {
        if (sfx != Sfx.GibSquelch)
            return null;
        var rng = new Random(12900 + v * 7919);
        float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
        // A wet pop: a low thump and a bursting crack, a squelch with a fast wobble, then bits pattering down.
        var b = new Buf(0.75f);
        Tone(b, 0, 0.9f, R(110, 135), R(38, 46), 0.05f, 0.001f, 0.08f);
        Noise(b, rng, 0, 0.75f, 0.0008f, 0.035f, R(700, 900));
        Noise(b, rng, 0, 0.35f, 0.0003f, 0.004f, 3000, highpass: true);
        Band(b, rng, 0.012f, 0.65f, 0.005f, 0.07f, 350, 1500, tremolo: R(24, 36));
        Band(b, rng, 0.06f, 0.35f, 0.01f, 0.06f, 250, 900, tremolo: R(14, 20));
        for (var i = 0; i < 9; i++) // chunks landing: short soft splats
        {
            var t = 0.12f + R(0, 0.5f);
            Tone(b, t, R(0.15f, 0.3f), R(160, 230), R(70, 90), 0.02f, 0.001f, 0.02f);
            Noise(b, rng, t, R(0.12f, 0.25f), 0.001f, 0.012f, R(600, 1100));
        }
        return Finish(b, 0.65f);
    }
}
