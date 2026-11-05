using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>A need (Energy, Company): a quality from 0 (desperate) to 100 (met) that falls with time and actions refill.</summary>
public sealed class NeedDef
{
    public string Name = "", About = "";
    public ushort Quality;
    public float FallsPerSecond;
}

/// <summary>What a consideration of a goal reads (docs/story/STORY.md "People").</summary>
public enum NeedInput : byte
{
    Need, // one of my needs, 0..100
    Quality, // one of my qualities (a trait, Wealth)
    Their, // one of the target's qualities
    Rel, // my relation's strength towards the target
    Held, // the target's relation's strength towards me
    News, // the most interesting news I have that the target hasn't heard, 0..100
    Here, // 1 when the target is where I am
    Since, // seconds since this goal last ended (done, given up or put aside); while it is under way, since before
    Radio, // the most interesting news I have that hasn't been on the radio, 0..100
}

public enum NeedCurve : byte
{
    Linear,
    Inverse, // 1 - x
    Square,
    InverseSquare, // (1 - x)^2
    Root,
}

/// <summary>One factor of a goal's score: an input mapped from [From, To] onto 0..1, through a curve, and kept at least Min.</summary>
public readonly record struct Consideration(NeedInput Input, int What, float From, float To, NeedCurve Curve, float Min, string Text);

/// <summary>
/// A primitive task: a verb a person can do, how long it takes, what must hold (a query over ?me, ?here, ?target,
/// ?owner, ?place, ?item), and the needs it refills. <see cref="Chronicle"/> false: routine (its effects happen, but no
/// event is recorded).
/// </summary>
public sealed class ActionDef
{
    public const int Me = 0, Here = 1, Target = 2, Owner = 3, Place = 4, Item = 5; // its query's variables
    public ushort Verb;
    public string Name = "", About = "";
    public double Takes;
    public bool Road; // takes the road's time from here to ?place
    public StoryQuery? If;
    public (ushort Need, float Amount)[] Refills = [];
    public bool Chronicle = true, News, Sneaky;
    public bool Radio; // a broadcast: news (the best not yet on the air) that everyone in a place hears
}

/// <summary>A step a method takes: a verb (roles from the method's variables) or a compound task (arguments likewise).</summary>
public sealed class CallDef
{
    public bool Action;
    public int Index; // an action, or a task
    public sbyte[] Args = []; // action: per role target, owner, place, item; task: per parameter. A method variable, -1 none
    public string Text = "";
}

public sealed class MethodDef
{
    public string Name = "";
    public StoryQuery? If;
    public CallDef[] Do = [];
    public string[] Vars = [];
}

/// <summary>A compound task: methods tried in order; the first whose condition holds and whose steps all plan is taken.</summary>
public sealed class TaskDef
{
    public const int Me = 0, Here = 1; // then its parameters, then what its methods bind
    public string Name = "", About = "";
    public int Id;
    public string[] Params = [];
    public MethodDef[] Methods = [];
}

/// <summary>Something a person can want, scored by utility (each consideration multiplied), with a task to plan for it.</summary>
public sealed class GoalDef
{
    public const int Me = 0, Here = 1, Them = 2;
    public string Name = "", About = "";
    public int Id;
    public StoryQuery? If, Pick;
    public Consideration[] Score = [];
    public float Weight = 1;
    public CallDef Do = new();
    public string[] Vars = [];
}

/// <summary>How a person feels about news: when they learn of an event of this verb and the condition holds, the effects.</summary>
public sealed class ReactionDef
{
    public const int Me = 0, Actor = 1, Target = 2, Owner = 3, Place = 4, Item = 5;
    public ushort Verb;
    public StoryQuery? If;
    public StoryEffect[] Then = [];
    public int Interest;
    public string Text = "";
}

/// <summary>A set of people to put in a world (Story/Casts/*.json): places and roads, people, how they feel at the start.</summary>
public sealed class CastDef
{
    public sealed record Place(string Name, bool World);
    public sealed record Road(string From, string To, int Minutes);
    public sealed record Person(string Name, string About, string Home, string Works, (ushort Quality, int Value)[] Qualities);
    public sealed record Feeling(string Who, ushort Rel, string To, int Value);

