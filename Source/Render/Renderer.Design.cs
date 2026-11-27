using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Ship design bodies (DesignParts.cs): a body's design elements as one merged, vertex-coloured triangle mesh in its frame (made by
/// ConstructRenderDesign.cs), drawn with the box shader's own fragment shader (<see cref="InstancedFragment"/>), so the sun, sky, fog and lamps
/// light it as they light the boxes round it. A vertex is its position, flat normal and colour (as the designer's sRGB, the way every box colour is given). Its triangles cast
/// into the sun shadows, the dark interiors and the lamp shadows as shapes (SunShadows.AddDesign).
/// </summary>
public sealed unsafe partial class Renderer
{
    /// <summary>One merged design mesh on the GPU.</summary>
    public sealed class DesignGpu
    {
        internal GlVertexArray Vao;
        internal GlBuffer Vbo;
        public int Count;
    }

    private GlProgram _designProgram;
    private bool _designReady, _designBroken;
    private MergeLoc _dLoc;

    private bool InitDesign()
    {
        if (_designReady || _designBroken)
            return _designReady;
        try
        {
            const string vsSrc = """
                #version 410 core
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in vec4 aCol;
                uniform mat4 uViewProj;
                uniform mat4 uBody;
                uniform vec3 uOrigin;
                uniform vec3 uCam;
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
                    vec4 w = uBody * vec4(aPos, 1.0);
                    vRel = w.xyz;
                    vWorld = w.xyz + uOrigin;
                    vec3 n = mat3(uBody) * aNormal;
                    if (dot(n, uCam - vWorld) < 0.0)
                        n = -n; // (seen from its other side: lit as the side that faces the eye, as the designer's yard draws it)
                    vNormal = n;
                    vSize = vec3(1.0);
                    vLocal = aPos;
                    vLocalN = aNormal;
                    vBody = aPos;
                    vBodyN = aNormal;
                    vUpL = vec3(uBody[0].y, uBody[1].y, uBody[2].y);
                    bool glow = abs(aCol.a * 255.0 - 254.0) < 0.5; // (a lamp's face, DesignParts.GlowAlpha: unlit and bright, marked for the bloom as the merged boxes' glow is)
                    vModeI = glow ? 2.0 : 0.0;
                    vColorI = glow ? vec4(aCol.rgb, uGlowAlpha) : aCol; // (glass has its alpha: DesignParts.GlassAlpha, drawn blended in ConstructRender.DrawGlass; the rest is 1)
                    vLayerI = 0.0;
                    gl_Position = uViewProj * w;
                }
                """;
            _designProgram = MakeMerged(vsSrc, out _dLoc);
            _program.Use();
            _vao.Bind();
            _designReady = true;
            Console.WriteLine("[render] ship design meshes ready");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[render] ship design meshes off: {ex.Message}");
            _designBroken = true;
            _program.Use();
            _vao.Bind();
        }
        return _designReady;
    }

    /// <summary>A merged design mesh to the GPU (positions, normals, packed colours), into <paramref name="into"/>'s buffers when it has them. Null: none can be drawn here.</summary>
    public DesignGpu? UploadDesign(DesignGpu? into, float[] pos, float[] normal, uint[] colour, int vertices)
    {
        if (Recorder != null || !InitDesign())
            return null;
        var m = into ?? new DesignGpu();
        if (m.Vao.Id == 0)
        {
            m.Vao = GenVertexArray();
            m.Vbo = GenBuffer();
            m.Vao.Bind();
            Native.glBindBuffer(GL_ARRAY_BUFFER, m.Vbo);
            const int stride = 7 * sizeof(float);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, stride, (void*)0);
            EnableVertexAttribArray(0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, stride, (void*)(3 * sizeof(float)));
            EnableVertexAttribArray(1);
            Native.glVertexAttribPointer(2, 4, GL_UNSIGNED_BYTE, true, stride, (void*)(6 * sizeof(float)));
            EnableVertexAttribArray(2);
            GlDebug.Label(GL_VERTEX_ARRAY, m.Vao.Id, "Renderer.design");
        }
        var data = new float[Math.Max(1, vertices) * 7];
        for (var i = 0; i < vertices; i++)
        {
            data[i * 7] = pos[i * 3];
            data[i * 7 + 1] = pos[i * 3 + 1];
            data[i * 7 + 2] = pos[i * 3 + 2];
            data[i * 7 + 3] = normal[i * 3];
            data[i * 7 + 4] = normal[i * 3 + 1];
            data[i * 7 + 5] = normal[i * 3 + 2];
            data[i * 7 + 6] = BitConverter.UInt32BitsToSingle(colour[i]);
        }
        Native.glBindBuffer(GL_ARRAY_BUFFER, m.Vbo);
        fixed (float* p = data)
            Native.glBufferData(GL_ARRAY_BUFFER, data.Length * sizeof(float), p, GL_STATIC_DRAW);
        m.Count = vertices;
        Native.glBindBuffer(GL_ARRAY_BUFFER, _vbo);
        _vao.Bind();
        return m;
    }

    /// <summary>Its GPU buffers go (on the render thread).</summary>
    public void FreeDesign(DesignGpu m)
    {
        if (m.Vao.Id == 0)
            return;
        var b = m.Vbo;
        Native.glDeleteBuffers(1, &b);
        var a = m.Vao;
        Native.glDeleteVertexArrays(1, &a);
        m.Vao = default;
        m.Vbo = default;
        m.Count = 0;
    }

    /// <summary>Draws a merged design mesh at <paramref name="frame"/> (its body's pose; relative to the origin while <see cref="Relative"/>).</summary>
    public void DrawDesign(DesignGpu m, in Matrix4x4 frame)
    {
        if (m.Count == 0 || m.Vao.Id == 0 || !_designReady)
            return;
        MergedUniforms(_designProgram, in _dLoc);
        var f = FromOrigin(frame);
        Native.glUniformMatrix4fv(_dLoc.Body, 1, false, &f.M11);
        m.Vao.Bind();
        Native.glDrawArrays(GL_TRIANGLES, 0, m.Count);
        PerfCounters.Draw(m.Count);
        _program.Use();
        _vao.Bind();
    }
}
