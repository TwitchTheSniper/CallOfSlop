using System.Globalization;

namespace CallOfSlop.Platform.Steam;

/// <summary>
/// Rich Presence (Phase 2, ISteamFriends.SetRichPresence): what friends see under this player's name, and the "Join
/// game" button. The <c>steam_display</c> key names a token from the game's Rich Presence localisation file, which the
/// developer uploads on the Steamworks site (docs/steam/richpresence_english.txt: the tokens here, with the same
/// names); the other keys are its substitutions. <c>connect</c> is the launch argument Steam hands a friend who joins
/// (<see cref="SteamLobbies.ConnectStringOf"/>), and <c>steam_player_group</c> groups the party in the friends list.
/// </summary>
public sealed class SteamPresence
{
    public const string DisplayKey = "steam_display", ConnectKey = "connect", GroupKey = "steam_player_group", GroupSizeKey = "steam_player_group_size";
    public const string TokenMenu = "#Status_Menu", TokenSingle = "#Status_SinglePlayer", TokenLobby = "#Status_Lobby", TokenPlaying = "#Status_Playing", TokenZombies = "#Status_Zombies";
    /// <summary>Every token the game sets: the localisation file must define each (the self-test checks it does).</summary>
    public static readonly string[] Tokens = [TokenMenu, TokenSingle, TokenLobby, TokenPlaying, TokenZombies];
    /// <summary>The substitution keys the tokens use.</summary>
    public static readonly string[] Keys = ["mode", "map", "players", "max", "round"];

    private ISteamBackend? _steam;
    private readonly Dictionary<string, string> _set = new();

    /// <summary>The localisation file the developer uploads (docs/steam/richpresence_english.txt, embedded in the build), "" when missing.</summary>
    public static string LocalisationText()
    {
        try
        {
            using var stream = typeof(SteamPresence).Assembly.GetManifestResourceStream("richpresence_english.txt");
            if (stream == null)
                return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return "";
        }
    }

    internal void Attach(ISteamBackend steam)
    {
        _steam = steam;
        _set.Clear();
    }

    internal void Detach(ISteamBackend steam)
    {
        if (_steam == steam)
            Clear();
        _steam = null;
    }

    /// <summary>The current keys as last set (for the self-test and the log).</summary>
    public IReadOnlyDictionary<string, string> Current => _set;

    /// <summary>In the menus.</summary>
    public void SetMenu() => Apply(TokenMenu, null, null, 0, 0, 0, 0);

    /// <summary>Playing alone (no lobby to join).</summary>
    public void SetSinglePlayer(string mode, string map) => Apply(TokenSingle, mode, map, 0, 0, 0, 0);

    /// <summary>In the pre-match lobby room of a joinable game.</summary>
    public void SetLobby(string mode, string map, int players, int max, ulong lobbyId) => Apply(TokenLobby, mode, map, players, max, 0, lobbyId);

    /// <summary>In a match (Zombies shows the round instead of the player count).</summary>
    public void SetPlaying(string mode, string map, int players, int max, int zombiesRound, ulong lobbyId) =>
        Apply(zombiesRound > 0 ? TokenZombies : TokenPlaying, mode, map, players, max, zombiesRound, lobbyId);

    public void Clear()
    {
        _set.Clear();
        _steam?.ClearRichPresence();
    }

    private void Apply(string token, string? mode, string? map, int players, int max, int round, ulong lobbyId)
    {
        if (_steam == null)
            return;
        Put(DisplayKey, token);
        Put("mode", mode ?? "");
        Put("map", map ?? "");
        Put("players", players > 0 ? players.ToString(CultureInfo.InvariantCulture) : "");
        Put("max", max > 0 ? max.ToString(CultureInfo.InvariantCulture) : "");
        Put("round", round > 0 ? round.ToString(CultureInfo.InvariantCulture) : "");
        Put(ConnectKey, lobbyId != 0 ? SteamLobbies.ConnectStringOf(lobbyId) : "");
        Put(GroupKey, lobbyId != 0 ? lobbyId.ToString(CultureInfo.InvariantCulture) : "");
        Put(GroupSizeKey, lobbyId != 0 && players > 0 ? players.ToString(CultureInfo.InvariantCulture) : "");
    }

    private void Put(string key, string value)
    {
        if (_set.TryGetValue(key, out var old) && old == value)
            return;
        if (value.Length == 0)
            _set.Remove(key);
        else
            _set[key] = value;
        _steam!.SetRichPresence(key, value.Length == 0 ? null : value);
    }
}

public static partial class SteamPlatform
{
    /// <summary>Rich Presence (Phase 2). Everything is a no-op without Steam.</summary>
    public static SteamPresence Presence { get; } = new();
}
