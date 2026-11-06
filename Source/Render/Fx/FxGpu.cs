using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The particle pool's GL side (docs/render/MATERIALS.md, "Effects"). On the 4.6 path: a storage buffer of
/// <see cref="FxParticle"/>s, a persistent ring the frame's births are copied through, one compute pass that writes them in, one that
/// steps every slot and lists the living, and one indirect draw of the list: the CPU never sees a particle after its birth.
/// On the 4.1 path: <see cref="Fx.StepCpu"/>'s array, streamed to one instanced draw. Both draw through the same vertex and fragment
/// code: the only difference is where a particle comes from (FX_PULL).
/// </summary>
public static unsafe class FxGpu
{
    private const uint KindsBinding = 8;
    private const int PoolBinding = 1, AliveBinding = 2, CmdBinding = 3, BirthBinding = 4;

    /// <summary>Living particles last frame are the GPU's business; this is what the F9 window can say cheaply: the pool's size.</summary>
    public static int PoolSize { get; private set; }

    private const string Block = """
        layout(std140) uniform FxKinds
        {
            vec4 uKind[80]; // five a kind (FxKinds.Block): c0 + mid, c1 + alpha, c2 + glow, sizes and fades, gravity drag turbulence stretch
        };
        """;

    private const string EmitSrc = """
        #version 410 core
        layout(local_size_x = 64) in;
        struct P { vec4 a; vec4 b; vec3 c; uint tint; };
        layout(std430, binding = 1) buffer Pool { P pool[]; };
        layout(std430, binding = 4) readonly buffer Births { P births[]; };
        uniform uint uFirst, uCount, uCursor, uCap;
        void main()
        {
            uint i = gl_GlobalInvocationID.x;
            if (i < uCount)
                pool[(uCursor + i) % uCap] = births[uFirst + i];
        }
        """;

    private static readonly string UpdateSrc = "#version 410 core\n" + Block + """

        layout(local_size_x = 256) in;
        struct P { vec4 a; vec4 b; vec3 c; uint tint; };
        layout(std430, binding = 1) buffer Pool { P pool[]; };
        layout(std430, binding = 2) writeonly buffer Alive { uint alive[]; };
        layout(std430, binding = 3) buffer Cmd { uint verts; uint instances; uint first; uint baseInstance; };
        uniform float uDt;
        uniform float uTime;
        uniform uint uCap;
        void main()
        {
            uint i = gl_GlobalInvocationID.x;
            if (i >= uCap)
                return;
            vec4 a = pool[i].a;
            vec4 b = pool[i].b;
            if (b.w <= 0.0)
                return;
            a.w += uDt;
            if (a.w >= b.w)
            {
                pool[i].b.w = 0.0;
                return;
            }
            int k = int(pool[i].c.z + 0.5) * 5;
            vec4 m = uKind[k + 4]; // gravity, drag, turbulence
            float seed = pool[i].c.y * 6.2832;
            vec3 v = b.xyz * max(0.0, 1.0 - m.y * uDt);
            v.y -= m.x * uDt;
            if (m.z > 0.0)
                v += vec3(sin(uTime * 2.3 + seed + a.y * 0.7), cos(uTime * 1.7 + seed * 1.3 + a.x * 0.7), sin(uTime * 2.9 + seed * 0.7 + a.z * 0.7)) * m.z * uDt;
            a.xyz += v * uDt;
            pool[i].a = a;
            pool[i].b.xyz = v;
            alive[atomicAdd(instances, 1u)] = i;
        }
        """;

