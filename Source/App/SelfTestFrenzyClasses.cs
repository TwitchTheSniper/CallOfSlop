namespace CallOfSlop;

/// <summary>
/// Self-test section "Frenzy classes": the per-weapon-class frenzy styles (Weapons/Frenzy/classes.json "music",
/// MusicFrenzy.Classes.cs, FrenzyStyleRig.cs). The data loads for every class; each class renders deterministically;
/// the classes sound measurably different (a rhythm-and-spectrum fingerprint per class); the levels still build up
/// inside each class; level 6 of every class sits in the same loudness window against the game's gunfire as the
/// classic frenzy; a class switch starts on a bar line and crossfades over one bar; CPU per class against the heaviest
/// hardbass track; no allocations while mixing (after a warm-up).
/// </summary>
public static partial class SelfTest
{
    /// <summary>The smallest fingerprint distance two classes may have (see <see cref="ClassFingerprint"/>).</summary>
    private const double FrenzyClassMinDistance = 0.15;

    private static readonly List<string> FrenzyClassNotes = new();

    private static void ClassLine(string s)
    {
        Line(s);
        FrenzyClassNotes.Add(s);
    }

    private static void CheckFrenzyClasses()
    {
        FrenzyClassNotes.Clear();
        CheckFrenzyClassData();
        CheckFrenzyClassSound();
        CheckFrenzyClassSwitch();
        CheckFrenzyClassCpu();
        // Two annotations on the CI run (one gets cut off): the classes and levels, then the comparisons, switch and CPU.
        var half = Math.Min(FrenzyClassNotes.Count, 2 * AllClasses.Length);
        string Join(IEnumerable<string> notes) => string.Join("%0A", notes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " ")));
        Line("::warning title=Frenzy classes (styles)::" + Join(FrenzyClassNotes.Take(half)));
        Line("::warning title=Frenzy classes (results)::" + Join(FrenzyClassNotes.Skip(half)));
    }

    private static readonly WeaponClass[] AllClasses = Enum.GetValues<WeaponClass>(); // cached: the allocation check indexes it per block

    private static void CheckFrenzyClassData()
    {
        var styles = FrenzyStyles.Load(FrenzyStyles.FilePath);
        foreach (var e in FrenzyStyles.LoadErrors)
            Fail($"classes.json (music): {e}");
        if (styles.Length != AllClasses.Length)
            Fail($"{styles.Length} styles for {AllClasses.Length} classes");
        var names = new HashSet<string>();
        var tracks = MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)).ToArray();
        foreach (var c in AllClasses)
        {
            var s = styles[(int)c];
            var pool = FrenzyLayer.PoolTracks(tracks, s.Pool);
            ClassLine($"{c}: \"{s.Name}\"; pool {s.Pool} ({pool.Length} tracks), stems {s.StemDrums}/{s.StemBass}/{s.StemLead}, "
                      + $"{s.Layers.Length} layers ({string.Join(", ", s.Layers.Select(l => $"{l.Voice} from L{l.From}"))}), trim {s.Trim}");
            if (!names.Add(s.Name))
                Fail($"{c}: style name \"{s.Name}\" is used by another class");
            if (pool.Length == 0)
                Fail($"{c}: the {s.Pool} pool has no frenzy tracks");
            if (c == WeaponClass.AssaultRifle ? !s.IsPlain : s.IsPlain)
                Fail($"{c}: {(s.IsPlain ? "plays the plain track (no character of its own)" : "should be the classic frenzy, the track as it is")}");
            if (c != WeaponClass.AssaultRifle && !s.Layers.Any(l => l.From <= 2))
                Fail($"{c}: nothing of its character plays at levels 1-2");
        }
        if (!FrenzyStyles.TryClass("Assault Rifle", out var ar) || ar != WeaponClass.AssaultRifle || FrenzyStyles.TryClass("Bazooka", out _))
            Fail("class names don't parse like the weapon files' (\"Assault Rifle\")");

        // A broken section only loses that class (it plays the classic frenzy), the rest still load.
        var probe = new FrenzyStyle[AllClasses.Length];
        Array.Fill(probe, FrenzyStyle.Plain);
        FrenzyStyles.LoadErrors.Clear();
        FrenzyStyles.Parse("""{"format":"call-of-slop-frenzy-classes/1","classes":{"Pistol":{"music":{"style":"A","layers":[{"voice":"kazoo","pattern":"x"}]}},"SMG":{"buffs":{},"music":{"style":"B","pool":"fast"}}}}""", probe);
        if (FrenzyStyles.LoadErrors.Count != 1 || !probe[(int)WeaponClass.Pistol].IsPlain || probe[(int)WeaponClass.SMG].Name != "B")
            Fail($"a broken class section: {FrenzyStyles.LoadErrors.Count} errors, Pistol plain {probe[(int)WeaponClass.Pistol].IsPlain}, SMG \"{probe[(int)WeaponClass.SMG].Name}\"");
        FrenzyStyles.LoadErrors.Clear();
    }

    /// <summary>Runs a layer for some seconds into <paramref name="sink"/> (or nowhere); returns the RMS of the whole span.</summary>
    private static double RunLayer(FrenzyLayer f, double seconds, List<float>? sink = null)
    {
        var block = new float[MusicPlayer.BlockFrames * 2];
        var n = (int)Math.Ceiling(seconds * Audio.SampleRate / MusicPlayer.BlockFrames);
        double sum = 0;
        long count = 0;
        for (var b = 0; b < n; b++)
        {
            Array.Clear(block);
            f.MixInto(block, true);
            foreach (var x in block)
                sum += x * (double)x;
            count += block.Length;
            sink?.AddRange(block);
        }
        return Math.Sqrt(sum / Math.Max(1, count));
    }

    /// <summary>A layer playing <paramref name="c"/> (on <paramref name="track"/>, or the class's own pool), started at level 1.</summary>
    private static FrenzyLayer ClassLayer(WeaponClass c, MusicTrack? track, uint seed = 17)
    {
        var f = new FrenzyLayer(seed) { Class = c, TestTrack = track };
        f.Target = 1;
        return f;
    }

    /// <summary>
    /// A fingerprint of what a layer plays: for three bands (below 200 Hz, 200-2000 Hz, above 2 kHz) the energy on each
    /// of the bar's 16 steps (normalised per band: the rhythm), plus each band's share of the total (the spectrum).
    /// </summary>
    private static double[] ClassFingerprint(FrenzyLayer f, double seconds)
    {
        var buf = new List<float>();
        var start = f.FramesSinceStart;
        RunLayer(f, seconds, buf);
        var fpb = f.Renderer.FramesPerBar;
        var e = new double[3, 16];
        float lo = 0, mid = 0;
        float aLo = Dsp.OnePole(200f), aMid = Dsp.OnePole(2000f);
        for (var i = 0; i < buf.Count / 2; i++)
        {
            var x = 0.5f * (buf[i * 2] + buf[i * 2 + 1]);
            lo += aLo * (x - lo);
            mid += aMid * (x - mid);
            float b0 = lo, b1 = mid - lo, b2 = x - mid;
            var pos = (int)((start + i) % fpb / (fpb / 16)) & 15;
            e[0, pos] += b0 * b0;
            e[1, pos] += b1 * b1;
            e[2, pos] += b2 * b2;
        }
        var fp = new double[3 * 16 + 3];
        var total = 0.0;
        for (var b = 0; b < 3; b++)
        {
            double sum = 0, sq = 0;
            for (var s = 0; s < 16; s++)
            {
                sum += e[b, s];
                sq += e[b, s] * e[b, s];
            }
            total += sum;
            var norm = Math.Sqrt(Math.Max(1e-30, sq));
            for (var s = 0; s < 16; s++)
                fp[b * 16 + s] = e[b, s] / norm;
            fp[48 + b] = sum;
        }
        for (var b = 0; b < 3; b++)
            fp[48 + b] = 2.0 * fp[48 + b] / Math.Max(1e-30, total); // shares, weighted up: the spectrum counts as much as a rhythm
        return fp;
    }

    private static double FingerprintDistance(double[] a, double[] b)
    {
        double d = 0;
        for (var i = 0; i < a.Length; i++)
            d += (a[i] - b[i]) * (a[i] - b[i]);
        return Math.Sqrt(d);
    }

    private static void CheckFrenzyClassSound()
    {
        var track = MusicTracks.All.First(t => t.Name == "Tracksuit Tactical");
        var reference = _frenzySfxReference > 0 ? _frenzySfxReference : Audio.CombatReferenceRms().Combined;
        var musicGain = FrenzyLayer.DefaultMusicVolume * FrenzyLayer.DefaultMusicVolume;
        var fps = new Dictionary<WeaponClass, double[]>();
        var arRms6 = 0.0;
        foreach (var c in AllClasses)
        {
            // The levels on the shared track: 1, 2, 4 and 6 (each measured once it has settled), and the fingerprint at 4.
            var f = ClassLayer(c, track);
            var rms = new double[7];
            foreach (var level in new[] { 1, 2, 4, 6 })
            {
                f.Target = level;
                RunLayer(f, 2.5);
                if (f.Level != level)
                    Fail($"{c}: level {level} never came (playing {f.Level})");
                if (level == 4)
                    fps[c] = ClassFingerprint(f, 6);
                rms[level] = RunLayer(f, 2);
            }
            if (f.ClassPlaying != c)
                Fail($"{c}: the layer plays {f.ClassPlaying}");
            if (c == WeaponClass.AssaultRifle)
                arRms6 = rms[6];
            if (!(rms[1] < rms[2] && rms[2] < rms[4] && rms[6] > rms[4] * 1.5))
                Fail($"{c}: the levels don't build up inside the class (rms L1 {rms[1]:0.000}, L2 {rms[2]:0.000}, L4 {rms[4]:0.000}, L6 {rms[6]:0.000})");

            // Level 6 on the class's own track pool (what really plays), against the gunfire-and-explosion reference.
            var own = ClassLayer(c, null, 23);
            own.Target = 6;
            RunLayer(own, 4);
            var ownRms = RunLayer(own, 3);
            var l6 = ownRms * musicGain;
            var shared6 = rms[6] * musicGain;
            ClassLine($"{c} \"{f.StyleName}\": rms L1 {rms[1]:0.000} L2 {rms[2]:0.000} L4 {rms[4]:0.000} L6 {rms[6]:0.000} ({rms[6] / Math.Max(1e-9, arRms6 > 0 ? arRms6 : rms[6]):0.00}x the classic on the same track); "
                      + $"level 6 at default sliders on \"{own.TrackName}\": {l6 / reference:0.00}x the SFX, on the shared track {shared6 / reference:0.00}x (window 1.05-1.60)");
            foreach (var (what, v) in new[] { ($"its own track \"{own.TrackName}\"", l6), ("the shared track", shared6) })
            {
                if (v < reference * 1.05)
                    Fail($"{c}: level 6 on {what} ({v:0.000} rms at the default sliders) doesn't aim above the gunfire and explosions ({reference:0.000}); raise its trim");
                if (v > reference * 1.6)
                    Fail($"{c}: level 6 on {what} ({v:0.000} rms) is far louder than the gunfire and explosions ({reference:0.000}); lower its trim");
            }

            // Deterministic: the same script twice gives the same samples.
            float[] Script()
            {
                var g = ClassLayer(c, track, 31);
                var outp = new List<float>();
                RunLayer(g, 1.5, outp);
                g.Target = 3;
                RunLayer(g, 2, outp);
                g.Target = 6;
                RunLayer(g, 2.5, outp);
                return outp.ToArray();
            }
            if (!Script().AsSpan().SequenceEqual(Script()))
                Fail($"{c}: two runs with the same seed differ");
        }

        // Every pair of classes differs by a margin.
        var classes = AllClasses;
        var min = double.MaxValue;
        var minPair = "";
        var rows = new List<string>();
        for (var i = 0; i < classes.Length; i++)
        {
            var row = new List<string>();
            for (var j = 0; j < classes.Length; j++)
            {
                var d = FingerprintDistance(fps[classes[i]], fps[classes[j]]);
                row.Add($"{d:0.00}");
                if (j > i && d < min)
                {
                    min = d;
                    minPair = $"{classes[i]} / {classes[j]}";
                }
            }
            rows.Add($"{classes[i]} [{string.Join(" ", row)}]");
        }
        ClassLine($"fingerprint distances (level 4, shared track; want each pair at least {FrenzyClassMinDistance}): closest {minPair} {min:0.000}; {string.Join("; ", rows)}");
        if (min < FrenzyClassMinDistance)
            Fail($"classes {minPair} sound too alike (fingerprint distance {min:0.000}, want at least {FrenzyClassMinDistance})");
    }

    private static void CheckFrenzyClassSwitch()
    {
        var track = MusicTracks.All.First(t => t.Name == "Tracksuit Tactical");
        var f = ClassLayer(WeaponClass.Pistol, track, 41);
        f.Target = 4;
        RunLayer(f, 3);
        var fpb = f.Renderer.FramesPerBar;
        var blockSeconds = MusicPlayer.BlockFrames / (double)Audio.SampleRate;

        // Ask for Shotgun mid-bar: nothing changes until the next bar line, then the crossfade starts there.
        f.Class = WeaponClass.Shotgun;
        var waited = 0.0;
        var steady = new List<float>();
        while (f.ClassSwitches == 0 && waited < 4)
        {
            RunLayer(f, blockSeconds, steady);
            waited += blockSeconds;
        }
        var bars = f.ClassChangeFrame / fpb;
        var onGrid = Math.Abs(bars - Math.Round(bars)) * fpb <= 2.0;
        if (f.ClassSwitches != 1 || !onGrid)
            Fail($"the class switch started at frame {f.ClassChangeFrame} ({bars:0.000} bars; switches {f.ClassSwitches}): not on a bar line");
        if (waited > fpb / Audio.SampleRate + 2 * blockSeconds)
            Fail($"the class switch waited {waited:0.00} s (more than a bar)");
        if (f.ClassPlaying != WeaponClass.Shotgun || f.Crossfade >= 0.5f)
            Fail($"the switch didn't start a crossfade (playing {f.ClassPlaying}, crossfade {f.Crossfade:0.00})");

        // A request for another class during the crossfade waits for the next bar line too.
        f.Class = WeaponClass.Sniper;
        var mid = new List<float>();
        var halfBar = fpb / 2 / Audio.SampleRate;
        RunLayer(f, halfBar - blockSeconds, mid);
        var xfHalf = f.Crossfade;
        if (Math.Abs(xfHalf - 0.5f) > 0.08f || f.ClassPlaying != WeaponClass.Shotgun || f.ClassSwitches != 1)
            Fail($"half a bar into the switch: crossfade {xfHalf:0.00} (want about 0.5), playing {f.ClassPlaying}, switches {f.ClassSwitches}");
        var t = 0.0;
        while (f.ClassSwitches < 2 && t < 4)
        {
            RunLayer(f, blockSeconds, mid);
            t += blockSeconds;
        }
        var bars2 = f.ClassChangeFrame / fpb;
        if (f.ClassSwitches != 2 || f.ClassPlaying != WeaponClass.Sniper || Math.Abs(bars2 - bars - 1) * fpb > 2.0)
            Fail($"the second switch: {f.ClassSwitches} switches, playing {f.ClassPlaying}, {bars2 - bars:0.000} bars after the first (want exactly 1)");

        // Smooth: quarter-note loudness through the switches never jumps far past what the styles do on their own.
        f.Class = WeaponClass.Sniper;
        var after = new List<float>();
        RunLayer(f, 3, after);
        if (f.Crossfade < 1f)
            Fail($"the crossfade never finished ({f.Crossfade:0.00})");
        var beat = (int)(fpb / 4);
        double MaxBeat(List<float> s)
        {
            var m = 0.0;
            for (var i = 0; i + beat * 2 <= s.Count; i += beat * 2)
            {
                double q = 0;
                for (var j = i; j < i + beat * 2; j++)
                    q += s[j] * (double)s[j];
                m = Math.Max(m, Math.Sqrt(q / (beat * 2)));
            }
            return m;
        }
        var sg = ClassLayer(WeaponClass.Shotgun, track, 41);
        sg.Target = 4;
        RunLayer(sg, 3);
        var shotgun = new List<float>();
        RunLayer(sg, 3, shotgun);
        var maxSteady = Math.Max(Math.Max(MaxBeat(steady), MaxBeat(after)), MaxBeat(shotgun));
        var maxSwitch = MaxBeat(mid);
        ClassLine($"class switch Pistol -> Shotgun -> Sniper at level 4: first on bar {bars:0.000} after {waited:0.00} s, crossfade {xfHalf:0.00} half a bar in, second exactly {bars2 - bars:0.000} bars later; "
                  + $"loudest beat during the switches {maxSwitch:0.000} vs {maxSteady:0.000} steady");
        if (maxSwitch > maxSteady * 1.3)
            Fail($"a beat during the class switch is {maxSwitch / maxSteady:0.00}x louder than the styles on their own");

        // A class picked while silent starts straight in its style (no crossfade), and death fades a class style out too.
        var g = new FrenzyLayer(43) { Class = WeaponClass.LMG, TestTrack = track };
        g.Target = 2;
        RunLayer(g, 1);
        if (g.ClassPlaying != WeaponClass.LMG || g.ClassSwitches != 0 || g.Crossfade < 1f)
            Fail($"a frenzy starting with LMG plays {g.ClassPlaying} ({g.ClassSwitches} switches, crossfade {g.Crossfade:0.00})");
        g.Died();
        g.Target = 0;
        RunLayer(g, FrenzyLayer.QuickFadeSeconds + 0.1);
        if (g.Active || RunLayer(g, 0.3) > 0)
            Fail("dying didn't silence a class style");

        // Allocation-free: climbing, switching classes and fading, after a warm-up pass.
        var h = new FrenzyLayer(47) { TestTrack = track };
        var block = new float[MusicPlayer.BlockFrames * 2];
        long Pass()
        {
            h.Class = WeaponClass.Special;
            h.Target = 1;
            RunLayer(h, 0.5);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var b = 0; b < 600; b++)
            {
                h.Target = b < 100 ? 2 : b < 250 ? 4 : b < 400 ? 6 : 0;
                h.Class = AllClasses[(b / 60) % AllClasses.Length];
                if (b == 500)
                    h.Died();
                Array.Clear(block);
                h.MixInto(block, true);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            h.Target = 0;
            RunLayer(h, FrenzyLayer.QuickFadeSeconds + 0.5);
            return allocated;
        }
        var warm = Pass();
        var bytes = Math.Max(Pass(), Pass());
        ClassLine($"allocations while mixing with class switches: {bytes} bytes per 600 blocks after warm-up (first pass {warm}); {h.ClassSwitches} switches");
        if (bytes > 0)
            Fail($"the class styles allocated {bytes} bytes while mixing (want 0)");
        if (h.ClassSwitches < 6)
            Fail($"the allocation run only switched classes {h.ClassSwitches} times");
    }

    /// <summary>
    /// CPU per class at levels 4 and 6 against the heaviest hardbass track: the median ratio of interleaved runs on this
    /// thread's CPU clock (<see cref="CompareCpu"/>), so the check holds on a noisy 2-core CI runner as on a quiet PC.
    /// </summary>
    private static void CheckFrenzyClassCpu()
    {
        const double seconds = 1.5;
        var r = new MusicRenderer();
        var hb = MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass).ToArray();
        var (heaviest, _) = HeaviestTrack(hb, t => TrackWork(r, t, seconds));
        var track = TrackWork(r, heaviest, seconds);
        var parts = new List<string>();
        var worst = 0.0;
        var worstMs = 0.0;
        var heaviestCost = 0.0;
        var worstAt = "";
        foreach (var c in AllClasses)
        {
            var c4 = CompareCpu(track, FrenzyWork(c, 4, seconds), 7);
            var c6 = CompareCpu(track, FrenzyWork(c, 6, seconds), 7);
            var top = Math.Max(c4.Ratio, c6.Ratio);
            var topMs = Math.Max(c4.CandMs, c6.CandMs) / seconds;
            heaviestCost = Math.Max(heaviestCost, c4.RefMs / seconds);
            if (top > worst)
                (worst, worstAt) = (top, c.ToString());
            worstMs = Math.Max(worstMs, topMs);
            parts.Add($"{c} {c4.CandMs / seconds:0.00}/{c6.CandMs / seconds:0.00} = {c4.Ratio:0.00}x/{c6.Ratio:0.00}x");
            if (top > FrenzyCpuRatio)
                Fail($"{c}: the frenzy costs {top:0.00}x the heaviest track ({heaviest.Name}; medians {topMs:0.00} vs {c4.RefMs / seconds:0.00} ms per s; allowed {FrenzyCpuRatio}x)");
            if (topMs > MusicBudgetMsPerSecond)
                Fail($"{c}: the frenzy costs {topMs:0.0} ms per s (budget {MusicBudgetMsPerSecond})");
        }
        ClassLine($"CPU per class ({ThreadCpu.Kind}, median of interleaved pairs), ms per s of audio at level 4/6 and the ratio to the heaviest hardbass track "
                  + $"({heaviest.Name}, {heaviestCost:0.00}): {string.Join(", ", parts)}; worst {worst:0.00}x ({worstAt}; allowed {FrenzyCpuRatio}x) = {worstMs / 10:0.00}% of a core");
        CpuRatioNotes.Add($"frenzy classes: worst {worst:0.00}x ({worstAt}) of {heaviest.Name}");
        ClassLine($"music CPU ratios ({ThreadCpu.Kind}; the user's rule: at most {FrenzyCpuRatio}x): {string.Join("; ", CpuRatioNotes)}");
    }
}
