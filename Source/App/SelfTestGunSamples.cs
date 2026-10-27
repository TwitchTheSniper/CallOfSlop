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
        var folder = Path.Combine(AppFolders.Data, GunSampleLibrary.FolderName);
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
        CheckGunVoices(lib);
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
            if (set!.Shots.Count < 2 || set.Guns.Count < 2)
                Fail($"{w.Name}: set {set.Name} has {set.Shots.Count} shot recording(s) from {set.Guns.Count} gun(s) (want 2+ from 2+)");
            var synth = Synth.Gunshot(w);
            var shots = GunSamples.Shots(lib, w, synth);
            var wantShots = set.Guns.Count > 0 ? GunSamples.Variants : Math.Min(GunSamples.Variants, set.Shots.Count);
            if (shots.Length != wantShots)
                Fail($"{w.Name}: {shots.Length} baked variants (want {wantShots})");
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
                // Lower bound 0.18: the synthesised pistols are almost square-wave dense over 80 ms; a recording's blast
                // decays inside that window, and the snappy bake limits its peak by at most
                // GunSamples.MaxPeakReductionDb (a sharp transient over raw RMS) and a bright gun's tone tilt comes after
                // the limiter at the same peak, so it lands well under their RMS.
                if (ratio is < 0.18 or > 1.25)
                    Fail($"{w.Name} #{v}: loudness {ratio:0.00}x the synth it replaces (want 0.18-1.25x)");
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

    /// <summary>
    /// Snappiness of the baked near shots before this work (main bebf410, measured the same way on CI run 39744671925): the
    /// median over every recorded weapon's variants. The snappier bake must beat it on attack, early energy and decay.
    /// </summary>
    private static readonly GunSnap.Numbers SnapBefore = new(PreRollMs: 0.2f, AttackMs: 3.0f, Early: 0.49f, CrestDb: 9.2f, DecayMs: 288f);

    /// <summary>
    /// The closest two weapons of one class may be by sonic fingerprint (dB, see <see cref="GunSnap.Distance"/>). For
    /// scale: before this work same-class pairs were 0.14-0.32 dB apart (they shared their recordings), and on this
    /// measure the two most alike distinct real firearms in the library (the Winchester Model 12 and Benelli Nova pump
    /// shotguns, untreated) are 0.46 dB apart, the median pair of firearms 1.7 dB.
    /// </summary>
    private const float MinSameClassDistanceDb = 0.6f;

    /// <summary>
    /// Per-weapon identity (<see cref="GunVoice"/>) and snappiness: the baked shots beat <see cref="SnapBefore"/>; every
    /// same-class pair is at least <see cref="MinSameClassDistanceDb"/> apart by fingerprint; identities are deterministic
    /// (independent of weapon order / id, the name hash is the same everywhere, two bakes are bit-identical); the
    /// "voice" block overrides and round-trips; the per-shot kit colour doesn't allocate.
    /// </summary>
    private static void CheckGunVoices(GunSampleLibrary lib)
    {
        var baked = Weapons.All.ToDictionary(d => d.Id, d => GunSamples.Shots(lib, d, Synth.Gunshot(d)));

        // ---- Snappiness ----
        var byClass = new SortedDictionary<string, List<short[]>>(StringComparer.Ordinal);
        foreach (var w in Weapons.All)
        {
            if (w.Sound is not (SoundStyle.Gun or SoundStyle.Boom) || lib.SetFor(w) == null)
                continue;
            if (!byClass.TryGetValue(w.Class.ToString(), out var list))
                byClass[w.Class.ToString()] = list = new();
            list.AddRange(baked[w.Id]);
        }
        var snap = GunSnap.Median(byClass.Values.SelectMany(v => v));
        GunLine($"snappiness (median of baked shots): {GunSnap.Describe(snap)}; before: {GunSnap.Describe(SnapBefore)}");
        foreach (var (cls, list) in byClass)
            Line($"  snappiness {cls}: {GunSnap.Describe(GunSnap.Median(list))}");
        if (snap.PreRollMs > 0.5f)
            Fail($"gunshots start {snap.PreRollMs:0.0} ms late (want 0.5 ms or less)");
        if (snap.AttackMs > Math.Min(1f, SnapBefore.AttackMs))
            Fail($"gunshot attack {snap.AttackMs:0.0} ms (want 1 ms or less, before {SnapBefore.AttackMs:0.0})");
        if (snap.Early < SnapBefore.Early + 0.03f)
            Fail($"gunshots put {snap.Early:0.00} of their energy in the first 30 ms (before {SnapBefore.Early:0.00}; want more)");
        if (snap.DecayMs > SnapBefore.DecayMs * 0.9f)
            Fail($"gunshot bodies take {snap.DecayMs:0} ms to fall 30 dB (before {SnapBefore.DecayMs:0}; want them drier)");
        if (snap.CrestDb < SnapBefore.CrestDb)
            Fail($"gunshot crest {snap.CrestDb:0.0} dB is below before ({SnapBefore.CrestDb:0.0}): the transient got squashed");

        // ---- Same-class distinctness: every pair in a class (recorded or synth) ----
        var prints = Weapons.All.Select(d => (W: d, F: GunSnap.Fingerprint(baked[d.Id]))).ToArray();
        var closest = new List<string>();
        var closeDetail = new List<string>();
        var pairs = 0;
        foreach (var g in prints.Where(p => !p.W.IsLauncher).GroupBy(p => p.W.Class)) // launchers have their own synth path and check (SelfTestLaunchers.cs)
        {
            var best = (D: float.MaxValue, A: "", B: "");
            var arr = g.ToArray();
            for (var i = 0; i < arr.Length; i++)
                for (var j = i + 1; j < arr.Length; j++)
                {
                    pairs++;
                    var d = GunSnap.Distance(arr[i].F, arr[j].F);
                    if (d < best.D)
                        best = (d, arr[i].W.Name, arr[j].W.Name);
                    if (d < MinSameClassDistanceDb)
                    {
                        Fail($"{arr[i].W.Name} and {arr[j].W.Name} ({g.Key}) sound alike: fingerprint distance {d:0.00} dB (want {MinSameClassDistanceDb:0.0}+)");
                        if (closeDetail.Count < 6)
                            foreach (var x in new[] { arr[i], arr[j] })
                                closeDetail.Add($"{x.W.Name} [{string.Join(" ", x.F.Select(f => f.ToString("0.0")))}] {(lib.SetFor(x.W) is { } xs ? xs.Name + " " + GunVoice.For(x.W, xs).Describe() : "synth")}");
                    }
                }
            if (arr.Length > 1)
                closest.Add($"{g.Key} {best.D:0.00} ({best.A} / {best.B})");
        }
        foreach (var cd in closeDetail)
            Line("  close: " + cd);
        GunLine($"distinct: {pairs} same-class pairs, closest per class in dB (want {MinSameClassDistanceDb:0.0}+): " + string.Join("; ", closest));

        // ---- Determinism ----
        if (GunVoice.NameHash("P9 Sidearm") != 1893677205u)
            Fail($"GunVoice.NameHash(\"P9 Sidearm\") = {GunVoice.NameHash("P9 Sidearm")} (want 1893677205 on every machine)");
        var probe = Weapons.All.FirstOrDefault(d => lib.SetFor(d) is { Guns.Count: > 1 } && d.Sound == SoundStyle.Gun);
        if (probe != null)
        {
            var moved = probe.Clone();
            moved.Id = (byte)(probe.Id ^ 0x55); // another position in the weapon list (another client's order)
            var set = lib.SetFor(probe)!;
            var again = GunSamples.Shots(lib, moved, Synth.Gunshot(probe));
            var same = again.Length == baked[probe.Id].Length && again.Zip(baked[probe.Id]).All(p => p.First.AsSpan().SequenceEqual(p.Second));
            if (GunVoice.For(moved, set).Describe() != GunVoice.For(probe, set).Describe() || !same)
                Fail($"{probe.Name}: its sound changed with its id / on a second bake");

            // ---- The "voice" block: pins the recording, round-trips through the weapon file ----
            var pinned = probe.Clone();
            var other = set.Guns.First(g => g != GunVoice.For(probe, set).MainName);
            pinned.Voice = new VoiceSpec { Recording = other, Pitch = 1.1f, Tone = -0.8f, Click = 0f };
            var pv = GunVoice.For(pinned, set);
            var back = WeaponFile.Load(WeaponFile.ToJson(pinned), "voice-test.json");
            var dPinned = GunSnap.Distance(GunSnap.Fingerprint(GunSamples.Shots(lib, pinned, Synth.Gunshot(probe))), GunSnap.Fingerprint(baked[probe.Id]));
            GunLine($"determinism: {probe.Name} same on another id and a second bake: {same}; voice block -> {pv.Describe()} ({dPinned:0.00} dB from stock), round trip {back.Voice?.ToJson()}");
            if (pv.MainName != other || MathF.Abs(pv.Pitch - 1.1f) > 1e-6f || pv.ClickLevel != 0f || back.Voice?.ToJson() != pinned.Voice.ToJson() || dPinned < MinSameClassDistanceDb)
                Fail($"voice block: main {pv.MainName} (want {other}), pitch {pv.Pitch}, click {pv.ClickLevel}, round trip {back.Voice?.ToJson()}, {dPinned:0.00} dB from stock");
            try
            {
                WeaponFile.Load(WeaponFile.ToJson(probe).TrimEnd().TrimEnd('}') + ", \"voice\": { \"pitch\": 3 } }", "bad-voice.json");
                Fail("voice block: a pitch of 3 was accepted");
            }
            catch (WeaponFileException)
            {
                // expected
            }
        }

        // ---- The per-shot kit colour: cached, no allocation ----
        var audio = new Audio();
        var all = Attachments.BySlot.SelectMany(x => x).ToArray();
        uint KitOf(AttachmentDef a) => Attachments.With(0, a.Slot, a.Index);
        var longBarrel = all.FirstOrDefault(a => a.Name == "Long Barrel");
        var shortBarrel = all.FirstOrDefault(a => a.Name == "Short Barrel");
        var kitted = Weapons.All[0].Clone();
        kitted.Kit = longBarrel != null ? KitOf(longBarrel) : 1u;
        // Warm up well past the JIT's tier-up (one call wasn't enough run alone with --selftest-only: 88 one-off bytes
        // landed in the measured window, which earlier sections had already paid in the full suite).
        for (var i = 0; i < 1000; i++)
            audio.KitVoiceForTest(kitted);
        // The least of a few identical rounds (SelfTestCpuRatio.cs): a real per-call allocation shows in every round, a one-off runtime allocation in one at most.
        var allocated = SteadyAllocBytes(() =>
        {
            for (var i = 0; i < 1000; i++)
                audio.KitVoiceForTest(kitted);
        }, warmups: 0);
        if (allocated > 0)
            Fail($"the kit sound colour allocates {allocated} bytes per 1000 shots");
        if (longBarrel != null && shortBarrel != null)
        {
            var (pl, _) = Audio.KitVoiceFor(KitOf(longBarrel));
            var (ps, _) = Audio.KitVoiceFor(KitOf(shortBarrel));
            Line($"  kit colour: long barrel pitch x{pl:0.000}, short barrel x{ps:0.000}; {allocated} bytes per 1000 shots");
            if (!(pl < 1f && ps > 1f))
                Fail($"kit colour: long barrel x{pl}, short barrel x{ps} (want lower / higher)");
        }

        foreach (var d in Weapons.All)
            if (lib.SetFor(d) is { } vs)
                Line($"  [gunvoice] {d.Name}: {GunVoice.For(d, vs).Describe()}");
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
            GunLine($"fallback: {missing} missing + {corrupt} corrupt file(s) logged; {rifleGun.Name} {rifleShots.Length} variants from {part.Sets["rifle"].Shots.Count} recordings (was {rifleSet.Shots.Count}); {smgGun.Name} {(smgShots.Length == 1 && smgShots[0] == smgSynth ? "synth" : "NOT synth")}");
            if (missing != smgFiles.Length + 1 || corrupt != 1)
                Fail($"fallback: {missing} missing / {corrupt} corrupt reported (want {smgFiles.Length + 1} / 1)");
            if (rifleShots.Length != GunSamples.Variants || part.Sets["rifle"].Shots.Count != rifleSet.Shots.Count - 2)
                Fail($"fallback: {rifleGun.Name} has {rifleShots.Length} variants (want {GunSamples.Variants}) from {part.Sets["rifle"].Shots.Count} recordings (want {rifleSet.Shots.Count - 2})");
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
