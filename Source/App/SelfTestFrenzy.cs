using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Frenzy music": the kill-frenzy heat model (quick kills climb to level 4, slow kills stay low,
/// the hold and decay timings, death, Zombies, frame-rate independence) and the adaptive layer mixed headlessly (levels
/// change on bar lines, ease down one level at a time, drops loop, level 4 jumps to the last drop, death and the
/// setting off go silent, deterministic, allocation-free, CPU against the heaviest hardbass track), plus the settings.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The frenzy layer may cost at most this many times the heaviest hardbass track (Music CPU rule: 1.5x).</summary>
    private const double FrenzyCpuRatio = 1.5;

    private static readonly List<string> FrenzyNotes = new();

    private static void FrenzyLine(string s)
    {
        Line(s);
        FrenzyNotes.Add(s);
    }

    private static void CheckFrenzyMusic()
    {
        FrenzyNotes.Clear();
        CheckFrenzyMeter();
        CheckFrenzyLayer();
        CheckFrenzyCpu();
        CheckFrenzySettings();
        // The numbers as one annotation on the CI run (the workflow prints selftest.log; notices are already used up).
        Line("::warning title=Frenzy music (results)::" + string.Join("%0A", FrenzyNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    private static void CheckFrenzyMeter()
    {
        // A burst: five kills one second apart climb 0 -> 4 without ever going down.
        var m = new FrenzyMeter();
        var levels = new int[5];
        for (var i = 0; i < 5; i++)
        {
            m.OnKill(i, false);
            levels[i] = m.Level;
        }
        FrenzyLine($"burst of 5 kills 1 s apart: levels {string.Join(" ", levels)} (heat {m.Heat:0})");
        if (levels[0] != 0)
            Fail($"a single kill already starts the frenzy (level {levels[0]})");
        if (levels[1] < 1)
            Fail("a quick double kill doesn't start the frenzy");
        if (levels[4] != 4)
            Fail($"five quick kills reach level {levels[4]} (want 4)");
        for (var i = 1; i < 5; i++)
            if (levels[i] < levels[i - 1])
                Fail($"the level went down during a burst ({string.Join(" ", levels)})");

        // The hold and decay after the burst.
        double toL3 = -1, toL2 = -1, toL1 = -1, toOff = -1;
        for (var t = 4.0; t < 40; t += 0.05)
        {
            m.Update(t);
            var since = t - 4;
            if (toL3 < 0 && m.Level <= 3)
                toL3 = since;
            if (toL2 < 0 && m.Level <= 2)
                toL2 = since;
            if (toL1 < 0 && m.Level <= 1)
                toL1 = since;
            if (toOff < 0 && m.Level == 0)
                toOff = since;
        }
        FrenzyLine($"after the burst: level 3 at +{toL3:0.0} s, 2 at +{toL2:0.0} s, 1 at +{toL1:0.0} s, off at +{toOff:0.0} s; {Program.FrenzyBurstLine(5, 0.8)}");
        if (toL3 < 4.5)
            Fail($"level 4 lasts only {toL3:0.0} s after the last kill (want a hold of about 5 s)");
        if (toOff is < 15 or > 26)
            Fail($"the frenzy is gone {toOff:0.0} s after the last kill (want about 15-25 s)");
        if (!(toL3 < toL2 && toL2 < toL1 && toL1 < toOff))
            Fail("the levels don't come down one after the other");

        // Slow kills never pass level 2; really slow ones never start it.
        foreach (var (gap, max) in new[] { (6.0, 2), (8.0, 1), (12.0, 0) })
        {
            var s = new FrenzyMeter();
            var top = 0;
            var every = (int)Math.Round(gap * 10);
            for (var step = 0; step < 1800; step++)
            {
                var t = step * 0.1;
                if (step % every == 0)
                    s.OnKill(t, true);
                else
                    s.Update(t);
                top = Math.Max(top, s.Level);
            }
            FrenzyLine($"a headshot kill every {gap:0} s for 3 minutes: top level {top} (allowed {max})");
            if (top > max)
                Fail($"a kill every {gap:0} s reaches level {top} (want at most {max})");
        }

        // Death wipes it; a kill after respawning starts from zero.
        m = new FrenzyMeter();
        for (var i = 0; i < 5; i++)
            m.OnKill(i * 0.8, true);
        m.OnDeath(4);
        if (m.Level != 0 || m.Heat != 0)
            Fail($"dying leaves level {m.Level}, heat {m.Heat}");
        m.OnKill(9, false);
        if (m.Level != 0)
            Fail($"the first kill after dying is level {m.Level} (the old frenzy carried over)");

        // Frame-rate independent: 240 fps and 20 fps give the same heat.
        float HeatAt(double dt)
        {
            var x = new FrenzyMeter();
            var next = 0;
            double[] kills = [0, 1.1, 1.9, 3.5, 4.2, 9.0];
            for (var t = 0.0; t <= 20.0001; t += dt)
            {
                while (next < kills.Length && kills[next] <= t + 1e-9)
                    x.OnKill(kills[next++], false);
                x.Update(t);
            }
            return x.Heat;
        }
        var fast = HeatAt(1 / 240.0);
        var slow = HeatAt(1 / 20.0);
        if (Math.Abs(fast - slow) > 0.5f)
            Fail($"heat depends on the frame rate: {fast:0.00} at 240 fps, {slow:0.00} at 20 fps");

        // Zombies: the same burst is worth less.
        var z = new FrenzyMeter { Zombies = true };
        for (var i = 0; i < 5; i++)
            z.OnKill(i, false);
        var zFive = z.Level;
        for (var i = 5; i < 12; i++)
            z.OnKill(i, false);
        FrenzyLine($"Zombies: 5 kills 1 s apart -> level {zFive}, 12 -> level {z.Level}");
        if (zFive >= 4)
            Fail($"Zombies: five zombie kills already reach level {zFive}");
        if (z.Level < 3)
            Fail($"Zombies: twelve kills a second apart only reach level {z.Level}");
    }

    private static void CheckFrenzyLayer()
    {
        const int sr = Audio.SampleRate;
        var block = new float[MusicPlayer.BlockFrames * 2];
        var blockSeconds = MusicPlayer.BlockFrames / (double)sr;

        // Run the layer for some seconds; returns the RMS of the last half of it.
        double Run(FrenzyLayer f, double seconds, bool allowed = true)
        {
            var n = (int)Math.Ceiling(seconds / blockSeconds);
            double sum = 0;
            long count = 0;
            for (var b = 0; b < n; b++)
            {
                Array.Clear(block);
                f.MixInto(block, allowed);
                if (b >= n / 2)
                {
                    for (var i = 0; i < block.Length; i++)
                        sum += block[i] * (double)block[i];
                    count += block.Length;
                }
            }
            return count == 0 ? 0 : Math.Sqrt(sum / count);
        }

        bool OnGrid(FrenzyLayer f)
        {
            var fpb = f.Renderer.FramesPerBar;
            var bars = f.LastChangeFrame / fpb;
            return Math.Abs(bars - Math.Round(bars)) * fpb <= 2.0;
        }

        // Off: nothing, no cost.
        var layer = new FrenzyLayer(7);
        if (Run(layer, 1) > 0 || layer.Active)
            Fail("the frenzy layer plays with no frenzy");

        // Levels 1-4, each on a bar line, each louder or brighter than the last.
        var rms = new double[5];
        var lines = new List<string>();
        for (var level = 1; level <= 4; level++)
        {
            layer.Target = level;
            var waited = 0.0;
            while (layer.Level != level && waited < 4)
            {
                Run(layer, blockSeconds);
                waited += blockSeconds;
            }
            var bar = layer.Renderer.FramesPerBar / sr;
            if (layer.Level != level)
                Fail($"level {level} never came (playing {layer.Level})");
            else if (waited > bar + 2 * blockSeconds)
                Fail($"level {level} took {waited:0.00} s to come in (a bar is {bar:0.00} s)");
            if (!OnGrid(layer))
                Fail($"the change to level {level} wasn't on a bar line (frame {layer.LastChangeFrame}, {layer.Renderer.FramesPerBar:0} frames per bar)");
            rms[level] = Run(layer, 3);
            lines.Add($"L{level} rms {rms[level]:0.000} cutoff {layer.Cutoff:0} Hz gain {layer.Gain:0.00}");
        }
        FrenzyLine($"frenzy on \"{layer.TrackName}\": {string.Join(", ", lines)}");
        if (rms[1] < 0.005)
            Fail($"level 1 is (nearly) silent (rms {rms[1]:0.0000})");
        if (!(rms[1] < rms[2] && rms[2] < rms[3] && rms[3] <= rms[4] * 1.05))
            Fail($"the levels don't build up (rms {rms[1]:0.000} {rms[2]:0.000} {rms[3]:0.000} {rms[4]:0.000})");
        var lastDrop = MusicRenderer.LastDropBar(MusicTracks.All.First(tr => tr.Name == layer.TrackName));
        if (layer.Renderer.Part != MusicPart.Drop || layer.Renderer.StepIndex / 16 < lastDrop)
            Fail($"level 4 isn't in the last drop (bar {layer.Renderer.StepIndex / 16}, last drop at {lastDrop}, part {layer.Renderer.Part})");

        // Drops loop: a minute at level 4 never leaves the drop.
        var leftDrop = 0;
        for (var b = 0; b < (int)(60 / blockSeconds); b++)
        {
            Array.Clear(block);
            layer.MixInto(block, true);
            if (layer.Renderer.Part != MusicPart.Drop)
                leftDrop++;
        }
        if (leftDrop > 0 || !layer.Active || layer.Level != 4)
            Fail($"a minute of level 4 left the drop for {leftDrop} blocks (active {layer.Active}, level {layer.Level})");

        // Easing down: one level at a time, at least two bars apart, on bar lines, then silence.
        layer.Target = 0;
        var seq = new List<int> { layer.Level };
        var changeFrames = new List<long>();
        var fpbFrames = layer.Renderer.FramesPerBar;
        var t = 0.0;
        while (layer.Active && t < 40)
        {
            Run(layer, blockSeconds);
            t += blockSeconds;
            if (layer.Level != seq[^1])
            {
                seq.Add(layer.Level);
                changeFrames.Add(layer.LastChangeFrame);
                if (layer.Active && !OnGrid(layer))
                    Fail($"easing down to level {layer.Level} wasn't on a bar line");
            }
        }
        FrenzyLine($"easing down from level 4: {string.Join(" -> ", seq)} in {t:0.0} s");
        if (layer.Active || Run(layer, 0.5) > 0)
            Fail($"the layer didn't fall silent after the heat ran out ({t:0.0} s)");
        for (var i = 1; i < seq.Count; i++)
            if (seq[i] != seq[i - 1] - 1)
                Fail($"easing down jumped a level ({string.Join(" -> ", seq)})");
        for (var i = 1; i < changeFrames.Count; i++)
            if (changeFrames[i] - changeFrames[i - 1] < 2 * fpbFrames - 4)
                Fail($"easing down went faster than a level per two bars ({(changeFrames[i] - changeFrames[i - 1]) / fpbFrames:0.0} bars)");

        // Death: a quick fade, silent within the fade time.
        layer.Target = 3;
        Run(layer, 4);
        layer.Died();
        layer.Target = 0;
        Run(layer, FrenzyLayer.QuickFadeSeconds + 0.1);
        var after = Run(layer, 0.4);
        if (layer.Active || after > 0)
            Fail($"dying didn't fade the frenzy out within {FrenzyLayer.QuickFadeSeconds} s (active {layer.Active}, rms {after:0.0000})");

        // Setting off: silent within the fade, and stays silent at any level; not allowed (menus, lobby): the same.
        layer.Target = 3;
        Run(layer, 3);
        layer.Enabled = false;
        Run(layer, FrenzyLayer.QuickFadeSeconds + 0.1);
        layer.Target = 4;
        var off = Run(layer, 2);
        layer.Enabled = true;
        if (off > 0 || layer.Active)
            Fail($"Kill frenzy music off still plays (rms {off:0.0000})");
        var notAllowed = Run(layer, 2, allowed: false);
        if (notAllowed > 0)
            Fail("the frenzy plays outside a live match");

        // Through the player: only while the mood is Off (a live match); the lobby mood fades it out.
        var p = new MusicPlayer(3) { Volume = 1f };
        var tenth = new float[sr / 10 * 2];
        p.SetMood(MusicMood.Off);
        p.Frenzy.Target = 3;
        for (var i = 0; i < 30; i++)
            p.Mix(tenth);
        var inMatch = Measure(tenth).Rms;
        p.SetMood(MusicMood.Lobby);
        for (var i = 0; i < 30; i++)
            p.Mix(tenth);
        if (inMatch < 0.005 || p.Frenzy.Level != 0 || p.Frenzy.Active)
            Fail($"through the player: frenzy rms {inMatch:0.000} in the match, level {p.Frenzy.Level} in the lobby (want 0)");
        p.Volume = 0f;
        p.SetMood(MusicMood.Off);
        for (var i = 0; i < 30; i++)
            p.Mix(tenth);
        if (Measure(tenth).Peak > 1e-5f)
            Fail("music volume 0 doesn't silence the frenzy");

        // Deterministic: the same script twice gives the same samples.
        float[] Script(uint seed)
        {
            var f = new FrenzyLayer(seed);
            var outp = new float[sr * 2 * 12];
            var b = new float[MusicPlayer.BlockFrames * 2];
            for (var i = 0; i + b.Length <= outp.Length; i += b.Length)
            {
                var sec = i / 2.0 / sr;
                f.Target = sec < 3 ? 1 : sec < 6 ? 3 : sec < 8 ? 4 : 0;
                if (i == b.Length * 300)
                    f.Shot();
                Array.Clear(b);
                f.MixInto(b, true);
                b.CopyTo(outp, i);
            }
            return outp;
        }
        if (!Script(11).AsSpan().SequenceEqual(Script(11)))
            Fail("two frenzy runs with the same seed differ");

        // Allocation-free while climbing, jumping to the last drop and fading.
        var g = new FrenzyLayer(5);
        g.Target = 1;
        Run(g, 0.5);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var b = 0; b < 400; b++)
        {
            g.Target = b < 100 ? 2 : b < 250 ? 4 : 0;
            if (b == 300)
                g.Died();
            Array.Clear(block);
            g.MixInto(block, true);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 0)
            Fail($"the frenzy layer allocated {allocated} bytes while mixing (want 0)");
    }

    /// <summary>
    /// CPU: the frenzy at level 4 (and level 1) against the heaviest hardbass track's drop rendered plainly, each the
    /// median of three runs on this machine, so a slow or busy CI runner can't make it flaky.
    /// </summary>
    private static void CheckFrenzyCpu()
    {
        const int sr = Audio.SampleRate;
        const double seconds = 3;
        var block = new float[MusicPlayer.BlockFrames * 2];
        var blocks = (int)(seconds * sr / MusicPlayer.BlockFrames);

        double Median(Func<double> run)
        {
            var a = new[] { run(), run(), run() };
            Array.Sort(a);
            return a[1];
        }

        var r = new MusicRenderer();
        double TrackCost(MusicTrack t)
        {
            r.Load(t, 9, t.FirstDropBar);
            var t0 = Stopwatch.GetTimestamp();
            for (var b = 0; b < blocks; b++)
                r.Render(block);
            return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / seconds;
        }
        var hb = MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass).ToArray();
        TrackCost(hb[0]); // JIT warm-up
        MusicTrack heaviest = hb[0];
        var heaviestCost = 0.0;
        foreach (var t in hb)
        {
            var c = TrackCost(t);
            if (c > heaviestCost)
            {
                heaviestCost = c;
                heaviest = t;
            }
        }
        heaviestCost = Median(() => TrackCost(heaviest));

        double FrenzyCost(int level)
        {
            var f = new FrenzyLayer(13);
            f.Target = level;
            for (var b = 0; b < 200; b++)
            {
                Array.Clear(block);
                f.MixInto(block, true); // reach the level (and the last drop)
            }
            var t0 = Stopwatch.GetTimestamp();
            for (var b = 0; b < blocks; b++)
            {
                Array.Clear(block);
                f.MixInto(block, true);
            }
            return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency / seconds;
        }
        FrenzyCost(4); // warm-up
        var l4 = Median(() => FrenzyCost(4));
        var l1 = Median(() => FrenzyCost(1));
        FrenzyLine($"frenzy CPU: level 4 {l4:0.00} ms per s of audio, level 1 {l1:0.00}; heaviest hardbass track ({heaviest.Name}) {heaviestCost:0.00}; "
             + $"ratio {l4 / Math.Max(1e-6, heaviestCost):0.00} (allowed {FrenzyCpuRatio}x)");
        if (l4 > heaviestCost * FrenzyCpuRatio)
            Fail($"the frenzy layer costs {l4:0.00} ms per s, more than {FrenzyCpuRatio}x the heaviest track ({heaviestCost:0.00})");
        if (l4 > MusicBudgetMsPerSecond)
            Fail($"the frenzy layer costs {l4:0.0} ms per s (budget {MusicBudgetMsPerSecond})");
        if (l1 > l4)
            Fail($"level 1 ({l1:0.00} ms per s) costs more than level 4 ({l4:0.00}); the muted leads should be skipped");
    }

    /// <summary>Kill frenzy music and Frenzy volume: defaults for old files, parsed, saved. Temporary files only.</summary>
    private static void CheckFrenzySettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"callofslop-selftest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "settings.txt");
            File.WriteAllLines(path, ["name=Old Timer", "volume=0.6", "music_volume=0.4", "music_style=HardbassOnly"]); // from before the frenzy
            var s = Settings.Load(path);
            if (!s.FrenzyMusic || Math.Abs(s.FrenzyVolume - 0.8f) > 1e-6f || s.MusicChoice != MusicChoice.HardbassOnly)
                Fail($"old settings file: frenzy music {s.FrenzyMusic} (want on), frenzy volume {s.FrenzyVolume} (want 0.8)");
            foreach (var (text, want) in new[] { ("0", false), ("1", true), ("", true) })
            {
                File.WriteAllLines(path, [$"frenzy_music={text}"]);
                if (Settings.Load(path).FrenzyMusic != want)
                    Fail($"frenzy_music={text} loaded as {!want} (want {want})");
            }
            foreach (var (text, want) in new[] { ("0.3", 0.3f), ("abc", 0.8f), ("7", 1f), ("-1", 0f), ("NaN", 0.8f) })
            {
                File.WriteAllLines(path, [$"frenzy_volume={text}"]);
                var v = Settings.Load(path).FrenzyVolume;
                if (Math.Abs(v - want) > 1e-6f)
                    Fail($"frenzy_volume={text} loaded as {v} (want {want})");
            }
            s.FrenzyMusic = false;
            s.FrenzyVolume = 0.35f;
            s.Save(path);
            var back = Settings.Load(path);
            var file = File.ReadAllLines(path);
            if (back.FrenzyMusic || Math.Abs(back.FrenzyVolume - 0.35f) > 1e-6f || !file.Contains("frenzy_music=0") || !file.Contains("frenzy_volume=0.35"))
                Fail("the frenzy settings didn't survive a save and load");
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
