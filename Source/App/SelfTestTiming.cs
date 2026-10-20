using System.Runtime.InteropServices;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>Most CPU time one server tick may use in the bot matches: half the 16.7 ms budget of a 60 Hz tick.</summary>
    private const double MaxTickWorkMs = 8;

    /// <summary>
    /// How much work each server tick does: the game thread's CPU time, so a tick the OS paused to run something else
    /// (CI runners do that a lot) doesn't count as slow code. Windows only (the thread's cycle counter, turned into ms
    /// with the cycles per ms of ordinary ticks on the same machine); elsewhere <see cref="Available"/> is false.
    /// </summary>
    private sealed class TickCpu
    {
        private ulong _start;
        private double _calCycles, _calMs, _worstCycles;

        public static bool Available => OperatingSystem.IsWindows();

        public void Start() => _start = Cycles();

        /// <param name="wallMs">The tick's wall-clock time.</param>
        /// <param name="count">Whether this tick counts towards the worst (the first second is warm-up).</param>
        public void Stop(double wallMs, bool count)
        {
            if (!Available)
                return;
            var c = Cycles() - _start;
            if (wallMs > 0 && wallMs < 5)
            {
                _calCycles += c; // ordinary ticks calibrate cycles per ms (a pause in one only makes the estimate cautious)
                _calMs += wallMs;
            }
            if (count)
                _worstCycles = Math.Max(_worstCycles, c);
        }

        /// <summary>CPU time of the busiest tick, in ms (0 when it can't be measured).</summary>
        public double WorstMs => Available && _calMs > 0 && _calCycles > 0 ? _worstCycles / (_calCycles / _calMs) : 0;

        public string WorstText => Available ? $"{WorstMs:0.00} ms" : "n/a";

        private static ulong Cycles() => Available && QueryThreadCycleTime(GetCurrentThread(), out var c) ? c : 0;

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryThreadCycleTime(nint thread, out ulong cycles);

        [DllImport("kernel32.dll")]
        private static extern nint GetCurrentThread();
    }
}