    public string Name = "", About = "", File = "";
    public Place[] Places = [];
    public Road[] Roads = [];
    public Person[] People = [];
    public Feeling[] Feelings = [];
}

/// <summary>
/// The people's rules (box 4 in the diagram): needs, actions over the shared verbs, compound tasks, goals, reactions to
/// news, how interesting each verb is as news, and the casts. From Story/People/*.json and Story/Casts/*.json; a bad file
/// is refused whole with a reason (in <see cref="StoryRules.Errors"/>). Empty when there are none.
/// </summary>
public sealed class PeopleRules
{
    public const int MaxParams = 4, MaxSteps = 8;

    public NeedDef[] Needs = [];
    public ActionDef[] Actions = [];
    public int[] ActionOfVerb = []; // per verb: its action, -1 none
    public TaskDef[] Tasks = [];
    public GoalDef[] Goals = [];
    public ReactionDef[] Reactions = [];
    public int[] NewsInterest = []; // per verb, 0..100
    public CastDef[] Casts = [];
    public int KeepInterest = 15; // news less interesting than this isn't kept
    public int Home = -1, WorksAt = -1, Road = -1, In = -1, Knows = -1; // relations the people need
    public int Dead = -1; // the quality a dead person carries (-1: none in the data; the agent still dies)
    public int KillVerb = -1, DieVerb = -1; // a person who is a Kill's target or a Die's actor dies for good

    public bool Any => Goals.Length > 0 && Actions.Length > 0;

