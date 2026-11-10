using System.Text.Json;
using CallOfSlop.Story;

namespace CallOfSlop;

/// <summary>
/// The story's fragments and the sealed data centres that hold them (docs/story/FRAGMENTS.md; data <c>Story/Fragments/Fragments.json</c>,
/// format <c>call-of-slop-fragments/1</c>). Read once on every peer: the host places the sites and decides who holds what
/// (Game/GameStoryFragments.cs), a client needs the texts for its Logbook and the terminals' screens. A bad file is refused with a
/// reason (the log, and <c>--check-story</c>) and there are no fragments.
/// </summary>
public sealed class FragmentScript
{
    public const string Format = "call-of-slop-fragments/1";
    public const int MaxFragments = 16, MaxSites = 16;

    public sealed class Fragment
    {
        public string Id = "", Title = "", Kind = "", Hint = ""; // its id is also the story world's name of its Thing ("frag.threat"), which storylets name it by
        public string[] Text = []; // paragraphs
    }

    public sealed class Site
    {
        public string Id = "", Name = "", Blueprint = "", Lead = "", Source = "";
        public int Fragment; // an index into Fragments
        public float From = 160, To = 320;
        public float Multiply = 3, Add = 4, Every = 6;
        public int Locks = 3;
        public string Arrive = "", Power = "", Decoded = "", Copied = "";

        /// <summary>What the key input must carry for this carrier.</summary>
        public float Answer(float carrier) => carrier * Multiply + Add;

        /// <summary>The rule as the terminal's screen says it ("CARRIER x 3 + 4").</summary>
        public string Rule = "";
    }

    public Fragment[] Fragments = [];
    public Site[] Sites = [];
    public string Problem = "";

