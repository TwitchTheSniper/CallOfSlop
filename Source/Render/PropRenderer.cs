using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a map's props (trash cans, crates, litter...) and the debris of broken ones.
///
/// How it stays cheap:
/// <list type="bullet">
/// <item>Every prop model in the library is baked once into one static vertex and index buffer (<see cref="PropMeshes"/>);
/// each map gets one static per-instance buffer (<see cref="PropBatches"/>), uploaded when the map changes. A prop
/// breaking or the props being restored rewrites that small buffer once (no per-frame uploads, no allocations).</item>
/// <item>Instanced draws: props are sorted by type and 24 m chunk; each frame the chunks outside the view frustum (and,
/// for small litter, beyond its fade distance) are skipped and visible neighbours merge into one glDrawElementsInstanced.</item>
/// <item>Small cosmetic props dither out with distance (no popping, no blending or sorting); fully faded and broken ones
/// are collapsed in the vertex shader so they cost no pixels. Solid props never fade.</item>
/// <item>Debris pieces are one cube instanced per piece, streamed from a preallocated array (at most <see cref="PropDebris.Capacity"/>).</item>
/// </list>
/// Lighting, fog and the surface textures are the same as the map boxes'. GPU time is measured with GL_TIME_ELAPSED
/// queries read a few frames later (never stalling), and one "[props]" stat line is logged every few seconds.
/// </summary>
public sealed unsafe class PropRenderer
{
    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;
        layout(location = 3) in vec3 aUvLayer;    // texture coordinates, layer (-1: plain colour)
        layout(location = 4) in vec4 iPosScale;   // per instance: position, scale (0 = broken, hidden)
        layout(location = 5) in vec4 iYawFade;    // per instance: yaw (radians), fade distance (0 = never), random 0..1
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec3 vColor;
        out vec2 vUv;
        flat out float vLayer;
        flat out float vFade;
        flat out float vHash;

        void main()
        {
            float s = iPosScale.w;
            float fade = 1.0;
            if (iYawFade.y > 0.0)
            {
                float d = distance(uCam.xz, iPosScale.xz);
                fade = clamp((iYawFade.y - d) / (iYawFade.y * 0.2), 0.0, 1.0);
            }
            if (s <= 0.0 || fade <= 0.0)
            {
                vWorld = iPosScale.xyz; vNormal = vec3(0.0, 1.0, 0.0); vColor = vec3(0.0); vUv = vec2(0.0);
                vLayer = -1.0; vFade = 0.0; vHash = 0.0;
                gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
                return;
            }
            float c = cos(iYawFade.x), sn = sin(iYawFade.x);
            vec3 p = aPos * s;
            p = vec3(p.x * c + p.z * sn, p.y, -p.x * sn + p.z * c);
            vNormal = vec3(aNormal.x * c + aNormal.z * sn, aNormal.y, -aNormal.x * sn + aNormal.z * c);
            vWorld = iPosScale.xyz + p;
            vColor = aColor * (0.93 + 0.14 * iYawFade.z);
            vUv = aUvLayer.xy;
            vLayer = aUvLayer.z;
            vFade = fade;
            vHash = iYawFade.z;
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    private const string DebrisVertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 iPosFade;    // per piece: position, fade
        layout(location = 3) in vec4 iRot;        // per piece: rotation quaternion
        layout(location = 4) in vec4 iSize;
        layout(location = 5) in vec4 iColor;
        uniform mat4 uViewProj;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec3 vColor;
        out vec2 vUv;
        flat out float vLayer;
        flat out float vFade;
        flat out float vHash;

        vec3 Rotate(vec4 q, vec3 v) { return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v); }

