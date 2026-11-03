using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One point of a contact manifold: where, the normal from the B side into the A side, the gap along it (below 0: overlap).
/// </summary>
internal struct CPoint
{
    public Vector3 P, N;
    public float Gap;
    public int LeafA, LeafB; // (filled in by the caller)
}

/// <summary>
/// The narrow phase (docs/sandbox/CONSTRUCTS.md, "Compound collision"): the separating-axis test between two oriented
/// boxes (the three face axes of each and the nine edge crosses), or two convex hulls, or a piece and a triangle, with a
/// contact manifold: a face clipped against a face gives up to four points, an edge against an edge one. A pair that is
/// apart by no more than <c>margin</c> still gives points, with a gap above 0 (speculative: the solver lets it close up to
/// that gap and no further). Writes to a caller's span; no allocation.
/// </summary>
internal static class ConstructNarrow
{
    /// <summary>
    /// How low a side face may stand against what rests on a level top (metres): a box sliding on a floor of slabs meets each
    /// next slab's side at floor height, and must ride over it, not stop. The same rule rides a crate over a 3 cm kerb.
    /// </summary>
    public const float Ledge = 0.03f;

    private const float EdgeBias = 0.01f; // an edge axis must beat the faces by this much (faces give stabler manifolds)
    public const int MaxPoints = 4;

