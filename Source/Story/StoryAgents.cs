using System.Diagnostics;

namespace CallOfSlop.Story;

/// <summary>How a person came to know something: they saw it, were told, did it themselves, heard it on the radio, or (a
/// stranger) heard it on the road before they came.</summary>
public enum KnowSource : byte
{
    Seen,
    Told,
    Did,
    Radio,
    Road,
}

/// <summary>A piece of news a person holds: the event (its id, and enough of it to work with once it has folded away).</summary>
public struct KnownItem
{
    public int Event;
    public ushort Verb;
    public int Actor, Target, Owner, Place;
    public KnowSource Source;
    public int From; // who told them (-1: nobody)
    public float Interest; // 0..100 when learnt; it halves every NewsHalfLife
    public double At;
}

/// <summary>A step of a plan: an action and its roles (the actor is the person). <see cref="Node"/>: its line in the plan tree.</summary>
public struct PlanStep
{
    public short Action;
    public int Target, Owner, Place, Item;
    public byte Node;
}

/// <summary>A line of the plan tree the window shows: a task with the method taken, or an action, at a depth.</summary>
public struct PlanNode
{
    public byte Depth;
    public bool Action;
    public short Index, Method;
    public int A, B; // its first two arguments (a task's parameters, an action's target and place)
}

/// <summary>Why a person isn't doing what they meant to (the window says it).</summary>
public enum AgentWhy : byte
{
    None,
    NoPlan, // no method of the goal's task worked out
    NoLonger, // a step's condition didn't hold when it was due
    TheyLeft, // the other one wasn't there at the end
    Away, // the other one is on the road
    NoNews, // nothing to tell them
    Idle, // nothing worth doing
    Unreached, // their bodies never met (the game's bodies: one couldn't get to the other in time)
    Dead, // killed, or died: they do nothing more
    InPlay, // in a play (StoryPlays): they start nothing of their own until it is over
}

/// <summary>A goal that isn't tried again until <see cref="Until"/> (for that target; -1: for any).</summary>
public struct GoalRest
{
    public int Goal, Target;
    public double Until;
}

/// <summary>A step done (or not), for the window's list of what a person did lately.</summary>
public struct AgentDone
{
    public short Action;
    public int Target, Place;
    public double At;
    public bool Failed;
}

/// <summary>A person with a mind of their own (box 4): needs, a goal, a plan, what they're doing, what they know.</summary>
public sealed class StoryAgent
{
    public const int MaxKnown = 32, MaxSteps = 16, MaxNodes = 40, HistorySize = 16, MaxRests = 8;

    public int Entity;
    public bool Scripted;
    public float[] Need = []; // per PeopleRules.Needs, as of NeedAt
    public double NeedAt;
    public double WakeAt; // when they next think (the end of the step under way, or a wait)

    public int Goal = -1, GoalTarget = -1;
    public float GoalScore;
    public float[] Scores = []; // per goal, the last think
    public int[] ScoreTargets = [];
    public double[] GoalLast = []; // per goal: when it last ended (done, given up or put aside): a goal's "since"
    public readonly GoalRest[] Rests = new GoalRest[MaxRests]; // goals resting after a plan failed (for a target, or -1 any)

    public readonly PlanStep[] Plan = new PlanStep[MaxSteps];
    public int PlanCount, PlanNext;
    public readonly PlanNode[] Tree = new PlanNode[MaxNodes];
    public int TreeCount;

    public bool Busy;
    public PlanStep Doing;
    public double DoingFrom, DoingUntil;
    public int DoingFromPlace = -1; // a trip: where they set off from (they are on the road, in no place)

    public readonly KnownItem[] Known = new KnownItem[MaxKnown];
    public int KnownCount;

    public readonly AgentDone[] History = new AgentDone[HistorySize];
    public int HistoryNext, HistoryCount;

    public AgentWhy Why;
    public int WhyIndex = -1, WhyTarget = -1; // the goal or action, and who
    public int Directed = -1; // the drama manager's direction for them (StoryDrama.Directions), -1 none; StoryDrama.Of checks it
    public bool Dead; // for good: they think no more, are in no place, and nobody picks them (saved as the Dead quality)
    public double DiedAt = -1;
    public bool Meeting; // the step's time is up but their bodies haven't met yet (the game's bodies; StoryAgents.Meet)
    public int Thinks, Plans, PlanFails, Steps, StepFails;
}

/// <summary>
/// The people in the story world (docs/story/STORY.md "People"; box 4 in the diagram). Each one thinks only when their
/// step under way ends or a wait runs out, a few per tick: needs fall with time, goals are scored by utility, the best is
/// planned with the HTN planner (<see cref="StoryHtn"/>) over the same verbs players use, and its steps are acted out on
/// the story clock, each recorded as an event (routine ones only change the world). What happens where they are, and what
/// they are told, they learn, and react to (Story/People reactions). No allocation per think once the arrays have grown.
/// </summary>
public sealed class StoryAgents
{
    public const double NewsHalfLife = 3600; // s: news half as interesting an hour of play later
    public const double IdleWait = 30;
    public const double NoPlanWait = 300; // s: a goal that found no plan isn't tried again (for that target) for this long
    public const float KeepBonus = 1.5f; // the goal under way counts this much more (people stick to what they started)
    public const float MinScore = 0.1f; // below this a goal isn't worth doing (they wait)
    public const int PickMax = 12;

    public readonly StoryEngine Engine;
    public readonly List<StoryAgent> All = new();
    private readonly Dictionary<int, int> _of = new(); // entity -> index in All
    private readonly StoryHtn _htn;
    private readonly int[] _pick = new int[PickMax];
    private int[][] _reactionsOf = [];

    /// <summary>The random state (jitter in scores, starting needs). Saved, so a loaded world doesn't draw the same again.</summary>
    public ulong Seed = 0x9E3779B97F4A7C15UL;

    /// <summary>How many people may think in one <see cref="Tick"/> (the game keeps it to a few; headless runs take all).</summary>
    public int PerTick = int.MaxValue;

    /// <summary>When set, each choice and failure as a line (--story-sim --trace). Nothing is made when it isn't.</summary>
    public Action<StoryAgent, string>? Trace;

    /// <summary>
    /// The game's bodies (GameStoryBodies.cs): are these two together, or is either without a body? A step with someone
    /// in mind doesn't end until their bodies have met (up to <see cref="MeetWait"/> past its time, then it fails). Null
    /// (headless): the story's own clock alone, as before.
    /// </summary>
    public Func<int, int, bool>? Meet;

