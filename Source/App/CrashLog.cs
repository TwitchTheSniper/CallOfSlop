using System.Text;

namespace CallOfSlop;

/// <summary>
/// Crash logging (board item dev-link-tool-mp-crash-lc01). The game has no console, so a crash used to vanish from
/// callofslop.log without a trace. Every unhandled exception (any thread), every unobserved task exception and the main
/// loop's own catch write a "[crash]" block (the exception with its full stack, the build, the frame phase breadcrumb) to
/// callofslop.log (flushed) and to crash.txt beside it. Windows fail-fast errors (a stack overflow, an access violation in
/// native GL or ImGui code) cannot be caught: <see cref="Phase"/> and <see cref="Detail"/> are plain static fields the
/// frame sets as it goes (string literals: no allocation) and a handler that does run writes them out.
/// </summary>
public static class CrashLog
{
    /// <summary>The frame phase now (a string literal: assigning it costs nothing).</summary>
    public static string Phase = "start";

    /// <summary>A second breadcrumb: the last thing the phase was doing (a literal too).</summary>
    public static string Detail = "";

    private static int _reported;
    private static readonly object _lock = new();

    /// <summary>Hooks the process-wide handlers. Call once, early in Main.</summary>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("unhandled exception" + (e.IsTerminating ? " (terminating)" : ""), e.ExceptionObject as Exception, e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report("unobserved task exception", e.Exception, e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Writes a "[crash]" block to the log and to crash.txt. Safe to call from any thread; never throws.</summary>
    public static string Report(string what, Exception? ex, object? raw = null)
    {
        var text = Format(what, ex, raw);
        try
        {
            lock (_lock)
            {
                Console.WriteLine(text);
                Console.Out.Flush();
                _reported++;
                File.WriteAllText(AppFolders.LogPath("crash.txt"), text);
            }
        }
        catch
        {
            // (a crash report must not crash)
        }
        return text;
    }

    /// <summary>How many reports were written (the self-test).</summary>
    public static int Reported => _reported;

    internal static string Format(string what, Exception? ex, object? raw)
    {
        var sb = new StringBuilder();
        sb.Append("[crash] ").Append(what).Append(" at ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
        sb.Append("[crash] build ").Append(BuildInfo.Label).Append(", protocol ").Append(ServerInfo.ProtocolVersion).Append(", ").Append(Environment.OSVersion).AppendLine();
        sb.Append("[crash] frame phase: ").Append(Phase).Append(Detail.Length > 0 ? " / " + Detail : "").Append(", thread ").Append(Environment.CurrentManagedThreadId).AppendLine();
        sb.Append("[crash] ").Append(ex?.ToString() ?? raw?.ToString() ?? "(no exception object)").AppendLine();
        return sb.ToString();
    }
}
