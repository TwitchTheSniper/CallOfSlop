namespace CallOfSlop.ShipDesigner;

public static partial class DesignScene
{
    /// <summary>The most triangles one copy of a body's cladding may add; past it the rest of the body is left bare (logged).</summary>
    public const int MaxCladTris = 60000;

    /// <summary>The last body whose cladding hit the cap (its name), for the page's log; empty when none did.</summary>
    public static string CladCapped = "";

    private const double CladSeam = 0.02, CladSink = 0.03, CladMin = 0.01;

    /// <summary>
    /// Plated cladding on one copy of a body's sides (the user's Star Destroyer flank, 2026-11-24): each band between two stations is
    /// a grid of cells along the arc and along z, merged at random into plates of one to four cells each way, each plate inset by a seam,
    /// raised by a varied height along the skin's normal (at least 1 cm, so 4 mm and more off the skin and staggered from the plate
    /// before), its skirt sunk 3 cm into the hull. Heavy plates rise higher and some carry a small box. Seeded, so the same design
    /// always clads the same; cells <paramref name="blocked"/> says an opening may reach (by two opposite world corners) stay bare.
    /// </summary>
    private static void CladBody(DesignMesh m, HullBody h, double cx, Func<(double, double, double), (double, double, double), bool> blocked)
    {
        var heavy = h.Clad == "Heavy";
        var start = m.Tris;
        var col = DesignMesh.Hex(h.Skin);
        var ah = new double[HullBody.MaxLoft];
        var ay = new double[HullBody.MaxLoft];
        var bh = new double[HullBody.MaxLoft];
        var by = new double[HullBody.MaxLoft];
        var ca = new double[HullBody.MaxLoft];
        var cb = new double[HullBody.MaxLoft];
        var size = Math.Clamp(h.CladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        var rise = (heavy ? 0.20 : 0.08) - CladMin;
        for (var i = 0; i < h.Stations.Count - 1; i++)
        {
            var n = h.Pair(i, ah, ay, bh, by);
            if (n < 2)
                continue;
            double za = h.Stations[i].Z, zb = h.Stations[i + 1].Z;
            Cum(ah, ay, n, ca);
            Cum(bh, by, n, cb);
            double arc = (ca[n - 1] + cb[n - 1]) / 2, len = zb - za;
            if (arc < 0.3 || len < 0.3)
                continue;
            int nu = Math.Clamp((int)Math.Round(arc / size), 1, 64), nv = Math.Clamp((int)Math.Round(len / size), 1, 64);
            var yMid = (Math.Min(ay[0], by[0]) + Math.Max(ay[n - 1], by[n - 1])) / 2;
            double du = CladSeam / arc, dv = CladSeam / len;
            foreach (var s in new[] { 1, -1 })
            {
                if (m.Tris - start > MaxCladTris)
                {
                    CladCapped = h.Name;
                    Console.WriteLine($"ship designer: cladding on \"{h.Name}\" hit its cap of {MaxCladTris} triangles; the rest of the body is left bare");
                    return;
                }
                // The same seed a band on both sides, so the plating is symmetric but where an opening breaks it.
                var rng = new Random(unchecked(h.CladSeed * 1000003 + i * 7919));
                (double X, double Y, double Z) P(double u, double v)
                {
                    var (xa, ya) = AtArc(ah, ay, ca, n, u);
                    var (xb, yb) = AtArc(bh, by, cb, n, u);
                    return (s * (xa + (xb - xa) * v), ya + (yb - ya) * v, za + (zb - za) * v);
                }
                (double X, double Y, double Z) Nrm(double u, double v)
                {
                    const double e = 1e-3;
                    var (u0, u1) = (Math.Max(0, u - e), Math.Min(1, u + e));
                    var (v0, v1) = (Math.Max(0, v - e), Math.Min(1, v + e));
                    var pu = Sub(P(u1, v), P(u0, v));
                    var pv = Sub(P(u, v1), P(u, v0));
                    var c = (pu.Y * pv.Z - pu.Z * pv.Y, pu.Z * pv.X - pu.X * pv.Z, pu.X * pv.Y - pu.Y * pv.X);
                    var l = Math.Sqrt(c.Item1 * c.Item1 + c.Item2 * c.Item2 + c.Item3 * c.Item3);
                    if (l < 1e-12)
                        return (s, 0, 0);
                    var p = P(u, v);
                    var sg = c.Item1 * p.X + c.Item2 * (p.Y - yMid) >= 0 ? 1 : -1;
                    return (sg * c.Item1 / l, sg * c.Item2 / l, sg * c.Item3 / l);
                }
                (double, double, double) W(double u, double v, double off)
                {
                    var p = P(u, v);
                    var q = Nrm(u, v);
                    return (cx + p.X + q.X * off, h.Y + p.Y + q.Y * off, h.Z + p.Z + q.Z * off);
                }
                var taken = new bool[nu, nv];
                for (var a = 0; a < nu; a++)
                    for (var b = 0; b < nv; b++)
                    {
                        double u0 = (double)a / nu, u1 = (double)(a + 1) / nu, v0 = (double)b / nv, v1 = (double)(b + 1) / nv;
                        taken[a, b] = blocked(W(u0, v0, 0), W(u1, v1, 0)) || blocked(W(u1, v0, 0), W(u0, v1, 0));
                    }
                var last = -1.0;
                for (var b = 0; b < nv; b++)
                    for (var a = 0; a < nu; a++)
                    {
                        if (taken[a, b])
                            continue;
                        int big = heavy ? 4 : 3, wu = 1 + rng.Next(big), wv = 1 + rng.Next(big);
                        var hgt = CladMin + rise * rng.NextDouble();
                        var tone = 0.88 + 0.18 * rng.NextDouble();
                        var greeble = heavy && rng.NextDouble() < 0.35;
                        var gr = (rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
                        // Grow across then along while the cells are free.
                        var cu = 1;
                        while (cu < wu && a + cu < nu && !taken[a + cu, b])
                            cu++;
                        var cv = 1;
                        while (cv < wv && b + cv < nv && Enumerable.Range(a, cu).All(k => !taken[k, b + cv]))
                            cv++;
                        for (var x = a; x < a + cu; x++)
                            for (var y = b; y < b + cv; y++)
                                taken[x, y] = true;
                        // Staggered: never within 5 mm of the plate before (a 5 mm step, and at least 1 cm up).
                        hgt = Math.Round(hgt / 0.005) * 0.005;
                        if (Math.Abs(hgt - last) < 0.005)
                            hgt += hgt + 0.005 <= CladMin + rise ? 0.005 : -0.005;
                        last = hgt;
                        hgt *= h.CladDepth;
                        hgt = Math.Max(hgt, 0.004 + 0.002);
                        double pu0 = (double)a / nu + du, pu1 = (double)(a + cu) / nu - du, pv0 = (double)b / nv + dv, pv1 = (double)(b + cv) / nv - dv;
                        if (pu1 <= pu0 || pv1 <= pv0)
                            continue;
                        var top = DesignMesh.Tone(col, tone);
                        var side = DesignMesh.Tone(col, tone * 0.72);
                        // The top follows the skin's curve: one quad a cell.
                        double U(int k) => pu0 + (pu1 - pu0) * k / cu;
                        double V(int k) => pv0 + (pv1 - pv0) * k / cv;
                        for (var x = 0; x < cu; x++)
                            for (var y = 0; y < cv; y++)
                                m.Quad(W(U(x), V(y), hgt), W(U(x + 1), V(y), hgt), W(U(x + 1), V(y + 1), hgt), W(U(x), V(y + 1), hgt), top, false);
                        // The skirt, sunk into the hull.
                        for (var x = 0; x < cu; x++)
                        {
                            m.Quad(W(U(x), pv0, -CladSink), W(U(x + 1), pv0, -CladSink), W(U(x + 1), pv0, hgt), W(U(x), pv0, hgt), side, false);
                            m.Quad(W(U(x), pv1, -CladSink), W(U(x + 1), pv1, -CladSink), W(U(x + 1), pv1, hgt), W(U(x), pv1, hgt), side, false);
                        }
                        for (var y = 0; y < cv; y++)
                        {
                            m.Quad(W(pu0, V(y), -CladSink), W(pu0, V(y + 1), -CladSink), W(pu0, V(y + 1), hgt), W(pu0, V(y), hgt), side, false);
                            m.Quad(W(pu1, V(y), -CladSink), W(pu1, V(y + 1), -CladSink), W(pu1, V(y + 1), hgt), W(pu1, V(y), hgt), side, false);
                        }
                        if (greeble)
                        {
                            double pw = (pu1 - pu0) * arc, pl = (pv1 - pv0) * len;
                            if (pw < 0.4 || pl < 0.4)
                                continue;
                            // A small box on the plate, its own axes the skin's there (across, out, along).
                            double gu = pu0 + (pu1 - pu0) * (0.25 + 0.5 * gr.Item1), gv = pv0 + (pv1 - pv0) * (0.25 + 0.5 * gr.Item2);
                            var o = W(gu, gv, hgt);
                            var nn = Nrm(gu, gv);
                            var t = Sub(P(Math.Min(1, gu + 1e-3), gv), P(Math.Max(0, gu - 1e-3), gv));
                            var tl = Math.Sqrt(t.X * t.X + t.Y * t.Y + t.Z * t.Z);
                            if (tl < 1e-12)
                                continue;
                            double[] xa = [t.X / tl, t.Y / tl, t.Z / tl], ya = [nn.X, nn.Y, nn.Z];
                            double[] zz = [xa[1] * ya[2] - xa[2] * ya[1], xa[2] * ya[0] - xa[0] * ya[2], xa[0] * ya[1] - xa[1] * ya[0]];
                            double gx = Math.Min(0.08 + 0.15 * gr.Item3, pw / 4), gz = Math.Min(0.1 + 0.25 * gr.Item1, pl / 4), gh = 0.04 + 0.08 * gr.Item2;
                            m.Frame(o.Item1, o.Item2, o.Item3, xa, ya, zz);
                            var g = DesignMesh.Tone(col, tone * 0.8);
                            m.Box(-gx, -0.02, -gz, gx, gh, gz, $"#{g.Item1:x2}{g.Item2:x2}{g.Item3:x2}");
                            m.World();
                        }
                    }
            }
        }
    }

    private static (double X, double Y, double Z) Sub((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static void Cum(double[] hs, double[] ys, int n, double[] c)
    {
        c[0] = 0;
        for (var k = 1; k < n; k++)
            c[k] = c[k - 1] + Math.Sqrt(Math.Pow(hs[k] - hs[k - 1], 2) + Math.Pow(ys[k] - ys[k - 1], 2));
    }

    /// <summary>The point a share u of the way round a sampled half profile, by length.</summary>
    private static (double H, double Y) AtArc(double[] hs, double[] ys, double[] c, int n, double u)
    {
        var l = Math.Clamp(u, 0, 1) * c[n - 1];
        for (var k = 0; k < n - 1; k++)
            if (l <= c[k + 1] || k == n - 2)
            {
                var seg = c[k + 1] - c[k];
                var f = seg < 1e-9 ? 0 : Math.Clamp((l - c[k]) / seg, 0, 1);
                return (hs[k] + (hs[k + 1] - hs[k]) * f, ys[k] + (ys[k + 1] - ys[k]) * f);
            }
        return (hs[0], ys[0]);
    }

    // ---- partition walls

    /// <summary>
    /// A room's partition walls, 10 cm thick, from just over its floor to just under its ceiling. One across (axis x) reaches the room's
    /// real sides at each height (out to the skin for a room out to the hull), so it follows the hull's shape; one along (axis z) runs its
    /// drawn length. A door gap leaves the wall open to 2 m up (less in a low room) with a lintel over it.
    /// </summary>
    public static DesignMesh WallMesh(ShipDesign d, Room r)
    {
        var m = new DesignMesh();
        var dk = d.DeckOf(r);
        var col = DesignMesh.Tone(DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour), 0.75);
        double lo = 0.025, hi = dk.Height - 0.012;
        const int nu = 6;
        foreach (var w in r.Walls)
        {
            var across = w.Axis == "x";
            // The wall's place across its own run (c) and along it (a), to world.
            var c = across ? r.Z0 + (r.Z1 - r.Z0) * w.T : r.X0 + (r.X1 - r.X0) * w.T;
            (double, double, double) P(double a, double off, double up)
            {
                var p = across ? d.World(dk, a, c + off, up) : d.World(dk, c + off, a, up);
                return (p[0], p[1], p[2]);
            }
            (double A0, double A1) Ends(double up)
            {
                if (!across)
                    return (r.Z0 + 0.01, r.Z1 - 0.01);
                var (l, rr) = Edges(d, r, c, up);
                return (l + 0.01, rr - 0.01);
            }
            var doorH = Math.Min(RoomWall.DoorH, hi - 0.2);
            var dc = (across ? r.X0 + (r.X1 - r.X0) * w.DoorT : r.Z0 + (r.Z1 - r.Z0) * w.DoorT);
            double g0 = dc - w.DoorW / 2, g1 = dc + w.DoorW / 2;
            var door = w.DoorW > 0.05 && doorH > 0.5;
            // Heights the wall is drawn between: even steps, with the door's head among them.
            var ups = Enumerable.Range(0, nu + 1).Select(k => lo + (hi - lo) * k / nu).ToList();
            if (door)
                ups.Add(doorH);
            ups = [.. ups.Distinct().OrderBy(x => x)];
            const double t = RoomWall.Thick / 2;
            for (var k = 0; k < ups.Count - 1; k++)
            {
                double u0 = ups[k], u1 = ups[k + 1];
                var (l0, r0) = Ends(u0);
                var (l1, r1) = Ends(u1);
                var spans = new List<(double, double, double, double)> { (l0, r0, l1, r1) };
                if (door && u1 <= doorH + 1e-6)
                {
                    spans.Clear();
                    // Left of the gap, then right of it, each where the wall is wide enough.
                    if (Math.Min(l0, l1) < g0) spans.Add((l0, Math.Min(r0, g0), l1, Math.Min(r1, g0)));
                    if (Math.Max(r0, r1) > g1) spans.Add((Math.Max(l0, g1), r0, Math.Max(l1, g1), r1));
                }
                foreach (var (a0, a1, a2, a3) in spans)
                {
                    if (a1 - a0 < 0.01 && a3 - a2 < 0.01)
                        continue;
                    foreach (var off in new[] { -t, t })
                        m.Quad(P(a0, off, u0), P(a1, off, u0), P(a3, off, u1), P(a2, off, u1), col, false);
                    m.Quad(P(a0, -t, u0), P(a0, t, u0), P(a2, t, u1), P(a2, -t, u1), DesignMesh.Tone(col, 0.85), false);
                    m.Quad(P(a1, -t, u0), P(a1, t, u0), P(a3, t, u1), P(a3, -t, u1), DesignMesh.Tone(col, 0.85), false);
                    if (door && Math.Abs(u0 - doorH) < 1e-6 && a0 < g0 + 1e-6 && a1 > g1 - 1e-6)
                        m.Quad(P(Math.Max(a0, g0), -t, u0), P(Math.Min(a1, g1), -t, u0), P(Math.Min(a1, g1), t, u0), P(Math.Max(a0, g0), t, u0), DesignMesh.Tone(col, 0.7), false);
                }
            }
            var (e0, e1) = Ends(hi);
            m.Quad(P(e0, -t, hi), P(e1, -t, hi), P(e1, t, hi), P(e0, t, hi), DesignMesh.Tone(col, 0.9), false);
        }
        return m;
    }
}
