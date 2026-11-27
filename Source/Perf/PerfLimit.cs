namespace CallOfSlop;

/// <summary>What holds the frame rate back, at a glance (the F9 window's top line, its overlay, the snapshot and the recording).</summary>
public enum PerfLimitKind : byte
{
    Unknown, // no frames yet
    Cpu, // the main thread's own work takes longer than the GPU's: the GPU waits for it
    Gpu, // the GPU's passes take longer: the CPU waits for it at Swap
    Even, // within a tenth of each other: either one cut alone gains little
    VSync, // both done before the display's refresh: VSync waits
    Cap, // both done before the Max FPS cap: the limiter waits
}

/// <summary>
/// Works out <see cref="PerfLimitKind"/> from a frame's average figures. The CPU's share of a frame is the frame less the
/// Swap wait (waiting on vsync or a GPU that is behind) and the Max FPS limiter's wait; the GPU's is the sum of its timed
/// passes. No GPU timer: a Swap wait over a quarter of the frame reads as GPU. Allocates nothing.
/// </summary>
public static class PerfLimit
{
    /// <summary>VSync as the game set it (Program sets it whenever it changes the swap interval).</summary>
    public static bool VSync;

    public static PerfLimitKind Judge(float frameMs, float swapMs, float limiterMs, float gpuMs, out float cpuMs)
    {
        cpuMs = MathF.Max(0f, frameMs - swapMs - limiterMs);
        if (frameMs <= 0)
            return PerfLimitKind.Unknown;
        var busy = MathF.Max(cpuMs, gpuMs);
        if (limiterMs > frameMs * 0.1f)
            return PerfLimitKind.Cap;
        if (VSync && busy < frameMs * 0.85f && swapMs > frameMs * 0.1f)
            return PerfLimitKind.VSync;
        if (gpuMs < 0)
            return swapMs > frameMs * 0.25f ? PerfLimitKind.Gpu : PerfLimitKind.Cpu;
        if (MathF.Abs(cpuMs - gpuMs) <= busy * 0.1f)
            return PerfLimitKind.Even;
        return gpuMs > cpuMs ? PerfLimitKind.Gpu : PerfLimitKind.Cpu;
    }

    /// <summary>The short label ("CPU", "GPU", ...).</summary>
    public static string Label(PerfLimitKind k) => k switch
    {
        PerfLimitKind.Cpu => "CPU",
        PerfLimitKind.Gpu => "GPU",
        PerfLimitKind.Even => "CPU and GPU",
        PerfLimitKind.VSync => "VSync",
        PerfLimitKind.Cap => "Max FPS cap",
        _ => "?",
    };

    /// <summary>What it means, one clause.</summary>
    public static string Hint(PerfLimitKind k) => k switch
    {
        PerfLimitKind.Cpu => "the GPU waits for the main thread: look at the CPU tree",
        PerfLimitKind.Gpu => "the CPU waits for the GPU at Swap: look at the GPU passes",
        PerfLimitKind.Even => "about even: both must get quicker for more fps",
        PerfLimitKind.VSync => "both finish before the display's refresh: turn VSync off to see the real limit",
        PerfLimitKind.Cap => "both finish before the Max FPS cap: the limiter waits",
        _ => "no frames yet",
    };

    /// <summary>The snapshot's and the recording's line (allocates: only when asked for).</summary>
    public static string Line(float frameMs, float swapMs, float limiterMs, float gpuMs)
    {
        var k = Judge(frameMs, swapMs, limiterMs, gpuMs, out var cpu);
        var gpu = gpuMs >= 0 ? FormattableString.Invariant($"{gpuMs:0.00} ms") : "no timer";
        return FormattableString.Invariant($"limited by: {Label(k)} (CPU {cpu:0.00} ms busy, GPU {gpu}, Swap wait {swapMs:0.00}, frame {frameMs:0.00} ms): {Hint(k)}\n");
    }
}
