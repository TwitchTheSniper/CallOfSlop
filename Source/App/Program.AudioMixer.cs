namespace CallOfSlop;

/// <summary>
/// The Audio mixer debug window (Source/UI/UiAudioMixer.cs) from the game's side: audio_mix.json loaded at start-up,
/// the key that opens and closes it (F8 by default, rebindable; Esc closes it too), and the mouse (the window counts as
/// a menu for <see cref="CursorPolicy"/>, so the cursor is free while it's open, in the lobby and in a match).
/// </summary>
public static unsafe partial class Program
{
    /// <summary>Loads the player's mix (audio_mix.json in the save folder) over the built-in defaults, if the file exists.</summary>
    private static void LoadAudioMix()
    {
        var report = AudioSystem.Mix.LoadFile(AudioMix.DefaultPath);
        if (report.Ok)
            Console.WriteLine($"[audiomix] loaded {AudioMix.DefaultPath}: {report}; {AudioSystem.Mix.Changed} value(s) differ from the defaults");
        else if (report.Error != "no file")
            Console.WriteLine($"[audiomix] {AudioMix.DefaultPath} {report} (playing the built-in defaults)");
    }

    /// <summary>A key press for the mixer: its bind toggles the window, Esc closes it. Returns true when it used the key.</summary>
    private static bool AudioMixerKey(Brutal.GlfwApi.GlfwKey key)
    {
        if (key == Brutal.GlfwApi.GlfwKey.Escape && Ui.AudioMixerOpen)
        {
            SetAudioMixerOpen(false);
            return true;
        }
        if (key == Brutal.GlfwApi.GlfwKey.Unknown || Keybinds.Key(key) != _settings.Bind(GameAction.AudioMixer) || Ui.ChatOpen || Ui.WantsTextInput)
            return false;
        SetAudioMixerOpen(!Ui.AudioMixerOpen);
        return true;
    }

    private static void SetAudioMixerOpen(bool open)
    {
        Ui.AudioMixerOpen = open;
        if (_session == null)
            return;
        if (open)
            SetMouseCaptured(false); // a menu: the cursor is free to drag the sliders
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true); // nothing else open in a match: straight back to playing
            _swallowClick = true;
        }
    }

    private static int _smokeMixerStep;
    private static double _smokeMixerStart;

    /// <summary>
    /// Smoke step: opens the Audio mixer over the match, fires a test shot of your weapon and an enemy shot at 30 m, and
    /// screenshots it with the meters moving (smoke_audiomixer.bmp); logs the voices and the master estimate, and the
    /// frame time with the window open. Never saves anything (no button is pressed).
    /// </summary>
    private static void SmokeAudioMixer(double now)
    {
        switch (_smokeMixerStep)
        {
            case 0:
                _smokeMixerStart = now;
                Ui.AudioMixerOpen = true;
                _smokeMixerStep = 1;
                break;
            case 1 when now - _smokeMixerStart > 0.4:
                var w = _session?.Local?.WeaponDef ?? Weapons.All[0];
                AudioSystem.TestShot(w, 0f, 0f);
                AudioSystem.TestShot(w, 30f, 60f);
                _smokeMixerStep = 2;
                break;
            case 2 when now - _smokeMixerStart > 0.5:
                SmokeShot("audiomixer");
                var m = AudioSystem.Meters;
                Console.WriteLine($"[smoke] audio mixer: open, {m.ActiveVoices} voices, master est. peak {AudioMix.GainToDb(m.MasterHold):0.0} dBFS, own gun {AudioMix.GainToDb(m.Hold[(int)MixBus.GunOwn]):0.0} dBFS, "
                                  + $"near layer {AudioMix.GainToDb(m.Hold[(int)MixBus.GunOtherNear]):0.0} dBFS, mix {(AudioSystem.Mix.IsDefault ? "defaults" : $"{AudioSystem.Mix.Changed} changed")}, frame {FrameDt * 1000:0.00} ms");
                Ui.AudioMixerOpen = false;
                _smokeMixerStep = 3;
                break;
        }
    }
}
