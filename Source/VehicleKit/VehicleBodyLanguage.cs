using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace CallOfSlop.VehicleKit;

/// <summary>
/// A style's body language (docs/vehiclekit/BODY-STYLES.md): the cross-sections its rooms are lofted from, and how a seed varies them. The grammar still plans a room's
/// bays as tall, low and narrow sections (<see cref="ShapeGrammar.Bays(Dice, int, bool, BodyRange)"/>); a language turns each into this style's own section, at this
/// seed's height and width, and its brow and tail into this seed's rake. Kit 5 (<see cref="VehicleKitVersion.Lofted"/>). A language is data: a new archetype (a van, a
/// coach, a pod, later a car's cabin) is a row here and a row in <see cref="BodyStyleRule.ByStyle"/>, never a special case in the builder.
/// </summary>
public sealed record BodyLanguage(string Name, string Words, HullLoft.Section Tall, float HeightMin, float HeightMax, float BrowRakeMin, float BrowRakeMax, float TailRakeMin, float TailRakeMax,
    float BrowNarrow, float TailNarrow, float LowShare = 0.87f, float NarrowShare = 0.8f)
{
    private static HullLoft.Section S(params float[] xy) => new(Enumerable.Range(0, xy.Length / 2).Select(i => new Vector2(xy[2 * i], xy[2 * i + 1])).ToArray());

    // The languages. Every tall section keeps x >= 1.0 below 1.6 m (furniture reaches a metre out: Vehicle.BayHalf), stands on the floor at 1.0 m or more and is over
    // 2.1 m at its top, so a door bay can be any of them; a low one keeps 2 m at the centre. The rounded ones are control polygons: a smooth style draws them as
    // curves (BodyStyleRule.Facets), a faceted one as they are.
    public static readonly BodyLanguage Slab = new("slab", "welded slabs: upright walls, one hard chamfer, a flat roof", S(1.04f, 0, 1.07f, 1.86f, 0.8f, 2.22f, 0, 2.22f), 2.16f, 2.32f, 0.62f, 0.8f, 0.7f, 0.9f, 0.96f, 0.94f);
    public static readonly BodyLanguage Ledge = new("ledge", "a box welded over a box: the upper walls stand proud of the lower on a ledge", S(1.02f, 0, 1.03f, 1.08f, 1.12f, 1.16f, 1.12f, 1.9f, 0.84f, 2.2f, 0, 2.2f), 2.14f, 2.3f, 0.7f, 0.85f, 0.75f, 0.9f, 0.95f, 0.93f);
    public static readonly BodyLanguage Pod = new("pod", "clean rounded panels: tumblehome and a crowned roof", S(1.0f, 0, 1.1f, 0.45f, 1.1f, 1.3f, 0.98f, 1.8f, 0.72f, 2.15f, 0.36f, 2.3f, 0, 2.33f), 2.26f, 2.4f, 0.64f, 0.76f, 0.65f, 0.82f, 0.9f, 0.9f);
    public static readonly BodyLanguage Van = new("van", "a panel van: near-upright sides rounding into a gently crowned roof", S(1.02f, 0, 1.08f, 0.6f, 1.08f, 1.75f, 0.96f, 2.08f, 0.6f, 2.24f, 0, 2.28f), 2.22f, 2.36f, 0.6f, 0.78f, 0.78f, 0.92f, 0.94f, 0.95f);
    public static readonly BodyLanguage Bus = new("bus", "a bus: tall upright sides, a domed roof with rounded corners", S(1.04f, 0, 1.07f, 0.95f, 1.06f, 1.85f, 0.98f, 2.12f, 0.78f, 2.3f, 0.4f, 2.38f, 0, 2.4f), 2.34f, 2.48f, 0.72f, 0.9f, 0.72f, 0.88f, 0.96f, 0.95f);
    public static readonly BodyLanguage Coach = new("coach", "a coach: a lower body, a long tumblehome and a raked front", S(1.02f, 0, 1.08f, 0.7f, 1.06f, 1.5f, 0.92f, 1.98f, 0.62f, 2.18f, 0, 2.24f), 2.2f, 2.32f, 0.6f, 0.72f, 0.72f, 0.88f, 0.9f, 0.93f);
    public static readonly BodyLanguage Boat = new("boat", "boat-like: a flare to a strake, a curved tumblehome, a boat-tail stern", S(1.02f, 0, 1.13f, 0.7f, 1.09f, 1.45f, 0.92f, 1.95f, 0.55f, 2.22f, 0, 2.26f), 2.2f, 2.36f, 0.65f, 0.82f, 0.6f, 0.78f, 0.92f, 0.8f);
    public static readonly BodyLanguage Cabin = new("cabin", "a site cabin: plain walls, eaves and a pitched roof", S(1.04f, 0, 1.06f, 1.92f, 1.12f, 1.98f, 0, 2.42f), 2.36f, 2.5f, 0.75f, 0.9f, 0.75f, 0.9f, 0.97f, 0.97f, 0.88f);
    public static readonly BodyLanguage Armour = new("armour", "faceted armour: a flared lower hull, a steep glacis-like upper, a narrow roof", S(1.0f, 0, 1.13f, 0.75f, 0.88f, 2.0f, 0.56f, 2.2f, 0, 2.2f), 2.16f, 2.28f, 0.62f, 0.75f, 0.65f, 0.8f, 0.9f, 0.88f);

    public static readonly BodyLanguage[] All = [Slab, Ledge, Pod, Van, Bus, Coach, Boat, Cabin, Armour];

    /// <summary>Which languages a style speaks, and how often (a style not named speaks armour). Kept for the tools; the rule is <see cref="BodyStyleRule"/>.</summary>
    public static (BodyLanguage L, double W)[] Of(FactionStyle st) => BodyStyleRule.Of(st).Languages;

    /// <summary>
    /// The language's tall section drawn with <paramref name="facets"/> facets a side (0: as written, its own corners). A smooth one is the control polygon cut at its
    /// corners three times (Chaikin), then walked at even steps along it; its last row (onto the centreline) stays level, so the roof is one panel across.
    /// </summary>
    public HullLoft.Section Drawn(int facets)
    {
        if (facets <= 0 || facets + 1 == Tall.P.Length)
            return Tall;
        var pts = Tall.P.ToList();
        for (var pass = 0; pass < 3; pass++)
        {
            var next = new List<Vector2> { pts[0] };
            for (var k = 0; k + 1 < pts.Count; k++)
            {
                var (a, b) = (pts[k], pts[k + 1]);
                if (k > 0)
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                if (k + 2 < pts.Count)
                    next.Add(Vector2.Lerp(a, b, 0.75f));
            }
            next.Add(pts[^1]);
            pts = next;
        }
        var len = new float[pts.Count];
        for (var k = 1; k < pts.Count; k++)
            len[k] = len[k - 1] + Vector2.Distance(pts[k - 1], pts[k]);
        var outPts = new Vector2[facets + 1];
        var j = 0;
        for (var i = 0; i <= facets; i++)
        {
            var at = len[^1] * i / facets;
            while (j + 2 < pts.Count && len[j + 1] < at)
                j++;
            var t = len[j + 1] > len[j] ? Math.Clamp((at - len[j]) / (len[j + 1] - len[j]), 0, 1) : 0;
            outPts[i] = Vector2.Lerp(pts[j], pts[j + 1], t);
        }
        outPts[0] = Tall.P[0];
        outPts[^1] = Tall.P[^1];
        outPts[^2].Y = outPts[^1].Y; // (level onto the centreline: one roof panel, no seam)
        for (var i = 1; i < outPts.Length; i++)
            outPts[i].Y = MathF.Max(outPts[i].Y, outPts[i - 1].Y); // (a profile only goes up)
        return new HullLoft.Section(outPts);
    }
}

