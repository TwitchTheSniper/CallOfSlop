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
    public const int MaxLine = 96, MaxLines = 6, MaxPages = 200;

    public string From = "IT Department";
    public double FirstMin = 600, FirstMax = 3600, EveryMin = 1500, EveryMax = 3000, ReplyMin = 40, ReplyMax = 180;
    public string[][] Pages = [];
    public (string[] Words, string[] Lines)[] Replies = [];
    public string Sent = "", NoFax = "";

    /// <summary>What IT faxes when one of its computers has been hooked by the scammers' adverts (<see cref="ScamDesk"/>); null: nothing.</summary>
    public string[]? Malware;

    /// <summary>The malware notice's page id (past the story's pages and the replies).</summary>
    public int MalwarePage => Pages.Length + Replies.Length;

    /// <summary>A page by its id: the story's pages first, then the replies, then the malware notice. Null past the end.</summary>
    public string[]? Page(int id) =>
        id < 0 ? null : id < Pages.Length ? Pages[id] : id - Pages.Length < Replies.Length ? Replies[id - Pages.Length].Lines : id == MalwarePage ? Malware : null;

    /// <summary>The reply to an email, as a page id: the first rule with a word in it, else the last rule with no words. -1: none.</summary>
    public int ReplyTo(string email)
    {
        var fallback = -1;
        for (var k = 0; k < Replies.Length; k++)
        {
            if (Replies[k].Words.Length == 0)
            {
                fallback = k;
                continue;
            }
            foreach (var w in Replies[k].Words)
                if ((" " + email + " ").Contains(w, StringComparison.OrdinalIgnoreCase))
                    return Pages.Length + k;
        }
        return fallback < 0 ? -1 : Pages.Length + fallback;
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
                    _script = File.Exists(file) ? Read(File.ReadAllText(file), "Office/IT.json") : null;
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

    /// <summary>Reads a desk file; throws a <see cref="StoryException"/> naming what is wrong.</summary>
    public static ItDesk Read(string json, string where)
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
                if (p.Name is not ("format" or "about" or "from" or "first" or "every" or "pages" or "replyIn" or "replies" or "sent" or "noFax" or "malware"))
                    throw new StoryException($"{where}: unknown key \"{p.Name}\"");
            if (root.TryGetProperty("from", out var from))
                desk.From = Text(from, $"{where}: from");
            (desk.FirstMin, desk.FirstMax) = Span(root, "first", where, desk.FirstMin, desk.FirstMax);
            (desk.EveryMin, desk.EveryMax) = Span(root, "every", where, desk.EveryMin, desk.EveryMax);
            (desk.ReplyMin, desk.ReplyMax) = Span(root, "replyIn", where, desk.ReplyMin, desk.ReplyMax);
            if (root.TryGetProperty("pages", out var pages))
                desk.Pages = pages.EnumerateArray().Select((p, i) => Lines(p, $"{where}: pages[{i}]")).ToArray();
            if (root.TryGetProperty("replies", out var replies))
                desk.Replies = replies.EnumerateArray().Select((r, i) =>
                {
                    var words = r.TryGetProperty("if", out var w) ? w.EnumerateArray().Select(x => Text(x, $"{where}: replies[{i}].if")).ToArray() : [];
                    return (words, Lines(r, $"{where}: replies[{i}]"));
                }).ToArray();
            if (root.TryGetProperty("malware", out var malware))
                desk.Malware = Lines(malware, $"{where}: malware");
            if (desk.Pages.Length + desk.Replies.Length > MaxPages)
                throw new StoryException($"{where}: {desk.Pages.Length + desk.Replies.Length} pages and replies, at most {MaxPages} (a fax keeps its page in a byte)");
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
