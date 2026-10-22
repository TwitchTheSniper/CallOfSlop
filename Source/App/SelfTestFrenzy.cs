using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Frenzy music": the kill-frenzy heat model (quick kills climb to level 4, a real spree to 5 and
/// 6, slow kills stay low, the hold and decay timings, death, Zombies, frame-rate independence) and the adaptive layer
/// mixed headlessly (levels change on bar lines, ease down one level at a time, drops loop, level 4 jumps to the last
/// drop, level 5 louder, level 6 as loud as the game's own gunfire and explosions, measured, ducking per level, the
/// limiter, the volume sliders still multiply, death and the setting off go silent, deterministic, allocation-free,
/// CPU against the heaviest hardbass track), plus the settings.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The frenzy layer may cost at most this many times the heaviest hardbass track (Music CPU rule: 1.5x).</summary>
    private const double FrenzyCpuRatio = 1.5;

    private static readonly List<string> FrenzyNotes = new();

    /// <summary>The measured gunfire-and-explosion reference (set by <see cref="CheckFrenzyLevel6Loudness"/>, used by the limiter test).</summary>
    private static double _frenzySfxReference;

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

        // A real spree: kills a second apart reach level 5 on the 9th and level 6 (MAX FRENZY) on the 15th; half a
        // second apart on the 8th and 13th; every 2 s level 5 on the 10th and 6 only on the 22nd; every 2.5 s level 5
        // on the 12th and never 6; every 3 s level 5 after 14, never 6.
        foreach (var (gap, want5, want6) in new[] { (1.0, 9, 15), (0.5, 8, 13), (2.0, 10, 22), (2.5, 12, 0), (3.0, 14, 0) })
        {
            var sp = new FrenzyMeter();
            int first5 = 0, first6 = 0;
            var top = 0;
            for (var i = 0; i < 30; i++)
            {
                sp.OnKill(i * gap, false);
                top = Math.Max(top, sp.Level);
                if (first5 == 0 && sp.Level >= 5)
                    first5 = i + 1;
                if (first6 == 0 && sp.Level >= 6)
                    first6 = i + 1;
            }
            FrenzyLine($"a kill every {gap:0.0} s: level 5 on kill {first5}, level 6 on kill {first6} (0 = never in 30 kills), top {top}, heat {sp.Heat:0} after 30 (cap {FrenzyMeter.MaxHeat})");
            if (Math.Abs(first5 - want5) > 1)
                Fail($"kills {gap} s apart reach level 5 on kill {first5} (want about {want5})");
            if (want6 == 0 ? first6 != 0 : Math.Abs(first6 - want6) > 1)
                Fail($"kills {gap} s apart reach level 6 on kill {first6} (want {(want6 == 0 ? "never" : $"about {want6}")})");
        }
        for (var l = 0; l < FrenzyMeter.MaxLevel; l++)
            if (!(FrenzyMeter.Down[l] < FrenzyMeter.Up[l] && (l == 0 || FrenzyMeter.Down[l] > FrenzyMeter.Up[l - 1])))
                Fail($"level {l + 1}: thresholds up {FrenzyMeter.Up[l]} / down {FrenzyMeter.Down[l]} aren't a proper hysteresis band");
        FrenzyLine($"thresholds up {string.Join("/", FrenzyMeter.Up)}, down {string.Join("/", FrenzyMeter.Down)}; max spree on paper: {Program.FrenzyBurstLine(16, 0.7, true)}");

        // The hold at the top and the way down from a maxed-out frenzy: level 6 holds about 5 s, then one level at a time.
        var mx = new FrenzyMeter();
        for (var i = 0; i < 20; i++)
            mx.OnKill(i * 0.7, true);
        if (mx.Level != 6 || mx.Heat < FrenzyMeter.MaxHeat - 1)
            Fail($"20 headshot kills 0.7 s apart end at level {mx.Level}, heat {mx.Heat:0} (want 6 at the cap)");
        var lastKill = 19 * 0.7;
        var downAt = new double[7];
        for (var t = lastKill; t < lastKill + 60; t += 0.05)
        {
            mx.Update(t);
            for (var l = 5; l >= 0; l--)
                if (downAt[l] == 0 && mx.Level <= l)
                    downAt[l] = t - lastKill;
        }
        FrenzyLine($"after a maxed spree: level 5 at +{downAt[5]:0.0} s, 4 at +{downAt[4]:0.0}, 3 at +{downAt[3]:0.0}, 2 at +{downAt[2]:0.0}, 1 at +{downAt[1]:0.0}, off at +{downAt[0]:0.0}");
        if (downAt[5] < 4.5 || downAt[5] > 8)
            Fail($"level 6 lasts {downAt[5]:0.0} s after the last kill (want about 5-6 s)");
        if (downAt[0] is < 25 or > 45)
            Fail($"a maxed frenzy is gone {downAt[0]:0.0} s after the last kill (want about 30-40 s)");
        for (var l = 1; l <= 5; l++)
            if (downAt[l] >= downAt[l - 1])
                Fail("the levels after a maxed spree don't come down one after the other");

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
        var zTwelve = z.Level;
        for (var i = 12; i < 30; i++)
            z.OnKill(11 + (i - 11) * 0.5, false);
        FrenzyLine($"Zombies: 5 kills 1 s apart -> level {zFive}, 12 -> level {zTwelve}; 30 kills half a second apart -> level {z.Level}");
        if (zFive >= 4)
            Fail($"Zombies: five zombie kills already reach level {zFive}");
        if (zTwelve < 3)
            Fail($"Zombies: twelve kills a second apart only reach level {zTwelve}");
        if (z.Level < 6)
            Fail($"Zombies: a horde of 30 fast kills only reaches level {z.Level} (want 6: the same levels apply)");
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

        // Levels 1-6, each on a bar line, each louder or brighter than the last.
        var rms = new double[7];
        var lines = new List<string>();
        var lastDrop = -1;
        for (var level = 1; level <= 6; level++)
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
            lines.Add($"L{level} rms {rms[level]:0.000} cutoff {layer.Cutoff:0} Hz gain {layer.Gain:0.00} duck {layer.DuckingDepth:0.00}");
            if (level == 4)
            {
                lastDrop = MusicRenderer.LastDropBar(MusicTracks.All.First(tr => tr.Name == layer.TrackName));
                if (layer.Renderer.Part != MusicPart.Drop || layer.Renderer.StepIndex / 16 < lastDrop)
                    Fail($"level 4 isn't in the last drop (bar {layer.Renderer.StepIndex / 16}, last drop at {lastDrop}, part {layer.Renderer.Part})");
            }
            if (Math.Abs(layer.DuckingDepth - FrenzyLayer.LevelDuck[level]) > 1e-3f)
                Fail($"level {level} ducking depth {layer.DuckingDepth:0.00} (want {FrenzyLayer.LevelDuck[level]:0.00})");
        }
        FrenzyLine($"frenzy on \"{layer.TrackName}\": {string.Join(", ", lines)}");
        if (rms[1] < 0.005)
            Fail($"level 1 is (nearly) silent (rms {rms[1]:0.0000})");
        if (!(rms[1] < rms[2] && rms[2] < rms[3] && rms[3] <= rms[4] * 1.05))
            Fail($"the levels don't build up (rms {rms[1]:0.000} {rms[2]:0.000} {rms[3]:0.000} {rms[4]:0.000})");
        if (!(rms[5] > rms[4] * 1.2 && rms[6] > rms[5] * 1.5))
            Fail($"levels 5 and 6 aren't clearly louder (rms L4 {rms[4]:0.000}, L5 {rms[5]:0.000}, L6 {rms[6]:0.000})");
        if (layer.Hits != 1)
            Fail($"the level-6 hit fired {layer.Hits} times on the way up (want once)");
        if (Math.Abs(rms[5] / rms[4] - FrenzyLayer.Level5Gain) > 0.12)
            Fail($"level 5 is {rms[5] / rms[4]:0.00}x level 4 (want about {FrenzyLayer.Level5Gain:0.00}: as loud as the menu music)");

        // Level 6 against the game's own sounds: with the sliders at their defaults, the frenzy's drop is at least as loud
        // as a second of your own gun firing with a frag explosion on top (the median weapon), and not wildly louder.
        CheckFrenzyLevel6Loudness(rms[6], rms[4]);

        // Drops loop: a minute at level 6 never leaves the drop.
        var leftDrop = 0;
        for (var b = 0; b < (int)(60 / blockSeconds); b++)
        {
            Array.Clear(block);
            layer.MixInto(block, true);
            if (layer.Renderer.Part != MusicPart.Drop)
                leftDrop++;
        }
        if (leftDrop > 0 || !layer.Active || layer.Level != 6)
            Fail($"a minute of level 6 left the drop for {leftDrop} blocks (active {layer.Active}, level {layer.Level})");

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
        FrenzyLine($"easing down from level 6: {string.Join(" -> ", seq)} in {t:0.0} s");
        if (seq.Count != 7)
            Fail($"easing down from level 6 should pass every level (got {string.Join(" -> ", seq)})");
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
            var outp = new float[sr * 2 * 14];
            var b = new float[MusicPlayer.BlockFrames * 2];
            for (var i = 0; i + b.Length <= outp.Length; i += b.Length)
            {
                var sec = i / 2.0 / sr;
                f.Target = sec < 3 ? 1 : sec < 6 ? 3 : sec < 8 ? 4 : sec < 11 ? 6 : 0;
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

        // Allocation-free while climbing, jumping to the last drop, hitting level 6 and fading. The same script runs
        // three times on one layer: the first pass warms up (tiered JIT and anything lazily made on a first mix are not
        // per-block allocations), the two measured passes must both be clean (the lower one is reported).
        var g = new FrenzyLayer(5);
        long Pass()
        {
            g.Target = 1;
            Run(g, 0.5);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var b = 0; b < 500; b++)
            {
                g.Target = b < 100 ? 2 : b < 220 ? 4 : b < 350 ? 6 : 0;
                if (b == 400)
                    g.Died();
                Array.Clear(block);
                g.MixInto(block, true);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            g.Target = 0;
            Run(g, FrenzyLayer.QuickFadeSeconds + 0.5);
            return allocated;
        }
        var warm = Pass();
        var allocated = Math.Max(Pass(), Pass());
        FrenzyLine($"allocations while mixing: {allocated} bytes per 500 blocks after warm-up (first pass {warm})");
        if (allocated > 0)
            Fail($"the frenzy layer allocated {allocated} bytes while mixing (want 0)");

        CheckFrenzyDuckingAndLimiter();
    }

    /// <summary>
    /// Level 6 against the game's own sounds (<see cref="Audio.CombatReferenceRms"/>): the calibration constants match
    /// what's measured (the SFX reference within 10%, the pool's median drop RMS within 10%), and the frenzy at level 6
    /// with the sliders at their defaults aims above the reference before the limiter (at most 1.6x); what comes out
    /// after the limiter is checked in <see cref="CheckFrenzyDuckingAndLimiter"/>.
    /// <paramref name="rms6"/> and <paramref name="rms4"/> are the layer's output (frenzy volume 0.8, before the music volume).
    /// </summary>
    private static void CheckFrenzyLevel6Loudness(double rms6, double rms4)
    {
        var sw = Stopwatch.StartNew();
        var sfx = Audio.CombatReferenceRms();
        var measureMs = sw.Elapsed.TotalMilliseconds;

        // The drop RMS of every track in the frenzy pool, rendered plainly from its last drop.
        var r = new MusicRenderer();
        var buf = new float[MusicPlayer.BlockFrames * 2];
        var drops = new List<(string Name, double Rms)>();
        foreach (var t in MusicTracks.All.Where(t => t.Style == MusicStyle.Hardbass && (t.Timeline?.SteadyDrops ?? true)))
        {
            r.Load(t, 5, MusicRenderer.LastDropBar(t));
            r.LoopDrops = true;
            double sum = 0;
            long n = 0;
            for (var b = 0; b < 4 * Audio.SampleRate / MusicPlayer.BlockFrames; b++)
            {
                r.Render(buf);
                if (b < 2 * Audio.SampleRate / MusicPlayer.BlockFrames)
                    continue; // skip the crash and sub drop at the start
                foreach (var x in buf)
                    sum += x * (double)x;
                n += buf.Length;
            }
            drops.Add((t.Name, Math.Sqrt(sum / Math.Max(1, n))));
        }
        drops.Sort((a, b) => a.Rms.CompareTo(b.Rms));
        var medianDrop = drops[drops.Count / 2].Rms;

        _frenzySfxReference = sfx.Combined;
        var musicGain = FrenzyLayer.DefaultMusicVolume * FrenzyLayer.DefaultMusicVolume; // the player's squared curve
        var level6 = rms6 * musicGain; // what comes out of the music bus at the default sliders (frenzy volume 0.8 is already in)
        var level4 = rms4 * musicGain;
        FrenzyLine($"SFX reference: {sfx.Combined:0.000} rms (gunfire median {sfx.GunfireMedian:0.000}, explosion {sfx.Explosion:0.000}, weapons {sfx.Min:0.000}-{sfx.Max:0.000} over {sfx.Weapons}; "
            + $"constant {FrenzyLayer.SfxReferenceRms:0.000}; measured in {measureMs:0} ms); frenzy drops {drops[0].Rms:0.000} ({drops[0].Name}) to {drops[^1].Rms:0.000} ({drops[^1].Name}), median {medianDrop:0.000} (constant {FrenzyLayer.DropRms:0.00}); "
            + $"at default sliders before the limiter: level 4 {level4:0.000} rms = {level4 / sfx.Combined:0.00}x the SFX, level 6 {level6:0.000} rms = {level6 / sfx.Combined:0.00}x the SFX (gain {FrenzyLayer.Level6Gain:0.00}, aim {FrenzyLayer.Level6Headroom:0.00}x)");
        if (Math.Abs(sfx.Combined - FrenzyLayer.SfxReferenceRms) > 0.1 * sfx.Combined)
            Fail($"the SFX reference constant {FrenzyLayer.SfxReferenceRms:0.000} is off: measured {sfx.Combined:0.000} (retune FrenzyLayer.SfxReferenceRms)");
        if (Math.Abs(medianDrop - FrenzyLayer.DropRms) > 0.1 * medianDrop)
            Fail($"the drop RMS constant {FrenzyLayer.DropRms:0.00} is off: measured {medianDrop:0.000} (retune FrenzyLayer.DropRms)");
        if (level6 < sfx.Combined * 1.05)
            Fail($"level 6 at the default sliders ({level6:0.000} rms before the limiter) doesn't aim above the gunfire and explosions ({sfx.Combined:0.000})");
        if (level6 > sfx.Combined * 1.6)
            Fail($"level 6 at the default sliders ({level6:0.000} rms) is far louder than the gunfire and explosions ({sfx.Combined:0.000}); want about {FrenzyLayer.Level6Headroom:0.00}x");
        if (level4 >= sfx.Combined)
            Fail($"level 4 ({level4:0.000} rms) is already as loud as the SFX ({sfx.Combined:0.000}); it should sit under them");
    }

    /// <summary>
    /// Ducking per level (two identical layers, one of them shot at: the exact ratio), the limiter (level 6 at the
    /// loudest sliders never clips; at the defaults it doesn't even engage) and the sliders still multiplying at level 6.
    /// </summary>
    private static void CheckFrenzyDuckingAndLimiter()
    {
        const int sr = Audio.SampleRate;
        var a = new float[MusicPlayer.BlockFrames * 2];
        var b = new float[MusicPlayer.BlockFrames * 2];

        // Ducking: over the 0.1 s after a shot the duck is 1 -> 0.8 (linear release over 0.5 s), so the level is
        // 1 - 0.9 * depth of the unshot layer's: 0.73 at levels 1-4, 0.865 at 5, 1.0 at 6.
        var duckLines = new List<string>();
        foreach (var level in new[] { 4, 5, 6 })
        {
            var x = new FrenzyLayer(21) { Target = level };
            var y = new FrenzyLayer(21) { Target = level };
            for (var i = 0; i < 300; i++)
            {
                Array.Clear(a);
                Array.Clear(b);
                x.MixInto(a, true);
                y.MixInto(b, true);
            }
            if (x.Level != level || y.Level != level)
                Fail($"ducking test: the layers didn't reach level {level} ({x.Level}, {y.Level})");
            y.Shot();
            double sx = 0, sy = 0;
            for (var i = 0; i < sr / 10 / MusicPlayer.BlockFrames + 1; i++)
            {
                Array.Clear(a);
                Array.Clear(b);
                x.MixInto(a, true);
                y.MixInto(b, true);
                for (var j = 0; j < a.Length; j++)
                {
                    sx += a[j] * (double)a[j];
                    sy += b[j] * (double)b[j];
                }
            }
            var ratio = Math.Sqrt(sy / Math.Max(1e-12, sx));
            var want = 1 - 0.9 * FrenzyLayer.LevelDuck[level];
            duckLines.Add($"L{level} {ratio:0.000} (want {want:0.000})");
            if (Math.Abs(ratio - want) > 0.03)
                Fail($"gunfire ducking at level {level}: the shot layer is {ratio:0.000}x the unshot one (want {want:0.000})");
        }
        FrenzyLine($"gunfire ducking, shot vs unshot layer over 0.1 s: {string.Join(", ", duckLines)}");

        // The limiter: level 6 at full music and frenzy volume through the player; the peak stays under the safety
        // ceiling and the limiter did work. At the default sliders the limiter needn't engage; log the peak with your
        // own gun's peak on top (what OpenAL mixes, before the master volume).
        var tenth = new float[sr / 10 * 2];
        (float Peak, double Rms, float MinGain) Loud(float music, float frenzy)
        {
            var p = new MusicPlayer(3) { Volume = music };
            p.Frenzy.Volume = frenzy;
            p.SetMood(MusicMood.Off);
            p.Frenzy.Target = 6;
            for (var i = 0; i < 60; i++)
                p.Mix(tenth); // 6 s: up to level 6 and past the hit
            float peak = 0;
            double sum = 0;
            long n = 0;
            for (var i = 0; i < 30; i++)
            {
                p.Mix(tenth);
                if (p.Frenzy.Level != 6)
                    Fail($"limiter test: the player's frenzy is at level {p.Frenzy.Level} (want 6)");
                var m = Measure(tenth);
                peak = MathF.Max(peak, m.Peak);
                sum += m.Rms * m.Rms;
                n++;
            }
            return (peak, Math.Sqrt(sum / Math.Max(1, n)), p.FrenzyLimiterMinGain);
        }
        var max = Loud(1f, 1f);
        var def = Loud(FrenzyLayer.DefaultMusicVolume, FrenzyLayer.DefaultVolume);
        var gunPeak = 0.9f * Audio.OwnShotGain; // gunshots are normalised to 0.9 (0.98 for the boom guns)
        var reference = _frenzySfxReference;
        FrenzyLine($"level 6 through the player: sliders at max peak {max.Peak:0.000} rms {max.Rms:0.000} (limiter min gain {max.MinGain:0.00}); "
            + $"defaults peak {def.Peak:0.000} rms {def.Rms:0.000} = {def.Rms / Math.Max(1e-9, reference):0.00}x the gunfire-and-explosion reference {reference:0.000} (limiter min gain {def.MinGain:0.00}); "
            + $"with your own gun's peak ({gunPeak:0.00}) on top: {def.Peak + gunPeak:0.00}, x master 0.8 = {(def.Peak + gunPeak) * 0.8f:0.00}");
        if (max.Peak > MasterLimiter.SafetyCeiling + 1e-4f)
            Fail($"level 6 at full volume peaks at {max.Peak:0.000}, past the limiter's ceiling {MasterLimiter.SafetyCeiling}");
        if (max.MinGain >= 0.999f)
            Fail("level 6 at full volume never engaged the limiter (the gain is lower than it should be)");
        if (def.Peak > MasterLimiter.SafetyCeiling + 1e-4f)
            Fail($"level 6 at the default sliders peaks at {def.Peak:0.000}");
        if (reference > 0 && def.Rms < reference)
            Fail($"level 6 at the default sliders comes out at {def.Rms:0.000} rms after the limiter, quieter than the gunfire and explosions ({reference:0.000})");
        if (reference > 0 && def.Rms > reference * 1.5)
            Fail($"level 6 at the default sliders comes out at {def.Rms:0.000} rms, far louder than the gunfire and explosions ({reference:0.000})");

        // The sliders still multiply at level 6 (measured below the limiter's reach): half the frenzy volume halves it;
        // half the music volume quarters it (squared curve).
        var baseFrenzy = Loud(FrenzyLayer.DefaultMusicVolume, FrenzyLayer.DefaultVolume * 0.5f);
        var halfFrenzy = Loud(FrenzyLayer.DefaultMusicVolume, FrenzyLayer.DefaultVolume * 0.25f);
        var baseMusic = Loud(0.35f, FrenzyLayer.DefaultVolume);
        var quarterMusic = Loud(0.175f, FrenzyLayer.DefaultVolume);
        FrenzyLine($"sliders at level 6: frenzy volume halved -> {halfFrenzy.Rms / baseFrenzy.Rms:0.000}x (want 0.5), music volume halved -> {quarterMusic.Rms / baseMusic.Rms:0.000}x (want 0.25); "
            + $"frenzy volume {FrenzyLayer.DefaultVolume * 0.5f:0.0} rms {baseFrenzy.Rms:0.000}, music volume 0.35 rms {baseMusic.Rms:0.000}");
        if (baseFrenzy.MinGain < 0.999f || baseMusic.MinGain < 0.999f)
            Fail($"the slider test's baselines hit the limiter (min gain {baseFrenzy.MinGain:0.00}, {baseMusic.MinGain:0.00}); lower them");
        if (Math.Abs(halfFrenzy.Rms / baseFrenzy.Rms - 0.5) > 0.03)
            Fail($"the frenzy volume slider doesn't multiply at level 6 ({halfFrenzy.Rms / baseFrenzy.Rms:0.000}x for half)");
        if (Math.Abs(quarterMusic.Rms / baseMusic.Rms - 0.25) > 0.03)
            Fail($"the music volume slider doesn't multiply at level 6 ({quarterMusic.Rms / baseMusic.Rms:0.000}x for half)");
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
        var l6 = Median(() => FrenzyCost(6));
        var l1 = Median(() => FrenzyCost(1));
        var top = Math.Max(l4, l6);
        FrenzyLine($"frenzy CPU: level 4 {l4:0.00} ms per s of audio, level 6 {l6:0.00}, level 1 {l1:0.00}; heaviest hardbass track ({heaviest.Name}) {heaviestCost:0.00}; "
             + $"ratio {top / Math.Max(1e-6, heaviestCost):0.00} (allowed {FrenzyCpuRatio}x)");
        if (top > heaviestCost * FrenzyCpuRatio)
            Fail($"the frenzy layer costs {top:0.00} ms per s, more than {FrenzyCpuRatio}x the heaviest track ({heaviestCost:0.00})");
        if (top > MusicBudgetMsPerSecond)
            Fail($"the frenzy layer costs {top:0.0} ms per s (budget {MusicBudgetMsPerSecond})");
        if (l6 > l4 * 1.25 + 0.1)
            Fail($"level 6 ({l6:0.00} ms per s) costs clearly more than level 4 ({l4:0.00}); levels 5 and 6 should only change gains");
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
