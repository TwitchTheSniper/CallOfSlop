using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Minimal OpenGL 3.3 renderer. Everything in the game is a coloured box: the room, the crates,
/// the players, the gun, the tracers and even the HUD.
/// Matrices are System.Numerics (row-vector, M = S * R * T) uploaded untransposed, which GLSL
/// reads as the transpose, so "uMVP * vec4(p, 1)" in the shader is correct.
/// </summary>
public sealed unsafe class Renderer
{
    private const string VertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        uniform mat4 uMVP;
        uniform mat4 uModel;
        out vec3 vWorld;
        out vec3 vNormal;
        void main()
        {
            vWorld = (uModel * vec4(aPos, 1.0)).xyz;
            vNormal = mat3(uModel) * aNormal;
            gl_Position = uMVP * vec4(aPos, 1.0);
        }
        """;

    // uMode: 0 = lit, 1 = lit + world-space texture (layer uLayer of uTex), 2 = unlit (HUD, tracers, muzzle flash)
    // Sun, ambient and fog come from Lighting.Glsl (shared with the foliage renderer).
    private static readonly string FragmentSrc = "#version 410 core\n" + Lighting.Glsl + """

        in vec3 vWorld;
        in vec3 vNormal;
        uniform vec4 uColor;
        uniform float uMode;
        uniform float uLayer;
        uniform sampler2DArray uTex;
        // Body blood (Source/Render/BodyBloodRender.cs): up to 8 marks on the body being drawn, each a sphere (centre,
        // radius) and (opacity, seed); uBloodN = 0 everywhere else. uBloodRot turns world offsets into the body's facing
        // so the ragged edges stay put on the body as it turns.
        uniform vec4 uBlood[16];
        uniform float uBloodN;
        uniform vec3 uBloodCol;
        uniform vec2 uBloodRot;
        out vec4 FragColor;

        float BloodHash(vec3 p)
        {
            p = fract(p * vec3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 33.33);
            return fract((p.x + p.y) * p.z);
        }

        vec3 Bleed(vec3 col)
        {
            float cover = 0.0, wet = 0.0;
            int n = int(uBloodN + 0.5);
            for (int i = 0; i < n; i++)
            {
                vec4 s = uBlood[i * 2];
                vec4 a = uBlood[i * 2 + 1];
                vec3 d = vWorld - s.xyz;
                float r = length(d) / s.w;
                if (r > 1.7)
                    continue;
                vec3 q = vec3(d.x * uBloodRot.x + d.z * uBloodRot.y, d.y, d.z * uBloodRot.x - d.x * uBloodRot.y); // into the body's facing
                vec3 cell = floor(q * 34.0) + a.y * 97.0;                // 3 cm blocks, the game's blocky look
                float h = BloodHash(cell);
                float blob = 1.0 - step(0.62 + 0.5 * h, r);              // ragged splat
                float drop = step(0.9, BloodHash(cell + 17.0)) * (1.0 - step(1.65, r)); // flecks around it
                float m = max(blob, drop) * a.x;
                cover = max(cover, m);
                wet = max(wet, (1.0 - clamp(r, 0.0, 1.0)) * a.x);
            }
            return mix(col, uBloodCol * (1.0 - 0.35 * wet), cover);     // darker where it pooled
        }

