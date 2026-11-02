using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Dug ground (TerrainEdits.cs) in the terrain renderer: when a chunk's dug cells change, its surface indices are
/// rewritten in place (dug cells become degenerate triangles) and its blocks (only the faces where ground meets air) go
/// into a small mesh of their own, drawn with the terrain's shader. Untouched chunks cost nothing; a few chunks are
/// rebuilt per frame at most, so a barrage never stalls a frame.
/// </summary>
public sealed unsafe partial class TerrainRenderer
{
    /// <summary>Most dug chunks rebuilt in one frame (the rest wait for the next frames).</summary>
    private const int DugRebuildsPerFrame = 3;

    private sealed class DugMesh
    {
        public GlVertexArray Vao;
        public GlBuffer Vbo, Ibo;
        public int IndexCount;
        public Vector3 Min, Max;
    }

    private readonly Dictionary<int, DugMesh> _dug = new();
    private int[] _dugSeen = [];
    private int _dugVersion = -1;
    private readonly List<Vertex> _dugVerts = new();
    private readonly List<uint> _dugIdx = new();
    private uint[] _chunkScratch = [];

    /// <summary>Dug chunks drawn as blocks right now, and the triangles in them (self-test, perf window).</summary>
    public (int Chunks, int Triangles) DugStats
    {
        get
        {
            var tris = 0;
            foreach (var m in _dug.Values)
                tris += m.IndexCount / 3;
            return (_dug.Count, tris);
        }
    }

    private void ResetDug(TerrainData t)
    {
        FreeDug();
        var e = t.EditsIfMade;
        _dugSeen = new int[e?.ChunkVersion.Length ?? 0];
        _dugVersion = -1;
    }

    private void UpdateDug(TerrainData t)
    {
        if (t.EditsIfMade is not { } e || e.Version == _dugVersion || !_hasBuffers)
            return;
        if (_dugSeen.Length != e.ChunkVersion.Length)
            _dugSeen = new int[e.ChunkVersion.Length];
        var built = 0;
        var left = false;
        for (var c = 0; c < e.ChunkVersion.Length; c++)
        {
            if (e.ChunkVersion[c] == _dugSeen[c])
                continue;
            if (built == DugRebuildsPerFrame)
            {
                left = true;
                break;
            }
            RebuildDugChunk(t, e, c);
            _dugSeen[c] = e.ChunkVersion[c];
            built++;
        }
        if (!left)
            _dugVersion = e.Version;
    }

    private void RebuildDugChunk(TerrainData t, TerrainEdits e, int c)
    {
        int cx = c % e.ChunksX, cz = c / e.ChunksX;
        // The surface: this chunk's index range, rewritten in place (same length: dug cells are degenerate).
        if (c < _chunks.Length && _chunks[c].IndexCount > 0)
        {
            var ch = _chunks[c];
            if (_chunkScratch.Length < ChunkCells * ChunkCells * 6)
                _chunkScratch = new uint[ChunkCells * ChunkCells * 6];
            var n = ChunkIndices(t, cx, cz, _chunkScratch, out _, out _);
            if (n == ch.IndexCount)
            {
                _vao.Bind();
                Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo);
                fixed (uint* p = _chunkScratch)
                    Native.glBufferSubData(GL_ELEMENT_ARRAY_BUFFER, (nint)ch.FirstIndex * sizeof(uint), (nint)n * sizeof(uint), p);
            }
        }
        // The blocks.
        _dugVerts.Clear();
        _dugIdx.Clear();
        var sink = new DugVertices { Verts = _dugVerts, Idx = _dugIdx };
        int i0 = cx * ChunkCells, i1 = Math.Min(i0 + ChunkCells, t.CountX - 1), j0 = cz * ChunkCells, j1 = Math.Min(j0 + ChunkCells, t.CountZ - 1);
        for (var j = j0; j < j1; j++)
        for (var i = i0; i < i1; i++)
            e.CellFaces(i, j, ref sink, smooth: true);
        if (_dug.Remove(c, out var old))
            FreeDugMesh(old);
        if (_dugIdx.Count == 0)
            return;
        var m = new DugMesh { IndexCount = _dugIdx.Count, Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
        foreach (var v in _dugVerts)
        {
            m.Min = Vector3.Min(m.Min, v.Pos);
            m.Max = Vector3.Max(m.Max, v.Pos);
        }
        m.Vao = GenVertexArray();
        m.Vao.Bind();
        m.Vbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, m.Vbo);
        var verts = CollectionsMarshalSpan(_dugVerts);
        fixed (Vertex* p = verts)
            Native.glBufferData(GL_ARRAY_BUFFER, verts.Length * sizeof(Vertex), p, GL_STATIC_DRAW);
        m.Ibo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, m.Ibo);
        var idx = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_dugIdx);
        fixed (uint* p = idx)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, idx.Length * sizeof(uint), p, GL_STATIC_DRAW);
        SetupAttributes(sizeof(Vertex));
        _dug[c] = m;
        _vao.Bind();
    }

    private static Span<Vertex> CollectionsMarshalSpan(List<Vertex> l) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(l);

    private void DrawDug(ReadOnlySpan<Vector4> planes)
    {
        if (_dug.Count == 0)
            return;
        foreach (var m in _dug.Values)
        {
            if (!Visible(planes, m.Min, m.Max))
                continue;
            m.Vao.Bind();
            PerfCounters.Draw(m.IndexCount);
            Native.glDrawElements(GL_TRIANGLES, m.IndexCount, GL_UNSIGNED_INT, (void*)0);
            _drawCalls++;
            _visibleTris += m.IndexCount / 3;
        }
        _vao.Bind();
    }

    private void FreeDug()
    {
        foreach (var m in _dug.Values)
            FreeDugMesh(m);
        _dug.Clear();
        _dugVersion = -1;
    }

    private static void FreeDugMesh(DugMesh m)
    {
        var vao = m.Vao;
        Native.glDeleteVertexArrays(1, &vao);
        GlBuffer* bufs = stackalloc GlBuffer[2];
        bufs[0] = m.Vbo;
        bufs[1] = m.Ibo;
        Native.glDeleteBuffers(2, bufs);
    }

    /// <summary>Dug faces into the terrain's vertex format (two triangles a quad, flat normals).</summary>
    internal struct DugVertices : IDigFaceSink
    {
        public List<Vertex> Verts;
        public List<uint> Idx;

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector3 color, float rock)
        {
            var b0 = (uint)Verts.Count;
            var col = Pack(color, rock);
            Verts.Add(new Vertex { Pos = a, Normal = n, Color = col });
            Verts.Add(new Vertex { Pos = b, Normal = n, Color = col });
            Verts.Add(new Vertex { Pos = c, Normal = n, Color = col });
            Verts.Add(new Vertex { Pos = d, Normal = n, Color = col });
            Idx.Add(b0);
            Idx.Add(b0 + 1);
            Idx.Add(b0 + 2);
            Idx.Add(b0);
            Idx.Add(b0 + 2);
            Idx.Add(b0 + 3);
        }
    }
}
