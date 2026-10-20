using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The low-poly plant meshes the foliage renderer instances, all generated in code into one shared vertex and
/// index buffer. Each <see cref="FoliageType"/> has two levels of detail: [type * 2] is the near mesh, [type * 2 + 1]
/// the cheaper far one. Small plants are modelled at scale 1 (an instance's Scale multiplies them); trees are
/// modelled 1 unit tall, so an instance's Scale is the tree's height in metres. Tree trunks are not part of these
/// meshes: they are solid boxes in the map and drawn with the rest of the boxes.
///
/// Vertex layout (8 floats): position (3), normal (3), sway weight (0 at the root, 1 at the tip; drives the wind)
/// and shade (a baked darkening towards the root, a cheap stand-in for ambient occlusion).
/// </summary>
public static class FoliageMeshes
{
    public const int FloatsPerVertex = 8;
    public const int TypeCount = 5;

    /// <summary>A range of the shared index buffer.</summary>
    public readonly record struct Range(int FirstIndex, int IndexCount);

    /// <summary>Every mesh's vertices (see the layout above).</summary>
    public static readonly float[] Vertices;

    /// <summary>Every mesh's triangles, indexing <see cref="Vertices"/> directly.</summary>
    public static readonly ushort[] Indices;

    /// <summary>[type * 2 + lod]: where each mesh is in <see cref="Indices"/>.</summary>
    public static readonly Range[] Meshes = new Range[TypeCount * 2];

    /// <summary>Per type: how far the mesh reaches sideways from its origin and how tall it is, at scale 1 (for culling bounds).</summary>
    public static readonly (float Radius, float Height)[] Extent = new (float, float)[TypeCount];

    public static Range Mesh(FoliageType t, int lod) => Meshes[(int)t * 2 + lod];

    public static int Triangles(FoliageType t, int lod) => Mesh(t, lod).IndexCount / 3;

    private static readonly List<float> V = new();
    private static readonly List<ushort> I = new();

    static FoliageMeshes()
    {
        for (var t = 0; t < TypeCount; t++)
        {
            for (var lod = 0; lod < 2; lod++)
            {
                var first = I.Count;
                var firstVertex = V.Count / FloatsPerVertex;
                Build((FoliageType)t, lod);
                Meshes[t * 2 + lod] = new Range(first, I.Count - first);
                for (var v = firstVertex; v < V.Count / FloatsPerVertex; v++)
                {
                    float x = V[v * FloatsPerVertex], y = V[v * FloatsPerVertex + 1], z = V[v * FloatsPerVertex + 2];
                    var (r, h) = Extent[t];
                    Extent[t] = (MathF.Max(r, MathF.Sqrt(x * x + z * z)), MathF.Max(h, y));
                }
            }
        }
        Vertices = V.ToArray();
        Indices = I.ToArray();
        V.Clear();
        I.Clear();
    }

