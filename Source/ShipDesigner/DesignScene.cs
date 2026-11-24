using System.Text;

namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Everything the designer shows and checks, worked out from a design: the hull skins (cut where bodies meet), the pieces where they sit, the
/// rooms and their doors, each socket's link to its room, the issues and the totals. Written as one JSON object for the page.
/// </summary>
public static class DesignScene
{
    /// <summary>False while a piece is being dragged: its opening is cut when it is let go (Karl, 2026-11-23).</summary>
    public static bool Holes = true;

    /// <summary>True while a room or a piece is being dragged: the hulls' meshes are left out (the page keeps the last ones), so a drag stays quick.</summary>
    public static bool Light;

    public sealed record Issue(string Level, string Text, string What);

    public sealed class Check
    {
        public readonly List<Issue> Issues = [];
        public readonly Dictionary<int, bool> RoomOk = [];
        public readonly Dictionary<int, bool> PieceOk = [];
        public readonly List<(double[] A, double[] B, bool Ok, double Shaft, bool Service, double[] N)> Links = [];
        public readonly List<double[]> Doors = [];
        public readonly Dictionary<int, bool> RiserOk = [];
        public readonly List<(int Id, double[] At, double[] S0, double[] S1)> Placed = [];
        public readonly HashSet<int> Reached = [];
    }

    // ---- where a piece sits

    public const double MaxConnector = 12;
    public readonly record struct Place(double[] O, double[] X, double[] Y, double[] Z);

    public static Place Where(HullBody h, Piece p)
    {
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        double[] o, y, z;
        switch (p.Surface)
        {
            case "aft":
                o = [cx + p.U, h.Y + p.V, h.Z + h.Back];
                y = [0, 0, 1];
                z = [0, 1, 0];
                break;
            case "fore":
                o = [cx + p.U, h.Y + p.V, h.Z + h.Front];
                y = [0, 0, -1];
                z = [0, 1, 0];
                break;
            default:
                var s = h.OnSkin(p.Z, p.T, p.Side);
                o = [cx + s.X, h.Y + s.Y, h.Z + s.Z];
                y = [s.Nx, s.Ny, 0];
                z = [0, 0, -1];
                break;
        }
        var x = Cross(y, z);
        // A piece with an up and a down (a gangway, a ladder) is turned about its normal on the port side, so its X stays up.
        if (p.Side < 0 && p.Surface == "skin" && p.Kind is "Gangway" or "Boarding ladder")
        {
            x = [-x[0], -x[1], -x[2]];
            z = [-z[0], -z[1], -z[2]];
        }
        if (p.Turn != 0)
        {
            var a = p.Turn * Math.PI / 180;
            double c = Math.Cos(a), sn = Math.Sin(a);
            var x2 = new[] { x[0] * c + z[0] * sn, x[1] * c + z[1] * sn, x[2] * c + z[2] * sn };
            var z2 = new[] { z[0] * c - x[0] * sn, z[1] * c - x[1] * sn, z[2] * c - x[2] * sn };
            (x, z) = (x2, z2);
        }
        return new Place(o, x, y, z);
    }

    private static double[] Cross(double[] a, double[] b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];

    /// <summary>The world points of a piece and its mirror twin (the twin across its hull copy's centreline).</summary>
    public static IEnumerable<(double[] At, double[] N)> Spots(ShipDesign d, Piece p)
    {
        var h = d.Hull(p.Hull);
        if (h is null)
            yield break;
        var w = Where(h, p);
        var cx = h.CopyX(Math.Min(p.Copy, h.Copies - 1));
        var pair = p.Pair && h.Copies == 2;
        yield return (w.O, w.Y);
        if (pair)
            yield return ([-w.O[0], w.O[1], w.O[2]], [-w.Y[0], w.Y[1], w.Y[2]]);
        if (p.Mirror)
        {
            yield return ([2 * cx - w.O[0], w.O[1], w.O[2]], [-w.Y[0], w.Y[1], w.Y[2]]);
            if (pair)
                yield return ([-(2 * cx - w.O[0]), w.O[1], w.O[2]], [w.Y[0], w.Y[1], w.Y[2]]);
        }
    }

    // ---- hull meshes

