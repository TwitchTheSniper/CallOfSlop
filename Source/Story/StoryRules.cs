using System.Globalization;
using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>A story data file that can't be used, with the reason (the file and the entry in the message).</summary>
public sealed class StoryException(string message) : Exception(message);

/// <summary>What a story entity is. The window colours by it; the appraiser makes each kind from its own game object.</summary>
public enum StoryKind : byte
{
    Player, // a person playing (found again by their world key)
    Person, // an NPC (phase 2) or a bot
    Faction,
    Place, // a world (map or body), a star system, later a station
    Construct, // a construct someone built (a vehicle, a base, its wreck)
    Thing, // a kind of thing rather than one: a resource, a part, a machine tier (milestones; saved as a byte, so new kinds go last)
}

/// <summary>The roles an event fills. <see cref="Owner"/> is the target's owner when the event happened (ownership can change later).</summary>
public enum StoryRole : byte
{
    Actor,
    Target,
    Owner,
    Place,
    Item,
}

/// <summary>A relationship: (subject, relation, target) with a strength. Exclusive: one target at a time (In). Symmetric: both ways (Knows).</summary>
public sealed class RelationDef
{
    public string Name = "", About = "";
    public ushort Id;
    public bool Exclusive, Symmetric;
    public int Max = 1_000_000; // what adding stops at ("max": a feeling from 0 to 100)
}

/// <summary>A quality: a named number on one entity (Kills, Wealth, Fear).</summary>
public sealed class QualityDef
{
    public string Name = "", About = "";
    public ushort Id;
}

public enum StoryOp : byte
{
    Add,
    Set,
}

/// <summary>
/// One change to the world state, from a verb's <c>effects</c> or a pattern's <c>then</c>: "target Grudge actor +40",
/// "actor Kills +1", "actor In place =1" (=0 removes). Subject and Object are slots: a role for a verb, a variable for a
/// pattern. An effect whose subject is its object, or whose slot is empty, does nothing.
/// </summary>
public readonly record struct StoryEffect(sbyte Subject, ushort What, bool Quality, sbyte Object, StoryOp Op, int Amount);

/// <summary>An action anyone can take: players now, NPCs later (Story/Verbs.json).</summary>
public sealed class VerbDef
{
    public string Name = "", About = "", Sentence = "";
    public ushort Id;
    public StoryEffect[] Effects = [];
    public bool Players, Npcs; // who can take it in this build (the window lists it)
}

/// <summary>A pattern's stage, or one of its <c>unless</c> events: a verb and which variable each role must be (or bind).</summary>
public sealed class PatternStep
{
    public ushort Verb;
    public readonly sbyte[] Roles = [-1, -1, -1, -1, -1]; // per StoryRole: a variable index, -1 any
    public int Story = -1; // a Story event: only this pattern's
    public double Within; // seconds since the stage before (0: no limit)
    public StoryQuery? Where;
    public float Nudge = 1; // how much the drama manager may push for this stage (0: never), times the pattern's drama
    public string Text = ""; // as written (the window)
}

/// <summary>A small story the sifter watches for (Story/Patterns/*.json): stages in order, things that end it, and what it changes when it completes.</summary>
public sealed class PatternDef
{
    public string Name = "", About = "", Tells = "", File = "";
    public ushort Id;
    public string[] Vars = [];
    public PatternStep[] Stages = [];
    public PatternStep[] Unless = [];
    public StoryEffect[] Then = [];
    public sbyte StoryActor = -1, StoryTarget = -1; // which variables the Story event names
    public float Drama = 1; // how much the drama manager wants it told (0: left alone)
}

/// <summary>
/// Everything the story layer reads from data (docs/story/STORY.md): relations and qualities, verbs, patterns. Loaded at
/// start-up; a bad file is refused with a reason. Immutable once loaded.
/// </summary>
public sealed class StoryRules
{
    public const string FolderName = "Story";
    public const int MaxVars = 8, MaxStages = 8;

