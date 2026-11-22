using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Grapnel, the Salvagers' salvage tug (Karl, 2026-11-21: his picture of a salvage ship, "add this ship to the salvage ships"):
/// a boxy hull in salvage yellow and worn cream, a glass cockpit at the front over a flat yellow chin with three tool mounts
/// (Karl, 2026-11-21: the claws and the cutter came off, the mounts take tools through the sockets), dark ring frames round the hull and a dark belt along its beam, a rack of four banded tanks on each flank, antennas, a dish
/// and a radome on the roof, four legs. About 30 m long: the cockpit and a crew cab forward, a hold amidships with two standard 2 m
/// containers for sorted salvage (filled from the passages down its sides), then engineering, whose two hull drives bolt to the
/// stern bulkhead with their engines inside.
///
/// Started from the Satchel's generator (CourierParts.cs), whose helpers this copies: a hull lofted through stations, the rooms a
/// box of their own inside it, each big part drawn whole and dealt out into welded pieces. Ship metres: x across (the centreline at
/// Cx), y up from the deck's underside, z along the ship (forward is -z; 0 is the hold's middle); P() moves them by half a cell into
/// a part's frame (docs/sandbox/constructs/grapnel.md).
/// </summary>
public sealed class GrapnelParts : IPartGenerator
{
    public string Name => "grapnel_parts";

    private const string Out = "Constructs/Parts/Grapnel";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Grapnel salvage tug";
    public const string ShipName = "Grapnel Salvage Tug";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint (Karl's picture): salvage yellow on the chin, the cockpit's hood, the shoulders and the panels' inserts; worn cream on
    // the upper sides, pale grey below and on the roof, dark ring frames and a dark belt along the beam, orange marks.
    private const string Skin = "#d8d3c2", Skin2 = "#b9b8b0", Armour = "#d9a12b", Armour2 = "#c48b22", Bare = "#9a9d9f", Frame = "#2f3134",
        Dark = "#232528", Hazard = "#f0b81c", Orange = "#e2702a", Red = "#c0392b", Glow = "#ffe0a0", Glass = "#8fb4c4", Link = "#0f7bd8",
        Paper = "#e6e2d6", Green = "#2f9a52",
        Wall = "#cfcdc4", WallLow = "#5b5f63", WallTrim = "#2f3134", Deck = "#5d6266", Grate = "#3d4144", Ceil = "#b4b6b2",
        RackCol = "#34383d", Latch = "#c4362b";

    /// <summary>The walls WallRun draws with.</summary>
    private static string WallCol = Wall, DadoCol = WallLow;

    // ---- the plan

    private const double Cx = 0.0; // the centreline
    private const double Ft = 0.25; // the deck's top, all along the ship
    private const double CCeil = 2.75, CRoof = 3.0, Lw = 3.25; // the cab: its ceiling, roof slab's top, rooms' inner half width
    private const double SCeil = 2.6, SRoof = 2.85, SLine = 0.06; // the passages beside the hold: ceiling, roof slab, the hull's lining
    private const double PassIn = 3.0, PassOut = 4.0; // a passage's inner face (the hold wall's outer face) and its lining's face
    private const double ECeil = 2.75, ERoof = 3.0; // engineering
    private const double Zt = -18.0, ZS1 = -16.0, ZCk = -13.5, ZCab = -11.0, ZNeck = -7.25, ZCabEnd = -5.15, ZEng = 3.65, ZEngMid = 7.0, ZEngEnd = 10.5, Stern = 11.4;
    private const double ZCkFloor = -16.5; // the cockpit's floor, forward
    private const double ZCkW = -13.125, ZCabW = -5.275; // the cross walls' middles: the cockpit's back, the cab's aft
    private const double DoorH = 2.25;
    /// <summary>The cab's rooms: port the bunk room then the head, starboard the galley then the airlock (the cross walls' middles).</summary>
    private const double ZPortW = -8.675, ZStbdW = -9.775;

    /// <summary>The hold: its inner half width and ends (the containers' cells), its walls' top, its bulkheads' middles.</summary>
    private const double BayHx = 2.75, BayZ0 = -3.65, BayZ1 = 3.65, BayIn = 3.4, BayTop = 3.9;
    /// <summary>The standard container ("Standard Container 2m", three 2x2x2 m parts in a row): across, up, along; where its middle
    /// stands off the centreline, and the hold floor's twist-lock pads under its corner castings.</summary>
    private const double CtW = 2.5, CtL = 6.5, CtX = 1.4, LockH = 0.06, CastX = 1.165, CastZ = 3.165;
    public const string ContainerName = "Standard Container 2m";

    /// <summary>The hull's stations (the hull pieces are cut at them).</summary>
    private static readonly double[] Bands = [Zt, ZS1, ZCk, ZCab, ZNeck, ZCabEnd, BayZ0, BayZ1, ZEngMid, ZEngEnd, Stern];
    /// <summary>The dark ring frames standing proud of the skin (the picture's bands): the cockpit's back, the hold's ends, the cab's and engineering's ends.</summary>
    private static readonly double[] RibZ = [ZCk, ZCabEnd, BayZ0, BayZ1, ZEngEnd];

    // The boarding shaft in the airlock (starboard, aft in the cab), the belly hatch shutting it and the ladder on the hatch (the Dray's way).
    private static readonly Rect BellyHatch = new(Cx + 1.1, -8.6, Cx + 2.25, -7.35);
    private const string BellyHatchName = "Grapnel Belly Hatch";
    private static double HatchX => BellyHatch.X0 + 0.575;
    private static double HatchBottom => At(BellyHatch.Z1)[0].Y - 0.012;
    private static double HatchTop => HatchBottom + 0.08;
    private static double HatchTravel => Math.Min(HatchBottom - Ground - 0.005, CCeil - 0.03 - Ft);
    private static double LadderZ => BellyHatch.Z1 - 0.03;
    private static double BoardRise => Ft - (HatchTop - HatchTravel);

    // ---- flying it: two hull drives on the stern bulkhead, lift jets and legs under the cab and engineering, attitude jets at both ends.

    /// <summary>The drives: their axes off the centreline and over the keel (a Drive Core M's saddles on engineering's deck), their shrouds.</summary>
    private static readonly double[] DriveX = [-2.0, 2.0];
    private static readonly double DriveY = Ft + ShipWorksParts.AxisOver(1);
    private const double ShroudR = 1.1;
    private const int KeelFuel = 7000, SternFuel = 6000, RackFuel = 3000; // litres
    private const double LiftForce = 1200000, JetScale = 1.05, Burn = 2.5;
    private static readonly (double X, double Z)[] LiftAt = [(-1.44, -10.4), (1.76, -10.4), (-1.44, 8.6), (1.76, 8.6)];
    private static readonly (double X, double Z)[] LegAt = [(-3.0, -12.4), (3.0, -12.4), (-3.0, 8.0), (3.0, 8.0)];
    private const string LegPod = "S";
    private const double PadProud = 0.1; // a jet's mount under the flat
    private const double LegProud = 0.16; // a leg pod's mount
    private static double Ground => -0.8 - LegProud - LegPodParts.ReachOf(LegPod);
    private const string Rcs = "Heavy RCS Thruster";
    private const double BowRcsZ = -12.0, SternRcsZ = 11.0, RcsPadW = 0.6;

    /// <summary>The tank racks: the hardpoint's face on the hold's flank (x) and height, the tanks' middles across and up, their radius.</summary>
    private const double RackX = 4.46, RackY = 1.2, TankCx = 5.1, RackY0 = 0.6, RackY1 = 1.85, TankR = 0.55;

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge)

    // A box with its corners cut: a small chamfer under the beam, upright sides, a big chamfer to the flat roof (the picture's yellow
    // shoulders). The nose is lower and narrower at the chin, so the windscreen rakes back from it to the roof.
    private static readonly (double, double)[] Body = [(3.6, -0.8), (4.25, -0.15), (4.3, 1.4), (4.25, 2.95), (3.15, 4.0)];
    private static readonly (double, double)[] Aft = [(3.5, -0.78), (4.2, -0.14), (4.25, 1.4), (4.2, 2.95), (3.1, 3.97)];

    private static readonly Sec[] Hull =
    [
        new(Zt, [(3.0, -0.6), (3.5, -0.15), (3.6, 1.0), (3.1, 2.0), (1.9, 2.45)]), // (the chin's flat face up to y 1, the windscreen over it)
        new(ZS1, [(3.5, -0.78), (4.1, -0.15), (4.2, 1.2), (4.0, 2.5), (2.7, 3.35)]),
        new(ZCk, Body), new(ZEngEnd, Body),
        new(Stern, Aft),
    ];

