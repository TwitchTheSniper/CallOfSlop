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

    /// <summary>
    /// A slot's covered faces (six bits: 2 x axis + side, side 1 the + face) ride on its look's code as whole steps of this
    /// (away from zero: a glowing box's code is below zero); the vertex shader takes them off and drops those faces of the cube.
    /// </summary>
    public const float MergeMaskStep = 1024f;

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

        /// <summary>
        /// The far detail level: group k's slots are sorted biggest first, and its first <c>GroupClassEnd[k * classes + q]</c>
        /// are those of size class q or bigger (ConstructRenderBatch.cs "SizeClass"); a far chunk draws only those.
        /// </summary>
        public int[] GroupClassEnd = [];
        public int ClassCount = 1;

        /// <summary>Its occlusion query (0: none yet) and the occlusion frame it was last issued in (ConstructRenderOcclusion.cs).</summary>
        internal GlQuery Query;
        internal int QueryFrame = -1;

        /// <summary>
        /// A second query the CPU reads (the parts walked one by one beside it: ConstructRenderBatch.cs): asked again only once its
        /// last answer came, so it never waits and never loses an answer to a newer ask. <see cref="CpuHidden"/> is that answer,
        /// from occlusion frame <see cref="CpuAt"/> (-1: none), asked with the eye at <see cref="CpuEye"/> (the group's own frame).
        /// </summary>
        internal GlQuery CpuQuery;
        internal bool CpuPending, CpuVoid;
        internal int CpuIssued;
        internal Vector3 CpuAskedEye;
        public bool CpuHidden;
        public int CpuAt = -1;
        public Vector3 CpuEye;

        /// <summary>Slots drawn at detail level <paramref name="detail"/> (the smallest size class drawn).</summary>
        public int SlotsUpTo(int detail)
        {
            if (detail >= ClassCount - 1 || GroupClassEnd.Length < Groups * ClassCount)
                return Slots;
            var n = 0;
            for (var g = 0; g < Groups; g++)
                n += GroupClassEnd[g * ClassCount + detail];
            return n;
        }

        /// <summary>Bytes it holds on the GPU.</summary>
        public int Bytes => XformCap + LookCap;
    }

    private GlProgram _mergeProgram;
    private bool _mergeReady, _mergeBroken, _mergeOn;
    private MergeLoc _mLoc;

    /// <summary>The merged program's uniform locations.</summary>
    private struct MergeLoc
    {
        public int ViewProj, Body, Origin, Cam, Fog, FogRange, Glow;
    }

    /// <summary>Merged draws and the slots in them since the last read (the bench, the F9 window).</summary>
    public int MergedDraws, MergedSlots;

    /// <summary>Can draw groups be drawn here (a real GL context, and the shader was made)?</summary>
    public bool MergedMeshes => Recorder == null && !_mergeBroken && (_mergeReady || InitMerged());

    /// <summary>The merged program (never the openings' variant: the surfaces a flush pod opens are walked, Renderer.Cuts.cs); throws if it doesn't link.</summary>
    private GlProgram MakeMerged(string vsSrc, out MergeLoc loc)
    {
        var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vsSrc)).Compile();
        var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(InstancedFragment(false))).Compile();
        var program = CreateProgram();
        Native.glAttachShader(program, vs);
        Native.glAttachShader(program, fs);
        Native.glLinkProgram(program);
        GlDebug.Label(GL_PROGRAM, program.Id, "Renderer.mergeProgram");
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
                    // (faces another box covers: whole steps of 1024 on the code, ConstructRenderBatch.HideCovered; a plain box's cube
                    // has one normal a face, so a dropped face's two triangles go wholly outside the view and are clipped)
                    float codeA = abs(iLook.w);
                    float maskF = floor(codeA / 1024.0);
                    float code = sign(iLook.w) * (codeA - maskF * 1024.0);
                    int mask = int(maskF);
                    if (mask != 0)
                    {
                        vec3 an = abs(aNormal);
                        int face = an.x > 0.5 ? (aNormal.x > 0.0 ? 1 : 0) : an.y > 0.5 ? (aNormal.y > 0.0 ? 3 : 2) : (aNormal.z > 0.0 ? 5 : 4);
                        if ((mask & (1 << face)) != 0)
                        {
                            gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
                            return;
                        }
                    }
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
                    bool glow = code < 0.0;
                    vModeI = glow ? 2.0 : 0.0;
                    vColorI = vec4(iLook.rgb, glow ? uGlowAlpha : 1.0);
                    vLayerI = glow ? 0.0 : code;
                    gl_Position = uViewProj * w;
                }
                """;
            _mergeProgram = MakeMerged(vsSrc, out _mLoc);
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
        m.QueryFrame = -1; // (its bounds may have changed: last frame's query no longer speaks for it)
        m.CpuAt = -1;
        m.CpuVoid = m.CpuPending; // (an answer still on its way speaks for the old bounds)
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
        MergedUniforms(_mergeProgram, in _mLoc);
    }

    /// <summary>The merged program's per-frame uniforms: the camera, fog and glow mark. Leaves it bound.</summary>
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
    }

    /// <summary>
    /// Draws a group at <paramref name="frame"/> (its frame's pose; relative to the origin while <see cref="Relative"/>), its
    /// slots down to size class <paramref name="detail"/>; with <paramref name="occluded"/>, only if last frame's occlusion query
    /// of its box saw any of it (the GPU decides: conditional rendering, no wait on the CPU). Between <see cref="BeginMerged"/> and <see cref="EndMerged"/>.
    /// </summary>
    public void DrawMerged(MergedMesh m, in Matrix4x4 frame, int detail = int.MaxValue, bool occluded = false)
    {
        if (!_mergeOn || m.Slots == 0 || m.Vao.Id == 0)
            return;
        var f = FromOrigin(frame);
        Native.glUniformMatrix4fv(_mLoc.Body, 1, false, &f.M11);
        m.Vao.Bind();
        var conditional = occluded && m.Query.Id != 0 && m.QueryFrame == OcclusionFrame - 1 && Native.glBeginConditionalRender != null;
        if (conditional)
        {
            Native.glBeginConditionalRender(m.Query.Id, GL_QUERY_NO_WAIT);
            OccludedTested++;
        }
        var all = detail >= m.ClassCount - 1 || m.GroupClassEnd.Length < m.Groups * m.ClassCount;
        var slots = 0;
        for (var g = 0; g < m.Groups; g++)
        {
            var shape = m.GroupShape[g];
            int first, count;
            if (shape == 0)
                (first, count) = (0, 36);
            else if (!ShapeRange(shape, out first, out count))
                continue;
            var instances = all ? m.GroupCount[g] : m.GroupClassEnd[g * m.ClassCount + Math.Max(0, detail)];
            if (instances <= 0)
                continue; // (a far chunk's group of small boxes: none drawn)
            if (m.PointedAt != m.GroupFirst[g])
                PointMerged(m, m.GroupFirst[g]);
            PerfCounters.Draw(count, instances);
            Native.glDrawArraysInstanced(GL_TRIANGLES, first, count, instances);
            MergedDraws++;
            slots += instances;
        }
        if (conditional)
            Native.glEndConditionalRender();
        MergedSlots += slots;
    }

    // Occlusion queries (ConstructRenderOcclusion.cs): each merged chunk drawn in the main view has its box tested against the
    // depth just drawn, colour and depth writes off; next frame draws it only where the test saw a sample.
    private GlProgram _occProgram;
    private GlVertexArray _occVao;
    private int _occMvp = -1;
    private bool _occReady, _occBroken, _occOn;

    /// <summary>The occlusion frame: counted once a main view frame (ConstructRender), so a query from the frame before can be told from an older one.</summary>
    public int OcclusionFrame;

    /// <summary>Merged draws that were left to last frame's query, and queries issued, since the last read (the bench, the F9 window).</summary>
    public int OccludedTested, OcclusionQueries;

    /// <summary>Can merged chunks be tested here (queries and conditional rendering, and the shader made)?</summary>
    public bool OcclusionQueriesOn => Recorder == null && !_occBroken && Native.glBeginConditionalRender != null && Native.glGenQueries != null && (_occReady || InitOcclusion());

    private bool InitOcclusion()
    {
        try
        {
            const string vs = """
                #version 410 core
                layout(location = 0) in vec3 aPos;
                uniform mat4 uMvp;
                void main() { gl_Position = uMvp * vec4(aPos, 1.0); }
                """;
            const string fs = """
                #version 410 core
                out vec4 oColor;
                void main() { oColor = vec4(1.0); }
                """;
            var v = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(vs)).Compile();
            var f = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fs)).Compile();
            var program = CreateProgram();
            Native.glAttachShader(program, v);
            Native.glAttachShader(program, f);
            Native.glLinkProgram(program);
            int linked;
            Native.glGetProgramiv(program, GL_LINK_STATUS, &linked);
            if (linked == 0)
            {
                var log = stackalloc byte[2048];
                int len;
                Native.glGetProgramInfoLog(program, 2048, &len, log);
                throw new InvalidOperationException("occlusion shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
            }
            v.Dispose();
            f.Dispose();
            GlDebug.Label(GL_PROGRAM, program.Id, "Renderer.occlusionProgram");
            _occProgram = program;
            fixed (byte* name = "uMvp\0"u8)
                _occMvp = Native.glGetUniformLocation(program, name);
            _occVao = GenVertexArray();
            _occVao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo); // (the box buffer's cube: its first 36 vertices, a unit cube round the middle)
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)0);
            EnableVertexAttribArray(0);
            GlDebug.Label(GL_VERTEX_ARRAY, _occVao.Id, "Renderer.occlusion");
            _program.Use();
            _vao.Bind();
            Console.WriteLine("[render] occlusion queries ready");
            _occReady = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] occlusion queries off: {ex.Message}");
            _occBroken = true;
            _program.Use();
            _vao.Bind();
            return false;
        }
    }

    /// <summary>Starts a run of <see cref="QueryMerged"/> calls: the box program, colour and depth writes off (face culling as the box pass has it: the eye is never inside a tested box).</summary>
    public void BeginOcclusion()
    {
        if (!OcclusionQueriesOn)
            return;
        _occOn = true;
        _occProgram.Use();
        _occVao.Bind();
        Native.glColorMask(false, false, false, false);
        Native.glDepthMask(false);
    }

    /// <summary>Tests a group's box (<paramref name="min"/> to <paramref name="max"/> in <paramref name="frame"/>) against the depth drawn so far: next occlusion frame, <see cref="DrawMerged"/> with <c>occluded</c> draws it only if a sample passed.</summary>
    public void QueryMerged(MergedMesh m, in Matrix4x4 frame, Vector3 min, Vector3 max, Vector3 eye = default)
    {
        if (!_occOn || m.Vao.Id == 0)
            return;
        if (m.Query.Id == 0)
        {
            GlQuery q;
            Native.glGenQueries(1, &q);
            m.Query = q;
        }
        var mvp = Matrix4x4.CreateScale(Vector3.Max(max - min, new Vector3(0.01f))) * Matrix4x4.CreateTranslation((min + max) * 0.5f) * FromOrigin(frame) * _viewProjRel;
        Native.glUniformMatrix4fv(_occMvp, 1, false, &mvp.M11);
        var target = RenderCaps.Gl46 ? GL_ANY_SAMPLES_PASSED_CONSERVATIVE : GL_ANY_SAMPLES_PASSED;
        Native.glBeginQuery(target, m.Query);
        Native.glDrawArrays(GL_TRIANGLES, 0, 36);
        Native.glEndQuery(target);
        m.QueryFrame = OcclusionFrame;
        OcclusionQueries++;
        if (!m.CpuPending && Native.glGetQueryObjectuiv != null)
        {
            if (m.CpuQuery.Id == 0)
            {
                GlQuery q;
                Native.glGenQueries(1, &q);
                m.CpuQuery = q;
            }
            Native.glBeginQuery(target, m.CpuQuery);
            Native.glDrawArrays(GL_TRIANGLES, 0, 36);
            Native.glEndQuery(target);
            (m.CpuPending, m.CpuVoid, m.CpuIssued, m.CpuAskedEye) = (true, false, OcclusionFrame, eye);
            OcclusionQueries++;
        }
    }

    /// <summary>Takes the CPU query's answer if it has come (never waits): <see cref="MergedMesh.CpuHidden"/> and its frame then hold it.</summary>
    public void PollCpuQuery(MergedMesh m)
    {
        if (!m.CpuPending || m.CpuQuery.Id == 0 || Native.glGetQueryObjectuiv == null)
            return;
        int available;
        Native.glGetQueryObjectiv(m.CpuQuery.Id, GL_QUERY_RESULT_AVAILABLE, &available);
        if (available == 0)
            return;
        uint r;
        Native.glGetQueryObjectuiv(m.CpuQuery.Id, GL_QUERY_RESULT, &r);
        m.CpuPending = false;
        if (m.CpuVoid)
            return;
        (m.CpuHidden, m.CpuAt, m.CpuEye) = (r == 0, m.CpuIssued, m.CpuAskedEye);
    }

    /// <summary>The bench, after a glFinish: did this frame's query of <paramref name="m"/> see any of it (false: hidden; null: not tested this frame)?</summary>
    public bool? QuerySaw(MergedMesh m)
    {
        if (m.Query.Id == 0 || m.QueryFrame != OcclusionFrame || Native.glGetQueryObjectuiv == null)
            return null;
        uint r;
        Native.glGetQueryObjectuiv(m.Query.Id, GL_QUERY_RESULT, &r);
        return r != 0;
    }

    /// <summary>Ends a run of occlusion queries: writes and the box shader as they were.</summary>
    public void EndOcclusion()
    {
        if (!_occOn)
            return;
        _occOn = false;
        Native.glColorMask(true, true, true, true);
        Native.glDepthMask(true);
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        _program.Use();
        _vao.Bind();
    }

    /// <summary>Ends a run of merged draws: the box shader and its cube as they were.</summary>
    public void EndMerged()
    {
        if (!_mergeOn)
            return;
        _mergeOn = false;
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
        if (m.Query.Id != 0)
        {
            var q = m.Query;
            Native.glDeleteQueries(1, &q);
            m.Query = default;
        }
        if (m.CpuQuery.Id != 0)
        {
            var q = m.CpuQuery;
            Native.glDeleteQueries(1, &q);
            m.CpuQuery = default;
        }
        (m.CpuPending, m.CpuAt) = (false, -1);
        m.QueryFrame = -1;
        m.Vao = default;
        m.XformVbo = m.LookVbo = default;
        m.XformCap = m.LookCap = 0;
        m.Slots = m.Groups = 0;
        m.PointedAt = -1;
    }
}
