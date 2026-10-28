using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws a map's sky (<see cref="SkySettings"/>) and keeps the scene's light in step with it.
///
/// How it draws: one full-screen triangle at the far plane (z = w, so depth 1.0), depth-tested with LEQUAL and no depth
/// writes, at the END of the opaque world pass (<see cref="Draw"/> is the last call of Program.DrawArena). Early depth
/// testing then skips every pixel the world already covered, so the sky only shades the pixels you actually see it in;
/// players, tracers, smoke and the viewmodel are drawn after it as before. The sight lens copies the back buffer after
/// the world pass, so the view through a scope includes the sky with no extra work. Maps without a sky draw nothing
/// here and keep their flat clear colour.
///
/// Procedural sky, all in one fragment shader: the zenith / horizon / ground gradient (SkyGradient, shared with the fog
/// in <see cref="Lighting.Glsl"/>), stars (one hash per pixel, night skies), the moon (lit by the sun, so it has
/// phases), the sun disc and glow, and two cloud layers drawn from one small tiling noise texture (RGBA = four octaves,
/// so one fetch is a whole fBm): cumulus with a large-scale coverage variation and soft shading, and thin streaky
/// cirrus above. The clouds drift with the wind; offsets are wrapped on the CPU in double precision so they never lose
/// precision on long sessions. Image skies sample a cube map (6 PNG faces) or an equirectangular PNG / HDR panorama
/// (HDR tone mapped with an exposure).
///
/// Cost: a handful of ALU ops and 3-4 texture fetches per sky pixel at High, 1 fetch at Low (no clouds or stars), and
/// zero CPU allocations per frame. GPU time is measured with timestamp queries (the mesh, prop and foliage renderers
/// use GL_TIME_ELAPSED queries of their own, which can't nest) and logged as a "[sky]" line every few seconds.
/// If the shader can't be built, the sky is skipped (the flat clear colour shows) and the light still follows the sky.
/// </summary>
public static unsafe class SkyRenderer
{
    private const string VertexSrc = """
        #version 410 core
        uniform mat4 uInvVP;   // inverse of (view without translation) * projection
        out vec4 vRay;
        void main()
        {
            // One triangle covering the screen: (-1,-1), (3,-1), (-1,3), on the far plane.
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)) * 2.0 - 1.0;
            vRay = uInvVP * vec4(p, 1.0, 1.0);
            gl_Position = vec4(p, 1.0, 1.0);
        }
        """;

