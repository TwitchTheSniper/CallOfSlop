namespace CallOfSlop;

/// <summary>
/// A worn robot shell's faults (Game/GameShellWear.cs; Karl, 2026-11-17: "noises to show that the various parts are failing ... without
/// being annoying"): all short, soft and well below the guns, so they read as a hint from inside the robot, never an alarm.
/// </summary>
public static partial class Synth
{
    /// <summary>A shell fault's sound, or null for an effect this file doesn't make.</summary>
    private static short[]? ShellFoley(Sfx sfx, int variant) => sfx switch
    {
        Sfx.ShellGlitch => ShellGlitch(variant),
        Sfx.ShellStatic => ShellStatic(variant),
        Sfx.ShellBrownout => ShellBrownout(),
        Sfx.ShellServoGrind => ShellServoGrind(variant),
        _ => null,
    };

    /// <summary>The processor: a stutter of a few square-ish blips at hopping pitches, a little bit-crushed, over in a quarter second.</summary>
    private static short[] ShellGlitch(int variant)
    {
        var rng = new Random(4021 + variant * 77);
        var b = new Buf(0.35f);
        var at = 0f;
        var n = 4 + variant;
        for (var k = 0; k < n; k++)
        {
            var f = 600f + 1800f * (float)rng.NextDouble();
            var len = 0.012f + 0.03f * (float)rng.NextDouble();
            Tone(b, at, 0.35f, f, f, 1, 0.0005f, len, harmonics: 0.8f, length: len * 1.4f);
            at += len + 0.006f + 0.02f * (float)rng.NextDouble();
        }
        Noise(b, rng, 0, 0.12f, 0.001f, 0.06f, 7000, highpass: true, length: at);
        // Bit-crushed: held in steps of 6 samples and 24 levels.
        for (var i = 0; i < b.Len; i++)
            b.S[i] = MathF.Round(b.S[i - i % 6] * 12f) / 12f;
        return Finish(b, 0.35f);
    }

    /// <summary>The optics: a soft wash of static with a few crackles in it, swelling and gone.</summary>
    private static short[] ShellStatic(int variant)
    {
        var rng = new Random(5113 + variant * 59);
        var len = 0.5f + 0.25f * variant;
        var b = new Buf(len + 0.1f);
        Band(b, rng, 0, 0.35f, len * 0.35f, len * 0.3f, 1500, 9000, tremolo: 13f + 4 * variant, length: len);
        for (var k = 0; k < 6 + 3 * variant; k++)
            Click(b, rng, len * (float)rng.NextDouble(), 0.15f + 0.15f * (float)rng.NextDouble(), 0.7f + 0.8f * (float)rng.NextDouble());
        return Finish(b, 0.3f);
    }

    /// <summary>The power cell: a whine falling away as the power sags, a relay's click, then the whine coming back up softly.</summary>
    private static short[] ShellBrownout()
    {
        var rng = new Random(6007);
        var b = new Buf(1.1f);
        Tone(b, 0, 0.3f, 2400, 380, 0.2f, 0.01f, 0.35f, length: 0.6f);
        Tone(b, 0, 0.12f, 4800, 760, 0.2f, 0.01f, 0.25f, length: 0.5f);
        Click(b, rng, 0.5f, 0.35f, 0.6f);
        Tone(b, 0.62f, 0.12f, 300, 2200, 0.15f, 0.2f, 0.18f, length: 0.45f);
        return Finish(b, 0.4f);
    }

    /// <summary>A worn leg actuator: a gear grinding for a moment, a buzz whose pitch catches, and a tick as it gives.</summary>
    private static short[] ShellServoGrind(int variant)
    {
        var rng = new Random(7177 + variant * 41);
        var len = 0.22f + 0.08f * variant;
        var b = new Buf(len + 0.15f);
        double ph = 0;
        var f0 = 140f + 40f * variant;
        for (var i = 0; i < (int)(len * Sr) && i < b.Len; i++)
        {
            var t = i / (float)Sr;
            var f = f0 * (1f + 0.25f * MathF.Sin(t * 45f) + 0.1f * (float)(rng.NextDouble() - 0.5));
            ph += 2 * Math.PI * f / Sr;
            var teeth = MathF.Pow(0.5f + 0.5f * (float)Math.Sin(ph * 7), 4); // (the teeth catching)
            var s = (float)Math.Sin(ph) * 0.5f + teeth * 0.6f + 0.2f * (float)(rng.NextDouble() * 2 - 1);
            var env = MathF.Min(1f, t / 0.03f) * MathF.Min(1f, (len - t) / 0.05f);
            b.S[i] += s * env * 0.6f;
        }
        Click(b, rng, len - 0.02f, 0.3f, 0.9f);
        return Finish(b, 0.4f);
    }
}
