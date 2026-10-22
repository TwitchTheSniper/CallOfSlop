using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws the frenzy visuals (<see cref="FrenzyFx"/>): one unit cube instanced per streak (sparks, trails, weapon glows),
/// each stretched along its own axis in the vertex shader, unlit and additive (glow adds light, never darkens, needs no
/// sorting), depth-tested but not depth-written. Two passes per frame, each one glDrawArraysInstanced from a streamed
/// buffer (orphaned each upload, no stall): the world pass (particles + other players' gun glow) and the viewmodel pass
/// (your gun, camera space). GPU time of both from timestamp queries (<see cref="GpuStampRing"/>), CPU time from
/// <see cref="FrenzyFx.TakeCpuMs"/>; a "[frenzyfx]" log line every few seconds while anything is drawn.
/// Any setup failure turns it off cleanly (the tracers still take the class colours).
/// </summary>
public sealed unsafe class FrenzyFxRenderer
{
    private const string VertexSrc = """
        #version 330 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 iCenterWidth; // centre, width
        layout(location = 3) in vec4 iAxisAlpha;   // full-length axis, alpha
        layout(location = 4) in vec4 iColor;
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        out vec3 vColor;
        out vec3 vNormal;
        out vec3 vView;
        void main()
        {
            vec3 ax = iAxisAlpha.xyz;
            float len = length(ax);
            vec3 z = len > 1e-5 ? ax / len : vec3(0.0, 0.0, 1.0);
            vec3 r = abs(z.y) < 0.95 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
            vec3 x = normalize(cross(r, z));
            vec3 y = cross(z, x);
            float w = iCenterWidth.w;
            vec3 p = iCenterWidth.xyz + x * (aPos.x * w) + y * (aPos.y * w) + z * (aPos.z * max(len, w));
            vNormal = x * aNormal.x + y * aNormal.y + z * aNormal.z;
            vView = uCam - p;
            vColor = iColor.rgb * iAxisAlpha.w;
            gl_Position = uViewProj * vec4(p, 1.0);
        }
        """;

    // Additive glow: brightest where the face looks at the camera, so a box reads as a soft streak, not a crate.
    private const string FragmentSrc = """
        #version 330 core
        in vec3 vColor;
        in vec3 vNormal;
        in vec3 vView;
        out vec4 FragColor;
        void main()
        {
            float facing = abs(dot(normalize(vNormal), normalize(vView)));
            FragColor = vec4(vColor * (0.3 + 0.7 * facing * facing), 1.0);
        }
        """;

    /// <summary>Instances per pass at most: the particle pool plus the per-frame glows of a full lobby.</summary>
    public const int MaxInstances = FrenzyParticles.Capacity + 256;

    public const double LogInterval = 5;

    private bool _ready, _broken;
    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _cubeVbo, _instVbo;
    private int _locViewProj, _locCam;
    private readonly float[] _data = new float[MaxInstances * FrenzyParticles.FloatsPerInstance];
    private GpuStampRing? _timer;
    private const int QueryRing = 4, PassWorld = 0, PassView = 1;
    private bool _framed, _viewDone;

    /// <summary>Averages over a window of frames (the log line, and one the smoke test resets and reads).</summary>
    public struct Stats
    {
        public double Start;
        public int Frames, GpuSamples;
        public long WorldInstances, ViewInstances, Draws;
        public double CpuMs, GpuMs;

        public readonly double AvgWorld => Frames > 0 ? (double)WorldInstances / Frames : 0;
        public readonly double AvgView => Frames > 0 ? (double)ViewInstances / Frames : 0;
        public readonly double AvgDraws => Frames > 0 ? (double)Draws / Frames : 0;
        public readonly double AvgCpuMs => Frames > 0 ? CpuMs / Frames : 0;

        /// <summary>GPU ms per frame (both passes), -1 when the timer gave nothing.</summary>
        public readonly double AvgGpuMs => GpuSamples > 0 ? GpuMs / GpuSamples : -1;
    }

    private Stats _log;
    private double _gpuFrameMs;

    /// <summary>Stats since the last <see cref="ResetWindow"/>.</summary>
    public Stats Window;

    public void ResetWindow(double now) => Window = new Stats { Start = now };

    /// <summary>Is the renderer up (false: never set up yet, or turned off after a failure)?</summary>
    public bool Ready => _ready;

    /// <summary>
    /// The world pass, after the opaque scene and the tracers, with the world camera set on <paramref name="gfx"/>:
    /// builds the instances (<see cref="FrenzyFx.BuildWorld"/>) and draws them. Starts this frame's GPU timing.
    /// </summary>
    public void DrawWorld(Renderer gfx, Game game, double now)
    {
        if (_broken || (!_ready && !Init()))
            return;
        _timer?.BeginFrame();
        _framed = true;
        _viewDone = false;
        FrenzyFx.Camera = gfx.CameraPosition;
        var n = FrenzyFx.BuildWorld(game, now, _data);
        _log.WorldInstances += n;
        Window.WorldInstances += n;
        if (n > 0)
            Pass(gfx, gfx.ViewProj, gfx.CameraPosition, n);
        _timer?.EndPass(PassWorld);
    }