/// <summary>
/// How a style's rooms are lofted (the user's decisions, 2026-11-07): its languages and their weights; how many facets a side it draws them with (Bandit and
/// military flat-faceted, 3 to 6; IT, British and any future high-tech smooth, 8 to 12; 0: each language's own corners); and whether its bays mix languages one
/// station to the next (Bandit: cobbled from wrecks), or keep one language a vehicle. Data, so a new style or archetype is a row.
/// </summary>
public sealed record BodyStyleRule((BodyLanguage L, double W)[] Languages, int FacetsMin = 0, int FacetsMax = 0, double Mix = 0)
{
    public static readonly Dictionary<string, BodyStyleRule> ByStyle = new(StringComparer.Ordinal)
    {
        ["Bandit"] = new([(BodyLanguage.Slab, 2), (BodyLanguage.Ledge, 1.5), (BodyLanguage.Armour, 0.5)], Mix: 0.5), // (each station after the first: a new language half the time)
        ["Militia"] = new([(BodyLanguage.Armour, 1)]),
        ["IT"] = new([(BodyLanguage.Pod, 2), (BodyLanguage.Van, 1.5)], 8, 12),
        ["Scam"] = new([(BodyLanguage.Cabin, 2), (BodyLanguage.Van, 1)]),
        ["Highland"] = new([(BodyLanguage.Boat, 3), (BodyLanguage.Slab, 0.5)]),
        ["British"] = new([(BodyLanguage.Bus, 2.5), (BodyLanguage.Coach, 1.5)], 8, 12),
    };

