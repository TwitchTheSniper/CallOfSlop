using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws decals: the map's baked decals (<see cref="DecalMesh"/>) and the bullet impacts (<see cref="ImpactDecals"/>).
///
/// How it stays cheap:
/// <list type="bullet">
/// <item>One decal atlas (a 2D texture array, a layer per decal, mipmapped) built once from the decal files.</item>
/// <item>Map decals are baked once into one static vertex buffer, sorted by blend mode and 24 m chunk. Each frame chunks
/// outside the view frustum or past their fade distance are skipped, and neighbours in the buffer merge into one draw.
/// Decals on a box that broke or moved are hidden in the vertex shader through a small "box alive" texture, updated only
/// when a box changes (no rebake).</item>
/// <item>Impacts are one instanced draw of a quad over a fixed ring (at most the setting's capacity); only slots that
/// changed are uploaded (glBufferSubData of a few floats when shots land). Fading, distance fade and culling of dead or
/// far impacts happen in the vertex shader. Culling a thousand quads on the CPU would cost more than drawing them.</item>
/// <item>Depth test on, depth writes off, polygon offset against z-fighting; lit and fogged with the shared Lighting.</item>
/// </list>
/// GPU time is measured with timestamp queries (read a few frames later, never stalling) and one "[decals]" line is
/// logged every few seconds.
/// </summary>
public sealed unsafe class DecalRenderer
{
    private const string StaticVertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec2 aUv;
        layout(location = 3) in vec3 aInfo;   // atlas layer, owner box (-1: none), fade distance
        layout(location = 4) in vec4 aColor;
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        uniform sampler2D uAlive;             // 1 per box while it is as baked (1024 boxes per row)
        out vec3 vWorld;
        out vec3 vNormal;
        out vec2 vUv;
        out vec4 vColor;
        flat out float vLayer;

        void main()
        {
            float alive = 1.0;
            if (aInfo.y > -0.5)
            {
                int o = int(aInfo.y + 0.5);
                alive = texelFetch(uAlive, ivec2(o & 1023, o >> 10), 0).r;
            }
            float d = distance(uCam, aPos);
            float fade = clamp((aInfo.z - d) / (aInfo.z * 0.25), 0.0, 1.0);
            vWorld = aPos;
            vNormal = aNormal;
            vUv = aUv;
            vLayer = aInfo.x;
            vColor = vec4(aColor.rgb, aColor.a * fade);
            // A broken or moved box: every vertex of its triangles goes off screen together.
            gl_Position = alive < 0.5 ? vec4(0.0, 0.0, -2.0, 1.0) : uViewProj * vec4(aPos, 1.0);
        }
        """;

    private const string ImpactVertexSrc = """
        #version 330 core
        layout(location = 0) in vec2 aCorner;     // -1..1
        layout(location = 1) in vec4 iPosSize;    // per instance: position, size (0: empty slot)
        layout(location = 2) in vec4 iNormalRot;  // normal, turn (radians)
        layout(location = 3) in vec4 iInfo;       // atlas layer, birth time, fade start (-1: none), shade
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        uniform float uTime;
        uniform float uFadeTime;
        uniform float uDrawDist;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec2 vUv;
        out vec4 vColor;
        flat out float vLayer;

