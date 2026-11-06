using System.Text.Json;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The Scammers' side of a Computer (docs/story/STORY.md, "The Scammers"; data <c>Story/Office/Scammers.json</c>): the adverts on a
/// computer's screen, the scam each one opens, the messages a hooked computer gets, and the answers to what a player types back.
/// Read once; a bad file leaves the adverts dead and says why in the log (and <c>--check-story</c>).
/// </summary>
public sealed class ScamDesk
{
    /// <summary>At most this many scams (a computer keeps its scam in a byte) and adverts.</summary>
    public const int MaxScams = 200, MaxAdverts = 64;

    /// <summary>A banner's words are drawn across a screen, so they stay short.</summary>
    public const int MaxLabel = 28, MaxPopup = 16;

    /// <summary>One scam: who it claims to be, the screen's title while it runs, and its messages in order.</summary>
    public sealed class Scam
    {
        public string Name = "", From = "", Popup = "";
        public string[][] Messages = [];
    }

    /// <summary>An answer to an email from a hooked computer. <see cref="Take"/> above 0: the player fell for it, and the scammers take that share of the battery.</summary>
    public sealed class Reply
    {
        public string[] Words = [];
        public string[] Lines = [];
        public float Take;
    }

    public string From = "The Call Centre";
    public (string Label, int Scam)[] Adverts = [];
    public Scam[] Scams = [];
    public Reply[] Replies = [];
    public double FirstIn = 3, EveryMin = 20, EveryMax = 45, ReplyMin = 4, ReplyMax = 12;
    public string Sent = "Email sent.", Closed = "The pop-ups close by themselves.", Cured = "The computer restarts. The pop-ups are gone.";
    public string Robbed = "Your battery drops to {percent} percent.";

    /// <summary>The advert in banner <paramref name="slot"/> of a computer with seed <paramref name="seed"/>: two different ones per screen, the same on every peer.</summary>
    public int AdvertAt(uint seed, int slot)
    {
        var n = Adverts.Length;
        if (n == 0)
            return -1;
        var h = Mix(seed);
        var a = (int)(h % (uint)n);
        return slot == 0 || n == 1 ? a : (a + 1 + (int)((h >> 11) % (uint)(n - 1))) % n;
    }

