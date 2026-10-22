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
}
