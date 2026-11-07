using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// Scratch memory for spans sized by a construct (a part, a link or a node each), in place of <c>stackalloc</c>: a big
/// construct's floods, drive line, logic step and merges would otherwise put hundreds of KB on the stack (the drive
/// line alone ~400 KB at 8,000 parts; Windows gives the main thread 1 MB, and an overflow is a native crash).
/// One arena a thread, used last in, first out: a method opens a frame, takes its spans from it and gives them all back
/// when the frame ends (<c>using var s = Scratch.Begin();</c>, then <c>s.Take&lt;bool&gt;(PartCount)</c>). A span comes
/// cleared, as a <c>stackalloc</c> one does. Frames nest (a rebuild that floods, a flood that merges).
/// The arena grows by doubling when a frame needs more than it has ever had (a bigger construct than before: an event,
/// never a steady tick) and never shrinks, so a steady tick allocates nothing. Spans already taken stay valid across a
/// growth (they keep the old buffer alive). docs/perf/LIMITS.md, "Parts a construct".
/// Small spans with a fixed bound (a part's sockets, the joints, a track's wheels) stay on the stack.
/// </summary>
public static class Scratch
{
    [ThreadStatic] private static Arena? _arena;

    /// <summary>Opens a frame on this thread's arena: everything taken from it is given back when it is disposed.</summary>
    public static Frame Begin()
    {
        var a = _arena ??= new Arena();
        return new Frame(a, a.Used);
    }

    /// <summary>Makes this thread's arena at least <paramref name="bytes"/> long (outside the tick, e.g. when a construct reserves parts).</summary>
    public static void Reserve(int bytes) => (_arena ??= new Arena()).Reserve(bytes);

    /// <summary>This thread's arena size in bytes (0 before its first use), for the perf notes.</summary>
    public static int Capacity => _arena?.Capacity ?? 0;

    /// <summary>Times any arena grew (a growth allocates; a steady tick must not).</summary>
    public static int Growths;

    public sealed class Arena
    {
        private const int Align = 16;
        private byte[] _buf = new byte[64 * 1024];
        internal int Used;

        public int Capacity => _buf.Length;

        internal void Reserve(int bytes)
        {
            if (bytes > _buf.Length)
                Grow(bytes);
        }

        private void Grow(int need)
        {
            var n = _buf.Length;
            while (n < need)
                n = n >= (1 << 29) ? int.MaxValue : n * 2;
            // The old buffer is dropped, not copied: spans taken from it hold it alive, and offsets below Used stay
            // reserved in the new one, so a frame's mark still means the same place.
            _buf = new byte[n];
            Interlocked.Increment(ref Growths);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Span<T> Take<T>(int count) where T : unmanaged
        {
            if (count <= 0)
                return Span<T>.Empty;
            var bytes = checked(count * Unsafe.SizeOf<T>());
            var at = (Used + Align - 1) & ~(Align - 1);
            var end = checked(at + bytes);
            if (end > _buf.Length)
                Grow(end);
            Used = end;
            var s = MemoryMarshal.Cast<byte, T>(_buf.AsSpan(at, bytes));
            s.Clear();
            return s;
        }
    }

    /// <summary>A frame of scratch: disposing it gives back everything taken since it opened.</summary>
    public readonly ref struct Frame
    {
        private readonly Arena _a;
        private readonly int _mark;

        internal Frame(Arena a, int mark)
        {
            _a = a;
            _mark = mark;
        }

        /// <summary>A cleared span of <paramref name="count"/> items, valid until this frame ends.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Take<T>(int count) where T : unmanaged => _a.Take<T>(count);

        public void Dispose() => _a.Used = _mark;
    }
}