    private static void Build(FoliageType t, int lod)
    {
        switch (t)
        {
            case FoliageType.Grass:
                // A clump of tapered blades crossing at different angles (near: 5 blades, far: 2 crossed cards).
                if (lod == 0)
                {
                    for (var k = 0; k < 5; k++)
                    {
                        var a = k * 2.39996f; // golden angle, so no two blades line up
                        var off = new Vector3(MathF.Cos(a * 1.7f), 0, MathF.Sin(a * 1.7f)) * 0.07f;
                        Blade(off, a, 0.16f, 0.015f, 0.5f + 0.06f * (k % 3), 0.12f + 0.03f * (k % 2));
                    }
                }
                else
                {
                    Blade(Vector3.Zero, 0.3f, 0.34f, 0.12f, 0.52f, 0.05f);
                    Blade(Vector3.Zero, 0.3f + MathF.PI / 2, 0.34f, 0.12f, 0.52f, 0.05f);
                }
                break;

            case FoliageType.Bush:
                // A lumpy leaf ball, about 1.2 m wide and 1 m tall, plus a smaller lobe (far: one coarse ball).
                if (lod == 0)
                {
                    Blob(new Vector3(0, 0.48f, 0), new Vector3(0.62f, 0.48f, 0.6f), 1, 11, 0.16f, 0f, 1f);
                    Blob(new Vector3(0.32f, 0.34f, 0.18f), new Vector3(0.36f, 0.32f, 0.36f), 1, 23, 0.14f, 0f, 1f);
                }
                else
                    Blob(new Vector3(0.05f, 0.46f, 0.03f), new Vector3(0.7f, 0.48f, 0.66f), 0, 11, 0.1f, 0f, 1f);
                break;

            case FoliageType.Shrub:
                // Fern-like fronds arching out of the ground (near: 7, far: 4).
                var fronds = lod == 0 ? 7 : 4;
                for (var k = 0; k < fronds; k++)
                    Frond(k * MathF.Tau / fronds + 0.4f * (k % 2), 0.75f + 0.12f * (k % 3), lod == 0 ? 0.2f : 0.28f, 0.55f + 0.15f * (k % 2));
                break;

            case FoliageType.Tree:
                // Broadleaf canopy for a tree 1 unit tall (the trunk box reaches 0.62): three overlapping leaf masses.
                if (lod == 0)
                {
                    Blob(new Vector3(0, 0.66f, 0), new Vector3(0.27f, 0.2f, 0.26f), 1, 5, 0.14f, 0.45f, 1f);
                    Blob(new Vector3(0.02f, 0.84f, -0.03f), new Vector3(0.18f, 0.14f, 0.18f), 1, 9, 0.14f, 0.45f, 1f);
                    Blob(new Vector3(0.14f, 0.6f, 0.1f), new Vector3(0.16f, 0.13f, 0.15f), 1, 17, 0.14f, 0.45f, 1f);
                }
                else
                {
                    Blob(new Vector3(0.02f, 0.7f, 0), new Vector3(0.29f, 0.25f, 0.28f), 0, 5, 0.08f, 0.45f, 1f);
                    Blob(new Vector3(0.02f, 0.86f, -0.03f), new Vector3(0.17f, 0.13f, 0.17f), 0, 9, 0.08f, 0.45f, 1f);
                }
                break;

            case FoliageType.Pine:
                // Conifer for a tree 1 unit tall (the trunk box reaches 0.5): stacked cones (near: 3 x 9 sides, far: 2 x 6).
                if (lod == 0)
                {
                    Cone(0.22f, 0.62f, 0.25f, 9, 0f);
                    Cone(0.44f, 0.82f, 0.19f, 9, 0.35f);
                    Cone(0.64f, 1.0f, 0.12f, 9, 0.7f);
                }
                else
                {
                    Cone(0.22f, 0.7f, 0.25f, 6, 0f);
                    Cone(0.52f, 1.0f, 0.16f, 6, 0.5f);
                }
                break;
        }
    }

