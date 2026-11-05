namespace CallOfSlop.Story;

/// <summary>Where the drama manager's pacing is (docs/story/STORY.md "Pacing").</summary>
public enum DramaPace : byte
{
    Building, // it nudges, more as the stretch goes on, and strangers may come
    Quiet, // after a big stretch: no nudges and no strangers until things have settled
}

/// <summary>
/// The drama manager's pacing (docs/story/STORY.md "Pacing"), after the build-up, peak and relax of game directors.
/// Tension rises with what happens to the people (each event as much as it is news, each story told more) and halves
/// every <see cref="TensionHalfLife"/>. Past <see cref="Peak"/> a quiet stretch starts: no stage directions and no
/// strangers, so the people go their own way and things settle, until the tension is back under <see cref="Calm"/> and
/// <see cref="MinQuiet"/> went by, or <see cref="MaxQuiet"/> did (a cast that keeps itself busy). Then the nudges build up
/// again over <see cref="BuildFor"/>, and no quiet stretch starts before that is over. While building, a story in
/// progress that waits for anyone, with nobody here who knows of it or would take it, may bring a stranger in by road who
/// heard of it on the way (the cast's <c>"strangers"</c>); a long lull with nothing to nudge brings one with history of
/// their own. Nothing here is saved: tension starts afresh after a load, and strangers who came are people like the rest.
/// </summary>
public sealed partial class StoryDrama
{
    public const double TensionHalfLife = 1200, MinQuiet = 600, MaxQuiet = 1800, BuildFor = 1200; // s (a building stretch lasts BuildFor at least)
    public const double StrangerEvery = 1800, LullFor = 2700; // s: one stranger at most this often; a lull this long brings one
    public const float Peak = 7, Calm = 3, BuildFrom = 0.4f; // tension; the nudges' share as a stretch starts building
    public const float StoryTension = 0.5f; // a story told adds its pattern's drama times this per stage

    /// <summary>Off: every story is nudged at full strength all the time, and no strangers come (phase 3's way).</summary>
    public bool Pacing = true;

    public DramaPace Pace { get; private set; }
    public double PaceSince { get; private set; }
    public long Quiets, Strangers;
    public float PeakTension;
    public double QuietTotal; // s spent quiet, the stretch under way included as of the last rank

    private float _tension;
    private double _tensionAt, _strangerAt, _busyAt, _quietFrom;
    private long _paceSeen;
    private bool _fresh = true; // (the first rank since a clear or a load starts the clocks)
    private int _open = -1; // the best story waiting for anyone that nobody here would take (the pool slot), this rank
    private double _openStarted;
    private float _openScore;
    private ushort _openVerb;
    private bool[] _strangerVerbs = []; // per verb: some cast's stranger would join a story for it

    private void MakePacing()
    {
        var rules = _engine.Rules;
        _strangerVerbs = new bool[rules.Verbs.Length];
        foreach (var cast in rules.People.Casts)
            foreach (var st in cast.Strangers)
                for (var v = 0; v < _strangerVerbs.Length; v++)
                    _strangerVerbs[v] |= st.Suits.Length == 0 ? rules.People.ActionOfVerb[v] >= 0 : Array.IndexOf(st.Suits, (ushort)v) >= 0;
    }

    /// <summary>The tension now: what happened lately, halving every <see cref="TensionHalfLife"/>.</summary>
    public float Tension(double now) => _tension * (float)Math.Pow(0.5, Math.Max(0, now - _tensionAt) / TensionHalfLife);

    /// <summary>How much of the strength the nudges get now: none in a quiet stretch, building to all of it.</summary>
    public float BuildUp(double now) =>
        !Pacing ? 1 : Pace == DramaPace.Quiet ? 0 : BuildFrom + (1 - BuildFrom) * (float)Math.Clamp((now - PaceSince) / BuildFor, 0, 1);

    /// <summary>When the next stranger may come (story time).</summary>
    public double StrangerAt => _strangerAt;

    private void ClearPacing()
    {
        Pace = DramaPace.Building;
        PaceSince = _tensionAt = _strangerAt = _busyAt = _quietFrom = 0;
        _tension = PeakTension = 0;
        Quiets = Strangers = 0;
        QuietTotal = 0;
        _paceSeen = 0;
        _open = -1;
        _fresh = true;
    }

