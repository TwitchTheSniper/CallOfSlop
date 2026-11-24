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
        var ah = new double[HullBody.MaxLoft + 2];
        var ay = new double[HullBody.MaxLoft + 2];
        var bh = new double[HullBody.MaxLoft + 2];
        var by = new double[HullBody.MaxLoft + 2];
        var ca = new double[HullBody.MaxLoft + 2];
        var cb = new double[HullBody.MaxLoft + 2];
        var size = Math.Clamp(h.CladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        var rings = h.Ring;
        for (var i = 0; i < rings.Count - 1; i++)
        {
            var n = h.Pair(i, ah, ay, bh, by);
            if (n < 2)
                continue;
            // The profile runs out to the keel and spine strips (Karl, 2026-11-24: the top and bottom were left bare): a point on the
            // centreline at each end, so each side's plating reaches the middle and meets its mirror there.
            n = Centre(ah, ay, n);
            Centre(bh, by, n - 2);
            double za = rings[i].Z, zb = rings[i + 1].Z;
            Cum(ah, ay, n, ca);
            Cum(bh, by, n, cb);
            double arc = (ca[n - 1] + cb[n - 1]) / 2, len = zb - za;
            if (arc < 0.3 || len < 0.3)
                continue;
            int nu = Math.Clamp((int)Math.Round(arc / size), 1, 64), nv = Math.Clamp((int)Math.Round(len / size), 1, 64);
            var yMid = (Math.Min(ay[0], by[0]) + Math.Max(ay[n - 1], by[n - 1])) / 2;
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
                LayPlates(m, nu, nv, arc, len, rng, heavy, h.CladDepth, col, P, Nrm, W, taken, (u, v, cell) => h.Paint(za + (zb - za) * v, u, arc, i * 4096 + cell + 777));
            }
        }
    }

    /// <summary>
    /// Lays plates on a grid of cells over a surface P(u, v) (u across, v along, both 0..1; its outward normal Nrm, and W the world point
    /// off it by a distance): free cells merged at random into plates of one to four cells each way, each inset by a seam, raised by a varied
    /// step along the normal, its skirt sunk into the skin. Shared by bodies and connections; cells set in <paramref name="taken"/> stay bare.
    /// </summary>
    internal static void LayPlates(DesignMesh m, int nu, int nv, double arc, double len, Random rng, bool heavy, double depth, (byte, byte, byte) col,
        Func<double, double, (double X, double Y, double Z)> P, Func<double, double, (double X, double Y, double Z)> Nrm, Func<double, double, double, (double, double, double)> W, bool[,] taken,
        Func<double, double, int, (byte, byte, byte)>? paint = null)
    {
        var rise = (heavy ? 0.20 : 0.08) - CladMin;
        double du = CladSeam / arc, dv = CladSeam / len;
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
                hgt *= depth;
                hgt = Math.Max(hgt, 0.004 + 0.002);
                double pu0 = (double)a / nu + du, pu1 = (double)(a + cu) / nu - du, pv0 = (double)b / nv + dv, pv1 = (double)(b + cv) / nv - dv;
                if (pu1 <= pu0 || pv1 <= pv0)
                    continue;
                var pc = paint?.Invoke((pu0 + pu1) / 2, (pv0 + pv1) / 2, b * 64 + a) ?? col;
                var top = DesignMesh.Tone(pc, tone);
                var side = DesignMesh.Tone(pc, tone * 0.72);
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
                    var g = DesignMesh.Tone(pc, tone * 0.8);
                    m.Box(-gx, -0.02, -gz, gx, gh, gz, $"#{g.Item1:x2}{g.Item2:x2}{g.Item3:x2}");
                    m.World();
                }
            }
    }

    private static int Centre(double[] hs, double[] ys, int n)
    {
        for (var k = n; k > 0; k--)
        {
            hs[k] = hs[k - 1];
            ys[k] = ys[k - 1];
        }
        hs[0] = 0;
        ys[0] = ys[1];
        hs[n + 1] = 0;
        ys[n + 1] = ys[n];
        return n + 2;
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

    // ---- room shells (Karl, 2026-11-24: in place of partition walls)

    /// <summary>
    /// A room's walls: a shell WallThick thick built outward from its box's floor, ceiling and four sides, so the room keeps its full
    /// inside. Cut into cells about 30 cm square; a cell is left out where it is outside every hull (so the shell stops at the skin),
    /// inside a room of the same group or an older room (a shared wall is drawn once), or in a door between rooms (0.55 m either
    /// side of it, up to 2.1 m). A room out to the hull has no port or starboard walls, its floor and ceiling running out to the skin;
    /// a run-on end has none either. Kept cells are merged into runs, each one box.
    /// </summary>
    public static DesignMesh RoomShell(ShipDesign d, Room r, List<double[]> doors)
    {
        var m = new DesignMesh();
        var t = Math.Clamp(r.WallThick, 0, Room.WallThickMax);
        if (t < 0.005)
            return m;
        m.World();
        var b = d.Box(r);
        // World axes: across (plan u, X), along (plan v) and up.
        int ax = 0, al = d.Stacked ? 1 : 2, up = d.Stacked ? 2 : 1;
        var lo = new[] { b[0], b[2], b[4] };
        var hi = new[] { b[1], b[3], b[5] };
        // Its inner faces 5 mm off the box's own (and so off the room's markers and the deck), never on them.
        for (var k = 0; k < 3; k++)
        {
            lo[k] -= 0.005;
            hi[k] += 0.005;
        }
        var colour = DesignMesh.Tone(DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour), 0.55);
        var hex = $"#{colour.Item1:x2}{colour.Item2:x2}{colour.Item3:x2}";
        double wide0 = lo[ax], wide1 = hi[ax];
        if (r.ToHull)
            foreach (var h in d.Hulls)
            {
                var (x0, x1, _, _, _, _) = h.Bounds();
                wide0 = Math.Min(wide0, x0);
                wide1 = Math.Max(wide1, x1);
            }
        // Grown by their own shells, so where two shells would share a plane (side by side, floor under floor) only the older draws.
        var others = d.Rooms.Where(o => o != r && ((r.Group != 0 && o.Group == r.Group) || o.Id < r.Id)).Select(o =>
        {
            var ob = (double[])d.Box(o).Clone();
            var g = o.Group != 0 && o.Group == r.Group ? 0 : Math.Clamp(o.WallThick, 0, Room.WallThickMax) + 0.005;
            for (var k = 0; k < 6; k++)
                ob[k] += k % 2 == 0 ? -g : g;
            return ob;
        }).ToList();
        bool Keep(double[] p, bool hull = true)
        {
            if (hull && !d.Hulls.Any(h => h.Depth(p[0], p[1], p[2]) > 0.01))
                return false;
            foreach (var o in others)
                if (p[0] > o[0] && p[0] < o[1] && p[1] > o[2] && p[1] < o[3] && p[2] > o[4] && p[2] < o[5])
                    return false;
            foreach (var q in doors)
            {
                var rise = d.Stacked ? q[up] - p[up] : p[up] - q[up];
                if (Math.Abs(p[ax] - q[ax]) < 0.55 && Math.Abs(p[al] - q[al]) < 0.55 && rise > -0.3 && rise < 2.1)
                    return false;
            }
            return true;
        }
        if (r.ToHull)
        {
            HullShell(d, r, t, colour, p => Keep(p, false), m);
            return m;
        }
        // A slab: its extent on each world axis, cut along two of them (i across, j along), the third its thickness.
        void Slab(double[] s0, double[] s1, int i, int j)
        {
            int ni = Math.Clamp((int)Math.Ceiling((s1[i] - s0[i]) / 0.3), 1, 160), nj = Math.Clamp((int)Math.Ceiling((s1[j] - s0[j]) / 0.3), 1, 160);
            var p = new double[3];
            var c0 = new double[3];
            var c1 = new double[3];
            for (var bj = 0; bj < nj; bj++)
            {
                var run = -1;
                for (var bi = 0; bi <= ni; bi++)
                {
                    var keep = false;
                    if (bi < ni)
                    {
                        for (var k = 0; k < 3; k++)
                            p[k] = (s0[k] + s1[k]) / 2;
                        p[i] = s0[i] + (s1[i] - s0[i]) * (bi + 0.5) / ni;
                        p[j] = s0[j] + (s1[j] - s0[j]) * (bj + 0.5) / nj;
                        keep = Keep(p);
                    }
                    if (keep && run < 0)
                        run = bi;
                    if (!keep && run >= 0)
                    {
                        Array.Copy(s0, c0, 3);
                        Array.Copy(s1, c1, 3);
                        c0[i] = s0[i] + (s1[i] - s0[i]) * run / ni;
                        c1[i] = s0[i] + (s1[i] - s0[i]) * bi / ni;
                        c0[j] = s0[j] + (s1[j] - s0[j]) * bj / nj;
                        c1[j] = s0[j] + (s1[j] - s0[j]) * (bj + 1) / nj;
                        m.Box(c0[0], c0[1], c0[2], c1[0], c1[1], c1[2], hex);
                        run = -1;
                    }
                }
            }
        }
        double[] V(double a, double b2, double u) { var v = new double[3]; v[ax] = a; v[al] = b2; v[up] = u; return v; }
        // Floor and ceiling, over the whole footprint and its walls (offset 3 mm in from the sides' faces so no faces meet).
        Slab(V(wide0 - t, lo[al] - t, lo[up] - t), V(wide1 + t, hi[al] + t, lo[up]), ax, al);
        Slab(V(wide0 - t, lo[al] - t, hi[up]), V(wide1 + t, hi[al] + t, hi[up] + t), ax, al);
        if (!r.ToHull)
        {
            Slab(V(lo[ax] - t, lo[al] - t, lo[up] + 0.003), V(lo[ax], hi[al] + t, hi[up] - 0.003), al, up);
            Slab(V(hi[ax], lo[al] - t, lo[up] + 0.003), V(hi[ax] + t, hi[al] + t, hi[up] - 0.003), al, up);
        }
        // Fore and aft (stacked: below and above on the plan), less a run-on end.
        var noLo = r.ToHull && r.RunOn && !d.Stacked;
        var noHi = r.ToHull && r.RunAft && !d.Stacked;
        if (!noLo)
            Slab(V(wide0 - 0.003 + (r.ToHull ? 0 : 0.006), lo[al] - t, lo[up] + 0.003), V(wide1 + 0.003 - (r.ToHull ? 0 : 0.006), lo[al], hi[up] - 0.003), ax, up);
        if (!noHi)
            Slab(V(wide0 - 0.003 + (r.ToHull ? 0 : 0.006), hi[al], lo[up] + 0.003), V(wide1 + 0.003 - (r.ToHull ? 0 : 0.006), hi[al] + t, hi[up] - 0.003), ax, up);
        return m;
    }

    /// <summary>
    /// The shell of a room out to the hull, from the very samples <see cref="RoomMesh"/> draws its volume with (its Span, its Edges at the
    /// same four heights and the same 50 cm slices), so it follows the skin as the hologram does: its inner face 5 mm off the hologram,
    /// its outer face t beyond that. A quad whose middle is in a door, in an older room's shell or a group-mate's room is left out.
    /// </summary>
    private static void HullShell(ShipDesign d, Room r, double t, (byte, byte, byte) col, Func<double[], bool> keep, DesignMesh m)
    {
        const double g = 0.005;
        var dk = d.DeckOf(r);
        var (s0, s1) = Span(d, r);
        var nv = Math.Max(1, (int)Math.Ceiling((s1 - s0) / 0.5));
        const int nu = 4;
        var hgt = dk.Height;
        (double, double, double) P(double u, double v, double up) { var w = d.World(dk, u, v, up); return (w[0], w[1], w[2]); }
        double V(int i) => s0 + (s1 - s0) * i / nv;
        double Up(int k) => hgt * k / nu;
        var side = DesignMesh.Tone(col, 0.9);
        // Both faces of a strip of shell: inner (off by g) and outer (off by g + t), dir the plan offset per unit of push.
        void Two(Func<double, ((double, double, double), (double, double, double), (double, double, double), (double, double, double))> at, (byte, byte, byte) c)
        {
            var (a, b, cc, dd) = at(g);
            var mid = new[] { (a.Item1 + cc.Item1) / 2, (a.Item2 + cc.Item2) / 2, (a.Item3 + cc.Item3) / 2 };
            if (!keep(mid))
                return;
            m.Quad(a, b, cc, dd, c, false);
            var (a2, b2, c2, d2) = at(g + t);
            m.Quad(a2, b2, c2, d2, c, false);
            // Its rims, so the shell reads solid edge-on.
            m.Quad(a, b, b2, a2, side, false);
            m.Quad(cc, dd, d2, c2, side, false);
        }
        for (var i = 0; i < nv; i++)
        {
            double va = V(i), vb = V(i + 1);
            for (var k = 0; k < nu; k++)
            {
                double ua = Up(k), ub = Up(k + 1);
                var (a0, b0) = Edges(d, r, va, ua);
                var (a1, b1) = Edges(d, r, vb, ua);
                var (a2, b2) = Edges(d, r, va, ub);
                var (a3, b3) = Edges(d, r, vb, ub);
                // Its first and last rows reach on to the floor's and ceiling's outer faces, so the corners close.
                Two(o => (P(a0 - o, va, k == 0 ? ua - o : ua), P(a1 - o, vb, k == 0 ? ua - o : ua), P(a3 - o, vb, k == nu - 1 ? ub + o : ub), P(a2 - o, va, k == nu - 1 ? ub + o : ub)), col);
                Two(o => (P(b0 + o, va, k == 0 ? ua - o : ua), P(b1 + o, vb, k == 0 ? ua - o : ua), P(b3 + o, vb, k == nu - 1 ? ub + o : ub), P(b2 + o, va, k == nu - 1 ? ub + o : ub)), col);
            }
            var (f0, h0) = Edges(d, r, va, 0);
            var (f1, h1) = Edges(d, r, vb, 0);
            Two(o => (P(f0 - o, va, -o), P(h0 + o, va, -o), P(h1 + o, vb, -o), P(f1 - o, vb, -o)), col);
            var (c0, e0) = Edges(d, r, va, hgt);
            var (c1, e1) = Edges(d, r, vb, hgt);
            Two(o => (P(c0 - o, va, hgt + o), P(e0 + o, va, hgt + o), P(e1 + o, vb, hgt + o), P(c1 - o, vb, hgt + o)), col);
        }
        // Its ends, less one that runs on (the hologram's ends are at the drawn box's ends).
        var noLo = r.RunOn && !d.Stacked;
        var noHi = r.RunAft && !d.Stacked;
        foreach (var (v, sg) in new[] { (r.Z0, -1.0), (r.Z1, 1.0) })
        {
            if (sg < 0 ? noLo : noHi)
                continue;
            for (var k = 0; k < nu; k++)
            {
                double ua = Up(k), ub = Up(k + 1);
                var (a0, b0) = Edges(d, r, v, ua);
                var (a1, b1) = Edges(d, r, v, ub);
                Two(o => (P(a0 - g, v + sg * o, k == 0 ? ua - g : ua), P(b0 + g, v + sg * o, k == 0 ? ua - g : ua), P(b1 + g, v + sg * o, k == nu - 1 ? ub + g : ub), P(a1 - g, v + sg * o, k == nu - 1 ? ub + g : ub)), col);
            }
        }
    }
}
