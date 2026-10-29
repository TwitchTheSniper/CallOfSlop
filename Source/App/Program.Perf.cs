using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The performance window (Source/UI/UiPerf.cs, Source/Perf) from the game's side: the key that cycles it (F9 by
/// default, rebindable: window, corner overlay, off; Esc closes the window), the cursor (the full window counts as a
/// menu, the overlay doesn't), the GPU timer made once GL is up, the smoke step, and <c>--perf-shot</c>: a short
/// single-player match with the window and then the overlay screenshotted (smoke_perf_window.bmp, smoke_perf_overlay.bmp,
/// full size) and the allocations per frame with the window open logged, then quit. Every mode, every build.
/// </summary>
public static unsafe partial class Program
{
    private static bool _perfShot;
    private static long _perfUiBytes, _perfUiFrames, _perfUiAllocFrames;

    /// <summary>Makes the frame's GPU pass timer and hooks the music thread's counter (after GL is loaded).</summary>
    private static void InitPerf()
    {
        Perf.Gpu = new PerfGpu(GlGpuStamps.Create);
        Perf.MusicTicks = () => AudioSystem.Music.RenderTicks;
    }

    /// <summary>A key press for the perf window: its bind cycles window, overlay, off; Esc closes the window. Returns true when it used the key.</summary>
    private static bool PerfKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.PerfWindowOpen)
        {
            SetPerfMode(PerfView.Off);
            return true;
        }
        if (key == GlfwKey.Unknown || Keybinds.Key(key) != _settings.Bind(GameAction.PerfWindow) || Ui.ChatOpen || Ui.WantsTextInput)
            return false;
        SetPerfMode(Ui.PerfMode switch { PerfView.Off => PerfView.Window, PerfView.Window => PerfView.Overlay, _ => PerfView.Off });
        return true;
    }

    private static void SetPerfMode(PerfView mode)
    {
        var wasWindow = Ui.PerfWindowOpen;
        Ui.PerfMode = mode;
        PerfCursor(wasWindow);
    }

    /// <summary>The full window frees the cursor like a menu; leaving it (to the overlay or off) gives it back to the match.</summary>
    private static void PerfCursor(bool wasWindow)
    {
        if (_session == null || wasWindow == Ui.PerfWindowOpen)
            return;
        if (Ui.PerfWindowOpen)
            SetMouseCaptured(false);
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    /// <summary>Draws the window or overlay (every frame; closed it is one check). Its own buttons can change the view.</summary>
    private static void DrawPerfUi(double now)
    {
        if (_perfShot)
            PerfShotTick(now);
        var wasWindow = Ui.PerfWindowOpen;
        var before = GC.GetAllocatedBytesForCurrentThread();
        Ui.DrawPerf(_session, now, _settings.Bind(GameAction.PerfWindow));
        if (Ui.PerfMode != PerfView.Off)
        {
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before; // (the smoke and --perf-shot log lines report it)
            _perfUiBytes += bytes;
            _perfUiFrames++;
            if (bytes > 0)
                _perfUiAllocFrames++;
        }
        PerfCursor(wasWindow);
    }

    // ---- Smoke step -------------------------------------------------------------------------------------------

    private static int _smokePerfStep;
    private static double _smokePerfStart;

    /// <summary>Smoke step: opens the perf window over the match, screenshots it (smoke_perfwindow.bmp), then the overlay; logs what it saw.</summary>
    private static void SmokePerf(double now)
    {
        switch (_smokePerfStep)
        {
            case 0:
                _smokePerfStart = now;
                Ui.PerfMode = PerfView.Window;
                _smokePerfStep = 1;
                break;
            case 1 when now - _smokePerfStart > 1.2:
                SmokeShot("perfwindow");
                LogPerfLine("[smoke] perf window");
                Ui.PerfMode = PerfView.Overlay;
                _smokePerfStep = 2;
                break;
            case 2 when now - _smokePerfStart > 1.6:
                SmokeShot("perfoverlay");
                _smokePerfStep = 3;
                break;
            case 3 when now - _smokePerfStart > 1.7:
                Ui.PerfMode = PerfView.Off;
                _smokePerfStep = 4;
                break;
        }
    }

    /// <summary>One log line: what the window shows right now (fps, the busiest areas, GPU, draws, allocations per frame).</summary>
    private static void LogPerfLine(string prefix)
    {
        var s = new PerfStats();
        s.Update();
        var gpu = Perf.Gpu;
        Console.WriteLine($"{prefix}: {s.RecentFrames} frames, {s.Fps:0} fps avg {s.AvgMs:0.00} ms max {s.MaxMs:0.0}; render {s.AreaAvg[(int)PerfArea.Render]:0.00} ms, game {s.AreaAvg[(int)PerfArea.Game]:0.00}, "
                          + $"ui {s.AreaAvg[(int)PerfArea.Ui]:0.00}, ticks {s.TicksPerSecond:0}/s worst {s.TickWorstMs:0.00} ms; GPU {(gpu is { Available: true } ? $"{gpu.TotalAvgMs():0.00} ms over {gpu.Readings} readings" : "unavailable")}; "
                          + $"{s.Draws:0} draws, {s.Triangles:0} tris; {s.AllocBytes:0} B/frame allocated on the main thread, "
                          + $"the perf UI itself {(_perfUiFrames > 0 ? _perfUiBytes / (double)_perfUiFrames : 0):0} B/frame over {_perfUiFrames} frames ({_perfUiAllocFrames} allocated); misnested {Perf.Misnested}, GPU marks out of order {gpu?.OutOfOrder ?? 0}");
    }

    // ---- --perf-shot ------------------------------------------------------------------------------------------

    private static double _perfShotStart = -1;
    private static int _perfShotStep;

    private static void StartPerfShot(string[] args)
    {
        _perfShot = true;
        _settings.Bots = 5;
        _settings.MapRandom = false;
        // --perf-shot "Map" [--stress-size N]: that map (a Zombies map in Zombies, no bots; Horde Stress at N zombies).
        if (args.SkipWhile(a => !a.Equals("--perf-shot", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault(a => !a.StartsWith("--")) is { } name && Maps.Find(name) is { } map)
        {
            _settings.MapName = map.Name;
            if (map.Zombies != null)
            {
                _settings.Mode = GameMode.Zombies;
                _settings.Bots = 0;
            }
            if (args.SkipWhile(a => !a.Equals("--stress-size", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault() is { } sz && int.TryParse(sz, out var n))
                _settings.ZombieStressSize = n;
        }
        StartSession(LobbyAction.SinglePlayer);
    }

    private static void PerfShotTick(double now)
    {
        if (_perfShotStart < 0)
            _perfShotStart = now;
        var t = now - _perfShotStart;
        if (_session is { } game && t < 15)
        {
            if (Ui.MenuOpen)
                Ui.AutoDeploy(game, now); // past the loadout screen (and the warmup) into the match
            else if (game.Phase == MatchPhase.Playing && game.Local is not { Alive: true } && !game.RespawnRequested)
                game.RequestRespawn();
        }
        if (_session is { Local: { } me })
            me.ImmuneUntil = now + 0.5; // the bots mustn't kill the camera
        switch (_perfShotStep)
        {
            case 0 when t > 4:
                Ui.PerfMode = PerfView.Window;
                _perfShotStep = 1;
                break;
            case 1 when t > 9: // (10 s of graph would be nicer; 5 s keeps it quick)
                _shotFull = true;
                SmokeShot("perf_window");
                LogPerfLine("[perf-shot] window");
                _perfShotStep = 2;
                break;
            case 2 when t > 9.2:
                Ui.PerfMode = PerfView.Overlay;
                _perfShotStep = 3;
                break;
            case 3 when t > 10.5:
                _shotFull = true;
                SmokeShot("perf_overlay");
                LogPerfLine("[perf-shot] overlay");
                _perfShotStep = 4;
                break;
            case 4 when t > 10.7:
                Ui.PerfMode = PerfView.Off;
                _quit = true;
                Console.WriteLine("[perf-shot] done");
                break;
        }
    }
}
