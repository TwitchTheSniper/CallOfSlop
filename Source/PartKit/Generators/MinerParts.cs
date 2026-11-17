using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Magpie, a mining ship: the first big Survival goal (Karl, 2026-11-15: a mining ship paired with a small refinery station or a
/// ground refinery, not a refinery ship). It sets down on its legs by a deposit and lowers its stern ramp; its crew carry the drills out
/// of their docks, dig, and tip the ore into two hoppers on its flanks; then it flies the load to a refinery (docs/sandbox/constructs/miner.md).
/// Built as the Zero Day is (Karl: the Halberd and the rust bucket are the bad examples, the Zero Day the good one): a hull lofted
/// through stations first, two decks running its length fitted inside it, function on the outside.
///
/// Every big part has its origin at the body's; the generator works in ship metres (x across, port to starboard, the centreline at Cx;
/// y up from the lower deck's underside; z from the bow back, so forward is -z) and moves them by half a cell. Each big part draws its
/// own band of the hull's facets and the hidden boxes behind them; solid boxes of different parts never overlap, and wherever two of
/// them touch they get a facing socket pair (Connect), so the game welds them and a hit takes a section, not the ship.
/// </summary>
public sealed class MinerParts : IPartGenerator
{
    public string Name => "miner_parts";

    private const string Out = "Constructs/Parts/Magpie";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Magpie miner";
    private const string ShipName = "Magpie Mining Ship";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: a working ship. Weathered grey skin under mining-yellow plate (worn), a dark frame, hazard orange on what moves and
    // where people go in, primer red where the paint has gone, warm work lights. Inside: pale green-grey panels over a dark dado.
    private const string Skin = "#8f8c82", Skin2 = "#7b7870", Yellow = "#c8962a", Yellow2 = "#a97c24", Frame = "#26241f", Steel = "#5a5d5c",
        Hazard = "#e0a020", Orange = "#d8661c", Primer = "#7a3b2c", Glow = "#ffd890", Green = "#50d070", Red = "#e0402c", Glass = "#7f9ea8",
        Wall = "#a9ad9f", WallLow = "#4f5a52", WallTrim = "#2a2e2b", Deck = "#4a4d4b", Grate = "#3a3c3a", Ceil = "#8c918a", Console = "#22262b",
        Screen = "#78d8a0", Amber = "#f0a830", PipeRed = "#a8402c", PipeBlue = "#2f6a9a", PipeYellow = "#c8a030";

    // ---- the plan

    private const double Cx = 6.0; // the centreline
    private const double Ft0 = 0.25, Ceil0 = 3.25, Ft1 = 3.5, Ceil1 = 6.5, Roof = 6.75; // floor tops and ceilings of the two decks
    private const double Ground = -2.1; // where the legs stand: a lowered drill's bit is 10 cm over it
    private const double ZFront = 5.0, ZWork = 10.5, ZDrill = 18.0, ZAft = 26.0, ZT = 26.25; // the rooms' front, the drill bay's ends, the back
    private const double ZUp = 15.0, ZBridge = 8.75; // where the upper deck's two parts meet; the bridge's back
    private const double Lw = 4.2, LwF = 3.6, Uw = 3.3, UwF = 2.4; // the rooms' inner half widths: lower, the workshop, upper, the bridge
    private const double DoorH = 2.25;

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the spine's edge)

    private readonly record struct Sec(double Z, (double H, double Y)[] Pts);

    private static readonly (double, double)[] Mid = [(2.6, -1.3), (4.9, 0.0), (5.4, 3.0), (4.7, 6.2), (3.0, 7.4)];

    private const double Zt = -4.0, Zc = 0.5; // the bow's tip, the brow

    private static readonly Sec[] Hull =
    [
        new(Zt, [(0.5, 0.6), (1.0, 1.2), (1.3, 2.6), (1.1, 3.8), (0.6, 4.3)]),
        new(Zc, [(1.6, -0.5), (3.4, 0.2), (4.2, 2.8), (3.8, 5.4), (2.0, 6.3)]),
        new(5.0, [(2.4, -1.1), (4.6, 0.0), (5.2, 3.0), (4.5, 6.1), (2.8, 7.3)]),
        new(7.0, Mid), new(23.0, Mid),
        new(ZT, [(2.3, -1.0), (4.8, 0.0), (5.1, 3.1), (4.4, 6.1), (3.1, 7.3)]),
    ];

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
            throw new InvalidOperationException($"miner_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
    }

    /// <summary>A facet's two edge points at z. k: 0 the keel (port to starboard), 1..4 the side facets (P[k-1] to P[k]) on side s, 5 the spine.</summary>
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

    private const double FacetStagger = 0.006, FacetTrim = 0.0015; // (odd facets 6 mm aft, each 1.5 mm short of its band: no shared planes)

    /// <summary>The skin of facet k on side s from za to zb.</summary>
    private static void Facet(Part p, int k, int s, double za, double zb, string color, string finish = "painted plating")
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At2(double u, double v) { var q = OnFacet(k, s, za, zb, u, v); return Sh([q[0], q[1], q[2] + dz + (u < 0.5 ? FacetTrim : -FacetTrim)]); }
        Quad(p, At2(0, 0), At2(0, 1), At2(1, 1), At2(1, 0), color, finish);
    }

    /// <summary>A bevelled plate on facet k from (u0, v0) to (u1, v1), standing d0..d1 out, its face drawn in by bev metres.</summary>
    private static void Slab(Part p, int k, int s, double za, double zb, double u0, double u1, double v0, double v1, double d0, double d1, double bev, string color, string finish = "worn paint")
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

    /// <summary>A chine rail: a faceted bar along profile point i (1..4) on side s from za to zb, w wide each way and h proud.</summary>
    private static void Chine(Part p, int i, int s, double za, double zb, double w, double h, string color) => Chine(p, i, s, za, zb, w, h, w, h, color);

    /// <summary>A shoulder block: a big chine rail whose ends rake down to a smaller section over rake metres.</summary>
    private static void Shoulder(Part p, int i, int s, double za, double zb, double w, double h, double rake, string color, string color2)
    {
        Chine(p, i, s, za, za + rake, w * 0.55, h * 0.3, w, h, color);
        Chine(p, i, s, za + rake + 0.004, zb - rake - 0.004, w, h, w, h, color2);
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
            q[2] += FacetStagger / 2 + FacetTrim;
        foreach (var q in rb)
            q[2] += FacetStagger / 2 - FacetTrim;
        Prism(p, ra, rb, color, "plated steel");
    }

    /// <summary>The plating on a facet band: panels about 3 m long, each a mining-yellow plate (some stepped, some primer where the
    /// yellow has worn through), or bare skin with its seam; the same on both sides.</summary>
    private static void Plates(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.07, double v1 = 0.93)
    {
        var n = Math.Max(1, (int)Math.Round((zb - za) / 3.0));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.03 / (zb - za) * 4, u1 = (double)(i + 1) / n - 0.03 / (zb - za) * 4;
            var pick = (k * 7 + i * 3 + salt * 5 + (int)za) % 6;
            switch (pick)
            {
                case 0:
                case 3:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.11, 0.06, Yellow);
                    break;
                case 1:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.1, 0.06, Yellow2);
                    Slab(p, k, s, za, zb, u0 + (u1 - u0) * 0.2, u1 - (u1 - u0) * 0.15, v0 + 0.15, v1 - 0.2, 0.1, 0.17, 0.05, Yellow);
                    break;
                case 2:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.08, 0.05, Steel, "plated steel");
                    FStrip(p, k, s, za, zb, u0 + 0.1, u0 + 0.3, v0 + 0.15, v0 + 0.32, 0.085, Primer, "rusty paint");
                    break;
                case 4:
                    Slab(p, k, s, za, zb, u0, (u0 + u1) / 2 - 0.01, v0, v1, 0.02, 0.13, 0.07, Yellow);
                    Slab(p, k, s, za, zb, (u0 + u1) / 2 + 0.01, u1, v0 + 0.1, v1, 0.02, 0.09, 0.05, Yellow2);
                    break;
                default:
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.03, Frame, "dirty steel");
                    FStrip(p, k, s, za, zb, u0, u0 + Math.Min(0.25, (u1 - u0) * 0.2), v0 + 0.1, v0 + 0.22, 0.031, Orange, "worn paint");
                    break;
            }
        }
    }

    // ---- geometry helpers (the Zero Day's)

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

    /// <summary>A quad as one plate when flat (or nearly), else as two triangles.</summary>
    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating", bool glass = false)
    {
        var n = Cross(Sub(b, a), Sub(c, a));
        if (Dot(n, n) < 1e-10)
        {
            Plate(p, [a, c, d], color, finish, glass);
            return;
        }
        var flat = Math.Abs(Dot(Unit(n), Sub(d, a))) < 0.08;
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

    /// <summary>
    /// A mount for an attitude jet (ShipParts.Rcs): a square pad standing <c>0.04</c> m proud of the surface point, its outer face the socket (a socket sits on a solid box's face).
    /// <paramref name="w"/>: the pad's width, a little over the jet's housing.
    /// </summary>
    private static void RcsMount(Part p, string name, double x, double y, double z, string dir, double w, string color)
    {
        var s = dir[0] == '-' ? -1 : 1;
        double[] at = [x, y, z];
        var axis = dir[1] - 'x';
        double[] lo = [x - w / 2, y - w / 2, z - w / 2], hi = [x + w / 2, y + w / 2, z + w / 2];
        lo[axis] = Math.Min(at[axis] - s * 0.11, at[axis] + s * 0.04);
        hi[axis] = Math.Max(at[axis] - s * 0.11, at[axis] + s * 0.04);
        at[axis] += s * 0.04;
        Solid(p, lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], color, "dirty steel");
        Sock(p, name, at[0], at[1], at[2], dir);
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
        // (Box grows a detail only under 9.9 mm, and an exact 1 cm one can come out a hair under it after rounding, which the game refuses.)
        double[] mn = P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), mx = P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
        for (var k = 0; k < 3; k++)
            if (mx[k] - mn[k] < DetailMin)
            {
                var mid = (mn[k] + mx[k]) / 2;
                mn[k] = mid - DetailMin / 2;
                mx[k] = mid + DetailMin / 2;
            }
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
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

    /// <summary>A ring of n points round an axis along z at (cx, cy), radius r, at z (ship metres, shifted).</summary>
    private static double[][] RingZ(double cx, double cy, double r, double z, int n) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = Math.PI / n + i * 2 * Math.PI / n;
        return P(cx + r * Math.Cos(a), cy + r * Math.Sin(a), z);
    })];

    /// <summary>A frame bar from a to b (shifted points), w wide, drawn as a thin plate turned to face out from the hull's axis.</summary>
    private static void Bar(Part p, double[] a, double[] b, double w, string color = Frame, string finish = "dirty steel")
    {
        var d = Unit(Sub(b, a));
        double[] up = Math.Abs(d[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        var side = Unit(Cross(d, up));
        double[] Off(double[] q, double t) => [q[0] + side[0] * t, q[1] + side[1] * t, q[2] + side[2] * t];
        Plate(p, [Off(a, -w / 2), Off(a, w / 2), Off(b, w / 2), Off(b, -w / 2)], color, finish, thick: w * 0.8);
    }

    /// <summary>Hidden boxes for the void between the skin and the rooms, both sides: x from xin (the rooms' outer face; 0: one box across
    /// the middle) out to the skin less a margin, in a y and a z band.</summary>
    private static void Void(Part p, double za, double zb, double ya, double yb, double xin)
    {
        var xo = Math.Floor((new[] { SkinHalf(za, ya), SkinHalf(za, yb), SkinHalf(zb, ya), SkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        za += 0.004;
        zb -= 0.004;
        if (xin <= 0)
            Hidden(p, Cx - xo, ya, za, Cx + xo, yb, zb);
        else
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * xin, ya, za, Cx + s * xo, yb, zb);
    }

    /// <summary>The part of a convex polygon (x, y) on the side of a line where a*x + b*y &lt;= c.</summary>
    private static List<(double X, double Y)> Clip(List<(double X, double Y)> poly, double a, double b, double c)
    {
        var o = new List<(double X, double Y)>();
        for (var i = 0; i < poly.Count; i++)
        {
            var (p0, p1) = (poly[i], poly[(i + 1) % poly.Count]);
            double d0 = a * p0.X + b * p0.Y - c, d1 = a * p1.X + b * p1.Y - c;
            if (d0 <= 0)
                o.Add(p0);
            if (d0 * d1 < 0)
            {
                var t = d0 / (d0 - d1);
                o.Add((p0.X + (p1.X - p0.X) * t, p0.Y + (p1.Y - p0.Y) * t));
            }
        }
        return o;
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

    /// <summary>A wall 0.25 m thick centred on c, along x (alongX) or z from a0 to a1, from the floor's top y0 to y1, with its gaps; a dark
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
                Run(at + 0.05, g.A - 0.05, y0, y0 + 0.75, WallLow, "painted plating", 0.137, true);
            }
            else
                Run(at + 0.05, g.A - 0.05, y0, y0 + 0.75, WallLow, "painted plating", 0.137, true);
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", 0.145, true);
            at = g.A + g.W;
        }
        Run(at, a1, y0, y1, Wall, "painted plating", 0.125, false);
        Run(at + 0.05, a1 - 0.05, y0, y0 + 0.75, WallLow, "painted plating", 0.137, true);
    }

    /// <summary>The socket a door hangs by in a wall's doorway: on the wall's centre line at the doorway's start.</summary>
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

    private static void CeilStrip(Part p, double x0, double x1, double z0, double z1, double ceil) =>
        Detail(p, x0, ceil - 0.025, z0, x1, ceil, z1, "#fff0d0", glow: true);

    /// <summary>Pipes along both upper corners of a room, labelled (red and blue mains, a yellow one beside the red).</summary>
    private static void CeilPipes(Part p, double hw, double z0, double z1, double ceil)
    {
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * (hw - 0.18) - 0.05, ceil - 0.3, z0, Cx + s * (hw - 0.18) + 0.05, ceil - 0.2, z1, s < 0 ? PipeRed : PipeBlue, "painted steel");
            Detail(p, Cx + s * (hw - 0.32) - 0.04, ceil - 0.28, z0, Cx + s * (hw - 0.32) + 0.04, ceil - 0.2, z1, PipeYellow, "painted steel");
            Detail(p, Cx + s * (hw - 0.18) - 0.062, ceil - 0.312, (z0 + z1) / 2 - 0.06, Cx + s * (hw - 0.18) + 0.062, ceil - 0.188, (z0 + z1) / 2 + 0.06, "#e8e4d8", "matte paint");
        }
    }

    private static Part Big(string name, string desc, string role = "frame", string color = Skin) =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    // ---- the drill well: an opening through the drill bay's floor and the keel; four drills hang over it on rams

    private static readonly Rect Well = new(Cx - 1.75, 12.15, Cx + 1.75, 15.85); // (its rail leaves a standing landing inside both bulkhead doors)
    private const double WellLeaf = 1.9, WellDoorZ0 = 12.1 - WellLeaf, WellDoorZ1 = 15.9 + WellLeaf; // the cargo doors' leaves (each covers half the well) and how far their rails run
    private static readonly double[] DrillZ = [12.6, 15.4]; // the gantry beams over the well (a pair of rams on each)
    private const double DrillX = 0.6, GantryY = 2.95; // the rams either side of the centreline, hung under the beams

    // ---- the keel: the flat underside with the well through it, the fuel under the lower deck, the legs

    private static Part Keel()
    {
        var p = Big("Magpie Keel", "The Magpie's keel: the flat underside, the fuel tankage under the lower deck and the legs' mounts.", "fueltank", Steel);
        double[] zs = [5.0, 7.0, ZWork, ZDrill, 23.0, ZT];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            Facet(p, 0, 1, za, zb, Steel, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Skin2, "painted plating");
                if (za >= ZWork && zb <= ZDrill)
                    continue; // (the ore hopper sits on the chine here)
                Plates(p, 1, s, za, zb, 1);
                Chine(p, 1, s, za, zb, 0.22, 0.1, Frame);
            }
            // Skid strakes along the flat: two dark runners (a miner sets down on rough ground), worn bright where they scrape.
            foreach (var s in new[] { -1, 1 })
            {
                var y = Math.Max(At(za)[0].Y, At(zb)[0].Y);
                Detail(p, Cx + s * 2.1 - 0.1, y - 0.14, za + 0.05, Cx + s * 2.1 + 0.1, y - 0.002, zb - 0.05, Frame, "dirty steel");
                Detail(p, Cx + s * 2.1 - 0.06, y - 0.152, za + 0.4, Cx + s * 2.1 + 0.06, y - 0.14, zb - 0.4, "#9a9c98", "steel");
            }
        }
        // The fuel void: hidden boxes under the floor.
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            foreach (var (ya, yt) in new[] { (yb, -0.9), (-0.9, -0.35), (-0.35, 0.0) })
            {
                var xo = new[] { SkinHalf(za, ya), SkinHalf(za, yt), SkinHalf(zb, ya), SkinHalf(zb, yt) }.Min() - 0.06;
                if (xo < 0.3 || yt <= ya)
                    continue;
                Hidden(p, Cx - xo, ya, i == 0 ? za : za + 0.012, Cx + xo, yt, i == zs.Length - 2 ? zb : zb - 0.012);
            }
        }
        foreach (var (s, z, n) in new[] { (-1, 8.0, "legFL"), (1, 8.0, "legFR"), (-1, 21.5, "legBL"), (1, 21.5, "legBR") })
            Sock(p, n, Cx + s * 2.2, At(z)[0].Y + 0.02, z, "-y");
        p.Set(("fuelTank", Kw(("capacity", 6000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx, -0.6, ZT).Cast<object>()], "+z", "fuel"))));
        return p.Stats(18000, 50000, 40, 25.0);
    }

    // ---- the lower deck: the workshop, the drill bay, engineering and the airlock

    private static readonly Rect ForeHatch = new(Cx + 1.4, 9.1, Cx + 2.65, 10.35); // the workshop up to the hall behind the bridge
    private static readonly Rect AftHatch = new(Cx + 2.0, 21.4, Cx + 3.25, 22.65); // engineering up to the mess

    private static Part LowerFore()
    {
        var p = Big("Magpie Workshop Deck", "The Magpie's lower deck forward: the workshop, where bits are changed and samples assayed, with the ladder up.");
        double y0 = Ft0, y1 = Ceil0;
        Floor(p, [new Rect(Cx - LwF - 0.25, ZFront - 0.25, Cx + LwF + 0.25, ZWork)], 0, Ft0, Deck, "painted deck plate");
        Detail(p, Cx - 0.8, Ft0, ZFront + 0.6, Cx + 0.8, Ft0 + 0.012, ZWork - 0.1, "#30332f", "rubber matting");
        WallRun(p, true, Cx - LwF - 0.25, Cx + LwF + 0.25, ZFront - 0.125, y0, y1);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZFront, ZWork, Cx + s * (LwF + 0.125), y0, y1);
        // The bench along the front wall: a steel top with a vice and a drill-bit rack over it.
        Solid(p, Cx + 0.6, Ft0, ZFront, Cx + 3.4, Ft0 + 0.88, ZFront + 0.62, Steel, "painted steel");
        Detail(p, Cx + 0.58, Ft0 + 0.88, ZFront + 0.004, Cx + 3.42, Ft0 + 0.92, ZFront + 0.66, "#9a9c98", "steel");
        Detail(p, Cx + 2.7, Ft0 + 0.92, ZFront + 0.25, Cx + 3.0, Ft0 + 1.08, ZFront + 0.45, Orange, "worn paint");
        Detail(p, Cx + 0.8, Ft0 + 1.5, ZFront + 0.004, Cx + 2.2, Ft0 + 1.56, ZFront + 0.12, Frame, "dirty steel");
        foreach (var i in Enumerable.Range(0, 6))
            Detail(p, Cx + 0.9 + i * 0.22, Ft0 + 1.1, ZFront + 0.06, Cx + 0.96 + i * 0.22, Ft0 + 1.5, ZFront + 0.12, "#9a9c98", "steel");
        CeilStrip(p, Cx - 0.5, Cx + 0.5, ZFront + 0.5, ZWork - 0.5, y1);
        CeilPipes(p, LwF, ZFront + 0.2, ZWork - 0.02, y1);
        foreach (var z in new[] { 7.0, 9.0 })
            Detail(p, Cx - LwF + 0.1, y1 - 0.18, z - 0.07, Cx + LwF - 0.1, y1 - 0.012, z + 0.07, Frame, "dirty steel");
        foreach (var s in new[] { -1, 1 })
        {
            Facet(p, 2, s, ZFront, ZWork, Skin, "painted plating");
            Plates(p, 2, s, ZFront, ZWork, 2);
            Chine(p, 2, s, ZFront, ZWork, 0.3, 0.14, Frame);
        }
        Void(p, ZFront - 0.25, ZWork, 0.0, 0.7, LwF + 0.25);
        Void(p, ZFront - 0.25, ZWork, 0.7, 3.25, LwF + 0.25);
        Sock(p, "toolRack", Cx - LwF, Ft0 + 0.9, 6.2, "+x");
        Sock(p, "locker0", Cx - LwF, Ft0 + 0.125, 8.3, "+x");
        Sock(p, "locker1", Cx - LwF, Ft0 + 0.125, 9.0, "+x");
        Sock(p, "desk", Cx - 1.6, Ft0 + 0.125, ZFront, "+z");
        Sock(p, "crate0", Cx + 3.0, Ft0, 5.95, "+y");
        Sock(p, "crate1", Cx + 3.0, Ft0, 6.45, "+y");
        Sock(p, "shelf", Cx + LwF, Ft0 + 0.125, 7.7, "-x");
        Sock(p, "ladder", ForeHatch.X0 + 0.625, Ft0, ForeHatch.Z1 - 0.095, "+y");
        Sock(p, "lampA", Cx - LwF, Ft0 + 2.45, 7.4, "+x");
        Sock(p, "lampB", Cx + LwF, Ft0 + 2.45, 9.6, "-x");
        Sock(p, "switch", Cx + 0.4, Ft0 + 1.3, ZFront, "+z");
        foreach (var y in new[] { 0.0, Ceil0 + 0.25 })
            MustFit("workshop", ZFront - 0.25, LwF + 0.25, y);
        return p.Stats(9000, 30000, 30, 20.0);
    }

    private static Part DrillBay()
    {
        var p = Big("Magpie Drill Bay", "The Magpie's drill bay: the drill docks on its aft bulkhead and the hatches of both ore hoppers.");
        double y0 = Ft0, y1 = Ceil0;
        Floor(p, [new Rect(Cx - Lw - 0.25, ZWork, Cx + Lw + 0.25, ZDrill)], 0, Ft0, Deck, "painted deck plate");
        // A grating down the middle where the drills are carried through, hazard by each dock.
        Detail(p, Cx - 1.0, Ft0, ZWork + 0.3, Cx + 1.0, Ft0 + 0.012, ZDrill - 0.3, Grate, "painted grating");
        foreach (var x in DockX)
            Detail(p, x - 0.5, Ft0, ZDrill - 1.25, x + 0.5, Ft0 + 0.012, ZDrill - 0.3, Hazard, "hazard paint");
        // The bulkheads fore and aft (they close the step from the narrower workshop), and the sides with long windows into the hoppers.
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZWork + 0.125, y0, y1, Door(Cx - 0.75));
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZDrill - 0.125, y0, y1, Door(Cx - 0.75));
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZWork + 0.25, ZDrill - 0.25, Cx + s * (Lw + 0.125), y0, y1, new Gap(11.6, 5.3, 1.4, 2.4, true));
        // A hoist rail along the middle; a hose reel and a spare bit on the forward bulkhead; ore spill on the floor by each hopper.
        Detail(p, Cx - 0.08, y1 - 0.16, ZWork + 0.25, Cx + 0.08, y1 - 0.004, ZDrill - 0.25, Steel, "steel");
        Detail(p, Cx - 3.4, Ft0 + 1.1, ZWork + 0.25, Cx - 2.6, Ft0 + 1.9, ZWork + 0.55, Orange, "worn paint");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 3.0 - 0.5, Ft0, 12.4, Cx + s * 3.0 + 0.5, Ft0 + 0.02, 13.1, "#6a5340", "soot");
        CeilStrip(p, Cx - 2.6, Cx - 2.2, ZWork + 0.5, ZDrill - 0.5, y1);
        CeilStrip(p, Cx + 2.2, Cx + 2.6, ZWork + 0.5, ZDrill - 0.5, y1);
        // No hull here: the hoppers cover the sides. Sockets.
        for (var i = 0; i < DockX.Length; i++)
            Sock(p, $"dock{i}", DockX[i], DockY, ZDrill - 0.25, "-z");
        Sock(p, "holdL", Cx - Lw + 0.3, Ft0, 14.25, "+y");
        Sock(p, "holdR", Cx + Lw - 0.3, Ft0, 14.25, "+y");
        DoorSock(p, "doorFore", true, Cx - 0.75, ZWork + 0.125, Ft0);
        DoorSock(p, "doorAft", true, Cx - 0.75, ZDrill - 0.125, Ft0);
        Sock(p, "lampFL", Cx - Lw, Ft0 + 2.45, 11.1, "+x");
        Sock(p, "lampFR", Cx + Lw, Ft0 + 2.45, 11.1, "-x");
        Sock(p, "lampAL", Cx - Lw, Ft0 + 2.45, 17.4, "+x");
        Sock(p, "lampAR", Cx + Lw, Ft0 + 2.45, 17.4, "-x");
        Sock(p, "switchFore", Cx - 1.4, Ft0 + 1.3, ZWork + 0.25, "+z");
        Sock(p, "switchAft", Cx + 1.4, Ft0 + 1.3, ZDrill - 0.25, "-z");
        foreach (var z in new[] { ZWork, ZDrill })
            foreach (var y in new[] { 0.0, Ceil0 + 0.25 })
                MustFit("drill bay", z, Lw + 0.25, y);
        return p.Stats(10000, 30000, 30, 20.0);
    }

    // ---- the drill docks (Karl, 2026-11-16: the drills are carried, not fixed to the ship): a bracket on the drill bay's aft
    // bulkhead that holds a Drill Module by its back socket. F lets the drill (with the battery on it) go as a loose piece to
    // carry outside with the physics gun; F with it close by clips it back on (ConstructEditsDock.cs).

    private static readonly double[] DockX = [Cx - 2.9, Cx + 2.9];
    private const double DockY = Ft0 + 1.5; // the dock socket's height: a docked drill's bit stops 5 cm over the floor

    private const string DockName = "Drill Dock", FittingsOut = "Constructs/Parts/Fittings";

    private static Part DrillDock()
    {
        // A fitting any ship can use (Karl, 2026-11-16): a name that names no ship, in "Ship fittings", in a folder of its own.
        var p = new Part(DockName, Small, "Ship fittings", "A wall dock for a Drill Module (any tier), for any ship or base: weld it to a wall or bulkhead. F lets the drill go, to carry outside with the physics gun; F with a loose drill close by clips it back on, square.",
            role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        // Its frame: the wall at z 0, the dock's face at z 0.12 (the drill's back socket meets it there).
        Rugged(p, -0.46, -0.45, 0, 0.46, 0.45, 0.06, "#3f433f", "dirty steel", detail: false); // (the backplate)
        p.Add(Box([-0.14, -0.14, 0.06], [0.14, 0.14, 0.12], "#5c615d")); // (the clamp's face)
        foreach (var x in new[] { -0.39, 0.39 })
        {
            Rugged(p, x - 0.04, -0.3, 0.06, x + 0.04, 0.3, 0.2, Hazard, "hazard paint"); // (the jaws, either side of a docked drill)
            Rugged(p, x - 0.05, -0.32, 0.055, x + 0.05, -0.26, 0.21, "#2a2d2b", "steel");
        }
        foreach (var x in new[] { -0.3, 0.3 })
            foreach (var y in new[] { -0.4, 0.4 })
                Rugged(p, x - 0.02, y - 0.02, 0.06, x + 0.02, y + 0.02, 0.075, "#9a9c98", "steel"); // (bolts)
        Rugged(p, -0.08, 0.3, 0.06, 0.08, 0.4, 0.1, Orange, "worn paint"); // (the release button)
        p.Add(Box([-0.25, 0.32, 0.06], [-0.15, 0.38, 0.085], "#40e070", detail: true, shape: Kw(("glow", true), ("paintable", false)))); // (its lamp)
        p.Sock("wall", [0, 0, 0], "-z");
        p.Sock("dock", [0, 0, 0.12], "+z");
        p.Set(("toolDock", "Drill Module"));
        return p.Stats(35, 600, 8, 3.0);
    }

    private static Part LowerAft()
    {
        // Karl (2026-11-16): engineering, the suit room and the airlock are one cargo bay, open aft to the stern ramp.
        var p = Big("Magpie Engine Deck", "The Magpie's lower deck aft: one cargo bay with the power plant, board and batteries along its port side, the suit lockers, and the stern ramp.");
        double y0 = Ft0, y1 = Ceil0;
        Floor(p, [new Rect(Cx - Lw - 0.25, ZDrill, Cx + Lw + 0.25, ZT)], 0, Ft0, Deck, "painted deck plate");
        Detail(p, Cx - Lw + 0.1, Ft0, ZDrill + 0.1, Cx - 1.6, Ft0 + 0.012, 22.9, Grate, "painted grating");
        Detail(p, Cx - 1.3, Ft0, ZT - 1.2, Cx + 1.3, Ft0 + 0.012, ZT - 0.02, Hazard, "hazard paint"); // (keep clear of the ramp)
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZDrill, ZT, Cx + s * (Lw + 0.125), y0, y1);
        // Engineering's feed trunks up the sides, lagged; frames across the bay; tie-down rails along the floor.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * Lw - 0.18, Ft0, 22.6, Cx + s * Lw + 0.18, y1 - 0.02, 22.99, "#c9c2a8", "lagging");
            Detail(p, Cx + s * Lw - 0.19, Ft0 + 0.7, 22.59, Cx + s * Lw + 0.19, Ft0 + 0.82, 22.995, PipeRed, "painted steel");
            Detail(p, Cx + s * 1.6 - 0.04, Ft0, 23.2, Cx + s * 1.6 + 0.04, Ft0 + 0.05, ZT - 1.3, "#9a9c98", "steel");
        }
        foreach (var z in new[] { 19.6, 21.4, 23.2, 25.0 })
            Detail(p, Cx - Lw + 0.1, y1 - 0.2, z - 0.07, Cx + Lw - 0.1, y1 - 0.012, z + 0.07, Frame, "dirty steel");
        CeilStrip(p, Cx - 0.5, Cx + 0.5, ZDrill + 0.4, ZT - 0.4, y1);
        // The pylons' roots: what the engine nacelles are welded to.
        foreach (var s in new[] { -1, 1 })
            Hidden(p, Cx + s * (Lw + 0.25), 2.2, NacZ0 + 1.6, Cx + s * PylonX, 3.0, NacZ0 + 6.1);
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (ZDrill, 23.0), (23.0, ZT) })
            {
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                Plates(p, 2, s, za, zb, 3);
                Chine(p, 2, s, za, zb, 0.3, 0.14, Frame);
            }
        foreach (var (za, zb) in new[] { (ZDrill, 23.0), (23.0, ZT) })
        {
            Void(p, za, zb, 0.0, 0.7, Lw + 0.25);
            Void(p, za, zb, 0.7, 3.25, Lw + 0.25);
        }
        // The ramp hangs on the floor's aft edge.
        Hidden(p, Cx - 0.1, 0.0, ZT - 0.1, Cx + 0.1, 0.2, ZT);
        Sock(p, "ramp", Cx, 0.1, ZT, "+z");
        Sock(p, "reactor", Cx - 2.5, Ft0, 20.0, "+y");
        Sock(p, "battery0", Cx - 3.85, Ft0, 18.6, "+y");
        Sock(p, "battery1", Cx - 3.85, Ft0, 18.95, "+y");
        Sock(p, "battery2", Cx - 3.85, Ft0, 19.3, "+y");
        Sock(p, "board", Cx - Lw, Ft0 + 0.9, 21.9, "+x");
        Sock(p, "pipes", Cx - Lw, Ft0 + 2.75, 20.6, "+x");
        Sock(p, "panel", Cx + Lw, Ft0 + 0.6, 19.2, "-x");
        Sock(p, "ladder", AftHatch.X0 + 0.625, Ft0, AftHatch.Z1 - 0.095, "+y");
        for (var i = 0; i < 3; i++)
            Sock(p, $"suit{i}", Cx - Lw, Ft0 + 0.125, 23.4 + i * 0.7, "+x");
        Sock(p, "toolRack", Cx + Lw, Ft0 + 0.9, 24.0, "-x");
        for (var i = 0; i < 3; i++) // (the Robot Shell Docks down the starboard wall, by the ramp: Karl, 2026-11-17)
            Sock(p, $"shellDock{i}", Cx + Lw, Ft0 + 0.125, 20.3 + i * (2 * CrewFittingsParts.DockHalf + 0.02), "-x");
        Sock(p, "lampEngA", Cx + Lw, Ft0 + 2.45, 20.6, "-x");
        Sock(p, "lampEngB", Cx - Lw, Ft0 + 2.45, 22.3, "+x");
        Sock(p, "lampBayA", Cx + Lw, Ft0 + 2.45, 25.4, "-x");
        Sock(p, "lampBayB", Cx - Lw, Ft0 + 2.45, 25.4, "+x");
        Sock(p, "switchEng", Cx + Lw, Ft0 + 1.3, 18.7, "-x");
        Sock(p, "switchBayR", Cx + Lw, Ft0 + 1.3, 25.0, "-x");
        Sock(p, "switchBayL", Cx - Lw, Ft0 + 1.3, 25.7, "+x");
        foreach (var z in new[] { ZDrill, ZT })
            foreach (var y in new[] { 0.0, Ceil0 + 0.25 })
                MustFit("engine deck", z, Lw + 0.25, y);
        return p.Stats(10000, 30000, 30, 20.0);
    }

    // ---- the stern ramp (Karl, 2026-11-16): an event door that lowers aft to the ground. Shut, it leans aft under the stern hood
    // at RampLean from upright; F turns it a quarter about its hinge on the floor's aft edge, so it lies RampLean below level with its
    // tip RampLen * sin(RampLean) under the floor: the ground, with the ship on its legs. That reach is as far as it goes (event doors
    // turn by whole quarters).

    private const double RampLean = 40.0, RampHalf = 1.3;
    private static double RampLen => (Ft0 - Ground) / Math.Sin(RampLean * Math.PI / 180);

    private static Part Ramp()
    {
        var p = new Part("Magpie Stern Ramp", Small, Section, "The Magpie's stern ramp: F lowers it to the ground and raises it again.", role: "door", tool: "welder", material: "steel", color: Steel, group: Group);
        double b = RampLean * Math.PI / 180, len = RampLen, hy = Ft0, hz = ZT;
        double uy = Math.Cos(b), uz = Math.Sin(b); // up the plate, shut (it leans aft)
        double ny = -uz, nz = uy; // its outer face's normal, shut (aft and down)
        double[] Shut(double x, double a, double o) => P(x, hy + a * uy + o * ny, hz + a * uz + o * nz);
        void Strip(double a0, double a1, double o, double xa, double xb, string c, string f) =>
            Plate(p, [Shut(xa, a0, o), Shut(xb, a0, o), Shut(xb, a1, o), Shut(xa, a1, o)], c, f);
        double x0 = Cx - RampHalf, x1 = Cx + RampHalf;
        Strip(0, len, -0.02, x0, x1, Yellow2, "worn paint");
        foreach (var a in new[] { 0.9, 1.8, 2.7 })
            Strip(a - 0.06, a + 0.06, -0.08, x0 + 0.1, x1 - 0.1, Frame, "dirty steel"); // (ribs on the walking face)
        foreach (var x in new[] { x0 + 0.04, x1 - 0.2 })
            Strip(0.05, len - 0.05, -0.075, x, x + 0.16, Frame, "dirty steel"); // (side rails)
        Strip(0.03, 0.35, 0.012, x0 + 0.05, x1 - 0.05, Hazard, "hazard paint"); // (the hinge end's band, outside)
        Strip(len - 0.3, len - 0.03, 0.012, x0 + 0.05, x1 - 0.05, Hazard, "hazard paint"); // (the lip's band)
        // Treads, open: step i runs from i/n to (i+1)/n of the way out, its top level with the slope at its hinge end. Opening turns a
        // quarter about +x, (y, z) -> (-z, y) from the hinge, so shut is (yo, zo) -> (zo, -yo).
        const int N = 12;
        for (var i = 0; i < N; i++)
        {
            double a0 = len * i / N, a1 = len * (i + 1) / N;
            double oz0 = a0 * uy, oz1 = a1 * uy, oy1 = -a0 * uz, oy0 = -a1 * uz - 0.08; // (open: aft along z, down along y)
            Hidden(p, x0, hy + oz0, hz - oy1, x1, hy + oz1, hz - oy0);
        }
        Hidden(p, Cx - 0.1, 0.0, hz, Cx + 0.1, 0.2, hz + 0.1);
        Sock(p, "hang", Cx, 0.1, hz, "-z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("pivot", Py.L(Py.Round(Cx - H, 3), Py.Round(hy - H, 3), Py.Round(hz - H, 3))), ("angle", 90), ("seconds", 4.0), ("hang", "hang"))));
        return p.Stats(1600, 3000, 30, 8.0);
    }

    // ---- the upper deck: the bridge, the hall, two cabins, the head, the sick bay, the mess

    private static Part UpperFore()
    {
        var p = Big("Magpie Bridge Deck", "The Magpie's upper deck forward: the sealed bridge (screens, no glass), the hall at the top of the workshop ladder and the two cabins.");
        double y0 = Ft1, y1 = Ceil1;
        Floor(p, [new Rect(Cx - LwF - 0.25, ZFront - 0.25, Cx + LwF + 0.25, ZWork), new Rect(Cx - Lw - 0.25, ZWork, Cx + Lw + 0.25, ZUp)], Ceil0, Ft1, Deck, "painted deck plate", ForeHatch);
        Floor(p, [new Rect(Cx - UwF - 0.25, ZFront - 0.25, Cx + UwF + 0.25, ZBridge), new Rect(Cx - Uw - 0.25, ZBridge, Cx + Uw + 0.25, ZUp)], Ceil1, Roof, Ceil, "painted plating");
        Detail(p, Cx - UwF + 0.05, Ft1, ZFront + 0.6, Cx + UwF - 0.05, Ft1 + 0.012, ZBridge - 0.05, "#30332f", "rubber matting");
        HatchRail(p, ForeHatch, Ft1);
        // The bridge: sealed, a view screen across its front wall over the dashboard, a camera screen each side looking down at the ground.
        WallRun(p, true, Cx - UwF - 0.25, Cx + UwF + 0.25, ZFront - 0.125, y0, y1);
        Detail(p, Cx - 2.1, Ft1 + 1.2, ZFront - 0.016, Cx + 2.1, Ft1 + 2.6, ZFront + 0.016, "#202428", "plastic");
        Detail(p, Cx - 2.0, Ft1 + 1.28, ZFront + 0.008, Cx + 2.0, Ft1 + 2.52, ZFront + 0.028, "#2a5c48", glow: true);
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZFront - 0.25, ZBridge, Cx + s * (UwF + 0.125), y0, y1);
            Detail(p, Cx + s * UwF - 0.016, Ft1 + 1.25, 6.1, Cx + s * UwF + 0.016, Ft1 + 2.2, 7.7, "#202428", "plastic");
            Detail(p, Cx + s * (UwF - 0.02) - 0.008, Ft1 + 1.32, 6.2, Cx + s * (UwF - 0.02) + 0.008, Ft1 + 2.13, 7.6, "#3a5a40", glow: true);
            WallRun(p, false, ZBridge + 0.25, ZUp, Cx + s * (Uw + 0.125), y0, y1);
        }
        // The dashboard: a sloped console across the screen's foot, green screens on it (one shows the drills), a strip of lights.
        Solid(p, Cx - UwF + 0.05, Ft1, ZFront, Cx + UwF - 0.05, Ft1 + 0.85, ZFront + 0.55, Console, "plastic");
        Plate(p, [P(Cx - UwF + 0.06, Ft1 + 0.85, ZFront + 0.55), P(Cx + UwF - 0.06, Ft1 + 0.85, ZFront + 0.55), P(Cx + UwF - 0.06, Ft1 + 1.05, ZFront + 0.12), P(Cx - UwF + 0.06, Ft1 + 1.05, ZFront + 0.12)], Console, "plastic");
        foreach (var x in new[] { -1.6, -0.8, 0.8, 1.6 })
            Plate(p, [P(Cx + x - 0.32, Ft1 + 0.875, ZFront + 0.5), P(Cx + x + 0.32, Ft1 + 0.875, ZFront + 0.5), P(Cx + x + 0.32, Ft1 + 1.04, ZFront + 0.16), P(Cx + x - 0.32, Ft1 + 1.04, ZFront + 0.16)], Screen, glow: true, thick: 0.015);
        Detail(p, Cx - 0.3, Ft1 + 0.86, ZFront + 0.3, Cx + 0.3, Ft1 + 0.88, ZFront + 0.45, Amber, glow: true);
        Detail(p, Cx - UwF + 0.1, Ft1 + 0.6, ZFront + 0.551, Cx + UwF - 0.1, Ft1 + 0.63, ZFront + 0.56, Glow, glow: true);
        // The bulkhead behind the bridge (it closes the step to the wider deck); the hall; the cabins off a short corridor.
        WallRun(p, true, Cx - Uw - 0.25, Cx + Uw + 0.25, ZBridge + 0.125, y0, y1, Door(Cx - 0.75));
        WallRun(p, true, Cx - Uw, Cx - 1.125, 11.5, y0, y1);
        WallRun(p, true, Cx + 1.125, Cx + Uw, 11.5, y0, y1);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, 11.375, ZUp, Cx + s * 1.0, y0, y1, Door(11.9));
        // The hall: a chart of the claims on the port wall, a hazard band where the ladder comes up.
        Detail(p, Cx - Uw + 0.004, Ft1 + 1.2, 9.25, Cx - Uw + 0.02, Ft1 + 2.1, 10.9, "#d8cfa8", "matte paint");
        foreach (var (x, z) in new[] { (1.5, 9.6), (1.75, 10.1), (1.35, 10.5), (1.9, 9.9) })
            Detail(p, Cx - Uw + 0.02, Ft1 + x, z, Cx - Uw + 0.03, Ft1 + x + 0.06, z + 0.06, Red, "gloss paint");
        CeilStrip(p, Cx - 0.4, Cx + 0.4, ZFront + 1.0, ZBridge - 0.4, y1);
        CeilStrip(p, Cx - 0.25, Cx + 0.25, 11.6, ZUp - 0.02, y1);
        CeilStrip(p, Cx - 1.8, Cx - 1.6, 9.2, 11.2, y1);
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (ZFront, ZBridge), (ZBridge, ZUp) })
            {
                Facet(p, 3, s, za, zb, Skin, "painted plating");
                Facet(p, 4, s, za, zb, Skin2, "painted plating");
                Plates(p, 3, s, za, zb, 4, 0.12, 0.9);
                Plates(p, 4, s, za, zb, 5);
                Chine(p, 3, s, za, zb, 0.25, 0.12, Frame);
                FStrip(p, 3, s, za, zb, 0.03, 0.97, 0.04, 0.065, 0.025, Amber, glow: true); // (a running light under the beam)
            }
        // The brow: shoulder blocks over the bridge, so its armour reads as a heavy forehead.
        foreach (var s in new[] { -1, 1 })
        {
            Shoulder(p, 3, s, ZFront + 0.3, ZBridge + 1.6, 0.8, 0.45, 1.3, Yellow2, Frame);
            Shoulder(p, 4, s, ZFront + 0.6, 9.4, 0.5, 0.3, 1.0, Frame, Yellow);
        }
        foreach (var (za, zb, xl, xu) in new[] { (ZFront - 0.25, ZBridge, LwF + 0.25, UwF + 0.25), (ZBridge, ZWork, LwF + 0.25, Uw + 0.25), (ZWork, ZUp, Lw + 0.25, Uw + 0.25) })
        {
            Void(p, za, zb, 3.25, 3.5, xl);
            Void(p, za, zb, 3.5, 5.2, xu);
            Void(p, za, zb, 5.2, 6.75, xu);
        }
        Sock(p, "pilot", Cx - 0.8, Ft1, 6.95, "+y");
        Sock(p, "copilot", Cx + 0.8, Ft1, 6.95, "+y");
        Sock(p, "module", Cx, Ft1, 5.9, "+y");
        Sock(p, "chart", Cx + UwF, Ft1 + 0.475, 7.9, "-x");
        Sock(p, "bridgeLocker", Cx - UwF, Ft1 + 0.125, 8.2, "+x");
        Sock(p, "shelf", Cx - Uw, Ft1 + 0.125, 10.2, "+x");
        Sock(p, "couchA", Cx - Uw, Ft1 + 0.125, 14.4, "+x");
        Sock(p, "lockerA", Cx - 2.2, Ft1 + 0.125, 11.625, "+z");
        Sock(p, "couchB", Cx + Uw, Ft1 + 0.125, 14.4, "-x");
        Sock(p, "deskB", Cx + Uw, Ft1 + 0.125, 12.45, "-x");
        DoorSock(p, "doorBridge", true, Cx - 0.75, ZBridge + 0.125, Ft1);
        // (the cabin doors hang on their aft jamb and slide aft into the cabin walls: forward of the doorways is the open hall, Karl 2026-11-15)
        Sock(p, "doorCabinA", Cx - 1.0, Ft1 + 1.125, 13.4, "-z");
        Sock(p, "doorCabinB", Cx + 1.0, Ft1 + 1.125, 13.4, "-z");
        Sock(p, "lampBridgeL", Cx - UwF, Ft1 + 2.45, 7.0, "+x");
        Sock(p, "lampBridgeR", Cx + UwF, Ft1 + 2.45, 7.0, "-x");
        Sock(p, "lampHall", Cx + Uw, Ft1 + 2.45, 9.8, "-x");
        Sock(p, "lampCabinA", Cx - Uw, Ft1 + 2.45, 12.6, "+x");
        Sock(p, "lampCabinB", Cx + Uw, Ft1 + 2.45, 14.4, "-x");
        Sock(p, "switchBridge", Cx + 1.0, Ft1 + 1.3, ZBridge, "-z");
        Sock(p, "switchHall", Cx - 0.875, Ft1 + 1.3, 13.9, "+x");
        Sock(p, "switchCabinB", Cx + 0.875, Ft1 + 1.3, 13.8, "-x");
        foreach (var (what, hx, z) in new[] { ("bridge", UwF, ZFront - 0.25), ("hall", Uw, ZBridge), ("cabins", Uw, ZUp) })
            MustFit(what, z, hx + 0.25, Roof);
        MustFit("bridge floor", ZFront - 0.25, LwF + 0.25, Ft1);
        return p.Stats(12000, 30000, 30, 20.0);
    }

    private static Part UpperAft()
    {
        var p = Big("Magpie Mess Deck", "The Magpie's upper deck aft: the head and the sick bay off the corridor, then the mess and galley with the ladder down to engineering.");
        double y0 = Ft1, y1 = Ceil1;
        Floor(p, [new Rect(Cx - Lw - 0.25, ZUp, Cx + Lw + 0.25, ZT)], Ceil0, Ft1, Deck, "painted deck plate", AftHatch);
        Floor(p, [new Rect(Cx - Uw - 0.25, ZUp, Cx + Uw + 0.25, ZT)], Ceil1, Roof, Ceil, "painted plating");
        Detail(p, Cx - 0.8, Ft1, ZUp, Cx + 0.8, Ft1 + 0.012, 18.95, "#30332f", "rubber matting");
        Detail(p, Cx + 1.2, Ft1, 16.2, Cx + Uw - 0.05, Ft1 + 0.012, 18.95, "#b9c4c2", "gloss paint"); // (the sick bay's floor)
        HatchRail(p, AftHatch, Ft1);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZUp, ZAft, Cx + s * (Uw + 0.125), y0, y1);
        WallRun(p, true, Cx - Uw - 0.25, Cx + Uw + 0.25, ZAft + 0.125, y0, y1);
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZUp, 19.0, Cx + s * 1.0, y0, y1, Door(16.6));
            WallRun(p, true, s < 0 ? Cx - Uw : Cx + 1.125, s < 0 ? Cx - 1.125 : Cx + Uw, 16.0, y0, y1);
        }
        WallRun(p, true, Cx - Uw, Cx + Uw, 19.125, y0, y1, Door(Cx - 0.75));
        // The sick bay's wipe-clean wall and green cross; the head's splashback.
        Detail(p, Cx + Uw - 0.014, Ft1 + 1.9, 16.4, Cx + Uw, Ft1 + 2.4, 18.8, "#dfe4e2", "gloss paint");
        Detail(p, Cx + Uw - 0.02, Ft1 + 2.05, 17.95, Cx + Uw - 0.014, Ft1 + 2.2, 18.45, "#40c070", glow: true);
        Detail(p, Cx + Uw - 0.02, Ft1 + 1.875, 18.125, Cx + Uw - 0.014, Ft1 + 2.375, 18.275, "#40c070", glow: true);
        Detail(p, Cx - Uw + 0.0, Ft1 + 0.75, 16.4, Cx - Uw + 0.014, Ft1 + 1.6, 17.9, "#d8dcdc", "gloss paint");
        // The mess: a notice board of ore prices and shift rotas, a porthole screen of the ground under the ship.
        Detail(p, Cx + Uw - 0.016, Ft1 + 1.1, 23.2, Cx + Uw, Ft1 + 2.0, 24.8, "#c8b890", "matte paint");
        foreach (var (y, z) in new[] { (1.3, 23.4), (1.6, 23.9), (1.25, 24.3), (1.7, 24.5) })
            Detail(p, Cx + Uw - 0.022, Ft1 + y, z, Cx + Uw - 0.016, Ft1 + y + 0.22, z + 0.18, "#f0ece0", "matte paint");
        Detail(p, Cx - 0.6, Ft1 + 1.2, ZAft - 0.016, Cx + 0.6, Ft1 + 2.1, ZAft, "#3a5a40", glow: true);
        CeilStrip(p, Cx - 0.25, Cx + 0.25, ZUp + 0.1, 18.9, y1);
        CeilStrip(p, Cx - 2.4, Cx + 2.4, 21.0, 21.25, y1);
        CeilStrip(p, Cx - 2.4, Cx + 2.4, 24.0, 24.25, y1);
        CeilStrip(p, Cx + 1.6, Cx + 3.0, 16.6, 18.6, y1);
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (ZUp, 21.0), (21.0, ZT) })
            {
                Facet(p, 3, s, za, zb, Skin, "painted plating");
                Facet(p, 4, s, za, zb, Skin2, "painted plating");
                Plates(p, 3, s, za, zb, 6, 0.12, 0.9);
                Plates(p, 4, s, za, zb, 7);
                Chine(p, 3, s, za, zb, 0.25, 0.12, Frame);
                FStrip(p, 3, s, za, zb, 0.03, 0.97, 0.04, 0.065, 0.025, Amber, glow: true);
            }
        foreach (var (za, zb) in new[] { (ZUp, 21.0), (21.0, ZT) })
        {
            Void(p, za, zb, 3.25, 3.5, Lw + 0.25);
            Void(p, za, zb, 3.5, 5.2, Uw + 0.25);
            Void(p, za, zb, 5.2, 6.75, Uw + 0.25);
        }
        Sock(p, "toilet", Cx - 2.6, Ft1 + 0.125, 16.125, "+z");
        Sock(p, "basin", Cx - Uw, Ft1 + 0.125, 17.15, "+x");
        Sock(p, "shower", Cx - 2.7, Ft1 + 0.125, 19.0, "-z");
        Sock(p, "med", Cx + 2.75, Ft1 + 0.8, 19.0, "-z"); // (the bed runs 2 m out from the aft wall, clear of the doorway)
        Sock(p, "cabinet", Cx + 1.65, Ft1 + 0.85, 16.125, "+z");
        Sock(p, "stove", Cx - Uw, Ft1 + 0.45, 20.0, "+x");
        Sock(p, "coffee", Cx - Uw, Ft1 + 0.125, 21.1, "+x");
        Sock(p, "messLocker", Cx - Uw, Ft1 + 0.125, 21.9, "+x");
        Sock(p, "table", Cx - 1.6, Ft1, 23.6, "+y");
        Sock(p, "growL", Cx - 1.9, Ft1 + 0.125, ZAft, "-z");
        for (var i = 0; i < 3; i++) // (the Shell Control Chairs down the mess's starboard wall, aft of the ladder: Karl, 2026-11-17)
            Sock(p, $"linkChair{i}", Cx + Uw - 0.5, Ft1, 23.8 + i * 0.8, "+y"); // (the first clear of the aft ladder's landing)
        Sock(p, "growR", Cx + 1.05, Ft1 + 0.125, ZAft, "-z"); // (to port of the last Shell Control Chair)
        DoorSock(p, "doorHead", false, 16.6, Cx - 1.0, Ft1);
        DoorSock(p, "doorMed", false, 16.6, Cx + 1.0, Ft1);
        DoorSock(p, "doorMess", true, Cx - 0.75, 19.125, Ft1);
        Sock(p, "lampHead", Cx - Uw, Ft1 + 2.45, 18.4, "+x");
        Sock(p, "lampMed", Cx + Uw, Ft1 + 2.45, 17.0, "-x");
        Sock(p, "lampMessL", Cx - Uw, Ft1 + 2.45, 23.2, "+x");
        Sock(p, "lampMessR", Cx + Uw, Ft1 + 2.45, 21.0, "-x");
        Sock(p, "switchHead", Cx - 0.875, Ft1 + 1.3, 15.6, "+x");
        Sock(p, "switchMed", Cx + 0.875, Ft1 + 1.3, 15.6, "-x");
        Sock(p, "switchMess", Cx + 1.0, Ft1 + 1.3, 19.25, "+z");
        foreach (var z in new[] { ZUp, ZT })
            MustFit("mess deck", z, Uw + 0.25, Roof);
        return p.Stats(12000, 30000, 30, 20.0);
    }

    // ---- the bow: a blunt, armoured prospector's nose with a ground scanner under its chin and a light bar across its brow

    private static Part Bow()
    {
        var p = Big("Magpie Bow", "The Magpie's bow: an armoured nose over the bridge, a light bar across its brow, the ground scanner under its chin and two retro jets in its cheeks.", "fueltank", Steel);
        foreach (var (za, zb) in new[] { (Zt, Zc), (Zc, ZFront) })
        {
            Facet(p, 0, 1, za, zb, Steel, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Skin2, "painted plating");
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                Facet(p, 3, s, za, zb, za >= Zc ? Yellow2 : Skin, za >= Zc ? "worn paint" : "painted plating");
                Facet(p, 4, s, za, zb, za >= Zc ? Frame : Skin2, za >= Zc ? "plated steel" : "painted plating");
                Chine(p, 2, s, za, zb, 0.26, 0.12, Frame);
                Chine(p, 3, s, za, zb, 0.2, 0.1, Frame);
            }
            Facet(p, 5, 1, za, zb, Frame, "plated steel");
        }
        foreach (var s in new[] { -1, 1 })
        {
            Slab(p, 2, s, Zc, ZFront, 0.05, 0.95, 0.1, 0.9, 0.02, 0.14, 0.08, Yellow);
            Slab(p, 2, s, Zc, ZFront, 0.25, 0.9, 0.25, 0.75, 0.14, 0.22, 0.05, Yellow2);
            Slab(p, 2, s, Zt, Zc, 0.25, 0.95, 0.12, 0.88, 0.02, 0.11, 0.06, Orange);
            Slab(p, 1, s, Zc, ZFront, 0.08, 0.92, 0.1, 0.9, 0.02, 0.1, 0.05, Steel, "plated steel");
            Slab(p, 3, s, Zt, Zc, 0.3, 0.95, 0.1, 0.9, 0.02, 0.1, 0.05, Yellow);
            foreach (var u in new[] { 0.02, 0.36, 0.68, 0.98 })
                FStrip(p, 4, s, Zc, ZFront, u - 0.015, u + 0.015, 0.012, 0.988, 0.03, Steel, "painted steel");
            var q = OnFacet(2, s, Zc, ZFront, 0.5, 1.0);
            Detail(p, q[0] - 0.08 + s * 0.04, q[1] - 0.05, q[2] - 0.08, q[0] + 0.08 + s * 0.04, q[1] + 0.07, q[2] + 0.08, s < 0 ? "#ff3020" : "#30ff60", glow: true);
        }
        Slab(p, 5, 1, Zt, Zc, 0.1, 0.92, 0.12, 0.88, 0.02, 0.12, 0.06, Yellow2);
        Slab(p, 5, 1, Zc, ZFront, 0.06, 0.94, 0.15, 0.85, 0.02, 0.14, 0.07, Steel, "plated steel");
        // The tip: a dark cap over the blunt end.
        var tip = At(Zt);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + tip[k].H, tip[k].Y, Zt)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - tip[k].H, tip[k].Y, Zt)))], Frame, "dirty steel");
        // The light bar across the brow: a dark box standing off the nose on two brackets, five warm lenses.
        double ly = 3.7, lz = Zt + 1.0;
        var w = SkinHalf(lz, ly) + 0.35;
        Prism(p, [P(Cx - w, ly - 0.18, lz - 0.2), P(Cx + w, ly - 0.18, lz - 0.2), P(Cx + w, ly + 0.18, lz - 0.2), P(Cx - w, ly + 0.18, lz - 0.2)],
            [P(Cx - w, ly - 0.15, lz + 0.15), P(Cx + w, ly - 0.15, lz + 0.15), P(Cx + w, ly + 0.15, lz + 0.15), P(Cx - w, ly + 0.15, lz + 0.15)], Frame, "dirty steel");
        foreach (var i in Enumerable.Range(0, 5))
        {
            var x = Cx - w + 0.2 + i * (2 * w - 0.4) / 4;
            Detail(p, x - 0.13, ly - 0.12, lz - 0.235, x + 0.13, ly + 0.12, lz - 0.215, Glow, glow: true);
        }
        // The ground scanner under the chin: a faceted pod with a green sensor strip along its face, on a short pylon.
        double cz0 = Zc - 1.4, cz1 = Zc + 2.4;
        double cy = At(Zc)[0].Y;
        Prism(p, [P(Cx - 0.55, cy - 0.15, cz0), P(Cx + 0.55, cy - 0.15, cz0), P(Cx + 0.75, cy - 0.65, cz0 + 0.6), P(Cx - 0.75, cy - 0.65, cz0 + 0.6)],
            [P(Cx - 0.8, cy - 0.05, cz1), P(Cx + 0.8, cy - 0.05, cz1), P(Cx + 0.8, cy - 0.7, cz1), P(Cx - 0.8, cy - 0.7, cz1)], Frame, "plated steel");
        Detail(p, Cx - 0.5, cy - 0.69, cz0 + 0.8, Cx + 0.5, cy - 0.72, cz1 - 0.3, Green, glow: true);
        Detail(p, Cx - 0.81, cy - 0.5, cz0 + 1.6, Cx + 0.81, cy - 0.42, cz1 - 0.4, Hazard, "hazard paint");
        // Hidden boxes inside the nose, and the collar that meets the decks' front walls.
        foreach (var (za, zb) in new[] { (Zt + 1.5, Zc), (Zc, ZFront - 0.25) })
            foreach (var (ya, yb) in new[] { (At(za)[0].Y + 0.05, 1.5), (1.5, 3.2), (3.2, 5.0), (5.0, 6.2) })
                Void(p, za, zb, ya, yb, 0);
        Hidden(p, Cx - 3.0, 0.3, ZFront - 0.6, Cx + 3.0, 3.2, ZFront - 0.25);
        Hidden(p, Cx - 2.4, 3.6, ZFront - 0.6, Cx + 2.4, 6.2, ZFront - 0.25);
        p.Set(("fuelTank", Kw(("capacity", 1200))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx, 1.0, ZFront - 0.25).Cast<object>()], "+z", "fuel"))));
        return p.Stats(7000, 24000, 40, 15.0);
    }

    // ---- the spine: the roof, the ore derrick, the ground radar, the radiators

    private static Part Spine()
    {
        var p = Big("Magpie Spine", "The Magpie's spine: the roof, the ore derrick with its boom laid along it, the ground radar dish and the radiators.", color: Frame);
        double[] zs = [ZFront, ZWork, ZUp, 21.0, ZT];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            Facet(p, 5, 1, zs[i], zs[i + 1], Steel, "plated steel");
            Slab(p, 5, 1, zs[i], zs[i + 1], 0.04, 0.96, 0.05, 0.28, 0.02, 0.1, 0.05, i % 2 == 0 ? Yellow : Yellow2);
            Slab(p, 5, 1, zs[i], zs[i + 1], 0.04, 0.96, 0.72, 0.95, 0.02, 0.1, 0.05, i % 2 == 0 ? Yellow2 : Yellow);
            foreach (var s in new[] { -1, 1 })
                Chine(p, 4, s, zs[i], zs[i + 1], 0.2, 0.1, Frame);
        }
        double Top(double z) => At(z)[4].Y;
        // The derrick: a slewing pedestal aft, a boom laid forward along the spine on a crutch, its ram, the hook block at the tip.
        const double pz = 20.0, tipZ = 8.4;
        var py = Top(pz);
        Prism(p, Ngon(Cx, py - 0.012, pz, 0.95, 8), Ngon(Cx, py + 0.5, pz, 0.8, 8), Frame, "dirty steel");
        Prism(p, Ngon(Cx, py + 0.5, pz, 0.82, 8), Ngon(Cx, py + 0.58, pz, 0.82, 8), Hazard, "hazard paint");
        Prism(p, [P(Cx - 0.6, py + 0.58, pz - 0.7), P(Cx + 0.6, py + 0.58, pz - 0.7), P(Cx + 0.6, py + 0.58, pz + 0.8), P(Cx - 0.6, py + 0.58, pz + 0.8)],
            [P(Cx - 0.45, py + 1.4, pz - 0.3), P(Cx + 0.45, py + 1.4, pz - 0.3), P(Cx + 0.45, py + 1.4, pz + 0.7), P(Cx - 0.45, py + 1.4, pz + 0.7)], Yellow, "worn paint");
        Detail(p, Cx + 0.46, py + 0.9, pz - 0.15, Cx + 0.49, py + 1.25, pz + 0.45, Glass, "glass"); // (the operator's slit)
        Detail(p, Cx - 0.1, py + 1.4, pz + 0.2, Cx + 0.1, py + 1.55, pz + 0.4, Amber, glow: true); // (the beacon)
        double by0 = py + 1.0, by1 = Top(tipZ) + 0.75;
        double[][] Boom(double z, double y, double hw, double hh) => [P(Cx - hw, y - hh, z), P(Cx + hw, y - hh, z), P(Cx + hw, y + hh, z), P(Cx - hw, y + hh, z)];
        Prism(p, Boom(pz - 0.3, by0, 0.32, 0.3), Boom(14.0, (by0 + by1) / 2, 0.27, 0.25), Yellow, "worn paint");
        Prism(p, Boom(14.0 - 0.004, (by0 + by1) / 2, 0.27, 0.25), Boom(tipZ, by1, 0.2, 0.18), Yellow2, "worn paint");
        foreach (var z in new[] { 17.5, 15.0, 12.5, 10.5 })
        {
            var t = (pz - 0.3 - z) / (pz - 0.3 - tipZ);
            var y = by0 + (by1 - by0) * t;
            var hw = 0.32 + (0.2 - 0.32) * t + 0.012;
            Detail(p, Cx - hw, y - 0.05, z - 0.06, Cx + hw, y + 0.05, z + 0.06, Frame, "dirty steel");
        }
        Prism(p, Boom(tipZ + 0.3, by1, 0.26, 0.24), Boom(tipZ - 0.15, by1, 0.26, 0.24), Frame, "dirty steel"); // (the sheave head)
        Detail(p, Cx - 0.18, by1 - 0.62, tipZ - 0.1, Cx + 0.18, by1 - 0.25, tipZ + 0.2, Orange, "worn paint"); // (the hook block, stowed)
        Detail(p, Cx - 0.12, by1 - 0.2, tipZ - 0.05, Cx + 0.12, by1 - 0.14, tipZ + 0.15, Hazard, "hazard paint");
        // The crutch it rests on: an A-frame across the spine.
        var cz = tipZ + 1.6;
        foreach (var s in new[] { -1, 1 })
            Bar(p, P(Cx + s * 1.1, Top(cz) - 0.01, cz), P(Cx + s * 0.18, by1 - 0.25, cz), 0.12);
        Detail(p, Cx - 0.3, by1 - 0.36, cz - 0.1, Cx + 0.3, by1 - 0.25, cz + 0.1, Frame, "dirty steel");
        // The luffing ram from the pedestal to the boom's underside.
        Bar(p, P(Cx, py + 0.6, pz - 0.75), P(Cx, (by0 + by1) / 2 - 0.27, 15.5), 0.16, Steel, "steel");
        // The ground radar forward: a dish on a short mast that looks down past the bow.
        double dz = 6.3, dy = Top(dz);
        Detail(p, Cx + 1.2 - 0.06, dy - 0.01, dz - 0.06, Cx + 1.2 + 0.06, dy + 0.6, dz + 0.06, Frame, "painted steel");
        Prism(p, Ngon(Cx + 1.2, dy + 0.6, dz, 0.1, 8), Ngon(Cx + 1.2, dy + 0.85, dz, 0.6, 8), "#c8ccc8", "painted steel");
        Detail(p, Cx + 1.15, dy + 0.85, dz - 0.05, Cx + 1.25, dy + 1.1, dz + 0.05, Red, glow: true);
        // The radiators aft: two finned panels standing up either side of the pedestal, leaning out.
        foreach (var s in new[] { -1, 1 })
        {
            double x0 = Cx + s * 1.5, z0 = 21.6, z1 = 25.6;
            Prism(p, [P(x0 - 0.05, Top(22) - 0.01, z0), P(x0 + 0.05, Top(22) - 0.01, z0), P(x0 + 0.05, Top(22) - 0.01, z1), P(x0 - 0.05, Top(22) - 0.01, z1)],
                [P(x0 + s * 0.5 - 0.03, Top(22) + 1.1, z0 + 0.3), P(x0 + s * 0.5 + 0.03, Top(22) + 1.1, z0 + 0.3), P(x0 + s * 0.5 + 0.03, Top(22) + 1.1, z1 - 0.3), P(x0 + s * 0.5 - 0.03, Top(22) + 1.1, z1 - 0.3)], Steel, "plated steel");
            for (var z = z0 + 0.5; z < z1 - 0.3; z += 0.6)
                Detail(p, x0 + s * 0.25 - 0.09, Top(22) + 0.45, z, x0 + s * 0.25 + 0.09, Top(22) + 0.5, z + 0.04, Frame, "soot");
        }
        // Whip antennae at the stern corners of the roof, red tips.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 2.4 - 0.03, Top(25.5) - 0.12, 25.4, Cx + s * 2.4 + 0.03, Top(25.5) + 1.6, 25.46, Frame, "steel");
            Detail(p, Cx + s * 2.4 - 0.04, Top(25.5) + 1.6, 25.39, Cx + s * 2.4 + 0.04, Top(25.5) + 1.68, 25.47, Red, glow: true);
        }
        for (var i = 0; i < zs.Length - 1; i++)
            Void(p, zs[i], zs[i + 1], Roof, Math.Min(Top(zs[i]), Top(zs[i + 1])) - 0.02, 0);
        Hidden(p, Cx - 0.7, py - 0.02, pz - 0.7, Cx + 0.7, py + 1.3, pz + 0.7);
        Hidden(p, Cx - 0.25, Top(14) - 0.02 + 0.4, 9.0, Cx + 0.25, (by0 + by1) / 2 + 0.2, 19.2);
        return p.Stats(8000, 24000, 40, 15.0);
    }

    // ---- the ore hoppers: a sponson on each flank, wide at the top and narrowing down, the ore inside behind a sight glass

    private const double HopZ0 = ZWork, HopZ1 = ZDrill, HopIn = Lw + 0.25;
    private static readonly (double H, double Y)[] HopOut = [(5.4, -1.0), (7.4, 1.3), (7.8, 2.2), (7.5, 3.5)]; // B, C, D, E (A and F are on the hull)
    private const double FillH0 = 4.6, FillH1 = 7.35, FillY0 = 1.35, FillY1 = 2.95, FillZ0 = 10.8, FillZ1 = 17.7; // the ore's box

    /// <summary>The hopper's outline at full size (A on the hull's lower chine, B..E round the outside, F at the beam), or collapsed onto
    /// the chord from A to F (where the hopper's ends taper into the hull).</summary>
    private static (double H, double Y)[] HopOutline(bool collapsed)
    {
        var q = At(14.0);
        (double H, double Y) a = (SkinHalf(14.0, -0.9), -0.9), f = (q[2].H, q[2].Y);
        if (!collapsed)
            return [a, .. HopOut, f];
        return [.. Enumerable.Range(0, 6).Select(i => (a.H + (f.H - a.H) * i / 5.0, a.Y + (f.Y - a.Y) * i / 5.0))];
    }

    private static Part Hopper(int s)
    {
        var name = s < 0 ? "Magpie Ore Hopper L" : "Magpie Ore Hopper R";
        var p = Big(name, "One of the Magpie's ore hoppers: a sponson on the flank that the drills fill, a sight glass on its side, the lift jet's shelf and a saddle fuel tank under it.", "fueltank", Yellow);
        double zf = HopZ0 - 1.0, za = HopZ1 + 1.2;
        var full = HopOutline(false);
        var flat = HopOutline(true);
        double[] V((double H, double Y) q, double z) => P(Cx + s * q.H, q.Y, z);
        string[] cols = [Steel, Yellow, Frame, Yellow2, Skin];
        string[] fins = ["plated steel", "worn paint", "dirty steel", "worn paint", "painted grating"];
        for (var i = 0; i < full.Length - 1; i++)
        {
            var dz = i % 2 == 1 ? FacetStagger : 0;
            Quad(p, V(flat[i], zf + dz), V(flat[i + 1], zf + dz), V(full[i + 1], HopZ0 + dz), V(full[i], HopZ0 + dz), cols[i], fins[i]);
            Quad(p, V(full[i], HopZ0 + dz), V(full[i + 1], HopZ0 + dz), V(full[i + 1], HopZ1 + dz), V(full[i], HopZ1 + dz), cols[i], fins[i]);
            Quad(p, V(full[i], HopZ1 + dz), V(full[i + 1], HopZ1 + dz), V(flat[i + 1], za), V(flat[i], za), cols[i], fins[i]);
        }
        // Ribs down the slope and the outer face every 1.25 m: the hopper's frames, standing out.
        double[] On(int face, double z, double v, double d)
        {
            var (a, b) = (full[face], full[face + 1]);
            double h = a.H + (b.H - a.H) * v, y = a.Y + (b.Y - a.Y) * v;
            double nh = b.Y - a.Y, ny = -(b.H - a.H);
            var l = Math.Sqrt(nh * nh + ny * ny);
            return P(Cx + s * (h + nh / l * d), y + ny / l * d, z);
        }
        for (var z = HopZ0 + 0.6; z < HopZ1 - 0.3; z += 1.25)
            foreach (var face in new[] { 1, 3 })
                Plate(p, [On(face, z - 0.07, 0.04, 0.03), On(face, z + 0.07, 0.04, 0.03), On(face, z + 0.07, 0.96, 0.03), On(face, z - 0.07, 0.96, 0.03)], Frame, "dirty steel", thick: 0.06);
        // The sight glass along the outer face (the ore's level shows through it), framed.
        Plate(p, [On(3, 11.2, 0.42, 0.02), On(3, 17.3, 0.42, 0.02), On(3, 17.3, 0.78, 0.02), On(3, 11.2, 0.78, 0.02)], Glass, glass: true);
        Plate(p, [On(3, 11.1, 0.36, 0.045), On(3, 17.4, 0.36, 0.045), On(3, 17.4, 0.41, 0.045), On(3, 11.1, 0.41, 0.045)], Hazard, "hazard paint");
        Plate(p, [On(3, 11.1, 0.79, 0.045), On(3, 17.4, 0.79, 0.045), On(3, 17.4, 0.84, 0.045), On(3, 11.1, 0.84, 0.045)], Hazard, "hazard paint");
        // The loading hatch on top: a dark lid in a hazard frame; the beacon at the hopper's forward corner.
        Plate(p, [On(4, 12.8, 0.25, 0.03), On(4, 15.6, 0.25, 0.03), On(4, 15.6, 0.75, 0.03), On(4, 12.8, 0.75, 0.03)], Hazard, "hazard paint");
        Plate(p, [On(4, 12.95, 0.3, 0.05), On(4, 15.45, 0.3, 0.05), On(4, 15.45, 0.7, 0.05), On(4, 12.95, 0.7, 0.05)], Frame, "dirty steel");
        var bq = On(3, HopZ0 + 0.3, 1.0, 0.0);
        Detail(p, bq[0] + H - 0.1, bq[1] + H, bq[2] + H - 0.1, bq[0] + H + 0.1, bq[1] + H + 0.2, bq[2] + H + 0.1, Amber, glow: true);
        // The discharge spout under the hopper's aft end: a tapered chute with a hazard band.
        var (bh, byy) = HopOut[0];
        double sh = bh - 0.6;
        Prism(p, [P(Cx + s * (sh - 0.45), byy + 0.01, 16.6), P(Cx + s * (sh + 0.45), byy + 0.01, 16.6), P(Cx + s * (sh + 0.45), byy + 0.01, 17.6), P(Cx + s * (sh - 0.45), byy + 0.01, 17.6)],
            [P(Cx + s * (sh - 0.2), byy - 0.55, 16.85), P(Cx + s * (sh + 0.2), byy - 0.55, 16.85), P(Cx + s * (sh + 0.2), byy - 0.55, 17.35), P(Cx + s * (sh - 0.2), byy - 0.55, 17.35)], Steel, "dirty steel");
        Detail(p, Cx + s * sh - 0.26, byy - 0.4, 16.8, Cx + s * sh + 0.26, byy - 0.32, 17.4, Hazard, "hazard paint");
        // The lift jet's shelf: a block standing out of the slope under the outer face's overhang.
        double jx0 = Cx + s * 6.95, jx1 = Cx + s * 8.6;
        Solid(p, Math.Min(jx0, jx1), 1.0, JetZ - 0.8, Math.Max(jx0, jx1), 1.45, JetZ + 0.8, Yellow2, "worn paint");
        Detail(p, Math.Min(jx0, jx1) - 0.004, 1.3, JetZ - 0.804, Math.Max(jx0, jx1) + 0.004, 1.38, JetZ + 0.804, Hazard, "hazard paint");
        // The retro jet's housing on the shelf's nose (Karl, 2026-11-15: the retros under the bow's cheeks looked stuck on): a block
        // standing forward of the lift jet, its face square to the bow, so the retro fires straight ahead from the thruster pod.
        double rx0 = Cx + s * 7.3, rx1 = Cx + s * 8.4;
        Solid(p, Math.Min(rx0, rx1), 0.72, RetroZ, Math.Max(rx0, rx1), 1.72, JetZ - 0.8, Yellow2, "worn paint");
        Detail(p, Math.Min(rx0, rx1) - 0.004, 1.52, RetroZ - 0.004, Math.Max(rx0, rx1) + 0.004, 1.6, JetZ - 0.9, Hazard, "hazard paint");
        Detail(p, Math.Min(rx0, rx1) + 0.08, 0.66, RetroZ + 0.08, Math.Max(rx0, rx1) - 0.08, 0.724, JetZ - 0.9, Frame, "dirty steel");
        // Hidden: the hopper's body, stepped inside its slope (the ore's box is drawn by the hold, which collides with nothing).
        double xi = Cx + s * HopIn;
        foreach (var (h1, y0, y1) in new[] { (5.3, -0.95, 0.0), (6.2, 0.0, 1.0), (7.0, 1.0, 1.3), (7.35, 1.3, 2.95) })
            Hidden(p, xi, y0, HopZ0 + 0.004, Cx + s * h1, y1, HopZ1 - 0.004);
        Sock(p, "lift", Cx + s * 7.85, 1.0, JetZ, "-y");
        Sock(p, "retro", Cx + s * 7.85, 1.22, RetroZ, "-z");
        // The attitude jets (ShipParts.Rcs): up on the retro housing's outboard top, down under it, out on the shelf's outboard face.
        RcsMount(p, "rcsUp", Cx + s * 8.1, 1.72, 10.15, "+y", 0.54, Frame);
        RcsMount(p, "rcsDown", Cx + s * 7.85, 0.66, 10.15, "-y", 0.54, Frame);
        RcsMount(p, "rcsOut", Cx + s * 8.6, 1.225, 11.45, s < 0 ? "-x" : "+x", 0.54, Frame);
        p.Set(("fuelTank", Kw(("capacity", 1500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx + s * 7.0, 0.5, HopZ1).Cast<object>()], "+z", "fuel"))));
        return p.Stats(6000, 24000, 35, 15.0);
    }

    private const double JetZ = 11.2, RetroZ = 9.9; // the forward lift jets under the hoppers' shelves; the retros' housings' faces

    // ---- the engine nacelles: a pod on a pylon each side aft, a drive at its tail and a lift jet under it

    private const double NacX = 6.95, NacY = 2.3, NacR = 1.15, NacZ0 = 19.4, NacZ1 = 29.2, PylonX = 5.45, AftJetZ = 21.4;
    private const double NacEng = 26.8; // the bulkhead between a nacelle's tank and its engine housing (the last 2.4 m)

    private static Part Nacelle(int s)
    {
        var name = s < 0 ? "Magpie Nacelle L" : "Magpie Nacelle R";
        var p = Big(name, "One of the Magpie's engine nacelles: a fuel pod on a pylon off the flank, a lift jet under it, and at its tail the engine housing: the main drive on the tail plate outside, its Drive Core inside behind it, a service hatch in the outboard flank.", "fueltank", Yellow);
        var x = Cx + s * NacX;
        // The pod: a nose cone, an eight-sided tank in three bands, then the engine housing (ship-works.md 15.4, Karl 2026-11-16:
        // "the nacelles don't have any thrust components in them"): hollow, clad, the drive's own Drive Core S inside on its axis.
        Prism(p, RingZ(x, NacY, 0.45, NacZ0, 8), RingZ(x, NacY, NacR, NacZ0 + 1.0, 8), Frame, "plated steel");
        Prism(p, RingZ(x, NacY, NacR, NacZ0 + 1.004, 8), RingZ(x, NacY, NacR, 23.0, 8), Yellow, "worn paint");
        Prism(p, RingZ(x, NacY, NacR + 0.02, 23.004, 8), RingZ(x, NacY, NacR + 0.02, 23.5, 8), Hazard, "hazard paint");
        Prism(p, RingZ(x, NacY, NacR, 23.504, 8), RingZ(x, NacY, NacR, NacEng - 0.004, 8), Yellow2, "worn paint");
        var flatR = NacR * Math.Cos(Math.PI / 8);
        EngineHousing.Draw(p, P, x, NacY, flatR, EngineHousing.HalfFlat(flatR), NacEng, NacZ1, s, Yellow, Yellow2, Frame, Hazard, "worn paint", ShipWorksParts.CoreR[0], ShipWorksParts.CoreD[0], ShipWorksParts.CoreD[0]);
        Prism(p, RingZ(x, NacY, NacR + 0.07, NacEng - 0.06, 8), RingZ(x, NacY, NacR + 0.07, NacEng + 0.08, 8), Frame, "dirty steel"); // (the band round the bulkhead, where the tank ends and the housing starts)
        // Panel seams and a primer patch, an intake grille on the outboard flank.
        foreach (var z in new[] { 21.2, 25.5 })
            Prism(p, RingZ(x, NacY, NacR + 0.012, z, 8), RingZ(x, NacY, NacR + 0.012, z + 0.05, 8), Frame, "dirty steel");
        Detail(p, x + s * (NacR * 0.92) - 0.05, NacY + 0.1, 24.2, x + s * (NacR * 0.92) + 0.05, NacY + 0.5, 26.4, Frame, "soot");
        Detail(p, x - 0.4, NacY + NacR * 0.92, 21.6, x + 0.4, NacY + NacR * 0.92 + 0.02, 22.4, Primer, "rusty paint");
        // The engine is mended outside, through the hatch (ship works, Karl 2026-11-16): a grab rail along the nacelle's top to it.
        var top = NacR * Math.Cos(Math.PI / 8);
        foreach (var z in new[] { 24.4, 26.2 })
            Detail(p, x - 0.03, NacY + top - 0.02, z - 0.03, x + 0.03, NacY + top + 0.2, z + 0.03, "#9a9c98", "steel");
        Detail(p, x - 0.025, NacY + top + 0.18, 24.36, x + 0.025, NacY + top + 0.23, 26.24, Hazard, "worn paint");
        // The pylon: a raked faceted web from the hull to the pod, the fuel lines over it.
        double hx = Cx + s * 4.9;
        Prism(p, [P(hx, 2.05, NacZ0 + 1.4), P(hx, 3.15, NacZ0 + 1.0), P(hx, 3.15, NacZ0 + 6.6), P(hx, 2.05, NacZ0 + 6.2)],
            [P(x, 1.95, NacZ0 + 2.2), P(x, 2.85, NacZ0 + 1.9), P(x, 2.85, NacZ0 + 5.8), P(x, 1.95, NacZ0 + 5.4)], Frame, "plated steel");
        foreach (var (dzz, col) in new[] { (2.6, PipeRed), (3.2, PipeBlue), (3.8, PipeYellow) })
            Bar(p, P(Cx + s * 5.3, 3.08 + 0.06, NacZ0 + dzz), P(x - s * 0.5, NacY + 0.62 + 0.06, NacZ0 + dzz + 0.3), 0.08, col, "painted steel");
        // The lift jet's saddle under the pod.
        var liftY = Math.Round(NacY - NacR * Math.Cos(Math.PI / 8) - 0.01, 2); // (a centimetre proud of the pod's flat underside)
        Solid(p, x - 0.45, liftY, AftJetZ - 0.7, x + 0.45, NacY - 0.75, AftJetZ + 0.7, Frame, "dirty steel");
        // Hidden: the pod, the pylon (out from where the engine deck's root stops).
        Hidden(p, x - 0.8, NacY - 0.75, NacZ0 + 1.0, x + 0.8, NacY + 0.8, NacEng - 0.004);
        Hidden(p, Cx + s * PylonX, 2.2, NacZ0 + 1.6, x - s * 0.8, 3.0, NacZ0 + 6.1);
        Works.Mount(p, outside: true); // (the housing's tail plate: the drive bolts to it outside, its Drive Core inside)
        Sock(p, "lift", x, liftY, AftJetZ, "-y");
        // The attitude jets (ShipParts.Rcs) aft on the tank's flats, ahead of the housing: up, down and outboard.
        var flat = Math.Round(NacR * Math.Cos(Math.PI / 8), 3);
        RcsMount(p, "rcsUp", x, NacY + flat, NacEng - 0.45, "+y", 0.54, Frame);
        RcsMount(p, "rcsDown", x, NacY - flat, NacEng - 0.45, "-y", 0.54, Frame);
        RcsMount(p, "rcsOut", x + s * flat, NacY, NacEng - 0.45, s < 0 ? "-x" : "+x", 0.54, Frame);
        p.Set(("fuelTank", Kw(("capacity", 2500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(x, NacY + 0.3, NacZ1).Cast<object>()], "+z", "fuel"))));
        return p.Stats(5000, 20000, 35, 15.0);
    }

    // ---- the stern: the transom round the airlock's outer door, and the hood over the porch

    private static Part SternPart()
    {
        var p = Big("Magpie Stern", "The Magpie's stern: the transom round the ramp, the ramp's well, and the hood over it, its back a radiator grille.", color: Steel);
        var q = At(ZT);
        var outline = Enumerable.Range(0, 5).Select(k => (Cx + q[k].H, q[k].Y)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => (Cx - q[k].H, q[k].Y))).ToList();
        // The transom below the hood: the outline cut round the ramp's opening (with its frame) into convex pieces.
        double dx0 = Cx - RampHalf - 0.1, dx1 = Cx + RampHalf + 0.1, yHood = 3.3, zt = ZT + 0.03;
        var low = Clip(outline, 0, 1, yHood);
        foreach (var piece in new[] { Clip(low, 1, 0, dx0), Clip(low, -1, 0, -dx1), Clip(Clip(Clip(low, -1, 0, -dx0), 1, 0, dx1), 0, 1, -0.01) })
            if (piece.Count >= 3)
                Plate(p, [.. piece.Select(v => P(v.Item1, v.Item2, zt))], Steel, "plated steel");
        // The ramp's well under the hood: a cheek either side from the transom back to the ramp's line, and a header over its top.
        var lean = Math.Tan(RampLean * Math.PI / 180);
        foreach (var x in new[] { dx0, dx1 })
        {
            Plate(p, [P(x, Ft0, zt), P(x, yHood - 0.005, zt), P(x, yHood - 0.005, ZT + (yHood - Ft0) * lean + 0.06)], Steel, "plated steel");
            Detail(p, x - 0.1, Ft0, zt + 0.02, x + 0.1, yHood - 0.008, zt + 0.06, Hazard, "hazard paint"); // (the opening's frame, hazard)
        }
        var topZ = ZT + RampLen * Math.Sin(RampLean * Math.PI / 180);
        var topY = Ft0 + RampLen * Math.Cos(RampLean * Math.PI / 180);
        Solid(p, dx0, topY + 0.03, topZ - 0.25, dx1, yHood + 0.04, topZ + 0.35, Frame, "painted steel"); // (the header the ramp shuts against)
        // The hood: from the transom's top back over the porch, raked down aft.
        double[][] Sect(double z, double w0, double w1, double w2, double y1, double y2, double yb) =>
            [P(Cx - w0, yb, z), P(Cx - w1, y1, z), P(Cx - w2, y2, z), P(Cx + w2, y2, z), P(Cx + w1, y1, z), P(Cx + w0, yb, z)];
        var front = Sect(ZT + 0.004, SkinHalf(ZT, yHood) - 0.02, q[3].H - 0.02, q[4].H - 0.02, q[3].Y, q[4].Y - 0.01, yHood);
        var back = Sect(28.9, 3.6, 3.2, 2.2, 5.4, 6.0, yHood + 0.1);
        Prism(p, front, back, Yellow2, "worn paint");
        // Its back: a radiator grille of dark slats; work lamps under its lip.
        for (var y = yHood + 0.4; y < 5.6; y += 0.3)
            Detail(p, Cx - 2.8, y, 28.88 + (y - yHood) * 0.0, Cx + 2.8, y + 0.08, 28.98, Frame, "soot");
        foreach (var x in new[] { Cx - 2.2, Cx + 2.2 })
            Detail(p, x - 0.25, yHood - 0.03, 28.2, x + 0.25, yHood + 0.005, 28.6, Glow, glow: true);
        Hidden(p, Cx - 3.3, yHood + 0.05, ZT, Cx + 3.3, 5.6, 28.6);
        Solid(p, Cx + 1.85, Ft0 + 2.4, zt, Cx + 2.15, Ft0 + 2.6, zt + 0.06, Frame, "painted steel"); // the ramp lamp's bracket
        Sock(p, "lampPorch", Cx + 2.0, Ft0 + 2.5, zt + 0.06, "+z");
        return p.Stats(4000, 16000, 35, 12.0);
    }

    // ---- the drive, the jets, the legs

    private const double DriveForce = 900000, LiftForce = 1200000, RetroForce = 500000, Burn = 2.5;

    /// <summary>The main drive on a nacelle's tail: a short collar and a bell along +z, a warm glow deep in it.</summary>
    private static Part Drive()
    {
        var p = new Part("Magpie Drive", Small, Section, $"A Magpie main drive: {DriveForce / 1000:0} kN out of a nacelle's tail, from the nacelle's own fuel.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[][] R(double r, double z) => [.. Enumerable.Range(0, 10).Select(i =>
        {
            var a = Math.PI / 10 + i * 2 * Math.PI / 10;
            return new[] { Py.Round(r * Math.Cos(a), 4), Py.Round(r * Math.Sin(a), 4), z };
        })];
        (double Z, double R)[] st = [(0.0, 0.85), (0.35, 0.7), (0.9, 0.85), (1.7, 1.05)];
        for (var s = 0; s < st.Length - 1; s++)
        {
            var (a, b) = (R(st[s].R, st[s].Z), R(st[s + 1].R, st[s + 1].Z));
            for (var i = 0; i < 10; i++)
                Quad(p, a[i], a[(i + 1) % 10], b[(i + 1) % 10], b[i], s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        }
        Prism(p, R(0.92, 1.1), R(0.97, 1.22), "#5a4a3a", "copper");
        Plate(p, R(0.6, 0.45), "#ffb060", glow: true);
        p.Add(Box([-0.7, -0.7, 0.0], [0.7, 0.7, 1.7], hidden: true));
        p.Sock("front", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 1.5), ("exhaust", "+z"), ("at", Py.L(0, 0, 1.7)), ("burnRate", Py.Round(DriveForce / 1e6 * Burn, 4)), ("gimbal", 12), ("gimbalRate", 20))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 0.7, 0.2], "+y", "fuel"))));
        Works.Head(p, Works.Table(), "torch", DriveForce, nacelle: true); // (its engine is in the nacelle behind it: mended through the hatch, outside; ship-works.md 15)
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, 1.7)), ("dir", "+z"), ("radius", 0.95), ("length", 5.0), ("kind", "Rocket")))));
        return p.Stats(1800, 6000, 15, 8.0);
    }

    /// <summary>A jet in a squat housing: lift (down) or retro (forward). Origin at its root; the bell along -y.</summary>
    private static Part Jet(string name, string desc, double force, double scale)
    {
        var p = new Part(name, Small, Section, desc, role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[] R(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * scale * Math.Cos(an), 4), Py.Round(y * scale, 4), Py.Round(r * scale * Math.Sin(an), 4)];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.55, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.48, -0.45, i))], Yellow2, "worn paint");
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.32, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.24, i))], Hazard, "hazard paint");
        (double Y, double R)[] st = [(-0.45, 0.32), (-0.6, 0.27), (-0.9, 0.38), (-1.05, 0.44)];
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < 8; i++)
                Quad(p, R(st[s].R, st[s].Y, i), R(st[s].R, st[s].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => R(0.24, -0.61, i))], "#ffb060", glow: true);
        p.Add(Box([-0.45 * scale, -0.45 * scale, -0.45 * scale], [0.45 * scale, 0, 0.45 * scale], hidden: true));
        p.Add(Box([-0.4 * scale, -1.05 * scale, -0.4 * scale], [0.4 * scale, -0.45 * scale, 0.4 * scale], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", force), ("spool", 0.5), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-1.05 * scale, 4), 0)), ("burnRate", Py.Round(force / 1e6 * Burn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.45 * scale, 4), Py.Round(-0.2 * scale, 4), 0], "+x", "fuel"))));
        // (whole, with nothing inside: only main drives have engines, Karl 2026-11-16)
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-1.05 * scale, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.4 * scale, 4)), ("length", Py.Round(2.8 * scale, 4)), ("kind", "Rocket")))));
        return p.Stats(700 * scale, 3000, 20, 6.0);
    }

    /// <summary>A landing leg: a short, heavy strut and its ram on a broad pad (a miner squats close to its claim).</summary>
    private static Part Leg()
    {
        var p = new Part("Magpie Landing Leg", Small, Section, "A landing leg: a short, heavy strut and its ram on a broad pad.", role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var bottom = Ground - Mid[0].Item2 - 0.02; // (the pad's underside, below the keel)
        p.Add(Box([-0.26, -0.35, -0.26], [0.26, 0, 0.26], Yellow2, shape: Kw(("finish", "worn paint"))));
        p.Add(Box([-0.17, bottom + 0.2, -0.17], [0.17, -0.35, 0.17], "#9aa0a4", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.6, bottom, -0.6], [0.6, bottom + 0.2, 0.6], Frame, shape: Kw(("finish", "rubber"))));
        p.Add(Box([-0.34, bottom + 0.2, -0.34], [0.34, bottom + 0.3, 0.34], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([0.27, bottom + 0.32, -0.06], [0.37, -0.05, 0.06], "#4a4e52", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.265, -0.3, -0.265], [0.265, -0.22, 0.265], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("top", [0, 0, 0], "+y");
        return p.Stats(900, 3500, 20, 6.0);
    }

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

    // ---- the boarding ladder (Karl, 2026-11-15: "roll down as two pieces, like a normal ladder would", and more rugged). The upper
    // flight is welded to the porch; the foot is an event door riding on it that slides down (F, 2 s). Stowed, the foot sits up
    // against the upper flight's outside with its feet 1.75 m off the ground, and the ladder can't be climbed (ConstructRide.ClimbLadder).
    private const double BoardRise = Ft0 - Ground, BoardTop = BoardRise + 1.0, FootLen = 1.5, FootMount = BoardTop - 0.15, FootZ = -0.2;
    private static double FootTravel => Py.Round(FootMount + 0.05 - FootLen, 2);

    private static void Rugged(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish, bool detail = true) =>
        p.Add(Box([x0, y0, z0], [x1, y1, z1], color, detail: detail, shape: Kw(("finish", finish))));

    private static Part BoardingLadder()
    {
        var p = new Part("Magpie Boarding Ladder", Small, Section, "The Magpie's boarding ladder off the porch at its stern. Its foot rolls down to the ground (F on the foot); then F climbs it.",
            role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        double y0 = FootLen - 0.35, top = BoardTop;
        p.Add(Box([-0.3, 0, -0.19], [0.3, top, -0.005], hidden: true));
        foreach (var x in new[] { -0.27, 0.27 })
        {
            Rugged(p, x - 0.04, y0, -0.185, x + 0.04, top, -0.105, "#5c615d", "dirty steel"); // (channel stiles)
            Rugged(p, x - 0.045, top - 0.9, -0.19, x + 0.045, top - 0.84, -0.1, Hazard, "hazard paint");
            for (var y = y0 + 0.3; y < top - 0.2; y += 0.6)
                Rugged(p, x - 0.025, y, -0.105, x + 0.025, y + 0.08, -0.01, Frame, "dirty steel"); // (stand-off brackets to the hull)
        }
        for (var y = y0 + 0.15; y < BoardRise + 0.1; y += 0.3)
        {
            Rugged(p, -0.23, y - 0.025, -0.175, 0.23, y + 0.025, -0.115, "#3b3e3c", "dirty steel"); // (chequered treads)
            Rugged(p, -0.2, y + 0.025, -0.165, 0.2, y + 0.037, -0.125, Hazard, "hazard paint");
        }
        Rugged(p, -0.32, top - 0.07, -0.2, 0.32, top, -0.1, Hazard, "hazard paint");
        // The foot's mount: a bolted bracket with a roller track down both stiles' outsides.
        p.Add(Box([-0.1, FootMount - 0.06, FootZ], [0.1, FootMount + 0.06, -0.19], Frame));
        foreach (var x in new[] { -0.24, 0.24 })
            Rugged(p, x - 0.012, y0 + 0.1, -0.197, x + 0.012, FootMount + 0.08, -0.185, "#2a2d2b", "steel");
        p.Sock("hull", [0, 0.125 - Ground, -0.005], "+z");
        p.Sock("lower", [0, FootMount, FootZ], "-z");
        p.Set(("ladder", BoardRise));
        return p.Stats(60 + BoardRise * 10, 450, 8, 3.0);
    }

    private static Part BoardingLadderLower()
    {
        var p = new Part("Magpie Ladder Foot", Small, Section, $"The foot of the Magpie's boarding ladder: F rolls it down to the ground ({FootTravel:0.##} m) or back up. The ladder can only be climbed with its foot down.",
            role: "door", tool: "wrench", material: "steel", color: Frame, group: Group);
        // In its own frame its hang socket (on the upper flight's mount) is the origin; it hangs out along -z, stowed.
        double y1 = 0.05, y0 = y1 - FootLen;
        p.Add(Box([-0.1, -0.06, -0.01], [0.1, 0.06, 0], hidden: true));
        foreach (var x in new[] { -0.21, 0.21 })
        {
            p.Add(Box([x - 0.035, y0 + 0.06, -0.075], [x + 0.035, y1, -0.012], "#5c615d"));
            Rugged(p, x - 0.05, y0, -0.09, x + 0.05, y0 + 0.06, 0.0, "#1d1e1d", "rubber"); // (rubber feet)
            Rugged(p, x - 0.04, y1 - 0.2, -0.08, x + 0.04, y1 - 0.12, -0.006, Hazard, "hazard paint");
            Rugged(p, x - 0.02, y1 - 0.06, -0.004, x + 0.02, y1 - 0.02, 0.0, "#8a8f86", "steel"); // (rollers in the track)
        }
        for (var y = y0 + 0.3; y < y1 - 0.15; y += 0.3)
        {
            Rugged(p, -0.18, y - 0.025, -0.07, 0.18, y + 0.025, -0.017, "#3b3e3c", "dirty steel");
            Rugged(p, -0.16, y + 0.025, -0.06, 0.16, y + 0.037, -0.027, Hazard, "hazard paint");
        }
        p.Sock("hang", [0, 0, 0], "+z");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", FootTravel), ("seconds", 2.0), ("hang", "hang"))));
        return p.Stats(45, 300, 6, 2.0);
    }

    // ---- the fittings (Karl, 2026-11-15: the stock stove, table and lockers read as a house kitchen, "not on a space ship"). Each
    // keeps the footprint and socket names of the stock part it replaces, so the decks' sockets stand; each is bolted down on a
    // dark plinth or wall rail, latched shut, in the ship's paint (pale panel over dark frame, hazard on what moves).

    private static void Fx(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish, bool detail = false, bool glow = false)
    {
        var shape = glow ? Kw(("glow", true)) : Kw(("finish", finish));
        p.Add(Box([Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)], color, detail: detail, shape: shape));
    }

    /// <summary>A hollow case (bottom, top, back, two sides, front), walls t thick, with shelf plates at the given heights: the room a storage slot needs.</summary>
    private static void Case(Part p, double x0, double y0, double z0, double x1, double y1, double z1, double t, string color, string finish, params double[] shelves)
    {
        Fx(p, x0, y0, z0, x1, y0 + t, z1, color, finish);
        Fx(p, x0, y1 - t, z0, x1, y1, z1, color, finish);
        Fx(p, x0, y0 + t, z0, x1, y1 - t, z0 + t, color, finish);
        Fx(p, x0, y0 + t, z1 - t, x1, y1 - t, z1, color, finish);
        Fx(p, x0, y0 + t, z0 + t, x0 + t, y1 - t, z1 - t, color, finish);
        Fx(p, x1 - t, y0 + t, z0 + t, x1, y1 - t, z1 - t, color, finish);
        foreach (var y in shelves)
            Fx(p, x0 + t, y - t / 2, z0 + t, x1 - t, y + t / 2, z1 - t, color, finish);
    }

    private static PyDict Slot(double x0, double y0, double z0, double x1, double y1, double z1, string? label = null)
    {
        var d = Kw(("min", Py.L(x0, y0, z0)), ("max", Py.L(x1, y1, z1)));
        if (label != null)
            d["label"] = label;
        return d;
    }

    /// <summary>Bolt heads along a strip on a +z face at z: small dark squares, sunk 4 mm into the face.</summary>
    private static void Bolts(Part p, double x0, double x1, double y, double z, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var x = x0 + (x1 - x0) * (i + 0.5) / n;
            Fx(p, x - 0.012, y - 0.012, z - 0.004, x + 0.012, y + 0.012, z + 0.014, "#3a3c3a", "steel", detail: true);
        }
    }

    /// <summary>A leaf of the well's cargo door: a heavy plate with a ribbed underside, rollers riding the rails' lips, a hazard band
    /// along its meeting edge. It hangs from the keel's track beam by its starboard edge and slides away from the well (fore or aft).</summary>
    private static Part WellDoor(int dir)
    {
        var name = dir < 0 ? "Magpie Well Door F" : "Magpie Well Door A";
        var p = new Part(name, Small, Section, "A leaf of the Magpie's well door: a ribbed steel plate under the keel that slides " + (dir < 0 ? "forward" : "aft") + " along its rails to open the well for the drills.", role: "door", tool: "welder", material: "steel", color: Steel, group: Group);
        double hx = 1.88, hz = WellLeaf / 2, t = 0.14, m = -dir * hz; // (m: the meeting edge's z, towards the other leaf)
        double z0 = -hz + (dir > 0 ? 0.002 : 0), z1 = hz - (dir < 0 ? 0.002 : 0);
        double ox = 1.84; // (the hang socket on the starboard edge: the leaf's own frame is centred under it)
        double X(double x) => x - ox;
        Fx(p, X(-hx), -t, z0, X(hx), 0, z1, "#5a5d5c", "plated steel");
        // The underside: a frame round the edge, three ribs across, a run of bolts down each rib, rollers at the rail edges.
        Fx(p, X(-hx + 0.04), -t - 0.05, z0 + 0.04, X(hx - 0.1), -t + 0.004, z0 + 0.12, Frame, "dirty steel");
        Fx(p, X(-hx + 0.04), -t - 0.05, z1 - 0.12, X(hx - 0.1), -t + 0.004, z1 - 0.04, Frame, "dirty steel");
        foreach (var x in new[] { -hx + 0.04, hx - 0.18 })
            Fx(p, X(x), -t - 0.05, z0 + 0.12, X(x + 0.08), -t + 0.004, z1 - 0.12, Frame, "dirty steel");
        foreach (var x in new[] { -1.1, 0.0, 1.1 })
        {
            Fx(p, X(x - 0.06), -t - 0.08, z0 + 0.12, X(x + 0.06), -t + 0.004, z1 - 0.12, "#3a3c3a", "dirty steel");
            for (var i = 0; i < 4; i++)
            {
                var z = z0 + 0.3 + i * (z1 - z0 - 0.6) / 3;
                Fx(p, X(x - 0.025), -t - 0.096, z - 0.025, X(x + 0.025), -t - 0.076, z + 0.025, "#9a9c98", "steel", detail: true);
            }
        }
        // The meeting edge: a rubber seal and a hazard band on the plate's underside beside it.
        Fx(p, X(-hx + 0.12), -t - 0.03, Math.Min(m, m + dir * 0.0), X(hx - 0.2), -0.02, m + dir * 0.03, Frame, "rubber", detail: true);
        double hb0 = m + dir * 0.13, hb1 = m + dir * 0.33;
        Fx(p, X(-hx + 0.14), -t - 0.016, Math.Min(hb0, hb1), X(hx - 0.2), -t + 0.004, Math.Max(hb0, hb1), Hazard, "hazard paint", detail: true);
        foreach (var x in new[] { -hx + 0.02, hx - 0.06 })
            foreach (var z in new[] { z0 + 0.25, z1 - 0.25 })
                Fx(p, X(x), -t - 0.03, z - 0.07, X(x + 0.05), -t + 0.004, z + 0.07, "#9a9c98", "steel", detail: true); // (rollers)
        p.Sock("hang", [0, 0, 0], "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", dir < 0 ? "-z" : "+z"), ("travel", WellLeaf + 0.05), ("seconds", 3.0), ("hang", "hang"))));
        return p.Stats(900, 6000, 35, 6.0);
    }

    /// <summary>The galley: a sealed cook module on a plinth, two induction plates under a fiddle rail, a latched oven, a vented hood
    /// over it on two struts.</summary>
    private static Part GalleyUnit()
    {
        var p = new Part("Magpie Galley", Small, Section, "The Magpie's galley: a sealed cook module bolted to the deck, induction plates under a fiddle rail, a latched oven and a vented hood.", role: "storage", tool: "wrench", material: "steel", color: Steel, group: Group);
        Fx(p, -0.5, 0, -0.35, 0.5, 0.1, 0.33, Frame, "dirty steel"); // (the plinth, kicked back)
        Case(p, -0.48, 0.1, -0.35, 0.48, 0.86, 0.36, 0.03, "#b8bdb4", "painted steel");
        Fx(p, -0.5, 0.86, -0.35, 0.5, 0.92, 0.38, "#3a3e40", "steel"); // (the top)
        foreach (var x in new[] { -0.22, 0.22 })
        {
            Fx(p, x - 0.17, 0.916, -0.15, x + 0.17, 0.936, 0.19, "#1d2022", "plastic", detail: true);
            Fx(p, x - 0.1, 0.932, -0.05, x + 0.1, 0.95, 0.09, "#ff7040", "glass", detail: true, glow: true);
        }
        // The fiddle rail: posts and a bar round the front and sides, so a pan stays put when the ship lurches.
        foreach (var x in new[] { -0.47, 0.0, 0.47 })
            Fx(p, x - 0.015, 0.92, 0.33, x + 0.015, 1.02, 0.36, "#9aa0a4", "chrome", detail: true);
        Fx(p, -0.49, 1.0, 0.33, 0.49, 1.025, 0.36, "#9aa0a4", "chrome", detail: true);
        foreach (var x in new[] { -0.485, 0.485 })
            Fx(p, x - 0.015, 1.0, -0.2, x + 0.015, 1.025, 0.36, "#9aa0a4", "chrome", detail: true);
        // The oven: a heavy door with a window, two latches and a hazard edge; a control strip over it.
        Fx(p, -0.4, 0.18, 0.352, 0.4, 0.66, 0.38, "#9ea39a", "painted steel", detail: true);
        Fx(p, -0.28, 0.3, 0.374, 0.28, 0.56, 0.39, "#40302a", "glass", detail: true);
        foreach (var x in new[] { -0.34, 0.34 })
            Fx(p, x - 0.03, 0.56, 0.374, x + 0.03, 0.64, 0.4, Frame, "steel", detail: true);
        Fx(p, -0.4, 0.66, 0.352, 0.4, 0.68, 0.376, Hazard, "hazard paint", detail: true);
        Fx(p, -0.4, 0.7, 0.352, 0.4, 0.84, 0.372, Console, "plastic", detail: true);
        for (var i = 0; i < 4; i++)
            Fx(p, -0.3 + i * 0.16, 0.74, 0.366, -0.24 + i * 0.16, 0.8, 0.384, i == 0 ? Red : Amber, "plastic", detail: true, glow: true);
        // The hood: two struts off the back, a vented box, a lamp under it.
        foreach (var x in new[] { -0.44, 0.44 })
            Fx(p, x - 0.03, 0.92, -0.34, x + 0.03, 1.62, -0.28, Frame, "dirty steel");
        Fx(p, -0.5, 1.62, -0.35, 0.5, 1.86, 0.2, "#b8bdb4", "painted steel");
        for (var i = 0; i < 5; i++)
            Fx(p, -0.4 + i * 0.18, 1.66, 0.196, -0.3 + i * 0.18, 1.82, 0.214, "#2a2e2b", "soot", detail: true);
        Fx(p, -0.3, 1.596, -0.15, 0.3, 1.624, 0.05, Glow, "glass", detail: true, glow: true);
        Fx(p, -0.5, 1.86, -0.35, 0.5, 1.9, 0.2, Frame, "dirty steel");
        Bolts(p, -0.45, 0.45, 0.05, 0.33, 4);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.45, -0.35], "-z");
        p.Set(("storage", Kw(("slots", Py.L(Slot(-0.44, 0.14, -0.31, 0.44, 0.82, 0.32, "oven"))))));
        return p.Stats(110, 300, 5, 2.5);
    }

    /// <summary>The brew unit: a wall-hung pressure urn on a rail, gauge and tap, cups clipped in a rack, a drip grate.</summary>
    private static Part BrewUnit()
    {
        var p = new Part("Magpie Brew Unit", Small, Section, "The Magpie's brew unit: a pressure urn hung on a wall rail, its gauge and tap, cups clipped in a rack over a drip grate. Coffee for the night shift.", role: "frame", tool: "wrench", material: "steel", color: Steel, group: Group);
        Fx(p, -0.37, 0.125, -0.3, 0.37, 1.45, -0.24, Frame, "dirty steel"); // (the back plate on its rail)
        Fx(p, -0.37, 0.0, -0.3, 0.37, 0.125, 0.3, Frame, "dirty steel"); // (the base tray)
        Fx(p, -0.33, 0.121, -0.2, 0.33, 0.14, 0.26, Grate, "painted grating", detail: true);
        foreach (var x in new[] { -0.33, 0.33 })
            Fx(p, x - 0.03, 0.14, 0.2, x + 0.03, 0.5, 0.26, "#3a3e40", "steel");
        // The urn: a faceted drum, a band, a lid, a gauge, a tap.
        Prism(p, Ngon(0, 0.5, -0.02, 0.22, 8), Ngon(0, 1.15, -0.02, 0.22, 8), "#9aa0a4", "steel");
        Prism(p, Ngon(0, 0.78, -0.02, 0.232, 8), Ngon(0, 0.84, -0.02, 0.232, 8), Orange, "worn paint");
        Fx(p, -0.08, 1.15, -0.1, 0.08, 1.24, 0.06, Frame, "steel");
        Fx(p, -0.07, 0.95, 0.2, 0.07, 1.09, 0.225, "#e8e4d8", "gloss paint", detail: true);
        Fx(p, -0.005, 1.0, 0.222, 0.05, 1.016, 0.238, Red, "plastic", detail: true);
        Fx(p, -0.03, 0.55, 0.15, 0.03, 0.6, 0.3, Frame, "steel");
        Fx(p, -0.015, 0.5, 0.27, 0.015, 0.56, 0.3, Frame, "steel");
        // The cup rack: a bar of clips, mugs in them.
        Fx(p, -0.34, 1.3, -0.24, 0.34, 1.32, -0.08, "#3a3e40", "steel");
        for (var i = 0; i < 4; i++)
            Fx(p, -0.3 + i * 0.17, 1.32, -0.2, -0.22 + i * 0.17, 1.42, -0.12, i % 2 == 0 ? "#d8d4c4" : "#5a7a8a", "gloss paint");
        Fx(p, -0.36, 0.3, -0.244, 0.36, 0.34, -0.226, Hazard, "hazard paint", detail: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 1.45, 0], "+y");
        p.Sock("wall", [0, 0.125, -0.3], "-z");
        return p.Stats(60, 200, 4, 2.0);
    }

    /// <summary>The mess table: a lipped steel top on lockers and one pedestal flanged to the deck, a tie-down rail along each side,
    /// a lidded condiment box clipped in the middle.</summary>
    private static Part MessTable()
    {
        var p = new Part("Magpie Mess Table", Small, Section, "The Magpie's mess table: a lipped steel top on lockers and a pedestal bolted to the deck, tie-down rails along its sides.", role: "storage", tool: "wrench", material: "steel", color: Steel, group: Group);
        Fx(p, -0.34, 0, -0.3, 0.34, 0.03, 0.3, Frame, "dirty steel"); // (the deck flange)
        Bolts(p, -0.3, 0.3, 0.015, 0.3, 4);
        Fx(p, -0.12, 0.03, -0.12, 0.12, 0.16, 0.12, Frame, "dirty steel");
        Case(p, -0.8, 0.16, -0.36, 0.8, 0.68, 0.36, 0.03, "#6c7270", "painted steel"); // (the lockers under the top)
        Fx(p, -0.015, 0.19, -0.33, 0.015, 0.65, 0.33, "#6c7270", "painted steel");
        foreach (var x in new[] { -0.4, 0.4 })
        {
            Fx(p, x - 0.36, 0.2, 0.356, x + 0.36, 0.64, 0.372, "#80878a", "painted steel", detail: true);
            Fx(p, x - 0.08, 0.56, 0.366, x + 0.08, 0.59, 0.384, Frame, "steel", detail: true);
            Fx(p, x - 0.36, 0.2, -0.372, x + 0.36, 0.64, -0.356, "#80878a", "painted steel", detail: true);
        }
        Fx(p, -0.9, 0.68, -0.45, 0.9, 0.73, 0.45, "#a6aca4", "painted steel");
        // The lip and the tie-down rails.
        Fx(p, -0.9, 0.73, -0.45, 0.9, 0.75, -0.41, Frame, "dirty steel");
        Fx(p, -0.9, 0.73, 0.41, 0.9, 0.75, 0.45, Frame, "dirty steel");
        Fx(p, -0.9, 0.73, -0.41, -0.86, 0.75, 0.41, Frame, "dirty steel");
        Fx(p, 0.86, 0.73, -0.41, 0.9, 0.75, 0.41, Frame, "dirty steel");
        foreach (var z in new[] { -0.47, 0.47 })
            Fx(p, -0.8, 0.6, z - 0.015, 0.8, 0.625, z + 0.015, "#9aa0a4", "chrome", detail: true);
        Fx(p, -0.12, 0.746, -0.08, 0.12, 0.82, 0.08, Orange, "worn paint");
        Fx(p, -0.13, 0.816, -0.09, 0.13, 0.836, 0.09, Frame, "plastic", detail: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("storage", Kw(("slots", Py.L(Slot(-0.76, 0.2, -0.32, -0.02, 0.64, 0.32, "locker"), Slot(0.02, 0.2, -0.32, 0.76, 0.64, 0.32, "locker"))))));
        return p.Stats(90, 220, 3, 2.0);
    }

    /// <summary>The kit locker: a tall steel locker on a wall rail, slotted vents in its door, two latch bars, a stencil band; a
    /// helmet strapped on top.</summary>
    private static Part KitLocker()
    {
        var p = new Part("Magpie Locker", Small, Section, "A kit locker: tall steel, hung on a wall rail and latched shut, vents in its door, a helmet strapped on top.", role: "storage", tool: "wrench", material: "steel", color: "#6a737b", group: Group);
        Fx(p, -0.3, 0, -0.25, 0.3, 0.08, 0.2, Frame, "dirty steel"); // (the kick plinth, set back)
        Case(p, -0.3, 0.08, -0.25, 0.3, 1.82, 0.24, 0.025, "#6a737b", "painted steel", 0.95);
        Fx(p, -0.304, 1.82, -0.25, 0.304, 1.86, 0.258, Frame, "dirty steel");
        Fx(p, -0.27, 0.12, 0.236, 0.27, 1.78, 0.252, "#7a838b", "painted steel", detail: true);
        for (var i = 0; i < 6; i++)
            Fx(p, -0.16, 1.5 + i * 0.045, 0.248, 0.16, 1.52 + i * 0.045, 0.264, Frame, "soot", detail: true);
        foreach (var y in new[] { 0.55, 1.15 })
            Fx(p, 0.17, y, 0.248, 0.23, y + 0.2, 0.272, "#9aa0a4", "chrome", detail: true);
        Fx(p, -0.27, 0.96, 0.248, 0.27, 1.02, 0.264, Hazard, "hazard paint", detail: true);
        Fx(p, -0.3, 1.86, -0.25, 0.3, 1.88, -0.2, "#3a3e40", "steel"); // (the rail it hangs from)
        Prism(p, Ngon(0, 1.862, 0, 0.13, 8), Ngon(0, 1.98, 0, 0.12, 8), "#d8d4c4", "gloss paint");
        Fx(p, -0.14, 1.9, -0.015, 0.14, 1.92, 0.015, Frame, "rubber", detail: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 1.86, 0], "+y");
        p.Sock("back", [0, 0.95, -0.25], "-z");
        p.Sock("wall", [0, 0.125, -0.25], "-z");
        p.Set(("storage", Kw(("slots", Py.L(Slot(-0.26, 0.12, -0.21, 0.26, 0.92, 0.2), Slot(-0.26, 0.98, -0.21, 0.26, 1.78, 0.2))))));
        return p.Stats(50, 260, 5, 2.0);
    }

    /// <summary>The fold desk: a work top on two wall brackets, a screen and keypad set into a backboard on the wall, a drawer box under.</summary>
    private static Part FoldDesk()
    {
        var p = new Part("Magpie Fold Desk", Small, Section, "A fold desk: a work top on wall brackets, a screen and keypad on its backboard, a drawer box under it. Nothing stands on the deck.", role: "frame", tool: "wrench", material: "steel", color: Steel, group: Group);
        Fx(p, -0.7, 0.125, -0.35, 0.7, 1.35, -0.31, "#3a3e40", "dirty steel"); // (the backboard on the wall)
        Fx(p, -0.7, 0.72, -0.31, 0.7, 0.76, 0.35, "#a6aca4", "painted steel");
        Fx(p, -0.704, 0.7, 0.33, 0.704, 0.772, 0.372, Frame, "rubber");
        foreach (var x in new[] { -0.6, 0.6 })
            Prism(p, [[x - 0.025, 0.3, -0.31], [x + 0.025, 0.3, -0.31], [x + 0.025, 0.3, -0.27], [x - 0.025, 0.3, -0.27]], [[x - 0.025, 0.716, -0.31], [x + 0.025, 0.716, -0.31], [x + 0.025, 0.716, 0.3], [x - 0.025, 0.716, 0.3]], Frame, "dirty steel");
        Fx(p, 0.15, 0.44, -0.31, 0.56, 0.716, 0.2, "#6c7270", "painted steel"); // (the drawer box)
        Fx(p, 0.18, 0.47, 0.196, 0.53, 0.67, 0.212, "#80878a", "painted steel", detail: true);
        Fx(p, 0.3, 0.6, 0.206, 0.42, 0.62, 0.224, Frame, "steel", detail: true);
        Fx(p, -0.5, 0.86, -0.316, 0.1, 1.26, -0.296, Console, "plastic", detail: true);
        Fx(p, -0.46, 0.9, -0.3, 0.06, 1.22, -0.286, Screen, "glass", detail: true, glow: true);
        Fx(p, 0.18, 0.88, -0.316, 0.5, 1.08, -0.296, Console, "plastic", detail: true);
        for (var i = 0; i < 3; i++)
            Fx(p, 0.22 + i * 0.1, 0.92, -0.3, 0.28 + i * 0.1, 0.98, -0.286, i == 2 ? Green : Amber, "plastic", detail: true, glow: true);
        Fx(p, -0.7, 1.3, -0.314, 0.7, 1.33, -0.294, Hazard, "hazard paint", detail: true);
        p.Sock("top", [0, 0.76, 0], "+y");
        p.Sock("wall", [0, 0.125, -0.35], "-z");
        return p.Stats(55, 200, 4, 2.0);
    }

    /// <summary>The bunk: a mattress in a steel tray framed to the wall, restraint straps across it, a curtain on a rail above, a
    /// reading lamp and a shelf in the frame's head.</summary>
    private static Part Bunk()
    {
        var p = new Part("Magpie Bunk", Small, Section, "A crew bunk: a mattress in a steel tray framed to the wall, restraint straps for a hard landing, a curtain rail, a reading lamp.", role: "seat", tool: "wrench", material: "steel", color: "#3e4a5a", group: Group);
        Fx(p, -0.4, 0, -0.96, 0.44, 0.3, 0.96, "#3a3e40", "dirty steel"); // (the drawer base)
        foreach (var z in new[] { -0.47, 0.47 })
            Fx(p, -0.3, 0.06, z - 0.4, 0.3, 0.26, z + 0.4, "#4a5258", "painted steel", detail: true);
        Fx(p, -0.396, 0.3, -0.96, 0.42, 0.46, 0.96, "#3e4a5a", "fabric");
        Fx(p, -0.38, 0.456, -0.94, 0.38, 0.54, -0.64, "#c8c4b4", "fabric"); // (the pillow)
        foreach (var z in new[] { -0.2, 0.5 })
            Fx(p, -0.39, 0.4, z - 0.04, 0.425, 0.48, z + 0.04, Orange, "rubber", detail: true);
        // The frame: end panels, a top, a curtain rail and its curtain gathered at the foot.
        Fx(p, -0.4, 0, -1.0, 0.444, 1.6, -0.96, "#a6aca4", "painted steel");
        Fx(p, -0.4, 0, 0.96, 0.444, 1.6, 1.0, "#a6aca4", "painted steel");
        Fx(p, -0.4, 1.556, -0.96, 0.44, 1.6, 0.96, "#a6aca4", "painted steel");
        Fx(p, 0.38, 1.49, -0.96, 0.41, 1.52, 0.96, "#9aa0a4", "chrome", detail: true);
        Fx(p, 0.34, 0.5, 0.74, 0.42, 1.48, 0.954, "#5a6a5a", "fabric");
        Fx(p, -0.3, 0.9, -0.96, -0.04, 0.98, -0.8, "#6c7270", "painted steel"); // (the head shelf)
        Fx(p, 0.08, 1.2, -0.962, 0.22, 1.3, -0.944, Glow, "glass", detail: true, glow: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [-0.4, 0.125, 0], "-x");
        p.Set(("seat", Kw(("at", Py.L(0, 0.15, 0)), ("bunk", true))));
        return p.Stats(100, 240, 4, 2.5);
    }

    /// <summary>The med bed: a padded bed on a hazard-striped lift ram bolted to the deck, a monitor and a lamp arm on its head panel.</summary>
    private static Part MedBed()
    {
        var p = new Part("Magpie Med Bed", Small, Section, "The Magpie's med bed: a padded bed on a lift ram bolted to the deck, an arm lamp and a monitor at its head. Lie on it to be patched up.", role: "seat", tool: "wrench", material: "steel", color: "#d8dcd8", group: Group);
        Fx(p, -0.36, 0, -0.5, 0.36, 0.05, 0.6, Frame, "dirty steel");
        Bolts(p, -0.3, 0.3, 0.025, 0.6, 3);
        Fx(p, -0.14, 0.05, -0.1, 0.14, 0.45, 0.2, "#9aa0a4", "chrome");
        Fx(p, -0.145, 0.2, -0.105, 0.145, 0.26, 0.205, Hazard, "hazard paint", detail: true);
        Fx(p, -0.4, 0.45, -0.95, 0.4, 0.58, 1.0, "#d8dcd8", "gloss paint");
        Fx(p, -0.37, 0.576, -0.92, 0.37, 0.68, 0.96, "#5a8a8a", "rubber");
        Fx(p, -0.405, 0.5, -0.6, 0.405, 0.54, 0.7, Green, "plastic", detail: true);
        // The head: a panel on the back socket, a monitor on it, a lamp on an arm over the bed.
        Fx(p, -0.4, 0.45, -1.0, 0.4, 1.6, -0.954, "#d8dcd8", "gloss paint");
        Fx(p, -0.3, 1.0, -0.958, 0.06, 1.3, -0.9, Console, "plastic");
        Fx(p, -0.27, 1.03, -0.904, 0.03, 1.27, -0.888, Screen, "glass", detail: true, glow: true);
        Fx(p, 0.18, 1.5, -0.958, 0.24, 1.56, -0.2, "#9aa0a4", "chrome");
        Fx(p, 0.08, 1.44, -0.32, 0.34, 1.504, -0.12, "#d8dcd8", "gloss paint");
        Fx(p, 0.1, 1.424, -0.3, 0.32, 1.444, -0.14, Glow, "glass", detail: true, glow: true);
        Fx(p, -0.2, 1.4, -0.96, 0.2, 1.48, -0.946, Red, "gloss paint", detail: true);
        Fx(p, -0.04, 1.36, -0.96, 0.04, 1.52, -0.946, "#f0f0f0", "gloss paint", detail: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 1.6, 0], "+y");
        p.Sock("back", [0, 0.8, -1.0], "-z");
        p.Set(("seat", Kw(("at", Py.L(0, 0.15, 0.2)), ("heal", 4))));
        return p.Stats(140, 260, 3, 3.0);
    }

    /// <summary>The med locker: a sealed white case on a wall plate, a green cross, a latch, a strip light over it.</summary>
    private static Part MedLocker()
    {
        var p = new Part("Magpie Med Locker", Small, Section, "A sealed medical locker on the wall: dressings, stims and the splint kit behind a latched door with a green cross.", role: "storage", tool: "wrench", material: "steel", color: "#d8dcd8", group: Group);
        Fx(p, -0.4, 0.5, -0.175, 0.4, 1.6, -0.15, "#3a3e40", "dirty steel"); // (the wall plate)
        Case(p, -0.37, 0.55, -0.15, 0.37, 1.5, 0.15, 0.025, "#d8dcd8", "gloss paint", 1.02);
        Fx(p, -0.34, 0.58, 0.146, 0.34, 1.47, 0.162, "#e8ece8", "gloss paint", detail: true);
        Fx(p, -0.12, 1.05, 0.158, 0.12, 1.11, 0.174, Green, "gloss paint", detail: true);
        Fx(p, -0.03, 0.96, 0.158, 0.03, 1.2, 0.176, Green, "gloss paint", detail: true);
        Fx(p, 0.26, 0.85, 0.158, 0.31, 1.0, 0.186, "#9aa0a4", "chrome", detail: true);
        Fx(p, -0.374, 1.5, -0.15, 0.374, 1.56, 0.12, Frame, "dirty steel");
        Fx(p, -0.3, 1.488, -0.1, 0.3, 1.504, 0.08, "#e0f0e8", "glass", detail: true, glow: true);
        p.Sock("top", [0, 1.6, -0.16], "+y");
        p.Sock("back", [0, 0.85, -0.175], "-z");
        p.Set(("storage", Kw(("slots", Py.L(Slot(-0.33, 0.6, -0.12, 0.33, 1.0, 0.12, "dressings"), Slot(-0.33, 1.04, -0.12, 0.33, 1.44, 0.12, "kit"))))));
        return p.Stats(30, 160, 3, 2.0);
    }

    /// <summary>The cargo rack: two bays of steel shelves between uprights bolted to the wall, a cargo net over each, lashing points.</summary>
    private static Part CargoRack()
    {
        var p = new Part("Magpie Cargo Rack", Small, Section, "A cargo rack: steel shelves between uprights bolted to the wall, a cargo net strapped over each bay, lashing points on the posts.", role: "storage", tool: "wrench", material: "steel", color: "#6b6f5a", group: Group);
        foreach (var x in new[] { -1.0, -0.03, 0.94 })
            Fx(p, x, 0, -0.25, x + 0.06, 2.0, 0.25, "#6b6f5a", "worn paint");
        foreach (var y in new[] { 0.04, 0.68, 1.32, 1.96 })
        {
            Fx(p, -0.94, y - 0.04, -0.246, -0.03, y, 0.22, "#4a4e48", "dirty steel");
            Fx(p, 0.03, y - 0.04, -0.246, 0.94, y, 0.22, "#4a4e48", "dirty steel");
        }
        foreach (var x0 in new[] { -0.94, 0.03 })
        {
            for (var i = 0; i < 4; i++)
                Fx(p, x0 + 0.08 + i * 0.22, 0.06, 0.232, x0 + 0.1 + i * 0.22, 1.9, 0.248, "#2a2e2b", "rubber", detail: true);
            foreach (var y in new[] { 0.45, 1.1, 1.75 })
                Fx(p, x0 + 0.02, y, 0.236, x0 + 0.89, y + 0.02, 0.252, "#2a2e2b", "rubber", detail: true);
        }
        foreach (var x in new[] { -1.0, -0.03, 0.94 })
            Fx(p, x - 0.004, 1.0, 0.246, x + 0.064, 1.06, 0.266, Hazard, "hazard paint", detail: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 2.0, 0], "+y");
        p.Sock("back", [0, 1.0, -0.25], "-z");
        p.Sock("wall", [0, 0.125, -0.25], "-z");
        p.Set(("storage", Kw(("slots", Py.L(Slot(-0.9, 0.08, -0.21, -0.07, 0.6, 0.21), Slot(0.07, 0.08, -0.21, 0.9, 0.6, 0.21), Slot(-0.9, 0.72, -0.21, -0.07, 1.24, 0.21),
            Slot(0.07, 0.72, -0.21, 0.9, 1.24, 0.21), Slot(-0.9, 1.36, -0.21, -0.07, 1.88, 0.21), Slot(0.07, 1.36, -0.21, 0.9, 1.88, 0.21))))));
        return p.Stats(70, 240, 4, 2.0);
    }

    /// <summary>The flight computer: a low console between the couches, a flight stabilizer with a ship-size gyro in it. Fit it level, its arrow forward.</summary>
    private static Part FlightComputer()
    {
        var p = new Part("Magpie Flight Computer", Small, Section, "The Magpie's flight computer: a flight stabilizer with a ship-size gyro in a console between the couches. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: Console, group: Group);
        p.Add(Box([-0.35, 0, -0.3], [0.35, 0.9, 0.3], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.3, 0.45, 0.3], [0.3, 0.8, 0.318], Screen, detail: true, shape: Kw(("glow", true)))); // (its screen faces the couches)
        for (var i = 0; i < 4; i++)
            p.Add(Box([-0.24 + i * 0.14, 0.3, 0.3], [-0.18 + i * 0.14, 0.36, 0.318], i % 2 == 0 ? Green : Amber, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.05, 0.901, -0.22], [0.05, 0.917, 0.1], Hazard, detail: true, shape: Kw(("finish", "hazard paint")))); // (the arrow, forward)
        p.Add(Box([-0.12, 0.901, -0.22], [0.12, 0.917, -0.14], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 10000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(300, 600, 8, 3.0);
    }

    /// <summary>The power plant: a squat stack of fuel-cell drums in a frame, coolant loops round it. Makes the ship's power, no fuel.</summary>
    private static Part PowerPlant()
    {
        var p = new Part("Magpie Power Plant", Small, Section, "The Magpie's power plant: a stack of cells in a cage, 400 kW for the drills, the rams and the lights. No fuel.", role: "generator", tool: "welder", material: "steel", color: Yellow2, group: Group);
        double[] O(double x, double y, double z, double r, int i, int n = 8)
        {
            var a = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(x + r * Math.Cos(a), 4), y, Py.Round(z + r * Math.Sin(a), 4)];
        }
        p.Add(Box([-0.85, 0, -0.85], [0.85, 0.2, 0.85], Frame, shape: Kw(("finish", "dirty steel"))));
        foreach (var (x, z) in new[] { (-0.4, -0.4), (0.4, -0.4), (-0.4, 0.4), (0.4, 0.4) })
        {
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(x, 0.2, z, 0.34, i))], [.. Enumerable.Range(0, 8).Select(i => O(x, 1.7, z, 0.34, i))], Yellow2, "worn paint");
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(x, 1.0, z, 0.36, i))], [.. Enumerable.Range(0, 8).Select(i => O(x, 1.08, z, 0.36, i))], "#ffb060", "glass", glow: true);
        }
        foreach (var (x, z) in new[] { (-0.82, -0.82), (0.82, -0.82), (-0.82, 0.82), (0.82, 0.82) })
            p.Add(Box([x - 0.04, 0.2, z - 0.04], [x + 0.04, 1.9, z + 0.04], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.86, 1.86, -0.86], [0.86, 1.94, 0.86], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        foreach (var (x, col) in new[] { (-0.1, PipeRed), (0.1, PipeBlue) })
            p.Add(Box([x - 0.05, 1.7, -0.05], [x + 0.05, 2.6, 0.05], col, detail: true, shape: Kw(("finish", "painted steel"))));
        p.Add(Box([-0.78, 0.2, -0.78], [0.78, 1.7, 0.78], hidden: true));
        p.Add(Box([-0.84, 0.2, -0.862], [-0.5, 0.45, -0.84], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 400), ("spool", 6), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.1, 0.85)), ("face", "+z")))));
        return p.Stats(2500, 3000, 25, 10.0);
    }

    // ---- welding the structure: a facing socket pair wherever two parts' solid boxes touch (the Halberd's Connect)

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = new int[parts.Count];
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var (bi, bj) = (bounds[i], bounds[j]);
                if (Enumerable.Range(0, 3).Any(a => bi.Mn[a] > bj.Mx[a] + 0.002 || bj.Mn[a] > bi.Mx[a] + 0.002))
                    continue;
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
                    if (da[1] == db[1] && da[0] != db[0])
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
            throw new InvalidOperationException("miner_parts: parts welded to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
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

    private static int LampAt(BuildingParts.Blueprint bp, int on, string sock, string colour = "#fff0d0")
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

    private static int Door(BuildingParts.Blueprint bp, int on, string sock) => Paint(bp, bp.Hang("Slide Door 1.5x2.25 m", on, sock, "hang"), Orange, "worn paint");

    /// <summary>The Drill Module the ship carries: tier 1 (iron, stone, sand); a later tier is the same socket.</summary>
    public const string DrillPart = "Drill Module";

    private static PyDict Blueprint(Dictionary<string, PyDict> defs, List<string> structure)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "A mining ship: it sets down by a deposit and lowers its stern ramp; its crew carry the drills out from their docks, and fill the ore hoppers on its flanks, then fly the load to a refinery.", defs, maxSteps: 200);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var st = new Dictionary<string, int>();
        foreach (var name in structure)
            st[name] = st.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int keel = st["Magpie Keel"], bow = st["Magpie Bow"], work = st["Magpie Workshop Deck"], bay = st["Magpie Drill Bay"], eng = st["Magpie Engine Deck"],
            uf = st["Magpie Bridge Deck"], ua = st["Magpie Mess Deck"], stern = st["Magpie Stern"];
        int[] hoppers = [st["Magpie Ore Hopper L"], st["Magpie Ore Hopper R"]], nacelles = [st["Magpie Nacelle L"], st["Magpie Nacelle R"]];
        // Flight: a drive on each nacelle's tail, lift jets under the hoppers and the nacelles, retro jets in the bow's cheeks, the legs.
        var lifts = new List<int>();
        foreach (var n in nacelles)
        {
            bp.Hang("Magpie Drive", n, "drive", "front");
            bp.Hang("Drive Core S", n, "core", "front"); // (its engine, inside the housing on the drive's axis, bolted to the tail plate)
            Works.HangHatch(bp, n, n == nacelles[0] ? -1 : 1);
            lifts.Add(bp.Hang("Magpie Lift Jet", n, "lift", "top"));
        }
        foreach (var h in hoppers)
            lifts.Add(bp.Hang("Magpie Lift Jet", h, "lift", "top"));
        foreach (var h in hoppers)
            bp.Hang("Magpie Retro Jet", h, "retro", "top");
        // Attitude jets pointing every way fore and aft (Karl, 2026-11-15): it turns about any axis and stops any way.
        foreach (var t in hoppers.Concat(nacelles))
            foreach (var sock in new[] { "rcsUp", "rcsDown", "rcsOut" })
                bp.Hang("Heavy RCS Thruster", t, sock, "base");
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            bp.Hang("Magpie Landing Leg", keel, n, "top");
        // Getting about: the boarding ladder off the porch, a ladder up from the workshop and one from engineering.
        Paint(bp, bp.Hang("Magpie Stern Ramp", eng, "ramp", "hang"), Yellow2, "worn paint"); // (F lowers it to the ground: the way in)
        bp.Hang("Magpie Ladder", work, "ladder", "bottom");
        bp.Hang("Magpie Ladder", eng, "ladder", "bottom");
        // The workshop.
        bp.Hang("Tool Rack", work, "toolRack", "back");
        Paint(bp, bp.Hang("Magpie Locker", work, "locker0", "wall"), Yellow2, "worn paint");
        Paint(bp, bp.Hang("Magpie Locker", work, "locker1", "wall"), "#5d6a60", "worn paint");
        bp.Hang("Magpie Fold Desk", work, "desk", "wall");
        bp.Hang("Requisition Crate", work, "crate0", "-y");
        bp.Hang("Requisition Crate", work, "crate1", "-y");
        Paint(bp, bp.Hang("Magpie Cargo Rack", work, "shelf", "wall"), "#6b6f5a", "worn paint");
        Switch(bp, work, "switch", LampAt(bp, work, "lampA"), LampAt(bp, work, "lampB"));
        // The drill bay: two drills in their docks on the aft bulkhead, a battery on each so it digs off the ship; the ore hoppers'
        // units by the side windows (F tips in the ore a player carries, or takes a load).
        for (var s = 0; s < 2; s++)
        {
            var hold = bp.Hang("Storage Volume", bay, s == 0 ? "holdL" : "holdR", "-y");
            var sign = s == 0 ? -1 : 1;
            var partX = Cx + sign * (Lw - 0.3);
            bp.Steps[hold]["config"] = new PyDict
            {
                ["sizeX"] = Py.Round(FillH1 - FillH0, 2), ["sizeY"] = Py.Round(FillY1 - FillY0, 2), ["sizeZ"] = Py.Round(FillZ1 - FillZ0, 2),
                ["atX"] = Py.Round(Cx + sign * (FillH0 + FillH1) / 2 - partX, 2), ["atY"] = Py.Round((FillY0 + FillY1) / 2 - (Ft0 + 0.2), 2), ["atZ"] = Py.Round((FillZ0 + FillZ1) / 2 - 14.25, 2),
            };
            Paint(bp, hold, Yellow2, "worn paint");
        }
        for (var i = 0; i < DockX.Length; i++)
        {
            var dock = bp.Hang(DockName, bay, $"dock{i}", "wall");
            var drill = bp.Hang(DrillPart, dock, "dock", "back");
            bp.Hang("Battery", drill, "-x", "+x");
        }
        Switch(bp, bay, "switchFore", LampAt(bp, bay, "lampFL", "#ffe0a0"), LampAt(bp, bay, "lampFR", "#ffe0a0"));
        Switch(bp, bay, "switchAft", LampAt(bp, bay, "lampAL", "#ffe0a0"), LampAt(bp, bay, "lampAR", "#ffe0a0"));
        Door(bp, bay, "doorFore");
        Door(bp, bay, "doorAft");
        // The cargo bay aft: engineering along its port side, the suit lockers, the ramp.
        bp.Hang("Magpie Power Plant", eng, "reactor", "bottom");
        for (var i = 0; i < 3; i++)
            bp.Hang("Battery", eng, $"battery{i}", "-y");
        bp.Hang("Power Board", eng, "board", "back");
        bp.Hang("Pipe Bank", eng, "pipes", "back");
        bp.Hang("Engineer's Panel", eng, "panel", "back");
        for (var i = 0; i < 3; i++)
            Paint(bp, bp.Hang("Magpie Locker", eng, $"suit{i}", "wall"), Hazard, "worn paint");
        bp.Hang("Tool Rack", eng, "toolRack", "back");
        for (var i = 0; i < 3; i++)
            bp.Hang("Robot Shell", bp.Hang("Robot Shell Dock", eng, $"shellDock{i}", "wall"), "dock", "back");
        Switch(bp, eng, "switchEng", LampAt(bp, eng, "lampEngA", "#a0d0ff"));
        Switch(bp, eng, "switchBayR", LampAt(bp, eng, "lampBayA"), LampAt(bp, stern, "lampPorch", "#ffd890"));
        Switch(bp, eng, "switchBayL", LampAt(bp, eng, "lampBayB"), LampAt(bp, eng, "lampEngB", "#a0d0ff"));
        // The bridge: the pilot flies through the flight computer between the couches (Rotorcraft keys, as the Halberd and the Zero Day do).
        var pilot = bp.Hang("Flight Couch", uf, "pilot", "bottom");
        bp.Hang("Flight Couch", uf, "copilot", "bottom");
        var fc = bp.Hang("Magpie Flight Computer", uf, "module", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, fc));
        bp.Hang("Chart Table", uf, "chart", "back");
        Paint(bp, bp.Hang("Magpie Locker", uf, "bridgeLocker", "wall"), Yellow2, "worn paint");
        Paint(bp, bp.Hang("Magpie Cargo Rack", uf, "shelf", "wall"), "#6b6f5a", "worn paint");
        bp.Hang("Magpie Bunk", uf, "couchA", "wall");
        Paint(bp, bp.Hang("Magpie Locker", uf, "lockerA", "wall"), "#5d6a60", "worn paint");
        bp.Hang("Magpie Bunk", uf, "couchB", "wall");
        bp.Hang("Magpie Fold Desk", uf, "deskB", "wall");
        foreach (var d in new[] { "doorBridge", "doorCabinA", "doorCabinB" })
            Door(bp, uf, d);
        Switch(bp, uf, "switchBridge", LampAt(bp, uf, "lampBridgeL", "#ff6050"), LampAt(bp, uf, "lampBridgeR", "#ff6050"));
        Switch(bp, uf, "switchHall", LampAt(bp, uf, "lampHall"), LampAt(bp, uf, "lampCabinA", "#ffd8a0"));
        Switch(bp, uf, "switchCabinB", LampAt(bp, uf, "lampCabinB", "#ffd8a0"));
        // The head, the sick bay, the mess.
        bp.Hang("Ship Toilet", ua, "toilet", "wall");
        bp.Hang("Ship Basin", ua, "basin", "wall");
        Paint(bp, bp.Hang("Shower Stall", ua, "shower", "wall"), "#dfe4e2", "gloss paint");
        bp.Hang("Magpie Med Bed", ua, "med", "back");
        bp.Hang("Magpie Med Locker", ua, "cabinet", "back");
        bp.Hang("Magpie Galley", ua, "stove", "back");
        bp.Hang("Magpie Brew Unit", ua, "coffee", "wall");
        Paint(bp, bp.Hang("Magpie Locker", ua, "messLocker", "wall"), "#5c646a", "worn paint");
        bp.HangTurned("Magpie Mess Table", ua, "table", "bottom", BuildingParts.RY90);
        bp.Hang("Grow Rack 2 m", ua, "growL", "wall");
        bp.Hang("Grow Rack 2 m", ua, "growR", "wall");
        for (var i = 0; i < 3; i++)
            bp.HangTurned("Shell Control Chair", ua, $"linkChair{i}", "bottom", BuildingParts.RY90);
        foreach (var d in new[] { "doorHead", "doorMed", "doorMess" })
            Door(bp, ua, d);
        Switch(bp, ua, "switchHead", LampAt(bp, ua, "lampHead"));
        Switch(bp, ua, "switchMed", LampAt(bp, ua, "lampMed", "#f4fbff"));
        Switch(bp, ua, "switchMess", LampAt(bp, ua, "lampMessL", "#ffe0b0"), LampAt(bp, ua, "lampMessR", "#ffe0b0"));
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        List<Part> structure = [Keel(), Bow(), LowerFore(), DrillBay(), LowerAft(), UpperFore(), UpperAft(), Spine(), Hopper(-1), Hopper(1), Nacelle(-1), Nacelle(1), SternPart()];
        Connect(structure);
        var order = PlaceOrder(structure, "Magpie Keel");
        List<Part> parts =
        [
            .. structure, Drive(),
            Jet("Magpie Lift Jet", $"A lift jet: {LiftForce / 1e6:0.0} MN straight down. Four of them stand round the Magpie's middle.", LiftForce, 1.2),
            Jet("Magpie Retro Jet", $"A retro jet in the nose of a hopper's thruster pod: {RetroForce / 1000:0} kN forward, to stop it over a claim.", RetroForce, 0.8),
            Leg(),
            Ladder("Magpie Ladder", Ft1 - Ft0, "A ladder between the Magpie's decks: F climbs it.", "bottom", [0, 0, -0.095], "-y"),
            BoardingLadder(), BoardingLadderLower(), Ramp(),
            PowerPlant(), FlightComputer(),
            WellDoor(-1), WellDoor(1), GalleyUnit(), BrewUnit(), MessTable(), KitLocker(), FoldDesk(), Bunk(), MedBed(), MedLocker(), CargoRack(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        o.Own(FittingsOut);
        foreach (var (p, folder) in parts.Select(x => (x, Out)).Append((DrillDock(), FittingsOut)))
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{folder}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/MagpieMiningShip.json", PyJson.Dumps(Blueprint(defs, order), 2) + "\n");
    }
}
