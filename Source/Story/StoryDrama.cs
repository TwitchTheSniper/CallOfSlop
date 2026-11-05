using System.Diagnostics;

namespace CallOfSlop.Story;

/// <summary>
/// A stage direction (box 3): one person is nudged towards the verb a story in progress waits for, against one target,
/// until the story's next stage would run out. Their goals that can lead to that verb score (1 + Bonus) times as much, and
/// news that fits it is more worth telling. A nudge, never an order: a goal its person wouldn't want stays at nothing.
/// </summary>
public struct StageDirection
{
    public bool Live;
    public int Agent; // the person's entity
    public ushort Verb;
    public int Target, Item, Owner; // who the stage needs (-1: anyone)
    public float Bonus;
    public float Rank; // its story's drama score when last ranked
    public double Given, Until;
    public int Match; // the sifter's pool slot of the story it is for
    public double MatchStarted; // (that slot's match, not a later one in the same slot)
    public ushort Pattern;
    public byte Stage; // the stage it waits for (stages done, as the match had when given)
    public Ints8 Binds; // the match's, when given
    public bool Followed; // they did it while directed
    public bool MovedOn; // someone else moved the story on (it needs no nudge now)
}

public enum DramaKind : byte
{
    Given,
    Followed, // the person did the verb while directed
    MovedOn, // someone else did what the story waited for
    Lapsed, // it went unfollowed: the story ran out or ended, or a better one took the person
    Quiet, // a quiet stretch began (pacing: the tension passed its peak)
    Building, // it ended: the nudges build up again
    Stranger, // a stranger set off this way (Agent; Target the place, Item where from; Pattern the story, or none for a lull)
}

/// <summary>What the drama manager did, for the window and the CLI.</summary>
public struct DramaNote
{
    public double At;
    public DramaKind Kind;
    public StageDirection Direction;
}

/// <summary>Why a story in progress got no direction, counted each rank (the window).</summary>
public enum DramaSkip : byte
{
    LeftAlone, // the pattern or its next stage says no drama
    NotForPeople, // people can't take the verb it waits for (a kill: phase 4's bodies)
    Player, // a player acts next: nobody nudges players
    NobodyKnows, // anyone may act next, but no person knows what happened
    Unwilling, // the one who would act has no goal that would take it now (no grudge, not here, not their nature)
    Quiet, // it could be nudged, but a quiet stretch is on (pacing)
    Count,
}

/// <summary>A story in progress as the drama manager ranked it, with the person it would nudge.</summary>
public struct DramaCandidate
{
    public int Match, Agent;
    public double Started; // the match's (its slot may hold another later)
    public ushort Pattern;
    public byte Stage;
    public float Score, Drama, Progress, Novelty, Players, Urgency, Would, Keep;
}

/// <summary>
/// The drama manager (box 3; Awash's Influence Mode, docs/story/STORY.md "Drama"). It ranks the sifter's live partial
/// matches by how much they would add (the pattern's drama, how far along, how new, whether players are in it, how
/// pressing) and gives the best few a stage direction: the person who would act next is nudged towards the verb the story
/// waits for. It runs once a second at most, when an event came in or every half minute, never per tick, and allocates
/// nothing once made. Directions aren't saved: they are worked out again from the live matches after a load.
/// </summary>
public sealed partial class StoryDrama
{
    public const int MaxDirections = 8, MaxCandidates = 1024, NoteRing = 128, PerMatch = 2; // (people nudged for one story at most)
    public const double RankEvery = 30, MaxFor = 3600; // s: a story with no time limit is nudged for an hour at a time
    public const float KeepBonus = 1.25f; // a direction already given ranks this much higher (so they don't flit between stories)
    public const float Ready = 0.5f; // a goal score that counts as wanting to (goals score 0 to their weight, about 1)

    private readonly StoryEngine _engine;
    private readonly bool[][] _reaches; // per goal, per verb: can its plans take that verb

    /// <summary>Off: people go their own way (Awash's Baseline Mode, for comparing).</summary>
    public bool On = true;

    /// <summary>The most a direction adds to a goal's score (0.6: up to 1.6 times), the best ranked story getting all of it.</summary>
    public float Strength = 0.6f;

    public readonly StageDirection[] Directions = new StageDirection[MaxDirections];
    public int LiveCount { get; private set; }

