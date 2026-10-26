using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Audio mixer debug window (F8, rebindable in Settings > Controls; or Settings > Audio > Open audio mixer): a
/// level slider in dB with mute and reset for every sound category (<see cref="MixBus"/>), the gun mix's distance shape
/// (<see cref="MixParam"/>), live per-category meters with voice counts and the master estimate with a clip light,
/// test sounds (your shot, an enemy's at 5/30/80 m, an explosion, steps, hit sounds, a frenzy level preview), and
/// Save / Load / Revert / Save as / Copy to clipboard (<see cref="AudioMix"/>). Works in the lobby and live in a match
/// (offline or online: it only changes what you hear). Everything it draws per frame is allocation-free; closed, it
/// costs one bool check (the meters are off).
/// </summary>
public sealed partial class Ui
{
    /// <summary>The Audio mixer window is open.</summary>
    public bool AudioMixerOpen;

    private string _mixMsg = "";
    private double _mixMsgAt = -10;
    private ImInputString? _mixPresetName;
    private List<string>? _mixPresets;
    private int _mixPreset;
    private int _mixTestWeapon = -1; // -1: your current weapon
    private float _mixTestAngle = 45f;
    private int _mixFrenzyLevel = 6;
    private float _mixFrenzySeconds = 20f;
    private double _mixFrenzyUntil = -1;
    private bool _mixWasOpen;
    private double _mixLastNow = double.NaN;
    private int _mixBind = Keybinds.Unbound;

    // ImGui ids per row, made once (the window builds no strings per row each frame).
    private static readonly string[] MixSliderIds = AudioMixDefaults.Entries.Select(e => "##" + e.Key).ToArray();
    private static readonly string[] MixMuteIds = AudioMixDefaults.Entries.Select(e => "##m" + e.Key).ToArray();
    private static readonly string[] MixResetIds = AudioMixDefaults.Entries.Select(e => "R##r" + e.Key).ToArray();
    private static readonly string[] MixDefaultTexts = AudioMixDefaults.Entries.Select(e => e.IsLevel ? e.Default.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) : e.Default.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    private static readonly string[] MixTooltips = AudioMixDefaults.Entries.Select(e => $"{e.Help}\nKey: {e.Key}  (default {(e.IsLevel ? e.Default.ToString("+0.0;-0.0;0.0", System.Globalization.CultureInfo.InvariantCulture) + " dB" : e.Default.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))})").ToArray();
    private static readonly string[] SmallNumbers = Enumerable.Range(0, 100).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    // Up to +12 dBFS: with level rows up to +50 dB the estimate can go well over full scale (the clip light and the red
    // peak show it), and the extra room shows how far over it is.
    private const float MeterMinDb = -60f, MeterMaxDb = 12f;

    /// <summary>The frenzy level the mixer is previewing (0-6), or -1 when it isn't.</summary>
    public int FrenzyPreviewLevel(double now) => AudioMixerOpen && now < _mixFrenzyUntil ? _mixFrenzyLevel : -1;

    private void MixMessage(string text, double now)
    {
        _mixMsg = text;
        _mixMsgAt = now;
        Console.WriteLine($"[audiomix] {text}");
    }

