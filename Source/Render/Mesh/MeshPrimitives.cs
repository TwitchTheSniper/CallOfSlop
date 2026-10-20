using System.Globalization;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The procedural shapes <see cref="MeshPrimitives"/> can build.</summary>
public enum PrimitiveKind
{
    Box,
    RoundedBox,
    Cylinder,
    Cone,
    Sphere,
    IcoSphere,
    Capsule,
    Torus,
    Wedge,
    Tube,
}

/// <summary>
/// The parameters of one procedural shape. Every primitive stands on y = 0 (its lowest point) and is centred on x = z = 0.
/// <list type="bullet">
/// <item><b>box</b>, <b>wedge</b> (alias ramp): <see cref="Width"/> (x), <see cref="Height"/> (y), <see cref="Depth"/> (z). The wedge rises towards +z.</item>
/// <item><b>roundedbox</b>: the same plus <see cref="Radius"/> (edge rounding, default 0.1) and <see cref="Segments"/> (steps per rounded edge, default 3).</item>
/// <item><b>cylinder</b>, <b>cone</b>: diameters <see cref="Width"/> and <see cref="Depth"/>, <see cref="Height"/>; <see cref="Segments"/> around (default 24). Capped.</item>
/// <item><b>sphere</b> (UV sphere): diameters <see cref="Width"/>, <see cref="Height"/>, <see cref="Depth"/>; <see cref="Segments"/> around (24), <see cref="Detail"/> rings (segments / 2).</item>
/// <item><b>icosphere</b>: diameters as the sphere; <see cref="Detail"/> subdivisions (default 2, 0-5).</item>
/// <item><b>capsule</b>: <see cref="Radius"/> (default width / 2), total <see cref="Height"/>; <see cref="Segments"/> around, <see cref="Detail"/> rings per cap (segments / 4).</item>
/// <item><b>torus</b>: <see cref="Radius"/> (ring radius, default 0.4), <see cref="Inner"/> (tube radius, default 0.1), lying flat; <see cref="Segments"/> around (24), <see cref="Detail"/> around the tube (12).</item>
/// <item><b>tube</b> (alias pipe): outer <see cref="Radius"/> (default width / 2), <see cref="Inner"/> radius (default 0.8 x radius), <see cref="Height"/>; <see cref="Segments"/>. A hollow cylinder, closed at the rims.</item>
/// </list>
/// Zero means "default" for radius, inner, segments and detail. Sizes are metres.
/// </summary>
public readonly record struct PrimitiveSpec(PrimitiveKind Kind, float Width = 1, float Height = 1, float Depth = 1, float Radius = 0, float Inner = 0, int Segments = 0, int Detail = 0)
{
    /// <summary>Every name a map can use, with its kind (aliases included).</summary>
    public static readonly IReadOnlyDictionary<string, PrimitiveKind> Names = new Dictionary<string, PrimitiveKind>(StringComparer.OrdinalIgnoreCase)
    {
        ["box"] = PrimitiveKind.Box, ["cube"] = PrimitiveKind.Box,
        ["roundedbox"] = PrimitiveKind.RoundedBox, ["rounded_box"] = PrimitiveKind.RoundedBox, ["roundbox"] = PrimitiveKind.RoundedBox,
        ["cylinder"] = PrimitiveKind.Cylinder, ["cone"] = PrimitiveKind.Cone,
        ["sphere"] = PrimitiveKind.Sphere, ["uvsphere"] = PrimitiveKind.Sphere,
        ["icosphere"] = PrimitiveKind.IcoSphere, ["ico"] = PrimitiveKind.IcoSphere,
        ["capsule"] = PrimitiveKind.Capsule, ["torus"] = PrimitiveKind.Torus, ["ring"] = PrimitiveKind.Torus,
        ["wedge"] = PrimitiveKind.Wedge, ["ramp"] = PrimitiveKind.Wedge,
        ["tube"] = PrimitiveKind.Tube, ["pipe"] = PrimitiveKind.Tube,
    };

    /// <summary>The primitive called <paramref name="name"/>; throws with the list of valid names (and a close match) if there's none.</summary>
    public static PrimitiveKind ParseKind(string name)
    {
        if (Names.TryGetValue(name.Trim(), out var kind))
            return kind;
        var close = Names.Keys.OrderBy(k => Distance(k, name.ToLowerInvariant())).First();
        throw new InvalidOperationException($"unknown mesh \"{name}\" (did you mean \"{close}\"?). Primitives: box, roundedbox, cylinder, cone, sphere, icosphere, capsule, torus, wedge (ramp), tube (pipe)");
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++)
            d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>Defaults filled in and every value checked; throws a readable message if something is out of range.</summary>
    public PrimitiveSpec Resolve()
    {
        static float Size(float v, string what) => v is >= 0.01f and <= 500f && float.IsFinite(v) ? v : throw new InvalidOperationException($"{what} must be 0.01 to 500 m, got {v.ToString(CultureInfo.InvariantCulture)}");
        static int Count(int v, int min, int max, string what) => v >= min && v <= max ? v : throw new InvalidOperationException($"{what} must be {min} to {max}, got {v}");
        var s = this with { Width = Size(Width, "width"), Height = Size(Height, "height"), Depth = Size(Depth, "depth") };
        if (Radius < 0 || Inner < 0 || Segments < 0 || Detail < 0)
            throw new InvalidOperationException("radius, inner, segments and detail can't be negative");
        switch (Kind)
        {
            case PrimitiveKind.Box:
            case PrimitiveKind.Wedge:
                return s with { Radius = 0, Inner = 0, Segments = 0, Detail = 0 };
            case PrimitiveKind.RoundedBox:
            {
                var maxR = MathF.Min(MathF.Min(s.Width, s.Height), s.Depth) * 0.5f;
                var r = Radius == 0 ? MathF.Min(0.1f, maxR * 0.5f) : Radius;
                if (r > maxR)
                    throw new InvalidOperationException($"roundedbox radius {r.ToString(CultureInfo.InvariantCulture)} is more than half the smallest side ({maxR.ToString(CultureInfo.InvariantCulture)})");
                return s with { Radius = MathF.Max(r, 0.002f), Inner = 0, Segments = Count(Segments == 0 ? 3 : Segments, 1, 16, "roundedbox segments"), Detail = 0 };
            }
            case PrimitiveKind.Cylinder:
            case PrimitiveKind.Cone:
                return s with { Radius = 0, Inner = 0, Segments = Count(Segments == 0 ? 24 : Segments, 3, 128, "segments"), Detail = 0 };
            case PrimitiveKind.Sphere:
            {
                var seg = Count(Segments == 0 ? 24 : Segments, 4, 128, "segments");
                return s with { Radius = 0, Inner = 0, Segments = seg, Detail = Count(Detail == 0 ? Math.Max(seg / 2, 2) : Detail, 2, 64, "detail (rings)") };
            }
            case PrimitiveKind.IcoSphere:
                return s with { Radius = 0, Inner = 0, Segments = 0, Detail = Count(Detail == 0 ? 2 : Detail, 0, 5, "detail (subdivisions)") };
            case PrimitiveKind.Capsule:
            {
                var r = Radius == 0 ? MathF.Min(s.Width, s.Depth) * 0.5f : Size(Radius, "radius");
                if (s.Height < 2 * r - 1e-4f)
                    throw new InvalidOperationException($"capsule height {s.Height.ToString(CultureInfo.InvariantCulture)} is less than twice the radius ({(2 * r).ToString(CultureInfo.InvariantCulture)})");
                var seg = Count(Segments == 0 ? 24 : Segments, 4, 128, "segments");
                return s with { Radius = r, Inner = 0, Segments = seg, Detail = Count(Detail == 0 ? Math.Max(seg / 4, 2) : Detail, 1, 32, "detail (rings per cap)") };
            }
            case PrimitiveKind.Torus:
            {
                var r = Radius == 0 ? 0.4f : Size(Radius, "radius");
                var t = Inner == 0 ? MathF.Min(0.1f, r * 0.5f) : Size(Inner, "inner");
                if (t >= r)
                    throw new InvalidOperationException("torus inner (tube radius) must be less than radius");
                return s with { Radius = r, Inner = t, Segments = Count(Segments == 0 ? 24 : Segments, 3, 128, "segments"), Detail = Count(Detail == 0 ? 12 : Detail, 3, 64, "detail") };
            }
            case PrimitiveKind.Tube:
            {
                var r = Radius == 0 ? s.Width * 0.5f : Size(Radius, "radius");
                var i = Inner == 0 ? r * 0.8f : Size(Inner, "inner");
                if (i >= r)
                    throw new InvalidOperationException("tube inner radius must be less than radius");
                return s with { Radius = r, Inner = i, Segments = Count(Segments == 0 ? 24 : Segments, 3, 128, "segments"), Detail = 0 };
            }
        }
        throw new InvalidOperationException($"unknown primitive {Kind}");
    }

    /// <summary>The same shape with fewer segments for a distance LOD (null when it can't get simpler).</summary>
    public PrimitiveSpec? ForLod(int lod)
    {
        if (lod <= 0)
            return this;
        static int Less(int v, int lod, int min) => Math.Max(min, v >> lod);
        var s = Kind switch
        {
            PrimitiveKind.Cylinder or PrimitiveKind.Cone or PrimitiveKind.Tube => this with { Segments = Less(Segments, lod, 6) },
            PrimitiveKind.Sphere => this with { Segments = Less(Segments, lod, 8), Detail = Less(Detail, lod, 4) },
            PrimitiveKind.Capsule => this with { Segments = Less(Segments, lod, 8), Detail = Less(Detail, lod, 2) },
            PrimitiveKind.Torus => this with { Segments = Less(Segments, lod, 8), Detail = Less(Detail, lod, 4) },
            PrimitiveKind.IcoSphere => this with { Detail = Math.Max(0, Detail - lod) },
            PrimitiveKind.RoundedBox => this with { Segments = Less(Segments, lod, 1) },
            _ => this,
        };
        var prev = lod == 1 ? this : ForLod(lod - 1);
        return prev is not { } p || s == p ? null : s;
    }

    /// <summary>A canonical name for caching ("cylinder w1 h2 d1 s24"): equal shapes share one mesh.</summary>
    public string Key
    {
        get
        {
            static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
            return $"{Kind.ToString().ToLowerInvariant()} w{F(Width)} h{F(Height)} d{F(Depth)} r{F(Radius)} i{F(Inner)} s{Segments} n{Detail}";
        }
    }
}

