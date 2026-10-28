using System.Diagnostics;
using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws the <see cref="BloodOverlay"/> in one instanced call: a unit quad per instance (no vertex buffers; the corners
/// come from gl_VertexID and each instance's two vec4s from uniform arrays), the splatter masks from a small R8 texture
/// array generated at startup (<see cref="BloodTextures"/>). The vignette is four thin bands along the screen edges, so
/// the middle of the screen is never shaded at all. Measures its own GPU time with timestamps and logs a
/// <c>[blood]</c> line every few seconds while it shows. If the shader can't be built it draws nothing (and says so once).
/// </summary>
public static unsafe class BloodOverlayRenderer
{
    private const string VertexSrc = """
        #version 410 core
        uniform vec4 uA[27];
        uniform vec4 uB[27];
        uniform vec2 uScreen;
        out vec2 vUv;
        out vec2 vPix;
        flat out vec4 vB;
        void main()
        {
            vec2 c = vec2((gl_VertexID & 1) != 0 ? 1.0 : -1.0, (gl_VertexID & 2) != 0 ? 1.0 : -1.0);
            vec4 a = uA[gl_InstanceID];
            vec4 b = uB[gl_InstanceID];
            vec2 p;
            if (b.y < 0.0)
                p = mix(a.xy, a.zw, c * 0.5 + 0.5); // a vignette band: a rectangle
            else
            {
                float cs = cos(a.w), sn = sin(a.w);
                p = a.xy + vec2(c.x * cs - c.y * sn, c.x * sn + c.y * cs) * a.z; // a splatter: centre, half size, rotation
            }
            vUv = c * 0.5 + 0.5;
            vPix = p;
            vB = b;
            gl_Position = vec4(p / uScreen * 2.0 - 1.0, 0.0, 1.0);
        }
        """;

    private const string FragmentSrc = """
        #version 410 core
        uniform sampler2DArray uMask;
        uniform vec3 uRed;
        uniform vec3 uDark;
        uniform vec2 uScreen;
        in vec2 vUv;
        in vec2 vPix;
        flat in vec4 vB;
        out vec4 FragColor;
        void main()
        {
            if (vB.y < 0.0)
            {
                vec2 e = min(vPix, uScreen - vPix);
                float d = clamp(min(e.x, e.y) / vB.w, 0.0, 1.0);
                float a = 1.0 - smoothstep(0.0, 1.0, d);
                a = a * a * vB.x;
                if (a < 0.003)
                    discard;
                FragColor = vec4(mix(uRed, uDark, 0.5 + 0.5 * vB.z) * 0.7, a);
                return;
            }
            float t = texture(uMask, vec3(vUv, vB.y)).r;
            float a = t * vB.x;
            if (a < 0.003)
                discard;
            vec3 col = mix(uRed, uDark, clamp(vB.z * 0.8 + (t - 0.6) * 0.7, 0.0, 1.0)); // thicker and nearer death: darker
            FragColor = vec4(col, min(a * 1.15, 0.94));
        }
        """;

    private static GlProgram _program;
    private static GlVertexArray _vao;
    private static uint _tex;
    private static bool _ready, _failed;
    private static int _locA, _locB, _locScreen, _locMask, _locRed, _locDark;
    private static readonly Vector4[] A = new Vector4[BloodOverlay.MaxInstances], B = new Vector4[BloodOverlay.MaxInstances];

    // GPU timing (timestamps, a few frames in flight) and the log line.
    private const int Ring = 4;
    private static readonly GlQuery[] Queries = new GlQuery[Ring * 2];
    private static readonly bool[] Pending = new bool[Ring];
    private static bool _timer;
    private static int _frame, _samples, _drawFrames, _instances;
    private static double _gpuMs, _worstMs, _logAt;

    /// <summary>Instances drawn last frame (0: nothing on screen).</summary>
    public static int LastInstances { get; private set; }

    /// <summary>Average GPU ms of the overlay pass over the last log window (-1: unknown).</summary>
    public static double LastGpuMs { get; private set; } = -1;

    /// <summary>The blood colours: bright red, or dark maroon (Settings > Video "Blood colour").</summary>
    public static (Vector3 Red, Vector3 Dark) Colors(BloodColour c) => c == BloodColour.Dark
        ? (new Vector3(0.24f, 0.035f, 0.045f), new Vector3(0.08f, 0.01f, 0.015f))
        : (new Vector3(0.62f, 0.03f, 0.03f), new Vector3(0.2f, 0.0f, 0.01f));

    /// <summary>
    /// Builds and draws the overlay for this frame (call in the HUD pass: ortho camera, blending on, depth test off).
    /// Restores <paramref name="g"/>'s program and texture afterwards.
    /// </summary>
    public static void Draw(Renderer g, BloodOverlay overlay, float screenW, float screenH, float spanLeft, float spanW, BloodMode mode, BloodColour colour)
    {
        var n = overlay.Build(A, B, screenW, screenH, spanLeft, spanW, mode);
        LastInstances = n;
        if (n == 0 || !EnsureReady())
            return;
        Collect();
        var slot = _frame % Ring;
        var timed = _timer && !Pending[slot];
        if (timed)
            Native.glQueryCounter(Queries[slot * 2].Id, GL_TIMESTAMP);
        _program.Use();
        _vao.Bind();
        fixed (Vector4* pa = A)
            Native.glUniform4fv(_locA, n, (float*)pa);
        fixed (Vector4* pb = B)
            Native.glUniform4fv(_locB, n, (float*)pb);
        Native.glUniform2f(_locScreen, screenW, screenH);
        var (red, dark) = Colors(colour);
        Native.glUniform3f(_locRed, red.X, red.Y, red.Z);
        Native.glUniform3f(_locDark, dark.X, dark.Y, dark.Z);
        Native.glUniform1i(_locMask, 2);
        Native.glActiveTexture(GL_TEXTURE2);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
        Native.glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, n);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, 0);
        Native.glActiveTexture(GL_TEXTURE0);
        if (timed)
        {
            Native.glQueryCounter(Queries[slot * 2 + 1].Id, GL_TIMESTAMP);
            Pending[slot] = true;
        }
        _frame++;
        g.Rebind(); // the box renderer's program, cube and texture array again
        _drawFrames++;
        _instances += n;
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        if (_logAt == 0)
            _logAt = now + 3;
        if (now < _logAt)
            return;
        _logAt = now + 10;
        LastGpuMs = _samples > 0 ? _gpuMs / _samples : -1;
        Console.WriteLine($"[blood] overlay {mode}: {_instances / (double)Math.Max(1, _drawFrames):0.0} instances in 1 draw call per frame, GPU "
            + (_samples > 0 ? $"{LastGpuMs:0.000} ms avg, {_worstMs:0.000} ms worst" : "n/a") + $" ({screenW:0}x{screenH:0}, span {spanW:0} px); {BodyBloodRender.Stats}");
        _gpuMs = _worstMs = 0;
        _samples = _drawFrames = _instances = 0;
    }

    private static void Collect()
    {
        if (!_timer)
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
            _gpuMs += ms;
            _worstMs = Math.Max(_worstMs, ms);
            _samples++;
        }
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
            GlDebug.Label(GL_PROGRAM, _program.Id, "BloodOverlayRenderer");
            DetachShader(_program, vs);
            DetachShader(_program, fs);
            _vao = GenVertexArray(); // core profile: a VAO must be bound even with no vertex buffers
            _locA = Uniform("uA\0"u8);
            _locB = Uniform("uB\0"u8);
            _locScreen = Uniform("uScreen\0"u8);
            _locMask = Uniform("uMask\0"u8);
            _locRed = Uniform("uRed\0"u8);
            _locDark = Uniform("uDark\0"u8);

            var t0 = Stopwatch.GetTimestamp();
            var pixels = BloodTextures.Generate();
            var genMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            GlTexture tex = default;
            Native.glGenTextures(1, &tex);
            _tex = tex.Id;
            Native.glActiveTexture(GL_TEXTURE2);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _tex);
            Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            fixed (byte* p = pixels)
                Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_R8, BloodTextures.Size, BloodTextures.Size, BloodTextures.Layers, 0, GL_RED, GL_UNSIGNED_BYTE, p);
            Native.glGenerateMipmap(GL_TEXTURE_2D_ARRAY);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
            Native.glBindTexture(GL_TEXTURE_2D_ARRAY, 0);
            Native.glActiveTexture(GL_TEXTURE0);

            if (Native.glGenQueries != null && Native.glQueryCounter != null && Native.glGetQueryObjectui64v != null && Native.glGetQueryObjectiv != null)
            {
                fixed (GlQuery* q = Queries)
                    Native.glGenQueries(Queries.Length, q);
                _timer = true;
            }
            _ready = true;
            Console.WriteLine($"[blood] overlay shader ready: {BloodTextures.Layers} procedural splatter masks {BloodTextures.Size}px generated in {genMs:0.0} ms, "
                + $"GPU timer {(_timer ? "on" : "unavailable")}, GL error 0x{GlDebug.Error():X}");
        }
        catch (Exception ex)
        {
            _failed = true;
            Console.WriteLine($"[blood] overlay shader failed, no screen blood (body blood is unaffected): {ex.Message}");
        }
        return _ready;
    }

    private static int Uniform(ReadOnlySpan<byte> nameZ)
    {
        fixed (byte* p = nameZ)
            return Native.glGetUniformLocation(_program, p);
    }
}
