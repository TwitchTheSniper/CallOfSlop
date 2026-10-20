using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The view through an optic, drawn flat in the HUD pass: the eyepiece rim, a faint lens tint, a
/// vignette towards the rim, the reticle, and (at 4x and above) the black scope surround.
///
/// It is one small shader drawing one quad (the lens's bounding square, or the whole screen for the
/// scope surround), so the reticle is resolution-independent and anti-aliased (signed distances in
/// pixels) and costs one draw call. Everything is centred on the screen centre, which is exactly where
/// the sight line sits at full ADS (<see cref="GunModel.SightHeight"/>/<see cref="GunModel.AdsZ"/>).
/// The lens is a circle sized from the screen height, so it looks the same at 16:9 and 32:9: the
/// surround just covers more width on an ultrawide.
///
/// If the shader can't be built (old driver), it falls back to plain rectangles through the renderer.
/// </summary>
public static unsafe class ScopeOverlay
{
    private const string VertexSrc = """
        #version 330 core
        uniform vec4 uRect; // x0, y0, x1, y1 in clip space
        void main()
        {
            vec2 c = vec2((gl_VertexID & 1) != 0 ? uRect.z : uRect.x, (gl_VertexID & 2) != 0 ? uRect.w : uRect.y);
            gl_Position = vec4(c, 0.0, 1.0);
        }
        """;

    // Styles match GunModel.Reticle: 1 dot, 2 holo ring, 3 chevron, 4 horseshoe, 5 circle-dot,
    // 6 duplex, 7 fine crosshair, 8 stadia crosshair, 9 mil-dot. All sizes in pixels (uScale = px per unit).
    private const string FragmentSrc = """
        #version 330 core
        uniform vec2 uCenter;
        uniform float uRadius;   // lens radius in px (0 = open reflex window: reticle only)
        uniform float uSurround; // 0..1 black outside the lens
        uniform float uRing;     // 0..1 eyepiece rim strength
        uniform vec3 uTint;
        uniform float uTintA;
        uniform float uStyle;
        uniform vec3 uRetColor;
        uniform float uScale;
        uniform float uAlpha;
        out vec4 FragColor;

        vec4 acc = vec4(0.0);
        void over(vec3 c, float a)
        {
            a = clamp(a, 0.0, 1.0);
            acc.rgb = c * a + acc.rgb * (1.0 - a);
            acc.a = a + acc.a * (1.0 - a);
        }
        float seg(vec2 p, vec2 a, vec2 b, float hw)
        {
            vec2 pa = p - a, ba = b - a;
            float t = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
            return clamp(hw + 0.5 - length(pa - ba * t), 0.0, 1.0);
        }
        float disc(vec2 p, float r) { return clamp(r + 0.5 - length(p), 0.0, 1.0); }
        float ring(vec2 p, float r, float hw) { return clamp(hw + 0.5 - abs(length(p) - r), 0.0, 1.0); }

        void main()
        {
            vec2 p = gl_FragCoord.xy - uCenter;
            float r = length(p);
            float s = uScale;
            float R = uRadius;
            float soft = max(1.5, R * 0.03);
            float inLens = R > 0.0 ? 1.0 - smoothstep(R - soft, R, r) : 1.0;
            if (R > 0.0)
            {
                over(uTint, uTintA * inLens);
                over(vec3(0.0), smoothstep(R * 0.72, R, r) * 0.5 * inLens * uAlpha); // vignette towards the rim
            }

            int st = int(uStyle + 0.5);
            float hw = max(0.5, 0.6 * s);           // etched hairline half width
            float ext = R > 0.0 ? R : 120.0 * s;    // how far crosshair lines reach
            float etch = 0.0, lit = 0.0;
            if (st == 1)
                lit = max(disc(p, 2.0 * s), 0.3 * exp(-r / (2.0 * s)));
            else if (st == 2)
                lit = max(disc(p, 1.6 * s), ring(p, 22.0 * s, max(0.6, 0.9 * s)));
            else if (st == 3)
            {
                lit = max(seg(p, vec2(0.0), vec2(-9.0 * s, -9.0 * s), 1.2 * s), seg(p, vec2(0.0), vec2(9.0 * s, -9.0 * s), 1.2 * s));
                etch = seg(p, vec2(0.0, -16.0 * s), vec2(0.0, -64.0 * s), hw);
                for (int i = 1; i <= 3; i++)
                {
                    float y = -(16.0 + 12.0 * float(i)) * s, hl = (6.0 - float(i)) * s;
                    etch = max(etch, seg(p, vec2(-hl, y), vec2(hl, y), hw));
                }
            }
            else if (st == 4)
            {
                float ang = atan(abs(p.x), -p.y); // 0 = straight down
                lit = max(ring(p, 16.0 * s, max(0.6, 1.0 * s)) * smoothstep(0.55, 0.65, ang), disc(p, 1.6 * s));
            }
            else if (st == 5)
            {
                lit = max(ring(p, 11.0 * s, max(0.6, 0.9 * s)), disc(p, 1.4 * s));
                for (int i = 1; i <= 3; i++)
                {
                    float y = -(18.0 + 9.0 * float(i)) * s, hl = (6.0 - float(i)) * s;
                    lit = max(lit, seg(p, vec2(-hl, y), vec2(hl, y), max(0.5, 0.7 * s)));
                }
            }
            else if (st >= 6)
            {
                // Crosshairs: hairlines in the middle, thick posts towards the rim (none on the fine crosshair)
                float post = st == 7 ? 0.0 : ext * (st == 6 ? 0.33 : (st == 8 ? 0.5 : 0.55));
                float thin = post > 0.0 ? post : ext;
                float thick = 2.6 * s;
                etch = max(seg(p, vec2(-thin, 0.0), vec2(thin, 0.0), hw), seg(p, vec2(0.0, -thin), vec2(0.0, thin), hw));
                if (post > 0.0)
                {
                    etch = max(etch, max(seg(p, vec2(post, 0.0), vec2(ext, 0.0), thick), seg(p, vec2(-ext, 0.0), vec2(-post, 0.0), thick)));
                    etch = max(etch, max(seg(p, vec2(0.0, post), vec2(0.0, ext), thick), seg(p, vec2(0.0, -ext), vec2(0.0, -post), thick)));
                }
                if (st == 8)
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        float o = 10.0 * s * float(i), tl = (i == 2 || i == 4 ? 4.0 : 2.5) * s;
                        etch = max(etch, seg(p, vec2(o, -tl), vec2(o, tl), hw));
                        etch = max(etch, seg(p, vec2(-o, -tl), vec2(-o, tl), hw));
                        etch = max(etch, seg(p, vec2(-tl, -o), vec2(tl, -o), hw));
                    }
                    lit = max(disc(p, 1.5 * s), ring(p, 5.0 * s, max(0.5, 0.6 * s)) * 0.8);
                }
                else if (st == 9)
                {
                    float dr = max(1.0, 1.6 * s);
                    for (int i = 1; i <= 4; i++)
                    {
                        float o = ext * 0.1 * float(i);
                        etch = max(etch, max(max(disc(p - vec2(o, 0.0), dr), disc(p + vec2(o, 0.0), dr)), max(disc(p - vec2(0.0, o), dr), disc(p + vec2(0.0, o), dr))));
                    }
                    lit = disc(p, 0.9 * s);
                }
                else
                    lit = disc(p, 1.4 * s);
            }
            over(vec3(0.02), etch * 0.92 * inLens * uAlpha);
            over(uRetColor, lit * inLens * uAlpha);

            if (R > 0.0)
            {
                // Eyepiece rim just outside the lens, then black beyond it for the full scope view.
                float band = smoothstep(R - soft, R, r) * (1.0 - smoothstep(R * 1.05, R * 1.05 + soft * 2.0, r));
                over(vec3(0.05, 0.055, 0.06), band * uRing * 0.9);
                over(vec3(0.0), (1.0 - inLens) * uSurround);
            }
            if (acc.a < 0.002)
                discard;
            FragColor = vec4(acc.rgb / acc.a, acc.a);
        }
        """;

