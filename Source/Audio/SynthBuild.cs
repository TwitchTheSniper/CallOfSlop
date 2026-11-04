namespace CallOfSlop;

/// <summary>
/// The Sandbox's building sounds (BuildSounds.cs plays them; docs/sandbox/CONSTRUCTS.md "Building sounds"): ticks for the
/// ghost, a clunk per size and material for a part going on, the welder's arc and the wrench's ratchet as exact loops,
/// pops, link and physics gun cues. Everything is made once at start-up, deterministically for its arguments.
/// </summary>
public static partial class Synth
{
    /// <summary>The clunks' size classes (small click to a capital part's deep thud).</summary>
    public const int BuildTiers = 4;

    /// <summary>Takes per size and material, so a row of parts doesn't sound copied.</summary>
    public const int BuildTakes = 2;

    /// <summary>The variants of <see cref="Sfx.BuildPlace"/>: size class x material x take.</summary>
    public const int BuildPlaceVariants = BuildTiers * (int)BuildMaterial.Count * BuildTakes;

    /// <summary>The <see cref="Sfx.BuildPlace"/> variant for a size class, material and take.</summary>
    public static int BuildPlaceVariant(int tier, BuildMaterial material, int take) => (tier * (int)BuildMaterial.Count + (int)material) * BuildTakes + take;

    private static short[]? BuildFoley(Sfx sfx, int v)
    {
        if (sfx is < Sfx.BuildSnap or > Sfx.BuildDrop)
            return null;
        var rng = new Random(91000 + (int)sfx * 613 + v * 2017);
        Buf b;
        switch (sfx)
        {
            case Sfx.BuildSnap:
                // A soft, bright tick: a magnet catching.
                b = new Buf(0.05f);
                Tone(b, 0, 0.8f, 2500, 1900, 0.008f, 0.0004f, 0.007f);
                Noise(b, rng, 0, 0.22f, 0.0002f, 0.003f, 6000, highpass: true);
                return Finish(b, 0.5f);
            case Sfx.BuildRotate:
                // Lower and doubled: a detent going round.
                b = new Buf(0.06f);
                Tone(b, 0, 0.7f, 1500, 1250, 0.006f, 0.0004f, 0.006f);
                Tone(b, 0.017f, 0.5f, 1250, 1050, 0.006f, 0.0004f, 0.006f);
                return Finish(b, 0.45f);
            case Sfx.BuildRefuse:
                // A dull little "no": a falling wooden tock with nothing bright in it.
                b = new Buf(0.12f);
                Tone(b, 0, 0.9f, 340, 210, 0.03f, 0.001f, 0.03f);
                Noise(b, rng, 0, 0.15f, 0.0005f, 0.01f, 900);
                return Finish(b, 0.5f);
            case Sfx.BuildPlace:
                return BuildClunk(v / (BuildTakes * (int)BuildMaterial.Count), (BuildMaterial)(v / BuildTakes % (int)BuildMaterial.Count), rng);
            case Sfx.BuildWeldLoop:
                return WeldLoop(rng);
            case Sfx.BuildRatchetLoop:
                return RatchetLoop(rng);
            case Sfx.BuildWeldDone:
                // The arc lets go: a tick, a ring off the hot metal and a little hiss.
                b = new Buf(0.3f);
                Tone(b, 0, 0.5f, 3300, 3000, 0.02f, 0.0005f, 0.04f);
                Tone(b, 0, 0.25f, 5200, 5100, 0.02f, 0.0005f, 0.02f);
                Noise(b, rng, 0.005f, 0.14f, 0.002f, 0.06f, 3500, highpass: true);
                return Finish(b, 0.55f);
            case Sfx.BuildUnbolt:
                // The bolt lets go, then the piece comes away: a click and a looser, lower clack.
                b = new Buf(0.22f);
                Tone(b, 0, 0.5f, 2100, 1600, 0.01f, 0.0004f, 0.008f, harmonics: 0.3f);
                Tone(b, 0.045f + 0.012f * v, 0.8f, 520 + 90 * v, 340 + 60 * v, 0.03f, 0.001f, 0.035f, harmonics: 0.25f);
                Noise(b, rng, 0.045f + 0.012f * v, 0.3f, 0.0005f, 0.012f, 2500);
                return Finish(b, 0.65f);
            case Sfx.BuildPop:
                // The remover: a quick upward pop, a part simply gone.
                b = new Buf(0.14f);
                Tone(b, 0, 0.9f, 190 + 40 * v, 760 + 80 * v, 0.018f, 0.001f, 0.022f);
                Noise(b, rng, 0, 0.2f, 0.0005f, 0.015f, 1800);
                return Finish(b, 0.6f);
            case Sfx.BuildLinkNode:
                b = new Buf(0.05f);
                Tone(b, 0, 0.8f, 1150, 1000, 0.008f, 0.0005f, 0.009f);
                Noise(b, rng, 0, 0.12f, 0.0002f, 0.003f, 5000, highpass: true);
                return Finish(b, 0.45f);
            case Sfx.BuildLinkCable:
                // A plug home: two quick clicks and a thin ping.
                b = new Buf(0.16f);
                Tone(b, 0, 0.6f, 2000, 1500, 0.006f, 0.0004f, 0.006f);
                Tone(b, 0.03f, 0.8f, 1700, 1300, 0.006f, 0.0004f, 0.008f);
                Tone(b, 0.032f, 0.2f, 3100, 3050, 0.02f, 0.001f, 0.03f);
                return Finish(b, 0.5f);
            case Sfx.BuildLinkPipe:
                // A flange seating: a hollow low clunk and its short ring.
                b = new Buf(0.26f);
                Tone(b, 0, 0.8f, 300, 245, 0.02f, 0.001f, 0.05f, harmonics: 0.15f);
                Tone(b, 0.002f, 0.3f, 830, 800, 0.03f, 0.001f, 0.05f);
                Noise(b, rng, 0, 0.2f, 0.0005f, 0.012f, 1600);
                return Finish(b, 0.6f);
            case Sfx.BuildGrab:
                b = new Buf(0.14f);
                Tone(b, 0, 0.7f, 420, 980, 0.04f, 0.004f, 0.03f);
                Noise(b, rng, 0, 0.1f, 0.004f, 0.025f, 2600);
                return Finish(b, 0.4f);
            case Sfx.BuildDrop:
                b = new Buf(0.12f);
                Tone(b, 0, 0.7f, 760, 290, 0.03f, 0.002f, 0.025f);
                return Finish(b, 0.4f);
        }
        return null;
    }

