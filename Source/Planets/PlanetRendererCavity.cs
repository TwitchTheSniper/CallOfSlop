using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// A hollow planetoid's cavity in the drawing (docs/planets/PLANETS.md "Hollow planetoids"). The cavity's wall is an exact
/// sphere, so it isn't a mesh: a cube round it is drawn from the inside (its back faces) and each pixel traces its own ray
/// to the sphere's far side, writing that depth. The near side of the sphere is always inside the crust (hidden by the
/// ground in front of it) or a hole dug into the cavity, so the far side is all that can show, from inside the cavity or
/// down a shaft. The glowing core at the centre is traced the same way.
/// <para>
/// Dug cells are left out (a bit a cell, in an 8-bit integer texture array, one layer a face): their columns' own faces,
/// with the roof under each column still solid, are the dug blocks (PlanetRendererDug.cs). Nothing is drawn while the
/// camera is outside the cavity and nothing has been dug through to it.
/// </para>
/// </summary>
public sealed unsafe partial class PlanetRenderer
{
    private const int CavityUnit = 4; // (0: the box texture array, 5-7 and 9: other renderers, 8: the dug masks, 10-11: the pages, 12-14: the light grid)

    private const string CavityVertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos; // a unit cube's corner
        uniform mat4 uViewProj;
        uniform vec3 uCenter;
        uniform float uHalf;
        out vec3 vWorld;

        void main()
        {
            vWorld = uCenter + aPos * uHalf;
            gl_Position = uViewProj * vec4(vWorld, 1.0);
        }
        """;

    private const string CavityFragmentSrc = """
        #version 410 core
        in vec3 vWorld;
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        uniform vec3 uCenter;
        uniform float uInner;   // the cavity's radius
        uniform float uCore;    // the core's radius (0: none)
        uniform vec3 uGlow;
        uniform vec3 uRock;
        uniform int uN;         // cells along a face's side
        uniform usampler2DArray uTouched; // dug cells, a bit each: texel (i / 8, j), layer = face
        out vec4 FragColor;

