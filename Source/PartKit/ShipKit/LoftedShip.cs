using CallOfSlop.PartKit.Generators;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.ShipKit;

/// <summary>
/// The ship kit (docs/sandbox/constructs/ship-kit.md; Karl, 2026-11-21: one kit for new ships, "not generic, cookie-cutter
/// replacements and snap-ons"). The code every lofted ship generator had its own copy of (the Satchel's, the Packet's, the Docket's
/// ...), once: a hull lofted through stations of a five-point half profile, the facets, slabs, chines and ribs drawn on it, rooms
/// fitted inside it, each big part dealt out into welded pieces (SplitDeck, SplitBands), and the welds and the place order between
/// them (Connect, PlaceOrder). A ship derives from it, sets its hull, stations and style, and draws its own parts with these.
///
/// What the kit does not hold is any ship's shape: the hull, the rooms, the dressing and the look of every fitting come from the ship.
/// Fittings (LoftedShip.Fittings.cs) are drawn from a spec each ship writes for itself (a jet's nozzle profile and shroud, a plant's
/// cells, a console's screens), in its faction's style, under its own name.
///
/// Ship metres: x across (the centreline at Cx), y up from the deck's underside, z along the ship (forward is -z). P() moves them by
/// half a cell into a part's frame.
/// </summary>
public abstract partial class LoftedShip
{
    protected const double H = 0.125; // half a cell
    protected const double T = 0.04; // a drawn plate's thickness
    protected const double FacetStagger = 0.006, FacetTrim = 0.0015; // (odd facets 6 mm aft; each stops 1.5 mm short of its band's ends)

    protected readonly record struct Sec(double Z, (double H, double Y)[] Pts);
    protected readonly record struct Rect(double X0, double Z0, double X1, double Z1);
    /// <summary>A gap in a wall: from A, W wide, open from sill to head over the floor (a doorway: sill 0).</summary>
    protected readonly record struct Gap(double A, double W, double Sill, double Head, bool Glass = false);

    /// <summary>The ship's name as its pieces start ("Folio"), the gallery's section, the paint.</summary>
    protected readonly string Prefix;
    protected readonly string Section;
    protected readonly ShipStyle Style;
    protected const string Group = "Ship";
    protected double Cx = 0.0;
    protected double DoorH = 2.25;
    /// <summary>The hull's stations (bow to stern), the bands the hull pieces are cut at, the ring frames standing proud of the skin.</summary>
    protected Sec[] Hull = [];
    protected double[] Bands = [];
    protected double[] RibZ = [];

    protected LoftedShip(string prefix, string section, ShipStyle style)
    {
        Prefix = prefix;
        Section = section;
        Style = style;
        WallCol = style.Wall;
        DadoCol = style.WallLow;
    }

    /// <summary>What errors are prefixed with.</summary>
    protected string Tag => Prefix.ToLowerInvariant() + "_parts";

    // ---- the paint (the style's, by the names the generators have always used)

    protected string Skin => Style.Skin;
    protected string Skin2 => Style.Skin2;
    protected string Armour => Style.Armour;
    protected string Armour2 => Style.Armour2;
    protected string Bare => Style.Bare;
    protected string Frame => Style.Frame;
    protected string Dark => Style.Dark;
    protected string Hazard => Style.Hazard;
    protected string Red => Style.Red;
    protected string Glow => Style.Glow;
    protected string Glass => Style.Glass;
    protected string Link => Style.Link;
    protected string Paper => Style.Paper;
    protected string Green => Style.Green;
    protected string Wall => Style.Wall;
    protected string WallLow => Style.WallLow;
    protected string WallTrim => Style.WallTrim;
    protected string Deck => Style.Deck;
    protected string Grate => Style.Grate;
    protected string Ceil => Style.Ceil;
    protected string Latch => Style.Latch;

    /// <summary>The walls WallRun draws with (a room may change them before its walls).</summary>
    protected string WallCol, DadoCol;

    // ---- geometry

    protected static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    protected static double[] Sh(double[] q) => P(q[0], q[1], q[2]);