    /// <summary>
    /// Draws the mixer if it's open (call every frame, also when closed: it turns the meters on and off).
    /// <paramref name="current"/>: the weapon the test shots use by default (yours in a match); <paramref name="bind"/>: the key that opens it.
    /// </summary>
    public void DrawAudioMixer(Audio audio, double now, WeaponDef current, int bind)
    {
        if (!AudioMixerOpen)
        {
            if (_mixWasOpen)
            {
                _mixFrenzyUntil = -1; // closing the window ends a preview
                audio.SetMetering(false);
                _mixWasOpen = false;
            }
            return; // closed: one bool check a frame, nothing recorded or measured
        }
        audio.SetMetering(audio.Enabled);
        _mixBind = bind;
        if (!_mixWasOpen)
        {
            _mixWasOpen = true;
            _mixPresets = AudioMix.ListPresets();
            _mixPresetName ??= new ImInputString(41);
            _mixLastNow = double.NaN;
            ImGui.SetNextWindowFocus(); // over the lobby's full-screen menu
        }
        var dt = double.IsNaN(_mixLastNow) ? 0f : (float)Math.Clamp(now - _mixLastNow, 0, 0.25);
        _mixLastNow = now;
        audio.UpdateMeters(now, dt);
        var mix = audio.Mix;
        var m = audio.Meters;

        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(new float2(vp.WorkPos.X + vp.WorkSize.X - 24 * _scale, vp.WorkPos.Y + 40 * _scale), ImGuiCond.FirstUseEver, new float2(1, 0));
        ImGui.SetNextWindowSize(new float2(MathF.Min(900 * _scale, vp.WorkSize.X * 0.9f), MathF.Min(820 * _scale, vp.WorkSize.Y * 0.9f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.94f);
        if (ImGui.Begin("Audio mixer###audiomixer"u8, ref AudioMixerOpen))
        {
            DrawMixToolbar(mix, now);
            DrawMixMaster(audio, m, now);
            if (ImGui.CollapsingHeader("Test sounds"u8, ImGuiTreeNodeFlags.DefaultOpen))
                DrawMixTests(audio, now, current);
            DrawMixLevels(mix, m);
            DrawMixShape(audio, mix);
        }
        ImGui.End();
    }

    private void DrawMixToolbar(AudioMix mix, double now)
    {
        if (ImGui.Button("Save"u8))
        {
            var err = mix.SaveFile(AudioMix.DefaultPath, null);
            MixMessage(err == null ? $"saved {AudioMix.FileName} ({mix.Changed} value(s) changed from the defaults)" : $"save failed: {err}", now);
        }
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Saves audio_mix.json in your save folder (it carries over to new builds); it's loaded every time the game starts."u8);
        ImGui.SameLine();
        if (ImGui.Button("Load"u8))
            MixMessage($"{AudioMix.FileName}: {mix.LoadFile(AudioMix.DefaultPath)}", now);
        ImGui.SameLine();
        if (ImGui.Button("Revert to defaults"u8))
        {
            mix.ResetAll();
            MixMessage("reverted to the built-in defaults (not saved: Save to keep it)", now);
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy to clipboard"u8))
        {
            try
            {
                SafeClipboard.Sink(mix.ToJson(_mixPresetName?.ToString() is { Length: > 0 } n ? n : null, AudioMix.GameVersion(), DateTime.UtcNow));
                MixMessage("copied the mix as JSON to the clipboard", now);
            }
            catch (Exception ex)
            {
                MixMessage($"copy failed: {ex.Message}", now);
            }
        }
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"{mix.Changed} changed");

        ImGui.SetNextItemWidth(200 * _scale);
        try
        {
            ImGui.InputText("##presetname"u8, _mixPresetName!);
        }
        catch (Exception ex)
        {
            ImGuiSafe.FieldFailed("audio mix preset name", ex);
        }
        ImGui.SameLine();
        if (ImGui.Button("Save as..."u8))
        {
            var name = AudioMix.CleanPresetName(_mixPresetName!.ToString());
            if (name.Length == 0)
                MixMessage("type a preset name first (letters, digits, space, - and _)", now);
            else
            {
                var err = mix.SaveFile(Path.Combine(AudioMix.PresetDir, name + ".json"), name);
                MixMessage(err == null ? $"saved preset {AudioMix.PresetFolder}/{name}.json" : $"save failed: {err}", now);
                _mixPresets = AudioMix.ListPresets();
                _mixPreset = Math.Max(0, _mixPresets.IndexOf(name));
            }
        }
        var presets = _mixPresets;
        if (presets is { Count: > 0 })
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180 * _scale);
            _mixPreset = Math.Clamp(_mixPreset, 0, presets.Count - 1);
            if (ImGui.BeginCombo("##presets"u8, presets[_mixPreset]))
            {
                for (var i = 0; i < presets.Count; i++)
                    if (ImGui.Selectable(presets[i], i == _mixPreset))
                        _mixPreset = i;
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.Button("Load preset"u8))
            {
                var name = presets[_mixPreset];
                MixMessage($"preset {name}: {mix.LoadFile(Path.Combine(AudioMix.PresetDir, name + ".json"))}", now);
                ImGuiSafe.SetInput(_mixPresetName!, name);
            }
        }
        if (_mixMsg.Length > 0 && now - _mixMsgAt < 8)
            ImGuiSafe.TextColored(Orange, _mixMsg);
        else
            ImGuiSafe.TextColored(Dim, $"Drag a slider (Ctrl+click to type a value, Ctrl+wheel for 0.5 dB steps, Ctrl+Shift+wheel for 0.1 dB). {Keybinds.Name(_mixBind)} or Esc closes the window (in a match the mouse comes back to the game).");
    }

