namespace CallOfSlop;

/// <summary>
/// Hit feedback, footsteps, bullet whizzes, ricochets, impacts and distant gunshots, synthesised like the rest of the
/// game's sounds (no samples). Every call is deterministic for its arguments.
/// </summary>
public static partial class Synth
{
    /// <summary>Samples that came out NaN or infinite before <see cref="Finish"/> zeroed them (the self-test wants 0).</summary>
    public static int NonFinite;

    /// <summary>How many variations of an effect are made (picked at random on playback so repeats don't sound copied).</summary>
    public static int VariantCount(Sfx sfx) => sfx switch
    {
        Sfx.BreakWood or Sfx.BreakGlass or Sfx.BreakMetal or Sfx.BreakSoft or Sfx.GibSquelch => 3,
        Sfx.Headshot or Sfx.HitBody or Sfx.Headshot3D or Sfx.FleshHit3D or Sfx.HitMetal => 3,
        Sfx.Whizz or Sfx.Crack or Sfx.Ricochet or Sfx.Scuffle or Sfx.LandThud or Sfx.JumpPush => 3,
        Sfx.ImpactConcrete or Sfx.ImpactMetal or Sfx.ImpactWood or Sfx.ImpactDirt => 3,
        >= Sfx.ZombieGroan and <= Sfx.ZombieSprint => ZombieVariants(sfx), // SynthZombie.cs
        _ => 1,
    };

    /// <summary>Footstep variations per surface and gait.</summary>
    public const int StepVariants = 4;

    // ---- More building blocks ---------------------------------------------------------------------

    /// <summary>Band-limited noise burst (one-pole low-pass at <paramref name="hi"/> minus one at <paramref name="lo"/>), optionally tremolo'd.</summary>
    private static void Band(Buf b, Random rng, float start, float gain, float attack, float decay, float lo, float hi, float tremolo = 0, float length = 10f)
    {
        var ah = 1f - MathF.Exp(-2f * MathF.PI * hi / Sr);
        var al = 1f - MathF.Exp(-2f * MathF.PI * lo / Sr);
        float h = 0, l = 0;
        var i0 = (int)(start * Sr);
        var i1 = Math.Min(b.Len, i0 + (int)(Math.Min(length, decay * 9 + attack) * Sr));
        for (var i = Math.Max(0, i0); i < i1; i++)
        {
            var t = (i - i0) / (float)Sr;
            var n = (float)(rng.NextDouble() * 2 - 1);
            h += ah * (n - h);
            l += al * (h - l);
            var am = tremolo > 0 ? 0.55f + 0.45f * MathF.Sin(MathF.Tau * tremolo * t) : 1f;
            b.S[i] += (h - l) * gain * am * Env(t, attack, decay);
        }
    }

    /// <summary>Little grains (grit, gravel, snow crystals, falling chips): short noise ticks, denser at the start.</summary>
    private static void Grains(Buf b, Random rng, float start, float span, int count, float gain, float cutoff, bool highpass = false)
    {
        for (var k = 0; k < count; k++)
        {
            var u = (float)rng.NextDouble();
            var g = gain * (0.4f + 0.6f * (float)rng.NextDouble()) * (1f - 0.6f * u);
            Noise(b, rng, start + span * u * u, g, 0.0002f, 0.0015f + 0.002f * (float)rng.NextDouble(), cutoff * (0.7f + 0.6f * (float)rng.NextDouble()), highpass);
        }
    }

    /// <summary>The meaty part of a headshot: bone crack, wet low thump, squelch and a gritty crunch tail.</summary>
    private static void Crunch(Buf b, Random rng, float start, float gain, float bright)
    {
        float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
        // Bone crack: a tight cluster of sharp snaps plus a short inharmonic knock.
        for (var k = 0; k < 5; k++)
            Noise(b, rng, start + R(0, 0.012f), gain * R(0.4f, 0.8f), 0.0002f, R(0.0015f, 0.003f), R(3500, 7000) * bright);
        Noise(b, rng, start, gain * 0.5f, 0.0002f, 0.002f, 5000, highpass: true);
        Tone(b, start, gain * 0.35f, R(1100, 1300), R(700, 800), 0.01f, 0.0003f, 0.012f);
        Tone(b, start, gain * 0.2f, R(2300, 2600), R(1800, 2000), 0.01f, 0.0003f, 0.008f);
        // Wet low thump.
        Tone(b, start + 0.002f, gain, R(120, 140), R(42, 50), 0.035f, 0.001f, 0.09f);
        Noise(b, rng, start + 0.002f, gain * 0.7f, 0.002f, 0.05f, 380);
        // Squelch: band noise with a fast wobble.
        Band(b, rng, start + 0.01f, gain * 0.5f, 0.004f, 0.06f, 500, 1700, tremolo: R(28, 40));
        // Gritty tail.
        Grains(b, rng, start + 0.015f, 0.18f, 28, gain * 0.25f, 2600 * bright);
    }

