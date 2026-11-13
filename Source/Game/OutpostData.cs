using System.Text.Json;

namespace CallOfSlop;

/// <summary>A building of an outpost: a blueprint at a spot in the site's frame (m right of and towards its facing), turned by its yaw.</summary>
public readonly record struct OutpostBuilding(string Blueprint, float X, float Z, float Yaw);

/// <summary>
/// One of an outpost's people: a name (a title first, "Officer ", dresses them: Cosmetics/uniforms.json), what they are,
/// and either a post (a spot, faced one way) or a patrol (a loop of spots walked for ever, standing a while at each).
/// </summary>
public sealed class OutpostPerson
{
    public string Name = "", About = "";
    public float PostX, PostZ, Face; // the post (site frame) and which way it faces (degrees from the site's facing)
    public (float X, float Z)[] Route = []; // a patrol's spots in order (none: a post)
    public double Stand = 6; // s at each spot of a patrol
    public string[] Self = []; // "what do you do here?" (none: the site's)
}

/// <summary>The lines an outpost's people say, by the player's standing with its faction and by the moment.</summary>
public sealed class OutpostLines
{
    // Said to a player who comes up to them, by standing: the faction's lowest tier (they fight), the next, the middle, above.
    public string[] Hostile = [], Cold = [], Neutral = [], Friendly = [];
    public string[] Idle = []; // now and then, with a player near
    public string[] Hurt = []; // shot at
    public string[] Place = [], Self = [], Advice = [], News = []; // the talk panel's questions
    public string[] Snub = []; // the talk panel, to someone they would rather shoot
}

/// <summary>
/// One outpost: a faction's small place out in the world (a checkpoint, a camp, a relay), placed by bearing and distance
/// from the colony's gate or by latitude and longitude on a planetoid, with its buildings, its people and their lines.
/// </summary>
public sealed class OutpostSite
{
    public string Name = "", Faction = "", About = "";
    public bool FromColony; // placed from the colony's gate (else Lat, Lon on the planetoid)
    public float Bearing, Distance, Lat, Lon;
    public float Flat = 12; // m: the ground within this of the middle must be level enough (the spot is moved a little until it is)
    public double Relief = 900; // s before someone killed is replaced (while nobody is near)
    public OutpostBuilding[] Buildings = [];
    public OutpostPerson[] People = [];
    public OutpostLines Lines = new();
}

/// <summary>
/// The outposts of one world (Story/Outposts/*.json, format <see cref="Format"/>; docs/story/OUTPOSTS.md): the factions'
/// small places beyond the colony. A bad file is refused with a reason and that world has no outposts.
/// </summary>
public sealed class OutpostData
{
    public const string Format = "call-of-slop-outposts/1";
    public const int MaxSites = 16, MaxPeople = 6, MaxBuildings = 8, MaxRoute = 8, MaxLines = 16;

    public string Map = "", Problem = "", File = "";
    public float Wake = 150, Sleep = 200; // m: a site's people come when a player is this close, and go once nobody is within Sleep
    public OutpostSite[] Sites = [];

    private static List<OutpostData>? _all;

    /// <summary>Every outposts file (one a world), loaded once.</summary>
    public static List<OutpostData> All => _all ??= LoadAll(Path.Combine(AppFolders.Data, "Story", "Outposts"));

