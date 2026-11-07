using System.Collections;
using System.Diagnostics;

namespace CallOfSlop;

/// <summary>
/// .NET crash dumps (board item dev-link-tool-mp-crash-lc01, reopened): a native crash (an access violation in GL or
/// ImGui code, a stack overflow, a fail-fast) leaves no managed trace, so the runtime is asked to write a minidump
/// (crash-PID.dmp in the log folder; on Linux and macOS a crash report JSON with the native and managed stacks too).
/// The runtime reads these settings only from the environment, before it starts, so they are set by whatever starts the
/// game: Start.bat, the updater's launch of a new build (<see cref="UpdateInstaller.LaunchCommand"/>), and otherwise the
/// game itself, which starts itself again once with them set (<see cref="Relaunch"/>; guarded against loops by
/// <see cref="ChildVar"/>). The dumps are logs, never saves: they stay in the log folder and are not packaged.
/// </summary>
public static class CrashDumps
{
    /// <summary>Set in the copy the game starts with dumps on, so it never starts another.</summary>
    public const string ChildVar = "CALLOFSLOP_DUMPS_CHILD";

    /// <summary>Set to 1 to turn the dumps (and the restart) off.</summary>
    public const string OffVar = "CALLOFSLOP_NO_DUMPS";

    public const string DumpPattern = "crash-%p.dmp";

    /// <summary>The variables the runtime needs, for dumps in <paramref name="logFolder"/>.</summary>
    public static (string Name, string Value)[] Vars(string logFolder) =>
    [
        ("DOTNET_DbgEnableMiniDump", "1"),
        ("DOTNET_DbgMiniDumpType", "1"), // (a minidump: the threads' stacks and a little memory, a few MB)
        ("DOTNET_DbgMiniDumpName", Path.Combine(logFolder, DumpPattern)),
        ("DOTNET_CreateDumpDiagnostics", "1"),
        ("DOTNET_EnableCrashReport", "1"), // (Linux and macOS: crash-PID.dmp.crashreport.json beside the dump)
    ];

    /// <summary>Sets the variables in a process's environment (a ProcessStartInfo's, or a test's dictionary).</summary>
    public static void Apply(IDictionary<string, string?> env, string logFolder)
    {
        foreach (var (name, value) in Vars(logFolder))
            env[name] = value;
    }

    /// <summary>Are dumps already on in this environment (Start.bat or the updater set them)?</summary>
    public static bool On(Func<string, string?> env) => env("DOTNET_DbgEnableMiniDump") == "1" && !string.IsNullOrEmpty(env("DOTNET_DbgMiniDumpName"));

    /// <summary>Command-line modes that are not the windowed game (tools, tests, servers): no restart for these.</summary>
    private static readonly string[] Headless =
    [
        "--selftest", "--selftest-only", "--selftest-list", "--net-client", "--crowd-bench", "--export-maps", "--check-map", "--check-maps", "--map-ops", "--weather-sim",
        "--gen-map", "--gen-maps", "--gen-plan", "--gen-vehicle", "--gen-vehicles", "--check-vehicle", "--sweep-vehicle", "--diversity", "--diversity-style", "--kit-report", "--role", "--family", "--convoy", "--identity", "--grade-vehicle", "--grade-report", "--check-devedit", "--check-weapon", "--export-weapons", "--check-part", "--check-construct-parts", "--check-mods",
        "--check-parts", "--export-parts", "--bench-fuse", "--master", "--bake", "--stuck-report", "--verify-update",
    ];

    /// <summary>
    /// Should this start restart itself with dumps on? Only a release build's windowed game, when nothing set them, not under a
    /// debugger, not under Steam (Steam starts the game itself), not in the copy already restarted, and not when turned off.
    /// </summary>
    public static bool ShouldRelaunch(string[] args, Func<string, string?> env, bool release, bool debugger)
    {
        if (!release || debugger || env(ChildVar) == "1" || env(OffVar) == "1" || On(env))
            return false;
        if (!string.IsNullOrEmpty(env("SteamGameId")) || !string.IsNullOrEmpty(env("SteamAppId")))
            return false;
        foreach (var a in args)
            foreach (var h in Headless)
                if (a.Equals(h, StringComparison.OrdinalIgnoreCase))
                    return false;
        return true;
    }

    /// <summary>How this process is started again: the same program (and assembly, under the dotnet host) and arguments, dumps on, the guard set.</summary>
    public static ProcessStartInfo ChildStart(string[] args, string processPath, string entryAssembly, string logFolder)
    {
        var psi = new ProcessStartInfo(processPath) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
        var host = Path.GetFileNameWithoutExtension(processPath);
        if (host.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && entryAssembly.Length > 0)
            psi.ArgumentList.Add(entryAssembly);
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        Apply(psi.Environment, logFolder);
        psi.Environment[ChildVar] = "1";
        return psi;
    }

    /// <summary>
    /// The first thing Main does: when <see cref="ShouldRelaunch"/>, starts the game again with dumps on and waits for it,
    /// returning its exit code; null to carry on in this process (dumps already on, not wanted, or the restart failed).
    /// </summary>
    public static int? Relaunch(string[] args)
    {
        try
        {
            if (!ShouldRelaunch(args, Environment.GetEnvironmentVariable, BuildInfo.IsRelease, Debugger.IsAttached) || Environment.ProcessPath is not { } exe)
                return null;
            var entry = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
            using var child = Process.Start(ChildStart(args, exe, entry, AppFolders.Logs));
            if (child == null)
                return null;
            child.WaitForExit();
            return child.ExitCode;
        }
        catch
        {
            return null; // (no restart: the game runs without dumps)
        }
    }

    /// <summary>The newest crash dumps in the log folder (the start-up report after a crash).</summary>
    public static IEnumerable<string> Recent(string logFolder)
    {
        try
        {
            return new DirectoryInfo(logFolder).GetFiles("crash-*.dmp").OrderByDescending(f => f.LastWriteTimeUtc).Take(3).Select(f => $"{f.Name} ({f.Length / 1024} KB, {f.LastWriteTime:yyyy-MM-dd HH:mm})").ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The environment as a lookup (the self-test passes made-up ones).</summary>
    public static Func<string, string?> Lookup(IDictionary env) => name => env.Contains(name) ? env[name]?.ToString() : null;
}
