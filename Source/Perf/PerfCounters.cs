using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Per-frame counters for the performance window: draw calls, triangles and instances (counted at every GL draw call
/// in the renderers: a few integer adds, always on), and while <see cref="Perf.On"/> the garbage collector (collections
/// per generation, bytes allocated on the main thread and on every thread) and the managed heap. Kept per frame in rings
/// alongside <see cref="Perf"/>'s history; nothing allocates.
/// </summary>
public static class PerfCounters
{
    // This frame (the renderers add to these directly).
    public static int Draws, Triangles, Instances;

    private static readonly int[] DrawsRing = new int[Perf.History];
    private static readonly int[] TrianglesRing = new int[Perf.History];
    private static readonly int[] InstancesRing = new int[Perf.History];
    private static readonly int[] AllocRing = new int[Perf.History]; // main-thread bytes allocated that frame
    private static readonly int[] AllocAllRing = new int[Perf.History]; // every thread
    private static readonly byte[] GcRing = new byte[Perf.History]; // bit 0/1/2: a gen 0/1/2 collection that frame

    private static long _alloc = -1, _allocAll = -1;
    private static int _gc0 = -1, _gc1, _gc2;

    /// <summary>Managed heap size (bytes) at the last frame end while on.</summary>
    public static long HeapBytes;

    /// <summary>Collections seen while on, per generation.</summary>
    public static int Gen0, Gen1, Gen2;

    /// <summary>Counts one draw call of <paramref name="vertices"/> vertices (or indices) per instance.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Draw(int vertices, int instances = 1, bool strip = false)
    {
        Draws++;
        Instances += instances;
        Triangles += (strip ? Math.Max(vertices - 2, 0) : vertices / 3) * instances;
    }

    public static int DrawsAt(int back) => DrawsRing[Perf.Slot(back)];

    public static int TrianglesAt(int back) => TrianglesRing[Perf.Slot(back)];

    public static int InstancesAt(int back) => InstancesRing[Perf.Slot(back)];

    public static int AllocAt(int back) => AllocRing[Perf.Slot(back)];

    public static int AllocAllAt(int back) => AllocAllRing[Perf.Slot(back)];

    public static int GcAt(int back) => GcRing[Perf.Slot(back)];

    /// <summary>Called by <see cref="Perf.EndFrame"/>: stores the frame in slot <paramref name="slot"/> (-1: off or paused, nothing stored).</summary>
    internal static void EndFrame(int slot)
    {
        if (slot >= 0)
        {
            DrawsRing[slot] = Draws;
            TrianglesRing[slot] = Triangles;
            InstancesRing[slot] = Instances;
            var a = GC.GetAllocatedBytesForCurrentThread();
            var all = GC.GetTotalAllocatedBytes(false);
            AllocRing[slot] = _alloc < 0 ? 0 : (int)Math.Min(a - _alloc, int.MaxValue);
            AllocAllRing[slot] = _allocAll < 0 ? 0 : (int)Math.Min(all - _allocAll, int.MaxValue);
            _alloc = a;
            _allocAll = all;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            byte bits = 0;
            if (_gc0 >= 0)
            {
                if (g0 != _gc0)
                    bits |= 1;
                if (g1 != _gc1)
                    bits |= 2;
                if (g2 != _gc2)
                    bits |= 4;
                Gen0 += g0 - _gc0;
                Gen1 += g1 - _gc1;
                Gen2 += g2 - _gc2;
            }
            GcRing[slot] = bits;
            (_gc0, _gc1, _gc2) = (g0, g1, g2);
            HeapBytes = GC.GetTotalMemory(false);
        }
        else
            _alloc = _allocAll = _gc0 = -1; // a gap: the next frame on starts the deltas afresh
        Draws = Triangles = Instances = 0;
    }

    /// <summary>Forgets the history (the window opened, or a self-test).</summary>
    public static void Reset()
    {
        Array.Clear(DrawsRing);
        Array.Clear(TrianglesRing);
        Array.Clear(InstancesRing);
        Array.Clear(AllocRing);
        Array.Clear(AllocAllRing);
        Array.Clear(GcRing);
        _alloc = _allocAll = _gc0 = -1;
        Gen0 = Gen1 = Gen2 = 0;
        Draws = Triangles = Instances = 0;
    }
}