        void main()
        {
            float life = iInfo.z < 0.0 ? 1.0 : clamp(1.0 - (uTime - iInfo.z) / uFadeTime, 0.0, 1.0);
            float d = distance(uCam, iPosSize.xyz);
            float far = clamp((uDrawDist - d) / (uDrawDist * 0.2), 0.0, 1.0);
            if (iPosSize.w <= 0.0 || life <= 0.0 || far <= 0.0)
            {
                vWorld = vec3(0.0); vNormal = vec3(0.0, 1.0, 0.0); vUv = vec2(0.0); vColor = vec4(0.0); vLayer = 0.0;
                gl_Position = vec4(0.0, 0.0, -2.0, 1.0);
                return;
            }
            vec3 n = iNormalRot.xyz;
            vec3 up = abs(n.y) > 0.9 ? vec3(0.0, 0.0, -1.0) : vec3(0.0, 1.0, 0.0);
            vec3 r = normalize(cross(up, n));
            vec3 u = cross(n, r);
            float c = cos(iNormalRot.w), s = sin(iNormalRot.w);
            vec2 q = vec2(aCorner.x * c - aCorner.y * s, aCorner.x * s + aCorner.y * c) * (iPosSize.w * 0.5);
            vWorld = iPosSize.xyz + r * q.x + u * q.y + n * 0.002;
            vNormal = n;
            vUv = aCorner * 0.5 + 0.5;
            vLayer = iInfo.x;
            vColor = vec4(vec3(iInfo.w), life * far);
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    // Alpha decals: the decal's colour, lit like the boxes (same rim term), over the surface by its alpha.
    // Multiply decals: the surface times the decal's colour by its alpha, fading to no effect in the fog.
    private static readonly string FragmentSrc = "#version 330 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec2 vUv;
        in vec4 vColor;
        flat in float vLayer;
        uniform sampler2DArray uAtlas;
        uniform float uMultiply;
        out vec4 FragColor;

        void main()
        {
            vec4 t = texture(uAtlas, vec3(vUv, vLayer));
            float a = t.a * vColor.a;
            if (a < 0.004)
                discard;
            if (uMultiply > 0.5)
            {
                vec3 m = mix(vec3(1.0), t.rgb * vColor.rgb, a);
                float keep = ApplyFog(vec3(1.0), vWorld).r - ApplyFog(vec3(0.0), vWorld).r; // 1 - fog amount
                FragColor = vec4(mix(vec3(1.0), m, keep), 1.0);
                return;
            }
            vec3 n = normalize(vNormal);
            vec3 albedo = t.rgb * vColor.rgb;
            vec3 col = ApplyLight(albedo, n, 0.0) + albedo * (0.12 * (1.0 - abs(n.y)));
            FragColor = vec4(ApplyFog(col, vWorld), a);
        }
        """;

    private const uint AtlasUnit = 6, AliveUnit = 7;
    private const int QueryRing = 4;
    private const int AliveWidth = 1024;

    private bool _ready, _broken;
    private GlProgram _static, _impact;
    private GlVertexArray _staticVao, _impactVao;
    private GlBuffer _staticVbo, _cornerVbo, _instVbo;
    private uint _atlas, _alive;
    private int _sLocViewProj, _sLocCam, _sLocFog, _sLocFogRange, _sLocMultiply;
    private int _iLocViewProj, _iLocCam, _iLocFog, _iLocFogRange, _iLocMultiply, _iLocTime, _iLocFadeTime, _iLocDrawDist;
    private int _atlasGeneration = -1, _atlasLayers, _atlasTaskGeneration = -1, _maxGlLayers;
    private Task<(DecalDef[] Global, byte[] Data, double Ms, int Painted, int Dropped)>? _atlasTask;
    private DecalDef[] _atlasPack = [], _atlasTaskPack = []; // the map's pack decals in the uploaded atlas / being painted
    private DecalDef[]? _globalDefs; // the global layers' CPU copy (at most Decals.MaxGlobal x 64 KB), so a new map only paints its pack decals
    private byte[]? _globalData;
    private double _atlasMs;
    private DecalMesh? _mesh;
    private Map? _meshMap;
    private int _aliveVersion = -1, _aliveRows;
    private byte[] _aliveData = [];
    private int _instCapacity = -1;
    private readonly Vector4[] _planes = new Vector4[6];
    private GpuStampRing? _timer;

    /// <summary>Per-frame and averaged numbers for the log and the smoke test.</summary>
    public struct Stats
    {
        public double Start;
        public long Frames, Draws, Triangles, Impacts, Chunks;
        public double CpuMs, GpuMs;
        public long GpuSamples;
        public readonly double AvgDraws => Frames > 0 ? (double)Draws / Frames : 0;
        public readonly double AvgTriangles => Frames > 0 ? (double)Triangles / Frames : 0;
        public readonly double AvgImpacts => Frames > 0 ? (double)Impacts / Frames : 0;
        public readonly double AvgChunks => Frames > 0 ? (double)Chunks / Frames : 0;
        public readonly double AvgCpuMs => Frames > 0 ? CpuMs / Frames : 0;
        public readonly double AvgGpuMs => GpuSamples > 0 ? GpuMs / GpuSamples : -1;
    }

    private Stats _log;

    /// <summary>Stats since the last <see cref="ResetWindow"/> (smoke test).</summary>
    public Stats Window;

    public const double LogInterval = 5;

    public void ResetWindow(double now) => Window = new Stats { Start = now };

    /// <summary>The baked decal mesh in the GPU buffer now (null: none).</summary>
    public DecalMesh? Uploaded => _mesh;

    /// <summary>
    /// Draws the decals of <paramref name="map"/> and the bullet impacts, with the camera, fog and depth buffer
    /// <paramref name="gfx"/> has set up. Call after the opaque world and before the sky. Applies the impact setting.
    /// </summary>
    public void Draw(Map map, Renderer gfx, ImpactDecalQuality quality, double now)
    {
        ImpactDecals.Configure(quality);
        var pool = ImpactDecals.Pool;
        pool.Tick(now);
        if (!ReferenceEquals(pool.Map, map))
            pool.Clear(map, now); // a new map: no impacts carried over
        if (_broken)
            return;
        // Baked on a worker thread the first time (null until ready: impacts are drawn meanwhile).
        var mesh = map.DecalRequests.Count > 0 ? map.DecalMeshIfReady() ?? DecalMesh.Empty : DecalMesh.Empty;
        if (mesh.VertexCount == 0 && pool.HighWater == 0)
            return;
        var t0 = Stopwatch.GetTimestamp();
        if (!_ready && !Init())
            return;
        try
        {
            if (!AtlasHolds(mesh.PackDefs) && !UploadAtlas(mesh.PackDefs))
            {
                if (_atlasGeneration != Decals.Generation)
                    return; // the atlas is still being painted on a worker thread (a few frames at startup)
                mesh = DecalMesh.Empty; // this map's pack decals are being painted: impacts only, for a few frames
            }
            if (!ReferenceEquals(mesh, _mesh) || !ReferenceEquals(map, _meshMap))
                UploadMesh(map, mesh);
            if (_aliveVersion != DecalEvents.Version)
                UpdateAlive(map, mesh, upload: true);
            UploadImpacts(pool);
            Render(map, gfx, mesh, pool, quality, now, t0);
        }
        catch (Exception ex)
        {
            // Never take the game down over decals: log once and carry on without them.
            _broken = true;
            Console.WriteLine($"[decals] rendering disabled: {ex.Message}");
        }
        finally
        {
            gfx.Rebind();
        }
    }

    private void Render(Map map, Renderer gfx, DecalMesh mesh, ImpactPool pool, ImpactDecalQuality quality, double now, long t0)
    {
        var vp = gfx.ViewProj;
        var cam = gfx.CameraPosition;
        var fog = gfx.FogColor;
        var fogRange = gfx.FogRange;
        ExtractPlanes(vp);

        _timer?.BeginFrame();
        DepthMask(false);
        Enable(GlCapability.Blend);
        Enable(GlCapability.PolygonOffsetFill);
        Native.glPolygonOffset(-1f, -2f);
        Native.glActiveTexture(GL_TEXTURE0 + AtlasUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _atlas);
        Native.glActiveTexture(GL_TEXTURE0 + AliveUnit);
        Native.glBindTexture(GL_TEXTURE_2D, _alive);
        Native.glActiveTexture(GL_TEXTURE0);

        long draws = 0, triangles = 0, chunks = 0;
        if (mesh.VertexCount > 0)
        {
            _static.Use();
            Native.glUniformMatrix4fv(_sLocViewProj, 1, false, &vp.M11);
            Native.glUniform3f(_sLocCam, cam.X, cam.Y, cam.Z);
            Native.glUniform3f(_sLocFog, fog.X, fog.Y, fog.Z);
            Native.glUniform2f(_sLocFogRange, fogRange.X, fogRange.Y);
            _staticVao.Bind();
            for (var pass = 0; pass < 2; pass++)
            {
                var blend = pass == 0 ? DecalBlend.Multiply : DecalBlend.Alpha;
                if (blend == DecalBlend.Multiply)
                    BlendFunc(GL_DST_COLOR, GL_ZERO);
                else
                    BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                Native.glUniform1f(_sLocMultiply, blend == DecalBlend.Multiply ? 1f : 0f);
                int runStart = -1, runCount = 0;
                foreach (var c in mesh.Chunks)
                {
                    if (c.Blend != blend)
                        continue;
                    if (NearDistance(cam, c.Min, c.Max) > c.FadeEnd || !InFrustum(c.Min, c.Max))
                        continue;
                    chunks++;
                    triangles += c.Count / 3;
                    if (runStart >= 0 && runStart + runCount == c.First)
                    {
                        runCount += c.Count;
                        continue;
                    }
                    if (runStart >= 0)
                    {
                        Native.glDrawArrays(GL_TRIANGLES, runStart, runCount);
                        draws++;
                    }
                    runStart = c.First;
                    runCount = c.Count;
                }
                if (runStart >= 0)
                {
                    Native.glDrawArrays(GL_TRIANGLES, runStart, runCount);
                    draws++;
                }
            }
        }

        var impacts = pool.HighWater;
        if (impacts > 0)
        {
            BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
            _impact.Use();
            Native.glUniformMatrix4fv(_iLocViewProj, 1, false, &vp.M11);
            Native.glUniform3f(_iLocCam, cam.X, cam.Y, cam.Z);
            Native.glUniform3f(_iLocFog, fog.X, fog.Y, fog.Z);
            Native.glUniform2f(_iLocFogRange, fogRange.X, fogRange.Y);
            Native.glUniform1f(_iLocMultiply, 0f);
            Native.glUniform1f(_iLocTime, pool.Time);
            Native.glUniform1f(_iLocFadeTime, ImpactPool.FadeTime);
            Native.glUniform1f(_iLocDrawDist, ImpactPool.DrawDistanceFor(quality));
            _impactVao.Bind();
            Native.glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, impacts);
            draws++;
            triangles += impacts * 2;
        }

        Native.glDisable(GlCapability.PolygonOffsetFill);
        BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        Native.glDisable(GlCapability.Blend);
        DepthMask(true);
        _timer?.EndPass(0);
        _timer?.EndFrame();

        var cpuMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Accumulate(ref _log, now, draws, triangles, impacts, chunks, cpuMs);
        Accumulate(ref Window, now, draws, triangles, impacts, chunks, cpuMs);
        if (now - _log.Start >= LogInterval)
        {
            var fps = _log.Frames / Math.Max(now - _log.Start, 1e-6);
            Console.WriteLine($"[decals] {map.Name}: {mesh.Placed} map decals ({mesh.Triangles} triangles, {mesh.Chunks.Length} chunks, {_log.AvgChunks:0.0} visible), "
                + $"impacts {pool.Showing()} showing / {pool.Count} in ring / {pool.Capacity} cap ({quality}; added {pool.Added}, merged {pool.Merged}, popped {pool.Popped}, "
                + $"player-capped {pool.PlayerFaded}, removed {pool.Removed}), {_log.AvgDraws:0.0} draw calls, {_log.AvgTriangles:0} triangles, "
                + $"CPU {_log.AvgCpuMs:0.000} ms, GPU {(_log.AvgGpuMs >= 0 ? $"{_log.AvgGpuMs:0.000} ms" : "n/a")}, {fps:0} fps, atlas {_atlasLayers} layers, "
                + $"GL error 0x{(uint)Native.glGetError():X}");
            _log = new Stats { Start = now };
        }
    }

    private void OnGpuSample(int pass, double ms)
    {
        _log.GpuMs += ms;
        _log.GpuSamples++;
        Window.GpuMs += ms;
        Window.GpuSamples++;
    }

    private static void Accumulate(ref Stats s, double now, long draws, long triangles, long impacts, long chunks, double cpuMs)
    {
        if (s.Start <= 0)
            s.Start = now;
        s.Frames++;
        s.Draws += draws;
        s.Triangles += triangles;
        s.Impacts += impacts;
        s.Chunks += chunks;
        s.CpuMs += cpuMs;
    }

    // ---- Uploads ------------------------------------------------------------------------------------------

    /// <summary>Does the uploaded atlas hold the current global library and exactly these pack decals (a map's <see cref="DecalMesh.PackDefs"/>)?</summary>
    private bool AtlasHolds(DecalDef[] pack)
    {
        if (_atlasGeneration != Decals.Generation)
            return false;
        if (ReferenceEquals(pack, _atlasPack))
            return true;
        if (!pack.SequenceEqual(_atlasPack))
            return false;
        _atlasPack = pack; // same decals: the next frame matches by reference
        return true;
    }

    /// <summary>
    /// The atlas is map-scoped: the global library (always: impacts can land anywhere) then the pack decals the current
    /// map places (<paramref name="pack"/>, from its bake), so a pack with thousands of decals costs only what a map uses.
    /// Painted on a worker thread (the global layers are copied from a CPU copy, only the pack layers are painted) and
    /// uploaded once it's done. False while it's still being painted (never stalling a frame).
    /// </summary>
    private bool UploadAtlas(DecalDef[] pack)
    {
        var generation = Decals.Generation;
        if (_maxGlLayers == 0)
        {
            int max;
            Native.glGetIntegerv(GL_MAX_ARRAY_TEXTURE_LAYERS, &max);
            _maxGlLayers = max > 0 ? max : DecalAtlas.MaxLayers;
            Console.WriteLine($"[decals] GPU allows {_maxGlLayers} array texture layers (a map's atlas needs at most {Decals.MaxGlobal} global + {Decals.MaxPackDecalsPerMap} pack)");
        }
        if (_atlasTask == null || _atlasTaskGeneration != generation || !ReferenceEquals(pack, _atlasTaskPack) && !pack.SequenceEqual(_atlasTaskPack))
        {
            _atlasTaskGeneration = generation;
            _atlasTaskPack = pack;
            var global = Decals.All;
            var prevDefs = _globalDefs;
            var prevData = _globalData;
            var room = Math.Max(0, Math.Min(_maxGlLayers, DecalAtlas.MaxLayers) - global.Length);
            var used = pack.Length > room ? pack[..room] : pack;
            _atlasTask = Task.Run(() =>
            {
                var sw0 = Stopwatch.StartNew();
                var defs = new DecalDef[global.Length + used.Length];
                global.CopyTo(defs, 0);
                used.CopyTo(defs, global.Length);
                // The global layers are copied from the last build; only the map's pack decals are painted. Image problems are logged.
                var bytes = DecalAtlas.Build(defs, null, prevDefs, prevData, out var painted);
                return (global, bytes, sw0.Elapsed.TotalMilliseconds, painted, pack.Length - used.Length);
            });
        }
        if (!_atlasTask.IsCompleted)
            return false;
        var ok = _atlasTask.IsCompletedSuccessfully;
        var (globalDefs, data, paintMs, paintedLayers, dropped) = ok ? _atlasTask.Result : (Array.Empty<DecalDef>(), new byte[DecalAtlas.BytesPerLayer], 0.0, 0, 0);
        if (!ok)
            Console.WriteLine($"[decals] painting the atlas failed: {_atlasTask.Exception?.GetBaseException().Message}");
        _atlasTask = null;
        if (!ReferenceEquals(globalDefs, _globalDefs))
        {
            _globalDefs = globalDefs;
            _globalData = data.AsSpan(0, Math.Min(data.Length, globalDefs.Length * DecalAtlas.BytesPerLayer)).ToArray();
        }
        if (dropped > 0)
            Console.WriteLine($"[decals] atlas: this map places {pack.Length} different pack decals but the GPU allows {_maxGlLayers} layers in all; "
                              + $"{dropped} of them show another decal's picture");
        var sw = Stopwatch.StartNew();
        _atlasLayers = Math.Max(1, data.Length / DecalAtlas.BytesPerLayer);
        Native.glActiveTexture(GL_TEXTURE0 + AtlasUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _atlas);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = data)
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, DecalAtlas.LayerSize, DecalAtlas.LayerSize, _atlasLayers, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAX_ANISOTROPY, 4);
        Native.glActiveTexture(GL_TEXTURE0);
        _atlasGeneration = generation;
        _atlasPack = pack;
        _atlasMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[decals] atlas: {_atlasLayers} layer(s) of {DecalAtlas.LayerSize} px ({globalDefs.Length} global + {_atlasLayers - globalDefs.Length} of this map's pack decals; "
                          + $"{data.Length / 1024} KB + mipmaps), {paintedLayers} painted in {paintMs:0.0} ms (worker thread), uploaded in {_atlasMs:0.0} ms");
        return true;
    }

    private void UploadMesh(Map map, DecalMesh mesh)
    {
        Native.glBindBuffer(GL_ARRAY_BUFFER, _staticVbo);
        var floats = mesh.VertexCount * DecalMesh.FloatsPerVertex;
        if (floats > 0)
        {
            fixed (float* p = mesh.Vertices)
                Native.glBufferData(GL_ARRAY_BUFFER, floats * sizeof(float), p, GL_STATIC_DRAW);
        }
        else
            Native.glBufferData(GL_ARRAY_BUFFER, 16, null, GL_STATIC_DRAW);
        _mesh = mesh;
        _meshMap = map;
        UpdateAlive(map, mesh, upload: false);
        // A fresh alive texture sized for this map's boxes.
        _aliveRows = Math.Max(1, (mesh.Owners.Length + AliveWidth - 1) / AliveWidth);
        Native.glActiveTexture(GL_TEXTURE0 + AliveUnit);
        Native.glBindTexture(GL_TEXTURE_2D, _alive);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = _aliveData)
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_R8, AliveWidth, _aliveRows, 0, GL_RED, GL_UNSIGNED_BYTE, p);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glActiveTexture(GL_TEXTURE0);
        if (mesh.VertexCount > 0)
            Console.WriteLine($"[decals] uploaded '{map.Name}': {mesh.VertexCount} vertices ({floats * sizeof(float) / 1024} KB), {mesh.Chunks.Length} chunks, {mesh.Owners.Length} boxes watched");
    }

    /// <summary>Recomputes which boxes are as baked (cheap: one comparison per box) and uploads the texture.</summary>
    private void UpdateAlive(Map map, DecalMesh mesh, bool upload)
    {
        _aliveVersion = DecalEvents.Version;
        var rows = Math.Max(1, (mesh.Owners.Length + AliveWidth - 1) / AliveWidth);
        if (_aliveData.Length != rows * AliveWidth)
            _aliveData = new byte[rows * AliveWidth];
        DecalEvents.Alive(map, mesh, _aliveData);
        if (!upload || _aliveRows != rows)
            return;
        Native.glActiveTexture(GL_TEXTURE0 + AliveUnit);
        Native.glBindTexture(GL_TEXTURE_2D, _alive);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = _aliveData)
            Native.glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, AliveWidth, rows, GL_RED, GL_UNSIGNED_BYTE, p);
        Native.glActiveTexture(GL_TEXTURE0);
    }

    private void UploadImpacts(ImpactPool pool)
    {
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        if (_instCapacity != pool.Capacity)
        {
            _instCapacity = pool.Capacity;
            var bytes = Math.Max(1, pool.Capacity) * ImpactPool.FloatsPerInstance * sizeof(float);
            if (pool.Capacity > 0)
            {
                fixed (float* p = pool.Instances)
                    Native.glBufferData(GL_ARRAY_BUFFER, bytes, p, GL_DYNAMIC_DRAW);
            }
            else
                Native.glBufferData(GL_ARRAY_BUFFER, bytes, null, GL_DYNAMIC_DRAW);
            pool.ClearDirty();
            return;
        }
        if (pool.DirtyMax < pool.DirtyMin || pool.Capacity == 0)
            return;
        const int slotBytes = ImpactPool.FloatsPerInstance * sizeof(float);
        fixed (float* p = pool.Instances)
        {
            if (pool.DirtyOverflow)
            {
                var first = pool.DirtyMin;
                var count = Math.Min(pool.DirtyMax, pool.Capacity - 1) - first + 1;
                if (count > 0)
                    Native.glBufferSubData(GL_ARRAY_BUFFER, (nint)first * slotBytes, (nint)count * slotBytes, p + first * ImpactPool.FloatsPerInstance);
            }
            else
            {
                foreach (var slot in pool.DirtySlots)
                    Native.glBufferSubData(GL_ARRAY_BUFFER, (nint)slot * slotBytes, slotBytes, p + slot * ImpactPool.FloatsPerInstance);
            }
        }
        pool.ClearDirty();
    }

    // ---- Culling ----------------------------------------------------------------------------------------------

    private static float NearDistance(Vector3 cam, Vector3 min, Vector3 max)
    {
        var d = Vector3.Max(Vector3.Max(min - cam, Vector3.Zero), cam - max);
        return d.Length();
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

    // ---- GPU resources ------------------------------------------------------------------------------------------

    private static GlProgram Link(string vertex, string fragment)
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
        Native.glUniform1i(Uniform(program, "uAtlas\0"u8), (int)AtlasUnit);
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
            _static = Link(StaticVertexSrc, FragmentSrc);
            Native.glUniform1i(Uniform(_static, "uAlive\0"u8), (int)AliveUnit);
            _sLocViewProj = Uniform(_static, "uViewProj\0"u8);
            _sLocCam = Uniform(_static, "uCam\0"u8);
            _sLocFog = Uniform(_static, "uFog\0"u8);
            _sLocFogRange = Uniform(_static, "uFogRange\0"u8);
            _sLocMultiply = Uniform(_static, "uMultiply\0"u8);
            _impact = Link(ImpactVertexSrc, FragmentSrc);
            _iLocViewProj = Uniform(_impact, "uViewProj\0"u8);
            _iLocCam = Uniform(_impact, "uCam\0"u8);
            _iLocFog = Uniform(_impact, "uFog\0"u8);
            _iLocFogRange = Uniform(_impact, "uFogRange\0"u8);
            _iLocMultiply = Uniform(_impact, "uMultiply\0"u8);
            _iLocTime = Uniform(_impact, "uTime\0"u8);
            _iLocFadeTime = Uniform(_impact, "uFadeTime\0"u8);
            _iLocDrawDist = Uniform(_impact, "uDrawDist\0"u8);

            // Map decals: one static buffer, 15 floats per vertex.
            _staticVao = GenVertexArray();
            _staticVao.Bind();
            _staticVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _staticVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, 16, null, GL_STATIC_DRAW);
            const int stride = DecalMesh.FloatsPerVertex * sizeof(float);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
            Native.glVertexAttribPointer(2, 2, GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));
            Native.glVertexAttribPointer(3, 3, GL_FLOAT, false, stride, (void*)(8 * sizeof(float)));
            Native.glVertexAttribPointer(4, 4, GL_FLOAT, false, stride, (void*)(11 * sizeof(float)));
            for (uint a = 0; a < 5; a++)
                EnableVertexAttribArray(a);

            // Impacts: a quad (triangle strip) and the per-instance ring.
            _impactVao = GenVertexArray();
            _impactVao.Bind();
            _cornerVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cornerVbo);
            float* corners = stackalloc float[] { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
            Native.glBufferData(GL_ARRAY_BUFFER, 8 * sizeof(float), corners, GL_STATIC_DRAW);
            Native.glVertexAttribPointer(0, 2, GL_FLOAT, false, 2 * sizeof(float), (void*)0);
            EnableVertexAttribArray(0);
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, ImpactPool.FloatsPerInstance * sizeof(float), null, GL_DYNAMIC_DRAW);
            const int istride = ImpactPool.FloatsPerInstance * sizeof(float);
            for (uint a = 0; a < 3; a++)
            {
                Native.glVertexAttribPointer(1 + a, 4, GL_FLOAT, false, istride, (void*)(a * 4 * sizeof(float)));
                EnableVertexAttribArray(1 + a);
                Native.glVertexAttribDivisor(1 + a, 1);
            }
            _instCapacity = -1;

            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _atlas = tex.Id;
            GlTexture alive = default;
            Native.glGenTextures(1, &alive);
            _alive = alive.Id;
            if (_atlas == 0 || _alive == 0)
                throw new InvalidOperationException("glGenTextures gave no texture");

            if (GlGpuStamps.Create(GpuStampRing.QueryCount(1, QueryRing)) is { } stamps)
                _timer = new GpuStampRing(1, QueryRing, stamps, OnGpuSample);
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[decals] GL error 0x{err:X} while setting up (continuing)");
            Console.WriteLine($"[decals] renderer ready, GPU timer {(_timer != null ? "on (timestamps)" : "unavailable")}");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[decals] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
    }
}
