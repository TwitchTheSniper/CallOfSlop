using System.Numerics;
using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The weather (Source/Weather, docs/weather/WEATHER.md) from the game's side: stepping it once a frame in a match (its
/// worker thread does the work), and the weather map window's key (F11 by default, rebindable; Esc closes it). The
/// window counts as a menu, so the cursor is free while it's open.
/// </summary>
public static unsafe partial class Program
{
    private static double _weatherLast = -1;

    /// <summary>Moves the weather on for this frame and tells it where the camera is (before the sky is drawn).</summary>
    private static void WeatherFrame(double now, Vector3 eye)
    {
        var dt = _weatherLast < 0 ? 0 : now - _weatherLast;
        _weatherLast = now;
        Weather.Frame(World.Map, dt, eye);
    }

    /// <summary>A key press for the weather map: its bind toggles it, Esc closes it. Returns true when it used the key.</summary>
    private static bool WeatherMapKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.WeatherMapOpen)
        {
            SetWeatherMapOpen(false);
            return true;
        }
        if (key == GlfwKey.Unknown || Keybinds.Key(key) != _settings.Bind(GameAction.WeatherMap) || Ui.ChatOpen || Ui.WantsTextInput || _session == null)
            return false;
        SetWeatherMapOpen(!Ui.WeatherMapOpen);
        return true;
    }

    private static void SetWeatherMapOpen(bool open)
    {
        Ui.WeatherMapOpen = open;
        if (_session == null)
            return;
        if (open)
            SetMouseCaptured(false);
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    /// <summary>The weather map window (closed: one check).</summary>
    private static void DrawWeatherUi()
    {
        var was = Ui.WeatherMapOpen;
        Ui.DrawWeatherMap(_worldEye, World.Map, _settings.Bind(GameAction.WeatherMap));
        if (was && !Ui.WeatherMapOpen)
            SetWeatherMapOpen(false); // closed with its own X
    }
}