    private void AddTension(float amount, double now)
    {
        _tension = Tension(now) + amount;
        _tensionAt = now;
        PeakTension = Math.Max(PeakTension, _tension);
    }

    /// <summary>An event's share of the tension (it involves a person), and the stories it told.</summary>
    private void PaceEvent(in StoryEvent ev)
    {
        var rules = _engine.Rules;
        var agents = _engine.Agents;
        var interest = rules.People.NewsInterest;
        if (ev.Verb != rules.StoryVerb && (uint)ev.Verb < (uint)interest.Length && interest[ev.Verb] > 0 && (agents.Of(ev.Actor) != null || agents.Of(ev.Target) != null))
            AddTension(interest[ev.Verb] / 100f, ev.At);
        var sifter = _engine.Sifter;
        if (sifter.NoteTotal - _paceSeen > StorySifter.NoteRing)
            _paceSeen = sifter.NoteTotal - StorySifter.NoteRing;
        for (; _paceSeen < sifter.NoteTotal; _paceSeen++)
        {
            ref readonly var n = ref sifter.Notes[_paceSeen % StorySifter.NoteRing];
            if (n.Kind != SiftKind.Completed)
                continue;
            var p = rules.Patterns[n.Pattern];
            var people = false;
            for (var v = 0; v < p.Vars.Length && !people; v++)
                people = agents.Of(n.Binds[v]) != null;
            if (people)
                AddTension(p.Drama * StoryTension * p.Stages.Length, ev.At);
        }
    }

    /// <summary>A story waiting for anyone got nobody: the best such is the one a stranger may come for.</summary>
    private void OpenStory(in DramaCandidate c, ushort verb)
    {
        if (!_strangerVerbs[verb] || _open >= 0 && c.Score <= _openScore)
            return;
        (_open, _openStarted, _openScore, _openVerb) = (c.Match, c.Started, c.Score, verb);
    }

    /// <summary>The rank's pacing: the stretch moves on (building to quiet past the peak, quiet to building once calm).</summary>
    private void PaceRank(double now)
    {
        if (_fresh)
        {
            // (a new world or a load: building from now, the first stranger no sooner than half the gap)
            (PaceSince, _tensionAt, _busyAt, _strangerAt, _fresh) = (now, now, now, now + StrangerEvery / 2, false);
        }
        if (!Pacing)
        {
            if (Pace == DramaPace.Quiet)
                (Pace, PaceSince) = (DramaPace.Building, now);
            return;
        }
        var t = Tension(now);
        if (Pace == DramaPace.Building && t >= Peak && now - PaceSince >= BuildFor)
        {
            (Pace, PaceSince, _quietFrom) = (DramaPace.Quiet, now, QuietTotal);
            Quiets++;
            Note(DramaKind.Quiet, new StageDirection { Agent = -1 }, now);
        }
        else if (Pace == DramaPace.Quiet && (t <= Calm && now - PaceSince >= MinQuiet || now - PaceSince >= MaxQuiet))
        {
            QuietTotal = _quietFrom + (now - PaceSince);
            (Pace, PaceSince, _busyAt) = (DramaPace.Building, now, now);
            Note(DramaKind.Building, new StageDirection { Agent = -1 }, now);
        }
        if (Pace == DramaPace.Quiet)
        {
            QuietTotal = _quietFrom + (now - PaceSince);
            _busyAt = now;
        }
    }