/// <summary>
/// Procedural meshes with smooth normals on curved surfaces, hard edges where faces meet at an angle (cylinder rims,
/// box edges) and texture coordinates in metres (lathe shapes: u around, v along the profile). Deterministic: the same
/// spec always gives the same bytes. Most round shapes are lathes, a profile curve revolved around the y axis.
/// </summary>
public static class MeshPrimitives
{
    public static MeshData Build(PrimitiveSpec spec)
    {
        var s = spec.Resolve();
        switch (s.Kind)
        {
            case PrimitiveKind.Box:
                return Box(s.Width, s.Height, s.Depth);
            case PrimitiveKind.RoundedBox:
                return RoundedBox(s.Width, s.Height, s.Depth, s.Radius, s.Segments);
            case PrimitiveKind.Wedge:
                return Wedge(s.Width, s.Height, s.Depth);
            case PrimitiveKind.Cylinder:
                return Scaled(Lathe([[new(0, 0, 0, -1), new(0.5f, 0, 0, -1)], [new(0.5f, 0, 1, 0), new(0.5f, 1, 1, 0)], [new(0.5f, 1, 0, 1), new(0, 1, 0, 1)]], s.Segments), s);
            case PrimitiveKind.Cone:
                return Scaled(Lathe([[new(0, 0, 0, -1), new(0.5f, 0, 0, -1)], [new(0.5f, 0, 1, 0.5f), new(0, 1, 1, 0.5f)]], s.Segments), s);
            case PrimitiveKind.Sphere:
            {
                var strip = new ProfilePoint[s.Detail + 1];
                for (var k = 0; k <= s.Detail; k++)
                {
                    var phi = MathF.PI * k / s.Detail;
                    var (sn, cs) = MathF.SinCos(phi);
                    strip[k] = new ProfilePoint(0.5f * sn, 0.5f - 0.5f * cs, sn, -cs);
                }
                return Scaled(Lathe([strip], s.Segments), s);
            }
            case PrimitiveKind.IcoSphere:
                return Scaled(IcoSphere(s.Detail), s);
            case PrimitiveKind.Capsule:
            {
                var strip = new List<ProfilePoint>();
                for (var k = 0; k <= s.Detail; k++)
                {
                    var (sn, cs) = MathF.SinCos(MathF.PI * 0.5f * k / s.Detail);
                    strip.Add(new ProfilePoint(s.Radius * sn, s.Radius - s.Radius * cs, sn, -cs));
                }
                for (var k = 0; k <= s.Detail; k++)
                {
                    var (sn, cs) = MathF.SinCos(MathF.PI * 0.5f + MathF.PI * 0.5f * k / s.Detail);
                    strip.Add(new ProfilePoint(s.Radius * sn, s.Height - s.Radius - s.Radius * cs, sn, -cs));
                }
                return Lathe([strip.ToArray()], s.Segments);
            }
            case PrimitiveKind.Torus:
            {
                var strip = new ProfilePoint[s.Detail + 1];
                for (var k = 0; k <= s.Detail; k++)
                {
                    var (sn, cs) = MathF.SinCos(2 * MathF.PI * (k % s.Detail) / s.Detail - MathF.PI * 0.5f); // start at the bottom
                    strip[k] = new ProfilePoint(s.Radius + s.Inner * cs, s.Inner + s.Inner * sn, cs, sn);
                }
                return Lathe([strip], s.Segments);
            }
            case PrimitiveKind.Tube:
            {
                float ro = s.Radius, ri = s.Inner, h = s.Height;
                return Lathe(
                [
                    [new(ro, 0, 1, 0), new(ro, h, 1, 0)],
                    [new(ro, h, 0, 1), new(ri, h, 0, 1)],
                    [new(ri, h, -1, 0), new(ri, 0, -1, 0)],
                    [new(ri, 0, 0, -1), new(ro, 0, 0, -1)],
                ], s.Segments);
            }
        }
        throw new InvalidOperationException($"unknown primitive {s.Kind}");
    }

