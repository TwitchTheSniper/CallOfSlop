namespace CallOfSlop;

/// <summary>
/// The weapon tweak debug window (Source/UI/UiWeaponTweaks.cs) from the game's side: the key that opens and closes it
/// (F7 by default, rebindable; Esc closes it too). Dev builds always have it; a release build only with Settings >
/// Graphics > "Allow the weapon tweak window". Like the Audio mixer it counts as a menu, so the cursor is free while it's open.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>May this build open the weapon tweak window?</summary>
    private static bool WeaponTweaksAllowed => !BuildInfo.IsRelease || _settings.AllowWeaponTweaks;

    /// <summary>A key press for the weapon tweak window: its bind toggles it, Esc closes it. Returns true when it used the key.</summary>
    private static bool WeaponTweaksKey(Brutal.GlfwApi.GlfwKey key)
    {
        if (key == Brutal.GlfwApi.GlfwKey.Escape && Ui.WeaponTweaksOpen)
        {
            SetWeaponTweaksOpen(false);
            return true;
        }
        if (key == Brutal.GlfwApi.GlfwKey.Unknown || Keybinds.Key(key) != _settings.Bind(GameAction.WeaponTweaks) || Ui.ChatOpen || Ui.WantsTextInput || !WeaponTweaksAllowed)
            return false;
        SetWeaponTweaksOpen(!Ui.WeaponTweaksOpen);
        return true;
    }

    private static void SetWeaponTweaksOpen(bool open)
    {
        Ui.WeaponTweaksOpen = open;
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