    /// <summary>The sky fragment shader (after <see cref="Lighting.Glsl"/>, which supplies SkyGradient and the sun).</summary>
    public const string FragmentBody = """

        in vec4 vRay;
        uniform float uMode;      // 0 procedural, 1 cube map, 2 panorama
        uniform float uQuality;   // 0 low (no clouds or stars), 1 high
        uniform float uHdr;
        uniform float uExposure;
        uniform vec2 uRot;        // cos, sin of the image rotation
        uniform vec3 uSunPos;     // towards the real sun
        uniform vec4 uSunDisc;    // disc colour, cos(radius) (0: no disc)
        uniform vec3 uSunGlow;    // halo colour * strength
        uniform vec3 uMoonPos;
        uniform vec4 uMoon;       // colour, cos(radius) (0: no moon)
        uniform float uStars;
        uniform float uSpace;     // 1 on a planet map: no horizon, stars and the sun and moon all round
        uniform float uTime;
        uniform vec4 uCloud;      // coverage threshold, density, uv per unit of xz/y, cirrus amount
        uniform vec3 uCloudCol;
        uniform vec3 uCloudShade;
        uniform vec4 uWind;       // cumulus offset, coverage-variation offset
        uniform vec2 uWind2;      // cirrus offset
        uniform sampler2D uNoise; // RGBA = value noise with 4, 8, 16 and 32 cells per tile
        uniform samplerCube uCube;
        uniform sampler2D uPano;
        out vec4 FragColor;

        float Hash13(vec3 p)
        {
            p = fract(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return fract((p.x + p.y) * p.z);
        }

        void main()
        {
            vec3 d = normalize(vRay.xyz / vRay.w);
            vec3 col;
            float above = max(smoothstep(-0.015, 0.015, d.y), uSpace);
            if (uMode > 0.5)
            {
                vec3 r = vec3(d.x * uRot.x - d.z * uRot.y, d.y, d.x * uRot.y + d.z * uRot.x);
                vec3 t = uMode < 1.5 ? texture(uCube, r).rgb
                                     : texture(uPano, vec2(atan(r.x, -r.z) * 0.15915494 + 0.5, acos(clamp(r.y, -1.0, 1.0)) * 0.31830989)).rgb;
                col = uHdr > 0.5 ? vec3(1.0) - exp(-t * uExposure) : t * uExposure;
            }
            else
            {
                col = SkyGradient(d);

                // Stars: a sparse hash on a grid of directions, twinkling a little; behind everything else.
                if (uQuality > 0.5 && uStars > 0.0 && (d.y > 0.0 || uSpace > 0.5))
                {
                    vec3 sp = d * 260.0;
                    float h = Hash13(floor(sp));
                    if (h > 0.992)
                    {
                        float r = length(fract(sp) - 0.5);
                        float tw = 0.7 + 0.3 * sin(uTime * (1.3 + h * 4.0) + h * 300.0);
                        float b = 0.25 + 0.75 * fract(h * 917.0);
                        col += vec3(0.88, 0.93, 1.0) * (smoothstep(0.36, 0.06, r) * b * tw * uStars * max(smoothstep(0.0, 0.3, d.y), uSpace));
                    }
                }

                // Moon: a sphere lit by the sun, so its phase follows where the sun is.
                if (uMoon.w > 0.0)
                {
                    float md = dot(d, uMoonPos);
                    col += uMoon.rgb * (0.05 * pow(max(md, 0.0), 40.0) + 0.12 * pow(max(md, 0.0), 900.0)) * above;
                    if (md > uMoon.w - 0.001)
                    {
                        float sinR = sqrt(max(1.0 - uMoon.w * uMoon.w, 1e-8));
                        vec3 q = (d - uMoonPos * md) / sinR;
                        float r2 = dot(q, q);
                        vec3 n = q - uMoonPos * sqrt(max(1.0 - r2, 0.0));
                        float lit = clamp(dot(n, uSunPos) * 1.4, 0.0, 1.0);
                        float maria = texture(uNoise, q.xz * 0.35 + 0.5).g;
                        vec3 mc = uMoon.rgb * (0.07 + 0.93 * lit) * (0.82 + 0.36 * maria);
                        float a = 1.0 - smoothstep(0.94, 1.0, sqrt(r2));
                        col = mix(col, mc, a * above);
                    }
                }

                // Sun: halo, then the disc.
                float sd = max(dot(d, uSunPos), 0.0);
                col += uSunGlow * (pow(sd, 700.0) * 0.9 + pow(sd, 48.0) * 0.22 + pow(sd, 6.0) * 0.06) * above;
                if (uSunDisc.w > 0.0)
                {
                    float aa = max(fwidth(sd), 1e-5);
                    float disc = smoothstep(uSunDisc.w - aa, uSunDisc.w + aa, sd) * above;
                    col = mix(col, max(col, uSunDisc.rgb), disc); // never darker than the glow round it
                }

                // Clouds: a cumulus layer and thin cirrus above it, on flat layers seen in perspective.
                if (uQuality > 0.5 && uCloud.y > 0.0 && d.y > 0.0)
                {
                    float fade = smoothstep(0.0, 0.16, d.y);
                    vec2 p = d.xz / (d.y + 0.08) * uCloud.z;
                    float n = dot(texture(uNoise, p + uWind.xy), vec4(0.50, 0.25, 0.15, 0.10));
                    n = n * 0.72 + texture(uNoise, p * 0.23 + uWind.zw).r * 0.28;
                    float thr = uCloud.x;
                    float a = smoothstep(thr, thr + 0.09, n) * uCloud.y * fade;
                    float thick = smoothstep(thr, thr + 0.30, n);
                    vec3 cc = mix(uCloudCol, uCloudShade, thick * 0.85);
                    cc += uSunDisc.rgb * (pow(sd, 10.0) * (1.0 - thick) * 0.35); // light through thin edges near the sun
                    cc = mix(cc, SkyGradient(d), (1.0 - smoothstep(0.0, 0.35, d.y)) * 0.55); // distance haze
                    col = mix(col, cc, a);

                    vec2 p2 = d.xz / (d.y + 0.05) * (uCloud.z * 0.3) + uWind2;
                    float c2 = dot(texture(uNoise, vec2(p2.x * 0.8 + p2.y * 0.3, p2.y * 1.6)).gb, vec2(0.65, 0.35));
                    float a2 = smoothstep(0.6, 0.85, c2) * uCloud.w * fade * (1.0 - a);
                    col = mix(col, mix(uCloudCol, SkyGradient(d), 0.35), a2);
                }
            }
            // A touch of noise so 8-bit gradients don't band.
            col += (Hash13(vec3(gl_FragCoord.xy, 7.0)) - 0.5) / 255.0;
            FragColor = vec4(col, 1.0);
        }
        """;

