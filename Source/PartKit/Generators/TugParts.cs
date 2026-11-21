using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Docket, the Traders' tug (Karl, 2026-11-21: "a tug for the trader faction ... a new design that feels very trader", taking
/// from no ship but the Traders' own). A stubby head that does the work and a string it tows: the head is a short, deep hull with a
/// rubber-fendered push bow, the crew's rooms in it, engineering aft of them and a raised glass wheelhouse over engineering, all
/// round glass so the pilot watches the tow; two drive nacelles stacked on an outrigger each side (four bells: a tug is all
/// engine); behind the transom a tow yoke grips an open steel truss, the string, three cells long, each taking one standard 2 m
/// container on twist locks on its top so a dock's crane lifts it straight off, with a railed catwalk down each side to the
/// string's end car and its tail jets and legs. The Traders' mood board's "tug and string", made a tug.
///
/// Built as the Satchel and the Packet are (CourierParts.cs, FreighterParts.cs, whose helpers this copies): the head's hull lofted
/// through stations first, the rooms a box inside it, each big part drawn whole and dealt out into welded pieces. The string is
/// not lofted: its cells are pieces of their own, welded end to end. Ship metres: x across (the centreline at Cx), y up from the
/// deck's underside, z along the ship (forward is -z; 0 is the head's transom); P() moves them by half a cell into a part's frame.
/// The look is the Traders' (their IT style): beige office-case plating with case vents and drive-bay panels on the bow,
/// corporate blue, lanyard yellow (docs/sandbox/constructs/tug.md).
/// </summary>
public sealed class TugParts : IPartGenerator
{
    public string Name => "tug_parts";

    private const string Out = "Constructs/Parts/Docket";
    private const string BpOut = "Constructs/Blueprints";
    private const string Group = "Ship";
    private const string Section = "Docket tug";
    public const string ShipName = "Docket Tug";
    private const double H = 0.125; // half a cell
    private const double T = 0.04; // a drawn plate's thickness

    // The paint: the Traders' (FactionStyle IT), as the Satchel's: old office-case beige over a greyer skin, corporate blue, a dark
    // frame, lanyard yellow where things move and where people go in, link blue lights; the string is the Packet's rack steel and the
    // fenders black rubber.
    private const string Skin = "#d8d0b8", Skin2 = "#c4bca3", Armour = "#1f4e8c", Armour2 = "#2a64a8", Bare = "#8e9296", Frame = "#3b3f44",
        Dark = "#26292d", Hazard = "#f2c400", Orange = "#f2c400", Red = "#c0392b", Glow = "#ffe0a0", Glass = "#86a9b8", Link = "#0f7bd8",
        Paper = "#e6e2d6", Green = "#2f9a52", Rubber = "#1d1e20", Vent = "#2b2e31",
        Wall = "#cdcac0", WallLow = "#2a64a8", WallTrim = "#3b3f44", Deck = "#5d6266", Grate = "#3d4144", Ceil = "#b4b6b2",
        RackCol = "#34383d", Latch = "#c4362b";

    /// <summary>The walls WallRun draws with.</summary>
    private static string WallCol = Wall, DadoCol = WallLow;

    // ---- the plan: the head

    private const double Cx = 0.0; // the centreline
    private const double Ft = 0.25; // the deck's top, all along the head
    private const double CCeil = 2.75, CRoof = 3.0, Lw = 3.25; // the cab: its ceiling, roof slab's top, rooms' inner half width
    private const double PassOut = 3.5, SLine = 0.06; // engineering's lining: its face and thickness
    private const double ECeil = 2.75, ERoof = 3.0; // engineering
    /// <summary>The hull's stations: the bow's push face, the bow's shoulder, the cab's front, its middle, its end (engineering and the
    /// wheelhouse over it begin), engineering's middle, the wheelhouse's end, engineering's end, the transom.</summary>
    private const double Zt = -17.0, ZB = -16.0, ZCab0 = -15.5, ZCabM = -11.6, ZCabEnd = -7.15, ZEngM = -4.4, ZWh1 = -1.65, ZEngEnd = -1.0, Stern = 0.0;
    private const double ZFw = -15.375, ZCabW = -7.275, ZEngAW = -1.125; // the cross walls' middles: the cab's front and aft, engineering's aft
    private const double ZEng = ZCabEnd, ZWh0 = ZCabEnd;
    private const double DoorH = 2.25;
    /// <summary>The cab's rooms: port the bunk room then the head, starboard the galley then the airlock (the cross walls' middles).</summary>
    private const double ZPortW = -10.9, ZStbdW = -11.6;

    /// <summary>The wheelhouse over engineering: its half width, its floor's top, the glass's sill and head, the ladder's opening.</summary>
    private const double WhX = 2.4, WFl = 3.45, WSill = WFl + 1.0, WHead = WFl + 2.3, WRake = 0.35;
    private static readonly Rect WhHole = new(Cx - 0.6, -4.15, Cx + 0.6, -2.95);
    private static double WhLadderZ => WhHole.Z1 - 0.03;

    /// <summary>The hull's stations (the hull pieces are cut at them).</summary>
    private static readonly double[] Bands = [Zt, ZB, ZCab0, ZCabM, ZCabEnd, ZEngM, ZWh1, ZEngEnd, Stern];
    /// <summary>The ring frames standing proud of the skin: the bow's shoulder, the cab's end and engineering's end.</summary>
    private static readonly double[] RibZ = [ZB, ZCabEnd, ZEngEnd];

    // The boarding shaft in the airlock (starboard, aft in the cab), the belly hatch shutting it and the ladder on the hatch (the Satchel's way).
    private static readonly Rect BellyHatch = new(Cx + 1.1, -10.55, Cx + 2.25, -9.3);
    private const string BellyHatchName = "Docket Belly Hatch";
    private static double HatchX => BellyHatch.X0 + 0.575;
    private static double HatchBottom => At(BellyHatch.Z1)[0].Y - 0.012;
    private static double HatchTop => HatchBottom + 0.08;
    private static double HatchTravel => Math.Min(HatchBottom - Ground - 0.005, CCeil - 0.03 - Ft);
    private static double LadderZ => BellyHatch.Z1 - 0.03;
    private static double BoardRise => Ft - (HatchTop - HatchTravel);

    // ---- the string: the tow yoke behind the transom, three cells of open truss, the end car

    /// <summary>The truss: its chords' outer half width, bottom and top (the containers' feet stand on its top), a chord's section.</summary>
    private const double TW = 1.35, TB = -0.5, TT = 0.85, Chord = 0.2;
    private const double YokeZ1 = 1.5, CellL = 6.9, EndL = 2.2;
    private const int Cells = 3;
    private static double CellZ0(int i) => YokeZ1 + i * CellL;
    private static double EndZ0 => CellZ0(Cells);
    private static double EndZ1 => EndZ0 + EndL;
    /// <summary>The catwalk outboard of the chords: its outer edge (the rail).</summary>
    private const double WalkX = TW + 0.75;
    /// <summary>The standard container (the Satchel's numbers): across, up, along, its twist locks' pads and its corner castings.</summary>
    private const double CtW = 2.5, CtH = 2.5, CtL = 6.5, LockH = 0.06, CastX = 1.165, CastZ = 3.165;
    public const string ContainerName = "Standard Container 2m";

    // ---- flying it: four drive nacelles, lift jets under the head, legs under the head and the end car, attitude jets at both ends.

    /// <summary>The nacelles: middles off the centreline and up (upper, lower), the housing's flat distance, the nose, the tank's ends, the tail.</summary>
    private const double NacX = 6.4, NacF = 1.4;
    private static readonly double[] NacYs = [3.2, -0.1];
    private const double PylonX = 4.2, PylonZ = -5.5; // the pylons' hardpoints on engineering's flank
    private const double NacNose = PylonZ - 5.2, NacTank0 = PylonZ - 3.8, NacTank1 = PylonZ + 3.0, NacTail = PylonZ + 8.4;
    private const int KeelFuel = 9000, NacFuel = 5000; // litres
    private const double LiftForce = 1700000, JetScale = 1.05, Burn = 2.5;
    private static readonly (double X, double Z)[] LiftAt = [(-2.25, -11.1), (2.25, -11.1), (-2.25, -0.5), (2.25, -0.5)];
    private static readonly (double X, double Z)[] LegAt = [(-2.2, -14.4), (2.2, -14.4), (-2.4, -3.0), (2.4, -3.0)];
    private static readonly double[] EndLegX = [-1.0, 1.0];
    private const string LegPod = "S"; // (small pods, as the Packet's and the Satchel's)
    private const double PadProud = 0.1; // a jet's mount under the flat
    private const double LegProud = 0.16; // a leg pod's mount
    private const double KeelY = -0.8; // the flat's height (the head's and the end car's)
    private static double Ground => KeelY - LegProud - LegPodParts.ReachOf(LegPod);
    private const string Rcs = "Heavy RCS Thruster";
    private const double BowRcsZ = -16.4, RcsPadW = 0.6;

    // ---- the hull: stations of a half profile (P0 the keel's edge, P1 the lower chine, P2 the beam, P3 the upper chine, P4 the roof's edge)

    private static readonly (double, double)[] FullSec = [(2.9, -0.8), (3.8, 0.0), (4.0, 1.5), (3.85, 2.95), (3.0, 3.3)];

