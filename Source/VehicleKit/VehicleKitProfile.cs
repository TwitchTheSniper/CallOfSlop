using System.Diagnostics;
using System.Text;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// Where a generation's time goes, by stage (<c>--slice-vehicle NAME --profile</c>: VEHICLEKIT.md, "Generation time"). Off unless a probe turns it on: then each stage
/// adds its wall time and count. Stages nest (a plan's build holds its Puts, a Put its tries), so the table reads as a tree, not a sum. One generation at a time.
/// </summary>
public static class KitProfile
{
    public enum S
    {
        Plan, Build, AfterBuild, DriveGate, IdentityGate, PutScan, PutTry, Onto, Attach, Rebuild, Weld, Wire, Link, LoftMake, Fill, Text, Parse, Brief, Gunsmith, Count,
    }

    private static readonly string[] Names =
    [
        "plan (grammar)", "build (the plan's parts)", "after build", "drive gate", "identity gate", "Put's scan (tries in it)", "Put's tries (Place)", "Onto (fit, turn, place)",
        "attach", "rebuild after a step", "weld", "wire", "link", "loft parts made", "fill (outline)", "text", "parse", "brief", "gunsmith checks",
    ];

    public static bool On;
    private static readonly long[] T = new long[(int)S.Count];
    private static readonly int[] N = new int[(int)S.Count];

    public readonly struct Span(S s, long t0) : IDisposable
    {
        public void Dispose()
        {
            if (t0 != 0)
            {
                T[(int)s] += Stopwatch.GetTimestamp() - t0;
                N[(int)s]++;
            }
        }
    }

    /// <summary><c>using var _ = KitProfile.Time(S.X);</c>: a struct, nothing allocated; nothing measured when off.</summary>
    public static Span Time(S s) => new(s, On ? Stopwatch.GetTimestamp() : 0);

    public static void Add(S s, long ticks)
    {
        T[(int)s] += ticks;
        N[(int)s]++;
    }

    public static void Reset()
    {
        Array.Clear(T);
        Array.Clear(N);
    }

    /// <summary>The table, each stage's ms a generation (divided by <paramref name="runs"/>) and its count a generation.</summary>
    public static string Report(int runs)
    {
        var sb = new StringBuilder();
        for (var k = 0; k < (int)S.Count; k++)
            if (N[k] > 0)
                sb.Append($"  {Names[k],-28} {T[k] * 1000.0 / Stopwatch.Frequency / runs,9:0.0} ms {N[k] / (double)runs,9:0.0} times\n");
        return sb.ToString();
    }
}
