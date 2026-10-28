namespace CallOfSlop;

/// <summary>
/// The zombies' own sounds (docs/zombies/ZOMBIES-MODE.md, "Sound"), synthesised like every other effect and played by
/// <see cref="ZombieSounds"/>: a throat model (a buzzing glottal pulse with jitter, a period-doubling growl, a wet
/// gurgle and breath noise) through three formant resonators gliding between vowels, then a little drive. Moans, growls,
/// the swipe's snarl, hurt grunts, the headshot screech, death rattles; and shuffling, running and sprinting steps.
/// Every variant differs in its pitch contour, vowels, length and roughness. Deterministic per variant.
/// </summary>
public static partial class Synth
{
    /// <summary>
    /// The longest a zombie sound lasts (seconds at pitch 1): every variant of it fits in a buffer this long, so the
    /// voice pool knows how long a voice is busy without asking OpenAL.
    /// </summary>
    public static float ZombieSeconds(Sfx sfx) => sfx switch
    {
        Sfx.ZombieGroan => 2.3f,
        Sfx.ZombieGrowl => 1.25f,
        Sfx.ZombieAttack => 0.7f,
        Sfx.ZombieHurt => 0.42f,
        Sfx.ZombieHeadshot => 0.75f,
        Sfx.ZombieDeath => 1.5f,
        Sfx.ZombieHeadDeath => 0.9f,
        Sfx.ZombieShuffle => 0.34f,
        Sfx.ZombieRun => 0.22f,
        Sfx.ZombieSprint => 0.26f,
        _ => 0f,
    };

    /// <summary>Variants of each zombie sound (picked per zombie so a horde doesn't sound cloned).</summary>
    private static int ZombieVariants(Sfx sfx) => sfx switch
    {
        Sfx.ZombieGroan => 6,
        Sfx.ZombieGrowl => 5,
        Sfx.ZombieAttack or Sfx.ZombieHurt => 4,
        Sfx.ZombieHeadshot or Sfx.ZombieDeath or Sfx.ZombieHeadDeath => 3,
        Sfx.ZombieShuffle or Sfx.ZombieRun or Sfx.ZombieSprint => 4,
        _ => 0,
    };

    public static bool IsZombieSound(Sfx sfx) => sfx is >= Sfx.ZombieGroan and <= Sfx.ZombieSprint;

    public static bool IsZombieStep(Sfx sfx) => sfx is Sfx.ZombieShuffle or Sfx.ZombieRun or Sfx.ZombieSprint;

    // Formants (F1, F2, F3 in Hz), a little lower than a person's (a bigger, slacker throat).
    private static readonly float[] ZA = [650, 1050, 2350], ZO = [460, 780, 2300], ZU = [330, 640, 2200], ZE = [470, 1600, 2350], ZI = [300, 1950, 2700], ZAe = [600, 1450, 2400];

    /// <summary>One throat sound's recipe (see <see cref="Throat"/>).</summary>
    private struct ThroatParams
    {
        public float Start, Length, Gain;

        /// <summary>Pitch at the start, the peak (at <see cref="PeakAt"/> of the way) and the end (Hz).</summary>
        public float F0, FPeak, FEnd, PeakAt;

        /// <summary>Vowels at the start and the end.</summary>
        public float[] VowelA, VowelB;

        /// <summary>Period doubling (the growl, 0-1), random pitch jitter (0-1), breath noise (0-1), wet gurgle depth and rate.</summary>
        public float Sub, Jitter, Breath, Gurgle, GurgleHz;

        /// <summary>Attack and release (seconds), slow vibrato depth (fraction) and rate.</summary>
        public float Attack, Release, Vibrato, VibratoHz;

        /// <summary>How far the formants open towards the middle (a moan swelling) and the drive (grit).</summary>
        public float Open, Drive;

        /// <summary>The upper formants' level over the first's (0 = 1: a shriek has them loud).</summary>
        public float Bright;
    }

