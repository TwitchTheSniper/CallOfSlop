using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// One convex piece of the world a capsule can touch, gathered from the physics world for a region: a box (axis-aligned,
/// yaw-only or fully rotated, all as <see cref="Obb"/>) or a triangle (meshes, terrain cells).
/// </summary>
public struct ContactShape
{
    public const byte TypeBox = 0, TypeTriangle = 1;

    public ShapeId Id;
    public byte Type;

    /// <summary>Triangles only: collide with the front face only (terrain, whose front faces up).</summary>
    public bool OneSided;

    public Obb Box;
    public Vector3 A, B, C;
    public Vector3 Min, Max;

    public static ContactShape FromObb(in Obb box, ShapeId id)
    {
        box.Bounds(out var min, out var max);
        return new ContactShape { Id = id, Type = TypeBox, Box = box, Min = min, Max = max };
    }

    public static ContactShape FromTriangle(Vector3 a, Vector3 b, Vector3 c, ShapeId id, bool oneSided = false) =>
        new()
        {
            Id = id,
            Type = TypeTriangle,
            OneSided = oneSided,
            A = a,
            B = b,
            C = c,
            Min = Vector3.Min(a, Vector3.Min(b, c)),
            Max = Vector3.Max(a, Vector3.Max(b, c)),
        };
}

/// <summary>
/// Capsule queries against gathered <see cref="ContactShape"/>s: signed separation with a contact normal, overlap, a swept
/// cast (time of impact) and pushing out of overlaps. The cast is a per-shape conservative advancement: the distance
/// between a moving convex capsule and a convex shape is a convex function of the distance travelled, so a Newton step
/// from the current distance and its slope (the normal against the direction) never overshoots the contact. It converges
/// in a few steps, never tunnels and is deterministic.
/// </summary>
public static class CapsuleContact
{
    /// <summary>Casts stop this far short of contact, so the next cast starts separated.</summary>
    public const float Skin = 0.01f;

    /// <summary>
    /// Signed separation between the capsule (core pq, radius r) and a shape: distance between their surfaces, negative when
    /// they overlap (then -separation is how far to move along <paramref name="normal"/> to get clear). The normal points from
    /// the shape towards the capsule; <paramref name="point"/> is the closest point on the shape.
    /// </summary>
    public static float Separation(in ContactShape s, Vector3 p, Vector3 q, float r, out Vector3 normal, out Vector3 point)
    {
        if (s.Type == ContactShape.TypeBox)
        {
            ref readonly var b = ref s.Box;
            var lp = b.ToLocal(p);
            var lq = b.ToLocal(q);
            var d2 = Geometry.ClosestSegmentBox(lp, lq, b.Half, out var onSeg, out var onBox);
            point = b.ToWorld(onBox);
            if (d2 > 1e-12f)
            {
                var d = MathF.Sqrt(d2);
                normal = b.DirToWorld((onSeg - onBox) / d);
                return d - r;
            }
            normal = b.DirToWorld(Geometry.BoxEscape(lp, lq, b.Half, r, out var depth));
            return -depth;
        }

        var dist2 = Geometry.ClosestSegmentTriangle(p, q, s.A, s.B, s.C, out var onCore, out var onTri);
        point = onTri;
        if (dist2 > 1e-12f)
        {
            var d = MathF.Sqrt(dist2);
            normal = (onCore - onTri) / d;
            return d - r;
        }
        var face = Geometry.TriangleNormal(s.A, s.B, s.C);
        // The core passes through the triangle: out along the face normal, on the side the core's middle is on.
        var mid = (p + q) * 0.5f;
        var side = Vector3.Dot(mid - s.A, face);
        if (side < 0 && !s.OneSided)
            face = -face;
        var deepest = MathF.Min(Vector3.Dot(p - s.A, face), Vector3.Dot(q - s.A, face));
        normal = face;
        return MathF.Min(deepest, 0) - r;
    }

