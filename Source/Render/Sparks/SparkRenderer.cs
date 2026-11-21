using System.Numerics;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// Draws the spark pool (<see cref="Sparks"/>: welding and cutting sparks, ricochets, launcher motors): one unit cube
/// instanced per streak, each stretched along its own axis in the vertex shader, unlit and additive (glow adds light,
/// never darkens, needs no sorting), depth-tested but not depth-written. One glDrawArraysInstanced a frame from a
/// streamed buffer (orphaned each upload, no stall). Any setup failure turns it off cleanly.
/// </summary>
public sealed unsafe class SparkRenderer
{
    private const string VertexSrc = """
        #version 410 core
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec4 iCenterWidth; // centre, width
        layout(location = 3) in vec4 iAxisAlpha;   // full-length axis, alpha
        layout(location = 4) in vec4 iColor;
        uniform mat4 uViewProj;
        uniform vec3 uCam;
        out vec3 vColor;
        out vec3 vNormal;
        out vec3 vView;
        void main()
        {
            vec3 ax = iAxisAlpha.xyz;
            float len = length(ax);
            vec3 z = len > 1e-5 ? ax / len : vec3(0.0, 0.0, 1.0);
            vec3 r = abs(z.y) < 0.95 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
            vec3 x = normalize(cross(r, z));
            vec3 y = cross(z, x);
            float w = iCenterWidth.w;
            vec3 p = iCenterWidth.xyz + x * (aPos.x * w) + y * (aPos.y * w) + z * (aPos.z * max(len, w));
            vNormal = x * aNormal.x + y * aNormal.y + z * aNormal.z;
            vView = uCam - p;
            vColor = iColor.rgb * iAxisAlpha.w;
            gl_Position = uViewProj * vec4(p, 1.0);
        }
        """;

    // Additive glow: brightest where the face looks at the camera, so a box reads as a soft streak, not a crate.
    private const string FragmentSrc = """
        #version 410 core
        in vec3 vColor;
        in vec3 vNormal;
        in vec3 vView;
        out vec4 FragColor;
        void main()
        {
            float facing = abs(dot(normalize(vNormal), normalize(vView)));
            FragColor = vec4(vColor * (0.3 + 0.7 * facing * facing), 1.0);
        }
        """;

    private bool _ready, _broken;
    private GlProgram _program;
    private GlVertexArray _vao;
    private GlBuffer _cubeVbo, _instVbo;
    private int _locViewProj, _locCam;
    private readonly float[] _data = new float[Sparks.Capacity * Sparks.FloatsPerInstance];

    /// <summary>
    /// Once per frame, after the opaque scene and the tracers, with the world camera set on <paramref name="gfx"/>:
    /// steps the pool by <paramref name="dt"/> and draws what is alive.
    /// </summary>
    public void DrawWorld(Renderer gfx, float dt)
    {
        Sparks.Update(dt);
        if (Sparks.Count == 0 || _broken || (!_ready && !Init()))
            return;
        var n = Sparks.Write(_data, 0, Sparks.Capacity);
        if (n > 0)
            Pass(gfx, gfx.ViewProj, gfx.CameraPosition, n);
    }

    private void Pass(Renderer gfx, in Matrix4x4 viewProj, Vector3 cam, int n)
    {
        _program.Use();
        var vp = viewProj;
        Native.glUniformMatrix4fv(_locViewProj, 1, false, &vp.M11);
        Native.glUniform3f(_locCam, cam.X, cam.Y, cam.Z);
        _vao.Bind();
        Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
        Native.glBufferData(GL_ARRAY_BUFFER, _data.Length * sizeof(float), null, GL_STREAM_DRAW); // orphan: no stall on last frame's copy
        fixed (float* p = _data)
            Native.glBufferSubData(GL_ARRAY_BUFFER, 0, n * Sparks.FloatsPerInstance * sizeof(float), p);
        Enable(GlCapability.Blend);
        BlendFunc(GL_ONE, GL_ONE);
        DepthMask(false);
        PerfCounters.Draw(36, n); // the perf window's counters (Source/Perf/PerfCounters.cs)
        Native.glDrawArraysInstanced(GL_TRIANGLES, 0, 36, n);
        DepthMask(true);
        Renderer.BlendDefault();
        Native.glDisable(GlCapability.Blend);
        gfx.Rebind();
    }

