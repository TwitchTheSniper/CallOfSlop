using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Visible beams under spot lamps (Sean, 2026-11-23: the works' high-bay pendants should cast cones of light). Each spot lamp the frame
/// lights (<see cref="ConstructLamps"/>, a part's "light" cone) is drawn as a faint cone of lit dust: one instanced draw with no buffer,
/// each cone's apex, axis, spread and colour in a uniform array, its 16 sides made in the vertex shader. Added to the picture in the
/// blended pass (depth read, never written; the back buffer's alpha, the bloom's mark, left as it is), brightest by the lamp, fading out
/// down its length and at its silhouette, so it never shows a hard edge where it meets the floor. Video > Light beams; --no-lamp-beams.
/// <para>Cost: one draw of at most <see cref="Max"/> x 96 vertices; the fill is a few ALU ops a covered pixel, no texture read.</para>
/// </summary>
public static unsafe class LampBeams
{
    /// <summary>Beams a frame at most (every dynamic light could be a spot lamp).</summary>
    public const int Max = LightGrid.MaxDynamic;

    /// <summary>How far down its range a beam is drawn (the rest of its light is on the floor, not in the air).</summary>
    public const float LengthShare = 0.72f;

    /// <summary>How bright the dust in a beam is, for a lamp of colour 1.</summary>
    public const float Haze = 0.045f;

    /// <summary>The setting (Video: Light beams); <see cref="ForcedOff"/>: --no-lamp-beams.</summary>
    public static bool On = true, ForcedOff;

    /// <summary>Beams drawn last frame (F9, checks).</summary>
    public static int LastDrawn { get; private set; }

    private static readonly Vector4[] Apex = new Vector4[Max], Axis = new Vector4[Max], Colour = new Vector4[Max];
    private static int _count;

    private const int Sides = 16;

    private const string Vs = """
        #version 410 core
        uniform mat4 uViewProj;    // from the render origin
        uniform vec4 uApex[32];    // apex (from the render origin), length (m)
        uniform vec4 uAxis[32];    // axis (unit), tan of the half angle
        out vec3 vRel;             // the point, from the render origin
        out vec3 vN;               // the side's outward normal
        out float vT;              // 0 at the lamp, 1 at the beam's end
        flat out int vBeam;
        const int Sides = 16;
        void main()
        {
            int b = gl_InstanceID;
            int tri = gl_VertexID / 3, corner = gl_VertexID % 3;
            // Each side is two triangles from the apex ring (a small disc round the bulb) to the end ring.
            int side = tri / 2;
            bool far = (tri & 1) == 0 ? corner != 0 : corner == 2;
            int k = side + ((tri & 1) == 0 ? (corner == 2 ? 1 : 0) : (corner == 0 ? 0 : 1));
            float a = float(k) * (6.2831853 / float(Sides));
            vec3 ax = uAxis[b].xyz;
            vec3 u = normalize(abs(ax.y) < 0.9 ? cross(ax, vec3(0.0, 1.0, 0.0)) : cross(ax, vec3(1.0, 0.0, 0.0)));
            vec3 v = cross(ax, u);
            vec3 ring = u * cos(a) + v * sin(a);
            float len = uApex[b].w, tanH = uAxis[b].w;
            float t = far ? 1.0 : 0.0;
            float r = far ? len * tanH : 0.12;
            vec3 p = uApex[b].xyz + ax * (len * t) + ring * r;
            vRel = p;
            vN = normalize(ring - ax * tanH);
            vT = t;
            vBeam = b;
            gl_Position = uViewProj * vec4(p, 1.0);
        }
        """;

    private const string Fs = """
        #version 410 core
        uniform vec3 uEye;         // the camera, from the render origin
        uniform vec4 uColour[32];  // colour times haze
        uniform vec2 uFogRange;
        in vec3 vRel;
        in vec3 vN;
        in float vT;
        flat in int vBeam;
        out vec4 FragColor;
        void main()
        {
            vec3 view = normalize(uEye - vRel);
            // Seen through its middle the beam is thickest; at its silhouette (the side edge-on) it thins to nothing.
            float thick = pow(clamp(abs(dot(normalize(vN), view)), 0.0, 1.0), 1.5);
            float along = (1.0 - vT) * (1.0 - vT) * smoothstep(0.0, 0.06, vT);
            float dist = length(uEye - vRel);
            float fog = uFogRange.y > uFogRange.x ? 1.0 - clamp((dist - uFogRange.x) / (uFogRange.y - uFogRange.x), 0.0, 1.0) : 1.0;
            FragColor = vec4(uColour[vBeam].rgb * (thick * along * fog), 0.0);
        }
        """;

    private static GlProgram _prog;
    private static GlVertexArray _vao;
    private static int _lViewProj, _lApex, _lAxis, _lColour, _lEye, _lFog;
    private static bool _ready, _broken;

    /// <summary>A spot lamp lit this frame (<see cref="ConstructLamps"/>): its light's world point, direction (unit), cosine of its half angle, colour times strength and range.</summary>
    public static void Add(Vector3 at, Vector3 dir, float cosHalf, Vector3 colour, float range)
    {
        if (_count >= Max || !On || ForcedOff || cosHalf <= 0.05f || cosHalf >= 0.9999f)
            return;
        var tan = MathF.Sqrt(1f - cosHalf * cosHalf) / cosHalf;
        Apex[_count] = new Vector4(at, range * LengthShare);
        Axis[_count] = new Vector4(dir, tan);
        Colour[_count] = new Vector4(colour * Haze, 0f);
        _count++;
    }

    /// <summary>Forgets the frame's beams without drawing them (a pass that draws no world).</summary>
    public static void Clear() => _count = 0;

    /// <summary>The frame's beams, in the blended pass after the opaque world; clears the list.</summary>
    public static void Draw(Renderer gfx)
    {
        var n = _count;
        _count = 0;
        LastDrawn = 0;
        if (n == 0 || !On || ForcedOff || CameraFeeds.InPass || !Init())
            return;
        var origin = gfx.Origin;
        for (var i = 0; i < n; i++)
            Apex[i] = new Vector4(new Vector3(Apex[i].X, Apex[i].Y, Apex[i].Z) - origin, Apex[i].W);
        var eye = gfx.CameraPosition - origin;
        var fog = gfx.FogRange;
        _prog.Use();
        var vp = gfx.ViewProjRelative;
        Native.glUniformMatrix4fv(_lViewProj, 1, false, &vp.M11);
        fixed (Vector4* a = Apex)
            Native.glUniform4fv(_lApex, n, &a->X);
        fixed (Vector4* a = Axis)
            Native.glUniform4fv(_lAxis, n, &a->X);
        fixed (Vector4* a = Colour)
            Native.glUniform4fv(_lColour, n, &a->X);
        Native.glUniform3f(_lEye, eye.X, eye.Y, eye.Z);
        Native.glUniform2f(_lFog, fog.X, fog.Y);
        _vao.Bind();
        var cull = Native.glIsEnabled((GlEnum)(uint)GlCapability.CullFace);
        Native.glDisable(GlCapability.CullFace);
        Native.glEnable(GlCapability.Blend);
        Native.glBlendFuncSeparate(GL_ONE, GL_ONE, GL_ZERO, GL_ONE); // (added light; the back buffer's alpha, the bloom's mark, kept)
        Native.glDepthMask(false);
        Native.glDrawArraysInstanced(GL_TRIANGLES, 0, Sides * 6, n);
        PerfCounters.Draw(Sides * 6, n);
        Native.glDepthMask(true);
        Renderer.BlendDefault();
        Native.glDisable(GlCapability.Blend);
        if (cull)
            Native.glEnable(GlCapability.CullFace);
        gfx.AfterFx();
        LastDrawn = n;
    }

    private static bool Init()
    {
        if (_ready || _broken)
            return _ready;
        try
        {
            if (!Vs.Contains($"uApex[{Max}]", StringComparison.Ordinal) || !Fs.Contains($"uColour[{Max}]", StringComparison.Ordinal))
                throw new InvalidOperationException($"LampBeams.Max is {Max} but the shader's arrays are another size");
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(Vs)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(Fs)).Compile();
            _prog = CreateProgram();
            Native.glAttachShader(_prog, vs);
            Native.glAttachShader(_prog, fs);
            Native.glLinkProgram(_prog);
            GlDebug.Label(GL_PROGRAM, _prog.Id, "LampBeams");
            int linked;
            Native.glGetProgramiv(_prog, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("the beam shader did not link");
            vs.Dispose();
            fs.Dispose();
            _lViewProj = Loc("uViewProj\0"u8);
            _lApex = Loc("uApex\0"u8);
            _lAxis = Loc("uAxis\0"u8);
            _lColour = Loc("uColour\0"u8);
            _lEye = Loc("uEye\0"u8);
            _lFog = Loc("uFogRange\0"u8);
            _vao = GenVertexArray(); // (no attributes: everything comes from the vertex and instance numbers)
            _ready = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[lighting] lamp beams off: {ex.Message}");
            _broken = true;
        }
        return _ready;
    }

    private static int Loc(ReadOnlySpan<byte> name)
    {
        fixed (byte* n = name)
            return Native.glGetUniformLocation(_prog, n);
    }
}