    /// <summary>A style not named: military armour, faceted, one language.</summary>
    public static readonly BodyStyleRule Default = new([(BodyLanguage.Armour, 1)]);

    public static BodyStyleRule Of(FactionStyle st) => ByStyle.GetValueOrDefault(st.Name) ?? Default;

    /// <summary>A weighted pick from the languages.</summary>
    public BodyLanguage Pick(Dice d)
    {
        var roll = d.Next() * Languages.Sum(x => x.W);
        foreach (var (l, w) in Languages)
            if ((roll -= w) < 0)
                return l;
        return Languages[^1].L;
    }
}

/// <summary>
/// One body's roll of its language: its height and width, how far its brow and tail rake, how many facets it draws with, and the sections that come of it. Where its
/// style mixes, each station between two bays (a room's front, each joint, its back) takes a language of its own, and a bay lofts from one to the next.
/// </summary>
public sealed class BodyLook
{
    public BodyLanguage Language { get; }
    public float Height, Width, BrowRake, TailRake, BrowNarrow, TailNarrow;
    public int Facets;
    private readonly BodyStyleRule _rule;
    private readonly Dice _mix;

    public BodyLook(BodyLanguage l, Dice d, BodyStyleRule? rule = null)
    {
        Language = l;
        _rule = rule ?? new BodyStyleRule([(l, 1)]);
        Height = (float)d.Between(l.HeightMin, l.HeightMax);
        Width = (float)d.Between(0.98, 1.02);
        BrowRake = (float)d.Between(l.BrowRakeMin, l.BrowRakeMax);
        TailRake = (float)d.Between(l.TailRakeMin, l.TailRakeMax);
        BrowNarrow = l.BrowNarrow * (float)d.Between(0.97, 1.0);
        TailNarrow = l.TailNarrow * (float)d.Between(0.97, 1.0);
        Facets = _rule.FacetsMax > 0 ? d.Fork("facets").Int(_rule.FacetsMin, _rule.FacetsMax) : 0;
        _mix = d.Fork("stations");
    }

    /// <summary><c>--language NAME</c>: every kit-5 body in that language, whatever its style (to compare languages on one vehicle).</summary>
    public static BodyLanguage? Force;

    /// <summary>A body's look from the style's languages, on the body layer's "language" fork.</summary>
    public static BodyLook Roll(Vehicle v)
    {
        var d = v.Dice.Fork("language");
        var rule = BodyStyleRule.Of(v.Style);
        if (Force is { } forced)
            rule = rule with { Languages = [(forced, 1.0)], Mix = 0 };
        return new BodyLook(rule.Pick(d), d.Fork("proportions"), rule);
    }

    /// <summary>Station <paramref name="i"/>'s language (0: the room's front): the look's own unless the style mixes, then, after the first, a new one now and then. No dice state: the same station always answers the same.</summary>
    public BodyLanguage At(int i)
    {
        if (_rule.Mix <= 0 || i <= 0)
            return Language;
        var prev = At(i - 1);
        var d = _mix.Fork("station " + i);
        return d.Chance(_rule.Mix) ? _rule.Pick(d) : prev;
    }

    /// <summary>The languages a room of <paramref name="stations"/> stations speaks, front to back, without repeats ("slab", "slab+ledge").</summary>
    public string Spoken(int stations) => string.Join("+", Enumerable.Range(0, stations).Select(At).Select(l => l.Name).Distinct());

