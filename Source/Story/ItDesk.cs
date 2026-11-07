using System.Text.Json;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The IT Department's side of its fax line (docs/story/STORY.md, "The IT Department's fax"; data <c>Story/Office/IT.json</c>): the pages a
/// plugged-in Fax Machine prints in order, and the answers to emails sent from a Computer, picked by the words in them. Read once; a bad
/// file leaves the line dead and says why in the log (and <c>--check-story</c>).
/// </summary>
public sealed class ItDesk
{
    public const int MaxLine = 96, MaxLines = 6, MaxPages = 250;

    public string From = "IT Department";
    public double FirstMin = 600, FirstMax = 3600, EveryMin = 1500, EveryMax = 3000, ReplyMin = 40, ReplyMax = 180;

    /// <summary>
    /// Every page the desk can print, by id (a fax keeps its page in a byte): the story's pages first, then the replies,
    /// then the malware notice (so a save made before the rest came keeps its page), then the pages' alternatives and the notices.
    /// </summary>
    public string[][] All = [];

    /// <summary>Per page id, the story query it asks (<c>"when"</c>, over ?us the faction and ?them the reader's group), or null.</summary>
    public StoryQuery?[] When = [];

    /// <summary>The story's pages in order: each step's page ids, its alternatives first (the most specific first), its own page last.</summary>
    public int[][] Steps = [];

    /// <summary>The answers to emails: the words one of which must be in it, and the page id.</summary>
    public (string[] Words, int Page)[] Replies = [];

    /// <summary>What the faction faxes about a deed or a tier of its standing (by name, Story/Factions), in order: the first whose "when" holds.</summary>
    public (string On, int Page)[] Notices = [];

    public string Sent = "", NoFax = "";

    /// <summary>What IT faxes when one of its computers has been hooked by the scammers' adverts (<see cref="ScamDesk"/>); -1: nothing.</summary>
    public int MalwarePage = -1;

    public string[]? Malware => MalwarePage >= 0 ? All[MalwarePage] : null;

    /// <summary>The story's pages.</summary>
    public int StepCount => Steps.Length;

    /// <summary>A page by its id; null past the end.</summary>
    public string[]? Page(int id) => (uint)id < (uint)All.Length ? All[id] : null;

    /// <summary>Does a page's "when" hold for the faction <paramref name="us"/> and the group <paramref name="them"/>? (No question: yes. No story: no.)</summary>
    public bool Holds(int page, StoryWorld? w, int us, int them)
    {
        var q = (uint)page < (uint)When.Length ? When[page] : null;
        if (q == null)
            return true;
        if (w == null || us < 0 || them < 0 && AsksThem(q))
            return false; // (an unbound ?them would match anyone)
        Span<int> b = stackalloc int[Math.Max(2, q.VarCount)];
        b.Fill(-1);
        b[0] = us;
        b[1] = them;
        return q.Any(w, b);
    }

    private static bool AsksThem(StoryQuery q)
    {
        foreach (var c in q.Clauses)
            if (c.A.Var == 1 || c.Kind is StoryQuery.ClauseKind.Relation or StoryQuery.ClauseKind.Same or StoryQuery.ClauseKind.Different && c.B.Var == 1)
                return true;
        return false;
    }

    /// <summary>Story page <paramref name="step"/> as it prints for this reader: the first alternative whose "when" holds, else its own. -1: past the end.</summary>
    public int PageFor(int step, StoryWorld? w = null, int us = -1, int them = -1)
    {
        if ((uint)step >= (uint)Steps.Length)
            return -1;
        var ids = Steps[step];
        foreach (var id in ids)
            if (Holds(id, w, us, them))
                return id;
        return ids[^1];
    }

    /// <summary>
    /// The reply to an email, as a page id: the first rule with a word in it whose "when" holds; else the first rule with no
    /// words whose "when" holds, those with one first (the plain one is the last resort). -1: none.
    /// </summary>
    public int ReplyTo(string email, StoryWorld? w = null, int us = -1, int them = -1)
    {
        var text = " " + email + " ";
        foreach (var (words, page) in Replies)
        {
            if (words.Length == 0 || !Holds(page, w, us, them))
                continue;
            foreach (var word in words)
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return page;
        }
        var plain = -1;
        foreach (var (words, page) in Replies)
        {
            if (words.Length > 0)
                continue;
            if (When[page] == null)
                plain = plain < 0 ? page : plain;
            else if (Holds(page, w, us, them))
                return page;
        }
        return plain;
    }

