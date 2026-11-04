using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>
/// A scripted run of events (Story/Scenarios/*.json): entities to make, events at times, and how many of each story it
/// should complete. <c>--story-sim</c> plays one headless on its own clock; the Story window plays one into the live story
/// world, an event every <see cref="LiveGap"/> seconds, marked as scripted. Its entities are its own (made fresh each
/// run), so a scripted "Karl" never touches the real one.
/// </summary>
public sealed class StoryScenario
{
    public const string FolderName = "Scenarios";
    public const double LiveGap = 0.75;

    public sealed class Thing
    {
        public string Name = "", Owner = "";
        public StoryKind Kind;
    }

    public sealed class Step
    {
        public double At;
        public int Verb;
        public readonly string[] Roles = ["", "", "", "", ""]; // per StoryRole: an entity's name, "" none
        public string Text = "";
    }

    public string Name = "", About = "", File = "";
    public Thing[] Things = [];
    public Step[] Steps = [];
    public (string Pattern, int Count)[] Expect = [];

    private static readonly string[] Keys = ["name", "about", "entities", "events", "expect"];
    private static readonly string[] ThingKeys = ["name", "kind", "owner"];
    private static readonly string[] StepKeys = ["at", "verb", "actor", "target", "owner", "place", "item"];

    /// <summary>Every scenario in the folder (bad ones are skipped, with the reason in <paramref name="errors"/>).</summary>
    public static List<StoryScenario> LoadFolder(StoryRules rules, string? folder, List<string> errors)
    {
        folder ??= Path.Combine(AppFolders.Data, StoryRules.FolderName, FolderName);
        var list = new List<StoryScenario>();
        if (!Directory.Exists(folder))
            return list;
        foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            try
            {
                list.Add(Load(rules, System.IO.File.ReadAllText(f), Path.GetFileName(f)));
            }
            catch (Exception ex) when (ex is StoryException or IOException)
            {
                errors.Add(ex.Message);
            }
        return list;
    }

    public static StoryScenario Load(StoryRules rules, string json, string file)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, StoryRules.Json);
        }
        catch (JsonException ex)
        {
            throw new StoryException($"{file}: not valid JSON ({ex.Message})");
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{file}: expected an object");
            StoryRules.Keys(root, Keys, file);
            var s = new StoryScenario { File = file, Name = StoryRules.Str(root, "name", file), About = StoryRules.Str(root, "about", file) };
            if (s.Name.Length == 0)
                s.Name = Path.GetFileNameWithoutExtension(file);
            var things = new List<Thing>();
            if (root.TryGetProperty("entities", out var ents))
            {
                if (ents.ValueKind != JsonValueKind.Array)
                    throw new StoryException($"{file}: \"entities\" should be a list");
                foreach (var e in ents.EnumerateArray())
                {
                    var name = StoryRules.Str(e, "name", $"{file}: an entity", true);
                    var where = $"{file}: entity {name}";
                    StoryRules.Keys(e, ThingKeys, where);
                    if (name.Length == 0 || things.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        throw new StoryException($"{where}: a name, and only once");
                    if (!Enum.TryParse<StoryKind>(StoryRules.Str(e, "kind", where, true), true, out var kind))
                        throw new StoryException($"{where}: \"kind\" is one of {string.Join(", ", Enum.GetNames<StoryKind>())}");
                    things.Add(new Thing { Name = name, Kind = kind, Owner = StoryRules.Str(e, "owner", where) });
                }
            }
            foreach (var t in things)
                if (t.Owner.Length > 0 && !things.Any(o => o.Name.Equals(t.Owner, StringComparison.OrdinalIgnoreCase)))
                    throw new StoryException($"{file}: entity {t.Name}: its owner {t.Owner} isn't in \"entities\"");
            s.Things = things.ToArray();
            var steps = new List<Step>();
            if (!root.TryGetProperty("events", out var evs) || evs.ValueKind != JsonValueKind.Array)
                throw new StoryException($"{file}: \"events\" should be a list");
            var i = 0;
            foreach (var e in evs.EnumerateArray())
            {
                var where = $"{file}: event {++i}";
                StoryRules.Keys(e, StepKeys, where);
                var verb = StoryRules.Str(e, "verb", where, true);
                var step = new Step { Verb = rules.VerbId(verb) };
                if (step.Verb < 0)
                    throw new StoryException($"{where}: no verb \"{verb}\" (Verbs.json)");
                var at = StoryRules.Str(e, "at", where, true);
                step.At = StoryRules.Duration(at) ?? throw new StoryException($"{where}: \"at\" should be a time from the start, like \"0s\", \"5m\" or \"1h20m\"");
                if (steps.Count > 0 && step.At < steps[^1].At)
                    throw new StoryException($"{where}: \"at\" goes back in time ({at})");
                step.Text = $"{at} {verb}";
                for (var r = 0; r < StoryRules.RoleNames.Length; r++)
                {
                    var who = StoryRules.Str(e, StoryRules.RoleNames[r], where);
                    if (who.Length > 0 && !things.Any(t => t.Name.Equals(who, StringComparison.OrdinalIgnoreCase)))
                        throw new StoryException($"{where}: \"{who}\" isn't in \"entities\"");
                    step.Roles[r] = who;
                }
                steps.Add(step);
            }
            s.Steps = steps.ToArray();
            if (root.TryGetProperty("expect", out var ex))
            {
                if (ex.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{file}: \"expect\" should be {{\"pattern name\": count}}");
                var expect = new List<(string, int)>();
                foreach (var p in ex.EnumerateObject())
                {
                    if (rules.Pattern(p.Name) == null)
                        throw new StoryException($"{file}: expect: no pattern \"{p.Name}\"");
                    if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var n) || n < 0)
                        throw new StoryException($"{file}: expect: {p.Name} should be a count");
                    expect.Add((p.Name, n));
                }
                s.Expect = expect.ToArray();
            }
            return s;
        }
    }
}

