using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The cold storm's looks (<see cref="ColdStorm"/>; docs/weather/WEATHER.md, "Cold storm"): driven snow round the camera
/// and the whiteout over the sky. The snow is one instanced draw of thin streaks with no buffer at all: each flake's
/// place in a box round the camera comes from a hash of its number, moved by the storm's drift and wrapped in the
/// vertex shader, so the CPU does nothing per flake. The veil is one triangle on the far plane, drawn only where the
/// sky shows (the depth there is still the clear value), so the sky fades into the same white as the fogged ground.
/// </summary>
public static unsafe class SnowRenderer
{
    private const string SnowVs = """
        #version 410 core
        uniform mat4 uViewProj;  // from the render origin
        uniform vec3 uEye;       // the camera, from the render origin
        uniform vec3 uOffset;    // the drift less the camera, wrapped (ColdStorm.Drift), plus half the box
        uniform vec3 uStreak;    // a flake's smear: its speed times the time the eye blurs it
        uniform vec4 uP;         // box, time, alpha, fog end
        out float vA;
        out vec2 vUv;

        float Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return float(x) * (1.0 / 4294967296.0);
        }

        void main()
        {
            uint id = uint(gl_InstanceID);
            float box = uP.x;
            vec3 r = vec3(Hash(id * 4u), Hash(id * 4u + 1u), Hash(id * 4u + 2u));
            float k = Hash(id * 4u + 3u);
            vec3 p = mod(r * box + uOffset, box) - box * 0.5;
            float ph = k * 6.2832;
            p += vec3(sin(uP.y * 1.7 + ph), sin(uP.y * 1.3 + ph * 2.0), cos(uP.y * 1.9 + ph)) * 0.22; // (each flake tumbles)
            float size = mix(0.008, 0.024, k * k);
            vec3 along = uStreak * mix(0.7, 1.3, fract(k * 7.0));
            float len = length(along);
            vec3 dir = len > 1e-4 ? along / len : vec3(0.0, 1.0, 0.0);
            float dist = max(length(p), 1e-3);
            vec3 side = cross(dir, p / dist);
            float sl = length(side);
            side = sl > 1e-3 ? side / sl : vec3(1.0, 0.0, 0.0);
            vec2 q = vec2(gl_VertexID & 1, (gl_VertexID >> 1) & 1) * 2.0 - 1.0;
            vec3 at = p + side * (size * q.x) + dir * (max(len, size) * 0.5 * q.y);
            vUv = q;
            float near = smoothstep(0.25, 1.1, dist);           // (none in your face)
            float far = 1.0 - smoothstep(box * 0.3, box * 0.5, dist); // (no box edge to see)
            float fog = 1.0 - clamp(dist / max(uP.w, 1.0), 0.0, 1.0);
            vA = uP.z * near * far * fog * mix(0.55, 1.0, k);
            gl_Position = uViewProj * vec4(uEye + at, 1.0);
        }
        """;

    private const string SnowFs = """
        #version 410 core
        uniform vec3 uCol;
        in float vA;
        in vec2 vUv;
        out vec4 oColor;
        void main()
        {
            float a = vA * (1.0 - vUv.x * vUv.x) * (1.0 - vUv.y * vUv.y * 0.6);
            if (a < 0.004)
                discard;
            oColor = vec4(uCol, a);
        }
        """;

    private const string VeilVs = """
        #version 410 core
        void main()
        {
            vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2)) * 2.0 - 1.0;
            gl_Position = vec4(p, 1.0, 1.0); // on the far plane: only where nothing was drawn (the sky)
        }
        """;

    private const string VeilFs = """
        #version 410 core
        uniform vec4 uCol;
        out vec4 oColor;
        void main() { oColor = uCol; }
        """;

    private static GlProgram _snow, _veil;
    private static GlVertexArray _vao;
    private static int _lViewProj, _lEye, _lOffset, _lStreak, _lP, _lCol, _lVeilCol;
    private static bool _ready, _broken;

    /// <summary>Flakes drawn last frame (the perf window can say).</summary>
    public static int Flakes { get; private set; }

