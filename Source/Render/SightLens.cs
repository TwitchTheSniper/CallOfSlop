using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The view through a sight, drawn as part of the first-person gun: one quad on the optic's lens plane
/// (<see cref="Sights.Lens"/>), posed with the gun, so the sight is one continuous object from the hip to full aim.
///
/// Inside the lens it shows the world exactly as it is behind the lens on screen (a copy of those pixels taken
/// before the gun is drawn), the reticle, a faint glass tint and a vignette; a round eyepiece rim goes around it. So
/// nothing of the gun (rail, receiver, the tube's insides) is ever seen through the glass, while the housing around
/// the lens stays on the gun. Magnified lenses start as dark glass at the hip and clear as you aim; reflex windows
/// start clear (the gun shows through) and hide what's behind them as you aim. The lens's size on screen comes only
/// from where the gun puts it (its eye distance), so it grows smoothly as the sight comes up to the eye.
///
/// Cost: one small blit (the lens's screen rectangle, resolved from the multisampled back buffer) and one quad with
/// one shader. The world is drawn once with one field of view for the whole screen (no second "picture-in-picture"
/// world pass). The pass logs its GPU time and the world pass's next to it (<c>[optics]</c> lines).
///
/// If the copy can't be made (old driver), <see cref="SeeThrough"/> turns off and the optics fall back to the old
/// fade-out of the housing with the reticle and tint on the lens; if the shader can't be built, nothing is drawn and
/// the HUD keeps its crosshair.
/// </summary>
public static unsafe class SightLens
{
    private const string VertexSrc = """
        #version 410 core
        uniform mat4 uMVP;
        uniform vec2 uQuad; // half size of the quad on the lens plane (metres)
        out vec2 vP;
        void main()
        {
            vec2 c = vec2((gl_VertexID & 1) != 0 ? 1.0 : -1.0, (gl_VertexID & 2) != 0 ? 1.0 : -1.0) * uQuad;
            vP = c;
            gl_Position = uMVP * vec4(c, 0.0, 1.0);
        }
        """;

