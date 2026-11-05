using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>
/// One response rule (docs/story/STORY.md "Lines"; Valve's dialogue rules): an occasion (<see cref="Concept"/>), what must
/// hold (the verb, what it is about, a query), and lines to say. Of the rules for an occasion whose criteria all hold, the
/// one with the most criteria (<see cref="Weight"/>) is said; ties are drawn at random.
/// </summary>
public sealed class LineRule
{
    public const int Me = 0, Them = 1, Actor = 2, Target = 3, Place = 4, Here = 5, Owner = 6; // its query's variables
    public static readonly List<string> Vars = ["me", "them", "actor", "target", "place", "here", "owner"];

    public int Id, Concept;
    public int Verb = -1, About = -1; // the occasion's verb (a step's, an event's), and the verb of what it is about (news)
    public StoryQuery? If;
    public int Weight; // criteria: the verb, what about, and each clause of the query
    public string[] Lines = [];
    public float Chance = 1; // the rule wins and then says nothing this often (1 - chance)
    public double Every; // s: one speaker says this rule at most this often
    public bool Once; // once per speaker and listener (this session)
    public int Reply = -1; // the occasion the one spoken to answers on, a moment later
    public int Needs; // the names its lines fill, a bit each (LineRules.Fills): it isn't said when one of them is missing
    public string File = "", Text = "";
}

/// <summary>
/// The response rules from Story/Lines/*.json: what people say, by occasion. Occasions are names; the built-in ones (the
/// game and the engine ask for them) are in <see cref="Builtin"/>, and a rule's "reply" may name any other (its rules
/// are then that occasion's). A bad file is refused whole with a reason (in <see cref="StoryRules.Errors"/>).
/// </summary>
public sealed class LineRules
{
    public const int MaxRules = 2000, MaxLines = 24, MaxLength = 110; // a line fits the chat (120) with a name in front

    /// <summary>
    /// The occasions the code asks for: a step of theirs (when their bodies meet, or as it starts headless), an event
    /// they saw happen, news they were told (the reply to a Tell), a player coming up to them, being hurt, and a radio
    /// broadcast of theirs.
    /// </summary>
    public static readonly string[] Builtin = ["Step", "Saw", "Heard", "Near", "Hurt", "Radio"];

    public const int Step = 0, Saw = 1, Heard = 2, Near = 3, Hurt = 4, Radio = 5;

    public List<string> Concepts = [.. Builtin];
    public LineRule[] Rules = [];
    public int[][] OfConcept = []; // per concept, its rules, the most specific first

    public int Concept(string name) => Concepts.FindIndex(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["about", "rules"];
    private static readonly string[] RuleKeys = ["on", "verb", "about", "if", "say", "chance", "every", "once", "reply"];

    /// <summary>Story/Lines/*.json under <paramref name="folder"/>. Errors go to the rules' list.</summary>
    public static LineRules Load(StoryRules rules, string folder)
    {
        var lr = new LineRules();
        var all = new List<LineRule>();
        foreach (var f in StoryRules.Files(Path.Combine(folder, "Lines")))
        {
            var name = $"Lines/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), name);
                var concepts = lr.Concepts.Count;
                try
                {
                    all.AddRange(lr.Read(rules, doc.RootElement, name, all.Count));
                }
                catch (StoryException)
                {
                    lr.Concepts.RemoveRange(concepts, lr.Concepts.Count - concepts); // (the file is refused whole)
                    throw;
                }
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
            }
        }
        lr.Rules = all.ToArray();
        lr.OfConcept = Enumerable.Range(0, lr.Concepts.Count)
            .Select(c => lr.Rules.Where(r => r.Concept == c).OrderByDescending(r => r.Weight).Select(r => r.Id).ToArray()).ToArray();
        return lr;
    }

