using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a map's grass, bushes, shrubs and tree canopies (tree trunks are solid boxes, drawn with the other boxes).
///
/// How it stays cheap:
/// <list type="bullet">
/// <item>One shader, one vertex array, one static mesh buffer for every plant mesh (<see cref="FoliageMeshes"/>) and one
/// static per-instance buffer per map (<see cref="FoliageBatches"/>), uploaded once when the map or quality changes.
/// Nothing is uploaded per frame except a handful of uniforms, and nothing is allocated.</item>
/// <item>Instanced draws: plants are sorted by type and 16 m chunk; each frame the chunks outside the view frustum or
/// beyond the type's fade distance are skipped, and the visible neighbours merge into one glDrawElementsInstanced call.</item>
/// <item>Distance thinning and LOD in the vertex shader: each plant has its own cutoff between the fade start and end,
/// and fades out with a screen-space dither (no popping, no blending or sorting). Near and far meshes cross-fade with
/// complementary dithers. Fully faded plants are collapsed in the vertex shader, so they cost no pixels.</item>
/// <item>Wind sway is vertex-shader maths. Lighting and fog come from <see cref="Lighting"/>, like the boxes.</item>
/// </list>
/// GPU time is measured with GL_TIME_ELAPSED queries (read a few frames later, never stalling), and a one-line stat
/// is logged every few seconds while a foliage map is drawn.
/// </summary>
public sealed unsafe class FoliageRenderer
{
    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aSwayShade;  // x: sway weight (0 root .. 1 tip), y: baked shade
        layout(location = 3) in vec4 iPosScale;   // per instance: ground position, scale (trees: height)
        layout(location = 4) in vec4 iColorYaw;   // per instance: colour, yaw (fraction of a turn)
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        uniform float uTime;
        uniform vec4 uFade;   // fade start, fade end, LOD start, LOD end (metres)
        uniform vec4 uMat;    // x: 0 = near mesh, 1 = far mesh; y: sway amount; z: light wrap
        out vec3 vWorld;
        out vec3 vNormal;
        out vec3 vColor;
        flat out float vFade;
        flat out float vLod;
        flat out float vHash;

        float Hash(vec2 p)
        {
            uvec2 q = floatBitsToUint(p);
            uint h = q.x * 0x8da6b343u ^ q.y * 0xd8163841u;
            h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12; h *= 0x297a2d39u; h ^= h >> 15;
            return float(h & 0xffffffu) / 16777216.0;
        }

