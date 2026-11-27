namespace CallOfSlop.ShipDesigner;

public static partial class DesignScene
{
    /// <summary>
    /// Beading (Karl, 2026-11-27): a raised strip along the hull's outside, bow to stern, on each point of its profile (Creases: only where
    /// the profile turns past <see cref="CreaseAngle"/>, the chines the cladding seams on; All: every point). Each strip is a bevelled bar
    /// BeadSize wide standing out along the point's mitred normal, over the cladding when the body is clad, sunk 1 cm into the skin, cut in
    /// half-metre runs so it stops short of every opening (<paramref name="open"/>) and caps its ends.
    /// </summary>
    private static void Bead(DesignMesh m, HullBody h, double cx, Func<double, double, double, bool> open, string cladKind, double cladDepth)
    {
        if (h.Bead == "Off")
            return;
        var all = h.Bead == "All";
        var w = Math.Clamp(h.BeadSize, HullBody.BeadSizeMin, HullBody.BeadSizeMax);
        var rise = w * 0.8 + (cladKind == "Off" ? 0 : HullBody.CladRise(cladKind) * cladDepth);
        var col = h.BeadColour.Length == 7 ? DesignMesh.Hex(h.BeadColour) : DesignMesh.Tone(DesignMesh.Hex(h.Skin), 0.62);
        var lit = DesignMesh.Tone(col, 1.1);
        var dark = DesignMesh.Tone(col, 0.8);
        var ah = new double[HullBody.MaxLoft];
        var ay = new double[HullBody.MaxLoft];
        var bh = new double[HullBody.MaxLoft];
        var by = new double[HullBody.MaxLoft];
        var rings = h.Ring;
        for (var i = 0; i < rings.Count - 1; i++)
        {
            var n = h.Pair(i, ah, ay, bh, by);
            if (n < 2)
                continue;
            double za = rings[i].Z, zb = rings[i + 1].Z, len = zb - za;
            if (len < 0.05)
                continue;
            for (var k = 0; k < n; k++)
            {
                // A point on the middle line is beaded once (its +x copy).
                var onMid = ah[k] < 0.02 && bh[k] < 0.02;
                if (!all && Math.Max(BeadTurn(ah, ay, n, k), BeadTurn(bh, by, n, k)) <= CreaseAngle)
                    continue;
                var (na, ta) = BeadFrame(ah, ay, n, k);
                var (nb, tb) = BeadFrame(bh, by, n, k);
                foreach (var s in onMid ? new[] { 1 } : new[] { 1, -1 })
                {
                    // The bar's section at a share f along the band: base corners 1 cm in, top corners drawn in to 30% each side, rise out.
                    (double, double, double)[] Sec(double f)
                    {
                        double px = ah[k] + (bh[k] - ah[k]) * f, py = ay[k] + (by[k] - ay[k]) * f, z = za + len * f;
                        double nx = na.X + (nb.X - na.X) * f, ny = na.Y + (nb.Y - na.Y) * f, tx = ta.X + (tb.X - ta.X) * f, ty = ta.Y + (tb.Y - ta.Y) * f;
                        (double, double, double) P(double along, double outw) => (cx + s * (px + tx * along + nx * outw), h.Y + py + ty * along + ny * outw, h.Z + z);
                        return [P(-w / 2, -0.01), P(-w * 0.3, rise), P(w * 0.3, rise), P(w / 2, -0.01)];
                    }
                    var pieces = Math.Max(1, (int)Math.Ceiling(len / 0.5));
                    var run = false;
                    (double, double, double)[]? prev = null;
                    for (var p = 0; p < pieces; p++)
                    {
                        double f0 = (double)p / pieces, f1 = (double)(p + 1) / pieces, fm = (f0 + f1) / 2;
                        var mid = (cx + s * (ah[k] + (bh[k] - ah[k]) * fm), h.Y + ay[k] + (by[k] - ay[k]) * fm, h.Z + za + len * fm);
                        var a = prev ?? Sec(f0);
                        var b = Sec(f1);
                        prev = b;
                        if (open(mid.Item1, mid.Item2, mid.Item3))
                        {
                            if (run)
                                BeadCap(m, a, dark);
                            run = false;
                            continue;
                        }
                        if (!run)
                            BeadCap(m, a, dark);
                        run = true;
                        m.Quad(a[0], b[0], b[1], a[1], dark, false);
                        m.Quad(a[1], b[1], b[2], a[2], lit, false);
                        m.Quad(a[2], b[2], b[3], a[3], dark, false);
                    }
                    if (run)
                        BeadCap(m, prev!, dark);
                }
            }
        }
    }

    private static void BeadCap(DesignMesh m, (double, double, double)[] q, (byte, byte, byte) col) => m.Quad(q[0], q[1], q[2], q[3], col, false);

    /// <summary>The profile's turn at point k in degrees, its ends against their mirror across the middle line (the keel and the spine).</summary>
    private static double BeadTurn(double[] hs, double[] ys, int n, int k)
    {
        var (p, q, r) = BeadNeighbours(hs, ys, n, k);
        double ax = q.X - p.X, ay = q.Y - p.Y, bx = r.X - q.X, by = r.Y - q.Y;
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la < 1e-6 || lb < 1e-6)
            return 0;
        return Math.Acos(Math.Clamp((ax * bx + ay * by) / (la * lb), -1, 1)) * 180 / Math.PI;
    }

    private static ((double X, double Y) P, (double X, double Y) Q, (double X, double Y) R) BeadNeighbours(double[] hs, double[] ys, int n, int k)
    {
        (double, double) Q(int j) => (hs[j], ys[j]);
        var prev = k > 0 ? Q(k - 1) : (-hs[0], ys[0]);
        var next = k < n - 1 ? Q(k + 1) : (-hs[n - 1], ys[n - 1]);
        return (prev, Q(k), next);
    }

    /// <summary>The outward mitred normal at point k of a +x half profile (keel first) and the tangent across it, both unit, in (h, y).</summary>
    private static ((double X, double Y) N, (double X, double Y) T) BeadFrame(double[] hs, double[] ys, int n, int k)
    {
        var (p, q, r) = BeadNeighbours(hs, ys, n, k);
        (double X, double Y) Out((double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-9 ? (0, 0) : (dy / l, -dx / l); // (an edge running up the +x side faces +x)
        }
        var o1 = Out(p, q);
        var o2 = Out(q, r);
        double nx = o1.X + o2.X, ny = o1.Y + o2.Y, nl = Math.Sqrt(nx * nx + ny * ny);
        if (nl < 1e-9)
            (nx, ny, nl) = (1, 0, 1);
        nx /= nl;
        ny /= nl;
        return ((nx, ny), (-ny, nx));
    }
}
