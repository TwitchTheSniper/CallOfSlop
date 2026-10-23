using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Weird hardbass": the weird hardbass tracks (MusicWeird.cs) are well formed, between them use every
/// weird trick (7/8 and 5/4, tempo ramps, the drunk breakdown, lo-fi and music-box swerves, microtones, accordion and
/// chip leads, toy sound effects, crushed and shifted chants), follow their timelines to the sample, and cost at most
/// about 1.5x the heaviest normal track. The new voices and the tape-deck bus are checked on their own. (The Music
/// section already renders them start to finish: NaNs, clipping, loudness window, determinism, allocations.)
/// </summary>
public static partial class SelfTest
{
    /// <summary>
    /// A weird track may cost this much more than the heaviest classic track: the user's 1.5x rule, as the median ratio
    /// of interleaved runs on the same machine (<see cref="CompareCpu"/>), not as a wall-clock cap. (It had room for
    /// timing noise on top while the check used wall-clock medians.)
    /// </summary>
    private const double WeirdCostRatioMax = 1.5;

    private static void CheckWeirdHardbass()
    {
        const int sr = Audio.SampleRate;
        var weird = MusicTracks.All.Where(t => t.Weird != null).ToArray();
        Line($"{weird.Length} weird hardbass tracks ({weird.Count(t => t.Set == MusicSet.Menu)} menu, {weird.Count(t => t.Set == MusicSet.Lobby)} lobby)");
        if (weird.Length < 4)
            Fail($"only {weird.Length} weird hardbass tracks (want 4+)");
        if (!weird.Any(t => t.Set == MusicSet.Menu) || !weird.Any(t => t.Set == MusicSet.Lobby))
            Fail("weird tracks belong in both the menu and the lobby");

        // ---- Well formed --------------------------------------------------------------------------------
        foreach (var t in weird)
        {
            var f = t.Weird!;
            if (t.Style != MusicStyle.Hardbass || t.Timeline == null)
                Fail($"{t.Name}: a weird recipe on a non-hardbass track");
            if (f.Sections.Length != t.Sections.Length)
                Fail($"{t.Name}: {f.Sections.Length} section codes for {t.Sections.Length} sections ({t.Form})");
            for (var i = 0; i < f.Sections.Length && i < t.Sections.Length; i++)
            {
                var c = f.Sections[i];
                if (!WeirdFlavor.Codes.Contains(c))
                    Fail($"{t.Name}: unknown section code '{c}'");
                if (WeirdFlavor.BreakCodes.Contains(c) && t.Sections[i].Part != MusicPart.Break)
                    Fail($"{t.Name}: section code '{c}' is for breaks, not a {t.Sections[i].Part}");
            }
            if (t.Seconds is < 90 or > 180)
                Fail($"{t.Name}: {t.Seconds:0} s long (want 90-180)");
            if (f.Sfx.Length is not (0 or 32) || f.Sfx.Any(c => ".qswvb".IndexOf(c) < 0) || ".qswvb".IndexOf(f.ExtraBeatSfx) < 0)
                Fail($"{t.Name}: bad sound effect pattern \"{f.Sfx}\"");
            if (f.Micro is { } m && (m.Length != 7 || m.Any(x => MathF.Abs(x) > 1f)))
                Fail($"{t.Name}: microtones need 7 offsets within a semitone");
            if (f.ChantCrush is < 0 or > 1 || MathF.Abs(f.ChantSemis) > 12 || MathF.Abs(f.ChantSemisAlt) > 12)
                Fail($"{t.Name}: chant crush or shift out of range");
            var tricks = WeirdTricks(t);
            if (tricks.Count < 3)
                Fail($"{t.Name}: only {tricks.Count} weird tricks ({string.Join(", ", tricks)}); weird tracks should be weird");
            Line($"{t.Name}: {t.Bpm} BPM, {t.Key}, {t.Seconds:0} s; {string.Join(", ", tricks)}");
        }

        // ---- Every trick is used somewhere ------------------------------------------------------------------
        var used = weird.SelectMany(WeirdTricks).ToHashSet();
        foreach (var want in new[] { "7/8", "5/4", "speed-up", "accelerando", "drunk breakdown", "lo-fi swerve", "music box swerve", "microtones",
                                     "accordion lead", "chip lead", "chip arpeggio", "quack", "squeak", "slide whistle", "boing", "crushed chants",
                                     "shifted chants", "out-of-tune harmony" })
            if (!used.Contains(want))
                Fail($"no weird track uses: {want}");

        // ---- Timelines -------------------------------------------------------------------------------------------
        foreach (var t in weird)
        {
            var tl = t.Timeline!;
            var step = 15.0 / t.Bpm;
            if (Math.Abs(tl.Seconds - t.BarStart(t.TotalBars + MusicTrack.TailBars)) > 1e-9 || Math.Abs(t.Seconds - tl.Seconds) > 1e-9)
                Fail($"{t.Name}: the timeline's length doesn't add up");
            for (var b = 0; b < t.TotalBars; b++)
            {
                var len = t.BarStart(b + 1) - t.BarStart(b);
                var c = tl.Code(b);
                var want = c == '7' ? 14 * step : c == '5' ? 20 * step : c == '.' ? 16 * step : -1;
                if (want > 0 && Math.Abs(len - want) > 1e-9)
                {
                    Fail($"{t.Name}: bar {b} ('{c}') lasts {len:0.000} s, want {want:0.000}");
                    break;
                }
                if (c == 'd' && len <= 16 * step)
                {
                    Fail($"{t.Name}: a drunk bar isn't slower ({len:0.000} s)");
                    break;
                }
                if (c == 'r' && len >= 16 * step + 1e-9 && b > 0 && tl.Code(b - 1) == 'r')
                {
                    Fail($"{t.Name}: an accelerando bar isn't faster ({len:0.000} s)");
                    break;
                }
                if (t.BarAt(t.BarStart(b) + len * 0.5) != b)
                {
                    Fail($"{t.Name}: BarAt doesn't find bar {b}");
                    break;
                }
            }

            // The renderer follows the timeline to the sample: from the last drop to the end in small blocks.
            var r = new MusicRenderer();
            var from = LastDropBar(t);
            r.Load(t, 11, from);
            var buf = new float[256 * 2];
            long frames = 0;
            var guard = (long)((t.Seconds + 5) * sr);
            var finite = true;
            while (!r.Finished && frames < guard)
            {
                r.Render(buf);
                frames += 256;
                foreach (var x in buf)
                    finite &= float.IsFinite(x);
            }
            var want2 = t.Seconds - t.BarStart(from);
            var got = frames / (double)sr;
            if (Math.Abs(got - want2) > 0.02 || !finite)
                Fail($"{t.Name}: from bar {from} it finished after {got:0.000} s, the timeline says {want2:0.000} s (finite {finite})");
        }

        // ---- The swerves sound like themselves: drunk and lo-fi bars are softer than the drops, the music box softer still --
        foreach (var t in weird)
        {
            var drop = Measure(RenderSeconds(new MusicRenderer(), t, 5, t.FirstDropBar, 2)).Rms;
            var bar = 0;
            for (var i = 0; i < t.Sections.Length; i++)
            {
                var c = t.Weird!.Sections[i];
                if (WeirdFlavor.BreakCodes.Contains(c))
                {
                    var x = Measure(RenderSeconds(new MusicRenderer(), t, 5, bar, 2)).Rms;
                    Line($"{t.Name}: '{c}' section at bar {bar}: rms {x:0.000} (first drop {drop:0.000})");
                    if (x < 0.01 || x > drop * 0.85)
                        Fail($"{t.Name}: the '{c}' swerve at bar {bar} has rms {x:0.000} (the drop {drop:0.000}): it should be audible and softer");
                }
                bar += t.Sections[i].Bars;
            }
        }

        CheckWeirdVoices();

        // ---- CPU: at most ~1.5x the heaviest classic track (not the weird or harder ones): the busiest stretch (first or
        // last drop) of each, as the median ratio of interleaved runs on this thread's CPU clock (CompareCpu) -----
        const double seconds = 1;
        var cr = new MusicRenderer();
        var spots = MusicTracks.All.Where(t => t.Weird == null && t.Hardbass?.Harder == null)
            .SelectMany(t => new[] { (Track: t, Bar: t.FirstDropBar), (Track: t, Bar: LastDropBar(t)) }).ToArray();
        TrackWork(cr, spots[0].Track, seconds, 1234, spots[0].Bar)()(); // JIT warm-up
        var refSpot = spots[0];
        var refMs = -1.0;
        var runs = new double[3];
        foreach (var sp in spots)
        {
            var w = TrackWork(cr, sp.Track, seconds, 1234, sp.Bar);
            for (var k = 0; k < runs.Length; k++)
                runs[k] = ThreadCpu.TimeMs(w());
            var ms = MedianOf(runs);
            if (ms > refMs)
                (refSpot, refMs) = (sp, ms);
        }
        var refWork = TrackWork(cr, refSpot.Track, seconds, 1234, refSpot.Bar);
        var refName = refSpot.Track.Name;
        var stat = new System.Text.StringBuilder($"heaviest classic track {refName} (bar {refSpot.Bar}, {ThreadCpu.Kind}, median of interleaved pairs);");
        var worstWeird = 0.0;
        foreach (var t in weird)
        {
            var first = CompareCpu(refWork, TrackWork(cr, t, seconds, 1234, t.FirstDropBar), 7);
            var last = CompareCpu(refWork, TrackWork(cr, t, seconds, 1234, LastDropBar(t)), 7);
            var ratio = Math.Max(first.Ratio, last.Ratio);
            var cost = Math.Max(first.CandMs, last.CandMs) / seconds;
            var reference = first.RefMs / seconds;
            worstWeird = Math.Max(worstWeird, ratio);
            var drop = Measure(RenderSeconds(new MusicRenderer(), t, 1234, t.FirstDropBar, 3)).Rms;
            stat.Append($" {t.Name} {t.Bpm}bpm {t.Seconds:0}s {cost:0.00}ms/s {ratio:0.00}x drop{drop:0.000};");
            if (ratio > WeirdCostRatioMax)
                Fail($"{t.Name}: costs {ratio:0.00}x the heaviest classic track {refName} (medians {cost:0.00} vs {reference:0.00} ms per s; allowed {WeirdCostRatioMax}x)");
            if (cost > MusicBudgetMsPerSecond)
                Fail($"{t.Name}: costs {cost:0.00} ms per s (budget {MusicBudgetMsPerSecond} for every track)");
        }
        CpuRatioNotes.Add($"weird tracks: worst {worstWeird:0.00}x of {refName}");
        Line("WEIRDSTAT " + stat);
    }

