using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The mass horde's crowd on the GPU (docs/zombies/MASS-HORDE.md, "Rendering"): one instanced draw per level of detail.
/// Each rig (<see cref="CrowdRig"/>) is its boxes' cubes in one vertex buffer (each vertex knows its box), and its walk
/// cycle a float texture; per instance 16 bytes (<see cref="CrowdInstance"/>: position, yaw, walk phase, brightness,
/// the debug tint, held). The vertex shader reads the box's transform for the frame (texelFetch) and places it; the
/// fragment shader is the box shader's own (sun, sky, baked lighting, fog). Both GL paths: the 4.1 path uploads each
/// level's stripes by orphaning and <c>glBufferSubData</c>; on the 4.6 path with the <c>persistent</c> experiment they
/// are copied into a persistently mapped ring (<see cref="StreamRing"/>) and drawn with a base instance. No compute, no
/// indirect draws (Mac). Texture unit <see cref="CrowdAnimUnit"/> while it draws.
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>The crowd's animation texture unit (units 0-2, 5-7 and 12-14 are taken elsewhere).</summary>
    public const int CrowdAnimUnit = 9;

    private sealed class CrowdGl
    {
        public CrowdRig Rig = null!;
        public GlVertexArray Vao;
        public GlBuffer Mesh, Inst;
        public uint Tex;
        public int InstCapacity;
    }

    private CrowdGl[]? _crowd;
    private bool _crowdBroken;
    private GlProgram _crowdProgram;
    private int _cLocViewProj, _cLocCam, _cLocFog, _cLocFogRange, _cLocTex, _cLocAnim, _cLocFrames, _cLocDbgA, _cLocDbgB, _cLocBaseY;
    private StreamRing? _crowdRing;
    private int _crowdRingGen = -1;

    /// <summary>The crowd's debug tier colours: crowd and held back.</summary>
    public static readonly Vector3 CrowdTierYellow = new(0.95f, 0.82f, 0.2f), CrowdTierOrange = new(1f, 0.45f, 0.1f);

    /// <summary>Draws this frame's crowd instances (one instanced draw per level of detail). Nothing headless.</summary>
    public void DrawCrowd(CrowdInstances inst)
    {
        if (Recorder != null || _crowdBroken || inst.Total == 0)
            return;
        if (_crowd == null && !InitCrowd())
            return;
        FlushBatch(); // (anything batched first: the order stays)
        _crowdProgram.Use();
        var vp = _viewProj;
        Native.glUniformMatrix4fv(_cLocViewProj, 1, false, &vp.M11);
        var cam = CameraPosition;
        Native.glUniform3f(_cLocCam, cam.X, cam.Y, cam.Z);
        Native.glUniform3f(_cLocFog, FogColor.X, FogColor.Y, FogColor.Z);
        Native.glUniform2f(_cLocFogRange, FogRange.X, FogRange.Y);
        Native.glUniform3f(_cLocDbgA, CrowdTierYellow.X, CrowdTierYellow.Y, CrowdTierYellow.Z);
        Native.glUniform3f(_cLocDbgB, CrowdTierOrange.X, CrowdTierOrange.Y, CrowdTierOrange.Z);
        Native.glUniform1f(_cLocBaseY, inst.BaseY);
        var ring = RenderCaps.PersistentBuffers ? _crowdRing ??= new StreamRing(CrowdInstances.Bytes, 65536, "Renderer.crowdRing") : null;
        if (ring != null ? _crowdRingGen != ring.Generation : _crowdRingGen >= 0)
        {
            // (Which buffer the vertex arrays read instances from: the ring, or each level's own; --gl-bench flips it.)
            for (var lod = 0; lod < CrowdInstances.Lods; lod++)
            {
                _crowd![lod].Vao.Bind();
                Native.glBindBuffer(GL_ARRAY_BUFFER, ring != null ? ring.Buffer : _crowd[lod].Inst);
                CrowdInstanceAttribs();
            }
            _crowdRingGen = ring?.Generation ?? -1;
        }
        for (var lod = 0; lod < CrowdInstances.Lods; lod++)
        {
            var n = inst.Count[lod];
            if (n == 0)
                continue;
            var c = _crowd![lod];
            Native.glActiveTexture(GL_TEXTURE0 + CrowdAnimUnit);
            Native.glBindTexture(GL_TEXTURE_2D, c.Tex);
            Native.glUniform1f(_cLocFrames, c.Rig.Frames);
            c.Vao.Bind();
            var first = 0;
            if (ring != null)
            {
                // The 4.6 path: every stripe copied into this frame's segment of the mapped ring, drawn from its base.
                var dst = (byte*)ring.Reserve(n, out first);
                foreach (var st in inst.Stripes)
                {
                    var k = st.Count[lod];
                    if (k == 0)
                        continue;
                    var bytes = (long)k * CrowdInstances.Bytes;
                    fixed (CrowdInstance* p = st.Data[lod])
                        System.Buffer.MemoryCopy(p, dst, bytes, bytes);
                    dst += bytes;
                }
                Native.glDrawArraysInstancedBaseInstance(GL_TRIANGLES, 0, 36 * c.Rig.Boxes, n, (uint)first);
            }
            else
            {
                Native.glBindBuffer(GL_ARRAY_BUFFER, c.Inst);
                var total = n * CrowdInstances.Bytes;
                if (total > c.InstCapacity)
                    c.InstCapacity = Math.Max(total, c.InstCapacity * 2);
                Native.glBufferData(GL_ARRAY_BUFFER, c.InstCapacity, null, GL_STREAM_DRAW); // orphan
                var at = 0;
                foreach (var st in inst.Stripes)
                {
                    var k = st.Count[lod];
                    if (k == 0)
                        continue;
                    fixed (CrowdInstance* p = st.Data[lod])
                        Native.glBufferSubData(GL_ARRAY_BUFFER, at, k * CrowdInstances.Bytes, p);
                    at += k * CrowdInstances.Bytes;
                }
                Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36 * c.Rig.Boxes, n);
            }
            PerfCounters.Draw(36 * c.Rig.Boxes, n);
        }
        Native.glActiveTexture(GL_TEXTURE0);
        _program.Use();
        _vao.Bind();
    }

    /// <summary>The instance attributes (16 bytes, <see cref="CrowdInstance"/>) from the bound array buffer, into the bound vertex array.</summary>
    private static void CrowdInstanceAttribs()
    {
        const int istride = CrowdInstances.Bytes;
        Native.glVertexAttribPointer(3, 2, GL_FLOAT, false, istride, (void*)0);
        EnableVertexAttribArray(3);
        Native.glVertexAttribDivisor(3, 1);
        Native.glVertexAttribPointer(4, 2, GL_HALF_FLOAT, false, istride, (void*)8);
        EnableVertexAttribArray(4);
        Native.glVertexAttribDivisor(4, 1);
        Native.glVertexAttribPointer(5, 4, GL_UNSIGNED_BYTE, true, istride, (void*)12);
        EnableVertexAttribArray(5);
        Native.glVertexAttribDivisor(5, 1);
    }

    /// <summary>The crowd's vertex shader: a rig box placed for the instance's frame of the walk cycle.</summary>
    private const string CrowdVertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in float aBox;
        layout(location = 3) in vec2 iXZ;
        layout(location = 4) in vec2 iYPhase;
        layout(location = 5) in vec4 iLook;
        uniform mat4 uViewProj;
        uniform sampler2D uAnim;
        uniform float uFrames;
        uniform float uBaseY;
        uniform vec3 uDbgA;
        uniform vec3 uDbgB;
        out vec3 vWorld;
        out vec3 vNormal;
        out vec4 vColorI;
        flat out float vModeI;
        flat out float vLayerI;
        void main()
        {
            // The frame: the gait (the whole part: walk, run, sprint; the rig's rows gait by gait, CrowdRig.Gaits) and the
            // cycle's phase (the fraction).
            int frames = int(uFrames);
            int gaits = max(textureSize(uAnim, 0).y / frames, 1);
            int gait = clamp(int(floor(iYPhase.y)), 0, gaits - 1);
            int frame = int(floor(fract(iYPhase.y) * uFrames)) % frames + gait * frames;
            int b = int(aBox + 0.5) * 4;
            vec4 r0 = texelFetch(uAnim, ivec2(b, frame), 0);
            vec4 r1 = texelFetch(uAnim, ivec2(b + 1, frame), 0);
            vec4 r2 = texelFetch(uAnim, ivec2(b + 2, frame), 0);
            vec4 r3 = texelFetch(uAnim, ivec2(b + 3, frame), 0);
            // Row vectors (System.Numerics): p * M.
            vec3 local = aPos.x * r0.xyz + aPos.y * r1.xyz + aPos.z * r2.xyz + r3.xyz;
            vec3 n = aNormal.x * r0.xyz + aNormal.y * r1.xyz + aNormal.z * r2.xyz;
            float yaw = iLook.x * 6.28318531;
            float s = sin(yaw), c = cos(yaw);
            // Turned to its yaw (World.FlatForward: yaw 0 faces -z).
            vec3 w = vec3(local.x * c - local.z * s, local.y, local.x * s + local.z * c) + vec3(iXZ.x, iYPhase.x + uBaseY, iXZ.y);
            vWorld = w;
            vNormal = normalize(vec3(n.x * c - n.z * s, n.y, n.x * s + n.z * c));
            float bright = iLook.y * 1.5;
            vec3 col = vec3(r0.w, r1.w, r2.w) * bright;
            vColorI = vec4(mix(col, (iLook.w > 0.5 ? uDbgB : uDbgA) * bright, iLook.z), 1.0);
            vModeI = r3.w;
            vLayerI = 0.0;
            gl_Position = uViewProj * vec4(w, 1.0);
        }
        """;

    private bool InitCrowd()
    {
        try
        {
            var fsSrc = FragmentSrc.Replace("uniform vec4 uColor;", "in vec4 vColorI;\n#define uColor vColorI")
                .Replace("uniform float uMode;", "flat in float vModeI;\n#define uMode vModeI")
                .Replace("uniform float uLayer;", "flat in float vLayerI;\n#define uLayer vLayerI");
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(CrowdVertexSrc)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(fsSrc)).Compile();
            _crowdProgram = CreateProgram();
            Native.glAttachShader(_crowdProgram, vs);
            Native.glAttachShader(_crowdProgram, fs);
            Native.glLinkProgram(_crowdProgram);
            int linked;
            Native.glGetProgramiv(_crowdProgram, GL_LINK_STATUS, &linked);
            if (linked == 0)
                throw new InvalidOperationException("crowd shader link failed");
            vs.Dispose();
            fs.Dispose();
            Lighting.Upload(_crowdProgram);
            int Loc(ReadOnlySpan<byte> name)
            {
                fixed (byte* p = name)
                    return Native.glGetUniformLocation(_crowdProgram, p);
            }
            _cLocViewProj = Loc("uViewProj\0"u8);
            _cLocCam = Loc("uCam\0"u8);
            _cLocFog = Loc("uFog\0"u8);
            _cLocFogRange = Loc("uFogRange\0"u8);
            _cLocTex = Loc("uTex\0"u8);
            _cLocAnim = Loc("uAnim\0"u8);
            _cLocFrames = Loc("uFrames\0"u8);
            _cLocBaseY = Loc("uBaseY\0"u8);
            _cLocDbgA = Loc("uDbgA\0"u8);
            _cLocDbgB = Loc("uDbgB\0"u8);
            _crowdProgram.Use();
            Native.glUniform1i(_cLocTex, 0);
            Native.glUniform1i(_cLocAnim, CrowdAnimUnit);

            var cube = BuildCube(); // 36 vertices x (position, normal)
            _crowd = new CrowdGl[CrowdInstances.Lods];
            for (var lod = 0; lod < CrowdInstances.Lods; lod++)
            {
                var rig = CrowdRig.Bake(lod);
                var c = new CrowdGl { Rig = rig };
                // The rig's mesh: its boxes' cubes, each vertex with its box number.
                var mesh = new float[rig.Boxes * 36 * 7];
                for (var b = 0; b < rig.Boxes; b++)
                    for (var v = 0; v < 36; v++)
                    {
                        var o = (b * 36 + v) * 7;
                        for (var k = 0; k < 6; k++)
                            mesh[o + k] = cube[v * 6 + k];
                        mesh[o + 6] = b;
                    }
                c.Vao = GenVertexArray();
                c.Vao.Bind();
                c.Mesh = GenBuffer();
                Native.glBindBuffer(GL_ARRAY_BUFFER, c.Mesh);
                fixed (float* p = mesh)
                    Native.glBufferData(GL_ARRAY_BUFFER, mesh.Length * sizeof(float), p, GL_STATIC_DRAW);
                const int stride = 7 * sizeof(float);
                Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
                EnableVertexAttribArray(0);
                Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
                EnableVertexAttribArray(1);
                Native.glVertexAttribPointer(2, 1, GL_FLOAT, false, stride, (void*)(6 * sizeof(float)));
                EnableVertexAttribArray(2);
                c.Inst = GenBuffer();
                Native.glBindBuffer(GL_ARRAY_BUFFER, c.Inst);
                c.InstCapacity = 4096 * CrowdInstances.Bytes;
                Native.glBufferData(GL_ARRAY_BUFFER, c.InstCapacity, null, GL_STREAM_DRAW);
                CrowdInstanceAttribs();
                // The walk, run and sprint cycles: a float texture, a row per frame, gait by gait.
                GlTexture tex = default;
                Native.glGenTextures(1, &tex);
                c.Tex = tex.Id;
                Native.glActiveTexture(GL_TEXTURE0 + CrowdAnimUnit);
                Native.glBindTexture(GL_TEXTURE_2D, c.Tex);
                Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
                fixed (float* p = rig.Texels)
                    Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA32F, rig.Width, rig.Rows, 0, GL_RGBA, GL_FLOAT, p);
                Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
                Native.glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
                Native.glActiveTexture(GL_TEXTURE0);
                _crowd[lod] = c;
            }
            _program.Use();
            _vao.Bind();
            var err = (uint)Native.glGetError();
            if (err != 0)
                Console.WriteLine($"[render] GL error 0x{err:X} while setting up the crowd (continuing)");
            Console.WriteLine($"[render] crowd rigs ready: {string.Join(", ", _crowd.Select(c => $"{c.Rig.Name} {c.Rig.Boxes} boxes x {c.Rig.Frames} frames x {c.Rig.Gaits} gaits"))}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] crowd drawing off: {ex.Message}");
            _crowdBroken = true;
            _crowd = null;
            _program.Use();
            _vao.Bind();
            return false;
        }
    }
}
