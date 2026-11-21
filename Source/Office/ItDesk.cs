using System.Text.Json;

namespace CallOfSlop;

/// <summary>A bad desk file: what is wrong, and where.</summary>
public sealed class ItDeskException(string message) : Exception(message);

/// <summary>
/// The IT Department's side of its fax line (data <c>Story/Office/IT.json</c>; the server side Game/GameSandboxOffice.cs): the pages a
/// plugged-in Fax Machine prints in order, and the answers to emails sent from a Computer, picked by the words in them. Read once; a bad
/// file leaves the line dead and says why in the log.
/// </summary>
public sealed class ItDesk
{
    public const int MaxLine = 96, MaxLines = 6, MaxPages = 250;

    public string From = "IT Department";
    public double FirstMin = 600, FirstMax = 3600, EveryMin = 1500, EveryMax = 3000, ReplyMin = 40, ReplyMax = 180;

    /// <summary>Every page the desk can print, by id (a fax keeps its page in a byte): the pages in order first, then the replies.</summary>
    public string[][] All = [];

    /// <summary>How many pages a fax prints in order (ids 0 to this less one).</summary>
    public int StepCount;

    /// <summary>The answers to emails: the words one of which must be in it, and the page id.</summary>
    public (string[] Words, int Page)[] Replies = [];

    public string Sent = "", NoFax = "";

    /// <summary>A page by its id; null past the end.</summary>
    public string[]? Page(int id) => (uint)id < (uint)All.Length ? All[id] : null;

    /// <summary>Page <paramref name="step"/> of the pages in order. -1: past the end.</summary>
    public int PageFor(int step) => (uint)step < (uint)StepCount ? step : -1;

    /// <summary>The reply to an email, as a page id: the first rule with a word in it; else the first rule with no words. -1: none.</summary>
    public int ReplyTo(string email)
    {
        var text = " " + email + " ";
        foreach (var (words, page) in Replies)
            foreach (var word in words)
                if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return page;
        foreach (var (words, page) in Replies)
            if (words.Length == 0)
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
                var file = Path.Combine(AppFolders.Data, "Story", "Office", "IT.json");
                try
                {
                    _script = File.Exists(file) ? Read(File.ReadAllText(file), "Office/IT.json") : null;
                }
                catch (ItDeskException ex)
                {
                    Console.WriteLine($"[office] {ex.Message}: the IT Department's fax is dead");
                    LastError = ex.Message;
                }
            }
            return _script;
        }
    }

    public static string? LastError;

    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Reads a desk file; throws an <see cref="ItDeskException"/> naming what is wrong.</summary>
    public static ItDesk Read(string json, string where)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ItDeskException($"{where}: {ex.Message}");
        }
        using (doc)
        {
            var root = doc.RootElement;
            var desk = new ItDesk();
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "from" or "first" or "every" or "pages" or "replyIn" or "replies" or "sent" or "noFax"))
                    throw new ItDeskException($"{where}: unknown key \"{p.Name}\"");
            if (root.TryGetProperty("from", out var from))
                desk.From = Text(from, $"{where}: from");
            (desk.FirstMin, desk.FirstMax) = Span(root, "first", where, desk.FirstMin, desk.FirstMax);
            (desk.EveryMin, desk.EveryMax) = Span(root, "every", where, desk.EveryMin, desk.EveryMax);
            (desk.ReplyMin, desk.ReplyMax) = Span(root, "replyIn", where, desk.ReplyMin, desk.ReplyMax);
            var all = new List<string[]>();
            var pages = root.TryGetProperty("pages", out var ps) ? ps.EnumerateArray().ToArray() : [];
            var replies = root.TryGetProperty("replies", out var rs) ? rs.EnumerateArray().ToArray() : [];
            foreach (var (pg, i) in pages.Select((x, i) => (x, i)))
                all.Add(Lines(pg, $"{where}: pages[{i}]"));
            desk.StepCount = all.Count;
            desk.Replies = replies.Select((r, i) =>
            {
                all.Add(Lines(r, $"{where}: replies[{i}]"));
                var words = r.TryGetProperty("if", out var w) ? w.EnumerateArray().Select(x => Text(x, $"{where}: replies[{i}].if")).ToArray() : [];
                return (words, all.Count - 1);
            }).ToArray();
            desk.All = all.ToArray();
            if (desk.All.Length > MaxPages)
                throw new ItDeskException($"{where}: {desk.All.Length} pages and replies, at most {MaxPages} (a fax keeps its page in a byte)");
            desk.Sent = root.TryGetProperty("sent", out var s) ? Text(s, $"{where}: sent") : "Email sent.";
            desk.NoFax = root.TryGetProperty("noFax", out var n) ? Text(n, $"{where}: noFax") : desk.Sent;
            return desk;
        }
    }

    /// <summary>A time like "90s", "5m" or "1h" in seconds; null if it isn't one.</summary>
    private static double? Duration(string t)
    {
        if (t.Length < 2 || !double.TryParse(t[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) || v < 0)
            return null;
        return t[^1] switch { 's' => v, 'm' => v * 60, 'h' => v * 3600, _ => null };
    }

    internal static (double, double) Span(JsonElement root, string key, string where, double a, double b)
    {
        if (!root.TryGetProperty(key, out var e))
            return (a, b);
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2)
            throw new ItDeskException($"{where}: {key} is [\"shortest\", \"longest\"]");
        var lo = Duration(e[0].GetString() ?? "") ?? throw new ItDeskException($"{where}: {key}[0] isn't a time (\"90s\", \"5m\", \"1h\")");
        var hi = Duration(e[1].GetString() ?? "") ?? throw new ItDeskException($"{where}: {key}[1] isn't a time (\"90s\", \"5m\", \"1h\")");
        if (hi < lo || hi > 65000)
            throw new ItDeskException($"{where}: {key} runs from the shortest to the longest, at most 18 hours");
        return (lo, hi);
    }

    internal static string[] Lines(JsonElement e, string where)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ItDeskException($"{where}: like {{\"lines\": [\"...\"]}}");
        if (!e.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array || lines.GetArrayLength() is 0 or > MaxLines)
            throw new ItDeskException($"{where}: \"lines\" is 1 to {MaxLines} lines");
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("lines" or "if"))
                throw new ItDeskException($"{where}: unknown key \"{p.Name}\"");
        return lines.EnumerateArray().Select((l, i) => Text(l, $"{where}.lines[{i}]")).ToArray();
    }

    internal static string Text(JsonElement e, string where)
    {
        var s = e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : throw new ItDeskException($"{where} is text");
        if (s.Length == 0 || s.Length > MaxLine || s.Any(c => c < 32 || c > 126))
            throw new ItDeskException($"{where}: 1 to {MaxLine} printable ASCII characters");
        return s;
    }
}
