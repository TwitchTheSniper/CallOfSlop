using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// What the OpenGL context can do (docs/render/OPENGL46.md). The game asks for a 4.6 core context and falls back to 4.1
/// core when the driver can't make one, always on macOS (4.1 is the most it offers) and with <c>--gl41</c> (to test the
/// fallback on any machine). Two tiers only: <see cref="Gl46"/> is the 4.6 path; otherwise everything runs exactly as
/// the 4.1 game always did. Every 4.6-only path checks one of the feature flags below, which are all off on the 4.1
/// tier even when the driver happens to hand back a newer context than asked for.
///
/// Shaders: every source starts with <c>#version 410 core</c> and goes through <see cref="Glsl"/> before compiling,
/// which swaps that line for the context's prelude (<c>#version 460 core</c> or <c>#version 410 core</c>, then
/// <c>#define GL46 1</c> or <c>0</c>), so one source serves both tiers and a 4.6-only block sits in <c>#if GL46</c>.
///
/// Experiments (<see cref="PersistentBuffers"/>, ...) are on only on the 4.6 tier and when measured worth keeping; each
/// can be turned off with <c>--gl-no=NAME[,NAME]</c> for an A/B (the bench, <c>--gl-bench</c>, does that itself).
/// </summary>
public static unsafe class RenderCaps
{
    /// <summary>The first line of every shader source in the game.</summary>
    public const string SourceVersion = "#version 410 core";

    /// <summary><c>--gl41</c>: ask for 4.1 even where 4.6 is available (tests the fallback).</summary>
    public static bool ForceGl41 { get; set; }

    /// <summary>Was a 4.6 context asked for, and made?</summary>
    public static bool Gl46 { get; private set; }

    /// <summary>The context version the driver reports (it may be higher than asked for).</summary>
    public static int Major { get; private set; }

    public static int Minor { get; private set; }

    /// <summary>GL_RENDERER / GL_VERSION, for logs.</summary>
    public static string Renderer { get; private set; } = "";

    public static string Version { get; private set; } = "";

    /// <summary>Why the 4.6 path is off ("" when on): "--gl41", "macOS", "driver: ..." and so on.</summary>
    public static string FallbackReason { get; private set; } = "not detected yet";

    /// <summary>KHR_debug message callback (4.3 core), used in dev builds instead of polling glGetError (<see cref="GlDebug"/>).</summary>
    public static bool DebugOutput { get; private set; }

    /// <summary>glObjectLabel names on programs, buffers and vertex arrays (4.3 core): they show in debug messages and GPU tools.</summary>
    public static bool Labels { get; private set; }

    /// <summary>glBufferStorage + persistent coherent mapping (4.4 core).</summary>
    public static bool BufferStorage { get; private set; }

    /// <summary>glMultiDrawElementsIndirect and gl_BaseInstance / gl_DrawID (4.3 / 4.6 core).</summary>
    public static bool MultiDrawIndirect { get; private set; }

    /// <summary>Compute shaders and shader storage buffers (4.3 core).</summary>
    public static bool Compute { get; private set; }

    /// <summary>Direct state access (4.5 core).</summary>
    public static bool Dsa { get; private set; }

    /// <summary>
    /// Experiment: per-frame instance data written straight into a persistently mapped, triple-buffered ring (fenced)
    /// instead of glBufferData orphaning + glBufferSubData. Box batches, props, meshes, frenzy particles.
    /// </summary>
    public static bool PersistentBuffers => BufferStorage && !Disabled("persistent");

    /// <summary>The experiments <c>--gl-no=</c> can name (and the bench toggles).</summary>
    public static readonly string[] Experiments = { "persistent" };

    private static readonly HashSet<string> Off = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Is experiment <paramref name="name"/> turned off (<c>--gl-no=</c>, or by the bench)?</summary>
    public static bool Disabled(string name) => Off.Contains(name);

    /// <summary>Turns an experiment off or back on (the bench's A/B; renderers read the flag every frame).</summary>
    public static void SetDisabled(string name, bool off)
    {
        if (off)
            Off.Add(name);
        else
            Off.Remove(name);
    }

    /// <summary>Reads <c>--gl41</c> and <c>--gl-no=a,b</c>.</summary>
    public static void ReadArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (a.Equals("--gl41", StringComparison.OrdinalIgnoreCase))
                ForceGl41 = true;
            else if (a.StartsWith("--gl-no=", StringComparison.OrdinalIgnoreCase))
                foreach (var n in a["--gl-no=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Off.Add(n);
        }
    }

