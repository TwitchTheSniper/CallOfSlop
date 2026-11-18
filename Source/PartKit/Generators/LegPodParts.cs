using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Landing leg pods (Karl, 2026-11-18: "They should be in their own pod underneath the ship. When you press a button to action them
/// from a connected seat, the canopy opens, and the end of two points is on a gantry inside, which move the landing feet out of the
/// canopy ... in various sizes, following the same design language of the Zero Day style of ships ... start from scratch").
///
/// Each size is four parts that build one pod: the <b>housing</b> (a faceted armoured pod welded under a keel by its top, hollow, the
/// gantry's rails on its end walls), two <b>canopy</b> leaves (event doors that swing down and out about their outboard edges, a
/// shallow V under the pod when shut) and the <b>gantry</b> (an event door that slides straight down its rails) that carries the leg:
/// a telescopic strut and two raked braces whose tops ride the gantry's carriage and whose feet meet at the ankle over a broad pad.
/// Stowed, the whole leg is inside the pod behind the canopy; deployed, the carriage stops at the pod's mouth and the pad stands
/// <see cref="Size.Reach"/> under the pod's top. The order (canopy, then gantry; gantry, then canopy) is the wiring's.
///
/// The look is the Zero Day's (TorpedoFrigateParts.cs): a pale case-grey skin with graphite armour slabs standing proud of it, a
/// sharp beam line with a dark chine rail and a link-blue light strip under it, lanyard-yellow hazard paint at the moving edges, an
/// amber lamp at the bow end. Frame: the pod's top at y 0 (its "top" socket, +y, on a keel), x across, z along the ship (bow -z).
/// Every moving part shares that frame: its "hang" socket meets the housing's socket at the same point. Writes Constructs/Parts/LegPods
/// and one blueprint a size (the pod assembled, for the galleries).
/// </summary>
public sealed class LegPodParts : IPartGenerator
{
    public string Name => "leg_pods";

    private const string Out = "Constructs/Parts/LegPods";
    private const string BpOut = "Constructs/Blueprints";
    private const string Section = "Ship fittings", Group = "Ship";

    // The Zero Day's paint.
    private const string Skin = "#bdb9ad", Skin2 = "#a9a59a", Armour = "#3b4148", Armour2 = "#30353b", Blue = "#2c5aa0", Frame = "#1f2226",
        Hazard = "#e0b020", Glow = "#5ac8ff", Amber = "#f0a830", Chrome = "#9aa0a4", Rubber = "#1a1b1d", Lining = "#2a2e33";

    /// <summary>A size: its letter, its scale on the L (the Zero Day's), and the masses. Reach: the pad's sole under the pod's top, deployed.</summary>
    private sealed record Size(string L, double K, double HousingMass, double GantryMass, double LeafMass, double Health)
    {
        public double Travel => Py.Round(CarTravel * K, 2);
        public double FootTravel => Py.Round((CarTravel + RamOut) * K, 2);
        public double Reach => Py.Round((-PadSole + CarTravel + RamOut) * K, 2);
    }

    private static readonly Size[] Sizes =
    [
        new("S", 0.5, 220, 140, 40, 1500),
        new("M", 0.75, 700, 420, 110, 3500),
        new("L", 1.0, 1500, 900, 240, 6000),
    ];

    // The pod in L metres (scaled by K): top half width and half length, the beam's height and half width, the mouth's.
    private const double TopW = 0.85, TopL = 1.75, BeamY = -0.45, BeamW = 0.95, MouthY = -1.35, MouthW = 0.68, MouthL = 1.5;

    /// <summary>The top's and the beam's half length towards end e (-1 the bow, raked out further: a chisel nose; +1 the stern).</summary>
    private static double TopAt(double e) => e < 0 ? 2.15 : TopL;
    private static double BeamAt(double e) => e < 0 ? 2.05 : 1.8;
    private const double InW = 0.6, InL = 1.45, Ceil = -0.12; // the hollow inside: half width, half length (the rails' face), the ceiling
    private const double Hinge = 0.64; // a canopy leaf's outboard edge (its hinge runs along the bottom corner there)
    private const double PlateT = 0.04;

    private static double[] Pt(double k, double x, double y, double z) => [Py.Round(x * k, 4), Py.Round(y * k, 4), Py.Round(z * k, 4)];

    private static PyDict F(string finish) => Kw(("finish", finish));

    private static void D(Part p, double k, double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish, bool glow = false) =>
        p.Add(Box(Pt(k, Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), Pt(k, Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), col, detail: true,
            shape: glow ? Kw(("glow", true), ("paintable", false)) : F(finish)));

    private static void Hidden(Part p, double k, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box(Pt(k, Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), Pt(k, Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));

    /// <summary>A convex solid from its points (L metres), drawn only.</summary>
    private static void Hull(Part p, double k, IEnumerable<double[]> pts, string col, string finish, bool glow = false)
    {
        var q = pts.Select(a => Pt(k, a[0], a[1], a[2])).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(i => q.Min(a => a[i]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(i => q.Max(a => a[i]))];
        var shape = Kw(("shape", "hull"), ("points", q.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()));
        if (glow)
        {
            shape["glow"] = true;
            shape["paintable"] = false;
        }
        else
            shape["finish"] = finish;
        p.Add(Box(mn, mx, col, detail: true, shape: shape));
    }

    private static double[] Sub(double[] a, double[] b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
    private static double[] Add(double[] a, double[] b, double s = 1) => [a[0] + b[0] * s, a[1] + b[1] * s, a[2] + b[2] * s];
    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    private static double[] Unit(double[] a) { var l = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]); return [a[0] / l, a[1] / l, a[2] / l]; }
    private static double[] Lerp(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

    /// <summary>A point on quad a b c d (a to b is u, a to d is v).</summary>
    private static double[] On(double[][] q, double u, double v) => Lerp(Lerp(q[0], q[1], u), Lerp(q[3], q[2], u), v);

    /// <summary>The outward normal of quad a b c d, pointed away from <paramref name="inside"/>.</summary>
    private static double[] Normal(double[][] q, double[] inside)
    {
        var n = Unit(Cross(Sub(q[1], q[0]), Sub(q[3], q[0])));
        var c = Lerp(q[0], q[2], 0.5);
        var toIn = Sub(inside, c);
        return n[0] * toIn[0] + n[1] * toIn[1] + n[2] * toIn[2] > 0 ? [-n[0], -n[1], -n[2]] : n;
    }

    /// <summary>A plate on quad q, from <paramref name="o0"/> to <paramref name="o1"/> out along its normal; the outer face inset by <paramref name="bevel"/> (a chamfered slab).</summary>
    private static void Slab(Part p, double k, double[][] q, double[] inside, double u0, double u1, double v0, double v1, double o0, double o1, double bevel, string col, string finish, bool glow = false)
    {
        var n = Normal(q, inside);
        o1 = Math.Max(o1, o0 + 0.012 / k); // (a slab stays over the game's 1 cm at every size)
        var lu = Math.Sqrt(Sq(Sub(q[1], q[0])));
        var lv = Math.Sqrt(Sq(Sub(q[3], q[0])));
        var (bu, bv) = (Math.Min(bevel / lu, (u1 - u0) / 3), Math.Min(bevel / lv, (v1 - v0) / 3));
        var pts = new List<double[]>();
        foreach (var (u, v) in new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) })
            pts.Add(Add(On(q, u, v), n, o0));
        foreach (var (u, v) in new[] { (u0 + bu, v0 + bv), (u1 - bu, v0 + bv), (u1 - bu, v1 - bv), (u0 + bu, v1 - bv) })
            pts.Add(Add(On(q, u, v), n, o1));
        Hull(p, k, pts, col, finish, glow);
    }

    private static double Sq(double[] a) => a[0] * a[0] + a[1] * a[1] + a[2] * a[2];

    /// <summary>The pod's shell faces (L metres): each side's upper and lower facet, each end's upper and lower facet.</summary>
    private static double[][][] Faces()
    {
        var f = new List<double[][]>();
        foreach (var s in new[] { -1.0, 1.0 })
        {
            f.Add([[s * TopW, 0, -TopAt(-1)], [s * TopW, 0, TopAt(1)], [s * BeamW, BeamY, BeamAt(1)], [s * BeamW, BeamY, -BeamAt(-1)]]);
            f.Add([[s * BeamW, BeamY, -BeamAt(-1)], [s * BeamW, BeamY, BeamAt(1)], [s * MouthW, MouthY, MouthL], [s * MouthW, MouthY, -MouthL]]);
        }
        foreach (var e in new[] { -1.0, 1.0 })
        {
            f.Add([[-TopW, 0, e * TopAt(e)], [TopW, 0, e * TopAt(e)], [BeamW, BeamY, e * BeamAt(e)], [-BeamW, BeamY, e * BeamAt(e)]]);
            f.Add([[-BeamW, BeamY, e * BeamAt(e)], [BeamW, BeamY, e * BeamAt(e)], [MouthW, MouthY, e * MouthL], [-MouthW, MouthY, e * MouthL]]);
        }
        return [.. f];
    }

    private static readonly double[] Centre = [0, -0.9, 0];

    // ---- the housing

    private static Part Housing(Size s)
    {
        var k = s.K;
        var p = new Part($"Leg Pod Housing {s.L}", Small, Section,
            $"A landing leg pod's housing ({s.L}): an armoured pod welded under a keel by its top, hollow, with the gantry's rails on its end walls. Hang its two canopy leaves and its gantry on it; the leg stands {Py.G(s.Reach)} m under the pod's top when lowered. A generic ship fitting: it fits any ship.",
            role: "frame", tool: "welder", material: "steel", color: Skin, group: Group);
        // Collision: the top plate, the two side walls and the two end walls round the hollow.
        Hidden(p, k, -TopW, Ceil, -TopAt(-1), TopW, 0, TopAt(1));
        foreach (var sx in new[] { -1.0, 1.0 })
            Hidden(p, k, sx * InW, MouthY, -InL, sx * 0.9, Ceil, InL);
        foreach (var e in new[] { -1.0, 1.0 })
            Hidden(p, k, -InW, MouthY, e * InL, InW, Ceil, e * 1.65);
        // The skin: eight facets, plated, pale; the top plate's underside dark (the bay's ceiling).
        var faces = Faces();
        foreach (var q in faces)
            Slab(p, k, q, Centre, 0, 1, 0, 1, -PlateT, 0, 0, Skin, "painted plating");
        D(p, k, -TopW + 0.02, -0.06, -TopAt(-1) + 0.02, TopW - 0.02, -0.004, TopAt(1) - 0.02, Armour2, "plated steel"); // (the mount plate on the keel)
        // Armour slabs proud of the skin: a pattern on each side's lower facet (both sides the same), a slab on each end's lower facet.
        for (var i = 0; i < 4; i++)
        {
            var side = faces[i];
            var lower = i % 2 == 1;
            if (lower)
            {
                Slab(p, k, side, Centre, 0.05, 0.47, 0.08, 0.72, 0.004, 0.07, 0.04, Armour, "plated steel");
                Slab(p, k, side, Centre, 0.53, 0.95, 0.08, 0.72, 0.004, 0.07, 0.04, Armour2, "plated steel");
                Slab(p, k, side, Centre, 0.6, 0.88, 0.2, 0.6, 0.07, 0.11, 0.03, Armour, "plated steel"); // (a second step on the aft slab)
                Slab(p, k, side, Centre, 0.12, 0.4, 0.22, 0.55, 0.07, 0.095, 0.02, Blue, "painted plating"); // (the IT blue panel)
                // Hazard chevrons along the mouth.
                for (var c = 0; c < 9; c++)
                {
                    var u = 0.08 + c * 0.1;
                    Slab(p, k, side, Centre, u, u + 0.05, 0.8, 0.97, 0.004, 0.02, 0, c % 2 == 0 ? Hazard : Frame, c % 2 == 0 ? "hazard paint" : "dirty steel");
                }
            }
            else
            {
                Slab(p, k, side, Centre, 0.04, 0.96, 0.15, 0.85, 0.004, 0.045, 0.03, Skin2, "plated steel");
                Slab(p, k, side, Centre, 0.25, 0.75, 0.35, 0.7, 0.045, 0.07, 0.02, Armour2, "plated steel");
            }
        }
        for (var i = 4; i < 8; i++)
        {
            if (i % 2 == 1)
            {
                Slab(p, k, faces[i], Centre, 0.12, 0.88, 0.1, 0.7, 0.004, 0.07, 0.04, Armour, "plated steel");
                Slab(p, k, faces[i], Centre, 0.3, 0.7, 0.78, 0.95, 0.004, 0.02, 0, Hazard, "hazard paint");
            }
            else
                Slab(p, k, faces[i], Centre, 0.1, 0.9, 0.2, 0.8, 0.004, 0.04, 0.02, Skin2, "plated steel");
        }
        // The beam line: a dark chine rail along each side and round the ends, a link-blue light strip under it.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            double zf = -BeamAt(-1) - 0.03, za = BeamAt(1) + 0.03;
            Hull(p, k, [[sx * (BeamW - 0.04), BeamY + 0.05, zf], [sx * (BeamW + 0.05), BeamY + 0.03, zf], [sx * (BeamW + 0.05), BeamY - 0.04, zf], [sx * (BeamW - 0.04), BeamY - 0.06, zf],
                [sx * (BeamW - 0.04), BeamY + 0.05, za], [sx * (BeamW + 0.05), BeamY + 0.03, za], [sx * (BeamW + 0.05), BeamY - 0.04, za], [sx * (BeamW - 0.04), BeamY - 0.06, za]], Frame, "dirty steel");
            var lo = faces[sx < 0 ? 1 : 3];
            Slab(p, k, lo, Centre, 0.06, 0.94, 0.045, 0.07, 0.004, 0.022, 0, Glow, "", glow: true);
        }
        foreach (var e in new[] { -1.0, 1.0 })
            Hull(p, k, [[-BeamW, BeamY + 0.05, e * (BeamAt(e) - 0.04)], [BeamW, BeamY + 0.05, e * (BeamAt(e) - 0.04)], [-BeamW, BeamY - 0.06, e * (BeamAt(e) - 0.04)], [BeamW, BeamY - 0.06, e * (BeamAt(e) - 0.04)],
                [-BeamW + 0.01, BeamY + 0.03, e * (BeamAt(e) + 0.05)], [BeamW - 0.01, BeamY + 0.03, e * (BeamAt(e) + 0.05)], [-BeamW + 0.01, BeamY - 0.04, e * (BeamAt(e) + 0.05)], [BeamW - 0.01, BeamY - 0.04, e * (BeamAt(e) + 0.05)]], Frame, "dirty steel");
        // The amber lamp on the bow end, under the rail, and a stencil plate beside it.
        Slab(p, k, faces[5], Centre, 0.44, 0.56, 0.06, 0.16, 0.004, 0.05, 0.01, Amber, "", glow: true);
        Slab(p, k, faces[5], Centre, 0.6, 0.82, 0.06, 0.14, 0.004, 0.015, 0, Frame, "painted plating");
        // Inside: the bay's lining (dark, on the walls' inner faces), the ceiling with its light strip, the rails on the end walls.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            D(p, k, sx * (InW + 0.004), MouthY + 0.02, -InL + 0.02, sx * (InW + 0.044), Ceil - 0.02, InL - 0.02, Lining, "dirty steel");
            for (var z = -1.0; z <= 1.01; z += 0.5) // (ribs)
                D(p, k, sx * (InW - 0.04), MouthY + 0.04, z - 0.04, sx * (InW + 0.004), Ceil - 0.04, z + 0.04, Frame, "dirty steel");
        }
        foreach (var e in new[] { -1.0, 1.0 })
            D(p, k, -InW + 0.03, MouthY + 0.02, e * (InL + 0.004), InW - 0.03, Ceil - 0.02, e * (InL + 0.044), Lining, "dirty steel");
        D(p, k, -0.05, -0.09, -1.1, 0.05, -0.064, 1.1, Glow, "", glow: true);
        foreach (var e in new[] { -1.0, 1.0 })
            foreach (var x in new[] { -0.4, 0.4 })
            {
                // A rail: a chrome flat on a dark web, toothed down its face (the gantry's trolleys climb it).
                D(p, k, x - 0.06, MouthY + 0.01, e * (InL - 0.03), x + 0.06, Ceil - 0.01, e * InL, Frame, "dirty steel");
                D(p, k, x - 0.035, MouthY + 0.02, e * (InL - 0.055), x + 0.035, Ceil - 0.02, e * (InL - 0.03), Chrome, "chrome");
                for (var y = MouthY + 0.08; y < Ceil - 0.06; y += 0.1)
                    D(p, k, x - 0.05, y, e * (InL - 0.042), x - 0.035, y + 0.04, e * (InL - 0.03), Hazard, "hazard paint");
                D(p, k, x - 0.08, MouthY + 0.01, e * (InL - 0.07), x + 0.08, MouthY + 0.06, e * InL, Hazard, "hazard paint"); // (the stop at its foot)
            }
        p.Sock("top", [0, 0, 0], "+y");
        p.Sock("gantry", Pt(k, 0, Ceil, 0), "-y");
        p.Sock("foot", Pt(k, InW, Guide, StrutZ), "-x");
        p.Sock("canopyL", Pt(k, -0.62, MouthY, 0), "-y");
        p.Sock("canopyR", Pt(k, 0.62, MouthY, 0), "-y");
        return p.Stats(s.HousingMass, s.Health, 30, k >= 1 ? 6.0 : 4.0);
    }

    // ---- a canopy leaf

    private static Part Canopy(Size s, int side)
    {
        var k = s.K;
        var sx = (double)side;
        var name = $"Leg Pod Canopy {(side < 0 ? "Port" : "Stbd")} {s.L}";
        var p = new Part(name, Small, Section,
            $"One leaf of a landing leg pod's canopy ({s.L}, {(side < 0 ? "port" : "starboard")}): an armoured plate under the pod, hinged along its outboard edge. It swings down and out of the leg's way; wire its Open port with the gantry's. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        // Shut, the two leaves make a shallow V under the pod: the inner edge 0.16 m lower than the hinge.
        const double In = 0.005, Lz = MouthL, Ez = MouthL - 0.1;
        double[] P(double x, double y, double z) => [sx * x, y, z];
        Hull(p, k, [P(In, MouthY, -Lz), P(Hinge, MouthY, -Lz), P(Hinge, MouthY, Lz), P(In, MouthY, Lz),
            P(Hinge, MouthY - 0.06, -Lz + 0.03), P(Hinge, MouthY - 0.06, Lz - 0.03), P(In, MouthY - 0.16, -Ez), P(In, MouthY - 0.16, Ez)], Armour2, "plated steel");
        var under = new[] { P(Hinge, MouthY - 0.06, -Lz + 0.03), P(In, MouthY - 0.16, -Ez), P(In, MouthY - 0.16, Ez), P(Hinge, MouthY - 0.06, Lz - 0.03) };
        double[] above = [0, 0, 0];
        Slab(p, k, under, above, 0.1, 0.84, 0.06, 0.45, 0.004, 0.06, 0.035, Armour, "plated steel");
        Slab(p, k, under, above, 0.1, 0.84, 0.55, 0.94, 0.004, 0.06, 0.035, Armour, "plated steel");
        Slab(p, k, under, above, 0.3, 0.62, 0.62, 0.86, 0.06, 0.08, 0.015, Blue, "painted plating");
        // Hazard stripes on the leading (inner) edge, hinge knuckles on the outboard edge.
        for (var c = 0; c < 7; c++)
        {
            var v = 0.04 + c * 0.135;
            Slab(p, k, under, above, 0.88, 0.99, v, v + 0.07, 0.004, 0.025, 0, c % 2 == 0 ? Hazard : Frame, c % 2 == 0 ? "hazard paint" : "dirty steel");
        }
        foreach (var z in new[] { -1.1, 0.0, 1.1 })
            D(p, k, sx * (Hinge - 0.07), MouthY - 0.075, z - 0.14, sx * (Hinge + 0.004), MouthY - 0.005, z + 0.14, Frame, "dirty steel");
        // The inside face: a dark stiffener grid.
        foreach (var z in new[] { -0.75, 0.0, 0.75 })
            D(p, k, sx * 0.08, MouthY + 0.004, z - 0.03, sx * (Hinge - 0.08), MouthY + 0.03, z + 0.03, Frame, "dirty steel");
        Hidden(p, k, sx * In, MouthY - 0.06, -Lz, sx * Hinge, MouthY, Lz);
        p.Sock("hang", Pt(k, sx * 0.62, MouthY, 0), "+y");
        // It turns about its bottom outboard corner, the inner edge down (right hand about +z: +90 on starboard, -90 on port).
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+z"), ("pivot", new List<object?> { Py.Round(sx * Hinge * k, 4), Py.Round((MouthY - 0.06) * k, 4), 0.0 }), ("angle", 90.0 * side), ("seconds", 1.5), ("hang", "hang"))));
        return p.Stats(s.LeafMass, s.Health * 0.4, 30, 4.0);
    }

    // ---- the gantry and its leg
    //
    // Two event doors hung on the housing make the leg telescope: the gantry (its carriage on the rails, the strut's sleeve and the two
    // braces) slides CarTravel down, and the foot (the chrome ram inside the sleeve, the ankle and the pad) slides CarTravel + RamOut,
    // so the ram runs RamOut out of the sleeve as the carriage comes down. Stowed, the whole leg is inside the pod over the canopy.

    private const double CarTop = Ceil - 0.004, CarBot = -0.4, CarL = 1.36, CarW = 0.3, CarTravel = 1.15, RamOut = 0.8;
    private const double StrutZ = -0.55, SleeveBot = -1.0, AnkleY = -1.07, PadTop = -1.15, PadSole = -1.31, PadR = 0.52, RamTop = -0.13, Guide = -1.2;

    private static Part Gantry(Size s)
    {
        var k = s.K;
        var p = new Part($"Leg Pod Gantry {s.L}", Small, Section,
            $"A landing leg pod's gantry ({s.L}): a carriage on the pod's rails carrying the leg's sleeve and two raked braces. It slides down {Py.G(s.Travel)} m as the foot's ram runs out of the sleeve, and back up to stow; open the canopy first. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Hazard, group: Group);
        // The carriage: a yellow box beam across the bay, dark trolleys at each end on the rails, a drive motor on each trolley.
        Hull(p, k, [[-CarW, CarTop, -CarL + 0.1], [CarW, CarTop, -CarL + 0.1], [-CarW, CarTop, CarL - 0.1], [CarW, CarTop, CarL - 0.1],
            [-CarW + 0.06, CarBot, -CarL + 0.16], [CarW - 0.06, CarBot, -CarL + 0.16], [-CarW + 0.06, CarBot, CarL - 0.16], [CarW - 0.06, CarBot, CarL - 0.16]], Hazard, "hazard paint");
        foreach (var e in new[] { -1.0, 1.0 })
        {
            D(p, k, -0.52, CarTop - 0.02, e * (CarL - 0.14), 0.52, CarBot + 0.02, e * CarL, Frame, "dirty steel");
            foreach (var x in new[] { -0.4, 0.4 })
            {
                D(p, k, x - 0.07, CarTop - 0.05, e * CarL, x + 0.07, CarBot + 0.05, e * (CarL + 0.03), Armour2, "steel"); // (the shoe on the rail)
                D(p, k, x - 0.05, CarTop - 0.09, e * (CarL - 0.14), x + 0.05, CarTop - 0.03, e * (CarL - 0.004), Chrome, "chrome");
            }
            D(p, k, -0.2, CarTop - 0.03, e * (CarL - 0.42), 0.2, CarBot + 0.04, e * (CarL - 0.15), Armour, "plated steel"); // (the motor)
            D(p, k, -0.14, CarTop - 0.05, e * (CarL - 0.38), 0.14, CarTop - 0.03, e * (CarL - 0.2), Glow, "", glow: true);
        }
        foreach (var sx in new[] { -1.0, 1.0 })
            for (var z = -0.9; z <= 0.91; z += 0.3)
                D(p, k, sx * (CarW - 0.004), CarTop - 0.08, z - 0.06, sx * (CarW + 0.012), CarBot + 0.1, z + 0.06, Frame, "dirty steel");
        // The sleeve: graphite, a dark mount under the carriage, a hazard collar at its mouth.
        Up(p, k, 0, StrutZ, 0.19, CarBot - 0.1, CarBot - 0.004, Frame, "dirty steel");
        Up(p, k, 0, StrutZ, 0.17, SleeveBot + 0.07, CarBot - 0.1, Armour, "plated steel");
        Up(p, k, 0, StrutZ, 0.185, SleeveBot, SleeveBot + 0.07, Hazard, "hazard paint");
        // Two raked braces from the carriage's aft corners down to the sleeve's collar (the sketch's second line).
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            double[] top = [sx * 0.2, CarBot - 0.01, 1.05], foot = [sx * 0.12, SleeveBot + 0.06, StrutZ + 0.16];
            Beam(p, k, top, foot, 0.055, Armour2, "plated steel");
            Beam(p, k, top, Lerp(top, foot, 0.14), 0.07, Hazard, "hazard paint");
            D(p, k, sx * 0.12, CarBot - 0.05, 0.93, sx * 0.28, CarBot - 0.004, 1.17, Frame, "dirty steel"); // (the clevis under the carriage)
        }
        D(p, k, -0.2, SleeveBot + 0.02, StrutZ + 0.1, 0.2, SleeveBot + 0.1, StrutZ + 0.26, Frame, "dirty steel"); // (the braces' collar lug)
        Hidden(p, k, -0.52, CarBot, -CarL, 0.52, CarTop, CarL);
        Hidden(p, k, -0.17, SleeveBot, StrutZ - 0.17, 0.17, CarBot, StrutZ + 0.17);
        p.Sock("hang", Pt(k, 0, CarTop, 0), "+y");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", s.Travel), ("seconds", 2.5), ("hang", "hang"))));
        return p.Stats(s.GantryMass * 0.6, s.Health * 0.6, 30, 4.0);
    }

    private static Part Foot(Size s)
    {
        var k = s.K;
        var p = new Part($"Leg Pod Foot {s.L}", Small, Section,
            $"A landing leg pod's foot ({s.L}): the chrome ram that runs out of the gantry's sleeve, a torque link, the ankle and a broad pad. It slides down {Py.G(s.FootTravel)} m with the gantry to stand the pad {Py.G(s.Reach)} m under the pod's top. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        Up(p, k, 0, StrutZ, 0.11, AnkleY + 0.05, RamTop, Chrome, "chrome");
        Up(p, k, 0, StrutZ, 0.125, AnkleY + 0.05, AnkleY + 0.13, Frame, "dirty steel"); // (the ram's foot gland)
        // The torque link: two arms from the sleeve's side to the ankle, folded (drawn on the ram's foot, in front).
        D(p, k, -0.03, AnkleY + 0.13, StrutZ - 0.3, 0.03, AnkleY + 0.32, StrutZ - 0.24, Frame, "dirty steel");
        D(p, k, -0.03, AnkleY + 0.04, StrutZ - 0.3, 0.03, AnkleY + 0.13, StrutZ - 0.11, Frame, "dirty steel");
        p.Add(Box(Pt(k, -0.14, AnkleY - 0.12, StrutZ - 0.14), Pt(k, 0.14, AnkleY + 0.12, StrutZ + 0.14), Frame, detail: true, shape: Kw(("shape", "sphere"), ("segments", 12), ("finish", "dirty steel"))));
        D(p, k, -0.08, PadTop + 0.06, StrutZ - 0.24, 0.08, AnkleY - 0.06, StrutZ + 0.24, Frame, "dirty steel"); // (the ankle's yoke)
        // The pad: an eight-sided armoured foot, a rubber sole, a hazard ring round its edge.
        p.Add(Box(Pt(k, -PadR, PadTop, StrutZ - PadR), Pt(k, PadR, PadTop + 0.06, StrutZ + PadR), Armour, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"), ("taper", Py.L(0.7, 0.7)), ("finish", "plated steel"))));
        Up(p, k, 0, StrutZ, PadR, PadSole + 0.05, PadTop, Armour2, "plated steel", 8);
        Up(p, k, 0, StrutZ, PadR + 0.012, PadSole + 0.07, PadTop - 0.04, Hazard, "hazard paint", 8);
        Up(p, k, 0, StrutZ, PadR - 0.04, PadSole, PadSole + 0.05, Rubber, "rubber", 8);
        Hidden(p, k, -PadR * 0.92, PadSole, StrutZ - PadR * 0.92, PadR * 0.92, PadTop + 0.06, StrutZ + PadR * 0.92);
        // A guide shoe off the pad's starboard edge that runs down the bay's wall: what the foot hangs by.
        D(p, k, PadR * 0.9, Guide - 0.05, StrutZ - 0.06, InW - 0.004, Guide + 0.05, StrutZ + 0.06, Frame, "dirty steel");
        Hidden(p, k, PadR * 0.92, Guide - 0.05, StrutZ - 0.06, InW, Guide + 0.05, StrutZ + 0.06);
        p.Sock("hang", Pt(k, InW, Guide, StrutZ), "+x");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-y"), ("travel", s.FootTravel), ("seconds", 3.5), ("hang", "hang"))));
        return p.Stats(s.GantryMass * 0.4, s.Health * 0.6, 30, 4.0);
    }

    /// <summary>A square beam of half side <paramref name="r"/> from a to b (L metres).</summary>
    private static void Beam(Part p, double k, double[] a, double[] b, double r, string col, string finish)
    {
        var ax = Unit(Sub(b, a));
        var side = Unit(Cross(ax, [1, 0, 0]));
        double[] across = [1, 0, 0];
        var pts = new List<double[]>();
        foreach (var end in new[] { a, b })
            foreach (var (u, v) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                pts.Add(Add(Add(end, across, u * r), side, v * r));
        Hull(p, k, pts, col, finish);
    }

    /// <summary>A cylinder standing along y at (x, z), L metres.</summary>
    private static void Up(Part p, double k, double x, double z, double r, double y0, double y1, string col, string finish, int segments = 16) =>
        p.Add(Box(Pt(k, x - r, Math.Min(y0, y1), z - r), Pt(k, x + r, Math.Max(y0, y1), z + r), col, detail: true,
            shape: Kw(("shape", "cylinder"), ("segments", segments), ("front", "+y"), ("finish", finish))));

    // ---- the pod assembled (a blueprint a size, for the galleries and to spawn one)

    private static PyDict Assembly(Size s, Dictionary<string, PyDict> defs)
    {
        var bp = new BuildingParts.Blueprint($"Landing Leg Pod {s.L}",
            $"A landing leg pod ({s.L}) assembled: the housing, its two canopy leaves and the gantry with its leg, stowed. Weld the housing's top under a keel; open the canopy, then lower the gantry.", defs);
        var h = bp.First($"Leg Pod Housing {s.L}");
        bp.Hang($"Leg Pod Canopy Port {s.L}", h, "canopyL", "hang");
        bp.Hang($"Leg Pod Canopy Stbd {s.L}", h, "canopyR", "hang");
        bp.Hang($"Leg Pod Gantry {s.L}", h, "gantry", "hang");
        bp.Hang($"Leg Pod Foot {s.L}", h, "foot", "hang");
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var s in Sizes)
        {
            var defs = new Dictionary<string, PyDict>();
            foreach (var part in new[] { Housing(s), Canopy(s, -1), Canopy(s, 1), Gantry(s), Foot(s) })
            {
                var d = part.Json();
                var name = (string)d["name"]!;
                defs.Add(name, d);
                o.File($"{Out}/{FileName(name)}", PyJson.Dumps(d, 2) + "\n");
            }
            o.File($"{BpOut}/LandingLegPod{s.L}.json", PyJson.Dumps(Assembly(s, defs), 2) + "\n");
        }
    }
}
