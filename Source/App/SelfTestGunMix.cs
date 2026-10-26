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
/// allocates nothing and costs next to no CPU. Those checks measure the voices as tuned (every mixer level at 0 dB,
/// <see cref="VoiceTune"/>); <see cref="CheckGunMixDefaultLevels"/> then renders through the built-in default mix (the
/// baked SeabMix2: own gun +20 dB, others' near layer +14.1, distant layer +10, explosions +20.1) and checks that its
/// boosts are heard in full through the per-voice cap and that your own shot still clearly leads.
/// Headless (no OpenAL needed); stereo panning and reverb are left out.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> GunMixNotes = new();

    /// <summary>Every mixer level at 0 dB (the factory shape): the voices as tuned, before the default mix's levels.</summary>
    private static readonly AudioMix VoiceTune = UnityLevels();

    private static AudioMix UnityLevels()
    {
        var m = new AudioMix();
        for (var i = 0; i < (int)MixBus.COUNT; i++)
            m.SetDb((MixBus)i, 0f);
        return m;
    }

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

        if (Audio.DistanceLf(5) != 1f || !(Audio.DistanceLf(60) < 0.3f) || Audio.DistanceLf(500) < 0.15f - 1e-6f)
            Fail($"distance low shelf: {Audio.DistanceLf(5)} at 5 m, {Audio.DistanceLf(60)} at 60 m, {Audio.DistanceLf(500)} at 500 m");

        CheckGunMixSuppressed(set, real);
        CheckGunMixRelations(set, real);
        CheckGunMixHeadroom(set, real);
        CheckGunMixCost();
        CheckGunMixDefaultLevels(set, real);
        CheckOwnGunVolumeSetting();
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

    /// <summary>
    /// Your fastest full-auto gun for a second with a frag going off 3 m and 8 m away, at the default master volume: the
    /// gun alone stays under <see cref="OwnFireCeiling"/> (nothing for the output limiter to do), and with the frag 8 m
    /// away the summed peak is at most <see cref="LimiterAllowanceDb"/> over full scale (a few milliseconds of gentle
    /// limiting, no pumping). The peaks at the old own-gun gain of 0.75 are logged next to them.
    /// </summary>
    private static void CheckGunMixHeadroom(Audio.SoundSet set, WeaponDef[] real)
    {
        var w = real.Where(x => x.Mode != FireMode.Semi).OrderByDescending(x => x.Rpm).FirstOrDefault() ?? real[0];
        var sr = Audio.SampleRate;
        var mix = new float[(int)(1.5f * sr)];
        var plan = Audio.PlanShot(VoiceTune, true, false, true, 0f);
        var period = 60.0 * sr / Math.Max(1, w.Rpm);
        var k = 0;
        for (var t = 0.0; t < sr; t += period)
        {
            var shot = set.Shots[w.Id][k++ % set.Shots[w.Id].Length];
            for (var i = 0; i < shot.Length && (int)t + i < mix.Length; i++)
                mix[(int)t + i] += shot[i] / 32768f * MathF.Min(1f, plan.Near);
        }
        const float master = 0.8f; // Audio.MasterVolume's default (Settings > Audio)
        const float oldOwn = 0.75f; // Audio.OwnShotGain before the gun mix
        double gunPeak = 0;
        foreach (var v in mix)
            gunPeak = Math.Max(gunPeak, Math.Abs(v) * master);
        if (gunPeak > OwnFireCeiling)
            Fail($"{w.Name} full auto alone peaks at {gunPeak:0.00} at master {master:0.0} (want under {OwnFireCeiling})");
        var results = new List<string>();
        foreach (var d in new[] { 3f, 8f })
        {
            var boom = RenderMixEffect(set.Effects[(int)Sfx.Explosion][0], d, Audio.FragExplosionGain);
            double peak = 0, oldPeak = 0;
            var at = (int)(0.3f * sr);
            for (var i = 0; i < mix.Length; i++)
            {
                var e = i >= at && i - at < boom.Length ? boom[i - at] : 0f;
                peak = Math.Max(peak, Math.Abs(mix[i] + e) * master);
                oldPeak = Math.Max(oldPeak, Math.Abs(mix[i] * oldOwn / MathF.Max(plan.Near, 1e-3f) + e) * master);
            }
            results.Add($"frag at {d:0} m -> peak {peak:0.00} ({20 * Math.Log10(Math.Max(peak, 1e-9)):+0.0;-0.0} dBFS; {oldPeak:0.00} at the old 0.75)");
            if (d == 8f && peak > Math.Pow(10, LimiterAllowanceDb / 20))
                Fail($"{w.Name} full auto with a frag 8 m away peaks at {peak:0.00} at master {master:0.0} (more than {LimiterAllowanceDb} dB over full scale: the output limiter would pump)");
        }
        MixLine($"headroom: your {w.Name} full auto ({w.Rpm} rpm) peaks {gunPeak:0.00} at master {master:0.0}; " + string.Join(", ", results));
    }

    /// <summary>The most your own full-auto fire alone may peak at the default master volume.</summary>
    private const double OwnFireCeiling = 0.8;

    /// <summary>How far over full scale (dB) full-auto fire plus a frag 8 m away may peak: OpenAL Soft's output limiter takes off at most this much.</summary>
    private const double LimiterAllowanceDb = 2;

    /// <summary>Planning a shot (what <see cref="Audio.Shot"/> does per shot before OpenAL) allocates nothing and is cheap.</summary>
    private static void CheckGunMixCost()
    {
        float sink = 0;
        const int calls = 200_000;
        void Round()
        {
            for (var i = 0; i < calls; i++)
            {
                var p = Audio.PlanShot(i % 3 == 0, i % 5 == 0, true, i % 120);
                var f = Audio.DistanceFilter(p.NearFalloff, i % 120);
                sink += p.Near + p.Far + f.Lf + f.Hf;
            }
        }
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off
        // runtime allocation on this thread (seen while other threads allocate and collect) in one at most.
        var bytes = SteadyAllocBytes(Round);
        var sw = Stopwatch.StartNew();
        Round();
        var ns = sw.Elapsed.TotalMilliseconds * 1e6 / calls;
        MixLine($"cost: {ns:0} ns per shot plan + filter, {bytes} bytes allocated over {calls} (sink {sink % 1:0.0})");
        if (bytes > 0)
            Fail($"planning a shot allocated {bytes} bytes over {calls} calls (want none)");
        if (ns > 2000)
            Fail($"planning a shot takes {ns:0} ns (budget 2 us)");
    }

    /// <summary>
    /// The built-in default mix (the baked SeabMix2) against the tuned voices: your own shot plays exactly its level
    /// louder (+20 dB: the per-voice cap lifted with it, not clipped at 1), another player's 5 m shot its near level
    /// louder, so your own still leads by at least <see cref="OwnShotMarginDb"/> (by the tuned margin plus own minus near
    /// level); explosions still beat gunfire at 15 m. The launchers and headroom at the default levels are logged.
    /// </summary>
    private static void CheckGunMixDefaultLevels(Audio.SoundSet set, WeaponDef[] real)
    {
        var mix = AudioMix.Defaults;
        float ownDb = mix.Db(MixBus.GunOwn), nearDb = mix.Db(MixBus.GunOtherNear), farDb = mix.Db(MixBus.GunOtherFar);
        var ownGain = new List<double>();
        var margins = new List<double>();
        var tuneMargins = new List<double>();
        var at80 = new List<double>();
        foreach (var w in real)
        {
            var own = MeasureMix(RenderMixShot(set, w, null, false, mix));
            var ownTune = MeasureMix(RenderMixShot(set, w, null, false));
            ownGain.Add(own.Lufs - ownTune.Lufs);
            margins.Add(own.Lufs - MeasureMix(RenderMixShot(set, w, 5f, false, mix)).Lufs);
            tuneMargins.Add(ownTune.Lufs - MeasureMix(RenderMixShot(set, w, 5f, false)).Lufs);
            at80.Add(MeasureMix(RenderMixShot(set, w, 80f, false, mix)).Lufs - own.Lufs);
        }
        var shift = Median(margins) - Median(tuneMargins);
        MixLine($"default mix ({AudioMixDefaults.BakedMix}: own {ownDb:+0.0;-0.0} dB, near {nearDb:+0.0;-0.0}, far {farDb:+0.0;-0.0}): own shot {Median(ownGain):+0.00;-0.00} dB over the tuned voice (least {ownGain.Min():+0.00;-0.00}); "
                + $"own vs another player's at 5 m {Median(margins):+0.0;-0.0} dB median ({margins.Min():+0.0;-0.0} least; tuned {Median(tuneMargins):+0.0;-0.0}, so {shift:+0.0;-0.0} dB); another player's at 80 m {Median(at80):+0.0;-0.0} dB under your own");
        if (ownGain.Any(g => Math.Abs(g - ownDb) > 0.05))
            Fail($"your own shot at the default {ownDb:+0.0;-0.0} dB plays {ownGain.Min():+0.00;-0.00}..{ownGain.Max():+0.00;-0.00} dB over the tuned voice (want {ownDb:+0.0;-0.0}: clipped by the per-voice cap?)");
        if (Median(margins) < OwnShotMarginDb || margins.Min() < OwnShotMarginDb - 2)
            Fail($"with the default mix your own shot is only {Median(margins):+0.0;-0.0} dB (median; least {margins.Min():+0.0;-0.0}) louder than another player's at 5 m (want {OwnShotMarginDb} dB, {OwnShotMarginDb - 2} for every gun)");
        if (Math.Abs(shift - (ownDb - nearDb)) > 0.2)
            Fail($"the default mix moves own vs another player's at 5 m by {shift:+0.0;-0.0} dB (want own minus near level, {ownDb - nearDb:+0.0;-0.0} dB)");

        var rifle = real.FirstOrDefault(w => w.Class == WeaponClass.AssaultRifle) ?? real[0];
        var boomPcm = set.Effects[(int)Sfx.Explosion][0];
        var boomBus = mix.Gain(MixBus.Explosion);
        var e15 = MeasureMix(RenderMixEffect(boomPcm, 15f, Audio.FragExplosionGain, boomBus)).Lufs - MeasureMix(RenderMixShot(set, rifle, 15f, false, mix)).Lufs;
        if (e15 < 6)
            Fail($"with the default mix a frag explosion at 15 m is only {e15:+0.0;-0.0} dB louder than {rifle.Name} fired from there (want 6 dB)");
        var ownRifle = MeasureMix(RenderMixShot(set, rifle, null, false, mix)).Lufs;
        var launchers = Weapons.All.Where(w => w.Sound is SoundStyle.Rocket or SoundStyle.Thump).ToArray();
        var lOwn = launchers.Select(w => MeasureMix(RenderMixShot(set, w, null, false, mix)).Lufs - ownRifle).ToList();
        var lFar = launchers.Select(w => MeasureMix(RenderMixShot(set, w, 30f, false, mix)).Lufs - MeasureMix(RenderMixShot(set, rifle, 30f, false, mix)).Lufs).ToList();

        // Headroom at the default levels (logged: the tuned-voice check above is the one that must hold).
        var fast = real.Where(x => x.Mode != FireMode.Semi).OrderByDescending(x => x.Rpm).FirstOrDefault() ?? real[0];
        var plan = Audio.PlanShot(mix, true, false, true, 0f);
        var sr = Audio.SampleRate;
        var period = 60.0 * sr / Math.Max(1, fast.Rpm);
        var buf = new float[sr];
        var k = 0;
        for (var t = 0.0; t < sr; t += period)
        {
            var shot = set.Shots[fast.Id][k++ % set.Shots[fast.Id].Length];
            for (var i = 0; i < shot.Length && (int)t + i < buf.Length; i++)
                buf[(int)t + i] += shot[i] / 32768f * plan.NearBusGain * MathF.Min(1f, plan.Near);
        }
        var peak = buf.Max(MathF.Abs) * 0.8f;
        MixLine($"default mix: frag at 15 m {e15:+0.0;-0.0} dB over {rifle.Name} there; launchers vs {rifle.Name}: your own {Median(lOwn):+0.0;-0.0} dB, another player's at 30 m {Median(lFar):+0.0;-0.0} dB; "
                + $"your {fast.Name} full auto peaks {peak:0.00} ({20 * Math.Log10(Math.Max(peak, 1e-9)):+0.0;-0.0} dBFS) at master 0.8 before OpenAL's output limiter");
    }

    /// <summary>Settings > Audio "Own gun volume": an old settings file loads it as 1, a bad value is ignored or clamped, and it saves.</summary>
    private static void CheckOwnGunVolumeSetting()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.txt");
            File.WriteAllLines(path, ["name=Old Timer", "volume=0.6", "effects_volume=0.9"]); // from before the slider
            var s = Settings.Load(path);
            if (s.OwnGunVolume != 1f || Math.Abs(s.EffectsVolume - 0.9f) > 1e-6f)
                Fail($"old settings file: own gun volume {s.OwnGunVolume} (want 1), effects {s.EffectsVolume}");
            File.WriteAllLines(path, ["own_gun_volume=abc"]);
            if (Settings.Load(path).OwnGunVolume != 1f)
                Fail("a bad own gun volume didn't fall back to 1");
            File.WriteAllLines(path, ["own_gun_volume=4"]);
            if (Settings.Load(path).OwnGunVolume != 1f)
                Fail("own gun volume 4 wasn't clamped to 1");
            s.OwnGunVolume = 0.55f;
            s.Save(path);
            var back = Settings.Load(path).OwnGunVolume;
            if (Math.Abs(back - 0.55f) > 1e-6f)
                Fail($"own gun volume didn't survive a save: {back}");
            if (new Settings().OwnGunVolume != 1f)
                Fail("own gun volume doesn't default to 1 (the tuned mix)");
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // a leftover temp folder is harmless
            }
        }
    }

    // ---- Offline mixing path ---------------------------------------------------------------------------

    /// <summary>
    /// A weapon's shot as the game would play it (variant 0, no pitch or gain jitter): your own (<paramref name="distance"/>
    /// null) or another player's at a distance, near and far layers each at their plan gain times OpenAL's attenuation
    /// (capped at 1, AL_MAX_GAIN), each through the distance filter, through <paramref name="mix"/> (default
    /// <see cref="VoiceTune"/>): the bus gain scales the voice and its cap together (Audio.Mixer.cs).
    /// </summary>
    private static float[] RenderMixShot(Audio.SoundSet set, WeaponDef w, float? distance, bool suppressed, AudioMix? mix = null)
    {
        var near = (suppressed ? set.Suppressed : set.Shots)[w.Id][0];
        var far = suppressed ? null : set.Far[w.Id];
        var d = distance ?? 0f;
        var plan = Audio.PlanShot(mix ?? VoiceTune, distance == null, suppressed, far != null, d, w.Class == WeaponClass.Launcher);
        var len = Math.Max(near.Length, far?.Length ?? 0);
        var o = new float[len];
        AddLayer(o, near, plan.Near, plan.NearFalloff, distance, plan.NearBusGain);
        if (far != null && plan.Far > 0f)
            AddLayer(o, far, plan.Far * Audio.FarTrimFor(set.Shots[w.Id][0], far), plan.FarFalloff, distance, plan.FarBusGain);
        return o;
    }

    /// <summary>A positional effect (<see cref="Audio.Effect3D"/>: the normal falloff) at a distance, at bus gain <paramref name="bus"/>.</summary>
    private static float[] RenderMixEffect(short[] pcm, float distance, float gain, float bus = 1f)
    {
        var o = new float[pcm.Length];
        AddLayer(o, pcm, gain, Audio.EffectFalloff, distance, bus);
        return o;
    }

    /// <summary>One voice: OpenAL plays min(AL_GAIN x attenuation, AL_MAX_GAIN), with both scaled by the bus gain.</summary>
    private static void AddLayer(float[] o, short[] pcm, float gain, Audio.Falloff falloff, float? distance, float bus = 1f)
    {
        if (gain <= 0f || bus <= 0f)
            return;
        var g = bus * MathF.Min(1f, gain * (distance is { } d ? falloff.Attenuation(d) : 1f));
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