    /// <summary>One point of a lathe profile: radius, height and the outward normal in the (r, y) plane.</summary>
    private readonly record struct ProfilePoint(float R, float Y, float NR, float NY);

    /// <summary>Unit shapes (diameter 1, height 1) scaled to the spec's width, height and depth.</summary>
    private static MeshData Scaled(MeshData m, PrimitiveSpec s) =>
        s.Width == 1 && s.Height == 1 && s.Depth == 1 ? m : m.Transformed(Matrix4x4.CreateScale(s.Width, s.Height, s.Depth));

    /// <summary>
    /// Revolves profile strips around +y with <paramref name="segments"/> steps. Neighbouring points of a strip are joined
    /// by a band of quads; separate strips meet at hard edges. A strip at one height (a cap) gets planar uvs.
    /// The seam column is duplicated so u runs 0 to the circumference without wrapping.
    /// </summary>
    private static MeshData Lathe(ProfilePoint[][] strips, int segments)
    {
        var b = new MeshBuilder();
        var sin = new float[segments + 1];
        var cos = new float[segments + 1];
        for (var j = 0; j <= segments; j++)
            (sin[j], cos[j]) = j == segments ? (0f, 1f) : MathF.SinCos(2 * MathF.PI * j / segments);
        var rows = new int[segments + 1];
        foreach (var strip in strips)
        {
            var flat = strip.All(p => p.Y == strip[0].Y);
            var rmax = strip.Max(p => p.R);
            var arc = 0f;
            var prevRow = -1;
            for (var k = 0; k < strip.Length; k++)
            {
                var p = strip[k];
                if (k > 0)
                    arc += MathF.Sqrt((p.R - strip[k - 1].R) * (p.R - strip[k - 1].R) + (p.Y - strip[k - 1].Y) * (p.Y - strip[k - 1].Y));
                var nl = MathF.Sqrt(p.NR * p.NR + p.NY * p.NY);
                float nr = p.NR / nl, ny = p.NY / nl;
                var first = b.VertexCount;
                var radius = MathF.Abs(p.R) < 1e-6f ? 0f : p.R; // sin(pi) isn't exactly 0 in floats: poles must weld
                for (var j = 0; j <= segments; j++)
                {
                    var pos = new Vector3(radius * sin[j], p.Y, radius * cos[j]);
                    var uv = flat ? new Vector2(pos.X, pos.Z) : new Vector2(2 * MathF.PI * rmax * j / segments, arc);
                    b.Vertex(pos, new Vector3(nr * sin[j], ny, nr * cos[j]), uv);
                }
                if (prevRow >= 0)
                    for (var j = 0; j < segments; j++)
                        b.Quad(prevRow + j, prevRow + j + 1, first + j + 1, first + j);
                prevRow = first;
            }
        }
        return b.Build();
    }