    public RelationDef[] Relations = [];
    public QualityDef[] Qualities = [];
    public VerbDef[] Verbs = [];
    public PatternDef[] Patterns = [];
    public readonly List<string> Errors = new();

    private readonly Dictionary<string, RelationDef> _rel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, QualityDef> _qual = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VerbDef> _verb = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PatternDef> _pattern = new(StringComparer.OrdinalIgnoreCase);

    public RelationDef? Relation(string name) => _rel.GetValueOrDefault(name);
    public QualityDef? Quality(string name) => _qual.GetValueOrDefault(name);
    public VerbDef? Verb(string name) => _verb.GetValueOrDefault(name);
    public PatternDef? Pattern(string name) => _pattern.GetValueOrDefault(name);

    /// <summary>A verb's id, or -1 when the data has no such verb (the appraiser then leaves that moment out).</summary>
    public int VerbId(string name) => _verb.TryGetValue(name, out var v) ? v.Id : -1;

    /// <summary>The built-in verb a completed pattern records.</summary>
    public ushort StoryVerb;

    public static readonly JsonDocumentOptions Json = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The game's story data (bad files are skipped and listed in <see cref="Errors"/>; a bad Relations or Verbs file leaves an empty rule set).</summary>
    public static StoryRules LoadFolder(string? folder = null)
    {
        folder ??= Path.Combine(AppFolders.Data, FolderName);
        var rules = new StoryRules();
        try
        {
            rules.ReadRelations(ReadFile(Path.Combine(folder, "Relations.json")), "Relations.json");
            rules.ReadVerbs(ReadFile(Path.Combine(folder, "Verbs.json")), "Verbs.json");
        }
        catch (StoryException ex)
        {
            rules.Errors.Add(ex.Message);
            Console.WriteLine($"[story] {ex.Message}: no story rules");
            return new StoryRules { Errors = { ex.Message } }.WithStoryVerb();
        }
        // More relations and verbs in folders of their own (the people's, a mod's): each file adds to the lists, or is
        // refused whole.
        foreach (var (sub, read) in (ReadOnlySpan<(string, Action<string, string>)>)[("Relations", rules.ReadRelations), ("Verbs", rules.ReadVerbs)])
            foreach (var f in Files(Path.Combine(folder, sub)))
                try
                {
                    read(ReadFile(f), $"{sub}/{Path.GetFileName(f)}");
                }
                catch (StoryException ex)
                {
                    rules.Errors.Add(ex.Message);
                    Console.WriteLine($"[story] skipped: {ex.Message}");
                }
        var storyletQualities = StoryletRules.ReadQualities(rules, folder); // (before anything that compiles a query reads them)
        foreach (var f in Files(Path.Combine(folder, "Patterns")))
            try
            {
                rules.ReadPatterns(ReadFile(f), Path.GetFileName(f));
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
                Console.WriteLine($"[story] patterns skipped: {ex.Message}");
            }
        rules.People = PeopleRules.Load(rules, folder);
        rules.Lines = LineRules.Load(rules, folder);
        rules.Moods = MoodRules.Load(rules, folder);
        rules.Plays = PlayRules.Load(rules, folder);
        rules.Places = PlaceRules.Load(rules, folder);
        rules.Factions = FactionRules.Load(rules, folder);
        rules.Storylets = StoryletRules.Load(rules, folder, storyletQualities);
        return rules;
    }

