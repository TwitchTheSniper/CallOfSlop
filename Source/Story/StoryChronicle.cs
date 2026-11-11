namespace CallOfSlop.Story;

[Flags]
public enum StoryEventFlags : byte
{
    None = 0,
    Scripted = 1, // from a scenario, not from play
    Kept = 2, // part of a match or a story: never folded away
}

/// <summary>A narratable event: who did what to whom, where, when. -1: an empty role.</summary>
public struct StoryEvent
{
    public int Id;
    public double At;
    public ushort Verb;
    public int Actor, Target, Owner, Place, Item;
    public int Value; // a Story event: the pattern; otherwise free (a kill's weapon id)
    public StoryEventFlags Flags;

    public readonly int Role(StoryRole r) => r switch
    {
        StoryRole.Actor => Actor,
        StoryRole.Target => Target,
        StoryRole.Owner => Owner,
        StoryRole.Place => Place,
        _ => Item,
    };

    public readonly int Role(int r) => Role((StoryRole)r);
}

/// <summary>
/// The chronicle: every event in order, append-only, saved with the world. Past <see cref="Cap"/> events the oldest plain
/// ones (not part of any story or live match) fold into a count per verb, so a long-lived world keeps its stories and a
/// bounded list. Grows only at its cap's pace: no allocation per event once it has reached its size.
/// </summary>
public sealed class StoryChronicle
{
    public const int Cap = 20_000;

    private StoryEvent[] _e = new StoryEvent[1024];
    public int Count { get; private set; }
    public int NextId = 1;
    public long FoldedTotal;
    public int[] Folded = []; // per verb

    public ref StoryEvent this[int index] => ref _e[index];
    public ReadOnlySpan<StoryEvent> All => _e.AsSpan(0, Count);

    public void Clear()
    {
        Count = 0;
        NextId = 1;
        FoldedTotal = 0;
        Array.Clear(Folded);
    }

    public ref StoryEvent Add(in StoryEvent ev)
    {
        if (Count >= Cap)
            Fold();
        if (Count == _e.Length)
            Array.Resize(ref _e, Math.Min(Cap, _e.Length * 2));
        ref var slot = ref _e[Count++];
        slot = ev;
        slot.Id = NextId++;
        return ref slot;
    }

    /// <summary>Puts a saved event back as it was (its id kept).</summary>
    public void Restore(in StoryEvent ev)
    {
        if (Count == _e.Length)
            Array.Resize(ref _e, Math.Max(16, Math.Min(Cap + 1, _e.Length * 2)));
        _e[Count++] = ev;
        NextId = Math.Max(NextId, ev.Id + 1);
    }

    /// <summary>The index of an event by id (ids rise with the index), or -1 once folded away.</summary>
    public int IndexOf(int id)
    {
        int lo = 0, hi = Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            var k = _e[mid].Id;
            if (k == id)
                return mid;
            if (k < id)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return -1;
    }

    /// <summary>Marks an event as part of a story (it is never folded).</summary>
    public void Keep(int id)
    {
        var i = IndexOf(id);
        if (i >= 0)
            _e[i].Flags |= StoryEventFlags.Kept;
    }

    /// <summary>The oldest plain events fold into counts until a quarter of the cap is free (kept events all stay).</summary>
    private void Fold()
    {
        var target = Cap * 3 / 4;
        var drop = Count - target;
        var w = 0;
        for (var r = 0; r < Count; r++)
        {
            ref var ev = ref _e[r];
            if (drop > 0 && (ev.Flags & StoryEventFlags.Kept) == 0)
            {
                if (ev.Verb >= Folded.Length)
                    Array.Resize(ref Folded, ev.Verb + 1);
                Folded[ev.Verb]++;
                FoldedTotal++;
                drop--;
                continue;
            }
            _e[w++] = ev;
        }
        Count = w;
        if (Count >= Cap) // (everything left is kept: the oldest kept go too, so the chronicle stays bounded)
        {
            var extra = Count - target;
            Array.Copy(_e, extra, _e, 0, Count - extra);
            Count -= extra;
            FoldedTotal += extra;
        }
    }
}