    /// <summary>Box against box: points into <paramref name="into"/> at <paramref name="n"/>; returns the new count. The normal runs from <paramref name="b"/> into <paramref name="a"/>.</summary>
    public static int BoxBox(in WBox a, in WBox b, float margin, Span<CPoint> into, int n)
    {
        var d = b.C - a.C;
        // The turn from b's axes to a's.
        float r00 = Vector3.Dot(a.X, b.X), r01 = Vector3.Dot(a.X, b.Y), r02 = Vector3.Dot(a.X, b.Z);
        float r10 = Vector3.Dot(a.Y, b.X), r11 = Vector3.Dot(a.Y, b.Y), r12 = Vector3.Dot(a.Y, b.Z);
        float r20 = Vector3.Dot(a.Z, b.X), r21 = Vector3.Dot(a.Z, b.Y), r22 = Vector3.Dot(a.Z, b.Z);
        const float Fudge = 1e-5f;
        float a00 = MathF.Abs(r00) + Fudge, a01 = MathF.Abs(r01) + Fudge, a02 = MathF.Abs(r02) + Fudge;
        float a10 = MathF.Abs(r10) + Fudge, a11 = MathF.Abs(r11) + Fudge, a12 = MathF.Abs(r12) + Fudge;
        float a20 = MathF.Abs(r20) + Fudge, a21 = MathF.Abs(r21) + Fudge, a22 = MathF.Abs(r22) + Fudge;
        Vector3 ah = a.H, bh = b.H;
        float dax = Vector3.Dot(d, a.X), day = Vector3.Dot(d, a.Y), daz = Vector3.Dot(d, a.Z);
        // Face axes of a.
        var sa0 = MathF.Abs(dax) - (ah.X + bh.X * a00 + bh.Y * a01 + bh.Z * a02);
        if (sa0 > margin)
            return n;
        var sa1 = MathF.Abs(day) - (ah.Y + bh.X * a10 + bh.Y * a11 + bh.Z * a12);
        if (sa1 > margin)
            return n;
        var sa2 = MathF.Abs(daz) - (ah.Z + bh.X * a20 + bh.Y * a21 + bh.Z * a22);
        if (sa2 > margin)
            return n;
        // Face axes of b.
        float dbx = Vector3.Dot(d, b.X), dby = Vector3.Dot(d, b.Y), dbz = Vector3.Dot(d, b.Z);
        var sb0 = MathF.Abs(dbx) - (bh.X + ah.X * a00 + ah.Y * a10 + ah.Z * a20);
        if (sb0 > margin)
            return n;
        var sb1 = MathF.Abs(dby) - (bh.Y + ah.X * a01 + ah.Y * a11 + ah.Z * a21);
        if (sb1 > margin)
            return n;
        var sb2 = MathF.Abs(dbz) - (bh.Z + ah.X * a02 + ah.Y * a12 + ah.Z * a22);
        if (sb2 > margin)
            return n;
        // The best face: the one the boxes are furthest apart along (a's first, on a tie).
        var best = sa0;
        var code = 0;
        if (sa1 > best)
            (best, code) = (sa1, 1);
        if (sa2 > best)
            (best, code) = (sa2, 2);
        if (sb0 > best + 1e-4f)
            (best, code) = (sb0, 3);
        if (sb1 > best + 1e-4f)
            (best, code) = (sb1, 4);
        if (sb2 > best + 1e-4f)
            (best, code) = (sb2, 5);
        // The level top rule: a side face met at the height of a top the piece rests on or skims isn't a wall (see Ledge).
        var side = code switch { 0 => a.X.Y, 1 => a.Y.Y, 2 => a.Z.Y, 3 => b.X.Y, 4 => b.Y.Y, _ => b.Z.Y };
        var levelled = false;
        if (MathF.Abs(side) < 0.7f)
        {
            var bestUp = float.MinValue;
            var upCode = -1;
            for (var k = 0; k < 6; k++)
            {
                var ax = k switch { 0 => a.X.Y, 1 => a.Y.Y, 2 => a.Z.Y, 3 => b.X.Y, 4 => b.Y.Y, _ => b.Z.Y };
                var s = k switch { 0 => sa0, 1 => sa1, 2 => sa2, 3 => sb0, 4 => sb1, _ => sb2 };
                if (MathF.Abs(ax) > 0.98f && s > bestUp)
                    (bestUp, upCode) = (s, k);
            }
            // (never more than a quarter of the thinner piece's height: a thin plate edge-on is met by the wall, not the rule)
            var heightA = MathF.Abs(a.X.Y) * ah.X + MathF.Abs(a.Y.Y) * ah.Y + MathF.Abs(a.Z.Y) * ah.Z;
            var heightB = MathF.Abs(b.X.Y) * bh.X + MathF.Abs(b.Y.Y) * bh.Y + MathF.Abs(b.Z.Y) * bh.Z;
            if (upCode >= 0 && bestUp > -MathF.Min(Ledge, 0.5f * MathF.Min(heightA, heightB)))
            {
                best = bestUp;
                code = upCode;
                levelled = true;
            }
        }
        if (!levelled)
        {
            // The nine edge axes (each cross product's length divides the separation).
            var edge = -1;
            var bestEdge = best + EdgeBias;
            Span<float> rr = [r00, r01, r02, r10, r11, r12, r20, r21, r22];
            Span<float> ab = [a00, a01, a02, a10, a11, a12, a20, a21, a22];
            for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
            {
                var len2 = 1 - rr[i * 3 + j] * rr[i * 3 + j];
                if (len2 < 1e-3f)
                    continue; // (parallel edges: a face axis stands in)
                int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
                var ta = (i == 0 ? daz : i == 1 ? dax : day) * rr[i1 * 3 + j] - (i == 0 ? day : i == 1 ? daz : dax) * rr[i2 * 3 + j];
                var ra = ah[i1] * ab[i2 * 3 + j] + ah[i2] * ab[i1 * 3 + j];
                var rb = bh[j1] * ab[i * 3 + j2] + bh[j2] * ab[i * 3 + j1];
                var s = (MathF.Abs(ta) - ra - rb) / MathF.Sqrt(len2);
                if (s > margin)
                    return n;
                if (s > bestEdge)
                {
                    bestEdge = s;
                    edge = i * 3 + j;
                }
            }
            if (edge >= 0)
                return EdgeContact(a, b, edge / 3, edge % 3, d, bestEdge, into, n);
        }
        var refA = code < 3;
        ref readonly var rf = ref refA ? ref a : ref b;
        ref readonly var ic = ref refA ? ref b : ref a;
        var axis = refA ? code : code - 3;
        var nf = axis == 0 ? rf.X : axis == 1 ? rf.Y : rf.Z;
        var dir = refA ? d : -d;
        if (Vector3.Dot(nf, dir) < 0)
            nf = -nf; // (out of the reference face, towards the other box)
        return FaceContact(rf, ic, nf, axis, refA, margin, into, n);
    }

