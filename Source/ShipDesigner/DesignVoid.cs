namespace CallOfSlop.ShipDesigner;

/// <summary>
/// The space between a hull's skin and its rooms' walls, filled with structure (Karl, 2026-11-27: the bare inside of the skin showed
/// there). A PREVIEW of four ways to fill it, picked by the COS_VOID_FILL environment variable (A frames and stringers, B a lined
/// hull tied to the rooms, C truss frames, D service runs): drawn only, no colliders, kept out of every room's box.
/// </summary>
public static partial class DesignScene
{
    /// <summary>The preview's style letter, or "" for none.</summary>
    public static readonly string VoidStyle = (Environment.GetEnvironmentVariable("COS_VOID_FILL") ?? "").Trim().ToUpperInvariant();

    private readonly record struct V3(double X, double Y, double Z)
    {
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public V3 Cross(V3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Len => Math.Sqrt(Dot(this));
        public V3 Unit => Len < 1e-12 ? this : this * (1 / Len);
        public (double, double, double) T => (X, Y, Z);
    }

    private sealed class VoidCtx
    {
        public required ShipDesign D;
        public required HullBody H;
        public required DesignMesh M;
        public required List<double[]> Rooms;
        public double Cx;

        public bool InRoom(V3 p, double pad)
        {
            foreach (var b in Rooms)
                if (p.X > b[0] - pad && p.X < b[1] + pad && p.Y > b[2] - pad && p.Y < b[3] + pad && p.Z > b[4] - pad && p.Z < b[5] + pad)
                    return true;
            return false;
        }

        public bool InHull(V3 p, double m) => D.Hulls.Any(h => h.Depth(p.X, p.Y, p.Z) > m);

        public bool Clear(V3 p, double pad) => InHull(p, 0.015) && !InRoom(p, pad);

        /// <summary>A world point from hull-local (h across on side s, y, z).</summary>
        public V3 W(int s, double hh, double y, double z) => new(Cx + s * hh, H.Y + y, H.Z + z);
    }

    /// <summary>The fill of one copy of a hull body (world frame), or null when no style is picked.</summary>
    public static DesignMesh? VoidFill(ShipDesign d, HullBody h, int copy)
    {
        if (VoidStyle.Length == 0 || h.Ring.Count < 2)
            return null;
        var m = new DesignMesh();
        m.World();
        var c = new VoidCtx { D = d, H = h, M = m, Rooms = d.Rooms.Select(d.Box).ToList(), Cx = h.CopyX(copy) };
        switch (VoidStyle)
        {
            case "A": VoidFrames(c); break;
            case "B": VoidLined(c); break;
            case "C": VoidTruss(c); break;
            case "D": VoidServices(c); break;
            case "E": VoidSolid(c); break;
        }
        Console.WriteLine($"ship designer: void fill {VoidStyle} on \"{h.Name}\" copy {copy}: {m.Tris} triangles");
        return m;
    }

    // ---- shared pieces --------------------------------------------------------------------------------------------------------

    /// <summary>An oriented box from a to b (its axis), u across it (half hu), v = axis x u (half hv), each face wound outward.</summary>
    private static void OBox(DesignMesh m, V3 a, V3 b, V3 u, double hu, double hv, (byte, byte, byte) col, bool ends = true)
    {
        var e = (b - a).Unit;
        u = (u - e * u.Dot(e)).Unit;
        var v = e.Cross(u).Unit;
        var mid = (a + b) * 0.5;
        V3 P(V3 o, int su, int sv) => o + u * (su * hu) + v * (sv * hv);
        void Q(V3 p0, V3 p1, V3 p2, V3 p3, double shade)
        {
            var n = (p1 - p0).Cross(p2 - p0);
            var cen = (p0 + p1 + p2 + p3) * 0.25;
            var c = Tone(col, shade);
            if (n.Dot(cen - mid) < 0)
                m.Quad(p0.T, p3.T, p2.T, p1.T, c);
            else
                m.Quad(p0.T, p1.T, p2.T, p3.T, c);
        }
        Q(P(a, 1, -1), P(b, 1, -1), P(b, 1, 1), P(a, 1, 1), 1.0);
        Q(P(a, -1, -1), P(a, -1, 1), P(b, -1, 1), P(b, -1, -1), 0.8);
        Q(P(a, -1, 1), P(a, 1, 1), P(b, 1, 1), P(b, -1, 1), 0.9);
        Q(P(a, -1, -1), P(b, -1, -1), P(b, 1, -1), P(a, 1, -1), 0.85);
        if (ends)
        {
            Q(P(a, -1, -1), P(a, 1, -1), P(a, 1, 1), P(a, -1, 1), 0.75);
            Q(P(b, -1, -1), P(b, -1, 1), P(b, 1, 1), P(b, 1, -1), 0.75);
        }
    }

    private static (byte, byte, byte) Tone((byte R, byte G, byte B) c, double f) => DesignMesh.Tone(c, f);

    /// <summary>A member a to b, cut where it would enter a room (tested every step), each kept run one box.</summary>
    private static void Member(VoidCtx c, V3 a, V3 b, V3 u, double hu, double hv, (byte, byte, byte) col, double step = 0.25)
    {
        var len = (b - a).Len;
        if (len < 0.02)
            return;
        var n = Math.Max(1, (int)Math.Ceiling(len / step));
        var pad = Math.Max(hu, hv) + 0.04;
        var run = -1;
        for (var i = 0; i <= n + 1; i++)
        {
            var ok = i <= n && c.Clear(a + (b - a) * ((double)i / n), pad);
            if (ok && run < 0)
                run = i;
            else if (!ok && run >= 0)
            {
                var end = i - 1;
                if (end > run)
                    OBox(c.M, a + (b - a) * ((double)run / n), a + (b - a) * ((double)end / n), u, hu, hv, col);
                run = -1;
            }
        }
    }

    /// <summary>The path round one side's half profile at local z: centreline keel, the profile, centreline spine; hull-local (h, y).</summary>
    private static int VoidPath(HullBody h, double z, Span<double> hs, Span<double> ys)
    {
        var n = h.At(z, hs[1..], ys[1..]);
        if (n == 0)
            return 0;
        hs[0] = 0;
        ys[0] = ys[1];
        hs[n + 1] = 0;
        ys[n + 1] = ys[n];
        return n + 2;
    }

    /// <summary>A ring of samples round both sides at local z: each a skin point and its inward normal (world), corners mitred; split
    /// into steps no longer than <paramref name="step"/>. One list a side, keel to spine.</summary>
    private static List<(V3 P, V3 N)>[] VoidRing(VoidCtx c, double z, double step)
    {
        Span<double> hs = stackalloc double[HullBody.MaxLoft + 2];
        Span<double> ys = stackalloc double[HullBody.MaxLoft + 2];
        var n = VoidPath(c.H, z, hs, ys);
        var sides = new List<(V3, V3)>[2];
        for (var si = 0; si < 2; si++)
        {
            var s = si == 0 ? 1 : -1;
            var list = new List<(V3, V3)>();
            sides[si] = list;
            if (n < 2)
                continue;
            // Each facet's inward normal (h, y).
            var fn = new (double H, double Y)[n - 1];
            for (var k = 0; k < n - 1; k++)
            {
                double dx = hs[k + 1] - hs[k], dy = ys[k + 1] - ys[k], l = Math.Max(1e-9, Math.Sqrt(dx * dx + dy * dy));
                fn[k] = (-dy / l, dx / l);
            }
            V3 N(double nh, double ny) => new V3(s * nh, ny, 0);
            for (var k = 0; k < n - 1; k++)
            {
                double dx = hs[k + 1] - hs[k], dy = ys[k + 1] - ys[k], l = Math.Sqrt(dx * dx + dy * dy);
                if (l < 0.02)
                    continue;
                var cnt = Math.Max(1, (int)Math.Ceiling(l / step));
                for (var j = 0; j < cnt; j++)
                {
                    var f = (double)j / cnt;
                    var nn = fn[k];
                    if (j == 0 && k > 0)
                    {
                        // A corner: the two facets' normals averaged, longer by the miter so the offset line stays parallel to both.
                        var (ah, ay) = (fn[k - 1].H + fn[k].H, fn[k - 1].Y + fn[k].Y);
                        var al = Math.Sqrt(ah * ah + ay * ay);
                        if (al > 0.2)
                        {
                            var cos = (ah / al * fn[k].H + ay / al * fn[k].Y);
                            nn = (ah / al / Math.Max(0.5, cos), ay / al / Math.Max(0.5, cos));
                        }
                    }
                    list.Add((c.W(s, hs[k] + dx * f, ys[k] + dy * f, z), N(nn.H, nn.Y)));
                }
            }
            list.Add((c.W(s, hs[n - 1], ys[n - 1], z), N(fn[n - 2].H, fn[n - 2].Y)));
        }
        return sides;
    }

    /// <summary>The frame stations along the body: every pitch metres, from half a pitch in.</summary>
    private static IEnumerable<double> VoidStations(HullBody h, double pitch)
    {
        var len = h.Back - h.Front;
        var count = Math.Max(1, (int)Math.Floor(len / pitch));
        var start = h.Front + (len - (count - 1) * pitch) / 2;
        for (var i = 0; i < count; i++)
            yield return start + i * pitch;
    }

    // ---- A: ring frames and stringers -----------------------------------------------------------------------------------------

    private static void VoidFrames(VoidCtx c)
    {
        var frame = DesignMesh.Hex("#4b5157");
        var flange = DesignMesh.Hex("#5a6067");
        var stringer = DesignMesh.Hex("#666c72");
        var z1 = new V3(0, 0, 1);
        const double web = 0.24, skinOff = 0.03;
        foreach (var z in VoidStations(c.H, 1.25))
        {
            foreach (var side in VoidRing(c, z, 0.6))
                for (var i = 0; i + 1 < side.Count; i++)
                {
                    var (p0, n0) = side[i];
                    var (p1, n1) = side[i + 1];
                    var nm = (n0 + n1).Unit;
                    // The web: from the skin in to the flange, 4 cm thick along the hull.
                    Member(c, p0 + n0 * (skinOff + web / 2), p1 + n1 * (skinOff + web / 2), nm, web / 2, 0.02, frame, 0.3);
                    // The inner flange: 14 cm wide, 2.5 cm thick.
                    Member(c, p0 + n0 * (skinOff + web), p1 + n1 * (skinOff + web), nm, 0.0125, 0.07, flange, 0.3);
                }
        }
        // Stringers: a Z-section's look (one box 7 x 5 cm) every ~0.7 m round the profile, straight between the stations it meets.
        var rings = c.H.Ring;
        var mid = rings[rings.Count / 2].Z;
        var count = (int)Math.Round(VoidRing(c, mid, 100)[0].Zip(VoidRing(c, mid, 100)[0].Skip(1), (a, b) => (b.P - a.P).Len).Sum() / 0.7);
        var zs = new List<double>();
        for (var z = c.H.Front + 0.05; z < c.H.Back - 0.05; z += 1.25)
            zs.Add(z);
        zs.Add(c.H.Back - 0.05);
        for (var s = -1; s <= 1; s += 2)
            for (var k = 1; k < count; k++)
            {
                var t = (double)k / count;
                for (var i = 0; i + 1 < zs.Count; i++)
                {
                    var (ax, ay, az, anx, any) = c.H.OnSkin(zs[i], t, s);
                    var (bx, by, bz, bnx, bny) = c.H.OnSkin(zs[i + 1], t, s);
                    var na = new V3(-anx, -any, 0);
                    var nb = new V3(-bnx, -bny, 0);
                    var pa = new V3(c.Cx + ax, c.H.Y + ay, c.H.Z + az) + na * (skinOff + 0.035);
                    var pb = new V3(c.Cx + bx, c.H.Y + by, c.H.Z + bz) + nb * (skinOff + 0.035);
                    Member(c, pa, pb, (na + nb).Unit, 0.035, 0.025, stringer, 0.3);
                }
            }
    }

    // ---- B: a lined hull, the rooms clad outside and tied to the lining ---------------------------------------------------------

    private const double LineIn = 0.32; // the lining's inset from the skin

    private static void VoidLined(VoidCtx c)
    {
        var lining = DesignMesh.Hex("#7a7e80");
        var liningDark = DesignMesh.Hex("#666a6d");
        var outside = DesignMesh.Hex("#6e7377");
        var tie = DesignMesh.Hex("#a8873a"); // (yellow primer on the struts: a working ship's structure, not a show piece)
        var plate = DesignMesh.Hex("#3f4448");
        var rings = c.H.Ring;
        const double seam = 0.025, panelZ = 1.5, panelArc = 1.2, thick = 0.02;
        // The lining: the profile inset by LineIn, cut into panels between stations every panelZ, each facet into panelArc pieces.
        var zs = VoidStations(c.H, panelZ).Select(z => z - panelZ / 2).Append(c.H.Back).Where(z => z >= c.H.Front).ToList();
        if (zs[0] > c.H.Front + 0.01)
            zs.Insert(0, c.H.Front);
        for (var i = 0; i + 1 < zs.Count; i++)
        {
            var ra = VoidRing(c, zs[i] + 0.01, 100);
            var rb = VoidRing(c, zs[i + 1] - 0.01, 100);
            for (var si = 0; si < 2; si++)
            {
                var (a, b) = (ra[si], rb[si]);
                if (a.Count != b.Count)
                    continue;
                for (var k = 0; k + 1 < a.Count; k++)
                {
                    var a0 = a[k].P + a[k].N * LineIn;
                    var a1 = a[k + 1].P + a[k + 1].N * LineIn;
                    var b0 = b[k].P + b[k].N * LineIn;
                    var b1 = b[k + 1].P + b[k + 1].N * LineIn;
                    var arc = Math.Max((a1 - a0).Len, (b1 - b0).Len);
                    var cnt = Math.Max(1, (int)Math.Round(arc / panelArc));
                    for (var j = 0; j < cnt; j++)
                    {
                        double f0 = (double)j / cnt, f1 = (double)(j + 1) / cnt;
                        V3 L(double f, double g) => (a0 + (a1 - a0) * f) * (1 - g) + (b0 + (b1 - b0) * f) * g;
                        // inset by the seam each way
                        var dz = Math.Max(1e-6, (b0 - a0).Len);
                        var ga = seam / dz;
                        var arcLen = Math.Max(1e-6, (a1 - a0).Len * (f1 - f0));
                        var fa = seam / arcLen * (f1 - f0);
                        var p00 = L(f0 + fa, ga);
                        var p10 = L(f1 - fa, ga);
                        var p11 = L(f1 - fa, 1 - ga);
                        var p01 = L(f0 + fa, 1 - ga);
                        var cen = (p00 + p10 + p11 + p01) * 0.25;
                        var nIn = (a[k].N + a[k + 1].N).Unit;
                        if (!c.Clear(cen, 0.03) || !c.Clear(p00, 0.0) || !c.Clear(p10, 0.0) || !c.Clear(p11, 0.0) || !c.Clear(p01, 0.0))
                            continue;
                        var col = ((j + i + k) % 5 == 0) ? liningDark : lining;
                        VSlab(c.M, p00, p10, p11, p01, nIn, thick, col);
                    }
                }
            }
        }
        // The rooms' outer faces: panels on every face that looks into the void, and square-tube ties out to the lining.
        foreach (var r in c.D.Rooms)
        {
            var bx = c.D.Box(r);
            for (var ax = 0; ax < 3; ax++)
                for (var dir = -1; dir <= 1; dir += 2)
                {
                    int ua = (ax + 1) % 3, va = (ax + 2) % 3;
                    double face = dir < 0 ? bx[ax * 2] : bx[ax * 2 + 1];
                    double u0 = bx[ua * 2], u1 = bx[ua * 2 + 1], v0 = bx[va * 2], v1 = bx[va * 2 + 1];
                    V3 At(double a, double u, double v)
                    {
                        var q = new double[3];
                        q[ax] = a; q[ua] = u; q[va] = v;
                        return new V3(q[0], q[1], q[2]);
                    }
                    var n = At(dir, 0, 0);
                    int nu = Math.Max(1, (int)Math.Round((u1 - u0) / 1.2)), nv = Math.Max(1, (int)Math.Round((v1 - v0) / 1.2));
                    for (var iu = 0; iu < nu; iu++)
                        for (var iv = 0; iv < nv; iv++)
                        {
                            double pu0 = u0 + (u1 - u0) * iu / nu + seam, pu1 = u0 + (u1 - u0) * (iu + 1) / nu - seam;
                            double pv0 = v0 + (v1 - v0) * iv / nv + seam, pv1 = v0 + (v1 - v0) * (iv + 1) / nv - seam;
                            var cen = At(face, (pu0 + pu1) / 2, (pv0 + pv1) / 2);
                            var probe = cen + n * 0.12;
                            if (!c.InHull(probe, 0.03) || c.InRoom(probe, 0.0))
                                continue;
                            var off = face + dir * 0.008;
                            VSlab(c.M, At(off, pu0, pv0), At(off, pu1, pv0), At(off, pu1, pv1), At(off, pu0, pv1), n, 0.02, outside);
                        }
                    // Ties: every ~1.6 m across the face, at its middle the other way (two rows on a tall face), out along its normal to the lining.
                    int tu = Math.Max(1, (int)Math.Round((u1 - u0) / 1.6)), tv = (v1 - v0) > 2.5 ? 2 : 1;
                    for (var iu = 0; iu < tu; iu++)
                        for (var iv = 0; iv < tv; iv++)
                        {
                            var u = u0 + (u1 - u0) * (iu + 0.5) / tu;
                            var v = v0 + (v1 - v0) * (iv + 0.5) / tv;
                            var from = At(face + dir * 0.03, u, v);
                            V3? to = null;
                            for (var d = 0.08; d < 4; d += 0.04)
                            {
                                var p = from + n * d;
                                if (c.InRoom(p, 0.02))
                                    break;
                                if (c.H.Depth(p.X, p.Y, p.Z) < LineIn + 0.01)
                                {
                                    to = p;
                                    break;
                                }
                            }
                            if (to is not { } end || (end - from).Len < 0.12)
                                continue;
                            var side = Math.Abs(n.Y) > 0.5 ? new V3(1, 0, 0) : new V3(0, 1, 0);
                            OBox(c.M, from, end, side, 0.04, 0.04, tie);
                            OBox(c.M, from - n * 0.01, from + n * 0.02, side, 0.11, 0.11, plate);
                            OBox(c.M, end - n * 0.02, end + n * 0.01, side, 0.11, 0.11, plate);
                        }
                }
        }
    }

    /// <summary>A thin panel: the quad, and its thickness back along -n.</summary>
    private static void VSlab(DesignMesh m, V3 p00, V3 p10, V3 p11, V3 p01, V3 n, double t, (byte, byte, byte) col)
    {
        var mid = (p00 + p10 + p11 + p01) * 0.25 - n * (t / 2);
        void Q(V3 a, V3 b, V3 cc, V3 d, double shade)
        {
            var nn = (b - a).Cross(cc - a);
            var cen = (a + b + cc + d) * 0.25;
            if (nn.Dot(cen - mid) < 0)
                m.Quad(a.T, d.T, cc.T, b.T, Tone(col, shade));
            else
                m.Quad(a.T, b.T, cc.T, d.T, Tone(col, shade));
        }
        var q00 = p00 - n * t; var q10 = p10 - n * t; var q11 = p11 - n * t; var q01 = p01 - n * t;
        Q(p00, p10, p11, p01, 1.0);
        Q(p00, q00, q10, p10, 0.8);
        Q(p10, q10, q11, p11, 0.85);
        Q(p11, q11, q01, p01, 0.8);
        Q(p01, q01, q00, p00, 0.85);
    }

    // ---- C: truss frames ------------------------------------------------------------------------------------------------------

    private static void VoidTruss(VoidCtx c)
    {
        var chord = DesignMesh.Hex("#555b61");
        var lace = DesignMesh.Hex("#6d7379");
        var keel = DesignMesh.Hex("#4a4f54");
        const double depth = 0.45, off = 0.04;
        var ringsAt = new List<(double Z, List<(V3 P, V3 N)>[] R)>();
        foreach (var z in VoidStations(c.H, 2.0))
        {
            var ring = VoidRing(c, z, 0.75);
            ringsAt.Add((z, ring));
            foreach (var side in ring)
                for (var i = 0; i + 1 < side.Count; i++)
                {
                    var (p0, n0) = side[i];
                    var (p1, n1) = side[i + 1];
                    var nm = (n0 + n1).Unit;
                    var zAxis = new V3(0, 0, 1);
                    var o0 = p0 + n0 * (off + 0.04);
                    var o1 = p1 + n1 * (off + 0.04);
                    var i0 = p0 + n0 * (off + depth);
                    var i1 = p1 + n1 * (off + depth);
                    Member(c, o0, o1, zAxis, 0.05, 0.04, chord);           // outer chord on the skin
                    Member(c, i0, i1, zAxis, 0.04, 0.04, chord);           // inner chord
                    Member(c, i % 2 == 0 ? o0 : i0, i % 2 == 0 ? i1 : o1, zAxis, 0.025, 0.025, lace, 0.15); // the diagonal (Warren)
                    if (i == 0)
                        Member(c, o0, i0, zAxis, 0.025, 0.025, lace, 0.15);
                    Member(c, o1, i1, zAxis, 0.025, 0.025, lace, 0.15);   // the post
                }
        }
        // Longitudinals: a heavy box beam along the inner chords' corners, every other sample, station to station.
        for (var k = 0; k + 1 < ringsAt.Count; k++)
            for (var si = 0; si < 2; si++)
            {
                var (a, b) = (ringsAt[k].R[si], ringsAt[k + 1].R[si]);
                if (a.Count != b.Count)
                    continue;
                for (var i = 1; i + 1 < a.Count; i += 2)
                {
                    var pa = a[i].P + a[i].N * (off + depth);
                    var pb = b[i].P + b[i].N * (off + depth);
                    Member(c, pa, pb, (a[i].N + b[i].N).Unit, 0.045, 0.045, keel, 0.3);
                }
            }
    }

    // ---- D: service runs on light frames ---------------------------------------------------------------------------------------

    private static void VoidServices(VoidCtx c)
    {
        var frame = DesignMesh.Hex("#4e545a");
        var bracket = DesignMesh.Hex("#3c4146");
        var tray = DesignMesh.Hex("#8b8f91");
        (string Col, double R)[] pipes =
        [
            ("#c9c4b6", 0.07), // lagged coolant
            ("#c9c4b6", 0.07),
            ("#3d6e8f", 0.04), // water
            ("#b8902a", 0.035), // gas
            ("#8c2f2a", 0.045), // fire main
        ];
        const double skinOff = 0.03, web = 0.14;
        foreach (var z in VoidStations(c.H, 1.5))
            foreach (var side in VoidRing(c, z, 0.6))
                for (var i = 0; i + 1 < side.Count; i++)
                {
                    var (p0, n0) = side[i];
                    var (p1, n1) = side[i + 1];
                    Member(c, p0 + n0 * (skinOff + web / 2), p1 + n1 * (skinOff + web / 2), (n0 + n1).Unit, web / 2, 0.025, frame, 0.3);
                }
        // Runs along the hull at a few heights round each side: a pipe bundle (t 0.42), a cable tray (0.55), another bundle (0.72).
        var zs = new List<double>();
        for (var z = c.H.Front + 0.05; z < c.H.Back - 0.05; z += 1.5)
            zs.Add(z);
        zs.Add(c.H.Back - 0.05);
        for (var s = -1; s <= 1; s += 2)
        {
            foreach (var (t, kind) in new[] { (0.40, 0), (0.55, 1), (0.70, 0), (0.22, 1) })
                for (var i = 0; i + 1 < zs.Count; i++)
                {
                    var (ax, ay, az, anx, any) = c.H.OnSkin(zs[i], t, s);
                    var (bx, by, bz, bnx, bny) = c.H.OnSkin(zs[i + 1], t, s);
                    var na = new V3(-anx, -any, 0);
                    var nb = new V3(-bnx, -bny, 0);
                    var ta = new V3(-na.Y, na.X, 0); // along the arc
                    var tb = new V3(-nb.Y, nb.X, 0);
                    var pa = new V3(c.Cx + ax, c.H.Y + ay, c.H.Z + az);
                    var pb = new V3(c.Cx + bx, c.H.Y + by, c.H.Z + bz);
                    var stand = skinOff + web + 0.04;
                    if (kind == 0)
                    {
                        var across = 0.0;
                        foreach (var (col, r) in pipes)
                        {
                            var oa = pa + na * (stand + 0.07) + ta * (across - 0.25);
                            var ob = pb + nb * (stand + 0.07) + tb * (across - 0.25);
                            Pipe(c, oa, ob, r, DesignMesh.Hex(col));
                            across += 2 * r + 0.035;
                        }
                        // The hanger at the station: a strap across the bundle, back to the frame.
                        var hp = pa + na * (stand + 0.07) + ta * -0.02;
                        OBox(c.M, hp - ta * 0.3, hp + ta * 0.32, na, 0.09, 0.02, bracket);
                    }
                    else
                    {
                        // A cable tray: a galvanised U (floor and two lips) with a black and an orange bundle in it.
                        var oa = pa + na * stand;
                        var ob = pb + nb * stand;
                        var nn = (na + nb).Unit;
                        var tt = (ta + tb).Unit;
                        Member(c, oa, ob, nn, 0.006, 0.16, tray, 0.3);
                        Member(c, oa + tt * 0.16 + nn * 0.04, ob + tt * 0.16 + nn * 0.04, tt, 0.006, 0.04, tray, 0.3);
                        Member(c, oa - tt * 0.16 + nn * 0.04, ob - tt * 0.16 + nn * 0.04, tt, 0.006, 0.04, tray, 0.3);
                        Member(c, oa + nn * 0.035 - tt * 0.07, ob + nn * 0.035 - tt * 0.07, nn, 0.03, 0.07, DesignMesh.Hex("#26282a"), 0.3);
                        Member(c, oa + nn * 0.03 + tt * 0.07, ob + nn * 0.03 + tt * 0.07, nn, 0.025, 0.06, DesignMesh.Hex("#b4561f"), 0.3);
                    }
                }
        }
    }

    /// <summary>A pipe a to b, six sides, cut where it would enter a room.</summary>
    private static void Pipe(VoidCtx c, V3 a, V3 b, double r, (byte, byte, byte) col)
    {
        var len = (b - a).Len;
        if (len < 0.02)
            return;
        var n = Math.Max(1, (int)Math.Ceiling(len / 0.3));
        var run = -1;
        for (var i = 0; i <= n + 1; i++)
        {
            var ok = i <= n && c.Clear(a + (b - a) * ((double)i / n), r + 0.04);
            if (ok && run < 0)
                run = i;
            else if (!ok && run >= 0)
            {
                if (i - 1 > run)
                    VPrism(c.M, a + (b - a) * ((double)run / n), a + (b - a) * ((double)(i - 1) / n), r, col);
                run = -1;
            }
        }
    }

    private static void VPrism(DesignMesh m, V3 a, V3 b, double r, (byte, byte, byte) col)
    {
        var e = (b - a).Unit;
        var u = (Math.Abs(e.Y) < 0.9 ? new V3(0, 1, 0) : new V3(1, 0, 0));
        u = (u - e * u.Dot(e)).Unit;
        var v = e.Cross(u);
        const int sides = 6;
        for (var k = 0; k < sides; k++)
        {
            double a0 = 2 * Math.PI * k / sides, a1 = 2 * Math.PI * (k + 1) / sides;
            var d0 = u * Math.Cos(a0) + v * Math.Sin(a0);
            var d1 = u * Math.Cos(a1) + v * Math.Sin(a1);
            var shade = 0.75 + 0.25 * (0.5 + 0.5 * ((d0 + d1).Unit.Y));
            var p0 = a + d0 * r; var p1 = a + d1 * r; var q1 = b + d1 * r; var q0 = b + d0 * r;
            var nn = (p1 - p0).Cross(q1 - p0);
            if (nn.Dot(d0 + d1) < 0)
                m.Quad(p0.T, q0.T, q1.T, p1.T, Tone(col, shade));
            else
                m.Quad(p0.T, p1.T, q1.T, q0.T, Tone(col, shade));
        }
    }
}
