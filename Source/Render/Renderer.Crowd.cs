using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// The mass horde's crowd on the GPU (docs/zombies/MASS-HORDE.md, "Rendering"): one instanced draw per level of detail.
/// Each rig (<see cref="CrowdRig"/>) is its boxes' cubes in one vertex buffer (each vertex knows its box), and its walk
/// cycle a float texture; per instance only the position, yaw, metres walked, brightness and the debug tint
/// (<see cref="CrowdInstances"/>). The vertex shader reads the box's transform for the frame (texelFetch) and places it;
/// the fragment shader is the box shader's own (sun, sky, baked lighting, fog). OpenGL 3.3/4.1 only: no compute, no
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
    private int _cLocViewProj, _cLocCam, _cLocFog, _cLocFogRange, _cLocTex, _cLocAnim, _cLocFrames, _cLocCycle, _cLocDbgA, _cLocDbgB;

    /// <summary>Instanced crowd draws and the zombies in them since the last read (smoke test, perf).</summary>
    public int CrowdDraws, CrowdZombies;

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
        for (var lod = 0; lod < CrowdInstances.Lods; lod++)
        {
            var n = inst.Count[lod];
            if (n == 0)
                continue;
            var c = _crowd![lod];
            Native.glActiveTexture(GL_TEXTURE0 + CrowdAnimUnit);
            Native.glBindTexture(GL_TEXTURE_2D, c.Tex);
            Native.glUniform1f(_cLocFrames, c.Rig.Frames);
            Native.glUniform1f(_cLocCycle, c.Rig.CycleMetres);
            c.Vao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, c.Inst);
            var bytes = n * CrowdInstances.Floats * sizeof(float);
            if (bytes > c.InstCapacity)
                c.InstCapacity = Math.Max(bytes, c.InstCapacity * 2);
            Native.glBufferData(GL_ARRAY_BUFFER, c.InstCapacity, null, GL_STREAM_DRAW); // orphan
            fixed (float* p = inst.Data[lod])
                Native.glBufferSubData(GL_ARRAY_BUFFER, 0, bytes, p);
            Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36 * c.Rig.Boxes, n);
            CrowdDraws++;
            CrowdZombies += n;
        }
        Native.glActiveTexture(GL_TEXTURE0);
        _program.Use();
        _vao.Bind();
    }

    private bool InitCrowd()
    {
        try
        {
            const string vsSrc = """
                #version 330 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in float aBox;
                layout(location = 3) in vec4 iPosYaw;
                layout(location = 4) in vec4 iWalk;
                uniform mat4 uViewProj;
                uniform sampler2D uAnim;
                uniform float uFrames;
                uniform float uCycle;
                uniform vec3 uDbgA;
                uniform vec3 uDbgB;
                out vec3 vWorld;
                out vec3 vNormal;
                out vec4 vColorI;
                flat out float vModeI;
                flat out float vLayerI;
                void main()
                {
                    // The frame: the walk cycle by metres walked.
                    float cyc = fract(iWalk.x / uCycle);
                    int frame = int(floor(cyc * uFrames)) % int(uFrames);
                    int b = int(aBox + 0.5) * 4;
                    vec4 r0 = texelFetch(uAnim, ivec2(b, frame), 0);
                    vec4 r1 = texelFetch(uAnim, ivec2(b + 1, frame), 0);
                    vec4 r2 = texelFetch(uAnim, ivec2(b + 2, frame), 0);
                    vec4 r3 = texelFetch(uAnim, ivec2(b + 3, frame), 0);
                    // Row vectors (System.Numerics): p * M.
                    vec3 local = aPos.x * r0.xyz + aPos.y * r1.xyz + aPos.z * r2.xyz + r3.xyz;
                    vec3 n = aNormal.x * r0.xyz + aNormal.y * r1.xyz + aNormal.z * r2.xyz;
                    float s = sin(iPosYaw.w), c = cos(iPosYaw.w);
                    // Turned to its yaw (World.FlatForward: yaw 0 faces -z).
                    vec3 w = vec3(local.x * c - local.z * s, local.y, local.x * s + local.z * c) + iPosYaw.xyz;
                    vWorld = w;
                    vNormal = normalize(vec3(n.x * c - n.z * s, n.y, n.x * s + n.z * c));
                    vec3 col = vec3(r0.w, r1.w, r2.w) * iWalk.y;
                    vColorI = vec4(mix(col, iWalk.w > 0.5 ? uDbgB : uDbgA, iWalk.z), 1.0);
                    vModeI = r3.w;
                    vLayerI = 0.0;
                    gl_Position = uViewProj * vec4(w, 1.0);
                }
                """;
            var fsSrc = FragmentSrc.Replace("uniform vec4 uColor;", "in vec4 vColorI;\n#define uColor vColorI")
                .Replace("uniform float uMode;", "flat in float vModeI;\n#define uMode vModeI")
                .Replace("uniform float uLayer;", "flat in float vLayerI;\n#define uLayer vLayerI");
            var vs = CreateShader(GlShaderType.Vertex).SetSource(vsSrc).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(fsSrc).Compile();
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
            _cLocCycle = Loc("uCycle\0"u8);
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
                c.InstCapacity = 4096 * CrowdInstances.Floats * sizeof(float);
                Native.glBufferData(GL_ARRAY_BUFFER, c.InstCapacity, null, GL_STREAM_DRAW);
                const int istride = CrowdInstances.Floats * sizeof(float);
                Native.glVertexAttribPointer(3, 4, GL_FLOAT, false, istride, (void*)0);
                EnableVertexAttribArray(3);
                Native.glVertexAttribDivisor(3, 1);
                Native.glVertexAttribPointer(4, 4, GL_FLOAT, false, istride, (void*)(4 * sizeof(float)));
                EnableVertexAttribArray(4);
                Native.glVertexAttribDivisor(4, 1);
                // The walk cycle: a float texture, a row per frame.
                GlTexture tex = default;
                Native.glGenTextures(1, &tex);
                c.Tex = tex.Id;
                Native.glActiveTexture(GL_TEXTURE0 + CrowdAnimUnit);
                Native.glBindTexture(GL_TEXTURE_2D, c.Tex);
                Native.glPixelStorei(GL_UNPACK_ALIGNMENT, 4);
                fixed (float* p = rig.Texels)
                    Native.glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA32F, rig.Width, rig.Frames, 0, GL_RGBA, GL_FLOAT, p);
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
            Console.WriteLine($"[render] crowd rigs ready: {string.Join(", ", _crowd.Select(c => $"{c.Rig.Name} {c.Rig.Boxes} boxes x {c.Rig.Frames} frames"))}");
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
