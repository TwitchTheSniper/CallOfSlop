using System.Numerics;

namespace CallOfSlop.MapKit;

/// <summary>
/// Outline shapes and 2D helpers for generators: circles, ellipses, regular polygons, beans, noisy blobs, smoothed
/// control polygons (the game's "smooth" boundary), outlines built from one half, point-in-polygon, edge distance and
/// area; yaws for props and meshes; the game's rotation order for placing turned meshes. Points are [x, z] pairs as the
/// map file's "boundary" takes them.
/// </summary>
public static class Geometry
{
    public static List<double[]> Circle(double radius, int n = 32, double cx = 0, double cz = 0) => Ellipse(radius, radius, n, cx, cz);

    public static List<double[]> Ellipse(double rx, double rz, int n = 32, double cx = 0, double cz = 0)
    {
        var pts = new List<double[]>(n);
        for (var k = 0; k < n; k++)
        {
            var a = 2 * Math.PI * k / n;
            pts.Add([Kit.R(cx + rx * Math.Cos(a)), Kit.R(cz + rz * Math.Sin(a))]);
        }
        return pts;
    }

    /// <summary>A regular polygon with a corner at <paramref name="phaseDeg"/> degrees (0: a corner on +X).</summary>
    public static List<double[]> RegularPolygon(double radius, int sides, double phaseDeg = 0)
    {
        var pts = new List<double[]>(sides);
        for (var i = 0; i < sides; i++)
        {
            var a = Math.PI / 180 * (phaseDeg + 360.0 * i / sides);
            pts.Add([Kit.R(radius * Math.Cos(a)), Kit.R(radius * Math.Sin(a))]);
        }
        return pts;
    }

    /// <summary>
    /// A bean (a kidney): an ellipse rx by rz whose +Z side is pushed in by <paramref name="pinch"/> metres at the middle,
    /// giving a crescent-like playable band (Vesper Strip's shape). A negative pinch bulges instead.
    /// </summary>
    public static List<double[]> Bean(double rx, double rz, double pinch, int n = 36)
    {
        var pts = new List<double[]>(n);
        for (var k = 0; k < n; k++)
        {
            var a = 2 * Math.PI * k / n;
            var x = rx * Math.Cos(a);
            var z = rz * Math.Sin(a);
            if (z > 0)
                z -= pinch * Math.Pow(Math.Max(0, 1 - (x / rx) * (x / rx)), 1.5) * (z / rz);
            pts.Add([Kit.R(x), Kit.R(z)]);
        }
        return pts;
    }

    /// <summary>
    /// A blob: a circle whose radius wobbles by up to <paramref name="amplitude"/> metres with a few seeded harmonics
    /// (<paramref name="octaves"/> of them, the biggest bumps about <paramref name="scale"/> metres across). Deterministic.
    /// </summary>
    public static List<double[]> Blob(double radius, double amplitude, int seed, int n = 48, double scale = 24, int octaves = 3)
    {
        var rng = new Rng(seed);
        var waves = new List<(double Freq, double Phase, double Amp)>();
        var amp = 1.0;
        var total = 0.0;
        var baseFreq = Math.Max(2, Math.Round(2 * Math.PI * radius / scale));
        for (var o = 0; o < octaves; o++)
        {
            waves.Add((baseFreq * (1 << o), rng.Uniform(0, 2 * Math.PI), amp));
            total += amp;
            amp *= 0.5;
        }
        var pts = new List<double[]>(n);
        for (var k = 0; k < n; k++)
        {
            var a = 2 * Math.PI * k / n;
            var w = 0.0;
            foreach (var (f, p, am) in waves)
                w += am * Math.Sin(f * a + p);
            var r = radius + amplitude * w / total;
            pts.Add([Kit.R(r * Math.Cos(a)), Kit.R(r * Math.Sin(a))]);
        }
        return pts;
    }

