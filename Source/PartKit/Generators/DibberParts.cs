using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Dibber, the farmers' vertical lander (Karl, 2026-11-21: a picture of a squat tapered lander on four jointed legs, "add this lander
/// to the space faction"; he picked the farmers). A small craft that drops straight down from a ship in orbit to a field and back:
/// an eight-sided capsule standing on end, wider at the shoulder over its belly than at its roof, a glass cab poking out low on the
/// nose, one lift jet in a skirt under the belly, a trim jet under each of four fixed legs at the diagonals, angled out over a knee to broad pads.
///
/// Two decks inside a separate room shell (an upright octagon inside the tapering skin): the cockpit and the boarding hatch below
/// (two couches, the flight computer between them, two suit lockers, a belly hatch that slides down to the ground with its ladder on
/// it), a ladder up to the upper deck (the power plant, batteries, life support and its oxygen, room for crates). The look is the
/// farmers' (the Haywain's combine harvester, haywain.md) in lighter dress: cream pressed-steel panels, an orange band round the
/// upper hull, graphite panels on the diagonal faces, a dark chassis, hazard yellow on the feet.
///
/// Built of welded pieces as the Zero Day is: the belly, the cab, the floor between the decks, the roof, four legs, and each deck's
/// shell in four quarters (each a flat face and the diagonal face after it). Ship metres: x across, y up from the belly's underside,
/// z along (forward is -z); P() moves them by half a cell into a part's frame. Facet k faces the angle 45k degrees from +x towards +z
/// (0 starboard, 2 aft, 4 port, 6 fore). Notes: docs/sandbox/constructs/dibber.md.
/// </summary>
public sealed class DibberParts : IPartGenerator
{
    public string Name => "dibber_parts";

    private const string Out = "Constructs/Parts/Dibber";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Dibber lander";
    public const string ShipName = "Dibber Lander";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: cream pressed steel and a paler cream panel, a farm-machine orange band, graphite panels, a dark chassis, hazard
    // yellow. Inside: cream walls over an olive dado (the Haywain's), a dark rubber floor.
    private const string Cream = "#e4d8b8", Cream2 = "#d4c7a3", Orange = "#d8692a", Grey = "#5a5f63", Grey2 = "#474b4f", Frame = "#2e302d",
        Dark = "#222421", Hazard = "#e8b317", Amber = "#f0a830", Red = "#d03a2a", Glass = "#8fc3c8", Chrome = "#9aa0a4",
        Wall = "#d6cfb8", WallLow = "#56654a", WallTrim = "#2e302d", Deck = "#3e403c", Ceil = "#c4bfac", Console = "#22262b";

    // ---- the plan (y up from the belly's underside)

    private const double Ft = 0.3, Mid0 = 2.85, Mid1 = 3.1, Roof0 = 5.2, RoofMid = 5.4, Top = 5.6; // the lower deck, the floor between, the roof
    private const double LIn = 2.2, UIn = 1.75; // the room shells' apothems: lower and upper deck
    private const double Strip = 0.15; // the hidden boxes' strips
    private static readonly double C225 = Math.Cos(Math.PI / 8), Tan225 = Math.Tan(Math.PI / 8), Sq2 = Math.Sqrt(2);

    /// <summary>The skin's apothem at height y: a chamfer out from the belly to the shoulder, a long taper to the roof's edge, a chamfer in to the roof.</summary>
    private static double A(double y) =>
        y <= 0.6 ? 2.3 + 0.75 * y :
        y <= Roof0 ? 2.75 - (2.75 - 2.05) * (y - 0.6) / (Roof0 - 0.6) :
        2.05 - 1.25 * (y - Roof0);

    /// <summary>The skin's slope (da/dy) in the band from y0 to y1.</summary>
    private static double Slope(double y0, double y1) => (A(y1) - A(y0)) / (y1 - y0);

    // The cab: a glass box poking out of the fore face over the lower deck, its glass raked back.
    private const double CabX = 0.75, CabY0 = 0.75, CabY1 = 2.25, CabSill = 0.85, CabZ0 = -3.2, CabZ1 = -2.95, CabBack = -2.2;

    // The boarding shaft (aft of the jets): too shallow for a flight under its deck hatch, so its belly hatch hangs on its fore edge and
    // carries the rungs (BoardingShaft.Shallow); the hole up into the upper deck (starboard).
    private const double HatchZ1 = 2.1;
    private const double ShaftX = 0.6, ShaftZ0 = 1.2, SkirtZ = -0.06, SkirtA = 1.15; // (the shaft through the belly: a little wider than the hatch, on the strips' lattice)
    private const double HoleX0 = 0.9, HoleX1 = 1.65, HoleZ0 = -0.45, HoleZ1 = 0.45;
    private const double Ground = -1.5; // the pads' soles
    internal static BoardingShaft.Spec Shaft => new("Dibber", Section, Group, -ShaftX, ShaftZ0, ShaftX, HatchZ1, Ft + 0.012, 0, Ground, false, Grey2, Frame, Hazard, Fore: true, Lip: 0);

    // Flying it: one big jet in the skirt under the middle of the belly, a small trim jet under each leg's thigh (the flight computer tips it on those), RCS pads on the lower flat faces and four up the roof.
    private const double MainForce = 440000, MainScale = 1.15, TrimForce = 80000, TrimScale = 0.55, Burn = 2.5, TrimR = 2.95, TrimY = 0.4;
    private const string MainJet = "Dibber Belly Jet", TrimJet = "Dibber Trim Jet";
    private const int BellyFuel = 1800, ShareFuel = 75; // litres: the belly, and each piece that carries jets (a jet burns from the tank it is on)
    private const double RcsYFore = 2.55, RcsY = 1.8, RcsPadW = 0.56; // (the fore pad over the cab, the others level with the centre of mass)

    // The legs: in the radial plane of each diagonal, the hip on the belly's chamfer, the knee out and up, the ankle over the pad.
    private const double HipR = 2.45, HipY = 0.22, KneeR = 3.45, KneeY = 0.85, AnkleR = 4.25, AnkleY = -1.12, PadR = 4.3;