    /// <summary>Should the window ask for a 4.6 context at all? Not on macOS (4.1 at most) or with <c>--gl41</c>.</summary>
    public static bool Want46(out string reason)
    {
        reason = ForceGl41 ? "--gl41" : OperatingSystem.IsMacOS() ? "macOS (OpenGL 4.1 at most)" : "";
        return reason.Length == 0;
    }

    /// <summary>
    /// Reads the made context (call once, after the GL functions are loaded). <paramref name="asked46"/>: the 4.6
    /// context was asked for and made; <paramref name="why"/>: why not, when it wasn't.
    /// </summary>
    public static void Detect(bool asked46, string why)
    {
        int major = 0, minor = 0;
        if (Native.glGetIntegerv != null)
        {
            Native.glGetIntegerv(GL_MAJOR_VERSION, &major);
            Native.glGetIntegerv(GL_MINOR_VERSION, &minor);
        }
        Major = major;
        Minor = minor;
        Renderer = Str(GL_RENDERER);
        Version = Str(GL_VERSION);
        Gl46 = asked46 && major * 10 + minor >= 46;
        FallbackReason = Gl46 ? "" : why.Length > 0 ? why : $"driver gave {major}.{minor}";
        DebugOutput = Gl46 && Native.glDebugMessageCallback != null && Native.glDebugMessageControl != null;
        Labels = Gl46 && Native.glObjectLabel != null;
        BufferStorage = Gl46 && Native.glBufferStorage != null && Native.glMapBufferRange != null && Native.glFenceSync != null
                        && Native.glClientWaitSync != null && Native.glDeleteSync != null;
        MultiDrawIndirect = Gl46 && Native.glMultiDrawElementsIndirect != null;
        Compute = Gl46 && Native.glDispatchCompute != null && Native.glBindBufferBase != null;
        Dsa = Gl46 && Native.glCreateBuffers != null && Native.glNamedBufferSubData != null;
        Console.WriteLine("[gl] " + Describe());
    }

    /// <summary>For tests: pretend a tier without a context (shader preludes only; no feature is on).</summary>
    public static void SetTierForTest(bool gl46)
    {
        Gl46 = gl46;
        DebugOutput = Labels = BufferStorage = MultiDrawIndirect = Compute = Dsa = false;
    }

    /// <summary>One log line: tier, the driver's version and renderer, the features on.</summary>
    public static string Describe()
    {
        var on = new List<string>();
        if (DebugOutput) on.Add("debug output");
        if (Labels) on.Add("labels");
        if (BufferStorage) on.Add("buffer storage");
        if (MultiDrawIndirect) on.Add("multi-draw indirect");
        if (Compute) on.Add("compute");
        if (Dsa) on.Add("DSA");
        var exps = string.Join(", ", Experiments.Select(e => e + (Disabled(e) ? " off" : " on")));
        return $"OpenGL {(Gl46 ? "4.6 path" : "4.1 path")} (context {Major}.{Minor}: {Version}; {Renderer})"
               + (Gl46 ? $", features: {(on.Count > 0 ? string.Join(", ", on) : "none")}; experiments: {exps}" : $", fallback: {FallbackReason}");
    }

    /// <summary>The prelude that replaces <see cref="SourceVersion"/> on this tier.</summary>
    public static string Prelude => Gl46 ? "#version 460 core\n#define GL46 1\n" : "#version 410 core\n#define GL46 0\n";

    /// <summary>
    /// A shader source ready for this context: its first line (<see cref="SourceVersion"/>) becomes <see cref="Prelude"/>.
    /// A source without that line is a bug and throws, so a new shader can't silently skip the prelude.
    /// </summary>
    public static string Glsl(string source)
    {
        if (!source.StartsWith(SourceVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"shader source must start with \"{SourceVersion}\"");
        var rest = source.AsSpan(SourceVersion.Length).TrimStart(' ').TrimStart('\r');
        if (rest.Length > 0 && rest[0] == '\n')
            rest = rest[1..];
        return Prelude + rest.ToString();
    }

    private static string Str(uint name)
    {
        if (Native.glGetString == null)
            return "";
        var p = Native.glGetString(name);
        return p == null ? "" : new string((sbyte*)p);
    }
}