    /// <summary>The outposts of this map, or null (none, or its file was refused).</summary>
    public static OutpostData? For(string map)
    {
        foreach (var d in All)
            if (d.Problem.Length == 0 && d.Map.Equals(map, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    public static List<OutpostData> LoadAll(string dir)
    {
        var o = new List<OutpostData>();
        if (!Directory.Exists(dir))
            return o;
        foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            OutpostData d;
            try
            {
                d = Parse(System.IO.File.ReadAllText(path));
                Console.WriteLine($"[outpost] {Path.GetFileName(path)}: {d.Sites.Length} outposts on {d.Map}, {d.Sites.Sum(s => s.People.Length)} people");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException)
            {
                Console.WriteLine($"[outpost] {Path.GetFileName(path)} refused: {ex.Message}");
                d = new OutpostData { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
            }
            d.File = Path.GetFileName(path);
            o.Add(d);
        }
        return o;
    }

    public static OutpostData Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
            throw new FormatException($"format must be \"{Format}\"");
        var d = new OutpostData { Map = Str(root, "map", "file", need: true) };
        d.Wake = (float)Num(root, "wake", "file", 150, 40, 600);
        d.Sleep = (float)Num(root, "sleep", "file", d.Wake + 50, d.Wake + 10, 800);
        var sites = List(root, "sites", MaxSites, "file");
        d.Sites = new OutpostSite[sites.Count];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sites.Count; i++)
        {
            var e = sites[i];
            var where = $"sites[{i}]";
            var st = new OutpostSite
            {
                Name = Str(e, "name", where, need: true),
                Faction = Str(e, "faction", where, need: true),
                About = Str(e, "about", where),
                Flat = (float)Num(e, "flat", where, 12, 2, 40),
                Relief = Num(e, "relief", where, 900, 30, 86400),
            };
            where = $"outpost \"{st.Name}\"";
            if (!names.Add(st.Name))
                throw new FormatException($"{where}: two outposts of that name");
            var from = Str(e, "from", where);
            if (from.Equals("colony", StringComparison.OrdinalIgnoreCase))
            {
                st.FromColony = true;
                st.Bearing = (float)Num(e, "bearing", where, double.NaN, -360, 360);
                st.Distance = (float)Num(e, "distance", where, double.NaN, 100, 5000);
                if (float.IsNaN(st.Bearing) || float.IsNaN(st.Distance))
                    throw new FormatException($"{where}: \"from\": \"colony\" needs a \"bearing\" and a \"distance\"");
            }
            else if (from.Length > 0)
                throw new FormatException($"{where}.from: \"colony\" (or leave it out and give \"lat\" and \"lon\")");
            else
            {
                st.Lat = (float)Num(e, "lat", where, double.NaN, -90, 90);
                st.Lon = (float)Num(e, "lon", where, double.NaN, -360, 360);
                if (float.IsNaN(st.Lat) || float.IsNaN(st.Lon))
                    throw new FormatException($"{where}: give \"from\": \"colony\" with a bearing and distance, or \"lat\" and \"lon\"");
            }
            var bs = List(e, "buildings", MaxBuildings, where);
            st.Buildings = new OutpostBuilding[bs.Count];
            for (var k = 0; k < bs.Count; k++)
            {
                var w = $"{where}.buildings[{k}]";
                st.Buildings[k] = new OutpostBuilding(Str(bs[k], "blueprint", w, need: true), (float)Num(bs[k], "x", w, 0, -60, 60),
                    (float)Num(bs[k], "z", w, 0, -60, 60), (float)Num(bs[k], "yaw", w, 0, -360, 360));
            }
            var ps = List(e, "people", MaxPeople, where);
            if (ps.Count == 0)
                throw new FormatException($"{where}: nobody in \"people\"");
            st.People = new OutpostPerson[ps.Count];
            for (var k = 0; k < ps.Count; k++)
            {
                var w = $"{where}.people[{k}]";
                var p = new OutpostPerson
                {
                    Name = Str(ps[k], "name", w, need: true),
                    About = Str(ps[k], "about", w),
                    Face = (float)Num(ps[k], "face", w, 0, -360, 360),
                    Stand = Num(ps[k], "stand", w, 6, 0, 600),
                    Self = Strs(ps[k], "self", w),
                };
                if (p.Name.Length > 28)
                    throw new FormatException($"{w}.name: at most 28 characters");
                if (!names.Add("person:" + p.Name))
                    throw new FormatException($"{w}: someone else is called {p.Name}");
                if (ps[k].TryGetProperty("post", out _))
                    (p.PostX, p.PostZ) = Pair(ps[k], "post", w);
                if (ps[k].TryGetProperty("route", out var r))
                {
                    if (r.ValueKind != JsonValueKind.Array || r.GetArrayLength() is < 2 or > MaxRoute)
                        throw new FormatException($"{w}.route: a list of 2 to {MaxRoute} spots, each [x, z]");
                    p.Route = new (float, float)[r.GetArrayLength()];
                    var j = 0;
                    foreach (var spot in r.EnumerateArray())
                        p.Route[j++] = PairOf(spot, $"{w}.route[{j}]", 250);
                    (p.PostX, p.PostZ) = p.Route[0];
                }
                else if (!ps[k].TryGetProperty("post", out _))
                    throw new FormatException($"{w}: give a \"post\" [x, z] or a \"route\"");
                st.People[k] = p;
            }
            if (e.TryGetProperty("lines", out var ln))
            {
                if (ln.ValueKind != JsonValueKind.Object)
                    throw new FormatException($"{where}.lines must be an object");
                var w = $"{where}.lines";
                var l = st.Lines;
                l.Hostile = Strs(ln, "hostile", w);
                l.Cold = Strs(ln, "cold", w);
                l.Neutral = Strs(ln, "neutral", w);
                l.Friendly = Strs(ln, "friendly", w);
                l.Idle = Strs(ln, "idle", w);
                l.Hurt = Strs(ln, "hurt", w);
                l.Place = Strs(ln, "place", w);
                l.Self = Strs(ln, "self", w);
                l.Advice = Strs(ln, "advice", w);
                l.News = Strs(ln, "news", w);
                l.Snub = Strs(ln, "snub", w);
                foreach (var p in ln.EnumerateObject())
                    if (p.Name is not ("hostile" or "cold" or "neutral" or "friendly" or "idle" or "hurt" or "place" or "self" or "advice" or "news" or "snub"))
                        throw new FormatException($"{w}.{p.Name}: not a kind of line (hostile, cold, neutral, friendly, idle, hurt, place, self, advice, news, snub)");
            }
            if (st.Lines.Neutral.Length == 0)
                throw new FormatException($"{where}.lines: no \"neutral\" lines (what they say to a stranger)");
            d.Sites[i] = st;
        }
        return d;
    }

    private static (float, float) Pair(JsonElement e, string key, string where) => PairOf(e.GetProperty(key), $"{where}.{key}", 60);

    private static (float, float) PairOf(JsonElement v, string where, float most)
    {
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2 || v[0].ValueKind != JsonValueKind.Number || v[1].ValueKind != JsonValueKind.Number
            || MathF.Abs(v[0].GetSingle()) > most || MathF.Abs(v[1].GetSingle()) > most)
            throw new FormatException($"{where}: [x, z], each from -{most} to {most} m");
        return (v[0].GetSingle(), v[1].GetSingle());
    }