    // A blunt push face (a tug's: wide and flat, the fenders on it), out to the full section a metre aft, which runs the head's length
    // and tucks in a little to the transom, where the yoke takes the string.
    private static readonly Sec[] Hull =
    [
        new(Zt, [(2.3, -0.35), (3.15, 0.2), (3.35, 1.5), (3.2, 2.8), (2.45, 3.12)]),
        new(ZB, FullSec), new(ZEngEnd, FullSec),
        new(Stern, [(2.6, -0.65), (3.45, 0.05), (3.7, 1.5), (3.55, 2.85), (2.75, 3.18)]),
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
            throw new InvalidOperationException($"tug_parts: {what} at z {z}, y {y} is {hx:0.00} from the centreline and the skin {s:0.00}");
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


    // ---- the Docket's own helpers

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


    // ---- the keel: the flat underside the head's length, the void under the floors (fuel), the boarding shaft, the jets' and legs' mounts

    private static Part Keel()
    {
        var p = Big("Docket Keel", "The Docket's keel: the flat underside of the tug's head from the push bow to the transom, with the fuel tankage in the void under the decks, the boarding shaft, and the lift jets' and legs' mounts.", "fueltank", Armour);
        var shaft = new Rect(BellyHatch.X0 - 0.05, BellyHatch.Z0 - 0.05, BellyHatch.X1 + 0.05, BellyHatch.Z1 + 0.05);
        foreach (var (i, za, zb) in BandsIn(Zt, Stern))
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
                Band(p, 1, s, za, zb, Skin2, 1 + i, plates: za >= ZB - 1e-6);
                Chine(p, 1, s, za, zb, 0.2, 0.09, Frame);
            }
            // Keel skids along the flat.
            if (zb - za > 2 && za >= ZB - 1e-6)
                foreach (var s in new[] { -1, 1 })
                    Detail(p, Cx + s * 2.6 - 0.08, y - 0.12, za + 0.3, Cx + s * 2.6 + 0.08, y - 0.023, zb - 0.3, Dark, "dirty steel");
        }
        Ribs(p, Zt, Stern, 0, 1);
        // The void under the floors (the tankage), round the shaft.
        foreach (var (i, za, zb) in BandsIn(Zt, Stern))
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
        // The lift jets' and the legs' mounts: square pads under the flat.
        for (var i = 0; i < LiftAt.Length; i++)
            MountPad(p, $"lift{i}", Cx + LiftAt[i].X, At(LiftAt[i].Z)[0].Y, LiftAt[i].Z, 0.45);
        for (var i = 0; i < LegAt.Length; i++)
            MountPad(p, $"leg{i}", Cx + LegAt[i].X, At(LegAt[i].Z)[0].Y, LegAt[i].Z, 0.4, LegProud);
        // Landing lamps under the bow's shoulder, floodlights under engineering for the yoke.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 1.0 - 0.16, KeelY - 0.05, ZB + 0.25, Cx + s * 1.0 + 0.16, KeelY - 0.012, ZB + 0.55, Glow, glow: true);
            Detail(p, Cx + s * 0.9 - 0.2, KeelY - 0.05, -2.2, Cx + s * 0.9 + 0.2, KeelY - 0.012, -1.9, Glow, glow: true);
        }
        p.Set(("fuelTank", Kw(("capacity", KeelFuel)))); // (shared out over its bands, each with its Fuel out: SplitBands)
        return p.Stats(18000, 50000, 40, 25.0);
    }

    // ---- the roof: the top facets the head's length; the foredeck's bollards and vents over the cab, the strips either side of the
    // wheelhouse over engineering; the void over the ceilings

    /// <summary>A strip of the roof's facet (5) from v0 to v1 across it (0 the starboard edge, 1 the port edge) between za and zb.</summary>
    private static void RoofStrip(Part p, double za, double zb, double v0, double v1, string color)
    {
        double[] Q(double u, double v) { var q = OnFacet(5, 1, za, zb, u, v); return Sh([q[0], q[1], q[2] + FacetStagger + (u < 0.5 ? FacetTrim : -FacetTrim)]); } // (staggered as Facet staggers the odd facets)
        Quad(p, Q(0, v0), Q(0, v1), Q(1, v1), Q(1, v0), color, "painted plating");
    }

    private static bool UnderWheelhouse(double za, double zb) => za >= ZWh0 - 1e-6 && zb <= ZWh1 + 1e-6;

    private static double Top(double z) => At(z)[4].Y;

    private static Part Roof()
    {
        var p = Big("Docket Roof", "The Docket's roof: the foredeck over the cab with its bollards, vents and deck lockers, the strips either side of the wheelhouse, and the plating over the transom.", color: Skin);
        foreach (var (i, za, zb) in BandsIn(Zt, Stern))
        {
            if (UnderWheelhouse(za, zb))
            {
                // (the wheelhouse stands on engineering's ceiling: the roof here is a strip each side of it)
                var h4 = At(za)[4].H;
                var vin = (h4 - WhX - 0.004) / (2 * h4);
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
                Band(p, 4, s, za, zb, Skin, 4 + i);
                Chine(p, 4, s, za, zb, 0.16, 0.08, Armour);
            }
        }
        Ribs(p, Zt, Stern, 4);
        foreach (var z in RibZ.Where(z => Math.Abs(z - ZWh0) > 1e-6))
            Rib(p, 5, 1, z);
        // The foredeck: two pairs of bollards (yellow, a tug's), a deck locker, a pair of office-case vent boxes, a floodlight mast.
        foreach (var z in new[] { -14.6, -9.2 })
            foreach (var s in new[] { -1, 1 })
            {
                double bx = Cx + s * 2.15, by = Top(z);
                Solid(p, bx - 0.32, by - 0.1, z - 0.32, bx + 0.32, by + 0.085, z + 0.32, Frame, "dirty steel");
                foreach (var dz in new[] { -0.15, 0.15 })
                {
                    Prism(p, Ngon(bx, by + 0.085, z + dz, 0.11, 10), Ngon(bx, by + 0.48, z + dz, 0.11, 10), Hazard, "worn paint");
                    Prism(p, Ngon(bx, by + 0.48, z + dz, 0.15, 10), Ngon(bx, by + 0.54, z + dz, 0.15, 10), Hazard, "worn paint");
                }
            }
        {
            double z = -12.2, y = Top(z);
            Solid(p, Cx - 0.9, y - 0.1, z - 0.55, Cx + 0.9, y + 0.55, z + 0.55, Armour, "worn paint"); // (a deck locker)
            Detail(p, Cx - 0.92, y + 0.55, z - 0.57, Cx + 0.92, y + 0.6, z + 0.57, Frame, "dirty steel");
            Detail(p, Cx - 0.2, y + 0.3, z - 0.574, Cx + 0.2, y + 0.38, z - 0.555, Hazard, "hazard paint");
            foreach (var s in new[] { -1, 1 })
            {
                double vx = Cx + s * 1.7, vz = -10.4;
                var vy = Top(vz);
                Solid(p, vx - 0.4, vy - 0.1, vz - 0.6, vx + 0.4, vy + 0.4, vz + 0.6, Skin, "worn paint");
                for (var k = 0; k < 6; k++)
                    Detail(p, vx - 0.33, vy + 0.06 + k * 0.05, vz - 0.604, vx + 0.33, vy + 0.085 + k * 0.05, vz - 0.6, Vent, "plastic");
            }
            // A floodlight mast over the bow.
            double mz = -15.2, my = Top(mz);
            Solid(p, Cx - 0.25, my - 0.1, mz - 0.25, Cx + 0.25, my + 0.1, mz + 0.25, Frame, "dirty steel");
            Detail(p, Cx - 0.06, my + 0.1, mz - 0.06, Cx + 0.06, my + 1.4, mz + 0.06, Paper, "painted steel");
            Detail(p, Cx - 0.5, my + 1.4, mz - 0.1, Cx + 0.5, my + 1.5, mz + 0.1, Frame, "painted steel");
            foreach (var s in new[] { -1, 1 })
            {
                Detail(p, Cx + s * 0.4 - 0.12, my + 1.5, mz - 0.14, Cx + s * 0.4 + 0.12, my + 1.72, mz + 0.1, Dark, "painted steel");
                Detail(p, Cx + s * 0.4 - 0.1, my + 1.52, mz - 0.146, Cx + s * 0.4 + 0.1, my + 1.7, mz - 0.14, Glow, glow: true);
            }
        }
        // Radiators aft of the wheelhouse over the transom, two raked red fins (the Traders' "hot here").
        foreach (var s in new[] { -1, 1 })
        {
            var ry = Top(-0.8);
            for (var k = 0; k < 2; k++)
            {
                double z0 = -1.45 + k * 0.6, z1 = z0 + 0.45;
                Prism(p, [P(Cx + s * 0.5, ry - 0.01, z0), P(Cx + s * 0.58, ry - 0.01, z0), P(Cx + s * 0.58, ry - 0.01, z1), P(Cx + s * 0.5, ry - 0.01, z1)],
                    [P(Cx + s * 1.5, ry + 0.8, z0 + 0.15), P(Cx + s * 1.56, ry + 0.8, z0 + 0.15), P(Cx + s * 1.56, ry + 0.8, z1 + 0.15), P(Cx + s * 1.5, ry + 0.8, z1 + 0.15)], Latch, "worn paint");
                Detail(p, Cx + s * 1.5 - 0.04, ry + 0.8, z0 + 0.19, Cx + s * 1.56 + 0.04, ry + 0.83, z1 + 0.11, "#ff7040", glow: true);
            }
        }
        // The void over the ceilings (beside the wheelhouse, from its floor out).
        foreach (var (i, za, zb) in BandsIn(ZCab0, Stern))
            VoidT(p, za, zb, CRoof, Math.Min(Top(za), Top(zb)) - 0.03, UnderWheelhouse(za, zb) ? WhX + 0.02 : 0, 0.004, 0.004);
        return p.Stats(9000, 30000, 35, 15.0);
    }

    // ---- the push bow: the flat face sealing the bow, its rubber fenders, the drive-bay panels and the bow's attitude jets

    private static Part PushBow()
    {
        var p = Big("Docket Push Bow", "The Docket's push bow: the flat face of the tug's bow behind black rubber fenders, so it can shove a container, a wreck or another ship; its drive-bay panels, its beacon and the bow's attitude jets.", color: Skin);
        var q = At(Zt);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, Zt)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, Zt)))], Skin2, "painted plating");
        const double f = Zt - 0.025; // (in front of the face plate)
        // The fenders: a deep rubber knee across the face, two tall pads, a rubber cap along the top edge; yellow chevrons on the knee.
        Solid(p, Cx - 2.55, 0.25, Zt - 0.6, Cx + 2.55, 1.25, f, Rubber, "rubber matting");
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * 1.5 - 0.35, -0.25, Zt - 0.5, Cx + s * 1.5 + 0.35, 2.55, f, Rubber, "rubber matting");
            for (var k = 0; k < 4; k++)
                Detail(p, Cx + s * (0.25 + k * 0.28) - 0.07, 0.55, Zt - 0.606, Cx + s * (0.25 + k * 0.28) + 0.07, 0.95, Zt - 0.6, Hazard, "hazard paint");
            Detail(p, Cx + s * 1.5 - 0.36, 2.55, Zt - 0.51, Cx + s * 1.5 + 0.36, 2.6, f, Hazard, "hazard paint");
        }
        Solid(p, Cx - 2.0, 2.75, Zt - 0.3, Cx + 2.0, 3.0, f, Rubber, "rubber matting");
        // The drive-bay panels between the pads (an office case's front, the Traders' joke): three stacked bays with their slots and
        // buttons, a blue power lamp, a vent grille under them.
        for (var k = 0; k < 3; k++)
        {
            var y = 1.62 + k * 0.33;
            Detail(p, Cx - 0.8, y, Zt - 0.06, Cx + 0.8, y + 0.29, f, k == 1 ? Skin : Paper, "plastic");
            Detail(p, Cx - 0.6, y + 0.12, Zt - 0.066, Cx + 0.35, y + 0.17, Zt - 0.06, Vent, "plastic");
            Detail(p, Cx + 0.5, y + 0.1, Zt - 0.07, Cx + 0.62, y + 0.19, Zt - 0.06, Frame, "plastic");
        }
        double[][] Disc(double z) => [.. Enumerable.Range(0, 12).Select(i => P(Cx + 0.98 + 0.1 * Math.Cos(i * Math.PI / 6), 2.25 + 0.1 * Math.Sin(i * Math.PI / 6), z))];
        Prism(p, Disc(Zt - 0.03), Disc(Zt - 0.07), Link, "plastic", glow: true);
        for (var k = 0; k < 4; k++)
            Detail(p, Cx - 0.8, 1.32 + k * 0.065, Zt - 0.05, Cx + 0.8, 1.355 + k * 0.065, f, Vent, "plastic");
        // The beacon over the face, the registry plate on it.
        Detail(p, Cx - 0.09, 3.0, Zt - 0.2, Cx + 0.09, 3.14, Zt - 0.04, Red, glow: true);
        Detail(p, Cx - 2.0, 2.45, Zt - 0.05, Cx - 1.95, 2.6, f, Armour, "painted plating");
        // The fill behind the face to the cab's front wall.
        foreach (var (za, zb, ta, tb) in new[] { (Zt, ZB, 0.004, 0.0), (ZB, ZCab0, 0.0, 0.0) })
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, Math.Min(Top(za), Top(zb)) - 0.03) })
                VoidT(p, za, zb, ya, yb, 0, ta, tb);
        // The bow's attitude jets: up, down and out.
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            RcsPad(p, "rcsUp" + n, Cx + s * 1.2, Top(BowRcsZ), BowRcsZ, "+y", 0.056);
            RcsPad(p, "rcsDown" + n, Cx + s * 0.9, At(BowRcsZ)[0].Y, BowRcsZ, "-y", 0.05);
            RcsPad(p, "rcsOut" + n, FacetX(2, s, BowRcsZ, 0.8), 0.8, BowRcsZ, s < 0 ? "-x" : "+x", 0.28);
        }
        return p.Stats(7000, 40000, 45, 12.0);
    }

    // ---- the cab: the crew's rooms off a short corridor; its hull bands

    private static Part Cab()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Docket Cab", "The Docket's cab: the crew's rooms off a short corridor, the bunk room and the head to port, the galley and the airlock over the boarding shaft to starboard.");
        Rect cab = new(Cx - Lw - 0.25, ZCab0, Cx + Lw + 0.25, ZCabW - 0.125);
        Floor(p, [cab], 0, Ft, Deck, "painted deck plate", BellyHatch);
        Floor(p, [new Rect(cab.X0, ZFw + 0.125, cab.X1, ZCabW - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Room(p, cab, 0, CRoof);
        Ceils[p] = CCeil;
        // The front wall, the cab's sides, the corridor's walls with the rooms' doors, the rooms' cross walls, the aft wall (the whole
        // width, into engineering).
        WallRun(p, true, cab.X0, cab.X1, ZFw, Ft, CCeil);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZFw + 0.125, ZCabW - 0.125, Cx + s * (Lw + 0.125), Ft, CCeil);
        WallRun(p, false, ZFw + 0.125, ZCabW - 0.125, Cx - 0.875, Ft, CCeil, Door(-13.9), Door(-10.625));
        WallRun(p, false, ZFw + 0.125, ZCabW - 0.125, Cx + 0.875, Ft, CCeil, Door(-14.65), Door(-9.1));
        WallRun(p, true, Cx - Lw, Cx - 1.0, ZPortW, Ft, CCeil);
        WallRun(p, true, Cx + 1.0, Cx + Lw, ZStbdW, Ft, CCeil);
        WallRun(p, true, Cx - PassOut - SLine, Cx + PassOut + SLine, ZCabW, Ft, CCeil, Door(Cx - 0.75));
        // The airlock: a rail round the boarding shaft, the cycle lights over its door.
        HatchRail(p, BellyHatch, Ft);
        Detail(p, Cx + 1.0, Ft + DoorH + 0.15, -8.4, Cx + 1.015, Ft + DoorH + 0.3, -8.1, Red, glow: true);
        Detail(p, Cx + 0.735, Ft + DoorH + 0.15, -8.4, Cx + 0.75, Ft + DoorH + 0.3, -8.1, "#40e070", glow: true);
        // The corridor: a grating runner, a light strip.
        Detail(p, Cx - 0.6, Ft, ZFw + 0.3, Cx + 0.6, Ft + 0.012, ZCabW - 0.3, Grate, "painted grating");
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZFw + 0.6, ZCabW - 0.6, CCeil);
        // The hull: the bow's bands, then the cab's with the livery, the portholes and the office-case vents.
        foreach (var s in new[] { -1, 1 })
        {
            foreach (var (_, za, zb) in BandsIn(Zt, ZCabEnd))
            {
                var bow = zb <= ZCab0 + 1e-6;
                Band(p, 2, s, za, zb, Skin, 3, plates: !bow);
                Band(p, 3, s, za, zb, Skin, 5, plates: false);
                if (!bow)
                    Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.24, 0.1, Armour);
                Chine(p, 3, s, za, zb, 0.18, 0.08, Frame);
            }
            // Portholes in the upper sides: the bunk room and the galley, the head and the airlock.
            foreach (var z in new[] { -14.0, -12.4, -9.9, -8.4 })
            {
                var (za, zb) = z < ZCabM ? (ZCab0, ZCabM) : (ZCabM, ZCabEnd);
                double len = zb - za, u = (z - za) / len, du = 0.32 / len;
                Slab(p, 3, s, za, zb, u - du - 0.06 / len, u + du + 0.06 / len, 0.38, 0.72, 0.02, 0.06, 0.02, Dark, "painted steel");
                FStrip(p, 3, s, za, zb, u - du, u + du, 0.43, 0.67, 0.062, "#24384a", "glass");
            }
            // The registry on the cab's flank: a white plate with the Traders' blue square.
            Slab(p, 3, s, ZCabM, ZCabEnd, 0.62, 0.92, 0.35, 0.8, 0.02, 0.05, 0.02, Paper, "painted plating");
            Slab(p, 3, s, ZCabM, ZCabEnd, 0.65, 0.7, 0.42, 0.73, 0.05, 0.07, 0.01, Armour, "painted plating");
        }
        Ribs(p, Zt, ZCabEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZCab0, ZCabEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, CRoof) })
                VoidT(p, za, zb, ya, yb, Lw + 0.25, 0.004, zb == ZCabEnd ? 0.25 : 0.004);
        // Doors.
        DoorSock(p, "doorAft", true, Cx - 0.75, ZCabW, Ft);
        DoorSock(p, "doorBunk", false, -13.9, Cx - 0.875, Ft);
        DoorSock(p, "doorHead", false, -10.625, Cx - 0.875, Ft);
        DoorSock(p, "doorGalley", false, -14.65, Cx + 0.875, Ft);
        DoorSock(p, "doorAirlock", false, -9.1, Cx + 0.875, Ft);
        // The corridor's end: a service locker on the front wall.
        Sock(p, "lockerHall", Cx, Ft + 0.125, ZFw + 0.125, "+z");
        // The bunk room: two bunks along the outer wall, a locker by the door.
        Sock(p, "bunk0", Cx - Lw + 0.55, Ft, -14.2, "+y");
        Sock(p, "bunk1", Cx - Lw + 0.55, Ft, -12.075, "+y");
        Sock(p, "lockerBunk", Cx - 1.5, Ft + 0.125, ZPortW - 0.125, "-z");
        Sock(p, "lampBunk", Cx - Lw, Ft + 2.2, -13.15, "+x");
        Sock(p, "switchBunk", Cx - 1.0, Ft + 1.3, -14.05, "-x");
        // The head: the toilet and the basin on the outer wall, the shower in the aft corner.
        Sock(p, "toilet", Cx - Lw, Ft + 0.125, -10.3, "+x");
        Sock(p, "basin", Cx - Lw, Ft + 0.125, -9.45, "+x");
        Sock(p, "shower", Cx - 2.7, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "lampHead", Cx - Lw, Ft + 2.3, -8.9, "+x");
        Sock(p, "switchHead", Cx - 1.0, Ft + 1.3, -8.95, "-x");
        // The galley: the range and the coffee station on the forward wall, the mess table along the outer wall.
        Sock(p, "range", Cx + 2.5, Ft + 0.45, ZFw + 0.125, "+z");
        Sock(p, "coffee", Cx + 1.55, Ft + 0.125, ZFw + 0.125, "+z");
        Sock(p, "table", Cx + 2.6, Ft, -13.25, "+y");
        Sock(p, "lampGalley", Cx + Lw, Ft + 2.2, -13.85, "-x");
        Sock(p, "switchGalley", Cx + 1.0, Ft + 1.3, -14.8, "+x");
        // The airlock: suit lockers on the outer wall, the ship's air on the aft wall, the hatch's switch, the belly hatch's hanging.
        Sock(p, "lifeSupport", Cx + 2.85, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "o2Tank0", Cx + 2.0, Ft + 0.125, ZCabW - 0.125, "-z");
        Sock(p, "suit0", Cx + Lw, Ft + 0.125, -10.8, "-x");
        Sock(p, "suit1", Cx + Lw, Ft + 0.125, -8.4, "-x");
        Sock(p, "switchHatch", Cx + 1.0, Ft + 1.45, -9.22, "+x");
        Sock(p, "lampAirlock", Cx + Lw, Ft + 2.3, -9.7, "-x");
        Sock(p, "boarding", HatchX, Ft - 0.125, BellyHatch.Z1, "-z");
        // The corridor's lamps; the power board and engineering's switch on the aft wall's back.
        Sock(p, "lampHallA", Cx - 0.75, Ft + 2.3, -12.6, "+x");
        Sock(p, "lampHallB", Cx + 0.75, Ft + 2.3, -10.0, "-x");
        Sock(p, "board", Cx - 1.8, Ft + 0.9, ZCabW + 0.125, "+z");
        Sock(p, "switchEng", Cx + 0.9, Ft + 1.3, ZCabW + 0.125, "+z");
        foreach (var (what, z, hx) in new[] { ("cab front", ZCab0, Lw + 0.25), ("cab middle", ZCabM, Lw + 0.25), ("cab's aft wall", ZCabEnd, PassOut + SLine) })
            foreach (var y in new[] { 0.0, CRoof })
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


    // ---- engineering: the plant, the batteries, the Engineer's Panel, the space-walk gear for the nacelles, the ladder up to the wheelhouse

    private static Part Engineering()
    {
        WallCol = Wall;
        DadoCol = WallLow;
        var p = Big("Docket Engineering", "The Docket's engineering room behind the cab: the power plant and the batteries, the Engineer's Panel, an MMU dock, the fuel manifolds to the four nacelles' pylons, a suit locker for the space walk out to them, and the ladder up to the wheelhouse.");
        var plan = new Rect(Cx - PassOut, ZEng, Cx + PassOut, ZEngEnd);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate");
        Floor(p, [new Rect(plan.X0, ZEng, plan.X1, ZEngAW - 0.125)], ECeil, ERoof, Ceil, "painted plating", WhHole);
        Room(p, new Rect(plan.X0 + 0.2, ZEng, plan.X1 - 0.2, ZEngEnd), 0, ERoof);
        Ceils[p] = ECeil;
        Detail(p, Cx - 0.75, Ft, ZEng + 0.3, Cx + 0.75, Ft + 0.012, WhHole.Z0 - 0.4, Grate, "painted grating");
        foreach (var x in new[] { -0.79, 0.75 })
            Detail(p, Cx + x, Ft + 0.004, ZEng + 0.3, Cx + x + 0.04, Ft + 0.016, WhHole.Z0 - 0.4, Hazard, "hazard paint");
        // The ladder's foot: a hazard square round it on the deck, the opening's lining in the ceiling.
        Detail(p, WhHole.X0 - 0.1, Ft, WhHole.Z0 - 0.1, WhHole.X1 + 0.1, Ft + 0.008, WhHole.Z1 + 0.1, Hazard, "hazard paint");
        foreach (var s in new[] { -1, 1 })
        {
            Lining(p, s, ZEng, ZEngM, ERoof, ECeil, true, false);
            Lining(p, s, ZEngM, ZEngAW - 0.125, ERoof, ECeil, false, true);
            var face = Cx + s * PassOut;
            // The fuel manifold: a feed up the lining to the pylons' pass-throughs, a valve wheel and a gauge on it.
            foreach (var z in new[] { PylonZ - 0.6, PylonZ + 0.6 })
                Detail(p, face - s * 0.12, Ft + 0.004, z - 0.05, face - s * 0.02, ECeil - 0.004, z + 0.05, z < PylonZ ? Red : Bare, "painted steel");
            Detail(p, face - s * 0.16, 1.1, PylonZ - 0.75, face - s * 0.12, 1.4, PylonZ - 0.45, Red, "painted steel");
            Detail(p, face - s * 0.2, 1.75, PylonZ - 0.1, face - s * 0.004, 2.25, PylonZ + 0.3, Frame, "dirty steel");
            Detail(p, face - s * 0.205, 1.85, PylonZ, face - s * 0.2, 2.15, PylonZ + 0.2, Paper, "plastic");
            Detail(p, face - s * 0.14, ECeil - 0.3, PylonZ - 0.7, face - s * 0.004, ECeil - 0.18, PylonZ + 0.7, Hazard, "hazard paint");
        }
        WallRun(p, true, plan.X0 - SLine, plan.X1 + SLine, ZEngAW, Ft, ECeil);
        CeilStrip(p, Cx - 0.12, Cx + 0.12, ZEng + 0.6, WhHole.Z0 - 0.3, ECeil);
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZEng, ZEngEnd))
            {
                Band(p, 2, s, za, zb, Skin, 21 + i, plates: za >= ZEngM - 1e-6);
                Band(p, 3, s, za, zb, Skin, 22 + i, plates: false);
                if (zb - za > 1.5)
                    Livery(p, s, za, zb);
                Chine(p, 2, s, za, zb, 0.24, 0.1, Armour);
                Chine(p, 3, s, za, zb, 0.18, 0.08, Frame);
                if (za < ZEngM - 1e-6)
                {
                    // The plant's vents on the lower flank: an office case's side grille, a dark panel with rows of slots.
                    var len = zb - za;
                    Slab(p, 2, s, za, zb, 0.08, 0.92, 0.3, 0.9, 0.02, 0.06, 0.02, Vent, "plastic");
                    for (var k = 0; k < 7; k++)
                        FStrip(p, 2, s, za, zb, 0.12, 0.88, 0.34 + k * 0.075, 0.36 + k * 0.075, 0.064, Dark, "plastic");
                    _ = len;
                }
                else if (zb - za > 1.5)
                {
                    // The Traders' mark on engineering's flank: a white letterhead panel with a blue square on it.
                    Slab(p, 3, s, za, zb, 0.25, 0.75, 0.35, 0.8, 0.02, 0.05, 0.02, Paper, "painted plating");
                    Slab(p, 3, s, za, zb, 0.28, 0.36, 0.42, 0.73, 0.05, 0.07, 0.01, Armour, "painted plating");
                }
            }
        Ribs(p, ZEng, ZEngEnd, 2, 3);
        // The nacelles' pylons' hardpoints on the flanks: an upper and a lower each side.
        foreach (var s in new[] { -1, 1 })
            for (var u = 0; u < 2; u++)
            {
                Solid(p, Cx + s * (PassOut + SLine + 0.004), NacYs[u] - 0.32, PylonZ - 0.6, Cx + s * PylonX, NacYs[u] + 0.32, PylonZ + 0.6, Frame, "dirty steel");
                Sock(p, NacSock(s, u), Cx + s * PylonX, NacYs[u], PylonZ, s < 0 ? "-x" : "+x");
            }
        foreach (var (_, za, zb) in BandsIn(ZEng, ZEngEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, ERoof) })
                VoidT(p, za, zb, ya, yb, PassOut + SLine + 0.004, 0.0, 0.0); // (end to end: the short band by the transom welds to its neighbour)
        Sock(p, "panel", Cx - PassOut, Ft + 0.6, -2.6, "+x");
        Sock(p, "tools", Cx - PassOut, Ft + 0.9, -6.4, "+x");
        Sock(p, "plant", Cx - 2.3, Ft, -4.6, "+y");
        for (var i = 0; i < 3; i++)
            Sock(p, $"battery{i}", Cx + PassOut - 0.3, Ft, -5.9 + i * 0.7, "+y");
        Sock(p, "suit", Cx + 2.6, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "locker", Cx + 1.8, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "mmuDock", Cx - 1.9, Ft + 0.125, ZEngAW - 0.125, "-z");
        Sock(p, "lampEngL", Cx - PassOut, Ft + 2.3, -5.0, "+x");
        Sock(p, "lampEngR", Cx + PassOut, Ft + 2.3, -3.0, "-x");
        Sock(p, "ladderWh", Cx, Ft, WhLadderZ - 0.1, "+y");
        foreach (var z in new[] { plan.Z0, ZEngM, plan.Z1 })
            foreach (var y in new[] { 0.0, ERoof })
                MustFit("engineering", z, PassOut + SLine, y);
        return p.Stats(14000, 30000, 30, 15.0);
    }

    private static string NacSock(int s, int u) => "nac" + (u == 0 ? "U" : "D") + (s < 0 ? "L" : "R");

    // ---- the wheelhouse: a glass box over engineering, raked windows forward, glass all round so the pilot watches the tow

    private static Part Wheelhouse()
    {
        var p = Big("Docket Wheelhouse", "The Docket's wheelhouse: a glass box standing on engineering's ceiling, its windows raked out forward and glass all round so the pilot sees the bow and the string; the flight couches, the dash and the flight computer, a ladder up from engineering, the comms mast, searchlights and the beacon on its roof.", color: Skin);
        double z0 = ZWh0, z1 = ZWh1, x = WhX;
        // The floor (on engineering's ceiling, round the ladder's opening) and its blue skirt over the roof.
        foreach (var r in Minus([new Rect(Cx - x, z0, Cx + x, z1)], WhHole))
            Solid(p, r.X0, CRoof, r.Z0, r.X1, WFl, r.Z1, Deck, "painted deck plate");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * x, 3.335, z0 + 0.004, Cx + s * (x + 0.012), WFl - 0.004, z1 - 0.004, Armour, "painted plating");
        Detail(p, Cx - x + 0.004, 3.335, z0 - 0.012, Cx + x - 0.004, WFl - 0.004, z0, Armour, "painted plating");
        Detail(p, Cx - x + 0.004, 3.335, z1, Cx + x - 0.004, WFl - 0.004, z1 + 0.012, Armour, "painted plating");
        Detail(p, Cx - x + 0.2, WFl, z0 + 0.55, Cx + x - 0.2, WFl + 0.012, WhHole.Z0 - 0.2, "#30353b", "rubber matting");
        HatchRail(p, WhHole, WFl);
        // The sills all round (beige outside, a blue dado inside), corner posts and mid posts.
        Solid(p, Cx - x, WFl, z0, Cx + x, WSill, z0 + 0.12, Skin, "painted plating");
        Solid(p, Cx - x, WFl, z1 - 0.12, Cx + x, WSill, z1, Skin, "painted plating");
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * (x - 0.12), WFl, z0 + 0.12, Cx + s * x, WSill, z1 - 0.12, Skin, "painted plating");
            Detail(p, Cx + s * (x - 0.132), WFl + 0.004, z0 + 0.2, Cx + s * (x - 0.12), WFl + 0.7, z1 - 0.2, WallLow, "painted plating");
            foreach (var zc in new[] { z0 + 0.08, z1 - 0.08 })
                Solid(p, Cx + s * (x - 0.16), WSill, zc - 0.08, Cx + s * x, WHead, zc + 0.08, Frame, "painted steel");
            foreach (var zc in new[] { -5.3, -3.5 })
                Solid(p, Cx + s * (x - 0.1), WSill, zc - 0.06, Cx + s * x, WHead, zc + 0.06, Frame, "painted steel");
        }
        Solid(p, Cx - 0.06, WSill, z1 - 0.1, Cx + 0.06, WHead, z1, Frame, "painted steel");
        // The glass: the front raked out from the sill to the head, the sides and the back upright, dark mullions on the front;
        // a cheek plate in each front corner between the raked front and the upright side; hidden boxes behind every pane.
        double fz0 = z0 + 0.06, fz1 = z0 - WRake;
        var xs = new[] { -x, -1.2, 0.0, 1.2, x };
        for (var k = 0; k < 4; k++)
        {
            double xa = Cx + xs[k] + (k == 0 ? 0.004 : 0.03), xb = Cx + xs[k + 1] - (k == 3 ? 0.004 : 0.03);
            Plate(p, [P(xa, WSill, fz0), P(xb, WSill, fz0), P(xb, WHead, fz1), P(xa, WHead, fz1)], Glass, glass: true);
        }
        foreach (var mx in new[] { -1.2, 0.0, 1.2 })
            Prism(p, [P(Cx + mx - 0.035, WSill, fz0 + 0.03), P(Cx + mx + 0.035, WSill, fz0 + 0.03), P(Cx + mx + 0.035, WSill, fz0 - 0.03), P(Cx + mx - 0.035, WSill, fz0 - 0.03)],
                [P(Cx + mx - 0.035, WHead, fz1 + 0.03), P(Cx + mx + 0.035, WHead, fz1 + 0.03), P(Cx + mx + 0.035, WHead, fz1 - 0.03), P(Cx + mx - 0.035, WHead, fz1 - 0.03)], Dark, "painted steel");
        Hidden(p, Cx - x, WSill, z0, Cx + x, WHead, z0 + 0.1);
        foreach (var s in new[] { -1, 1 })
        {
            Plate(p, [P(Cx + s * (x + 0.006), WSill, fz0 - 0.004), P(Cx + s * (x + 0.006), WHead, fz1), P(Cx + s * (x + 0.006), WHead, z0 + 0.165), P(Cx + s * (x + 0.006), WSill, z0 + 0.165)], Skin, "painted plating");
            foreach (var (za, zb) in new[] { (z0 + 0.16, -5.36), (-5.24, -3.56), (-3.44, z1 - 0.16) })
                Plate(p, [P(Cx + s * (x - 0.05), WSill, za + 0.004), P(Cx + s * (x - 0.05), WSill, zb - 0.004), P(Cx + s * (x - 0.05), WHead, zb - 0.004), P(Cx + s * (x - 0.05), WHead, za + 0.004)], Glass, glass: true);
            Hidden(p, Cx + s * (x - 0.1), WSill, z0 + 0.16, Cx + s * (x - 0.01), WHead, z1 - 0.16);
            Plate(p, [P(Cx + s * 0.06, WSill, z1 - 0.05), P(Cx + s * (x - 0.16), WSill, z1 - 0.05), P(Cx + s * (x - 0.16), WHead, z1 - 0.05), P(Cx + s * 0.06, WHead, z1 - 0.05)], Glass, glass: true);
        }
        Hidden(p, Cx - x + 0.16, WSill, z1 - 0.1, Cx + x - 0.16, WHead, z1 - 0.01);
        // The roof: a slab with a visor out over the raked glass, a blue band round its edge, a light strip under it.
        double rt = WHead + 0.16;
        Solid(p, Cx - x - 0.2, WHead, fz1 - 0.3, Cx + x + 0.2, rt, z1 + 0.12, Skin2, "painted plating");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * (x + 0.2), WHead + 0.02, fz1 - 0.28, Cx + s * (x + 0.212), rt - 0.02, z1 + 0.1, Armour, "painted plating");
        Detail(p, Cx - x - 0.18, WHead + 0.02, fz1 - 0.312, Cx + x + 0.18, rt - 0.02, fz1 - 0.3, Armour, "painted plating");
        Detail(p, Cx - 0.12, WHead - 0.025, z0 + 0.4, Cx + 0.12, WHead, z1 - 0.4, "#d8f0ff", glow: true);
        // The comms mast (the Traders' dishes and masts): a lattice tower, a dish on a yoke, whip antennas, a beacon.
        double mz = -2.8;
        Solid(p, Cx - 0.5, rt, mz - 0.5, Cx + 0.5, rt + 0.15, mz + 0.5, Frame, "dirty steel");
        foreach (var (dx, dz) in new[] { (-0.3, -0.3), (0.3, -0.3), (-0.3, 0.3), (0.3, 0.3) })
            Detail(p, Cx + dx - 0.035, rt + 0.15, mz + dz - 0.035, Cx + dx + 0.035, rt + 2.2, mz + dz + 0.035, Paper, "painted steel");
        foreach (var yy in new[] { 0.7, 1.3, 1.85, 2.15 })
            Detail(p, Cx - 0.33, rt + yy, mz - 0.33, Cx + 0.33, rt + yy + 0.05, mz + 0.33, Red, "painted steel");
        Prism(p, Ngon(Cx + 0.9, rt + 1.5, mz, 0.1, 10), Ngon(Cx + 0.9, rt + 1.85, mz, 0.7, 10), Paper, "painted steel");
        Detail(p, Cx + 0.33, rt + 1.45, mz - 0.05, Cx + 0.9, rt + 1.55, mz + 0.05, Frame, "painted steel");
        Detail(p, Cx + 0.86, rt + 1.6, mz - 0.03, Cx + 0.94, rt + 2.3, mz + 0.03, Frame, "painted steel");
        Detail(p, Cx - 0.06, rt + 2.2, mz - 0.06, Cx + 0.06, rt + 2.35, mz + 0.06, Red, glow: true);
        foreach (var (dx, dz, h) in new[] { (-1.6, -2.2, 1.5), (-1.9, -4.0, 1.1), (1.7, -4.6, 1.8) })
        {
            Detail(p, Cx + dx - 0.12, rt, dz - 0.12, Cx + dx + 0.12, rt + 0.124, dz + 0.12, Frame, "dirty steel");
            Detail(p, Cx + dx - 0.02, rt + 0.124, dz - 0.02, Cx + dx + 0.02, rt + h, dz + 0.02, Dark, "painted steel");
            Detail(p, Cx + dx - 0.04, rt + h, dz - 0.04, Cx + dx + 0.04, rt + h + 0.08, dz + 0.04, Link, glow: true);
        }
        // Twin searchlights on the visor's corners, a horn, and the nav lights on the roof's corners: red to port, green to starboard.
        foreach (var s in new[] { -1, 1 })
        {
            double sx = Cx + s * (x - 0.3), sz = fz1 + 0.1;
            Detail(p, sx - 0.15, rt, sz - 0.15, sx + 0.15, rt + 0.12, sz + 0.15, Frame, "dirty steel");
            Prism(p, RingZf(sx, rt + 0.34, 0.2, sz - 0.25), RingZf(sx, rt + 0.34, 0.2, sz + 0.2), Dark, "painted steel");
            Plate(p, RingZf(sx, rt + 0.34, 0.16, sz - 0.26), Glow, glow: true);
            Detail(p, sx - 0.05, rt + 0.12, sz - 0.05, sx + 0.05, rt + 0.16, sz + 0.05, Frame, "painted steel");
            Detail(p, Cx + s * (x + 0.05) - 0.08, rt, z0 - 0.1, Cx + s * (x + 0.05) + 0.08, rt + 0.1, z0 + 0.1, s < 0 ? Red : Green, glow: true);
        }
        Prism(p, RingZf(Cx - 0.9, rt + 0.3, 0.06, -1.9), RingZf(Cx - 0.9, rt + 0.3, 0.18, -2.4), Paper, "painted steel");
        Detail(p, Cx - 0.95, rt, -1.95, Cx - 0.85, rt + 0.3, -1.85, Frame, "painted steel");
        // The dash under the front glass: a console the width of the room, its screens and switches.
        Solid(p, Cx - x + 0.16, WFl, z0 + 0.12, Cx + x - 0.16, WFl + 0.8, z0 + 0.5, Dark, "plastic");
        foreach (var (x0, x1, col) in new[] { (-1.7, -0.9, "#78c8e8"), (-0.6, 0.6, "#60e090"), (0.9, 1.7, "#78c8e8") })
            Detail(p, Cx + x0, WFl + 0.8, z0 + 0.15, Cx + x1, WFl + 0.812, z0 + 0.45, col, glow: true);
        for (var i = 0; i < 9; i++)
            Detail(p, Cx - 0.8 + i * 0.2, WFl + 0.45, z0 + 0.5, Cx - 0.72 + i * 0.2, WFl + 0.53, z0 + 0.513, i % 3 == 0 ? Hazard : Paper, "plastic");
        // The couches and computer, the lamps on the aft posts, the switch on the aft sill.
        Sock(p, "pilot", Cx - 0.8, WFl, -5.4, "+y");
        Sock(p, "copilot", Cx + 0.8, WFl, -5.4, "+y");
        Sock(p, "computer", Cx, WFl, -6.3, "+y");
        Sock(p, "lampWhL", Cx - x + 0.1, WFl + 2.0, -3.5, "+x");
        Sock(p, "lampWhR", Cx + x - 0.1, WFl + 2.0, -3.5, "-x");
        Sock(p, "switchWh", Cx + 1.2, WFl + 0.7, z1 - 0.12, "-z");
        return p.Stats(6000, 15000, 25, 10.0);
    }

    /// <summary>A ring of 12 points round an axis along z at (cx, cy), radius r, at z, in the part's frame (P applied).</summary>
    private static double[][] RingZf(double cx, double cy, double r, double z) =>
        [.. Enumerable.Range(0, 12).Select(i => P(cx + r * Math.Cos(i * Math.PI / 6), cy + r * Math.Sin(i * Math.PI / 6), z))];

    // ---- the stern: the head's flanks closing to the transom, the yoke's seat on it

    private static Part SternPart()
    {
        var p = Big("Docket Stern", "The Docket's stern: the head's flanks tucking in to the transom, with its tail lights and the seat the tow yoke is pinned to.", color: Skin);
        foreach (var s in new[] { -1, 1 })
        {
            for (var k = 2; k <= 3; k++)
            {
                Facet(p, k, s, ZEngEnd, Stern, k % 2 == 0 ? Skin : Skin2, "painted plating");
                Slab(p, k, s, ZEngEnd, Stern, 0.3, 0.9, 0.1, 0.9, 0.02, k == 2 ? 0.12 : 0.08, 0.04, k == 3 ? Armour : Skin, "painted plating");
            }
            Chine(p, 2, s, ZEngEnd, Stern, 0.24, 0.1, Armour);
            Chine(p, 3, s, ZEngEnd, Stern, 0.18, 0.08, Frame);
        }
        Ribs(p, ZEngEnd, Stern, 2, 3);
        // The transom: a plate, tail lights, hazard bands; the yoke's seat, a solid block standing just proud of it.
        var q = At(Stern);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, Stern)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, Stern)))], Skin2, "painted plating");
        Solid(p, Cx - 1.2, 0.0, Stern - 0.3, Cx + 1.2, 1.0, YokeZ0, Frame, "dirty steel");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 2.6 - 0.15, 1.3, Stern + 0.025, Cx + s * 2.6 + 0.15, 1.7, Stern + 0.06, Red, glow: true);
            Detail(p, Cx + s * 2.0 - 0.45, 2.5, Stern + 0.025, Cx + s * 2.0 + 0.45, 2.58, Stern + 0.05, Hazard, "hazard paint");
            Detail(p, Cx + s * 1.25, 0.05, Stern + 0.025, Cx + s * 1.5, 0.95, Stern + 0.05, Hazard, "hazard paint");
        }
        Detail(p, Cx - 0.3, 2.0, Stern + 0.025, Cx + 0.3, 2.2, Stern + 0.07, Glow, glow: true);
        foreach (var (ya, yb) in new[] { (0.0, 1.5), (1.5, ERoof) })
            VoidT(p, ZEngEnd, Stern, ya, yb, 0, 0.0, 0.03);
        return p.Stats(5000, 20000, 35, 10.0);
    }

    private const double YokeZ0 = Stern + 0.03;

    // ---- the nacelles: a fuel tank forward, the engine housing aft (the drive outside its tail plate, its engine inside), a pylon;
    // stacked two a side on an outrigger

    private static string NacName(int s, int u) => $"Docket Nacelle {(u == 0 ? "Upper" : "Lower")} {(s < 0 ? "L" : "R")}";

    private static Part Nacelle(int s, int u)
    {
        var name = NacName(s, u);
        var p = new Part(name, Small, Section, $"One of the Docket's four drive nacelles, the {(u == 0 ? "upper" : "lower")} {(s < 0 ? "to port" : "to starboard")}: a fuel tank forward of an engine housing, on a pylon off engineering's flank, its twin stacked {(u == 0 ? "under" : "over")} it. The drive bolts to the housing's tail plate outside, its Drive Core and Fuel Pump inside behind it, mended through the service hatch in its outboard flank on a space walk.", role: "fueltank", tool: "welder", material: "steel", color: Skin, group: Group);
        double cx = Cx + s * NacX, cy = NacYs[u], f = NacF, c = EngineHousing.HalfFlat(NacF);
        double t0 = NacTank0, t1 = NacTank1;
        // The nose: a red tip (the Traders' "hot here"), a long cone; the tank: cream plates in bands with blue between, collars over the seams.
        EngineHousing.Ring(p, P, cx, cy, 0.3, 0.12, NacNose, 0.6, 0.25, NacNose + 0.4, Latch, "painted plating");
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
        Detail(p, cx + s * (f + 0.004), cy + 0.1, t0 + 0.9, cx + s * (f + 0.016), cy + 0.5, t0 + 1.6, Paper, "painted plating");
        // Running lights at the nose (red to port, green to starboard) and a white one at the tail.
        Detail(p, cx + s * 0.62 - 0.06, cy - 0.06, NacNose + 0.6, cx + s * 0.62 + 0.06, cy + 0.06, NacNose + 0.75, s < 0 ? Red : Green, glow: true);
        if (u == 0)
        {
            Detail(p, cx - 0.06, cy + f + 0.004, NacTail - 0.4, cx + 0.06, cy + f + 0.08, NacTail - 0.25, Glow, glow: true);
            // Radiator fins on the upper nacelle's back, in signal red (the mood board's "hot" part), glowing along their tops.
            for (var k = 0; k < 4; k++)
            {
                var zf = t0 + 1.0 + k * 1.2;
                Detail(p, cx - 0.04, cy + f + 0.04, zf, cx + 0.04, cy + f + 0.62, zf + 0.85, Latch, "worn paint");
                Detail(p, cx - 0.05, cy + f + 0.62, zf + 0.05, cx + 0.05, cy + f + 0.65, zf + 0.8, "#ff7040", glow: true);
                Detail(p, cx - 0.12, cy + f - 0.02, zf - 0.04, cx + 0.12, cy + f + 0.04, zf + 0.89, Frame, "dirty steel");
            }
            // The outrigger: a blue strut down to the lower nacelle, hazard bands at both ends.
            double ya = NacYs[1] + NacF - 0.06, yb = cy - NacF + 0.06;
            Detail(p, cx - 0.14, ya, PylonZ - 1.2, cx + 0.14, yb, PylonZ + 1.2, Armour, "painted plating");
            foreach (var yy in new[] { ya + 0.004, yb - 0.084 })
                Detail(p, cx - 0.146, yy, PylonZ - 1.21, cx + 0.146, yy + 0.08, PylonZ + 1.21, Hazard, "hazard paint");
        }
        // The pylon: a solid spar from the hardpoint into the tank, under a swept fairing; the fuel feed along it.
        Solid(p, Cx + s * PylonX, cy - 0.2, PylonZ - 2.4, cx - s * (f - 0.03), cy + 0.2, PylonZ + 2.4, Frame, "dirty steel");
        Prism(p, [P(Cx + s * (PylonX + 0.004), cy - 0.32, PylonZ - 3.0), P(Cx + s * (PylonX + 0.004), cy + 0.32, PylonZ - 3.0), P(Cx + s * (PylonX + 0.004), cy + 0.32, PylonZ + 3.0), P(Cx + s * (PylonX + 0.004), cy - 0.32, PylonZ + 3.0)],
            [P(cx - s * (f + 0.004), cy - 0.24, PylonZ - 2.6), P(cx - s * (f + 0.004), cy + 0.24, PylonZ - 2.6), P(cx - s * (f + 0.004), cy + 0.24, PylonZ + 2.8), P(cx - s * (f + 0.004), cy - 0.24, PylonZ + 2.8)], Armour, "painted plating");
        Detail(p, Cx + s * (PylonX + 0.2), cy + 0.27, PylonZ - 2.5, cx - s * (f + 0.1), cy + 0.33, PylonZ - 2.0, Hazard, "hazard paint");
        Hidden(p, cx - (f - 0.38), cy - (f - 0.38), t0, cx + (f - 0.38), cy + (f - 0.38), t1 - 0.004);
        Sock(p, "hull", Cx + s * PylonX, cy, PylonZ, s < 0 ? "+x" : "-x");
        Works.Mount(p, outside: true); // (the housing's tail plate: the drive bolts to it outside, its engine inside)
        RcsPad(p, "rcsFore", cx, cy, NacNose + 0.06, "-z", 0.05);
        // A pitch jet on the outer flat of each pair, far aft: up on the upper nacelle, down on the lower.
        if (u == 0)
            RcsPad(p, "rcsUp", cx, cy + f, t1 - 0.3, "+y", 0.05);
        else
            RcsPad(p, "rcsDown", cx, cy - f, t1 - 1.4, "-y", 0.05);
        p.Set(("fuelTank", Kw(("capacity", NacFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [.. P(cx - s * (f - 0.38), cy, t1 - 2.0).Cast<object>()], s < 0 ? "+x" : "-x", "fuel"))));
        return p.Stats(3800, 7000, 20, 10.0);
    }

    // ---- the string: the yoke, the cells of open truss with a container on each, the end car

    private static Part Yoke()
    {
        var p = Big("Docket Tow Yoke", "The Docket's tow yoke: the heavy coupling pinned to the transom that grips the string's first cell, two red hydraulic jaws on a king pin. Shot off, the string drifts free.", color: Frame);
        Solid(p, Cx - 1.0, 0.0, YokeZ0, Cx + 1.0, TT, YokeZ1, Frame, "dirty steel");
        // The king pin, the jaws over it, their rams, the hoses; hazard chevrons on the yoke's sides.
        Prism(p, Ngon(Cx, TT, (YokeZ0 + YokeZ1) / 2, 0.22, 10), Ngon(Cx, TT + 0.35, (YokeZ0 + YokeZ1) / 2, 0.22, 10), Bare, "steel");
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 0.25, TT + 0.004, YokeZ0 + 0.2, Cx + s * 0.85, TT + 0.42, YokeZ1 - 0.2, Latch, "worn paint");
            Detail(p, Cx + s * 0.86, TT + 0.1, YokeZ0 + 0.35, Cx + s * 0.98, TT + 0.3, YokeZ1 - 0.35, Bare, "steel");
            Detail(p, Cx + s * 1.004, 0.1, YokeZ0 + 0.1, Cx + s * 1.016, TT - 0.1, YokeZ1 - 0.1, Hazard, "hazard paint");
            Detail(p, Cx + s * 0.6 - 0.03, 0.2, YokeZ0 + 0.004, Cx + s * 0.6 + 0.03, TT - 0.05, YokeZ0 + 0.06, Red, "painted steel");
        }
        Detail(p, Cx - 0.9, -0.4, YokeZ0 + 0.1, Cx + 0.9, 0.0, YokeZ1 - 0.1, RackCol, "dirty steel");
        return p.Stats(2500, 20000, 40, 8.0);
    }

    private static string CellName(int i) => $"Docket String Cell {i + 1}";

    private static Part Cell(int i, bool loaded)
    {
        var p = Big(CellName(i), $"Cell {i + 1} of the Docket's string: a length of open steel truss, X-braced, with a standard 2 m container's twist locks on its top so a dock's crane lifts the container straight off, a railed catwalk down each side, floodlights under it.", color: RackCol);
        double z0 = CellZ0(i), z1 = z0 + CellL, zc = (z0 + z1) / 2;
        // The chords (four longerons), the end frame at its forward end, cross beams under the container's corner castings.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * (TW - Chord), TB, z0, Cx + s * TW, TB + Chord, z1, RackCol, "dirty steel");
            Solid(p, Cx + s * (TW - Chord), TT - Chord, z0, Cx + s * TW, TT, z1, RackCol, "dirty steel");
            Solid(p, Cx + s * (TW - Chord), TB + Chord, z0, Cx + s * TW, TT - Chord, z0 + Chord, RackCol, "dirty steel");
            Solid(p, Cx + s * (TW - 0.15), TB + Chord, zc - 0.075, Cx + s * (TW - 0.05), TT - Chord, zc + 0.075, RackCol, "dirty steel");
        }
        Solid(p, Cx - TW + Chord, TT - Chord, z0, Cx + TW - Chord, TT, z0 + Chord, RackCol, "dirty steel");
        Solid(p, Cx - TW + Chord, TB, z0, Cx + TW - Chord, TB + Chord, z0 + Chord, RackCol, "dirty steel");
        foreach (var dz in new[] { -1, 1 })
            Solid(p, Cx - TW + Chord, TT - 0.16, zc + dz * CastZ - 0.15, Cx + TW - Chord, TT - 0.004, zc + dz * CastZ + 0.15, Frame, "dirty steel");
        // The X-bracing on the sides and the bottom: flat bars between the chords, one of each pair 5 mm further out.
        void Bar(double[] a, double[] b, double w, int axis, double t)
        {
            double[][] Sq(double[] c)
            {
                double[] Q(double da, double dz) { var q = (double[])c.Clone(); q[axis] += da; q[2] += dz; return Sh(q); }
                return [Q(0, -w), Q(0, w), Q(t, w), Q(t, -w)];
            }
            Prism(p, Sq(a), Sq(b), Frame, "dirty steel");
        }
        foreach (var s in new[] { -1, 1 })
        {
            double bx = Cx + s * TW;
            foreach (var (za, zb) in new[] { (z0 + Chord, zc - 0.075), (zc + 0.075, z1) })
            {
                Bar([bx + s * 0.004, TB + Chord, za + 0.05], [bx + s * 0.004, TT - Chord, zb - 0.05], 0.05, 0, s * 0.03);
                Bar([bx + s * 0.009, TT - Chord, za + 0.05], [bx + s * 0.009, TB + Chord, zb - 0.05], 0.05, 0, s * 0.03);
            }
        }
        foreach (var (za, zb) in new[] { (z0 + Chord, zc), (zc, z1) })
        {
            Bar([Cx - TW + Chord, TB - 0.034, za + 0.05], [Cx + TW - Chord, TB - 0.034, zb - 0.05], 0.05, 1, 0.03);
            Bar([Cx + TW - Chord, TB - 0.039, za + 0.05], [Cx - TW + Chord, TB - 0.039, zb - 0.05], 0.05, 1, 0.03);
        }
        // The twist locks under the container's corner castings: red pads on the cross beams, yellow lines round its footprint.
        var k = 0;
        foreach (var dx in new[] { -1, 1 })
            foreach (var dz in new[] { -1, 1 })
            {
                double x = Cx + dx * CastX, z = zc + dz * CastZ;
                Solid(p, x - 0.17, TT, z - 0.15, x + 0.17, TT + LockH, z + 0.15, Latch, "worn paint");
                Detail(p, x - 0.05, TT + LockH, z - 0.09, x + 0.05, TT + LockH + 0.015, z + 0.09, Frame, "steel");
                p.Sock($"lock{k++}", P(x, TT + LockH, z), "+y", Medium); // (a container is a medium part)
            }
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * (TW - Chord / 2) - 0.03, TT, z0 + Chord + 0.1, Cx + s * (TW - Chord / 2) + 0.03, TT + 0.008, z1 - 0.1, Hazard, "hazard paint");
        // The catwalks: grating on brackets outboard of the chords, a railing with a yellow top rail, a kick plate.
        foreach (var s in new[] { -1, 1 })
        {
            Solid(p, Cx + s * (TW + 0.004), TT - 0.05, z0 + 0.004, Cx + s * WalkX, TT - 0.004, z1 - 0.004, Grate, "painted grating");
            foreach (var zb in new[] { z0 + 0.6, zc, z1 - 0.6 })
                Detail(p, Cx + s * (TW + 0.002), TT - 0.4, zb - 0.04, Cx + s * (WalkX - 0.05), TT - 0.054, zb + 0.04, Frame, "dirty steel");
            for (var zp = z0 + 0.3; zp < z1; zp += 2.1)
                Solid(p, Cx + s * (WalkX - 0.05), TT - 0.004, zp - 0.03, Cx + s * WalkX, TT + 1.0, zp + 0.03, Frame, "painted steel");
            Solid(p, Cx + s * (WalkX - 0.055), TT + 1.0, z0 + 0.004, Cx + s * (WalkX + 0.005), TT + 1.06, z1 - 0.004, Hazard, "worn paint");
            Detail(p, Cx + s * (WalkX - 0.045), TT + 0.5, z0 + 0.04, Cx + s * (WalkX - 0.005), TT + 0.53, z1 - 0.04, Frame, "painted steel");
            Detail(p, Cx + s * (WalkX - 0.04), TT - 0.004, z0 + 0.04, Cx + s * (WalkX - 0.01), TT + 0.12, z1 - 0.04, Hazard, "hazard paint");
            // The bay's stencil on the bottom chord's outer face (a white plate, the Traders' blue square), and the bay's lamp: amber
            // when a container is on it.
            Detail(p, Cx + s * (TW + 0.004), TB + 0.03, zc - 0.5, Cx + s * (TW + 0.012), TB + 0.17, zc + 0.5, Paper, "painted plating");
            Detail(p, Cx + s * (TW + 0.012), TB + 0.05, zc - 0.45, Cx + s * (TW + 0.018), TB + 0.15, zc - 0.3, Armour, "painted plating");
            Detail(p, Cx + s * (WalkX - 0.045), TT + 1.06, z0 + 0.25, Cx + s * (WalkX - 0.005), TT + 1.14, z0 + 0.4, loaded ? "#ffb040" : Frame, glow: loaded);
            // Pipes along the inner face of the bottom chord: fuel (red), power (blue).
            Detail(p, Cx + s * (TW - Chord - 0.06), TB + 0.05, z0 + 0.004, Cx + s * (TW - Chord - 0.004), TB + 0.11, z1 - 0.004, s < 0 ? Red : Link, "painted steel");
        }
        // Floodlights under the string for loading and docking.
        Detail(p, Cx - 0.25, TB - 0.06, zc - 0.25, Cx + 0.25, TB - 0.044, zc + 0.25, Glow, glow: true);
        return p.Stats(3000, 14000, 30, 8.0);
    }

    private static Part EndCar()
    {
        var p = Big("Docket String End", "The Docket's string end car: a squat frame at the string's tail with its legs, the stern's attitude jets, the tail lights and the stern beacon.", color: Skin);
        double z0 = EndZ0, z1 = EndZ1;
        Solid(p, Cx - TW, KeelY, z0, Cx + TW, TT, z1, Frame, "dirty steel");
        // Beige cladding on its sides and top, a blue band, hazard stripes on its back, red and white tail lights.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * (TW + 0.004), KeelY + 0.1, z0 + 0.1, Cx + s * (TW + 0.03), TT - 0.1, z1 - 0.1, Skin, "painted plating");
            Detail(p, Cx + s * (TW + 0.03), KeelY + 0.7, z0 + 0.15, Cx + s * (TW + 0.044), KeelY + 0.95, z1 - 0.15, Armour, "painted plating");
            Detail(p, Cx + s * 0.9 - 0.15, 0.3, z1 + 0.004, Cx + s * 0.9 + 0.15, 0.6, z1 + 0.04, Red, glow: true);
            for (var k = 0; k < 3; k++)
                Detail(p, Cx + s * (0.2 + k * 0.38), KeelY + 0.1, z1 + 0.004, Cx + s * (0.36 + k * 0.38), KeelY + 0.5, z1 + 0.02, Hazard, "hazard paint");
        }
        Detail(p, Cx - TW + 0.1, TT + 0.004, z0 + 0.1, Cx + TW - 0.1, TT + 0.03, z1 - 0.1, Skin2, "painted plating");
        Detail(p, Cx - 0.25, 0.65, z1 + 0.004, Cx + 0.25, 0.8, z1 + 0.05, Glow, glow: true);
        // The stern beacon on a short mast.
        double mz = z1 - 0.5;
        Detail(p, Cx - 0.05, TT + 0.03, mz - 0.05, Cx + 0.05, TT + 1.6, mz + 0.05, Paper, "painted steel");
        Detail(p, Cx - 0.09, TT + 1.6, mz - 0.09, Cx + 0.09, TT + 1.75, mz + 0.09, Red, glow: true);
        // The stern's attitude jets: up, down, out each side, and along (astern).
        foreach (var s in new[] { -1, 1 })
        {
            var n = s < 0 ? "L" : "R";
            double z = (z0 + z1) / 2;
            RcsPad(p, "rcsUp" + n, Cx + s * 0.8, TT, z, "+y", 0.06);
            RcsPad(p, "rcsDown" + n, Cx + s * 0.3, KeelY, z0 + 0.6, "-y", 0.05);
            RcsPad(p, "rcsOut" + n, Cx + s * TW, 0.2, z, s < 0 ? "-x" : "+x", 0.06);
            RcsPad(p, "rcsAlong" + n, Cx + s * 0.75, 0.0, z1, "+z", 0.06);
        }
        for (var i = 0; i < EndLegX.Length; i++)
            MountPad(p, $"leg{i}", Cx + EndLegX[i], KeelY, z1 - 0.8, 0.35, LegProud);
        return p.Stats(3500, 20000, 40, 8.0);
    }

    // ---- the flying parts: lift jets, the power plant, the flight computer (the Dray's kinds, in the Traders' colours and size)

    private static Part LiftJet()
    {
        const double k = JetScale;
        var p = new Part("Docket Lift Jet", Small, Section, $"A lift jet: {LiftForce / 1e6:0.0} MN straight down. Four under the Docket's head hold it up, string and all, from the keel's tanks.", role: "thruster", tool: "wrench", material: "steel", color: Frame, group: Group);
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
        var p = new Part("Docket Power Plant", Small, Section, "The Docket's power plant: a stack of cells in a cage, 400 kW for the docks, the lights, the life support and the flight computer. No fuel.", role: "generator", tool: "welder", material: "steel", color: Armour, group: Group);
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

    /// <summary>The flight computer: a flight stabilizer with a tug's gyro in a console between the couches and the dash.</summary>
    private static Part FlightComputer()
    {
        const string console = "#22262b";
        var p = new Part("Docket Flight Computer", Small, Section, "The Docket's flight computer: a flight stabilizer with a tug's gyro in a console between the couches. Fit it level, its arrow forward.", role: "logic", tool: "wrench", material: "steel", color: console, group: Group);
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
        var p = new Part(BellyHatchName, Small, Section, "The Docket's belly hatch: a plate that shuts the boarding shaft under the keel. F (or the airlock's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.", role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
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
        Part WallPiece(int i) => wallPieces[i] ??= Big($"Docket {shortName} Wall {i + 1}", $"A wall of the Docket's {what}, with its door frames and what is fixed to it.", color: Wall);
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
                    hull[key] = h = Big($"Docket {shortName} Hull {(key.Item1 < 0 ? "Port" : "Stbd")} {key.Item2 + 1}", $"A hull piece beside the Docket's {what}, the {(key.Item1 < 0 ? "port" : "starboard")} side: skin, armour and the void behind them. Shot off, it opens the side.");
                return h;
            }
            if (ceilY is double y && c[1] > y - 0.35)
                return ceiling ??= Big($"Docket {shortName} Ceiling", $"The ceiling of the Docket's {what}.", color: Ceil);
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
                    bands[k] = piece = Big($"Docket {shortName} {k + 1}", $"A length of the Docket's {shortName.ToLowerInvariant()}, between two of the hull's stations: plating, armour and the void inside.", color: core.Color);
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
                Console.WriteLine($"tug_parts: {shortName} band {k + 1} has only details; they join {to.D["name"]}");
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
            throw new InvalidOperationException("Docket: pieces joined to nothing: " + string.Join(", ", names.Where((n, i) => !seen.Contains(i))));
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
        var bp = new BuildingParts.Blueprint(ShipName, "The Traders' tug: a stubby head that does the work and the string it tows. A rubber-fendered push bow, the crew's rooms (bunks, a head, a galley, an airlock over the boarding shaft), engineering behind them and a raised glass wheelhouse over it, glass all round so the pilot watches the tow; four drive nacelles stacked two a side on outriggers. Behind the transom a tow yoke grips the string, an open steel truss three cells long, each taking a standard 2 m container on twist locks on its top for a dock's crane, a railed catwalk down each side to the end car with its legs and tail jets.", defs, maxSteps: 500);
        bp.Conduit = true;
        double[] zero = [0, 0, 0];
        var placed = new Dictionary<string, int>();
        foreach (var name in Structure)
            placed[name] = placed.Count == 0 ? bp.First(name) : bp.Put(name, zero, 0);
        PieceSteps = placed;
        int keel = placed["Docket Keel"], cab = placed["Docket Cab"], eng = placed["Docket Engineering"], wh = placed["Docket Wheelhouse"],
            bow = placed["Docket Push Bow"], end = placed["Docket String End"];
        foreach (var d in new[] { "doorAft", "doorBunk", "doorHead", "doorGalley", "doorAirlock" })
            Door(bp, cab, d);
        // The wheelhouse: the pilot flies through the flight computer between the couches and the dash (Ship keys, the mouse aims the nose).
        var pilot = Hang(bp, "Flight Couch", wh, "pilot", "bottom");
        Hang(bp, "Flight Couch", wh, "copilot", "bottom");
        var fc = Hang(bp, "Docket Flight Computer", wh, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight();
        bp.Wires.Add((pilot, fc));
        Paint(bp, Hang(bp, "Docket Wheelhouse Ladder", eng, "ladderWh", "foot"), Frame, "painted steel");
        // The bunk room and the head to port, a locker at the corridor's end.
        Hang(bp, "Bunk", cab, "bunk0", "bottom");
        Hang(bp, "Bunk", cab, "bunk1", "bottom");
        Paint(bp, Hang(bp, "Service Locker", cab, "lockerBunk", "wall"), Armour);
        Paint(bp, Hang(bp, "Service Locker", cab, "lockerHall", "wall"), Armour);
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
        Paint(bp, bp.Hang("Docket Boarding Ladder", hatch, "ladder", "foot"), Frame, "painted steel");
        Switch(bp, cab, "switchHatch", false, hatch); // (off: the hatch shut)
        // Engineering: the panel, the power, a suit and the MMU for the walk out to the nacelles and down the string.
        Hang(bp, "Engineer's Panel", eng, "panel", "back");
        Hang(bp, "Power Board", cab, "board", "back");
        Hang(bp, "Docket Power Plant", eng, "plant", "bottom");
        for (var i = 0; i < 3; i++)
            Hang(bp, "Battery", eng, $"battery{i}", "-y");
        Hang(bp, EvaParts.LockerName, eng, "suit", "wall");
        Paint(bp, Hang(bp, "Pressure Locker", eng, "locker", "wall"), Armour);
        EvaParts.FitDock(bp, Holder(bp, eng, "mmuDock"), "mmuDock");
        Hang(bp, "Tool Rack", eng, "tools", "back");
        // The nacelles: each on its pylon, its drive on the tail plate outside, the engine inside, the hatch in its outboard flank.
        foreach (var (s, n) in new[] { (-1, "L"), (1, "R") })
            for (var u = 0; u < 2; u++)
            {
                var nac = Hang(bp, NacName(s, u), eng, NacSock(s, u), "hull");
                Hang(bp, "Drive Nozzle M", nac, "drive", "mount");
                Hang(bp, "Fuel Pump M", Hang(bp, "Drive Core M", nac, "core", "front"), "back", "front");
                Paint(bp, Works.HangHatch(bp, nac, s), Armour, "worn paint");
                foreach (var j in new[] { "rcsFore", u == 0 ? "rcsUp" : "rcsDown" })
                    Paint(bp, Hang(bp, Rcs, nac, j, "base"), Frame, "dirty steel");
            }
        // The attitude jets: the bow's up, down and out, the string end's every way.
        foreach (var n in new[] { "L", "R" })
        {
            foreach (var d in new[] { "Up", "Down", "Out" })
                Paint(bp, Hang(bp, Rcs, bow, $"rcs{d}{n}", "base"), Frame, "dirty steel");
            foreach (var d in new[] { "Up", "Down", "Out", "Along" })
                Paint(bp, Hang(bp, Rcs, end, $"rcs{d}{n}", "base"), Frame, "dirty steel");
        }
        // Lift jets under the head, legs under the head and the end car.
        for (var i = 0; i < LiftAt.Length; i++)
            Hang(bp, "Docket Lift Jet", keel, $"lift{i}", "top");
        for (var i = 0; i < LegAt.Length; i++)
            LegPodParts.HangPod(bp, LegPod, Holder(bp, keel, $"leg{i}"), $"leg{i}");
        for (var i = 0; i < EndLegX.Length; i++)
            LegPodParts.HangPod(bp, LegPod, end, $"leg{i}");
        // The tow: a standard 2 m container on each of the first two cells, on the twist locks by its corner castings, its hatches
        // shut and its three cradles in it (green: food, water; yellow: freight); the last cell left empty, so the frame shows.
        foreach (var (i, paint) in new[] { (0, "#4f9a3e"), (1, "#d8b23a") })
        {
            var box = Paint(bp, Hang(bp, ContainerName, placed[CellName(i)], "lock0", "foot.0"), paint, "worn paint");
            foreach (var h in new[] { "hatch", "hatch.1" })
                Paint(bp, bp.Hang(ContainerName + " Hatch", box, h, "hang"), paint, "worn paint");
            for (var k = 0; k < 3; k++)
                bp.Hang("Container Cradle 2x2", box, $"slot{k}", "base");
        }
        // Lamps and their switches.
        Switch(bp, wh, "switchWh", Lamp(bp, wh, "lampWhL", "#ffd080"), Lamp(bp, wh, "lampWhR", "#ffd080"));
        Switch(bp, cab, "switchBunk", Lamp(bp, cab, "lampBunk", "#ffe0b0"));
        Switch(bp, cab, "switchHead", Lamp(bp, cab, "lampHead"));
        Switch(bp, cab, "switchGalley", Lamp(bp, cab, "lampGalley", "#ffe0b0"));
        Lamp(bp, cab, "lampAirlock", "#ffd040");
        Lamp(bp, cab, "lampHallA");
        Lamp(bp, cab, "lampHallB");
        Switch(bp, cab, "switchEng", Lamp(bp, eng, "lampEngL", "#a0d0ff"), Lamp(bp, eng, "lampEngR", "#a0d0ff"));
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
        if (NacYs[1] - NacF < Ground + 0.3)
            throw new InvalidOperationException($"tug_parts: the lower nacelles ({NacYs[1] - NacF:0.00}) reach the ground ({Ground:0.00})");
        var structure = new List<Part>();
        structure.AddRange(SplitBands(Keel(), "Keel", -9.0)); // (the band under the cab's lift jets keeps the keel's name)
        structure.AddRange(SplitDeck(Cab(), "Cab"));
        structure.AddRange(SplitDeck(Engineering(), "Eng"));
        structure.AddRange(SplitBands(Roof(), "Roof", -9.0));
        structure.Add(Wheelhouse());
        structure.Add(SternPart());
        structure.Add(PushBow());
        structure.Add(Yoke());
        for (var i = 0; i < Cells; i++)
            structure.Add(Cell(i, i < 2));
        structure.Add(EndCar());
        Connect(structure);
        Structure = PlaceOrder(structure, "Docket Keel");
        List<Part> parts =
        [
            .. structure, Nacelle(-1, 0), Nacelle(-1, 1), Nacelle(1, 0), Nacelle(1, 1),
            Ladder("Docket Boarding Ladder", Py.Round(BoardRise, 3), "The Docket's boarding ladder: it stands on the belly hatch, stowed up through the airlock's floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.", "foot", [0, 0, -0.1], "-y", over: -0.03),
            Ladder("Docket Wheelhouse Ladder", Py.Round(WFl - Ft, 3), "The Docket's wheelhouse ladder: from engineering's deck up through the opening in its ceiling to the wheelhouse. F climbs it.", "foot", [0, 0, -0.1], "-y"),
            BellyHatchPart(), LiftJet(), PowerPlant(), FlightComputer(),
        ];
        var defs = new Dictionary<string, PyDict>();
        o.Own(Out);
        foreach (var p in parts)
        {
            var d = p.Json();
            var name = (string)d["name"]!;
            if (name.Length > 32)
                throw new InvalidOperationException($"tug_parts: {name} is longer than 32 characters");
            defs.Add(name, d);
            o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
        }
        BuildingParts.AddPartDefs(o, defs, Out);
        o.File($"{BpOut}/DocketTug.json", PyJson.Dumps(Blueprint(defs), 2) + "\n");
    }
}
