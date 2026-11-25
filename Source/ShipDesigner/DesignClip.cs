namespace CallOfSlop.ShipDesigner;

/// <summary>
/// Rooms kept inside the ship (the user, 2026-11-24: a room's hologram and its shell poked out through the curved upper hull): every vertex
/// of a room's meshes that is outside all the bodies, or closer to a skin than an inset, is pulled in onto that skin less the inset. A box
/// face against the skin so bends to it, and a shell's outward thickness, roof and corners all end inside.
/// </summary>
public static partial class DesignScene
{
    /// <summary>The insets (m): the shell's outer face, the hologram inside it and the interior cladding inside that, so none share a face.</summary>
    public const double ShellInset = 0.012, HoloInset = 0.022, InteriorInset = 0.03;

    /// <summary>Pulls every vertex of a world-space mesh to at least inset inside a body's skin.</summary>
    public static void ClipToHulls(ShipDesign d, DesignMesh m, double inset, Room? cap = null, double over = 0)
    {
        if (d.Hulls.Count == 0)
            return;
        // Its top (Karl, 2026-11-25): pulled onto the skin, a vertex never rises above the room's own ceiling (plus over).
        double top = double.NaN;
        if (cap is not null)
        {
            var dk = d.DeckOf(cap);
            top = d.Stacked ? dk.Floor - dk.Height - over : dk.Floor + dk.Height + over;
        }
        var p = m.P;
        for (var i = 0; i + 2 < p.Count; i += 3)
        {
            double x = p[i], y = p[i + 1], z = p[i + 2];
            for (var pass = 0; pass < 3; pass++)
            {
                HullBody? best = null;
                var bd = double.NegativeInfinity;
                foreach (var h in d.Hulls)
                {
                    var dh = h.Depth(x, y, z);
                    if (dh > bd)
                        (bd, best) = (dh, h);
                }
                if (best is null || bd >= inset * 0.95)
                    break;
                var h0 = best;
                var copy = h0.Copies == 2 && Math.Abs(x - h0.CopyX(1)) < Math.Abs(x - h0.CopyX(0)) ? 1 : 0;
                var e = inset * (1 + pass); // a profile's corner: pulled in further on the next pass
                double lx = x - h0.CopyX(copy), ly = y - h0.Y, lz = Math.Clamp(z - h0.Z, h0.Front + e, h0.Back - e);
                var s = h0.OnSkin(lz, h0.NearestT(lx, ly, lz), lx < -0.01 ? -1 : 1);
                (x, y, z) = (h0.CopyX(copy) + s.X - s.Nx * e, h0.Y + s.Y - s.Ny * e, h0.Z + lz);
            }
            if (!double.IsNaN(top))
            {
                if (d.Stacked) z = Math.Max(z, top);
                else y = Math.Min(y, top);
            }
            (p[i], p[i + 1], p[i + 2]) = (x, y, z);
        }
    }