    /// <summary>
    /// The whole outline from one half: the half's points followed by their twins in reverse (shared end points on the
    /// symmetry line are not doubled). For a Turn180 half, the twins follow in the same order.
    /// </summary>
    public static List<double[]> FromHalf(IReadOnlyList<(double X, double Z)> half, Symmetry sym)
    {
        var pts = half.Select(p => new[] { (double)p.X, p.Z }).ToList();
        if (sym == Symmetry.Turn180)
        {
            var inner = OnLine(half[0], sym) ? 1 : 0;
            var last = OnLine(half[^1], sym) ? half.Count - 1 : half.Count;
            for (var i = inner; i < last; i++)
                pts.Add([-half[i].X, -half[i].Z]);
            return pts;
        }
        for (var i = half.Count - 1; i >= 0; i--)
        {
            if (OnLine(half[i], sym))
                continue;
            var (x, z) = Kit.TwinPoint(half[i].X, half[i].Z, sym);
            pts.Add([x, z]);
        }
        return pts;
    }

    private static bool OnLine((double X, double Z) p, Symmetry sym) => sym switch
    {
        Symmetry.MirrorX => Math.Abs(p.X) < 1e-9,
        Symmetry.MirrorZ => Math.Abs(p.Z) < 1e-9,
        Symmetry.Turn180 => Math.Abs(p.X) < 1e-9 && Math.Abs(p.Z) < 1e-9,
        _ => false,
    };