    // ---- Effects added with the sound pass -----------------------------------------------------------

    /// <summary>The newer effects (and the headshot), variant <paramref name="v"/>. Null for an effect it doesn't make.</summary>
    private static short[]? Foley(Sfx sfx, int v)
    {
        var rng = new Random(4400 + (int)sfx * 131 + v * 7919);
        float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
        Buf b;
        switch (sfx)
        {
            case Sfx.Headshot:
                // Shooter feedback: a bright hitmarker ping on top of the crunch.
                b = new Buf(0.45f);
                Tone(b, 0, 0.4f, 2700, 2700, 1, 0.0005f, 0.035f);
                Tone(b, 0, 0.22f, 4050, 4050, 1, 0.0005f, 0.025f);
                Noise(b, rng, 0, 0.2f, 0.0003f, 0.003f, 6000, highpass: true);
                Crunch(b, rng, 0.004f, 1f, 1f);
                return Finish(b, 0.85f);
            case Sfx.Headshot3D:
                b = new Buf(0.45f);
                Crunch(b, rng, 0, 1f, 0.75f);
                Echo(b, 0.15f, 0.6f);
                return Finish(b, 0.8f);
            case Sfx.HitBody:
                // Shooter feedback for a body hit: a dull flesh thwack and a short tick.
                b = new Buf(0.2f);
                Tone(b, 0, 0.45f, 1900, 1750, 0.02f, 0.0005f, 0.01f);
                Tone(b, 0.001f, 0.8f, R(160, 185), R(70, 80), 0.03f, 0.001f, 0.045f);
                Noise(b, rng, 0.001f, 0.6f, 0.001f, 0.025f, R(1000, 1300));
                Noise(b, rng, 0.001f, 0.3f, 0.0003f, 0.004f, 1500, highpass: true);
                return Finish(b, 0.6f);
            case Sfx.FleshHit3D:
                b = new Buf(0.2f);
                Tone(b, 0, 0.9f, R(150, 175), R(65, 75), 0.03f, 0.001f, 0.05f);
                Noise(b, rng, 0, 0.6f, 0.001f, 0.03f, R(800, 1100));
                Noise(b, rng, 0, 0.25f, 0.0003f, 0.004f, 1500, highpass: true);
                return Finish(b, 0.6f);
            case Sfx.HitMetal:
                // Hitting a turret or drone: a metal ping.
                b = new Buf(0.3f);
                Click(b, rng, 0, 0.5f, 1.1f);
                Tone(b, 0, 0.5f, R(1550, 1750), R(1500, 1700), 1, 0.0005f, 0.07f);
                Tone(b, 0, 0.3f, R(2400, 2600), R(2380, 2580), 1, 0.0005f, 0.05f);
                Tone(b, 0, 0.15f, R(3800, 4100), R(3800, 4100), 1, 0.0005f, 0.03f);
                return Finish(b, 0.55f);
            case Sfx.KillHeadshot:
                // Extra accent on a headshot kill: a high bell, a sub boom and some shimmer.
                b = new Buf(0.7f);
                Tone(b, 0.03f, 0.5f, 1760, 1760, 1, 0.002f, 0.18f, harmonics: 0.2f);
                Tone(b, 0.03f, 0.35f, 2640, 2640, 1, 0.002f, 0.14f);
                Tone(b, 0, 0.8f, 75, 34, 0.08f, 0.002f, 0.2f);
                Noise(b, rng, 0.03f, 0.15f, 0.005f, 0.08f, 6000, highpass: true);
                Echo(b, 0.25f, 0.9f);
                return Finish(b, 0.6f);
            case Sfx.Whizz:
                // A bullet passing close by (subsonic: no crack).
                b = new Buf(0.35f);
                Band(b, rng, 0, 0.8f, R(0.035f, 0.06f), R(0.05f, 0.07f), 700, R(2600, 3400));
                Tone(b, 0, 0.25f, R(1300, 1600), R(550, 650), 0.12f, 0.04f, 0.07f);
                return Finish(b, 0.6f);
            case Sfx.Crack:
                // A supersonic round passing close by: a sharp snap and a short tail.
                b = new Buf(0.3f);
                Noise(b, rng, 0, 1f, 0.0002f, 0.0025f, 2500, highpass: true);
                Noise(b, rng, 0, 0.5f, 0.0003f, 0.006f, 6000);
                Band(b, rng, 0.004f, 0.35f, 0.02f, 0.05f, 900, 3500);
                return Finish(b, 0.75f);
            case Sfx.Ricochet:
                b = new Buf(0.5f);
                Click(b, rng, 0, 0.6f, R(0.9f, 1.3f));
                Tone(b, 0.005f, 0.6f, R(3000, 4200), R(700, 1100), R(0.1f, 0.18f), 0.003f, R(0.12f, 0.2f), harmonics: 0.25f);
                Noise(b, rng, 0, 0.2f, 0.0005f, 0.04f, 3000, highpass: true);
                Echo(b, 0.2f, 0.7f);
                return Finish(b, 0.6f);
            case Sfx.ImpactConcrete:
                b = new Buf(0.25f);
                Noise(b, rng, 0, 0.9f, 0.0003f, R(0.005f, 0.008f), 5000);
                Tone(b, 0, 0.4f, R(200, 240), 120, 0.02f, 0.001f, 0.02f);
                Grains(b, rng, 0.01f, 0.12f, 10, 0.12f, 3000, highpass: true);
                return Finish(b, 0.55f);
            case Sfx.ImpactMetal:
                b = new Buf(0.3f);
                Click(b, rng, 0, 0.6f, R(0.8f, 1.2f));
                var f = R(1500, 2200);
                Tone(b, 0, 0.4f, f, f * 0.99f, 1, 0.0005f, 0.09f);
                Tone(b, 0, 0.2f, f * 2.7f, f * 2.68f, 1, 0.0005f, 0.05f);
                Noise(b, rng, 0, 0.5f, 0.0003f, 0.004f, 4000);
                return Finish(b, 0.55f);
            case Sfx.ImpactWood:
                b = new Buf(0.2f);
                Tone(b, 0, 0.7f, R(260, 320), 180, 0.02f, 0.001f, 0.03f);
                Noise(b, rng, 0, 0.6f, 0.0005f, 0.012f, 1800);
                Grains(b, rng, 0.005f, 0.06f, 4, 0.1f, 2500);
                return Finish(b, 0.55f);
            case Sfx.ImpactDirt:
                b = new Buf(0.3f);
                Noise(b, rng, 0, 0.8f, 0.002f, 0.04f, 900);
                Tone(b, 0, 0.4f, 90, 50, 0.02f, 0.001f, 0.03f);
                Grains(b, rng, 0.01f, 0.2f, 16, 0.08f, 2500);
                return Finish(b, 0.5f);
            case Sfx.Scuffle:
                // Prone crawl: cloth dragging over the ground, a bit of grit and kit.
                b = new Buf(0.4f);
                Noise(b, rng, 0, 0.6f, R(0.05f, 0.08f), 0.1f, 800, length: 0.35f);
                Noise(b, rng, 0.02f, 0.15f, 0.05f, 0.08f, 2500, highpass: true, length: 0.3f);
                Grains(b, rng, 0.03f, 0.25f, 12, 0.08f, 2500);
                Click(b, rng, R(0.08f, 0.16f), 0.1f, R(0.7f, 0.9f));
                return Finish(b, 0.45f);
            case Sfx.LandThud:
                // Body weight hitting the ground plus the kit rattling.
                b = new Buf(0.5f);
                Tone(b, 0, 1f, R(80, 90), 38, 0.05f, 0.002f, 0.12f);
                Noise(b, rng, 0, 0.7f, 0.002f, 0.06f, 450);
                Noise(b, rng, 0, 0.4f, 0.0005f, 0.012f, 1800);
                for (var k = 0; k < 4; k++)
                    Click(b, rng, R(0.02f, 0.12f), 0.12f, R(0.7f, 1.1f));
                return Finish(b, 0.75f);
            case Sfx.JumpPush:
                b = new Buf(0.25f);
                Noise(b, rng, 0, 0.4f, 0.0005f, 0.025f, 1800, highpass: true);
                Noise(b, rng, 0, 0.4f, 0.04f, 0.05f, 900);
                Tone(b, 0, 0.3f, 120, 80, 0.02f, 0.001f, 0.02f);
                Click(b, rng, R(0.04f, 0.07f), 0.08f, 0.9f);
                return Finish(b, 0.45f);
            case Sfx.ShieldBlock:
                // Last Stand's shield taking a bullet: a falling energy zap, a soft low thunk and a glassy, wobbling ring
                // (a fifth apart, slightly detuned), so it can't be mistaken for a flesh hit or a turret ping.
                b = new Buf(0.75f);
                Tone(b, 0, 0.55f, 3200, 700, 0.09f, 0.001f, 0.06f, harmonics: 0.4f);
                Tone(b, 0, 0.6f, 260, 150, 0.06f, 0.002f, 0.07f);
                Noise(b, rng, 0, 0.3f, 0.0005f, 0.02f, 5500, highpass: true);
                Band(b, rng, 0.01f, 0.25f, 0.005f, 0.12f, 1800, 4200, tremolo: 22);
                Tone(b, 0.015f, 0.4f, 990, 990, 1, 0.003f, 0.22f, harmonics: 0.25f);
                Tone(b, 0.015f, 0.3f, 1487, 1487, 1, 0.003f, 0.18f);
                Tone(b, 0.015f, 0.18f, 1003, 1003, 1, 0.003f, 0.2f);
                Echo(b, 0.2f, 0.6f);
                return Finish(b, 0.6f);
            case Sfx.FriendlyHit:
                // Hitting a teammate (no damage): a dull, muffled wooden "bonk" (low, rounded, no flesh noise or bright tick)
                // and a soft falling two-note beep, so it reads as "wrong target", never as the enemy hit or the headshot crunch.
                b = new Buf(0.32f);
                Tone(b, 0, 0.7f, 330, 220, 0.04f, 0.002f, 0.05f, harmonics: 0.15f);
                Tone(b, 0, 0.35f, 110, 90, 0.05f, 0.003f, 0.06f);
                Noise(b, rng, 0, 0.25f, 0.002f, 0.015f, 500);
                Tone(b, 0.06f, 0.22f, 520, 520, 1, 0.004f, 0.035f);
                Tone(b, 0.14f, 0.2f, 390, 390, 1, 0.004f, 0.045f);
                return Finish(b, 0.45f);
        }
        return null;
    }

