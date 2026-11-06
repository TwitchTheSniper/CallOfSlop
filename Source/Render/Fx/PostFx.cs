using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Post effects over the frame, each on a setting (docs/render/MATERIALS.md, "Post effects"). The world is still drawn straight to the
/// multisampled back buffer: nothing here changes how it is drawn. Once the world and the effects are in, the back buffer is resolved into a
/// plain texture once (<see cref="Apply"/>), and what is worth doing is done from that copy at small sizes. <b>Bloom</b> blooms only what
/// glows: lamps, glowing boxes, flames and screens leave a mark in the back buffer's alpha (<see cref="Renderer.MarkGlow"/>, the particle
/// blend, the prop and panel shaders), so a white wall in the sun never blooms. It is added back to the back buffer, with no HDR target.
/// <b>Heat haze</b> is a few small quads over flames (<see cref="AddHaze"/>) that read the copy a little off to the side.
/// <b>Ambient occlusion</b> (High only; <see cref="ApplyAo"/>, before the effects) reads a resolved depth buffer at half size, 8 samples.
/// Low costs nothing: no pass runs and nothing is made. The 4.1 and 4.6 paths share all of this.
/// </summary>
public static unsafe class PostFx
{
    public const int MaxHaze = 32;
    private const int Unit0 = 17, Unit1 = 18; // (0-16 are taken: VideoFeeds.Unit, SurfaceGpu.Unit)

    /// <summary>Settings > Video > Post effects, and each effect's own switch.</summary>
    public static VideoLevel Level = VideoLevel.Medium;

    public static bool BloomOn = true, HazeOn = true, AoOn = true;

    public static bool Bloom => Level >= VideoLevel.Medium && BloomOn;

    public static bool Haze => Level >= VideoLevel.Medium && HazeOn;

    public static bool Ao => Level >= VideoLevel.High && AoOn;

    private static readonly Vector4[] Hazes = new Vector4[MaxHaze];
    private static int _hazeCount;

    /// <summary>A spot of heat this frame: world position, radius (m), strength 0..1. More than <see cref="MaxHaze"/> a frame are dropped.</summary>
    public static void AddHaze(Vector3 at, float radius, float strength)
    {
        if (_hazeCount < MaxHaze && Haze)
            Hazes[_hazeCount++] = new Vector4(at, MathF.Floor(radius * 100f) + Math.Clamp(strength, 0f, 0.99f)); // (w: radius in cm, strength in the fraction)
    }