    // ---- geometry helpers

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Sh(double[] q) => P(q[0], q[1], q[2]);

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? [0, 1, 0] : [a[0] / l, a[1] / l, a[2] / l];
    }

    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    /// <summary>A point on facet k of an octagon of apothem a at height y: u across from its corner at 45k - 22.5 degrees to the one at
    /// 45k + 22.5, d out along its normal (s the slope of the octagon's apothem with height). Unshifted ship metres.</summary>
    private static double[] Pt(int k, double u, double y, double a, double s, double d)
    {
        var r = a / C225;
        double t0 = (45 * k - 22.5) * Math.PI / 180, t1 = (45 * k + 22.5) * Math.PI / 180;
        double[] p0 = [r * Math.Cos(t0), y, r * Math.Sin(t0)], p1 = [r * Math.Cos(t1), y, r * Math.Sin(t1)];
        var q = Lerp(p0, p1, u);
        var n = Normal(k, s);
        return [q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d];
    }

    private static double[] Normal(int k, double s)
    {
        var t = 45 * k * Math.PI / 180;
        return Unit([Math.Cos(t), -s, Math.Sin(t)]);
    }

    /// <summary>A point on the skin's facet k, in the band from y0 to y1 (its slope), at u and y, d out.</summary>
    private static double[] Skin(int k, double y0, double y1, double u, double y, double d = 0) => Pt(k, u, y, A(y), Slope(y0, y1), d);

    /// <summary>A flat plate through the points (a convex polygon), thick about them: a "hull" shape, drawn only.</summary>
    private static void Plate(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool glow = false, double thick = T)
    {
        double[]? n = null;
        for (var i = 2; i < pts.Length && n == null; i++)
        {
            var c = Cross(Sub(pts[1], pts[0]), Sub(pts[i], pts[0]));
            if (Dot(c, c) > 1e-8)
                n = Unit(c);
        }
        if (n == null)
            return;
        var both = new List<double[]>();
        foreach (var q in pts)
            foreach (var s in new[] { -1, 1 })
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(q[k] + s * n[k] * thick / 2, 4))]);
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (!glass && !glow)
            shape["finish"] = finish;
        if (glow)
            shape["glow"] = true;
        p.Add(Box(mn, mx, color, detail: true, glass: glass, shape: shape));
    }

    /// <summary>A plate through points given in unshifted ship metres.</summary>
    private static void PlateS(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool glow = false, double thick = T) =>
        Plate(p, [.. pts.Select(Sh)], color, finish, glass, glow, thick);

    /// <summary>A convex solid between two outlines (drawn only), unshifted ship metres.</summary>
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false)
    {
        var both = a.Concat(b).Select(q => Sh(q).Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (glow)
            shape["glow"] = true;
        else
            shape["finish"] = finish;
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        var shape = finish == null ? null : Kw(("finish", finish));
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: shape));
    }

    private static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        if (Math.Abs(x1 - x0) < 0.05 || Math.Abs(y1 - y0) < 0.05 || Math.Abs(z1 - z0) < 0.05)
            return;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));
    }

    private static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    /// <summary>The octagon of apothem a as a polygon in (x, z), corners at 22.5 + 45i degrees.</summary>
    private static List<(double X, double Z)> Oct(double a) => [.. Enumerable.Range(0, 8).Select(i =>
    {
        var t = (22.5 + 45 * i) * Math.PI / 180;
        return (a / C225 * Math.Cos(t), a / C225 * Math.Sin(t));
    })];

    /// <summary>Clips a convex polygon to the half plane nx x + nz z &lt;= c.</summary>
    private static List<(double X, double Z)> Clip(List<(double X, double Z)> poly, double nx, double nz, double c)
    {
        var res = new List<(double, double)>();
        for (var i = 0; i < poly.Count; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % poly.Count]);
            double fa = nx * a.X + nz * a.Z - c, fb = nx * b.X + nz * b.Z - c;
            if (fa <= 0)
                res.Add(a);
            if (fa * fb < 0)
            {
                var t = fa / (fa - fb);
                res.Add((a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t));
            }
        }
        return res;
    }

    /// <summary>The octagon of apothem a less a rectangular hole, as up to four convex pieces.</summary>
    private static List<List<(double X, double Z)>> OctLess(double a, double x0, double z0, double x1, double z1)
    {
        var o = Oct(a);
        var mid = Clip(Clip(o, 0, -1, -z0), 0, 1, z1);
        return [.. new[] { Clip(o, 0, 1, z0), Clip(o, 0, -1, -z1), Clip(mid, 1, 0, x0), Clip(mid, -1, 0, -x1) }.Where(q => q.Count >= 3)];
    }

    /// <summary>A flat horizontal plate over a polygon at height y (its middle).</summary>
    private static void Floor(Part p, List<(double X, double Z)> poly, double y, string color, string finish, double thick = 0.02) =>
        PlateS(p, [.. poly.Select(q => new[] { q.X, y, q.Z })], color, finish, thick: thick);

    /// <summary>
    /// The hidden boxes between two octagons (apothems aIn and aOut; aIn 0 fills it) from y0 to y1, in strips along x: each strip's box
    /// runs from the inner octagon to the outer one at its inner edge, so none pokes into the room and the outer corners stand at most a
    /// strip's width proud of the skin. holes: boxes (x0, y0, z0, x1, y1, z1) the strips leave open. owner picks the part for a box by its middle.
    /// </summary>
    private static void Ring(Func<double, double, Part> owner, double y0, double y1, double aIn, double aOut, params double[][] holes)
    {
        double ZIn(double x) => aIn <= 0 || x >= aIn - 1e-9 ? 0 : Math.Min(aIn, aIn * Sq2 - x);
        double ZOut(double x) => Math.Min(aOut, aOut * Sq2 - x);
        var edges = new List<double>();
        for (var x = 0.0; x < (aIn > 0 ? aIn : aOut) - 1e-6; x += Strip)
            edges.Add(Math.Round(x, 4));
        if (aIn > 0)
            for (var x = aIn; x < aOut - 1e-6; x += Strip)
                edges.Add(Math.Round(x, 4));
        edges.Add(aOut);
        for (var i = 0; i < edges.Count - 1; i++)
        {
            double xa = edges[i], xb = edges[i + 1];
            double za = ZIn(xa), zb = ZOut(xa);
            if (zb - za < 0.05)
                continue;
            foreach (var sx in new[] { -1, 1 })
                foreach (var sz in new[] { -1, 1 })
                {
                    double bx0 = sx < 0 ? -xb : xa, bx1 = sx < 0 ? -xa : xb, bz0 = sz < 0 ? -zb : za, bz1 = sz < 0 ? -za : zb;
                    // (z0, z1, y0, y1) pieces; a hole that overlaps the strip cuts it along z, and in its z range along y
                    var pieces = new List<(double Z0, double Z1, double Y0, double Y1)> { (bz0, bz1, y0, y1) };
                    foreach (var h in holes)
                    {
                        if (!(bx0 < h[3] - 1e-6 && bx1 > h[0] + 1e-6))
                            continue;
                        var next = new List<(double, double, double, double)>();
                        foreach (var b in pieces)
                        {
                            if (!(b.Z0 < h[5] - 1e-6 && b.Z1 > h[2] + 1e-6 && b.Y0 < h[4] - 1e-6 && b.Y1 > h[1] + 1e-6))
                            {
                                next.Add(b);
                                continue;
                            }
                            next.Add((b.Z0, h[2], b.Y0, b.Y1));
                            next.Add((h[5], b.Z1, b.Y0, b.Y1));
                            double m0 = Math.Max(b.Z0, h[2]), m1 = Math.Min(b.Z1, h[5]);
                            next.Add((m0, m1, b.Y0, Math.Min(b.Y1, h[1])));
                            next.Add((m0, m1, Math.Max(b.Y0, h[4]), b.Y1));
                        }
                        pieces = [.. next.Where(b => b.Item2 - b.Item1 > 0.049 && b.Item4 - b.Item3 > 0.049)];
                    }
                    foreach (var b in pieces)
                        Hidden(owner((bx0 + bx1) / 2, (b.Z0 + b.Z1) / 2), bx0, b.Y0, b.Z0, bx1, b.Y1, b.Z1);
                }
        }
    }

    /// <summary>The quarter (0 starboard, 1 aft, 2 port, 3 fore) a point's angle falls in: each a flat face and half of each diagonal beside it.</summary>
    private static int Quarter(double x, double z)
    {
        var t = Math.Atan2(z, x) * 180 / Math.PI;
        return (int)Math.Floor(((t + 45) % 360 + 360) % 360 / 90);
    }

    /// <summary>Deals what was drawn on diagonal facet k (into scratch) to the two quarters either side of its middle.</summary>
    private static void Deal(Part scratch, Part[] q)
    {
        foreach (var b in scratch.Boxes)
        {
            double[] mn = Vd(b, "min"), mx = Vd(b, "max");
            q[Quarter((mn[0] + mx[0]) / 2, (mn[2] + mx[2]) / 2)].Add(b);
        }
    }

    /// <summary>Draws a facet whole, or a diagonal one in two halves with a seam between (so each half goes to its own quarter).</summary>
    private static void Halves(int k, Action<double, double> draw)
    {
        if (k % 2 == 0)
            draw(0, 1);
        else
        {
            draw(0, 0.499);
            draw(0.501, 1);
        }
    }

    /// <summary>A scratch part to draw a diagonal facet into.</summary>
    private static Part Scratch() => new("scratch", Small, Section, "", group: Group);

    private static Part Big(string name, string desc, string color = Cream, string role = "frame") =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    // ---- drawing the skin

    /// <summary>Facet k's skin from y0 to y1 (or a part of it: u from ua to ub); odd facets sit 6 mm up so neighbours' ends never share a plane.</summary>
    private static void Facet(Part p, int k, double y0, double y1, string color, string finish = "painted plating", double ua = 0, double ub = 1)
    {
        var st = k % 2 == 1 ? 0.006 : 0;
        double ya = y0 + st + 0.0015, yb = y1 + st - 0.0015;
        PlateS(p, [Skin(k, y0, y1, ua, ya), Skin(k, y0, y1, ub, ya), Skin(k, y0, y1, ub, yb), Skin(k, y0, y1, ua, yb)], color, finish);
    }

    /// <summary>A bevelled panel on facet k (band y0..y1) from (u0, ya) to (u1, yb), standing d0..d1 out, its face drawn in by bev metres.</summary>
    private static void Slab(Part p, int k, double y0, double y1, double u0, double u1, double ya, double yb, double d0, double d1, double bev, string color, string finish = "painted plating")
    {
        var w = 2 * A((ya + yb) / 2) * Tan225;
        var bu = bev / w;
        double[][] inner = [Skin(k, y0, y1, u0, ya, d0), Skin(k, y0, y1, u1, ya, d0), Skin(k, y0, y1, u1, yb, d0), Skin(k, y0, y1, u0, yb, d0)];
        double[][] outer = [Skin(k, y0, y1, u0 + bu, ya + bev, d1), Skin(k, y0, y1, u1 - bu, ya + bev, d1), Skin(k, y0, y1, u1 - bu, yb - bev, d1), Skin(k, y0, y1, u0 + bu, yb - bev, d1)];
        Prism(p, inner, outer, color, finish);
    }

    /// <summary>A thin flat strip on facet k (a stripe, a seam, a lamp), centred d out.</summary>
    private static void FStrip(Part p, int k, double y0, double y1, double u0, double u1, double ya, double yb, double d, string color, string finish = "painted plating", bool glow = false) =>
        PlateS(p, [Skin(k, y0, y1, u0, ya, d), Skin(k, y0, y1, u1, ya, d), Skin(k, y0, y1, u1, yb, d), Skin(k, y0, y1, u0, yb, d)], color, finish, glow: glow, thick: 0.014);

    /// <summary>The room shell's lining on facet k (an upright octagon of apothem a, its inner face on a): the dado, the trim, the wall.</summary>
    private static void Lining(Part p, int k, double a, double floor, double ceil, double ua = 0, double ub = 1, double ya = -1, double yb = -1)
    {
        var lo = ya < 0 ? floor + 0.01 : ya;
        var hi = yb < 0 ? ceil - 0.02 : yb;
        void L(double y0, double y1, string col, double d, string fin)
        {
            if (y1 - y0 < 0.01)
                return;
            var st = k % 2 == 1 ? 0.004 : 0;
            PlateS(p, [Pt(k, ua, y0 + st, a, 0, d), Pt(k, ub, y0 + st, a, 0, d), Pt(k, ub, y1 + st, a, 0, d), Pt(k, ua, y1 + st, a, 0, d)], col, fin);
        }
        double dado = floor + 0.98;
        L(lo, Math.Min(hi, dado - 0.002), WallLow, 0.02, "painted plating");
        if (hi > dado + 0.04 && lo < dado)
            L(dado, dado + 0.04, WallTrim, 0.012, "painted steel");
        L(Math.Max(lo, dado + 0.042), hi, Wall, 0.02, "painted plating");
    }

    /// <summary>The cladding on facet k of a band: the flat faces cream pressed-steel panels (a pale one with seams), the diagonals a
    /// graphite panel up the middle; the pattern is the same each side.</summary>
    private static void Panels(Part p, int k, double y0, double y1, double ya, double yb, int salt)
    {
        if (yb - ya < 0.12)
            return;
        if (k % 2 == 1)
        {
            Slab(p, k, y0, y1, 0.24, 0.497, ya, yb, 0.019, 0.07, 0.03, (k + salt) % 4 == 1 ? Grey2 : Grey, "worn paint");
            Slab(p, k, y0, y1, 0.503, 0.76, ya, yb, 0.019, 0.07, 0.03, (k + salt) % 4 == 1 ? Grey2 : Grey, "worn paint");
            Slab(p, k, y0, y1, 0.06, 0.2, ya + 0.1, yb - 0.1, 0.019, 0.045, 0.015, Cream2);
            Slab(p, k, y0, y1, 0.8, 0.94, ya + 0.1, yb - 0.1, 0.019, 0.045, 0.015, Cream2);
            return;
        }
        Slab(p, k, y0, y1, 0.05, 0.95, ya, yb, 0.019, 0.055, 0.03, (salt + k / 2) % 2 == 0 ? Cream2 : Cream);
        // Pressed ribs across a tall panel, a latch on a short one.
        if (yb - ya > 0.8)
            for (var y = ya + 0.25; y < yb - 0.2; y += 0.35)
                FStrip(p, k, y0, y1, 0.1, 0.9, y - 0.015, y + 0.015, 0.062, Cream, "painted plating");
        else
            FStrip(p, k, y0, y1, 0.78, 0.86, (ya + yb) / 2 - 0.025, (ya + yb) / 2 + 0.025, 0.062, Dark, "dirty steel");
    }

    // ---- the pieces

    /// <summary>The belly: the floor slab under the lower deck with the boarding hatch's shaft, the chamfer out to the shoulder, the
    /// skirt round the four lift jets and their mounts, the fuel.</summary>
    private static Part Belly()
    {
        var p = Big("Dibber Belly", "The Dibber's belly: the deck plate under the cockpit, the boarding hatch's shaft through it, the chamfer out to the shoulder, the fuel tank and the skirt the four lift jets hang in.", Grey2, "fueltank");
        double[] hatch = [-ShaftX, -0.1, ShaftZ0, ShaftX, Ft + 0.1, HatchZ1];
        Ring(Fixed(p), 0, Ft, 0, A(0), hatch);
        // The deck plate and the underside, both round the hatch.
        foreach (var poly in OctLess(LIn - 0.004, -ShaftX, ShaftZ0, ShaftX, HatchZ1))
            Floor(p, poly, Ft + 0.012, Deck, "rubber");
        foreach (var poly in OctLess(A(0) - 0.01, -ShaftX, ShaftZ0, ShaftX, HatchZ1))
            Floor(p, poly, -0.006, Dark, "dirty steel");
        // Hazard paint round the hatch on the deck, the shaft's lining.
        DoorParts.DeckHatchRim(p, -ShaftX, ShaftZ0, ShaftX, HatchZ1, Ft + 0.012, "+z", H); // (the deck hatch's coaming, walked over shut; the aft wall stands at its aft edge)
        foreach (var (x0, z0, x1, z1) in new[] { (-ShaftX, ShaftZ0, ShaftX, ShaftZ0 + 0.016), (-ShaftX + 0.003, ShaftZ0 + 0.019, -ShaftX + 0.019, HatchZ1 - 0.003), (ShaftX - 0.019, ShaftZ0 + 0.019, ShaftX - 0.003, HatchZ1 - 0.003) })
            Detail(p, x0, 0.01, z0, x1, Ft - 0.004, z1, Frame, "dirty steel");
        // The chamfer from the belly to the shoulder, all eight faces.
        for (var k = 0; k < 8; k++)
        {
            Facet(p, k, 0, 0.6, k % 2 == 0 ? Grey2 : Grey);
            FStrip(p, k, 0, 0.6, 0.08, 0.92, 0.5, 0.56, 0.026, Frame, "dirty steel");
        }
        // The skirt: an eight-sided tube round the jets, a ring at its lip, hazard bands.
        void Tube(double r0, double y0, double r1, double y1, string col, string fin)
        {
            for (var i = 0; i < 8; i++)
            {
                var st = i % 2 == 1 ? 0.004 : 0;
                double[] Q(double r, double y, int j) { var t = (22.5 + 45 * j) * Math.PI / 180; return [r / C225 * Math.Cos(t), y + st, SkirtZ + r / C225 * Math.Sin(t)]; }
                PlateS(p, [Q(r0, y0, i), Q(r0, y0, i + 1), Q(r1, y1, i + 1), Q(r1, y1, i)], col, fin);
            }
        }
        const double a = SkirtA;
        Tube(a + 0.08, -0.02, a + 0.04, -0.16, Grey2, "worn paint");
        Tube(a + 0.03, -0.17, a, -0.55, Dark, "dirty steel");
        Tube(a + 0.07, -0.42, a + 0.07, -0.5, Hazard, "hazard paint");
        Tube(a, -0.56, a + 0.06, -0.64, Frame, "soot");
        Sock(p, "main", 0, 0, SkirtZ, "-y");
        // The boarding shaft: the ladder's line on its fore face, the deck hatch by its port edge, the belly hatch on its fore face.
        Sock(p, "boarding", Shaft.LadderAt[0], Shaft.LadderAt[1], Shaft.LadderAt[2], Shaft.LadderDir);
        Sock(p, "deckHatch", -ShaftX, Ft + 0.012 - DoorParts.HatchThick / 2, (ShaftZ0 + HatchZ1) / 2, "+x");
        Sock(p, "bellyHatch", Shaft.HatchAt[0], Shaft.HatchAt[1], Shaft.HatchAt[2], Shaft.HatchDir);
        p.Set(("fuelTank", Kw(("capacity", BellyFuel))));
        var zMin = p.Boxes.Where(b => !Drawn(b)).Min(b => Convert.ToDouble(((List<object?>)b["min"]!)[2])); // (a port leaves the part's own fore face)
        p.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. new[] { -H, 0.15 - H, zMin }.Cast<object>()], "-z", "fuel"))));
        return p.Stats(2200, 7000, 45, 12.0);
    }

    private static Func<double, double, Part> Fixed(Part p) => (_, _) => p;

    /// <summary>The lower deck's shell in four quarters: the hidden ring, the skin from the shoulder to the floor between the decks, the
    /// lining, and the hole in the fore face the cab fills.</summary>
    private static Part[] LowerShell()
    {
        string[] names = ["Dibber Lower Starboard", "Dibber Lower Aft", "Dibber Lower Port", "Dibber Lower Fore"];
        string[] what = ["starboard", "aft", "port", "fore"];
        var q = names.Select((n, i) => Big(n, $"The {what[i]} quarter of the Dibber's lower deck: the shell round the cockpit, its cream skin and graphite panels, the lining inside, an RCS pad on its flat face over the cab's height and the jets' fuel in the void behind it.", Cream, "fueltank")).ToArray();
        double[] cab = [-CabX, Ft - 0.1, -3.0, CabX, CabY1, -1.0];
        Ring((x, z) => q[Quarter(x, z)], Ft, Mid0, LIn, A(Mid0) - 0.0076, cab);
        const double y0 = 0.6, y1 = Mid0;
        for (var k = 0; k < 8; k++)
        {
            var p = q[k / 2];
            if (k == 6)
            {
                // The fore face round the cab: a strip under it, one over it, and the two sides (the face is the plane z = -A(y)).
                double E(double y) => A(y) * Tan225;
                double[] F(double x, double y) => [x, y, -A(y)];
                PlateS(p, [F(-E(y0 + 0.0015), y0 + 0.0015), F(E(y0 + 0.0015), y0 + 0.0015), F(E(CabY0 - 0.003), CabY0 - 0.003), F(-E(CabY0 - 0.003), CabY0 - 0.003)], Cream);
                PlateS(p, [F(-E(CabY1 + 0.003), CabY1 + 0.003), F(E(CabY1 + 0.003), CabY1 + 0.003), F(E(y1 - 0.0015), y1 - 0.0015), F(-E(y1 - 0.0015), y1 - 0.0015)], Cream);
                foreach (var s in new[] { -1, 1 })
                    PlateS(p, [F(s * E(CabY0), CabY0), F(s * (CabX + 0.03), CabY0), F(s * (CabX + 0.03), CabY1), F(s * E(CabY1), CabY1)], Cream);
                Lining(p, 6, LIn, Ft, Mid0, ya: CabY1 + 0.004);
                double U(double x) => (x + LIn * Tan225) / (2 * LIn * Tan225);
                Lining(p, 6, LIn, Ft, Mid0, 0, U(-CabX - 0.004), yb: CabY1 + 0.002);
                Lining(p, 6, LIn, Ft, Mid0, U(CabX + 0.004), 1, yb: CabY1 + 0.002);
                Lining(p, 6, LIn, Ft, Mid0, U(-CabX + 0.004), U(CabX - 0.004), yb: CabSill - 0.004);
                continue;
            }
            var d = k % 2 == 1 ? Scratch() : p;
            Halves(k, (ua, ub) => Facet(d, k, y0, y1, Cream, "painted plating", ua, ub));
            Panels(d, k, y0, y1, 0.72, 1.62, 0);
            Panels(d, k, y0, y1, 1.7, k == 6 ? RcsYFore - RcsPadW / 2 - 0.06 : 2.76, 1);
            Halves(k, (ua, ub) => Lining(d, k, LIn, Ft, Mid0, ua, ub));
            if (d != p)
                Deal(d, q);
        }
        // A stencil (the craft's number) on each side face and the hatch side's hazard tab.
        foreach (var k in new[] { 0, 4 })
            for (var m = 0; m < 3; m++)
                FStrip(q[k / 2], k, y0, y1, 0.3 + m * 0.1, 0.36 + m * 0.1, 2.3, 2.45, 0.065, Dark);
        FStrip(q[1], 2, y0, y1, 0.15, 0.85, 0.66, 0.7, 0.03, Hazard, "hazard paint");
        // Inside: the cockpit's wall lamps, the switches, the suit lockers to port.
        var all = q.ToList();
        SockOn(all, "lampL", -LIn, 2.5, -0.6, "+x");
        SockOn(all, "lampR", LIn, 2.5, -0.6, "-x");
        SockOn(all, "lampAft", -0.6, 2.5, LIn, "-z");
        SockOn(all, "switchLights", LIn, 1.3, -0.88, "-x");
        SockOn(all, "switchHatch", 0.75, 1.3, LIn, "-z");
        SockOn(all, "suit0", -LIn, Ft + 0.125, -0.45, "+x");
        SockOn(all, "suit1", -LIn, Ft + 0.125, 0.45, "+x");
        // The RCS pads: square blocks standing out of each flat face level with the centre of mass, a jet out and one each way round.
        RcsPads(k => q[k / 2], PadY, RcsPadW / 2, "rcs", true);
        for (var i = 0; i < 4; i++)
        {
            // Its share of the fuel, and a Fuel out port on the RCS pad's face (the quarter's outermost face).
            double cx = Math.Round(Math.Cos(i * Math.PI / 2)), cz = Math.Round(Math.Sin(i * Math.PI / 2)), r = A(PadY(2 * i)) + 0.33;
            q[i].Set(("fuelTank", Kw(("capacity", ShareFuel))));
            q[i].Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx * r + cz * 0.18, PadY(2 * i) - 0.16, cz * r - cx * 0.18).Cast<object>()], (cx + cz > 0 ? "+" : "-") + (cx != 0 ? "x" : "z"), "fuel"))));
            q[i].Stats(800, 5000, 35, 10.0);
        }
        return q;
    }

    private static double PadY(int k) => k == 6 ? RcsYFore : RcsY;

    /// <summary>An RCS pad on each flat face at height y: a square block (half wide hw) standing out of the skin, a jet socket out of
    /// it (prefix + "Out") and, with sides, one each way round (prefix + "L", "R").</summary>
    private static void RcsPads(Func<int, Part> owner, Func<int, double> yOf, double hw, string prefix, bool sides)
    {
        for (var k = 0; k < 8; k += 2)
        {
            var p = owner(k);
            var y = yOf(k);
            var a = A(y);
            double cx = Math.Round(Math.Cos(k * Math.PI / 4)), cz = Math.Round(Math.Sin(k * Math.PI / 4));
            double r0 = a - 0.11, r1 = a + 0.33, w = hw;
            double X(double r, double t) => cx * r - cz * t;
            double Z(double r, double t) => cz * r + cx * t;
            Solid(p, X(r0, -w), y - w, Z(r0, -w), X(r1, w), y + w, Z(r1, w), Frame, "dirty steel");
            Detail(p, X(r1, -w + 0.04), y + w - 0.1, Z(r1, -w + 0.04), X(r1 + 0.012, w - 0.04), y + w - 0.04, Z(r1 + 0.012, w - 0.04), Hazard, "hazard paint");
            var dirs = new Dictionary<int, (string Out, string L, string R)> { [0] = ("+x", "-z", "+z"), [2] = ("+z", "+x", "-x"), [4] = ("-x", "+z", "-z"), [6] = ("-z", "-x", "+x") };
            Sock(p, (sides ? prefix : prefix + k) + "Out", X(r1, 0), y, Z(r1, 0), dirs[k].Out);
            if (!sides)
                continue;
            Sock(p, prefix + "L", X(a + 0.15, -w), y, Z(a + 0.15, -w), dirs[k].L);
            Sock(p, prefix + "R", X(a + 0.15, w), y, Z(a + 0.15, w), dirs[k].R);
        }
    }

    /// <summary>The upper deck's shell in four quarters: the ring, the skin to the roof's edge with the orange band, the RCS pads.</summary>
    private static Part[] UpperShell()
    {
        string[] names = ["Dibber Upper Starboard", "Dibber Upper Aft", "Dibber Upper Port", "Dibber Upper Fore"];
        string[] what = ["starboard", "aft", "port", "fore"];
        var q = names.Select((n, i) => Big(n, $"The {what[i]} quarter of the Dibber's upper deck: the shell round the systems deck and its orange band.")).ToArray();
        Ring((x, z) => q[Quarter(x, z)], Mid1, Roof0, UIn, A(Roof0) - 0.004);
        const double y0 = Mid0, y1 = Roof0;
        const double band0 = 3.3, band1 = 3.72;
        for (var k = 0; k < 8; k++)
        {
            var p = k % 2 == 1 ? Scratch() : q[k / 2];
            Halves(k, (ua, ub) => Facet(p, k, y0, y1, Cream, "painted plating", ua, ub));
            if (k % 2 == 0)
            {
                Panels(p, k, y0, y1, 2.95, band0 - 0.05, k);
                FStrip(p, k, y0, y1, 0.0, 1.0, band0, band1, 0.03, Orange, "worn paint");
                Panels(p, k, y0, y1, band1 + 0.06, y1 - 0.08, k + 1);
            }
            else
            {
                Panels(p, k, y0, y1, 2.95, y1 - 0.1, k);
                FStrip(p, k, y0, y1, 0.0, 0.2, band0, band1, 0.052, Orange, "worn paint");
                FStrip(p, k, y0, y1, 0.8, 1.0, band0, band1, 0.052, Orange, "worn paint");
            }
            Halves(k, (ua, ub) => Lining(p, k, UIn, Mid1, Roof0, ua, ub));
            if (k % 2 == 1)
                Deal(p, q);
        }
        // Amber beacons on the fore face over the band, the stern's tail lamps.
        FStrip(q[3], 6, y0, y1, 0.2, 0.3, 3.86, 3.98, 0.07, Amber, glow: true);
        FStrip(q[3], 6, y0, y1, 0.7, 0.8, 3.86, 3.98, 0.07, Amber, glow: true);
        FStrip(q[1], 2, y0, y1, 0.42, 0.58, 3.86, 3.98, 0.07, Red, glow: true);
        var all = q.ToList();
        SockOn(all, "lampL", -UIn, 4.8, 0.62, "+x");
        SockOn(all, "lampR", UIn, 4.8, -0.6, "-x");
        SockOn(all, "switchUpper", UIn, 4.3, 0.62, "-x");
        SockOn(all, "lifeSupport", -UIn, Mid1 + 0.125, 0, "+x");
        SockOn(all, "o2Tank", 0, Mid1 + 0.125, UIn, "-z");
        foreach (var x in q)
            x.Stats(650, 4500, 30, 9.0);
        return q;
    }

    /// <summary>The floor between the decks: the lower deck's ceiling under it, the upper deck plate on it, the ladder's hole.</summary>
    private static Part MidFloor()
    {
        var p = Big("Dibber Mid Deck", "The floor between the Dibber's decks: the cockpit's ceiling under it, the systems deck's plate on it and the hole the ladder climbs through.", Deck);
        double[] hole = [HoleX0, Mid0 - 0.1, HoleZ0, HoleX1, Mid1 + 0.1, HoleZ1];
        Ring(Fixed(p), Mid0, Mid1, 0, A(Mid1), hole);
        foreach (var poly in OctLess(LIn - 0.004, HoleX0, HoleZ0, HoleX1, HoleZ1))
            Floor(p, poly, Mid0 - 0.012, Ceil, "painted plating");
        foreach (var poly in OctLess(UIn - 0.004, HoleX0, HoleZ0, HoleX1, HoleZ1))
            Floor(p, poly, Mid1 + 0.012, Deck, "rubber");
        // The skin's edge between the bands: a dark rail round the hull.
        for (var k = 0; k < 8; k++)
            FStrip(p, k, Mid0 - 0.4, Mid0 + 0.4, 0.0, 1.0, Mid0 - 0.04, Mid0 + 0.04, 0.035 + (k % 2) * 0.004, Frame, "dirty steel");
        // Hazard round the hole, the hole's lining, a light strip under the ceiling.
        // (Karl, 2026-11-22: a flush Deck Hatch over the shaft, hung by its fore edge so it hangs clear of the climber, DoorParts.DeckHatch)
        DoorParts.DeckHatchRim(p, HoleX0, HoleZ0, HoleX1, HoleZ1, Mid1 + 0.012, "+x", H);
        Sock(p, "hatchUp", (HoleX0 + HoleX1) / 2, Mid1 + 0.012 - DoorParts.HatchThick / 2, HoleZ0, "+z");
        Detail(p, HoleX0, Mid0 + 0.005, HoleZ0, HoleX0 + 0.016, Mid1 - 0.005, HoleZ1, Frame, "dirty steel");
        Detail(p, -0.6, Mid0 - 0.04, -0.06, 0.6, Mid0 - 0.022, 0.06, "#fff0d0", glow: true);
        Sock(p, "plant", 0, Mid1, 0.3, "+y");
        Sock(p, "battery0", 1.05, Mid1, 1.0, "+y");
        Sock(p, "battery1", 0.7, Mid1, 1.15, "+y");
        foreach (var (n, x, z) in new[] { ("crate0", -0.9, -0.45), ("crate1", 0.85, -0.95) })
            Sock(p, n, x, Mid1, z, "+y");
        return p.Stats(900, 5000, 30, 10.0);
    }

    /// <summary>The roof: the slab over the upper deck, the chamfer in to the cap, the cap with its hatch box, antenna and beacon.</summary>
    private static Part Roof()
    {
        var p = Big("Dibber Roof", "The Dibber's roof: the ceiling over the systems deck, the chamfer in to the cap, a service box, the antenna and its beacon, four attitude jets round the cap and their fuel.", Cream, "fueltank");
        Ring(Fixed(p), Roof0, RoofMid, 0, A(RoofMid));
        Ring(Fixed(p), RoofMid, Top, 0, A(Top));
        Floor(p, Oct(UIn - 0.004), Roof0 - 0.012, Ceil, "painted plating");
        Detail(p, -0.06, Roof0 - 0.04, -0.8, 0.06, Roof0 - 0.022, 0.8, "#fff0d0", glow: true);
        for (var k = 0; k < 8; k++)
            Facet(p, k, Roof0, Top, k % 2 == 0 ? Cream2 : Grey);
        Floor(p, Oct(A(Top) - 0.012), Top - 0.004, Cream, "painted plating", thick: 0.03);
        // The service box: a cream block with a graphite lid and a vent, off the middle; the antenna to starboard fore, a red tip.
        Detail(p, -0.55, Top + 0.012, -0.15, 0.25, Top + 0.36, 0.62, Cream2, "painted plating");
        Detail(p, -0.5, Top + 0.36, -0.1, 0.2, Top + 0.42, 0.57, Grey, "worn paint");
        for (var i = 0; i < 4; i++)
            Detail(p, 0.252, Top + 0.08 + i * 0.06, -0.05, 0.27, Top + 0.11 + i * 0.06, 0.5, Dark, "soot");
        Detail(p, 0.45, Top + 0.012, -0.6, 0.65, Top + 0.1, -0.4, Frame, "dirty steel");
        Detail(p, 0.53, Top + 0.1, -0.52, 0.57, Top + 1.05, -0.48, Chrome, "steel");
        Detail(p, 0.52, Top + 1.05, -0.53, 0.58, Top + 1.13, -0.47, Red, glow: true);
        Detail(p, -0.9, Top + 0.012, -0.95, -0.7, Top + 0.12, -0.75, Amber, glow: true);
        // Four attitude jets standing on the cap, firing up: they tip the nose without pushing it sideways.
        foreach (var (n, x, z) in new[] { ("rcsTop0", 1.15, 0.0), ("rcsTop1", 0.0, 1.15), ("rcsTop2", -1.15, 0.0), ("rcsTop3", 0.0, -1.15) })
        {
            Detail(p, x - 0.2, Top + 0.012, z - 0.2, x + 0.2, Top + 0.026, z + 0.2, Hazard, "hazard paint");
            Sock(p, n, x, Top, z, "+y");
        }
        p.Set(("fuelTank", Kw(("capacity", ShareFuel))));
        p.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(-0.9, Top, 0.9).Cast<object>()], "+y", "fuel"))));
        return p.Stats(600, 4500, 30, 8.0);
    }

    /// <summary>The cab: a glass box out of the fore face, its glass raked back, a sill inside the cockpit can lean on.</summary>
    private static Part Cab()
    {
        var p = Big("Dibber Cab", "The Dibber's cab: a glass box standing out of its nose over the cockpit, the glass raked back, amber lamps under it.", Cream);
        // Hidden: the sill, the roof, the cheeks and the glass.
        Hidden(p, -CabX, Ft, CabZ0 + 0.05, CabX, CabSill, CabBack);
        Hidden(p, -CabX, CabY1 - 0.1, CabZ1, CabX, CabY1, CabBack);
        foreach (var s in new[] { -1, 1 })
            Hidden(p, s * (CabX - 0.08), CabSill, CabZ1, s * CabX, CabY1 - 0.1, CabBack);
        Hidden(p, -CabX + 0.08, CabSill, CabZ1 - 0.07, CabX - 0.08, CabY1 - 0.1, CabZ1);
        // Drawn: the sill block, the roof slab with a visor lip, the cheeks, the glass and its frame.
        double ZAt(double y) => CabZ0 + (CabZ1 - CabZ0) * (y - CabY0) / (CabY1 - CabY0);
        double[][] Sec(double x, double ya, double yb) => [[x, ya, ZAt(ya)], [x, yb, ZAt(yb)], [x, yb, CabBack - 0.25], [x, ya, CabBack - 0.25]];
        Prism(p, Sec(-CabX - 0.03, CabY0, CabSill + 0.03), Sec(CabX + 0.03, CabY0, CabSill + 0.03), Grey, "worn paint");
        Prism(p, Sec(-CabX - 0.03, CabY1 - 0.1, CabY1), Sec(CabX + 0.03, CabY1 - 0.1, CabY1), Cream, "painted plating");
        Prism(p, [[-CabX - 0.06, CabY1, ZAt(CabY1) - 0.12], [-CabX - 0.06, CabY1 + 0.06, ZAt(CabY1) - 0.1], [-CabX - 0.06, CabY1 + 0.06, CabBack - 0.3], [-CabX - 0.06, CabY1, CabBack - 0.3]],
            [[CabX + 0.06, CabY1, ZAt(CabY1) - 0.12], [CabX + 0.06, CabY1 + 0.06, ZAt(CabY1) - 0.1], [CabX + 0.06, CabY1 + 0.06, CabBack - 0.3], [CabX + 0.06, CabY1, CabBack - 0.3]], Grey2, "worn paint");
        foreach (var s in new[] { -1, 1 })
        {
            Prism(p, Sec(s * (CabX - 0.07), CabSill + 0.033, CabY1 - 0.103), Sec(s * (CabX + 0.03), CabSill + 0.033, CabY1 - 0.103), Cream, "painted plating");
            // A side window in each cheek.
            PlateS(p, [[s * (CabX + 0.034), 1.15, ZAt(1.15) + 0.12], [s * (CabX + 0.034), 1.95, ZAt(1.95) + 0.12], [s * (CabX + 0.034), 1.95, CabBack - 0.4], [s * (CabX + 0.034), 1.15, CabBack - 0.4]], Glass, glass: true, thick: 0.014);
        }
        // The raked glass in two panes, a mullion between, the frame round it.
        double gy0 = CabSill + 0.06, gy1 = CabY1 - 0.13;
        foreach (var (x0, x1) in new[] { (-CabX + 0.06, -0.025), (0.025, CabX - 0.06) })
            PlateS(p, [[x0, gy0, ZAt(gy0) - 0.035], [x1, gy0, ZAt(gy0) - 0.035], [x1, gy1, ZAt(gy1) - 0.035], [x0, gy1, ZAt(gy1) - 0.035]], Glass, glass: true, thick: 0.012);
        Prism(p, [[-0.025, gy0, ZAt(gy0) - 0.07], [0.025, gy0, ZAt(gy0) - 0.07], [0.025, gy0, ZAt(gy0)], [-0.025, gy0, ZAt(gy0)]],
            [[-0.025, gy1, ZAt(gy1) - 0.07], [0.025, gy1, ZAt(gy1) - 0.07], [0.025, gy1, ZAt(gy1)], [-0.025, gy1, ZAt(gy1)]], Dark, "dirty steel");
        // Amber lamps on the sill's face, hazard tabs at its corners.
        foreach (var x in new[] { -0.45, -0.3, 0.3, 0.45 })
            PlateS(p, [[x - 0.05, 0.78, ZAt(0.78) - 0.035], [x + 0.05, 0.78, ZAt(0.78) - 0.035], [x + 0.05, 0.82, ZAt(0.82) - 0.035], [x - 0.05, 0.82, ZAt(0.82) - 0.035]], Amber, glow: true, thick: 0.014);
        foreach (var s in new[] { -1, 1 })
            PlateS(p, [[s * 0.62, 0.78, ZAt(0.78) - 0.035], [s * 0.74, 0.78, ZAt(0.78) - 0.035], [s * 0.74, 0.83, ZAt(0.83) - 0.035], [s * 0.62, 0.83, ZAt(0.83) - 0.035]], Hazard, "hazard paint", thick: 0.014);
        // A dash on the sill inside.
        Detail(p, -CabX + 0.1, CabSill, CabBack - 0.55, CabX - 0.1, CabSill + 0.06, CabBack - 0.05, Console, "plastic");
        Detail(p, -0.4, CabSill + 0.06, CabBack - 0.45, 0.4, CabSill + 0.075, CabBack - 0.25, "#78c8e8", glow: true);
        return p.Stats(500, 3500, 20, 6.0);
    }

    /// <summary>A leg at the diagonal (sx, sz): a hip bracket on the belly's chamfer, the thigh out and up to a knee block, the shin down
    /// to the ankle over a broad pad, a ram from the hull to the knee. Fixed: the craft stands on the four pads.</summary>
    private static Part Leg(int sx, int sz)
    {
        var name = $"Dibber Leg {(sz < 0 ? "Fore" : "Aft")} {(sx < 0 ? "Port" : "Stbd")}";
        var p = Big(name, "One of the Dibber's four legs: a thigh out from a hip under the belly to a knee, a shin down to a broad pad, a ram to steady it, a trim jet under the thigh with its fuel in the knee. The craft stands on its pads.", Cream, "fueltank");
        double dx = sx / Sq2, dz = sz / Sq2, tx = -dz, tz = dx;
        double[] W(double r, double y, double t = 0) => [dx * r + tx * t, y, dz * r + tz * t];
        double[][] Sq(double r, double y, double pr, double py, double w, double h) =>
            [W(r - pr * h, y - py * h, -w), W(r - pr * h, y - py * h, w), W(r + pr * h, y + py * h, w), W(r + pr * h, y + py * h, -w)];
        void Strut(double r0, double y0, double r1, double y1, double w, string col, string fin)
        {
            double lr = r1 - r0, ly = y1 - y0, l = Math.Sqrt(lr * lr + ly * ly);
            double pr = -ly / l, py = lr / l;
            Prism(p, Sq(r0, y0, pr, py, w, w), Sq(r1, y1, pr, py, w, w), col, fin);
        }
        // The hip bracket on the chamfer, the thigh, the knee block, the shin, the ankle and the pad.
        Prism(p, Sq(HipR - 0.35, HipY + 0.02, 0, 1, 0.28, 0.22), Sq(HipR + 0.25, HipY + 0.02, 0, 1, 0.28, 0.22), Grey2, "worn paint");
        Strut(HipR, HipY, KneeR, KneeY, 0.2, Cream, "painted plating");
        Strut(HipR + 0.15, HipY + 0.16, KneeR - 0.15, KneeY + 0.01, 0.13, Cream2, "painted plating");
        Prism(p, Sq(KneeR - 0.28, KneeY, 0, 1, 0.27, 0.28), Sq(KneeR + 0.28, KneeY, 0, 1, 0.27, 0.28), Grey, "worn paint");
        Prism(p, Sq(KneeR - 0.14, KneeY, 0, 1, 0.285, 0.14), Sq(KneeR + 0.14, KneeY, 0, 1, 0.285, 0.14), Hazard, "hazard paint");
        Strut(KneeR + 0.05, KneeY - 0.15, AnkleR, AnkleY, 0.17, Cream, "painted plating");
        Strut(KneeR + 0.12, KneeY - 0.45, AnkleR - 0.08, AnkleY + 0.35, 0.185, Grey2, "worn paint");
        Prism(p, Sq(AnkleR - 0.22, AnkleY, 0, 1, 0.22, 0.18), Sq(AnkleR + 0.22, AnkleY, 0, 1, 0.22, 0.18), Grey, "worn paint");
        // The ram: a chrome rod in a dark cylinder from the hull's shoulder to the shin.
        double hr = A(1.15) + 0.05;
        Strut(hr, 1.15, KneeR - 0.05, KneeY + 0.2, 0.07, Dark, "dirty steel");
        Strut(KneeR - 0.4, KneeY + 0.33, KneeR + 0.2, KneeY - 0.35, 0.035, Chrome, "steel");
        Prism(p, Sq(hr - 0.06, 1.15, 0, 1, 0.12, 0.1), Sq(hr + 0.06, 1.15, 0, 1, 0.12, 0.1), Grey2, "worn paint");
        // The pad: a square foot, its rim hazard yellow, a dark sole.
        double px = dx * PadR, pz = dz * PadR;
        Detail(p, px - 0.4, Ground + 0.004, pz - 0.4, px + 0.4, Ground + 0.06, pz + 0.4, Dark, "rubber");
        Detail(p, px - 0.37, Ground + 0.064, pz - 0.37, px + 0.37, Ground + 0.12, pz + 0.37, Hazard, "hazard paint");
        Detail(p, px - 0.25, Ground + 0.124, pz - 0.25, px + 0.25, AnkleY - 0.12, pz + 0.25, Grey2, "worn paint");
        // Hidden: the hip under the belly (welded there), the pad (it stands on it), boxes down the shin.
        double hx = dx * 2.2, hz = dz * 2.2;
        Hidden(p, hx - 0.18, -0.3, hz - 0.18, hx + 0.18, 0, hz + 0.18);
        Hidden(p, px - 0.4, Ground, pz - 0.4, px + 0.4, Ground + 0.15, pz + 0.4);
        for (var i = 0; i < 3; i++)
        {
            var t = (i + 0.5) / 3;
            double r = KneeR + (AnkleR - KneeR) * t, y = KneeY + (AnkleY - KneeY) * t;
            Hidden(p, dx * r - 0.13, y - 0.3, dz * r - 0.13, dx * r + 0.13, y + 0.3, dz * r + 0.13);
        }
        Hidden(p, dx * KneeR - 0.2, KneeY - 0.2, dz * KneeR - 0.2, dx * KneeR + 0.2, KneeY + 0.2, dz * KneeR + 0.2);
        // The trim jet's pod under the thigh: a block the jet hangs from, far out from the middle so it levels the craft.
        double jx = dx * TrimR, jz = dz * TrimR;
        Hidden(p, jx - 0.16, TrimY, jz - 0.16, jx + 0.16, TrimY + 0.2, jz + 0.16);
        Prism(p, Sq(TrimR - 0.18, TrimY + 0.1, 0, 1, 0.18, 0.11), Sq(TrimR + 0.18, TrimY + 0.1, 0, 1, 0.18, 0.11), Grey2, "worn paint");
        Detail(p, jx - 0.14, TrimY + 0.004, jz - 0.14, jx + 0.14, TrimY + 0.02, jz + 0.14, Hazard, "hazard paint");
        Sock(p, "trim", jx, TrimY, jz, "-y");
        p.Set(("fuelTank", Kw(("capacity", ShareFuel))));
        p.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(dx * KneeR, KneeY + 0.2, dz * KneeR).Cast<object>()], "+y", "fuel"))));
        return p.Stats(380, 4000, 30, 6.0);
    }

    // ---- fittings

    private static Part LiftJet(string name, double force, double k, string desc)
    {
        var LiftForce = force;
        var p = new Part(name, Small, Section, desc, role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[] R(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * k * Math.Cos(an), 4), Py.Round(y * k, 4), Py.Round(r * k * Math.Sin(an), 4)];
        }
        void Pr(double[][] a, double[][] b, string col, string fin, bool glow = false)
        {
            var both = a.Concat(b).ToList();
            double[] mn = [.. Enumerable.Range(0, 3).Select(j => both.Min(q => q[j]))];
            double[] mx = [.. Enumerable.Range(0, 3).Select(j => both.Max(q => q[j]))];
            var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
            if (glow) shape["glow"] = true; else shape["finish"] = fin;
            p.Add(Box(mn, mx, col, detail: true, shape: shape));
        }
        Pr([.. Enumerable.Range(0, 8).Select(i => R(0.45, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.4, -0.45, i))], Grey2, "worn paint");
        Pr([.. Enumerable.Range(0, 8).Select(i => R(0.46, -0.32, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.46, -0.24, i))], Hazard, "hazard paint");
        Pr([.. Enumerable.Range(0, 8).Select(i => R(0.3, -0.45, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.4, -1.0, i))], "#2a2c2e", "soot");
        Pr([.. Enumerable.Range(0, 8).Select(i => R(0.2, -0.6, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.2, -0.62, i))], "#ffb060", "", glow: true);
        p.Add(Box([-0.32 * k, -0.45 * k, -0.32 * k], [0.32 * k, 0, 0.32 * k], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", force < 200000 ? 0.2 : 0.5), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-1.0 * k, 4), 0)), ("burnRate", Py.Round(LiftForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.32 * k, 4), Py.Round(-0.2 * k, 4), 0], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-1.0 * k, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.36 * k, 4)), ("length", Py.Round(2.4 * k, 4)), ("kind", "Rocket")))));
        return p.Stats(Math.Round(60 + force / 1000), 3000, 25, 5.0);
    }

    /// <summary>The power plant: a squat cell stack in a cage, its coolant risers. No fuel.</summary>
    private static Part PowerPlant()
    {
        var p = new Part("Dibber Power Plant", Small, Section, "The Dibber's power plant: a squat stack of cells in a cage on the systems deck, 150 kW for the lights, the air and the flight computer. No fuel.", role: "generator", tool: "welder", material: "steel", color: Grey2, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        B(-0.55, 0, -0.45, 0.55, 0.15, 0.45, Frame, "dirty steel");
        B(-0.48, 0.15, -0.38, 0.48, 1.25, 0.38, Cream2, "worn paint");
        for (var i = 0; i < 3; i++)
            B(-0.49, 0.35 + i * 0.3, -0.39, 0.49, 0.4 + i * 0.3, 0.39, i == 1 ? Orange : Grey, "worn paint", true);
        B(-0.3, 1.25, -0.2, 0.3, 1.33, 0.2, Grey, "worn paint", true);
        foreach (var (x, z) in new[] { (-0.52, -0.42), (0.52, -0.42), (-0.52, 0.42), (0.52, 0.42) })
            B(x - 0.03, 0.15, z - 0.03, x + 0.03, 1.4, z + 0.03, Frame, "painted steel", true);
        B(-0.2, 0.7, 0.381, 0.2, 1.0, 0.395, "#80d0ff", "glass", true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 150), ("spool", 5), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.08, 0.45)), ("face", "+z")))));
        return p.Stats(900, 2500, 20, 8.0);
    }

    /// <summary>The flight computer: a flight stabilizer in a console between the couches, a light gyro for a light craft.</summary>
    private static Part FlightComputer()
    {
        var p = new Part("Dibber Flight Computer", Small, Section, "The Dibber's flight computer: a flight stabilizer in a console between the couches, with a light craft's gyro. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: Console, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, PyDict shape, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: shape));
        B(-0.36, 0, -0.26, 0.36, 0.7, 0.26, Console, Kw(("finish", "plastic")));
        B(-0.3, 0.36, 0.26, 0.3, 0.64, 0.278, "#78c8e8", Kw(("glow", true)), true);
        for (var i = 0; i < 4; i++)
            B(-0.24 + i * 0.14, 0.22, 0.26, -0.18 + i * 0.14, 0.28, 0.278, i % 2 == 0 ? "#60e090" : Hazard, Kw(("glow", true)), true);
        B(-0.05, 0.701, -0.2, 0.05, 0.717, 0.1, Hazard, Kw(("finish", "hazard paint")), true);
        B(-0.12, 0.701, -0.2, 0.12, 0.717, -0.12, Hazard, Kw(("finish", "hazard paint")), true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 2000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z")); // (a light gyro for a light craft: its jets turn it)
        return p.Stats(200, 600, 8, 3.0);
    }

    private static Part Ladder(string name, double rise, double over, string desc, string sockName, double[] sockAt, string sockDir)
    {
        var p = new Part(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var top = rise + over;
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], Chrome, detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.6; y < top - 0.2; y += 1.2)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < rise + 0.1; y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.29, top - 0.06, -0.19], [0.29, top, -0.11], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock(sockName, sockAt, sockDir);
        p.Set(("ladder", Py.Round(rise, 3)));
        return p.Stats(60 + rise * 12, 400, 6, 3.0);
    }

    // ---- welding the pieces

    private static readonly Dictionary<string, string> SockOwner = [];

    /// <summary>A socket on whichever of the parts has a solid or hidden box face there (facing dir): throws when none does.</summary>
    private static void SockOn(List<Part> parts, string name, double x, double y, double z, string dir)
    {
        var ax = dir[1] - 'x';
        var plus = dir[0] == '+';
        double[] at = [x, y, z];
        foreach (var q in parts)
            foreach (var b in q.Boxes)
            {
                if (Drawn(b))
                    continue;
                double[] mn = Vd(b, "min"), mx = Vd(b, "max");
                if (Math.Abs((plus ? mx[ax] : mn[ax]) - at[ax]) > 0.005)
                    continue;
                if (Enumerable.Range(0, 3).All(a => a == ax || (at[a] >= mn[a] - 0.005 && at[a] <= mx[a] + 0.005)))
                {
                    Sock(q, name, x, y, z, dir);
                    SockOwner[name + "@" + (parts.Count > 1 ? (string)parts[0].D["name"]! : "")] = (string)q.D["name"]!;
                    return;
                }
            }
        throw new InvalidOperationException($"dibber_parts: no box face for socket {name} at ({x}, {y}, {z}) {dir}");
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair, on their biggest contact).</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var count = new int[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                (double Area, double[] At, int Axis, bool IPlus) best = (0, [], 0, false);
                foreach (var A in solid[i])
                    foreach (var B in solid[j])
                        for (var ax = 0; ax < 3; ax++)
                        {
                            bool iPlus = Math.Abs(A.Mx[ax] - B.Mn[ax]) < 0.001, iMinus = Math.Abs(A.Mn[ax] - B.Mx[ax]) < 0.001;
                            if (!iPlus && !iMinus)
                                continue;
                            var at = new double[3];
                            var area = 1.0;
                            var ok = true;
                            for (var a = 0; a < 3 && ok; a++)
                            {
                                if (a == ax)
                                {
                                    at[a] = iPlus ? A.Mx[a] : A.Mn[a];
                                    continue;
                                }
                                double lo = Math.Max(A.Mn[a], B.Mn[a]), hi = Math.Min(A.Mx[a], B.Mx[a]);
                                if (hi - lo < 0.03)
                                    ok = false;
                                at[a] = Math.Round((lo + hi) / 2, 2);
                                area *= hi - lo;
                            }
                            if (ok && area > best.Area)
                                best = (area, at, ax, iPlus);
                        }
                if (best.Area <= 0)
                    continue;
                var axis = "xyz"[best.Axis];
                Sock(parts[i], $"j{count[i]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "+" : "-") + axis);
                Sock(parts[j], $"j{count[j]++}", best.At[0], best.At[1], best.At[2], (best.IPlus ? "-" : "+") + axis);
            }
    }

    private static bool Meet(Part a, Part b)
    {
        foreach (var s in a.Sockets)
            foreach (var t in b.Sockets)
            {
                var (pa, pb) = ((List<object?>)s["at"]!, (List<object?>)t["at"]!);
                if (Enumerable.Range(0, 3).All(i => Math.Abs(Convert.ToDouble(pa[i]) - Convert.ToDouble(pb[i])) < 0.004))
                {
                    var (da, db) = ((string)s["dir"]!, (string)t["dir"]!);
                    if (da[1] == db[1] && da[0] != db[0] && ((string)s["name"]!).StartsWith('j'))
                        return true;
                }
            }
        return false;
    }

    private static List<string> PlaceOrder(List<Part> parts, string first)
    {
        var names = parts.Select(q => (string)q.D["name"]!).ToList();
        var seen = new HashSet<int> { names.IndexOf(first) };
        var order = new List<string> { first };
        var queue = new Queue<int>(seen);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            for (var j = 0; j < parts.Count; j++)
                if (!seen.Contains(j) && Meet(parts[i], parts[j]))
                {
                    seen.Add(j);
                    order.Add(names[j]);
                    queue.Enqueue(j);
                }
        }
        if (order.Count != parts.Count)
            throw new InvalidOperationException("dibber_parts: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    // ---- the blueprint

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static PyDict Blueprint(Dictionary<string, PyDict> defs, List<string> order, List<Part> lower, List<Part> upper)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "The farmers' vertical lander: a squat eight-sided capsule in cream pressed steel with an orange band, a glass cab low on its nose and four legs angled out to broad pads. It drops straight down from a ship in orbit to a field on four lift jets in the skirt under its belly and climbs back the same way. Below, the cockpit (two couches, the flight computer between them, suit lockers) and the belly hatch, which slides down to the ground with the ladder on it; a ladder up to the systems deck (power plant, batteries, life support, room for crates).", defs, maxSteps: 160);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var st = new Dictionary<string, int>();
        foreach (var name in order)
            st[name] = st.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int On(List<Part> shell, string sock) => st[(string)shell.First(q => q.Sockets.Any(s => (string)s["name"]! == sock)).D["name"]!];
        int belly = st["Dibber Belly"], mid = st["Dibber Mid Deck"];
        // Lift jets in the skirt.
        foreach (var leg in new[] { "Fore Port", "Fore Stbd", "Aft Port", "Aft Stbd" })
            bp.Hang(TrimJet, st["Dibber Leg " + leg], "trim", "top");
        bp.Hang(MainJet, belly, "main", "top");
        // RCS: on each pad a jet out and one each way round.
        foreach (var q in lower)
            foreach (var r in new[] { "rcsOut", "rcsL", "rcsR" })
                Paint(bp, bp.Hang("RCS Thruster", st[(string)q.D["name"]!], r, "base"), Frame, "dirty steel");
        for (var i = 0; i < 4; i++)
            Paint(bp, bp.Hang("RCS Thruster", st["Dibber Roof"], $"rcsTop{i}", "base"), Frame, "dirty steel");
        // The cockpit: two couches and the flight computer between them.
        var pilot = bp.Hang("Flight Couch", belly, "pilot", "bottom");
        Paint(bp, pilot, "#7a4a2a", "fabric");
        Paint(bp, bp.Hang("Flight Couch", belly, "copilot", "bottom"), "#7a4a2a", "fabric");
        var fc = bp.Hang("Dibber Flight Computer", belly, "computer", "bottom");
        var flight = ShipParts.ShipFlight();
        flight["mouseAim"] = false; // (a lander flies upright over its belly jet: the mouse doesn't swing its nose round)
        bp.Steps[fc]["config"] = flight;
        bp.Wires.Add((pilot, fc));
        // The suit lockers, the belly hatch with its ladder, the ladder up.
        foreach (var n in new[] { "suit0", "suit1" })
            bp.Hang(EvaParts.LockerName, On(lower, n), n, "wall");
        var hatch = BoardingShaft.Fit(bp, Shaft, belly, "boarding", belly, "deckHatch", belly, "bellyHatch"); // (both hatches shut)
        bp.HangTurned("Dibber Deck Ladder", belly, "ladderUp", "foot", BuildingParts.RY270); // (its back to the hole's inboard edge: off its top onto the deck inboard)
        bp.Hang(DoorParts.HatchName(0.9, 0.75), mid, "hatchUp", "hang"); // (shut: the systems deck walks over it)
        // The systems deck.
        bp.Hang("Dibber Power Plant", mid, "plant", "bottom");
        bp.Hang("Battery", mid, "battery0", "-y");
        bp.Hang("Battery", mid, "battery1", "-y");
        bp.Hang(EvaParts.LifeSupportName, On(upper, "lifeSupport"), "lifeSupport", "wall");
        bp.Hang(EvaParts.OxygenTankName, On(upper, "o2Tank"), "o2Tank", "wall");
        // Lamps and switches.
        int Lamp(List<Part> shell, string sock, string colour)
        {
            var s = bp.Hang("Bulkhead Light", On(shell, sock), sock, "back");
            bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
            return s;
        }
        int Switch(List<Part> shell, string sock, bool on, params int[] to)
        {
            var s = bp.Hang("Bakelite Switch", On(shell, sock), sock, "back");
            bp.Steps[s]["config"] = new PyDict { ["startOn"] = on };
            foreach (var t in to)
                bp.Wires.Add((s, t));
            return s;
        }
        Switch(lower, "switchLights", true, Lamp(lower, "lampL", "#ffd890"), Lamp(lower, "lampR", "#ffd890"), Lamp(lower, "lampAft", "#ffe0b0"));
        Switch(lower, "switchHatch", false, hatch);
        Switch(upper, "switchUpper", true, Lamp(upper, "lampL", "#e8f0ff"), Lamp(upper, "lampR", "#e8f0ff"));
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        SockOwner.Clear();
        var belly = Belly();
        var lower = LowerShell();
        var upper = UpperShell();
        var mid = MidFloor();
        var roof = Roof();
        var cab = Cab();
        var legs = new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }.Select(t => Leg(t.Item1, t.Item2)).ToList();
        // The cockpit's floor sockets, on the belly.
        Sock(belly, "pilot", -0.5, Ft, -1.0, "+y");
        Sock(belly, "copilot", 0.5, Ft, -1.0, "+y");
        Sock(belly, "computer", 0, Ft, -1.88, "+y");
        Sock(belly, "ladderUp", 1.02, Ft, 0, "+y");
        var structure = new List<Part> { belly };
        structure.AddRange(lower);
        structure.Add(mid);
        structure.AddRange(upper);
        structure.Add(roof);
        structure.Add(cab);
        structure.AddRange(legs);
        Connect(structure);
        var order = PlaceOrder(structure, "Dibber Belly");
        List<Part> parts =
        [
            .. structure,
            LiftJet(MainJet, MainForce, MainScale, $"The Dibber's belly jet: {MainForce / 1e3:0} kN straight down from the skirt under its belly, most of what lifts it off a field and sets it down; tilted, it pushes the craft along."),
            LiftJet(TrimJet, TrimForce, TrimScale, $"A trim jet: {TrimForce / 1e3:0} kN straight down. One under each of the Dibber's thighs, far out from its middle, keeps it level over its belly jet."),
            PowerPlant(), FlightComputer(), .. BoardingShaft.Parts(Shaft),
            Ladder("Dibber Deck Ladder", Mid1 - Ft, -DoorParts.HatchThick - 0.01, "The ladder from the Dibber's cockpit up through the floor, under a deck hatch, to the systems deck. F climbs it.", "foot", [0, 0, -0.1], "-y"),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"dibber_parts: {name} is longer than 32 characters");
            if (p.Boxes.Count > 256)
                throw new InvalidOperationException($"dibber_parts: {name} has {p.Boxes.Count} boxes (256 at most)");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/DibberLander.json", PyJson.Dumps(Blueprint(defs, order, [.. lower], [.. upper]), 2) + "\n");
    }
}