    /// <summary>The notice for a deed or tier (by name) for this reader: the first whose "when" holds. -1: none.</summary>
    public int NoticeFor(string on, StoryWorld? w, int us, int them)
    {
        foreach (var (name, page) in Notices)
            if (name.Equals(on, StringComparison.OrdinalIgnoreCase) && Holds(page, w, us, them))
                return page;
        return -1;
    }

    /// <summary>How long a fax waits, plugged in, before story page <paramref name="step"/>: seeded by the fax and the step, the same on every machine.</summary>
    public double DueAfter(int step, uint seed) =>
        step == 0 ? Between(FirstMin, FirstMax, seed, 0) : Between(EveryMin, EveryMax, seed, (uint)step);

    /// <summary>How long IT takes over a reply.</summary>
    public double ReplyAfter(uint seed) => Between(ReplyMin, ReplyMax, seed, 0xfa);

    private static double Between(double a, double b, uint seed, uint salt)
    {
        var h = (seed ^ salt * 0x9e3779b9u) * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return a + (b - a) * (h / 4294967296.0);
    }

    private static ItDesk? _script;
    private static bool _read;

    /// <summary>The game's desk (null: no file, or a bad one).</summary>
    public static ItDesk? Script
    {
        get
        {
            if (!_read)
            {
                _read = true;
                var file = Path.Combine(AppFolders.Data, StoryRules.FolderName, "Office", "IT.json");
                try
                {
                    _script = File.Exists(file) ? Read(File.ReadAllText(file), "Office/IT.json", Game.StoryData) : null;
                }
                catch (StoryException ex)
                {
                    Console.WriteLine($"[story] {ex.Message}: the IT Department's fax is dead");
                    LastError = ex.Message;
                }
            }
            return _script;
        }
    }

    public static string? LastError;

