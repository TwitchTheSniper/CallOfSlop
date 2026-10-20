using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Pure geometry used by the physics world and the movers: ray tests, closest points and distances between a capsule's core
/// segment and boxes or triangles. No state, no allocation, plain float maths (deterministic for the same inputs).
/// </summary>
public static class Geometry
{
    /// <summary>Directions shorter than this along an axis count as parallel to it (the same rule as <see cref="Box.Raycast"/>).</summary>
    public const float ParallelEpsilon = 1e-6f;

    // ---- Rays ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Outward normal of the face a ray enters an axis-aligned box through, for a hit at <paramref name="t"/> (from
    /// <see cref="Box.Raycast"/>). A ray starting inside (t = 0) gets the reverse of its direction.
    /// </summary>
    public static Vector3 BoxEntryNormal(Vector3 min, Vector3 max, Vector3 origin, Vector3 dir, float t)
    {
        var best = float.NegativeInfinity;
        var axis = -1;
        for (var a = 0; a < 3; a++)
        {
            var d = dir[a];
            if (MathF.Abs(d) < ParallelEpsilon)
                continue;
            var near = ((d > 0 ? min[a] : max[a]) - origin[a]) / d;
            if (near > best)
            {
                best = near;
                axis = a;
            }
        }
        if (axis < 0 || t <= 0 || best < 0)
            return dir.LengthSquared() > 0 ? -Vector3.Normalize(dir) : Vector3.UnitY;
        return Axis(axis, dir[axis] > 0 ? -1 : 1);
    }

    /// <summary>Ray against an oriented box: distance along the unit ray or null (the axis-aligned test in box coordinates).</summary>
    public static float? RayObb(in Obb b, Vector3 origin, Vector3 dir, float maxDist) =>
        new Box(Vector3.Zero, b.Half).Raycast(b.ToLocal(origin), b.DirToLocal(dir), maxDist);

    /// <summary>World normal where a ray enters an oriented box at distance <paramref name="t"/>.</summary>
    public static Vector3 ObbEntryNormal(in Obb b, Vector3 origin, Vector3 dir, float t) =>
        b.DirToWorld(BoxEntryNormal(-b.Half, b.Half, b.ToLocal(origin), b.DirToLocal(dir), t));

    /// <summary>Two-sided ray-triangle test (Möller-Trumbore): distance along the unit ray, or null.</summary>
    public static float? RayTriangle(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, float maxDist)
    {
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(dir, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < 1e-12f)
            return null;
        var inv = 1f / det;
        var s = origin - a;
        var u = Vector3.Dot(s, p) * inv;
        if (u < 0 || u > 1)
            return null;
        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(dir, q) * inv;
        if (v < 0 || u + v > 1)
            return null;
        var t = Vector3.Dot(e2, q) * inv;
        return t >= 0 && t <= maxDist ? t : null;
    }

