namespace CallOfSlop.ShipDesigner;

public static partial class DesignScene
{
    /// <summary>The most triangles one copy of a body's cladding may add; past it the panel size is raised until the whole body fits (logged).</summary>
    public const int MaxCladTris = 60000;

    /// <summary>The last body whose cladding hit the cap (its name), for the page's log; empty when none did.</summary>
    public static string CladCapped = "";

    /// <summary>The panel size the last <see cref="CladBody"/> used when it had to raise it to fit the cap; 0 when the body's own size fitted.</summary>
    public static double CladFitSize;

    private const double CladSeam = 0.02, CladSink = 0.03, CladMin = 0.01;

    /// <summary>
    /// Plated cladding on one copy of a body's sides (the user's Star Destroyer flank, 2026-11-24): each band between two stations is
    /// a grid of cells along the arc and along z, merged at random into plates of one to four cells each way, each plate inset by a seam,
    /// raised by a varied height along the skin's normal (at least 1 cm, so 4 mm and more off the skin and staggered from the plate
    /// before), its skirt sunk 3 cm into the hull. Heavy plates rise higher and some carry a small box. Seeded, so the same design
    /// always clads the same; cells <paramref name="blocked"/> says an opening may reach (by two opposite world corners) stay bare.
    /// </summary>
    /// <returns>The panel size used: the body's own, or larger when its cladding wouldn't fit <see cref="MaxCladTris"/> (2026-11-25: the cap
    /// used to cut the cladding off part way, leaving most of a big hull bare; now the whole body is always covered, in bigger panels).</returns>
    private static double CladBody(DesignMesh m, HullBody h, double cx, Func<(double, double, double), (double, double, double), bool> blocked, Func<(double, double, double), (double, double, double), bool> nearCut, Func<(double, double, double), bool> fine)
    {
        CladFitSize = 0;
        var own = Math.Clamp(h.CladSize, HullBody.CladSizeMin, HullBody.CladSizeMax);
        var size = own;
        var start = m.Tris;
        for (var tries = 0; ; tries++)
        {
            CladBodyAt(m, h, cx, new(blocked, nearCut, fine), size);
            var made = m.Tris - start;
            if (made <= MaxCladTris)
                break;
            if (size >= HullBody.CladSizeMax || tries >= 6)
            {
                CladCapped = h.Name;
                Console.WriteLine($"ship designer: cladding on \"{h.Name}\" has {made} triangles even at {size * 100:0} cm panels, past its cap of {MaxCladTris}; kept whole");
                break;
            }
            m.P.RemoveRange(start * 9, m.P.Count - start * 9);
            m.C.RemoveRange(start * 9, m.C.Count - start * 9);
            // Triangles go as 1/size^2; a margin and a 5 cm step so one more try is rarely needed.
            size = Math.Min(HullBody.CladSizeMax, Math.Ceiling(size * Math.Sqrt((double)made / MaxCladTris) * 1.05 / 0.05) * 0.05);
        }
        if (size > own)
        {
            CladFitSize = size;
            Console.WriteLine($"ship designer: cladding on \"{h.Name}\" raised from {own * 100:0} cm to {size * 100:0} cm panels to fit its cap of {MaxCladTris} triangles");
        }
        return size;
    }

    private static void CladBodyAt(DesignMesh m, HullBody h, double cx, CladMask blocked, double size)
    {
        var heavy = h.Clad == "Heavy";
        var hex = h.Clad == "Hex";
        var col = DesignMesh.Hex(h.Skin);
        var ah = new double[HullBody.MaxLoft + 2];
        var ay = new double[HullBody.MaxLoft + 2];
        var bh = new double[HullBody.MaxLoft + 2];
        var by = new double[HullBody.MaxLoft + 2];
        var ca = new double[HullBody.MaxLoft + 2];
        var cb = new double[HullBody.MaxLoft + 2];
        var rings = h.Ring;
        // The ends, where the body meets its front and back (Karl, 2026-11-27: rows of small plates there, busy): the first and last
        // bands and any run of short bands next to them (a rounded end's rings) are always laid smooth, as Smooth busy areas does.
        int firstLong = 0, lastLong = rings.Count - 2;
        while (firstLong < rings.Count - 1 && rings[firstLong + 1].Z - rings[firstLong].Z < 1.5 * size)
            firstLong++;
        while (lastLong > 0 && rings[lastLong + 1].Z - rings[lastLong].Z < 1.5 * size)
            lastLong--;
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
            double arcA = ca[n - 1], arcB = cb[n - 1], arc = (arcA + arcB) / 2, len = zb - za;
            if (Math.Max(arcA, arcB) < 0.3 || len < 0.3)
                continue;
            int nu = Math.Clamp((int)Math.Round(arc / size), 1, 64), nv = Math.Clamp((int)Math.Round(len / size), 1, 64);
            // A terminus (the first or last band, or one whose arc changes past 1.4x: a tapering nose, a rounded end) is laid in rows
            // along z (the user, 2026-11-24: one grid there tapered into slivers meeting at the tip), each row its own count round
            // the arc, from its own arc, the seams staggered row to row; near the tip, where a row's arc is under two panels, it is
            // one wide cap ring a side that wraps the end.
            var ratio = Math.Max(arcA, arcB) / Math.Max(1e-6, Math.Min(arcA, arcB));
            var terminus = i == 0 || i == rings.Count - 2 || ratio > 1.4;
            // Smooth busy areas (Karl, 2026-11-27: a tapering end was a clutter of small plates at many heights): a terminus, or a band
            // too short for a plate's length, is laid in plates twice the size, all at one low height, merged as far as they go, no greebles.
            var endZone = i == 0 || i == rings.Count - 2 || i < firstLong || i > lastLong;
            var smooth = endZone || h.CladSmooth && (terminus || len < 1.5 * size);
            var tsize = smooth ? size * 2 : size;
            var tnv = smooth ? Math.Clamp((int)Math.Round(len / tsize), 1, 64) : nv;
            var yMid = (Math.Min(ay[0], by[0]) + Math.Max(ay[n - 1], by[n - 1])) / 2;
            // Round the profile by its points, not by length (Karl, 2026-11-27: plates clipped through each other along a chine): point k
            // sits at the same u at both ends, so a corner of the profile runs straight along the band, and the grid's columns break at
            // every sharp corner (a crease), so no plate is laid across one and the plates either side meet on its mitre.
            var knot = new double[n];
            for (var k = 0; k < n; k++)
                knot[k] = 0.5 * (arcA > 1e-9 ? ca[k] / arcA : (double)k / (n - 1)) + 0.5 * (arcB > 1e-9 ? cb[k] / arcB : (double)k / (n - 1));
            knot[n - 1] = 1;
            var creases = new List<double> { 0 };
            for (var k = 1; k < n - 1; k++)
                if (Math.Max(Turn(ah, ay, k), Turn(bh, by, k)) > CreaseAngle && knot[k] - creases[^1] > 1e-4)
                    creases.Add(knot[k]);
            creases.Add(1);
            var spanCells = new int[creases.Count - 1];
            var cols = 0;
            for (var j = 0; j < spanCells.Length; j++)
                cols += spanCells[j] = Math.Max(1, (int)Math.Round(arc * (creases[j + 1] - creases[j]) / size));
            var brk = new bool[Math.Max(1, cols)];
            for (int j = 0, c = 0; j < spanCells.Length; c += spanCells[j], j++)
                if (c + spanCells[j] - 1 < brk.Length)
                    brk[c + spanCells[j] - 1] = true;
            // Grid share t (cols equal columns) to the profile's share u: each crease span its own whole number of columns.
            double Gc(double t)
            {
                var q = Math.Clamp(t, 0, 1) * cols;
                var c = 0;
                for (var j = 0; j < spanCells.Length; j++)
                {
                    if (q <= c + spanCells[j] || j == spanCells.Length - 1)
                        return creases[j] + (creases[j + 1] - creases[j]) * Math.Clamp((q - c) / spanCells[j], 0, 1);
                    c += spanCells[j];
                }
                return 1;
            }
            foreach (var s in new[] { 1, -1 })
            {
                // The same seed a band on both sides, so the plating is symmetric but where an opening breaks it.
                var rng = new Random(unchecked(h.CladSeed * 1000003 + i * 7919));
                (double X, double Y, double Z) P(double u, double v)
                {
                    var (xa, ya) = AtKnot(ah, ay, knot, n, u);
                    var (xb, yb) = AtKnot(bh, by, knot, n, u);
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
                if (!terminus)
                {
                    (double X, double Y, double Z) Pc(double t, double v) => P(Gc(t), v);
                    (double X, double Y, double Z) Nc(double t, double v) => Nrm(Gc(t), v);
                    (double, double, double) Wc(double t, double v, double off) => W(Gc(t), v, off);
                    LayGrid(m, hex ? nu : cols, nv, arc, len, rng, heavy, h.CladDepth, col, hex ? P : Pc, hex ? Nrm : Nc, hex ? W : Wc, blocked,
                        (u, v, cell) => h.Paint(za + (zb - za) * v, hex ? u : Gc(u), arc, i * 4096 + cell + 777), 0, hex, h.Damage, h.DamageSeed, hex ? null : brk, Gc, knot, smooth);
                    continue;
                }
                for (var r = 0; r < tnv; r++)
                {
                    double r0 = (double)r / tnv, r1 = (double)(r + 1) / tnv;
                    var rowArc = arcA + (arcB - arcA) * (r0 + r1) / 2;
                    if (rowArc < 0.05)
                        continue;
                    var cap = rowArc < 2 * tsize;
                    var ru = cap ? 1 : Math.Clamp((int)Math.Round(rowArc / tsize), 1, 64);
                    // Masonry: every other row starts with a half cell, so its seams fall mid-plate of the row before.
                    var stag = !cap && ru > 1 && (r & 1) == 1;
                    var cells = stag ? ru + 1 : ru;
                    double G(double t)
                    {
                        if (!stag)
                            return t;
                        var q = Math.Clamp(t, 0, 1) * cells;
                        var k = Math.Min((int)q, cells - 1);
                        double B(int j) => j <= 0 ? 0 : j >= cells ? 1 : (j - 0.5) / ru;
                        return B(k) + (B(k + 1) - B(k)) * (q - k);
                    }
                    var rr = r;
                    (double X, double Y, double Z) Pr(double u, double v) => P(G(u), r0 + (r1 - r0) * v);
                    (double X, double Y, double Z) Nr(double u, double v) => Nrm(G(u), r0 + (r1 - r0) * v);
                    (double, double, double) Wr(double u, double v, double off) => W(G(u), r0 + (r1 - r0) * v, off);
                    LayGrid(m, cells, 1, rowArc, len / tnv, rng, heavy, h.CladDepth, col, Pr, Nr, Wr, blocked,
                        (u, v, cell) => h.Paint(za + (zb - za) * (r0 + (r1 - r0) * v), G(u), arc, i * 4096 + rr * 97 + cell + 777), cap ? 1 : 0, hex, h.Damage, h.DamageSeed, null, G, knot, smooth);
                }
            }
        }
    }

    /// <summary>
    /// Plated cladding on a flat end cap (the user, 2026-11-24: the stern round the drive bells was bare), laid simply (Karl, 2026-11-27:
    /// rings of small plates round the middle read as a busy spiral): a straight grid of cells across the cap, half the panel size, kept
    /// where it lies wholly inside the outline, merged into big flush plates at one height. A cell any point of which
    /// <paramref name="open"/> says is in an opening (a drive bell, a mouth) or another body stays bare.
    /// </summary>
    private static void CladCap(DesignMesh m, double cx, HullBody h, Station s, bool aft, Func<double, double, double, bool> open, double size)
    {
        var heavy = h.Clad == "Heavy";
        var col = DesignMesh.Hex(h.Skin);
        if (h.CladSmooth)
            size *= 1.5;
        // The outline as a closed ring, round the +x side and back down the -x.
        var ring = new List<(double X, double Y)>();
        foreach (var (hh, y) in s.P)
            ring.Add((hh, y));
        for (var k = s.P.Count - 1; k >= 0; k--)
            ring.Add((-s.P[k].H, s.P[k].Y));
        if (ring.Count < 3)
            return;
        double x1 = ring.Max(q => q.X) - CapInset, x0 = -x1, y0 = ring.Min(q => q.Y) + CapInset, y1 = ring.Max(q => q.Y) - CapInset;
        double w = x1 - x0, ht = y1 - y0, z = h.Z + s.Z, nz = aft ? 1 : -1;
        if (w < 0.5 || ht < 0.5)
            return;
        bool Inside(double x, double y) // (even-odd, and CapInset clear of every edge)
        {
            var c = false;
            for (int a = 0, b = ring.Count - 1; a < ring.Count; b = a++)
            {
                var (p, q) = (ring[a], ring[b]);
                if ((p.Y > y) != (q.Y > y) && x < (q.X - p.X) * (y - p.Y) / (q.Y - p.Y) + p.X)
                    c = !c;
                var (ex, ey) = (q.X - p.X, q.Y - p.Y);
                var l2 = ex * ex + ey * ey;
                var t = l2 < 1e-12 ? 0 : Math.Clamp(((x - p.X) * ex + (y - p.Y) * ey) / l2, 0, 1);
                if (CapSq(x - p.X - ex * t) + CapSq(y - p.Y - ey * t) < CapInset * CapInset)
                    return false;
            }
            return c;
        }
        // Cells half the panel size, so the rim left bare round the outline is narrow; merged four by four into plates twice the size.
        int nu = Math.Clamp((int)Math.Round(2 * w / size), 1, 64), nv = Math.Clamp((int)Math.Round(2 * ht / size), 1, 64);
        // The fore cap runs the other way across, so its plates face out.
        (double X, double Y, double Z) P(double u, double v) => (aft ? x0 + w * u : x1 - w * u, y0 + ht * v, s.Z);
        (double X, double Y, double Z) N(double u, double v) => (0, 0, nz);
        (double, double, double) W(double u, double v, double off)
        {
            var p = P(u, v);
            return (cx + p.X, h.Y + p.Y, z + nz * off);
        }
        var taken = new bool[nu, nv];
        for (var a = 0; a < nu; a++)
            for (var b = 0; b < nv; b++)
                for (var i = 0; i <= 2 && !taken[a, b]; i++)
                    for (var j = 0; j <= 2; j++)
                    {
                        var p = P((a + i / 2.0) / nu, (b + j / 2.0) / nv);
                        var q = W((a + i / 2.0) / nu, (b + j / 2.0) / nv, 0);
                        if (!Inside(p.X, p.Y) || open(q.Item1, q.Item2, q.Item3))
                        {
                            taken[a, b] = true;
                            break;
                        }
                    }
        var rng = new Random(unchecked(h.CladSeed * 1000003 + (aft ? 31337 : 4241)));
        LayPlates(m, nu, nv, w, ht, rng, heavy, h.CladDepth, col, P, N, W, taken,
            (u, v, cell) => h.Paint(s.Z, u, w, (aft ? 50000 : 60000) + cell), 0, h.Clad == "Hex", h.Damage, h.DamageSeed, smooth: true);
    }

