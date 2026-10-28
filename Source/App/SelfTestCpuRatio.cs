using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// CPU comparisons that a noisy CI runner can't fake: "does B cost more than X times A?". Both sides are timed on
/// this thread's own CPU clock (so time the OS spends running something else doesn't count), interleaved in one loop
/// (A, B, B, A, A, B...: a slow patch of the machine hits both alike), after a warm-up with identical work, and the
/// answer is the ratio of each side's quickest run. Every music CPU rule (frenzy, frenzy classes, harder and weird
/// tracks) goes through <see cref="CompareCpu"/>. Also <see cref="SteadyAllocBytes"/> for the allocation probes.
/// <para>
/// Why the quickest run (2026-10-28, fix/windows-flaky-checks): every run does identical, deterministic work, so a
/// run can only be slowed by the machine (the other hyperthread of a 2-vCPU runner busy, cache and memory traffic of
/// other threads, a VM's stolen cycles: Windows' thread cycle counter counts those), never sped up. The median of the
/// per-pair ratios let that noise through on the Windows runners: the same Launcher level-6 comparison read 1.51x,
/// 1.38x and 1.07x on three main runs of one commit, against 1.37-1.38x every time on Linux, where the per-pair
/// ratios sit within +-0.03. The quickest run of each side is the cost of the work itself.
/// </para>
/// </summary>
public static partial class SelfTest
{
    /// <summary>A summary of every music CPU comparison made in this run (shown in the "Frenzy classes (results)" annotation).</summary>
    private static readonly List<string> CpuRatioNotes = new();

    /// <summary>
    /// The result of <see cref="CompareCpu"/>: the cost ratio (candidate / reference, each side's quickest run), the
    /// CPU ms of each side's quickest run, and the spread of the per-pair ratios (largest minus smallest), for the log.
    /// </summary>
    private readonly record struct CpuCompare(double Ratio, double RefMs, double CandMs, double Spread);

    /// <summary>
    /// Times <paramref name="reference"/> against <paramref name="candidate"/>. Each is a setup that runs untimed and
    /// returns the work to time (e.g. load a track at its drop, then render two seconds of it), so every timed run does
    /// identical work. Two warm-up pairs, then <paramref name="pairs"/> timed pairs, alternating which side goes first;
    /// the ratio is the candidate's quickest run over the reference's.
    /// </summary>
    private static CpuCompare CompareCpu(Func<Action> reference, Func<Action> candidate, int pairs = 9)
    {
        for (var w = 0; w < 2; w++)
        {
            reference()();
            candidate()();
        }
        var ratios = new double[pairs];
        var refMs = new double[pairs];
        var candMs = new double[pairs];
        for (var i = 0; i < pairs; i++)
        {
            if ((i & 1) == 0)
            {
                refMs[i] = ThreadCpu.TimeMs(reference());
                candMs[i] = ThreadCpu.TimeMs(candidate());
            }
            else
            {
                candMs[i] = ThreadCpu.TimeMs(candidate());
                refMs[i] = ThreadCpu.TimeMs(reference());
            }
            ratios[i] = candMs[i] / Math.Max(1e-9, refMs[i]);
        }
        Array.Sort(ratios);
        var refBest = refMs.Min();
        var candBest = candMs.Min();
        return new CpuCompare(candBest / Math.Max(1e-9, refBest), refBest, candBest, ratios[^1] - ratios[0]);
    }

    /// <summary>
    /// Music work for <see cref="CompareCpu"/>: load <paramref name="t"/> at <paramref name="bar"/> (its first drop by
    /// default), then render <paramref name="seconds"/> of it.
    /// </summary>
    private static Func<Action> TrackWork(MusicRenderer r, MusicTrack t, double seconds, uint seed = 9, int bar = -1)
    {
        var block = new float[MusicPlayer.BlockFrames * 2];
        var blocks = Math.Max(1, (int)(seconds * Audio.SampleRate / MusicPlayer.BlockFrames));
        return () =>
        {
            r.Load(t, seed, bar >= 0 ? bar : t.FirstDropBar);
            return () =>
            {
                for (var b = 0; b < blocks; b++)
                    r.Render(block);
            };
        };
    }