    public const double MeetWait = 45, MeetCheck = 1; // s

    private int _next;
    private readonly int[] _aired = new int[MaxAired]; // the news the radio has put out lately (event ids), a ring
    private int _airedNext, _airedCount;
    public const int MaxAired = 64;
    public long Thinks, PlansMade, PlanFails, StepsDone, Deaths, MeetWaits, Unreached;
    public double ThinkMs, WorstThinkMs, LastThinkMs;

    public StoryAgents(StoryEngine engine)
    {
        Engine = engine;
        _htn = new StoryHtn(this);
    }

    public StoryRules Rules => Engine.Rules;
    public PeopleRules P => Engine.Rules.People;
    public StoryWorld W => Engine.World;
    public int Count => All.Count;

    public void Clear()
    {
        All.Clear();
        _of.Clear();
        Thinks = PlansMade = PlanFails = StepsDone = Deaths = MeetWaits = Unreached = 0;
        _airedNext = _airedCount = 0;
        ThinkMs = WorstThinkMs = LastThinkMs = 0;
        _next = 0;
    }

    /// <summary>The person for an entity, or null.</summary>
    public StoryAgent? Of(int entity) => _of.TryGetValue(entity, out var i) ? All[i] : null;

    /// <summary>Where an entity is (its In), or -1 (on the road, or not anywhere).</summary>
    public int Where(int entity)
    {
        var rel = P.In;
        if (rel < 0 || !W.Valid(entity))
            return -1;
        var (a, b) = W.Range(entity, rel);
        return b > a ? W[entity].Pairs[a].Target : -1;
    }

    private double Random01()
    {
        // xorshift64*: the same run from the same seed.
        var x = Seed;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        Seed = x;
        return ((x * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53));
    }