    private static int EdgeContact(in WBox a, in WBox b, int i, int j, Vector3 d, float sep, Span<CPoint> into, int n)
    {
        var ea = i == 0 ? a.X : i == 1 ? a.Y : a.Z;
        var eb = j == 0 ? b.X : j == 1 ? b.Y : b.Z;
        var l = Vector3.Normalize(Vector3.Cross(ea, eb));
        if (Vector3.Dot(l, d) < 0)
            l = -l; // (a towards b)
        // The edges: a's furthest along l, b's furthest against it.
        var pa = a.C;
        for (var k = 0; k < 3; k++)
            if (k != i)
            {
                var ax = k == 0 ? a.X : k == 1 ? a.Y : a.Z;
                pa += ax * (a.H[k] * (Vector3.Dot(ax, l) > 0 ? 1 : -1));
            }
        var pb = b.C;
        for (var k = 0; k < 3; k++)
            if (k != j)
            {
                var ax = k == 0 ? b.X : k == 1 ? b.Y : b.Z;
                pb -= ax * (b.H[k] * (Vector3.Dot(ax, l) > 0 ? 1 : -1));
            }
        ClosestLines(pa, ea, a.H[i], pb, eb, b.H[j], out var ca, out var cb);
        if (n < into.Length)
            into[n++] = new CPoint { P = (ca + cb) * 0.5f, N = -l, Gap = sep };
        return n;
    }

    /// <summary>The closest points of two segments, each given by its middle, unit direction and half length.</summary>
    private static void ClosestLines(Vector3 pa, Vector3 ea, float ha, Vector3 pb, Vector3 eb, float hb, out Vector3 ca, out Vector3 cb)
    {
        var w = pa - pb;
        var b = Vector3.Dot(ea, eb);
        var dd = Vector3.Dot(ea, w);
        var e = Vector3.Dot(eb, w);
        var den = 1 - b * b;
        var s = den > 1e-6f ? (b * e - dd) / den : 0f;
        s = Math.Clamp(s, -ha, ha);
        var t = Math.Clamp(b * s + e, -hb, hb);
        s = Math.Clamp(b * t - dd, -ha, ha);
        ca = pa + ea * s;
        cb = pb + eb * t;
    }