        void main()
        {
            vec3 ip = iPosScale.xyz;
            float s = iPosScale.w;
            float h = Hash(ip.xz);
            float d = distance(uCam.xz, ip.xz);

            // Density falloff: every plant has its own cutoff between fade start and end, then dithers out over a band.
            float cutoff = mix(uFade.x, uFade.y, h);
            float band = max((uFade.y - uFade.x) * 0.15, 1.0);
            float fade = clamp((cutoff - d) / band, 0.0, 1.0);
            float lod = clamp((d - uFade.z) / max(uFade.w - uFade.z, 0.01), 0.0, 1.0);
            vFade = fade;
            vLod = lod;
            vHash = h;
            if (fade <= 0.0 || (uMat.x < 0.5 ? lod >= 1.0 : lod <= 0.0))
            {
                // Not drawn at this distance with this mesh: collapse it outside the clip volume.
                vWorld = ip; vNormal = vec3(0.0, 1.0, 0.0); vColor = vec3(0.0);
                gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
                return;
            }

            float a = iColorYaw.w * 6.2831853;
            float c = cos(a), sn = sin(a);
            vec3 p = aPos * s;
            p = vec3(p.x * c - p.z * sn, p.y, p.x * sn + p.z * c);
            vec3 n = vec3(aNormal.x * c - aNormal.z * sn, aNormal.y, aNormal.x * sn + aNormal.z * c);

            // Wind: a steady lean plus two gust waves travelling across the map, strongest at the tips.
            float phase = dot(ip.xz, vec2(0.11, 0.07)) + h * 2.0;
            float gust = sin(uTime * 1.3 - phase) * 0.6 + sin(uTime * 2.7 - phase * 1.7) * 0.3;
            float bend = aSwayShade.x * aSwayShade.x * uMat.y * s * (0.4 + gust);
            p.xz += normalize(vec2(1.0, 0.45)) * bend;

            vWorld = ip + p;
            vNormal = n;
            vColor = iColorYaw.rgb * aSwayShade.y;
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 330 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec3 vColor;
        flat in float vFade;
        flat in float vLod;
        flat in float vHash;
        uniform vec4 uMat;
        out vec4 FragColor;

        // 4x4 ordered dither threshold in (0, 1).
        float Bayer(ivec2 p)
        {
            const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
            return (m[(p.x & 3) + (p.y & 3) * 4] + 0.5) / 16.0;
        }

        void main()
        {
            ivec2 px = ivec2(gl_FragCoord.xy);
            // Distance fade: the pattern is shifted per plant, so neighbours don't fade in lockstep.
            if (Bayer(px + ivec2(int(vHash * 7.0), int(vHash * 53.0))) >= vFade)
                discard;
            // LOD cross-fade: near and far meshes use the same screen-locked pattern, so exactly one of them covers each pixel.
            float t = Bayer(px);
            if (uMat.x < 0.5 ? t < vLod : t >= vLod)
                discard;
            vec3 col = ApplyLight(vColor, normalize(vNormal), uMat.z);
            FragColor = vec4(ApplyFog(col, vWorld), 1.0);
        }
        """;

    private bool _ready, _broken;
    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _meshVbo, _meshIbo, _instVbo;
    private int _locViewProj, _locCam, _locFog, _locFogRange, _locTime, _locFade, _locMat;

    private Map? _map;
    private FoliageQuality _quality;
    private uint _builtHash;
    private FoliageBatches _batches = new();
    private readonly Vector4[] _planes = new Vector4[6];

    // GPU timing: a small ring of GL_TIME_ELAPSED queries, read back a few frames later so it never stalls.
    private const int QueryRing = 4;
    private readonly GlQuery[] _queries = new GlQuery[QueryRing];
    private readonly bool[] _queryPending = new bool[QueryRing];
    private int _queryFrame;
    private bool _timers;

    /// <summary>Averages over a window of frames (the periodic log line, and one the smoke test can reset and read).</summary>
    public struct Stats
    {
        public double Start;
        public int Frames, GpuSamples;
        public long Instances, Draws;
        public double CpuMs, GpuMs;

        public readonly double AvgInstances => Frames > 0 ? (double)Instances / Frames : 0;
        public readonly double AvgDraws => Frames > 0 ? (double)Draws / Frames : 0;
        public readonly double AvgCpuMs => Frames > 0 ? CpuMs / Frames : 0;

        /// <summary>Average GPU time per frame, or -1 when timer queries aren't available.</summary>
        public readonly double AvgGpuMs => GpuSamples > 0 ? GpuMs / GpuSamples : -1;
    }

    private Stats _log;

    /// <summary>Stats since the last <see cref="ResetWindow"/> (for the smoke test).</summary>
    public Stats Window;

    /// <summary>How often the stat line is written to the log, in seconds.</summary>
    public const double LogInterval = 5;

    /// <summary>Plants in the current GPU buffer (after the quality filter).</summary>
    public int Uploaded => _batches.Count;

    public void ResetWindow(double now) => Window = new Stats { Start = now };

    /// <summary>
    /// Draws the foliage of <paramref name="map"/> with the camera, fog and depth buffer <paramref name="gfx"/> has
    /// set up. Call after the opaque boxes. Builds (or frees) the GPU data when the map or quality changes; with
    /// quality Off or a map without foliage it does nothing at all.
    /// </summary>
    public void Draw(Map map, Renderer gfx, FoliageQuality quality, double now)
    {
        if (quality == FoliageQuality.Off || map.FoliageInstances.Length == 0 || _broken)
        {
            if (_batches.Count > 0)
                Free();
            return;
        }
        var t0 = Stopwatch.GetTimestamp();
        if (!_ready && !Init())
            return;
        if (map != _map || quality != _quality)
            Rebuild(map, quality);
        if (_batches.Count == 0)
            return;

        ExtractPlanes(gfx.ViewProj);
        var cam = gfx.CameraPosition;
        _program.Use();
        var vp = gfx.ViewProj;
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        var fog = gfx.FogColor;
        var fogRange = gfx.FogRange;
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, fogRange.X, fogRange.Y);
        Native.glUniform1f(_locTime, (float)(now % 3600.0));
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);

        var slot = _queryFrame % QueryRing;
        var timing = false;
        if (_timers)
        {
            var q = _queries[slot];
            if (_queryPending[slot])
            {
                int available;
                Native.glGetQueryObjectiv(q.Id, GL_QUERY_RESULT_AVAILABLE, &available);
                if (available != 0)
                {
                    ulong ns;
                    Native.glGetQueryObjectui64v(q.Id, GL_QUERY_RESULT, &ns);
                    _log.GpuMs += ns / 1e6;
                    _log.GpuSamples++;
                    Window.GpuMs += ns / 1e6;
                    Window.GpuSamples++;
                    _queryPending[slot] = false;
                }
            }
            if (!_queryPending[slot])
            {
                Native.glBeginQuery(GL_TIME_ELAPSED, q);
                timing = true;
            }
        }

        long draws = 0, instances = 0;
        for (var t = 0; t < FoliageMeshes.TypeCount; t++)
        {
            var count = _batches.TypeChunkCount[t];
            if (count == 0)
                continue;
            var type = (FoliageType)t;
            var fadeEnd = Foliage.FadeDistance(type, quality);
            var fadeStart = fadeEnd * Foliage.FadeStartFraction;
            var lodStart = Foliage.LodDistance(type, quality);
            var lodEnd = lodStart * 1.3f;
            Native.glUniform4f(_locFade, fadeStart, fadeEnd, lodStart, lodEnd);
            var (sway, wrap) = Material(type);
            for (var lod = 0; lod < 2; lod++)
            {
                Native.glUniform4f(_locMat, lod, sway, wrap, 0f);
                var mesh = FoliageMeshes.Mesh(type, lod);
                int runStart = -1, runCount = 0;
                var first = _batches.TypeChunkStart[t];
                for (var i = first; i < first + count; i++)
                {
                    ref readonly var c = ref _batches.Chunks[i];
                    var visible = false;
                    var near = NearDistanceXZ(cam, c.Min, c.Max);
                    if (near < fadeEnd && (lod == 1 ? FarDistanceXZ(cam, c.Min, c.Max) > lodStart : near < lodEnd))
                        visible = InFrustum(c.Min, c.Max);
                    if (visible && runStart >= 0 && runStart + runCount == c.Start)
                    {
                        runCount += c.Count; // the next chunk in the buffer: grow the run
                        continue;
                    }
                    if (runStart >= 0)
                    {
                        DrawRun(mesh, runStart, runCount);
                        draws++;
                        instances += runCount;
                        runStart = -1;
                    }
                    if (visible)
                    {
                        runStart = c.Start;
                        runCount = c.Count;
                    }
                }
                if (runStart >= 0)
                {
                    DrawRun(mesh, runStart, runCount);
                    draws++;
                    instances += runCount;
                }
            }
        }

        if (timing)
        {
            Native.glEndQuery(GL_TIME_ELAPSED);
            _queryPending[slot] = true;
        }
        _queryFrame++;
        gfx.Rebind();

        var cpuMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Accumulate(ref _log, now, draws, instances, cpuMs);
        Accumulate(ref Window, now, draws, instances, cpuMs);
        if (now - _log.Start >= LogInterval)
        {
            var fps = _log.Frames / Math.Max(now - _log.Start, 1e-6);
            Console.WriteLine($"[foliage] {map.Name}, {quality}: {_batches.Count} plants uploaded, {_log.AvgInstances:0} drawn/frame in {_log.AvgDraws:0.0} draw calls, "
                + $"CPU {_log.AvgCpuMs:0.000} ms, GPU {(_log.AvgGpuMs >= 0 ? $"{_log.AvgGpuMs:0.000} ms" : "n/a")}, {fps:0} fps");
            _log = new Stats { Start = now };
        }
    }

    private static void Accumulate(ref Stats s, double now, long draws, long instances, double cpuMs)
    {
        if (s.Start <= 0)
            s.Start = now;
        s.Frames++;
        s.Draws += draws;
        s.Instances += instances;
        s.CpuMs += cpuMs;
    }

    /// <summary>Per type: how far the wind bends it (times its scale) and how much light wraps round it.</summary>
    private static (float Sway, float Wrap) Material(FoliageType t) => t switch
    {
        FoliageType.Grass => (0.2f, 0.6f),
        FoliageType.Shrub => (0.12f, 0.5f),
        FoliageType.Bush => (0.035f, 0.35f),
        FoliageType.Tree => (0.012f, 0.3f),
        _ => (0.008f, 0.25f),
    };

    private void DrawRun(FoliageMeshes.Range mesh, int start, int count)
    {
        var offset = (nint)start * FoliageBatches.FloatsPerInstance * sizeof(float);
        const int stride = FoliageBatches.FloatsPerInstance * sizeof(float);
        Native.glVertexAttribPointer(3, 4, GL_FLOAT, false, stride, (void*)offset);
        Native.glVertexAttribPointer(4, 4, GL_FLOAT, false, stride, (void*)(offset + 4 * sizeof(float)));
        Native.glDrawElementsInstanced(GL_TRIANGLES, mesh.IndexCount, GL_UNSIGNED_SHORT, (void*)((nint)mesh.FirstIndex * sizeof(ushort)), count);
    }

    // ---- Culling ------------------------------------------------------------------------------

    private static float NearDistanceXZ(Vector3 cam, Vector3 min, Vector3 max)
    {
        var dx = MathF.Max(MathF.Max(min.X - cam.X, 0f), cam.X - max.X);
        var dz = MathF.Max(MathF.Max(min.Z - cam.Z, 0f), cam.Z - max.Z);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    private static float FarDistanceXZ(Vector3 cam, Vector3 min, Vector3 max)
    {
        var dx = MathF.Max(MathF.Abs(min.X - cam.X), MathF.Abs(max.X - cam.X));
        var dz = MathF.Max(MathF.Abs(min.Z - cam.Z), MathF.Abs(max.Z - cam.Z));
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The six frustum planes of a row-vector view * projection matrix (clip = v * M), as (normal, d).</summary>
    private void ExtractPlanes(in Matrix4x4 m)
    {
        var c1 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        var c2 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        var c3 = new Vector4(m.M13, m.M23, m.M33, m.M43);
        var c4 = new Vector4(m.M14, m.M24, m.M34, m.M44);
        _planes[0] = c4 + c1; // left
        _planes[1] = c4 - c1; // right
        _planes[2] = c4 + c2; // bottom
        _planes[3] = c4 - c2; // top
        _planes[4] = c4 + c3; // near (the looser GL-style plane; it never culls anything visible)
        _planes[5] = c4 - c3; // far
    }

    private bool InFrustum(Vector3 min, Vector3 max)
    {
        foreach (var p in _planes)
        {
            // The box corner furthest along the plane normal; if even that is behind the plane, the box is outside.
            var x = p.X >= 0 ? max.X : min.X;
            var y = p.Y >= 0 ? max.Y : min.Y;
            var z = p.Z >= 0 ? max.Z : min.Z;
            if (p.X * x + p.Y * y + p.Z * z + p.W < 0)
                return false;
        }
        return true;
    }

    // ---- GPU resources ------------------------------------------------------------------------

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
            _locViewProj = Uniform("uViewProj\0"u8);
            _locCam = Uniform("uCam\0"u8);
            _locFog = Uniform("uFog\0"u8);
            _locFogRange = Uniform("uFogRange\0"u8);
            _locTime = Uniform("uTime\0"u8);
            _locFade = Uniform("uFade\0"u8);
            _locMat = Uniform("uMat\0"u8);

            _vao = GenVertexArray();
            _vao.Bind();
            _meshVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _meshVbo);
            fixed (float* p = FoliageMeshes.Vertices)
                Native.glBufferData(GL_ARRAY_BUFFER, FoliageMeshes.Vertices.Length * sizeof(float), p, GL_STATIC_DRAW);
            const int stride = FoliageMeshes.FloatsPerVertex * sizeof(float);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
            Native.glVertexAttribPointer(2, 2, GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
            EnableVertexAttribArray(2);
            _meshIbo = GenBuffer();
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _meshIbo); // remembered by the vertex array
            fixed (ushort* p = FoliageMeshes.Indices)
                Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, FoliageMeshes.Indices.Length * sizeof(ushort), p, GL_STATIC_DRAW);
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            const int istride = FoliageBatches.FloatsPerInstance * sizeof(float);
            Native.glVertexAttribPointer(3, 4, GL_FLOAT, false, istride, (void*)0);
            Native.glVertexAttribPointer(4, 4, GL_FLOAT, false, istride, (void*)(4 * sizeof(float)));
            EnableVertexAttribArray(3);
            EnableVertexAttribArray(4);
            Native.glVertexAttribDivisor(3, 1);
            Native.glVertexAttribDivisor(4, 1);

            if (Native.glGenQueries != null && Native.glBeginQuery != null && Native.glGetQueryObjectui64v != null && Native.glGetQueryObjectiv != null)
            {
                fixed (GlQuery* q = _queries)
                    Native.glGenQueries(QueryRing, q);
                _timers = true;
            }
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[foliage] GL error 0x{err:X} while setting up (continuing)");
            Console.WriteLine($"[foliage] renderer ready: {FoliageMeshes.Vertices.Length / FoliageMeshes.FloatsPerVertex} mesh vertices, "
                + $"{FoliageMeshes.Indices.Length / 3} triangles over {FoliageMeshes.TypeCount * 2} meshes, GPU timer {(_timers ? "on" : "unavailable")}");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[foliage] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
    }

    private int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    /// <summary>Builds and uploads the instance buffer for a map at a quality (only when the plants actually changed).</summary>
    private void Rebuild(Map map, FoliageQuality quality)
    {
        // A reloaded map file with the same plants (Maps.Reload swaps in new Map objects) keeps the buffer as it is.
        if (_batches.Count > 0 && quality == _quality && map.FoliageHash == _builtHash)
        {
            _map = map;
            return;
        }
        var sw = Stopwatch.StartNew();
        _batches = FoliageBatches.Build(map.FoliageInstances, map.HalfX, map.HalfZ, quality);
        _map = map;
        _quality = quality;
        _builtHash = map.FoliageHash;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        fixed (float* p = _batches.Instances)
            Native.glBufferData(GL_ARRAY_BUFFER, Math.Max(_batches.Instances.Length, 1) * sizeof(float), _batches.Count > 0 ? p : null, GL_STATIC_DRAW);
        var c = _batches.TypeCount;
        Console.WriteLine($"[foliage] built {map.Name} at {quality}: {_batches.Count} plants (grass {c[0]}, bushes {c[1]}, shrubs {c[2]}, trees {c[3]}, pines {c[4]}) "
            + $"in {_batches.Chunks.Length} chunks, {_batches.Instances.Length * sizeof(float) / 1024} KB, {sw.Elapsed.TotalMilliseconds:0.0} ms");
        _log = new Stats();
    }

    /// <summary>Releases the per-map instance data (quality Off, or a map without foliage). The small shared meshes stay.</summary>
    public void Free()
    {
        if (_ready)
        {
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, 1, null, GL_STATIC_DRAW);
        }
        _batches = new FoliageBatches();
        _map = null;
        _builtHash = 0;
    }
}