    // ---- Making people ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Puts a cast in the world: its places (one may be the host's world, <paramref name="worldPlace"/>; a place already
    /// there by name is used), roads, people at home, their qualities and feelings. People already there by name are left
    /// as they are. Returns how many people came.
    /// </summary>
    public int AddCast(CastDef cast, int worldPlace, bool scripted)
    {
        var now = Engine.Time;
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pl in cast.Places)
        {
            var id = pl.World && W.Valid(worldPlace) ? worldPlace : W.Find(pl.Name);
            if (id < 0 || W[id].Kind != StoryKind.Place)
                id = W.Create(StoryKind.Place, pl.Name, 0, now, scripted);
            ids[pl.Name] = id;
        }
        foreach (var r in cast.Roads)
            W.Set(ids[r.From], P.Road, ids[r.To], r.Minutes);
        var added = new List<int>();
        foreach (var pe in cast.People)
        {
            var id = W.Find(pe.Name);
            if (id >= 0 && W[id].Kind == StoryKind.Person && _of.ContainsKey(id))
            {
                ids[pe.Name] = id;
                continue;
            }
            id = W.Create(StoryKind.Person, pe.Name, 0, now, scripted);
            if (id < 0)
                break;
            ids[pe.Name] = id;
            var home = ids[pe.Home];
            W.Set(id, P.Home, home, 1);
            if (pe.Works.Length > 0)
                W.Set(id, P.WorksAt, ids[pe.Works], 1);
            W.Set(id, P.In, home, 1);
            foreach (var (q, v) in pe.Qualities)
                W.SetQuality(id, q, v);
            var a = NewAgent(id, scripted, now);
            for (var n = 0; n < a.Need.Length; n++)
                a.Need[n] = 45 + (float)(Random01() * 45);
            a.WakeAt = now + Random01() * 10;
            Mirror(a);
            added.Add(id);
        }
        // Those who share a home know each other a little; then the cast's own feelings.
        if (P.Knows >= 0)
            foreach (var a in added)
                foreach (var b in added)
                    if (a < b && W.Get(a, P.Home, Where(a)) > 0 && Where(a) == Where(b))
                        W.Set(a, P.Knows, b, 3);
        foreach (var f in cast.Feelings)
            if (ids.TryGetValue(f.Who, out var who) && ids.TryGetValue(f.To, out var to))
                W.Set(who, f.Rel, to, f.Value);
        Engine.Drama.Touch();
        return added.Count;
    }

    /// <summary>
    /// A stranger comes (the drama manager's pacing, docs/story/STORY.md "Pacing"): a new person, already on the road from
    /// <paramref name="from"/> to <paramref name="place"/> (<paramref name="minutes"/> long), who makes it their home. Their
    /// qualities and feelings are the data's; their needs start middling, Company low (new in town, they want to meet
    /// people). Returns the person, or null when the world is full or the name is taken.
    /// </summary>
    public StoryAgent? AddStranger(CastDef.Stranger st, int place, int from, int minutes)
    {
        var now = Engine.Time;
        var travel = Array.FindIndex(P.Actions, x => x.Road);
        if (travel < 0 || W.Find(st.Name) >= 0 || !W.Valid(place) || !W.Valid(from))
            return null;
        var id = W.Create(StoryKind.Person, st.Name, 0, now);
        if (id < 0)
            return null;
        W.Set(id, P.Home, place, 1);
        foreach (var (q, v) in st.Qualities)
            W.SetQuality(id, q, v);
        foreach (var f in st.Feelings)
            if (W.Find(f.To) is var to and >= 0 && Of(to) is { Dead: false })
                W.Set(id, f.Rel, to, f.Value);
        var a = NewAgent(id, false, now);
        for (var n = 0; n < a.Need.Length; n++)
            a.Need[n] = 50 + (float)(Random01() * 35);
        if (P.Need("Company") is var company and >= 0)
            a.Need[company] = 20 + (float)(Random01() * 15);
        var step = new PlanStep { Action = (short)travel, Target = -1, Owner = -1, Place = place, Item = -1 };
        a.Plan[0] = a.Doing = step;
        (a.PlanCount, a.PlanNext) = (1, 0);
        a.Busy = true;
        (a.DoingFrom, a.DoingUntil, a.DoingFromPlace) = (now, now + Math.Max(60, minutes * 60), from);
        a.WakeAt = a.DoingUntil;
        Mirror(a);
        Trace?.Invoke(a, $"a stranger, on the road from {W.NameOf(from)} to {W.NameOf(place)}");
        Engine.Drama.Touch();
        return a;
    }

    private StoryAgent NewAgent(int entity, bool scripted, double now)
    {
        var g = P.Goals.Length;
        var a = new StoryAgent
        {
            Entity = entity,
            Scripted = scripted,
            Need = new float[P.Needs.Length],
            NeedAt = now,
            Scores = new float[g],
            ScoreTargets = new int[g],
            GoalLast = Enumerable.Repeat(-1e9, g).ToArray(),
        };
        _of[entity] = All.Count;
        All.Add(a);
        return a;
    }

    // ---- Needs --------------------------------------------------------------------------------------------------------

    public float NeedNow(StoryAgent a, int need, double now) =>
        Math.Clamp(a.Need[need] - P.Needs[need].FallsPerSecond * (float)(now - a.NeedAt), 0, 100);

    private void Settle(StoryAgent a, double now)
    {
        for (var n = 0; n < a.Need.Length; n++)
            a.Need[n] = NeedNow(a, n, now);
        a.NeedAt = now;
    }

    /// <summary>Needs show as qualities (rounded), so queries, the graph and the window see them.</summary>
    private void Mirror(StoryAgent a)
    {
        for (var n = 0; n < a.Need.Length; n++)
            W.SetQuality(a.Entity, P.Needs[n].Quality, Math.Max(1, (int)MathF.Round(a.Need[n])));
    }

    // ---- The clock ----------------------------------------------------------------------------------------------------

    /// <summary>The story clock moved on: those whose step ended or whose wait ran out think, <see cref="PerTick"/> at most.</summary>
    public void Tick(double now)
    {
        var n = All.Count;
        if (n == 0)
            return;
        var budget = PerTick;
        for (var k = 0; k < n && budget > 0; k++)
        {
            var a = All[(_next + k) % n];
            if (a.WakeAt > now)
                continue;
            budget--;
            var clock = Stopwatch.GetTimestamp();
            Run(a, now);
            LastThinkMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
            ThinkMs += LastThinkMs;
            WorstThinkMs = Math.Max(WorstThinkMs, LastThinkMs);
            Thinks++;
        }
        _next = (_next + 1) % n;
    }

    private void Run(StoryAgent a, double now)
    {
        if (a.Busy && Apart(a) && now < a.DoingUntil + MeetWait)
        {
            if (!a.Meeting)
                MeetWaits++;
            a.Meeting = true;
            a.WakeAt = now + MeetCheck; // (their bodies are still on their way to each other)
            return;
        }
        a.Meeting = false;
        a.Thinks++;
        if (a.Busy)
        {
            Finish(a, now);
            if (a.PlanNext >= a.PlanCount)
                EndGoal(a, now); // (done)
        }
        Settle(a, now);
        Mirror(a);
        if (Engine.Plays.HeldBy(a.Entity) is var play and >= 0)
        {
            // In a play: the step they were on is done, but nothing new starts until the play is over.
            (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.InPlay, Engine.Plays.Running[play].Play, -1);
            a.WakeAt = now + StoryPlays.HoldCheck;
            return;
        }
        var have = Decide(a, now);
        for (var tries = 0; tries < 3 && have && !a.Busy; tries++)
        {
            if (Start(a, now))
                break;
            Drop(a, now, 20);
            have = Decide(a, now);
        }
        if (!a.Busy)
        {
            if (a.Why == AgentWhy.None)
                a.Why = AgentWhy.Idle;
            a.WakeAt = now + IdleWait * (0.75 + Random01() * 0.5);
        }
    }

    /// <summary>A step with someone in mind whose time is up while their two bodies are still apart (both in the same place).</summary>
    private bool Apart(StoryAgent a)
    {
        if (Meet == null || a.Doing.Target < 0 || P.Actions[a.Doing.Action].Road)
            return false;
        var here = Where(a.Entity);
        return here >= 0 && Where(a.Doing.Target) == here && !Meet(a.Entity, a.Doing.Target);
    }

    /// <summary>
    /// A person dies for good (a Kill of them, a Die of theirs; Karl, 2026-11-04): what they were doing stops, they leave
    /// their place for no other, they carry the Dead quality (so it is saved, and queries see it), and nobody picks them
    /// for a goal again, and the stories still waiting on them to act or be acted on end (by <paramref name="ev"/>, the
    /// event that killed them). Their knowledge and history stay for the window.
    /// </summary>
    public void Die(StoryAgent a, double now, int ev = -1)
    {
        if (a.Dead)
            return;
        a.Dead = true;
        a.DiedAt = now;
        a.Busy = a.Meeting = false;
        a.DoingFromPlace = -1;
        a.PlanCount = a.PlanNext = 0;
        EndGoal(a, now);
        (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.Dead, -1, -1);
        a.WakeAt = double.MaxValue;
        if (Where(a.Entity) is var here and >= 0)
            W.Set(a.Entity, P.In, here, 0);
        if (P.Dead >= 0)
            W.SetQuality(a.Entity, P.Dead, 1);
        Deaths++;
        Engine.Sifter.EndFor(a.Entity, ev, now);
        Trace?.Invoke(a, "died");
        Engine.Drama.Touch();
    }

    /// <summary>The plan is given up; its goal (for that target) rests a while.</summary>
    private void Drop(StoryAgent a, double now, double rest)
    {
        if (a.Goal >= 0)
            Rest(a, a.Goal, a.GoalTarget, now + rest);
        a.PlanCount = a.PlanNext = 0;
        EndGoal(a, now);
    }

    /// <summary>The goal is over (done, given up or put aside): "since" counts from now.</summary>
    private static void EndGoal(StoryAgent a, double now)
    {
        if (a.Goal >= 0)
            a.GoalLast[a.Goal] = now;
        a.Goal = a.GoalTarget = -1;
    }

    // ---- Goals --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Scores every goal; keeps the plan under way while its goal is still the best (it gets a 50% lead), otherwise plans
    /// for the best goal that plans (three tries). True when there is a plan with a step to take.
    /// </summary>
    private bool Decide(StoryAgent a, double now)
    {
        var goals = P.Goals;
        var here = Where(a.Entity);
        for (var g = 0; g < goals.Length; g++)
        {
            a.Scores[g] = Score(a, goals[g], here, now, out var t);
            a.ScoreTargets[g] = t;
        }
        var keeping = a.Goal >= 0 && a.PlanNext < a.PlanCount;
        if (keeping)
            a.Scores[a.Goal] *= KeepBonus;
        Span<bool> tried = stackalloc bool[goals.Length];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var best = -1;
            for (var g = 0; g < goals.Length; g++)
                if (!tried[g] && a.Scores[g] >= MinScore && (best < 0 || a.Scores[g] > a.Scores[best]))
                    best = g;
            if (best < 0)
                break;
            tried[best] = true;
            if (keeping && best == a.Goal && a.ScoreTargets[best] == a.GoalTarget)
                return true;
            a.Plans++;
            PlansMade++;
            var was = a.Goal;
            if (_htn.Plan(a, goals[best], a.ScoreTargets[best], now))
            {
                if (was >= 0 && was != best)
                    EndGoal(a, now); // (put aside for this one)
                a.Goal = best;
                a.GoalTarget = a.ScoreTargets[best];
                a.GoalScore = a.Scores[best];
                a.Why = AgentWhy.None;
                Trace?.Invoke(a, $"{goals[best].Name}{TargetText(a.GoalTarget)} ({a.GoalScore:0.00}): {PlanText(a)}");
                return true;
            }
            a.PlanFails++;
            PlanFails++;
            keeping = false; // (planning wrote over the plan under way: if its goal comes up next, it is planned again)
            Rest(a, best, a.ScoreTargets[best], now + NoPlanWait);
            (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.NoPlan, best, a.ScoreTargets[best]);
            Trace?.Invoke(a, WhyText(a));
        }
        a.PlanCount = a.PlanNext = 0;
        EndGoal(a, now);
        return false;
    }

    /// <summary>The goal rests (for that target) until then: in the slot it had, or a free one, or the one that ends first.</summary>
    private static void Rest(StoryAgent a, int goal, int target, double until)
    {
        var slot = 0;
        for (var i = 0; i < StoryAgent.MaxRests; i++)
        {
            ref readonly var r = ref a.Rests[i];
            if (r.Goal == goal && r.Target == target)
            {
                slot = i;
                break;
            }
            if (r.Until < a.Rests[slot].Until)
                slot = i;
        }
        a.Rests[slot] = new GoalRest { Goal = goal, Target = target, Until = until };
    }

    private static bool Resting(StoryAgent a, int goal, int target, double now)
    {
        foreach (ref readonly var r in a.Rests.AsSpan())
            if (r.Until > now && r.Goal == goal && (r.Target < 0 || r.Target == target))
                return true;
        return false;
    }

    /// <summary>A goal's utility now (0..weight), for its best target (<paramref name="target"/>, -1 when it has none).</summary>
    public float Score(StoryAgent a, GoalDef goal, int here, double now, out int target)
    {
        target = -1;
        var f = Ints8.Empty();
        f[GoalDef.Me] = a.Entity;
        f[GoalDef.Here] = here;
        if (goal.If != null && !goal.If.Any(W, f))
            return 0;
        float best;
        if (goal.Pick == null)
        {
            best = Resting(a, goal.Id, -1, now) ? 0 : Considered(a, goal, -1, here, now) * Engine.Drama.GoalFactor(a, goal.Id, -1, now);
        }
        else
        {
            var n = goal.Pick.Collect(W, f, GoalDef.Them, _pick);
            best = 0;
            for (var i = 0; i < n; i++)
            {
                var t = _pick[i];
                if (t == a.Entity || Resting(a, goal.Id, t, now) || Of(t) is { Dead: true })
                    continue;
                var s = Considered(a, goal, t, here, now) * Engine.Drama.GoalFactor(a, goal.Id, t, now); // (a stage direction's nudge)
                if (s > best)
                    (best, target) = (s, t);
            }
        }
        return best * (float)(0.92 + Random01() * 0.16); // (a little each way, so people differ)
    }

    /// <summary>
    /// The drama manager's question: what would doing this verb to that target (-1: anyone) be worth to them now, as the
    /// best of their goals whose plans can take it (no jitter, no direction)? 0: they wouldn't.
    /// </summary>
    public float Would(StoryAgent a, int verb, int target, double now)
    {
        var here = Where(a.Entity);
        var best = 0f;
        foreach (var goal in P.Goals)
        {
            if (goal.Pick == null || !Engine.Drama.Reaches(goal.Id, verb))
                continue; // (a goal with nobody in mind leaves the target to its plan: no telling who)
            var f = Ints8.Empty();
            f[GoalDef.Me] = a.Entity;
            f[GoalDef.Here] = here;
            if (goal.If != null && !goal.If.Any(W, f))
                continue;
            var n = goal.Pick.Collect(W, f, GoalDef.Them, _pick);
            for (var i = 0; i < n; i++)
            {
                var t = _pick[i];
                if (t == a.Entity || target >= 0 && t != target || Resting(a, goal.Id, t, now) || Of(t) is { Dead: true })
                    continue;
                best = Math.Max(best, Considered(a, goal, t, here, now));
            }
        }
        return best;
    }

    private float Considered(StoryAgent a, GoalDef goal, int t, int here, double now)
    {
        var v = goal.Weight;
        foreach (ref readonly var c in goal.Score.AsSpan())
        {
            v *= Factor(a, goal, c, t, here, now);
            if (v <= 0)
                return 0;
        }
        return v;
    }

    /// <summary>One consideration's factor, Min..1 (also the window's breakdown).</summary>
    public float Factor(StoryAgent a, GoalDef goal, in Consideration c, int t, int here, double now)
    {
        float raw = c.Input switch
        {
            NeedInput.Need => NeedNow(a, c.What, now),
            NeedInput.Quality => W.Quality(a.Entity, c.What),
            NeedInput.Their => t >= 0 ? W.Quality(t, c.What) : 0,
            NeedInput.Rel => t >= 0 ? W.Get(a.Entity, c.What, t) : 0,
            NeedInput.Held => t >= 0 ? W.Get(t, c.What, a.Entity) : 0,
            NeedInput.News => t >= 0 ? BestNews(a, t, now, out _) : 0,
            NeedInput.Here => t >= 0 && here >= 0 && Where(t) == here ? 1 : 0,
            NeedInput.Radio => BestRadioNews(a, now, out _),
            _ => (float)Math.Min(1e9, now - a.GoalLast[goal.Id]),
        };
        var x = Math.Clamp((raw - c.From) / (c.To - c.From), 0, 1);
        var y = c.Curve switch
        {
            NeedCurve.Inverse => 1 - x,
            NeedCurve.Square => x * x,
            NeedCurve.InverseSquare => (1 - x) * (1 - x),
            NeedCurve.Root => MathF.Sqrt(x),
            _ => x,
        };
        return c.Min + (1 - c.Min) * y;
    }

    // ---- Acting -------------------------------------------------------------------------------------------------------

    /// <summary>Starts the next step: its condition again (the world moved since it was planned), then it takes its time.</summary>
    private bool Start(StoryAgent a, double now)
    {
        ref readonly var st = ref a.Plan[a.PlanNext];
        var act = P.Actions[st.Action];
        var here = Where(a.Entity);
        var f = Ints8.Empty();
        (f[ActionDef.Me], f[ActionDef.Here], f[ActionDef.Target], f[ActionDef.Owner], f[ActionDef.Place], f[ActionDef.Item]) = (a.Entity, here, st.Target, st.Owner, st.Place, st.Item);
        if (act.If != null && !act.If.Any(W, f))
        {
            (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.NoLonger, st.Action, st.Target);
            Done(a, st, here, now, failed: true);
            return false;
        }
        var step = st;
        (step.Target, step.Owner, step.Place, step.Item) = (f[ActionDef.Target], f[ActionDef.Owner], f[ActionDef.Place], f[ActionDef.Item]);
        if (Of(step.Target) is { Busy: true, DoingFromPlace: >= 0 })
        {
            (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.Away, st.Action, st.Target);
            Done(a, st, here, now, failed: true);
            return false;
        }
        double takes;
        if (act.Road)
        {
            takes = (P.Road >= 0 && here >= 0 && step.Place >= 0 ? W.Get(here, P.Road, step.Place) : 0) * 60.0;
            if (takes <= 0)
            {
                (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.NoLonger, st.Action, step.Place);
                Done(a, st, here, now, failed: true);
                return false;
            }
            a.DoingFromPlace = here;
            W.Set(a.Entity, P.In, here, 0); // (on the road: in no place until they arrive)
        }
        else
        {
            takes = act.Takes;
            a.DoingFromPlace = -1;
            if (step.Place < 0)
                step.Place = here;
        }
        a.Doing = step;
        a.Busy = true;
        a.DoingFrom = now;
        a.DoingUntil = now + takes;
        a.WakeAt = a.DoingUntil;
        return true;
    }

    /// <summary>The step under way ends: it happens (an event, or quietly when routine) and refills needs; or it fails.</summary>
    private void Finish(StoryAgent a, double now)
    {
        a.Busy = false;
        var st = a.Doing;
        var act = P.Actions[st.Action];
        a.PlanNext++;
        var place = act.Road ? st.Place : Where(a.Entity);
        var ok = place >= 0;
        if (ok && !act.Road && st.Target >= 0 && Where(st.Target) != place)
        {
            ok = false;
            (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.TheyLeft, st.Action, st.Target);
        }
        else if (ok && Meet != null && !act.Road && st.Target >= 0 && !Meet(a.Entity, st.Target))
        {
            ok = false;
            Unreached++;
            (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.Unreached, st.Action, st.Target);
        }
        else if (ok && !act.Road && act.If != null)
        {
            // Its condition again at the end: the world went on meanwhile (they met as I greeted them, the debt was paid).
            var f = Ints8.Empty();
            (f[ActionDef.Me], f[ActionDef.Here], f[ActionDef.Target], f[ActionDef.Owner], f[ActionDef.Place], f[ActionDef.Item]) = (a.Entity, place, st.Target, st.Owner, st.Place, st.Item);
            if (!act.If.Any(W, f))
            {
                ok = false;
                (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.NoLonger, st.Action, st.Target);
            }
        }
        var value = 0;
        var item = act.Road ? a.DoingFromPlace : st.Item;
        var owner = st.Owner;
        if (ok && act.News)
        {
            var k = -1;
            if (act.Radio)
                BestRadioNews(a, now, out k);
            else
                BestNews(a, st.Target, now, out k);
            if (k < 0)
            {
                ok = false;
                (a.Why, a.WhyIndex, a.WhyTarget) = (AgentWhy.NoNews, st.Action, st.Target);
            }
            else
                (value, item, owner) = (a.Known[k].Event, a.Known[k].Actor, a.Known[k].Target); // (who did what to whom, for patterns)
        }
        if (!ok)
        {
            if (act.Road && a.DoingFromPlace >= 0)
                W.Set(a.Entity, P.In, a.DoingFromPlace, 1);
            a.DoingFromPlace = -1;
            Done(a, st, place, now, failed: true);
            Drop(a, now, 30);
            return;
        }
        a.DoingFromPlace = -1;
        if (act.Chronicle)
            Engine.Record(act.Verb, a.Entity, st.Target, owner, place, item, value, a.Scripted);
        else
        {
            Span<int> slots = [a.Entity, st.Target, owner, place, item];
            W.Apply(Rules.Verbs[act.Verb].Effects, slots);
        }
        if (act.Road && Where(a.Entity) != place)
            W.Set(a.Entity, P.In, place, 1); // (whatever the verb's effects say, a trip ends there)
        Settle(a, now);
        foreach (var (need, amount) in act.Refills)
            a.Need[need] = Math.Clamp(a.Need[need] + amount, 0, 100);
        Mirror(a);
        a.Steps++;
        StepsDone++;
        Done(a, st, place, now, failed: false);
    }

    /// <summary>
    /// A play's beat does one of the people's actions now (<see cref="StoryPlays"/>): its condition, then it happens as a
    /// step's end would (an event, or quietly when routine) and refills their needs. False: its condition didn't hold.
    /// </summary>
    public bool PlayStep(StoryAgent a, int action, int target, int place, double now)
    {
        var act = P.Actions[action];
        var f = Ints8.Empty();
        (f[ActionDef.Me], f[ActionDef.Here], f[ActionDef.Target], f[ActionDef.Place]) = (a.Entity, place, target, place);
        if (a.Dead || act.If != null && !act.If.Any(W, f))
            return false;
        var (owner, item) = (f[ActionDef.Owner], f[ActionDef.Item]);
        if (act.Chronicle)
            Engine.Record(act.Verb, a.Entity, target, owner, place, item, 0, a.Scripted);
        else
        {
            Span<int> slots = [a.Entity, target, owner, place, item];
            W.Apply(Rules.Verbs[act.Verb].Effects, slots);
        }
        Settle(a, now);
        foreach (var (need, amount) in act.Refills)
            a.Need[need] = Math.Clamp(a.Need[need] + amount, 0, 100);
        Mirror(a);
        a.Steps++;
        StepsDone++;
        Done(a, new PlanStep { Action = (short)action, Target = target, Owner = owner, Place = place, Item = item }, place, now, failed: false);
        return true;
    }

    private void Done(StoryAgent a, in PlanStep st, int place, double now, bool failed)
    {
        if (failed)
            Trace?.Invoke(a, WhyText(a));
        a.History[a.HistoryNext] = new AgentDone { Action = st.Action, Target = st.Target, Place = place, At = now, Failed = failed };
        a.HistoryNext = (a.HistoryNext + 1) % StoryAgent.HistorySize;
        a.HistoryCount = Math.Min(a.HistoryCount + 1, StoryAgent.HistorySize);
        if (failed)
            a.StepFails++;
    }

    // ---- Words (the window, the trace) ---------------------------------------------------------------------------------

    private string TargetText(int t) => t >= 0 ? $" ({W.NameOf(t)})" : "";

    /// <summary>A step as words: "Chat Rhea Lund", "Travel to Landing".</summary>
    public string StepText(in PlanStep st)
    {
        var act = P.Actions[st.Action];
        if (act.Road)
            return $"{act.Name} to {W.NameOf(st.Place)}";
        return st.Target >= 0 ? $"{act.Name} {W.NameOf(st.Target)}" : act.Name;
    }

    /// <summary>The plan's steps from the next one on, as words.</summary>
    public string PlanText(StoryAgent a) =>
        a.PlanNext >= a.PlanCount ? "nothing" : string.Join(", ", Enumerable.Range(a.PlanNext, a.PlanCount - a.PlanNext).Select(i => StepText(a.Plan[i])));

    /// <summary>Why they aren't doing what they meant to, as words.</summary>
    public string WhyText(StoryAgent a)
    {
        string Act() => (uint)a.WhyIndex < (uint)P.Actions.Length ? P.Actions[a.WhyIndex].Name : "?";
        return a.Why switch
        {
            AgentWhy.NoPlan => $"no plan for {((uint)a.WhyIndex < (uint)P.Goals.Length ? P.Goals[a.WhyIndex].Name : "?")}{TargetText(a.WhyTarget)}",
            AgentWhy.NoLonger => $"{Act()}{TargetText(a.WhyTarget)}: its condition no longer held",
            AgentWhy.TheyLeft => $"{Act()}{TargetText(a.WhyTarget)}: they had gone",
            AgentWhy.Away => $"{Act()}{TargetText(a.WhyTarget)}: they are on the road",
            AgentWhy.NoNews => $"{Act()}{TargetText(a.WhyTarget)}: nothing to tell them",
            AgentWhy.Idle => "nothing worth doing",
            AgentWhy.Unreached => $"{Act()}{TargetText(a.WhyTarget)}: couldn't get to them",
            AgentWhy.Dead => "dead",
            AgentWhy.InPlay => $"in the play {((uint)a.WhyIndex < (uint)Rules.Plays.Plays.Length ? Rules.Plays.Plays[a.WhyIndex].Name : "?")}",
            _ => "",
        };
    }

    // ---- Knowledge ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// An event was recorded: the people where it happened see it (the actor always knows; a sneaky one's target doesn't
    /// see it), and news told there reaches the listener and whoever else is there.
    /// </summary>
    public void OnEvent(in StoryEvent ev)
    {
        if (All.Count == 0 || ev.Verb == Rules.StoryVerb)
            return;
        // A person killed, or dead by themselves: for good, before anyone learns of it (the dead learn nothing).
        if (ev.Verb == P.KillVerb && ev.Target >= 0 && Of(ev.Target) is { Dead: false } victim)
            Die(victim, Engine.Time, ev.Id);
        else if (ev.Verb == P.DieVerb && ev.Actor >= 0 && Of(ev.Actor) is { Dead: false } died)
            Die(died, Engine.Time, ev.Id);
        var act = (uint)ev.Verb < (uint)P.ActionOfVerb.Length && P.ActionOfVerb[ev.Verb] >= 0 ? P.Actions[P.ActionOfVerb[ev.Verb]] : null;
        foreach (var a in All)
        {
            if (a.Dead)
                continue;
            var me = a.Entity;
            if (me == ev.Actor)
                Learn(a, ev.Id, ev.Verb, ev.Actor, ev.Target, ev.Owner, ev.Place, ev.Item, KnowSource.Did, -1);
            else if (ev.Place >= 0 && Where(me) == ev.Place && !(act is { Sneaky: true } && me == ev.Target))
                Learn(a, ev.Id, ev.Verb, ev.Actor, ev.Target, ev.Owner, ev.Place, ev.Item, KnowSource.Seen, -1);
        }
        if (act is { News: true } && ev.Value > 0)
        {
            // What was told: from the teller's own knowledge (it may have folded out of the chronicle).
            KnownItem news = default;
            var found = false;
            if (Of(ev.Actor) is { } teller)
                for (var k = 0; k < teller.KnownCount && !found; k++)
                    if (teller.Known[k].Event == ev.Value)
                        (news, found) = (teller.Known[k], true);
            if (!found && Engine.Chronicle.IndexOf(ev.Value) is var i and >= 0)
            {
                ref readonly var e = ref Engine.Chronicle[i];
                news = new KnownItem { Event = e.Id, Verb = e.Verb, Actor = e.Actor, Target = e.Target, Owner = e.Owner, Place = e.Place };
                found = true;
            }
            if (act.Radio)
                Air(ev.Value);
            if (found)
                foreach (var a in All)
                {
                    if (a.Dead || a.Entity == ev.Actor)
                        continue;
                    if (act.Radio)
                    {
                        // On the radio: everyone in a place hears it (the road has no radio).
                        if (Where(a.Entity) >= 0)
                            Learn(a, news.Event, news.Verb, news.Actor, news.Target, news.Owner, news.Place, -1, KnowSource.Radio, ev.Actor);
                    }
                    else if (a.Entity == ev.Target || ev.Place >= 0 && Where(a.Entity) == ev.Place)
                        Learn(a, news.Event, news.Verb, news.Actor, news.Target, news.Owner, news.Place, -1, KnowSource.Told, ev.Actor);
                }
        }
    }

    /// <summary>Has the radio put this news out lately?</summary>
    public bool Aired(int ev)
    {
        for (var i = 0; i < _airedCount; i++)
            if (_aired[i] == ev)
                return true;
        return false;
    }

    private void Air(int ev)
    {
        if (Aired(ev))
            return;
        _aired[_airedNext] = ev;
        _airedNext = (_airedNext + 1) % MaxAired;
        _airedCount = Math.Min(_airedCount + 1, MaxAired);
    }

    /// <summary>After a load: what the radio put out, from the broadcasts in the chronicle (oldest first, the last <see cref="MaxAired"/> kept).</summary>
    public void AiredFromChronicle()
    {
        _airedNext = _airedCount = 0;
        foreach (ref readonly var ev in Engine.Chronicle.All)
            if (ev.Value > 0 && (uint)ev.Verb < (uint)P.ActionOfVerb.Length && P.ActionOfVerb[ev.Verb] is var ai and >= 0 && P.Actions[ai].Radio)
                Air(ev.Value);
    }

    public bool Knows(StoryAgent a, int ev)
    {
        for (var k = 0; k < a.KnownCount; k++)
            if (a.Known[k].Event == ev)
                return true;
        return false;
    }

    /// <summary>
    /// A person learns of an event (once): their reactions to it change how they feel, and it is kept as news when it is
    /// interesting enough (the least interesting goes when they know too much).
    /// </summary>
    public void Learn(StoryAgent a, int ev, ushort verb, int actor, int target, int owner, int place, int item, KnowSource source, int from)
    {
        if (Knows(a, ev))
            return;
        var now = Engine.Time;
        var interest = (uint)verb < (uint)P.NewsInterest.Length ? P.NewsInterest[verb] : 0;
        if (_reactionsOf.Length != Rules.Verbs.Length)
            _reactionsOf = Enumerable.Range(0, Rules.Verbs.Length).Select(v => Enumerable.Range(0, P.Reactions.Length).Where(r => P.Reactions[r].Verb == v).ToArray()).ToArray();
        foreach (var r in _reactionsOf[verb])
        {
            var re = P.Reactions[r];
            var f = Ints8.Empty();
            (f[ReactionDef.Me], f[ReactionDef.Actor], f[ReactionDef.Target], f[ReactionDef.Owner], f[ReactionDef.Place], f[ReactionDef.Item]) = (a.Entity, actor, target, owner, place, item);
            if (re.If != null && !re.If.Any(W, f))
                continue;
            W.Apply(re.Then, f);
            interest += re.Interest;
        }
        if (interest < P.KeepInterest)
            return;
        Trace?.Invoke(a, $"learnt ({(source == KnowSource.Told ? $"told by {W.NameOf(from)}" : source == KnowSource.Radio ? $"on the radio, from {W.NameOf(from)}" : source == KnowSource.Road ? "heard on the road" : source.ToString().ToLowerInvariant())}, {interest}): {Engine.Sentence(new StoryEvent { Id = ev, Verb = verb, Actor = actor, Target = target, Owner = owner, Place = place, Item = item })}");
        var it = new KnownItem { Event = ev, Verb = verb, Actor = actor, Target = target, Owner = owner, Place = place, Source = source, From = from, Interest = Math.Clamp(interest, 0, 100), At = now };
        if (a.KnownCount < StoryAgent.MaxKnown)
        {
            a.Known[a.KnownCount++] = it;
            return;
        }
        var worst = 0;
        for (var k = 1; k < a.KnownCount; k++)
            if (Faded(a.Known[k], now) < Faded(a.Known[worst], now))
                worst = k;
        if (Faded(a.Known[worst], now) < it.Interest)
            a.Known[worst] = it;
    }

    /// <summary>
    /// The news worth most to put on the radio (0..100) and which (<paramref name="index"/>, -1 none): what hasn't been on
    /// the air lately, not one's own sneaky deeds, not news of the radio itself.
    /// </summary>
    public float BestRadioNews(StoryAgent a, double now, out int index)
    {
        index = -1;
        var best = 0f;
        for (var k = 0; k < a.KnownCount; k++)
        {
            ref readonly var it = ref a.Known[k];
            if (Aired(it.Event) || (uint)it.Verb < (uint)P.ActionOfVerb.Length && P.ActionOfVerb[it.Verb] is var ai and >= 0 && (P.Actions[ai].Radio || it.Source == KnowSource.Did && P.Actions[ai].Sneaky))
                continue;
            var v = Faded(it, now);
            if (v > best)
                (best, index) = (v, k);
        }
        return Math.Min(best, 100);
    }

    public static float Faded(in KnownItem it, double now) => it.Interest * (float)Math.Pow(0.5, Math.Max(0, now - it.At) / NewsHalfLife);

    /// <summary>
    /// The news worth most to tell <paramref name="listener"/> (0..100) and which (<paramref name="index"/>, -1 none): what
    /// they haven't heard, not their own doing, not one's own sneaky deeds; more when it is about them or someone they like.
    /// Only people (agents) can be told.
    /// </summary>
    public float BestNews(StoryAgent a, int listener, double now, out int index)
    {
        index = -1;
        var them = Of(listener);
        if (them == null || listener == a.Entity)
            return 0;
        var best = 0f;
        var likes = Rules.Relation("Likes")?.Id ?? -1;
        for (var k = 0; k < a.KnownCount; k++)
        {
            ref readonly var it = ref a.Known[k];
            if (it.Actor == listener || Knows(them, it.Event))
                continue;
            if (it.Source == KnowSource.Did && P.ActionOfVerb[it.Verb] is var ai and >= 0 && P.Actions[ai].Sneaky)
                continue;
            var v = Faded(it, now);
            if (it.Target == listener || likes >= 0 && it.Target >= 0 && W.Get(listener, likes, it.Target) > 0)
                v += 20;
            v *= Engine.Drama.NewsFactor(a, it, listener, now);
            if (v > best)
                (best, index) = (v, k);
        }
        return Math.Min(best, 100);
    }

    // ---- Saving -------------------------------------------------------------------------------------------------------

    /// <summary>A plain copy of the people for the save (with <see cref="StoryEngine.Capture"/>).</summary>
    public void Capture(StorySave s)
    {
        s.AgentSeed = Seed;
        if (s.NeedNames.Length != P.Needs.Length || s.GoalNames.Length != P.Goals.Length)
        {
            s.NeedNames = P.Needs.Select(n => n.Name).ToArray();
            s.GoalNames = P.Goals.Select(g => g.Name).ToArray();
        }
        var n = All.Count;
        StorySave.Grow(ref s.Agents, n);
        StorySave.Grow(ref s.AgentNeeds, n * P.Needs.Length);
        var goals = P.Goals.Length;
        StorySave.Grow(ref s.AgentGoalLast, n * goals);
        StorySave.Grow(ref s.AgentRests, n * StoryAgent.MaxRests);
        var known = 0;
        foreach (var a in All)
            known += a.KnownCount;
        StorySave.Grow(ref s.Known, known);
        StorySave.Grow(ref s.KnownStart, n + 1);
        s.AgentCount = n;
        known = 0;
        for (var i = 0; i < n; i++)
        {
            var a = All[i];
            var doing = a.Busy ? a.Doing : default;
            s.Agents[i] = new StorySave.AgentRec(a.Entity, a.Scripted, a.NeedAt, a.WakeAt, a.Goal, a.GoalTarget, a.Busy,
                a.Busy ? P.Actions[doing.Action].Verb : (ushort)0, doing.Target, doing.Owner, doing.Place, doing.Item, a.DoingFrom, a.DoingUntil, a.DoingFromPlace);
            a.Need.AsSpan().CopyTo(s.AgentNeeds.AsSpan(i * P.Needs.Length));
            a.GoalLast.AsSpan().CopyTo(s.AgentGoalLast.AsSpan(i * goals));
            a.Rests.AsSpan().CopyTo(s.AgentRests.AsSpan(i * StoryAgent.MaxRests));
            s.KnownStart[i] = known;
            a.Known.AsSpan(0, a.KnownCount).CopyTo(s.Known.AsSpan(known));
            known += a.KnownCount;
        }
        s.KnownStart[n] = known;
    }

    /// <summary>
    /// The people from a save (<see cref="StoryEngine.Read"/>, version 2): needs and goals by name, verbs through the save's
    /// verb map. A step under way goes on (a trip still arrives); the rest of a plan is made again.
    /// </summary>
    public void Read(SaveReader r, int[] verbs)
    {
        Seed = r.U64();
        if (Seed == 0)
            Seed = 0x9E3779B97F4A7C15UL;
        var needNames = new string[r.Count(4)];
        for (var i = 0; i < needNames.Length; i++)
            needNames[i] = r.Str();
        var goalNames = new string[r.Count(4)];
        for (var i = 0; i < goalNames.Length; i++)
            goalNames[i] = r.Str();
        var needMap = needNames.Select(P.Need).ToArray();
        var goalMap = goalNames.Select(P.Goal).ToArray();
        int VerbNow(int old) => (uint)old < (uint)verbs.Length ? verbs[old] : -1;
        var n = r.Count(70);
        for (var i = 0; i < n; i++)
        {
            var entity = r.I32();
            var scripted = r.U8() != 0;
            var needAt = r.F64();
            var wakeAt = r.F64();
            var goal = r.I32();
            var goalTarget = r.I32();
            var busy = r.U8() != 0;
            var verb = VerbNow(r.U16());
            var (target, owner, place, item) = (r.I32(), r.I32(), r.I32(), r.I32());
            var (from, until, fromPlace) = (r.F64(), r.F64(), r.I32());
            var needs = new float[needNames.Length];
            for (var k = 0; k < needs.Length; k++)
                needs[k] = r.F32();
            var goalLast = new double[goalNames.Length];
            for (var k = 0; k < goalLast.Length; k++)
                goalLast[k] = r.F64();
            Span<GoalRest> rests = stackalloc GoalRest[StoryAgent.MaxRests];
            for (var k = 0; k < rests.Length; k++)
                rests[k] = new GoalRest { Goal = r.I32(), Target = r.I32(), Until = r.F64() };
            var kc = r.Count(37);
            var known = new KnownItem[kc];
            for (var k = 0; k < kc; k++)
            {
                known[k] = new KnownItem { Event = r.I32(), Verb = (ushort)Math.Max(0, VerbNow(r.U16())), Actor = r.I32(), Target = r.I32(), Owner = r.I32(), Place = r.I32() };
                known[k].Source = (KnowSource)r.U8();
                known[k].From = r.I32();
                known[k].Interest = r.F32();
                known[k].At = r.F64();
            }
            if (!W.Valid(entity) || _of.ContainsKey(entity))
                continue;
            var a = NewAgent(entity, scripted, needAt);
            for (var k = 0; k < needs.Length; k++)
                if (needMap[k] >= 0 && float.IsFinite(needs[k]))
                    a.Need[needMap[k]] = Math.Clamp(needs[k], 0, 100);
            a.WakeAt = double.IsFinite(wakeAt) ? wakeAt : Engine.Time;
            for (var k = 0; k < goalLast.Length; k++)
                if (goalMap[k] >= 0 && double.IsFinite(goalLast[k]))
                    a.GoalLast[goalMap[k]] = goalLast[k];
            for (var k = 0; k < rests.Length; k++)
                if ((uint)rests[k].Goal < (uint)goalMap.Length && goalMap[rests[k].Goal] >= 0 && double.IsFinite(rests[k].Until))
                    a.Rests[k] = rests[k] with { Goal = goalMap[rests[k].Goal] };
            if ((uint)goal < (uint)goalMap.Length && goalMap[goal] >= 0)
                (a.Goal, a.GoalTarget) = (goalMap[goal], goalTarget); // (its plan is made again when they next think)
            var action = verb >= 0 && verb < P.ActionOfVerb.Length ? P.ActionOfVerb[verb] : -1;
            if (busy && action >= 0)
            {
                a.Busy = true;
                a.Doing = new PlanStep { Action = (short)action, Target = target, Owner = owner, Place = place, Item = item };
                (a.DoingFrom, a.DoingUntil, a.DoingFromPlace) = (from, until, fromPlace);
                a.Plan[0] = a.Doing;
                (a.PlanCount, a.PlanNext) = (1, 0);
                a.WakeAt = until;
            }
            else if (busy && fromPlace >= 0 && W.Valid(fromPlace))
                W.Set(entity, P.In, fromPlace, 1); // (a trip whose verb is gone: back where they set off)
            if (P.Dead >= 0 && W.Quality(entity, P.Dead) > 0)
            {
                // (dead for good: the quality is what the save keeps)
                (a.Dead, a.Busy, a.PlanCount, a.PlanNext, a.Goal, a.GoalTarget) = (true, false, 0, 0, -1, -1);
                (a.Why, a.WakeAt) = (AgentWhy.Dead, double.MaxValue);
            }
            foreach (var it in known)
                if (it.Event > 0 && a.KnownCount < StoryAgent.MaxKnown && W.Valid(it.Actor) | it.Actor < 0)
                    a.Known[a.KnownCount++] = it;
        }
    }
}