    private static readonly string FragmentSrc = "#version 410 core\n" + Lighting.Glsl + FragmentBody;

    /// <summary>The uniforms the sky shader declares (besides <see cref="Lighting.UniformNames"/>); the self-test checks both sides agree.</summary>
    public static readonly string[] UniformNames =
        ["uInvVP", "uMode", "uQuality", "uHdr", "uExposure", "uRot", "uSunPos", "uSunDisc", "uSunGlow", "uMoonPos", "uMoon", "uStars", "uSpace", "uTime",
         "uCloud", "uCloudCol", "uCloudShade", "uWind", "uWind2", "uNoise", "uCube", "uPano"];

    /// <summary>The vertex shader source (for the self-test's shader check).</summary>
    public static string VertexSource => VertexSrc;

    /// <summary>The full fragment shader source.</summary>
    public static string FragmentSource => FragmentSrc;

    /// <summary>Texture units the sky uses (unit 0 is the box renderer's texture array, 1 the sight lens copy).</summary>
    private const uint NoiseUnit = 5, CubeUnit = 6, PanoUnit = 7;

    /// <summary>Noise texture size; each channel tiles (4, 8, 16, 32 cells across).</summary>
    public const int NoiseSize = 256;

    private static GlProgram _program;
    private static GlVertexArray _vao;
    private static uint _noise;
    private static bool _ready, _failed, _checkedFirstDraw;
    private static int _locInvVP, _locMode, _locQuality, _locHdr, _locExposure, _locRot, _locSunPos, _locSunDisc, _locSunGlow, _locMoonPos, _locMoon,
        _locStars, _locSpace, _locTime, _locCloud, _locCloudCol, _locCloudShade, _locWind, _locWind2;

    // Image textures, a few kept (thumbnails of other image maps mustn't re-upload the current one every frame).
    private static readonly List<(SkyImage Image, uint Texture)> ImageTextures = new();
    private const int MaxImageTextures = 3;

    /// <summary>True when the shader failed to build (the sky is then skipped; the light still follows the sky).</summary>
    public static bool Failed => _failed;

    // ---- Light ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gives every scene shader <paramref name="map"/>'s light: its sky's sun, ambient and gradient fog, or the classic
    /// light for maps without a sky, plus the horizon fade past a flat shaped map's outline (<see cref="Map.OuterFog"/>).
    /// Nothing happens when it's already set, so calling it every frame is free.
    /// </summary>
    public static void Use(Map map) => Lighting.Use(map.Light);

    /// <summary>Back to the classic light (gun previews and menus look the same on every map).</summary>
    public static void UseDefaultLight()
    {
        Lighting.Use(Lighting.Default);
        LightGrid.Off(); // gun pictures and previews: no map's baked light either
    }

    // ---- Drawing --------------------------------------------------------------------------------------------------