    /// <summary>The section a plan's token stands for in this look's language: T tall, L low, N narrow; <paramref name="end"/> shrinks it for a brow (-1) or a tail (+1).</summary>
    public HullLoft.Section Section(string token, int end = 0) => Section(Language, token, end);

    /// <summary>The section a plan's token stands for in language <paramref name="l"/> at this look's proportions.</summary>
    public HullLoft.Section Section(BodyLanguage l, string token, int end = 0)
    {
        var t = l.Drawn(Facets);
        var ky = Height / t.Top;
        var kx = Width;
        if (token == "L")
            ky *= l.LowShare;
        else if (token == "N")
        {
            kx *= l.NarrowShare;
            ky *= (1 + l.LowShare) / 2;
        }
        if (end == 0)
            ky = MathF.Max(ky, 2.02f / t.Top); // (headroom at the centre: a player is 1.8 m)
        if (end < 0)
            (kx, ky) = (kx * BrowNarrow, ky * BrowRake);
        else if (end > 0)
            (kx, ky) = (kx * TailNarrow, ky * TailRake);
        kx = MathF.Max(kx, 0.76f / t.P[0].X); // (a seat or a crate stands half a metre out by a brow or a tail: the fixed bays' narrowest end is 0.74 m)
        var s = t.Scaled(kx, ky);
        if (end == 0 && token != "N")
            s = new HullLoft.Section(s.P.Select((p, i) => i == 0 || p.Y < 1.6f ? p with { X = MathF.Max(p.X, i == 0 ? 1.0f : 1.02f) } : p).ToArray()); // (furniture's metre)
        return Tidy(s);
    }

    /// <summary>Drops points the centimetre grid made repeats of (a smooth curve's close points), keeping the floor edge and the centreline.</summary>
    private static HullLoft.Section Tidy(HullLoft.Section s)
    {
        var list = new List<Vector2> { s.P[0] };
        for (var i = 1; i < s.P.Length; i++)
            if (Vector2.Distance(s.P[i], list[^1]) >= 0.015f || i == s.P.Length - 1)
            {
                if (i == s.P.Length - 1 && Vector2.Distance(s.P[i], list[^1]) < 0.015f && list.Count > 1)
                    list[^1] = s.P[i];
                else
                    list.Add(s.P[i]);
            }
        return list.Count == s.P.Length ? s : new HullLoft.Section(list.ToArray());
    }

    /// <summary>
    /// Two sections with as many points each, for a bay that lofts from one to the other: the one with fewer gets its longest side rows split in half (collinear points,
    /// so its shape doesn't change). The roof's last row is never split, so a flat roof stays one panel.
    /// </summary>
    public static (HullLoft.Section A, HullLoft.Section B) Match(HullLoft.Section a, HullLoft.Section b)
    {
        static HullLoft.Section Grow(HullLoft.Section s, int n)
        {
            var pts = s.P.ToList();
            while (pts.Count < n)
            {
                var (best, len) = (0, -1f);
                for (var k = 0; k + 2 < pts.Count; k++) // (not the last row)
                    if (Vector2.Distance(pts[k], pts[k + 1]) > len)
                        (best, len) = (k, Vector2.Distance(pts[k], pts[k + 1]));
                var mid = (pts[best] + pts[best + 1]) / 2;
                pts.Insert(best + 1, new Vector2(MathF.Round(mid.X * 100) / 100, MathF.Round(mid.Y * 100) / 100));
            }
            return new HullLoft.Section(pts.ToArray());
        }
        var m = Math.Max(a.P.Length, b.P.Length);
        return (a.P.Length < m ? Grow(a, m) : a, b.P.Length < m ? Grow(b, m) : b);
    }

    /// <summary>A plan's bay names ("Brow T", "Door", "Bay T-L", "Tail L") as lofted parts' keys, front to back; bay k lofts from station k to station k + 1.</summary>
    public string[] Names(string[] bays)
    {
        var names = new string[bays.Length];
        for (var k = 0; k < bays.Length; k++)
        {
            var b = bays[k];
            if (b == "Door")
            {
                var (ta, tb) = Match(Section(At(k), "T"), Section(At(k + 1), "T"));
                names[k] = HullLoft.Key(1.5f, true, false, false, ta, tb);
                continue;
            }
            var parts = b.Split(' ');
            var tokens = parts[1].Split('-');
            var (front, back) = (tokens[0], tokens[^1]);
            var (fa, fb) = parts[0] switch
            {
                "Brow" => (Section(At(1), front, -1), Section(At(1), front)),
                "Tail" => (Section(At(k), back), Section(At(k), back, 1)),
                _ => (Section(At(k), front), Section(At(k + 1), back)),
            };
            var (ma, mb) = Match(fa, fb);
            names[k] = HullLoft.Key(1, false, parts[0] == "Brow", parts[0] == "Tail", ma, mb);
        }
        return names;
    }