    /// <summary>Clips the incident box's face against the reference box's face (both boxes, the reference axis): up to four points kept.</summary>
    private static int FaceContact(in WBox rf, in WBox ic, Vector3 nf, int axis, bool refIsA, float margin, Span<CPoint> into, int n)
    {
        // The incident face: the one most against nf.
        float dx = Vector3.Dot(ic.X, nf), dy = Vector3.Dot(ic.Y, nf), dz = Vector3.Dot(ic.Z, nf);
        float ax = MathF.Abs(dx), ay = MathF.Abs(dy), az = MathF.Abs(dz);
        Vector3 fc, u, v;
        float hu, hv;
        if (ax >= ay && ax >= az)
        {
            fc = ic.C - ic.X * (MathF.Sign(dx) * ic.H.X);
            (u, hu, v, hv) = (ic.Y, ic.H.Y, ic.Z, ic.H.Z);
        }
        else if (ay >= az)
        {
            fc = ic.C - ic.Y * (MathF.Sign(dy) * ic.H.Y);
            (u, hu, v, hv) = (ic.Z, ic.H.Z, ic.X, ic.H.X);
        }
        else
        {
            fc = ic.C - ic.Z * (MathF.Sign(dz) * ic.H.Z);
            (u, hu, v, hv) = (ic.X, ic.H.X, ic.Y, ic.H.Y);
        }
        Span<Vector3> poly = stackalloc Vector3[4];
        poly[0] = fc + u * hu + v * hv;
        poly[1] = fc - u * hu + v * hv;
        poly[2] = fc - u * hu - v * hv;
        poly[3] = fc + u * hu - v * hv;
        // The reference face's two side axes.
        var ru = axis == 0 ? rf.Y : axis == 1 ? rf.Z : rf.X;
        var rv = axis == 0 ? rf.Z : axis == 1 ? rf.X : rf.Y;
        var rhu = axis == 0 ? rf.H.Y : axis == 1 ? rf.H.Z : rf.H.X;
        var rhv = axis == 0 ? rf.H.Z : axis == 1 ? rf.H.X : rf.H.Y;
        var rh = rf.H[axis];
        Span<Vector3> buf = stackalloc Vector3[8];
        Span<Vector3> buf2 = stackalloc Vector3[8];
        // In the reference's coordinates: x and y along the face, z the height over the face.
        var count = 4;
        for (var k = 0; k < 4; k++)
        {
            var o = poly[k] - rf.C;
            buf[k] = new Vector3(Vector3.Dot(o, ru), Vector3.Dot(o, rv), Vector3.Dot(o, nf) - rh);
        }
        count = Clip(buf, count, buf2, 0, rhu, 1);
        count = Clip(buf2, count, buf, 0, rhu, -1);
        count = Clip(buf, count, buf2, 1, rhv, 1);
        count = Clip(buf2, count, buf, 1, rhv, -1);
        // Keep what is within the margin; at most four, the deepest and those spread furthest from it.
        Span<Vector3> keep = stackalloc Vector3[8];
        var m = 0;
        for (var k = 0; k < count; k++)
            if (buf[k].Z <= margin)
                keep[m++] = buf[k];
        if (m > MaxPoints)
            m = Reduce(keep, m);
        var faceC = rf.C + nf * rh;
        var normal = refIsA ? -nf : nf;
        for (var k = 0; k < m && n < into.Length; k++)
        {
            var p = keep[k];
            into[n++] = new CPoint { P = faceC + ru * p.X + rv * p.Y + nf * (p.Z * 0.5f), N = normal, Gap = p.Z };
        }
        return n;
    }

    /// <summary>One side of a clip: keeps the part of a polygon (x, y, height) where <c>sign * axis coordinate &lt;= limit</c>.</summary>
    private static int Clip(Span<Vector3> from, int count, Span<Vector3> to, int axis, float limit, int sign)
    {
        var m = 0;
        for (var k = 0; k < count; k++)
        {
            var a = from[k];
            var b = from[k + 1 == count ? 0 : k + 1];
            var da = sign * a[axis] - limit;
            var db = sign * b[axis] - limit;
            if (da <= 0 && m < to.Length)
                to[m++] = a;
            if ((da < 0 && db > 0 || da > 0 && db < 0) && m < to.Length)
                to[m++] = a + (b - a) * (da / (da - db));
        }
        return m;
    }

    /// <summary>Four of a clipped polygon's points (x, y, height): the deepest, the furthest from it, and the furthest either side of the line between.</summary>
    private static int Reduce(Span<Vector3> p, int n)
    {
        var deep = 0;
        for (var k = 1; k < n; k++)
            if (p[k].Z < p[deep].Z)
                deep = k;
        (p[0], p[deep]) = (p[deep], p[0]);
        var far = 1;
        var best = -1f;
        for (var k = 1; k < n; k++)
        {
            var d = Dist2(p[k], p[0]);
            if (d > best)
                (best, far) = (d, k);
        }
        (p[1], p[far]) = (p[far], p[1]);
        float ex = p[1].X - p[0].X, ey = p[1].Y - p[0].Y;
        int hi = -1, lo = -1;
        float bh = 1e-6f, bl = -1e-6f;
        for (var k = 2; k < n; k++)
        {
            var s = ex * (p[k].Y - p[0].Y) - ey * (p[k].X - p[0].X);
            if (s > bh)
                (bh, hi) = (s, k);
            if (s < bl)
                (bl, lo) = (s, k);
        }
        var m = 2;
        Vector3 ph = hi >= 0 ? p[hi] : default, pl = lo >= 0 ? p[lo] : default;
        if (hi >= 0)
            p[m++] = ph;
        if (lo >= 0)
            p[m++] = pl;
        return m;
    }