    // Reticles (GunModel.Reticle): 1 dot, 2 holo ring, 3 chevron, 4 horseshoe, 5 circle-dot, 6 duplex, 7 fine
    // crosshair, 8 stadia crosshair, 9 mil-dot, 10 rangefinder (a dot between brackets, a range ladder). Reticle sizes are in pixels (uScale = px per design unit), measured from
    // uRet, the screen point the optic's axis points at (a reticle at infinity), so they're crisp at any resolution.
    private const string FragmentSrc = """
        #version 410 core
        in vec2 vP;
        uniform sampler2D uWorld;
        uniform float uWorldA;   // how much the view behind the lens replaces the gun
        uniform float uGlassA;   // dark glass under it (magnified lenses)
        uniform vec2 uLens;      // half width, half height (metres)
        uniform float uRim;      // eyepiece rim outer radius (metres; <= lens radius: none)
        uniform float uRound;
        uniform float uSquareRim; // the rim is a square of half size uRim (square eyecups)
        uniform float uPx;       // pixels per metre on the lens plane
        uniform vec2 uRet;       // reticle centre (pixels)
        uniform float uStyle;
        uniform vec3 uRetColor;
        uniform float uScale;
        uniform float uRetA;
        uniform vec3 uTint;
        uniform float uTintA;
        uniform float uVig;
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
            float px = 1.0 / uPx; // one pixel in metres
            float r = length(vP);
            float R = uLens.y;
            float inLens, rim, rn;
            if (uRound > 0.5)
            {
                inLens = clamp((R - r) / px + 0.5, 0.0, 1.0);
                float rd = uSquareRim > 0.5 ? max(abs(vP.x), abs(vP.y)) : r;
                rim = uRim > R ? clamp((uRim - rd) / px + 0.5, 0.0, 1.0) * (1.0 - inLens) : 0.0;
                rn = r / R;
            }
            else
            {
                vec2 q = abs(vP) - uLens;
                inLens = clamp(-max(q.x, q.y) / px + 0.5, 0.0, 1.0);
                rim = 0.0;
                rn = max(abs(vP.x) / uLens.x, abs(vP.y) / uLens.y);
            }
            if (inLens + rim < 0.002)
                discard;

            // Glass (a soft reflection across it), then the view behind the lens, tint and vignette.
            float hl = (1.0 - smoothstep(0.0, 0.9, length(vP / R - vec2(-0.35, 0.45)))) * 0.7;
            over(mix(vec3(0.02, 0.025, 0.035), vec3(0.17, 0.21, 0.27), hl), uGlassA);
            over(texelFetch(uWorld, ivec2(gl_FragCoord.xy), 0).rgb, uWorldA);
            over(uTint, uTintA);
            over(vec3(0.0), smoothstep(0.7, 1.0, rn) * uVig * uWorldA);

            // Reticle
            vec2 p = gl_FragCoord.xy - uRet;
            float d = length(p);
            float s = uScale;
            int st = int(uStyle + 0.5);
            float hw = max(0.5, 0.6 * s);      // etched hairline half width
            float ext = max(R * uPx, 40.0 * s); // how far crosshair lines reach (the lens radius in pixels)
            float etch = 0.0, lit = 0.0;
            if (st == 1)
                lit = max(disc(p, 2.0 * s), 0.3 * exp(-d / (2.0 * s)));
            else if (st == 2)
                lit = max(disc(p, 1.6 * s), ring(p, 22.0 * s, max(0.6, 0.9 * s)));
            else if (st == 3)
            {
                lit = max(seg(p, vec2(0.0), vec2(-9.0 * s, -9.0 * s), 1.2 * s), seg(p, vec2(0.0), vec2(9.0 * s, -9.0 * s), 1.2 * s));
                etch = seg(p, vec2(0.0, -16.0 * s), vec2(0.0, -64.0 * s), hw);
                for (int i = 1; i <= 3; i++)
                {
                    float y = -(16.0 + 12.0 * float(i)) * s, hl2 = (6.0 - float(i)) * s;
                    etch = max(etch, seg(p, vec2(-hl2, y), vec2(hl2, y), hw));
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
                    float y = -(18.0 + 9.0 * float(i)) * s, hl2 = (6.0 - float(i)) * s;
                    lit = max(lit, seg(p, vec2(-hl2, y), vec2(hl2, y), max(0.5, 0.7 * s)));
                }
            }
            else if (st == 10)
            {
                // Rangefinder: a lit dot between two lit brackets, etched hairlines out to the rim and a range ladder below
                float bw = max(0.6, 0.9 * s);
                lit = disc(p, 1.6 * s);
                for (int k = -1; k <= 1; k += 2)
                {
                    float x = 14.0 * s * float(k);
                    lit = max(lit, seg(p, vec2(x, -10.0 * s), vec2(x, 10.0 * s), bw));
                    lit = max(lit, seg(p, vec2(x, 10.0 * s), vec2(x - 4.0 * s * float(k), 10.0 * s), bw));
                    lit = max(lit, seg(p, vec2(x, -10.0 * s), vec2(x - 4.0 * s * float(k), -10.0 * s), bw));
                }
                etch = max(seg(p, vec2(-ext, 0.0), vec2(-18.0 * s, 0.0), hw), seg(p, vec2(18.0 * s, 0.0), vec2(ext, 0.0), hw));
                etch = max(etch, seg(p, vec2(0.0, 14.0 * s), vec2(0.0, ext), hw));
                etch = max(etch, seg(p, vec2(0.0, -14.0 * s), vec2(0.0, -ext), hw));
                for (int i = 1; i <= 4; i++)
                {
                    float y = -(14.0 + 9.0 * float(i)) * s, hl2 = (7.0 - float(i)) * s;
                    etch = max(etch, seg(p, vec2(-hl2, y), vec2(hl2, y), hw));
                }
            }
            else if (st >= 6)
            {
                // Crosshairs: hairlines in the middle, thick posts towards the rim (none on the fine crosshair)
                float post = st == 7 ? 0.0 : ext * (st == 6 ? 0.33 : (st == 8 ? 0.5 : 0.55));
                float thin = post > 0.0 ? post : ext * 2.0;
                float outer = ext * 2.0;
                float thick = 2.6 * s;
                etch = max(seg(p, vec2(-thin, 0.0), vec2(thin, 0.0), hw), seg(p, vec2(0.0, -thin), vec2(0.0, thin), hw));
                if (post > 0.0)
                {
                    etch = max(etch, max(seg(p, vec2(post, 0.0), vec2(outer, 0.0), thick), seg(p, vec2(-outer, 0.0), vec2(-post, 0.0), thick)));
                    etch = max(etch, max(seg(p, vec2(0.0, post), vec2(0.0, outer), thick), seg(p, vec2(0.0, -outer), vec2(0.0, -post), thick)));
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
            over(vec3(0.02), etch * 0.92 * uRetA);
            over(uRetColor, lit * uRetA);
            acc *= inLens;

            // Eyepiece rim: dark metal, lit from above, with a bevel next to the glass.
            if (rim > 0.0)
            {
                float bevel = 1.0 - smoothstep(R, R + (uRim - R) * 0.35, r);
                vec3 metal = vec3(0.075, 0.08, 0.09) * (0.75 + 0.35 * clamp(vP.y / uRim * 0.5 + 0.5, 0.0, 1.0)) + vec3(0.05) * bevel;
                acc += vec4(metal, 1.0) * rim;
            }

            if (acc.a < 0.002)
                discard;
            FragColor = vec4(acc.rgb / acc.a, min(acc.a, 1.0));
        }
        """;