    private static readonly string VertexSrc = "#version 410 core\n#define FX_PULL 0\n" + Block + """

        #if GL46 && FX_PULL
        struct P { vec4 a; vec4 b; vec3 c; uint tint; };
        layout(std430, binding = 1) readonly buffer Pool { P pool[]; };
        layout(std430, binding = 2) readonly buffer Alive { uint alive[]; };
        #else
        layout(location = 0) in vec4 iA;
        layout(location = 1) in vec4 iB;
        layout(location = 2) in vec3 iC;
        layout(location = 3) in vec4 iTint;
        #endif
        uniform mat4 uViewProj;
        uniform vec3 uOrigin;
        uniform vec3 uCam;
        uniform vec3 uRight;
        uniform vec3 uUp;
        uniform vec3 uAmbient;   // the sky's light on smoke
        uniform vec3 uFog;
        uniform vec2 uFogRange;
        out vec2 vUv;
        out vec4 vCol;           // colour, how much it covers
        flat out float vGlow;
        flat out float vSeed;

        void main()
        {
            #if GL46 && FX_PULL
            P pa = pool[alive[gl_InstanceID]];
            vec4 a = pa.a;
            vec4 b = pa.b;
            vec3 c = pa.c;
            vec4 tint = unpackUnorm4x8(pa.tint);
            #else
            vec4 a = iA;
            vec4 b = iB;
            vec3 c = iC;
            vec4 tint = iTint;
            #endif
            int k = int(c.z + 0.5) * 5;
            float t = clamp(a.w / b.w, 0.0, 1.0);
            vec4 k0 = uKind[k], k1 = uKind[k + 1], k2 = uKind[k + 2], k3 = uKind[k + 3], k4 = uKind[k + 4];
            vec3 col = t < k0.w ? mix(k0.rgb, k1.rgb, t / max(k0.w, 1e-3)) : mix(k1.rgb, k2.rgb, (t - k0.w) / max(1.0 - k0.w, 1e-3));
            float size = c.x * mix(k3.x, k3.y, sqrt(t));
            float alpha = k1.w * tint.a * min(k3.z > 0.0 ? t / k3.z : 1.0, 1.0) * pow(1.0 - t, k3.w);
            float stretch = mod(k4.w, 10.0);
            float lit = floor(k4.w / 10.0);
            vec2 q = vec2(gl_VertexID & 1, (gl_VertexID >> 1) & 1) * 2.0 - 1.0;
            vUv = q;
            vec3 world = a.xyz;
            vec3 toCam = uCam - world;
            float dist = length(toCam);
            vec3 o;
            float speed = length(b.xyz);
            if (stretch > 0.0 && speed > 0.5)
            {
                // A streak: long along its speed as the camera sees it.
                vec3 along = b.xyz / speed;
                vec3 across = cross(along, toCam / max(dist, 1e-3));
                float l = length(across);
                across = l > 1e-3 ? across / l : uRight;
                o = across * (q.x * size) + along * (q.y * (size + speed * stretch));
            }
            else
            {
                float ang = c.y * 6.2832 + a.w * (c.y - 0.5) * 1.6;
                float cs = cos(ang), sn = sin(ang);
                vec2 r = vec2(q.x * cs - q.y * sn, q.x * sn + q.y * cs) * size;
                o = uRight * r.x + uUp * r.y;
            }
            alpha *= clamp((dist - 0.25) / max(size, 0.5), 0.0, 1.0); // (fades out rather than filling the screen as you pass through it)
            vec3 shade = col * tint.rgb * mix(vec3(1.0), uAmbient, lit);
            float fog = uFogRange.y > uFogRange.x ? clamp((dist - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0) : 0.0;
            vCol = vec4(mix(shade, uFog, fog * (1.0 - k2.w)), alpha * (1.0 - fog * fog * k2.w));
            vGlow = k2.w;
            vSeed = c.y * 40.0;
            gl_Position = uViewProj * vec4(world + o - uOrigin, 1.0);
        }
        """;

    private const string FragmentSrc = """
        #version 410 core
        in vec2 vUv;
        in vec4 vCol;
        flat in float vGlow;
        flat in float vSeed;
        layout(location = 0, index = 0) out vec4 FragColor; // premultiplied light; alpha marks the bloom (PostFx.cs)
        layout(location = 0, index = 1) out vec4 Cover;     // alpha: how much of what is behind it is hidden
        void main()
        {
            float r2 = dot(vUv, vUv);
            if (r2 >= 1.0)
                discard;
            float d = 1.0 - r2;
            float lump = 0.72 + 0.28 * sin(vUv.x * 4.0 + vSeed) * sin(vUv.y * 4.0 + vSeed * 1.7);
            float a = d * d * vCol.a * lump;
            FragColor = vec4(vCol.rgb * a, a * vGlow);
            Cover = vec4(0.0, 0.0, 0.0, a * (1.0 - vGlow));
        }
        """;