    private List<LineRule> Read(StoryRules rules, JsonElement root, string file, int first)
    {
        StoryRules.Keys(root, FileKeys, file);
        var list = new List<LineRule>();
        foreach (var r in StoryRules.List(root, "rules", file, true))
        {
            var where = $"{file}: rule {list.Count + 1}";
            if (r.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{where}: a rule is like {{\"on\": \"Step\", \"verb\": \"Greet\", \"say\": [\"Hello.\"]}}");
            StoryRules.Keys(r, RuleKeys, where);
            var on = StoryRules.Str(r, "on", where, true);
            if (!StoryRules.Word(on))
                throw new StoryException($"{where}: \"on\" is an occasion, one word ({string.Join(", ", Builtin)}, or a reply's)");
            var def = new LineRule { Id = first + list.Count, Concept = Intern(on), File = file };
            var verb = StoryRules.Str(r, "verb", where);
            if (verb.Length > 0)
                def.Verb = rules.Verb(verb)?.Id ?? throw new StoryException($"{where}: no verb {verb}");
            var about = StoryRules.Str(r, "about", where);
            if (about.Length > 0)
                def.About = rules.Verb(about)?.Id ?? throw new StoryException($"{where}: \"about\": no verb {about}");
            var text = StoryRules.Str(r, "if", where);
            if (text.Length > 0)
                try
                {
                    def.If = StoryQuery.Compile(text, rules, [.. LineRule.Vars]); // (one more variable of its own, like ?x)
                }
                catch (StoryException ex)
                {
                    throw new StoryException($"{where}: \"{text}\": {ex.Message}");
                }
            def.Weight = (def.Verb >= 0 ? 1 : 0) + (def.About >= 0 ? 1 : 0) + (def.If?.Clauses.Length ?? 0);
            // The occasion's names its query reads must be there too (an unbound ?them would match anyone).
            foreach (var cl in def.If?.Clauses ?? [])
            {
                if (cl.A.Var is >= 0 and < 7)
                    def.Needs |= 1 << cl.A.Var;
                if (cl.Kind is StoryQuery.ClauseKind.Relation or StoryQuery.ClauseKind.Same or StoryQuery.ClauseKind.Different && cl.B.Var is >= 0 and < 7)
                    def.Needs |= 1 << cl.B.Var;
            }
            def.Lines = StoryRules.List(r, "say", where, true).Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : throw new StoryException($"{where}: each of \"say\" is a line")).ToArray();
            if (def.Lines.Length is 0 or > MaxLines)
                throw new StoryException($"{where}: \"say\" has 1 to {MaxLines} lines");
            foreach (var line in def.Lines)
                def.Needs |= Check(line, where);
            if (r.TryGetProperty("chance", out var ch))
                def.Chance = ch.ValueKind == JsonValueKind.Number && ch.TryGetSingle(out var c) && c is > 0 and <= 1 ? c : throw new StoryException($"{where}: \"chance\" is a number above 0, up to 1");
            var every = StoryRules.Str(r, "every", where);
            if (every.Length > 0)
                def.Every = StoryRules.Duration(every) is { } d and > 0 and <= 86400 ? d : throw new StoryException($"{where}: \"every\" is a time like \"90s\" or \"10m\"");
            def.Once = StoryRules.Bool(r, "once", where);
            var reply = StoryRules.Str(r, "reply", where);
            if (reply.Length > 0)
            {
                if (!StoryRules.Word(reply))
                    throw new StoryException($"{where}: \"reply\" is an occasion, one word");
                def.Reply = Intern(reply);
            }
            def.Text = $"on {on}{(verb.Length > 0 ? $" {verb}" : "")}{(about.Length > 0 ? $" about {about}" : "")}{(text.Length > 0 ? $" if {text}" : "")}";
            list.Add(def);
            if (first + list.Count > MaxRules)
                throw new StoryException($"{file}: too many rules ({MaxRules} at most in all)");
        }
        return list;
    }

    private int Intern(string concept)
    {
        var c = Concept(concept);
        if (c >= 0)
            return c;
        Concepts.Add(concept);
        return Concepts.Count - 1;
    }

    /// <summary>The names a line can fill, in <see cref="LineRule.Needs"/>'s bit order.</summary>
    public static readonly string[] Fills = ["me", "them", "actor", "target", "place", "here", "owner", "news"];

    /// <summary>A line as written: printable ASCII, short enough for the chat, and only the names it can fill (returned, a bit each).</summary>
    private static int Check(string line, string where)
    {
        var needs = 0;
        if (line.Length == 0 || line.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: \"{line}\": a line is printable ASCII");
        for (var i = line.IndexOf('{'); i >= 0; i = line.IndexOf('{', i + 1))
        {
            var end = line.IndexOf('}', i);
            var name = end > i ? line[(i + 1)..end] : "";
            var bare = name.EndsWith(".first", StringComparison.Ordinal) ? name[..^6] : name;
            if (Array.IndexOf(Fills, bare) < 0 || bare == "news" && bare != name)
                throw new StoryException($"{where}: \"{line}\": {{{name}}} isn't a name it fills ({{me}}, {{them}}, {{actor}}, {{target}}, {{place}}, {{here}}, {{owner}}, {{news}}; add .first for a first name)");
            needs |= 1 << Array.IndexOf(Fills, bare);
        }
        if (line.Length > MaxLength)
            throw new StoryException($"{where}: \"{line}\": {MaxLength} characters at most");
        return needs;
    }
}
