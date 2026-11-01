using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Crash breadcrumb" (board item dev-link-tool-mp-crash-lc01, reopened: a native crash left a log that just ended): the
    /// breadcrumb file is written in place as the phase, the detail and the frame change, with no allocation; another process
    /// reads it while it is open; a run that never closes it (a crash) is reported by the next open with its phase and frame,
    /// a clean close is not; a second copy of the game at the same time gets breadcrumb-2.txt.
    /// </summary>
    private static void CheckCrashBreadcrumb()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cos-crumb-{Environment.ProcessId}");
        var old = CrashLog.UseBreadcrumb(null);
        var phase0 = CrashLog.Phase;
        var detail0 = CrashLog.Detail;
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            var b = Breadcrumb.Open(dir, "self-test build", out var previous);
            if (b == null)
            {
                Fail("breadcrumb: the file didn't open");
                return;
            }
            if (previous != null)
                Fail($"breadcrumb: a new file reported a previous run: {previous}");
            CrashLog.UseBreadcrumb(b);
            // The game's per-frame writes, warmed, then measured.
            string[] phases = ["events", "input", "game update", "render", "ui", "swap"];
            for (var i = 0; i < 200; i++)
            {
                CrashLog.NextFrame();
                CrashLog.Phase = phases[i % phases.Length];
                CrashLog.Detail = i % 2 == 0 ? "port markers" : "";
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 20000; i++)
            {
                CrashLog.NextFrame();
                CrashLog.Phase = phases[i % phases.Length];
                CrashLog.Detail = i % 2 == 0 ? "port markers" : "input";
            }
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (bytes != 0)
                Fail($"breadcrumb: 20,000 frames of breadcrumb writes allocated {bytes} B (want 0)");
            CrashLog.Phase = "linking tool";
            CrashLog.Detail = "port markers";
            var frame = CrashLog.Frame;
            var text = Breadcrumb.ReadShared(b.Path); // (another process reading it while the game has it open)
            if (!text.Contains("phase: linking tool") || !text.Contains("detail: port markers") || !text.Contains($"frame: {frame}") || !text.Contains("state: running")
                || text.Length != Breadcrumb.Size)
                Fail($"breadcrumb: the open file doesn't say the phase, detail, frame {frame} and running ({text.Length} B): {text.Replace('\n', '|')}");
            // A second copy of the game meanwhile: its own file.
            var b2 = Breadcrumb.Open(dir, "self-test build", out _);
            if (b2 == null || !b2.Path.EndsWith("breadcrumb-2.txt", StringComparison.Ordinal))
                Fail($"breadcrumb: a second copy got {b2?.Path ?? "no file"}, not breadcrumb-2.txt");
            b2?.Close(clean: true);
            // The run dies (never closed cleanly): the next start reports what it was doing.
            CrashLog.UseBreadcrumb(null);
            b.Close(clean: false);
            var b3 = Breadcrumb.Open(dir, "self-test build", out previous);
            if (previous == null || !previous.Contains("phase linking tool / port markers") || !previous.Contains($"frame {frame}") || !previous.Contains("running"))
                Fail($"breadcrumb: after a crash the next start reported \"{previous}\"");
            b3?.Close(clean: true);
            var b4 = Breadcrumb.Open(dir, "self-test build", out previous);
            if (previous != null)
                Fail($"breadcrumb: after a clean exit the next start still reported \"{previous}\"");
            if (b4 != null)
            {
                b4.MarkCrashed();
                b4.Close(clean: false);
            }
            if (Breadcrumb.Summary(File.ReadAllText(Path.Combine(dir, "breadcrumb.txt"))) is not { } crashed || !crashed.Contains("crashed"))
                Fail("breadcrumb: a run that wrote a [crash] report isn't reported as crashed");
            if (Breadcrumb.Summary("something else") != null)
                Fail("breadcrumb: a file that isn't a breadcrumb was read as one");
            Line($"breadcrumb: {Breadcrumb.Size} B mapped file, 20,000 frames of writes allocated {bytes} B");
        }
        catch (Exception ex)
        {
            Fail($"breadcrumb: {ex.GetType().Name} {ex.Message}");
        }
        finally
        {
            CrashLog.UseBreadcrumb(old);
            CrashLog.Phase = phase0;
            CrashLog.Detail = detail0;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // (a temp folder)
            }
        }
    }

    /// <summary>
    /// "Crash dumps start-up" (lc01, reopened): the .NET crash dump settings are set by everything that starts the game:
    /// Start.bat, the updater's launch of a new build, and the game's own one-time restart, which only a release build's
    /// windowed game does (not the tools, tests or servers, not under a debugger or Steam, not when Start.bat or the updater
    /// already set them, never twice: the guard variable), passing the same program, assembly and arguments.
    /// </summary>
    private static void CheckCrashDumpStartup()
    {
        var logs = Path.Combine(Path.GetTempPath(), "cos-logs");
        var want = CrashDumps.Vars(logs);
        bool Has(IDictionary<string, string?> env, string where)
        {
            foreach (var (name, value) in want)
                if (!env.TryGetValue(name, out var v) || v != value)
                {
                    Fail($"crash dumps: {where} doesn't set {name}={value} (has \"{(env.TryGetValue(name, out var got) ? got : null)}\")");
                    return false;
                }
            return true;
        }
        if (!want.Any(v => v.Name == "DOTNET_DbgMiniDumpName" && v.Value.EndsWith("crash-%p.dmp", StringComparison.Ordinal) && v.Value.StartsWith(logs, StringComparison.Ordinal)))
            Fail("crash dumps: the dump name isn't crash-%p.dmp in the log folder");
        Func<string, string?> none = _ => null;
        if (!CrashDumps.ShouldRelaunch(["host", "7777"], none, release: true, debugger: false))
            Fail("crash dumps: a release game started with nothing set doesn't restart itself with dumps on");
        if (!CrashDumps.ShouldRelaunch([], none, release: true, debugger: false))
            Fail("crash dumps: a release game started plainly doesn't restart itself with dumps on");
        foreach (var (args, env, release, debugger, why) in new (string[], Func<string, string?>, bool, bool, string)[]
                 {
                     ([], name => name == CrashDumps.ChildVar ? "1" : null, true, false, "the restarted copy (the loop guard)"),
                     ([], name => name switch { "DOTNET_DbgEnableMiniDump" => "1", "DOTNET_DbgMiniDumpName" => "x.dmp", _ => null }, true, false, "dumps already set"),
                     ([], name => name == CrashDumps.OffVar ? "1" : null, true, false, "turned off"),
                     ([], name => name == "SteamGameId" ? "1" : null, true, false, "under Steam"),
                     ([], none, false, false, "a dev build"),
                     ([], none, true, true, "a debugger"),
                     (["--selftest"], none, true, false, "the self-test"),
                     (["--selftest-only", "X"], none, true, false, "a filtered self-test"),
                     (["--master"], none, true, false, "a server list"),
                     (["--net-client"], none, true, false, "a headless client"),
                     (["--verify-update", "a", "b"], none, true, false, "the update check"),
                 })
            if (CrashDumps.ShouldRelaunch(args, env, release, debugger))
                Fail($"crash dumps: the game restarts itself for {why}");
        // The restart: same program, assembly and arguments; dumps on; the guard set.
        var child = CrashDumps.ChildStart(["join", "1.2.3.4", "--lag=50"], "/opt/dotnet/dotnet", "/game/CallOfSlop.dll", logs);
        if (Has(child.Environment, "the restart") && (child.Environment.TryGetValue(CrashDumps.ChildVar, out var guard) ? guard : null) != "1")
            Fail("crash dumps: the restarted copy has no loop guard");
        if (!child.ArgumentList.SequenceEqual(["/game/CallOfSlop.dll", "join", "1.2.3.4", "--lag=50"]) || child.UseShellExecute)
            Fail($"crash dumps: the restart under the dotnet host runs \"{string.Join(' ', child.ArgumentList)}\"");
        var exeChild = CrashDumps.ChildStart(["host"], Path.Combine("C:", "Games", "CallOfSlop.exe"), "C:/Games/CallOfSlop.dll", logs);
        if (!exeChild.ArgumentList.SequenceEqual(["host"]))
            Fail($"crash dumps: the restart of the exe runs \"{string.Join(' ', exeChild.ArgumentList)}\"");
        if (CrashDumps.ShouldRelaunch(["host"], CrashDumps.Lookup(new System.Collections.Hashtable(child.Environment.ToDictionary(k => k.Key, k => (object?)k.Value))), true, false))
            Fail("crash dumps: the restarted copy would restart again");
        // The updater's launch of a new build.
        var folder = Path.Combine(Path.GetTempPath(), "cos-newbuild");
        var launch = UpdateInstaller.LaunchCommand(Path.Combine(folder, "CallOfSlop.exe"));
        var launchName = launch.Environment.TryGetValue("DOTNET_DbgMiniDumpName", out var ln) ? ln ?? "" : "";
        if (launch.Environment.TryGetValue("DOTNET_DbgEnableMiniDump", out var on) ? on != "1" : true)
            Fail("crash dumps: the updater starts a new build without dumps on");
        else if (!launchName.StartsWith(folder, StringComparison.Ordinal) || !launchName.EndsWith("crash-%p.dmp", StringComparison.Ordinal))
            Fail($"crash dumps: the updater's new build writes dumps to \"{launchName}\", not its own folder");
        var launchDll = UpdateInstaller.LaunchCommand(Path.Combine(folder, "CallOfSlop.dll"));
        if (!launchDll.Environment.ContainsKey("DOTNET_DbgEnableMiniDump"))
            Fail("crash dumps: the updater's dotnet launch has no dumps on");
        // Start.bat (the repo's; CI and a dev checkout have it).
        var dirInfo = new DirectoryInfo(AppContext.BaseDirectory);
        while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "Start.bat")))
            dirInfo = dirInfo.Parent;
        if (dirInfo == null)
            Line("crash dumps: no Start.bat above the program folder (not a checkout): not checked");
        else
        {
            var bat = File.ReadAllText(Path.Combine(dirInfo.FullName, "Start.bat"));
            var start = bat.IndexOf("CallOfSlop.exe\" %*", StringComparison.Ordinal);
            foreach (var (name, value) in want)
            {
                var line = name == "DOTNET_DbgMiniDumpName" ? "set \"DOTNET_DbgMiniDumpName=%~dp0bin\\Release\\net10.0\\crash-%%p.dmp\"" : $"set {name}={value}";
                var at = bat.IndexOf(line, StringComparison.Ordinal);
                if (at < 0 || start < 0 || at > start)
                    Fail($"crash dumps: Start.bat doesn't set {name} before it starts the game (want: {line})");
            }
        }
        Line($"crash dumps: {want.Length} variables set by the restart, the updater and Start.bat");
    }

    /// <summary>
    /// "HUD text out of frame" (lc01, reopened: the root cause): HUD text measured or drawn outside an ImGui frame (between
    /// Render and the next NewFrame, where the game's input runs) used to dereference ImGui's null font in native code; now
    /// it is skipped and counted. Inside a frame it draws. ImGui's error recovery is on: a window left open (a missing End)
    /// is counted and recovered at the end of the frame instead of aborting the process.
    /// </summary>
    private static void CheckHudOutOfFrame()
    {
        var ctx = ImGui.CreateContext();
        var errors0 = Ui.ImGuiErrors;
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = default;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;
            io.DisplaySize = new float2(1280, 720);
            io.DeltaTime = 1f / 60;
            Ui.SafeErrors(io);
            var eye = new Vector3(0, 1.6f, 4f);
            var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
            var proj = Matrix4x4.CreatePerspectiveFieldOfView(1.2f, 1280f / 720f, 0.05f, 500f);
            Hud.Camera = new HudCamera(view * proj, eye, Vector3.Normalize(-eye), true);
            // Before any frame, then between frames (after Render): skipped and counted, no crash.
            var oof = Hud.OutOfFrame;
            if (Hud.WorldLabel(Vector3.Zero, "Data out", Vector3.One, 1f, 13f))
                Fail("hud out of frame: a label was drawn before the first frame");
            ImGui.NewFrame();
            ImGui.Render();
            FakeTextureUploads(ImGui.GetDrawData());
            if (Hud.WorldLabel(Vector3.Zero, "Data out", Vector3.One, 1f, 13f) || Hud.TextWidth("Data out", 13f) != 0f)
                Fail("hud out of frame: a label was drawn (or measured) between Render and NewFrame");
            Hud.ShadowText(default, new float2(10, 10), 13f, "Data out", Vector3.One, 1f); // (no draw list either: the guard comes first)
            if (Hud.OutOfFrame - oof != 4)
                Fail($"hud out of frame: {Hud.OutOfFrame - oof} out-of-frame calls counted, want 4");
            // Inside a frame: drawn; NaN positions and sizes refused.
            ImGui.NewFrame();
            oof = Hud.OutOfFrame;
            if (!Hud.WorldLabel(Vector3.Zero, "Data out", Vector3.One, 1f, 13f))
                Fail("hud out of frame: a label in front of the camera wasn't drawn inside a frame");
            if (Hud.WorldLabel(new Vector3(float.NaN, 0, 0), "Data out", Vector3.One, 1f, 13f))
                Fail("hud out of frame: a label at NaN was drawn");
            Hud.ShadowText(ImGui.GetBackgroundDrawList(), new float2(float.NaN, 10), 13f, "x", Vector3.One, 1f);
            if (Hud.OutOfFrame != oof)
                Fail("hud out of frame: a call inside a frame was counted as out of frame");
            ImGui.Render();
            if (ImGui.GetDrawData().TotalVtxCount == 0)
                Fail("hud out of frame: nothing was drawn inside the frame");
            FakeTextureUploads(ImGui.GetDrawData());
            ImString.SharedStorage.Reset();
            // A window left open: recovered and counted, not an abort.
            ImGui.NewFrame();
            ImGui.Begin("left open");
            ImGui.PushID(7);
            ImGui.Render();
            Ui.CountImGuiErrors();
            FakeTextureUploads(ImGui.GetDrawData());
            if (Ui.ImGuiErrors == errors0)
                Fail("hud out of frame: a missing End/PopID wasn't counted as an ImGui error");
            ImGui.NewFrame();
            ImGui.Render();
            Ui.CountImGuiErrors();
            Line($"hud out of frame: out-of-frame calls skipped and counted, a missing End recovered ({Ui.ImGuiErrors - errors0} ImGui errors counted)");
        }
        finally
        {
            Ui.ImGuiErrors = errors0;
            Hud.Camera = default;
            ImString.SharedStorage.Reset();
            ImGui.DestroyContext(ctx);
        }
    }
}