    public int Find(string id)
    {
        for (var i = 0; i < Fragments.Length; i++)
            if (Fragments[i].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>A fragment's index from its story Thing's name (its id), or -1.</summary>
    public int FromThing(string name) => Find(name);

    public static string FilePath => Path.Combine(AppFolders.Data, StoryRules.FolderName, "Fragments", "Fragments.json");

    private static FragmentScript? _current;

    /// <summary>The game's fragments (a bad file: none, and <see cref="Problem"/> says why).</summary>
    public static FragmentScript Current
    {
        get
        {
            if (_current != null)
                return _current;
            try
            {
                _current = File.Exists(FilePath) ? Read(File.ReadAllText(FilePath), "Fragments/Fragments.json") : new FragmentScript { Problem = "no Fragments/Fragments.json" };
            }
            catch (StoryException ex)
            {
                Console.WriteLine($"[story] {ex.Message}: no fragments");
                _current = new FragmentScript { Problem = ex.Message };
            }
            return _current;
        }
    }

    /// <summary>Reads a fragments file; throws a <see cref="StoryException"/> naming what is wrong.</summary>
    public static FragmentScript Read(string json, string where)
    {
        var t = new FragmentScript();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new StoryException($"{where}: \"format\" must be \"{Format}\"");
            var frags = new List<Fragment>();
            foreach (var e in root.GetProperty("fragments").EnumerateArray())
            {
                var fr = new Fragment
                {
                    Id = Id(e, "id", where), Title = Text(e, "title", where, true), Kind = Text(e, "kind", where, false), Hint = Text(e, "hint", where, false),
                };
                if (!e.TryGetProperty("text", out var tx) || tx.ValueKind != JsonValueKind.Array || tx.GetArrayLength() is 0 or > 12)
                    throw new StoryException($"{where}: fragment \"{fr.Id}\": \"text\" is a list of 1 to 12 paragraphs");
                fr.Text = [.. tx.EnumerateArray().Select(x => Ascii(x.GetString() ?? "", where))];
                if (frags.Exists(x => x.Id.Equals(fr.Id, StringComparison.OrdinalIgnoreCase)))
                    throw new StoryException($"{where}: two fragments are \"{fr.Id}\"");
                frags.Add(fr);
            }
            if (frags.Count is 0 or > MaxFragments)
                throw new StoryException($"{where}: 1 to {MaxFragments} fragments");
            t.Fragments = [.. frags];
            var sites = new List<Site>();
            if (root.TryGetProperty("sites", out var ss))
                foreach (var e in ss.EnumerateArray())
                {
                    var s = new Site
                    {
                        Id = Id(e, "id", where), Name = Text(e, "name", where, true), Blueprint = Text(e, "blueprint", where, true), Lead = Text(e, "lead", where, false),
                        Source = Text(e, "source", where, true), Arrive = Text(e, "arrive", where, false), Power = Text(e, "power", where, false),
                        Decoded = Text(e, "decoded", where, false), Copied = Text(e, "copied", where, false),
                    };
                    var at = $"{where}: site \"{s.Id}\"";
                    s.Fragment = t.Find(Text(e, "fragment", where, true));
                    if (s.Fragment < 0)
                        throw new StoryException($"{at}: \"fragment\" names none of the fragments");
                    if (sites.Exists(x => x.Id.Equals(s.Id, StringComparison.OrdinalIgnoreCase)))
                        throw new StoryException($"{at}: two sites have this id");
                    if (sites.Exists(x => x.Fragment == s.Fragment))
                        throw new StoryException($"{at}: another site holds {t.Fragments[s.Fragment].Id} already");
                    s.From = Num(e, "from", s.From, 30, 2000, at);
                    s.To = Num(e, "to", s.To, s.From + 10, 3000, at);
                    s.Multiply = Num(e, "multiply", s.Multiply, -9, 9, at);
                    s.Add = Num(e, "add", s.Add, -99, 99, at);
                    s.Every = Num(e, "every", s.Every, 2, 60, at);
                    s.Locks = (int)Num(e, "locks", s.Locks, 1, 8, at);
                    if (s.Multiply is > -1 and < 1 || s.Multiply != MathF.Round(s.Multiply) || s.Add != MathF.Round(s.Add))
                        throw new StoryException($"{at}: \"multiply\" and \"add\" are whole numbers, and multiply isn't 0");
                    s.Rule = s.Add == 0 ? $"CARRIER x {s.Multiply:0}" : $"CARRIER x {s.Multiply:0} {(s.Add > 0 ? '+' : '-')} {MathF.Abs(s.Add):0}";
                    sites.Add(s);
                }
            if (sites.Count > MaxSites)
                throw new StoryException($"{where}: at most {MaxSites} sites");
            t.Sites = [.. sites];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new StoryException($"{where}: {ex.Message}");
        }
        return t;
    }

    private static string Id(JsonElement e, string key, string where)
    {
        var s = Text(e, key, where, true);
        if (!s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            throw new StoryException($"{where}: \"{s}\": an id is letters, digits, '.', - and _");
        return s;
    }

    private static string Text(JsonElement e, string key, string where, bool need)
    {
        if (!e.TryGetProperty(key, out var v))
            return need ? throw new StoryException($"{where}: something has no \"{key}\"") : "";
        var s = v.GetString() ?? "";
        if (need && s.Length == 0)
            throw new StoryException($"{where}: \"{key}\" is empty");
        return Ascii(s, where);
    }

    private static string Ascii(string s, string where) =>
        s.All(c => c >= ' ' && c < 127) ? s : throw new StoryException($"{where}: player-visible text is ASCII (\"{s}\")");

    private static float Num(JsonElement e, string key, float fallback, float min, float max, string where)
    {
        if (!e.TryGetProperty(key, out var v))
            return fallback;
        var x = v.GetSingle();
        return x >= min && x <= max ? x : throw new StoryException($"{where}: \"{key}\" is {min} to {max}");
    }

    // ---- The terminals' screens (every peer; PanelSources "story.terminal") ----

    /// <summary>
    /// The sites' constructs as this peer knows them (index: the site; 0: none): the host sets it when it places or loads them, a client
    /// hears it in <see cref="Msg.Fragments"/>. A terminal on no site is a dead one.
    /// </summary>
    public static readonly ushort[] SiteBodies = new ushort[MaxSites];

    /// <summary>The site whose construct this is, or null.</summary>
    public static Site? SiteOf(ushort body)
    {
        var sites = Current.Sites;
        for (var k = 0; k < sites.Length; k++)
            if (body != 0 && SiteBodies[k] == body)
                return sites[k];
        return null;
    }

    /// <summary>
    /// What a terminal's <see cref="CPart.Reading"/> carries to every peer: how far the decode has got (0 to the site's locks; 9: decoded)
    /// times 10000, plus the key it hears, clamped to +-999 (Game/GameStoryFragments.cs writes it).
    /// </summary>
    public const int ReadingBand = 10000, DecodedBand = 9;

    public static float Reading(int band, float key) => band * ReadingBand + Math.Clamp(MathF.Round(key * 10) / 10, -999, 999);

    public static int BandOf(float reading) => (int)MathF.Round(reading / ReadingBand);

    public static float KeyOf(float reading) => reading - BandOf(reading) * ReadingBand;

    /// <summary>A terminal's screen: no power, the cipher lock (its carrier, the rule, the key heard, the locks), or decoded.</summary>
    public static void FillTerminal(ConstructWorld world, Construct c, int part, PanelDef panel, PanelCard card)
    {
        ref var p = ref c.Parts[part];
        if (p.Broken)
        {
            card.State = PanelState.Fault;
            card.Add(0, "TERMINAL - DAMAGED");
            return;
        }
        var site = SiteOf(c.Id);
        if (site == null)
        {
            card.State = PanelState.Off;
            card.Add(0, "TERMINAL - NO RECORD");
            card.Add(1, "Nothing on this one.");
            return;
        }
        if (!c.Electrical || !ConstructPower.On(c, part))
        {
            card.State = PanelState.Fault;
            card.Add(0, "TERMINAL - NO POWER");
            card.Add(1, "Fit a battery to this");
            card.Add(2, "building's frame to wake it.");
            return;
        }
        var band = BandOf(p.Reading);
        if (band >= DecodedBand)
        {
            card.State = PanelState.Ok;
            card.Add(0, "DECODED");
            card.Add(1, Current.Fragments[site.Fragment].Title);
            card.Add(3, "Stand at the terminal");
            card.Add(4, "to copy the record.");
            return;
        }
        card.State = band > 0 ? PanelState.Working : PanelState.Wants;
        card.Add(0, "CIPHER LOCK ").Add(0, Math.Max(band, 0)).Add(0, '/').Add(0, site.Locks);
        card.Add(1, "CARRIER OUT  ").Add(1, (int)MathF.Round(p.Out));
        card.Add(2, "SEND ").Add(2, site.Rule);
        card.Add(3, "KEY IN  ").AddFixed(3, KeyOf(p.Reading), 1);
        card.Add(4, "Wire the answer into KEY.");
        card.Add(5, "It changes: build the sum.");
    }
}
