using CallOfSlop.PartKit.ShipKit;
using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Knacker, the Salvagers' breaker (Karl, 2026-11-21: "New design using a different style please. This one can be for salvage").
/// The third ship on the ship kit and the first in the Salvagers' look from their mood board: a frame for holding someone else's ship.
/// Two open truss jaws reach 18 m forward of a wide plated root, fendered clamp pads on their inner faces and chevron claws at their
/// tips; the bridge looks straight down between them through the root's front. Behind it the sorting floor (the Recycler and a row of
/// Materials Bins), then a narrower crew pod (four bunks; the utility room with the belly hatch). Four standard containers ride on
/// open racks along the flanks, fed by lime chutes off the root, and two engines sit in lime-strapped cradles on the stern.
/// Bare galvanised zinc, dark mill-scale frames, fender black, claim lime where hands go and material flows, heat-tinted cut seams
/// and the odd panel in someone else's paint.
///
/// Drawn on the kit (LoftedShip, docs/sandbox/constructs/ship-kit.md). Ship metres: x across, y up from the deck's underside, z along
/// (forward is -z; 0 on the sorting floor).
/// </summary>
public sealed class KnackerParts : LoftedShip, IPartGenerator
{
    public string Name => "knacker_parts";

    private const string Out = "Constructs/Parts/Knacker";
    private const string BpOut = "Constructs/Blueprints";
    public const string ShipName = "Knacker Breaker";

    /// <summary>The Salvagers' paint (their mood board, draft 2): galvanised zinc over a darker zinc, mill-scale frames, fender black,
    /// claim lime, heat tint, warm floodlights, amber screens.</summary>
    private static readonly ShipStyle Salvagers = new(
        Skin: "#a7aba8", Skin2: "#959a97", Armour: "#2f3438", Armour2: "#3b4146", Bare: "#8b8f8c", Frame: "#26292b", Dark: "#161819",
        Hazard: "#c4dc2e", Red: "#b8432c", Glow: "#ffcf80", Glass: "#7d968e", Link: "#e8a040", Paper: "#d6d4ca", Green: "#5aa040",
        Wall: "#9a9e98", WallLow: "#3b4146", WallTrim: "#26292b", Deck: "#4c4f4d", Grate: "#363938", Ceil: "#a4a7a2", Latch: "#c4dc2e",
        Screen: "#e8a040", Screen2: "#c4dc2e", Console: "#26292b", Mat: "#2a2c2b");

    private const string Tint = "#7a4a30", Tint2 = "#4a5a78"; // a cut seam's heat tint (bronze to blue)
    private static readonly string[] Borrowed = ["#1f4e8c", "#8a2a22", "#c8a640"]; // other factions' paint on salvaged panels

    public KnackerParts() : base("Knacker", "Knacker breaker", Salvagers)
    {
        Hull = HullSecs;
        Bands = [Zf, ZR1, ZR2, ZR3, ZRootEnd, ZPodF, ZP1, ZPodEnd, ZStern];
        RibZ = [ZR2, ZRootEnd, ZPodEnd]; // (Karl, 2026-11-21: "remove a little bit of the beading": the hoops at ZR1, ZR3 and ZP1 went)
    }

    // ---- the plan

    private const double Ft = 0.25, CCeil = 2.65, CRoof = 2.9;
    private const double RLw = 4.5, PLw = 2.5; // the root's and the pod's rooms' inner half widths
    private const double Zf = -3.0, ZR1 = -0.5, ZR2 = 2.0, ZR3 = 4.5, ZRootEnd = 7.0, ZPodF = 8.2, ZP1 = 11.7, ZPodEnd = 15.2, ZStern = 16.6;
    private const double ZFloor0 = Zf + 0.15; // the root's floor starts behind the front plate's backing

    // The bridge window in the front plate.
    private const double WinX = 1.8, WinY0 = 1.0, WinY1 = 2.4;

    // The jaws: two box trusses forward of the root, their hinges on plinths in front of the front plate.
    private const double JawOut = 5.4, JawIn = 4.0, JawY0 = 0.4, JawY1 = 2.8, JawLen = 18.0, Lon = 0.22;
    private const double ZJaw0 = Zf - 0.402; // the hinge's fore face
    private static double ZJaw1 => Zf - JawLen; // where the claw begins
    private static double ZTip => ZJaw1 - 1.6;
    private static readonly double[] PadZ = [-7.0, -12.5, -18.0];

    // The boarding shaft in the utility room, the belly hatch shutting it and the ladder on the hatch.
    private static readonly Rect Shaft = new(1.1, 12.3, 2.25, 13.55);
    private const string HatchName = "Knacker Belly Hatch";
    private static double HatchX => Shaft.X0 + 0.575;

    // The container racks along the flanks: a standard container each over the root and over the pod.
    private const double CtW = 2.5, CtH = 2.5, CtL = 6.5, LockH = 0.06, CastX = 1.165, CastZ = 3.165;
    private const double RackX0 = 5.3, RackX1 = 8.05, RackY0 = -0.5, RackY1 = -0.2, RackZ0 = -1.7, RackZ1 = 14.1, CtX = 6.7;
    private static readonly double[] CtZ = [1.8, 10.6];
    private static readonly double[] BeamZ = [0.3, 4.2, 9.0, 13.2];

    // ---- flying it

    private const double LiftForce = 480000, DriveForce = 480000, Burn = 2.5;
    private const int KeelFuel = 14000, SternFuel = 6000;
    private static readonly (double X, double Z)[] LiftAt = [(-2.0, -1.75), (2.0, -1.75), (-2.0, 3.25), (2.0, 3.25), (-1.3, 9.9), (1.3, 9.9)];
    private static readonly (double X, double Z)[] LegAt = [(-3.8, -2.0), (3.8, -2.0), (-3.8, 5.6), (3.8, 5.6), (-2.0, 14.8), (2.0, 14.8)];
    private const string Gear = "Ram Leg"; // (Karl, 2026-11-21: "doesn't need landing gear so big": the compact ram legs, not the M pods)
    private const double LegProud = 0.16;
    private static double Ground => -0.6 - LegProud - LandingGearParts.StandingOf(Gear);
    private const string Rcs = "RCS Thruster";
    private const double NacX = 1.75, NacY = 1.7, NacR = 0.85, NacZ1 = 19.0;

    // ---- the hull: a wide flat-sided root with chamfered edges, a step in to the narrower, taller crew pod, a short stern.

    private static readonly (double, double)[] RootSec = [(4.7, -0.5), (5.2, 0.0), (5.3, 1.7), (5.1, 3.0), (4.3, 3.4)];
    private static readonly (double, double)[] PodSec = [(2.9, -0.5), (3.3, 0.0), (3.4, 1.6), (3.25, 3.5), (2.4, 3.95)];

    private static readonly Sec[] HullSecs =
    [
        new(Zf, RootSec), new(ZR1, RootSec), new(ZR2, RootSec), new(ZR3, RootSec), new(ZRootEnd, RootSec),
        new(ZPodF, PodSec), new(ZP1, PodSec), new(ZPodEnd, PodSec),
        new(ZStern, [(2.5, -0.3), (2.9, 0.15), (3.0, 1.6), (2.85, 3.35), (2.1, 3.75)]),
    ];

    /// <summary>Patchwork plating: zinc patches of two greys with dark borders, now and then a panel in borrowed paint, cut seams with
    /// their heat tint, a lime claim strap.</summary>
    private void Panels(Part p, int k, int s, double za, double zb, int salt, double v0 = 0.08, double v1 = 0.92)
    {
        var n = Math.Max(1, (int)Math.Round((zb - za) / 1.25));
        for (var i = 0; i < n; i++)
        {
            double u0 = (double)i / n + 0.06 / (zb - za), u1 = (double)(i + 1) / n - 0.06 / (zb - za);
            var pick = (k * 5 + i * 3 + salt * 7) % 11;
            // (patches run ragged: each its own height on the band)
            double a = v0 + (pick % 3) * 0.06, b = v1 - ((pick + 1) % 3) * 0.07;
            if (pick == 4)
                Slab(p, k, s, za, zb, u0, u1, a, b, 0.02, 0.06, 0.03, Borrowed[(salt + i) % Borrowed.Length], "worn paint");
            else if (pick % 3 == 0)
                Slab(p, k, s, za, zb, u0, u1, a, b, 0.02, 0.07, 0.04, Skin2, "plated steel");
            else if (pick % 3 == 1)
            {
                Slab(p, k, s, za, zb, u0, u1, a, b, 0.02, 0.05, 0.02, Skin, "plated steel");
                FStrip(p, k, s, za, zb, u0 + 0.02, u1 - 0.02, (a + b) / 2 - 0.012, (a + b) / 2 + 0.012, 0.052, Tint, "dirty steel"); // (a cut seam)
            }
            else
            {
                FStrip(p, k, s, za, zb, (u0 + u1) / 2 - 0.006, (u0 + u1) / 2 + 0.006, v0, v1, 0.03, Tint2, "dirty steel");
                if (i % 2 == 0)
                    FStrip(p, k, s, za, zb, u0 + 0.1 * (u1 - u0), u0 + 0.22 * (u1 - u0), v0, v1, 0.031, Hazard, "hazard paint"); // (a claim strap)
            }
        }
    }