    /// <summary>
    /// Draws <paramref name="map"/>'s sky behind everything drawn so far (call after the opaque world, with the world
    /// camera still set). Does nothing for maps without a sky. <paramref name="timed"/>: count it in the [sky] GPU stats
    /// (off for thumbnails).
    /// </summary>
    public static void Draw(Renderer g, Map map, SkyQuality quality, double time, bool timed = true)
    {
        if (map.Skybox is not { } s || g.Recorder != null || !EnsureReady())
            return;
        var image = s.Type != SkyType.Procedural && s.Loaded != null ? ImageTexture(s.Loaded) : 0;
        if (s.Type != SkyType.Procedural && image == 0)
            return;
        var view = g.View;
        view.M41 = view.M42 = view.M43 = 0;
        if (!Matrix4x4.Invert(view * g.Proj, out var inv))
            return;
        if (timed)
            Timer.Begin(map, s, quality);
        _program.Use();
        _vao.Bind();
        Native.glUniformMatrix4fv(_locInvVP, 1, false, &inv.M11);
        var mode = s.Type switch { SkyType.Cubemap => 1f, SkyType.Equirect => 2f, _ => 0f };
        Native.glUniform1f(_locMode, mode);
        Native.glUniform1f(_locQuality, quality == SkyQuality.High ? 1f : 0f);
        Native.glUniform1f(_locHdr, s.Loaded?.Hdr == true ? 1f : 0f);
        Native.glUniform1f(_locExposure, s.Exposure);
        var rot = s.Rotation * MathF.PI / 180f;
        Native.glUniform2f(_locRot, MathF.Cos(rot), MathF.Sin(rot));

        var sun = s.SunDir;
        Native.glUniform3f(_locSunPos, sun.X, sun.Y, sun.Z);
        var disc = Vector3.Min(s.SunColor * MathF.Max(1.3f * s.SunIntensity, 1.15f), Vector3.One);
        var discCos = s.SunDisc ? MathF.Cos(s.SunSize * 0.5f * MathF.PI / 180f) : 0f;
        Native.glUniform4f(_locSunDisc, disc.X, disc.Y, disc.Z, discCos);
        var glow = s.SunColor * s.SunGlow;
        Native.glUniform3f(_locSunGlow, glow.X, glow.Y, glow.Z);
        var moon = s.MoonDir;
        Native.glUniform3f(_locMoonPos, moon.X, moon.Y, moon.Z);
        Native.glUniform4f(_locMoon, s.MoonColor.X, s.MoonColor.Y, s.MoonColor.Z, s.Moon ? MathF.Cos(s.MoonSize * 0.5f * MathF.PI / 180f) : 0f);
        Native.glUniform1f(_locStars, s.Stars);
        Native.glUniform1f(_locSpace, World.Map.PointGravity != null ? 1f : 0f); // a planet map: sky all round (MapPlanet.cs)
        Native.glUniform1f(_locTime, (float)(time % 3600.0));

        // Clouds: one noise tile is 4 "cells" of CloudScale metres; uv per unit of (x/y, z/y) on the layer.
        var uvScale = s.CloudAltitude / (4f * s.CloudScale);
        Native.glUniform4f(_locCloud, CoverageThreshold(s.CloudCoverage), s.CloudCoverage > 0 ? s.CloudDensity : 0f, uvScale, Math.Clamp(s.CloudCoverage * 1.2f, 0f, 0.3f));
        Native.glUniform3f(_locCloudCol, s.CloudColor.X, s.CloudColor.Y, s.CloudColor.Z);
        Native.glUniform3f(_locCloudShade, s.CloudShade.X, s.CloudShade.Y, s.CloudShade.Z);
        var w = WindOffsets(s, time);
        Native.glUniform4f(_locWind, w.X, w.Y, w.Z, w.W);
        var w2 = CirrusOffset(s, time);
        Native.glUniform2f(_locWind2, w2.X, w2.Y);

        Native.glActiveTexture(GL_TEXTURE0 + NoiseUnit);
        Native.glBindTexture(GL_TEXTURE_2D, _noise);
        if (image != 0)
        {
            Native.glActiveTexture(GL_TEXTURE0 + (s.Type == SkyType.Cubemap ? CubeUnit : PanoUnit));
            Native.glBindTexture(s.Type == SkyType.Cubemap ? GL_TEXTURE_CUBE_MAP : GL_TEXTURE_2D, image);
        }

        if (!_checkedFirstDraw)
            while (GlDebug.Error() != 0) { } // only our own errors count below
        g.SetBlend(false);
        DepthMask(false);
        PerfCounters.Draw(3); // the perf window's counters (Source/Perf/PerfCounters.cs)
        Native.glDrawArrays(GL_TRIANGLES, 0, 3);
        DepthMask(true);
        g.Rebind(); // the box renderer's program, cube and texture array (unit 0) again
        if (!_checkedFirstDraw)
        {
            _checkedFirstDraw = true;
            var err = GlDebug.Error();
            Console.WriteLine($"[sky] first sky drawn ({s.Describe()}), GL error 0x{err:X}");
        }
        if (timed)
            Timer.End();
    }