    /// <summary>How far an end cap's plates keep in from its outline (m).</summary>
    private const double CapInset = 0.05;

    private static double CapSq(double v) => v * v;

    /// <summary>One grid of cells over P, its cells an opening may reach left bare, then laid with <see cref="LayPlates"/>.</summary>
    private static void LayGrid(DesignMesh m, int nu, int nv, double arc, double len, Random rng, bool heavy, double depth, (byte, byte, byte) col,
        Func<double, double, (double X, double Y, double Z)> P, Func<double, double, (double X, double Y, double Z)> Nrm, Func<double, double, double, (double, double, double)> W,
        CladMask mask, Func<double, double, int, (byte, byte, byte)> paint, int maxAcross = 0, bool hex = false, double damage = 0, int dseed = 0, bool[]? brk = null, Func<double, double>? toU = null, double[]? knots = null, bool smooth = false)
    {
        var taken = new bool[nu, nv];
        var trim = new bool[nu, nv];
        var any = false;
        for (var a = 0; a < nu; a++)
            for (var b = 0; b < nv; b++)
            {
                double u0 = (double)a / nu, u1 = (double)(a + 1) / nu, v0 = (double)b / nv, v1 = (double)(b + 1) / nv;
                var (p0, p1, p2, p3) = (W(u0, v0, 0), W(u1, v1, 0), W(u1, v0, 0), W(u0, v1, 0));
                taken[a, b] = mask.Blocked(p0, p1) || mask.Blocked(p2, p3);
                // A cell an opening reaches is trimmed round it, not dropped (Karl, 2026-11-25); hex tiles still drop whole.
                if (!taken[a, b] && (mask.Near(p0, p1) || mask.Near(p2, p3)))
                {
                    taken[a, b] = true;
                    trim[a, b] = !hex;
                    any |= !hex;
                }
            }
        // The trimmed cells' plate heights are drawn from their own seed, so the rest of the grid lays as before.
        var trng = new Random(rng.Next());
        LayPlates(m, nu, nv, arc, len, rng, heavy, depth, col, P, Nrm, W, taken, paint, maxAcross, hex, damage, dseed, brk, toU, knots, smooth);
        if (!any)
            return;
        for (var a = 0; a < nu; a++)
            for (var b = 0; b < nv; b++)
                if (trim[a, b])
                    TrimCell(m, a, b, nu, nv, arc, len, trng, heavy, depth, col, W, mask.Fine, paint, damage, dseed);
    }

    /// <summary>What may hold cladding: Blocked cells stay bare, Near cells (an opening's clearance reaches them) are trimmed point by point with Fine.</summary>
    private sealed record CladMask(Func<(double, double, double), (double, double, double), bool> Blocked,
        Func<(double, double, double), (double, double, double), bool> Near, Func<(double, double, double), bool> Fine);