    public readonly DramaCandidate[] Candidates = new DramaCandidate[MaxCandidates];
    public int CandidateCount { get; private set; }
    public readonly int[] Skipped = new int[(int)DramaSkip.Count];

    public readonly DramaNote[] Notes = new DramaNote[NoteRing];
    public long NoteTotal;

    public long Given, Followed, MovedOn, Lapsed, Ranks;
    // (pacing, quiet stretches and strangers: StoryDramaPacing.cs)
    public double LastRankMs, WorstRankMs, TotalRankMs;

    private bool _dirty = true;
    private double _rankAt;
    private long _siftSeen;
    private readonly StageDirection[] _was = new StageDirection[MaxDirections];

    public StoryDrama(StoryEngine engine)
    {
        _engine = engine;
        var p = engine.Rules.People;
        var verbs = engine.Rules.Verbs.Length;
        _reaches = new bool[p.Goals.Length][];
        for (var g = 0; g < p.Goals.Length; g++)
        {
            _reaches[g] = new bool[verbs];
            Reach(p, p.Goals[g].Do, _reaches[g], new bool[p.Tasks.Length]);
        }
        MakePacing();
    }

    /// <summary>Every verb a call can come to through its task's methods.</summary>
    private static void Reach(PeopleRules p, CallDef call, bool[] verbs, bool[] seen)
    {
        if (call.Action)
        {
            if ((uint)call.Index < (uint)p.Actions.Length)
                verbs[p.Actions[call.Index].Verb] = true;
            return;
        }
        if ((uint)call.Index >= (uint)p.Tasks.Length || seen[call.Index])
            return;
        seen[call.Index] = true;
        foreach (var m in p.Tasks[call.Index].Methods)
            foreach (var c in m.Do)
                Reach(p, c, verbs, seen);
    }

    /// <summary>Can this goal's plans take this verb (the window lists them)?</summary>
    public bool Reaches(int goal, int verb) => (uint)goal < (uint)_reaches.Length && (uint)verb < (uint)_reaches[goal].Length && _reaches[goal][verb];

    public void Clear()
    {
        Array.Clear(Directions);
        LiveCount = 0;
        CandidateCount = 0;
        Array.Clear(Skipped);
        NoteTotal = 0;
        Given = Followed = MovedOn = Lapsed = Ranks = 0;
        _siftSeen = 0;
        LastRankMs = WorstRankMs = TotalRankMs = 0;
        _dirty = true;
        _rankAt = 0;
        ClearPacing();
        foreach (var a in _engine.Agents.All)
            a.Directed = -1;
    }

    /// <summary>Something changed (an event, a load, a cast added): rank again at the next chance.</summary>
    public void Touch() => _dirty = true;

    /// <summary>The once-a-second sweep: ranks again when something changed or half a minute went by.</summary>
    public void Update(double now)
    {
        if (!_dirty && now < _rankAt)
            return;
        if (_engine.Agents.Count == 0 && LiveCount == 0)
        {
            _dirty = false; // (nobody to nudge: players' stories are theirs)
            return;
        }
        Rank(now);
    }

    // ---- For the people ---------------------------------------------------------------------------------------------

    /// <summary>A person's direction now, or -1.</summary>
    public int Of(StoryAgent a, double now)
    {
        var i = a.Directed;
        if ((uint)i >= MaxDirections)
            return -1;
        ref readonly var d = ref Directions[i];
        return d.Live && d.Agent == a.Entity && d.Until > now ? i : -1;
    }

    /// <summary>How much more a goal is worth to a person for this target (1: no direction for it).</summary>
    public float GoalFactor(StoryAgent a, int goal, int target, double now)
    {
        var i = Of(a, now);
        if (i < 0)
            return 1;
        ref readonly var d = ref Directions[i];
        if (!_reaches[goal][d.Verb] || d.Target >= 0 && d.Target != target)
            return 1;
        return 1 + d.Bonus;
    }

    /// <summary>How much more a piece of news is worth telling this listener (1: no direction for it).</summary>
    public float NewsFactor(StoryAgent a, in KnownItem it, int listener, double now)
    {
        var i = Of(a, now);
        if (i < 0)
            return 1;
        ref readonly var d = ref Directions[i];
        if (!_engine.Rules.People.ActionIsNews(d.Verb) || d.Target >= 0 && d.Target != listener)
            return 1;
        if (d.Item >= 0 && it.Actor != d.Item || d.Owner >= 0 && it.Target != d.Owner)
            return 1;
        return 1 + d.Bonus;
    }

