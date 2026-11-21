using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Hatchet, the Raiders' wedge fighter (Karl, 2026-11-21: a picture of a flat crimson and cream wedge with a long glass canopy,
/// a gun turret on each wing tip, rocket pods, canted tail fins and a rear engine block; "make it and give it to the faction that
/// suits it"). The Raiders get it: they want speed, two guns and missiles above all (Crew/Factions/Raiders.json "needs"), and a
/// fighter that dives on a convoy and runs is their trade. Original, not a copy: a lofted fuselage about 16 m long with a delta wing
/// 13 m across, crimson over cream, grey panels, dark frame ribs (docs/sandbox/constructs/hatchet.md).
///
/// Inside: the pilot alone under the glass (the power cell against the cockpit's wall, room to stand behind the couch), a small cabin
/// behind with the boarding hatch and its ladder in the floor, suit lockers, the gunner's couch and console on the centreline, the
/// air, the battery, and the two drives' engines (a Drive Core XS each) against the stern's plate. The gunner works both wing-tip
/// turrets from a Gunnery Console; the pilot fires the two rocket pods (fixed
/// forward) through a Turret Module. Lift jets and leg pods under the belly and the wings set it down.
///
/// Built as the Zero Day's Shuttle is (TorpedoFrigateShuttle.cs, whose helpers this copies, and CourierParts.cs): a hull lofted
/// through stations, each piece drawn on its own and welded to what it touches (Connect, PlaceOrder), so a hit takes a piece:
/// the belly, a hull side each, the canopy, the cabin roof, the nose, the stern, the bulkhead and a wing each. Ship metres: x across
/// from the centreline, y up from the keel's line, z along the ship (forward is -z); P() moves them by half a cell into a part's frame.
/// </summary>
public sealed class WedgeFighterParts : IPartGenerator
{
    public string Name => "wedge_fighter_parts";

    private const string Out = "Constructs/Parts/Hatchet";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Hatchet fighter";
    public const string ShipName = "Hatchet Fighter";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: the picture's crimson over cream, grey panels, near-black frame ribs, orange running marks. Worn: these are
    // Raiders' craft, taken and kept flying.
    private const string Crimson = "#9b2733", Crimson2 = "#84202b", Cream = "#e2d8c0", Cream2 = "#cfc5ad", Grey = "#7b8086", Grey2 = "#5f646a",
        Rib = "#26282c", Frame = "#33363b", Dark = "#1c1e21", Orange = "#f08a24", Red = "#e0302a", Glow = "#ffd9a0", Glass = "#8fb4c4",
        Hazard = "#e8a020", Screen = "#7cd0e8", Console = "#2a2d32",
        Wall = "#c8c2b4", WallLow = "#84202b", Deck = "#55595e", Ceil = "#b0aea6";

    // ---- the plan (ship metres)

    private const double Ft = 0.7, Floor0 = 0.45, CeilY = 2.76, CeilTop = 2.86; // the floor's top and underside, the cabin's ceiling and its top
    private const double Tip = -8.6, NoseZ = -5.6, BulkZ = -1.6, AftZ = 4.9, Stern = 5.2; // the nose's tip, the cockpit's front wall, the bulkhead, the cabin's aft wall (the stern's mount plate), the stern
    private const double CockW = 0.8, CabW = 1.05, WallT = 0.08; // the cockpit's and the cabin's inner half widths, a wall's thickness
    private const double Sill = 2.3; // the cockpit's walls stop here, under the canopy's glass
    private const double EngX = 0.6; // the drives' axes either side of the centreline
    private static double EngY => Ft + ShipWorksParts.AxisOver(ShipWorksParts.XS) + 0.01; // a Drive Core XS's saddles 1 cm over the floor

    // The boarding shaft (port, just aft of the bulkhead), its belly hatch and the ladder on it (the Satchel's way).
    private static readonly Rect BellyHatch = new(-0.97, -1.25, 0.18, 0.0);
    private const string BellyHatchName = "Hatchet Belly Hatch", LadderName = "Hatchet Boarding Ladder";
    private static double HatchX => BellyHatch.X0 + 0.575;
    private static double HatchBottom => At(BellyHatch.Z1)[0].Y - 0.012;
    private static double HatchTop => HatchBottom + 0.08;
    private static double HatchTravel => Math.Min(HatchBottom - Ground - 0.005, CeilY - 0.03 - Ft);
    private static double LadderZ => BellyHatch.Z1 - 0.03;
    private static double BoardRise => Ft - (HatchTop - HatchTravel);

    // ---- flying it