/// <summary>A scenario being played into an engine: its own entities (by name), the next event, and when it began.</summary>
public sealed class StoryScenarioRun
{
    public readonly StoryScenario Scenario;
    private readonly Dictionary<string, int> _ids = new(StringComparer.OrdinalIgnoreCase);
    public int Next;
    public readonly double Began;
    private double _nextLive;

    public bool Finished => Next >= Scenario.Steps.Length;

    public StoryScenarioRun(StoryScenario scenario, StoryEngine engine)
    {
        Scenario = scenario;
        Began = engine.Time;
        _nextLive = engine.Time;
        var owns = engine.Rules.Relation("Owns")?.Id ?? -1;
        foreach (var t in scenario.Things)
            _ids[t.Name] = engine.World.Create(t.Kind, t.Name, 0, engine.Time, scripted: true);
        foreach (var t in scenario.Things)
            if (t.Owner.Length > 0 && owns >= 0)
                engine.World.Set(_ids[t.Owner], owns, _ids[t.Name], 1);
    }

    public int Id(string name) => name.Length > 0 && _ids.TryGetValue(name, out var id) ? id : -1;

    /// <summary>Records one step now. An empty owner is the target's owner (the holder of Owns), as the game fills it.</summary>
    public int Play(StoryEngine engine)
    {
        var s = Scenario.Steps[Next++];
        Span<int> roles = stackalloc int[5];
        for (var r = 0; r < 5; r++)
            roles[r] = Id(s.Roles[r]);
        if (roles[(int)StoryRole.Owner] < 0 && roles[(int)StoryRole.Target] >= 0 && engine.Rules.Relation("Owns") is { } owns)
        {
            var (a, b) = engine.World.HeldRange(roles[(int)StoryRole.Target], owns.Id);
            if (b > a)
                roles[(int)StoryRole.Owner] = (int)(uint)engine.World[roles[(int)StoryRole.Target]].Held[a];
        }
        return engine.Record(s.Verb, roles[0], roles[1], roles[2], roles[3], roles[4], scripted: true);
    }

    /// <summary>Headless: the clock moves to the next step's time (limits run out on the way) and it is played.</summary>
    public int PlayNextOnClock(StoryEngine engine)
    {
        var due = Began + Scenario.Steps[Next].At;
        if (due > engine.Time)
            engine.Advance(due - engine.Time);
        return Play(engine);
    }

    /// <summary>In the game: one step every <see cref="StoryScenario.LiveGap"/> seconds of the live clock.</summary>
    public void Live(StoryEngine engine)
    {
        while (!Finished && engine.Time >= _nextLive)
        {
            Play(engine);
            _nextLive = engine.Time + StoryScenario.LiveGap;
        }
    }
}
