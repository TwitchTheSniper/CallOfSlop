namespace CallOfSlop.Platform.Steam;

/// <summary>
/// A stand-in Steam for the self-test ("Steam platform" section) and for driving the layer without a Steam client: it
/// records every call in <see cref="Calls"/>, answers with the names and avatars it was given, and lets a test raise the
/// overlay and persona callbacks by hand. Later phases add their recorded answers here next to the real ones.
/// </summary>
public sealed class RecordedSteamBackend : ISteamBackend
{
    public readonly List<string> Calls = new();
    /// <summary>Init answers false with this when set (a Steam client that is not running).</summary>
    public string? InitError;
    public bool WantsRestart;
    public bool Running { get; private set; }
    public readonly Dictionary<ulong, string> Names = new();
    public readonly Dictionary<ulong, SteamAvatar> Avatars = new();
    /// <summary>Users whose name and avatar arrive only after a request (then the callbacks fire on the next RunCallbacks).</summary>
    public readonly HashSet<ulong> Pending = new();
    private readonly List<ulong> _arriving = new();

    public ulong LocalSteamId { get; set; } = 76561197960287930; // a valid-looking id: the test's own
    public string PersonaName { get; set; } = "Recorded Tester";
    public uint AppId { get; private set; }
    public bool OverlayEnabled { get; set; } = true;

    public event Action<bool>? OverlayActivated;
    public event Action<ulong>? PersonaChanged;
    public event Action<ulong>? AvatarLoaded;

    public bool Init(uint appId, out string error)
    {
        Calls.Add($"Init({appId})");
        error = InitError ?? "";
        if (InitError != null)
            return false;
        Running = true;
        AppId = appId;
        return true;
    }

    public bool RestartAppIfNecessary(uint appId)
    {
        Calls.Add($"RestartAppIfNecessary({appId})");
        return WantsRestart;
    }

    public void RunCallbacks()
    {
        Calls.Add("RunCallbacks");
        if (_arriving.Count == 0)
            return;
        var ids = _arriving.ToArray();
        _arriving.Clear();
        foreach (var id in ids)
        {
            Pending.Remove(id);
            PersonaChanged?.Invoke(id);
            if (Avatars.ContainsKey(id))
                AvatarLoaded?.Invoke(id);
        }
    }

    public void Shutdown()
    {
        Calls.Add("Shutdown");
        Running = false;
    }

    public string PersonaNameOf(ulong steamId)
    {
        Calls.Add($"PersonaNameOf({steamId})");
        return !Running || Pending.Contains(steamId) ? "" : Names.GetValueOrDefault(steamId, "");
    }

    public bool RequestUserInformation(ulong steamId)
    {
        Calls.Add($"RequestUserInformation({steamId})");
        if (!Running || !Pending.Contains(steamId))
            return false;
        if (!_arriving.Contains(steamId))
            _arriving.Add(steamId);
        return true;
    }

    public SteamAvatar? AvatarOf(ulong steamId)
    {
        Calls.Add($"AvatarOf({steamId})");
        return !Running || Pending.Contains(steamId) ? null : Avatars.GetValueOrDefault(steamId);
    }

    /// <summary>The test opens or closes the Steam overlay.</summary>
    public void RaiseOverlay(bool active) => OverlayActivated?.Invoke(active);
}
