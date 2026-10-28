using CallOfSlop.Platform.Steam;

namespace CallOfSlop;

/// <summary>
/// Steam in the app (Source/Platform/Steam): started after the save folder is known, pumped once a frame, shut down last.
/// Without Steam (every GitHub build, LAN game and self-test) each call here is a no-op. The overlay frees the mouse and
/// opens the pause menu, so an online match is not steered blindly and a single-player one waits.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>Starts Steam when this run asks for it (SteamPlatform.Plan). True when the game must quit at once
    /// because Steam is relaunching it (a Steam build started outside Steam).</summary>
    private static bool InitSteam(string[] args)
    {
        SteamPlatform.Init(args, SavePaths.ExeFolder);
        if (SteamPlatform.RestartRequested)
            return true;
        SteamPlatform.OverlayChanged += OnSteamOverlay;
        return false;
    }

    private static void ShutdownSteam()
    {
        SteamPlatform.OverlayChanged -= OnSteamOverlay;
        if (_window != null)
            SteamAvatars.Dispose(); // the GL context is still up here
        SteamPlatform.Shutdown();
    }

    /// <summary>The name other players see: the Steam persona name when Steam runs and Settings says to use it, else
    /// the settings name. Persona names are cut to the game's ASCII rules; one with nothing left keeps the settings name.</summary>
    private static string DisplayName()
    {
        if (!SteamPlatform.Available || !_settings.SteamName)
            return _settings.Name;
        var persona = SteamPlatform.PersonaName;
        var clean = Settings.Clean(persona);
        return clean == "Player" && !persona.Trim().Equals("Player", StringComparison.Ordinal) ? _settings.Name : clean;
    }

    private static void OnSteamOverlay(bool active)
    {
        if (!active || _window == null)
            return;
        SetMouseCaptured(false);
        if (_session is { Local: not null } && !InLobbyRoom && !Ui.MenuOpen && !Ui.PauseOpen)
            Ui.PauseOpen = true; // the pause menu: an offline game waits, an online one stops taking the mouse
    }
}