    private static float Dist2(Vector3 a, Vector3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    // ---- Convex hulls ---------------------------------------------------------------------------------------------

    /// <summary>A piece's corners and faces in the world, for the hull test (a box as eight corners and six faces).</summary>
    internal sealed class Shape
    {
        public readonly Vector3[] V = new Vector3[ConvexHull.MaxCorners];
        public readonly Vector3[] N = new Vector3[ConvexHull.MaxCorners + 2];
        public readonly Vector3[] Dirs = new Vector3[ConvexHull.MaxCorners * 2];
        public int[] FaceStart = [], FaceIdx = [];
        public int VCount, FCount, DCount;
        public Vector3 Centre;

        private static readonly int[] BoxStart = [0, 4, 8, 12, 16, 20, 24];
        private static readonly int[] BoxIdx = [1, 3, 7, 5, /* +x */ 0, 4, 6, 2, /* -x */ 2, 6, 7, 3, /* +y */ 0, 1, 5, 4, /* -y */ 4, 5, 7, 6, /* +z */ 0, 2, 3, 1 /* -z */];

        public void FromBox(in WBox b)
        {
            for (var k = 0; k < 8; k++)
                V[k] = b.C + b.X * ((k & 1) != 0 ? b.H.X : -b.H.X) + b.Y * ((k & 2) != 0 ? b.H.Y : -b.H.Y) + b.Z * ((k & 4) != 0 ? b.H.Z : -b.H.Z);
            (N[0], N[1], N[2], N[3], N[4], N[5]) = (b.X, -b.X, b.Y, -b.Y, b.Z, -b.Z);
            (Dirs[0], Dirs[1], Dirs[2]) = (b.X, b.Y, b.Z);
            FaceStart = BoxStart;
            FaceIdx = BoxIdx;
            VCount = 8;
            FCount = 6;
            DCount = 3;
            Centre = b.C;
        }

        public void FromHull(ConvexHull h, in CFrame f)
        {
            VCount = h.V.Length;
            FCount = h.N.Length;
            DCount = h.Dirs.Length;
            var c = Vector3.Zero;
            for (var k = 0; k < VCount; k++)
            {
                V[k] = f.Point(h.V[k]);
                c += V[k];
            }
            Centre = c / VCount;
            for (var k = 0; k < FCount; k++)
                N[k] = f.Dir(h.N[k]);
            for (var k = 0; k < DCount; k++)
                Dirs[k] = f.Dir(h.Dirs[k]);
            FaceStart = h.FaceStart;
            FaceIdx = h.FaceIdx;
        }

        public (float, float) Span(Vector3 axis)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (var k = 0; k < VCount; k++)
            {
                var d = Vector3.Dot(axis, V[k]);
                lo = MathF.Min(lo, d);
                hi = MathF.Max(hi, d);
            }
            return (lo, hi);
        }
    }

    /// <summary>The largest separation along a's face normals of b, and the face.</summary>
    private static float FaceSep(Shape a, Shape b, float margin, out int face)
    {
        var best = float.MinValue;
        face = 0;
        for (var f = 0; f < a.FCount; f++)
        {
            var nv = a.N[f];
            var plane = Vector3.Dot(nv, a.V[a.FaceIdx[a.FaceStart[f]]]);
            var low = float.MaxValue;
            for (var k = 0; k < b.VCount; k++)
                low = MathF.Min(low, Vector3.Dot(nv, b.V[k]));
            var s = low - plane;
            if (s > best)
                (best, face) = (s, f);
            if (s > margin)
                return s;
        }
        return best;
    }

