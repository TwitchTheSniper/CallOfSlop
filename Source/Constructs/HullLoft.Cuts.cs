using System.Globalization;
using System.Numerics;
using System.Text;

namespace CallOfSlop;

/// <summary>
/// Cut parts (docs/sandbox/design/laser-cutter.md, phase 1): a formed panel or a flat extruded piece with openings, notches and corners cut
/// out of it by a Laser Cutter. Its key is its name, the blank's own words then the cuts:
/// <c>Cut Panel (door 110x120 c6 r8 t20 steel | r70,60,40,50,5)</c>, <c>Cut Plate (plate 200x20x300 steel | r150,100,60,60,17 m0,20,20)</c>.
/// A cut is placed on the flat sheet, <c>L</c> by <c>W</c> cm before any forming: <c>rU,V,A,B,C</c> an opening whose middle is <c>U</c> cm
/// along from the back edge and <c>V</c> across from the left, <c>A</c> long, <c>B</c> wide, each corner cut back by <c>C</c>;
/// <c>mK,A,B</c> a triangle off corner <c>K</c> (0 back left, 1 back right, 2 nose right, 3 nose left). The part is the blank's surface
/// with those pieces removed: each facet the cuts touch is split into convex pieces on its own quad, so a split facet meets a whole one
/// with no crack. Mass and health are what is left. Collision (phase 2) is the solid sheet as rectangles round each opening of 10 cm or
/// more, so shots, bodies and players pass; an edge socket whose spot was cut away is left out, and each such opening has an <c>o</c> socket.
/// </summary>
public static partial class HullLoft
{
    public const string CutPanelPrefix = "Cut Panel (", CutPlatePrefix = "Cut Plate (";
    private const string CutSplit = " | ";
    public const int MaxCuts = 6, CutMaxFacets = 96, CutWebCm = 3, CutSnapCm = 2, CutMinCm = 5, CutMaxCm = 600;
    /// <summary>The thickest stock any cutter's bed takes (the Medium Laser Cutter's 2 m, design section 5): no rule of its own, only the bed (the user, 2026-11-13).</summary>
    public const int CutMaxThickMm = 2000;
    /// <summary>An opening this wide both ways or more opens its collision (shots, bodies and players pass) and gets an <c>o</c> socket; a smaller one is drawn and weighed only.</summary>
    public const int CutOpenCm = 10;
    /// <summary>Collision boxes a cut part: a body's collider and every ray read them (a panel has 4).</summary>
    public const int CutMaxBoxes = 24;
    private const int PlateSection = 1, SolidSection = 0; // (Extrusion.Presets: "solid", "plate")

    /// <summary>One cut: an opening (<see cref="Corner"/> false: U, V, A, B, C) or a cut corner (true: U is the corner K, then A, B).</summary>
    public readonly record struct Cut(bool Corner, int U, int V, int A, int B, int C)
    {
        public string Text => Corner
            ? string.Create(CultureInfo.InvariantCulture, $"m{U},{A},{B}")
            : string.Create(CultureInfo.InvariantCulture, $"r{U},{V},{A},{B},{C}");
    }

    /// <summary>A cut key read and planned: the sheet to cut (a plate is a flat one-facet sheet), its cuts and the pieces each facet keeps.</summary>
    internal sealed class CutPlan
    {
        public PanelShape Sheet; // the canvas; a plate's is a flat "curve" with its length and width (never a key of its own)
        public bool Plate;
        public ExtrusionKey Stock; // a plate's blank
        public float Thick; // m
        public string Blank = ""; // the blank's own name: "Formed Panel (...)" or "Extrusion ..."
        public Cut[] Cuts = [];
        public Vector2d[][] Polys = []; // each cut's outline on the sheet, cm, snapped to the facet lines
        public Vector2d[][]?[] Pieces = []; // [i * across + j]: null, the facet whole; else the pieces it keeps
        public int Items; // facets drawn: whole facets and pieces
        public double[] Us = [], Vs = []; // the facet lines on the sheet, cm
        public CutRect[] Rects = []; // the solid sheet left, as rectangles on it: one hidden box each (phase 2)
    }

    /// <summary>
    /// A rectangle of solid sheet, cm, one collision box. Its sides along the sheet (V) are always rims (the sheet's edge or an opening's);
    /// <see cref="RimU0"/> and <see cref="RimU1"/> say whether its ends are too, or only the next rectangle's or strip's seam.
    /// </summary>
    internal readonly record struct CutRect(double U0, double U1, double V0, double V1, bool RimU0, bool RimU1);

    /// <summary>A point on the flat sheet, cm (U along, V across): doubles, so every peer's pieces come out the same.</summary>
    internal readonly record struct Vector2d(double U, double V);

    public static bool IsCutName(string name) => name.StartsWith(CutPanelPrefix, StringComparison.Ordinal) || name.StartsWith(CutPlatePrefix, StringComparison.Ordinal);

    /// <summary>The key for a blank and its cuts, the cuts in their one order (openings by U, V, A, B, C; then corners by K).</summary>
    public static string CutKey(bool plate, string blankText, IEnumerable<Cut> cuts)
    {
        var sb = new StringBuilder(plate ? CutPlatePrefix : CutPanelPrefix).Append(blankText).Append(CutSplit);
        var first = true;
        foreach (var c in cuts.OrderBy(c => c.Corner).ThenBy(c => c.U).ThenBy(c => c.V).ThenBy(c => c.A).ThenBy(c => c.B).ThenBy(c => c.C))
        {
            sb.Append(first ? "" : " ").Append(c.Text);
            first = false;
        }
        return sb.Append(')').ToString();
    }

    /// <summary>Why a name is not a cut part a Laser Cutter would make, or null. Allocates: a key is read once, when its part is first made.</summary>
    public static string? CutRefusal(string name) => ReadCut(name, out var why) == null ? why : null;