    /// <summary>
    /// A throat sound into <paramref name="b"/>: a glottal pulse (a saw softened into a pulse) whose pitch follows a
    /// start-peak-end contour with jitter and vibrato, every other period damped for the growl (<see cref="ThroatParams.Sub"/>),
    /// a fast amplitude wobble for the gurgle, breath noise, three formant band-passes gliding from one vowel to the
    /// other, an envelope and a soft drive.
    /// </summary>
    private static void Throat(Buf b, Random rng, in ThroatParams p)
    {
        var i0 = (int)(p.Start * Sr);
        var n = Math.Min(b.Len - i0, (int)(p.Length * Sr));
        if (n <= 0)
            return;
        double phase = 0;
        float jit = 0, jitTarget = 0, gurgleNoise = 0;
        // Chamberlin state-variable band-passes, one per formant.
        Span<float> low = stackalloc float[3], band = stackalloc float[3], fc = stackalloc float[3], q = stackalloc float[3];
        q[0] = 1f / 6f;
        q[1] = 1f / 8f;
        q[2] = 1f / 10f;
        var hi = p.Bright > 0f ? p.Bright : 1f;
        ReadOnlySpan<float> fg = [1f / MathF.Sqrt(hi), 0.65f * hi, 0.3f * hi];
        var period = 0;
        for (var k = 0; k < n; k++)
        {
            var t = k / (float)Sr;
            var u = t / p.Length;
            if ((k & 31) == 0)
            {
                // Pitch contour, jitter, formants: every 32 samples.
                if ((k & 1023) == 0)
                    jitTarget = (float)(rng.NextDouble() * 2 - 1) * p.Jitter;
                var open = p.Open * MathF.Sin(MathF.PI * Math.Clamp(u, 0, 1));
                for (var f = 0; f < 3; f++)
                {
                    var v = p.VowelA[f] + (p.VowelB[f] - p.VowelA[f]) * Smooth(u);
                    fc[f] = 2f * MathF.Sin(MathF.PI * MathF.Min(v * (1f + open * (f == 0 ? 0.35f : 0.15f)), 6000f) / Sr);
                }
            }
            jit += (jitTarget - jit) * 0.0015f;
            var f0 = u < p.PeakAt
                ? p.F0 + (p.FPeak - p.F0) * Smooth(u / MathF.Max(p.PeakAt, 1e-3f))
                : p.FPeak + (p.FEnd - p.FPeak) * Smooth((u - p.PeakAt) / MathF.Max(1f - p.PeakAt, 1e-3f));
            f0 *= 1f + jit * 0.12f + p.Vibrato * MathF.Sin(MathF.Tau * p.VibratoHz * t);
            phase += f0 / Sr;
            if (phase >= 1)
            {
                phase -= 1;
                period++;
            }
            var ph = (float)phase;
            // A glottal pulse: a fast rise and a slower fall (the saw's edge softened), with every other period
            // weaker for the period-doubling growl.
            var pulse = ph < 0.35f ? ph / 0.35f : 1f - (ph - 0.35f) / 0.65f;
            pulse = pulse * pulse * 2f - 0.6f;
            if ((period & 1) == 1)
                pulse *= 1f - p.Sub;
            var noise = (float)(rng.NextDouble() * 2 - 1);
            gurgleNoise += (noise - gurgleNoise) * 0.02f;
            var wobble = 1f - p.Gurgle * (0.5f + 0.5f * MathF.Sin(MathF.Tau * p.GurgleHz * t + gurgleNoise * 6f));
            var src = pulse * (1f - p.Breath * 0.6f) * wobble + noise * p.Breath * (0.6f + 0.4f * wobble);
            float y = 0;
            for (var f = 0; f < 3; f++)
            {
                var high = src - low[f] - q[f] * band[f];
                band[f] += fc[f] * high;
                low[f] += fc[f] * band[f];
                y += band[f] * fg[f];
            }
            var env = Math.Clamp(t / MathF.Max(p.Attack, 1e-3f), 0f, 1f) * Math.Clamp((p.Length - t) / MathF.Max(p.Release, 1e-3f), 0f, 1f);
            y = MathF.Tanh(y * p.Drive * 0.25f) * env * p.Gain;
            b.S[i0 + k] += y;
        }
    }

