using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Gun mix" (<see cref="Audio.PlanShot"/>): offline renders of the game's baked gunshots through the
/// mixing path the game plays them with (the shot plan's layer gains, OpenAL's inverse-distance-clamped falloff, the
/// per-source gain cap of 1 and the EFX distance filter's low and high shelves), measured for loudness (a K-weighted
/// "LUFS-ish" over the first 400 ms) and spectral balance (energy below 150 Hz, 150 Hz-2 kHz, above 2 kHz). Checks:
/// your own shot is clearly louder than another player's at 5 m; another player's shot gets steadily quieter and thinner
/// (less low end) with distance yet stays audible at 80 m; a suppressor stays quiet far away; explosions stay big and
/// launchers aren't drowned out; full-auto fire with an explosion on top stays under full scale; and planning a shot
/// allocates nothing and costs next to no CPU. Headless (no OpenAL needed); stereo panning and reverb are left out.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> GunMixNotes = new();

    private static void MixLine(string s)
    {
        Line(s);
        GunMixNotes.Add(s);
    }

    /// <summary>The distances another player's shot is rendered at (m).</summary>
    private static readonly float[] MixDistances = [5f, 15f, 30f, 60f, 80f, 100f];

    /// <summary>Loudness (K-weighted, dB), plain RMS (dBFS), peak and the energy shares of the three bands.</summary>
    private readonly record struct MixMeasure(double Lufs, double RmsDb, double Peak, double Low, double Mid, double High);

    private static void CheckGunMix()
    {
        GunMixNotes.Clear();
        var set = Audio.SynthesiseAll();
        var real = Weapons.All.Where(w => w.Sound is SoundStyle.Gun or SoundStyle.Boom && !w.Suppressor && set.Far[w.Id] != null).ToArray();
        if (real.Length == 0)
        {
            Fail("gun mix: no real firearms with a distant layer");
            return;
        }
        var n = MixDistances.Length;
        var rel = Enumerable.Range(0, n).Select(_ => new List<double>()).ToArray();
        var lows = Enumerable.Range(0, n).Select(_ => new List<double>()).ToArray();
        var highs = Enumerable.Range(0, n).Select(_ => new List<double>()).ToArray();
        var ownLufs = new List<double>();
        var ownLow = new List<double>();
        var ownHigh = new List<double>();
        var margins = new List<(double Db, string Name)>();
        var notMonotonic = new List<string>();
        var bassier = new List<string>();
        foreach (var w in real)
        {
            var own = MeasureMix(RenderMixShot(set, w, null, false));
            ownLufs.Add(own.Lufs);
            ownLow.Add(own.Low);
            ownHigh.Add(own.High);
            MixMeasure prev = default;
            for (var i = 0; i < n; i++)
            {
                var m = MeasureMix(RenderMixShot(set, w, MixDistances[i], false));
                rel[i].Add(m.Lufs - own.Lufs);
                lows[i].Add(m.Low);
                highs[i].Add(m.High);
                if (i == 0)
                    margins.Add((own.Lufs - m.Lufs, w.Name));
                else
                {
                    var step = MixDistances[i] - MixDistances[i - 1] >= 30f ? 1.0 : 0.5;
                    if (m.Lufs > prev.Lufs - step)
                        notMonotonic.Add($"{w.Name} {MixDistances[i - 1]:0}->{MixDistances[i]:0} m {prev.Lufs - own.Lufs:+0.0;-0.0}->{m.Lufs - own.Lufs:+0.0;-0.0} dB");
                    if (m.Low > prev.Low + 0.02)
                        bassier.Add($"{w.Name} {MixDistances[i - 1]:0}->{MixDistances[i]:0} m low {prev.Low:0%}->{m.Low:0%}");
                }
                prev = m;
            }
        }
        margins.Sort((a, b) => a.Db.CompareTo(b.Db));
        var med = rel.Select(Median).ToArray();
        var medLow = lows.Select(Median).ToArray();
        var medHigh = highs.Select(Median).ToArray();
        MixLine($"own shot (median of {real.Length} firearms): {Median(ownLufs):0.0} LUFS-ish, low {Median(ownLow):0%} high {Median(ownHigh):0%}; "
            + $"own vs another player's at 5 m: {Median(margins.Select(m => m.Db).ToList()):+0.0;-0.0} dB median, {margins[0].Db:+0.0;-0.0} dB least ({margins[0].Name})");
        MixLine("another player's shot vs your own: " + string.Join(", ", Enumerable.Range(0, n).Select(i => $"{MixDistances[i]:0} m {med[i]:+0.0;-0.0} dB (low {medLow[i]:0%} high {medHigh[i]:0%})")));

        // A few classes on their own, so a reader can hear what to expect.
        foreach (var cls in new[] { WeaponClass.AssaultRifle, WeaponClass.Sniper, WeaponClass.Shotgun })
            if (real.FirstOrDefault(x => x.Class == cls) is { } w)
            {
                var own = MeasureMix(RenderMixShot(set, w, null, false));
                MixLine($"  {w.Name}: own {own.Lufs:0.0} LUFS-ish peak {own.Peak:0.00}; " + string.Join(", ", MixDistances.Select(d =>
                {
                    var m = MeasureMix(RenderMixShot(set, w, d, false));
                    return $"{d:0} m {m.Lufs - own.Lufs:+0.0;-0.0} low {m.Low:0%}";
                })));
            }

        if (Median(margins.Select(m => m.Db).ToList()) < OwnShotMarginDb || margins[0].Db < OwnShotMarginDb - 2)
            Fail($"your own shot is only {Median(margins.Select(m => m.Db).ToList()):+0.0;-0.0} dB (median; {margins[0].Name} {margins[0].Db:+0.0;-0.0}) louder than another player's at 5 m (want {OwnShotMarginDb} dB, {OwnShotMarginDb - 2} dB for every gun)");
        if (notMonotonic.Count > 0)
            Fail($"another player's shot doesn't get steadily quieter with distance for {notMonotonic.Count} step(s): {string.Join("; ", notMonotonic.Take(6))}");
        if (bassier.Count > 0)
            Fail($"another player's shot gets bassier with distance for {bassier.Count} step(s): {string.Join("; ", bassier.Take(6))}");
        if (!(medLow[3] < medLow[0] * 0.6))
            Fail($"the low end doesn't fall away with distance: {medLow[0]:0%} below 150 Hz at 5 m, {medLow[3]:0%} at 60 m (want under 0.6x)");
        var at80 = Array.IndexOf(MixDistances, 80f);
        if (med[at80] < -40)
            Fail($"another player's shot at 80 m is {med[at80]:0.0} dB under your own (want at least -40 dB: you must still hear and place it)");
        if (med[^1] > -20)
            Fail($"another player's shot at 100 m is only {med[^1]:0.0} dB under your own (want -20 dB or quieter)");
        if (medHigh[at80] < 0.04)
            Fail($"another player's shot at 80 m keeps only {medHigh[at80]:0%} of its energy above 2 kHz (the crack that locates it)");

        CheckGunMixSuppressed(set, real);
        CheckGunMixRelations(set, real);
        CheckGunMixHeadroom(set, real);
        CheckGunMixCost();
        Line("::notice title=Gun mix (results)::" + string.Join("%0A", GunMixNotes.Select(t => t.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>How much louder your own shot must be than another player's at 5 m (median over the firearms).</summary>
    private const double OwnShotMarginDb = 6;

    /// <summary>A suppressed shot from another player stays well under the open one, near and far.</summary>
    private static void CheckGunMixSuppressed(Audio.SoundSet set, WeaponDef[] real)
    {
        var at30 = new List<double>();
        var at60 = new List<double>();
        foreach (var w in real.Where(w => w.Sound is SoundStyle.Gun or SoundStyle.Boom))
        {
            at30.Add(MeasureMix(RenderMixShot(set, w, 30f, true)).Lufs - MeasureMix(RenderMixShot(set, w, 30f, false)).Lufs);
            at60.Add(MeasureMix(RenderMixShot(set, w, 60f, true)).Lufs - MeasureMix(RenderMixShot(set, w, 60f, false)).Lufs);
        }
        MixLine($"suppressed vs open, another player's: 30 m {Median(at30):+0.0;-0.0} dB, 60 m {Median(at60):+0.0;-0.0} dB (median)");
        if (Median(at60) > -8)
            Fail($"a suppressed shot at 60 m is only {Median(at60):+0.0;-0.0} dB under the open one (want -8 dB or quieter)");
    }

    /// <summary>Explosions stay bigger than gunfire at the same distance; launchers aren't drowned out by rifles.</summary>
    private static void CheckGunMixRelations(Audio.SoundSet set, WeaponDef[] real)
    {
        var rifle = real.FirstOrDefault(w => w.Class == WeaponClass.AssaultRifle) ?? real[0];
        var ownRifle = MeasureMix(RenderMixShot(set, rifle, null, false));
        var boom = set.Effects[(int)Sfx.Explosion][0];
        var parts = new List<string>();
        foreach (var d in new[] { 5f, 15f, 30f, 60f })
        {
            var e = MeasureMix(RenderMixEffect(boom, d, Audio.FragExplosionGain));
            var g = MeasureMix(RenderMixShot(set, rifle, d, false));
            parts.Add($"{d:0} m {e.Lufs - ownRifle.Lufs:+0.0;-0.0} dB ({e.Lufs - g.Lufs:+0.0;-0.0} vs the rifle there)");
            if (d == 15f && e.Lufs < g.Lufs + 6)
                Fail($"a frag explosion at 15 m is only {e.Lufs - g.Lufs:+0.0;-0.0} dB louder than {rifle.Name} fired from there (want 6 dB)");
        }
        MixLine($"frag explosion vs your own {rifle.Name}: " + string.Join(", ", parts));
        var launchers = Weapons.All.Where(w => w.Sound is SoundStyle.Rocket or SoundStyle.Thump).ToArray();
        if (launchers.Length > 0)
        {
            var own = launchers.Select(w => MeasureMix(RenderMixShot(set, w, null, false)).Lufs - ownRifle.Lufs).ToList();
            var far = launchers.Select(w => MeasureMix(RenderMixShot(set, w, 30f, false)).Lufs - MeasureMix(RenderMixShot(set, rifle, 30f, false)).Lufs).ToList();
            MixLine($"launchers ({launchers.Length}) vs {rifle.Name}: your own {Median(own):+0.0;-0.0} dB, another player's at 30 m {Median(far):+0.0;-0.0} dB (median)");
            if (Median(far) < -3)
                Fail($"another player's launcher at 30 m is {Median(far):+0.0;-0.0} dB under a rifle there (drowned out)");
            if (Median(own) < -6)
                Fail($"your own launcher is {Median(own):+0.0;-0.0} dB under your own rifle");
        }
    }

    /// <summary>Your fastest full-auto gun for a second with a frag going off 8 m away, at the default master volume: under full scale.</summary>
    private static void CheckGunMixHeadroom(Audio.SoundSet set, WeaponDef[] real)
    {
        var w = real.Where(x => x.Mode != FireMode.Semi).OrderByDescending(x => x.Rpm).FirstOrDefault() ?? real[0];
        var sr = Audio.SampleRate;
        var mix = new float[(int)(1.5f * sr)];
        var plan = Audio.PlanShot(true, false, true, 0f);
        var period = 60.0 * sr / Math.Max(1, w.Rpm);
        var k = 0;
        for (var t = 0.0; t < sr; t += period)
        {
            var shot = set.Shots[w.Id][k++ % set.Shots[w.Id].Length];
            for (var i = 0; i < shot.Length && (int)t + i < mix.Length; i++)
                mix[(int)t + i] += shot[i] / 32768f * MathF.Min(1f, plan.Near);
        }
        double gunPeak = 0;
        foreach (var v in mix)
            gunPeak = Math.Max(gunPeak, Math.Abs(v));
        var results = new List<string>();
        const float master = 0.8f; // Audio.MasterVolume's default (Settings > Audio)
        foreach (var d in new[] { 3f, 8f })
        {
            var boom = RenderMixEffect(set.Effects[(int)Sfx.Explosion][0], d, Audio.FragExplosionGain);
            double peak = 0;
            var at = (int)(0.3f * sr);
            for (var i = 0; i < mix.Length; i++)
            {
                var v = mix[i] + (i >= at && i - at < boom.Length ? boom[i - at] : 0f);
                peak = Math.Max(peak, Math.Abs(v) * master);
            }
            results.Add($"explosion at {d:0} m -> peak {peak:0.00}");
            if (d == 8f && peak > 1.0)
                Fail($"{w.Name} full auto with a frag 8 m away peaks at {peak:0.00} at master {master:0.0} (over full scale: the output limiter would pump)");
        }
        MixLine($"headroom: your {w.Name} full auto ({w.Rpm:0} rpm) peaks {gunPeak * master:0.00} at master {master:0.0}; " + string.Join(", ", results));
    }

    /// <summary>Planning a shot (what <see cref="Audio.Shot"/> does per shot before OpenAL) allocates nothing and is cheap.</summary>
    private static void CheckGunMixCost()
    {
        float sink = 0;
        for (var i = 0; i < 1000; i++)
            sink += Audio.PlanShot(i % 3 == 0, i % 5 == 0, true, i % 120).Near; // warm-up
        var sw = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        const int calls = 200_000;
        for (var i = 0; i < calls; i++)
        {
            var p = Audio.PlanShot(i % 3 == 0, i % 5 == 0, true, i % 120);
            var f = Audio.DistanceFilter(p.NearFalloff, i % 120);
            sink += p.Near + p.Far + f.Lf + f.Hf;
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var ns = sw.Elapsed.TotalMilliseconds * 1e6 / calls;
        MixLine($"cost: {ns:0} ns per shot plan + filter, {bytes} bytes allocated over {calls} (sink {sink % 1:0.0})");
        if (bytes > 0)
            Fail($"planning a shot allocated {bytes} bytes over {calls} calls (want none)");
        if (ns > 2000)
            Fail($"planning a shot takes {ns:0} ns (budget 2 us)");
    }

    // ---- Offline mixing path ---------------------------------------------------------------------------

    /// <summary>
    /// A weapon's shot as the game would play it (variant 0, no pitch or gain jitter): your own (<paramref name="distance"/>
    /// null) or another player's at a distance, near and far layers each at their plan gain times OpenAL's attenuation
    /// (capped at 1, AL_MAX_GAIN), each through the distance filter.
    /// </summary>
    private static float[] RenderMixShot(Audio.SoundSet set, WeaponDef w, float? distance, bool suppressed)
    {
        var near = (suppressed ? set.Suppressed : set.Shots)[w.Id][0];
        var far = suppressed ? null : set.Far[w.Id];
        var d = distance ?? 0f;
        var plan = Audio.PlanShot(distance == null, suppressed, far != null, d);
        var len = Math.Max(near.Length, far?.Length ?? 0);
        var o = new float[len];
        AddLayer(o, near, plan.Near, plan.NearFalloff, distance);
        if (far != null && plan.Far > 0f)
            AddLayer(o, far, plan.Far * Audio.FarTrimFor(set.Shots[w.Id][0], far), plan.FarFalloff, distance);
        return o;
    }

    /// <summary>A positional effect (<see cref="Audio.Effect3D"/>: the normal falloff) at a distance.</summary>
    private static float[] RenderMixEffect(short[] pcm, float distance, float gain)
    {
        var o = new float[pcm.Length];
        AddLayer(o, pcm, gain, Audio.EffectFalloff, distance);
        return o;
    }

    private static void AddLayer(float[] o, short[] pcm, float gain, Audio.Falloff falloff, float? distance)
    {
        if (gain <= 0f)
            return;
        var g = MathF.Min(1f, gain * (distance is { } d ? falloff.Attenuation(d) : 1f));
        var x = new float[pcm.Length];
        for (var i = 0; i < x.Length; i++)
            x[i] = pcm[i] / 32768f * g;
        if (distance is { } dd)
        {
            var (lf, hf) = Audio.DistanceFilter(falloff, dd);
            if (lf < 0.999f)
                MixBiquad.Shelf(false, 250f, lf).Run(x);
            if (hf < 0.999f)
                MixBiquad.Shelf(true, 5000f, hf).Run(x);
        }
        for (var i = 0; i < x.Length && i < o.Length; i++)
            o[i] += x[i];
    }

    /// <summary>Loudness and spectral balance of a rendered shot over its first 400 ms.</summary>
    private static MixMeasure MeasureMix(float[] x)
    {
        var n = Math.Min(x.Length, Audio.SampleRate * 2 / 5);
        var win = x.AsSpan(0, n).ToArray();
        double peak = 0, total = 0;
        foreach (var v in win)
        {
            peak = Math.Max(peak, Math.Abs(v));
            total += v * (double)v;
        }
        var k = (float[])win.Clone();
        MixBiquad.Shelf(true, 1681.974f, MathF.Pow(10f, 3.99984f / 20f), 0.7071752f).Run(k);
        MixBiquad.HighPass(38.13547f, 0.500327f).Run(k);
        double kw = 0;
        foreach (var v in k)
            kw += v * (double)v;
        var lo = (float[])win.Clone();
        MixBiquad.LowPass(150f).Run(lo);
        MixBiquad.LowPass(150f).Run(lo);
        var hi = (float[])win.Clone();
        MixBiquad.HighPass(2000f).Run(hi);
        MixBiquad.HighPass(2000f).Run(hi);
        double el = 0, eh = 0;
        foreach (var v in lo)
            el += v * (double)v;
        foreach (var v in hi)
            eh += v * (double)v;
        var t = Math.Max(total, 1e-20);
        var low = Math.Min(1, el / t);
        var high = Math.Min(1, eh / t);
        return new MixMeasure(-0.691 + 10 * Math.Log10(Math.Max(kw / Math.Max(1, n), 1e-20)), 10 * Math.Log10(Math.Max(total / Math.Max(1, n), 1e-20)),
            peak, low, Math.Max(0, 1 - low - high), high);
    }

    private static double Median(List<double> v)
    {
        if (v.Count == 0)
            return 0;
        var s = v.OrderBy(x => x).ToList();
        return s[s.Count / 2];
    }

    /// <summary>A direct-form biquad (RBJ cookbook) for the offline mix: the EFX shelves, K-weighting and the band split.</summary>
    private sealed class MixBiquad(double b0, double b1, double b2, double a1, double a2)
    {
        private const double Sr = Audio.SampleRate;

        public void Run(float[] x)
        {
            double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (var i = 0; i < x.Length; i++)
            {
                double xi = x[i];
                var y = b0 * xi + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                (x2, x1, y2, y1) = (x1, xi, y1, y);
                x[i] = (float)y;
            }
        }

        private static MixBiquad Norm(double n0, double n1, double n2, double d0, double d1, double d2) => new(n0 / d0, n1 / d0, n2 / d0, d1 / d0, d2 / d0);

        /// <summary>A shelf with linear gain <paramref name="gain"/> above (<paramref name="high"/>) or below <paramref name="hz"/> (slope 1 by default).</summary>
        public static MixBiquad Shelf(bool high, float hz, float gain, float q = 0.70710678f)
        {
            var a = Math.Sqrt(Math.Max(gain, 1e-4));
            var w0 = 2 * Math.PI * hz / Sr;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2 * q);
            var s = 2 * Math.Sqrt(a) * alpha;
            return high
                ? Norm(a * (a + 1 + (a - 1) * cos + s), -2 * a * (a - 1 + (a + 1) * cos), a * (a + 1 + (a - 1) * cos - s), a + 1 - (a - 1) * cos + s, 2 * (a - 1 - (a + 1) * cos), a + 1 - (a - 1) * cos - s)
                : Norm(a * (a + 1 - (a - 1) * cos + s), 2 * a * (a - 1 - (a + 1) * cos), a * (a + 1 - (a - 1) * cos - s), a + 1 + (a - 1) * cos + s, -2 * (a - 1 + (a + 1) * cos), a + 1 + (a - 1) * cos - s);
        }

        public static MixBiquad LowPass(float hz, float q = 0.70710678f)
        {
            var w0 = 2 * Math.PI * hz / Sr;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2 * q);
            return Norm((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }

        public static MixBiquad HighPass(float hz, float q = 0.70710678f)
        {
            var w0 = 2 * Math.PI * hz / Sr;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2 * q);
            return Norm((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
        }
    }
}