    private void Band(Part p, int k, int s, double za, double zb, string color, int salt, bool panels = true)
    {
        Facet(p, k, s, za, zb, color, "plated steel");
        if (panels && zb - za > 1.0)
            Panels(p, k, s, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), salt);
    }

    /// <summary>A square bar between two points (drawn): the trusses' diagonals and the rams.</summary>
    private static void Bar(Part p, double[] a, double[] b, double w, string color, string finish = "dirty steel")
    {
        var d = Unit(Sub(b, a));
        double[] up = Math.Abs(d[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        var s1 = Unit(Cross(d, up));
        var s2 = Cross(d, s1);
        double[][] Ring(double[] c) => [.. new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(q => Sh([c[0] + (q.Item1 * s1[0] + q.Item2 * s2[0]) * w / 2, c[1] + (q.Item1 * s1[1] + q.Item2 * s2[1]) * w / 2, c[2] + (q.Item1 * s1[2] + q.Item2 * s2[2]) * w / 2]))];
        Prism(p, Ring(a), Ring(b), color, finish);
    }

    /// <summary>A round bar along z (drawn): nacelles, straps.</summary>
    private static void TubeZ(Part p, double cx, double cy, double r, double z0, double z1, int n, string color, string finish)
    {
        double[][] Ring(double z) => [.. RingZ(cx, cy, r, r, z, n).Select(Sh)];
        Prism(p, Ring(z0), Ring(z1), color, finish);
    }

    /// <summary>A polygon in x/y cut by a line: keeps the side where f is not negative.</summary>
    private static List<(double X, double Y)> Clip(List<(double X, double Y)> poly, Func<(double X, double Y), double> f)
    {
        var o = new List<(double, double)>();
        for (var i = 0; i < poly.Count; i++)
        {
            var (a, b) = (poly[i], poly[(i + 1) % poly.Count]);
            double fa = f(a), fb = f(b);
            if (fa >= 0)
                o.Add(a);
            if ((fa >= 0) != (fb >= 0))
            {
                var t = fa / (fa - fb);
                o.Add((a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
            }
        }
        return o;
    }

    // ---- the keel: the flat underside, the tankage under the decks, the shaft, the jets' and legs' mounts, the racks' beams

    private Part Keel()
    {
        var p = Big("Knacker Keel", "The Knacker's keel: the flat underside of the root and the crew pod, with the fuel tankage under the decks, the boarding shaft, the lift jets' and legs' mounts, and the beams the container racks hang on.", "fueltank", Armour);
        var shaft = new Rect(Shaft.X0 - 0.05, Shaft.Z0 - 0.05, Shaft.X1 + 0.05, Shaft.Z1 + 0.05);
        foreach (var (i, za, zb) in BandsIn(Zf, ZPodEnd))
        {
            var y = At(za)[0].Y;
            var hb = At(za)[0].H;
            if (za <= Shaft.Z0 && zb >= Shaft.Z1)
            {
                var a = Shaft;
                double hb2 = At(zb)[0].H, yb2 = At(zb)[0].Y;
                double H(double z) => hb + (hb2 - hb) * (z - za) / (zb - za);
                Plate(p, [P(Cx - hb, y, za), P(Cx + hb, y, za), P(Cx + H(a.Z0), y, a.Z0), P(Cx - H(a.Z0), y, a.Z0)], Armour, "plated steel");
                Plate(p, [P(Cx - H(a.Z1), y, a.Z1), P(Cx + H(a.Z1), y, a.Z1), P(Cx + hb2, yb2, zb), P(Cx - hb2, yb2, zb)], Armour, "plated steel");
                Plate(p, [P(Cx - H(a.Z0), y, a.Z0), P(a.X0, y, a.Z0), P(a.X0, y, a.Z1), P(Cx - H(a.Z1), y, a.Z1)], Armour, "plated steel");
                Plate(p, [P(a.X1, y, a.Z0), P(Cx + H(a.Z0), y, a.Z0), P(Cx + H(a.Z1), y, a.Z1), P(a.X1, y, a.Z1)], Armour, "plated steel");
            }
            else
                Facet(p, 0, 1, za, zb, Armour, "plated steel");
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 1, s, za, zb, Skin2, 1 + i, panels: false);
                Chine(p, 1, s, za, zb, 0.16, 0.07, Frame);
            }
            if (zb - za > 2 && Math.Abs(At(zb)[0].Y - y) < 1e-6) // (rubbing strakes along the flat: it sets down on wrecks)
                foreach (var s in new[] { -1, 1 })
                    foreach (var sx in za < ZRootEnd ? new[] { 1.2, 3.2 } : new[] { 0.7 })
                        Detail(p, Cx + s * sx - 0.08, y - 0.1, za + 0.25, Cx + s * sx + 0.08, y - 0.023, zb - 0.25, Dark, "rubber");
        }
        Ribs(p, Zf, ZPodEnd, 0, 1);
        foreach (var (_, za, zb) in BandsIn(Zf, ZPodEnd))
        {
            var yb = Math.Max(At(za)[0].Y, At(zb)[0].Y) + 0.02;
            var xo = Math.Floor((new[] { SkinHalf(za, yb), SkinHalf(za, 0), SkinHalf(zb, yb), SkinHalf(zb, 0) }.Min() - 0.06) * 100) / 100;
            if (xo < 0.3)
                continue;
            foreach (var r in Minus([new Rect(Cx - xo, za + 0.012, Cx + xo, zb - 0.012)], shaft))
                Hidden(p, r.X0, yb, r.Z0, r.X1, 0.0, r.Z1);
        }
        // The racks' beams: mill-scale I-beams out through the skin under the deck, a lime cap where each meets its rack.
        foreach (var z in BeamZ)
            foreach (var s in new[] { -1, 1 })
            {
                var x0 = z < ZRootEnd ? 4.55 : 2.8;
                Solid(p, Cx + s * x0, RackY0 + 0.05, z - 0.15, Cx + s * RackX0, RackY1 + 0.05, z + 0.15, Frame, "painted steel");
                Detail(p, Cx + s * (RackX0 - 0.09), RackY1 + 0.05, z - 0.17, Cx + s * (RackX0 - 0.012), RackY1 + 0.09, z + 0.17, Hazard, "hazard paint");
            }
        // The shaft's lining down through the keel, lime round its mouth.
        var hy = At((Shaft.Z0 + Shaft.Z1) / 2)[0].Y;
        foreach (var (x0, z0, x1, z1, e) in new[] { (shaft.X0, shaft.Z0, shaft.X1, shaft.Z0, 0.0), (shaft.X0, shaft.Z1, shaft.X1, shaft.Z1, 0.0),
            (shaft.X0, shaft.Z0 + 0.025, shaft.X0, shaft.Z1 - 0.025, 0.005), (shaft.X1, shaft.Z0 + 0.025, shaft.X1, shaft.Z1 - 0.025, 0.005) })
            Plate(p, [P(x0, hy + 0.01 + e, z0), P(x1, hy + 0.01 + e, z1), P(x1, -0.01 - e, z1), P(x0, -0.01 - e, z0)], Grate, "dirty steel");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z0 - 0.12, shaft.X1 + 0.12, hy - 0.004, shaft.Z0, Hazard, "hazard paint");
        Detail(p, shaft.X0 - 0.12, hy - 0.03, shaft.Z1, shaft.X1 + 0.12, hy - 0.004, shaft.Z1 + 0.12, Hazard, "hazard paint");
        for (var i = 0; i < LiftAt.Length; i++)
            MountPad(p, $"lift{i}", Cx + LiftAt[i].X, At(LiftAt[i].Z)[0].Y, LiftAt[i].Z, 0.4);
        for (var i = 0; i < LegAt.Length; i++)
            MountPad(p, $"leg{i}", Cx + LegAt[i].X, At(LegAt[i].Z)[0].Y, LegAt[i].Z, 0.38, LegProud);
        foreach (var s in new[] { -1, 1 })
        {
            RcsPad(p, "rcsDown" + (s < 0 ? "L" : "R"), Cx + s * 0.5, At(-2.6)[0].Y, -2.6, "-y", 0.05, 0.4);
            RcsPad(p, "rcsDownAft" + (s < 0 ? "L" : "R"), Cx + s * 0.6, At(14.4)[0].Y, 14.4, "-y", 0.05, 0.4);
        }
        // Work floods under the root's front, looking down at what the jaws hold; amber markers along the edges.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 2.6 - 0.2, -0.56, Zf + 0.3, Cx + s * 2.6 + 0.2, -0.512, Zf + 0.55, Glow, glow: true);
            foreach (var z in new[] { 0.0, 4.0, 10.5 })
            {
                var x = z < ZRootEnd ? 4.45 : 2.65;
                Detail(p, Cx + s * x - 0.06, -0.54, z - 0.1, Cx + s * x + 0.06, -0.512, z + 0.1, "#ffb040", glow: true);
            }
        }
        p.Set(("fuelTank", Kw(("capacity", KeelFuel))));
        return p.Stats(18000, 44000, 40, 20.0);
    }

    // ---- the roof: the root's flat deck with its claim marks and floods, the pod's taller roof with skylights of three sizes

    private Part Roof()
    {
        var p = Big("Knacker Roof", "The Knacker's roof: the root's flat top with its claim marks, mast and floodlights over the jaws, stepping up to the crew pod's roof and its mismatched skylights.", color: Skin);
        foreach (var (i, za, zb) in BandsIn(Zf, ZPodEnd))
        {
            Facet(p, 5, 1, za, zb, Skin2, "plated steel");
            if (zb - za > 1.0)
                Panels(p, 5, 1, za + (RibAt(za) ? 0.3 : 0.05), zb - (RibAt(zb) ? 0.3 : 0.05), 7 + i, 0.1, 0.9);
            foreach (var s in new[] { -1, 1 })
            {
                Band(p, 4, s, za, zb, Skin, 4 + i);
                Chine(p, 4, s, za, zb, 0.14, 0.07, Frame);
            }
        }
        Ribs(p, Zf, ZPodEnd, 4, 5);
        double Top(double z) => At(z)[4].Y;
        var y = Top(0) + 0.12;
        // The claim mark: a big lime chevron on the root's top pointing at the jaws, and the yard's number beside it.
        for (var k = 0; k < 2; k++)
            foreach (var s in new[] { -1, 1 })
            {
                double z0 = Zf + 0.9 + k * 0.9, z1 = z0 + 0.35;
                Detail(p, Cx + s * 0.1, y, z0, Cx + s * 1.6, y + 0.012, z1, Hazard, "hazard paint");
                Detail(p, Cx + s * 1.25, y, z1, Cx + s * 1.6, y + 0.012, z1 + 0.5, Hazard, "hazard paint");
            }
        Detail(p, Cx + 2.4, y, ZR2 + 0.4, Cx + 3.6, y + 0.012, ZR2 + 0.7, Paper, "worn paint");
        Detail(p, Cx + 2.4, y, ZR2 + 0.9, Cx + 3.0, y + 0.012, ZR2 + 1.2, Paper, "worn paint");
        // A lattice mast on the root's aft corner: a whip, a red beacon, two floods on a bar looking forward at the jaws.
        {
            double mx = Cx - 3.2, mz = ZR3 + 0.6;
            foreach (var (dx, dz) in new[] { (-0.2, -0.2), (0.2, -0.2), (0.2, 0.2), (-0.2, 0.2) })
                Detail(p, mx + dx - 0.03, y, mz + dz - 0.03, mx + dx + 0.03, y + 2.4, mz + dz + 0.03, Frame, "painted steel");
            for (var k = 0; k < 4; k++)
                Detail(p, mx - 0.24, y + 0.4 + k * 0.6, mz - 0.24, mx + 0.24, y + 0.44 + k * 0.6, mz + 0.24, Frame, "painted steel");
            Detail(p, mx - 0.012, y + 2.44, mz - 0.012, mx + 0.012, y + 3.8, mz + 0.012, Dark, "painted steel");
            Detail(p, mx - 0.06, y + 2.44, mz - 0.06, mx + 0.06, y + 2.56, mz + 0.06, Red, glow: true);
            Detail(p, mx - 1.0, y + 2.0, mz - 0.33, mx + 1.0, y + 2.08, mz - 0.25, Frame, "painted steel");
            foreach (var dx in new[] { -0.8, 0.8 })
            {
                Detail(p, mx + dx - 0.18, y + 1.88, mz - 0.6, mx + dx + 0.18, y + 2.2, mz - 0.33, Dark, "painted steel");
                Detail(p, mx + dx - 0.15, y + 1.91, mz - 0.612, mx + dx + 0.15, y + 2.17, mz - 0.6, Glow, glow: true);
            }
        }
        // Floods along the root's front edge, looking down the jaws.
        foreach (var x in new[] { -3.2, -1.2, 1.2, 3.2 })
        {
            Detail(p, Cx + x - 0.25, y, Zf + 0.15, Cx + x + 0.25, y + 0.3, Zf + 0.45, Dark, "painted steel");
            Detail(p, Cx + x - 0.21, y + 0.04, Zf + 0.138, Cx + x + 0.21, y + 0.26, Zf + 0.15, Glow, glow: true);
        }
        // The pod's skylights: one round, one square, one small and round, every one from a different ship; lime grab rails.
        {
            var yp = Top(ZP1) + 0.11;
            foreach (var (z, r, n) in new[] { (9.4, 0.5, 12), (13.6, 0.3, 10) })
            {
                Prism(p, Ngon(Cx - 0.6, yp, z, r + 0.12, n), Ngon(Cx - 0.6, yp + 0.08, z, r + 0.08, n), Frame, "painted steel");
                Prism(p, Ngon(Cx - 0.6, yp + 0.08, z, r, n), Ngon(Cx - 0.6, yp + 0.11, z, r - 0.04, n), Glass, "glass");
            }
            Detail(p, Cx + 0.3, yp, 10.8, Cx + 1.4, yp + 0.09, 11.9, Borrowed[0], "worn paint");
            Detail(p, Cx + 0.42, yp + 0.09, 10.92, Cx + 1.28, yp + 0.12, 11.78, Glass, "glass");
            foreach (var s in new[] { -1, 1 })
            {
                Detail(p, Cx + s * 1.9 - 0.03, yp, ZPodF + 0.5, Cx + s * 1.9 + 0.03, yp + 0.25, ZPodF + 0.56, Hazard, "hazard paint");
                Detail(p, Cx + s * 1.9 - 0.03, yp, ZPodEnd - 0.56, Cx + s * 1.9 + 0.03, yp + 0.25, ZPodEnd - 0.5, Hazard, "hazard paint");
                Detail(p, Cx + s * 1.9 - 0.03, yp + 0.25, ZPodF + 0.5, Cx + s * 1.9 + 0.03, yp + 0.3, ZPodEnd - 0.5, Hazard, "hazard paint");
            }
        }
        // Navigation lights on the root's front corners and the pod's aft corners: red to port, green to starboard.
        foreach (var s in new[] { -1, 1 })
        {
            Detail(p, Cx + s * 4.0 - 0.09, Top(Zf) - 0.01, Zf + 0.2, Cx + s * 4.0 + 0.09, Top(Zf) + 0.08, Zf + 0.38, s < 0 ? Red : Green, glow: true);
            Detail(p, Cx + s * 2.2 - 0.09, Top(ZPodEnd) - 0.01, ZPodEnd - 0.4, Cx + s * 2.2 + 0.09, Top(ZPodEnd) + 0.08, ZPodEnd - 0.22, s < 0 ? Red : Green, glow: true);
        }
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsUp" + (s < 0 ? "L" : "R"), Cx + s * 3.0, Top(-1.8), -1.8, "+y", 0.05, 0.4);
        // The void over the ceilings (flush at the stern's station: its void welds to it).
        foreach (var (_, za, zb) in BandsIn(Zf, ZPodEnd))
        {
            var z0 = Math.Max(za, ZFloor0);
            var tb = zb == ZPodEnd ? 0.0 : 0.004;
            var ta = z0 > za ? 0.0 : 0.004;
            VoidT(p, z0, zb, CRoof, Math.Min(Top(z0), Top(zb)) - 0.03, 0, ta, tb);
        }
        return p.Stats(9000, 24000, 30, 15.0);
    }

    // ---- the bow: the root's front plate with the bridge window, the jaws' hinge plinths

    private Part Bow()
    {
        var p = Big("Knacker Bow", "The Knacker's front plate: the bridge window looking down between the jaws, the jaws' hinge plinths either side, the work lights.", color: Skin2);
        var q = At(Zf);
        var poly = Enumerable.Range(0, 5).Select(k => (Cx + q[k].H, q[k].Y)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => (Cx - q[k].H, q[k].Y))).ToList();
        void Piece(List<(double X, double Y)> pts, string col, string fin)
        {
            if (pts.Count >= 3)
                Plate(p, [.. pts.Select(v => P(v.X, v.Y, Zf))], col, fin);
        }
        Piece(Clip(poly, v => WinY0 - v.Y), Armour, "plated steel");
        Piece(Clip(poly, v => v.Y - WinY1), Skin2, "plated steel");
        var mid = Clip(Clip(poly, v => v.Y - WinY0), v => WinY1 - v.Y);
        Piece(Clip(mid, v => -WinX - v.X), Skin, "plated steel");
        Piece(Clip(mid, v => v.X - WinX), Skin, "plated steel");
        Plate(p, [P(Cx - WinX, WinY0, Zf), P(Cx + WinX, WinY0, Zf), P(Cx + WinX, WinY1, Zf), P(Cx - WinX, WinY1, Zf)], Glass, glass: true);
        // The window's frame and its bars (a breaker's window gets hit), a lime chevron band under it, a tinted cut seam over it.
        Detail(p, Cx - WinX - 0.12, WinY0 - 0.12, Zf - 0.08, Cx + WinX + 0.12, WinY0, Zf - 0.015, Frame, "painted steel");
        Detail(p, Cx - WinX - 0.12, WinY1, Zf - 0.08, Cx + WinX + 0.12, WinY1 + 0.12, Zf - 0.015, Frame, "painted steel");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * WinX - 0.12 * (s < 0 ? 1 : 0), WinY0, Zf - 0.08, Cx + s * WinX + 0.12 * (s > 0 ? 1 : 0), WinY1, Zf - 0.015, Frame, "painted steel");
        foreach (var x in new[] { -0.9, 0.0, 0.9 })
            Detail(p, Cx + x - 0.03, WinY0, Zf - 0.06, Cx + x + 0.03, WinY1, Zf - 0.025, Frame, "painted steel");
        for (var i = 0; i < 9; i++)
            Detail(p, Cx - 3.6 + i * 0.8, 0.3, Zf - 0.035, Cx - 3.2 + i * 0.8, 0.62, Zf - 0.021, i % 2 == 0 ? Hazard : Dark, "hazard paint");
        Detail(p, Cx - 3.0, WinY1 + 0.32, Zf - 0.032, Cx + 2.2, WinY1 + 0.35, Zf - 0.021, Tint, "dirty steel");
        // The hinge plinths (the jaws weld to their fore faces), the plinths' looks.
        foreach (var s in new[] { -1, 1 })
        {
            Hidden(p, Cx + s * (JawIn - 0.1), JawY0 - 0.1, ZJaw0, Cx + s * (JawOut + 0.1), JawY1 + 0.1, Zf - 0.002);
            Detail(p, Cx + s * (JawIn - 0.05), JawY0 - 0.05, ZJaw0 + 0.01, Cx + s * (JawOut + 0.05), JawY1 + 0.05, Zf - 0.025, Armour, "painted steel");
            Detail(p, Cx + s * (JawIn - 0.07), JawY1 + 0.05, ZJaw0 + 0.05, Cx + s * (JawOut + 0.07), JawY1 + 0.09, Zf - 0.05, Hazard, "hazard paint");
            RcsPad(p, "rcsFwd" + (s < 0 ? "L" : "R"), Cx + s * 2.8, 0.75, Zf - 0.002, "-z", 0.05, 0.4);
        }
        // The backing the root's floor welds to.
        Hidden(p, Cx - RLw - 0.25, 0.0, Zf + 0.002, Cx + RLw + 0.25, Ft, ZFloor0);
        return p.Stats(3000, 12000, 40, 12.0);
    }

    // ---- the jaws: open box trusses, fendered clamp pads inside, chevron claws at the tips

    private Part Jaw(int s)
    {
        var side = s < 0 ? "Port" : "Stbd";
        var p = Big($"Knacker Jaw {side}", $"The Knacker's {(s < 0 ? "port" : "starboard")} jaw: an open box truss 18 m long on its hinge plinth, fendered clamp pads on its inner face and a chevron claw at its tip. Between the jaws the Knacker holds a hull section up to 18 by 8 m while it is broken.", color: Frame);
        double xo = Cx + s * JawOut, xi = Cx + s * JawIn, zh = ZJaw0 - 0.3;
        // The longerons (solid), the hinge block behind them.
        foreach (var x in new[] { xo, xi })
            foreach (var y in new[] { JawY0, JawY1 })
                Solid(p, x - Lon / 2, y - Lon / 2, ZJaw1, x + Lon / 2, y + Lon / 2, zh, Frame, "dirty steel");
        Hidden(p, Cx + s * (JawIn - 0.1), JawY0 - 0.1, zh, Cx + s * (JawOut + 0.1), JawY1 + 0.1, ZJaw0);
        // The hinge: two knuckles round a pin, the pin's cap lime.
        foreach (var (y0, y1) in new[] { (JawY0 - 0.15, JawY0 + 0.6), (JawY1 - 0.6, JawY1 + 0.15) })
            Prism(p, Ngon((xo + xi) / 2, y0, ZJaw0 - 0.25, 0.42, 12), Ngon((xo + xi) / 2, y1, ZJaw0 - 0.25, 0.42, 12), Armour, "painted steel");
        Prism(p, Ngon((xo + xi) / 2, JawY0 + 0.6, ZJaw0 - 0.25, 0.2, 10), Ngon((xo + xi) / 2, JawY1 - 0.6, ZJaw0 - 0.25, 0.2, 10), Bare, "steel");
        Prism(p, Ngon((xo + xi) / 2, JawY1 + 0.15, ZJaw0 - 0.25, 0.24, 10), Ngon((xo + xi) / 2, JawY1 + 0.22, ZJaw0 - 0.25, 0.24, 10), Hazard, "hazard paint");
        // Bays: verticals (thinner than the longerons) at the nodes, a zig-zag of diagonals on each face, alternate ones a few mm apart.
        var nb = 9;
        double Zn(int k) => zh + (ZJaw1 + 0.15 - zh) * k / nb;
        for (var k = 0; k <= nb; k++)
        {
            var z = Zn(k);
            foreach (var x in new[] { xo, xi })
                Detail(p, x - 0.07, JawY0 + Lon / 2 - 0.02, z - 0.07, x + 0.07, JawY1 - Lon / 2 + 0.02, z + 0.07, Frame, "dirty steel");
            foreach (var y in new[] { JawY0, JawY1 })
                Detail(p, Math.Min(xo, xi) + Lon / 2 - 0.02, y - 0.07, z - 0.07, Math.Max(xo, xi) - Lon / 2 + 0.02, y + 0.07, z + 0.07, Frame, "dirty steel");
        }
        for (var k = 0; k < nb; k++)
        {
            double za = Zn(k), zb = Zn(k + 1), e = (k % 2) * 0.004;
            var up = k % 2 == 0;
            foreach (var x in new[] { xo, xi })
                Bar(p, [x + e, up ? JawY0 : JawY1, za], [x + e, up ? JawY1 : JawY0, zb], 0.12, Armour2);
            foreach (var y in new[] { JawY0, JawY1 })
                Bar(p, [up ? xo : xi, y + e, za], [up ? xi : xo, y + e, zb], 0.1, Armour2);
        }
        // Lime claim straps round the longerons, two per jaw.
        foreach (var z in new[] { Zn(3), Zn(7) })
            foreach (var x in new[] { xo, xi })
                foreach (var y in new[] { JawY0, JawY1 })
                    Detail(p, x - Lon / 2 - 0.015, y - Lon / 2 - 0.015, z + 0.2, x + Lon / 2 + 0.015, y + Lon / 2 + 0.015, z + 0.35, Hazard, "hazard paint");
        // The clamp pads on the inner face: fender-black blocks on dark backing plates.
        foreach (var zc in PadZ)
        {
            Solid(p, xi - s * (Lon / 2), JawY0 + 0.25, zc - 0.85, xi - s * 0.2, JawY1 - 0.25, zc + 0.85, Armour, "painted steel");
            Solid(p, xi - s * 0.2, JawY0 + 0.35, zc - 0.75, xi - s * 0.55, JawY1 - 0.35, zc + 0.75, Dark, "rubber");
            for (var k = 0; k < 4; k++)
                Detail(p, xi - s * 0.55, JawY0 + 0.45 + k * 0.42, zc - 0.7, xi - s * 0.58, JawY0 + 0.6 + k * 0.42, zc + 0.7, Dark, "rubber");
        }
        // The ram from the root's front to the jaw's third node (a look: the jaws stand open as built).
        {
            var a = new[] { Cx + s * 3.3, 1.6, Zf - 0.05 };
            var b = new[] { xi + s * 0.05, 1.6, Zn(3) };
            var m = Lerp(a, b, 0.55);
            Bar(p, a, m, 0.3, Armour, "painted steel");
            Bar(p, Lerp(a, b, 0.5), b, 0.13, Bare, "steel");
            Bar(p, Lerp(a, b, 0.53), Lerp(a, b, 0.56), 0.33, Hazard, "hazard paint");
        }
        // The claw: the truss closing to a chevron hooked inward, its faces dark plate with lime chevrons.
        {
            double tx = Cx + s * (JawIn - 0.35);
            double[] Pt(double x, double y, double z) => P(x, y, z);
            foreach (var y in new[] { JawY0, JawY1 })
            {
                var yy = y + (y == JawY0 ? -Lon / 2 - 0.004 : Lon / 2 + 0.004); // (just proud of the longerons' faces)
                Plate(p, [Pt(xo + s * Lon / 2, yy, ZJaw1), Pt(xi - s * Lon / 2, yy, ZJaw1), Pt(tx, yy, ZTip)], Frame, "dirty steel");
            }
            Quad(p, Pt(xo + s * Lon / 2, JawY0 - Lon / 2 + 0.008, ZJaw1), Pt(tx, JawY0 - Lon / 2 + 0.008, ZTip), Pt(tx, JawY1 + Lon / 2 - 0.008, ZTip), Pt(xo + s * Lon / 2, JawY1 + Lon / 2 - 0.008, ZJaw1), Frame, "dirty steel");
            Quad(p, Pt(xi - s * Lon / 2, JawY0 - Lon / 2 + 0.016, ZJaw1), Pt(xi - s * Lon / 2, JawY1 + Lon / 2 - 0.016, ZJaw1), Pt(tx, JawY1 + Lon / 2 - 0.016, ZTip), Pt(tx, JawY0 - Lon / 2 + 0.016, ZTip), Dark, "dirty steel");
            for (var k = 0; k < 3; k++)
            {
                double u = 0.2 + k * 0.25, z = ZJaw1 + (ZTip - ZJaw1) * u, x = xo + s * Lon / 2 + (tx - xo - s * Lon / 2) * u;
                var n = Unit(Cross(Sub(P(tx, 0, ZTip), P(xo, 0, ZJaw1)), [0, 1, 0]));
                double o1 = s * 0.025, o2 = s * 0.031; // (the chevron's two strokes a few mm apart)
                Bar(p, [x + n[0] * o1, JawY0 + 0.3, z + n[2] * o1], [x + n[0] * o1, (JawY0 + JawY1) / 2, z - 0.25 + n[2] * o1], 0.1, Hazard, "hazard paint");
                Bar(p, [x + n[0] * o2, (JawY0 + JawY1) / 2, z - 0.25 + n[2] * o2], [x + n[0] * o2, JawY1 - 0.3, z + n[2] * o2], 0.1, Hazard, "hazard paint");
            }
            Hidden(p, Math.Min(xo, tx) - 0.05, JawY0 - Lon / 2, ZTip + 0.5, Math.Max(xo, tx) + 0.05, JawY1 + Lon / 2, ZJaw1);
        }
        return p.Stats(4500, 16000, 45, 15.0);
    }

    // ---- the root: the bridge forward (the window in the bow plate), the sorting floor (the Recycler, the bins), the engineer's wall

    private Part Root()
    {
        var p = Big("Knacker Root", "The Knacker's root, one big room behind the bow plate: the bridge at the window looking down between the jaws, then the sorting floor with the Recycler and a row of Materials Bins, the engineer's boards on the starboard wall and the ship's air by the door aft.");
        var plan = new Rect(Cx - RLw - 0.25, ZFloor0, Cx + RLw + 0.25, ZRootEnd + 0.125); // (under the aft wall: the fittings on its face stand clear of the floor's end)
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate");
        Floor(p, [new Rect(plan.X0, ZFloor0, plan.X1, ZRootEnd - 0.125)], CCeil, CRoof, Ceil, "painted plating");
        Room(p, new Rect(plan.X0, Zf - 0.1, plan.X1, ZRootEnd + 0.125), 0, CRoof);
        CeilingAt(p, CCeil);
        foreach (var s in new[] { -1, 1 })
            WallRun(p, false, ZFloor0, ZRootEnd - 0.125, Cx + s * (RLw + 0.125), Ft, CCeil);
        WallRun(p, true, Cx - RLw - 0.25, Cx + RLw + 0.25, ZRootEnd, Ft, CCeil, Door(Cx - 0.75));
        // The bridge: a dark dash under the window with amber screens and a lime key row, a mat under the couches.
        Solid(p, Cx - 1.0, Ft, ZFloor0, Cx + 1.0, 0.9, ZFloor0 + 0.45, Dark, "plastic");
        foreach (var (x0, x1, col) in new[] { (-0.94, -0.36, Style.Screen), (-0.3, 0.3, Paper), (0.36, 0.94, Style.Screen) })
            Detail(p, Cx + x0, 0.9, ZFloor0 + 0.05, Cx + x1, 0.912, ZFloor0 + 0.4, col, glow: col != Paper);
        for (var i = 0; i < 8; i++)
            Detail(p, Cx - 0.72 + i * 0.18, 0.55, ZFloor0 + 0.45, Cx - 0.64 + i * 0.18, 0.63, ZFloor0 + 0.463, i % 3 == 1 ? Hazard : Paper, "plastic");
        Detail(p, Cx - 1.3, Ft, ZFloor0 + 0.55, Cx + 1.3, Ft + 0.012, ZFloor0 + 2.4, Style.Mat, "rubber matting");
        // The sorting floor: lime lines where material goes (the Recycler to the bins), a drain grating round the Recycler.
        Detail(p, Cx - 0.7, Ft, 2.3, Cx + 0.7, Ft + 0.012, 3.7, Grate, "painted grating");
        Detail(p, Cx - 3.5, Ft, 2.96, Cx - 0.72, Ft + 0.012, 3.04, Hazard, "hazard paint");
        Detail(p, Cx - 3.48, Ft, -0.4, Cx - 3.4, Ft + 0.012, 3.04, Hazard, "hazard paint");
        foreach (var (z, col) in new[] { (-0.5, "#8a4a2e"), (0.5, "#b87333"), (1.5, "#c9cdd1"), (2.5, "#5a6e3a") })
            Detail(p, Cx - RLw + 0.004, Ft + 1.0, z - 0.3, Cx - RLw + 0.02, Ft + 1.2, z + 0.3, col, "worn paint"); // (the bins' material tags)
        // Rails along the walls, a light strip down the middle.
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * (RLw - 0.05), Ft + 2.0, ZFloor0 + 2.6, Cx + s * (RLw - 0.01), Ft + 2.05, ZRootEnd - 0.4, Frame, "painted steel");
        CeilStrip(p, Cx - 0.08, Cx + 0.08, ZFloor0 + 0.6, ZRootEnd - 0.5, CCeil, "#ffe8c0");
        // The hull bands: patchwork zinc, chute hatches over the racks.
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(Zf, ZRootEnd))
            {
                Band(p, 2, s, za, zb, Skin, 11 + i);
                Band(p, 3, s, za, zb, Skin2, 12 + i);
                Chine(p, 2, s, za, zb, 0.2, 0.09, Frame);
                Chine(p, 3, s, za, zb, 0.16, 0.07, Armour);
                if (za == ZR1 || za == ZR3)
                {
                    Slab(p, 3, s, za, zb, 0.3, 0.7, 0.15, 0.7, 0.07, 0.13, 0.02, Armour, "painted steel"); // (a chute's hatch)
                    FStrip(p, 3, s, za, zb, 0.32, 0.68, 0.72, 0.78, 0.131, Hazard, "hazard paint");
                }
            }
        Ribs(p, Zf, ZRootEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(Zf, ZRootEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.3), (1.3, CRoof) })
                VoidT(p, za, zb, ya, yb, RLw + 0.25, za == Zf ? 0.06 : 0.004, zb == ZRootEnd ? 0.125 : 0.004);
        foreach (var s in new[] { -1, 1 })
            RcsPad(p, "rcsOut" + (s < 0 ? "L" : "R"), FacetX(2, s, -2.5, 0.8), 0.8, -2.5, s < 0 ? "-x" : "+x", 0.2, 0.4);
        DoorSock(p, "doorRoot", true, Cx - 0.75, ZRootEnd, Ft);
        Sock(p, "pilot", Cx - 0.75, Ft, ZFloor0 + 1.5, "+y");
        Sock(p, "copilot", Cx + 0.75, Ft, ZFloor0 + 1.5, "+y");
        Sock(p, "computer", Cx, Ft, ZFloor0 + 0.85, "+y");
        Sock(p, "recycler", Cx, Ft, 3.0, "+y");
        for (var i = 0; i < 4; i++)
            Sock(p, $"bin{i}", Cx - RLw + 0.55, Ft, -0.5 + i * 1.0, "+y");
        Sock(p, "panel", Cx + RLw, Ft + 0.6, 0.6, "-x");
        Sock(p, "board", Cx + RLw, Ft + 0.9, 2.7, "-x");
        Sock(p, "coffee", Cx + RLw, Ft + 0.125, 4.6, "-x");
        Sock(p, "lifeSupport", Cx + 2.6, Ft + 0.125, ZRootEnd - 0.125, "-z");
        Sock(p, "o2Tank", Cx + 3.7, Ft + 0.125, ZRootEnd - 0.125, "-z");
        Sock(p, "switchRoot", Cx + 1.3, Ft + 1.3, ZRootEnd - 0.125, "-z");
        foreach (var x in new[] { -2.5, 2.5 })
            foreach (var z in new[] { 0.5, 4.5 })
                Sock(p, $"lampR{(x < 0 ? "L" : "R")}{(z < 2 ? 0 : 1)}", Cx + x, CCeil, z, "-y");
        foreach (var z in new[] { ZFloor0, ZRootEnd - 0.125 })
            foreach (var y in new[] { 0.0, CRoof })
                MustFit("root", z, RLw + 0.25, y);
        return p.Stats(14000, 30000, 30, 20.0);
    }

    // ---- the crew pod: four bunks, then the utility room with the belly hatch, the plant, the batteries, the suit

    private Part Pod()
    {
        var p = Big("Knacker Pod", "The Knacker's crew pod: a bunk room for the four of them (pilot, two cutters, the sorter), then the utility room with the belly hatch and its ladder, the plant, the batteries and a suit locker.");
        var plan = new Rect(Cx - PLw - 0.25, ZRootEnd + 0.125, Cx + PLw + 0.25, ZPodEnd - 0.125);
        Floor(p, [plan], 0, Ft, Deck, "painted deck plate", Shaft);
        Floor(p, [plan], CCeil, CRoof, Ceil, "painted plating");
        Room(p, plan, 0, CRoof);
        CeilingAt(p, CCeil);
        foreach (var s in new[] { -1, 1 })
            foreach (var (a, b) in new[] { (ZRootEnd + 0.125, ZP1 - 0.125), (ZP1 + 0.125, ZPodEnd - 0.125) })
                WallRun(p, false, a, b, Cx + s * (PLw + 0.125), Ft, CCeil);
        WallRun(p, true, Cx - PLw - 0.25, Cx + PLw + 0.25, ZP1, Ft, CCeil, Door(Cx - 0.9));
        WallRun(p, true, Cx - PLw - 0.25, Cx + PLw + 0.25, ZPodEnd, Ft, CCeil);
        HatchRail(p, Shaft, Ft);
        // Hammock hooks and a mismatched runner in the bunk room, gratings in the utility room, a light strip each.
        Detail(p, Cx - 0.6, Ft, ZRootEnd + 0.4, Cx + 0.6, Ft + 0.012, ZP1 - 0.4, Borrowed[1], "rubber matting");
        Detail(p, Cx - 0.85, Ft, ZP1 + 0.3, Cx + 0.85, Ft + 0.012, ZPodEnd - 0.4, Grate, "painted grating");
        CeilStrip(p, Cx - 0.08, Cx + 0.08, ZRootEnd + 0.5, ZP1 - 0.4, CCeil, "#ffe0b0");
        CeilStrip(p, Cx - 0.08, Cx + 0.08, ZP1 + 0.4, ZPodEnd - 0.5, CCeil, "#ffe0b0");
        foreach (var s in new[] { -1, 1 })
            Detail(p, Cx + s * (PLw - 0.05), Ft + 1.9, ZRootEnd + 0.4, Cx + s * (PLw - 0.01), Ft + 1.95, ZP1 - 0.4, Hazard, "painted steel");
        // The hull: patchwork, and portholes from three different ships along the bunk room.
        foreach (var s in new[] { -1, 1 })
            foreach (var (i, za, zb) in BandsIn(ZRootEnd, ZPodEnd))
            {
                Band(p, 2, s, za, zb, Skin2, 21 + i);
                Band(p, 3, s, za, zb, Skin, 22 + i, panels: za != ZPodF);
                Chine(p, 2, s, za, zb, 0.2, 0.09, Frame);
                Chine(p, 3, s, za, zb, 0.16, 0.07, Armour);
            }
        foreach (var s in new[] { -1, 1 })
        {
            var (za, zb) = (ZPodF, ZP1);
            double len = zb - za;
            foreach (var (z, w, h, col) in new[] { (8.9, 0.5, 0.5, Frame), (10.0, 0.7, 0.45, Borrowed[s < 0 ? 0 : 2]), (11.0, 0.36, 0.36, Armour) })
            {
                var q = At(z);
                double u0 = (z - w / 2 - za) / len, u1 = (z + w / 2 - za) / len, vm = (Ft + 1.5 - q[2].Y) / (q[3].Y - q[2].Y), dv = h / 2 / (q[3].Y - q[2].Y);
                Slab(p, 3, s, za, zb, u0 - 0.06 / len, u1 + 0.06 / len, vm - dv - 0.035, vm + dv + 0.035, 0.03, 0.08, 0.02, col, "painted steel");
                FStrip(p, 3, s, za, zb, u0, u1, vm - dv, vm + dv, 0.082, "#24343a", "glass");
            }
        }
        Ribs(p, ZRootEnd, ZPodEnd, 2, 3);
        foreach (var (_, za, zb) in BandsIn(ZRootEnd, ZPodEnd))
            foreach (var (ya, yb) in new[] { (0.0, 1.3), (1.3, CRoof) })
                VoidT(p, za, zb, ya, yb, PLw + 0.25, za == ZRootEnd ? 0.125 : 0.004, zb == ZPodEnd ? 0.125 : 0.004);
        // The bunk room.
        foreach (var s in new[] { -1, 1 })
            foreach (var (z, k) in new[] { (8.2, 0), (10.45, 1) })
                Sock(p, $"bunk{(s < 0 ? "L" : "R")}{k}", Cx + s * (PLw - 0.55), Ft, z, "+y");
        Sock(p, "switchCrew", Cx - PLw, Ft + 1.3, ZRootEnd + 0.5, "+x");
        Sock(p, "lampCL", Cx - PLw, Ft + 2.05, 9.3, "+x");
        Sock(p, "lampCR", Cx + PLw, Ft + 2.05, 9.3, "-x");
        // The utility room.
        DoorSock(p, "doorPod", true, Cx - 0.9, ZP1, Ft);
        Sock(p, "plant", Cx - 2.15, Ft, 13.9, "+y");
        Sock(p, "battery0", Cx - 2.2, Ft, 12.4, "+y");
        Sock(p, "battery1", Cx - 1.8, Ft, 12.4, "+y");
        Sock(p, "suit", Cx + PLw, Ft + 0.125, 14.55, "-x");
        Sock(p, "switchUtil", Cx - 1.5, Ft + 1.3, ZP1 + 0.125, "+z");
        Sock(p, "switchHatch", Cx + 0.85, Ft + 1.45, ZP1 + 0.125, "+z");
        Sock(p, "lampUL", Cx - PLw, Ft + 2.05, 12.8, "+x");
        Sock(p, "lampUR", Cx + PLw, Ft + 2.05, 13.0, "-x");
        Sock(p, "boarding", HatchX, Ft - 0.125, Shaft.Z1, "-z");
        foreach (var z in new[] { plan.Z0, plan.Z1 })
            foreach (var y in new[] { 0.0, CRoof })
                MustFit("pod", z, PLw + 0.25, y);
        return p.Stats(9000, 22000, 30, 15.0);
    }

    // ---- the stern: a short taper, the transom, the two engines in their lime-strapped cradles

    private Part SternPart()
    {
        var p = Big("Knacker Stern", "The Knacker's stern: the pod's short taper to a dark transom, and the two engines in open cradles on it, strapped down in lime. The engines' own tank is in it.", "fueltank", Skin);
        var (za, zb) = (ZPodEnd, ZStern);
        Facet(p, 5, 1, za, zb, Skin2, "plated steel");
        Facet(p, 0, 1, za, zb, Armour, "plated steel");
        foreach (var s in new[] { -1, 1 })
            for (var k = 1; k <= 4; k++)
            {
                Facet(p, k, s, za, zb, k % 2 == 0 ? Skin : Skin2, "plated steel");
                Chine(p, k, s, za, zb, 0.15, 0.07, k == 2 ? Frame : Armour);
            }
        Ribs(p, za, zb, 0, 1, 2, 3, 4, 5);
        var q = At(zb);
        Plate(p, [.. Enumerable.Range(0, 5).Select(k => P(Cx + q[k].H, q[k].Y, zb)).Concat(Enumerable.Range(0, 5).Reverse().Select(k => P(Cx - q[k].H, q[k].Y, zb)))], Frame, "soot");
        // The trophy: a door off some other ship bolted on the transom, still in its own paint.
        Detail(p, Cx - 0.55, 1.2, zb + 0.02, Cx + 0.55, 3.0, zb + 0.07, Borrowed[2], "worn paint");
        Detail(p, Cx - 0.4, 2.3, zb + 0.07, Cx + 0.4, 2.75, zb + 0.085, Glass, "glass");
        Detail(p, Cx + 0.3, 1.9, zb + 0.07, Cx + 0.42, 1.98, zb + 0.11, Bare, "steel");
        // The engines: long round nacelles in open cradles of mill-scale channel, three lime straps each.
        foreach (var s in new[] { -1, 1 })
        {
            double x = Cx + s * NacX, z0 = zb - 0.5;
            TubeZ(p, x, NacY, NacR, z0, NacZ1 - 0.3, 16, Bare, "dirty steel");
            TubeZ(p, x, NacY, NacR * 0.92, NacZ1 - 0.3, NacZ1, 16, Armour, "soot");
            foreach (var zs in new[] { zb + 0.3, zb + 1.2, zb + 2.1 })
                TubeZ(p, x, NacY, NacR + 0.03, zs, zs + 0.18, 16, Hazard, "hazard paint");
            // The cradle: saddles under each nacelle on two rails out of the transom.
            foreach (var dx in new[] { -0.55, 0.55 })
                Solid(p, x + dx - 0.09, NacY - NacR - 0.32, zb - 0.002, x + dx + 0.09, NacY - NacR - 0.1, NacZ1 - 0.4, Frame, "painted steel");
            foreach (var zs in new[] { zb + 0.75, zb + 1.65 })
                Detail(p, x - 0.75, NacY - NacR - 0.1, zs - 0.08, x + 0.75, NacY - 0.45, zs + 0.08, Frame, "painted steel");
            Hidden(p, x - NacR * 0.7, NacY - NacR * 0.7, zb + 0.002, x + NacR * 0.7, NacY + NacR * 0.7, NacZ1);
            Sock(p, $"drive{(s < 0 ? 0 : 1)}", x, NacY, NacZ1, "+z");
            Detail(p, Cx + s * 2.65 - 0.1, 2.9, zb + 0.02, Cx + s * 2.65 + 0.1, 3.3, zb + 0.06, Red, glow: true);
        }
        // Inside: the void behind the pod's aft wall.
        foreach (var (ya, yb) in new[] { (0.0, 1.3), (1.3, CRoof) })
            VoidT(p, za, zb - 0.05, ya, yb, 0, 0.125, 0.0);
        VoidT(p, za, zb - 0.05, CRoof, Math.Min(At(za)[4].Y, At(zb)[4].Y) - 0.03, 0, 0.0, 0.0);
        foreach (var s in new[] { -1, 1 })
        {
            RcsPad(p, "rcsUp" + (s < 0 ? "L" : "R"), Cx + s * 1.2, At(15.9)[4].Y, 15.9, "+y", 0.05, 0.4);
            RcsPad(p, "rcsOut" + (s < 0 ? "L" : "R"), FacetX(3, s, 15.9, 2.4), 2.4, 15.9, s < 0 ? "-x" : "+x", 0.2, 0.4);
        }
        p.Set(("fuelTank", Kw(("capacity", SternFuel))));
        FuelPort(p, 3.2);
        return p.Stats(7000, 20000, 35, 15.0);
    }

    // ---- the racks: open container decks along the flanks, the chutes off the root

    private Part Rack(int s)
    {
        var side = s < 0 ? "Port" : "Stbd";
        var p = Big($"Knacker Rack {side}", $"The Knacker's {(s < 0 ? "port" : "starboard")} container rack: an open deck on the keel's beams for two standard containers, each tagged for one material, the root's chute over the forward one.", color: Frame);
        double x0 = Cx + s * RackX0, x1 = Cx + s * RackX1;
        Solid(p, x0, RackY0, RackZ0, x1, RackY1, RackZ1, Frame, "painted steel");
        // A lime rail along the outer edge, posts at the ends, a hazard-striped toe at the ends.
        Detail(p, x1 - s * 0.08, RackY1, RackZ0 + 0.1, x1 - s * 0.004, RackY1 + 0.5, RackZ0 + 0.18, Hazard, "hazard paint");
        Detail(p, x1 - s * 0.08, RackY1, RackZ1 - 0.18, x1 - s * 0.004, RackY1 + 0.5, RackZ1 - 0.1, Hazard, "hazard paint");
        Detail(p, x1 - s * 0.08, RackY1 + 0.5, RackZ0 + 0.1, x1 - s * 0.004, RackY1 + 0.56, RackZ1 - 0.1, Hazard, "hazard paint");
        for (var i = 0; i < 5; i++)
            Detail(p, Math.Min(x0, x1) + 0.1 + i * 0.52, RackY0 - 0.012, RackZ0 - 0.002, Math.Min(x0, x1) + 0.36 + i * 0.52, RackY1 - 0.05, RackZ0 + 0.04, i % 2 == 0 ? Hazard : Dark, "hazard paint");
        // The deck's underside: cross channels.
        for (var z = RackZ0 + 0.9; z < RackZ1 - 0.5; z += 1.6)
            Detail(p, Math.Min(x0, x1) + 0.05, RackY0 - 0.14, z - 0.06, Math.Max(x0, x1) - 0.05, RackY0, z + 0.06, Armour, "painted steel");
        // The twist locks.
        for (var ri = 0; ri < CtZ.Length; ri++)
        {
            double xc = Cx + s * CtX, zc = CtZ[ri];
            var k = 0;
            foreach (var dx in new[] { -1, 1 })
                foreach (var dz in new[] { -1, 1 })
                {
                    double x = xc + dx * CastX, z = zc + dz * CastZ;
                    Solid(p, x - 0.17, RackY1, z - 0.17, x + 0.17, RackY1 + LockH, z + 0.17, Latch, "worn paint");
                    p.Sock($"lock{ri}{k++}", P(x, RackY1 + LockH, z), "+y", Medium);
                }
        }
        // The chute off the root's hatch, down over the forward container: a lime trough on two hangers.
        {
            double zc = CtZ[0], xa = Cx + s * 5.36, xb = Cx + s * 6.4, ya = 2.95, yb = RackY1 + LockH + CtH + 0.25;
            foreach (var dz in new[] { -0.45, 0.45 })
                Bar(p, [xa, ya, zc + dz], [xb, yb, zc + dz], 0.06, Hazard, "hazard paint");
            Plate(p, [P(xa, ya - 0.04, zc - 0.42), P(xa, ya - 0.04, zc + 0.42), P(xb, yb - 0.04, zc + 0.42), P(xb, yb - 0.04, zc - 0.42)], Armour, "dirty steel");
        }
        return p.Stats(3000, 12000, 35, 10.0);
    }

    // ---- the Knacker's own fittings

    private Part LiftJet() => Jet(new JetSpec("Knacker Lift Jet",
        $"A lift jet: {LiftForce / 1e3:0} kN straight down from a six-sided bell in a two-louvre shroud. Six under the Knacker's keel hold it and what its jaws carry.",
        JetAxis.Lift, LiftForce, Burn, 0.6, [(0.4, 0.26), (0.5, 0.22), (0.75, 0.32), (0.9, 0.36)], Sides: 6,
        ShroudW: 0.42, ShroudL: 0.38, Louvres: 2, BandAt: 0.25, Mass: 700, Health: 4000));

    private Part Drive() => Jet(new JetSpec("Knacker Drive",
        $"One of the Knacker's two engines, {DriveForce / 1e3:0} kN, out of the back of a long nacelle in a lime-strapped cradle. Slow, and meant to be: it tows wrecks.",
        JetAxis.Aft, DriveForce, Burn, 0.8, [(0.3, 0.42), (0.5, 0.36), (0.8, 0.5), (1.0, 0.56)], Sides: 12,
        ShroudW: 0.45, ShroudL: 0.3, Louvres: 0, BandAt: 0.2, Body: Frame, Mass: 900, Health: 5000));

    private Part Plant() => PowerPlant(new PlantSpec("Knacker Power Plant",
        "The Knacker's power plant: two cells in a cage against the utility room's wall, 200 kW for the Recycler, the lights and the air. No fuel.",
        200, 0.24, 0.42, 0.15, [(0.0, -0.19), (0.0, 0.19)], 0.16, 1.35, 8, 1.05, 0.03, 1.45, [(-0.12, Red), (0.12, Hazard)], Mass: 2000, Health: 3000));

    private Part Computer() => FlightComputer(new ConsoleSpec("Knacker Flight Computer",
        "The Knacker's flight computer: a flight stabilizer with a big gyro (the jaws are long) in a squat console between the couches. Fit it level, its arrow forward.",
        30000000, 0.34, 0.5, 0.82, 0.6, [(-0.3, -0.02, 0.32, 0.56, Style.Screen), (0.02, 0.3, 0.32, 0.56, Style.Screen2)], 6, 0.18));

    private Part BoardingLadder(double rise) => Ladder(new LadderSpec("Knacker Boarding Ladder",
        "The Knacker's boarding ladder: it stands on the belly hatch, stowed up through the utility room's floor while the hatch is shut, and runs from the ground up the shaft when it is open. F climbs it.",
        Py.Round(rise, 3), -0.03, "foot", [0, 0, -0.1], "-y", Rung: 0.3, Standoff: 1.0, HalfW: 0.24));

    private (Part Hatch, double Rise) Hatch()
    {
        var bottom = At(Shaft.Z1)[0].Y - 0.012;
        var top = bottom + 0.08;
        var travel = Math.Min(bottom - Ground - 0.005, CCeil - 0.03 - Ft);
        var hatch = BellyHatch(new HatchSpec(HatchName,
            "The Knacker's belly hatch: a plate that shuts the boarding shaft under the crew pod. F (or the utility room's switch) slides it down to the ground, and the boarding ladder standing on it comes down with it.",
            Shaft, bottom, top, Ft, HatchX, Shaft.Z1 - 0.03, travel, Stripes: 4));
        return (hatch, Ft - (top - travel));
    }

    // ---- the blueprint

    private List<string> order = [];

    private PyDict Blueprint(Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint(ShipName, "The Salvagers' breaker: two open truss jaws 18 m long hold a wreck's section while its crew cut it up, the Recycler on the sorting floor breaks the pieces into material, and four containers on the flank racks take it home, one material each. The bridge looks down between the jaws; a crew pod with four bunks and the belly hatch behind; two engines in lime-strapped cradles.", defs, maxSteps: 400);
        bp.Conduit = true;
        var placed = PlaceStructure(bp, order);
        int keel = placed["Knacker Keel"], roof = placed["Knacker Roof"], root = placed["Knacker Root"], pod = placed["Knacker Pod"], stern = placed["Knacker Stern"], bow = placed["Knacker Bow"];
        HangDoor(bp, root, "doorRoot", Armour);
        HangDoor(bp, pod, "doorPod", Borrowed[0]); // (a door off a Traders' ship)
        // The bridge.
        var pilot = Hang(bp, "Flight Couch", root, "pilot", "bottom");
        Hang(bp, "Flight Couch", root, "copilot", "bottom");
        var fc = Hang(bp, "Knacker Flight Computer", root, "computer", "bottom");
        bp.Steps[fc]["config"] = ShipParts.ShipFlight(pitchDamping: 200);
        bp.Wires.Add((pilot, fc));
        // The sorting floor.
        Paint(bp, Hang(bp, "Recycler", root, "recycler", "bottom"), Hazard, "worn paint");
        var tags = new[] { "#8a4a2e", "#b87333", "#c9cdd1", "#5a6e3a" };
        for (var i = 0; i < 4; i++)
            Paint(bp, Hang(bp, "Materials Bin", root, $"bin{i}", "bottom"), tags[i], "worn paint");
        Hang(bp, "Engineer's Panel", root, "panel", "back");
        Hang(bp, "Power Board", root, "board", "back");
        Hang(bp, "Galley Coffee Station", root, "coffee", "wall");
        Hang(bp, EvaParts.LifeSupportName, root, "lifeSupport", "wall");
        Hang(bp, EvaParts.OxygenTankName, root, "o2Tank", "wall");
        // The crew pod.
        foreach (var n in new[] { "L0", "L1", "R0", "R1" })
            Hang(bp, "Bunk", pod, $"bunk{n}", "bottom");
        var hatch = Hang(bp, HatchName, pod, "boarding", "hang");
        Paint(bp, bp.Hang("Knacker Boarding Ladder", hatch, "ladder", "foot"), Hazard, "painted steel");
        Switch(bp, pod, "switchHatch", false, hatch);
        Hang(bp, "Knacker Power Plant", pod, "plant", "bottom");
        Hang(bp, "Battery", pod, "battery0", "-y");
        Hang(bp, "Battery", pod, "battery1", "-y");
        Hang(bp, EvaParts.LockerName, pod, "suit", "wall");
        // The jaws and the racks on them.
        foreach (var side in new[] { "Port", "Stbd" })
        {
            var rack = placed[$"Knacker Rack {side}"];
            for (var ri = 0; ri < CtZ.Length; ri++)
            {
                var col = tags[(side == "Port" ? 0 : 2) + ri];
                var box = Paint(bp, Hang(bp, CourierParts.ContainerName, rack, $"lock{ri}0", "foot.0"), col, "worn paint");
                foreach (var h in new[] { "hatch", "hatch.1" })
                    Paint(bp, bp.Hang(CourierParts.ContainerName + " Hatch", box, h, "hang"), col, "worn paint");
                for (var k = 0; k < 3; k++)
                    bp.Hang("Container Cradle 2x2", box, $"slot{k}", "base");
            }
        }
        // The engines, the jets, the legs.
        for (var i = 0; i < 2; i++)
            Hang(bp, "Knacker Drive", stern, $"drive{i}", "mount");
        foreach (var n in new[] { "L", "R" })
        {
            Paint(bp, Hang(bp, Rcs, stern, $"rcsUp{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, stern, $"rcsOut{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, roof, $"rcsUp{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, keel, $"rcsDown{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, keel, $"rcsDownAft{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, root, $"rcsOut{n}", "base"), Frame, "dirty steel");
            Paint(bp, Hang(bp, Rcs, bow, $"rcsFwd{n}", "base"), Frame, "dirty steel");
        }
        for (var i = 0; i < LiftAt.Length; i++)
            Hang(bp, "Knacker Lift Jet", keel, $"lift{i}", "top");
        for (var i = 0; i < LegAt.Length; i++)
            LandingGearParts.HangGear(bp, Gear, Holder(bp, keel, $"leg{i}"), $"leg{i}");
        // Lamps and their switches: warm floods on the sorting floor, amber in the bunks, cold in the utility room.
        Switch(bp, root, "switchRoot", [.. new[] { "L", "R" }.SelectMany(s => new[] { 0, 1 }.Select(k => Lamp(bp, root, $"lampR{s}{k}", "#ffd8a0")))]);
        Switch(bp, pod, "switchCrew", Lamp(bp, pod, "lampCL", "#ffb060"), Lamp(bp, pod, "lampCR", "#ffb060"));
        Switch(bp, pod, "switchUtil", Lamp(bp, pod, "lampUL", "#d0e8ff"), Lamp(bp, pod, "lampUR", "#d0e8ff"));
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        Reset();
        var structure = new List<Part>();
        structure.AddRange(SplitBands(Keel(), "Keel", 0.0));
        structure.AddRange(SplitDeck(Root(), "Root"));
        structure.AddRange(SplitDeck(Pod(), "Pod"));
        structure.AddRange(SplitBands(Roof(), "Roof", 0.0));
        structure.Add(SternPart());
        structure.Add(Bow());
        structure.Add(Jaw(-1));
        structure.Add(Jaw(1));
        structure.Add(Rack(-1));
        structure.Add(Rack(1));
        Connect(structure);
        order = PlaceOrder(structure, "Knacker Keel");
        var (hatch, rise) = Hatch();
        Emit(o, Out, [.. structure, hatch, BoardingLadder(rise), LiftJet(), Drive(), Plant(), Computer()], $"{BpOut}/KnackerBreaker.json", Blueprint);
    }
}
