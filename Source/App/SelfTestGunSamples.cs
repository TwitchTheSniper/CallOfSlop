using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Gun samples" (<see cref="GunSampleLibrary"/>, <see cref="GunSamples"/>): the manifest loads with
/// no problems, every listed file exists and decodes as 16-bit mono 44.1 kHz, the assets stay small; every real firearm
/// resolves a set with at least 2 shot variants (novelty guns stay synth); the baked shots are as loud as the synth
/// they replace (within a window), never clip, differ between variants, a suppressor muffles them, and the far layer
/// exists; the source files sit in a loudness window; the WAV loader reads 16/24-bit mono/stereo at other rates; the
/// fallback (a missing file, a whole missing set, a corrupt file, no manifest); the frenzy level-6 reference; and the
/// start-up cost of loading and baking.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> GunSampleNotes = new();

    private static void GunLine(string s)
    {
        Line(s);
        GunSampleNotes.Add(s);
    }

    private static void CheckGunSamples()
    {
        GunSampleNotes.Clear();
        var folder = Path.Combine(AppContext.BaseDirectory, GunSampleLibrary.FolderName);
        var lib = GunSampleLibrary.Load(folder);
        GunLine($"[gunsamples] {lib.FilesLoaded}/{lib.FilesListed} files, {lib.BytesLoaded / 1024} KB, {lib.Sets.Count} sets, {lib.Rules.Count} rules, loaded in {lib.LoadMs:0.0} ms (cold)");
        if (!lib.ManifestFound)
            Fail($"no gun sample manifest in {folder}");
        foreach (var p in lib.Problems)
            Fail($"gun samples: {p}");
        if (lib.FilesLoaded < 22)
            Fail($"only {lib.FilesLoaded} gun sample files loaded");
        if (lib.BytesLoaded > 8L * 1024 * 1024)
            Fail($"gun samples take {lib.BytesLoaded / 1024} KB (budget 8 MB)");
        CheckGunSampleFiles(lib, folder);
        CheckGunSampleBakes(lib);
        CheckWavLoader();
        CheckGunSampleFallback(folder);
        CheckGunSampleCost(folder);

        var sfx = Audio.CombatReferenceRms();
        GunLine($"frenzy reference with recorded gunshots: {sfx.Combined:0.000} rms (gunfire median {sfx.GunfireMedian:0.000}, explosion {sfx.Explosion:0.000}); constant {FrenzyLayer.SfxReferenceRms:0.000}");
        if (Math.Abs(sfx.Combined - FrenzyLayer.SfxReferenceRms) > 0.1 * sfx.Combined)
            Fail($"FrenzyLayer.SfxReferenceRms {FrenzyLayer.SfxReferenceRms:0.000} is off from the measured {sfx.Combined:0.000}");

        foreach (var l in lib.Describe(Weapons.All))
            Line(l);
        Line("::notice title=Gun samples::" + string.Join("%0A", GunSampleNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>Every listed file: canonical 16-bit mono 44.1 kHz, a sane loudness (RMS of the first 200 ms) and no full-scale peaks.</summary>
    private static void CheckGunSampleFiles(GunSampleLibrary lib, string folder)
    {
        var shots = new List<double>();
        foreach (var set in lib.Sets.Values)
            foreach (var file in set.ShotFiles)
            {
                var bytes = File.ReadAllBytes(Path.Combine(folder, file));
                var channels = BitConverter.ToUInt16(bytes, 22);
                var rate = BitConverter.ToInt32(bytes, 24);
                var bits = BitConverter.ToUInt16(bytes, 34);
                if (channels != 1 || rate != Audio.SampleRate || bits != 16)
                    Fail($"{file}: {channels} ch, {rate} Hz, {bits}-bit (want mono 44.1 kHz 16-bit)");
                var pcm = WavFile.ReadMono(bytes, Audio.SampleRate);
                var rms = GunSamples.Rms(pcm, Audio.SampleRate / 5);
                var peak = pcm.Max(x => MathF.Abs(x));
                shots.Add(rms);
                if (rms is < 0.08f or > 0.3f)
                    Fail($"{file}: loudness {rms:0.000} rms (want 0.08-0.3)");
                if (peak >= 0.995f)
                    Fail($"{file}: peak {peak:0.000} (clipping)");
            }
        if (shots.Count > 0)
            GunLine($"shot files: {shots.Count}, loudness {shots.Min():0.000}-{shots.Max():0.000} rms (first 200 ms, {20 * Math.Log10(shots.Max() / shots.Min()):0.0} dB spread)");
    }

    /// <summary>Every weapon's baked buffers: sets resolved, variants, loudness vs the synth, no clipping, suppressor, far layer.</summary>
    private static void CheckGunSampleBakes(GunSampleLibrary lib)
    {
        var ratios = new List<(string Name, double Ratio)>();
        var sampled = 0;
        foreach (var w in Weapons.All)
        {
            var set = lib.SetFor(w);
            var real = w.Sound is SoundStyle.Gun or SoundStyle.Boom or SoundStyle.Suppressed;
            if (real && set == null)
            {
                Fail($"{w.Name} ({w.Class}, {w.Sound}) has no gun sample set");
                continue;
            }
            if (!real)
            {
                if (set != null)
                    Fail($"{w.Name}: a {w.Sound} weapon picked the recorded set {set.Name}");
                continue;
            }
            sampled++;
            if (set!.Shots.Count < 2)
                Fail($"{w.Name}: set {set.Name} has {set.Shots.Count} shot variant(s) (want 2+)");
            var synth = Synth.Gunshot(w);
            var shots = GunSamples.Shots(lib, w, synth);
            if (shots.Length != set.Shots.Count)
                Fail($"{w.Name}: {shots.Length} baked variants for {set.Shots.Count} recordings");
            var synthRms = PcmRms(synth, GunSamples.LoudnessWindow);
            foreach (var (s, v) in shots.Select((s, v) => (s, v)))
            {
                if (s.Length < Audio.SampleRate / 5)
                    Fail($"{w.Name} #{v}: only {s.Length} samples");
                var clipped = s.Count(x => Math.Abs((int)x) >= 32760);
                if (clipped > 0)
                    Fail($"{w.Name} #{v}: {clipped} clipped samples");
                var ratio = PcmRms(s, GunSamples.LoudnessWindow) / synthRms;
                ratios.Add(($"{w.Name} #{v}", ratio));
                // Lower bound 0.45: the synthesised pistols are almost square-wave dense over 80 ms; a recording's blast
                // decays inside that window, so through the limiter it tops out near 0.55-0.6x their RMS (as loud as it
                // can be without audible squashing).
                if (ratio is < 0.45 or > 1.25)
                    Fail($"{w.Name} #{v}: loudness {ratio:0.00}x the synth it replaces (want 0.45-1.25x)");
            }
            for (var v = 1; v < shots.Length; v++)
                if (shots[v].AsSpan().SequenceEqual(shots[0]))
                    Fail($"{w.Name}: variant {v} is identical to variant 0");

            if (w.Sound is SoundStyle.Gun or SoundStyle.Boom)
            {
                var quiet = w.Clone();
                quiet.Sound = SoundStyle.Suppressed;
                var thwip = Synth.Gunshot(quiet);
                var supp = GunSamples.Suppressed(lib, w, thwip);
                var wantSupp = Math.Min(GunSamples.SuppressedVariants, shots.Length);
                if (supp.Length != wantSupp || supp.Any(s => s.Any(x => Math.Abs((int)x) >= 32760)))
                    Fail($"{w.Name}: suppressed variants {supp.Length} (want {wantSupp}) or clipping");
                else if (Features(supp[0]).Bright > Features(thwip).Bright * 1.05f)
                    Fail($"{w.Name}: the recording made the suppressed shot brighter ({Features(supp[0]).Bright:0.000} vs the synth's {Features(thwip).Bright:0.000}); it should be muffled under it");
                var far = GunSamples.Far(lib, w, Synth.GunshotDistant(w));
                if (far == null || far.Length < Audio.SampleRate / 2 || far.Any(x => Math.Abs((int)x) >= 32760))
                    Fail($"{w.Name}: far layer missing, short or clipping");
            }
        }
        ratios.Sort((a, b) => a.Ratio.CompareTo(b.Ratio));
        if (ratios.Count > 0)
            GunLine($"baked: {sampled} weapons from recordings, {Weapons.All.Length - sampled} synth only; loudness vs synth {ratios[0].Ratio:0.00}x ({ratios[0].Name}) to {ratios[^1].Ratio:0.00}x ({ratios[^1].Name}), median {ratios[ratios.Count / 2].Ratio:0.00}x");
    }

    private static double PcmRms(short[] s, int window)
    {
        var n = Math.Min(window, s.Length);
        double sum = 0;
        for (var i = 0; i < n; i++)
            sum += (double)s[i] * s[i];
        return Math.Sqrt(sum / Math.Max(1, n)) / 32768.0;
    }

    /// <summary>The WAV loader on hand-made files: 16-bit mono at 44.1 kHz, 24-bit stereo at 48 kHz (mixed and resampled), and junk.</summary>
    private static void CheckWavLoader()
    {
        var mono = new short[441];
        for (var i = 0; i < mono.Length; i++)
            mono[i] = (short)(i * 50 - 11000);
        var back = WavFile.ReadMono(WavFile.Write16(mono, 44100), 44100);
        if (back.Length != mono.Length || Math.Abs(back[100] - mono[100] / 32768f) > 1e-6f)
            Fail($"WAV 16-bit mono: {back.Length} samples, [100] = {back[100]}");

        // 24-bit stereo 48 kHz: left +0.5, right -0.25, a fmt chunk with an odd-sized extra chunk before it.
        const int frames = 4800;
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var data = frames * 6;
            bw.Write("RIFF"u8);
            bw.Write(4 + 8 + 3 + 1 + 8 + 16 + 8 + data);
            bw.Write("WAVE"u8);
            bw.Write("junk"u8);
            bw.Write(3);
            bw.Write(new byte[4]); // 3 bytes + the pad byte
            bw.Write("fmt "u8);
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)2);
            bw.Write(48000);
            bw.Write(48000 * 6);
            bw.Write((short)6);
            bw.Write((short)24);
            bw.Write("data"u8);
            bw.Write(data);
            for (var i = 0; i < frames; i++)
            {
                WriteInt24(bw, 4194304); // +0.5
                WriteInt24(bw, -2097152); // -0.25
            }
        }
        var st = WavFile.ReadMono(ms.ToArray(), 44100);
        if (Math.Abs(st.Length - 4410) > 1 || Math.Abs(st[2000] - 0.125f) > 1e-4f)
            Fail($"WAV 24-bit stereo 48 kHz: {st.Length} samples (want 4410), [2000] = {st[2000]:0.0000} (want 0.125)");
        try
        {
            WavFile.ReadMono("RIFF0000WAVEnope"u8, 44100);
            Fail("WAV loader accepted junk");
        }
        catch (InvalidDataException)
        {
            // expected
        }
    }

    /// <summary>Synth-only audio start-up on the user's PC (the [audio] log line there), the reference for the cost budget.</summary>
    private const double UserPcSynthMs = 58;

    private static void GunSlowdownLine(double extra, double budget, double with, double without, double loadMs) =>
        GunLine($"start-up: load {loadMs:0.0} ms + synthesis with samples {with:0} ms vs synth only {without:0} ms = {extra:0} ms extra on {Environment.ProcessorCount} threads "
            + $"(budget {budget:0} ms here; about {extra * UserPcSynthMs / Math.Max(without, 1):0} ms at the user's PC speed, budget 100)");

    private static void WriteInt24(BinaryWriter bw, int v)
    {
        bw.Write((byte)(v & 0xFF));
        bw.Write((byte)((v >> 8) & 0xFF));
        bw.Write((byte)((v >> 16) & 0xFF));
    }

    /// <summary>A copy of the folder with a file removed, a whole set removed, a corrupt file and no manifest: the synth covers each.</summary>
    private static void CheckGunSampleFallback(string folder)
    {
        var dir = Path.Combine(Path.GetTempPath(), "cos-gunsamples-" + Environment.ProcessId);
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(folder))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)));
            var full = GunSampleLibrary.Load(dir);
            var smgGun = Weapons.All.FirstOrDefault(w => full.SetFor(w)?.Name == "smg" && w.Sound == SoundStyle.Gun);
            var rifleGun = Weapons.All.FirstOrDefault(w => full.SetFor(w)?.Name == "rifle" && w.Sound == SoundStyle.Gun);
            if (smgGun == null || rifleGun == null)
            {
                Fail("fallback test: no SMG / rifle weapon on the smg / rifle sets");
                return;
            }
            var rifleSet = full.Sets["rifle"];
            var smgFiles = full.Sets["smg"].ShotFiles.ToArray();

            // One rifle shot missing: one variant fewer, the rest still recorded.
            File.Delete(Path.Combine(dir, rifleSet.ShotFiles[0]));
            // A corrupt rifle shot: logged, skipped.
            File.WriteAllBytes(Path.Combine(dir, rifleSet.ShotFiles[1]), "RIFF\0\0\0\0WAVEjunk"u8.ToArray());
            // Every SMG shot missing: SMGs fall back to the synth.
            foreach (var f in smgFiles)
                File.Delete(Path.Combine(dir, f));
            var part = GunSampleLibrary.Load(dir);
            var missing = part.Problems.Count(p => p.StartsWith("missing", StringComparison.Ordinal));
            var corrupt = part.Problems.Count - missing;
            var rifleShots = GunSamples.Shots(part, rifleGun, Synth.Gunshot(rifleGun));
            var smgSynth = Synth.Gunshot(smgGun);
            var smgShots = GunSamples.Shots(part, smgGun, smgSynth);
            GunLine($"fallback: {missing} missing + {corrupt} corrupt file(s) logged; {rifleGun.Name} {rifleShots.Length} variants (was {rifleSet.Shots.Count}); {smgGun.Name} {(smgShots.Length == 1 && smgShots[0] == smgSynth ? "synth" : "NOT synth")}");
            if (missing != smgFiles.Length + 1 || corrupt != 1)
                Fail($"fallback: {missing} missing / {corrupt} corrupt reported (want {smgFiles.Length + 1} / 1)");
            if (rifleShots.Length != rifleSet.Shots.Count - 2)
                Fail($"fallback: {rifleGun.Name} has {rifleShots.Length} variants (want {rifleSet.Shots.Count - 2})");
            if (smgShots.Length != 1 || smgShots[0] != smgSynth)
                Fail($"fallback: {smgGun.Name} with its set gone didn't fall back to the synth");
            if (part.SetFor(smgGun) != null || GunSamples.Far(part, smgGun, Synth.GunshotDistant(smgGun)) is not { } far || far.Length == 0)
                Fail($"fallback: {smgGun.Name}'s far layer without its set");

            // No manifest at all: an empty library, everything synth, and SynthesiseAll still works.
            File.Delete(Path.Combine(dir, "manifest.json"));
            var none = GunSampleLibrary.Load(dir);
            if (none.ManifestFound || none.Sets.Count != 0 || none.Problems.Count != 1)
                Fail($"fallback: without a manifest: found {none.ManifestFound}, {none.Sets.Count} sets, {none.Problems.Count} problems");
            var set = Audio.SynthesiseAll(none);
            if (Weapons.All.Any(w => set.Shots[w.Id].Length != 1))
                Fail("fallback: synth-only SynthesiseAll gave a weapon more than one shot variant");
        }
        catch (Exception ex)
        {
            Fail($"fallback test threw {ex.GetType().Name}: {ex.Message}");
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

    /// <summary>
    /// Start-up cost: loading the files (cold) and the extra time SynthesiseAll takes with them vs synth only (best of
    /// 3 each, after warm-up). Budget: 100 ms more than synth only (the brief's limit on the user's PC; CI runners are slower, so this is generous there).
    /// </summary>
    private static void CheckGunSampleCost(string folder)
    {
        var load = GunSampleLibrary.Load(folder);
        double Best(GunSampleLibrary l)
        {
            var best = double.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var sw = Stopwatch.StartNew();
                _ = Audio.SynthesiseAll(l);
                best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        _ = Audio.SynthesiseAll(load); // warm-up
        var with = Best(load);
        var without = Best(GunSampleLibrary.Empty);
        var extra = with - without + load.LoadMs;
        // The budget is 100 ms on the user's PC, where synth-only start-up takes about 58 ms; CI runners (2 slow
        // threads) take ~10x longer for the same work, so the budget scales with the synth-only time measured here.
        var budget = 100.0 * Math.Max(1.0, without / UserPcSynthMs);
        GunSlowdownLine(extra, budget, with, without, load.LoadMs);
        if (extra > budget)
            Fail($"gun samples add {extra:0} ms to audio start-up (budget {budget:0} ms here = 100 ms at the user's PC speed)");
    }
}