    /// <summary>Unit normal of a triangle (counter-clockwise when seen from the front), or up for a degenerate one.</summary>
    public static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var n = Vector3.Cross(b - a, c - a);
        var len = n.Length();
        return len > 1e-12f ? n / len : Vector3.UnitY;
    }

    // ---- Closest points ------------------------------------------------------------------------------------

    /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
    public static Vector3 ClosestPointTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
            return a;
        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
            return b;
        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return a + ab * (d1 / (d1 - d3));
        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
            return c;
        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return a + ac * (d2 / (d2 - d6));
        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        var denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>
    /// Closest points between segments p1q1 and p2q2 (Ericson 5.1.9). Returns the squared distance; c1 and c2 are the
    /// points. Handles zero-length segments.
    /// </summary>
    public static float ClosestSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s, t;
        const float eps = 1e-12f;
        if (a <= eps && e <= eps)
        {
            c1 = p1;
            c2 = p2;
            return Vector3.DistanceSquared(c1, c2);
        }
        if (a <= eps)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= eps)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denom = a * e - b * b;
                s = denom > eps * a * e ? Math.Clamp((b * f - c * e) / denom, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0, 1);
                }
            }
        }
        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
        return Vector3.DistanceSquared(c1, c2);
    }

    /// <summary>
    /// Closest points between segment pq and triangle abc. Returns the squared distance (0 if the segment passes through
    /// the triangle); onSeg and onTri are the points.
    /// </summary>
    public static float ClosestSegmentTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, out Vector3 onSeg, out Vector3 onTri)
    {
        // Crossing the triangle's plane inside the triangle: distance 0.
        var n = Vector3.Cross(b - a, c - a);
        var dp = Vector3.Dot(p - a, n);
        var dq = Vector3.Dot(q - a, n);
        if ((dp <= 0 && dq >= 0) || (dp >= 0 && dq <= 0))
        {
            var denom = dp - dq;
            if (MathF.Abs(denom) > 1e-20f)
            {
                var x = p + (q - p) * (dp / denom);
                if (InsideTriangle(x, a, b, c, n))
                {
                    onSeg = onTri = x;
                    return 0;
                }
            }
        }
        // Otherwise the closest pair involves an end of the segment or an edge of the triangle.
        var cp = ClosestPointTriangle(p, a, b, c);
        var best = Vector3.DistanceSquared(p, cp);
        onSeg = p;
        onTri = cp;
        var cq = ClosestPointTriangle(q, a, b, c);
        var dq2 = Vector3.DistanceSquared(q, cq);
        if (dq2 < best)
        {
            best = dq2;
            onSeg = q;
            onTri = cq;
        }
        Edge(a, b, ref best, ref onSeg, ref onTri);
        Edge(b, c, ref best, ref onSeg, ref onTri);
        Edge(c, a, ref best, ref onSeg, ref onTri);
        return best;

        void Edge(Vector3 e0, Vector3 e1, ref float bestD, ref Vector3 s, ref Vector3 t)
        {
            var d = ClosestSegmentSegment(p, q, e0, e1, out var c1, out var c2);
            if (d < bestD)
            {
                bestD = d;
                s = c1;
                t = c2;
            }
        }
    }

    private static bool InsideTriangle(Vector3 x, Vector3 a, Vector3 b, Vector3 c, Vector3 n) =>
        Vector3.Dot(Vector3.Cross(b - a, x - a), n) >= 0
        && Vector3.Dot(Vector3.Cross(c - b, x - b), n) >= 0
        && Vector3.Dot(Vector3.Cross(a - c, x - c), n) >= 0;

    /// <summary>Closest point of a box centred at the origin (half size <paramref name="h"/>) to p.</summary>
    public static Vector3 ClampToBox(Vector3 p, Vector3 h) => Vector3.Clamp(p, -h, h);

    /// <summary>
    /// Closest points between segment pq and a box centred at the origin with half size <paramref name="h"/>, all in box
    /// coordinates. Returns the squared distance. Exact and cheap when the segment runs along one of the box's axes (a
    /// standing capsule against an upright box, the usual case); otherwise a golden-section search on the segment, which is
    /// exact up to float precision because the distance to a convex box is convex along a line.
    /// </summary>
    public static float ClosestSegmentBox(Vector3 p, Vector3 q, Vector3 h, out Vector3 onSeg, out Vector3 onBox)
    {
        var d = q - p;
        var ax = MathF.Abs(d.X);
        var ay = MathF.Abs(d.Y);
        var az = MathF.Abs(d.Z);
        var scale = MathF.Max(1e-3f, ax + ay + az);
        var tiny = scale * 1e-6f;
        var axis = -1;
        if (ax <= tiny && az <= tiny)
            axis = 1;
        else if (ay <= tiny && az <= tiny)
            axis = 0;
        else if (ax <= tiny && ay <= tiny)
            axis = 2;
        if (axis >= 0)
            return AlignedSegmentBox(p, q, h, axis, out onSeg, out onBox);

        // General direction: minimise the (convex) squared distance over the segment parameter.
        float lo = 0, hi = 1;
        const float g = 0.618034f;
        var x1 = hi - g * (hi - lo);
        var x2 = lo + g * (hi - lo);
        var f1 = PointBox2(p + d * x1, h);
        var f2 = PointBox2(p + d * x2, h);
        for (var i = 0; i < 40; i++)
        {
            if (f1 <= f2)
            {
                hi = x2;
                x2 = x1;
                f2 = f1;
                x1 = hi - g * (hi - lo);
                f1 = PointBox2(p + d * x1, h);
            }
            else
            {
                lo = x1;
                x1 = x2;
                f1 = f2;
                x2 = lo + g * (hi - lo);
                f2 = PointBox2(p + d * x2, h);
            }
        }
        var s = (lo + hi) * 0.5f;
        // The ends can be the minimum too (golden section never evaluates exactly 0 or 1).
        var best = PointBox2(p + d * s, h);
        var f0 = PointBox2(p, h);
        var fe = PointBox2(q, h);
        if (f0 <= best)
        {
            best = f0;
            s = 0;
        }
        if (fe < best)
        {
            best = fe;
            s = 1;
        }
        onSeg = p + d * s;
        onBox = ClampToBox(onSeg, h);
        return best;
    }

    private static float PointBox2(Vector3 x, Vector3 h) => Vector3.DistanceSquared(x, ClampToBox(x, h));

    /// <summary>Segment pq running along box axis <paramref name="k"/> (its other coordinates are p's): closed form.</summary>
    private static float AlignedSegmentBox(Vector3 p, Vector3 q, Vector3 h, int k, out Vector3 onSeg, out Vector3 onBox)
    {
        var lo = MathF.Min(p[k], q[k]);
        var hi = MathF.Max(p[k], q[k]);
        var hk = h[k];
        float segK, boxK;
        if (hi < -hk)
        {
            segK = hi;
            boxK = -hk;
        }
        else if (lo > hk)
        {
            segK = lo;
            boxK = hk;
        }
        else
        {
            // The intervals overlap: any shared coordinate works; take the middle of the overlap.
            segK = boxK = (MathF.Max(lo, -hk) + MathF.Min(hi, hk)) * 0.5f;
        }
        onSeg = With(p, k, segK);
        onBox = With(ClampToBox(p, h), k, boxK);
        return Vector3.DistanceSquared(onSeg, onBox);
    }

    /// <summary>
    /// When a capsule core segment pq (box coordinates) reaches into a box, the cheapest way out: the box face direction
    /// (box coordinates, unit) and how far the capsule of radius r must move along it to clear the box.
    /// </summary>
    public static Vector3 BoxEscape(Vector3 p, Vector3 q, Vector3 h, float r, out float depth)
    {
        var lo = Vector3.Min(p, q);
        var hi = Vector3.Max(p, q);
        depth = float.MaxValue;
        var n = Vector3.UnitY;
        for (var a = 0; a < 3; a++)
        {
            var up = h[a] - lo[a] + r; // push towards +axis until the lowest point clears the + face
            var down = hi[a] + h[a] + r;
            if (up < depth)
            {
                depth = up;
                n = Axis(a, 1);
            }
            if (down < depth)
            {
                depth = down;
                n = Axis(a, -1);
            }
        }
        return n;
    }

    // ---- Box overlap (separating axes) -----------------------------------------------------------------------

    /// <summary>Do two oriented boxes overlap (touching doesn't count)? Separating-axis test on the 15 candidate axes.</summary>
    public static bool ObbOverlap(in Obb a, in Obb b)
    {
        var d = b.Center - a.Center;
        Span<Vector3> axes = stackalloc Vector3[15];
        axes[0] = a.AxisX;
        axes[1] = a.AxisY;
        axes[2] = a.AxisZ;
        axes[3] = b.AxisX;
        axes[4] = b.AxisY;
        axes[5] = b.AxisZ;
        var k = 6;
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                axes[k++] = Vector3.Cross(axes[i], axes[3 + j]);
        foreach (var l in axes)
        {
            if (l.LengthSquared() < 1e-10f)
                continue; // parallel edges: covered by the face axes
            var ra = a.Half.X * MathF.Abs(Vector3.Dot(a.AxisX, l)) + a.Half.Y * MathF.Abs(Vector3.Dot(a.AxisY, l)) + a.Half.Z * MathF.Abs(Vector3.Dot(a.AxisZ, l));
            var rb = b.Half.X * MathF.Abs(Vector3.Dot(b.AxisX, l)) + b.Half.Y * MathF.Abs(Vector3.Dot(b.AxisY, l)) + b.Half.Z * MathF.Abs(Vector3.Dot(b.AxisZ, l));
            if (MathF.Abs(Vector3.Dot(d, l)) >= ra + rb)
                return false;
        }
        return true;
    }

    /// <summary>Does an oriented box overlap a triangle (touching doesn't count)? Separating axes in box coordinates.</summary>
    public static bool ObbTriangleOverlap(in Obb box, Vector3 a, Vector3 b, Vector3 c)
    {
        var v0 = box.ToLocal(a);
        var v1 = box.ToLocal(b);
        var v2 = box.ToLocal(c);
        var h = box.Half;
        Span<Vector3> edges = [v1 - v0, v2 - v1, v0 - v2];
        Span<Vector3> axes = stackalloc Vector3[13];
        axes[0] = Vector3.UnitX;
        axes[1] = Vector3.UnitY;
        axes[2] = Vector3.UnitZ;
        axes[3] = Vector3.Cross(edges[0], edges[1]);
        var k = 4;
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                axes[k++] = Vector3.Cross(Axis(i, 1), edges[j]);
        foreach (var l in axes)
        {
            if (l.LengthSquared() < 1e-12f)
                continue;
            var p0 = Vector3.Dot(v0, l);
            var p1 = Vector3.Dot(v1, l);
            var p2 = Vector3.Dot(v2, l);
            var r = h.X * MathF.Abs(l.X) + h.Y * MathF.Abs(l.Y) + h.Z * MathF.Abs(l.Z);
            if (MathF.Min(p0, MathF.Min(p1, p2)) >= r || MathF.Max(p0, MathF.Max(p1, p2)) <= -r)
                return false;
        }
        return true;
    }

    /// <summary>A vector with only component <paramref name="axis"/> set.</summary>
    public static Vector3 Axis(int axis, float value) => axis switch { 0 => new(value, 0, 0), 1 => new(0, value, 0), _ => new(0, 0, value) };

    /// <summary><paramref name="v"/> with component <paramref name="axis"/> replaced.</summary>
    public static Vector3 With(Vector3 v, int axis, float value) => axis switch { 0 => v with { X = value }, 1 => v with { Y = value }, _ => v with { Z = value } };
}
