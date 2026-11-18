using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Zero Day, a torpedo frigate in the IT style (Karl, 2026-11-15: "a state of the art faction torpedo frigate. Sleek lines, bigger
/// than the Halberd, countermeasures, torpedo bay on the side, rail gun, thruster the width of the ship ... make the levels run the length
/// of the ship instead. Try and make doors, features etc what you'd expect from a space ship"; the faction "renowned for their powerful,
/// state-of-the-art ships"). IT is the tech style, so it is theirs (the Traders fly it) (docs/sandbox/constructs/torpedo-frigate.md).
///
/// Unlike the Halberd (a tower of storeys) it flies level: two decks the length of the ship inside a lofted, faceted hull (a chisel
/// nose, sides that flare out to a sharp beam line and lean in above it, a flat keel and a flat spine), armour slabs standing proud of a
/// pale skin in steps, chine rails along the beam. Torpedo sponsons on both flanks, a railgun along the spine, four point-defence
/// turrets on barbettes at the beam, flare launchers behind the bridge and four drive bells on a collar across the whole stern, each with its own engine in the bay behind it.
///
/// Every big part has its origin at the body's; the generator works in ship metres (x across, port to starboard, the centreline at
/// Cx; y up from the lower deck's underside; z from the bow's tip back, so forward is -z) and moves them by half a cell. The hull is
/// lofted between stations (a half profile of five points mirrored): each big part draws the facets of its own band of the hull and the
/// hidden boxes that stand for the void between the skin and the rooms. A hull facet is drawn only; solid boxes of different parts
/// never overlap (the build refuses "something is in the way").
/// </summary>
public sealed partial class TorpedoFrigateParts : IPartGenerator
{
    public string Name => "torpedo_frigate_parts";

    private const string Out = "Constructs/Parts/ZeroDay";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Zero Day frigate";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint (the IT style's, made for a warship): graphite armour over a pale case-grey skin, corporate blue panels, lanyard
    // yellow at doors and hatches, link-blue lights, failed-build red for warnings.
    private const string Skin = "#bdb9ad", Skin2 = "#a9a59a", Armour = "#3b4148", Armour2 = "#30353b", Blue = "#2c5aa0", Frame = "#1f2226",
        Hazard = "#e0b020", Glow = "#5ac8ff", Red = "#e0402c", Glass = "#6f9ec0",
        Wall = "#c4c8ca", WallLow = "#2c5aa0", WallTrim = "#2a2e33", Deck = "#4a4f55", Grate = "#3a3e43", Ceil = "#9aa1a6", Console = "#22262b",
        Screen = "#58c8e8", Amber = "#f0a830", PipeRed = "#a8402c", PipeBlue = "#2f6a9a", PipeYellow = "#c8a030", MedWhite = "#dfe4e2";

    // ---- the plan

    private const double Cx = 6.5; // the centreline
    private const double Ft0 = 0.25, Ceil0 = 3.25, Ft1 = 3.5, Ceil1 = 6.5, Roof = 6.75; // floor tops and ceilings of the two decks
    private const double Ground = -4.85; // where the legs stand (0.75 m longer for the Limpet docked under the keel: 0.8 m under its belly)
    private const double ZFront = 8.5, ZStep = 14.75, ZAft = 42.0; // the rooms' front, where they widen, their back
    // A PDC's magazine (ship-works.md 5.2): a bunker in the hull's void behind the room's side wall at its barbette, aft of the
    // feed's line: its size out from the wall, up and along (m), and how far aft of the feed's line it starts. 0.7 m3: 2,800 rounds.
    private const double MagX = 1.0, MagY = 0.7, MagZ = 1.0, MagGap = 0.15;
    private const double MagCube = 3.296; // the magazine's Ammo Container (a 0.4 m cube) stands against the side wall inside: its middle this far out
    private const double Lw = 4.5, LwF = 3.5, Uw = 3.5, UwF = 2.5; // the rooms' inner half widths: lower aft, lower fore, upper aft, the bridge
    private const double DoorH = 2.25;
    private static readonly (int I, double X, double Z)[] LinkChairs = [(0, Cx - Uw + 0.5, 16.5), (1, Cx - Uw + 1.1, 18.15), (2, Cx - Uw + 1.1, 19.05), (3, Cx + Uw - 0.5, 17.6)];

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

    // The downward attitude jets (Karl, 2026-11-17: the lift jets push up and nothing pushed down, so the ship couldn't descend under
    // thrust or pitch and roll like the shuttle): a Heavy RCS Thruster on a pad either side of the roof at the bow and at the stern, each
    // on a tank part so its fuel reaches it, clear of the railgun's barrel, the flare launchers, the cradle, the sensor and the fins.
    private const double TopRcsBowX = 1.4, TopRcsBowZ = 5.2, TopRcsSternX = 2.0, TopRcsSternZ = 45.9, TopRcsPad = 0.54;

    // The side jets (Karl, 2026-11-18: A and D slid nothing, it had no jet pushing sideways): a Heavy RCS Thruster out of each flank low
    // on the beam (below it the hull's void boxes reach out under the flare), at the bow behind the retro jets and at the stern, each pushing the ship away from its side.
    private const double SideRcsBowZ = 5.5, SideRcsBowY = 3.0, SideRcsSternZ = 45.0, SideRcsSternY = 3.3;

    /// <summary>A block out of the hull's side (on the beam line, its widest) on side s at y, z, its outer face standing clear of the skin and its armour, the socket on it facing out.</summary>
    private static void SideJetPad(Part p, string name, int s, double y, double z)
    {
        var h = TopRcsPad / 2;
        double most = 0, least = double.MaxValue;
        foreach (var zz in new[] { z - h, z + h })
            foreach (var yy in new[] { y - h, y + h })
            {
                most = Math.Max(most, SkinHalf(zz, yy));
                least = Math.Min(least, SkinHalf(zz, yy));
            }
        double outer = Math.Ceiling((most + 0.3) * 100) / 100, inner = least - 0.12; // (clear of the beam's chine and the armour on the side)
        Solid(p, Cx + Math.Min(s * inner, s * outer), y - h, z - h, Cx + Math.Max(s * inner, s * outer), y + h, z + h, Frame, "dirty steel");
        Sock(p, name, Cx + s * outer, y, z, s < 0 ? "-x" : "+x");
    }

    /// <summary>A square pad standing out of the spine's plate (which may slope along z) at x, z, its top a whole centimetre and the socket on it, facing up.</summary>
    private static void TopJetPad(Part p, string name, double x, double z)
    {
        double h = TopRcsPad / 2, ya = At(z - h)[4].Y, yb = At(z + h)[4].Y;
        double top = Math.Ceiling((Math.Max(ya, yb) + 0.05) * 100) / 100, bottom = Math.Min(ya, yb) - 0.12;
        Solid(p, x - h, bottom, z - h, x + h, top, z + h, Frame, "dirty steel");
        Sock(p, name, x, top, z, "+y");
    }

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
            HiddenOut(p, Cx - xo, ya, za, Cx + xo, yb, zb);
        else
            foreach (var s in new[] { -1, 1 })
                HiddenOut(p, Math.Min(Cx + s * xin, Cx + s * xo), ya, za, Math.Max(Cx + s * xin, Cx + s * xo), yb, zb);
    }

    /// <summary>The rooms cut out of the void while a deck is made (a PDC's magazine): x0, y0, z0, x1, y1, z1 in the deck's frame.</summary>
    private static readonly List<double[]> VoidHoles = [];

    /// <summary>A hidden box less <see cref="VoidHoles"/>: the pieces of it round each hole (none thinner than a centimetre).</summary>
    private static void HiddenOut(Part p, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var pieces = new List<double[]> { new[] { x0, y0, z0, x1, y1, z1 } };
        foreach (var h in VoidHoles)
        {
            var next = new List<double[]>();
            foreach (var b in pieces)
            {
                if (h[0] >= b[3] || h[3] <= b[0] || h[1] >= b[4] || h[4] <= b[1] || h[2] >= b[5] || h[5] <= b[2])
                {
                    next.Add(b);
                    continue;
                }
                var r = (double[])b.Clone();
                for (var a = 0; a < 3; a++) // (slabs below and above the hole on each axis in turn, then what is left narrows to it)
                {
                    if (h[a] > r[a] + 0.01)
                    {
                        var lo = (double[])r.Clone();
                        lo[a + 3] = h[a];
                        next.Add(lo);
                    }
                    if (h[a + 3] < r[a + 3] - 0.01)
                    {
                        var hi = (double[])r.Clone();
                        hi[a] = h[a + 3];
                        next.Add(hi);
                    }
                    r[a] = Math.Max(r[a], h[a]);
                    r[a + 3] = Math.Min(r[a + 3], h[a + 3]);
                }
            }
            pieces = next;
        }
        foreach (var b in pieces)
            Hidden(p, b[0], b[1], b[2], b[3], b[4], b[5]);
    }

    /// <summary>
    /// The PDC magazine behind side <paramref name="s"/>'s wall at the barbette at <paramref name="zc"/>: the hole in the void (from
    /// the wall's outer face, on the deck, aft of the feed's line) and the socket its Ammo Container stands on, inside against the wall.
    /// The container's box fills the hole; the wall, the deck and the void round it seal it (ConstructSeal's reach).
    /// </summary>
    private static void MagazineAt(Part p, int s, double zc)
    {
        double d0 = Uw + 0.25 + 0.006, d1 = Uw + 0.25 + 0.046 + MagX + 0.04;
        VoidHoles.Add([Math.Min(Cx + s * d0, Cx + s * d1), Ft1, zc + MagGap - 0.04, Math.Max(Cx + s * d0, Cx + s * d1), Ft1 + 0.05 + MagY + 0.05, zc + MagGap + MagZ + 0.04]);
        Sock(p, s < 0 ? "magL" : "magR", Cx + s * MagCube, Ft1, zc, "+y");
    }

    /// <summary>The config of a PDC magazine's Ammo Container on side <paramref name="s"/>: its box in the hole behind the wall, full in creative.</summary>
    private static PyDict MagazineConfig(int s) => new()
    {
        ["sizeX"] = MagX, ["sizeY"] = MagY, ["sizeZ"] = MagZ,
        ["atX"] = s * Py.Round(Uw + 0.25 + 0.046 + MagX / 2 - MagCube, 2), ["atY"] = Py.Round(0.05 + MagY / 2 - 0.2, 2), ["atZ"] = Py.Round(MagGap + MagZ / 2, 2),
        ["material"] = "ammo", ["fill"] = 100,
    };

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
        (Floors.TryGetValue(p, out var fs) ? fs : Floors[p] = []).Add((plan, y0, y1));
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
        (Walls.TryGetValue(p, out var ws) ? ws : Walls[p] = []).Add(new WallLine(alongX, a0, a1, c, y0, y1)); // (each run is a piece of its own: Split)
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
                Run(at + 0.05, g.A - 0.05, y0 + 0.003, y0 + 0.75, WallLow, "painted plating", 0.137, true);
            // The frame: dark jambs, a hazard head.
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", 0.145, true);
            at = g.A + g.W;
        }
        Run(at, a1, y0, y1, Wall, "painted plating", 0.125, false);
        Run(at + 0.05, a1 - 0.05, y0 + 0.003, y0 + 0.75, WallLow, "painted plating", 0.137, true);
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
    /// <summary>The Limpet's shaft (TorpedoFrigateLimpet.cs): from the passage by the airlock down through the keel into the pod's receiver, on the centreline.</summary>
    private const double LimpetLiningFoot = 0.18; // the Limpet shaft's lining stops this far over the keel: under it the docking hatch slides aft
    private static readonly Rect LimpetShaft = new(Cx - 0.55, LimpetRecvZ - 0.55, Cx + 0.55, LimpetRecvZ + 0.55);

    private static Part Keel()
    {
        var p = Big("Zero Day Keel", "The Zero Day's keel: the flat underside, the fuel tankage under the lower deck, the legs' and lift jets' mounts and the boarding shaft.", "fueltank", Armour);
        double[] zs = [8.0, 14.0, 20.0, 30.0, 38.0, 42.0];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            // The keel's flat: plated, with the boarding shaft's and the Limpet's holes cut where they pass.
            if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                var y = At(za)[0].Y;
                var hb = At(za)[0].H;
                foreach (var r in Minus(Minus([new Rect(Cx - hb, za, Cx + hb, zb)], BellyHatch), LimpetShaft))
                    Plate(p, [P(r.X0, y, r.Z0), P(r.X1, y, r.Z0), P(r.X1, y, r.Z1), P(r.X0, y, r.Z1)], Armour, "plated steel");
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
        var limpetShaft = new Rect(LimpetShaft.X0 - 0.05, LimpetShaft.Z0 - 0.05, LimpetShaft.X1 + 0.05, LimpetShaft.Z1 + 0.05);
        var limpetPocket = new Rect(Cx - DockHatchHalf - 0.06, LimpetShaft.Z0 - 0.25, Cx + DockHatchHalf + 0.06, LimpetRecvZ + DockHatchHalf + DockHatchTravel + 0.06); // (the docking hatch's, shut and slid open)
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            foreach (var (ya, yt) in new[] { (yb, -0.9), (-0.9, -0.35), (-0.35, 0.0) })
            {
                var xo = new[] { SkinHalf(za, ya), SkinHalf(za, yt), SkinHalf(zb, ya), SkinHalf(zb, yt) }.Min() - 0.06;
                if (xo < 0.3 || yt <= ya)
                    continue;
                foreach (var r in Minus(Minus(Minus([new Rect(Cx - xo, i == 0 ? za : za + 0.012, Cx + xo, i == zs.Length - 2 ? zb : zb - 0.012)], shaft), limpetShaft), ya < -0.9 ? limpetPocket : limpetShaft)) // (inner ends kept off the decks' voids; the Limpet's hatch slides in a pocket at the bottom)
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
        // The Limpet's shaft: lined the same way, rungs up its aft side (the pod's ladder climbs on through it), a hazard frame round
        // its mouth under the keel; the pod's Docking Clamp hangs beside it, to port.
        var ls = limpetShaft;
        foreach (var (x0, z0, x1, z1, e) in new[] { (ls.X0, ls.Z0, ls.X1, ls.Z0, 0.0), (ls.X0, ls.Z1, ls.X1, ls.Z1, 0.0),
            (ls.X0, ls.Z0 + 0.025, ls.X0, ls.Z1 - 0.025, 0.005), (ls.X1, ls.Z0 + 0.025, ls.X1, ls.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + LimpetLiningFoot + e, z0), P(x1, hy + LimpetLiningFoot + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        for (var y = hy + 0.25; y < -0.1; y += 0.3)
            Detail(p, Cx - 0.22, y - 0.02, ls.Z1 - 0.08, Cx + 0.22, y + 0.02, ls.Z1 - 0.012, Hazard, "hazard paint");
        foreach (var (x0, z0, x1, z1) in new[] { (ls.X0 - 0.12, ls.Z0 - 0.12, ls.X1 + 0.12, ls.Z0), (ls.X0 - 0.12, ls.Z1, ls.X1 + 0.12, ls.Z1 + 0.12),
            (ls.X0 - 0.12, ls.Z0 + 0.003, ls.X0, ls.Z1 - 0.003), (ls.X1, ls.Z0 + 0.003, ls.X1 + 0.12, ls.Z1 - 0.003) })
            Detail(p, x0, hy - 0.03, z0, x1, hy - 0.004, z1, Hazard, "hazard paint");
        p.Sock("limpetClamp", P(LimpetClampX, hy + 0.02, LimpetClampZ), "-y", Medium); // (a medium socket: the Docking Clamp Medium)
        // The Docking Hatch inside the keel over the shaft's mouth, its shoe on a sill fore of it: it slides aft into the keel, and opens
        // with the Limpet's own while the clamp holds the pod.
        Hidden(p, Cx - 0.35, hy + 0.02, LimpetShaft.Z0 - 0.2, Cx + 0.35, hy + 0.07, LimpetShaft.Z0 - 0.06); // (5 cm at least, or Hidden drops it)
        Sock(p, "limpetHatch", Cx, hy + 0.07, LimpetRecvZ - DockHatchHang, "+y");
        // Mounts for the parts that go on the keel.
        Sock(p, "bow", Cx, 0.0, 8.0, "-z");
                Sock(p, "lowerFore", Cx - 2.0, 0.0, 12.0, "+y");
        Sock(p, "lowerAft", Cx - 2.0, 0.0, 36.0, "+y");
                foreach (var (s, z, n) in new[] { (-1, 18.0, "legFL"), (1, 18.0, "legFR"), (-1, 34.0, "legBL"), (1, 34.0, "legBR") })
            Sock(p, n, Cx + s * 2.2, At(z)[0].Y + 0.02, z, "-y");
        foreach (var (s, z, n) in new[] { (-1, 14.25, "liftFL"), (1, 14.25, "liftFR"), (-1, 37.75, "liftBL"), (1, 37.75, "liftBR") })
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
            WallRun(p, false, ZFront, ZStep - 0.25, Cx + s * (LwF + 0.125), y0, y1);
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
        Sock(p, "upper", Cx - 2.0, Ceil0, ZFront - 0.125, "+y"); // (on the forward wall's top, which the upper deck's floor lies on)
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
        // Under the torpedo room's ceiling, the OR Gates that carry the forward gunner's torpedo key to each bay's magazine (a wire reaches 8 m;
        // each on a small plate 5 mm under the deck above, off its plane).
        foreach (var (n, x) in new[] { ("relayL", Cx - 3.0), ("relayR", Cx + 3.0) })
        {
            Solid(p, x - 0.15, Ceil0 - 0.03, 21.85, x + 0.15, Ceil0 - 0.005, 22.15, Ceil, "painted plating");
            Sock(p, n, x, Ceil0 - 0.03, 22.0, "-y");
        }
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
        Floor(p, [new Rect(Cx - Lw - 0.25, 25.0, Cx + Lw + 0.25, ZAft + 0.25)], 0, Ft0, Deck, "painted deck plate", BellyHatch, LimpetShaft);
        Detail(p, Cx - 3.0, Ft0, 25.1, Cx + 3.0, Ft0 + 0.012, 29.9, Grate, "painted grating");
        Detail(p, Cx - Lw + 0.1, Ft0, 34.4, Cx + Lw - 0.1, Ft0 + 0.012, ZAft - 0.1, Grate, "painted grating"); // (engineering's grating)
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, 25.0, ZAft, Cx + s * (Lw + 0.125), y0, y1, s > 0 ? [Door(26.4)] : [Door(26.4)]);
        // The aft wall is open from side to side under a lintel: engineering and the engine bay are one space, the four engines
        // seen from the reactor (ship-works.md 15.4).
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZAft + 0.125, y0, y1, new Gap(Cx - Lw + 0.35, 2 * Lw - 0.7, 0, y1 - y0 - 0.3));
        // The airlock: a chamber to starboard over the boarding shaft, its inner door off the passage; the suit bay to port.
        WallRun(p, true, Cx + 1.75, Cx + Lw, 30.25, y0, y1);
        WallRun(p, false, 30.375, 34.0, Cx + 1.875, y0, y1, Door(32.25));
        HatchRail(p, BellyHatch, Ft0);
        HatchRail(p, LimpetShaft, Ft0); // (the way down into the Limpet, open at its aft edge where its ladder lands)
        // The bulkhead into engineering.
        WallRun(p, true, Cx - Lw, Cx + Lw, 34.125, y0, y1, Door(Cx - 0.75));
        // Engineering: pipes along the ceiling's corners, a catwalk-grey ceiling of frames, the reactor forward to starboard. Aft,
        // through the open wall, the engine bay: four engines running forward from the drive collar, one behind each bell.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * (Lw - 0.18) - 0.05, y1 - 0.3, 25.05, Cx + s * (Lw - 0.18) + 0.05, y1 - 0.2, ZAft - 0.5, s < 0 ? PipeRed : PipeBlue, "painted steel");
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
        Sock(p, "upper", Cx - 2.0, Ceil0, 34.125, "+y"); // (on the engineering bulkhead's top)
        DoorSock(p, "doorBayL", false, 26.4, Cx - Lw - 0.125, Ft0);
        DoorSock(p, "doorBayR", false, 26.4, Cx + Lw + 0.125, Ft0);
        DoorSock(p, "doorAirlock", false, 32.25, Cx + 1.875, Ft0);
        DoorSock(p, "doorEngine", true, Cx - 0.75, 34.125, Ft0);
        Sock(p, "boarding", BellyHatch.X0 + 0.625, Ft0 - 0.125, BellyHatch.Z1, "-z");
        // Fittings: suit lockers in the bay, more torpedo crates, engineering's reactor, boards, batteries, pipes and the ladder up.
        for (var i = 0; i < 4; i++)
            Sock(p, $"suit{i}", Cx - Lw, Ft0 + 0.125, 30.6 + i * 0.7, "+x");
        Sock(p, "airlockLocker", Cx + Lw, Ft0 + 0.125, 33.3, "-x");
        for (var i = 0; i < 4; i++) // (the Robot Shell Docks along the engineering bulkhead's forward face, port of its door: Karl, 2026-11-17)
            Sock(p, $"shellDock{i}", Cx - Lw + 0.2 + CrewFittingsParts.DockHalf + i * (2 * CrewFittingsParts.DockHalf + 0.01), Ft0 + 0.125, 34.0, "-z");
        Sock(p, "reactor", Cx + 3.1, Ft0, 35.5, "+y");
        Sock(p, "board", Cx + Lw, Ft0 + 0.9, 40.6, "-x");
        Sock(p, "panel", Cx - Lw, Ft0 + 0.6, 38.0, "+x");
        Sock(p, "battery0", Cx - 3.9, Ft0, 39.0, "+y");
        Sock(p, "battery1", Cx - 3.9, Ft0, 39.5, "+y");
        Sock(p, "battery2", Cx - 3.9, Ft0, 40.0, "+y");
        Sock(p, "capacitor", Cx + 2.4, Ft0, 37.0, "+y"); // (the railgun's Capacitor Bank, aft of the reactor: ship-works.md 8)
        Sock(p, "pipes", Cx - 2.7, Ft0 + 2.75, 34.25, "+z"); // (on the bulkhead's aft face)
        Sock(p, "toolRack", Cx + Lw, Ft0 + 0.9, 38.2, "-x");
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
        // Under the ceiling, the OR Gates that carry the mess gunner's torpedo key forward (a wire reaches 8 m): one in the passage
        // between the suit bay and the airlock, then one each side at the torpedo room's aft end, each on a small plate 5 mm under
        // the deck above.
        foreach (var (n, x, z) in new[] { ("relayAft", Cx + 0.8, 33.2), ("relayAftL", Cx - 3.0, 28.0), ("relayAftR", Cx + 3.0, 28.0) })
        {
            Solid(p, x - 0.15, Ceil0 - 0.03, z - 0.15, x + 0.15, Ceil0 - 0.005, z + 0.15, Ceil, "painted plating");
            Sock(p, n, x, Ceil0 - 0.03, z, "-y");
        }
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
        // The bridge: narrow and sealed, a wide view screen on its forward wall over the dashboard, a camera screen on each side.
        WallRun(p, true, Cx - UwF - 0.25, Cx + UwF + 0.25, ZFront - 0.125, y0, y1);
        Detail(p, Cx - 2.1, Ft1 + 1.2, ZFront - 0.016, Cx + 2.1, Ft1 + 2.6, ZFront + 0.016, "#202428", "plastic"); // (the screen's bezel)
        Detail(p, Cx - 2.0, Ft1 + 1.28, ZFront + 0.008, Cx + 2.0, Ft1 + 2.52, ZFront + 0.028, "#2a5878", glow: true);
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZFront, ZStep - 0.25, Cx + s * (UwF + 0.125), y0, y1);
            Detail(p, Cx + s * UwF - 0.016, Ft1 + 1.25, 9.9, Cx + s * UwF + 0.016, Ft1 + 2.2, 11.6, "#202428", "plastic");
            Detail(p, Cx + s * (UwF - 0.02) - 0.008, Ft1 + 1.32, 10.0, Cx + s * (UwF - 0.02) + 0.008, Ft1 + 2.13, 11.5, "#2a5878", glow: true);
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
                // A link-blue light strip under the beam's rail, the IT style's mark.
                FStrip(p, 3, s, za, zb, 0.03, 0.97, 0.04, 0.065, 0.025, Glow, glow: true);
            }
        // Shoulder blocks on the upper chine either side of the bridge, and a lower one on the beam under them.
        foreach (var s in new[] { -1, 1 })
        {
            Shoulder(p, 3, s, 8.4, 13.6, 0.75, 0.42, 1.4, Armour);
            Shoulder(p, 2, s, 9.0, 13.0, 0.55, 0.3, 1.0, Blue);
        }
        // The void: beside the floor slab, beside the walls, beside the ceiling; the forward PDCs' magazines cut out of it behind ops' walls.
        foreach (var s in new[] { -1, 1 })
            MagazineAt(p, s, 15.5);
        foreach (var (za, zb, xl, xu) in new[] { (8.0, 14.0, LwF + 0.25, UwF + 0.25), (14.0, 20.0, Lw + 0.25, Uw + 0.25), (20.0, 30.0, Lw + 0.25, Uw + 0.25) })
        {
            Void(p, za, Math.Min(zb, 25.0), 3.25, 3.5, xl);
            Void(p, za, Math.Min(zb, 25.0), 3.5, 5.2, xu);
            Void(p, za, Math.Min(zb, 25.0), 5.2, 6.75, xu);
        }
        VoidHoles.Clear();
        Sock(p, "lower", Cx - 2.0, Ceil0, ZFront - 0.125, "-y");
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
        // The Shell Control Chairs (Karl, 2026-11-17): one with its back to ops' port wall, two at the ops console facing it, one with
        // its back to the starboard wall clear of the hatch and the plot.
        foreach (var (i, x, z) in LinkChairs)
            Sock(p, $"linkChair{i}", x, Ft1, z, "+y");
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
        Floor(p, [new Rect(Cx - Uw - 0.25, 25.0, Cx + Uw + 0.25, ZAft + 0.25)], Ceil1, Roof, Ceil, "painted plating", ShuttleShaft);
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
        foreach (var s in new[] { -1, 1 })
            MagazineAt(p, s, 37.5); // (the aft PDCs' magazines, behind the mess's walls)
        foreach (var (za, zb) in new[] { (25.0, 30.0), (30.0, 38.0), (38.0, ZAft) })
        {
            Void(p, za, zb, 3.25, 3.5, Lw + 0.25);
            Void(p, za, zb, 3.5, 5.2, Uw + 0.25);
            Void(p, za, zb, 5.2, 6.75, Uw + 0.25);
        }
        VoidHoles.Clear();
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
        // The mess: galley, coffee, the table (with the office plant on it: it is an IT-style ship), the aft gunnery station.
        Sock(p, "stove", Cx - Uw, Ft1 + 0.45, 35.6, "+x");
        Sock(p, "coffee", Cx - Uw, Ft1 + 0.125, 36.6, "+x");
        Sock(p, "messLocker", Cx + Uw, Ft1 + 0.125, 36.2, "-x"); // (starboard: the port magazine's container stands where it was)
        Sock(p, "table", Cx - 2.0, Ft1, 38.4, "+y");
        Sock(p, "gun", Cx - 0.3, Ft1, 40.1, "+y"); // (aft of the shuttle's ladder)
        Sock(p, "gunSeat", Cx - 0.3, Ft1, 41.1, "+y");
        Sock(p, "shuttleLadder", Cx, Ft1, ShuttleShaft.Z1 - 0.15, "+y"); // (the ladder up into the shuttle: its back on the shaft's aft edge)
        // The shaft's lip in the ceiling: hazard round it, a red light on its fore edge.
        Detail(p, ShuttleShaft.X0 - 0.1, y1 - 0.012, ShuttleShaft.Z0 - 0.1, ShuttleShaft.X1 + 0.1, y1, ShuttleShaft.Z0, Hazard, "hazard paint");
        Detail(p, ShuttleShaft.X0 - 0.1, y1 - 0.012, ShuttleShaft.Z1, ShuttleShaft.X1 + 0.1, y1, ShuttleShaft.Z1 + 0.1, Hazard, "hazard paint");
        Detail(p, ShuttleShaft.X0 + 0.2, y1 - 0.02, ShuttleShaft.Z0 - 0.22, ShuttleShaft.X1 - 0.2, y1 - 0.012, ShuttleShaft.Z0 - 0.12, Red, glow: true);
        Sock(p, "director", Cx + Uw - 0.3, Ft1, 41.3, "+y"); // (the Fire Director in the starboard aft corner, under the mast's radar: a wire reaches 8 m; against the wall, clear of the aft ladder's landing)
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
                Facet(p, 4, s, za, zb, canopy ? Armour2 : Skin2, canopy ? "plated steel" : "painted plating"); // (the bridge is sealed: no glass, it sees through cameras)
                Chine(p, 2, s, za, zb, 0.26, 0.12, Armour2);
                Chine(p, 3, s, za, zb, 0.2, 0.1, Armour);
            }
            Facet(p, 5, 1, za, zb, Armour, "plated steel");
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
        // Two attitude jets on the canopy's roof, either side of the railgun's barrel, firing up: they push the nose down (and roll the ship).
        foreach (var s in new[] { -1, 1 })
            TopJetPad(p, s < 0 ? "rcsTopL" : "rcsTopR", Cx + s * TopRcsBowX, TopRcsBowZ);
        foreach (var s in new[] { -1, 1 })
            SideJetPad(p, s < 0 ? "rcsSideL" : "rcsSideR", s, SideRcsBowY, SideRcsBowZ);
        p.Set(("fuelTank", Kw(("capacity", 1500))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx, 1.0, 8.0).Cast<object>()], "+z", "fuel"))));
        return p.Stats(9000, 24000, 40, 15.0);
    }

    // ---- the spine: the roof, the railgun's housing, the sensor mast, the flare launchers' mounts

    // The shuttle (TorpedoFrigateShuttle.cs) on its cradle: the pod's middle and nose (z), the cradle's top and aft end, the shaft from the
    // mess up through the ceiling, the roof and the cradle into the pod's belly hatch; the ladder's landing is the pod's floor.
    private const double ShuttleZ = 39.0, ShuttleZ0 = ShuttleZ - 5.0, CradleY = 7.75, CradleZ1 = 45.2, ShuttleLift = 0.005;
    private static readonly Rect ShuttleShaft = new(Cx - 0.6, ShuttleZ - 0.65, Cx + 0.6, ShuttleZ + 0.65);
    private static Rect ShuttleShaftWide => ShuttleShaft;
    private static readonly (double, double)[] CradleBands = [(ShuttleZ0 - 0.6, 37.996), (38.004, 41.996), (42.004, CradleZ1)];
    private const double LadderRise = CradleY + ShuttleLift + LFt - Ft1, LadderRails = CradleY - Ft1;

    /// <summary>The shuttle's Docking Clamp: holding from the start, its box round the pod on the cradle (its centre of mass in it).</summary>
    private static PyDict ShuttleClampConfig()
    {
        double cx = Cx - 1.3, cy = CradleY + 0.5, cz = CradleZ1 - 0.6; // (the clamp's middle: half a metre over its foot)
        return new PyDict
        {
            ["startOn"] = true,
            ["sizeX"] = 4.5, ["sizeY"] = 3.5, ["sizeZ"] = 10.5,
            ["atX"] = Py.Round(Cx - cx, 2), ["atY"] = Py.Round(CradleY + ShuttleLift + ShuttleCom.Y - cy, 2), ["atZ"] = Py.Round(ShuttleZ + ShuttleCom.Z - cz, 2),
        };
    }

    /// <summary>The shuttle's centre of mass in its own frame (as built: a pull dock holds a craft with this over its box's middle).</summary>
    private static readonly (double Y, double Z) ShuttleCom = (1.3, 0.3);

    /// <summary>The spine's top (facet 5) from za to zb with a rectangular hole cut in it: four plates round the hole.</summary>
    private static void FacetHole(Part p, int k, double za, double zb, Rect h, string color, string finish)
    {
        var dz = FacetStagger;
        double Y(double z) => At(za)[4].Y + (At(zb)[4].Y - At(za)[4].Y) * (z - za) / (zb - za);
        double Hw(double z) => At(za)[4].H + (At(zb)[4].H - At(za)[4].H) * (z - za) / (zb - za);
        double[] Q(double x, double z) => P(x, Y(z), z + dz);
        double z0 = za + FacetTrim, z1 = zb - FacetTrim;
        Plate(p, [Q(Cx - Hw(z0), z0), Q(Cx + Hw(z0), z0), Q(Cx + Hw(h.Z0), h.Z0), Q(Cx - Hw(h.Z0), h.Z0)], color, finish);
        Plate(p, [Q(Cx - Hw(h.Z1), h.Z1), Q(Cx + Hw(h.Z1), h.Z1), Q(Cx + Hw(z1), z1), Q(Cx - Hw(z1), z1)], color, finish);
        Plate(p, [Q(Cx - Hw(h.Z0), h.Z0), Q(h.X0, h.Z0), Q(h.X0, h.Z1), Q(Cx - Hw(h.Z1), h.Z1)], color, finish);
        Plate(p, [Q(h.X1, h.Z0), Q(Cx + Hw(h.Z0), h.Z0), Q(Cx + Hw(h.Z1), h.Z1), Q(h.X1, h.Z1)], color, finish);
    }

    private static Part Spine()
    {
        var p = Big("Zero Day Spine", "The Zero Day's spine: the flat roof, the railgun's armoured housing, the sensor mast and the flare launchers' mounts.", color: Armour);
        double[] zs = [8.0, 14.0, 20.0, 30.0, 38.0, 42.0];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            if (zs[i] <= ShuttleShaftWide.Z0 && zs[i + 1] >= ShuttleShaftWide.Z1)
                FacetHole(p, 5, zs[i], zs[i + 1], ShuttleShaftWide, Armour, "plated steel"); // (the shuttle's shaft through the roof plate)
            else
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
        double[][] Blk(double z, double w, double h, double wt)
        {
            var y = Top(z) - 0.012;
            return [P(Cx - w, y, z), P(Cx - wt - (w - wt) * 0.25, y + h * 0.7, z), P(Cx - wt, y + h, z), P(Cx + wt, y + h, z), P(Cx + wt + (w - wt) * 0.25, y + h * 0.7, z), P(Cx + w, y, z)];
        }
        // Aft of the housing, the shuttle's cradle (Karl, 2026-11-17: the boarding pod rides on the spine): a low raked block out of the
        // railgun's housing, then a flat deck standing on the roof the length of the pod, its hazard edges and guide lips, the shaft
        // down to the mess through it, the pod's Docking Clamp on its aft port corner and the sensor mast on its aft starboard one.
        Prism(p, Blk(hz1, 1.2, 0.55, 0.95), Blk(ShuttleZ0 - 0.6, 1.9, CradleY - Top(ShuttleZ0 - 0.6) + 0.012, 1.6), Armour, "plated steel");
        foreach (var (za, zb) in CradleBands)
        {
            // The deck plate (solid), round the shaft; under it hidden boxes down to the roof.
            foreach (var r in Minus([new Rect(Cx - 1.9, za, Cx + 1.9, zb)], ShuttleShaftWide))
                Solid(p, r.X0, CradleY - 0.08, r.Z0, r.X1, CradleY, r.Z1, Armour2, "plated steel");
            var low = Math.Max(Top(za), Top(zb)) + 0.004;
            foreach (var r in Minus([new Rect(Cx - 1.8, za + 0.05, Cx + 1.8, zb - 0.05)], ShuttleShaftWide))
                Hidden(p, r.X0, low, r.Z0, r.X1, CradleY - 0.08, r.Z1);
            foreach (var sd in new[] { -1, 1 })
            {
                Plate(p, [P(Cx + sd * 1.9, CradleY - 0.08, za + 0.02), P(Cx + sd * 1.9, CradleY - 0.08, zb - 0.02), P(Cx + sd * 1.55, Top(zb) - 0.01, zb - 0.02), P(Cx + sd * 1.55, Top(za) - 0.01, za + 0.02)], Armour, "plated steel"); // (its raked flanks down to the roof)
                Detail(p, Cx + sd * 1.9 - 0.18, CradleY, za + 0.03, Cx + sd * 1.9 - 0.02, CradleY + 0.012, zb - 0.03, Hazard, "hazard paint");
                Detail(p, Cx + sd * 1.9 - 0.02, CradleY, za + 0.03, Cx + sd * 1.9 + 0.03, CradleY + 0.1, zb - 0.03, Frame, "painted steel");
            }
        }
        // The shaft's lining down through the roof, hazard round its mouth on the cradle.
        var sh = ShuttleShaftWide;
        foreach (var (x0, z0, x1, z1) in new[] { (sh.X0, sh.Z0, sh.X1, sh.Z0), (sh.X0, sh.Z1, sh.X1, sh.Z1), (sh.X0, sh.Z0 + 0.02, sh.X0, sh.Z1 - 0.02), (sh.X1, sh.Z0 + 0.02, sh.X1, sh.Z1 - 0.02) })
            Plate(p, [P(x0, CradleY - 0.09, z0), P(x1, CradleY - 0.09, z1), P(x1, Roof + 0.01, z1), P(x0, Roof + 0.01, z0)], Grate, "dirty steel");
        Detail(p, sh.X0 - 0.12, CradleY, sh.Z0 - 0.12, sh.X1 + 0.12, CradleY + 0.012, sh.Z0, Hazard, "hazard paint");
        Detail(p, sh.X0 - 0.12, CradleY, sh.Z1, sh.X1 + 0.12, CradleY + 0.012, sh.Z1 + 0.12, Hazard, "hazard paint");
        // The sensor mast on the cradle's aft starboard corner: a raked blade, a dish, red tips; a whip antenna to port.
        double mx = Cx + 1.3, mz = CradleZ1 - 0.6, my = CradleY;
        Prism(p, [P(mx - 0.12, my, mz - 0.5), P(mx + 0.12, my, mz - 0.5), P(mx + 0.12, my, mz + 0.4), P(mx - 0.12, my, mz + 0.4)], [P(mx - 0.06, my + 2.0, mz + 0.05), P(mx + 0.06, my + 2.0, mz + 0.05), P(mx + 0.06, my + 2.0, mz + 0.35), P(mx - 0.06, my + 2.0, mz + 0.35)], Armour, "plated steel");
        Detail(p, mx - 0.07, my + 2.0, mz + 0.12, mx + 0.07, my + 2.14, mz + 0.3, Red, glow: true);
        Prism(p, Ngon(mx + 0.35, my + 0.7, mz - 0.1, 0.06, 8), Ngon(mx + 0.35, my + 0.9, mz - 0.1, 0.4, 8), "#c8ccc8", "painted steel");
        Detail(p, mx + 0.31, my + 0.004, mz - 0.14, mx + 0.39, my + 0.7, mz - 0.06, Frame, "painted steel");
        Detail(p, Cx - 1.75 - 0.03, my + 0.004, CradleZ1 - 0.25, Cx - 1.75 + 0.03, my + 1.6, CradleZ1 - 0.19, Frame, "steel");
        Detail(p, Cx - 1.75 - 0.04, my + 1.6, CradleZ1 - 0.26, Cx - 1.75 + 0.04, my + 1.68, CradleZ1 - 0.18, Red, glow: true);
        // A low fairing aft of the cradle down to the drive.
        Prism(p, Blk(CradleZ1 + 0.01, 1.7, CradleY - Top(CradleZ1) - 0.1, 1.4), Blk(46.0, 1.2, 0.2, 1.0), Armour, "plated steel");
        // Hidden: the roof void, the shuttle's shaft cut out of it.
        VoidHoles.Add([ShuttleShaftWide.X0, Roof, ShuttleShaftWide.Z0, ShuttleShaftWide.X1, 8.0, ShuttleShaftWide.Z1]);
        for (var i = 0; i < zs.Length - 1; i++)
            Void(p, zs[i], zs[i + 1], Roof, Math.Min(Top(zs[i]), Top(zs[i + 1])) - 0.02, 0);
        VoidHoles.Clear();
        Sock(p, "upper", Cx - 1.5, Roof, 12.0, "-y");
        Sock(p, "railgun", Cx, Top(20) + 0.06, 32.0, "+y");
        p.Sock("shuttleClamp", P(Cx - 1.3, CradleY, CradleZ1 - 0.6), "+y", Medium); // (a medium socket: the Docking Clamp Medium)
        // The Long Range Sensor (the radar, wired to the Fire Director in the mess below) on the cradle deck aft of the shuttle, between its
        // drive and the mast: a wire reaches 8 m.
        p.Sock("sensor", P(Cx + 0.75, CradleY, CradleZ1 - 0.35), "+y");
        p.Sock("dockHatch", P(Cx, CradleY, ShuttleZ - DockHatchHang), "+y"); // (the Docking Hatch's shoe on the deck ahead of the shaft, the lid over it; it slides aft, between the shuttle's keel and the cradle)
        foreach (var (s, z, n) in new[] { (-1, 17.6, "flaresFL"), (1, 17.6, "flaresFR"), (-1, 21.4, "flaresBL"), (1, 21.4, "flaresBR") })
            Sock(p, n, Cx + s * 1.75, Top(z) - 0.02, z, "+y");
        return p.Stats(8000, 24000, 45, 15.0);
    }

    // ---- the stern: the flare out to the drive and its mount

    // ---- the engine bay (ship-works.md 15.4, Karl 2026-11-16: "there should be one connected to each of the thrusters ... Not in a
    // separate, unjoined room"). Four bells on the stern collar; behind each, through the collar's plate, its own engine: a Drive Core M
    // and a Fuel Pump M, one clad cylinder on the bell's axis running forward into the bay, the walkways between them open to engineering.

    private static readonly double[] BellX = [-4.35, -1.45, 1.45, 4.35]; // (from the centreline)
    private const double DriveY = 2.6, BellY = 0.2; // the collar's origin over the keel's underside, the bells' axis over that
    private const double CollarPlate = 0.9, CollarFace = 1.2; // the collar's plate, from its inner face (the engines' front) to its outer (the bells')
    private static double EngineY => DriveY + BellY; // the engines' axis, ship metres
    private static double EngineFront => Stern + CollarPlate; // the cores' front flanges, on the plate's inner face
    private static double EngineBack => EngineFront - ShipWorksParts.CoreD[1] - ShipWorksParts.PumpD[1]; // the pumps' back ends
    private const double BayCeil = 4.2; // the bay's ceiling (the fuel tank over it)

    /// <summary>The bay floor's half width at z: inside the skin at the floor's underside, less the plates.</summary>
    private static double BayHalf(double z) => Math.Floor((SkinHalf(z, 0.0) - 0.08) * 100) / 100;

    private static Part SternPart()
    {
        var p = Big("Zero Day Stern", "The Zero Day's stern, its engine bay: the hull flares out round the four engines that run forward from the drive collar, side by side, a walkway between each pair, open to engineering. The drive's tankage is over the bay and under its floor.", "fueltank", Armour);
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
        // Two attitude jets on the roof aft of the shuttle, between the heat-sink fins, firing up: they push the tail down (and roll the ship).
        foreach (var s in new[] { -1, 1 })
            TopJetPad(p, s < 0 ? "rcsTopL" : "rcsTopR", Cx + s * TopRcsSternX, TopRcsSternZ);
        foreach (var s in new[] { -1, 1 })
            SideJetPad(p, s < 0 ? "rcsSideL" : "rcsSideR", s, SideRcsSternY, SideRcsSternZ);
        // (no stern face: the drive collar closes it, its plate the engines' mount)
        // Heat-sink fins above the drive, angled out.
        foreach (var s in new[] { -1, 1 })
            foreach (var x in new[] { 1.2, 2.6 })
            {
                Prism(p, [P(Cx + s * x - 0.05, At(44)[4].Y, 43.4), P(Cx + s * x + 0.05, At(44)[4].Y, 43.4), P(Cx + s * x + 0.05, At(44)[4].Y, 46.2), P(Cx + s * x - 0.05, At(44)[4].Y, 46.2)],
                    [P(Cx + s * x - 0.03, At(44)[4].Y + 0.7, 44.6), P(Cx + s * x + 0.03, At(44)[4].Y + 0.7, 44.6), P(Cx + s * x + 0.03, At(44)[4].Y + 0.7, 46.6), P(Cx + s * x - 0.03, At(44)[4].Y + 0.7, 46.6)], Armour2, "plated steel");
            }
        // The bay's floor at engineering's height, stepping in as the hull narrows aft; its grating; the ceiling under the tank.
        double z0 = ZAft + 0.25, zEnd = Stern - 0.05;
        var floor = new List<Rect>();
        for (var z = z0; z < zEnd - 0.01; z += 0.5)
        {
            var z1 = Math.Min(zEnd, z + 0.5);
            var h = Math.Min(BayHalf(z), BayHalf(z1));
            floor.Add(new Rect(Cx - h, z, Cx + h, z1));
        }
        Floor(p, [.. floor], 0, Ft0, Deck, "painted deck plate");
        Detail(p, Cx - 3.6, Ft0, z0 + 0.1, Cx + 3.6, Ft0 + 0.012, EngineBack - 0.15, Grate, "painted grating"); // (the cross walkway, the reactor room's floor carried on)
        foreach (var x in new[] { 0.0, -2.9, 2.9 })
            Detail(p, Cx + x - 0.45, Ft0, EngineBack - 0.1, Cx + x + 0.45, Ft0 + 0.012, zEnd - 0.4, Grate, "painted grating"); // (the walkways between the engines)
        var ceil = new List<Rect>();
        foreach (var (za, zb) in new[] { (z0, 44.0), (44.0, zEnd) })
        {
            var h = Math.Floor((Math.Min(Math.Min(SkinHalf(za, BayCeil), SkinHalf(zb, BayCeil)), Math.Min(SkinHalf(za, BayCeil + 0.25), SkinHalf(zb, BayCeil + 0.25))) - 0.08) * 100) / 100;
            ceil.Add(new Rect(Cx - h, za, Cx + h, zb));
        }
        Floor(p, [.. ceil], BayCeil, BayCeil + 0.25, Ceil, "painted plating");
        foreach (var x in new[] { 0.0, -2.9, 2.9 })
            CeilStrip(p, Cx + x - 0.2, Cx + x + 0.2, EngineBack + 0.2, zEnd - 0.3, BayCeil);
        // The fuel header across the bay under the ceiling, and a drop to each engine's pump.
        Detail(p, Cx - 4.6, BayCeil - 0.25, EngineBack + 0.35, Cx + 4.6, BayCeil - 0.12, EngineBack + 0.48, PipeYellow, "painted steel");
        foreach (var x in BellX)
            Detail(p, Cx + x - 0.06, EngineY + 0.92, EngineBack + 0.36, Cx + x + 0.06, BayCeil - 0.25, EngineBack + 0.47, PipeYellow, "painted steel");
        // The engines' beds: a pedestal under each one's saddles, the floor to the saddles' feet, wherever the floor is under it.
        var feet = EngineY - ShipWorksParts.AxisOver(1);
        foreach (var x in BellX)
        {
            var bz1 = Math.Min(EngineFront - ShipWorksParts.CoreD[1] * 0.5, zEnd - 0.05);
            while (BayHalf(bz1) < Math.Abs(x) + 0.32 && bz1 > EngineBack + 1)
                bz1 -= 0.1;
            Solid(p, Cx + x - 0.3, Ft0, EngineBack + 0.15, Cx + x + 0.3, feet - 0.004, bz1, Frame, "painted steel");
            Detail(p, Cx + x - 0.305, Ft0 + 0.1, EngineBack + 0.145, Cx + x + 0.305, Ft0 + 0.18, bz1 + 0.005, Hazard, "hazard paint");
        }
        // Hidden: the void under the floor and over the ceiling (the tankage), and the skin's side outside the engines.
        foreach (var (ya, yb) in new[] { (-1.4, -0.4), (-0.4, 0.0), (BayCeil + 0.25, 5.6), (5.6, 7.0) })
            Void(p, z0, Stern, ya, yb, 0);
        foreach (var (ya, yb) in new[] { (0.0, 1.0), (1.0, 3.3), (3.3, BayCeil) })
            Void(p, z0, Stern, ya, yb, 5.45);
        Sock(p, "deck", Cx, 1.5, ZAft + 0.25, "-z");
        Sock(p, "drive", Cx, DriveY, Stern, "+z");
        // Each engine inside the skin, with room for a hand along its outer side.
        foreach (var z in new[] { EngineBack, 44.0, Stern })
            foreach (var y in new[] { EngineY - 0.9, EngineY, EngineY + 0.9 })
                MustFit("an engine", z, 4.35 + 0.9, y);
        p.Set(("fuelTank", Kw(("capacity", 9000))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx + 4.0, BayCeil + 0.5, Stern).Cast<object>()], "+z", "fuel"))));
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
            // The nose: only the lips under and over the launch port fair into the hull; between them the front is flat, the port
            // in it behind its hatch (Karl, 2026-11-18: the cowl closed the port, "a hatch here instead, which opens before the missiles fire").
            if (i == 0 || i == full.Length - 2)
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
        // (over the port to the top of the outline, the lip's edge: two convex plates)
        Plate(p, [P((s < 0 ? xb : xa) + s * 0.004, py1, zA), P(s < 0 ? xa : xb, py1, zA), V(capOut[4], zA), P(capOut[5].X + s * 0.004, capOut[5].Y, zA)], Armour, "plated steel"); // (its inboard edge 4 mm off the inboard plate's)
        Plate(p, [P(s < 0 ? xa : xb, py1, zA - 0.0025), V(capOut[3], zA - 0.0025), V(capOut[4], zA - 0.0025)], Armour, "plated steel");
        // The lips' open ends, closed back to the front: under the port the lower lip's outboard end, over it the upper lip's.
        Plate(p, [V(full[1], SpZ0), V(flat[1], zf), V(flat[1], SpZ0 - 0.0075)], Armour, "plated steel");
        Plate(p, [V(full[4], SpZ0), V(flat[4], zf), V(flat[4], SpZ0 - 0.0075)], Armour, "plated steel");
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
        Hidden(p, Cx + s * (HatchHinge - 0.07), HatchY - 1.2, HatchZ, Cx + s * (HatchHinge + 0.07), HatchY + 1.2, SpZ0 + 0.3); // (the hatch's hinge post, behind the frame outboard of the port)
        Sock(p, "hatch", Cx + s * HatchHinge, HatchY, HatchZ, "-z"); // (the launch hatch's hinge, at the port's outboard edge on the front)
        return p.Stats(9000, 30000, 45, 15.0);
    }

    // The launch hatch over each torpedo port (Karl, 2026-11-18): a plate hinged on its outboard edge, clear of the port's hazard frame
    // in front of the flat nose, swinging forward a quarter turn to lie along the hull's flank while a torpedo goes (ConstructDoors.OpenForLaunch).
    private const double HatchHinge = SpOut - 0.25, HatchW = 2.55, HatchH = 2.5, HatchT = 0.08, HatchY = 1.55, HatchZ = SpZ0 - 0.055;

    /// <summary>
    /// The torpedo port's launch hatch: an armoured plate in its own frame from the hinge (its "hang" socket, facing +z into the
    /// sponson's front) inboard along -x and forward along -z. A launch hatch (<c>"launch": true</c>): when a magazine fires and finds
    /// it shut in the way, the server swings it open, the torpedo goes once it is clear, and it shuts four seconds after the last one.
    /// F opens and shuts it by hand too. Symmetric top to bottom, so the port side's is the same part turned half round its z.
    /// </summary>
    private static Part TorpedoHatch()
    {
        var p = new Part("Zero Day Torpedo Hatch", Small, Section, "The armoured hatch over one of the Zero Day's torpedo ports, hinged on its outboard edge. It swings open by itself just before a torpedo launches and shuts a few seconds after the last one. F opens or shuts it by hand.", role: "door", tool: "welder", material: "steel", color: Armour2, group: Group);
        double w = HatchW, h = HatchH / 2, t = HatchT;
        p.Add(Box([-w, -h, -t], [0, h, 0], Armour2, shape: Kw(("finish", "plated steel"))));
        // On its face: ribs across, hazard chevrons down its leading (inboard) edge, the IT blue band, hinge knuckles and a lamp.
        foreach (var y in new[] { -0.8, 0.0, 0.8 })
            p.Add(Box([-w + 0.25, y - 0.05, -t - 0.035], [-0.2, y + 0.05, -t + 0.006], Armour, detail: true, shape: Kw(("finish", "plated steel"))));
        p.Add(Box([-w + 0.03, -h + 0.03, -t - 0.012], [-w + 0.17, h - 0.03, -t + 0.006], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        foreach (var y in new[] { -0.5, 0.5 })
            p.Add(Box([-w + 0.3, y - 0.12, -t - 0.016], [-0.25, y + 0.12, -t + 0.004], Blue, detail: true, shape: Kw(("finish", "painted plating"))));
        foreach (var y in new[] { -0.95, 0.95 })
            p.Add(Box([-0.12, y - 0.15, -t - 0.05], [0.0, y + 0.15, -t + 0.007], Frame, detail: true, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-w + 0.24, -0.06, -t - 0.022], [-w + 0.32, 0.06, -t + 0.005], Amber, detail: true, shape: Kw(("glow", true))));
        p.Sock("hang", [0, 0, 0], "+z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+y"), ("angle", -90), ("seconds", 1.0), ("hang", "hang"), ("launch", true))));
        return p.Stats(600, 8000, 45, 6.0);
    }

    /// <summary>Where a barbette meets the hull: the outer face of the void box beside the upper floor (Void's rounding).</summary>
    private static double PodX => Math.Floor((Math.Min(SkinHalf(20, 3.25), SkinHalf(20, 3.5)) - 0.06) * 100) / 100;

    // ---- the turret barbette (both sides)

    private static Part Barbette()
    {
        var p = Big("Zero Day Turret Barbette", "An armoured barbette on the Zero Day's beam: a point-defence turret turns on its top, on a Turret Ring whose trunk goes down into the barbette's well, fed from the magazine inside.", color: Armour);
        // Local: x out from the hull (the socket at 0), y up, z along the ship.
        double[][] Sec(double x, double w, double y0, double y1) => [[x, y0, -w], [x, y1, -w * 0.8], [x, y1, w * 0.8], [x, y0, w]];
        double[][] Pt(IEnumerable<double[]> q) => [.. q.Select(a => a.Select(v => Py.Round(v, 4)).ToArray())];
        Prism(p, Pt(Sec(-0.5, 1.2, -0.9, 0.7)), Pt(Sec(0.9, 1.1, -0.6, 0.9)), Armour, "plated steel");
        Prism(p, Pt(Sec(0.9, 1.1, -0.6, 0.9)), Pt(Sec(1.6, 0.75, -0.2, 0.9)), Armour2, "plated steel");
        Prism(p, Pt(Sec(0.3, 0.9, 0.9, 0.91)), Pt(Sec(1.45, 0.6, 0.9, 0.95)), Blue, "painted plating");
        p.Add(Box([1.55, 0.2, -0.45], [1.62, 0.5, 0.45], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        // Its solid round the well the Turret Ring's trunk goes down (the trunk reaches TrunkD under the ring's base, at the top socket).
        var (wx, wr) = (1.2 - ShipWorksParts.TrunkR[0] - 0.02, ShipWorksParts.TrunkR[0] + 0.02);
        var wy = Math.Round(1.0 - ShipWorksParts.TrunkD[0] - 0.02, 2);
        p.Add(Box([0.0, -0.45, -0.75], [wx, 1.0, 0.75], hidden: true));
        p.Add(Box([wx, -0.45, -0.75], [1.45, wy, 0.75], hidden: true));
        p.Add(Box([wx, wy, -0.75], [1.45, 1.0, -wr], hidden: true));
        p.Add(Box([wx, wy, wr], [1.45, 1.0, 0.75], hidden: true));
        p.Sock("hull", [0, 0, 0], "-x");
        p.Sock("top", [1.2, 1.0, 0], "+y");
        return p.Stats(1800, 8000, 40, 8.0);
    }

    // ---- the drive (four bells on the collar, each with its own engine), the lift and retro jets, the legs

    // (lift 1.7 MN a jet, from 1.5, since the shuttle came: the carried pod is 11 t on 233 t, and at 1.5 the hover hold overshot past the flight check's metre)
    private const double DriveForce = 9000000, LiftForce = 1700000, RetroForce = 1200000;

    // The main drive is four bells of a quarter of it each, every one a hull drive with its own engine behind it (ship-works.md 15:
    // a Drive Core M and a Fuel Pump M), so they burn a hull drive's 2 l/s a MN. The lift jets and retros are whole parts with
    // nothing inside (Karl, 2026-11-16: "Main drives only") and burn what they did before the works came.
    private const double Burn = ShipWorksParts.HullBurn, JetBurn = 2.5;
    private static double BellForce => DriveForce / BellX.Length;

    /// <summary>The drive collar across the whole stern: a faceted armoured shroud standing off the stern, heat-sink vanes, and its
    /// plate, the mount the four bells bolt to outside and their engines inside. Origin on the stern face, the plate's outer face at
    /// z 1.2. It closes the engine bay: the engines run through the shroud from the plate forward. It holds the bells' header tank.</summary>
    private static Part Collar()
    {
        var p = new Part("Zero Day Drive Collar", Small, Section, $"The Zero Day's drive collar: an armoured shroud the width of the stern and the plate its four drive bells bolt to, {DriveForce / 1e6:0} MN in all. Each bell's engine (a Drive Core and a Fuel Pump) bolts to the plate's inner face, on that bell's axis, and runs forward into the engine bay.", role: "fueltank", tool: "welder", material: "steel", color: Frame, group: Group);
        var q = At(Stern);
        double[][] Outline(double z, double grow, double dy) => [.. Enumerable.Range(0, 5).Select(k => new[] { Py.Round(q[k].H * grow, 4), Py.Round(q[k].Y - DriveY + dy, 4), z }).Concat(Enumerable.Range(0, 5).Reverse().Select(k => new[] { Py.Round(-q[k].H * grow, 4), Py.Round(q[k].Y - DriveY + dy, 4), z }))];
        var a = Outline(0.006, 0.995, 0);
        var b = Outline(CollarFace, 0.93, 0);
        for (var i = 0; i < 10; i++)
        {
            var j = (i + 1) % 10;
            Quad(p, a[i], a[j], b[j], b[i], i % 5 == 2 ? Armour2 : Armour, "plated steel");
        }
        // The plate: 0.3 m of armour the bells come out of, dark on its face.
        Prism(p, [.. Outline(CollarPlate, 0.935, 0).Select(v => new[] { v[0], v[1], CollarPlate })], [.. b.Select(v => new[] { Py.Round(v[0] * 0.995, 4), v[1], CollarFace - 0.004 })], "#1a1c1e", "soot");
        // Heat-sink vanes above and below the bells, and the engines' cradles inside the shroud.
        foreach (var x in new[] { -5.0, -2.9, 0.0, 2.9, 5.0 })
        {
            p.Add(Box([x - 0.04, 1.6, 0.3], [x + 0.04, 3.3, 1.15], Armour2, detail: true, shape: Kw(("finish", "plated steel"))));
            p.Add(Box([x - 0.04, -3.4, 0.3], [x + 0.04, -1.4, 1.15], Armour2, detail: true, shape: Kw(("finish", "plated steel"))));
        }
        var feet = BellY - ShipWorksParts.AxisOver(1);
        foreach (var x in BellX)
        {
            var bottom = Math.Abs(x) + 0.3 <= 2.6 ? -1.1 : Math.Round(Bottom(q, x, 0.3) - DriveY + 0.06, 3); // (the middle two stand on the hidden block under them)
            p.Add(Box([x - 0.3, bottom, 0.06], [x + 0.3, feet - 0.004, CollarPlate - 0.04], Frame, shape: Kw(("finish", "painted steel"))));
        }
        // Hidden: the plate whole; inside the shroud, only what is above and below the engines (they run through the rest).
        p.Add(Box([-5.6, -1.2, CollarPlate], [5.6, 1.6, CollarFace], hidden: true));
        p.Add(Box([-4.6, 1.6, CollarPlate], [4.6, 3.8, CollarFace], hidden: true));
        p.Add(Box([-2.6, -3.9, CollarPlate], [2.6, -1.2, CollarFace], hidden: true));
        p.Add(Box([-4.6, 1.3, 0.0], [4.6, 3.8, CollarPlate], hidden: true));
        p.Add(Box([-2.6, -3.9, 0.0], [2.6, -1.1, CollarPlate], hidden: true));
        p.Sock("front", [0, 0, 0], "-z");
        for (var i = 0; i < BellX.Length; i++)
        {
            p.Sock($"bell{i}", [BellX[i], BellY, CollarFace], "+z");
            p.Sock($"engine{i}", [BellX[i], BellY, CollarPlate], "-z");
        }
        p.Set(("fuelTank", Kw(("capacity", 600))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [0, 1.0, 0], "-z", "fuel"))));
        Works.Mount(p);
        return p.Stats(6000, 30000, 45, 20.0);
    }

    /// <summary>The lowest point inside the collar's shroud at x (from its outline at the stern, shrunk to the plate's 0.93), plus a margin.</summary>
    private static double Bottom((double H, double Y)[] q, double x, double margin)
    {
        var ax = Math.Abs(x) + margin;
        for (var k = 0; k < 4; k++)
        {
            double h0 = q[k].H * 0.93, h1 = q[k + 1].H * 0.93;
            if (ax <= h0)
                return q[k].Y;
            if (ax <= h1)
                return q[k].Y + (q[k + 1].Y - q[k].Y) * (ax - h0) / (h1 - h0);
        }
        return q[4].Y;
    }

    /// <summary>One of the drive's four bells: a throat and bell in rings, a copper band, the link-blue glow deep in it. Origin on the
    /// collar's plate; pushes along -z (its exhaust +z). Its engine is behind the plate on its axis.</summary>
    private static Part Bell()
    {
        var p = new Part("Zero Day Drive Bell", Small, Section, $"One of the Zero Day's four drive bells, {BellForce / 1e6:0.00} MN, on the drive collar. Its engine is right behind it, through the collar's plate: a Drive Core and a Fuel Pump on its axis, running forward into the engine bay.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        (double Z, double R)[] st = [(0.0, 1.15), (0.4, 1.0), (1.2, 1.2), (2.1, 1.4)];
        for (var s = 0; s < st.Length - 1; s++)
        {
            var r0 = RingZ(0, 0, st[s].R, st[s].R, st[s].Z + (s == 0 ? 0.004 : 0), 10);
            var r1 = RingZ(0, 0, st[s + 1].R, st[s + 1].R, st[s + 1].Z, 10);
            for (var i = 0; i < 10; i++)
                Quad(p, r0[i], r0[(i + 1) % 10], r1[(i + 1) % 10], r1[i], s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        }
        Prism(p, RingZ(0, 0, 1.24, 1.24, 1.4, 10), RingZ(0, 0, 1.3, 1.3, 1.55, 10), "#5a4a3a", "copper");
        Plate(p, [.. RingZ(0, 0, 0.85, 0.85, 0.45, 10)], Glow, glow: true);
        p.Add(Box([-1.0, -1.0, 0.0], [1.0, 1.0, 2.1], hidden: true));
        p.Sock("mount", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", BellForce), ("spool", 2.0), ("exhaust", "+z"), ("at", Py.L(0, 0, 2.1)), ("burnRate", Py.Round(BellForce / 1e6 * Burn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 1.0, 0.1], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, 2.1)), ("dir", "+z"), ("radius", 1.15), ("length", 9.0), ("kind", "Rocket")))));
        Works.Head(p, Works.Table(), "torch", BellForce);
        return p.Stats(2000, 6000, 35, 8.0);
    }

    /// <summary>A jet in a squat armoured housing: lift (down) or retro (forward). Origin at its root; the bell along -y.</summary>
    private static Part Jet(string name, string desc, double force, double scale)
    {
        var burn = JetBurn;
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
        p.Set(("thruster", Kw(("force", force), ("spool", 0.5), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-1.05 * scale, 4), 0)), ("burnRate", Py.Round(force / 1e6 * burn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.45 * scale, 4), Py.Round(-0.2 * scale, 4), 0], "+x", "fuel"))));
        // The flame out of the bell's mouth, as wide as it (the default would be 5 m across from a 1 m bell).
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-1.05 * scale, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.4 * scale, 4)), ("length", Py.Round(2.8 * scale, 4)), ("kind", "Rocket")))));
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
    private const double RailEnergy = 15000; // kJ a slug takes from the capacitors (ship-works.md 8): the Capacitor Bank M aboard holds two and a bit, and refills one in 10 s, its rate of fire

    private static Part Railgun()
    {
        var p = new Part("Zero Day Railgun", Small, Section, "The Zero Day's railgun: one slug at great speed through anything in a line, each shot 15 MJ from the ship's Capacitor Bank. Laid along the spine, so the ship is the aim.", role: "gun", tool: "welder", material: "steel", color: Armour, group: Group);
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
        p.Set(("gun", Kw(("damage", 900), ("rpm", 6), ("spread", 0.04), ("range", 800), ("mag", 1), ("reload", 7.0), ("energy", RailEnergy), ("flags", Py.L("penetrating")), ("kick", 3500), ("fx", "cannon"), ("speed", 1100), ("soundOf", "Leviathan .50"), ("muzzle", Py.L(0, 0.3, Py.Round(-RailLen - 0.42, 4))))), ("centreSockets", false));
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

    /// <summary>The crew's locker (Karl, 2026-11-16: the shared open-shelf Locker "looks a little bland"): a closed graphite cabinet with
    /// a raked top, its door the one panel the blueprint paints (lanyard yellow at the airlock, corporate blue in the cabins), louvres top
    /// and bottom, a recessed pull, a badge reader with its status lamp, a lit name strip and a link-blue edge light. The same size and
    /// sockets as the Locker, so it hangs by "wall" where that did.</summary>
    private static Part Locker()
    {
        var p = new Part("Zero Day Locker", Small, Section, "A Zero Day crew locker: a closed cabinet with two shelves for a person's kit, a badge reader on its door.", role: "storage", tool: "wrench", material: "steel", color: Blue, group: Group);
        p.Set(("finish", "painted steel"));
        PyDict Keep(string finish) => Kw(("finish", finish), ("paintable", false));
        PyDict Lit() => Kw(("glow", true), ("paintable", false));
        void D(double x0, double y0, double z0, double x1, double y1, double z1, string col, PyDict shape) => p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: true, shape: shape));
        foreach (var (mn, mx) in new (double[], double[])[] // (what a hand or a round meets: a shell round the shelves' room)
        {
            ([-0.3, 0, -0.25], [0.3, 0.085, 0.2]), ([-0.3, 1.75, -0.25], [0.3, 1.9, 0.2]), ([-0.3, 0.085, -0.25], [0.3, 1.75, -0.215]),
            ([-0.3, 0.085, -0.215], [-0.27, 1.75, 0.2]), ([0.27, 0.085, -0.215], [0.3, 1.75, 0.2]), ([-0.27, 0.085, 0.175], [0.27, 1.75, 0.2]),
            ([-0.27, 0.925, -0.215], [0.27, 0.975, 0.175]),
        })
            p.Add(Box(mn, mx, Armour, hidden: true));
        // The carcass: graphite, its top raked down to the front.
        double[][] Side(double x) => [[x, 0.08, -0.25], [x, 0.08, 0.2], [x, 1.76, 0.2], [x, 1.9, 0.07], [x, 1.9, -0.25]];
        Prism(p, Side(-0.3), Side(0.3), Armour, "painted steel");
        D(-0.285, 0, -0.235, 0.285, 0.085, 0.182, Frame, Keep("dirty steel")); // (the plinth, into the carcass)
        for (var i = 0; i < 4; i++)
            D(-0.21 + i * 0.11, 0.02, 0.174, -0.16 + i * 0.11, 0.055, 0.188, Hazard, Keep("hazard paint"));
        // The door, standing 12 mm proud: the paint goes here.
        D(-0.262, 0.12, 0.195, 0.262, 1.72, 0.212, Blue, Kw(("finish", "painted steel")));
        D(-0.262, 0.94, 0.205, 0.262, 0.952, 0.217, Frame, Keep("dirty steel")); // (the seam between its two panels)
        for (var i = 0; i < 5; i++)
            D(-0.17, 1.5 + i * 0.035, 0.206, 0.17, 1.515 + i * 0.035, 0.218, Frame, Keep("dirty steel")); // louvres, top
        for (var i = 0; i < 3; i++)
            D(-0.17, 0.2 + i * 0.035, 0.206, 0.17, 0.215 + i * 0.035, 0.218, Frame, Keep("dirty steel")); // and bottom
        D(0.185, 0.72, 0.208, 0.232, 1.17, 0.236, WallTrim, Keep("painted steel")); // the pull
        D(0.193, 0.76, 0.228, 0.224, 1.13, 0.242, "#7a8088", Keep("steel"));
        D(0.12, 1.25, 0.208, 0.235, 1.42, 0.226, Console, Keep("plastic")); // the badge reader
        D(0.135, 1.31, 0.219, 0.22, 1.405, 0.231, Screen, Lit());
        D(0.15, 1.265, 0.219, 0.2, 1.29, 0.231, "#40e070", Lit()); // its lamp: unlocked
        D(-0.2, 1.33, 0.208, 0.06, 1.39, 0.22, Console, Keep("plastic")); // the name strip
        D(-0.19, 1.34, 0.214, 0.05, 1.38, 0.226, "#d8f0ff", Lit());
        D(-0.293, 0.15, 0.194, -0.271, 1.7, 0.206, Glow, Lit()); // the edge light, down the hinge side
        foreach (var s in new[] { -1, 1 }) // (a pressed rib down each side)
            D(s > 0 ? 0.296 : -0.308, 0.3, -0.12, s > 0 ? 0.308 : -0.296, 1.6, -0.06, WallTrim, Keep("painted steel"));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 1.9, 0], "+y");
        p.Sock("back", [0, 0.95, -0.25], "-z");
        p.Sock("+x", [0.3, 0.95, 0], "+x");
        p.Sock("-x", [-0.3, 0.95, 0], "-x");
        p.Sock("wall", [0, 0.125, -0.25], "-z");
        PyDict Slot(double[] mn, double[] mx) => new() { ["min"] = mn.Select(v => (object?)v).ToList(), ["max"] = mx.Select(v => (object?)v).ToList() };
        p.Set(("storage", new PyDict { ["slots"] = Py.L(Slot([-0.26, 0.09, -0.21], [0.26, 0.92, 0.17]), Slot([-0.26, 0.98, -0.21], [0.26, 1.74, 0.17])) }));
        return p.Stats(60, 320, 6, 2.0);
    }

    // ---- the ship in welded pieces (Karl, 2026-11-16, after the Halberd's: one hit shouldn't take a whole deck). Each big part is drawn
    // whole above, then its boxes are dealt out. A deck part keeps its floor (and what stands loose on it) under its own name; each wall
    // run (WallRun) is a wall piece; the upper decks' ceilings are a piece; the hull outside the rooms (the skin, its armour and the void
    // behind it) is one piece a side per station band. The keel and the spine are cut at the stations. A socket goes with the piece whose
    // face it is on; wherever two pieces' solid boxes touch they get a facing socket pair (Connect): one pair places a piece (PlaceOrder,
    // out from the keel), the game welds the rest (ConstructBuild.AutoLink), so a piece shot off leaves the others welded.

    private readonly record struct WallLine(bool AlongX, double A0, double A1, double C, double Y0, double Y1);

    private static readonly Dictionary<Part, List<WallLine>> Walls = [];
    private static readonly Dictionary<Part, List<(Rect[] Plan, double Y0, double Y1)>> Floors = [];
    private static readonly Dictionary<(string Part, string Sock), string> SockOwner = []; // (a big part's socket: the piece that holds it now)
    private static readonly double[] Bands = [8.0, 14.0, 20.0, 30.0, 38.0, 42.0]; // (the stations the hull pieces are cut at)

    private static double[] Vd(PyDict b, string key) => [.. ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H)];

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    private static double[] Mid3(PyDict b)
    {
        double[] mn = Vd(b, "min"), mx = Vd(b, "max");
        return [(mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2];
    }

    private static int Band(double z)
    {
        var i = 0;
        while (i < Bands.Length - 2 && z >= Bands[i + 1])
            i++;
        return i;
    }

    /// <summary>A deck part dealt out into its floor (the part itself), its wall runs, its ceiling and its hull pieces.</summary>
    private static List<Part> SplitDeck(Part core, string shortName)
    {
        var walls = Walls.TryGetValue(core, out var ws) ? ws : [];
        var wallPieces = new Part?[walls.Count];
        var hull = new SortedDictionary<(int Side, int Band), Part>();
        Part? ceiling = null;
        var low = shortName.StartsWith("Lower") ? "lower" : "upper";
        // The rooms' outline at each height: a lower deck's floor plan up to its ceiling; an upper deck's floor slab (wider) at its own
        // height, then the ceiling's plan above it. Outside that is hull.
        var rooms = new List<(Rect R, double Y0, double Y1)>();
        foreach (var (plan, y0, _) in Floors[core])
        {
            var (ra, rb) = y0 < 0.1 ? (0.0, Ceil0) : y0 < Ceil0 + 0.01 ? (Ceil0, Ft1) : (Ft1, Roof);
            foreach (var r in plan)
                rooms.Add((r, ra, rb));
        }
        var upper = Floors[core].Any(q => q.Y0 > Ceil1 - 0.01);
        bool Inside(double[] c) => rooms.Any(q => c[0] > q.R.X0 - 0.02 && c[0] < q.R.X1 + 0.02 && c[2] > q.R.Z0 - 0.02 && c[2] < q.R.Z1 + 0.02 && c[1] > q.Y0 - 0.02 && c[1] < q.Y1 + 0.02);
        int? WallOf(double[] c, double thick, bool alongX, bool any)
        {
            for (var i = 0; i < walls.Count; i++)
            {
                var w = walls[i];
                if (!any && w.AlongX != alongX)
                    continue;
                double u = w.AlongX ? c[0] : c[2], v = w.AlongX ? c[2] : c[0];
                if (Math.Abs(v - w.C) <= 0.15 && thick <= 0.32 && u > Math.Min(w.A0, w.A1) - 0.08 && u < Math.Max(w.A0, w.A1) + 0.08 && c[1] > w.Y0 - 0.01 && c[1] < w.Y1 + 0.1)
                    return i;
            }
            return null;
        }
        Part WallPiece(int i) => wallPieces[i] ??= Big($"Zero Day {shortName} Wall {i + 1}", $"A wall of the Zero Day's {low} deck ({shortName.ToLowerInvariant()}), with its door frames and what is on its faces.", color: Wall);
        Part Of(PyDict b)
        {
            double[] mn = Vd(b, "min"), mx = Vd(b, "max"), c = Mid3(b);
            foreach (var alongX in new[] { true, false })
                if (WallOf(c, alongX ? mx[2] - mn[2] : mx[0] - mn[0], alongX, false) is int i)
                    return WallPiece(i);
            if (!Inside(c))
            {
                var key = (c[0] < Cx ? -1 : 1, Band(c[2]));
                if (!hull.TryGetValue(key, out var h))
                    hull[key] = h = Big($"Zero Day {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece of the Zero Day's {low} deck ({shortName.ToLowerInvariant()}), the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (upper && c[1] > Ceil1 - 0.35)
                return ceiling ??= Big($"Zero Day {shortName} Ceiling", $"The ceiling of the Zero Day's {low} deck ({shortName.ToLowerInvariant()}), with its light strips.", color: Ceil);
            return core;
        }
        double mass = Convert.ToDouble(core.D["mass"]), health = Convert.ToDouble(core.D["health"]);
        var boxes = core.Boxes.ToList();
        core.Boxes.Clear();
        foreach (var b in boxes)
            Of(b).Add(b);
        var all = new List<Part> { core };
        all.AddRange(wallPieces.Where(q => q != null)!);
        if (ceiling != null)
            all.Add(ceiling);
        // A hull piece needs something drawn and something solid: one that lacks either joins the next band's on its side (the upper
        // deck aft's void from z 25 under the upper deck fore's plates), else the floor.
        foreach (var (key, h) in hull.ToList())
        {
            if (h.Boxes.Any(Drawn) && h.Boxes.Any(q => !Drawn(q)))
                continue;
            hull.Remove(key);
            var to = hull.Where(q => q.Key.Side == key.Side && q.Value.Boxes.Any(Drawn)).OrderBy(q => Math.Abs(q.Key.Band - key.Band)).Select(q => q.Value).FirstOrDefault() ?? core;
            to.Boxes.AddRange(h.Boxes);
        }
        all.AddRange(hull.Values);
        DealSockets(core, all, p => WallOf(p, 0, true, true) is int i ? WallPiece(i) : null);
        (double Mass, double Hp, int Armour) Kind(Part q) => q == ceiling ? (1200.0, 5000.0, 25) : q.Color == Wall ? (700.0, 3500.0, 25) : (1800.0, 9000.0, 40);
        var others = all.Skip(1).Sum(q => Kind(q).Mass);
        var share = Math.Min(1.0, mass * 0.6 / others); // (the pieces take at most 60% of the part's mass; the floor keeps the rest, so the ship weighs what it did)
        foreach (var q in all.Skip(1))
        {
            var (m, hp, ar) = Kind(q);
            q.Stats(m * share, hp, ar, 6.0);
        }
        core.Stats(mass - others * share, health * 0.5, 30, 15.0);
        return all;
    }

    /// <summary>The keel or the spine cut at the stations: the band holding coreZ keeps the part's name (and its tankage).</summary>
    private static List<Part> SplitBands(Part core, string shortName, double coreZ)
    {
        var keep = Band(coreZ);
        var bands = new SortedDictionary<int, Part>();
        double mass = Convert.ToDouble(core.D["mass"]), health = Convert.ToDouble(core.D["health"]);
        var armour = core.D["armour"];
        var boxes = core.Boxes.ToList();
        core.Boxes.Clear();
        foreach (var b in boxes)
        {
            var k = Band(Mid3(b)[2]);
            if (k == keep)
                core.Add(b);
            else
            {
                if (!bands.TryGetValue(k, out var piece))
                    bands[k] = piece = Big($"Zero Day {shortName} {k + 1}", $"A length of the Zero Day's {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
                piece.Add(b);
            }
        }
        var all = new List<Part> { core };
        all.AddRange(bands.Values);
        DealSockets(core, all, at => Band(at[2]) == keep ? core : bands.GetValueOrDefault(Band(at[2]))); // (a mount inside a drawn plate: the band it is in)
        foreach (var q in all)
            q.Stats(mass / all.Count, health * 0.45, armour, 10.0);
        if (core.D.Get("fuelTank") is PyDict tank) // (the keel's tankage shared out: a jet takes its fuel from the tank it is on)
        {
            var each = (int)Math.Round(Convert.ToDouble(tank["capacity"]) / all.Count);
            foreach (var q in all)
            {
                q.D["role"] = core.D["role"];
                q.Set(("fuelTank", Kw(("capacity", each))));
                if (q != core) // (a tank has a fuel-out port: on its aft face, under the floor, for a pipe a player lays)
                {
                    var zmax = q.Boxes.Where(bx => !Drawn(bx)).Max(bx => Convert.ToDouble(((List<object?>)bx["max"]!)[2]));
                    q.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. new[] { Cx - H, -1.0 - H, zmax }.Cast<object>()], "+z", "fuel"))));
                }
            }
        }
        return all;
    }

    /// <summary>A split part's sockets: each to the piece whose box face it is on (a solid box first), else to fallback's, else the core.</summary>
    private static void DealSockets(Part core, List<Part> all, Func<double[], Part?> fallback)
    {
        var name = (string)core.D["name"]!;
        var socks = core.Sockets.ToList();
        core.Sockets.Clear();
        foreach (var sk in socks)
        {
            var at = ((List<object?>)sk["at"]!).Select(v => Convert.ToDouble(v) + H).ToArray();
            var dir = (string)sk["dir"]!;
            var ax = dir[1] - 'x';
            var plus = dir[0] == '+';
            Part? owner = null, drawnOwner = null;
            foreach (var q in all)
                foreach (var b in q.Boxes)
                {
                    double[] mn = Vd(b, "min"), mx = Vd(b, "max");
                    if (Math.Abs((plus ? mx[ax] : mn[ax]) - at[ax]) > 0.005)
                        continue;
                    var inside = true;
                    for (var a = 0; a < 3; a++)
                        if (a != ax && (at[a] < mn[a] - 0.005 || at[a] > mx[a] + 0.005))
                            inside = false;
                    if (!inside)
                        continue;
                    if (Drawn(b))
                        drawnOwner ??= q;
                    else
                        owner ??= q;
                }
            owner ??= fallback(at) ?? drawnOwner ?? core;
            owner.Sockets.Add(sk);
            SockOwner[(name, (string)sk["name"]!)] = (string)owner.D["name"]!;
        }
    }

    /// <summary>A facing socket pair wherever two of the parts' solid boxes touch face to face (one pair a pair of parts, on their
    /// biggest contact), unless they already meet at one: what welds the pieces to each other.</summary>
    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min"), Mx: Vd(b, "max"))).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = parts.Select(q => q.Sockets.Count(s => ((string)s["name"]!).StartsWith('j'))).ToArray();
        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
            {
                var (bi, bj) = (bounds[i], bounds[j]);
                if (Enumerable.Range(0, 3).Any(a => bi.Mn[a] > bj.Mx[a] + 0.002 || bj.Mn[a] > bi.Mx[a] + 0.002))
                    continue;
                if (Meet(parts[i], parts[j]))
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

    /// <summary>Whether two parts (at one origin) already have a socket of each at one point, facing each other.</summary>
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

    /// <summary>The order to place the structure in: out from the first part over the socket pairs they meet at.</summary>
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
            throw new InvalidOperationException("Zero Day: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    private static List<string> Structure = [];
    private static Dictionary<string, int> PieceSteps = [];

    /// <summary>The step that holds socket sock of step on: the piece that took it when its part was split, else on itself.</summary>
    private static int Holder(BuildingParts.Blueprint bp, int on, string sock) =>
        bp.Steps[on]["part"] is string part && SockOwner.TryGetValue((part, sock), out var piece) ? PieceSteps[piece] : on;

    private static int Hang(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int spin = 0) => bp.Hang(part, Holder(bp, on, sock), sock, by, spin);

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
        var s = Hang(bp, "Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps)
    {
        var s = Hang(bp, "Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = true };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    /// <summary>A point-defence turret on a barbette: a Turret Ring S (its trunk down the barbette's well), a Motor Hinge, the PDC. Its
    /// rounds come up the ring's feed from the magazine (ship-works.md 5.2), so nothing is bolted to the gun outside.</summary>
    private static int Turret(BuildingParts.Blueprint bp, int pod, out int ring)
    {
        ring = Hang(bp, "Turret Ring S", pod, "top", "base");
        var hinge = Hang(bp, "Motor Hinge", ring, "top", "base");
        return Hang(bp, "Zero Day PDC", hinge, "leaf", "bottom");
    }

    /// <summary>
    /// A PDC magazine's feed (body frame): from its container's Ammo out up 15 cm, out through the wall, the void and the hull level
    /// with that, then down into the barbette under the trunk's foot and up into the ring's Ammo in (ship-works.md 5.2).
    /// </summary>
    private static double[][] MagazineFeed(BuildingParts.Blueprint bp, int mag, int ring)
    {
        var o = bp.Parts[0].Pos;
        double[] At(int step, double[] local)
        {
            var a = BuildingParts.Apply(bp.Parts[step].Rot, local);
            return [bp.Parts[step].Pos[0] + a[0] - o[0], bp.Parts[step].Pos[1] + a[1] - o[1], bp.Parts[step].Pos[2] + a[2] - o[2]];
        }
        var top = At(mag, [0, 0.2, 0]);
        var foot = At(ring, [0, -ShipWorksParts.TrunkD[0], 0]);
        var s = Math.Sign(foot[0] - top[0]);
        double[] R(double x, double y, double z) => [Py.Round(x, 3), Py.Round(y, 3), Py.Round(z, 3)];
        return [R(top[0], top[1] + 0.15, top[2]), R(foot[0] - s * 0.3, top[1] + 0.15, top[2]), R(foot[0], foot[1] - 0.175, foot[2])];
    }

    private static int Door(BuildingParts.Blueprint bp, int on, string sock) => Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", on, sock, "hang"), Armour, "plated steel");

    /// <summary>The frigate's blueprint; <paramref name="crewed"/>: with an AI crew that fights everyone (its flight computer's AI crew setting; ConstructShipAi.cs), a test enemy for the parts menu.</summary>
    private static PyDict Blueprint(Dictionary<string, PyDict> defs, bool crewed = false)
    {
        var bp = crewed
            ? new BuildingParts.Blueprint("Zero Day Frigate (crewed)", "The Zero Day with a crew aboard: it lifts off, closes on the nearest player, you included, and fights with its railgun, turrets and torpedoes. Sit at its helm to take it.", defs, maxSteps: 420)
            : new BuildingParts.Blueprint("Zero Day Frigate", "A torpedo frigate: two decks the length of a sleek armoured hull, a rotary torpedo bay in each flank, a spinal railgun, four point-defence turrets, flare launchers and four drive bells across the stern, each with its own engine.", defs, maxSteps: 420);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in Structure)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = placed;
        int keel = placed["Zero Day Keel"], bow = placed["Zero Day Bow"], lf = placed["Zero Day Lower Deck Fore"], la = placed["Zero Day Lower Deck Aft"],
            stern = placed["Zero Day Stern"], uf = placed["Zero Day Upper Deck Fore"], ua = placed["Zero Day Upper Deck Aft"], spine = placed["Zero Day Spine"],
            bayL = placed["Zero Day Torpedo Bay L"], bayR = placed["Zero Day Torpedo Bay R"];
        // The drive: the collar on the stern, a bell on its plate outside, and behind each bell, through the plate on its axis, its
        // own engine running forward into the bay (a Drive Core M, then a Fuel Pump M bolted on behind it: ship-works.md 15).
        var collar = Hang(bp, "Zero Day Drive Collar", stern, "drive", "front");
        for (var i = 0; i < BellX.Length; i++)
        {
            Hang(bp, "Zero Day Drive Bell", collar, $"bell{i}", "mount");
            Hang(bp, "Fuel Pump M", Hang(bp, "Drive Core M", collar, $"engine{i}", "front"), "back", "front");
        }
        foreach (var n in new[] { "liftFL", "liftFR", "liftBL", "liftBR" })
            Hang(bp, "Zero Day Lift Jet", keel, n, "top");
        foreach (var n in new[] { "legFL", "legFR", "legBL", "legBR" })
            Hang(bp, "Zero Day Landing Leg", keel, n, "top");
        foreach (var n in new[] { "retroL", "retroR" })
            Hang(bp, "Zero Day Retro Jet", bow, n, "top");
        foreach (var piece in new[] { bow, stern })
            foreach (var n in new[] { "rcsTopL", "rcsTopR", "rcsSideL", "rcsSideR" })
                Hang(bp, "Heavy RCS Thruster", piece, n, "base");
        // Ladders: the boarding ladder up the shaft, two between the decks.
        Hang(bp, "Zero Day Boarding Ladder", la, "boarding", "hull");
        Hang(bp, "Zero Day Ladder", lf, "ladder", "bottom");
        Hang(bp, "Zero Day Ladder", la, "ladder", "bottom");
        // Lower deck fore: the server room (the IT style's own), the armoury, the torpedo room.
        for (var i = 0; i < 4; i++)
        {
            Hang(bp, "Server Rack", lf, $"serverL{i}", "foot00");
            Hang(bp, "Server Rack", lf, $"serverR{i}", "foot00");
        }
        Hang(bp, "Computer", lf, "computer", "foot00");
        Hang(bp, "Office Plant", lf, "plant", "base");
        Hang(bp, "Requisition Crate", lf, "crate0", "-y");
        Hang(bp, "Requisition Crate", lf, "crate1", "-y");
        for (var i = 0; i < 3; i++)
            Paint(bp, Hang(bp, "Zero Day Locker", lf, $"locker{i}", "wall"), Armour, "worn paint");
        Hang(bp, "Tool Rack", lf, "toolRack", "back");
        Hang(bp, "Ammo Crate", lf, "ammo0", "bottom");
        Hang(bp, "Ammo Crate", lf, "ammo1", "bottom");
        Hang(bp, "Ammo Crate", lf, "warheads0", "bottom");
        Hang(bp, "Ammo Crate", lf, "warheads1", "bottom");
        foreach (var d in new[] { "doorServer", "doorTorpedo", "doorBayL", "doorBayR" })
            Door(bp, lf, d);
        var fireL = Hang(bp, "Button", lf, "fireL", "-x");
        var fireR = Hang(bp, "Button", lf, "fireR", "+x");
        Switch(bp, lf, "switchServer", LampAt(bp, lf, "lampServer", "#a0d0ff"));
        Switch(bp, lf, "switchFore", LampAt(bp, lf, "lampArmouryL"), LampAt(bp, lf, "lampArmouryR"));
        Switch(bp, lf, "switchTorp", LampAt(bp, lf, "lampTorpL", "#ffd8a0"), LampAt(bp, lf, "lampTorpR", "#ffd8a0"));
        foreach (var bay in new[] { bayL, bayR })
            Switch(bp, bay, "switch", LampAt(bp, bay, "lamp", "#ff7050"));
        // Lower deck aft: suits, the airlock, engineering.
        for (var i = 0; i < 4; i++)
            Paint(bp, Hang(bp, "Zero Day Locker", la, $"suit{i}", "wall"), Hazard, "worn paint");
        Paint(bp, Hang(bp, "Zero Day Locker", la, "airlockLocker", "wall"), Hazard, "worn paint");
        for (var i = 0; i < 4; i++)
            Hang(bp, "Robot Shell", Hang(bp, "Robot Shell Dock", la, $"shellDock{i}", "wall"), "dock", "back");
        foreach (var d in new[] { "doorBayL", "doorBayR", "doorAirlock", "doorEngine" })
            Door(bp, la, d);
        Hang(bp, "Zero Day Reactor", la, "reactor", "bottom");
        Hang(bp, "Power Board", la, "board", "back");
        Hang(bp, "Engineer's Panel", la, "panel", "back");
        for (var i = 0; i < 3; i++)
            Hang(bp, "Battery", la, $"battery{i}", "-y");
        Hang(bp, "Capacitor Bank M", la, "capacitor", "deck"); // (the railgun fires from it: Karl, 2026-11-17, energy guns fire from capacitors only)
        Hang(bp, "Pipe Bank", la, "pipes", "back");
        Hang(bp, "Tool Rack", la, "toolRack", "back");
        Hang(bp, "Requisition Crate", la, "crate0", "-y");
        Hang(bp, "Requisition Crate", la, "crate1", "-y");
        Switch(bp, la, "switchAirlock", LampAt(bp, la, "lampAirlock", "#ff5040"), LampAt(bp, la, "lampSuit"));
        Switch(bp, la, "switchEng", LampAt(bp, la, "lampEngL", "#a0d0ff"), LampAt(bp, la, "lampEngR", "#a0d0ff")); 
        Switch(bp, la, "switchTorp", LampAt(bp, la, "lampTorpL", "#ffd8a0"), LampAt(bp, la, "lampTorpR", "#ffd8a0"));
        // Upper deck fore: the bridge, ops, the cabins.
        var pilot = Hang(bp, "Flight Couch", uf, "pilot", "bottom");
        Hang(bp, "Flight Couch", uf, "copilot", "bottom");
        var fc = Hang(bp, "Zero Day Flight Computer", uf, "computer", "bottom");
        // Ship keys with mouse aim, as the other ships fly (Karl, 2026-11-18: on Auto it flew as a rotorcraft, so W tipped it on its lift jets and Ctrl never lit the roof's jets).
        var flight = ShipParts.ShipFlight();
        if (crewed)
            flight["aiCrew"] = (int)ShipAiMode.Everyone;
        bp.Steps[fc]["config"] = flight;
        bp.Wires.Add((pilot, fc));
        var railModule = Hang(bp, "Turret Module", uf, "gunModule", "-y");
        Hang(bp, "Chart Table", uf, "chart", "back");
        foreach (var (i, x, _) in LinkChairs)
            bp.HangTurned("Shell Control Chair", Holder(bp, uf, $"linkChair{i}"), $"linkChair{i}", "bottom", i == 0 ? BuildingParts.RY270 : BuildingParts.RY90);
        Paint(bp, Hang(bp, "Zero Day Locker", uf, "bridgeLocker", "wall"), Armour, "worn paint");
        Hang(bp, "Chart Table", uf, "plot", "bottom");
        Paint(bp, Hang(bp, "Crash Couch", uf, "couchA", "wall"), "#2c3e58", "fabric");
        Paint(bp, Hang(bp, "Zero Day Locker", uf, "lockerA", "wall"), Blue, "worn paint");
        Paint(bp, Hang(bp, "Crash Couch", uf, "couchCaptain", "wall"), "#2c3e58", "fabric");
        Hang(bp, "Steel Desk", uf, "deskCaptain", "wall");
        Paint(bp, Hang(bp, "Zero Day Locker", uf, "lockerCaptain", "wall"), Blue, "worn paint");
        foreach (var d in new[] { "doorBridge", "doorOps", "doorCabinA", "doorCaptain" })
            Door(bp, uf, d);
        var flareButton = Hang(bp, "Button", uf, "flares", "+z");
        Switch(bp, uf, "switchBridge", LampAt(bp, uf, "lampBridgeL", "#ff6050"), LampAt(bp, uf, "lampBridgeR", "#ff6050"));
        Switch(bp, uf, "switchOps", LampAt(bp, uf, "lampOpsL", "#a0c8ff"), LampAt(bp, uf, "lampOpsR", "#a0c8ff"));
        Switch(bp, uf, "switchCabins", LampAt(bp, uf, "lampCabinA", "#ffd8a0"), LampAt(bp, uf, "lampCaptain", "#ffd8a0"));
        // Upper deck aft: the crew cabin, the head, the med bay, the mess.
        Paint(bp, Hang(bp, "Crash Couch", ua, "couchB0", "wall"), "#2c3e58", "fabric");
        Paint(bp, Hang(bp, "Zero Day Locker", ua, "lockerB", "wall"), Blue, "worn paint");
        Hang(bp, "Steel Desk", ua, "deskB", "wall");
        Hang(bp, "Ship Toilet", ua, "toilet", "wall");
        Hang(bp, "Ship Basin", ua, "basin", "wall");
        Paint(bp, Hang(bp, "Shower Stall", ua, "shower", "wall"), MedWhite, "gloss paint");
        Hang(bp, "Medical Station", ua, "med0", "back");
        Hang(bp, "Medical Station", ua, "med1", "back");
        Hang(bp, "Medicine Cabinet", ua, "cabinet", "back");
        Paint(bp, Hang(bp, "Zero Day Locker", ua, "medLocker", "wall"), MedWhite, "gloss paint");
        Hang(bp, "Galley Stove", ua, "stove", "back");
        Hang(bp, "Galley Coffee Station", ua, "coffee", "wall");
        Paint(bp, Hang(bp, "Zero Day Locker", ua, "messLocker", "wall"), "#5c646a", "worn paint");
        var table = bp.HangTurned("Mess Table", Holder(bp, ua, "table"), "table", "bottom", BuildingParts.RY90);
        _ = table;
        foreach (var d in new[] { "doorCabinB", "doorHead", "doorMed", "doorMess" })
            Door(bp, ua, d);
        Switch(bp, ua, "switchCrew", LampAt(bp, ua, "lampCabinB", "#ffd8a0"), LampAt(bp, ua, "lampHead"));
        Switch(bp, ua, "switchMed", LampAt(bp, ua, "lampMed", "#f4fbff"));
        Switch(bp, ua, "switchMess", LampAt(bp, ua, "lampMessL", "#ffe0b0"), LampAt(bp, ua, "lampMessR", "#ffe0b0"));
        // The turrets: two barbettes forward worked from ops, two aft worked from the mess.
        var pods = new[] { Hang(bp, "Zero Day Turret Barbette", uf, "podL", "hull"), Hang(bp, "Zero Day Turret Barbette", uf, "podR", "hull"), Hang(bp, "Zero Day Turret Barbette", ua, "podL", "hull"), Hang(bp, "Zero Day Turret Barbette", ua, "podR", "hull") };
        var rings = new int[pods.Length];
        var guns = pods.Select((pd, k) => Turret(bp, pd, out rings[k])).ToArray();
        // Each PDC's magazine: an Ammo Container against the room's side wall by its barbette, its box in the hull's void behind the
        // wall (ops forward, the mess aft), its feed laid up the wall, through it, the void and the hull into the barbette, to the ring's
        // Ammo in at the trunk's foot. Laid, not routed: the void and the barbette are solid to the router.
        for (var k = 0; k < rings.Length; k++)
        {
            var side = k % 2 == 0 ? -1 : 1;
            var mag = Hang(bp, "Ammo Container", k < 2 ? uf : ua, side < 0 ? "magL" : "magR", "-y");
            bp.Steps[mag]["config"] = MagazineConfig(side);
            bp.Feeds.Add((mag, rings[k], "Ammo out", "Ammo in", MagazineFeed(bp, mag, rings[k])));
        }
        var torpedoKeys = new int[2];
        foreach (var (deck, pair) in new[] { (uf, new[] { guns[0], guns[1] }), (ua, new[] { guns[2], guns[3] }) })
        {
            var console = Hang(bp, "Gunnery Console", deck, "gun", "bottom");
            var seat = Hang(bp, "Flight Couch", deck, "gunSeat", "bottom");
            var module = Hang(bp, "Turret Module", console, "module", "-x");
            bp.Wires.Add((seat, module));
            foreach (var g in pair)
                bp.Wires.Add((module, g));
            // Each gunner's torpedo key: an Input Reader on the module reading Space, wired through OR Gates under the lower deck's ceiling
            // to each bay's magazine (Karl, 2026-11-17/18: "connect the torpedo launch to the gunnery chair", both chairs). The mouse stays
            // the PDCs'; the torpedo room's buttons still fire a bay each.
            var key = Hang(bp, "Input Reader", module, "+x", "-x");
            bp.Steps[key]["config"] = new PyDict { ["setting"] = "jump" };
            bp.Wires.Add((seat, key));
            torpedoKeys[deck == uf ? 0 : 1] = key;
        }
        // Ops' key goes to a gate each side at the torpedo room's fore end; the mess's to one in the passage aft, then a gate each side at
        // the torpedo room's aft end. Each side's gates are wired to its magazine below.
        var relays = new[] { Hang(bp, "OR Gate", lf, "relayL", "+y"), Hang(bp, "OR Gate", lf, "relayR", "+y") };
        foreach (var r in relays)
            bp.Wires.Add((torpedoKeys[0], r));
        var aftRelay = Hang(bp, "OR Gate", la, "relayAft", "+y");
        bp.Wires.Add((torpedoKeys[1], aftRelay));
        var aftRelays = new[] { Hang(bp, "OR Gate", la, "relayAftL", "+y"), Hang(bp, "OR Gate", la, "relayAftR", "+y") };
        foreach (var r in aftRelays)
            bp.Wires.Add((aftRelay, r));
        // The shuttle's Docking Clamp on the cradle (holding from the start, its box round the pod) and the ladder up into it from the mess.
        var clamp = Hang(bp, "Docking Clamp Medium", spine, "shuttleClamp", "-y");
        bp.Steps[clamp]["config"] = ShuttleClampConfig();
        Hang(bp, "Zero Day Shuttle Ladder", ua, "shuttleLadder", "bottom");
        // The Limpet's Docking Clamp, upside down under the keel to port of the pod it holds there.
        var limpetClamp = bp.HangTurned("Docking Clamp Medium", Holder(bp, keel, "limpetClamp"), "limpetClamp", "-y", BuildingParts.Turn("+x", "-y"));
        bp.Steps[limpetClamp]["config"] = LimpetClampConfig();
        bp.HangTurned("Docking Hatch", Holder(bp, keel, "limpetHatch"), "limpetHatch", "base", BuildingParts.R0); // (opens with the Limpet's own while its clamp holds it)
        bp.HangTurned("Docking Hatch", Holder(bp, spine, "dockHatch"), "dockHatch", "base", BuildingParts.R0); // (opens with the shuttle's own while the clamp holds it)
        // The launch hatches over the torpedo ports: they swing open as a magazine fires (the port side's turned half round, its hinge outboard).
        bp.HangTurned("Zero Day Torpedo Hatch", bayR, "hatch", "hang", BuildingParts.R0);
        bp.HangTurned("Zero Day Torpedo Hatch", bayL, "hatch", "hang", BuildingParts.Turn("-x", "-y"));
        // The railgun along the spine, fired by the pilot; its slug box on the breech.
        var rail = Hang(bp, "Zero Day Railgun", spine, "railgun", "back");
        Hang(bp, "Ammo Box", rail, "ammo", "bottom");
        bp.Wires.Add((pilot, railModule));
        bp.Wires.Add((railModule, rail));
        // Flare launchers behind the bridge, on ops' flare button.
        foreach (var n in new[] { "flaresFL", "flaresFR", "flaresBL", "flaresBR" })
            bp.Wires.Add((flareButton, Hang(bp, "Zero Day Countermeasure Launcher", spine, n, "back")));
        // The radar: the Long Range Sensor by the mast wired into the Fire Director in the mess below it, so the middle mouse locks a contact in any
        // seat and the torpedoes take that lock at launch (ConstructRadar.CanLock; Karl, 2026-11-17). No guns are wired out of it.
        bp.Wires.Add((Hang(bp, "Long Range Sensor", spine, "sensor", "-y"), Hang(bp, "Fire Director", ua, "director", "-y")));
        var json = bp.Json();
        json["carry"] = Py.L(Kw(("blueprint", ShuttleName), ("at", Py.L(Cx, CradleY + ShuttleLift, ShuttleZ)), ("turn", 0)), // (built on its cradle, held by the clamp: ConstructCargo.BuildCarry)
            Kw(("blueprint", LimpetName), ("at", Py.L(Cx, LimpetBaseY, LimpetOriginZ)), ("turn", 0))); // (under the keel, its receiver on the Limpet shaft)
        // The torpedo magazines (a rack's shape is made by the game, so its steps go last, by socket name) and their torpedoes; each
        // bay's fire button lets one go.
        var steps = (List<object?>)json["steps"]!;
        var wires = (List<object?>)json["wires"]!;
        foreach (var (bay, fire, relay, aft) in new[] { (bayL, fireL, relays[0], aftRelays[0]), (bayR, fireR, relays[1], aftRelays[1]) })
        {
            steps.Add(Kw(("part", Magazine), ("on", bay), ("socket", "magazine"), ("by", "top")));
            var mag = steps.Count - 1;
            for (var i = 0; i < 6; i++)
                steps.Add(Kw(("part", Torpedo), ("on", mag), ("socket", $"lug{i}"), ("by", "lug")));
            wires.Add(new List<object?> { fire, mag });
            wires.Add(new List<object?> { relay, mag });
            wires.Add(new List<object?> { aft, mag });
        }
        return json;
    }

    public void Make(PartOutput o)
    {
        Walls.Clear();
        Floors.Clear();
        SockOwner.Clear();
        var structure = new List<Part>();
        structure.AddRange(SplitBands(Keel(), "Keel", 40.0)); // (the aft band, where the fuel leaves for the drive, keeps the keel's name and its tankage)
        structure.AddRange(SplitDeck(LowerFore(), "Lower Fore"));
        structure.AddRange(SplitDeck(LowerAft(), "Lower Aft"));
        structure.AddRange(SplitDeck(UpperFore(), "Upper Fore"));
        structure.AddRange(SplitDeck(UpperAft(), "Upper Aft"));
        structure.AddRange(SplitBands(Spine(), "Spine", 12.0)); // (the band over the bridge, where it sits on the upper deck)
        structure.AddRange([Bow(), SternPart(), Sponson(-1), Sponson(1)]);
        Connect(structure);
        Structure = PlaceOrder(structure, "Zero Day Keel");
        List<Part> parts =
        [
            .. structure, Barbette(), Collar(), Bell(),
            Jet("Zero Day Lift Jet", $"A lift jet under the Zero Day's keel: {LiftForce / 1e6:0.0} MN straight down.", LiftForce, 1.4),
            Jet("Zero Day Retro Jet", $"A retro jet in the Zero Day's cheek: {RetroForce / 1e6:0.0} MN forward, to slow it down.", RetroForce, 1.0),
            Leg(),
            Ladder("Zero Day Ladder", Ft1 - Ft0, "A ladder between the Zero Day's decks: F climbs it.", "bottom", [0, 0, -0.095], "-y"),
            Ladder("Zero Day Boarding Ladder", Ft0 - Ground, "The Zero Day's boarding ladder, from the ground up the shaft into the airlock: F climbs it.", "hull", [0, Ft0 - Ground - 0.125, -0.005], "+z"),
            Railgun(), Pdc(), Countermeasures(), Reactor(), FlightComputer(), Locker(), TorpedoHatch(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        o.Own(LOut);
        o.Own(BoardingOut);
        o.Own(POut);
        var (shuttle, shuttleOrder) = ShuttleStructure();
        var (limpet, limpetOrder) = LimpetStructure();
        foreach (var (p, folder) in parts.Select(x => (x, Out)).Append((ShuttleLadder(), Out)).Concat(ShuttleParts(shuttle)).Concat(LimpetParts(limpet)))
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            defs.Add(name, d);
            o.File($"{folder}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/Shuttle.json", PyJson.Dumps(ShuttleBlueprint(defs, shuttleOrder), 2) + "\n");
        o.File($"{BpOut}/Limpet.json", PyJson.Dumps(LimpetBlueprint(defs, limpetOrder), 2) + "\n");
        o.File($"{BpOut}/ZeroDayFrigate.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
        o.File($"{BpOut}/ZeroDayFrigateCrewed.json", PyJson.Dumps(Blueprint(defs, crewed: true), 2) + "\n");
    }
}
