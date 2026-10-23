using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Audio mixer" (Source/Audio/AudioMix.cs, AudioMixDefaults.cs, Audio.Mixer.cs, UiAudioMixer.cs):
/// the defaults table (keys unique and stable, levels in <see cref="MixBus"/> order, groups together, every sound on a
/// bus); the baked default mix (SeabMix2: the expected table below); with no audio_mix.json every value is the
/// pre-mixer behaviour times the default levels exactly (each gain 10^(default/20), the gun-mix falloff, crossfade and
/// distance filter equal the pre-mixer formulas, every shot plan's layer gains unchanged, the music trims, the limiter
/// ceiling); the JSON round trip, unknown and missing keys, clamping and bad files, an old save's untouched levels moving
/// to the new defaults; each level slider changes only its own
/// category's planned gain; mute; the F8 key's binds migration; and no allocations while the window is closed or in the
/// meters. Headless, temp files only.
/// </summary>
public static partial class SelfTest
{
    private static void CheckAudioMixer()
    {
        var entries = AudioMixDefaults.Entries;
        const int levels = (int)MixBus.COUNT, shape = (int)MixParam.COUNT;

        // ---- The table ---------------------------------------------------------------------------------------
        if (entries.Length != levels + shape)
            Fail($"audio mixer: the defaults table has {entries.Length} rows for {levels} levels + {shape} shape values");
        var dupKeys = entries.GroupBy(e => e.Key).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (dupKeys.Length > 0)
            Fail($"audio mixer: duplicate keys {string.Join(", ", dupKeys)}");
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.IsLevel != (i < levels))
                Fail($"audio mixer: row {i} ({e.Key}) should be a {(i < levels ? "level" : "shape value")}");
            if (e.Default < e.Min || e.Default > e.Max)
                Fail($"audio mixer: {e.Key} default {e.Default} outside its range {e.Min}..{e.Max}");
            if (!System.Text.RegularExpressions.Regex.IsMatch(e.Key, "^[a-z0-9_.]+$"))
                Fail($"audio mixer: key '{e.Key}' should be lower-case letters, digits, '_' and '.'");
        }
        var seenGroups = new List<string>();
        foreach (var e in entries)
            if (seenGroups.Count == 0 || seenGroups[^1] != e.Group)
            {
                if (seenGroups.Contains(e.Group))
                    Fail($"audio mixer: group '{e.Group}' is split in two (rows of a group must sit together)");
                seenGroups.Add(e.Group);
            }
        var autoBus = new int[levels];
        for (var s = 0; s < (int)Sfx.COUNT; s++)
            foreach (var world in new[] { false, true })
            {
                var b = Audio.BusFor((Sfx)s, world);
                if ((uint)b >= levels || b == MixBus.Master)
                    Fail($"audio mixer: {(Sfx)s} ({(world ? "world" : "ear")}) has no level row ({b})");
                else
                    autoBus[(int)b]++;
            }
        Line($"table: {levels} levels in {seenGroups.Count - 1} groups + {shape} distance/limiter values; keys unique; every sound effect on a bus; factory values {(AudioMixDefaults.IsFactory ? "yes" : "no (a mix is baked in)")}");
        Line("levels: " + string.Join(", ", entries.Take(levels).Select(e => e.Key)));

        // ---- The baked mix: SeabMix2 (2026-10-23) --------------------------------------------------------------
        var bakedBad = new List<string>();
        for (var i = 0; i < levels; i++)
        {
            var want = BakedLevels.TryGetValue(entries[i].Key, out var w) ? w : 0f;
            if (entries[i].Default != want)
                bakedBad.Add($"{entries[i].Key} {entries[i].Default:+0.0;-0.0;0} (want {want:+0.0;-0.0;0})");
        }
        if (bakedBad.Count > 0 || AudioMixDefaults.BakedMix != "SeabMix2" || !AudioMixDefaults.ShapeIsFactory || AudioMixDefaults.IsFactory)
            Fail($"audio mixer: the baked default mix ({AudioMixDefaults.BakedMix}) isn't SeabMix2 (shape factory {AudioMixDefaults.ShapeIsFactory}): {string.Join(", ", bakedBad)}");
        Line($"baked mix: {AudioMixDefaults.BakedMix}, {BakedLevels.Count} levels off 0 dB (" + string.Join(", ", BakedLevels.Select(kv => $"{kv.Key} {kv.Value:+0.0;-0.0}")) + $"), shape factory: {(bakedBad.Count == 0 ? "OK" : "WRONG")}");

        // ---- Identity: no audio_mix.json = the pre-mixer game times the default levels -------------------------
        var mix = new AudioMix();
        {
            var badGain = Enumerable.Range(0, levels).Where(i => mix.Gain((MixBus)i) != AudioMix.DbToGain(entries[i].Default) || AudioMix.Defaults.Gain((MixBus)i) != mix.Gain((MixBus)i)
                                                                 || entries[i].Default == 0f && mix.Gain((MixBus)i) != 1f).Select(i => entries[i].Key).ToArray();
            if (badGain.Length > 0)
                Fail($"audio mixer identity: gain not exactly 10^(default/20) for {string.Join(", ", badGain)}");
            if (!mix.IsDefault || mix.Changed != 0 || !AudioMix.Defaults.IsDefault)
                Fail($"audio mixer identity: a fresh mix isn't the defaults ({mix.Changed} changed)");
        }
        if (AudioMixDefaults.ShapeIsFactory)
        {
            if (mix.ShotFalloff != new Audio.Falloff(5f, 0.8f, 250f, Thin: true) || Audio.ShotFalloff != mix.ShotFalloff)
                Fail($"audio mixer identity: the gunshot falloff is {mix.ShotFalloff}, not 5 m / 0.8 / 250 m");
            var mism = 0;
            for (var d = 0f; d <= 400f; d += 0.25f)
            {
                var x = Math.Clamp((d - 25f) / 65f, 0f, 1f);
                var far = x * x * (3 - 2 * x);
                var lf = Math.Clamp(MathF.Exp(-MathF.Max(0f, d - 8f) / 35f), 0.15f, 1f);
                var hf = Math.Clamp(MathF.Exp(-MathF.Max(0f, d - 8f) / 55f), 0.12f, 1f);
                if (mix.FarMix(d) != far || Audio.FarMix(d) != far || mix.DistanceLf(d) != lf || Audio.DistanceLf(d) != lf || mix.DistanceHf(d) != hf || Audio.DistanceHf(d) != hf)
                    mism++;
                foreach (var own in new[] { false, true })
                foreach (var sup in new[] { false, true })
                foreach (var hasFar in new[] { false, true })
                foreach (var launcher in new[] { false, true })
                {
                    var p = Audio.PlanShot(mix, own, sup, hasFar, d, launcher);
                    var (nearL, farL, fallL) = LegacyPlan(own, sup, hasFar, d);
                    float gn = mix.Gain(p.NearBus), gf = mix.Gain(p.FarBus);
                    if (p.Near != nearL || p.Far != farL || p.NearFalloff != fallL || p.FarFalloff != fallL || p.NearBusGain != gn || p.FarBusGain != gf
                        || p.NearMixed != nearL * gn || p.FarMixed != farL * gf)
                        mism++;
                }
            }
            if (mism > 0)
                Fail($"audio mixer identity: {mism} distance/plan value(s) differ from the pre-mixer formulas times the default levels");
            var audio = new Audio();
            audio.SyncMix();
            var music = audio.Music;
            var trims = Enumerable.Range(1, 6).Select(l => music.Frenzy.LevelTrim(l)).ToArray();
            var wantTrims = Enumerable.Range(1, 6).Select(l => mix.Gain((MixBus)((int)MixBus.Frenzy1 + l - 1))).ToArray();
            if (music.MenuTrim != mix.Gain(MixBus.MusicMenu) || music.LobbyTrim != mix.Gain(MixBus.MusicLobby) || !trims.SequenceEqual(wantTrims) || music.LimiterCeiling != FrenzyLayer.LimiterCeiling)
                Fail($"audio mixer identity: music trims menu {music.MenuTrim}, lobby {music.LobbyTrim}, frenzy {string.Join("/", trims)}, limiter {music.LimiterCeiling} (want the default levels' gains and {FrenzyLayer.LimiterCeiling})");
            Line($"identity: every gain 10^(default/20) (1 at 0 dB), falloff 5 m/0.8/250 m, crossfade/bass/air equal to the old formulas at 1601 distances x 16 shot kinds, "
                 + $"shot layers unchanged with the default bus gains (own {AudioMix.GainToDb(mix.Gain(MixBus.GunOwn)):+0.0} dB, near {AudioMix.GainToDb(mix.Gain(MixBus.GunOtherNear)):+0.0} dB, far {AudioMix.GainToDb(mix.Gain(MixBus.GunOtherFar)):+0.0} dB), "
                 + $"music trims {music.MenuTrim:0.##}/{music.LobbyTrim:0.##}/{string.Join("/", trims.Select(t => t.ToString("0.##")))}, limiter {music.LimiterCeiling}: {(mism == 0 ? "OK" : "DIFFERENT")}");
            audio.Dispose();
        }
        else
            Line("identity: shape part skipped (the defaults table holds a baked-in shape)");

        // ---- A slider changes only its category --------------------------------------------------------------
        var leaks = new List<string>();
        for (var b = 1; b < levels; b++)
        {
            var m = new AudioMix();
            m.SetDb((MixBus)b, 6f);
            for (var o = 0; o < levels; o++)
                if (o != b && m.Gain((MixBus)o) != AudioMix.Defaults.Gain((MixBus)o))
                    leaks.Add($"{entries[b].Key}->{entries[o].Key}");
            if (MathF.Abs(m.Gain((MixBus)b) - MathF.Pow(10f, 0.3f)) > 1e-5f)
                leaks.Add($"{entries[b].Key} +6 dB gain {m.Gain((MixBus)b)}");
            foreach (var own in new[] { false, true })
            foreach (var sup in new[] { false, true })
            foreach (var launcher in new[] { false, true })
            foreach (var d in new[] { 5f, 50f, 120f })
            {
                var p = Audio.PlanShot(m, own, sup, true, d, launcher);
                var expectNear = p.NearBus == (MixBus)b ? m.Gain((MixBus)b) : AudioMix.Defaults.Gain(p.NearBus);
                var expectFar = p.FarBus == (MixBus)b ? m.Gain((MixBus)b) : AudioMix.Defaults.Gain(p.FarBus);
                if (p.NearBusGain != expectNear || p.FarBusGain != expectFar)
                    leaks.Add($"{entries[b].Key}: shot plan own={own} sup={sup} launcher={launcher} {d} m");
            }
        }
        var bm = new AudioMix();
        bm.SetDb(MixBus.GunOtherFar, -12f);
        var near5 = Audio.PlanShot(bm, false, false, true, 5f);
        var far120 = Audio.PlanShot(bm, false, false, true, 120f);
        var own0 = Audio.PlanShot(bm, true, false, true, 0f);
        if (near5.NearBus != MixBus.GunOtherNear || far120.FarBus != MixBus.GunOtherFar || own0.NearBus != MixBus.GunOwn
            || Audio.PlanShot(bm, true, true, true, 0f).NearBus != MixBus.GunOwnSuppressed || Audio.PlanShot(bm, false, true, true, 30f).NearBus != MixBus.GunOtherSuppressed
            || Audio.PlanShot(bm, true, false, false, 0f, true).NearBus != MixBus.LauncherOwn || Audio.PlanShot(bm, false, false, false, 30f, true).NearBus != MixBus.LauncherOther)
            leaks.Add("shot plans on the wrong buses");
        if (Math.Abs(far120.FarMixed / far120.Far - AudioMix.DbToGain(-12f)) > 1e-5f || own0.NearMixed != own0.Near * AudioMix.Defaults.Gain(MixBus.GunOwn))
            leaks.Add("distant layer -12 dB doesn't scale only the far layer");
        if (Audio.BusFor(StepKind.Run, true) != MixBus.StepOwn || Audio.BusFor(StepKind.Walk, false) != MixBus.StepOther
            || Audio.BusFor(StepKind.Land, true) != MixBus.FoleyOwn || Audio.BusFor(StepKind.Jump, false) != MixBus.FoleyOther)
            leaks.Add("footstep buses");
        if (Audio.BusFor(Sfx.GibSquelch, true) != MixBus.Gore || Audio.BusFor(Sfx.Explosion, true) != MixBus.Explosion || Audio.BusFor(Sfx.HitBody, false) != MixBus.HitMarker
            || Audio.BusFor(Sfx.Kill, false) != MixBus.KillConfirm || Audio.BusFor(Sfx.FriendlyHit, false) != MixBus.FriendlyHit || Audio.BusFor(Sfx.Headshot, false) != MixBus.Headshot)
            leaks.Add("effect buses (gibs, explosion, hit marker, kill, friendly, headshot)");
        // The music rows reach the music thread's trims, each its own.
        {
            var audio = new Audio();
            audio.Mix.SetDb(MixBus.Frenzy5, -6f);
            audio.Mix.SetDb(MixBus.MusicMenu, 3f);
            audio.Mix.SetParam(MixParam.LimiterCeiling, 0.9f);
            audio.SyncMix();
            var mu = audio.Music;
            if (MathF.Abs(mu.Frenzy.LevelTrim(5) - AudioMix.DbToGain(-6f)) > 1e-6f || mu.Frenzy.LevelTrim(4) != AudioMix.Defaults.Gain(MixBus.Frenzy4) || mu.Frenzy.LevelTrim(6) != AudioMix.Defaults.Gain(MixBus.Frenzy6)
                || MathF.Abs(mu.MenuTrim - AudioMix.DbToGain(3f)) > 1e-6f || mu.LobbyTrim != AudioMix.Defaults.Gain(MixBus.MusicLobby) || MathF.Abs(mu.LimiterCeiling - 0.9f) > 1e-6f)
                leaks.Add("music rows: frenzy level 5 / menu / limiter didn't reach only their own trims");
            audio.Dispose();
        }
        if (leaks.Count > 0)
            Fail("audio mixer: a slider changed more than its own category: " + string.Join("; ", leaks.Take(8)));
        Line($"isolation: each of {levels - 1} level rows at +6 dB moves only its own gain and only the shot layers on its bus; music rows reach only their own trims: {(leaks.Count == 0 ? "OK" : "LEAKS")}");

        // ---- Mute ----------------------------------------------------------------------------------------------
        var mm = new AudioMix();
        mm.SetDb(MixBus.GunOwn, 6f);
        mm.SetMuted(MixBus.GunOwn, true);
        var mp = Audio.PlanShot(mm, true, false, true, 0f);
        if (mm.Gain(MixBus.GunOwn) != 0f || mp.NearMixed != 0f || mm.Db(MixBus.GunOwn) != 6f)
            Fail("audio mixer: muting own gun should give gain 0 (and keep its +6 dB for unmuting)");
        mm.SetMuted(MixBus.GunOwn, false);
        if (MathF.Abs(mm.Gain(MixBus.GunOwn) - AudioMix.DbToGain(6f)) > 1e-6f)
            Fail("audio mixer: unmuting should bring back the slider's level");
        mm.ResetLevel(MixBus.GunOwn);
        if (!mm.IsDefault)
            Fail("audio mixer: reset of a row didn't bring it back to its default");

        // ---- Clamping -------------------------------------------------------------------------------------------
        var cm = new AudioMix();
        cm.SetDb(MixBus.Explosion, 99f);
        cm.SetDb(MixBus.Melee, -99f);
        cm.SetDb(MixBus.Ui, float.NaN);
        cm.SetParam(MixParam.ShotRolloff, 50f);
        cm.SetParam(MixParam.LimiterCeiling, 2f);
        cm.SetParam(MixParam.FarEnd, 10f);
        cm.SetParam(MixParam.FarStart, 100f);
        if (cm.Db(MixBus.Explosion) != AudioMixDefaults.MaxDb || cm.Db(MixBus.Melee) != AudioMixDefaults.MinDb || cm.Db(MixBus.Ui) != 0f
            || cm.Param(MixParam.ShotRolloff) != AudioMixDefaults.Of(MixParam.ShotRolloff).Max || cm.Param(MixParam.LimiterCeiling) > MasterLimiter.SafetyCeiling
            || !(cm.FarMix(104f) > 0f && cm.FarMix(104f) < 1f) || cm.FarMix(99f) != 0f || cm.FarMix(105.01f) != 1f)
            Fail($"audio mixer: clamping (explosion {cm.Db(MixBus.Explosion)}, melee {cm.Db(MixBus.Melee)}, ui {cm.Db(MixBus.Ui)}, rolloff {cm.Param(MixParam.ShotRolloff)}, ceiling {cm.Param(MixParam.LimiterCeiling)}, crossfade end kept 5 m past start)");
        if (AudioMix.Defaults.IsDefault is false)
            Fail("audio mixer: the read-only defaults changed");
        AudioMix.Defaults.SetDb(MixBus.Master, -10f);
        if (AudioMix.Defaults.Gain(MixBus.Master) != AudioMix.DbToGain(AudioMixDefaults.Of(MixBus.Master).Default))
            Fail("audio mixer: AudioMix.Defaults must be read-only");

        // ---- JSON ------------------------------------------------------------------------------------------------
        var rng = new Random(4242);
        var src = new AudioMix();
        for (var i = 0; i < levels; i++)
        {
            src.SetDb((MixBus)i, MathF.Round((float)(AudioMixDefaults.MinDb + rng.NextDouble() * (AudioMixDefaults.MaxDb - AudioMixDefaults.MinDb)), 1));
            src.SetMuted((MixBus)i, rng.Next(5) == 0);
        }
        for (var i = 0; i < shape; i++)
        {
            ref readonly var e = ref AudioMixDefaults.Of((MixParam)i);
            src.SetParam((MixParam)i, MathF.Round(e.Min + (float)rng.NextDouble() * (e.Max - e.Min), 3));
        }
        var json = src.ToJson("Round trip", "test-build", new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc));
        var back = new AudioMix();
        var rep = back.LoadJson(json);
        var diffs = 0;
        for (var i = 0; i < levels; i++)
            if (back.Db((MixBus)i) != src.Db((MixBus)i) || back.Muted((MixBus)i) != src.Muted((MixBus)i))
                diffs++;
        for (var i = 0; i < shape; i++)
            if (MathF.Abs(back.Param((MixParam)i) - src.Param((MixParam)i)) > 1e-4f)
                diffs++;
        using (var doc = JsonDocument.Parse(json))
        {
            var r = doc.RootElement;
            if (r.GetProperty("version").GetInt32() != AudioMix.FormatVersion || r.GetProperty("game").GetString() != "test-build"
                || r.GetProperty("saved").GetString() != "2026-10-22T12:00:00Z" || r.GetProperty("levels").EnumerateObject().Count() != levels
                || r.GetProperty("shape").EnumerateObject().Count() != shape || !json.Contains('\n'))
                diffs++;
        }
        if (!rep.Ok || diffs > 0 || rep.Unknown != 0 || rep.Name != "Round trip")
            Fail($"audio mixer: JSON round trip: {rep}; {diffs} difference(s)");
        if (!AudioMix.GameVersion().Equals(BuildInfo.Label))
            Fail("audio mixer: the saved game version should be BuildInfo.Label");
        Line($"JSON round trip: {json.Length} bytes, {rep}: {(diffs == 0 ? "identical" : "DIFFERENT")}");

        // Unknown and missing keys, wrong types, a bare number for a level, comments and trailing commas.
        var partial = new AudioMix();
        var pr = partial.LoadJson("""
            { // a hand-edited file
              "version": 7, "from_the_future": {"x": 1},
              "levels": { "explosion": {"db": -3.5, "mute": true}, "melee": 4, "not.a.bus": {"db": 2}, "ui": {"db": "loud"}, },
              "shape": { "dist.rolloff": 1.0, "dist.unknown": 3, "dist.air": "x" },
            }
            """);
        var others = Enumerable.Range(0, levels).Where(i => (MixBus)i is not (MixBus.Explosion or MixBus.Melee)).All(i => partial.Gain((MixBus)i) == AudioMix.Defaults.Gain((MixBus)i));
        if (!pr.Ok || pr.Unknown != 3 || partial.Db(MixBus.Explosion) != -3.5f || !partial.Muted(MixBus.Explosion) || partial.Db(MixBus.Melee) != 4f
            || partial.Param(MixParam.ShotRolloff) != 1f || partial.Param(MixParam.AirAbsorption) != AudioMixDefaults.Of(MixParam.AirAbsorption).Default || !others)
            Fail($"audio mixer: a partial file with unknown keys loaded wrong: {pr}");
        var clampRep = new AudioMix().LoadJson("""{"levels": {"explosion": 80, "melee": {"db": -300}}, "shape": {"dist.max_m": 1e9}}""");
        if (!clampRep.Ok || clampRep.Clamped != 3)
            Fail($"audio mixer: out-of-range values should load clamped and be counted: {clampRep}");
        var keep = new AudioMix();
        keep.SetDb(MixBus.Streaks, -7f);
        foreach (var bad in new[] { "", "{", "[1,2]", "null", "{\"levels\": {\"streaks\": " })
            if (keep.LoadJson(bad).Ok || keep.Db(MixBus.Streaks) != -7f)
                Fail($"audio mixer: a broken file ({(bad.Length == 0 ? "empty" : bad)}) should load nothing and change nothing");
        Line($"hand-edited file: {pr}; out of range: {clampRep}; broken files change nothing");

        CheckAudioMixerOldSaves();

        // Files: save and load through a temp folder, preset names, a missing file.
        var tmp = Path.Combine(Path.GetTempPath(), "callofslop-audiomix-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(tmp, "AudioMixes", "Night Mix.json");
            var err = src.SaveFile(path, "Night Mix");
            var loaded = new AudioMix();
            var fr = loaded.LoadFile(path);
            var missing = new AudioMix().LoadFile(Path.Combine(tmp, "nope.json"));
            if (err != null || !fr.Ok || loaded.Changed != src.Changed || missing.Ok || missing.Error != "no file"
                || AudioMix.CleanPresetName("  ../My:Mix*  ") != "My Mix" && AudioMix.CleanPresetName("  ../My:Mix*  ") != "MyMix")
                Fail($"audio mixer: files: save {err ?? "ok"}, load {fr}, missing {missing}, clean name '{AudioMix.CleanPresetName("  ../My:Mix*  ")}'");
            if (!AudioMix.DefaultPath.StartsWith(SavePaths.Folder, StringComparison.Ordinal) || !AudioMix.PresetDir.StartsWith(SavePaths.Folder, StringComparison.Ordinal))
                Fail("audio mixer: audio_mix.json and AudioMixes/ should live in the save folder (SavePaths)");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }

        // ---- The key: F8 by default, added to old settings without taking a used key ------------------------------
        var kd = Keybinds.Defaults;
        var ktmp = Path.Combine(Path.GetTempPath(), "callofslop-mixkey-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            if (kd[(int)GameAction.AudioMixer] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F8) || Duplicates(kd).Length > 0)
                Fail($"audio mixer key: default {Code(kd[(int)GameAction.AudioMixer])} (want F8, unshared)");
            List<string> V3(int melee)
            {
                var l = new List<string> { "binds_version=3" };
                for (var i = 0; i < (int)GameAction.AudioMixer; i++)
                    l.Add($"bind_{(GameAction)i}={(i == (int)GameAction.Melee ? melee : kd[i])}");
                return l;
            }
            File.WriteAllLines(ktmp, V3(kd[(int)GameAction.Melee]));
            var a = Settings.Load(ktmp).Binds;
            File.WriteAllLines(ktmp, V3(Keybinds.Key(Brutal.GlfwApi.GlfwKey.F8)));
            var b = Settings.Load(ktmp).Binds;
            if (a[(int)GameAction.AudioMixer] != kd[(int)GameAction.AudioMixer] || b[(int)GameAction.AudioMixer] != Keybinds.Unbound
                || b[(int)GameAction.Melee] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F8) || Duplicates(b).Length > 0)
                Fail($"audio mixer key migration: a v3 file gets {Code(a[(int)GameAction.AudioMixer])}; with Melee on F8 it gets {Code(b[(int)GameAction.AudioMixer])} (want F8, then unbound)");
            Line($"key: Audio mixer {Code(kd[(int)GameAction.AudioMixer])}; an old (binds v3) settings.txt gets it, one with F8 already used keeps that and leaves the mixer unbound");
        }
        finally
        {
            try { File.Delete(ktmp); } catch (IOException) { }
        }

        // ---- No allocations: window closed, sound calls, meters ------------------------------------------------
        {
            var audio = new Audio(); // no device: the sound calls return early, the mix and meters still run
            var ui = new Ui();
            var w = Weapons.All[0];
            for (var i = 0; i < 100; i++)
                ui.DrawAudioMixer(audio, i * 0.016, w, Keybinds.Unbound); // warm-up
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            var sink = 0f;
            for (var i = 0; i < 20000; i++)
            {
                ui.DrawAudioMixer(audio, i * 0.016, w, Keybinds.Unbound); // closed
                sink += audio.Mix.Gain((MixBus)(i % levels)) + Audio.PlanShot(audio.Mix, false, false, true, i % 200).FarMixed;
                audio.Meters.Record(i % AudioMeters.MaxVoices, MixBus.Explosion, 1f, i, 1f, 0); // off: ignored
            }
            var closedBytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            if (closedBytes > 0 || audio.Meters.Enabled)
                Fail($"audio mixer: {closedBytes} bytes allocated over 20000 frames with the window closed (want 0), meters {(audio.Meters.Enabled ? "on" : "off")}");

            // Meters on: synthetic envelopes, voices recorded and read every frame.
            var meters = new AudioMeters();
            var loud = new short[48000];
            for (var i = 0; i < loud.Length; i++)
                loud[i] = (short)(i % 100 < 50 ? 16384 : -16384); // a square wave at half scale
            var first = meters.BuildEnvelopes([loud, new short[4800]]);
            meters.Enabled = true;
            meters.Record(0, MixBus.Explosion, 1f, 0, 1f, first);
            meters.Record(1, MixBus.Explosion, 0.5f, 0, 1f, first);
            meters.Record(2, MixBus.StepOwn, 0.25f, 0, 1f, first + 1);
            meters.Begin();
            meters.AccumulateVoices(0.1, 1f);
            meters.AddExternal(MixBus.MusicMenu, 0.2f, 0.01f);
            meters.End(0.1, 0.016f);
            var okRead = MathF.Abs(meters.Peak[(int)MixBus.Explosion] - 0.5f) < 1e-3f && meters.VoiceCount[(int)MixBus.Explosion] == 2
                         && MathF.Abs(meters.Rms[(int)MixBus.Explosion] - MathF.Sqrt(0.25f + 0.0625f)) < 1e-3f && meters.ActiveVoices == 3
                         && MathF.Abs(meters.MasterPeak - (0.5f + 0.25f + 0.2f)) < 1e-3f && meters.Clips == 0;
            meters.Record(3, MixBus.Explosion, 4f, 0.1, 1f, first); // 4 x half scale: clips
            meters.Begin();
            meters.AccumulateVoices(0.2, 1f);
            meters.End(0.2, 0.016f);
            okRead &= meters.Clips == 1 && meters.ClipUntil > 0.2;
            meters.Begin();
            meters.AccumulateVoices(5.0, 1f); // all finished
            meters.End(5.0, 0.016f);
            okRead &= meters.ActiveVoices == 0 && meters.Hold[(int)MixBus.Explosion] < 2f;
            if (!okRead)
                Fail($"audio mixer meters: explosion peak {meters.Peak[(int)MixBus.Explosion]}, voices {meters.VoiceCount[(int)MixBus.Explosion]}, master {meters.MasterPeak}, clips {meters.Clips}");
            audio.SetMetering(true);
            for (var i = 0; i < 100; i++)
            {
                meters.Record(i % AudioMeters.MaxVoices, MixBus.Explosion, 0.5f, i * 0.016, 1f, first);
                meters.Begin(); meters.AccumulateVoices(i * 0.016, 1f); meters.End(i * 0.016, 0.016f);
                audio.UpdateMeters(i * 0.016, 0.016f);
            }
            a0 = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20000; i++)
            {
                var t = 2 + i * 0.016;
                meters.Record(i % AudioMeters.MaxVoices, (MixBus)(i % levels), 0.5f, t, 1f + (i % 3) * 0.1f, first + (i & 1));
                meters.Begin();
                meters.AccumulateVoices(t, 0.8f);
                meters.AddExternal(MixBus.Frenzy3, 0.2f, 0.01f, toMaster: false);
                meters.End(t, 0.016f);
                audio.UpdateMeters(t, 0.016f);
            }
            var meterBytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            audio.SetMetering(false);
            if (meterBytes > 0)
                Fail($"audio mixer meters: {meterBytes} bytes allocated over 20000 frames (want 0)");
            Line($"no allocations: window closed {closedBytes} B, meters on {meterBytes} B over 20000 frames each; meter readings {(okRead ? "OK" : "WRONG")} (sink {sink:0})");
            audio.Dispose();
        }

        CheckAudioMixerRange();
    }

    /// <summary>
    /// The level range (-50..+50 dB): every level row uses it, old and new saved values load unclamped, a boost really
    /// reaches the voices (AL_MAX_GAIN lifted with it: +20 dB = 10x, not capped at 1) and the music trims, and a boosted
    /// music bus stays under the limiter's safety ceiling at no real CPU cost.
    /// </summary>
    private static void CheckAudioMixerRange()
    {
        const int levels = (int)MixBus.COUNT;
        float min = AudioMixDefaults.MinDb, max = AudioMixDefaults.MaxDb;
        if (min != -50f || max != 50f)
            Fail($"audio mixer range: {min}..{max} dB (want -50..+50)");
        var badRows = AudioMixDefaults.Entries.Take(levels).Where(e => e.Min != min || e.Max != max).Select(e => e.Key).ToArray();
        if (badRows.Length > 0)
            Fail($"audio mixer range: level rows not on {min}..{max} dB: {string.Join(", ", badRows)}");

        // Saved values: the old range's ends and the new range's ends load exactly, nothing counted as clamped.
        var inRange = new AudioMix();
        var rep = inRange.LoadJson("""
            {"version": 1, "levels": {"explosion": {"db": 12, "mute": false}, "melee": {"db": -40}, "ui": {"db": 50}, "notify": {"db": -50},
             "streaks": 35.5, "gun.own": {"db": -47.3}}}
            """);
        if (!rep.Ok || rep.Clamped != 0 || inRange.Db(MixBus.Explosion) != 12f || inRange.Db(MixBus.Melee) != -40f || inRange.Db(MixBus.Ui) != 50f
            || inRange.Db(MixBus.Notify) != -50f || inRange.Db(MixBus.Streaks) != 35.5f || inRange.Db(MixBus.GunOwn) != -47.3f)
            Fail($"audio mixer range: saved values inside -50..+50 should load unclamped: {rep}; explosion {inRange.Db(MixBus.Explosion)}, ui {inRange.Db(MixBus.Ui)}, notify {inRange.Db(MixBus.Notify)}");
        var outRep = new AudioMix().LoadJson("""{"levels": {"ui": 50.5, "notify": -51}}""");
        if (outRep.Clamped != 2)
            Fail($"audio mixer range: values past +-50 dB should load clamped and be counted: {outRep}");

        // Gains: +50 dB is about 316x, and a +20 dB bus gives a voice 10x its 0 dB gain (AL_MAX_GAIN lifted with it).
        var g50 = inRange.Gain(MixBus.Ui);
        var g20 = AudioMix.DbToGain(20f);
        if (MathF.Abs(g50 - 316.2278f) > 0.01f || g50 != AudioMixDefaults.MaxLevelGain || MathF.Abs(inRange.Gain(MixBus.Notify) - 0.0031623f) > 1e-6f)
            Fail($"audio mixer range: +50 dB gain {g50} (want 316.23 = MaxLevelGain {AudioMixDefaults.MaxLevelGain}), -50 dB gain {inRange.Gain(MixBus.Notify)}");
        var voiceBad = new List<string>();
        foreach (var pre in new[] { 0.3f, 1f, 1.3f }) // a quiet voice, one at the cap, one over it (your own gun plays at the cap)
        {
            var at0 = Audio.VoiceEffectiveGain(pre, 1f);
            var at20 = Audio.VoiceEffectiveGain(pre, g20);
            var at50 = Audio.VoiceEffectiveGain(pre, g50);
            var (_, cap20) = Audio.VoiceGain(pre, g20);
            if (MathF.Abs(at20 / at0 - 10f) > 0.01f || cap20 <= 1f || MathF.Abs(at50 / at0 - g50) > 0.05f)
                voiceBad.Add($"gain {pre}: 0 dB {at0}, +20 dB {at20} (x{at20 / at0:0.00}, cap {cap20}), +50 dB x{at50 / at0:0.0}");
        }
        var (g0, c0) = Audio.VoiceGain(0.7f, 1f);
        if (g0 != 0.7f || c0 != 1f)
            voiceBad.Add($"0 dB changed the voice ({g0}, cap {c0}; want 0.7, cap 1)");
        // The default mix's boosts (own gun +20 dB, explosions +20.1, headshot +15.7, ...) are heard in full through the
        // per-voice cap: a voice at the cap (your own gun) and one over it play exactly the bus gain louder.
        var boosted = new List<string>();
        for (var i = 0; i < levels; i++)
        {
            var bg = AudioMix.Defaults.Gain((MixBus)i);
            if (bg <= 1f)
                continue;
            boosted.Add($"{AudioMixDefaults.Entries[i].Key} x{bg:0.00}");
            foreach (var pre in new[] { 0.3f, 1f, 1.3f })
            {
                var ratio = Audio.VoiceEffectiveGain(pre, bg) / Audio.VoiceEffectiveGain(pre, 1f);
                if (MathF.Abs(ratio / bg - 1f) > 1e-5f)
                    voiceBad.Add($"default {AudioMixDefaults.Entries[i].Key} ({AudioMix.GainToDb(bg):+0.0} dB) gives a voice at {pre} x{ratio:0.00} (want x{bg:0.00}: clipped by the cap)");
            }
        }
        var ownCap = Audio.VoiceGain(Audio.OwnShotGain, AudioMix.Defaults.Gain(MixBus.GunOwn));
        if (MathF.Abs(ownCap.MaxGain - 10f) > 1e-4f || MathF.Abs(Audio.VoiceEffectiveGain(Audio.OwnShotGain, ownCap.MaxGain) - 10f * Audio.OwnShotGain) > 1e-4f)
            voiceBad.Add($"your own gun at the default +20 dB: AL_GAIN {ownCap.Gain}, AL_MAX_GAIN {ownCap.MaxGain} (want 10x the cap)");
        if (voiceBad.Count > 0)
            Fail("audio mixer range: voice gains through the mix: " + string.Join("; ", voiceBad));
        Line($"default boosts through the per-voice cap, heard in full ({boosted.Count}): {string.Join(", ", boosted)}; own gun AL_MAX_GAIN {ownCap.MaxGain:0.0}");

        // The music trims take the whole range (they were capped at 4x = +12 dB).
        var audio = new Audio();
        audio.Mix.SetDb(MixBus.MusicMenu, 50f);
        audio.Mix.SetDb(MixBus.MusicLobby, 20f);
        audio.Mix.SetDb(MixBus.Frenzy6, 50f);
        audio.Mix.SetDb(MixBus.Frenzy1, -50f);
        audio.SyncMix();
        var mu = audio.Music;
        if (mu.MenuTrim != g50 || MathF.Abs(mu.LobbyTrim - 10f) > 1e-4f || mu.Frenzy.LevelTrim(6) != g50 || MathF.Abs(mu.Frenzy.LevelTrim(1) - 0.0031623f) > 1e-6f)
            Fail($"audio mixer range: music trims menu {mu.MenuTrim}, lobby {mu.LobbyTrim}, frenzy 6 {mu.Frenzy.LevelTrim(6)}, frenzy 1 {mu.Frenzy.LevelTrim(1)} (want 316.2, 10, 316.2, 0.0032)");
        audio.Dispose();

        // A boosted music bus: louder, every sample finite and under the limiter's safety ceiling; the built-in mix
        // never runs the limiter. CPU: the boosted mix (limiter on) against the default one, interleaved, medians.
        const int sr = Audio.SampleRate;
        var block = new float[sr / 10 * 2];
        double Rms(MusicPlayer p, out float peak, out bool finite)
        {
            double sum = 0;
            peak = 0;
            finite = true;
            for (var b = 0; b < 15; b++) // 1.5 s after a 1.5 s fade-in
            {
                p.Mix(block);
                foreach (var x in block)
                {
                    finite &= float.IsFinite(x);
                    peak = MathF.Max(peak, MathF.Abs(x));
                    sum += x * (double)x;
                }
            }
            return Math.Sqrt(sum / (15.0 * block.Length));
        }
        MusicPlayer Player(float trim)
        {
            var p = new MusicPlayer(7) { Volume = 0.5f, MenuTrim = trim };
            p.SetMood(MusicMood.Menu);
            for (var b = 0; b < 15; b++)
                p.Mix(block);
            return p;
        }
        var plain = Player(1f);
        var rmsPlain = Rms(plain, out var peakPlain, out var finPlain);
        var loud = Player(g50);
        var rmsLoud = Rms(loud, out var peakLoud, out var finLoud);
        var plus6 = Player(AudioMix.DbToGain(6f));
        var rms6 = Rms(plus6, out _, out _);
        if (plain.Limiting || !loud.Limiting || !finPlain || !finLoud || peakLoud > MasterLimiter.SafetyCeiling + 1e-6f || !(rmsLoud > rmsPlain * 1.5) || !(rms6 > rmsPlain * 1.3))
            Fail($"audio mixer range: boosted menu music: limiter {(loud.Limiting ? "on" : "OFF")} (default {(plain.Limiting ? "ON" : "off")}), peak {peakLoud:0.000} (ceiling {MasterLimiter.SafetyCeiling}), "
                 + $"RMS {rmsPlain:0.000} -> +6 dB {rms6:0.000} -> +50 dB {rmsLoud:0.000}, finite {finPlain && finLoud}");
        var tPlain = new List<double>();
        var tLoud = new List<double>();
        for (var r = 0; r < 5; r++)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var b = 0; b < 5; b++)
                plain.Mix(block);
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var b = 0; b < 5; b++)
                loud.Mix(block);
            var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            tPlain.Add(t1 - t0);
            tLoud.Add(t2 - t1);
        }
        tPlain.Sort();
        tLoud.Sort();
        var cpu = tLoud[2] / Math.Max(1.0, tPlain[2]);
        if (cpu > 1.5)
            Fail($"audio mixer range: boosted music costs {cpu:0.00}x the default mix (budget 1.5x)");
        Line($"range: {min:+0;-0}..{max:+0;-0} dB on all {levels} level rows; old/new saved values load unclamped ({rep.Clamped} clamped), past the range clamped ({outRep.Clamped}); "
             + $"+20 dB voice x{Audio.VoiceEffectiveGain(1f, g20) / Audio.VoiceEffectiveGain(1f, 1f):0.00} (AL_MAX_GAIN {g20:0.0}), +50 dB x{g50:0.0}; music trims to x{g50:0}; "
             + $"menu music RMS {rmsPlain:0.000} -> +6 dB {rms6:0.000} -> +50 dB {rmsLoud:0.000} (peak {peakPlain:0.000} -> {peakLoud:0.000}, limited, ceiling {MasterLimiter.SafetyCeiling}); boosted music CPU {cpu:0.00}x the default");
    }

    /// <summary>
    /// The levels the baked default mix (the user's SeabMix2, saved 2026-10-23 by the F8 mixer's "Save as...") sets off
    /// 0 dB; every other level is 0 dB and every shape value the factory one.
    /// </summary>
    private static readonly Dictionary<string, float> BakedLevels = new()
    {
        ["gun.own"] = 20f, ["gun.other_near"] = 14.1f, ["gun.other_far"] = 10f, ["explosion"] = 20.1f, ["impact.whizz"] = 12.3f,
        ["props.break"] = 5f, ["gore.gibs"] = 7.3f, ["hit.world"] = 3.9f, ["hit.marker"] = 8.4f, ["hit.headshot"] = 15.7f,
        ["hit.kill"] = 13f, ["player.hurt"] = 10f, ["step.own"] = -8f, ["step.other"] = -4.1f,
    };

    /// <summary>
    /// An audio_mix.json as builds before the baked mix wrote it: every level {"db", "mute", "default": 0} (the old
    /// defaults), the factory shape; <paramref name="set"/> gives the player's changes by key.
    /// </summary>
    private static string OldAudioMixJson(Dictionary<string, (float Db, bool Mute)> set, string name = "")
    {
        var sb = new System.Text.StringBuilder("{\"format\": \"callofslop-audio-mix\", \"version\": 1, \"game\": \"dev build (bdaae14)\", ");
        if (name.Length > 0)
            sb.Append($"\"name\": \"{name}\", ");
        sb.Append("\"levels\": {");
        for (var i = 0; i < (int)MixBus.COUNT; i++)
        {
            var key = AudioMixDefaults.Entries[i].Key;
            var (db, mute) = set.TryGetValue(key, out var v) ? v : (0f, false);
            sb.Append(System.FormattableString.Invariant($"{(i > 0 ? ", " : "")}\"{key}\": {{\"db\": {db}, \"mute\": {(mute ? "true" : "false")}, \"default\": 0}}"));
        }
        sb.Append("}, \"shape\": {");
        for (var i = 0; i < (int)MixParam.COUNT; i++)
            sb.Append(System.FormattableString.Invariant($"{(i > 0 ? ", " : "")}\"{AudioMixDefaults.Of((MixParam)i).Key}\": {AudioMixDefaults.FactoryShape[i]}"));
        return sb.Append("}}").ToString();
    }

    /// <summary>
    /// Saves from before the baked mix: a level left at its old default (the "default" saved with it) moves to the new
    /// default, a level the player set keeps their value (mute too), the user's own SeabMix2 file loads as exactly the
    /// defaults, and a file saved by this build keeps a level turned back to 0 dB.
    /// </summary>
    private static void CheckAudioMixerOldSaves()
    {
        var bad = new List<string>();
        var none = new Dictionary<string, (float, bool)>();

        // Untouched: every level at its old default -> the new defaults.
        var untouched = new AudioMix();
        var r1 = untouched.LoadJson(OldAudioMixJson(none));
        if (!r1.Ok || !untouched.IsDefault || r1.Updated != BakedLevels.Count)
            bad.Add($"an untouched old save loads with {untouched.Changed} value(s) off the new defaults ({r1}; want 0 and {BakedLevels.Count} moved)");

        // The player's own choices stay: a boosted own gun, a cut explosion, a muted kill chime (its level untouched).
        var chosen = new AudioMix();
        var r2 = chosen.LoadJson(OldAudioMixJson(new() { ["gun.own"] = (6f, false), ["explosion"] = (-3f, false), ["hit.kill"] = (0f, true), ["ui"] = (2f, false) }));
        if (!r2.Ok || chosen.Db(MixBus.GunOwn) != 6f || chosen.Db(MixBus.Explosion) != -3f || chosen.Db(MixBus.KillConfirm) != 13f || !chosen.Muted(MixBus.KillConfirm)
            || chosen.Db(MixBus.Ui) != 2f || chosen.Db(MixBus.StepOwn) != -8f || chosen.Db(MixBus.GunOtherNear) != 14.1f || r2.Updated != BakedLevels.Count - 2)
            bad.Add($"an old save with choices: own gun {chosen.Db(MixBus.GunOwn)} (want 6), explosion {chosen.Db(MixBus.Explosion)} (-3), kill {chosen.Db(MixBus.KillConfirm)} muted {chosen.Muted(MixBus.KillConfirm)} (13, muted), "
                    + $"ui {chosen.Db(MixBus.Ui)} (2), own steps {chosen.Db(MixBus.StepOwn)} (-8); {r2}");

        // The user's SeabMix2 file (saved with the old defaults) is exactly the new defaults.
        var seab = new AudioMix();
        var r3 = seab.LoadJson(OldAudioMixJson(BakedLevels.ToDictionary(kv => kv.Key, kv => (kv.Value, false)), "SeabMix2"));
        if (!r3.Ok || !seab.IsDefault || r3.Updated != 0 || r3.Name != "SeabMix2")
            bad.Add($"the SeabMix2 file loads {seab.Changed} value(s) off the defaults ({r3})");

        // Saved by this build: a level turned back down to 0 dB stays 0 (its saved default is the new one).
        var down = new AudioMix();
        down.SetDb(MixBus.GunOwn, 0f);
        var back = new AudioMix();
        var r4 = back.LoadJson(down.ToJson(null, "test-build", new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc)));
        if (!r4.Ok || back.Db(MixBus.GunOwn) != 0f || back.Changed != 1 || r4.Updated != 0)
            bad.Add($"a new save with own gun at 0 dB loads {back.Db(MixBus.GunOwn)} ({r4})");

        // No saved default (hand-edited, bare number): the value as written.
        var bare = new AudioMix();
        bare.LoadJson("""{"levels": {"gun.own": 0, "explosion": {"db": 0}}}""");
        if (bare.Db(MixBus.GunOwn) != 0f || bare.Db(MixBus.Explosion) != 0f)
            bad.Add($"a hand-edited 0 dB (no saved default) loads own gun {bare.Db(MixBus.GunOwn)}, explosion {bare.Db(MixBus.Explosion)} (want 0, 0)");

        if (bad.Count > 0)
            Fail("audio mixer old saves: " + string.Join("; ", bad));
        Line($"old saves: untouched -> the new defaults ({r1.Updated} moved); player choices kept ({r2.Updated} moved); SeabMix2 file = the defaults; a new save's 0 dB stays; hand-edited values as written: {(bad.Count == 0 ? "OK" : "WRONG")}");
    }

    /// <summary>The shot plan as it was before the mixer (Audio.GunMix.cs at caf39f9), for the identity check.</summary>
    private static (float Near, float Far, Audio.Falloff Fall) LegacyPlan(bool own, bool suppressed, bool hasFar, float distance)
    {
        var shot = new Audio.Falloff(5f, 0.8f, 250f, Thin: true);
        if (own)
            return (Audio.OwnShotGain * (suppressed ? Audio.OwnSuppressedGain : 1f), 0f, shot);
        if (suppressed)
            return (Audio.OtherShotGain * Audio.OtherSuppressedGain, 0f, Audio.SuppressedFalloff);
        if (!hasFar)
            return (Audio.OtherShotGain, 0f, shot);
        var x = Math.Clamp((distance - 25f) / 65f, 0f, 1f);
        var far = x * x * (3 - 2 * x);
        var near = far < 0.98f ? MathF.Sqrt(1f - far) : 0f;
        return (Audio.OtherShotGain * near, far > 0.02f ? Audio.OtherShotGain * MathF.Sqrt(far) : 0f, shot);
    }
}