    /// <summary>
    /// The noise value above which a pixel is cloud, for a coverage of 0-1, so that about that share of the cloud layer
    /// is covered. Calibrated once on the real noise texture with the shader's own mix (<see cref="Calibration"/>), so it
    /// holds whatever the noise looks like.
    /// </summary>
    public static float CoverageThreshold(float coverage)
    {
        if (coverage <= 0)
            return 2f; // never
        var table = Calibration;
        var f = Math.Clamp(coverage, 0f, 1f) * (table.Length - 1);
        var i = Math.Min((int)f, table.Length - 2);
        return table[i] + (table[i + 1] - table[i]) * (f - i);
    }

    /// <summary>The middle of the shader's soft cloud edge, above the threshold (smoothstep(thr, thr + 0.09, n)).</summary>
    private const float EdgeMid = 0.045f;

    private static float[]? _calibration;
    private static byte[]? _noiseCache;

    /// <summary>The cloud noise texels (made once; see <see cref="MakeNoise"/>).</summary>
    public static byte[] Noise => _noiseCache ??= MakeNoise();

    /// <summary>Thresholds for coverage 0, 1/32 ... 1: quantiles of the shader's noise mix over many layer positions.</summary>
    private static float[] Calibration
    {
        get
        {
            if (_calibration != null)
                return _calibration;
            var values = SampleMix(Noise, 40000, 77);
            Array.Sort(values);
            var table = new float[33];
            for (var k = 0; k <= 32; k++)
            {
                var q = 1f - k / 32f; // coverage c: the (1 - c) quantile is where cloud starts
                table[k] = values[Math.Clamp((int)(q * (values.Length - 1)), 0, values.Length - 1)] - EdgeMid;
            }
            table[32] = MathF.Min(table[32], -0.1f); // full coverage: everything
            return _calibration = table;
        }
    }

    /// <summary>The shader's cumulus noise mix at <paramref name="count"/> pseudo-random layer positions.</summary>
    private static float[] SampleMix(byte[] noise, int count, int seed)
    {
        var rng = new Random(seed);
        var values = new float[count];
        for (var k = 0; k < count; k++)
        {
            var u = rng.NextDouble() * 64;
            var v = rng.NextDouble() * 64;
            Texel(noise, u, v, out var r, out var g, out var b, out var a);
            var fbm = (r * 0.50f + g * 0.25f + b * 0.15f + a * 0.10f) / 255f;
            Texel(noise, u * 0.23, v * 0.23, out var br, out _, out _, out _);
            values[k] = fbm * 0.72f + br / 255f * 0.28f;
        }
        return values;
    }

    /// <summary>Cloud layer offsets in noise tiles, wrapped to 0-1 in double precision (cumulus xy, coverage variation zw).</summary>
    public static Vector4 WindOffsets(SkySettings s, double time)
    {
        var tiles = time * s.CloudSpeed / (4.0 * s.CloudScale);
        var h = s.CloudHeading * Math.PI / 180.0;
        double dx = -Math.Sin(h) * tiles, dz = Math.Cos(h) * tiles; // the pattern moves towards the heading (north = -z)
        return new Vector4((float)Frac(dx), (float)Frac(dz), (float)Frac(dx * 0.23), (float)Frac(dz * 0.23));
    }

    private static Vector2 CirrusOffset(SkySettings s, double time)
    {
        var tiles = time * s.CloudSpeed * 1.5 / (4.0 * s.CloudScale);
        var h = s.CloudHeading * Math.PI / 180.0;
        return new Vector2((float)Frac(-Math.Sin(h) * tiles * 0.3), (float)Frac(Math.Cos(h) * tiles * 0.3));
    }

    private static double Frac(double v) => v - Math.Floor(v);

    /// <summary>Turns <paramref name="d"/> round the vertical axis by <paramref name="radians"/> (the shader's image rotation).</summary>
    public static Vector3 Rotate(Vector3 d, float radians)
    {
        float c = MathF.Cos(radians), sn = MathF.Sin(radians);
        return new Vector3(d.X * c - d.Z * sn, d.Y, d.X * sn + d.Z * c);
    }