        void main()
        {
            vWorld = iPosFade.xyz + Rotate(iRot, aPos * iSize.xyz);
            vNormal = Rotate(iRot, aNormal);
            vColor = iColor.rgb;
            vUv = vec2(0.0);
            vLayer = -1.0;
            vFade = iPosFade.w;
            vHash = fract(iPosFade.x * 7.31 + iPosFade.z * 3.17);
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 330 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec3 vColor;
        in vec2 vUv;
        flat in float vLayer;
        flat in float vFade;
        flat in float vHash;
        uniform sampler2DArray uTex;
        out vec4 FragColor;

        float Bayer(ivec2 p)
        {
            const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
            return (m[(p.x & 3) + (p.y & 3) * 4] + 0.5) / 16.0;
        }

        void main()
        {
            if (vFade < 1.0 && Bayer(ivec2(gl_FragCoord.xy) + ivec2(int(vHash * 7.0), int(vHash * 53.0))) >= vFade)
                discard;
            vec3 n = normalize(vNormal);
            vec3 col = vColor;
            if (vLayer >= 0.0)
                col *= texture(uTex, vec3(vUv, vLayer)).r * 1.25;
            float rim = 0.12 * (1.0 - abs(n.y));
            col = ApplyLight(col, n, 0.0) + col * rim;
            FragColor = vec4(ApplyFog(col, vWorld), 1.0);
        }
        """;

    private bool _ready, _broken;
    private GlProgram _program, _debrisProgram;
    private GlVertexArray _vao, _debrisVao;
    private GlBuffer _meshVbo, _meshIbo, _instVbo, _cubeVbo, _debrisVbo;
    private int _locViewProj, _locCam, _locFog, _locFogRange;
    private int _dLocViewProj, _dLocCam, _dLocFog, _dLocFogRange;

    private PropMeshes? _meshes;
    private int _meshGeneration = -1;
    private Map? _map;
    private int _breakVersion = -1;
    private PropBatches _batches = new();
    private readonly Vector4[] _planes = new Vector4[6];
    private readonly float[] _debrisData = new float[PropDebris.Capacity * PropDebris.FloatsPerPiece];
    private double _lastNow;

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
        public long Instances, Draws, Debris;
        public double CpuMs, GpuMs;

        public readonly double AvgInstances => Frames > 0 ? (double)Instances / Frames : 0;
        public readonly double AvgDraws => Frames > 0 ? (double)Draws / Frames : 0;
        public readonly double AvgDebris => Frames > 0 ? (double)Debris / Frames : 0;
        public readonly double AvgCpuMs => Frames > 0 ? CpuMs / Frames : 0;
        public readonly double AvgGpuMs => GpuSamples > 0 ? GpuMs / GpuSamples : -1;
    }

    private Stats _log;

    /// <summary>Stats since the last <see cref="ResetWindow"/> (for the smoke test).</summary>
    public Stats Window;

    public const double LogInterval = 5;

    /// <summary>Props in the current instance buffer.</summary>
    public int Uploaded => _batches.Count;

    public void ResetWindow(double now) => Window = new Stats { Start = now };

    /// <summary>
    /// Draws the props of <paramref name="map"/> and any flying debris, with the camera, fog and depth buffer
    /// <paramref name="gfx"/> has set up. Call after the opaque boxes. Also steps the debris simulation (frame time).
    /// </summary>
    public void Draw(Map map, Renderer gfx, double now)
    {
        var dt = (float)Math.Clamp(now - _lastNow, 0, 0.1);
        _lastNow = now;
        PropDebris.Update(dt);
        if (_broken)
            return;
        if (map.PropInstances.Count == 0 && PropDebris.Count == 0)
        {
            if (_batches.Count > 0)
                Free();
            return;
        }
        var t0 = Stopwatch.GetTimestamp();
        if (!_ready && !Init())
            return;
        if (_meshGeneration != Props.Generation || _meshes == null)
            UploadMeshes();
        if (map != _map)
            Rebuild(map);
        if (map.PropBreakVersion != _breakVersion && _batches.Count > 0)
        {
            _batches.ApplyBroken(map);
            _breakVersion = map.PropBreakVersion;
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            fixed (float* p = _batches.Instances)
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, _batches.Instances.Length * sizeof(float), p);
        }

        ExtractPlanes(gfx.ViewProj);
        var cam = gfx.CameraPosition;
        var vp = gfx.ViewProj;
        var fog = gfx.FogColor;
        var fogRange = gfx.FogRange;

        var slot = _queryFrame % QueryRing;
        var timing = BeginTimer(slot);
        long draws = 0, instances = 0;

        if (_batches.Count > 0 && _meshes != null)
        {
            _program.Use();
            Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
            Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
            Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
            Native.glUniform2f(_locFogRange, fogRange.X, fogRange.Y);
            _vao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            for (var d = 0; d < _batches.TypeChunkCount.Length; d++)
            {
                var count = _batches.TypeChunkCount[d];
                if (count == 0)
                    continue;
                var def = _meshes.Defs[d];
                var fadeEnd = def.FadeDistance;
                var mesh = _meshes.Ranges[d];
                int runStart = -1, runCount = 0;
                var first = _batches.TypeChunkStart[d];
                for (var i = first; i < first + count; i++)
                {
                    ref readonly var c = ref _batches.Chunks[i];
                    var visible = (fadeEnd <= 0 || NearDistanceXZ(cam, c.Min, c.Max) < fadeEnd) && InFrustum(c.Min, c.Max);
                    if (visible && runStart >= 0 && runStart + runCount == c.Start)
                    {
                        runCount += c.Count;
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

        var debris = PropDebris.Write(_debrisData);
        if (debris > 0)
        {
            _debrisProgram.Use();
            Native.glUniformMatrix4fv(_dLocViewProj, 1, false, &vp.M11);
            Native.glUniform3f(_dLocCam, cam.X, cam.Y, cam.Z);
            Native.glUniform3f(_dLocFog, fog.X, fog.Y, fog.Z);
            Native.glUniform2f(_dLocFogRange, fogRange.X, fogRange.Y);
            _debrisVao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _debrisVbo);
            var bytes = debris * PropDebris.FloatsPerPiece * sizeof(float);
            Native.glBufferData(GL_ARRAY_BUFFER, _debrisData.Length * sizeof(float), null, GL_STREAM_DRAW); // orphan: no stall on last frame's copy
            fixed (float* p = _debrisData)
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, bytes, p);
            Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36, debris);
            draws++;
        }

        if (timing)
        {
            Native.glEndQuery(GL_TIME_ELAPSED);
            _queryPending[slot] = true;
        }
        _queryFrame++;
        gfx.Rebind();

        var cpuMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Accumulate(ref _log, now, draws, instances, debris, cpuMs);
        Accumulate(ref Window, now, draws, instances, debris, cpuMs);
        if (now - _log.Start >= LogInterval)
        {
            var fps = _log.Frames / Math.Max(now - _log.Start, 1e-6);
            Console.WriteLine($"[props] {map.Name}: {_batches.Count} props ({_batches.Solid} solid, {_batches.Cosmetic} cosmetic, {_batches.Types} types, {map.BrokenProps.Count} broken), "
                + $"{_log.AvgInstances:0} drawn/frame in {_log.AvgDraws:0.0} draw calls, debris {_log.AvgDebris:0}, CPU {_log.AvgCpuMs:0.000} ms, "
                + $"GPU {(_log.AvgGpuMs >= 0 ? $"{_log.AvgGpuMs:0.000} ms" : "n/a")}, {fps:0} fps");
            _log = new Stats { Start = now };
        }
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

    private static void Accumulate(ref Stats s, double now, long draws, long instances, int debris, double cpuMs)
    {
        if (s.Start <= 0)
            s.Start = now;
        s.Frames++;
        s.Draws += draws;
        s.Instances += instances;
        s.Debris += debris;
        s.CpuMs += cpuMs;
    }

    private void DrawRun(PropMeshes.Range mesh, int start, int count)
    {
        var offset = (nint)start * PropBatches.FloatsPerInstance * sizeof(float);
        const int stride = PropBatches.FloatsPerInstance * sizeof(float);
        Native.glVertexAttribPointer(4, 4, GL_FLOAT, false, stride, (void*)offset);
        Native.glVertexAttribPointer(5, 4, GL_FLOAT, false, stride, (void*)(offset + 4 * sizeof(float)));
        Native.glDrawElementsInstanced(GL_TRIANGLES, mesh.IndexCount, GL_UNSIGNED_INT, (void*)((nint)mesh.FirstIndex * sizeof(uint)), count);
    }

    // ---- Culling (same as the foliage renderer) -----------------------------------------------

    private static float NearDistanceXZ(Vector3 cam, Vector3 min, Vector3 max)
    {
        var dx = MathF.Max(MathF.Max(min.X - cam.X, 0f), cam.X - max.X);
        var dz = MathF.Max(MathF.Max(min.Z - cam.Z, 0f), cam.Z - max.Z);
        return MathF.Sqrt(dx * dx + dz * dz);
    }

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
    }

    private bool InFrustum(Vector3 min, Vector3 max)
    {
        foreach (var p in _planes)
        {
            var x = p.X >= 0 ? max.X : min.X;
            var y = p.Y >= 0 ? max.Y : min.Y;
            var z = p.Z >= 0 ? max.Z : min.Z;
            if (p.X * x + p.Y * y + p.Z * z + p.W < 0)
                return false;
        }
        return true;
    }

    // ---- GPU resources ------------------------------------------------------------------------

    private GlProgram Link(string vertex, string fragment)
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(vertex).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(fragment).Compile();
        var program = CreateProgram();
        Native.glAttachShader(program, vs);
        Native.glAttachShader(program, fs);
        Native.glLinkProgram(program);
        int linked;
        Native.glGetProgramiv(program, GL_LINK_STATUS, &linked);
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(program, 2048, &len, log);
            throw new InvalidOperationException("shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        vs.Dispose();
        fs.Dispose();
        Lighting.Upload(program);
        program.Use();
        Native.glUniform1i(Uniform(program, "uTex\0"u8), 0); // the box renderer's texture array, bound to unit 0
        return program;
    }

    private static int Uniform(GlProgram program, ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(program, p);
    }

    private bool Init()
    {
        try
        {
            _program = Link(VertexSrc, FragmentSrc);
            _locViewProj = Uniform(_program, "uViewProj\0"u8);
            _locCam = Uniform(_program, "uCam\0"u8);
            _locFog = Uniform(_program, "uFog\0"u8);
            _locFogRange = Uniform(_program, "uFogRange\0"u8);
            _debrisProgram = Link(DebrisVertexSrc, FragmentSrc);
            _dLocViewProj = Uniform(_debrisProgram, "uViewProj\0"u8);
            _dLocCam = Uniform(_debrisProgram, "uCam\0"u8);
            _dLocFog = Uniform(_debrisProgram, "uFog\0"u8);
            _dLocFogRange = Uniform(_debrisProgram, "uFogRange\0"u8);

            // Props: mesh attributes 0-3 from the shared mesh buffer, instance attributes 4-5.
            _vao = GenVertexArray();
            _vao.Bind();
            _meshVbo = GenBuffer();
            _meshIbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _meshVbo);
            const int stride = PropMeshes.FloatsPerVertex * sizeof(float);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
            Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));
            Native.glVertexAttribPointer(3, 3, GL_FLOAT, false, stride, (void*)(9 * sizeof(float)));
            for (uint a = 0; a < 4; a++)
                EnableVertexAttribArray(a);
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _meshIbo); // remembered by the vertex array
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            const int istride = PropBatches.FloatsPerInstance * sizeof(float);
            Native.glVertexAttribPointer(4, 4, GL_FLOAT, false, istride, (void*)0);
            Native.glVertexAttribPointer(5, 4, GL_FLOAT, false, istride, (void*)(4 * sizeof(float)));
            EnableVertexAttribArray(4);
            EnableVertexAttribArray(5);
            Native.glVertexAttribDivisor(4, 1);
            Native.glVertexAttribDivisor(5, 1);

            // Debris: a unit cube (36 vertices, position + normal) and a streamed per-piece buffer.
            _debrisVao = GenVertexArray();
            _debrisVao.Bind();
            _cubeVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cubeVbo);
            var cube = Cube();
            fixed (float* p = cube)
                Native.glBufferData(GL_ARRAY_BUFFER, cube.Length * sizeof(float), p, GL_STATIC_DRAW);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
            _debrisVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _debrisVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, _debrisData.Length * sizeof(float), null, GL_STREAM_DRAW);
            const int dstride = PropDebris.FloatsPerPiece * sizeof(float);
            for (uint a = 0; a < 4; a++)
            {
                Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, dstride, (void*)(a * 4 * sizeof(float)));
                EnableVertexAttribArray(2 + a);
                Native.glVertexAttribDivisor(2 + a, 1);
            }

            if (Native.glGenQueries != null && Native.glBeginQuery != null && Native.glGetQueryObjectui64v != null && Native.glGetQueryObjectiv != null)
            {
                fixed (GlQuery* q = _queries)
                    Native.glGenQueries(QueryRing, q);
                _timers = true;
            }
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[props] GL error 0x{err:X} while setting up (continuing)");
            Console.WriteLine($"[props] renderer ready, GPU timer {(_timers ? "on" : "unavailable")}");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[props] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
    }

    private void UploadMeshes()
    {
        var sw = Stopwatch.StartNew();
        _meshes = PropMeshes.Build(Props.All);
        _meshGeneration = Props.Generation;
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _meshVbo);
        fixed (float* p = _meshes.Vertices)
            Native.glBufferData(GL_ARRAY_BUFFER, Math.Max(_meshes.Vertices.Length, 1) * sizeof(float), _meshes.Vertices.Length > 0 ? p : null, GL_STATIC_DRAW);
        fixed (uint* p = _meshes.Indices)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, Math.Max(_meshes.Indices.Length, 1) * sizeof(uint), _meshes.Indices.Length > 0 ? p : null, GL_STATIC_DRAW);
        Console.WriteLine($"[props] meshes for {_meshes.Defs.Length} prop types: {_meshes.Vertices.Length / PropMeshes.FloatsPerVertex} vertices, "
            + $"{_meshes.Triangles} triangles, {sw.Elapsed.TotalMilliseconds:0.0} ms");
        _map = null; // instance batches refer to mesh indices: rebuild them too
    }

    private void Rebuild(Map map)
    {
        var sw = Stopwatch.StartNew();
        _batches = PropBatches.Build(map, _meshes!);
        _map = map;
        _breakVersion = map.PropBreakVersion;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        fixed (float* p = _batches.Instances)
            Native.glBufferData(GL_ARRAY_BUFFER, Math.Max(_batches.Instances.Length, 1) * sizeof(float), _batches.Count > 0 ? p : null, GL_STATIC_DRAW);
        if (_batches.Count > 0)
            Console.WriteLine($"[props] built {map.Name}: {_batches.Count} props ({_batches.Solid} solid, {_batches.Cosmetic} cosmetic) of {_batches.Types} types "
                + $"in {_batches.Chunks.Length} chunks, {_batches.Instances.Length * sizeof(float) / 1024} KB, {sw.Elapsed.TotalMilliseconds:0.0} ms");
        _log = new Stats();
    }

    /// <summary>Releases the per-map instance data (a map without props). The shared meshes stay.</summary>
    public void Free()
    {
        if (_ready)
        {
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, 1, null, GL_STATIC_DRAW);
        }
        _batches = new PropBatches();
        _map = null;
    }

    private static float[] Cube()
    {
        (Vector3 n, Vector3 u, Vector3 v)[] faces =
        [
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
        ];
        var data = new List<float>(36 * 6);
        foreach (var (n, u, v) in faces)
        {
            var c = n * 0.5f;
            Vector3 a = c - u * 0.5f - v * 0.5f, b = c + u * 0.5f - v * 0.5f, d = c + u * 0.5f + v * 0.5f, e = c - u * 0.5f + v * 0.5f;
            Vector3[] corners = [a, b, d, a, d, e];
            foreach (var p in corners)
                data.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z]);
        }
        return data.ToArray();
    }
}