    /// <summary>The most flakes at each Particles setting (Low, Medium, High).</summary>
    private static readonly int[] MaxFlakes = [3000, 6000, 10000];

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        try
        {
            _snow = Link(SnowVs, SnowFs, "Snow");
            _veil = Link(VeilVs, VeilFs, "Snow.veil");
            _lViewProj = Loc(_snow, "uViewProj\0"u8);
            _lEye = Loc(_snow, "uEye\0"u8);
            _lOffset = Loc(_snow, "uOffset\0"u8);
            _lStreak = Loc(_snow, "uStreak\0"u8);
            _lP = Loc(_snow, "uP\0"u8);
            _lCol = Loc(_snow, "uCol\0"u8);
            _lVeilCol = Loc(_veil, "uCol\0"u8);
            _vao = GenVertexArray(); // (no attributes: everything comes from the vertex and instance numbers)
            _ready = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[storm] snow off: {ex.Message}");
            _broken = true;
        }
        return _ready;
    }

    private static GlProgram Link(string vsrc, string fsrc, string label)
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vsrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fsrc)).Compile();
        var p = CreateProgram();
        Native.glAttachShader(p, vs);
        Native.glAttachShader(p, fs);
        Native.glLinkProgram(p);
        GlDebug.Label(GL_PROGRAM, p.Id, label);
        int linked;
        Native.glGetProgramiv(p, GL_LINK_STATUS, &linked);
        if (linked == 0)
            throw new InvalidOperationException($"the {label} shader did not link");
        vs.Dispose();
        fs.Dispose();
        return p;
    }

    private static int Loc(GlProgram p, ReadOnlySpan<byte> name)
    {
        fixed (byte* n = name)
            return Native.glGetUniformLocation(p, n);
    }

    /// <summary>
    /// The whiteout over the sky (after the sky, before anything blended): the storm's colour, as thick as the storm.
    /// Nothing without a storm.
    /// </summary>
    public static void DrawVeil(Renderer gfx)
    {
        if (!ColdStorm.Active || !Init())
            return;
        var a = Math.Clamp(0.35f + 0.65f * ColdStorm.Level * 1.25f, 0f, 0.97f) * MathF.Min(ColdStorm.Level * 8f, 1f);
        var c = ColdStorm.Color;
        _veil.Use();
        Native.glUniform4f(_lVeilCol, c.X, c.Y, c.Z, a);
        _vao.Bind();
        Native.glEnable(GlCapability.Blend);
        Renderer.BlendDefault();
        Native.glDepthMask(false);
        Native.glDrawArrays(GL_TRIANGLES, 0, 3);
        PerfCounters.Draw(3);
        Native.glDepthMask(true);
        Native.glDisable(GlCapability.Blend);
        gfx.AfterFx();
    }

    /// <summary>
    /// The driven snow round the camera, in the blended pass (the depth buffer is read, never written), as many flakes as
    /// the storm is hard and you are out in it, up to <paramref name="level"/>'s most. Nothing without a storm.
    /// </summary>
    public static void Draw(Renderer gfx, VideoLevel level, double time)
    {
        Flakes = 0;
        var amount = ColdStorm.Level * ColdStorm.Outside;
        if (!ColdStorm.Active || amount < 0.01f || !Init())
            return;
        var n = (int)(MaxFlakes[Math.Clamp((int)level, 0, MaxFlakes.Length - 1)] * Math.Clamp(amount * 1.2f, 0f, 1f));
        if (n <= 0)
            return;
        const float box = ColdStorm.SnowBox;
        var eye = gfx.CameraPosition;
        var drift = ColdStorm.Drift;
        var offset = new Vector3(Wrap(drift.X - eye.X + box * 0.5f), Wrap(drift.Y - eye.Y + box * 0.5f), Wrap(drift.Z - eye.Z + box * 0.5f));
        var eyeRel = eye - gfx.Origin;
        var vel = ColdStorm.Wind * 0.85f - ColdStorm.Up * (1.1f + 0.6f * ColdStorm.Level);
        var streak = vel * 0.03f; // (about the time the eye smears a flake going past)
        var col = ColdStorm.Color * 1.15f + new Vector3(0.05f);
        var fogEnd = MathF.Max(gfx.FogRange.Y, 8f);

        _snow.Use();
        var vp = gfx.ViewProjRelative;
        Native.glUniformMatrix4fv(_lViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_lEye, eyeRel.X, eyeRel.Y, eyeRel.Z);
        Native.glUniform3f(_lOffset, offset.X, offset.Y, offset.Z);
        Native.glUniform3f(_lStreak, streak.X, streak.Y, streak.Z);
        Native.glUniform4f(_lP, box, (float)(time % 1024.0), MathF.Min(0.55f + 0.45f * amount, 1f), fogEnd);
        Native.glUniform3f(_lCol, col.X, col.Y, col.Z);
        _vao.Bind();
        Native.glEnable(GlCapability.Blend);
        Renderer.BlendDefault();
        Native.glDepthMask(false);
        Native.glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, n);
        PerfCounters.Draw(4, n);
        Native.glDepthMask(true);
        Native.glDisable(GlCapability.Blend);
        gfx.AfterFx();
        Flakes = n;
    }

    private static float Wrap(float v)
    {
        v %= ColdStorm.SnowBox;
        return v < 0 ? v + ColdStorm.SnowBox : v;
    }
}