    /// <summary>The weird tricks a track uses, by name.</summary>
    private static List<string> WeirdTricks(MusicTrack t)
    {
        var f = t.Weird!;
        var list = new List<string>();
        void Add(bool on, string name)
        {
            if (on && !list.Contains(name))
                list.Add(name);
        }
        Add(f.Sections.Contains('7'), "7/8");
        Add(f.Sections.Contains('5'), "5/4");
        Add(f.Sections.Contains('u'), "speed-up");
        Add(f.Sections.Contains('r'), "accelerando");
        Add(f.Sections.Contains('d'), "drunk breakdown");
        Add(f.Sections.Contains('l'), "lo-fi swerve");
        Add(f.Sections.Contains('m'), "music box swerve");
        Add(f.Micro != null && f.Micro.Any(x => x != MathF.Round(x)), "microtones");
        Add(f.Lead == WeirdLead.Accordion, "accordion lead");
        Add(f.Lead == WeirdLead.Chip, "chip lead");
        Add(f.Lead == WeirdLead.MusicBox, "music box lead");
        Add(f.ChipArp, "chip arpeggio");
        var sfx = f.Sfx + f.ExtraBeatSfx;
        Add(sfx.Contains('q'), "quack");
        Add(sfx.Contains('s'), "squeak");
        Add(sfx.Contains('w') || sfx.Contains('v') || f.WhistleIntoDrops || f.Sections.Contains('d'), "slide whistle");
        Add(sfx.Contains('b'), "boing");
        Add(f.ChantCrush > 0, "crushed chants");
        Add(f.ChantSemis != 0 || f.ChantSemisAlt != 0, "shifted chants");
        Add(f.HarmonyCents != 0, "out-of-tune harmony");
        return list;
    }