    // ---- Footsteps ------------------------------------------------------------------------------

    /// <summary>One footstep on a surface: heel strike then toe roll (closer together and heavier when running).</summary>
    public static short[] Footstep(FootSurface s, bool run, int variant)
    {
        var rng = new Random(31000 + (int)s * 977 + (run ? 50000 : 0) + variant * 7919);
        float R(float a, float c) => a + (float)rng.NextDouble() * (c - a);
        var b = new Buf(run ? 0.3f : 0.24f);
        var w = run ? 1.6f : 1f; // weight
        var heel = 0.003f;
        var toe = heel + (run ? R(0.012f, 0.02f) : R(0.03f, 0.05f));
        switch (s)
        {
            case FootSurface.Concrete:
                Tone(b, heel, 0.5f * w, R(120, 150), 70, 0.02f, 0.001f, 0.025f);
                Noise(b, rng, heel, 0.6f, 0.0008f, R(0.008f, 0.012f), R(2500, 3500));
                Noise(b, rng, heel, 0.25f, 0.0005f, 0.004f, 5000, highpass: true);
                Noise(b, rng, toe, 0.35f, 0.002f, R(0.01f, 0.016f), R(1800, 2600));
                Noise(b, rng, toe, 0.12f, 0.01f, 0.03f, 4000, highpass: true);
                break;
            case FootSurface.Metal:
                Tone(b, heel, 0.45f * w, R(105, 125), 60, 0.02f, 0.001f, 0.03f);
                Noise(b, rng, heel, 0.4f, 0.0005f, 0.006f, 4000);
                foreach (var (freq, g, d) in new[] { (R(480, 560), 0.3f, R(0.07f, 0.1f)), (R(1250, 1400), 0.2f, R(0.05f, 0.08f)), (R(2100, 2400), 0.12f, 0.04f) })
                    Tone(b, heel, g * (run ? 1.3f : 1f), freq, freq * 0.995f, 1, 0.0005f, d);
                Noise(b, rng, toe, 0.25f, 0.0005f, 0.005f, 3500);
                Tone(b, toe, 0.12f, R(600, 700), R(600, 700), 1, 0.0005f, 0.05f);
                break;
            case FootSurface.Wood:
                Tone(b, heel, 0.7f * w, R(170, 210), R(90, 110), 0.015f, 0.001f, 0.04f);
                var body = R(300, 360);
                Tone(b, heel, 0.25f, body, body, 1, 0.001f, 0.05f);
                Noise(b, rng, heel, 0.4f, 0.0008f, 0.015f, 1500);
                Tone(b, toe, 0.3f, R(210, 240), R(150, 170), 0.015f, 0.001f, 0.03f);
                Noise(b, rng, toe, 0.2f, 0.001f, 0.012f, 1800);
                break;
            case FootSurface.Grass:
                Noise(b, rng, heel, 0.5f, 0.008f, R(0.04f, 0.06f), R(2500, 3500));
                Noise(b, rng, heel, 0.25f, 0.01f, 0.03f, 3000, highpass: true);
                Grains(b, rng, heel, 0.1f, 10, 0.08f, 4500, highpass: true);
                Tone(b, heel, 0.3f * w, 90, 60, 0.02f, 0.002f, 0.03f);
                Noise(b, rng, toe, 0.3f, 0.01f, 0.04f, R(2000, 3000));
                break;
            case FootSurface.Dirt:
                Tone(b, heel, 0.45f * w, R(95, 110), 60, 0.02f, 0.001f, 0.03f);
                Noise(b, rng, heel, 0.6f, 0.001f, 0.02f, R(1400, 1800));
                Grains(b, rng, heel, 0.06f, 14, 0.15f, 3000);
                Noise(b, rng, toe, 0.4f, 0.002f, 0.025f, R(1200, 1600));
                Grains(b, rng, toe, 0.05f, 8, 0.1f, 3000);
                break;
            case FootSurface.Sand:
                Noise(b, rng, heel, 0.5f, 0.006f, 0.05f, R(2400, 3000));
                Grains(b, rng, heel, 0.1f, 20, 0.06f, 5000, highpass: true);
                Tone(b, heel, 0.3f * w, 85, 55, 0.02f, 0.002f, 0.03f);
                Noise(b, rng, toe, 0.35f, 0.01f, 0.05f, R(2000, 2600));
                break;
            case FootSurface.Rock:
                Noise(b, rng, heel, 0.7f, 0.0003f, 0.006f, R(4000, 5000));
                Tone(b, heel, 0.4f * w, R(130, 150), 80, 0.015f, 0.001f, 0.02f);
                Grains(b, rng, heel, 0.08f, 18, 0.2f, 3000, highpass: true);
                Noise(b, rng, toe, 0.4f, 0.0005f, 0.006f, R(3000, 4000));
                Grains(b, rng, toe, 0.05f, 8, 0.12f, 3000, highpass: true);
                break;
            case FootSurface.Snow:
                Grains(b, rng, heel, 0.09f, 40, 0.15f, 2500);
                Grains(b, rng, toe, 0.07f, 30, 0.12f, 3000);
                Tone(b, heel, 0.35f * w, 80, 50, 0.02f, 0.003f, 0.04f);
                Noise(b, rng, heel, 0.25f, 0.005f, 0.04f, 1200);
                Tone(b, toe, 0.05f, R(850, 950), R(1050, 1150), 0.03f, 0.005f, 0.02f); // the squeak of packed snow
                break;
        }
        return Finish(b, run ? 0.75f : 0.6f);
    }

