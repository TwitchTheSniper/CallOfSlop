namespace CallOfSlop.Platform.Steam;

/// <summary>Why the Steam client was (or was not) started for this run (<see cref="SteamPlatform.Plan"/>).</summary>
public enum SteamStartReason : byte
{
    /// <summary>Nothing asked for Steam: not a Steam build, no steam_appid.txt, no --steam, not launched by Steam.</summary>
    None,
    /// <summary>--no-steam on the command line, or a headless run (self-test, map tools).</summary>
    Disabled,
    /// <summary>The App ID is stamped into the assembly (dotnet build -p:SteamAppId=NNN): a Steam build.</summary>
    SteamBuild,
    /// <summary>steam_appid.txt next to the program (a developer's run, not shipped).</summary>
    AppIdFile,
    /// <summary>--steam[=APPID] on the command line (a developer's run; the Spacewar placeholder without a number).</summary>
    Flag,
    /// <summary>The Steam client launched the game (the SteamAppId environment variable).</summary>
    Launcher,
}

/// <summary>What <see cref="SteamPlatform.Plan"/> decided: whether to start Steam, with which App ID, and why.</summary>
public readonly record struct SteamStartPlan(SteamStartReason Reason, uint AppId, string Detail)
{
    public bool Start => Reason is SteamStartReason.SteamBuild or SteamStartReason.AppIdFile or SteamStartReason.Flag or SteamStartReason.Launcher;
    /// <summary>Only a real Steam build asks Steam to relaunch the game when it was started outside Steam (a developer's
    /// run with steam_appid.txt or --steam never does: with the Spacewar placeholder it would launch Spacewar).</summary>
    public bool RestartThroughSteam => Reason == SteamStartReason.SteamBuild;
}

/// <summary>A user's small avatar as Steam gives it: RGBA, top-down.</summary>
public sealed record SteamAvatar(int Width, int Height, byte[] Rgba);

/// <summary>
/// What the game asks Steam for, behind one interface so the game runs without Steam (no binding is touched) and the
/// self-test drives the layer with a recorded backend (<see cref="RecordedSteamBackend"/>). The real one is
/// <see cref="SteamworksBackend"/> (Steamworks.NET). Later phases add their members here (transport, lobbies, stats,
/// UGC, Steam Input) and implement them in both backends; keep every member safe to call before <see cref="Init"/>
/// (return false / null / empty) so a feature can ask without checking first.
/// </summary>
public partial interface ISteamBackend
{
    /// <summary>Starts the Steam API. False with the reason when Steam is not running, the App ID is unknown, or the
    /// native library is missing; never throws.</summary>
    bool Init(uint appId, out string error);
    /// <summary>True when Steam wants to relaunch the game itself (the game should quit at once). Only for Steam builds.</summary>
    bool RestartAppIfNecessary(uint appId);
    /// <summary>Delivers the callbacks that arrived (<see cref="OverlayActivated"/>, <see cref="AvatarLoaded"/>, ...). Once a frame.</summary>
    void RunCallbacks();
    void Shutdown();

    ulong LocalSteamId { get; }
    string PersonaName { get; }
    uint AppId { get; }
    /// <summary>Another user's persona name, or "" when Steam does not know them yet (see <see cref="RequestUserInformation"/>).</summary>
    string PersonaNameOf(ulong steamId);
    /// <summary>Asks Steam for a user's name and avatar; true when it is fetching (a <see cref="PersonaChanged"/> or
    /// <see cref="AvatarLoaded"/> follows), false when it is already cached.</summary>
    bool RequestUserInformation(ulong steamId);
    /// <summary>The small avatar when Steam has it, else null (request it and wait for <see cref="AvatarLoaded"/>).</summary>
    SteamAvatar? AvatarOf(ulong steamId);

    bool OverlayEnabled { get; }
    /// <summary>The Steam overlay opened (true) or closed (false).</summary>
    event Action<bool>? OverlayActivated;
    /// <summary>A user's persona (name or avatar) changed.</summary>
    event Action<ulong>? PersonaChanged;
    /// <summary>A user's avatar image is ready.</summary>
    event Action<ulong>? AvatarLoaded;
}

/// <summary>The real backend: Steamworks.NET (the AnyCPU fork) over the native steam_api. Every call that can fail is
/// caught and turned into an "unavailable" answer; the native library is only touched from <see cref="Init"/> on.</summary>
public sealed partial class SteamworksBackend : ISteamBackend
{
    private bool _running;
    private Steamworks.Callback<Steamworks.GameOverlayActivated_t>? _overlayCb;
    private Steamworks.Callback<Steamworks.PersonaStateChange_t>? _personaCb;
    private Steamworks.Callback<Steamworks.AvatarImageLoaded_t>? _avatarCb;

