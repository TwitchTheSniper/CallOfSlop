using Brutal.GlfwApi;

namespace CallOfSlop;

// The Max FPS frame limiter in the main loop (FrameLimiter.cs; Settings > Video). It runs right after SwapBuffers and
// before the next frame's PollEvents, so input is read straight after the wait, as late as it can be. The server's
// fixed 60 Hz ticks run inside Game.Update from the frame's elapsed time, so a cap only changes how many ticks a frame
// runs, never their rate (the lowest cap, 20 fps, is 3 ticks a frame). The wait is timed as its own F9 area, apart
// from "Swap (vsync/GPU wait)". The smoke test and the picture and bench tools run uncapped (they measure frame rates).
public static partial class Program
{
    private static readonly FrameLimiter Limiter = new();

    /// <summary>The smoke test, --light-shots / --perf-shot / --gl-bench: no frame cap.</summary>
    private static bool _frameCapOff;

    /// <summary>"In a menu" for the menu cap: the main menu, the lobby room, the pause and loadout menus, the results screen.</summary>
    private static bool FrameCapMenu => _session == null || InLobbyRoom || Ui.PauseOpen || Ui.MenuOpen || ResultsScreen;

    /// <summary>The window has no focus or is minimised (the background cap).</summary>
    private static bool FrameCapBackground =>
        _window.GetAttribute(GlfwWindowAttribute.Focused) == 0 || _window.GetAttribute(GlfwWindowAttribute.Iconified) != 0;

    private static void LimitFrame()
    {
        var s = _settings;
        var fps = _frameCapOff
            ? 0
            : FrameCap.Choose(s.MaxFps, s.MaxFpsMenu, s.MaxFpsBackground, s.VSync, FrameCapMenu, (s.MaxFpsBackground > 0) && FrameCapBackground);
        FrameLimiter.ActiveFps = fps;
        using (Perf.Measure(PerfArea.Limiter))
            Limiter.Limit(fps);
    }
}
