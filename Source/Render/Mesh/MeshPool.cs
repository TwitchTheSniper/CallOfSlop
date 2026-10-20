using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// One shared vertex buffer and one shared index buffer for every mesh (all in the <see cref="MeshVertex"/> layout).
/// Meshes are appended once and never move; a mesh is a range: first index, index count (indices already include the
/// mesh's base vertex, so draws need no base-vertex call). LODs that share their vertices (meshoptimizer LODs) share
/// one vertex range. Switching meshes therefore never rebinds a buffer: one vertex array covers everything.
///
/// The CPU copy is kept so the GPU buffers can grow (doubling) by re-uploading; normally that happens only while the
/// first map loads. Packing is CPU-only and testable headlessly; <see cref="Sync"/> does the GL part.
/// </summary>
public sealed unsafe class MeshPool
{
    /// <summary>Where one LOD of one mesh lives in the pool.</summary>
    public readonly record struct Range(int FirstIndex, int IndexCount, int BaseVertex, int VertexCount);

    private MeshVertex[] _vertices = new MeshVertex[16384];
    private uint[] _indices = new uint[32768];
    private readonly Dictionary<int, Range[]> _byAsset = new();

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    public int MeshCount => _byAsset.Count;

    /// <summary>CPU-side bytes of the packed data (the GPU holds the same, plus spare capacity).</summary>
    public long Bytes => (long)VertexCount * MeshVertex.SizeInBytes + (long)IndexCount * sizeof(uint);

    public ReadOnlySpan<MeshVertex> Vertices => _vertices.AsSpan(0, VertexCount);
    public ReadOnlySpan<uint> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>The ranges of <paramref name="asset"/>'s LODs, packing it first if it's new.</summary>
    public Range[] Add(MeshAsset asset)
    {
        if (_byAsset.TryGetValue(asset.Id, out var known))
            return known;
        var ranges = new Range[asset.Lods.Length];
        MeshVertex[]? lastVerts = null;
        int lastBase = 0;
        for (var l = 0; l < asset.Lods.Length; l++)
        {
            var m = asset.Lods[l];
            int baseVertex;
            if (ReferenceEquals(m.Vertices, lastVerts))
                baseVertex = lastBase;
            else
            {
                baseVertex = VertexCount;
                Grow(ref _vertices, VertexCount + m.Vertices.Length);
                m.Vertices.CopyTo(_vertices, VertexCount);
                VertexCount += m.Vertices.Length;
                lastVerts = m.Vertices;
                lastBase = baseVertex;
            }
            Grow(ref _indices, IndexCount + m.Indices.Length);
            for (var i = 0; i < m.Indices.Length; i++)
                _indices[IndexCount + i] = m.Indices[i] + (uint)baseVertex;
            ranges[l] = new Range(IndexCount, m.Indices.Length, baseVertex, m.Vertices.Length);
            IndexCount += m.Indices.Length;
        }
        _byAsset[asset.Id] = ranges;
        return ranges;
    }

    private static void Grow<T>(ref T[] array, int need)
    {
        if (need <= array.Length)
            return;
        var size = array.Length;
        while (size < need)
            size *= 2;
        Array.Resize(ref array, size);
    }

    // ---- GPU side -------------------------------------------------------------------------------------------------

    private int _gpuVertices, _gpuIndices, _capVertices, _capIndices;

    /// <summary>Times the GPU buffers had to be reallocated (growth).</summary>
    public int Reallocations { get; private set; }

    /// <summary>
    /// Uploads what was added since the last call into <paramref name="vbo"/> and <paramref name="ibo"/>. The vertex array
    /// that uses them must be bound (the index buffer binding belongs to it).
    /// </summary>
    public void Sync(GlBuffer vbo, GlBuffer ibo)
    {
        if (_gpuVertices == VertexCount && _gpuIndices == IndexCount)
            return;
        Native.glBindBuffer(GL_ARRAY_BUFFER, vbo);
        fixed (MeshVertex* p = _vertices)
        {
            if (VertexCount > _capVertices)
            {
                _capVertices = _vertices.Length; // the CPU array's capacity: room to grow before the next reallocation
                Native.glBufferData(GL_ARRAY_BUFFER, (nint)_capVertices * MeshVertex.SizeInBytes, null, GL_STATIC_DRAW);
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)VertexCount * MeshVertex.SizeInBytes, p);
                Reallocations++;
            }
            else
                Native.glBufferSubData(GL_ARRAY_BUFFER, (nint)_gpuVertices * MeshVertex.SizeInBytes, (nint)(VertexCount - _gpuVertices) * MeshVertex.SizeInBytes, p + _gpuVertices);
        }
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, ibo);
        fixed (uint* p = _indices)
        {
            if (IndexCount > _capIndices)
            {
                _capIndices = _indices.Length;
                Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, (nint)_capIndices * sizeof(uint), null, GL_STATIC_DRAW);
                Native.glBufferSubData(GL_ELEMENT_ARRAY_BUFFER, 0, (nint)IndexCount * sizeof(uint), p);
            }
            else
                Native.glBufferSubData(GL_ELEMENT_ARRAY_BUFFER, (nint)_gpuIndices * sizeof(uint), (nint)(IndexCount - _gpuIndices) * sizeof(uint), p + _gpuIndices);
        }
        _gpuVertices = VertexCount;
        _gpuIndices = IndexCount;
    }
}