    // ---- Events -----------------------------------------------------------------------------------------------------

    /// <summary>An event happened: a directed person who did what they were nudged to has followed it.</summary>
    public void OnEvent(in StoryEvent ev)
    {
        _dirty = true;
        PaceEvent(ev);
        if (LiveCount == 0)
            return;
        for (var i = 0; i < Directions.Length; i++)
        {
            ref var d = ref Directions[i];
            if (!d.Live || d.Followed || ev.Actor != d.Agent || ev.Verb != d.Verb || d.Target >= 0 && ev.Target != d.Target)
                continue;
            d.Followed = true;
            Followed++;
            Note(DramaKind.Followed, d, ev.At);
        }
        // Stories this event moved on: a direction for that stage is served, whoever did it.
        var sifter = _engine.Sifter;
        if (sifter.NoteTotal - _siftSeen > StorySifter.NoteRing)
            _siftSeen = sifter.NoteTotal - StorySifter.NoteRing;
        for (; _siftSeen < sifter.NoteTotal; _siftSeen++)
        {
            ref readonly var n = ref sifter.Notes[_siftSeen % StorySifter.NoteRing];
            if (n.Kind is not (SiftKind.Advanced or SiftKind.Completed))
                continue;
            for (var i = 0; i < Directions.Length; i++)
            {
                ref var d = ref Directions[i];
                if (d.Live && !d.Followed && !d.MovedOn && d.Pattern == n.Pattern && n.Stage == d.Stage + 1 && Agree(d.Binds, n.Binds))
                    d.MovedOn = true;
            }
        }
    }

    /// <summary>Every variable bound in <paramref name="was"/> is the same in <paramref name="now"/>.</summary>
    private static bool Agree(in Ints8 was, in Ints8 now)
    {
        for (var v = 0; v < StoryRules.MaxVars; v++)
            if (was[v] >= 0 && was[v] != now[v])
                return false;
        return true;
    }

    private void Note(DramaKind kind, in StageDirection d, double at)
    {
        ref var n = ref Notes[NoteTotal++ % NoteRing];
        n.At = at;
        n.Kind = kind;
        n.Direction = d;
    }

    // ---- Ranking ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ranks every live match the people could move on and gives the best <see cref="MaxDirections"/> a direction, one
    /// person each. Directions that still hold keep their start; the rest lapse (or were followed).
    /// </summary>
    public void Rank(double now)
    {
        var clock = Stopwatch.GetTimestamp();
        _dirty = false;
        _rankAt = now + RankEvery;
        Ranks++;
        CandidateCount = 0;
        Array.Clear(Skipped);
        var agents = _engine.Agents;
        _open = -1;
        PaceRank(now);
        if (On && agents.Count > 0)
            Collect(now); // (before the directions are cleared: a person's direction held counts)
        var cands = Candidates.AsSpan(0, CandidateCount);
        var build = BuildUp(now);
        if (build <= 0)
        {
            Skipped[(int)DramaSkip.Quiet] = CandidateCount; // (a quiet stretch: ranked for the window, nobody nudged)
            cands = default;
        }
        cands.Sort(static (x, y) => y.Score.CompareTo(x.Score));

        Array.Copy(Directions, _was, MaxDirections);
        Array.Clear(Directions);
        var count = 0;
        var top = cands.Length > 0 ? cands[0].Score : 1;
        foreach (ref readonly var c in cands)
        {
            if (count >= MaxDirections)
                break;
            var taken = false;
            for (var k = 0; k < count && !taken; k++)
                taken = Directions[k].Agent == c.Agent;
            if (taken)
                continue;
            ref readonly var m = ref _engine.Sifter.Pool[c.Match];
            var p = _engine.Rules.Patterns[m.Pattern];
            var next = p.Stages[m.Stage];
            ref var d = ref Directions[count++];
            d = new StageDirection
            {
                Live = true, Agent = c.Agent, Verb = next.Verb,
                Target = Bound(m, next, StoryRole.Target), Item = Bound(m, next, StoryRole.Item), Owner = Bound(m, next, StoryRole.Owner),
                Bonus = Strength * build * (0.5f + 0.5f * c.Score / top), Rank = c.Score,
                Given = now, Until = next.Within > 0 ? m.Last + next.Within : now + MaxFor,
                Match = c.Match, MatchStarted = m.Started, Pattern = m.Pattern, Stage = m.Stage, Binds = m.Binds,
            };
            // The same direction as before carries on (its start, whether it was followed, its hour for an open story).
            for (var k = 0; k < MaxDirections; k++)
            {
                ref var w = ref _was[k];
                if (w.Live && w.Agent == d.Agent && w.Match == d.Match && w.MatchStarted == d.MatchStarted && w.Stage == d.Stage)
                {
                    (d.Given, d.Followed) = (w.Given, w.Followed);
                    if (next.Within <= 0)
                        d.Until = w.Until;
                    w.Live = false;
                    break;
                }
            }
            if (d.Given == now)
            {
                Given++;
                Note(DramaKind.Given, d, now);
                agents.Trace?.Invoke(agents.Of(d.Agent)!, $"nudged: {Words(d)}");
            }
        }
        LiveCount = count;
        foreach (ref readonly var w in _was.AsSpan())
            if (!w.Live || w.Followed)
                continue; // (followed: counted when it was)
            else if (w.MovedOn)
            {
                MovedOn++;
                Note(DramaKind.MovedOn, w, now);
            }
            else
            {
                Lapsed++;
                Note(DramaKind.Lapsed, w, now);
            }
        foreach (var a in agents.All)
            a.Directed = -1;
        for (var k = 0; k < count; k++)
            if (agents.Of(Directions[k].Agent) is { } a)
                a.Directed = k;
        MaybeStranger(now);
        LastRankMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        WorstRankMs = Math.Max(WorstRankMs, LastRankMs);
        TotalRankMs += LastRankMs;
    }

