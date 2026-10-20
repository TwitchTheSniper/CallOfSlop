using System.Numerics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// One vertex of the shared mesh format (40 bytes): position, normal, texture coordinates, a texture layer and an RGBA8
/// colour. Every mesh in the game (primitives and glTF models) uses this one layout, so they all live in the same
/// <see cref="MeshPool"/> buffers and draw with one vertex array and one shader.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MeshVertex
{
    /// <summary>No texture: plain (vertex colour * instance colour).</summary>
    public const float NoLayer = -1f;

    /// <summary>Layers from this value up are imported model textures (<see cref="MeshTextures"/>); below it, the procedural <see cref="Material"/> layers.</summary>
    public const float ImportedLayerBase = 100f;

    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;

    /// <summary><see cref="NoLayer"/>, a procedural <see cref="Material"/> (world-space mapped like the boxes), or <see cref="ImportedLayerBase"/> + an imported texture (mesh uvs).</summary>
    public float Layer;

    /// <summary>RGBA8, red in the lowest byte (sRGB-ish, like every colour in the game).</summary>
    public uint Color;

    public const int SizeInBytes = 40;

    public MeshVertex(Vector3 position, Vector3 normal, Vector2 uv, uint color = 0xFFFFFFFF, float layer = NoLayer)
    {
        Position = position;
        Normal = normal;
        Uv = uv;
        Layer = layer;
        Color = color;
    }

    /// <summary>Packs a 0-1 colour into RGBA8.</summary>
    public static uint Pack(Vector4 c)
    {
        static uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        return B(c.X) | (B(c.Y) << 8) | (B(c.Z) << 16) | (B(c.W) << 24);
    }

    public static Vector4 Unpack(uint c) => new((c & 255) / 255f, ((c >> 8) & 255) / 255f, ((c >> 16) & 255) / 255f, (c >> 24) / 255f);
}

/// <summary>An axis-aligned bounding box.</summary>
public readonly record struct MeshBounds(Vector3 Min, Vector3 Max)
{
    public static readonly MeshBounds Empty = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public bool IsEmpty => Min.X > Max.X;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;

    /// <summary>Radius of the sphere around <see cref="Center"/> that holds the box.</summary>
    public float Radius => (Max - Min).Length() * 0.5f;

    public MeshBounds Include(Vector3 p) => new(Vector3.Min(Min, p), Vector3.Max(Max, p));

    public MeshBounds Union(MeshBounds b) => b.IsEmpty ? this : IsEmpty ? b : new(Vector3.Min(Min, b.Min), Vector3.Max(Max, b.Max));

    /// <summary>The box around this box after a transform (row-vector convention, like the rest of the game).</summary>
    public MeshBounds Transform(in Matrix4x4 m)
    {
        if (IsEmpty)
            return this;
        var r = Empty;
        for (var i = 0; i < 8; i++)
            r = r.Include(Vector3.Transform(new Vector3((i & 1) != 0 ? Max.X : Min.X, (i & 2) != 0 ? Max.Y : Min.Y, (i & 4) != 0 ? Max.Z : Min.Z), m));
        return r;
    }

    public static MeshBounds Of(ReadOnlySpan<MeshVertex> vertices)
    {
        var b = Empty;
        foreach (ref readonly var v in vertices)
            b = b.Include(v.Position);
        return b;
    }

    public override string ToString() => $"({Min.X:0.###}, {Min.Y:0.###}, {Min.Z:0.###})-({Max.X:0.###}, {Max.Y:0.###}, {Max.Z:0.###})";
}

/// <summary>
/// A CPU-side triangle mesh in the shared vertex format: 32-bit indices, counter-clockwise outward-facing triangles.
/// Immutable once built (the arrays are shared between LODs, the pool and the collision data).
/// </summary>
public sealed class MeshData
{
    public readonly MeshVertex[] Vertices;
    public readonly uint[] Indices;
    public readonly MeshBounds Bounds;

    public MeshData(MeshVertex[] vertices, uint[] indices)
    {
        Vertices = vertices;
        Indices = indices;
        Bounds = MeshBounds.Of(vertices);
    }

    public int TriangleCount => Indices.Length / 3;