    private static GlProgram _program;
    private static GlVertexArray _vao;
    private static bool _ready, _failed;
    private static int _locSquareRim;
    private static int _locMvp, _locQuad, _locWorld, _locWorldA, _locGlassA, _locLens, _locRim, _locRound, _locPx, _locRet, _locStyle, _locRetColor, _locScale, _locRetA, _locTint, _locTintA, _locVig;

    // The copy of the world behind the lens (a screen-sized texture; only the lens's rectangle is filled each frame).
    private static uint _fbo, _tex;
    private static int _texW, _texH;
    private static bool _copyFailed, _copyChecked, _rgbFallback;

    /// <summary>
    /// True while the view through lenses can be shown (the copy works): magnified optics then keep their whole housing.
    /// False after the copy failed once: every optic falls back to fading its housing out (<see cref="Sights.Cutaway"/>).
    /// </summary>
    public static bool SeeThrough => !_copyFailed && !_failed;

    /// <summary>The housing cut-away to draw the first-person gun with (see <see cref="Sights.Cutaway"/>).</summary>
    public static float Cutaway(WeaponDef d, float aimT) => Sights.Cutaway(d, aimT, SeeThrough);

    /// <summary>True when the lens's reticle replaces the HUD crosshair.</summary>
    public static bool HidesCrosshair(WeaponDef d, float aimT) => !_failed && Sights.HasLens(d) && aimT > 0.55f;

    /// <summary>The last lens drawn: where it was and how much of the screen it covered (the smoke test logs these).</summary>
    public static Sights.LensView LastView { get; private set; }

    /// <inheritdoc cref="LastView"/>
    public static float LastCoverage { get; private set; }

    // Per-frame state between Begin and Draw
    private static bool _pending;
    private static Matrix4x4 _mvp;
    private static Vector2 _quad, _ret, _lens;
    private static float _rim, _px, _worldA, _glassA, _retA, _scale, _tintA, _vig;
    private static bool _round, _squareRim;
    private static SightSpec _spec = new();
    private static OpticKind _kind;