    /// <summary>
    /// A part landing: a body note that drops an octave and more per size class, shaped by the material. Metal rings on
    /// inharmonic partials, wood knocks and dies fast, stone is dull with grit after it, rubber is a soft pitched thump.
    /// </summary>
    private static short[] BuildClunk(int tier, BuildMaterial material, Random rng)
    {
        ReadOnlySpan<float> freq = [860f, 300f, 105f, 50f];
        ReadOnlySpan<float> decay = [0.016f, 0.04f, 0.11f, 0.3f];
        var f = freq[tier] * (0.96f + 0.08f * (float)rng.NextDouble());
        var d = decay[tier];
        var b = new Buf(0.1f + d * 6f);
        // The impact itself.
        var snap = MathF.Min(9000f, 2400f + f * 2.4f);
        Noise(b, rng, 0, tier == 0 ? 0.3f : 0.55f, 0.0004f, 0.004f + d * 0.08f, material == BuildMaterial.Rubber ? snap * 0.25f : snap);
        Tone(b, 0, 1f, f * 1.5f, f, d * 0.25f, 0.0006f, d, harmonics: material == BuildMaterial.Metal ? 0.12f : 0f);
        switch (material)
        {
            case BuildMaterial.Metal:
                Tone(b, 0, 0.34f, f * 2.76f, f * 2.76f, 1, 0.0006f, d * 1.4f);
                Tone(b, 0.0008f, 0.2f, f * 5.4f, f * 5.4f, 1, 0.0006f, d * 0.9f);
                if (tier >= 2)
                    Tone(b, 0.012f, 0.14f, f * 8.9f, f * 8.9f, 1, 0.001f, d * 0.7f);
                break;
            case BuildMaterial.Wood:
                Tone(b, 0, 0.3f, f * 2.2f, f * 2.1f, 0.01f, 0.0006f, d * 0.5f);
                Band(b, rng, 0, 0.3f, 0.0005f, d * 0.4f, 280f, 1500f);
                break;
            case BuildMaterial.Stone:
                Noise(b, rng, 0.002f, 0.5f, 0.001f, d * 0.7f, 900f + f);
                for (var k = 0; k < 2 + tier; k++) // (grit settling)
                    Noise(b, rng, d * (0.5f + 0.7f * k), 0.12f, 0.0005f, 0.006f + d * 0.08f, 2600f);
                break;
            case BuildMaterial.Rubber:
                Tone(b, 0, 0.6f, f * 2.1f, f * 0.8f, d * 0.4f, 0.002f, d * 0.8f);
                break;
        }
        if (tier == 3)
        {
            // A capital part: a sub thump under it and the load settling a moment later.
            Tone(b, 0, 0.9f, 62f, 34f, 0.09f, 0.003f, 0.4f);
            Tone(b, 0.11f, 0.3f, f * 1.7f, f * 1.2f, 0.05f, 0.002f, 0.13f, harmonics: material == BuildMaterial.Metal ? 0.1f : 0f);
            Noise(b, rng, 0.02f, 0.25f, 0.02f, 0.3f, 240f);
        }
        return Finish(b, 0.82f);
    }

