using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Packet, the Traders' spine freighter (Karl, 2026-11-20: "a thin central spine ... realistic cargo holds ... which can each hold
/// two small 1x1 parts ... nacelles ... unique to traders and as detailed as the Rust Bucket. Small glass cockpit at the front"). A
/// medium ship (the Zero Day's class): a crew cab forward under a small glass cockpit, a thin walkable spine running aft from it with
/// twelve open cargo racks strapped along its flanks, six a side, each holding two 8-foot cube containers one over the other, then an
/// engineering block and two drive nacelles on swept pylons (docs/sandbox/constructs/freighter.md).
///
/// Built as the Dray and the Zero Day are (HaulerParts.cs, whose helpers this copies): a hull lofted through stations first, the rooms a
/// box of their own inside it, then each big part drawn whole and dealt out into welded pieces (floors, wall runs, ceilings, hull
/// bands), so a hit takes a piece, not a deck. Ship metres: x across (the centreline at Cx), y up from the deck's underside, z along
/// the ship (forward is -z; 0 is near the spine's middle, so the cargo stands within 30 m of the origin); P() moves them by half a
/// cell into a part's frame. The look is the Traders' (their IT style): beige office-case plating, corporate blue, lanyard yellow.
/// </summary>
public sealed class FreighterParts : IPartGenerator
{
    public string Name => "freighter_parts";

    private const string Out = "Constructs/Parts/Packet";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Packet freighter";
    public const string ShipName = "Packet Spine Freighter";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: the Traders' (FactionStyle IT): old office-case beige over a greyer skin, corporate blue, carpet grey, a dark frame,
    // lanyard yellow where things move and where people go in, link blue lights. Inside: pale office walls over a blue dado.
    private const string Skin = "#d8d0b8", Skin2 = "#c4bca3", Armour = "#1f4e8c", Armour2 = "#2a64a8", Bare = "#8e9296", Frame = "#3b3f44",
        Dark = "#26292d", Hazard = "#f2c400", Orange = "#f2c400", Red = "#c0392b", Glow = "#ffe0a0", Glass = "#86a9b8", Link = "#0f7bd8",
        Paper = "#e6e2d6", Green = "#2f9a52",
        Wall = "#cdcac0", WallLow = "#2a64a8", WallTrim = "#3b3f44", Deck = "#5d6266", Grate = "#3d4144", Ceil = "#b4b6b2",
        RackCol = "#34383d";

    /// <summary>The walls WallRun draws with.</summary>
    private static string WallCol = Wall, DadoCol = WallLow;

    // ---- the plan

    private const double Cx = 0.0; // the centreline
    private const double Ft = 0.25; // the deck's top, all along the ship
    private const double CCeil = 2.75, CRoof = 3.0, Lw = 3.0; // the cab: its ceiling, roof slab's top, rooms' inner half width
    private const double SCeil = 2.6, SRoof = 2.85, Sw = 0.8, SLine = 0.06; // the spine's corridor: its half width, the hull's lining (the corridor's wall)
    private const double ECeil = 2.75, ERoof = 3.0, Ew = 2.75; // engineering
    private const double Zt = -27.5, ZS1 = -25.5, ZCk = -23.0, ZCab = -20.5, ZCabEnd = -11.5, ZSp0 = -9.0, ZSp1 = 15.5, ZEng = 17.5, ZEngEnd = 26.5, Stern = 29.5;
    private const double ZCkFloor = -26.0; // the cockpit's floor, forward
    private const double ZCkW = -22.625, ZCabW = -11.625, ZEngW = 17.875, ZEngAW = 26.375; // the cross walls' middles: the cockpit's back, the cab's aft, engineering's front and aft
    private const double DoorH = 2.25;

    /// <summary>The cargo racks' middles along the spine, one a side at each (a rib between each two).</summary>
    private static readonly double[] RackZ = [-4.75, 2.75, 10.25];
    private const double RackX = 1.45, RackY = 1.4; // a rack's hardpoint face off the centreline, and its middle's height
    /// <summary>A rack's frame (in its own frame: x out from the hardpoint, y up from its middle, z along the ship): its depth, its half
    /// height and half length, a shelf's thickness and a post's.</summary>
    private const double RkD = 2.95, RkHy = 2.78, RkHz = 3.45, RkSh = 0.12, RkPost = 0.15;
    /// <summary>The standard container a rack takes (Karl, 2026-11-20: three 2x2x2 m parts in a row): across, up, along. Its shape is
    /// the standard container work's; until that part lands the racks carry a stand-in of the same size.</summary>
    private const double CtH = 2.5;

    /// <summary>The hull's stations (the hull pieces are cut at them).</summary>
    private static readonly double[] Bands = [Zt, ZS1, ZCk, ZCab, -17.0, ZCabEnd, ZSp0, -1.0, 6.5, ZSp1, ZEng, 22.0, ZEngEnd, Stern];
    /// <summary>The ring frames standing proud of the skin: the cab's, a rib between each two racks, engineering's.</summary>
    private static readonly double[] RibZ = [ZCab, -17.0, ZCabEnd, ZSp0, -1.0, 6.5, ZSp1, ZEng, 22.0, ZEngEnd];

    // The boarding shaft in the airlock (starboard, mid cab), the belly hatch shutting it and the ladder on the hatch (the Dray's way).
    private static readonly Rect BellyHatch = new(Cx + 1.1, -16.8, Cx + 2.25, -15.55);
    internal static BoardingShaft.Spec Shaft => new("Packet", Section, Group, BellyHatch.X0, BellyHatch.Z0, BellyHatch.X1, BellyHatch.Z1, Ft,
        At((BellyHatch.Z0 + BellyHatch.Z1) / 2)[0].Y, Ground, false, Armour, Frame, Hazard); // (the boarding shaft, BoardingShaft; its belly hatch hung inboard: open, it leaves the way out to starboard clear)

    // ---- flying it: two nacelles, lift jets and legs under the cab and engineering, attitude jets at both ends.

