using System.Reflection;
using System.Runtime.InteropServices;

namespace CallOfSlop.Platform.Steam;

public enum SteamState : byte
{
    /// <summary>Nothing asked for Steam this run (the normal state of a GitHub / tester build and of every self-test).</summary>
    Off,
    /// <summary>Steam was asked for but could not start (not running, wrong App ID, no native library): the game runs as without it.</summary>
    Unavailable,
    Running,
}

/// <summary>
/// The game's one door to Steam (Phase 1 of the Steam migration, docs/steam/STEAM.md). Steam is optional: nothing here
/// touches the Steamworks binding unless <see cref="Plan"/> says to start it (a Steam build, steam_appid.txt next to the
/// program, --steam, or the Steam client launched the game), so every GitHub build, LAN game and self-test runs as
/// before and <see cref="Available"/> is simply false. When it does run: the callbacks are pumped once a frame
/// (<see cref="RunCallbacks"/>), the local player's persona name becomes their display name (Settings "Use my Steam
/// name"), remote Steam players' names and avatars come from <see cref="Friends"/> / <see cref="Avatars"/>, the overlay
/// frees the mouse and pauses (<see cref="OverlayChanged"/>), and the in-game GitHub updater is off in a Steam build.
/// The backend (<see cref="ISteamBackend"/>) is swappable: the self-test drives this layer with a recorded one.
/// Later phases (transport, lobbies, stats, Workshop, Steam Input) add partial files here and members to the backend.
/// </summary>
public static partial class SteamPlatform
{
    /// <summary>Call of Slop's Steam App ID (the Steamworks partner site; not secret). A Steam build is made with
    /// dotnet publish -p:SteamAppId=1750920, and steam_appid.txt holds it for a developer's run; a plain build never
    /// assumes it, so GitHub builds, LAN games and the self-test stay off Steam.</summary>
    public const uint CallOfSlopAppId = 1750920;
    /// <summary>Spacewar, Valve's public test App ID: local testing with a bare --steam only, never a real build.</summary>
    public const uint PlaceholderAppId = 480;
    public const string AppIdFileName = "steam_appid.txt";

    public static SteamState State { get; private set; } = SteamState.Off;
    /// <summary>Why Steam is off or unavailable (empty while running). Shown in the log and the settings screen.</summary>
    public static string Reason { get; private set; } = "not started";
    public static bool Available => State == SteamState.Running;
    public static SteamStartPlan StartPlan { get; private set; }
    public static uint AppId => StartPlan.AppId;
    /// <summary>Steam asked to relaunch the game through itself: Main returns at once without a window.</summary>
    public static bool RestartRequested { get; private set; }
    /// <summary>The App ID stamped into this build (dotnet build -p:SteamAppId=NNN), 0 for a GitHub / dev build.
    /// A Steam build turns the GitHub updater off (Steam updates it) and relaunches through Steam when started directly.</summary>
    public static bool IsSteamBuild => BuildAppId != 0;
    public static uint BuildAppId { get; } = ReadBuildAppId();

    public static ulong LocalSteamId => Available ? Backend.LocalSteamId : 0;
    /// <summary>The local player's Steam persona name ("" without Steam).</summary>
    public static string PersonaName => Available ? Backend.PersonaName : "";

    public static ISteamBackend Backend { get; private set; } = new SteamworksBackend();

    /// <summary>Swaps the backend (the self-test's recorded one; a later phase's test double). Shuts the old one down first.</summary>
    public static void UseBackend(ISteamBackend backend)
    {
        Shutdown();
        Backend = backend;
        State = SteamState.Off;
        Reason = "not started";
        RestartRequested = false;
        StartPlan = default;
    }

