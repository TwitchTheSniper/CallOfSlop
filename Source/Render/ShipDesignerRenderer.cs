using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The Ship Designer's yard (UI/UiShipDesigner*.cs): the designer's own triangle meshes and lines, drawn in the yard's viewport
/// the way its web page draws them (three.js, tools/ship-designer): faceted, vertex coloured, a hemisphere light, a sun and a fill
/// light, colours worked in linear and written out as sRGB. Keyed meshes (the model's per-object cache key) stay on the GPU while
/// the scene keeps them; the rest are made at each rebuild and let go at the next.
/// </summary>
public static unsafe class ShipDesignerRenderer
{
    private const string Vs = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec4 aCol;
        uniform mat4 uVP;
        uniform vec3 uOff;
        out vec3 vW;
        out vec4 vC;
        void main()
        {
            vW = aPos + uOff;
            vC = aCol;
            gl_Position = uVP * vec4(vW, 1.0);
        }
        """;

    private const string Fs = """
        #version 410 core
        in vec3 vW;
        in vec4 vC;
        uniform vec3 uEye;
        uniform vec4 uTint;     // linear colour, alpha
        uniform vec3 uEmit;     // linear
        uniform vec3 uMode;     // x: 1 unlit; y: metalness; z: 1 vertex colours
        uniform vec3 uSurf;     // x: roughness; y: clearcoat; z: environment strength (0: painted, no reflections)
        uniform vec4 uPlane;    // Room view's cutaway: n.p + d < 0 is cut, when uCut is 1 (three.js clipping planes)
        uniform float uCut;
        out vec4 oColor;
        // The page's small reflection room: a bright horizon band between a blue-grey sky and a dark ground (linear).
        vec3 envAt(vec3 r)
        {
            vec3 sky = vec3(0.275, 0.381, 0.509), hor = vec3(0.807, 0.855, 0.906), gnd = vec3(0.024, 0.030, 0.037);
            return r.y > 0.0 ? mix(hor, sky, min(1.0, r.y * 1.6)) : mix(hor, gnd, min(1.0, -r.y * 3.0));
        }
        void main()
        {
            if (uCut > 0.5 && dot(uPlane.xyz, vW) + uPlane.w < 0.0)
                discard;
            vec3 base = mix(vec3(1.0), vC.rgb, uMode.z) * uTint.rgb;
            vec3 c;
            if (uMode.x > 0.5)
                c = base;
            else
            {
                vec3 n = normalize(cross(dFdx(vW), dFdy(vW)));
                if (dot(n, uEye - vW) < 0.0)
                    n = -n;
                // three.js: a hemisphere light (sky dfe9f2, ground 2a3036, 1.25), a sun (1.6) and a cool fill (9fc0e0, 0.5), Lambert over pi.
                vec3 sky = vec3(0.737, 0.815, 0.888), ground = vec3(0.023, 0.030, 0.037);
                vec3 hemi = mix(ground, sky, 0.5 * n.y + 0.5) * 1.25;
                float sun = max(dot(n, normalize(vec3(30.0, 50.0, -20.0))), 0.0) * 1.6;
                vec3 fill = vec3(0.347, 0.527, 0.745) * max(dot(n, normalize(vec3(-30.0, -10.0, 40.0))), 0.0) * 0.5;
                c = base * (1.0 - uMode.y) * (hemi + vec3(sun) + fill) / 3.14159;
                c += 0.04 * (hemi + vec3(sun)) * 0.12; // (a touch of the specular three's standard material shows)
                if (uSurf.z > 0.0)
                {
                    // Metals and plastics (the page's materials): the room reflected, blurred toward its mean by roughness.
                    vec3 v = normalize(uEye - vW), r = reflect(-v, n);
                    vec3 env = mix(envAt(r), vec3(0.45, 0.5, 0.56), clamp(uSurf.x, 0.0, 1.0));
                    vec3 f0 = mix(vec3(0.04), base, uMode.y);
                    float fres = pow(1.0 - max(dot(n, v), 0.0), 5.0);
                    vec3 spec = (f0 + (1.0 - f0) * fres * (1.0 - uSurf.x)) * env * uSurf.z;
                    vec3 hv = normalize(v + normalize(vec3(30.0, 50.0, -20.0)));
                    float shin = mix(200.0, 8.0, clamp(uSurf.x, 0.0, 1.0));
                    spec += f0 * pow(max(dot(n, hv), 0.0), shin) * 1.6 * (1.0 - uSurf.x * 0.7);
                    c += spec + uSurf.y * (0.04 + 0.96 * fres) * envAt(r) * 0.6;
                }
            }
            c += uEmit;
            oColor = vec4(pow(clamp(c, 0.0, 1.0), vec3(1.0 / 2.2)), uTint.a * vC.a);
        }
        """;

    private static GlProgram _prog;
    private static int _lVP, _lOff, _lEye, _lTint, _lEmit, _lMode, _lSurf, _lPlane, _lCut;

    /// <summary>Room view's cutaway plane (n, d: n.p + d below 0 is cut), or null for none.</summary>
    public static Vector4? Cut;
    private static bool _ready, _broken;
    private static readonly List<DesignGeo> _free = [];

    /// <summary>A geometry let go (its GPU buffers are freed at the next draw, on the render thread).</summary>
    public static void Free(DesignGeo g)
    {
        if (g.Vao.Id != 0)
            _free.Add(g);
    }

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        try
        {
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(Vs)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(Fs)).Compile();
            _prog = CreateProgram();
            Native.glAttachShader(_prog, vs);
            Native.glAttachShader(_prog, fs);
            Native.glLinkProgram(_prog);
            int linked;
            Native.glGetProgramiv(_prog, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("the ship designer shader did not link");
            vs.Dispose();
            fs.Dispose();
            _lVP = Loc("uVP\0"u8);
            _lOff = Loc("uOff\0"u8);
            _lEye = Loc("uEye\0"u8);
            _lTint = Loc("uTint\0"u8);
            _lEmit = Loc("uEmit\0"u8);
            _lMode = Loc("uMode\0"u8);
            _lSurf = Loc("uSurf\0"u8);
            _lPlane = Loc("uPlane\0"u8);
            _lCut = Loc("uCut\0"u8);
            _ready = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ship designer] view off: {ex.Message}");
            _broken = true;
        }
        return _ready;
    }

    private static int Loc(ReadOnlySpan<byte> name)
    {
        fixed (byte* n = name)
            return Native.glGetUniformLocation(_prog, n);
    }

    private static void Upload(DesignGeo g)
    {
        g.Vao = GenVertexArray();
        g.Vao.Bind();
        g.Vbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, g.Vbo);
        var n = g.Count;
        var data = new float[Math.Max(1, n) * 4];
        for (var i = 0; i < n; i++)
        {
            data[i * 4] = g.P[i * 3];
            data[i * 4 + 1] = g.P[i * 3 + 1];
            data[i * 4 + 2] = g.P[i * 3 + 2];
            data[i * 4 + 3] = BitConverter.UInt32BitsToSingle(g.C[i]);
        }
        fixed (float* p = data)
            Native.glBufferData(GL_ARRAY_BUFFER, data.Length * sizeof(float), p, GL_STATIC_DRAW);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 16, (void*)0);
        Native.glVertexAttribPointer(1, 4, GL_UNSIGNED_BYTE, true, 16, (void*)12);
        EnableVertexAttribArray(0);
        EnableVertexAttribArray(1);
        g.Uploaded = true;
    }

    /// <summary>
    /// Draws the yard: <paramref name="objs"/> in the viewport (framebuffer pixels, GL's bottom-left origin), opaque first, then the
    /// see-through ones by their order and far to near, as three.js sorts them.
    /// </summary>
    public static void Draw(Renderer gfx, IReadOnlyList<DesignObj> objs, Matrix4x4 view, Matrix4x4 proj, Vector3 eye, int vx, int vy, int vw, int vh, Vector3 background)
    {
        if (!Init())
            return;
        foreach (var g in _free)
        {
            var b = g.Vbo;
            Native.glDeleteBuffers(1, &b);
            var a = g.Vao;
            Native.glDeleteVertexArrays(1, &a);
            g.Vao = default;
            g.Vbo = default;
            g.Uploaded = false;
        }
        _free.Clear();

        gfx.SetViewport(vx, vy, vw, vh);
        OpenGl.Scissor(vx, vy, vw, vh);
        OpenGl.Enable(GlCapability.ScissorTest);
        Native.glClearColor(background.X, background.Y, background.Z, 1);
        Native.glClear(GL_COLOR_BUFFER_BIT);
        gfx.SetCamera(view, proj);
        gfx.ClearDepthBuffer();
        Native.glDisable(GlCapability.ScissorTest);
        gfx.SetDepthTest(true);
        gfx.SetDepthWrite(true);
        gfx.SetBlend(false);
        Native.glDisable(GlCapability.CullFace);

        _prog.Use();
        var vp = gfx.ViewProj;
        Native.glUniformMatrix4fv(_lVP, 1, false, &vp.M11);
        Native.glUniform3f(_lEye, eye.X, eye.Y, eye.Z);
        var cut = Cut ?? default;
        Native.glUniform4f(_lPlane, cut.X, cut.Y, cut.Z, cut.W);
        Native.glUniform1f(_lCut, Cut.HasValue ? 1 : 0);

        var opaque = new List<DesignObj>(objs.Count);
        var see = new List<(DesignObj O, float D)>();
        foreach (var o in objs)
        {
            if (o.Geo.Count == 0 || !o.Visible)
                continue;
            if (o.Transparent)
                see.Add((o, Vector3.DistanceSquared(eye, o.Geo.Centre + o.Pos)));
            else
                opaque.Add(o);
        }
        opaque.Sort((a, b) => a.Order.CompareTo(b.Order));
        see.Sort((a, b) => a.O.Order != b.O.Order ? a.O.Order.CompareTo(b.O.Order) : b.D.CompareTo(a.D));
        foreach (var o in opaque)
            One(gfx, o);
        gfx.SetBlend(true);
        Renderer.BlendDefault();
        foreach (var (o, _) in see)
            One(gfx, o);
        gfx.SetBlend(false);
        gfx.SetDepthWrite(true);
        gfx.SetDepthTest(true);
        Native.glBindVertexArray(default);
        gfx.Rebind();
    }

    private static void One(Renderer gfx, DesignObj o)
    {
        var g = o.Geo;
        if (!g.Uploaded)
            Upload(g);
        gfx.SetDepthTest(o.DepthTest);
        gfx.SetDepthWrite(o.DepthWrite);
        Native.glUniform3f(_lOff, o.Pos.X, o.Pos.Y, o.Pos.Z);
        Native.glUniform4f(_lTint, o.Tint.X, o.Tint.Y, o.Tint.Z, o.Alpha);
        Native.glUniform3f(_lEmit, o.Emissive.X, o.Emissive.Y, o.Emissive.Z);
        Native.glUniform3f(_lMode, o.Unlit || g.Lines ? 1 : 0, o.Metal, o.VertexColours ? 1 : 0);
        Native.glUniform3f(_lSurf, o.Rough, o.Coat, o.Env);
        g.Vao.Bind();
        Native.glDrawArrays(g.Lines ? GL_LINES : GL_TRIANGLES, 0, g.Count);
        PerfCounters.Draw(g.Count);
    }
}

/// <summary>Positions (x, y, z a vertex) and colours (RGBA, packed) for the designer's yard: triangles, or line segments.</summary>
public sealed class DesignGeo
{
    public float[] P = [];
    public uint[] C = [];
    public bool Lines;
    public string? Key;
    public Vector3 Min, Max;
    internal GlVertexArray Vao;
    internal GlBuffer Vbo;
    internal bool Uploaded;

    public int Count => P.Length / 3;
    public Vector3 Centre => (Min + Max) * 0.5f;

    public DesignGeo Bound()
    {
        Min = new Vector3(float.MaxValue);
        Max = new Vector3(float.MinValue);
        for (var i = 0; i + 2 < P.Length; i += 3)
        {
            var v = new Vector3(P[i], P[i + 1], P[i + 2]);
            Min = Vector3.Min(Min, v);
            Max = Vector3.Max(Max, v);
        }
        return this;
    }
}

/// <summary>What a thing in the yard is, for a click (the page's userData).</summary>
public sealed record DesignPick(string Type, int Id = 0, int Hull = 0, int Copy = 0, string Surface = "", int I = -1, bool Floor = false);

/// <summary>One drawn thing in the yard: its geometry, moved by <see cref="Pos"/>, and its material.</summary>
public sealed class DesignObj
{
    public DesignGeo Geo = null!;
    public Vector3 Pos;
    public Vector3 Tint = Vector3.One;
    public float Alpha = 1, Metal = 0.15f, Rough = 0.7f, Coat, Env;
    public Vector3 Emissive;
    public bool VertexColours, Unlit, Transparent, DepthWrite = true, DepthTest = true, Visible = true;
    public int Order;
    public DesignPick? Pick;
}