    /// <summary>
    /// Frenzy work for <see cref="CompareCpu"/>: a fresh frenzy layer (seed 13) for <paramref name="cls"/> brought up to
    /// <paramref name="level"/> untimed (200 blocks: the level, the last drop, the tempo), then
    /// <paramref name="seconds"/> of mixing.
    /// </summary>
    private static Func<Action> FrenzyWork(WeaponClass cls, int level, double seconds)
    {
        var block = new float[MusicPlayer.BlockFrames * 2];
        var blocks = Math.Max(1, (int)(seconds * Audio.SampleRate / MusicPlayer.BlockFrames));
        return () =>
        {
            var f = new FrenzyLayer(13) { Class = cls };
            f.Target = level;
            for (var b = 0; b < 200; b++)
            {
                Array.Clear(block);
                f.MixInto(block, true);
            }
            return () =>
            {
                for (var b = 0; b < blocks; b++)
                {
                    Array.Clear(block);
                    f.MixInto(block, true);
                }
            };
        };
    }

    /// <summary>
    /// The most expensive of <paramref name="tracks"/> and its cost in CPU ms per run: every track timed once per round,
    /// round-robin over five rounds (so a slow patch of the machine hits them all alike), the median of each. Picking
    /// the largest of noisy numbers errs towards a heavier reference, never a lighter one.
    /// </summary>
    private static (MusicTrack Track, double Ms) HeaviestTrack(IReadOnlyList<MusicTrack> tracks, Func<MusicTrack, Func<Action>> work) =>
        HeaviestOf(tracks, work);

    /// <inheritdoc cref="HeaviestTrack"/>
    private static (T Item, double Ms) HeaviestOf<T>(IReadOnlyList<T> items, Func<T, Func<Action>> work)
    {
        const int rounds = 5;
        var works = items.Select(work).ToArray();
        foreach (var w in works)
            w()(); // warm-up (JIT, first gathers)
        var ms = new double[items.Count][];
        for (var i = 0; i < items.Count; i++)
            ms[i] = new double[rounds];
        for (var r = 0; r < rounds; r++)
            for (var i = 0; i < items.Count; i++)
                ms[i][r] = ThreadCpu.TimeMs(works[i]());
        var best = 0;
        var bestMs = -1.0;
        for (var i = 0; i < items.Count; i++)
        {
            var m = MedianOf(ms[i]);
            if (m > bestMs)
                (best, bestMs) = (i, m);
        }
        return (items[best], bestMs);
    }

