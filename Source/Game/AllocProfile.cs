using System.Runtime.CompilerServices;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Where a tick's managed allocations come from (self-test only): the game calls <see cref="Mark"/> at the start of each
/// section of the frame and the server tick, and while <see cref="On"/> the bytes the game thread allocated since the
/// previous mark are charged to the section that was running. Off (always, outside the self-test) a mark is one static
/// bool test. Allocation-free itself while measuring (fixed arrays; section names are string literals compared by
/// reference first).
/// </summary>
public static class AllocProfile
{
    /// <summary>Measuring now? (Set by the self-test around the ticks it measures.)</summary>
    public static bool On;

    private const int MaxSections = 128;
    private static readonly string[] Names = new string[MaxSections];
    private static readonly long[] Bytes = new long[MaxSections];
    private static int _count;
    private static int _current = -1;
    private static long _mark;

    /// <summary>A new section starts here (the previous one ends).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Mark(string section)
    {
        if (On)
            MarkSlow(section);
    }

    private static void MarkSlow(string section)
    {
        var now = GC.GetAllocatedBytesForCurrentThread();
        if (_current >= 0)
            Bytes[_current] += now - _mark;
        _current = IndexOf(section);
        _mark = GC.GetAllocatedBytesForCurrentThread();
    }

    /// <summary>The running section ends (nothing is charged until the next <see cref="Mark"/>).</summary>
    public static void Stop()
    {
        if (!On)
            return;
        var now = GC.GetAllocatedBytesForCurrentThread();
        if (_current >= 0)
            Bytes[_current] += now - _mark;
        _current = -1;
    }

    /// <summary>Forgets everything measured so far.</summary>
    public static void Reset()
    {
        Array.Clear(Bytes);
        _current = -1;
    }

    /// <summary>All bytes charged to any section.</summary>
    public static long Total
    {
        get
        {
            long t = 0;
            for (var i = 0; i < _count; i++)
                t += Bytes[i];
            return t;
        }
    }

    /// <summary>The <paramref name="n"/> sections that allocated most, as "name 123" per tick (allocates: call it after measuring).</summary>
    public static string Top(int n, double ticks)
    {
        var idx = Enumerable.Range(0, _count).Where(i => Bytes[i] > 0).OrderByDescending(i => Bytes[i]).Take(n);
        var sb = new StringBuilder();
        foreach (var i in idx)
            sb.Append(sb.Length > 0 ? ", " : "").Append(Names[i]).Append(' ').Append((Bytes[i] / Math.Max(1, ticks)).ToString("0.#"));
        return sb.Length > 0 ? sb.ToString() : "none";
    }

    private static int IndexOf(string section)
    {
        for (var i = 0; i < _count; i++)
            if (ReferenceEquals(Names[i], section))
                return i;
        for (var i = 0; i < _count; i++)
            if (Names[i] == section)
                return i;
        if (_count == MaxSections)
            return MaxSections - 1; // (never in practice: everything past the cap lands in the last one)
        Names[_count] = section;
        return _count++;
    }
}