    private static int Bound(in StoryMatch m, PatternStep step, StoryRole role) =>
        step.Roles[(int)role] is var v and >= 0 ? m.Binds[v] : -1;

    /// <summary>Every live match a person could move on next, scored.</summary>
    private void Collect(double now)
    {
        var rules = _engine.Rules;
        var people = rules.People;
        var sifter = _engine.Sifter;
        var agents = _engine.Agents;
        var world = _engine.World;
        var pool = sifter.Pool;
        for (var i = 0; i < pool.Length; i++)
        {
            ref readonly var m = ref pool[i];
            if (!m.Live)
                continue;
            var p = rules.Patterns[m.Pattern];
            var next = p.Stages[m.Stage];
            if (p.Drama <= 0 || next.Nudge <= 0)
            {
                Skipped[(int)DramaSkip.LeftAlone]++;
                continue;
            }
            if (next.Verb >= people.ActionOfVerb.Length || people.ActionOfVerb[next.Verb] < 0)
            {
                Skipped[(int)DramaSkip.NotForPeople]++;
                continue;
            }
            var left = next.Within > 0 ? next.Within - (now - m.Last) : 0;
            if (next.Within > 0 && left <= 0)
                continue; // (the sweep ends it)

            // How much it would add.
            var c = new DramaCandidate { Match = i, Started = m.Started, Pattern = m.Pattern, Stage = m.Stage, Drama = p.Drama * next.Nudge };
            c.Progress = 0.5f + 0.5f * Math.Min(1, m.Stage / Math.Max(1f, p.Stages.Length - 1));
            c.Novelty = 1 / (1 + 0.25f * sifter.Completed[m.Pattern]);
            c.Players = 1;
            for (var v = 0; v < p.Vars.Length; v++)
                if (world.Valid(m.Binds[v]) && world[m.Binds[v]].Kind == StoryKind.Player)
                    c.Players = 1.5f;
            c.Urgency = next.Within > 0 ? 0.8f + 0.4f * (float)Math.Clamp(1 - left / next.Within, 0, 1) : 1;
            c.Score = c.Drama * c.Progress * c.Novelty * c.Players * c.Urgency;

            // Who would act next: the one bound, or (anyone) people who know of the story's last event.
            var target = Bound(m, next, StoryRole.Target);
            var actor = Bound(m, next, StoryRole.Actor);
            if (actor >= 0)
            {
                if (agents.Of(actor) is not { } a)
                {
                    Skipped[(int)DramaSkip.Player]++;
                    continue;
                }
                if (!Add(ref c, a, i, next.Verb, target, now))
                    Skipped[(int)DramaSkip.Unwilling]++;
                continue;
            }
            var last = m.Events[m.Stage - 1];
            int knew = 0, found = 0;
            foreach (var a in agents.All)
            {
                if (found >= PerMatch)
                    break;
                if (IsBound(m, p, a.Entity) || !agents.Knows(a, last))
                    continue;
                knew++;
                if (Add(ref c, a, i, next.Verb, target, now))
                    found++;
            }
            if (found == 0)
            {
                Skipped[(int)(knew == 0 ? DramaSkip.NobodyKnows : DramaSkip.Unwilling)]++;
                OpenStory(c, next.Verb); // (a stranger may come for it)
            }
        }
    }