    /// <summary>
    /// Decides whether this run starts Steam and with which App ID. Pure (no Steam call), so the self-test checks every case:
    /// --no-steam wins; then a Steam build's stamped id; --steam[=ID] (the Spacewar placeholder without one);
    /// steam_appid.txt next to the program; the Steam client's SteamAppId environment variable; else nothing.
    /// </summary>
    public static SteamStartPlan Plan(string[] args, string exeFolder, Func<string, string?> env, uint buildAppId)
    {
        if (args.Any(a => a.Equals("--no-steam", StringComparison.OrdinalIgnoreCase)))
            return new(SteamStartReason.Disabled, 0, "--no-steam");
        if (buildAppId != 0)
            return new(SteamStartReason.SteamBuild, buildAppId, $"Steam build, App ID {buildAppId}");
        var flag = args.FirstOrDefault(a => a.Equals("--steam", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--steam=", StringComparison.OrdinalIgnoreCase));
        if (flag != null)
        {
            var value = flag.Length > 7 ? flag[8..] : "";
            if (value.Length == 0)
                return new(SteamStartReason.Flag, PlaceholderAppId, $"--steam with the PLACEHOLDER App ID {PlaceholderAppId} (Spacewar): local testing only");
            return uint.TryParse(value, out var id) && id != 0
                ? new(SteamStartReason.Flag, id, $"--steam={id}")
                : new(SteamStartReason.Disabled, 0, $"--steam={value} is not an App ID");
        }
        var file = Path.Combine(exeFolder, AppIdFileName);
        if (File.Exists(file))
        {
            string text;
            try { text = File.ReadAllText(file).Trim(); }
            catch (Exception ex) { return new(SteamStartReason.Disabled, 0, $"{AppIdFileName} unreadable: {ex.Message}"); }
            if (uint.TryParse(text, out var id) && id != 0)
                return new(SteamStartReason.AppIdFile, id, $"{AppIdFileName}: App ID {id}" + (id == PlaceholderAppId ? " (the Spacewar PLACEHOLDER: local testing only)" : ""));
            return new(SteamStartReason.Disabled, 0, $"{AppIdFileName} does not hold an App ID ('{text}')");
        }
        if (uint.TryParse(env("SteamAppId"), out var launched) && launched != 0)
            return new(SteamStartReason.Launcher, launched, $"launched by Steam, App ID {launched}");
        return new(SteamStartReason.None, 0, "not a Steam build (no steam_appid.txt, no --steam)");
    }

    /// <summary>
    /// Starts Steam when the plan says so. False (and <see cref="Available"/> false) otherwise, with <see cref="Reason"/>
    /// saying why; the game carries on either way. Check <see cref="RestartRequested"/> after it: a Steam build started
    /// outside Steam hands over to Steam and must quit.
    /// </summary>
    public static bool Init(string[] args, string exeFolder) => Init(args, exeFolder, BuildAppId);

    /// <summary>As <see cref="Init(string[], string)"/> with the build's App ID given (the self-test plays a Steam build).</summary>
    public static bool Init(string[] args, string exeFolder, uint buildAppId)
    {
        if (Available)
            return true;
        StartPlan = Plan(args, exeFolder, Environment.GetEnvironmentVariable, buildAppId);
        if (!StartPlan.Start)
        {
            State = SteamState.Off;
            Reason = StartPlan.Detail;
            Console.WriteLine($"[steam] off: {Reason}");
            return false;
        }
        Console.WriteLine($"[steam] starting: {StartPlan.Detail}");
        if (StartPlan.RestartThroughSteam && Backend.RestartAppIfNecessary(StartPlan.AppId))
        {
            RestartRequested = true;
            State = SteamState.Off;
            Reason = "Steam is relaunching the game";
            Console.WriteLine($"[steam] {Reason}");
            return false;
        }
        if (StartPlan.Reason == SteamStartReason.Flag)
        {
            // The native library reads the App ID from steam_appid.txt or these variables; --steam never writes a file.
            Environment.SetEnvironmentVariable("SteamAppId", StartPlan.AppId.ToString());
            Environment.SetEnvironmentVariable("SteamGameId", StartPlan.AppId.ToString());
        }
        if (!Backend.Init(StartPlan.AppId, out var error))
        {
            State = SteamState.Unavailable;
            Reason = error;
            Console.WriteLine($"[steam] unavailable: {Reason}");
            return false;
        }
        State = SteamState.Running;
        Reason = "";
        Backend.OverlayActivated += OnOverlay;
        Friends.Attach(Backend);
        Lobby.Attach(Backend, Backend.LocalSteamId); // Phase 2: lobbies, Rich Presence, auth (SteamLobbies.cs, SteamPresence.cs, SteamAuth.cs)
        Presence.Attach(Backend);
        Auth.Attach(Backend);
        Console.WriteLine($"[steam] running: App ID {Backend.AppId}, {Backend.PersonaName} ({Backend.LocalSteamId}), overlay {(Backend.OverlayEnabled ? "on" : "off")}");
        return true;
    }

    /// <summary>Once a frame, from the main loop: delivers Steam's callbacks (overlay, persona changes, avatars).</summary>
    public static void RunCallbacks()
    {
        if (Available)
            Backend.RunCallbacks();
    }

    public static void Shutdown()
    {
        if (State == SteamState.Running)
        {
            Backend.OverlayActivated -= OnOverlay;
            Auth.Detach(Backend);
            Presence.Detach(Backend);
            Lobby.Detach(Backend);
            Friends.Detach(Backend);
            Backend.Shutdown();
            Console.WriteLine("[steam] shut down");
        }
        State = SteamState.Off;
        Reason = "shut down";
        OverlayActive = false;
    }

    // ---- Overlay -------------------------------------------------------------------------

    /// <summary>The Steam overlay is up: the game frees the mouse and pauses (Program.Steam.cs).</summary>
    public static bool OverlayActive { get; private set; }
    /// <summary>The overlay opened (true) or closed (false).</summary>
    public static event Action<bool>? OverlayChanged;

    private static void OnOverlay(bool active)
    {
        if (OverlayActive == active)
            return;
        OverlayActive = active;
        OverlayChanged?.Invoke(active);
    }

    // ---- Build and files ----------------------------------------------------------------

    private static uint ReadBuildAppId()
    {
        try
        {
            var value = typeof(SteamPlatform).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "SteamAppId")?.Value;
            return uint.TryParse(value, out var id) ? id : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>The native Steam library's file name on this OS (steam_api64.dll, libsteam_api.so, libsteam_api.dylib).</summary>
    public static string NativeLibraryFileName() =>
        OperatingSystem.IsWindows() ? "steam_api64.dll" : OperatingSystem.IsMacOS() ? "libsteam_api.dylib" : "libsteam_api.so";

    /// <summary>Where the native Steam library is: next to the program (a published package) or in the NuGet layout
    /// runtimes/&lt;rid&gt;/native (a dev build). Null when it is missing, which <see cref="ISteamBackend.Init"/> reports.</summary>
    public static string? NativeLibraryPath()
    {
        var name = NativeLibraryFileName();
        var baseDir = AppContext.BaseDirectory;
        var flat = Path.Combine(baseDir, name);
        if (File.Exists(flat))
            return flat;
        var rid = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
        var nested = Path.Combine(baseDir, "runtimes", rid, "native", name);
        return File.Exists(nested) ? nested : null;
    }
}