    /// <summary>
    /// The hologram of a room not out to the hull: its box (3 cm in from its own, as the page drew it) with each face cut into a grid about
    /// 40 cm square, so pulled inside the skin it bends to the hull's curve. Its floor slab is the page's own.
    /// </summary>
    public static DesignMesh RoomBoxMesh(ShipDesign d, Room r)
    {
        var m = new DesignMesh();
        m.World();
        var b = d.Box(r);
        var col = DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour);
        GridBox(m, b[0] + 0.03, b[2] + 0.03, b[4] + 0.03, b[1] - 0.03, b[3] - 0.03, b[5] - 0.03, col, 0.4);
        ClipToHulls(d, m, HoloInset, r);
        return m;
    }

    /// <summary>An axis-aligned box whose six faces are grids of cells about step square.</summary>
    public static void GridBox(DesignMesh m, double x0, double y0, double z0, double x1, double y1, double z1, (byte, byte, byte) col, double step)
    {
        double[] lo = [x0, y0, z0], hi = [x1, y1, z1];
        for (var ax = 0; ax < 3; ax++)
        {
            int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
            int n1 = Math.Clamp((int)Math.Ceiling((hi[a1] - lo[a1]) / step), 1, 200), n2 = Math.Clamp((int)Math.Ceiling((hi[a2] - lo[a2]) / step), 1, 200);
            foreach (var f in new[] { lo[ax], hi[ax] })
            {
                var c = DesignMesh.Tone(col, f == lo[ax] ? 0.8 : 1.0);
                (double, double, double) Pt(int i, int j)
                {
                    var v = new double[3];
                    v[ax] = f;
                    v[a1] = lo[a1] + (hi[a1] - lo[a1]) * i / n1;
                    v[a2] = lo[a2] + (hi[a2] - lo[a2]) * j / n2;
                    return (v[0], v[1], v[2]);
                }
                for (var i = 0; i < n1; i++)
                    for (var j = 0; j < n2; j++)
                        m.Quad(Pt(i, j), Pt(i + 1, j), Pt(i + 1, j + 1), Pt(i, j + 1), c, false);
            }
        }
    }

    /// <summary>
    /// An extension's hologram (Karl, 2026-11-25: wall and ceiling shapes stuck out of the hull as wings): its two rings joined by sides cut
    /// about step long each way and a cap on each ring cut into bands, pulled inside the skin so it bends to the hull. Its rings come back
    /// pulled in too, for its outline.
    /// </summary>
    public static (DesignMesh Mesh, List<double[]> A, List<double[]> B) ExtMesh(ShipDesign d, Room r, RoomExt e, double step = 0.4)
    {
        var (ra, rb) = ExtRings(d, r, e);
        var m = new DesignMesh();
        m.World();
        var col = DesignMesh.Hex(RoomKinds.Find(r.Kind)!.Colour);
        var n = ra.Count;
        if (n < 3 || rb.Count != n)
            return (m, ra, rb);
        static double[] L(double[] a, double[] b, double t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
        static double Dist(double[] a, double[] b) => Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        static (double, double, double) T(double[] v) => (v[0], v[1], v[2]);
        int Cuts(double len) => Math.Clamp((int)Math.Ceiling(len / step), 1, 100);
        var nv = Cuts(Enumerable.Range(0, n).Max(i => Dist(ra[i], rb[i])));
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            var nu = Cuts(Math.Max(Dist(ra[i], ra[j]), Dist(rb[i], rb[j])));
            double[] P(int u, int v) => L(L(ra[i], ra[j], (double)u / nu), L(rb[i], rb[j], (double)u / nu), (double)v / nv);
            for (var a = 0; a < nu; a++)
                for (var b = 0; b < nv; b++)
                    m.Quad(T(P(a, b)), T(P(a + 1, b)), T(P(a + 1, b + 1)), T(P(a, b + 1)), col, false);
        }
        foreach (var ring in new[] { ra, rb })
        {
            var c = new double[3];
            foreach (var p in ring)
                for (var k = 0; k < 3; k++)
                    c[k] += p[k] / n;
            for (var i = 0; i < n; i++)
            {
                var j = (i + 1) % n;
                int nr = Cuts(Math.Max(Dist(c, ring[i]), Dist(c, ring[j]))), nu = Cuts(Dist(ring[i], ring[j]));
                double[] P(int rr, int uu) => L(c, L(ring[i], ring[j], (double)uu / nu), (double)rr / nr);
                for (var a = 0; a < nr; a++)
                    for (var u = 0; u < nu; u++)
                        if (a == 0)
                            m.Tri(T(c), T(P(1, u)), T(P(1, u + 1)), col, false);
                        else
                            m.Quad(T(P(a, u)), T(P(a + 1, u)), T(P(a + 1, u + 1)), T(P(a, u + 1)), col, false);
            }
        }
        var over = e.Side == "up" ? e.Depth : 0;
        ClipToHulls(d, m, HoloInset, r, over);
        var rings = new DesignMesh();
        foreach (var p in ra.Concat(rb))
            rings.P.AddRange(p);
        ClipToHulls(d, rings, HoloInset, r, over);
        var q = rings.P;
        List<double[]> Ring(int o) => [.. Enumerable.Range(o, n).Select(i => new[] { q[3 * i], q[3 * i + 1], q[3 * i + 2] })];
        return (m, Ring(0), Ring(n));
    }
}