    private static float Smooth(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3 - 2 * x);
    }

    /// <summary>A zombie effect, variant <paramref name="v"/>; null for an effect this file doesn't make.</summary>
    /// <summary>The crowd roar's variants (two loops, so two clusters never sound in step) and its length (seconds; it loops).</summary>
    public const int CrowdRoarVariants = 2;

    public const float CrowdRoarSeconds = 4f;

    /// <summary>
    /// The mass horde's crowd (CrowdRoar.cs): a loop of many low throats groaning over each other at random, none alike,
    /// over a deep rumble (the shuffle of thousands of feet) with a slow swell, so a dense cluster sounds like a crowd
    /// and not one loud zombie. The throats end before the loop's end, so the seam is only the rumble.
    /// </summary>
    private static short[] CrowdRoarLoop(int v)
    {
        var rng = new Random(41000 + v * 7919);
        float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
        float[] Pick(params float[][] vowels) => vowels[rng.Next(vowels.Length)];
        var b = new Buf(CrowdRoarSeconds);
        for (var k = 0; k < 28; k++)
        {
            var p = default(ThroatParams);
            p.Length = R(0.9f, 1.9f);
            p.Start = R(0f, CrowdRoarSeconds - p.Length - 0.05f);
            p.F0 = R(70, 150);
            p.FPeak = p.F0 * R(1.05f, 1.3f);
            p.FEnd = p.F0 * R(0.7f, 0.95f);
            p.PeakAt = R(0.2f, 0.55f);
            p.VowelA = Pick(ZU, ZO, ZA);
            p.VowelB = Pick(ZO, ZA, ZU, ZAe);
            p.Sub = R(0.3f, 0.65f);
            p.Jitter = R(0.4f, 0.8f);
            p.Breath = R(0.2f, 0.4f);
            p.Gurgle = R(0.1f, 0.4f);
            p.GurgleHz = R(12, 30);
            p.Attack = R(0.12f, 0.3f);
            p.Release = R(0.2f, 0.4f);
            p.Vibrato = R(0.01f, 0.04f);
            p.VibratoHz = R(3.5f, 6f);
            p.Open = R(0.2f, 0.5f);
            p.Drive = R(2.5f, 4.5f);
            p.Gain = R(0.18f, 0.3f);
            Throat(b, rng, p);
        }
        // The rumble of feet and bodies: low band noise, swelling slowly over the loop (the same at both ends).
        var sr = (float)Sr;
        var a = 1f - MathF.Exp(-2f * MathF.PI * 260f / sr);
        var al = 1f - MathF.Exp(-2f * MathF.PI * 45f / sr);
        float lp = 0, lo = 0;
        for (var i = 0; i < b.Len; i++)
        {
            var n = (float)(rng.NextDouble() * 2 - 1);
            lp += a * (n - lp);
            lo += al * (lp - lo);
            var t = i / sr;
            var swell = 0.75f + 0.25f * MathF.Sin(MathF.Tau * t / CrowdRoarSeconds);
            b.S[i] += (lp - lo) * 0.35f * swell;
        }
        return Finish(b, 0.6f);
    }

    private static short[]? ZombieFoley(Sfx sfx, int v)
    {
        if (sfx == Sfx.CrowdRoar)
            return CrowdRoarLoop(v);
        if (!IsZombieSound(sfx))
            return null;
        var rng = new Random(31000 + (int)sfx * 977 + v * 7919);
        float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
        float[] Pick(params float[][] vowels) => vowels[rng.Next(vowels.Length)];
        var b = new Buf(ZombieSeconds(sfx));
        var p = default(ThroatParams);
        switch (sfx)
        {
            case Sfx.ZombieGroan:
            {
                // A long low moan: slow swell, a sag in pitch, a slow wobble, breathy.
                p.Length = R(1.35f, 2.2f);
                p.F0 = R(82, 125);
                p.FPeak = p.F0 * R(1.05f, 1.25f);
                p.FEnd = p.F0 * R(0.72f, 0.9f);
                p.PeakAt = R(0.25f, 0.5f);
                p.VowelA = Pick(ZU, ZO);
                p.VowelB = Pick(ZO, ZA, ZU);
                p.Sub = R(0.25f, 0.55f);
                p.Jitter = R(0.3f, 0.6f);
                p.Breath = R(0.2f, 0.35f);
                p.Gurgle = R(0.05f, 0.25f);
                p.GurgleHz = R(9, 16);
                p.Attack = R(0.12f, 0.3f);
                p.Release = R(0.35f, 0.6f);
                p.Vibrato = R(0.015f, 0.04f);
                p.VibratoHz = R(3.5f, 5.5f);
                p.Open = R(0.2f, 0.5f);
                p.Drive = R(2.5f, 4f);
                p.Gain = 1f;
                Throat(b, rng, p);
                return Finish(b, 0.6f);
            }
            case Sfx.ZombieGrowl:
            {
                // A wet rattling growl: low, steady, strongly period-doubled with a fast gurgle.
                p.Length = R(0.75f, 1.2f);
                p.F0 = R(65, 95);
                p.FPeak = p.F0 * R(1.1f, 1.35f);
                p.FEnd = p.F0 * R(0.85f, 1f);
                p.PeakAt = R(0.2f, 0.4f);
                p.VowelA = Pick(ZA, ZAe);
                p.VowelB = Pick(ZO, ZA);
                p.Sub = R(0.6f, 0.85f);
                p.Jitter = R(0.5f, 0.9f);
                p.Breath = R(0.25f, 0.4f);
                p.Gurgle = R(0.35f, 0.6f);
                p.GurgleHz = R(24, 38);
                p.Attack = R(0.04f, 0.1f);
                p.Release = R(0.2f, 0.35f);
                p.Vibrato = 0.01f;
                p.VibratoHz = 6f;
                p.Open = R(0.1f, 0.3f);
                p.Drive = R(4f, 6f);
                p.Gain = 1f;
                Throat(b, rng, p);
                return Finish(b, 0.65f);
            }
            case Sfx.ZombieAttack:
            {
                // The swipe's snarl: a sharp hissing intake, then a rising open-mouthed roar that breaks off.
                Band(b, rng, 0, R(0.35f, 0.5f), 0.03f, 0.05f, 1800, 6000, length: 0.12f);
                p.Start = R(0.06f, 0.09f);
                p.Length = R(0.42f, 0.58f);
                p.F0 = R(130, 170);
                p.FPeak = p.F0 * R(1.5f, 1.9f);
                p.FEnd = p.F0 * R(1f, 1.2f);
                p.PeakAt = R(0.3f, 0.5f);
                p.VowelA = ZA;
                p.VowelB = Pick(ZAe, ZE);
                p.Sub = R(0.35f, 0.55f);
                p.Jitter = R(0.6f, 1f);
                p.Breath = R(0.4f, 0.55f);
                p.Gurgle = R(0.15f, 0.3f);
                p.GurgleHz = R(30, 45);
                p.Attack = 0.02f;
                p.Release = R(0.08f, 0.14f);
                p.Vibrato = 0.02f;
                p.VibratoHz = 9f;
                p.Open = R(0.4f, 0.7f);
                p.Drive = R(5f, 7f);
                p.Gain = 1f;
                Throat(b, rng, p);
                return Finish(b, 0.75f);
            }
            case Sfx.ZombieHurt:
            {
                // A short winded grunt: a chest thump and a falling "uhh".
                Tone(b, 0, R(0.35f, 0.5f), R(120, 150), R(60, 75), 0.03f, 0.002f, 0.05f);
                p.Start = 0.005f;
                p.Length = R(0.2f, 0.36f);
                p.F0 = R(120, 160);
                p.FPeak = p.F0 * R(1.05f, 1.2f);
                p.FEnd = p.F0 * R(0.6f, 0.75f);
                p.PeakAt = R(0.1f, 0.2f);
                p.VowelA = Pick(ZU, ZO);
                p.VowelB = ZA;
                p.Sub = R(0.3f, 0.5f);
                p.Jitter = R(0.4f, 0.7f);
                p.Breath = R(0.3f, 0.45f);
                p.Gurgle = R(0.1f, 0.2f);
                p.GurgleHz = R(18, 26);
                p.Attack = 0.006f;
                p.Release = R(0.08f, 0.12f);
                p.Vibrato = 0;
                p.VibratoHz = 1;
                p.Open = 0.2f;
                p.Drive = R(4f, 5.5f);
                p.Gain = 1f;
                Throat(b, rng, p);
                return Finish(b, 0.7f);
            }
            case Sfx.ZombieHeadshot:
            {
                // A shrill screech that chokes off: high, bright vowels, very rough, then a wet catch.
                p.Length = R(0.42f, 0.6f);
                p.F0 = R(260, 320);
                p.FPeak = p.F0 * R(1.5f, 1.8f);
                p.FEnd = p.F0 * R(0.9f, 1.1f);
                p.PeakAt = R(0.15f, 0.3f);
                p.VowelA = ZE;
                p.VowelB = ZI;
                p.Bright = R(3f, 3.6f);
                p.Sub = R(0.4f, 0.6f);
                p.Jitter = R(0.8f, 1f);
                p.Breath = R(0.35f, 0.5f);
                p.Gurgle = R(0.2f, 0.35f);
                p.GurgleHz = R(40, 55);
                p.Attack = 0.008f;
                p.Release = R(0.05f, 0.09f);
                p.Vibrato = 0.03f;
                p.VibratoHz = 11f;
                p.Open = R(0.3f, 0.5f);
                p.Drive = R(5f, 7f);
                p.Gain = 1f;
                Throat(b, rng, p);
                // The rasp: a torn, hissing edge over the whole screech (what makes it shrill).
                Band(b, rng, 0.01f, R(0.8f, 1f), 0.02f, p.Length * 0.45f, 2400, 7500, tremolo: p.GurgleHz, length: p.Length);
                Noise(b, rng, 0, R(0.3f, 0.4f), 0.0005f, 0.012f, 5000, highpass: true); // the crack it starts on
                Band(b, rng, p.Length * 0.9f, R(0.2f, 0.3f), 0.01f, 0.05f, 200, 900, tremolo: R(30, 40), length: 0.15f); // the catch
                return Finish(b, 0.75f);
            }
            case Sfx.ZombieDeath:
            {
                // A long falling death rattle: open vowel closing down, the rattle speeding up, a last exhale.
                p.Length = R(0.95f, 1.3f);
                p.F0 = R(110, 140);
                p.FPeak = p.F0 * R(1.15f, 1.35f);
                p.FEnd = R(45, 58);
                p.PeakAt = R(0.08f, 0.18f);
                p.VowelA = Pick(ZA, ZAe);
                p.VowelB = ZU;
                p.Sub = R(0.5f, 0.75f);
                p.Jitter = R(0.6f, 0.9f);
                p.Breath = R(0.3f, 0.45f);
                p.Gurgle = R(0.35f, 0.55f);
                p.GurgleHz = R(14, 22);
                p.Attack = 0.02f;
                p.Release = R(0.3f, 0.45f);
                p.Vibrato = 0.02f;
                p.VibratoHz = 5f;
                p.Open = R(0.2f, 0.4f);
                p.Drive = R(3.5f, 5f);
                p.Gain = 1f;
                Throat(b, rng, p);
                Band(b, rng, p.Length * 0.8f, R(0.2f, 0.3f), 0.08f, 0.12f, 400, 2500, length: 0.4f); // the exhale
                return Finish(b, 0.7f);
            }
            case Sfx.ZombieHeadDeath:
            {
                // Cut off mid-screech: a short shriek collapsing into a wet gurgle.
                p.Length = R(0.5f, 0.68f);
                p.F0 = R(230, 290);
                p.FPeak = p.F0 * R(1.2f, 1.4f);
                p.FEnd = R(55, 70);
                p.PeakAt = R(0.08f, 0.15f);
                p.VowelA = ZE;
                p.VowelB = ZU;
                p.Bright = 2f;
                p.Sub = R(0.55f, 0.8f);
                p.Jitter = R(0.8f, 1f);
                p.Breath = R(0.35f, 0.5f);
                p.Gurgle = R(0.5f, 0.7f);
                p.GurgleHz = R(28, 40);
                p.Attack = 0.005f;
                p.Release = R(0.15f, 0.25f);
                p.Vibrato = 0;
                p.VibratoHz = 1;
                p.Open = 0.3f;
                p.Drive = R(5f, 6.5f);
                p.Gain = 1f;
                Throat(b, rng, p);
                Tone(b, 0, R(0.3f, 0.45f), R(160, 190), R(55, 70), 0.04f, 0.002f, 0.06f); // the knock
                return Finish(b, 0.72f);
            }
            case Sfx.ZombieShuffle:
            {
                // A dragged foot: a soft scuff swelling and fading (grit), a dull heel touch.
                var at = R(0.02f, 0.05f);
                Band(b, rng, 0, R(0.5f, 0.7f), at, R(0.05f, 0.08f), R(250, 400), R(1800, 2800), tremolo: R(14, 24), length: 0.3f);
                Noise(b, rng, R(0.1f, 0.16f), R(0.25f, 0.4f), 0.004f, 0.025f, R(500, 800));
                Tone(b, R(0.1f, 0.16f), R(0.2f, 0.35f), R(95, 120), R(55, 65), 0.03f, 0.002f, 0.04f);
                return Finish(b, 0.5f);
            }
            case Sfx.ZombieRun:
            {
                // A heavy bare-ish footfall: thud and a short scuff.
                Tone(b, 0, R(0.7f, 0.9f), R(105, 130), R(50, 60), 0.025f, 0.001f, 0.045f);
                Noise(b, rng, 0, R(0.45f, 0.6f), 0.001f, 0.025f, R(700, 1100));
                Band(b, rng, 0.012f, R(0.2f, 0.3f), 0.005f, 0.03f, 900, 3500, length: 0.1f);
                return Finish(b, 0.6f);
            }
            default: // Sfx.ZombieSprint
            {
                // A stamping slap: a bright slap on top of a deep thud and a skid.
                Noise(b, rng, 0, R(0.5f, 0.7f), 0.0004f, 0.008f, R(2500, 3500), highpass: true);
                Tone(b, 0.002f, R(0.8f, 1f), R(120, 145), R(45, 55), 0.03f, 0.001f, 0.05f);
                Noise(b, rng, 0.002f, R(0.5f, 0.65f), 0.001f, 0.03f, R(800, 1200));
                Band(b, rng, 0.02f, R(0.2f, 0.3f), 0.01f, 0.04f, 600, 2600, tremolo: R(20, 30), length: 0.15f);
                return Finish(b, 0.68f);
            }
        }
    }
}