    private const double DriveForce = 300000; // N a drive (two): under ShipWorks' 500 kN, but each asks for its engine (the Shuttle's way); a Drive Core XS serves 300 kN
    private const double LiftForce = 150000; // N a lift jet (four)
    private const double Burn = 2.5; // l/s a MN
    private const int BellyFuel = 900, WingFuel = 700; // litres
    private const string LegPod = "S";
    private const double LegY = 0.18; // every leg pod's mount (under the belly and on the wing pylons)
    private static double Ground => LegY - LegPodParts.ReachOf(LegPod);
    private static readonly (double X, double Z)[] BellyLegs = [(-0.62, -4.4), (0.62, -4.4)];
    private const double WingLegX = 3.0, WingLegZ = 2.4;
    private const double LiftX = 2.9, LiftFz = -0.6, LiftBz = 4.15;

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge)

    private static readonly (double, double)[] Mid = [(1.15, 0.3), (1.75, 0.48), (1.95, 1.25), (1.7, 2.55), (0.88, 3.22)];

    private static readonly Sec[] Hull =
    [
        new(Tip, [(0.22, 0.8), (0.38, 0.92), (0.45, 1.2), (0.36, 1.42), (0.2, 1.52)]), // (a blunt chisel tip: the dark intake mouth)
        new(-6.8, [(0.75, 0.5), (1.15, 0.62), (1.32, 1.2), (1.1, 1.85), (0.55, 2.2)]),
        new(NoseZ, [(1.0, 0.38), (1.5, 0.52), (1.72, 1.22), (1.45, 2.3), (0.68, 2.85)]),
        new(-2.0, Mid), new(3.4, Mid),
        new(Stern, [(1.05, 0.36), (1.65, 0.52), (1.85, 1.25), (1.64, 2.52), (0.9, 3.16)]),
    ];

    private static readonly (double Za, double Zb)[] Body = [(NoseZ, -2.0), (-2.0, 3.4), (3.4, AftZ)]; // the bands along the rooms

    // ---- the wing (starboard; the port one mirrors it): a flat delta, its leading edge swept from the nose to the tip pod

    private const double WingY = 1.25; // its middle plane
    private const double TipX = 6.0; // where the panels stop and the tip pod starts
    private const double PodX0 = 5.95, PodX1 = 6.75, PodZ0 = 1.4, PodZ1 = 4.8, PodY0 = 0.95, PodY1 = 1.75; // the tip pod
    private static double LE(double x) => -6.9 + (x - 1.3) * 1.86; // the leading edge's z at x
    private static double TE(double x) => 5.0 - (x - 1.8) * 0.13; // the trailing edge's z at x
    private static double Thick(double x) => 0.6 - 0.34 * Math.Clamp((x - 1.9) / 4.1, 0, 1);
    private static double WTop(double x) => WingY + Thick(x) / 2;
    private static double WBot(double x) => WingY - Thick(x) / 2;
    private const double RootX0 = 1.32, RootZ0 = -6.86, RootX1 = 1.85, RootZ1 = -2.0; // the root edge: inside the hull's skin
    private static double RootX(double z) => z >= RootZ1 ? RootX1 : RootX0 + (RootX1 - RootX0) * (z - RootZ0) / (RootZ1 - RootZ0);
    private const double WingRoot = 2.05; // the wing's solid starts here, against the hull side's root block
    private static readonly double[] WingBands = [2.5, 3.5, 4.5, 5.5]; // the rib lines across the wing (and the panel bands)
    private static readonly double[] Chord = [0, 0.18, 0.42, 0.68, 1.0]; // the panel cuts along the chord
    private const double RocketX = 4.0, RocketZ = 0.6;
    private const double TurretZ = 3.05;

    // ---- the hull's geometry (the Shuttle's and the Satchel's helpers)

    private readonly record struct Sec(double Z, (double H, double Y)[] Pts);

    private readonly record struct Rect(double X0, double Z0, double X1, double Z1);

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    private static double[] Sh(double[] q) => P(q[0], q[1], q[2]);

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

    private static void MustFit(string what, double z, double hx, double y)
    {
        var s = SkinHalf(z, y);
        if (s < hx + 0.06)
            throw new InvalidOperationException($"wedge_fighter_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
    }

    private static (double[] A, double[] B) Edge(int k, int s, double z)
    {
        var q = At(z);
        double[] V((double H, double Y) p, int side) => [side * p.H, p.Y, z];
        return k switch
        {
            0 => (V(q[0], -1), V(q[0], 1)),
            5 => (V(q[4], 1), V(q[4], -1)),
            _ => (V(q[k - 1], s), V(q[k], s)),
        };
    }

    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    private static double[] OnFacet(int k, int s, double za, double zb, double u, double v)
    {
        var (a0, b0) = Edge(k, s, za);
        var (a1, b1) = Edge(k, s, zb);
        return Lerp(Lerp(a0, b0, v), Lerp(a1, b1, v), u);
    }

    private static double[] FacetNormal(int k, int s, double za, double zb)
    {
        var c = OnFacet(k, s, za, zb, 0.5, 0.5);
        var n = Unit(Cross(Sub(OnFacet(k, s, za, zb, 0.5, 1), OnFacet(k, s, za, zb, 0.5, 0)), Sub(OnFacet(k, s, za, zb, 1, 0.5), OnFacet(k, s, za, zb, 0, 0.5))));
        double[] axis = [0, 1.4, c[2]];
        return Dot(n, Sub(c, axis)) < 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    private const double FacetStagger = 0.006, FacetTrim = 0.0015;

    /// <summary>The skin of facet k on side s from za to zb (odd facets' ends staggered 6 mm aft: z-fighting.md).</summary>
    private static void Facet(Part p, int k, int s, double za, double zb, string color, string finish = "painted plating", bool glass = false)
    {
        var dz = k % 2 == 1 ? FacetStagger : 0;
        double[] At2(double u, double v) { var q = OnFacet(k, s, za, zb, u, v); return Sh([q[0], q[1], q[2] + dz + (u < 0.5 ? FacetTrim : -FacetTrim)]); }
        Quad(p, At2(0, 0), At2(0, 1), At2(1, 1), At2(1, 0), color, finish, glass);
    }

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

    /// <summary>A chine rail along profile point i (1..4) on side s from za to zb.</summary>
    private static void Chine(Part p, int i, int s, double za, double zb, double w, double h, string color)
    {
        double[][] Ring(double z)
        {
            var q = At(z);
            double[] c = [s * q[i].H, q[i].Y, z], a = [s * q[i - 1].H, q[i - 1].Y, z], b = i == 4 ? [-s * q[4].H, q[4].Y, z] : [s * q[i + 1].H, q[i + 1].Y, z];
            var da = Unit(Sub(a, c));
            var db = Unit(Sub(b, c));
            var n = Unit([-(da[0] + db[0]), -(da[1] + db[1]), 0]);
            double[] Pt(double[] d1, double t1, double t2) => Sh([c[0] + d1[0] * t1 + n[0] * t2, c[1] + d1[1] * t1 + n[1] * t2, z]);
            return [Pt(da, w, 0.01), Pt(da, w * 0.45, h), Pt(db, w * 0.45, h), Pt(db, w, 0.01)];
        }
        var ra = Ring(za);
        var rb = Ring(zb);
        foreach (var q in ra)
            q[2] += FacetStagger / 2 + FacetTrim;
        foreach (var q in rb)
            q[2] += FacetStagger / 2 - FacetTrim;
        Prism(p, ra, rb, color, "plated steel");
    }

    /// <summary>A dark frame rib round facet k (side s) at z: the picture's ribs that wrap over the hull, w wide and d proud.</summary>
    private static void HullRib(Part p, int k, int s, double z, double w = 0.13, double d = 0.06)
    {
        double za = z - w / 2, zb = z + w / 2;
        var n = FacetNormal(k, s, za, zb);
        double[] Q(double u, double v, double dd) { var q = OnFacet(k, s, za, zb, u, v); return Sh([q[0] + n[0] * dd, q[1] + n[1] * dd, q[2] + (k % 2 == 1 ? 0.004 : 0)]); }
        // (each facet's segment stops 1 cm short of the facet's edges, so neighbouring segments meet at a corner without a shared face)
        Prism(p, [Q(0, 0.02, 0.008), Q(1, 0.02, 0.008), Q(1, 0.98, 0.008), Q(0, 0.98, 0.008)], [Q(0, 0.04, d), Q(1, 0.04, d), Q(1, 0.96, d), Q(0, 0.96, d)], Rib, "dirty steel");
    }

    /// <summary>Panels on a facet band: crimson, cream, grey, the pattern picked by facet and panel (both sides match).</summary>
    private static void Panels(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.08, double v1 = 0.92)
    {
        var n = Math.Max(1, (int)Math.Round((zb - za) / 1.8));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.12 / (zb - za), u1 = (double)(i + 1) / n - 0.12 / (zb - za);
            switch ((k * 7 + i * 3 + salt * 5) % 5)
            {
                case 0:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.012, 0.045, 0.03, Crimson2);
                    break;
                case 1:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.012, 0.04, 0.03, Grey);
                    break;
                case 2:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.012, 0.04, 0.03, Cream2);
                    break;
                case 3:
                    FStrip(p, k, s, za, zb, u0, u0 + Math.Min(0.3, (u1 - u0) * 0.25), (v0 + v1) / 2 - 0.05, (v0 + v1) / 2 + 0.05, 0.02, Orange, "painted plating");
                    break;
                default:
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.006, (u0 + u1) / 2 + 0.006, v0 + 0.08, v1 - 0.08, 0.018, Dark, "dirty steel");
                    break;
            }
        }
    }

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

    private static void Quad(Part p, double[] a, double[] b, double[] c, double[] d, string color, string finish = "painted plating", bool glass = false)
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

    /// <summary>Hidden boxes for the void from xin (0: across the middle) out to the skin less a margin, y and z bands.</summary>
    private static void Void(Part p, double za, double zb, double ya, double yb, double xin)
    {
        var xo = Math.Floor((new[] { SkinHalf(za, ya), SkinHalf(za, yb), SkinHalf(zb, ya), SkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        za += 0.004;
        zb -= 0.004;
        if (xin <= 0)
            Hidden(p, -xo, ya, za, xo, yb, zb);
        else
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Math.Min(s * xin, s * xo), ya, za, Math.Max(s * xin, s * xo), yb, zb);
    }

    private static void VoidSide(Part p, int s, double za, double zb, double ya, double yb, double xin)
    {
        var xo = Math.Floor((new[] { SkinHalf(za, ya), SkinHalf(za, yb), SkinHalf(zb, ya), SkinHalf(zb, yb) }.Min() - 0.06) * 100) / 100;
        if (xo < xin + 0.08)
            return;
        Hidden(p, Math.Min(s * xin, s * xo), ya, za + 0.004, Math.Max(s * xin, s * xo), yb, zb - 0.004);
    }

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

    /// <summary>A cap across the hull at z: the profile's ten points as one plate.</summary>
    private static void Cap(Part p, double z, string color, string finish)
    {
        var q = At(z);
        var pts = new List<double[]>();
        for (var i = 0; i < 5; i++)
            pts.Add(P(-q[i].H, q[i].Y, z));
        for (var i = 4; i >= 0; i--)
            pts.Add(P(q[i].H, q[i].Y, z));
        Plate(p, [.. pts], color, finish);
    }

    /// <summary>The height of the upper hull's skin (facet 4) at half width x and station z.</summary>
    private static double UpperY(double z, double x)
    {
        var q = At(z);
        return q[3].Y + (q[4].Y - q[3].Y) * Math.Clamp((q[3].H - x) / (q[3].H - q[4].H), 0, 1);
    }

    /// <summary>A pad for an attitude jet: a square solid standing proud off the surface point, sunk 11 cm into the hull; its outer face takes the socket.</summary>
    private static void RcsPad(Part p, string name, double x, double y, double z, string dir, double proud, double w = 0.4)
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

    private static Part Big(string name, string desc, string role = "frame", string color = Crimson) =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);

    // ---- the pieces

    /// <summary>The belly: the floor of the cockpit and the cabin (with the boarding shaft), the keel and the lower chines, the
    /// fuel in the void under the floor, the front leg pods' mounts and the cockpit's and the cabin's floor fittings.</summary>
    private static Part Belly()
    {
        var p = Big("Hatchet Belly", "The Hatchet's belly: the cockpit's and the cabin's floor, the keel, the boarding shaft and the fuel tank under the floor, and the front leg pods' mounts.", "fueltank", Grey2);
        var ckW = Math.Floor((Math.Min(SkinHalf(NoseZ, Floor0), SkinHalf(NoseZ, Ft)) - 0.07) * 100) / 100;
        var cabW = Math.Floor((new[] { SkinHalf(AftZ, Floor0), SkinHalf(AftZ, Ft), SkinHalf(BulkZ, Floor0) }.Min() - 0.07) * 100) / 100;
        ckW = Math.Min(ckW, CockW + WallT);
        cabW = Math.Min(cabW, CabW + WallT);
        foreach (var r in Minus([new Rect(-ckW, NoseZ, ckW, BulkZ), new Rect(-cabW, BulkZ, cabW, AftZ)], BellyHatch))
            Solid(p, r.X0, Floor0, r.Z0, r.X1, Ft, r.Z1, Deck, "painted deck plate");
        // The keel's flat (facet 0) with the shaft's mouth cut through it, the lower chines (facet 1) with their panels.
        foreach (var (za, zb) in Body)
        {
            if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                var q0 = At(za);
                var q1 = At(zb);
                double y0 = q0[0].Y, y1 = q1[0].Y, h0 = q0[0].H, h1 = q1[0].H;
                var a = BellyHatch;
                double Y(double z) => y0 + (y1 - y0) * (z - za) / (zb - za);
                double Hh(double z) => h0 + (h1 - h0) * (z - za) / (zb - za);
                Quad(p, P(-h0, y0, za), P(h0, y0, za), P(Hh(a.Z0), Y(a.Z0), a.Z0), P(-Hh(a.Z0), Y(a.Z0), a.Z0), Grey2, "plated steel");
                Quad(p, P(-Hh(a.Z1), Y(a.Z1), a.Z1), P(Hh(a.Z1), Y(a.Z1), a.Z1), P(h1, y1, zb), P(-h1, y1, zb), Grey2, "plated steel");
                Quad(p, P(-Hh(a.Z0), Y(a.Z0), a.Z0), P(a.X0, Y(a.Z0), a.Z0), P(a.X0, Y(a.Z1), a.Z1), P(-Hh(a.Z1), Y(a.Z1), a.Z1), Grey2, "plated steel");
                Quad(p, P(a.X1, Y(a.Z0), a.Z0), P(Hh(a.Z0), Y(a.Z0), a.Z0), P(Hh(a.Z1), Y(a.Z1), a.Z1), P(a.X1, Y(a.Z1), a.Z1), Grey2, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Grey2, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Grey, "worn paint");
                Panels(p, 1, s, za, zb, 1);
                Chine(p, 1, s, za, zb, 0.1, 0.05, Rib);
            }
        }
        // The void under the floor (the fuel), round the shaft.
        var shaft = new Rect(BellyHatch.X0 - 0.03, BellyHatch.Z0 - 0.03, BellyHatch.X1 + 0.03, BellyHatch.Z1 + 0.03);
        foreach (var (za, zb) in Body)
        {
            var y0 = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.03;
            var xo = new[] { SkinHalf(za, y0), SkinHalf(zb, y0), SkinHalf(za, Floor0), SkinHalf(zb, Floor0) }.Min() - 0.06;
            foreach (var r in Minus([new Rect(-xo, za + 0.004, xo, zb - 0.004)], shaft))
                Hidden(p, r.X0, y0, r.Z0, r.X1, Floor0, r.Z1);
        }
        // The shaft's lining down to the keel, a hazard lip round it on the floor (open at its aft edge, where the ladder stands).
        var hy = At(BellyHatch.Z1)[0].Y;
        foreach (var (x0, z0, x1, z1) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1), (shaft.X0, shaft.Z0 + 0.02, shaft.X0, shaft.Z1 - 0.02), (shaft.X1, shaft.Z0 + 0.02, shaft.X1, shaft.Z1 - 0.02) })
            Plate(p, [P(x0, hy + 0.02, z0), P(x1, hy + 0.02, z1), P(x1, Floor0 - 0.01, z1), P(x0, Floor0 - 0.01, z0)], Frame, "dirty steel");
        var hz = BellyHatch;
        Detail(p, hz.X0 - 0.08, Ft, hz.Z0 - 0.08, hz.X1 + 0.08, Ft + 0.012, hz.Z0, Hazard, "hazard paint");
        Detail(p, hz.X0 - 0.08, Ft, hz.Z0, hz.X0, Ft + 0.012, hz.Z1, Hazard, "hazard paint");
        Detail(p, hz.X1, Ft, hz.Z0, hz.X1 + 0.08, Ft + 0.012, hz.Z1, Hazard, "hazard paint");
        // Rubber runners down the cabin (starboard of the shaft) and the cockpit.
        Detail(p, 0.32, Ft, BulkZ + 0.05, 0.8, Ft + 0.012, 3.7, Dark, "rubber matting");
        Detail(p, -0.35, Ft, NoseZ + 0.08, 0.35, Ft + 0.012, BulkZ - 0.05, Dark, "rubber matting");
        // The belly hatch hangs from the floor's edge at the shaft's aft side.
        Sock(p, "boarding", HatchX, Ft - 0.125, BellyHatch.Z1, "-z");
        // The front leg pods' mounts under the keel's flat.
        foreach (var (i, (x, z)) in BellyLegs.Select((l, i) => (i, l)))
        {
            var ky = At(z)[0].Y;
            Solid(p, x - 0.3, LegY, z - 0.4, x + 0.3, ky + 0.11, z + 0.4, Frame, "dirty steel");
            Sock(p, $"leg{i}", x, LegY, z, "-y");
        }
        // The cockpit: the pilot, the flight computer ahead of the pilot and the rocket pods' Turret Module beside it, the power cell on
        // the starboard wall and floor behind the couch to stand on (a couch fills the cockpit's width: a second one there shut the pilot
        // off). The cabin: lockers either side aft of the hatch, the gunner's console and couch on the centreline behind them (both used
        // from the gap between the lockers), the air and the battery along the walls.
        Sock(p, "pilot", 0, Ft, -4.72, "+y");
        Sock(p, "computer", 0, Ft, -5.3, "+y");
        Sock(p, "rocketModule", 0.58, Ft, -5.3, "+y");
        Sock(p, "gunConsole", 0, Ft, 1.95, "+y");
        Sock(p, "gunner", 0, Ft, 2.7, "+y");
        Sock(p, "lockerL", -(CabW - 0.3), Ft, 1.0, "+y");
        Sock(p, "lifeSupport", -(CabW - 0.3), Ft, 2.75, "+y"); // (beside the gunner's couch)
        Sock(p, "lockerR", CabW - 0.3, Ft, 0.98, "+y"); // (aft of the belly hatch: the way forward past it runs along this wall)
        Sock(p, "battery", CabW - 0.17, Ft, 1.68, "+y");
        Sock(p, "power", CockW - 0.33, Ft, -3.2, "+y"); // (under the canopy's sill: 1 m tall)
        // (the wing turrets' Turret Module on the floor between the engines: a wire reaches 8 m, the console is too far from the
        // wing tips, and a seat aims one module, so one module takes both guns, about 6.6 m from each)
        Sock(p, "gunModule", 0, Ft, 3.35, "+y");
        foreach (var z in new[] { NoseZ, BulkZ })
            MustFit("cockpit floor", z, ckW, Ft);
        foreach (var z in new[] { BulkZ, AftZ })
            MustFit("cabin floor", z, cabW, Ft);
        p.Set(("fuelTank", Kw(("capacity", BellyFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(0.6, 0.55, AftZ).Cast<object>()], "+z", "fuel"))));
        return p.Stats(1700, 6500, 30, 12.0);
    }

    /// <summary>A hull side: the beam's facets and panels, the rooms' side wall, the void between, the wing's root block and the
    /// side vent; the lamps and switches on the wall.</summary>
    private static Part Side(int s)
    {
        var n = s < 0 ? "Port" : "Starboard";
        var p = Big($"Hatchet Hull {n}", $"The Hatchet's {n.ToLowerInvariant()} side: the beam's armour, the side wall of the cockpit and the cabin, the {n.ToLowerInvariant()} wing's root and the vent behind the canopy.");
        foreach (var (za, zb) in Body)
        {
            Facet(p, 2, s, za, zb, Cream, "worn paint");
            Facet(p, 3, s, za, zb, Crimson, "worn paint");
            Panels(p, 2, s, za, zb, 2, 0.1, 0.55);
            if (za != -2.0) // (the middle band carries the vent)
                Panels(p, 3, s, za, zb, 3);
            Chine(p, 2, s, za, zb, 0.12, 0.06, Rib);
            FStrip(p, 3, s, za, zb, 0.04, 0.96, 0.06, 0.1, 0.016, Orange, "painted plating");
        }
        foreach (var z in new[] { -4.4, BulkZ - 0.09, 2.0, 3.9 })
            foreach (var k in new[] { 2, 3 })
                HullRib(p, k, s, z);
        // The vent behind the canopy (the picture's black grille): a dark recess on the upper facet with slats.
        Slab(p, 3, s, -1.2, 1.4, 0.04, 0.96, 0.25, 0.85, 0.008, 0.02, 0.01, Dark, "soot");
        for (var i = 0; i < 9; i++)
        {
            var u = 0.1 + i * 0.1;
            FStrip(p, 3, s, -1.2, 1.4, u - 0.012, u + 0.012, 0.28, 0.82, 0.04, Grey2, "dirty steel");
        }
        // The side walls: the cockpit's in from the cabin's, a step between them at the bulkhead.
        double xc = s * CockW, xb = s * CabW;
        Solid(p, Math.Min(xc, xc + s * WallT), Ft, NoseZ + 0.004, Math.Max(xc, xc + s * WallT), Sill, BulkZ - WallT, Wall, "painted plating");
        Solid(p, Math.Min(xc, xb + s * WallT), Ft, BulkZ - WallT, Math.Max(xc, xb + s * WallT), Sill, BulkZ, Wall, "painted plating");
        Solid(p, Math.Min(xb, xb + s * WallT), Sill, BulkZ - WallT, Math.Max(xb, xb + s * WallT), CeilY, BulkZ, Wall, "painted plating");
        Solid(p, Math.Min(xb, xb + s * WallT), Ft, BulkZ, Math.Max(xb, xb + s * WallT), CeilY, AftZ - 0.004, Wall, "painted plating");
        Detail(p, Math.Min(xb, xb - s * 0.012), Ft + 0.003, BulkZ + 0.05, Math.Max(xb, xb - s * 0.012), Ft + 0.7, AftZ - 0.05, WallLow, "painted plating");
        Detail(p, Math.Min(xc, xc - s * 0.012), Ft + 0.003, NoseZ + 0.05, Math.Max(xc, xc - s * 0.012), Ft + 0.7, BulkZ - 0.13, WallLow, "painted plating");
        // The cockpit's side screens, a strip light along the cabin under the ceiling.
        Detail(p, Math.Min(xc, xc - s * 0.016), Ft + 0.95, -5.0, Math.Max(xc, xc - s * 0.016), Ft + 1.35, -4.2, "#101830", glow: true);
        Detail(p, Math.Min(xc, xc - s * 0.02), Ft + 1.0, -4.95, Math.Max(xc, xc - s * 0.02), Ft + 1.3, -4.25, Screen, glow: true);
        Detail(p, Math.Min(xb, xb - s * 0.02), CeilY - 0.12, BulkZ + 0.2, Math.Max(xb, xb - s * 0.02), CeilY - 0.08, AftZ - 0.25, "#ffe6c8", glow: true);
        // The void between the walls and the skin.
        foreach (var (za, zb) in Body)
        {
            if (za < BulkZ && zb > BulkZ)
            {
                VoidSide(p, s, za, BulkZ - WallT, Ft, Sill, CockW + WallT);
                VoidSide(p, s, BulkZ, zb, Ft, CeilY, CabW + WallT);
            }
            else
                VoidSide(p, s, za, zb, Ft, za < BulkZ ? Sill : CeilY, za < BulkZ ? CockW + WallT : CabW + WallT);
        }
        // The wing's root block: a hardpoint through the skin at the beam, which the wing's spar boxes weld to.
        Solid(p, Math.Min(s * 1.75, s * WingRoot), 1.0, RootZ1, Math.Max(s * 1.75, s * WingRoot), 1.5, 4.6, Frame, "dirty steel");
        // Lamps and switches.
        Sock(p, "lampCockpit", xc, Sill - 0.35, -2.2, s < 0 ? "+x" : "-x");
        Sock(p, "lampCabinF", xb, CeilY - 0.4, -0.6, s < 0 ? "+x" : "-x");
        Sock(p, "lampCabinB", xb, CeilY - 0.4, 3.65, s < 0 ? "+x" : "-x");
        Sock(p, "switch", xc, Ft + 1.1, -1.95, s < 0 ? "+x" : "-x");
        Sock(p, "switchCabin", xb, Ft + 1.3, -0.25, s < 0 ? "+x" : "-x");
        foreach (var (z, xh, top) in new[] { (NoseZ, CockW + WallT, Sill), (BulkZ, CockW + WallT, Sill), (BulkZ, CabW + WallT, CeilY), (AftZ, CabW + WallT, CeilY) })
        {
            MustFit("side wall", z, xh, Ft);
            MustFit("side wall", z, xh, top);
        }
        return p.Stats(1100, 6000, 30, 12.0);
    }

    /// <summary>The canopy: the long glass over the cockpit, its dark frame bars, the sills under the glass and the hidden boxes
    /// that keep people in.</summary>
    private static Part Canopy()
    {
        var p = Big("Hatchet Canopy", "The Hatchet's canopy: the long glass over the pilot in its dark frame, from the nose to the cabin.", color: Rib);
        var (za, zb) = (NoseZ, BulkZ);
        foreach (var (a, b) in new[] { (NoseZ, -2.0), (-2.0, BulkZ) })
        {
            Facet(p, 5, 1, a, b, Glass, "glass", glass: true);
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 4, s, a, b, Glass, "glass", glass: true);
                Chine(p, 3, s, a, b, 0.09, 0.06, Rib);
                Chine(p, 4, s, a, b, 0.07, 0.05, Rib);
            }
        }
        // The frame bars across the glass (the canopy's ribs), the last one heavier where the cabin roof starts.
        foreach (var (z, w) in new[] { (-4.4, 0.08), (-3.1, 0.08), (BulkZ - 0.09, 0.16) })
        {
            var (a, b) = z < -2.0 ? (NoseZ, -2.0) : (-2.0, BulkZ);
            foreach (var s in new[] { -1, 1 })
                FrameBar(p, 4, s, a, b, z, w);
            FrameBar(p, 5, 1, a, b, z, w);
        }
        // The sills: a dark plate from the cockpit wall's top out to the skin each side, under the glass.
        foreach (var s in new[] { -1, 1 })
            foreach (var (a, b) in new[] { (NoseZ, -2.0), (-2.0, BulkZ) })
                Quad(p, P(s * (CockW + WallT), Sill + 0.004, a + 0.01), P(s * (SkinHalf(a, Sill) - 0.015), Sill + 0.004, a + 0.01), P(s * (SkinHalf(b, Sill) - 0.015), Sill + 0.004, b - 0.01), P(s * (CockW + WallT), Sill + 0.004, b - 0.01), Console, "plastic");
        // What keeps people in: the sills' boxes on the walls' tops and a roof box under the glass's peak.
        var roofY = Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.07;
        var roofX = Math.Min(At(za)[4].H, At(zb)[4].H) - 0.06;
        var sideX = Math.Min(SkinHalf(za, Sill + 0.25), SkinHalf(zb, Sill + 0.25)) - 0.06;
        foreach (var s in new[] { -1, 1 })
            Hidden(p, s * CockW, Sill, za + 0.004, s * sideX, Sill + 0.25, zb - WallT - 0.004);
        Hidden(p, -roofX, roofY - 0.06, za + 0.004, roofX, roofY, zb - 0.004);
        MustFit("canopy roof", za, roofX, roofY);
        return p.Stats(450, 2500, 10, 10.0);
    }

    /// <summary>A dark frame bar across facet k at z, w wide, standing a few centimetres off the glass.</summary>
    private static void FrameBar(Part p, int k, int s, double za, double zb, double z, double w)
    {
        var n = FacetNormal(k, s, za, zb);
        double u0 = (z - w / 2 - za) / (zb - za), u1 = (z + w / 2 - za) / (zb - za);
        double[] Q(double u, double v, double d) { var q = OnFacet(k, s, za, zb, u, v); return Sh([q[0] + n[0] * d, q[1] + n[1] * d, q[2]]); }
        Prism(p, [Q(u0, 0.02, 0.01), Q(u1, 0.02, 0.01), Q(u1, 0.98, 0.01), Q(u0, 0.98, 0.01)], [Q(u0 + 0.004, 0.03, 0.05), Q(u1 - 0.004, 0.03, 0.05), Q(u1 - 0.004, 0.97, 0.05), Q(u0 + 0.004, 0.97, 0.05)], Rib, "dirty steel");
    }

    /// <summary>The cabin roof: the upper facets and the top from the canopy to the stern, the ceiling, the dorsal fins and the antenna.</summary>
    private static Part Roof()
    {
        var p = Big("Hatchet Roof", "The Hatchet's roof: the upper armour over the cabin, its ceiling, the two canted dorsal fins and the antenna.");
        foreach (var (za, zb) in new[] { (BulkZ, 3.4), (3.4, AftZ) })
        {
            Facet(p, 5, 1, za, zb, Cream, "worn paint");
            Slab(p, 5, 1, za, zb, 0.06, 0.94, 0.08, 0.3, 0.012, 0.045, 0.03, Grey);
            Slab(p, 5, 1, za, zb, 0.06, 0.94, 0.7, 0.92, 0.012, 0.045, 0.03, Grey);
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 4, s, za, zb, Crimson, "worn paint");
                Panels(p, 4, s, za, zb, 4);
                Chine(p, 3, s, za, zb, 0.09, 0.05, Rib);
                Chine(p, 4, s, za, zb, 0.07, 0.04, Rib);
            }
        }
        foreach (var z in new[] { 2.0, 3.9 })
        {
            foreach (var s in new[] { -1, 1 })
                HullRib(p, 4, s, z);
            HullRib(p, 5, 1, z);
        }
        // The ceiling and the void over it.
        Solid(p, -(CabW + WallT), CeilY, BulkZ, CabW + WallT, CeilTop, AftZ - 0.004, Ceil, "painted plating");
        Detail(p, -0.2, CeilY - 0.012, BulkZ + 0.3, 0.2, CeilY, AftZ - 0.4, "#fff0d8", glow: true);
        foreach (var (za, zb) in new[] { (BulkZ, 3.4), (3.4, AftZ) })
            Void(p, za, zb, CeilTop, Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.03, 0);
        // Two canted dorsal fins aft, raked back, a red light at each tip; a whip antenna between.
        double Top(double z) => At(z)[4].Y;
        foreach (var s in new[] { -1, 1 })
        {
            const double rootX = 0.6, h = 1.4, cant = 0.33; // (x out per metre up)
            double y0 = Top(4.0) + 0.004, y1 = y0 + h, x1 = rootX + h * cant;
            double[][] Face(double dx) => [P(s * (rootX + dx), y0, 2.3), P(s * (rootX + dx), y0, AftZ - 0.05), P(s * (x1 + dx), y1, 5.6), P(s * (x1 + dx), y1, 3.7)];
            Prism(p, Face(-0.04), Face(0.04), Crimson, "worn paint");
            Prism(p, [P(s * (rootX + 0.02), y0, 2.25), P(s * (rootX + 0.02), y0, 2.6), P(s * (x1 + 0.02), y1 + 0.01, 3.95), P(s * (x1 + 0.02), y1 + 0.01, 3.65)], [P(s * (rootX + 0.06), y0, 2.25), P(s * (rootX + 0.06), y0, 2.6), P(s * (x1 + 0.06), y1 + 0.01, 3.95), P(s * (x1 + 0.06), y1 + 0.01, 3.65)], Rib, "dirty steel");
            Detail(p, s * x1 - 0.05, y1 + 0.004, 4.5, s * x1 + 0.05, y1 + 0.08, 4.7, Red, glow: true);
            Hidden(p, s * (rootX - 0.06), Top(4.0) - 0.05, 2.7, s * (rootX + 0.06), Top(4.0) + 0.4, 4.8);
        }
        Detail(p, -0.015, Top(0.5) + 0.004, 0.5, 0.015, Top(0.5) + 1.0, 0.53, Frame, "steel");
        Detail(p, -0.025, Top(0.5) + 1.0, 0.495, 0.025, Top(0.5) + 1.05, 0.535, Red, glow: true);
        MustFit("ceiling", BulkZ, CabW + WallT, CeilTop);
        MustFit("ceiling", AftZ, CabW + WallT, CeilTop);
        return p.Stats(900, 5500, 25, 12.0);
    }

    /// <summary>The nose: the chisel bow from its tip to the cockpit's front wall, the canopy's glass running forward over the
    /// instrument hood, the dark mouth at the tip and the bow's attitude jets.</summary>
    private static Part Nose()
    {
        var p = Big("Hatchet Nose", "The Hatchet's nose: the chisel bow, the forward glass over the instrument hood, the cockpit's front wall and the bow's attitude jets.");
        foreach (var (za, zb, glass) in new[] { (Tip, -6.8, false), (-6.8, NoseZ, true) })
        {
            Facet(p, 0, 1, za, zb, Grey2, "plated steel");
            if (glass)
                Facet(p, 5, 1, za, zb, Glass, "glass", glass: true);
            else
                Facet(p, 5, 1, za, zb, Cream, "worn paint");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Grey, "worn paint");
                Facet(p, 2, s, za, zb, Cream, "worn paint");
                Facet(p, 3, s, za, zb, Crimson, "worn paint");
                if (glass)
                    Facet(p, 4, s, za, zb, Glass, "glass", glass: true);
                else
                    Facet(p, 4, s, za, zb, Crimson, "worn paint");
                Chine(p, 2, s, za, zb, 0.1, 0.05, Rib);
                Chine(p, 3, s, za, zb, glass ? 0.09 : 0.08, glass ? 0.06 : 0.05, Rib);
                if (glass)
                    Chine(p, 4, s, za, zb, 0.07, 0.05, Rib);
                Slab(p, 2, s, za, zb, 0.08, 0.92, 0.15, 0.85, 0.012, 0.04, 0.03, za < -7 ? Grey : Cream2);
            }
            if (!glass)
                Slab(p, 5, 1, za, zb, 0.1, 0.9, 0.2, 0.8, 0.012, 0.045, 0.03, Grey);
        }
        foreach (var s in new[] { -1, 1 })
            FrameBar(p, 4, s, -6.8, NoseZ, -6.75, 0.1);
        FrameBar(p, 5, 1, -6.8, NoseZ, -6.75, 0.1);
        // The tip: a dark mouth in the cap, its lip.
        Cap(p, Tip, Rib, "dirty steel");
        Detail(p, -0.3, 0.98, Tip - 0.03, 0.3, 1.18, Tip - 0.012, Dark, "soot");
        Detail(p, -0.24, 1.3, Tip - 0.026, 0.24, 1.36, Tip - 0.012, Red, glow: true);
        // The instrument hood under the forward glass: a dark plate across, the void under it.
        {
            const double hy = 1.95;
            Quad(p, P(-(SkinHalf(-6.75, hy) - 0.02), hy, -6.75), P(SkinHalf(-6.75, hy) - 0.02, hy, -6.75), P(SkinHalf(NoseZ - 0.1, hy) - 0.02, hy, NoseZ - 0.1), P(-(SkinHalf(NoseZ - 0.1, hy) - 0.02), hy, NoseZ - 0.1), Console, "plastic");
            for (var i = 0; i < 4; i++)
                Detail(p, -0.5 + i * 0.28, hy + 0.025, -6.1, -0.32 + i * 0.28, hy + 0.04, -5.8, i % 2 == 0 ? Screen : "#60e090", glow: true);
        }
        // The cockpit's front wall.
        Solid(p, -(CockW + WallT), Floor0, NoseZ - 0.1, CockW + WallT, 2.78, NoseZ, Wall, "painted plating");
        Detail(p, -0.6, Ft + 0.95, NoseZ - 0.002, 0.6, Ft + 1.2, NoseZ + 0.012, Console, "plastic");
        // The void ahead of it.
        foreach (var (za, zb) in new[] { (-8.2, -6.8), (-6.8, NoseZ - 0.1) })
        {
            var ya = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.03;
            var yb = Math.Min(Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.03, za > -7 ? 1.92 : 9);
            Void(p, za, zb, ya, yb, 0);
        }
        // The bow's attitude jets: down, out each way and up (on the hood's top, ahead of the glass).
        var zj = -7.5;
        var q = At(zj);
        RcsPad(p, "rcsDownN", 0, q[0].Y, zj, "-y", 0.05);
        RcsPad(p, "rcsUpN", 0, q[4].Y, zj, "+y", 0.05);
        RcsPad(p, "rcsOutNL", -q[2].H, q[2].Y, zj, "-x", 0.05, 0.3);
        RcsPad(p, "rcsOutNR", q[2].H, q[2].Y, zj, "+x", 0.05, 0.3);
        return p.Stats(800, 6000, 35, 12.0);
    }

    /// <summary>The stern: the last band of the hull, the cabin's aft wall as the drives' mount plate (their engines bolt to its
    /// inner face, in the cabin), the engine block's cowling over the two drives and the tail lights.</summary>
    private static Part SternPart()
    {
        var p = Big("Hatchet Stern", "The Hatchet's stern: the cabin's aft wall, the armoured plate its two drives bolt to outside with their engines inside, and the engine block's cowling.", "frame", Grey2);
        var (za, zb) = (AftZ, Stern);
        Facet(p, 0, 1, za, zb, Grey2, "plated steel");
        Facet(p, 5, 1, za, zb, Cream, "worn paint");
        foreach (var s in new[] { -1, 1 })
        {
            Facet(p, 1, s, za, zb, Grey, "worn paint");
            Facet(p, 2, s, za, zb, Cream, "worn paint");
            Facet(p, 3, s, za, zb, Crimson, "worn paint");
            Facet(p, 4, s, za, zb, Crimson, "worn paint");
        }
        Cap(p, Stern, Grey2, "plated steel");
        // The plate: the cabin's end wall, as thick as the band.
        Solid(p, -(CabW + WallT), Floor0, AftZ, CabW + WallT, CeilTop, Stern - 0.004, Wall, "painted plating");
        Detail(p, -CabW + 0.05, Ft + 0.003, AftZ - 0.012, -EngX - 0.44, Ft + 0.7, AftZ, WallLow, "painted plating");
        Detail(p, EngX + 0.44, Ft + 0.003, AftZ - 0.012, CabW - 0.05, Ft + 0.7, AftZ, WallLow, "painted plating");
        Detail(p, -EngX + 0.44, Ft + 0.003, AftZ - 0.012, EngX - 0.44, Ft + 0.7, AftZ, WallLow, "painted plating");
        var ya = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.03;
        var yb = Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.03;
        Void(p, za, zb - 0.05, ya, Floor0, 0);
        Void(p, za, zb - 0.05, CeilTop, yb, 0);
        Void(p, za, zb - 0.05, Floor0, CeilTop, CabW + WallT);
        // The engine block's cowling over the drives: a raked hood with grilles, cream and grey, its hidden box.
        double[][] Hood(double z, double inset) => [P(-1.3 + inset, 1.66, z), P(1.3 - inset, 1.66, z), P(1.1 - inset, 2.38 - inset * 0.5, z), P(-1.1 + inset, 2.38 - inset * 0.5, z)];
        Prism(p, Hood(Stern + 0.004, 0), Hood(6.5, 0), Cream2, "worn paint");
        Prism(p, Hood(6.504, 0), Hood(7.0, 0.25), Grey, "worn paint");
        foreach (var s in new[] { -1, 1 })
        {
            Plate(p, [P(s * 0.25, 2.39, 5.5), P(s * 1.0, 2.39, 5.5), P(s * 1.0, 2.39, 6.3), P(s * 0.25, 2.39, 6.3)], Dark, "soot");
            for (var i = 0; i < 6; i++)
            {
                var z = 5.6 + i * 0.12;
                Detail(p, s * 0.27, 2.405, z, s * 0.98, 2.43, z + 0.04, Grey2, "dirty steel");
            }
            Detail(p, s * 1.2 - 0.12, 1.8, 7.0 - 0.27, s * 1.2 + 0.12, 1.9, 7.0 - 0.25, Red, glow: true);
        }
        Hidden(p, -1.25, 1.67, Stern, 1.25, 2.33, 6.5);
        // The drives' ring plates on the stern, hazard round them.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, s * EngX - 0.42, EngY - 0.42, Stern - 0.04, s * EngX + 0.42, EngY + 0.42, Stern + 0.06, Frame, "dirty steel");
            var nm = s < 0 ? "L" : "R";
            Sock(p, $"drive{nm}", s * EngX, EngY, Stern + 0.06, "+z");
            Sock(p, $"engine{nm}", s * EngX, EngY, AftZ, "-z");
        }
        // The stern's attitude jets: up and down either side of the block.
        foreach (var s in new[] { -1, 1 })
        {
            var nm = s < 0 ? "L" : "R";
            const double zj = Stern - 0.15; // (inside the stern's own band: its pads may not reach into the neighbours' voids)
            var q = At(zj);
            RcsPad(p, $"rcsUpS{nm}", s * 1.3, UpperY(zj, 1.3), zj, "+y", 0.06, 0.26);
            RcsPad(p, $"rcsDownS{nm}", s * 0.8, q[0].Y, zj, "-y", 0.05, 0.26);
        }
        Works.Mount(p);
        return p.Stats(900, 7000, 40, 10.0);
    }

    private const double DoorHalf = 0.55; // (1.1 m: a 0.9 m door let a body through only along a 10 cm band)

    /// <summary>The bulkhead between the cockpit and the cabin: a frame open in the middle, up to the canopy (no lintel: the sill is too low to walk under).</summary>
    private static Part Bulkhead()
    {
        var p = Big("Hatchet Bulkhead", "The frame between the Hatchet's cockpit and its cabin, open in the middle.", color: Wall);
        foreach (var s in new[] { -1, 1 })
            Solid(p, Math.Min(s * DoorHalf, s * CockW), Ft, BulkZ - WallT, Math.Max(s * DoorHalf, s * CockW), Sill, BulkZ, Wall, "painted plating");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Math.Min(s * DoorHalf, s * (DoorHalf + 0.07)), Ft, BulkZ - WallT - 0.02, Math.Max(s * DoorHalf, s * (DoorHalf + 0.07)), Sill - 0.01, BulkZ + 0.02, Rib, "painted steel");
        return p.Stats(150, 2500, 15, 6.0);
    }

    /// <summary>A wing: the flat delta from the hull to the tip pod, crimson panels with a cream stripe and grey trailing panels
    /// under a lattice of dark ribs, its fuel, the tip pod that carries the turret and attitude jets, the rocket pod's pylon, the
    /// lift jets' pads and the rear leg pod's pylon underneath.</summary>
    private static Part Wing(int s)
    {
        var n = s < 0 ? "Port" : "Starboard";
        var p = Big($"Hatchet Wing {n}", $"The Hatchet's {n.ToLowerInvariant()} wing: the fuel in it, the gun turret's pod on its tip, the rocket pod's pylon, the lift jets and the rear leg pod under it.", "fueltank");
        double[] W(double x, double y, double z) => P(s * x, y, z);
        // Panels: x bands from the root to the tip, cut along the chord. (The root band's root edge runs inside the hull's skin.)
        double[] xs = [RootX0, .. WingBands, TipX];
        string Livery(int i, int j) => j switch
        {
            0 => Crimson,
            1 => i == xs.Length - 2 ? Crimson : Cream,
            2 => i == 0 ? Grey : Crimson,
            _ => i % 2 == 1 ? Grey : Crimson2,
        };
        for (var i = 0; i < xs.Length - 1; i++)
            for (var j = 0; j < Chord.Length - 1; j++)
            {
                // The cell's corners: its inner edge (x = xa, or the root edge) and its outer edge (x = xb), between chord fractions.
                double xb = xs[i + 1];
                (double X, double Z) Inner(double t)
                {
                    if (i > 0)
                    {
                        var xa = xs[i];
                        return (xa, LE(xa) + (TE(xa) - LE(xa)) * t);
                    }
                    // the root edge: from where the leading edge meets the hull back to the trailing edge
                    var z = RootZ0 + (TE(RootX1) - RootZ0) * t;
                    return (RootX(z), z);
                }
                var (ax0, az0) = Inner(Chord[j]);
                var (ax1, az1) = Inner(Chord[j + 1]);
                var lift = (i + j) % 2 == 1 ? 0.003 : 0; // (neighbouring cells lie on one plane: every other one 3 mm proud)
                double[] Top(double x, double z) => W(x, WTop(x) + lift, z);
                double[] Bot(double x, double z) => W(x, WBot(x) - lift, z);
                var bz0 = LE(xb) + (TE(xb) - LE(xb)) * Chord[j];
                var bz1 = LE(xb) + (TE(xb) - LE(xb)) * Chord[j + 1];
                Quad(p, Top(ax0, az0), Top(xb, bz0), Top(xb, bz1), Top(ax1, az1), Livery(i, j), "worn paint");
                Quad(p, Bot(ax0, az0), Bot(xb, bz0), Bot(xb, bz1), Bot(ax1, az1), j == 3 ? Grey : Grey2, "plated steel");
            }
        // The edges: the leading edge, the trailing edge (dark rims), per band.
        for (var i = 0; i < xs.Length - 1; i++)
        {
            double xa = xs[i], xb = xs[i + 1];
            Quad(p, W(xa, WTop(xa) - 0.01, LE(xa)), W(xb, WTop(xb) - 0.01, LE(xb)), W(xb, WBot(xb) + 0.01, LE(xb)), W(xa, WBot(xa) + 0.01, LE(xa)), Rib, "dirty steel");
            Quad(p, W(xa, WTop(xa) - 0.01, TE(xa)), W(xb, WTop(xb) - 0.01, TE(xb)), W(xb, WBot(xb) + 0.01, TE(xb)), W(xa, WBot(xa) + 0.01, TE(xa)), Rib, "dirty steel");
        }
        // The dark ribs on top: along each band line (chordwise) and along two chord cuts (spanwise), a running light at the leading edge.
        foreach (var x in WingBands)
            WingBar(p, s, x, LE(x) + 0.04, x, TE(x) - 0.04, 0.11, 0.05);
        foreach (var (t, h) in new[] { (Chord[1], 0.056), (Chord[3], 0.056) })
        {
            double x0 = 2.0, x1 = TipX - 0.05;
            WingBar(p, s, x0, LE(x0) + (TE(x0) - LE(x0)) * t, x1, LE(x1) + (TE(x1) - LE(x1)) * t, 0.1, h, 0.008);
        }
        foreach (var x in new[] { 3.0, 5.0 })
            Detail(p, Math.Min(s * (x - 0.06), s * (x + 0.06)), WingY - 0.03, LE(x) - 0.016, Math.Max(s * (x - 0.06), s * (x + 0.06)), WingY + 0.03, LE(x) - 0.002, Orange, glow: true);
        // The spar boxes (collision): a strip per band from the root block out to the pod.
        double[] sx = [WingRoot, .. WingBands, PodX0];
        for (var i = 0; i < sx.Length - 1; i++)
        {
            double xa = sx[i], xb = sx[i + 1];
            Hidden(p, s * xa, WBot(xb) + 0.012, LE(xb) + 0.03, s * xb, WTop(xb) - 0.012, TE(xb) - 0.03);
        }
        // The forward root (ahead of the hull's root block): spar boxes that stop clear of the hull's void.
        foreach (var (za, zb) in new[] { (-5.4, -4.2), (-4.2, -3.1), (-3.1, RootZ1 - 0.01) })
        {
            var xin = Math.Max(SkinHalf(za, WBot(2.0)), SkinHalf(zb, WBot(2.0))) + 0.02;
            var xout = Math.Min(WingRoot - 0.01, (za + 6.9) / 1.86 + 1.3 - 0.05);
            if (xout > xin + 0.1)
                Hidden(p, s * xin, WBot(xout) + 0.012, Math.Max(za, LE(xout) + 0.03), s * xout, WTop(xout) - 0.012, zb);
        }
        // The tip pod: an armoured block with a chamfered nose, the turret's base on its top, attitude jets out, up, down, fore and aft.
        {
            double cx = (PodX0 + PodX1) / 2;
            double[][] Sec(double z, double ins) => [W(PodX0 + ins, PodY0 + ins, z), W(PodX1 - ins, PodY0 + ins, z), W(PodX1 - ins, PodY1 - ins - 0.1, z), W(PodX1 - 0.15 - ins, PodY1 - ins, z), W(PodX0 + ins, PodY1 - ins, z)];
            Prism(p, Sec(PodZ0 - 0.004, 0.12), Sec(PodZ0 + 0.35, 0), Crimson2, "worn paint");
            Prism(p, Sec(PodZ0 + 0.354, 0), Sec(PodZ1 - 0.25, 0), Cream, "worn paint");
            Prism(p, Sec(PodZ1 - 0.246, 0), Sec(PodZ1 + 0.004, 0.1), Grey, "worn paint");
            Detail(p, Math.Min(s * (PodX1 + 0.003), s * (PodX1 + 0.016)), 1.25, PodZ0 + 0.6, Math.Max(s * (PodX1 + 0.003), s * (PodX1 + 0.016)), 1.33, PodZ1 - 0.6, Orange, "painted plating");
            Detail(p, Math.Min(s * (PodX1 + 0.003), s * (PodX1 + 0.015)), 1.1, PodZ0 + 0.4, Math.Max(s * (PodX1 + 0.003), s * (PodX1 + 0.015)), 1.18, PodZ0 + 0.55, s < 0 ? Red : "#30e060", glow: true);
            Hidden(p, s * PodX0, PodY0, PodZ0, s * PodX1, PodY1, PodZ1);
            // The turret's base ring on top.
            Prism(p, Ngon(s * cx, PodY1 + 0.004, TurretZ, 0.3, 10), Ngon(s * cx, PodY1 + 0.03, TurretZ, 0.27, 10), Rib, "dirty steel");
            Sock(p, "turret", s * cx, PodY1 + 0.04, TurretZ, "+y");
            Solid(p, s * cx - 0.2, PodY1 - 0.1, TurretZ - 0.2, s * cx + 0.2, PodY1 + 0.04, TurretZ + 0.2, Rib, "dirty steel");
            Sock(p, "rcsOut", s * PodX1, (PodY0 + PodY1) / 2 - 0.05, 3.9, s < 0 ? "-x" : "+x");
            Sock(p, "rcsUp", s * cx, PodY1, 4.35, "+y");
            Sock(p, "rcsDown", s * cx, PodY0, 3.05, "-y");
            Sock(p, "rcsFore", s * cx, (PodY0 + PodY1) / 2 - 0.05, PodZ0, "-z");
            Sock(p, "rcsAft", s * cx, (PodY0 + PodY1) / 2 - 0.05, PodZ1, "+z");
        }
        // The rocket pod's pylon on top, the lift jets' pads and the rear leg pod's pylon under.
        Solid(p, s * RocketX - 0.13, WTop(RocketX + 0.15) - 0.05, RocketZ - 0.38, s * RocketX + 0.13, WTop(RocketX) + 0.16, RocketZ + 0.38, Rib, "dirty steel");
        Sock(p, "rocket", s * RocketX, WTop(RocketX) + 0.16, RocketZ, "+y");
        foreach (var (nm, z) in new[] { ("liftF", LiftFz), ("liftB", LiftBz) })
        {
            Solid(p, s * LiftX - 0.3, WBot(LiftX) - 0.06, z - 0.3, s * LiftX + 0.3, WBot(LiftX - 0.3) + 0.11, z + 0.3, Frame, "dirty steel");
            Sock(p, nm, s * LiftX, WBot(LiftX) - 0.06, z, "-y");
        }
        Solid(p, s * WingLegX - 0.25, LegY, WingLegZ - 0.5, s * WingLegX + 0.25, WBot(WingLegX - 0.25) + 0.11, WingLegZ + 0.5, Frame, "dirty steel");
        Detail(p, Math.Min(s * (WingLegX - 0.252), s * (WingLegX + 0.252)), LegY + 0.25, WingLegZ - 0.502, Math.Max(s * (WingLegX - 0.252), s * (WingLegX + 0.252)), LegY + 0.33, WingLegZ + 0.502, Hazard, "hazard paint");
        Sock(p, "leg", s * WingLegX, LegY, WingLegZ, "-y");
        // A small canted fin on the trailing edge, raked back.
        {
            const double fx = 2.6, h = 0.9, cant = 0.7;
            var y0 = WTop(fx) + 0.004;
            double[][] Face(double dx) => [W(fx + dx, y0, 3.5), W(fx + dx, y0, TE(fx) - 0.05), W(fx + h * cant + dx, y0 + h * 0.72, TE(fx) + 0.55), W(fx + h * cant + dx, y0 + h * 0.72, 4.5)];
            Prism(p, Face(-0.035), Face(0.035), Crimson2, "worn paint");
        }
        p.Set(("fuelTank", Kw(("capacity", WingFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(s * 2.3, WingY, TE(2.5) - 0.03).Cast<object>()], "+z", "fuel"))));
        return p.Stats(1300, 5000, 25, 12.0);
    }

    /// <summary>A dark rib bar on the wing's top from (x0, z0) to (x1, z1), w wide and h proud.</summary>
    private static void WingBar(Part p, int s, double x0, double z0, double x1, double z1, double w, double h, double b = 0.004)
    {
        var d = Unit([x1 - x0, 0, z1 - z0]);
        double nx = -d[2] * w / 2, nz = d[0] * w / 2;
        double[] Q(double x, double z, double ox, double oz, double up) => P(s * (x + ox), WTop(x + ox) + up, z + oz);
        Prism(p, [Q(x0, z0, nx, nz, b), Q(x0, z0, -nx, -nz, b), Q(x1, z1, -nx, -nz, b), Q(x1, z1, nx, nz, b)],
            [Q(x0, z0, nx * 0.7, nz * 0.7, h), Q(x0, z0, -nx * 0.7, -nz * 0.7, h), Q(x1, z1, -nx * 0.7, -nz * 0.7, h), Q(x1, z1, nx * 0.7, nz * 0.7, h)], Rib, "dirty steel");
    }

    // ---- the fittings of its own

    /// <summary>One of the two drives: an armoured block (half the engine block) with a bell at its end. Its engine, a Drive Core XS,
    /// stands in the cabin on its axis behind the stern's plate.</summary>
    private static Part Drive()
    {
        var p = new Part("Hatchet Drive", Small, Section, $"One of the Hatchet's two drives, {DriveForce / 1e3:0} kN: an armoured block on its stern with the bell at its end. Its engine, a Drive Core XS, stands in the cabin on its axis.", role: "thruster", tool: "wrench", material: "steel", color: Grey2, group: Group);
        const double hw = 0.48, hh = 0.48, len = 1.3;
        double[][] Sq(double z, double ins) => [[-hw + ins, -hh + ins + 0.1, z], [-hw + ins + 0.1, -hh + ins, z], [hw - ins - 0.1, -hh + ins, z], [hw - ins, -hh + ins + 0.1, z], [hw - ins, hh - ins - 0.1, z], [hw - ins - 0.1, hh - ins, z], [-hw + ins + 0.1, hh - ins, z], [-hw + ins, hh - ins - 0.1, z]];
        Prism(p, Sq(0.004, 0.04), Sq(0.2, 0), Grey2, "plated steel");
        Prism(p, Sq(0.204, 0), Sq(len - 0.15, 0), Crimson2, "worn paint");
        Prism(p, Sq(len - 0.146, 0), Sq(len, 0.06), Rib, "dirty steel");
        // Grilles on its flanks, a heat band.
        foreach (var sx in new[] { -1, 1 })
            for (var i = 0; i < 5; i++)
            {
                var z = 0.35 + i * 0.13;
                p.Add(Box([sx > 0 ? hw - 0.002 : -hw - 0.018, -0.25, z], [sx > 0 ? hw + 0.018 : -hw + 0.002, 0.25, z + 0.05], Dark, detail: true, shape: Kw(("finish", "soot"))));
            }
        // The bell.
        (double Z, double R)[] st = [(len - 0.004, 0.3), (len + 0.1, 0.27), (len + 0.3, 0.36), (len + 0.5, 0.43)];
        for (var k = 0; k < st.Length - 1; k++)
        {
            var r0 = RingZ(0, 0, st[k].R, st[k].R, st[k].Z, 12);
            var r1 = RingZ(0, 0, st[k + 1].R, st[k + 1].R, st[k + 1].Z, 12);
            for (var i = 0; i < 12; i++)
                Quad(p, r0[i], r0[(i + 1) % 12], r1[(i + 1) % 12], r1[i], k == 0 ? "#3a3d40" : "#2a2c2e", k == 0 ? "dirty steel" : "soot");
        }
        Prism(p, RingZ(0, 0, 0.37, 0.37, len + 0.33, 12), RingZ(0, 0, 0.39, 0.39, len + 0.38, 12), "#5a4a3a", "copper");
        Plate(p, [.. RingZ(0, 0, 0.24, 0.24, len + 0.12, 12)], Glow, glow: true);
        p.Add(Box([-hw + 0.02, -hh + 0.02, 0.0], [hw - 0.02, hh - 0.02, len], hidden: true));
        p.Sock("mount", [0, 0, 0], "-z");
        var tail = len + 0.5;
        p.Set(("thruster", Kw(("force", DriveForce), ("spool", 0.6), ("exhaust", "+z"), ("at", Py.L(0, 0, tail)), ("burnRate", Py.Round(DriveForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, Py.Round(hh - 0.02, 3), 0.6], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, tail)), ("dir", "+z"), ("radius", 0.42), ("length", 3.4), ("kind", "Rocket")))));
        p.Set(("works", Kw(("needs", Py.L("core")))));
        p.Set(("collider", "boxes"));
        return p.Stats(650, 3500, 30, 4.0);
    }

    /// <summary>A lift jet: a squat nozzle under a pad, 150 kN straight down.</summary>
    private static Part LiftJet()
    {
        const double k = 0.6;
        var p = new Part("Hatchet Lift Jet", Small, Section, $"A lift jet: {LiftForce / 1e3:0} kN straight down. Four under the Hatchet's wings hold it up.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[] R(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * k * Math.Cos(an), 4), Py.Round(y * k, 4), Py.Round(r * k * Math.Sin(an), 4)];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.55, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.48, -0.45, i))], Grey2, "worn paint");
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.32, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.24, i))], Orange, "painted plating");
        (double Y, double R)[] st = [(-0.45, 0.32), (-0.6, 0.27), (-0.9, 0.38)];
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < 8; i++)
                Quad(p, R(st[s].R, st[s].Y, i), R(st[s].R, st[s].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => R(0.24, -0.61, i))], "#ffb060", glow: true);
        p.Add(Box([-0.45 * k, -0.45 * k, -0.45 * k], [0.45 * k, 0, 0.45 * k], hidden: true));
        p.Add(Box([-0.3 * k, -0.9 * k, -0.3 * k], [0.3 * k, -0.45 * k, 0.3 * k], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", 0.5), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-0.9 * k, 4), 0)), ("burnRate", Py.Round(LiftForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.45 * k, 4), Py.Round(-0.2 * k, 4), 0], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-0.9 * k, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.38 * k, 4)), ("length", 1.8), ("kind", "Rocket")))));
        p.Set(("collider", "boxes"));
        return p.Stats(300, 2500, 25, 6.0);
    }

    /// <summary>The wing-tip gun: a cream armoured mantlet with twin barrels, belt-fed from the box on its back.</summary>
    private static Part TwinCannon()
    {
        var p = new Part("Hatchet Twin Cannon", Small, Section, "The Hatchet's wing-tip gun: twin 30 mm barrels in a cream mantlet, 480 rounds a minute out to 900 m. Belt-fed from the box on its back.", role: "gun", tool: "wrench", material: "steel", color: Cream, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = true) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        p.Add(Box([-0.26, -0.18, -0.34], [0.26, 0.18, 0.36], Cream, shape: Kw(("finish", "worn paint"))));
        Prism(p, [[-0.26, 0.18, -0.34], [0.26, 0.18, -0.34], [0.26, 0.18, 0.36], [-0.26, 0.18, 0.36]], [[-0.19, 0.29, -0.22], [0.19, 0.29, -0.22], [0.19, 0.29, 0.3], [-0.19, 0.29, 0.3]], Cream2, "worn paint");
        Prism(p, [[-0.26, -0.18, -0.34], [0.26, -0.18, -0.34], [0.26, 0.1, -0.34], [-0.26, 0.1, -0.34]], [[-0.2, -0.13, -0.46], [0.2, -0.13, -0.46], [0.2, 0.06, -0.46], [-0.2, 0.06, -0.46]], Grey, "worn paint");
        foreach (var x in new[] { -0.1, 0.1 })
        {
            B(x - 0.055, -0.08, -0.62, x + 0.055, 0.03, -0.46, Rib, "dirty steel");
            B(x - 0.035, -0.06, -1.5, x + 0.035, 0.01, -0.62, "#2a2c2e", "steel");
            B(x - 0.05, -0.075, -1.6, x + 0.05, 0.025, -1.48, Dark, "soot");
        }
        B(-0.265, -0.04, -0.2, 0.265, 0.02, 0.25, Crimson, "worn paint");
        B(0.12, 0.2, 0.05, 0.2, 0.27, 0.15, "#ff5040", "plastic");
        p.Add(Box([0.13, 0.27, 0.06], [0.19, 0.296, 0.14], "#ff6050", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, -0.18, 0], "-y");
        p.Sock("ammo", [0, 0.18, 0.2], "+y");
        p.Set(("gun", Kw(("damage", 30), ("rpm", 480), ("spread", 0.5), ("range", 900), ("mag", 120), ("reload", 3.5), ("kick", 14), ("fx", "cannon"), ("speed", 950), ("soundOf", "Bastille"), ("muzzle", Py.L(0, -0.025, -1.6)))), ("centreSockets", false));
        return p.Stats(260, 900, 16, 4.0);
    }

    /// <summary>The power cell: a squat armoured cell on the cabin floor, 120 kW for the turrets, the air and the lights. No fuel.</summary>
    private static Part PowerCell()
    {
        var p = new Part("Hatchet Power Cell", Small, Section, "The Hatchet's power cell: a squat armoured cell on the cabin floor, 120 kW for the turrets, the air, the flight computer and the lights. No fuel.", role: "generator", tool: "welder", material: "steel", color: Grey2, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        B(-0.25, 0, -0.3, 0.25, 0.9, 0.3, Grey2, "plated steel");
        B(-0.255, 0.1, -0.2, 0.255, 0.16, 0.2, Orange, "painted plating", true);
        B(-0.2, 0.9, -0.24, 0.2, 1.0, 0.24, Rib, "dirty steel", true);
        p.Add(Box([-0.16, 0.5, -0.305], [0.16, 0.75, -0.3], "#60e0a0", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 120), ("spool", 4), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.1, 0.3)), ("face", "+z")))));
        return p.Stats(400, 2500, 20, 6.0);
    }

    /// <summary>The flight computer: a flight stabilizer with a fighter's gyro in a low console ahead of the pilot.</summary>
    private static Part FlightComputer()
    {
        var p = new Part("Hatchet Flight Computer", Small, Section, "The Hatchet's flight computer: a flight stabilizer with a fighter's gyro in a low console ahead of the pilot. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: Console, group: Group);
        p.Add(Box([-0.4, 0, -0.25], [0.4, 0.75, 0.2], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.4, 0.75, -0.25], [0.4, 0.88, -0.05], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.32, 0.55, 0.2], [0.32, 0.72, 0.212], Screen, detail: true, shape: Kw(("glow", true))));
        for (var k = 0; k < 4; k++)
            p.Add(Box([-0.25 + k * 0.14, 0.4, 0.2], [-0.2 + k * 0.14, 0.45, 0.215], k % 2 == 0 ? "#40e070" : Orange, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.08, 0.881, -0.2], [0.08, 0.89, -0.1], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 900000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(120, 600, 6, 3.0);
    }

    private static Part Ladder(string name, double rise, string desc, string sockName, double[] sockAt, string sockDir, double over = 1.0)
    {
        var p = new Part(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var top = rise + over;
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

    /// <summary>The belly hatch (the Satchel's kind): a plate that shuts the boarding shaft flush under the keel, on a strut up the
    /// shaft's aft wall to the floor's edge, where it hangs. It slides down to the ground and the ladder standing on it rides it.</summary>
    private static Part BellyHatchPart()
    {
        var p = new Part(BellyHatchName, Small, Section, "The Hatchet's belly hatch: a plate that shuts the boarding shaft under the keel. F (or the cabin's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.", role: "door", tool: "welder", material: "steel", color: Grey2, group: Group);
        double[] S = [HatchX, Ft - 0.125, BellyHatch.Z1];
        double[] L(double x, double y, double z) => [Py.Round(x - S[0], 4), Py.Round(y - S[1], 4), Py.Round(z - S[2], 4)];
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string? col, string? fin, bool detail = false, bool hidden = false) =>
            p.Add(hidden ? Box(L(x0, y0, z0), L(x1, y1, z1), hidden: true) : Box(L(x0, y0, z0), L(x1, y1, z1), col!, detail: detail, shape: Kw(("finish", fin!))));
        double x0 = BellyHatch.X0 - 0.02, x1 = BellyHatch.X1 + 0.02, z0 = BellyHatch.Z0 - 0.02, z1 = BellyHatch.Z1 - 0.035;
        B(x0, HatchBottom, z0, x1, HatchTop, z1, Grey2, "plated steel");
        foreach (var i in Enumerable.Range(0, 4))
        {
            var z = z0 + 0.15 + i * 0.28;
            B(x0 + 0.1, HatchBottom - 0.008, z, x1 - 0.1, HatchBottom - 0.002, z + 0.12, Hazard, "hazard paint", detail: true);
        }
        B(x0 + 0.03, HatchTop + 0.002, z0 + 0.03, x0 + 0.13, HatchTop + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(x1 - 0.13, HatchTop + 0.002, z0 + 0.03, x1 - 0.03, HatchTop + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(HatchX - 0.3, HatchTop, BellyHatch.Z1 - 0.03, HatchX + 0.3, Ft - 0.01, BellyHatch.Z1, null, null, hidden: true);
        B(HatchX - 0.28, HatchTop + 0.004, BellyHatch.Z1 - 0.028, HatchX + 0.28, Ft - 0.02, BellyHatch.Z1 - 0.004, Frame, "painted steel");
        p.Sock("hang", [0, 0, 0], "+z");
        p.Sock("ladder", L(HatchX, HatchTop, LadderZ - 0.1), "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", Py.Round(HatchTravel, 3)), ("seconds", 3.0), ("hang", "hang"))));
        return p.Stats(300, 3000, 30, 6.0);
    }

    // ---- welding the pieces (the Satchel's way)

    private static IEnumerable<double> Vd(PyDict b, string key) => ((List<object?>)b[key]!).Select(v => Convert.ToDouble(v) + H);

    private static bool Drawn(PyDict b) => b.Get("detail") is true;

    private static void Connect(List<Part> parts)
    {
        var solid = parts.Select(q => q.Boxes.Where(b => !Drawn(b)).Select(b => (Mn: Vd(b, "min").ToArray(), Mx: Vd(b, "max").ToArray())).ToList()).ToList();
        var bounds = solid.Select(l => (Mn: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Min(b => b.Mn[a])).ToArray(), Mx: Enumerable.Range(0, 3).Select(a => l.Count == 0 ? 0 : l.Max(b => b.Mx[a])).ToArray())).ToList();
        var count = parts.Select(q => q.Sockets.Count(s => ((string)s["name"]!).StartsWith('j'))).ToArray();
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
                    if (da[1] == db[1] && da[0] != db[0] && ((string)s["name"]!).StartsWith('j') && ((string)t["name"]!).StartsWith('j'))
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
            throw new InvalidOperationException("wedge_fighter_parts: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
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

    private static PyDict Blueprint(Dictionary<string, PyDict> defs, List<string> order)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "The Raiders' wedge fighter: a pilot under a long glass canopy and a gunner in the cabin behind, a twin cannon turret on each wing tip worked by the gunner, two rocket pods the pilot fires straight ahead, and two drives in the engine block at the stern with their engines in the small cabin behind the cockpit. Boarded up a ladder through a hatch in the cabin floor; lift jets and leg pods under the wings and the belly set it down.", defs, maxSteps: 160);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var st = new Dictionary<string, int>();
        foreach (var name in order)
            st[name] = st.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        int belly = st["Hatchet Belly"], port = st["Hatchet Hull Port"], stbd = st["Hatchet Hull Starboard"], stern = st["Hatchet Stern"],
            nose = st["Hatchet Nose"], wingL = st["Hatchet Wing Port"], wingR = st["Hatchet Wing Starboard"];
        const string rcs = "RCS Thruster";
        // The drives on the stern's plate, each with its engine bolted to the plate's inner face on its axis.
        foreach (var n in new[] { "L", "R" })
        {
            bp.HangTurned("Hatchet Drive", stern, $"drive{n}", "mount", 0);
            bp.HangTurned("Drive Core XS", stern, $"engine{n}", "front", 0);
        }
        // Lift jets, legs and attitude jets.
        foreach (var w in new[] { wingL, wingR })
        {
            bp.Hang("Hatchet Lift Jet", w, "liftF", "top");
            bp.Hang("Hatchet Lift Jet", w, "liftB", "top");
            LegPodParts.HangPod(bp, LegPod, w, "leg");
            foreach (var r in new[] { "rcsOut", "rcsUp", "rcsDown", "rcsFore", "rcsAft" })
                Paint(bp, bp.Hang(rcs, w, r, "base"), Frame, "dirty steel");
        }
        for (var i = 0; i < BellyLegs.Length; i++)
            LegPodParts.HangPod(bp, LegPod, belly, $"leg{i}");
        foreach (var r in new[] { "rcsDownN", "rcsUpN", "rcsOutNL", "rcsOutNR" })
            Paint(bp, bp.Hang(rcs, nose, r, "base"), Frame, "dirty steel");
        foreach (var r in new[] { "rcsUpSL", "rcsUpSR", "rcsDownSL", "rcsDownSR" })
            Paint(bp, bp.Hang(rcs, stern, r, "base"), Frame, "dirty steel");
        // The pilot flies and fires the rocket pods; the gunner works both wing-tip turrets from the Gunnery Console.
        var pilot = Paint(bp, bp.Hang("Flight Couch", belly, "pilot", "bottom"), "#3a2a2c", "fabric");
        var fc = bp.Hang("Hatchet Flight Computer", belly, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, fc));
        var rocketModule = bp.Hang("Turret Module", belly, "rocketModule", "-y");
        bp.Wires.Add((pilot, rocketModule));
        foreach (var w in new[] { wingL, wingR })
        {
            var pod = bp.Hang("Rocket Pod Mk2", w, "rocket", "bottom");
            Paint(bp, bp.Hang("Ammo Box", pod, "back", "front"), Grey2, "worn paint");
            bp.Wires.Add((rocketModule, pod));
        }
        bp.Hang("Gunnery Console", belly, "gunConsole", "bottom"); // (its screens; the turrets' module is on the cabin floor aft)
        var gunner = Paint(bp, bp.Hang("Flight Couch", belly, "gunner", "bottom"), "#3a2a2c", "fabric");
        // (the turrets' Turret Module on the cabin floor aft, within a wire's 8 m of both guns and of the gunner's couch)
        var gunModule = bp.Hang("Turret Module", belly, "gunModule", "-y");
        bp.Wires.Add((gunner, gunModule));
        foreach (var w in new[] { wingL, wingR })
        {
            var table = bp.Hang("Turntable 1x1 Long", w, "turret", "base"); // (the fast one: 180 degrees a second)
            var hinge = bp.Hang("Motor Hinge", table, "top", "base");
            var gun = bp.Hang("Hatchet Twin Cannon", hinge, "leaf", "bottom");
            Paint(bp, bp.Hang("Ammo Box", gun, "ammo", "bottom"), Grey2, "worn paint");
            bp.Wires.Add((gunModule, gun));
        }
        // The cabin: the belly hatch and its ladder, suits, the air, the battery and the power cell.
        var hatch = bp.Hang(BellyHatchName, belly, "boarding", "hang");
        Paint(bp, bp.Hang(LadderName, hatch, "ladder", "foot"), Frame, "painted steel");
        var hatchSwitch = bp.Hang("Bakelite Switch", port, "switchCabin", "back");
        bp.Steps[hatchSwitch]["config"] = new PyDict { ["startOn"] = false };
        bp.Wires.Add((hatchSwitch, hatch));
        // (a locker's back is its +z: RY270 puts it to -x, against the port wall, and RY90 to +x)
        bp.HangTurned(EvaParts.LockerName, belly, "lockerL", "bottom", BuildingParts.RY270);
        bp.HangTurned(EvaParts.LifeSupportName, belly, "lifeSupport", "bottom", BuildingParts.RY270);
        bp.HangTurned(EvaParts.LockerName, belly, "lockerR", "bottom", BuildingParts.RY90);
        bp.HangTurned("Ship Battery XS", belly, "battery", "bottom", BuildingParts.RY90);
        bp.Hang("Hatchet Power Cell", belly, "power", "bottom");
        // Lights: the cockpit's on its switch to starboard, the cabin's on the switch by the hatch... (the cabin's own, starboard).
        int Lamp(int on, string sock, string colour)
        {
            var s = bp.Hang("Bulkhead Light", on, sock, "back");
            bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
            return s;
        }
        var ckSwitch = bp.Hang("Bakelite Switch", stbd, "switch", "back");
        bp.Steps[ckSwitch]["config"] = new PyDict { ["startOn"] = true };
        foreach (var side in new[] { port, stbd })
            bp.Wires.Add((ckSwitch, Lamp(side, "lampCockpit", "#ff7060")));
        var cabSwitch = bp.Hang("Bakelite Switch", stbd, "switchCabin", "back");
        bp.Steps[cabSwitch]["config"] = new PyDict { ["startOn"] = true };
        foreach (var side in new[] { port, stbd })
            foreach (var l in new[] { "lampCabinF", "lampCabinB" })
                bp.Wires.Add((cabSwitch, Lamp(side, l, l == "lampCabinF" ? "#ffb070" : "#e8f4ff")));
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        List<Part> structure = [Belly(), Side(-1), Side(1), Canopy(), Roof(), Nose(), SternPart(), Bulkhead(), Wing(-1), Wing(1)];
        Connect(structure);
        var order = PlaceOrder(structure, "Hatchet Belly");
        List<Part> parts =
        [
            .. structure,
            Drive(), LiftJet(), TwinCannon(), PowerCell(), FlightComputer(), BellyHatchPart(),
            Ladder(LadderName, Py.Round(BoardRise, 3), "The Hatchet's boarding ladder: it stands on the belly hatch, stowed up through the cabin floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.", "foot", [0, 0, -0.1], "-y", over: -0.03),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"wedge_fighter_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/HatchetFighter.json", PyJson.Dumps(Blueprint(defs, order), 2) + "\n");
    }
}
