using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Zero Day, the IT Department's torpedo frigate (Karl, 2026-11-15: "a state of the art faction torpedo frigate. Sleek lines, bigger
/// than the Halberd, countermeasures, torpedo bay on the side, rail gun, thruster the width of the ship ... make the levels run the length
/// of the ship instead. Try and make doors, features etc what you'd expect from a space ship"; the faction "renowned for their powerful,
/// state-of-the-art ships"). The IT Department is the tech faction, so it is theirs (docs/sandbox/constructs/torpedo-frigate.md).
///
/// Unlike the Halberd (a tower of storeys) it flies level: two decks the length of the ship inside a lofted, faceted hull (a chisel
/// nose, sides that flare out to a sharp beam line and lean in above it, a flat keel and a flat spine), armour slabs standing proud of a
/// pale skin in steps, chine rails along the beam. Torpedo sponsons on both flanks, a railgun along the spine, four point-defence
/// turrets on barbettes at the beam, flare launchers behind the bridge and one drive array across the whole stern.
///
/// Every big part has its origin at the body's; the generator works in ship metres (x across, port to starboard, the centreline at
/// Cx; y up from the lower deck's underside; z from the bow's tip back, so forward is -z) and moves them by half a cell. The hull is
/// lofted between stations (a half profile of five points mirrored): each big part draws the facets of its own band of the hull and the
/// hidden boxes that stand for the void between the skin and the rooms. A hull facet is drawn only; solid boxes of different parts
/// never overlap (the build refuses "something is in the way").
/// </summary>
public sealed class TorpedoFrigateParts : IPartGenerator
{
    public string Name => "torpedo_frigate_parts";

    private const string Out = "Constructs/Parts/ZeroDay";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Zero Day frigate";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint (the IT Department's, made for a warship): graphite armour over a pale case-grey skin, corporate blue panels, lanyard
    // yellow at doors and hatches, link-blue lights, failed-build red for warnings.
    private const string Skin = "#bdb9ad", Skin2 = "#a9a59a", Armour = "#3b4148", Armour2 = "#30353b", Blue = "#2c5aa0", Frame = "#1f2226",
        Hazard = "#e0b020", Glow = "#5ac8ff", Red = "#e0402c", Glass = "#6f9ec0",
        Wall = "#c4c8ca", WallLow = "#2c5aa0", WallTrim = "#2a2e33", Deck = "#4a4f55", Grate = "#3a3e43", Ceil = "#9aa1a6", Console = "#22262b",
        Screen = "#58c8e8", Amber = "#f0a830", PipeRed = "#a8402c", PipeBlue = "#2f6a9a", PipeYellow = "#c8a030", MedWhite = "#dfe4e2";

    // ---- the plan

    private const double Cx = 6.5; // the centreline
    private const double Ft0 = 0.25, Ceil0 = 3.25, Ft1 = 3.5, Ceil1 = 6.5, Roof = 6.75; // floor tops and ceilings of the two decks
    private const double Ground = -4.1; // where the legs stand
    private const double ZFront = 8.5, ZStep = 14.75, ZAft = 42.0; // the rooms' front, where they widen, their back
    private const double Lw = 4.5, LwF = 3.5, Uw = 3.5, UwF = 2.5; // the rooms' inner half widths: lower aft, lower fore, upper aft, the bridge
    private const double DoorH = 2.25;

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the spine's edge)

    private readonly record struct Sec(double Z, (double H, double Y)[] Pts);

    private static readonly (double, double)[] Mid = [(2.8, -1.6), (5.3, 0.3), (6.0, 3.3), (5.0, 6.3), (2.6, 7.4)];

    private static readonly Sec[] Hull =
    [
        new(Zt, [(0.2, 1.4), (0.45, 1.8), (0.65, 2.7), (0.5, 3.4), (0.25, 3.7)]),
        new(Zc, [(1.5, -0.5), (3.0, 0.45), (3.8, 2.95), (3.3, 5.5), (1.6, 6.2)]),
        new(8.0, [(2.3, -1.1), (4.4, 0.3), (5.2, 3.1), (4.4, 6.2), (2.4, 7.1)]),
        new(14.0, Mid), new(20.0, Mid), new(30.0, Mid), new(38.0, Mid),
        new(42.0, [(3.0, -1.7), (5.6, 0.2), (6.3, 3.3), (5.3, 6.4), (2.8, 7.5)]),
        new(44.0, [(3.0, -1.3), (6.0, 0.1), (6.6, 3.3), (5.7, 6.5), (3.1, 7.6)]),
        new(46.5, [(2.6, -0.5), (5.6, 0.5), (6.4, 3.3), (5.5, 6.3), (3.0, 7.3)]),
    ];

    private const double Stern = 46.5;
    private const double Zt = -3.5, Zc = 2.0; // the bow's tip and where the canopy starts

    /// <summary>The half profile at z (between stations, straight lines).</summary>
    private static (double H, double Y)[] At(double z)
    {
        if (z <= Hull[0].Z)
            return Hull[0].Pts;
        for (var i = 0; i < Hull.Length - 1; i++)
            if (z <= Hull[i + 1].Z)
            {
                var t = (z - Hull[i].Z) / (Hull[i + 1].Z - Hull[i].Z);
                return [.. Enumerable.Range(0, 5).Select(k => (Hull[i].Pts[k].H + (Hull[i + 1].Pts[k].H - Hull[i].Pts[k].H) * t, Hull[i].Pts[k].Y + (Hull[i + 1].Pts[k].Y - Hull[i].Pts[k].Y) * t))];
            }
        return Hull[^1].Pts;
    }

    /// <summary>The hull's half width at height y and z (0 outside its height).</summary>
    private static double SkinHalf(double z, double y)
    {
        var q = At(z);
        if (y < q[0].Y || y > q[4].Y)
            return 0;
        for (var i = 0; i < 4; i++)
            if (y <= q[i + 1].Y)
                return q[i].H + (q[i + 1].H - q[i].H) * (y - q[i].Y) / (q[i + 1].Y - q[i].Y);
        return q[4].H;
    }

    /// <summary>Throws when a point of the rooms isn't inside the skin (with room for the plates).</summary>
    private static void MustFit(string what, double z, double hx, double y)
    {
        var s = SkinHalf(z, y);
        if (s < hx + 0.06)
            throw new InvalidOperationException($"torpedo_frigate_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
    }

    /// <summary>A facet's two edge points at z (ship metres, unshifted). k: 0 the keel (port to starboard), 1..4 the side facets
    /// (P[k-1] to P[k]) on side s, 5 the spine (starboard to port).</summary>
    private static (double[] A, double[] B) Edge(int k, int s, double z)
    {
        var q = At(z);
        double[] V((double H, double Y) p, int side) => [Cx + side * p.H, p.Y, z];
        return k switch
        {
            0 => (V(q[0], -1), V(q[0], 1)),
            5 => (V(q[4], 1), V(q[4], -1)),
            _ => (V(q[k - 1], s), V(q[k], s)),
        };
    }

    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    private static double[] Sh(double[] q) => P(q[0], q[1], q[2]);

    /// <summary>A point on facet k (side s) between za and zb: u along z, v across from edge A to B.</summary>
    private static double[] OnFacet(int k, int s, double za, double zb, double u, double v)
    {
        var (a0, b0) = Edge(k, s, za);
        var (a1, b1) = Edge(k, s, zb);
        return Lerp(Lerp(a0, b0, v), Lerp(a1, b1, v), u);
    }

    /// <summary>The facet's outward normal at its middle (away from the hull's axis).</summary>
    private static double[] FacetNormal(int k, int s, double za, double zb)
    {
        var c = OnFacet(k, s, za, zb, 0.5, 0.5);
        var n = Unit(Cross(Sub(OnFacet(k, s, za, zb, 0.5, 1), OnFacet(k, s, za, zb, 0.5, 0)), Sub(OnFacet(k, s, za, zb, 1, 0.5), OnFacet(k, s, za, zb, 0, 0.5))));
        double[] axis = [Cx, 3.0, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>The skin of facet k on side s from za to zb (between two stations or inside one segment).</summary>
    /// The odd facets sit 6 mm aft of the even ones and the chine rails 3 mm, so where neighbours' plates cross at an edge their ends
    /// never share a plane (z-fighting.md); a facet's ends still meet the next band's same facet end to end.
    private static void Facet(Part p, int k, int s, double za, double zb, string color, string finish = "painted plating", bool glass = false)
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At2(double u, double v) { var q = OnFacet(k, s, za, zb, u, v); return Sh([q[0], q[1], q[2] + dz + (u < 0.5 ? FacetTrim : -FacetTrim)]); }
        Quad(p, At2(0, 0), At2(0, 1), At2(1, 1), At2(1, 0), color, finish, glass);
    }

    private const double FacetStagger = 0.006, FacetTrim = 0.0015; // (and each stops 1.5 mm short of its band's ends: a tapering band's plate leans past them)

    /// <summary>A bevelled armour slab on facet k from (u0, v0) to (u1, v1), standing d0..d1 out, its face drawn in by bev metres.</summary>
    private static void Slab(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d0, double d1, double bev, string color, string finish = "plated steel")
    {
        var n = FacetNormal(k, s, za, zb);
        var lu = zb - za;
        var lv = Math.Sqrt(Dot(Sub(OnFacet(k, s, za, zb, 0.5, 1), OnFacet(k, s, za, zb, 0.5, 0)), Sub(OnFacet(k, s, za, zb, 0.5, 1), OnFacet(k, s, za, zb, 0.5, 0))));
        double bu = bev / lu, bv = bev / Math.Max(lv, 0.1);
        double[] Off(double[] q, double d) => [q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d];
        double[][] inner = [.. new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) }.Select(t => Sh(Off(OnFacet(k, s, za, zb, t.Item1, t.Item2), d0)))];
        double[][] outer = [.. new[] { (u0 + bu, v0 + bv), (u1 - bu, v0 + bv), (u1 - bu, v1 - bv), (u0 + bu, v1 - bv) }.Select(t => Sh(Off(OnFacet(k, s, za, zb, t.Item1, t.Item2), d1)))];
        Prism(p, inner, outer, color, finish);
    }