    /// <summary>The welder's arc: a hiss with crackles over a mains buzz, an exact loop (the crackles wrap round; the buzz is a whole number of cycles).</summary>
    private static short[] WeldLoop(Random rng)
    {
        const int cycles = 180; // (of 100 Hz)
        var b = new Buf(cycles / 100f);
        var len = b.Len;
        float lp = 0, hi = 0;
        var noise = new float[len];
        for (var pass = 0; pass < 2; pass++) // (twice round: the filters' state matches at the seam)
            for (var i = 0; i < len; i++)
            {
                if (pass == 0)
                    noise[i] = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.32f * (noise[i] - lp);
                hi += 0.06f * (lp - hi);
                if (pass == 1)
                    b.S[i] = (lp - hi) * 0.42f;
            }
        for (var i = 0; i < len; i++)
        {
            var t = i / (float)len;
            var buzz = MathF.Sin(MathF.Tau * cycles * t) * 0.09f + MathF.Sin(MathF.Tau * cycles * 3 * t) * 0.04f;
            var flutter = 0.82f + 0.18f * MathF.Sin(MathF.Tau * 7 * t) * MathF.Sin(MathF.Tau * 3 * t);
            b.S[i] = b.S[i] * flutter + buzz;
        }
        var tail = (int)(0.012f * Sr);
        for (var k = 0; k < 46; k++)
        {
            var at = rng.Next(len);
            var gain = 0.25f + 0.5f * (float)rng.NextDouble();
            var pitch = 2600f + 3200f * (float)rng.NextDouble();
            for (var i = 0; i < tail; i++)
            {
                var t = i / (float)Sr;
                b.S[(at + i) % len] += MathF.Sin(MathF.Tau * pitch * t) * gain * MathF.Exp(-t * 520f);
            }
        }
        return FinishLoop(b, 0.5f);
    }

    /// <summary>The wrench's ratchet while a part is fitted: four pawl clicks with a little swing, an exact loop.</summary>
    private static short[] RatchetLoop(Random rng)
    {
        const int clicks = 4;
        var b = new Buf(0.6f);
        var len = b.Len;
        var tail = (int)(0.03f * Sr);
        for (var k = 0; k < clicks; k++)
        {
            var at = (int)((k + (k % 2 == 1 ? 0.12f : 0f)) * len / clicks);
            var pitch = (k % 2 == 1 ? 3100f : 3500f) * (0.97f + 0.06f * (float)rng.NextDouble());
            var gain = k % 2 == 1 ? 0.7f : 1f;
            float lp = 0;
            for (var i = 0; i < tail; i++)
            {
                var t = i / (float)Sr;
                lp += 0.4f * ((float)(rng.NextDouble() * 2 - 1) - lp);
                var s = MathF.Sin(MathF.Tau * pitch * t) * 0.7f * MathF.Exp(-t * 380f)
                    + MathF.Sin(MathF.Tau * 1450f * t) * 0.5f * MathF.Exp(-t * 300f)
                    + MathF.Sin(MathF.Tau * 340f * t) * 0.5f * MathF.Exp(-t * 180f)
                    + lp * 0.5f * MathF.Exp(-t * 700f);
                b.S[(at + i) % len] += s * gain;
            }
        }
        return FinishLoop(b, 0.6f);
    }
}