    /// <summary>The new voices and the tape-deck bus on their own.</summary>
    private static void CheckWeirdVoices()
    {
        const int sr = Audio.SampleRate;
        var rng = new MusicRng(9);

        // Toys: every sound is audible, finite, ends, and differs from the others.
        var kinds = Enum.GetValues<ToySound>();
        var sounds = new float[kinds.Length][];
        var toy = new ToyFxVoice();
        var notes = new System.Text.StringBuilder("toys:");
        for (var k = 0; k < kinds.Length; k++)
        {
            toy.Reset();
            toy.Trigger(kinds[k]);
            var buf = new float[sr];
            for (var i = 0; i < buf.Length; i += 512)
                toy.Render(buf.AsSpan(i, Math.Min(512, buf.Length - i)), ref rng);
            sounds[k] = buf;
            var (peak, rms) = Measure(buf);
            var active = buf.Take(sr / 2).Count(x => MathF.Abs(x) > 0.01f);
            notes.Append($" {kinds[k]} peak {peak:0.00} ({active} loud samples);");
            if (!buf.All(float.IsFinite) || peak < 0.15f || peak > 1.2f || active < sr / 40)
                Fail($"toy {kinds[k]}: peak {peak:0.000}, {active} loud samples (want an audible, sane sound)");
            if (toy.Active || buf.Skip(sr * 7 / 10).Any(x => MathF.Abs(x) > 1e-4f))
                Fail($"toy {kinds[k]} doesn't end within 0.7 s");
        }
        Line(notes.ToString());
        for (var a = 0; a < kinds.Length; a++)
            for (var b = a + 1; b < kinds.Length; b++)
            {
                var c = Math.Abs(Correlation(sounds[a], sounds[b]));
                if (c > 0.6)
                    Fail($"toys {kinds[a]} and {kinds[b]} sound alike (correlation {c:0.00})");
            }

        // Accordion: loud enough, wide (the musette reeds beat left against right), finite, and quiet soon after AllOff.
        var acc = new AccordionVoice();
        acc.Reset();
        acc.NoteOn(0, 440f, 1f);
        acc.NoteOn(1, 349f, 0.6f);
        var al = new float[sr];
        var ar = new float[sr];
        for (var i = 0; i < sr / 4; i += 512)
            acc.Render(al.AsSpan(i, Math.Min(512, sr / 4 - i)), ar.AsSpan(i, Math.Min(512, sr / 4 - i)));
        acc.AllOff();
        for (var i = sr / 4; i < al.Length; i += 512)
            acc.Render(al.AsSpan(i, Math.Min(512, al.Length - i)), ar.AsSpan(i, Math.Min(512, al.Length - i)));
        var accRms = Math.Sqrt(al.Take(sr / 4).Sum(x => x * (double)x) / (sr / 4));
        var lr = Correlation(al.Take(sr / 4).ToArray(), ar.Take(sr / 4).ToArray());
        var accTail = al.Skip(sr * 3 / 4).Concat(ar.Skip(sr * 3 / 4)).Max(MathF.Abs);
        Line($"accordion: rms {accRms:0.000}, left/right correlation {lr:0.000}, {accTail:0.0000} 0.5 s after release");
        if (accRms < 0.05 || !al.Concat(ar).All(float.IsFinite) || lr > 0.995 || accTail > 1e-3f)
            Fail($"accordion: rms {accRms:0.000}, left/right correlation {lr:0.000}, tail {accTail:0.0000}");

        // Chip: a handful of output levels (4-bit), audible, ends after Off.
        var chip = new ChipVoice();
        chip.Reset();
        chip.Arp(220f, 277f, 330f, 1f, 0.15f);
        var cb = new float[sr];
        for (var i = 0; i < cb.Length; i += 512)
            chip.Render(cb.AsSpan(i, Math.Min(512, cb.Length - i)));
        var levels = cb.Take(sr / 5).Distinct().Count();
        var chipPeak = cb.Max(MathF.Abs);
        Line($"chip: {levels} distinct levels, peak {chipPeak:0.000}, active after 1 s: {chip.Active}");
        if (levels is < 2 or > 32 || chipPeak < 0.2f || !cb.All(float.IsFinite) || chip.Active)
            Fail($"chip: {levels} levels (want a few), peak {chipPeak:0.000}, still on {chip.Active}");

        // Music box: a pluck that rings and dies away.
        var box = new MusicBoxVoice();
        box.Reset();
        box.Strike(1046f, 0.8f);
        var bl = new float[sr * 2];
        var br = new float[sr * 2];
        for (var i = 0; i < bl.Length; i += 512)
            box.Render(bl.AsSpan(i, Math.Min(512, bl.Length - i)), br.AsSpan(i, Math.Min(512, bl.Length - i)));
        double early = 0, late = 0;
        for (var i = 0; i < sr / 10; i++)
            early += bl[i] * (double)bl[i];
        for (var i = sr; i < sr + sr / 10; i++)
            late += bl[i] * (double)bl[i];
        Line($"music box: energy 1 s after the pluck is {late / Math.Max(early, 1e-12):0.000} of the first 0.1 s");
        if (early < 1e-3 || late > early * 0.4 || !bl.All(float.IsFinite))
            Fail($"music box: early energy {early:0.0000}, late {late:0.0000} (want a pluck that decays)");

        // Tape deck: with nothing on it's a pure 300-sample delay; wow bends the signal, crush and crackle stay sane.
        var tape = new TapeBus();
        var sine = new float[sr];
        for (var i = 0; i < sine.Length; i += 2)
            sine[i] = sine[i + 1] = 0.5f * MathF.Sin(i * 0.5f * 440f * Dsp.TwoPi / sr);
        var plain = (float[])sine.Clone();
        tape.Reset();
        for (var i = 0; i < plain.Length; i += 1024)
            tape.Process(plain.AsSpan(i, Math.Min(1024, plain.Length - i)), ref rng);
        var err = 0f;
        for (var i = 600; i < plain.Length; i++)
            err = MathF.Max(err, MathF.Abs(plain[i] - sine[i - 600]));
        var wobbly = (float[])sine.Clone();
        tape.Reset();
        tape.Wow = 1f;
        tape.Crush = 0.6f;
        tape.Crackle = 1f;
        tape.LowpassHz = 2600f;
        for (var i = 0; i < wobbly.Length; i += 1024)
            tape.Process(wobbly.AsSpan(i, Math.Min(1024, wobbly.Length - i)), ref rng);
        var half = sine.Length / 2;
        var wc = Correlation(plain.Skip(half).ToArray(), wobbly.Skip(half).ToArray());
        var wpeak = wobbly.Max(MathF.Abs);
        Line($"tape deck: plain delay error {err:0.0000000}, wobbly vs plain correlation {wc:0.000}, peak {wpeak:0.000}");
        if (err > 1e-5f)
            Fail($"tape deck with everything off isn't a clean delay (error {err})");
        if (wc > 0.95 || !wobbly.All(float.IsFinite) || wpeak > 1f)
            Fail($"tape deck: wow/crush barely change the sound (correlation {wc:0.000}) or it misbehaves (peak {wpeak:0.000})");
    }
}