    private static bool _ready, _broken, _gpu;
    private static GlProgram _draw;
    private static GlCompute? _emit, _update;
    private static GlVertexArray _vao;
    private static GlBuffer _kinds, _pool, _alive, _cmd, _cpuVbo;
    private static StreamRing? _births;
    private static int _cap, _cursor;
    private static float _busy; // seconds until the longest life still running is over
    private static bool _idle;
    private static int _lViewProj, _lOrigin, _lCam, _lRight, _lUp, _lAmbient, _lFog, _lFogRange;
    private static int _uFirst, _uCount, _uCursor, _uCapE, _uDt, _uTime, _uCapU;

    private const uint GlSrc1Alpha = 0x88FB; // GL_ONE_MINUS_SRC1_ALPHA (dual-source blending, core since GL 3.3)

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        try
        {
            _gpu = RenderCaps.GpuParticles;
            var vsrc = _gpu ? VertexSrc.Replace("#define FX_PULL 0", "#define FX_PULL 1") : VertexSrc;
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vsrc)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
            _draw = CreateProgram();
            Native.glAttachShader(_draw, vs);
            Native.glAttachShader(_draw, fs);
            Native.glLinkProgram(_draw);
            GlDebug.Label(GL_PROGRAM, _draw.Id, "Fx.draw");
            int linked;
            Native.glGetProgramiv(_draw, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("the particle shader did not link");
            vs.Dispose();
            fs.Dispose();
            _draw.Use();
            _lViewProj = Loc(_draw, "uViewProj\0"u8);
            _lOrigin = Loc(_draw, "uOrigin\0"u8);
            _lCam = Loc(_draw, "uCam\0"u8);
            _lRight = Loc(_draw, "uRight\0"u8);
            _lUp = Loc(_draw, "uUp\0"u8);
            _lAmbient = Loc(_draw, "uAmbient\0"u8);
            _lFog = Loc(_draw, "uFog\0"u8);
            _lFogRange = Loc(_draw, "uFogRange\0"u8);
            var block = FxKinds.Block();
            _kinds = GenBuffer();
            Native.glBindBuffer(GL_UNIFORM_BUFFER, _kinds);
            fixed (float* p = block)
                Native.glBufferData(GL_UNIFORM_BUFFER, block.Length * sizeof(float), p, GL_STATIC_DRAW);
            Native.glBindBufferBase(GL_UNIFORM_BUFFER, KindsBinding, _kinds);
            Native.glBindBuffer(GL_UNIFORM_BUFFER, default);
            BindKinds(_draw);
            _vao = GenVertexArray();
            _vao.Bind();
            GlDebug.Label(GL_VERTEX_ARRAY, _vao.Id, "Fx.draw");
            if (_gpu)
                MakePool();
            else
                MakeStream();
            Console.WriteLine($"[fx] particles: {(_gpu ? "GPU pool, compute and indirect draw" : "fallback CPU pool")} ({Fx.Capacity(Fx.Level, _gpu)} at this setting)");
            _ready = true;
            Fx.Gpu = _gpu;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[fx] particles off: {ex.Message}");
            _broken = true;
            Fx.On = false;
            return false;
        }
    }

    private static int Loc(GlProgram p, ReadOnlySpan<byte> name)
    {
        fixed (byte* n = name)
            return Native.glGetUniformLocation(p, n);
    }

    private static void BindKinds(GlProgram p)
    {
        fixed (byte* n = "FxKinds\0"u8)
        {
            var index = Native.glGetUniformBlockIndex(p, n);
            if (index != GL_INVALID_INDEX)
                Native.glUniformBlockBinding(p, index, KindsBinding);
        }
    }

    private static void MakePool()
    {
        _emit = GlCompute.Create(EmitSrc, "Fx.emit") ?? throw new InvalidOperationException("the birth pass did not build");
        _update = GlCompute.Create(UpdateSrc, "Fx.update") ?? throw new InvalidOperationException("the step pass did not build");
        BindKinds(_update.Program);
        _uFirst = Loc(_emit.Program, "uFirst\0"u8);
        _uCount = Loc(_emit.Program, "uCount\0"u8);
        _uCursor = Loc(_emit.Program, "uCursor\0"u8);
        _uCapE = Loc(_emit.Program, "uCap\0"u8);
        _uDt = Loc(_update.Program, "uDt\0"u8);
        _uTime = Loc(_update.Program, "uTime\0"u8);
        _uCapU = Loc(_update.Program, "uCap\0"u8);
        _births = new StreamRing(sizeof(FxParticle), Fx.MaxBirths, "Fx.births");
        _pool = GenBuffer();
        _alive = GenBuffer();
        _cmd = GenBuffer();
        GlDebug.Label(GL_BUFFER, _pool.Id, "Fx.pool");
        GlDebug.Label(GL_BUFFER, _alive.Id, "Fx.alive");
        GlDebug.Label(GL_BUFFER, _cmd.Id, "Fx.cmd");
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, _cmd);
        var cmd = stackalloc uint[4] { 4, 0, 0, 0 };
        Native.glBufferData(GL_SHADER_STORAGE_BUFFER, 16, cmd, GL_DYNAMIC_COPY);
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, default);
        Size(Fx.Capacity(Fx.Level, true));
    }

    /// <summary>Makes the pool for a setting (at most when the setting changes: a few MB, cleared).</summary>
    private static void Size(int cap)
    {
        _cap = cap;
        PoolSize = cap;
        _cursor = 0;
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, _pool);
        Native.glBufferData(GL_SHADER_STORAGE_BUFFER, (nint)cap * sizeof(FxParticle), null, GL_DYNAMIC_COPY);
        var zero = 0u;
        Native.glClearBufferData(GL_SHADER_STORAGE_BUFFER, GL_R32UI, GL_RED_INTEGER, GL_UNSIGNED_INT, &zero);
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, _alive);
        Native.glBufferData(GL_SHADER_STORAGE_BUFFER, (nint)cap * sizeof(uint), null, GL_DYNAMIC_COPY);
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, default);
    }

    private static void MakeStream()
    {
        _cpuVbo = GenBuffer();
        GlDebug.Label(GL_BUFFER, _cpuVbo.Id, "Fx.stream");
        Native.glBindBuffer(GL_ARRAY_BUFFER, _cpuVbo);
        Native.glBufferData(GL_ARRAY_BUFFER, Fx.CpuDraw.Length * sizeof(FxParticle), null, GL_STREAM_DRAW);
        var s = sizeof(FxParticle);
        Native.glVertexAttribPointer(0, 4, GL_FLOAT, false, s, (void*)0);
        Native.glVertexAttribPointer(1, 4, GL_FLOAT, false, s, (void*)16);
        Native.glVertexAttribPointer(2, 3, GL_FLOAT, false, s, (void*)32);
        Native.glVertexAttribPointer(3, 4, GL_UNSIGNED_BYTE, true, s, (void*)44);
        for (uint a = 0; a < 4; a++)
        {
            EnableVertexAttribArray(a);
            Native.glVertexAttribDivisor(a, 1);
        }
    }

    /// <summary>The frame's step: the births in, everything on. Before the draw; <paramref name="dt"/> is this frame's seconds.</summary>
    public static void Step(float dt, float time)
    {
        if (!Init())
            return;
        dt = Math.Clamp(dt, 0f, 0.1f);
        if (!_gpu)
        {
            Fx.StepCpu(dt);
            return;
        }
        var cap = Fx.Capacity(Fx.Level, true);
        if (cap != _cap)
            Size(cap);
        var births = Fx.BirthCount;
        Fx.BirthCount = 0;
        // Nothing alive and nothing new: no dispatch and no draw. Effects cost nothing in a quiet frame, whatever the pool's size.
        _busy = MathF.Max(_busy - dt, Fx.LongestBirth);
        Fx.LongestBirth = 0;
        if (births == 0 && _busy <= 0 && _idle)
            return;
        _idle = births == 0 && _busy <= 0;
        Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, PoolBinding, _pool);
        Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, AliveBinding, _alive);
        Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, CmdBinding, _cmd);
        if (births > 0)
        {
            int first;
            fixed (FxParticle* p = Fx.Births)
                first = _births!.Write((float*)p, births, sizeof(FxParticle) / sizeof(float));
            Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, BirthBinding, _births.Buffer);
            _emit!.Program.Use();
            Native.glUniform1ui(_uFirst, (uint)first);
            Native.glUniform1ui(_uCount, (uint)births);
            Native.glUniform1ui(_uCursor, (uint)_cursor);
            Native.glUniform1ui(_uCapE, (uint)_cap);
            Native.glDispatchCompute((uint)((births + 63) / 64), 1, 1);
            _cursor = (_cursor + births) % _cap;
            Native.glMemoryBarrier((GlBitfield)GL_SHADER_STORAGE_BARRIER_BIT);
        }
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, _cmd);
        var zero = 0u;
        Native.glBufferSubData(GL_SHADER_STORAGE_BUFFER, 4, 4, &zero);
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, default);
        _update!.Program.Use();
        Native.glUniform1f(_uDt, dt);
        Native.glUniform1f(_uTime, time % 4096f);
        Native.glUniform1ui(_uCapU, (uint)_cap);
        Native.glDispatchCompute((uint)((_cap + 255) / 256), 1, 1);
        Native.glMemoryBarrier((GlBitfield)(GL_SHADER_STORAGE_BARRIER_BIT | GL_COMMAND_BARRIER_BIT));
    }

    /// <summary>One draw of everything alive, in the blended pass (the depth buffer is read, never written).</summary>
    public static void Draw(Renderer gfx, Vector3 ambient)
    {
        if (!_ready)
            return;
        if (_gpu ? _idle : Fx.CpuAlive == 0)
        {
            gfx.AfterFx(); // (the step's last pass left its own program bound)
            return;
        }
        _draw.Use();
        var vp = gfx.ViewProjRelative;
        Native.glUniformMatrix4fv(_lViewProj, 1, false, &vp.M11);
        var o = gfx.Origin;
        Native.glUniform3f(_lOrigin, o.X, o.Y, o.Z);
        var cam = gfx.CameraPosition;
        Native.glUniform3f(_lCam, cam.X, cam.Y, cam.Z);
        var v = gfx.View;
        Native.glUniform3f(_lRight, v.M11, v.M21, v.M31);
        Native.glUniform3f(_lUp, v.M12, v.M22, v.M32);
        Native.glUniform3f(_lAmbient, ambient.X, ambient.Y, ambient.Z);
        var fog = gfx.FogColor;
        Native.glUniform3f(_lFog, fog.X, fog.Y, fog.Z);
        Native.glUniform2f(_lFogRange, gfx.FogRange.X, gfx.FogRange.Y);
        _vao.Bind();
        Native.glEnable(GlCapability.Blend);
        Native.glBlendFuncSeparate(GL_ONE, GlSrc1Alpha, GL_ZERO, GL_ONE_MINUS_SRC_ALPHA);
        Native.glDepthMask(false);
        if (_gpu)
        {
            Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, PoolBinding, _pool);
            Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, AliveBinding, _alive);
            Native.glBindBuffer(GL_DRAW_INDIRECT_BUFFER, _cmd);
            Native.glDrawArraysIndirect(GL_TRIANGLE_STRIP, null);
            Native.glBindBuffer(GL_DRAW_INDIRECT_BUFFER, default);
        }
        else
        {
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cpuVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, Fx.CpuDraw.Length * sizeof(FxParticle), null, GL_STREAM_DRAW);
            fixed (FxParticle* p = Fx.CpuDraw)
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, Fx.CpuAlive * sizeof(FxParticle), p);
            Native.glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, Fx.CpuAlive);
        }
        PerfCounters.Draw(4, _gpu ? 1 : Fx.CpuAlive);
        Native.glDepthMask(true);
        Native.glDisable(GlCapability.Blend);
        gfx.AfterFx();
    }
}
