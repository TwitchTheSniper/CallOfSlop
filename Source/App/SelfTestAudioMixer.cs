using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Audio mixer" (Source/Audio/AudioMix.cs, AudioMixDefaults.cs, Audio.Mixer.cs, UiAudioMixer.cs):
/// the defaults table (keys unique and stable, levels in <see cref="MixBus"/> order, groups together, every sound on a
/// bus); with no audio_mix.json every value is today's behaviour exactly (every gain 1, the gun-mix falloff, crossfade
/// and distance filter equal the pre-mixer formulas, every shot plan unchanged, the music trims 1, the limiter ceiling);
/// the JSON round trip, unknown and missing keys, clamping and bad files; each level slider changes only its own
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

        // ---- Identity: no audio_mix.json = today's game ------------------------------------------------------
        var mix = new AudioMix();
        if (AudioMixDefaults.IsFactory)
        {
            var badGain = Enumerable.Range(0, levels).Where(i => mix.Gain((MixBus)i) != 1f || AudioMix.Defaults.Gain((MixBus)i) != 1f).Select(i => entries[i].Key).ToArray();
            if (badGain.Length > 0)
                Fail($"audio mixer identity: gain not exactly 1 for {string.Join(", ", badGain)}");
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
                    if (p.NearMixed != nearL || p.FarMixed != farL || p.Near != nearL || p.Far != farL || p.NearFalloff != fallL || p.FarFalloff != fallL
                        || p.NearBusGain != 1f || p.FarBusGain != 1f)
                        mism++;
                }
            }
            if (mism > 0)
                Fail($"audio mixer identity: {mism} distance/plan value(s) differ from the pre-mixer formulas");
            var audio = new Audio();
            audio.SyncMix();
            var music = audio.Music;
            var trims = Enumerable.Range(1, 6).Select(l => music.Frenzy.LevelTrim(l)).ToArray();
            if (music.MenuTrim != 1f || music.LobbyTrim != 1f || trims.Any(t => t != 1f) || music.LimiterCeiling != FrenzyLayer.LimiterCeiling)
                Fail($"audio mixer identity: music trims menu {music.MenuTrim}, lobby {music.LobbyTrim}, frenzy {string.Join("/", trims)}, limiter {music.LimiterCeiling} (want 1s and {FrenzyLayer.LimiterCeiling})");
            Line($"identity: every gain exactly 1, falloff 5 m/0.8/250 m, crossfade/bass/air equal to the old formulas at 1601 distances x 16 shot kinds, music trims 1, limiter {music.LimiterCeiling}: {(mism == 0 ? "OK" : "DIFFERENT")}");
            audio.Dispose();
        }
        else
            Line("identity: skipped (the defaults table holds a baked-in mix)");

        // ---- A slider changes only its category --------------------------------------------------------------
        var leaks = new List<string>();
        for (var b = 1; b < levels; b++)
        {
            var m = new AudioMix();
            m.SetDb((MixBus)b, 6f);
            for (var o = 0; o < levels; o++)
                if (o != b && m.Gain((MixBus)o) != 1f)
                    leaks.Add($"{entries[b].Key}->{entries[o].Key}");
            if (MathF.Abs(m.Gain((MixBus)b) - MathF.Pow(10f, 0.3f)) > 1e-5f)
                leaks.Add($"{entries[b].Key} +6 dB gain {m.Gain((MixBus)b)}");
            foreach (var own in new[] { false, true })
            foreach (var sup in new[] { false, true })
            foreach (var launcher in new[] { false, true })
            foreach (var d in new[] { 5f, 50f, 120f })
            {
                var p = Audio.PlanShot(m, own, sup, true, d, launcher);
                var expectNear = p.NearBus == (MixBus)b ? m.Gain((MixBus)b) : 1f;
                var expectFar = p.FarBus == (MixBus)b ? m.Gain((MixBus)b) : 1f;
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
        if (Math.Abs(far120.FarMixed / far120.Far - AudioMix.DbToGain(-12f)) > 1e-5f || own0.NearMixed != own0.Near)
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
            if (MathF.Abs(mu.Frenzy.LevelTrim(5) - AudioMix.DbToGain(-6f)) > 1e-6f || mu.Frenzy.LevelTrim(4) != 1f || mu.Frenzy.LevelTrim(6) != 1f
                || MathF.Abs(mu.MenuTrim - AudioMix.DbToGain(3f)) > 1e-6f || mu.LobbyTrim != 1f || MathF.Abs(mu.LimiterCeiling - 0.9f) > 1e-6f)
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
            src.SetDb((MixBus)i, MathF.Round((float)(rng.NextDouble() * 52 - 40), 1));
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
        var others = Enumerable.Range(0, levels).Where(i => (MixBus)i is not (MixBus.Explosion or MixBus.Melee)).All(i => partial.Gain((MixBus)i) == 1f || !AudioMixDefaults.IsFactory);
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
        var d = Keybinds.Defaults;
        var ktmp = Path.Combine(Path.GetTempPath(), "callofslop-mixkey-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            if (kd[(int)GameAction.AudioMixer] != Keybinds.Key(Brutal.GlfwApi.GlfwKey.F8) || Duplicates(d).Length > 0)
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
