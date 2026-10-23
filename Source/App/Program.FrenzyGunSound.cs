namespace CallOfSlop;

/// <summary>
/// The per-class frenzy gun sound (Source/Audio/FrenzyGunSound.cs) from the game's side: hands the audio your frenzy
/// level and class as the frenzy visuals show them (the same meter as the frenzy music; the class of your most recent
/// kill, see <see cref="FrenzyFx.LocalClassOf"/>; level 0 outside a live match or while dead) and the setting.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>Once per frame, after <see cref="UpdateFrenzyFx"/>.</summary>
    private static void UpdateFrenzyGunSound()
    {
        AudioSystem.FrenzyGunMode = _settings.FrenzyGunSound;
        var preview = Ui.FrenzyPreviewLevel(Brutal.GlfwApi.Glfw.Time); // the Audio mixer's frenzy preview drives your gun's layer too
        AudioSystem.FrenzyGunLevel = preview >= 0 ? preview : FrenzyFx.LocalShownLevel;
        AudioSystem.FrenzyGunClass = FrenzyFx.Override?.Class ?? FrenzyFx.LocalClass;
    }
}
