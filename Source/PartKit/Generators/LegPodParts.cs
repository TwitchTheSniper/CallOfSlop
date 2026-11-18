using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Landing leg pods (Karl, 2026-11-18: "They should be in their own pod underneath the ship. When you press a button to action them
/// from a connected seat, the canopy opens, and the end of two points is on a gantry inside, which move the landing feet out of the
/// canopy ... in various sizes, following the same design language of the Zero Day style of ships ... start from scratch").
///
/// Each size is six parts that build one pod: the <b>housing</b> (a faceted armoured pod welded under a keel by its top, hollow, the
/// carriage's rails along its side walls and the strut's hinge bracket under its ceiling at the bow end), two <b>canopy</b> leaves
/// (event doors that swing down and out about their outboard edges, a shallow V under the pod when shut), the <b>strut</b> (a leg
/// folded flat along the pod, hinged at the bow end, that swings down a quarter turn to stand upright over its pad), the
/// <b>carriage</b> (the gantry: a slide door that runs along the rails) and the <b>brace</b> (two raked beams from the carriage to
/// the strut's knee). Karl's rework (2026-11-18): the leg folds lengthways on two hinges and, as it opens, the gantry it rides on
/// runs to the other side. The brace and carriage carry a door "link" block (ConstructDoors.CDoorLink) so they are drawn as a
/// crank and slider on the way; both still settle by a quarter turn or a slide. The order (canopy, then leg; leg, then canopy) is
/// the wiring's.
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
        public double Reach => Py.Round((-CrankY + StrutLen) * K, 2);
    }

    private static readonly Size[] Sizes =
    [
        new("S", 0.5, 220, 140, 40, 1500),
        new("M", 0.75, 700, 420, 110, 3500),
        new("L", 1.0, 1500, 900, 240, 6000),
    ];

    // The pod in L metres (scaled by K): top half width and half length, the beam's height and half width, the mouth's.
    private const double TopW = 0.85, TopL = 2.05, BeamY = -0.45, BeamW = 0.95, MouthY = -1.35, MouthW = 0.68, MouthL = 1.8;

    /// <summary>The top's and the beam's half length towards end e (-1 the bow, raked out further: a chisel nose; +1 the stern).</summary>
    private static double TopAt(double e) => e < 0 ? 2.45 : TopL;
    private static double BeamAt(double e) => e < 0 ? 2.35 : 2.1;
    private const double InW = 0.6, InL = 1.75, Ceil = -0.12; // the hollow inside: half width, half length (the rails' face), the ceiling
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
            for (var z = -1.5; z <= 1.51; z += 0.5) // (ribs, above the carriage's rail)
                D(p, k, sx * (InW - 0.04), Pin0.Y + 0.14, z - 0.04, sx * (InW + 0.004), Ceil - 0.04, z + 0.04, Frame, "dirty steel");
            // The carriage's rail along the wall: a chrome flat on a dark web, hazard stops at each end of its run.
            D(p, k, sx * (InW - 0.012), Pin0.Y - 0.1, -InL + 0.03, sx * (InW + 0.004), Pin0.Y + 0.1, InL - 0.03, Frame, "dirty steel");
            D(p, k, sx * (InW - 0.022), Pin0.Y + CarH + 0.068, Pin1.Z - CarHalf - 0.1, sx * (InW - 0.008), Pin0.Y + CarH + 0.088, Pin0.Z + CarHalf + 0.1, Chrome, "chrome");
            foreach (var zs in new[] { Pin1.Z - CarHalf - 0.08, Pin0.Z + CarHalf + 0.02 })
                D(p, k, sx * (InW - 0.05), Pin0.Y - 0.08, zs, sx * (InW - 0.008), Pin0.Y + 0.08, zs + 0.06, Hazard, "hazard paint");
        }
        foreach (var e in new[] { -1.0, 1.0 })
            D(p, k, -InW + 0.03, MouthY + 0.02, e * (InL + 0.004), InW - 0.03, Ceil - 0.02, e * (InL + 0.044), Lining, "dirty steel");
        D(p, k, -0.05, -0.09, -1.4, 0.05, -0.064, 1.4, Glow, "", glow: true);
        // The strut's hinge bracket under the ceiling at the bow end: two cheeks and a block between them, a hazard band.
        Hidden(p, k, -0.3, CrankY + StrutR, CrankZ - 0.2, 0.3, Ceil, CrankZ + 0.2);
        D(p, k, -0.3, CrankY + StrutR + 0.004, CrankZ - 0.2, 0.3, Ceil - 0.004, CrankZ + 0.2, Armour, "plated steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            D(p, k, sx * 0.21, CrankY - 0.12, CrankZ - 0.16, sx * 0.3, CrankY + StrutR + 0.004, CrankZ + 0.16, Frame, "dirty steel");
        D(p, k, -0.305, CrankY + StrutR + 0.05, CrankZ - 0.205, 0.305, CrankY + StrutR + 0.11, CrankZ + 0.205, Hazard, "hazard paint");
        p.Sock("top", [0, 0, 0], "+y");
        p.Sock("strut", Pt(k, 0, CrankY + StrutR, CrankZ), "-y");
        p.Sock("carriage", Pt(k, InW, Pin0.Y, Pin0.Z + 0.08), "-x");
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

    // ---- the leg: a strut on a hinge, a brace and the carriage its top rides (Karl, 2026-11-18: "with the two hinges, lengthways. As it
    // opens up, the gantry it's on goes to the other side")
    //
    // Stowed, the strut lies along the pod, hinged at its bow end (Crank), its pad standing on edge at the stern end; the two braces
    // run from its knee forward and down to a pin on the carriage, which sits on rails along the bay's walls aft of the hinge. Deploying,
    // the strut swings a quarter turn down about its hinge while the carriage runs forward past the hinge (a crank and slider: the brace's
    // length sets where the carriage is), and the brace turns a quarter turn too, so every piece settles square. The geometry is solved
    // so the brace's quarter turn joins the knee and the pin at both ends (Alpha: the brace's lean below the strut, stowed).

    private const double CrankY = -0.65, CrankZ = -1.0, KneeAt = 1.6, StrutLen = 2.6, StrutR = 0.17, PadR = 0.47, PadT = 0.16, BraceX = 0.25;
    private const double Alpha = -30 * Math.PI / 180;
    private static double Rod => KneeAt / (Math.Cos(Alpha) - Math.Sin(Alpha)); // the brace's length, pin to pin
    private static (double Y, double Z) Knee0 => (CrankY, CrankZ + KneeAt);
    private static (double Y, double Z) Knee1 => (CrankY - KneeAt, CrankZ);
    private static (double Y, double Z) Pin0 => (Knee0.Y + Rod * Math.Sin(Alpha), Knee0.Z - Rod * Math.Cos(Alpha));
    private static (double Y, double Z) Pin1 => (Knee1.Y + Rod * Math.Cos(Alpha), Knee1.Z + Rod * Math.Sin(Alpha));
    private static double CarTravel => Pin0.Z - Pin1.Z; // (forward, along -z)
    private const double CarHalf = 0.12, CarH = 0.065, CarMid = 0.3, CarShoe = 0.45; // the carriage: half length along the rail, half height round the pin, where its beam ends and its shoes start

    /// <summary>The fixed point of the brace's quarter turn about +x (stowed to deployed): where its settle turns it.</summary>
    private static (double Y, double Z) BracePivot()
    {
        // A quarter turn about +x: y' = Py - (z - Pz), z' = Pz + (y - Py); solved for Knee0 -> Knee1.
        var (y0, z0) = Knee0; var (y1, z1) = Knee1;
        var pz = (y1 + z0 + z1 - y0) / 2;
        var py = (y1 + z0 - z1 + y0) / 2;
        return (py, pz);
    }

    private static List<object?> V3(double k, double x, double y, double z) => [Py.Round(x * k, 4), Py.Round(y * k, 4), Py.Round(z * k, 4)];

    private static PyDict LinkBlock(double k) => Kw(("crank", V3(k, 0, CrankY, CrankZ)), ("crankAxis", "+x"), ("crankAngle", 90),
        ("knee", V3(k, 0, Knee0.Y, Knee0.Z)), ("pin", V3(k, 0, Pin0.Y, Pin0.Z)), ("rail", "-z"));

    /// <summary>A cylinder along z at (x, y), L metres (a stowed strut's).</summary>
    private static void Along(Part p, double k, double x, double y, double r, double z0, double z1, string col, string finish, int segments = 16) =>
        p.Add(Box(Pt(k, x - r, y - r, Math.Min(z0, z1)), Pt(k, x + r, y + r, Math.Max(z0, z1)), col, detail: true,
            shape: Kw(("shape", "cylinder"), ("segments", segments), ("finish", finish))));

    /// <summary>A cylinder along x at (y, z), L metres (a hinge pin's).</summary>
    private static void Across(Part p, double k, double y, double z, double r, double x0, double x1, string col, string finish) =>
        p.Add(Box(Pt(k, Math.Min(x0, x1), y - r, z - r), Pt(k, Math.Max(x0, x1), y + r, z + r), col, detail: true,
            shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"), ("finish", finish))));

    private static Part Strut(Size s)
    {
        var k = s.K;
        var p = new Part($"Leg Pod Strut {s.L}", Small, Section,
            $"A landing leg pod's strut ({s.L}): a graphite sleeve and chrome ram on a broad pad, hinged at the pod's bow end. Stowed it lies along the pod; it swings a quarter turn down to stand the pad {Py.G(s.Reach)} m under the pod's top, its brace and carriage with it. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Armour, group: Group);
        double y = CrankY, z0 = CrankZ, tip = CrankZ + StrutLen;
        // The knuckle on its hinge pin, the sleeve to past the knee, a hazard collar, the chrome ram out to the ankle.
        Across(p, k, y, z0, 0.2, -0.2, 0.2, Frame, "dirty steel");
        Along(p, k, 0, y, StrutR, z0 + 0.1, Knee0.Z + 0.25, Armour, "plated steel");
        Along(p, k, 0, y, StrutR + 0.015, Knee0.Z + 0.25, Knee0.Z + 0.33, Hazard, "hazard paint");
        Along(p, k, 0, y, 0.11, Knee0.Z + 0.33, tip - PadT - 0.16, Chrome, "chrome");
        // The knee: a lug either side for the braces' pins.
        foreach (var sx in new[] { -1.0, 1.0 })
            D(p, k, sx * (StrutR - 0.02), y - 0.1, Knee0.Z - 0.12, sx * (BraceX - 0.06), y + 0.1, Knee0.Z + 0.12, Frame, "dirty steel");
        Across(p, k, y, Knee0.Z, 0.05, -BraceX + 0.05, BraceX - 0.05, Chrome, "chrome");
        // The ankle and the pad, the pad's sole facing aft while stowed (down when deployed).
        p.Add(Box(Pt(k, -0.14, y - 0.14, tip - PadT - 0.28), Pt(k, 0.14, y + 0.14, tip - PadT), Frame, detail: true, shape: Kw(("shape", "sphere"), ("segments", 12), ("finish", "dirty steel"))));
        p.Add(Box(Pt(k, -PadR, y - PadR, tip - PadT), Pt(k, PadR, y + PadR, tip - PadT + 0.06), Armour, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 8), ("taper", Py.L(0.7, 0.7)), ("finish", "plated steel"))));
        Along(p, k, 0, y, PadR, tip - PadT + 0.06, tip - 0.05, Armour2, "plated steel", 8);
        Along(p, k, 0, y, PadR + 0.012, tip - 0.09, tip - 0.06, Hazard, "hazard paint", 8);
        Along(p, k, 0, y, PadR - 0.04, tip - 0.05, tip, Rubber, "rubber", 8);
        // Collision: the strut and the pad (a hinge leaf turns about its pin; the box behind the pin turns up into the bay).
        Hidden(p, k, -StrutR, y - StrutR, z0 - 0.15, StrutR, y + StrutR, tip - PadT);
        Hidden(p, k, -PadR * 0.92, y - PadR * 0.92, tip - PadT, PadR * 0.92, y + PadR * 0.92, tip);
        p.Sock("hang", Pt(k, 0, y + StrutR, z0), "+y");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("pivot", V3(k, 0, y, z0)), ("angle", 90), ("seconds", 3.5), ("hang", "hang"))));
        return p.Stats(s.GantryMass * 0.6, s.Health * 0.6, 30, 4.0);
    }

    private static Part Brace(Size s)
    {
        var k = s.K;
        var p = new Part($"Leg Pod Brace {s.L}", Small, Section,
            $"A landing leg pod's brace ({s.L}): two raked bars from the strut's knee to a pin on the carriage. As the strut swings down the carriage runs forward and the brace turns with them, locking the leg upright. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Armour2, group: Group);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            double[] knee = [sx * BraceX, Knee0.Y, Knee0.Z], pin = [sx * BraceX, Pin0.Y, Pin0.Z];
            Beam(p, k, Lerp(knee, pin, 0.04), Lerp(pin, knee, 0.04), 0.05, Armour2, "plated steel");
            Beam(p, k, Lerp(pin, knee, 0.03), Lerp(pin, knee, 0.16), 0.064, Hazard, "hazard paint");
            Across(p, k, Knee0.Y, Knee0.Z, 0.075, sx * (BraceX - 0.05), sx * (BraceX + 0.05), Frame, "dirty steel");
            Across(p, k, Pin0.Y, Pin0.Z, 0.075, sx * (BraceX - 0.05), sx * (BraceX + 0.05), Frame, "dirty steel");
        }
        // The pin through the carriage, out to its shoes, and the pin's boss on the carriage that the brace hangs by.
        Across(p, k, Pin0.Y, Pin0.Z, 0.035, -InW + 0.02, InW - 0.02, Chrome, "chrome");
        Hidden(p, k, CarMid, Pin0.Y - 0.05, Pin0.Z - 0.05, CarShoe, Pin0.Y + 0.05, Pin0.Z + 0.05);
        var mid = Lerp([0, Knee0.Y, Knee0.Z], [0, Pin0.Y, Pin0.Z], 0.5);
        Hidden(p, k, -0.05, mid[1] - 0.05, mid[2] - 0.05, 0.05, mid[1] + 0.05, mid[2] + 0.05);
        p.Sock("hang", Pt(k, CarMid, Pin0.Y, Pin0.Z), "-x");
        var (py, pz) = BracePivot();
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+x"), ("pivot", V3(k, 0, py, pz)), ("angle", 90), ("seconds", 3.5), ("hang", "hang"), ("link", LinkBlock(k)))));
        return p.Stats(s.GantryMass * 0.15, s.Health * 0.5, 30, 4.0);
    }

    private static Part Carriage(Size s)
    {
        var k = s.K;
        var p = new Part($"Leg Pod Carriage {s.L}", Small, Section,
            $"A landing leg pod's carriage ({s.L}): a yellow cross beam on the bay's rails carrying the brace's pin. It runs {Py.G(Py.Round(CarTravel * k, 2))} m forward as the strut swings down, and back to stow. A generic ship fitting: it fits any ship.",
            role: "door", tool: "welder", material: "steel", color: Hazard, group: Group);
        var (y, z) = Pin0;
        D(p, k, -InW + 0.07, y - CarH, z - CarHalf + 0.02, InW - 0.07, y + CarH, z + CarHalf - 0.02, Hazard, "hazard paint");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            D(p, k, sx * (InW - 0.07), y - CarH - 0.02, z - CarHalf, sx * (InW - 0.018), y + CarH + 0.02, z + CarHalf, Frame, "dirty steel"); // (the shoe on the rail)
            Across(p, k, y + CarH + 0.035, z, 0.03, sx * (InW - 0.06), sx * (InW - 0.02), Chrome, "chrome"); // (its roller)
            D(p, k, sx * 0.3, y + CarH, z - 0.05, sx * 0.38, y + CarH + 0.06, z + 0.05, Glow, "", glow: true);
            for (var c = 0; c < 3; c++)
                D(p, k, sx * (0.1 + c * 0.14), y - CarH - 0.012, z - CarHalf + 0.03, sx * (0.16 + c * 0.14), y - CarH + 0.004, z + CarHalf - 0.03, Frame, "dirty steel");
        }
        Hidden(p, k, -CarMid, y - CarH, z - CarHalf, CarMid, y + CarH, z + CarHalf); // (the beam, and a shoe each end: the brace's pin boss sits between, on the starboard side)
        Hidden(p, k, -InW, y - CarH, z - CarHalf, -CarShoe, y + CarH, z + CarHalf);
        Hidden(p, k, CarShoe, y - CarH, z - CarHalf, InW, y + CarH, z + CarHalf);
        Hidden(p, k, -CarShoe, y - CarH, z - CarHalf, -CarMid, y + CarH, z + CarHalf);
        Hidden(p, k, CarMid, y - CarH, z + 0.06, CarShoe, y + CarH, z + CarHalf);
        Hidden(p, k, CarMid, y - CarH, z - CarHalf, CarShoe, y + CarH, z - 0.06);
        p.Sock("hang", Pt(k, InW, y, z + 0.08), "+x");
        p.Sock("brace", Pt(k, CarMid, y, z), "+x");
        p.Set(("door", Kw(("kind", "slide"), ("axis", "-z"), ("travel", Py.Round(CarTravel * k, 4)), ("seconds", 3.5), ("hang", "hang"), ("link", LinkBlock(k)))));
        return p.Stats(s.GantryMass * 0.25, s.Health * 0.5, 30, 4.0);
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
        bp.Hang($"Leg Pod Strut {s.L}", h, "strut", "hang");
        var car = bp.Hang($"Leg Pod Carriage {s.L}", h, "carriage", "hang");
        bp.Hang($"Leg Pod Brace {s.L}", car, "brace", "hang");
        return bp.Json();
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var s in Sizes)
        {
            var defs = new Dictionary<string, PyDict>();
            foreach (var part in new[] { Housing(s), Canopy(s, -1), Canopy(s, 1), Strut(s), Brace(s), Carriage(s) })
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
