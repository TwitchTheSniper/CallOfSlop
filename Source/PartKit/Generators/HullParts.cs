using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Faceted hull parts: Constructs/Parts/Hull (docs/vehiclekit/VEHICLEKIT.md, "Silhouette"). Was tools/constructs/hull_parts.py.
///
/// A walk-in body is a run of 1 m bays, each a slice between two cross-sections, so a roofline steps, a tail narrows and a nose rakes
/// without a wall of plates. A section (Profiles) is a floor edge, a waist, a shoulder and a roof edge: the side breaks twice on the way
/// up, so no side is one flat sheet. Bay names give their sections, front to back: Hull Bay T-L goes from tall to low. Brows close a
/// body's front, Tails its back, the Door Bay holds a side door.
///
/// Facets are "hull" shapes (drawn only, any angle); hidden boxes stand for them to the build rules, the physics and a player's feet.
/// The inside is hollow to all of them. Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// </summary>
public sealed class HullParts : IPartGenerator
{
    public string Name => "hull_parts";

    private const string Out = "Constructs/Parts/Hull";
    private const string Group = "Vehicle Body";
    private const string Hulls = "Hull bays", Guns = "Sponsons and panniers";
    private const string Steel = "#7a7f86", Dark = "#2e3033", Rust = "#4a3a2e", Roof = "#5c5f62";
    private const double T = 0.04; // a facet's thickness

    private static (double X, double Y)[] S(params (double, double)[] p) => p;

    // Half width and height: floor edge, waist, shoulder, roof edge.
    private static readonly Dictionary<string, (double X, double Y)[]> Profiles = new()
    {
        ["T"] = S((1.02, 0.0), (1.1, 0.85), (1.0, 1.95), (0.6, 2.3)),
        ["L"] = S((1.02, 0.0), (1.1, 0.7), (1.0, 1.65), (0.55, 2.02)),
        ["N"] = S((0.82, 0.0), (0.88, 0.85), (0.8, 1.75), (0.45, 2.12)),
    };

    // What a section shrinks to where a body ends: a brow's front, a tail's back.
    private static readonly Dictionary<string, (double X, double Y)[]> Smaller = new()
    {
        ["T"] = S((0.95, 0.0), (0.98, 0.8), (0.82, 1.45), (0.5, 1.72)),
        ["L"] = S((0.95, 0.0), (0.98, 0.65), (0.82, 1.3), (0.48, 1.55)),
        ["N"] = S((0.74, 0.0), (0.78, 0.8), (0.66, 1.4), (0.38, 1.62)),
    };

    private static Part P(string name, string section, string desc, string color = Steel, string role = "panel", string tool = "welder") =>
        new(name, Small, section, desc, role: role, tool: tool, color: color, group: Group);

    private static double Sq(double v) => Math.Pow(v, 0.5);

