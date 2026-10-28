namespace CallOfSlop.Platform.Steam;

public static partial class SteamPlatform
{
    /// <summary>Remote Steam players' names and avatars (ISteamFriends), cached, fetched on demand. No Steam: everything is empty.</summary>
    public static class Friends
    {
        private static readonly Dictionary<ulong, string> _names = new();
        private static readonly Dictionary<ulong, SteamAvatar?> _avatars = new();
        private static readonly HashSet<ulong> _requested = new();
        /// <summary>A user's name or avatar arrived or changed (the UI drops its cached texture, SteamAvatars.cs).</summary>
        public static event Action<ulong>? Changed;

        internal static void Attach(ISteamBackend backend)
        {
            backend.PersonaChanged += OnChanged;
            backend.AvatarLoaded += OnChanged;
        }

        internal static void Detach(ISteamBackend backend)
        {
            backend.PersonaChanged -= OnChanged;
            backend.AvatarLoaded -= OnChanged;
            _names.Clear();
            _avatars.Clear();
            _requested.Clear();
        }

        private static void OnChanged(ulong steamId)
        {
            _names.Remove(steamId);
            _avatars.Remove(steamId);
            _requested.Remove(steamId);
            Changed?.Invoke(steamId);
        }

        /// <summary>A Steam user's persona name, "" until Steam has it (it is requested; <see cref="Changed"/> fires when it arrives).</summary>
        public static string NameOf(ulong steamId)
        {
            if (!Available || steamId == 0)
                return "";
            if (_names.TryGetValue(steamId, out var name))
                return name;
            name = Backend.PersonaNameOf(steamId);
            if (name.Length > 0 || !Request(steamId))
                _names[steamId] = name; // known, or Steam has nothing more coming: stop asking
            return name;
        }

        /// <summary>A Steam user's small avatar (RGBA), null until Steam has it (requested; <see cref="Changed"/> fires when it arrives).</summary>
        public static SteamAvatar? AvatarOf(ulong steamId)
        {
            if (!Available || steamId == 0)
                return null;
            if (_avatars.TryGetValue(steamId, out var avatar))
                return avatar;
            avatar = Backend.AvatarOf(steamId);
            if (avatar != null || !Request(steamId))
                _avatars[steamId] = avatar; // known, or Steam has nothing more coming (no avatar): stop asking
            return avatar;
        }

        /// <summary>True while Steam is fetching this user (asked once; <see cref="Changed"/> ends the wait).</summary>
        private static bool Request(ulong steamId)
        {
            if (!_requested.Add(steamId))
                return true;
            if (Backend.RequestUserInformation(steamId))
                return true;
            _requested.Remove(steamId);
            return false;
        }

        /// <summary>How many users are cached (the self-test).</summary>
        public static int CachedNames => _names.Count;
    }
}