    /// <summary>A folder's .json files in name order (none when it isn't there).</summary>
    internal static string[] Files(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray() : [];

    /// <summary>The people (Story/People, Story/Casts): needs, goals, tasks, reactions and casts. Empty when there are none.</summary>
    public PeopleRules People = new();

    /// <summary>What people say (Story/Lines): the response rules. Empty when there are none.</summary>
    public LineRules Lines = new();

    /// <summary>The people's moods in a fight (Story/Moods): buffs and debuffs. Empty when there are none.</summary>
    public MoodRules Moods = new();

    /// <summary>The plays (Story/Plays): scenes cast from the world. Empty when there are none.</summary>
    public PlayRules Plays = new();

    /// <summary>The states places can be in (Story/Places). Empty when there are none.</summary>
    public PlaceRules Places = new();

    /// <summary>The factions (Story/Factions): who they are, what is theirs and what they remember. Empty when there are none.</summary>
    public FactionRules Factions = new();

    /// <summary>The storylets (Story/Storylets): authored beats that wait for the world to be ready. Empty when there are none.</summary>
    public StoryletRules Storylets = new();

    private StoryRules WithStoryVerb()
    {
        if (_verb.ContainsKey("Story"))
            return this;
        var v = new VerbDef { Name = "Story", About = "a pattern completed", Sentence = "{actor}: {item}", Id = (ushort)Verbs.Length };
        Verbs = [.. Verbs, v];
        _verb[v.Name] = v;
        StoryVerb = v.Id;
        return this;
    }

    internal static string ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new StoryException($"{Path.GetFileName(path)}: can't be read ({ex.Message})");
        }
    }

    internal static JsonDocument Parse(string json, string file)
    {
        try
        {
            var doc = JsonDocument.Parse(json, Json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{file}: expected an object");
            return doc;
        }
        catch (JsonException ex)
        {
            throw new StoryException($"{file}: not valid JSON ({ex.Message})");
        }
    }

    internal static void Keys(JsonElement e, string[] allowed, string where)
    {
        foreach (var p in e.EnumerateObject())
            if (Array.IndexOf(allowed, p.Name) < 0)
                throw new StoryException($"{where}: unknown key \"{p.Name}\" (known: {string.Join(", ", allowed)})");
    }

    internal static string Str(JsonElement e, string key, string where, bool required = false)
    {
        if (!e.TryGetProperty(key, out var v))
            return required ? throw new StoryException($"{where}: \"{key}\" is missing") : "";
        if (v.ValueKind != JsonValueKind.String)
            throw new StoryException($"{where}: \"{key}\" should be text");
        return v.GetString() ?? "";
    }

    internal static bool Bool(JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var v))
            return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new StoryException($"{where}: \"{key}\" should be true or false"),
        };
    }

    internal static IEnumerable<JsonElement> List(JsonElement e, string key, string where, bool required = false)
    {
        if (!e.TryGetProperty(key, out var v))
            return required ? throw new StoryException($"{where}: \"{key}\" is missing") : [];
        if (v.ValueKind != JsonValueKind.Array)
            throw new StoryException($"{where}: \"{key}\" should be a list");
        return v.EnumerateArray().ToArray();
    }

    /// <summary>A word: letters, digits and _ only, a letter first (names are used in effects and queries).</summary>
    internal static bool Word(string s) => s.Length > 0 && char.IsLetter(s[0]) && s.All(c => char.IsLetterOrDigit(c) || c == '_');

    // ---- Relations.json -------------------------------------------------------------------------------------------

    private static readonly string[] RelationsKeys = ["relations", "qualities", "about"];
    private static readonly string[] RelationKeys = ["name", "about", "exclusive", "symmetric", "max"];
    private static readonly string[] QualityKeys = ["name", "about"];

    public void ReadRelations(string json, string file)
    {
        using var doc = Parse(json, file);
        var root = doc.RootElement;
        Keys(root, RelationsKeys, file);
        var rels = Relations.ToList();
        var taken = new HashSet<string>(_rel.Keys.Concat(_qual.Keys), StringComparer.OrdinalIgnoreCase);
        foreach (var r in List(root, "relations", file, Relations.Length == 0))
        {
            if (r.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{file}: each relation should be an object");
            var name = Str(r, "name", $"{file}: a relation", true);
            var where = $"{file}: relation {name}";
            Keys(r, RelationKeys, where);
            if (!Word(name))
                throw new StoryException($"{where}: a name is one word (letters, digits, _)");
            if (!taken.Add(name))
                throw new StoryException($"{where}: named twice (relations and qualities share names)");
            var def = new RelationDef { Name = name, About = Str(r, "about", where), Id = (ushort)rels.Count, Exclusive = Bool(r, "exclusive", where), Symmetric = Bool(r, "symmetric", where) };
            if (r.TryGetProperty("max", out var max))
                def.Max = max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var mv) && mv is >= 1 and <= 1_000_000 ? mv : throw new StoryException($"{where}: \"max\" is a whole number from 1 to 1000000");
            if (def.Exclusive && def.Symmetric)
                throw new StoryException($"{where}: can't be both exclusive and symmetric");
            rels.Add(def);
        }
        var quals = Qualities.ToList();
        foreach (var q in List(root, "qualities", file))
        {
            if (q.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{file}: each quality should be an object");
            var name = Str(q, "name", $"{file}: a quality", true);
            var where = $"{file}: quality {name}";
            Keys(q, QualityKeys, where);
            if (!Word(name))
                throw new StoryException($"{where}: a name is one word (letters, digits, _)");
            if (!taken.Add(name))
                throw new StoryException($"{where}: the name is taken (relations and qualities share names)");
            quals.Add(new QualityDef { Name = name, About = Str(q, "about", where), Id = (ushort)quals.Count });
        }
        if (rels.Count > 4096 || quals.Count > 4096)
            throw new StoryException($"{file}: too many (4096 relations and 4096 qualities at most)");
        foreach (var r in rels)
            _rel[r.Name] = r;
        foreach (var q in quals)
            _qual[q.Name] = q;
        Relations = rels.ToArray();
        Qualities = quals.ToArray();
    }

    /// <summary>A quality declared outside Relations.json (a storylet file's): its id.</summary>
    internal ushort AddQuality(string name, string about)
    {
        var q = new QualityDef { Name = name, About = about, Id = (ushort)Qualities.Length };
        Qualities = [.. Qualities, q];
        _qual[name] = q;
        return q.Id;
    }

    // ---- Verbs.json -----------------------------------------------------------------------------------------------

    private static readonly string[] VerbsKeys = ["verbs", "about"];
    private static readonly string[] VerbKeys = ["name", "about", "sentence", "effects", "players", "npcs"];
    public static readonly string[] RoleNames = ["actor", "target", "owner", "place", "item"];

    public void ReadVerbs(string json, string file)
    {
        using var doc = Parse(json, file);
        var root = doc.RootElement;
        Keys(root, VerbsKeys, file);
        var verbs = Verbs.ToList();
        var added = new Dictionary<string, VerbDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in List(root, "verbs", file, true))
        {
            if (v.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{file}: each verb should be an object");
            var name = Str(v, "name", $"{file}: a verb", true);
            var where = $"{file}: verb {name}";
            Keys(v, VerbKeys, where);
            if (!Word(name))
                throw new StoryException($"{where}: a name is one word (letters, digits, _)");
            if (_verb.ContainsKey(name) || added.ContainsKey(name))
                throw new StoryException($"{where}: named twice");
            var def = new VerbDef { Name = name, About = Str(v, "about", where), Sentence = Str(v, "sentence", where), Id = (ushort)verbs.Count, Players = Bool(v, "players", where), Npcs = Bool(v, "npcs", where) };
            if (def.Sentence.Length == 0)
                def.Sentence = "{actor} " + name.ToLowerInvariant() + " {target}";
            var effects = new List<StoryEffect>();
            foreach (var e in List(v, "effects", where))
            {
                if (e.ValueKind != JsonValueKind.String)
                    throw new StoryException($"{where}: each effect is text, like \"target Grudge actor +40\"");
                effects.Add(ParseEffect(e.GetString() ?? "", where, RoleSlot));
            }
            def.Effects = effects.ToArray();
            verbs.Add(def);
            added[name] = def;
        }
        if (verbs.Count > 60000)
            throw new StoryException($"{file}: too many verbs");
        foreach (var (name, def) in added)
            _verb[name] = def;
        Verbs = verbs.ToArray();
        if (!_verb.TryGetValue("Story", out var story))
            WithStoryVerb();
        else
            StoryVerb = story.Id;
    }

    private static int RoleSlot(string word)
    {
        var i = Array.IndexOf(RoleNames, word.ToLowerInvariant());
        return i;
    }

    /// <summary>
    /// "subject Relation object [+n|-n|=n]" or "subject Quality +n|-n|=n": a slot word, a relation or quality, (an object
    /// slot,) and an amount. With no amount a relation is set to 1.
    /// </summary>
    public StoryEffect ParseEffect(string text, string where, Func<string, int> slot)
    {
        var t = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = $"{where}: effect \"{text}\"";
        if (t.Length < 2)
            throw new StoryException($"{at}: too short (like \"target Grudge actor +40\" or \"actor Kills +1\")");
        var subject = slot(t[0]);
        if (subject < 0)
            throw new StoryException($"{at}: \"{t[0]}\" isn't a role or variable here");
        if (_qual.TryGetValue(t[1], out var q))
        {
            if (t.Length != 3)
                throw new StoryException($"{at}: a quality takes an amount: \"{t[0]} {q.Name} +1\"");
            var (op, n) = Amount(t[2], at);
            return new StoryEffect((sbyte)subject, q.Id, true, -1, op, n);
        }
        if (!_rel.TryGetValue(t[1], out var r))
            throw new StoryException($"{at}: \"{t[1]}\" isn't a relation or quality (Relations.json)");
        if (t.Length is < 3 or > 4)
            throw new StoryException($"{at}: a relation needs an object: \"{t[0]} {r.Name} target +20\"");
        var obj = slot(t[2]);
        if (obj < 0)
            throw new StoryException($"{at}: \"{t[2]}\" isn't a role or variable here");
        var (rop, rn) = t.Length == 4 ? Amount(t[3], at) : (StoryOp.Set, 1);
        return new StoryEffect((sbyte)subject, r.Id, false, (sbyte)obj, rop, rn);
    }

    private static (StoryOp, int) Amount(string s, string at)
    {
        if (s.Length >= 2 && s[0] is '+' or '-' or '=' && int.TryParse(s.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n <= 1_000_000)
            return s[0] switch
            {
                '+' => (StoryOp.Add, n),
                '-' => (StoryOp.Add, -n),
                _ => (StoryOp.Set, n),
            };
        throw new StoryException($"{at}: \"{s}\" should be +n, -n or =n");
    }

    // ---- Patterns/*.json ------------------------------------------------------------------------------------------

    private static readonly string[] PatternsKeys = ["patterns", "about"];
    private static readonly string[] PatternKeys = ["name", "about", "tells", "stages", "unless", "then", "story", "drama"];
    private static readonly string[] StepKeys = ["verb", "actor", "target", "owner", "place", "item", "within", "where", "pattern", "nudge"];
    private static readonly string[] StoryKeys = ["actor", "target"];

    public void ReadPatterns(string json, string file)
    {
        using var doc = Parse(json, file);
        var root = doc.RootElement;
        Keys(root, PatternsKeys, file);
        var list = Patterns.ToList();
        var added = new List<PatternDef>();
        foreach (var p in List(root, "patterns", file, true))
        {
            if (p.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{file}: each pattern should be an object");
            var name = Str(p, "name", $"{file}: a pattern", true);
            var where = $"{file}: pattern {name}";
            Keys(p, PatternKeys, where);
            if (name.Length == 0 || name.Length > 48)
                throw new StoryException($"{where}: a name is 1 to 48 characters");
            if (_pattern.ContainsKey(name) || added.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new StoryException($"{where}: named twice");
            var vars = new List<string>();
            var def = new PatternDef { Name = name, About = Str(p, "about", where), Tells = Str(p, "tells", where), File = file, Id = (ushort)(list.Count + added.Count) };
            var stages = List(p, "stages", where, true).Select((s, i) => ReadStep(s, $"{where}: stage {i + 1}", vars, true, list.Concat(added).Append(def))).ToArray();
            if (stages.Length is 0 or > MaxStages)
                throw new StoryException($"{where}: 1 to {MaxStages} stages");
            if (stages[0].Within > 0)
                throw new StoryException($"{where}: stage 1 can't have \"within\" (it starts the story)");
            if (stages[0].Nudge != 1)
                throw new StoryException($"{where}: stage 1 can't have \"nudge\" (it starts the story, so nothing waits for it)");
            var unless = List(p, "unless", where).Select((s, i) => ReadStep(s, $"{where}: unless {i + 1}", vars, false, list.Concat(added).Append(def))).ToArray();
            def.Stages = stages;
            def.Unless = unless;
            if (p.TryGetProperty("drama", out var dr))
                def.Drama = dr.ValueKind == JsonValueKind.Number && dr.TryGetSingle(out var dv) && dv is >= 0 and <= 10 ? dv : throw new StoryException($"{where}: \"drama\" is a number from 0 (the drama manager leaves it alone) to 10");
            foreach (var u in unless)
                if (u.Nudge != 1)
                    throw new StoryException($"{where}: \"nudge\" goes on a stage, not an \"unless\"");
            def.Vars = vars.ToArray();
            int Var(string w) => w.StartsWith('?') ? vars.IndexOf(w[1..]) : -1;
            def.Then = List(p, "then", where).Select(e => e.ValueKind == JsonValueKind.String
                ? ParseEffect(e.GetString() ?? "", where, Var)
                : throw new StoryException($"{where}: each \"then\" is text, like \"?b Grudge ?a =0\"")).ToArray();
            if (p.TryGetProperty("story", out var st))
            {
                if (st.ValueKind != JsonValueKind.Object)
                    throw new StoryException($"{where}: \"story\" should be {{\"actor\": \"?a\", \"target\": \"?b\"}}");
                Keys(st, StoryKeys, $"{where}: story");
                def.StoryActor = (sbyte)Var(Str(st, "actor", where));
                def.StoryTarget = (sbyte)Var(Str(st, "target", where));
                if (st.TryGetProperty("actor", out _) && def.StoryActor < 0 || st.TryGetProperty("target", out _) && def.StoryTarget < 0)
                    throw new StoryException($"{where}: \"story\" names a variable no stage binds");
            }
            else
                def.StoryActor = (sbyte)(vars.Count > 0 ? 0 : -1);
            foreach (var u in def.Unless)
                for (var r = 0; r < u.Roles.Length; r++)
                    if (u.Roles[r] >= 0 && !Bound(def.Stages, u.Roles[r]))
                        throw new StoryException($"{where}: an \"unless\" uses ?{vars[u.Roles[r]]}, which no stage binds");
            foreach (var t in def.Tells.Split('{').Skip(1))
            {
                var close = t.IndexOf('}');
                if (close < 0 || !vars.Contains(t[..close]))
                    throw new StoryException($"{where}: \"tells\" names {{{(close < 0 ? t : t[..close])}}}, which isn't a variable");
            }
            added.Add(def);
        }
        foreach (var a in added)
            _pattern[a.Name] = a;
        Patterns = [.. list, .. added];
        if (Patterns.Length > 1000)
            throw new StoryException($"{file}: more than 1000 patterns in all");
    }

    private static bool Bound(PatternStep[] stages, int v)
    {
        foreach (var s in stages)
            if (Array.IndexOf(s.Roles, (sbyte)v) >= 0)
                return true;
        return false;
    }

    private PatternStep ReadStep(JsonElement s, string where, List<string> vars, bool binds, IEnumerable<PatternDef> patterns)
    {
        if (s.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: should be an object, like {{\"verb\": \"Kill\", \"actor\": \"?a\", \"target\": \"?b\"}}");
        Keys(s, StepKeys, where);
        var verbName = Str(s, "verb", where, true);
        if (!_verb.TryGetValue(verbName, out var verb))
            throw new StoryException($"{where}: no verb \"{verbName}\" (Verbs.json)");
        var step = new PatternStep { Verb = verb.Id, Text = verbName };
        for (var r = 0; r < RoleNames.Length; r++)
        {
            var w = Str(s, RoleNames[r], where);
            if (w.Length == 0)
                continue;
            if (!w.StartsWith('?') || !Word(w[1..]))
                throw new StoryException($"{where}: \"{RoleNames[r]}\" should be a variable like \"?a\"");
            var name = w[1..];
            var i = vars.IndexOf(name);
            if (i < 0)
            {
                if (!binds)
                    throw new StoryException($"{where}: ?{name} isn't bound by any stage before it");
                if (vars.Count >= MaxVars)
                    throw new StoryException($"{where}: more than {MaxVars} variables");
                vars.Add(name);
                i = vars.Count - 1;
            }
            step.Roles[r] = (sbyte)i;
            step.Text += $" {RoleNames[r]} {w}";
        }
        var pattern = Str(s, "pattern", where);
        if (pattern.Length > 0)
        {
            if (verb.Id != StoryVerb)
                throw new StoryException($"{where}: \"pattern\" goes with the verb Story");
            step.Story = patterns.FirstOrDefault(p => p.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase))?.Id
                ?? throw new StoryException($"{where}: no pattern \"{pattern}\" (patterns load in file order)");
            step.Text += $" ({pattern})";
        }
        var within = Str(s, "within", where);
        if (within.Length > 0)
        {
            step.Within = Duration(within) ?? throw new StoryException($"{where}: \"within\" should be a time like \"90s\", \"5m\", \"2h\" or \"1h30m\"");
            step.Text += $" within {within}";
        }
        if (s.TryGetProperty("nudge", out var nu))
            step.Nudge = nu.ValueKind == JsonValueKind.Number && nu.TryGetSingle(out var nv) && nv is >= 0 and <= 10 ? nv : throw new StoryException($"{where}: \"nudge\" is a number from 0 (never nudged) to 10");
        var whereText = Str(s, "where", where);
        if (whereText.Length > 0)
        {
            try
            {
                step.Where = StoryQuery.Compile(whereText, this, vars, binds ? MaxVars : vars.Count);
            }
            catch (StoryException ex)
            {
                throw new StoryException($"{where}: where: {ex.Message}");
            }
            step.Text += $" where {whereText}";
        }
        return step;
    }

    /// <summary>"90s", "5m", "2h", "1h30m", "1d" (seconds), or a plain number of seconds. Null when it doesn't read.</summary>
    public static double? Duration(string s)
    {
        s = s.Trim();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
            return plain >= 0 && double.IsFinite(plain) ? plain : null;
        double total = 0;
        var num = 0.0;
        var digits = false;
        foreach (var c in s)
        {
            if (char.IsAsciiDigit(c))
            {
                num = num * 10 + (c - '0');
                digits = true;
                continue;
            }
            if (!digits)
                return null;
            var unit = char.ToLowerInvariant(c) switch
            {
                's' => 1.0,
                'm' => 60.0,
                'h' => 3600.0,
                'd' => 86400.0,
                _ => -1,
            };
            if (unit < 0)
                return null;
            total += num * unit;
            num = 0;
            digits = false;
        }
        return digits ? null : total;
    }

    /// <summary>A time shown in the window and the CLI: "1:05:00" (hours, minutes, seconds).</summary>
    public static string Clock(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var t = (long)seconds;
        return $"{t / 3600}:{t / 60 % 60:00}:{t % 60:00}";
    }

    /// <summary>A length of time in words: "2h", "45m", "30s".</summary>
    public static string Span(double seconds)
    {
        if (seconds >= 3600)
            return seconds % 3600 < 60 ? $"{(int)(seconds / 3600)}h" : $"{(int)(seconds / 3600)}h{(int)(seconds % 3600 / 60)}m";
        if (seconds >= 60)
            return $"{(int)(seconds / 60)}m";
        return $"{(int)Math.Max(0, seconds)}s";
    }
}