    /// <summary>A cut key read, checked and planned; null with <paramref name="why"/> when it is refused (never clamped: every peer makes the same part or none).</summary>
    internal static CutPlan? ReadCut(string name, out string why)
    {
        why = "not a cut part";
        if (!IsCutName(name) || !name.EndsWith(')'))
            return null;
        if (name.Length > ConstructParts.MaxRuntimeName)
        {
            why = "too many cuts for this part's key";
            return null;
        }
        var plate = name.StartsWith(CutPlatePrefix, StringComparison.Ordinal);
        var inner = name[(plate ? CutPlatePrefix : CutPanelPrefix).Length..^1];
        var split = inner.IndexOf(CutSplit, StringComparison.Ordinal);
        if (split <= 0)
            return null;
        var blankText = inner[..split];
        var plan = new CutPlan { Plate = plate };
        if (plate)
        {
            if (Extrusion.ParseKey(Extrusion.Prefix + blankText) is not { } k || Extrusion.Name(k) != Extrusion.Prefix + blankText)
            {
                why = "not a piece an Extruder makes";
                return null;
            }
            if (StockRefusal(k) is { } bad)
            {
                why = bad;
                return null;
            }
            plan.Stock = k;
            plan.Blank = Extrusion.Prefix + blankText;
            var mm = Extrusion.PresetOf(k.Mask) == PlateSection ? k.H * 2.5f : k.H * 10f;
            plan.Thick = mm / 1000f;
            plan.Sheet = new PanelShape(0, k.L, k.W, 0, 0, (int)mm, RecyclingTable.Current.NameOf(k.Material));
        }
        else
        {
            plan.Blank = PanelPrefix + blankText + ")";
            if (PanelOf(plan.Blank) is not { } sh)
            {
                why = "its blank is not a panel a Panel Former makes";
                return null;
            }
            plan.Sheet = sh;
            plan.Thick = sh.Mm / 1000f;
        }
        // The cuts.
        var words = inner[(split + CutSplit.Length)..].Split(' ');
        if (words.Length == 0 || words.Length == 1 && words[0].Length == 0)
        {
            why = "nothing to cut: a Panel Former's order";
            return null;
        }
        if (words.Length > MaxCuts)
        {
            why = $"at most {MaxCuts} cuts a part";
            return null;
        }
        var cuts = new Cut[words.Length];
        for (var n = 0; n < words.Length; n++)
        {
            var w = words[n];
            if (w.Length < 2 || w[0] is not ('r' or 'm'))
                return null;
            var nums = w[1..].Split(',');
            var v = new int[nums.Length];
            for (var q = 0; q < nums.Length; q++)
                if (!int.TryParse(nums[q], NumberStyles.None, CultureInfo.InvariantCulture, out v[q]))
                    return null;
            if (w[0] == 'r' && nums.Length == 5)
                cuts[n] = new Cut(false, v[0], v[1], v[2], v[3], v[4]);
            else if (w[0] == 'm' && nums.Length == 3)
                cuts[n] = new Cut(true, v[0], 0, v[1], v[2], 0);
            else
                return null;
        }
        if (CutKey(plate, blankText, cuts) != name)
            return null; // (one spelling a key: the cuts in their order, no leading zeros)
        plan.Cuts = cuts;
        if (CutRules(plan) is { } broken)
        {
            why = broken;
            return null;
        }
        Plan(plan);
        if (plan.Items > CutMaxFacets)
        {
            why = $"too many facets ({plan.Items} of {CutMaxFacets}): fewer cut-outs, or straighter ones";
            return null;
        }
        if (plan.Rects.Length > CutMaxBoxes)
        {
            why = $"too many collision boxes ({plan.Rects.Length} of {CutMaxBoxes}): line the cut-outs up, or use fewer";
            return null;
        }
        why = "";
        return plan;
    }

    /// <summary>Why an extruded piece can't be cut, or null: flat stock only, plate (a quarter of its height thick) or a solid bar lying flat, at most 10 cm thick and 6 m a side.</summary>
    public static string? StockRefusal(in ExtrusionKey k)
    {
        if (k.Growing)
            return "that piece is still on its Extruder";
        if (k.Shape is ExShape.Round or ExShape.Pipe)
            return $"a {(k.Shape == ExShape.Pipe ? "pipe" : "round bar")} is not flat stock: a Laser Cutter cuts plate or a flat bar";
        if (k.Shape != ExShape.Section)
            return "that is not stock: a Laser Cutter cuts plate or a flat bar";
        var section = Extrusion.PresetOf(k.Mask);
        if (section is not (PlateSection or SolidSection))
        {
            var label = section >= 0 ? Extrusion.ProfileLabel(k.Mask) : "custom section";
            label = label.Length > 1 && label[1] == '-' ? label : label.ToLowerInvariant();
            return $"a Laser Cutter cuts flat stock: plate or a flat bar, not {("aeiouAEIOUL".Contains(label[0]) ? "an" : "a")} {label}";
        }
        var mm = section == PlateSection ? k.H * 2.5f : k.H * 10f;
        if (section == SolidSection && k.H > k.W)
            return "lay the bar flat: a Laser Cutter cuts through its thinner side";
        if (mm > CutMaxThickMm)
            return $"too thick for any Laser Cutter's bed: {mm / 1000f:0.##} m, the largest takes {CutMaxThickMm / 1000} m";
        if (k.L < PanelMinCm || k.L > CutMaxCm || k.W < PanelMinCm || k.W > CutMaxCm)
            return k.L > CutMaxCm ? $"cut it to length at the Extruder: a Laser Cutter's bed is {CutMaxCm / 100} m" : $"a cut piece is {PanelMinCm} to {CutMaxCm} cm long and wide";
        return null;
    }

    // ---- The rules -----------------------------------------------------------------------------------------------

    private static Vector2d[] Outline(in Cut c, int l, int w)
    {
        if (c.Corner)
            return c.U switch
            {
                0 => [new(0, 0), new(c.A, 0), new(0, c.B)],
                1 => [new(0, w), new(0, w - c.B), new(c.A, w)],
                2 => [new(l, w), new(l - c.A, w), new(l, w - c.B)],
                _ => [new(l, 0), new(l, c.B), new(l - c.A, 0)],
            };
        double u0 = c.U - c.A / 2.0, u1 = c.U + c.A / 2.0, v0 = c.V - c.B / 2.0, v1 = c.V + c.B / 2.0, k = c.C;
        var pts = new List<Vector2d>(8)
        {
            new(u0 + k, v0), new(u1 - k, v0), new(u1, v0 + k), new(u1, v1 - k), new(u1 - k, v1), new(u0 + k, v1), new(u0, v1 - k), new(u0, v0 + k),
        };
        for (var i = pts.Count - 1; i >= 0 && pts.Count > 3; i--)
            if (Near(pts[i], pts[(i + 1) % pts.Count]))
                pts.RemoveAt(i);
        return [.. pts];
    }