    private bool Init()
    {
        try
        {
            var vs = CreateShader(GlShaderType.Vertex).SetSource(RenderCaps.Glsl(VertexSrc)).Compile();
            var fs = CreateShader(GlShaderType.Fragment).SetSource(RenderCaps.Glsl(FragmentSrc)).Compile();
            _program = CreateProgram();
            Native.glAttachShader(_program, vs);
            Native.glAttachShader(_program, fs);
            Native.glLinkProgram(_program);
            GlDebug.Label(GL_PROGRAM, _program.Id, "SparkRenderer");
            int linked;
            Native.glGetProgramiv(_program, GL_LINK_STATUS, &linked);
            if (linked == 0)
            {
                var log = stackalloc byte[2048];
                int len;
                Native.glGetProgramInfoLog(_program, 2048, &len, log);
                throw new InvalidOperationException("shader link failed: " + System.Text.Encoding.UTF8.GetString(log, Math.Max(len, 0)));
            }
            vs.Dispose();
            fs.Dispose();
            fixed (byte* p = "uViewProj\0"u8)
                _locViewProj = Native.glGetUniformLocation(_program, p);
            fixed (byte* p = "uCam\0"u8)
                _locCam = Native.glGetUniformLocation(_program, p);

            _vao = GenVertexArray();
            _vao.Bind();
            _cubeVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _cubeVbo);
            var cube = Cube();
            fixed (float* p = cube)
                Native.glBufferData(GL_ARRAY_BUFFER, cube.Length * sizeof(float), p, GL_STATIC_DRAW);
            Native.glVertexAttribPointer(0, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)0);
            Native.glVertexAttribPointer(1, 3, GL_FLOAT, false, 6 * sizeof(float), (void*)(3 * sizeof(float)));
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
            _instVbo = GenBuffer();
            Native.glBindBuffer(GL_ARRAY_BUFFER, _instVbo);
            Native.glBufferData(GL_ARRAY_BUFFER, _data.Length * sizeof(float), null, GL_STREAM_DRAW);
            const int stride = Sparks.FloatsPerInstance * sizeof(float);
            for (uint a = 0; a < 3; a++)
            {
                Native.glVertexAttribPointer(2 + a, 4, GL_FLOAT, false, stride, (void*)(a * 4 * sizeof(float)));
                EnableVertexAttribArray(2 + a);
                Native.glVertexAttribDivisor(2 + a, 1);
            }
            var err = GlDebug.Error();
            if (err != 0)
                Console.WriteLine($"[sparks] GL error 0x{err:X} while setting up (continuing)");
            _ready = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[sparks] disabled, could not set up the renderer: {ex.Message}");
            _broken = true;
            return false;
        }
    }

    private static float[] Cube()
    {
        (Vector3 n, Vector3 u, Vector3 v)[] faces =
        [
            (Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY), (-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX),
        ];
        var data = new List<float>(36 * 6);
        foreach (var (n, u, v) in faces)
        {
            var c = n * 0.5f;
            Vector3 a = c - u * 0.5f - v * 0.5f, b = c + u * 0.5f - v * 0.5f, d = c + u * 0.5f + v * 0.5f, e = c - u * 0.5f + v * 0.5f;
            Vector3[] corners = [a, b, d, a, d, e];
            foreach (var p in corners)
                data.AddRange([p.X, p.Y, p.Z, n.X, n.Y, n.Z]);
        }
        return data.ToArray();
    }
}