    /// <summary>The answer to an email, by the first rule with one of its words in it, else the last rule with none. -1: none.</summary>
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
                    return k;
        }
        return fallback;
    }

    /// <summary>Seconds a hooked computer waits before message <paramref name="step"/> (the first at once, near enough).</summary>
    public double DueAfter(int step, uint seed) => step == 0 ? FirstIn : Between(EveryMin, EveryMax, seed, (uint)step);

    public double ReplyAfter(uint seed) => Between(ReplyMin, ReplyMax, seed, 0x5ca);

    private static uint Mix(uint h)
    {
        h *= 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return h;
    }

    private static double Between(double a, double b, uint seed, uint salt) => a + (b - a) * (Mix(seed ^ salt * 0x9e3779b9u) / 4294967296.0);

    private static ScamDesk? _script;
    private static bool _read;

    /// <summary>The game's scammers (null: no file, or a bad one).</summary>
    public static ScamDesk? Script
    {
        get
        {
            if (!_read)
            {
                _read = true;
                var file = Path.Combine(AppFolders.Data, StoryRules.FolderName, "Office", "Scammers.json");
                try
                {
                    _script = File.Exists(file) ? Read(File.ReadAllText(file), "Office/Scammers.json") : null;
                }
                catch (StoryException ex)
                {
                    Console.WriteLine($"[story] {ex.Message}: the computers show no adverts");
                    LastError = ex.Message;
                }
            }
            return _script;
        }
    }

    public static string? LastError;

    /// <summary>Reads a scammers file; throws a <see cref="StoryException"/> naming what is wrong.</summary>
    public static ScamDesk Read(string json, string where)
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
            var desk = new ScamDesk();
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "about" or "from" or "firstIn" or "every" or "replyIn" or "scams" or "adverts" or "replies" or "sent" or "closed" or "cured" or "robbed"))
                    throw new StoryException($"{where}: unknown key \"{p.Name}\"");
            if (root.TryGetProperty("from", out var from))
                desk.From = ItDesk.Text(from, $"{where}: from");
            if (root.TryGetProperty("firstIn", out var first))
                desk.FirstIn = StoryRules.Duration(first.GetString() ?? "") is { } f and <= 600 ? f : throw new StoryException($"{where}: firstIn is a time up to 10 minutes (\"3s\")");
            (desk.EveryMin, desk.EveryMax) = ItDesk.Span(root, "every", where, desk.EveryMin, desk.EveryMax);
            (desk.ReplyMin, desk.ReplyMax) = ItDesk.Span(root, "replyIn", where, desk.ReplyMin, desk.ReplyMax);
            if (desk.EveryMax > ushort.MaxValue)
                throw new StoryException($"{where}: every is at most 18 hours");
            if (!root.TryGetProperty("scams", out var scams) || scams.ValueKind != JsonValueKind.Array || scams.GetArrayLength() is 0 or > MaxScams)
                throw new StoryException($"{where}: \"scams\" lists 1 to {MaxScams} scams");
            desk.Scams = scams.EnumerateArray().Select((s, i) =>
            {
                var at = $"{where}: scams[{i}]";
                foreach (var p in s.EnumerateObject())
                    if (p.Name is not ("name" or "from" or "popup" or "messages"))
                        throw new StoryException($"{at}: unknown key \"{p.Name}\"");
                var scam = new Scam
                {
                    Name = s.TryGetProperty("name", out var n) ? ItDesk.Text(n, at + ".name") : throw new StoryException($"{at}: needs a \"name\""),
                    From = s.TryGetProperty("from", out var f) ? ItDesk.Text(f, at + ".from") : throw new StoryException($"{at}: needs a \"from\""),
                    Popup = s.TryGetProperty("popup", out var pu) ? Label(pu, at + ".popup", MaxPopup) : "1 NEW MESSAGE",
                };
                if (!s.TryGetProperty("messages", out var ms) || ms.ValueKind != JsonValueKind.Array || ms.GetArrayLength() is 0 or > 50)
                    throw new StoryException($"{at}: \"messages\" lists 1 to 50 messages");
                scam.Messages = ms.EnumerateArray().Select((m, k) => ItDesk.Lines(m, $"{at}.messages[{k}]")).ToArray();
                return scam;
            }).ToArray();
            if (!root.TryGetProperty("adverts", out var ads) || ads.ValueKind != JsonValueKind.Array || ads.GetArrayLength() is 0 or > MaxAdverts)
                throw new StoryException($"{where}: \"adverts\" lists 1 to {MaxAdverts} adverts");
            desk.Adverts = ads.EnumerateArray().Select((a, i) =>
            {
                var at = $"{where}: adverts[{i}]";
                var label = a.TryGetProperty("label", out var l) ? Label(l, at + ".label", MaxLabel) : throw new StoryException($"{at}: needs a \"label\"");
                var name = a.TryGetProperty("scam", out var s) ? ItDesk.Text(s, at + ".scam") : throw new StoryException($"{at}: needs a \"scam\"");
                var k = Array.FindIndex(desk.Scams, x => x.Name == name);
                return k >= 0 ? (label, k) : throw new StoryException($"{at}: no scam named \"{name}\"");
            }).ToArray();
            if (root.TryGetProperty("replies", out var replies))
                desk.Replies = replies.EnumerateArray().Select((r, i) =>
                {
                    var at = $"{where}: replies[{i}]";
                    var take = r.TryGetProperty("take", out var t) ? t.GetSingle() : 0;
                    if (take is < 0 or > 1)
                        throw new StoryException($"{at}: take is a share of the battery, 0 to 1");
                    return new Reply
                    {
                        Words = r.TryGetProperty("if", out var w) ? w.EnumerateArray().Select(x => ItDesk.Text(x, at + ".if")).ToArray() : [],
                        Lines = ItDesk.Lines(r, at),
                        Take = take,
                    };
                }).ToArray();
            if (root.TryGetProperty("sent", out var sent))
                desk.Sent = ItDesk.Text(sent, $"{where}: sent");
            if (root.TryGetProperty("closed", out var closed))
                desk.Closed = ItDesk.Text(closed, $"{where}: closed");
            if (root.TryGetProperty("cured", out var cured))
                desk.Cured = ItDesk.Text(cured, $"{where}: cured");
            if (root.TryGetProperty("robbed", out var robbed))
                desk.Robbed = ItDesk.Text(robbed, $"{where}: robbed");
            return desk;
        }
    }

    private static string Label(JsonElement e, string where, int most)
    {
        var s = ItDesk.Text(e, where);
        return s.Length <= most ? s : throw new StoryException($"{where}: at most {most} characters (it is drawn across a screen)");
    }
}
