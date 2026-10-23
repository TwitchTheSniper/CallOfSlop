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
}