    /// <summary>Do the bounds lo..hi reach the shape's bounds?</summary>
    private static bool BoundsReach(in ContactShape s, Vector3 lo, Vector3 hi) =>
        !(s.Min.X > hi.X || s.Max.X < lo.X || s.Min.Y > hi.Y || s.Max.Y < lo.Y || s.Min.Z > hi.Z || s.Max.Z < lo.Z);

    /// <summary>Does the capsule overlap any of the shapes by more than <paramref name="allowance"/>?</summary>
    public static bool Overlaps(ReadOnlySpan<ContactShape> shapes, Vector3 p, Vector3 q, float r, float allowance = 1e-4f)
    {
        var lo = Vector3.Min(p, q) - new Vector3(r);
        var hi = Vector3.Max(p, q) + new Vector3(r);
        for (var i = 0; i < shapes.Length; i++)
            if (BoundsReach(shapes[i], lo, hi) && Separation(shapes[i], p, q, r, out _, out _) < -allowance)
                return true;
        return false;
    }

    /// <summary>
    /// Sweeps the capsule along the unit direction <paramref name="dir"/> up to <paramref name="maxDist"/>. Returns true on a
    /// hit with the travel <paramref name="toi"/> (stopping <see cref="Skin"/> short), the contact normal and point and the
    /// index of the shape. Shapes it touches but moves away from (or along) at the start don't block it.
    /// </summary>
    public static bool Cast(ReadOnlySpan<ContactShape> shapes, Vector3 p, Vector3 q, float r, Vector3 dir, float maxDist,
        out float toi, out Vector3 normal, out Vector3 point, out int index)
    {
        toi = maxDist;
        normal = Vector3.Zero;
        point = Vector3.Zero;
        index = -1;
        if (maxDist <= 0)
            return false;
        var end = dir * maxDist;
        var lo = Vector3.Min(Vector3.Min(p, q), Vector3.Min(p + end, q + end)) - new Vector3(r + Skin);
        var hi = Vector3.Max(Vector3.Max(p, q), Vector3.Max(p + end, q + end)) + new Vector3(r + Skin);
        for (var i = 0; i < shapes.Length; i++)
        {
            ref readonly var s = ref shapes[i];
            if (!BoundsReach(s, lo, hi))
                continue;
            var t = 0f;
            for (var iter = 0; ; iter++)
            {
                var sep = Separation(s, p + dir * t, q + dir * t, r, out var n, out var pt);
                var approach = Vector3.Dot(dir, n);
                if (sep <= Skin || iter == 24)
                {
                    if (iter == 0 && approach >= -1e-4f)
                        break; // touching, but moving away from it or along it
                    if (t < toi || (t == toi && i < index))
                    {
                        toi = t;
                        normal = n;
                        point = pt;
                        index = i;
                    }
                    break;
                }
                if (approach >= -1e-6f)
                    break; // never gets closer
                t += (sep - Skin * 0.5f) / -approach;
                if (t >= toi)
                    break;
            }
        }
        return index >= 0;
    }

    /// <summary>
    /// Pushes the capsule out of every shape it overlaps (each in turn, a few rounds). Returns the total offset; clear is false
    /// if it still overlaps something afterwards (squeezed between two things).
    /// </summary>
    public static Vector3 Depenetrate(ReadOnlySpan<ContactShape> shapes, Vector3 p, Vector3 q, float r, int rounds, out bool clear)
    {
        var offset = Vector3.Zero;
        clear = true;
        for (var round = 0; round < rounds; round++)
        {
            var moved = false;
            var lo = Vector3.Min(p, q) + offset - new Vector3(r + 0.1f);
            var hi = Vector3.Max(p, q) + offset + new Vector3(r + 0.1f);
            for (var i = 0; i < shapes.Length; i++)
            {
                if (!BoundsReach(shapes[i], lo, hi))
                    continue;
                var sep = Separation(shapes[i], p + offset, q + offset, r, out var n, out _);
                if (sep < -1e-4f)
                {
                    offset += n * (-sep + 1e-3f);
                    moved = true;
                }
            }
            if (!moved)
                return offset;
        }
        clear = !Overlaps(shapes, p + offset, q + offset, r);
        return offset;
    }
}
