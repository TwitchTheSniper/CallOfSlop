using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// The hull dressing library (docs/sandbox/design/hull-dress.md; Sean, 2026-11-21): the Zero Day's armour, rails and strips, lifted out of
/// its generator (TorpedoFrigateParts.cs) so other ships, the part kit and the game's own Dress step share one look. It works on a
/// <see cref="Band"/>: one flat facet of a hull between two ends, its points in whatever frame the caller uses. Nothing here draws: it
/// lays out what goes where (<see cref="Plates"/>) and gives the corners of each thing (<see cref="SlabOutlines"/>, <see cref="StripCorners"/>,
/// <see cref="ChineRing"/>), and the caller turns those into shapes. The faction looks are data (<see cref="DressStyles"/>, Constructs/DressStyles.json).
///
/// The arithmetic is the Zero Day generator's, step for step: <c>--gen-parts torpedo_frigate_parts --check</c> shows no change, which is
/// the proof that the library makes the real look.
/// </summary>
public static class HullDress
{
    /// <summary>
    /// A facet band: edge A to edge B across, end 0 to end 1 along. <see cref="A0"/> and <see cref="B0"/> are the edges' points at end 0,
    /// <see cref="A1"/> and <see cref="B1"/> at end 1 (metres, in the caller's frame). <see cref="Length"/> is its length along, for spacing
    /// panels and turning a bevel into a fraction (the Zero Day's is its z length).
    /// </summary>
    public readonly record struct Band(double[] A0, double[] B0, double[] A1, double[] B1, double Length);

    /// <summary>One thing a pattern lays on a band: a bevelled slab standing <see cref="D0"/> to <see cref="D1"/> out (its face drawn in by
    /// <see cref="Bevel"/>), or a flat strip <see cref="D0"/> out; u along, v across, both 0 to 1.</summary>
    public readonly record struct Item(bool Strip, double U0, double U1, double V0, double V1, double D0, double D1, double Bevel, string Color, string Finish);

    // ---- geometry