    /// <summary>
    /// One cell an opening reaches, as a plate trimmed round it (Karl, 2026-11-25: whole plates dropped left a big bare square round a
    /// porthole): the cell split into a fine grid (about 5 cm, at most 20 a side), a sub-cell left bare when any corner or its middle is in the
    /// opening's clearance, the rest laid as rows of strips at one height (runs merged across, equal runs merged along), seamed at the cell's
    /// edge, a skirt on every edge that borders bare.
    /// </summary>
    private static void TrimCell(DesignMesh m, int a, int b, int nu, int nv, double arc, double len, Random rng, bool heavy, double depth, (byte, byte, byte) col,
        Func<double, double, double, (double, double, double)> W, Func<(double, double, double), bool> fine, Func<double, double, int, (byte, byte, byte)>? paint, double damage, int dseed)
    {
        double cw = arc / nu, cl = len / nv;
        int nx = Math.Clamp((int)Math.Ceiling(cw / 0.05), 2, 20), ny = Math.Clamp((int)Math.Ceiling(cl / 0.05), 2, 20);
        var seam = CladSeam;
        double du = seam / arc, dv = seam / len;
        double ua = (double)a / nu + du, ub = (double)(a + 1) / nu - du, va = (double)b / nv + dv, vb = (double)(b + 1) / nv - dv;
        if (ub <= ua || vb <= va)
            return;
        double U(int i) => ua + (ub - ua) * i / nx;
        double V(int j) => va + (vb - va) * j / ny;
        var free = new bool[nx, ny];
        var pts = new bool[nx + 1, ny + 1];
        for (var i = 0; i <= nx; i++)
            for (var j = 0; j <= ny; j++)
                pts[i, j] = fine(W(U(i), V(j), 0));
        var anyFree = false;
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
            {
                free[i, j] = !(pts[i, j] || pts[i + 1, j] || pts[i, j + 1] || pts[i + 1, j + 1] || fine(W((U(i) + U(i + 1)) / 2, (V(j) + V(j + 1)) / 2, 0)));
                anyFree |= free[i, j];
            }
        var rise = HullBody.CladRise(heavy ? "Heavy" : "Plates") - CladMin;
        var hgt = Math.Max((CladMin + rise * rng.NextDouble()) * depth, 0.006);
        var tone = 0.88 + 0.18 * rng.NextDouble();
        if (!anyFree)
            return;
        var pc = paint?.Invoke((ua + ub) / 2, (va + vb) / 2, b * 64 + a) ?? col;
        var fx = Fx(damage, dseed, W((ua + ub) / 2, (va + vb) / 2, 0));
        if (fx.Gone)
            return;
        var top = DesignMesh.Tone(pc, tone * fx.Tone);
        var side = DesignMesh.Tone(pc, tone * 0.72 * fx.Tone);
        (double, double, double) T(double u, double v) => W(u, v, hgt);
        (double, double, double) S(double u, double v) => W(u, v, -CladSink);
        // Tops: runs across each row, a run carried along while the next row has the same.
        var done = new bool[nx, ny];
        for (var j = 0; j < ny; j++)
            for (var i = 0; i < nx; i++)
            {
                if (!free[i, j] || done[i, j])
                    continue;
                var i1 = i;
                while (i1 + 1 < nx && free[i1 + 1, j] && !done[i1 + 1, j])
                    i1++;
                var j1 = j;
                while (j1 + 1 < ny && Enumerable.Range(i, i1 - i + 1).All(k => free[k, j1 + 1] && !done[k, j1 + 1]) && (i == 0 || !free[i - 1, j1 + 1]) && (i1 == nx - 1 || !free[i1 + 1, j1 + 1]))
                    j1++;
                for (var x = i; x <= i1; x++)
                    for (var y = j; y <= j1; y++)
                        done[x, y] = true;
                m.Quad(T(U(i), V(j)), T(U(i1 + 1), V(j)), T(U(i1 + 1), V(j1 + 1)), T(U(i), V(j1 + 1)), top, false);
            }
        // Skirts on every sub-cell edge between plate and bare (or the cell's edge), the same winding as LayPlates.
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < ny; j++)
            {
                if (!free[i, j])
                    continue;
                if (j == 0 || !free[i, j - 1])
                    m.Quad(S(U(i), V(j)), S(U(i + 1), V(j)), T(U(i + 1), V(j)), T(U(i), V(j)), side, false);
                if (j == ny - 1 || !free[i, j + 1])
                    m.Quad(S(U(i), V(j + 1)), S(U(i + 1), V(j + 1)), T(U(i + 1), V(j + 1)), T(U(i), V(j + 1)), side, false);
                if (i == 0 || !free[i - 1, j])
                    m.Quad(S(U(i), V(j)), S(U(i), V(j + 1)), T(U(i), V(j + 1)), T(U(i), V(j)), side, false);
                if (i == nx - 1 || !free[i + 1, j])
                    m.Quad(S(U(i + 1), V(j)), S(U(i + 1), V(j + 1)), T(U(i + 1), V(j + 1)), T(U(i + 1), V(j)), side, false);
            }
    }

    /// <summary>
    /// Lays plates on a grid of cells over a surface P(u, v) (u across, v along, both 0..1; its outward normal Nrm, and W the world point
    /// off it by a distance): free cells merged at random into plates of one to four cells each way, each inset by a seam, raised by a varied
    /// step along the normal, its skirt sunk into the skin. Shared by bodies and connections; cells set in <paramref name="taken"/> stay bare.
    /// </summary>
    internal static void LayPlates(DesignMesh m, int nu, int nv, double arc, double len, Random rng, bool heavy, double depth, (byte, byte, byte) col,
        Func<double, double, (double X, double Y, double Z)> P, Func<double, double, (double X, double Y, double Z)> Nrm, Func<double, double, double, (double, double, double)> W, bool[,] taken,
        Func<double, double, int, (byte, byte, byte)>? paint = null, int maxAcross = 0, bool hex = false, double damage = 0, int dseed = 0, bool[]? brk = null, Func<double, double>? toU = null, double[]? knots = null, bool smooth = false)
    {
        if (hex)
        {
            LayHex(m, nu, nv, arc, len, rng, depth, col, Nrm, W, taken, paint, damage, dseed);
            return;
        }
        var floor = CladMin;
        var rise = HullBody.CladRise(heavy ? "Heavy" : "Plates") - floor;
        var seam = CladSeam;
        double du = seam / arc, dv = seam / len;
        var last = -1.0;
        for (var b = 0; b < nv; b++)
            for (var a = 0; a < nu; a++)
            {
                if (taken[a, b])
                    continue;
                int big = heavy ? 4 : 3, wu = 1 + rng.Next(big), wv = 1 + rng.Next(big);
                var hgt = floor + rise * rng.NextDouble();
                var tone = 0.88 + 0.18 * rng.NextDouble();
                var greeble = heavy && rng.NextDouble() < 0.35;
                var gr = (rng.NextDouble(), rng.NextDouble(), rng.NextDouble());
                if (smooth) // (a busy area smoothed: plates as big as they grow, one low height and tone, no greebles)
                {
                    (wu, wv, hgt, tone, greeble) = (4, 4, floor + rise * 0.2, 0.97, false);
                }
                if (maxAcross > 0)
                    wu = Math.Min(wu, maxAcross);
                // Grow across then along while the cells are free.
                var cu = 1;
                while (cu < wu && a + cu < nu && !taken[a + cu, b] && (brk == null || a + cu - 1 >= brk.Length || !brk[a + cu - 1])) // (never across a crease)
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
                    hgt += hgt + 0.005 <= floor + rise ? 0.005 : -0.005;
                last = hgt;
                hgt *= depth;
                hgt = Math.Max(hgt, 0.004 + 0.002);
                double pu0 = (double)a / nu + du, pu1 = (double)(a + cu) / nu - du, pv0 = (double)b / nv + dv, pv1 = (double)(b + cv) / nv - dv;
                if (pu1 <= pu0 || pv1 <= pv0)
                    continue;
                var pc = paint?.Invoke((pu0 + pu1) / 2, (pv0 + pv1) / 2, b * 64 + a) ?? col;
                var fx = Fx(damage, dseed, W((pu0 + pu1) / 2, (pv0 + pv1) / 2, 0));
                if (fx.Gone)
                {
                    Under(m, pu0 - du, pu1 + du, pv0 - dv, pv1 + dv, arc, len, W, pc, dseed);
                    continue;
                }
                var top = DesignMesh.Tone(pc, tone * fx.Tone);
                var side = DesignMesh.Tone(pc, tone * 0.72 * fx.Tone);
                // Damage: a lift along one edge (a tilted plate) and a dent pushing the top's middle in towards the skin.
                var hg0 = hgt;
                double H(double u, double v)
                {
                    var su = (u - pu0) / (pu1 - pu0);
                    var e = hg0 + fx.Lift * su;
                    if (fx.Dent > 0)
                        e -= fx.Dent * Math.Sin(Math.PI * Math.Clamp(su, 0, 1)) * Math.Sin(Math.PI * Math.Clamp((v - pv0) / (pv1 - pv0), 0, 1));
                    return Math.Max(e, 0.006);
                }
                // Where the top bends with the skin across: at even steps, and at every profile point inside the plate (Karl, 2026-11-27: a
                // plate laid flat over a point of the profile let the skin poke through its middle, worst on a tapering bow).
                var us = Stops(pu0, pu1, cu * (fx.Dent > 0 ? 2 : 1), toU, knots);
                // The top follows the skin's curve: one quad a cell (a dented plate four).
                var sub = fx.Dent > 0 ? 2 : 1;
                int tu = us.Length - 1, tv = cv * sub;
                double U(int k) => us[k];
                double V(int k) => pv0 + (pv1 - pv0) * k / tv;
                (double, double, double) T(double u, double v) => W(u, v, H(u, v));
                for (var x = 0; x < tu; x++)
                    for (var y = 0; y < tv; y++)
                        m.Quad(T(U(x), V(y)), T(U(x + 1), V(y)), T(U(x + 1), V(y + 1)), T(U(x), V(y + 1)), top, false);
                // The skirt, sunk into the hull.
                for (var x = 0; x < tu; x++)
                {
                    m.Quad(W(U(x), pv0, -CladSink), W(U(x + 1), pv0, -CladSink), T(U(x + 1), pv0), T(U(x), pv0), side, false);
                    m.Quad(W(U(x), pv1, -CladSink), W(U(x + 1), pv1, -CladSink), T(U(x + 1), pv1), T(U(x), pv1), side, false);
                }
                for (var y = 0; y < tv; y++)
                {
                    m.Quad(W(pu0, V(y), -CladSink), W(pu0, V(y + 1), -CladSink), T(pu0, V(y + 1)), T(pu0, V(y)), side, false);
                    m.Quad(W(pu1, V(y), -CladSink), W(pu1, V(y + 1), -CladSink), T(pu1, V(y + 1)), T(pu1, V(y)), side, false);
                }
                // Abrasion (vertex colour only): a worn plate's edges scuffed and a few scratches across its top, both lightened toward bare
                // metal and lifted 3 mm off it; how worn each plate is varies with the damage seed, so worn plates sit by less worn ones.
                var wear = damage <= 0 ? 0 : Math.Clamp(damage * (0.3 + 1.4 * DHash(dseed, W((pu0 + pu1) / 2, (pv0 + pv1) / 2, 0), 31)), 0, 1);
                if (wear > 0.12)
                {
                    var bare = Bare(top, wear);
                    (double, double, double) L(double u, double v) => W(u, v, H(u, v) + 0.003);
                    double eu = Math.Min(0.03 / arc, (pu1 - pu0) / 6), ev = Math.Min(0.03 / len, (pv1 - pv0) / 6);
                    m.Quad(L(pu0, pv0), L(pu1, pv0), L(pu1, pv0 + ev), L(pu0, pv0 + ev), bare, false);
                    m.Quad(L(pu0, pv1 - ev), L(pu1, pv1 - ev), L(pu1, pv1), L(pu0, pv1), bare, false);
                    m.Quad(L(pu0, pv0 + ev), L(pu0 + eu, pv0 + ev), L(pu0 + eu, pv1 - ev), L(pu0, pv1 - ev), bare, false);
                    m.Quad(L(pu1 - eu, pv0 + ev), L(pu1, pv0 + ev), L(pu1, pv1 - ev), L(pu1 - eu, pv1 - ev), bare, false);
                    var w0 = W((pu0 + pu1) / 2, (pv0 + pv1) / 2, 0);
                    var scratches = (int)(wear * 3.5);
                    for (var k = 0; k < scratches; k++)
                    {
                        // A thin streak (1 cm wide) slanting along the plate, inside its edges.
                        double ua = pu0 + eu + (pu1 - pu0 - 2 * eu) * DHash(dseed, w0, 40 + k), ub = pu0 + eu + (pu1 - pu0 - 2 * eu) * DHash(dseed, w0, 50 + k);
                        double va = pv0 + ev + (pv1 - pv0 - 2 * ev) * 0.1, vb = pv1 - ev - (pv1 - pv0 - 2 * ev) * 0.1;
                        var hw = 0.005 / arc;
                        m.Quad(L(ua - hw, va), L(ua + hw, va), L(ub + hw, vb), L(ub - hw, vb), Bare(top, wear * 0.8), false);
                    }
                }
                if (greeble)
                {
                    double pw = (pu1 - pu0) * arc, pl = (pv1 - pv0) * len;
                    if (pw < 0.4 || pl < 0.4)
                        continue;
                    // A small box on the plate, its own axes the skin's there (across, out, along).
                    double gu = pu0 + (pu1 - pu0) * (0.25 + 0.5 * gr.Item1), gv = pv0 + (pv1 - pv0) * (0.25 + 0.5 * gr.Item2);
                    var o = W(gu, gv, H(gu, gv));
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

    /// <summary>
    /// The steps across a plate from a to b (grid shares): n even pieces, and the share of every profile point inside it, found through
    /// <paramref name="toU"/> (grid share to the profile's share, rising) against <paramref name="knots"/> (the profile points' shares), so
    /// a plate's top and skirts bend where the skin does. Without them, the n pieces.
    /// </summary>
    private static double[] Stops(double a, double b, int n, Func<double, double>? toU, double[]? knots)
    {
        n = Math.Max(1, n);
        var list = new List<double>(n + 4);
        for (var k = 0; k <= n; k++)
            list.Add(a + (b - a) * k / n);
        if (toU is not null && knots is not null && b > a)
        {
            double ua = toU(a), ub = toU(b);
            foreach (var kn in knots)
            {
                if (kn <= ua + 1e-6 || kn >= ub - 1e-6)
                    continue;
                double lo = a, hi = b;
                for (var it = 0; it < 40; it++)
                {
                    var mid = (lo + hi) / 2;
                    if (toU(mid) < kn) lo = mid; else hi = mid;
                }
                var t = (lo + hi) / 2;
                if (list.TrueForAll(q => Math.Abs(q - t) > (b - a) * 1e-3))
                    list.Add(t);
            }
            list.Sort();
        }
        return [.. list];
    }

    /// <summary>The primer under a missing plate (Karl, 2026-11-25): dark and matte, no paint pattern.</summary>
    private static readonly (byte, byte, byte) Primer = (0x3a, 0x3d, 0x3c), FrameCol = (0x4e, 0x50, 0x52), Char = (0x16, 0x14, 0x12);

    /// <summary>
    /// What shows where a plate is gone (Karl, 2026-11-25), over the cell rectangle u0..u1, v0..v1 (shares; arc and len its metres): the
    /// inner skin (matte primer, 4 mm off the hull), the frame (ribs along the seams, 2.5 cm up, and a bolt stub at each corner), a scorch
    /// ring just inside the edge (7 mm up), and one or two torn plate edges bent up off the seam. Only built for missing plates, so an
    /// intact hull costs nothing; about 40 triangles a gap. Heights all differ by at least 3 mm, so no faces share a plane.
    /// </summary>
    internal static void Under(DesignMesh m, double u0, double u1, double v0, double v1, double arc, double len,
        Func<double, double, double, (double, double, double)> W, (byte, byte, byte) paint, int dseed)
    {
        if (u1 <= u0 || v1 <= v0)
            return;
        m.Quad(W(u0, v0, 0.004), W(u1, v0, 0.004), W(u1, v1, 0.004), W(u0, v1, 0.004), Primer, false);
        // Scorch ring: a charred band round the inside of the gap.
        double su = Math.Min(0.12 / arc, (u1 - u0) / 4), sv = Math.Min(0.12 / len, (v1 - v0) / 4);
        const double hs = 0.007;
        m.Quad(W(u0, v0, hs), W(u1, v0, hs), W(u1 - su, v0 + sv, hs), W(u0 + su, v0 + sv, hs), Char, false);
        m.Quad(W(u1, v0, hs), W(u1, v1, hs), W(u1 - su, v1 - sv, hs), W(u1 - su, v0 + sv, hs), Char, false);
        m.Quad(W(u1, v1, hs), W(u0, v1, hs), W(u0 + su, v1 - sv, hs), W(u1 - su, v1 - sv, hs), Char, false);
        m.Quad(W(u0, v1, hs), W(u0, v0, hs), W(u0 + su, v0 + sv, hs), W(u0 + su, v1 - sv, hs), Char, false);
        // Ribs (along v, at the u seams) and stringers (along u, at the v seams): a top and two sides each, 3 cm wide.
        double ru = 0.015 / arc, rv = 0.015 / len;
        const double hr = 0.025;
        void Rib(double ua, double ub, double va, double vb)
        {
            m.Quad(W(ua, va, hr), W(ub, va, hr), W(ub, vb, hr), W(ua, vb, hr), FrameCol, false);
            m.Quad(W(ua, va, -0.02), W(ua, va, hr), W(ua, vb, hr), W(ua, vb, -0.02), DesignMesh.Tone(FrameCol, 0.75), false);
            m.Quad(W(ub, vb, -0.02), W(ub, vb, hr), W(ub, va, hr), W(ub, va, -0.02), DesignMesh.Tone(FrameCol, 0.75), false);
        }
        Rib(u0, u0 + ru, v0, v1);
        Rib(u1 - ru, u1, v0, v1);
        Rib(u0 + ru, u1 - ru, v0, v0 + rv);
        Rib(u0 + ru, u1 - ru, v1 - rv, v1);
        // Bolt stubs: a little pyramid on each corner bracket, 5 cm up.
        double bu = 0.025 / arc, bv = 0.025 / len;
        foreach (var (cu, cv) in new[] { (u0 + ru, v0 + rv), (u1 - ru, v0 + rv), (u1 - ru, v1 - rv), (u0 + ru, v1 - rv) })
        {
            var tip = W(cu, cv, 0.05);
            (double, double, double) B(double a, double b) => W(cu + a * bu, cv + b * bv, hr + 0.003);
            var bc = DesignMesh.Tone(FrameCol, 1.15);
            m.Tri(B(-1, -1), B(1, -1), tip, bc, false);
            m.Tri(B(1, -1), B(1, 1), tip, bc, false);
            m.Tri(B(1, 1), B(-1, 1), tip, DesignMesh.Tone(bc, 0.8), false);
            m.Tri(B(-1, 1), B(-1, -1), tip, DesignMesh.Tone(bc, 0.8), false);
        }
        // Torn edges: one or two ragged strips of the old plate still on a seam, bent up and out.
        var mid = W((u0 + u1) / 2, (v0 + v1) / 2, 0);
        var torn = DesignMesh.Tone(paint, 0.55);
        var count = 1 + (DHash(dseed, mid, 70) < 0.5 ? 1 : 0);
        for (var k = 0; k < count; k++)
        {
            var edge = (int)(DHash(dseed, mid, 71 + k) * 4) & 3;
            double a = 0.15 + 0.3 * DHash(dseed, mid, 75 + k), b = a + 0.25 + 0.3 * DHash(dseed, mid, 79 + k);
            b = Math.Min(b, 0.95);
            var rise = 0.06 + 0.1 * DHash(dseed, mid, 83 + k);
            var inward = 0.08 + 0.08 * DHash(dseed, mid, 87 + k);
            (double, double, double) Pt(double s, double off, double h) => edge switch
            {
                0 => W(u0 + off / arc, v0 + (v1 - v0) * s, h),
                1 => W(u1 - off / arc, v0 + (v1 - v0) * s, h),
                2 => W(u0 + (u1 - u0) * s, v0 + off / len, h),
                _ => W(u0 + (u1 - u0) * s, v1 - off / len, h),
            };
            // The bent lip: from the seam (on the frame) up to a jagged tip line partway in.
            var r0 = Pt(a, 0.005, hr + 0.006);
            var r1 = Pt(b, 0.005, hr + 0.006);
            var t1 = Pt(b - 0.05, inward * 0.7, hr + rise * 0.8);
            var tm = Pt((a + b) / 2, inward, hr + rise);
            var t0 = Pt(a + 0.04, inward * 0.8, hr + rise * 0.6);
            m.Quad(r0, r1, t1, t0, torn, false);
            m.Tri(t0, t1, tm, torn, false);
        }
    }

    /// <summary>A colour lightened toward bare metal by a plate's wear (0..1).</summary>
    private static (byte, byte, byte) Bare((byte, byte, byte) c, double wear)
    {
        var f = 0.35 + 0.4 * wear;
        static byte Mix(byte a, int b, double f) => (byte)Math.Clamp(a + (b - a) * f, 0, 255);
        return (Mix(c.Item1, 0xd6, f), Mix(c.Item2, 0xda, f), Mix(c.Item3, 0xde, f));
    }

    /// <summary>A share in 0..1 from a seed and a point (to the centimetre), so the same place always takes the same damage.</summary>
    internal static double DHash(int seed, (double X, double Y, double Z) p, int k)
    {
        unchecked
        {
            var x = (long)Math.Round(p.X * 100) * 73856093L ^ (long)Math.Round(p.Y * 100) * 19349663L ^ (long)Math.Round(p.Z * 100) * 83492791L ^ (seed + 1) * 2654435761L ^ k * 97531L;
            x ^= x >> 33; x *= -49064778989728563L; x ^= x >> 33; x *= -4265267296055464877L; x ^= x >> 33;
            return (x & 0xFFFFFF) / (double)0x1000000;
        }
    }

    /// <summary>Scorch and grime (colour only): a factor for a point, patches about 2.5 m across darkening as damage rises.</summary>
    internal static double Scorch(double damage, int seed, (double X, double Y, double Z) p)
    {
        if (damage <= 0)
            return 1;
        var cell = (Math.Floor(p.X / 2.5), Math.Floor(p.Y / 2.5), Math.Floor(p.Z / 2.5));
        var hs = DHash(seed, cell, 11);
        return hs < damage * 0.45 ? 0.42 + 0.3 * DHash(seed, cell, 12) : 1;
    }

    /// <summary>What damage does to one plate or tile, from its middle: gone, lifted along one edge, dented, and its tone.</summary>
    internal static (bool Gone, double Lift, double Dent, double Tone) Fx(double damage, int seed, (double, double, double) p)
    {
        if (damage <= 0)
            return (false, 0, 0, 1);
        var gone = DHash(seed, p, 1) < damage * 0.35;
        var lift = DHash(seed, p, 2) < damage * 0.3 ? 0.02 + 0.04 * DHash(seed, p, 3) : 0;
        var dent = DHash(seed, p, 4) < damage * 0.3 ? 0.01 + 0.03 * DHash(seed, p, 5) : 0;
        return (gone, lift, dent, Scorch(damage, seed, p));
    }

    /// <summary>
    /// Hex cladding: a honeycomb of raised flat-topped hexagons over the grid's surface, about a cell across the flats, each inset by
    /// a seam, clipped at the grid's edges (a skirt sunk into the skin on every edge), its flat top on the skin's offset at each corner.
    /// The plates' rise, depth, paint and stagger; a tile any point of which lands in a taken cell (an opening) stays bare.
    /// </summary>
    private static void LayHex(DesignMesh m, int nu, int nv, double arc, double len, Random rng, double depth, (byte, byte, byte) col,
        Func<double, double, (double X, double Y, double Z)> Nrm, Func<double, double, double, (double, double, double)> W, bool[,] taken,
        Func<double, double, int, (byte, byte, byte)>? paint, double damage, int dseed)
    {
        var rise = 0.08 - CladMin;
        var cs = arc / nu;
        cs = Math.Min(cs, Math.Max(len / nv, 0.5 * cs));
        cs = Math.Max(cs, 0.1);
        var R = cs / Math.Sqrt(3);
        double dx = 1.5 * R, dy = Math.Sqrt(3) * R;
        var ri = R - 2 * CladSeam / Math.Sqrt(3);
        if (ri < 0.03)
            return;
        double e = CladSeam, x0 = e, x1 = arc - e, y0 = e, y1 = len - e;
        if (x1 <= x0 || y1 <= y0)
            return;
        var full = 1.5 * Math.Sqrt(3) * ri * ri;
        var last = -1.0;
        var poly = new List<(double X, double Y)>(12);
        var tmp = new List<(double X, double Y)>(12);
        var topV = new (double, double, double)[16];
        var botV = new (double, double, double)[16];
        int ncol = (int)Math.Ceiling(arc / dx) + 1, nrow = (int)Math.Ceiling(len / dy) + 1;
        for (var c = 0; c <= ncol; c++)
            for (var r = -1; r <= nrow; r++)
            {
                double cxm = c * dx, cym = (r + ((c & 1) == 1 ? 0.5 : 0)) * dy;
                poly.Clear();
                for (var k = 0; k < 6; k++)
                    poly.Add((cxm + ri * Math.Cos(Math.PI / 3 * k), cym + ri * Math.Sin(Math.PI / 3 * k)));
                Clip(poly, tmp, 0, x0, true); Clip(poly, tmp, 0, x1, false); Clip(poly, tmp, 1, y0, true); Clip(poly, tmp, 1, y1, false);
                if (poly.Count < 3)
                    continue;
                double area = 0, gx = 0, gy = 0;
                for (var k = 0; k < poly.Count; k++)
                {
                    var (p, q) = (poly[k], poly[(k + 1) % poly.Count]);
                    area += p.X * q.Y - q.X * p.Y;
                    gx += p.X; gy += p.Y;
                }
                area = Math.Abs(area) / 2;
                if (area < 0.12 * full)
                    continue;
                gx /= poly.Count; gy /= poly.Count;
                int Cu(double x) => Math.Clamp((int)(x / arc * nu), 0, nu - 1);
                int Cv(double y) => Math.Clamp((int)(y / len * nv), 0, nv - 1);
                var blocked = taken[Cu(gx), Cv(gy)];
                foreach (var p in poly)
                    blocked |= taken[Cu(p.X), Cv(p.Y)];
                var hgt = CladMin + rise * rng.NextDouble();
                var tone = 0.88 + 0.18 * rng.NextDouble();
                if (blocked)
                    continue;
                hgt = Math.Round(hgt / 0.005) * 0.005;
                if (Math.Abs(hgt - last) < 0.005)
                    hgt += hgt + 0.005 <= CladMin + rise ? 0.005 : -0.005;
                last = hgt;
                hgt = Math.Max(hgt * depth, 0.006);
                double uc = gx / arc, vc = gy / len;
                var fx = Fx(damage, dseed, W(uc, vc, 0));
                var pc = paint?.Invoke(uc, vc, Cv(gy) * 64 + Cu(gx)) ?? col;
                if (fx.Gone)
                {
                    // The tile's middle as a box (inside its outline, so the frame stays clear of the tiles round it).
                    double hx = (poly.Max(q => q.X) - poly.Min(q => q.X)) * 0.36, hy = (poly.Max(q => q.Y) - poly.Min(q => q.Y)) * 0.36;
                    Under(m, (gx - hx) / arc, (gx + hx) / arc, (gy - hy) / len, (gy + hy) / len, arc, len, W, pc, dseed);
                    continue;
                }
                var top = DesignMesh.Tone(pc, tone * fx.Tone);
                var side = DesignMesh.Tone(pc, tone * 0.72 * fx.Tone);
                // Cheap tiles (2026-11-25: a ringed, edge-cut tile cost ~60 triangles and the body's cap left most hulls bare): a flat top
                // over the clipped outline (a fan, 4 triangles for a whole hexagon) and one skirt quad an edge, each corner sampled once.
                // A dent darkens the tile instead of bending it; a lift tilts it along the arc.
                var lx = poly.Min(q => q.X);
                var wx = Math.Max(1e-6, poly.Max(q => q.X) - lx);
                if (fx.Dent > 0)
                    top = DesignMesh.Tone(top, 0.85);
                var nc = poly.Count;
                if (topV.Length < nc)
                {
                    topV = new (double, double, double)[nc * 2];
                    botV = new (double, double, double)[nc * 2];
                }
                for (var k = 0; k < nc; k++)
                {
                    var (px, py) = poly[k];
                    double u = px / arc, v = py / len;
                    topV[k] = W(u, v, Math.Max(hgt + fx.Lift * (px - lx) / wx, 0.006));
                    botV[k] = W(u, v, -CladSink);
                }
                for (var k = 1; k < nc - 1; k++)
                    m.Tri(topV[0], topV[k], topV[k + 1], top, false);
                for (var k = 0; k < nc; k++)
                {
                    var k1 = (k + 1) % nc;
                    m.Quad(botV[k], botV[k1], topV[k1], topV[k], side, false);
                }
            }
    }

    /// <summary>Clips a convex polygon to one side of an axis line (Sutherland-Hodgman), in place.</summary>
    private static void Clip(List<(double X, double Y)> poly, List<(double X, double Y)> tmp, int axis, double at, bool keepAbove)
    {
        if (poly.Count == 0)
            return;
        tmp.Clear();
        double Of((double X, double Y) p) => axis == 0 ? p.X : p.Y;
        bool In((double X, double Y) p) => keepAbove ? Of(p) >= at : Of(p) <= at;
        for (var k = 0; k < poly.Count; k++)
        {
            var (p, q) = (poly[k], poly[(k + 1) % poly.Count]);
            bool ip = In(p), iq = In(q);
            if (ip)
                tmp.Add(p);
            if (ip != iq)
            {
                var f = (at - Of(p)) / (Of(q) - Of(p));
                tmp.Add((p.X + (q.X - p.X) * f, p.Y + (q.Y - p.Y) * f));
            }
        }
        poly.Clear();
        poly.AddRange(tmp);
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

    /// <summary>The profile's turn at point k (degrees between the edges in and out of it); 0 where an edge has no length.</summary>
    private static double Turn(double[] hs, double[] ys, int k)
    {
        double ax = hs[k] - hs[k - 1], ay = ys[k] - ys[k - 1], bx = hs[k + 1] - hs[k], by = ys[k + 1] - ys[k];
        double la = Math.Sqrt(ax * ax + ay * ay), lb = Math.Sqrt(bx * bx + by * by);
        if (la < 1e-6 || lb < 1e-6)
            return 0;
        return Math.Acos(Math.Clamp((ax * bx + ay * by) / (la * lb), -1, 1)) * 180 / Math.PI;
    }

    /// <summary>A profile corner past this many degrees is a crease: the cladding's columns break on it.</summary>
    private const double CreaseAngle = 12;

    /// <summary>The point a share u of the way round a half profile whose point k sits at share knot[k] (the same at both ends of a band).</summary>
    private static (double H, double Y) AtKnot(double[] hs, double[] ys, double[] knot, int n, double u)
    {
        u = Math.Clamp(u, 0, 1);
        for (var k = 0; k < n - 1; k++)
            if (u <= knot[k + 1] || k == n - 2)
            {
                var seg = knot[k + 1] - knot[k];
                var f = seg < 1e-12 ? 0 : Math.Clamp((u - knot[k]) / seg, 0, 1);
                return (hs[k] + (hs[k + 1] - hs[k]) * f, ys[k] + (ys[k + 1] - ys[k]) * f);
            }
        return (hs[0], ys[0]);
    }

    // ---- room shells (Karl, 2026-11-24: in place of partition walls)

    /// <summary>The gap between a room's box and its shell's outer faces, so two rooms' shells sit back to back 8 mm apart, never on one plane.</summary>
    public const double ShellGap = 0.004;

    // ---- Doorways (Check.Doors): each a point, the middle of its foot (world frame), [x, y, z], or [x, y, z, half, top, hole] -------------
    // half: its clearance either side (0.55 m, a 0.9 m door's); top: how far up from its foot (2.1 m); hole: 1 for a hole through a floor and
    // the ceiling under it (a shaft's, a boarding hatch's, a cargo hatch's), 0 for a door in a wall (walkable rooms, step 4: a placed door's
    // W and H, a socket's opening into its room).
    public const double DoorHalfDefault = 0.55, DoorTopDefault = 2.1;

    /// <summary>How near a room's floor a door's foot must be to be in its walls (m); a hole's, its floor or its ceiling (plus the deck between).</summary>
    public const double DoorLevel = 0.1, HoleLevel = 0.4;

    public static double[] DoorAt(double[] p, double half = DoorHalfDefault, double top = DoorTopDefault, bool hole = false) =>
        [p[0], p[1], p[2], half, top, hole ? 1 : 0];

    public static double DoorHalf(double[] q) => q.Length > 3 ? q[3] : DoorHalfDefault;

    public static double DoorTop(double[] q) => q.Length > 4 ? q[4] : DoorTopDefault;

    public static bool IsHole(double[] q) => q.Length > 5 && q[5] > 0.5 && q[5] < 1.5; // (2: a window's opening, DesignWindows.cs)

    /// <summary>
    /// The doorways that cut room r: a door whose foot is at its floor (on its plan, near a wall: a wall cell is cut only where it lies
    /// within the door's half), a hole whose foot is at its floor or just over its ceiling, over its plan.
    /// </summary>
    public static List<double[]> DoorsOf(ShipDesign d, Room r, List<double[]> doors)
    {
        var b = d.Box(r);
        int al = d.Stacked ? 1 : 2, up = d.Stacked ? 2 : 1;
        double fl = d.Stacked ? b[5] : b[2], ce = d.Stacked ? b[4] : b[3];
        double a0 = b[0], a1 = b[1], l0 = d.Stacked ? b[2] : b[4], l1 = d.Stacked ? b[3] : b[5];
        var o = new List<double[]>();
        foreach (var q in doors)
        {
            const double on = 0.05; // (a door's foot is on the room's wall, a hole's inside its plan)
            if (q[0] < a0 - on || q[0] > a1 + on || q[al] < l0 - on || q[al] > l1 + on)
                continue;
            if (IsWindow(q) ? (d.Stacked ? fl - q[up] : q[up] - fl) is var sill && sill > -DoorLevel && sill < Math.Abs(ce - fl) - 0.1
                : IsHole(q) ? Math.Abs(q[up] - fl) < DoorLevel || Math.Abs(q[up] - ce) < HoleLevel : Math.Abs(q[up] - fl) < DoorLevel)
                o.Add(q);
        }
        return o;
    }

    /// <summary>The most triangles one room's wall cladding and floor dressing may add together; past it the rest is left plain.</summary>
    public const int MaxShellDressTris = 9000;
    const double CorridorFrame = 1.35; // the Corridor lining's frame spacing, m

    /// <summary>How far in from its box a room's shell reaches (0 without one): the interior cladding starts there.</summary>
    public static double ShellDepth(Room r)
    {
        var t = Math.Clamp(r.WallThick, 0, Room.WallThickMax);
        return t < 0.005 ? 0 : ShellGap + t;
    }

    /// <summary>A room shell's colour in the game when its wall colour is its kind's and it has no interior cladding: bare steel.</summary>
    public const string SteelWall = "#6c7176";

    /// <summary>The top of what lies on a room's floor (the page's 8 cm marker, or its shell's floor), from the box's floor.</summary>
    public static double FloorTop(Room r) => Math.Max(0.08, ShellDepth(r));

    /// <summary>
    /// A room's walls, its own (Karl, 2026-11-25): a shell WallThick thick inside its box, ShellGap in from the box's floor, ceiling and four
    /// sides, so two rooms side by side each have their own wall, back to back, and neither reaches into the other's space. Cut into cells
    /// about 30 cm square; a cell is left out where it is outside every hull (so the shell stops at the skin), on a wall open to a room of
    /// the same group, or in a door between rooms (0.55 m either side of it, up to 2.1 m). A room out to the hull follows the skin (its
    /// <see cref="HullShell"/>); a run-on end has no wall. Kept cells are merged into runs, each one box. With no interior cladding, its
    /// walls take their WallStyle's relief; and the floor its Floor's dressing where that is not the interior's plate layer.
    /// </summary>
    /// <param name="boxes">When given, every box of the shell's floor, ceiling and walls (world frame, before the clip to the skin) is added to it: the
    /// game's walkable room (DesignParts.Rooms.cs) collides as the very boxes the designer draws.</param>
    /// <param name="strips">When given, a room out to the hull adds each strip of its <see cref="HullShell"/> as its 8 corners (24 numbers: the inner
    /// face's four, then the outer's), a thin convex piece each.</param>
    /// <param name="neutral">The game's copy (Karl, 2026-11-27: the designer's room tint showed in game through the cladding's seams): a room
    /// whose wall colour is its kind's (none picked) is walled in steel, a dark tone of its panels' colour behind interior cladding.</param>
    public static DesignMesh RoomShell(ShipDesign d, Room r, List<double[]> allDoors, List<(double[] Lo, double[] Hi)>? boxes = null, List<double[]>? strips = null, bool neutral = false)
    {
        var m = new DesignMesh();
        m.World();
        var t = Math.Clamp(r.WallThick, 0, Room.WallThickMax);
        const double g = ShellGap;
        var b = d.Box(r);
        int ax = 0, al = d.Stacked ? 1 : 2, up = d.Stacked ? 2 : 1;
        var lo = new[] { b[0], b[2], b[4] };
        var hi = new[] { b[1], b[3], b[5] };
        // Only this room's doorways: a door in a wall at its floor, a hole through its floor or its ceiling (walkable rooms, step 4: a door's
        // clearance once cut a notch in both rooms' floors, and in the ceiling and walls of the room under it).
        var doors = DoorsOf(d, r, allDoors);
        doors.AddRange(RoomWindows(d, r)); // (its walls open behind its windows: DesignWindows.cs)
        var colour = r.WallColour.Length == 7 ? DesignMesh.Hex(r.WallColour) :
            neutral ? (r.Interior != "Off" ? DesignMesh.Tone(DesignMesh.Hex(r.PanelColour), 0.42) : DesignMesh.Hex(SteelWall)) :
            DesignMesh.Tone(DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour), 0.55);
        var hex = $"#{colour.Item1:x2}{colour.Item2:x2}{colour.Item3:x2}";
        var mates = d.Rooms.Where(o => o != r && r.Group != 0 && o.Group == r.Group).Select(d.Box).ToList();
        bool InMate(double[] p) => mates.Any(o => p[0] > o[0] && p[0] < o[1] && p[1] > o[2] && p[1] < o[3] && p[2] > o[4] && p[2] < o[5]);
        // probe: a point just beyond the wall's outer face (in a group-mate: the wall is open, left out).
        // flat: a cell of the floor or the ceiling (cut only by a hole through it), else of a wall (cut only by a door in it).
        // Its extensions (step 4, package J): a wall one opens its wall across its width, floor to ceiling; an up one opens its ceiling.
        var exts = t >= 0.005 && !r.ToHull ? r.Exts : [];
        bool InExt(double[] p, double[]? probe, bool flat)
        {
            foreach (var e in exts)
            {
                if (e.Side == "up")
                {
                    if (flat && Math.Abs(p[up] - (d.Stacked ? lo[up] : hi[up])) < Math.Abs(p[up] - (d.Stacked ? hi[up] : lo[up])))
                        return true; // (a cell of its ceiling)
                    continue;
                }
                if (flat || probe is null)
                    continue;
                var (s0, s1, _, _) = ExtSpan(r, e);
                var beyond = e.Side switch { "x0" => probe[ax] < r.X0, "x1" => probe[ax] > r.X1, "z0" => probe[al] < r.Z0, _ => probe[al] > r.Z1 };
                var s = e.Side is "x0" or "x1" ? p[al] : p[ax];
                if (beyond && s > s0 && s < s1)
                    return true;
            }
            return false;
        }
        var voids = PieceVoids(d); // (an open hangar or airlock is seen and passed through: no shell across it)
        bool Keep(double[] p, double[]? probe, bool hull, bool flat)
        {
            foreach (var v in voids)
                if (v.Holds(p))
                    return false;
            if (hull && !d.Hulls.Any(h => h.Depth(p[0], p[1], p[2]) > 0.01))
                return false;
            if (probe is not null && InMate(probe))
                return false;
            if (exts.Count > 0 && InExt(p, probe, flat))
                return false;
            foreach (var q in doors)
            {
                if (IsHole(q) != flat)
                    continue;
                var half = DoorHalf(q);
                var rise = d.Stacked ? q[up] - p[up] : p[up] - q[up];
                if (IsWindow(q))
                {
                    // A window's opening: from its sill to its head, along its wall, any wall cell up to its reach along the wall's normal.
                    int wn = (int)q[6], wa = wn == ax ? al : ax;
                    if (Math.Abs(p[wn] - q[wn]) < q[7] && Math.Abs(p[wa] - q[wa]) < half && rise > 0 && rise < DoorTop(q))
                        return false;
                    continue;
                }
                if (Math.Abs(p[ax] - q[ax]) < half && Math.Abs(p[al] - q[al]) < half && (flat || rise > -0.3 && rise < DoorTop(q)))
                    return false;
            }
            return true;
        }
        var shell = t >= 0.005;
        if (shell && r.ToHull)
            HullShell(d, r, t, colour, (p, flat) => Keep(p, p, false, flat), m, doors, strips);
        double[] V(double a, double b2, double u) { var v = new double[3]; v[ax] = a; v[al] = b2; v[up] = u; return v; }
        // A slab: its extent on each world axis, cut along two of them (i, j), the third (n) its thickness; out is the side its outer face is on.
        // A door's opening edges on one world axis (Karl, 2026-11-25: both rooms cut the very same opening, so the cells split at its
        // edges, not on each room's own 30 cm grid, which put each room's opening somewhere else along the shared wall).
        void DoorEdges(int k, List<double> e)
        {
            foreach (var q in doors)
                if (k == up)
                {
                    if (IsHole(q)) { }
                    else if (IsWindow(q)) { e.Add(q[up]); e.Add(d.Stacked ? q[up] - DoorTop(q) : q[up] + DoorTop(q)); }
                    else if (d.Stacked) { e.Add(q[up] - DoorTop(q)); e.Add(q[up] + 0.3); }
                    else { e.Add(q[up] - 0.3); e.Add(q[up] + DoorTop(q)); }
                }
                else if (!IsWindow(q) || (int)q[6] != k) { e.Add(q[k] - DoorHalf(q)); e.Add(q[k] + DoorHalf(q)); }
            foreach (var x in exts)
                if (x.Side != "up" && (k == ax) == (x.Side is "z0" or "z1"))
                {
                    var (s0, s1, _, _) = ExtSpan(r, x);
                    e.Add(s0);
                    e.Add(s1);
                }
        }
        List<double> Breaks(double a, double b, int k)
        {
            var n0 = Math.Clamp((int)Math.Ceiling((b - a) / 0.3), 1, 160);
            var e = new List<double>(n0 + 8);
            for (var q = 0; q <= n0; q++)
                e.Add(a + (b - a) * q / n0);
            var at = e.Count;
            DoorEdges(k, e);
            for (var q = e.Count - 1; q >= at; q--)
                if (e[q] <= a + 0.002 || e[q] >= b - 0.002)
                    e.RemoveAt(q);
            e.Sort();
            for (var q = e.Count - 1; q > 0; q--)
                if (e[q] - e[q - 1] < 0.002 && q != e.Count - 1)
                    e.RemoveAt(q);
            return e;
        }
        // A slab: its extent on each world axis, cut along two of them (i, j), the third (n) its thickness; out is the side its outer face is on.
        void Slab(double[] s0, double[] s1, int i, int j, int n, double out_)
        {
            var ei = Breaks(s0[i], s1[i], i);
            var ej = Breaks(s0[j], s1[j], j);
            int ni = ei.Count - 1, nj = ej.Count - 1;
            var p = new double[3];
            var pr = new double[3];
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
                        p[i] = (ei[bi] + ei[bi + 1]) / 2;
                        p[j] = (ej[bj] + ej[bj + 1]) / 2;
                        Array.Copy(p, pr, 3);
                        pr[n] = (out_ < 0 ? s0[n] : s1[n]) + out_ * 0.03;
                        keep = Keep(p, n == up ? null : pr, true, n == up);
                    }
                    if (keep && run < 0)
                        run = bi;
                    if (!keep && run >= 0)
                    {
                        Array.Copy(s0, c0, 3);
                        Array.Copy(s1, c1, 3);
                        c0[i] = ei[run];
                        c1[i] = ei[bi];
                        c0[j] = ej[bj];
                        c1[j] = ej[bj + 1];
                        m.Box(c0[0], c0[1], c0[2], c1[0], c1[1], c1[2], hex);
                        boxes?.Add(((double[])c0.Clone(), (double[])c1.Clone()));
                        run = -1;
                    }
                }
            }
        }
        // Its floor and ceiling ends (stacked plans: the floor is the box's top, as the interior's).
        double fl = d.Stacked ? hi[up] : lo[up], sg = d.Stacked ? -1.0 : 1.0, ce = d.Stacked ? lo[up] : hi[up];
        double Up(double u) => fl + sg * u; // a height up from the floor
        var hgt = hi[up] - lo[up];
        var noLo = r.ToHull && r.RunOn && !d.Stacked;
        var noHi = r.ToHull && r.RunAft && !d.Stacked;
        if (shell && !r.ToHull)
        {
            double a0 = lo[ax] + g, a1 = hi[ax] - g, l0 = lo[al] + g, l1 = hi[al] - g;
            double Mn(double x, double y) => Math.Min(x, y);
            double Mx(double x, double y) => Math.Max(x, y);
            // Floor and ceiling over the whole inside of the box.
            Slab(V(a0, l0, Mn(Up(g), Up(g + t))), V(a1, l1, Mx(Up(g), Up(g + t))), ax, al, up, -sg);
            Slab(V(a0, l0, Mn(Up(hgt - g), Up(hgt - g - t))), V(a1, l1, Mx(Up(hgt - g), Up(hgt - g - t))), ax, al, up, sg);
            // The side walls (3 mm in from the floor's and ceiling's ends, so no faces meet), then the ends between them (6 mm).
            double u0 = Mn(Up(g + 0.003), Up(hgt - g - 0.003)), u1 = Mx(Up(g + 0.003), Up(hgt - g - 0.003));
            double v0 = Mn(Up(g + 0.006), Up(hgt - g - 0.006)), v1 = Mx(Up(g + 0.006), Up(hgt - g - 0.006));
            Slab(V(a0 + 0.003, l0 + 0.003, u0), V(a0 + t, l1 - 0.003, u1), al, up, ax, -1);
            Slab(V(a1 - t, l0 + 0.003, u0), V(a1 - 0.003, l1 - 0.003, u1), al, up, ax, 1);
            Slab(V(a0 + 0.006, l0 + 0.006, v0), V(a1 - 0.006, l0 + t + 0.003, v1), ax, up, al, -1);
            Slab(V(a0 + 0.006, l1 - t - 0.003, v0), V(a1 - 0.006, l1 - 0.006, v1), ax, up, al, 1);
            foreach (var e in exts)
                ExtShell(d, r, e, t, colour, m, strips);
        }
        var budget = MaxShellDressTris;
        var sh = ShellDepth(r);
        // The inside: its walls' inner faces, its floor's top and its ceiling's underside, up from the floor.
        double ia0 = lo[ax] + (r.ToHull ? 0.06 : sh), ia1 = hi[ax] - (r.ToHull ? 0.06 : sh);
        double il0 = lo[al] + sh + (shell ? 0.003 : 0), il1 = hi[al] - sh - (shell ? 0.003 : 0);
        double fTop = FloorTop(r), cBot = hgt - sh;
        var rnd = new Random(unchecked(r.Id * 7919 + 101 + r.Seed * 31337));

        // ---- wall cladding: relief on the inner faces of the box walls (a room out to the hull keeps its plain shell).
        if (shell && !r.ToHull && r.Interior == "Off" && r.WallStyle != "Plain")
        {
            var dark = DesignMesh.Tone(colour, 0.62);
            var light = DesignMesh.Tone(colour, 1.18);
            string H((byte R, byte G, byte B) c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";
            string hDark = H(dark), hLight = H(light), hMid = H(DesignMesh.Tone(colour, 0.85));
            const string metal = "#8a9097", pipe = "#6f7a80", brass = "#b48a3c";
            // Each wall: its normal axis, the plane of its inner face, the way into the room, the axis along it and its run.
            var walls = new List<(int N, double F, double In, int W, double W0, double W1)>
            {
                (ax, ia0, 1, al, il0, il1), (ax, ia1, -1, al, il0, il1),
            };
            if (!noLo) walls.Add((al, il0, 1, ax, ia0, ia1));
            if (!noHi) walls.Add((al, il1, -1, ax, ia0, ia1));
            foreach (var (n, f, inw, w, w0, w1) in walls)
            {
                double[] At(double s, double hh, double dd) { var v = new double[3]; v[n] = f + inw * dd; v[w] = s; v[up] = Up(hh); return v; }
                // The doors through this wall, as openings in its (s, h) (Karl, 2026-11-25: cladding runs right up to them).
                var holes = DoorHoles(d, doors, n, f - inw * t / 2, w, fl, sg);
                var framed = DoorHoles(d, doors, n, f - inw * t / 2, w, fl, sg, framedOnly: true);
                // A box of relief, cut round the doors' openings: the pieces left beside, under and over each.
                bool Rel(double s0, double s1, double h0, double h1, double dd, string col)
                {
                    var any = false;
                    AroundHoles(s0, s1, h0, h1, holes, 0, (a, b2, c, e) => any |= Rel1(a, b2, c, e, dd, col));
                    return any;
                }
                // One box of relief, s along the wall, h up, d out of it (embedded 5 mm in the wall, so its back never meets the face).
                bool Rel1(double s0, double s1, double h0, double h1, double dd, string col)
                {
                    if (budget < 12 || s1 - s0 < 1e-3 || h1 - h0 < 1e-3)
                        return false;
                    s0 = Math.Max(s0, w0 + 0.004); s1 = Math.Min(s1, w1 - 0.004);
                    h0 = Math.Max(h0, fTop + 0.004); h1 = Math.Min(h1, cBot - 0.004);
                    if (s1 - s0 < 1e-3 || h1 - h0 < 1e-3)
                        return false;
                    var mid = At((s0 + s1) / 2, (h0 + h1) / 2, -t / 2);
                    var probe = At((s0 + s1) / 2, (h0 + h1) / 2, -t - g - 0.03);
                    if (!Keep(mid, probe, true, false))
                        return false;
                    var c0 = At(s0, h0, -0.005);
                    var c1 = At(s1, h1, dd);
                    m.Box(Math.Min(c0[0], c1[0]), Math.Min(c0[1], c1[1]), Math.Min(c0[2], c1[2]), Math.Max(c0[0], c1[0]), Math.Max(c0[1], c1[1]), Math.Max(c0[2], c1[2]), col);
                    budget -= 12;
                    return true;
                }
                // Long pieces in chunks of about 1.2 m, so a door cuts only its own.
                void Run(double s0, double s1, double h0, double h1, double dd, string col)
                {
                    var k = Math.Max(1, (int)Math.Round((s1 - s0) / 1.2));
                    for (var i = 0; i < k; i++)
                        Rel(s0 + (s1 - s0) * i / k + (i > 0 ? 0.002 : 0), s0 + (s1 - s0) * (i + 1) / k, h0, h1, dd, col);
                }
                double len = w1 - w0, top = cBot, bot = fTop;
                switch (r.WallStyle)
                {
                    case "Panels":
                    {
                        int ns = Math.Max(1, (int)Math.Round(len / 1.2)), nh = Math.Max(1, (int)Math.Round((top - bot) / 1.0));
                        for (var i = 0; i < ns; i++)
                            for (var k = 0; k < nh; k++)
                                Rel(w0 + len * i / ns + 0.012, w0 + len * (i + 1) / ns - 0.012, bot + (top - bot) * k / nh + 0.012, bot + (top - bot) * (k + 1) / nh - 0.012, 0.012,
                                    rnd.NextDouble() < 0.15 ? hMid : hLight);
                        break;
                    }
                    case "Ribbed":
                    {
                        var ns = Math.Max(1, (int)Math.Round(len / 0.5));
                        for (var i = 0; i <= ns; i++)
                        {
                            var s = w0 + len * i / ns;
                            Rel(s - 0.03, s + 0.03, bot, top, 0.045, hDark);
                        }
                        Run(w0, w1, bot, bot + 0.12, 0.03, hMid);
                        Run(w0, w1, top - 0.1, top, 0.03, hMid);
                        break;
                    }
                    case "Padded":
                    {
                        Run(w0, w1, bot, bot + 0.25, 0.02, hDark);
                        int ns = Math.Max(1, (int)Math.Round(len / 0.5)), nh = Math.Max(1, (int)Math.Round((top - bot - 0.25) / 0.5));
                        for (var i = 0; i < ns; i++)
                            for (var k = 0; k < nh; k++)
                            {
                                double s0 = w0 + len * i / ns + 0.02, s1 = w0 + len * (i + 1) / ns - 0.02;
                                double h0 = bot + 0.25 + (top - bot - 0.25) * k / nh + 0.02, h1 = bot + 0.25 + (top - bot - 0.25) * (k + 1) / nh - 0.02;
                                if (Rel(s0, s1, h0, h1, 0.03, hLight))
                                    Rel(s0 + 0.05, s1 - 0.05, h0 + 0.05, h1 - 0.05, 0.045, hLight);
                            }
                        break;
                    }
                    case "Grate":
                    {
                        var ns = Math.Max(1, (int)Math.Round(len / 0.6));
                        for (var i = 0; i <= ns; i++)
                        {
                            var s = w0 + len * i / ns;
                            Rel(s - 0.02, s + 0.02, bot, top, 0.06, metal);
                        }
                        for (var hh = bot + 0.1; hh < top - 0.05; hh += 0.1)
                            Run(w0, w1, hh - 0.006, hh + 0.006, 0.07, metal);
                        break;
                    }
                    case "Conduit":
                    {
                        foreach (var (hh, rr) in new[] { (bot + 0.35, 0.035), (bot + 0.48, 0.025), (top - 0.35, 0.05), (top - 0.2, 0.03) })
                            Run(w0, w1, hh - rr, hh + rr, rr * 2 + 0.02, pipe);
                        var ns = Math.Max(1, (int)Math.Round(len / 1.2));
                        for (var i = 0; i <= ns; i++)
                        {
                            var s = w0 + len * i / ns;
                            Rel(s - 0.025, s + 0.025, bot + 0.28, bot + 0.55, 0.1, hDark);
                            Rel(s - 0.025, s + 0.025, top - 0.42, top - 0.14, 0.13, hDark);
                        }
                        for (var i = 0; i < ns; i++)
                        {
                            var s = w0 + len * (i + 0.5) / ns;
                            if (rnd.NextDouble() < 0.6)
                            {
                                Rel(s - 0.025, s + 0.025, bot + 0.55, top - 0.42, 0.06, pipe);
                                Rel(s - 0.14, s + 0.14, bot + 1.1, bot + 1.4, 0.09, hDark);
                                Rel(s - 0.1, s + 0.1, bot + 1.15, bot + 1.35, 0.095, brass);
                            }
                        }
                        break;
                    }
                    case "Hex":
                    {
                        // Flat hex tiles 1 cm proud, faceted as two quads each, a row offset by half.
                        const double R = 0.17;
                        double dx = R * 1.5, dy = R * Math.Sqrt(3);
                        var cDark = DesignMesh.Tone(colour, 0.9);
                        var i = 0;
                        for (var s = w0 + R; s < w1 - R * 0.5; s += dx, i++)
                            for (var hh = bot + dy * (i % 2 == 0 ? 0.5 : 1); hh < top - dy * 0.4; hh += dy)
                            {
                                if (budget < 4)
                                    break;
                                if (!Keep(At(s, hh, -t / 2), At(s, hh, -t - g - 0.03), true, false) || holes.Any(o => s > o.S0 - R && s < o.S1 + R && hh > o.H0 - R && hh < o.H1 + R))
                                    continue;
                                const double rr = R * 0.9;
                                (double, double, double) Pt(double ang, double dd) { var v = At(s + rr * Math.Cos(ang), hh + rr * Math.Sin(ang), dd); return (v[0], v[1], v[2]); }
                                var c = rnd.NextDouble() < 0.2 ? cDark : light;
                                const double dd = 0.01;
                                var p = new (double, double, double)[6];
                                for (var k = 0; k < 6; k++)
                                    p[k] = Pt(Math.PI / 3 * k, dd);
                                m.Quad(p[0], p[1], p[2], p[3], c, false);
                                m.Quad(p[0], p[3], p[4], p[5], c, false);
                                budget -= 4; // the page draws both sides
                            }
                        break;
                    }
                    case "Corridor":
                    {
                        // A ship corridor (Karl, 2026-11-25): arch frames every ~1.35 m (met by the ceiling's below), recessed
                        // panels between, a kick plate, a cable tray under the ceiling and a light strip in each top corner.
                        var ns = Math.Max(1, (int)Math.Round(len / CorridorFrame));
                        Run(w0, w1, bot, bot + 0.16, 0.035, hDark);                       // kick plate
                        Run(w0, w1, bot + 0.163, bot + 0.185, 0.045, brass);               // its trim
                        for (var i = 0; i <= ns; i++)
                        {
                            var s = w0 + len * i / ns;
                            Rel(s - 0.07, s + 0.07, bot, top, 0.085, metal);              // the frame's leg
                            Rel(s - 0.035, s + 0.035, bot + 0.25, top - 0.55, 0.098, hDark); // its rib
                        }
                        for (var i = 0; i < ns; i++)
                        {
                            double s0 = w0 + len * i / ns + 0.075, s1 = w0 + len * (i + 1) / ns - 0.075;
                            // The recessed panel: a frame around a face set back from it.
                            Rel(s0, s1, bot + 0.19, top - 0.5, 0.012, hLight);
                            Rel(s0 + 0.003, s1 - 0.003, bot + 0.19, bot + 0.25, 0.034, hMid);
                            Rel(s0 + 0.003, s1 - 0.003, top - 0.56, top - 0.5, 0.034, hMid);
                            if (rnd.NextDouble() < 0.35)
                                Rel((s0 + s1) / 2 - 0.12, (s0 + s1) / 2 + 0.12, bot + 1.05, bot + 1.3, 0.02, hDark); // a service hatch
                        }
                        // The cable tray: a shelf, its lip and two cable runs on it.
                        Run(w0, w1, top - 0.42, top - 0.39, 0.16, metal);
                        Run(w0, w1, top - 0.387, top - 0.33, 0.165, metal);
                        Run(w0, w1, top - 0.385, top - 0.34, 0.09, pipe);
                        Run(w0, w1, top - 0.385, top - 0.355, 0.135, "#a0442c");
                        // The light strip where wall meets ceiling.
                        Run(w0, w1, top - 0.06, top - 0.024, 0.05, "#f2f6ff");
                        Run(w0, w1, top - 0.02, top, 0.06, hDark);
                        break;
                    }
                    case "Zero Day":
                    {
                        // The Zero Day's own lining (Karl, 2026-11-27, its corridor before the room cladding): a white wall, pale grey
                        // panels over a dark rail with a blue strip, dark lower panels, a kick plate, and a yellow strip under a white
                        // cornice. Its own palette whatever the room's colour; each layer stands at least 6 mm off the one under it.
                        const string zWhite = "#e9edf0", zUp = "#b3bcc3", zUpFace = "#c0c8ce", zRail = "#3a4148", zBlue = "#2f6cc0",
                            zLow = "#565f67", zLowFace = "#626b73", zKick = "#2a2e33", zYellow = "#e8c21a";
                        double rail = Math.Min(bot + 1.0, top - 0.6), lowTop = rail - 0.04;
                        Run(w0, w1, bot, top, 0.006, zWhite);                                  // the white wall
                        Run(w0, w1, bot, bot + 0.14, 0.03, zKick);                             // kick plate
                        Run(w0, w1, top - 0.1, top, 0.04, zWhite);                             // cornice
                        Run(w0, w1, top - 0.13, top - 0.1, 0.028, zYellow);                    // its yellow strip
                        Run(w0, w1, rail, rail + 0.1, 0.04, zRail);                            // the rail
                        Run(w0, w1, rail + 0.1, rail + 0.125, 0.032, zBlue);                   // its blue strip
                        var ns = Math.Max(1, (int)Math.Round(len / 1.25));
                        for (var i = 0; i < ns; i++)
                        {
                            double s0 = w0 + len * i / ns + 0.06, s1 = w0 + len * (i + 1) / ns - 0.06;
                            if (s1 - s0 < 0.2)
                                continue;
                            double u0 = rail + 0.19, u1 = top - 0.22;
                            if (u1 - u0 > 0.2 && Rel(s0, s1, u0, u1, 0.014, zUp))               // upper panel and its raised face
                                Rel(s0 + 0.035, s1 - 0.035, u0 + 0.035, u1 - 0.035, 0.022, zUpFace);
                            double l0 = bot + 0.2, l1 = lowTop - 0.06;
                            if (l1 - l0 > 0.2 && Rel(s0, s1, l0, l1, 0.014, zLow))              // lower panel and its face
                                Rel(s0 + 0.035, s1 - 0.035, l0 + 0.035, l1 - 0.035, 0.022, zLowFace);
                        }
                        break;
                    }
                    case "Industrial":
                    {
                        Run(w0, w1, bot, bot + 0.9, 0.025, hDark);
                        Run(w0, w1, bot + 0.9, bot + 0.98, 0.05, brass);
                        var ns = Math.Max(1, (int)Math.Round(len / 1.2));
                        for (var i = 0; i <= ns; i++)
                        {
                            var s = w0 + len * i / ns;
                            Rel(s - 0.06, s + 0.06, bot, top, 0.07, metal);
                        }
                        for (var i = 0; i < ns; i++)
                        {
                            double s0 = w0 + len * i / ns + 0.07, s1 = w0 + len * (i + 1) / ns - 0.07;
                            Rel(s0 + 0.01, s1 - 0.01, bot + 1.0, top - 0.05, 0.01, hMid);
                            if (rnd.NextDouble() < 0.4)
                                for (var k = 0; k < 4; k++)
                                    Rel(s0 + 0.15, s1 - 0.15, bot + 1.4 + k * 0.08, bot + 1.44 + k * 0.08, 0.025, hDark);
                        }
                        break;
                    }
                }
                // A frame lining each opening's sides and head through the wall, its architrave over the cladding's cut ends.
                DoorFrames(framed, w0, w1, bot, cBot, -t + 0.004, 0.052, (s0, s1, h0, h1, d0, d1, c) =>
                {
                    if (budget < 12) return;
                    var c0 = At(s0, h0, d0);
                    var c1 = At(s1, h1, d1);
                    m.Box(Math.Min(c0[0], c1[0]), Math.Min(c0[1], c1[1]), Math.Min(c0[2], c1[2]), Math.Max(c0[0], c1[0]), Math.Max(c0[1], c1[1]), Math.Max(c0[2], c1[2]), c);
                    budget -= 12;
                }, r.WallStyle == "Zero Day" ? "#3a4148" : metal, r.WallStyle == "Zero Day" ? "#2a2e33" : hDark);
            }
            // The Zero Day's lit ceiling panels: a row down the middle of its long way, 1.2 m panels in a dark surround, 2 cm under the ceiling.
            if (r.WallStyle == "Zero Day")
            {
                bool longAl = il1 - il0 >= ia1 - ia0;
                double l0 = longAl ? il0 : ia0, l1 = longAl ? il1 : ia1, c0 = longAl ? ia0 : il0, c1 = longAl ? ia1 : il1;
                double cm = (c0 + c1) / 2, hw = Math.Min(0.28, (c1 - c0) / 4);
                var ns = Math.Max(1, (int)Math.Round((l1 - l0 - 0.4) / 1.6));
                for (var i = 0; i < ns && budget >= 24; i++)
                {
                    double sa = l0 + 0.2 + (l1 - l0 - 0.4) * i / ns + 0.2, sb = l0 + 0.2 + (l1 - l0 - 0.4) * (i + 1) / ns - 0.2;
                    if (sb - sa < 0.3)
                        continue;
                    double[] P(double c, double sAt, double h) => longAl ? V(c, sAt, Up(h)) : V(sAt, c, Up(h));
                    void B(double[] p, double[] q, string col)
                    {
                        m.Box(Math.Min(p[0], q[0]), Math.Min(p[1], q[1]), Math.Min(p[2], q[2]), Math.Max(p[0], q[0]), Math.Max(p[1], q[1]), Math.Max(p[2], q[2]), col);
                        budget -= 12;
                    }
                    B(P(cm - hw - 0.04, sa - 0.04, cBot + 0.005), P(cm + hw + 0.04, sb + 0.04, cBot - 0.012), "#3a4148"); // surround
                    B(P(cm - hw, sa, cBot - 0.004), P(cm + hw, sb, cBot - 0.02), "#f6f9ff");                             // the lit panel
                }
            }
            // The corridor's arches close over the ceiling, across its narrow way, at the frames' spacing along its long way.
            if (r.WallStyle == "Corridor")
            {
                bool longAl = il1 - il0 >= ia1 - ia0;
                double l0 = longAl ? il0 : ia0, l1 = longAl ? il1 : ia1, c0 = longAl ? ia0 : il0, c1 = longAl ? ia1 : il1;
                var ns = Math.Max(1, (int)Math.Round((l1 - l0) / CorridorFrame));
                for (var i = 0; i <= ns && budget >= 24; i++)
                {
                    var s = l0 + (l1 - l0) * i / ns;
                    double[] P(double c, double sAt, double h) => longAl ? V(c, sAt, Up(h)) : V(sAt, c, Up(h));
                    void B(double[] p, double[] q, string col)
                    {
                        m.Box(Math.Min(p[0], q[0]), Math.Min(p[1], q[1]), Math.Min(p[2], q[2]), Math.Max(p[0], q[0]), Math.Max(p[1], q[1]), Math.Max(p[2], q[2]), col);
                        budget -= 12;
                    }
                    // Between the legs (4 mm short of their faces) and 5 mm into the ceiling; the rib a little lower.
                    double sa = Math.Max(s - 0.07, l0 + 0.004), sb = Math.Min(s + 0.07, l1 - 0.004);
                    B(P(c0 + 0.089, sa + 0.002, cBot + 0.005), P(c1 - 0.089, sb - 0.002, cBot - 0.085), metal);
                    B(P(c0 + 0.24, s - 0.033, cBot - 0.08), P(c1 - 0.24, s + 0.033, cBot - 0.103), hDark);
                }
            }
        }

        // ---- floor dressing (the interior's plate layer does Plates and Hex), on what lies on the floor, inside the walls.
        if (r.Floor is not ("Plates" or "Hex"))
            FloorDress(d, r, m, V, Up, ia0 + 0.004, ia1 - 0.004, il0 + 0.004, il1 - 0.004, fTop, ax, al, rnd, ref budget,
                [.. doors.Where(q => IsHole(q) && Math.Abs(q[up] - fl) < DoorLevel).Select(q => (q[al] - DoorHalf(q), q[al] + DoorHalf(q), q[ax] - DoorHalf(q), q[ax] + DoorHalf(q)))]);
        return m;
    }

    /// <summary>The doors through a wall whose middle plane is at plane on axis n, as openings in its (s along axis w, h up from the floor
    /// at fl, sg the way up): the shell's door clearance, 0.55 m either side and 0.3 m under to 2.1 m over the door's point.</summary>
    internal static List<(double S0, double S1, double H0, double H1)> DoorHoles(ShipDesign d, List<double[]> doors, int n, double plane, int w, double fl, double sg, bool framedOnly = false)
    {
        var up = d.Stacked ? 2 : 1;
        var o = new List<(double, double, double, double)>();
        foreach (var q in doors)
            if (framedOnly && !IsFramed(q))
                continue; // (a window without its inside frame: cut, but framed by nothing)
            else if (IsWindow(q))
            {
                // A window's opening (DesignWindows.cs): from its sill to its head, on the wall it was cut for.
                if ((int)q[6] == n && Math.Abs(plane - q[n]) < q[7])
                {
                    var hs = sg * (q[up] - fl);
                    o.Add((q[w] - DoorHalf(q), q[w] + DoorHalf(q), hs, hs + DoorTop(q)));
                }
            }
            else if (!IsHole(q) && Math.Abs(plane - q[n]) < 0.55 && Math.Abs(q[up] - fl) < DoorLevel)
            {
                var hc = sg * (q[up] - fl);
                o.Add((q[w] - DoorHalf(q), q[w] + DoorHalf(q), hc - 0.3, hc + DoorTop(q)));
            }
        return o;
    }

    /// <summary>A wall piece (s0..s1, h0..h1) less the openings from index i on: emit gets what is left beside, under and over each.</summary>
    internal static void AroundHoles(double s0, double s1, double h0, double h1, List<(double S0, double S1, double H0, double H1)> holes, int i,
        Action<double, double, double, double> emit)
    {
        if (s1 - s0 < 1e-3 || h1 - h0 < 1e-3)
            return;
        if (i >= holes.Count)
        {
            emit(s0, s1, h0, h1);
            return;
        }
        var o = holes[i];
        if (s1 <= o.S0 || s0 >= o.S1 || h1 <= o.H0 || h0 >= o.H1)
        {
            AroundHoles(s0, s1, h0, h1, holes, i + 1, emit);
            return;
        }
        AroundHoles(s0, Math.Min(s1, o.S0), h0, h1, holes, i + 1, emit);
        AroundHoles(Math.Max(s0, o.S1), s1, h0, h1, holes, i + 1, emit);
        double m0 = Math.Max(s0, o.S0), m1 = Math.Min(s1, o.S1);
        AroundHoles(m0, m1, h0, Math.Min(h1, o.H0), holes, i + 1, emit);
        AroundHoles(m0, m1, Math.Max(h0, o.H1), h1, holes, i + 1, emit);
    }

    /// <summary>
    /// Frames for a wall's openings (Karl, 2026-11-25): a lining 37 mm thick on each side and the head, from back (in the wall) to just
    /// short of front, and an architrave 12 cm wide over the cladding's cut ends, front proud of the wall face. Head and jambs differ in
    /// their faces by 3-4 mm where they overlap, so none are coplanar. box gets (s0, s1, h0, h1, d0, d1, colour).
    /// </summary>
    internal static void DoorFrames(List<(double S0, double S1, double H0, double H1)> holes, double w0, double w1, double bot, double top,
        double back, double front, Action<double, double, double, double, double, double, string> box, string lining, string trim)
    {
        back = Math.Min(back, -0.004);
        foreach (var o in holes)
        {
            var head = o.H1 < top - 0.06;
            double jt = head ? o.H1 - 0.003 : top - 0.004, ft = head ? Math.Min(o.H1 + 0.08, top - 0.004) : top - 0.004;
            // A window's opening stands on a sill (DesignWindows.cs): its jambs start there, and a sill lining and trim run under it.
            var sill = o.H0 > bot + 0.1;
            double jb = sill ? o.H0 + 0.003 : bot + 0.004, fb = sill ? Math.Max(o.H0 - 0.08, bot + 0.004) : bot + 0.004;
            if (o.S0 > w0 + 0.1 && o.S0 < w1)
            {
                box(o.S0 + 0.003, o.S0 + 0.04, jb, jt, back, front - 0.012, lining);
                box(o.S0 - 0.08, o.S0 + 0.043, fb, ft, -0.005, front, trim);
            }
            if (o.S1 < w1 - 0.1 && o.S1 > w0)
            {
                box(o.S1 - 0.04, o.S1 - 0.003, jb, jt, back, front - 0.012, lining);
                box(o.S1 - 0.043, o.S1 + 0.08, fb, ft, -0.005, front, trim);
            }
            if (sill)
            {
                double a = Math.Max(o.S0, w0 + 0.004), b = Math.Min(o.S1, w1 - 0.004);
                if (b - a > 0.1)
                {
                    box(a + 0.041, b - 0.041, o.H0 + 0.003, o.H0 + 0.04, back, front - 0.012, lining);
                    box(Math.Max(o.S0 - 0.083, w0 + 0.004), Math.Min(o.S1 + 0.083, w1 - 0.004), fb, o.H0 + 0.043, -0.005, front + 0.004, trim);
                }
            }
            if (head)
            {
                double a = Math.Max(o.S0, w0 + 0.004), b = Math.Min(o.S1, w1 - 0.004);
                if (b - a > 0.1)
                {
                    box(a + 0.041, b - 0.041, o.H1 - 0.04, o.H1 - 0.003, back, front - 0.012, lining);
                    box(Math.Max(o.S0 - 0.083, w0 + 0.004), Math.Min(o.S1 + 0.083, w1 - 0.004), o.H1 - 0.043, Math.Min(o.H1 + 0.083, top - 0.004), -0.005, front + 0.004, trim);
                }
            }
        }
    }

    // ---- Door leaves (walkable designs, step 4: the game makes each a door of its own, DesignParts.Doors.cs) -------------------------------

    /// <summary>
    /// The leaf of a door placed between rooms a and b (a <see cref="RoomDoor"/> of kind Door), in the doorway <paramref name="q"/> the shells
    /// cut for it (<see cref="DoorAt"/>, world frame): a slab 4 cm thick in the middle of their shared wall, filling the cut from the higher of
    /// their floors' tops to the cut's head, with a pull strip on each face. <paramref name="across"/> is the wall's normal axis (0 x, 2 z);
    /// it opens by sliding along the wall into it. Null for a stacked plan (its rooms' walls are not made so) or a cut too small.
    /// </summary>
    public static DesignMesh? RoomDoorLeaf(ShipDesign d, double[] q, Room a, Room b, out int across)
    {
        var n = Math.Abs(a.X1 - b.X0) < 0.02 || Math.Abs(b.X1 - a.X0) < 0.02 ? 0 : 2;
        across = n;
        if (d.Stacked || IsHole(q))
            return null;
        var along = 2 - n;
        double half = DoorHalf(q) - 0.02, bot = q[1] + Math.Max(FloorTop(a), FloorTop(b)) + 0.004, top = q[1] + DoorTop(q) - 0.004;
        if (half < 0.2 || top - bot < 0.5)
            return null;
        var m = new DesignMesh();
        m.World();
        void B(double n0, double n1, double l0, double l1, double y0, double y1, string col)
        {
            double[] lo = new double[3], hi = new double[3];
            (lo[n], hi[n], lo[along], hi[along], lo[1], hi[1]) = (q[n] + n0, q[n] + n1, q[along] + l0, q[along] + l1, y0, y1);
            m.Box(lo[0], lo[1], lo[2], hi[0], hi[1], hi[2], col);
        }
        B(-0.02, 0.02, -half, half, bot, top, PieceKinds.Armour);
        var mid = bot + Math.Min(1.0, (top - bot) / 2);
        B(0.02, 0.032, half - 0.16, half - 0.1, mid - 0.25, mid + 0.25, PieceKinds.Amber); // (a pull strip each face, near its leading edge)
        B(-0.032, -0.02, half - 0.16, half - 0.1, mid - 0.25, mid + 0.25, PieceKinds.Amber);
        return m;
    }

    /// <summary>
    /// A cover for the hole <paramref name="q"/> a socket cuts through room r's floor (walkable designs, step 4: a docking collar's well, which
    /// the walk would fall down): a plate in the hole a few millimetres under the floor's top, inside the cut, with a lifting bar across. The game
    /// makes it a door that slides aside under the floor (DesignParts.Doors.cs). Null when the hole isn't in r's floor or r has no shell.
    /// </summary>
    public static DesignMesh? HoleCover(ShipDesign d, Room r, double[] q)
    {
        var b = d.Box(r);
        if (d.Stacked || !IsHole(q) || Math.Abs(q[1] - b[2]) > DoorLevel || q[0] < b[0] || q[0] > b[1] || q[2] < b[4] || q[2] > b[5])
            return null;
        var half = DoorHalf(q) - 0.006;
        double top = b[2] + Math.Max(ShellDepth(r), 0.05) - 0.002, bot = Math.Min(b[2] + ShellGap + 0.004, top - 0.03);
        var m = new DesignMesh();
        m.World();
        m.Box(q[0] - half, bot, q[2] - half, q[0] + half, top, q[2] + half, PieceKinds.Armour);
        m.Box(q[0] - 0.04, top - 0.003, q[2] - half * 0.6, q[0] + 0.04, top + 0.005, q[2] + half * 0.6, PieceKinds.Amber); // (its bar, 3 mm proud of the floor)
        return m;
    }

    /// <summary>A room's floor in one of the dressings (Karl, 2026-11-25): its pieces in plan a (across) and l (along), u up from what it lies on.</summary>
    private static void FloorDress(ShipDesign d, Room r, DesignMesh m, Func<double, double, double, double[]> V, Func<double, double> Up,
        double a0, double a1, double l0, double l1, double fTop, int ax, int al, Random rnd, ref int budget,
        List<(double S0, double S1, double H0, double H1)> holes)
    {
        if (a1 - a0 < 0.05 || l1 - l0 < 0.05)
            return;
        var left = budget;
        // Each piece cut round the holes through the floor (holes: along l, across a; Karl, 2026-11-27: the dressing ran over a ladder's well).
        bool Piece(double x0, double x1, double y0, double y1, double u0, double u1, string col)
        {
            var any = false;
            AroundHoles(y0, y1, x0, x1, holes, 0, (b0, b1, c0, c1) => any |= Piece1(c0, c1, b0, b1, u0, u1, col));
            return any;
        }
        bool Piece1(double x0, double x1, double y0, double y1, double u0, double u1, string col)
        {
            x0 = Math.Max(x0, a0); x1 = Math.Min(x1, a1); y0 = Math.Max(y0, l0); y1 = Math.Min(y1, l1);
            if (left < 12 || x1 - x0 < 1e-3 || y1 - y0 < 1e-3)
                return false;
            var mid = V((x0 + x1) / 2, (y0 + y1) / 2, Up(fTop + 0.02));
            if (!d.Hulls.Any(h => h.Depth(mid[0], mid[1], mid[2]) > 0.01))
                return false;
            var c0 = V(x0, y0, Up(fTop + u0));
            var c1 = V(x1, y1, Up(fTop + u1));
            m.Box(Math.Min(c0[0], c1[0]), Math.Min(c0[1], c1[1]), Math.Min(c0[2], c1[2]), Math.Max(c0[0], c1[0]), Math.Max(c0[1], c1[1]), Math.Max(c0[2], c1[2]), col);
            left -= 12;
            return true;
        }
        // A whole layer in cells about 60 cm (a cell outside the hull is left out).
        void Base(double u0, double u1, string col)
        {
            int ni = Math.Clamp((int)Math.Round((a1 - a0) / 0.6), 1, 60), nj = Math.Clamp((int)Math.Round((l1 - l0) / 0.6), 1, 80);
            if (ni * nj * 12 > left / 2) { ni = 1; nj = 1; }
            for (var i = 0; i < ni; i++)
                for (var j = 0; j < nj; j++)
                    Piece(a0 + (a1 - a0) * i / ni, a0 + (a1 - a0) * (i + 1) / ni, l0 + (l1 - l0) * j / nj, l0 + (l1 - l0) * (j + 1) / nj, u0, u1, col);
        }
        double wa = a1 - a0, wl = l1 - l0;
        switch (r.Floor)
        {
            case "Plain":
                Base(0.003, 0.013, "#7d848c");
                break;
            case "Diamond":
            {
                Base(0.003, 0.009, "#9aa2aa");
                // Raised diamonds in a herringbone, as tread plate: flat lozenges 3 mm proud, rows turned each way.
                const double s = 0.12;
                var c = DesignMesh.Hex("#b7bec6");
                for (var x = a0 + s / 2; x < a1 - s / 4; x += s)
                    for (var y = l0 + s / 2; y < l1 - s / 4; y += s)
                    {
                        if (left < 2)
                            break;
                        var turn = ((int)((x - a0) / s) + (int)((y - l0) / s)) % 2 == 0;
                        double hx = turn ? 0.045 : 0.012, hy = turn ? 0.012 : 0.045;
                        (double, double, double) P(double px, double py) { var v = V(px, py, Up(fTop + 0.012)); return (v[0], v[1], v[2]); }
                        var mid = V(x, y, Up(fTop));
                        if (!d.Hulls.Any(h => h.Depth(mid[0], mid[1], mid[2]) > 0.01) || holes.Any(o => y + 0.05 > o.S0 && y - 0.05 < o.S1 && x + 0.05 > o.H0 && x - 0.05 < o.H1))
                            continue;
                        var q = (P(x - hx, y), P(x, y - hy), P(x + hx, y), P(x, y + hy));
                        m.Quad(q.Item1, q.Item2, q.Item3, q.Item4, c, false);
                        left -= 2;
                    }
                break;
            }
            case "Grating":
            {
                Base(0.003, 0.006, "#24282c");
                // Bearing bars along the room every 4 cm on cross bars every 60 cm: an open grate over the dark pan.
                for (var x = a0 + 0.03; x < a1 - 0.01; x += 0.06)
                {
                    var n = Math.Max(1, (int)Math.Round(wl / 1.2));
                    for (var k = 0; k < n; k++)
                        Piece(x - 0.005, x + 0.005, l0 + wl * k / n + 0.002, l0 + wl * (k + 1) / n, 0.008, 0.04, "#7f878e");
                }
                for (var y = l0 + 0.3; y < l1 - 0.1; y += 0.6)
                    Piece(a0, a1, y - 0.006, y + 0.006, 0.01, 0.036, "#69727a");
                break;
            }
            case "Tiles":
            {
                Base(0.003, 0.008, "#3c4146");
                const double s = 0.5;
                for (var x = a0; x < a1 - 0.01; x += s)
                    for (var y = l0; y < l1 - 0.01; y += s)
                        Piece(x + 0.006, x + s - 0.006, y + 0.006, y + s - 0.006, 0.006, 0.016, rnd.NextDouble() < 0.12 ? "#b8bcc0" : "#cfd3d6");
                break;
            }
            case "Rubber":
            {
                Base(0.003, 0.012, "#2b2d30");
                for (var x = a0 + 0.04; x < a1 - 0.02; x += 0.07)
                {
                    var n = Math.Max(1, (int)Math.Round(wl / 1.2));
                    for (var k = 0; k < n; k++)
                        Piece(x - 0.012, x + 0.012, l0 + wl * k / n + 0.03, l0 + wl * (k + 1) / n - 0.03, 0.01, 0.018, "#37393d");
                }
                break;
            }
            case "Wood":
            {
                Base(0.003, 0.007, "#2a1f17");
                // Planks along the room, 14 cm wide, staggered lengths, three tones.
                string[] tones = ["#8a5d3b", "#7a5034", "#9a6a44"];
                for (var x = a0; x < a1 - 0.01; x += 0.14)
                {
                    var y = l0 - rnd.NextDouble() * 1.2;
                    while (y < l1 - 0.01)
                    {
                        var ln = 1.2 + rnd.NextDouble() * 1.2;
                        Piece(x + 0.003, x + 0.14 - 0.003, y + 0.003, y + ln - 0.003, 0.006, 0.02, tones[rnd.Next(3)]);
                        y += ln;
                    }
                }
                break;
            }
            case "Hazard":
            {
                // A grey deck in a 30 cm border of yellow and black blocks.
                const double bw = 0.3;
                Piece(a0 + bw + 0.004, a1 - bw - 0.004, l0 + bw + 0.004, l1 - bw - 0.004, 0.003, 0.012, "#70777e");
                var i = 0;
                for (var y = l0; y < l1 - 0.01; y += bw, i++)
                {
                    Piece(a0, a0 + bw, y + 0.002, y + bw - 0.002, 0.003, 0.016, i % 2 == 0 ? "#e2b21c" : "#1c1d1f");
                    Piece(a1 - bw, a1, y + 0.002, y + bw - 0.002, 0.003, 0.016, i % 2 == 0 ? "#e2b21c" : "#1c1d1f");
                }
                i = 0;
                for (var x = a0 + bw; x < a1 - bw - 0.01; x += bw, i++)
                {
                    Piece(x + 0.002, Math.Min(x + bw, a1 - bw) - 0.002, l0, l0 + bw, 0.003, 0.016, i % 2 == 0 ? "#1c1d1f" : "#e2b21c");
                    Piece(x + 0.002, Math.Min(x + bw, a1 - bw) - 0.002, l1 - bw, l1, 0.003, 0.016, i % 2 == 0 ? "#1c1d1f" : "#e2b21c");
                }
                break;
            }
        }
        budget = left;
    }

    /// <summary>
    /// The shell of a room out to the hull, from the very samples <see cref="RoomMesh"/> draws its volume with (its Span, its Edges at the
    /// same four heights and the same 50 cm slices), so it follows the skin as the hologram does: its outer face 4 mm in from the hologram,
    /// its inner face t further in (Karl, 2026-11-25: inside its own room). A quad whose outside is in a door or a group-mate's room is left out.
    /// </summary>
    /// <summary>
    /// A wall extension's place on its room's wall (<see cref="ExtRings"/>'s): its span along the wall (plan metres), the wall's plan
    /// coordinate, and the way out of it (-1 or 1).
    /// </summary>
    public static (double S0, double S1, double Wall, double Out) ExtSpan(Room r, RoomExt e)
    {
        var alongZ = e.Side is "x0" or "x1";
        double lo = alongZ ? r.Z0 : r.X0, len = (alongZ ? r.Z1 : r.X1) - lo, width = Math.Min(e.Width, len);
        var m = lo + width / 2 + (len - width) * e.T;
        return (m - width / 2, m + width / 2, e.Side switch { "x0" => r.X0, "x1" => r.X1, "z0" => r.Z0, _ => r.Z1 }, e.Side is "x0" or "z0" ? -1 : 1);
    }

    /// <summary>How far over a room's ceiling its shell may reach when it is pulled inside the skin: its wall, and an up extension's height over it.</summary>
    public static double ShellOver(Room r)
    {
        var o = r.WallThick;
        foreach (var e in r.Exts)
            if (e.Side == "up")
                o = Math.Max(o, e.Depth + r.WallThick);
        return o;
    }

    /// <summary>Where a room's extension lies, for its shell and its interior cladding alike: P(a across, o out of the wall or up over the
    /// ceiling, hl its height (a wall one's) or its place along the room (an up one's)) to world, its outline and the outline's middle,
    /// its base's span (S0, S1) and an up one's run along the room (L0, L1); the room's height. Null when it has no outline.</summary>
    internal sealed record ExtPlace(Func<double, double, double, double[]> P, List<(double A, double B)> Outline, double CA, double CO,
        bool Up, double S0, double S1, double L0, double L1, double Hgt);

    internal static ExtPlace? ExtAt(ShipDesign d, Room r, RoomExt e)
    {
        var b = d.Box(r);
        int ax = 0, al = d.Stacked ? 1 : 2, up = d.Stacked ? 2 : 1;
        double fl = d.Stacked ? b[5] : b[2], sg = d.Stacked ? -1 : 1, hgt = d.Stacked ? b[5] - b[4] : b[3] - b[2];
        double[] W(double u, double v, double h) { var o = new double[3]; o[ax] = u; o[al] = v; o[up] = fl + sg * h; return o; }
        var side = e.Side;
        var wallUp = side == "up";
        double s0, s1, wall = 0, so = 1, l0 = 0, l1 = 0, mid;
        var acrossX = r.X1 - r.X0 <= r.Z1 - r.Z0;
        if (wallUp)
        {
            (s0, s1) = acrossX ? (r.X0, r.X1) : (r.Z0, r.Z1);
            (l0, l1) = acrossX ? (r.Z0, r.Z1) : (r.X0, r.X1);
        }
        else
            (s0, s1, wall, so) = ExtSpan(r, e);
        mid = (s0 + s1) / 2;
        var ol = e.Outline(s1 - s0);
        if (ol.Count < 2)
            return null;
        // A plan point of its outline (a across, o out of the wall or up over the ceiling) at height h (a wall one's) or along l (an up one's).
        double[] P(double a, double o, double hl)
        {
            if (wallUp)
                return acrossX ? W(mid + a, hl, hgt + o) : W(hl, mid + a, hgt + o);
            var alongZ = side is "x0" or "x1";
            double u = alongZ ? wall + so * o : mid + a, v = alongZ ? mid + a : wall + so * o;
            return W(u, v, hl);
        }
        double ca = 0, co = 0;
        foreach (var (a, o) in ol)
            (ca, co) = (ca + a / ol.Count, co + o / ol.Count);
        return new ExtPlace(P, ol, ca, co, wallUp, s0, s1, l0, l1, hgt);
    }

    /// <summary>
    /// A room's extension walled (step 4, package J: an extension was a hologram only): its outline's walls, each a thin convex piece
    /// <paramref name="t"/> thick inside the outline, a post at each end of its base closing the corner with the room's wall (cut open across
    /// it: RoomShell), and for a wall one its floor and its ceiling, the outline closed along the wall (onto the room's floor's end, as the room's
    /// own shell is ShellGap in from its box); for an up one (a vault over the ceiling, which is left out) its end caps. Into the room's shell
    /// mesh, and each piece's corners into <paramref name="sink"/> (the game's walkable room collides as them: DesignParts.Rooms.cs).
    /// </summary>
    private static void ExtShell(ShipDesign d, Room r, RoomExt e, double t, (byte, byte, byte) col, DesignMesh m, List<double[]>? sink)
    {
        const double g = ShellGap;
        if (ExtAt(d, r, e) is not { } x)
            return;
        var (P, ol, ca, co, wallUp, s0, s1, l0, l1, hgt) = (x.P, x.Outline, x.CA, x.CO, x.Up, x.S0, x.S1, x.L0, x.L1, x.Hgt);
        // Its two ends in its run: a wall one's floor to ceiling, an up one's along the room.
        double e0 = wallUp ? l0 + g : g + 0.003, e1 = wallUp ? l1 - g : hgt - g - 0.003;
        void Piece(List<(double A, double O)> q, double h0, double h1)
        {
            Prism(m, [.. q.Select(v => P(v.A, v.O, h0))], [.. q.Select(v => P(v.A, v.O, h1))], col, sink);
        }
        for (var i = 0; i + 1 < ol.Count; i++)
        {
            var (q0, q1) = (ol[i], ol[i + 1]);
            double da = q1.A - q0.A, dO = q1.B - q0.B, len = Math.Sqrt(da * da + dO * dO);
            if (len < 1e-4)
                continue;
            double na = -dO / len, no = da / len;
            if (na * (ca - (q0.A + q1.A) / 2) + no * (co - (q0.B + q1.B) / 2) < 0)
                (na, no) = (-na, -no);
            Piece([q0, q1, (q1.A + na * t, q1.B + no * t), (q0.A + na * t, q0.B + no * t)], e0, e1);
        }
        // The posts at its base's ends, into the room wall's thickness (its cut there).
        var h = (s1 - s0) / 2;
        foreach (var sa in new[] { -1.0, 1.0 })
        {
            double a0 = sa * h, a1 = sa * (h - t);
            Piece([(Math.Min(a0, a1), -(g + t)), (Math.Max(a0, a1), -(g + t)), (Math.Max(a0, a1), 0), (Math.Min(a0, a1), 0)], e0, e1);
        }
        // Its outline closed along its base, a hair onto the room's side of the wall line.
        var closed = new List<(double A, double O)>(ol);
        closed[0] = (closed[0].A, -g);
        closed[^1] = (closed[^1].A, -g);
        if (wallUp)
        {
            Piece(closed, l0 + g, l0 + g + t);
            Piece(closed, l1 - g - t, l1 - g);
        }
        else
        {
            Piece(closed, g, g + t);
            Piece(closed, hgt - g - t, hgt - g);
        }
    }

    /// <summary>How far a slice boundary inside a merged run of a hull shell's strips may lie off the straight line between the run's ends (m).</summary>
    public const double LaneTol = 0.02;

    private static void HullShell(ShipDesign d, Room r, double t, (byte, byte, byte) col, Func<double[], bool, bool> keep, DesignMesh m, List<double[]> doors, List<double[]>? strips)
    {
        const double g = ShellGap;
        var dk = d.DeckOf(r);
        var (s0, s1) = Span(d, r);
        var hgt = dk.Height;
        // Slices along the room and rows up it, split at every door's edges (Karl, 2026-11-25: both rooms cut the same opening).
        int va_ = d.Stacked ? 1 : 2, up_ = d.Stacked ? 2 : 1;
        var vs = new List<double>();
        var n0 = Math.Max(1, (int)Math.Ceiling((s1 - s0) / 0.5));
        for (var q = 0; q <= n0; q++)
            vs.Add(s0 + (s1 - s0) * q / n0);
        var us = new List<double> { 0, hgt / 4, hgt / 2, hgt * 3 / 4, hgt };
        foreach (var q in doors)
        {
            foreach (var e in new[] { q[va_] - DoorHalf(q), q[va_] + DoorHalf(q) })
                if (e > s0 + 0.002 && e < s1 - 0.002)
                    vs.Add(e);
            var fu = d.Stacked ? dk.Floor - q[up_] : q[up_] - dk.Floor;
            if (IsWindow(q))
            {
                foreach (var e in new[] { fu, fu + DoorTop(q) })
                    if (e > 0.002 && e < hgt - 0.002)
                        us.Add(e);
            }
            else if (Math.Abs(fu) < 0.05 && !IsHole(q))
                foreach (var e in new[] { fu + DoorTop(q) })
                    if (e > 0.002 && e < hgt - 0.002)
                        us.Add(e);
        }
        vs.Sort();
        us.Sort();
        var nv = vs.Count - 1;
        var nu = us.Count - 1;
        (double, double, double) P(double u, double v, double up) { var w = d.World(dk, u, v, up); return (w[0], w[1], w[2]); }
        double V(int i) => vs[i];
        double Up(int k) => us[k];
        var side = DesignMesh.Tone(col, 0.9);
        // Both faces of a strip of shell: inner (off by g) and outer (off by g + t), dir the plan offset per unit of push.
        // A strip's collider joins the one before it in its lane (a wall's row, the floor, the ceiling) while the run stays a straight piece
        // (Karl, 2026-11-27: a room out to the hull made one thin hull per 50 cm slice and row, ~10 a metre; a few such rooms went past the
        // design's MaxHulls and the last rooms' floors were left out, so a person fell through them). Each lane's run: its first slice's
        // four corners, its last's, and every slice boundary between, which must lie within LaneTol of the straight line between its ends.
        var lanes = new Dictionary<int, (int Last, List<(double V, (double, double, double)[] C)> Bounds)>();
        void Flush(int lane)
        {
            if (strips is null || !lanes.Remove(lane, out var run))
                return;
            var (f, l) = (run.Bounds[0].C, run.Bounds[^1].C);
            var s8 = new double[24];
            var k = 0;
            foreach (var v in new[] { f[0], f[1], l[1], l[0], f[2], f[3], l[3], l[2] })
            {
                (s8[k], s8[k + 1], s8[k + 2]) = v;
                k += 3;
            }
            strips.Add(s8);
        }
        bool Straight(List<(double V, (double, double, double)[] C)> b)
        {
            var (v0, v1) = (b[0].V, b[^1].V);
            for (var j = 1; j < b.Count - 1; j++)
            {
                var f = (b[j].V - v0) / (v1 - v0);
                for (var c = 0; c < 4; c++)
                {
                    var (p, q, w) = (b[0].C[c], b[^1].C[c], b[j].C[c]);
                    var (dx, dy, dz) = (p.Item1 + (q.Item1 - p.Item1) * f - w.Item1, p.Item2 + (q.Item2 - p.Item2) * f - w.Item2, p.Item3 + (q.Item3 - p.Item3) * f - w.Item3);
                    if (dx * dx + dy * dy + dz * dz > LaneTol * LaneTol)
                        return false;
                }
            }
            return true;
        }
        // One strip into its lane: lo/hi its two boundaries' corners (inner a, inner b, outer a, outer b), slice i.
        void Lane(int lane, int i, double va, double vb, (double, double, double)[] lo, (double, double, double)[] hi)
        {
            if (lanes.TryGetValue(lane, out var run) && run.Last == i - 1)
            {
                run.Bounds.Add((vb, hi));
                if (Straight(run.Bounds))
                {
                    lanes[lane] = (i, run.Bounds);
                    return;
                }
                run.Bounds.RemoveAt(run.Bounds.Count - 1);
            }
            Flush(lane);
            lanes[lane] = (i, [(va, lo), (vb, hi)]);
        }
        void Two(Func<double, ((double, double, double), (double, double, double), (double, double, double), (double, double, double))> at, (byte, byte, byte) c, bool flat = false, int lane = -1, int slice = 0, double va = 0, double vb = 0)
        {
            var (pa, _, pc, _) = at(0.03); // just outside its outer face: the probe for a door or a group-mate's room
            if (!keep([(pa.Item1 + pc.Item1) / 2, (pa.Item2 + pc.Item2) / 2, (pa.Item3 + pc.Item3) / 2], flat))
                return;
            var (a, b, cc, dd) = at(-g);
            m.Quad(a, b, cc, dd, c, false);
            var (a2, b2, c2, d2) = at(-(g + t));
            m.Quad(a2, b2, c2, d2, c, false);
            if (strips is not null && lane >= 0)
                // (a wall's corners at va are a and dd, at vb b and cc; the floor's and ceiling's at va a and b, at vb cc and dd)
                Lane(lane, slice, va, vb, flat ? [a, b, a2, b2] : [a, dd, a2, d2], flat ? [dd, cc, d2, c2] : [b, cc, b2, c2]);
            else if (strips is not null)
            {
                var s8 = new double[24];
                var k = 0;
                foreach (var v in new[] { a, b, cc, dd, a2, b2, c2, d2 })
                {
                    (s8[k], s8[k + 1], s8[k + 2]) = v;
                    k += 3;
                }
                strips.Add(s8);
            }
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
                Two(o => (P(a0 - o, va, k == 0 ? ua - o : ua), P(a1 - o, vb, k == 0 ? ua - o : ua), P(a3 - o, vb, k == nu - 1 ? ub + o : ub), P(a2 - o, va, k == nu - 1 ? ub + o : ub)), col, false, 2 + 2 * k, i, va, vb);
                Two(o => (P(b0 + o, va, k == 0 ? ua - o : ua), P(b1 + o, vb, k == 0 ? ua - o : ua), P(b3 + o, vb, k == nu - 1 ? ub + o : ub), P(b2 + o, va, k == nu - 1 ? ub + o : ub)), col, false, 3 + 2 * k, i, va, vb);
            }
            var (f0, h0) = Edges(d, r, va, 0);
            var (f1, h1) = Edges(d, r, vb, 0);
            Two(o => (P(f0 - o, va, -o), P(h0 + o, va, -o), P(h1 + o, vb, -o), P(f1 - o, vb, -o)), col, true, 0, i, va, vb);
            var (c0, e0) = Edges(d, r, va, hgt);
            var (c1, e1) = Edges(d, r, vb, hgt);
            Two(o => (P(c0 - o, va, hgt + o), P(e0 + o, va, hgt + o), P(e1 + o, vb, hgt + o), P(c1 - o, vb, hgt + o)), col, true, 1, i, va, vb);
        }
        foreach (var lane in lanes.Keys.ToList())
            Flush(lane);
        // Its ends where the hologram's are: at its Span, a run-on end closed across the skin where it stops.
        foreach (var (v, sg) in new[] { (s0, -1.0), (s1, 1.0) })
        {
            // Split across at the edges of each door in this end wall (Karl, 2026-11-27: a strip ran the whole width, so a door through
            // it was either all wall or all hole, and two rooms out to the hull end to end were walled off from each other).
            var xs = new List<double> { double.MinValue };
            foreach (var q in doors)
                if (!IsHole(q) && Math.Abs(q[va_] - v) < 0.3)
                    xs.AddRange([q[0] - DoorHalf(q), q[0] + DoorHalf(q)]);
            xs.Add(double.MaxValue);
            xs.Sort();
            for (var k = 0; k < nu; k++)
            {
                double ua = Up(k), ub = Up(k + 1);
                var (a0, b0) = Edges(d, r, v, ua);
                var (a1, b1) = Edges(d, r, v, ub);
                for (var x = 0; x + 1 < xs.Count; x++)
                {
                    double la = Math.Max(a0 + g, xs[x]), ra = Math.Min(b0 - g, xs[x + 1]), lb = Math.Max(a1 + g, xs[x]), rb = Math.Min(b1 - g, xs[x + 1]);
                    if (ra <= la + 0.002 && rb <= lb + 0.002)
                        continue;
                    (ra, rb) = (Math.Max(ra, la), Math.Max(rb, lb));
                    Two(o => (P(la, v + sg * o, k == 0 ? ua - g : ua), P(ra, v + sg * o, k == 0 ? ua - g : ua), P(rb, v + sg * o, k == nu - 1 ? ub + g : ub), P(lb, v + sg * o, k == nu - 1 ? ub + g : ub)), col);
                }
            }
        }
    }
}