    private static GlProgram _program;
    private static GlVertexArray _vao;
    private static bool _ready, _failed;
    private static int _locRect, _locCenter, _locRadius, _locSurround, _locRing, _locTint, _locTintA, _locStyle, _locRetColor, _locScale, _locAlpha;

    /// <summary>How visible the lens and reticle are for an aim blend (0 at the hip, 1 from 85% aimed on).</summary>
    public static float LensAlpha(float aimT) => Smooth(0.55f, 0.85f, aimT);

    /// <summary>How opaque the black scope surround is (only for <see cref="GunModel.FullScope"/> optics).</summary>
    public static float SurroundAlpha(WeaponDef d, float aimT) => GunModel.FullScope(d) ? Smooth(0.85f, 0.97f, aimT) : 0f;

    /// <summary>
    /// Draws the optic view for the gun at this aim blend. Returns false when there is nothing to draw
    /// (iron sights, or not aimed far enough), so the caller shows its normal crosshair.
    /// Call with the HUD's ortho camera and blending on; the renderer's own program is restored after.
    /// </summary>
    public static bool Draw(Renderer g, WeaponDef d, float aimT, float w, float h)
    {
        var look = GunModel.Look(d);
        var a = LensAlpha(aimT);
        if (look.Reticle == GunModel.Reticle.None || a <= 0.001f)
            return false;
        var surround = SurroundAlpha(d, aimT);
        var radius = look.LensRadius * h;
        var scale = MathF.Max(0.75f, h / 1080f) * look.Size;
        float cx = w / 2, cy = h / 2;

        if (!EnsureReady())
        {
            Fallback(g, look, a, surround, radius, scale, w, h);
            return true;
        }

        // Only shade where something can be drawn: the lens and its rim, the reticle, or the whole screen for the surround.
        var half = surround > 0 ? MathF.Max(w, h) : radius > 0 ? radius * 1.15f + 4 : 120 * scale;
        float x0 = MathF.Max(-1f, (cx - half) / w * 2 - 1), x1 = MathF.Min(1f, (cx + half) / w * 2 - 1);
        float y0 = MathF.Max(-1f, (cy - half) / h * 2 - 1), y1 = MathF.Min(1f, (cy + half) / h * 2 - 1);

        int lastProgram, lastVao;
        Native.glGetIntegerv(GL_CURRENT_PROGRAM, &lastProgram);
        Native.glGetIntegerv(GL_VERTEX_ARRAY_BINDING, &lastVao);
        _program.Use();
        _vao.Bind();
        Native.glUniform4f(_locRect, x0, y0, x1, y1);
        Native.glUniform2f(_locCenter, cx, cy);
        Native.glUniform1f(_locRadius, radius);
        Native.glUniform1f(_locSurround, surround);
        Native.glUniform1f(_locRing, a);
        Native.glUniform3f(_locTint, look.Tint.X, look.Tint.Y, look.Tint.Z);
        Native.glUniform1f(_locTintA, radius > 0 ? 0.06f * a : 0f);
        Native.glUniform1f(_locStyle, (float)look.Reticle);
        Native.glUniform3f(_locRetColor, look.Color.X, look.Color.Y, look.Color.Z);
        Native.glUniform1f(_locScale, scale);
        Native.glUniform1f(_locAlpha, a);
        Native.glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        new GlProgram((uint)lastProgram).Use();
        new GlVertexArray((uint)lastVao).Bind();
        return true;
    }

