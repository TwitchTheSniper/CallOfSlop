namespace CallOfSlop;

/// <summary>
/// Self-test section "Frenzy tempo": levels 5 and 6 play faster (MusicFrenzy.Tempo.cs, MusicRenderer.Tempo.cs).
/// For every class and every track its pool can pick, the BPM strictly climbs from level 4 to 5 to 6. Live, per class
/// on a track from its own pool: the bars really get shorter (measured from the bar lines the track plays), each tempo
/// change starts on a bar line, the beat never skips or stumbles (every 16th one 16th after the last), no beat around
/// a change is far louder than the level on its own, the slow-down glides over a bar back to the level's tempo, and
/// nothing allocates. Loudness (level 6 against the SFX) and CPU per class are checked by "Frenzy music" and "Frenzy
/// classes", which now run at the faster tempos.
/// </summary>
public static partial class SelfTest
{
    private static void CheckFrenzyTempo()
    {
        const int sr = Audio.SampleRate;
        var styles = FrenzyStyles.All;
        var tracks = MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)).ToArray();
        MusicTrack[] Pool(FrenzyStyle s)
        {
            var p = FrenzyLayer.PoolTracks(tracks, s.Pool);
            return s.Pool == FrenzyPool.Any || p.Length == 0 ? tracks : p; // as FrenzyLayer.InitClasses picks them
        }

        // The maths: every class, every track it can play.
        var rows = new List<string>();
        foreach (var c in AllClasses)
        {
            var s = styles[(int)c];
            var pool = Pool(s);
            float lo4 = float.MaxValue, hi4 = 0, lo5 = float.MaxValue, hi5 = 0, lo6 = float.MaxValue, hi6 = 0;
            foreach (var t in pool)
            {
                float b4 = FrenzyLayer.LevelBpm(t.Bpm, s, 4), b5 = FrenzyLayer.LevelBpm(t.Bpm, s, 5), b6 = FrenzyLayer.LevelBpm(t.Bpm, s, 6);
                if (FrenzyLayer.LevelBpm(t.Bpm, s, 1) != t.Bpm || b4 != t.Bpm)
                    Fail($"{c} on \"{t.Name}\": levels 1-4 should play the track's own {t.Bpm} BPM");
                if (!(b5 >= b4 * 1.025f && b6 >= b5 * 1.025f))
                    Fail($"{c} on \"{t.Name}\": the BPM doesn't climb clearly: L4 {b4:0.#}, L5 {b5:0.#}, L6 {b6:0.#} (want each at least 2.5% over the last)");
                if (b6 > 250f)
                    Fail($"{c} on \"{t.Name}\": level 6 runs away to {b6:0.#} BPM");
                lo4 = MathF.Min(lo4, b4);
                hi4 = MathF.Max(hi4, b4);
                lo5 = MathF.Min(lo5, b5);
                hi5 = MathF.Max(hi5, b5);
                lo6 = MathF.Min(lo6, b6);
                hi6 = MathF.Max(hi6, b6);
            }
            rows.Add($"{c} x{s.Tempo5:0.00}/x{s.Tempo6:0.00} ({s.Pool}, {pool.Length}): L1-4 {lo4:0.#}-{hi4:0.#}, L5 {lo5:0.#}-{hi5:0.#}, L6 {lo6:0.#}-{hi6:0.#}");
        }
        Line($"frenzy BPM per class (boost share 1 up to {FrenzyLayer.TaperStartBpm} BPM, down to {FrenzyLayer.MinBoost}): {string.Join("; ", rows)}");
        var notes = new List<string>(rows);

        // Live, per class, on the middle-tempo track of its own pool.
        var block = new float[MusicPlayer.BlockFrames * 2];
        var blockSeconds = MusicPlayer.BlockFrames / (double)sr;
        var live = new List<string>();
        foreach (var c in AllClasses)
        {
            var s = styles[(int)c];
            var pool = Pool(s).OrderBy(t => t.Bpm).ToArray();
            var track = pool[pool.Length / 2];
            var f = new FrenzyLayer(61) { Class = c, TestTrack = track };
            var samples = new List<float>();

            void Mix(int blocks)
            {
                for (var b = 0; b < blocks; b++)
                {
                    Array.Clear(block);
                    f.MixInto(block, true);
                    samples.AddRange(block);
                }
            }

            // Plays until the next bar line; the length of the bar that just ended (frames).
            long Bar()
            {
                var n0 = f.BarLinesPlayed;
                for (var guard = 0; f.BarLinesPlayed < n0 + 1 && guard < 400; guard++)
                    Mix(1);
                return f.LatestBarFrames;
            }

            // Asks for a level; plays until it comes, then checks the tempo change sat on a bar line.
            bool Reach(int level)
            {
                f.Target = level;
                var changes = f.TempoChanges;
                for (var guard = 0; f.Level != level && guard < 400; guard++)
                    Mix(1);
                if (f.Level != level)
                {
                    Fail($"{c}: level {level} never came (playing {f.Level})");
                    return false;
                }
                Mix(2); // the bar line's own block is logged by now
                if (f.TempoChanges == changes)
                    Fail($"{c}: going to level {level} didn't change the tempo");
                else if (!f.IsBarLine(f.TempoChangeFrame) || f.TempoChangeFrame != f.LastChangeFrame || (f.Renderer.TempoChangeStep & 15) != 0)
                    Fail($"{c}: the tempo change to level {level} wasn't on a bar line (frame {f.TempoChangeFrame}, level change at {f.LastChangeFrame}, step {f.Renderer.TempoChangeStep})");
                return true;
            }

            // The loudest quarter note in samples[from..to) (interleaved stereo).
            double LoudestBeat(int from, int to, long barFrames)
            {
                var beat = (int)(barFrames / 4) * 2;
                var m = 0.0;
                for (var i = from; i + beat <= to; i += beat)
                {
                    double q = 0;
                    for (var j = i; j < i + beat; j++)
                        q += samples[j] * (double)samples[j];
                    m = Math.Max(m, Math.Sqrt(q / beat));
                }
                return m;
            }

            f.Target = 4;
            Mix((int)(3 / blockSeconds));
            var bar4 = Bar();
            var steady4 = LoudestBeat(samples.Count - (int)bar4 * 4, samples.Count, bar4);
            if (!Reach(5))
                continue;
            var at5 = samples.Count;
            Bar();
            var bar5 = Bar();
            var steady5 = LoudestBeat(samples.Count - (int)bar5 * 4, samples.Count, bar5);
            var up5 = LoudestBeat(Math.Max(0, at5 - (int)bar4 * 4), at5 + (int)bar5 * 4, bar5);
            if (!Reach(6))
                continue;
            var at6 = samples.Count;
            Bar();
            Bar();
            var bar6 = Bar();
            var steady6 = LoudestBeat(samples.Count - (int)bar6 * 4, samples.Count, bar6);
            var up6 = LoudestBeat(Math.Max(0, at6 - (int)bar5 * 4), at6 + (int)bar6 * 4, bar6); // reported: it holds the level-6 crash and roar

            // Easing down: 6 -> 5 glides over a bar back to level 5's tempo, then 5 -> 4 back to the track's own.
            f.Target = 0;
            var from6 = samples.Count;
            for (var guard = 0; f.Level == 6 && guard < 800; guard++)
                Mix(1);
            var tc65 = f.TempoChangeFrame;
            var gliding = f.Renderer.TempoGliding;
            var glideOnBar = f.IsBarLine(tc65) && (f.Renderer.TempoChangeStep & 15) == 0;
            var glideBar = Bar();
            var bpmAfter65 = f.Bpm;
            for (var guard = 0; f.Level == 5 && guard < 800; guard++)
                Mix(1);
            var tc54 = f.TempoChangeFrame;
            var glide54OnBar = f.IsBarLine(tc54) && (f.Renderer.TempoChangeStep & 15) == 0;
            Bar();
            var bpmAfter54 = f.Bpm;
            var down = LoudestBeat(from6, samples.Count, bar5);

            var want4 = FrenzyLayer.LevelBpm(track.Bpm, s, 4);
            var want5 = FrenzyLayer.LevelBpm(track.Bpm, s, 5);
            var want6 = FrenzyLayer.LevelBpm(track.Bpm, s, 6);
            double Measured(long frames) => frames <= 0 ? 0 : 4 * 60.0 * sr / frames;
            double m4 = Measured(bar4), m5 = Measured(bar5), m6 = Measured(bar6);
            live.Add($"{c} on \"{track.Name}\": measured L4 {m4:0.0} L5 {m5:0.0} L6 {m6:0.0} BPM (want {want4:0.0}/{want5:0.0}/{want6:0.0}); "
                     + $"down 6->5 glide bar {Measured(glideBar):0.0} then {bpmAfter65:0.0}, 5->4 then {bpmAfter54:0.0}; "
                     + $"loudest beat around 4->5 {up5 / Math.Max(1e-9, Math.Max(steady4, steady5)):0.00}x steady, 5->6 {up6 / Math.Max(1e-9, Math.Max(steady5, steady6)):0.00}x, easing down {down / Math.Max(1e-9, steady6):0.00}x L6; "
                     + $"{f.StepGlitches} stumbles");
            if (!(m4 < m5 && m5 < m6))
                Fail($"{c} on \"{track.Name}\": the bars don't get faster: L4 {m4:0.0}, L5 {m5:0.0}, L6 {m6:0.0} BPM");
            foreach (var (lv, m, w) in new[] { (4, m4, want4), (5, m5, want5), (6, m6, want6) })
                if (Math.Abs(m - w) > w * 0.003)
                    Fail($"{c} on \"{track.Name}\": level {lv} plays {m:0.00} BPM (want {w:0.00})");
            if (!gliding || !glideOnBar || !glide54OnBar)
                Fail($"{c}: easing down didn't glide from a bar line (gliding {gliding}, 6->5 on a bar line {glideOnBar}, 5->4 {glide54OnBar})");
            if (!(glideBar > bar6 && glideBar < bar5 + 2))
                Fail($"{c}: the 6 -> 5 glide bar is {glideBar} frames (want between level 6's {bar6} and level 5's {bar5})");
            if (Math.Abs(bpmAfter65 - want5) > 1e-3 || Math.Abs(bpmAfter54 - want4) > 1e-3)
                Fail($"{c}: after easing down the tempo is {bpmAfter65:0.00} (want {want5:0.00}) then {bpmAfter54:0.00} (want {want4:0.00})");
            if (f.StepGlitches > 0)
                Fail($"{c}: {f.StepGlitches} 16ths stumbled (skipped, repeated or mistimed) through the tempo changes");
            if (up5 > Math.Max(steady4, steady5) * 1.3 || down > steady6 * 1.3)
                Fail($"{c}: a beat around a tempo change jumps in loudness (4->5 {up5:0.000} vs {Math.Max(steady4, steady5):0.000}, easing down {down:0.000} vs {steady6:0.000})");
        }
        foreach (var l in live)
            Line(l);
        notes.AddRange(live);

        // A class switch at level 6 to a class with another tempo glides over the switch's bar.
        var sw = new FrenzyLayer(67) { Class = WeaponClass.SMG, TestTrack = tracks.First(t => t.Name == "Tracksuit Tactical") };
        sw.Target = 6;
        RunLayer(sw, 4);
        var smg6 = sw.Bpm;
        sw.Class = WeaponClass.DMR;
        for (var guard = 0; sw.ClassSwitches == 0 && guard < 400; guard++)
            RunLayer(sw, blockSeconds);
        var switchGlides = sw.Renderer.TempoGliding && sw.TempoChangeFrame == sw.ClassChangeFrame;
        RunLayer(sw, 3);
        var dmr6 = sw.Bpm;
        var wantDmr = FrenzyLayer.LevelBpm(sw.Renderer.Track.Bpm, styles[(int)WeaponClass.DMR], 6);
        notes.Add($"class switch at level 6, SMG -> DMR on \"{sw.TrackName}\": {smg6:0.0} -> {dmr6:0.0} BPM (want {wantDmr:0.0}), glided {switchGlides}, {sw.StepGlitches} stumbles");
        Line($"class switch at level 6, SMG -> DMR on \"{sw.TrackName}\": {smg6:0.0} -> {dmr6:0.0} BPM (want {wantDmr:0.0}), glided over the switch's bar: {switchGlides}, crossfade {sw.Crossfade:0.00}, {sw.StepGlitches} stumbles");
        if (!switchGlides || Math.Abs(dmr6 - wantDmr) > 1e-3 || sw.Crossfade < 1f || sw.StepGlitches > 0)
            Fail($"a class switch at level 6 didn't glide to the new class's tempo ({smg6:0.0} -> {dmr6:0.0}, want {wantDmr:0.0}; glided {switchGlides}, crossfade {sw.Crossfade:0.00}, stumbles {sw.StepGlitches})");

        // Allocation-free through the speed-ups and the glides down (after a warm-up pass).
        var g = new FrenzyLayer(71) { Class = WeaponClass.Shotgun };
        long Pass()
        {
            g.Target = 4;
            RunLayer(g, 1);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var blocks = (int)(14 / blockSeconds);
            for (var b = 0; b < blocks; b++)
            {
                g.Target = b < blocks / 7 ? 5 : b < 2 * blocks / 7 ? 6 : 0;
                Array.Clear(block);
                g.MixInto(block, true);
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            g.Died();
            g.Target = 0;
            RunLayer(g, FrenzyLayer.QuickFadeSeconds + 0.5);
            return bytes;
        }
        var warm = Pass();
        var allocated = Math.Max(Pass(), Pass());
        Line($"frenzy tempo: allocations through speed-ups and glides {allocated} bytes after warm-up (first pass {warm}); {g.TempoChanges} tempo changes");
        notes.Add($"allocations through speed-ups and glides: {allocated} bytes after warm-up; {g.TempoChanges} tempo changes");
        // One annotation on the CI run (a warning, like "Frenzy classes": the run keeps only the first few notices).
        Line("::warning title=Frenzy tempo (results)::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        if (allocated > 0)
            Fail($"the frenzy tempo changes allocated {allocated} bytes while mixing (want 0)");
        if (g.TempoChanges < 9)
            Fail($"the allocation run only changed tempo {g.TempoChanges} times");
    }
}
