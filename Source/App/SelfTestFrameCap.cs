using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// Max FPS (FrameLimiter.cs, Settings > Video): which cap applies (VSync, menus, background), the values a file or a
/// typed number may hold, the limiter's schedule on scripted clocks (the target frame times, an exact average rate,
/// a late wake made up, a long frame resetting the schedule with no burst after it, a cap change starting over), a few
/// short real waits (never early, no allocations) and the settings round-trip (old files load with no cap). Headless.
/// </summary>
public static partial class SelfTest
{
    private static void CheckFrameCap()
    {
        CheckFrameCapChoice();
        CheckFrameCapSchedule();
        CheckFrameCapWaits();
        CheckFrameCapSettings();
    }

    private static void CheckFrameCapChoice()
    {
        (int Main, int Menu, int Bg, bool VSync, bool InMenu, bool InBg, int Want, string What)[] cases =
        [
            (0, 0, 0, false, false, false, 0, "the defaults: no cap"),
            (144, 0, 0, false, false, false, 144, "Max FPS with VSync off"),
            (144, 0, 0, true, false, false, 0, "VSync on wins over Max FPS"),
            (144, 60, 0, false, true, false, 60, "the menu cap in a menu"),
            (144, 60, 0, false, false, false, 144, "the menu cap out of menus"),
            (50, 60, 0, false, true, false, 50, "a lower Max FPS beats the menu cap"),
            (0, 60, 0, true, true, false, 60, "the menu cap with VSync on"),
            (0, 60, 30, true, true, true, 30, "background beats menu"),
            (240, 0, 30, false, false, true, 30, "the background cap unfocused"),
            (0, 0, 0, false, true, true, 0, "menu and background caps off"),
            (5, 0, 0, false, false, false, FrameCap.Min, "a cap below 20 is held at 20"),
            (5000, 0, 0, false, false, false, FrameCap.Max, "a cap above 1000 is held at 1000"),
        ];
        foreach (var c in cases)
        {
            var got = FrameCap.Choose(c.Main, c.Menu, c.Bg, c.VSync, c.InMenu, c.InBg);
            if (got != c.Want)
                Fail($"frame cap: {c.What}: got {got}, want {c.Want}");
        }
        if (FrameCap.Clamp(-3) != 0 || FrameCap.Clamp(0) != 0 || FrameCap.Clamp(19) != 20 || FrameCap.Clamp(237) != 237 || FrameCap.Clamp(1001) != 1000)
            Fail("frame cap: Clamp doesn't keep 0 as no cap and 20-1000 otherwise");
        if (FrameCap.Presets.Any(p => FrameCap.Clamp(p) != p) || !FrameCap.Presets.SequenceEqual(FrameCap.Presets.Order()))
            Fail("frame cap: a preset is out of range or out of order");
        Line($"choice: {cases.Length} cases (VSync wins over Max FPS; menu and background caps apply with VSync too; lowest wins), presets {string.Join("/", FrameCap.Presets)}");
    }

    private static void CheckFrameCapSchedule()
    {
        var f = Stopwatch.Frequency;
        long Ms(double ms) => (long)(ms * f / 1000.0);
        double ToMs(long t) => t * 1000.0 / f;

        // Target frame times.
        foreach (var (fps, ms) in new[] { (30, 33.333), (60, 16.667), (144, 6.944), (240, 4.167), (1000, 1.0) })
            if (Math.Abs(ToMs(FrameLimiter.Period(fps)) - ms) > 0.01)
                Fail($"frame cap: {fps} fps is {ToMs(FrameLimiter.Period(fps)):0.000} ms a frame, want {ms:0.000}");
        if (FrameLimiter.Period(0) != 0 || ToMs(FrameLimiter.Tolerance(FrameLimiter.Period(60))) > 2.0001 || ToMs(FrameLimiter.Tolerance(FrameLimiter.Period(1000))) > 0.2501)
            Fail("frame cap: the late-wake tolerance isn't a quarter frame up to 2 ms");

        // A scripted clock: each frame does `work` ms, then the limiter waits; the wait ends exactly on time plus `late`.
        var lim = new FrameLimiter();
        var period = FrameLimiter.Period(60);
        long now = 1_000_000;
        if (lim.Plan(now, 60) != 0)
            Fail("frame cap: the first capped frame waited");
        long Frame(double workMs, double lateMs)
        {
            var start = now;
            now += Ms(workMs);
            var wait = lim.Plan(now, 60);
            now += wait + (wait > 0 ? Ms(lateMs) : 0);
            return now - start; // this frame's length, as the frame graph sees it
        }

        // Steady frames: every frame is exactly the period long, and 600 of them take 10 s to the tick.
        var t0 = now;
        for (var i = 0; i < 600; i++)
        {
            var len = Frame(5, 0);
            if (Math.Abs(len - period) > 1)
                Fail($"frame cap: steady frame {i} took {ToMs(len):0.000} ms, want {ToMs(period):0.000}");
        }
        if (Math.Abs(ToMs(now - t0) - 10_000) > 0.01)
            Fail($"frame cap: 600 frames at 60 took {ToMs(now - t0):0.000} ms, want 10000");

        // Waking 0.3 ms late every frame: the schedule holds, so the average stays 60 (each lateness is made up next frame).
        t0 = now;
        for (var i = 0; i < 600; i++)
            Frame(5, 0.3);
        var lateAvg = ToMs(now - t0) / 600;
        if (Math.Abs(lateAvg - ToMs(period)) > 0.01)
            Fail($"frame cap: waking 0.3 ms late drifts the rate: {lateAvg:0.0000} ms a frame, want {ToMs(period):0.0000}");

        // A long frame (50 ms): no wait after it, then the schedule starts over: the next frames are full length, no burst.
        var longLen = Frame(50, 0);
        if (Math.Abs(ToMs(longLen) - 50) > 0.31)
            Fail($"frame cap: a 50 ms frame waited ({ToMs(longLen):0.00} ms)");
        double shortest = double.MaxValue;
        for (var i = 0; i < 10; i++)
            shortest = Math.Min(shortest, ToMs(Frame(5, 0)));
        if (shortest < ToMs(period) - 0.001)
            Fail($"frame cap: after a long frame a frame was rushed out in {shortest:0.000} ms (a catch-up burst; want {ToMs(period):0.000})");

        // A frame 1 ms over the period (within the tolerance): it doesn't reset; the next is 1 ms short, then steady again.
        var over = Frame(ToMs(period) + 1, 0);
        var next = Frame(5, 0);
        var after = Frame(5, 0);
        if (Math.Abs(ToMs(over) - ToMs(period) - 1) > 0.01 || Math.Abs(ToMs(next) - (ToMs(period) - 1)) > 0.01 || Math.Abs(after - period) > 1)
            Fail($"frame cap: a frame 1 ms over: {ToMs(over):0.000}, then {ToMs(next):0.000}, {ToMs(after):0.000} ms (want +1, -1, then the period)");

        // A frame 3 ms over (beyond the 2 ms tolerance): it resets; the next is a full period, not 3 ms short.
        Frame(ToMs(period) + 3, 0);
        var reset = Frame(5, 0);
        if (Math.Abs(reset - period) > 1)
            Fail($"frame cap: after a frame 3 ms over, the next took {ToMs(reset):0.000} ms, want {ToMs(period):0.000}");

        // A new cap, or none, starts the schedule over.
        if (lim.Plan(now + Ms(1), 120) != 0 || lim.Plan(now + Ms(2), 120) <= 0)
            Fail("frame cap: a new cap didn't start the schedule over (or didn't wait the frame after)");
        if (lim.Plan(now + Ms(3), 0) != 0 || lim.Release != 0 || lim.Plan(now + Ms(4), 120) != 0)
            Fail("frame cap: no cap still waits, or doesn't clear the schedule");
        Line($"schedule: 60 fps = {ToMs(period):0.000} ms, 600 steady frames = 10 s exactly, 0.3 ms late wakes keep the rate ({lateAvg:0.0000} ms), after a 50 ms frame the shortest is {shortest:0.000} ms (no burst), +1 ms made up, +3 ms resets");
    }