    /// <summary>Closed centripetal Catmull-Rom through the control points (the game's "smooth" boundary), sampled every <paramref name="step"/> metres.</summary>
    public static List<double[]> Smooth(IReadOnlyList<double[]> pts, double step = 0.25)
    {
        var n = pts.Count;
        var outPts = new List<double[]>();
        for (var i = 0; i < n; i++)
        {
            double[] p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n];
            double Tj(double ti, double[] a, double[] b) => ti + Math.Sqrt(Math.Max(1e-4, Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]))));
            var t0 = 0.0;
            var t1 = Tj(t0, p0, p1);
            var t2 = Tj(t1, p1, p2);
            var t3 = Tj(t2, p2, p3);
            var seg = Math.Sqrt((p2[0] - p1[0]) * (p2[0] - p1[0]) + (p2[1] - p1[1]) * (p2[1] - p1[1]));
            var k = Math.Max(2, (int)(seg / step));
            for (var s = 0; s < k; s++)
            {
                var t = t1 + (t2 - t1) * s / k;
                double[] Lerp(double[] a, double[] b, double ta, double tb) =>
                    [(tb - t) / (tb - ta) * a[0] + (t - ta) / (tb - ta) * b[0], (tb - t) / (tb - ta) * a[1] + (t - ta) / (tb - ta) * b[1]];
                var a1 = Lerp(p0, p1, t0, t1);
                var a2 = Lerp(p1, p2, t1, t2);
                var a3 = Lerp(p2, p3, t2, t3);
                var b1 = Lerp(a1, a2, t0, t2);
                var b2 = Lerp(a2, a3, t1, t3);
                outPts.Add(Lerp(b1, b2, t1, t2));
            }
        }
        return outPts;
    }

    public static bool Inside(IReadOnlyList<double[]> poly, double x, double z)
    {
        var c = false;
        var n = poly.Count;
        for (var i = 0; i < n; i++)
        {
            double ax = poly[i][0], az = poly[i][1], bx = poly[(i + 1) % n][0], bz = poly[(i + 1) % n][1];
            if (az > z != bz > z && x < ax + (z - az) * (bx - ax) / (bz - az))
                c = !c;
        }
        return c;
    }

    public static double EdgeDistance(IReadOnlyList<double[]> poly, double x, double z)
    {
        var best = double.MaxValue;
        var n = poly.Count;
        for (var i = 0; i < n; i++)
        {
            double ax = poly[i][0], az = poly[i][1], bx = poly[(i + 1) % n][0], bz = poly[(i + 1) % n][1];
            double dx = bx - ax, dz = bz - az, ll = dx * dx + dz * dz;
            var t = ll == 0 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / ll, 0, 1);
            best = Math.Min(best, Math.Sqrt((ax + t * dx - x) * (ax + t * dx - x) + (az + t * dz - z) * (az + t * dz - z)));
        }
        return best;
    }

    public static double Area(IReadOnlyList<double[]> poly)
    {
        var a = 0.0;
        var n = poly.Count;
        for (var i = 0; i < n; i++)
            a += poly[i][0] * poly[(i + 1) % n][1] - poly[(i + 1) % n][0] * poly[i][1];
        return Math.Abs(a) / 2;
    }

    /// <summary>Points along a polyline every <paramref name="spacing"/> metres (the first point included, the end if it falls within half a step).</summary>
    public static List<(double X, double Z)> Along(IReadOnlyList<(double X, double Z)> pts, double spacing, double start = 0)
    {
        var result = new List<(double, double)>();
        var next = start;
        var walked = 0.0;
        for (var i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, az) = pts[i];
            var (bx, bz) = pts[i + 1];
            var len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            while (next <= walked + len + 1e-9)
            {
                var f = len == 0 ? 0 : (next - walked) / len;
                result.Add((ax + (bx - ax) * f, az + (bz - az) * f));
                next += spacing;
            }
            walked += len;
        }
        return result;
    }

    /// <summary>The yaw (degrees) that turns a prop's front (+Z) or a shape's local +Z towards (dx, dz).</summary>
    public static double YawFacing(double dx, double dz) => Math.Atan2(dx, dz) * 180 / Math.PI;

    /// <summary>The yaw (degrees) that turns a shape's local +X along (dx, dz) (a box mesh's "width" runs that way).</summary>
    public static double YawAlong(double dx, double dz) => Math.Atan2(-dz, dx) * 180 / Math.PI;

    /// <summary>The unit vector from a sphere's centre to latitude / longitude (degrees; lon 0 is +Z, lon 90 is +X), in doubles.</summary>
    public static (double X, double Y, double Z) SurfaceDir(double lat, double lon)
    {
        double la = lat * Math.PI / 180, lo = lon * Math.PI / 180;
        return (Math.Cos(la) * Math.Sin(lo), Math.Sin(la), Math.Cos(la) * Math.Cos(lo));
    }

    /// <summary>A vector turned the way the game turns a mesh (System.Numerics row vectors: v * Rz(roll) * Rx(pitch) * Ry(yaw)).</summary>
    public static Vector3 Turn(Vector3 v, double yaw, double pitch, double roll)
    {
        var m = Matrix4x4.CreateRotationZ((float)(roll * Math.PI / 180)) * Matrix4x4.CreateRotationX((float)(pitch * Math.PI / 180)) * Matrix4x4.CreateRotationY((float)(yaw * Math.PI / 180));
        return Vector3.Transform(v, m);
    }
}

/// <summary>
/// A tiny deterministic generator (a 32-bit linear congruential sequence), so a map never depends on the runtime's
/// Random. The same seed gives the same numbers on every machine and build; the Python generators' _Rng is this one.
/// </summary>
public sealed class Rng(int seed)
{
    private uint _s = (uint)(((long)seed * 2654435761L + 12345) & 0xFFFFFFFF);

    /// <summary>The next value in [0, 1).</summary>
    public double Next()
    {
        _s = unchecked(_s * 1664525u + 1013904223u);
        return _s / 4294967296.0;
    }

    public double Uniform(double a, double b) => a + (b - a) * Next();

    /// <summary>A whole number from <paramref name="lo"/> up to but not including <paramref name="hi"/>.</summary>
    public int Int(int lo, int hi) => lo + (int)Math.Floor(Next() * (hi - lo));

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count)];

    public bool Chance(double p) => Next() < p;
}
