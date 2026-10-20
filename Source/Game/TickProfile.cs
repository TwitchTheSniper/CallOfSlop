using System.Diagnostics;

namespace CallOfSlop;

/// <summary>TEMPORARY: per-tick timing by phase, for hunting server tick spikes in the self-test.</summary>
public static class TickProfile
{
    public static readonly string[] Names =
    [
        "pre", "match", "proj", "streaks", "bstreak", "pump", "perceive", "decide", "steer", "nearest", "astar",
        "aim", "shoot", "nade", "buttons", "physics", "spawn", "hist", "snap", "post", "log",
    ];

    public const int Pre = 0, Match = 1, Proj = 2, Streaks = 3, BStreak = 4, Pump = 5, Perceive = 6, Decide = 7, Steer = 8,
        Nearest = 9, AStar = 10, Aim = 11, Shoot = 12, Nade = 13, Buttons = 14, Physics = 15, Spawn = 16, Hist = 17, Snap = 18, Post = 19, Log = 20;

    public static readonly double[] Ms = new double[Names.Length];
    public static int Searches, Expanded, MaxExpanded;
    public static double PumpMaxStep;
    public static string PumpMaxStage = "";
    public static string Stage = "";

    public static void Reset()
    {
        Array.Clear(Ms);
        Searches = Expanded = MaxExpanded = 0;
        PumpMaxStep = 0;
        PumpMaxStage = "";
    }

    public static long Now() => Stopwatch.GetTimestamp();

    public static void Add(int phase, long t0) => Ms[phase] += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

    /// <summary>Times every console write (the game log file, flushed per line).</summary>
    public sealed class TimedWriter(TextWriter inner) : TextWriter
    {
        public static double MaxMs;
        public override System.Text.Encoding Encoding => inner.Encoding;

        public override void Write(char value)
        {
            var t = Now();
            inner.Write(value);
            Done(t);
        }

        public override void Write(string? value)
        {
            var t = Now();
            inner.Write(value);
            Done(t);
        }

        public override void WriteLine(string? value)
        {
            var t = Now();
            inner.WriteLine(value);
            Done(t);
        }

        private static void Done(long t)
        {
            var ms = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            Ms[Log] += ms;
            MaxMs = Math.Max(MaxMs, ms);
        }

        public override void Flush() => inner.Flush();
    }

    public static string Breakdown()
    {
        var parts = new List<string>();
        for (var i = 0; i < Names.Length; i++)
            if (Ms[i] >= 0.05)
                parts.Add($"{Names[i]} {Ms[i]:0.0}");
        parts.Add($"A* {Searches}x/{Expanded}n(max {MaxExpanded})");
        if (PumpMaxStep > 0)
            parts.Add($"pumpstep {PumpMaxStep:0.0}@{PumpMaxStage}");
        return string.Join(", ", parts);
    }
}
