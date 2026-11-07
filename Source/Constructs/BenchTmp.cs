namespace CallOfSlop;

// THROWAWAY (not committed): stage timers for the rebuild harness.
internal static class BenchTmp
{
    public static bool On;
    public static readonly double[][] T = [new double[64], new double[64], new double[64]];
    public static readonly string?[][] N = [new string?[64], new string?[64], new string?[64]];
    private static readonly long[] _t = new long[3];
    private static readonly int[] _k = new int[3];

    public static void Mark(int set, string? name)
    {
        if (!On)
            return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (name != null)
        {
            T[set][_k[set]] += (now - _t[set]) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            N[set][_k[set]++] = name;
        }
        else
            _k[set] = 0;
        _t[set] = now;
    }

    public static void Clear()
    {
        foreach (var a in T)
            Array.Clear(a);
    }
}