    public static MeshData Box(float w, float h, float d)
    {
        var b = new MeshBuilder();
        var half = new Vector3(w, h, d) * 0.5f;
        var c = new Vector3(0, h * 0.5f, 0);
        foreach (var (n, u, v) in BoxFaces)
        {
            int Corner(float su, float sv)
            {
                var p = c + (n + u * su + v * sv) * half;
                return b.Vertex(p, n, new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            }
            b.Quad(Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1));
        }
        return b.Build();
    }

    private static readonly (Vector3 N, Vector3 U, Vector3 V)[] BoxFaces =
    [
        (Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
        (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
    ];

    /// <summary>
    /// A box with rounded edges and corners: a cube grid per face whose points are pushed onto the surface of the inner
    /// box grown by <paramref name="r"/> (normal = direction from the nearest inner-box point). Grid lines in the rounded
    /// strips are spaced by tan so the arc steps are equal angles.
    /// </summary>
    public static MeshData RoundedBox(float w, float h, float d, float r, int steps)
    {
        var half = new Vector3(w, h, d) * 0.5f;
        var inner = half - new Vector3(r);
        float[] Axis(float hh)
        {
            var a = new float[2 * steps + 2];
            var inn = hh - r;
            for (var k = steps; k >= 1; k--)
                a[steps - k] = -(inn + r * MathF.Tan(MathF.PI * 0.25f * k / steps));
            a[steps] = -inn;
            a[steps + 1] = inn;
            for (var k = 1; k <= steps; k++)
                a[steps + 1 + k] = inn + r * MathF.Tan(MathF.PI * 0.25f * k / steps);
            a[0] = -hh; // exact ends (tan(45 deg) rounding)
            a[^1] = hh;
            return a;
        }
        var axes = new[] { Axis(half.X), Axis(half.Y), Axis(half.Z) };
        static int Index(Vector3 v) => v.X != 0 ? 0 : v.Y != 0 ? 1 : 2;
        var b = new MeshBuilder();
        foreach (var (n, u, v) in BoxFaces)
        {
            var au = axes[Index(u)];
            var av = axes[Index(v)];
            var first = b.VertexCount;
            for (var j = 0; j < av.Length; j++)
                for (var i = 0; i < au.Length; i++)
                {
                    var c = n * half + u * au[i] + v * av[j];
                    var q = Vector3.Clamp(c, -inner, inner);
                    var dir = c - q;
                    var nn = dir.LengthSquared() > 1e-20f ? Vector3.Normalize(dir) : n;
                    var p = q + nn * r + new Vector3(0, half.Y, 0);
                    b.Vertex(p, nn, new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
                }
            var stride = au.Length;
            for (var j = 0; j + 1 < av.Length; j++)
                for (var i = 0; i + 1 < au.Length; i++)
                {
                    var a = first + j * stride + i;
                    b.Quad(a, a + 1, a + stride + 1, a + stride);
                }
        }
        return b.Build();
    }

    /// <summary>A ramp: full depth on the ground, a vertical back face at +z, the slope facing up and towards -z.</summary>
    public static MeshData Wedge(float w, float h, float d)
    {
        float x = w * 0.5f, z = d * 0.5f;
        var b = new MeshBuilder();
        Vector3 p0 = new(-x, 0, -z), p1 = new(x, 0, -z), p2 = new(x, 0, z), p3 = new(-x, 0, z), p4 = new(x, h, z), p5 = new(-x, h, z);
        var slope = Vector3.Normalize(new Vector3(0, d, -h));
        void Face(Vector3 n, Vector3 u, Vector3 v, params Vector3[] pts)
        {
            var first = b.VertexCount;
            foreach (var p in pts)
                b.Vertex(p, n, new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)));
            for (var k = 1; k + 1 < pts.Length; k++)
                b.Triangle(first, first + k, first + k + 1);
        }
        Face(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, p0, p1, p2, p3);
        Face(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, p3, p2, p4, p5);
        var along = Vector3.Normalize(new Vector3(0, h, d));
        Face(slope, Vector3.UnitX, along, p0, p1, p4, p5);
        Face(Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, p1, p2, p4);
        Face(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, p0, p3, p5);
        return b.Build();
    }

    /// <summary>A geodesic sphere (diameter 1, standing on y = 0): an icosahedron subdivided <paramref name="detail"/> times.
    /// Spherical uvs; vertices on triangles that straddle the u seam are duplicated so u doesn't wrap across them.</summary>
    public static MeshData IcoSphere(int detail)
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        var pts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (var i = 0; i < pts.Count; i++)
            pts[i] = Vector3.Normalize(pts[i]);
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (var level = 0; level < detail; level++)
        {
            var cache = new Dictionary<long, int>();
            int Mid(int a, int c)
            {
                var key = ((long)Math.Min(a, c) << 32) | (uint)Math.Max(a, c);
                if (cache.TryGetValue(key, out var m))
                    return m;
                pts.Add(Vector3.Normalize((pts[a] + pts[c]) * 0.5f));
                cache[key] = pts.Count - 1;
                return pts.Count - 1;
            }
            var next = new List<(int, int, int)>(faces.Count * 4);
            foreach (var (a, b2, c) in faces)
            {
                int ab = Mid(a, b2), bc = Mid(b2, c), ca = Mid(c, a);
                next.Add((a, ab, ca));
                next.Add((b2, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }
            faces = next;
        }
        var b = new MeshBuilder();
        static Vector2 Uv(Vector3 n) => new(0.5f + MathF.Atan2(n.X, n.Z) / (2 * MathF.PI), MathF.Acos(Math.Clamp(-n.Y, -1f, 1f)) / MathF.PI);
        var baseIndex = new int[pts.Count];
        for (var i = 0; i < pts.Count; i++)
            baseIndex[i] = b.Vertex(pts[i] * 0.5f + new Vector3(0, 0.5f, 0), pts[i], Uv(pts[i]));
        var wrapped = new Dictionary<int, int>(); // seam copies with u + 1
        foreach (var (a, c, d) in faces)
        {
            float ua = Uv(pts[a]).X, uc = Uv(pts[c]).X, ud = Uv(pts[d]).X;
            var seam = MathF.Max(ua, MathF.Max(uc, ud)) - MathF.Min(ua, MathF.Min(uc, ud)) > 0.5f;
            int Pick(int i, float u)
            {
                if (!seam || u >= 0.5f)
                    return baseIndex[i];
                if (!wrapped.TryGetValue(i, out var w))
                {
                    var uv = Uv(pts[i]);
                    w = b.Vertex(pts[i] * 0.5f + new Vector3(0, 0.5f, 0), pts[i], new Vector2(uv.X + 1, uv.Y));
                    wrapped[i] = w;
                }
                return w;
            }
            b.Triangle(Pick(a, ua), Pick(c, uc), Pick(d, ud));
        }
        return b.Build();
    }
}