    /// <summary>
    /// A candidate for this person, when they would take the verb now: ranked higher the readier they are (their best goal
    /// that takes it: half again at <see cref="Ready"/> or more), and when they hold the direction already.
    /// </summary>
    private bool Add(ref DramaCandidate c, StoryAgent a, int match, ushort verb, int target, double now)
    {
        if (a.Dead)
            return false; // (dead for good: nothing more of theirs to nudge)
        var would = _engine.Agents.Would(a, verb, target, now);
        if (would <= 0 || CandidateCount >= MaxCandidates)
            return false;
        c.Agent = a.Entity;
        c.Would = 0.5f + 0.5f * Math.Min(1, would / Ready);
        c.Keep = 1;
        var held = Of(a, now);
        if (held >= 0 && Directions[held].Match == match && Directions[held].MatchStarted == _engine.Sifter.Pool[match].Started)
            c.Keep = KeepBonus;
        var score = c.Score;
        c.Score *= c.Would * c.Keep;
        Candidates[CandidateCount++] = c;
        c.Score = score;
        return true;
    }

    private static bool IsBound(in StoryMatch m, PatternDef p, int entity)
    {
        for (var v = 0; v < p.Vars.Length; v++)
            if (m.Binds[v] == entity)
                return true;
        return false;
    }

    // ---- Words ------------------------------------------------------------------------------------------------------

    /// <summary>"Tell Lena (of Rook and Mara) for Thief found out 1/2, x1.45".</summary>
    public string Words(in StageDirection d)
    {
        var w = _engine.World;
        var rules = _engine.Rules;
        var p = rules.Patterns[d.Pattern];
        var s = rules.Verbs[d.Verb].Name;
        s += d.Target >= 0 ? $" {w.NameOf(d.Target)}" : " anyone";
        if (d.Item >= 0 || d.Owner >= 0)
            s += $" (of {(d.Item >= 0 ? w.NameOf(d.Item) : "anyone")} and {(d.Owner >= 0 ? w.NameOf(d.Owner) : "anyone")})";
        return $"{s} for {p.Name} {d.Stage}/{p.Stages.Length}, x{1 + d.Bonus:0.00}";
    }

    /// <summary>A note as a line ("> Mara: Tell Lena ... (given)").</summary>
    public string NoteLine(in DramaNote n) => n.Kind switch
    {
        DramaKind.Quiet or DramaKind.Building or DramaKind.Stranger => $"* {PaceWords(n)}",
        _ => $"{n.Kind switch { DramaKind.Given => "!", DramaKind.Followed => "=", DramaKind.MovedOn => "~", _ => "-" }} {_engine.World.NameOf(n.Direction.Agent)}: {Words(n.Direction)} ({n.Kind.ToString().ToLowerInvariant()})",
    };

    /// <summary>A pacing note in words: "a quiet stretch begins", "Wren Halloway sets off from The Depot for Landing (for Thief found out)".</summary>
    public string PaceWords(in DramaNote n)
    {
        var d = n.Direction;
        var w = _engine.World;
        return n.Kind switch
        {
            DramaKind.Quiet => "a quiet stretch begins: no nudges or strangers until things settle",
            DramaKind.Building => "the quiet stretch is over: the nudges build up again",
            _ => $"{w.NameOf(d.Agent)} sets off from {w.NameOf(d.Item)} for {w.NameOf(d.Target)} ({(d.Pattern < _engine.Rules.Patterns.Length ? $"for {_engine.Rules.Patterns[d.Pattern].Name}, heard of on the road" : "a quiet spell: someone new")})",
        };
    }
}
