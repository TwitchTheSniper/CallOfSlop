using System.Text.Json;

namespace CallOfSlop.Story;

/// <summary>
/// One thing in the world a player can look at closely (docs/story/STORY.md "Lore"): a part of its own (from the part
/// kit's lore parts) stood in one of the colony's buildings, and the words a player reads when they look at it. Looking
/// is the relation <c>Inspected</c> from the player towards the item's Thing ("lore ID"), so topics and storylets can ask
/// for it (<c>"?them Inspected 'lore edie-tally'"</c>): reading something is how a new question opens with someone.
/// </summary>
public sealed class LoreDef
{
    public string Id = "", Part = "", Title = "", File = "", In = "";
    public int Index;
    public ulong Key; // the item's Thing ("lore:ID"), which Inspected points at
    public string[] Text = []; // the paragraphs read
    public float[] At = []; // in "in"'s own frame, metres (empty: the item isn't placed by the game)
    public float Yaw; // degrees about up, 0: the part's front faces the building's -z
    public StoryEffect[] Then = []; // the first look only (slots: ?me the player, then named things)
    public int Learn = -1; // a named thing the player learns of on the first look (a Lead)
    public string[] Named = [];
    public string NoticedBy = ""; // someone who sees the player at it and says something (their body within NoticedReach)
    public float NoticedReach = 12f;
    public string[] NoticedSay = []; // {me} the person, {them} the player
    public StoryEffect[] NoticedThen = []; // slots: ?me the person, ?them the player
    public StoryQuery? NoticedIf; // over ?me (the person) and ?them (the player)
    public string Opens = ""; // "jobs": the use key opens the job board (UI/UiJobBoard.cs) with the text above it, not the words alone
    public string OpensOn = ""; // "jobs:FACTION": the board opens on that faction's tab (the one that posts it)
}

/// <summary>The lore items from Story/Lore/*.json (format <see cref="Format"/>). A bad file is refused whole with a reason.</summary>
public sealed class LoreRules
{
    public const string Format = "call-of-slop-lore/1";
    public const int MaxItems = 200, MaxParagraphs = 6, MaxParagraph = 420, MaxTitle = 48;

    public LoreDef[] Items = [];
    public int Inspected = -1, Lead = -1;
    private readonly Dictionary<string, int> _byPart = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The item a part (by its name) is, or -1. Every peer: the client asks this for the part under the crosshair.</summary>
    public int ByPart(string partName) => _byPart.TryGetValue(partName, out var i) ? i : -1;

    public LoreDef? Find(string id) => Array.Find(Items, t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] FileKeys = ["format", "about", "items"];
    private static readonly string[] ItemKeys = ["id", "part", "title", "text", "in", "at", "yaw", "then", "learn", "noticed", "opens"];
    private static readonly string[] NoticedKeys = ["by", "reach", "if", "say", "then"];

    /// <summary>Story/Lore/*.json under <paramref name="folder"/>. Errors go to the rules' list.</summary>
    public static LoreRules Load(StoryRules rules, string folder)
    {
        var lr = new LoreRules { Inspected = rules.Relation("Inspected")?.Id ?? -1, Lead = rules.Relation("Lead")?.Id ?? -1 };
        var files = StoryRules.Files(Path.Combine(folder, "Lore"));
        if (files.Length == 0)
            return lr;
        if (lr.Inspected < 0)
        {
            rules.Errors.Add("Lore: the relation Inspected is missing (Story/Relations/Talk.json): no lore");
            return lr;
        }
        var all = new List<LoreDef>();
        foreach (var f in files)
        {
            var file = $"Lore/{Path.GetFileName(f)}";
            try
            {
                using var doc = StoryRules.Parse(StoryRules.ReadFile(f), file);
                var root = doc.RootElement;
                StoryRules.Keys(root, FileKeys, file);
                var format = StoryRules.Str(root, "format", file, true);
                if (format != Format)
                    throw new StoryException($"{file}: \"format\" is \"{format}\": this build reads \"{Format}\"");
                var mine = new List<LoreDef>();
                foreach (var e in StoryRules.List(root, "items", file, true))
                    mine.Add(Read(rules, e, file, all.Count + mine.Count, all.Concat(mine)));
                all.AddRange(mine);
            }
            catch (StoryException ex)
            {
                rules.Errors.Add(ex.Message);
                Console.WriteLine($"[story] lore skipped: {ex.Message}");
            }
        }
        if (all.Count > MaxItems)
        {
            rules.Errors.Add($"Lore: {all.Count} items, {MaxItems} at most: none loaded");
            return lr;
        }
        lr.Items = all.ToArray();
        foreach (var t in lr.Items)
            lr._byPart[t.Part] = t.Index;
        return lr;
    }

