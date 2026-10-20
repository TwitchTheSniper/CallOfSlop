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
    /// The CPU budget: at most this many ms of rendering per second of music, i.e. 0.5 ms per 10 ms of audio (half the
    /// 1 ms target, so a slow CI runner still passes). It measures about 7 ms per second on the CI runner.
    /// </summary>
    private const double MusicBudgetMsPerSecond = 50;

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
            if (t.Bpm is < 135 or > 180)
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
        double renderMs = 0, renderSeconds = 0;
        var costs = new System.Text.StringBuilder();
        for (var ti = 0; ti < all.Length; ti++)
        {
            var t = all[ti];
            var partSum = new double[5];
            var partCount = new long[5];
            long nan = 0, clipped = 0, samples = 0;
            float peak = 0;
            var dropStart = (long)(t.FirstDropBar * t.SecondsPerBar * sr);
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
                var bar = (int)(frame / (t.SecondsPerBar * sr));
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
            var ms = ticks * 1000.0 / Stopwatch.Frequency;
            var seconds = frame / (double)sr;
            renderMs += ms;
            renderSeconds += seconds;
            costs.Append($" {t.Name} {ms / seconds:0.0} (rms intro {Rms(MusicPart.Intro):0.00} drop {Rms(MusicPart.Drop):0.00} break {Rms(MusicPart.Break):0.00});");
            double Rms(MusicPart p) => partCount[(int)p] == 0 ? 0 : Math.Sqrt(partSum[(int)p] / partCount[(int)p]);
            var clipPct = samples == 0 ? 0 : 100.0 * clipped / samples;
            Line($"{t.Name,-24} {t.Set,-5} {t.Style,-11} {t.Bpm,3:0} BPM  {t.Key,-21} {seconds,4:0} s  rms intro {Rms(MusicPart.Intro):0.000} "
                 + $"build {Rms(MusicPart.Build):0.000} drop {Rms(MusicPart.Drop):0.000} break {Rms(MusicPart.Break):0.000} outro {Rms(MusicPart.Outro):0.000}  "
                 + $"peak {peak:0.000} clipped {clipPct:0.000}%  {ms / seconds:0.0} ms per s");
            if (!r.Finished)
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
        var before = GC.GetAllocatedBytesForCurrentThread();
        r.Load(lobby[0], 5, lobby[0].FirstDropBar);
        for (var i = 0; i < 44; i++)
            r.Render(block);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Line($"load + 1 s render allocated {allocated} bytes");
        if (allocated > 0)
            Fail($"music rendering allocated {allocated} bytes (want 0)");

        CheckMusicPlayer();
        CheckMusicPlaylist(menu, lobby);
        CheckMusicSettings();
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
