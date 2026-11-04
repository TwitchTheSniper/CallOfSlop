using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Tetrahedral meshes for gravity bodies (docs/space/GRAVITY.md): a box, a ball, a hollow shell and a neck (a cylinder
/// between two points), each as vertices plus four indices per tetrahedron, ready for a <see cref="TetraSource"/>. Used
/// by the self-test and by maps that build a stitched pair. Load-time only (they allocate).
/// </summary>
public static class GravityShapes
{
    public sealed class Mesh
    {
        public readonly List<Vec3d> Vertices = new();
        public readonly List<int[]> Tetras = new();
        public readonly List<double> Density = new();

        public int Vertex(Vec3d v)
        {
            Vertices.Add(v);
            return Vertices.Count - 1;
        }

        public void Tetra(int a, int b, int c, int d, double density = 1)
        {
            Tetras.Add([a, b, c, d]);
            Density.Add(density);
        }

        /// <summary>The triangular prism (a, b, c) to (a2, b2, c2) as three tetrahedra.</summary>
        public void Prism(int a, int b, int c, int a2, int b2, int c2, double density = 1)
        {
            Tetra(a, b, c, a2, density);
            Tetra(b, c, a2, b2, density);
            Tetra(c, a2, b2, c2, density);
        }

        /// <summary>Adds another mesh's tetrahedra (its vertices copied).</summary>
        public void Add(Mesh other)
        {
            var off = Vertices.Count;
            Vertices.AddRange(other.Vertices);
            for (var i = 0; i < other.Tetras.Count; i++)
            {
                var t = other.Tetras[i];
                Tetra(t[0] + off, t[1] + off, t[2] + off, t[3] + off, other.Density[i]);
            }
        }

        public TetraSource Source(double mu, string name = "") => new(Vertices.ToArray(), Tetras.ToArray(), Density.ToArray(), mu, name);
    }

    /// <summary>A box of half sizes <paramref name="half"/> as six tetrahedra round its main diagonal (the faces inside cancel).</summary>
    public static Mesh Box(Vec3d center, Vec3d half, double density = 1)
    {
        var m = new Mesh();
        for (var i = 0; i < 8; i++)
            m.Vertex(center + new Vec3d((i & 1) != 0 ? half.X : -half.X, (i & 2) != 0 ? half.Y : -half.Y, (i & 4) != 0 ? half.Z : -half.Z));
        // Kuhn's split: for each order of the three axes, the path 0 -> one axis -> two axes -> 7.
        int[][] orders = [[1, 2, 4], [1, 4, 2], [2, 1, 4], [2, 4, 1], [4, 1, 2], [4, 2, 1]];
        foreach (var o in orders)
            m.Tetra(0, o[0], o[0] | o[1], 7, density);
        return m;
    }

    /// <summary>An icosphere's triangles (unit vectors) after <paramref name="subdivisions"/> splits.</summary>
    public static (List<Vec3d> Points, List<int[]> Tris) Icosphere(int subdivisions)
    {
        var t = (1 + Math.Sqrt(5)) / 2;
        var pts = new List<Vec3d>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (var i = 0; i < pts.Count; i++)
            pts[i] = pts[i].Normalized();
        var tris = new List<int[]>
        {
            new[] { 0, 11, 5 }, new[] { 0, 5, 1 }, new[] { 0, 1, 7 }, new[] { 0, 7, 10 }, new[] { 0, 10, 11 },
            new[] { 1, 5, 9 }, new[] { 5, 11, 4 }, new[] { 11, 10, 2 }, new[] { 10, 7, 6 }, new[] { 7, 1, 8 },
            new[] { 3, 9, 4 }, new[] { 3, 4, 2 }, new[] { 3, 2, 6 }, new[] { 3, 6, 8 }, new[] { 3, 8, 9 },
            new[] { 4, 9, 5 }, new[] { 2, 4, 11 }, new[] { 6, 2, 10 }, new[] { 8, 6, 7 }, new[] { 9, 8, 1 },
        };
        for (var s = 0; s < subdivisions; s++)
        {
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(key, out var k))
                {
                    pts.Add(((pts[a] + pts[b]) * 0.5).Normalized());
                    mid[key] = k = pts.Count - 1;
                }
                return k;
            }
            var next = new List<int[]>();
            foreach (var tr in tris)
            {
                int a = Mid(tr[0], tr[1]), b = Mid(tr[1], tr[2]), c = Mid(tr[2], tr[0]);
                next.Add([tr[0], a, c]);
                next.Add([tr[1], b, a]);
                next.Add([tr[2], c, b]);
                next.Add([a, b, c]);
            }
            tris = next;
        }
        return (pts, tris);
    }

    /// <summary>A ball: an icosphere's triangles fanned to the centre.</summary>
    public static Mesh Ball(Vec3d center, double radius, int subdivisions = 2, double density = 1)
    {
        var (pts, tris) = Icosphere(subdivisions);
        var m = new Mesh();
        var c = m.Vertex(center);
        foreach (var p in pts)
            m.Vertex(center + p * radius);
        foreach (var t in tris)
            m.Tetra(c, t[0] + 1, t[1] + 1, t[2] + 1, density);
        return m;
    }

    /// <summary>A hollow shell from <paramref name="inner"/> to <paramref name="outer"/> radius (a hollow world).</summary>
    public static Mesh Shell(Vec3d center, double inner, double outer, int subdivisions = 2, double density = 1)
    {
        var (pts, tris) = Icosphere(subdivisions);
        var m = new Mesh();
        foreach (var p in pts)
            m.Vertex(center + p * inner);
        foreach (var p in pts)
            m.Vertex(center + p * outer);
        var n = pts.Count;
        foreach (var t in tris)
            m.Prism(t[0], t[1], t[2], t[0] + n, t[1] + n, t[2] + n, density);
        return m;
    }

    /// <summary>A solid cylinder from <paramref name="a"/> to <paramref name="b"/> (the neck of a stitched pair).</summary>
    public static Mesh Neck(Vec3d a, Vec3d b, double radius, int sides = 12, int rings = 4, double density = 1)
    {
        var axis = (b - a).Normalized();
        var side = Math.Abs(axis.Y) < 0.9 ? Vec3d.UnitY : Vec3d.UnitX;
        var u = Vec3d.Cross(axis, side).Normalized();
        var w = Vec3d.Cross(axis, u);
        var m = new Mesh();
        var centres = new int[rings + 1];
        var rim = new int[rings + 1, sides];
        for (var k = 0; k <= rings; k++)
        {
            var c = a + (b - a) * (k / (double)rings);
            centres[k] = m.Vertex(c);
            for (var s = 0; s < sides; s++)
            {
                var (sin, cos) = Math.SinCos(2 * Math.PI * s / sides);
                rim[k, s] = m.Vertex(c + (u * cos + w * sin) * radius);
            }
        }
        for (var k = 0; k < rings; k++)
            for (var s = 0; s < sides; s++)
            {
                var s2 = (s + 1) % sides;
                m.Prism(centres[k], rim[k, s], rim[k, s2], centres[k + 1], rim[k + 1, s], rim[k + 1, s2], density);
            }
        return m;
    }

    /// <summary>The volume of a mesh (relative density times volume summed: its mass in density units).</summary>
    public static double Mass(Mesh m)
    {
        var sum = 0.0;
        for (var i = 0; i < m.Tetras.Count; i++)
        {
            var t = m.Tetras[i];
            var v = m.Vertices;
            sum += Math.Abs(Vec3d.Dot(v[t[1]] - v[t[0]], Vec3d.Cross(v[t[2]] - v[t[0]], v[t[3]] - v[t[0]]))) / 6 * m.Density[i];
        }
        return sum;
    }
}
