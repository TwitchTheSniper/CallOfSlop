using System.Text.Json;

namespace CallOfSlop;

/// <summary>What a module socket takes (its Kind setting; Constructs/ModuleSockets.json "kinds", the wire's order).</summary>
public enum ModKind : byte
{
    Generic,
    Engine,
    Reactor,
    Turret,
}

/// <summary>How a module socket's module is mounted (its Style setting; "styles", the wire's order).</summary>
public enum ModStyle : byte
{
    Proud, // the plate sits on the hull and the module stands out
    Flush, // the plate at the foot of a well, the module's face level with the hull
    Opening, // the plate at the foot of a well cut through the hull, the module below and above the surface
}

/// <summary>What a module is, read from its parts ("tags", the wire's order: a bit each in <see cref="ModuleTags"/>).</summary>
public enum ModTag : byte
{
    Engine,
    Reactor,
    Turret,
    TurretBay,
    Missile,
    Armour,
    Harvest,
    Cargo,
    PowerStore,
    Sensor,
    LifeSupport,
    Utility,
    COUNT,
}

/// <summary>
/// The module socket standard's numbers (Constructs/ModuleSockets.json, docs/sandbox/design/module-sockets.md 2 and 13.1):
/// the sizes, the ratings by area, the kinds, styles and tags, the depth and room steps, the cut's cost, the hand fit's
/// limits and the fitters. Every number that depends on size is a rule here, not a list, so a bigger size is a key, not
/// code. The defaults match the file; a refused file logs a REFUSED line and the defaults are used.
/// </summary>
public sealed class ModuleSocketTable
{
    public const string Format = "call-of-slop-module-sockets/1";
    public const int MostSide = 30; // the formats' planned limit (whole metres): sockets and modules up to 30x30
    public const int BuiltSide = 3; // what the part kit builds now (1x1 to 3x3)

    public int Most = MostSide;
    public List<(int W, int L)> Built = [(1, 1), (1, 2), (1, 3), (2, 2), (2, 3), (3, 3)];
    public float MassPerM2 = 3000, ThrustPerM2 = 250_000;
    public float DepthStep = 0.25f, DepthMost = 2f; // depth: steps, and at most this x the short side
    public float AboveStep = 0.5f, AboveMost = 4f; // above: steps, and at most this x the long side
    public float Side = 0.05f; // side clearance of the room (m)
    public float CutSecondsPerMetrePer10cm = 2, CutWattsPerMetre = 4000;
    public int CutRingUpTo = 3;
    public int HandUpToW = 2, HandUpToL = 2;
    public float HandSeconds = 5, HandPerTonne = 1, HandPull = 2.5f;
    public List<Fitter> Fitters = [new("S", 1.5f, 8, 20000, 3, 3, 6, 0.1f, 0.25f, 8000)];
    public float BuyLow = 0.7f, BuyHigh = 1f, SellLow = 1.2f, SellHigh = 1.5f;
    public string Problem = "";

    /// <summary>The S fitter's row (the only one built: the first).</summary>
    public Fitter FitterS => Fitters.Count > 0 ? Fitters[0] : DefaultS;
    private static readonly Fitter DefaultS = new("S", 1.5f, 8, 20000, 3, 3, 6, 0.1f, 0.25f, 8000);

    public sealed record Fitter(string Name, float ReachMin, float ReachMax, float Payload, int W, int L, float Height, float Cut, float Weld, float Power);

