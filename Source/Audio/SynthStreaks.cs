namespace CallOfSlop;

/// <summary>Sounds of the kill streaks added later, synthesised like every other effect (no samples). Deterministic per variant.</summary>
public static partial class Synth
{
    /// <summary>A later streak's effect, variant <paramref name="v"/>; null for an effect this file doesn't make.</summary>
    private static short[]? StreakFoley(Sfx sfx, int v)
    {
        var rng = new Random(9700 + (int)sfx * 211 + v * 7919);
        Buf b;
        switch (sfx)
        {
            case Sfx.EmpBurst:
                // A deep electric thump, a falling buzz as everything shorts out, and a crackle tail.
                b = new Buf(1.6f);
                Tone(b, 0, 0.9f, 120, 40, 0.4f, 0.002f, 0.35f);
                Band(b, rng, 0, 0.5f, 0.005f, 0.6f, 300, 2400, tremolo: 50);
                Tone(b, 0.02f, 0.35f, 1800, 120, 0.9f, 0.01f, 0.6f, harmonics: 0.6f);
                for (var i = 0; i < 12; i++)
                    Click(b, rng, 0.1f + (float)rng.NextDouble() * 1.2f, 0.25f, 1.4f + (float)rng.NextDouble());
                Echo(b, 0.3f, 1.1f);
                return Finish(b, 0.8f);
            case Sfx.HunterBeep:
                // A short, sharp seeker chirp: it's a warning, so it cuts through.
                b = new Buf(0.1f);
                Tone(b, 0, 0.7f, 3100, 3300, 0.05f, 0.001f, 0.03f, harmonics: 0.3f, length: 0.06f);
                return Finish(b, 0.5f);
            case Sfx.GunshipCannon:
                // A heavy, slower boom than a rifle: crack, chest thump and a rolling echo from up high.
                b = new Buf(1.2f);
                Noise(b, rng, 0, 1f, 0.0005f, 0.04f, 3500);
                Tone(b, 0, 1f, 140, 45, 0.08f, 0.001f, 0.18f);
                Noise(b, rng, 0.03f, 0.4f, 0.03f, 0.5f, 600, length: 1f);
                Echo(b, 0.45f, 1.5f);
                return Finish(b, 0.85f);
            case Sfx.NapalmIgnite:
                // A soft whump as it lights, then a roaring rush of flame.
                b = new Buf(1.8f);
                Tone(b, 0, 0.8f, 90, 50, 0.2f, 0.01f, 0.2f);
                Noise(b, rng, 0, 0.8f, 0.02f, 0.3f, 1200);
                Band(b, rng, 0.05f, 0.5f, 0.1f, 0.9f, 200, 1800, tremolo: 9, length: 1.7f);
                Noise(b, rng, 0.1f, 0.25f, 0.2f, 0.8f, 5000, highpass: true, length: 1.6f);
                return Finish(b, 0.75f);
            case Sfx.CrateLand:
                // A heavy wooden box thumping down and settling.
                b = new Buf(0.6f);
                Tone(b, 0, 1f, 110, 60, 0.06f, 0.001f, 0.12f);
                Clunk(b, rng, 0, 0.8f, 0.7f);
                Noise(b, rng, 0, 0.5f, 0.002f, 0.08f, 900);
                Click(b, rng, 0.12f, 0.3f, 0.8f);
                return Finish(b, 0.6f);
        }
        return null;
    }
}