    private static bool Near(Vector2d a, Vector2d b) => Math.Abs(a.U - b.U) < 1e-9 && Math.Abs(a.V - b.V) < 1e-9;

    private static string? CutRules(CutPlan plan)
    {
        int l = plan.Sheet.L, w = plan.Sheet.W;
        Span<bool> corners = stackalloc bool[4];
        var polys = new Vector2d[plan.Cuts.Length][];
        for (var n = 0; n < plan.Cuts.Length; n++)
        {
            var c = plan.Cuts[n];
            if (c.Corner)
            {
                if (c.U is < 0 or > 3)
                    return "a cut corner is 0 to 3: back left, back right, nose right, nose left";
                if (corners[c.U])
                    return "one cut a corner";
                corners[c.U] = true;
                if (c.A < CutMinCm || c.B < CutMinCm || c.A > l - CutMinCm || c.B > w - CutMinCm)
                    return $"a cut corner is at least {CutMinCm} cm a side and leaves {CutMinCm} cm of the edges it cuts";
            }
            else
            {
                if (c.U > l || c.V > w)
                    return "an opening's middle lies on the sheet";
                if (c.A < CutMinCm || c.B < CutMinCm || c.A > CutMaxCm || c.B > CutMaxCm)
                    return $"an opening is {CutMinCm} to {CutMaxCm} cm a side";
                if (c.C * 2 > Math.Min(c.A, c.B))
                    return "an opening's corners are cut back by at most half its smaller side";
            }
            polys[n] = Outline(c, l, w);
        }
        double cutArea = 0;
        for (var n = 0; n < polys.Length; n++)
        {
            var (mnU, mxU, mnV, mxV) = Extent(polys[n]);
            if (mnU <= 0 && mxU >= l || mnV <= 0 && mxV >= w)
                return "that cut would part the sheet: it reaches two opposite edges";
            foreach (var d in (ReadOnlySpan<double>)[mnU, l - mxU, mnV, w - mxV])
                if (d > 0 && d < CutWebCm)
                    return $"a cut comes within {CutWebCm} cm of an edge: leave {CutWebCm} cm of sheet or cut through it";
            var on = ClipRect(polys[n], 0, l, 0, w);
            var a = Area(on);
            if (a <= 0)
                return "a cut lies off the sheet";
            cutArea += a;
            for (var m = 0; m < n; m++)
            {
                if (!Apart(polys[n], polys[m]))
                    return "two cut-outs overlap";
                if (Gap(polys[n], polys[m]) < CutWebCm)
                    return $"cut-outs keep {CutWebCm} cm of sheet between them";
            }
        }
        if (cutArea > 0.75 * l * w)
            return "at least a quarter of the sheet must be left";
        plan.Polys = polys;
        return null;
    }

    private static (double MnU, double MxU, double MnV, double MxV) Extent(Vector2d[] p)
    {
        var (a, b, c, d) = (double.MaxValue, double.MinValue, double.MaxValue, double.MinValue);
        foreach (var q in p)
            (a, b, c, d) = (Math.Min(a, q.U), Math.Max(b, q.U), Math.Min(c, q.V), Math.Max(d, q.V));
        return (a, b, c, d);
    }

    private static double Area(Vector2d[] p)
    {
        double s = 0;
        for (var i = 0; i < p.Length; i++)
        {
            var (a, b) = (p[i], p[(i + 1) % p.Length]);
            s += a.U * b.V - b.U * a.V;
        }
        return Math.Abs(s) / 2;
    }

    /// <summary>Two convex outlines with a line between them (a separating axis among their edges' normals).</summary>
    private static bool Apart(Vector2d[] a, Vector2d[] b)
    {
        foreach (var poly in (Vector2d[][])[a, b])
            for (var i = 0; i < poly.Length; i++)
            {
                var (p, q) = (poly[i], poly[(i + 1) % poly.Length]);
                double nu = q.V - p.V, nv = p.U - q.U;
                var (amin, amax) = Project(a, nu, nv);
                var (bmin, bmax) = Project(b, nu, nv);
                if (amax < bmin || bmax < amin)
                    return true;
            }
        return false;
    }

    private static (double, double) Project(Vector2d[] p, double nu, double nv)
    {
        var (mn, mx) = (double.MaxValue, double.MinValue);
        foreach (var q in p)
        {
            var d = q.U * nu + q.V * nv;
            (mn, mx) = (Math.Min(mn, d), Math.Max(mx, d));
        }
        return (mn, mx);
    }

    /// <summary>The distance between two convex outlines that do not overlap: the nearest corner of either to an edge of the other.</summary>
    private static double Gap(Vector2d[] a, Vector2d[] b)
    {
        var best = double.MaxValue;
        foreach (var (x, y) in (ReadOnlySpan<(Vector2d[], Vector2d[])>)[(a, b), (b, a)])
            foreach (var p in x)
                for (var i = 0; i < y.Length; i++)
                    best = Math.Min(best, SegDist(p, y[i], y[(i + 1) % y.Length]));
        return best;
    }

    private static double SegDist(Vector2d p, Vector2d a, Vector2d b)
    {
        double du = b.U - a.U, dv = b.V - a.V, len = du * du + dv * dv;
        var t = len < 1e-12 ? 0 : Math.Clamp(((p.U - a.U) * du + (p.V - a.V) * dv) / len, 0, 1);
        double eu = a.U + t * du - p.U, ev = a.V + t * dv - p.V;
        return Math.Sqrt(eu * eu + ev * ev);
    }