    private void DrawMixMaster(Audio audio, AudioMeters m, double now)
    {
        ImGui.Separator();
        var clip = now < m.ClipUntil;
        ImGui.Text("Master out (est.)"u8);
        ImGui.SameLine(170 * _scale);
        MeterBar(m.MasterPeak, m.MasterRms, m.MasterHold, 260 * _scale);
        ImGui.SameLine();
        if (clip)
            ImGui.TextColored(Red, "CLIP"u8);
        else
            ImGui.TextColored(Dim, "clip"u8);
        ImGui.SameLine();
        ImGuiSafe.TextColored(Dim, $"peak {Db(m.MasterHold),6:0.0} dBFS  RMS {Db(m.MasterRms),6:0.0}  {m.ActiveVoices} voices, {m.Clips} clip frames");
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("An estimate from each voice's gain and its sound's envelope (the effects are mixed by OpenAL) plus the music's measured output: the peak is the sum of every voice's peak, an upper bound. CLIP lights for 1.5 s when it passes full scale."u8);
        var music = audio.Music;
        var minGain = music.FrenzyLimiterMinGain;
        ImGuiSafe.TextColored(Dim, $"Music limiter (frenzy): ceiling {music.LimiterCeiling:0.000} ({Db(music.LimiterCeiling):0.0} dBFS), deepest gain reduction {Db(minGain):0.0} dB; level-6 headroom target {FrenzyLayer.Level6Headroom:0.00}x the SFX reference; frenzy level {music.Frenzy.Level}");
        if (!audio.Enabled)
            ImGui.TextColored(Red, "No audio device: the mix can be edited and saved, but nothing plays."u8);
        else if (!audio.EfxEnabled)
            ImGui.TextColored(Dim, "EFX is off on this device: the distance bass cut and air absorption rows have no effect."u8);
    }