    public ulong LocalSteamId { get; private set; }
    public string PersonaName { get; private set; } = "";
    public uint AppId { get; private set; }
    public bool OverlayEnabled => _running && Try(static () => Steamworks.SteamUtils.IsOverlayEnabled(), false);

    public event Action<bool>? OverlayActivated;
    public event Action<ulong>? PersonaChanged;
    public event Action<ulong>? AvatarLoaded;

    public bool Init(uint appId, out string error)
    {
        error = "";
        if (_running)
            return true;
        try
        {
            if (SteamPlatform.NativeLibraryPath() == null)
            {
                error = $"the native Steam library ({SteamPlatform.NativeLibraryFileName()}) is not next to the game";
                return false;
            }
            var result = Steamworks.SteamAPI.InitEx(out var message);
            if (result != Steamworks.ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                error = $"{result}: {message}".Replace("k_ESteamAPIInitResult_", "");
                return false;
            }
            _running = true;
            LocalSteamId = Steamworks.SteamUser.GetSteamID().m_SteamID;
            PersonaName = Steamworks.SteamFriends.GetPersonaName() ?? "";
            AppId = Steamworks.SteamUtils.GetAppID().m_AppId;
            if (AppId == 0)
                AppId = appId;
            _overlayCb = Steamworks.Callback<Steamworks.GameOverlayActivated_t>.Create(cb => OverlayActivated?.Invoke(cb.m_bActive != 0));
            _personaCb = Steamworks.Callback<Steamworks.PersonaStateChange_t>.Create(cb => PersonaChanged?.Invoke(cb.m_ulSteamID));
            _avatarCb = Steamworks.Callback<Steamworks.AvatarImageLoaded_t>.Create(cb => AvatarLoaded?.Invoke(cb.m_steamID.m_SteamID));
            HookPhase2(); // sockets, lobbies, Rich Presence and auth (SteamBackend.Net.cs)
            return true;
        }
        catch (Exception ex) // DllNotFoundException, BadImageFormat, a binding mismatch: all "no Steam"
        {
            error = ex.GetType().Name + ": " + ex.Message;
            _running = false;
            return false;
        }
    }

    public bool RestartAppIfNecessary(uint appId) =>
        Try(() => Steamworks.SteamAPI.RestartAppIfNecessary(new Steamworks.AppId_t(appId)), false);

    public void RunCallbacks()
    {
        if (_running)
            Try(static () => { Steamworks.SteamAPI.RunCallbacks(); return true; }, false);
    }

    public void Shutdown()
    {
        if (!_running)
            return;
        _running = false;
        UnhookPhase2();
        _overlayCb?.Dispose();
        _personaCb?.Dispose();
        _avatarCb?.Dispose();
        _overlayCb = null;
        _personaCb = null;
        _avatarCb = null;
        Try(static () => { Steamworks.SteamAPI.Shutdown(); return true; }, false);
    }

    public string PersonaNameOf(ulong steamId) =>
        !_running ? "" : Try(() => Steamworks.SteamFriends.GetFriendPersonaName(new Steamworks.CSteamID(steamId)) ?? "", "");

    public bool RequestUserInformation(ulong steamId) =>
        _running && Try(() => Steamworks.SteamFriends.RequestUserInformation(new Steamworks.CSteamID(steamId), false), false);

    public SteamAvatar? AvatarOf(ulong steamId)
    {
        if (!_running)
            return null;
        return Try<SteamAvatar?>(() =>
        {
            var handle = Steamworks.SteamFriends.GetSmallFriendAvatar(new Steamworks.CSteamID(steamId));
            if (handle <= 0) // 0: none, -1: loading
                return null;
            if (!Steamworks.SteamUtils.GetImageSize(handle, out var w, out var h) || w == 0 || h == 0 || w > 256 || h > 256)
                return null;
            var rgba = new byte[w * h * 4];
            return Steamworks.SteamUtils.GetImageRGBA(handle, rgba, rgba.Length) ? new SteamAvatar((int)w, (int)h, rgba) : null;
        }, null);
    }

    private static T Try<T>(Func<T> f, T fallback)
    {
        try
        {
            return f();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[steam] call failed: {ex.GetType().Name}: {ex.Message}");
            return fallback;
        }
    }
}