    public static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    public static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    public static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    public static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? [0, 1, 0] : [a[0] / l, a[1] / l, a[2] / l];
    }

    public static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    /// <summary>A point on the band: u along from end 0, v across from edge A.</summary>
    public static double[] On(Band b, double u, double v) => Lerp(Lerp(b.A0, b.B0, v), Lerp(b.A1, b.B1, v), u);

    /// <summary>The band's outward normal at its middle: away from the axis through (<paramref name="axisX"/>, <paramref name="axisY"/>)
    /// along z (a hull's middle line).</summary>
    public static double[] Normal(Band b, double axisX, double axisY)
    {
        var c = On(b, 0.5, 0.5);
        var n = Unit(Cross(Sub(On(b, 0.5, 1), On(b, 0.5, 0)), Sub(On(b, 1, 0.5), On(b, 0, 0.5))));
        double[] axis = [axisX, axisY, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>A slab's two outlines on the band: the inner one <paramref name="d0"/> out over (u0, v0)..(u1, v1), the outer one
    /// <paramref name="d1"/> out and drawn in by <paramref name="bevel"/> metres all round. Each is four corners in order.</summary>
    public static (double[][] Inner, double[][] Outer) SlabOutlines(Band b, double[] n, double u0, double u1, double v0, double v1, double d0, double d1, double bevel)
    {
        var lu = b.Length;
        var lv = Math.Sqrt(Dot(Sub(On(b, 0.5, 1), On(b, 0.5, 0)), Sub(On(b, 0.5, 1), On(b, 0.5, 0))));
        double bu = bevel / lu, bv = bevel / Math.Max(lv, 0.1);
        double[] Off(double[] q, double d) => [q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d];
        double[][] inner = [.. new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) }.Select(t => Off(On(b, t.Item1, t.Item2), d0))];
        double[][] outer = [.. new[] { (u0 + bu, v0 + bv), (u1 - bu, v0 + bv), (u1 - bu, v1 - bv), (u0 + bu, v1 - bv) }.Select(t => Off(On(b, t.Item1, t.Item2), d1))];
        return (inner, outer);
    }

    /// <summary>A flat strip's four corners on the band, <paramref name="d"/> out (a seam, a stripe, a light).</summary>
    public static double[][] StripCorners(Band b, double[] n, double u0, double u1, double v0, double v1, double d)
    {
        double[] Q(double u, double v)
        {
            var q = On(b, u, v);
            return [q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d];
        }
        return [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)];
    }

    /// <summary>
    /// A chine rail's section at one end: the hull's edge point <paramref name="c"/> between its neighbours <paramref name="a"/> and
    /// <paramref name="b"/> (all at the same z), a faceted bar <paramref name="w"/> wide each way along the faces and <paramref name="h"/>
    /// proud of the edge. Four points, a to b.
    /// </summary>
    public static double[][] ChineRing(double[] a, double[] c, double[] b, double w, double h)
    {
        var z = c[2];
        var da = Unit(Sub(a, c));
        var db = Unit(Sub(b, c));
        var n = Unit([-(da[0] + db[0]), -(da[1] + db[1]), 0]);
        double[] Pt(double[] d1, double t1, double t2) => [c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z];
        return [Pt(da, w, 0.01), Pt(da, w * 0.45, h), Pt(db, w * 0.45, h), Pt(db, w, 0.01)];
    }

    /// <summary>A shoulder block's three runs (a big chine rail whose ends rake down to a smaller section over <paramref name="rake"/>
    /// metres): each run's ends, its section at each end, and whether it is the middle (drawn in the style's second armour colour).</summary>
    public static (double Za, double Zb, double W, double H, double W2, double H2, bool Middle)[] Shoulder(double za, double zb, double w, double h, double rake) =>
    [
        (za, za + rake, w * 0.55, h * 0.3, w, h, false),
        (za + rake + 0.004, zb - rake - 0.004, w, h, w, h, true),
        (zb - rake, zb, w, h, w * 0.55, h * 0.3, false),
    ];

    // ---- layout

    /// <summary>
    /// The armour on a band, by the style's pattern list: panels about <see cref="DressStyle.PanelLength"/> long, each one of the patterns
    /// (picked from <paramref name="facet"/>, the panel's index, <paramref name="salt"/> and <paramref name="at"/>, so both sides of a
    /// ship match), over v0..v1 across. A pattern's layers are slabs and strips.
    /// </summary>
    public static List<Item> Plates(Band band, DressStyle style, int facet, int at, int salt, double v0 = 0.07, double v1 = 0.93)
    {
        var items = new List<Item>();
        var len = band.Length;
        var n = Math.Max(1, (int)Math.Round(len / style.PanelLength));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + style.PanelGap / len * 4, u1 = (double)(i + 1) / n - style.PanelGap / len * 4;
            var pick = (facet * 7 + i * 3 + salt * 5 + at) % style.Patterns.Count;
            foreach (var l in style.Patterns[pick])
                items.Add(new Item(l.Strip, l.U0.At(u0, u1), l.U1.At(u0, u1), l.V0.At(v0, v1), l.V1.At(v0, v1), l.D0, l.D1, l.Bevel, style.Color(l.Color), l.Finish));
        }
        return items;
    }
}

/// <summary>
/// One edge of a pattern layer, from a panel's ends: <c>from</c> (lo, hi or mid) plus <c>frac</c> times the panel's span (capped at
/// <c>cap</c> metres-as-fraction when set) plus <c>off</c>. Evaluated in that order, so the Zero Day's numbers come out bit for bit.
/// </summary>
public readonly record struct DressBound(int From, double Frac, double Off, double Cap)
{
    public const int Lo = 0, Hi = 1, Mid = 2;

    public double At(double lo, double hi)
    {
        var b = From switch { Hi => hi, Mid => (lo + hi) / 2, _ => lo };
        var span = hi - lo;
        var f = double.IsNaN(Cap) ? span * Frac : Math.Min(Cap, span * Frac);
        return b + f + Off;
    }
}