    public CastDef? Cast(string name) => Casts.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public int Task(string name) => Array.FindIndex(Tasks, t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public int Goal(string name) => Array.FindIndex(Goals, g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    public int Need(string name) => Array.FindIndex(Needs, n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Is this verb's action one that passes on news (Tell)?</summary>
    public bool ActionIsNews(int verb) => (uint)verb < (uint)ActionOfVerb.Length && ActionOfVerb[verb] is var a and >= 0 && Actions[a].News;

    private static readonly string[] FileKeys = ["about", "needs", "actions", "tasks", "goals", "reactions", "news", "keep"];
    private static readonly string[] NeedKeys = ["name", "falls", "about"];
    private static readonly string[] ActionKeys = ["verb", "takes", "if", "needs", "chronicle", "news", "sneaky", "radio", "about"];
    private static readonly string[] TaskKeys = ["name", "params", "methods", "about"];
    private static readonly string[] MethodKeys = ["name", "if", "do"];
    private static readonly string[] CallKeys = ["do", "task", "args", "target", "owner", "place", "item"];
    private static readonly string[] GoalKeys = ["name", "about", "if", "pick", "score", "weight", "do"];
    private static readonly string[] ScoreKeys = ["need", "quality", "their", "rel", "held", "news", "radio", "here", "since", "from", "to", "curve", "min"];
    private static readonly string[] ReactionKeys = ["verb", "if", "then", "interest"];
    private static readonly string[] NewsKeys = ["verb", "interest"];
    private static readonly string[] CastKeys = ["name", "about", "places", "roads", "people", "feelings"];
    private static readonly string[] PlaceKeys = ["name", "world"];
    private static readonly string[] RoadKeys = ["from", "to", "takes"];
    private static readonly string[] PersonKeys = ["name", "about", "home", "works", "qualities"];
    private static readonly string[] FeelingKeys = ["who", "rel", "to", "value"];

    /// <summary>Story/People/*.json then Story/Casts/*.json under <paramref name="folder"/>. Errors go to the rules' list.</summary>
    public static PeopleRules Load(StoryRules rules, string folder)
    {
        var p = new PeopleRules
        {
            ActionOfVerb = Enumerable.Repeat(-1, rules.Verbs.Length).ToArray(),
            NewsInterest = new int[rules.Verbs.Length],
            Home = rules.Relation("Home")?.Id ?? -1,
            WorksAt = rules.Relation("WorksAt")?.Id ?? -1,
            Road = rules.Relation("Road")?.Id ?? -1,
            In = rules.Relation("In")?.Id ?? -1,
            Knows = rules.Relation("Knows")?.Id ?? -1,
            Dead = rules.Quality("Dead") is { } dead ? dead.Id : -1,
            KillVerb = rules.VerbId("Kill"),
            DieVerb = rules.VerbId("Die"),
        };
        var files = StoryRules.Files(Path.Combine(folder, "People"));
        // Tasks are named before any method is read, so they may call each other in any order and across files.
        var docs = new List<(string File, JsonDocument Doc)>();
        try
        {
            foreach (var f in files)
            {
                var name = $"People/{Path.GetFileName(f)}";
                try
                {
                    docs.Add((name, StoryRules.Parse(StoryRules.ReadFile(f), name)));
                }
                catch (StoryException ex)
                {
                    rules.Errors.Add(ex.Message);
                }
            }
            var taskNames = new List<TaskDef>();
            foreach (var (file, doc) in docs)
                foreach (var t in StoryRules.List(doc.RootElement, "tasks", file))
                    if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        && !taskNames.Any(x => x.Name.Equals(n.GetString(), StringComparison.OrdinalIgnoreCase)))
                        taskNames.Add(new TaskDef { Name = n.GetString() ?? "", Id = taskNames.Count, Params = t.TryGetProperty("params", out var ps) && ps.ValueKind == JsonValueKind.Array ? ps.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToArray() : [] });
            p.Tasks = taskNames.ToArray();
            foreach (var (file, doc) in docs)
                try
                {
                    p.Read(rules, doc.RootElement, file);
                }
                catch (StoryException ex)
                {
                    rules.Errors.Add(ex.Message);
                }
        }
        finally
        {
            foreach (var (_, doc) in docs)
                doc.Dispose();
        }
        // (A task named in a file that was refused has no methods: nothing that was kept calls it, and it never plans.)
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Casts")))
        {
            var name = $"Casts/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), name);
                p.Casts = [.. p.Casts, p.ReadCast(rules, doc.RootElement, name)];
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
            }
        }
        if (p.Goals.Length > 0 && (p.In < 0 || p.Knows < 0))
            rules.Errors.Add("People: the relations In and Knows are needed (Relations.json)");
        return p;
    }

    /// <summary>One People file: its needs, actions, tasks, goals, reactions and news, all or nothing.</summary>
    private void Read(StoryRules rules, JsonElement root, string file)
    {
        StoryRules.Keys(root, FileKeys, file);
        var needs = Needs.ToList();
        foreach (var n in StoryRules.List(root, "needs", file))
        {
            var name = StoryRules.Str(n, "name", $"{file}: a need", true);
            var where = $"{file}: need {name}";
            StoryRules.Keys(n, NeedKeys, where);
            var q = rules.Quality(name) ?? throw new StoryException($"{where}: a need is a quality, and there is no quality {name} (Relations)");
            if (needs.Any(x => x.Quality == q.Id))
                throw new StoryException($"{where}: named twice");
            needs.Add(new NeedDef { Name = q.Name, About = StoryRules.Str(n, "about", where), Quality = q.Id, FallsPerSecond = Number(n, "falls", where, 0, 1000) / 3600f });
        }
        // Actions next: tasks call them.
        var actions = Actions.ToList();
        var ofVerb = (int[])ActionOfVerb.Clone();
        foreach (var a in StoryRules.List(root, "actions", file))
        {
            var verbName = StoryRules.Str(a, "verb", $"{file}: an action", true);
            var where = $"{file}: action {verbName}";
            StoryRules.Keys(a, ActionKeys, where);
            var verb = rules.Verb(verbName) ?? throw new StoryException($"{where}: no verb {verbName} (Verbs)");
            if (ofVerb[verb.Id] >= 0)
                throw new StoryException($"{where}: the verb has an action already");
            var def = new ActionDef { Verb = verb.Id, Name = verb.Name, About = StoryRules.Str(a, "about", where) };
            var takes = StoryRules.Str(a, "takes", where, true);
            if (takes.Equals("road", StringComparison.OrdinalIgnoreCase))
            {
                if (Road < 0)
                    throw new StoryException($"{where}: \"takes\": \"road\" needs the relation Road");
                def.Road = true;
            }
            else
                def.Takes = StoryRules.Duration(takes) is { } d and > 0 and <= 86400 ? d : throw new StoryException($"{where}: \"takes\" is a time like \"90s\" or \"4m\", or \"road\"");
            def.If = Query(rules, StoryRules.Str(a, "if", where), ["me", "here", "target", "owner", "place", "item"], where);
            def.Refills = Refills(a, needs, where);
            def.Chronicle = !a.TryGetProperty("chronicle", out _) || StoryRules.Bool(a, "chronicle", where);
            def.News = StoryRules.Bool(a, "news", where);
            def.Sneaky = StoryRules.Bool(a, "sneaky", where);
            def.Radio = StoryRules.Bool(a, "radio", where);
            if (def.Radio)
                def.News = true; // (a broadcast is news, to everyone at once)
            ofVerb[verb.Id] = actions.Count;
            actions.Add(def);
        }
        // Tasks: their names are known already (Load); here their methods.
        var tasksDone = new List<(TaskDef Task, MethodDef[] Methods)>();
        foreach (var t in StoryRules.List(root, "tasks", file))
        {
            var name = StoryRules.Str(t, "name", $"{file}: a task", true);
            var where = $"{file}: task {name}";
            StoryRules.Keys(t, TaskKeys, where);
            var task = Tasks.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Methods.Length == 0 && !tasksDone.Any(d => d.Task == x))
                ?? throw new StoryException($"{where}: named twice");
            if (!StoryRules.Word(name))
                throw new StoryException($"{where}: a name is one word");
            if (task.Params.Length > MaxParams || task.Params.Any(x => !x.StartsWith('?') || !StoryRules.Word(x[1..])))
                throw new StoryException($"{where}: \"params\" is up to {MaxParams} variables, like [\"?who\"]");
            task.About = StoryRules.Str(t, "about", where);
            var methods = new List<MethodDef>();
            foreach (var m in StoryRules.List(t, "methods", where, true))
            {
                var mname = StoryRules.Str(m, "name", where);
                var mwhere = $"{where}: method {(mname.Length > 0 ? mname : (methods.Count + 1).ToString())}";
                StoryRules.Keys(m, MethodKeys, mwhere);
                var vars = new List<string> { "me", "here" };
                vars.AddRange(task.Params.Select(x => x[1..]));
                var md = new MethodDef { Name = mname.Length > 0 ? mname : $"method {methods.Count + 1}" };
                md.If = Query(rules, StoryRules.Str(m, "if", mwhere), vars, mwhere);
                md.Do = StoryRules.List(m, "do", mwhere, true).Select((c, i) => Call(c, vars, actions, $"{mwhere}: step {i + 1}")).ToArray();
                if (md.Do.Length > MaxSteps)
                    throw new StoryException($"{mwhere}: {MaxSteps} steps at most");
                md.Vars = vars.ToArray();
                methods.Add(md);
            }
            if (methods.Count == 0)
                throw new StoryException($"{where}: no methods");
            tasksDone.Add((task, methods.ToArray()));
        }
        var goals = Goals.ToList();
        foreach (var g in StoryRules.List(root, "goals", file))
        {
            var name = StoryRules.Str(g, "name", $"{file}: a goal", true);
            var where = $"{file}: goal {name}";
            StoryRules.Keys(g, GoalKeys, where);
            if (name.Length is 0 or > 40 || goals.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{where}: a name of 1 to 40 characters, once");
            var vars = new List<string> { "me", "here", "them" };
            var def = new GoalDef { Name = name, About = StoryRules.Str(g, "about", where), Id = goals.Count };
            def.If = Query(rules, StoryRules.Str(g, "if", where), vars, where);
            def.Pick = Query(rules, StoryRules.Str(g, "pick", where), vars, $"{where}: pick");
            if (def.Pick != null && !def.Pick.Text.Contains("?them"))
                throw new StoryException($"{where}: \"pick\" is a query that finds ?them");
            if (g.TryGetProperty("weight", out var w))
                def.Weight = w.ValueKind == JsonValueKind.Number && w.TryGetSingle(out var wv) && wv is >= 0 and <= 10 ? wv : throw new StoryException($"{where}: \"weight\" is a number from 0 to 10");
            def.Score = StoryRules.List(g, "score", where, true).Select((c, i) => Consider(rules, c, needs, def.Pick != null, $"{where}: score {i + 1}")).ToArray();
            if (!g.TryGetProperty("do", out var doEl))
                throw new StoryException($"{where}: \"do\" is missing (a task, like {{\"task\": \"RestUp\"}})");
            def.Do = Call(doEl, vars, actions, $"{where}: do");
            def.Vars = vars.ToArray();
            goals.Add(def);
        }
        var reactions = Reactions.ToList();
        foreach (var r in StoryRules.List(root, "reactions", file))
        {
            var verbName = StoryRules.Str(r, "verb", $"{file}: a reaction", true);
            var where = $"{file}: reaction to {verbName}";
            StoryRules.Keys(r, ReactionKeys, where);
            var verb = rules.Verb(verbName) ?? throw new StoryException($"{where}: no verb {verbName}");
            var vars = new List<string> { "me", "actor", "target", "owner", "place", "item" };
            var def = new ReactionDef { Verb = verb.Id, Text = $"{verbName}{(r.TryGetProperty("if", out var iff) ? $" if {iff}" : "")}" };
            def.If = Query(rules, StoryRules.Str(r, "if", where), vars, where);
            int Slot(string word) => vars.IndexOf(word.TrimStart('?'));
            def.Then = StoryRules.List(r, "then", where).Select(e => e.ValueKind == JsonValueKind.String
                ? rules.ParseEffect(e.GetString() ?? "", where, Slot)
                : throw new StoryException($"{where}: each \"then\" is text, like \"me Grudge actor +30\"")).ToArray();
            def.Interest = (int)Number(r, "interest", where, -100, 100);
            reactions.Add(def);
        }
        var news = (int[])NewsInterest.Clone();
        foreach (var n in StoryRules.List(root, "news", file))
        {
            var verbName = StoryRules.Str(n, "verb", $"{file}: news", true);
            var where = $"{file}: news {verbName}";
            StoryRules.Keys(n, NewsKeys, where);
            var verb = rules.Verb(verbName) ?? throw new StoryException($"{where}: no verb {verbName}");
            news[verb.Id] = (int)Number(n, "interest", where, 0, 100);
        }
        var keep = root.TryGetProperty("keep", out _) ? (int)Number(root, "keep", file, 0, 100) : KeepInterest;
        // All read: commit.
        Needs = needs.ToArray();
        Actions = actions.ToArray();
        ActionOfVerb = ofVerb;
        foreach (var (task, methods) in tasksDone)
            task.Methods = methods;
        Goals = goals.ToArray();
        Reactions = reactions.ToArray();
        NewsInterest = news;
        KeepInterest = keep;
    }

    private static float Number(JsonElement e, string key, string where, float min, float max)
    {
        if (!e.TryGetProperty(key, out var v))
            return 0;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetSingle(out var f) || f < min || f > max)
            throw new StoryException($"{where}: \"{key}\" is a number from {min} to {max}");
        return f;
    }

