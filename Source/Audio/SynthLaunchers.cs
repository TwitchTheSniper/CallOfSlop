namespace CallOfSlop;

/// <summary>The launchers' sounds (rocket motor, lock-on tones, flares), synthesised like every other effect. Deterministic per variant.</summary>
public static partial class Synth
{
    /// <summary>A launcher effect, variant <paramref name="v"/>; null for an effect this file doesn't make.</summary>
    private static short[]? LauncherFoley(Sfx sfx, int v)
    {
        var rng = new Random(9700 + (int)sfx * 211 + v * 7919);
        Buf b;
        switch (sfx)
        {
            case Sfx.RocketMotor:
                // A burst of the motor's roar: rough low noise with a hissing top, fading in and out so repeats overlap smoothly.
                b = new Buf(0.45f);
                Noise(b, rng, 0, 0.8f, 0.08f, 0.15f, 1100, length: 0.42f);
                Noise(b, rng, 0, 0.45f, 0.08f, 0.15f, 5500, highpass: true, length: 0.42f);
                Tone(b, 0, 0.2f, 150, 140, 1, 0.08f, 0.15f, harmonics: 0.8f, length: 0.42f);
                return Finish(b, 0.6f);
            case Sfx.LockTone:
                // The seeker growling: a short square beep, low.
                b = new Buf(0.09f);
                Tone(b, 0, 0.7f, 880, 880, 1, 0.002f, 0.02f, harmonics: 1f, length: 0.06f);
                return Finish(b, 0.4f);
            case Sfx.LockedTone:
                // Locked: a high, steady square tone (played back to back it reads as one solid tone).
                b = new Buf(0.13f);
                Tone(b, 0, 0.7f, 1760, 1760, 1, 0.004f, 0.05f, harmonics: 1f, length: 0.125f);
                return Finish(b, 0.4f);
            case Sfx.FlarePop:
                // A string of sharp pops and a hiss of burning magnesium.
                b = new Buf(1.2f);
                for (var i = 0; i < 5; i++)
                {
                    var t = i * 0.09f + (float)rng.NextDouble() * 0.03f;
                    Noise(b, rng, t, 0.8f, 0.0005f, 0.015f, 4000);
                    Tone(b, t, 0.4f, 500, 200, 0.02f, 0.001f, 0.03f);
                }
                Noise(b, rng, 0.05f, 0.35f, 0.1f, 0.5f, 7000, highpass: true, length: 1.1f);
                return Finish(b, 0.7f);
            default:
                return null;
        }
    }
}
