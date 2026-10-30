namespace CallOfSlop;

/// <summary>
/// One stroke of the paint tool (hold LMB and sweep): the parts and links already done, so a part is never acted on twice
/// in a stroke. <see cref="Try"/> says whether a target is new to the stroke; <see cref="Clear"/> ends it (the button went
/// up, or the tool went away). The whole-construct mode is one target for the construct. Nothing allocates after the first
/// stroke (the set starts big enough for any sweep).
/// </summary>
public sealed class PaintStroke
{
    private readonly HashSet<long> _done = new(1024);

    public int Count => _done.Count;

    /// <summary>The key of part (or link) <paramref name="index"/> of construct <paramref name="body"/> (<paramref name="whole"/>: the construct itself).</summary>
    public static long Key(ushort body, bool route, int index, bool whole = false) =>
        ((long)body << 16) | (whole ? 0x7FFFL : route ? 0x8000L | (uint)index : (uint)index);

    /// <summary>True the first time a target comes up in this stroke (act on it), false after (leave it).</summary>
    public bool Try(ushort body, bool route, int index, bool whole = false) => _done.Add(Key(body, route, index, whole));

    public void Clear()
    {
        if (_done.Count > 0)
            _done.Clear();
    }
}