    /// <summary>The skin of one copy of a body: the side facets, the keel and the spine, each band a little toned so the stations show; cut where it meets a body set to cut.</summary>
    public static (DesignMesh Skin, DesignMesh Fore, DesignMesh Aft) HullMesh(ShipDesign d, HullBody h, int copy)
    {
        var skin = new DesignMesh();
        var fore = new DesignMesh();
        var aft = new DesignMesh();
        var cx = h.CopyX(copy);
        var (bx0, bx1, by0, by1, bz0, bz1) = h.Bounds();
        // The bodies that cut this one, each with its box, so a facet only tests (and is only cut finely by) those its own box meets.
        var cutters = d.Hulls.Where(o => o != h && (o.Cut || h.Cut)).SelectMany(o => Enumerable.Range(0, o.Copies).Select(c => (B: o, Box: CopyBox(o, c)))).Where(o => Overlap(o.Box, (bx0, bx1, by0, by1, bz0, bz1))).ToList();
        var near = new List<HullBody>();
        // The openings the sockets on this copy cut through the skin (each a frame and its half sizes).
        var holes = new List<(Place W, double Hx, double Hz, bool Round)>();
        foreach (var p in Holes ? d.Pieces : [])
        {
            if (p.Hull != h.Id || PieceKinds.Find(p.Kind)?.Opening is not { } op)
                continue;
            var own = Math.Min(p.Copy, h.Copies - 1) == copy;
            // On the other body of a pair, its twin across the ship.
            if (!own && !(p.Pair && h.Copies == 2))
                continue;
            double[] F(double[] v) => [-v[0], v[1], v[2]];
            var w = Where(h, p);
            if (!own)
                w = new Place([-w.O[0], w.O[1], w.O[2]], F(w.X), F(w.Y), F(w.Z));
            holes.Add((w, op.Hx * p.Size, op.Hz * p.Size, op.Round));
            if (p.Mirror)
                holes.Add((new Place([2 * cx - w.O[0], w.O[1], w.O[2]], F(w.X), F(w.Y), F(w.Z)), op.Hx * p.Size, op.Hz * p.Size, op.Round));
        }
        // The mouths of the connections that reach this body: its skin opens into them (Karl, 2026-11-24: one mesh, seen through).
        var mouths = d.Joins.SelectMany(j => j.Mouths(d)).Where(m => m.Hull == h.Id).ToList();
        // Every opening as an outline in its own plane (Karl, 2026-11-24: holes cut clean along their edge, not cell by cell).
        var cutouts = new List<Cutout>();
        foreach (var (w, hx, hz, round) in holes)
            cutouts.Add(new Cutout(w.O, w.X, w.Y, w.Z, round ? Ellipse(hx, hx) : Rect(hx, hz)));
        foreach (var m in mouths)
            cutouts.Add(new Cutout(m.C, m.S, m.T, m.U, m.Square ? Rect(m.W / 2, m.H / 2) : Ellipse(m.W / 2, m.H / 2)));
        // Hull windows cut the skin where they lie on it (Karl, 2026-11-24): the opening in the window's own plane, its size as laid.
        foreach (var hw in d.Windows)
        {
            // Steep skin (a hull turning under the window) is cut too: only facets almost along the rays are left whole.
            // Cut along the very rays the glass was laid by (straight out of the wall), so the hole is the glass's own outline on
            // the skin however the hull curves (Karl, 2026-11-24: a flat plane cut a box out of a curved side).
            var f = WindowFrame(d, hw);
            if (f is { } q)
            {
                // Only the skin the glass lies on: a band along the rays from just inside its nearest point to just past its
                // farthest, so a cut never runs on through a nose or a far wall (Karl, 2026-11-24: a window took a nose off).
                var (lp, _, _, _, _, _) = Lay(d, hw);
                double lo = double.MaxValue, hi = double.MinValue;
                for (var k = 0; k + 2 < lp.Length; k += 3)
                {
                    var nn = (lp[k] - q.O[0]) * q.N[0] + (lp[k + 1] - q.O[1]) * q.N[1] + (lp[k + 2] - q.O[2]) * q.N[2];
                    lo = Math.Min(lo, nn); hi = Math.Max(hi, nn);
                }
                if (lo <= hi)
                    cutouts.Add(new Cutout(q.O, q.U, q.N, q.V, Rect(q.Hx - 0.01, q.Hz - 0.01), 0, (lo - 0.12, hi + 0.08)));
            }
        }
        void Emit(DesignMesh m, (double, double, double) a, (double, double, double) b, (double, double, double) c, (double, double, double) e, (byte, byte, byte) col)
        {
            List<List<(double, double, double)>> polys = [[a, b, c, e]];
            foreach (var k in cutouts)
            {
                if (!k.Near(a, c))
                    continue;
                var next = new List<List<(double, double, double)>>();
                foreach (var poly in polys)
                    next.AddRange(k.Subtract(poly));
                polys = next;
                if (polys.Count == 0)
                    return;
            }
            foreach (var poly in polys)
                for (var i = 1; i < poly.Count - 1; i++)
                    m.Tri(poly[0], poly[i], poly[i + 1], col, false);
        }
        bool InHole(double x, double y, double z)
        {
            foreach (var m in mouths)
                if (m.Holds(x, y, z))
                    return true;
            foreach (var (w, hx, hz, round) in holes)
            {
                double dx = x - w.O[0], dy = y - w.O[1], dz = z - w.O[2];
                var ly = dx * w.Y[0] + dy * w.Y[1] + dz * w.Y[2];
                if (Math.Abs(ly) > 0.8)
                    continue;
                var lx = dx * w.X[0] + dy * w.X[1] + dz * w.X[2];
                var lz = dx * w.Z[0] + dy * w.Z[1] + dz * w.Z[2];
                if (round ? lx * lx + lz * lz < hx * hx : Math.Abs(lx) < hx && Math.Abs(lz) < hz)
                    return true;
            }
            return false;
        }
        bool CutBody(double x, double y, double z)
        {
            foreach (var o in near)
                if (o.Depth(x, y, z) > 0.02)
                    return true;
            return false;
        }
        bool Cut(double x, double y, double z)
        {
            if (InHole(x, y, z))
                return true;
            foreach (var o in near)
                if (o.Depth(x, y, z) > 0.02)
                    return true;
            return false;
        }
        // Picks the cutters a facet's box (grown 5 cm) meets; true when there is one.
        bool Near((double X, double Y, double Z) p0, (double X, double Y, double Z) p1, (double X, double Y, double Z) p2, (double X, double Y, double Z) p3)
        {
            near.Clear();
            double x0 = cx + Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)) - 0.05, x1 = cx + Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X)) + 0.05;
            double y0 = h.Y + Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)) - 0.05, y1 = h.Y + Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y)) + 0.05;
            double z0 = h.Z + Math.Min(p0.Z, p2.Z) - 0.05, z1 = h.Z + Math.Max(p0.Z, p2.Z) + 0.05;
            foreach (var o in cutters)
                if (Overlap(o.Box, (x0, x1, y0, y1, z0, z1)))
                    if (!near.Contains(o.B))
                        near.Add(o.B);
            return near.Count > 0;
        }
        void Facet(DesignMesh m, (double X, double Y, double Z) p0, (double X, double Y, double Z) p1, (double X, double Y, double Z) p2, (double X, double Y, double Z) p3,
            (byte, byte, byte) c, bool holed, double step, bool flip)
        {
            var cutting = Near(p0, p1, p2, p3);
            var nz = !cutting ? 1 : Math.Clamp((int)Math.Ceiling((p2.Z - p0.Z) / step), 1, 80);
            Strip(m, cx, h, p0, p1, p2, p3, c, nz, cutting, CutBody, flip, step, cutouts.Count > 0 ? Emit : null);
        }

        var col = DesignMesh.Hex(h.Skin);
        var dark = DesignMesh.Hex("#3a3e44");
        var n = h.Points;
        for (var i = 0; i < h.Stations.Count - 1; i++)
        {
            var a = h.Stations[i];
            var b = h.Stations[i + 1];
            var band = i % 2 == 0 ? 1.0 : 0.94;
            // A band with a socket's opening in it is cut finely (20 cm), one another body cuts at 50 cm, the rest whole.
            var holed = holes.Any(q => q.W.O[2] > h.Z + a.Z - 2 && q.W.O[2] < h.Z + b.Z + 2)
                || mouths.Any(q => q.C[2] > h.Z + a.Z - Math.Max(q.W, q.H) && q.C[2] < h.Z + b.Z + Math.Max(q.W, q.H));
            var step = 0.5;
            foreach (var s in new[] { -1, 1 })
                for (var k = 0; k < n - 1; k++)
                {
                    var shade = DesignMesh.Tone(col, band * (k % 2 == 0 ? 1.0 : 0.9));
                    Facet(skin, (s * a.P[k].H, a.P[k].Y, a.Z), (s * a.P[k + 1].H, a.P[k + 1].Y, a.Z), (s * b.P[k].H, b.P[k].Y, b.Z), (s * b.P[k + 1].H, b.P[k + 1].Y, b.Z), shade, holed, step, s < 0);
                }
            // The keel (across the bottom) and the spine (across the top).
            Facet(skin, (-a.P[0].H, a.P[0].Y, a.Z), (a.P[0].H, a.P[0].Y, a.Z), (-b.P[0].H, b.P[0].Y, b.Z), (b.P[0].H, b.P[0].Y, b.Z), DesignMesh.Tone(dark, band), holed, step, false);
            Facet(skin, (a.P[n - 1].H, a.P[n - 1].Y, a.Z), (-a.P[n - 1].H, a.P[n - 1].Y, a.Z), (b.P[n - 1].H, b.P[n - 1].Y, b.Z), (-b.P[n - 1].H, b.P[n - 1].Y, b.Z), DesignMesh.Tone(col, band * 1.05), holed, step, false);
        }
        near.Clear();
        near.AddRange(cutters.Select(o => o.B).Distinct());
        Cap(fore, cx, h, h.Stations[0], DesignMesh.Tone(col, 0.85), Cut);
        Cap(aft, cx, h, h.Stations[^1], DesignMesh.Tone(dark, 1.2), Cut);
        return (skin, fore, aft);
    }

    /// <summary>An opening's outline: a convex polygon (u along X, v along Z) in the plane through O facing Y.</summary>
    private sealed class Cutout(double[] o, double[] x, double[] y, double[] z, List<(double U, double V)> poly, double edgeMin = 0.35, (double Lo, double Hi)? band = null)
    {
        private readonly double _reach = poly.Max(q => Math.Sqrt(q.U * q.U + q.V * q.V));

        private (double U, double V, double N) Local((double X, double Y, double Z) p)
        {
            double dx = p.X - o[0], dy = p.Y - o[1], dz = p.Z - o[2];
            return (dx * x[0] + dy * x[1] + dz * x[2], dx * z[0] + dy * z[1] + dz * z[2], dx * y[0] + dy * y[1] + dz * y[2]);
        }

        /// <summary>Whether a facet (by two opposite corners) may reach the opening.</summary>
        public bool Near((double, double, double) a, (double, double, double) c)
        {
            var la = Local(a);
            var lc = Local(c);
            double mu = (la.U + lc.U) / 2, mv = (la.V + lc.V) / 2, mn = (la.N + lc.N) / 2;
            var half = Math.Sqrt(Math.Pow(la.U - lc.U, 2) + Math.Pow(la.V - lc.V, 2) + Math.Pow(la.N - lc.N, 2)) / 2;
            if (band is { } bd)
                return mn > bd.Lo - half && mn < bd.Hi + half && Math.Sqrt(mu * mu + mv * mv) < _reach + half;
                        return Math.Abs(mn) < 0.8 + half && Math.Sqrt(mu * mu + mv * mv) < _reach + half;
        }

        /// <summary>A convex polygon less the opening: the parts outside each of its edges in turn (each convex).</summary>
        public List<List<(double, double, double)>> Subtract(List<(double, double, double)> poly0)
        {
            var outv = new List<List<(double, double, double)>>();
            // Points far off the opening's plane (the skin's far side) are left whole.
            // Facets turned edge-on to the opening (running along its axis, as at a pointed nose) are left whole too: a cut
            // through them would take the whole end off (Karl, 2026-11-24: an old window at the bow broke the hull).
            double fx = 0, fy = 0, fz = 0;
            for (var i = 0; i < poly0.Count; i++)
            {
                var (p, q) = (poly0[i], poly0[(i + 1) % poly0.Count]);
                fx += (p.Item2 - q.Item2) * (p.Item3 + q.Item3);
                fy += (p.Item3 - q.Item3) * (p.Item1 + q.Item1);
                fz += (p.Item1 - q.Item1) * (p.Item2 + q.Item2);
            }
            var fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
            var edgeOn = fl > 1e-9 && Math.Abs((fx * y[0] + fy * y[1] + fz * y[2]) / fl) < edgeMin;
            if (band is { } bd)
            {
                // Outside the band stays whole; only the slice inside it is cut.
                var below = Clip(poly0, q => bd.Lo - Local(q).N);
                var above = Clip(poly0, q => Local(q).N - bd.Hi);
                if (below.Count >= 3) outv.Add(below);
                if (above.Count >= 3) outv.Add(above);
                poly0 = Clip(Clip(poly0, q => Local(q).N - bd.Lo), q => bd.Hi - Local(q).N);
                if (poly0.Count < 3)
                    return outv;
            }
            else if (edgeOn || poly0.All(q => Math.Abs(Local(q).N) > 0.8))
            {
                outv.Add(poly0);
                return outv;
            }
            var rest = poly0;
            for (var i = 0; i < poly.Count && rest.Count >= 3; i++)
            {
                var (a, b) = (poly[i], poly[(i + 1) % poly.Count]);
                // Signed side of the line a->b (the polygon runs counter-clockwise: inside is to the left).
                double Side((double, double, double) q) { var l = Local(q); return (b.U - a.U) * (l.V - a.V) - (b.V - a.V) * (l.U - a.U); }
                var outside = Clip(rest, q => -Side(q));
                if (outside.Count >= 3)
                    outv.Add(outside);
                rest = Clip(rest, Side);
            }
            return outv;
        }

        /// <summary>The part of a polygon where f is at least 0.</summary>
        private static List<(double, double, double)> Clip(List<(double, double, double)> p, Func<(double, double, double), double> f)
        {
            var r = new List<(double, double, double)>();
            for (var i = 0; i < p.Count; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % p.Count];
                double fa = f(a), fb = f(b);
                if (fa >= 0)
                    r.Add(a);
                if ((fa >= 0) != (fb >= 0))
                {
                    var t = fa / (fa - fb);
                    r.Add((a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t));
                }
            }
            return r;
        }
    }

    private static List<(double U, double V)> Rect(double hu, double hv) => [(-hu, -hv), (hu, -hv), (hu, hv), (-hu, hv)];

    private static List<(double U, double V)> Ellipse(double hu, double hv)
    {
        var l = new List<(double, double)>();
        for (var i = 0; i < 16; i++)
            l.Add((hu * Math.Cos(i * Math.PI / 8), hv * Math.Sin(i * Math.PI / 8)));
        return l;
    }

    /// <summary>One copy's box (a mirrored pair's whole box spans the gap between its copies).</summary>
    private static (double, double, double, double, double, double) CopyBox(HullBody o, int c)
    {
        var b = o.Bounds();
        var w = o.Copies == 1 ? 0 : (b.X1 - b.X0 - Math.Abs(o.CopyX(1) - o.CopyX(0))) / 2;
        return o.Copies == 1 ? b : (o.CopyX(c) - w, o.CopyX(c) + w, b.Y0, b.Y1, b.Z0, b.Z1);
    }

    private static bool Overlap((double, double, double, double, double, double) a, (double, double, double, double, double, double) b) =>
        a.Item1 < b.Item2 && b.Item1 < a.Item2 && a.Item3 < b.Item4 && b.Item3 < a.Item4 && a.Item5 < b.Item6 && b.Item5 < a.Item6;

    /// <summary>One facet between two stations as a grid of quads (nz along, more across where cutting), leaving out those inside a cutter.</summary>
    private static void Strip(DesignMesh m, double cx, HullBody h, (double X, double Y, double Z) a0, (double X, double Y, double Z) a1, (double X, double Y, double Z) b0,
        (double X, double Y, double Z) b1, (byte, byte, byte) col, int nz, bool cutting, Func<double, double, double, bool> cut, bool flip, double step = 0.5,
        Action<DesignMesh, (double, double, double), (double, double, double), (double, double, double), (double, double, double), (byte, byte, byte)>? emit = null)
    {
        var across = Math.Sqrt(Math.Pow(a1.X - a0.X, 2) + Math.Pow(a1.Y - a0.Y, 2));
        var nk = cutting ? Math.Clamp((int)Math.Ceiling(across / step), 1, 40) : 1;
        (double, double, double) Pt(double u, double v)
        {
            var px = a0.X + (a1.X - a0.X) * u + ((b0.X + (b1.X - b0.X) * u) - (a0.X + (a1.X - a0.X) * u)) * v;
            var py = a0.Y + (a1.Y - a0.Y) * u + ((b0.Y + (b1.Y - b0.Y) * u) - (a0.Y + (a1.Y - a0.Y) * u)) * v;
            var pz = a0.Z + (b0.Z - a0.Z) * v;
            return (cx + px, h.Y + py, h.Z + pz);
        }
        for (var j = 0; j < nz; j++)
            for (var i = 0; i < nk; i++)
            {
                double u0 = (double)i / nk, u1 = (double)(i + 1) / nk, v0 = (double)j / nz, v1 = (double)(j + 1) / nz;
                if (cutting)
                {
                    var (mx, my, mz) = Pt((u0 + u1) / 2, (v0 + v1) / 2);
                    if (cut(mx, my, mz))
                        continue;
                }
                var (q0, q1, q2, q3) = flip ? (Pt(u0, v0), Pt(u0, v1), Pt(u1, v1), Pt(u1, v0)) : (Pt(u0, v0), Pt(u1, v0), Pt(u1, v1), Pt(u0, v1));
                if (emit is not null)
                    emit(m, q0, q1, q2, q3, col);
                else
                    m.Quad(q0, q1, q2, q3, col, false);
            }
    }

    /// <summary>An end closed flat: a fan from the middle of the profile round both sides.</summary>
    private static void Cap(DesignMesh m, double cx, HullBody h, Station s, (byte, byte, byte) col, Func<double, double, double, bool> cut)
    {
        var ring = new List<(double, double, double)>();
        foreach (var (hh, y) in s.P)
            ring.Add((cx + hh, h.Y + y, h.Z + s.Z));
        for (var k = s.P.Count - 1; k >= 0; k--)
            ring.Add((cx - s.P[k].H, h.Y + s.P[k].Y, h.Z + s.Z));
        var mid = (cx, h.Y + (s.P[0].Y + s.P[^1].Y) / 2, h.Z + s.Z);
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            if (cut((a.Item1 + b.Item1 + mid.Item1) / 3, (a.Item2 + b.Item2 + mid.Item2) / 3, (a.Item3 + b.Item3 + mid.Item3) / 3))
                continue;
            m.Tri(mid, a, b, col, false);
        }
    }

    // ---- checks

    /// <summary>How many of a room's edge samples are outside the skin (the room check's own samples).</summary>
    public static int Outside(ShipDesign d, Room r) => Outside(d, r, null);

    public static int Outside(ShipDesign d, Room r, List<(double X, double Z)>? where)
    {
        var spines = d.Joins.Select(j => (J: j, S: j.Spine(d))).ToList();
        var dk = d.DeckOf(r);
        var n = 0;
        foreach (var up in new[] { 0.05, dk.Height - 0.02 })
        {
            var nx = Math.Max(1, (int)Math.Ceiling(r.X1 - r.X0));
            var nz = Math.Max(1, (int)Math.Ceiling(r.Z1 - r.Z0));
            for (var i = 0; i <= nx; i++)
                for (var j = 0; j <= nz; j++)
                {
                    if (i != 0 && i != nx && j != 0 && j != nz)
                        continue;
                    var v = r.Z0 + (r.Z1 - r.Z0) * j / nz;
                    var (el, er) = Edges(d, r, v, up);
                    var w = d.World(dk, i == 0 ? el : i == nx ? er : r.X0 + (r.X1 - r.X0) * i / nx, v, up);
                    if (!d.Hulls.Any(h => h.Depth(w[0], w[1], w[2]) >= 0.06) && !spines.Any(q => q.J.Inside(q.S, w[0], w[1], w[2], 0.06)))
                    {
                        n++;
                        where?.Add((r.X0 + (r.X1 - r.X0) * i / nx, v));
                    }
                }
        }
        return n;
    }

    /// <summary>After a deck moves (Karl, 2026-11-24): each room on it goes back to the plan it was drawn at, then shrinks a
    /// quarter metre at a time from whichever side frees most of the samples outside the skin. A room out to the hull follows
    /// the skin by itself (its open sides), so it only shrinks where its closed sides or its ends poke out.</summary>
    public static void FitDeck(ShipDesign d, int deck)
    {
        foreach (var r in d.Rooms)
        {
            if (r.Deck != deck)
                continue;
            r.Want ??= [r.X0, r.X1, r.Z0, r.Z1];
            (r.X0, r.X1, r.Z0, r.Z1) = (r.Want[0], r.Want[1], r.Want[2], r.Want[3]);
            var outs = new List<(double X, double Z)>();
            var bad = Outside(d, r, outs);
            // Each sample outside votes for the side nearest it; the side with most votes comes in a quarter metre.
            for (var k = 0; k < 160 && bad > 0; k++)
            {
                var votes = new double[4];
                foreach (var (x, z) in outs)
                {
                    double[] dist = [x - r.X0, r.X1 - x, z - r.Z0, r.Z1 - z];
                    var m = 0;
                    for (var q = 1; q < 4; q++)
                        if (dist[q] < dist[m])
                            m = q;
                    votes[m]++;
                }
                var side = -1;
                for (var q = 0; q < 4; q++)
                {
                    var room = q < 2 ? r.X1 - r.X0 : r.Z1 - r.Z0;
                    if (votes[q] > 0 && room >= 1.25 && (side < 0 || votes[q] > votes[side]))
                        side = q;
                }
                if (side < 0)
                    break;
                switch (side)
                {
                    case 0: r.X0 += 0.25; break;
                    case 1: r.X1 -= 0.25; break;
                    case 2: r.Z0 += 0.25; break;
                    default: r.Z1 -= 0.25; break;
                }
                outs.Clear();
                bad = Outside(d, r, outs);
            }
            if (bad > 0)
                (r.X0, r.X1, r.Z0, r.Z1) = (r.Want[0], r.Want[1], r.Want[2], r.Want[3]); // it can't fit at this height: keep its plan, the check says so
        }
    }

    /// <summary>A hull window laid on the skin: a grid of points (nu along the wall, nv up) found by going out from the room's
    /// wall along its normal to where the line leaves the hull, so the glass follows the ship's contour. Gap is the farthest the
    /// skin is from the wall; Ok when every point found the skin within <see cref="HullWindow.MaxGap"/>.</summary>
    /// <summary>A hull window's frame on its wall: middle, unit axes along the wall (U), up (V) and out (N, the ray it is laid by),
    /// and half its size; null when its room is gone.</summary>
    public static (double[] O, double[] U, double[] V, double[] N, double Hx, double Hz)? WindowFrame(ShipDesign d, HullWindow w)
    {
        var r = d.RoomById(w.Room);
        if (r is null)
            return null;
        var dk = d.DeckOf(r);
        var alongZ = w.Side is "x0" or "x1";
        double lo = alongZ ? r.Z0 : r.X0, hi = alongZ ? r.Z1 : r.X1, len = hi - lo;
        double wallAt = w.Side switch { "x0" => r.X0, "x1" => r.X1, "z0" => r.Z0, _ => r.Z1 };
        double du = w.Side == "x0" ? -1 : w.Side == "x1" ? 1 : 0, dv = w.Side == "z0" ? -1 : w.Side == "z1" ? 1 : 0;
        var width = Math.Min(w.W, len);
        var mid = lo + width / 2 + (len - width) * w.T;
        var top = Math.Min(w.Sill + w.H, dk.Height - 0.05);
        var foot = Math.Min(w.Sill, top - 0.2);
        double pu = alongZ ? wallAt : mid, pv = alongZ ? mid : wallAt, up = (foot + top) / 2;
        var o = d.World(dk, pu, pv, up);
        double[] Dir(double[] q) { var v = new[] { q[0] - o[0], q[1] - o[1], q[2] - o[2] }; var l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); return l < 1e-9 ? v : [v[0] / l, v[1] / l, v[2] / l]; }
        var u = Dir(d.World(dk, pu + (alongZ ? 0 : 1), pv + (alongZ ? 1 : 0), up));
        var vv = Dir(d.World(dk, pu, pv, up + 1));
        var n = Dir(d.World(dk, pu + du, pv + dv, up));
        return (o, u, vv, n, width / 2, (top - foot) / 2);
    }

    public static (double[] Pts, int Nu, int Nv, double Gap, bool Ok, double Len) Lay(ShipDesign d, HullWindow w, double grow = 0, double lift = 0.03)
    {
        var r = d.RoomById(w.Room);
        if (r is null)
            return ([], 0, 0, 0, false, 0);
        var dk = d.DeckOf(r);
        var alongZ = w.Side is "x0" or "x1";
        double lo = alongZ ? r.Z0 : r.X0, hi = alongZ ? r.Z1 : r.X1, len = hi - lo;
        double wallAt = w.Side switch { "x0" => r.X0, "x1" => r.X1, "z0" => r.Z0, _ => r.Z1 };
        double du = w.Side == "x0" ? -1 : w.Side == "x1" ? 1 : 0, dv = w.Side == "z0" ? -1 : w.Side == "z1" ? 1 : 0;
        var width = Math.Min(w.W, len);
        var mid = lo + width / 2 + (len - width) * w.T;
        var top = Math.Min(w.Sill + w.H, dk.Height - 0.05);
        var foot = Math.Min(w.Sill, top - 0.2);
        (width, top, foot) = (width + 2 * grow, top + grow, foot - grow); // the boarding's outer edge, laid the same way
        int nu = Math.Max(2, (int)Math.Ceiling(width / 0.1) + 1), nv = Math.Max(2, (int)Math.Ceiling((top - foot) / 0.05) + 1);
        var pts = new double[nu * nv * 3];
        var ray = new double[nu * nv * 3];
        var gap = 0.0;
        var ok = true;
        for (var j = 0; j < nv; j++)
            for (var i = 0; i < nu; i++)
            {
                var s = mid - width / 2 + width * i / (nu - 1);
                var up = foot + (top - foot) * j / (nv - 1);
                // A room out to the hull has its side walls at the skin (lined in): start from where it is drawn.
                var wallU = wallAt;
                if (alongZ)
                {
                    var (el, er) = Edges(d, r, s, up);
                    wallU = w.Side == "x0" ? el : er;
                }
                double pu = alongZ ? wallU : s, pv = alongZ ? s : wallAt;
                var p = d.World(dk, pu, pv, up);
                var q1 = d.World(dk, pu + du, pv + dv, up);
                double nx = q1[0] - p[0], ny = q1[1] - p[1], nz = q1[2] - p[2];
                var f = 0.0;
                var found = false;
                for (; f <= HullWindow.MaxGap + 0.02; f += 0.02)
                    if (!d.Hulls.Any(h => h.Depth(p[0] + nx * f, p[1] + ny * f, p[2] + nz * f) > 0))
                    {
                        found = true;
                        break;
                    }
                if (!found)
                {
                    ok = false;
                    f = HullWindow.MaxGap;
                }
                else
                {
                    // Close in on the skin between the last step inside and the first outside: on skin the ray meets at a slant,
                    // a 2 cm step leaves the glass well off it.
                    double lo2 = Math.Max(0, f - 0.02), hi2 = f;
                    for (var it = 0; it < 12 && f > 0; it++)
                    {
                        var m = (lo2 + hi2) / 2;
                        if (d.Hulls.Any(h => h.Depth(p[0] + nx * m, p[1] + ny * m, p[2] + nz * m) > 0)) lo2 = m; else hi2 = m;
                    }
                    f = hi2;
                }
                gap = Math.Max(gap, f);
                var k = (j * nu + i) * 3;
                (pts[k], pts[k + 1], pts[k + 2]) = (p[0] + nx * f, p[1] + ny * f, p[2] + nz * f);
                (ray[k], ray[k + 1], ray[k + 2]) = (nx, ny, nz);
            }
        // Stand it off the skin along the skin's own normal (from the grid's neighbours), not along the ray: where the hull turns
        // under the window a step along the ray slides along the skin and leaves the glass in it (Karl, 2026-11-24).
        var outp = new double[pts.Length];
        for (var j = 0; j < nv; j++)
            for (var i = 0; i < nu; i++)
            {
                int k = (j * nu + i) * 3, a = (j * nu + Math.Max(0, i - 1)) * 3, b = (j * nu + Math.Min(nu - 1, i + 1)) * 3,
                    c = (Math.Max(0, j - 1) * nu + i) * 3, e = (Math.Min(nv - 1, j + 1) * nu + i) * 3;
                double ux = pts[b] - pts[a], uy = pts[b + 1] - pts[a + 1], uz = pts[b + 2] - pts[a + 2];
                double vx = pts[e] - pts[c], vy = pts[e + 1] - pts[c + 1], vz = pts[e + 2] - pts[c + 2];
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx, cl = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (cl < 1e-9) (cx, cy, cz, cl) = (ray[k], ray[k + 1], ray[k + 2], 1);
                var sg = cx * ray[k] + cy * ray[k + 1] + cz * ray[k + 2] < 0 ? -1 : 1;
                for (var t = 0; t < 3; t++)
                    outp[k + t] = pts[k + t] + sg * lift * (t == 0 ? cx : t == 1 ? cy : cz) / cl;
            }
        return (outp, nu, nv, gap, ok, len);
    }

    /// <summary>An extension's two rings in world space, the same count each: for a wall, its outline at the floor and at the
    /// ceiling; up, its profile at either end of the room's longer side. Ok when every point is inside the skin.</summary>
    public static (List<double[]> A, List<double[]> B) ExtRings(ShipDesign d, Room r, RoomExt e)
    {
        var dk = d.DeckOf(r);
        var (ra, rb) = (new List<double[]>(), new List<double[]>());
        if (e.Side == "up")
        {
            var acrossX = r.X1 - r.X0 <= r.Z1 - r.Z0; // the profile goes across the narrower side
            double w = acrossX ? r.X1 - r.X0 : r.Z1 - r.Z0, mid = acrossX ? (r.X0 + r.X1) / 2 : (r.Z0 + r.Z1) / 2;
            double l0 = acrossX ? r.Z0 : r.X0, l1 = acrossX ? r.Z1 : r.X1;
            foreach (var (a, b) in e.Outline(w))
            {
                ra.Add(acrossX ? d.World(dk, mid + a, l0, dk.Height + b) : d.World(dk, l0, mid + a, dk.Height + b));
                rb.Add(acrossX ? d.World(dk, mid + a, l1, dk.Height + b) : d.World(dk, l1, mid + a, dk.Height + b));
            }
            return (ra, rb);
        }
        var alongZ = e.Side is "x0" or "x1";
        double lo = alongZ ? r.Z0 : r.X0, len = (alongZ ? r.Z1 : r.X1) - lo, width = Math.Min(e.Width, len);
        double m = lo + width / 2 + (len - width) * e.T, wall = e.Side switch { "x0" => r.X0, "x1" => r.X1, "z0" => r.Z0, _ => r.Z1 };
        var sg = e.Side is "x0" or "z0" ? -1 : 1;
        foreach (var (a, b) in e.Outline(width))
        {
            double u = alongZ ? wall + sg * b : m + a, v = alongZ ? m + a : wall + sg * b;
            ra.Add(d.World(dk, u, v, 0.02));
            rb.Add(d.World(dk, u, v, dk.Height - 0.02));
        }
        return (ra, rb);
    }

    public static Check Run(ShipDesign d)
    {
        var c = new Check();
        var spines = d.Joins.Select(j => (J: j, S: j.Spine(d))).ToList();
        // Rooms inside the skin (with 6 cm for the plates, as the Zero Day's MustFit), and apart.
        foreach (var r in d.Rooms)
        {
            var ok = true;
            var dk = d.DeckOf(r);
            foreach (var up in new[] { 0.05, dk.Height - 0.02 })
            {
                var nx = Math.Max(1, (int)Math.Ceiling(r.X1 - r.X0));
                var nz = Math.Max(1, (int)Math.Ceiling(r.Z1 - r.Z0));
                for (var i = 0; i <= nx && ok; i++)
                    for (var j = 0; j <= nz && ok; j++)
                    {
                        if (i != 0 && i != nx && j != 0 && j != nz)
                            continue; // the edges only
                        var v = r.Z0 + (r.Z1 - r.Z0) * j / nz;
                        var (el, er) = Edges(d, r, v, up);
                        var w = d.World(dk, i == 0 ? el : i == nx ? er : r.X0 + (r.X1 - r.X0) * i / nx, v, up);
                        if (!d.Hulls.Any(h => h.Depth(w[0], w[1], w[2]) >= 0.06) && !spines.Any(q => q.J.Inside(q.S, w[0], w[1], w[2], 0.06)))
                        {
                            ok = false;
                            c.Issues.Add(new("error", $"{Name(r)} on deck {r.Deck + 1} pokes through the skin near x {w[0]:0.0}, y {w[1]:0.0}, z {w[2]:0.0}", $"room:{r.Id}"));
                        }
                    }
            }
            foreach (var o in d.Rooms)
                if (o.Id < r.Id && o.Deck == r.Deck && Math.Min(r.X1, o.X1) - Math.Max(r.X0, o.X0) > 0.01 && Math.Min(r.Z1, o.Z1) - Math.Max(r.Z0, o.Z0) > 0.01)
                {
                    ok = false;
                    c.RoomOk[o.Id] = false;
                    c.Issues.Add(new("error", $"{Name(r)} and {Name(o)} overlap on deck {r.Deck + 1}", $"room:{r.Id}"));
                }
            if (!c.RoomOk.ContainsKey(r.Id))
                c.RoomOk[r.Id] = ok;
        }

        // Doors: rooms on one deck that share at least 0.9 m of wall; shafts join the decks they overlap.
        var joins = d.Rooms.ToDictionary(r => r.Id, _ => new List<int>());
        foreach (var a in d.Rooms)
            foreach (var b in d.Rooms)
            {
                if (a.Id >= b.Id)
                    continue;
                if (a.Deck == b.Deck)
                {
                    var dk = d.Decks[a.Deck];
                    var oz = Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0);
                    var ox = Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0);
                    double[]? door = null;
                    // The wall they share (plan ends), and where along it the door is: a placed door's T, else the middle.
                    // The wall they share (on plan: along x at zw, or along z at xw, from w0 to w1), if any.
                    bool alongZ = oz > 0.01 && (Math.Abs(a.X1 - b.X0) < 0.02 || Math.Abs(b.X1 - a.X0) < 0.02);
                    bool alongX = !alongZ && ox > 0.01 && (Math.Abs(a.Z1 - b.Z0) < 0.02 || Math.Abs(b.Z1 - a.Z0) < 0.02);
                    double wall = alongZ ? (Math.Abs(a.X1 - b.X0) < 0.02 ? a.X1 : a.X0) : (Math.Abs(a.Z1 - b.Z0) < 0.02 ? a.Z1 : a.Z0);
                    double w0 = alongZ ? Math.Max(a.Z0, b.Z0) : Math.Max(a.X0, b.X0), w1 = alongZ ? Math.Min(a.Z1, b.Z1) : Math.Min(a.X1, b.X1);
                    double[] At(double s, double up) => alongZ ? d.World(dk, wall, s, up) : d.World(dk, s, wall, up);
                    // A door (0.9 m) or a placed window (its width) slides between the wall's ends kept clear by half its width.
                    foreach (var k in d.Doors)
                    {
                        if (!k.Joins(a, b) || !(alongZ || alongX))
                            continue;
                        var half = k.W / 2; // a door or a window: its own width (Karl, 2026-11-24: more door sizes)
                        if (w1 - w0 < 2 * half - 0.01)
                            continue; // too short a wall: the check says so below
                        double lo = w0 + half, hi = w1 - half, mid = lo + (hi - lo) * k.T;
                        c.Placed.Add((k.Id, At(mid, 0), At(lo, 0), At(hi, 0)));
                    }
                    var placed = d.Doors.Find(k => k.Kind == "Door" && k.Joins(a, b));
                    var t = placed?.T ?? 0.5;
                    double[]? s0 = null, s1 = null;
                    var dw = placed?.W ?? 0.9;
                    if ((alongZ || alongX) && w1 - w0 >= dw - 0.01)
                    {
                        double lo = w0 + dw / 2, hi = w1 - dw / 2;
                        door = At(lo + (hi - lo) * t, 0);
                        (s0, s1) = (At(lo, 0), At(hi, 0));
                    }
                    var segments = a.Group != 0 && a.Group == b.Group && (ox >= -0.02 && oz > 0.01 || oz >= -0.02 && ox > 0.01);
                    if (door is not null || segments)
                    {
                        joins[a.Id].Add(b.Id);
                        joins[b.Id].Add(a.Id);
                        if (!segments)
                            c.Doors.Add(door!);
                    }
                }
                else if (d.Neighbours(a.Deck, b.Deck) && (a.Kind == "Shaft" || b.Kind == "Shaft") &&
                         Math.Min(a.X1, b.X1) - Math.Max(a.X0, b.X0) >= 0.8 && Math.Min(a.Z1, b.Z1) - Math.Max(a.Z0, b.Z0) >= 0.8)
                {
                    joins[a.Id].Add(b.Id);
                    joins[b.Id].Add(a.Id);
                    var s = a.Kind == "Shaft" ? a : b;
                    var hi = d.Decks[a.Deck].Floor > d.Decks[b.Deck].Floor != d.Stacked ? d.Decks[a.Deck] : d.Decks[b.Deck];
                    c.Doors.Add(d.World(hi, (s.X0 + s.X1) / 2, (s.Z0 + s.Z1) / 2, 0));
                }
            }

        foreach (var k in d.Doors)
            if (!c.Placed.Any(q => q.Id == k.Id))
            {
                var (ra, rb) = (d.RoomById(k.A), d.RoomById(k.B));
                if (ra is not null && rb is not null)
                    c.Issues.Add(new("warn", $"The {k.Kind.ToLowerInvariant()} between {Name(ra)} and {Name(rb)} has no wall to sit in: they need {k.W:0.0} m of shared wall on one deck", $"room:{ra.Id}"));
            }

        foreach (var r in d.Rooms)
            foreach (var e in r.Exts)
            {
                var (ra, rb) = ExtRings(d, r, e);
                if (ra.Concat(rb).Any(p => !d.Hulls.Any(h => h.Depth(p[0], p[1], p[2]) >= 0.06)))
                    c.Issues.Add(new("warn", $"{Name(r)}'s {e.Shape.ToLowerInvariant()} extension ({(e.Side == "up" ? "up" : "out")} {e.Depth:0.0} m) pokes through the skin: make it smaller", $"room:{r.Id}"));
            }
        foreach (var w in d.Windows)
        {
            var (_, _, _, gap, ok, _) = Lay(d, w);
            var r = d.RoomById(w.Room);
            if (!ok && r is not null)
                c.Issues.Add(new("warn", $"The hull window in {Name(r)} is more than {HullWindow.MaxGap:0} m from the skin: move the room out to the hull or the window along", $"room:{r.Id}"));
        }

        // Ladders and lifts: each joins the rooms it stands in on every deck it stops at (Karl, 2026-11-24).
        foreach (var k in d.Risers)
        {
            var (lo, hi) = d.Decks[k.From].Floor < d.Decks[k.To].Floor ? (d.Decks[k.From], d.Decks[k.To]) : (d.Decks[k.To], d.Decks[k.From]);
            Room? last = null;
            var stops = new List<string>();
            foreach (var dk in d.Decks.Where(q => q.Floor >= lo.Floor && q.Floor <= hi.Floor).OrderBy(q => q.Floor))
            {
                var di = d.Decks.IndexOf(dk);
                var (hx, hz) = k.Half;
                var r = d.Rooms.FirstOrDefault(q => q.Deck == di && k.X - hx >= q.X0 - 0.01 && k.X + hx <= q.X1 + 0.01 && k.Z - hz >= q.Z0 - 0.01 && k.Z + hz <= q.Z1 + 0.01);
                if (r is null)
                {
                    stops.Add($"{di + 1}");
                    continue;
                }
                if (last is not null)
                {
                    joins[last.Id].Add(r.Id);
                    joins[r.Id].Add(last.Id);
                }
                last = r;
            }
            c.RiserOk[k.Id] = stops.Count == 0;
            if (stops.Count > 0)
                c.Issues.Add(new("warn", $"{k.Kind} has no room round it on deck {string.Join(", ", stops)} (it needs a room its whole {2 * k.Half.Hx:0.0} x {2 * k.Half.Hz:0.0} m fits in)", $"riser:{k.Id}"));
        }

        // Pieces: on their hull, not where the skin is cut away; sockets reach a room of the kind they need.
        var lowest = d.Decks.Count == 0 ? -1 : d.Decks.IndexOf(d.Decks.MinBy(k => k.Floor)!);
        var entries = new List<int>();
        var anySocket = false;
        foreach (var p in d.Pieces)
        {
            var h = d.Hull(p.Hull)!;
            var kind = PieceKinds.Find(p.Kind)!;
            var ok = true;
            if (p.Surface != "skin")
            {
                var st = p.Surface == "aft" ? h.Stations[^1] : h.Stations[0];
                var half = Half(st, p.V);
                if (half < 0 || Math.Abs(p.U) > half)
                {
                    ok = false;
                    c.Issues.Add(new("error", $"{p.Kind} is off the {(p.Surface == "aft" ? "stern" : "bow")}'s end", $"piece:{p.Id}"));
                }
            }
            foreach (var (at, n) in Spots(d, p))
            {
                if (d.Hulls.Any(o => o != h && (o.Cut || h.Cut) && o.Depth(at[0], at[1], at[2]) > 0.05))
                {
                    ok = false;
                    c.Issues.Add(new("warn", $"{p.Kind} sits where the skin is cut away (inside {OtherName(d, h, at)})", $"piece:{p.Id}"));
                }
                if (kind.Socket is null && !kind.Service)
                    continue;
                if (kind.Socket is not null)
                    anySocket = true;
                Room? best = null;
                var bestD = double.MaxValue;
                double[] bestPt = at;
                foreach (var r in d.Rooms)
                {
                    if (p.Room != 0 && r.Id != p.Room)
                        continue; // the room the player picked for it
                    if (kind.Socket == "cargo" && r.Kind != "Hold")
                        continue;
                    var dk = d.DeckOf(r);
                    if (kind.Socket == "shaft" && !d.Stacked && (r.Deck != lowest || at[1] > dk.Floor || at[0] < r.X0 - 0.3 || at[0] > r.X1 + 0.3 || at[2] < r.Z0 - 0.3 || at[2] > r.Z1 + 0.3))
                        continue;
                    var bx = Outer(d, r);
                    double[] q = [Math.Clamp(at[0], bx[0], bx[1]), Math.Clamp(at[1], bx[2], bx[3]), Math.Clamp(at[2], bx[4], bx[5])];
                    var dist = Math.Sqrt(Math.Pow(q[0] - at[0], 2) + Math.Pow(q[1] - at[1], 2) + Math.Pow(q[2] - at[2], 2));
                    if (dist < bestD)
                        (best, bestD, bestPt) = (r, dist, q);
                }
                // A socket linked to a room the player picked may be farther than its reach: a lined connector runs to it, like the
                // Limpet's shaft from the Zero Day's keel to its lower deck (Karl, 2026-11-24), up to MaxConnector long.
                // A drive's service socket goes on the inside of the wall the drive is on: straight in from it along the skin's
                // normal to the first room wall (Karl, 2026-11-24), not on whichever room wall is nearest.
                if (kind.Service)
                    for (var t = 0.1; t <= kind.Reach; t += 0.05)
                    {
                        double[] q = [at[0] - n[0] * t, at[1] - n[1] * t, at[2] - n[2] * t];
                        var hit = d.Rooms.FirstOrDefault(r => (p.Room == 0 || r.Id == p.Room) && Outer(d, r) is var bx && q[0] >= bx[0] && q[0] <= bx[1] && q[1] >= bx[2] && q[1] <= bx[3] && q[2] >= bx[4] && q[2] <= bx[5]);
                        if (hit is not null)
                        {
                            (best, bestD, bestPt) = (hit, t, q);
                            break;
                        }
                    }
                var shaft = best is not null && p.Room != 0 && bestD > kind.Reach && bestD <= MaxConnector ? (kind.Opening is { } op ? Math.Max(0.8, 2 * Math.Min(op.Hx, op.Hz)) : 1.1) * p.Size : 0;
                var linked = best is not null && (bestD <= kind.Reach || shaft > 0);
                if (linked)
                {
                    if (kind.Socket is not null && kind.Socket != "window")
                        entries.Add(best!.Id); // a window looks out; it isn't a way aboard
                }
                else if (kind.Service)
                {
                    // A drive still works without one; it only has no way in from inside to feed and mend it.
                    c.Issues.Add(new("warn", best is null ? $"{p.Kind} has no room behind it for its service socket" :
                        $"{p.Kind}'s service socket is {bestD:0.0} m from {Name(best)} (at most {kind.Reach:0.0} m, or link it to the room): it has no way in from inside", $"piece:{p.Id}"));
                }
                else
                {
                    ok = false;
                    c.Issues.Add(new("error", best is null ? (p.Room != 0 ? $"{p.Kind}'s room is gone or isn't {kind.Needs}: pick another" : $"{p.Kind} needs {kind.Needs} behind it") :
                        $"{p.Kind} is {bestD:0.0} m from {Name(best)} (at most {kind.Reach:0.0} m, or {MaxConnector:0} m through a connector when you link it to the room): move it or the room closer", $"piece:{p.Id}"));
                }
                c.Links.Add((at, bestPt, linked, shaft, kind.Service, n));
            }
            c.PieceOk[p.Id] = ok;
        }

        // Every room reachable from a way aboard.
        var queue = new Queue<int>(entries.Distinct());
        foreach (var e in queue)
            c.Reached.Add(e);
        while (queue.Count > 0)
            foreach (var nb in joins[queue.Dequeue()])
                if (c.Reached.Add(nb))
                    queue.Enqueue(nb);
        if (d.Rooms.Count > 0 && entries.Count == 0)
            c.Issues.Add(new("error", anySocket ? "No airlock, dock or boarding hatch opens into a room: nobody can get aboard" : "No way aboard: add an airlock, a docking collar or a boarding hatch", ""));
        else
            foreach (var r in d.Rooms)
                if (!c.Reached.Contains(r.Id))
                    c.Issues.Add(new("warn", $"{Name(r)} on deck {r.Deck + 1} has no door to the rest (rooms need 0.9 m of shared wall; a Shaft joins decks)", $"room:{r.Id}"));
        if (!d.Rooms.Any(r => r.Kind == "Bridge"))
            c.Issues.Add(new("warn", "No bridge", ""));
        return c;
    }

    // ---- rooms out to the hull

    /// <summary>Which sides of a room go out to the skin: those with no room of its deck beyond them.</summary>
    public static (bool L, bool R) Expands(ShipDesign d, Room r)
    {
        if (!r.ToHull)
            return (false, false);
        bool Beyond(Func<Room, bool> side) => d.Rooms.Any(o => o != r && o.Deck == r.Deck && Math.Min(r.Z1, o.Z1) - Math.Max(r.Z0, o.Z0) > 0.01 && side(o));
        return (!Beyond(o => o.X1 <= r.X0 + 0.01), !Beyond(o => o.X0 >= r.X1 - 0.01));
    }

    /// <summary>A room's two side edges (its X) at a place v on the floor and a height up: its box's, or out to the skin (6 cm in) on a side that expands.</summary>
    public static (double L, double R) Edges(ShipDesign d, Room r, double v, double up)
    {
        var (el, er) = Expands(d, r);
        if (!el && !er)
            return (r.X0, r.X1);
        var w = d.World(d.Decks[r.Deck], 0, v, up);
        double lo = r.X0, hi = r.X1;
        foreach (var h in d.Hulls)
            for (var c = 0; c < h.Copies; c++)
            {
                var half = h.SkinHalf(w[2] - h.Z, w[1] - h.Y);
                if (half < 0.1)
                    continue;
                var cx = h.CopyX(c);
                if (cx + half < r.X0 || cx - half > r.X1)
                    continue; // a body the room isn't in
                lo = Math.Min(lo, cx - half + 0.07);
                hi = Math.Max(hi, cx + half - 0.07);
            }
        return (el ? Math.Min(r.X0, lo) : r.X0, er ? Math.Max(r.X1, hi) : r.X1);
    }

    /// <summary>A room's world box, out to the skin where it expands.</summary>
    public static double[] Outer(ShipDesign d, Room r)
    {
        var b = d.Box(r);
        if (!r.ToHull)
            return b;
        double lo = r.X0, hi = r.X1;
        var dk = d.DeckOf(r);
        for (var v = r.Z0; v <= r.Z1 + 1e-6; v += Math.Max(0.25, (r.Z1 - r.Z0) / 8))
            foreach (var up in new[] { 0.05, dk.Height / 2, dk.Height - 0.02 })
            {
                var (l, rr) = Edges(d, r, v, up);
                (lo, hi) = (Math.Min(lo, l), Math.Max(hi, rr));
            }
        b[0] = lo; b[1] = hi;
        return b;
    }

    /// <summary>A room's floor area (out to the skin where it expands).</summary>
    public static double Area(ShipDesign d, Room r)
    {
        if (!r.ToHull)
            return r.Area;
        var n = Math.Max(2, (int)Math.Ceiling((r.Z1 - r.Z0) / 0.25));
        var a = 0.0;
        for (var i = 0; i < n; i++)
        {
            var (l, rr) = Edges(d, r, r.Z0 + (r.Z1 - r.Z0) * (i + 0.5) / n, 0.05);
            a += (rr - l) * (r.Z1 - r.Z0) / n;
        }
        return a;
    }

    /// <summary>A room that follows the hull, as triangles: its floor, ceiling, sides and ends, sampled every 50 cm along and four heights up.</summary>
    public static DesignMesh RoomMesh(ShipDesign d, Room r)
    {
        var m = new DesignMesh();
        var dk = d.DeckOf(r);
        var col = DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour);
        var nv = Math.Max(1, (int)Math.Ceiling((r.Z1 - r.Z0) / 0.5));
        const int nu = 4;
        (double, double, double) P(double u, double v, double up) { var w = d.World(dk, u, v, up); return (w[0], w[1], w[2]); }
        double V(int i) => r.Z0 + (r.Z1 - r.Z0) * i / nv;
        double Up(int k) => dk.Height * k / nu;
        for (var i = 0; i < nv; i++)
        {
            for (var k = 0; k < nu; k++)
            {
                var (a0, b0) = Edges(d, r, V(i), Up(k));
                var (a1, b1) = Edges(d, r, V(i + 1), Up(k));
                var (a2, b2) = Edges(d, r, V(i), Up(k + 1));
                var (a3, b3) = Edges(d, r, V(i + 1), Up(k + 1));
                m.Quad(P(a0, V(i), Up(k)), P(a1, V(i + 1), Up(k)), P(a3, V(i + 1), Up(k + 1)), P(a2, V(i), Up(k + 1)), DesignMesh.Tone(col, 0.8), false);
                m.Quad(P(b0, V(i), Up(k)), P(b1, V(i + 1), Up(k)), P(b3, V(i + 1), Up(k + 1)), P(b2, V(i), Up(k + 1)), DesignMesh.Tone(col, 0.8), false);
            }
            var (f0, g0) = Edges(d, r, V(i), 0);
            var (f1, g1) = Edges(d, r, V(i + 1), 0);
            m.Quad(P(f0, V(i), 0.02), P(g0, V(i), 0.02), P(g1, V(i + 1), 0.02), P(f1, V(i + 1), 0.02), col, false);
            var (c0, e0) = Edges(d, r, V(i), dk.Height);
            var (c1, e1) = Edges(d, r, V(i + 1), dk.Height);
            m.Quad(P(c0, V(i), dk.Height), P(e0, V(i), dk.Height), P(e1, V(i + 1), dk.Height), P(c1, V(i + 1), dk.Height), DesignMesh.Tone(col, 0.9), false);
        }
        foreach (var v in new[] { r.Z0, r.Z1 })
            for (var k = 0; k < nu; k++)
            {
                var (a0, b0) = Edges(d, r, v, Up(k));
                var (a1, b1) = Edges(d, r, v, Up(k + 1));
                m.Quad(P(a0, v, Up(k)), P(b0, v, Up(k)), P(b1, v, Up(k + 1)), P(a1, v, Up(k + 1)), DesignMesh.Tone(col, 0.7), false);
            }
        return m;
    }

    private static double Half(Station s, double y)
    {
        if (y < s.P[0].Y || y > s.P[^1].Y)
            return -1;
        for (var k = 0; k < s.P.Count - 1; k++)
            if (y <= s.P[k + 1].Y)
                return s.P[k].H + (s.P[k + 1].H - s.P[k].H) * (y - s.P[k].Y) / (s.P[k + 1].Y - s.P[k].Y);
        return s.P[^1].H;
    }

    private static string OtherName(ShipDesign d, HullBody h, double[] at) =>
        d.Hulls.FirstOrDefault(o => o != h && o.Depth(at[0], at[1], at[2]) > 0.05)?.Name ?? "another body";

    public static string Name(Room r) => r.Label.Length > 0 ? r.Label : r.Kind;

    // ---- the page's JSON

    // Each hull copy's mesh as last written, under the key of all it was made from: an edit that leaves a body, the bodies that cut it and
    // its openings alone (a room, a deck, another body far off) sends the same text without lofting and cutting it again.
    private static readonly Dictionary<(int, int), (string Key, string Json)> Meshes = [];

    private static string MeshKey(ShipDesign d, HullBody h)
    {
        var k = new StringBuilder(h.ToJson().ToJsonString()).Append(Holes ? 'H' : 'h');
        var box = h.Bounds();
        foreach (var o in d.Hulls)
            if (o != h && Overlap(o.Bounds(), box))
                k.Append('|').Append(o.ToJson().ToJsonString());
        if (Holes)
            foreach (var p in d.Pieces)
                if (p.Hull == h.Id && PieceKinds.Find(p.Kind)?.Opening is not null)
                    k.Append('#').Append(p.ToJson().ToJsonString());
        foreach (var j in d.Joins)
            if (j.A.Hull == h.Id || j.B.Hull == h.Id)
                k.Append('&').Append(j.ToJson(d).ToJsonString()).Append(d.Hull(j.A.Hull == h.Id ? j.B.Hull : j.A.Hull)?.ToJson().ToJsonString());
        return k.ToString();
    }

    public static string Json(ShipDesign d)
    {
        if (Meshes.Count > 4 * ShipDesign.MaxHulls)
            Meshes.Clear();
        var c = Run(d);
        var sb = new StringBuilder(1 << 16);
        sb.Append("{\"name\":").Append(Q(d.Name)).Append(",\"stacked\":").Append(d.Stacked ? "true" : "false").Append(",\"hulls\":[");
        for (var i = 0; i < d.Hulls.Count; i++)
        {
            var h = d.Hulls[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(h.Id).Append(",\"name\":").Append(Q(h.Name))
              .Append(",\"x\":").Append(ShipDesign.S(h.X)).Append(",\"y\":").Append(ShipDesign.S(h.Y)).Append(",\"z\":").Append(ShipDesign.S(h.Z))
              .Append(",\"mirror\":").Append(h.Mirror ? "true" : "false").Append(",\"cut\":").Append(h.Cut ? "true" : "false").Append(",\"skin\":").Append(Q(h.Skin))
              .Append(",\"stations\":[");
            for (var s = 0; s < h.Stations.Count; s++)
            {
                if (s > 0) sb.Append(',');
                sb.Append("{\"z\":").Append(ShipDesign.S(h.Stations[s].Z)).Append(",\"p\":[");
                for (var k = 0; k < h.Points; k++)
                    sb.Append(k > 0 ? "," : "").Append('[').Append(ShipDesign.S(h.Stations[s].P[k].H)).Append(',').Append(ShipDesign.S(h.Stations[s].P[k].Y)).Append(']');
                sb.Append("]}");
            }
            sb.Append("],\"copies\":").Append(Light ? "null" : "[");
            var key = Light ? "" : MeshKey(d, h);
            for (var copy = 0; copy < (Light ? 0 : h.Copies); copy++)
            {
                if (copy > 0) sb.Append(',');
                if (!Meshes.TryGetValue((h.Id, copy), out var hit) || hit.Key != key)
                {
                    var (skin, fore, aft) = HullMesh(d, h, copy);
                    var one = new StringBuilder(1 << 14);
                    one.Append("{\"copy\":").Append(copy).Append(",\"skin\":");
                    skin.Write(one);
                    one.Append(",\"fore\":");
                    fore.Write(one);
                    one.Append(",\"aft\":");
                    aft.Write(one);
                    one.Append('}');
                    Meshes[(h.Id, copy)] = hit = (key, one.ToString());
                }
                sb.Append(hit.Json);
            }
            sb.Append(Light ? "}" : "]}");
        }
        sb.Append("],\"joins\":[");
        for (var i = 0; i < d.Joins.Count; i++)
        {
            var j = d.Joins[i];
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(j.Id).Append(",\"section\":").Append(Q(j.Section)).Append(",\"w\":").Append(ShipDesign.S(j.W))
              .Append(",\"h\":").Append(ShipDesign.S(j.H)).Append(",\"segments\":").Append(j.Segments).Append(",\"mirror\":").Append(j.Mirror ? "true" : "false")
              .Append(",\"squareHole\":").Append(j.SquareHole ? "true" : "false").Append(",\"flare\":").Append(ShipDesign.S(j.Flare)).Append(",\"buttress\":").Append(ShipDesign.S(j.Buttress)).Append(",\"openA\":").Append(ShipDesign.S(j.OpenA)).Append(",\"openB\":").Append(ShipDesign.S(j.OpenB))
              .Append(",\"mesh\":");
            j.Mesh(d).Write(sb);
            sb.Append('}');
        }
        sb.Append("],\"pieces\":[");
        for (var i = 0; i < d.Pieces.Count; i++)
        {
            var p = d.Pieces[i];
            var h = d.Hull(p.Hull)!;
            var kind = PieceKinds.Find(p.Kind)!;
            var m = new DesignMesh();
            var w = Where(h, p);
            m.Frame(w.O[0] - w.Y[0] * p.Recess, w.O[1] - w.Y[1] * p.Recess, w.O[2] - w.Y[2] * p.Recess, w.X, w.Y, w.Z, p.Size);
            kind.Draw(m);
            if (p.Clad > 0 && kind.Opening is { } op)
            {
                // Cladding: a plate band round the opening's frame, flush on the skin (its width in metres, whatever the size).
                double cl = p.Clad / p.Size, hx = op.Hx + 0.15, hz = op.Hz + 0.15;
                m.Box(-hx - cl, 0.005, -hz - cl, hx + cl, 0.07, -hz, PieceKinds.Armour);
                m.Box(-hx - cl, 0.005, hz, hx + cl, 0.07, hz + cl, PieceKinds.Armour);
                m.Box(-hx - cl, 0.005, -hz, -hx, 0.07, hz, PieceKinds.Armour);
                m.Box(hx, 0.005, -hz, hx + cl, 0.07, hz, PieceKinds.Armour);
            }
            if (p.Mirror)
                m.MirrorFrom(0, h.CopyX(Math.Min(p.Copy, h.Copies - 1)));
            if (p.Pair && h.Copies == 2)
                m.MirrorFrom(0, 0);
            if (i > 0) sb.Append(',');
            sb.Append("{\"id\":").Append(p.Id).Append(",\"kind\":").Append(Q(p.Kind)).Append(",\"hull\":").Append(p.Hull).Append(",\"surface\":").Append(Q(p.Surface))
              .Append(",\"mirror\":").Append(p.Mirror ? "true" : "false").Append(",\"pair\":").Append(p.Pair ? "true" : "false").Append(",\"size\":").Append(ShipDesign.S(p.Size)).Append(",\"recess\":").Append(ShipDesign.S(p.Recess)).Append(",\"clad\":").Append(ShipDesign.S(p.Clad))
              .Append(",\"onPair\":").Append(h.Copies == 2 ? "true" : "false").Append(",\"turn\":").Append(ShipDesign.S(p.Turn))
              .Append(",\"room\":").Append(p.Room).Append(",\"socket\":").Append(kind.Socket is null ? "null" : Q(kind.Socket)).Append(",\"ok\":").Append(c.PieceOk.GetValueOrDefault(p.Id, true) ? "true" : "false")
              .Append(",\"at\":[").Append(ShipDesign.S(w.O[0])).Append(',').Append(ShipDesign.S(w.O[1])).Append(',').Append(ShipDesign.S(w.O[2])).Append("],\"mesh\":");
            m.Write(sb);
            sb.Append('}');
        }
        sb.Append("],\"decks\":[");
        for (var i = 0; i < d.Decks.Count; i++)
            sb.Append(i > 0 ? "," : "").Append("{\"floor\":").Append(ShipDesign.S(d.Decks[i].Floor)).Append(",\"height\":").Append(ShipDesign.S(d.Decks[i].Height)).Append('}');
        sb.Append("],\"rooms\":[");
        for (var i = 0; i < d.Rooms.Count; i++)
        {
            var r = d.Rooms[i];
            sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(r.Id).Append(",\"deck\":").Append(r.Deck)
              .Append(",\"x0\":").Append(ShipDesign.S(r.X0)).Append(",\"x1\":").Append(ShipDesign.S(r.X1)).Append(",\"z0\":").Append(ShipDesign.S(r.Z0)).Append(",\"z1\":").Append(ShipDesign.S(r.Z1))
              .Append(",\"kind\":").Append(Q(r.Kind)).Append(",\"label\":").Append(Q(r.Label)).Append(",\"colour\":").Append(Q(RoomKinds.Find(r.Kind)!.Colour))
              .Append(",\"height\":").Append(ShipDesign.S(d.DeckOf(r).Height)).Append(",\"group\":").Append(r.Group).Append(",\"box\":[").Append(string.Join(',', d.Box(r).Select(ShipDesign.S))).Append(']')
              .Append(",\"exts\":[").Append(string.Join(',', r.Exts.Select(e =>
              {
                  var (ra, rb) = ExtRings(d, r, e);
                  return $"{{\"side\":{Q(e.Side)},\"shape\":{Q(e.Shape)},\"depth\":{ShipDesign.S(e.Depth)},\"width\":{ShipDesign.S(e.Width)},\"t\":{ShipDesign.S(e.T)},\"a\":[{string.Join(',', ra.SelectMany(p => p).Select(ShipDesign.S))}],\"b\":[{string.Join(',', rb.SelectMany(p => p).Select(ShipDesign.S))}]}}";
              }))).Append(']')
              .Append(",\"toHull\":").Append(r.ToHull ? "true" : "false").Append(",\"area\":").Append(ShipDesign.S(Area(d, r)));
            if (r.ToHull)
            {
                sb.Append(",\"mesh\":");
                RoomMesh(d, r).Write(sb);
            }
            sb
              .Append(",\"ok\":").Append(c.RoomOk.GetValueOrDefault(r.Id, true) ? "true" : "false").Append(",\"reached\":").Append(c.Reached.Contains(r.Id) ? "true" : "false").Append('}');
        }
        sb.Append("],\"doors\":[");
        for (var i = 0; i < c.Doors.Count; i++)
            sb.Append(i > 0 ? "," : "").Append('[').Append(ShipDesign.S(c.Doors[i][0])).Append(',').Append(ShipDesign.S(c.Doors[i][1])).Append(',').Append(ShipDesign.S(c.Doors[i][2])).Append(']');
        sb.Append("],\"placedDoors\":[");
        for (var i = 0; i < c.Placed.Count; i++)
        {
            var (id, at, s0, s1) = c.Placed[i];
            var k = d.Doors.Find(q => q.Id == id)!;
            sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(id).Append(",\"a\":").Append(k.A).Append(",\"b\":").Append(k.B).Append(",\"t\":").Append(ShipDesign.S(k.T))
              .Append(",\"kind\":").Append(Q(k.Kind)).Append(",\"shape\":").Append(Q(k.Shape)).Append(",\"w\":").Append(ShipDesign.S(k.W)).Append(",\"h\":").Append(ShipDesign.S(k.H)).Append(",\"sill\":").Append(ShipDesign.S(k.Sill))
              .Append(",\"at\":[").Append(string.Join(',', at.Select(ShipDesign.S))).Append("],\"s0\":[").Append(string.Join(',', s0.Select(ShipDesign.S)))
              .Append("],\"s1\":[").Append(string.Join(',', s1.Select(ShipDesign.S))).Append("]}");
        }
        sb.Append("],\"hullWindows\":[");
        for (var i = 0; i < d.Windows.Count; i++)
        {
            var w = d.Windows[i];
            var (pts, nu, nv, gap, ok, len) = Lay(d, w);
            sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(w.Id).Append(",\"room\":").Append(w.Room).Append(",\"side\":").Append(Q(w.Side))
              .Append(",\"t\":").Append(ShipDesign.S(w.T)).Append(",\"w\":").Append(ShipDesign.S(w.W)).Append(",\"h\":").Append(ShipDesign.S(w.H)).Append(",\"sill\":").Append(ShipDesign.S(w.Sill))
              .Append(",\"ok\":").Append(ok ? "true" : "false").Append(",\"gap\":").Append(ShipDesign.S(gap)).Append(",\"len\":").Append(ShipDesign.S(len))
              .Append(",\"nu\":").Append(nu).Append(",\"nv\":").Append(nv).Append(",\"pts\":[").Append(string.Join(',', pts.Select(ShipDesign.S))).Append("]");
            var (fp, fnu, fnv, _, _, _) = Lay(d, w, 0.1, 0.045);
            sb.Append(",\"fnu\":").Append(fnu).Append(",\"fnv\":").Append(fnv).Append(",\"fpts\":[").Append(string.Join(',', fp.Select(ShipDesign.S))).Append("]}");
        }
        sb.Append("],\"risers\":[");
        for (var i = 0; i < d.Risers.Count; i++)
        {
            var k = d.Risers[i];
            var (lo, hi) = d.Decks[k.From].Floor < d.Decks[k.To].Floor ? (d.Decks[k.From], d.Decks[k.To]) : (d.Decks[k.To], d.Decks[k.From]);
            var a = d.World(lo, k.X, k.Z, 0);
            var b = d.World(hi, k.X, k.Z, hi.Height);
            sb.Append(i > 0 ? "," : "").Append("{\"id\":").Append(k.Id).Append(",\"kind\":").Append(Q(k.Kind)).Append(",\"x\":").Append(ShipDesign.S(k.X)).Append(",\"z\":").Append(ShipDesign.S(k.Z))
              .Append(",\"from\":").Append(k.From).Append(",\"to\":").Append(k.To).Append(",\"w\":").Append(k.W).Append(",\"l\":").Append(k.L).Append(",\"hx\":").Append(ShipDesign.S(k.Half.Hx)).Append(",\"hz\":").Append(ShipDesign.S(k.Half.Hz))
              .Append(",\"ok\":").Append(c.RiserOk.GetValueOrDefault(k.Id) ? "true" : "false")
              .Append(",\"stops\":[").Append(string.Join(',', d.Decks.Where(q => q.Floor >= lo.Floor && q.Floor <= hi.Floor).Select(q => ShipDesign.S(q.Floor)))).Append(']')
              .Append(",\"a\":[").Append(string.Join(',', a.Select(ShipDesign.S))).Append("],\"b\":[").Append(string.Join(',', b.Select(ShipDesign.S))).Append("]}");
        }
        sb.Append("],\"links\":[");
        for (var i = 0; i < c.Links.Count; i++)
        {
            var (a, b, ok, sh, sv, nn) = c.Links[i];
            sb.Append(i > 0 ? "," : "").Append("{\"a\":[").Append(string.Join(',', a.Select(ShipDesign.S))).Append("],\"b\":[").Append(string.Join(',', b.Select(ShipDesign.S)))
              .Append("],\"ok\":").Append(ok ? "true" : "false").Append(",\"shaft\":").Append(ShipDesign.S(sh)).Append(",\"service\":").Append(sv ? "true" : "false").Append(",\"n\":[").Append(string.Join(',', nn.Select(ShipDesign.S))).Append("]}");
        }
        sb.Append("],\"issues\":[");
        for (var i = 0; i < c.Issues.Count; i++)
            sb.Append(i > 0 ? "," : "").Append("{\"level\":").Append(Q(c.Issues[i].Level)).Append(",\"text\":").Append(Q(c.Issues[i].Text)).Append(",\"what\":").Append(Q(c.Issues[i].What)).Append('}');
        sb.Append("],\"stats\":");
        Stats(d, sb);
        return sb.Append('}').ToString();
    }

    private static void Stats(ShipDesign d, StringBuilder sb)
    {
        double x0 = double.MaxValue, x1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
        foreach (var h in d.Hulls)
        {
            var b = h.Bounds();
            (x0, x1, y0, y1, z0, z1) = (Math.Min(x0, b.X0), Math.Max(x1, b.X1), Math.Min(y0, b.Y0), Math.Max(y1, b.Y1), Math.Min(z0, b.Z0), Math.Max(z1, b.Z1));
        }
        var floor = d.Rooms.Sum(r => Area(d, r));
        var hold = d.Rooms.Where(r => r.Kind == "Hold").Sum(r => Area(d, r) * d.DeckOf(r).Height);
        var bunks = d.Rooms.Where(r => r.Kind == "Cabin").Sum(r => Math.Floor(Area(d, r) / 6));
        sb.Append("{\"length\":").Append(ShipDesign.S(z1 - z0)).Append(",\"beam\":").Append(ShipDesign.S(x1 - x0)).Append(",\"height\":").Append(ShipDesign.S(y1 - y0))
          .Append(",\"floor\":").Append(ShipDesign.S(floor)).Append(",\"hold\":").Append(ShipDesign.S(hold)).Append(",\"bunks\":").Append(bunks)
          .Append(",\"rooms\":").Append(d.Rooms.Count).Append(",\"pieces\":").Append(d.Pieces.Sum(p => (p.Mirror ? 2 : 1) * (p.Pair && d.Hull(p.Hull)?.Copies == 2 ? 2 : 1))).Append(",\"hulls\":").Append(d.Hulls.Count).Append('}');
    }

    public static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in s)
            sb.Append(ch switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "", '\t' => " ", _ => ch < ' ' ? "" : ch.ToString() });
        return sb.Append('"').ToString();
    }
}
