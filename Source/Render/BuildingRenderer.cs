using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws the map's buildings: their shell boxes (walls, floors, roofs, stairs), doors and window glass.
///
/// How it stays cheap:
/// <list type="bullet">
/// <item>One unit cube and one static per-instance buffer of every shell box on the map (centre, size, colour, texture
/// layer: 64 bytes each), uploaded once when the map changes. Each building is a contiguous run; a building outside the
/// view frustum is skipped, visible neighbours merge into one glDrawArraysInstanced. A map of a few dozen buildings is
/// a handful of draw calls instead of thousands of single boxes.</item>
/// <item>Doors and glass are a second, small instance buffer rewritten only when one changes or while a door swings
/// (the swing is a rotation about the hinge in the vertex shader). Glass is drawn last, blended, without depth writes.</item>
/// <item>No allocations per frame. Lighting, fog and textures match the map boxes (same <see cref="Lighting"/>, world-space
/// texture mapping, the box renderer's texture array).</item>
/// </list>
/// One "[buildings]" stat line (boxes, draws, CPU time) is logged every few seconds while buildings are in view.
/// </summary>
public sealed unsafe class BuildingRenderer
{
    public const int FloatsPerInstance = 16;
    public const float SwingSeconds = 0.35f;

    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 iCenterYaw;   // box centre, turn about the pivot (radians, doors)
        layout(location = 3) in vec4 iHalfLayer;   // half size, texture layer (-1: plain colour)
        layout(location = 4) in vec4 iColor;       // colour, alpha
        layout(location = 5) in vec4 iPivot;       // pivot x, z (doors: the hinge)
        uniform mat4 uViewProj;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColor;
        flat out float vLayer;