    /// <summary>A pad for an attitude jet: a square solid standing proud metres off the surface point (clear of the armour on it), sunk
    /// 11 cm into the hull; its outer face takes the socket.</summary>
    private static void RcsPad(Part p, string name, double x, double y, double z, string dir, double proud)
    {
        var s = dir[0] == '-' ? -1 : 1;
        var axis = dir[1] - 'x';
        double[] at = [x, y, z];
        double[] lo = [x - RcsPadW / 2, y - RcsPadW / 2, z - RcsPadW / 2], hi = [x + RcsPadW / 2, y + RcsPadW / 2, z + RcsPadW / 2];
        lo[axis] = Math.Min(at[axis] - s * 0.11, at[axis] + s * proud);
        hi[axis] = Math.Max(at[axis] - s * 0.11, at[axis] + s * proud);
        at[axis] += s * proud;
        Solid(p, lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], Frame, "dirty steel");
        Sock(p, name, at[0], at[1], at[2], dir);
    }

    /// <summary>A mount under the keel's flat (bottom y): a square solid half wide, sunk 11 cm into the hull and standing PadProud
    /// proud; its underside takes the socket.</summary>
    private static void MountPad(Part p, string name, double x, double y, double z, double half, double proud = PadProud)
    {
        Solid(p, x - half, y - proud, z - half, x + half, y + 0.11, z + half, Frame, "dirty steel");
        Sock(p, name, x, y - proud, z, "-y");
    }

    /// <summary>The x of facet k's surface (side s) at height y and station z (between its two profile points).</summary>
    private static double FacetX(int k, int s, double z, double y)
    {
        var q = At(z);
        var (a, b) = (q[k - 1], q[k]);
        return Cx + s * (a.H + (b.H - a.H) * (y - a.Y) / (b.Y - a.Y));
    }


    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];
    private readonly record struct Sec(double Z, (double H, double Y)[] Pts);

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
            throw new InvalidOperationException($"grapnel_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
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
        double[] axis = [Cx, 1.5, c[2]];
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
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.1, 0.06, Skin);
                    break;
                case 1:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.09, 0.06, Skin2);
                    Slab(p, k, s, za, zb, u0 + (u1 - u0) * 0.18, u1 - (u1 - u0) * 0.12, v0 + 0.14, v1 - 0.2, 0.09, 0.15, 0.04, Armour);
                    break;
                case 2:
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.08, 0.05, Bare, "painted steel");
                    break;
                case 4:
                    Slab(p, k, s, za, zb, u0, (u0 + u1) / 2 - 0.01, v0, v1, 0.02, 0.12, 0.07, Skin);
                    Slab(p, k, s, za, zb, (u0 + u1) / 2 + 0.01, u1, v0 + 0.1, v1, 0.02, 0.09, 0.05, Armour2);
                    break;
                default:
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.03, Frame, "dirty steel");
                    FStrip(p, k, s, za, zb, u0, u0 + Math.Min(0.25, (u1 - u0) * 0.2), v0 + 0.1, v0 + 0.22, 0.031, Hazard, "hazard paint");
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

    /// <summary>A ring of n points round axis (0 x, 1 y, 2 z) through c, radius r, at a along the axis; map turns each point into the part's frame.</summary>
    private static double[][] RingAx(double[] c, int axis, double r, double a, int n, Func<double[], double[]> map) => [.. Enumerable.Range(0, n).Select(i =>
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
    private static void DrawTank(Part p, double[] c, int axis, double r, double a0, double a1, double dome, int hoops, string col, string band, Func<double[], double[]> map)
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
    private static double[][] RingZ(double cx, double cy, double rx, double ry, double z, int n) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = Math.PI / n + i * 2 * Math.PI / n;
        return new[] { Py.Round(cx + rx * Math.Cos(a), 4), Py.Round(cy + ry * Math.Sin(a), 4), z };
    })];

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
            Run(at, g.A, y0, y1, WallCol, "painted plating", 0.125, false);
            Run(g.A, g.A + g.W, g.Head + y0, y1, WallCol, "painted plating", 0.125, false);
            if (g.Sill > 0)
            {
                Run(g.A, g.A + g.W, y0, y0 + g.Sill, WallCol, "painted plating", 0.125, false);
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
                Run(at + 0.05, g.A - 0.05, y0 + 0.003, y0 + 0.75, DadoCol, "painted plating", 0.137, true);
            // The frame: dark jambs, a hazard head.
            Run(g.A - 0.07, g.A, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A + g.W, g.A + g.W + 0.07, y0, y0 + g.Head + 0.07, WallTrim, "painted steel", 0.145, true);
            Run(g.A, g.A + g.W, y0 + g.Head, y0 + g.Head + 0.07, Hazard, "hazard paint", 0.145, true);
            at = g.A + g.W;
        }
        Run(at, a1, y0, y1, WallCol, "painted plating", 0.125, false);
        Run(at + 0.05, a1 - 0.05, y0 + 0.003, y0 + 0.75, DadoCol, "painted plating", 0.137, true);
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



    /// <summary>The void between the skin and the rooms as Void, its ends trimmed by ta and tb (0: flush, to touch the next part).</summary>
    private static void VoidT(Part p, double za, double zb, double ya, double yb, double xin, double ta, double tb)
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


    // ---- the Grapnel's own helpers

    /// <summary>The bands (pairs of stations) from za to zb.</summary>
    private static IEnumerable<(int I, double A, double B)> BandsIn(double za, double zb) =>
        Enumerable.Range(0, Bands.Length - 1).Where(i => Bands[i] >= za - 1e-6 && Bands[i + 1] <= zb + 1e-6).Select(i => (i, Bands[i], Bands[i + 1]));

    /// <summary>A ring frame's band across facet k (side s) at z: a bevelled slab standing out of the skin, odd facets' ends 6 mm wider.</summary>
    private static void Rib(Part p, int k, int s, double z, string color = Frame)
    {
        var e = k % 2 == 1 ? FacetStagger : 0;
        Slab(p, k, s, z - 0.22 - e, z + 0.22 + e, 0, 1, 0, 1, 0.03, 0.26, 0.06, color, "dirty steel");
    }

    /// <summary>The ribs a part draws on its facets ks between za (included) and zb.</summary>
    private static void Ribs(Part p, double za, double zb, params int[] ks)
    {
        foreach (var z in RibZ)
            if (z >= za - 1e-6 && z < zb - 1e-6)
                foreach (var k in ks)
                    foreach (var s in k is 0 or 5 ? [1] : new[] { -1, 1 })
                        Rib(p, k, s, z);
    }

    /// <summary>Whether a rib stands at z (so a band's plates keep clear of it).</summary>
    private static bool RibAt(double z) => RibZ.Any(r => Math.Abs(r - z) < 1e-6);

    /// <summary>A band of facet k on side s from za to zb: its skin plate and the panels on it, kept off the ribs at either end.</summary>
    private static void Band(Part p, int k, int s, double za, double zb, string color, int salt, bool plates = true)
    {
        Facet(p, k, s, za, zb, color, "painted plating");
        if (plates && zb - za > 1.5)
            Plates(p, k, s, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), salt);
    }

    /// <summary>The volume a part's rooms take (with their walls): what SplitDeck calls inside; outside it is hull.</summary>
    private static void Room(Part p, Rect r, double y0, double y1) => (Rooms.TryGetValue(p, out var rs) ? rs : Rooms[p] = []).Add((r, y0, y1));

    /// <summary>Draws with draw() and gives every box it adds to the part's last wall run (a rail, a pipe, a frame on its face).</summary>
    private static void OnWall(Part p, Action draw)
    {
        var n = p.Boxes.Count;
        draw();
        var w = Walls[p].Count - 1;
        for (var i = n; i < p.Boxes.Count; i++)
            Stuck[p.Boxes[i]] = w;
    }

    /// <summary>The salvage yellow insert on a band of the upper side (facet 3), between its ribs (Karl's picture).</summary>
    private static void Livery(Part p, int s, double za, double zb)
    {
        double a = za + (RibAt(za) ? 0.3 : 0.1), b = zb - (RibAt(zb) ? 0.3 : 0.1), len = zb - za;
        Slab(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.22, 0.8, 0.012, 0.045, 0.025, Armour, "painted plating");
    }

    // ---- the keel: the flat underside the whole length, the void under the floors (fuel), the boarding shaft, the jets' and legs' mounts

    private static Part Keel()
    {
        var p = Big("Grapnel Keel", "The Grapnel's keel: the flat underside under the cab, the salvage bay and engineering, with the fuel tankage in the void under the decks, the boarding shaft, and the lift jets' and legs' mounts.", "fueltank", Bare);
        var shaft = new Rect(BellyHatch.X0 - 0.05, BellyHatch.Z0 - 0.05, BellyHatch.X1 + 0.05, BellyHatch.Z1 + 0.05);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var y = At(za)[0].Y;
            var hb = At(za)[0].H;
            if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                // (the cab's band is level: the flat round the shaft's hole in four plates)
                var a = BellyHatch;
                Plate(p, [P(Cx - hb, y, za), P(Cx + hb, y, za), P(Cx + hb, y, a.Z0), P(Cx - hb, y, a.Z0)], Bare, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z1), P(Cx + hb, y, a.Z1), P(Cx + hb, y, zb), P(Cx - hb, y, zb)], Bare, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z0), P(a.X0, y, a.Z0), P(a.X0, y, a.Z1), P(Cx - hb, y, a.Z1)], Bare, "plated steel");
                Plate(p, [P(a.X1, y, a.Z0), P(Cx + hb, y, a.Z0), P(Cx + hb, y, a.Z1), P(a.X1, y, a.Z1)], Bare, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Bare, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 1, s, za, zb, Skin2, 1 + i, plates: za >= ZCk);
                Chine(p, 1, s, za, zb, 0.2, 0.09, Frame);
            }
            // Keel skids along the flat under the cab, the bay and engineering.
            if (zb - za > 2 && za >= ZCk)
                foreach (var s in new[] { -1, 1 })
                    Detail(p, Cx + s * 1.9 - 0.08, y - 0.12, za + 0.3, Cx + s * 1.9 + 0.08, y - 0.023, zb - 0.3, Dark, "dirty steel");
        }
        Ribs(p, ZCk, ZEngEnd, 0, 1);
        // The void under the floors (the tankage), round the shaft.
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            if (yb >= -0.05)
                continue;
            var xo = Math.Floor((new[] { SkinHalf(za, yb), SkinHalf(za, 0), SkinHalf(zb, yb), SkinHalf(zb, 0) }.Min() - 0.06) * 100) / 100;
            if (xo < 0.3)
                continue;
            foreach (var r in Minus([new Rect(Cx - xo, za + 0.012, Cx + xo, zb - 0.012)], shaft))
                Hidden(p, r.X0, yb, r.Z0, r.X1, 0.0, r.Z1);
        }
        // The shaft's lining: plates down through the keel, hazard round its mouth.
        var hy = At((BellyHatch.Z0 + BellyHatch.Z1) / 2)[0].Y;
        foreach (var (x0, z0, x1, z1, e) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0, 0.0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1, 0.0),
            (shaft.X0, shaft.Z0 + 0.025, shaft.X0, shaft.Z1 - 0.025, 0.005), (shaft.X1, shaft.Z0 + 0.025, shaft.X1, shaft.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + 0.01 + e, z0), P(x1, hy + 0.01 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z0 - 0.12, shaft.X1 + 0.12, hy - 0.004, shaft.Z0, Hazard, "hazard paint");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z1, shaft.X1 + 0.12, hy - 0.004, shaft.Z1 + 0.12, Hazard, "hazard paint");
        // The lift jets' and the legs' mounts: square pads under the flat; the bow's down jets.
        for (var i = 0; i < LiftAt.Length; i++)
            MountPad(p, $"lift{i}", Cx + LiftAt[i].X, At(LiftAt[i].Z)[0].Y, LiftAt[i].Z, 0.45);
        for (var i = 0; i < LegAt.Length; i++)
            MountPad(p, $"leg{i}", Cx + LegAt[i].X, At(LegAt[i].Z)[0].Y, LegAt[i].Z, 0.4, LegProud);
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsDown" + (s < 0 ? "L" : "R"), Cx + s * 0.55, At(BowRcsZ)[0].Y, BowRcsZ, "-y", 0.05); // (inboard of the legs)
        // Running lights under the cab's chin: a pair of landing lamps.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 1.0 - 0.16, At(ZCk)[0].Y - 0.05, ZCk + 0.25, Cx + s * 1.0 + 0.16, At(ZCk)[0].Y - 0.012, ZCk + 0.55, Glow, glow: true);
        p.Set(("fuelTank", Kw(("capacity", KeelFuel)))); // (shared out over its bands, each with its Fuel out: SplitBands)
        return p.Stats(16000, 50000, 40, 25.0);
    }

    // ---- the roof: the top facets the whole length, the cockpit's glass roof, the void over the ceilings; a comms mast, the coamings
    // either side of the open bay with its floodlights, radiators over engineering

    /// <summary>A strip of the roof's facet (5) from v0 to v1 across it (0 the starboard edge, 1 the port edge) between za and zb.</summary>
    private static void RoofStrip(Part p, double za, double zb, double v0, double v1, string color)
    {
        double[] Q(double u, double v) { var q = OnFacet(5, 1, za, zb, u, v); return Sh([q[0], q[1], q[2] + FacetStagger + (u < 0.5 ? FacetTrim : -FacetTrim)]); } // (staggered as Facet staggers the odd facets)
        Quad(p, Q(0, v0), Q(0, v1), Q(1, v1), Q(1, v0), color, "painted plating");
    }

    private static Part Roof()
    {
        var p = Big("Grapnel Roof", "The Grapnel's roof: the cockpit's glass top, the cab's roof with its dish, whips and radome, the coamings either side of the open salvage bay with their floodlights, and engineering's vents and skylights.", color: Skin);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var glass = zb <= ZCk + 1e-6;
            var bay = za >= BayZ0 - 1e-6 && zb <= BayZ1 + 1e-6;
            if (glass)
                Facet(p, 5, 1, za, zb, Glass, glass: true);
            else if (bay)
            {
                // (the bay is open to the sky: the roof here is a strip each side, from the shoulder in to the bay's wall)
                var h4 = At(za)[4].H;
                var vin = (h4 - PassIn - 0.004) / (2 * h4);
                RoofStrip(p, za, zb, 0, vin, Skin2);
                RoofStrip(p, za, zb, 1 - vin, 1, Skin2);
            }
            else
            {
                Facet(p, 5, 1, za, zb, Skin2, "painted plating");
                if (zb - za > 1.5)
                    Plates(p, 5, 1, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), 7 + i, 0.12, 0.88);
            }
            foreach (var s in new[] { -1, 1 })
            {
                if (glass)
                    Facet(p, 4, s, za, zb, Glass, glass: true);
                else
                    Band(p, 4, s, za, zb, Armour, 4 + i);
                Chine(p, 4, s, za, zb, glass ? 0.07 : 0.16, glass ? 0.05 : 0.08, glass ? Dark : Frame);
            }
        }
        Ribs(p, ZCk, ZEngEnd, 4, 5);
        // The canopy's frame: hoops at the stations and a spine bar down its middle.
        foreach (var z in new[] { ZS1, ZCk })
        {
            Slab(p, 5, 1, z - 0.06, z + 0.06, 0, 1, 0, 1, 0.01, 0.07, 0.015, Dark, "painted steel");
            foreach (var s in new[] { -1, 1 })
                Slab(p, 4, s, z - 0.06 - FacetStagger, z + 0.06 + FacetStagger, 0, 1, 0, 1, 0.01, 0.07, 0.015, Dark, "painted steel");
        }
        foreach (var (za, zb) in new[] { (Zt + 0.1, ZS1 - 0.07), (ZS1 + 0.07, ZCk - 0.07) })
            FStrip(p, 5, 1, za, zb, 0, 1, 0.47, 0.53, 0.03, Dark, "painted steel");
        double Top(double z) => At(z)[4].Y;
        // Over the cab (Karl's picture): a dish on a post, whip antennas with red tips, a radome, an equipment box and a skylight.
        double mz = -9.6, my = Top(mz);
        Solid(p, Cx - 0.45, my - 0.12, mz - 0.45, Cx + 0.45, my + 0.18, mz + 0.45, Frame, "dirty steel");
        Detail(p, Cx - 0.08, my + 0.18, mz - 0.08, Cx + 0.08, my + 0.95, mz + 0.08, Bare, "painted steel");
        Prism(p, Ngon(Cx, my + 0.92, mz, 0.12, 14), Ngon(Cx, my + 1.25, mz, 0.85, 14), Paper, "painted steel");
        Detail(p, Cx - 0.03, my + 1.0, mz - 0.03, Cx + 0.03, my + 1.6, mz + 0.03, Frame, "painted steel");
        Detail(p, Cx - 0.06, my + 1.6, mz - 0.06, Cx + 0.06, my + 1.7, mz + 0.06, Red, glow: true);
        foreach (var (dx, dz, h) in new[] { (-1.6, -11.9, 1.8), (1.6, -11.9, 1.4), (-1.3, -7.3, 2.3), (2.3, -10.6, 1.1) })
        {
            Detail(p, Cx + dx - 0.12, Top(dz) - 0.01, dz - 0.12, Cx + dx + 0.12, Top(dz) + 0.124, dz + 0.12, Frame, "dirty steel");
            Detail(p, Cx + dx - 0.02, Top(dz) + 0.124, dz - 0.02, Cx + dx + 0.02, Top(dz) + h, dz + 0.02, Dark, "painted steel");
            Detail(p, Cx + dx - 0.045, Top(dz) + h, dz - 0.045, Cx + dx + 0.045, Top(dz) + h + 0.1, dz + 0.045, Red, glow: true);
        }
        double rx = Cx + 1.7, rz = -7.6, ry = Top(rz);
        foreach (var (y0, r0, y1, r1, col) in new[] { (0.0, 0.62, 0.12, 0.62, Frame), (0.12, 0.58, 0.4, 0.56, Paper), (0.4, 0.56, 0.6, 0.44, Paper), (0.6, 0.44, 0.72, 0.24, Paper), (0.72, 0.24, 0.76, 0.06, Paper) })
            Prism(p, Ngon(rx, ry + y0 + 0.002, rz, r0, 14), Ngon(rx, ry + y1, rz, r1, 14), col, "painted plating");
        Solid(p, Cx - 2.5, ry - 0.05, -10.9, Cx - 1.7, ry + 0.45, -9.9, Bare, "painted steel");
        for (var k = 0; k < 4; k++)
            Detail(p, Cx - 2.45, ry + 0.45, -10.8 + k * 0.24, Cx - 1.75, ry + 0.48, -10.7 + k * 0.24, Dark, "dirty steel");
        Detail(p, Cx - 0.7, ry - 0.01, -8.6, Cx + 0.7, ry + 0.2, -6.6, Frame, "dirty steel");
        Detail(p, Cx - 0.6, ry + 0.2, -8.5, Cx + 0.6, ry + 0.216, -6.7, Glass, "glass");
        // Navigation lights on the cab's shoulders: red to port, green to starboard.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 1.9 - 0.1, Top(-12.4) - 0.01, -12.5, Cx + s * 1.9 + 0.1, Top(-12.4) + 0.12, -12.3, s < 0 ? Red : Green, glow: true);
        // The bay's coamings: a hazard edge along each roof strip's inner edge, floodlights at the bay's four corners for loading,
        // amber loaded lamps between them, red latch blocks where a spreader's guide pins seat.
        var by = Top(0);
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * (PassIn + 0.012), by + 0.016, BayZ0 + 0.3, Cx + s * (PassIn + 0.14), by + 0.03, BayZ1 - 0.3, Hazard, "hazard paint");
            foreach (var z in new[] { BayZ0 + 0.55, BayZ1 - 0.55 })
            {
                Detail(p, Cx + s * (PassIn + 0.006), by + 0.032, z - 0.16, Cx + s * (PassIn + 0.14), by + 0.27, z + 0.16, Frame, "dirty steel");
                Detail(p, Cx + s * PassIn, by + 0.09, z - 0.12, Cx + s * (PassIn + 0.006), by + 0.23, z + 0.12, Glow, glow: true);
            }
            foreach (var z in new[] { -1.6, 1.6 })
            {
                Detail(p, Cx + s * (PassIn + 0.006), by + 0.032, z - 0.22, Cx + s * (PassIn + 0.14), by + 0.17, z + 0.22, Latch, "worn paint");
                Detail(p, Cx + s * (PassIn + 0.04), by + 0.17, z - 0.05, Cx + s * (PassIn + 0.1), by + 0.21, z + 0.05, "#ffb040", glow: true);
            }
        }
        // Over engineering: two vent housings with dark slats, a skylight down the middle, an orange working beacon.
        var ey = Top(ZEngMid);
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * 1.4 - 0.45, ey - 0.05, 4.6, Cx + s * 1.4 + 0.45, ey + 0.38, 6.6, Bare, "painted steel");
            for (var k = 0; k < 6; k++)
                Detail(p, Cx + s * 1.4 - 0.4, ey + 0.38, 4.7 + k * 0.32, Cx + s * 1.4 + 0.4, ey + 0.41, 4.86 + k * 0.32, Dark, "dirty steel");
        }
        Detail(p, Cx - 0.6, ey - 0.01, 7.2, Cx + 0.6, ey + 0.2, 9.6, Frame, "dirty steel");
        Detail(p, Cx - 0.5, ey + 0.2, 7.3, Cx + 0.5, ey + 0.216, 9.5, Glass, "glass");
        Detail(p, Cx - 0.1, ey - 0.01, 10.0, Cx + 0.1, ey + 0.25, 10.2, Frame, "dirty steel");
        Detail(p, Cx - 0.08, ey + 0.25, 10.02, Cx + 0.08, ey + 0.38, 10.18, Orange, glow: true);
        // The bow's up jets.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsUp" + (s < 0 ? "L" : "R"), Cx + s * 1.2, Top(BowRcsZ), BowRcsZ, "+y", 0.056); // (its top clear of the roof's rib)
        // The void over the ceilings (beside the bay, from its walls out).
        foreach (var (_, za, zb) in BandsIn(ZCk, ZEngEnd))
        {
            var bay = za >= BayZ0 - 1e-6 && zb <= BayZ1 + 1e-6;
            var floor = za >= ZEng - 1e-6 ? ERoof : za >= ZCabEnd - 1e-6 ? SRoof : CRoof;
            var z0 = Math.Max(za, ZCk + 0.25);
            VoidT(p, z0, zb, floor, Math.Min(Top(z0), Top(zb)) - 0.03, bay ? PassIn + 0.02 : 0, z0 > za ? 0.0 : 0.004, 0.004);
        }
        return p.Stats(9000, 30000, 35, 15.0);
    }

    // ---- the cab: the glass cockpit, then two rows of rooms off a corridor; its hull bands

    private static Part Cab()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Grapnel Cab", "The Grapnel's cab: the glass cockpit forward, then the crew's rooms off a short corridor: the bunk room and the head to port, the galley and the airlock over the boarding shaft to starboard.");
        Rect ckA = new(Cx - 1.0, ZCkFloor, Cx + 1.0, ZCkFloor + 1.0), ckB = new(Cx - 1.6, ZCkFloor + 1.0, Cx + 1.6, ZCkW - 0.125), cab = new(Cx - Lw - 0.25, ZCkW - 0.125, Cx + Lw + 0.25, ZCabW - 0.125);
        Floor(p, [ckA, ckB, cab], 0, Ft, Deck, "painted deck plate", BellyHatch);
        Floor(p, [new Rect(cab.X0, ZCkW + 0.125, cab.X1, ZCabW - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Room(p, new Rect(Cx - 1.6, ZCkFloor - 0.4, Cx + 1.6, ZCkW), 0, 3.3);
        Room(p, cab, 0, CRoof);
        Ceils[p] = CCeil;
        // The cockpit: a mat, the dash under the windscreen with its screens and switches, two couches and the flight computer.
        const double D = 9.5; // (the Packet's cockpit, moved aft by this much)
        Detail(p, Cx - 1.5, Ft, -24.95 + D, Cx + 1.5, Ft + 0.012, ZCkW - 0.25, "#30353b", "rubber matting");
        Solid(p, Cx - 0.95, Ft, ZCkFloor, Cx + 0.95, 0.95, -25.55 + D, Dark, "plastic");
        foreach (var (x0, x1, col) in new[] { (-0.85, -0.3, "#78c8e8"), (-0.22, 0.22, "#60e090"), (0.3, 0.85, "#78c8e8") })
            Detail(p, Cx + x0, 0.95, -25.98 + D, Cx + x1, 0.962, -25.6 + D, col, glow: true);
        for (var i = 0; i < 7; i++)
            Detail(p, Cx - 0.6 + i * 0.2, 0.6, -25.553 + D, Cx - 0.52 + i * 0.2, 0.68, -25.54 + D, i % 3 == 0 ? Hazard : Paper, "plastic");
        // The canopy's backing (what stops a body at the glass), the void under the side windows, the nose's void.
        foreach (var (za, zb) in new[] { (ZCkFloor, ZS1), (ZS1, ZCkW - 0.125) })
        {
            var hx = new[] { SkinHalf(za, 0.95), SkinHalf(za, 2.4), SkinHalf(zb, 0.95), SkinHalf(zb, 2.4) }.Min() - 0.1;
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * (hx - 0.08), 0.95, za + 0.004, Cx + s * hx, 2.4, zb - 0.004);
            var ty = Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.12;
            var tw = Math.Min(At(za)[4].H, At(zb)[4].H);
            Hidden(p, Cx - tw, ty - 0.08, za + 0.004, Cx + tw, ty, zb - 0.004);
            VoidT(p, za, zb, za == ZCkFloor ? 0 : 0.1, 0.95, za == ZCkFloor ? 1.0 : 1.6, 0.004, 0.004); // (aft, from 0.1 up: the skin is narrower under it)
        }
        Hidden(p, Cx - 0.9, Ft, ZCkFloor - 0.45, Cx + 0.9, 1.9, ZCkFloor - 0.004);
        // The cockpit's back wall, the cab's sides, the corridor's walls with the rooms' doors, the rooms' cross walls, the aft wall
        // (the whole width: the cross passage behind it reaches the side passages).
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCkW, Ft, CCeil, Door(Cx - 0.75));
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + s * (Lw + 0.125), Ft, CCeil);
        WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx - 0.875, Ft, CCeil, Door(-11.6), Door(-8.3));
        WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + 0.875, Ft, CCeil, Door(-12.4), Door(-7.1));
        WallRun(p, true, Cx - Lw, Cx - 1.0, ZPortW, Ft, CCeil);
        WallRun(p, true, Cx + 1.0, Cx + Lw, ZStbdW, Ft, CCeil);
        WallRun(p, true, Cx - PassOut - SLine, Cx + PassOut + SLine, ZCabW, Ft, CCeil, Door(Cx - 0.75));
        // The airlock: a rail round the boarding shaft, the cycle lights over its door.
        HatchRail(p, BellyHatch, Ft);
        Detail(p, Cx + 1.0, Ft + DoorH + 0.15, -6.5, Cx + 1.015, Ft + DoorH + 0.3, -6.2, Red, glow: true);
        Detail(p, Cx + 0.735, Ft + DoorH + 0.15, -6.5, Cx + 0.75, Ft + DoorH + 0.3, -6.2, "#40e070", glow: true);
        // The corridor: a grating runner, a light strip.
        Detail(p, Cx - 0.6, Ft, ZCkW + 0.3, Cx + 0.6, Ft + 0.012, ZCabW - 0.3, Grate, "painted grating");
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZCkW + 0.6, ZCabW - 0.6, CCeil);
        // The hull: the cockpit's glass sides and the windscreen, then the cab's bands with the livery and portholes.
        foreach (var s in new[] { -1, 1 })
        {
            Facet(p, 2, s, Zt, ZS1, Armour, "plated steel");
            Facet(p, 3, s, Zt, ZS1, Glass, glass: true);
            Band(p, 2, s, ZS1, ZCk, Skin, 2, plates: false);
            Facet(p, 3, s, ZS1, ZCk, Glass, glass: true);
            Chine(p, 2, s, Zt, ZS1, 0.07, 0.05, Dark);
            Chine(p, 3, s, Zt, ZS1, 0.07, 0.05, Dark);
            Chine(p, 2, s, ZS1, ZCk, 0.3, 0.12, Dark);
            Chine(p, 3, s, ZS1, ZCk, 0.07, 0.05, Dark);
            Slab(p, 2, s, ZS1, ZCk, 0.1, 0.9, 0.12, 0.88, 0.02, 0.1, 0.05, Armour);
            foreach (var (_, za, zb) in BandsIn(ZCk, ZCabEnd))
            {
                var neck = za >= ZNeck - 1e-6;
                var cut = neck ? 0.012 : 0.0; // (a neck's plates lean past their ends: its rails stop short of them)
                Band(p, 2, s, za, zb, Skin, 3);
                Band(p, 3, s, za, zb, Skin, 5, plates: false);
                Chine(p, 2, s, za + cut, zb - cut, 0.3, 0.12, Dark);
                Chine(p, 3, s, za + cut, zb - cut, 0.18, 0.08, Frame);
            }
            // Portholes in the upper sides: the bunk room and the galley, the head and the airlock.
            foreach (var z in new[] { -12.2, -10.2, -8.0 })
            {
                var (za, zb) = z < ZCab ? (ZCk, ZCab) : (ZCab, ZNeck);
                double len = zb - za, u = (z - za) / len, du = 0.32 / len;
                Slab(p, 3, s, za, zb, u - du - 0.06 / len, u + du + 0.06 / len, 0.38, 0.72, 0.02, 0.06, 0.02, Dark, "painted steel");
                FStrip(p, 3, s, za, zb, u - du, u + du, 0.43, 0.67, 0.062, "#24384a", "glass");
            }
            // The registry on the cab's flank: a white plate with an orange salvage square.
            Slab(p, 3, s, ZCab, ZNeck, 0.55, 0.9, 0.35, 0.8, 0.02, 0.05, 0.02, Paper, "painted plating");
            Slab(p, 3, s, ZCab, ZNeck, 0.58, 0.64, 0.42, 0.73, 0.05, 0.07, 0.01, Orange, "painted plating");
        }
        Ribs(p, ZCk, ZCabEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCk, ZCabEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, CRoof) })
                VoidT(p, za, zb, ya, yb, Lw + 0.25, za == ZCk ? 0.25 : 0.004, zb == ZCabEnd ? 0.25 : 0.004);
        // The bow's out jets on the cab's flanks.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsOut" + (s < 0 ? "L" : "R"), FacetX(2, s, BowRcsZ, 0.8), 0.8, BowRcsZ, s < 0 ? "-x" : "+x", 0.28);
        // Doors.
        DoorSock(p, "doorCk", true, Cx - 0.75, ZCkW, Ft);
        DoorSock(p, "doorAft", true, Cx - 0.75, ZCabW, Ft);
        DoorSock(p, "doorBunk", false, -11.6, Cx - 0.875, Ft);
        DoorSock(p, "doorHead", false, -8.3, Cx - 0.875, Ft);
        DoorSock(p, "doorGalley", false, -12.4, Cx + 0.875, Ft);
        DoorSock(p, "doorAirlock", false, -7.1, Cx + 0.875, Ft);
        // The cockpit's couches and computer, its lamps and their switch on the back wall.
        Sock(p, "pilot", Cx - 0.8, Ft, -23.9 + D, "+y");
        Sock(p, "copilot", Cx + 0.8, Ft, -23.9 + D, "+y");
        Sock(p, "computer", Cx, Ft, -25.0 + D, "+y");
        Sock(p, "lampCkL", Cx - 1.4, Ft + 2.2, ZCkW - 0.125, "-z");
        Sock(p, "lampCkR", Cx + 1.4, Ft + 2.2, ZCkW - 0.125, "-z");
        Sock(p, "switchCk", Cx + 1.1, Ft + 1.3, ZCkW - 0.125, "-z");
        // The bunk room: two bunks along the outer wall, a locker by the door.
        Sock(p, "bunk0", Cx - Lw + 0.55, Ft, -11.95, "+y");
        Sock(p, "bunk1", Cx - Lw + 0.55, Ft, -9.85, "+y");
        Sock(p, "lockerBunk", Cx - 1.5, Ft + 0.125, ZPortW - 0.125, "-z");
        Sock(p, "lampBunk", Cx - Lw, Ft + 2.2, -10.9, "+x");
        Sock(p, "switchBunk", Cx - 1.0, Ft + 1.3, -11.75, "-x");
        // The head: the toilet and the basin on the outer wall, the shower in the aft corner.
        Sock(p, "toilet", Cx - Lw, Ft + 0.125, -8.0, "+x");
        Sock(p, "basin", Cx - Lw, Ft + 0.125, -7.15, "+x");
        Sock(p, "shower", Cx - 2.7, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "lampHead", Cx - Lw, Ft + 2.3, -6.6, "+x");
        Sock(p, "switchHead", Cx - 1.0, Ft + 1.3, -8.45, "-x");
        // The galley: the range and the coffee station on the forward wall, the mess table along the outer wall.
        Sock(p, "range", Cx + 2.5, Ft + 0.45, ZCkW + 0.125, "+z");
        Sock(p, "coffee", Cx + 1.55, Ft + 0.125, ZCkW + 0.125, "+z");
        Sock(p, "table", Cx + 2.6, Ft, -11.0, "+y");
        Sock(p, "lampGalley", Cx + Lw, Ft + 2.2, -11.6, "-x");
        Sock(p, "switchGalley", Cx + 1.0, Ft + 1.3, -12.55, "+x");
        // The airlock: suit lockers on the outer wall, the ship's air on the aft wall, the hatch's switch, the belly hatch's hanging.
        Sock(p, "lifeSupport", Cx + 2.85, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "o2Tank0", Cx + 2.0, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "suit0", Cx + Lw, Ft + 0.125, -7.9, "-x");
        Sock(p, "suit1", Cx + Lw, Ft + 0.125, -6.4, "-x");
        Sock(p, "switchHatch", Cx + 1.0, Ft + 1.45, -7.25, "+x");
        Sock(p, "lampAirlock", Cx + Lw, Ft + 2.3, -8.9, "-x");
        Sock(p, "boarding", HatchX, Ft - 0.125, BellyHatch.Z1, "-z");
        // The corridor's lamps.
        Sock(p, "lampHallA", Cx - 0.75, Ft + 2.3, -10.0, "+x");
        Sock(p, "lampHallB", Cx + 0.75, Ft + 2.3, -8.5, "-x");
        foreach (var (what, z, hx) in new[] { ("cockpit front", ZCkFloor, 1.0), ("cockpit", ZCkFloor + 1.0, 1.6), ("cab front", ZCkW - 0.125, Lw + 0.25), ("cab aft", ZNeck, Lw + 0.25), ("cab's aft wall", ZCabEnd, PassOut + SLine) })
            foreach (var y in new[] { 0.0, z < ZCkW - 0.2 ? 0.95 : CRoof })
                MustFit(what, z, hx, y);
        return p.Stats(16000, 32000, 30, 18.0);
    }

    // ---- the bay: two container cells sunk into the hull amidships, open to the sky, between two side passages; the cross passage
    // forward of it from the cab

    /// <summary>The hull's lining as a passage's or engineering's outer wall (side s, from a0 to a1, up to top): pale over a blue dado,
    /// a handrail on standoffs, a frame post at its forward end, cable trays and pipes up high.</summary>
    private static void Lining(Part p, int s, double a0, double a1, double top, double ceil, bool first, bool last)
    {
        double l0 = a0 + (first ? 0.0 : 0.004), l1 = a1 - (last ? 0.0 : 0.004);
        var face = Cx + s * PassOut;
        double xa = Math.Min(face, face + s * SLine), xb = Math.Max(face, face + s * SLine);
        Solid(p, xa, 0, l0, xb, Ft + 0.9, l1, WallLow, "painted plating");
        Solid(p, xa, Ft + 0.9, l0, xb, top, l1, Wall, "painted plating");
        Detail(p, face - s * 0.09, Ft + 1.0, a0 + 0.25, face - s * 0.05, Ft + 1.05, a1 - 0.25, Hazard, "worn paint");
        for (var z = a0 + 0.5; z < a1 - 0.3; z += 1.5)
            Detail(p, face - s * 0.06, Ft + 1.0, z - 0.03, face - s * 0.004, Ft + 1.04, z + 0.03, Frame, "painted steel");
        Detail(p, face - s * 0.12, Ft + 0.004, a0 + 0.008, face - s * 0.004, ceil - 0.004, a0 + 0.16, Frame, "dirty steel");
        Detail(p, face - s * 0.02, Ft + 0.9 - 0.03, l0 + 0.004, face - s * 0.004, Ft + 0.9 + 0.03, l1 - 0.004, WallTrim, "painted steel");
        if (s < 0)
            foreach (var (y, col) in new[] { (2.15, Red), (2.28, Link), (2.41, Bare) })
                Detail(p, face - s * 0.09, y, a0 + 0.2, face - s * 0.014, y + 0.07, a1 - 0.03, col, "painted steel");
        else
            Detail(p, face - s * 0.16, 2.2, a0 + 0.2, face - s * 0.004, 2.24, a1 - 0.03, Frame, "dirty steel");
    }

    private static Part Bay()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Grapnel Bay", "The Grapnel's salvage bay: two standard 2 m containers side by side for sorted salvage, in cells sunk into the hull, open to the sky so a dock's crane lifts each one straight up between its cell guides; a passage down each side of it, a cross passage from the cab, and the tank racks' hardpoints on its flanks.");
        Rect cross = new(Cx - PassOut, ZCabEnd, Cx + PassOut, BayZ0), bay = new(Cx - PassIn, BayZ0, Cx + PassIn, BayZ1);
        Rect passL = new(Cx - PassOut, BayZ0, Cx - PassIn, BayZ1), passR = new(Cx + PassIn, BayZ0, Cx + PassOut, BayZ1);
        Floor(p, [cross, passL, passR], 0, Ft, Deck, "painted deck plate");
        Floor(p, [bay], 0, Ft, RackCol, "dirty steel");
        Floor(p, [cross, passL, passR], SCeil, SRoof, Ceil, "painted plating");
        Room(p, new Rect(Cx - PassOut + 0.1, ZCabEnd, Cx + PassOut - 0.1, BayZ0), 0, SRoof);
        foreach (var s in new[] { -1, 1 })
            Room(p, new Rect(s < 0 ? Cx - PassOut + 0.1 : Cx + PassIn, BayZ0, s < 0 ? Cx - PassIn : Cx + PassOut - 0.1, BayZ1), 0, SRoof);
        Room(p, bay, 0, 4.1);
        Ceils[p] = SCeil;
        // The passages' runners and light strips.
        Detail(p, Cx - 3.3, Ft, ZCabEnd + 0.2, Cx + 3.3, Ft + 0.012, BayZ0 - 0.2, Grate, "painted grating");
        CeilStrip(p, Cx - 3.0, Cx + 3.0, (ZCabEnd + BayZ0) / 2 - 0.1, (ZCabEnd + BayZ0) / 2 + 0.1, SCeil);
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * (PassIn + 0.2) - 0.3, Ft, BayZ0 + 0.2, Cx + s * (PassIn + 0.2) + 0.3, Ft + 0.012, BayZ1 - 0.2, Grate, "painted grating");
            CeilStrip(p, Cx + s * 3.5 - 0.1, Cx + s * 3.5 + 0.1, BayZ0 + 0.5, BayZ1 - 0.5, SCeil);
        }
        // The bay's walls: its bulkheads fore and aft, a wall down each side; the bay's faces are bare rack steel with stiffeners, the
        // bulkheads carry the cell guides that steer a container down onto its locks, and a hazard coaming caps every wall.
        foreach (var sz in new[] { -1, 1 })
        {
            var c = sz * (BayIn + 0.125);
            WallRun(p, true, Cx - PassIn, Cx + PassIn, c, Ft, BayTop);
            OnWall(p, () =>
            {
                double f0 = sz * (BayIn - 0.025), f1 = sz * (BayIn - 0.013); // (in front of the dado on the bay's face)
                Detail(p, Cx - BayHx + 0.01, Ft + 0.004, f0, Cx + BayHx - 0.01, BayTop - 0.004, f1, RackCol, "dirty steel");
                Detail(p, Cx - PassIn - 0.004, BayTop, c - 0.14, Cx + PassIn + 0.004, BayTop + 0.06, c + 0.14, Hazard, "hazard paint");
                // The cell guides: an angle at each container's outer corner, one between the two, their heads yellow.
                foreach (var (x0, x1) in new[] { (-2.72, -2.52), (-0.25, 0.25), (2.52, 2.72) })
                {
                    double g0 = sz * (BayIn - 0.09), g1 = sz * (BayIn - 0.026);
                    Detail(p, Cx + x0, Ft + 0.004, Math.Min(g0, g1), Cx + x1, BayTop - 0.25, Math.Max(g0, g1), Frame, "dirty steel");
                    Detail(p, Cx + x0 - 0.004, BayTop - 0.25, Math.Min(g0, g1) - 0.004, Cx + x1 + 0.004, BayTop - 0.004, Math.Max(g0, g1) + 0.004, Hazard, "hazard paint");
                }
                // A cell's stencil over each container's roof (BAY 01 PORT, BAY 02 STBD): a white plate with an orange square.
                if (sz < 0)
                    foreach (var xs in new[] { -1, 1 })
                    {
                        double b0 = sz * (BayIn - 0.03), b1 = sz * (BayIn - 0.0255);
                        Detail(p, Cx + xs * CtX - 0.45, 2.95, Math.Min(b0, b1), Cx + xs * CtX + 0.45, 3.3, Math.Max(b0, b1), Paper, "painted plating");
                        Detail(p, Cx + xs * CtX - 0.38, 3.0, Math.Max(b0, b1) + 0.001, Cx + xs * CtX - 0.2, 3.25, Math.Max(b0, b1) + 0.005, Orange, "painted plating");
                    }
            });
        }
        foreach (var s in new[] { -1, 1 })
        {
            var c = s * (BayHx + 0.125);
            WallRun(p, false, -BayIn, BayIn, c, Ft, BayTop);
            OnWall(p, () =>
            {
                double f0 = s * (BayHx - 0.025), f1 = s * (BayHx - 0.013);
                Detail(p, Math.Min(f0, f1), Ft + 0.004, -BayIn + 0.1, Math.Max(f0, f1), BayTop - 0.004, BayIn - 0.1, RackCol, "dirty steel");
                foreach (var y in new[] { 1.05, 1.95, 2.85 })
                    Detail(p, Math.Min(f1 - s * 0.045, f1 - s * 0.005), y, -BayIn + 0.15, Math.Max(f1 - s * 0.045, f1 - s * 0.005), y + 0.08, BayIn - 0.15, Frame, "dirty steel");
                Detail(p, c - 0.14, BayTop + 0.004, -BayIn - 0.004, c + 0.14, BayTop + 0.066, BayIn + 0.004, Hazard, "hazard paint"); // (over the bulkheads' coamings at the corners)
            });
        }
        // The twist locks under the containers' corner castings: red pads on the bay's floor (their tops take the containers' feet),
        // yellow lines round each container's footprint.
        for (var si = 0; si < 2; si++)
        {
            var s = si == 0 ? -1 : 1;
            var n = s < 0 ? "L" : "R";
            var xc = Cx + s * CtX;
            var k = 0;
            foreach (var dx in new[] { -1, 1 })
                foreach (var dz in new[] { -1, 1 })
                {
                    double x = xc + dx * CastX, z = dz * CastZ;
                    Solid(p, x - 0.17, Ft, z - 0.17, x + 0.17, Ft + LockH, z + 0.17, Latch, "worn paint");
                    Detail(p, x - 0.05, Ft + LockH, z - 0.09, x + 0.05, Ft + LockH + 0.015, z + 0.09, Frame, "steel");
                    p.Sock($"lock{n}{k++}", P(x, Ft + LockH, z), "+y", Medium); // (a container is a medium part)
                }
            foreach (var dx in new[] { -1, 1 })
                Detail(p, xc + dx * (CtW / 2 + 0.03) - 0.03, Ft, -CtL / 2 + 0.2, xc + dx * (CtW / 2 + 0.03) + 0.03, Ft + 0.008, CtL / 2 - 0.2, Hazard, "hazard paint");
        }
        // The lining down the passages' outer sides and the cross passage's ends.
        foreach (var s in new[] { -1, 1 })
        {
            Lining(p, s, ZCabEnd, BayZ0, SRoof, SCeil, true, false);
            Lining(p, s, BayZ0, BayZ1, SRoof, SCeil, false, true);
            // The frame's beam over each passage at the bay's ends.
            foreach (var z in new[] { BayZ0, 0.0 })
                Detail(p, Cx + s * (PassIn + 0.004), SCeil - 0.14, z + 0.012, Cx + s * (PassOut - 0.124), SCeil - 0.004, z + 0.152, Frame, "dirty steel");
        }
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZCabEnd, ZEng))
            {
                Band(p, 2, s, za, zb, Skin, 11 + i);
                Band(p, 3, s, za, zb, Skin, 12 + i, plates: false);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.3, 0.12, Dark);
                Chine(p, 3, s, za, zb, 0.18, 0.08, Frame);
            }
        Ribs(p, ZCabEnd, ZEng, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCabEnd, ZEng))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, SRoof) })
                VoidT(p, za, zb, ya, yb, PassOut + SLine + 0.004, za == ZCabEnd ? 0.0 : 0.004, zb == ZEng ? 0.0 : 0.004);
        // The lamps: the cross passage's, a pair down each passage; the engineering board and its switch on the aft bulkhead's back.
        Sock(p, "lampCrossL", Cx - PassOut, Ft + 2.0, (ZCabEnd + BayZ0) / 2, "+x");
        Sock(p, "lampCrossR", Cx + PassOut, Ft + 2.0, (ZCabEnd + BayZ0) / 2, "-x");
        foreach (var s in new[] { -1, 1 })
            foreach (var (z, k) in new[] { (-1.8, 0), (1.8, 1) })
                Sock(p, $"lamp{(s < 0 ? "L" : "R")}{k}", Cx + s * PassOut, Ft + 2.0, z, s < 0 ? "+x" : "-x");
        // On the aft bulkhead's back (engineering's forward wall): the power board, the MMU dock, a suit locker and a pressure locker.
        Sock(p, "board", Cx - 2.2, Ft + 0.9, BayZ1, "+z");
        Sock(p, "switchEng", Cx + 0.75, Ft + 1.3, BayZ1, "+z");
        Sock(p, "mmuDock", Cx - 0.6, Ft + 0.125, BayZ1, "+z");
        Sock(p, "locker", Cx + 1.5, Ft + 0.125, BayZ1, "+z");
        Sock(p, "suit", Cx + 2.35, Ft + 0.125, BayZ1, "+z");
        // The tank racks' hardpoints on the flanks.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * (PassOut + SLine + 0.004), RackY - 0.32, -0.6, Cx + s * RackX, RackY + 0.32, 0.6, Frame, "dirty steel");
            Sock(p, s < 0 ? "rackL" : "rackR", Cx + s * RackX, RackY, 0, s < 0 ? "-x" : "+x");
        }
        foreach (var z in new[] { ZCabEnd, 0.0, BayZ1 })
            foreach (var y in new[] { 0.0, SRoof })
                MustFit("passages", z, PassOut + SLine, y);
        return p.Stats(14000, 30000, 30, 15.0);
    }

    // ---- engineering: the plant, the batteries, the Engineer's Panel, the ship's air and the space-walk gear for the nacelles

    private static Part Engineering()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Grapnel Engineering", "The Grapnel's engineering, open to both passages: the power plant and the batteries forward, and aft the two drives' engines, a Drive Core and a Fuel Pump each, bolted to the stern bulkhead with a walkway down the middle and along each side.");
        var plan = new Rect(Cx - PassOut, ZEng, Cx + PassOut, ZEngEnd);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate");
        Floor(p, [plan], ECeil, ERoof, Ceil, "painted plating");
        Room(p, new Rect(plan.X0 + 0.2, ZEng, plan.X1 - 0.2, ZEngEnd), 0, ERoof);
        Ceils[p] = ECeil;
        // The walkway between the engines: a grating runner with hazard edges, and a keep-clear line outboard of each engine.
        double e0 = ZEngEnd - ShipWorksParts.CoreD[1] - ShipWorksParts.PumpD[1]; // (the engines' forward end)
        Detail(p, Cx - 0.75, Ft, ZEng + 0.3, Cx + 0.75, Ft + 0.012, ZEngEnd - 0.3, Grate, "painted grating");
        foreach (var x in new[] { -0.79, 0.75 })
            Detail(p, Cx + x, Ft + 0.004, ZEng + 0.3, Cx + x + 0.04, Ft + 0.016, ZEngEnd - 0.3, Hazard, "hazard paint");
        foreach (var dx in DriveX)
        {
            var xo = Cx + dx + Math.Sign(dx) * (ShipWorksParts.CoreR[1] + 0.12);
            Detail(p, xo - 0.02, Ft + 0.004, e0 - 0.2, xo + 0.02, Ft + 0.012, ZEngEnd - 0.1, Hazard, "hazard paint");
            Detail(p, Cx + dx - 1.0, Ft + 0.004, e0 - 0.24, Cx + dx + 1.0, Ft + 0.012, e0 - 0.2, Hazard, "hazard paint");
        }
        foreach (var s in new[] { -1, 1 })
        {
            Lining(p, s, ZEng, ZEngMid, ERoof, ECeil, true, false);
            Lining(p, s, ZEngMid, ZEngEnd, ERoof, ECeil, false, true);
            var face = Cx + s * PassOut;
            // The fuel feed down the lining from the stern tank to the engines: a red pipe, a valve wheel and a gauge.
            Detail(p, face - s * 0.12, Ft + 0.004, 9.9 - 0.05, face - s * 0.02, ECeil - 0.004, 9.9 + 0.05, Red, "painted steel");
            Detail(p, face - s * 0.16, 1.1, 9.6, face - s * 0.12, 1.4, 9.9 - 0.06, Red, "painted steel");
            Detail(p, face - s * 0.2, 1.75, 9.3, face - s * 0.004, 2.25, 9.7, Frame, "dirty steel");
            Detail(p, face - s * 0.205, 1.85, 9.4, face - s * 0.2, 2.15, 9.6, Paper, "plastic");
        }
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZEng + 0.6, ZEngEnd - 0.6, ECeil);
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZEng, ZEngEnd))
            {
                Band(p, 2, s, za, zb, Skin, 21 + i);
                Band(p, 3, s, za, zb, Skin, 22 + i, plates: false);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.3, 0.12, Dark);
                Chine(p, 3, s, za, zb, 0.18, 0.08, Frame);
            }
        Ribs(p, ZEng, ZEngEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZEng, ZEngEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, ERoof) })
                VoidT(p, za, zb, ya, yb, PassOut + SLine + 0.004, za == ZEng ? 0.0 : 0.004, zb == ZEngEnd ? 0.0 : 0.004);
        Sock(p, "panel", Cx - PassOut, Ft + 0.6, 8.6, "+x");
        Sock(p, "tools", Cx + PassOut, Ft + 0.9, 8.6, "-x");
        Sock(p, "plant", Cx - 2.5, Ft, 5.5, "+y"); // (to port, forward of its engine)
        for (var i = 0; i < 3; i++)
            Sock(p, $"battery{i}", Cx + 2.4, Ft, 4.8 + i * 0.6, "+y");
        Sock(p, "lampEngL", Cx - PassOut, Ft + 2.3, 6.4, "+x");
        Sock(p, "lampEngR", Cx + PassOut, Ft + 2.3, 6.4, "-x");
        Sock(p, "lampEngA", Cx - PassOut, Ft + 2.3, 9.0, "+x");
        foreach (var z in new[] { plan.Z0, ZEngMid, plan.Z1 })
            foreach (var y in new[] { 0.0, ERoof })
                MustFit("engineering", z, PassOut + SLine, y);
        return p.Stats(14000, 30000, 30, 15.0);
    }

    // ---- the bow and the stern: the chin with its tool mounts; the transom with the drives, their bulkhead, the stern's jets

    /// <summary>The bow: the chin's flat yellow face up to the beam with the windscreen over it, and on the face three tool mounts:
    /// bare hardpoints whose sockets take whatever a crew welds on (an arm on a hinge, a grab, a cutter), through the game's own
    /// sockets (Karl, 2026-11-21: the claws and the cutter came off; the mounts are for the socket system).</summary>
    private static Part Bow()
    {
        var p = Big("Grapnel Bow", "The Grapnel's bow: the chin's flat face under the windscreen, with three tool mounts on it: hardpoints that take whatever a crew welds on (an arm on a hinge, a grab, a cutter).", color: Armour);
        var q = At(Zt);
        const double fz = Zt - 0.012; // (the face's plane, in front of the facets' ends)
        double[] F(int k, int sd) => P(Cx + sd * q[k].H, q[k].Y, fz);
        Plate(p, [F(0, 1), F(1, 1), F(2, 1), F(2, -1), F(1, -1), F(0, -1)], Armour, "plated steel");
        Plate(p, [F(2, 1), F(3, 1), F(4, 1), F(4, -1), F(3, -1), F(2, -1)], Glass, glass: true);
        // The windscreen's frame: a sill along the beam, two mullions, a hood bar along its top.
        Detail(p, Cx - q[2].H + 0.05, q[2].Y - 0.06, Zt - 0.08, Cx + q[2].H - 0.05, q[2].Y + 0.06, Zt - 0.036, Frame, "painted steel");
        foreach (var x in new[] { -1.15, 1.15 })
            Detail(p, Cx + x - 0.05, q[2].Y + 0.06, Zt - 0.07, Cx + x + 0.05, q[4].Y - 0.1, Zt - 0.04, Dark, "painted steel");
        Detail(p, Cx - q[4].H + 0.1, q[4].Y - 0.1, Zt - 0.07, Cx + q[4].H - 0.1, q[4].Y - 0.02, Zt - 0.04, Dark, "painted steel");
        // Floodlights at the chin's corners for working a wreck in the dark, and hazard chevrons across the face.
        foreach (var sd in new[] { -1, 1 })
        {
            Detail(p, Cx + sd * 3.0 - 0.22, 0.45, Zt - 0.2, Cx + sd * 3.0 + 0.22, 0.85, Zt - 0.036, Frame, "dirty steel");
            Detail(p, Cx + sd * 3.0 - 0.17, 0.5, Zt - 0.206, Cx + sd * 3.0 + 0.17, 0.8, Zt - 0.198, Glow, glow: true);
        }
        for (var i = 0; i < 6; i++)
        {
            var x = -1.75 + i * 0.7;
            Detail(p, Cx + x - 0.15, 0.82, Zt - 0.05, Cx + x + 0.15, 0.92, Zt - 0.036, i % 2 == 0 ? Hazard : Dark, "hazard paint");
        }
        Hidden(p, Cx - 2.0, 0.0, Zt - 0.05, Cx + 2.0, 1.9, -16.954); // (back to the cockpit's dash: what a body meets at the glass)
        // The tool mounts: a square hardpoint each side and one in the middle, standing out from the chin's face, a hazard frame round
        // each and a socket on its face (any part welds on there, as on any other socket).
        foreach (var (n, x) in new[] { ("mountL", -1.6), ("mountC", 0.0), ("mountR", 1.6) })
        {
            const double my = 0.25, mf = Zt - 0.25, hw = 0.35;
            Solid(p, Cx + x - hw, my - hw, mf, Cx + x + hw, my + hw, Zt - 0.034, Frame, "dirty steel");
            foreach (var (x0, y0, x1, y1) in new[] { (-hw - 0.055, -hw - 0.055, hw + 0.055, -hw - 0.004), (-hw - 0.055, hw + 0.004, hw + 0.055, hw + 0.055), (-hw - 0.055, -hw - 0.004, -hw - 0.004, hw + 0.004), (hw + 0.004, -hw - 0.004, hw + 0.055, hw + 0.004) })
                Detail(p, Cx + x + x0, my + y0, Zt - 0.12, Cx + x + x1, my + y1, Zt - 0.036, Hazard, "hazard paint");
            foreach (var (dx, dy) in new[] { (-0.24, -0.24), (0.24, -0.24), (-0.24, 0.24), (0.24, 0.24) })
                Detail(p, Cx + x + dx - 0.04, my + dy - 0.04, mf - 0.03, Cx + x + dx + 0.04, my + dy + 0.04, mf, "#9a9c98", "steel"); // (its bolts)
            Sock(p, n, Cx + x, my, mf, "-z");
        }
        return p.Stats(2200, 12000, 40, 10.0);
    }

    private static Part SternPart()
    {
        var p = Big("Grapnel Stern", "The Grapnel's stern: the transom its two drives bolt to on shrouds, the drives' day tank in its void, and behind each, on the bulkhead's inner face, its engine running forward into engineering; the stern's attitude jets.", "fueltank", Skin);
        p.Set(("fuelTank", Kw(("capacity", SternFuel))));
        Works.Mount(p); // (the nozzles' mount: each one's engine bolts to the bulkhead's inner face on its axis)
        Facet(p, 0, 1, ZEngEnd, Stern, Bare, "plated steel");
        Facet(p, 5, 1, ZEngEnd, Stern, Skin2, "painted plating");
        Slab(p, 5, 1, ZEngEnd, Stern, 0.12, 0.9, 0.12, 0.88, 0.02, 0.1, 0.05, Armour);
        foreach (var s in new[] { -1, 1 })
        {
            for (var k = 1; k <= 4; k++)
            {
                Facet(p, k, s, ZEngEnd, Stern, k % 2 == 0 ? Skin : Skin2, "painted plating");
                Slab(p, k, s, ZEngEnd, Stern, 0.14, 0.92, 0.1, 0.9, 0.02, k == 2 ? 0.14 : 0.1, 0.05, k >= 3 ? Armour : Skin, "painted plating");
            }
            Chine(p, 1, s, ZEngEnd, Stern, 0.2, 0.09, Frame);
            Chine(p, 2, s, ZEngEnd, Stern, 0.3, 0.12, Dark);
            Chine(p, 3, s, ZEngEnd, Stern, 0.18, 0.08, Frame);
            Chine(p, 4, s, ZEngEnd, Stern, 0.16, 0.08, Frame);
        }
        Ribs(p, ZEngEnd, Stern, 0, 1, 2, 3, 4, 5);
        // The transom: a sooty plate, a shroud round each drive (a broad collar, its bolts), tail lights outboard, a floodlight.
        var q = At(Stern);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, Stern)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, Stern)))], Frame, "soot");
        foreach (var dx in DriveX)
        {
            var (x, y) = (Cx + dx, DriveY);
            Prism(p, RingZ(x - H, y - H, ShroudR, ShroudR, Stern + 0.02 - H, 16), RingZ(x - H, y - H, ShroudR - 0.1, ShroudR - 0.1, Stern + 0.3 - H, 16), Bare, "dirty steel");
            Prism(p, RingZ(x - H, y - H, ShroudR - 0.25, ShroudR - 0.25, Stern + 0.28 - H, 16), RingZ(x - H, y - H, ShroudR - 0.4, ShroudR - 0.4, Stern + 0.42 - H, 16), Frame, "soot");
            for (var k = 0; k < 12; k++)
            {
                var a = Math.PI / 12 + k * Math.PI / 6;
                var (bx, by) = (x + (ShroudR - 0.17) * Math.Cos(a), y + (ShroudR - 0.17) * Math.Sin(a));
                Detail(p, bx - 0.05, by - 0.05, Stern + 0.29, bx + 0.05, by + 0.05, Stern + 0.36, "#9a9c98", "steel"); // (its bolts)
            }
        }
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 3.7 - 0.15, 1.2, Stern + 0.02, Cx + s * 3.7 + 0.15, 1.6, Stern + 0.06, Red, glow: true);
            Detail(p, Cx + s * 1.4 - 0.25, 2.6, Stern + 0.02, Cx + s * 1.4 + 0.25, 2.66, Stern + 0.05, Hazard, "hazard paint");
        }
        Detail(p, Cx - 0.3, 2.9, Stern + 0.02, Cx + 0.3, 3.1, Stern + 0.07, Glow, glow: true);
        Detail(p, Cx - 0.12, DriveY - 0.9, Stern + 0.02, Cx + 0.12, DriveY + 0.9, Stern + 0.05, Hazard, "hazard paint");
        // The bulkhead across engineering's end: its inner face is the engines'.
        Solid(p, Cx - PassOut - SLine, 0, ZEngEnd, Cx + PassOut + SLine, ERoof, Stern - 0.03, Bare, "dirty steel");
        foreach (var dx in DriveX)
            Detail(p, Cx + dx - 1.0, DriveY + 1.1, ZEngEnd - 0.02, Cx + dx + 1.0, DriveY + 1.2, ZEngEnd - 0.003, Hazard, "hazard paint"); // (a keep-clear band over each engine)
        foreach (var (ya, yb) in new[] { (-0.7, 0.0), (ERoof, 3.85) })
            VoidT(p, ZEngEnd, Stern, ya, yb, 0, 0.004, 0.03);
        var sq = At(SternRcsZ);
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            RcsPad(p, "rcsUp" + n, Cx + s * 1.0, sq[4].Y, SternRcsZ, "+y", 0.05);
            RcsPad(p, "rcsDown" + n, Cx + s * 1.0, sq[0].Y, SternRcsZ, "-y", 0.05);
            RcsPad(p, "rcsUp2" + n, Cx + s * 2.4, sq[4].Y, SternRcsZ, "+y", 0.05);
            RcsPad(p, "rcsDown2" + n, Cx + s * 2.6, sq[0].Y, SternRcsZ, "-y", 0.05);
            RcsPad(p, "rcsOut" + n, FacetX(2, s, SternRcsZ, 0.9), 0.9, SternRcsZ, s < 0 ? "-x" : "+x", 0.28);
            RcsPad(p, "rcsAlong" + n, Cx + s * 3.2, 2.9, Stern, "+z", 0.05); // (above the shrouds' shoulders)
        }
        for (var i = 0; i < DriveX.Length; i++)
        {
            var x = Cx + DriveX[i];
            Solid(p, x - 0.7, DriveY - 0.7, Stern - 0.3, x + 0.7, DriveY + 0.7, Stern + 0.43, Frame, "dirty steel"); // (the nozzle's seat, inside its flange)
            Sock(p, $"drive{i}", x, DriveY, Stern + 0.43, "+z");
            Sock(p, $"engine{i}", x, DriveY, ZEngEnd, "-z");
        }
        FuelPort(p, DriveY);
        return p.Stats(9000, 30000, 45, 15.0);
    }

    // ---- the tank racks: four banded tanks on each flank, two rows of two, in a cradle off a hardpoint on the hold's flank

    private static Part TankRack(int s)
    {
        var name = s < 0 ? "Grapnel Tank Rack L" : "Grapnel Tank Rack R";
        var p = new Part(name, Small, Section, $"The Grapnel's tank rack {(s < 0 ? "to port" : "to starboard")}: four banded tanks in two rows of two, in a cage on a hardpoint off the hold's flank, the reserve tankage for a long tow.", role: "fueltank", tool: "welder", material: "steel", color: Frame, group: Group);
        double xi = Cx + s * RackX, xo = Cx + s * (TankCx + TankR + 0.06);
        Solid(p, xi, RackY - 0.2, -0.5, Cx + s * (TankCx - 0.2), RackY + 0.2, 0.5, Frame, "dirty steel"); // (the spar off the hardpoint)
        foreach (var zc in new[] { -3.45, -1.35, 0.0, 1.35, 3.45 })
        {
            Detail(p, xi + s * 0.01, -0.05, zc - 0.06, xi + s * 0.11, 2.53, zc + 0.06, Frame, "dirty steel");
            Detail(p, xo, -0.05, zc - 0.06, xo + s * 0.08, 2.53, zc + 0.06, Frame, "dirty steel");
            foreach (var (y0, y1) in new[] { (-0.04, 0.04), (1.18, 1.27), (2.43, 2.51) })
                Detail(p, xi + s * 0.015, y0, zc - 0.05, xo + s * 0.075, y1, zc + 0.05, Frame, "dirty steel");
        }
        foreach (var y in new[] { RackY0, RackY1 })
            foreach (var (a0, a1) in new[] { (-3.35, -0.2), (0.2, 3.35) })
            {
                double[] c = [Cx + s * TankCx, y, 0];
                DrawTank(p, c, 2, TankR, a0, a1, 0.35, 2, Skin, Frame, Sh);
                foreach (var (e, d) in new[] { (a0, -1), (a1, 1) })
                    Prism(p, RingAx(c, 2, 0.36, e - d * 0.06, 8, Sh), RingAx(c, 2, 0.36, e + d * 0.04, 8, Sh), Dark, "dirty steel"); // (its octagonal cap)
                Prism(p, RingAx(c, 2, TankR + 0.02, (a0 + a1) / 2 - 0.25, 16, Sh), RingAx(c, 2, TankR + 0.02, (a0 + a1) / 2 + 0.25, 16, Sh), Armour, "worn paint"); // (a yellow band)
                Hidden(p, c[0] - 0.38, y - 0.38, a0 + 0.1, c[0] + 0.38, y + 0.38, a1 - 0.1);
            }
        Sock(p, "hull", xi, RackY, 0, s < 0 ? "+x" : "-x");
        p.Set(("fuelTank", Kw(("capacity", RackFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(Cx + s * (RackX + 0.2), RackY, -0.5).Cast<object>()], "-z", "fuel"))));
        return p.Stats(3000, 6000, 20, 10.0);
    }

    // ---- the flying parts: lift jets, the power plant, the flight computer (the Dray's kinds, in the tug's colours)

    private static Part LiftJet()
    {
        const double k = JetScale;
        var p = new Part("Grapnel Lift Jet", Small, Section, $"A lift jet: {LiftForce / 1e6:0.0} MN straight down. Four under the Grapnel's cab and engineering hold it up loaded, from the keel's tanks.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[] R(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * k * Math.Cos(an), 4), Py.Round(y * k, 4), Py.Round(r * k * Math.Sin(an), 4)];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.55, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.48, -0.45, i))], Armour, "worn paint");
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.32, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.56, -0.24, i))], Hazard, "hazard paint");
        (double Y, double R)[] st = [(-0.45, 0.32), (-0.6, 0.27), (-0.9, 0.38), (-1.05, 0.44)];
        for (var s = 0; s < st.Length - 1; s++)
            for (var i = 0; i < 8; i++)
                Quad(p, R(st[s].R, st[s].Y, i), R(st[s].R, st[s].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i + 1), R(st[s + 1].R, st[s + 1].Y, i), s == 0 ? "#3a3d40" : "#2a2c2e", s == 0 ? "dirty steel" : "soot");
        Plate(p, [.. Enumerable.Range(0, 8).Select(i => R(0.24, -0.61, i))], "#ffb060", glow: true);
        p.Add(Box([-0.45 * k, -0.45 * k, -0.45 * k], [0.45 * k, 0, 0.45 * k], hidden: true));
        p.Add(Box([-0.4 * k, -1.05 * k, -0.4 * k], [0.4 * k, -0.45 * k, 0.4 * k], hidden: true));
        p.Sock("top", [0, 0, 0], "+y");
        p.Set(("thruster", Kw(("force", LiftForce), ("spool", 0.6), ("exhaust", "-y"), ("at", Py.L(0, Py.Round(-1.05 * k, 4), 0)), ("burnRate", Py.Round(LiftForce / 1e6 * Burn, 4)))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(0.45 * k, 4), Py.Round(-0.2 * k, 4), 0], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, Py.Round(-1.05 * k, 4), 0)), ("dir", "-y"), ("radius", Py.Round(0.4 * k, 4)), ("length", Py.Round(2.8 * k, 4)), ("kind", "Rocket")))));
        return p.Stats(1000, 4000, 25, 6.0);
    }

    /// <summary>The power plant: a stack of cells in a cage on engineering's deck, its coolant risers. No fuel.</summary>
    private static Part PowerPlant()
    {
        var p = new Part("Grapnel Power Plant", Small, Section, "The Grapnel's power plant: a stack of cells in a cage, 400 kW for the docks, the lights, the life support and the flight computer. No fuel.", role: "generator", tool: "welder", material: "steel", color: Armour, group: Group);
        double[] O(double x, double y, double z, double r, int i, int n = 8)
        {
            var a = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(x + r * Math.Cos(a), 4), y, Py.Round(z + r * Math.Sin(a), 4)];
        }
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        B(-0.8, 0, -0.8, 0.8, 0.2, 0.8, Frame, "dirty steel");
        foreach (var (x, z) in new[] { (-0.38, -0.38), (0.38, -0.38), (-0.38, 0.38), (0.38, 0.38) })
        {
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(x, 0.2, z, 0.32, i))], [.. Enumerable.Range(0, 8).Select(i => O(x, 1.7, z, 0.32, i))], Skin, "worn paint");
            Prism(p, [.. Enumerable.Range(0, 8).Select(i => O(x, 1.0, z, 0.34, i))], [.. Enumerable.Range(0, 8).Select(i => O(x, 1.08, z, 0.34, i))], "#80d0ff", "glass", glow: true);
        }
        foreach (var (x, z) in new[] { (-0.77, -0.77), (0.77, -0.77), (-0.77, 0.77), (0.77, 0.77) })
            B(x - 0.04, 0.2, z - 0.04, x + 0.04, 1.9, z + 0.04, Armour, "painted steel", true);
        B(-0.81, 1.86, -0.81, 0.81, 1.94, 0.81, Armour, "painted steel", true);
        foreach (var (x, col) in new[] { (-0.1, Red), (0.1, Link) })
            B(x - 0.05, 1.94, -0.05, x + 0.05, 2.4, 0.05, col, "painted steel", true);
        p.Add(Box([-0.74, 0.2, -0.74], [0.74, 1.7, 0.74], hidden: true));
        B(-0.79, 0.2, -0.812, -0.45, 0.45, -0.79, Hazard, "hazard paint", true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("generator", Kw(("rate", 400), ("spool", 6), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.1, 0.8)), ("face", "+z")))));
        return p.Stats(2400, 3500, 25, 10.0);
    }

    /// <summary>The flight computer: a flight stabilizer with a tug's gyro (the Packet's: the tank racks give it long flanks) in a console between the couches and the dash.</summary>
    private static Part FlightComputer()
    {
        const string console = "#22262b";
        var p = new Part("Grapnel Flight Computer", Small, Section, "The Grapnel's flight computer: a flight stabilizer with a tug's gyro in a console between the couches. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: console, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, PyDict shape, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: shape));
        B(-0.4, 0, -0.28, 0.4, 0.75, 0.28, console, Kw(("finish", "plastic")));
        B(-0.34, 0.38, 0.28, 0.34, 0.68, 0.298, "#78c8e8", Kw(("glow", true)), true); // (its screen faces the couches)
        for (var i = 0; i < 5; i++)
            B(-0.28 + i * 0.12, 0.24, 0.28, -0.22 + i * 0.12, 0.3, 0.298, i % 2 == 0 ? "#60e090" : Hazard, Kw(("glow", true)), true);
        B(-0.05, 0.751, -0.2, 0.05, 0.767, 0.1, Hazard, Kw(("finish", "hazard paint")), true); // (the arrow, forward)
        B(-0.12, 0.751, -0.2, 0.12, 0.767, -0.12, Hazard, Kw(("finish", "hazard paint")), true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 40000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(300, 600, 8, 3.0);
    }

    private static Part Ladder(string name, double rise, string desc, string sockName, double[] sockAt, string sockDir, double over = 1.0)
    {
        var p = new Part(name, Small, Section, desc, role: "frame", tool: "welder", material: "steel", color: Frame, group: Group);
        var top = rise + over; // (its rails' top over the landing)
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

    /// <summary>The belly hatch (the Dray's kind): a plate that shuts the boarding shaft flush under the keel, on a strut up the shaft's
    /// aft wall to the deck's edge, where it hangs. It slides down to the ground and the boarding ladder standing on it rides it.</summary>
    private static Part BellyHatchPart()
    {
        var p = new Part(BellyHatchName, Small, Section, "The Grapnel's belly hatch: a plate that shuts the boarding shaft under the keel. F (or the airlock's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.", role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        double[] S = [HatchX, Ft - 0.125, BellyHatch.Z1];
        double[] L(double x, double y, double z) => [Py.Round(x - S[0], 4), Py.Round(y - S[1], 4), Py.Round(z - S[2], 4)];
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string? col, string? fin, bool detail = false, bool hidden = false) =>
            p.Add(hidden ? Box(L(x0, y0, z0), L(x1, y1, z1), hidden: true) : Box(L(x0, y0, z0), L(x1, y1, z1), col!, detail: detail, shape: Kw(("finish", fin!))));
        double x0 = BellyHatch.X0 - 0.02, x1 = BellyHatch.X1 + 0.02, z0 = BellyHatch.Z0 - 0.02, z1 = BellyHatch.Z1 - 0.035;
        B(x0, HatchBottom, z0, x1, HatchTop, z1, Armour, "plated steel");
        foreach (var i in Enumerable.Range(0, 4))
        {
            var z = z0 + 0.15 + i * 0.3;
            B(x0 + 0.1, HatchBottom - 0.008, z, x1 - 0.1, HatchBottom - 0.002, z + 0.12, Hazard, "hazard paint", detail: true);
        }
        B(x0 + 0.03, HatchTop + 0.002, z0 + 0.03, x0 + 0.13, HatchTop + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(x1 - 0.13, HatchTop + 0.002, z0 + 0.03, x1 - 0.03, HatchTop + 0.06, z1 - 0.03, Frame, "dirty steel", detail: true);
        B(HatchX - 0.3, HatchTop, BellyHatch.Z1 - 0.03, HatchX + 0.3, Ft - 0.01, BellyHatch.Z1, null, null, hidden: true);
        B(HatchX - 0.28, HatchTop + 0.004, BellyHatch.Z1 - 0.028, HatchX + 0.28, Ft - 0.02, BellyHatch.Z1 - 0.004, Frame, "painted steel");
        p.Sock("hang", [0, 0, 0], "+z");
        p.Sock("ladder", L(HatchX, HatchTop, LadderZ - 0.1), "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", Py.Round(HatchTravel, 3)), ("seconds", 4.0), ("hang", "hang"))));
        return p.Stats(500, 4000, 30, 6.0);
    }

    private static Part Big(string name, string desc, string role = "frame", string color = Skin) =>
        new(name, Small, Section, desc, role: role, tool: "welder", material: "steel", color: color, group: Group);


    // ---- the ship in welded pieces (the Zero Day's way). Each big part is drawn whole, then its boxes are dealt out: a deck or hold
    // part keeps its floor (and what stands loose on it) under its own name; each wall run (WallRun) is a wall piece with what is
    // fixed to it (OnWall: the crane's girders and brackets); a ceiling is a piece; the hull outside the rooms (the skin, its armour
    // and the void behind it) is one piece a side per station band. The keel and the spine are cut at the stations. A socket goes
    // with the piece whose face it is on; wherever two pieces' solid boxes touch they get a facing socket pair (Connect): one pair
    // places a piece (PlaceOrder, out from the keel), the game welds the rest (ConstructBuild.AutoLink).

    private readonly record struct WallLine(bool AlongX, double A0, double A1, double C, double Y0, double Y1);

    private static readonly Dictionary<Part, List<WallLine>> Walls = [];
    private static readonly Dictionary<Part, List<(Rect[] Plan, double Y0, double Y1)>> Floors = [];
    private static readonly Dictionary<Part, List<(Rect R, double Y0, double Y1)>> Rooms = [];
    private static readonly Dictionary<Part, double> Ceils = [];
    private static readonly Dictionary<PyDict, int> Stuck = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<(string Part, string Sock), string> SockOwner = [];

    /// <summary>A deck or hold part dealt out into its floor (the part itself), its wall runs, its ceiling and its hull pieces.</summary>
    private static List<Part> SplitDeck(Part core, string shortName)
    {
        var walls = Walls.TryGetValue(core, out var ws) ? ws : [];
        var wallPieces = new Part?[walls.Count];
        var hull = new SortedDictionary<(int Side, int Band), Part>();
        Part? ceiling = null;
        var rooms = Rooms[core];
        double? ceilY = Ceils.TryGetValue(core, out var cy) ? cy : null;
        var what = shortName.ToLowerInvariant();
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
        Part WallPiece(int i) => wallPieces[i] ??= Big($"Grapnel {shortName} Wall {i + 1}", $"A wall of the Grapnel's {what}, with its door frames and what is fixed to it.", color: Wall);
        Part Of(PyDict b)
        {
            if (Stuck.TryGetValue(b, out var wi))
                return WallPiece(wi);
            double[] mn = Vd(b, "min"), mx = Vd(b, "max"), c = Mid3(b);
            foreach (var alongX in new[] { true, false })
                if (WallOf(c, alongX ? mx[2] - mn[2] : mx[0] - mn[0], alongX, false) is int i)
                    return WallPiece(i);
            if (!Inside(c))
            {
                var key = (c[0] < Cx ? -1 : 1, Band(c[2]));
                if (!hull.TryGetValue(key, out var h))
                    hull[key] = h = Big($"Grapnel {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece beside the Grapnel's {what}, the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (ceilY is double y && c[1] > y - 0.35)
                return ceiling ??= Big($"Grapnel {shortName} Ceiling", $"The ceiling of the Grapnel's {what}.", color: Ceil);
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
        // A hull piece needs something drawn and something solid: one that lacks either joins the nearest band's on its side, else the floor.
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
        var share = Math.Min(1.0, mass * 0.6 / others); // (the pieces take at most 60% of the part's mass; the floor keeps the rest)
        foreach (var q in all.Skip(1))
        {
            var (m, hp, ar) = Kind(q);
            q.Stats(m * share, hp, ar, 6.0);
        }
        core.Stats(mass - others * share, health * 0.5, 30, 15.0);
        return all;
    }

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

    /// <summary>The keel or the spine cut at the stations: the band holding coreZ keeps the part's name.</summary>
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
                    bands[k] = piece = Big($"Grapnel {shortName} {k + 1}", $"A length of the Grapnel's {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
                piece.Add(b);
            }
        }
        // A band holding only drawn details (a skid's end, a lamp past the last plate) goes to the nearest band with solid boxes.
        foreach (var k in bands.Keys.ToList())
            if (!bands[k].Boxes.Any(bx => !Drawn(bx)))
            {
                var to = bands.Where(e => e.Key != k && e.Value.Boxes.Any(bx => !Drawn(bx))).Select(e => (K: e.Key, P: e.Value)).Append((K: keep, P: core))
                    .MinBy(e => Math.Abs(e.K - k)).P;
                foreach (var bx in bands[k].Boxes)
                    to.Add(bx);
                Console.WriteLine($"grapnel_parts: {shortName} band {k + 1} has only details; they join {to.D["name"]}");
                bands.Remove(k);
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
                FuelPort(q, -0.6); // (a tank has a fuel-out port: on its aft face, under the floor, for a pipe a player lays)
            }
        }
        return all;
    }

    /// <summary>A fuel tank's Fuel out on its aft (+z) face, on the centreline at height y (ship metres).</summary>
    private static void FuelPort(Part q, double y)
    {
        var solid = q.Boxes.Where(bx => !Drawn(bx)).ToList();
        var zmax = solid.Max(bx => Convert.ToDouble(((List<object?>)bx["max"]!)[2]));
        // (on the aft face of the solid box there that holds the centreline: the spine's keel is shallower than the cab's)
        var aft = solid.Where(bx => Math.Abs(Vd(bx, "max")[2] - H - zmax) < 0.002 && Vd(bx, "min")[0] <= Cx && Vd(bx, "max")[0] >= Cx).ToList();
        if (aft.Count > 0)
            y = Math.Clamp(y, aft.Min(bx => Vd(bx, "min")[1]) + 0.05, aft.Max(bx => Vd(bx, "max")[1]) - 0.05);
        q.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. new[] { Cx - H, y - H, zmax }.Cast<object>()], "+z", "fuel"))));
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
            throw new InvalidOperationException("Grapnel: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
        return order;
    }

    private static List<string> Structure = [];
    private static Dictionary<string, int> PieceSteps = [];


    /// <summary>The step that holds socket sock of step on: the piece that took it when its part was split, else on itself.</summary>
    private static int Holder(BuildingParts.Blueprint bp, int on, string sock) =>
        bp.Steps[on]["part"] is string part && SockOwner.TryGetValue((part, sock), out var piece) ? PieceSteps[piece] : on;

    private static int Hang(BuildingParts.Blueprint bp, string part, int on, string sock, string by, int spin = 0) => bp.Hang(part, Holder(bp, on, sock), sock, by, spin);

    // ---- the blueprint

    private static int Paint(BuildingParts.Blueprint bp, int step, string paint, string? finish = null)
    {
        bp.Steps[step]["paint"] = paint;
        if (finish != null)
            bp.Steps[step]["finish"] = finish;
        return step;
    }

    private static int Door(BuildingParts.Blueprint bp, int on, string sock) => Paint(bp, Hang(bp, "Bulkhead Door 1.5x2.25 m", on, sock, "hang"), Orange, "worn paint");


    private static int Lamp(BuildingParts.Blueprint bp, int on, string sock, string colour = "#fff0d0")
    {
        var s = Hang(bp, "Bulkhead Light", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["colour"] = colour };
        return s;
    }

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, params int[] lamps) => Switch(bp, on, sock, true, lamps);

    private static void Switch(BuildingParts.Blueprint bp, int on, string sock, bool startOn, params int[] lamps)
    {
        var s = Hang(bp, "Bakelite Switch", on, sock, "back");
        bp.Steps[s]["config"] = new PyDict { ["startOn"] = startOn };
        foreach (var l in lamps)
            bp.Wires.Add((s, l));
    }

    private static PyDict Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "The Salvagers' salvage tug (Karl's picture): a boxy yellow hull with a glass cockpit over a chin with three tool mounts (sockets for whatever arm or grab a crew welds on), a crew cab behind it (bunks, a head, a galley, an airlock over the boarding shaft), two standard 2 m containers for sorted salvage in a bay open to the sky, four banded tanks on each flank, and engineering, whose two hull drives bolt to the stern with their engines inside. Lift jets and four legs set it down; attitude jets at both ends move it about.", defs, maxSteps: 400);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in Structure)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = placed;
        int keel = placed["Grapnel Keel"], roof = placed["Grapnel Roof"], cab = placed["Grapnel Cab"], bay = placed["Grapnel Bay"],
            eng = placed["Grapnel Engineering"], stern = placed["Grapnel Stern"];
        foreach (var d in new[] { "doorCk", "doorAft", "doorBunk", "doorHead", "doorGalley", "doorAirlock" })
            Door(bp, cab, d);
        // The cockpit: the pilot flies through the flight computer between the couches and the glass (Ship keys, the mouse aims the nose).
        var pilot = Hang(bp, "Flight Couch", cab, "pilot", "bottom");
        Hang(bp, "Flight Couch", cab, "copilot", "bottom");
        var fc = Hang(bp, "Grapnel Flight Computer", cab, "computer", "bottom");
        var flight = ShipParts.ShipFlight();
        flight["strength"] = 150; // (a long hull on small jets: at the usual strength it comes round too slowly)
        bp.Steps[fc]["config"] = flight;
        bp.Wires.Add((pilot, fc));
        // The bunk room and the head to port.
        Hang(bp, "Bunk", cab, "bunk0", "bottom");
        Hang(bp, "Bunk", cab, "bunk1", "bottom");
        Paint(bp, Hang(bp, "Service Locker", cab, "lockerBunk", "wall"), Armour);
        Hang(bp, "Ship Toilet", cab, "toilet", "wall");
        Hang(bp, "Ship Basin", cab, "basin", "wall");
        Hang(bp, "Shower Stall", cab, "shower", "wall");
        // The galley and the airlock to starboard.
        Hang(bp, "Ship Galley Range", cab, "range", "back");
        Hang(bp, "Galley Coffee Station", cab, "coffee", "wall");
        bp.HangTurned("Ship Mess Table", Holder(bp, cab, "table"), "table", "bottom", BuildingParts.RY90);
        foreach (var n in new[] { "suit0", "suit1" })
            Hang(bp, EvaParts.LockerName, cab, n, "wall");
        Hang(bp, EvaParts.LifeSupportName, cab, "lifeSupport", "wall");
        Hang(bp, EvaParts.OxygenTankName, cab, "o2Tank0", "wall");
        var hatch = Hang(bp, BellyHatchName, cab, "boarding", "hang"); // (the belly hatch, shut; the ladder stowed on it)
        Paint(bp, bp.Hang("Grapnel Boarding Ladder", hatch, "ladder", "foot"), Frame, "painted steel");
        Switch(bp, cab, "switchHatch", false, hatch); // (off: the hatch shut)
        // Engineering: the panel, the power, the lockers and the MMU dock on the bay's aft bulkhead.
        Hang(bp, "Engineer's Panel", eng, "panel", "back");
        Hang(bp, "Power Board", bay, "board", "back");
        Hang(bp, "Grapnel Power Plant", eng, "plant", "bottom");
        for (var i = 0; i < 3; i++)
            Hang(bp, "Battery", eng, $"battery{i}", "-y");
        Hang(bp, EvaParts.LockerName, bay, "suit", "wall");
        Paint(bp, Hang(bp, "Pressure Locker", bay, "locker", "wall"), Armour);
        EvaParts.FitDock(bp, Holder(bp, bay, "mmuDock"), "mmuDock");
        Hang(bp, "Tool Rack", eng, "tools", "back");
        // The drives: each nozzle on its seat through the transom, its engine (a Drive Core and a Fuel Pump) on the bulkhead's inner face.
        for (var i = 0; i < DriveX.Length; i++)
        {
            Hang(bp, "Drive Nozzle M", stern, $"drive{i}", "mount");
            Hang(bp, "Fuel Pump M", Hang(bp, "Drive Core M", stern, $"engine{i}", "front"), "back", "front");
        }
        // The tank racks on the bay's flanks.
        foreach (var n in new[] { "L", "R" })
            Hang(bp, $"Grapnel Tank Rack {n}", bay, $"rack{n}", "hull");
        // The attitude jets: the bow's up, down and out, the stern's every way.
        foreach (var n in new[] { "L", "R" })
        {
            Paint(bp, Hang(bp, Rcs, roof, $"rcsUp{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, keel, $"rcsDown{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, cab, $"rcsOut{n}", "base"), Frame, "dirty steel");
            foreach (var d in new[] { "Up", "Down", "Up2", "Down2", "Out", "Along" })
                Paint(bp, Hang(bp, Rcs, stern, $"rcs{d}{n}", "base"), Frame, "dirty steel");
        }
        // Lift jets and legs under the keel.
        for (var i = 0; i < LiftAt.Length; i++)
            Hang(bp, "Grapnel Lift Jet", keel, $"lift{i}", "top");
        for (var i = 0; i < LegAt.Length; i++)
            LegPodParts.HangPod(bp, LegPod, Holder(bp, keel, $"leg{i}"), $"leg{i}");
        // The salvage: a standard 2 m container in each cell, standing on the bay's twist locks by its corner castings, its hatches
        // shut and its three cradles in it. Port's is rust (scrap metal), starboard's grey (parts worth keeping): sorted as they come in.
        foreach (var (n, paint) in new[] { ("L", "#8a4a2a"), ("R", "#5f6b73") })
        {
            var box = Paint(bp, Hang(bp, ContainerName, bay, $"lock{n}0", "foot.0"), paint, "worn paint");
            foreach (var h in new[] { "hatch", "hatch.1" })
                Paint(bp, bp.Hang(ContainerName + " Hatch", box, h, "hang"), paint, "worn paint");
            for (var k = 0; k < 3; k++)
                bp.Hang("Container Cradle 2x2", box, $"slot{k}", "base");
        }
        // Lamps and their switches.
        Switch(bp, cab, "switchCk", Lamp(bp, cab, "lampCkL", "#ffd080"), Lamp(bp, cab, "lampCkR", "#ffd080"));
        Switch(bp, cab, "switchBunk", Lamp(bp, cab, "lampBunk", "#ffe0b0"));
        Switch(bp, cab, "switchHead", Lamp(bp, cab, "lampHead"));
        Switch(bp, cab, "switchGalley", Lamp(bp, cab, "lampGalley", "#ffe0b0"));
        Lamp(bp, cab, "lampAirlock", "#ffd040");
        Lamp(bp, cab, "lampHallA");
        Lamp(bp, cab, "lampHallB");
        Lamp(bp, bay, "lampCrossL", "#d8ecff");
        Lamp(bp, bay, "lampCrossR", "#d8ecff");
        foreach (var n in new[] { "L0", "L1", "R0", "R1" })
            Lamp(bp, bay, $"lamp{n}", "#d8ecff");
        Switch(bp, bay, "switchEng", Lamp(bp, eng, "lampEngL", "#ffe0b0"), Lamp(bp, eng, "lampEngR", "#ffe0b0"), Lamp(bp, eng, "lampEngA", "#ffe0b0"));
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        Walls.Clear();
        Floors.Clear();
        Rooms.Clear();
        Ceils.Clear();
        Stuck.Clear();
        SockOwner.Clear();
        var structure = new List<Part>();
        structure.AddRange(SplitBands(Keel(), "Keel", -10.0)); // (the band under the cab's lift jets keeps the keel's name)
        structure.AddRange(SplitDeck(Cab(), "Cab"));
        structure.AddRange(SplitDeck(Bay(), "Bay"));
        structure.AddRange(SplitDeck(Engineering(), "Eng"));
        structure.AddRange(SplitBands(Roof(), "Roof", -10.0));
        structure.Add(SternPart());
        structure.Add(Bow());
        Connect(structure);
        Structure = PlaceOrder(structure, "Grapnel Keel");
        List<Part> parts =
        [
            .. structure, TankRack(-1), TankRack(1),
            Ladder("Grapnel Boarding Ladder", Py.Round(BoardRise, 3), "The Grapnel's boarding ladder: it stands on the belly hatch, stowed up through the airlock's floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.", "foot", [0, 0, -0.1], "-y", over: -0.03),
            BellyHatchPart(), LiftJet(), PowerPlant(), FlightComputer(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"grapnel_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/GrapnelSalvageTug.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
    }
}