/// <summary>A pattern's layer: a slab or a strip, its four edges, how far out (d0..d1, a strip d0), its bevel, its colour (a palette
/// name) and finish.</summary>
public readonly record struct DressLayer(bool Strip, DressBound U0, DressBound U1, DressBound V0, DressBound V1, double D0, double D1, double Bevel, string Color, string Finish);

/// <summary>A faction's hull dress (Constructs/DressStyles.json): its palette, its armour panels' length and gap, and its pattern list.</summary>
public sealed class DressStyle
{
    public string Name = "";
    public string Label = "";
    public Dictionary<string, string> Palette = [];
    public double PanelLength = 3.2, PanelGap = 0.03;
    public List<DressLayer[]> Patterns = [];

    /// <summary>A hull section's rails, light strip and extra bits (Constructs/DressStyles.json "trim"; defaults are the Zero Day's look).</summary>
    public DressTrim Trim = new();

    /// <summary>A palette name's colour (a "#rrggbb" stands for itself).</summary>
    public string Color(string name) => Palette.TryGetValue(name, out var c) ? c : name;
}

/// <summary>
/// The rest of a style's dress on a hull section (<c>Hull Section</c> keys, HullLoft.Section.cs): chine rails at the section's corners (the beam's
/// own size at its widest point), the light strip just over the beam, and the extra bits Sean chose for the first run: A bolt rows on the slabs
/// and hazard chevrons, B vents, D navigation, running and warning lights, F a conduit along the top chine. Sizes in metres, colours palette names.
/// </summary>
public sealed class DressTrim
{
    public double RailW = 0.25, RailH = 0.12, BeamW = 0.3, BeamH = 0.14;
    public string Rail = "armour", BeamRail = "armour2";
    public double StripW = 0.025, StripOff = 0.04;
    public string Strip = "glow";
    public double BoltEvery = 0.4, BoltSize = 0.035, BoltInset = 0.06;
    public string Bolt = "frame";
    public double ChevronLength = 0.8, ChevronWidth = 0.22;
    public string Chevron = "hazard", ChevronDark = "frame";
    public double VentW = 0.7, VentH = 0.32, VentEvery = 6.0;
    public int VentSlats = 4;
    public string Vent = "frame", VentSlat = "skin2";
    public double RunEvery = 3.0, LampSize = 0.07;
    public string Port = "#e0402c", Starboard = "#40d060", Running = "#f4f4ec", Warning = "#f0a830";
    public double PipeR = 0.045, BracketEvery = 1.5;
    public string Pipe = "#a8402c", Bracket = "frame";
}

/// <summary>The dress styles (Constructs/DressStyles.json). A refused file logs a REFUSED line and leaves no styles.</summary>
public static class DressStyles
{
    public const string Format = "call-of-slop-dress-styles/1";
    public const int MostPatterns = 32, MostLayers = 8;

    public static string FilePath => Path.Combine(AppFolders.Data, "Constructs", "DressStyles.json");

    private static Dictionary<string, DressStyle>? _current;

    /// <summary>The styles in use, by name (loaded once).</summary>
    public static Dictionary<string, DressStyle> Current => _current ??= Load(FilePath, out _);

    public static DressStyle? Find(string name) => Current.GetValueOrDefault(name);