    // ---- Noise ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// The cloud noise: <see cref="NoiseSize"/> squared RGBA8, each channel tiling value noise (quintic interpolation)
    /// with 4, 8, 16 and 32 cells across, stretched to the full 0-255 range. Deterministic.
    /// </summary>
    public static byte[] MakeNoise()
    {
        const int n = NoiseSize;
        var px = new byte[n * n * 4];
        var plane = new float[n * n];
        int[] cells = [4, 8, 16, 32];
        for (var c = 0; c < 4; c++)
        {
            var k = cells[c];
            var rng = new Random(9173 + c * 101);
            var lattice = new float[k * k];
            for (var i = 0; i < lattice.Length; i++)
                lattice[i] = (float)rng.NextDouble();
            float lo = float.MaxValue, hi = float.MinValue;
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float fx = x * k / (float)n, fy = y * k / (float)n;
                    int x0 = (int)fx, y0 = (int)fy;
                    float tx = Quintic(fx - x0), ty = Quintic(fy - y0);
                    int x1 = (x0 + 1) % k, y1 = (y0 + 1) % k;
                    x0 %= k;
                    y0 %= k;
                    var a = lattice[y0 * k + x0] + (lattice[y0 * k + x1] - lattice[y0 * k + x0]) * tx;
                    var b = lattice[y1 * k + x0] + (lattice[y1 * k + x1] - lattice[y1 * k + x0]) * tx;
                    var v = a + (b - a) * ty;
                    plane[y * n + x] = v;
                    lo = MathF.Min(lo, v);
                    hi = MathF.Max(hi, v);
                }
            var scale = 255f / MathF.Max(hi - lo, 1e-6f);
            for (var i = 0; i < plane.Length; i++)
                px[i * 4 + c] = (byte)Math.Clamp((int)MathF.Round((plane[i] - lo) * scale), 0, 255);
        }
        return px;
    }

    private static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>
    /// The share of the cloud layer covered at <paramref name="coverage"/>, measured on <paramref name="noise"/> with the
    /// shader's own mix (the self-test checks coverage means what it says).
    /// </summary>
    public static float MeasuredCoverage(byte[] noise, float coverage)
    {
        var thr = CoverageThreshold(coverage);
        var values = SampleMix(noise, 20000, 4242); // other positions than the calibration's, so this isn't circular
        return values.Count(v => v > thr + EdgeMid) / (float)values.Length;
    }

    private static float Texel(byte[] noise, double u, double v, out float r, out float g, out float b, out float a)
    {
        const int n = NoiseSize;
        var x = (int)((u - Math.Floor(u)) * n) % n;
        var y = (int)((v - Math.Floor(v)) * n) % n;
        var i = (y * n + x) * 4;
        (r, g, b, a) = (noise[i], noise[i + 1], noise[i + 2], noise[i + 3]);
        return r;
    }

    // ---- GL setup -------------------------------------------------------------------------------------------------

    private static bool EnsureReady()
    {
        if (_ready || _failed)
            return _ready;
        try
        {
            using var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
            using var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
            _program = CreateProgram().Attach(vs, fs).Link();
            GlDebug.Label(GL_PROGRAM, _program.Id, "SkyRenderer");
            DetachShader(_program, vs);
            DetachShader(_program, fs);
            _vao = GenVertexArray(); // core profile needs a bound VAO even with no vertex buffers
            Lighting.Upload(_program); // SkyGradient's colours follow the map's sky like every other shader's
            _locInvVP = Uniform("uInvVP\0"u8);
            _locMode = Uniform("uMode\0"u8);
            _locQuality = Uniform("uQuality\0"u8);
            _locHdr = Uniform("uHdr\0"u8);
            _locExposure = Uniform("uExposure\0"u8);
            _locRot = Uniform("uRot\0"u8);
            _locSunPos = Uniform("uSunPos\0"u8);
            _locSunDisc = Uniform("uSunDisc\0"u8);
            _locSunGlow = Uniform("uSunGlow\0"u8);
            _locMoonPos = Uniform("uMoonPos\0"u8);
            _locMoon = Uniform("uMoon\0"u8);
            _locStars = Uniform("uStars\0"u8);
            _locSpace = Uniform("uSpace\0"u8);
            _locTime = Uniform("uTime\0"u8);
            _locCloud = Uniform("uCloud\0"u8);
            _locCloudCol = Uniform("uCloudCol\0"u8);
            _locCloudShade = Uniform("uCloudShade\0"u8);
            _locWind = Uniform("uWind\0"u8);
            _locWind2 = Uniform("uWind2\0"u8);
            Native.glUniform1i(Uniform("uNoise\0"u8), (int)NoiseUnit);
            Native.glUniform1i(Uniform("uCube\0"u8), (int)CubeUnit);
            Native.glUniform1i(Uniform("uPano\0"u8), (int)PanoUnit);
            _noise = MakeNoiseTexture();
            Native.glEnable(GlCapability.TextureCubeMapSeamless); // no seams between cube faces
            Timer.Init();
            _ready = true;
            Console.WriteLine($"[sky] sky shader ready, GPU timer {(Timer.Available ? "on" : "unavailable")}, noise {NoiseSize}x{NoiseSize} RGBA8, GL error 0x{GlDebug.Error():X}");
        }
        catch (Exception ex)
        {
            _failed = true;
            Console.WriteLine($"[sky] sky shader failed, maps keep their flat sky colour (the light still follows their sky): {ex.Message}");
        }
        return _ready;
    }

    private static int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }

    private static uint MakeNoiseTexture()
    {
        var px = Noise;
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        Native.glActiveTexture(GL_TEXTURE0 + NoiseUnit);
        Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        fixed (byte* p = px)
            Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, NoiseSize, NoiseSize, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
        Native.glGenerateMipmap(GL_TEXTURE_2D);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
        Native.glActiveTexture(GL_TEXTURE0);
        return tex.Id;
    }

    /// <summary>The GL texture for an image sky, uploaded on first use (a few are kept).</summary>
    private static uint ImageTexture(SkyImage img)
    {
        for (var i = 0; i < ImageTextures.Count; i++)
            if (ReferenceEquals(ImageTextures[i].Image, img))
                return ImageTextures[i].Texture;
        if (ImageTextures.Count >= MaxImageTextures)
        {
            var old = new GlTexture(ImageTextures[0].Texture);
            Native.glDeleteTextures(1, &old);
            ImageTextures.RemoveAt(0);
        }
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        var unit = img.Type == SkyType.Cubemap ? CubeUnit : PanoUnit;
        Native.glActiveTexture(GL_TEXTURE0 + unit);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        if (img.Type == SkyType.Cubemap)
        {
            Native.glBindTexture(GL_TEXTURE_CUBE_MAP, tex.Id);
            for (var f = 0; f < 6; f++)
                fixed (byte* p = img.Faces[f])
                    Native.glTexImage2D(GL_TEXTURE_CUBE_MAP_POSITIVE_X + (uint)f, 0, (int)GL_RGBA8, img.Width, img.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
            Native.glGenerateMipmap(GL_TEXTURE_CUBE_MAP);
            Native.glTexParameteri(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_R, (int)GL_CLAMP_TO_EDGE);
        }
        else
        {
            Native.glBindTexture(GL_TEXTURE_2D, tex.Id);
            if (img.Rgb is { } rgb)
                fixed (float* p = rgb)
                    Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGB16F, img.Width, img.Height, 0, GL_RGB, GL_FLOAT, p);
            else
                fixed (byte* p = img.Rgba)
                    Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, img.Width, img.Height, 0, GL_RGBA, GL_UNSIGNED_BYTE, p);
            // No mipmaps: the u wrap seam would pick a tiny mip there. Sky images are smooth, so plain bilinear is fine.
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
            Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        }
        Native.glActiveTexture(GL_TEXTURE0);
        ImageTextures.Add((img, tex.Id));
        Console.WriteLine($"[sky] uploaded {(img.Type == SkyType.Cubemap ? $"cubemap 6x {img.Width}x{img.Height}" : $"panorama {img.Width}x{img.Height}{(img.Hdr ? " HDR (RGB16F)" : "")}")}, "
                          + $"{img.GpuBytes / 1024} KB, decoded by {img.Decoder}, GL error 0x{GlDebug.Error():X}");
        return tex.Id;
    }

    // ---- GPU timing -----------------------------------------------------------------------------------------------

    /// <summary>Average sky GPU milliseconds over the window since <see cref="ResetWindow"/> (-1 if no timer results yet).</summary>
    public static double WindowGpuMs => Timer.WindowSamples > 0 ? Timer.WindowMs / Timer.WindowSamples : -1;

    /// <summary>Starts a new measuring window for <see cref="WindowGpuMs"/> (the smoke test uses one per preset).</summary>
    public static void ResetWindow() => Timer.ResetWindow();

    /// <summary>
    /// Timestamp queries around the sky draw, a few frames in flight, read back when ready; logs a "[sky]" line every
    /// 5 seconds while a sky is being drawn.
    /// </summary>
    private static class Timer
    {
        private const int Ring = 4;
        private static readonly GlQuery[] Queries = new GlQuery[Ring * 2];
        private static readonly bool[] Pending = new bool[Ring];
        public static bool Available;
        private static int _frame;
        private static bool _inFrame;
        private static double _logMs, _logAt;
        private static int _logSamples, _logFrames;
        public static double WindowMs;
        public static int WindowSamples;
        private static Map? _map;
        private static SkySettings? _sky;
        private static SkyQuality _quality;
        private static int _vw, _vh;

        public static void Init()
        {
            if (Native.glGenQueries == null || Native.glQueryCounter == null || Native.glGetQueryObjectui64v == null || Native.glGetQueryObjectiv == null)
                return;
            fixed (GlQuery* q = Queries)
                Native.glGenQueries(Queries.Length, q);
            Available = true;
        }

        public static void ResetWindow()
        {
            Collect();
            WindowMs = 0;
            WindowSamples = 0;
        }

        public static void Begin(Map map, SkySettings sky, SkyQuality quality)
        {
            _map = map;
            _sky = sky;
            _quality = quality;
            _inFrame = false;
            if (!Available)
                return;
            Collect();
            var slot = _frame % Ring;
            if (Pending[slot])
                return;
            Native.glQueryCounter(Queries[slot * 2].Id, GL_TIMESTAMP);
            _inFrame = true;
        }

        public static void End()
        {
            if (_inFrame)
            {
                var slot = _frame % Ring;
                Native.glQueryCounter(Queries[slot * 2 + 1].Id, GL_TIMESTAMP);
                Pending[slot] = true;
                _frame++;
            }
            _logFrames++;
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (_logAt == 0)
            {
                _logAt = now + 3;
                int* vp = stackalloc int[4];
                Native.glGetIntegerv(GL_VIEWPORT, vp);
                (_vw, _vh) = (vp[2], vp[3]);
            }
            if (now < _logAt)
                return;
            _logAt = now + 5;
            int* v = stackalloc int[4];
            Native.glGetIntegerv(GL_VIEWPORT, v);
            (_vw, _vh) = (v[2], v[3]);
            var gpu = _logSamples > 0 ? $"{_logMs / _logSamples:0.000} ms GPU" : "GPU time unavailable";
            Console.WriteLine($"[sky] {_map?.Name}: {_sky?.Describe()}, quality {_quality}: {gpu} (avg of {_logSamples} timed of {_logFrames} frames) at {_vw}x{_vh}");
            _logMs = 0;
            _logSamples = _logFrames = 0;
        }

        private static void Collect()
        {
            if (!Available)
                return;
            for (var s = 0; s < Ring; s++)
            {
                if (!Pending[s])
                    continue;
                int available;
                Native.glGetQueryObjectiv(Queries[s * 2 + 1].Id, GL_QUERY_RESULT_AVAILABLE, &available);
                if (available == 0)
                    continue;
                ulong t0, t1;
                Native.glGetQueryObjectui64v(Queries[s * 2].Id, GL_QUERY_RESULT, &t0);
                Native.glGetQueryObjectui64v(Queries[s * 2 + 1].Id, GL_QUERY_RESULT, &t1);
                Pending[s] = false;
                var ms = t1 > t0 ? (t1 - t0) / 1e6 : 0;
                _logMs += ms;
                _logSamples++;
                WindowMs += ms;
                WindowSamples++;
            }
        }
    }
}
