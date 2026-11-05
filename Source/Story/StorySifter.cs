using System.Runtime.CompilerServices;

namespace CallOfSlop.Story;

/// <summary>Eight ints held inline (a match's bindings, or its events per stage): no allocation.</summary>
[InlineArray(StoryRules.MaxVars)]
public struct Ints8
{
    private int _e;

    public static Ints8 Empty()
    {
        var x = new Ints8();
        ((Span<int>)x).Fill(-1);
        return x;
    }
}

/// <summary>A prospective story: a pattern part-way done, with who is bound to which variable (Winnow's partial match).</summary>
public struct StoryMatch
{
    public bool Live;
    public ushort Pattern;
    public byte Stage; // stages done (the next one waited for)
    public Ints8 Binds, Events;
    public double Started, Last;
}

/// <summary>A completed story.</summary>
public struct StoryDone
{
    public ushort Pattern;
    public double At;
    public Ints8 Binds, Events;
    public int StoryEvent;
}

public enum SiftKind : byte
{
    Started,
    Advanced,
    Completed,
    RanOut, // its next stage's time ran out
    Ended, // an "unless" event ended it
    Dropped, // the pool was full: the oldest, least advanced match went
}

/// <summary>What the sifter did, for the window's activity list and the CLI.</summary>
public struct SiftNote
{
    public double At;
    public SiftKind Kind;
    public ushort Pattern;
    public byte Stage;
    public Ints8 Binds;
    public int Event;
}

/// <summary>
/// The incremental story sifter (box 2): each new event advances the live partial matches waiting for its verb in place,
/// ends those an <c>unless</c> event names, and starts new ones. A completed match is a story: its <c>then</c> effects
/// apply and a Story event is recorded, which later patterns can build on. It runs only when an event arrives (and a sweep
/// a second for time limits), touches at most the pool, and allocates nothing once its arrays are made.
/// </summary>
public sealed class StorySifter
{
    public const int PoolSize = 512, PerPattern = 64, DoneKept = 500, NoteRing = 256;

    private readonly StoryRules _rules;
    private readonly StoryWorld _world;
    private readonly StoryChronicle _chronicle;
    public readonly StoryMatch[] Pool = new StoryMatch[PoolSize];
    private readonly int[] _livePer;
    private readonly int[][] _startsBy; // verb -> patterns whose first stage is that verb
    public int LiveCount { get; private set; }

    public readonly List<StoryDone> Done = new(DoneKept);
    public readonly int[] Started, Completed, RanOut, Ended, Dropped; // per pattern, since the world began

    public readonly SiftNote[] Notes = new SiftNote[NoteRing];
    public long NoteTotal;

    /// <summary>Stories completed by the last event, for the engine to record (in order).</summary>
    public readonly List<int> Pending = new(16);

    public StorySifter(StoryRules rules, StoryWorld world, StoryChronicle chronicle)
    {
        _rules = rules;
        _world = world;
        _chronicle = chronicle;
        var n = rules.Patterns.Length;
        _livePer = new int[n];
        Started = new int[n];
        Completed = new int[n];
        RanOut = new int[n];
        Ended = new int[n];
        Dropped = new int[n];
        _startsBy = new int[rules.Verbs.Length][];
        for (var v = 0; v < rules.Verbs.Length; v++)
            _startsBy[v] = rules.Patterns.Where(p => p.Stages[0].Verb == v).Select(p => (int)p.Id).ToArray();
    }

    public void Clear()
    {
        Array.Clear(Pool);
        Array.Clear(_livePer);
        LiveCount = 0;
        Done.Clear();
        foreach (var a in (int[][])[Started, Completed, RanOut, Ended, Dropped])
            Array.Clear(a);
        NoteTotal = 0;
        Pending.Clear();
    }

    private void Note(SiftKind kind, in StoryMatch m, int ev, double at)
    {
        ref var n = ref Notes[NoteTotal++ % NoteRing];
        n.At = at;
        n.Kind = kind;
        n.Pattern = m.Pattern;
        n.Stage = m.Stage;
        n.Binds = m.Binds;
        n.Event = ev;
    }

