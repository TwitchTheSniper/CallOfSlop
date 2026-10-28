using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Part of the "Music" self-test section: the harder hardbass tracks (MusicTracksHarder.cs). Enough of them in both
/// sets at the faster tempos (160-175 BPM plus one 210+ BPM track), 90-180 s long, as loud as the classic hardbass
/// tracks, a heavier kick, within 1.5x the CPU cost of the heaviest older track (median timings on the same machine,
/// not a wall-clock cap), and the new voices (siren, pitch riser, crowd) on their own.
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// A harder track's drop may cost this much more than the most expensive older drop: the user's 1.5x rule. (It had
    /// 10% timing slack on top while the check used wall-clock medians; CompareCpu's interleaved CPU-clock ratios don't need it.)
    /// </summary>
    private const double HarderCpuRatioMax = 1.5;

    private static void CheckHardbassHarder(MusicTrackStats[] stats, MusicRenderer r, List<string> notes)
    {
        var all = MusicTracks.All;
        var harder = all.Where(t => t.Hardbass?.Harder != null).ToArray();
        var classic = all.Where(t => t.Style == MusicStyle.Hardbass && t.Hardbass?.Harder == null).ToArray();
        Line($"harder hardbass: {harder.Length} tracks ({harder.Count(t => t.Set == MusicSet.Menu)} menu, {harder.Count(t => t.Set == MusicSet.Lobby)} lobby)");
        if (harder.Length < 4)
            Fail($"only {harder.Length} harder hardbass tracks (want 4+)");
        if (harder.Count(t => t.Set == MusicSet.Menu) < 1 || harder.Count(t => t.Set == MusicSet.Lobby) < 1)
            Fail("the harder hardbass tracks need to be in both the menu and the lobby");
        if (harder.Count(t => t.Bpm is >= 160 and <= 175) < 2)
            Fail("want at least two harder hardbass tracks at 160-175 BPM");
        if (harder.Count(t => t.Bpm >= 210) != 1)
            Fail($"want exactly one 210+ BPM harder hardbass track (have {harder.Count(t => t.Bpm >= 210)})");
        foreach (var t in harder)
        {
            var h = t.Hardbass!.Harder!;
            if (t.Seconds is < 90 or > 180)
                Fail($"{t.Name}: {t.Seconds:0} s long (want 90-180 s)");
            if (h.KickB.Length != 16 || h.KickB[0] == '.' || h.KickB.Count(c => c != '.') < 4)
                Fail($"{t.Name}: the drop's B kick pattern needs 16 steps, a kick on the one and at least four kicks ({h.KickB})");
            if (h.LeadSaws is < 1 or > SawSynth.MaxSaws)
                Fail($"{t.Name}: {h.LeadSaws} lead saws (the synth has {SawSynth.MaxSaws})");
        }

        // Loudness: as loud as the classic hardbass tracks (the drop window itself is checked for every hardbass track).
        double DropAvg(MusicTrack[] set) => set.Length == 0 ? 0 : set.Average(t => stats[Array.IndexOf(all, t)].Drop);
        double classicDrop = DropAvg(classic), harderDrop = DropAvg(harder);
        notes.Add($"harder: average drop rms {harderDrop:0.000} vs classic hardbass {classicDrop:0.000}");
        foreach (var t in harder)
        {
            var d = stats[Array.IndexOf(all, t)].Drop;
            if (d < classicDrop * 0.8 || d > classicDrop * 1.25)
                Fail($"{t.Name}: drop rms {d:0.000} isn't in line with the classic hardbass tracks ({classicDrop:0.000}, want 0.8-1.25x)");
        }

        // The kick: heavier (more energy per hit) than the classic hardbass kicks.
        double KickAvg(MusicTrack[] set) => set.Length == 0 ? 0 : set.Average(t => (double)r.KickRms(t));
        double classicKick = KickAvg(classic), harderKick = KickAvg(harder);
        notes.Add($"harder: kick rms per beat {harderKick:0.000} vs classic {classicKick:0.000}");
        if (harderKick < classicKick * 1.05)
            Fail($"the harder kicks ({harderKick:0.000}) aren't heavier than the classic hardbass kicks ({classicKick:0.000})");

        // CPU: each harder track's drop against the most expensive drop of the older tracks, as the median ratio of
        // interleaved runs on this thread's CPU clock (CompareCpu), so a slow or busy runner shifts both sides alike.
        var older = all.Where(t => t.Hardbass?.Harder == null && t.Weird == null).ToArray(); // the weird tracks are new too
        const double seconds = 1.5;
        var cr = new MusicRenderer();
        var (refTrack, _) = HeaviestTrack(older, t => TrackWork(cr, t, seconds, 1234));
        var refWork = TrackWork(cr, refTrack, seconds, 1234);
        var heaviestWhole = older.Max(t => stats[Array.IndexOf(all, t)].MsPerSecond);
        double worstRatio = 0, refCost = 0;
        var costLine = new System.Text.StringBuilder();
        foreach (var t in harder)
        {
            var cmp = CompareCpu(refWork, TrackWork(cr, t, seconds, 1234));
            var c = cmp.CandMs / seconds;
            refCost = cmp.RefMs / seconds;
            worstRatio = Math.Max(worstRatio, cmp.Ratio);
            var whole = stats[Array.IndexOf(all, t)].MsPerSecond;
            costLine.Append($" {t.Name} drop {c:0.0} ms/s = {cmp.Ratio:0.00}x, whole track {whole:0.0} ms/s = {whole / heaviestWhole:0.00}x;");
            if (cmp.Ratio > HarderCpuRatioMax)
                Fail($"{t.Name}: its drop costs {cmp.Ratio:0.00}x the most expensive older drop ({refTrack.Name}; quickest runs {c:0.0} vs {refCost:0.0} ms/s; want <= {HarderCpuRatioMax}x)");
        }
        notes.Add($"harder: CPU ({ThreadCpu.Kind}, quickest of interleaved runs) against the most expensive older drop ({refTrack.Name}, {refCost:0.0} ms/s) and the heaviest older whole track ({heaviestWhole:0.0} ms/s):{costLine} worst {worstRatio:0.00}x");
        CpuRatioNotes.Add($"harder drops: worst {worstRatio:0.00}x of {refTrack.Name}");

        CheckHarderVoices(notes);
    }

    /// <summary>ms of rendering per second of audio for a few seconds from a track's first drop.</summary>
    private static double TimeMsPerSecond(MusicRenderer r, MusicTrack t, double seconds)
    {
        r.Load(t, 1234, t.FirstDropBar);
        var block = new float[MusicPlayer.BlockFrames * 2];
        var blocks = (int)(seconds * Audio.SampleRate / MusicPlayer.BlockFrames);
        var ms = ThreadCpu.TimeMs(() =>
        {
            for (var i = 0; i < blocks; i++)
                r.Render(block);
        });
        return ms / (blocks * MusicPlayer.BlockFrames / (double)Audio.SampleRate);
    }

    /// <summary>The median of several <see cref="TimeMsPerSecond"/> runs (robust against a stalled CI runner).</summary>
    private static double MedianMsPerSecond(MusicRenderer r, MusicTrack t, int runs)
    {
        var x = new double[runs];
        for (var i = 0; i < runs; i++)
            x[i] = TimeMsPerSecond(r, t, 3);
        Array.Sort(x);
        return x[runs / 2];
    }

    /// <summary>Zero crossings per second over a stretch of a mono buffer.</summary>
    private static double CrossingRate(float[] buf, int from, int length)
    {
        var n = 0;
        for (var i = from + 1; i < from + length; i++)
            if ((buf[i - 1] < 0) != (buf[i] < 0))
                n++;
        return n * (double)Audio.SampleRate / length;
    }

    /// <summary>The siren wails, the pitch riser climbs, both fall silent; the crowd makes a chant bigger.</summary>
    private static void CheckHarderVoices(List<string> notes)
    {
        const int sr = Audio.SampleRate;
        float[] Render(SweepVoice v, int samples)
        {
            var buf = new float[samples];
            for (var i = 0; i < samples; i += 512)
                v.Render(buf.AsSpan(i, Math.Min(512, samples - i)));
            return buf;
        }

        // Siren: a 1 Hz wail of +-7 semitones; the pitch (zero-crossing rate) swings between the quarters of a cycle.
        var siren = new SweepVoice { Hz = 600f, LfoSemis = 7f, LfoHz = 1f, Drive = 3f, ToneHz = 3500f };
        siren.Configure();
        siren.Reset();
        siren.Level = 0.5f;
        var sb = Render(siren, sr);
        var rates = new double[4];
        for (var q = 0; q < 4; q++)
            rates[q] = CrossingRate(sb, q * sr / 4, sr / 4);
        var (peak, rms) = Measure(sb, sr / 4);
        var swing = rates.Max() / Math.Max(1, rates.Min());
        notes.Add($"siren: rms {rms:0.000}, peak {peak:0.000}, pitch swing {swing:0.00}x");
        if (sb.Any(x => !float.IsFinite(x)) || peak > 1f || rms < 0.05)
            Fail($"siren: rms {rms:0.000}, peak {peak:0.000}");
        if (swing < 1.3)
            Fail($"the siren doesn't wail (pitch swing {swing:0.00}x)");
        siren.Level = 0;
        var tail = Render(siren, sr / 2).AsSpan(sr / 4).ToArray().Max(MathF.Abs);
        if (tail > 1e-3f)
            Fail($"the siren rings on after it's turned off ({tail:0.0000})");

        // Pitch riser: steered from 200 Hz to 1600 Hz, it climbs (much higher crossing rate at the end).
        var riser = new SweepVoice { Hz = 200f, Drive = 2f, ToneHz = 6000f, Detune = 0.02f, GlideTime = 0.05f };
        riser.Configure();
        riser.Reset();
        riser.Level = 0.4f;
        var rb = new float[sr];
        for (var i = 0; i < sr; i += 441)
        {
            riser.Hz = 200f * MathF.Pow(2f, 3f * i / sr);
            riser.Render(rb.AsSpan(i, Math.Min(441, sr - i)));
        }
        var climb = CrossingRate(rb, sr * 3 / 4, sr / 4) / Math.Max(1, CrossingRate(rb, sr / 10, sr / 4));
        notes.Add($"pitch riser: climbs {climb:0.0}x");
        if (rb.Any(x => !float.IsFinite(x)) || rb.Max(MathF.Abs) > 1f || climb < 2.5)
            Fail($"the pitch riser doesn't climb (crossing rate {climb:0.0}x from start to end, want 2.5x+)");

        // Crowd: the chant with the octave-down crowd sounds different (a second voice) without getting quieter, and ends.
        float[] Chant(float crowd)
        {
            var vox = new VocalVoice { Crowd = crowd };
            var rng = new MusicRng(5);
            vox.Reset();
            vox.Trigger(VocalWord.Hey, 160f, 1f);
            var buf = new float[sr / 2];
            for (var i = 0; i < buf.Length; i += 512)
                vox.Render(buf.AsSpan(i, Math.Min(512, buf.Length - i)), ref rng);
            if (buf.Any(x => !float.IsFinite(x) || MathF.Abs(x) > 1.0001f))
                Fail($"the chant with crowd {crowd} has non-finite or overloud samples");
            return buf;
        }
        var single = Chant(0);
        var crowded = Chant(0.7f);
        double ChantRms(float[] b) => Math.Sqrt(b.Take(sr / 4).Sum(x => x * (double)x) / (sr / 4));
        var corr = Correlation(single, crowded);
        var crowdTail = crowded.AsSpan(sr * 9 / 20).ToArray().Max(MathF.Abs);
        notes.Add($"crowd chant: rms {ChantRms(crowded):0.000} vs {ChantRms(single):0.000} alone, correlation {corr:0.000}");
        if (corr > 0.97 || ChantRms(crowded) < ChantRms(single) * 0.95)
            Fail($"the crowd doesn't add a voice to the chant (correlation {corr:0.000}, rms {ChantRms(crowded):0.000} vs {ChantRms(single):0.000})");
        if (crowdTail > 1e-3f)
            Fail($"the crowd chant doesn't end ({crowdTail:0.0000} after 0.45 s)");
    }
}