    public string Label => string.Create(CultureInfo.InvariantCulture, $"{Language.Name} {Height:0.00} m, brow {BrowRake:0.00}, tail {TailRake:0.00}{(Facets > 0 ? $", {Facets} facets" : "")}");
}

public sealed partial class Vehicle
{
    /// <summary>Kit 5: the room's body language and this seed's roll of it (<see cref="BodyLook"/>); null: the fixed hull bays of kits 1 to 4.</summary>
    public BodyLook? Look { get; set; }

    /// <summary>The construct as built so far, for a measure (the report below).</summary>
    internal Construct Built => C;
}

/// <summary>
/// <c>--body-report Style [--seeds N] [--from S]</c>: what the rooms are made of and how their cross-sections differ (docs/vehiclekit/BODY-STYLES.md, "Measuring"):
/// distinct sections and bays, and a front-view distance the silhouette term can't see, within the style and against the other styles' rooms in the same run.
/// </summary>
public static class BodyReportCli
{
    private const int Bins = FrontProfile.Bins;

    /// <summary>A room's cross-section from the front (<see cref="FrontProfile"/>).</summary>
    private static float[] Front(Vehicle v)
    {
        var (tris, owner) = VehiclePlan.PartTriangles(v.Built);
        return FrontProfile.OfVehicle(v, tris, owner).Room;
    }

    private static float Dist(float[] a, float[] b) => FrontProfile.Dist(a, b);

    /// <summary>Coplanar faces inside one lofted bay (its own facets against each other), as --check-parts would find in a part file.</summary>
    private static int OwnFights(ConstructPartDef d) => ZFight.Find(ZFight.Boxes(d)).Count;