    /// <summary>
    /// Call right before the first-person gun is drawn (the world is in the back buffer, the gun isn't yet), with the gun's
    /// pose for this frame and the viewmodel projection: works out where the lens is and copies the world behind it.
    /// Returns true if <see cref="Draw"/> has something to draw after the gun.
    /// </summary>
    public static bool Begin(WeaponDef d, float aimT, in Matrix4x4 pose, in Matrix4x4 vmProj, int width, int height)
    {
        _pending = false;
        if (_failed || width <= 0 || height <= 0 || Sights.Lens(d) is not { } lens)
            return false;
        var aspect = width / (float)height;
        var view = Sights.Project(d, pose, aspect);
        LastView = view;
        LastCoverage = Sights.Coverage(view, aspect);
        if (!view.Valid || !EnsureReady())
            return false;
        var t0 = Stopwatch.GetTimestamp();
        var spec = Sights.Spec(d);
        _spec = spec;
        _kind = d.Optic;
        _mvp = Matrix4x4.CreateTranslation(lens.Center) * pose * vmProj;
        _round = lens.Round;
        _squareRim = lens.SquareRim;
        _lens = new Vector2(lens.HalfW, lens.HalfH);
        _rim = lens.Round ? MathF.Max(lens.Rim, lens.HalfH) : 0f;
        _quad = lens.Round ? new Vector2(_rim) : _lens;
        _px = view.HalfH * height * 0.5f / lens.HalfH;
        _ret = new Vector2((view.Reticle.X + 1) * 0.5f * width, (view.Reticle.Y + 1) * 0.5f * height);
        _worldA = Sights.WorldOpacity(d, aimT, SeeThrough);
        _glassA = Sights.GlassAlpha(d, aimT, SeeThrough);
        _retA = Sights.ReticleAlpha(d, aimT);
        _tintA = spec.TintAlpha * MathF.Max(_worldA, SeeThrough ? 0f : _retA);
        _vig = spec.Vignette;
        // The reticle is its design size at full aim and grows with the lens as a magnified sight comes up to the eye.
        _scale = MathF.Max(0.75f, height / 1080f) * spec.ReticleSize;
        if (Sights.Magnified(d))
        {
            var full = Sights.ProjectAimed(d, aspect);
            if (full.Valid && full.HalfH > 0)
                _scale *= Math.Clamp(view.HalfH / full.HalfH, 0.05f, 1.5f);
        }

        Timer.Stamp(Timer.CopyStart);
        if (_worldA > 0.001f && SeeThrough)
            Capture(ScreenRect(width, height), width, height);
        Timer.Stamp(Timer.CopyEnd);
        Timer.CpuWorldMs = (t0 - Timer.WorldStartCpu) * 1000.0 / Stopwatch.Frequency;
        _pending = true;
        return true;
    }

    /// <summary>Draws the lens prepared by <see cref="Begin"/>, over the gun (depth-tested against it, so a hand in front still covers it).</summary>
    public static void Draw(Renderer g)
    {
        if (!_pending)
            return;
        _pending = false;
        Timer.Stamp(Timer.DrawStart);
        _program.Use();
        _vao.Bind();
        var m = _mvp;
        Native.glUniformMatrix4fv(_locMvp, 1, false, &m.M11);
        Native.glUniform2f(_locQuad, _quad.X, _quad.Y);
        Native.glUniform1i(_locWorld, 1);
        Native.glUniform1f(_locWorldA, _worldA);
        Native.glUniform1f(_locGlassA, _glassA);
        Native.glUniform2f(_locLens, _lens.X, _lens.Y);
        Native.glUniform1f(_locRim, _rim);
        Native.glUniform1f(_locRound, _round ? 1f : 0f);
        Native.glUniform1f(_locSquareRim, _squareRim ? 1f : 0f);
        Native.glUniform1f(_locPx, MathF.Max(_px, 1e-3f));
        Native.glUniform2f(_locRet, _ret.X, _ret.Y);
        Native.glUniform1f(_locStyle, (float)_spec.Reticle);
        Native.glUniform3f(_locRetColor, _spec.ReticleColor.X, _spec.ReticleColor.Y, _spec.ReticleColor.Z);
        Native.glUniform1f(_locScale, _scale);
        Native.glUniform1f(_locRetA, _retA);
        Native.glUniform3f(_locTint, _spec.Tint.X, _spec.Tint.Y, _spec.Tint.Z);
        Native.glUniform1f(_locTintA, _tintA);
        Native.glUniform1f(_locVig, _vig);
        Native.glActiveTexture(GL_TEXTURE1);
        Native.glBindTexture(GL_TEXTURE_2D, _tex);
        g.SetBlend(true);
        DepthMask(false);
        Native.glDrawArrays(GL_TRIANGLE_STRIP, 0, 4);
        DepthMask(true);
        g.SetBlend(false);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        g.Rebind(); // the renderer's program, cube and texture array (unit 0) again
        Timer.Stamp(Timer.DrawEnd);
        Timer.EndFrame(_kind);
    }

