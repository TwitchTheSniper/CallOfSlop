using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.OpenGlApi;
using static Brutal.OpenGlApi.OpenGl;

namespace CallOfSlop;

/// <summary>
/// GL errors and object names. On the 4.6 path in dev builds the driver reports errors (and warnings) itself through
/// KHR_debug, synchronously, so a "[gl] error" log line names the call that went wrong instead of a later glGetError
/// finding a flag someone set frames ago. <see cref="Error"/> is what the renderers' set-up and smoke lines print: the
/// glGetError flag (still read, it's cheap and works on both paths) or, failing that, the first error the callback
/// caught since the last call. <see cref="Label"/> names programs, buffers and vertex arrays (glObjectLabel) so debug
/// messages and GPU capture tools say "props.instances" rather than "buffer 37".
/// </summary>
public static unsafe class GlDebug
{
    /// <summary>Errors the debug callback has reported (all of them, logged or not).</summary>
    public static int Errors { get; private set; }

    /// <summary>Messages of any kind the callback has logged.</summary>
    public static int Logged { get; private set; }

    /// <summary>Is the debug callback installed?</summary>
    public static bool On { get; private set; }

    private static uint _pendingError;
    private static readonly Dictionary<uint, int> PerId = new();
    private const int MaxPerId = 3, MaxLogged = 200, MaxTraces = 5;

    /// <summary>Installs the KHR_debug callback (4.6 path, dev builds). Notifications are muted; everything else is logged.</summary>
    public static void Install()
    {
        if (!RenderCaps.DebugOutput || On)
            return;
        Native.glEnable((GlCapability)GL_DEBUG_OUTPUT);
        Native.glEnable((GlCapability)GL_DEBUG_OUTPUT_SYNCHRONOUS);
        Native.glDebugMessageControl(GL_DONT_CARE, GL_DONT_CARE, GL_DEBUG_SEVERITY_NOTIFICATION, 0, null, false);
        delegate* unmanaged<uint, uint, uint, uint, int, byte*, void*, void> cb = &Callback;
        Native.glDebugMessageCallback((nint*)cb, null);
        On = true;
        Console.WriteLine("[gl] KHR_debug output on (synchronous; notifications muted)");
    }

    [UnmanagedCallersOnly]
    private static void Callback(uint source, uint type, uint id, uint severity, int length, byte* message, void* user)
    {
        try
        {
            if (type == GL_DEBUG_TYPE_ERROR)
            {
                Errors++;
                if (_pendingError == 0)
                    _pendingError = id == 0 ? 1u : id;
            }
            // Performance hints from some drivers repeat every frame: a few of each id, then quiet.
            PerId.TryGetValue(id, out var n);
            if (n >= MaxPerId || Logged >= MaxLogged)
                return;
            PerId[id] = n + 1;
            Logged++;
            var text = length > 0 ? Encoding.UTF8.GetString(message, length) : new string((sbyte*)message);
            Console.WriteLine($"[gl] {Kind(type)} ({SeverityName(severity)}, id {id}): {text.TrimEnd()}");
            if (type == GL_DEBUG_TYPE_ERROR && Errors <= MaxTraces)
                Console.WriteLine(Environment.StackTrace); // synchronous: the frames above Callback are the call that failed
        }
        catch
        {
            // never throw into the driver
        }
    }

    /// <summary>
    /// The GL error to print in a set-up or smoke log line: glGetError's flag, or the first error the debug callback saw
    /// since the last call (its id), 0 when all is well. Clears both.
    /// </summary>
    public static uint Error()
    {
        var e = (uint)Native.glGetError();
        var p = _pendingError;
        _pendingError = 0;
        return e != 0 ? e : p;
    }

    /// <summary>Names a GL object (no-op on the 4.1 path). <paramref name="kind"/>: GL_PROGRAM, GL_BUFFER, GL_VERTEX_ARRAY, GL_TEXTURE...</summary>
    public static void Label(uint kind, uint id, string name)
    {
        if (!RenderCaps.Labels || id == 0)
            return;
        var n = Math.Min(name.Length, 120);
        var buf = stackalloc byte[n + 1];
        for (var i = 0; i < n; i++)
            buf[i] = (byte)(name[i] < 128 ? name[i] : '?');
        buf[n] = 0;
        Native.glObjectLabel(kind, id, n, buf);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string Kind(uint t) => t switch
    {
        GL_DEBUG_TYPE_ERROR => "error",
        GL_DEBUG_TYPE_DEPRECATED_BEHAVIOR => "deprecated",
        GL_DEBUG_TYPE_UNDEFINED_BEHAVIOR => "undefined behaviour",
        GL_DEBUG_TYPE_PORTABILITY => "portability",
        GL_DEBUG_TYPE_PERFORMANCE => "performance",
        _ => "message",
    };

    private static string SeverityName(uint s) => s switch
    {
        GL_DEBUG_SEVERITY_HIGH => "high",
        GL_DEBUG_SEVERITY_MEDIUM => "medium",
        GL_DEBUG_SEVERITY_LOW => "low",
        _ => "info",
    };
}
