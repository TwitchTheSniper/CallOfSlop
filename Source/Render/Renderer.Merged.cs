using System.Globalization;
using System.Numerics;
using System.Text;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Merged meshes (docs/render/MERGED-MESHES.md): a draw group's boxes kept on the GPU between edits and drawn with one call
/// for its plain boxes and one for its contoured ones, however many there are. Each box is a slot: its model matrix in the
/// group's frame (three texels of a float texture buffer) and its look (one texel: tint and material code, a glowing box
/// marked by a negative code), so a paint or damage change rewrites the looks alone and an edit the slots it touches. The
/// plain boxes need no vertices at all: the vertex shader makes slot <c>gl_VertexID / 24</c>'s cube from the same corners the
/// box buffer holds (<see cref="BuildCube"/>), through one shared index buffer; contoured boxes have their shape's vertices copied into a small buffer of
/// the group's own, each with its slot. The fragment shader is the box shader's own (<see cref="FragmentSrc"/>), so a merged
/// box looks as it would drawn alone. Plain GL 4.1 (texture buffers, <c>texelFetch</c>): the 4.6 tier takes the same path.
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>Texture units of the merged meshes' slot buffers (0-18 are taken: VideoFeeds.Unit, SurfaceGpu.Unit, PostFx).</summary>
    public const int MergeXformUnit = 19, MergeLookUnit = 20;

    /// <summary>Floats a slot takes: three for its matrix, one for its look (the look is a texel of its own buffer).</summary>
    public const int MergeXformFloats = 12, MergeLookFloats = 4, MergeShapeFloats = 7;

    /// <summary>One draw group on the GPU (a construct segment's chunk now; a fused unit later).</summary>
    public sealed class MergedMesh
    {
        internal GlBuffer XformBuf, LookBuf, ShapeVbo;
        internal uint XformTex, LookTex;
        internal GlVertexArray ShapeVao;
        internal int XformCap, LookCap, ShapeCap; // bytes allocated
        public int Cubes; // slots 0..Cubes-1: plain boxes, drawn from gl_VertexID
        public int ShapeVerts; // the contoured boxes' vertices (each names its slot)
        public int Slots;

        /// <summary>Bytes it holds on the GPU.</summary>
        public int Bytes => XformCap + LookCap + ShapeCap;
    }

    private GlProgram _mergeProgram;
    private GlVertexArray _mergeVao; // no attributes: the plain boxes come from gl_VertexID
    private bool _mergeReady, _mergeBroken, _mergeOn;
    private int _mLocViewProj, _mLocBody, _mLocOrigin, _mLocCam, _mLocFog, _mLocFogRange, _mLocGlow, _mLocFromVbo;

    /// <summary>Merged draws and the slots in them since the last read (the bench, the F9 window).</summary>
    public int MergedDraws, MergedSlots;

    /// <summary>Can draw groups be drawn here (a real GL context, and the shader was made)?</summary>
    public bool MergedMeshes => Recorder == null && !_mergeBroken && (_mergeReady || InitMerged());

    /// <summary>
    /// The 24 distinct corners (with their face normals) of <see cref="BuildCube"/>'s 36 as a GLSL constant, so a merged cube
    /// is the drawn cube to the bit; <see cref="CubeIndex"/> makes the same triangles from them (4 corners a face: a, b, d, e).
    /// </summary>
    private static string CubeGlsl()
    {
        var v = BuildCube();
        var s = new StringBuilder("const vec3 kCube[48] = vec3[48](");
        for (var f = 0; f < 6; f++)
            foreach (var k in (ReadOnlySpan<int>)[0, 1, 2, 5]) // (each face's triangles are a b d, a d e)
            {
                var o = (f * 6 + k) * 6;
                if (f + k > 0)
                    s.Append(", ");
                s.Append(CultureInfo.InvariantCulture, $"vec3({v[o]:0.0###}, {v[o + 1]:0.0###}, {v[o + 2]:0.0###}), vec3({v[o + 3]:0.0###}, {v[o + 4]:0.0###}, {v[o + 5]:0.0###})");
            }
        return s.Append(");").ToString();
    }

    /// <summary>The shared index buffer of the plain boxes: box s's corners are vertex ids s * 24 up (a slot is <c>gl_VertexID / 24</c>).</summary>
    private static ushort[] CubeIndex(int boxes)
    {
        var ix = new ushort[boxes * 36];
        ReadOnlySpan<int> tri = [0, 1, 2, 0, 2, 3];
        for (var s = 0; s < boxes; s++)
            for (var f = 0; f < 6; f++)
                for (var k = 0; k < 6; k++)
                    ix[s * 36 + f * 6 + k] = (ushort)(s * 24 + f * 4 + tri[k]);
        return ix;
    }

    private bool InitMerged()
    {
        try
        {
            var vsSrc = """
                #version 410 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in float aSlot;
                uniform mat4 uViewProj;
                uniform mat4 uBody;
                uniform vec3 uOrigin;
                uniform float uGlowAlpha;
                uniform int uFromVbo;
                uniform samplerBuffer uXform;
                uniform samplerBuffer uLook;
                out vec3 vWorld;
                out vec3 vNormal;
                out vec3 vLocal;
                out vec3 vLocalN;
                flat out vec3 vSize;
                out vec4 vColorI;
                flat out float vModeI;
                flat out float vLayerI;
                CUBE
                void main()
                {
                    vec3 p;
                    vec3 nr;
                    int s;
                    if (uFromVbo == 0)
                    {
                        int v = gl_VertexID % 24;
                        s = gl_VertexID / 24;
                        p = kCube[v * 2];
                        nr = kCube[v * 2 + 1];
                    }
                    else
                    {
                        p = aPos;
                        nr = aNormal;
                        s = int(aSlot + 0.5);
                    }
                    vec4 r0 = texelFetch(uXform, s * 3);
                    vec4 r1 = texelFetch(uXform, s * 3 + 1);
                    vec4 r2 = texelFetch(uXform, s * 3 + 2);
                    mat4 inst = mat4(vec4(r0.xyz, 0.0), vec4(r1.xyz, 0.0), vec4(r2.xyz, 0.0), vec4(r0.w, r1.w, r2.w, 1.0));
                    mat4 model = uBody * inst;
                    vec4 w = model * vec4(p, 1.0);
                    vWorld = w.xyz + uOrigin;
                    vNormal = mat3(model) * nr;
                    vSize = vec3(length(r0.xyz), length(r1.xyz), length(r2.xyz));
                    vLocal = p * vSize;
                    vLocalN = nr;
                    vec4 look = texelFetch(uLook, s);
                    bool glow = look.w < 0.0;
                    vModeI = glow ? 2.0 : 0.0;
                    vColorI = vec4(look.rgb, glow ? uGlowAlpha : 1.0);
                    vLayerI = glow ? 0.0 : look.w;
                    gl_Position = uViewProj * w;
                }
                """.Replace("CUBE", CubeGlsl());
            var fsSrc = FragmentSrc.Replace("uniform vec4 uColor;", "in vec4 vColorI;\n#define uColor vColorI")
                .Replace("uniform float uMode;", "flat in float vModeI;\n#define uMode vModeI")
                .Replace("uniform float uLayer;", "flat in float vLayerI;\n#define uLayer vLayerI");
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vsSrc)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fsSrc)).Compile();
            _mergeProgram = CreateProgram();
            Native.glAttachShader(_mergeProgram, vs);
            Native.glAttachShader(_mergeProgram, fs);
            Native.glLinkProgram(_mergeProgram);
            GlDebug.Label(GL_PROGRAM, _mergeProgram.Id, "Renderer.mergeProgram");
            int linked;
            Native.glGetProgramiv(_mergeProgram, GL_LINK_STATUS, &linked);
            if (linked == 0)
            {
                var log = stackalloc byte[2048];
                int len;
                Native.glGetProgramInfoLog(_mergeProgram, 2048, &len, log);
                throw new InvalidOperationException("merged mesh shader link failed: " + Encoding.UTF8.GetString(log, Math.Max(len, 0)));
            }
            vs.Dispose();
            fs.Dispose();
            Lighting.Upload(_mergeProgram); // (binds it)
            int Loc(ReadOnlySpan<byte> name)
            {
                fixed (byte* p = name)
                    return Native.glGetUniformLocation(_mergeProgram, p);
            }
            _mLocViewProj = Loc("uViewProj\0"u8);
            _mLocBody = Loc("uBody\0"u8);
            _mLocOrigin = Loc("uOrigin\0"u8);
            _mLocCam = Loc("uCam\0"u8);
            _mLocFog = Loc("uFog\0"u8);
            _mLocFogRange = Loc("uFogRange\0"u8);
            _mLocGlow = Loc("uGlowAlpha\0"u8);
            _mLocFromVbo = Loc("uFromVbo\0"u8);
            Native.glUniform1i(Loc("uTex\0"u8), 0);
            Native.glUniform1i(Loc("uXform\0"u8), MergeXformUnit);
            Native.glUniform1i(Loc("uLook\0"u8), MergeLookUnit);
            _mergeVao = GenVertexArray();
            _mergeVao.Bind();
            GlDebug.Label(GL_VERTEX_ARRAY, _mergeVao.Id, "Renderer.mergeCubes");
            var ibo = GenBuffer(); // (the element buffer is the VAO's: bound once, here)
            Native.glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, ibo);
            var ix = CubeIndex(ConstructRender.MergeChunkBoxes);
            fixed (ushort* p = ix)
                Native.glBufferData(GL_ELEMENT_ARRAY_BUFFER, ix.Length * sizeof(ushort), p, GL_STATIC_DRAW);
            GlDebug.Label(GL_BUFFER, ibo.Id, "Renderer.mergeCubeIndex");
            _program.Use();
            _vao.Bind();
            var err = GlDebug.Error();
            if (err != 0)
                Console.WriteLine($"[render] GL error 0x{err:X} while setting up the merged meshes (continuing)");
            Console.WriteLine("[render] merged meshes ready");
            _mergeReady = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] merged meshes off: {ex.Message}");
            _mergeBroken = true;
            _program.Use();
            _vao.Bind();
            return false;
        }
    }

    /// <summary>A texture buffer's storage: grown (never shrunk) to <paramref name="bytes"/>, then the data written.</summary>
    private static void FillTexBuffer(ref GlBuffer buf, ref uint tex, ref int cap, float* data, int bytes, string label)
    {
        if (buf.Id == 0)
        {
            buf = GenBuffer();
            GlTexture t = default;
            Native.glGenTextures(1, &t);
            tex = t.Id;
            GlDebug.Label(GL_BUFFER, buf.Id, label);
        }
        Native.glBindBuffer(GL_TEXTURE_BUFFER, buf);
        if (bytes > cap)
        {
            cap = Math.Max(bytes, cap + cap / 4);
            Native.glBufferData(GL_TEXTURE_BUFFER, cap, null, GL_STATIC_DRAW);
            Native.glActiveTexture(GL_TEXTURE0 + MergeXformUnit);
            Native.glBindTexture(GL_TEXTURE_BUFFER, tex);
            Native.glTexBuffer(GL_TEXTURE_BUFFER, GL_RGBA32F, buf);
            Native.glActiveTexture(GL_TEXTURE0);
        }
        if (bytes > 0)
            Native.glBufferSubData(GL_TEXTURE_BUFFER, 0, bytes, data);
        Native.glBindBuffer(GL_TEXTURE_BUFFER, default);
    }

    /// <summary>A group's slots: <paramref name="slots"/> matrices (<see cref="MergeXformFloats"/> floats each, rows 1-3 with the translation in w), the first <paramref name="cubes"/> plain boxes.</summary>
    public void UploadMergedXforms(MergedMesh m, float[] data, int slots, int cubes)
    {
        fixed (float* p = data)
            FillTexBuffer(ref m.XformBuf, ref m.XformTex, ref m.XformCap, p, slots * MergeXformFloats * sizeof(float), "Renderer.mergeXform");
        m.Slots = slots;
        m.Cubes = cubes;
    }

    /// <summary>A group's looks: one RGBA texel a slot (tint, material code; a code below zero: a glowing box).</summary>
    public void UploadMergedLooks(MergedMesh m, float[] data, int slots)
    {
        fixed (float* p = data)
            FillTexBuffer(ref m.LookBuf, ref m.LookTex, ref m.LookCap, p, slots * MergeLookFloats * sizeof(float), "Renderer.mergeLook");
    }

    /// <summary>A group's contoured boxes: <paramref name="verts"/> vertices of <see cref="MergeShapeFloats"/> floats (unit-cube position, normal, slot).</summary>
    public void UploadMergedShapes(MergedMesh m, float[] data, int verts)
    {
        m.ShapeVerts = verts;
        if (verts == 0)
            return;
        var bytes = verts * MergeShapeFloats * sizeof(float);
        if (m.ShapeVao.Id == 0)
        {
            m.ShapeVbo = GenBuffer();
            m.ShapeVao = GenVertexArray();
            m.ShapeVao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, m.ShapeVbo);
            const int stride = MergeShapeFloats * sizeof(float);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            EnableVertexAttribArray(0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
            EnableVertexAttribArray(1);
            Native.glVertexAttribPointer(2, 1, GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));
            EnableVertexAttribArray(2);
            GlDebug.Label(GL_VERTEX_ARRAY, m.ShapeVao.Id, "Renderer.mergeShapes");
            _vao.Bind();
        }
        Native.glBindBuffer(GL_ARRAY_BUFFER, m.ShapeVbo);
        if (bytes > m.ShapeCap)
        {
            m.ShapeCap = Math.Max(bytes, m.ShapeCap + m.ShapeCap / 4);
            Native.glBufferData(GL_ARRAY_BUFFER, m.ShapeCap, null, GL_STATIC_DRAW);
        }
        fixed (float* p = data)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, bytes, p);
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
    }

    /// <summary>Starts a run of <see cref="DrawMerged"/> calls: the merged shader and this frame's camera, fog and glow mark.</summary>
    public void BeginMerged()
    {
        if (!MergedMeshes)
            return;
        _mergeOn = true;
        _mergeProgram.Use();
        var vp = _viewProjRel;
        Native.glUniformMatrix4fv(_mLocViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_mLocOrigin, Origin.X, Origin.Y, Origin.Z);
        var cam = CameraPosition;
        Native.glUniform3f(_mLocCam, cam.X, cam.Y, cam.Z);
        Native.glUniform3f(_mLocFog, FogColor.X, FogColor.Y, FogColor.Z);
        Native.glUniform2f(_mLocFogRange, FogRange.X, FogRange.Y);
        Native.glUniform1f(_mLocGlow, MarkGlow ? 0f : 1f);
    }

    /// <summary>Draws a group at <paramref name="frame"/> (its frame's pose; relative to the origin while <see cref="Relative"/>). Between <see cref="BeginMerged"/> and <see cref="EndMerged"/>.</summary>
    public void DrawMerged(MergedMesh m, in Matrix4x4 frame)
    {
        if (!_mergeOn || m.Slots == 0)
            return;
        var f = FromOrigin(frame);
        Native.glUniformMatrix4fv(_mLocBody, 1, false, &f.M11);
        Native.glActiveTexture(GL_TEXTURE0 + MergeXformUnit);
        Native.glBindTexture(GL_TEXTURE_BUFFER, m.XformTex);
        Native.glActiveTexture(GL_TEXTURE0 + MergeLookUnit);
        Native.glBindTexture(GL_TEXTURE_BUFFER, m.LookTex);
        if (m.Cubes > 0)
        {
            _mergeVao.Bind();
            Native.glUniform1i(_mLocFromVbo, 0);
            PerfCounters.Draw(m.Cubes * 36);
            Native.glDrawElements(GL_TRIANGLES, m.Cubes * 36, GL_UNSIGNED_SHORT, null);
            MergedDraws++;
        }
        if (m.ShapeVerts > 0)
        {
            m.ShapeVao.Bind();
            Native.glUniform1i(_mLocFromVbo, 1);
            PerfCounters.Draw(m.ShapeVerts);
            Native.glDrawArrays(GL_TRIANGLES, 0, m.ShapeVerts);
            Native.glUniform1i(_mLocFromVbo, 0);
            MergedDraws++;
        }
        MergedSlots += m.Slots;
    }

    /// <summary>Ends a run of merged draws: the box shader, its cube and texture unit 0 as they were.</summary>
    public void EndMerged()
    {
        if (!_mergeOn)
            return;
        _mergeOn = false;
        Native.glActiveTexture(GL_TEXTURE0);
        _program.Use();
        _vao.Bind();
    }

    /// <summary>Frees a group's GL objects (it can be filled again).</summary>
    public void FreeMerged(MergedMesh m)
    {
        if (m.XformBuf.Id != 0)
        {
            var b = m.XformBuf;
            Native.glDeleteBuffers(1, &b);
            var t = new GlTexture(m.XformTex);
            Native.glDeleteTextures(1, &t);
        }
        if (m.LookBuf.Id != 0)
        {
            var b = m.LookBuf;
            Native.glDeleteBuffers(1, &b);
            var t = new GlTexture(m.LookTex);
            Native.glDeleteTextures(1, &t);
        }
        if (m.ShapeVao.Id != 0)
        {
            var b = m.ShapeVbo;
            Native.glDeleteBuffers(1, &b);
            var a = m.ShapeVao;
            Native.glDeleteVertexArrays(1, &a);
        }
        m.XformBuf = m.LookBuf = m.ShapeVbo = default;
        m.ShapeVao = default;
        m.XformTex = m.LookTex = 0;
        m.XformCap = m.LookCap = m.ShapeCap = 0;
        m.Cubes = m.ShapeVerts = m.Slots = 0;
    }
}
