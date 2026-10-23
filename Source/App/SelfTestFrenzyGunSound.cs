using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Frenzy gun sound" (<see cref="FrenzyGunSound"/>): every class has a "sound" entry in
/// Weapons/Frenzy/classes.json with its own signature; the plan grows with the level (layer, drive, pitch shift, spread,
/// tier) and Subtle/Off scale it down; the bakes are sane, small and quick; a frenzied shot is never quieter and at
/// most <see cref="FrenzyGunSound.MaxLoudnessRatio"/> times as loud as the normal shot, for every weapon and class, and
/// its frenzy part grows with the level; tails extend with the tier; the class layers differ; a shot's planning
/// allocates nothing and is cheap; the data parser's fallbacks; the setting in settings.txt.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> FrenzyGunNotes = new();

    private static void FrenzyGunLine(string s)
    {
        Line(s);
        FrenzyGunNotes.Add(s);
    }

    private static void CheckFrenzyGunSound()
    {
        FrenzyGunNotes.Clear();
        FrenzyGunData();
        FrenzyGunPlans();
        FrenzyGunBakes();
        FrenzyGunCost();
        FrenzyGunParser();
        FrenzyGunSettings();
        Line("::notice title=Frenzy gun sound::" + string.Join("%0A", FrenzyGunNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    // ---- The shipped data: one sound entry per class, distinct signatures ----
    private static void FrenzyGunData()
    {
        var classes = Enum.GetValues<WeaponClass>();
        foreach (var c in classes)
        {
            var s = FrenzyClasses.Get(c).Sound;
            if (s.ValueKind != System.Text.Json.JsonValueKind.Object || !s.TryGetProperty("signature"u8, out _) || !s.TryGetProperty("levels"u8, out _))
                Fail($"frenzy gun sound: classes.json has no \"sound\" section (with signature and levels) for {c}");
        }
        var notes = new List<string>();
        var defs = FrenzyGunSound.Parse(FrenzyClasses.All, notes);
        foreach (var n in notes)
            Fail($"frenzy gun sound: classes.json: {n}");
        if (defs.Select(d => d.Signature).Distinct().Count() != classes.Length)
            Fail("frenzy gun sound: two classes share a signature (each class should have its own character)");
        FrenzyGunLine($"data: {classes.Length} classes, {notes.Count} notes; " + string.Join(", ", classes.Select(c => $"{c} {defs[(int)c].Signature}")));
    }

    // ---- The plan: monotonic in the level, Subtle under Full, Off and level 0 idle ----
    private static void FrenzyGunPlans()
    {
        var bad = 0;
        foreach (var c in Enum.GetValues<WeaponClass>())
        {
            var d = FrenzyGunSound.Get(c);
            if (FrenzyGunSound.Plan(0, c, FrenzyGunSoundMode.Full) != FrenzyGunSound.Idle || FrenzyGunSound.Plan(6, c, FrenzyGunSoundMode.Off) != FrenzyGunSound.Idle)
                bad++;
            if (!(d.Levels[^1] > d.Levels[0]))
                bad++;
            foreach (var mode in new[] { FrenzyGunSoundMode.Full, FrenzyGunSoundMode.Subtle })
            {
                var prev = FrenzyGunSound.Idle;
                for (var level = 1; level <= FrenzyMeterCore.MaxLevel; level++)
                {
                    var p = FrenzyGunSound.Plan(level, d, mode);
                    if (p.LayerGain < prev.LayerGain || p.DriveGain < prev.DriveGain || MathF.Abs(p.Pitch - 1f) < MathF.Abs(prev.Pitch - 1f)
                        || p.Spread < prev.Spread || p.Tier < prev.Tier || p.BaseGain > prev.BaseGain || !p.Active)
                    {
                        bad++;
                        Fail($"frenzy gun sound: {c} {mode}: level {level} is less intense than level {level - 1} ({p} after {prev})");
                    }
                    if (mode == FrenzyGunSoundMode.Subtle)
                    {
                        var full = FrenzyGunSound.Plan(level, d, FrenzyGunSoundMode.Full);
                        if (p.LayerGain > full.LayerGain || p.DriveGain > full.DriveGain || p.Spread > 0f || MathF.Abs(p.Pitch - 1f) > MathF.Abs(full.Pitch - 1f))
                        {
                            bad++;
                            Fail($"frenzy gun sound: {c}: Subtle at level {level} is more than Full");
                        }
                    }
                    prev = p;
                }
            }
        }
        if (bad > 0)
            Fail($"frenzy gun sound: {bad} plan problem(s)");
        var ar = FrenzyGunSound.Plan(6, WeaponClass.AssaultRifle, FrenzyGunSoundMode.Full);
        FrenzyGunLine($"plans: levels 1-6 monotonic for every class, Full and Subtle; AR level 6 {ar}");
    }

    // ---- Bakes: sanity, budget, loudness vs the normal shot, intensity growth, tails, distinct layers ----
    private static void FrenzyGunBakes()
    {
        var set = Audio.SynthesiseAll();
        _ = FrenzyGunSound.Bake(set); // warm-up (JIT)
        var bank = FrenzyGunSound.Bake(set);
        FrenzyGunLine($"bake: {bank.Count} buffers, {bank.Bytes / 1024} KB in {bank.BakeMs:0.0} ms");
        if (bank.Bytes > 6L * 1024 * 1024)
            Fail($"frenzy gun sound: bakes take {bank.Bytes / 1024} KB (budget 6 MB)");
        if (bank.BakeMs > 1500)
            Fail($"frenzy gun sound: baking took {bank.BakeMs:0} ms (budget 1500 ms)");
        var classes = Enum.GetValues<WeaponClass>();
        for (var c = 0; c < classes.Length; c++)
            for (var t = 0; t < FrenzyGunSound.Tiers; t++)
            {
                for (var v = 0; v < FrenzyGunSound.Variants; v++)
                {
                    var pcm = bank.Layers[c][t][v];
                    if (pcm.Length < Audio.SampleRate / 10 || bank.LayerRms[c][t][v] < 0.01f || !float.IsFinite(bank.LayerRms[c][t][v]))
                        Fail($"frenzy gun sound: {classes[c]} tier {t} variant {v}: {pcm.Length} samples, rms {bank.LayerRms[c][t][v]:0.000}");
                }
                if (t > 0 && bank.Layers[c][t][0].Length < bank.Layers[c][t - 1][0].Length)
                    Fail($"frenzy gun sound: {classes[c]}: tier {t}'s tail is shorter than tier {t - 1}'s");
            }
        for (var w = 0; w < bank.Drive.Length; w++)
            if (bank.Drive[w].Length == 0 || bank.DriveRms[w] < 0.01f || bank.ShotRms[w] < 0.01f)
                Fail($"frenzy gun sound: weapon {w}: driven copy rms {bank.DriveRms[w]:0.000}, shot rms {bank.ShotRms[w]:0.000}");

        // Every class on every weapon: a level-6 shot vs the normal one, and the frenzy part growing with the level.
        var win = GunSamples.LoudnessWindow;
        var mix = new float[win];
        var layer = new float[win];
        double minRatio = double.MaxValue, maxRatio = 0;
        string minAt = "", maxAt = "";
        var notMonotonic = 0;
        var notMonotonicAt = new List<string>();
        foreach (var w in Weapons.All)
        {
            var shot = set.Shots[w.Id][0];
            var baseRms = FrenzyGunSound.Rms(shot) * Audio.OwnShotGain;
            if (baseRms < 1e-4f)
                continue;
            for (var c = 0; c < classes.Length; c++)
            {
                var d = FrenzyGunSound.Get(classes[c]);
                var prevE = 0.0;
                double firstE = 0;
                for (var level = 1; level <= FrenzyMeterCore.MaxLevel; level++)
                {
                    var p = FrenzyGunSound.Plan(level, d, FrenzyGunSoundMode.Full);
                    var layers = bank.Layers[c][p.Tier];
                    var g = FrenzyGunSound.Gains(p, Audio.OwnShotGain, bank.ShotRms[w.Id], bank.DriveRms[w.Id], bank.LayerRms[c][p.Tier][0], false);
                    var drive = bank.Drive[w.Id];
                    // The driven copy plays in step with the shot (same pitch: they add coherently); the layer voices play at
                    // their own pitch, so their power adds.
                    for (var i = 0; i < win; i++)
                    {
                        mix[i] = ((i < drive.Length ? drive[i] * g.Drive : 0f) + (i < shot.Length ? shot[i] * g.Base : 0f)) / 32768f;
                        layer[i] = i < layers[0].Length ? layers[0][i] * g.Layer / 32768f : 0f;
                    }
                    var layerPower = Math.Pow(GunSamples.Rms(layer, win), 2) * (p.Spread > 0f ? 2 : 1);
                    var drivePart = FrenzyGunSound.Rms(drive) * g.Drive;
                    var e = Math.Sqrt(drivePart * (double)drivePart + layerPower); // the frenzy part's loudness
                    var total = Math.Sqrt(Math.Pow(GunSamples.Rms(mix, win), 2) + layerPower);
                    if (level == 1)
                        firstE = e;
                    if (e < prevE * 0.98 - 1e-4)
                    {
                        notMonotonic++;
                        notMonotonicAt.Add($"{w.Name} as {classes[c]} level {level} ({e:0.000} after {prevE:0.000})");
                    }
                    prevE = e;
                    if (level == FrenzyMeterCore.MaxLevel)
                    {
                        var ratio = total / baseRms;
                        if (ratio < minRatio)
                            (minRatio, minAt) = (ratio, $"{w.Name} as {classes[c]}");
                        if (ratio > maxRatio)
                            (maxRatio, maxAt) = (ratio, $"{w.Name} as {classes[c]}");
                        if (prevE < firstE * 1.5)
                        {
                            notMonotonic++;
                            notMonotonicAt.Add($"{w.Name} as {classes[c]}: level 6 {prevE:0.000} vs level 1 {firstE:0.000}");
                        }
                    }
                }
            }
        }
        FrenzyGunLine($"loudness: a level-6 shot is {minRatio:0.00}x ({minAt}) to {maxRatio:0.00}x ({maxAt}) the normal shot (allowed 0.97-{FrenzyGunSound.MaxLoudnessRatio:0.00}); the normal shot is unchanged, so the frenzy music's reference ({FrenzyLayer.SfxReferenceRms:0.000}) holds");
        if (minRatio < 0.97)
            Fail($"frenzy gun sound: a level-6 shot is quieter than the normal one ({minRatio:0.00}x, {minAt})");
        if (maxRatio > FrenzyGunSound.MaxLoudnessRatio)
            Fail($"frenzy gun sound: a level-6 shot is {maxRatio:0.00}x the normal one ({maxAt}; max {FrenzyGunSound.MaxLoudnessRatio})");
        if (notMonotonic > 0)
            Fail($"frenzy gun sound: {notMonotonic} weapon/class pair(s) whose frenzy layers don't grow with the level (or not 1.5x from level 1 to 6): {string.Join("; ", notMonotonicAt.Take(6))}");

        // The class layers sound different: low correlation between classes' top tiers.
        var n = Math.Min(8192, bank.Layers.Min(c => c[2][0].Length));
        var worst = 0.0;
        var worstPair = "";
        for (var a = 0; a < classes.Length; a++)
            for (var b = a + 1; b < classes.Length; b++)
            {
                double ab = 0, aa = 0, bb = 0;
                var x = bank.Layers[a][2][0];
                var y = bank.Layers[b][2][0];
                for (var i = 0; i < n; i++)
                {
                    ab += x[i] * (double)y[i];
                    aa += x[i] * (double)x[i];
                    bb += y[i] * (double)y[i];
                }
                var corr = Math.Abs(ab) / Math.Sqrt(Math.Max(aa * bb, 1e-9));
                if (corr > worst)
                    (worst, worstPair) = (corr, $"{classes[a]}/{classes[b]}");
            }
        FrenzyGunLine($"distinct: the most alike top-tier class layers correlate {worst:0.00} ({worstPair})");
        if (worst > 0.9)
            Fail($"frenzy gun sound: {worstPair} top-tier layers are nearly the same ({worst:0.00})");
    }

    // ---- Per shot: no allocation, cheap ----
    private static void FrenzyGunCost()
    {
        var classes = Enum.GetValues<WeaponClass>();
        var sum = 0f;
        void Shots(int calls)
        {
            for (var i = 0; i < calls; i++)
            {
                var p = FrenzyGunSound.Plan(i % 7, classes[i % classes.Length], (FrenzyGunSoundMode)(i % 3));
                var g = FrenzyGunSound.Gains(p, 0.75f, 0.3f, 0.4f, 0.2f, (i & 8) != 0);
                sum += g.Base + g.Drive + g.Layer + p.Pitch;
            }
        }
        Shots(4000); // warm-up with the same arguments as the measured rounds (every level, class, mode and flag)
        const int calls = 200_000;
        var sw = Stopwatch.StartNew();
        Shots(calls);
        var us = sw.Elapsed.TotalMilliseconds * 1000 / calls;
        // Allocations: the least of several identical rounds (a one-off runtime allocation, such as a JIT tier-up, lands
        // in one round at most; a real per-shot allocation shows in all of them).
        var bytes = SteadyAllocBytes(() => Shots(calls / 4));
        FrenzyGunLine($"per shot: {us:0.000} us to plan, {bytes} bytes allocated over {calls / 4} shots (least of 3 rounds; checksum {sum:0})");
        if (bytes > 0)
            Fail($"frenzy gun sound: planning a shot allocates ({bytes} bytes over {calls / 4})");
        if (us > 5)
            Fail($"frenzy gun sound: planning a shot takes {us:0.00} us (budget 5)");
    }

    // ---- The parser: bad levels, unknown signature, clamps, a missing section ----
    private static void FrenzyGunParser()
    {
        const string json = """
            { "format": "call-of-slop-frenzy-classes/1", "classes": {
              "Pistol": { "sound": { "signature": "Kazoo", "levels": [0.5, 0.2, 0.3, 0.4, 0.5, 1], "layer": 5, "pitch": 0.5 } },
              "Sniper": { "sound": { "signature": "whipcrack", "levels": [0, 0, 0.5, 0.5, 0.9, 1], "tail": 1 } },
              "SMG": { }
            } }
            """;
        var errors = new List<string>();
        var entries = FrenzyClasses.Parse(json, errors);
        var notes = new List<string>();
        var defs = FrenzyGunSound.Parse(entries, notes);
        var pistol = defs[(int)WeaponClass.Pistol];
        var sniper = defs[(int)WeaponClass.Sniper];
        var smg = defs[(int)WeaponClass.SMG];
        var defPistol = FrenzyGunSound.Default(WeaponClass.Pistol);
        var ok = pistol.Signature == defPistol.Signature && pistol.Levels.SequenceEqual(defPistol.Levels) && pistol.Layer == 0.6f && pistol.Pitch == 0.9f
                 && sniper.Signature == FrenzySoundSignature.WhipCrack && sniper.Levels[2] == 0.5f && sniper.Tail == 1f
                 && smg.Signature == FrenzySoundSignature.SmgBuzz
                 && notes.Any(n => n.Contains("Kazoo")) && notes.Any(n => n.StartsWith("Pistol") && n.Contains("levels"))
                 && notes.Any(n => n.StartsWith("SMG") && n.Contains("no \"sound\""));
        if (!ok)
            Fail($"frenzy gun sound: the parser's fallbacks are wrong: {string.Join("; ", notes)}");
        FrenzyGunLine($"parser: {notes.Count} notes for a broken file (bad levels, unknown signature, clamps, missing section), defaults kept");
    }

    // ---- settings.txt: frenzy_gunsound ----
    private static void FrenzyGunSettings()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-frenzygun-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllLines(tmp, ["name=Old", "fov=90", "frenzy_visuals=Subtle"]); // from before the setting
            var old = Settings.Load(tmp);
            var ok = new Settings().FrenzyGunSound == FrenzyGunSoundMode.Full && old.FrenzyGunSound == FrenzyGunSoundMode.Full && old.FrenzyVisuals == FrenzyVisualMode.Subtle;
            foreach (var m in Enum.GetValues<FrenzyGunSoundMode>())
            {
                new Settings { FrenzyGunSound = m }.Save(tmp);
                ok &= Settings.Load(tmp).FrenzyGunSound == m;
            }
            File.WriteAllLines(tmp, ["frenzy_gunsound=9"]);
            ok &= Settings.Load(tmp).FrenzyGunSound == FrenzyGunSoundMode.Full;
            File.WriteAllLines(tmp, ["frenzy_gunsound=loud"]);
            ok &= Settings.Load(tmp).FrenzyGunSound == FrenzyGunSoundMode.Full;
            File.WriteAllLines(tmp, ["frenzy_gunsound=subtle"]);
            ok &= Settings.Load(tmp).FrenzyGunSound == FrenzyGunSoundMode.Subtle;
            if (!ok)
                Fail("frenzy gun sound: the setting doesn't default to Full, round-trip Full/Subtle/Off, or reject junk");
            FrenzyGunLine($"settings: {string.Join(" / ", Enum.GetNames<FrenzyGunSoundMode>())}, default {new Settings().FrenzyGunSound}, old files load as Full");
        }
        finally
        {
            try { File.Delete(tmp); } catch (IOException) { }
        }
    }
}