    private static List<JsonElement> List(JsonElement root, string key, int max, string where)
    {
        var list = new List<JsonElement>();
        if (!root.TryGetProperty(key, out var a))
            return list;
        if (a.ValueKind != JsonValueKind.Array)
            throw new FormatException($"{where}.{key} must be a list");
        foreach (var e in a.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
                throw new FormatException($"{where}.{key}[{list.Count}] must be an object");
            list.Add(e);
        }
        return list.Count <= max ? list : throw new FormatException($"{where}.{key}: at most {max}");
    }

    private static double Num(JsonElement e, string key, string where, double fallback, double min, double max)
    {
        if (!e.TryGetProperty(key, out var v))
            return fallback;
        if (v.ValueKind != JsonValueKind.Number || v.GetDouble() is var n && !(n >= min && n <= max))
            throw new FormatException($"{where}.{key}: a number from {min:0.#} to {max:0.#}");
        return v.GetDouble();
    }

    private static string Str(JsonElement e, string key, string where, bool need = false)
    {
        if (e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            var s = Ascii(v.GetString() ?? "", $"{where}.{key}");
            return s.Length > 0 || !need ? s : throw new FormatException($"{where}.{key} is empty");
        }
        return need ? throw new FormatException($"{where} has no \"{key}\"") : "";
    }

    private static string[] Strs(JsonElement e, string key, string where)
    {
        if (!e.TryGetProperty(key, out var a))
            return [];
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() > MaxLines)
            throw new FormatException($"{where}.{key}: a list of at most {MaxLines} lines");
        var list = new List<string>();
        foreach (var v in a.EnumerateArray())
        {
            var s = v.ValueKind == JsonValueKind.String ? Ascii(v.GetString() ?? "", $"{where}.{key}") : throw new FormatException($"{where}.{key}: text only");
            if (s.Length > 150)
                throw new FormatException($"{where}.{key}: \"{s[..30]}...\" is over 150 characters");
            list.Add(s);
        }
        return list.ToArray();
    }

    private static string Ascii(string s, string where)
    {
        foreach (var c in s)
            if (c is < ' ' or > '~')
                throw new FormatException($"{where}: player text is ASCII only");
        if (s.Length > 300)
            throw new FormatException($"{where}: at most 300 characters");
        return s;
    }
}
