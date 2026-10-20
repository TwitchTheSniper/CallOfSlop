using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a map's terrain. The mesh is built once per map into GPU buffers (one vertex buffer for the whole grid,
/// one index buffer laid out chunk by chunk), then each frame only the 32 x 32-cell chunks inside the view frustum are
/// drawn, with neighbouring visible chunks merged into one draw call. Its own small shader does smooth per-vertex
/// lighting, grass/dirt/rock colour blending (baked into the vertices), a triplanar rock texture on steep ground and
/// the same distance fog as the box shader. Rebuilt automatically when the map changes (map vote, hot reload, lobby).
/// </summary>
public sealed unsafe class TerrainRenderer
{
    private const int ChunkCells = 32;

    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 aColor; // rgb = surface colour, a = rockiness
        uniform mat4 uViewProj;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColor;
        void main()
        {
            vWorld = aPos;
            vNormal = aNormal;
            vColor = aColor;
            gl_Position = uViewProj * vec4(aPos, 1.0);
        }
        """;

    // Sun, ambient and fog come from Lighting.Glsl (shared with the box and foliage renderers).
    private static readonly string FragmentSrc = "#version 330 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        uniform sampler2DArray uTex;
        uniform float uGroundLayer;
        uniform float uRockLayer;
        out vec4 FragColor;

        void main()
        {
            vec3 n = normalize(vNormal);
            // Ground detail mapped from above (2 m per repeat); rock mapped from all three sides (4 m) so cliffs don't smear.
            float ground = texture(uTex, vec3(vWorld.xz * 0.5, uGroundLayer)).r;
            float rock = 0.0;
            if (vColor.a > 0.01)
            {
                vec3 w = abs(n);
                w /= (w.x + w.y + w.z);
                rock = texture(uTex, vec3(vWorld.zy * 0.25, uRockLayer)).r * w.x
                     + texture(uTex, vec3(vWorld.xz * 0.25, uRockLayer)).r * w.y
                     + texture(uTex, vec3(vWorld.xy * 0.25, uRockLayer)).r * w.z;
            }
            vec3 col = vColor.rgb * mix(ground, rock, vColor.a) * 1.25;
            FragColor = vec4(ApplyFog(ApplyLight(col, n, 0.0), vWorld), 1.0);
        }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector3 Pos;
        public Vector3 Normal;
        public uint Color; // RGBA8: surface colour + rockiness
    }

    private readonly record struct Chunk(Vector3 Min, Vector3 Max, int FirstIndex, int IndexCount);

    private bool _ready, _failed;
    private GlProgram _program;
    private int _locViewProj, _locTex, _locGround, _locRock, _locCam, _locFog, _locFogRange;
    private GlVertexArray _vao;
    private GlBuffer _vbo, _ibo;
    private bool _hasBuffers;
    private Chunk[] _chunks = [];
    private Map? _builtFor;
    private TerrainData? _builtData;

    // Stats (logged every few seconds) and a GPU timer.
    private int _drawCalls, _visibleChunks, _visibleTris;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _nextLog = 5;
    private GlQuery _query;
    private bool _queryActive;
    private double _gpuMs = -1;
    private int _frame;

    /// <summary>Draw calls, visible chunks and triangles drawn last frame (for the lead's performance checks).</summary>
    public (int DrawCalls, int Chunks, int Triangles) LastFrame => (_drawCalls, _visibleChunks, _visibleTris);

    /// <summary>Draws the map's terrain (does nothing for flat maps). Call with the box renderer's camera and fog set.</summary>
    public void Draw(Renderer gfx, Map map)
    {
        _drawCalls = _visibleChunks = _visibleTris = 0;
        if (_failed)
            return;
        if (map.Terrain is not { } data)
        {
            if (_hasBuffers)
                FreeMesh(); // flat map now: give the memory back
            _builtFor = null;
            return;
        }
        try
        {
            if (!_ready)
                Init();
            if (_builtFor != map || _builtData != data)
                Build(map, data);
            Render(gfx, map);
        }
        catch (Exception ex)
        {
            // Never take the game down over terrain drawing: log once and carry on without it.
            _failed = true;
            Console.WriteLine($"[terrain] rendering disabled: {ex.Message}");
        }
        finally
        {
            gfx.Rebind();
        }
    }

    private void Init()
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(VertexSrc).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(FragmentSrc).Compile();
        _program = CreateProgram();
        Native.glAttachShader(_program, vs);
        Native.glAttachShader(_program, fs);
        Native.glLinkProgram(_program);
        int linked;
        Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
        vs.Dispose();
        fs.Dispose();
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_program, 2048, &len, log);
            throw new InvalidOperationException("terrain shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        _locViewProj = Uniform("uViewProj\0"u8);
        _locTex = Uniform("uTex\0"u8);
        _locGround = Uniform("uGroundLayer\0"u8);
        _locRock = Uniform("uRockLayer\0"u8);
        _locCam = Uniform("uCam\0"u8);
        _locFog = Uniform("uFog\0"u8);
        _locFogRange = Uniform("uFogRange\0"u8);
        Lighting.Upload(_program); // sun and ambient (constants); camera and fog are set every frame
        GlQuery q = default;
        Native.glGenQueries(1, &q);
        _query = q;
        _ready = true;
    }

    private int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    /// <summary>Builds the vertex and index buffers for a map (once per map).</summary>
    private void Build(Map map, TerrainData t)
    {
        var sw = Stopwatch.StartNew();
        FreeMesh();
        int nx = t.CountX, nz = t.CountZ;
        var verts = new Vertex[nx * nz];
        for (var j = 0; j < nz; j++)
        for (var i = 0; i < nx; i++)
        {
            var (color, rock) = t.VertexColor(i, j);
            verts[j * nx + i] = new Vertex
            {
                Pos = new Vector3(t.OriginX + i * t.Cell, t.Height(i, j), t.OriginZ + j * t.Cell),
                Normal = t.VertexNormal(i, j),
                Color = Pack(color, rock),
            };
        }

        // Indices chunk by chunk, so a chunk is one contiguous range (and neighbours in a row merge into one draw).
        int cellsX = nx - 1, cellsZ = nz - 1;
        int chunksX = (cellsX + ChunkCells - 1) / ChunkCells, chunksZ = (cellsZ + ChunkCells - 1) / ChunkCells;
        var indices = new uint[cellsX * cellsZ * 6];
        var chunks = new Chunk[chunksX * chunksZ];
        var w = 0;
        for (var cz = 0; cz < chunksZ; cz++)
        for (var cx = 0; cx < chunksX; cx++)
        {
            var first = w;
            int i0 = cx * ChunkCells, i1 = Math.Min(i0 + ChunkCells, cellsX), j0 = cz * ChunkCells, j1 = Math.Min(j0 + ChunkCells, cellsZ);
            float lo = float.MaxValue, hi = float.MinValue;
            for (var j = j0; j < j1; j++)
            for (var i = i0; i < i1; i++)
            {
                uint a = (uint)(j * nx + i), b = a + 1, c = a + (uint)nx, d = c + 1;
                float ha = t.Heights[a], hb = t.Heights[b], hc = t.Heights[c], hd = t.Heights[d];
                lo = MathF.Min(lo, MathF.Min(MathF.Min(ha, hb), MathF.Min(hc, hd)));
                hi = MathF.Max(hi, MathF.Max(MathF.Max(ha, hb), MathF.Max(hc, hd)));
                // Split each cell along the diagonal whose corners are closer in height (closest to the bilinear surface).
                if (MathF.Abs(ha - hd) <= MathF.Abs(hb - hc))
                {
                    indices[w++] = a; indices[w++] = c; indices[w++] = d;
                    indices[w++] = a; indices[w++] = d; indices[w++] = b;
                }
                else
                {
                    indices[w++] = a; indices[w++] = c; indices[w++] = b;
                    indices[w++] = b; indices[w++] = c; indices[w++] = d;
                }
            }
            chunks[cz * chunksX + cx] = new Chunk(
                new Vector3(t.OriginX + i0 * t.Cell, lo, t.OriginZ + j0 * t.Cell),
                new Vector3(t.OriginX + i1 * t.Cell, hi, t.OriginZ + j1 * t.Cell), first, w - first);
        }

        _vao = GenVertexArray();
        _vao.Bind();
        _vbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (Vertex* p = verts)
            Native.glBufferData(GL_ARRAY_BUFFER, verts.Length * sizeof(Vertex), p, GL_STATIC_DRAW);
        _ibo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo); // recorded in the VAO
        fixed (uint* p = indices)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, indices.Length * sizeof(uint), p, GL_STATIC_DRAW);
        var stride = sizeof(Vertex);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
        EnableVertexAttribArray(0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)12);
        EnableVertexAttribArray(1);
        Native.glVertexAttribPointer(2, 4, GL_UNSIGNED_BYTE, true, stride, (void*)24);
        EnableVertexAttribArray(2);
        _hasBuffers = true;
        _chunks = chunks;
        _builtFor = map;
        _builtData = t;
        var err = Native.glGetError();
        Console.WriteLine($"[terrain] built '{map.Name}': {nx} x {nz} vertices, {indices.Length / 3} triangles in {chunks.Length} chunks, "
                          + $"{(verts.Length * stride + indices.Length * 4) / 1024} KB on the GPU, {sw.Elapsed.TotalMilliseconds:0.0} ms, GL error 0x{(uint)err:X}");
        _nextLog = _clock.Elapsed.TotalSeconds + 5;
    }

    private static uint Pack(Vector3 c, float a)
    {
        static uint B(float v) => (uint)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
        return B(c.X) | (B(c.Y) << 8) | (B(c.Z) << 16) | (B(a) << 24);
    }

    private void FreeMesh()
    {
        if (!_hasBuffers)
            return;
        var vao = _vao;
        Native.glDeleteVertexArrays(1, &vao);
        GlBuffer* bufs = stackalloc GlBuffer[2];
        bufs[0] = _vbo;
        bufs[1] = _ibo;
        Native.glDeleteBuffers(2, bufs);
        _hasBuffers = false;
        _chunks = [];
        _builtData = null;
    }

    private void Render(Renderer gfx, Map map)
    {
        var vp = gfx.ViewProj;
        Span<Vector4> planes = stackalloc Vector4[6];
        FrustumPlanes(vp, planes);

        _program.Use();
        _vao.Bind();
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform1i(_locTex, 0); // the box renderer's texture array is already bound to unit 0
        Native.glUniform1f(_locGround, (float)Material.Dirt);
        Native.glUniform1f(_locRock, (float)Material.Concrete);
        var cam = gfx.CameraPosition;
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        var fog = gfx.FogColor;
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, gfx.FogRange.X, gfx.FogRange.Y);

        // Time the terrain on the GPU now and then (results are read a few frames later, never stalling).
        _frame++;
        var timing = false;
        if (_queryActive)
        {
            int available;
            Native.glGetQueryObjectiv(_query.Id, GL_QUERY_RESULT_AVAILABLE, &available);
            if (available != 0)
            {
                ulong ns;
                Native.glGetQueryObjectui64v(_query.Id, GL_QUERY_RESULT, &ns);
                _gpuMs = ns / 1e6;
                _queryActive = false;
            }
        }
        else if (_frame % 120 == 0)
        {
            Native.glBeginQuery(GL_TIME_ELAPSED, _query);
            timing = true;
        }

        // Visible chunks, merged into runs of neighbouring index ranges.
        var runStart = -1;
        var runCount = 0;
        foreach (var c in _chunks)
        {
            if (!Visible(planes, c.Min, c.Max))
                continue;
            _visibleChunks++;
            _visibleTris += c.IndexCount / 3;
            if (runStart >= 0 && runStart + runCount == c.FirstIndex)
            {
                runCount += c.IndexCount;
                continue;
            }
            if (runStart >= 0)
                DrawRange(runStart, runCount);
            runStart = c.FirstIndex;
            runCount = c.IndexCount;
        }
        if (runStart >= 0)
            DrawRange(runStart, runCount);

        if (timing)
        {
            Native.glEndQuery(GL_TIME_ELAPSED);
            _queryActive = true;
        }

        var now = _clock.Elapsed.TotalSeconds;
        if (now >= _nextLog)
        {
            _nextLog = now + 10;
            Console.WriteLine($"[terrain] {map.Name}: {_visibleChunks}/{_chunks.Length} chunks visible, {_drawCalls} draw call(s), {_visibleTris} triangles, "
                              + (_gpuMs >= 0 ? $"GPU {_gpuMs:0.000} ms" : "GPU time n/a") + $", GL error 0x{(uint)Native.glGetError():X}");
        }
    }

    private void DrawRange(int first, int count)
    {
        Native.glDrawElements(GL_TRIANGLES, count, GL_UNSIGNED_INT, (void*)(first * sizeof(uint)));
        _drawCalls++;
    }

    /// <summary>The six clip planes of a row-vector view * projection matrix (inside: dot(plane, (p, 1)) >= 0).</summary>
    private static void FrustumPlanes(in Matrix4x4 m, Span<Vector4> planes)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        planes[0] = c4 + c1;
        planes[1] = c4 - c1;
        planes[2] = c4 + c2;
        planes[3] = c4 - c2;
        planes[4] = c4 + c3; // near (loose, works for both depth conventions)
        planes[5] = c4 - c3;
    }

    private static bool Visible(ReadOnlySpan<Vector4> planes, Vector3 min, Vector3 max)
    {
        foreach (var p in planes)
        {
            var x = p.X >= 0 ? max.X : min.X;
            var y = p.Y >= 0 ? max.Y : min.Y;
            var z = p.Z >= 0 ? max.Z : min.Z;
            if (p.X * x + p.Y * y + p.Z * z + p.W < 0)
                return false;
        }
        return true;
    }
}
