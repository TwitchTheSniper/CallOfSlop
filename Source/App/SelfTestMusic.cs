using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Music": the procedural menu and lobby tracks render headlessly (no audio device). Every track
/// plays start to finish without NaNs or heavy clipping, its drops are louder than its breakdowns, tracks differ from
/// each other, rendering is deterministic for a seed, allocation-free and cheap. The player shuffles without
/// repeats, crossfades between tracks and moods and goes silent for live matches, and the music volume setting
/// loads from old and new settings files (temporary files only; the real settings.txt is never touched).
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// The CPU budget: at most this many ms of rendering per second of music on the CI runner (a generous cap for a slow,
    /// shared machine; the classic tracks measure about 7 ms per second there and 0.1-0.5% of a core on a gaming PC).
    /// Raised from 50 to 75 when the harder hardbass tracks came in: a track may now cost up to 1.5x the heaviest older
    /// one (the user's rule, checked on its own in CheckHardbassHarder), keeping all music under ~1% of a core on a PC.
    /// A track over the cap is re-timed (median of several runs) before it fails, so one noisy reading never fails CI.
    /// </summary>
    private const double MusicBudgetMsPerSecond = 75;

    private static void CheckMusic()
    {
        const int sr = Audio.SampleRate;
        var all = MusicTracks.All;
        var menu = MusicTracks.For(MusicSet.Menu);
        var lobby = MusicTracks.For(MusicSet.Lobby);
        Line($"{menu.Length} menu tracks, {lobby.Length} lobby tracks");
        if (menu.Length < 4)
            Fail($"only {menu.Length} menu tracks (want 4+)");
        if (lobby.Length < 3)
            Fail($"only {lobby.Length} lobby tracks (want 3+)");
        if (all.Select(t => t.Name).Distinct().Count() != all.Length)
            Fail("two tracks share a name");
        if (all.Select(t => (t.Bpm, t.Key)).Distinct().Count() != all.Length)
            Fail("two tracks share both tempo and key");
        foreach (var t in all)
        {
            if (t.Style != MusicStyle.Hardbass && t.Bpm is < 135 or > 180)
                Fail($"{t.Name}: {t.Bpm} BPM is outside 135-180");
            if (t.Scale.Length != 7 || t.Chords.Length == 0)
                Fail($"{t.Name}: bad scale or chords");
            if (!t.Sections.Any(s => s.Part == MusicPart.Drop) || !t.Sections.Any(s => s.Part == MusicPart.Build))
                Fail($"{t.Name}: the arrangement needs a build and a drop ({t.Form})");
            if (t.Sections.Sum(s => s.Bars) != t.TotalBars || t.Seconds is < 60 or > 300)
                Fail($"{t.Name}: odd length {t.Seconds:0} s ({t.Form})");
        }

        // ---- Every track start to finish ----------------------------------------------------------
        var r = new MusicRenderer();
        var block = new float[MusicPlayer.BlockFrames * 2];
        r.Load(all[0], 1);
        for (var i = 0; i < 40; i++)
            r.Render(block); // warm up the JIT before timing anything

        var dropWindows = new float[all.Length][];
        var stats = new MusicTrackStats[all.Length];
        double renderMs = 0, renderSeconds = 0;
        var costs = new System.Text.StringBuilder();
        for (var ti = 0; ti < all.Length; ti++)
        {
            var t = all[ti];
            var partSum = new double[5];
            var partCount = new long[5];
            long nan = 0, clipped = 0, samples = 0;
            float peak = 0;
            var dropStart = (long)(t.BarStart(t.FirstDropBar) * sr);
            var drop = new float[sr * 3];
            dropWindows[ti] = drop;
            var frame = 0L;
            r.Load(t, 1234);
            long ticks = 0; // only the rendering is timed, not the analysis below
            var guard = (long)((t.Seconds + 10) * sr);
            while (!r.Finished && frame < guard)
            {
                var t0 = Stopwatch.GetTimestamp();
                r.Render(block);
                ticks += Stopwatch.GetTimestamp() - t0;
                var bar = t.BarAt(frame / (double)sr); // weird tracks have bars of different lengths
                var part = PartAt(t, bar);
                for (var i = 0; i < block.Length; i += 2)
                {
                    float a = block[i], b = block[i + 1];
                    if (!float.IsFinite(a) || !float.IsFinite(b))
                    {
                        nan++;
                        continue;
                    }
                    var m = MathF.Max(MathF.Abs(a), MathF.Abs(b));
                    peak = MathF.Max(peak, m);
                    if (m > 0.99f)
                        clipped++;
                    samples++;
                    if (part is { } p)
                    {
                        partSum[(int)p] += 0.5 * (a * a + b * b);
                        partCount[(int)p]++;
                    }
                    var f = frame + i / 2 - dropStart;
                    if (f >= 0 && f < drop.Length)
                        drop[f] = 0.5f * (a + b);
                }
                frame += block.Length / 2;
            }
            var finished = r.Finished; // (a re-timing below reloads the renderer)
            var ms = ticks * 1000.0 / Stopwatch.Frequency;
            var seconds = frame / (double)sr;
            renderMs += ms;
            renderSeconds += seconds;
            costs.Append($" {t.Name} {ms / seconds:0.0} (rms intro {Rms(MusicPart.Intro):0.00} drop {Rms(MusicPart.Drop):0.00} break {Rms(MusicPart.Break):0.00});");
            double Rms(MusicPart p) => partCount[(int)p] == 0 ? 0 : Math.Sqrt(partSum[(int)p] / partCount[(int)p]);
            var clipPct = samples == 0 ? 0 : 100.0 * clipped / samples;
            stats[ti] = new MusicTrackStats(Rms(MusicPart.Intro), Rms(MusicPart.Build), Rms(MusicPart.Drop), Rms(MusicPart.Break), peak, clipped, ms / seconds,
                                            t.Style == MusicStyle.Hardbass ? r.LimiterMinGain : 1f);
            if (ms / seconds > MusicBudgetMsPerSecond)
            {
                // A busy runner can stall one pass: re-time a few seconds of its drop and take the median before failing.
                var median = MedianMsPerSecond(r, t, 5);
                Line($"{t.Name}: {ms / seconds:0.0} ms per s over the budget on the first pass; median of 5 re-timed runs {median:0.0}");
                renderMs += (median - ms / seconds) * seconds;
                if (median > MusicBudgetMsPerSecond)
                    Fail($"{t.Name}: rendering costs {median:0.0} ms per second of audio (median of 5 runs; budget {MusicBudgetMsPerSecond} for every track)");
            }
            Line($"{t.Name,-24} {t.Set,-5} {t.Style,-11} {t.Bpm,3:0} BPM  {t.Key,-21} {seconds,4:0} s  rms intro {Rms(MusicPart.Intro):0.000} "
                 + $"build {Rms(MusicPart.Build):0.000} drop {Rms(MusicPart.Drop):0.000} break {Rms(MusicPart.Break):0.000} outro {Rms(MusicPart.Outro):0.000}  "
                 + $"peak {peak:0.000} clipped {clipPct:0.000}%  {ms / seconds:0.0} ms per s");
            if (!finished)
                Fail($"{t.Name}: never finished (rendered {seconds:0} s of {t.Seconds:0} s)");
            else if (Math.Abs(seconds - t.Seconds) > 1.0)
                Fail($"{t.Name}: finished after {seconds:0.0} s, expected {t.Seconds:0.0} s");
            if (nan > 0)
                Fail($"{t.Name}: {nan} NaN/infinite samples");
            if (peak > 1.0001f)
                Fail($"{t.Name}: peak {peak} above full scale");
            if (clipPct > 0.5)
                Fail($"{t.Name}: {clipPct:0.00}% of samples at the limiter's ceiling (want < 0.5%)");
            if (Rms(MusicPart.Drop) < 0.08)
                Fail($"{t.Name}: drops too quiet (rms {Rms(MusicPart.Drop):0.000})");
            if (Rms(MusicPart.Intro) < 0.01)
                Fail($"{t.Name}: intro is (nearly) silent");
            if (Rms(MusicPart.Break) > 0 && Rms(MusicPart.Drop) < Rms(MusicPart.Break) * 1.2)
                Fail($"{t.Name}: drops ({Rms(MusicPart.Drop):0.000}) aren't clearly louder than the breakdown ({Rms(MusicPart.Break):0.000})");
            if (Rms(MusicPart.Drop) < Rms(MusicPart.Intro))
                Fail($"{t.Name}: the intro ({Rms(MusicPart.Intro):0.000}) is louder than the drops ({Rms(MusicPart.Drop):0.000})");
            if (Rms(MusicPart.Drop) > 0.6)
                Fail($"{t.Name}: drops are crushed (rms {Rms(MusicPart.Drop):0.000})");
        }
        Line($"track costs (ms per s of audio):{costs}");
        var msPerSecond = renderMs / renderSeconds;
        Line($"rendering cost: {msPerSecond:0.00} ms per second of music ({msPerSecond / 100:0.000} ms per 10 ms, {msPerSecond / 10:0.00}% of a core); budget {MusicBudgetMsPerSecond} ms");
        if (msPerSecond > MusicBudgetMsPerSecond)
            Fail($"music rendering costs {msPerSecond:0.0} ms per second of audio (budget {MusicBudgetMsPerSecond})");

        // ---- Tracks differ ---------------------------------------------------------------------------
        var maxCorr = 0.0;
        for (var a = 0; a < all.Length; a++)
            for (var b = a + 1; b < all.Length; b++)
            {
                var c = Math.Abs(Correlation(dropWindows[a], dropWindows[b]));
                maxCorr = Math.Max(maxCorr, c);
                if (c > 0.5)
                    Fail($"{all[a].Name} and {all[b].Name} sound alike (drop correlation {c:0.00})");
            }
        Line($"largest drop-to-drop correlation between two tracks: {maxCorr:0.000}");

        // ---- Deterministic for a seed, and allocation-free ----------------------------------------------
        foreach (var t in new[] { menu[0], lobby[^1] })
        {
            var x = RenderSeconds(new MusicRenderer(), t, 99, t.FirstDropBar, 2);
            var y = RenderSeconds(r, t, 99, t.FirstDropBar, 2);
            if (!x.AsSpan().SequenceEqual(y))
                Fail($"{t.Name}: two renders with the same seed differ");
        }
        void LoadAndRender()
        {
            r.Load(lobby[0], 5, lobby[0].FirstDropBar);
            for (var i = 0; i < 44; i++)
                r.Render(block);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        LoadAndRender();
        // Only if this window saw bytes: the least of it and three more identical rounds (SelfTestCpuRatio.cs), so a one-off runtime allocation can't fail it.
        var allocated = RecheckAllocBytes(GC.GetAllocatedBytesForCurrentThread() - before, LoadAndRender);
        Line($"load + 1 s render allocated {allocated} bytes");
        if (allocated > 0)
            Fail($"music rendering allocated {allocated} bytes (want 0)");

        CheckHardbass(stats, r, block);
        CheckMusicPlayer();
        CheckMusicPlaylist(menu, lobby);
        CheckMusicPlaylist(MusicTracks.For(MusicSet.Menu, true), MusicTracks.For(MusicSet.Lobby, true));
        CheckMusicSettings();
        CheckMusicStyleSetting();
    }

    private readonly record struct MusicTrackStats(double Intro, double Build, double Drop, double Break, float Peak, long Clipped, double MsPerSecond, float LimiterMinGain);

    /// <summary>Loudness window (RMS) for a hardbass drop: loud, but not a brick.</summary>
    private const double HardbassDropRmsMin = 0.3, HardbassDropRmsMax = 0.56;

    /// <summary>
    /// The hardbass tracks: enough of them in both sets, the tempo rules (145-165 BPM, plus one 200 BPM monster), each
    /// one distinct, loud within a window but never past the limiter's ceiling, harder than the older tracks, drops well
    /// above the intros, a kick and off-beat bass that never smear into each other, deterministic and allocation-free;
    /// then the new voices (chants, reverse bass, clipped kick) and the master limiter on their own.
    /// </summary>
    private static void CheckHardbass(MusicTrackStats[] stats, MusicRenderer r, float[] block)
    {
        var all = MusicTracks.All;
        var hb = all.Where(t => t.Style == MusicStyle.Hardbass).ToArray();
        var notes = new List<string>();
        Line($"hardbass: {hb.Length} tracks ({hb.Count(t => t.Set == MusicSet.Menu)} menu, {hb.Count(t => t.Set == MusicSet.Lobby)} lobby)");
        if (hb.Length < 6)
            Fail($"only {hb.Length} hardbass tracks (want 5 + the 200 BPM one)");
        if (hb.Count(t => t.Set == MusicSet.Menu) < 2 || hb.Count(t => t.Set == MusicSet.Lobby) < 2)
            Fail("both the menu and the lobby need at least two hardbass tracks");
        if (hb.Count(t => t.Bpm is >= 195 and <= 205) != 1)
            Fail($"want exactly one ~200 BPM hardbass track (have {hb.Count(t => t.Bpm is >= 195 and <= 205)})");
        foreach (var t in hb)
        {
            // Classic hardbass is 145-165 BPM; the harder tracks go to 175, plus one 210-225 BPM monster.
            if (t.Bpm is not (>= 145 and <= 165) && t.Bpm is not (>= 195 and <= 205) && (t.Hardbass?.Harder == null || t.Bpm is not ((>= 160 and <= 175) or (>= 210 and <= 225))))
                Fail($"{t.Name}: {t.Bpm} BPM (hardbass is 145-165, or the 200 BPM track; harder tracks 160-175 or 210-225)");
            if (t.Hardbass is not { } f)
            {
                Fail($"{t.Name}: no hardbass recipe");
                continue;
            }
            if (f.BassA.Length != 16 || f.BassB.Length != 16 || f.Chants.Length != 32 || !f.Chants.Any(c => c != '.'))
                Fail($"{t.Name}: bass patterns need 16 steps and chants 32 (with at least one chant)");
            if (f.BassA.Where((c, i) => c != '.' && i % 4 == 0).Any())
                Fail($"{t.Name}: the bass plays on a kick ({f.BassA}); the hardbass bass sits between the kicks");
        }
        if (hb.Select(t => t.Bpm).Distinct().Count() != hb.Length)
            Fail("two hardbass tracks share a tempo");
        if (hb.Select(t => t.Key).Distinct().Count() != hb.Length)
            Fail("two hardbass tracks share a key");
        if (hb.Select(t => (t.Hardbass!.Lead, t.Hardbass!.LaterLead, t.Hardbass!.BassA, t.Hardbass!.Reverse)).Distinct().Count() != hb.Length)
            Fail("two hardbass tracks share the same lead sounds, bassline and reverse bass");
        foreach (var lead in Enum.GetValues<HardbassLead>())
            if (!hb.Any(t => t.Hardbass!.Lead == lead || t.Hardbass!.LaterLead == lead))
                Fail($"no hardbass track uses the {lead} lead");
        if (!hb.Any(t => t.Hardbass!.Reverse != ReverseBassUse.Never))
            Fail("no hardbass track uses the reverse bass");

        // Loudness, limiter, drops against intros, CPU.
        double hbDrop = 0, oldDrop = 0;
        var oldCount = 0;
        for (var i = 0; i < all.Length; i++)
        {
            var t = all[i];
            var st = stats[i];
            if (t.Style != MusicStyle.Hardbass)
            {
                oldDrop += st.Drop;
                oldCount++;
                continue;
            }
            hbDrop += st.Drop;
            notes.Add($"{t.Name} ({t.Set}, {t.Bpm:0} BPM, {t.Key}): {st.MsPerSecond:0.0} ms/s, rms intro {st.Intro:0.000} drop {st.Drop:0.000} break {st.Break:0.000}, "
                      + $"peak {st.Peak:0.000}, limiter down to {st.LimiterMinGain:0.00}");
            if (st.Peak > MasterLimiter.SafetyCeiling + 1e-4f || st.Clipped > 0)
                Fail($"{t.Name}: peak {st.Peak:0.0000}, {st.Clipped} samples above 0.99 (the limiter should keep it under {MasterLimiter.SafetyCeiling})");
            if (st.Drop is < HardbassDropRmsMin or > HardbassDropRmsMax)
                Fail($"{t.Name}: drop rms {st.Drop:0.000} outside {HardbassDropRmsMin}-{HardbassDropRmsMax}");
            if (st.Drop < st.Intro * 1.25)
                Fail($"{t.Name}: drops ({st.Drop:0.000}) should hit at least 1.25x harder than the intro ({st.Intro:0.000})");
            if (st.LimiterMinGain > 0.97f)
                Fail($"{t.Name}: the master limiter never worked (min gain {st.LimiterMinGain:0.00}); the mix isn't driven");
            if (st.LimiterMinGain < 0.25f)
                Fail($"{t.Name}: the limiter pulls {st.LimiterMinGain:0.00}x: the mix is far too hot");
            var (kickTail, bassTail) = r.GrooveTails(t);
            notes[^1] += $", kick at the 8th {kickTail:0.000}, bass at the next kick {bassTail:0.000}";
            if (kickTail > 0.1f || bassTail > 0.1f)
                Fail($"{t.Name}: the kick ({kickTail:0.000} at the next 8th) or bass ({bassTail:0.000} at the next kick) smears (want < 0.1)");
        }
        hbDrop /= Math.Max(1, hb.Length);
        oldDrop /= Math.Max(1, oldCount);
        Line($"average drop rms: hardbass {hbDrop:0.000}, the other tracks {oldDrop:0.000}");
        notes.Add($"average drop rms: hardbass {hbDrop:0.000}, others {oldDrop:0.000}; heaviest track {stats.Max(x => x.MsPerSecond):0.0} ms/s (budget {MusicBudgetMsPerSecond})");
        if (hbDrop < oldDrop)
            Fail($"hardbass drops ({hbDrop:0.000}) aren't harder than the other tracks' ({oldDrop:0.000})");

        // Deterministic for a seed (from the drop and from the last drop, where reverse basses and later leads play),
        // different for another seed somewhere in the track, and allocation-free.
        foreach (var t in hb)
        {
            var lastDrop = LastDropBar(t);
            foreach (var bar in new[] { t.FirstDropBar, lastDrop })
            {
                var x = RenderSeconds(new MusicRenderer(), t, 77, bar, 2);
                var y = RenderSeconds(r, t, 77, bar, 2);
                if (!x.AsSpan().SequenceEqual(y))
                    Fail($"{t.Name}: two renders from bar {bar} with the same seed differ");
                if (Measure(x).Rms < 0.1)
                    Fail($"{t.Name}: bar {bar} is too quiet (rms {Measure(x).Rms:0.000})");
            }
            void LoadAndRender()
            {
                r.Load(t, 3, lastDrop);
                for (var i = 0; i < 44; i++)
                    r.Render(block);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            LoadAndRender();
            // Only if this window saw bytes: the least of it and three more identical rounds (SelfTestCpuRatio.cs), so a one-off runtime allocation can't fail it.
            var allocated = RecheckAllocBytes(GC.GetAllocatedBytesForCurrentThread() - before, LoadAndRender);
            if (allocated > 0)
                Fail($"{t.Name}: load + 1 s render allocated {allocated} bytes (want 0)");
        }

        CheckHardbassVoices(notes);
        CheckHardbassHarder(stats, r, notes); // SelfTestHardbassHarder.cs
        foreach (var n in notes)
            Line("hardbass: " + n); // log lines, not a notice: a CI step shows only 10 notices and the DONE line must stay visible
    }

    private static int LastDropBar(MusicTrack t)
    {
        int bar = 0, last = 0;
        foreach (var (part, bars) in t.Sections)
        {
            if (part == MusicPart.Drop)
                last = bar;
            bar += bars;
        }
        return last;
    }

    /// <summary>The new synth voices on their own: chants, reverse bass, the clipped kick and the master limiter.</summary>
    private static void CheckHardbassVoices(List<string> notes)
    {
        const int sr = Audio.SampleRate;
        var rng = new MusicRng(5);

        // Chants: every word is loud enough, finite, ends, and sounds unlike the others.
        var words = Enum.GetValues<VocalWord>();
        var shouts = new float[words.Length][];
        var vox = new VocalVoice();
        for (var w = 0; w < words.Length; w++)
        {
            vox.Reset();
            vox.Trigger(words[w], 160f, 1f);
            var buf = new float[sr / 2];
            for (var i = 0; i < buf.Length; i += 512)
                vox.Render(buf.AsSpan(i, Math.Min(512, buf.Length - i)), ref rng);
            shouts[w] = buf;
            double sum = 0;
            float peak = 0;
            var finite = true;
            for (var i = 0; i < sr / 4; i++)
            {
                finite &= float.IsFinite(buf[i]);
                sum += buf[i] * (double)buf[i];
                peak = MathF.Max(peak, MathF.Abs(buf[i]));
            }
            var rms = Math.Sqrt(sum / (sr / 4));
            var tail = buf.AsSpan(sr * 9 / 20).ToArray().Max(MathF.Abs);
            notes.Add($"chant {words[w]}: rms {rms:0.000}, peak {peak:0.000}");
            if (!finite || rms < 0.05 || peak > 1.0001f)
                Fail($"chant {words[w]}: rms {rms:0.000}, peak {peak:0.000}, finite {finite}");
            if (tail > 1e-3f)
                Fail($"chant {words[w]} doesn't end (level {tail:0.0000} after 0.45 s)");
        }
        for (var a = 0; a < words.Length; a++)
            for (var b = a + 1; b < words.Length; b++)
            {
                var c = Math.Abs(Correlation(shouts[a], shouts[b]));
                if (c > 0.6)
                    Fail($"chants {words[a]} and {words[b]} sound alike (correlation {c:0.00})");
            }

        // Reverse bass: swells (the second half much louder than the first) and is silent soon after its length.
        var rev = new ReverseBassVoice();
        rev.Configure();
        rev.Reset();
        rev.Trigger(55f, 0.25f, 1f);
        var rb = new float[sr * 3 / 10];
        for (var i = 0; i < rb.Length; i += 512)
            rev.Render(rb.AsSpan(i, Math.Min(512, rb.Length - i)));
        var len = sr / 4;
        double first = 0, second = 0;
        for (var i = 0; i < len / 2; i++)
            first += rb[i] * (double)rb[i];
        for (var i = len / 2; i < len; i++)
            second += rb[i] * (double)rb[i];
        var after = rb.AsSpan(len + 441).ToArray().Max(MathF.Abs);
        notes.Add($"reverse bass: second half {Math.Sqrt(second / first):0.0}x the first, {after:0.0000} after the cut");
        if (second < first * 4 || rb.Any(x => !float.IsFinite(x)))
            Fail($"the reverse bass doesn't swell (second half {Math.Sqrt(second / Math.Max(first, 1e-12)):0.0}x the first)");
        if (after > 0.01f)
            Fail($"the reverse bass rings on after its cut ({after:0.000})");

        // Clipped kick: harder (more energy) than the same kick without clipping, never past full scale.
        float KickEnergy(float clip)
        {
            var k = new KickVoice { StartHz = 320, EndHz = 50, Hold = 0.02f, DecayTime = 0.03f, Drive = 7, Clip = clip, Punch = 0.3f };
            k.Configure(0.15f);
            k.Reset();
            k.Trigger(1f);
            var kb = new float[sr / 8];
            var duck = new float[kb.Length];
            var kr = new MusicRng(1);
            k.Render(kb, duck, ref kr);
            if (kb.Any(x => !float.IsFinite(x) || MathF.Abs(x) > 1.6f))
                Fail("the kick produced non-finite or wild samples");
            return (float)Math.Sqrt(kb.Sum(x => x * (double)x) / kb.Length);
        }
        var soft = KickEnergy(0);
        var hard = KickEnergy(1.8f);
        notes.Add($"kick rms soft {soft:0.000}, clipped {hard:0.000}");
        if (hard < soft * 1.05f)
            Fail($"the clipped kick ({hard:0.000}) isn't harder than the soft one ({soft:0.000})");

        // Limiter: a quiet signal passes untouched (just delayed); a signal 3x too hot comes out at the ceiling.
        var lim = new MasterLimiter();
        lim.Configure();
        lim.Reset();
        var quiet = new float[4096];
        for (var i = 0; i < quiet.Length; i += 2)
            quiet[i] = quiet[i + 1] = 0.5f * MathF.Sin(i * 0.01f);
        var copy = (float[])quiet.Clone();
        lim.Process(quiet);
        var exact = true;
        for (var i = MasterLimiter.Lookahead * 2; i < quiet.Length; i++)
            exact &= quiet[i] == copy[i - MasterLimiter.Lookahead * 2];
        if (!exact || lim.MinGain < 1f)
            Fail("the limiter changes a signal that's under its ceiling");
        lim.Reset();
        var hot = new float[sr]; // 0.5 s stereo
        for (var i = 0; i < hot.Length; i += 2)
            hot[i] = hot[i + 1] = 3f * MathF.Sin(i * 0.5f * 110f * Dsp.TwoPi / sr) * (i < hot.Length / 2 ? 1f : 0.2f);
        lim.Process(hot);
        var hotPeak = hot.Max(MathF.Abs);
        var steady = hot.AsSpan(sr / 4, sr / 8).ToArray().Max(MathF.Abs);
        notes.Add($"limiter: 3x too hot comes out at peak {hotPeak:0.000}, steady {steady:0.000}");
        if (hotPeak > MasterLimiter.SafetyCeiling + 1e-5f || steady is < 0.85f or > 0.97f)
            Fail($"limiter: peak {hotPeak:0.000} (ceiling {MasterLimiter.SafetyCeiling}), steady level {steady:0.000} (want 0.85-0.97)");
    }

    /// <summary>Settings > Audio > Music style: Mixed for old files, parsed, saved; Hardbass only switches the music over.</summary>
    private static void CheckMusicStyleSetting()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.txt");
            File.WriteAllLines(path, ["name=Old Timer", "volume=0.6", "music_volume=0.4"]); // from before the music style
            var s = Settings.Load(path);
            if (s.MusicChoice != MusicChoice.Mixed || Math.Abs(s.MusicVolume - 0.4f) > 1e-6f)
                Fail($"old settings file: music style {s.MusicChoice} (want Mixed), music volume {s.MusicVolume}");
            foreach (var (text, want) in new[] { ("HardbassOnly", MusicChoice.HardbassOnly), ("hardbassonly", MusicChoice.HardbassOnly), ("Mixed", MusicChoice.Mixed),
                                                 ("7", MusicChoice.Mixed), ("polka", MusicChoice.Mixed), ("", MusicChoice.Mixed) })
            {
                File.WriteAllLines(path, ["volume=0.6", $"music_style={text}"]);
                var m = Settings.Load(path).MusicChoice;
                if (m != want)
                    Fail($"music_style={text} loaded as {m} (want {want})");
            }
            s.MusicChoice = MusicChoice.HardbassOnly;
            s.Save(path);
            if (Settings.Load(path).MusicChoice != MusicChoice.HardbassOnly || !File.ReadAllLines(path).Contains("music_style=HardbassOnly"))
                Fail("music style didn't survive a save and load");
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

        // The player: switching to Hardbass only crossfades into a hardbass track straight away, in the menu and the lobby.
        const int sr = Audio.SampleRate;
        var tenth = new float[sr / 10 * 2];
        for (var seed = 1u; seed <= 6; seed++)
        {
            var p = new MusicPlayer(seed) { Volume = 1f };
            void Mix(double seconds)
            {
                for (var i = 0; i < (int)Math.Round(seconds * 10); i++)
                    p.Mix(tenth);
            }
            p.SetMood(MusicMood.Menu);
            Mix(1.5);
            var first = p.Current;
            p.HardbassOnly = true;
            Mix(4);
            if (p.Current is not { Style: MusicStyle.Hardbass, Set: MusicSet.Menu })
                Fail($"Hardbass only: the menu still plays \"{p.Current?.Name}\" ({p.Current?.Style})");
            else if (first?.Style == MusicStyle.Hardbass && p.Current != first)
                Fail($"Hardbass only: \"{first.Name}\" was already hardbass but got replaced");
            p.SetMood(MusicMood.Lobby);
            Mix(4);
            if (p.Current is not { Style: MusicStyle.Hardbass, Set: MusicSet.Lobby })
                Fail($"Hardbass only: the lobby plays \"{p.Current?.Name}\" ({p.Current?.Style})");
            var held = p.Current;
            p.HardbassOnly = false;
            Mix(1);
            if (p.Current != held)
                Fail("switching back to Mixed shouldn't cut the hardbass track short");
            if (seed == 1)
                Line($"Hardbass only: menu \"{first?.Name}\" -> hardbass, lobby \"{held?.Name}\"");
        }
    }

    private static MusicPart? PartAt(MusicTrack t, int bar)
    {
        var start = 0;
        foreach (var (part, bars) in t.Sections)
        {
            if (bar < start + bars)
                return part;
            start += bars;
        }
        return null;
    }

    private static float[] RenderSeconds(MusicRenderer r, MusicTrack t, uint seed, int startBar, double seconds)
    {
        r.Load(t, seed, startBar);
        var buf = new float[(int)(seconds * Audio.SampleRate) * 2];
        for (var i = 0; i < buf.Length; i += MusicPlayer.BlockFrames * 2)
            r.Render(buf.AsSpan(i, Math.Min(MusicPlayer.BlockFrames * 2, buf.Length - i)));
        return buf;
    }

    private static double Correlation(float[] a, float[] b)
    {
        double ab = 0, aa = 0, bb = 0;
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            ab += a[i] * (double)b[i];
            aa += a[i] * (double)a[i];
            bb += b[i] * (double)b[i];
        }
        return aa <= 0 || bb <= 0 ? 1 : ab / Math.Sqrt(aa * bb);
    }

    private static (float Peak, double Rms) Measure(float[] buf, int fromFrame = 0)
    {
        float peak = 0;
        double sum = 0;
        for (var i = fromFrame * 2; i < buf.Length; i++)
        {
            peak = MathF.Max(peak, MathF.Abs(buf[i]));
            sum += buf[i] * (double)buf[i];
        }
        return (peak, Math.Sqrt(sum / Math.Max(1, buf.Length - fromFrame * 2)));
    }

    /// <summary>The mood state machine, crossfades, fades to silence and track changes, mixed headlessly.</summary>
    private static void CheckMusicPlayer()
    {
        const int sr = Audio.SampleRate;
        if (MusicPlayer.MoodFor(false, MatchPhase.Playing) != MusicMood.Menu
            || MusicPlayer.MoodFor(true, MatchPhase.Warmup) != MusicMood.Lobby
            || MusicPlayer.MoodFor(true, MatchPhase.Countdown) != MusicMood.Lobby
            || MusicPlayer.MoodFor(true, MatchPhase.Ended) != MusicMood.Lobby
            || MusicPlayer.MoodFor(true, MatchPhase.Playing) != MusicMood.Off)
            Fail("music moods: menu outside a session, lobby in warmup/countdown/results, off while playing");

        var p = new MusicPlayer(42) { Volume = 1f };
        var tenth = new float[sr / 10 * 2];
        void Mix(double seconds)
        {
            for (var i = 0; i < (int)Math.Round(seconds * 10); i++)
                p.Mix(tenth);
        }

        Mix(0.5);
        if (p.Current != null || Measure(tenth).Peak > 0)
            Fail("music plays before any mood is set");
        p.SetMood(MusicMood.Menu);
        Mix(2);
        if (p.Current is not { Set: MusicSet.Menu } || Measure(tenth).Rms < 0.01 || p.NowPlaying != p.Current.Name)
            Fail($"menu music didn't start (track {p.Current?.Name ?? "none"}, rms {Measure(tenth).Rms:0.000})");
        var before = GC.GetAllocatedBytesForCurrentThread();
        Mix(1);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 0)
            Fail($"mixing the music allocated {allocated} bytes per second (want 0)");

        var menuTrack = p.Current?.Name;
        p.SetMood(MusicMood.Lobby);
        Mix(0.5);
        if (p.ActiveDecks != 2)
            Fail($"menu to lobby should crossfade (decks playing: {p.ActiveDecks})");
        Mix(3);
        if (p.Current is not { Set: MusicSet.Lobby } || p.ActiveDecks != 1)
            Fail($"lobby music didn't take over (track {p.Current?.Name ?? "none"}, decks {p.ActiveDecks})");
        Line($"menu \"{menuTrack}\" crossfaded into lobby \"{p.Current?.Name}\"");

        p.SetMood(MusicMood.Off); // the match goes live
        Mix(2);
        if (Measure(tenth).Peak > 1e-4f || p.ActiveDecks != 0 || p.NowPlaying != null)
            Fail($"music didn't fade out for the live match (peak {Measure(tenth).Peak:0.0000}, decks {p.ActiveDecks})");
        p.SetMood(MusicMood.Lobby); // results screen
        Mix(2);
        if (p.Current is not { Set: MusicSet.Lobby } || Measure(tenth).Rms < 0.01)
            Fail("lobby music didn't come back on the results screen");

        p.Volume = 0f;
        Mix(0.2);
        if (Measure(tenth).Peak > 1e-4f)
            Fail("music volume 0 isn't silent");
        p.Volume = 1f;

        // Let a track play out: the next one crossfades in, and it's a different track.
        p.SetMood(MusicMood.Menu);
        Mix(4);
        var first = p.Current;
        var sawCrossfade = false;
        for (var i = 0; i < 3200 && p.Current == first; i++)
        {
            p.Mix(tenth);
            if (p.ActiveDecks == 2)
                sawCrossfade = true;
        }
        if (p.Current == first || p.Current == null)
            Fail($"\"{first?.Name}\" never handed over to the next track");
        else
        {
            Mix(0.5);
            sawCrossfade |= p.ActiveDecks == 2;
            if (!sawCrossfade)
                Fail("no crossfade between consecutive tracks");
            Line($"\"{first?.Name}\" played out into \"{p.Current.Name}\" (crossfade: {sawCrossfade}); player mixing cost {p.RenderMsPerSecond:0.00} ms per s");
        }
    }

    private static void CheckMusicPlaylist(MusicTrack[] menu, MusicTrack[] lobby)
    {
        foreach (var set in new[] { menu, lobby })
        {
            var pl = new MusicPlaylist(set, 1234);
            MusicTrack? prev = null;
            var seen = new HashSet<MusicTrack>();
            var repeats = 0;
            for (var i = 0; i < 300; i++)
            {
                var t = pl.Next();
                if (t == prev)
                    repeats++;
                seen.Add(t);
                prev = t;
            }
            if (repeats > 0)
                Fail($"playlist played the same track twice in a row {repeats} time(s)");
            if (seen.Count != set.Length)
                Fail($"playlist only ever played {seen.Count} of {set.Length} tracks");
        }
    }

    /// <summary>The music volume setting: default for old files, parsed, clamped, saved. Temporary files only.</summary>
    private static void CheckMusicSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.txt");
            File.WriteAllLines(path, ["name=Old Timer", "volume=0.6", "vsync=1"]); // a settings file from before music
            var s = Settings.Load(path);
            if (Math.Abs(s.MusicVolume - 0.5f) > 1e-6f || Math.Abs(s.Volume - 0.6f) > 1e-6f || s.Name != "Old Timer")
                Fail($"old settings file: music volume {s.MusicVolume} (want 0.5), volume {s.Volume}, name {s.Name}");
            foreach (var (text, want) in new[] { ("0.3", 0.3f), ("abc", 0.5f), ("7", 1f), ("-1", 0f), ("NaN", 0.5f) })
            {
                File.WriteAllLines(path, ["volume=0.6", $"music_volume={text}"]);
                var m = Settings.Load(path).MusicVolume;
                if (Math.Abs(m - want) > 1e-6f)
                    Fail($"music_volume={text} loaded as {m} (want {want})");
            }
            s.MusicVolume = 0.25f;
            s.Save(path);
            if (Math.Abs(Settings.Load(path).MusicVolume - 0.25f) > 1e-6f || !File.ReadAllLines(path).Contains("music_volume=0.25"))
                Fail("music volume didn't survive a save and load");
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
}