    private void DrawMixTests(Audio audio, double now, WeaponDef current)
    {
        var all = Weapons.All;
        var w = _mixTestWeapon >= 0 && _mixTestWeapon < all.Length ? all[_mixTestWeapon] : current;
        ImGui.SetNextItemWidth(220 * _scale);
        if (ImGui.BeginCombo("Test weapon"u8, _mixTestWeapon < 0 ? $"current: {current.Name}" : w.Name))
        {
            if (ImGui.Selectable($"current: {current.Name}", _mixTestWeapon < 0))
                _mixTestWeapon = -1;
            for (var i = 0; i < all.Length; i++)
                if (ImGui.Selectable($"{all[i].Name}##tw{i}", i == _mixTestWeapon))
                    _mixTestWeapon = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160 * _scale);
        ImGui.SliderFloat("Direction"u8, ref _mixTestAngle, -180f, 180f, "%.0f deg"u8);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Where the positioned test sounds come from: degrees to the right of where you look (0 = straight ahead, 180 = behind)."u8);

        if (ImGui.Button("Own shot"u8))
            audio.TestShot(w, 0f, 0f);
        ImGui.SameLine();
        if (ImGui.Button("Enemy 5 m"u8))
            audio.TestShot(w, 5f, _mixTestAngle);
        ImGui.SameLine();
        if (ImGui.Button("Enemy 30 m"u8))
            audio.TestShot(w, 30f, _mixTestAngle);
        ImGui.SameLine();
        if (ImGui.Button("Enemy 80 m"u8))
            audio.TestShot(w, 80f, _mixTestAngle);
        ImGui.SameLine();
        if (ImGui.Button("Explosion 15 m"u8))
            audio.TestExplosion(15f, _mixTestAngle);
        ImGui.SameLine();
        if (ImGui.Button("Gibs 10 m"u8))
            audio.Effect3D(Sfx.GibSquelch, audio.AroundListener(10f, _mixTestAngle), 0.95f);
        ImGui.SameLine();
        if (ImGui.Button("Zombie groan 8 m"u8))
            audio.TestZombie(false, 8f, _mixTestAngle);
        ImGui.SameLine();
        if (ImGui.Button("Zombie steps 8 m"u8))
            audio.TestZombie(true, 8f, _mixTestAngle);

        if (ImGui.Button("Own steps"u8))
            audio.TestFootstep(true, 0f, 0f);
        ImGui.SameLine();
        if (ImGui.Button("Enemy steps 10 m"u8))
            audio.TestFootstep(false, 10f, _mixTestAngle);
        ImGui.SameLine();
        if (ImGui.Button("Land"u8))
            audio.TestFootstep(true, 0f, 0f, StepKind.Land);
        ImGui.SameLine();
        if (ImGui.Button("Hit marker"u8))
            audio.Effect(Sfx.HitBody, 0.6f);
        ImGui.SameLine();
        if (ImGui.Button("Headshot"u8))
            audio.Effect(Sfx.Headshot, 0.75f);
        ImGui.SameLine();
        if (ImGui.Button("Kill"u8))
            audio.Effect(Sfx.Kill, 0.7f);
        ImGui.SameLine();
        if (ImGui.Button("Friendly hit"u8))
            audio.Effect(Sfx.FriendlyHit, 0.4f);

        ImGui.SetNextItemWidth(140 * _scale);
        ImGui.SliderInt("Frenzy level"u8, ref _mixFrenzyLevel, 0, FrenzyMeter.MaxLevel);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(140 * _scale);
        ImGui.SliderFloat("for##frenzysecs"u8, ref _mixFrenzySeconds, 5f, 60f, "%.0f s"u8);
        ImGui.SameLine();
        if (now < _mixFrenzyUntil)
        {
            if (ImGui.Button("Stop preview"u8))
                _mixFrenzyUntil = -1;
            ImGui.SameLine();
            ImGuiSafe.TextColored(Orange, $"previewing level {_mixFrenzyLevel}: {_mixFrenzyUntil - now:0} s left (music level {audio.Music.Frenzy.Level})");
        }
        else if (ImGui.Button("Preview frenzy"u8))
            _mixFrenzyUntil = now + _mixFrenzySeconds;
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Forces the kill-frenzy music (and your gun's frenzy layer) to this level for a while, in the lobby too. It moves up and down on the bar lines like the real thing."u8);
    }

    private void DrawMixLevels(AudioMix mix, AudioMeters m)
    {
        var entries = AudioMixDefaults.Entries;
        var i = 0;
        while (i < (int)MixBus.COUNT)
        {
            var group = entries[i].Group;
            var j = i;
            while (j < (int)MixBus.COUNT && entries[j].Group == group)
                j++;
            if (ImGui.CollapsingHeader(group, ImGuiTreeNodeFlags.DefaultOpen) && BeginMixTable(group))
            {
                for (var k = i; k < j; k++)
                    LevelRow(mix, m, (MixBus)k);
                ImGui.EndTable();
            }
            i = j;
        }
    }

    private bool BeginMixTable(string id)
    {
        if (!ImGui.BeginTable(id, 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit))
            return false;
        ImGui.TableSetupColumn("Source"u8, ImGuiTableColumnFlags.WidthFixed, 250 * _scale);
        ImGui.TableSetupColumn("Level"u8, ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Mute"u8, ImGuiTableColumnFlags.WidthFixed, 34 * _scale);
        ImGui.TableSetupColumn("Reset"u8, ImGuiTableColumnFlags.WidthFixed, 44 * _scale);
        ImGui.TableSetupColumn("Default"u8, ImGuiTableColumnFlags.WidthFixed, 60 * _scale);
        ImGui.TableSetupColumn("Meter"u8, ImGuiTableColumnFlags.WidthFixed, 150 * _scale);
        ImGui.TableSetupColumn("Voices"u8, ImGuiTableColumnFlags.WidthFixed, 44 * _scale);
        return true;
    }

    private void LevelRow(AudioMix mix, AudioMeters m, MixBus bus)
    {
        ref readonly var e = ref AudioMixDefaults.Of(bus);
        var k = (int)bus;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(e.Label);
        if (ImGui.IsItemHovered())
            ImGuiSafe.SetItemTooltip(MixTooltips[k]);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        var db = mix.Db(bus);
        if (ImGui.SliderFloat(MixSliderIds[k], ref db, AudioMixDefaults.MinDb, AudioMixDefaults.MaxDb, "%+.1f dB"u8) || FineWheel(ref db))
            mix.SetDb(bus, db);
        ImGui.TableNextColumn();
        var mute = mix.Muted(bus);
        if (ImGui.Checkbox(MixMuteIds[k], ref mute))
            mix.SetMuted(bus, mute);
        if (ImGui.IsItemHovered())
            ImGui.SetItemTooltip("Mute"u8);
        ImGui.TableNextColumn();
        var isDefault = !mute && db == e.Default;
        ImGui.BeginDisabled(isDefault);
        if (ImGui.SmallButton(MixResetIds[k]))
            mix.ResetLevel(bus);
        ImGui.EndDisabled();
        ImGui.TableNextColumn();
        ImGuiSafe.TextColored(Dim, MixDefaultTexts[k]);
        ImGui.TableNextColumn();
        if (bus == MixBus.Master)
            MeterBar(m.MasterPeak, m.MasterRms, m.MasterHold, 145 * _scale);
        else
            MeterBar(m.Peak[k], m.Rms[k], m.Hold[k], 145 * _scale);
        ImGui.TableNextColumn();
        var voices = bus == MixBus.Master ? m.ActiveVoices : m.VoiceCount[k];
        if (voices > 0)
            ImGui.Text(SmallNumbers[Math.Min(voices, SmallNumbers.Length - 1)]);
    }

    /// <summary>
    /// Ctrl + mouse wheel over the level slider just drawn: 0.5 dB a notch, 0.1 dB with Shift too, so the wide
    /// -50..+50 dB range stays precise around 0 dB (Ctrl+click still types a value). The window doesn't scroll meanwhile.
    /// </summary>
    private static bool FineWheel(ref float db)
    {
        if (!ImGui.IsItemHovered())
            return false;
        var io = ImGui.GetIO();
        bool ctrl = io.KeyCtrl, shift = io.KeyShift;
        if (!ctrl)
            return false;
        ImGui.SetItemKeyOwner(ImGuiKey.MouseWheelY);
        var wheel = io.MouseWheel;
        if (wheel == 0f)
            return false;
        db = MathF.Round((db + MathF.Sign(wheel) * (shift ? 0.1f : 0.5f)) * 10f) / 10f;
        return true;
    }

    private void DrawMixShape(Audio audio, AudioMix mix)
    {
        var group = AudioMixDefaults.Of((MixParam)0).Group;
        if (!ImGui.CollapsingHeader(group, ImGuiTreeNodeFlags.DefaultOpen) || !BeginMixTable(group))
            return;
        for (var i = 0; i < (int)MixParam.COUNT; i++)
        {
            var p = (MixParam)i;
            ref readonly var e = ref AudioMixDefaults.Of(p);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(e.Label);
            var row = (int)MixBus.COUNT + i;
            if (ImGui.IsItemHovered())
                ImGuiSafe.SetItemTooltip(MixTooltips[row]);
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            var v = mix.Param(p);
            if (ImGui.SliderFloat(MixSliderIds[row], ref v, e.Min, e.Max, e.Format))
                mix.SetParam(p, v);
            ImGui.TableNextColumn();
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(v == e.Default);
            if (ImGui.SmallButton(MixResetIds[row]))
                mix.ResetParam(p);
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            ImGuiSafe.TextColored(Dim, MixDefaultTexts[row]);
            ImGui.TableNextColumn();
            ShapeReadout(audio, mix, p);
            ImGui.TableNextColumn();
        }
        ImGui.EndTable();
    }

    /// <summary>What a shape value means in dB, next to its slider.</summary>
    private static void ShapeReadout(Audio audio, AudioMix mix, MixParam p)
    {
        switch (p)
        {
            case MixParam.ShotRolloff:
            {
                var f = mix.ShotFalloff;
                ImGuiSafe.TextColored(Dim, $"{Db(f.Attenuation(40f) / f.Attenuation(20f)):0.0} dB/doubling @20-40 m");
                break;
            }
            case MixParam.ShotReference or MixParam.ShotMax:
            {
                var f = mix.ShotFalloff;
                ImGuiSafe.TextColored(Dim, $"{Db(f.Attenuation(30f)):0.0} dB @30 m, {Db(f.Attenuation(80f)):0.0} @80 m");
                break;
            }
            case MixParam.BassCut or MixParam.BassFloor:
                ImGuiSafe.TextColored(Dim, $"bass {Db(mix.DistanceLf(30f)):0.0} dB @30 m, {Db(mix.DistanceLf(60f)):0.0} @60 m");
                break;
            case MixParam.AirAbsorption or MixParam.AirFloor:
                ImGuiSafe.TextColored(Dim, $"treble {Db(mix.DistanceHf(30f)):0.0} dB @30 m, {Db(mix.DistanceHf(60f)):0.0} @60 m");
                break;
            case MixParam.FarStart or MixParam.FarEnd:
                ImGuiSafe.TextColored(Dim, $"distant layer {mix.FarMix(30f) * 100:0}% @30 m, {mix.FarMix(60f) * 100:0}% @60 m");
                break;
            case MixParam.LimiterCeiling:
                ImGuiSafe.TextColored(Dim, $"{Db(mix.LimiterCeiling):0.0} dBFS (min gain {audio.Music.FrenzyLimiterMinGain:0.00})");
                break;
        }
    }

    private static float Db(float linear) => MathF.Max(-99f, AudioMix.GainToDb(linear));

    /// <summary>A horizontal meter (-60..+12 dBFS): the RMS bar, the peak line and the hold tick; 0 dB marked.</summary>
    private void MeterBar(float peak, float rms, float hold, float width)
    {
        var h = ImGui.GetFrameHeight() * 0.7f;
        var p0 = ImGui.GetCursorScreenPos();
        p0 = new float2(p0.X, p0.Y + ImGui.GetFrameHeight() * 0.15f);
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(p0, new float2(p0.X + width, p0.Y + h), new ImColor8(20, 22, 26, 255));
        float X(float lin) => p0.X + width * Math.Clamp((AudioMix.GainToDb(lin) - MeterMinDb) / (MeterMaxDb - MeterMinDb), 0f, 1f);
        var rx = X(rms);
        if (rx > p0.X)
            dl.AddRectFilled(p0, new float2(rx, p0.Y + h), new ImColor8(40, 140, 70, 255));
        var px = X(peak);
        if (px > p0.X)
        {
            var col = peak >= 1f ? new ImColor8(235, 60, 50, 255) : peak >= 0.5f ? new ImColor8(230, 190, 60, 255) : new ImColor8(110, 220, 120, 255);
            dl.AddRectFilled(new float2(p0.X, p0.Y + h * 0.35f), new float2(px, p0.Y + h * 0.65f), col);
        }
        var hx = X(hold);
        if (hx > p0.X)
            dl.AddRectFilled(new float2(hx - 1.5f, p0.Y), new float2(hx + 1.5f, p0.Y + h), hold >= 1f ? new ImColor8(255, 70, 60, 255) : new ImColor8(230, 230, 230, 220));
        var zx = p0.X + width * (-MeterMinDb / (MeterMaxDb - MeterMinDb));
        dl.AddRectFilled(new float2(zx - 0.5f, p0.Y), new float2(zx + 0.5f, p0.Y + h), new ImColor8(200, 80, 80, 200));
        ImGui.Dummy(new float2(width, ImGui.GetFrameHeight()));
    }
}