    /// <summary>Hull against hull (either may be a box): the same rules as <see cref="BoxBox"/>, for shapes with any faces. The normal runs from b into a.</summary>
    public static int HullHull(Shape a, Shape b, float margin, Span<CPoint> into, int n)
    {
        var sa = FaceSep(a, b, margin, out var fa);
        if (sa > margin)
            return n;
        var sb = FaceSep(b, a, margin, out var fb);
        if (sb > margin)
            return n;
        var best = MathF.Max(sa, sb);
        var bestEdge = best + EdgeBias;
        var axis = Vector3.Zero;
        var towards = b.Centre - a.Centre;
        for (var i = 0; i < a.DCount; i++)
        for (var j = 0; j < b.DCount; j++)
        {
            var l = Vector3.Cross(a.Dirs[i], b.Dirs[j]);
            var len2 = l.LengthSquared();
            if (len2 < 1e-3f)
                continue;
            l /= MathF.Sqrt(len2);
            if (Vector3.Dot(l, towards) < 0)
                l = -l;
            var (_, ha) = a.Span(l);
            var (lb, _) = b.Span(l);
            var s = lb - ha;
            if (s > margin)
                return n;
            if (s > bestEdge)
                (bestEdge, axis) = (s, l);
        }
        if (axis != Vector3.Zero)
        {
            Support(a, axis, out var pa, out var ea, out var la);
            Support(b, -axis, out var pb, out var eb, out var lb);
            ClosestLines(pa, ea, la, pb, eb, lb, out var ca, out var cb);
            if (n < into.Length)
                into[n++] = new CPoint { P = (ca + cb) * 0.5f, N = -axis, Gap = bestEdge };
            return n;
        }
        return sa >= sb - 1e-4f ? HullFace(a, fa, b, true, margin, into, n) : HullFace(b, fb, a, false, margin, into, n);
    }

    /// <summary>The middle, direction and half length of what lies furthest along <paramref name="d"/> (an edge, or a corner as a zero-length one).</summary>
    private static void Support(Shape s, Vector3 d, out Vector3 mid, out Vector3 dir, out float half)
    {
        var top = float.MinValue;
        for (var k = 0; k < s.VCount; k++)
            top = MathF.Max(top, Vector3.Dot(d, s.V[k]));
        var first = Vector3.Zero;
        var last = Vector3.Zero;
        var any = false;
        var far = -1f;
        for (var k = 0; k < s.VCount; k++)
        {
            if (Vector3.Dot(d, s.V[k]) < top - 1e-3f)
                continue;
            if (!any)
            {
                first = last = s.V[k];
                any = true;
            }
            else if (Vector3.DistanceSquared(first, s.V[k]) > far)
            {
                far = Vector3.DistanceSquared(first, s.V[k]);
                last = s.V[k];
            }
        }
        mid = (first + last) * 0.5f;
        var e = last - first;
        half = e.Length() * 0.5f;
        dir = half > 1e-5f ? e / (half * 2) : Vector3.UnitX;
    }

    private static int HullFace(Shape rf, int face, Shape ic, bool refIsA, float margin, Span<CPoint> into, int n)
    {
        var nf = rf.N[face];
        // The incident face: the most against nf.
        var inc = 0;
        var low = float.MaxValue;
        for (var f = 0; f < ic.FCount; f++)
        {
            var d = Vector3.Dot(ic.N[f], nf);
            if (d < low)
                (low, inc) = (d, f);
        }
        Span<Vector3> a = stackalloc Vector3[64];
        Span<Vector3> b = stackalloc Vector3[64];
        var count = 0;
        for (var k = ic.FaceStart[inc]; k < ic.FaceStart[inc + 1] && count < 32; k++)
            a[count++] = ic.V[ic.FaceIdx[k]];
        int s0 = rf.FaceStart[face], s1 = rf.FaceStart[face + 1];
        var mid = Vector3.Zero;
        for (var k = s0; k < s1; k++)
            mid += rf.V[rf.FaceIdx[k]];
        mid /= s1 - s0;
        for (var k = s0; k < s1 && count > 0; k++)
        {
            var e0 = rf.V[rf.FaceIdx[k]];
            var e1 = rf.V[rf.FaceIdx[k + 1 == s1 ? s0 : k + 1]];
            var side = Vector3.Cross(e1 - e0, nf);
            if (Vector3.Dot(side, mid - e0) > 0)
                side = -side; // (out of the face)
            count = ClipPlane(a, count, b, side, e0);
            b[..count].CopyTo(a);
        }
        var plane = Vector3.Dot(nf, rf.V[rf.FaceIdx[s0]]);
        var normal = refIsA ? -nf : nf;
        return Keep(a, count, nf, plane, normal, margin, into, n);
    }

