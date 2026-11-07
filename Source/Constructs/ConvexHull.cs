using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A convex collider: its corners, its faces (outward normals, corner lists in order) and its unique edge directions, the
/// three things the separating-axis test and the face clip need (docs/sandbox/CONSTRUCTS.md, "Compound collision"). Made
/// once for a shaped part (<see cref="ConstructHulls"/>); a construct holds copies in its own frames.
/// </summary>
public sealed class ConvexHull
{
    public const int MaxCorners = 32;

    public Vector3[] V = [];
    public Vector3[] N = [];
    public int[] FaceStart = []; // face f's corners are FaceIdx[FaceStart[f]..FaceStart[f + 1]]
    public int[] FaceIdx = [];
    public Vector3[] Dirs = [];

    /// <summary>The hull of <paramref name="points"/> (repeats dropped), or null when it is flat, or has more than <see cref="MaxCorners"/> corners. At load only: it tries every triple.</summary>
    public static ConvexHull? Of(ReadOnlySpan<Vector3> points)
    {
        const float Eps = 1e-4f;
        var pts = new List<Vector3>();
        foreach (var p in points)
        {
            var seen = false;
            foreach (var o in pts)
                seen |= Vector3.DistanceSquared(o, p) < Eps * Eps;
            if (!seen)
                pts.Add(p);
        }
        if (pts.Count < 4)
            return null;
        var centre = Vector3.Zero;
        foreach (var p in pts)
            centre += p;
        centre /= pts.Count;
        var normals = new List<Vector3>();
        var faces = new List<int[]>();
        var used = new bool[pts.Count];
        for (var a = 0; a < pts.Count; a++)
        for (var b = a + 1; b < pts.Count; b++)
        for (var c = b + 1; c < pts.Count; c++)
        {
            var n = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
            if (n.LengthSquared() < 1e-12f)
                continue;
            n = Vector3.Normalize(n);
            if (Vector3.Dot(n, pts[a] - centre) < 0)
                n = -n;
            var d = Vector3.Dot(n, pts[a]);
            var on = new List<int>();
            var outside = false;
            for (var k = 0; k < pts.Count && !outside; k++)
            {
                var s = Vector3.Dot(n, pts[k]) - d;
                outside = s > Eps;
                if (MathF.Abs(s) <= Eps)
                    on.Add(k);
            }
            if (outside)
                continue;
            var dup = false;
            foreach (var m in normals)
                dup |= Vector3.Dot(m, n) > 1 - 1e-6f;
            if (dup)
                continue;
            // Its corners in order round the face (points in the middle of a side stay out: the face's own hull by angle).
            var mid = Vector3.Zero;
            foreach (var k in on)
                mid += pts[k];
            mid /= on.Count;
            var u = Vector3.Normalize(pts[on[0]] - mid);
            var v = Vector3.Cross(n, u);
            on.Sort((x, y) => MathF.Atan2(Vector3.Dot(pts[x] - mid, v), Vector3.Dot(pts[x] - mid, u)).CompareTo(MathF.Atan2(Vector3.Dot(pts[y] - mid, v), Vector3.Dot(pts[y] - mid, u))));
            // Only the face's corners stay: a point inside it (a capped cylinder's centre, from its fan) or along a side
            // would make a notch in the ring, and counted as a corner it took a 16-sided cylinder past MaxCorners.
            for (var changed = true; changed && on.Count > 3;)
            {
                changed = false;
                for (var k = 0; k < on.Count && on.Count > 3; k++)
                {
                    var p = pts[on[k]];
                    var prev = pts[on[(k + on.Count - 1) % on.Count]];
                    var next = pts[on[(k + 1) % on.Count]];
                    if (Vector3.Dot(Vector3.Cross(p - prev, next - p), n) > Eps * Eps)
                        continue;
                    on.RemoveAt(k);
                    changed = true;
                    k--;
                }
            }
            normals.Add(n);
            faces.Add(on.ToArray());
            foreach (var k in on)
                used[k] = true;
        }
        if (faces.Count < 4)
            return null;
        // Only the corners faces use, renumbered.
        var map = new int[pts.Count];
        var kept = new List<Vector3>();
        for (var k = 0; k < pts.Count; k++)
            if (used[k])
            {
                map[k] = kept.Count;
                kept.Add(pts[k]);
            }
        if (kept.Count > MaxCorners)
            return null;
        var h = new ConvexHull { V = kept.ToArray(), N = normals.ToArray(), FaceStart = new int[faces.Count + 1] };
        var idx = new List<int>();
        var dirs = new List<Vector3>();
        for (var f = 0; f < faces.Count; f++)
        {
            h.FaceStart[f] = idx.Count;
            var fc = faces[f];
            for (var k = 0; k < fc.Length; k++)
            {
                idx.Add(map[fc[k]]);
                var e = pts[fc[(k + 1) % fc.Length]] - pts[fc[k]];
                if (e.LengthSquared() < 1e-10f)
                    continue;
                e = Vector3.Normalize(e);
                var dup = false;
                foreach (var o in dirs)
                    dup |= MathF.Abs(Vector3.Dot(o, e)) > 1 - 1e-5f;
                if (!dup)
                    dirs.Add(e);
            }
        }
        h.FaceStart[faces.Count] = idx.Count;
        h.FaceIdx = idx.ToArray();
        h.Dirs = dirs.ToArray();
        return h;
    }