        void main()
        {
            vec3 p = iCenterYaw.xyz + aPos * iHalfLayer.xyz * 2.0;
            vec3 n = aNormal;
            if (iCenterYaw.w != 0.0)
            {
                float c = cos(iCenterYaw.w), s = sin(iCenterYaw.w);
                vec2 rel = p.xz - iPivot.xy;
                rel = vec2(rel.x * c + rel.y * s, -rel.x * s + rel.y * c);
                p.xz = iPivot.xy + rel;
                n = vec3(n.x * c + n.z * s, n.y, -n.x * s + n.z * c);
            }
            vWorld = p;
            vNormal = n;
            vColor = iColor;
            vLayer = iHalfLayer.w;
            gl_Position = uViewProj * vec4(p, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 330 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        in vec4 vColor;
        flat in float vLayer;
        uniform sampler2DArray uTex;
        out vec4 FragColor;

        void main()
        {
            vec3 n = normalize(vNormal);
            vec3 col = vColor.rgb;
            if (vLayer >= 0.0)
            {
                // Planar mapping in world space, one repeat per 2 m (as the map boxes).
                vec2 uv = abs(n.y) > 0.5 ? vWorld.xz : (abs(n.x) > 0.5 ? vec2(vWorld.z, vWorld.y) : vec2(vWorld.x, vWorld.y));
                col *= texture(uTex, vec3(uv * 0.5, vLayer)).r * 1.25;
            }
            float rim = 0.12 * (1.0 - abs(n.y));
            col = ApplyLight(col, n, 0.0) + col * rim;
            FragColor = vec4(ApplyFog(col, vWorld), vColor.a);
        }
        """;

    private bool _ready, _broken;
    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _cubeVbo, _staticVbo, _dynVbo;
    private int _locViewProj, _locCam, _locFog, _locFogRange;
    private readonly Vector4[] _planes = new Vector4[6];

    private Map? _map;
    private float[] _static = [];
    private (int Start, int Count, Vector3 Min, Vector3 Max)[] _runs = [];
    private float[] _dyn = [];
    private int _doorCount, _glassCount, _dynVersion = -1;
    private double _lastNow;
    private int _destructionVersion = -1;
    private int[] _runVersions = [];
    private long _destructionUploads;

    /// <summary>Shell boxes uploaded for the current map.</summary>
    public int Boxes { get; private set; }

    /// <summary>Draw calls and instances drawn last frame (shell, doors, glass).</summary>
    public int LastDraws { get; private set; }

    public int LastInstances { get; private set; }

    private double _statStart = -1, _cpuMs;
    private long _statFrames, _statDraws, _statInstances;

    /// <summary>
    /// Draws the map's buildings with the camera, fog and texture array <paramref name="gfx"/> has set up (after the opaque
    /// boxes). Also eases the doors' swing (frame time).
    /// </summary>
    public void Draw(Map map, Renderer gfx, double now)
    {
        var dt = (float)Math.Clamp(now - _lastNow, 0, 0.1);
        _lastNow = now;
        if (_broken)
            return;
        if (map.PlacedBuildings.Count == 0 && map.Destruction == null)
        {
            _map = null;
            LastDraws = LastInstances = 0;
            return;
        }
        var t0 = Stopwatch.GetTimestamp();
        if (!_ready && !Init())
            return;
        if (!ReferenceEquals(map, _map))
            Rebuild(map);
        else if (map.Destruction is { } destruction && destruction.Version != _destructionVersion)
            RefreshDestroyed(map, destruction);
        var swinging = Swing(map, dt);
        if (swinging || map.BuildingStateVersion != _dynVersion)
            WriteDynamic(map);

        ExtractPlanes(gfx.ViewProj);
        var vp = gfx.ViewProj;
        var cam = gfx.CameraPosition;
        var fog = gfx.FogColor;
        var fogRange = gfx.FogRange;
        _program.Use();
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        Native.glUniform3f(_locFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_locFogRange, fogRange.X, fogRange.Y);
        _vao.Bind();
        var draws = 0;
        var instances = 0;

        // Shell: one run per visible building, neighbours merged.
        Native.glBindBuffer(GL_ARRAY_BUFFER, _staticVbo);
        int runStart = -1, runCount = 0;
        foreach (var r in _runs)
        {
            var visible = r.Count > 0 && InFrustum(r.Min, r.Max);
            if (visible && runStart >= 0 && runStart + runCount == r.Start)
            {
                runCount += r.Count;
                continue;
            }
            if (runStart >= 0)
            {
                DrawRun(runStart, runCount);
                draws++;
                instances += runCount;
                runStart = -1;
            }
            if (visible)
                (runStart, runCount) = (r.Start, r.Count);
        }
        if (runStart >= 0)
        {
            DrawRun(runStart, runCount);
            draws++;
            instances += runCount;
        }

        // Doors (opaque), then glass (blended, no depth writes, so what's behind it shows through).
        if (_doorCount + _glassCount > 0)
        {
            Native.glBindBuffer(GL_ARRAY_BUFFER, _dynVbo);
            if (_doorCount > 0)
            {
                DrawRun(0, _doorCount);
                draws++;
                instances += _doorCount;
            }
            if (_glassCount > 0)
            {
                Enable(GlCapability.Blend);
                DepthMask(false);
                DrawRun(_doorCount, _glassCount);
                DepthMask(true);
                Native.glDisable(GlCapability.Blend);
                draws++;
                instances += _glassCount;
            }
        }
        gfx.Rebind();
        LastDraws = draws;
        LastInstances = instances;

        _cpuMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _statFrames++;
        _statDraws += draws;
        _statInstances += instances;
        if (_statStart < 0)
            _statStart = now;
        if (now - _statStart >= 5)
        {
            Console.WriteLine($"[buildings] {map.Name}: {map.PlacedBuildings.Count} buildings, {Boxes} shell boxes, {map.BuildingDoors.Count} doors, {map.BuildingWindows.Count} windows"
                              + (map.Destruction is { } dd ? $", {dd.Count} destructible chunks ({dd.Structures.Sum(st => st.BrokenCount)} broken, {_destructionUploads} run uploads)" : "") + "; "
                              + $"avg {_statInstances / (double)_statFrames:0} boxes drawn in {_statDraws / (double)_statFrames:0.0} draw calls, CPU {_cpuMs / _statFrames:0.000} ms/frame, "
                              + $"GL error 0x{(uint)Native.glGetError():X}");
            _statStart = now;
            _statFrames = _statDraws = _statInstances = 0;
            _cpuMs = 0;
        }
    }

    private void DrawRun(int start, int count)
    {
        var offset = (nint)start * FloatsPerInstance * sizeof(float);
        const int stride = FloatsPerInstance * sizeof(float);
        for (uint a = 0; a < 4; a++)
            Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, stride, (void*)(offset + a * 4 * sizeof(float)));
        Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36, count);
    }

    // ---- Instance data ------------------------------------------------------------------------

    /// <summary>
    /// The shell instances of a map, one run per building, then one per destructible block (no GL: the self-test checks it).
    /// A broken destructible chunk is written with no size (nothing drawn, its slot kept, see <see cref="WriteRun"/>).
    /// </summary>
    public static (float[] Data, (int Start, int Count, Vector3 Min, Vector3 Max)[] Runs) BuildStatic(Map map)
    {
        var blocks = map.Destruction?.Structures.Where(st => st.Building < 0).ToArray() ?? Array.Empty<DestructibleStructure>();
        var total = map.PlacedBuildings.Sum(b => b.ShellCount) + blocks.Sum(st => st.SolidCount);
        var data = new float[Math.Max(1, total) * FloatsPerInstance];
        var runs = new (int, int, Vector3, Vector3)[map.PlacedBuildings.Count + blocks.Length];
        var n = 0;
        for (var bi = 0; bi < runs.Length; bi++)
        {
            var (first, count) = bi < map.PlacedBuildings.Count
                ? (map.PlacedBuildings[bi].ShellStart, map.PlacedBuildings[bi].ShellCount)
                : (blocks[bi - map.PlacedBuildings.Count].SolidStart, blocks[bi - map.PlacedBuildings.Count].SolidCount);
            var start = n;
            n = WriteRun(map, data, n, first, count, out var min, out var max);
            runs[bi] = (start, n - start, min, max);
        }
        return (data, runs);
    }

    /// <summary>Writes the instances of Solid[first .. first + count) from slot <paramref name="n"/>; returns the next free slot.</summary>
    private static int WriteRun(Map map, float[] data, int n, int first, int count, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue);
        max = new Vector3(float.MinValue);
        var d = map.Destruction;
        for (var k = first; k < first + count; k++)
        {
            if (!map.BuildingLooks.TryGetValue(k, out var look))
                continue;
            var gone = d != null && d.IsChunk(k, out var c) && d.Broken[c];
            Write(data, n++, gone ? look.Box with { Half = Vector3.Zero } : look.Box, 0, Vector2.Zero, look.Color, 1f, look.Layer);
            min = Vector3.Min(min, look.Box.Min);
            max = Vector3.Max(max, look.Box.Max);
        }
        return n;
    }

    private static void Write(float[] d, int i, Box b, float yaw, Vector2 pivot, Vector3 color, float alpha, int layer)
    {
        var o = i * FloatsPerInstance;
        d[o] = b.Center.X;
        d[o + 1] = b.Center.Y;
        d[o + 2] = b.Center.Z;
        d[o + 3] = yaw;
        d[o + 4] = b.Half.X;
        d[o + 5] = b.Half.Y;
        d[o + 6] = b.Half.Z;
        d[o + 7] = layer;
        d[o + 8] = color.X;
        d[o + 9] = color.Y;
        d[o + 10] = color.Z;
        d[o + 11] = alpha;
        d[o + 12] = pivot.X;
        d[o + 13] = pivot.Y;
        d[o + 14] = 0;
        d[o + 15] = 0;
    }

    /// <summary>The turn (radians, the shader's convention) that takes a door from shut to fully open.</summary>
    public static float OpenAngle(PlannedDoor d)
    {
        // The shader turns (x, z) to (x cos + z sin, -x sin + z cos): +90 degrees takes (x, z) to (z, -x).
        var plus = new Vector2(d.ClosedDir.Y, -d.ClosedDir.X);
        return Vector2.Dot(plus, d.OpenDir) >= 0 ? MathF.PI / 2 : -MathF.PI / 2;
    }

    /// <summary>Eases every door's swing towards open or shut. True while any is moving.</summary>
    public static bool Swing(Map map, float dt)
    {
        var moving = false;
        var step = dt / SwingSeconds;
        foreach (var d in map.BuildingDoors)
        {
            var target = d.Open ? 1f : 0f;
            if (d.Swing == target)
                continue;
            d.Swing = d.Swing < target ? MathF.Min(target, d.Swing + step) : MathF.Max(target, d.Swing - step);
            moving = true;
        }
        return moving;
    }

    private void WriteDynamic(Map map)
    {
        var need = (map.BuildingDoors.Count + map.BuildingWindows.Count) * FloatsPerInstance;
        if (_dyn.Length < need)
            _dyn = new float[Math.Max(need, FloatsPerInstance)];
        var n = 0;
        foreach (var d in map.BuildingDoors)
        {
            if (d.Broken)
                continue;
            var s = d.Swing * d.Swing * (3 - 2 * d.Swing);
            Write(_dyn, n++, d.Plan.Closed, OpenAngle(d.Plan) * s, new Vector2(d.Plan.Hinge.X, d.Plan.Hinge.Z), d.Plan.Color, 1f,
                d.Plan.Material == DoorMaterial.Metal ? (int)Material.Metal : d.Plan.Material == DoorMaterial.Wood ? (int)Material.Wood : -1);
        }
        _doorCount = n;
        foreach (var w in map.BuildingWindows)
            if (!w.Broken)
                Write(_dyn, n++, w.Plan.Glass, 0, Vector2.Zero, Map.GlassColor, 0.32f, -1);
        _glassCount = n - _doorCount;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _dynVbo);
        fixed (float* p = _dyn)
            Native.glBufferData(GL_ARRAY_BUFFER, Math.Max(n, 1) * FloatsPerInstance * sizeof(float), p, GL_DYNAMIC_DRAW);
        _dynVersion = map.BuildingStateVersion;
    }

    /// <summary>Re-uploads the runs of the structures whose chunks broke or came back since the last frame (only those).</summary>
    private void RefreshDestroyed(Map map, MapDestruction d)
    {
        _destructionVersion = d.Version;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _staticVbo);
        var blocks = 0;
        foreach (var st in d.Structures)
        {
            var run = st.Building >= 0 ? st.Building : map.PlacedBuildings.Count + blocks++;
            if (run >= _runs.Length || run >= _runVersions.Length || _runVersions[run] == st.Version)
                continue;
            _runVersions[run] = st.Version;
            var (start, count, _, _) = _runs[run];
            WriteRun(map, _static, start, st.SolidStart, st.SolidCount, out _, out _);
            fixed (float* p = _static)
                Native.glBufferSubData(GL_ARRAY_BUFFER, (nint)start * FloatsPerInstance * sizeof(float), (nint)count * FloatsPerInstance * sizeof(float), p + start * FloatsPerInstance);
            _destructionUploads++;
        }
    }

    private void Rebuild(Map map)
    {
        _map = map;
        (_static, _runs) = BuildStatic(map);
        _runVersions = new int[_runs.Length];
        if (map.Destruction is { } dv)
        {
            _destructionVersion = dv.Version;
            var blocks = 0;
            foreach (var st in dv.Structures)
            {
                var run = st.Building >= 0 ? st.Building : map.PlacedBuildings.Count + blocks++;
                if (run < _runVersions.Length)
                    _runVersions[run] = st.Version;
            }
        }
        Boxes = _runs.Sum(r => r.Count);
        Native.glBindBuffer(GL_ARRAY_BUFFER, _staticVbo);
        fixed (float* p = _static)
            Native.glBufferData(GL_ARRAY_BUFFER, _static.Length * sizeof(float), p, GL_STATIC_DRAW);
        _dynVersion = -1;
        Console.WriteLine($"[buildings] {map.Name}: uploaded {Boxes} shell boxes of {map.PlacedBuildings.Count} buildings ({_static.Length * sizeof(float) / 1024} KB), "
                          + $"{map.BuildingDoors.Count} doors, {map.BuildingWindows.Count} windows");
    }

    // ---- Culling ------------------------------------------------------------------------------

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
            Native.glUniform1i(Uniform(_program, "uTex\0"u8), 0); // the box renderer's texture array, bound to unit 0
            _locViewProj = Uniform(_program, "uViewProj\0"u8);
            _locCam = Uniform(_program, "uCam\0"u8);
            _locFog = Uniform(_program, "uFog\0"u8);
            _locFogRange = Uniform(_program, "uFogRange\0"u8);

            _vao = GenVertexArray();
            _vao.Bind();
            _cubeVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cubeVbo);
            var cube = Cube();
            fixed (float* p = cube)
                Native.glBufferData(GL_ARRAY_BUFFER, cube.Length * sizeof(float), p, GL_STATIC_DRAW);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
            _staticVbo = GenBuffer();
            _dynVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _staticVbo);
            const int stride = FloatsPerInstance * sizeof(float);
            for (uint a = 0; a < 4; a++)
            {
                Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, stride, (void*)(a * 4 * sizeof(float)));
                EnableVertexAttribArray(2 + a);
                Native.glVertexAttribDivisor(2 + a, 1);
            }
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[buildings] GL error 0x{err:X} while setting up (continuing)");
            Console.WriteLine("[buildings] renderer ready (instanced boxes)");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[buildings] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
    }

    /// <summary>A unit cube centred on the origin: 36 vertices of position + normal.</summary>
    private static float[] Cube()
    {
        (Vector3 n, Vector3 u, Vector3 v)[] faces =
        [
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX),
            (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
        ];
        var data = new List<float>(36 * 6);
        foreach (var (n, u, v) in faces)
        {
            var c = n * 0.5f;
            Vector3 a = c - u * 0.5f - v * 0.5f, b = c + u * 0.5f - v * 0.5f, d = c + u * 0.5f + v * 0.5f, e = c - u * 0.5f + v * 0.5f;
            foreach (var p in new[] { a, b, d, a, d, e })
                data.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z]);
        }
        return data.ToArray();
    }
}
