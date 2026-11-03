using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop.Space;

/// <summary>
/// A double-precision 3D vector for the space layer (docs/space/SPACE.md): orbital offsets, velocities and positions inside
/// one frame. Positions across a whole star system are <see cref="SpacePos"/> (fixed point); a <c>Vec3d</c> holds a
/// difference or a frame-local value, where a double keeps far below a millimetre out to millions of kilometres.
/// </summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static readonly Vec3d Zero = new(0, 0, 0);
    public static readonly Vec3d UnitX = new(1, 0, 0);
    public static readonly Vec3d UnitY = new(0, 1, 0);
    public static readonly Vec3d UnitZ = new(0, 0, 1);

    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vec3d operator *(Vec3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vec3d operator *(double s, Vec3d a) => new(a.X * s, a.Y * s, a.Z * s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vec3d operator /(Vec3d a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3d Cross(Vec3d a, Vec3d b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>This vector at unit length (zero stays zero).</summary>
    public Vec3d Normalized()
    {
        var l = Length;
        return l > 0 ? this / l : Zero;
    }

    public static Vec3d From(Vector3 v) => new(v.X, v.Y, v.Z);
    public Vector3 ToVector3() => new((float)X, (float)Y, (float)Z);

    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###}, {Z:0.###})");
}

/// <summary>
/// A double-precision unit quaternion (W + Xi + Yj + Zk), for frame orientations. Right-handed; <see cref="Rotate"/> turns a
/// vector from the frame's own axes into its parent's.
/// </summary>
public readonly record struct QuatD(double W, double X, double Y, double Z)
{
    public static readonly QuatD Identity = new(1, 0, 0, 0);

    /// <summary>A turn by <paramref name="angle"/> radians about a unit axis (right-hand rule).</summary>
    public static QuatD AxisAngle(Vec3d axis, double angle)
    {
        var (s, c) = Math.SinCos(angle * 0.5);
        return new QuatD(c, axis.X * s, axis.Y * s, axis.Z * s);
    }

    public static QuatD RotX(double angle) { var (s, c) = Math.SinCos(angle * 0.5); return new QuatD(c, s, 0, 0); }
    public static QuatD RotY(double angle) { var (s, c) = Math.SinCos(angle * 0.5); return new QuatD(c, 0, s, 0); }
    public static QuatD RotZ(double angle) { var (s, c) = Math.SinCos(angle * 0.5); return new QuatD(c, 0, 0, s); }

    /// <summary>Hamilton product: <c>(a * b).Rotate(v) == a.Rotate(b.Rotate(v))</c>.</summary>
    public static QuatD operator *(QuatD a, QuatD b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public QuatD Conjugate => new(W, -X, -Y, -Z);

    public QuatD Normalized()
    {
        var l = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
        return l > 0 ? new QuatD(W / l, X / l, Y / l, Z / l) : Identity;
    }

    /// <summary>The vector turned by this rotation (v' = q v q*), without building the product.</summary>
    public Vec3d Rotate(Vec3d v)
    {
        // t = 2 (q.xyz x v); v' = v + w t + q.xyz x t
        var tx = 2 * (Y * v.Z - Z * v.Y);
        var ty = 2 * (Z * v.X - X * v.Z);
        var tz = 2 * (X * v.Y - Y * v.X);
        return new Vec3d(
            v.X + W * tx + (Y * tz - Z * ty),
            v.Y + W * ty + (Z * tx - X * tz),
            v.Z + W * tz + (X * ty - Y * tx));
    }

    /// <summary>The vector turned by the inverse of this rotation (from the parent's axes into the frame's own).</summary>
    public Vec3d InverseRotate(Vec3d v) => Conjugate.Rotate(v);

    /// <summary>The rotation whose frame has these axes (unit, right-handed, orthogonal) in its parent's coordinates.</summary>
    public static QuatD FromAxes(Vec3d x, Vec3d y, Vec3d z)
    {
        // Shepperd's method on the matrix whose columns are x, y, z.
        double m00 = x.X, m01 = y.X, m02 = z.X;
        double m10 = x.Y, m11 = y.Y, m12 = z.Y;
        double m20 = x.Z, m21 = y.Z, m22 = z.Z;
        var tr = m00 + m11 + m22;
        QuatD q;
        if (tr > 0)
        {
            var s = Math.Sqrt(tr + 1) * 2;
            q = new QuatD(0.25 * s, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            var s = Math.Sqrt(1 + m00 - m11 - m22) * 2;
            q = new QuatD((m21 - m12) / s, 0.25 * s, (m01 + m10) / s, (m02 + m20) / s);
        }
        else if (m11 > m22)
        {
            var s = Math.Sqrt(1 + m11 - m00 - m22) * 2;
            q = new QuatD((m02 - m20) / s, (m01 + m10) / s, 0.25 * s, (m12 + m21) / s);
        }
        else
        {
            var s = Math.Sqrt(1 + m22 - m00 - m11) * 2;
            q = new QuatD((m10 - m01) / s, (m02 + m20) / s, (m12 + m21) / s, 0.25 * s);
        }
        return q.Normalized();
    }

    /// <summary>The angle in radians between two orientations (0 to pi).</summary>
    public static double AngleBetween(QuatD a, QuatD b)
    {
        var d = Math.Abs(a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z);
        return 2 * Math.Acos(Math.Min(1, d));
    }
}
