using System.Text;
using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>One way a person answers a topic: the first of a topic's answers whose "if" holds is the one given.</summary>
public sealed class TopicAnswer
{
    public StoryQuery? If; // over ?me (the person) and ?them (the player); null: always
    public string IfText = "";
    public string[] Say = []; // one is said (never the same twice running)
    public int Line = -1; // instead of "say": a Lines occasion the person answers on (Story/Lines), like "AskPlace"
    public StoryEffect[] Then = []; // what it changes (slots: me, them, the topic's named things)
    public int Learn = -1; // a named thing the player learns of (a Lead, and a Learn event from the person), or -1
    public bool Keep; // the topic isn't counted as asked: it stays on offer as it was (a brush-off, "not yet")
}

/// <summary>
/// A thing a player can ask one of the story's people about in the talk panel (docs/story/STORY.md "Topics"): its own
/// button, offered when its "if" holds for that person and that player, gone once asked (unless "again"), and the way
/// into the ones that follow it ("after"). Each person has their own; who they are, how they stand to the player and
/// how far the player's story has gone decide what they will talk about and how.
/// </summary>
public sealed class TopicDef
{
    public string Id = "", Ask = "", Who = "", File = "", About = "";
    public int Index;
    public ulong Key; // the topic's Thing ("topic:ID"), which the Asked relation points at
    public StoryQuery? If;
    public string IfText = "";
    public string[] AfterIds = [];
    public int[] After = []; // topics the player must have asked (of anyone) first: any one of them
    public bool Again; // stays on offer after it was asked
    public int Priority; // higher first (a follow-up usually ranks above what led to it)
    public TopicAnswer[] Answers = [];
    public string[] Named = []; // the named things' names (slots 2 on)
}

/// <summary>The topics from Story/Topics/*.json (format <see cref="Format"/>). A bad file is refused whole with a reason.</summary>
public sealed class TopicRules
{
    public const string Format = "call-of-slop-topics/1";
    public const int MaxTopics = 2000, MaxAnswers = 8, MaxAsk = 48, Offered = 4;

    public TopicDef[] Topics = [];
    public int Asked = -1, Lead = -1, VerbLearn = -1;

    public TopicDef? Find(string id) => Array.Find(Topics, t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["format", "about", "topics"];
    private static readonly string[] TopicKeys = ["id", "who", "ask", "about", "if", "after", "again", "priority", "answers"];
    private static readonly string[] AnswerKeys = ["if", "say", "line", "then", "learn", "keep"];

    /// <summary>Story/Topics/*.json under <paramref name="folder"/>. Errors go to the rules' list.</summary>
    public static TopicRules Load(StoryRules rules, string folder)
    {
        var tr = new TopicRules
        {
            Asked = rules.Relation("Asked")?.Id ?? -1,
            Lead = rules.Relation("Lead")?.Id ?? -1,
            VerbLearn = rules.VerbId("Learn"),
        };
        var files = StoryRules.Files(Path.Combine(folder, "Topics"));
        if (files.Length == 0)
            return tr;
        if (tr.Asked < 0)
        {
            rules.Errors.Add("Topics: the relation Asked is missing (Story/Relations/Talk.json): no topics");
            return tr;
        }
        var all = new List<TopicDef>();
        foreach (var f in files)
        {
            var file = $"Topics/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), file);
                var root = doc.RootElement;
                StoryRules.Keys(root, FileKeys, file);
                var format = StoryRules.Str(root, "format", file, true);
                if (format != Format)
                    throw new StoryException($"{file}: \"format\" is \"{format}\": this build reads \"{Format}\"");
                var mine = new List<TopicDef>();
                foreach (var e in StoryRules.List(root, "topics", file, true))
                    mine.Add(Read(rules, e, file, all.Count + mine.Count, all.Concat(mine)));
                all.AddRange(mine);
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
                Console.WriteLine($"[story] topics skipped: {ex.Message}");
            }
        }
        if (all.Count > MaxTopics)
        {
            rules.Errors.Add($"Topics: {all.Count} topics, {MaxTopics} at most: none loaded");
            return tr;
        }
        // "after" names topics from any file; one that isn't there is dropped with a reason (the topic then waits for nothing).
        foreach (var t in all)
        {
            var after = new List<int>();
            foreach (var id in t.AfterIds)
                if (all.FindIndex(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) is var i and >= 0)
                    after.Add(i);
                else
                    rules.Errors.Add($"{t.File}: topic {t.Id}: \"after\": no topic {id}");
            t.After = after.ToArray();
        }
        tr.Topics = all.ToArray();
        return tr;
    }