    /// <summary>
    /// Bytes that steady-state work allocates on this thread: <paramref name="warmups"/> untimed rounds, then the least
    /// of <paramref name="rounds"/> measured ones, all doing identical work. A real per-call allocation shows in every
    /// round; a one-off runtime allocation (JIT tier-up, a lazily built runtime structure) in one at most, so the
    /// allocation probes stay strict (0 bytes) without flaking.
    /// </summary>
    private static long SteadyAllocBytes(Action round, int warmups = 2, int rounds = 3)
    {
        for (var w = 0; w < warmups; w++)
            round();
        var least = long.MaxValue;
        for (var r = 0; r < rounds; r++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            round();
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return least;
    }

    /// <summary>
    /// <see cref="SteadyAllocBytes(Action, int, int)"/> for rounds that take their own reading (each returns the bytes of
    /// its measured window, so per-round set-up and wind-down outside the window don't count): the least of
    /// <paramref name="rounds"/> after <paramref name="warmups"/>.
    /// </summary>
    private static long SteadyAllocBytes(Func<long> measuredRound, int warmups, int rounds)
    {
        for (var w = 0; w < warmups; w++)
            measuredRound();
        var least = long.MaxValue;
        for (var r = 0; r < rounds; r++)
            least = Math.Min(least, measuredRound());
        return least;
    }

    /// <summary>
    /// For a probe whose measured loop is costly (or also timed): <paramref name="first"/> is that loop's own reading;
    /// only when it saw bytes does <paramref name="round"/> (the same work) run <paramref name="rounds"/> more times, and
    /// the least reading counts. A real per-call allocation still shows in every round; a clean run costs nothing extra.
    /// </summary>
    private static long RecheckAllocBytes(long first, Action round, int rounds = 3)
        => first <= 0 ? first : Math.Min(first, SteadyAllocBytes(round, warmups: 0, rounds: rounds));

    /// <summary>The median of a few values (sorts a copy).</summary>
    private static double MedianOf(double[] values)
    {
        var a = (double[])values.Clone();
        Array.Sort(a);
        return a.Length % 2 == 1 ? a[a.Length / 2] : 0.5 * (a[a.Length / 2 - 1] + a[a.Length / 2]);
    }

    /// <summary>
    /// The calling thread's CPU time: CLOCK_THREAD_CPUTIME_ID on Linux and macOS, the thread's cycle counter on Windows; falls
    /// back to the wall clock where neither works (then the interleaving and medians still do most of the job).
    /// </summary>
    private static class ThreadCpu
    {
        private static int _mode = -1; // -1 unknown, 0 wall clock, 1 posix thread clock, 2 Windows thread cycles

        /// <summary>Which clock the comparisons use (for the log).</summary>
        public static string Kind
        {
            get
            {
                Detect();
                return _mode switch { 1 => "thread CPU clock", 2 => "thread CPU cycles", _ => "wall clock" };
            }
        }

        /// <summary>This thread's CPU time so far, in ms (differences only mean something on the same thread).</summary>
        public static double NowMs()
        {
            Detect();
            return NowNs() / 1e6;
        }

        /// <summary>CPU ms spent on this thread by <paramref name="work"/>.</summary>
        public static double TimeMs(Action work)
        {
            Detect();
            var t0 = NowNs();
            work();
            return (NowNs() - t0) / 1e6;
        }

        private static void Detect()
        {
            if (_mode >= 0)
                return;
            _mode = 0;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (QueryThreadCycleTime(GetCurrentThread(), out _))
                        _mode = 2;
                }
                else if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && ClockGetTime(out _) == 0)
                {
                    _mode = 1;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                _mode = 0;
            }
            if (_mode == 2)
                CalibrateCycles();
        }

        /// <summary>
        /// Windows counts the thread's CPU cycles: ns per cycle from a few short busy loops against the wall clock (the
        /// least wall time per cycle, i.e. a loop the OS didn't interrupt). Only the ms in the log depend on it; the
        /// ratios don't.
        /// </summary>
        private static void CalibrateCycles()
        {
            var best = double.MaxValue;
            var x = 0.0;
            for (var k = 0; k < 5; k++)
            {
                QueryThreadCycleTime(GetCurrentThread(), out var c0);
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalMilliseconds < 4)
                    x += Math.Sqrt(x + 1);
                var ns = sw.Elapsed.TotalMilliseconds * 1e6;
                QueryThreadCycleTime(GetCurrentThread(), out var c1);
                if (c1 > c0)
                    best = Math.Min(best, ns / (c1 - c0));
            }
            if (best == double.MaxValue || x < 0)
                _mode = 0;
            else
                _nsPerCycle = best;
        }

        private static double _nsPerCycle = 1;

        private static long NowNs()
        {
            switch (_mode)
            {
                case 1:
                    ClockGetTime(out var ts);
                    return ts.Sec * 1_000_000_000L + ts.Nsec;
                case 2:
                    QueryThreadCycleTime(GetCurrentThread(), out var cycles);
                    return (long)(cycles * _nsPerCycle);
                default:
                    return (long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));
            }
        }

        /// <summary>clock_gettime(CLOCK_THREAD_CPUTIME_ID): 3 on Linux (glibc's libc.so.6), 16 on macOS.</summary>
        private static int ClockGetTime(out TimeSpec ts) => OperatingSystem.IsMacOS() ? clock_gettime_mac(16, out ts) : clock_gettime_linux(3, out ts);

        [StructLayout(LayoutKind.Sequential)]
        private struct TimeSpec
        {
            public long Sec;
            public long Nsec;
        }

        [DllImport("libc.so.6", EntryPoint = "clock_gettime")]
        private static extern int clock_gettime_linux(int clock, out TimeSpec ts);

        [DllImport("libc", EntryPoint = "clock_gettime")]
        private static extern int clock_gettime_mac(int clock, out TimeSpec ts);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);

        [DllImport("kernel32.dll")]
        private static extern nint GetCurrentThread();
    }
}
