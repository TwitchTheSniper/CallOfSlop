using System.Diagnostics.Tracing;
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
        private TimeSpan _jit0, _pause0;
        private long _methods0;
        private int _gc0, _gc2;

        /// <summary>What happened during the busiest tick: JIT compiling and GC (they run on the game thread too).</summary>
        public string WorstDetail { get; private set; } = "";

        public static bool Available => OperatingSystem.IsWindows();

        private DateTime _t0;
        public DateTime WorstFrom, WorstTo;

        public void Start()
        {
            _t0 = DateTime.UtcNow;
            _jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
            _methods0 = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true);
            _pause0 = GC.GetTotalPauseDuration();
            _gc0 = GC.CollectionCount(0);
            _gc2 = GC.CollectionCount(2);
            _start = Cycles();
        }

        /// <param name="wallMs">The tick's wall-clock time.</param>
        /// <param name="count">Whether this tick counts towards the worst (the first second is warm-up).</param>
        public void Stop(double wallMs, bool count, Func<string>? context = null)
        {
            if (!Available)
                return;
            var c = Cycles() - _start;
            if (count && c > _worstCycles)
            {
                var jit = System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - _jit0;
                var methods = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true) - _methods0;
                var pause = GC.GetTotalPauseDuration() - _pause0;
                WorstFrom = _t0;
                WorstTo = DateTime.UtcNow;
                WorstDetail = $"wall {wallMs:0.0} ms, JIT {jit.TotalMilliseconds:0.0} ms ({methods} methods), GC pause {pause.TotalMilliseconds:0.0} ms "
                    + $"({GC.CollectionCount(0) - _gc0} collections, {GC.CollectionCount(2) - _gc2} full)" + (context != null ? ", " + context() : "");
            }
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

    /// <summary>TEMP: names of methods the JIT compiled, with times, to see what a busy tick compiled.</summary>
    private sealed class JitWatch : EventListener
    {
        public readonly List<(DateTime At, string Name)> Loads = new();

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x10);
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName == null || !e.EventName.StartsWith("MethodLoadVerbose") || e.Payload == null || e.PayloadNames == null)
                return;
            string P(string n) => e.PayloadNames.IndexOf(n) is var i and >= 0 ? e.Payload[i]?.ToString() ?? "" : "";
            lock (Loads)
                Loads.Add((e.TimeStamp.ToUniversalTime(), $"{P("MethodNamespace")}::{P("MethodName")} size {P("MethodSize")} flags {P("MethodFlags")}"));
        }

        public string During(DateTime from, DateTime to)
        {
            Thread.Sleep(300); // events arrive a little late
            lock (Loads)
                return string.Join(" ; ", Loads.Where(l => l.At >= from.AddMilliseconds(-2) && l.At <= to.AddMilliseconds(2)).Select(l => l.Name));
        }
    }
}
