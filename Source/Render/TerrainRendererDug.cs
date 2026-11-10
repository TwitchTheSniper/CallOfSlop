using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Dug ground (TerrainEdits.cs) in the terrain renderer: when a chunk's dug cells change, its surface indices are
/// rewritten in place (dug cells become degenerate triangles) and the columns' faces (only where ground meets air) go
/// into a small mesh per drawing block (<see cref="DigBlocks"/>), drawn with the terrain's shader. Untouched ground costs
/// nothing; only blocks that changed are made again, for at most <see cref="PlanetRenderer.DugRebuildMs"/> a frame, so
/// neither a drill on a worked site nor a barrage stalls one.
/// </summary>
public sealed unsafe partial class TerrainRenderer
{

    private sealed class DugMesh
    {
        public GlVertexArray Vao;
        public GlBuffer Vbo, Ibo;
        public int IndexCount;
        public Vector3 Min, Max;
    }

    private readonly Dictionary<int, DugMesh> _dug = new();
    private int[] _dugSeen = [];
    private readonly Dictionary<int, int> _dugBlockSeen = new();
    private readonly HashSet<int> _dugWaiting = new();
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
        _dugBlockSeen.Clear();
        _dugVersion = -1;
    }

    private void UpdateDug(TerrainData t)
    {
        if (t.EditsIfMade is not { } e || e.Version == _dugVersion || !_hasBuffers)
            return;
        if (_dugSeen.Length != e.ChunkVersion.Length)
            _dugSeen = new int[e.ChunkVersion.Length];
        // The blocks whose ground changed, until the frame's time for it is spent (always one).
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var built = 0;
        var left = false;
        _dugWaiting.Clear();
        var keys = e.Blocks.Keys;
        var per = e.BlocksX;
        const int blocksAChunk = ChunkCells / DigBlocks.Cells;
        for (var k = 0; k < keys.Count; k++)
        {
            var key = keys[k];
            var v = e.Blocks.Version(key);
            if (_dugBlockSeen.TryGetValue(key, out var seen) && seen == v)
                continue;
            if (left || built > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= PlanetRenderer.DugRebuildMs)
            {
                left = true;
                _dugWaiting.Add(key / per / blocksAChunk * e.ChunksX + key % per / blocksAChunk);
                continue;
            }
            RebuildDugBlock(t, e, key);
            _dugBlockSeen[key] = v;
            built++;
        }
        // The surface under them: a chunk's dug cells are cut out once all its blocks are made (never a hole with nothing in it).
        for (var c = 0; c < e.ChunkVersion.Length; c++)
        {
            if (e.ChunkVersion[c] == _dugSeen[c] || left && _dugWaiting.Contains(c))
                continue;
            CutDugChunk(t, c % e.ChunksX, c / e.ChunksX, c);
            _dugSeen[c] = e.ChunkVersion[c];
        }
        if (!left)
            _dugVersion = e.Version;
    }

    /// <summary>A chunk's surface index range rewritten in place (same length: dug cells are degenerate).</summary>
    private void CutDugChunk(TerrainData t, int cx, int cz, int c)
    {
        if (c >= _chunks.Length || _chunks[c].IndexCount == 0)
            return;
        var ch = _chunks[c];
        if (_chunkScratch.Length < ChunkCells * ChunkCells * 6)
            _chunkScratch = new uint[ChunkCells * ChunkCells * 6];
        var n = ChunkIndices(t, cx, cz, _chunkScratch, out _, out _);
        if (n != ch.IndexCount)
            return;
        _vao.Bind();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo);
        fixed (uint* p = _chunkScratch)
            Native.glBufferSubData(GL_ELEMENT_ARRAY_BUFFER, (nint)ch.FirstIndex * sizeof(uint), (nint)n * sizeof(uint), p);
    }

    private void RebuildDugBlock(TerrainData t, TerrainEdits e, int c)
    {
        var per = e.BlocksX;
        _dugVerts.Clear();
        _dugIdx.Clear();
        var sink = new DugVertices { Verts = _dugVerts, Idx = _dugIdx };
        int i0 = c % per * DigBlocks.Cells, i1 = Math.Min(i0 + DigBlocks.Cells, t.CountX - 1), j0 = c / per * DigBlocks.Cells, j1 = Math.Min(j0 + DigBlocks.Cells, t.CountZ - 1);
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
        // Dug ground is loose: the rock layer's slab lines ran across every crater.
        Native.glUniform1f(_locRock, (float)Material.Dirt);
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
        Native.glUniform1f(_locRock, (float)Material.Concrete);
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

        public bool Shaded => true;

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector4 ca, Vector4 cb, Vector4 cc)
        {
            var b0 = (uint)Verts.Count;
            Verts.Add(new Vertex { Pos = a, Normal = na, Color = Pack(new Vector3(ca.X, ca.Y, ca.Z), ca.W) });
            Verts.Add(new Vertex { Pos = b, Normal = nb, Color = Pack(new Vector3(cb.X, cb.Y, cb.Z), cb.W) });
            Verts.Add(new Vertex { Pos = c, Normal = nc, Color = Pack(new Vector3(cc.X, cc.Y, cc.Z), cc.W) });
            Idx.Add(b0);
            Idx.Add(b0 + 1);
            Idx.Add(b0 + 2);
        }

        public void Smooth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd)
        {
            var b0 = (uint)Verts.Count;
            Verts.Add(new Vertex { Pos = a, Normal = na, Color = Pack(new Vector3(ca.X, ca.Y, ca.Z), ca.W) });
            Verts.Add(new Vertex { Pos = b, Normal = nb, Color = Pack(new Vector3(cb.X, cb.Y, cb.Z), cb.W) });
            Verts.Add(new Vertex { Pos = c, Normal = nc, Color = Pack(new Vector3(cc.X, cc.Y, cc.Z), cc.W) });
            Verts.Add(new Vertex { Pos = d, Normal = nd, Color = Pack(new Vector3(cd.X, cd.Y, cd.Z), cd.W) });
            Idx.Add(b0);
            Idx.Add(b0 + 1);
            Idx.Add(b0 + 2);
            Idx.Add(b0);
            Idx.Add(b0 + 2);
            Idx.Add(b0 + 3);
        }

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
