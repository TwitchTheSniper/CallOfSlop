namespace CallOfSlop.Story;

/// <summary>
/// The HTN planner (docs/story/STORY.md "People"): a goal's task is decomposed depth first, in order: a compound task tries
/// its methods in turn (the first whose condition holds and whose steps all plan), an action checks its condition and
/// puts its verb's effects on the world. It plans on the real world state inside a trial (<see cref="StoryWorld.BeginTrial"/>),
/// so a later condition sees what earlier steps change (after a Travel, "?me In ?here" is the new place), and backtracking
/// rolls the effects back. Bounded (steps, depth, expansions); allocates nothing.
/// </summary>
public sealed class StoryHtn
{
    public const int MaxExpansions = 400, MaxDepth = 12;

    private struct Pending
    {
        public bool Action;
        public short Index;
        public Ints8 Args; // action: target, owner, place, item; task: its parameters (-1 unbound)
        public byte Depth;
    }

    private readonly StoryAgents _people;
    private readonly Pending[] _agenda = new Pending[64];
    private StoryAgent _a = null!;
    private int _expansions;

    public StoryHtn(StoryAgents people) => _people = people;

    private StoryWorld W => _people.W;
    private PeopleRules P => _people.P;

    /// <summary>A plan for <paramref name="goal"/> (aimed at <paramref name="target"/>, -1 none) into the person's Plan and Tree. False: none.</summary>
    public bool Plan(StoryAgent a, GoalDef goal, int target, double now)
    {
        _a = a;
        a.PlanCount = a.PlanNext = 0;
        a.TreeCount = 0;
        // The goal's own variables again, with its target: what "if" and "pick" bind goes to the task's arguments.
        var f = Ints8.Empty();
        f[GoalDef.Me] = a.Entity;
        f[GoalDef.Here] = _people.Where(a.Entity);
        f[GoalDef.Them] = target;
        if (goal.If != null && !goal.If.Any(W, f) || goal.Pick != null && !goal.Pick.Any(W, f))
            return false;
        _agenda[0] = Make(goal.Do, f, 0);
        W.BeginTrial();
        try
        {
            _expansions = 0;
            return Run(1) && a.PlanCount > 0;
        }
        finally
        {
            W.EndTrial();
        }
    }

    private static Pending Make(CallDef call, in Ints8 frame, int depth)
    {
        var p = new Pending { Action = call.Action, Index = (short)call.Index, Args = Ints8.Empty(), Depth = (byte)Math.Min(depth, 255) };
        for (var i = 0; i < call.Args.Length && i < StoryRules.MaxVars; i++)
            p.Args[i] = call.Args[i] >= 0 ? frame[call.Args[i]] : -1;
        return p;
    }

    /// <summary>Plans the agenda's top <paramref name="top"/> items (the last is next). True: the whole plan worked out.</summary>
    private bool Run(int top)
    {
        if (top == 0)
            return true;
        if (++_expansions > MaxExpansions)
            return false;
        var at = top - 1;
        var item = _agenda[at];
        var me = _a.Entity;
        var treeMark = _a.TreeCount;
        if (item.Action)
        {
            var act = P.Actions[item.Index];
            var f = Ints8.Empty();
            (f[ActionDef.Me], f[ActionDef.Here], f[ActionDef.Target], f[ActionDef.Owner], f[ActionDef.Place], f[ActionDef.Item]) =
                (me, _people.Where(me), item.Args[0], item.Args[1], item.Args[2], item.Args[3]);
            if (act.If != null && !act.If.Any(W, f) || _a.PlanCount >= StoryAgent.MaxSteps)
                return false;
            var step = new PlanStep
            {
                Action = item.Index, Target = f[ActionDef.Target], Owner = f[ActionDef.Owner], Item = f[ActionDef.Item],
                Place = f[ActionDef.Place] >= 0 || act.Road ? f[ActionDef.Place] : f[ActionDef.Here],
                Node = (byte)Math.Min(_a.TreeCount, 255),
            };
            var mark = W.TrialMark;
            Span<int> slots = [me, step.Target, step.Owner, step.Place, act.Road ? f[ActionDef.Here] : step.Item];
            W.Apply(_people.Rules.Verbs[act.Verb].Effects, slots);
            if (act.Road && step.Place >= 0 && _people.Where(me) != step.Place)
                W.Set(me, P.In, step.Place, 1);
            Node(item.Depth, true, item.Index, 0, step.Target, step.Place);
            _a.Plan[_a.PlanCount++] = step;
            if (Run(at))
                return true;
            W.RollBack(mark);
            _a.PlanCount--;
            _a.TreeCount = treeMark;
            _agenda[at] = item;
            return false;
        }
        var task = P.Tasks[item.Index];
        if (item.Depth >= MaxDepth)
            return false;
        for (var m = 0; m < task.Methods.Length; m++)
        {
            var md = task.Methods[m];
            var f = Ints8.Empty();
            f[TaskDef.Me] = me;
            f[TaskDef.Here] = _people.Where(me);
            for (var i = 0; i < task.Params.Length; i++)
                f[2 + i] = item.Args[i];
            if (md.If != null && !md.If.Any(W, f))
                continue;
            var n = md.Do.Length;
            if (at + n > _agenda.Length)
                continue;
            Node(item.Depth, false, item.Index, m, task.Params.Length > 0 ? item.Args[0] : -1, task.Params.Length > 1 ? item.Args[1] : -1);
            for (var k = 0; k < n; k++)
                _agenda[at + n - 1 - k] = Make(md.Do[k], f, item.Depth + 1);
            if (Run(at + n))
                return true;
            _a.TreeCount = treeMark;
            _agenda[at] = item;
        }
        return false;
    }

    private void Node(int depth, bool action, int index, int method, int a, int b)
    {
        if (_a.TreeCount >= StoryAgent.MaxNodes)
            return;
        _a.Tree[_a.TreeCount++] = new PlanNode { Depth = (byte)Math.Min(depth, 255), Action = action, Index = (short)index, Method = (short)method, A = a, B = b };
    }
}