        float Hash(vec3 p)
        {
            p = fract(p * 0.3183099 + 0.1);
            p *= 17.0;
            return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise(vec3 x)
        {
            vec3 i = floor(x);
            vec3 f = fract(x);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(mix(Hash(i), Hash(i + vec3(1, 0, 0)), f.x), mix(Hash(i + vec3(0, 1, 0)), Hash(i + vec3(1, 1, 0)), f.x), f.y),
                       mix(mix(Hash(i + vec3(0, 0, 1)), Hash(i + vec3(1, 0, 1)), f.x), mix(Hash(i + vec3(0, 1, 1)), Hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
        }

        // Is the cell under direction p dug? (Planetoid.Locate: the face, then the equiangular grid.)
        bool Dug(vec3 p)
        {
            vec3 a = abs(p);
            int face;
            float n, u, v;
            if (a.x >= a.y && a.x >= a.z)
            {
                face = p.x >= 0.0 ? 0 : 1;
                n = a.x; u = p.y; v = p.x >= 0.0 ? -p.z : p.z;
            }
            else if (a.y >= a.z)
            {
                face = p.y >= 0.0 ? 2 : 3;
                n = a.y; u = p.y >= 0.0 ? -p.z : p.z; v = p.x;
            }
            else
            {
                face = p.z >= 0.0 ? 4 : 5;
                n = a.z; u = p.y; v = p.z >= 0.0 ? p.x : -p.x;
            }
            const float k = 4.0 / 3.14159265358979;
            int i = clamp(int(floor((atan(u / n) * k + 1.0) * 0.5 * float(uN))), 0, uN - 1);
            int j = clamp(int(floor((atan(v / n) * k + 1.0) * 0.5 * float(uN))), 0, uN - 1);
            uint bits = texelFetch(uTouched, ivec3(i >> 3, j, face), 0).r;
            return ((bits >> uint(i & 7)) & 1u) != 0u;
        }

        void main()
        {
            vec3 o = uCam - uCenter;
            vec3 d = normalize(vWorld - uCam);
            float b = dot(o, d);
            float disc = b * b - (dot(o, o) - uInner * uInner);
            if (disc <= 0.0)
                discard;
            float t = -b + sqrt(disc); // the far side: the near side is in the crust, or a hole
            if (t <= 0.0)
                discard;
            bool core = false;
            if (uCore > 0.0)
            {
                float dc = b * b - (dot(o, o) - uCore * uCore);
                if (dc > 0.0)
                {
                    float tc = -b - sqrt(dc);
                    if (tc > 0.0 && tc < t)
                    {
                        t = tc;
                        core = true;
                    }
                }
            }
            vec3 p = o + d * t;
            vec3 col;
            if (core)
            {
                // The core: a glowing ball, brightest face on, with slow churning bands.
                vec3 n = normalize(p);
                float face = max(dot(n, -d), 0.0);
                float churn = Noise(p * 0.08) * 0.6 + Noise(p * 0.3) * 0.4;
                col = uGlow * (0.9 + 0.6 * face + 0.35 * churn) + vec3(0.35) * pow(face, 3.0);
            }
            else
            {
                if (Dug(p))
                    discard; // (a dug cell: its columns' faces are the blocks)
                // The wall: rock lit by the core, with a few long glowing seams through it.
                float big = Noise(p * 0.006) * 0.65 + Noise(p * 0.02) * 0.35;
                float n1 = Noise(p * 0.011) * 0.7 + Noise(p * 0.031) * 0.3;
                float vein = pow(1.0 - abs(n1 * 2.0 - 1.0), 40.0) * smoothstep(0.45, 0.65, big);
                float n2 = Noise(p * 0.045 + 17.0);
                vein += 0.35 * pow(1.0 - abs(n2 * 2.0 - 1.0), 60.0) * smoothstep(0.5, 0.7, big);
                float grain = 0.7 + 0.25 * Noise(p * 0.3) + 0.15 * Noise(p * 1.3) + 0.08 * Noise(p * 4.1);
                vec3 rock = uRock * grain * (0.6 + 0.5 * big);
                vec3 lit = rock * (uGlow * 0.75 + vec3(0.22)) * (uCore > 0.0 ? 1.0 : 0.7);
                col = lit + uGlow * vein * 1.8;
            }
            // The cavity's air glows a little with distance.
            float haze = 1.0 - exp(-t / (uInner * 1.4));
            col = mix(col, uGlow * 0.32, haze * (core ? 0.35 : 0.6));
            FragColor = vec4(col, 1.0);
            vec4 clip = uViewProj * vec4(uCenter + p, 1.0);
            gl_FragDepth = clamp(clip.z / clip.w * 0.5 + 0.5, 0.0, 1.0);
        }
        """;

    private GlProgram _cavProgram;
    private GlVertexArray _cavVao;
    private GlBuffer _cavVbo, _cavIbo;
    private uint _cavTex;
    private int _cavN = -1;
    private bool _cavGlReady;
    private int _clViewProj, _clCam, _clCenter, _clHalf, _clInner, _clCore, _clGlow, _clRock, _clN, _clTouched;
    private int _cavGeneration = -1, _cavVersion = -1;
    private readonly Dictionary<int, int> _cavSeen = new();
    private byte[] _cavScratch = [];

    /// <summary>Was the cavity drawn last frame (the self-test)?</summary>
    public bool CavityDrawn { get; private set; }

    private void ResetCavity()
    {
        _cavGeneration = -1;
        _cavVersion = -1;
        _cavSeen.Clear();
        _cavN = -1;
    }

    private void DrawCavity(Renderer gfx, Planetoid pl)
    {
        CavityDrawn = false;
        if (!pl.Hollow || _headless)
            return;
        var cam = gfx.CameraPosition;
        var inside = (cam - pl.Center).Length() < pl.InnerRadius;
        var edits = pl.EditsIfMade;
        if (!inside && edits is not { OpenToCavity: true })
            return; // (from outside the cavity shows only down a hole dug into it)
        if (!_cavGlReady)
            InitCavityGl();
        UpdateCavityCells(pl, edits);
        var vp = gfx.ViewProj;
        _cavProgram.Use();
        Native.glUniformMatrix4fv(_clViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_clCam, cam.X, cam.Y, cam.Z);
        var c = pl.Center;
        Native.glUniform3f(_clCenter, c.X, c.Y, c.Z);
        Native.glUniform1f(_clHalf, (float)(pl.InnerRadius * 1.05));
        Native.glUniform1f(_clInner, (float)pl.InnerRadius);
        Native.glUniform1f(_clCore, pl.CoreRadius);
        Native.glUniform3f(_clGlow, pl.Glow.X, pl.Glow.Y, pl.Glow.Z);
        var rock = pl.Surface.Rock;
        Native.glUniform3f(_clRock, rock.X, rock.Y, rock.Z);
        Native.glUniform1i(_clN, pl.N);
        Native.glUniform1i(_clTouched, CavityUnit);
        Native.glActiveTexture(GL_TEXTURE0 + CavityUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _cavTex);
        Native.glActiveTexture(GL_TEXTURE0);
        Enable(GlCapability.CullFace);
        Native.glCullFace(GlFace.Front); // (the cube's inside: it covers every pixel the sphere's far side can be in)
        _cavVao.Bind();
        PerfCounters.Draw(36);
        Native.glDrawElements(GL_TRIANGLES, 36, GL_UNSIGNED_INT, (void*)0);
        Native.glCullFace(GlFace.Back);
        Native.glDisable(GlCapability.CullFace); // (the scene's passes draw without culling unless they ask)
        _vao.Bind();
        _plain.Program.Use();
        CavityDrawn = true;
    }

    /// <summary>Sends the dug cells' bits for chunks changed since last time (all of them again after the ground is restored).</summary>
    private void UpdateCavityCells(Planetoid pl, PlanetEdits? e)
    {
        var n = pl.N;
        var w = n / 8;
        Native.glActiveTexture(GL_TEXTURE0 + CavityUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _cavTex);
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        var gen = e?.Generation ?? 0;
        if (_cavN != n || gen != _cavGeneration)
        {
            // A new planetoid or the ground restored: every cell untouched.
            if (_cavN != n)
                Native.glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_R8UI, w, n, 6, 0, GL_RED_INTEGER, GL_UNSIGNED_BYTE, null);
            var zero = new byte[w * n];
            fixed (byte* p = zero)
                for (var f = 0; f < 6; f++)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, f, w, n, 1, GL_RED_INTEGER, GL_UNSIGNED_BYTE, p);
            _cavN = n;
            _cavGeneration = gen;
            _cavVersion = -1;
            _cavSeen.Clear();
        }
        if (e != null && e.Version != _cavVersion)
        {
            const int cc = PlanetEdits.ChunkCells; // (32: a chunk's row is 4 bytes)
            if (_cavScratch.Length < cc / 8 * cc)
                _cavScratch = new byte[cc / 8 * cc];
            foreach (var key in e.Chunks)
            {
                var v = e.ChunkVersion(key);
                if (_cavSeen.TryGetValue(key, out var seen) && seen == v)
                    continue;
                _cavSeen[key] = v;
                e.ChunkOf(key, out var face, out var i0, out var j0);
                var rows = Math.Min(cc, n - j0);
                var cols = Math.Min(cc, n - i0);
                Array.Clear(_cavScratch);
                for (var j = 0; j < rows; j++)
                for (var i = 0; i < cols; i++)
                    if (e.Touched(face, i0 + i, j0 + j))
                        _cavScratch[j * (cc / 8) + (i >> 3)] |= (byte)(1 << (i & 7));
                fixed (byte* p = _cavScratch)
                    Native.glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, i0 / 8, j0, face, (cols + 7) / 8, rows, 1, GL_RED_INTEGER, GL_UNSIGNED_BYTE, p);
            }
            _cavVersion = e.Version;
        }
        Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
        Native.glActiveTexture(GL_TEXTURE0);
    }

    private void InitCavityGl()
    {
        GlTexture tex = default;
        Native.glGenTextures(1, &tex);
        _cavTex = tex.Id;
        Native.glActiveTexture(GL_TEXTURE0 + CavityUnit);
        Native.glBindTexture(GL_TEXTURE_2D_ARRAY, _cavTex);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        Native.glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        Native.glActiveTexture(GL_TEXTURE0);
        _cavN = -1;

        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(CavityVertexSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(CavityFragmentSrc)).Compile();
        _cavProgram = CreateProgram();
        Native.glAttachShader(_cavProgram, vs);
        Native.glAttachShader(_cavProgram, fs);
        Native.glLinkProgram(_cavProgram);
        GlDebug.Label(GL_PROGRAM, _cavProgram.Id, "PlanetRenderer cavity");
        int linked;
        Native.glGetProgramiv(_cavProgram, GL_LINK_STATUS, &linked);
        vs.Dispose();
        fs.Dispose();
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(_cavProgram, 2048, &len, log);
            throw new InvalidOperationException("planet cavity shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        _clViewProj = Uniform(_cavProgram, "uViewProj\0"u8);
        _clCam = Uniform(_cavProgram, "uCam\0"u8);
        _clCenter = Uniform(_cavProgram, "uCenter\0"u8);
        _clHalf = Uniform(_cavProgram, "uHalf\0"u8);
        _clInner = Uniform(_cavProgram, "uInner\0"u8);
        _clCore = Uniform(_cavProgram, "uCore\0"u8);
        _clGlow = Uniform(_cavProgram, "uGlow\0"u8);
        _clRock = Uniform(_cavProgram, "uRock\0"u8);
        _clN = Uniform(_cavProgram, "uN\0"u8);
        _clTouched = Uniform(_cavProgram, "uTouched\0"u8);

        // A unit cube, faces wound outwards (counter-clockwise seen from outside).
        float[] corners = [-1, -1, -1, 1, -1, -1, 1, 1, -1, -1, 1, -1, -1, -1, 1, 1, -1, 1, 1, 1, 1, -1, 1, 1];
        uint[] idx =
        [
            0, 3, 2, 0, 2, 1, // -z
            4, 5, 6, 4, 6, 7, // +z
            0, 4, 7, 0, 7, 3, // -x
            1, 2, 6, 1, 6, 5, // +x
            0, 1, 5, 0, 5, 4, // -y
            3, 7, 6, 3, 6, 2, // +y
        ];
        _cavVao = GenVertexArray();
        _cavVao.Bind();
        _cavVbo = GenBuffer();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _cavVbo);
        fixed (float* p = corners)
            Native.glBufferData(GL_ARRAY_BUFFER, corners.Length * sizeof(float), p, GL_STATIC_DRAW);
        _cavIbo = GenBuffer();
        Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, _cavIbo);
        fixed (uint* p = idx)
            Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, idx.Length * sizeof(uint), p, GL_STATIC_DRAW);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 3 * sizeof(float), (void*)0);
        EnableVertexAttribArray(0);
        _vao.Bind();
        _plain.Program.Use();
        _cavGlReady = true;
    }
}