    public static int Run(string styleName, string[] args)
    {
        string? Arg(string k) => args.SkipWhile(a => !a.Equals(k, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
        var seeds = int.TryParse(Arg("--seeds"), out var n) ? n : 6;
        var from = int.TryParse(Arg("--from"), out var f) ? f : 1;
        var styles = styleName.Equals("all", StringComparison.OrdinalIgnoreCase) ? Styles.All.ToArray() : Styles.All.Where(s => s.Name.Equals(styleName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (styles.Length == 0)
        {
            Console.WriteLine($"--body-report: no style {styleName}");
            return 1;
        }
        var fronts = new List<(string Style, float[] F)>();
        var failures = 0;
        var fights = 0;
        foreach (var st in styles)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var sections = new HashSet<string>(StringComparer.Ordinal);
            var langs = new Dictionary<string, int>(StringComparer.Ordinal);
            var mine = new List<float[]>();
            var ms = new List<double>();
            var parts = new List<int>();
            var facets = new List<int>();
            foreach (var g in VehicleRegistry.For(st, false).Where(g => ShapeGrammar.RecipeOf(g) != null))
                for (var seed = from; seed < from + seeds; seed++)
                {
                    try
                    {
                        var (_, v, _, t) = VehicleDiversity.Generate(g, seed);
                        ms.Add(t);
                        parts.Add(v.Built.PartCount);
                        foreach (var b in v.Rooms.SelectMany(r => r.Bays))
                        {
                            if (keys.Add(b.Def.Name) && OwnFights(b.Def) is var own and > 0)
                            {
                                fights += own;
                                Console.WriteLine($"  {g.Name} {seed}: {own} coplanar pair(s) inside {b.Def.Name}");
                            }
                            if (HullLoft.ShapeOf(b.Def.Name) is { } sh)
                            {
                                sections.Add(sh.A.Key());
                                sections.Add(sh.B.Key());
                                facets.Add(sh.A.P.Length - 1);
                            }
                            else
                                sections.Add(b.Def.Name);
                        }
                        var lang = v.Look?.Language.Name ?? "fixed bays";
                        langs[lang] = langs.GetValueOrDefault(lang) + 1;
                        if (v.Rooms.Count > 0)
                        {
                            var fr = Front(v);
                            mine.Add(fr);
                            fronts.Add((st.Name, fr));
                        }
                    }
                    catch (Exception e) when (e is VehicleKitException or ConstructPartException)
                    {
                        failures++;
                        Console.WriteLine($"  {g.Name} {seed}: {e.Message}");
                    }
                }
            var pairs = new List<float>();
            for (var i = 0; i < mine.Count; i++)
                for (var j = i + 1; j < mine.Count; j++)
                    pairs.Add(Dist(mine[i], mine[j]));
            Console.WriteLine($"{st.Name}: {ms.Count} vehicles, {mine.Count} with rooms; bay parts {keys.Count} distinct, sections {sections.Count} distinct; languages {string.Join(", ", langs.Select(x => $"{x.Key} {x.Value}"))}");
            Console.WriteLine($"  facets a side {(facets.Count > 0 ? $"{facets.Min()} to {facets.Max()}, mean {facets.Average():0.0}" : "-")}");
            Console.WriteLine($"  front profile: mean pair {(pairs.Count > 0 ? pairs.Average() : 0):0.000}; generation mean {(ms.Count > 0 ? ms.Average() : 0):0} ms, worst {(ms.Count > 0 ? ms.Max() : 0):0} ms; parts mean {(parts.Count > 0 ? parts.Average() : 0):0}");
            if (mine.Count > 0)
                Console.WriteLine("  mean front: " + string.Join(" ", Enumerable.Range(0, Bins).Select(b => mine.Average(x => x[b]).ToString("0.00", CultureInfo.InvariantCulture))));
        }
        if (styles.Length > 1)
        {
            Console.WriteLine("front profile between styles (mean pair; the diagonal is within the style):");
            var names = styles.Select(s => s.Name).ToArray();
            var sb = new StringBuilder("            ");
            foreach (var b in names)
                sb.Append($"{b,10}");
            Console.WriteLine(sb.ToString());
            foreach (var a in names)
            {
                sb.Clear().Append($"{a,-12}");
                foreach (var b in names)
                {
                    var d = (from x in fronts where x.Style == a from y in fronts where y.Style == b && !ReferenceEquals(x.F, y.F) select Dist(x.F, y.F)).DefaultIfEmpty(0).Average();
                    sb.Append($"{d,10:0.000}");
                }
                Console.WriteLine(sb.ToString());
            }
        }
        Console.WriteLine($"failures: {failures}; coplanar pairs inside lofted bays: {fights}");
        return failures + fights;
    }
}


/// <summary>
/// Where a room's guns and gear may go, judged by its real bays (kit 5's lofted sections, read back from their keys) rather than by the plan's names: a roof turret on
/// a roof that is level under its turntable with nothing beside it higher, a side gun on a waist with nothing above it standing out over its turret (an eave, a
/// ledge, a neighbour's roof edge), a pannier on a waist with room above it. A bay that isn't lofted (kits 1 to 4, the Double Decker) keeps the plan's rule.
/// </summary>
public static class BaySpots
{
    /// <summary>A roof turret's bay: how level along its length, and how much higher than its roof a neighbour may stand (its own roof may fall away either side: a gable, a crown).</summary>
    private const float Level = 0.03f, Rise = 0.02f;

    /// <summary>A side gun's turret on a sponson: from just above the sponson's top to over the gunner's head, nothing out past the waist by more than this.</summary>
    private const float SideFrom = 0.15f, SideTo = 1.3f, SideProud = 0.03f, PannierTo = 0.35f;

    private static HullLoft.Shape? Of(string? name) => name == null ? null : HullLoft.ShapeOf(name);

    private static float Top(HullLoft.Shape s) => MathF.Max(s.A.Top, s.B.Top);

    /// <summary>Is bay <paramref name="k"/>'s roof a turret's: level along it, as tall as the room's tallest, no neighbour's roof above it (the turntable stands on its top).</summary>
    public static bool Roof(string[] plan, string[]? loft, int k)
    {
        if (Of(loft?[k]) is not { } s)
            return plan[k] is "Door" or "Bay T" || plan[k] is "Tail T" && loft == null;
        var top = s.A.Top;
        if (MathF.Abs(s.A.Top - s.B.Top) > Level || s.Front || s.Back)
            return false; // (a sloped roof, a brow or a tail: the arm runs into the next bay's wall)
        var tallest = loft!.Select(Of).Where(x => x != null).Max(x => Top(x!.Value));
        if (top < tallest - Level)
            return false;
        foreach (var j in new[] { k - 1, k + 1 })
            if (j >= 0 && j < loft.Length && Of(loft[j]) is { } n && Top(n) > top + Rise)
                return false;
        return true;
    }

    /// <summary>Is bay <paramref name="k"/>'s roof flat enough for a rack's deck plates (out to <paramref name="half"/> either side, level along it)?</summary>
    public static bool Flat(string[] plan, string[]? loft, int k, float half = 0.55f)
    {
        if (Of(loft?[k]) is not { } s)
            return plan[k] is "Door" or "Bay T";
        return !s.Front && !s.Back && MathF.Abs(s.A.Top - s.B.Top) <= 0.005f && s.A.YAt(half) >= s.A.Top - 0.005f && s.B.YAt(half) >= s.B.Top - 0.005f;
    }

    /// <summary>The waist a bay's side mounts hang on (height, and the side's half width there), as <see cref="HullLoft"/> puts its "+x0"/"-x0" sockets.</summary>
    private static (float Y, float X) Waist(HullLoft.Shape s)
    {
        var y = (s.A.WaistY + s.B.WaistY) / 2;
        return (y, (MathF.Max(s.A.XAt(y), 0) + MathF.Max(s.B.XAt(y), 0)) / 2);
    }

    /// <summary>Does anything stand out over a mount on bay <paramref name="k"/>'s waist, from <paramref name="from"/> to <paramref name="to"/> above it, on it or its neighbours?</summary>
    private static bool ClearAbove(string[] loft, int k, float from, float to, int reach)
    {
        if (Of(loft[k]) is not { } s)
            return true;
        var (wy, wx) = Waist(s);
        for (var j = k - reach; j <= k + reach; j++)
            if (j >= 0 && j < loft.Length && Of(loft[j]) is { } n)
                foreach (var sec in new[] { n.A, n.B })
                    if (sec.WidestBetween(wy + from, wy + to) > wx + SideProud)
                        return false;
        return true;
    }

    /// <summary>
    /// Can bay <paramref name="k"/> take a side gun (a sponson, its turntable, an arm a metre long either way, the hinge and the gun)? Not on the door bay, and not
    /// where an eave, a ledge or a neighbour's roof edge stands out over the turret (Bandit War Bus 5's ledge, a cabin's eaves: 2026-11-07).
    /// </summary>
    public static bool Side(string[] plan, string[]? loft, int k) => plan[k] != "Door" && (loft == null || ClearAbove(loft, k, SideFrom, SideTo, 1));

    /// <summary>Can a pannier hang on bay <paramref name="k"/>'s waist (0.3 m above it clear on the bay itself)?</summary>
    public static bool Pannier(string[]? loft, int k) => loft == null || ClearAbove(loft, k, 0.05f, PannierTo, 0);
}

/// <summary>
/// A cross-section seen from the front (docs/vehiclekit/BODY-STYLES.md, "Measuring"): the half width in <see cref="Bins"/> bands of the thing's own height, each over
/// the widest, so a box reads 1 all the way up and a triangle falls to 0. <see cref="VehicleDiversity"/> compares two vehicles' rooms and bodies by it, and
/// <see cref="VehicleIdentity"/> each room bay against its style's languages; <c>--body-report</c> prints it. Works on any drawn triangles or any section, so a
/// later archetype (a car's cabin, a buggy's cage) is measured the same way.
/// </summary>
public static class FrontProfile
{
    public const int Bins = 12;

    /// <summary>The mean difference per band: 0 the same section, about 0.3 a box against a triangle.</summary>
    public static float Dist(float[] a, float[] b)
    {
        var s = 0f;
        for (var i = 0; i < Bins; i++)
            s += MathF.Abs(a[i] - b[i]);
        return s / Bins;
    }

    private static void Add(float[] bins, Vector2 a, Vector2 b, float lo, float hi)
    {
        var n = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) / 0.04f));
        for (var k = 0; k <= n; k++)
        {
            var p = Vector2.Lerp(a, b, k / (float)n);
            var i = Math.Clamp((int)((p.Y - lo) / (hi - lo) * Bins), 0, Bins - 1);
            bins[i] = MathF.Max(bins[i], MathF.Abs(p.X));
        }
    }