    /// <summary>Reads a desk file; throws a <see cref="StoryException"/> naming what is wrong. <paramref name="rules"/>: the story's, for the "when" questions (none: a "when" is refused).</summary>
    public static ItDesk Read(string json, string where, StoryRules? rules = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, StoryRules.Json);
        }
        catch (JsonException ex)
        {
            throw new StoryException($"{where}: {ex.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            var desk = new ItDesk();
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "from" or "first" or "every" or "pages" or "replyIn" or "replies" or "sent" or "noFax" or "malware" or "notices"))
                    throw new StoryException($"{where}: unknown key \"{p.Name}\"");
            if (root.TryGetProperty("from", out var from))
                desk.From = Text(from, $"{where}: from");
            (desk.FirstMin, desk.FirstMax) = Span(root, "first", where, desk.FirstMin, desk.FirstMax);
            (desk.EveryMin, desk.EveryMax) = Span(root, "every", where, desk.EveryMin, desk.EveryMax);
            (desk.ReplyMin, desk.ReplyMax) = Span(root, "replyIn", where, desk.ReplyMin, desk.ReplyMax);
            var all = new List<string[]>();
            var when = new List<StoryQuery?>();
            int Add(JsonElement e, string at, StoryQuery? q)
            {
                all.Add(Lines(e, at));
                when.Add(q);
                return all.Count - 1;
            }
            StoryQuery? When(JsonElement e, string at)
            {
                if (!e.TryGetProperty("when", out var w))
                    return null;
                var text = w.ValueKind == JsonValueKind.String ? w.GetString() ?? "" : throw new StoryException($"{at}: \"when\" is a story query over ?us and ?them");
                if (rules == null)
                    throw new StoryException($"{at}: \"when\" needs the story's rules");
                try
                {
                    return StoryQuery.Compile(text, rules, ["us", "them"]);
                }
                catch (StoryException ex)
                {
                    throw new StoryException($"{at}: \"when\": \"{text}\": {ex.Message}");
                }
            }
            var pages = root.TryGetProperty("pages", out var ps) ? ps.EnumerateArray().ToArray() : [];
            var replies = root.TryGetProperty("replies", out var rs) ? rs.EnumerateArray().ToArray() : [];
            var own = pages.Select((p, i) => Add(p, $"{where}: pages[{i}]", null)).ToArray(); // (ids as before alternatives came: saved faxes keep their page)
            var replyIds = replies.Select((r, i) => Add(r, $"{where}: replies[{i}]", When(r, $"{where}: replies[{i}]"))).ToArray();
            desk.Replies = replies.Select((r, i) =>
            {
                var words = r.TryGetProperty("if", out var w) ? w.EnumerateArray().Select(x => Text(x, $"{where}: replies[{i}].if")).ToArray() : [];
                return (words, replyIds[i]);
            }).ToArray();
            if (root.TryGetProperty("malware", out var malware))
                desk.MalwarePage = Add(malware, $"{where}: malware", null);
            desk.Steps = pages.Select((p, i) =>
            {
                var alts = new List<(int Id, int Weight)>();
                if (p.TryGetProperty("alts", out var a))
                    foreach (var (alt, k) in a.EnumerateArray().Select((x, k) => (x, k)))
                    {
                        var at = $"{where}: pages[{i}].alts[{k}]";
                        var q = When(alt, at) ?? throw new StoryException($"{at}: an alternative asks something (\"when\")");
                        alts.Add((Add(alt, at, q), q.Clauses.Length));
                    }
                return alts.OrderByDescending(x => x.Weight).Select(x => x.Id).Append(own[i]).ToArray(); // (stable: the same weight keeps its order)
            }).ToArray();
            desk.Notices = (root.TryGetProperty("notices", out var ns) ? ns.EnumerateArray().ToArray() : []).Select((n, i) =>
            {
                var at = $"{where}: notices[{i}]";
                var on = n.TryGetProperty("on", out var o) ? Text(o, $"{at}.on") : throw new StoryException($"{at}: \"on\" names a deed or a tier of the faction (Story/Factions)");
                return (on, Add(n, at, When(n, at)));
            }).ToArray();
            desk.All = all.ToArray();
            desk.When = when.ToArray();
            if (desk.All.Length > MaxPages)
                throw new StoryException($"{where}: {desk.All.Length} pages, replies and notices, at most {MaxPages} (a fax keeps its page in a byte)");
            desk.Sent = root.TryGetProperty("sent", out var s) ? Text(s, $"{where}: sent") : "Email sent.";
            desk.NoFax = root.TryGetProperty("noFax", out var n) ? Text(n, $"{where}: noFax") : desk.Sent;
            return desk;
        }
    }

    internal static (double, double) Span(JsonElement root, string key, string where, double a, double b)
    {
        if (!root.TryGetProperty(key, out var e))
            return (a, b);
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new StoryException($"{where}: {key} is [\"shortest\", \"longest\"]");
        var lo = StoryRules.Duration(e[0].GetString() ?? "") ?? throw new StoryException($"{where}: {key}[0] isn't a time (\"90s\", \"5m\", \"1h\")");
        var hi = StoryRules.Duration(e[1].GetString() ?? "") ?? throw new StoryException($"{where}: {key}[1] isn't a time (\"90s\", \"5m\", \"1h\")");
        if (hi < lo || hi > 65000)
            throw new StoryException($"{where}: {key} runs from the shortest to the longest, at most 18 hours");
        return (lo, hi);
    }

    internal static string[] Lines(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{where}: like {{\"lines\": [\"...\"]}}");
        if (!e.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() is 0 or > MaxLines)
            throw new StoryException($"{where}: \"lines\" is 1 to {MaxLines} lines");
        return lines.EnumerateArray().Select((l, i) => Text(l, $"{where}.lines[{i}]")).ToArray();
    }

    internal static string Text(JsonElement e, string where)
    {
        var s = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : throw new StoryException($"{where} is text");
        if (s.Length == 0 || s.Length > MaxLine || s.Any(c => c < 32 || c > 126))
            throw new StoryException($"{where}: 1 to {MaxLine} printable ASCII characters");
        return s;
    }
}
