using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.StationLook;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The ring station (Karl, 2026-11-18, third pass: a ring of habitat segments round one hexagonal hub, a docking spindle below; his
/// reference picture is the project's station/reference-ring-station.png). The pieces' looks are hulls in their own frames (a ring
/// segment is a straight prism mitred to its neighbours); their collision is a <see cref="Lattice"/> of hidden axis-aligned boxes, every
/// piece of one layer filling its own cells of one shared grid, so neighbours meet face to face and weld, whatever their angle.
/// Detail comes from silhouette and depth (setbacks, stepped profiles, joint collars, hangar mouths) rather than many small boxes:
/// the whole station keeps under about 60,000 triangles.
/// </summary>
public sealed partial class SpaceStationParts
{
    private const string Cyan = "#5fe3f2", Dark = "#2a2e32", Hull2 = "#7d8388";

    // ---- the collision lattice

    /// <summary>
    /// Hidden collision boxes on one grid (cell metres across, in x and z): each piece adds convex footprints, a layer at a time, and
    /// takes every free cell whose four corners lie inside its footprint, first come first served. Cells are merged into rectangles when
    /// the boxes are written. Pieces of one layer never overlap and touch wherever their cells are neighbours.
    /// </summary>
    private sealed class Lattice(double cell = 2)
    {
        private sealed class Reg(string layer, Part p, List<(double X, double Z)> poly, double y0, double y1)
        {
            public readonly string Layer = layer;
            public readonly Part P = p;
            public readonly List<(double X, double Z)> Poly = poly;
            public readonly double Y0 = y0, Y1 = y1;
            public readonly List<(int I, int J)> Cells = [];
        }

        private readonly List<Reg> regs = [];
        private readonly HashSet<string> resolved = [];

        public void Add(string layer, Part p, IEnumerable<(double X, double Z)> poly, double y0, double y1)
        {
            if (resolved.Contains(layer))
                throw new InvalidOperationException($"space_station: lattice layer {layer} was already resolved");
            regs.Add(new Reg(layer, p, [.. poly], y0, y1));
        }

        private static bool Inside(List<(double X, double Z)> poly, double x, double z)
        {
            var sign = 0;
            for (var i = 0; i < poly.Count; i++)
            {
                var (ax, az) = poly[i];
                var (bx, bz) = poly[(i + 1) % poly.Count];
                var c = (bx - ax) * (z - az) - (bz - az) * (x - ax);
                if (Math.Abs(c) < 1e-6)
                    continue;
                var s = Math.Sign(c);
                if (sign == 0)
                    sign = s;
                else if (s != sign)
                    return false;
            }
            return true;
        }

        private void Resolve(string layer)
        {
            if (!resolved.Add(layer))
                return;
            var taken = new HashSet<(int, int)>();
            foreach (var r in regs.Where(q => q.Layer == layer))
            {
                int i0 = (int)Math.Floor(r.Poly.Min(q => q.X) / cell), i1 = (int)Math.Ceiling(r.Poly.Max(q => q.X) / cell);
                int j0 = (int)Math.Floor(r.Poly.Min(q => q.Z) / cell), j1 = (int)Math.Ceiling(r.Poly.Max(q => q.Z) / cell);
                for (var i = i0; i < i1; i++)
                    for (var j = j0; j < j1; j++)
                    {
                        if (taken.Contains((i, j)))
                            continue;
                        double x0 = i * cell, x1 = x0 + cell, z0 = j * cell, z1 = z0 + cell;
                        if (Inside(r.Poly, x0, z0) && Inside(r.Poly, x1, z0) && Inside(r.Poly, x0, z1) && Inside(r.Poly, x1, z1))
                        {
                            taken.Add((i, j));
                            r.Cells.Add((i, j));
                        }
                    }
            }
        }

        /// <summary>How far out along an axis direction (dx, dz: one of them 0) a piece's cells reach within hw of the axis: where a straight
        /// arm or boom welded to that piece starts.</summary>
        public double Reach(string layer, Part p, int dx, int dz, double hw)
        {
            Resolve(layer);
            var best = double.NegativeInfinity;
            foreach (var r in regs.Where(q => q.Layer == layer && q.P == p))
                foreach (var (i, j) in r.Cells)
                {
                    double x0 = i * cell, x1 = x0 + cell, z0 = j * cell, z1 = z0 + cell;
                    double c0 = dx != 0 ? z0 : x0, c1 = dx != 0 ? z1 : x1;
                    if (c1 <= -hw + 1e-6 || c0 >= hw - 1e-6)
                        continue;
                    var along = dx > 0 ? x1 : dx < 0 ? -x0 : dz > 0 ? z1 : -z0;
                    best = Math.Max(best, along);
                }
            if (double.IsNegativeInfinity(best))
                throw new InvalidOperationException($"space_station: {p.D["name"]} has no cells on that axis");
            return best;
        }

        /// <summary>Writes every piece's cells as hidden boxes, merged into rectangles (runs along x, stacked along z).</summary>
        public void Emit()
        {
            foreach (var l in regs.Select(q => q.Layer).Distinct().ToList())
                Resolve(l);
            foreach (var r in regs)
            {
                if (r.Cells.Count == 0)
                    throw new InvalidOperationException($"space_station: {r.P.D["name"]} took no cells in layer {r.Layer}");
                var runs = new List<(int J, int I0, int I1)>();
                foreach (var row in r.Cells.GroupBy(c => c.J).OrderBy(g => g.Key))
                {
                    var xs = row.Select(c => c.I).OrderBy(v => v).ToList();
                    var s = xs[0];
                    for (var n = 1; n <= xs.Count; n++)
                        if (n == xs.Count || xs[n] != xs[n - 1] + 1)
                        {
                            runs.Add((row.Key, s, xs[n - 1] + 1));
                            if (n < xs.Count)
                                s = xs[n];
                        }
                }
                var open = new Dictionary<(int, int), (int J0, int J1)>();
                var rects = new List<(int I0, int I1, int J0, int J1)>();
                foreach (var (j, a, b) in runs.OrderBy(q => q.J))
                {
                    if (open.TryGetValue((a, b), out var o) && o.J1 == j)
                        open[(a, b)] = (o.J0, j + 1);
                    else
                    {
                        if (open.TryGetValue((a, b), out o))
                            rects.Add((a, b, o.J0, o.J1));
                        open[(a, b)] = (j, j + 1);
                    }
                }
                rects.AddRange(open.Select(kv => (kv.Key.Item1, kv.Key.Item2, kv.Value.J0, kv.Value.J1)));
                foreach (var (a, b, j0, j1) in rects)
                    Hidden(r.P, a * cell, r.Y0, j0 * cell, b * cell, r.Y1, j1 * cell);
            }
        }
    }

    // ---- shapes in a turned frame

    /// <summary>A point in the frame turned to angle phi (radians from +x towards +z): u along the tangent, d out along the radius.</summary>
    private static double[] W(double phi, double u, double d, double y) =>
        [d * Math.Cos(phi) - u * Math.Sin(phi), y, d * Math.Sin(phi) + u * Math.Cos(phi)];

    private static (double X, double Z) W2(double phi, double u, double d)
    {
        var w = W(phi, u, d, 0);
        return (w[0], w[2]);
    }

    /// <summary>A box in the frame turned to phi, drawn only.</summary>
    private static void LBox(Part p, double phi, double u0, double u1, double d0, double d1, double y0, double y1, string col, string? fin, bool glow = false)
    {
        var pts = new List<double[]>();
        foreach (var u in new[] { u0, u1 })
            foreach (var d in new[] { d0, d1 })
                foreach (var y in new[] { y0, y1 })
                    pts.Add(W(phi, u, d, y));
        Hull(p, pts, col, fin, glow);
    }

    private static List<(double X, double Z)> NGonPts(double cx, double cz, double r, int n, double rot) =>
        [.. Enumerable.Range(0, n).Select(i => (cx + r * Math.Cos(rot + 2 * Math.PI * i / n), cz + r * Math.Sin(rot + 2 * Math.PI * i / n)))];

    /// <summary>A frustum of an n-sided prism round a vertical axis: radius r0 at y0, r1 at y1 (equal for a prism), drawn only.</summary>
    private static void NGon(Part p, double cx, double cz, double r0, double r1, int n, double rot, double y0, double y1, string col, string? fin, bool glow = false)
    {
        var pts = new List<double[]>();
        foreach (var (x, z) in NGonPts(cx, cz, r0, n, rot))
            pts.Add([x, y0, z]);
        foreach (var (x, z) in NGonPts(cx, cz, r1, n, rot))
            pts.Add([x, y1, z]);
        Hull(p, pts, col, fin, glow);
    }

    /// <summary>A plate on face i of an n-gon (circumradius r, turned rot): u0 to u1 along the face (0 to 1), d0 to d1 out from it.</summary>
    private static void NFace(Part p, double cx, double cz, double r, int n, double rot, int i, double u0, double u1, double d0, double d1, double y0, double y1, string col, string? fin, bool glow = false)
    {
        var a = rot + 2 * Math.PI * i / n;
        var b = rot + 2 * Math.PI * (i + 1) / n;
        double ax = cx + r * Math.Cos(a), az = cz + r * Math.Sin(a), bx = cx + r * Math.Cos(b), bz = cz + r * Math.Sin(b);
        double mid = (a + b) / 2, nx = Math.Cos(mid), nz = Math.Sin(mid);
        var pts = new List<double[]>();
        foreach (var u in new[] { u0, u1 })
            foreach (var d in new[] { d0, d1 })
                foreach (var y in new[] { y0, y1 })
                    pts.Add([ax + (bx - ax) * u + nx * d, y, az + (bz - az) * u + nz * d]);
        Hull(p, pts, col, fin, glow);
    }

    /// <summary>An n-sided tube from a to b, radius r (its faces square to the frame's up when n is a multiple of 4), drawn only.</summary>
    private static void Tube(Part p, double[] a, double[] b, double r, int n, string col, string? fin, bool glow = false)
    {
        double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2], l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        double[] d = [dx / l, dy / l, dz / l];
        double[] up = Math.Abs(d[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        double[] u = [d[1] * up[2] - d[2] * up[1], d[2] * up[0] - d[0] * up[2], d[0] * up[1] - d[1] * up[0]];
        var ul = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        u = [u[0] / ul, u[1] / ul, u[2] / ul];
        double[] v = [d[1] * u[2] - d[2] * u[1], d[2] * u[0] - d[0] * u[2], d[0] * u[1] - d[1] * u[0]];
        var pts = new List<double[]>();
        foreach (var e in new[] { a, b })
            for (var i = 0; i < n; i++)
            {
                var t = Math.PI / n + 2 * Math.PI * i / n;
                double cu = Math.Cos(t) * r, cv = Math.Sin(t) * r;
                pts.Add([e[0] + u[0] * cu + v[0] * cv, e[1] + u[1] * cu + v[1] * cv, e[2] + u[2] * cu + v[2] * cv]);
            }
        Hull(p, pts, col, fin, glow);
    }

    /// <summary>A dish aerial on a strut from at, facing dir (a unit vector), radius rd: a shallow dish, its feed and the strut.</summary>
    private static void Dish(Part p, double[] at, double[] dir, double rd, double strut)
    {
        double[] c = [at[0] + dir[0] * strut, at[1] + dir[1] * strut, at[2] + dir[2] * strut];
        Tube(p, at, c, 0.45, 4, Truss, Dirty);
        double[] up = Math.Abs(dir[1]) < 0.9 ? [0, 1, 0] : [1, 0, 0];
        double[] u = [dir[1] * up[2] - dir[2] * up[1], dir[2] * up[0] - dir[0] * up[2], dir[0] * up[1] - dir[1] * up[0]];
        var ul = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        u = [u[0] / ul, u[1] / ul, u[2] / ul];
        double[] v = [dir[1] * u[2] - dir[2] * u[1], dir[2] * u[0] - dir[0] * u[2], dir[0] * u[1] - dir[1] * u[0]];
        var pts = new List<double[]>();
        for (var i = 0; i < 10; i++)
        {
            var t = 2 * Math.PI * i / 10;
            foreach (var (rr, back) in new[] { (rd, rd * 0.32), (rd * 0.3, 0.05) })
                pts.Add([c[0] + (u[0] * Math.Cos(t) + v[0] * Math.Sin(t)) * rr + dir[0] * back, c[1] + (u[1] * Math.Cos(t) + v[1] * Math.Sin(t)) * rr + dir[1] * back, c[2] + (u[2] * Math.Cos(t) + v[2] * Math.Sin(t)) * rr + dir[2] * back]);
        }
        Hull(p, pts, Plate3, Painted);
        double[] f = [c[0] + dir[0] * rd * 0.9, c[1] + dir[1] * rd * 0.9, c[2] + dir[2] * rd * 0.9];
        Tube(p, [c[0] + dir[0] * 0.2, c[1] + dir[1] * 0.2, c[2] + dir[2] * 0.2], f, 0.18, 4, Frame, Steel);
        Hull(p, [[f[0] - 0.3, f[1] - 0.3, f[2] - 0.3], [f[0] + 0.3, f[1] + 0.3, f[2] + 0.3], [f[0] + 0.3, f[1] - 0.3, f[2] - 0.3], [f[0] - 0.3, f[1] + 0.3, f[2] + 0.3],
            [f[0] - 0.3, f[1] - 0.3, f[2] + 0.3], [f[0] + 0.3, f[1] + 0.3, f[2] - 0.3]], NavRed, null, glow: true);
    }

    // ---- the ring

    private const int RingSegs = 16;
    private const double RIn = 108, ROut = 132;

    /// <summary>The ring's cross-section (apothem d, height y), its outer wall taller and deeper than its inner.</summary>
    private static readonly (double D, double Y)[] RingProfile = [(108, -4), (108, 4), (111, 7), (129, 7), (132, 4), (132, -5), (129, -8), (111, -7)];

    private enum Seg { Pad, Habitat, Hangar, Industry, Tower }

    private static readonly Seg[] RingKinds =
    [
        Seg.Pad, Seg.Habitat, Seg.Hangar, Seg.Habitat, Seg.Pad, Seg.Industry, Seg.Habitat, Seg.Tower,
        Seg.Pad, Seg.Habitat, Seg.Hangar, Seg.Industry, Seg.Pad, Seg.Habitat, Seg.Hangar, Seg.Habitat,
    ];

    private static readonly double TanA = Math.Tan(Math.PI / RingSegs), CosA = Math.Cos(Math.PI / RingSegs);

    /// <summary>The longest u a box at apothem d may reach in a segment and stay inside its mitred ends.</summary>
    private static double UMax(double d) => d * TanA - 0.8;

    private static Part RingSegment(Kit k, Lattice lat, int i)
    {
        var kind = RingKinds[i];
        var phi = 2 * Math.PI * i / RingSegs;
        var desc = kind switch
        {
            Seg.Pad => "A ring segment with a landing pad on its roof for a Shuttle or a Limpet, low hangars either side of it and windows round its walls.",
            Seg.Hangar => "A ring segment of hangars: two big hangar mouths in its outer wall under hazard lintels, a low workshop on its roof.",
            Seg.Industry => "A ring segment of plant: storage tanks and a pipe rack on its roof, more tanks slung under its keel.",
            Seg.Tower => "The ring's tower segment: a tall block of lit decks over the ring, with a domed observation room on top.",
            _ => "A habitat segment of the ring: stepped blocks of quarters on its roof, rows of lit windows round its walls.",
        };
        var p = k.Piece($"Ring {i + 1}", desc, Plate);
        var seed = 500 + i * 37;

        // the main hull: the profile, mitred to the joints, stopped short of them inside the joint collars
        var main = new List<double[]>();
        foreach (var (d, y) in RingProfile)
            foreach (var s in new[] { -1, 1 })
                main.Add(W(phi, s * (d * TanA - 0.5), d, y));
        Hull(p, main, i % 2 == 0 ? Plate : Hull2, Painted);

        // the joint collar at this segment's start: the profile at the joint, a little bigger all round
        var pj = phi - Math.PI / RingSegs;
        var collar = new List<double[]>();
        foreach (var (d, y) in RingProfile)
        {
            var dj = d / CosA + (d < 120 ? -1.1 : 1.1);
            var yj = y + (y < 0 ? -1.1 : 1.1);
            foreach (var s in new[] { -1, 1 })
                collar.Add(W(pj, s * 1.7, dj, yj));
        }
        Hull(p, collar, Dark, Dirty);
        LBox(p, pj, -0.9, 0.9, ROut / CosA + 1.1, ROut / CosA + 1.14, -1.4, -0.6, Cyan, null, glow: true);
        LBox(p, pj, -0.9, 0.9, RIn / CosA - 1.14, RIn / CosA - 1.1, -0.4, 0.4, Cyan, null, glow: true);
        Lamp(p, W(pj, -0.3, 120, 8.1)[0] - 0.3, 8.1, W(pj, -0.3, 120, 8.1)[2] - 0.3, W(pj, -0.3, 120, 8.1)[0] + 0.3, 8.5, W(pj, -0.3, 120, 8.1)[2] + 0.3, i % 4 == 0 ? NavRed : LampCol);

        // the outer wall: ribs, rows of windows, a cyan panel; or hangar mouths
        var um = UMax(ROut);
        var ribs = kind == Seg.Hangar ? new[] { 0.0 } : [-um * 0.62, -um * 0.2, um * 0.2, um * 0.62];
        foreach (var u in ribs)
            LBox(p, phi, u - 0.4, u + 0.4, ROut, ROut + 0.45, -5, 4, Hull2, Dirty);
        var edges = new List<double> { -um };
        edges.AddRange(ribs);
        edges.Add(um);
        if (kind == Seg.Hangar)
        {
            foreach (var s in new[] { -1, 1 })
            {
                double u0 = s < 0 ? -um + 1.5 : 2.5, u1 = s < 0 ? -2.5 : um - 1.5;
                LBox(p, phi, u0, u1, ROut, ROut + 0.04, -4.4, 2.4, "#16191c", Soot);
                LBox(p, phi, u0 - 0.5, u1 + 0.5, ROut, ROut + 0.3, 2.4, 3.2, Hazard, HazardFin);
                LBox(p, phi, u0 + 0.5, u1 - 0.5, ROut + 0.3, ROut + 0.33, 2.6, 2.9, Cyan, null, glow: true);
                LBox(p, phi, u0, u1, ROut + 0.04, ROut + 0.07, -4.4, -4.1, Hazard, HazardFin);
            }
        }
        else
            for (var b = 0; b + 1 < edges.Count; b++)
            {
                double u0 = edges[b] + 1.0, u1 = edges[b + 1] - 1.0;
                if (u1 - u0 < 2)
                    continue;
                if (Rnd(seed, b) < 0.85)
                    LBox(p, phi, u0, u1, ROut, ROut + 0.03, 1.4, 2.0, Rnd(seed, b + 10) < 0.7 ? LampCol : Window, null, glow: true);
                if (Rnd(seed, b + 20) < 0.7)
                    LBox(p, phi, u0, u1, ROut, ROut + 0.03, -1.0, -0.4, LampCol, null, glow: true);
                if (b == (int)(Rnd(seed, 30) * (edges.Count - 1)))
                    LBox(p, phi, (u0 + u1) / 2 - 1.6, (u0 + u1) / 2 + 1.6, ROut, ROut + 0.03, -4.0, -2.6, Cyan, null, glow: true);
            }
        // the inner wall: a row of windows
        var ui = UMax(RIn) - 0.6;
        LBox(p, phi, -ui, -ui / 3 - 0.6, RIn - 0.03, RIn, 0.4, 1.0, LampCol, null, glow: true);
        LBox(p, phi, -ui / 3 + 0.6, ui / 3 - 0.6, RIn - 0.03, RIn, 0.4, 1.0, Rnd(seed, 40) < 0.5 ? Window : LampCol, null, glow: true);
        LBox(p, phi, ui / 3 + 0.6, ui, RIn - 0.03, RIn, 0.4, 1.0, LampCol, null, glow: true);

        // the keel under it
        LBox(p, phi, -UMax(114) + 1, UMax(114) - 1, 114, 126, -9.0, -6.8, Dark, Dirty);
        LBox(p, phi, -UMax(114) + 3, UMax(114) - 3, 116, 124, -9.6, -8.9, Frame, Dirty);

        // the roof, by kind
        const double roof = 7, sink = 6.7;
        switch (kind)
        {
            case Seg.Pad:
                {
                    var c = W(phi, 0, 120, roof);
                    Pad(k, p, c[0], roof, c[2], 6, $"p{i + 1}");
                    foreach (var s in new[] { -1, 1 })
                    {
                        double u0 = s < 0 ? -UMax(113) + 0.6 : 9, u1 = s < 0 ? -9 : UMax(113) - 0.6;
                        LBox(p, phi, u0, u1, 113, 127, sink, roof + 3.2, Hull2, Painted);
                        LBox(p, phi, u0 + 0.5, u1 - 0.5, 113.5, 126.5, roof + 3.2, roof + 3.5, Frame, Grating);
                        LBox(p, phi, u0 + 0.8, u1 - 0.8, 127, 127.03, roof + 1.2, roof + 1.8, LampCol, null, glow: true);
                        LBox(p, phi, s < 0 ? u1 : u0 - 0.04, s < 0 ? u1 + 0.04 : u0, 115, 125, roof + 0.4, roof + 2.6, "#16191c", Soot);
                    }
                    break;
                }
            case Seg.Hangar:
                LBox(p, phi, -12, 12, 115, 126, sink, roof + 2.6, Hull2, Painted);
                LBox(p, phi, -11.5, 11.5, 115.5, 125.5, roof + 2.6, roof + 2.9, Frame, Grating);
                LBox(p, phi, -10, 10, 126, 126.03, roof + 1.0, roof + 1.6, LampCol, null, glow: true);
                Cyl(p, 'y', W(phi, 8, 118, 0)[0], 0, W(phi, 8, 118, 0)[2], 0.6, roof + 2.9, roof + 5, Plate2, Dirty);
                break;
            case Seg.Industry:
                foreach (var (u, d, r, h) in new[] { (-14.0, 118.0, 3.2, 7.0), (-6.5, 121.0, 3.0, 6.0), (5.0, 117.0, 3.4, 8.0), (13.5, 121.5, 2.6, 5.0) })
                {
                    var c = W(phi, u, d, 0);
                    Cyl(p, 'y', c[0], 0, c[2], r, sink, roof + h, Tank, Rusty);
                    Cyl(p, 'y', c[0], 0, c[2], r + 0.15, roof + h * 0.45, roof + h * 0.45 + 0.4, Frame, Dirty);
                }
                LBox(p, phi, -UMax(126) + 1, UMax(126) - 1, 125.5, 127.5, sink, roof + 1.4, Truss, Dirty);
                LBox(p, phi, -UMax(126) + 1, UMax(126) - 1, 125.6, 127.4, roof + 1.4, roof + 2.2, Pipe, Dirty);
                foreach (var u in new[] { -9.0, 9.0 })
                {
                    var c = W(phi, u, 120, 0);
                    Cyl(p, 'y', c[0], 0, c[2], 2.4, -14, -9.4, Tank, Rusty);
                }
                break;
            case Seg.Tower:
                {
                    const double top = 26;
                    LBox(p, phi, -14, 14, 112, 128, sink, top, Plate3, Painted);
                    LBox(p, phi, -15, 15, 111.4, 128.6, roof + 3, roof + 3.6, Dark, Dirty);
                    for (var y = roof + 5; y < top - 1.5; y += 3.2)
                    {
                        LBox(p, phi, -12.5, 12.5, 128, 128.03, y, y + 1.1, LampCol, null, glow: true);
                        LBox(p, phi, -12.5, 12.5, 111.97, 112, y, y + 1.1, Window, null, glow: true);
                    }
                    LBox(p, phi, -14.03, -14, 119.4, 120.6, roof + 4, top - 2, Cyan, null, glow: true);
                    var c = W(phi, 0, 120, 0);
                    NGon(p, c[0], c[2], 7.5, 7.5, 12, 0, top - 0.3, top + 2.4, Dark, Dirty);
                    NGon(p, c[0], c[2], 7.6, 7.6, 12, 0, top + 0.6, top + 1.6, LampCol, null, glow: true);
                    var dome = new List<double[]>();
                    foreach (var (r, y) in new[] { (7.4, top + 2.3), (5.6, top + 5.0), (2.4, top + 6.4) })
                        foreach (var (x, z) in NGonPts(c[0], c[2], r, 10, 0))
                            dome.Add([x, y, z]);
                    Hull(p, dome, Plate3, Painted);
                    Lamp(p, c[0] - 0.3, top + 6.3, c[2] - 0.3, c[0] + 0.3, top + 7.0, c[2] + 0.3, NavRed);
                    break;
                }
            default:
                {
                    // two or three stepped blocks of quarters, varied in width, depth and height
                    var n = Rnd(seed, 50) < 0.5 ? 2 : 3;
                    var span = 2 * UMax(113) - 1.2;
                    var u = -UMax(113) + 0.6;
                    for (var b = 0; b < n; b++)
                    {
                        var w = span / n - 1.6;
                        double d0 = 112.5 + Rnd(seed, 60 + b) * 2, d1 = 127.5 - Rnd(seed, 70 + b) * 5;
                        var h = 3.2 + Math.Round(Rnd(seed, 80 + b) * 2) * 2.6;
                        LBox(p, phi, u, u + w, d0, d1, sink, roof + h, b % 2 == 0 ? Plate3 : Plate, Painted);
                        LBox(p, phi, u + 0.4, u + w - 0.4, d0 + 0.4, d1 - 0.4, roof + h, roof + h + 0.3, Dark, Grating);
                        for (var y = roof + 1.2; y + 0.6 < roof + h - 0.4; y += 2.6)
                            LBox(p, phi, u + 0.8, u + w - 0.8, d1, d1 + 0.03, y, y + 0.6, Rnd(seed, 90 + b) < 0.8 ? LampCol : Window, null, glow: true);
                        if (h > 6 && Rnd(seed, 100 + b) < 0.7)
                        {
                            // a setback storey on the tallest
                            LBox(p, phi, u + 2, u + w - 2, d0 + 2, d1 - 3, roof + h + 0.1, roof + h + 3, Hull2, Painted);
                            LBox(p, phi, u + 2.6, u + w - 2.6, d1 - 3, d1 - 2.97, roof + h + 1.2, roof + h + 1.8, LampCol, null, glow: true);
                        }
                        u += w + 1.6;
                    }
                    break;
                }
        }

        // collision: the ring's footprint in this segment
        lat.Add("ring", p, [W2(phi, -RIn * TanA, RIn), W2(phi, RIn * TanA, RIn), W2(phi, ROut * TanA, ROut), W2(phi, -ROut * TanA, ROut)], -7, roof);
        return p;
    }

    // ---- the hub

    private const double HubR = 34, HubTopR = 27;

    private static Part Hub(Kit k, Lattice lat, double[] spokes)
    {
        var p = k.Piece("Hub", "The hub: one hexagonal unit at the ring's middle, its decks in two steps under the command dome, hangar doors in the faces between the spokes, the docking spindle hanging under it.", Plate);
        const int six = 6;
        NGon(p, 0, 0, HubR, HubR, six, 0, -12, 4, Plate, Painted);
        NGon(p, 0, 0, HubR - 0.6, 25, six, 0, -11.7, -17, Dark, Dirty);
        NGon(p, 0, 0, HubTopR, HubTopR, six, Math.PI / 6, 3.7, 10, Plate3, Painted);
        NGon(p, 0, 0, HubR + 0.4, HubR + 0.4, six, 0, 3.6, 4.4, Dark, Dirty);
        for (var i = 0; i < six; i++)
        {
            var mid = Math.PI / 6 + Math.PI / 3 * i;
            var spoke = spokes.Any(s => Math.Abs(Math.IEEERemainder(s - mid, 2 * Math.PI)) < 0.01);
            foreach (var (u0, u1) in new[] { (0.06, 0.4), (0.6, 0.94) })
            {
                NFace(p, 0, 0, HubR, six, 0, i, u0, u1, 0, 0.03, 0.8, 1.4, LampCol, null, glow: true);
                NFace(p, 0, 0, HubR, six, 0, i, u0, u1, 0, 0.03, -1.8, -1.2, i % 2 == 0 ? Window : LampCol, null, glow: true);
            }
            if (spoke)
                NFace(p, 0, 0, HubR, six, 0, i, 0.44, 0.56, 0, 0.03, -9, -5, Cyan, null, glow: true);
            else
            {
                NFace(p, 0, 0, HubR, six, 0, i, 0.28, 0.72, 0, 0.04, -10.6, -4.2, "#16191c", Soot);
                NFace(p, 0, 0, HubR, six, 0, i, 0.26, 0.74, 0, 0.3, -4.2, -3.5, Hazard, HazardFin);
                NFace(p, 0, 0, HubR, six, 0, i, 0.3, 0.7, 0.3, 0.33, -4.0, -3.7, Cyan, null, glow: true);
            }
            NFace(p, 0, 0, HubTopR, six, Math.PI / 6, i, 0.1, 0.9, 0, 0.03, 6.2, 6.9, LampCol, null, glow: true);
        }
        lat.Add("ring", p, NGonPts(0, 0, HubR, six, 0), -12, 4);
        lat.Add("hubtop", p, NGonPts(0, 0, HubTopR, six, Math.PI / 6), 4, 10);
        return p;
    }

    private static Part Dome(Kit k, Lattice lat)
    {
        var p = k.Piece("Command Dome", "The command dome on the hub: a drum of lit windows round the station's bridge and traffic control, under a faceted dome.", Plate3);
        NGon(p, 0, 0, 19, 19, 12, 0, 9.7, 15, Hull2, Painted);
        for (var i = 0; i < 12; i++)
        {
            NFace(p, 0, 0, 19, 12, 0, i, 0.08, 0.92, 0, 0.03, 11.4, 13.4, LampCol, null, glow: true);
            NFace(p, 0, 0, 19, 12, 0, i, 0.47, 0.53, 0.03, 0.12, 11.2, 13.6, Frame, Dirty);
        }
        var dome = new List<double[]>();
        foreach (var (r, y, n) in new[] { (19.6, 14.7, 12), (14.5, 20.0, 12), (5.0, 23.2, 6) })
            foreach (var (x, z) in NGonPts(0, 0, r, n, 0))
                dome.Add([x, y, z]);
        Hull(p, dome, Plate3, Painted);
        NGon(p, 0, 0, 19.8, 19.8, 12, 0, 14.6, 15.2, Dark, Dirty);
        lat.Add("dome", p, NGonPts(0, 0, 18.5, 12, 0), 10, 21);
        return p;
    }

    private static Part Spoke(Kit k, Lattice lat, int n, double th)
    {
        var p = k.Piece($"Spoke {n}", "A spoke from the hub to the ring: a pressurised tube with a lit walkway, a service pipe under it and collars at both ends.", Plate);
        Tube(p, W(th, 0, HubR * Math.Cos(Math.PI / 6) - 0.5, 0), W(th, 0, 110, 0), 3.2, 8, Plate2, Painted);
        var a = 3.2 * Math.Cos(Math.PI / 8);
        foreach (var s in new[] { -1, 1 })
            LBox(p, th, s < 0 ? -a - 0.03 : a, s < 0 ? -a : a + 0.03, 34, 104, -0.3, 0.4, s < 0 ? Window : LampCol, null, glow: true);
        Tube(p, W(th, 0, 31, -4.6), W(th, 0, 109, -4.6), 0.8, 8, Pipe, Dirty);
        foreach (var d in new[] { 38.0, 58.0, 78.0, 98.0 })
            LBox(p, th, -0.4, 0.4, d - 0.3, d + 0.3, -4.0, -2.5, Frame, Dirty);
        foreach (var (d0, d1) in new[] { (30.5, 33.5), (103.5, 106.5), (68.5, 70.0) })
        {
            Tube(p, W(th, 0, d0, 0), W(th, 0, d1, 0), 4.1, 8, Dark, Dirty);
            LBox(p, th, -0.8, 0.8, d0 + 0.3, d1 - 0.3, 3.75, 3.82, NavRed, null, glow: true);
        }
        lat.Add("ring", p, [W2(th, -4.5, 20), W2(th, 4.5, 20), W2(th, 4.5, 118), W2(th, -4.5, 118)], -3.5, 3.5);
        return p;
    }

    // ---- the spindle

    /// <summary>A length of the spindle's column between two drums: twelve faces, stiffening rings, a light strip, a dish or two.</summary>
    private static Part Column(Kit k, Lattice lat, string name, double yTop, double yBot, int seed, bool dish)
    {
        var p = k.Piece(name, "A length of the docking spindle: the column with the lift shafts and mains inside it, stiffening rings and an aerial.", Truss);
        NGon(p, 0, 0, 7, 7, 12, 0, yBot - 0.3, yTop + 0.3, Plate2, Plated);
        var n = Math.Max(1, (int)((yTop - yBot) / 9));
        for (var j = 1; j <= n; j++)
        {
            var y = yTop - (yTop - yBot) * j / (n + 1);
            NGon(p, 0, 0, 8.1, 8.1, 12, 0, y - 0.5, y + 0.5, Dark, Dirty);
        }
        for (var i = 0; i < 12; i += 4)
            NFace(p, 0, 0, 7, 12, 0, i + seed % 4, 0.3, 0.7, 0, 0.03, yBot + 1, yTop - 1, Cyan, null, glow: true);
        if (dish)
        {
            var th = Math.PI / 4 + seed;
            double[] dir = [Math.Cos(th) * 0.8, -0.6, Math.Sin(th) * 0.8];
            var y = (yTop + yBot) / 2;
            Dish(p, [Math.Cos(th) * 6.8, y, Math.Sin(th) * 6.8], dir, 4.5, 6);
        }
        lat.Add(name, p, NGonPts(0, 0, 6.6, 12, 0), yBot, yTop);
        return p;
    }

    /// <summary>A wide drum on the spindle (customs, engineering): chamfered top and bottom, rows of windows.</summary>
    private static Part Drum(Kit k, Lattice lat, string name, string desc, double r, double yTop, double yBot, bool windows, string layer)
    {
        var p = k.Piece(name, desc, Plate);
        NGon(p, 0, 0, r, r, 12, 0, yBot + 2, yTop - 2, Plate, Painted);
        NGon(p, 0, 0, r - 0.3, r - 6, 12, 0, yTop - 2.3, yTop, Hull2, Painted);
        NGon(p, 0, 0, r - 0.3, r - 6, 12, 0, yBot + 2.3, yBot, Hull2, Painted);
        NGon(p, 0, 0, r + 0.4, r + 0.4, 12, 0, yTop - 2.6, yTop - 1.8, Dark, Dirty);
        for (var i = 0; i < 12; i++)
        {
            if (windows)
                for (var y = yBot + 3.2; y + 0.6 < yTop - 2.8; y += 3)
                    NFace(p, 0, 0, r, 12, 0, i, 0.12, 0.88, 0, 0.03, y, y + 0.6, (i + (int)y) % 5 == 0 ? Window : LampCol, null, glow: true);
            else if (i % 3 == 0)
                NFace(p, 0, 0, r, 12, 0, i, 0.2, 0.8, 0, 0.03, yBot + 3, yBot + 3.6, Hazard, HazardFin);
            if (i % 3 == 1)
                NFace(p, 0, 0, r, 12, 0, i, 0.4, 0.6, 0, 0.055, yBot + 2.6, yTop - 3, Cyan, null, glow: true);
        }
        lat.Add(layer, p, NGonPts(0, 0, r - 0.5, 12, 0), yBot, yTop);
        return p;
    }

    /// <summary>A docking collar on the spindle: a drum the arms weld to, a hazard band and floodlights over its berths.</summary>
    private static Part DockCollar(Kit k, Lattice lat, string name, double yTop, double yBot)
    {
        var p = k.Piece(name, "A docking collar on the spindle: the drum the docking arms weld to, the dockmaster's windows over the berths, a hazard band and floodlights.", Plate2);
        NGon(p, 0, 0, 16, 16, 12, 0, yBot, yTop, Plate2, Plated);
        NGon(p, 0, 0, 16.4, 16.4, 12, 0, yTop - 1.2, yTop - 0.4, Hazard, HazardFin);
        NGon(p, 0, 0, 15.7, 9, 12, 0, yBot + 0.3, yBot - 3, Dark, Dirty);
        for (var i = 0; i < 12; i++)
        {
            NFace(p, 0, 0, 16, 12, 0, i, 0.15, 0.85, 0, 0.03, yTop - 3.2, yTop - 2.4, Window, null, glow: true);
            if (i % 3 == 1)
                NFace(p, 0, 0, 16, 12, 0, i, 0.42, 0.58, 0, 0.25, yBot + 0.6, yBot + 1.2, LampCol, null, glow: true);
        }
        lat.Add(name, p, NGonPts(0, 0, 15.8, 12, 0), yBot, yTop);
        return p;
    }

    // ---- the station

    private static Kit RingStation()
    {
        var k = new Kit("Highport", HighportName);
        var lat = new Lattice();
        var own = new List<Part>();
        double[] spokes = [Math.PI / 2, Math.PI * 7 / 6, Math.PI * 11 / 6];

        // the hub, the ring and its spokes (one collision layer, the hub's and the ring's cells taken first)
        own.Add(Hub(k, lat, spokes));
        var ring = Enumerable.Range(0, RingSegs).Select(i => RingSegment(k, lat, i)).ToList();
        own.AddRange(ring);
        // the joints: each collar takes the cells its joint line crosses, so neighbouring segments weld
        for (var i = 0; i < RingSegs; i++)
        {
            var pj = 2 * Math.PI * i / RingSegs - Math.PI / RingSegs;
            lat.Add("ring", ring[i], [W2(pj, -2.5, RIn / CosA), W2(pj, 2.5, RIn / CosA), W2(pj, 2.5, ROut / CosA), W2(pj, -2.5, ROut / CosA)], -7, 7);
        }
        for (var s = 0; s < spokes.Length; s++)
            own.Add(Spoke(k, lat, s + 1, spokes[s]));
        own.Add(Dome(k, lat));
        Mast(k, "Mast", 21, 44);

        // the spindle, top to bottom
        own.Add(Column(k, lat, "Spindle 1", -12, -34, 1, true));
        own.Add(Drum(k, lat, "Customs Drum", "The customs drum on the spindle: arrivals, customs and the market over the docks, rows of lit windows.", 21, -34, -46, true, "customs"));
        own.Add(Column(k, lat, "Spindle 2", -46, -56, 2, false));
        var dockA = DockCollar(k, lat, "Dock Collar", -56, -70);
        own.Add(dockA);
        own.Add(Column(k, lat, "Spindle 3", -70, -96, 3, true));
        var dockB = DockCollar(k, lat, "Freight Collar", -96, -110);
        own.Add(dockB);
        own.Add(Column(k, lat, "Spindle 4", -110, -128, 4, false));
        var eng = Drum(k, lat, "Engineering", "Engineering at the spindle's foot: life support, water and power, the radiator booms' roots, the reactor under it.", 19, -128, -150, false, "engineering");
        own.Add(eng);
        Reactor(k, "Reactor", 8, -168, -158, 10, -150);
        Thrusters(k, "Thrust Block", 11, -172, -168);

        var N = new ArmFrame(0, -1);
        var S = new ArmFrame(0, 1);
        var E = new ArmFrame(1, 0);
        var Wd = new ArmFrame(-1, 0);

        // the dock collar: Zero Day berths on arms north and south, Magpie berths east and west, both sides of each arm
        const double ybA = -66;
        foreach (var (fr, nm, ship, off, s, gantry) in new[] { (N, "N", ZeroDay, 14.0, 18.0, true), (S, "S", ZeroDay, 14.0, 18.0, true), (E, "E", Magpie, 8.0, 13.0, false), (Wd, "W", Magpie, 8.0, 13.0, false) })
        {
            var r0 = lat.Reach("Dock Collar", dockA, fr.Dx, fr.Dz, ArmHalf);
            var mid = r0 + off + ship.L / 2;
            var r1 = Math.Ceiling(mid + ship.L / 2 + 6);
            var who = ship == ZeroDay ? "Frigate" : "Miner";
            Arm(k, $"{who} Arm {nm}", fr, r0, r1, ybA, [(mid - ship.L / 2, mid + ship.L / 2)], false, 70);
            var bottom = ybA - 3.6;
            foreach (var side in new[] { -1, 1 })
            {
                var sn = side < 0 ? "L" : "R";
                Cradle(k, $"{who} Cradle {nm}{sn} 1", fr, side, mid - s, ybA, bottom, ship.W, ship.L, ship.H, false);
                Cradle(k, $"{who} Cradle {nm}{sn} 2", fr, side, mid, ybA, bottom - 4, ship.W, ship.L, ship.H, true);
                Cradle(k, $"{who} Cradle {nm}{sn} 3", fr, side, mid + s, ybA, bottom, ship.W, ship.L, ship.H, false);
                ClampArmBase(k, $"{who} Clamp {nm}{sn}", fr, mid + side * (ship == ZeroDay ? 6 : 4), side, ybA, side, ship == ZeroDay ? ClampStowAlong : 11); // (SpaceStationClampArm.cs)
            }
            if (gantry)
                Gantry(k, $"{who} Gantry {nm}", fr, mid - 1, ybA, ArmHalf + 3.5 + ship.W + 3, bottom + ship.H + 3);
        }

        // the freight collar: Dray berths on arms east and west
        const double ybB = -106;
        foreach (var (fr, nm) in new[] { (E, "E"), (Wd, "W") })
        {
            var r0 = lat.Reach("Freight Collar", dockB, fr.Dx, fr.Dz, ArmHalf);
            var mid = r0 + 8 + Dray.L / 2;
            var r1 = Math.Ceiling(mid + Dray.L / 2 + 6);
            Arm(k, $"Freight Arm {nm}", fr, r0, r1, ybB, [(mid - Dray.L / 2, mid + Dray.L / 2)], false, 90);
            var bottom = ybB - 3.6;
            foreach (var side in new[] { -1, 1 })
            {
                var sn = side < 0 ? "L" : "R";
                Cradle(k, $"Freight Cradle {nm}{sn} 1", fr, side, mid - 22, ybB, bottom, Dray.W, Dray.L, Dray.H, false);
                Cradle(k, $"Freight Cradle {nm}{sn} 2", fr, side, mid, ybB, bottom - 4, Dray.W, Dray.L, Dray.H, true);
                Cradle(k, $"Freight Cradle {nm}{sn} 3", fr, side, mid + 22, ybB, bottom, Dray.W, Dray.L, Dray.H, false);
                ClampArmBase(k, $"Freight Clamp {nm}{sn}", fr, mid + side * 6, side, ybB, side);
            }
        }

        // the radiators off engineering, north and south, edge-on to the sun
        foreach (var (fr, nm) in new[] { (N, "N"), (S, "S") })
        {
            var r0 = lat.Reach("engineering", eng, fr.Dx, fr.Dz, 2);
            Radiator(k, $"Radiator {nm}", fr, r0, r0 + 66, -139, 12, 6, 3);
        }

        lat.Emit();
        foreach (var p in own)
            Finish(p, 120);
        return k;
    }
}