    /// <summary>A stable fingerprint of the vertex and index bytes (FNV-1a), for determinism checks.</summary>
    public ulong Hash()
    {
        var h = 14695981039346656037ul;
        foreach (var b in MemoryMarshal.AsBytes(Vertices.AsSpan()))
            h = (h ^ b) * 1099511628211ul;
        foreach (var b in MemoryMarshal.AsBytes(Indices.AsSpan()))
            h = (h ^ b) * 1099511628211ul;
        return h;
    }

    /// <summary>A copy with every position and normal transformed (normals by the inverse transpose, then renormalised).</summary>
    public MeshData Transformed(in Matrix4x4 m)
    {
        Matrix4x4.Invert(m, out var inv);
        var nm = Matrix4x4.Transpose(inv);
        var v = (MeshVertex[])Vertices.Clone();
        for (var i = 0; i < v.Length; i++)
        {
            v[i].Position = Vector3.Transform(v[i].Position, m);
            var n = Vector3.TransformNormal(v[i].Normal, nm);
            v[i].Normal = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
        }
        var idx = (uint[])Indices.Clone();
        if (m.GetDeterminant() < 0) // a mirror flips the winding: put it back
            for (var i = 0; i + 2 < idx.Length; i += 3)
                (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
        return new MeshData(v, idx);
    }

    /// <summary>A copy with the given colour and texture layer on every vertex (multiplied into the existing colour).</summary>
    public MeshData Painted(Vector4 color, float layer)
    {
        var v = (MeshVertex[])Vertices.Clone();
        for (var i = 0; i < v.Length; i++)
        {
            v[i].Color = MeshVertex.Pack(MeshVertex.Unpack(v[i].Color) * color);
            if (layer != MeshVertex.NoLayer)
                v[i].Layer = layer;
        }
        return new MeshData(v, Indices);
    }

    /// <summary>Several meshes as one (vertex and index arrays appended).</summary>
    public static MeshData Merge(IReadOnlyList<MeshData> parts)
    {
        var vc = parts.Sum(p => p.Vertices.Length);
        var ic = parts.Sum(p => p.Indices.Length);
        var v = new MeshVertex[vc];
        var idx = new uint[ic];
        int vo = 0, io = 0;
        foreach (var p in parts)
        {
            p.Vertices.CopyTo(v, vo);
            for (var i = 0; i < p.Indices.Length; i++)
                idx[io + i] = p.Indices[i] + (uint)vo;
            vo += p.Vertices.Length;
            io += p.Indices.Length;
        }
        return new MeshData(v, idx);
    }
}

/// <summary>
/// Builds a <see cref="MeshData"/>: add vertices, then triangles. <see cref="Triangle"/> orients every triangle by its
/// vertex normals (counter-clockwise seen from the side the normals point to) and drops degenerate ones, so generators
/// only have to get the normals right.
/// </summary>
public sealed class MeshBuilder
{
    private readonly List<MeshVertex> _v = new();
    private readonly List<uint> _i = new();

    public int VertexCount => _v.Count;

    public int Vertex(Vector3 p, Vector3 n, Vector2 uv)
    {
        _v.Add(new MeshVertex(p, n, uv));
        return _v.Count - 1;
    }

    public void Triangle(int a, int b, int c)
    {
        var pa = _v[a].Position;
        Vector3 e1 = _v[b].Position - pa, e2 = _v[c].Position - pa;
        var fn = Vector3.Cross(e1, e2);
        var lens = e1.LengthSquared() * e2.LengthSquared();
        if (lens <= 1e-24f || fn.LengthSquared() <= 1e-10f * lens || (_v[b].Position - _v[c].Position).LengthSquared() <= 1e-12f)
            return; // degenerate (a pole or an apex): no triangle
        if (Vector3.Dot(fn, _v[a].Normal + _v[b].Normal + _v[c].Normal) < 0)
            (b, c) = (c, b);
        _i.Add((uint)a);
        _i.Add((uint)b);
        _i.Add((uint)c);
    }

    /// <summary>Two triangles a-b-c and a-c-d.</summary>
    public void Quad(int a, int b, int c, int d)
    {
        Triangle(a, b, c);
        Triangle(a, c, d);
    }

    public MeshData Build() => new(_v.ToArray(), _i.ToArray());
}
