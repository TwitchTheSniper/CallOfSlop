using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a map's meshes and models (<see cref="Map.MeshPlacements"/>) with the same sun, ambient, fog and surface
/// textures as the boxes.
///
/// How it stays cheap:
/// <list type="bullet">
/// <item>All geometry sits in one <see cref="MeshPool"/> (one vertex and one index buffer, one vertex array), uploaded
/// when a map first shows a mesh. Nothing is re-uploaded per frame except the small instance list.</item>
/// <item>Instanced: one glDrawElementsInstanced per (mesh, LOD) that has anything visible, however many copies there
/// are. A model is one instance per part (node with a mesh), so 50 barrels are 3 draw calls.</item>
/// <item>Per frame each part is frustum-culled by its bounding sphere and gets a LOD from its size on screen; parts are
/// bucketed by (mesh, LOD) with a counting sort into preallocated arrays (no allocations, stable order) and streamed
/// in one buffer upload (orphaned, so no sync stall).</item>
/// <item>Back faces are culled (meshes are closed and wound counter-clockwise); double-sided glTF materials draw last
/// with culling off.</item>
/// </list>
/// GPU time comes from GL_TIME_ELAPSED queries read a few frames later (never stalling); a "[mesh]" stat line is
/// logged every few seconds.
/// </summary>
public sealed unsafe partial class MeshRenderer : IMeshDrawer
{
    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 3) in float aLayer;
        layout(location = 4) in vec4 aColor;
        layout(location = 5) in vec4 iRow0;   // per instance: the model matrix as three rows (column-vector form)
        layout(location = 6) in vec4 iRow1;
        layout(location = 7) in vec4 iRow2;
        layout(location = 8) in vec4 iColor;  // per instance: tint rgb, procedural layer for untextured vertices
        uniform mat4 uViewProj;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec3 vColor;
        out vec2 vUv;
        flat out float vLayer;

        void main()
        {
            vec4 p = vec4(aPos, 1.0);
            vWorld = vec3(dot(iRow0, p), dot(iRow1, p), dot(iRow2, p));
            // Normal matrix without an inverse: the cofactor matrix of the 3x3 part (any scale, no shear assumptions).
            vec3 a0 = vec3(iRow0.x, iRow1.x, iRow2.x), a1 = vec3(iRow0.y, iRow1.y, iRow2.y), a2 = vec3(iRow0.z, iRow1.z, iRow2.z);
            vec3 c0 = cross(a1, a2);
            vNormal = (c0 * aNormal.x + cross(a2, a0) * aNormal.y + cross(a0, a1) * aNormal.z) * (dot(a0, c0) < 0.0 ? -1.0 : 1.0);
            vColor = aColor.rgb * iColor.rgb;
            vUv = aUv;
            vLayer = aLayer >= 0.0 ? aLayer : iColor.w;
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 330 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec3 vColor;
        in vec2 vUv;
        flat in float vLayer;
        uniform sampler2DArray uTex;       // the procedural surface textures (unit 0, shared with the boxes)
        uniform sampler2DArray uModelTex;  // textures imported with models (unit 1)
        out vec4 FragColor;

        void main()
        {
            vec3 n = normalize(gl_FrontFacing ? vNormal : -vNormal);
            vec3 col = vColor;
            if (vLayer >= 99.5)
                col *= texture(uModelTex, vec3(vUv, vLayer - 100.0)).rgb;
            else if (vLayer >= 0.0)
            {
                // The boxes' world-space planar mapping (2 m per repeat), blended across the three planes by the normal
                // so round shapes don't show seams; an axis-aligned face gets exactly the box look.
                vec3 w = n * n;
                w *= w;
                w /= (w.x + w.y + w.z);
                float t = w.x * texture(uTex, vec3(vWorld.zy * 0.5, vLayer)).r
                        + w.y * texture(uTex, vec3(vWorld.xz * 0.5, vLayer)).r
                        + w.z * texture(uTex, vec3(vWorld.xy * 0.5, vLayer)).r;
                col *= t * 1.25;
            }
            float rim = 0.12 * (1.0 - abs(n.y));
            col = ApplyLightAt(col, n, 0.0, vWorld) + col * rim * gLitAmb;
            FragColor = vec4(ApplyFog(col, vWorld), 1.0);
        }
        """;

    /// <summary>Floats per instance: three matrix rows and the colour/layer vector.</summary>
    public const int FloatsPerInstance = 16;

    /// <summary>A part switches to LOD 1 when its bounding radius / distance falls under this, LOD 2 under <see cref="Lod2Below"/>.</summary>
    public static float Lod1Below = 0.05f, Lod2Below = 0.02f;

    private bool _ready, _broken, _timers;
    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _vbo, _ibo, _instVbo;
    private uint _modelTex;
    private int _modelTexLayers = -1, _modelTexGeneration = -1;
    private int _locViewProj, _locCam, _locFog, _locFogRange;
    private readonly MeshPool _pool = new();

    // The current map's parts (one per primitive placement, one per model node with a mesh).
    private Map? _map;
    private int _parts;
    private int[] _partSlot = [];
    private float[] _partData = [];     // FloatsPerInstance per part, ready to copy
    private Vector4[] _partSphere = []; // world centre, radius
    private byte[] _partLod = [];
    private MeshAsset[] _slotAsset = [];
    private MeshPool.Range[][] _slotRanges = [];
    private int[] _bucketCount = [], _bucketStart = [], _bucketFill = [];
    private float[] _instances = [];
    private int _instanceCapacity;
    private readonly Vector4[] _planes = new Vector4[6];

    private const int QueryRing = 4;
    private readonly GlQuery[] _queries = new GlQuery[QueryRing];
    private readonly bool[] _queryPending = new bool[QueryRing];
    private int _queryFrame;

    /// <summary>Averages over a window of frames (the periodic log line, and one the smoke test resets and reads).</summary>
    public struct Stats
    {
        public double Start;
        public int Frames, GpuSamples;
        public long Instances, Draws, Culled, Lod0, Lod1, Lod2, Triangles;
        public double CpuMs, GpuMs;

        public readonly double Avg(long v) => Frames > 0 ? (double)v / Frames : 0;
        public readonly double AvgCpuMs => Frames > 0 ? CpuMs / Frames : 0;
        public readonly double AvgGpuMs => GpuSamples > 0 ? GpuMs / GpuSamples : -1;
    }

    private Stats _log;

    /// <summary>Stats since the last <see cref="ResetWindow"/> (for the smoke test).</summary>
    public Stats Window;

    public const double LogInterval = 5;

    /// <summary>Parts of the current map (instances before culling).</summary>
    public int Parts => _parts;

    /// <summary>The shared geometry buffers (for stats).</summary>
    public MeshPool Pool => _pool;

    public void ResetWindow(double now) => Window = new Stats { Start = now };

    /// <summary>One line of totals for logs: pool size and the current window's averages.</summary>
    public string Describe(in Stats s) =>
        $"{_parts} parts of {_slotAsset.Length} meshes, pool {_pool.MeshCount} meshes {_pool.VertexCount} vertices {_pool.IndexCount / 3} triangles "
        + $"({_pool.Bytes / 1024} KB), {s.Avg(s.Instances):0} drawn/frame ({s.Avg(s.Triangles) / 1000:0.0}k triangles) in {s.Avg(s.Draws):0.0} draw calls, "
        + $"LOD0/1/2 {s.Avg(s.Lod0):0}/{s.Avg(s.Lod1):0}/{s.Avg(s.Lod2):0}, culled {s.Avg(s.Culled):0}, CPU {s.AvgCpuMs:0.000} ms, "
        + $"GPU {(s.AvgGpuMs >= 0 ? $"{s.AvgGpuMs:0.000} ms" : "n/a")}";

    /// <summary>Draws the meshes of <paramref name="map"/> with the camera, fog and depth buffer <paramref name="gfx"/> has set up.</summary>
    public void Draw(Map map, Renderer gfx, double now)
    {
        if (_broken)
            return;
        if (map.MeshPlacements.Count == 0)
        {
            if (_parts > 0)
                Free();
            return;
        }
        var t0 = Stopwatch.GetTimestamp();
        if (!_ready && !Init())
            return;
        if (map != _map)
            Rebuild(map);
        if (_modelTexGeneration != MeshTextures.Generation)
            UploadModelTextures();

        ExtractPlanes(gfx.ViewProj);
        var cam = gfx.CameraPosition;

        // 1. Cull and pick LODs, counting per (slot, LOD) bucket.
        Array.Clear(_bucketCount);
        long culled = 0, lod0 = 0, lod1 = 0, lod2 = 0;
        for (var p = 0; p < _parts; p++)
        {
            var s = _partSphere[p];
            var c = new Vector3(s.X, s.Y, s.Z);
            if (!SphereVisible(c, s.W))
            {
                _partLod[p] = 255;
                culled++;
                continue;
            }
            var size = s.W / MathF.Max(Vector3.Distance(cam, c), 0.001f);
            var slot = _partSlot[p];
            var lod = Math.Min(size < Lod2Below ? 2 : size < Lod1Below ? 1 : 0, _slotRanges[slot].Length - 1);
            _partLod[p] = (byte)lod;
            _bucketCount[slot * MeshLibrary.MaxLods + lod]++;
            if (lod == 0)
                lod0++;
            else if (lod == 1)
                lod1++;
            else
                lod2++;
        }
        // 2. Bucket starts, then copy each visible part's instance data into its bucket.
        var visible = 0;
        for (var b = 0; b < _bucketCount.Length; b++)
        {
            _bucketStart[b] = visible;
            _bucketFill[b] = visible;
            visible += _bucketCount[b];
        }
        for (var p = 0; p < _parts; p++)
        {
            var lod = _partLod[p];
            if (lod == 255)
                continue;
            var at = _bucketFill[_partSlot[p] * MeshLibrary.MaxLods + lod]++;
            Array.Copy(_partData, p * FloatsPerInstance, _instances, at * FloatsPerInstance, FloatsPerInstance);
        }

        var slotQuery = _queryFrame % QueryRing;
        var timing = BeginTimer(slotQuery);
        long draws = 0, tris = 0;
        if (visible > 0)
        {
            var vp = gfx.ViewProj;
            var fog = gfx.FogColor;
            var fogRange = gfx.FogRange;
            _program.Use();
            Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
            Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
            Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
            Native.glUniform2f(_locFogRange, fogRange.X, fogRange.Y);
            Native.glActiveTexture(GL_TEXTURE1);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _modelTex);
            Native.glActiveTexture(GL_TEXTURE0);
            _vao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, (nint)_instanceCapacity * FloatsPerInstance * sizeof(float), null, GL_STREAM_DRAW); // orphan
            fixed (float* src = _instances)
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, (nint)visible * FloatsPerInstance * sizeof(float), src);
            Enable(GlCapability.CullFace);
            Native.glCullFace(GlFace.Back);
            for (var pass = 0; pass < 2; pass++) // single-sided first, then double-sided with culling off
            {
                if (pass == 1)
                    Native.glDisable(GlCapability.CullFace);
                for (var b = 0; b < _bucketCount.Length; b++)
                {
                    var count = _bucketCount[b];
                    if (count == 0)
                        continue;
                    var slot = b / MeshLibrary.MaxLods;
                    if (_slotAsset[slot].DoubleSided != (pass == 1))
                        continue;
                    var range = _slotRanges[slot][b % MeshLibrary.MaxLods];
                    var offset = (nint)_bucketStart[b] * FloatsPerInstance * sizeof(float);
                    const int stride = FloatsPerInstance * sizeof(float);
                    for (uint a = 0; a < 4; a++)
                        Native.glVertexAttribPointer(5 + a, 4, GL_FLOAT, false, stride, (void*)(offset + a * 4 * sizeof(float)));
                    Native.glDrawElementsInstanced(GL_TRIANGLES, range.IndexCount, GL_UNSIGNED_INT, (void*)((nint)range.FirstIndex * sizeof(uint)), count);
                    draws++;
                    tris += (long)count * range.IndexCount / 3;
                }
            }
            Native.glDisable(GlCapability.CullFace);
        }
        if (timing)
        {
            Native.glEndQuery(GL_TIME_ELAPSED);
            _queryPending[slotQuery] = true;
        }
        _queryFrame++;
        gfx.Rebind();

        var cpuMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Accumulate(ref _log, now, visible, draws, culled, lod0, lod1, lod2, tris, cpuMs);
        Accumulate(ref Window, now, visible, draws, culled, lod0, lod1, lod2, tris, cpuMs);
        if (now - _log.Start >= LogInterval)
        {
            var fps = _log.Frames / Math.Max(now - _log.Start, 1e-6);
            Console.WriteLine($"[mesh] {map.Name}: {map.MeshPlacements.Count} placements, {Describe(_log)}, {fps:0} fps");
            _log = new Stats { Start = now };
        }
    }

    private static void Accumulate(ref Stats s, double now, long instances, long draws, long culled, long l0, long l1, long l2, long tris, double cpuMs)
    {
        if (s.Start <= 0)
            s.Start = now;
        s.Frames++;
        s.Instances += instances;
        s.Draws += draws;
        s.Culled += culled;
        s.Lod0 += l0;
        s.Lod1 += l1;
        s.Lod2 += l2;
        s.Triangles += tris;
        s.CpuMs += cpuMs;
    }

    private bool BeginTimer(int slot)
    {
        if (!_timers)
            return false;
        var q = _queries[slot];
        if (_queryPending[slot])
        {
            int available;
            Native.glGetQueryObjectiv(q.Id, GL_QUERY_RESULT_AVAILABLE, &available);
            if (available == 0)
                return false;
            ulong ns;
            Native.glGetQueryObjectui64v(q.Id, GL_QUERY_RESULT, &ns);
            _log.GpuMs += ns / 1e6;
            _log.GpuSamples++;
            Window.GpuMs += ns / 1e6;
            Window.GpuSamples++;
            _queryPending[slot] = false;
        }
        Native.glBeginQuery(GL_TIME_ELAPSED, q);
        return true;
    }

    // ---- Culling ----------------------------------------------------------------------------------------------------

    private void ExtractPlanes(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _planes[0] = c4 + c1;
        _planes[1] = c4 - c1;
        _planes[2] = c4 + c2;
        _planes[3] = c4 - c2;
        _planes[4] = c4 + c3;
        _planes[5] = c4 - c3;
        for (var i = 0; i < 6; i++) // normalised, so a sphere test can use the radius in metres
        {
            var len = new Vector3(_planes[i].X, _planes[i].Y, _planes[i].Z).Length();
            if (len > 1e-12f)
                _planes[i] /= len;
        }
    }

    private bool SphereVisible(Vector3 c, float r)
    {
        foreach (var p in _planes)
            if (p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W < -r)
                return false;
        return true;
    }

    // ---- Per-map data -----------------------------------------------------------------------------------------------

    private void Rebuild(Map map)
    {
        var sw = Stopwatch.StartNew();
        var slotOf = new Dictionary<int, int>();
        var assets = new List<MeshAsset>();
        var parts = new List<(int Slot, Matrix4x4 M, Vector4 Color, float Layer)>();
        int Slot(MeshAsset a)
        {
            if (!slotOf.TryGetValue(a.Id, out var s))
            {
                s = assets.Count;
                slotOf[a.Id] = s;
                assets.Add(a);
            }
            return s;
        }
        foreach (var pl in map.MeshPlacements)
        {
            if (pl.Mesh != null)
                parts.Add((Slot(pl.Mesh), pl.Transform, pl.Color, pl.Layer));
            else if (pl.Model != null)
                foreach (var part in pl.Model.Parts)
                    parts.Add((Slot(pl.Model.Meshes[part.Mesh]), part.Transform * pl.Transform, pl.Color, pl.Layer));
        }
        _slotAsset = assets.ToArray();
        _slotRanges = assets.Select(_pool.Add).ToArray();
        _vao.Bind();
        _pool.Sync(_vbo, _ibo);

        _parts = parts.Count;
        _partSlot = new int[_parts];
        _partData = new float[_parts * FloatsPerInstance];
        _partSphere = new Vector4[_parts];
        _partLod = new byte[_parts];
        for (var i = 0; i < _parts; i++)
        {
            var (slot, m, color, layer) = parts[i];
            _partSlot[i] = slot;
            var o = i * FloatsPerInstance;
            float[] row = [m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43, color.X, color.Y, color.Z, layer];
            row.CopyTo(_partData, o);
            var b = _slotAsset[slot].Bounds;
            var center = Vector3.Transform(b.Center, m);
            var scale = MathF.Sqrt(MathF.Max(MathF.Max(new Vector3(m.M11, m.M12, m.M13).LengthSquared(), new Vector3(m.M21, m.M22, m.M23).LengthSquared()),
                new Vector3(m.M31, m.M32, m.M33).LengthSquared()));
            _partSphere[i] = new Vector4(center, b.Radius * scale);
        }
        var buckets = _slotAsset.Length * MeshLibrary.MaxLods;
        _bucketCount = new int[buckets];
        _bucketStart = new int[buckets];
        _bucketFill = new int[buckets];
        _instanceCapacity = Math.Max(_parts, 1);
        _instances = new float[_instanceCapacity * FloatsPerInstance];
        _map = map;
        _log = new Stats();
        var err = (uint)Native.glGetError();
        Console.WriteLine($"[mesh] built {map.Name}: {map.MeshPlacements.Count} placements -> {_parts} parts of {_slotAsset.Length} meshes "
            + $"(LODs {string.Join(",", _slotAsset.Select(a => a.Lods.Length))}), pool {_pool.MeshCount} meshes {_pool.VertexCount} vertices "
            + $"{_pool.IndexCount / 3} triangles {_pool.Bytes / 1024} KB ({_pool.Reallocations} buffer growths), {sw.Elapsed.TotalMilliseconds:0.0} ms"
            + (err != 0 ? $", GL error 0x{err:X}" : ""));
    }

    /// <summary>Drops the per-map part data (a map without meshes). The pool keeps its meshes for later maps.</summary>
    public void Free()
    {
        _parts = 0;
        _partSlot = [];
        _partData = [];
        _partSphere = [];
        _partLod = [];
        _instances = [];
        _map = null;
    }

    // ---- GPU resources ----------------------------------------------------------------------------------------------

    private static int Uniform(GlProgram program, ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(program, p);
    }

    private bool Init()
    {
        try
        {
            var vs = CreateShader(GlShaderType.Vertex).SetSource(VertexSrc).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(FragmentSrc).Compile();
            _program = CreateProgram();
            Native.glAttachShader(_program, vs);
            Native.glAttachShader(_program, fs);
            Native.glLinkProgram(_program);
            int linked;
            Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
            if (linked == 0)
            {
                var log = stackalloc byte[2048];
                int len;
                Native.glGetProgramInfoLog(_program, 2048, &len, log);
                throw new InvalidOperationException("shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
            }
            vs.Dispose();
            fs.Dispose();
            Lighting.Upload(_program);
            _program.Use();
            Native.glUniform1i(Uniform(_program, "uTex\0"u8), 0);
            Native.glUniform1i(Uniform(_program, "uModelTex\0"u8), 1);
            _locViewProj = Uniform(_program, "uViewProj\0"u8);
            _locCam = Uniform(_program, "uCam\0"u8);
            _locFog = Uniform(_program, "uFog\0"u8);
            _locFogRange = Uniform(_program, "uFogRange\0"u8);

            _vao = GenVertexArray();
            _vao.Bind();
            _vbo = GenBuffer();
            _ibo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
            const int stride = MeshVertex.SizeInBytes;
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)12);
            Native.glVertexAttribPointer(2, 2, GL_FLOAT, false, stride, (void*)24);
            Native.glVertexAttribPointer(3, 1, GL_FLOAT, false, stride, (void*)32);
            Native.glVertexAttribPointer(4, 4, GL_UNSIGNED_BYTE, true, stride, (void*)36);
            for (uint a = 0; a < 5; a++)
                EnableVertexAttribArray(a);
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _ibo); // remembered by the vertex array
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            for (uint a = 0; a < 4; a++)
            {
                Native.glVertexAttribPointer(5 + a, 4, GL_FLOAT, false, FloatsPerInstance * sizeof(float), (void*)(a * 4 * sizeof(float)));
                EnableVertexAttribArray(5 + a);
                Native.glVertexAttribDivisor(5 + a, 1);
            }

            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _modelTex = tex.Id;

            if (Native.glGenQueries != null && Native.glBeginQuery != null && Native.glGetQueryObjectui64v != null && Native.glGetQueryObjectiv != null)
            {
                fixed (GlQuery* q = _queries)
                    Native.glGenQueries(QueryRing, q);
                _timers = true;
            }
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[mesh] GL error 0x{err:X} while setting up (continuing)");
            Console.WriteLine($"[mesh] renderer ready, GPU timer {(_timers ? "on" : "unavailable")}; meshoptimizer: {MeshOpt.CanaryReport}; Brutal.Texture: {MeshTextures.CanaryReport}");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[mesh] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
    }

    /// <summary>(Re)uploads the imported model textures as one array (a white layer when there are none, so the sampler is always valid).</summary>
    private void UploadModelTextures()
    {
        var layers = MeshTextures.Snapshot();
        _modelTexGeneration = MeshTextures.Generation;
        if (layers.Length == _modelTexLayers)
            return;
        var count = Math.Max(layers.Length, 1);
        const int size = MeshTextures.Size;
        var pixels = new byte[size * size * 4 * count];
        if (layers.Length == 0)
            Array.Fill(pixels, (byte)255);
        for (var i = 0; i < layers.Length; i++)
            layers[i].CopyTo(pixels, i * size * size * 4);
        Native.glActiveTexture(GL_TEXTURE1);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _modelTex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, size, size, count, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAX_ANISOTROPY, 8);
        Native.glActiveTexture(GL_TEXTURE0);
        _modelTexLayers = layers.Length;
        Console.WriteLine($"[mesh] model textures: {layers.Length} layer(s) of {size}x{size}, {pixels.Length / 1024} KB");
    }
}