    private static ushort Vert(Vector3 p, Vector3 n, float sway, float shade)
    {
        var index = V.Count / FloatsPerVertex;
        if (index > ushort.MaxValue)
            throw new InvalidOperationException("foliage meshes have too many vertices for 16-bit indices");
        n = Vector3.Normalize(n);
        V.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z, sway, shade]);
        return (ushort)index;
    }

    private static void Tri(ushort a, ushort b, ushort c) => I.AddRange([a, b, c]);

    /// <summary>A tapered blade (a quad narrowing to the tip), leaning outwards. Normals lean up, so grass lights softly from any side.</summary>
    private static void Blade(Vector3 root, float angle, float widthBottom, float widthTop, float height, float lean)
    {
        var along = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
        var facing = new Vector3(-along.Z, 0, along.X);
        var tip = root + facing * lean + new Vector3(0, height, 0);
        var n = facing * 0.35f + Vector3.UnitY;
        var a = Vert(root - along * (widthBottom / 2), n, 0f, 0.55f);
        var b = Vert(root + along * (widthBottom / 2), n, 0f, 0.55f);
        var c = Vert(tip + along * (widthTop / 2), n, 1f, 1.1f);
        var d = Vert(tip - along * (widthTop / 2), n, 1f, 1.1f);
        Tri(a, b, c);
        Tri(a, c, d);
    }

    /// <summary>A frond: a leaf-shaped strip arching out from the root at <paramref name="angle"/>.</summary>
    private static void Frond(float angle, float length, float width, float rise)
    {
        var dir = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
        var side = new Vector3(-dir.Z, 0, dir.X);
        // Root, two points along the arch, tip; the leaf is widest in the middle.
        Vector3 P(float t) => dir * (length * t) + new Vector3(0, rise * (1 - (1 - t) * (1 - t)) - 0.25f * rise * t * t, 0);
        var n = Vector3.UnitY + dir * 0.3f;
        var root = Vert(P(0), n, 0f, 0.6f);
        var l1 = Vert(P(0.35f) - side * width * 0.5f, n, 0.4f, 0.85f);
        var r1 = Vert(P(0.35f) + side * width * 0.5f, n, 0.4f, 0.85f);
        var l2 = Vert(P(0.7f) - side * width * 0.4f, n, 0.75f, 1.0f);
        var r2 = Vert(P(0.7f) + side * width * 0.4f, n, 0.75f, 1.0f);
        var tip = Vert(P(1f), n, 1f, 1.1f);
        Tri(root, r1, l1);
        Tri(l1, r1, r2);
        Tri(l1, r2, l2);
        Tri(l2, r2, tip);
    }

    /// <summary>
    /// A lumpy ellipsoid (an icosphere, subdivided <paramref name="subdiv"/> times, with each vertex pushed in or out a
    /// little by a fixed hash). Shared vertices keep it watertight; normals are smooth.
    /// </summary>
    private static void Blob(Vector3 center, Vector3 radii, int subdiv, int seed, float lumpiness, float swayBase, float swayTop)
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        var pts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (var i = 0; i < pts.Count; i++)
            pts[i] = Vector3.Normalize(pts[i]);
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (var s = 0; s < subdiv; s++)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (!mid.TryGetValue(key, out var m))
                {
                    m = pts.Count;
                    pts.Add(Vector3.Normalize(pts[a] + pts[b]));
                    mid[key] = m;
                }
                return m;
            }
            var next = new List<(int, int, int)>();
            foreach (var (a, b, c) in faces)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add((a, ab, ca));
                next.Add((b, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }
            faces = next;
        }
        var ids = new ushort[pts.Count];
        for (var i = 0; i < pts.Count; i++)
        {
            var u = pts[i];
            var bump = 1f + lumpiness * ((Foliage.Hash((uint)(i * 2654435761u) ^ (uint)seed) >> 8) * (2f / 16777216f) - 1f);
            var p = center + u * radii * bump;
            // Underside darker (self-shadowed), top brighter; sway grows with height.
            var up = u.Y * 0.5f + 0.5f;
            var n = u / radii; // ellipsoid normal direction
            ids[i] = Vert(p, n, swayBase + (swayTop - swayBase) * up, 0.62f + 0.48f * up);
        }
        foreach (var (a, b, c) in faces)
            Tri(ids[a], ids[b], ids[c]);
    }

    /// <summary>A closed cone from <paramref name="y0"/> (base, radius <paramref name="r"/>) to an apex at <paramref name="y1"/>.</summary>
    private static void Cone(float y0, float y1, float r, int sides, float phase)
    {
        var slope = r / (y1 - y0);
        var apex = new Vector3(0, y1, 0);
        var baseCenter = Vert(new Vector3(0, y0 + (y1 - y0) * 0.15f, 0), -Vector3.UnitY, 0.3f + 0.6f * y0, 0.5f);
        for (var k = 0; k < sides; k++)
        {
            float a0 = (k + phase) * MathF.Tau / sides, a1 = (k + 1 + phase) * MathF.Tau / sides, am = (a0 + a1) / 2;
            var d0 = new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0));
            var d1 = new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1));
            var dm = new Vector3(MathF.Cos(am), 0, MathF.Sin(am));
            // Separate vertices per side: faceted shading reads as layered needles.
            var n = dm + new Vector3(0, slope, 0);
            var b0 = Vert(new Vector3(0, y0, 0) + d0 * r, n, 0.3f + 0.6f * y0, 0.7f);
            var b1 = Vert(new Vector3(0, y0, 0) + d1 * r, n, 0.3f + 0.6f * y0, 0.7f);
            var top = Vert(apex, n, 0.3f + 0.6f * y1, 1.1f);
            Tri(b0, top, b1);
            Tri(b0, b1, baseCenter); // underside, slightly raised so it reads as a dark hollow
        }
    }
}