    /// <summary>Keeps the part of a polygon on the inner side of a plane through <paramref name="at"/> with outward <paramref name="side"/>.</summary>
    private static int ClipPlane(Span<Vector3> from, int count, Span<Vector3> to, Vector3 side, Vector3 at)
    {
        var m = 0;
        for (var k = 0; k < count; k++)
        {
            var p = from[k];
            var q = from[k + 1 == count ? 0 : k + 1];
            var dp = Vector3.Dot(side, p - at);
            var dq = Vector3.Dot(side, q - at);
            if (dp <= 0 && m < to.Length)
                to[m++] = p;
            if ((dp < 0 && dq > 0 || dp > 0 && dq < 0) && m < to.Length)
                to[m++] = p + (q - p) * (dp / (dp - dq));
        }
        return m;
    }

    /// <summary>A clipped world polygon's points within the margin of a plane, at most four.</summary>
    private static int Keep(Span<Vector3> poly, int count, Vector3 nf, float plane, Vector3 normal, float margin, Span<CPoint> into, int n)
    {
        // In the plane's coordinates, to pick the spread.
        var u = Vector3.Normalize(MathF.Abs(nf.X) < 0.9f ? Vector3.Cross(nf, Vector3.UnitX) : Vector3.Cross(nf, Vector3.UnitZ));
        var v = Vector3.Cross(nf, u);
        Span<Vector3> keep = stackalloc Vector3[64];
        var m = 0;
        for (var k = 0; k < count && m < keep.Length; k++)
        {
            var h = Vector3.Dot(nf, poly[k]) - plane;
            if (h <= margin)
                keep[m++] = new Vector3(Vector3.Dot(poly[k], u), Vector3.Dot(poly[k], v), h);
        }
        if (m > MaxPoints)
            m = Reduce(keep, m);
        for (var k = 0; k < m && n < into.Length; k++)
        {
            var p = keep[k];
            into[n++] = new CPoint { P = u * p.X + v * p.Y + nf * (plane + p.Z * 0.5f), N = normal, Gap = p.Z };
        }
        return n;
    }

    // ---- A piece against a triangle ---------------------------------------------------------------------------------

    /// <summary>
    /// A piece against one triangle of the ground or a mesh. A triangle isn't a solid, it's a sheet: the push is always along its
    /// own normal (a neighbour's edge never catches a sliding box). The piece's face turned most against the normal, clipped to
    /// the prism over the triangle, gives the points, so a plate lying across a ridge rests on the ridge line from both
    /// sides, however far apart its corners are. <paramref name="shape"/> holds the piece. The normal runs into the piece.
    /// </summary>
    public static int PieceTriangle(Shape shape, Vector3 ta, Vector3 tb, Vector3 tc, Vector3 nt, float margin, float deepest, Span<CPoint> into, int n)
    {
        var plane = Vector3.Dot(nt, ta);
        var (lo, hi) = shape.Span(nt);
        if (lo - plane > margin || hi - plane < -deepest)
            return n;
        var inc = 0;
        var low = float.MaxValue;
        for (var f = 0; f < shape.FCount; f++)
        {
            var d = Vector3.Dot(shape.N[f], nt);
            if (d < low)
                (low, inc) = (d, f);
        }
        Span<Vector3> a = stackalloc Vector3[64];
        Span<Vector3> b = stackalloc Vector3[64];
        var count = 0;
        for (var k = shape.FaceStart[inc]; k < shape.FaceStart[inc + 1] && count < 32; k++)
            a[count++] = shape.V[shape.FaceIdx[k]];
        Vector3 e0 = ta, e1 = tb, far = tc;
        for (var side = 0; side < 3 && count > 0; side++)
        {
            var o = Vector3.Cross(e1 - e0, nt);
            if (Vector3.Dot(o, far - e0) > 0)
                o = -o;
            count = ClipPlane(a, count, b, o, e0);
            b[..count].CopyTo(a);
            (e0, e1, far) = (e1, far, e0);
        }
        return Keep(a, count, nt, plane, nt, margin, into, n);
    }