    private static StoryQuery? Query(StoryRules rules, string text, List<string> vars, string where)
    {
        if (text.Length == 0)
            return null;
        try
        {
            return StoryQuery.Compile(text, rules, vars);
        }
        catch (StoryException ex)
        {
            throw new StoryException($"{where}: \"{text}\": {ex.Message}");
        }
    }

    private static (ushort, float)[] Refills(JsonElement a, List<NeedDef> needs, string where)
    {
        if (!a.TryGetProperty("needs", out var n))
            return [];
        if (n.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: \"needs\" is like {{\"Energy\": 50, \"Purpose\": -10}}");
        var list = new List<(ushort, float)>();
        foreach (var x in n.EnumerateObject())
        {
            var i = needs.FindIndex(d => d.Name.Equals(x.Name, StringComparison.OrdinalIgnoreCase));
            if (i < 0)
                throw new StoryException($"{where}: needs: no need {x.Name}");
            if (x.Value.ValueKind != JsonValueKind.Number || !x.Value.TryGetSingle(out var amount) || amount is < -100 or > 100)
                throw new StoryException($"{where}: needs: {x.Name} is a number from -100 to 100");
            list.Add(((ushort)i, amount));
        }
        return list.ToArray();
    }

    /// <summary>A step: {"do": "Chat", "target": "?who"} (a verb with roles) or {"task": "GoTo", "args": ["?p"]}.</summary>
    private CallDef Call(JsonElement c, List<string> vars, List<ActionDef> actions, string where)
    {
        if (c.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: a step is {{\"do\": \"Chat\", \"target\": \"?who\"}} or {{\"task\": \"GoTo\", \"args\": [\"?p\"]}}");
        StoryRules.Keys(c, CallKeys, where);
        sbyte Var(string w, string what)
        {
            if (w.Length == 0)
                return -1;
            var i = w.StartsWith('?') ? vars.IndexOf(w[1..]) : -1;
            return i >= 0 ? (sbyte)i : throw new StoryException($"{where}: {what} \"{w}\" isn't a variable bound here (?me, ?here, a parameter, or one the condition finds)");
        }
        var verbName = StoryRules.Str(c, "do", where);
        if (verbName.Length > 0)
        {
            var a = actions.FindIndex(x => x.Name.Equals(verbName, StringComparison.OrdinalIgnoreCase));
            if (a < 0)
                throw new StoryException($"{where}: no action for the verb {verbName} (an \"actions\" entry)");
            var args = new sbyte[4];
            for (var r = 0; r < 4; r++)
                args[r] = Var(StoryRules.Str(c, StoryRules.RoleNames[r + 1], where), StoryRules.RoleNames[r + 1]);
            return new CallDef { Action = true, Index = a, Args = args, Text = $"{actions[a].Name}{string.Concat(Enumerable.Range(0, 4).Where(r => args[r] >= 0).Select(r => $" {StoryRules.RoleNames[r + 1]} ?{vars[args[r]]}"))}" };
        }
        var taskName = StoryRules.Str(c, "task", where, true);
        var t = Task(taskName);
        if (t < 0)
            throw new StoryException($"{where}: no task {taskName}");
        var given = StoryRules.List(c, "args", where).Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : throw new StoryException($"{where}: \"args\" are variables")).ToArray();
        if (given.Length != Tasks[t].Params.Length)
            throw new StoryException($"{where}: {Tasks[t].Name} takes {Tasks[t].Params.Length} arguments ({string.Join(", ", Tasks[t].Params)})");
        var targs = given.Select(w => Var(w, "an argument")).ToArray();
        return new CallDef { Action = false, Index = t, Args = targs, Text = $"{Tasks[t].Name}({string.Join(", ", given)})" };
    }

    private static Consideration Consider(StoryRules rules, JsonElement c, List<NeedDef> needs, bool picks, string where)
    {
        if (c.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: a consideration is like {{\"need\": \"Energy\", \"curve\": \"inverse\"}}");
        StoryRules.Keys(c, ScoreKeys, where);
        NeedInput input;
        var what = -1;
        float from = 0, to = 100;
        string text;
        if (c.TryGetProperty("need", out _))
        {
            var n = StoryRules.Str(c, "need", where);
            what = needs.FindIndex(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            if (what < 0)
                throw new StoryException($"{where}: no need {n}");
            input = NeedInput.Need;
            text = $"need {needs[what].Name}";
        }
        else if (c.TryGetProperty("quality", out _) || c.TryGetProperty("their", out _))
        {
            var mine = c.TryGetProperty("quality", out _);
            var n = StoryRules.Str(c, mine ? "quality" : "their", where);
            what = rules.Quality(n)?.Id ?? throw new StoryException($"{where}: no quality {n}");
            input = mine ? NeedInput.Quality : NeedInput.Their;
            text = mine ? n : $"their {n}";
        }
        else if (c.TryGetProperty("rel", out _) || c.TryGetProperty("held", out _))
        {
            var mine = c.TryGetProperty("rel", out _);
            var n = StoryRules.Str(c, mine ? "rel" : "held", where);
            what = rules.Relation(n)?.Id ?? throw new StoryException($"{where}: no relation {n}");
            input = mine ? NeedInput.Rel : NeedInput.Held;
            text = mine ? $"my {n} of them" : $"their {n} of me";
        }
        else if (c.TryGetProperty("news", out _))
        {
            input = NeedInput.News;
            text = "news for them";
        }
        else if (c.TryGetProperty("radio", out _))
        {
            input = NeedInput.Radio;
            text = "news for the radio";
        }
        else if (c.TryGetProperty("here", out _))
        {
            input = NeedInput.Here;
            (from, to) = (0, 1);
            text = "they are here";
        }
        else if (c.TryGetProperty("since", out _))
        {
            input = NeedInput.Since;
            to = (float)(StoryRules.Duration(StoryRules.Str(c, "since", where)) ?? throw new StoryException($"{where}: \"since\" is a time, like \"20m\" (the input reaches 1 then)"));
            text = "time since";
        }
        else
            throw new StoryException($"{where}: say what it reads: need, quality, their, rel, held, news, radio, here or since");
        if (input is NeedInput.Their or NeedInput.Rel or NeedInput.Held or NeedInput.News or NeedInput.Here && !picks)
            throw new StoryException($"{where}: {text} needs a target: give the goal a \"pick\"");
        if (input == NeedInput.Since && c.TryGetProperty("from", out var sf) && sf.ValueKind == JsonValueKind.String)
            from = (float)(StoryRules.Duration(sf.GetString() ?? "") ?? throw new StoryException($"{where}: \"from\" is a time, like \"30m\" (the input is 0 until then)"));
        else if (c.TryGetProperty("from", out _))
            from = Number(c, "from", where, -1_000_000, 1_000_000);
        if (c.TryGetProperty("to", out _))
            to = Number(c, "to", where, -1_000_000, 1_000_000);
        if (to == from)
            throw new StoryException($"{where}: \"from\" and \"to\" are the same");
        var curveName = StoryRules.Str(c, "curve", where);
        var curve = curveName.Replace(" ", "").ToLowerInvariant() switch
        {
            "" or "linear" => NeedCurve.Linear,
            "inverse" => NeedCurve.Inverse,
            "square" => NeedCurve.Square,
            "inversesquare" => NeedCurve.InverseSquare,
            "root" => NeedCurve.Root,
            _ => throw new StoryException($"{where}: \"curve\" is linear, inverse, square, inverse square or root"),
        };
        var min = c.TryGetProperty("min", out _) ? Number(c, "min", where, 0, 1) : 0;
        return new Consideration(input, what, from, to, curve, min, $"{text}{(curve != NeedCurve.Linear ? $" ({curveName})" : "")}");
    }

    // ---- Casts ----------------------------------------------------------------------------------------------------

    private CastDef ReadCast(StoryRules rules, JsonElement root, string file)
    {
        StoryRules.Keys(root, CastKeys, file);
        if (Home < 0 || WorksAt < 0 || Road < 0 || In < 0)
            throw new StoryException($"{file}: a cast needs the relations Home, WorksAt, Road and In");
        var cast = new CastDef { Name = StoryRules.Str(root, "name", file), About = StoryRules.Str(root, "about", file), File = file };
        if (cast.Name.Length == 0)
            cast.Name = Path.GetFileNameWithoutExtension(file);
        if (Casts.Any(c => c.Name.Equals(cast.Name, StringComparison.OrdinalIgnoreCase)))
            throw new StoryException($"{file}: a cast named {cast.Name} is there already");
        var places = new List<CastDef.Place>();
        foreach (var pl in StoryRules.List(root, "places", file, true))
        {
            var name = StoryRules.Str(pl, "name", $"{file}: a place", true);
            StoryRules.Keys(pl, PlaceKeys, $"{file}: place {name}");
            if (name.Length == 0 || places.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{file}: place {name}: a name, once");
            places.Add(new CastDef.Place(name, StoryRules.Bool(pl, "world", $"{file}: place {name}")));
        }
        if (places.Count(x => x.World) > 1)
            throw new StoryException($"{file}: one place at most is the world");
        bool IsPlace(string n) => places.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        var roads = new List<CastDef.Road>();
        foreach (var r in StoryRules.List(root, "roads", file))
        {
            var where = $"{file}: road {roads.Count + 1}";
            StoryRules.Keys(r, RoadKeys, where);
            var (from, to) = (StoryRules.Str(r, "from", where, true), StoryRules.Str(r, "to", where, true));
            if (!IsPlace(from) || !IsPlace(to) || from.Equals(to, StringComparison.OrdinalIgnoreCase))
                throw new StoryException($"{where}: from and to are two of the cast's places");
            var takes = StoryRules.Duration(StoryRules.Str(r, "takes", where, true)) is { } d and >= 60 and <= 86400 ? d : throw new StoryException($"{where}: \"takes\" is a time from 1m to a day, like \"3m\"");
            roads.Add(new CastDef.Road(from, to, (int)Math.Round(takes / 60)));
        }
        var people = new List<CastDef.Person>();
        foreach (var pe in StoryRules.List(root, "people", file, true))
        {
            var name = StoryRules.Str(pe, "name", $"{file}: a person", true);
            var where = $"{file}: person {name}";
            StoryRules.Keys(pe, PersonKeys, where);
            if (name.Length is 0 or > 40 || people.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) || IsPlace(name))
                throw new StoryException($"{where}: a name of 1 to 40 characters, once (and not a place's)");
            var (home, works) = (StoryRules.Str(pe, "home", where, true), StoryRules.Str(pe, "works", where));
            if (!IsPlace(home) || works.Length > 0 && !IsPlace(works))
                throw new StoryException($"{where}: \"home\" and \"works\" are the cast's places");
            var quals = new List<(ushort, int)>();
            if (pe.TryGetProperty("qualities", out var qs))
            {
                if (qs.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{where}: \"qualities\" is like {{\"Brave\": 70, \"Wealth\": 10}}");
                foreach (var q in qs.EnumerateObject())
                {
                    var qd = rules.Quality(q.Name) ?? throw new StoryException($"{where}: no quality {q.Name}");
                    if (q.Value.ValueKind != JsonValueKind.Number || !q.Value.TryGetInt32(out var v) || v is < -1_000_000 or > 1_000_000)
                        throw new StoryException($"{where}: {q.Name} is a whole number");
                    quals.Add((qd.Id, v));
                }
            }
            people.Add(new CastDef.Person(name, StoryRules.Str(pe, "about", where), home, works, quals.ToArray()));
        }
        if (people.Count is 0 or > 200)
            throw new StoryException($"{file}: 1 to 200 people");
        bool IsSomeone(string n) => IsPlace(n) || people.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        var feelings = new List<CastDef.Feeling>();
        foreach (var f in StoryRules.List(root, "feelings", file))
        {
            var where = $"{file}: feeling {feelings.Count + 1}";
            StoryRules.Keys(f, FeelingKeys, where);
            var (who, to) = (StoryRules.Str(f, "who", where, true), StoryRules.Str(f, "to", where, true));
            if (!IsSomeone(who) || !IsSomeone(to))
                throw new StoryException($"{where}: who and to are the cast's people or places");
            var rel = rules.Relation(StoryRules.Str(f, "rel", where, true)) ?? throw new StoryException($"{where}: no relation {StoryRules.Str(f, "rel", where)}");
            var value = f.TryGetProperty("value", out var v) ? v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n is > 0 and <= 1_000_000 ? n : throw new StoryException($"{where}: \"value\" is a whole number above 0") : 1;
            feelings.Add(new CastDef.Feeling(who, rel.Id, to, value));
        }
        cast.Places = places.ToArray();
        cast.Roads = roads.ToArray();
        cast.People = people.ToArray();
        cast.Feelings = feelings.ToArray();
        return cast;
    }
}
