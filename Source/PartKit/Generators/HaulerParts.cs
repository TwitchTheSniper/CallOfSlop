using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Dray, a container hauler (Karl, 2026-11-16: "a hauler ship ... just the frame and interior walls/doors"; "10-20 cargo
/// containers, onboard crane to pick them up"). Survival is logistics: the Dray is the ship that moves the goods. It is built round
/// its hold: fourteen container slots (three lanes by five rows, less the slot over the well), a Medium Bridge Crane running the
/// hold's length on girders along both walls, and a well in the floor with two sliding belly doors, so it sets down over a container
/// on long legs, opens the well and the crane lifts the box straight up into the hold. A small crew section forward (two decks: the
/// bridge, two cabins, the head; a mess, stores, the suit room and the airlock), a drive room aft (docs/sandbox/constructs/hauler.md).
///
/// Built as the Zero Day is (TorpedoFrigateParts.cs, whose helpers this copies): a hull lofted through stations first, the rooms a box
/// of their own inside it, then each big part drawn whole and dealt out into welded pieces (floors, wall runs, ceilings, hull bands),
/// so a hit takes a piece, not a deck. Ship metres: x across (the centreline at Cx), y up from the lower deck's underside, z from the
/// bow back (forward is -z); P() moves them by half a cell into a part's frame. This first cut is the frame, the walls and the doors
/// only: no lamps, wiring, fittings, engines or legs yet (Karl's scope).
/// </summary>
public sealed class HaulerParts : IPartGenerator
{
    public string Name => "hauler_parts";

    private const string Out = "Constructs/Parts/Dray";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Dray hauler";
    public const string ShipName = "Dray Container Hauler";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: a freight line's. Freight-blue plate over a pale weathered skin, a dark frame, bare steel panels, hazard orange where
    // things move and where people go in. Inside: pale grey walls over a blue-grey dado; the hold in darker steel with a yellow dado.
    private const string Skin = "#a3a8a6", Skin2 = "#8d9391", Armour = "#2f5d80", Armour2 = "#264b67", Bare = "#666d71", Frame = "#202326",
        Hazard = "#e0a020", Orange = "#dd6a1e", Red = "#e0402c", Glow = "#ffe0a0", Glass = "#7f9ea8",
        Wall = "#b9bdb5", WallLow = "#3d5569", WallTrim = "#2a2e31", Deck = "#4b4f51", Grate = "#3a3d3f", Ceil = "#8f969a",
        HoldWall = "#71787c", HoldDado = "#c08a1c", HoldDeck = "#45494a";

    /// <summary>The walls WallRun draws with (the hold's are darker).</summary>
    private static string WallCol = Wall, DadoCol = WallLow;

    // ---- the plan

    private const double Cx = 7.0; // the centreline
    private const double Ft0 = 0.25, Ceil0 = 3.25, Ft1 = 3.5, Ceil1 = 6.5, Roof = 6.75; // the crew decks' floor tops and ceilings
    private const double HCeil = 7.25, HRoof = 7.5; // the hold's ceiling and roof
    private const double ZFront = 5.0, ZHold = 14.0, ZHoldEnd = 46.0, ZEngEnd = 49.5, Stern = 54.0;
    private const double Lw = 4.0, UwF = 3.0, UwA = Lw, Hw = 5.75; // inner half widths: the crew decks, the bridge, the upper aft hall, the hold
    private const double ZCab = 9.0, ZAftHall = 11.5; // the bridge's back wall; the cabins' back wall, where the upper deck widens
    private const double DoorH = 2.25;

    /// <summary>The hold's rows (a 20-foot container, 6.1 m, with room round it), its three lanes' middles from the centreline.</summary>
    private static readonly double[] Rows = [14.0, 20.4, 26.8, 33.2, 39.6, 46.0];
    private static readonly double[] Lanes = [-2.7, 0.0, 2.7];
    private const double BoxL = 6.06, BoxW = 2.44; // a container's floor print

    /// <summary>The crane: two runs of Crane Rail Medium 4 m, 8 m apart (the Medium Crane Bridge's span), on girders along the walls.</summary>
    private const double RailX = 4.0, RailY = 5.25, RailZ0 = 16.0;
    private const int RailN = 7;

    private static readonly Rect Well = new(Cx - 1.45, 26.8, Cx + 1.45, 33.2); // the belly well under the middle lane of row 3
    private static readonly Rect BellyHatch = new(Cx + 2.4, 11.4, Cx + 3.65, 12.65); // the airlock's boarding shaft
    private static readonly Rect LadderHatch = new(Cx - 3.4, 12.3, Cx - 2.15, 13.55); // the suit room up to the aft hall
    private const double TrackY = -1.26; // the well doors' tracks' underside

    /// <summary>The side doors beside the well (row 3), one a side: the opening's ends along the ship, its head, and the hinge socket.</summary>
    /// <summary>The drives (ship-works.md 15; Karl, 2026-11-17: two large thrusters that look substantial from behind, the drive room
    /// slim and long enough to meet the stern so every piece can be reached): two Drive Nozzle L (10 MN each) on shrouds through the
    /// transom, a mount. Behind each, bolted to the transom bulkhead's inner face on its axis, its own engine: a Drive Core L, a Fuel
    /// Pump L and a Coolant Exchanger L, one cylinder on saddles on the drive room's deck, running forward. A walkway down the middle
    /// between them and one outboard of each; the Engineer's Panel on the port wall forward of them.</summary>
    private static readonly double[] DriveX = [-2.0, 2.0];
    // (Karl, 2026-11-17: the drives on the ship's middle line: the engines stand on a raised floor, a stair flight up from the door)
    private const double Fe = Ft0 + 1.5, CeilE = 4.75, ZPlat = 48.0; // the engine floor's top, the drive room's ceiling, the engine floor's front
    private static double DriveY => Fe + ShipWorksParts.AxisOver(2); // (the engines' saddles stand on the engine floor)
    private const string StairPart = "Steel Stair Flight 1.5 m";
    private static readonly double[] EngineFrameZ = [52.6, 49.55, 48.35]; // (over each engine's core coil, pump volute and exchanger: clear of gauges, panels and the pump's motor)
    private const double Ew = 4.2; // the drive room's inner half width
    private const double StIn = 53.4; // the transom bulkhead's inner face, where the engines' front flanges bolt
    private const double ShroudR = 1.85; // a nozzle's shroud on the transom
    private const double PanelZ = 47.0;

    /// <summary>Attitude jets (Karl, 2026-11-16: it flips and burns on its drives, with small jets every way for small moves): a
    /// cluster at the bow and one at the stern, each up, down, out to both sides and along the ship, Heavy RCS Thrusters on pads.</summary>
    private const string Rcs = "Heavy RCS Thruster";
    private const double BowRcsZ = 3.0, SternRcsZ = 52.0, RcsPadW = 0.6;

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

    /// <summary>The x of facet k's surface (side s) at height y and station z (between its two profile points).</summary>
    private static double FacetX(int k, int s, double z, double y)
    {
        var q = At(z);
        var (a, b) = (q[k - 1], q[k]);
        return Cx + s * (a.H + (b.H - a.H) * (y - a.Y) / (b.Y - a.Y));
    }

    private const double SideZ0 = 27.4, SideZ1 = 32.6, SideTop = 3.05, SideSockX = 6.875, SideSockY = 3.075, SideSockZ = 30.125;

    private static double[] P(double x, double y, double z) => [x - H, y - H, z - H];

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the spine's edge)

    private readonly record struct Sec(double Z, (double H, double Y)[] Pts);

    private static readonly (double, double)[] Mid = [(3.9, -1.2), (6.5, 0.2), (7.0, 3.2), (6.7, 7.3), (4.4, 8.5)];

    private const double Zt = -3.5, Zc = 0.5; // the bow's tip, the brow

    // A cab forward of a wider, taller hold: the crew section's hull is narrow and low, then a raked shoulder steps out to the hold's
    // (stations 11.25 to 13.75), which runs straight to the drive room, where the stern pinches in to the drives.
    private static readonly Sec[] Hull =
    [
        new(Zt, [(0.4, 0.9), (0.9, 1.4), (1.2, 2.5), (1.0, 3.5), (0.5, 3.9)]),
        new(Zc, [(2.2, -0.5), (3.6, 0.3), (4.1, 2.9), (3.6, 5.6), (2.0, 6.6)]),
        new(ZFront, [(3.0, -0.9), (4.7, 0.2), (5.0, 3.0), (4.6, 6.6), (2.8, 7.5)]),
        new(11.25, [(3.1, -1.0), (4.8, 0.2), (5.1, 3.0), (4.8, 6.9), (3.0, 7.7)]),
        new(13.75, Mid), new(ZHoldEnd, Mid),
        new(ZEngEnd, [(3.4, -1.0), (5.6, 0.2), (6.0, 3.2), (5.2, 6.4), (3.0, 7.3)]),
        new(Stern, [(3.6, -0.8), (4.8, 0.2), (5.0, 3.1), (4.4, 5.6), (2.4, 6.4)]), // (full at the bottom: the drive room runs to the transom, the shrouds fill it)
    ];

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
            throw new InvalidOperationException($"hauler_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
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
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.09, 0.05, Bare, "painted steel");
                    break;
                case 4:
                    Slab(p, k, s, za, zb, u0, (u0 + u1) / 2 - 0.01, v0, v1, 0.02, 0.15, 0.08, Armour);
                    Slab(p, k, s, za, zb, (u0 + u1) / 2 + 0.01, u1, v0 + 0.1, v1, 0.02, 0.1, 0.06, Armour2);
                    break;
                default:
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.03, Frame, "dirty steel");
                    FStrip(p, k, s, za, zb, u0, u0 + Math.Min(0.25, (u1 - u0) * 0.2), v0 + 0.1, v0 + 0.22, 0.031, Bare, "painted steel");
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


    // ---- the hauler's own helpers

    private static readonly double[] RibZ = [14.0, 20.4, 26.8, 33.2, 39.6, 46.0, ZEngEnd]; // the ring frames standing proud of the skin

    /// <summary>A ring frame's band across facet k (side s) at z: a bevelled slab standing out of the skin, odd facets' ends 6 mm wider.</summary>
    private static void Rib(Part p, int k, int s, double z, string color = Frame)
    {
        var e = k % 2 == 1 ? FacetStagger : 0;
        Slab(p, k, s, z - 0.25 - e, z + 0.25 + e, 0, 1, 0, 1, 0.03, 0.34, 0.07, color, "dirty steel");
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

    /// <summary>A facet band's panel ribbed like a container's wall: vertical bars every 0.5 m between the ring frames, in one tone.</summary>
    private static void Corrugate(Part p, int k, int s, double za, double zb, string tone)
    {
        double a = za + 0.32, b = zb - 0.32, len = zb - za;
        var n = (int)Math.Floor((b - a) / 0.5);
        var pitch = (b - a) / n;
        Slab(p, k, s, za, zb, (a - za) / len, (b - za) / len, 0.05, 0.95, 0.02, 0.05, 0.02, tone, "worn paint");
        for (var i = 0; i < n; i++)
        {
            double u0 = (a + i * pitch + 0.08 - za) / len, u1 = (a + (i + 1) * pitch - 0.08 - za) / len;
            Slab(p, k, s, za, zb, u0, u1, 0.08, 0.92, 0.05, 0.12, 0.04, tone, "worn paint");
        }
    }

    /// <summary>Whether a rib stands at z (so a band's plates keep clear of it).</summary>
    private static bool RibAt(double z) => RibZ.Any(r => Math.Abs(r - z) < 1e-6);

    /// <summary>A band of facet k on side s from za to zb: its skin plate and the armour panels on it, kept off the ribs at either end.</summary>
    private static void Band(Part p, int k, int s, double za, double zb, string color, int salt, bool plates = true)
    {
        Facet(p, k, s, za, zb, color, "painted plating");
        if (plates && zb - za > 1.5)
            Plates(p, k, s, za + (RibAt(za) ? 0.35 : 0.05), zb - (RibAt(zb) ? 0.35 : 0.05), salt);
    }

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

    /// <summary>The volume a part's rooms take (with their walls): what SplitDeck calls inside; outside it is hull.</summary>
    private static void Room(Part p, Rect r, double y0, double y1) => (Rooms.TryGetValue(p, out var rs) ? rs : Rooms[p] = []).Add((r, y0, y1));

    /// <summary>Draws with draw() and gives every box it adds to the part's last wall run (a girder, a bracket, a frame on its face).</summary>
    private static void OnWall(Part p, Action draw)
    {
        var n = p.Boxes.Count;
        draw();
        var w = Walls[p].Count - 1;
        for (var i = n; i < p.Boxes.Count; i++)
            Stuck[p.Boxes[i]] = w;
    }

    private static void Marks(Part p, double x, double z)
    {
        // A container slot: four twistlock pads at its corners, a stencilled hazard tick beside each.
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double cx = x + sx * (BoxW / 2 - 0.08), cz = z + sz * (BoxL / 2 - 0.1);
                Detail(p, cx - 0.09, Ft0, cz - 0.09, cx + 0.09, Ft0 + 0.05, cz + 0.09, Frame, "dirty steel");
                Detail(p, cx - sx * 0.12 - 0.04, Ft0, cz - sz * 0.5 - 0.25, cx - sx * 0.12 + 0.04, Ft0 + 0.012, cz - sz * 0.5 + 0.25, Hazard, "hazard paint");
            }
    }

    // ---- the keel: the flat underside, the void under the floors, the boarding shaft, the well and its doors' tracks

    private static Part Keel()
    {
        var p = Big("Dray Keel", "The Dray's keel: the flat underside under the crew section, the hold and the drive room, with the boarding shaft, the cargo well and the tracks its belly doors slide on.", color: Armour2);
        double[] zs = [ZFront, 11.25, 13.75, ZHold, .. Rows[1..], ZEngEnd];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            var y = At(za)[0].Y;
            var hb = At(za)[0].H;
            if (za <= Well.Z0 && zb >= Well.Z1)
            {
                Plate(p, [P(Cx - hb, y, za), P(Well.X0, y, za), P(Well.X0, y, zb), P(Cx - hb, y, zb)], Armour2, "plated steel");
                Plate(p, [P(Well.X1, y, za), P(Cx + hb, y, za), P(Cx + hb, y, zb), P(Well.X1, y, zb)], Armour2, "plated steel");
            }
            else if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                // (between stations the flat tapers: draw it as the facet, then cut the shaft's hole out of a plate laid over it)
                var a = BellyHatch;
                var y1 = At(zb)[0].Y;
                var hb1 = At(zb)[0].H;
                double Ya(double z) => y + (y1 - y) * (z - za) / (zb - za);
                double Ha(double z) => hb + (hb1 - hb) * (z - za) / (zb - za);
                Plate(p, [P(Cx - hb, y, za), P(Cx + hb, y, za), P(Cx + Ha(a.Z0), Ya(a.Z0), a.Z0), P(Cx - Ha(a.Z0), Ya(a.Z0), a.Z0)], Armour2, "plated steel");
                Plate(p, [P(Cx - Ha(a.Z1), Ya(a.Z1), a.Z1), P(Cx + Ha(a.Z1), Ya(a.Z1), a.Z1), P(Cx + hb1, y1, zb), P(Cx - hb1, y1, zb)], Armour2, "plated steel");
                Plate(p, [P(Cx - Ha(a.Z0), Ya(a.Z0), a.Z0), P(a.X0, Ya(a.Z0), a.Z0), P(a.X0, Ya(a.Z1), a.Z1), P(Cx - Ha(a.Z1), Ya(a.Z1), a.Z1)], Armour2, "plated steel");
                Plate(p, [P(a.X1, Ya(a.Z0), a.Z0), P(Cx + Ha(a.Z0), Ya(a.Z0), a.Z0), P(Cx + Ha(a.Z1), Ya(a.Z1), a.Z1), P(a.X1, Ya(a.Z1), a.Z1)], Armour2, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Armour2, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 1, s, za, zb, Skin2, 1 + i);
                Chine(p, 1, s, za, zb, 0.24, 0.11, Armour2);
            }
            // Keel strakes: two dark skids along the flat (not across the well's tracks).
            if (!(za <= Well.Z0 && zb >= Well.Z1) && za >= ZHold)
                foreach (var s in new[] { -1, 1 })
                    Detail(p, Cx + s * 2.6 - 0.1, y - 0.14, za + 0.3, Cx + s * 2.6 + 0.1, y - 0.023, zb - 0.3, Frame, "dirty steel");
        }
        foreach (var z in RibZ.Where(z => z >= ZFront && z < ZEngEnd))
        {
            if (z != Well.Z0 && z != Well.Z1)
                Rib(p, 0, 1, z);
            foreach (var s in new[] { -1, 1 })
                Rib(p, 1, s, z);
        }
        // The void under the floors, round the shaft and the well.
        var shaft = new Rect(BellyHatch.X0 - 0.05, BellyHatch.Z0 - 0.05, BellyHatch.X1 + 0.05, BellyHatch.Z1 + 0.05);
        var well = new Rect(Well.X0 - 0.05, Well.Z0 - 0.01, Well.X1 + 0.05, Well.Z1 + 0.01);
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            foreach (var (ya, yt) in new[] { (yb, -0.8), (-0.8, -0.35), (-0.35, 0.0) })
            {
                var xo = Math.Floor((new[] { SkinHalf(za, ya), SkinHalf(za, yt), SkinHalf(zb, ya), SkinHalf(zb, yt) }.Min() - 0.06) * 100) / 100;
                if (xo < 0.3 || yt <= ya)
                    continue;
                foreach (var r in Minus(Minus([new Rect(Cx - xo, i == 0 ? za : za + 0.012, Cx + xo, i == zs.Length - 2 ? zb : zb - 0.012)], shaft), well))
                    Hidden(p, r.X0, ya, r.Z0, r.X1, yt, r.Z1);
            }
        }
        // The shaft's lining and the well's: plates down through the keel, hazard round the mouths.
        var hy = At(12.0)[0].Y;
        foreach (var (x0, z0, x1, z1, e) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0, 0.0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1, 0.0),
            (shaft.X0, shaft.Z0 + 0.025, shaft.X0, shaft.Z1 - 0.025, 0.005), (shaft.X1, shaft.Z0 + 0.025, shaft.X1, shaft.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + 0.01 + e, z0), P(x1, hy + 0.01 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z0 - 0.12, shaft.X1 + 0.12, hy - 0.004, shaft.Z0, Hazard, "hazard paint");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z1, shaft.X1 + 0.12, hy - 0.004, shaft.Z1 + 0.12, Hazard, "hazard paint");
        var wy = Mid[0].Item2;
        foreach (var (x0, z0, x1, z1, e) in new[] { (well.X0, Well.Z0 + 0.03, well.X1, Well.Z0 + 0.03, 0.0), (well.X0, Well.Z1 - 0.03, well.X1, Well.Z1 - 0.03, 0.0),
            (well.X0, Well.Z0 + 0.06, well.X0, Well.Z1 - 0.06, 0.005), (well.X1, Well.Z0 + 0.06, well.X1, Well.Z1 - 0.06, 0.005) })
            Plate(p, [P(x0, wy + 0.03 + e, z0), P(x1, wy + 0.03 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        // The belly doors' tracks: a beam across each end of the well under the keel, a hazard edge on its inner face.
        foreach (var (z0, z1) in new[] { (Well.Z0 - 0.3, Well.Z0), (Well.Z1, Well.Z1 + 0.3) })
        {
            Solid(p, Cx - 3.2, TrackY, z0, Cx + 3.2, wy + 0.01, z1, Frame, "dirty steel");
            var zi = z0 < Well.Z0 ? z1 : z0;
            Detail(p, Cx - 3.1, TrackY + 0.012, zi - 0.004, Cx + 3.1, wy - 0.002, zi + 0.004, Hazard, "hazard paint");
        }
        Sock(p, "wellDoorL", Cx - 0.73, TrackY, Well.Z0 - 0.15, "-y");
        Sock(p, "wellDoorR", Cx + 0.73, TrackY, Well.Z0 - 0.15, "-y");
        return p.Stats(30000, 70000, 45, 30.0);
    }

    // ---- the crew section, lower deck: the mess, the stores, the suit room with the ladder up, the airlock over the shaft, the way into the hold

    private static Part CrewLower()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Dray Crew Lower Deck", "The Dray's crew section, lower deck: the mess, the stores, the suit room with the ladder up, the airlock over the boarding shaft and the door aft into the hold.");
        double y0 = Ft0, y1 = Ceil0;
        var plan = new Rect(Cx - Lw - 0.25, ZFront - 0.25, Cx + Lw + 0.25, ZHold - 0.125);
        Floor(p, [plan], 0, Ft0, Deck, "painted deck plate", BellyHatch);
        Room(p, plan, 0, Ceil0);
        Detail(p, Cx - 0.9, Ft0, ZFront + 0.1, Cx + 1.1, Ft0 + 0.012, ZHold - 0.25, Grate, "painted grating"); // (the passage's grating)
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZFront - 0.125, y0, y1);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZFront, ZHold - 0.125, Cx + s * (Lw + 0.125), y0, y1);
        WallRun(p, false, ZFront, ZHold - 0.125, Cx - 1.0, y0, y1, Door(8.0), Door(11.6));
        WallRun(p, false, ZFront, ZHold - 0.125, Cx + 1.25, y0, y1, Door(6.0), Door(11.0));
        WallRun(p, true, Cx - Lw, Cx - 1.125, 10.5, y0, y1);
        WallRun(p, true, Cx + 1.375, Cx + Lw, 9.875, y0, y1);
        // The airlock's cycle light over its inner door, red and green.
        Detail(p, Cx + 1.37, Ft0 + DoorH + 0.15, 11.5, Cx + 1.385, Ft0 + DoorH + 0.3, 11.8, Red, glow: true);
        Detail(p, Cx + 1.115, Ft0 + DoorH + 0.15, 11.5, Cx + 1.13, Ft0 + DoorH + 0.3, 11.8, "#40e070", glow: true);
        HatchRail(p, BellyHatch, Ft0);
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (ZFront, 11.25), (11.25, 13.75), (13.75, ZHold) })
            {
                Band(p, 2, s, za, zb, Skin, 2);
                Chine(p, 2, s, za, zb, 0.3, 0.14, Armour2);
            }
        foreach (var (za, zb) in new[] { (ZFront, 11.25), (11.25, ZHold - 0.125) })
        {
            VoidT(p, za, zb, 0.0, 0.7, Lw + 0.25, za == ZFront ? 0.0 : 0.004, 0.004);
            VoidT(p, za, zb, 0.7, Ceil0, Lw + 0.25, za == ZFront ? 0.0 : 0.004, 0.004);
        }
        DoorSock(p, "doorMess", false, 8.0, Cx - 1.0, Ft0);
        DoorSock(p, "doorSuit", false, 11.6, Cx - 1.0, Ft0);
        DoorSock(p, "doorStore", false, 6.0, Cx + 1.25, Ft0);
        DoorSock(p, "doorAirlock", false, 11.0, Cx + 1.25, Ft0);
        Sock(p, "ladder", LadderHatch.X0 + 0.625, Ft0, LadderHatch.Z1 - 0.095, "+y");
        for (var i = 0; i < 3; i++) // (the Robot Shell Docks on the suit room's forward wall, and the Shell Control Chairs down the store's
            // starboard wall, backs to it: Karl, 2026-11-17)
            Sock(p, $"shellDock{i}", Cx - Lw + 0.17 + CrewFittingsParts.DockHalf + i * (2 * CrewFittingsParts.DockHalf + 0.01), Ft0 + 0.125, 10.625, "+z");
        for (var i = 0; i < 3; i++)
            Sock(p, $"linkChair{i}", Cx + Lw - 0.5, Ft0, 6.4 + i * 1.0, "+y");
        Sock(p, "boarding", BellyHatch.X0 + 0.625, Ft0 - 0.125, BellyHatch.Z1, "-z");
        foreach (var (what, z) in new[] { ("lower deck front", ZFront - 0.25), ("lower deck back", ZHold) })
            foreach (var y in new[] { 0.0, Ft1 })
                MustFit(what, z, Lw + 0.25, y);
        return p.Stats(14000, 32000, 30, 20.0);
    }

    // ---- the crew section, upper deck: the sealed bridge, two cabins off a hall, the aft hall with the ladder, the head

    private static Part CrewUpper()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Dray Crew Upper Deck", "The Dray's crew section, upper deck: the sealed bridge, two cabins, the head and the aft hall, with the ladder down.");
        double y0 = Ft1, y1 = Ceil1;
        Rect[] plan =
        [
            new(Cx - UwF - 0.25, ZFront - 0.25, Cx + UwF + 0.25, ZCab - 0.125),
            new(Cx - Lw - 0.25, ZCab - 0.125, Cx + Lw + 0.25, ZAftHall - 0.125),
            new(Cx - UwA - 0.25, ZAftHall - 0.125, Cx + UwA + 0.25, ZHold - 0.125),
        ];
        Floor(p, plan, Ceil0, Ft1, Deck, "painted deck plate", LadderHatch);
        Floor(p, plan, Ceil1, Roof, Ceil, "painted plating");
        foreach (var r in plan)
            Room(p, r, Ceil0, Roof);
        Ceils[p] = Ceil1;
        Detail(p, Cx - UwF + 0.05, Ft1, ZFront + 0.05, Cx + UwF - 0.05, Ft1 + 0.012, ZCab - 0.3, "#30353b", "rubber matting"); // (the bridge's mat)
        HatchRail(p, LadderHatch, Ft1);
        // The bridge: sealed (no glass), a wide view screen over the front wall, a camera screen each side.
        WallRun(p, true, Cx - UwF - 0.25, Cx + UwF + 0.25, ZFront - 0.125, y0, y1);
        Detail(p, Cx - 2.1, Ft1 + 1.2, ZFront - 0.016, Cx + 2.1, Ft1 + 2.6, ZFront + 0.016, "#202428", "plastic");
        Detail(p, Cx - 2.0, Ft1 + 1.28, ZFront + 0.008, Cx + 2.0, Ft1 + 2.52, ZFront + 0.028, "#2a5878", glow: true);
        foreach (var s in new[] { -1, 1 })
        {
            WallRun(p, false, ZFront, ZCab - 0.125, Cx + s * (UwF + 0.125), y0, y1);
            WallRun(p, false, ZCab + 0.125, ZAftHall - 0.125, Cx + s * (Lw + 0.125), y0, y1);
            WallRun(p, false, ZAftHall + 0.125, ZHold - 0.125, Cx + s * (UwA + 0.125), y0, y1);
        }
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCab, y0, y1, Door(Cx - 0.75));
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZCab + 0.125, ZAftHall - 0.125, Cx + s * 0.875, y0, y1, Door(9.6));
        WallRun(p, true, Cx - UwA - 0.25, Cx - 1.0, ZAftHall, y0, y1);
        WallRun(p, true, Cx + 1.0, Cx + UwA + 0.25, ZAftHall, y0, y1);
        WallRun(p, false, ZAftHall + 0.125, ZHold - 0.125, Cx + 1.125, y0, y1, Door(12.0));
        foreach (var s in new[] { -1, 1 })
            foreach (var (za, zb) in new[] { (ZFront, 11.25), (11.25, 13.75), (13.75, ZHold) })
            {
                Band(p, 3, s, za, zb, Skin, 3);
                Chine(p, 3, s, za, zb, 0.2, 0.1, Armour);
            }
        foreach (var (za, zb, xin) in new[] { (ZFront, ZCab - 0.125, UwF + 0.25), (ZCab - 0.125, ZAftHall - 0.125, Lw + 0.25), (ZAftHall - 0.125, ZHold - 0.125, UwA + 0.25) })
            foreach (var (ya, yb) in new[] { (Ceil0, 5.0), (5.0, Roof) })
                VoidT(p, za, zb, ya, yb, xin, za == ZFront ? 0.0 : 0.004, 0.004);
        DoorSock(p, "doorBridge", true, Cx - 0.75, ZCab, Ft1);
        DoorSock(p, "doorCabinA", false, 9.6, Cx - 0.875, Ft1);
        DoorSock(p, "doorCabinB", false, 9.6, Cx + 0.875, Ft1);
        DoorSock(p, "doorHead", false, 12.0, Cx + 1.125, Ft1);
        foreach (var (what, z, hx) in new[] { ("bridge", ZFront - 0.25, UwF + 0.25), ("cabins", ZCab, Lw + 0.25), ("aft hall", ZAftHall - 0.125, UwA + 0.25) })
            foreach (var y in new[] { Ceil0, Roof })
                MustFit(what, z, hx, y);
        return p.Stats(14000, 32000, 30, 20.0);
    }

    // ---- the hold: rows i0 to i1 of container slots, the walls with the crane's girders on them, the hull band beside it

    private static Part Hold(int i0, int i1, string name, string desc)
    {
        WallCol = HoldWall;
        DadoCol = HoldDado;
        var p = Big(name, desc, color: HoldDeck);
        double z0 = Rows[i0], z1 = Rows[i1];
        var plan = new Rect(Cx - Hw - 0.25, i0 == 0 ? z0 - 0.125 : z0, Cx + Hw + 0.25, i1 == Rows.Length - 1 ? z1 + 0.125 : z1);
        Floor(p, [plan], 0, Ft0, HoldDeck, "painted deck plate", Well);
        Floor(p, [plan], HCeil, HRoof, Ceil, "painted plating");
        Room(p, plan, 0, HRoof);
        Ceils[p] = HCeil;
        if (i0 == 0)
        {
            // The forward bulkhead, with the door from the crew's lower deck.
            WallRun(p, true, Cx - Hw - 0.25, Cx + Hw + 0.25, ZHold, Ft0, HCeil, Door(Cx - 0.625));
        }
        if (i1 == Rows.Length - 1)
            WallRun(p, true, Cx - Hw - 0.25, Cx + Hw + 0.25, ZHoldEnd, Ft0, HCeil, Door(Cx - 0.75));
        double railZ1 = RailZ0 + 4 * RailN;
        for (var r = i0; r < i1; r++)
        {
            double za = Rows[r], zb = Rows[r + 1];
            foreach (var s in new[] { -1, 1 })
            {
                var sideDoor = za == Well.Z0;
                if (sideDoor)
                    WallRun(p, false, za, zb, Cx + s * (Hw + 0.125), Ft0, HCeil, new Gap(SideZ0, SideZ1 - SideZ0, 0, SideTop - Ft0));
                else
                    WallRun(p, false, Math.Max(za, ZHold + 0.125), Math.Min(zb, ZHoldEnd - 0.125), Cx + s * (Hw + 0.125), Ft0, HCeil);
                OnWall(p, () =>
                {
                    // The crane's girder under its rail, on cantilever brackets off the wall; a frame up the wall at mid-row.
                    double ga = Math.Max(za, RailZ0), gb = Math.Min(zb, railZ1);
                    if (gb > ga)
                    {
                        Solid(p, Cx + s * (RailX - 0.5), RailY - 1.0, ga, Cx + s * (RailX + 0.5), RailY - 0.5, gb, Frame, "painted steel");
                        Detail(p, Cx + s * (RailX - 0.5) - s * 0.004, RailY - 0.95, ga + 0.004, Cx + s * (RailX - 0.5) + s * 0.012, RailY - 0.55, gb - 0.004, Hazard, "hazard paint");
                    }
                    // (beside the side door the brackets stand at its jambs)
                    var bzs = sideDoor ? [SideZ0 - 0.2, SideZ1 + 0.2] : Enumerable.Range(0, 12).Select(i => RailZ0 + 0.3 + 3.9 * i).Where(z => z < railZ1).ToArray();
                    foreach (var bz in bzs)
                        if (bz >= za && bz < zb)
                        {
                            Solid(p, Cx + s * (RailX - 0.5), RailY - 1.4, bz - 0.15, Cx + s * Hw, RailY - 1.0, bz + 0.15, Frame, "painted steel");
                            double xw = Cx + s * (Hw - 0.004), xg = Cx + s * (RailX + 0.6);
                            Prism(p, [P(xw, RailY - 2.4, bz - 0.1), P(xw, RailY - 2.4, bz + 0.1), P(xw - s * 0.01, RailY - 1.41, bz + 0.1), P(xw - s * 0.01, RailY - 1.41, bz - 0.1)],
                                [P(xg, RailY - 1.41, bz - 0.1), P(xg, RailY - 1.41, bz + 0.1)], Frame, "painted steel");
                        }
                    var fz = (za + zb) / 2;
                    if (!sideDoor)
                        Detail(p, Cx + s * (Hw - 0.12), Ft0 + 0.75, fz - 0.12, Cx + s * (Hw - 0.004), HCeil - 0.004, fz + 0.12, Frame, "dirty steel");
                    // The girder's tops take the rails (medium sockets: a rail is a medium part).
                    for (var k = 0; k < RailN; k++)
                    {
                        var rz = RailZ0 + 4 * k + 0.5;
                        if (rz >= za && rz < zb)
                            p.Sock($"rail{(s < 0 ? "L" : "R")}{k}", P(Cx + s * RailX, RailY - 0.5, rz), "+y", Medium);
                    }
                });
            }
            // The slots: three lanes, the middle one of row 3 the well.
            foreach (var lane in Lanes)
                if (!(lane == 0 && za == Well.Z0))
                    Marks(p, Cx + lane, (za + zb) / 2);
            // The hull beside the row: the beam's chine rail, raked shoulder blocks along the upper chine, the panels, the ribs.
            foreach (var s in new[] { -1, 1 })
            {
                var tone = r % 2 == 0 ? Skin : Armour;
                Facet(p, 3, s, za, zb, Skin2, "painted plating");
                Corrugate(p, 3, s, za, zb, tone);
                if (za == Well.Z0)
                    SideDoorway(p, s, za, zb);
                else
                {
                    Facet(p, 2, s, za, zb, Skin2, "painted plating");
                    Corrugate(p, 2, s, za, zb, tone);
                    Chine(p, 2, s, za, zb, 0.3, 0.14, Armour2);
                }
                Shoulder(p, 3, s, za + 0.45, zb - 0.45, 0.55, 0.3, 1.1, Armour);
            }
            var ta = r == 0 ? 0.0 : 0.004;
            foreach (var (ya, yb) in new[] { (0.0, 0.7), (0.7, 3.5), (3.5, 6.0), (6.0, HRoof) })
                if (za == Well.Z0 && ya < SideTop)
                {
                    // (the side doorway leaves the void below its head open)
                    VoidT(p, za, SideZ0 - 0.045, ya, yb, Hw + 0.25, ta, 0);
                    VoidT(p, SideZ1 + 0.045, zb, ya, yb, Hw + 0.25, 0, 0.004);
                }
                else
                    VoidT(p, r == 0 ? za - 0.125 : za, r == Rows.Length - 2 ? zb + 0.125 : zb, ya, yb, Hw + 0.25, ta, 0.004);
        }
        Ribs(p, z0, z1, 2, 3);
        if (za0(i0, i1))
        {
            // The well's coaming: a hazard band round its mouth.
            Detail(p, Well.X0 - 0.2, Ft0, Well.Z0 + 0.01, Well.X0, Ft0 + 0.012, Well.Z1 - 0.01, Hazard, "hazard paint");
            Detail(p, Well.X1, Ft0, Well.Z0 + 0.01, Well.X1 + 0.2, Ft0 + 0.012, Well.Z1 - 0.01, Hazard, "hazard paint");
        }
        if (za0(i0, i1))
        {
            Sock(p, "sideDoorL", Cx - SideSockX, SideSockY, SideSockZ, "-y");
            Sock(p, "sideDoorR", Cx + SideSockX, SideSockY, SideSockZ, "-y");
        }
        if (i0 == 0)
        {
            DoorSock(p, "doorHold", true, Cx - 0.625, ZHold, Ft0);
        }
        if (i1 == Rows.Length - 1)
            DoorSock(p, "doorDrive", true, Cx - 0.75, ZHoldEnd, Ft0);
        foreach (var z in new[] { plan.Z0, plan.Z1 })
            foreach (var y in new[] { 0.0, HRoof })
                MustFit(name, z, Hw + 0.25, y);
        return p.Stats(9000 * (i1 - i0), 30000, 30, 20.0);
        static bool za0(int a, int b) => Rows[a] <= Well.Z0 && Rows[b] >= Well.Z1;
    }

    // ---- the drive room aft of the hold (empty for now: the drives and their plant come later)

    private static Part DriveRoom()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Dray Drive Room", "The Dray's drive room, aft of the hold to the transom: the two drives' engines stand on a raised floor and run forward off the transom bulkhead, with a walkway each side of them; a stair flight up from the door, and the Engineer's Panel beside it.");
        var plan = new Rect(Cx - Ew - 0.25, ZHoldEnd + 0.125, Cx + Ew + 0.25, StIn);
        Floor(p, [plan], 0, Ft0, Deck, "painted deck plate");
        Floor(p, [new Rect(Cx - Ew + 0.004, ZPlat, Cx + Ew - 0.004, StIn)], Ft0, Fe, Deck, "painted deck plate"); // (the engine floor)
        Floor(p, [plan], CeilE, CeilE + 0.25, Ceil, "painted plating");
        Room(p, plan, 0, CeilE + 0.25);
        Ceils[p] = CeilE;
        Detail(p, Cx - Ew + 0.1, Ft0, ZHoldEnd + 0.6, Cx + Ew - 0.1, Ft0 + 0.012, ZPlat - 0.1, Grate, "painted grating");
        Detail(p, Cx - Ew + 0.1, Fe, ZPlat + 0.3, Cx + Ew - 0.1, Fe + 0.012, StIn - 0.1, Grate, "painted grating");
        Detail(p, Cx - Ew + 0.1, Fe, ZPlat + 0.003, Cx + Ew - 0.1, Fe + 0.008, ZPlat + 0.15, Hazard, "hazard paint"); // (the engine floor's edge)
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZHoldEnd + 0.125, StIn, Cx + s * (Ew + 0.125), Ft0, CeilE);
        foreach (var s in new[] { -1, 1 })
        {
            Band(p, 2, s, ZHoldEnd, ZEngEnd, Skin, 9);
            Chine(p, 2, s, ZHoldEnd, ZEngEnd, 0.3, 0.14, Armour2);
        }
        Ribs(p, ZHoldEnd, ZEngEnd, 2);
        foreach (var (ya, yb) in new[] { (0.0, 0.7), (0.7, Ft1), (Ft1, CeilE + 0.25) })
            VoidT(p, plan.Z0, ZEngEnd, ya, yb, Ew + 0.25, 0.004, 0.004); // (aft of the station the stern's void goes on)
        // The engines' supports (Karl, 2026-11-17): a ring frame over each engine's core, pump and exchanger, a beam across under the
        // ceiling on posts against the walls, with a pair of hangers down onto each engine's back, clear of its pipes.
        foreach (var z in EngineFrameZ)
        {
            Detail(p, Cx - Ew + 0.004, CeilE - 0.25, z - 0.1, Cx + Ew - 0.004, CeilE - 0.006, z + 0.1, Frame, "dirty steel");
            foreach (var sx in new[] { -1, 1 })
                Detail(p, Cx + sx * (Ew - 0.004), Fe + 0.004, z - 0.1, Cx + sx * (Ew - 0.14), CeilE - 0.252, z + 0.1, Frame, "dirty steel");
            foreach (var dx in DriveX)
                foreach (var hx in new[] { -0.6, 0.6 })
                    Detail(p, Cx + dx + hx - 0.04, DriveY + 0.86, z - 0.05, Cx + dx + hx + 0.04, CeilE - 0.248, z + 0.05, Bare, "dirty steel");
        }
        Sock(p, "stair", Cx - 0.375, Ft0, ZPlat - 1.375, "+y"); // (a 1.5 m flight's foot: it rises aft to the engine floor's edge)
        Sock(p, "panel", Cx - Ew, Ft0 + 0.6, PanelZ, "+x");
        foreach (var y in new[] { 0.0, Ft1, CeilE + 0.25 })
            MustFit("drive room", plan.Z1, Ew + 0.25, y);
        return p.Stats(11000, 24000, 30, 15.0);
    }

    // ---- the bow: a blunt, armoured cab nose over the crew section, sealed (no glass)

    private static Part Bow()
    {
        var p = Big("Dray Bow", "The Dray's bow: a blunt, faceted cab nose, armoured over the sealed bridge, with a ground scanner under its chin.", color: Armour);
        foreach (var (za, zb) in new[] { (Zt, Zc), (Zc, ZFront) })
        {
            Facet(p, 0, 1, za, zb, Armour2, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Facet(p, 1, s, za, zb, Skin2, "painted plating");
                Facet(p, 2, s, za, zb, Skin, "painted plating");
                Facet(p, 3, s, za, zb, za >= Zc ? Armour : Skin, za >= Zc ? "plated steel" : "painted plating");
                Facet(p, 4, s, za, zb, Armour2, "plated steel");
                Chine(p, 2, s, za, zb, 0.26, 0.12, Armour2);
                Chine(p, 3, s, za, zb, 0.2, 0.1, Armour);
            }
            Facet(p, 5, 1, za, zb, Armour, "plated steel");
        }
        foreach (var s in new[] { -1, 1 })
        {
            // Stepped armour on the cheeks and the brow, a freight-blue blaze down the nose, a hazard chevron at the tip.
            Slab(p, 2, s, Zc, ZFront, 0.05, 0.95, 0.1, 0.9, 0.02, 0.14, 0.08, Armour);
            Slab(p, 2, s, Zc, ZFront, 0.25, 0.9, 0.25, 0.75, 0.14, 0.22, 0.05, Armour2);
            Slab(p, 2, s, Zt, Zc, 0.25, 0.95, 0.12, 0.88, 0.02, 0.11, 0.06, Armour);
            Slab(p, 1, s, Zc, ZFront, 0.08, 0.92, 0.1, 0.9, 0.02, 0.1, 0.05, Armour2);
            Slab(p, 3, s, Zt, Zc, 0.3, 0.95, 0.1, 0.9, 0.02, 0.1, 0.05, Armour);
            Slab(p, 4, s, Zc, ZFront, 0.06, 0.94, 0.1, 0.9, 0.02, 0.12, 0.06, Armour);
            FStrip(p, 2, s, Zt, Zc, 0.05, 0.2, 0.3, 0.7, 0.03, Hazard, "hazard paint");
        }
        Slab(p, 5, 1, Zc, ZFront, 0.06, 0.94, 0.12, 0.88, 0.02, 0.14, 0.06, Armour2);
        Slab(p, 5, 1, Zt, Zc, 0.1, 0.92, 0.12, 0.88, 0.02, 0.12, 0.06, Armour2);
        var tip = At(Zt);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + tip[k].H, tip[k].Y, Zt)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - tip[k].H, tip[k].Y, Zt)))], Frame, "dirty steel");
        // The chin: a ground scanner's faceted pod under the nose.
        var cy = At(2.5)[0].Y;
        Prism(p, [P(Cx - 1.0, cy + 0.02, 1.2), P(Cx + 1.0, cy + 0.02, 1.2), P(Cx + 1.0, cy + 0.02, 4.4), P(Cx - 1.0, cy + 0.02, 4.4)],
            [P(Cx - 0.7, cy - 0.35, 1.8), P(Cx + 0.7, cy - 0.35, 1.8), P(Cx + 0.7, cy - 0.35, 4.1), P(Cx - 0.7, cy - 0.35, 4.1)], Frame, "dirty steel");
        // The bow's attitude jets: up and down on the brow and the flat, out on the cheeks, two retro jets in the tip.
        var bq = At(BowRcsZ);
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            RcsPad(p, "rcsUp" + n, Cx + s * 1.4, bq[4].Y, BowRcsZ, "+y", 0.2);
            RcsPad(p, "rcsDown" + n, Cx + s * 1.9, bq[0].Y, BowRcsZ, "-y", 0.05);
            RcsPad(p, "rcsOut" + n, FacetX(2, s, BowRcsZ, 1.6), 1.6, BowRcsZ, s < 0 ? "-x" : "+x", 0.3);
            RcsPad(p, "rcsAlong" + n, Cx + s * 0.55, 2.0, Zt, "-z", 0.05);
        }
        foreach (var (za, zb) in new[] { (Zt + 2.0, Zc), (Zc, ZFront - 0.25) })
            foreach (var (ya, yb) in new[] { (At(za)[0].Y + 0.05, 1.5), (1.5, 3.2), (3.2, 5.0), (5.0, 6.4) })
                VoidT(p, za, zb, ya, yb, 0, 0.004, zb == ZFront - 0.25 ? 0.0 : 0.004);
        return p.Stats(9000, 24000, 40, 15.0);
    }

    // ---- the spine: the roof over the crew section, the hold and the drive room; a dorsal strongback over the hold, a mast, radiators

    private static Part Spine()
    {
        var p = Big("Dray Spine", "The Dray's spine: the roof, the strongback girders along the hold, the sensor mast and the drive's radiators.", color: Armour);
        double[] zs = [ZFront, 11.25, 13.75, ZHold, .. Rows[1..], ZEngEnd];
        for (var i = 0; i < zs.Length - 1; i++)
        {
            double za = zs[i], zb = zs[i + 1];
            Facet(p, 5, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 4, s, za, zb, Skin2, 6 + i);
                Chine(p, 4, s, za, zb, 0.2, 0.1, Armour2);
            }
        }
        Ribs(p, ZFront, ZEngEnd, 4, 5);
        // The strongback: two box girders the hold's length either side of the middle, cross-tied at every rib; hatch covers between.
        double Top(double z) => At(z)[4].Y;
        var ty = Top(30.0);
        foreach (var s in new[] { -1, 1 })
        {
            double x = Cx + s * 2.0;
            double[][] Sec(double z, double w, double h) => [P(x - w, ty - 0.012, z), P(x - w * 0.7, ty + h, z), P(x + w * 0.7, ty + h, z), P(x + w, ty - 0.012, z)];
            Prism(p, Sec(ZHold + 0.6, 0.4, 0.2), Sec(ZHold + 1.8, 0.4, 0.62), Armour2, "plated steel");
            Prism(p, Sec(ZHold + 1.81, 0.4, 0.62), Sec(ZHoldEnd - 1.81, 0.4, 0.62), Armour2, "plated steel");
            Prism(p, Sec(ZHoldEnd - 1.8, 0.4, 0.62), Sec(ZHoldEnd - 0.6, 0.4, 0.2), Armour2, "plated steel");
            Detail(p, x - 0.41, ty + 0.25, ZHold + 2.0, x - 0.39, ty + 0.33, ZHoldEnd - 2.0, Hazard, "hazard paint");
        }
        foreach (var z in Rows[1..^1])
            Detail(p, Cx - 1.6, ty + 0.25, z - 0.12, Cx + 1.6, ty + 0.5, z + 0.12, Frame, "dirty steel");
        for (var r = 0; r < Rows.Length - 1; r++)
            Detail(p, Cx - 1.45, ty + 0.25, Rows[r] + 0.6, Cx + 1.45, ty + 0.3, Rows[r + 1] - 0.6, r % 2 == 0 ? Bare : Armour, "painted steel");
        // The mast forward of the hold: a raked blade with a dish, red tips.
        double mz = 11.5, my = Top(mz);
        Prism(p, [P(Cx - 0.12, my - 0.01, mz - 1.2), P(Cx + 0.12, my - 0.01, mz - 1.2), P(Cx + 0.12, my - 0.01, mz + 1.0), P(Cx - 0.12, my - 0.01, mz + 1.0)],
            [P(Cx - 0.06, my + 2.2, mz + 0.2), P(Cx + 0.06, my + 2.2, mz + 0.2), P(Cx + 0.06, my + 2.2, mz + 0.9), P(Cx - 0.06, my + 2.2, mz + 0.9)], Armour2, "plated steel");
        Prism(p, Ngon(Cx + 0.75, my + 0.5, mz + 1.9, 0.08, 8), Ngon(Cx + 0.75, my + 0.75, mz + 1.9, 0.55, 8), "#c8ccc8", "painted steel");
        Detail(p, Cx + 0.7, my - 0.02, mz + 1.85, Cx + 0.8, my + 0.5, mz + 1.95, Frame, "painted steel");
        Detail(p, Cx - 0.07, my + 2.2, mz + 0.45, Cx + 0.07, my + 2.34, mz + 0.65, Red, glow: true);
        // Radiators over the drive room: six fins raked aft.
        for (var i = 0; i < 6; i++)
        {
            double x = Cx - 2.5 + i;
            var y = Top(48.0) - 0.012;
            Prism(p, [P(x - 0.04, y, 46.5), P(x + 0.04, y, 46.5), P(x + 0.04, y, 49.0), P(x - 0.04, y, 49.0)],
                [P(x - 0.03, y + 0.9, 47.2), P(x + 0.03, y + 0.9, 47.2), P(x + 0.03, y + 0.9, 49.2), P(x - 0.03, y + 0.9, 49.2)], i % 2 == 0 ? Frame : Bare, "soot");
        }
        // The roof's void.
        foreach (var (za, zb, ya) in new[] { (ZFront, 11.25, Roof), (11.25, 13.75, Roof), (13.75, ZHold - 0.125, Roof) })
            VoidT(p, za, zb, ya, Math.Min(Top(za), Top(zb)) - 0.02, 0, 0.004, 0.004);
        for (var r = 0; r < Rows.Length - 1; r++)
            VoidT(p, r == 0 ? Rows[r] - 0.125 : Rows[r], r == Rows.Length - 2 ? Rows[r + 1] + 0.125 : Rows[r + 1], HRoof, Math.Min(Top(Rows[r]), Top(Rows[r + 1] + 0.125)) - 0.02, 0, r == 0 ? 0.0 : 0.004, 0.004);
        VoidT(p, ZHoldEnd + 0.125, ZEngEnd, 6.65, Top(ZEngEnd) - 0.02, 0, 0.004, 0.004);
        return p.Stats(12000, 30000, 40, 15.0);
    }

    // ---- the stern: the hull's last band, the upper band over the drive room, the transom with two drive seats on shrouds and the bulkhead their engines bolt to

    private static Part SternPart()
    {
        var p = Big("Dray Stern", "The Dray's stern: the hull closing over the drive room, and the transom its two big nozzles bolt to, on shrouds. Behind each, on the transom bulkhead's inner face, its engine runs forward into the drive room.", color: Armour);
        Works.Mount(p); // (the nozzles' mount: each one's engine bolts to the bulkhead's inner face on its axis)
        foreach (var s in new[] { -1, 1 })
        {
            Band(p, 3, s, ZHoldEnd, ZEngEnd, Skin2, 7);
            Shoulder(p, 3, s, ZHoldEnd + 0.45, ZEngEnd - 0.45, 0.55, 0.3, 1.1, Armour);
        }
        Ribs(p, ZHoldEnd, ZEngEnd, 3);
        Facet(p, 0, 1, ZEngEnd, Stern, Armour2, "plated steel");
        Facet(p, 5, 1, ZEngEnd, Stern, Armour, "plated steel");
        foreach (var s in new[] { -1, 1 })
        {
            for (var k = 1; k <= 4; k++)
            {
                Facet(p, k, s, ZEngEnd, Stern, k % 2 == 0 ? Skin : Skin2, "painted plating");
                Slab(p, k, s, ZEngEnd, Stern, 0.12, 0.94, 0.08, 0.92, 0.03, k == 2 ? 0.2 : 0.14, 0.06, k == 2 ? Armour : Armour2, "plated steel");
            }
            Chine(p, 2, s, ZEngEnd, Stern, 0.32, 0.16, Armour2);
            Chine(p, 4, s, ZEngEnd, Stern, 0.2, 0.1, Armour2);
        }
        Ribs(p, ZEngEnd, Stern, 0, 1, 2, 3, 4, 5);
        // The transom: a sooty plate across the stern, a shroud round each drive (a broad collar, its bolts, a hazard band), and
        // inside, the bulkhead across the drive room's end that the engines bolt to.
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
        Detail(p, Cx - 0.12, DriveY - 1.0, Stern + 0.02, Cx + 0.12, DriveY + 1.0, Stern + 0.05, Hazard, "hazard paint");
        Solid(p, Cx - Ew - 0.25, 0, StIn, Cx + Ew + 0.25, CeilE + 0.25, Stern - 0.03, Bare, "dirty steel"); // (the bulkhead: its inner face is the engines')
        foreach (var dx in DriveX)
            Detail(p, Cx + dx - 1.3, DriveY + 1.4, StIn - 0.02, Cx + dx + 1.3, DriveY + 1.5, StIn - 0.003, Hazard, "hazard paint"); // (a keep-clear band over each engine)
        foreach (var (ya, yb) in new[] { (-0.7, 0.0), (CeilE + 0.25, 5.6), (5.6, 6.3) })
            VoidT(p, ZEngEnd, Stern, ya, yb, 0, 0.004, 0.03);
        foreach (var (ya, yb) in new[] { (0.0, 0.7), (0.7, Ft1), (Ft1, CeilE + 0.25) })
            VoidT(p, ZEngEnd, Stern, ya, yb, Ew + 0.25, 0.004, 0.03);
        VoidT(p, ZHoldEnd + 0.125, ZEngEnd, CeilE + 0.25, 6.65, 0, 0.004, 0.004); // (up to the spine's void over the drive room)
        // The stern's attitude jets: up and down on the spine and the flat, out on the upper sides, two forward jets on the transom.
        var sq = At(SternRcsZ);
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            RcsPad(p, "rcsUp" + n, Cx + s * 1.5, sq[4].Y, SternRcsZ, "+y", 0.05);
            RcsPad(p, "rcsDown" + n, Cx + s * 1.5, sq[0].Y, SternRcsZ, "-y", 0.05);
            RcsPad(p, "rcsOut" + n, FacetX(3, s, SternRcsZ, 4.2), 4.2, SternRcsZ, s < 0 ? "-x" : "+x", 0.25);
            RcsPad(p, "rcsAlong" + n, Cx + s * 3.6, 5.3, Stern, "+z", 0.05); // (above the shrouds' shoulders)
        }
        for (var i = 0; i < DriveX.Length; i++)
        {
            var x = Cx + DriveX[i];
            Solid(p, x - 0.7, DriveY - 0.7, Stern - 0.3, x + 0.7, DriveY + 0.7, Stern + 0.43, Frame, "dirty steel"); // (the nozzle's seat, inside its flange)
            Sock(p, $"drive{i}", x, DriveY, Stern + 0.43, "+z");
            Sock(p, $"engine{i}", x, DriveY, StIn, "-z");
        }
        return p.Stats(12000, 32000, 45, 20.0);
    }

    // ---- the well's belly doors, and the ladder between the crew decks

    /// <summary>A leaf of the well's belly door (s -1 port, +1 starboard): a ribbed plate under the keel that slides outboard along the
    /// tracks across the well's ends to open it for the crane.</summary>
    /// <summary>The hull beside the well on side s with the side doorway cut in facet 2 (the beam's lower panel): the skin and its
    /// ribbed panels either side, jambs and a hazard frame round it, the chine rail stopped short of it, a sill across the void, the
    /// linings of its ends and the hinge beam over it that the door hangs from.</summary>
    private static void SideDoorway(Part p, int s, double za, double zb)
    {
        var len = zb - za;
        Facet(p, 2, s, za, SideZ0, Skin2, "painted plating");
        Facet(p, 2, s, SideZ1, zb, Skin2, "painted plating");
        Chine(p, 2, s, za, SideZ0 - 0.06, 0.3, 0.14, Armour2);
        Chine(p, 2, s, SideZ1 + 0.06, zb, 0.3, 0.14, Armour2);
        // The jambs: a dark frame each side, a hazard stripe on its face.
        foreach (var (u0, u1) in new[] { ((SideZ0 - 0.26 - za) / len, (SideZ0 - za) / len), ((SideZ1 - za) / len, (SideZ1 + 0.26 - za) / len) })
        {
            Slab(p, 2, s, za, zb, u0, u1, 0.01, 0.99, 0.02, 0.11, 0.03, Frame, "dirty steel");
            var um = (u0 + u1) / 2;
            FStrip(p, 2, s, za, zb, um - 0.06 / len, um + 0.06 / len, 0.06, 0.94, 0.116, Hazard, "hazard paint");
        }
        // Inside: the sill over the void, its hazard edge, the ends' linings and the hinge beam.
        double SkinX(double y) => SkinHalf(SideZ0, y);
        Solid(p, Cx + s * 6.025, 0.0, SideZ0 - 0.04, Cx + s * 6.44, Ft0, SideZ1 + 0.04, HoldDeck, "painted deck plate");
        Detail(p, Cx + s * 6.2, Ft0, SideZ0 + 0.02, Cx + s * 6.43, Ft0 + 0.012, SideZ1 - 0.02, Hazard, "hazard paint");
        foreach (var z in new[] { SideZ0 - 0.02, SideZ1 + 0.02 })
            Plate(p, [Sh([Cx + s * 6.025, Ft0 + 0.004, z]), Sh([Cx + s * (SkinX(Ft0) - 0.03), Ft0 + 0.004, z]), Sh([Cx + s * (SkinX(SideTop) - 0.03), SideTop, z]), Sh([Cx + s * 6.025, SideTop, z])], HoldWall, "dirty steel");
        Solid(p, Cx + s * 6.025, SideSockY, SideZ0 - 0.045, Cx + s * SideSockX, 3.45, SideZ1 + 0.045, Frame, "painted steel");
    }

    /// <summary>A side door of the Dray's hold, beside the well: a ribbed leaf in the hull's lower side panel, hinged along its head at the
    /// beam, that swings up and out to an awning over the doorway.</summary>
    private static Part SideDoor(int s)
    {
        var name = s < 0 ? "Dray Side Door L" : "Dray Side Door R";
        var p = new Part(name, Small, Section, "A side door of the Dray's hold beside the cargo well, " + (s < 0 ? "to port" : "to starboard") + ": a ribbed leaf hinged along its head that swings up and out, an awning over the doorway.", role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        double[] S = [Cx + s * SideSockX, SideSockY, SideSockZ]; // (the hinge socket, the part's origin)
        double za = SideZ0 + 0.015, zb = SideZ1 - 0.015, len = zb - za;
        double v0 = (Ft0 + 0.02 - Mid[1].Item2) / (Mid[2].Item2 - Mid[1].Item2), v1 = (SideTop - Mid[1].Item2) / (Mid[2].Item2 - Mid[1].Item2);
        var n = FacetNormal(2, s, za, zb);
        double[] L(double u, double v, double d)
        {
            var q = OnFacet(2, s, za, zb, u, v);
            return [Py.Round(q[0] + n[0] * d - S[0], 4), Py.Round(q[1] + n[1] * d - S[1], 4), Py.Round(q[2] + n[2] * d - S[2], 4)];
        }
        double[][] Q(double u0, double u1, double va, double vb, double d) => [L(u0, va, d), L(u1, va, d), L(u1, vb, d), L(u0, vb, d)];
        // The leaf: 6 cm of plate, its outer face 1 cm proud of the skin; bars every half metre like a container's, a frame round them.
        Plate(p, Q(0, 1, v0, v1, -0.02), Armour, "worn paint", thick: 0.06);
        var bars = (int)Math.Floor((len - 0.3) / 0.5);
        var pitch = (len - 0.3) / bars;
        for (var i = 0; i < bars; i++)
        {
            double u0 = (0.15 + i * pitch + 0.07) / len, u1 = (0.15 + (i + 1) * pitch - 0.07) / len;
            Prism(p, Q(u0, u1, v0 + 0.07, v1 - 0.07, 0.012), Q(u0 + 0.02 / len, u1 - 0.02 / len, v0 + 0.08, v1 - 0.08, 0.07), Armour2, "worn paint");
        }
        foreach (var (u0, u1) in new[] { (0.0, 0.1 / len), (1 - 0.1 / len, 1.0) })
            Prism(p, Q(u0, u1, v0, v1, 0.012), Q(u0 + 0.01 / len, u1 - 0.01 / len, v0 + 0.01, v1 - 0.01, 0.06), Frame, "dirty steel");
        Prism(p, Q(0.1 / len + 0.002, 1 - 0.1 / len - 0.002, v0, v0 + 0.045, 0.012), Q(0.1 / len + 0.004, 1 - 0.1 / len - 0.004, v0 + 0.005, v0 + 0.04, 0.05), Hazard, "hazard paint");
        Prism(p, Q(0.1 / len + 0.002, 1 - 0.1 / len - 0.002, v1 - 0.03, v1, 0.012), Q(0.1 / len + 0.004, 1 - 0.1 / len - 0.004, v1 - 0.025, v1 - 0.005, 0.045), Frame, "dirty steel");
        // Its body for the collider: a slab through the leaf's middle (the leaf leans 9 degrees).
        var mid = L(0.5, (v0 + v1) / 2, -0.02);
        p.Add(Box([mid[0] - 0.04, L(0, v0, 0)[1], L(0, 0, 0)[2]], [mid[0] + 0.04, L(0, v1, 0)[1], L(1, 0, 0)[2]], hidden: true));
        p.Add(Box([-0.1, L(0, v1, 0)[1], L(0, 0, 0)[2]], [0.1, 0, L(1, 0, 0)[2]], hidden: true)); // (the hinge's knuckle under the beam, which takes the socket)
        p.Sock("hang", [0, 0, 0], "+y");
        var pivot = L(0.5, v1, 0.01);
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+z"), ("pivot", new List<object?> { pivot[0], pivot[1], 0.0 }), ("angle", 90.0 * s), ("seconds", 5.0), ("hang", "hang"))));
        return p.Stats(1400, 7000, 35, 8.0);
    }

    private static Part WellDoor(int s)
    {
        var name = s < 0 ? "Dray Well Door L" : "Dray Well Door R";
        var p = new Part(name, Small, Section, "A leaf of the Dray's belly door: a ribbed steel plate under the keel that slides " + (s < 0 ? "to port" : "to starboard") + " to open the cargo well.", role: "door", tool: "welder", material: "steel", color: Bare, group: Group);
        double hx = 0.72, z0 = -0.1, z1 = Well.Z1 - Well.Z0 + 0.4, t = 0.14;
        void B(double x0, double y0, double zz0, double x1, double y1, double zz1, string col, string fin, bool detail = false) =>
            p.Add(Box([x0, y0, zz0], [x1, y1, zz1], col, detail: detail, shape: Kw(("finish", fin))));
        B(-hx, -t - 0.005, z0, hx, -0.005, z1, Bare, "plated steel");
        foreach (var x in new[] { -0.45, 0.0, 0.45 })
            B(x - 0.05, -t - 0.08, z0 + 0.15, x + 0.05, -t - 0.001, z1 - 0.15, Frame, "dirty steel", true);
        for (var i = 0; i < 6; i++)
        {
            var z = z0 + 0.5 + i * (z1 - z0 - 1.0) / 5;
            B(-hx + 0.05, -t - 0.06, z - 0.05, hx - 0.05, -t - 0.002, z + 0.05, Frame, "dirty steel", true);
        }
        var m = -s * hx; // (the meeting edge, inboard)
        B(Math.Min(m, m + s * 0.25), -t - 0.02, z0 + 0.1, Math.Max(m, m + s * 0.25), -t - 0.004, z1 - 0.1, Hazard, "hazard paint", true);
        p.Sock("hang", [0, 0, 0], "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", s < 0 ? "-x" : "+x"), ("travel", 1.5), ("seconds", 4.0), ("hang", "hang"))));
        return p.Stats(1600, 8000, 35, 8.0);
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
    private static readonly double[] Bands = [ZFront, 11.25, ZHold, 20.4, 26.8, 33.2, 39.6, ZHoldEnd, ZEngEnd, Stern]; // (the stations the hull pieces are cut at)

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
        Part WallPiece(int i) => wallPieces[i] ??= Big($"Dray {shortName} Wall {i + 1}", $"A wall of the Dray's {what}, with its door frames and what is fixed to it.", color: core.Color == HoldDeck ? HoldWall : Wall);
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
                    hull[key] = h = Big($"Dray {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece beside the Dray's {what}, the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (ceilY is double y && c[1] > y - 0.35)
                return ceiling ??= Big($"Dray {shortName} Ceiling", $"The ceiling of the Dray's {what}.", color: Ceil);
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
        (double Mass, double Hp, int Armour) Kind(Part q) => q == ceiling ? (1200.0, 5000.0, 25) : q.Color == Wall || q.Color == HoldWall ? (700.0, 3500.0, 25) : (1800.0, 9000.0, 40);
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
                    bands[k] = piece = Big($"Dray {shortName} {k + 1}", $"A length of the Dray's {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
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
            throw new InvalidOperationException("Dray: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
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

    private const string Rail = "Crane Rail Medium 4 m", CraneBridge = "Medium Crane Bridge";

    private static PyDict Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "A container hauler: a long hold for fourteen 20-foot containers with an overhead crane down its length and a well in its belly to lift them through, a small crew section forward and a drive room aft. Two big torch drives push it, each with its own engine in the drive room behind it, and attitude jets at the bow and stern move it about. (No legs, lamps or fittings yet.)", defs, maxSteps: 400);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in Structure)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = placed;
        int keel = placed["Dray Keel"], lower = placed["Dray Crew Lower Deck"], upper = placed["Dray Crew Upper Deck"],
            fore = placed["Dray Hold Fore"], well = placed["Dray Hold Well"], aft = placed["Dray Hold Aft"];
        foreach (var d in new[] { "doorMess", "doorSuit", "doorStore", "doorAirlock" })
            Door(bp, lower, d);
        foreach (var d in new[] { "doorBridge", "doorCabinA", "doorCabinB", "doorHead" })
            Door(bp, upper, d);
        Door(bp, fore, "doorHold");
        Door(bp, aft, "doorDrive");
        Hang(bp, "Dray Ladder", lower, "ladder", "bottom");
        for (var i = 0; i < 3; i++)
        {
            Hang(bp, "Robot Shell", Hang(bp, "Robot Shell Dock", lower, $"shellDock{i}", "wall"), "front", "back");
            bp.HangTurned("Shell Control Chair", Holder(bp, lower, $"linkChair{i}"), $"linkChair{i}", "bottom", BuildingParts.RY90);
        }
        // The drives: a big nozzle on each shroud, and behind it on the bulkhead its own engine (core, pump, exchanger, one cylinder).
        var stern = placed["Dray Stern"];
        var drive = placed["Dray Drive Room"];
        for (var i = 0; i < DriveX.Length; i++)
        {
            Hang(bp, "Drive Nozzle L", stern, $"drive{i}", "mount");
            Hang(bp, "Coolant Exchanger L", Hang(bp, "Fuel Pump L", Hang(bp, "Drive Core L", stern, $"engine{i}", "front"), "back", "front"), "back", "front");
        }
        Hang(bp, "Engineer's Panel", drive, "panel", "back");
        Paint(bp, Hang(bp, StairPart, drive, "stair", "b0_0"), Bare, "dirty steel");
        // The attitude jets, at the bow and the stern.
        foreach (var piece in new[] { placed["Dray Bow"], stern })
            foreach (var d in new[] { "Up", "Down", "Out", "Along" })
                foreach (var n in new[] { "L", "R" })
                    Paint(bp, Hang(bp, Rcs, piece, $"rcs{d}{n}", "base"), Frame, "dirty steel");
        Paint(bp, Hang(bp, "Dray Well Door L", keel, "wellDoorL", "hang"), Bare, "plated steel");
        Paint(bp, Hang(bp, "Dray Well Door R", keel, "wellDoorR", "hang"), Bare, "plated steel");
        Paint(bp, Hang(bp, "Dray Side Door L", well, "sideDoorL", "hang"), Armour, "worn paint");
        Paint(bp, Hang(bp, "Dray Side Door R", well, "sideDoorR", "hang"), Armour, "worn paint");
        // The crane: two runs of rail on the girders, the bridge on the port run's outer side, spanning to the starboard run.
        var port = new int[RailN];
        foreach (var s in new[] { -1, 1 })
            for (var k = 0; k < RailN; k++)
            {
                var r = Paint(bp, bp.Put(Rail, P(Cx + s * RailX, RailY, RailZ0 + 4 * k + 0.5), 0), Frame, "painted steel");
                if (s < 0)
                    port[k] = r;
            }
        Paint(bp, bp.Hang(CraneBridge, port[1], "-x.0.0", "+x"), Hazard, "hazard paint");
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
        structure.AddRange(SplitBands(Keel(), "Keel", 30.0)); // (the well's band keeps the keel's name: its doors hang on it)
        structure.AddRange(SplitDeck(CrewLower(), "Crew Lower"));
        structure.AddRange(SplitDeck(CrewUpper(), "Crew Upper"));
        structure.AddRange(SplitDeck(Hold(0, 2, "Dray Hold Fore", "The Dray's hold, forward: rows 1 and 2 of the container slots, the bulkhead door from the crew section."), "Hold Fore"));
        structure.AddRange(SplitDeck(Hold(2, 3, "Dray Hold Well", "The Dray's hold, middle: row 3, with the cargo well through the floor between two container slots."), "Hold Well"));
        structure.AddRange(SplitDeck(Hold(3, 5, "Dray Hold Aft", "The Dray's hold, aft: rows 4 and 5 of the container slots and the door aft into the drive room."), "Hold Aft"));
        structure.AddRange(SplitDeck(DriveRoom(), "Drive Room"));
        structure.AddRange(SplitBands(Spine(), "Spine", 30.0));
        structure.AddRange([Bow(), SternPart()]);
        Connect(structure);
        Structure = PlaceOrder(structure, "Dray Keel");
        List<Part> parts =
        [
            .. structure, WellDoor(-1), WellDoor(1), SideDoor(-1), SideDoor(1),
            Ladder("Dray Ladder", Ft1 - Ft0, "A ladder between the Dray's crew decks: F climbs it.", "bottom", [0, 0, -0.095], "-y"),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"hauler_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/DrayContainerHauler.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
    }
}