    private static bool EnsureReady()
    {
        if (_ready || _failed)
            return _ready;
        try
        {
            using var vs = CreateShader(GlShaderType.Vertex).SetSource(VertexSrc).Compile();
            using var fs = CreateShader(GlShaderType.Fragment).SetSource(FragmentSrc).Compile();
            _program = CreateProgram().Attach(vs, fs).Link();
            DetachShader(_program, vs);
            DetachShader(_program, fs);
            _vao = GenVertexArray(); // core profile needs a bound VAO even with no vertex buffers
            _locRect = Uniform("uRect\0"u8);
            _locCenter = Uniform("uCenter\0"u8);
            _locRadius = Uniform("uRadius\0"u8);
            _locSurround = Uniform("uSurround\0"u8);
            _locRing = Uniform("uRing\0"u8);
            _locTint = Uniform("uTint\0"u8);
            _locTintA = Uniform("uTintA\0"u8);
            _locStyle = Uniform("uStyle\0"u8);
            _locRetColor = Uniform("uRetColor\0"u8);
            _locScale = Uniform("uScale\0"u8);
            _locAlpha = Uniform("uAlpha\0"u8);
            _ready = true;
            Console.WriteLine($"[optics] scope overlay shader ready, GL error 0x{(uint)Native.glGetError():X}");
        }
        catch (Exception ex)
        {
            _failed = true;
            Console.WriteLine($"[optics] scope overlay shader failed, using plain rectangles: {ex.Message}");
        }
        return _ready;
    }

    private static int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    /// <summary>No shader: black bars around a square lens for full scopes, and a plain dot and crosshair.</summary>
    private static void Fallback(Renderer g, GunModel.OpticLook look, float a, float surround, float r, float k, float w, float h)
    {
        float cx = w / 2, cy = h / 2;
        var black = Vector3.Zero;
        if (surround > 0 && r > 0)
        {
            g.Rect(0, 0, cx - r, h, black, surround);
            g.Rect(cx + r, 0, w - (cx + r), h, black, surround);
            g.Rect(cx - r, 0, 2 * r, cy - r, black, surround);
            g.Rect(cx - r, cy + r, 2 * r, h - (cy + r), black, surround);
        }
        if (look.Reticle >= GunModel.Reticle.Duplex)
        {
            g.Rect(cx - r, cy - 1, 2 * r, 2, black, 0.9f * a);
            g.Rect(cx - 1, cy - r, 2, 2 * r, black, 0.9f * a);
        }
        var dot = MathF.Max(3, MathF.Round(3 * k));
        g.Rect(cx - dot / 2, cy - dot / 2, dot, dot, look.Color, a);
    }

    private static float Smooth(float e0, float e1, float x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}
