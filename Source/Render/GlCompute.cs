using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// A compute program on the 4.6 path (<see cref="RenderCaps.Compute"/>; docs/render/OPENGL46.md "Compute"): the hook
/// for GPU skinning of the horde and for GPU culling, neither of which is written yet. The source goes through the
/// shader prelude like every other shader (so it starts with <c>#version 410 core</c> and compiles as 460; the 4.1
/// path never compiles it: <see cref="Create"/> returns null there). Storage buffers bind with
/// <see cref="BindStorage"/> (a <c>layout(std430, binding = N) buffer</c> block in the source), the work runs with
/// <see cref="Dispatch"/>, and <see cref="Barrier"/> makes what it wrote visible to the next vertex fetch or read.
/// <c>--gl-bench</c> runs <see cref="SelfCheck"/> first: a kernel that doubles a buffer of floats, read back.
/// </summary>
public sealed unsafe class GlCompute : IDisposable
{
    private GlProgram _program;

    public GlProgram Program => _program;

    private GlCompute(GlProgram program) => _program = program;

    /// <summary>Compiles and links <paramref name="source"/> (a compute shader), or returns null without compute or when it fails (logged).</summary>
    public static GlCompute? Create(string source, string label)
    {
        if (!RenderCaps.Compute)
            return null;
        try
        {
            var cs = CreateShader((GlShaderType)GL_COMPUTE_SHADER).SetSource(RenderCaps.Glsl(source)).Compile();
            var program = CreateProgram();
            Native.glAttachShader(program, cs);
            Native.glLinkProgram(program);
            GlDebug.Label(GL_PROGRAM, program.Id, label);
            int linked;
            Native.glGetProgramiv(program, GL_LINK_STATUS, &linked);
            Native.glDeleteShader(cs);
            if (linked == 0)
            {
                Native.glDeleteProgram(program);
                Console.WriteLine($"[gl] compute program {label} did not link");
                return null;
            }
            return new GlCompute(program);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[gl] compute program {label} off: {ex.Message}");
            return null;
        }
    }

    /// <summary>Binds <paramref name="buffer"/> as the shader storage block at <paramref name="index"/>.</summary>
    public static void BindStorage(uint index, GlBuffer buffer) => Native.glBindBufferBase(GL_SHADER_STORAGE_BUFFER, index, buffer);

    /// <summary>Runs the program over <paramref name="groupsX"/> x <paramref name="groupsY"/> x <paramref name="groupsZ"/> work groups.</summary>
    public void Dispatch(uint groupsX, uint groupsY = 1, uint groupsZ = 1)
    {
        _program.Use();
        Native.glDispatchCompute(groupsX, groupsY, groupsZ);
    }

    /// <summary>After a dispatch: its storage writes are visible to vertex fetches, indirect commands and client reads.</summary>
    public static void Barrier() =>
        Native.glMemoryBarrier((GlBitfield)(GL_SHADER_STORAGE_BARRIER_BIT | GL_VERTEX_ATTRIB_ARRAY_BARRIER_BIT | GL_COMMAND_BARRIER_BIT | GL_BUFFER_UPDATE_BARRIER_BIT));

    private const string CheckSrc = """
        #version 410 core
        layout(local_size_x = 64) in;
        layout(std430, binding = 0) buffer Data { float v[]; };
        void main()
        {
            uint i = gl_GlobalInvocationID.x;
            if (i < v.length())
                v[i] *= 2.0;
        }
        """;

    /// <summary>Doubles 256 floats on the GPU and reads them back: "ok", "off" (4.1 path), or what went wrong.</summary>
    public static string SelfCheck()
    {
        using var k = Create(CheckSrc, "GlCompute.selfCheck");
        if (k == null)
            return RenderCaps.Compute ? "program failed" : "off";
        const int n = 256;
        var data = new float[n];
        for (var i = 0; i < n; i++)
            data[i] = i;
        var b = GenBuffer();
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, b);
        fixed (float* p = data)
            Native.glBufferData(GL_SHADER_STORAGE_BUFFER, n * sizeof(float), p, GL_DYNAMIC_COPY);
        BindStorage(0, b);
        k.Dispatch((n + 63) / 64);
        Barrier();
        fixed (float* p = data)
            Native.glGetBufferSubData(GL_SHADER_STORAGE_BUFFER, 0, n * sizeof(float), p);
        Native.glBindBuffer(GL_SHADER_STORAGE_BUFFER, default);
        Native.glDeleteBuffers(1, &b);
        for (var i = 0; i < n; i++)
            if (data[i] != i * 2f)
                return $"wrong result at {i}: {data[i]} (expected {i * 2})";
        var err = GlDebug.Error();
        return err == 0 ? "ok" : $"GL error 0x{err:X}";
    }

    public void Dispose()
    {
        if (_program.Id != 0)
        {
            Native.glDeleteProgram(_program);
            _program = default;
        }
    }
}