    private static TopicDef Read(StoryRules rules, JsonElement e, string file, int index, IEnumerable<TopicDef> before)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{file}: a topic is like {{\"id\": \"handbook\", \"who\": \"Warden Pettigrew\", \"ask\": \"About the handbook...\", \"answers\": [...]}}");
        var id = StoryRules.Str(e, "id", $"{file}: a topic", true);
        var where = $"{file}: topic {id}";
        StoryRules.Keys(e, TopicKeys, where);
        if (id.Length is 0 or > 48 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new StoryException($"{where}: an id is letters, digits, - and _, at most 48");
        if (before.Any(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new StoryException($"{where}: another topic has this id");
        var t = new TopicDef
        {
            Id = id, Index = index, File = file, Key = StoryFactions.Key("topic:" + id),
            Who = StoryRules.Str(e, "who", where), About = StoryRules.Str(e, "about", where),
            Ask = StoryRules.Str(e, "ask", where, true), Again = StoryRules.Bool(e, "again", where),
        };
        if (t.Ask.Length is 0 or > MaxAsk || t.Ask.Any(c => c < ' ' || c > '~') || t.Ask.Contains('#') || t.Ask.Contains('%'))
            throw new StoryException($"{where}: \"ask\" is printable ASCII, 1 to {MaxAsk} characters, no # or %");
        if (e.TryGetProperty("priority", out var pv))
            t.Priority = pv.ValueKind == JsonValueKind.Number && pv.TryGetInt32(out var p) && p is >= -100 and <= 100 ? p : throw new StoryException($"{where}: \"priority\" is a whole number from -100 to 100");
        if (e.TryGetProperty("after", out var av))
            t.AfterIds = av.ValueKind switch
            {
                JsonValueKind.String => [av.GetString() ?? ""],
                JsonValueKind.Array => av.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : throw new StoryException($"{where}: \"after\" names topics")).ToArray(),
                _ => throw new StoryException($"{where}: \"after\" is a topic's id, or a list of them"),
            };
        var cond = StoryRules.Str(e, "if", where);
        if (cond.Length > 0)
            (t.If, t.IfText) = (Compile(cond, rules, where, "if"), cond);

        var named = new List<string>();
        int Slot(string s)
        {
            if (s == "?me")
                return 0;
            if (s == "?them")
                return 1;
            return s.StartsWith('#') && int.TryParse(s.AsSpan(1), out var n) ? 2 + n : -1;
        }
        var answers = new List<TopicAnswer>();
        foreach (var x in StoryRules.List(e, "answers", where, true))
        {
            var at = $"{where}: answer {answers.Count + 1}";
            if (x.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at}: an answer is like {{\"if\": \"?me Likes ?them >= 20\", \"say\": [\"...\"]}}");
            StoryRules.Keys(x, AnswerKeys, at);
            var a = new TopicAnswer { Keep = StoryRules.Bool(x, "keep", at) };
            var ac = StoryRules.Str(x, "if", at);
            if (ac.Length > 0)
                (a.If, a.IfText) = (Compile(ac, rules, at, "if"), ac);
            a.Say = StoryRules.List(x, "say", at).Select(v => v.ValueKind == JsonValueKind.String ? CheckLine(v.GetString() ?? "", at) : throw new StoryException($"{at}: each of \"say\" is a line")).ToArray();
            if (a.Say.Length > LineRules.MaxLines)
                throw new StoryException($"{at}: {LineRules.MaxLines} lines at most");
            var line = StoryRules.Str(x, "line", at);
            if (line.Length > 0)
            {
                a.Line = rules.Lines.Concept(line);
                if (a.Line < 0)
                    throw new StoryException($"{at}: \"line\": no occasion {line} in Story/Lines");
            }
            if (a.Say.Length == 0 == (a.Line < 0))
                throw new StoryException($"{at}: an answer has \"say\" (its lines) or \"line\" (a Lines occasion), one of them");
            a.Then = StoryRules.List(x, "then", at).Select(v => v.ValueKind == JsonValueKind.String
                ? StoryletRules.ParseNamed(rules, v.GetString() ?? "", at, named, 2, Slot)
                : throw new StoryException($"{at}: each \"then\" is text, like \"?me Likes ?them +5\"")).ToArray();
            var learn = StoryRules.Str(x, "learn", at);
            if (learn.Length > 0)
                a.Learn = StoryletRules.NamedSlot(learn.Trim('\''), named, 2);
            answers.Add(a);
        }
        if (answers.Count is 0 or > MaxAnswers)
            throw new StoryException($"{where}: 1 to {MaxAnswers} answers");
        if (2 + named.Count > StoryRules.MaxVars)
            throw new StoryException($"{where}: {named.Count} named things, {StoryRules.MaxVars - 2} at most");
        t.Answers = answers.ToArray();
        t.Named = named.ToArray();
        return t;
    }

    private static StoryQuery Compile(string text, StoryRules rules, string where, string key)
    {
        try
        {
            return StoryQuery.Compile(text, rules, ["me", "them"]);
        }
        catch (StoryException ex)
        {
            throw new StoryException($"{where}: \"{key}\": \"{text}\": {ex.Message}");
        }
    }

    /// <summary>A line: printable ASCII, short, filling only {me}, {them} (and .first) and {here}.</summary>
    private static string CheckLine(string line, string where)
    {
        if (line.Length is 0 or > LineRules.MaxLength || line.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: line \"{line}\": printable ASCII, 1 to {LineRules.MaxLength} characters");
        for (var i = line.IndexOf('{'); i >= 0; i = line.IndexOf('{', i + 1))
        {
            var end = line.IndexOf('}', i);
            var name = end > i ? line[(i + 1)..end] : "";
            if (name is not ("me" or "me.first" or "them" or "them.first" or "here"))
                throw new StoryException($"{where}: line \"{line}\": {{{name}}}: a line fills {{me}}, {{them}}, {{me.first}}, {{them.first}} and {{here}}");
        }
        return line;
    }
}

/// <summary>
/// The topics at play (docs/story/STORY.md "Topics"): what a person offers to talk about with a player now, and what
/// happens when they are asked. What a player has asked is the Asked relation (player to the topic's Thing), so it
/// saves with the world. Host only; nothing allocates when offering.
/// </summary>
public sealed class StoryTopics
{
    private readonly StoryEngine _e;
    private readonly StringBuilder _sb = new();
    private readonly Dictionary<long, int> _lastLine = new(); // (topic, answer): the line said last (never the same twice running)
    private int _seed = 12345;

    public StoryTopics(StoryEngine engine) => _e = engine;

    private TopicRules R => _e.Rules.Topics;
    private StoryWorld W => _e.World;

    /// <summary>How many times a player asked a topic (of anyone).</summary>
    public int AskedCount(int player, TopicDef t)
    {
        var thing = W.Find(StoryKind.Thing, t.Key);
        return thing < 0 || R.Asked < 0 ? 0 : W.Get(player, R.Asked, thing);
    }

    /// <summary>
    /// The topics <paramref name="person"/> offers <paramref name="player"/> now, the most pressing first, at most
    /// <see cref="TopicRules.Offered"/>: into <paramref name="into"/> (topic index, asked before), how many.
    /// </summary>
    public int Offer(int person, int player, Span<(ushort Topic, bool Asked)> into)
    {
        var topics = R.Topics;
        if (topics.Length == 0 || person < 0 || player < 0)
            return 0;
        Span<int> b = stackalloc int[StoryRules.MaxVars];
        Span<int> pri = stackalloc int[TopicRules.Offered];
        var max = Math.Min(into.Length, TopicRules.Offered);
        var n = 0;
        for (var i = 0; i < topics.Length; i++)
        {
            var t = topics[i];
            if (n == max && t.Priority <= pri[n - 1])
                continue;
            if (!Open(t, person, player, b, out var asked))
                continue;
            // Insert by priority (file order among equals).
            var at = n < max ? n : max - 1;
            if (n < max)
                n++;
            while (at > 0 && pri[at - 1] < t.Priority)
            {
                (into[at], pri[at]) = (into[at - 1], pri[at - 1]);
                at--;
            }
            (into[at], pri[at]) = (((ushort)i, asked), t.Priority);
        }
        return n;
    }

    /// <summary>Is the topic on offer from this person to this player now (and has the player asked it before)?</summary>
    private bool Open(TopicDef t, int person, int player, Span<int> b, out bool asked)
    {
        asked = false;
        if (t.Who.Length > 0 && W.Find(t.Who) != person)
            return false;
        var count = AskedCount(player, t);
        asked = count > 0;
        if (asked && !t.Again)
            return false;
        if (t.After.Length > 0)
        {
            var any = false;
            foreach (var a in t.After)
                any |= AskedCount(player, R.Topics[a]) > 0;
            if (!any)
                return false;
        }
        Bind(t, person, player, b);
        if (t.If != null && !t.If.Any(W, b))
            return false;
        return AnswerFor(t, person, player, b) >= 0;
    }

    private void Bind(TopicDef t, int person, int player, Span<int> b)
    {
        b.Fill(-1);
        (b[0], b[1]) = (person, player);
    }

    /// <summary>The first answer whose "if" holds, or -1.</summary>
    private int AnswerFor(TopicDef t, int person, int player, Span<int> b)
    {
        for (var k = 0; k < t.Answers.Length; k++)
        {
            Bind(t, person, player, b);
            if (t.Answers[k].If is not { } q || q.Any(W, b))
                return k;
        }
        return -1;
    }

    /// <summary>
    /// A player asks a person about a topic: null when it isn't on offer; else the answer given (what it changes is done:
    /// the Asked count, its effects, a lead learnt) and, for a "say" answer, the line filled in.
    /// </summary>
    public TopicAnswer? Ask(int person, int player, int topic, out string? line)
    {
        line = null;
        if ((uint)topic >= (uint)R.Topics.Length)
            return null;
        var t = R.Topics[topic];
        Span<int> b = stackalloc int[StoryRules.MaxVars];
        if (!Open(t, person, player, b, out _))
            return null;
        var k = AnswerFor(t, person, player, b);
        var a = t.Answers[k];
        Bind(t, person, player, b);
        for (var i = 0; i < t.Named.Length; i++)
            b[2 + i] = Thing(t.Named[i]);
        W.Apply(a.Then, b);
        if (a.Learn >= 0 && R.Lead >= 0 && b[a.Learn] >= 0)
        {
            W.Add(player, R.Lead, b[a.Learn], 1);
            if (R.VerbLearn >= 0)
                _e.Record(R.VerbLearn, player, person, -1, _e.Agents.Where(person), b[a.Learn]);
        }
        if (!a.Keep)
            W.Add(player, R.Asked, W.Get(StoryKind.Thing, t.Key, "topic " + t.Id, _e.Time), 1);
        if (a.Say.Length > 0)
        {
            var key = (long)topic << 8 | (uint)k;
            var pick = 0;
            if (a.Say.Length > 1)
            {
                _seed = _seed * 1103515245 + 12345;
                pick = (int)((uint)_seed >> 8) % a.Say.Length;
                if (_lastLine.TryGetValue(key, out var last) && last == pick)
                    pick = (pick + 1) % a.Say.Length;
            }
            _lastLine[key] = pick;
            line = Fill(a.Say[pick], person, player);
        }
        return a;
    }

    private int Thing(string name)
    {
        var id = W.Find(name);
        return id >= 0 ? id : W.Get(StoryKind.Thing, StoryFactions.Key("fact:" + name), name, _e.Time);
    }

    /// <summary>A topic's line with {me}, {them} (.first) and {here} filled in.</summary>
    public string Fill(string line, int person, int player)
    {
        var sb = _sb.Clear();
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            var end = c == '{' ? line.IndexOf('}', i) : -1;
            if (end < 0)
            {
                sb.Append(c);
                continue;
            }
            var name = line.AsSpan(i + 1, end - i - 1);
            var first = name.EndsWith(".first");
            if (first)
                name = name[..^6];
            var who = name.SequenceEqual("me") ? person : name.SequenceEqual("them") ? player : _e.Agents.Where(person);
            var text = W.NameOf(who);
            sb.Append(first && text.IndexOf(' ') is var sp and > 0 ? text[(sp + 1)..] is { } rest && IsTitle(text[..sp]) ? rest : text[..sp] : text);
            i = end;
        }
        return sb.Length > StoryTalk.MaxText ? sb.ToString(0, StoryTalk.MaxText - 3) + "..." : sb.ToString();
    }

    /// <summary>"Warden Pettigrew".first is Pettigrew, not Warden: a title isn't a first name.</summary>
    private static bool IsTitle(string word) => word is "Warden" or "Sergeant" or "Officer" or "Foreman" or "Inspector" or "Doctor";
}