        void main()
        {
            vec3 col = uColor.rgb;
            if (uMode > 1.5)
            {
                FragColor = uColor;
                return;
            }
            if (uBloodN > 0.5)
                col = Bleed(col);

            vec3 n = normalize(vNormal);
            if (uMode > 0.5)
            {
                // Planar mapping in world space, one repeat per 2 m. Side faces keep "up" as v.
                vec2 uv = abs(n.y) > 0.5 ? vWorld.xz : (abs(n.x) > 0.5 ? vec2(vWorld.z, vWorld.y) : vec2(vWorld.x, vWorld.y));
                float t = texture(uTex, vec3(uv * 0.5, uLayer)).r;
                col *= t * 1.25;
            }

            // Hemisphere ambient + sun, plus a little rim light on the sides so box edges read.
            float rim = 0.12 * (1.0 - abs(n.y));
            col = ApplyLightAt(col, n, 0.0, vWorld) + col * rim * gLitAmb;
            // Distance haze towards the sky colour, so big maps fade out instead of ending abruptly.
            FragColor = vec4(ApplyFog(col, vWorld), uColor.a);
        }
        """;

    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _vbo;
    private int _locMvp, _locModel, _locColor, _locMode, _locLayer, _locTex, _locCam, _locFog, _locFogRange;
    private int _locBlood = -1, _locBloodN = -1, _locBloodCol = -1, _locBloodRot = -1;
    private bool _bloodOn;
    private uint _texture;

    private Matrix4x4 _viewProj = Matrix4x4.Identity;

    public void Init()
    {
        // --- Shaders -----------------------------------------------------------
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();

        _program = CreateProgram();
        Native.glAttachShader(_program, vs);
        Native.glAttachShader(_program, fs);
        Native.glLinkProgram(_program);
        GlDebug.Label(GL_PROGRAM, _program.Id, "Renderer");

        int linked;
        Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_program, 2048, &len, log);
            throw new InvalidOperationException(
                "Shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0))
            );
        }
        vs.Dispose();
        fs.Dispose();
        Lighting.Upload(_program);

        _locMvp = Uniform("uMVP\0"u8);
        _locModel = Uniform("uModel\0"u8);
        _locColor = Uniform("uColor\0"u8);
        _locMode = Uniform("uMode\0"u8);
        _locLayer = Uniform("uLayer\0"u8);
        _locTex = Uniform("uTex\0"u8);
        _locCam = Uniform("uCam\0"u8);
        _locFog = Uniform("uFog\0"u8);
        _locFogRange = Uniform("uFogRange\0"u8);
        _locBlood = Uniform("uBlood\0"u8);
        _locBloodN = Uniform("uBloodN\0"u8);
        _locBloodCol = Uniform("uBloodCol\0"u8);
        _locBloodRot = Uniform("uBloodRot\0"u8); // uBloodN starts at 0 (uniforms default to zero)
        CreateTextures();

        // --- Unit cube (36 verts, position + normal) ----------------------------
        var verts = BuildCube();
        _vao = GenVertexArray();
        _vao.Bind();
        _vbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (float* p = verts)
            Native.glBufferData(GL_ARRAY_BUFFER, verts.Length * sizeof(float), p, GL_STATIC_DRAW);

        const int stride = 6 * sizeof(float);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
        EnableVertexAttribArray(0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
        EnableVertexAttribArray(1);

        Enable(GlCapability.DepthTest);
        DepthFunc(GlFunc.LessEqual);
        BlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    }

    private int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    public void BeginFrame(int width, int height, Vector3 clear)
    {
        StreamRing.Frame++; // persistent instance rings move to their next segment (4.6 path)
        Viewport(0, 0, width, height);
        Native.glClearColor(clear.X, clear.Y, clear.Z, 1f);
        Clear(GlClearBit.Color | GlClearBit.Depth);
        _program.Use();
        _vao.Bind();
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _texture);
        Native.glUniform1i(_locTex, 0);
        Enable(GlCapability.DepthTest);
        Native.glDisable(GlCapability.Blend);
        _blendOn = false;
        LightGrid.Off(); // each frame starts with the classic light; the world pass turns a lit map's grid on (LightGrid.cs)
    }

    public void SetCamera(Matrix4x4 view, Matrix4x4 proj)
    {
        _viewProj = view * proj;
        View = view;
        Proj = proj;
        if (Matrix4x4.Invert(view, out var inv))
        {
            CameraPosition = new Vector3(inv.M41, inv.M42, inv.M43);
            Native.glUniform3f(_locCam, inv.M41, inv.M42, inv.M43);
        }
    }

    /// <summary>Distance fog towards <paramref name="color"/> between start and end metres (end &lt;= start: off).</summary>
    public void SetFog(Vector3 color, float start, float end)
    {
        FogColor = color;
        FogRange = new Vector2(start, end);
        Native.glUniform3f(_locFog, color.X, color.Y, color.Z);
        Native.glUniform2f(_locFogRange, start, end);
    }

    // ---- State other renderers (foliage) share ----------------------------------------------

    /// <summary>The current view * projection matrix (row-vector convention, as uploaded).</summary>
    public Matrix4x4 ViewProj => _viewProj;

    /// <summary>The view and projection matrices from the last <see cref="SetCamera"/> (the sky needs them apart).</summary>
    public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;

    /// <inheritdoc cref="View"/>
    public Matrix4x4 Proj { get; private set; } = Matrix4x4.Identity;

    /// <summary>The camera position from the last <see cref="SetCamera"/>.</summary>
    public Vector3 CameraPosition { get; private set; }

    /// <summary>The fog colour and (start, end) range from the last <see cref="SetFog"/>.</summary>
    public Vector3 FogColor { get; private set; }

    public Vector2 FogRange { get; private set; }

    /// <summary>The surface texture array (see <see cref="Textures"/>), bound to texture unit 0 while drawing.</summary>
    public uint TextureArray => _texture;

    /// <summary>Re-binds this renderer's shader, cube mesh and texture array after another renderer used its own.</summary>
    // ---- Instanced batches -------------------------------------------------------------------------------

    /// <summary>Floats per batched box (the model matrix, the colour and alpha, the mode and the material layer), and boxes per instanced draw.</summary>
    public const int BatchFloats = 22, BatchMax = 4096;

    private readonly float[] _batch = new float[BatchMax * BatchFloats];
    private int _batchCount;
    private bool _batching, _blendOn, _instReady, _instBroken;
    private GlProgram _instProgram;
    private GlVertexArray _instVao;
    private GlBuffer _instVbo;
    private int _iLocViewProj, _iLocCam, _iLocFog, _iLocFogRange, _iLocTex;

    /// <summary>Instanced draws and the boxes in them since the last read (the smoke test's zombie line).</summary>
    public int BatchDraws, BatchBoxes;

    /// <summary>
    /// Until <see cref="EndBatch"/>, opaque <see cref="Draw"/> calls (no blood, no blending) are collected and drawn as
    /// one instanced draw per <see cref="BatchMax"/> boxes with the same shader maths (an instanced copy of the box
    /// shader). Anything else flushes the batch first, so the order never changes. Used for a zombie horde: hundreds of
    /// bodies of a dozen boxes each. Nothing happens headless (recording) or if the instanced shader couldn't be made.
    /// </summary>
    public void BeginBatch()
    {
        if (Recorder != null || _instBroken || _batching)
            return;
        if (!_instReady && !InitInstanced())
            return;
        _batching = true;
        _batchCount = 0;
    }

    /// <summary>Draws what the batch collected and goes back to drawing each box at once.</summary>
    public void EndBatch()
    {
        if (!_batching)
            return;
        FlushBatch();
        _batching = false;
    }

    private void FlushBatch()
    {
        if (_batchCount == 0)
            return;
        _instProgram.Use();
        var vp = _viewProj;
        Native.glUniformMatrix4fv(_iLocViewProj, 1, false, &vp.M11);
        var cam = CameraPosition;
        Native.glUniform3f(_iLocCam, cam.X, cam.Y, cam.Z);
        Native.glUniform3f(_iLocFog, FogColor.X, FogColor.Y, FogColor.Z);
        Native.glUniform2f(_iLocFogRange, FogRange.X, FogRange.Y);
        if (RenderCaps.PersistentBuffers && Ring() is { } ring)
        {
            // 4.6 path: straight into this frame's segment of the persistent ring, drawn from its base instance
            // (written first: a write can grow the ring, and the VAO must point at the buffer it ended up in).
            int first;
            fixed (float* p = _batch)
                first = ring.Write(p, _batchCount, BatchFloats);
            RingVao(ring);
            Native.glDrawArraysInstancedBaseInstance(GL_TRIANGLES, 0, 36, _batchCount, (uint)first);
        }
        else
        {
            _instVao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, _batch.Length * sizeof(float), null, GL_STREAM_DRAW); // orphan: no stall on the last copy
            fixed (float* p = _batch)
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, _batchCount * BatchFloats * sizeof(float), p);
            Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36, _batchCount);
        }
        BatchDraws++;
        BatchBoxes += _batchCount;
        _batchCount = 0;
        _program.Use();
        _vao.Bind();
    }

    private StreamRing? _ring;
    private GlVertexArray _ringVao;
    private int _ringGen = -1;
    private bool _ringBroken;

    /// <summary>The persistent instance ring (made on first use), or null if it can't be made (then the batch takes the 4.1 path's orphaned buffer).</summary>
    private StreamRing? Ring()
    {
        if (_ringBroken)
            return null;
        try
        {
            return _ring ??= new StreamRing(BatchFloats * sizeof(float), BatchMax * 2, "Renderer.batchRing");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] persistent instance ring off: {ex.Message}");
            _ringBroken = true;
            return null;
        }
    }

    /// <summary>Binds the ring's VAO, made or re-pointed when the ring was (re)made.</summary>
    private void RingVao(StreamRing ring)
    {
        if (_ringVao.Id == 0)
        {
            _ringVao = GenVertexArray();
            GlDebug.Label(GL_VERTEX_ARRAY, _ringVao.Id, "Renderer.batchRing");
        }
        _ringVao.Bind();
        if (_ringGen != ring.Generation)
        {
            BoxInstanceAttributes(ring.Buffer);
            _ringGen = ring.Generation;
        }
    }

    /// <summary>The instanced box VAO's attributes: the cube from the box buffer, the instances from <paramref name="instances"/> (bound VAO).</summary>
    private void BoxInstanceAttributes(GlBuffer instances)
    {
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        const int stride = 6 * sizeof(float);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
        EnableVertexAttribArray(0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
        EnableVertexAttribArray(1);
        Native.glBindBuffer(GL_ARRAY_BUFFER, instances);
        const int istride = BatchFloats * sizeof(float);
        for (uint a = 0; a < 5; a++)
        {
            Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, istride, (void*)(a * 4 * sizeof(float)));
            EnableVertexAttribArray(2 + a);
            Native.glVertexAttribDivisor(2 + a, 1);
        }
        Native.glVertexAttribPointer(7, 2, GL_FLOAT, false, istride, (void*)(20 * sizeof(float)));
        EnableVertexAttribArray(7);
        Native.glVertexAttribDivisor(7, 1);
    }

    private bool InitInstanced()
    {
        try
        {
            const string vsSrc = """
                #version 410 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in vec4 iM0;
                layout(location = 3) in vec4 iM1;
                layout(location = 4) in vec4 iM2;
                layout(location = 5) in vec4 iM3;
                layout(location = 6) in vec4 iColor;
                layout(location = 7) in vec2 iModeLayer;
                uniform mat4 uViewProj;
                out vec3 vWorld;
                out vec3 vNormal;
                out vec4 vColorI;
                flat out float vModeI;
                flat out float vLayerI;
                void main()
                {
                    mat4 model = mat4(iM0, iM1, iM2, iM3);
                    vec4 w = model * vec4(aPos, 1.0);
                    vWorld = w.xyz;
                    vNormal = mat3(model) * aNormal;
                    vColorI = iColor;
                    vModeI = iModeLayer.x;
                    vLayerI = iModeLayer.y;
                    gl_Position = uViewProj * w;
                }
                """;
            // The box shader's own fragment code, with the per-draw uniforms coming from the instance instead.
            var fsSrc = FragmentSrc.Replace("uniform vec4 uColor;", "in vec4 vColorI;\n#define uColor vColorI")
                .Replace("uniform float uMode;", "flat in float vModeI;\n#define uMode vModeI")
                .Replace("uniform float uLayer;", "flat in float vLayerI;\n#define uLayer vLayerI");
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vsSrc)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fsSrc)).Compile();
            _instProgram = CreateProgram();
            Native.glAttachShader(_instProgram, vs);
            Native.glAttachShader(_instProgram, fs);
            Native.glLinkProgram(_instProgram);
            GlDebug.Label(GL_PROGRAM, _instProgram.Id, "Renderer.instProgram");
            int linked;
            Native.glGetProgramiv(_instProgram, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("instanced box shader link failed");
            vs.Dispose();
            fs.Dispose();
            Lighting.Upload(_instProgram); // the sun and sky like every scene shader (and their changes)
            fixed (byte* p = "uViewProj\0"u8)
                _iLocViewProj = Native.glGetUniformLocation(_instProgram, p);
            fixed (byte* p = "uCam\0"u8)
                _iLocCam = Native.glGetUniformLocation(_instProgram, p);
            fixed (byte* p = "uFog\0"u8)
                _iLocFog = Native.glGetUniformLocation(_instProgram, p);
            fixed (byte* p = "uFogRange\0"u8)
                _iLocFogRange = Native.glGetUniformLocation(_instProgram, p);
            fixed (byte* p = "uTex\0"u8)
                _iLocTex = Native.glGetUniformLocation(_instProgram, p);
            Native.glUniform1i(_iLocTex, 0);

            _instVao = GenVertexArray();
            _instVao.Bind();
            GlDebug.Label(GL_VERTEX_ARRAY, _instVao.Id, "Renderer.batch");
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, _batch.Length * sizeof(float), null, GL_STREAM_DRAW);
            GlDebug.Label(GL_BUFFER, _instVbo.Id, "Renderer.batch");
            BoxInstanceAttributes(_instVbo);
            _program.Use();
            _vao.Bind();
            var err = GlDebug.Error();
            if (err != 0)
                Console.WriteLine($"[render] GL error 0x{err:X} while setting up the instanced boxes (continuing)");
            Console.WriteLine($"[render] instanced boxes ready ({BatchMax} a draw)");
            _instReady = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] instanced boxes off (one draw each): {ex.Message}");
            _instBroken = true;
            _program.Use();
            _vao.Bind();
            return false;
        }
    }

    public void Rebind()
    {
        _program.Use();
        _vao.Bind();
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _texture);
    }

    public void ClearDepthBuffer() => Clear(GlClearBit.Depth);

    /// <summary>Restrict drawing to a rectangle of the framebuffer (pixels, origin bottom-left).</summary>
    public void SetViewport(int x, int y, int w, int h) => Viewport(x, y, Math.Max(1, w), Math.Max(1, h));

    public void SetDepthTest(bool on)
    {
        if (on)
            Enable(GlCapability.DepthTest);
        else
            Native.glDisable(GlCapability.DepthTest);
    }

    /// <summary>Turn depth writes off for soft transparent things like smoke.</summary>
    public void SetDepthWrite(bool on)
    {
        if (Recorder != null)
            Recorder.SetDepthWrite(on);
        else
            DepthMask(on);
    }

    /// <summary>
    /// Self-test hook: while set, <see cref="Draw"/>, <see cref="SetBlend"/> and <see cref="SetDepthWrite"/> report to it
    /// instead of calling OpenGL, so draw code can be checked headlessly (see Source/Scene).
    /// </summary>
    public IDrawRecorder? Recorder;

    public void SetBlend(bool on)
    {
        if (Recorder != null)
        {
            Recorder.SetBlend(on);
            return;
        }
        if (_batching)
            FlushBatch();
        _blendOn = on;
        if (on)
            Enable(GlCapability.Blend);
        else
            Native.glDisable(GlCapability.Blend);
    }

    /// <summary>
    /// Body blood for the next draws (one body): <paramref name="marks"/> marks, two vec4s each in <paramref name="data"/>
    /// (centre + radius, opacity + seed), painted in <paramref name="color"/> on whatever lit box surface they touch; the
    /// offsets are turned by <paramref name="yaw"/> so the pattern rides with the body. No extra draw calls: the body's own
    /// boxes paint it in the fragment shader. <see cref="ClearBlood"/> after the body. Does nothing headless or recording.
    /// </summary>
    public void SetBlood(ReadOnlySpan<Vector4> data, int marks, Vector3 color, float yaw)
    {
        if (Recorder != null || _locBloodN < 0 || marks <= 0)
            return;
        if (_batching)
            FlushBatch();
        marks = Math.Min(marks, Math.Min(8, data.Length / 2));
        float c = MathF.Cos(yaw), sn = MathF.Sin(yaw);
        fixed (Vector4* p = data)
        {
            if (Native.glProgramUniform4fv != null && Native.glProgramUniform1f != null && Native.glProgramUniform3f != null && Native.glProgramUniform2f != null)
            {
                // Straight into this renderer's program, whichever program is bound (GL 4.1).
                Native.glProgramUniform4fv(_program, _locBlood, marks * 2, (float*)p);
                Native.glProgramUniform1f(_program, _locBloodN, marks);
                Native.glProgramUniform3f(_program, _locBloodCol, color.X, color.Y, color.Z);
                Native.glProgramUniform2f(_program, _locBloodRot, c, sn);
            }
            else
            {
                _program.Use();
                Native.glUniform4fv(_locBlood, marks * 2, (float*)p);
                Native.glUniform1f(_locBloodN, marks);
                Native.glUniform3f(_locBloodCol, color.X, color.Y, color.Z);
                Native.glUniform2f(_locBloodRot, c, sn);
            }
        }
        _bloodOn = true;
    }

    /// <summary>No body blood on the next draws (cheap when none was set).</summary>
    public void ClearBlood()
    {
        if (!_bloodOn)
            return;
        _bloodOn = false;
        if (Native.glProgramUniform1f != null)
            Native.glProgramUniform1f(_program, _locBloodN, 0f);
        else
        {
            _program.Use();
            Native.glUniform1f(_locBloodN, 0f);
        }
    }

    /// <summary>Draws the unit cube transformed by <paramref name="model"/>.</summary>
    /// <summary>
    /// A planet map: an extra transform applied after every <see cref="Draw"/>'s model matrix while set (see
    /// <see cref="StandAt"/>), so code that draws something upright with world axes stands it on the curve instead.
    /// </summary>
    private Matrix4x4 _tilt = Matrix4x4.Identity;

    private bool _tilted;

    /// <summary>Restores the previous tilt when disposed (<see cref="StandAt"/>).</summary>
    public readonly struct TiltScope : IDisposable
    {
        private readonly Renderer? _g;
        private readonly bool _wasTilted;
        private readonly Matrix4x4 _was;

        public TiltScope(Renderer g, bool tilted, Matrix4x4 tilt)
        {
            _g = g;
            _wasTilted = tilted;
            _was = tilt;
        }

        public void Dispose()
        {
            if (_g != null)
            {
                _g._tilted = _wasTilted;
                _g._tilt = _was;
            }
        }
    }

    /// <summary>
    /// On a planet map, until the returned scope is disposed: things drawn upright round <paramref name="p"/> stand on the
    /// ground there (<see cref="GravityFrames.StandAt"/>). Nothing at all on other maps.
    /// </summary>
    public TiltScope StandAt(Vector3 p)
    {
        if (!GravityFrames.Curved)
            return default;
        var scope = new TiltScope(this, _tilted, _tilt);
        _tilt = GravityFrames.StandAt(p);
        _tilted = true;
        return scope;
    }

    /// <summary>
    /// Draws the unit cube transformed by <paramref name="model"/>, or with <paramref name="shape"/> &gt; 0 a weapon part's
    /// contoured shape inside it (<see cref="PartShapes"/>; uploaded into the box buffer the first time it's needed).
    /// </summary>
    public void Draw(in Matrix4x4 model, Vector3 color, float alpha = 1f, int mode = 0, Material material = Material.Grid, int shape = 0)
    {
        if (_tilted)
        {
            var tilted = model * _tilt;
            _tilted = false;
            Draw(tilted, color, alpha, mode, material, shape);
            _tilted = true;
            return;
        }
        if (Recorder != null)
        {
            Recorder.Draw(model, color, alpha, mode, material);
            return;
        }
        if (_batching)
        {
            if (!_bloodOn && !_blendOn && alpha >= 1f && shape == 0)
            {
                // Batched (a zombie horde): one instanced draw per few thousand boxes instead of one draw each.
                var o = _batchCount * BatchFloats;
                var b = _batch;
                b[o] = model.M11; b[o + 1] = model.M12; b[o + 2] = model.M13; b[o + 3] = model.M14;
                b[o + 4] = model.M21; b[o + 5] = model.M22; b[o + 6] = model.M23; b[o + 7] = model.M24;
                b[o + 8] = model.M31; b[o + 9] = model.M32; b[o + 10] = model.M33; b[o + 11] = model.M34;
                b[o + 12] = model.M41; b[o + 13] = model.M42; b[o + 14] = model.M43; b[o + 15] = model.M44;
                b[o + 16] = color.X; b[o + 17] = color.Y; b[o + 18] = color.Z; b[o + 19] = alpha;
                b[o + 20] = mode; b[o + 21] = (float)material;
                if (++_batchCount == BatchMax)
                    FlushBatch();
                return;
            }
            FlushBatch(); // keep the order: everything batched so far goes first
        }
        Native.glUniform1f(_locLayer, (float)material);
        var mvp = model * _viewProj;
        var m = model;
        Native.glUniformMatrix4fv(_locMvp, 1, false, &mvp.M11);
        Native.glUniformMatrix4fv(_locModel, 1, false, &m.M11);
        Native.glUniform4f(_locColor, color.X, color.Y, color.Z, alpha);
        Native.glUniform1f(_locMode, mode);
        if (shape == 0)
        {
            Native.glDrawArrays(GL_TRIANGLES, 0, 36);
            return;
        }
        if (_shapeVersion != PartShapes.Version)
            UploadShapes();
        if (shape + 1 < _shapeFirst.Count)
            Native.glDrawArrays(GL_TRIANGLES, _shapeFirst[shape], _shapeFirst[shape + 1] - _shapeFirst[shape]);
    }

    private int _shapeVersion; // PartShapes.Version starts at 0 with no shapes
    private readonly List<int> _shapeFirst = [];

    /// <summary>Puts the unit cube and every part shape made so far into the box vertex buffer (once per new batch of shapes, at load).</summary>
    private void UploadShapes()
    {
        var data = new List<float>(BuildCube());
        _shapeVersion = PartShapes.CopyAll(data, _shapeFirst);
        var arr = data.ToArray();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (float* p = arr)
            Native.glBufferData(GL_ARRAY_BUFFER, arr.Length * sizeof(float), p, GL_STATIC_DRAW);
    }

    public void DrawBox(Box box, Vector3 color, int mode = 0, Material material = Material.Grid) =>
        Draw(Matrix4x4.CreateScale(box.Half * 2) * Matrix4x4.CreateTranslation(box.Center), color, 1f, mode, material);

    /// <summary>Uploads the generated textures (see <see cref="Textures"/>) as one 2D texture array with mipmaps.</summary>
    private void CreateTextures()
    {
        var pixels = Textures.Generate();
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _texture = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _texture);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = pixels)
            Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, Textures.Size, Textures.Size, (int)Material.Count, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
        // Crisp texels up close (blocky style), smooth mipmaps far away so it doesn't shimmer.
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAX_ANISOTROPY, 8);
    }

    /// <summary>Screen-space rectangle in pixels (origin bottom-left). Use with an ortho camera.</summary>
    public void Rect(float x, float y, float w, float h, Vector3 color, float alpha = 1f) =>
        Draw(
            Matrix4x4.CreateScale(w, h, 1) * Matrix4x4.CreateTranslation(x + w / 2, y + h / 2, 0),
            color,
            alpha,
            mode: 2
        );

    private static float[] BuildCube()
    {
        // Each face: normal, then 2 triangles.
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
            Vector3 a = c - u * 0.5f - v * 0.5f,
                b = c + u * 0.5f - v * 0.5f,
                d = c + u * 0.5f + v * 0.5f,
                e = c - u * 0.5f + v * 0.5f;
            Vector3[] corners = [a, b, d, a, d, e];
            foreach (var p in corners)
                data.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z]);
        }
        return data.ToArray();
    }
}