    /// <summary>A convex outline clipped to a rectangle (Sutherland-Hodgman, four sides).</summary>
    private static Vector2d[] ClipRect(Vector2d[] p, double u0, double u1, double v0, double v1)
    {
        var list = new List<Vector2d>(p);
        for (var side = 0; side < 4 && list.Count > 0; side++)
        {
            var src = list;
            list = new List<Vector2d>(src.Count + 4);
            double In(Vector2d q) => side switch { 0 => q.U - u0, 1 => u1 - q.U, 2 => q.V - v0, _ => v1 - q.V };
            for (var i = 0; i < src.Count; i++)
            {
                var (a, b) = (src[i], src[(i + 1) % src.Count]);
                double da = In(a), db = In(b);
                if (da >= 0)
                    list.Add(a);
                if (da >= 0 != db >= 0)
                {
                    var t = da / (da - db);
                    list.Add(new(a.U + t * (b.U - a.U), a.V + t * (b.V - a.V)));
                }
            }
        }
        return list.Count >= 3 ? [.. list] : [];
    }

    // ---- The clipper ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Each facet's pieces: its rectangle on the sheet less the cuts, as convex pieces between the cuts' corners (bands along the sheet,
    /// a band's free spans between the cuts; a span that carries on in the next band along the same two lines is one piece). A cut corner
    /// within <see cref="CutSnapCm"/> of a facet line is moved onto it first, so no sliver is left that the loader would drop.
    /// </summary>
    private static void Plan(CutPlan plan)
    {
        var sh = plan.Sheet;
        var (nu, nv) = PanelGrid(sh);
        var us = new double[nu + 1];
        var vs = new double[nv + 1];
        for (var i = 0; i <= nu; i++)
            us[i] = i == nu ? sh.L : PanelRowS(sh, i, nu) * (double)sh.L;
        for (var j = 0; j <= nv; j++)
            vs[j] = j * (double)sh.W / nv;
        static double Snap(double x, double[] lines)
        {
            foreach (var g in lines)
                if (Math.Abs(x - g) < CutSnapCm)
                    return g;
            return x;
        }
        var polys = new Vector2d[plan.Polys.Length][];
        for (var n = 0; n < polys.Length; n++)
            polys[n] = [.. plan.Polys[n].Select(q => new Vector2d(Snap(q.U, us), Snap(q.V, vs)))];
        plan.Polys = polys;
        plan.Pieces = new Vector2d[nu * nv][][];
        plan.Items = 0;
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                var pieces = FacetPieces(polys, us[i], us[i + 1], vs[j], vs[j + 1]);
                plan.Pieces[i * nv + j] = pieces;
                plan.Items += pieces?.Length ?? 1;
            }
        plan.Us = us;
        plan.Vs = vs;
        PlanRects(plan, nu);
    }

    // ---- Collision (phase 2) -------------------------------------------------------------------------------------

    /// <summary>
    /// The solid sheet as rectangles: the panel's strips (up to four along it, as an uncut panel's boxes) split round each hole. A hole is an
    /// opening of <see cref="CutOpenCm"/> or more both ways, by its whole extent (the drawn metal in its cut-back corners is look only, so
    /// nothing unseen blocks a ray), or a cut corner by the rectangle inside its triangle (half its sides: a little air there still collides).
    /// Each strip is cut into bands at the holes' ends; a band's free spans across are rectangles, and one that carries on unchanged into
    /// the next band is the same rectangle.
    /// </summary>
    private static void PlanRects(CutPlan plan, int nu)
    {
        const double Eps = 1e-6;
        double l = plan.Sheet.L, w = plan.Sheet.W;
        var holes = new List<(double U0, double U1, double V0, double V1)>(plan.Cuts.Length);
        for (var n = 0; n < plan.Cuts.Length; n++)
        {
            var c = plan.Cuts[n];
            var (mnU, mxU, mnV, mxV) = Extent(plan.Polys[n]);
            if (!c.Corner)
            {
                if (c.A >= CutOpenCm && c.B >= CutOpenCm)
                    holes.Add((Math.Max(0, mnU), Math.Min(l, mxU), Math.Max(0, mnV), Math.Min(w, mxV)));
                continue;
            }
            double a = (mxU - mnU) / 2, b = (mxV - mnV) / 2;
            holes.Add(c.U switch
            {
                0 => (0, a, 0, b),
                1 => (0, a, w - b, w),
                2 => (l - a, l, w - b, w),
                _ => (l - a, l, 0, b),
            });
        }
        holes.Sort((x, y) => x.V0.CompareTo(y.V0));
        var rects = new List<CutRect>();
        var open = new List<(double V0, double V1, double U0)>();
        var next = new List<(double V0, double V1, double U0)>();
        var rows = new List<double>();
        var strips = Math.Min(4, nu);
        void Close((double V0, double V1, double U0) r, double u1)
        {
            bool Touch(double u, bool end) // (a hole's far end at u0, or its near end at u1, beside this rectangle)
            {
                foreach (var h in holes)
                    if (Math.Abs((end ? h.U0 : h.U1) - u) < Eps && h.V0 < r.V1 - Eps && h.V1 > r.V0 + Eps)
                        return true;
                return false;
            }
            rects.Add(new CutRect(r.U0, u1, r.V0, r.V1, r.U0 <= Eps || Touch(r.U0, false), u1 >= l - Eps || Touch(u1, true)));
        }
        for (var k = 0; k < strips; k++)
        {
            double sa = plan.Us[k * nu / strips], sb = plan.Us[(k + 1) * nu / strips];
            rows.Clear();
            rows.Add(sa);
            rows.Add(sb);
            foreach (var h in holes)
                foreach (var u in (ReadOnlySpan<double>)[h.U0, h.U1])
                    if (u > sa + Eps && u < sb - Eps)
                        rows.Add(u);
            rows.Sort();
            open.Clear();
            for (var b = 0; b + 1 < rows.Count; b++)
            {
                double ua = rows[b], ub = rows[b + 1];
                if (ub - ua <= Eps)
                    continue;
                next.Clear();
                var v = 0.0;
                void Free(double v0, double v1)
                {
                    if (v1 - v0 <= Eps)
                        return;
                    for (var o = 0; o < open.Count; o++)
                        if (Math.Abs(open[o].V0 - v0) < Eps && Math.Abs(open[o].V1 - v1) < Eps)
                        {
                            next.Add(open[o]);
                            open.RemoveAt(o);
                            return;
                        }
                    next.Add((v0, v1, ua));
                }
                foreach (var h in holes)
                    if (h.U0 < ub - Eps && h.U1 > ua + Eps)
                    {
                        Free(v, h.V0);
                        v = Math.Max(v, h.V1);
                    }
                Free(v, w);
                foreach (var r in open)
                    Close(r, ua);
                (open, next) = (next, open);
            }
            foreach (var r in open)
                Close(r, sb);
        }
        plan.Rects = [.. rects];
    }

    /// <summary>A cut cuts this spot of the sheet away (within half a centimetre): a socket there is left out.</summary>
    private static bool CutAway(CutPlan plan, double u, double v)
    {
        const double Tol = 0.5;
        foreach (var p in plan.Polys)
        {
            var sign = 0.0;
            for (var i = 0; i < p.Length; i++)
            {
                var (a, b) = (p[i], p[(i + 1) % p.Length]);
                sign += a.U * b.V - b.U * a.V;
            }
            var inside = true;
            for (var i = 0; i < p.Length && inside; i++)
            {
                var (a, b) = (p[i], p[(i + 1) % p.Length]);
                double eu = b.U - a.U, ev = b.V - a.V, len = Math.Sqrt(eu * eu + ev * ev);
                if (len < 1e-9)
                    continue;
                var d = (eu * (v - a.V) - ev * (u - a.U)) / len * Math.Sign(sign); // (positive: on the inner side)
                inside = d >= -Tol;
            }
            if (inside)
                return true;
        }
        return false;
    }

    /// <summary>A point on the sheet (cm) on its facet's flat quad, as the pieces are drawn: bilinear in the facet's four corners.</summary>
    private static Vector3 SheetAt(CutPlan plan, double u, double v)
    {
        var sh = plan.Sheet;
        int nu = plan.Us.Length - 1, nv = plan.Vs.Length - 1, i = 0, j = 0;
        while (i < nu - 1 && u > plan.Us[i + 1])
            i++;
        while (j < nv - 1 && v > plan.Vs[j + 1])
            j++;
        float s0 = PanelRowS(sh, i, nu), s1 = PanelRowS(sh, i + 1, nu), x0 = j * 2f / nv - 1, x1 = (j + 1) * 2f / nv - 1;
        var a = (float)((u - plan.Us[i]) / (plan.Us[i + 1] - plan.Us[i]));
        var b = (float)((v - plan.Vs[j]) / (plan.Vs[j + 1] - plan.Vs[j]));
        return PanelPoint(sh, s0, x0) * ((1 - a) * (1 - b)) + PanelPoint(sh, s0, x1) * ((1 - a) * b)
            + PanelPoint(sh, s1, x1) * (a * b) + PanelPoint(sh, s1, x0) * (a * (1 - b));
    }

    private static float CutIn(float v, bool up) => up ? MathF.Ceiling(v * 100 + 0.2f) / 100 : MathF.Floor(v * 100 - 0.2f) / 100;

    private static bool Solid(Vector3 mn, Vector3 mx) => mx.X - mn.X >= 0.01f && mx.Y - mn.Y >= 0.01f && mx.Z - mn.Z >= 0.01f;

    /// <summary>
    /// A cut panel's collision and edge sockets: a hidden box over each rectangle of <see cref="CutPlan.Rects"/>, the bounds of the drawn
    /// surface over it with its rims a whole centimetre inside the drawn edges (an uncut panel's rule, on every opening's rim too); the
    /// blank's edge sockets by their names, each on the box under it, and those whose spot was cut away left out.
    /// </summary>
    private static void CutPanelCollision(Builder p, in PanelShape sh, CutPlan cut, bool slabs, string steel)
    {
        var boxes = new (Vector3 Mn, Vector3 Mx, bool Ok)[cut.Rects.Length];
        for (var r = 0; r < boxes.Length; r++)
        {
            var c = cut.Rects[r];
            var (mn, mx) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            void At(double u)
            {
                void One(double v)
                {
                    var q = SheetAt(cut, u, v);
                    (mn, mx) = (Vector3.Min(mn, q), Vector3.Max(mx, q));
                }
                One(c.V0);
                foreach (var v in cut.Vs)
                    if (v > c.V0 && v < c.V1)
                        One(v);
                One(c.V1);
            }
            At(c.U0);
            foreach (var u in cut.Us)
                if (u > c.U0 && u < c.U1)
                    At(u);
            At(c.U1);
            var vm = (c.V0 + c.V1) / 2;
            var dir = SheetAt(cut, c.U1, vm) - SheetAt(cut, c.U0, vm);
            var along = MathF.Abs(dir.Z) >= MathF.Abs(dir.Y) ? 2 : 1; // (the axis the sheet runs along here: z, or y on a fold's raised leg)
            var thick = 3 - along;
            var fwd = dir[along] >= 0;
            var (lowRim, highRim) = fwd ? (c.RimU0, c.RimU1) : (c.RimU1, c.RimU0);
            mn.X = CutIn(mn.X, true);
            mx.X = CutIn(mx.X, false);
            mn[along] = lowRim ? CutIn(mn[along], true) : MathF.Round(mn[along], 2);
            mx[along] = highRim ? CutIn(mx[along], false) : MathF.Round(mx[along], 2);
            (mn[thick], mx[thick]) = (MathF.Round(mn[thick], 2), MathF.Round(mx[thick], 2));
            if (mx[thick] - mn[thick] < 0.03f)
                (mn[thick], mx[thick]) = (mn[thick] - 0.01f, mn[thick] + 0.02f);
            boxes[r] = (mn, mx, Solid(mn, mx));
            if (!boxes[r].Ok)
                continue; // (a web too thin for a box after its rims: drawn only)
            p.Hide(mn, mx);
            if (slabs)
                p.Boxes.Add(new System.Text.Json.Nodes.JsonObject { ["min"] = V(mn.X, mn.Y, mn.Z), ["max"] = V(mx.X, mx.Y, mx.Z), ["color"] = steel, ["detail"] = true });
        }
        // The box under a spot of the sheet, or -1 (cut away, or only a web too thin for a box).
        int BoxAt(double u, double v)
        {
            if (CutAway(cut, u, v))
                return -1;
            for (var r = 0; r < boxes.Length; r++)
            {
                var c = cut.Rects[r];
                if (boxes[r].Ok && u >= c.U0 - 0.5 && u <= c.U1 + 0.5 && v >= c.V0 - 0.5 && v <= c.V1 + 0.5)
                    return r;
            }
            return -1;
        }
        static float Cm2(float v) => MathF.Round(v, 2);
        double l = sh.L, w = sh.W;
        var along2 = Math.Clamp(sh.L / 50, 1, 6);
        var across = Math.Clamp(sh.W / 50, 1, 6);
        for (var k = 0; k < along2; k++)
        {
            var s = (k + 0.5f) / along2;
            var z = Cm2(PanelPoint(sh, s, 0).Z);
            if (BoxAt(s * l, w) is var e and >= 0)
            {
                var (mn, mx, _) = boxes[e];
                p.Sock($"e{k}", new Vector3(mx.X, Math.Clamp(Cm2(PanelPoint(sh, s, 1).Y), mn.Y, mx.Y), Math.Clamp(z, mn.Z, mx.Z)), "+x");
            }
            if (BoxAt(s * l, 0) is var west and >= 0)
            {
                var (mn, mx, _) = boxes[west];
                p.Sock($"w{k}", new Vector3(mn.X, Math.Clamp(Cm2(PanelPoint(sh, s, -1).Y), mn.Y, mx.Y), Math.Clamp(z, mn.Z, mx.Z)), "-x");
            }
        }
        for (var k = 0; k < across; k++)
        {
            var x = (k + 0.5f) / across * 2 - 1;
            var v = (x + 1) / 2 * w;
            if (BoxAt(0, v) is var a and >= 0)
            {
                var (a0, a1, _) = boxes[a];
                var ps = PanelPoint(sh, 0, x);
                p.Sock($"s{k}", new Vector3(Math.Clamp(Cm2(ps.X), a0.X, a1.X), Math.Clamp(Cm2(ps.Y), a0.Y, a1.Y), a0.Z), "-z");
            }
            if (BoxAt(l, v) is var b and >= 0)
            {
                var (b0, b1, _) = boxes[b];
                var pn = PanelPoint(sh, 1, x);
                if (sh.Kind == PanelBend && Math.Abs(sh.Angle) > 45)
                    p.Sock($"n{k}", new Vector3(Math.Clamp(Cm2(pn.X), b0.X, b1.X), sh.Angle > 0 ? b1.Y : b0.Y, Math.Clamp(Cm2(pn.Z), b0.Z, b1.Z)), sh.Angle > 0 ? "+y" : "-y");
                else
                    p.Sock($"n{k}", new Vector3(Math.Clamp(Cm2(pn.X), b0.X, b1.X), Math.Clamp(Cm2(pn.Y), b0.Y, b1.Y), b1.Z), "+z");
            }
        }
        var (nu, _) = PanelGrid(sh);
        var backS = sh.Kind == PanelBend ? PanelRowS(sh, 1, nu) / 2 : 0.5f;
        if (BoxAt(backS * l, w / 2) is var m and >= 0)
        {
            var backZ = sh.Kind == PanelBend ? PanelPoint(sh, backS, 0).Z : sh.L / 200f;
            var (m0, m1, _) = boxes[m];
            p.Sock("back", new Vector3(0, m0.Y, Math.Clamp(Cm2(backZ), m0.Z, m1.Z)), "-y");
        }
    }

    /// <summary>
    /// Each opening of <see cref="CutOpenCm"/> or more whose middle lies on the sheet: a socket <c>o0</c>.. in its middle, on the sheet's middle
    /// plane, facing the way the surface faces there (its nearest axis), for a pane, hatch, lamp or vent. It stands in the opening's air, as
    /// a pipe's <c>base</c> stands in its bore, so it is added to the part made from the file (which wants every socket on a face), on every peer alike.
    /// </summary>
    private static void AddOpeningSockets(ConstructPartDef d, CutPlan plan)
    {
        List<CSocket>? add = null;
        var sh = plan.Sheet;
        foreach (var c in plan.Cuts)
        {
            if (c.Corner || c.A < CutOpenCm || c.B < CutOpenCm || c.U <= 0 || c.U >= sh.L || c.V <= 0 || c.V >= sh.W)
                continue;
            var at = SheetAt(plan, c.U, c.V);
            var n = plan.Plate ? Vector3.UnitY : PanelNormal(sh, c.U / (float)sh.L, c.V * 2f / sh.W - 1);
            var axis = MathF.Abs(n.X) >= MathF.Abs(n.Y) && MathF.Abs(n.X) >= MathF.Abs(n.Z) ? 0 : MathF.Abs(n.Y) >= MathF.Abs(n.Z) ? 1 : 2;
            var dir = (byte)(axis * 2 + (n[axis] < 0 ? 1 : 0));
            add ??= new List<CSocket>(MaxCuts);
            add.Add(new CSocket($"o{add.Count}", new Vector3(R(at.X), R(at.Y), R(at.Z)), dir, SocketKind.Struct, d.Class));
        }
        if (add != null)
            d.Sockets = [.. d.Sockets, .. add];
    }

    private static Vector2d[][]? FacetPieces(Vector2d[][] polys, double u0, double u1, double v0, double v1)
    {
        const double Eps = 1e-6;
        var on = new List<Vector2d[]>();
        foreach (var p in polys)
        {
            var (mnU, mxU, mnV, mxV) = Extent(p);
            if (mxU <= u0 + Eps || mnU >= u1 - Eps || mxV <= v0 + Eps || mnV >= v1 - Eps)
                continue;
            var c = ClipRect(p, u0, u1, v0, v1);
            if (c.Length >= 3 && Area(c) > Eps)
                on.Add(c);
        }
        if (on.Count == 0)
            return null;
        var cuts = new List<double> { u0, u1 };
        foreach (var p in on)
            foreach (var q in p)
                if (q.U > u0 + Eps && q.U < u1 - Eps)
                    cuts.Add(q.U);
        cuts.Sort();
        var rows = new List<double>();
        foreach (var x in cuts)
            if (rows.Count == 0 || x - rows[^1] > Eps)
                rows.Add(x);
        var done = new List<Vector2d[]>();
        // Open pieces from the band before: their sources (-1 the facet's left side, -2 its right, else a cut's span) and their four corners.
        var open = new List<(int L, int R, double Ua, double La, double Ra, double Ub, double Lb, double Rb)>();
        var next = new List<(int L, int R, double Ua, double La, double Ra, double Ub, double Lb, double Rb)>();
        var spans = new List<(double Lo0, double Lo1, double Hi0, double Hi1, int K)>();
        for (var b = 0; b + 1 < rows.Count; b++)
        {
            double ua = rows[b], ub = rows[b + 1];
            spans.Clear();
            for (var k = 0; k < on.Count; k++)
            {
                var (mnU, mxU, _, _) = Extent(on[k]);
                if (mnU > ua + Eps || mxU < ub - Eps)
                    continue;
                var (lo0, hi0) = Span(on[k], ua);
                var (lo1, hi1) = Span(on[k], ub);
                spans.Add((lo0, lo1, hi0, hi1, k));
            }
            spans.Sort((x, y) => (x.Lo0 + x.Lo1).CompareTo(y.Lo0 + y.Lo1));
            next.Clear();
            var (src, la, lb) = (-1, v0, v0);
            void Free(int right, double ra, double rb)
            {
                if (ra - la <= Eps && rb - lb <= Eps)
                    return;
                // Carry on the piece from the band before when it runs between the same two lines, straight on.
                for (var o = 0; o < open.Count; o++)
                {
                    var p = open[o];
                    if (p.L != src || p.R != right || Math.Abs(p.Lb - la) > Eps || Math.Abs(p.Rb - ra) > Eps
                        || !Straight(p.Ua, p.La, p.Ub, p.Lb, ub, lb) || !Straight(p.Ua, p.Ra, p.Ub, p.Rb, ub, rb))
                        continue;
                    open.RemoveAt(o);
                    next.Add((src, right, p.Ua, p.La, p.Ra, ub, lb, rb));
                    return;
                }
                next.Add((src, right, ua, la, ra, ub, lb, rb));
            }
            foreach (var s in spans)
            {
                Free(s.K, s.Lo0, s.Lo1);
                (src, la, lb) = (s.K, s.Hi0, s.Hi1);
            }
            Free(-2, v1, v1);
            foreach (var p in open)
                done.Add(Corners(p.Ua, p.La, p.Ra, p.Ub, p.Lb, p.Rb));
            (open, next) = (next, open);
        }
        foreach (var p in open)
            done.Add(Corners(p.Ua, p.La, p.Ra, p.Ub, p.Lb, p.Rb));
        return [.. done];
    }

    private static bool Straight(double u0, double v0, double u1, double v1, double u2, double v2) =>
        Math.Abs((v1 - v0) * (u2 - u1) - (v2 - v1) * (u1 - u0)) <= 1e-6 * Math.Max(1, (u2 - u0) * (u2 - u0));

    private static Vector2d[] Corners(double ua, double la, double ra, double ub, double lb, double rb)
    {
        var pts = new List<Vector2d>(4) { new(ua, la), new(ub, lb), new(ub, rb), new(ua, ra) };
        for (var i = pts.Count - 1; i >= 0 && pts.Count > 3; i--)
            if (Math.Abs(pts[i].U - pts[(i + 1) % pts.Count].U) < 1e-6 && Math.Abs(pts[i].V - pts[(i + 1) % pts.Count].V) < 1e-6)
                pts.RemoveAt(i);
        return [.. pts];
    }

    /// <summary>Where a convex outline crosses the line U = <paramref name="u"/>: its lowest and highest V.</summary>
    private static (double Lo, double Hi) Span(Vector2d[] p, double u)
    {
        var (lo, hi) = (double.MaxValue, double.MinValue);
        for (var i = 0; i < p.Length; i++)
        {
            var (a, b) = (p[i], p[(i + 1) % p.Length]);
            if (u < Math.Min(a.U, b.U) - 1e-9 || u > Math.Max(a.U, b.U) + 1e-9)
                continue;
            if (Math.Abs(b.U - a.U) < 1e-9)
            {
                (lo, hi) = (Math.Min(lo, Math.Min(a.V, b.V)), Math.Max(hi, Math.Max(a.V, b.V)));
                continue;
            }
            var v = a.V + (u - a.U) / (b.U - a.U) * (b.V - a.V);
            (lo, hi) = (Math.Min(lo, v), Math.Max(hi, v));
        }
        return lo > hi ? (0, 0) : (lo, hi);
    }

    /// <summary>A piece's corners on its facet's flat quad: bilinear in the facet's four corners (and their offsets), so a split facet's pieces meet its whole neighbours.</summary>
    private static void PieceOnQuad(Vector2d[] piece, double u0, double u1, double v0, double v1, Vector3[] q, Vector3[] off, out Vector3[] pts, out Vector3[] offs)
    {
        pts = new Vector3[piece.Length];
        offs = new Vector3[piece.Length];
        for (var c = 0; c < piece.Length; c++)
        {
            var a = (float)((piece[c].U - u0) / (u1 - u0));
            var b = (float)((piece[c].V - v0) / (v1 - v0));
            float w00 = (1 - a) * (1 - b), w01 = (1 - a) * b, w11 = a * b, w10 = a * (1 - b);
            pts[c] = q[0] * w00 + q[1] * w01 + q[2] * w11 + q[3] * w10;
            var o = off[0] * w00 + off[1] * w01 + off[2] * w11 + off[3] * w10;
            offs[c] = o.LengthSquared() < 1e-12f ? Vector3.UnitY : Vector3.Normalize(o);
        }
    }

    private static float PolyArea(Vector3[] p)
    {
        var n = Vector3.Zero;
        for (var i = 0; i < p.Length; i++)
            n += Vector3.Cross(p[i], p[(i + 1) % p.Length]);
        return n.Length() / 2;
    }

    // ---- Making it -----------------------------------------------------------------------------------------------

    /// <summary>(HullLoft.Make's hook.) A cut part from its name; null: not a cut part's name at all.</summary>
    private static (bool Ok, ConstructPartDef? Def)? MakeCut(string name)
    {
        if (!IsCutName(name))
            return null;
        if (ReadCut(name, out var why) is not { } plan)
        {
            Console.WriteLine($"[cuts] {name}: refused, {why}");
            return (false, null);
        }
        try
        {
            var defer = _panelPumped && !PanelSync;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var def = ConstructParts.Parse(PanelJson(name, plan.Sheet, 0, defer ? 0 : plan.Items, defer, plan), name);
            AddOpeningSockets(def, plan);
            if (Adopt(def, name) is not { } made)
                return (false, null);
            if (defer)
                lock (PanelJobs)
                {
                    PanelJobs.Enqueue(new PanelJob { Def = made, Shape = plan.Sheet, Total = plan.Items, Cut = plan });
                    _panelSpent += System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                }
            return (true, made);
        }
        catch (ConstructPartException e)
        {
            Console.WriteLine($"[cuts] {name}: refused, {e.Message}");
            return (false, null);
        }
    }

    /// <summary>
    /// A cut plate's part file: its pieces (already in <paramref name="p"/>), the extrusion's own box as its collision (phase 1) and the extrusion's
    /// own sockets at their places, so a blueprint that welded the plate welds the cut plate; mass, health and armour by the extrusion's rules from what is left.
    /// </summary>
    private static string PlateJson(string name, CutPlan cut, Builder p, float area, bool slabs)
    {
        var k = cut.Stock;
        float w = k.W / 100f, len = k.L / 100f, half = cut.Thick / 2;
        var (mn, mx) = (new Vector3(-w / 2, -half, 0), new Vector3(w / 2, half, len));
        // Collision (phase 2): a box over each rectangle of solid sheet, exact at the plate's own edges (as the extrusion's box), a whole
        // centimetre inside an opening's rim, and exact at a seam between two rectangles.
        foreach (var c in cut.Rects)
        {
            float x0 = (float)(c.V0 / 100 - w / 2), x1 = (float)(c.V1 / 100 - w / 2), z0 = (float)(c.U0 / 100), z1 = (float)(c.U1 / 100);
            if (c.V0 > 0)
                x0 = CutIn(x0, true);
            if (c.V1 < k.W)
                x1 = CutIn(x1, false);
            if (c.RimU0 && c.U0 > 0)
                z0 = CutIn(z0, true);
            if (c.RimU1 && c.U1 < k.L)
                z1 = CutIn(z1, false);
            var (b0, b1) = (new Vector3(x0, -half, z0), new Vector3(x1, half, z1));
            if (!Solid(b0, b1))
                continue;
            p.Hide(b0, b1);
            if (slabs)
                p.Boxes.Add(new System.Text.Json.Nodes.JsonObject { ["min"] = V(b0.X, b0.Y, b0.Z), ["max"] = V(b1.X, b1.Y, b1.Z), ["color"] = Hex(RecyclingTable.Current.ColourOf(k.Material)), ["detail"] = true });
        }
        // The extrusion's own sockets at their places, less those whose spot was cut away.
        foreach (var s in Extrusion.Sockets(k, w, k.H / 100f, len, mn, mx))
            if (!CutAway(cut, s.At.Z * 100, (s.At.X + w / 2) * 100))
                p.Sock(s.Name, s.At, ConstructParts.DirName(s.Dir));
        var names = RecyclingTable.Current;
        var row = ExtrusionTable.Current.Find(k.Material)!;
        var volume = area * cut.Thick;
        var part = new System.Text.Json.Nodes.JsonObject
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = $"A cut {(Extrusion.PresetOf(k.Mask) == PlateSection ? "plate" : "flat bar")}, {len:0.##} by {w:0.##} m of {cut.Thick * 100:0.#} cm {names.NameOf(k.Material)}, with {cut.Cuts.Length} cut-out{(cut.Cuts.Length == 1 ? "" : "s")}. Cut by a Laser Cutter; weld it by its faces.",
            ["group"] = "Production",
            ["section"] = "Cut parts",
            ["role"] = "frame",
            ["tool"] = "welder",
            ["mass"] = MathF.Round(Math.Clamp(volume * row.UnitsPerM3 * names.WeightOf(k.Material), 0.5f, ConstructParts.MaxPartMass), 1),
            ["health"] = MathF.Round(Math.Clamp(volume * row.HealthPerM3, 10, ConstructParts.MaxPartHealth)),
            ["armour"] = MathF.Round(MathF.Min(row.MaxArmour, row.ArmourPerCm * cut.Thick * 100)),
            ["material"] = row.Class,
            ["color"] = Hex(names.ColourOf(k.Material)),
            ["attachSeconds"] = Math.Clamp(2 + volume * 2, 2, 8),
            ["stage"] = true, // (never offered in the menus: a Laser Cutter makes it)
            ["centreSockets"] = false, // (an extrusion has none: its sockets are its own)
            ["boxes"] = p.Boxes,
            ["sockets"] = p.Sockets,
        };
        return part.ToJsonString();
    }

    /// <summary>The material a cut part is made of (0: not a cut part).</summary>
    private static byte CutMaterial(string name)
    {
        var plate = name.StartsWith(CutPlatePrefix, StringComparison.Ordinal);
        var inner = name[(plate ? CutPlatePrefix : CutPanelPrefix).Length..];
        var split = inner.IndexOf(CutSplit, StringComparison.Ordinal);
        if (split <= 0)
            return 0;
        if (plate)
            return Extrusion.ParseKey(Extrusion.Prefix + inner[..split]) is { } k ? k.Material : (byte)0;
        return PanelOf(PanelPrefix + inner[..split] + ")") is { } s ? RecyclingTable.Current.IdOf(s.Material) : (byte)0;
    }

    /// <summary>A material's colour as a part file writes it.</summary>
    private static string Hex(Vector3 c) =>
        string.Create(CultureInfo.InvariantCulture, $"#{(int)Math.Clamp(c.X * 255 + 0.5f, 0, 255):x2}{(int)Math.Clamp(c.Y * 255 + 0.5f, 0, 255):x2}{(int)Math.Clamp(c.Z * 255 + 0.5f, 0, 255):x2}");
}