    private static void CheckFrameCapWaits()
    {
        // Never early: a 3 ms wait ends at or after its time (late only by the scheduler; not checked: the box may be busy).
        var start = Stopwatch.GetTimestamp();
        FrameLimiter.WaitUntil(start + Stopwatch.Frequency * 3 / 1000);
        var took = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        if (took < 3)
            Fail($"frame cap: a 3 ms wait ended after {took:0.000} ms");

        // The main loop's call at 500 fps: frames no shorter than the period, and no allocations.
        var lim = new FrameLimiter();
        lim.Limit(500);
        lim.Limit(500); // (warm: the timer is made on the first sleep)
        var before = GC.GetAllocatedBytesForCurrentThread();
        var t0 = Stopwatch.GetTimestamp();
        for (var i = 0; i < 10; i++)
            lim.Limit(500);
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        var ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        if (ms < 10 * 2.0 - 2.0 - 0.05) // (ten 2 ms frames; the first may be up to the 2 ms tolerance short)
            Fail($"frame cap: ten frames at 500 fps took {ms:0.00} ms, want at least 18");
        if (alloc > 0)
            Fail($"frame cap: the limiter allocated {alloc} bytes over ten frames");
        lim.Limit(0);
        Line($"waits: 3 ms wait took {took:0.000} ms; ten frames at 500 fps took {ms:0.00} ms (want 20), {alloc} bytes allocated; spin {FrameLimiter.SpinMs:0.0} ms");
    }

    private static void CheckFrameCapSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cos_framecap_{Environment.ProcessId}.txt");
        try
        {
            new Settings { MaxFps = 237, MaxFpsMenu = 60, MaxFpsBackground = 30, VSync = false }.Save(path);
            var back = Settings.Load(path);
            File.WriteAllText(path, "volume=0.5\nvsync=0\n"); // an old save without the keys
            var old = Settings.Load(path);
            File.WriteAllText(path, "max_fps=5\nmax_fps_menu=99999\nmax_fps_background=abc\n");
            var bad = Settings.Load(path);
            Line($"settings: saved 237/60/30 -> {back.MaxFps}/{back.MaxFpsMenu}/{back.MaxFpsBackground}; old file {old.MaxFps}/{old.MaxFpsMenu}/{old.MaxFpsBackground}; 5/99999/abc -> {bad.MaxFps}/{bad.MaxFpsMenu}/{bad.MaxFpsBackground}");
            if (back.MaxFps != 237 || back.MaxFpsMenu != 60 || back.MaxFpsBackground != 30 || back.VSync)
                Fail("frame cap: the Max FPS settings don't round-trip");
            if (old.MaxFps != 0 || old.MaxFpsMenu != 0 || old.MaxFpsBackground != 0 || Math.Abs(old.Volume - 0.5f) > 1e-6)
                Fail("frame cap: an old settings file doesn't load with no caps");
            if (bad.MaxFps != FrameCap.Min || bad.MaxFpsMenu != FrameCap.Max || bad.MaxFpsBackground != 0)
                Fail("frame cap: out-of-range or broken values aren't held to 20-1000 / no cap");
            if (new Settings().MaxFps != 0)
                Fail("frame cap: the default isn't Unlimited");
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }
}