    /// <summary>A flat strip on a facet (a seam, a stripe, a light), d out from it.</summary>
    private static void FStrip(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d, string color, string finish = "painted plating", bool glow = false)
    {
        var n = FacetNormal(k, s, za, zb);
        double[] Q(double u, double v)
        {
            var q = OnFacet(k, s, za, zb, u, v);
            return Sh([q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d]);
        }
        Plate(p, [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)], color, finish, glow: glow);
    }

    /// <summary>A chine rail: a faceted bar along profile point i (1..3) on side s from za to zb, w wide each way and h proud.</summary>
    private static void Chine(Part p, int i, int s, double za, double zb, double w, double h, string color) => Chine(p, i, s, za, zb, w, h, w, h, color);

    /// <summary>A shoulder block: a big chine rail whose ends rake down to a smaller section over rake metres.</summary>
    private static void Shoulder(Part p, int i, int s, double za, double zb, double w, double h, double rake, string color)
    {
        Chine(p, i, s, za, za + rake, w * 0.55, h * 0.3, w, h, color);
        Chine(p, i, s, za + rake + 0.004, zb - rake - 0.004, w, h, w, h, color == Armour ? Armour2 : Armour);
        Chine(p, i, s, zb - rake, zb, w, h, w * 0.55, h * 0.3, color);
    }

    private static void Chine(Part p, int i, int s, double za, double zb, double w, double h, double w2, double h2, string color)
    {
        double[][] Ring(double z, double w, double h)
        {
            var q = At(z);
            double[] c = [Cx + s * q[i].H, q[i].Y, z], a = [Cx + s * q[i - 1].H, q[i - 1].Y, z], b = i == 4 ? [Cx - s * q[4].H, q[4].Y, z] : [Cx + s * q[i + 1].H, q[i + 1].Y, z];
            var da = Unit(Sub(a, c));
            var db = Unit(Sub(b, c));
            var n = Unit([-(da[0] + db[0]), -(da[1] + db[1]), 0]);
            double[] Pt(double[] d1, double t1, double t2) => Sh([c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z]);
            return [Pt(da, w, 0.01), Pt(da, w * 0.45, h), Pt(db, w * 0.45, h), Pt(db, w, 0.01)];
        }
        var ra = Ring(za, w, h);
        var rb = Ring(zb, w2, h2);
        foreach (var q in ra)
            q[2] += FacetStagger / 2 + FacetTrim; // (between the even and the odd facets' ends)
        foreach (var q in rb)
            q[2] += FacetStagger / 2 - FacetTrim;
        Prism(p, ra, rb, color, "plated steel");
    }

    /// <summary>The armour on a facet band from za to zb: panels about 3 m long, each a slab (some stepped twice, some corporate blue),
    /// or bare skin with its seam; the pattern is the same on both sides (k and the panel's index pick it).</summary>
    private static void Plates(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.07, double v1 = 0.93)
    {
        var n = Math.Max(1, (int)Math.Round((zb - za) / 3.2));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.03 / (zb - za) * 4, u1 = (double)(i + 1) / n - 0.03 / (zb - za) * 4;
            var pick = (k * 7 + i * 3 + salt * 5 + (int)za) % 6;
            switch (pick)
            {
                case 0:
                case 3:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.13, 0.07, Armour);
                    break;
                case 1:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.12, 0.07, Armour2);
                    Slab(p, k, s, za, zb, u0 + (u1 - u0) * 0.18, u1 - (u1 - u0) * 0.12, v0 + 0.14, v1 - 0.2, 0.12, 0.2, 0.05, Armour);
                    break;
                case 2:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.09, 0.05, Blue, "painted plating");
                    break;
                case 4:
                    Slab(p, k, s, za, zb, u0, (u0 + u1) / 2 - 0.01, v0, v1, 0.02, 0.15, 0.08, Armour);
                    Slab(p, k, s, za, zb, (u0 + u1) / 2 + 0.01, u1, v0 + 0.1, v1, 0.02, 0.1, 0.06, Armour2);
                    break;
                default:
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.03, Frame, "dirty steel");
                    FStrip(p, k, s, za, zb, u0, u0 + Math.Min(0.25, (u1 - u0) * 0.2), v0 + 0.1, v0 + 0.22, 0.031, Blue, "painted plating");
                    break;
            }
        }
    }

    // ---- geometry helpers (the Halberd's)

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    private static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? [0, 1, 0] : [a[0] / l, a[1] / l, a[2] / l];
    }

    /// <summary>A flat plate through the points (a convex polygon), T thick about them: a "hull" shape, drawn only.</summary>
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

    /// <summary>A quad as one plate when flat, else as two triangles.</summary>
    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating", bool glass = false)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-10)
        {
            Plate(p, [a, c, d], color, finish, glass);
            return;
        }
        var flat = Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.08; // (a little twisted: one plate round all four, so no seam of two near-coplanar halves)
        if (flat)
            Plate(p, [a, b, c, d], color, finish, glass);
        else
        {
            Plate(p, [a, b, c], color, finish, glass);
            Plate(p, [a, c, d], color, finish, glass);
        }
    }

    /// <summary>A convex solid between two outlines (drawn only).</summary>
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
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

    private static double[][] Ngon(double cx, double y, double cz, double r, int n, double phase = 0) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = phase + Math.PI / n + i * 2 * Math.PI / n;
        return P(cx + r * Math.Cos(a), y, cz + r * Math.Sin(a));
    })];

    /// <summary>A ring of n points round an axis along z at (cx, cy), radius rx by ry, at z (unshifted part-local metres).</summary>
    private static double[][] RingZ(double cx, double cy, double rx, double ry, double z, int n) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = Math.PI / n + i * 2 * Math.PI / n;
        return new[] { Py.Round(cx + rx * Math.Cos(a), 4), Py.Round(cy + ry * Math.Sin(a), 4), z };
    })];

    /// <summary>Hidden boxes for the void between the skin and the rooms, both sides: x from xin (the rooms' outer face; 0: one box across the
    /// middle) out to the skin less a margin, y and z bands.</summary>
    private static void Void(Part p, double za, double zb, double ya, double yb, double xin)
    {
        var xo = Math.Floor((new[] { SkinHalf(za, ya), SkinHalf(za, yb), SkinHalf(zb, ya), SkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        za += 0.004; // (no face shared with the neighbouring band's boxes)
        zb -= 0.004;
        if (xin <= 0)
            Hidden(p, Cx - xo, ya, za, Cx + xo, yb, zb);
        else
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * xin, ya, za, Cx + s * xo, yb, zb);
    }

    // ---- rooms: floors, walls, doors

    private readonly record struct Rect(double X0, double Z0, double X1, double Z1);

    private static List<Rect> Minus(List<Rect> rs, Rect h)
    {
        var o = new List<Rect>();
        foreach (var r in rs)
        {
            if (h.X1 <= r.X0 || h.X0 >= r.X1 || h.Z1 <= r.Z0 || h.Z0 >= r.Z1)
            {
                o.Add(r);
                continue;
            }
            if (h.Z0 > r.Z0) o.Add(r with { Z1 = h.Z0 });
            if (h.Z1 < r.Z1) o.Add(r with { Z0 = h.Z1 });
            double za = Math.Max(r.Z0, h.Z0), zb = Math.Min(r.Z1, h.Z1);
            if (h.X0 > r.X0) o.Add(new Rect(r.X0, za, h.X0, zb));
            if (h.X1 < r.X1) o.Add(new Rect(h.X1, za, r.X1, zb));
        }
        return [.. o.Where(r => r.X1 - r.X0 > 0.02 && r.Z1 - r.Z0 > 0.02)];
    }

    private static void Floor(Part p, Rect[] plan, double y0, double y1, string color, string finish, params Rect[] holes)
    {
        var rs = plan.ToList();
        foreach (var h in holes)
            rs = Minus(rs, h);
        foreach (var r in rs)
            Solid(p, r.X0, y0, r.Z0, r.X1, y1, r.Z1, color, finish);
    }

    /// <summary>A gap in a wall: from A, W wide, open from sill to head over the floor (a doorway: sill 0).</summary>
    private readonly record struct Gap(double A, double W, double Sill, double Head, bool Glass = false);

    private static Gap Door(double a) => new(a, 1.5, 0, DoorH);

    /// <summary>A wall 0.25 m thick centred on c, along x (alongX) or z from a0 to a1, from the floor's top y0 to y1, with its gaps; a blue
    /// dado on both faces, dark jambs and a hazard head on each doorway, glass in a window.</summary>
    private static void WallRun(Part p, bool alongX, double a0, double a1, double c, double y0, double y1, params Gap[] gaps)
    {
        void Run(double a, double b, double ya, double yb, string col, string fin, double th, bool detail)
        {
            if (b - a < 0.02 || yb - ya < 0.02)
                return;
            if (alongX)
            {
                if (detail) Detail(p, a, ya, c - th, b, yb, c + th, col, fin);
                else Solid(p, a, ya, c - th, b, yb, c + th, col, fin);
            }
            else
            {
                if (detail) Detail(p, c - th, ya, a, c + th, yb, b, col, fin);
                else Solid(p, c - th, ya, a, c + th, yb, b, col, fin);
            }
        }
        var at = a0;
        foreach (var g in gaps.OrderBy(g => g.A))
        {
            Run(at, g.A, y0, y1, Wall, "painted plating", 0.125, false);
            Run(g.A, g.A + g.W, g.Head + y0, y1, Wall, "painted plating", 0.125, false);
            if (g.Sill > 0)
            {
                Run(g.A, g.A + g.W, y0, y0 + g.Sill, Wall, "painted plating", 0.125, false);
                if (g.Glass)
                {
                    Run(g.A, g.A + g.W, y0 + g.Sill, y0 + g.Head, Glass, "glass", 0.02, true);
                    if (alongX)
                        Hidden(p, g.A, y0 + g.Sill, c - 0.125, g.A + g.W, y0 + g.Head, c + 0.125);
                    else
                        Hidden(p, c - 0.125, y0 + g.Sill, g.A, c + 0.125, y0 + g.Head, g.A + g.W);
                }
                Run(g.A - 0.05, g.A + g.W + 0.05, y0 + g.Sill - 0.06, y0 + g.Sill, WallTrim, "painted steel", 0.14, true);
            }
            else
                Run(at + 0.05, g.A - 0.05, y0, y0 + 0.75, WallLow, "painted plating", 0.137, true);
            // The frame: dark jambs, a hazard head.
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", 0.145, true);
            at = g.A + g.W;
        }
        Run(at, a1, y0, y1, Wall, "painted plating", 0.125, false);
        Run(at + 0.05, a1 - 0.05, y0, y0 + 0.75, WallLow, "painted plating", 0.137, true);
    }

    /// <summary>The socket a door hangs by in a wall's doorway (from Gap a): on the wall's centre line at the doorway's start.</summary>
    private static void DoorSock(Part p, string name, bool alongX, double a, double c, double floor) =>
        Sock(p, name, alongX ? a : c, floor + 1.125, alongX ? c : a, alongX ? "+x" : "+z");

    /// <summary>A safety rail round a hatch on three sides, open at its back edge (+z), where the ladder lands people.</summary>
    private static void HatchRail(Part p, Rect h, double y)
    {
        Solid(p, h.X0 - 0.06, y, h.Z0 - 0.06, h.X1 + 0.06, y + 1.0, h.Z0, Frame, "painted steel");
        Solid(p, h.X0 - 0.06, y, h.Z0, h.X0, y + 1.0, h.Z1 - 0.3, Frame, "painted steel");
        Solid(p, h.X1, y, h.Z0, h.X1 + 0.06, y + 1.0, h.Z1 - 0.3, Frame, "painted steel");
        Detail(p, h.X0 - 0.075, y + 0.96, h.Z0 - 0.075, h.X1 + 0.075, y + 1.04, h.Z0 + 0.015, Hazard, "hazard paint");
        Detail(p, h.X0 - 0.12, y, h.Z1, h.X1 + 0.12, y + 0.012, h.Z1 + 0.12, Hazard, "hazard paint");
    }

    /// <summary>A light strip under a ceiling (link blue), and a lamp socket on a wall face.</summary>
    private static void CeilStrip(Part p, double x0, double x1, double z0, double z1, double ceil) =>
        Detail(p, x0, ceil - 0.025, z0, x1, ceil, z1, "#d8f0ff", glow: true);

    private static Part Big(string name, string desc, string role = "frame", string color = Skin) =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    // ---- the keel: the hull's underside, the fuel under the lower deck, the legs, the lift jets and the boarding shaft

    private static readonly Rect BellyHatch = new(Cx + 2.6, 30.6, Cx + 3.85, 31.85);

    private static Part Keel()
    {
        var p = Big("Zero Day Keel", "The Zero Day's keel: the flat underside, the fuel tankage under the lower deck, the legs' and lift jets' mounts and the boarding shaft.", "fueltank", Armour);
        double[] zs = [8.0, 14.0, 20.0, 30.0, 38.0, 42.0];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            // The keel's flat: plated, with the boarding shaft's hole cut where it passes.
            if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                var y = At(za)[0].Y;
                var hb = At(za)[0].H;
                var a = BellyHatch;
                Plate(p, [P(Cx - hb, y, za), P(Cx + hb, y, za), P(Cx + hb, y, a.Z0), P(Cx - hb, y, a.Z0)], Armour, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z1), P(Cx + hb, y, a.Z1), P(Cx + hb, y, zb), P(Cx - hb, y, zb)], Armour, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z0), P(a.X0, y, a.Z0), P(a.X0, y, a.Z1), P(Cx - hb, y, a.Z1)], Armour, "plated steel");
                Plate(p, [P(a.X1, y, a.Z0), P(Cx + hb, y, a.Z0), P(Cx + hb, y, a.Z1), P(a.X1, y, a.Z1)], Armour, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Skin2, "painted plating");
                if (za >= 20 && zb <= 30)
                    continue; // (the torpedo sponson covers the chine here)
                Plates(p, 1, s, za, zb, 1);
                Chine(p, 1, s, za == SpZ1 ? za + 0.005 : za, zb, 0.22, 0.1, Armour2); // (its end by the sponson kept off the beam chine's)
            }
            // Keel strakes: two dark ribs along the flat, a link-blue stripe between them.
            foreach (var s in new[] { -1, 1 })
                Detail(p, Cx + s * 1.6 - 0.09, At(za)[0].Y - 0.12, za + 0.05, Cx + s * 1.6 + 0.09, Math.Max(At(za)[0].Y, At(zb)[0].Y) - 0.002, zb - 0.05, Frame, "dirty steel");
        }
        // The fuel void: hidden boxes under the floor, round the shaft.
        var shaft = new Rect(BellyHatch.X0 - 0.05, BellyHatch.Z0 - 0.05, BellyHatch.X1 + 0.05, BellyHatch.Z1 + 0.05);
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            foreach (var (ya, yt) in new[] { (yb, -0.9), (-0.9, -0.35), (-0.35, 0.0) })
            {
                var xo = new[] { SkinHalf(za, ya), SkinHalf(za, yt), SkinHalf(zb, ya), SkinHalf(zb, yt) }.Min() - 0.06;
                if (xo < 0.3 || yt <= ya)
                    continue;
                foreach (var r in Minus([new Rect(Cx - xo, i == 0 ? za : za + 0.012, Cx + xo, i == zs.Length - 2 ? zb : zb - 0.012)], shaft)) // (inner ends kept off the decks' voids)
                    Hidden(p, r.X0, ya, r.Z0, r.X1, yt, r.Z1);
            }
        }
        // The shaft's lining: four plates down to the keel, hazard round its mouth.
        var hy = At(31)[0].Y;
        foreach (var (x0, z0, x1, z1, e) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0, 0.0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1, 0.0),
            (shaft.X0, shaft.Z0 + 0.025, shaft.X0, shaft.Z1 - 0.025, 0.005), (shaft.X1, shaft.Z0 + 0.025, shaft.X1, shaft.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + 0.01 + e, z0), P(x1, hy + 0.01 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel"); // (the sides between the ends, a little taller)
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z0 - 0.12, shaft.X1 + 0.12, hy - 0.004, shaft.Z0, Hazard, "hazard paint");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z1, shaft.X1 + 0.12, hy - 0.004, shaft.Z1 + 0.12, Hazard, "hazard paint");
        Detail(p, shaft.X0 + 0.1, hy - 0.03, shaft.Z0 - 0.4, shaft.X1 - 0.1, hy - 0.004, shaft.Z0 - 0.25, Red, glow: true);
        // Mounts for the parts that go on the keel.
        Sock(p, "bow", Cx, 0.0, 8.0, "-z");
                Sock(p, "lowerFore", Cx - 2.0, 0.0, 12.0, "+y");
        Sock(p, "lowerAft", Cx - 2.0, 0.0, 36.0, "+y");
                foreach (var (s, z, n) in new[] { (-1, 18.0, "legFL"), (1, 18.0, "legFR"), (-1, 34.0, "legBL"), (1, 34.0, "legBR") })
            Sock(p, n, Cx + s * 2.2, At(z)[0].Y + 0.02, z, "-y");
        foreach (var (s, z, n) in new[] { (-1, 14.0, "liftFL"), (1, 14.0, "liftFR"), (-1, 38.0, "liftBL"), (1, 38.0, "liftBR") })
            Sock(p, n, Cx + s * 1.4, At(z)[0].Y + 0.02, z, "-y");
        p.Set(("fuelTank", Kw(("capacity", 14000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx, -1.0, 42.0).Cast<object>()], "+z", "fuel"))));
        return p.Stats(26000, 60000, 45, 30.0);
    }

    // ---- the lower deck: the server room, the armoury, the torpedo room, the airlock, engineering

    private static readonly Rect AftHatch = new(Cx + 2.0, 39.5, Cx + 3.25, 40.75); // engineering up to the mess
    private static readonly Rect ForeHatch = new(Cx + 1.6, 15.6, Cx + 2.85, 16.85); // the armoury up to the ops room

    private static Part LowerFore()
    {
        var p = Big("Zero Day Lower Deck Fore", "The Zero Day's lower deck, forward: the ship's server room, the armoury and the torpedo room with the doors into both torpedo bays.");
        double y0 = Ft0, y1 = Ceil0;
        Floor(p, [new Rect(Cx - LwF - 0.25, 8.25, Cx + LwF + 0.25, ZStep), new Rect(Cx - Lw - 0.25, ZStep, Cx + Lw + 0.25, 25.0)], 0, Ft0, Deck, "painted deck plate");
        Detail(p, Cx - 3.0, Ft0, 20.6, Cx + 3.0, Ft0 + 0.012, 24.9, Grate, "painted grating"); // (the torpedo room's grating)
        // Outer walls: the server room's (narrow), the step out, then the armoury's and the torpedo room's with a bay door each side.
        WallRun(p, true, Cx - LwF - 0.25, Cx + LwF + 0.25, ZFront - 0.125, y0, y1);
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZFront - 0.25, ZStep - 0.25, Cx + s * (LwF + 0.125), y0, y1);
            WallRun(p, false, ZStep, 25.0, Cx + s * (Lw + 0.125), y0, y1, Door(21.6));
        }
        // The bulkhead between the server room and the armoury (it also closes the step), and the torpedo room's forward bulkhead.
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZStep - 0.125, y0, y1, Door(Cx - 0.75));
        WallRun(p, true, Cx - Lw, Cx + Lw, 20.0, y0, y1, Door(Cx - 0.75));
        // Ceiling light strips and frames.
        CeilStrip(p, Cx - 0.6, Cx + 0.6, 9.5, 13.5, y1);
        CeilStrip(p, Cx - 0.6, Cx + 0.6, 15.3, 19.6, y1);
        CeilStrip(p, Cx - 0.6, Cx + 0.6, 20.6, 24.9, y1);
        foreach (var z in new[] { 17.4, 22.5 })
            Detail(p, Cx - Lw + 0.1, y1 - 0.18, z - 0.07, Cx + Lw - 0.1, y1 - 0.012, z + 0.07, Frame, "dirty steel");
        // Pipes along the ceiling corners, labelled.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * (Lw - 0.18) - 0.05, y1 - 0.3, ZStep + 0.3, Cx + s * (Lw - 0.18) + 0.05, y1 - 0.2, 24.95, s < 0 ? PipeRed : PipeBlue, "painted steel");
            Detail(p, Cx + s * (Lw - 0.32) - 0.04, y1 - 0.28, ZStep + 0.3, Cx + s * (Lw - 0.32) + 0.04, y1 - 0.2, 24.95, PipeYellow, "painted steel");
        }
        // The server room's raised floor tiles and cable trays (state of the art, and labelled).
        Detail(p, Cx - 3.2, Ft0, 9.0, Cx + 3.2, Ft0 + 0.02, 10.6, "#5a6068", "rubber matting");
        Detail(p, Cx - 3.2, y1 - 0.4, 11.1, Cx + 3.2, y1 - 0.36, 11.6, Frame, "dirty steel");
        // Reload cradles in the torpedo room: two spare torpedoes on chocks down the middle.
        foreach (var s in new[] { -1, 1 })
        {
            var x = Cx + s * 0.9;
            foreach (var z in new[] { 21.4, 24.0 })
            {
                Solid(p, x - 0.35, Ft0, z - 0.2, x + 0.35, Ft0 + 0.35, z + 0.2, Frame, "painted steel");
                Detail(p, x - 0.36, Ft0 + 0.25, z - 0.21, x + 0.36, Ft0 + 0.3, z + 0.21, Hazard, "hazard paint");
            }
            Prism(p, RingZ(x - H, Ft0 + 0.62 - H, 0.26, 0.26, 20.9 - H, 10), RingZ(x - H, Ft0 + 0.62 - H, 0.26, 0.26, 24.9 - H, 10), "#6f7880", "painted steel");
            Prism(p, RingZ(x - H, Ft0 + 0.62 - H, 0.265, 0.265, 22.2 - H, 10), RingZ(x - H, Ft0 + 0.62 - H, 0.265, 0.265, 22.5 - H, 10), Blue, "painted plating");
            Prism(p, RingZ(x - H, Ft0 + 0.62 - H, 0.26, 0.26, 20.9 - H, 10), RingZ(x - H, Ft0 + 0.62 - H, 0.06, 0.06, 20.5 - H, 10), Red, "gloss paint");
        }
        // The hull band: the lower side facets (the sponsons cover them from z 20), their armour and the beam's chine rail.
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (8.0, 14.0), (14.0, 20.0) })
            {
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                Plates(p, 2, s, za, zb, 2);
                Chine(p, 2, s, za, zb, 0.3, 0.14, Armour2);
            }
        // The void between the skin and the rooms.
        foreach (var (za, zb, xin) in new[] { (8.0, 14.0, LwF + 0.25), (14.0, 20.0, Lw + 0.25) })
        {
            Void(p, za, zb, 0.0, 0.7, xin);
            Void(p, za, zb, 0.7, 3.25, xin);
        }
        Sock(p, "keel", Cx - 2.0, 0.0, 12.0, "-y");
        Sock(p, "upper", Cx - 2.0, Ceil0, 12.0, "+y");
        Sock(p, "sponsonL", Cx - Lw - 0.25, 2.95, 24.5, "-x");
        Sock(p, "sponsonR", Cx + Lw + 0.25, 2.95, 24.5, "+x");
        // Fittings: the server room.
        for (var i = 0; i < 4; i++)
        {
            Sock(p, $"serverL{i}", Cx - 2.8 + i * 0.62 - 0.125, Ft0, 12.0 - 0.125, "+y");
            Sock(p, $"serverR{i}", Cx + 0.94 + i * 0.62 - 0.125, Ft0, 12.0 - 0.125, "+y");
        }
        Sock(p, "computer", Cx - 1.6 - 0.25, Ft0, 9.6, "+y");
        Sock(p, "plant", Cx + LwF - 0.4, Ft0, ZFront + 0.4, "+y");
        Sock(p, "crate0", Cx + 2.6, Ft0, 13.9, "+y");
        Sock(p, "crate1", Cx + 3.05, Ft0, 13.9, "+y");
        // The armoury: lockers down the port wall, a tool rack and ammunition starboard.
        for (var i = 0; i < 3; i++)
            Sock(p, $"locker{i}", Cx - Lw, Ft0 + 0.125, 15.6 + i * 0.7, "+x");
        Sock(p, "toolRack", Cx + Lw, Ft0 + 0.9, 18.6, "-x");
        Sock(p, "ammo0", Cx - 1.2, Ft0, 19.4, "+y");
        Sock(p, "ammo1", Cx - 0.6, Ft0, 19.4, "+y");
        Sock(p, "ladder", ForeHatch.X0 + 0.625, Ft0, ForeHatch.Z1 - 0.095, "+y");
        // The torpedo room: a fire button by each bay door, the doors, crates of warheads.
        DoorSock(p, "doorServer", true, Cx - 0.75, ZStep - 0.125, Ft0);
        DoorSock(p, "doorTorpedo", true, Cx - 0.75, 20.0, Ft0);
        DoorSock(p, "doorBayL", false, 21.6, Cx - Lw - 0.125, Ft0);
        DoorSock(p, "doorBayR", false, 21.6, Cx + Lw + 0.125, Ft0);
        Sock(p, "fireL", Cx - Lw, Ft0 + 1.35, 23.6, "+x");
        Sock(p, "fireR", Cx + Lw, Ft0 + 1.35, 23.6, "-x");
        Sock(p, "warheads0", Cx - 3.6, Ft0, 24.6, "+y");
        Sock(p, "warheads1", Cx + 3.6, Ft0, 24.6, "+y");
        // Lamps and their switches.
        Sock(p, "lampServer", Cx - LwF, Ft0 + 2.45, 11.0, "+x");
        Sock(p, "lampArmouryL", Cx - Lw, Ft0 + 2.45, 18.0, "+x");
        Sock(p, "lampArmouryR", Cx + Lw, Ft0 + 2.45, 16.0, "-x");
        Sock(p, "lampTorpL", Cx - Lw, Ft0 + 2.45, 20.9, "+x");
        Sock(p, "lampTorpR", Cx + Lw, Ft0 + 2.45, 20.9, "-x");
        Sock(p, "switchServer", Cx + 1.0, Ft0 + 1.3, ZStep - 0.25, "-z");
        Sock(p, "switchFore", Cx + 1.0, Ft0 + 1.3, 19.875, "-z");
        Sock(p, "switchTorp", Cx + 1.0, Ft0 + 1.3, 20.125, "+z");
        foreach (var (what, hx, z) in new[] { ("server room", LwF, 9.0), ("armoury", Lw, 15.0), ("torpedo room", Lw, 24.9) })
            foreach (var y in new[] { 0.0, Ceil0 + 0.25 })
                MustFit(what, z, hx + 0.25, y);
        return p.Stats(18000, 40000, 30, 25.0);
    }

    private static Part LowerAft()
    {
        var p = Big("Zero Day Lower Deck Aft", "The Zero Day's lower deck, aft: the rest of the torpedo room, the airlock over the boarding shaft, the suit bay and engineering.");
        double y0 = Ft0, y1 = Ceil0;
        Floor(p, [new Rect(Cx - Lw - 0.25, 25.0, Cx + Lw + 0.25, ZAft + 0.25)], 0, Ft0, Deck, "painted deck plate", BellyHatch);
        Detail(p, Cx - 3.0, Ft0, 25.1, Cx + 3.0, Ft0 + 0.012, 29.9, Grate, "painted grating");
        Detail(p, Cx - Lw + 0.1, Ft0, 34.4, Cx + Lw - 0.1, Ft0 + 0.012, ZAft - 0.1, Grate, "painted grating"); // (engineering's grating)
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, 25.0, ZAft, Cx + s * (Lw + 0.125), y0, y1, s > 0 ? [Door(26.4)] : [Door(26.4)]);
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZAft + 0.125, y0, y1);
        // The airlock: a chamber to starboard over the boarding shaft, its inner door off the passage; the suit bay to port.
        WallRun(p, true, Cx + 1.75, Cx + Lw, 30.25, y0, y1);
        WallRun(p, false, 30.375, 34.0, Cx + 1.875, y0, y1, Door(32.25));
        HatchRail(p, BellyHatch, Ft0);
        // The bulkhead into engineering.
        WallRun(p, true, Cx - Lw, Cx + Lw, 34.125, y0, y1, Door(Cx - 0.75));
        // Engineering: the drive's feed trunks into the aft wall, a catwalk-grey ceiling of frames.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 3.6 - 0.18, Ft0, ZAft - 0.42, Cx + s * 3.6 + 0.18, y1 - 0.02, ZAft - 0.06, "#c9c2a8", "lagging");
            Detail(p, Cx + s * 3.6 - 0.19, Ft0 + 0.7, ZAft - 0.43, Cx + s * 3.6 + 0.19, Ft0 + 0.82, ZAft - 0.05, PipeRed, "painted steel");
            Detail(p, Cx + s * (Lw - 0.18) - 0.05, y1 - 0.3, 25.05, Cx + s * (Lw - 0.18) + 0.05, y1 - 0.2, ZAft - 0.5, s < 0 ? PipeRed : PipeBlue, "painted steel");
        }
        foreach (var z in new[] { 36.5, 39.0 })
            Detail(p, Cx - Lw + 0.1, y1 - 0.2, z - 0.07, Cx + Lw - 0.1, y1 - 0.012, z + 0.07, Frame, "dirty steel");
        CeilStrip(p, Cx - 0.6, Cx + 0.6, 25.1, 29.9, y1);
        CeilStrip(p, Cx - 0.5, Cx + 0.5, 30.6, 33.8, y1);
        CeilStrip(p, Cx - 3.0, Cx - 1.6, 35.0, 41.0, y1);
        CeilStrip(p, Cx + 3.4, Cx + 4.2, 35.0, 41.0, y1);
        // The airlock's cycle light over its inner door, red.
        Detail(p, Cx + 1.995, Ft0 + DoorH + 0.15, 32.9, Cx + 2.01, Ft0 + DoorH + 0.3, 33.2, Red, glow: true);
        Detail(p, Cx + 1.74, Ft0 + DoorH + 0.15, 32.9, Cx + 1.755, Ft0 + DoorH + 0.3, 33.2, "#40e070", glow: true);
        // The hull band and the void.
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (30.0, 38.0), (38.0, ZAft) })
            {
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                Plates(p, 2, s, za, zb, 3);
                Chine(p, 2, s, za, zb, 0.3, 0.14, Armour2);
            }
        foreach (var (za, zb) in new[] { (30.0, 38.0), (38.0, ZAft) })
        {
            Void(p, za, zb, 0.0, 0.7, Lw + 0.25);
            Void(p, za, zb, 0.7, 3.25, Lw + 0.25);
        }
        Sock(p, "keel", Cx - 2.0, 0.0, 36.0, "-y");
        Sock(p, "stern", Cx, 1.5, ZAft + 0.25, "+z");
        Sock(p, "upper", Cx - 2.0, Ceil0, 36.0, "+y");
        DoorSock(p, "doorBayL", false, 26.4, Cx - Lw - 0.125, Ft0);
        DoorSock(p, "doorBayR", false, 26.4, Cx + Lw + 0.125, Ft0);
        DoorSock(p, "doorAirlock", false, 32.25, Cx + 1.875, Ft0);
        DoorSock(p, "doorEngine", true, Cx - 0.75, 34.125, Ft0);
        Sock(p, "boarding", BellyHatch.X0 + 0.625, Ft0 - 0.125, BellyHatch.Z1, "-z");
        // Fittings: suit lockers in the bay, more torpedo crates, engineering's reactor, boards, batteries, pipes and the ladder up.
        for (var i = 0; i < 4; i++)
            Sock(p, $"suit{i}", Cx - Lw, Ft0 + 0.125, 30.6 + i * 0.7, "+x");
        Sock(p, "airlockLocker", Cx + Lw, Ft0 + 0.125, 33.3, "-x");
        Sock(p, "reactor", Cx + 2.4, Ft0, 37.6, "+y");
        Sock(p, "board", Cx + Lw, Ft0 + 0.9, 40.6, "-x");
        Sock(p, "panel", Cx - Lw, Ft0 + 0.6, 37.0, "+x");
        Sock(p, "battery0", Cx - 3.9, Ft0, 39.0, "+y");
        Sock(p, "battery1", Cx - 3.9, Ft0, 39.5, "+y");
        Sock(p, "battery2", Cx - 3.9, Ft0, 40.0, "+y");
        Sock(p, "pipes", Cx - 2.2, Ft0 + 2.75, ZAft, "-z");
        Sock(p, "toolRack", Cx - Lw, Ft0 + 0.9, 35.4, "+x");
        Sock(p, "ladder", AftHatch.X0 + 0.625, Ft0, AftHatch.Z1 - 0.095, "+y");
        Sock(p, "crate0", Cx + 3.6, Ft0, 29.5, "+y");
        Sock(p, "crate1", Cx - 3.6, Ft0, 29.5, "+y");
        Sock(p, "lampTorpL", Cx - Lw, Ft0 + 2.45, 28.5, "+x");
        Sock(p, "lampTorpR", Cx + Lw, Ft0 + 2.45, 28.5, "-x");
        Sock(p, "lampSuit", Cx - Lw, Ft0 + 2.45, 33.6, "+x");
        Sock(p, "lampAirlock", Cx + Lw, Ft0 + 2.45, 31.6, "-x");
        Sock(p, "lampEngL", Cx - Lw, Ft0 + 2.45, 38.2, "+x");
        Sock(p, "lampEngR", Cx + Lw, Ft0 + 2.45, 36.0, "-x");
        Sock(p, "switchAirlock", Cx + 1.75, Ft0 + 1.3, 31.6, "-x");
        Sock(p, "switchEng", Cx + 1.0, Ft0 + 1.3, 34.25, "+z");
        Sock(p, "switchTorp", Cx + 2.5, Ft0 + 1.3, 30.125, "-z");
        foreach (var (what, z) in new[] { ("torpedo room", 25.0), ("engineering", ZAft + 0.25) })
            foreach (var y in new[] { 0.0, Ceil0 + 0.25 })
                MustFit(what, z, Lw + 0.25, y);
        return p.Stats(18000, 40000, 30, 25.0);
    }

    // ---- the upper deck: the bridge, ops (fire control), the cabins, the head, the med bay, the mess

    private static Part UpperFore()
    {
        var p = Big("Zero Day Upper Deck Fore", "The Zero Day's upper deck, forward: the bridge under the canopy, the ops room with the forward gunnery station and the flare controls, and the cabins.");
        double y0 = Ft1, y1 = Ceil1;
        // The floor (the lower deck's ceiling) and this deck's ceiling.
        Floor(p, [new Rect(Cx - LwF - 0.25, 8.25, Cx + LwF + 0.25, ZStep), new Rect(Cx - Lw - 0.25, ZStep, Cx + Lw + 0.25, 25.0)], Ceil0, Ft1, Deck, "painted deck plate", ForeHatch);
        Floor(p, [new Rect(Cx - UwF - 0.25, 8.25, Cx + UwF + 0.25, ZStep), new Rect(Cx - Uw - 0.25, ZStep, Cx + Uw + 0.25, 25.0)], Ceil1, Roof, Ceil, "painted plating");
        Detail(p, Cx - UwF + 0.05, Ft1, ZFront + 0.05, Cx + UwF - 0.05, Ft1 + 0.012, ZStep - 0.3, "#30353b", "rubber matting"); // (the bridge's mat)
        HatchRail(p, ForeHatch, Ft1);
        // The bridge: narrow, a wide window forward onto the canopy, the dashboard under it.
        WallRun(p, true, Cx - UwF - 0.25, Cx + UwF + 0.25, ZFront - 0.125, y0, y1, new Gap(Cx - 2.1, 4.2, 0.95, 2.65, true));
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZFront - 0.25, ZStep - 0.25, Cx + s * (UwF + 0.125), y0, y1, new Gap(9.4, 2.6, 1.0, 2.4, true));
            WallRun(p, false, ZStep, 25.0, Cx + s * (Uw + 0.125), y0, y1);
        }
        // The dashboard: a sloped console across the window's foot, screens on it, a strip of lights.
        Solid(p, Cx - UwF + 0.05, Ft1, ZFront, Cx + UwF - 0.05, Ft1 + 0.85, ZFront + 0.55, Console, "plastic");
        Plate(p, [P(Cx - UwF + 0.06, Ft1 + 0.85, ZFront + 0.55), P(Cx + UwF - 0.06, Ft1 + 0.85, ZFront + 0.55), P(Cx + UwF - 0.06, Ft1 + 1.05, ZFront + 0.12), P(Cx - UwF + 0.06, Ft1 + 1.05, ZFront + 0.12)], Console, "plastic");
        foreach (var x in new[] { -1.7, -0.9, 0.9, 1.7 })
            Plate(p, [P(Cx + x - 0.32, Ft1 + 0.875, ZFront + 0.5), P(Cx + x + 0.32, Ft1 + 0.875, ZFront + 0.5), P(Cx + x + 0.32, Ft1 + 1.04, ZFront + 0.16), P(Cx + x - 0.32, Ft1 + 1.04, ZFront + 0.16)], Screen, glow: true, thick: 0.015);
        Detail(p, Cx - 0.3, Ft1 + 0.86, ZFront + 0.3, Cx + 0.3, Ft1 + 0.88, ZFront + 0.45, Amber, glow: true);
        Detail(p, Cx - UwF + 0.1, Ft1 + 0.6, ZFront + 0.551, Cx + UwF - 0.1, Ft1 + 0.63, ZFront + 0.56, Glow, glow: true);
        // The bulkhead between the bridge and ops (closing the step), and ops' aft bulkhead onto the corridor.
        WallRun(p, true, Cx - Uw - 0.25, Cx + Uw + 0.25, ZStep - 0.125, y0, y1, Door(Cx - 0.75));
        WallRun(p, true, Cx - Uw, Cx + Uw, 20.0, y0, y1, Door(Cx - 0.75));
        // The corridor's partitions, and the cabins either side: port two crew cabins, starboard the captain's cabin; the med bay aft.
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, 20.125, 25.0, Cx + s * 1.0, y0, y1, Door(22.9));
        WallRun(p, true, Cx - Uw, Cx - 1.125, 24.875, y0, y1);
        WallRun(p, true, Cx + 1.125, Cx + Uw, 24.875, y0, y1);
        // Ops: a plotting desk of screens against the port wall, a status board over the bridge door.
        Solid(p, Cx - Uw, Ft1, 17.6, Cx - Uw + 0.6, Ft1 + 0.8, 19.6, Console, "plastic");
        Detail(p, Cx - Uw + 0.04, Ft1 + 1.0, 17.7, Cx - Uw + 0.06, Ft1 + 1.9, 19.5, Screen, glow: true);
        Detail(p, Cx - 1.6, Ft1 + 2.35, ZStep + 0.004, Cx + 1.6, Ft1 + 2.85, ZStep + 0.02, "#1a3a60", glow: true);
        foreach (var i in Enumerable.Range(0, 6))
            Detail(p, Cx - 1.45 + i * 0.5, Ft1 + 2.45, ZStep + 0.02, Cx - 1.15 + i * 0.5, Ft1 + 2.55, ZStep + 0.035, i % 3 == 2 ? Amber : "#40e070", glow: true);
        CeilStrip(p, Cx - 0.4, Cx + 0.4, 9.6, 13.8, y1);
        CeilStrip(p, Cx - 2.2, Cx + 2.2, 17.2, 17.4, y1);
        CeilStrip(p, Cx - 0.25, Cx + 0.25, 20.4, 24.9, y1);
        // The hull band: the upper side and the upper chine, their armour, a chine rail along the upper chine.
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (8.0, 14.0), (14.0, 20.0), (20.0, 30.0) })
            {
                Facet(p, 3, s, za, zb, Skin, "painted plating");
                Facet(p, 4, s, za, zb, Skin2, "painted plating");
                Plates(p, 3, s, za, zb, 4, 0.12, 0.9);
                Plates(p, 4, s, za, zb, 5);
                Chine(p, 3, s, za, zb, 0.25, 0.12, Armour);
                // A link-blue light strip under the beam's rail, the IT Department's mark.
                FStrip(p, 3, s, za, zb, 0.03, 0.97, 0.04, 0.065, 0.025, Glow, glow: true);
            }
        // Shoulder blocks on the upper chine either side of the bridge, and a lower one on the beam under them.
        foreach (var s in new[] { -1, 1 })
        {
            Shoulder(p, 3, s, 8.4, 13.6, 0.75, 0.42, 1.4, Armour);
            Shoulder(p, 2, s, 9.0, 13.0, 0.55, 0.3, 1.0, Blue);
        }
        // The void: beside the floor slab, beside the walls, beside the ceiling.
        foreach (var (za, zb, xl, xu) in new[] { (8.0, 14.0, LwF + 0.25, UwF + 0.25), (14.0, 20.0, Lw + 0.25, Uw + 0.25), (20.0, 30.0, Lw + 0.25, Uw + 0.25) })
        {
            Void(p, za, Math.Min(zb, 25.0), 3.25, 3.5, xl);
            Void(p, za, Math.Min(zb, 25.0), 3.5, 5.2, xu);
            Void(p, za, Math.Min(zb, 25.0), 5.2, 6.75, xu);
        }
        Sock(p, "lower", Cx - 2.0, Ceil0, 12.0, "-y");
        Sock(p, "spine", Cx - 1.5, Roof, 12.0, "+y");
        Sock(p, "upperAft", Cx - 2.0, Ft1, 25.0, "+z");
        foreach (var s in new[] { -1, 1 })
            Sock(p, s < 0 ? "podL" : "podR", Cx + s * PodX, 3.375, 15.5, s < 0 ? "-x" : "+x");
        // The bridge: the pilot to port, the weapons officer to starboard, the flight computer and the railgun's module between them.
        Sock(p, "pilot", Cx - 0.9, Ft1, 10.8, "+y");
        Sock(p, "copilot", Cx + 0.9, Ft1, 10.8, "+y");
        Sock(p, "computer", Cx, Ft1, 12.9, "+y");
        Sock(p, "gunModule", Cx, Ft1, 9.3, "+y");
        Sock(p, "chart", Cx + UwF, Ft1 + 0.475, 13.4, "-x");
        Sock(p, "bridgeLocker", Cx - UwF, Ft1 + 0.125, 13.6, "+x");
        // Ops: the forward gunnery station, a plot table with a CRT on it, the flare button.
        Sock(p, "gun", Cx - 0.3, Ft1, 16.3, "+y");
        Sock(p, "gunSeat", Cx - 0.3, Ft1, 17.3, "+y");
        Sock(p, "plot", Cx + 1.6, Ft1, 18.85, "+y");
        Sock(p, "flares", Cx - 2.4, Ft1 + 1.3, 19.875, "-z");
        // The cabins.
        Sock(p, "couchA", Cx - Uw, Ft1 + 0.125, 21.4, "+x");
        Sock(p, "lockerA", Cx - 1.125, Ft1 + 0.125, 21.0, "-x");
        Sock(p, "couchCaptain", Cx + Uw, Ft1 + 0.125, 21.4, "-x");
        Sock(p, "deskCaptain", Cx + 2.4, Ft1 + 0.125, 24.75, "-z");
        Sock(p, "lockerCaptain", Cx + 1.125, Ft1 + 0.125, 21.0, "+x");
        DoorSock(p, "doorBridge", true, Cx - 0.75, ZStep - 0.125, Ft1);
        DoorSock(p, "doorOps", true, Cx - 0.75, 20.0, Ft1);
        DoorSock(p, "doorCabinA", false, 22.9, Cx - 1.0, Ft1);
        DoorSock(p, "doorCaptain", false, 22.9, Cx + 1.0, Ft1);
        Sock(p, "lampBridgeL", Cx - UwF, Ft1 + 2.45, 12.7, "+x");
        Sock(p, "lampBridgeR", Cx + UwF, Ft1 + 2.45, 12.7, "-x");
        Sock(p, "lampOpsL", Cx - Uw, Ft1 + 2.45, 16.6, "+x");
        Sock(p, "lampOpsR", Cx + Uw, Ft1 + 2.45, 16.6, "-x");
        Sock(p, "lampCabinA", Cx - Uw, Ft1 + 2.45, 23.6, "+x");
        Sock(p, "lampCaptain", Cx + Uw, Ft1 + 2.45, 23.6, "-x");
        Sock(p, "switchBridge", Cx + 1.0, Ft1 + 1.3, ZStep - 0.25, "-z");
        Sock(p, "switchOps", Cx + 1.0, Ft1 + 1.3, 19.875, "-z");
        Sock(p, "switchCabins", Cx - 0.875, Ft1 + 1.3, 21.0, "+x");
        foreach (var (what, hx, z) in new[] { ("bridge", UwF, ZFront - 0.25), ("ops", Uw, ZStep), ("cabins", Uw, 25.0) })
            MustFit(what, z, hx + 0.25, Roof);
        MustFit("bridge floor", ZFront - 0.25, LwF + 0.25, Ft1);
        return p.Stats(16000, 36000, 30, 25.0);
    }

    private static Part UpperAft()
    {
        var p = Big("Zero Day Upper Deck Aft", "The Zero Day's upper deck, aft: the crew cabin, the head and the med bay off the corridor, then the mess with the aft gunnery station.");
        double y0 = Ft1, y1 = Ceil1;
        Floor(p, [new Rect(Cx - Lw - 0.25, 25.0, Cx + Lw + 0.25, ZAft + 0.25)], Ceil0, Ft1, Deck, "painted deck plate", AftHatch);
        Floor(p, [new Rect(Cx - Uw - 0.25, 25.0, Cx + Uw + 0.25, ZAft + 0.25)], Ceil1, Roof, Ceil, "painted plating");
        Detail(p, Cx + 1.2, Ft1, 25.15, Cx + Uw - 0.05, Ft1 + 0.012, 33.8, "#b9c4c2", "gloss paint"); // (the med bay's floor, wipe-clean)
        Detail(p, Cx - 0.8, Ft1, 25.0, Cx + 0.8, Ft1 + 0.012, 33.9, "#30353b", "rubber matting"); // (the corridor's runner)
        HatchRail(p, AftHatch, Ft1);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, 25.0, ZAft, Cx + s * (Uw + 0.125), y0, y1);
        WallRun(p, true, Cx - Uw - 0.25, Cx + Uw + 0.25, ZAft + 0.125, y0, y1);
        // The corridor: port the crew cabin and the head, starboard the med bay; the bulkhead into the mess.
        WallRun(p, false, 25.125, 34.0, Cx - 1.0, y0, y1, Door(25.9), Door(31.9));
        WallRun(p, false, 25.125, 34.0, Cx + 1.0, y0, y1, Door(25.9));
        WallRun(p, true, Cx - Uw, Cx - 1.125, 30.0, y0, y1);
        WallRun(p, true, Cx - Uw, Cx + Uw, 34.125, y0, y1, Door(Cx - 0.75));
        // The med bay's wipe-clean walls and a green cross; the head's splashback.
        Detail(p, Cx + Uw - 0.014, Ft1 + 1.0, 28.0, Cx + Uw, Ft1 + 2.2, 31.0, MedWhite, "gloss paint");
        Detail(p, Cx + Uw - 0.02, Ft1 + 1.9, 29.35, Cx + Uw - 0.014, Ft1 + 2.05, 29.85, "#40c070", glow: true);
        Detail(p, Cx + Uw - 0.02, Ft1 + 1.725, 29.525, Cx + Uw - 0.014, Ft1 + 2.225, 29.675, "#40c070", glow: true);
        Detail(p, Cx - Uw + 0.0, Ft1 + 0.75, 30.2, Cx - Uw + 0.014, Ft1 + 1.6, 33.8, "#d8dcdc", "gloss paint");
        // The mess: a serving counter along the port wall, a viewscreen of stars on the aft wall.
        Detail(p, Cx - 1.9, Ft1 + 0.9, ZAft - 0.016, Cx + 1.1, Ft1 + 2.4, ZAft, "#101830", glow: true);
        foreach (var (x, y) in new[] { (-1.6, 1.2), (-1.0, 2.0), (-0.3, 1.5), (0.4, 2.2), (0.8, 1.1), (-1.3, 1.7) })
            Detail(p, Cx + x, Ft1 + y, ZAft - 0.02, Cx + x + 0.03, Ft1 + y + 0.03, ZAft - 0.016, "#ffffff", glow: true);
        CeilStrip(p, Cx - 0.25, Cx + 0.25, 25.1, 33.9, y1);
        CeilStrip(p, Cx - 2.4, Cx + 2.4, 37.0, 37.25, y1);
        CeilStrip(p, Cx + 1.6, Cx + 3.0, 26.5, 32.5, y1);
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (30.0, 38.0), (38.0, ZAft) })
            {
                Facet(p, 3, s, za, zb, Skin, "painted plating");
                Facet(p, 4, s, za, zb, Skin2, "painted plating");
                Plates(p, 3, s, za, zb, 6, 0.12, 0.9);
                Plates(p, 4, s, za, zb, 7);
                Chine(p, 3, s, za, zb, 0.25, 0.12, Armour);
                FStrip(p, 3, s, za, zb, 0.03, 0.97, 0.04, 0.065, 0.025, Glow, glow: true);
            }
        foreach (var (za, zb) in new[] { (25.0, 30.0), (30.0, 38.0), (38.0, ZAft) })
        {
            Void(p, za, zb, 3.25, 3.5, Lw + 0.25);
            Void(p, za, zb, 3.5, 5.2, Uw + 0.25);
            Void(p, za, zb, 5.2, 6.75, Uw + 0.25);
        }
        Sock(p, "fore", Cx - 2.0, Ft1, 25.0, "-z");
        foreach (var s in new[] { -1, 1 })
            Sock(p, s < 0 ? "podL" : "podR", Cx + s * PodX, 3.375, 37.5, s < 0 ? "-x" : "+x");
        // The crew cabin, the head, the med bay.
        Sock(p, "couchB0", Cx - Uw, Ft1 + 0.125, 27.2, "+x");
        Sock(p, "lockerB", Cx - 1.125, Ft1 + 0.125, 28.0, "-x");
        Sock(p, "deskB", Cx - 2.3, Ft1 + 0.125, 29.875, "-z");
        Sock(p, "toilet", Cx - 2.9, Ft1 + 0.125, 34.0, "-z");
        Sock(p, "basin", Cx - Uw, Ft1 + 0.125, 31.2, "+x");
        Sock(p, "shower", Cx - 1.85, Ft1 + 0.125, 30.125, "+z");
        Sock(p, "med0", Cx + 2.95, Ft1 + 0.8, 34.0, "-z");
        Sock(p, "med1", Cx + 1.85, Ft1 + 0.8, 34.0, "-z");
        Sock(p, "cabinet", Cx + Uw, Ft1 + 0.85, 27.3, "-x");
        Sock(p, "medLocker", Cx + 1.125, Ft1 + 0.125, 29.0, "+x");
        // The mess: galley, coffee, the table (with the office plant on it: it is the IT Department's ship), the aft gunnery station.
        Sock(p, "stove", Cx - Uw, Ft1 + 0.45, 35.6, "+x");
        Sock(p, "coffee", Cx - Uw, Ft1 + 0.125, 36.6, "+x");
        Sock(p, "messLocker", Cx - Uw, Ft1 + 0.125, 37.4, "+x");
        Sock(p, "table", Cx - 2.0, Ft1, 38.4, "+y");
        Sock(p, "gun", Cx - 0.3, Ft1, 39.6, "+y");
        Sock(p, "gunSeat", Cx - 0.3, Ft1, 40.6, "+y");
        DoorSock(p, "doorCabinB", false, 25.9, Cx - 1.0, Ft1);
        DoorSock(p, "doorHead", false, 31.9, Cx - 1.0, Ft1);
        DoorSock(p, "doorMed", false, 25.9, Cx + 1.0, Ft1);
        DoorSock(p, "doorMess", true, Cx - 0.75, 34.125, Ft1);
        Sock(p, "lampCabinB", Cx - Uw, Ft1 + 2.45, 28.8, "+x");
        Sock(p, "lampHead", Cx - Uw, Ft1 + 2.45, 32.6, "+x");
        Sock(p, "lampMed", Cx + Uw, Ft1 + 2.45, 32.0, "-x");
        Sock(p, "lampMessL", Cx - Uw, Ft1 + 2.45, 39.6, "+x");
        Sock(p, "lampMessR", Cx + Uw, Ft1 + 2.45, 36.4, "-x");
        Sock(p, "switchCrew", Cx - 0.875, Ft1 + 1.3, 30.5, "+x");
        Sock(p, "switchMed", Cx + 0.875, Ft1 + 1.3, 28.0, "-x");
        Sock(p, "switchMess", Cx + 1.0, Ft1 + 1.3, 34.25, "+z");
        foreach (var z in new[] { 25.0, ZAft + 0.25 })
            MustFit("upper aft", z, Uw + 0.25, Roof);
        return p.Stats(16000, 36000, 30, 25.0);
    }

    // ---- the bow: the chisel nose and the canopy

    private static Part Bow()
    {
        var p = Big("Zero Day Bow", "The Zero Day's bow: a faceted chisel nose, the bridge's canopy and two retro jets in its cheeks. Trim tankage inside.", "fueltank", Armour);
        foreach (var (za, zb) in new[] { (Zt, Zc), (Zc, 8.0) })
        {
            Facet(p, 0, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Skin2, "painted plating");
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                var canopy = za >= Zc;
                Facet(p, 3, s, za, zb, canopy ? Armour : Skin, canopy ? "plated steel" : "painted plating");
                Facet(p, 4, s, za, zb, canopy ? Glass : Skin2, "painted plating", glass: canopy);
                Chine(p, 2, s, za, zb, 0.26, 0.12, Armour2);
                Chine(p, 3, s, za, zb, 0.2, 0.1, Armour);
            }
            Facet(p, 5, 1, za, zb, za >= Zc ? Glass : Armour, "plated steel", glass: za >= Zc);
        }
        // Armour on the nose: the cheeks stepped, the chin, a blue blaze down each side.
        foreach (var s in new[] { -1, 1 })
        {
            Slab(p, 2, s, Zc, 8.0, 0.05, 0.95, 0.1, 0.9, 0.02, 0.14, 0.08, Armour);
            Slab(p, 2, s, Zc, 8.0, 0.25, 0.9, 0.25, 0.75, 0.14, 0.22, 0.05, Armour2);
            Slab(p, 2, s, Zt, Zc, 0.25, 0.95, 0.12, 0.88, 0.02, 0.11, 0.06, Blue, "painted plating");
            Slab(p, 1, s, Zc, 8.0, 0.08, 0.92, 0.1, 0.9, 0.02, 0.1, 0.05, Armour2);
            Slab(p, 3, s, Zt, Zc, 0.3, 0.95, 0.1, 0.9, 0.02, 0.1, 0.05, Armour);
            // The canopy's frames: three bars across the glass and one along its foot.
            foreach (var u in new[] { 0.02, 0.36, 0.68, 0.98 })
                FStrip(p, 4, s, Zc, 8.0, u - 0.015, u + 0.015, 0.012, 0.988, 0.03, Frame, "painted steel");
            // Running lights on the cheeks: port red, starboard green.
            var q = OnFacet(2, s, Zc, 8.0, 0.5, 1.0);
            Detail(p, q[0] - 0.08 + s * 0.04, q[1] - 0.05, q[2] - 0.08, q[0] + 0.08 + s * 0.04, q[1] + 0.07, q[2] + 0.08, s < 0 ? "#ff3020" : "#30ff60", glow: true);
        }
        foreach (var u in new[] { 0.02, 0.36, 0.68, 0.98 })
            FStrip(p, 5, 1, Zc, 8.0, u - 0.015, u + 0.015, 0.012, 0.988, 0.03, Frame, "painted steel");
        FStrip(p, 5, 1, Zc, 8.0, 0.0, 1.0, 0.485, 0.515, 0.037, Frame, "painted steel");
        Slab(p, 5, 1, Zt, Zc, 0.1, 0.92, 0.12, 0.88, 0.02, 0.12, 0.06, Armour2);
        // The tip: a dark cap over the blade, a sensor glow.
        var tip = At(Zt);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + tip[k].H, tip[k].Y, Zt)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - tip[k].H, tip[k].Y, Zt)))], Frame, "dirty steel");
        Detail(p, Cx - 0.12, 2.3, Zt - 0.032, Cx + 0.12, 2.6, Zt + 0.01, Glow, glow: true); // (proud of the tip plate)
        // Hidden boxes inside the nose.
        foreach (var (za, zb) in new[] { (Zt + 2.0, Zc), (Zc, 8.0) })
            foreach (var (ya, yb) in new[] { (At(za)[0].Y + 0.05, 1.5), (1.5, 3.2), (3.2, 5.0), (5.0, 6.4) })
                Void(p, za, zb, ya, yb, 0);
        Sock(p, "keel", Cx, 0.0, 8.0, "+z");
        foreach (var s in new[] { -1, 1 })
        {
            var q = OnFacet(1, s, Zc, 8.0, 0.4, 0.75);
            double rx = Math.Round(q[0], 2), ry = Math.Round(q[1], 2), rz = Math.Round(q[2], 2);
            Sock(p, s < 0 ? "retroL" : "retroR", rx, ry, rz, "-z");
            Solid(p, rx - 0.4, ry - 0.4, rz, rx + 0.4, ry + 0.4, rz + 0.9, Armour2, "plated steel"); // (the retro jet's housing)
            Detail(p, rx - 0.41, ry + 0.1, rz + 0.2, rx + 0.41, ry + 0.18, rz + 0.7, Hazard, "hazard paint");
        }
        p.Set(("fuelTank", Kw(("capacity", 1500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx, 1.0, 8.0).Cast<object>()], "+z", "fuel"))));
        return p.Stats(9000, 24000, 40, 15.0);
    }

    // ---- the spine: the roof, the railgun's housing, the sensor mast, the flare launchers' mounts

    private static Part Spine()
    {
        var p = Big("Zero Day Spine", "The Zero Day's spine: the flat roof, the railgun's armoured housing, the sensor mast and the flare launchers' mounts.", color: Armour);
        double[] zs = [8.0, 14.0, 20.0, 30.0, 38.0, 42.0];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            Facet(p, 5, 1, zs[i], zs[i + 1], Armour, "plated steel");
            Slab(p, 5, 1, zs[i], zs[i + 1], 0.04, 0.96, 0.05, 0.3, 0.02, 0.1, 0.05, Armour2);
            Slab(p, 5, 1, zs[i], zs[i + 1], 0.04, 0.96, 0.7, 0.95, 0.02, 0.1, 0.05, Armour2);
            foreach (var s in new[] { -1, 1 })
                Chine(p, 4, s, zs[i], zs[i + 1], 0.2, 0.1, Armour2);
        }
        // The railgun's housing: a raised, faceted trough down the middle from behind the bridge to the mast, the gun in it.
        double hz0 = 11.0, hz1 = 33.0;
        double Top(double z) => At(z)[4].Y;
        double[][] Sect(double z, double w, double h)
        {
            var y = Top(z);
            return [P(Cx - w, y - 0.012, z), P(Cx - w * 0.8, y + h, z), P(Cx + w * 0.8, y + h, z), P(Cx + w, y - 0.012, z)]; // (its foot inside the roof plate, off its underside)
        }
        Prism(p, Sect(hz0 - 2.0, 0.5, 0.05), Sect(hz0, 1.2, 0.55), Armour2, "plated steel");
        foreach (var s in new[] { -1, 1 })
        {
            // Each cheek of the trough a long slab (the barrel lies between them).
            double[][] Cheek(double z) { var y = Top(z); return [P(Cx + s * 1.2, y - 0.012, z), P(Cx + s * 0.95, y + 0.55, z), P(Cx + s * 0.62, y + 0.55, z), P(Cx + s * 0.55, y - 0.012, z)]; }
            Prism(p, Cheek(hz0), Cheek(20.0), Armour, "plated steel");
            Prism(p, Cheek(20.0 + 0.02), Cheek(30.0), Armour, "plated steel");
            Prism(p, Cheek(30.0 + 0.02), Cheek(hz1), Armour2, "plated steel");
            foreach (var z in new[] { 13.0, 17.0, 23.0, 27.0, 31.5 })
                Detail(p, Cx + s * 0.85 - 0.12, Top(z) + 0.555, z - 0.12, Cx + s * 0.85 + 0.12, Top(z) + 0.57, z + 0.12, Blue, "painted plating");
            Detail(p, Cx + s * 1.21 - 0.01, Top(20) + 0.05, 12.0, Cx + s * 1.21 + 0.01, Top(20) + 0.09, 32.0, Glow, glow: true);
        }
        Solid(p, Cx - 0.55, Top(20) - 0.01, hz0, Cx + 0.55, Top(20) + 0.06, hz1, Frame, "dirty steel"); // (the trough's floor, which the gun lies on)
        Hidden(p, Cx - 1.15, Top(20), hz0 + 0.2, Cx - 0.6, Top(20) + 0.5, hz1 - 0.2);
        Hidden(p, Cx + 0.6, Top(20), hz0 + 0.2, Cx + 1.15, Top(20) + 0.5, hz1 - 0.2);
        // The dorsal block aft of the housing (the electronics: it is the IT Department's ship): a faceted mass that rises out of the
        // railgun's housing, steps up again over the mess and rakes down to the drive, so the roofline isn't one flat line.
        double[][] Blk(double z, double w, double h, double wt)
        {
            var y = Top(z) - 0.012;
            return [P(Cx - w, y, z), P(Cx - wt - (w - wt) * 0.25, y + h * 0.7, z), P(Cx - wt, y + h, z), P(Cx + wt, y + h, z), P(Cx + wt + (w - wt) * 0.25, y + h * 0.7, z), P(Cx + w, y, z)];
        }
        Prism(p, Blk(hz1, 1.2, 0.55, 0.95), Blk(36.0, 2.3, 1.05, 1.5), Armour, "plated steel");
        Prism(p, Blk(36.0 + 0.01, 2.3, 1.05, 1.5), Blk(43.0, 2.3, 1.05, 1.5), Armour2, "plated steel");
        Prism(p, Blk(43.0 + 0.01, 2.3, 1.05, 1.5), Blk(46.0, 1.6, 0.35, 1.2), Armour, "plated steel");
        // (the step over it: a second, narrower block with a corporate blue band, and vents)
        Prism(p, Blk(37.2, 1.2, 1.05, 1.0), Blk(38.4, 1.5, 1.5, 1.05), Armour, "plated steel");
        Prism(p, Blk(38.4 + 0.01, 1.5, 1.5, 1.05), Blk(41.8, 1.5, 1.5, 1.05), Armour, "plated steel");
        Prism(p, Blk(41.8 + 0.01, 1.5, 1.5, 1.05), Blk(42.9, 1.2, 1.05, 1.0), Armour2, "plated steel");
        foreach (var sd in new[] { -1, 1 })
        {
            Plate(p, [P(Cx + sd * 1.4, Top(40) + 1.05, 38.6), P(Cx + sd * 1.4, Top(40) + 1.05, 41.6), P(Cx + sd * 1.43, Top(40) + 1.3, 41.6), P(Cx + sd * 1.43, Top(40) + 1.3, 38.6)], Blue, "painted plating");
            for (var z = 36.6; z < 42.6; z += 0.5)
                Detail(p, Cx + sd * 1.9 - 0.25, Top(z) + 0.62, z, Cx + sd * 1.9 + 0.25, Top(z) + 0.66, z + 0.25, Frame, "soot");
            Detail(p, Cx + sd * 2.0 - 0.02, Top(40) + 0.25, 36.4, Cx + sd * 2.0 + 0.02, Top(40) + 0.29, 42.9, Glow, glow: true);
        }
        Hidden(p, Cx - 1.6, Top(40) - 0.01, 36.2, Cx + 1.6, Top(40) + 1.0, 42.0);
        // The sensor mast on top: a raked blade, a dish to starboard, red tips; two whip antennae aft.
        double mz = 39.2, my = Top(39.2) + 1.48;
        Prism(p, [P(Cx - 0.12, my, mz - 1.2), P(Cx + 0.12, my, mz - 1.2), P(Cx + 0.12, my, mz + 1.0), P(Cx - 0.12, my, mz + 1.0)], [P(Cx - 0.06, my + 2.2, mz + 0.2), P(Cx + 0.06, my + 2.2, mz + 0.2), P(Cx + 0.06, my + 2.2, mz + 0.9), P(Cx - 0.06, my + 2.2, mz + 0.9)], Armour, "plated steel");
        Detail(p, Cx - 0.07, my + 2.2, mz + 0.45, Cx + 0.07, my + 2.34, mz + 0.65, Red, glow: true);
        Prism(p, Ngon(Cx + 0.75, my + 0.5, mz + 1.9, 0.08, 8), Ngon(Cx + 0.75, my + 0.75, mz + 1.9, 0.55, 8), "#c8ccc8", "painted steel");
        Detail(p, Cx + 0.7, my - 0.02, mz + 1.85, Cx + 0.8, my + 0.5, mz + 1.95, Frame, "painted steel");
        foreach (var sd in new[] { -1, 1 })
        {
            Detail(p, Cx + sd * 1.25 - 0.03, Top(42.5) + 1.03, 42.4, Cx + sd * 1.25 + 0.03, Top(42.5) + 2.3, 42.46, Frame, "steel");
            Detail(p, Cx + sd * 1.25 - 0.04, Top(42.5) + 2.3, 42.39, Cx + sd * 1.25 + 0.04, Top(42.5) + 2.38, 42.47, Red, glow: true);
        }
        // Hidden: the roof void.
        for (var i = 0; i < zs.Length - 1; i++)
            Void(p, zs[i], zs[i + 1], Roof, Math.Min(Top(zs[i]), Top(zs[i + 1])) - 0.02, 0);
        Sock(p, "upper", Cx - 1.5, Roof, 12.0, "-y");
        Sock(p, "railgun", Cx, Top(20) + 0.06, 32.0, "+y");
        foreach (var (s, z, n) in new[] { (-1, 17.6, "flaresFL"), (1, 17.6, "flaresFR"), (-1, 21.4, "flaresBL"), (1, 21.4, "flaresBR") })
            Sock(p, n, Cx + s * 1.75, Top(z) - 0.02, z, "+y");
        return p.Stats(8000, 24000, 45, 15.0);
    }

    // ---- the stern: the flare out to the drive and its mount

    private static Part SternPart()
    {
        var p = Big("Zero Day Stern", "The Zero Day's stern: the hull flares out to carry the drive array across its whole width. The drive's tankage is inside.", "fueltank", Armour);
        foreach (var (za, zb) in new[] { (42.0, 44.0), (44.0, Stern) })
        {
            Facet(p, 0, 1, za, zb, Armour, "plated steel");
            Facet(p, 5, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Skin2, "painted plating");
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                Facet(p, 3, s, za, zb, Skin, "painted plating");
                Facet(p, 4, s, za, zb, Skin2, "painted plating");
                for (var k = 1; k <= 4; k++)
                    Slab(p, k, s, za, zb, 0.06, 0.94, 0.08, 0.92, 0.02, za < 44 ? 0.14 : 0.2, 0.06, k == 2 && za < 44 ? Blue : Armour, k == 2 && za < 44 ? "painted plating" : "plated steel");
                Chine(p, 2, s, za, zb, 0.32, 0.16, Armour2);
                Chine(p, 3, s, za, zb, 0.25, 0.12, Armour);
            }
        }
        foreach (var s in new[] { -1, 1 })
        {
            Shoulder(p, 3, s, 42.3, 46.3, 0.8, 0.45, 1.2, Armour);
            Shoulder(p, 1, s, 42.6, 46.0, 0.6, 0.3, 1.0, Armour);
        }
        // The stern face: an armoured ring round the drive's mouth (the drive's housing closes the middle).
        var q = At(Stern);
        var ring = Enumerable.Range(0, 5).Select(k => (Cx + q[k].H, q[k].Y)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => (Cx - q[k].H, q[k].Y))).ToArray();
        Plate(p, [.. ring.Select(r => P(r.Item1, r.Item2, Stern))], Frame, "soot");
        // Heat-sink fins above and below the drive, angled out.
        foreach (var s in new[] { -1, 1 })
            foreach (var x in new[] { 1.2, 2.6 })
            {
                Prism(p, [P(Cx + s * x - 0.05, At(44)[4].Y, 43.4), P(Cx + s * x + 0.05, At(44)[4].Y, 43.4), P(Cx + s * x + 0.05, At(44)[4].Y, 46.2), P(Cx + s * x - 0.05, At(44)[4].Y, 46.2)],
                    [P(Cx + s * x - 0.03, At(44)[4].Y + 0.7, 44.6), P(Cx + s * x + 0.03, At(44)[4].Y + 0.7, 44.6), P(Cx + s * x + 0.03, At(44)[4].Y + 0.7, 46.6), P(Cx + s * x - 0.03, At(44)[4].Y + 0.7, 46.6)], Armour2, "plated steel");
            }
        foreach (var (ya, yb) in new[] { (-1.4, -0.4), (-0.4, 1.0), (1.0, 3.3), (3.3, 5.6), (5.6, 7.0) })
            Void(p, ZAft + 0.25, Stern, ya, yb, 0);
        Sock(p, "deck", Cx, 1.5, ZAft + 0.25, "-z");
        Sock(p, "drive", Cx, 2.6, Stern, "+z");
        p.Set(("fuelTank", Kw(("capacity", 9000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx + 4.0, 1.0, Stern).Cast<object>()], "+z", "fuel"))));
        return p.Stats(14000, 36000, 45, 20.0);
    }

    // ---- the torpedo sponsons

    private const double SpZ0 = 20.0, SpZ1 = 30.0, SpOut = 8.3, SpRoom = 7.95, SpTop = 3.1, SpBot = -0.6;

    /// <summary>A sponson's outline (side s) at full size: from the hull's lower chine round the outside up to the beam (x, y).</summary>
    private static (double X, double Y)[] SpOutline(int s, double shrink)
    {
        var q = At(25.0);
        var low = q[0].H + (q[1].H - q[0].H) * (SpBot - q[0].Y) / (q[1].Y - q[0].Y); // where the sponson's floor meets the lower chine
        (double, double) L(double h, double y) => (Cx + s * h, y);
        if (shrink >= 1)
        {
            // Collapsed onto the hull: points along the line from the lower chine meeting to the beam.
            return [L(low, SpBot), L(low + (q[2].H - low) * 0.2, SpBot + (q[2].Y - SpBot) * 0.2), L(low + (q[2].H - low) * 0.4, SpBot + (q[2].Y - SpBot) * 0.4),
                L(low + (q[2].H - low) * 0.6, SpBot + (q[2].Y - SpBot) * 0.6), L(low + (q[2].H - low) * 0.8, SpBot + (q[2].Y - SpBot) * 0.8), L(q[2].H, q[2].Y)];
        }
        return [L(low, SpBot), L(SpOut - 0.6, SpBot), L(SpOut, SpBot + 0.6), L(SpOut, 2.6), L(SpOut - 0.5, SpTop), L(q[2].H, q[2].Y)];
    }

    private static Part Sponson(int s)
    {
        var name = s < 0 ? "Zero Day Torpedo Bay L" : "Zero Day Torpedo Bay R";
        var p = Big(name, "One of the Zero Day's torpedo bays: an armoured sponson on the flank with a rotary torpedo magazine in it and a launch port forward.", color: Armour);
        double zf = SpZ0 - 0.75, za = SpZ1 + 4.0;
        var full = SpOutline(s, 0);
        var flat = SpOutline(s, 1);
        double[] V((double X, double Y) q, double z) => P(q.X, q.Y, z);
        for (var i = 0; i < full.Length - 1; i++)
        {
            var col = i == 3 ? Skin : i == 4 ? Skin2 : Armour;
            var fin = i == 3 ? "painted plating" : "plated steel";
            var dz = i % 2 == 1 ? FacetStagger : 0; // (odd panels' corners a little aft, so neighbours' ends never share a plane)
            Quad(p, V(flat[i], zf + dz), V(flat[i + 1], zf + dz), V(full[i + 1], SpZ0 + dz), V(full[i], SpZ0 + dz), col, fin);
            Quad(p, V(full[i], SpZ0 + dz), V(full[i + 1], SpZ0 + dz), V(full[i + 1], SpZ1 + dz), V(full[i], SpZ1 + dz), col, fin);
            Quad(p, V(full[i], SpZ1 + dz), V(full[i + 1], SpZ1 + dz), V(flat[i + 1], za), V(flat[i], za), col, fin);
        }
        // Armour on the outer face, stepped, and the beam's rail continued along its top.
        double Xo(double d) => Cx + s * (SpOut + d);
        foreach (var (z0, z1, d, col) in new[] { (20.3, 23.6, 0.14, Armour2), (23.7, 26.3, 0.1, Blue), (26.4, 29.7, 0.16, Armour2) })
        {
            Prism(p, [P(Xo(0.0), 0.3, z0), P(Xo(0.0), 2.4, z0), P(Xo(0.0), 2.4, z1), P(Xo(0.0), 0.3, z1)],
                [P(Xo(d), 0.38, z0 + 0.08), P(Xo(d), 2.32, z0 + 0.08), P(Xo(d), 2.32, z1 - 0.08), P(Xo(d), 0.38, z1 - 0.08)], col, col == Blue ? "painted plating" : "plated steel");
        }
        Prism(p, [P(Xo(0.16), 0.9, 21.0), P(Xo(0.16), 1.8, 21.0), P(Xo(0.16), 1.8, 29.0), P(Xo(0.16), 0.9, 29.0)],
            [P(Xo(0.26), 0.98, 21.5), P(Xo(0.26), 1.72, 21.5), P(Xo(0.26), 1.72, 28.5), P(Xo(0.26), 0.98, 28.5)], Armour, "plated steel");
        // Hazard chevrons and "TORPEDO" stripes by the port; a light strip along the top.
        Detail(p, Xo(-0.6) - 0.3, SpTop - 0.02, 20.4, Xo(-0.6) + 0.3, SpTop + 0.01, 21.2, Hazard, "hazard paint");
        Plate(p, [P(Xo(0.003), 2.5, 20.1), P(Xo(0.003), 2.56, 20.1), P(Xo(0.003), 2.56, 29.9), P(Xo(0.003), 2.5, 29.9)], Glow, glow: true, thick: 0.015);
        // The front: a cap round the launch port, framed in hazard yellow; the port open onto the magazine.
        double px0 = Cx + s * 5.6, px1 = Cx + s * (SpOut - 0.35), py0 = 0.4, py1 = 2.7;
        double xa = Math.Min(px0, px1), xb = Math.Max(px0, px1);
        var capOut = full;
        // (the cap as plates round the port: below, above, inboard and outboard of it, each a few mm off the next so none share a face)
        double zB = SpZ0, zA = SpZ0 - 0.005, zO = SpZ0 + 0.005, zI = SpZ0 + 0.0025;
        Plate(p, [V(capOut[0], zB), V(capOut[1], zB), V(capOut[2], zB), P(s < 0 ? xa : xb, py0, zB), P(s < 0 ? xb : xa, py0, zB)], Armour, "plated steel");
        Plate(p, [P(xa, py1, zA), P(xb, py1, zA), P(xb, SpTop - 0.1, zA), P(xa, SpTop - 0.1, zA)], Armour, "plated steel");
        Plate(p, [P(Cx + s * (SpOut), SpBot + 0.6, zO), P(Cx + s * SpOut, 2.6, zO), P(s < 0 ? xa : xb, py1, zO), P(s < 0 ? xa : xb, py0, zO)], Armour, "plated steel");
        Plate(p, [V(full[0], zI), P(s < 0 ? xb : xa, py0, zI), P(s < 0 ? xb : xa, py1, zI), V(full[5], zI)], Armour, "plated steel");
        foreach (var (x0, y0, x1, y1) in new[] { (xa - 0.08, py0 - 0.08, xb + 0.08, py0), (xa - 0.08, py1, xb + 0.08, py1 + 0.08), (xa - 0.08, py0, xa, py1), (xb, py0, xb + 0.08, py1) })
            Detail(p, x0, y0, SpZ0 - 0.045, x1, y1, SpZ0 - 0.03, Hazard, "hazard paint"); // (clear in front of the cap's plates)
        // The port's inner tube: dark walls back to the magazine, from behind the cap.
        Detail(p, xa, py0 - 0.016, SpZ0 + 0.03, xb, py0, 21.0, Frame, "soot");
        Detail(p, xa, py1, SpZ0 + 0.03, xb, py1 + 0.016, 21.0, Frame, "soot");
        // The room: floor, the outer wall, the ceiling, the back wall; hidden boxes round it inside the sponson.
        double xi = Cx + s * (Lw + 0.25), xo = Cx + s * SpRoom, xl = Math.Min(xi, xo), xr = Math.Max(xi, xo);
        Solid(p, xl, 0.0, SpZ0 + 0.25, xr, Ft0, SpZ1 - 0.25, Grate, "painted grating");
        Solid(p, s < 0 ? Cx - SpOut + 0.12 : xo, Ft0, SpZ0 + 0.25, s < 0 ? xo : Cx + SpOut - 0.12, 2.85, SpZ1 - 0.25, Wall, "painted plating");
        Solid(p, xl, 2.85, SpZ0 + 0.25, xr, SpTop - 0.05, SpZ1 - 0.25, Ceil, "painted plating");
        Solid(p, xl, Ft0, SpZ1 - 0.5, xr, 2.85, SpZ1 - 0.25, Wall, "painted plating");
        // (the forward wall: closed inboard of the port and under and over it)
        Solid(p, s < 0 ? xb : xl, Ft0, SpZ0 + 0.25, s < 0 ? xr : xa, 2.85, SpZ0 + 0.5, Wall, "painted plating");
        Solid(p, Math.Max(xl, xa), Ft0, SpZ0 + 0.25, Math.Min(xr, xb), py0, SpZ0 + 0.5, Wall, "painted plating");
        Hidden(p, xl, SpBot + 0.05, SpZ0 + 0.25, xr, 0.0, SpZ1 - 0.25);
        Hidden(p, xa, py1 + 0.05, SpZ0 + 0.05, xb, SpTop - 0.15, SpZ0 + 0.24);
        Detail(p, xl + 0.05, Ft0, 23.3, xr - 0.05, Ft0 + 0.012, 27.2, Hazard, "hazard paint"); // (the magazine's keep-clear)
        CeilStrip(p, Math.Min(xi, xo) + 0.3, Math.Max(xi, xo) - 0.3, 21.0, 21.2, 2.85);
        CeilStrip(p, Math.Min(xi, xo) + 0.3, Math.Max(xi, xo) - 0.3, 28.8, 29.0, 2.85);
        Sock(p, "deck", Cx + s * (Lw + 0.25), 2.95, 24.5, s < 0 ? "+x" : "-x");
        Sock(p, "magazine", Cx + s * 6.9, 2.85, 25.4, "-y");
        Sock(p, "lamp", Cx + s * SpRoom, Ft0 + 2.2, 23.0, s < 0 ? "+x" : "-x");
        Sock(p, "switch", Cx + s * SpRoom, Ft0 + 1.3, 21.4, s < 0 ? "+x" : "-x");
        return p.Stats(9000, 30000, 45, 15.0);
    }

    /// <summary>Where a barbette meets the hull: the outer face of the void box beside the upper floor (Void's rounding).</summary>
    private static double PodX => Math.Floor((Math.Min(SkinHalf(20, 3.25), SkinHalf(20, 3.5)) - 0.06) * 100) / 100;

    // ---- the turret barbette (both sides)

    private static Part Barbette()
    {
        var p = Big("Zero Day Turret Barbette", "An armoured barbette on the Zero Day's beam: a point-defence turret turns on its top.", color: Armour);
        // Local: x out from the hull (the socket at 0), y up, z along the ship.
        double[][] Sec(double x, double w, double y0, double y1) => [[x, y0, -w], [x, y1, -w * 0.8], [x, y1, w * 0.8], [x, y0, w]];
        double[][] Pt(IEnumerable<double[]> q) => [.. q.Select(a => a.Select(v => Py.Round(v, 4)).ToArray())];
        Prism(p, Pt(Sec(-0.5, 1.2, -0.9, 0.7)), Pt(Sec(0.9, 1.1, -0.6, 0.9)), Armour, "plated steel");
        Prism(p, Pt(Sec(0.9, 1.1, -0.6, 0.9)), Pt(Sec(1.6, 0.75, -0.2, 0.9)), Armour2, "plated steel");
        Prism(p, Pt(Sec(0.3, 0.9, 0.9, 0.91)), Pt(Sec(1.45, 0.6, 0.9, 0.95)), Blue, "painted plating");
        p.Add(Box([1.55, 0.2, -0.45], [1.62, 0.5, 0.45], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([1.0, 0.955, -0.42], [1.4, 0.995, 0.42], Frame, detail: true, shape: Kw(("finish", "dirty steel")))); // (the turntable's ring)
        p.Add(Box([0.0, -0.45, -0.75], [1.45, 1.0, 0.75], hidden: true));
        p.Sock("hull", [0, 0, 0], "-x");
        p.Sock("top", [1.2, 1.0, 0], "+y");
        return p.Stats(1800, 8000, 40, 8.0);
    }

    // ---- the drive array, the lift and retro jets, the legs

    private const double DriveForce = 9000000, LiftForce = 1500000, RetroForce = 1200000, Burn = 2.5;

    /// <summary>The drive array across the whole stern: four bells side by side in one armoured collar, heat-sink plates between them,
    /// a link-blue glow deep in each. Origin on the stern face; pushes along -z (its exhaust +z).</summary>
    private static Part Drive()
    {
        var p = new Part("Zero Day Drive Array", Small, Section, $"The Zero Day's drive: four bells in one armoured collar the width of the ship, {DriveForce / 1e6:0} MN from the stern's tankage.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        // The collar: a faceted frame matching the stern's outline, standing 1.2 m off it.
        var q = At(Stern);
        double[][] Outline(double z, double grow, double dy) => [.. Enumerable.Range(0, 5).Select(k => new[] { Py.Round(q[k].H * grow, 4), Py.Round(q[k].Y - 2.6 + dy, 4), z }).Concat(Enumerable.Range(0, 5).Reverse().Select(k => new[] { Py.Round(-q[k].H * grow, 4), Py.Round(q[k].Y - 2.6 + dy, 4), z }))];
        var a = Outline(0.02, 0.97, 0);
        var b = Outline(1.2, 0.93, 0);
        for (var i = 0; i < 10; i++)
        {
            var j = (i + 1) % 10;
            Quad(p, a[i], a[j], b[j], b[i], i % 5 == 2 ? Armour2 : Armour, "plated steel");
        }
        // The face: a dark plate the bells come out of.
        Plate(p, [.. b.Select(v => new[] { v[0], v[1], 1.2 })], "#1a1c1e", "soot");
        // Four bells across.
        foreach (var x in new[] { -4.35, -1.45, 1.45, 4.35 })
        {
            (double Z, double R)[] st = [(1.2, 1.15), (1.6, 1.0), (2.4, 1.2), (3.3, 1.4)];
            for (var s = 0; s < st.Length - 1; s++)
            {
                var r0 = RingZ(x, 0.2, st[s].R, st[s].R, st[s].Z, 10);
                var r1 = RingZ(x, 0.2, st[s + 1].R, st[s + 1].R, st[s + 1].Z, 10);
                for (var i = 0; i < 10; i++)
                    Quad(p, r0[i], r0[(i + 1) % 10], r1[(i + 1) % 10], r1[i], s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
            }
            Prism(p, RingZ(x, 0.2, 1.24, 1.24, 2.6, 10), RingZ(x, 0.2, 1.3, 1.3, 2.75, 10), "#5a4a3a", "copper");
            Plate(p, [.. RingZ(x, 0.2, 0.85, 0.85, 1.65, 10)], Glow, glow: true);
        }
        // Heat-sink vanes above and below the bells, and hazard marks on the collar's corners.
        foreach (var x in new[] { -5.0, -2.9, 0.0, 2.9, 5.0 })
        {
            p.Add(Box([x - 0.04, 1.6, 0.3], [x + 0.04, 3.3, 1.15], Armour2, detail: true, shape: Kw(("finish", "plated steel"))));
            p.Add(Box([x - 0.04, -3.4, 0.3], [x + 0.04, -1.4, 1.15], Armour2, detail: true, shape: Kw(("finish", "plated steel"))));
        }
        p.Add(Box([-5.6, -1.2, 0.0], [5.6, 1.6, 1.2], hidden: true));
        p.Add(Box([-4.6, 1.6, 0.0], [4.6, 3.8, 1.0], hidden: true));
        p.Add(Box([-2.6, -3.9, 0.0], [2.6, -1.2, 1.0], hidden: true));
        p.Sock("front", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 2.0), ("exhaust", "+z"), ("at", Py.L(0, 0.2, 3.3)), ("burnRate", Py.Round(DriveForce / 1e6 * Burn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 1.0, 0], "-z", "fuel"))));
        return p.Stats(16000, 20000, 35, 20.0);
    }

    /// <summary>A jet in a squat armoured housing: lift (down) or retro (forward). Origin at its root; the bell along -y.</summary>
    private static Part Jet(string name, string desc, double force, double scale)
    {
        var p = new Part(name, Small, Section, desc, role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[] R(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * scale * Math.Cos(an), 4), Py.Round(y * scale, 4), Py.Round(r * scale * Math.Sin(an), 4)];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.55, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.48, -0.45, i))], Armour2, "plated steel");
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.32, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.24, i))], Blue, "painted plating");
        (double Y, double R)[] st = [(-0.45, 0.32), (-0.6, 0.27), (-0.9, 0.38), (-1.05, 0.44)];
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < 8; i++)
                Quad(p, R(st[s].R, st[s].Y, i), R(st[s].R, st[s].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => R(0.24, -0.61, i))], Glow, glow: true);
        p.Add(Box([-0.45 * scale, -0.45 * scale, -0.45 * scale], [0.45 * scale, 0, 0.45 * scale], hidden: true));
        p.Add(Box([-0.4 * scale, -1.05 * scale, -0.4 * scale], [0.4 * scale, -0.45 * scale, 0.4 * scale], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", force), ("spool", 0.5), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-1.05 * scale, 4), 0)), ("burnRate", Py.Round(force / 1e6 * Burn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.45 * scale, 4), Py.Round(-0.2 * scale, 4), 0], "+x", "fuel"))));
        return p.Stats(900 * scale, 3000, 20, 6.0);
    }

    private static Part Leg()
    {
        var p = new Part("Zero Day Landing Leg", Small, Section, "A landing leg: a telescopic strut and its ram on a broad pad.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var bottom = Ground - Mid[0].Item2 - 0.02; // (the pad's underside, below the keel)
        p.Add(Box([-0.24, -0.9, -0.24], [0.24, 0, 0.24], Armour, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-0.16, bottom + 0.2, -0.16], [0.16, -0.9, 0.16], "#9aa0a4", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.55, bottom, -0.55], [0.55, bottom + 0.2, 0.55], Frame, shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.32, bottom + 0.2, -0.32], [0.32, bottom + 0.3, 0.32], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([0.26, -0.8, -0.06], [0.36, -0.1, 0.06], "#4a4e52", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([0.28, bottom + 0.35, -0.04], [0.34, -0.8, 0.04], "#9aa0a4", detail: true, shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.245, -0.45, -0.245], [0.245, -0.33, 0.245], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, 0, 0], "+y");
        return p.Stats(1100, 3500, 20, 6.0);
    }

    // ---- ladders

    private static Part Ladder(string name, double rise, string desc, string sockName, double[] sockAt, string sockDir)
    {
        var p = new Part(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var top = rise + 1.0;
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.25, 0.25 })
        {
            p.Add(Box([x - 0.03, 0.0, -0.18], [x + 0.03, top, -0.12], "#9aa0a4", detail: true, shape: Kw(("finish", "painted steel"))));
            for (var y = 0.6; y < top - 0.2; y += 1.2)
                p.Add(Box([x - 0.02, y, -0.12], [x + 0.02, y + 0.06, -0.01], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        for (var y = 0.3; y < rise + 0.1; y += 0.3)
            p.Add(Box([-0.22, y - 0.02, -0.165], [0.22, y + 0.02, -0.135], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.29, top - 0.06, -0.19], [0.29, top, -0.11], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock(sockName, sockAt, sockDir);
        p.Set(("ladder", rise));
        return p.Stats(60 + rise * 12, 400, 6, 3.0);
    }

    // ---- the weapons

    /// <summary>The railgun: a long breech in the spine's housing and a barrel between copper rails out over the bow. Fires along -z.</summary>
    private const double RailLen = 32.5;

    private static Part Railgun()
    {
        var p = new Part("Zero Day Railgun", Small, Section, "The Zero Day's railgun: one slug at great speed through anything in a line. Laid along the spine, so the ship is the aim.", role: "gun", tool: "welder", material: "steel", color: Armour, group: Group);
        // Local: the breech's back on the socket at the origin, the barrel forward along -z.
        p.Add(Box([-0.5, 0, -4.0], [0.5, 0.7, 0], Armour, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-0.3, 0.1, -RailLen], [0.3, 0.5, -4.0], "#3a3d40", shape: Kw(("finish", "dirty steel"))));
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([s * 0.3 - (s > 0 ? 0 : 0.08), 0.15, -RailLen + 0.1], [s * 0.3 + (s > 0 ? 0.08 : 0), 0.45, -4.1], "#b87333", detail: true, shape: Kw(("finish", "copper"))));
            p.Add(Box([s * 0.505 - 0.002, 0.3, -3.6], [s * 0.505 + 0.002, 0.42, -0.4], Glow, detail: true, shape: Kw(("glow", true))));
        }
        // Field coils every 3 m along the barrel (link blue when charged), a muzzle shroud.
        for (var z = -6.5; z > -RailLen + 1.5; z -= 3.0)
        {
            p.Add(Box([-0.42, 0.04, z - 0.25], [0.42, 0.56, z], Armour2, detail: true, shape: Kw(("finish", "plated steel"))));
            p.Add(Box([-0.425, 0.48, z - 0.18], [0.425, 0.5, z - 0.07], Glow, detail: true, shape: Kw(("glow", true))));
        }
        p.Add(Box([-0.4, 0.02, -RailLen - 0.4], [0.4, 0.58, -RailLen + 0.6], Armour2, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-0.2, 0.2, -RailLen - 0.42], [0.2, 0.4, -RailLen - 0.4], Glow, detail: true, shape: Kw(("glow", true))));
        foreach (var z in new[] { -1.2, -2.6 })
            p.Add(Box([-0.505, 0.0, z], [0.505, 0.705, z + 0.14], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("back", [0, 0, 0], "-y");
        p.Sock("ammo", [0, 0.7, -2.0], "+y");
        p.Set(("gun", Kw(("damage", 900), ("rpm", 6), ("spread", 0.04), ("range", 800), ("mag", 1), ("reload", 7.0), ("flags", Py.L("penetrating")), ("kick", 3500), ("fx", "cannon"), ("speed", 1100), ("soundOf", "Leviathan .50"), ("muzzle", Py.L(0, 0.3, Py.Round(-RailLen - 0.42, 4))))), ("centreSockets", false));
        return p.Stats(9000, 6000, 30, 12.0);
    }

    /// <summary>A point-defence gun: a low, angular mantlet and twin barrels, belt-fed from the box on its back.</summary>
    private static Part Pdc()
    {
        var p = new Part("Zero Day PDC", Small, Section, "A twin-barrel point-defence gun for a turret: very fast, short range. It shoots down torpedoes and missiles if the gunner is quick.", role: "gun", tool: "wrench", material: "steel", color: Armour, group: Group);
        p.Add(Box([-0.22, -0.15, -0.32], [0.22, 0.15, 0.3]));
        Prism(p, [[-0.22, 0.15, -0.32], [0.22, 0.15, -0.32], [0.22, 0.15, 0.3], [-0.22, 0.15, 0.3]], [[-0.16, 0.24, -0.2], [0.16, 0.24, -0.2], [0.16, 0.24, 0.26], [-0.16, 0.24, 0.26]], Armour2, "plated steel");
        foreach (var x in new[] { -0.08, 0.08 })
        {
            p.Add(Box([x - 0.03, -0.03, -1.3], [x + 0.03, 0.03, -0.32], "#2a2c2e", detail: true, shape: Kw(("finish", "steel"))));
            p.Add(Box([x - 0.045, -0.045, -1.38], [x + 0.045, 0.045, -1.26], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        }
        p.Add(Box([-0.225, 0.0, -0.1], [0.225, 0.04, 0.1], Blue, detail: true, shape: Kw(("finish", "painted plating"))));
        p.Add(Box([0.06, 0.18, -0.06], [0.14, 0.26, 0.04], Glow, detail: true, shape: Kw(("glow", true)))); // (its fire-control eye)
        p.Sock("bottom", [0, -0.15, 0], "-y");
        p.Sock("top", [0, 0.15, 0], "+y");
        p.Sock("back", [0, 0, 0.3], "+z");
        p.Set(("gun", Kw(("damage", 14), ("rpm", 1800), ("spread", 1.4), ("range", 240), ("mag", 400), ("reload", 2.5), ("kick", 6), ("fx", "bullet"), ("soundOf", "Bastille"), ("muzzle", Py.L(0, 0, -1.38)))), ("centreSockets", false));
        return p.Stats(200, 520, 14, 3.0);
    }

    /// <summary>A countermeasure launcher: a box of six tubes that throws flares out from the hull (missiles may go for them instead).</summary>
    private static Part Countermeasures()
    {
        var p = new Part("Zero Day Countermeasure Launcher", Small, Section, "Six tubes of flares on the spine: a press of the flare button throws a spread of them. Missiles and torpedoes coming in may go for one instead.", role: "gun", tool: "welder", material: "steel", color: Armour, group: Group);
        // Local: the tubes point along -z (the muzzle); it stands on its back (+z), so on a roof they point up.
        p.Add(Box([-0.3, -0.2, -0.45], [0.3, 0.2, 0.0], Armour, shape: Kw(("finish", "plated steel"))));
        for (var i = 0; i < 6; i++)
        {
            double x = -0.18 + (i % 3) * 0.18, y = i < 3 ? -0.08 : 0.08;
            p.Add(Box([x - 0.06, y - 0.06, -0.47], [x + 0.06, y + 0.06, -0.45], Frame, detail: true, shape: Kw(("finish", "soot"))));
        }
        p.Add(Box([-0.305, -0.205, -0.2], [0.305, 0.205, -0.12], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("back", [0, 0, 0], "+z");
        p.Set(("gun", Kw(("damage", 1), ("rpm", 240), ("spread", 14), ("range", 40), ("maxRange", 80), ("mag", 24), ("reload", 8), ("kick", 2), ("fx", "cannon"), ("soundOf", "Leviathan .50"), ("shell", "flare"), ("speed", 32), ("burn", 6), ("muzzle", Py.L(0, 0, -0.47)))), ("centreSockets", false));
        return p.Stats(90, 600, 10, 4.0);
    }

    // ---- fittings

    private static Part Reactor()
    {
        var p = new Part("Zero Day Reactor", Small, Section, "The Zero Day's fusion core: a tall armoured drum in a cage of coolant, 3 MW for the ship's circuits, no fuel. The blue is normal.", role: "generator", tool: "welder", material: "steel", color: Armour, group: Group);
        double[] O(double y, double r, int i, int n = 10)
        {
            var a = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * Math.Cos(a), 4), y, Py.Round(r * Math.Sin(a), 4)];
        }
        p.Add(Box([-0.9, 0, -0.9], [0.9, 0.25, 0.9], Frame, shape: Kw(("finish", "dirty steel"))));
        Prism(p, [.. Enumerable.Range(0, 10).Select(i => O(0.25, 0.75, i))], [.. Enumerable.Range(0, 10).Select(i => O(2.6, 0.75, i))], Armour, "plated steel");
        Prism(p, [.. Enumerable.Range(0, 10).Select(i => O(1.0, 0.77, i))], [.. Enumerable.Range(0, 10).Select(i => O(1.9, 0.77, i))], Glow, "glass", glow: true);
        foreach (var y in new[] { 0.95, 1.9 })
            Prism(p, [.. Enumerable.Range(0, 10).Select(i => O(y, 0.8, i))], [.. Enumerable.Range(0, 10).Select(i => O(y + 0.08, 0.8, i))], Frame, "dirty steel");
        foreach (var (x, z, col) in new[] { (0.82, 0.0, PipeBlue), (-0.82, 0.0, PipeBlue), (0.0, 0.82, PipeRed), (0.0, -0.82, PipeRed) })
            p.Add(Box([x - 0.06, 0.25, z - 0.06], [x + 0.06, 2.95, z + 0.06], col, detail: true, shape: Kw(("finish", "painted steel"))));
        p.Add(Box([-0.6, 0.25, -0.6], [0.6, 2.6, 0.6], hidden: true));
        p.Add(Box([-0.84, 0.25, -0.86], [-0.5, 0.5, -0.82], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 3000), ("spool", 10), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.12, 0.9)), ("face", "+z")))));
        return p.Stats(5000, 4000, 30, 12.0);
    }

    private static Part FlightComputer()
    {
        var p = new Part("Zero Day Flight Computer", Small, Section, "The Zero Day's flight computer: a flight stabilizer with a capital-ship gyro in a cabinet. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: Console, group: Group);
        p.Add(Box([-0.4, 0, -0.3], [0.4, 1.2, 0.3], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.35, 0.8, -0.318], [0.35, 1.1, -0.3], Screen, detail: true, shape: Kw(("glow", true))));
        for (var i = 0; i < 5; i++)
            p.Add(Box([-0.3 + i * 0.14, 0.65, -0.318], [-0.25 + i * 0.14, 0.7, -0.302], i % 2 == 0 ? "#40e070" : Amber, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.05, 1.201, -0.25], [0.05, 1.215, 0.05], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([-0.12, 1.201, -0.25], [0.12, 1.215, -0.17], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 30000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(400, 800, 8, 3.0);
    }

    // ---- the blueprint

    /// <summary>The torpedo: a designed weapon (Source/Constructs/OrdnanceDesign.cs) 50 cm across, about 5.3 m long: a radar seeker (flares
    /// don't fool it), an impact fuse, a double HE warhead, reaction jets to steer anywhere, a solid motor.</summary>
    public const string Torpedo = "Ordnance:M50:Radar Seeker+Impact Fuse+Warhead HE*2+Reaction Jets+Solid Motor";

    /// <summary>The rotary magazine in each bay: six stations round a drum, 50 cm by 5.5 m (ModularRack's run-time shape).</summary>
    public const string Magazine = "Ord Rack (1x6 drum 500x5500)";

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static int LampAt(BuildingParts.Blueprint bp, int on, string sock, string colour = "#e8f4ff")
    {
        var s = bp.Hang("Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = bp.Hang("Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    private static int Turret(BuildingParts.Blueprint bp, int pod)
    {
        var tt = bp.Hang("Turntable", pod, "top", "base");
        var hinge = bp.Hang("Motor Hinge", tt, "top", "base");
        var gun = bp.Hang("Zero Day PDC", hinge, "leaf", "bottom");
        bp.Hang("Ammo Box", gun, "back", "front");
        return gun;
    }

    private static int Door(BuildingParts.Blueprint bp, int on, string sock) => Paint(bp, bp.Hang("Bulkhead Door 1.5x2.25 m", on, sock, "hang"), Armour, "plated steel");

    private static PyDict Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint("Zero Day Frigate", "The IT Department's torpedo frigate: two decks the length of a sleek armoured hull, a rotary torpedo bay in each flank, a spinal railgun, four point-defence turrets, flare launchers and a drive array the width of the stern.", defs, maxSteps: 256);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var keel = bp.First("Zero Day Keel");
        var bow = bp.Put("Zero Day Bow", zero, 0);
        var lf = bp.Put("Zero Day Lower Deck Fore", zero, 0);
        var la = bp.Put("Zero Day Lower Deck Aft", zero, 0);
        var stern = bp.Put("Zero Day Stern", zero, 0);
        var uf = bp.Put("Zero Day Upper Deck Fore", zero, 0);
        var ua = bp.Put("Zero Day Upper Deck Aft", zero, 0);
        var spine = bp.Put("Zero Day Spine", zero, 0);
        var bayL = bp.Put("Zero Day Torpedo Bay L", zero, 0);
        var bayR = bp.Put("Zero Day Torpedo Bay R", zero, 0);
        // The drive, the jets, the legs.
        bp.Hang("Zero Day Drive Array", stern, "drive", "front");
        foreach (var n in new[] { "liftFL", "liftFR", "liftBL", "liftBR" })
            bp.Hang("Zero Day Lift Jet", keel, n, "top");
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            bp.Hang("Zero Day Landing Leg", keel, n, "top");
        foreach (var n in new[] { "retroL", "retroR" })
            bp.Hang("Zero Day Retro Jet", bow, n, "top");
        // Ladders: the boarding ladder up the shaft, two between the decks.
        bp.Hang("Zero Day Boarding Ladder", la, "boarding", "hull");
        bp.Hang("Zero Day Ladder", lf, "ladder", "bottom");
        bp.Hang("Zero Day Ladder", la, "ladder", "bottom");
        // Lower deck fore: the server room (the IT Department's own), the armoury, the torpedo room.
        for (var i = 0; i < 4; i++)
        {
            bp.Hang("Server Rack", lf, $"serverL{i}", "foot00");
            bp.Hang("Server Rack", lf, $"serverR{i}", "foot00");
        }
        bp.Hang("Computer", lf, "computer", "foot00");
        bp.Hang("Office Plant", lf, "plant", "base");
        bp.Hang("Requisition Crate", lf, "crate0", "-y");
        bp.Hang("Requisition Crate", lf, "crate1", "-y");
        for (var i = 0; i < 3; i++)
            Paint(bp, bp.Hang("Locker", lf, $"locker{i}", "wall"), Armour, "worn paint");
        bp.Hang("Tool Rack", lf, "toolRack", "back");
        bp.Hang("Ammo Crate", lf, "ammo0", "bottom");
        bp.Hang("Ammo Crate", lf, "ammo1", "bottom");
        bp.Hang("Ammo Crate", lf, "warheads0", "bottom");
        bp.Hang("Ammo Crate", lf, "warheads1", "bottom");
        foreach (var d in new[] { "doorServer", "doorTorpedo", "doorBayL", "doorBayR" })
            Door(bp, lf, d);
        var fireL = bp.Hang("Button", lf, "fireL", "-x");
        var fireR = bp.Hang("Button", lf, "fireR", "+x");
        Switch(bp, lf, "switchServer", LampAt(bp, lf, "lampServer", "#a0d0ff"));
        Switch(bp, lf, "switchFore", LampAt(bp, lf, "lampArmouryL"), LampAt(bp, lf, "lampArmouryR"));
        Switch(bp, lf, "switchTorp", LampAt(bp, lf, "lampTorpL", "#ffd8a0"), LampAt(bp, lf, "lampTorpR", "#ffd8a0"));
        foreach (var bay in new[] { bayL, bayR })
            Switch(bp, bay, "switch", LampAt(bp, bay, "lamp", "#ff7050"));
        // Lower deck aft: suits, the airlock, engineering.
        for (var i = 0; i < 4; i++)
            Paint(bp, bp.Hang("Locker", la, $"suit{i}", "wall"), Hazard, "worn paint");
        Paint(bp, bp.Hang("Locker", la, "airlockLocker", "wall"), Hazard, "worn paint");
        foreach (var d in new[] { "doorBayL", "doorBayR", "doorAirlock", "doorEngine" })
            Door(bp, la, d);
        bp.Hang("Zero Day Reactor", la, "reactor", "bottom");
        bp.Hang("Power Board", la, "board", "back");
        bp.Hang("Engineer's Panel", la, "panel", "back");
        for (var i = 0; i < 3; i++)
            bp.Hang("Battery", la, $"battery{i}", "-y");
        bp.Hang("Pipe Bank", la, "pipes", "back");
        bp.Hang("Tool Rack", la, "toolRack", "back");
        bp.Hang("Requisition Crate", la, "crate0", "-y");
        bp.Hang("Requisition Crate", la, "crate1", "-y");
        Switch(bp, la, "switchAirlock", LampAt(bp, la, "lampAirlock", "#ff5040"), LampAt(bp, la, "lampSuit"));
        Switch(bp, la, "switchEng", LampAt(bp, la, "lampEngL", "#a0d0ff"), LampAt(bp, la, "lampEngR", "#a0d0ff")); 
        Switch(bp, la, "switchTorp", LampAt(bp, la, "lampTorpL", "#ffd8a0"), LampAt(bp, la, "lampTorpR", "#ffd8a0"));
        // Upper deck fore: the bridge, ops, the cabins.
        var pilot = bp.Hang("Flight Couch", uf, "pilot", "bottom");
        bp.Hang("Flight Couch", uf, "copilot", "bottom");
        var fc = bp.Hang("Zero Day Flight Computer", uf, "computer", "bottom");
        bp.Wires.Add((pilot, fc));
        var railModule = bp.Hang("Turret Module", uf, "gunModule", "-y");
        bp.Hang("Chart Table", uf, "chart", "back");
        Paint(bp, bp.Hang("Locker", uf, "bridgeLocker", "wall"), Armour, "worn paint");
        bp.Hang("Chart Table", uf, "plot", "bottom");
        Paint(bp, bp.Hang("Crash Couch", uf, "couchA", "wall"), "#2c3e58", "fabric");
        Paint(bp, bp.Hang("Locker", uf, "lockerA", "wall"), Blue, "worn paint");
        Paint(bp, bp.Hang("Crash Couch", uf, "couchCaptain", "wall"), "#2c3e58", "fabric");
        bp.Hang("Steel Desk", uf, "deskCaptain", "wall");
        Paint(bp, bp.Hang("Locker", uf, "lockerCaptain", "wall"), Blue, "worn paint");
        foreach (var d in new[] { "doorBridge", "doorOps", "doorCabinA", "doorCaptain" })
            Door(bp, uf, d);
        var flareButton = bp.Hang("Button", uf, "flares", "+z");
        Switch(bp, uf, "switchBridge", LampAt(bp, uf, "lampBridgeL", "#ff6050"), LampAt(bp, uf, "lampBridgeR", "#ff6050"));
        Switch(bp, uf, "switchOps", LampAt(bp, uf, "lampOpsL", "#a0c8ff"), LampAt(bp, uf, "lampOpsR", "#a0c8ff"));
        Switch(bp, uf, "switchCabins", LampAt(bp, uf, "lampCabinA", "#ffd8a0"), LampAt(bp, uf, "lampCaptain", "#ffd8a0"));
        // Upper deck aft: the crew cabin, the head, the med bay, the mess.
        Paint(bp, bp.Hang("Crash Couch", ua, "couchB0", "wall"), "#2c3e58", "fabric");
        Paint(bp, bp.Hang("Locker", ua, "lockerB", "wall"), Blue, "worn paint");
        bp.Hang("Steel Desk", ua, "deskB", "wall");
        bp.Hang("Ship Toilet", ua, "toilet", "wall");
        bp.Hang("Ship Basin", ua, "basin", "wall");
        Paint(bp, bp.Hang("Shower Stall", ua, "shower", "wall"), MedWhite, "gloss paint");
        bp.Hang("Medical Station", ua, "med0", "back");
        bp.Hang("Medical Station", ua, "med1", "back");
        bp.Hang("Medicine Cabinet", ua, "cabinet", "back");
        Paint(bp, bp.Hang("Locker", ua, "medLocker", "wall"), MedWhite, "gloss paint");
        bp.Hang("Galley Stove", ua, "stove", "back");
        bp.Hang("Galley Coffee Station", ua, "coffee", "wall");
        Paint(bp, bp.Hang("Locker", ua, "messLocker", "wall"), "#5c646a", "worn paint");
        var table = bp.HangTurned("Mess Table", ua, "table", "bottom", BuildingParts.RY90);
        _ = table;
        foreach (var d in new[] { "doorCabinB", "doorHead", "doorMed", "doorMess" })
            Door(bp, ua, d);
        Switch(bp, ua, "switchCrew", LampAt(bp, ua, "lampCabinB", "#ffd8a0"), LampAt(bp, ua, "lampHead"));
        Switch(bp, ua, "switchMed", LampAt(bp, ua, "lampMed", "#f4fbff"));
        Switch(bp, ua, "switchMess", LampAt(bp, ua, "lampMessL", "#ffe0b0"), LampAt(bp, ua, "lampMessR", "#ffe0b0"));
        // The turrets: two barbettes forward worked from ops, two aft worked from the mess.
        var pods = new[] { bp.Hang("Zero Day Turret Barbette", uf, "podL", "hull"), bp.Hang("Zero Day Turret Barbette", uf, "podR", "hull"), bp.Hang("Zero Day Turret Barbette", ua, "podL", "hull"), bp.Hang("Zero Day Turret Barbette", ua, "podR", "hull") };
        var guns = pods.Select(pd => Turret(bp, pd)).ToArray();
        foreach (var (deck, pair) in new[] { (uf, new[] { guns[0], guns[1] }), (ua, new[] { guns[2], guns[3] }) })
        {
            var console = bp.Hang("Gunnery Console", deck, "gun", "bottom");
            var seat = bp.Hang("Flight Couch", deck, "gunSeat", "bottom");
            var module = bp.Hang("Turret Module", console, "module", "-x");
            bp.Wires.Add((seat, module));
            foreach (var g in pair)
                bp.Wires.Add((module, g));
        }
        // The railgun along the spine, fired by the pilot; its slug box on the breech.
        var rail = bp.Hang("Zero Day Railgun", spine, "railgun", "back");
        bp.Hang("Ammo Box", rail, "ammo", "bottom");
        bp.Wires.Add((pilot, railModule));
        bp.Wires.Add((railModule, rail));
        // Flare launchers behind the bridge, on ops' flare button.
        foreach (var n in new[] { "flaresFL", "flaresFR", "flaresBL", "flaresBR" })
            bp.Wires.Add((flareButton, bp.Hang("Zero Day Countermeasure Launcher", spine, n, "back")));
        var json = bp.Json();
        // The torpedo magazines (a rack's shape is made by the game, so its steps go last, by socket name) and their torpedoes; each
        // bay's fire button lets one go.
        var steps = (List<object?>)json["steps"]!;
        var wires = (List<object?>)json["wires"]!;
        foreach (var (bay, fire) in new[] { (bayL, fireL), (bayR, fireR) })
        {
            steps.Add(Kw(("part", Magazine), ("on", bay), ("socket", "magazine"), ("by", "top")));
            var mag = steps.Count - 1;
            for (var i = 0; i < 6; i++)
                steps.Add(Kw(("part", Torpedo), ("on", mag), ("socket", $"lug{i}"), ("by", "lug")));
            wires.Add(new List<object?> { fire, mag });
        }
        return json;
    }

    public void Make(PartOutput o)
    {
        List<Part> parts =
        [
            Keel(), Bow(), SternPart(), LowerFore(), LowerAft(), UpperFore(), UpperAft(), Spine(), Sponson(-1), Sponson(1), Barbette(), Drive(),
            Jet("Zero Day Lift Jet", $"A lift jet under the Zero Day's keel: {LiftForce / 1e6:0.0} MN straight down.", LiftForce, 1.4),
            Jet("Zero Day Retro Jet", $"A retro jet in the Zero Day's cheek: {RetroForce / 1e6:0.0} MN forward, to slow it down.", RetroForce, 1.0),
            Leg(),
            Ladder("Zero Day Ladder", Ft1 - Ft0, "A ladder between the Zero Day's decks: F climbs it.", "bottom", [0, 0, -0.095], "-y"),
            Ladder("Zero Day Boarding Ladder", Ft0 - Ground, "The Zero Day's boarding ladder, from the ground up the shaft into the airlock: F climbs it.", "hull", [0, Ft0 - Ground - 0.125, -0.005], "+z"),
            Railgun(), Pdc(), Countermeasures(), Reactor(), FlightComputer(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/ZeroDayFrigate.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
    }
}