    /// <summary>Call at the start of the world pass: the lens pass logs the world pass's cost next to its own.</summary>
    public static void MarkWorldStart() => Timer.WorldStart();

    /// <summary>The lens quad's bounding rectangle on screen (pixels), or the whole screen if part of it is behind the eye.</summary>
    private static (int X0, int Y0, int X1, int Y1) ScreenRect(int w, int h)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (var i = 0; i < 4; i++)
        {
            var c = new Vector4((i & 1) != 0 ? _quad.X : -_quad.X, (i & 2) != 0 ? _quad.Y : -_quad.Y, 0, 1);
            var p = Vector4.Transform(c, _mvp);
            if (p.W <= 1e-5f)
                return (0, 0, w, h);
            float sx = (p.X / p.W + 1) * 0.5f * w, sy = (p.Y / p.W + 1) * 0.5f * h;
            (x0, y0, x1, y1) = (MathF.Min(x0, sx), MathF.Min(y0, sy), MathF.Max(x1, sx), MathF.Max(y1, sy));
        }
        return (Math.Clamp((int)x0 - 2, 0, w), Math.Clamp((int)y0 - 2, 0, h), Math.Clamp((int)x1 + 3, 0, w), Math.Clamp((int)y1 + 3, 0, h));
    }

    /// <summary>Copies (and resolves, if the back buffer is multisampled) the given rectangle of the current framebuffer into the lens texture.</summary>
    private static void Capture((int X0, int Y0, int X1, int Y1) r, int w, int h)
    {
        if (r.X1 <= r.X0 || r.Y1 <= r.Y0)
            return;
        int drawFb, readFb;
        Native.glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &drawFb);
        Native.glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &readFb);
        if (!EnsureTarget(w, h, drawFb, readFb))
            return;
        if (!_copyChecked)
            while (GlDebug.Error() != 0) { } // only our own error counts below
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, new GlFramebuffer((uint)drawFb));
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, new GlFramebuffer(_fbo));
        Native.glBlitFramebuffer(r.X0, r.Y0, r.X1, r.Y1, r.X0, r.Y0, r.X1, r.Y1, GL_COLOR_BUFFER_BIT, GL_NEAREST);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, new GlFramebuffer((uint)drawFb));
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, new GlFramebuffer((uint)readFb));
        if (_copyChecked)
            return;
        var err = GlDebug.Error();
        if (err == 0)
        {
            _copyChecked = true;
            Console.WriteLine($"[optics] lens view copy works ({(_rgbFallback ? "RGB8" : "RGBA8")} {w}x{h}); magnified optics keep their housing and show the view through the lens");
            return;
        }
        if (!_rgbFallback)
        {
            // A multisampled back buffer can only be resolved into the same format; try RGB8 once.
            Console.WriteLine($"[optics] lens view copy failed with GL error 0x{err:X} into RGBA8, trying RGB8");
            _rgbFallback = true;
            _texW = _texH = 0;
            return;
        }
        _copyFailed = true;
        Console.WriteLine($"[optics] lens view copy failed with GL error 0x{err:X}; falling back to fading optic housings out (no see-through lens)");
    }

    private static bool EnsureTarget(int w, int h, int drawFb, int readFb)
    {
        if (_texW == w && _texH == h && _fbo != 0)
            return true;
        if (_fbo == 0)
        {
            GlFramebuffer fb = default;
            Native.glGenFramebuffers(1, &fb);
            _fbo = fb.Id;
            GlTexture t = default;
            Native.glGenTextures(1, &t);
            _tex = t.Id;
        }
        Native.glActiveTexture(GL_TEXTURE1);
        Native.glBindTexture(GL_TEXTURE_2D, _tex);
        Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)(_rgbFallback ? GL_RGB8 : GL_RGBA8), w, h, 0, _rgbFallback ? GL_RGB : GL_RGBA, GL_UNSIGNED_BYTE, null);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glBindTexture(GL_TEXTURE_2D, 0);
        Native.glActiveTexture(GL_TEXTURE0);
        Native.glBindFramebuffer(GL_FRAMEBUFFER, new GlFramebuffer(_fbo));
        Native.glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _tex, 0);
        var status = (uint)Native.glCheckFramebufferStatus(GL_FRAMEBUFFER);
        Native.glBindFramebuffer(GL_DRAW_FRAMEBUFFER, new GlFramebuffer((uint)drawFb));
        Native.glBindFramebuffer(GL_READ_FRAMEBUFFER, new GlFramebuffer((uint)readFb));
        if (status != GL_FRAMEBUFFER_COMPLETE)
        {
            _copyFailed = true;
            Console.WriteLine($"[optics] lens view target incomplete (0x{status:X}); falling back to fading optic housings out");
            return false;
        }
        (_texW, _texH) = (w, h);
        return true;
    }

    private static bool EnsureReady()
    {
        if (_ready || _failed)
            return _ready;
        try
        {
            using var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
            using var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
            _program = CreateProgram().Attach(vs, fs).Link();
            GlDebug.Label(GL_PROGRAM, _program.Id, "SightLens");
            DetachShader(_program, vs);
            DetachShader(_program, fs);
            _vao = GenVertexArray(); // core profile needs a bound VAO even with no vertex buffers
            _locMvp = Uniform("uMVP\0"u8);
            _locQuad = Uniform("uQuad\0"u8);
            _locWorld = Uniform("uWorld\0"u8);
            _locWorldA = Uniform("uWorldA\0"u8);
            _locGlassA = Uniform("uGlassA\0"u8);
            _locLens = Uniform("uLens\0"u8);
            _locRim = Uniform("uRim\0"u8);
            _locRound = Uniform("uRound\0"u8);
            _locSquareRim = Uniform("uSquareRim\0"u8);
            _locPx = Uniform("uPx\0"u8);
            _locRet = Uniform("uRet\0"u8);
            _locStyle = Uniform("uStyle\0"u8);
            _locRetColor = Uniform("uRetColor\0"u8);
            _locScale = Uniform("uScale\0"u8);
            _locRetA = Uniform("uRetA\0"u8);
            _locTint = Uniform("uTint\0"u8);
            _locTintA = Uniform("uTintA\0"u8);
            _locVig = Uniform("uVig\0"u8);
            Timer.Init();
            _ready = true;
            Console.WriteLine($"[optics] sight lens shader ready, GPU timer {(Timer.Available ? "on" : "unavailable")}, GL error 0x{GlDebug.Error():X}");
        }
        catch (Exception ex)
        {
            _failed = true;
            Console.WriteLine($"[optics] sight lens shader failed, optics fade out and the HUD keeps its crosshair: {ex.Message}");
        }
        return _ready;
    }

    private static int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    /// <summary>
    /// GPU timestamps around the lens pass (copy + quad) and the world pass before it, a few frames in flight, read back
    /// when ready and logged every few seconds while a lens is on screen. Timestamps, not GL_TIME_ELAPSED, because the
    /// mesh, prop and foliage renderers run their own elapsed-time queries inside the world pass.
    /// </summary>
    private static class Timer
    {
        public const int World = 0, CopyStart = 1, CopyEnd = 2, DrawStart = 3, DrawEnd = 4, Stamps = 5, Ring = 4;
        private static readonly GlQuery[] Queries = new GlQuery[Stamps * Ring];
        private static readonly bool[] Pending = new bool[Ring];
        private static readonly bool[] HasWorld = new bool[Ring];
        public static bool Available;
        private static int _frame;
        private static bool _active, _worldMarked;
        public static long WorldStartCpu;
        public static double CpuWorldMs;
        private static double _lensMs, _copyMs, _worldMs, _cpuWorldSum, _logAt;
        private static int _samples, _cpuSamples;

        public static void Init()
        {
            if (Native.glGenQueries == null || Native.glQueryCounter == null || Native.glGetQueryObjectui64v == null || Native.glGetQueryObjectiv == null)
                return;
            fixed (GlQuery* q = Queries)
                Native.glGenQueries(Queries.Length, q);
            Available = true;
        }

        private static int Slot => _frame % Ring;

        public static void WorldStart()
        {
            WorldStartCpu = Stopwatch.GetTimestamp();
            _worldMarked = false;
            if (!Available || !_active)
                return; // only time the world while a lens is being drawn
            Collect();
            if (Pending[Slot])
                return;
            Native.glQueryCounter(Queries[Slot * Stamps + World].Id, GL_TIMESTAMP);
            _worldMarked = true;
        }

        public static void Stamp(int which)
        {
            if (!Available || Pending[Slot])
                return;
            Native.glQueryCounter(Queries[Slot * Stamps + which].Id, GL_TIMESTAMP);
        }

        public static void EndFrame(OpticKind kind)
        {
            _active = true;
            _cpuWorldSum += CpuWorldMs;
            _cpuSamples++;
            if (Available && !Pending[Slot])
            {
                Pending[Slot] = true;
                HasWorld[Slot] = _worldMarked;
            }
            _frame++;
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (_logAt == 0)
                _logAt = now + 3;
            if (now < _logAt)
                return;
            _logAt = now + 5;
            Collect();
            var gpu = _samples > 0
                ? $"lens pass {_lensMs / _samples:0.000} ms GPU (copy {_copyMs / _samples:0.000} ms), world pass {_worldMs / _samples:0.00} ms GPU"
                : "GPU time unavailable";
            Console.WriteLine($"[optics] {kind} lens on screen: {gpu}, world pass {_cpuWorldSum / Math.Max(1, _cpuSamples):0.00} ms CPU, coverage {LastCoverage * 100:0}% "
                + $"(a picture-in-picture lens would add a second world pass of about that cost; not used)");
            _lensMs = _copyMs = _worldMs = _cpuWorldSum = 0;
            _samples = _cpuSamples = 0;
        }

        private static void Collect()
        {
            for (var s = 0; s < Ring; s++)
            {
                if (!Pending[s])
                    continue;
                int available;
                Native.glGetQueryObjectiv(Queries[s * Stamps + DrawEnd].Id, GL_QUERY_RESULT_AVAILABLE, &available);
                if (available == 0)
                    continue;
                ulong w = 0, c0, c1, d0, d1;
                if (HasWorld[s]) // a query never stamped is not a query object yet: reading it is GL_INVALID_OPERATION (found by KHR_debug)
                    Native.glGetQueryObjectui64v(Queries[s * Stamps + World].Id, GL_QUERY_RESULT, &w);
                Native.glGetQueryObjectui64v(Queries[s * Stamps + CopyStart].Id, GL_QUERY_RESULT, &c0);
                Native.glGetQueryObjectui64v(Queries[s * Stamps + CopyEnd].Id, GL_QUERY_RESULT, &c1);
                Native.glGetQueryObjectui64v(Queries[s * Stamps + DrawStart].Id, GL_QUERY_RESULT, &d0);
                Native.glGetQueryObjectui64v(Queries[s * Stamps + DrawEnd].Id, GL_QUERY_RESULT, &d1);
                Pending[s] = false;
                var copy = (c1 - c0) / 1e6;
                _copyMs += copy;
                _lensMs += copy + (d1 - d0) / 1e6;
                if (HasWorld[s] && c0 > w)
                    _worldMs += (c0 - w) / 1e6;
                _samples++;
            }
        }
    }
}