    /// <summary>After a rank: a stranger may come, for the story nobody here takes up, or out of a long lull.</summary>
    private void MaybeStranger(double now)
    {
        if (CandidateCount > 0)
            _busyAt = now;
        if (!Pacing || !On || Pace != DramaPace.Building || now < _strangerAt)
            return;
        var sifter = _engine.Sifter;
        var forStory = _open >= 0 && sifter.Pool[_open].Live && sifter.Pool[_open].Started == _openStarted;
        if (!forStory && (CandidateCount > 0 || LiveCount > 0 || now - _busyAt < LullFor))
            return;
        _strangerAt = now + StrangerEvery; // (whether one comes or not: the pool is looked at no more often than this)
        var place = -1;
        if (forStory)
        {
            // Where the one the story needs them to meet is now; else where its last event happened.
            ref readonly var m = ref sifter.Pool[_open];
            var next = _engine.Rules.Patterns[m.Pattern].Stages[m.Stage];
            var target = Bound(m, next, StoryRole.Target);
            place = target >= 0 ? _engine.Agents.Where(target) : -1;
            if (place < 0)
            {
                var idx = _engine.Chronicle.IndexOf(m.Events[m.Stage - 1]);
                place = idx >= 0 ? _engine.Chronicle[idx].Place : -1;
            }
        }
        else
            place = Busiest();
        var (from, minutes) = Road(place);
        if (from < 0)
            return;
        var st = PickStranger(forStory ? _openVerb : (ushort)ushort.MaxValue);
        if (st == null || _engine.Agents.AddStranger(st, place, from, minutes) is not { } a)
            return;
        Strangers++;
        var d = new StageDirection { Agent = a.Entity, Target = place, Item = from, Pattern = ushort.MaxValue, Match = -1 };
        if (forStory)
        {
            ref readonly var m = ref sifter.Pool[_open];
            (d.Pattern, d.Stage, d.Match, d.MatchStarted, d.Verb, d.Binds) = (m.Pattern, m.Stage, _open, m.Started, _openVerb, m.Binds);
            for (var k = 0; k < m.Stage; k++)
            {
                var i = _engine.Chronicle.IndexOf(m.Events[k]);
                if (i < 0)
                    continue;
                ref readonly var ev = ref _engine.Chronicle[i];
                _engine.Agents.Learn(a, ev.Id, ev.Verb, ev.Actor, ev.Target, ev.Owner, ev.Place, ev.Item, KnowSource.Road, -1);
            }
        }
        Note(DramaKind.Stranger, d, now);
        _engine.Agents.Trace?.Invoke(a, forStory ? $"comes for {_engine.Rules.Patterns[d.Pattern].Name}, having heard of it on the road" : "comes into a quiet world");
    }

    /// <summary>The place with the most living people (a lull's stranger comes where they'll be met), or -1.</summary>
    private int Busiest()
    {
        int best = -1, bestCount = 0;
        var agents = _engine.Agents;
        foreach (var a in agents.All)
        {
            if (a.Dead || agents.Where(a.Entity) is not (var here and >= 0))
                continue;
            var n = 0;
            foreach (var b in agents.All)
                if (!b.Dead && agents.Where(b.Entity) == here)
                    n++;
            if (n > bestCount)
                (best, bestCount) = (here, n);
        }
        return best;
    }

    /// <summary>The longest road into a place (a stranger comes from furthest off): where from, and its minutes.</summary>
    private (int From, int Minutes) Road(int place)
    {
        var w = _engine.World;
        var rel = _engine.Rules.People.Road;
        if (rel < 0 || !w.Valid(place) || w[place].Kind != StoryKind.Place)
            return (-1, 0);
        int from = -1, minutes = 0;
        var (s, e) = w.Range(place, rel); // (roads go both ways)
        for (var i = s; i < e; i++)
            if (w[place].Pairs[i].Value > minutes)
                (from, minutes) = (w[place].Pairs[i].Target, w[place].Pairs[i].Value);
        return (from, minutes);
    }

    /// <summary>
    /// A stranger who hasn't come yet, from a cast whose people are here, who suits the verb (any verb when
    /// <paramref name="verb"/> is none; a lull prefers one with history here). The start is the people's seed, so runs differ.
    /// </summary>
    private CastDef.Stranger? PickStranger(ushort verb)
    {
        var w = _engine.World;
        var agents = _engine.Agents;
        CastDef.Stranger? best = null;
        var bestRank = -1;
        foreach (var cast in _engine.Rules.People.Casts)
        {
            if (cast.Strangers.Length == 0 || !Array.Exists(cast.People, pe => agents.Of(w.Find(pe.Name)) != null))
                continue;
            var start = (int)((agents.Seed >> 33) % (ulong)cast.Strangers.Length);
            for (var k = 0; k < cast.Strangers.Length; k++)
            {
                var st = cast.Strangers[(start + k) % cast.Strangers.Length];
                if (w.Find(st.Name) >= 0)
                    continue; // (came already)
                var suits = verb == ushort.MaxValue || st.Suits.Length == 0 || Array.IndexOf(st.Suits, verb) >= 0;
                if (!suits)
                    continue;
                var rank = verb == ushort.MaxValue ? (st.Feelings.Length > 0 ? 2 : 1) : (st.Suits.Length > 0 ? 2 : 1);
                if (rank > bestRank)
                    (best, bestRank) = (st, rank);
            }
        }
        return best;
    }
}