    public static Dictionary<string, DressStyle> Load(string path, out string? problem)
    {
        var all = new Dictionary<string, DressStyle>(StringComparer.OrdinalIgnoreCase);
        problem = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" must be \"{Format}\"");
            foreach (var p in root.EnumerateObject())
                if (p.Name is not ("format" or "_comment" or "styles"))
                    throw new FormatException($"unknown key \"{p.Name}\"");
            foreach (var e in root.GetProperty("styles").EnumerateArray())
            {
                var s = ReadStyle(e);
                if (!all.TryAdd(s.Name, s))
                    throw new FormatException($"style \"{s.Name}\" twice");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            all.Clear();
            problem = $"{Path.GetFileName(path)}: {ex.Message}";
            Console.WriteLine($"[dress] REFUSED {problem}: no dress styles");
        }
        return all;
    }

    private static DressStyle ReadStyle(JsonElement e)
    {
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("name" or "label" or "_comment" or "palette" or "panel" or "patterns" or "trim"))
                throw new FormatException($"a style: unknown key \"{p.Name}\"");
        var s = new DressStyle { Name = e.GetProperty("name").GetString() ?? "", Label = e.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "" };
        if (s.Name.Length is 0 or > 24 || !s.Name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new FormatException($"style name \"{s.Name}\": 1 to 24 letters, digits or -");
        if (s.Label.Length == 0)
            s.Label = s.Name;
        foreach (var c in e.GetProperty("palette").EnumerateObject())
        {
            var v = c.Value.GetString() ?? "";
            if (v.Length != 7 || v[0] != '#' || !v.Skip(1).All(char.IsAsciiHexDigit))
                throw new FormatException($"{s.Name}: palette \"{c.Name}\" is \"{v}\", not #rrggbb");
            s.Palette[c.Name] = v;
        }
        var panel = e.GetProperty("panel");
        s.PanelLength = Num(panel, "length", 0.5, 20, s.Name);
        s.PanelGap = Num(panel, "gap", 0, 0.5, s.Name);
        foreach (var pat in e.GetProperty("patterns").EnumerateArray())
        {
            var layers = pat.EnumerateArray().Select(l => ReadLayer(l, s)).ToArray();
            if (layers.Length is 0 or > MostLayers)
                throw new FormatException($"{s.Name}: a pattern has {layers.Length} layers (1 to {MostLayers})");
            s.Patterns.Add(layers);
        }
        if (s.Patterns.Count is 0 or > MostPatterns)
            throw new FormatException($"{s.Name}: {s.Patterns.Count} patterns (1 to {MostPatterns})");
        if (e.TryGetProperty("trim", out var tr))
            s.Trim = ReadTrim(tr, s);
        return s;
    }

    /// <summary>A style's "trim": every key optional (the Zero Day's look for any left out), each checked.</summary>
    private static DressTrim ReadTrim(JsonElement e, DressStyle s)
    {
        var t = new DressTrim();
        foreach (var p in e.EnumerateObject())
        {
            double N(double lo, double hi)
            {
                var v = p.Value.GetDouble();
                return v >= lo && v <= hi ? v : throw new FormatException($"{s.Name}: trim \"{p.Name}\" is {v} ({lo} to {hi})");
            }
            string C()
            {
                var v = p.Value.GetString() ?? "";
                return s.Palette.ContainsKey(v) || v.Length == 7 && v[0] == '#' && v.Skip(1).All(char.IsAsciiHexDigit) ? v : throw new FormatException($"{s.Name}: trim \"{p.Name}\" is \"{v}\": a palette name or #rrggbb");
            }
            switch (p.Name)
            {
                case "_comment": break;
                case "railW": t.RailW = N(0.05, 1); break;
                case "railH": t.RailH = N(0.02, 0.6); break;
                case "beamW": t.BeamW = N(0.05, 1); break;
                case "beamH": t.BeamH = N(0.02, 0.6); break;
                case "rail": t.Rail = C(); break;
                case "beamRail": t.BeamRail = C(); break;
                case "stripW": t.StripW = N(0.012, 0.2); break;
                case "stripOff": t.StripOff = N(0, 0.5); break;
                case "strip": t.Strip = C(); break;
                case "boltEvery": t.BoltEvery = N(0.15, 2); break;
                case "boltSize": t.BoltSize = N(0.016, 0.1); break;
                case "boltInset": t.BoltInset = N(0.02, 0.3); break;
                case "bolt": t.Bolt = C(); break;
                case "chevronLength": t.ChevronLength = N(0.2, 3); break;
                case "chevronWidth": t.ChevronWidth = N(0.05, 1); break;
                case "chevron": t.Chevron = C(); break;
                case "chevronDark": t.ChevronDark = C(); break;
                case "ventW": t.VentW = N(0.2, 3); break;
                case "ventH": t.VentH = N(0.1, 2); break;
                case "ventEvery": t.VentEvery = N(1, 40); break;
                case "ventSlats": t.VentSlats = (int)N(2, 8); break;
                case "vent": t.Vent = C(); break;
                case "ventSlat": t.VentSlat = C(); break;
                case "runEvery": t.RunEvery = N(1, 20); break;
                case "lampSize": t.LampSize = N(0.03, 0.3); break;
                case "port": t.Port = C(); break;
                case "starboard": t.Starboard = C(); break;
                case "running": t.Running = C(); break;
                case "warning": t.Warning = C(); break;
                case "pipeR": t.PipeR = N(0.015, 0.3); break;
                case "bracketEvery": t.BracketEvery = N(0.5, 6); break;
                case "pipe": t.Pipe = C(); break;
                case "bracket": t.Bracket = C(); break;
                default: throw new FormatException($"{s.Name}: trim: unknown key \"{p.Name}\"");
            }
        }
        return t;
    }

    private static DressLayer ReadLayer(JsonElement l, DressStyle s)
    {
        foreach (var p in l.EnumerateObject())
            if (p.Name is not ("kind" or "u" or "v" or "d" or "bevel" or "color" or "finish"))
                throw new FormatException($"{s.Name}: a layer: unknown key \"{p.Name}\"");
        var kind = l.GetProperty("kind").GetString();
        if (kind is not ("slab" or "strip"))
            throw new FormatException($"{s.Name}: a layer's kind is \"{kind}\", not slab or strip");
        var strip = kind == "strip";
        var u = l.GetProperty("u");
        var v = l.GetProperty("v");
        var d = l.GetProperty("d");
        double d0, d1;
        if (strip)
            d0 = d1 = d.GetDouble();
        else
        {
            d0 = d[0].GetDouble();
            d1 = d[1].GetDouble();
        }
        if (!(d0 >= 0 && d1 >= d0 && d1 <= 1))
            throw new FormatException($"{s.Name}: a layer stands {d0} to {d1} m out (0 to 1 m, outer no nearer)");
        var bevel = strip ? 0 : l.GetProperty("bevel").GetDouble();
        var color = l.GetProperty("color").GetString() ?? "";
        if (!s.Palette.ContainsKey(color) && !(color.Length == 7 && color[0] == '#'))
            throw new FormatException($"{s.Name}: colour \"{color}\" is not in its palette");
        var finish = l.TryGetProperty("finish", out var fi) ? fi.GetString() ?? "" : strip ? "painted plating" : "plated steel";
        return new DressLayer(strip, Bound(u[0], s), Bound(u[1], s), Bound(v[0], s), Bound(v[1], s), d0, d1, bevel, color, finish);
    }

    /// <summary>A bound: "lo", "hi", "mid", or {"from", "frac", "off", "cap"}.</summary>
    private static DressBound Bound(JsonElement e, DressStyle s)
    {
        static int From(string? w, string style) => w switch
        {
            "lo" => DressBound.Lo,
            "hi" => DressBound.Hi,
            "mid" => DressBound.Mid,
            _ => throw new FormatException($"{style}: a bound's \"from\" is \"{w}\", not lo, hi or mid"),
        };
        if (e.ValueKind == JsonValueKind.String)
            return new DressBound(From(e.GetString(), s.Name), 0, 0, double.NaN);
        foreach (var p in e.EnumerateObject())
            if (p.Name is not ("from" or "frac" or "off" or "cap"))
                throw new FormatException($"{s.Name}: a bound: unknown key \"{p.Name}\"");
        return new DressBound(From(e.GetProperty("from").GetString(), s.Name), e.TryGetProperty("frac", out var fr) ? fr.GetDouble() : 0,
            e.TryGetProperty("off", out var of) ? of.GetDouble() : 0, e.TryGetProperty("cap", out var cp) ? cp.GetDouble() : double.NaN);
    }

    private static double Num(JsonElement e, string key, double lo, double hi, string style)
    {
        var v = e.GetProperty(key).GetDouble();
        if (!(v >= lo && v <= hi))
            throw new FormatException($"{style}: \"{key}\" is {v} ({lo} to {hi})");
        return v;
    }
}