    /// <summary>
    /// A flat panel through the points, t thick along normal (a unit vector): the hull of the points moved each way. paint false: it keeps
    /// its colour when the part is painted. offsets: each point's own way to move (a mitre), so two panels meeting at an angle share the
    /// joint's plane instead of overlapping there.
    /// </summary>
    private static void Facet(Part p, double[][] points, double[] normal, string color, double t = T, bool paint = true, double[][]? offsets = null)
    {
        var both = new List<double[]>();
        for (var i = 0; i < points.Length; i++)
            foreach (var s in new[] { -1, 1 })
            {
                var n = offsets is not null ? offsets[i] : normal;
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(points[i][k] + s * n[k] * t / 2, 4))]);
            }
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()));
        if (!paint)
            shape["paintable"] = false;
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    private static void Hide(Part p, double[] mn, double[] mx) => p.Add(Box(mn, mx, hidden: true));

    private static void Pad(Part p, string name, double[] at, string d, double size = 0.03)
    {
        int[] v = d switch
        {
            "+x" => [1, 0, 0], "-x" => [-1, 0, 0], "+y" => [0, 1, 0], "-y" => [0, -1, 0], "+z" => [0, 0, 1], _ => [0, 0, -1],
        };
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => at[k] - (v[k] != 0 ? 0 : size) - (v[k] > 0 ? 2 * size : 0))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => at[k] + (v[k] != 0 ? 0 : size) + (v[k] < 0 ? 2 * size : 0))];
        Hide(p, mn, mx);
        p.Sock(name, at, d);
    }

    /// <summary>The outward normal (x, y) of each row of a half section (the +x side), then the roof's (0, 1).</summary>
    private static List<(double X, double Y)> RowNormals((double X, double Y)[] sec)
    {
        var o = new List<(double, double)>();
        for (var k = 0; k < sec.Length - 1; k++)
        {
            var (x0, y0) = sec[k];
            var (x1, y1) = sec[k + 1];
            double nx = y1 - y0, ny = -(x1 - x0);
            var ln = Sq(nx * nx + ny * ny);
            o.Add((nx / ln, ny / ln));
        }
        o.Add((0.0, 1.0));
        return o;
    }

    /// <summary>
    /// Each point's offset for a facet's thickness (+x side): along its row's normal at the floor edge, else on the bisector of the two
    /// rows that meet there, lengthened so each face keeps its own offset (z-fighting, 2026-11-07).
    /// </summary>
    private static List<(double X, double Y)> Mitres((double X, double Y)[] sec)
    {
        var ns = RowNormals(sec);
        var o = new List<(double, double)> { ns[0] };
        for (var k = 1; k < sec.Length; k++)
        {
            var (ax, ay) = ns[k - 1];
            var (bx, by) = ns[k];
            double mx = ax + bx, my = ay + by;
            var ln = Sq(mx * mx + my * my);
            mx /= ln;
            my /= ln;
            var c = mx * ax + my * ay;
            o.Add((mx / c, my / c));
        }
        return o;
    }

    /// <summary>The offset two facets share at a point where they meet (normals n1 and n2, unit): the bisector, lengthened.</summary>
    private static double[] Mitre(double[] n1, double[] n2)
    {
        double[] m = [.. n1.Zip(n2, (x, y) => x + y)];
        var ln = Sq(Py.Sum(m.Select(x => x * x)));
        m = [.. m.Select(x => x / ln)];
        var c = Py.Sum(m.Zip(n1, (x, y) => x * y));
        return [.. m.Select(x => x / c)];
    }

    private static double[] V3((double X, double Y) m, double s = 1) => [s * m.X, m.Y, 0];

    /// <summary>Both sides and the roof between sections a at z0 and b at z1. skip: the +x side is left to the caller; roofPx: the roof's +x edge offset then.</summary>
    private static void Sides(Part p, (double X, double Y)[] a, (double X, double Y)[] b, double z0, double z1, bool skip = false, double[]? roofPx = null)
    {
        string[] colours = [Rust, Steel, Roof];
        var ma = Mitres(a);
        var mb = Mitres(b);
        foreach (var s in skip ? new[] { -1 } : new[] { -1, 1 })
        {
            for (var k = 0; k < 3; k++)
            {
                var (ax0, ay0) = a[k];
                var (ax1, ay1) = a[k + 1];
                var (bx0, by0) = b[k];
                var (bx1, by1) = b[k + 1];
                double[][] q = [[s * ax0, ay0, z0], [s * ax1, ay1, z0], [s * bx1, by1, z1], [s * bx0, by0, z1]];
                double dy = ay1 - ay0, dx = ax1 - ax0;
                double n0 = dy * s, n1 = -dx;
                var ln = Sq(Math.Pow(n0, 2) + Math.Pow(n1, 2));
                double[][] offs = [V3(ma[k], s), V3(ma[k + 1], s), V3(mb[k + 1], s), V3(mb[k], s)];
                Facet(p, q, [n0 / ln, n1 / ln, 0], colours[k], paint: k == 1, offsets: offs);
            }
            // What stands for them: a wall up to the shoulder, a step for the cant.
            var x = (a[1].X + b[1].X + a[2].X + b[2].X) / 4;
            var y = Math.Min(a[2].Y, b[2].Y);
            var (lo, hi) = Sorted(s * (x - 0.03), s * (x + 0.03));
            Hide(p, [lo, 0.0, z0], [hi, y, z1]);
            var x2 = (a[3].X + b[3].X + a[2].X + b[2].X) / 4;
            (lo, hi) = Sorted(s * (x2 - 0.03), s * (x2 + 0.03));
            Hide(p, [lo, y, z0], [hi, Math.Min(a[3].Y, b[3].Y), z1]);
        }
        double[] pa = roofPx ?? [ma[3].X, ma[3].Y], pb = roofPx ?? [mb[3].X, mb[3].Y];
        double[][] roof = [[-ma[3].X, ma[3].Y, 0], [pa[0], pa[1], 0], [pb[0], pb[1], 0], [-mb[3].X, mb[3].Y, 0]];
        Facet(p, [[-a[3].X, a[3].Y, z0], [a[3].X, a[3].Y, z0], [b[3].X, b[3].Y, z1], [-b[3].X, b[3].Y, z1]], [0, 1, 0], Roof, paint: false, offsets: roof);
        double w = Math.Min(a[3].X, b[3].X), yy = Math.Min(a[3].Y, b[3].Y);
        Hide(p, [-w, yy - 0.03, z0], [w, yy + 0.03, z1]);
    }

    private static (double, double) Sorted(double a, double b) => a <= b ? (a, b) : (b, a);

    /// <summary>Feet on a Bolt Deck's grid, a turret's socket on the roof, a sponson's on each waist.</summary>
    private static void Fittings(Part p, (double X, double Y)[] a, (double X, double Y)[] b, double length, bool door = false)
    {
        double[] zs = [0.125, length - 0.125];
        for (var k = 0; k < 2; k++)
        {
            p.Sock($"footL{k}", [-0.75, 0, zs[k]], "-y");
            p.Sock($"footR{k}", [0.75, 0, zs[k]], "-y");
        }
        var w = (a[1].X + b[1].X) / 2;
        var y = (a[1].Y + b[1].Y) / 2;
        Pad(p, "-x0", [-w, y, length / 2], "-x");
        if (!door)
            Pad(p, "+x0", [w, y, length / 2], "+x");
        Pad(p, "top", [0, Math.Min(a[3].Y, b[3].Y) + 0.03, length / 2], "+y");
        Pad(p, "lever", [0, Math.Min(a[3].Y, b[3].Y) - 0.03, length / 2], "-y");
    }

    private static (string, Part) Bay(string front, string back)
    {
        var (a, b) = (Profiles[front], Profiles[back]);
        var name = front == back ? $"Hull Bay {front}" : $"Hull Bay {front}-{back}";
        var p = P(name, Hulls, "A metre of walk-in hull. Bays stand on a bolt deck, one after another.");
        Sides(p, a, b, 0.0, 1.0);
        Fittings(p, a, b, 1.0);
        p.Stats(120, 500, 8, 2.0);
        return ("", p);
    }

    /// <summary>Closes a body's end: the section as a flat plate at z.</summary>
    private static void Cap(Part p, (double X, double Y)[] section, double z, string color = Steel)
    {
        var pts = section.Select(q => new[] { q.X, q.Y, z }).Concat(section.Select(q => new[] { -q.X, q.Y, z })).ToArray();
        Facet(p, pts, [0, 0, 1], color, 0.05);
        Hide(p, [-section[1].X + 0.05, 0, z - 0.03], [section[1].X - 0.05, section[3].Y * 0.9, z + 0.03]);
    }

    /// <summary>A body's front: the sides close in and the top rakes back from the front plate.</summary>
    private static (string, Part) Brow(string name)
    {
        var (a, b) = (Smaller[name], Profiles[name]);
        var p = P($"Hull Brow {name}", Hulls, "A body's raked front: bolts on as the first bay.");
        Sides(p, a, b, 0.0, 1.0);
        Cap(p, a, 0.0);
        Fittings(p, a, b, 1.0);
        p.Stats(150, 600, 10, 2.0);
        return ("", p);
    }

    /// <summary>A body's back: narrower and lower to a flat stern.</summary>
    private static (string, Part) Tail(string name)
    {
        var (a, b) = (Profiles[name], Smaller[name]);
        var p = P($"Hull Tail {name}", Hulls, "A body's tapering back: bolts on as the last bay.");
        Sides(p, a, b, 0.0, 1.0);
        Cap(p, b, 1.0);
        Fittings(p, a, b, 1.0);
        p.Stats(150, 600, 10, 2.0);
        return ("", p);
    }

    /// <summary>1.5 m of tall hull with a 1.25 x 2 m doorway in its +x side, the door's plane at x = Plane.</summary>
    private static (string, Part) DoorBay()
    {
        const double Plane = 1.0;
        var a = Profiles["T"];
        var p = P("Hull Door Bay T", Hulls, "A bay with a doorway in one side. Takes a 1.25x2 m flush door on a Door Hinge.");
        const double L = 1.5;
        const double Top = 2.0;
        double c0 = a[3].Y - Top, c1 = Plane - a[3].X;
        var ln = Sq(Math.Pow(c0, 2) + Math.Pow(c1, 2));
        double[] cn = [c0 / ln, c1 / ln]; // the cant over the doorway, outward
        var atTop = Mitre([1.0, 0.0], cn);
        var atRoof = Mitre(cn, [0.0, 1.0]);
        Sides(p, a, a, 0.0, L, skip: true, roofPx: atRoof);
        // The flat frame round the doorway, then the cant over it.
        foreach (var (z0, z1) in new[] { (0.0, 0.125), (1.375, L) })
        {
            Facet(p, [[Plane, 0.0, z0], [Plane, Top, z0], [Plane, Top, z1], [Plane, 0.0, z1]], [1, 0, 0], Rust,
                offsets: [[1, 0, 0], [atTop[0], atTop[1], 0], [atTop[0], atTop[1], 0], [1, 0, 0]]);
            Hide(p, [Plane - 0.03, 0.0, z0], [Plane + 0.03, Top, z1]);
        }
        double[] o1 = [atTop[0], atTop[1], 0], o2 = [atRoof[0], atRoof[1], 0];
        Facet(p, [[Plane, Top, 0.0], [a[3].X, a[3].Y, 0.0], [a[3].X, a[3].Y, L], [Plane, Top, L]], [cn[0], cn[1], 0], Steel, offsets: [o1, o2, o2, o1]);
        Hide(p, [Plane - 0.03, Top, 0.0], [Plane + 0.03, Top + 0.1, L]);
        Hide(p, [0.65, 2.15, 0.0], [0.71, a[3].Y, L]);
        Fittings(p, a, a, L, door: true);
        p.Stats(170, 500, 8, 2.0);
        return ("", p);
    }

    /// <summary>A cab-forward shell over engine and seats: blunt raked nose, tall screen, no bonnet. The back is open to climb in.</summary>
    private static (string, Part) CabOver()
    {
        var p = P("Cab Over Shell", Hulls, "A flat-faced cab over the engine, the seats and all. Open at the back.", Steel);
        double w0 = 0.8, w1 = 0.95, zn = -2.15, zs = -1.25, zb = 0.25;
        double y0 = 0.12, ybelt = 1.02, yr = 1.82;
        foreach (var s in new[] { -1, 1 })
        {
            double[] ns = [s, 0.0, 0.0], nf0 = [0.95 * s, 0.0, -0.3];
            var nl = Sq(Py.Sum(nf0.Select(y => y * y)));
            double[] nf = [.. nf0.Select(x => x / nl)];
            var j = Mitre(ns, nf); // (the side and the raked front share their joint)
            Facet(p, [[s * w1, y0, zs], [s * w1, ybelt, zs], [s * w1, ybelt, zb], [s * w1, y0, zb]], [1, 0, 0], Rust, offsets: [j, j, ns, ns]);
            Facet(p, [[s * w0, y0, zn], [s * w0, ybelt, zn], [s * w1, ybelt, zs], [s * w1, y0, zs]], nf, Rust, offsets: [nf, nf, j, j]);
            Facet(p, [[s * w1, ybelt, zs], [s * w0, ybelt, zn], [s * 0.68, yr, -1.45], [s * 0.78, yr, zb]], [0.9 * s, 0.4, -0.15], Steel);
            var (lo, hi) = Sorted(s * 0.9, s * 0.96);
            Hide(p, [lo, 0.3, zs], [hi, ybelt, zb - 0.9]);
        }
        Facet(p, [[-w0, y0, zn], [w0, y0, zn], [w0, ybelt, zn], [-w0, ybelt, zn]], [0, 0.05, -1], Dark, 0.05);
        Facet(p, [[-w0, ybelt, zn], [w0, ybelt, zn], [0.68, yr, -1.45], [-0.68, yr, -1.45]], [0, 0.6, -0.8], "#3f4648");
        Facet(p, [[-0.68, yr, -1.45], [0.68, yr, -1.45], [0.78, yr, zb], [-0.78, yr, zb]], [0, 1, 0], Roof);
        Hide(p, [-0.8, y0, zn], [0.8, ybelt - 0.1, zn + 0.05]);
        Hide(p, [-0.68, yr - 0.03, -1.45], [0.68, yr + 0.03, zb]);
        foreach (var s in new[] { -1, 1 })
        {
            var (lo, hi) = Sorted(s * 0.8, s * 0.86);
            Hide(p, [lo, 0.3, zb - 0.06], [hi, yr, zb]);
        }
        p.Sock("footL", [-0.25, 0, 0.25], "-y");
        p.Sock("footR", [0.25, 0, 0.25], "-y");
        Hide(p, [-0.28, 0, 0.22], [0.28, 0.06, 0.28]);
        Pad(p, "top", [0, yr + 0.03, -0.3], "+y");
        p.Stats(90, 480, 10, 3.0);
        return ("", p);
    }

    private static List<object?> Pts(IEnumerable<double[]> pts, bool round) =>
        [.. pts.Select(q => (object?)q.Select(c => (object?)(round ? Py.Round(c, 4) : c)).ToList())];

    /// <summary>A gun tub on a hull's side: the turntable sits on its top.</summary>
    private static (string, Part) Sponson()
    {
        var p = P("Gun Sponson", Guns, "A bulged tub on a vehicle's side, with a gun's turntable on top.", Steel);
        double h = 0.22;
        double[][] top = [[0.0, h, -0.4], [0.62, h, -0.3], [0.62, h, 0.3], [0.0, h, 0.4]];
        double[][] low = [[0.0, -0.3, -0.25], [0.28, -0.3, -0.15], [0.28, -0.3, 0.15], [0.0, -0.3, 0.25]];
        p.Add(Box([0, -0.3, -0.4], [0.62, h, 0.4], Rust, detail: true, shape: Kw(("shape", "hull"), ("points", Pts(top.Concat(low), true)))));
        Hide(p, [0.02, -0.1, -0.28], [0.5, h, 0.28]);
        Pad(p, "back", [0, 0, 0], "-x");
        p.Sock("top", [0.3, h, 0], "+y");
        p.Stats(40, 260, 10, 1.0);
        return ("", p);
    }

    /// <summary>A faceted stowage box hung on a side: a bulge that breaks the flank.</summary>
    private static (string, Part) Pannier()
    {
        var p = P("Hull Pannier 1 m", Guns, "A faceted box hung on a hull's side.", Rust);
        double[][] pts = [[0.0, 0.3, -0.5], [0.0, 0.3, 0.5], [0.22, 0.26, -0.42], [0.22, 0.26, 0.42], [0.34, 0.0, -0.42], [0.34, 0.0, 0.42], [0.2, -0.28, -0.34], [0.2, -0.28, 0.34], [0.0, -0.3, -0.42], [0.0, -0.3, 0.42]];
        p.Add(Box([0, -0.3, -0.5], [0.34, 0.3, 0.5], Rust, detail: true, shape: Kw(("shape", "hull"), ("points", Pts(pts, false)))));
        Hide(p, [0.02, -0.2, -0.38], [0.24, 0.2, 0.38]);
        Pad(p, "back", [0, 0, 0], "-x");
        p.Stats(30, 220, 6, 1.0);
        return ("", p);
    }

    /// <summary>A low armoured lid over a crate or two on a deck: where a feed starts.</summary>
    private static (string, Part) AmmoHutch()
    {
        var p = P("Ammo Hutch", Guns, "A low armoured lid over ammunition on a deck. Stands on a bolt deck.", Dark);
        foreach (var s in new[] { -1, 1 })
        {
            Facet(p, [[s * 0.62, 0, -0.52], [s * 0.46, 0.62, -0.4], [s * 0.46, 0.62, 0.4], [s * 0.62, 0, 0.52]], [0.97 * s, 0.24, 0], Dark, 0.06);
            var (lo, hi) = Sorted(s * 0.55, s * 0.6);
            Hide(p, [lo, 0, -0.4], [hi, 0.55, 0.4]);
        }
        Facet(p, [[-0.46, 0.62, -0.4], [0.46, 0.62, -0.4], [0.46, 0.62, 0.4], [-0.46, 0.62, 0.4]], [0, 1, 0], Dark, 0.06);
        Hide(p, [-0.46, 0.59, -0.4], [0.46, 0.65, 0.4]);
        Facet(p, [[-0.62, 0, -0.52], [0.62, 0, -0.52], [0.46, 0.62, -0.4], [-0.46, 0.62, -0.4]], [0, 0.2, -0.98], Rust, 0.06);
        double[] xs = [-0.5, 0.5], zs = [-0.25, 0.25];
        for (var i = 0; i < 2; i++)
            for (var k = 0; k < 2; k++)
                p.Sock($"foot{i}{k}", [xs[i], 0, zs[k]], "-y");
        Pad(p, "top", [0, 0.65, 0], "+y");
        p.Stats(60, 500, 16, 2.0);
        return ("", p);
    }

    /// <summary>Two tyres on one hub, for a heavy rear axle.</summary>
    private static (string, Part) Dually()
    {
        var p = P("Dually Wheel", "Wheels", "Twin 0.9 m tyres on one hub: a heavy axle's rear wheels.", "#1f2124", role: "wheel", tool: "wrench");
        foreach (var (x0, x1) in new[] { (-0.34, -0.03), (0.03, 0.34) })
        {
            p.Add(Box([x0, -0.45, -0.45], [x1, 0.45, 0.45], "#1f2124", detail: true, shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x"))));
            Hide(p, [x0, -0.32, -0.32], [x1, 0.32, 0.32]);
        }
        p.Add(Box([-0.03, -0.2, -0.2], [0.03, 0.2, 0.2], "#55595c", detail: true, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+x"))));
        foreach (var s in new[] { -1, 1 })
            p.Add(Box([s * 0.33 - 0.015, -0.22, -0.22], [s * 0.33 + 0.015, 0.22, 0.22], "#6e7275", detail: true, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+x"))));
        p.Sock("axle", [-0.34, 0, 0], "-x", kind: "axle");
        p.Stats(68, 300, 3, 1.5);
        p.Set(("wheel", Kw(("radius", 0.45), ("grip", 1.0), ("offroad", 1.3), ("roll", 0.025), ("flat", true))), ("material", "rubber"), ("onBreak", "disable"));
        return ("", p);
    }

    public void Make(PartOutput o)
    {
        string[] names = ["T", "L", "N"];
        var parts = names.Select(a => Bay(a, a)).ToList();
        foreach (var a in names)
            foreach (var b in names)
                if (a != b)
                    parts.Add(Bay(a, b));
        parts.AddRange(names.Select(Brow));
        parts.AddRange(names.Select(Tail));
        parts.AddRange([DoorBay(), CabOver(), Sponson(), Pannier(), AmmoHutch(), Dually()]);
        WriteParts(o, Out, parts);
    }
}
