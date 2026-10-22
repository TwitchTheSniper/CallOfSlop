namespace CallOfSlop;

/// <summary>
/// How snappy a gunshot buffer is (the self-test's measure for the "snappier gunshots" work): pre-roll before the
/// blast, attack time, how much of the energy lands early, how hard the peak stands out of the body, and how fast the
/// body dies away. Headless, allocation-free.
/// </summary>
public static class GunSnap
{
    /// <summary>One buffer's numbers.</summary>
    /// <param name="PreRollMs">Time until the signal first passes 10% of its peak.</param>
    /// <param name="AttackMs">Time until the 0.5 ms peak envelope first reaches 90% of its maximum.</param>
    /// <param name="Early">Energy in the first 30 ms over the energy in the first 300 ms (1 = all up front).</param>
    /// <param name="CrestDb">Peak over the RMS of the first 80 ms, in dB (higher = a sharper, less squashed transient).</param>
    /// <param name="DecayMs">Time from the envelope peak until it first falls 30 dB under it (shorter = drier body).</param>
    public readonly record struct Numbers(float PreRollMs, float AttackMs, float Early, float CrestDb, float DecayMs);

    /// <summary>Measures 16-bit mono PCM at <see cref="Audio.SampleRate"/>.</summary>
    public static Numbers Measure(ReadOnlySpan<short> pcm)
    {
        const int sr = Audio.SampleRate;
        if (pcm.Length == 0)
            return default;
        var peak = 1;
        foreach (var s in pcm)
            peak = Math.Max(peak, Math.Abs((int)s));
        var pre = 0;
        while (pre < pcm.Length && Math.Abs((int)pcm[pre]) < peak / 10)
            pre++;

        // 0.5 ms block peak envelope (computed on the fly twice instead of stored: no allocation).
        const int hop = sr / 2000;
        var blocks = pcm.Length / hop;
        int Block(ReadOnlySpan<short> p, int b)
        {
            var m = 0;
            for (var i = b * hop; i < (b + 1) * hop; i++)
                m = Math.Max(m, Math.Abs((int)p[i]));
            return m;
        }
        int envMax = 0, envAt = 0;
        for (var b = 0; b < blocks; b++)
        {
            var e = Block(pcm, b);
            if (e > envMax)
                (envMax, envAt) = (e, b);
        }
        var attack = 0;
        while (attack < blocks && Block(pcm, attack) < envMax * 0.9f)
            attack++;
        var decay = envAt;
        var floor = envMax * 0.0316f; // -30 dB
        while (decay < blocks && Block(pcm, decay) >= floor)
            decay++;

        double e30 = 0, e300 = 0, e80 = 0;
        for (var i = 0; i < pcm.Length && i < sr * 3 / 10; i++)
        {
            var v = pcm[i] / 32768.0;
            var p = v * v;
            e300 += p;
            if (i < sr * 3 / 100)
                e30 += p;
            if (i < sr * 8 / 100)
                e80 += p;
        }
        var rms80 = Math.Sqrt(e80 / Math.Min(pcm.Length, sr * 8 / 100));
        return new Numbers(
            pre * 1000f / sr,
            attack * 1000f * hop / sr,
            (float)(e30 / Math.Max(e300, 1e-12)),
            (float)(20 * Math.Log10(peak / 32768.0 / Math.Max(rms80, 1e-9))),
            (decay - envAt) * 1000f * hop / sr);
    }

    /// <summary>The median of each number over a list of buffers.</summary>
    public static Numbers Median(IEnumerable<short[]> buffers)
    {
        var all = buffers.Select(b => Measure(b)).ToArray();
        if (all.Length == 0)
            return default;
        float M(Func<Numbers, float> f)
        {
            var v = all.Select(f).OrderBy(x => x).ToArray();
            return v[v.Length / 2];
        }
        return new Numbers(M(n => n.PreRollMs), M(n => n.AttackMs), M(n => n.Early), M(n => n.CrestDb), M(n => n.DecayMs));
    }

    /// <summary>"pre 0.3 ms, attack 0.5 ms, early 0.82, crest 9.1 dB, decay 60 ms".</summary>
    public static string Describe(Numbers n) =>
        $"pre {n.PreRollMs:0.0} ms, attack {n.AttackMs:0.0} ms, early {n.Early:0.00}, crest {n.CrestDb:0.0} dB, decay {n.DecayMs:0} ms";

    /// <summary>Band centres of <see cref="Fingerprint"/> (octaves, 125 Hz to 16 kHz).</summary>
    public static readonly float[] FingerprintBands = [125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Length of a fingerprint: the band levels, then the log decay time, then the crest factor.</summary>
    public static int FingerprintLength => FingerprintBands.Length + 2;

    /// <summary>
    /// A weapon's sonic fingerprint, averaged over its shot variants: the level of each octave band over the first
    /// 150 ms in dB relative to the loudest band (the timbre, independent of loudness), the body's decay time
    /// (20 dB per factor of 10) and the crest factor in dB. Compare two with <see cref="Distance"/>.
    /// </summary>
    public static float[] Fingerprint(IReadOnlyList<short[]> variants)
    {
        var fp = new float[FingerprintLength];
        if (variants.Count == 0)
            return fp;
        var bands = new double[FingerprintBands.Length];
        foreach (var pcm in variants)
        {
            var n = Math.Min(pcm.Length, Audio.SampleRate * 15 / 100);
            var max = -999.0;
            for (var b = 0; b < bands.Length; b++)
            {
                // RBJ band-pass (one octave), energy of the output.
                var w0 = 2 * Math.PI * Math.Min(FingerprintBands[b], Audio.SampleRate * 0.45) / Audio.SampleRate;
                var alpha = Math.Sin(w0) * Math.Sinh(Math.Log(2) / 2 * 1.0 * w0 / Math.Sin(w0));
                var a0 = 1 + alpha;
                double b0 = alpha / a0, b2 = -alpha / a0, a1 = -2 * Math.Cos(w0) / a0, a2 = (1 - alpha) / a0;
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, e = 0;
                for (var i = 0; i < n; i++)
                {
                    var x = pcm[i] / 32768.0;
                    var y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                    (x2, x1, y2, y1) = (x1, x, y1, y);
                    e += y * y;
                }
                bands[b] = 10 * Math.Log10(e / Math.Max(1, n) + 1e-12);
                max = Math.Max(max, bands[b]);
            }
            for (var b = 0; b < bands.Length; b++)
                fp[b] += (float)(bands[b] - max) / variants.Count;
            var m = Measure(pcm);
            fp[bands.Length] += 20f * MathF.Log10(MathF.Max(m.DecayMs, 1f)) / variants.Count;
            fp[bands.Length + 1] += m.CrestDb / variants.Count;
        }
        return fp;
    }

    /// <summary>RMS difference of two fingerprints in dB (band levels, decay and crest weighted alike).</summary>
    public static float Distance(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
            sum += (a[i] - b[i]) * (double)(a[i] - b[i]);
        return (float)Math.Sqrt(sum / Math.Max(1, a.Length));
    }
}