    protected static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];

    protected static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    protected static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

    protected static double[] Unit(double[] a)
    {
        var l = Math.Sqrt(Dot(a, a));
        return l < 1e-12 ? [0, 1, 0] : [a[0] / l, a[1] / l, a[2] / l];
    }

    protected static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    /// <summary>A flat plate through the points (a convex polygon), thick about them: a "hull" shape, drawn only.</summary>
    protected static void Plate(Part p, double[][] pts, string color, string finish = "painted plating", bool glass = false, bool glow = false, double thick = T)
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

    /// <summary>A quad as one plate when flat (or a little twisted), else as two triangles.</summary>
    protected static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating", bool glass = false)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-10)
        {
            Plate(p, [a, c, d], color, finish, glass);
            return;
        }
        if (Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.08)
            Plate(p, [a, b, c, d], color, finish, glass);
        else
        {
            Plate(p, [a, b, c], color, finish, glass);
            Plate(p, [a, c, d], color, finish, glass);
        }
    }

    /// <summary>A convex solid between two outlines (drawn only).</summary>
    protected static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false)
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

    protected static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null)
    {
        var shape = finish == null ? null : Kw(("finish", finish));
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: shape));
    }

    protected static void Detail(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string? finish = null, bool glow = false)
    {
        var shape = finish == null && !glow ? null : new PyDict();
        if (finish != null)
            shape!["finish"] = finish;
        if (glow)
            shape!["glow"] = true;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    protected static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        if (Math.Abs(x1 - x0) < 0.05 || Math.Abs(y1 - y0) < 0.05 || Math.Abs(z1 - z0) < 0.05)
            return;
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));
    }

    protected static void Sock(Part p, string name, double x, double y, double z, string dir) => p.Sock(name, P(x, y, z), dir);

    /// <summary>A flat ring of n points round a vertical axis at (cx, cz), radius r, at height y (shifted into the part's frame).</summary>
    protected static double[][] Ngon(double cx, double y, double cz, double r, int n, double phase = 0) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = phase + Math.PI / n + i * 2 * Math.PI / n;
        return P(cx + r * Math.Cos(a), y, cz + r * Math.Sin(a));
    })];

    /// <summary>A ring of n points round axis (0 x, 1 y, 2 z) through c, radius r, at a along the axis; map turns each point into the part's frame.</summary>
    protected static double[][] RingAx(double[] c, int axis, double r, double a, int n, Func<double[], double[]> map) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var an = Math.PI / n + i * 2 * Math.PI / n;
        int u = (axis + 1) % 3, v = (axis + 2) % 3;
        var q = (double[])c.Clone();
        q[axis] = a;
        q[u] += r * Math.Cos(an);
        q[v] += r * Math.Sin(an);
        return map(q);
    })];

    /// <summary>A drawn tank: a cylinder of radius r along axis from a0 to a1, its ends domed over dome metres, hoop bands round it.</summary>
    protected static void DrawTank(Part p, double[] c, int axis, double r, double a0, double a1, double dome, int hoops, string col, string band, Func<double[], double[]> map)
    {
        const int n = 16;
        double[][] R(double rr, double a) => RingAx(c, axis, rr, a, n, map);
        Prism(p, R(r * 0.55, a0), R(r, a0 + dome), col, "worn paint");
        Prism(p, R(r, a0 + dome + 0.003), R(r, a1 - dome - 0.003), col, "worn paint");
        Prism(p, R(r, a1 - dome), R(r * 0.55, a1), col, "worn paint");
        for (var i = 0; i < hoops; i++)
        {
            var a = a0 + dome + 0.4 + i * (a1 - a0 - 2 * dome - 0.8) / Math.Max(1, hoops - 1);
            Prism(p, R(r + 0.025, a - 0.06), R(r + 0.025, a + 0.06), band, "dirty steel");
        }
    }

    /// <summary>A ring of n points round an axis along z at (cx, cy), radius rx by ry, at z (unshifted part-local metres).</summary>
    protected static double[][] RingZ(double cx, double cy, double rx, double ry, double z, int n) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = Math.PI / n + i * 2 * Math.PI / n;
        return new[] { Py.Round(cx + rx * Math.Cos(a), 4), Py.Round(cy + ry * Math.Sin(a), 4), z };
    })];

    // ---- the loft: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge)

    /// <summary>The half profile at z, blended between the two stations either side.</summary>
    protected (double H, double Y)[] At(double z)
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
    protected double SkinHalf(double z, double y)
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
    protected void MustFit(string what, double z, double hx, double y)
    {
        var s = SkinHalf(z, y);
        if (s < hx + 0.06)
            throw new InvalidOperationException($"{Tag}: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
    }

    /// <summary>The x of facet k's surface (side s) at height y and station z (between its two profile points).</summary>
    protected double FacetX(int k, int s, double z, double y)
    {
        var q = At(z);
        var (a, b) = (q[k - 1], q[k]);
        return Cx + s * (a.H + (b.H - a.H) * (y - a.Y) / (b.Y - a.Y));
    }

    /// <summary>A facet's two edge points at z (ship metres, unshifted). k: 0 the keel (port to starboard), 1..4 the side facets
    /// (P[k-1] to P[k]) on side s, 5 the spine (starboard to port).</summary>
    protected (double[] A, double[] B) Edge(int k, int s, double z)
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

    /// <summary>A point on facet k (side s) between za and zb: u along z, v across from edge A to B.</summary>
    protected double[] OnFacet(int k, int s, double za, double zb, double u, double v)
    {
        var (a0, b0) = Edge(k, s, za);
        var (a1, b1) = Edge(k, s, zb);
        return Lerp(Lerp(a0, b0, v), Lerp(a1, b1, v), u);
    }

    /// <summary>The facet's outward normal at its middle (away from the hull's axis).</summary>
    protected double[] FacetNormal(int k, int s, double za, double zb)
    {
        var c = OnFacet(k, s, za, zb, 0.5, 0.5);
        var n = Unit(Cross(Sub(OnFacet(k, s, za, zb, 0.5, 1), OnFacet(k, s, za, zb, 0.5, 0)), Sub(OnFacet(k, s, za, zb, 1, 0.5), OnFacet(k, s, za, zb, 0, 0.5))));
        double[] axis = [Cx, (At(c[2])[0].Y + At(c[2])[4].Y) / 2, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>The skin of facet k on side s from za to zb. The odd facets sit 6 mm aft of the even ones and the chine rails 3 mm, so
    /// where neighbours' plates cross at an edge their ends never share a plane (z-fighting.md).</summary>
    protected void Facet(Part p, int k, int s, double za, double zb, string color, string finish = "painted plating", bool glass = false)
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At2(double u, double v) { var q = OnFacet(k, s, za, zb, u, v); return Sh([q[0], q[1], q[2] + dz + (u < 0.5 ? FacetTrim : -FacetTrim)]); }
        Quad(p, At2(0, 0), At2(0, 1), At2(1, 1), At2(1, 0), color, finish, glass);
    }

    /// <summary>A bevelled slab on facet k from (u0, v0) to (u1, v1), standing d0..d1 out, its face drawn in by bev metres.</summary>
    protected void Slab(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d0, double d1, double bev, string color, string finish = "plated steel")
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

    /// <summary>A flat strip on a facet (a seam, a stripe, a light, a window), d out from it.</summary>
    protected void FStrip(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d, string color, string finish = "painted plating", bool glow = false)
    {
        var n = FacetNormal(k, s, za, zb);
        double[] Q(double u, double v)
        {
            var q = OnFacet(k, s, za, zb, u, v);
            return Sh([q[0] + n[0] * d, q[1] + n[1] * d, q[2] + n[2] * d]);
        }
        Plate(p, [Q(u0, v0), Q(u1, v0), Q(u1, v1), Q(u0, v1)], color, finish, glow: glow);
    }

    /// <summary>A chine rail: a faceted bar along profile point i (1..4) on side s from za to zb, w wide each way and h proud.</summary>
    protected void Chine(Part p, int i, int s, double za, double zb, double w, double h, string color) => Chine(p, i, s, za, zb, w, h, w, h, color);

    /// <summary>A chine rail whose section changes from (w, h) at za to (w2, h2) at zb.</summary>
    protected void Chine(Part p, int i, int s, double za, double zb, double w, double h, double w2, double h2, string color)
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

    /// <summary>The bands (pairs of stations) from za to zb.</summary>
    protected IEnumerable<(int I, double A, double B)> BandsIn(double za, double zb) =>
        Enumerable.Range(0, Bands.Length - 1).Where(i => Bands[i] >= za - 1e-6 && Bands[i + 1] <= zb + 1e-6).Select(i => (i, Bands[i], Bands[i + 1]));

    /// <summary>A ring frame's band across facet k (side s) at z: a bevelled slab standing out of the skin, odd facets' ends 6 mm wider.</summary>
    protected void Rib(Part p, int k, int s, double z, string? color = null, double half = 0.22, double proud = 0.26)
    {
        var e = k % 2 == 1 ? FacetStagger : 0;
        Slab(p, k, s, z - half - e, z + half + e, 0, 1, 0, 1, 0.03, proud, 0.06, color ?? Frame, "dirty steel");
    }

    /// <summary>The ribs a part draws on its facets ks between za (included) and zb.</summary>
    protected void Ribs(Part p, double za, double zb, params int[] ks)
    {
        foreach (var z in RibZ)
            if (z >= za - 1e-6 && z < zb - 1e-6)
                foreach (var k in ks)
                    foreach (var s in k is 0 or 5 ? [1] : new[] { -1, 1 })
                        Rib(p, k, s, z);
    }

    /// <summary>Whether a rib stands at z (so a band's plates keep clear of it).</summary>
    protected bool RibAt(double z) => RibZ.Any(r => Math.Abs(r - z) < 1e-6);

    /// <summary>Hidden boxes for the void between the skin and the rooms, both sides: x from xin (the rooms' outer face; 0: one box
    /// across the middle) out to the skin less a margin, its ends trimmed by ta and tb (0: flush, to touch the next part).</summary>
    protected void VoidT(Part p, double za, double zb, double ya, double yb, double xin, double ta = 0.004, double tb = 0.004)
    {
        var xo = Math.Floor((new[] { SkinHalf(za, ya), SkinHalf(za, yb), SkinHalf(zb, ya), SkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        za += ta;
        zb -= tb;
        if (xin <= 0)
            Hidden(p, Cx - xo, ya, za, Cx + xo, yb, zb);
        else
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * xin, ya, za, Cx + s * xo, yb, zb);
    }

    /// <summary>A pad for an attitude jet: a square solid w wide standing proud metres off the surface point, sunk 11 cm into the
    /// hull; its outer face takes the socket.</summary>
    protected void RcsPad(Part p, string name, double x, double y, double z, string dir, double proud, double w = 0.6)
    {
        var s = dir[0] == '-' ? -1 : 1;
        var axis = dir[1] - 'x';
        double[] at = [x, y, z];
        double[] lo = [x - w / 2, y - w / 2, z - w / 2], hi = [x + w / 2, y + w / 2, z + w / 2];
        lo[axis] = Math.Min(at[axis] - s * 0.11, at[axis] + s * proud);
        hi[axis] = Math.Max(at[axis] - s * 0.11, at[axis] + s * proud);
        at[axis] += s * proud;
        Solid(p, lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], Frame, "dirty steel");
        Sock(p, name, at[0], at[1], at[2], dir);
    }

    /// <summary>A mount under the keel's flat (bottom y): a square solid half wide, sunk 11 cm into the hull and standing proud; its
    /// underside takes the socket.</summary>
    protected void MountPad(Part p, string name, double x, double y, double z, double half, double proud = 0.1)
    {
        Solid(p, x - half, y - proud, z - half, x + half, y + 0.11, z + half, Frame, "dirty steel");
        Sock(p, name, x, y - proud, z, "-y");
    }

    /// <summary>A big part of the ship's own (a deck, the keel, a hull piece), in its section.</summary>
    protected Part Big(string name, string desc, string role = "frame", string? color = null) =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color ?? Skin, group: Group);
}