    /// <summary>Does an event fit a step, with these bindings? On true, <paramref name="binds"/> holds the new ones (and those its <c>where</c> found).</summary>
    private bool Fits(PatternStep step, in StoryEvent ev, ref Ints8 binds, bool bind)
    {
        if (step.Verb != ev.Verb || step.Story >= 0 && ev.Value != step.Story)
            return false;
        var b = binds; // (a copy: committed only when it all fits)
        Span<int> s = b;
        for (var r = 0; r < step.Roles.Length; r++)
        {
            var v = step.Roles[r];
            if (v < 0)
                continue;
            var who = ev.Role(r);
            if (s[v] >= 0)
            {
                if (s[v] != who)
                    return false;
                continue;
            }
            if (!bind)
                continue; // (an unless's free variable: anyone)
            if (who < 0)
                return false; // (a role a stage binds must be filled)
            s[v] = who;
        }
        if (step.Where != null && !step.Where.Any(_world, s))
            return false;
        if (bind)
            binds = b;
        return true;
    }

    /// <summary>A new event: matches end, advance and start; completed stories go to <see cref="Pending"/>.</summary>
    public void OnEvent(in StoryEvent ev)
    {
        var patterns = _rules.Patterns;
        // Live matches first (one that this event starts isn't advanced by it again).
        for (var i = 0; i < Pool.Length; i++)
        {
            ref var m = ref Pool[i];
            if (!m.Live)
                continue;
            var p = patterns[m.Pattern];
            var ended = false;
            foreach (var u in p.Unless)
                if (Fits(u, ev, ref m.Binds, false))
                {
                    ended = true;
                    break;
                }
            if (ended)
            {
                Ended[m.Pattern]++;
                Note(SiftKind.Ended, m, ev.Id, ev.At);
                Free(ref m);
                continue;
            }
            var next = p.Stages[m.Stage];
            if (next.Within > 0 && ev.At - m.Last > next.Within)
            {
                RanOut[m.Pattern]++;
                Note(SiftKind.RanOut, m, ev.Id, m.Last + next.Within);
                Free(ref m);
                continue;
            }
            if (!Fits(next, ev, ref m.Binds, true))
                continue;
            m.Events[m.Stage] = ev.Id;
            m.Stage++;
            m.Last = ev.At;
            _chronicle.Keep(ev.Id);
            if (m.Stage >= p.Stages.Length)
            {
                Complete(m, ev.At);
                Free(ref m);
            }
            else
                Note(SiftKind.Advanced, m, ev.Id, ev.At);
        }
        // New matches from this event.
        if (ev.Verb >= _startsBy.Length)
            return;
        foreach (var pi in _startsBy[ev.Verb])
        {
            var p = patterns[pi];
            var b = Ints8.Empty();
            if (!Fits(p.Stages[0], ev, ref b, true))
                continue;
            var m = new StoryMatch { Live = true, Pattern = (ushort)pi, Stage = 1, Binds = b, Events = Ints8.Empty(), Started = ev.At, Last = ev.At };
            m.Events[0] = ev.Id;
            _chronicle.Keep(ev.Id);
            Started[pi]++;
            if (p.Stages.Length == 1)
            {
                Complete(m, ev.At);
                continue;
            }
            // The same story begun again (A kills B twice): the match there is refreshed, not doubled.
            var same = -1;
            for (var i = 0; i < Pool.Length && same < 0; i++)
                if (Pool[i].Live && Pool[i].Pattern == pi && Pool[i].Stage == 1 && SameBinds(Pool[i].Binds, b))
                    same = i;
            if (same >= 0)
            {
                Started[pi]--;
                Pool[same].Events[0] = ev.Id;
                Pool[same].Started = Pool[same].Last = ev.At;
                continue;
            }
            var slot = Slot(pi, ev.At);
            if (slot < 0)
                continue;
            Pool[slot] = m;
            _livePer[pi]++;
            LiveCount++;
            Note(SiftKind.Started, m, ev.Id, ev.At);
        }
    }

    private static bool SameBinds(in Ints8 a, in Ints8 b)
    {
        for (var i = 0; i < StoryRules.MaxVars; i++)
            if (a[i] != b[i])
                return false;
        return true;
    }

    private void Free(ref StoryMatch m)
    {
        if (!m.Live)
            return;
        m.Live = false;
        _livePer[m.Pattern]--;
        LiveCount--;
    }