    private static float[] Finish(float[] bins)
    {
        var most = bins.Max();
        for (var b = 0; b < Bins; b++)
            bins[b] = most > 0 ? bins[b] / most : 0;
        for (var b = 1; b < Bins; b++)
            if (bins[b] == 0)
                bins[b] = bins[b - 1]; // (a band nothing fell in takes the one below)
        return bins;
    }

    /// <summary>Of the triangles <paramref name="ranges"/> pick out of <paramref name="tris"/> (three points each; ranges in triangles, end exclusive), about their middle in x.</summary>
    public static float[] Of(Vector3[] tris, IEnumerable<(int From, int To)> ranges)
    {
        var bins = new float[Bins];
        float lo = float.MaxValue, hi = float.MinValue, xl = float.MaxValue, xh = float.MinValue;
        var list = ranges.Where(r => r.From >= 0 && r.To > r.From).ToList();
        foreach (var (f, t) in list)
            for (var i = f * 3; i < t * 3; i++)
                (lo, hi, xl, xh) = (MathF.Min(lo, tris[i].Y), MathF.Max(hi, tris[i].Y), MathF.Min(xl, tris[i].X), MathF.Max(xh, tris[i].X));
        var mid = (xl + xh) / 2;
        if (hi <= lo + 0.05f)
            return bins;
        foreach (var (f, t) in list)
            for (var i = f; i < t; i++)
                for (var e = 0; e < 3; e++)
                {
                    var (p, q) = (tris[i * 3 + e], tris[i * 3 + (e + 1) % 3]);
                    Add(bins, new Vector2(p.X - mid, p.Y), new Vector2(q.X - mid, q.Y), lo, hi);
                }
        return Finish(bins);
    }

