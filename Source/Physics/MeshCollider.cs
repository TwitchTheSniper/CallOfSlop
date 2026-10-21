using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A static triangle-mesh collider (complex props, terrain overhangs, models from the mesh pipeline). The physics world
/// files the whole mesh in its BVH by <see cref="Min"/>/<see cref="Max"/> and asks the mesh for triangles near a query;
/// the mesh keeps its own BVH over its triangles. Triangles are in world space and two-sided.
/// </summary>
public interface IMeshCollider
{
    /// <summary>World-space bounds of the whole mesh.</summary>
    Vector3 Min { get; }

    Vector3 Max { get; }

    int TriangleCount { get; }

    /// <summary>The corners of triangle <paramref name="index"/> (world space).</summary>
    void Triangle(int index, out Vector3 a, out Vector3 b, out Vector3 c);

    /// <summary>The triangles' BVH (items are triangle indices).</summary>
    Bvh Tree { get; }
}

/// <summary>
/// The standard <see cref="IMeshCollider"/>: vertices and an index list (three per triangle), already in world space
/// (bake the node transform in before building one). Builds its BVH once.
/// </summary>
public sealed class TriangleMeshCollider : IMeshCollider
{
    private readonly Vector3[] _vertices;
    private readonly int[] _indices;

    public Vector3 Min { get; }
    public Vector3 Max { get; }
    public int TriangleCount => _indices.Length / 3;
    public Bvh Tree { get; }

    public TriangleMeshCollider(Vector3[] vertices, int[] indices)
    {
        if (indices.Length % 3 != 0)
            throw new ArgumentException("a triangle list needs three indices per triangle");
        foreach (var i in indices)
            if ((uint)i >= (uint)vertices.Length)
                throw new ArgumentException("triangle index out of range");
        _vertices = vertices;
        _indices = indices;
        var n = indices.Length / 3;
        var mins = new Vector3[n];
        var maxs = new Vector3[n];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var t = 0; t < n; t++)
        {
            Triangle(t, out var a, out var b, out var c);
            mins[t] = Vector3.Min(a, Vector3.Min(b, c));
            maxs[t] = Vector3.Max(a, Vector3.Max(b, c));
            lo = Vector3.Min(lo, mins[t]);
            hi = Vector3.Max(hi, maxs[t]);
        }
        Min = n == 0 ? Vector3.Zero : lo;
        Max = n == 0 ? Vector3.Zero : hi;
        Tree = new Bvh(mins, maxs);
    }

    public void Triangle(int index, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        a = _vertices[_indices[index * 3]];
        b = _vertices[_indices[index * 3 + 1]];
        c = _vertices[_indices[index * 3 + 2]];
    }

    /// <summary>
    /// A collider from the mesh pipeline's collision data (welded triangles, see <see cref="MeshCollision"/>), placed in the
    /// world by <paramref name="world"/> (the node's transform). Not used by gameplay yet: map meshes stay cosmetic until a
    /// later step adds them to the physics world with <see cref="PhysicsWorld.AddMesh"/>.
    /// </summary>
    public static TriangleMeshCollider FromMesh(MeshCollision mesh, Matrix4x4 world)
    {
        var v = new Vector3[mesh.Positions.Length];
        for (var i = 0; i < v.Length; i++)
            v[i] = Vector3.Transform(mesh.Positions[i], world);
        return new TriangleMeshCollider(v, (int[])mesh.Triangles.Clone());
    }

    /// <summary>A box as 12 triangles (tests, and a stand-in until real meshes arrive).</summary>
    public static TriangleMeshCollider FromObb(in Obb box)
    {
        var v = new Vector3[8];
        for (var i = 0; i < 8; i++)
            v[i] = box.ToWorld(new Vector3((i & 1) != 0 ? box.Half.X : -box.Half.X, (i & 2) != 0 ? box.Half.Y : -box.Half.Y, (i & 4) != 0 ? box.Half.Z : -box.Half.Z));
        int[] idx =
        [
            0, 2, 1, 1, 2, 3, // -Z
            4, 5, 6, 5, 7, 6, // +Z
            0, 1, 4, 1, 5, 4, // -Y
            2, 6, 3, 3, 6, 7, // +Y
            0, 4, 2, 2, 4, 6, // -X
            1, 3, 5, 3, 7, 5, // +X
        ];
        return new TriangleMeshCollider(v, idx);
    }
}