    /// <summary>A free slot; when the pattern or the pool is full the oldest, least advanced match goes.</summary>
    private int Slot(int pattern, double now)
    {
        var full = _livePer[pattern] >= PerPattern;
        var victim = -1;
        for (var i = 0; i < Pool.Length; i++)
        {
            ref var m = ref Pool[i];
            if (!m.Live)
            {
                if (!full)
                    return i;
                continue;
            }
            if (full && m.Pattern != pattern)
                continue;
            if (victim < 0 || m.Stage < Pool[victim].Stage || m.Stage == Pool[victim].Stage && m.Last < Pool[victim].Last)
                victim = i;
        }
        if (victim < 0)
            return -1;
        ref var v = ref Pool[victim];
        Dropped[v.Pattern]++;
        Note(SiftKind.Dropped, v, -1, now);
        Free(ref v);
        return victim;
    }

    private void Complete(in StoryMatch m, double at)
    {
        var p = _rules.Patterns[m.Pattern];
        Completed[m.Pattern]++;
        Note(SiftKind.Completed, m, m.Events[p.Stages.Length - 1], at);
        var binds = m.Binds;
        _world.Apply(p.Then, (Span<int>)binds);
        if (Done.Count >= DoneKept)
            Done.RemoveAt(0);
        Done.Add(new StoryDone { Pattern = m.Pattern, At = at, Binds = m.Binds, Events = m.Events, StoryEvent = -1 });
        Pending.Add(Done.Count - 1);
    }

    /// <summary>
    /// Someone died for good (a person: <see cref="StoryAgents.Die"/>): every live match with a stage still to come that
    /// they would act in, or have done to them, can never complete, and ends (by <paramref name="ev"/>, their death).
    /// </summary>
    public void EndFor(int entity, int ev, double at)
    {
        if (LiveCount == 0 || entity < 0)
            return;
        var patterns = _rules.Patterns;
        for (var i = 0; i < Pool.Length; i++)
        {
            ref var m = ref Pool[i];
            if (!m.Live)
                continue;
            var p = patterns[m.Pattern];
            var theirs = false;
            for (var k = m.Stage; k < p.Stages.Length && !theirs; k++)
            {
                var st = p.Stages[k];
                theirs = st.Roles[(int)StoryRole.Actor] is var a and >= 0 && m.Binds[a] == entity
                    || st.Roles[(int)StoryRole.Target] is var t and >= 0 && m.Binds[t] == entity;
            }
            if (!theirs)
                continue;
            Ended[m.Pattern]++;
            Note(SiftKind.Ended, m, ev, at);
            Free(ref m);
        }
    }

    /// <summary>Time limits, once a second: a match whose next stage can't come in time any more has run out.</summary>
    public void Sweep(double now)
    {
        if (LiveCount == 0)
            return;
        var patterns = _rules.Patterns;
        for (var i = 0; i < Pool.Length; i++)
        {
            ref var m = ref Pool[i];
            if (!m.Live)
                continue;
            var within = patterns[m.Pattern].Stages[m.Stage].Within;
            if (within > 0 && now - m.Last > within)
            {
                RanOut[m.Pattern]++;
                Note(SiftKind.RanOut, m, -1, m.Last + within); // (when its time ran out, which a coarse clock passes late)
                Free(ref m);
            }
        }
    }

    /// <summary>A pattern's sentence with its variables filled ("Rook settled the score with Karl").</summary>
    public string Tell(int pattern, in Ints8 binds)
    {
        var p = _rules.Patterns[pattern];
        if (p.Tells.Length == 0)
            return p.Name;
        var s = p.Tells;
        for (var v = 0; v < p.Vars.Length; v++)
            s = s.Replace("{" + p.Vars[v] + "}", _world.NameOf(binds[v]));
        return s;
    }

    /// <summary>"a = Karl, b = Rook" (bound variables only).</summary>
    public string BindText(int pattern, in Ints8 binds)
    {
        var p = _rules.Patterns[pattern];
        var parts = new List<string>(p.Vars.Length);
        for (var v = 0; v < p.Vars.Length; v++)
            if (binds[v] >= 0)
                parts.Add($"{p.Vars[v]} = {_world.NameOf(binds[v])}");
        return string.Join(", ", parts);
    }

    /// <summary>Restores a saved live match (the counts follow it).</summary>
    public void Restore(in StoryMatch m)
    {
        for (var i = 0; i < Pool.Length; i++)
            if (!Pool[i].Live)
            {
                Pool[i] = m;
                Pool[i].Live = true;
                _livePer[m.Pattern]++;
                LiveCount++;
                return;
            }
    }
}