    /// <summary>
    /// The viewmodel pass (after the gun is drawn): your gun's glow, in camera space with the viewmodel projection
    /// <paramref name="vmProj"/>. Ends this frame's timing and stats.
    /// </summary>
    public void DrawViewModel(Renderer gfx, WeaponDef def, in Matrix4x4 gunPose, in Matrix4x4 vmProj, double now)
    {
        if (!_ready || !_framed || _viewDone)
            return;
        _viewDone = true;
        var n = FrenzyFx.BuildViewModel(def, gunPose, now, _data);
        _log.ViewInstances += n;
        Window.ViewInstances += n;
        if (n > 0)
            Pass(gfx, vmProj, Vector3.Zero, n);
        _timer?.EndPass(PassView);
    }

    /// <summary>Closes the frame (call once per rendered frame, after both passes or when there was no viewmodel).</summary>
    public void EndFrame(double now)
    {
        if (!_ready || !_framed)
            return;
        _framed = false;
        if (!_viewDone)
            _timer?.EndPass(PassView); // no viewmodel this frame: an empty pass, so each frame gives one sample
        _timer?.EndFrame();
        var cpu = FrenzyFx.TakeCpuMs();
        _log.CpuMs += cpu;
        Window.CpuMs += cpu;
        _log.Frames++;
        Window.Frames++;
        if (_log.Start <= 0)
            _log.Start = now;
        if (now - _log.Start >= LogInterval)
        {
            if (_log.WorldInstances + _log.ViewInstances > 0)
                Console.WriteLine($"[frenzyfx] {_log.AvgWorld:0} world + {_log.AvgView:0} viewmodel streaks/frame in {_log.AvgDraws:0.0} draw calls, "
                    + $"{FrenzyParticles.Count} particles live, mode {FrenzyFx.Mode}, CPU {_log.AvgCpuMs:0.000} ms, GPU {(_log.AvgGpuMs >= 0 ? $"{_log.AvgGpuMs:0.000} ms" : "n/a")}");
            _log = new Stats { Start = now };
        }
    }

    private void Pass(Renderer gfx, in Matrix4x4 viewProj, Vector3 cam, int n)
    {
        _program.Use();
        var vp = viewProj;
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        Native.glBufferData(GL_ARRAY_BUFFER, _data.Length * sizeof(float), null, GL_STREAM_DRAW); // orphan: no stall on last frame's copy
        fixed (float* p = _data)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, n * FrenzyParticles.FloatsPerInstance * sizeof(float), p);
        Enable(GlCapability.Blend);
        BlendFunc(GL_ONE, GL_ONE);
        DepthMask(false);
        Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36, n);
        DepthMask(true);
        BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        Native.glDisable(GlCapability.Blend);
        _log.Draws++;
        Window.Draws++;
        gfx.Rebind();
    }

    private void OnGpuSample(int pass, double ms)
    {
        // Both passes of a frame make one sample (the viewmodel pass is always stamped, empty when there was none).
        _gpuFrameMs += ms;
        if (pass == PassView)
        {
            _log.GpuMs += _gpuFrameMs;
            _log.GpuSamples++;
            Window.GpuMs += _gpuFrameMs;
            Window.GpuSamples++;
            _gpuFrameMs = 0;
        }
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
            fixed (byte* p = "uViewProj\0"u8)
                _locViewProj = Native.glGetUniformLocation(_program, p);
            fixed (byte* p = "uCam\0"u8)
                _locCam = Native.glGetUniformLocation(_program, p);

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
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, _data.Length * sizeof(float), null, GL_STREAM_DRAW);
            const int stride = FrenzyParticles.FloatsPerInstance * sizeof(float);
            for (uint a = 0; a < 3; a++)
            {
                Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, stride, (void*)(a * 4 * sizeof(float)));
                EnableVertexAttribArray(2 + a);
                Native.glVertexAttribDivisor(2 + a, 1);
            }
            if (GlGpuStamps.Create(GpuStampRing.QueryCount(2, QueryRing)) is { } stamps)
                _timer = new GpuStampRing(2, QueryRing, stamps, OnGpuSample);
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[frenzyfx] GL error 0x{err:X} while setting up (continuing)");
            FrenzyLooks.Load();
            Console.WriteLine($"[frenzyfx] renderer ready: {MaxInstances} streaks per pass, looks {FrenzyLooks.Source}, GPU timer {(_timer != null ? "on (timestamps)" : "unavailable")}");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[frenzyfx] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
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