    private static LoreDef Read(StoryRules rules, JsonElement e, string file, int index, IEnumerable<LoreDef> before)
    {
        if (e.ValueKind != JsonValueKind.Object)
            throw new StoryException($"{file}: an item is like {{\"id\": \"edie-tally\", \"part\": \"Scratched Tally Board\", \"title\": \"...\", \"text\": [...]}}");
        var id = StoryRules.Str(e, "id", $"{file}: an item", true);
        var where = $"{file}: item {id}";
        StoryRules.Keys(e, ItemKeys, where);
        if (id.Length is 0 or > 48 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new StoryException($"{where}: an id is letters, digits, - and _, at most 48");
        if (before.Any(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            throw new StoryException($"{where}: another item has this id");
        var t = new LoreDef
        {
            Id = id, Index = index, File = file, Key = StoryFactions.Key("lore:" + id),
            Part = StoryRules.Str(e, "part", where, true), Title = StoryRules.Str(e, "title", where, true), In = StoryRules.Str(e, "in", where),
        };
        if (before.Any(o => o.Part.Equals(t.Part, StringComparison.OrdinalIgnoreCase)))
            throw new StoryException($"{where}: \"part\": another item is a {t.Part} (one item a part)");
        Ascii(t.Title, MaxTitle, where, "title");
        t.Text = StoryRules.List(e, "text", where, true).Select(v => v.ValueKind == JsonValueKind.String ? Ascii(v.GetString() ?? "", MaxParagraph, where, "text") : throw new StoryException($"{where}: each of \"text\" is a paragraph")).ToArray();
        if (t.Text.Length is 0 or > MaxParagraphs)
            throw new StoryException($"{where}: \"text\" is 1 to {MaxParagraphs} paragraphs");
        if (e.TryGetProperty("at", out var at))
        {
            if (at.ValueKind != JsonValueKind.Array || at.GetArrayLength() != 3 || at.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number || Math.Abs(v.GetDouble()) > 500))
                throw new StoryException($"{where}: \"at\" is [x, y, z], metres in the building's frame");
            t.At = at.EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            if (t.In.Length == 0)
                throw new StoryException($"{where}: \"at\" wants \"in\", the building it stands in");
        }
        var opens = StoryRules.Str(e, "opens", where);
        if (opens.Length > 0)
        {
            var colon = opens.IndexOf(':');
            (t.Opens, t.OpensOn) = colon < 0 ? (opens, "") : (opens[..colon].Trim(), opens[(colon + 1)..].Trim());
            if (t.Opens != "jobs")
                throw new StoryException($"{where}: \"opens\" is \"jobs\" or \"jobs:FACTION\" (the job board, on that faction's tab) or left out");
            if (t.OpensOn.Length > 0 && rules.Factions.Find(t.OpensOn) == null)
                throw new StoryException($"{where}: \"opens\": there is no faction \"{t.OpensOn}\" (Story/Factions)");
        }
        if (e.TryGetProperty("yaw", out var yaw))
            t.Yaw = yaw.ValueKind == JsonValueKind.Number && yaw.GetDouble() is var y and >= -360 and <= 360 ? (float)y : throw new StoryException($"{where}: \"yaw\" is degrees, -360 to 360");

        var named = new List<string>();
        static int PlayerSlot(string s) => s == "?me" ? 0 : -1;
        t.Then = StoryRules.List(e, "then", where).Select(v => v.ValueKind == JsonValueKind.String
            ? StoryletRules.ParseNamed(rules, v.GetString() ?? "", where, named, 2, PlayerSlot)
            : throw new StoryException($"{where}: each \"then\" is text, like \"?me Suspicion +1\"")).ToArray();
        var learn = StoryRules.Str(e, "learn", where);
        if (learn.Length > 0)
            t.Learn = StoryletRules.NamedSlot(learn.Trim('\''), named, 2);

        if (e.TryGetProperty("noticed", out var n))
        {
            var at2 = $"{where}: \"noticed\"";
            if (n.ValueKind != JsonValueKind.Object)
                throw new StoryException($"{at2} is like {{\"by\": \"Officer Crumb\", \"say\": [\"Oi!\"]}}");
            StoryRules.Keys(n, NoticedKeys, at2);
            t.NoticedBy = StoryRules.Str(n, "by", at2, true);
            if (n.TryGetProperty("reach", out var r))
                t.NoticedReach = r.ValueKind == JsonValueKind.Number && r.GetDouble() is var rr and > 0 and <= 60 ? (float)rr : throw new StoryException($"{at2}: \"reach\" is metres, up to 60");
            var cond = StoryRules.Str(n, "if", at2);
            if (cond.Length > 0)
                try
                {
                    t.NoticedIf = StoryQuery.Compile(cond, rules, ["me", "them"]);
                }
                catch (StoryException ex)
                {
                    throw new StoryException($"{at2}: \"if\": \"{cond}\": {ex.Message}");
                }
            t.NoticedSay = StoryRules.List(n, "say", at2, true).Select(v => v.ValueKind == JsonValueKind.String ? Line(v.GetString() ?? "", at2) : throw new StoryException($"{at2}: each of \"say\" is a line")).ToArray();
            if (t.NoticedSay.Length is 0 or > LineRules.MaxLines)
                throw new StoryException($"{at2}: 1 to {LineRules.MaxLines} lines");
            static int PersonSlot(string s) => s == "?me" ? 0 : s == "?them" ? 1 : -1;
            t.NoticedThen = StoryRules.List(n, "then", at2).Select(v => v.ValueKind == JsonValueKind.String
                ? StoryletRules.ParseNamed(rules, v.GetString() ?? "", at2, named, 2, PersonSlot)
                : throw new StoryException($"{at2}: each \"then\" is text, like \"?me Likes ?them +1\"")).ToArray();
        }
        if (2 + named.Count > StoryRules.MaxVars)
            throw new StoryException($"{where}: {named.Count} named things, {StoryRules.MaxVars - 2} at most");
        t.Named = named.ToArray();
        return t;
    }

    private static string Ascii(string s, int max, string where, string key)
    {
        if (s.Length is 0 || s.Length > max || s.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: \"{key}\": printable ASCII, 1 to {max} characters");
        return s;
    }

    /// <summary>A said line: printable ASCII, short, filling only {me}, {them} (and .first).</summary>
    private static string Line(string line, string where)
    {
        if (line.Length is 0 or > LineRules.MaxLength || line.Any(c => c < ' ' || c > '~'))
            throw new StoryException($"{where}: line \"{line}\": printable ASCII, 1 to {LineRules.MaxLength} characters");
        for (var i = line.IndexOf('{'); i >= 0; i = line.IndexOf('{', i + 1))
        {
            var end = line.IndexOf('}', i);
            var name = end > i ? line[(i + 1)..end] : "";
            if (name is not ("me" or "me.first" or "them" or "them.first"))
                throw new StoryException($"{where}: line \"{line}\": {{{name}}}: a line fills {{me}}, {{them}}, {{me.first}} and {{them.first}}");
        }
        return line;
    }
}

/// <summary>
/// Lore at play (docs/story/STORY.md "Lore"): a player looking at an item. What they have looked at is the Inspected
/// relation (player to the item's Thing), so it saves with the world. Host only.
/// </summary>
public sealed class StoryLore
{
    private readonly StoryEngine _e;
    private int _seed = 4242;

    public StoryLore(StoryEngine engine) => _e = engine;

    private LoreRules R => _e.Rules.Lore;
    private StoryWorld W => _e.World;

    /// <summary>How many times a player has looked at an item.</summary>
    public int Count(int player, LoreDef t)
    {
        var thing = W.Find(StoryKind.Thing, t.Key);
        return thing < 0 || R.Inspected < 0 ? 0 : W.Get(player, R.Inspected, thing);
    }

    /// <summary>A player looks at an item: counted, and on the first look its effects and its lead. Returns how many looks before this one.</summary>
    public int Inspect(int player, int item)
    {
        if ((uint)item >= (uint)R.Items.Length || player < 0 || R.Inspected < 0)
            return -1;
        var t = R.Items[item];
        var before = Count(player, t);
        W.Add(player, R.Inspected, W.Get(StoryKind.Thing, t.Key, "lore " + t.Id, _e.Time), 1);
        if (before > 0)
            return before;
        Span<int> b = stackalloc int[StoryRules.MaxVars];
        b.Fill(-1);
        b[0] = player;
        Name(t, b);
        if (t.Then.Length > 0)
            W.Apply(t.Then, b);
        if (t.Learn >= 0 && R.Lead >= 0 && b[t.Learn] >= 0)
            W.Add(player, R.Lead, b[t.Learn], 1);
        return 0;
    }

    /// <summary>
    /// The person who notices a player at an item says their line (the first look only; <paramref name="person"/> is
    /// their entity, already found near enough): null when they don't (the "if" fails), else the line, filled.
    /// </summary>
    public string? Noticed(int player, int item, int person)
    {
        if ((uint)item >= (uint)R.Items.Length || person < 0)
            return null;
        var t = R.Items[item];
        if (t.NoticedSay.Length == 0)
            return null;
        Span<int> b = stackalloc int[StoryRules.MaxVars];
        b.Fill(-1);
        (b[0], b[1]) = (person, player);
        if (t.NoticedIf != null && !t.NoticedIf.Any(W, b))
            return null;
        Name(t, b);
        if (t.NoticedThen.Length > 0)
            W.Apply(t.NoticedThen, b);
        _seed = _seed * 1103515245 + 12345;
        return _e.Topics.Fill(t.NoticedSay[(int)((uint)_seed >> 8) % t.NoticedSay.Length], person, player);
    }

    private void Name(LoreDef t, Span<int> b)
    {
        for (var i = 0; i < t.Named.Length; i++)
        {
            var id = W.Find(t.Named[i]);
            b[2 + i] = id >= 0 ? id : W.Get(StoryKind.Thing, StoryFactions.Key("fact:" + t.Named[i]), t.Named[i], _e.Time);
        }
    }
}