    /// <summary>Are the two pieces overlapped by more than <paramref name="skin"/>? (The physics gun's step test: the faces and edges, no manifold.)</summary>
    public static bool Overlapping(in WBox a, in WBox b, float skin)
    {
        var d = b.C - a.C;
        float r00 = Vector3.Dot(a.X, b.X), r01 = Vector3.Dot(a.X, b.Y), r02 = Vector3.Dot(a.X, b.Z);
        float r10 = Vector3.Dot(a.Y, b.X), r11 = Vector3.Dot(a.Y, b.Y), r12 = Vector3.Dot(a.Y, b.Z);
        float r20 = Vector3.Dot(a.Z, b.X), r21 = Vector3.Dot(a.Z, b.Y), r22 = Vector3.Dot(a.Z, b.Z);
        const float Fudge = 1e-5f;
        float a00 = MathF.Abs(r00) + Fudge, a01 = MathF.Abs(r01) + Fudge, a02 = MathF.Abs(r02) + Fudge;
        float a10 = MathF.Abs(r10) + Fudge, a11 = MathF.Abs(r11) + Fudge, a12 = MathF.Abs(r12) + Fudge;
        float a20 = MathF.Abs(r20) + Fudge, a21 = MathF.Abs(r21) + Fudge, a22 = MathF.Abs(r22) + Fudge;
        Vector3 ah = a.H, bh = b.H;
        float dax = Vector3.Dot(d, a.X), day = Vector3.Dot(d, a.Y), daz = Vector3.Dot(d, a.Z);
        if (MathF.Abs(dax) - (ah.X + bh.X * a00 + bh.Y * a01 + bh.Z * a02) > -skin
            || MathF.Abs(day) - (ah.Y + bh.X * a10 + bh.Y * a11 + bh.Z * a12) > -skin
            || MathF.Abs(daz) - (ah.Z + bh.X * a20 + bh.Y * a21 + bh.Z * a22) > -skin
            || MathF.Abs(Vector3.Dot(d, b.X)) - (bh.X + ah.X * a00 + ah.Y * a10 + ah.Z * a20) > -skin
            || MathF.Abs(Vector3.Dot(d, b.Y)) - (bh.Y + ah.X * a01 + ah.Y * a11 + ah.Z * a21) > -skin
            || MathF.Abs(Vector3.Dot(d, b.Z)) - (bh.Z + ah.X * a02 + ah.Y * a12 + ah.Z * a22) > -skin)
            return false;
        Span<float> rr = [r00, r01, r02, r10, r11, r12, r20, r21, r22];
        Span<float> ab = [a00, a01, a02, a10, a11, a12, a20, a21, a22];
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
        {
            var len2 = 1 - rr[i * 3 + j] * rr[i * 3 + j];
            if (len2 < 1e-3f)
                continue;
            int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
            var ta = (i == 0 ? daz : i == 1 ? dax : day) * rr[i1 * 3 + j] - (i == 0 ? day : i == 1 ? daz : dax) * rr[i2 * 3 + j];
            var ra = ah[i1] * ab[i2 * 3 + j] + ah[i2] * ab[i1 * 3 + j];
            var rb = bh[j1] * ab[i * 3 + j2] + bh[j2] * ab[i * 3 + j1];
            if ((MathF.Abs(ta) - ra - rb) / MathF.Sqrt(len2) > -skin)
                return false;
        }
        return true;
    }
}
