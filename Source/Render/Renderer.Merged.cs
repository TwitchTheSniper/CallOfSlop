using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Merged meshes (docs/render/MERGED-MESHES.md): a draw group's boxes kept on the GPU between edits and drawn with one call
/// for its plain boxes and one for each contoured shape in it, however many boxes there are. Each box is a slot: its model
/// matrix in the group's frame (three vec4s, the translation in w) in one buffer and its look (tint and material code, a
/// glowing box marked by a negative code) in another, both per-instance attributes over the box buffer's own cube and
/// shapes, so a paint or damage change rewrites the looks alone. Slots are grouped by shape (the plain boxes first); a
/// group's draw points the instance attributes at its first slot (GL 4.1 has no base instance). The fragment shader is the
/// box shader's own (<see cref="FragmentSrc"/>), so a merged box looks as it would drawn alone. Plain GL 4.1: the 4.6 tier
/// takes the same path.
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>Floats a slot's matrix and its look take.</summary>
    public const int MergeXformFloats = 12, MergeLookFloats = 4;

    /// <summary>One draw group on the GPU (a construct segment's chunk now; a fused unit later).</summary>
    public sealed class MergedMesh
    {
        internal GlVertexArray Vao;
        internal GlBuffer XformVbo, LookVbo;
        internal int XformCap, LookCap; // bytes allocated
        internal int PointedAt = -1; // the slot the instance attributes start at now
        public int Slots;

        /// <summary>The slots by shape: group k is <see cref="GroupCount"/>[k] slots of shape <see cref="GroupShape"/>[k] from slot <see cref="GroupFirst"/>[k].</summary>
        public int[] GroupShape = [], GroupFirst = [], GroupCount = [];
        public int Groups;

        /// <summary>Bytes it holds on the GPU.</summary>
        public int Bytes => XformCap + LookCap;
    }

    private GlProgram _mergeProgram, _mergeCutProgram;
    private bool _mergeReady, _mergeBroken, _mergeOn;
    private MergeLoc _mLoc, _mcLoc;
    private int _mergeCode = -1; // the cut code the bound merged program draws with (0: the plain one; -1: none bound yet)

    /// <summary>A merged program's uniform locations (Cut, Code: -1 in the plain one).</summary>
    private struct MergeLoc
    {
        public int ViewProj, Body, Origin, Cam, Fog, FogRange, Glow, Cut, Code;
    }

    /// <summary>Merged draws and the slots in them since the last read (the bench, the F9 window).</summary>
    public int MergedDraws, MergedSlots;

    /// <summary>Can draw groups be drawn here (a real GL context, and the shader was made)?</summary>
    public bool MergedMeshes => Recorder == null && !_mergeBroken && (_mergeReady || InitMerged());

    /// <summary>The merged program, plain or with the openings (<c>CUTS</c>); throws if it doesn't link.</summary>
    private GlProgram MakeMerged(string vsSrc, bool cuts, out MergeLoc loc)
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vsSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(InstancedFragment(cuts))).Compile();
        var program = CreateProgram();
        Native.glAttachShader(program, vs);
        Native.glAttachShader(program, fs);
        Native.glLinkProgram(program);
        GlDebug.Label(GL_PROGRAM, program.Id, cuts ? "Renderer.mergeCutProgram" : "Renderer.mergeProgram");
        int linked;
        Native.glGetProgramiv(program, GL_LINK_STATUS, &linked);
        if (linked == 0)
        {
            var log = stackalloc byte[2048];
            int len;
            Native.glGetProgramInfoLog(program, 2048, &len, log);
            throw new InvalidOperationException("merged mesh shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
        }
        vs.Dispose();
        fs.Dispose();
        Lighting.Upload(program); // (binds it)
        int Loc(ReadOnlySpan<byte> name)
        {
            fixed (byte* p = name)
                return Native.glGetUniformLocation(program, p);
        }
        loc = new MergeLoc
        {
            ViewProj = Loc("uViewProj\0"u8), Body = Loc("uBody\0"u8), Origin = Loc("uOrigin\0"u8), Cam = Loc("uCam\0"u8),
            Fog = Loc("uFog\0"u8), FogRange = Loc("uFogRange\0"u8), Glow = Loc("uGlowAlpha\0"u8),
            Cut = cuts ? Loc("uCut\0"u8) : -1, Code = cuts ? Loc("uCutCode\0"u8) : -1,
        };
        Native.glUniform1i(Loc("uTex\0"u8), 0);
        return program;
    }

    private bool InitMerged()
    {
        try
        {
            const string vsSrc = """
                #version 410 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in vec4 iR0;
                layout(location = 3) in vec4 iR1;
                layout(location = 4) in vec4 iR2;
                layout(location = 5) in vec4 iLook;
                uniform mat4 uViewProj;
                uniform mat4 uBody;
                uniform vec3 uOrigin;
                uniform float uGlowAlpha;
                uniform float uCutCode; // the cut variant: this group's body's openings (Renderer.Cuts.cs; 0 in the plain one)
                out vec3 vWorld;
                out vec3 vRel;
                out vec3 vNormal;
                out vec3 vLocal;
                out vec3 vLocalN;
                flat out vec3 vSize;
                out vec3 vBody;
                flat out vec3 vBodyN;
                flat out vec3 vUpL;
                out vec4 vColorI;
                flat out float vModeI;
                flat out float vLayerI;
                void main()
                {
                    mat4 inst = mat4(vec4(iR0.xyz, 0.0), vec4(iR1.xyz, 0.0), vec4(iR2.xyz, 0.0), vec4(iR0.w, iR1.w, iR2.w, 1.0));
                    mat4 model = uBody * inst;
                    vec4 w = model * vec4(aPos, 1.0);
                    vRel = w.xyz;
                    vWorld = w.xyz + uOrigin;
                    vNormal = mat3(model) * aNormal;
                    vSize = vec3(length(iR0.xyz), length(iR1.xyz), length(iR2.xyz));
                    vLocal = aPos * vSize;
                    vLocalN = aNormal;
                    vBody = (inst * vec4(aPos, 1.0)).xyz;
                    vBodyN = mat3(inst) * aNormal;
                    vUpL = vec3(model[0].y, model[1].y, model[2].y) / max(vSize, vec3(1e-6));
                    bool glow = iLook.w < 0.0;
                    vModeI = (glow ? 2.0 : 0.0) + 4.0 * uCutCode;
                    vColorI = vec4(iLook.rgb, glow ? uGlowAlpha : 1.0);
                    vLayerI = glow ? 0.0 : iLook.w;
                    gl_Position = uViewProj * w;
                }
                """;
            _mergeProgram = MakeMerged(vsSrc, false, out _mLoc);
            _mergeCutProgram = MakeMerged(vsSrc, true, out _mcLoc); // (boxes round flush pods' pockets: Renderer.Cuts.cs)
            _program.Use();
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

    /// <summary>An array buffer grown (never shrunk) to <paramref name="bytes"/>, then the data written.</summary>
    private static void FillArray(GlBuffer buf, ref int cap, float* data, int bytes)
    {
        Native.glBindBuffer(GL_ARRAY_BUFFER, buf);
        if (bytes > cap)
        {
            cap = Math.Max(bytes, cap + cap / 4);
            Native.glBufferData(GL_ARRAY_BUFFER, cap, null, GL_STATIC_DRAW);
        }
        if (bytes > 0)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, bytes, data);
    }

    private void MakeMerged(MergedMesh m)
    {
        if (m.Vao.Id != 0)
            return;
        m.XformVbo = GenBuffer();
        m.LookVbo = GenBuffer();
        m.Vao = GenVertexArray();
        m.Vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo); // (the box buffer: the cube, then every part shape)
        const int stride = 6 * sizeof(float);
        Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
        EnableVertexAttribArray(0);
        Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
        EnableVertexAttribArray(1);
        for (uint a = 2; a <= 5; a++)
        {
            EnableVertexAttribArray(a);
            Native.glVertexAttribDivisor(a, 1);
        }
        m.PointedAt = -1;
        GlDebug.Label(GL_VERTEX_ARRAY, m.Vao.Id, "Renderer.merged");
        GlDebug.Label(GL_BUFFER, m.XformVbo.Id, "Renderer.mergedXform");
        GlDebug.Label(GL_BUFFER, m.LookVbo.Id, "Renderer.mergedLook");
        _vao.Bind();
    }

    /// <summary>A group's slots: <paramref name="slots"/> matrices of <see cref="MergeXformFloats"/> floats (rows 1-3, the translation in w), in shape groups.</summary>
    public void UploadMergedXforms(MergedMesh m, float[] data, int slots)
    {
        MakeMerged(m);
        fixed (float* p = data)
            FillArray(m.XformVbo, ref m.XformCap, p, slots * MergeXformFloats * sizeof(float));
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        m.Slots = slots;
        m.PointedAt = -1; // (the buffer may have been made again: point the attributes afresh)
    }

    /// <summary>A group's looks: one vec4 a slot (tint, material code; a code below zero: a glowing box).</summary>
    public void UploadMergedLooks(MergedMesh m, float[] data, int slots)
    {
        MakeMerged(m);
        fixed (float* p = data)
            FillArray(m.LookVbo, ref m.LookCap, p, slots * MergeLookFloats * sizeof(float));
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        m.PointedAt = -1;
    }

    /// <summary>Points the instance attributes at slot <paramref name="first"/> (bound VAO).</summary>
    private static void PointMerged(MergedMesh m, int first)
    {
        Native.glBindBuffer(GL_ARRAY_BUFFER, m.XformVbo);
        const int xs = MergeXformFloats * sizeof(float);
        for (uint a = 0; a < 3; a++)
            Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, xs, (void*)((nint)first * xs + a * 4 * sizeof(float)));
        Native.glBindBuffer(GL_ARRAY_BUFFER, m.LookVbo);
        const int ls = MergeLookFloats * sizeof(float);
        Native.glVertexAttribPointer(5, 4, GL_FLOAT, false, ls, (void*)((nint)first * ls));
        m.PointedAt = first;
    }

    /// <summary>Starts a run of <see cref="DrawMerged"/> calls: the merged shader and this frame's camera, fog and glow mark.</summary>
    public void BeginMerged()
    {
        if (!MergedMeshes)
            return;
        if (_shapeVersion != PartShapes.Version)
            UploadShapes(); // (every shape a group names is in the box buffer)
        _mergeOn = true;
        if (_cutCount > 0)
            MergedUniforms(_mergeCutProgram, in _mcLoc);
        MergedUniforms(_mergeProgram, in _mLoc);
        _mergeCode = 0;
    }

    /// <summary>A merged program's per-frame uniforms: the camera, fog, glow mark and (the cut variant) openings. Leaves it bound.</summary>
    private void MergedUniforms(GlProgram program, in MergeLoc loc)
    {
        program.Use();
        var vp = _viewProjRel;
        Native.glUniformMatrix4fv(loc.ViewProj, 1, false, &vp.M11);
        Native.glUniform3f(loc.Origin, Origin.X, Origin.Y, Origin.Z);
        var cam = CameraPosition;
        Native.glUniform3f(loc.Cam, cam.X, cam.Y, cam.Z);
        Native.glUniform3f(loc.Fog, FogColor.X, FogColor.Y, FogColor.Z);
        Native.glUniform2f(loc.FogRange, FogRange.X, FogRange.Y);
        Native.glUniform1f(loc.Glow, MarkGlow ? 0f : 1f);
        if (loc.Cut >= 0)
            PushCuts(loc.Cut, ref _cutMerged);
    }

    /// <summary>Draws a group at <paramref name="frame"/> (its frame's pose; relative to the origin while <see cref="Relative"/>). Between <see cref="BeginMerged"/> and <see cref="EndMerged"/>.</summary>
    public void DrawMerged(MergedMesh m, in Matrix4x4 frame, int cutCode = 0)
    {
        if (!_mergeOn || m.Slots == 0 || m.Vao.Id == 0)
            return;
        if (_cutCount == 0)
            cutCode = 0;
        if (cutCode != _mergeCode)
        {
            // (a body round flush pods: the cut variant with its openings, Renderer.Cuts.cs; the plain one for the rest)
            if (cutCode == 0)
                _mergeProgram.Use();
            else
            {
                if (_mergeCode <= 0)
                    _mergeCutProgram.Use();
                Native.glUniform1f(_mcLoc.Code, cutCode);
            }
            _mergeCode = cutCode;
        }
        var f = FromOrigin(frame);
        Native.glUniformMatrix4fv(cutCode == 0 ? _mLoc.Body : _mcLoc.Body, 1, false, &f.M11);
        m.Vao.Bind();
        for (var g = 0; g < m.Groups; g++)
        {
            var shape = m.GroupShape[g];
            int first, count;
            if (shape == 0)
                (first, count) = (0, 36);
            else if (shape + 1 < _shapeFirst.Count)
                (first, count) = (_shapeFirst[shape], _shapeFirst[shape + 1] - _shapeFirst[shape]);
            else
                continue;
            if (m.PointedAt != m.GroupFirst[g])
                PointMerged(m, m.GroupFirst[g]);
            PerfCounters.Draw(count, m.GroupCount[g]);
            Native.glDrawArraysInstanced(GL_TRIANGLES, first, count, m.GroupCount[g]);
            MergedDraws++;
        }
        MergedSlots += m.Slots;
    }

    /// <summary>Ends a run of merged draws: the box shader and its cube as they were.</summary>
    public void EndMerged()
    {
        if (!_mergeOn)
            return;
        _mergeOn = false;
        _mergeCode = -1;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        _program.Use();
        _vao.Bind();
    }

    /// <summary>Frees a group's GL objects (it can be filled again).</summary>
    public void FreeMerged(MergedMesh m)
    {
        if (m.Vao.Id != 0)
        {
            var b = m.XformVbo;
            Native.glDeleteBuffers(1, &b);
            b = m.LookVbo;
            Native.glDeleteBuffers(1, &b);
            var a = m.Vao;
            Native.glDeleteVertexArrays(1, &a);
        }
        m.Vao = default;
        m.XformVbo = m.LookVbo = default;
        m.XformCap = m.LookCap = 0;
        m.Slots = m.Groups = 0;
        m.PointedAt = -1;
    }
}
