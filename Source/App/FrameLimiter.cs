using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// The Max FPS settings (Settings > Video): the choices and which cap applies this frame. 0 means no cap. A cap is
/// 20-1000 fps: below 20 a frame would be longer than the host's catch-up window, and the fixed 60 Hz server ticks
/// (which run inside the frame, <see cref="Game.Update"/>) would start to fall behind.
/// </summary>
public static class FrameCap
{
    public const int Min = 20, Max = 1000;

    /// <summary>The ready-made choices in the Max FPS lists (a custom number is anything else in range).</summary>
    public static readonly int[] Presets = [30, 60, 75, 90, 120, 144, 165, 180, 240, 360];

    /// <summary>A saved or typed value made valid: 0 (or less) is no cap, anything else is held to 20-1000.</summary>
    public static int Clamp(int fps) => fps <= 0 ? 0 : Math.Clamp(fps, Min, Max);

    /// <summary>
    /// The cap for this frame (0: none). The main cap applies only with VSync off: with VSync on the monitor already
    /// paces the frames, and a cap that is not a divisor of the refresh rate would make the frames uneven. The menu
    /// cap (lobby, menus, pause menu) and the background cap (window unfocused or minimised) apply with VSync on or
    /// off, and the lowest cap that applies wins.
    /// </summary>
    public static int Choose(int main, int menu, int background, bool vsync, bool inMenu, bool inBackground)
    {
        var cap = vsync ? 0 : Clamp(main);
        if (inMenu)
            cap = Lowest(cap, Clamp(menu));
        if (inBackground)
            cap = Lowest(cap, Clamp(background));
        return cap;
    }

    private static int Lowest(int a, int b) => a <= 0 ? b : b <= 0 ? a : Math.Min(a, b);
}

/// <summary>
/// The frame limiter: called once a frame right after SwapBuffers, it waits out the rest of the frame's time so frames
/// are released at an even <c>1 / fps</c>. The schedule is kept in <see cref="Stopwatch"/> ticks: each frame's release
/// time is the last one plus the period, so a frame that wakes a little late (up to a quarter of a frame, at most
/// 2 ms) is made up on the next one and the average rate stays exact; a frame that ran long resets the schedule to
/// now, so the frames after it are not rushed out back to back to catch up (no burst). The wait sleeps for most of the
/// time and spins (yielding) for the last part, so it is precise without burning a core: on Windows a high-resolution
/// waitable timer (Windows 10 1803+; older ones fall back to a 1 ms timer period and a longer spin), elsewhere
/// Thread.Sleep. No allocations. Main thread only.
/// </summary>
public sealed partial class FrameLimiter
{
    /// <summary>The cap in force this frame (0: none), for the F9 window's guide line.</summary>
    public static int ActiveFps;

    /// <summary>The release time of the last frame (Stopwatch ticks; 0: no schedule yet).</summary>
    public long Release { get; private set; }

    private int _fps;
    private long _anchor, _frames; // the schedule: frame n is released at _anchor + n / fps

    /// <summary>Lateness kept in the schedule (made up next frame) rather than resetting it: a quarter frame, at most 2 ms.</summary>
    public static long Tolerance(long period) => Math.Min(period / 4, Stopwatch.Frequency / 500);

    public static long Period(int fps) => fps <= 0 ? 0 : Stopwatch.Frequency / fps;

    /// <summary>
    /// Plans this frame's wait at <paramref name="now"/> (Stopwatch ticks) for a cap of <paramref name="fps"/>: returns
    /// the ticks to wait (0: release now), and moves <see cref="Release"/> to the time this frame is released. A new cap,
    /// or none, starts the schedule over.
    /// </summary>
    public long Plan(long now, int fps)
    {
        if (fps <= 0)
        {
            _fps = 0;
            Release = 0;
            return 0;
        }
        if (fps != _fps || Release == 0)
        {
            _fps = fps;
            Release = _anchor = now; // the first capped frame goes out now; the schedule starts from it
            _frames = 0;
            return 0;
        }
        var target = _anchor + (_frames + 1) * Stopwatch.Frequency / fps; // (from the anchor: no rounding drift)
        if (now < target)
        {
            _frames++;
            Release = target;
            return target - now;
        }
        if (now - target <= Tolerance(Period(fps)))
        {
            _frames++;
            Release = target; // a little late: kept, made up next frame
        }
        else
        {
            Release = _anchor = now; // a long frame: start over from now (no burst)
            _frames = 0;
        }
        return 0;
    }

    /// <summary>Plans and waits (the main loop's call). Returns the ticks it meant to wait.</summary>
    public long Limit(int fps)
    {
        var wait = Plan(Stopwatch.GetTimestamp(), fps);
        if (wait > 0)
            WaitUntil(Release);
        return wait;
    }

    // ---- Waiting ------------------------------------------------------------------------------------------------

    private static nint _timer = -1; // the Windows high-resolution timer (-1: not made yet, 0: not available)
    private static bool _timerPeriodSet;

    /// <summary>The last part of every wait, spun rather than slept (sleeps wake up to about this late).</summary>
    public static double SpinMs => OperatingSystem.IsWindows() && _timer == 0 ? 2.0 : 1.0;

    /// <summary>Waits until <paramref name="until"/> (Stopwatch ticks): sleeps most of the way, spins the end.</summary>
    public static void WaitUntil(long until)
    {
        while (true)
        {
            var left = until - Stopwatch.GetTimestamp();
            if (left <= 0)
                return;
            var sleepMs = left * 1000.0 / Stopwatch.Frequency - SpinMs;
            if (sleepMs >= 0.25)
                Sleep(sleepMs);
            else if (!Thread.Yield())
                Thread.SpinWait(20);
        }
    }

    private static void Sleep(double ms)
    {
        if (OperatingSystem.IsWindows())
        {
            if (_timer == -1)
                _timer = Win.MakeTimer();
            if (_timer != 0)
            {
                var due = -(long)(ms * 10_000); // relative, in 100 ns units
                if (Win.SetWaitableTimerEx(_timer, ref due, 0, 0, 0, 0, 0) != 0)
                {
                    Win.WaitForSingleObject(_timer, 0xFFFFFFFF);
                    return;
                }
            }
            else if (!_timerPeriodSet)
            {
                _timerPeriodSet = true;
                Win.timeBeginPeriod(1); // older Windows: 1 ms sleeps instead of the 15.6 ms default
            }
        }
        if (ms >= 1)
            Thread.Sleep((int)ms);
        else if (!Thread.Yield())
            Thread.SpinWait(20);
    }

    private static partial class Win
    {
        private const uint HighResolution = 0x2; // CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
        private const uint AllAccess = 0x1F0003; // TIMER_ALL_ACCESS

        public static nint MakeTimer()
        {
            try
            {
                return CreateWaitableTimerExW(0, 0, HighResolution, AllAccess);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        [LibraryImport("kernel32.dll")]
        private static partial nint CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);

        [LibraryImport("kernel32.dll")]
        public static partial int SetWaitableTimerEx(nint timer, ref long due, int period, nint routine, nint arg, nint wakeContext, uint tolerableDelay);

        [LibraryImport("kernel32.dll")]
        public static partial uint WaitForSingleObject(nint handle, uint ms);

        [LibraryImport("winmm.dll")]
        public static partial uint timeBeginPeriod(uint ms);
    }
}
