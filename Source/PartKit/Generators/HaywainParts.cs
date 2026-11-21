using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Haywain, a medium hauler for the farmers (Karl, 2026-11-21: "a new ship for the farmers faction ... 90s and 80s combine harvester
/// vibe, blocky with the ships having a cockpit as a room attached to the ship, slightly protruding ... not so much farm equipment in
/// space as it is the aesthetic, colour choices, textures, cladding"). About 45 m long: a glass cab room on the nose, narrower than the
/// body and standing out ahead of it with a walkway and a ladder round it, the crew's rooms behind it, then a deep container bay open
/// to the sky (six standard 2 m containers, two across and three along, inside a raised tank coaming) between two side passages, then
/// an engine box with louvred flanks, exhaust stacks and a drive pod low on each side.
///
/// Built as the Satchel courier and the Packet freighter are (their helpers copied): a hull lofted through stations first, the rooms a
/// box of their own inside it, then each big part drawn whole and dealt out into welded pieces (floors, wall runs, ceilings, hull
/// bands), so a hit takes a piece, not a deck. Ship metres: x across (the centreline at Cx), y up from the deck's underside, z along
/// the ship (forward is -z; 0 is the bay's middle); P() moves them by half a cell into a part's frame. The look is a combine
/// harvester's: barn-red ribbed cladding with sharp corners, a cream stripe band, a dark chassis, amber beacons and hazard ends,
/// louvres, stencils, tread-plate steps (docs/sandbox/constructs/haywain.md).
/// </summary>
public sealed class HaywainParts : IPartGenerator
{
    public string Name => "haywain_parts";

    private const string Out = "Constructs/Parts/Haywain";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Haywain hauler";
    public const string ShipName = "Haywain Hauler";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: a combine harvester's. Barn red over a darker red, a cream stripe band and cream trim, a dark chassis and keel, bare
    // galvanised steel, amber for hazard ends and beacons. Inside: cream walls over an olive dado, a dark rubber floor.
    private const string Skin = "#a8322a", Skin2 = "#8e2a23", Armour = "#e2d7b5", Armour2 = "#cfc29c", Bare = "#8a8d86", Frame = "#2e302d",
        Dark = "#222421", Hazard = "#e8a317", Orange = "#e8a317", Red = "#c0392b", Glow = "#ffe0a0", Glass = "#8fb5bb", Link = "#ffa020",
        Paper = "#ece6d2", Green = "#2f9a52",
        Wall = "#d6cfb8", WallLow = "#56654a", WallTrim = "#2e302d", Deck = "#3e403c", Grate = "#353733", Ceil = "#c4bfac",
        RackCol = "#3a3c38", Latch = "#c4362b", Chassis = "#2e302d";

    /// <summary>The walls WallRun draws with.</summary>
    private static string WallCol = Wall, DadoCol = WallLow;

    // ---- the plan

    private const double Cx = 0.0; // the centreline
    private const double Ft = 0.25; // the deck's top, all along the ship
    private const double CCeil = 2.75, CRoof = 3.0, Lw = 3.25; // the crew rooms: their ceiling, roof slab's top, rooms' inner half width
    private const double SCeil = 2.6, SRoof = 2.85, SLine = 0.06; // the passages beside the bay: ceiling, roof slab, the hull's lining
    private const double PassIn = 3.0, PassOut = 4.0; // a passage's inner face (the bay wall's outer face) and its lining's face
    private const double ECeil = 2.75, ERoof = 3.0; // engineering
    /// <summary>The stations: the cab room's front, its middle and its back (ZCk), the shoulder where the body steps out (ZCab), the
    /// crew block, the bay's ends and middles, engineering, the transom.</summary>
    private const double Zt = -23.6, ZS1 = -22.0, ZCk = -20.45, ZCab = -20.05, ZNeck = -16.3, ZCabEnd = -11.95, ZEng = 10.45, ZEngMid = 13.8, ZEngEnd = 17.3, Stern = 19.6;
    private const double ZCkFloor = -23.3; // the cab room's floor, forward
    private const double ZCkW = -19.925, ZCabW = -12.075, ZEngAW = 17.175; // the cross walls' middles: the cab room's back, the crew block's aft, engineering's aft
    private const double DoorH = 2.25;
    /// <summary>How far the crew block's fittings sit from the Satchel's (whose cab this copies).</summary>
    private const double D2 = -6.8;
    /// <summary>The crew rooms: port the bunk room then the head, starboard the galley then the airlock (the cross walls' middles).</summary>
    private const double ZPortW = -8.675 + D2, ZStbdW = -9.775 + D2;
    /// <summary>The cab room: its inner half width, and its glass's sill.</summary>
    private const double CkHx = 2.0, Sill = 0.95, CkCeil = 3.45, CkRoof = 3.55, CkGlass = 2.22;

    /// <summary>The bay: its inner half width, a row's half length (three rows of cells along it), its walls' top, its ends.</summary>
    private const double BayHx = 2.75, RowIn = 3.4, BayTop = 3.5;
    private const int Rows = 3;
    private const double BayIn = RowIn * Rows, BayZ0 = -BayIn - 0.25, BayZ1 = BayIn + 0.25;
    /// <summary>A row's middle along the bay.</summary>
    private static double RowZ(int r) => (r - (Rows - 1) / 2.0) * 2 * RowIn;
    /// <summary>The standard container (Karl, 2026-11-20: "Standard Container 2m", three 2x2x2 m parts in a row): across, up, along;
    /// where its middle stands off the centreline, and the bay floor's twist-lock pads under its corner castings.</summary>
    private const double CtW = 2.5, CtH = 2.5, CtL = 6.5, CtX = 1.4, LockH = 0.06, CastX = 1.165, CastZ = 3.165;
    public const string ContainerName = "Standard Container 2m";
    /// <summary>The tank coaming round the bay's mouth (a grain tank's raised sides): how high it stands over the roof, how far it leans out.</summary>
    private const double CoamH = 1.3, CoamOut = 0.55;

    /// <summary>Where the engine box steps down from the bay's height, just aft of the bay.</summary>
    private const double ZEngStep = 10.85;
    /// <summary>The hull's stations (the hull pieces are cut at them).</summary>
    private static readonly double[] Bands = [Zt, ZS1, ZCk, ZCab, ZNeck, ZCabEnd, BayZ0, -RowIn, RowIn, BayZ1, ZEngStep, ZEngMid, ZEngEnd, Stern];
    /// <summary>The ring frames standing proud of the skin: the shoulder behind the cab room, the crew block's end, the bay's ends and
    /// the frames between its rows, engineering's end.</summary>
    private static readonly double[] RibZ = [ZCabEnd, BayZ0, -RowIn, RowIn, ZEngEnd];

    // The boarding shaft in the airlock (starboard, aft in the crew block), the belly hatch shutting it and the ladder on the hatch.
    private static readonly Rect BellyHatch = new(Cx + 1.1, -8.6 + D2, Cx + 2.25, -7.35 + D2);
    private const string BellyHatchName = "Haywain Belly Hatch";
    private static double HatchX => BellyHatch.X0 + 0.575;
    private static double HatchBottom => At(BellyHatch.Z1)[0].Y - 0.012;
    private static double HatchTop => HatchBottom + 0.08;
    private static double HatchTravel => Math.Min(HatchBottom - Ground - 0.005, CCeil - 0.03 - Ft);
    private static double LadderZ => BellyHatch.Z1 - 0.03;
    private static double BoardRise => Ft - (HatchTop - HatchTravel);

    // ---- flying it: a drive pod low on each side of the engine box, lift jets and legs under the crew block, the bay and engineering,
    // attitude jets at both ends.

    /// <summary>The drive pods: middles off the centreline and up, the housing's flat distance, the nose, the tank's ends, the tail.</summary>
    private const double NacX = 6.6, NacY = 1.2, NacF = 1.4, NacNose = 7.4, NacTank0 = 8.8, NacTank1 = 15.4, NacTail = 20.8;
    private const double PylonX = 4.55, PylonZ = 12.4; // the pod's hardpoint on engineering's flank
    private const int KeelFuel = 12000, NacFuel = 8000; // litres
    private const double LiftForce = 1300000, JetScale = 1.1, Burn = 2.5;
    private static readonly (double X, double Z)[] LiftAt = [(-1.5, -12.8), (1.5, -12.8), (-1.5, 0.0), (1.5, 0.0), (-1.5, 14.5), (1.5, 14.5)];
    private static readonly (double X, double Z)[] LegAt = [(-2.4, -17.6), (2.4, -17.6), (-2.8, 15.6), (2.8, 15.6)];
    private const string LegPod = "S";
    private const double PadProud = 0.1; // a jet's mount under the flat
    private const double LegProud = 0.16; // a leg pod's mount
    private static double Ground => -0.8 - LegProud - LegPodParts.ReachOf(LegPod);
    private const string Rcs = "Heavy RCS Thruster";
    private const double BowRcsZ = -17.0, SternRcsZ = 18.6, RcsPadW = 0.6;

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge)

    /// <summary>The cab room: a box half the body's width standing taller than it, glass from the sill to the roof (facet 3 is upright), its floor well above the body's keel.</summary>
    private static readonly (double, double)[] CkSec = [(2.05, -0.12), (2.2, 0.05), (CkGlass, Sill - 0.05), (CkGlass, 3.65), (2.05, 3.85)];
    /// <summary>The body: a big box with sharp corners, upright sides from the beam to the upper chine, a shallow chamfer to the roof.</summary>
    private static readonly (double, double)[] BaySec = [(3.85, -0.8), (4.4, -0.3), (4.5, 0.9), (4.5, 3.35), (4.25, 3.6)];
    /// <summary>The engine box: as wide, its roof 0.3 m lower.</summary>
    private static readonly (double, double)[] EngSec = [(3.85, -0.8), (4.4, -0.3), (4.5, 0.9), (4.5, 3.05), (4.2, 3.3)];

    // The cab room's box, then a sharp shoulder out to the body's full section, which runs the length of the bay, steps down to the
    // engine box and tucks in a little to a flat transom.
    private static readonly Sec[] Hull =
    [
        new(Zt, CkSec), new(ZCk, CkSec),
        new(ZCab, BaySec), new(BayZ1, BaySec),
        new(ZEngStep, EngSec), new(ZEngEnd, EngSec),
        new(Stern, [(3.3, -0.5), (3.85, -0.1), (3.95, 0.9), (3.95, 2.75), (3.65, 3.0)]),
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
            throw new InvalidOperationException($"haywain_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
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

    /// <summary>The cladding on a facet band from za to zb: panels about 3 m long, each a combine's pressed-steel panel: ribbed, a flat
    /// access door with its hinges and latch, a louvred grille, a plain darker panel, or a stencilled one; the pattern is the same on both
    /// sides (k and the panel's index pick it).</summary>
    private static void Plates(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.07, double v1 = 0.93)
    {
        var len = zb - za;
        var n = Math.Max(1, (int)Math.Round(len / 3.2));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.03 / len * 4, u1 = (double)(i + 1) / n - 0.03 / len * 4;
            var pick = (k * 7 + i * 3 + salt * 5 + (int)za) % 6;
            var ul = (u1 - u0) * len; // (the panel's length in metres)
            switch (pick)
            {
                case 0:
                case 3:
                    // Ribbed: a flat panel with upright ribs pressed into it every 0.3 m.
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.06, 0.03, pick == 0 ? Skin : Skin2);
                    var nr = Math.Max(2, (int)(ul / 0.3));
                    for (var r = 1; r < nr; r++)
                    {
                        var u = u0 + (u1 - u0) * r / nr;
                        Slab(p, k, s, za, zb, u - 0.03 / len, u + 0.03 / len, v0 + 0.04, v1 - 0.04, 0.055, 0.095, 0.012, pick == 0 ? Skin2 : Skin);
                    }
                    break;
                case 1:
                    // An access door: a flat panel, two hinge knuckles at its forward edge, a latch handle aft, its outline in dark.
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.08, 0.03, Skin);
                    foreach (var hv in new[] { v0 + (v1 - v0) * 0.2, v1 - (v1 - v0) * 0.2 })
                        FStrip(p, k, s, za, zb, u0 + 0.02 / len, u0 + 0.16 / len, hv - 0.03, hv + 0.03, 0.084, Bare, "steel");
                    FStrip(p, k, s, za, zb, u1 - 0.3 / len, u1 - 0.1 / len, (v0 + v1) / 2 - 0.02, (v0 + v1) / 2 + 0.02, 0.084, Dark, "dirty steel");
                    break;
                case 2:
                    // A louvred grille: a dark frame with slats across it.
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.07, 0.03, Skin2);
                    Slab(p, k, s, za, zb, u0 + 0.15 / len, u1 - 0.15 / len, v0 + 0.12, v1 - 0.12, 0.065, 0.078, 0.005, Dark, "soot");
                    var nl = Math.Max(3, (int)((v1 - v0 - 0.24) * 9));
                    for (var r = 0; r < nl; r++)
                    {
                        var v = v0 + 0.14 + (v1 - v0 - 0.28) * (r + 0.5) / nl;
                        FStrip(p, k, s, za, zb, u0 + 0.17 / len, u1 - 0.17 / len, v - 0.012, v + 0.012, 0.09, Skin, "painted plating");
                    }
                    break;
                case 4:
                    // Plain, with a stencil: a cream plate and dark marks (a model number, NO STEP).
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.07, 0.03, Skin);
                    double c0 = u0 + (u1 - u0) * 0.15, c1 = c0 + Math.Min(0.25, (u1 - u0) * 0.35);
                    FStrip(p, k, s, za, zb, c0, c1, v1 - 0.22, v1 - 0.1, 0.074, Armour, "painted plating");
                    for (var m = 0; m < 4; m++)
                    {
                        var mu = c0 + (c1 - c0) * (0.1 + m * 0.22);
                        FStrip(p, k, s, za, zb, mu, mu + (c1 - c0) * 0.14, v1 - 0.19, v1 - 0.13, 0.078, Dark, "painted plating");
                    }
                    break;
                default:
                    // Bare galvanised steel with a seam down it and a hazard tab.
                    Slab(p, k, s, za, zb, u0, u1, v0, v1, 0.02, 0.06, 0.03, Bare, "painted steel");
                    FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.004, (u0 + u1) / 2 + 0.004, v0, v1, 0.064, Frame, "dirty steel");
                    FStrip(p, k, s, za, zb, u0 + 0.02 / len, u0 + Math.Min(0.25, (u1 - u0) * 0.2), v0 + 0.1, v0 + 0.22, 0.065, Hazard, "hazard paint");
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


    // ---- the Haywain's own helpers

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
    private static void Band(Part p, int k, int s, double za, double zb, string color, int salt, bool plates = true, double v0 = 0.07)
    {
        Facet(p, k, s, za, zb, color, "painted plating");
        if (plates && zb - za > 1.5)
            Plates(p, k, s, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), salt, v0);
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

    /// <summary>The stripe band on a band of the upper side (facet 3), along its foot: a wide cream stripe, a dark pinstripe, a thin
    /// cream one (a combine's decal band).</summary>
    private static void Livery(Part p, int s, double za, double zb)
    {
        double a = za + (RibAt(za) ? 0.26 : 0.02), b = zb - (RibAt(zb) ? 0.26 : 0.02), len = zb - za;
        if (b - a < 0.3)
            return;
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.03, 0.12, 0.024, Armour, "painted plating");
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.14, 0.16, 0.024, Dark, "painted plating");
        FStrip(p, 3, s, za, zb, (a - za) / len, (b - za) / len, 0.18, 0.205, 0.024, Armour, "painted plating");
    }

    // ---- the keel: the flat underside the whole length, the void under the floors (fuel), the boarding shaft, the jets' and legs' mounts

    private static Part Keel()
    {
        var p = Big("Haywain Keel", "The Haywain's keel: the dark chassis underside under the cab room, the crew block, the container bay and engineering, with the fuel tankage in the void under the decks, the boarding shaft, and the lift jets' and legs' mounts.", "fueltank", Chassis);
        var shaft = new Rect(BellyHatch.X0 - 0.05, BellyHatch.Z0 - 0.05, BellyHatch.X1 + 0.05, BellyHatch.Z1 + 0.05);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var y = At(za)[0].Y;
            var hb = At(za)[0].H;
            if (za <= BellyHatch.Z0 && zb >= BellyHatch.Z1)
            {
                // (the cab's band is level: the flat round the shaft's hole in four plates)
                var a = BellyHatch;
                Plate(p, [P(Cx - hb, y, za), P(Cx + hb, y, za), P(Cx + hb, y, a.Z0), P(Cx - hb, y, a.Z0)], Chassis, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z1), P(Cx + hb, y, a.Z1), P(Cx + hb, y, zb), P(Cx - hb, y, zb)], Chassis, "plated steel");
                Plate(p, [P(Cx - hb, y, a.Z0), P(a.X0, y, a.Z0), P(a.X0, y, a.Z1), P(Cx - hb, y, a.Z1)], Chassis, "plated steel");
                Plate(p, [P(a.X1, y, a.Z0), P(Cx + hb, y, a.Z0), P(Cx + hb, y, a.Z1), P(a.X1, y, a.Z1)], Chassis, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Chassis, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 1, s, za, zb, Frame, 1 + i, plates: false);
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
        var p = Big("Haywain Roof", "The Haywain's roof: the cab room's cream roof with its amber beacon, the crew block's roof with the comms mast, the raised tank coaming round the open container bay with its floodlights, and the engine box's exhaust stacks and air intake.", color: Skin);
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            var room = zb <= ZCk + 1e-6;
            var step = za >= ZCk - 1e-6 && zb <= ZCab + 1e-6;
            var bay = za >= BayZ0 - 1e-6 && zb <= BayZ1 + 1e-6;
            if (bay)
            {
                // (the bay is open to the sky: the roof here is a strip each side, from the shoulder in to the bay's wall)
                var h4 = At(za)[4].H;
                var vin = (h4 - PassIn - 0.004) / (2 * h4);
                RoofStrip(p, za, zb, 0, vin, Skin2);
                RoofStrip(p, za, zb, 1 - vin, 1, Skin2);
            }
            else
            {
                Facet(p, 5, 1, za, zb, room ? Armour : step ? Skin : Skin2, "painted plating");
                if (!room && !step && zb - za > 1.5)
                    Plates(p, 5, 1, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), 7 + i, 0.12, 0.88);
            }
            foreach (var s in new[] { -1, 1 })
            {
                if (room)
                    Facet(p, 4, s, za, zb, Armour, "painted plating");
                else
                    Band(p, 4, s, za, zb, Skin, 4 + i, plates: false);
                if (!step)
                    Chine(p, 4, s, za, zb, room ? 0.08 : 0.14, room ? 0.06 : 0.08, room ? Dark : Armour);
            }
        }
        Ribs(p, ZCab, ZEngEnd, 4, 5);
        double Top(double z) => At(z)[4].Y;
        // The cab room's roof: the amber beacon on a short post at its back, a roof hatch, the radio's whip.
        double by0 = Top(ZS1);
        Detail(p, Cx - 0.5, by0 + 0.004, ZS1 - 0.4, Cx + 0.5, by0 + 0.06, ZS1 + 0.4, Armour2, "painted plating");
        Detail(p, Cx - 0.12, by0, ZCk - 0.75, Cx + 0.12, by0 + 0.25, ZCk - 0.51, Dark, "painted steel");
        Detail(p, Cx - 0.13, by0 + 0.25, ZCk - 0.76, Cx + 0.13, by0 + 0.45, ZCk - 0.5, Hazard, glow: true);
        Detail(p, Cx - 0.15, by0 + 0.45, ZCk - 0.78, Cx + 0.15, by0 + 0.49, ZCk - 0.48, Dark, "painted steel");
        Detail(p, Cx + 1.1, by0, ZCk - 0.5, Cx + 1.3, by0 + 0.08, ZCk - 0.3, Frame, "dirty steel");
        Detail(p, Cx + 1.18, by0 + 0.08, ZCk - 0.42, Cx + 1.22, by0 + 1.4, ZCk - 0.38, Dark, "painted steel");
        // The comms mast over the crew block: a lattice tower, a dish on a yoke, whip antennas, a beacon.
        double mz = -14.6, my = Top(mz);
        Solid(p, Cx - 0.5, my - 0.12, mz - 0.5, Cx + 0.5, my + 0.15, mz + 0.5, Frame, "dirty steel");
        foreach (var (dx, dz) in new[] { (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
            Detail(p, Cx + dx - 0.035, my + 0.15, mz + dz - 0.035, Cx + dx + 0.035, my + 2.2, mz + dz + 0.035, Bare, "painted steel");
        foreach (var yy in new[] { 0.7, 1.3, 1.85, 2.15 })
            Detail(p, Cx - 0.33, my + yy, mz - 0.33, Cx + 0.33, my + yy + 0.05, mz + 0.33, Hazard, "painted steel");
        Prism(p, Ngon(Cx + 0.9, my + 1.5, mz, 0.1, 10), Ngon(Cx + 0.9, my + 1.85, mz, 0.7, 10), Paper, "painted steel");
        Detail(p, Cx + 0.33, my + 1.45, mz - 0.05, Cx + 0.9, my + 1.55, mz + 0.05, Frame, "painted steel");
        Detail(p, Cx + 0.86, my + 1.6, mz - 0.03, Cx + 0.94, my + 2.3, mz + 0.03, Frame, "painted steel");
        Detail(p, Cx - 0.06, my + 2.2, mz - 0.06, Cx + 0.06, my + 2.35, mz + 0.06, Red, glow: true);
        foreach (var (dx, dz, h) in new[] { (-2.3, -14.0, 1.5), (-2.6, -13.2, 1.1), (-2.1, -12.6, 1.8) })
        {
            Detail(p, Cx + dx - 0.12, Top(dz) - 0.01, dz - 0.12, Cx + dx + 0.12, Top(dz) + 0.124, dz + 0.12, Frame, "dirty steel");
            Detail(p, Cx + dx - 0.02, Top(dz) + 0.124, dz - 0.02, Cx + dx + 0.02, Top(dz) + h, dz + 0.02, Dark, "painted steel");
            Detail(p, Cx + dx - 0.04, Top(dz) + h, dz - 0.04, Cx + dx + 0.04, Top(dz) + h + 0.08, dz + 0.04, Link, glow: true);
        }
        // Navigation lights on the body's front shoulders (red to port, green to starboard), work lamps beside them facing ahead.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 3.9 - 0.1, Top(ZCab + 0.3) - 0.01, ZCab + 0.2, Cx + s * 3.9 + 0.1, Top(ZCab + 0.3) + 0.09, ZCab + 0.4, s < 0 ? Red : Green, glow: true);
            foreach (var x in new[] { 2.4, 3.0 })
            {
                Detail(p, Cx + s * x - 0.16, Top(ZCab + 0.3), ZCab + 0.25, Cx + s * x + 0.16, Top(ZCab + 0.3) + 0.3, ZCab + 0.45, Dark, "painted steel");
                Detail(p, Cx + s * x - 0.12, Top(ZCab + 0.3) + 0.04, ZCab + 0.24, Cx + s * x + 0.12, Top(ZCab + 0.3) + 0.26, ZCab + 0.25, Glow, glow: true);
            }
        }
        // The tank coaming round the bay's mouth: four plate walls leaning out from the bay's edges (a grain tank's raised sides),
        // mitred at the corners, stiffened outside, a cream rim along the top, floodlights at the corners for loading.
        double cy0 = BayTop + 0.07, cy1 = Top(0) + CoamH, ct = 0.08, cz0 = BayZ0, cz1 = BayZ1;
        double[] C(double x, double y, double z) => P(x, y, z);
        foreach (var s in new[] { -1, 1 })
        {
            double xi = Cx + s * PassIn, xo = Cx + s * (PassIn + CoamOut);
            Prism(p, [C(xi, cy0, cz0), C(xi, cy0, cz1), C(xi + s * ct, cy0, cz1 + ct), C(xi + s * ct, cy0, cz0 - ct)],
                [C(xo, cy1, cz0 - CoamOut), C(xo, cy1, cz1 + CoamOut), C(xo + s * ct, cy1, cz1 + CoamOut + ct), C(xo + s * ct, cy1, cz0 - CoamOut - ct)], Skin, "painted plating");
            double zi = s < 0 ? cz0 : cz1, zo = zi + s * CoamOut;
            Prism(p, [C(Cx - PassIn, cy0, zi), C(Cx + PassIn, cy0, zi), C(Cx + PassIn + ct, cy0, zi + s * ct), C(Cx - PassIn - ct, cy0, zi + s * ct)],
                [C(Cx - PassIn - CoamOut, cy1, zo), C(Cx + PassIn + CoamOut, cy1, zo), C(Cx + PassIn + CoamOut + ct, cy1, zo + s * ct), C(Cx - PassIn - CoamOut - ct, cy1, zo + s * ct)], Skin, "painted plating");
            // Stiffeners down the side walls' outer faces, a cream rim on the side walls' tops.
            for (var z = cz0 + 1.2; z < cz1 - 0.6; z += 1.7)
            {
                double xa = xi + s * (ct + 0.004), xb = xo + s * (ct + 0.004);
                Prism(p, [C(xa, cy0 + 0.1, z - 0.05), C(xa + s * 0.07, cy0 + 0.1, z - 0.05), C(xa + s * 0.07, cy0 + 0.1, z + 0.05), C(xa, cy0 + 0.1, z + 0.05)],
                    [C(xb - s * 0.03, cy1 - 0.12, z - 0.05), C(xb + s * 0.04, cy1 - 0.12, z - 0.05), C(xb + s * 0.04, cy1 - 0.12, z + 0.05), C(xb - s * 0.03, cy1 - 0.12, z + 0.05)], Skin2, "painted plating");
            }
            Detail(p, Math.Min(xo - s * 0.01, xo + s * (ct + 0.03)), cy1 + 0.004, cz0 - CoamOut + 0.1, Math.Max(xo - s * 0.01, xo + s * (ct + 0.03)), cy1 + 0.07, cz1 + CoamOut - 0.1, Armour, "painted plating");
            foreach (var z in new[] { cz0 - CoamOut + 0.3, cz1 + CoamOut - 0.3 })
            {
                Detail(p, xo + s * 0.04 - 0.14, cy1 + 0.07, z - 0.14, xo + s * 0.04 + 0.14, cy1 + 0.36, z + 0.14, Frame, "dirty steel");
                Detail(p, xo + s * 0.04 - 0.1, cy1 + 0.1, z - 0.145, xo + s * 0.04 + 0.1, cy1 + 0.3, z - 0.14, Glow, glow: true);
            }
            // Hazard chevrons on the end walls' tops: amber on the outer thirds.
            Detail(p, Cx - PassIn - CoamOut + 0.1, cy1 + 0.004, zo + s * 0.005, Cx - 1.2, cy1 + 0.07, zo + s * (ct + 0.03), Hazard, "hazard paint");
            Detail(p, Cx + 1.2, cy1 + 0.004, zo + s * 0.005, Cx + PassIn + CoamOut - 0.1, cy1 + 0.07, zo + s * (ct + 0.03), Hazard, "hazard paint");
        }
        // The engine box's roof: two exhaust stacks with rain caps, an air intake with a louvred face, a hand rail along the edge.
        var ey = Top(ZEngMid);
        foreach (var s in new[] { -1, 1 })
        {
            double sx = Cx + s * 2.6, sz = 15.6;
            Solid(p, sx - 0.35, ey - 0.1, sz - 0.35, sx + 0.35, ey + 0.2, sz + 0.35, Frame, "dirty steel");
            Prism(p, Ngon(sx, ey + 0.2, sz, 0.2, 12), Ngon(sx, ey + 2.1, sz, 0.18, 12), Bare, "soot");
            Prism(p, Ngon(sx, ey + 2.1, sz, 0.21, 12), Ngon(sx, ey + 2.16, sz, 0.21, 12), Dark, "soot");
            Detail(p, sx - 0.24, ey + 2.22, sz - 0.24, sx + 0.24, ey + 2.26, sz + 0.24, Dark, "soot"); // (the flap)
            Detail(p, sx - 0.23, ey + 0.9, sz - 0.23, sx + 0.23, ey + 1.0, sz + 0.23, Frame, "dirty steel"); // (a clamp band)
        }
        Solid(p, Cx - 1.0, ey - 0.1, 11.2, Cx + 1.0, ey + 0.65, 13.6, Skin, "painted plating"); // (inside one roof band: ZEngStep to ZEngMid)
        for (var k = 0; k < 6; k++)
            Detail(p, Cx - 0.85, ey + 0.08 + k * 0.09, 11.16, Cx + 0.85, ey + 0.12 + k * 0.09, 11.2, Dark, "soot");
        Detail(p, Cx - 1.004, ey + 0.65, 11.196, Cx + 1.004, ey + 0.7, 13.604, Armour, "painted plating");
        // The bow's up jets.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsUp" + (s < 0 ? "L" : "R"), Cx + s * 1.2, Top(BowRcsZ), BowRcsZ, "+y", 0.056);
        // The void over the ceilings (the cab room's, the crew block's, beside the bay from its walls out, engineering's).
        foreach (var (i, za, zb) in BandsIn(Zt, ZEngEnd))
        {
            if (za >= ZCk - 1e-6 && zb <= ZCab + 1e-6)
                continue;
            var bay = za >= BayZ0 - 1e-6 && zb <= BayZ1 + 1e-6;
            var floor = za >= ZEng - 1e-6 ? ERoof : za >= ZCabEnd - 1e-6 ? SRoof : zb <= ZCk + 1e-6 ? CkRoof : CRoof;
            VoidT(p, za, zb, floor, Math.Min(Top(za), Top(zb)) - 0.03, bay ? PassIn + 0.02 : 0, za == ZCab ? 0.26 : 0.004, 0.004); // (clear of the header over the cab room's back wall)
        }
        return p.Stats(9000, 30000, 35, 15.0);
    }

    // ---- the cab: the glass cab room on the nose (a combine's cab), then the crew block's two rows of rooms off a corridor; its hull bands

    private static Part Cab()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Haywain Cab", "The Haywain's cab: the glass cab room standing out on the nose with the flight couches, then the crew's rooms off a short corridor: the bunk room and the head to port, the galley and the airlock over the boarding shaft to starboard.");
        const double D = 9.5 + D2; // (the Packet's cockpit fittings, moved aft by this much)
        Rect ck = new(Cx - CkHx, ZCkFloor, Cx + CkHx, ZCkW - 0.125), cab = new(Cx - Lw - 0.25, ZCkW - 0.125, Cx + Lw + 0.25, ZCabW - 0.125);
        Floor(p, [ck, cab], 0, Ft, Deck, "rubber matting", BellyHatch);
        Floor(p, [new Rect(cab.X0, ZCkW + 0.125, cab.X1, ZCabW - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Floor(p, [new Rect(ck.X0, ZCkFloor, ck.X1, ZCkW - 0.125)], CkCeil, CkRoof, Ceil, "painted plating");
        Room(p, new Rect(ck.X0 - 0.05, ZCkFloor - 0.25, ck.X1 + 0.05, ZCkW), 0, CkRoof + 0.1);
        Room(p, cab, 0, CRoof);
        Ceils[p] = CCeil;
        // The cab room: a tread mat, the dash under the windscreen with its screens and switches, two couches and the flight computer.
        Detail(p, Cx - 1.4, Ft, -24.95 + D, Cx + 1.4, Ft + 0.012, ZCkW - 0.25, "#30332e", "tread plate");
        Solid(p, Cx - 1.2, Ft, ZCkFloor, Cx + 1.2, Sill, -25.55 + D, Dark, "plastic");
        foreach (var (x0, x1, col) in new[] { (-0.85, -0.3, "#60e090"), (-0.22, 0.22, "#ffb040"), (0.3, 0.85, "#60e090") })
            Detail(p, Cx + x0, Sill, -25.98 + D, Cx + x1, Sill + 0.012, -25.6 + D, col, glow: true);
        for (var i = 0; i < 7; i++)
            Detail(p, Cx - 0.6 + i * 0.2, 0.6, -25.553 + D, Cx - 0.52 + i * 0.2, 0.68, -25.54 + D, i % 3 == 0 ? Hazard : Paper, "plastic");
        // A ceiling of toggle switches over the windscreen, a sun visor, a fire bottle by the door.
        Detail(p, Cx - 1.0, CkCeil - 0.12, ZCkFloor + 0.05, Cx + 1.0, CkCeil - 0.004, ZCkFloor + 0.7, Dark, "plastic");
        for (var i = 0; i < 9; i++)
            Detail(p, Cx - 0.85 + i * 0.2, CkCeil - 0.16, ZCkFloor + 0.4, Cx - 0.8 + i * 0.2, CkCeil - 0.12, ZCkFloor + 0.45, i % 4 == 0 ? Red : Paper, "plastic");
        Detail(p, Cx + CkHx - 0.2, Ft + 0.3, ZCkW - 0.45, Cx + CkHx - 0.05, Ft + 0.8, ZCkW - 0.3, Red, "worn paint");
        // The glass's backing (what stops a body at the panes) and the void under the sill, both sides; the void ahead of the dash.
        foreach (var (za, zb) in new[] { (ZCkFloor, ZS1), (ZS1, ZCk) })
        {
            var hx = new[] { SkinHalf(za, Sill), SkinHalf(za, CkCeil), SkinHalf(zb, Sill), SkinHalf(zb, CkCeil) }.Min() - 0.1;
            foreach (var s in new[] { -1, 1 })
                Hidden(p, Cx + s * (hx - 0.08), Sill, za + 0.004, Cx + s * hx, CkCeil, zb - 0.004);
            VoidT(p, za, zb, 0.1, Sill, CkHx, 0.004, 0.004);
        }
        Hidden(p, Cx - CkHx, Ft, Zt + 0.004, Cx + CkHx, Sill, ZCkFloor - 0.004);
        // The cab room's back wall (the crew block's front), the crew block's sides, the corridor's walls with the rooms' doors, the
        // rooms' cross walls, the aft wall (the whole width: the cross passage behind it reaches the side passages).
        WallRun(p, true, Cx - Lw - 0.25, Cx + Lw + 0.25, ZCkW, Ft, CCeil, Door(Cx - 0.75));
        Solid(p, Cx - CkHx - 0.05, CCeil + 0.004, ZCkW - 0.12, Cx + CkHx + 0.05, CkCeil - 0.004, ZCkW + 0.12, Wall, "painted plating"); // (the cab room is taller: a header over the wall up to its ceiling)
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + s * (Lw + 0.125), Ft, CCeil);
        WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx - 0.875, Ft, CCeil, Door(-11.6 + D2), Door(-8.3 + D2));
        WallRun(p, false, ZCkW + 0.125, ZCabW - 0.125, Cx + 0.875, Ft, CCeil, Door(-12.4 + D2), Door(-7.1 + D2));
        WallRun(p, true, Cx - Lw, Cx - 1.0, ZPortW, Ft, CCeil);
        WallRun(p, true, Cx + 1.0, Cx + Lw, ZStbdW, Ft, CCeil);
        WallRun(p, true, Cx - PassOut - SLine, Cx + PassOut + SLine, ZCabW, Ft, CCeil, Door(Cx - 0.75));
        // The airlock: a rail round the boarding shaft, the cycle lights over its door.
        HatchRail(p, BellyHatch, Ft);
        Detail(p, Cx + 1.0, Ft + DoorH + 0.15, -6.5 + D2, Cx + 1.015, Ft + DoorH + 0.3, -6.2 + D2, Red, glow: true);
        Detail(p, Cx + 0.735, Ft + DoorH + 0.15, -6.5 + D2, Cx + 0.75, Ft + DoorH + 0.3, -6.2 + D2, "#40e070", glow: true);
        // The corridor: a grating runner, a light strip.
        Detail(p, Cx - 0.6, Ft, ZCkW + 0.3, Cx + 0.6, Ft + 0.012, ZCabW - 0.3, Grate, "painted grating");
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZCkW + 0.6, ZCabW - 0.6, CCeil);
        // The hull: the cab room's cream sill panels under its glass, the dark posts at its corners and middle; the shoulder out to the
        // body; the crew block's bands, dark below the beam and red above it with the stripe band, its windows and its model plate.
        foreach (var s in new[] { -1, 1 })
        {
            foreach (var (za, zb) in new[] { (Zt, ZS1), (ZS1, ZCk) })
            {
                Facet(p, 2, s, za, zb, Armour, "painted plating");
                Slab(p, 2, s, za, zb, 0.08, 0.92, 0.15, 0.85, 0.02, 0.06, 0.03, Armour2);
                Facet(p, 3, s, za, zb, Glass, glass: true);
                Chine(p, 2, s, za, zb, 0.1, 0.06, Dark);
                Chine(p, 3, s, za, zb, 0.08, 0.06, Dark);
            }
            var gx = Cx + s * CkGlass;
            foreach (var z in new[] { Zt + 0.04, ZS1, ZCk - 0.05 })
                Detail(p, Math.Min(gx, gx + s * 0.05), Sill - 0.06, z - 0.045, Math.Max(gx, gx + s * 0.05), 3.66, z + 0.045, Dark, "painted steel");
            Detail(p, Math.Min(gx, gx + s * 0.05), 2.55, Zt + 0.08, Math.Max(gx, gx + s * 0.05), 2.6, ZCk - 0.1, Dark, "painted steel"); // (a transom across the side glass)
            // The shoulder: red plate from the cab room out to the body, a cream edge where it meets the body's side.
            Facet(p, 2, s, ZCk, ZCab, Frame, "painted plating");
            Facet(p, 3, s, ZCk, ZCab, Skin, "painted plating");
            foreach (var (_, za, zb) in BandsIn(ZCab, ZCabEnd))
            {
                Band(p, 2, s, za, zb, Frame, 3, plates: false);
                Band(p, 3, s, za, zb, Skin, 5, plates: false);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.2, 0.08, Armour);
                Chine(p, 3, s, za, zb, 0.16, 0.08, Frame);
            }
            // Square windows in the upper sides: the bunk room and the galley, the head and the airlock.
            foreach (var z in new[] { -12.2 + D2, -10.2 + D2, -8.0 + D2 })
            {
                var (za, zb) = z < ZNeck ? (ZCab, ZNeck) : (ZNeck, ZCabEnd);
                double len = zb - za, u = (z - za) / len, du = 0.4 / len;
                Slab(p, 3, s, za, zb, u - du - 0.07 / len, u + du + 0.07 / len, 0.4, 0.74, 0.02, 0.07, 0.015, Dark, "painted steel");
                FStrip(p, 3, s, za, zb, u - du, u + du, 0.44, 0.7, 0.072, "#24384a", "glass");
            }
            // The model plate on the crew block's flank: a cream panel with a dark border and the model's marks.
            Slab(p, 3, s, ZNeck, ZCabEnd, 0.5, 0.9, 0.42, 0.78, 0.02, 0.05, 0.015, Dark, "painted plating");
            Slab(p, 3, s, ZNeck, ZCabEnd, 0.52, 0.88, 0.46, 0.74, 0.05, 0.07, 0.01, Armour, "painted plating");
            for (var m = 0; m < 5; m++)
                FStrip(p, 3, s, ZNeck, ZCabEnd, 0.56 + m * 0.06, 0.6 + m * 0.06, 0.53, 0.67, 0.08, m == 0 ? Skin : Dark, "painted plating");
            // The walkway round the cab room: a tread-plate platform on the shoulder's foot each side, a hand rail round it, and a
            // ladder from its outer corner down to the ground (a combine's cab platform).
            double w0 = Cx + s * (CkGlass + 0.06), w1 = Cx + s * 4.3, pz0 = ZCk - 1.2, pz1 = ZCk - 0.02;
            Detail(p, Math.Min(w0, w1), -0.08, pz0, Math.Max(w0, w1), 0.0, pz1, Dark, "tread plate");
            Detail(p, Math.Min(w0, w1) + 0.02, -0.2, pz0 + 0.02, Math.Max(w0, w1) - 0.02, -0.08, pz0 + 0.12, Hazard, "hazard paint");
            foreach (var (x, z) in new[] { (w1 - s * 0.03, pz0 + 0.03), (w1 - s * 0.03, pz1 - 0.06), (w0 + s * 0.6, pz0 + 0.03) })
                Detail(p, x - 0.025, 0.0, z - 0.025, x + 0.025, 1.1, z + 0.025, Hazard, "worn paint");
            Detail(p, Math.Min(w0 + s * 0.6, w1) - 0.02, 1.08, pz0 + 0.005, Math.Max(w0 + s * 0.6, w1) + 0.02, 1.13, pz0 + 0.055, Hazard, "worn paint");
            Detail(p, Math.Min(w1, w1 - s * 0.06) - 0.004, 1.08, pz0 + 0.005, Math.Max(w1, w1 - s * 0.06) + 0.004, 1.13, pz1 - 0.035, Hazard, "worn paint");
            Detail(p, Math.Min(w1, w1 - s * 0.06), 0.55, pz0 + 0.06, Math.Max(w1, w1 - s * 0.06), 0.59, pz1 - 0.08, Hazard, "worn paint");
            var lx = Cx + s * 3.7;
            foreach (var dx in new[] { -0.22, 0.22 })
                Detail(p, lx + dx - 0.025, Ground + 0.3, pz0 - 0.05, lx + dx + 0.025, 0.0, pz0 + 0.0, Frame, "painted steel");
            for (var y = -0.3; y > Ground + 0.35; y -= 0.3)
                Detail(p, lx - 0.22, y - 0.02, pz0 - 0.04, lx + 0.22, y + 0.02, pz0 - 0.005, Bare, "tread plate");
            // Mirrors on arms off the cab room's front corners.
            Detail(p, Math.Min(Cx + s * (CkGlass + 0.06), Cx + s * 2.85), 3.0, Zt + 0.25, Math.Max(Cx + s * (CkGlass + 0.06), Cx + s * 2.85), 3.05, Zt + 0.3, Dark, "painted steel");
            Detail(p, Math.Min(Cx + s * 2.8, Cx + s * 2.88), 2.65, Zt + 0.18, Math.Max(Cx + s * 2.8, Cx + s * 2.88), 3.2, Zt + 0.42, Dark, "painted steel");
        }
        Ribs(p, ZCab, ZCabEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCab, ZCabEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, CRoof) })
                VoidT(p, za, zb, ya, yb, Lw + 0.25, za == ZCab ? 0.25 : 0.004, zb == ZCabEnd ? 0.25 : 0.004);
        // The bow's out jets on the crew block's flanks.
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsOut" + (s < 0 ? "L" : "R"), FacetX(2, s, BowRcsZ, 0.4), 0.4, BowRcsZ, s < 0 ? "-x" : "+x", 0.28);
        // Doors.
        DoorSock(p, "doorCk", true, Cx - 0.75, ZCkW, Ft);
        DoorSock(p, "doorAft", true, Cx - 0.75, ZCabW, Ft);
        DoorSock(p, "doorBunk", false, -11.6 + D2, Cx - 0.875, Ft);
        DoorSock(p, "doorHead", false, -8.3 + D2, Cx - 0.875, Ft);
        DoorSock(p, "doorGalley", false, -12.4 + D2, Cx + 0.875, Ft);
        DoorSock(p, "doorAirlock", false, -7.1 + D2, Cx + 0.875, Ft);
        // The cab room's couches and computer, its lamps and their switch on the back wall.
        Sock(p, "pilot", Cx - 0.7, Ft, -23.9 + D, "+y");
        Sock(p, "copilot", Cx + 0.7, Ft, -23.9 + D, "+y");
        Sock(p, "computer", Cx, Ft, -25.0 + D, "+y");
        Sock(p, "lampCkL", Cx - 1.2, Ft + 2.2, ZCkW - 0.125, "-z");
        Sock(p, "lampCkR", Cx + 1.2, Ft + 2.2, ZCkW - 0.125, "-z");
        Sock(p, "switchCk", Cx + 0.4, Ft + 1.3, ZCkW - 0.125, "-z");
        // The bunk room: two bunks along the outer wall, a locker by the door.
        Sock(p, "bunk0", Cx - Lw + 0.55, Ft, -11.95 + D2, "+y");
        Sock(p, "bunk1", Cx - Lw + 0.55, Ft, -9.85 + D2, "+y");
        Sock(p, "lockerBunk", Cx - 1.5, Ft + 0.125, ZPortW - 0.125, "-z");
        Sock(p, "lampBunk", Cx - Lw, Ft + 2.2, -10.9 + D2, "+x");
        Sock(p, "switchBunk", Cx - 1.0, Ft + 1.3, -11.75 + D2, "-x");
        // The head: the toilet and the basin on the outer wall, the shower in the aft corner.
        Sock(p, "toilet", Cx - Lw, Ft + 0.125, -8.0 + D2, "+x");
        Sock(p, "basin", Cx - Lw, Ft + 0.125, -7.15 + D2, "+x");
        Sock(p, "shower", Cx - 2.7, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "lampHead", Cx - Lw, Ft + 2.3, -6.6 + D2, "+x");
        Sock(p, "switchHead", Cx - 1.0, Ft + 1.3, -8.45 + D2, "-x");
        // The galley: the range and the coffee station on the forward wall, the mess table along the outer wall.
        Sock(p, "range", Cx + 2.5, Ft + 0.45, ZCkW + 0.125, "+z");
        Sock(p, "coffee", Cx + 1.55, Ft + 0.125, ZCkW + 0.125, "+z");
        Sock(p, "table", Cx + 2.6, Ft, -11.0 + D2, "+y");
        Sock(p, "lampGalley", Cx + Lw, Ft + 2.2, -11.6 + D2, "-x");
        Sock(p, "switchGalley", Cx + 1.0, Ft + 1.3, -12.55 + D2, "+x");
        // The airlock: suit lockers on the outer wall, the ship's air on the aft wall, the hatch's switch, the belly hatch's hanging.
        Sock(p, "lifeSupport", Cx + 2.85, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "o2Tank0", Cx + 2.0, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "suit0", Cx + Lw, Ft + 0.125, -7.9 + D2, "-x");
        Sock(p, "suit1", Cx + Lw, Ft + 0.125, -6.4 + D2, "-x");
        Sock(p, "switchHatch", Cx + 1.0, Ft + 1.45, -7.25 + D2, "+x");
        Sock(p, "lampAirlock", Cx + Lw, Ft + 2.3, -8.9 + D2, "-x");
        Sock(p, "boarding", HatchX, Ft - 0.125, BellyHatch.Z1, "-z");
        // The corridor's lamps.
        Sock(p, "lampHallA", Cx - 0.75, Ft + 2.3, -10.0 + D2, "+x");
        Sock(p, "lampHallB", Cx + 0.75, Ft + 2.3, -8.5 + D2, "-x");
        foreach (var (what, z, hx) in new[] { ("cab room front", ZCkFloor, CkHx), ("cab room back", ZCk, CkHx), ("crew block front", ZCkW - 0.125, Lw + 0.25), ("crew block aft", ZNeck, Lw + 0.25), ("crew block's aft wall", ZCabEnd, PassOut + SLine) })
            foreach (var y in new[] { 0.0, z < ZCab ? Sill : CRoof })
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
            Detail(p, face - s * 0.06, Ft + 1.004, z - 0.03, face - s * 0.004, Ft + 1.04, z + 0.03, Frame, "painted steel");
        Detail(p, face - s * 0.12, Ft + 0.004, a0 + 0.008, face - s * 0.004, ceil - 0.004, a0 + 0.16, Frame, "dirty steel");
        Detail(p, face - s * 0.022, Ft + 0.9 - 0.03, l0 + 0.014, face - s * 0.007, Ft + 0.9 + 0.03, l1 - 0.004, WallTrim, "painted steel");
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
        var p = Big("Haywain Bay", "The Haywain's container bay: six standard 2 m containers, two across and three along, in cells sunk into the hull and open to the sky inside the tank coaming, so a dock's crane lifts each one straight up between its cell guides; twist locks under their corners, a passage down each side and a cross passage from the crew block.");
        Rect cross = new(Cx - PassOut, ZCabEnd, Cx + PassOut, BayZ0), bay = new(Cx - PassIn, BayZ0, Cx + PassIn, BayZ1);
        Rect passL = new(Cx - PassOut, BayZ0, Cx - PassIn, BayZ1), passR = new(Cx + PassIn, BayZ0, Cx + PassOut, BayZ1);
        Floor(p, [cross, passL, passR], 0, Ft, Deck, "painted deck plate");
        Floor(p, [bay], 0, Ft, RackCol, "dirty steel");
        Floor(p, [cross, passL, passR], SCeil, SRoof, Ceil, "painted plating");
        Room(p, new Rect(Cx - PassOut + 0.1, ZCabEnd, Cx + PassOut - 0.1, BayZ0), 0, SRoof);
        foreach (var s in new[] { -1, 1 })
            Room(p, new Rect(s < 0 ? Cx - PassOut + 0.1 : Cx + PassIn, BayZ0, s < 0 ? Cx - PassIn : Cx + PassOut - 0.1, BayZ1), 0, SRoof);
        Room(p, bay, 0, 3.8);
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
                // A cell's stencil over each container's roof: a cream plate with a red tab.
                if (sz < 0)
                    foreach (var xs in new[] { -1, 1 })
                    {
                        double b0 = sz * (BayIn - 0.03), b1 = sz * (BayIn - 0.0255);
                        Detail(p, Cx + xs * CtX - 0.45, 2.95, Math.Min(b0, b1), Cx + xs * CtX + 0.45, 3.3, Math.Max(b0, b1), Paper, "painted plating");
                        Detail(p, Cx + xs * CtX - 0.38, 3.0, Math.Max(b0, b1) + 0.001, Cx + xs * CtX - 0.2, 3.25, Math.Max(b0, b1) + 0.005, Skin, "painted plating");
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
                // The cell guides between the rows: an angle up the wall at each row's end, its head yellow.
                for (var r = 1; r < Rows; r++)
                {
                    var gz = RowZ(r) - RowIn;
                    double g0 = s * (BayHx - 0.09), g1 = s * (BayHx - 0.026);
                    Detail(p, Math.Min(g0, g1), Ft + 0.004, gz - 0.1, Math.Max(g0, g1), BayTop - 0.25, gz + 0.1, Frame, "dirty steel");
                    Detail(p, Math.Min(g0, g1) - 0.004, BayTop - 0.25, gz - 0.104, Math.Max(g0, g1) + 0.004, BayTop - 0.004, gz + 0.104, Hazard, "hazard paint");
                }
            });
        }
        // The twist locks under the containers' corner castings: red pads on the bay's floor (their tops take the containers' feet),
        // yellow lines round each container's footprint.
        for (var r = 0; r < Rows; r++)
            for (var si = 0; si < 2; si++)
            {
                var s = si == 0 ? -1 : 1;
                var n = s < 0 ? "L" : "R";
                double xc = Cx + s * CtX, zc = RowZ(r);
                var k = 0;
                foreach (var dx in new[] { -1, 1 })
                    foreach (var dz in new[] { -1, 1 })
                    {
                        double x = xc + dx * CastX, z = zc + dz * CastZ;
                        Solid(p, x - 0.17, Ft, z - 0.17, x + 0.17, Ft + LockH, z + 0.17, Latch, "worn paint");
                        Detail(p, x - 0.05, Ft + LockH, z - 0.09, x + 0.05, Ft + LockH + 0.015, z + 0.09, Frame, "steel");
                        p.Sock($"lock{n}{r}{k++}", P(x, Ft + LockH, z), "+y", Medium); // (a container is a medium part)
                    }
                foreach (var dx in new[] { -1, 1 })
                    Detail(p, xc + dx * (CtW / 2 + 0.03) - 0.03, Ft, zc - CtL / 2 + 0.2, xc + dx * (CtW / 2 + 0.03) + 0.03, Ft + 0.008, zc + CtL / 2 - 0.2, Hazard, "hazard paint");
            }
        // A low frame across the bay's floor between each two rows.
        for (var r = 1; r < Rows; r++)
            Detail(p, Cx - BayHx + 0.02, Ft, RowZ(r) - RowIn - 0.05, Cx + BayHx - 0.02, Ft + 0.05, RowZ(r) - RowIn + 0.05, Frame, "dirty steel");
        // The lining down the passages' outer sides and the cross passage's ends.
        foreach (var s in new[] { -1, 1 })
        {
            Lining(p, s, ZCabEnd, BayZ0, SRoof, SCeil, true, false);
            Lining(p, s, BayZ0, BayZ1, SRoof, SCeil, false, true);
            // The frame's beam over each passage at the bay's ends.
            foreach (var z in new[] { BayZ0, -RowIn, RowIn })
                Detail(p, Cx + s * (PassIn + 0.004), SCeil - 0.14, z + 0.012, Cx + s * (PassOut - 0.124), SCeil - 0.004, z + 0.152, Frame, "dirty steel");
        }
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZCabEnd, ZEng))
            {
                Band(p, 2, s, za, zb, Frame, 11 + i, plates: false);
                Band(p, 3, s, za, zb, Skin, 12 + i, v0: 0.28);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.2, 0.08, Armour);
                Chine(p, 3, s, za, zb, 0.16, 0.08, Frame);
            }
        Ribs(p, ZCabEnd, ZEng, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCabEnd, ZEng))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, SRoof) })
                VoidT(p, za, zb, ya, yb, PassOut + SLine + 0.004, za == ZCabEnd ? 0.0 : 0.004, zb == ZEng ? 0.0 : 0.004);
        // The lamps: the cross passage's, a pair down each passage; the engineering board and its switch on the aft bulkhead's back.
        Sock(p, "lampCrossL", Cx - PassOut, Ft + 2.0, (ZCabEnd + BayZ0) / 2, "+x");
        Sock(p, "lampCrossR", Cx + PassOut, Ft + 2.0, (ZCabEnd + BayZ0) / 2, "-x");
        foreach (var s in new[] { -1, 1 })
            for (var r = 0; r < Rows; r++)
                Sock(p, $"lamp{(s < 0 ? "L" : "R")}{r}", Cx + s * PassOut, Ft + 2.0, RowZ(r) + 1.0, s < 0 ? "+x" : "-x");
        Sock(p, "board", Cx - 1.8, Ft + 0.9, BayZ1, "+z");
        Sock(p, "switchEng", Cx + 0.9, Ft + 1.3, BayZ1, "+z");
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
        var p = Big("Haywain Engineering", "The Haywain's engine box, open to both passages: the power plant and the batteries, the Engineer's Panel, an MMU dock, the fuel manifolds to the drive pods' pylons, and a suit locker for the space walk out to the pods. Louvred grilles down its flanks.");
        var plan = new Rect(Cx - PassOut, ZEng, Cx + PassOut, ZEngEnd);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate");
        Floor(p, [new Rect(plan.X0, ZEng, plan.X1, ZEngAW - 0.125)], ECeil, ERoof, Ceil, "painted plating");
        Room(p, new Rect(plan.X0 + 0.2, ZEng, plan.X1 - 0.2, ZEngEnd), 0, ERoof);
        Ceils[p] = ECeil;
        Detail(p, Cx - 0.75, Ft, ZEng + 0.3, Cx + 0.75, Ft + 0.012, ZEngAW - 0.3, Grate, "painted grating");
        foreach (var x in new[] { -0.79, 0.75 })
            Detail(p, Cx + x, Ft + 0.004, ZEng + 0.3, Cx + x + 0.04, Ft + 0.016, ZEngAW - 0.3, Hazard, "hazard paint");
        foreach (var s in new[] { -1, 1 })
        {
            Lining(p, s, ZEng, ZEngMid, ERoof, ECeil, true, false);
            Lining(p, s, ZEngMid, ZEngAW - 0.125, ERoof, ECeil, false, true);
            var face = Cx + s * PassOut;
            // The fuel manifold: a feed up the lining to the pylon's pass-through, a valve wheel and a gauge on it.
            foreach (var z in new[] { PylonZ - 0.6, PylonZ + 0.6 })
                Detail(p, face - s * 0.12, Ft + 0.004, z - 0.05, face - s * 0.02, ECeil - 0.004, z + 0.05, z < PylonZ ? Red : Bare, "painted steel");
            Detail(p, face - s * 0.16, 1.1, PylonZ - 0.75, face - s * 0.12, 1.4, PylonZ - 0.45, Red, "painted steel");
            Detail(p, face - s * 0.2, 1.75, PylonZ - 0.1, face - s * 0.004, 2.25, PylonZ + 0.3, Frame, "dirty steel");
            Detail(p, face - s * 0.205, 1.85, PylonZ, face - s * 0.2, 2.15, PylonZ + 0.2, Paper, "plastic");
            Detail(p, face - s * 0.14, ECeil - 0.3, PylonZ - 0.7, face - s * 0.004, ECeil - 0.18, PylonZ + 0.7, Hazard, "hazard paint");
        }
        WallRun(p, true, plan.X0 - SLine, plan.X1 + SLine, ZEngAW, Ft, ECeil);
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZEng + 0.6, ZEngAW - 0.6, ECeil);
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZEng, ZEngEnd))
            {
                Band(p, 2, s, za, zb, Frame, 21 + i, plates: false);
                Band(p, 3, s, za, zb, Skin, 22 + i, plates: false);
                Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.2, 0.08, Armour);
                Chine(p, 3, s, za, zb, 0.16, 0.08, Frame);
                if (zb - za > 1.5)
                {
                    // The engine box's flanks: a long louvred grille in a dark frame over the stripe band, its slats red.
                    double len = zb - za, u0 = 0.3 / len, u1 = 1 - 0.3 / len;
                    Slab(p, 3, s, za, zb, u0, u1, 0.28, 0.93, 0.02, 0.07, 0.03, Skin2);
                    Slab(p, 3, s, za, zb, u0 + 0.12 / len, u1 - 0.12 / len, 0.33, 0.88, 0.065, 0.078, 0.005, Dark, "soot");
                    for (var r = 0; r < 12; r++)
                    {
                        var v = 0.35 + 0.51 * (r + 0.5) / 12;
                        FStrip(p, 3, s, za, zb, u0 + 0.14 / len, u1 - 0.14 / len, v - 0.012, v + 0.012, 0.09, Skin, "painted plating");
                    }
                }
            }
        Ribs(p, ZEng, ZEngEnd, 2, 3);
        // The nacelles' pylons' hardpoints on the flanks.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * (PassOut + SLine + 0.004), NacY - 0.32, PylonZ - 0.6, Cx + s * PylonX, NacY + 0.32, PylonZ + 0.6, Frame, "dirty steel");
            Sock(p, s < 0 ? "nacL" : "nacR", Cx + s * PylonX, NacY, PylonZ, s < 0 ? "-x" : "+x");
        }
        foreach (var (_, za, zb) in BandsIn(ZEng, ZEngEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, ERoof) })
                VoidT(p, za, zb, ya, yb, PassOut + SLine + 0.004, za == ZEng ? 0.0 : 0.004, zb == ZEngEnd ? 0.0 : 0.004);
        Sock(p, "panel", Cx - PassOut, Ft + 0.6, 6.9 - D2, "+x");
        Sock(p, "tools", Cx - PassOut, Ft + 0.9, 8.9 - D2, "+x");
        Sock(p, "plant", Cx - 2.3, Ft, 9.2 - D2, "+y"); // (to port: the airlock, its shaft and the batteries are to starboard)
        for (var i = 0; i < 3; i++)
            Sock(p, $"battery{i}", Cx + PassOut - 0.3, Ft, 6.2 - D2 + i * 0.7, "+y");
        Sock(p, "suit", Cx + 2.6, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "locker", Cx + 1.8, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "mmuDock", Cx - 0.4, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "lampEngL", Cx - PassOut, Ft + 2.3, 7.6 - D2, "+x");
        Sock(p, "lampEngR", Cx + PassOut, Ft + 2.3, 7.9 - D2, "-x");
        foreach (var z in new[] { plan.Z0, ZEngMid, plan.Z1 })
            foreach (var y in new[] { 0.0, ERoof })
                MustFit("engineering", z, PassOut + SLine, y);
        return p.Stats(14000, 30000, 30, 15.0);
    }

    // ---- the stern: the hull closing in behind engineering, the transom, the stern's attitude jets

    /// <summary>The cab room's front: one big flat windscreen in a dark frame from the sill to the roof, a cream panel under it with the
    /// headlamps and a bumper, and the roof's visor overhanging it with a row of work lamps (a combine cab's face).</summary>
    private static Part NoseCap()
    {
        var p = Big("Haywain Windscreen", "The Haywain's cab front: one big windscreen in a dark frame, a cream panel under it with the headlamps and a bumper, and the roof's visor over it with a row of work lamps.", color: Armour);
        var q = At(Zt);
        double z = Zt - 0.025, hw = q[2].H;
        // The panel under the glass, the glass, the band over it.
        Plate(p, [P(Cx - q[0].H, q[0].Y, z), P(Cx + q[0].H, q[0].Y, z), P(Cx + q[1].H, q[1].Y, z), P(Cx + q[2].H, q[2].Y, z), P(Cx - q[2].H, q[2].Y, z), P(Cx - q[1].H, q[1].Y, z)], Armour, "painted plating");
        Plate(p, [P(Cx - hw, q[2].Y, z + 0.006), P(Cx + hw, q[2].Y, z + 0.006), P(Cx + hw, q[3].Y, z + 0.006), P(Cx - hw, q[3].Y, z + 0.006)], Glass, glass: true);
        Plate(p, [P(Cx - q[3].H, q[3].Y, z), P(Cx + q[3].H, q[3].Y, z), P(Cx + q[4].H, q[4].Y, z), P(Cx - q[4].H, q[4].Y, z)], Armour, "painted plating");
        // The frame round the glass, and a wiper parked across its foot.
        Detail(p, Cx - hw - 0.02, q[2].Y - 0.05, z - 0.07, Cx - hw + 0.08, q[3].Y + 0.05, z - 0.02, Dark, "painted steel");
        Detail(p, Cx + hw - 0.08, q[2].Y - 0.05, z - 0.07, Cx + hw + 0.02, q[3].Y + 0.05, z - 0.02, Dark, "painted steel");
        Detail(p, Cx - hw + 0.08, q[2].Y - 0.05, z - 0.07, Cx + hw - 0.08, q[2].Y + 0.05, z - 0.021, Dark, "painted steel");
        Detail(p, Cx - hw + 0.08, q[3].Y - 0.05, z - 0.07, Cx + hw - 0.08, q[3].Y + 0.05, z - 0.021, Dark, "painted steel");
        Detail(p, Cx - 0.9, q[2].Y + 0.06, z - 0.05, Cx + 0.3, q[2].Y + 0.09, z - 0.03, Frame, "painted steel");
        // The visor: the roof running on 0.5 m over the glass, its lip dark, four work lamps under it.
        var ry = q[4].Y;
        Detail(p, Cx - hw - 0.12, ry - 0.07, Zt - 0.55, Cx + hw + 0.12, ry + 0.08, Zt - 0.03, Armour2, "painted plating");
        Detail(p, Cx - hw - 0.124, ry - 0.11, Zt - 0.56, Cx + hw + 0.124, ry - 0.07, Zt - 0.5, Dark, "painted steel");
        foreach (var x in new[] { -1.3, -0.55, 0.55, 1.3 })
        {
            Detail(p, Cx + x - 0.15, ry - 0.24, Zt - 0.48, Cx + x + 0.15, ry - 0.074, Zt - 0.3, Dark, "painted steel");
            Detail(p, Cx + x - 0.12, ry - 0.21, Zt - 0.485, Cx + x + 0.12, ry - 0.1, Zt - 0.48, Glow, glow: true);
        }
        // The headlamps, the indicators at the corners, a grille between, the bumper.
        foreach (var sd in new[] { -1, 1 })
        {
            Detail(p, Cx + sd * 1.1 - 0.16, 0.42, Zt - 0.11, Cx + sd * 1.1 + 0.16, 0.7, Zt - 0.03, Dark, "painted steel");
            Detail(p, Cx + sd * 1.1 - 0.12, 0.46, Zt - 0.115, Cx + sd * 1.1 + 0.12, 0.66, Zt - 0.11, "#f3eccd", glow: true);
            Detail(p, Cx + sd * 1.52 - 0.08, 0.45, Zt - 0.1, Cx + sd * 1.52 + 0.08, 0.6, Zt - 0.03, Hazard, glow: true);
        }
        Detail(p, Cx - 0.7, 0.4, Zt - 0.08, Cx + 0.7, 0.75, Zt - 0.03, Dark, "soot");
        for (var k = 0; k < 4; k++)
            Detail(p, Cx - 0.65, 0.45 + k * 0.08, Zt - 0.1, Cx + 0.65, 0.48 + k * 0.08, Zt - 0.08, Bare, "steel");
        Detail(p, Cx - 1.62, 0.02, Zt - 0.2, Cx + 1.62, 0.26, Zt - 0.03, Frame, "dirty steel");
        Detail(p, Cx - 1.6, 0.06, Zt - 0.205, Cx - 1.0, 0.22, Zt - 0.2, Hazard, "hazard paint");
        Detail(p, Cx + 1.0, 0.06, Zt - 0.205, Cx + 1.6, 0.22, Zt - 0.2, Hazard, "hazard paint");
        Hidden(p, Cx - 1.4, Ft, Zt - 0.12, Cx + 1.4, Sill, Zt + 0.004);
        return p.Stats(900, 6000, 30, 10.0);
    }

    private static Part SternPart()
    {
        var p = Big("Haywain Stern", "The Haywain's stern: the engine box tucking in to a flat transom with a big radiator grille, tail lights and a ladder, and the stern's attitude jets.", color: Skin);
        Facet(p, 0, 1, ZEngEnd, Stern, Chassis, "plated steel");
        Facet(p, 5, 1, ZEngEnd, Stern, Skin2, "painted plating");
        Slab(p, 5, 1, ZEngEnd, Stern, 0.12, 0.9, 0.12, 0.88, 0.02, 0.1, 0.05, Skin);
        foreach (var s in new[] { -1, 1 })
        {
            for (var k = 1; k <= 4; k++)
            {
                Facet(p, k, s, ZEngEnd, Stern, k <= 2 ? Frame : Skin, "painted plating");
                Slab(p, k, s, ZEngEnd, Stern, 0.14, 0.92, 0.1, 0.9, 0.02, k == 2 ? 0.14 : 0.1, 0.05, k <= 2 ? Frame : k == 3 ? Skin2 : Skin, "painted plating");
            }
            Chine(p, 1, s, ZEngEnd, Stern, 0.2, 0.09, Frame);
            Chine(p, 2, s, ZEngEnd, Stern, 0.2, 0.08, Armour);
            Chine(p, 3, s, ZEngEnd, Stern, 0.18, 0.08, Frame);
            Chine(p, 4, s, ZEngEnd, Stern, 0.16, 0.08, Armour);
        }
        Ribs(p, ZEngEnd, Stern, 0, 1, 2, 3, 4, 5);
        // The transom: a dark plate, tail lights, a docking floodlight, a hazard band over the jets.
        var q = At(Stern);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, Stern)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, Stern)))], Frame, "soot");
        // The radiator grille across the transom: a dark frame, red slats; tail lights and amber indicators either side; a hazard band
        // under it; a ladder up the port side to the roof.
        Detail(p, Cx - 2.4, 1.0, Stern + 0.02, Cx + 2.4, 2.6, Stern + 0.07, Dark, "soot");
        for (var r = 0; r < 9; r++)
            Detail(p, Cx - 2.3, 1.08 + r * 0.17, Stern + 0.07, Cx + 2.3, 1.14 + r * 0.17, Stern + 0.1, Skin, "painted plating");
        Detail(p, Cx - 2.44, 2.6, Stern + 0.02, Cx + 2.44, 2.68, Stern + 0.09, Armour, "painted plating");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 3.05 - 0.2, 1.3, Stern + 0.02, Cx + s * 3.05 + 0.2, 1.7, Stern + 0.06, Red, glow: true);
            Detail(p, Cx + s * 3.05 - 0.2, 1.75, Stern + 0.02, Cx + s * 3.05 + 0.2, 1.95, Stern + 0.06, Hazard, glow: true);
            Detail(p, Cx + s * 1.95 - 0.5, 0.35, Stern + 0.02, Cx + s * 1.95 + 0.5, 0.6, Stern + 0.05, Hazard, "hazard paint");
        }
        foreach (var dx in new[] { -0.22, 0.22 })
            Detail(p, Cx - 3.2 + dx - 0.025, 0.2, Stern + 0.02, Cx - 3.2 + dx + 0.025, 2.95, Stern + 0.2, Frame, "painted steel");
        for (var y = 0.4; y < 2.9; y += 0.3)
            Detail(p, Cx - 3.42, y - 0.02, Stern + 0.15, Cx - 2.98, y + 0.02, Stern + 0.19, Bare, "tread plate");
        Detail(p, Cx - 0.3, 2.75, Stern + 0.02, Cx + 0.3, 2.9, Stern + 0.07, Glow, glow: true);
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
        var name = s < 0 ? "Haywain Drive Pod L" : "Haywain Drive Pod R";
        var p = new Part(name, Small, Section, $"One of the Haywain's drive pods, {(s < 0 ? "to port" : "to starboard")}: a long fuel tank forward of an engine housing, slung low on a stub pylon off the engine box's flank. The drive bolts to the housing's tail plate outside, its Drive Core and Fuel Pump inside behind it, mended through the service hatch in its outboard flank on a space walk.", role: "fueltank", tool: "welder", material: "steel", color: Skin, group: Group);
        double cx = Cx + s * NacX, cy = NacY, f = NacF, c = EngineHousing.HalfFlat(NacF);
        double t0 = NacTank0, t1 = NacTank1;
        // The nose: an amber tip, a short cone; the tank: red plates in bands with a cream band between, dark collars over the seams.
        EngineHousing.Ring(p, P, cx, cy, 0.3, 0.12, NacNose, 0.6, 0.25, NacNose + 0.4, Hazard, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, 0.6, 0.25, NacNose + 0.404, f, c, t0, Skin, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, f, c, t0 + 0.004, f, c, t0 + 1.8, Skin2, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, f, c, t0 + 1.804, f, c, t0 + 2.8, Armour, "painted plating");
        EngineHousing.Ring(p, P, cx, cy, f, c, t0 + 2.804, f, c, t1 - 0.004, Skin, "painted plating");
        foreach (var z in new[] { t0 + 0.6, t0 + 4.4, t1 - 1.2 })
            EngineHousing.Ring(p, P, cx, cy, f + 0.03, c + 0.012, z - 0.08, f + 0.03, c + 0.012, z + 0.08, Frame, "dirty steel");
        EngineHousing.Draw(p, P, cx, cy, f, c, t1, NacTail, s, Skin, Skin2, Frame, Hazard, "worn paint", ShipWorksParts.CoreR[1], ShipWorksParts.CoreD[1], ShipWorksParts.CoreD[1] + ShipWorksParts.PumpD[1]);
        EngineHousing.Ring(p, P, cx, cy, f + 0.035, c + 0.014, t1 - 0.1, f + 0.035, c + 0.014, t1 + 0.1, Armour, "plated steel"); // (the band over the bulkhead)
        // A fuel line along its inboard flank, a grab rail and a step under the hatch, a fill cap in red, the registry's stripe.
        Detail(p, cx - s * (f + 0.02), cy - 0.35, t0 + 0.4, cx - s * (f + 0.08), cy - 0.29, t1 - 0.2, Red, "painted steel");
        foreach (var z in new[] { NacTail - 2.5, NacTail - 0.5 })
            Detail(p, cx + s * (f + 0.02), cy + 0.5, z - 0.03, cx + s * (f + 0.3), cy + 0.54, z + 0.03, "#9a9c98", "steel");
        Detail(p, cx + s * (f + 0.28), cy + 0.5, NacTail - 2.55, cx + s * (f + 0.33), cy + 0.56, NacTail - 0.45, Hazard, "worn paint");
        Detail(p, cx + s * (f - 0.05), cy - 0.62, NacTail - 2.0, cx + s * (f + 0.3), cy - 0.58, NacTail - 0.7, Dark, "tread plate");
        Detail(p, cx - 0.1, cy + f + 0.004, t1 - 1.9, cx + 0.1, cy + f + 0.12, t1 - 1.7, Red, "worn paint");
        Detail(p, cx + s * (f + 0.004), cy + 0.1, t0 + 0.9, cx + s * (f + 0.016), cy + 0.5, t0 + 1.6, Paper, "painted plating");
        // Running lights at the nose (red to port, green to starboard) and a white one at the tail's top.
        Detail(p, cx + s * 0.62 - 0.06, cy - 0.06, NacNose + 0.6, cx + s * 0.62 + 0.06, cy + 0.06, NacNose + 0.75, s < 0 ? Red : Green, glow: true);
        Detail(p, cx - 0.06, cy + f + 0.004, NacTail - 0.4, cx + 0.06, cy + f + 0.08, NacTail - 0.25, Glow, glow: true);
        // The pylon: a solid spar from the hardpoint into the tank, under a swept fairing; the fuel feed along it.
        Solid(p, Cx + s * PylonX, cy - 0.2, PylonZ - 2.4, cx - s * (f - 0.03), cy + 0.2, PylonZ + 2.4, Frame, "dirty steel");
        Prism(p, [P(Cx + s * (PylonX + 0.004), cy - 0.32, PylonZ - 3.0), P(Cx + s * (PylonX + 0.004), cy + 0.32, PylonZ - 3.0), P(Cx + s * (PylonX + 0.004), cy + 0.32, PylonZ + 3.0), P(Cx + s * (PylonX + 0.004), cy - 0.32, PylonZ + 3.0)],
            [P(cx - s * (f + 0.004), cy - 0.24, PylonZ - 2.6), P(cx - s * (f + 0.004), cy + 0.24, PylonZ - 2.6), P(cx - s * (f + 0.004), cy + 0.24, PylonZ + 2.8), P(cx - s * (f + 0.004), cy - 0.24, PylonZ + 2.8)], Armour, "painted plating");
        Detail(p, Cx + s * (PylonX + 0.2), cy + 0.27, PylonZ - 2.5, cx - s * (f + 0.1), cy + 0.33, PylonZ - 2.0, Hazard, "hazard paint");
        Hidden(p, cx - (f - 0.38), cy - (f - 0.38), t0, cx + (f - 0.38), cy + (f - 0.38), t1 - 0.004);
        Sock(p, "hull", Cx + s * PylonX, cy, PylonZ, s < 0 ? "+x" : "-x");
        Works.Mount(p, outside: true); // (the housing's tail plate: the drive bolts to it outside, its engine inside)
        RcsPad(p, "rcsFore", cx, cy, NacNose + 0.06, "-z", 0.05);
        // Pitch jets on the tank's top and bottom flats, far aft: they push the nose round.
        RcsPad(p, "rcsUp", cx, cy + f, t1 - 1.4, "+y", 0.05);
        RcsPad(p, "rcsDown", cx, cy - f, t1 - 1.4, "-y", 0.05);
        p.Set(("fuelTank", Kw(("capacity", NacFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx - s * (f - 0.38), cy, t1 - 2.0).Cast<object>()], s < 0 ? "+x" : "-x", "fuel"))));
        return p.Stats(3800, 7000, 20, 10.0);
    }

    // ---- the flying parts: lift jets, the power plant, the flight computer (the Dray's kinds, in the Haywain's colours and size)

    private static Part LiftJet()
    {
        const double k = JetScale;
        var p = new Part("Haywain Lift Jet", Small, Section, $"A lift jet: {LiftForce / 1e6:0.0} MN straight down. Six under the Haywain's crew block, bay and engine box hold it up loaded, from the keel's tanks.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
        double[] R(double r, double y, int i, int n = 8)
        {
            var an = Math.PI / n + i * 2 * Math.PI / n;
            return [Py.Round(r * k * Math.Cos(an), 4), Py.Round(y * k, 4), Py.Round(r * k * Math.Sin(an), 4)];
        }
        Prism(p, [.. Enumerable.Range(0, 8).Select(i => R(0.55, -0.003, i))], [.. Enumerable.Range(0, 8).Select(i => R(0.48, -0.45, i))], Skin, "worn paint");
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
        var p = new Part("Haywain Power Plant", Small, Section, "The Haywain's power plant: a stack of cells in a cage, 400 kW for the docks, the lights, the life support and the flight computer. No fuel.", role: "generator", tool: "welder", material: "steel", color: Armour, group: Group);
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

    /// <summary>The flight computer: a flight stabilizer with a courier's gyro (half the Packet's: a lighter hull overshoots on a big one) in a console between the couches and the dash.</summary>
    private static Part FlightComputer()
    {
        const string console = "#22262b";
        var p = new Part("Haywain Flight Computer", Small, Section, "The Haywain's flight computer: a flight stabilizer with a courier's gyro (half the Packet's: a lighter hull overshoots on a big one) in a console between the couches. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: console, group: Group);
        void B(double x0, double y0, double z0, double x1, double y1, double z1, string col, PyDict shape, bool detail = false) =>
            p.Add(Box([x0, y0, z0], [x1, y1, z1], col, detail: detail, shape: shape));
        B(-0.4, 0, -0.28, 0.4, 0.75, 0.28, console, Kw(("finish", "plastic")));
        B(-0.34, 0.38, 0.28, 0.34, 0.68, 0.298, "#78c8e8", Kw(("glow", true)), true); // (its screen faces the couches)
        for (var i = 0; i < 5; i++)
            B(-0.28 + i * 0.12, 0.24, 0.28, -0.22 + i * 0.12, 0.3, 0.298, i % 2 == 0 ? "#60e090" : Hazard, Kw(("glow", true)), true);
        B(-0.05, 0.751, -0.2, 0.05, 0.767, 0.1, Hazard, Kw(("finish", "hazard paint")), true); // (the arrow, forward)
        B(-0.12, 0.751, -0.2, 0.12, 0.767, -0.12, Hazard, Kw(("finish", "hazard paint")), true);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", 20000000))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
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
        var p = new Part(BellyHatchName, Small, Section, "The Haywain's belly hatch: a plate that shuts the boarding shaft under the keel. F (or the airlock's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.", role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
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
        Part WallPiece(int i) => wallPieces[i] ??= Big($"Haywain {shortName} Wall {i + 1}", $"A wall of the Haywain's {what}, with its door frames and what is fixed to it.", color: Wall);
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
                    hull[key] = h = Big($"Haywain {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece beside the Haywain's {what}, the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (ceilY is double y && c[1] > y - 0.35)
                return ceiling ??= Big($"Haywain {shortName} Ceiling", $"The ceiling of the Haywain's {what}.", color: Ceil);
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
                    bands[k] = piece = Big($"Haywain {shortName} {k + 1}", $"A length of the Haywain's {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
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
                Console.WriteLine($"haywain_parts: {shortName} band {k + 1} has only details; they join {to.D["name"]}");
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
            throw new InvalidOperationException("Haywain: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
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
        var bp = new BuildingParts.Blueprint(ShipName, "A medium hauler for the farmers, in a combine harvester's clothes: blocky barn-red cladding, a cream stripe band, a dark chassis. A glass cab room stands out on the nose with a walkway round it; behind it the crew block (bunks, a head, a galley, an airlock over the boarding shaft); then six standard 2 m containers, two across and three along, in a bay open to the sky inside a raised tank coaming, so a dock's crane lifts each one straight out; then the engine box with its exhaust stacks, and a drive pod slung low on each side. Lift jets and legs under it set it down; attitude jets at both ends and on the pods' noses move it about.", defs, maxSteps: 600);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in Structure)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = placed;
        int keel = placed["Haywain Keel"], roof = placed["Haywain Roof"], cab = placed["Haywain Cab"], bay = placed["Haywain Bay"],
            eng = placed["Haywain Engineering"], stern = placed["Haywain Stern"];
        foreach (var d in new[] { "doorCk", "doorAft", "doorBunk", "doorHead", "doorGalley", "doorAirlock" })
            Door(bp, cab, d);
        // The cockpit: the pilot flies through the flight computer between the couches and the glass (Ship keys, the mouse aims the nose).
        var pilot = Hang(bp, "Flight Couch", cab, "pilot", "bottom");
        Hang(bp, "Flight Couch", cab, "copilot", "bottom");
        var fc = Hang(bp, "Haywain Flight Computer", cab, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
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
        Paint(bp, bp.Hang("Haywain Boarding Ladder", hatch, "ladder", "foot"), Frame, "painted steel");
        Switch(bp, cab, "switchHatch", false, hatch); // (off: the hatch shut)
        // Engineering: the panel, the power, the ship's air and a suit for the walk out to the nacelles.
        Hang(bp, "Engineer's Panel", eng, "panel", "back");
        Hang(bp, "Power Board", bay, "board", "back");
        Hang(bp, "Haywain Power Plant", eng, "plant", "bottom");
        for (var i = 0; i < 3; i++)
            Hang(bp, "Battery", eng, $"battery{i}", "-y");
        Hang(bp, EvaParts.LockerName, eng, "suit", "wall");
        Paint(bp, Hang(bp, "Pressure Locker", eng, "locker", "wall"), Armour);
        EvaParts.FitDock(bp, Holder(bp, eng, "mmuDock"), "mmuDock");
        Hang(bp, "Tool Rack", eng, "tools", "back");
        // The nacelles: each on its pylon, its drive on the tail plate outside, the engine inside, the hatch in its outboard flank.
        foreach (var (s, n) in new[] { (-1, "L"), (1, "R") })
        {
            var nac = Hang(bp, $"Haywain Drive Pod {n}", eng, $"nac{n}", "hull");
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
            Hang(bp, "Haywain Lift Jet", keel, $"lift{i}", "top");
        for (var i = 0; i < LegAt.Length; i++)
            LegPodParts.HangPod(bp, LegPod, Holder(bp, keel, $"leg{i}"), $"leg{i}");
        // The cargo: a standard 2 m container in each cell, standing on the bay's twist locks by its corner castings, its hatches
        // shut and its three cradles in it. The cargo is the colour: green for food, a straw yellow for grain and feed, maroon for metal.
        string[] paints = ["#4f9a3e", "#c9a83a", "#6e2a2e", "#c9a83a", "#4f9a3e", "#5d6a74"];
        for (var r = 0; r < Rows; r++)
            foreach (var (n, si) in new[] { ("L", 0), ("R", 1) })
            {
                var paint = paints[r * 2 + si];
                var box = Paint(bp, Hang(bp, ContainerName, bay, $"lock{n}{r}0", "foot.0"), paint, "worn paint");
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
        foreach (var n in new[] { "L0", "L1", "L2", "R0", "R1", "R2" })
            Lamp(bp, bay, $"lamp{n}", "#d8ecff");
        Switch(bp, bay, "switchEng", Lamp(bp, eng, "lampEngL", "#a0d0ff"), Lamp(bp, eng, "lampEngR", "#a0d0ff"));
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
        structure.AddRange(SplitBands(Keel(), "Keel", -14.5)); // (the band under the crew block's lift jets keeps the keel's name)
        structure.AddRange(SplitDeck(Cab(), "Cab"));
        structure.AddRange(SplitDeck(Bay(), "Bay"));
        structure.AddRange(SplitDeck(Engineering(), "Eng"));
        structure.AddRange(SplitBands(Roof(), "Roof", -14.6));
        structure.Add(SternPart());
        structure.Add(NoseCap());
        Connect(structure);
        Structure = PlaceOrder(structure, "Haywain Keel");
        List<Part> parts =
        [
            .. structure, Nacelle(-1), Nacelle(1),
            Ladder("Haywain Boarding Ladder", Py.Round(BoardRise, 3), "The Haywain's boarding ladder: it stands on the belly hatch, stowed up through the airlock's floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.", "foot", [0, 0, -0.1], "-y", over: -0.03),
            BellyHatchPart(), LiftJet(), PowerPlant(), FlightComputer(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"haywain_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/HaywainHauler.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
    }
}