    /// <summary>This hull moved by <c>p => t + q * (scale * p)</c>, into <paramref name="into"/> (reused when it has the same faces; rebuilding a construct allocates nothing the second time).</summary>
    public ConvexHull MovedInto(ConvexHull? into, Quaternion q, Vector3 t, float scale = 1f)
    {
        var h = into != null && ReferenceEquals(into.FaceIdx, FaceIdx)
            ? into
            : new ConvexHull { V = new Vector3[V.Length], N = new Vector3[N.Length], Dirs = new Vector3[Dirs.Length], FaceStart = FaceStart, FaceIdx = FaceIdx };
        for (var k = 0; k < V.Length; k++)
            h.V[k] = t + Vector3.Transform(V[k] * scale, q);
        for (var k = 0; k < N.Length; k++)
            h.N[k] = Vector3.Transform(N[k], q);
        for (var k = 0; k < Dirs.Length; k++)
            h.Dirs[k] = Vector3.Transform(Dirs[k], q);
        return h;
    }

    public void Bounds(out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        foreach (var v in V)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
    }

    /// <summary>Where a ray enters this hull before <paramref name="maxDist"/>, with the face's normal (false: a miss, or a start inside).</summary>
    public bool Ray(Vector3 o, Vector3 d, float maxDist, out float dist, out Vector3 normal)
    {
        var enter = 0f;
        var leave = maxDist;
        normal = Vector3.Zero;
        dist = 0;
        for (var f = 0; f < N.Length; f++)
        {
            var n = N[f];
            var plane = Vector3.Dot(n, V[FaceIdx[FaceStart[f]]]);
            var from = Vector3.Dot(n, o) - plane;
            var along = Vector3.Dot(n, d);
            if (MathF.Abs(along) < 1e-7f)
            {
                if (from > 0)
                    return false;
                continue;
            }
            var t = -from / along;
            if (along < 0)
            {
                if (t > enter)
                {
                    enter = t;
                    normal = n;
                }
            }
            else if (t < leave)
                leave = t;
            if (enter > leave)
                return false;
        }
        if (normal == Vector3.Zero)
            return false;
        dist = enter;
        return true;
    }
}

/// <summary>
/// Which of a part's shapes collide as what. A part file's detail box may have a contoured shape (<see cref="PartShapes"/>)
/// over hidden solid boxes that stand for it. A wedge and a cylinder (or tube: its outside) are convex, so they get a real
/// convex collider made from their mesh, and the hidden boxes under them stop being colliders. The rest keep their solid
/// boxes: an arc is not convex (its stepped boxes follow the curve), rounded and bevelled pieces are nearly their box.
/// A part file says <c>"collider": "boxes"</c> to keep its hidden boxes whatever its shapes. Ray hits use the same collider
/// (<see cref="ConstructWorld.Raycast"/>); a player's feet use the hidden boxes, the shape in steps a player walks up.
/// </summary>
public static class ConstructHulls
{
    private static readonly ConvexHull[] None = [];
    private static readonly bool[] NoCovers = [];
    private static ConvexHull? _prism;

    /// <summary>
    /// A wheel's cylinder (docs/sandbox/CONSTRUCTS.md, "Wheels"): a sixteen-sided prism, the most <see cref="ConvexHull.MaxCorners"/>
    /// allows, half a unit along X and one unit in radius; <see cref="ConstructNarrow.Shape.FromWheel"/> sizes it. It sits inside
    /// the true circle by 1% of the radius at most. Made on first use.
    /// </summary>
    internal static ConvexHull WheelPrism => _prism ??= MakePrism();

    private static ConvexHull MakePrism()
    {
        var points = new Vector3[32];
        for (var k = 0; k < 16; k++)
        {
            var a = (k + 0.5f) * MathF.Tau / 16;
            points[k * 2] = new Vector3(1, MathF.Sin(a), MathF.Cos(a));
            points[k * 2 + 1] = new Vector3(-1, MathF.Sin(a), MathF.Cos(a));
        }
        return ConvexHull.Of(points) ?? throw new InvalidOperationException("the wheel's prism has no hull");
    }

    private static bool HullShape(string shape) => shape is "wedge" or "cylinder" or "tube";

    /// <summary><paramref name="d"/>'s convex colliders (in the part's frame) and, per solid box, whether a hull stands for it. Made on first use.</summary>
    public static ConvexHull[] Of(ConstructPartDef d)
    {
        if (d.Hulls == null)
            Make(d);
        return d.Hulls!;
    }

    public static bool Covered(ConstructPartDef d, int box)
    {
        if (d.Hulls == null)
            Make(d);
        return box < d.HullCovers!.Length && d.HullCovers[box];
    }

    private static void Make(ConstructPartDef d)
    {
        var hulls = new List<ConvexHull>();
        var covers = NoCovers;
        for (var b = d.SolidBoxes; b < d.Boxes.Length && !d.ColliderBoxes; b++)
        {
            var box = d.Boxes[b];
            if (box.Shape == 0 || !HullShape(PartShapes.KindOf(box.Shape)))
                continue;
            var mesh = PartShapes.Mesh(box.Shape);
            var points = new Vector3[mesh.Length / 6];
            for (var k = 0; k < points.Length; k++)
                points[k] = box.ShapePoint(new Vector3(mesh[k * 6], mesh[k * 6 + 1], mesh[k * 6 + 2]));
            if (ConvexHull.Of(points) is not { } hull)
                continue;
            hulls.Add(hull);
            // The hidden solid boxes inside this shape's box stand for it no more.
            if (covers.Length == 0)
                covers = new bool[d.SolidBoxes];
            for (var s = 0; s < d.SolidBoxes; s++)
            {
                var o = d.Boxes[s];
                var at = Vector3.Abs(o.Center - box.Center) - box.Half;
                if (o.Hidden && at.X <= 1e-3f && at.Y <= 1e-3f && at.Z <= 1e-3f)
                    covers[s] = true;
            }
        }
        d.HullCovers = covers;
        d.Hulls = hulls.Count > 0 ? hulls.ToArray() : None;
    }
}