    // ---- Distant gunshot ------------------------------------------------------------------------

    /// <summary>
    /// The far layer of a real firearm (Gun and Boom styles), crossfaded in with distance: the report as it arrives from
    /// far off, mid-heavy (the mids are what carry and what you place it by): a softened crack, a band-limited blast, only
    /// a small boom, and a long rolling echo with slap-back. Null for styles that don't carry (suppressed, energy, toy,
    /// nailgun).
    /// </summary>
    public static short[]? GunshotDistant(WeaponDef w)
    {
        if (w.Sound is not (SoundStyle.Gun or SoundStyle.Boom))
            return null;
        var rng = new Random(2000 + w.Id * 7919);
        var c = w.Caliber;
        var tail = 0.12f + w.Tail * 0.9f;
        var boom = w.Sound == SoundStyle.Boom;
        var b = new Buf(0.55f + tail * 2.2f);
        Band(b, rng, 0, 0.45f, 0.0008f, 0.008f, 900, 4500 + 1500 * w.Bright); // the crack, softened by the distance
        Band(b, rng, 0.001f, 1f, 0.003f, 0.035f + 0.05f * c + (boom ? 0.04f : 0), 220, 1500 + 900 * w.Bright); // the blast
        Tone(b, 0, 0.18f + 0.15f * c, 140 - 40 * c, 70, 0.04f, 0.003f, 0.06f + 0.08f * c + (boom ? 0.06f : 0)); // a small boom (not a sub rumble)
        Band(b, rng, 0.02f, 0.4f, 0.04f, tail * 0.9f + 0.15f, 300, 1100 + 500 * w.Bright, length: tail * 3 + 0.4f); // the rolling echo
        Echo(b, 0.55f, 2.2f + w.Tail);
        return Finish(b, boom ? 0.95f : 0.85f);
    }
}