    private const string QuadVs = """
        #version 410 core
        out vec2 vUv;
        void main()
        {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            vUv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    // The glowing part of a texture, down to half size: its colour where the alpha marks it (or, with no alpha in the back buffer, its brightest).
    private const string ExtractFs = """
        #version 410 core
        in vec2 vUv;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        uniform float uMarked;
        out vec4 FragColor;
        vec3 Pick(vec2 uv)
        {
            vec4 c = texture(uTex, uv);
            if (uMarked > 0.5)
                return c.rgb * (1.0 - c.a);
            float b = max(c.r, max(c.g, c.b));
            return c.rgb * smoothstep(0.86, 1.0, b);
        }
        void main()
        {
            vec2 d = uTexel * 0.5;
            FragColor = vec4((Pick(vUv + vec2(-d.x, -d.y)) + Pick(vUv + vec2(d.x, -d.y)) + Pick(vUv + vec2(-d.x, d.y)) + Pick(vUv + d)) * 0.25, 1.0);
        }
        """;

    private const string BlurFs = """
        #version 410 core
        in vec2 vUv;
        uniform sampler2D uTex;
        uniform vec2 uStep;
        out vec4 FragColor;
        void main()
        {
            // 9 taps in 5 fetches (linear filtering).
            vec3 c = texture(uTex, vUv).rgb * 0.227027;
            c += (texture(uTex, vUv + uStep * 1.384615).rgb + texture(uTex, vUv - uStep * 1.384615).rgb) * 0.316216;
            c += (texture(uTex, vUv + uStep * 3.230769).rgb + texture(uTex, vUv - uStep * 3.230769).rgb) * 0.070270;
            FragColor = vec4(c, 1.0);
        }
        """;

    private const string CopyFs = """
        #version 410 core
        in vec2 vUv;
        uniform sampler2D uTex;
        uniform sampler2D uTex2;
        uniform float uMix;
        uniform float uGain;
        out vec4 FragColor;
        void main()
        {
            FragColor = vec4((texture(uTex, vUv).rgb + texture(uTex2, vUv).rgb * uMix) * uGain, 1.0);
        }
        """;

    private const string HazeVs = """
        #version 410 core
        uniform mat4 uViewProj;
        uniform vec3 uRight;
        uniform vec3 uUp;
        uniform vec4 uHaze[32];
        out vec2 vQuad;
        flat out float vStrength;
        void main()
        {
            vec2 q = vec2(gl_VertexID & 1, (gl_VertexID >> 1) & 1) * 2.0 - 1.0;
            vec4 h = uHaze[gl_InstanceID];
            vQuad = q;
            float r = floor(h.w) * 0.01;
            vStrength = fract(h.w) * 1.1;
            gl_Position = uViewProj * vec4(h.xyz + (uRight * q.x + uUp * q.y) * r, 1.0);
        }
        """;

    private const string HazeFs = """
        #version 410 core
        in vec2 vQuad;
        flat in float vStrength;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        uniform float uTime;
        out vec4 FragColor;
        void main()
        {
            float r2 = dot(vQuad, vQuad);
            if (r2 >= 1.0)
                discard;
            float f = (1.0 - r2) * (1.0 - r2) * vStrength;
            vec2 uv = gl_FragCoord.xy * uTexel;
            vec2 w = gl_FragCoord.xy * 0.06;
            vec2 off = vec2(sin(w.y * 1.7 + uTime * 9.0) + sin(w.y * 0.9 - uTime * 6.0), sin(w.x * 1.3 - uTime * 7.0)) * uTexel * 3.2 * f;
            FragColor = vec4(texture(uTex, uv + off).rgb, 1.0);
        }
        """;

    private const string AoFs = """
        #version 410 core
        in vec2 vUv;
        uniform sampler2D uDepth;
        uniform vec4 uProj;    // M33, M43, 1 / M11, 1 / M22
        uniform vec2 uTexel;
        out vec4 FragColor;
        float Linear(vec2 uv)
        {
            float nd = texture(uDepth, uv).r * 2.0 - 1.0;
            return uProj.y / (nd + uProj.x);
        }
        vec3 View(vec2 uv)
        {
            float l = Linear(uv);
            return vec3((uv * 2.0 - 1.0) * uProj.zw * l, -l);
        }
        void main()
        {
            float d0 = texture(uDepth, vUv).r;
            if (d0 >= 0.99999)
            {
                FragColor = vec4(1.0);
                return;
            }
            vec3 p = View(vUv);
            vec3 n = normalize(cross(dFdx(p), dFdy(p)));
            float a0 = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715)))) * 6.2832;
            const float radius = 0.7;
            float screen = radius / (uProj.z * max(-p.z, 0.3)) * 0.5;
            float occ = 0.0;
            for (int i = 0; i < 8; i++)
            {
                float t = (float(i) + 0.5) / 8.0;
                float a = a0 + t * 18.85;
                vec2 uv = vUv + vec2(cos(a), sin(a)) * vec2(screen, screen * uTexel.y / uTexel.x) * t;
                vec3 v = View(uv) - p;
                float d2 = dot(v, v);
                float c = max(dot(v, n) - 0.02 * -p.z * 0.05, 0.0) / (d2 + 0.02);
                occ += c * max(0.0, 1.0 - d2 / (radius * radius * 1.4));
            }
            float ao = clamp(1.0 - occ * radius * 0.28, 0.0, 1.0);
            FragColor = vec4(ao, ao, ao, 1.0);
        }
        """;

    private const string AoBlurFs = """
        #version 410 core
        in vec2 vUv;
        uniform sampler2D uTex;
        uniform vec2 uTexel;
        out vec4 FragColor;
        void main()
        {
            vec2 d = uTexel;
            float s = texture(uTex, vUv + vec2(-d.x, -d.y)).r + texture(uTex, vUv + vec2(d.x, -d.y)).r
                    + texture(uTex, vUv + vec2(-d.x, d.y)).r + texture(uTex, vUv + d).r;
            FragColor = vec4(vec3(s * 0.25), 1.0);
        }
        """;

    private sealed class Target
    {
        public GlFramebuffer Fbo;
        public uint Tex;
        public int W, H;
    }

    private static bool _ready, _broken, _aoBroken, _alpha, _copied;
    private static int _w, _h;
    private static GlVertexArray _vao;
    private static GlProgram _extract, _blur, _copy, _haze, _ao, _aoBlur;
    private static int _lExtractTexel, _lExtractMarked, _lBlurStep, _lCopyMix, _lCopyGain, _lHazeVp, _lHazeRight, _lHazeUp, _lHazeData, _lHazeTexel, _lHazeTime, _lAoProj, _lAoTexel, _lAoBlurTexel;
    private static readonly Target Scene = new(), Half = new(), Quarter = new(), Quarter2 = new(), Eighth = new(), Eighth2 = new(), AoA = new(), AoB = new();
    private static uint _depthTex;
    private static GlFramebuffer _depthFbo;

    private static GlProgram Link(string vs, string fs, string label)
    {
        var v = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vs)).Compile();
        var f = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fs)).Compile();
        var p = CreateProgram();
        Native.glAttachShader(p, v);
        Native.glAttachShader(p, f);
        Native.glLinkProgram(p);
        GlDebug.Label(GL_PROGRAM, p.Id, label);
        int linked;
        Native.glGetProgramiv(p, GL_LINK_STATUS, &linked);
        if (linked == 0)
            throw new InvalidOperationException(label + " did not link");
        v.Dispose();
        f.Dispose();
        p.Use();
        Set(p, "uTex\0"u8, Unit0);
        Set(p, "uTex2\0"u8, Unit1);
        Set(p, "uDepth\0"u8, Unit0);
        return p;
    }

    private static void Set(GlProgram p, ReadOnlySpan<byte> name, int v)
    {
        var l = Loc(p, name);
        if (l >= 0)
            Native.glUniform1i(l, v);
    }

    private static int Loc(GlProgram p, ReadOnlySpan<byte> name)
    {
        fixed (byte* n = name)
            return Native.glGetUniformLocation(p, n);
    }

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        try
        {
            _extract = Link(QuadVs, ExtractFs, "PostFx.extract");
            _lExtractTexel = Loc(_extract, "uTexel\0"u8);
            _lExtractMarked = Loc(_extract, "uMarked\0"u8);
            _blur = Link(QuadVs, BlurFs, "PostFx.blur");
            _lBlurStep = Loc(_blur, "uStep\0"u8);
            _copy = Link(QuadVs, CopyFs, "PostFx.copy");
            _lCopyMix = Loc(_copy, "uMix\0"u8);
            _lCopyGain = Loc(_copy, "uGain\0"u8);
            _haze = Link(HazeVs, HazeFs, "PostFx.haze");
            _lHazeVp = Loc(_haze, "uViewProj\0"u8);
            _lHazeRight = Loc(_haze, "uRight\0"u8);
            _lHazeUp = Loc(_haze, "uUp\0"u8);
            _lHazeData = Loc(_haze, "uHaze\0"u8);
            _lHazeTexel = Loc(_haze, "uTexel\0"u8);
            _lHazeTime = Loc(_haze, "uTime\0"u8);
            _ao = Link(QuadVs, AoFs, "PostFx.ao");
            _lAoProj = Loc(_ao, "uProj\0"u8);
            _lAoTexel = Loc(_ao, "uTexel\0"u8);
            _aoBlur = Link(QuadVs, AoBlurFs, "PostFx.aoBlur");
            _lAoBlurTexel = Loc(_aoBlur, "uTexel\0"u8);
            _vao = GenVertexArray();
            GlDebug.Label(GL_VERTEX_ARRAY, _vao.Id, "PostFx");
            int alpha = 0;
            Native.glBindFramebuffer(GL_FRAMEBUFFER, default);
            Native.glGetFramebufferAttachmentParameteriv(GL_FRAMEBUFFER, GL_BACK_LEFT, GL_FRAMEBUFFER_ATTACHMENT_ALPHA_SIZE, &alpha);
            _alpha = alpha > 0;
            while (GlDebug.Error() != 0) { }
            Console.WriteLine($"[postfx] ready: {(_alpha ? "glow marked in the back buffer's alpha" : "no alpha in the back buffer: bloom picks the brightest pixels")}");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[postfx] off: {ex.Message}");
            _broken = true;
            return false;
        }
    }

    private static void Size(Target t, int w, int h, uint format, bool linear)
    {
        if (t.Tex == 0)
        {
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            t.Tex = tex.Id;
            GlFramebuffer fb;
            Native.glGenFramebuffers(1, &fb);
            t.Fbo = fb;
        }
        t.W = w;
        t.H = h;
        Native.glActiveTexture(GL_TEXTURE0 + Unit0);
        Native.glBindTexture(GL_TEXTURE_2D, t.Tex);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)format, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)(linear ? GL_LINEAR : GL_NEAREST));
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)(linear ? GL_LINEAR : GL_NEAREST));
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, t.Fbo);
        Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, t.Tex, 0);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, default);
    }

    /// <summary>The targets for this frame's size (made again only when the window's size changes).</summary>
    private static void Resize(int w, int h)
    {
        if (w == _w && h == _h)
            return;
        _w = w;
        _h = h;
        Size(Scene, w, h, GL_RGBA8, true);
        Size(Half, Math.Max(w / 2, 1), Math.Max(h / 2, 1), GL_RGBA8, true);
        Size(Quarter, Math.Max(w / 4, 1), Math.Max(h / 4, 1), GL_RGBA8, true);
        Size(Quarter2, Quarter.W, Quarter.H, GL_RGBA8, true);
        Size(Eighth, Math.Max(w / 8, 1), Math.Max(h / 8, 1), GL_RGBA8, true);
        Size(Eighth2, Eighth.W, Eighth.H, GL_RGBA8, true);
        Size(AoA, Half.W, Half.H, GL_RGBA8, false);
        Size(AoB, Half.W, Half.H, GL_RGBA8, true);
        if (_depthTex != 0)
        {
            var t = new GlTexture(_depthTex);
            Native.glDeleteTextures(1, &t);
            _depthTex = 0;
        }
    }

    private static void Bind(int unit, uint tex)
    {
        Native.glActiveTexture(GL_TEXTURE0 + (uint)unit);
        Native.glBindTexture(GL_TEXTURE_2D, tex);
    }

    private static void Pass(Target to)
    {
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, to.Fbo);
        Viewport(0, 0, to.W, to.H);
        Native.glDrawArrays(GL_TRIANGLES, 0, 3);
    }

    private static void Begin()
    {
        Native.glDisable(GlCapability.DepthTest);
        Native.glDisable(GlCapability.Blend);
        Native.glDepthMask(false);
        _vao.Bind();
    }

    private static void End(Renderer gfx, int w, int h)
    {
        Native.glBindFramebuffer(GL_FRAMEBUFFER, default);
        Viewport(0, 0, w, h);
        Native.glColorMask(true, true, true, true);
        Native.glDisable(GlCapability.Blend);
        Native.glEnable(GlCapability.DepthTest);
        Native.glDepthMask(true);
        Native.glActiveTexture(GL_TEXTURE0);
        gfx.AfterFx();
    }

    /// <summary>Ambient occlusion over what is drawn so far (the opaque world): before the blended effects, so smoke and flames aren't darkened.</summary>
    public static void ApplyAo(Renderer gfx, int w, int h, in Matrix4x4 proj)
    {
        if (!Ao || _aoBroken || !Init())
            return;
        Resize(w, h);
        if (_depthTex == 0)
        {
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _depthTex = tex.Id;
            Native.glActiveTexture(GL_TEXTURE0 + Unit0);
            Native.glBindTexture(GL_TEXTURE_2D, _depthTex);
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_DEPTH24_STENCIL8, w, h, 0, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, null);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            if (_depthFbo.Id == 0)
            {
                GlFramebuffer fb;
                Native.glGenFramebuffers(1, &fb);
                _depthFbo = fb;
            }
            Native.glBindFramebuffer(GL_FRAMEBUFFER, _depthFbo);
            Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, _depthTex, 0);
            Native.glBindFramebuffer(GL_FRAMEBUFFER, default);
        }
        while (GlDebug.Error() != 0) { }
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, default);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, _depthFbo);
        Native.glBlitFramebuffer(0, 0, w, h, 0, 0, w, h, GL_DEPTH_BUFFER_BIT, GL_NEAREST);
        var err = GlDebug.Error();
        if (err != 0)
        {
            Console.WriteLine($"[postfx] ambient occlusion off: the depth buffer could not be read (GL error 0x{err:X})");
            _aoBroken = true;
            End(gfx, w, h);
            return;
        }
        Begin();
        _ao.Use();
        Native.glUniform4f(_lAoProj, proj.M33, proj.M43, 1f / proj.M11, 1f / proj.M22);
        Native.glUniform2f(_lAoTexel, 1f / AoA.W, 1f / AoA.H);
        Bind(Unit0, _depthTex);
        Pass(AoA);
        _aoBlur.Use();
        Native.glUniform2f(_lAoBlurTexel, 1f / AoA.W, 1f / AoA.H);
        Bind(Unit0, AoA.Tex);
        Pass(AoB);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, default);
        Viewport(0, 0, w, h);
        Native.glEnable(GlCapability.Blend);
        Native.glBlendFunc(GL_ZERO, GL_SRC_COLOR);
        Native.glColorMask(true, true, true, false);
        _copy.Use();
        Native.glUniform1f(_lCopyMix, 0f);
        Native.glUniform1f(_lCopyGain, 1f);
        Bind(Unit0, AoB.Tex);
        Bind(Unit1, AoB.Tex);
        Native.glDrawArrays(GL_TRIANGLES, 0, 3);
        End(gfx, w, h);
    }

    /// <summary>Haze and bloom over the frame so far (after the effects, before the first-person gun and the HUD).</summary>
    public static void Apply(Renderer gfx, int w, int h, double time)
    {
        var haze = Haze && _hazeCount > 0;
        if ((!Bloom && !haze) || !Init())
        {
            _hazeCount = 0;
            return;
        }
        Resize(w, h);
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, default);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, Scene.Fbo);
        Native.glBlitFramebuffer(0, 0, w, h, 0, 0, w, h, GL_COLOR_BUFFER_BIT, GL_NEAREST);
        if (!_copied)
        {
            _copied = true;
            var err = GlDebug.Error();
            if (err != 0)
            {
                Console.WriteLine($"[postfx] off: the back buffer could not be read (GL error 0x{err:X})");
                _broken = true;
                _ready = false;
                End(gfx, w, h);
                return;
            }
        }
        Begin();
        if (haze)
            DrawHaze(gfx, w, h, time);
        if (Bloom)
            DrawBloom(w, h);
        _hazeCount = 0;
        End(gfx, w, h);
    }

    private static void DrawHaze(Renderer gfx, int w, int h, double time)
    {
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, default);
        Viewport(0, 0, w, h);
        Native.glEnable(GlCapability.DepthTest);
        Native.glColorMask(true, true, true, false);
        _haze.Use();
        var vp = gfx.ViewProj;
        Native.glUniformMatrix4fv(_lHazeVp, 1, false, &vp.M11);
        var v = gfx.View;
        Native.glUniform3f(_lHazeRight, v.M11, v.M21, v.M31);
        Native.glUniform3f(_lHazeUp, v.M12, v.M22, v.M32);
        fixed (Vector4* p = Hazes)
            Native.glUniform4fv(_lHazeData, _hazeCount, (float*)p);
        Native.glUniform2f(_lHazeTexel, 1f / w, 1f / h);
        Native.glUniform1f(_lHazeTime, (float)(time % 1024.0));
        Bind(Unit0, Scene.Tex);
        Native.glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, _hazeCount);
        Native.glDisable(GlCapability.DepthTest);
        Native.glColorMask(true, true, true, true);
    }

    private static void DrawBloom(int w, int h)
    {
        var high = Level >= VideoLevel.High;
        _extract.Use();
        Native.glUniform2f(_lExtractTexel, 1f / Scene.W, 1f / Scene.H);
        Native.glUniform1f(_lExtractMarked, _alpha ? 1f : 0f);
        Bind(Unit0, Scene.Tex);
        Pass(Half);
        _blur.Use();
        Blur(Half, Quarter, Quarter.W, Quarter.H, true);
        Blur(Quarter, Quarter2, Quarter.W, Quarter.H, false);
        if (high)
        {
            Blur(Quarter2, Eighth, Eighth.W, Eighth.H, true);
            Blur(Eighth, Eighth2, Eighth.W, Eighth.H, false);
        }
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, default);
        Viewport(0, 0, w, h);
        Native.glEnable(GlCapability.Blend);
        Native.glBlendFunc(GL_ONE, GL_ONE);
        Native.glColorMask(true, true, true, false);
        _copy.Use();
        Native.glUniform1f(_lCopyMix, high ? 1f : 0f);
        Native.glUniform1f(_lCopyGain, high ? 0.55f : 0.8f);
        Bind(Unit0, Quarter2.Tex);
        Bind(Unit1, Eighth2.Tex);
        Native.glDrawArrays(GL_TRIANGLES, 0, 3);
        Native.glDisable(GlCapability.Blend);
    }

    private static void Blur(Target from, Target to, int w, int h, bool across)
    {
        Native.glUniform2f(_lBlurStep, across ? 1f / w : 0f, across ? 0f : 1f / h);
        Bind(Unit0, from.Tex);
        Pass(to);
    }
}