    /// <summary>The nacelles: middles off the centreline and up, the housing's flat distance, the nose, the tank's ends, the tail.</summary>
    private const double NacX = 6.3, NacY = 1.5, NacF = 1.2, NacNose = 16.6, NacTank0 = 18.0, NacTank1 = 25.0, NacTail = 30.4;
    private const double PylonX = 3.75, PylonZ = 22.0; // the pylon's hardpoint on engineering's flank
    private const int KeelFuel = 9000, NacFuel = 6000; // litres
    private const double LiftForce = 1500000, JetScale = 1.15, Burn = 2.5;
    private static readonly (double X, double Z)[] LiftAt = [(-1.5, -14.0), (1.5, -14.0), (-1.5, 21.0), (1.5, 21.0)];
    private static readonly (double X, double Z)[] LegAt = [(-2.0, -20.0), (2.0, -20.0), (-2.0, 25.0), (2.0, 25.0)];
    private const string LegPod = "S"; // (small pods: Karl, 2026-11-20)
    private const double PadProud = 0.1; // a jet's mount under the flat
    private const double LegProud = 0.16; // a leg pod's mount
    private static double Ground => -0.8 - LegProud - LegPodParts.ReachOf(LegPod);
    private const string Rcs = "Heavy RCS Thruster";
    private const double BowRcsZ = -18.8, SternRcsZ = 28.0, RcsPadW = 0.6;

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge)

    private static readonly (double, double)[] CabSec = [(2.6, -0.8), (3.6, 0.1), (3.8, 1.5), (3.55, 3.25), (2.35, 3.9)];
    private static readonly (double, double)[] SpSec = [(0.7, -0.35), (1.05, 0.05), (1.15, 1.4), (1.05, 2.95), (0.65, 3.3)];
    private static readonly (double, double)[] EngSec = [(2.5, -0.8), (3.4, 0.1), (3.6, 1.5), (3.4, 3.25), (2.25, 3.9)];

    // A rounded glass nose that widens to the cab; the cab's full section; a short neck down to the spine, which runs straight to a
    // second neck up to the engineering block, which tucks in to the transom.
    private static readonly Sec[] Hull =
    [
        new(Zt, [(0.9, 0.0), (1.3, 0.35), (1.45, 1.5), (1.25, 2.3), (0.65, 2.65)]), // (blunt: the glass stops at a wide face, the Nose Cap seals it)
        new(ZS1, [(1.3, -0.35), (1.85, 0.1), (2.05, 1.4), (1.85, 2.75), (1.05, 3.15)]),
        new(ZCk, [(2.6, -0.8), (3.45, 0.1), (3.55, 1.5), (3.3, 3.2), (2.1, 3.8)]),
        new(ZCab, CabSec), new(ZCabEnd, CabSec),
        new(ZSp0, SpSec), new(ZSp1, SpSec),
        new(ZEng, EngSec), new(ZEngEnd, EngSec),
        new(Stern, [(1.9, -0.55), (2.6, 0.1), (2.8, 1.5), (2.6, 2.95), (1.6, 3.35)]),
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
            throw new InvalidOperationException($"freighter_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
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
    private static void Prism(Part p, double[][] a, double[][] b, string color, string finish, bool glow = false, bool glass = false)
    {
        var both = a.Concat(b).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
        if (glow)
            shape["glow"] = true;
        else if (!glass)
            shape["finish"] = finish;
        p.Add(Box(mn, mx, color, detail: true, glass: glass, shape: shape));
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


    // ---- the Packet's own helpers

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

    /// <summary>The Traders' livery on a band of the upper side (facet 3): a corporate-blue stripe along its foot with a yellow pinstripe
    /// over it.</summary>
    private static void Livery(Part p, int s, double za, double zb)
    {
        double a = za + (RibAt(za) ? 0.26 : 0.02), b = zb - (RibAt(zb) ? 0.26 : 0.02), len = zb - za;
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.02, 0.2, 0.024, Armour, "painted plating");
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.225, 0.25, 0.024, Hazard, "painted plating");
    }

    // ---- the keel: the flat underside the whole length, the void under the floors (fuel), the boarding shaft, the jets' and legs' mounts

    private static Part Keel()
    {
        var p = Big("Packet Keel", "The Packet's keel: the flat underside under the cab, the spine and engineering, with the fuel tankage in the void under the decks, the boarding shaft, and the lift jets' and legs' mounts.", "fueltank", Armour);
        var shaft = new Rect(BellyHatch.X0 - 0.05, BellyHatch.Z0 - 0.05, BellyHatch.X1 + 0.05, BellyHatch.Z1 + 0.05);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var y = At(za)[0].Y;
            var hb = At(za)[0].H;
            if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                // (the cab's band is level: the flat round the shaft's hole in four plates)
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
                Band(p, 1, s, za, zb, Skin2, 1 + i, plates: za >= ZCk);
                Chine(p, 1, s, za, zb, 0.2, 0.09, Frame);
            }
            // Keel skids under the cab and engineering (not the spine: the racks hang below it there).
            if (zb - za > 3 && (zb <= ZCabEnd || za >= ZEng) && za >= ZCk)
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
        var hy = At(-16.0)[0].Y;
        foreach (var (x0, z0, x1, z1, e) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0, 0.0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1, 0.0),
            (shaft.X0, shaft.Z0 + 0.025, shaft.X0, shaft.Z1 - 0.025, 0.005), (shaft.X1, shaft.Z0 + 0.025, shaft.X1, shaft.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + 0.01 + e, z0), P(x1, hy + 0.01 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z0 - 0.12, shaft.X1 + 0.12, hy - 0.004, shaft.Z0, Hazard, "hazard paint");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z1, shaft.X1 + 0.12, hy - 0.004, shaft.Z1 + 0.12, Hazard, "hazard paint");
        Sock(p, "bellyHatch", Shaft.HatchAt[0], Shaft.HatchAt[1], Shaft.HatchAt[2], Shaft.HatchDir);
        // The lift jets' and the legs' mounts: square pads under the flat; the bow's down jets.
        for (var i = 0; i < LiftAt.Length; i++)
            MountPad(p, $"lift{i}", Cx + LiftAt[i].X, At(LiftAt[i].Z)[0].Y, LiftAt[i].Z, 0.45);
        for (var i = 0; i < LegAt.Length; i++)
            MountPad(p, $"leg{i}", Cx + LegAt[i].X, At(LegAt[i].Z)[0].Y, LegAt[i].Z, 0.4, LegProud);
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsDown" + (s < 0 ? "L" : "R"), Cx + s * 0.55, At(BowRcsZ)[0].Y, BowRcsZ, "-y", 0.05); // (inboard of the legs)
        // Running lights under the cab's chin: a pair of landing lamps.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 1.0 - 0.16, At(-22.0)[0].Y - 0.05, -22.2, Cx + s * 1.0 + 0.16, At(-22.0)[0].Y - 0.012, -21.9, Glow, glow: true);
        p.Set(("fuelTank", Kw(("capacity", KeelFuel)))); // (shared out over its bands, each with its Fuel out: SplitBands)
        return p.Stats(16000, 50000, 40, 25.0);
    }

    // ---- the roof: the top facets the whole length, the cockpit's glass roof, the void over the ceilings; a comms mast, a dorsal
    // trunk along the spine, radiators over engineering

    private static Part Roof()
    {
        var p = Big("Packet Roof", "The Packet's roof: the cockpit's glass top, the cab's roof with the comms mast and its dish, the dorsal trunk and rail along the spine, and engineering's radiators.", color: Skin);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var glass = zb <= ZCk + 1e-6;
            if (glass)
                Facet(p, 5, 1, za, zb, Glass, glass: true);
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
                    Band(p, 4, s, za, zb, Skin, 4 + i);
                Chine(p, 4, s, za, zb, glass ? 0.07 : 0.16, glass ? 0.05 : 0.08, glass ? Dark : Armour);
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
        // The comms mast over the cab (the Traders' dishes and masts): a lattice tower, a dish on a yoke, whip antennas, a beacon.
        double mz = -14.5, my = Top(mz);
        Solid(p, Cx - 0.5, my - 0.12, mz - 0.5, Cx + 0.5, my + 0.15, mz + 0.5, Frame, "dirty steel");
        foreach (var (dx, dz) in new[] { (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
            Detail(p, Cx + dx - 0.035, my + 0.15, mz + dz - 0.035, Cx + dx + 0.035, my + 2.6, mz + dz + 0.035, Paper, "painted steel");
        foreach (var yy in new[] { 0.8, 1.4, 2.0, 2.55 })
            Detail(p, Cx - 0.33, my + yy, mz - 0.33, Cx + 0.33, my + yy + 0.05, mz + 0.33, Red, "painted steel");
        Prism(p, Ngon(Cx + 0.9, my + 1.9, mz, 0.1, 10), Ngon(Cx + 0.9, my + 2.25, mz, 0.75, 10), Paper, "painted steel");
        Detail(p, Cx + 0.33, my + 1.85, mz - 0.05, Cx + 0.9, my + 1.95, mz + 0.05, Frame, "painted steel");
        Detail(p, Cx + 0.86, my + 2.0, mz - 0.03, Cx + 0.94, my + 2.75, mz + 0.03, Frame, "painted steel");
        Detail(p, Cx - 0.06, my + 2.6, mz - 0.06, Cx + 0.06, my + 2.75, mz + 0.06, Red, glow: true);
        foreach (var (dx, dz, h) in new[] { (-1.4, -15.8, 1.6), (-1.6, -15.2, 1.2), (-1.2, -13.0, 1.9) })
        {
            Detail(p, Cx + dx - 0.12, Top(dz) - 0.01, dz - 0.12, Cx + dx + 0.12, Top(dz) + 0.1, dz + 0.12, Frame, "dirty steel");
            Detail(p, Cx + dx - 0.02, Top(dz) + 0.1, dz - 0.02, Cx + dx + 0.02, Top(dz) + h, dz + 0.02, Dark, "painted steel");
            Detail(p, Cx + dx - 0.04, Top(dz) + h, dz - 0.04, Cx + dx + 0.04, Top(dz) + h + 0.08, dz + 0.04, Link, glow: true);
        }
        // Navigation lights on the cab's shoulders: red to port, green to starboard.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 2.0 - 0.1, Top(-21.5) - 0.01, -21.6, Cx + s * 2.0 + 0.1, Top(-21.5) + 0.09, -21.4, s < 0 ? Red : Green, glow: true);
        // The dorsal trunk along the spine: a duct on standoffs, with a rail either side to clip a tether to on a space walk.
        var sy = Top(0);
        Solid(p, Cx - 0.25, sy - 0.012, ZSp0 + 0.6, Cx + 0.25, sy + 0.32, ZSp1 - 0.6, Bare, "painted steel"); // (its foot inside the roof plate)
        for (var z = ZSp0 + 1.0; z < ZSp1 - 0.8; z += 2.0)
        {
            Detail(p, Cx - 0.3, sy + 0.32, z - 0.08, Cx + 0.3, sy + 0.36, z + 0.08, Frame, "dirty steel");
            foreach (var s in new[] { -1, 1 })
                Detail(p, Cx + s * 0.6 - 0.025, sy - 0.01, z - 0.025, Cx + s * 0.6 + 0.025, sy + 0.55, z + 0.025, Hazard, "worn paint");
        }
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * 0.6 - 0.03, sy + 0.55, ZSp0 + 0.9, Cx + s * 0.6 + 0.03, sy + 0.61, ZSp1 - 0.9, Hazard, "worn paint");
        // The radiators over engineering: two raked fin panels, glowing faintly along their edges.
        foreach (var s in new[] { -1, 1 })
        {
            var ry = Top(22.0);
            for (var k = 0; k < 4; k++)
            {
                double z0 = 19.0 + k * 1.7, z1 = z0 + 1.4;
                Prism(p, [P(Cx + s * 0.5, ry - 0.01, z0), P(Cx + s * 0.58, ry - 0.01, z0), P(Cx + s * 0.58, ry - 0.01, z1), P(Cx + s * 0.5, ry - 0.01, z1)],
                    [P(Cx + s * 1.6, ry + 0.9, z0 + 0.3), P(Cx + s * 1.66, ry + 0.9, z0 + 0.3), P(Cx + s * 1.66, ry + 0.9, z1 + 0.3), P(Cx + s * 1.6, ry + 0.9, z1 + 0.3)], k % 2 == 0 ? Frame : Bare, "soot");
                Detail(p, Cx + s * 1.6 - 0.04, ry + 0.9, z0 + 0.34, Cx + s * 1.66 + 0.04, ry + 0.93, z1 + 0.26, "#ff7040", glow: true);
            }
        }
        // The bow's up jets.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsUp" + (s < 0 ? "L" : "R"), Cx + s * 1.2, Top(BowRcsZ), BowRcsZ, "+y", 0.056); // (its top clear of the roof's rib)
        // The void over the ceilings.
        foreach (var (_, za, zb) in BandsIn(ZCk, ZEngEnd))
        {
            var floor = za >= ZEng - 1e-6 ? ERoof : za >= ZCabEnd - 1e-6 ? SRoof : CRoof;
            var z0 = Math.Max(za, ZCk + 0.25);
            VoidT(p, z0, zb, floor, Math.Min(Top(z0), Top(zb)) - 0.03, 0, z0 > za ? 0.0 : 0.004, 0.004);
        }
        return p.Stats(12000, 36000, 35, 15.0);
    }

    // ---- the cab: the glass cockpit, then two rows of rooms off a corridor; its hull bands

    private static Part Cab()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Packet Cab", "The Packet's cab: the glass cockpit forward, then the crew's rooms off a corridor: the bunk room, the head and the trade office to port; the galley, the airlock over the boarding shaft and the stores to starboard.");
        Rect ckA = new(Cx - 1.0, ZCkFloor, Cx + 1.0, -25.0), ckB = new(Cx - 1.6, -25.0, Cx + 1.6, ZCkW - 0.125), cab = new(Cx - Lw - 0.25, ZCkW - 0.125, Cx + Lw + 0.25, ZCabEnd);
        Floor(p, [ckA, ckB, cab], 0, Ft, Deck, "painted deck plate", BellyHatch);
        Floor(p, [new Rect(cab.X0, ZCkW + 0.125, cab.X1, ZCabW - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Room(p, new Rect(Cx - 1.6, ZCkFloor - 0.4, Cx + 1.6, ZCkW), 0, 3.3);
        Room(p, cab, 0, CRoof);
        Ceils[p] = CCeil;
        // The cockpit: a mat, the dash under the windscreen with its screens and switches, two couches and the flight computer.
        Detail(p, Cx - 1.5, Ft, -24.95, Cx + 1.5, Ft + 0.012, ZCkW - 0.25, "#30353b", "rubber matting");
        Solid(p, Cx - 0.95, Ft, ZCkFloor, Cx + 0.95, 0.95, -25.55, Dark, "plastic");
        foreach (var (x0, x1, col) in new[] { (-0.85, -0.3, "#78c8e8"), (-0.22, 0.22, "#60e090"), (0.3, 0.85, "#78c8e8") })
            Detail(p, Cx + x0, 0.95, -25.98, Cx + x1, 0.962, -25.6, col, glow: true);
        for (var i = 0; i < 7; i++)
            Detail(p, Cx - 0.6 + i * 0.2, 0.6, -25.553, Cx - 0.52 + i * 0.2, 0.68, -25.54, i % 3 == 0 ? Hazard : Paper, "plastic");
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
        // The cockpit's back wall, the cab's sides, the corridor's walls with the rooms' doors, the rooms' cross walls, the aft wall.
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCkW, Ft, CCeil, Door(Cx - 0.75));
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + s * (Lw + 0.125), Ft, CCeil);
        WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx - 0.875, Ft, CCeil, Door(-21.9), Door(-17.7), Door(-14.4));
        WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + 0.875, Ft, CCeil, Door(-21.6), Door(-15.6), Door(-13.5));
        foreach (var z in new[] { -18.125, -14.875 })
            WallRun(p, true, Cx - Lw, Cx - 1.0, z, Ft, CCeil);
        foreach (var z in new[] { -18.625, -13.875 })
            WallRun(p, true, Cx + 1.0, Cx + Lw, z, Ft, CCeil);
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCabW, Ft, CCeil, Door(Cx - 0.75));
        // The boarding shaft's coaming round its deck hatch (walked over shut).
        DoorParts.DeckHatchRim(p, BellyHatch.X0, BellyHatch.Z0, BellyHatch.X1, BellyHatch.Z1, Ft, null, H);
        Detail(p, Cx + 1.0, Ft + DoorH + 0.15, -14.9, Cx + 1.015, Ft + DoorH + 0.3, -14.6, Red, glow: true);
        Detail(p, Cx + 0.735, Ft + DoorH + 0.15, -14.9, Cx + 0.75, Ft + DoorH + 0.3, -14.6, "#40e070", glow: true);
        // The corridor: a grating runner, a light strip.
        Detail(p, Cx - 0.6, Ft, ZCkW + 0.3, Cx + 0.6, Ft + 0.012, ZCabW - 0.3, Grate, "painted grating");
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZCkW + 0.6, ZCabW - 0.6, CCeil);
        // The hull: the cockpit's glass sides and the windscreen, then the cab's bands with the livery and portholes.
        foreach (var s in new[] { -1, 1 })
        {
            Facet(p, 2, s, Zt, ZS1, Glass, glass: true);
            Facet(p, 3, s, Zt, ZS1, Glass, glass: true);
            Band(p, 2, s, ZS1, ZCk, Skin, 2, plates: false);
            Facet(p, 3, s, ZS1, ZCk, Glass, glass: true);
            Chine(p, 2, s, Zt, ZS1, 0.07, 0.05, Dark);
            Chine(p, 3, s, Zt, ZS1, 0.07, 0.05, Dark);
            Chine(p, 2, s, ZS1, ZCk, 0.24, 0.1, Armour);
            Chine(p, 3, s, ZS1, ZCk, 0.07, 0.05, Dark);
            Slab(p, 2, s, ZS1, ZCk, 0.1, 0.9, 0.12, 0.88, 0.02, 0.1, 0.05, Armour);
            foreach (var (_, za, zb) in BandsIn(ZCk, ZCabEnd))
            {
                Band(p, 2, s, za, zb, Skin, 3);
                Band(p, 3, s, za, zb, Skin, 5, plates: false);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.24, 0.1, Armour);
                Chine(p, 3, s, za, zb, 0.18, 0.08, Frame);
            }
            // Portholes in the upper sides: the bunk room, the galley, the office.
            foreach (var z in new[] { -21.3, -19.6, -13.3 })
            {
                var (za, zb) = z < ZCab ? (ZCk, ZCab) : z < -17.0 ? (ZCab, -17.0) : (-17.0, ZCabEnd);
                double len = zb - za, u = (z - za) / len, du = 0.32 / len;
                Slab(p, 3, s, za, zb, u - du - 0.06 / len, u + du + 0.06 / len, 0.38, 0.72, 0.02, 0.06, 0.02, Dark, "painted steel");
                FStrip(p, 3, s, za, zb, u - du, u + du, 0.43, 0.67, 0.062, "#24384a", "glass");
            }
            // The Traders' mark on the cab's flank: a white letterhead panel with a blue square on it.
            Slab(p, 3, s, -17.0, ZCabEnd, 0.3, 0.6, 0.35, 0.8, 0.02, 0.05, 0.02, Paper, "painted plating");
            Slab(p, 3, s, -17.0, ZCabEnd, 0.33, 0.39, 0.42, 0.73, 0.05, 0.07, 0.01, Armour, "painted plating");
        }
        Ribs(p, ZCk, ZCabEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCk, ZCabEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, CRoof) })
                VoidT(p, za, zb, ya, yb, Lw + 0.25, za == ZCk ? 0.25 : 0.004, 0.004);
        // The bow's out jets on the cab's flanks.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsOut" + (s < 0 ? "L" : "R"), FacetX(2, s, BowRcsZ, 0.8), 0.8, BowRcsZ, s < 0 ? "-x" : "+x", 0.28);
        // Doors.
        DoorSock(p, "doorCk", true, Cx - 0.75, ZCkW, Ft);
        DoorSock(p, "doorAft", true, Cx - 0.75, ZCabW, Ft);
        DoorSock(p, "doorBunk", false, -21.9, Cx - 0.875, Ft);
        DoorSock(p, "doorHead", false, -17.7, Cx - 0.875, Ft);
        DoorSock(p, "doorOffice", false, -14.4, Cx - 0.875, Ft);
        DoorSock(p, "doorGalley", false, -21.6, Cx + 0.875, Ft);
        DoorSock(p, "doorAirlock", false, -15.6, Cx + 0.875, Ft);
        DoorSock(p, "doorStores", false, -13.5, Cx + 0.875, Ft);
        // The cockpit's couches and computer, its lamps and their switch on the back wall.
        Sock(p, "pilot", Cx - 0.8, Ft, -23.9, "+y");
        Sock(p, "copilot", Cx + 0.8, Ft, -23.9, "+y");
        Sock(p, "computer", Cx, Ft, -25.0, "+y");
        Sock(p, "lampCkL", Cx - 1.4, Ft + 2.2, ZCkW - 0.125, "-z");
        Sock(p, "lampCkR", Cx + 1.4, Ft + 2.2, ZCkW - 0.125, "-z");
        Sock(p, "switchCk", Cx + 1.1, Ft + 1.3, ZCkW - 0.125, "-z");
        // The bunk room: two bunks along the outer wall, a locker by the door.
        Sock(p, "bunk0", Cx - 2.45, Ft, -21.45, "+y");
        Sock(p, "bunk1", Cx - 2.45, Ft, -19.3, "+y");
        Sock(p, "lockerBunk", Cx - 1.5, Ft + 0.125, ZCkW + 0.125, "+z");
        Sock(p, "lampBunk", Cx - Lw, Ft + 2.2, -20.4, "+x");
        Sock(p, "switchBunk", Cx - 1.0, Ft + 1.3, -22.2, "-x");
        // The head.
        Sock(p, "toilet", Cx - Lw, Ft + 0.125, -17.35, "+x");
        Sock(p, "basin", Cx - Lw, Ft + 0.125, -16.45, "+x");
        Sock(p, "shower", Cx - 2.45, Ft + 0.125, -15.0, "-z");
        Sock(p, "lampHead", Cx - Lw, Ft + 2.3, -17.0, "+x");
        Sock(p, "switchHead", Cx - 1.0, Ft + 1.3, -15.6, "-x");
        // The trade office: a desk with its computer by the outer wall, two server racks on the aft wall, a filing cabinet and a fax.
        Sock(p, "computerDesk", Cx - 2.85, Ft, -13.2, "+y");
        Sock(p, "server0", Cx - 2.675, Ft, -12.4, "+y");
        Sock(p, "server1", Cx - 1.975, Ft, -12.4, "+y");
        Sock(p, "cabinet", Cx - 2.6, Ft + 0.125, -14.75, "+z");
        Sock(p, "lampOffice", Cx - Lw, Ft + 2.3, -13.9, "+x");
        Sock(p, "switchOffice", Cx - 1.0, Ft + 1.3, -12.6, "-x");
        // The galley: the range and the coffee station on the forward wall, the mess table.
        Sock(p, "range", Cx + 2.3, Ft + 0.45, ZCkW + 0.125, "+z");
        Sock(p, "coffee", Cx + 1.4, Ft + 0.125, ZCkW + 0.125, "+z");
        Sock(p, "table", Cx + 2.3, Ft, -20.1, "+y");
        Sock(p, "lampGalley", Cx + Lw, Ft + 2.2, -20.6, "-x");
        Sock(p, "switchGalley", Cx + 1.0, Ft + 1.3, -22.2, "+x");
        // The airlock: the MMU dock on the forward wall, suit lockers on the outer wall, the hatch's switch, the belly hatch's hanging.
        Sock(p, "mmuDock", Cx + 2.35, Ft + 0.125, -18.5, "+z");
        Sock(p, "suit0", Cx + Lw, Ft + 0.125, -16.05, "-x");
        Sock(p, "suit1", Cx + Lw, Ft + 0.125, -14.55, "-x");
        Sock(p, "switchHatch", Cx + 1.0, Ft + 1.45, -16.3, "+x");
        Sock(p, "lampAirlock", Cx + Lw, Ft + 2.3, -17.6, "-x");
        Sock(p, "boarding", Shaft.LadderAt[0], Shaft.LadderAt[1], Shaft.LadderAt[2], "-z");
        Sock(p, "deckHatch", BellyHatch.X0, Ft - DoorParts.HatchThick / 2, (BellyHatch.Z0 + BellyHatch.Z1) / 2, "+x"); // (hung by its inboard edge, swinging up)
        // The stores: the ship's air on the outer wall, a crate.
        Sock(p, "lifeSupport", Cx + Lw, Ft + 0.125, -13.2, "-x");
        Sock(p, "o2Tank0", Cx + Lw, Ft + 0.125, -12.3, "-x");
        Sock(p, "lampStores", Cx + 2.0, Ft + 2.3, ZCabW - 0.125, "-z");
        // The corridor's lamps.
        Sock(p, "lampHallA", Cx - 0.75, Ft + 2.3, -19.0, "+x");
        Sock(p, "lampHallB", Cx + 0.75, Ft + 2.3, -17.0, "-x");
        foreach (var (what, z, hx) in new[] { ("cockpit front", ZCkFloor, 1.0), ("cockpit", -25.0, 1.6), ("cab front", ZCkW - 0.125, Lw + 0.25), ("cab aft", ZCabEnd, Lw + 0.25) })
            foreach (var y in new[] { 0.0, z < ZCkW - 0.2 ? 0.95 : CRoof })
                MustFit(what, z, hx, y);
        return p.Stats(22000, 40000, 30, 20.0);
    }

    // ---- the spine: the corridor from the cab to engineering, its hull bands and the racks' hardpoints

    private static Part Spine()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Packet Spine", "The Packet's spine: a narrow corridor from the cab to engineering inside the hull itself, its lining railed for when the gravity is off, and the cargo racks' hardpoints through it, six a side.");
        // No wall runs: the hull is the corridor's wall. Its lining (pale over a blue dado) stands on the floor's edge, a length to
        // each station band, so a hull piece shot off opens the corridor. The rooms' box is drawn narrow so the lining and what is
        // fixed to it go with the hull pieces.
        var plan = new Rect(Cx - Sw, ZCabEnd, Cx + Sw, ZEngW - 0.125);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate");
        Floor(p, [plan], SCeil, SRoof, Ceil, "painted plating");
        Room(p, new Rect(Cx - Sw + 0.2, ZCabEnd, Cx + Sw - 0.2, plan.Z1), 0, SRoof);
        Ceils[p] = SCeil;
        Detail(p, Cx - 0.5, Ft, plan.Z0 + 0.2, Cx + 0.5, Ft + 0.012, plan.Z1 - 0.2, Grate, "painted grating");
        double[] segs = [ZCabEnd, ZSp0, -1.0, 6.5, ZSp1, plan.Z1];
        for (var i = 0; i < segs.Length - 1; i++)
        {
            double a0 = segs[i], a1 = segs[i + 1];
            double l0 = a0 + (i == 0 ? 0.0 : 0.004), l1 = a1 - (a1 >= plan.Z1 - 1e-6 ? 0.0 : 0.004);
            foreach (var s in new[] { -1, 1 })
            {
                var face = Cx + s * Sw;
                double xa = Math.Min(face, face + s * SLine), xb = Math.Max(face, face + s * SLine);
                Solid(p, xa, 0, l0, xb, Ft + 0.9, l1, WallLow, "painted plating");
                Solid(p, xa, Ft + 0.9, l0, xb, SRoof, l1, Wall, "painted plating");
                // A handrail on standoffs, a frame post at the length's forward end, cable trays and pipes up high.
                Detail(p, face - s * 0.09, Ft + 1.0, a0 + 0.25, face - s * 0.05, Ft + 1.05, a1 - 0.25, Hazard, "worn paint");
                for (var z = a0 + 0.5; z < a1 - 0.3; z += 1.5)
                    Detail(p, face - s * 0.06, Ft + 1.0, z - 0.03, face - s * 0.004, Ft + 1.04, z + 0.03, Frame, "painted steel");
                Detail(p, face - s * 0.12, Ft + 0.004, a0 + 0.008, face - s * 0.004, SCeil - 0.004, a0 + 0.16, Frame, "dirty steel");
                Detail(p, face - s * 0.02, Ft + 0.9 - 0.03, l0 + 0.004, face - s * 0.004, Ft + 0.9 + 0.03, l1 - 0.004, WallTrim, "painted steel");
                if (s < 0)
                    foreach (var (y, col) in new[] { (2.15, Red), (2.28, Link), (2.41, Bare) })
                        Detail(p, face - s * 0.09, y, a0 + 0.2, face - s * 0.014, y + 0.07, a1 - 0.03, col, "painted steel");
                else
                    Detail(p, face - s * 0.16, 2.2, a0 + 0.2, face - s * 0.004, 2.24, a1 - 0.03, Frame, "dirty steel");
            }
            // The frame's beam over the corridor at the length's forward end, and the light strip.
            Detail(p, Cx - Sw + 0.124, SCeil - 0.14, a0 + 0.012, Cx + Sw - 0.124, SCeil - 0.004, a0 + 0.152, Frame, "dirty steel"); // (between the posts)
            if (a1 - a0 > 1.5)
                CeilStrip(p, Cx - 0.1, Cx + 0.1, a0 + 0.5, a1 - 0.3, SCeil);
        }
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZCabEnd, ZEng))
            {
                var spine = za >= ZSp0 - 1e-6 && zb <= ZSp1 + 1e-6;
                Band(p, 2, s, za, zb, spine ? Skin2 : Skin, 11 + i, plates: !spine);
                Band(p, 3, s, za, zb, Skin, 12 + i, plates: !spine);
                var cut = spine ? 0.0 : 0.012; // (a neck's plates lean past their ends: its rails stop short of them)
                Chine(p, 2, s, za + cut, zb - cut, spine ? 0.14 : 0.24, spine ? 0.07 : 0.1, Armour);
                Chine(p, 3, s, za + cut, zb - cut, 0.12, 0.06, Frame);
                if (spine) // (the necks have no livery: their plates twist too much for a strip to lie on them)
                    // Along the spine the panels are long stringers between the ribs, with a blue band on the upper side.
                    foreach (var (v0, v1) in new[] { (0.12, 0.42), (0.58, 0.88) })
                        Slab(p, 2, s, za, zb, 0.07, 0.93, v0, v1, 0.02, 0.07, 0.03, Skin, "painted plating");
                if (spine)
                    Slab(p, 3, s, za, zb, 0.07, 0.93, 0.1, 0.45, 0.02, 0.06, 0.03, Armour, "painted plating");
            }
        Ribs(p, ZCabEnd, ZEng, 2, 3);
        // The racks' hardpoints: a block through the skin at the rack's middle, its face the rack's socket, a clamp either side of it.
        for (var i = 0; i < RackZ.Length; i++)
            foreach (var s in new[] { -1, 1 })
            {
                var z = RackZ[i];
                Solid(p, Cx + s * (Sw + SLine + 0.004), RackY - 0.42, z - 0.6, Cx + s * RackX, RackY + 0.42, z + 0.6, Frame, "dirty steel");
                foreach (var dz in new[] { -2.2, 2.2 })
                    Detail(p, Cx + s * 1.0, RackY - 0.18, z + dz - 0.12, Cx + s * (RackX - 0.004), RackY + 0.18, z + dz + 0.12, Dark, "dirty steel");
                Sock(p, $"rack{(s < 0 ? "L" : "R")}{i}", Cx + s * RackX, RackY, z, s < 0 ? "-x" : "+x");
            }
        foreach (var (_, za, zb) in BandsIn(ZCabEnd, ZEng))
            foreach (var (ya, yb) in new[] { (0.0, RackY), (RackY, SRoof) })
                VoidT(p, za, zb, ya, yb, Sw + SLine + 0.004, za == ZCabEnd ? 0.0 : 0.004, zb == ZEng ? 0.0 : 0.004); // (thin: the skin is close)
        // The lamps along the corridor, a side each band in turn.
        for (var i = 0; i < segs.Length - 1; i++)
        {
            var s = i % 2 == 0 ? -1 : 1;
            Sock(p, $"lamp{i}", Cx + s * Sw, Ft + 2.0, (segs[i] + segs[i + 1]) / 2, s < 0 ? "+x" : "-x");
        }
        foreach (var z in new[] { ZCabEnd, 0.0, plan.Z1 })
            foreach (var y in new[] { 0.0, SRoof })
                MustFit("spine", z, Sw + SLine, y);
        return p.Stats(18000, 30000, 30, 15.0);
    }

    // ---- engineering: the plant, the board, the batteries, the Engineer's Panel and the space-walk gear for the nacelles

    private static Part Engineering()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Packet Engineering", "The Packet's engineering block: the power plant, the Power Board and the batteries, the Engineer's Panel, the fuel manifold to the nacelles' pylons, and a suit locker for the space walk out to the nacelles.");
        var plan = new Rect(Cx - Ew - 0.25, ZEngW - 0.125, Cx + Ew + 0.25, ZEngEnd);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate");
        Floor(p, [new Rect(plan.X0, ZEngW + 0.125, plan.X1, ZEngAW - 0.125)], ECeil, ERoof, Ceil, "painted plating");
        Room(p, plan, 0, ERoof);
        Ceils[p] = ECeil;
        Detail(p, Cx - 0.75, Ft, ZEngW + 0.3, Cx + 0.75, Ft + 0.012, ZEngAW - 0.3, Grate, "painted grating");
        foreach (var x in new[] { -0.79, 0.75 })
            Detail(p, Cx + x, Ft + 0.004, ZEngW + 0.3, Cx + x + 0.04, Ft + 0.016, ZEngAW - 0.3, Hazard, "hazard paint");
        WallRun(p, true, plan.X0, plan.X1, ZEngW, Ft, ECeil, Door(Cx - 0.75));
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZEngW + 0.125, ZEngAW - 0.125, Cx + s * (Ew + 0.125), Ft, ECeil);
            var face = Cx + s * Ew;
            OnWall(p, () =>
            {
                // The fuel manifold: a feed up the wall to the pylon's pass-through, a valve wheel and a gauge on it.
                foreach (var z in new[] { 21.4, 22.6 })
                    Detail(p, face - s * 0.12, Ft + 0.004, z - 0.05, face - s * 0.02, ECeil - 0.004, z + 0.05, z < 22 ? Red : Bare, "painted steel");
                Detail(p, face - s * 0.16, 1.1, 21.25, face - s * 0.12, 1.4, 21.55, Red, "painted steel");
                Detail(p, face - s * 0.2, 1.75, 21.9, face - s * 0.004, 2.25, 22.3, Frame, "dirty steel");
                Detail(p, face - s * 0.205, 1.85, 22.0, face - s * 0.2, 2.15, 22.2, Paper, "plastic");
                Detail(p, face - s * 0.14, ECeil - 0.3, 21.3, face - s * 0.004, ECeil - 0.18, 22.7, Hazard, "hazard paint");
            });
        }
        WallRun(p, true, plan.X0, plan.X1, ZEngAW, Ft, ECeil);
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZEngW + 0.6, ZEngAW - 0.6, ECeil);
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZEng, ZEngEnd))
            {
                Band(p, 2, s, za, zb, Skin, 21 + i);
                Band(p, 3, s, za, zb, Skin, 22 + i, plates: false);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.24, 0.1, Armour);
                Chine(p, 3, s, za, zb, 0.18, 0.08, Frame);
            }
        Ribs(p, ZEng, ZEngEnd, 2, 3);
        // The nacelles' pylons' hardpoints on the flanks.
        foreach (var s in new[] { -1, 1 })
        {
            // (in two halves meeting at the station, one to each band's hull piece)
            Solid(p, Cx + s * 3.3, NacY - 0.32, PylonZ - 0.6, Cx + s * PylonX, NacY + 0.32, PylonZ - 0.003, Frame, "dirty steel");
            Solid(p, Cx + s * 3.3, NacY - 0.32, PylonZ + 0.003, Cx + s * PylonX, NacY + 0.32, PylonZ + 0.6, Frame, "dirty steel");
            Sock(p, s < 0 ? "nacL" : "nacR", Cx + s * PylonX, NacY, PylonZ, s < 0 ? "-x" : "+x");
        }
        foreach (var (_, za, zb) in BandsIn(ZEng, ZEngEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, ERoof) })
                VoidT(p, za, zb, ya, yb, Ew + 0.25, za == ZEng ? 0.25 : 0.004, zb == ZEngEnd ? 0.0 : 0.004);
        DoorSock(p, "doorEng", true, Cx - 0.75, ZEngW, Ft);
        Sock(p, "panel", Cx - Ew, Ft + 0.6, 19.8, "+x");
        Sock(p, "board", Cx - 1.9, Ft + 0.9, ZEngW + 0.125, "+z");
        Sock(p, "plant", Cx + 1.85, Ft, 24.85, "+y");
        for (var i = 0; i < 3; i++)
            Sock(p, $"battery{i}", Cx + Ew - 0.25, Ft, 19.0 + i * 0.7, "+y");
        Sock(p, "suit", Cx - 2.2, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "locker", Cx - 1.4, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "tools", Cx - Ew, Ft + 0.9, 24.0, "+x");
        Sock(p, "lampEngL", Cx - Ew, Ft + 2.3, 22.9, "+x");
        Sock(p, "lampEngR", Cx + Ew, Ft + 2.3, 23.2, "-x");
        Sock(p, "switchEng", Cx + 1.2, Ft + 1.3, ZEngW + 0.125, "+z");
        foreach (var z in new[] { plan.Z0, plan.Z1 })
            foreach (var y in new[] { 0.0, ERoof })
                MustFit("engineering", z, Ew + 0.25, y);
        return p.Stats(16000, 32000, 30, 15.0);
    }

    // ---- the stern: the hull closing in behind engineering, the transom, the stern's attitude jets

    /// <summary>The nose cap: a slightly rounded solid dome sealing the blunt glass nose, lofted from the first station's section in
    /// shrinking rings, with a dark rim where it meets the glass and a hidden block back to the cockpit's nose void.</summary>
    private static Part NoseCap()
    {
        var p = Big("Packet Nose Cap", "The Packet's nose cap: a rounded dome of glass panes sealing the front of the glass cockpit, split down the middle, each pane in dark beading like the canopy's, with the nav beacon at its tip.", color: Skin);
        const double cy = 1.35, bw = 0.05, bh = 0.03; // (the dome's axis height; the beading's width and how far it stands proud)
        var sec = At(Zt);
        // Each ring goes round the section scaled about the axis: up the port side, across the top through its middle, down the
        // starboard side and back across the bottom through its middle, so the cap splits into two halves on the centreline.
        double[][] Ring(double dz, double k)
        {
            var pts = new List<double[]>();
            double[] At3(double x, double y) => Sh([Cx + x * k, cy + (y - cy) * k, Zt - dz]);
            pts.Add(At3(0, sec[0].Y));
            foreach (var q in sec)
                pts.Add(At3(-q.H, q.Y));
            pts.Add(At3(0, sec[^1].Y));
            for (var i = sec.Length - 1; i >= 0; i--)
                pts.Add(At3(sec[i].H, sec[i].Y));
            return [.. pts];
        }
        (double Dz, double K)[] rings = [(0.004, 1.0), (0.24, 0.93), (0.44, 0.72), (0.57, 0.42)];
        var R = rings.Select(r => Ring(r.Dz, r.K)).ToArray();
        int n = R[0].Length, bands = R.Length - 1;
        var back = Sh([Cx, cy, Zt + 0.6]); // (a point behind the cap: every pane faces away from it)
        double[] Outward(double[] nrm, double[] at) => Dot(nrm, Sub(at, back)) < 0 ? [-nrm[0], -nrm[1], -nrm[2]] : nrm;
        double[] Mid(params double[][] q) => [q.Average(v => v[0]), q.Average(v => v[1]), q.Average(v => v[2])];
        // The panes, glass like the canopy's: one flat plate each (a ring is the last scaled about the axis, so each quad is flat).
        var N = new double[bands + 1][][];
        for (var i = 0; i < bands; i++)
        {
            N[i] = new double[n][];
            for (var j = 0; j < n; j++)
            {
                double[] a0 = R[i][j], a1 = R[i][(j + 1) % n], b1 = R[i + 1][(j + 1) % n], b0 = R[i + 1][j];
                N[i][j] = Outward(Unit(Cross(Sub(a1, a0), Sub(b0, a0))), Mid(a0, a1, b1, b0));
                Quad(p, a0, a1, b1, b0, Glass, "glass", true);
            }
        }
        double[] tipN = [0, 0, -1];
        N[bands] = [.. Enumerable.Range(0, n).Select(_ => tipN)];
        // The tip: two flat halves, port and starboard of the centreline.
        var half = n / 2;
        Plate(p, [.. Enumerable.Range(0, half + 1).Select(j => R[bands][j])], Glass, "glass", true);
        Plate(p, [.. Enumerable.Range(half, half + 1).Select(j => R[bands][j % n])], Glass, "glass", true);
        // The beading over every pane's edges: a bar from a to b standing out along nrm.
        void Bead(double[] a, double[] b, double[] nrm, double w, double lift)
        {
            var d = Unit(Sub(b, a));
            var side = Unit(Cross(d, nrm));
            double[][] End(double[] q) => [.. new[] { (-1, -0.006 - lift), (1, -0.006 - lift), (1, T / 2 + bh + lift), (-1, T / 2 + bh + lift) }.Select(t => new[] { q[0] + side[0] * t.Item1 * w / 2 + nrm[0] * t.Item2, q[1] + side[1] * t.Item1 * w / 2 + nrm[1] * t.Item2, q[2] + side[2] * t.Item1 * w / 2 + nrm[2] * t.Item2 })];
            double[] Ext(double[] q, double s) => [q[0] + d[0] * s, q[1] + d[1] * s, q[2] + d[2] * s];
            Prism(p, End(Ext(a, -w / 2)), End(Ext(b, w / 2)), Frame, "painted plating");
        }
        double[] Avg(double[] u, double[] v) => Unit([u[0] + v[0], u[1] + v[1], u[2] + v[2]]);
        for (var i = 0; i <= bands; i++)
            for (var j = 0; j < n; j++)
            {
                // Round each ring (the first, where the cap meets the canopy, a little wider), and along each pane's sides.
                var below = i == 0 ? N[0][j] : N[i - 1][j];
                Bead(R[i][j], R[i][(j + 1) % n], i == 0 ? below : Avg(below, N[i][j]), (i == 0 ? bw * 1.6 : bw) + (j % 2) * 0.004, (j % 2) * 0.003); // (neighbours differ by a few mm: where two beads run on in line or cross, no faces share a plane)
                if (i < bands)
                    Bead(R[i][j], R[i + 1][j], Avg(N[i][(j + n - 1) % n], N[i][j]), bw + 0.009 + (i % 2) * 0.004, 0.0065 + (i % 2) * 0.003);
            }
        Bead(R[bands][0], R[bands][half], tipN, bw + 0.018, 0.013); // (down the middle of the tip)
        // A beacon lamp at the tip, over the centre bead.
        var tz = Zt - rings[^1].Dz - T / 2;
        Detail(p, Cx - 0.07, cy + 0.3, tz - bh - 0.03, Cx + 0.07, cy + 0.42, tz - bh + 0.004, Red, glow: true);
        Hidden(p, Cx - 0.6, Ft, Zt - 0.3, Cx + 0.6, 1.9, ZCkFloor - 0.45);
        return p.Stats(900, 6000, 30, 10.0);
    }

    private static Part SternPart()
    {
        var p = Big("Packet Stern", "The Packet's stern: the hull tucking in behind engineering to a transom with its tail lights, and the stern's attitude jets.", color: Skin);
        Facet(p, 0, 1, ZEngEnd, Stern, Armour, "plated steel");
        Facet(p, 5, 1, ZEngEnd, Stern, Skin2, "painted plating");
        Slab(p, 5, 1, ZEngEnd, Stern, 0.12, 0.9, 0.12, 0.88, 0.02, 0.1, 0.05, Armour);
        foreach (var s in new[] { -1, 1 })
        {
            for (var k = 1; k <= 4; k++)
            {
                Facet(p, k, s, ZEngEnd, Stern, k % 2 == 0 ? Skin : Skin2, "painted plating");
                Slab(p, k, s, ZEngEnd, Stern, 0.14, 0.92, 0.1, 0.9, 0.02, k == 2 ? 0.14 : 0.1, 0.05, k == 3 ? Armour : Skin, "painted plating");
            }
            Chine(p, 1, s, ZEngEnd, Stern, 0.2, 0.09, Frame);
            Chine(p, 2, s, ZEngEnd, Stern, 0.24, 0.1, Armour);
            Chine(p, 3, s, ZEngEnd, Stern, 0.18, 0.08, Frame);
            Chine(p, 4, s, ZEngEnd, Stern, 0.16, 0.08, Armour);
        }
        Ribs(p, ZEngEnd, Stern, 0, 1, 2, 3, 4, 5);
        // The transom: a dark plate, tail lights, a docking floodlight, a hazard band over the jets.
        var q = At(Stern);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, Stern)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, Stern)))], Frame, "soot");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 2.0 - 0.15, 1.3, Stern + 0.02, Cx + s * 2.0 + 0.15, 1.7, Stern + 0.06, Red, glow: true);
            Detail(p, Cx + s * 1.4 - 0.25, 2.6, Stern + 0.02, Cx + s * 1.4 + 0.25, 2.66, Stern + 0.05, Hazard, "hazard paint");
        }
        Detail(p, Cx - 0.3, 2.2, Stern + 0.02, Cx + 0.3, 2.4, Stern + 0.07, Glow, glow: true);
        foreach (var (ya, yb) in new[] { (-0.5, 0.0), (0.0, 1.5), (1.5, 3.0), (3.0, 3.3) })
            VoidT(p, ZEngEnd, Stern, ya, yb, 0, 0.0, 0.03);
        var sq = At(SternRcsZ);
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            RcsPad(p, "rcsUp" + n, Cx + s * 1.0, sq[4].Y, SternRcsZ, "+y", 0.05);
            RcsPad(p, "rcsDown" + n, Cx + s * 1.0, sq[0].Y, SternRcsZ, "-y", 0.05);
            RcsPad(p, "rcsOut" + n, FacetX(2, s, SternRcsZ, 0.9), 0.9, SternRcsZ, s < 0 ? "-x" : "+x", 0.28);
            RcsPad(p, "rcsAlong" + n, Cx + s * 0.9, 0.6, Stern, "+z", 0.05);
        }
        return p.Stats(6000, 20000, 35, 10.0);
    }

    // ---- the nacelles: a fuel tank forward, the engine housing aft (the drive outside its tail plate, its engine inside), a swept pylon

    private static Part Nacelle(int s)
    {
        var name = s < 0 ? "Packet Nacelle L" : "Packet Nacelle R";
        var p = new Part(name, Small, Section, $"One of the Packet's drive nacelles, {(s < 0 ? "to port" : "to starboard")}: a fuel tank forward of an engine housing, on a swept pylon off engineering's flank. The drive bolts to the housing's tail plate outside, its Drive Core and Fuel Pump inside behind it, mended through the service hatch in its outboard flank on a space walk.", role: "fueltank", tool: "welder", material: "steel", color: Skin, group: Group);
        double cx = Cx + s * NacX, cy = NacY, f = NacF, c = EngineHousing.HalfFlat(NacF);
        // The nose: a yellow tip, a short cone; the tank: cream plates in bands with blue between, collars over the seams.
        EngineHousing.Ring(p, P, cx, cy, 0.3, 0.12, NacNose, 0.55, 0.23, NacNose + 0.35, Hazard, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, 0.55, 0.23, NacNose + 0.354, f, c, NacTank0, Skin, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, f, c, NacTank0 + 0.004, f, c, 19.6, Skin2, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, f, c, 19.604, f, c, 20.6, Armour, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, f, c, 20.604, f, c, NacTank1 - 0.004, Skin, "painted plating");
        foreach (var z in new[] { 18.6, 22.0, 24.4 })
            EngineHousing.Ring(p, P, cx, cy, f + 0.03, c + 0.012, z - 0.08, f + 0.03, c + 0.012, z + 0.08, Frame, "dirty steel");
        EngineHousing.Draw(p, P, cx, cy, f, c, NacTank1, NacTail, s, Skin, Skin2, Frame, Hazard, "worn paint", ShipWorksParts.CoreR[1], ShipWorksParts.CoreD[1], ShipWorksParts.CoreD[1] + ShipWorksParts.PumpD[1]);
        EngineHousing.Ring(p, P, cx, cy, f + 0.035, c + 0.014, NacTank1 - 0.1, f + 0.035, c + 0.014, NacTank1 + 0.1, Armour, "plated steel"); // (the band over the bulkhead)
        // A fuel line along its inboard flank, a grab rail and a step under the hatch, a fill cap, stencilled hazard ticks.
        Detail(p, cx - s * (f + 0.02), cy - 0.35, 18.4, cx - s * (f + 0.08), cy - 0.29, NacTank1 - 0.2, Red, "painted steel");
        foreach (var z in new[] { 27.9, 29.9 })
            Detail(p, cx + s * (f + 0.02), cy + 0.5, z - 0.03, cx + s * (f + 0.3), cy + 0.54, z + 0.03, "#9a9c98", "steel");
        Detail(p, cx + s * (f + 0.28), cy + 0.5, 27.85, cx + s * (f + 0.33), cy + 0.56, 29.95, Hazard, "worn paint");
        Detail(p, cx + s * (f - 0.05), cy - 0.62, 28.4, cx + s * (f + 0.3), cy - 0.58, 29.7, Dark, "tread plate");
        Detail(p, cx - 0.1, cy + f + 0.004, 23.1, cx + 0.1, cy + f + 0.12, 23.3, Red, "worn paint");
        // The pylon: a solid spar from the hardpoint into the tank, under a swept fairing; the fuel feed along it.
        Solid(p, Cx + s * PylonX, cy - 0.2, 19.6, cx - s * (f - 0.03), cy + 0.2, 24.4, Frame, "dirty steel");
        Prism(p, [P(Cx + s * (PylonX + 0.004), cy - 0.32, 19.0), P(Cx + s * (PylonX + 0.004), cy + 0.32, 19.0), P(Cx + s * (PylonX + 0.004), cy + 0.32, 25.0), P(Cx + s * (PylonX + 0.004), cy - 0.32, 25.0)],
            [P(cx - s * (f + 0.004), cy - 0.24, 19.8), P(cx - s * (f + 0.004), cy + 0.24, 19.8), P(cx - s * (f + 0.004), cy + 0.24, 24.2), P(cx - s * (f + 0.004), cy - 0.24, 24.2)], Armour, "painted plating");
        Detail(p, Cx + s * (PylonX + 0.2), cy + 0.27, 19.9, cx - s * (f + 0.1), cy + 0.33, 20.4, Hazard, "hazard paint");
        Hidden(p, cx - 0.82, cy - 0.82, NacTank0, cx + 0.82, cy + 0.82, NacTank1 - 0.004);
        Sock(p, "hull", Cx + s * PylonX, cy, PylonZ, s < 0 ? "+x" : "-x");
        Works.Mount(p, outside: true); // (the housing's tail plate: the drive bolts to it outside, its engine inside)
        RcsPad(p, "rcsFore", cx, cy, NacNose + 0.06, "-z", 0.05);
        // Pitch jets on the tank's top and bottom flats, far aft: the long spine wants the leverage to turn its nose.
        RcsPad(p, "rcsUp", cx, cy + f, 23.6, "+y", 0.05);
        RcsPad(p, "rcsDown", cx, cy - f, 23.6, "-y", 0.05);
        p.Set(("fuelTank", Kw(("capacity", NacFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx - s * 0.82, cy, 23.0).Cast<object>()], s < 0 ? "+x" : "-x", "fuel"))));
        return p.Stats(3500, 7000, 20, 10.0);
    }

    // ---- the cargo rack: an open frame bolted to a spine hardpoint, two tiers, each holding one 8-foot cube container

    /// <summary>
    /// A Packet cargo rack: an open steel frame on a spine hardpoint, three shelves of rails make two tiers, each holding one 8-foot
    /// cube container (Cargo Container 8 ft or Cargo Tank 8 ft). X-braced on its outer face and ends, the loads strapped down, marker
    /// lamps on its outer corners. A frozen Cargo Volume on its top holds what is in it. Origin at its hardpoint face; +x out.
    /// </summary>
    private static Part Rack()
    {
        var p = new Part("Packet Cargo Rack", Small, Section, "One of the Packet's cargo racks: an open frame strapped to the spine, two tiers each holding one 8-foot cube container, X-braced and lit at its corners. The Cargo Volume on its top holds the containers in it.", role: "frame", tool: "welder", material: "steel", color: RackCol, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: Kw(("finish", fin))));
        double d = RkD, hy = RkHy, hz = RkHz, sh = RkSh, po = RkPost;
        // The hardpoint's block and the strongback up the inner face.
        B(0, -0.42, -0.36, 0.14, 0.42, 0.36, Frame, "dirty steel");
        B(0, -hy + sh, -0.15, 0.12, hy - sh, 0.15, RackCol, "painted steel");
        // The posts: inner and outer, at both ends.
        foreach (var x in new[] { 0.0, d - po })
            foreach (var sz in new[] { -1, 1 })
                B(x, -hy, sz > 0 ? hz - po : -hz, x + po, hy, sz > 0 ? hz : -hz + po, RackCol, "painted steel");
        // The shelves: a frame of rails round each level, two load rails along it on the lower two.
        foreach (var (y0, lo) in new[] { (-hy, true), (-sh / 2, true), (hy - sh, false) })
        {
            double y1 = y0 + sh;
            foreach (var x in new[] { 0.0, d - po })
                B(x, y0, -hz + po, x + po, y1, hz - po, RackCol, "painted steel");
            foreach (var sz in new[] { -1, 1 })
                B(po, y0, sz > 0 ? hz - po : -hz, d - po, y1, sz > 0 ? hz : -hz + po, RackCol, "painted steel");
            if (lo)
                foreach (var x in new[] { 0.7, 2.15 })
                    B(x, y0, -hz + po, x + 0.12, y1, hz - po, RackCol, "painted steel");
            else
                B(po, y0, -0.06, d - po, y1, 0.06, RackCol, "painted steel");
        }
        // X-bracing on the outer face and the ends, a tier at a time, standing 5 mm off the frame.
        void Diag(double[] a, double[] b, double[] n, double w)
        {
            double[] u = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
            var l = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
            double[] v = [n[1] * u[2] - n[2] * u[1], n[2] * u[0] - n[0] * u[2], n[0] * u[1] - n[1] * u[0]];
            for (var k = 0; k < 3; k++)
                v[k] *= w / 2 / l;
            Plate(p, [[a[0] - v[0], a[1] - v[1], a[2] - v[2]], [a[0] + v[0], a[1] + v[1], a[2] + v[2]], [b[0] + v[0], b[1] + v[1], b[2] + v[2]], [b[0] - v[0], b[1] - v[1], b[2] - v[2]]], RackCol, "painted steel", thick: 0.02);
        }
        foreach (var (y0, y1) in new[] { (-hy + sh, -sh / 2), (sh / 2, hy - sh) })
        {
            // (the outer face is open: a container slides out of it to a shell or a crane)
            foreach (var sz in new[] { -1, 1 })
            {
                var zo = sz * (hz + 0.016);
                Diag([po, y0 + 0.05, zo], [d - po, y1 - 0.05, zo], [0, 0, 1], 0.08);
                Diag([po, y1 - 0.05, zo + sz * 0.022], [d - po, y0 + 0.05, zo + sz * 0.022], [0, 0, 1], 0.08);
            }
            // The straps over each load: two yellow bands down the outer face, a ratchet on each.
            foreach (var z in new[] { -2.2, 0.0, 2.2 })
            {
                B(d - po - 0.08, y0 + 0.004, z - 0.04, d - po - 0.06, y1 - 0.004, z + 0.04, Hazard, "rubber", true);
                B(d - po - 0.1, (y0 + y1) / 2 - 0.1, z - 0.06, d - po - 0.062, (y0 + y1) / 2 + 0.1, z + 0.06, Frame, "steel", true);
            }
        }
        // Marker lamps on the outer corners (amber), a stencilled number plate on the top rail.
        foreach (var sy in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                B(d + 0.004, sy * (hy - 0.1) - 0.04, sz * (hz - 0.075) - 0.04, d + 0.05, sy * (hy - 0.1) + 0.04, sz * (hz - 0.075) + 0.04, "#ffb040", "glass", true);
        B(d + 0.004, hy - sh + 0.02, -0.4, d + 0.012, hy - 0.02, 0.4, Paper, "painted plating", true);
        B(d + 0.012, hy - sh + 0.035, -0.32, d + 0.016, hy - 0.035, -0.12, Armour, "painted plating", true);
        p.Sock("hull", [0, 0, 0], "-x");
        p.Sock("volume", [1.5, hy, 0.0], "+y");
        return p.Stats(1300, 6000, 25, 8.0);
    }

    // ---- the flying parts: lift jets, the power plant, the flight computer (the Dray's kinds, in the Traders' colours and size)

    private static Part LiftJet()
    {
        const double k = JetScale;
        var p = new Part("Packet Lift Jet", Small, Section, $"A lift jet: {LiftForce / 1e6:0.0} MN straight down. Four under the Packet's cab and engineering hold it up loaded, from the keel's tanks.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
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
        var p = new Part("Packet Power Plant", Small, Section, "The Packet's power plant: a stack of cells in a cage, 400 kW for the docks, the lights, the life support and the flight computer. No fuel.", role: "generator", tool: "welder", material: "steel", color: Armour, group: Group);
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

    /// <summary>The flight computer: a flight stabilizer with a freighter's gyro in a console between the couches and the dash.</summary>
    private static Part FlightComputer()
    {
        const string console = "#22262b";
        var p = new Part("Packet Flight Computer", Small, Section, "The Packet's flight computer: a flight stabilizer with a freighter's gyro in a console between the couches. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: console, group: Group);
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

    // ---- the freight: the standard container ("Design the standard cargo container", ContainerParts.cs), origin at its roof's middle

    public const string BoxName = "Standard Container 2m";
    public const string HeavyBoxName = "Heavy Container 2m"; // (the bottom shelf's: a store's heavy furniture, Karl 2026-11-22)

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
        Part WallPiece(int i) => wallPieces[i] ??= Big($"Packet {shortName} Wall {i + 1}", $"A wall of the Packet's {what}, with its door frames and what is fixed to it.", color: Wall);
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
                    hull[key] = h = Big($"Packet {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece beside the Packet's {what}, the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (ceilY is double y && c[1] > y - 0.35)
                return ceiling ??= Big($"Packet {shortName} Ceiling", $"The ceiling of the Packet's {what}.", color: Ceil);
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
                    bands[k] = piece = Big($"Packet {shortName} {k + 1}", $"A length of the Packet's {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
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
                Console.WriteLine($"freighter_parts: {shortName} band {k + 1} has only details; they join {to.D["name"]}");
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
            throw new InvalidOperationException("Packet: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
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
        var bp = new BuildingParts.Blueprint(ShipName, "The Traders' spine freighter: a crew cab under a small glass cockpit, then a thin walkable spine with twelve open cargo racks strapped along its flanks, six a side, each holding two 8-foot cube containers one over the other, and an engineering block aft with a drive nacelle on a pylon to each side. The crew live in the cab: bunks, a head, a trade office, a galley, an airlock over the boarding shaft and the stores. Lift jets and legs under the cab and engineering set it down; attitude jets at both ends and on the nacelles' noses move it about. Its containers ride in frozen cargo volumes, one on each rack.", defs, maxSteps: 600);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in Structure)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = placed;
        int keel = placed["Packet Keel"], roof = placed["Packet Roof"], cab = placed["Packet Cab"], spine = placed["Packet Spine"],
            eng = placed["Packet Engineering"], stern = placed["Packet Stern"];
        foreach (var d in new[] { "doorCk", "doorAft", "doorBunk", "doorHead", "doorOffice", "doorGalley", "doorAirlock", "doorStores" })
            Door(bp, cab, d);
        Door(bp, eng, "doorEng");
        // The cockpit: the pilot flies through the flight computer between the couches and the glass (Ship keys, the mouse aims the nose).
        var pilot = Hang(bp, "Flight Couch", cab, "pilot", "bottom");
        Hang(bp, "Flight Couch", cab, "copilot", "bottom");
        var fc = Hang(bp, "Packet Flight Computer", cab, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, fc));
        // The bunk room, the head and the trade office to port.
        Hang(bp, "Bunk", cab, "bunk0", "bottom");
        Hang(bp, "Bunk", cab, "bunk1", "bottom");
        Paint(bp, Hang(bp, "Service Locker", cab, "lockerBunk", "wall"), Armour);
        Hang(bp, "Ship Toilet", cab, "toilet", "wall");
        Hang(bp, "Ship Basin", cab, "basin", "wall");
        Hang(bp, "Shower Stall", cab, "shower", "wall");
        Hang(bp, "Computer", cab, "computerDesk", "foot00");
        Hang(bp, "Server Rack", cab, "server0", "foot00");
        Hang(bp, "Server Rack", cab, "server1", "foot00");
        Paint(bp, Hang(bp, "Filing Cabinet", cab, "cabinet", "wall"), Skin);
        // The galley, the airlock and the stores to starboard.
        Hang(bp, "Ship Galley Range", cab, "range", "back");
        Hang(bp, "Galley Coffee Station", cab, "coffee", "wall");
        bp.HangTurned("Ship Mess Table", Holder(bp, cab, "table"), "table", "bottom", BuildingParts.RY90);
        EvaParts.FitDock(bp, Holder(bp, cab, "mmuDock"), "mmuDock");
        foreach (var n in new[] { "suit0", "suit1" })
            Hang(bp, EvaParts.LockerName, cab, n, "wall");
        Hang(bp, EvaParts.LifeSupportName, cab, "lifeSupport", "wall");
        Hang(bp, EvaParts.OxygenTankName, cab, "o2Tank0", "wall");
        Switch(bp, cab, "switchHatch", false, BoardingShaft.Fit(bp, Shaft, Holder(bp, cab, "boarding"), "boarding", Holder(bp, cab, "deckHatch"), "deckHatch", Holder(bp, keel, "bellyHatch"), "bellyHatch")); // (off: both hatches shut)
        // Engineering: the panel, the power and a suit for the walk out to the nacelles.
        Hang(bp, "Engineer's Panel", eng, "panel", "back");
        Hang(bp, "Power Board", eng, "board", "back");
        Hang(bp, "Packet Power Plant", eng, "plant", "bottom");
        for (var i = 0; i < 3; i++)
            Hang(bp, "Battery", eng, $"battery{i}", "-y");
        Hang(bp, EvaParts.LockerName, eng, "suit", "wall");
        Paint(bp, Hang(bp, "Pressure Locker", eng, "locker", "wall"), Armour);
        Hang(bp, "Tool Rack", eng, "tools", "back");
        // The nacelles: each on its pylon, its drive on the tail plate outside, the engine inside, the hatch in its outboard flank.
        foreach (var (s, n) in new[] { (-1, "L"), (1, "R") })
        {
            var nac = Hang(bp, $"Packet Nacelle {n}", eng, $"nac{n}", "hull");
            Hang(bp, "Drive Nozzle M", nac, "drive", "mount");
            Hang(bp, "Fuel Pump M", Hang(bp, "Drive Core M", nac, "core", "front"), "back", "front");
            Paint(bp, Works.HangHatch(bp, nac, s), Armour, "worn paint");
            foreach (var j in new[] { "rcsFore", "rcsUp", "rcsDown" })
                Paint(bp, Hang(bp, Rcs, nac, j, "base"), Frame, "dirty steel");
        }
        // The attitude jets: the bow's up, down and out, the stern's every way.
        foreach (var n in new[] { "L", "R" })
        {
            Paint(bp, Hang(bp, Rcs, roof, $"rcsUp{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, keel, $"rcsDown{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, cab, $"rcsOut{n}", "base"), Frame, "dirty steel");
            foreach (var d in new[] { "Up", "Down", "Out", "Along" })
                Paint(bp, Hang(bp, Rcs, stern, $"rcs{d}{n}", "base"), Frame, "dirty steel");
        }
        // Lift jets and legs under the keel.
        for (var i = 0; i < LiftAt.Length; i++)
            Hang(bp, "Packet Lift Jet", keel, $"lift{i}", "top");
        for (var i = 0; i < LegAt.Length; i++)
            LegPodParts.HangPod(bp, LegPod, Holder(bp, keel, $"leg{i}"), $"leg{i}");
        // The racks: twelve on the spine's hardpoints, a frozen Cargo Volume on each holding its two containers.
        var cargo = new List<object?>();
        // A container's hatch is its whole +x side: to port it is turned half round so the hatch faces outboard.
        var halfRound = Enumerable.Range(0, 24).First(r => BuildingParts.Apply(r, [1, 0, 0]) is [-1, 0, 0] && BuildingParts.Apply(r, [0, 1, 0]) is [0, 1, 0]);
        for (var i = 0; i < RackZ.Length; i++)
            foreach (var (s, n) in new[] { (-1, "L"), (1, "R") })
            {
                var rack = Paint(bp, Hang(bp, "Packet Cargo Rack", spine, $"rack{n}{i}", "hull"), RackCol, "painted steel");
                var vol = bp.Hang("Cargo Volume", rack, "volume", "-y");
                bp.Steps[vol]["config"] = new PyDict
                {
                    ["startOn"] = true, ["sizeX"] = 2.7, ["sizeY"] = 5.5, ["sizeZ"] = 6.8,
                    ["atX"] = 0.0, ["atY"] = Py.Round(-(RkHy + 0.1), 2), ["atZ"] = 0.0,
                };
                // The containers stand on the bottom and middle shelves; their origin is their roof's middle.
                var x = Cx + s * (RackX + RkD / 2);
                foreach (var y in new[] { -RkHy + RkSh + 0.003 + CtH, RkSh / 2 + 0.003 + CtH })
                    cargo.Add(Kw(("part", y < 0 ? HeavyBoxName : BoxName), // (the bottom shelf a heavy container: beds, lockers and wall bays ride low)
                        ("at", Py.L(Py.Round(x - H, 3), Py.Round(RackY + y - H, 3), Py.Round(RackZ[i] - H, 3))), ("turn", s < 0 ? halfRound : 0)));
            }
        // Lamps and their switches (the spine's and engineering's light while the power reaches them).
        Switch(bp, cab, "switchCk", Lamp(bp, cab, "lampCkL", "#ffd080"), Lamp(bp, cab, "lampCkR", "#ffd080"));
        Switch(bp, cab, "switchBunk", Lamp(bp, cab, "lampBunk", "#ffe0b0"));
        Switch(bp, cab, "switchHead", Lamp(bp, cab, "lampHead"));
        Switch(bp, cab, "switchOffice", Lamp(bp, cab, "lampOffice", "#f0f4ff"));
        Switch(bp, cab, "switchGalley", Lamp(bp, cab, "lampGalley", "#ffe0b0"));
        Lamp(bp, cab, "lampAirlock", "#ffd040");
        Lamp(bp, cab, "lampStores");
        Lamp(bp, cab, "lampHallA");
        Lamp(bp, cab, "lampHallB");
        for (var i = 0; i < 5; i++) // (one a length of the spine's lining)
            Lamp(bp, spine, $"lamp{i}", "#d8ecff");
        Switch(bp, eng, "switchEng", Lamp(bp, eng, "lampEngL", "#a0d0ff"), Lamp(bp, eng, "lampEngR", "#a0d0ff"));
        var json = bp.Json();
        json["cargo"] = cargo;
        return json;
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
        structure.AddRange(SplitBands(Keel(), "Keel", -14.0)); // (the band under the airlock keeps the keel's name: the jets and legs find it)
        structure.AddRange(SplitDeck(Cab(), "Cab"));
        structure.AddRange(SplitDeck(Spine(), "Spine"));
        structure.AddRange(SplitDeck(Engineering(), "Eng"));
        structure.AddRange(SplitBands(Roof(), "Roof", -14.0));
        structure.Add(SternPart());
        structure.Add(NoseCap());
        Connect(structure);
        Structure = PlaceOrder(structure, "Packet Keel");
        List<Part> parts =
        [
            .. structure, Nacelle(-1), Nacelle(1), Rack(),
            .. BoardingShaft.Parts(Shaft), LiftJet(), PowerPlant(), FlightComputer(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"freighter_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/PacketSpineFreighter.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
    }
}
