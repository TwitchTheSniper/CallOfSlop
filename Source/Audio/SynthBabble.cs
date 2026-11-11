namespace CallOfSlop;

/// <summary>
/// The story people's murmur (Game/GameStoryBabble.cs): short sung syllables, mostly a nasal, open "a" ("ma", "nga",
/// "ga", "ba"...), made with a buzzing voice source through vowel resonances. Each person plays them at their own pitch
/// and pace, so one small set of syllables gives every voice.
/// </summary>
public static partial class Synth
{
    /// <summary>How many syllables there are (the variants of <see cref="Sfx.Babble"/>).</summary>
    public const int BabbleVariants = 10;

    /// <summary>The voice's base note (Hz) at pitch 1: each person plays it higher or lower.</summary>
    public const float BabbleF0 = 175f;

    // Per syllable: the onset (m, n, ng, g, b, w, d, y), and the vowel's first three resonances (Hz).
    private enum Onset : byte { M, N, Ng, G, B, W, D, Y }

    private static readonly (Onset On, float F1, float F2, float F3, float Len)[] Syllables =
    [
        (Onset.M, 760, 1250, 2550, 0.13f), // ma
        (Onset.Ng, 740, 1230, 2500, 0.13f), // nga (the nasal "gagaga")
        (Onset.G, 760, 1280, 2550, 0.12f), // ga
        (Onset.N, 720, 1300, 2600, 0.12f), // na
        (Onset.B, 780, 1220, 2500, 0.12f), // ba
        (Onset.M, 560, 960, 2450, 0.14f), // mo
        (Onset.W, 700, 1150, 2500, 0.13f), // wa
        (Onset.N, 520, 1750, 2600, 0.12f), // ne
        (Onset.D, 740, 1300, 2600, 0.11f), // da
        (Onset.Y, 680, 1500, 2600, 0.13f), // ya
    ];

    /// <summary>A syllable, or null for an effect this file doesn't make.</summary>
    private static short[]? BabbleFoley(Sfx sfx, int variant)
    {
        if (sfx != Sfx.Babble)
            return null;
        var (on, f1, f2, f3, len) = Syllables[Math.Clamp(variant, 0, Syllables.Length - 1)];
        var b = new Buf(len);
        var rng = new Random(9100 + variant);
        var nasalOnset = on is Onset.M or Onset.N or Onset.Ng;
        var onsetLen = on switch { Onset.M or Onset.N => 0.035f, Onset.Ng => 0.03f, Onset.W or Onset.Y => 0.045f, _ => 0.012f };
        // Resonators (two-pole band-pass) for three formants and the nasal murmur.
        Span<float> y1 = stackalloc float[4], y2 = stackalloc float[4];
        double ph = 0;
        for (var i = 0; i < b.Len; i++)
        {
            var t = i / (float)Sr;
            var u = t / len;
            // The note: up a little into the vowel, then falling off at the end (sung, not spoken flat).
            var f0 = BabbleF0 * (1f + 0.05f * MathF.Sin(MathF.PI * MathF.Min(1f, u * 1.6f)) - 0.07f * MathF.Max(0f, u - 0.6f));
            f0 *= 1f + 0.004f * MathF.Sin(t * MathF.Tau * 5.5f); // a touch of vibrato
            ph += f0 / Sr;
            var phase = (float)(ph % 1.0);
            // The voice source: a buzzy pulse (a saw with its edge rounded) for the harmonics an "a" needs.
            var src = 1f - 2f * phase;
            src = src * MathF.Abs(src) * 0.6f + src * 0.4f;
            src += ((float)rng.NextDouble() - 0.5f) * 0.04f; // breath

            // Into the vowel: the formants glide from the onset's own place.
            var k = Math.Clamp((t - onsetLen * 0.6f) / (onsetLen + 0.02f), 0f, 1f);
            k = k * k * (3f - 2f * k);
            var (s1, s2) = on switch
            {
                Onset.M or Onset.B => (350f, 900f),
                Onset.N or Onset.D => (380f, 1650f),
                Onset.Ng or Onset.G => (360f, 1900f),
                Onset.W => (320f, 680f),
                _ => (300f, 2200f), // y
            };
            var cf1 = s1 + (f1 - s1) * k;
            var cf2 = s2 + (f2 - s2) * k;
            // Mouth open (vowel) and nose (murmur) levels: a nasal onset is all nose, then the "a" stays a little nasal.
            var nose = nasalOnset ? 1f - 0.65f * k : 0.3f;
            var mouth = nasalOnset ? k : on is Onset.G or Onset.B or Onset.D ? Math.Clamp((t - onsetLen) / 0.008f, 0f, 1f) : 0.35f + 0.65f * k;

            var v = mouth * (Reson(src, cf1, 90f, 1.6f, ref y1[0], ref y2[0])
                             + Reson(src, cf2, 110f, 2.4f, ref y1[1], ref y2[1])
                             + Reson(src, f3, 160f, 1.6f, ref y1[2], ref y2[2]))
                    + nose * Reson(src, 270f, 70f, 0.9f, ref y1[3], ref y2[3]);

            // A stop's little burst before the vowel (g a mid hiss, d a high one, b a soft puff).
            if (on is Onset.G or Onset.D or Onset.B && t < onsetLen)
            {
                var burst = ((float)rng.NextDouble() - 0.5f) * MathF.Exp(-t / 0.004f);
                v += burst * (on == Onset.B ? 0.5f : 0.9f);
            }
            // The syllable's level: a quick start, a steady middle and a soft end.
            var env = MathF.Min(1f, t / 0.008f) * MathF.Min(1f, (len - t) / 0.04f);
            b.S[i] = v * env;
        }
        return Finish(b, 0.7f);
    }

    /// <summary>One sample through a two-pole resonator at <paramref name="f"/> (Hz) with bandwidth <paramref name="bw"/>, about unit gain at its peak.</summary>
    private static float Reson(float x, float f, float bw, float gain, ref float y1, ref float y2)
    {
        var r = MathF.Exp(-MathF.PI * bw / Sr);
        var c = 2f * r * MathF.Cos(MathF.Tau * f / Sr);
        var y = (1f - r) * 2f * MathF.Sin(MathF.Tau * f / Sr) * x + c * y1 - r * r * y2;
        y2 = y1;
        y1 = y;
        return y * gain;
    }
}
