using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Collision data for a mesh, ready for the physics side (an IMeshCollider can be fed straight from it):
/// <list type="bullet">
/// <item><see cref="Positions"/> + <see cref="Triangles"/>: the exact triangle soup with duplicate positions welded
/// (uv and normal seams removed), counter-clockwise seen from outside.</item>
/// <item><see cref="Bounds"/>: the axis-aligned box (the cheapest approximation: one box collider).</item>
/// <item><see cref="HullPoints"/>: a simplified convex approximation, the mesh's extreme points along 26 directions
/// (a k-DOP). Every hull point is a mesh vertex, so the hull fits inside the mesh's convex hull and around most of it;
/// a convex-hull shape or a GJK support function can use the points as they are.</item>
/// </list>
/// Nothing in the game uses this yet: meshes are cosmetic until the physics work lands (see Maps/README.md).
/// </summary>
public sealed class MeshCollision
{
    public readonly Vector3[] Positions;
    public readonly int[] Triangles;
    public readonly MeshBounds Bounds;
    public readonly Vector3[] HullPoints;

    private MeshCollision(Vector3[] positions, int[] triangles, MeshBounds bounds, Vector3[] hull)
    {
        Positions = positions;
        Triangles = triangles;
        Bounds = bounds;
        HullPoints = hull;
    }

    public int TriangleCount => Triangles.Length / 3;

    /// <summary>The box approximation as a centre and half extents (the shape the game's colliders use today).</summary>
    public (Vector3 Center, Vector3 Half) Box => (Bounds.Center, Bounds.Size * 0.5f);

    /// <summary>Builds the collision data of <paramref name="mesh"/>, optionally transformed (e.g. a model part).</summary>
    public static MeshCollision From(MeshData mesh, Matrix4x4? transform = null) => From([(mesh, transform ?? Matrix4x4.Identity)]);

    /// <summary>Builds one collision soup from several transformed meshes (a whole model).</summary>
    public static MeshCollision From(IReadOnlyList<(MeshData Mesh, Matrix4x4 Transform)> parts)
    {
        var weld = new Dictionary<Vector3, int>();
        var positions = new List<Vector3>();
        var tris = new List<int>();
        var bounds = MeshBounds.Empty;
        foreach (var (mesh, m) in parts)
        {
            var flip = m.GetDeterminant() < 0;
            var map = new int[mesh.Vertices.Length];
            for (var i = 0; i < map.Length; i++)
            {
                var p = Vector3.Transform(mesh.Vertices[i].Position, m);
                if (!weld.TryGetValue(p, out var w))
                {
                    w = positions.Count;
                    weld[p] = w;
                    positions.Add(p);
                    bounds = bounds.Include(p);
                }
                map[i] = w;
            }
            for (var i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                int a = map[mesh.Indices[i]], b = map[mesh.Indices[i + 1]], c = map[mesh.Indices[i + 2]];
                if (a == b || b == c || a == c)
                    continue;
                tris.Add(a);
                tris.Add(flip ? c : b);
                tris.Add(flip ? b : c);
            }
        }
        return new MeshCollision(positions.ToArray(), tris.ToArray(), bounds, Kdop(positions));
    }

    private static Vector3[] Kdop(List<Vector3> points)
    {
        if (points.Count == 0)
            return [];
        var picked = new List<int>();
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                for (var z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;
                    var dir = new Vector3(x, y, z);
                    int best = 0;
                    var bestDot = float.MinValue;
                    for (var i = 0; i < points.Count; i++)
                    {
                        var d = Vector3.Dot(points[i], dir);
                        if (d > bestDot + 1e-6f) // first one wins ties: deterministic
                        {
                            bestDot = d;
                            best = i;
                        }
                    }
                    if (!picked.Contains(best))
                        picked.Add(best);
                }
        picked.Sort();
        return picked.Select(i => points[i]).ToArray();
    }

    /// <summary>
    /// Is every edge shared by exactly two triangles running opposite ways? True for a closed, consistently wound
    /// (watertight) mesh.
    /// </summary>
    public bool IsClosed(out int badEdges)
    {
        var edges = new Dictionary<(int, int), int>();
        for (var i = 0; i + 2 < Triangles.Length; i += 3)
            for (var k = 0; k < 3; k++)
            {
                var e = (Triangles[i + k], Triangles[i + (k + 1) % 3]);
                edges[e] = edges.GetValueOrDefault(e) + 1;
            }
        badEdges = 0;
        foreach (var ((a, b), n) in edges)
            if (n != 1 || edges.GetValueOrDefault((b, a)) != 1)
                badEdges++;
        return badEdges == 0;
    }

    /// <summary>The enclosed volume (positive when the triangles face outward).</summary>
    public float SignedVolume()
    {
        double v = 0;
        for (var i = 0; i + 2 < Triangles.Length; i += 3)
            v += Vector3.Dot(Positions[Triangles[i]], Vector3.Cross(Positions[Triangles[i + 1]], Positions[Triangles[i + 2]]));
        return (float)(v / 6);
    }
}