    public static readonly string[] KindNames = ["generic", "engine", "reactor", "turret"];
    public static readonly string[] KindLabels = ["Any module", "Engine", "Reactor", "Turret"];
    public static readonly string[] StyleNames = ["proud", "flush", "opening"];
    public static readonly string[] StyleLabels = ["Proud", "Flush", "Opening"];
    public static readonly string[] TagNames = ["engine", "reactor", "turret", "turret-bay", "missile", "armour", "harvest", "cargo", "power-store", "sensor", "life-support", "utility"];

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "ModuleSockets.json");

    private static ModuleSocketTable? _current;

    /// <summary>The table in use (loaded once).</summary>
    public static ModuleSocketTable Current => _current ??= Load(FilePath);

    // ---- The size rules ---------------------------------------------------------------------------------------------

    public static bool ValidSize(int w, int l) => w >= 1 && l >= w && l <= MostSide;

    /// <summary>The most a module of this footprint may weigh (kg).</summary>
    public float MassRating(int w, int l) => MassPerM2 * w * l;

    /// <summary>The most a module of this footprint may push or recoil (N).</summary>
    public float ThrustRating(int w, int l) => ThrustPerM2 * w * l;

    /// <summary>The turns about the normal a module may sit at: 4 if square, else 2 (1 when keyed).</summary>
    public static int Turns(int w, int l, bool keyed) => keyed ? 1 : w == l ? 4 : 2;

    /// <summary>The proud room's height over the plate when the setting is 0: twice the short side.</summary>
    public static float DefaultAbove(int w) => 2 * w;

    public float MostDepth(int w) => DepthMost * w;

    public float MostAbove(int l) => AboveMost * l;

    /// <summary>Is a module of this footprint fitted by hand (up to the hand size; its mass is the physics gun's to say).</summary>
    public bool HandSize(int w, int l) => w <= HandUpToW && l <= HandUpToL;

    /// <summary>Seconds a hand fit takes in Survival (5 s and 1 s a tonne).</summary>
    public float HandTime(float kg) => HandSeconds + HandPerTonne * kg / 1000f;

    public static string SizeText(int w, int l) => $"{w}x{l}";

    public static bool TryParseSize(string s, out int w, out int l)
    {
        w = l = 0;
        var x = s.IndexOf('x');
        return x > 0 && int.TryParse(s.AsSpan(0, x), out w) && int.TryParse(s.AsSpan(x + 1), out l) && ValidSize(w, l);
    }

    // ---- Loading ----------------------------------------------------------------------------------------------------

    public static ModuleSocketTable Load(string path)
    {
        var t = new ModuleSocketTable();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "_comment" or "sizes" or "rating" or "kinds" or "styles" or "tags" or "depth" or "above" or "side" or "cut" or "hand" or "fitters" or "price"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            var sizes = root.GetProperty("sizes");
            t.Most = (int)Num(sizes, "most", 1, MostSide);
            t.Built.Clear();
            foreach (var s in sizes.GetProperty("built").EnumerateArray())
            {
                if (!TryParseSize(s.GetString() ?? "", out var w, out var l) || l > t.Most)
                    throw new FormatException($"\"sizes.built\": \"{s.GetString()}\" is not W x L whole metres, 1 <= W <= L <= {t.Most}");
                t.Built.Add((w, l));
            }
            var rating = root.GetProperty("rating");
            t.MassPerM2 = Num(rating, "massPerM2", 1, 1e6f);
            t.ThrustPerM2 = Num(rating, "thrustPerM2", 1, 1e9f);
            Names(root, "kinds", KindNames);
            Names(root, "styles", StyleNames);
            Names(root, "tags", TagNames);
            var depth = root.GetProperty("depth");
            t.DepthStep = Num(depth, "step", 0.05f, 2);
            t.DepthMost = Num(depth, "most", 0.25f, 4);
            var above = root.GetProperty("above");
            t.AboveStep = Num(above, "step", 0.05f, 2);
            t.AboveMost = Num(above, "most", 0.5f, 8);
            t.Side = root.GetProperty("side").GetSingle();
            if (!(t.Side >= 0 && t.Side <= 0.5f))
                throw new FormatException("\"side\" is 0 to 0.5 m");
            var cut = root.GetProperty("cut");
            t.CutSecondsPerMetrePer10cm = Num(cut, "secondsPerMetrePer10cm", 0, 600);
            t.CutWattsPerMetre = Num(cut, "wattsPerMetre", 0, 1e7f);
            t.CutRingUpTo = (int)Num(cut, "ringUpTo", 1, MostSide);
            var hand = root.GetProperty("hand");
            if (!TryParseSize(hand.GetProperty("upTo").GetString() ?? "", out t.HandUpToW, out t.HandUpToL))
                throw new FormatException("\"hand.upTo\" is a size, W x L");
            t.HandSeconds = Num(hand, "seconds", 0, 600);
            t.HandPerTonne = Num(hand, "perTonne", 0, 600);
            t.HandPull = Num(hand, "pull", 0.5f, 10);
            t.Fitters.Clear();
            foreach (var e in root.GetProperty("fitters").EnumerateArray())
            {
                var reach = e.GetProperty("reach");
                var largest = e.GetProperty("largest");
                t.Fitters.Add(new Fitter(e.GetProperty("name").GetString() ?? "?", reach[0].GetSingle(), reach[1].GetSingle(), Num(e, "payload", 1, 1e8f),
                    largest[0].GetInt32(), largest[1].GetInt32(), largest[2].GetSingle(), Num(e, "cut", 0.001f, 10), Num(e, "weld", 0.001f, 10), Num(e, "power", 0, 1e9f)));
            }
            var price = root.GetProperty("price");
            t.BuyLow = price.GetProperty("buy")[0].GetSingle();
            t.BuyHigh = price.GetProperty("buy")[1].GetSingle();
            t.SellLow = price.GetProperty("sell")[0].GetSingle();
            t.SellHigh = price.GetProperty("sell")[1].GetSingle();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            t = new ModuleSocketTable { Problem = $"{Path.GetFileName(path)}: {ex.Message}" };
            Console.WriteLine($"[modules] REFUSED {t.Problem}: the defaults are used");
        }
        return t;
    }

    /// <summary>A list whose order is the wire's: it must start with the names the code knows, in that order (new ones go at the end, in code first).</summary>
    private static void Names(JsonElement root, string key, string[] known)
    {
        var list = root.GetProperty(key);
        if (list.GetArrayLength() != known.Length)
            throw new FormatException($"\"{key}\" lists {list.GetArrayLength()} names: the game knows {known.Length} ({string.Join(", ", known)})");
        var i = 0;
        foreach (var e in list.EnumerateArray())
            if (e.GetString() != known[i++])
                throw new FormatException($"\"{key}\"[{i - 1}] is \"{e.GetString()}\": the order is the wire's ({string.Join(", ", known)})");
    }

    private static float Num(JsonElement e, string key, float min, float max)
    {
        var v = e.GetProperty(key).GetSingle();
        if (!float.IsFinite(v) || v < min || v > max)
            throw new FormatException($"\"{key}\" is {v}, not between {min} and {max}");
        return v;
    }
}