    /// <summary>Of a half section (floor edge to the centreline).</summary>
    public static float[] Of(HullLoft.Section s)
    {
        var bins = new float[Bins];
        for (var k = 0; k + 1 < s.P.Length; k++)
            Add(bins, s.P[k], s.P[k + 1], 0, s.Top);
        return Finish(bins);
    }

    private static readonly Dictionary<(string, int), float[]> Langs = new();

    /// <summary>A language's tall section as a style draws it (cached).</summary>
    public static float[] Of(BodyLanguage l, int facets)
    {
        lock (Langs)
        {
            if (!Langs.TryGetValue((l.Name, facets), out var p))
                Langs[(l.Name, facets)] = p = Of(l.Drawn(facets));
            return p;
        }
    }

    /// <summary>The facets a style draws with, for its languages' profiles: the middle of its range (0: as written).</summary>
    public static int FacetsOf(BodyStyleRule r) => r.FacetsMax > 0 ? (r.FacetsMin + r.FacetsMax) / 2 : 0;

    /// <summary>A built vehicle's rooms (every bay of every room) and its whole body (every part but its wheels), from the front. Empty rooms: zeros.</summary>
    public static (float[] Room, float[] Body) OfVehicle(Vehicle v, Vector3[] tris, int[] owner)
    {
        var c = v.Built;
        var bays = new HashSet<int>(v.Rooms.SelectMany(r => r.Bays).Select(b => b.Index));
        var rooms = new List<(int, int)>();
        var body = new List<(int, int)>();
        var (start, prev) = (0, owner.Length > 0 ? owner[0] : -1);
        for (var t = 0; t <= owner.Length; t++)
            if (t == owner.Length || owner[t] != prev)
            {
                if (prev >= 0 && prev < c.PartCount)
                {
                    if (bays.Contains(prev))
                        rooms.Add((start, t));
                    if (c.DefOf(prev).Role is not (PartRole.Wheel or PartRole.Suspension))
                        body.Add((start, t));
                }
                if (t < owner.Length)
                    (start, prev) = (t, owner[t]);
            }
        return (rooms.Count > 0 ? Of(tris, rooms) : new float[Bins], Of(tris, body));
    }
}
