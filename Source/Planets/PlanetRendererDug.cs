using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Dug ground on the planetoid (PlanetEdits.cs) in its drawing: level-0 pages over dug cells get a mask (one byte a cell,
/// with a ring of the next pages' cells) and the ground shader leaves those cells and the skirts under them out; the
/// columns' faces go into a small mesh per drawing block (<see cref="DigBlocks"/>), positions relative to the block's own
/// origin (worked out in doubles), drawn with the ground's fragment shader. Only blocks that changed are made again, for
/// at most <see cref="DugRebuildMs"/> a frame, so neither a drill on a worked site nor a barrage stalls one.
/// </summary>
public sealed unsafe partial class PlanetRenderer
{
    /// <summary>The time (ms) a frame may spend making changed blocks' meshes again; the rest wait for the next frames.</summary>
    public const double DugRebuildMs = 2.0;

    private const int DugSide = PageCells + 2;
    private const int DugUnit = 8; // (0: the box texture array, 5-7 and 9: other renderers, 10-11: the pages, 12-14: the light grid)

    private const string DugVertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos;   // from the chunk's origin
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aColor; // colour, rockiness
        uniform mat4 uViewProj;
        uniform vec3 uCenter;
        uniform vec3 uTexOrigin;
        uniform vec3 uOrigin;
        out vec3 vWorld;
        out vec3 vTex;
        out vec3 vNormal;
        out vec3 vUp;
        out vec4 vColor;

        void main()
        {
            vec3 world = uOrigin + aPos;
            vWorld = world;
            vTex = world - uTexOrigin;
            vNormal = aNormal;
            vUp = normalize(world - uCenter);
            vColor = aColor;
            gl_Position = uViewProj * vec4(world, 1.0);
        }
        """;

    private readonly int[] _pageDugStamp = new int[MaxPages];
    private readonly bool[] _pageDug = new bool[MaxPages];
    private readonly byte[] _dugScratch = new byte[DugSide * DugSide];
    private uint _dugTex;

    private GlProgram _dugProgram;
    private int _dlViewProj, _dlCenter, _dlTexOrigin, _dlOrigin, _dlTex, _dlGround, _dlRock, _dlFog, _dlFogRange, _dlToLocal, _dlDip, _dlCam;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DugVertex
    {
        public Vector3 Pos, Normal;
        public Vector4 Color;
    }

    private sealed class DugMesh
    {
        public GlVertexArray Vao;
        public GlBuffer Vbo, Ibo;
        public int IndexCount;
        public Vector3 Origin, Min, Max;
    }

    private readonly Dictionary<int, DugMesh> _dug = new();
    private readonly Dictionary<int, int> _dugSeen = new();
    private int _dugVersion = -1, _dugGeneration = -1, _maskVersion = -1, _maskMade = -1;
    private bool _dugLeft;
    private readonly List<DugVertex> _dugVerts = new();
    private readonly List<uint> _dugIdx = new();

    /// <summary>Level-0 pages with a dug mask, chunks drawn as blocks and the triangles in them (the self-test, the perf window).</summary>
    public (int MaskedPages, int Chunks, int Triangles) DugStats
    {
        get
        {
            var masked = 0;
            for (var p = 0; p < MaxPages; p++)
                if (_pageDug[p])
                    masked++;
            var tris = 0;
            foreach (var m in _dug.Values)
                tris += m.IndexCount / 3;
            return (masked, _dug.Count, tris);
        }
    }

    /// <summary>Is every changed dug block rebuilt?</summary>
    public bool DugSettled => !_dugLeft;

    private void ResetDug()
    {
        FreeDug();
        _dugSeen.Clear();
        _dugVersion = -1;
        _dugGeneration = -1;
        _maskVersion = -1;
        _dugLeft = false;
    }

    /// <summary>Each frame: masks for level-0 pages over dug cells, then the changed blocks' meshes.</summary>
    private void UpdateDug(Planetoid pl)
    {
        if (pl.EditsIfMade is not { } e)
            return;
        if (e.Generation != _dugGeneration)
        {
            FreeDug();
            _dugSeen.Clear();
            _dugGeneration = e.Generation;
            _dugVersion = -1;
        }
        if (e.Version != _maskVersion || _made != _maskMade)
        {
            UpdateDugMasks(e); // (only when the ground or the pages held changed)
            _maskVersion = e.Version;
            _maskMade = _made;
        }
        if (e.Version == _dugVersion && !_dugLeft)
            return;
        // The blocks whose ground changed, until the frame's time for it is spent (always one).
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var built = 0;
        _dugLeft = false;
        var keys = e.Blocks.Keys;
        for (var k = 0; k < keys.Count; k++)
        {
            var key = keys[k];
            var v = e.Blocks.Version(key);
            if (_dugSeen.TryGetValue(key, out var seen) && seen == v)
                continue;
            if (built > 0 && System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds >= DugRebuildMs)
            {
                _dugLeft = true;
                break;
            }
            RebuildDugBlock(pl, e, key);
            _dugSeen[key] = v;
            built++;
        }
        _dugVersion = e.Version;
    }

    /// <summary>Level-0 pages get a mask of their dug cells (redone when a chunk under them, or its generation, changes).</summary>
    private void UpdateDugMasks(PlanetEdits e)
    {
        var per = e.ChunksPerSide;
        for (var p = 0; p < MaxPages; p++)
        {
            var key = _pageKey[p];
            if (key < 0 || ((key >> 48) & 0xFF) != 0)
                continue;
            var face = (int)(key >> 56);
            int nx = (int)(key & 0xFFFFFF), nz = (int)((key >> 24) & 0xFFFFFF);
            // The page's 64 cells are 2 x 2 chunks; its ring reaches one chunk further each way (the same face's: a ring
            // cell over a seam is read through its direction, but a change there alone doesn't redo the mask).
            int cx = nx * 2, cz = nz * 2;
            var stamp = e.Generation * 7919;
            for (var b = -1; b < 3; b++)
            for (var a = -1; a < 3; a++)
                if ((uint)(cx + a) < (uint)per && (uint)(cz + b) < (uint)per)
                    stamp = stamp * 31 + e.ChunkVersion((face * per + cz + b) * per + cx + a);
            if (stamp == _pageDugStamp[p])
                continue;
            _pageDugStamp[p] = stamp;
            var dug = false;
            int i0 = nx * PageCells - 1, j0 = nz * PageCells - 1;
            for (var j = 0; j < DugSide; j++)
            for (var i = 0; i < DugSide; i++)
            {
                var edge = i == 0 || j == 0 || i == DugSide - 1 || j == DugSide - 1;
                var d = edge ? e.TouchedAround(face, i0 + i, j0 + j) : e.Touched(face, i0 + i, j0 + j);
                _dugScratch[j * DugSide + i] = d ? (byte)255 : (byte)0;
                dug |= d;
            }
            _pageDug[p] = dug;
            if (dug && !_headless && _glReady)
            {
                Native.glActiveTexture(GL_TEXTURE0 + DugUnit);
                Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _dugTex);
                Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1); // (rows of 66 bytes)
                fixed (byte* bp = _dugScratch)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, p, DugSide, DugSide, 1, GL_RED, GL_UNSIGNED_BYTE, bp);
                Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
                Native.glActiveTexture(GL_TEXTURE0);
            }
        }
    }

    private void RebuildDugBlock(Planetoid pl, PlanetEdits e, int key)
    {
        e.BlockOf(key, out var face, out var i0, out var j0);
        // The block's origin: its middle on the sphere (float exactly; every vertex is worked out relative to it in doubles).
        pl.DirOf(face, i0 + DigBlocks.Cells / 2.0, j0 + DigBlocks.Cells / 2.0, out var x, out var y, out var z);
        var origin = new Vector3((float)(pl.Cx + x * pl.Radius), (float)(pl.Cy + y * pl.Radius), (float)(pl.Cz + z * pl.Radius));
        _dugVerts.Clear();
        _dugIdx.Clear();
        var sink = new DugSink { Verts = _dugVerts, Idx = _dugIdx };
        var n = pl.N;
        for (var j = j0; j < Math.Min(j0 + DigBlocks.Cells, n); j++)
        for (var i = i0; i < Math.Min(i0 + DigBlocks.Cells, n); i++)
            e.CellFaces(face, i, j, origin.X, origin.Y, origin.Z, ref sink);
        if (_dug.Remove(key, out var old))
            FreeDugMesh(old);
        if (_dugIdx.Count == 0)
            return;
        var m = new DugMesh { IndexCount = _dugIdx.Count, Origin = origin, Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
        foreach (var v in _dugVerts)
        {
            m.Min = Vector3.Min(m.Min, origin + v.Pos);
            m.Max = Vector3.Max(m.Max, origin + v.Pos);
        }
        _dug[key] = m;
        if (_headless || !_glReady)
            return;
        m.Vao = GenVertexArray();
        m.Vao.Bind();
        m.Vbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, m.Vbo);
        var verts = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_dugVerts);
        fixed (DugVertex* p = verts)
            Native.glBufferData(GL_ARRAY_BUFFER, verts.Length * sizeof(DugVertex), p, GL_STATIC_DRAW);
        m.Ibo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, m.Ibo);
        var idx = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_dugIdx);
        fixed (uint* p = idx)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, idx.Length * sizeof(uint), p, GL_STATIC_DRAW);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, sizeof(DugVertex), (void*)0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, sizeof(DugVertex), (void*)12);
        Native.glVertexAttribPointer(2, 4, GL_FLOAT, false, sizeof(DugVertex), (void*)24);
        for (uint a = 0; a < 3; a++)
            EnableVertexAttribArray(a);
        _vao.Bind();
    }

    private void DrawDug(Renderer gfx, Planetoid pl, ReadOnlySpan<Vector4> planes, Vector3 texOrigin, Matrix3x3 toLocal)
    {
        if (_dug.Count == 0)
            return;
        var vp = gfx.ViewProj;
        _dugProgram.Use();
        Native.glUniformMatrix4fv(_dlViewProj, 1, false, &vp.M11);
        var cam = gfx.CameraPosition;
        Native.glUniform3f(_dlCam, cam.X, cam.Y, cam.Z);
        var c = pl.Center;
        Native.glUniform3f(_dlCenter, c.X, c.Y, c.Z);
        Native.glUniform3f(_dlTexOrigin, texOrigin.X, texOrigin.Y, texOrigin.Z);
        Native.glUniformMatrix3fv(_dlToLocal, 1, false, &toLocal.M11);
        Native.glUniform1f(_dlDip, HorizonDip(pl, cam));
        Native.glUniform1i(_dlTex, 0);
        Native.glUniform1f(_dlGround, (float)Material.Dirt);
        Native.glUniform1f(_dlRock, (float)Material.Dirt); // (dug ground is loose: the rock layer's slab lines ran across every crater)
        var fog = gfx.FogColor;
        Native.glUniform3f(_dlFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_dlFogRange, gfx.FogRange.X, gfx.FogRange.Y);
        // Past the next level the ground over a chunk has no mask, so its blocks are under that ground (a heap on top is too
        // small to see from there), and no chunk past the horizon shows.
        var reach = _range[Math.Min(2, _levels - 1)];
        foreach (var m in _dug.Values)
        {
            if (m.IndexCount == 0 || !SphereTouchesBox(cam, reach, m.Min, m.Max) || !TerrainRenderer.Visible(planes, m.Min, m.Max))
                continue;
            Native.glUniform3f(_dlOrigin, m.Origin.X, m.Origin.Y, m.Origin.Z);
            m.Vao.Bind();
            PerfCounters.Draw(m.IndexCount);
            Native.glDrawElements(GL_TRIANGLES, m.IndexCount, GL_UNSIGNED_INT, (void*)0);
        }
        _vao.Bind();
    }

    private void InitDugGl()
    {
        GlTexture dug = default;
        Native.glGenTextures(1, &dug);
        _dugTex = dug.Id;
        Native.glActiveTexture(GL_TEXTURE0 + DugUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _dugTex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_R8, DugSide, DugSide, MaxPages, 0, GL_RED, GL_UNSIGNED_BYTE, null);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        // The masks of pages made before GL was ready are sent again.
        for (var p = 0; p < MaxPages; p++)
            _pageDugStamp[p] = -1;
        _maskVersion = -1;

        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(DugVertexSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
        _dugProgram = CreateProgram();
        Native.glAttachShader(_dugProgram, vs);
        Native.glAttachShader(_dugProgram, fs);
        Native.glLinkProgram(_dugProgram);
        GlDebug.Label(GL_PROGRAM, _dugProgram.Id, "PlanetRenderer dug");
        int linked;
        Native.glGetProgramiv(_dugProgram, GL_LINK_STATUS, &linked);
        vs.Dispose();
        fs.Dispose();
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_dugProgram, 2048, &len, log);
            throw new InvalidOperationException("planet dug shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        _dlViewProj = Uniform(_dugProgram, "uViewProj\0"u8);
        _dlCam = Uniform(_dugProgram, "uCam\0"u8);
        _dlCenter = Uniform(_dugProgram, "uCenter\0"u8);
        _dlTexOrigin = Uniform(_dugProgram, "uTexOrigin\0"u8);
        _dlOrigin = Uniform(_dugProgram, "uOrigin\0"u8);
        _dlTex = Uniform(_dugProgram, "uTex\0"u8);
        _dlGround = Uniform(_dugProgram, "uGroundLayer\0"u8);
        _dlRock = Uniform(_dugProgram, "uRockLayer\0"u8);
        _dlFog = Uniform(_dugProgram, "uFog\0"u8);
        _dlFogRange = Uniform(_dugProgram, "uFogRange\0"u8);
        _dlToLocal = Uniform(_dugProgram, "uToLocal\0"u8);
        _dlDip = Uniform(_dugProgram, "uDip\0"u8);
        Lighting.Upload(_dugProgram);
        // Chunks built headless (before GL) are built again with buffers.
        _dugSeen.Clear();
        _dugVersion = -1;
        foreach (var m in _dug.Values)
            m.IndexCount = 0;
    }

    private void FreeDug()
    {
        foreach (var m in _dug.Values)
            FreeDugMesh(m);
        _dug.Clear();
    }

    private void FreeDugMesh(DugMesh m)
    {
        if (_headless || m.Vao.Id == 0)
            return;
        var vao = m.Vao;
        Native.glDeleteVertexArrays(1, &vao);
        GlBuffer* bufs = stackalloc GlBuffer[2];
        bufs[0] = m.Vbo;
        bufs[1] = m.Ibo;
        Native.glDeleteBuffers(2, bufs);
    }

    /// <summary>Dug faces into the dug mesh's vertex format (two triangles a quad, flat normals).</summary>
    private struct DugSink : IDigFaceSink
    {
        public List<DugVertex> Verts;
        public List<uint> Idx;

        public bool Shaded => true;

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector4 ca, Vector4 cb, Vector4 cc)
        {
            var b0 = (uint)Verts.Count;
            Verts.Add(new DugVertex { Pos = a, Normal = na, Color = ca });
            Verts.Add(new DugVertex { Pos = b, Normal = nb, Color = cb });
            Verts.Add(new DugVertex { Pos = c, Normal = nc, Color = cc });
            Idx.Add(b0);
            Idx.Add(b0 + 1);
            Idx.Add(b0 + 2);
        }

        public void Smooth(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector4 ca, Vector4 cb, Vector4 cc, Vector4 cd)
        {
            var b0 = (uint)Verts.Count;
            Verts.Add(new DugVertex { Pos = a, Normal = na, Color = ca });
            Verts.Add(new DugVertex { Pos = b, Normal = nb, Color = cb });
            Verts.Add(new DugVertex { Pos = c, Normal = nc, Color = cc });
            Verts.Add(new DugVertex { Pos = d, Normal = nd, Color = cd });
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
            var col = new Vector4(color, Math.Clamp(rock, 0f, 1f));
            Verts.Add(new DugVertex { Pos = a, Normal = n, Color = col });
            Verts.Add(new DugVertex { Pos = b, Normal = n, Color = col });
            Verts.Add(new DugVertex { Pos = c, Normal = n, Color = col });
            Verts.Add(new DugVertex { Pos = d, Normal = n, Color = col });
            Idx.Add(b0);
            Idx.Add(b0 + 1);
            Idx.Add(b0 + 2);
            Idx.Add(b0);
            Idx.Add(b0 + 2);
            Idx.Add(b0 + 3);
        }
    }
}
