using System.Globalization;

namespace CallOfSlop;

public enum DisplayMode
{
    Windowed,
    Borderless, // borderless window covering the whole monitor
    Fullscreen, // exclusive fullscreen at the chosen resolution
}

public enum BotDifficulty
{
    Easy,
    Normal,
    Hard,
    Insane, // Hard+ aim and reactions; in team modes they play as squads (Source/Game/Bots/Squads)
}

/// <summary>Player preferences, saved to settings.txt next to the game.</summary>
public sealed class Settings
{
    // Player
    public string Name = DefaultName();
    public bool SteamName = true; // Settings > Player > "Use my Steam name": with Steam running, the persona name is the display name (Program.Steam.cs)
    public byte SteamLobby = 2; // hosting on Steam: who finds the lobby: 0 invite only, 1 friends, 2 public (SteamLobbies.cs; Program.SteamNet.cs)
    public List<ulong> SteamBans = new(); // hosting: Steam IDs refused at the door (GameSteamAuth.cs)
    public float Sensitivity = 1f; // multiplier
    public float Fov = 75f;
    public bool InvertY;
    public bool HoldToPaint = true; // Sandbox snap build: a held press paints on further sockets after a short hold (off: one part per click)
    public bool SnapBuild = true; // Sandbox: snappy building (connection points, the magnet, quick build in creative, hold-to-paint, undo; Constructs/SnapBuild.cs)
    public bool SmartRotation = true; // Sandbox: the ghost's first turn is smart rotation's guess (Constructs/SmartRotation.cs); off: the fixed order
    public List<string> SmartRotationLearned = new(); // ...and the player's corrections ("Part|kind|side|turn"; SmartRotationMemory.Save)

    // Audio / video
    public float Volume = 0.8f;
    public float MusicVolume = 0.5f; // menu and lobby music, on top of the master volume (0 = off)
    public float EffectsVolume = 1f; // gunfire, hits and every other sound effect, on top of the master volume
    public float FootstepVolume = 1f; // footsteps, jumps and landings, on top of the effects volume
    public float OwnGunVolume = 1f; // your own gunshots, on top of the effects volume (1 = the tuned mix, see Audio.GunMix.cs)
    public MusicChoice MusicChoice = MusicChoice.Mixed; // Settings > Audio > Music style
    public bool FrenzyMusic = true; // Settings > Audio > Kill frenzy music: hard music fades in during kill sprees (MusicFrenzy.cs)
    public float FrenzyVolume = 0.8f; // Settings > Audio > Frenzy volume, on top of the music volume
    public bool VSync = true;

    /// <summary>Settings > Video "Max FPS" (FrameLimiter.cs): 0 unlimited (the default), else 20-1000. Applies with VSync off.</summary>
    public int MaxFps;

    /// <summary>"Max FPS in menus": the lobby, menus and the pause menu (0: off, the main cap). Applies with VSync on too.</summary>
    public int MaxFpsMenu;

    /// <summary>"Max FPS in the background": the window unfocused or minimised (0: off). Applies with VSync on too.</summary>
    public int MaxFpsBackground;

    // Voice and chat (Settings > Audio, Source/Audio/Voice). The microphone stays off until the player turns it on.
    public VoiceMode VoiceMode = VoiceMode.Proximity; // how you talk; Off: no voice at all (you hear nobody either)
    public bool VoiceTransmit; // "Use my microphone": off on first run
    public bool VoiceActivation; // false: push to talk; true: transmit while the level is over the threshold
    public float VoiceThresholdDb = VoiceSender.DefaultThresholdDb;
    public string VoiceDevice = ""; // capture device name ("" = the system default)
    public float VoiceVolume = 1f; // other players' voices, on top of the master volume: 0 to VoiceBoost.Max (300%)
    public readonly Dictionary<string, float> VoicePlayerVolume = new(StringComparer.Ordinal); // by player name: 0 to VoiceBoost.Max (absent = 100%)
    public readonly HashSet<string> VoicePlayerMuted = new(StringComparer.Ordinal); // players whose voice you muted (by name)
    public bool VoiceMuteAll; // hear no voices (your own mic is unaffected)
    public bool AllowVoice = true; // hosting: relay voice chat at all
    public bool DeadChatPrivate; // hosting: dead players' chat in a live match reaches only the dead

    /// <summary>A small fps counter in the top-right corner.</summary>
    public bool ShowFps = true;
    public bool AllowWeaponTweaks; // Settings > Graphics: allow the weapon tweak window in a release build (dev builds always do; UiWeaponTweaks.cs)
    public bool AllowDevTools; // Settings > Graphics: allow the look-at inspector and prop editor in a release build (dev builds always do; UiDevTools.cs)
    public bool ShowNetStats = true;
    public DisplayMode Display = DisplayMode.Windowed;
    public int Width = 1600; // windowed size, or fullscreen resolution
    public int Height = 900;
    public float UiScale = 1f; // multiplier on the automatic scale (which follows window height)
    public HudWidth HudWidth = HudWidth.Wide16x9; // in-match HUD kept inside a centred area this wide
    public FoliageQuality FoliageQuality = FoliageQuality.Medium; // grass, bushes and tree canopies (Off draws none)
    public SkyQuality SkyQuality = SkyQuality.High; // maps with a sky: Low = gradient, sun and moon; High = plus clouds and stars
    public LightingQuality Lighting = LightingQuality.Full; // lit maps: baked light (Off: the classic light everywhere); Full adds muzzle flashes and blasts (LightGrid.cs)
    public ImpactDecalQuality ImpactDecals = ImpactDecalQuality.High; // bullet-impact decals kept at once: High 1024, Medium 512, Low 128, Off (Source/Decals)
    public FrenzyVisualMode FrenzyVisuals = FrenzyVisualMode.Full; // Settings > Video "Frenzy visuals": per-class frenzy effects (Source/Render/FrenzyFx)
    public BloodMode BloodOverlay = BloodMode.Full; // Settings > Video "Blood overlay": screen splatters by your health + blood marks on bodies (BloodOverlay.cs, BodyBloodRender.cs)
    public BloodColour BloodColour = BloodColour.Red; // Settings > Video "Blood colour": red or dark
    public GoreMode Gore = GoreMode.Full; // Settings > Video "Gore": meaty gibs from animals and blast kills, blood splats (Source/Props/Gibs.cs)
    public FrenzyGunSoundMode FrenzyGunSound = FrenzyGunSoundMode.Full; // Settings > Audio "Frenzy gun sound": per-class frenzy layers on your gun (Source/Audio/FrenzyGunSound.cs)

    /// <summary>Camera head bob when moving, in percent (0 = none, the steady camera; 100 = full). See HeadBob.</summary>
    public int HeadBob = 100;

    /// <summary>Name tags over players' heads: All (teammates + the enemy you aim at), Team only, or Off. See NameTags.</summary>
    public NameTagMode NameTags = NameTagMode.All;

    /// <summary>A small health bar under teammates' name tags (off by default).</summary>
    public bool NameTagHealth;

    /// <summary>Workbench inventory: list parts that fit nowhere on the build too (dimmed). Off by default; old files get off.</summary>
    public bool WorkbenchShowIncompatible;

    /// <summary>Workbench grip picker: list every grip in the game, the ones you don't own dimmed. Off by default (only owned grips).</summary>
    public bool WorkbenchShowAllGrips;

    /// <summary>Set by the settings screen when display options change; Program applies them.</summary>
    [NonSerialized] public bool DisplayDirty;

    // Controls
    public readonly int[] Binds = (int[])Keybinds.Defaults.Clone();

    public int Bind(GameAction a) => Binds[(int)a];

    // Single player
    public int Bots = 3; // 0 = "No bots (test mode)": you alone, the match never ends on its own
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public BotGearMode BotGear = BotGearMode.MatchPlayers; // single player and hosting: bots carry what the players' level has unlocked (BotGear.cs); files without it get Match players

    // Multiplayer
    public ushort Port = Program.DefaultPort;
    public bool UseUpnp = true;
    public int HostBots;
    public string JoinAddress = "127.0.0.1";
    public string MapName = "Proto Box"; // map for single player and hosting (when not random)
    public bool MapRandom = true; // default: a random map each time you start
    public string MapPack = DefaultMapPack; // the map pack "random" picks from (and the lobby lists); "*all" = True Random (MapPack.TrueRandomKey)

    /// <summary>The pack a fresh settings file picks random maps from: Legacy (the shipped maps, Proto Box first).</summary>
    public const string DefaultMapPack = "Legacy";

    /// <summary>
    /// Packs that were folded into the Legacy pack: a settings.txt from before that still names one of them. Mapped to
    /// Legacy on load (<see cref="MigrateMapPack"/>) unless a pack of that name is back (someone's own folder).
    /// </summary>
    private static readonly string[] LegacyPackNames = ["Classic", "Collision Point", "High Ground"];

    /// <summary>The map_pack value to use for a saved one: an old pack name becomes Legacy, anything else is kept as it is.</summary>
    public static string MigrateMapPack(string saved, IReadOnlyList<MapPack> packs)
    {
        if (!LegacyPackNames.Any(n => n.Equals(saved.Trim(), StringComparison.OrdinalIgnoreCase)))
            return saved;
        if (packs.Any(p => p.Name.Equals(saved.Trim(), StringComparison.OrdinalIgnoreCase)))
            return saved; // a pack of that name exists again: keep the choice
        return DefaultMapPack;
    }
    public bool ShareOnLists = true; // hosting: register my game on my server lists (friends who have one see it); files with only the old advertise=0 get on
    public bool AutoListServer = true; // hosting: if none of my server lists answers, run one in this game (HostListing)
    public bool RunListServer; // "Run a server list for friends": keep one running in this game while it's open (ListServer)
    public bool UseStun = true; // find my public address for invite codes by asking a public STUN server (Stun.cs)
    public bool FrenzyBuffs = true; // host setting: per-class gameplay buffs during a kill frenzy (Source/Game/Frenzy/FrenzyBuffs.cs)
    public bool Destruction = true; // host setting: destructible buildings on maps that have them (Source/Game/GameDestruction.cs)
    public string ServerName = ""; // empty: "<name>'s game"
    public string MasterServer = ""; // server lists (master servers), "host[:port], host[:port]...", typed or learned from invites (empty: none)

    // Updates (Source/Updates): check GitHub for a newer release at startup; a version the player chose to skip
    public bool UpdateCheck = true; // files without it get On
    public string UpdateSkip = ""; // "0.2026.1022.5": no banner for this release (a newer one shows again)
    public bool UpdateCleanup = true; // "Remove old versions after updating" (UpdateCleanup.cs); files without it get On
    public bool UpdateKeepPrevious = true; // "Keep previous version for rollback": the cleanup keeps one older build

    // Matches (single player and hosting)
    public GameMode Mode = GameMode.FreeForAll;
    public int AutoStart; // hosting: start N seconds after the first person readies up (0: wait for everyone)
    public int Gravity = 100; // hosting: every map's gravity in percent, 25-200 (Source/Game/GameGravity.cs)
    public int ZombieStartRound = 1; // hosting: the round a Zombies run starts at (testing; Source/Game/ZombiesStartRound.cs)
    public bool ZombieTestKit; // hosting: the test kit on maps that allow it (Horde Test)
    public int ZombieStressSize; // hosting: the horde size on a horde stress map (Horde Stress; 0: the map's own; Source/Game/ZombiesCrowdStress.cs)
    public bool ZombieStressEndless; // hosting: endless waves on a horde stress map
    public int ZombieHordeCap = Game.MaxZombies; // hosting: Zombies' most zombies alive at once (weak PCs lower it; Source/Game/ZombieHorde.cs); files without it get no extra limit
    public TeamSelection TeamSelect = TeamSelection.Manual; // team modes: players pick their team, or Auto only (TeamPick.cs)
    public int TeamMaxDiff = TeamRules.DefaultMaxDiff; // team modes: how much bigger a side may get by picks (0-4)
    public PhysicsMode Physics = PhysicsMode.Classic; // hosting: player movement (classic, or the swept capsule controller); joiners use the host's
    public ScoreLimitMode ScoreLimit = ScoreLimitMode.Auto; // single player and hosting: Auto scales the limit with the player count; files without it get Auto
    public TeamBalance TeamBalance = TeamBalance.Auto; // team modes: Auto / Off / Humans vs Bots (TeamBalance.cs); files without it get Auto
    public byte HumansTeam; // Humans vs Bots: the people's side (0 Blue, 1 Red)
    public bool LobbyReturn; // hosting: back to the lobby room between matches (off: straight into the next warmup; PreMatchLobby.cs)
    public int MatchMinutes = 10; // hosting: the time limit, set in the lobby room (files without it get 10)

    public string ServerNameOrDefault => ServerName.Length > 0 ? ServerName : $"{Name}'s game";

    /// <summary>The first server list as (host, port), or null if none is set.</summary>
    public (string Host, ushort Port)? Master => Masters.Count > 0 ? Masters[0] : null;

    /// <summary>The most server lists kept in settings (the oldest go first when a new one is learned).</summary>
    public const int MaxMasters = 8;

    /// <summary>The server lists typed or learned (<see cref="MasterServer"/>, comma separated), deduplicated.</summary>
    public IReadOnlyList<(string Host, ushort Port)> Masters => ParseMasters(MasterServer);

    /// <summary>"a, b:27051; c" -> (host, port) pairs (default port 27050), duplicates and unusable entries dropped.</summary>
    public static List<(string Host, ushort Port)> ParseMasters(string? text)
    {
        var list = new List<(string Host, ushort Port)>();
        foreach (var raw in (text ?? "").Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = raw.Trim();
            var host = m;
            var port = MasterServerApp.DefaultPort;
            var colon = m.LastIndexOf(':');
            if (colon >= 0)
            {
                if (colon == 0 || m.IndexOf(':') != colon || !ushort.TryParse(m[(colon + 1)..], out port) || port == 0)
                    continue; // ":27050", IPv6 or a bad port
                host = m[..colon];
            }
            if (!list.Any(e => e.Port == port && e.Host.Equals(host, StringComparison.OrdinalIgnoreCase)))
                list.Add((host, port));
            if (list.Count >= MaxMasters)
                break;
        }
        return list;
    }

    private static string JoinMasters(IEnumerable<(string Host, ushort Port)> list) =>
        string.Join(", ", list.Select(m => m.Port == MasterServerApp.DefaultPort ? m.Host : $"{m.Host}:{m.Port}"));

    /// <summary>
    /// Adds a server list learned from an invite or a host ("host:port"); true if it was new. Keeps at most
    /// <see cref="MaxMasters"/> (dropping the oldest).
    /// </summary>
    public bool LearnMaster(string? address)
    {
        var add = ParseMasters(CleanAddress(address ?? ""));
        if (add.Count != 1)
            return false;
        var list = ParseMasters(MasterServer);
        if (list.Any(e => e.Port == add[0].Port && e.Host.Equals(add[0].Host, StringComparison.OrdinalIgnoreCase)))
            return false;
        list.Add(add[0]);
        while (list.Count > MaxMasters)
            list.RemoveAt(0);
        MasterServer = CleanMasterList(JoinMasters(list));
        return true;
    }

    /// <summary>Where the release's shipped server lists are read from (masters.txt next to the game; the self-test points it elsewhere).</summary>
    public static string ShippedMastersPath = Path.Combine(AppFolders.Data, "masters.txt");

    private static (string Path, DateTime Stamp, List<(string Host, ushort Port)> List)? _shipped;

    /// <summary>
    /// Community server lists shipped with the release in masters.txt (one "host[:port]" per line, # comments), so a
    /// group can bake its list into the zip it shares. Missing or unreadable: none.
    /// </summary>
    public static IReadOnlyList<(string Host, ushort Port)> ShippedMasters
    {
        get
        {
            try
            {
                var path = ShippedMastersPath;
                var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_shipped is { } c && c.Path == path && c.Stamp == stamp)
                    return c.List;
                var list = stamp == DateTime.MinValue ? new List<(string, ushort)>()
                    : ParseMasters(string.Join(",", File.ReadLines(path).Take(64).Select(l => l.Split('#')[0]).Select(l => CleanAddress(l.Trim())).Where(l => l.Length > 0)));
                _shipped = (path, stamp, list);
                return list;
            }
            catch (Exception)
            {
                return [];
            }
        }
    }

    /// <summary>Every server list to use: the typed / learned ones, the shipped ones, and any running inside this game.</summary>
    public IReadOnlyList<(string Host, ushort Port)> AllMasters
    {
        get
        {
            var all = new List<(string Host, ushort Port)>(Masters);
            foreach (var m in ShippedMasters.Concat(ListServer.Running.Select(l => ("127.0.0.1", l.Port))))
                if (!all.Any(e => e.Port == m.Item2 && e.Host.Equals(m.Item1, StringComparison.OrdinalIgnoreCase)))
                    all.Add(m);
            return all;
        }
    }

    /// <summary>The map to start: the chosen one, or a random one from the selected pack (or from every pack in True Random).</summary>
    public Map PickMap(int players = 0) => PickMap(Maps.Packs, Maps.All, Random.Shared, players);

    /// <summary>
    /// <see cref="PickMap()"/> over a given set of packs and maps (the self-test uses its own). A pack that's gone
    /// falls back to all maps, and so does a chosen map that's gone.
    /// </summary>
    /// <remarks>
    /// "Random from pack" with <paramref name="players"/> (you + bots) picks only among the pack's maps that take that
    /// many players (the lobby's slider goes up to the biggest map's limit); if none does, the biggest map.
    /// </remarks>
    public Map PickMap(IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all, Random rng, int players = 0)
    {
        if (!MapRandom && all.FirstOrDefault(m => m.Name.Equals(MapName, StringComparison.OrdinalIgnoreCase)) is { } chosen)
            return chosen;
        var pool = CallOfSlop.MapPack.RandomPool(MapPack, packs, all); // the pack, True Random's packs, or everything
        if (players <= 0)
            return pool[rng.Next(pool.Count)];
        var fit = pool.Where(m => m.MaxPlayers >= players).ToList();
        return fit.Count > 0 ? fit[rng.Next(fit.Count)] : pool.OrderByDescending(m => m.MaxPlayers).First();
    }

    /// <summary>The player limit the lobby's sliders use: the chosen map's, or for "random from pack" the biggest in the pack (True Random: in all its packs).</summary>
    public int LobbyMaxPlayers(IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all)
    {
        if (!MapRandom && all.FirstOrDefault(m => m.Name.Equals(MapName, StringComparison.OrdinalIgnoreCase)) is { } chosen)
            return chosen.MaxPlayers;
        var pool = CallOfSlop.MapPack.RandomPool(MapPack, packs, all);
        return pool.Count == 0 ? Game.ClassicPlayers : pool.Max(m => m.MaxPlayers);
    }

    private static string FilePath => SavePaths.PathOf("settings.txt");

    /// <summary>The <c>version=</c> line written to settings.txt (separate from binds_version; files without one load the same).</summary>
    public const int SaveVersion = 1;

    private static string DefaultName()
    {
        var n = Environment.UserName;
        return string.IsNullOrWhiteSpace(n) ? "Player" : Clean(n);
    }

    /// <summary>Player names: printable ASCII, 1-16 characters.</summary>
    public static string Clean(string name)
    {
        var chars = name.Where(c => c >= 32 && c < 127).Take(16).ToArray();
        var s = new string(chars).Trim();
        return s.Length == 0 ? "Player" : s;
    }

    /// <summary>Server names: printable ASCII, up to 32 characters (may be empty).</summary>
    /// <summary>Your volume for a player's voice (1 unless set), 0 to <see cref="VoiceBoost.Max"/>.</summary>
    public float VoicePlayerGain(string name) => VoicePlayerVolume.TryGetValue(name, out var v) ? v : 1f;

    /// <summary>Sets a player's voice volume (100% removes the entry).</summary>
    public void SetVoicePlayerVolume(string name, float volume)
    {
        volume = float.IsFinite(volume) ? Math.Clamp(volume, 0f, VoiceBoost.Max) : 1f;
        if (MathF.Abs(volume - 1f) < 0.005f)
            VoicePlayerVolume.Remove(name);
        else
            VoicePlayerVolume[name] = volume;
    }

    /// <summary>Saved per-player voice lines: <c>voice_player=VOLUME:MUTED:NAME</c> (the name last: it may hold a colon).</summary>
    private IEnumerable<string> VoicePlayerLines()
    {
        var inv = CultureInfo.InvariantCulture;
        foreach (var name in VoicePlayerVolume.Keys.Union(VoicePlayerMuted).Order(StringComparer.Ordinal).Take(MaxVoicePlayers))
            if (name.Length > 0 && name.IndexOfAny(['\r', '\n']) < 0)
                yield return $"voice_player={VoicePlayerGain(name).ToString(inv)}:{(VoicePlayerMuted.Contains(name) ? 1 : 0)}:{name}";
    }

    /// <summary>The most players remembered (a long-lived save stays small).</summary>
    public const int MaxVoicePlayers = 500;

    private void ReadVoicePlayer(string v)
    {
        var parts = v.Split(':', 3);
        if (parts.Length != 3 || parts[2].Length == 0 || VoicePlayerVolume.Count + VoicePlayerMuted.Count >= MaxVoicePlayers * 2)
            return;
        var name = Clean(parts[2]);
        if (name.Length == 0)
            return;
        if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            SetVoicePlayerVolume(name, f);
        if (parts[1] == "1")
            VoicePlayerMuted.Add(name);
    }

    public static string CleanServerName(string name) => new string(name.Where(c => c >= 32 && c < 127).Take(32).ToArray()).Trim();

    /// <summary>Addresses (join address, master server): printable ASCII without spaces, up to 127 characters (fits the lobby's text box).</summary>
    public static string CleanAddress(string address) => new string(address.Where(c => c > 32 && c < 127).Take(127).ToArray());

    /// <summary>Server list text (several comma separated addresses): printable ASCII, up to 255 characters.</summary>
    public static string CleanMasterList(string list) => new string(list.Where(c => c >= 32 && c < 127).Take(255).ToArray()).Trim();

    public static Settings Load() => Load(FilePath);

    /// <summary>Loads a settings file from <paramref name="path"/> (the self-test uses a temporary one).</summary>
    public static Settings Load(string path)
    {
        var s = new Settings();
        try
        {
            if (SavePaths.ReadLines(path, SaveVersion) is not { } fileLines)
                return s;
            var bindsVersion = 1; // files without a binds_version line have the first defaults (Keybinds.Migrate)
            foreach (var line in fileLines)
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2)
                    continue;
                var key = kv[0].Trim();
                var v = kv[1].Trim();
                var inv = CultureInfo.InvariantCulture;
                switch (key)
                {
                    case "name": s.Name = Clean(v); break;
                    case "steam_name": s.SteamName = v == "1"; break;
                    case "steam_lobby" when byte.TryParse(v, out var sl): s.SteamLobby = Math.Min(sl, (byte)2); break;
                    case "steam_bans":
                        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (ulong.TryParse(part, NumberStyles.None, inv, out var ban) && ban != 0 && !s.SteamBans.Contains(ban) && s.SteamBans.Count < 1000)
                                s.SteamBans.Add(ban);
                        break;
                    case "sensitivity" when float.TryParse(v, inv, out var f): s.Sensitivity = Math.Clamp(f, 0.1f, 5f); break;
                    case "fov" when float.TryParse(v, inv, out var f): s.Fov = Math.Clamp(f, 60f, 110f); break;
                    case "invert_y": s.InvertY = v == "1"; break;
                    case "smart_rotation": s.SmartRotation = v != "0"; break;
                    case "snap_build": s.SnapBuild = v != "0"; break;
                    case "hold_to_paint": s.HoldToPaint = v != "0"; break;
                    case "smart_rotation_learned":
                        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            if (s.SmartRotationLearned.Count < SmartRotationMemory.MaxLearned && part.Length <= 64 && part.All(ch => ch >= 32 && ch < 127))
                                s.SmartRotationLearned.Add(part);
                        break;
                    case "volume" when float.TryParse(v, inv, out var f): s.Volume = Math.Clamp(f, 0f, 1f); break;
                    case "music_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.MusicVolume = Math.Clamp(f, 0f, 1f); break;
                    case "effects_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.EffectsVolume = Math.Clamp(f, 0f, 1f); break;
                    case "footstep_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.FootstepVolume = Math.Clamp(f, 0f, 1f); break;
                    case "own_gun_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.OwnGunVolume = Math.Clamp(f, 0f, 1f); break;
                    case "music_style" when Enum.TryParse<MusicChoice>(v, true, out var mc) && Enum.IsDefined(mc): s.MusicChoice = mc; break;
                    case "frenzy_music": s.FrenzyMusic = v != "0"; break;
                    case "frenzy_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.FrenzyVolume = Math.Clamp(f, 0f, 1f); break;
                    case "vsync": s.VSync = v == "1"; break;
                    case "max_fps" when int.TryParse(v, NumberStyles.Integer, inv, out var n): s.MaxFps = FrameCap.Clamp(n); break;
                    case "max_fps_menu" when int.TryParse(v, NumberStyles.Integer, inv, out var n): s.MaxFpsMenu = FrameCap.Clamp(n); break;
                    case "max_fps_background" when int.TryParse(v, NumberStyles.Integer, inv, out var n): s.MaxFpsBackground = FrameCap.Clamp(n); break;
                    case "voice_mode" when Enum.TryParse<VoiceMode>(v, true, out var vm) && Enum.IsDefined(vm): s.VoiceMode = vm; break;
                    case "voice_transmit": s.VoiceTransmit = v == "1"; break;
                    case "voice_activation": s.VoiceActivation = v == "1"; break;
                    case "voice_threshold" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.VoiceThresholdDb = Math.Clamp(f, VoiceSender.MinThresholdDb, VoiceSender.MaxThresholdDb); break;
                    case "voice_device": s.VoiceDevice = new string(v.Where(c => c >= 32).Take(200).ToArray()); break;
                    case "voice_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.VoiceVolume = Math.Clamp(f, 0f, VoiceBoost.Max); break;
                    case "voice_player": s.ReadVoicePlayer(v); break;
                    case "voice_mute_all": s.VoiceMuteAll = v == "1"; break;
                    case "allow_voice": s.AllowVoice = v != "0"; break;
                    case "dead_chat": s.DeadChatPrivate = v == "dead"; break;
                    case "show_fps": s.ShowFps = v == "1"; break;
                    case "weapon_tweaks": s.AllowWeaponTweaks = v == "1"; break;
                    case "dev_tools": s.AllowDevTools = v == "1"; break;
                    case "net_stats": s.ShowNetStats = v == "1"; break;
                    case "bots" when int.TryParse(v, out var i): s.Bots = Math.Clamp(i, 0, Game.MaxPlayers - 1); break;
                    case "difficulty" when Enum.TryParse<BotDifficulty>(v, out var d): s.Difficulty = d; break;
                    case "bot_gear": s.BotGear = CallOfSlop.BotGear.Parse(v); break;
                    case "port" when ushort.TryParse(v, out var p) && p > 0: s.Port = p; break;
                    case "upnp": s.UseUpnp = v == "1"; break;
                    case "host_bots" when int.TryParse(v, out var i): s.HostBots = Math.Clamp(i, 0, Game.MaxPlayers - 2); break;
                    case "join_address": s.JoinAddress = CleanAddress(v); break;
                    case "map" when Maps.Find(v) != null: s.MapName = v; break;
                    case "map_random": s.MapRandom = v == "1"; break;
                    case "map_pack" when v.Length > 0: // kept even if the pack is missing now (it may come back)
                        s.MapPack = MigrateMapPack(v, Maps.Packs); // the old Classic, Collision Point and High Ground packs became Legacy
                        if (s.MapPack != v)
                            Console.WriteLine($"[settings] map pack '{v}' is now part of {s.MapPack}");
                        break;
                    case "advertise" when v == "1": s.ShareOnLists = true; break; // the old opt-in (before share_on_lists)
                    case "share_on_lists": s.ShareOnLists = v != "0"; break;
                    case "auto_list": s.AutoListServer = v != "0"; break;
                    case "run_list": s.RunListServer = v == "1"; break;
                    case "stun": s.UseStun = v != "0"; break;
                    case "frenzy_buffs": s.FrenzyBuffs = v == "1"; break;
                    case "destruction": s.Destruction = v != "0"; break;
                    case "server_name": s.ServerName = CleanServerName(v); break;
                    case "master": s.MasterServer = CleanMasterList(v); break;
                    case "update_check": s.UpdateCheck = v != "0"; break;
                    case "update_skip": s.UpdateSkip = BuildInfo.ParseRelease(v)?.ToString() ?? ""; break;
                    case "update_cleanup": s.UpdateCleanup = v != "0"; break;
                    case "update_keep_previous": s.UpdateKeepPrevious = v != "0"; break;
                    case "mode" when Enum.TryParse<GameMode>(v, out var gm) && Enum.IsDefined(gm): s.Mode = gm; break;
                    case "auto_start" when int.TryParse(v, out var i): s.AutoStart = Math.Clamp(i, 0, 300); break;
                    case "gravity" when int.TryParse(v, out var i): s.Gravity = Math.Clamp(i, Game.MinGravityPercent, Game.MaxGravityPercent); break;
                    case "zombie_horde_cap" when int.TryParse(v, out var i): s.ZombieHordeCap = i >= Game.OldMaxZombies ? Game.MaxZombies : Math.Clamp(i, Game.MinHordeCap, Game.MaxZombies); break; // (512, the old "no limit", stays no limit)
                    case "zombie_start_round" when int.TryParse(v, out var i): s.ZombieStartRound = Math.Clamp(i, 1, Game.MaxStartRound); break;
                    case "zombie_test_kit": s.ZombieTestKit = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "zombie_stress_size" when int.TryParse(v, out var i): s.ZombieStressSize = Math.Clamp(i, 0, Game.StressMax); break;
                    case "zombie_stress_endless": s.ZombieStressEndless = v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                    case "team_select": s.TeamSelect = v == "auto" ? TeamSelection.AutoOnly : TeamSelection.Manual; break;
                    case "team_max_diff" when int.TryParse(v, out var i): s.TeamMaxDiff = Math.Clamp(i, 0, TeamRules.MaxDiffLimit); break;
                    case "physics" when Enum.TryParse<PhysicsMode>(v, true, out var pm) && Enum.IsDefined(pm): s.Physics = pm; break;
                    case "team_balance": s.TeamBalance = Game.ParseTeamBalance(v); break;
                    case "humans_team": s.HumansTeam = v == "1" ? (byte)1 : (byte)0; break;
                    case "lobby_return": s.LobbyReturn = v == "1"; break;
                    case "match_minutes" when int.TryParse(v, out var mm): s.MatchMinutes = Math.Clamp(mm, 1, 60); break;
                    case "score_limit" when Enum.TryParse<ScoreLimitMode>(v, true, out var sl) && Enum.IsDefined(sl): s.ScoreLimit = sl; break;
                    case "display" when Enum.TryParse<DisplayMode>(v, out var dm): s.Display = dm; break;
                    case "width" when int.TryParse(v, out var i): s.Width = Math.Clamp(i, 640, 16384); break;
                    case "height" when int.TryParse(v, out var i): s.Height = Math.Clamp(i, 360, 16384); break;
                    case "ui_scale" when float.TryParse(v, inv, out var f): s.UiScale = Math.Clamp(f, 0.6f, 2.5f); break;
                    case "hud_width" when Enum.TryParse<HudWidth>(v, out var hw) && Enum.IsDefined(hw): s.HudWidth = hw; break;
                    case "name_tags" when Enum.TryParse<NameTagMode>(v, out var nt) && Enum.IsDefined(nt): s.NameTags = nt; break;
                    case "name_tag_health": s.NameTagHealth = v == "1"; break;
                    case "wb_show_incompatible": s.WorkbenchShowIncompatible = v == "1"; break;
                    case "wb_show_all_grips": s.WorkbenchShowAllGrips = v == "1"; break;
                    case "head_bob" when int.TryParse(v, out var i): s.HeadBob = Math.Clamp(i, 0, 100); break;
                    case "foliage" when Enum.TryParse<FoliageQuality>(v, out var fq) && Enum.IsDefined(fq): s.FoliageQuality = fq; break;
                    case "sky" when Enum.TryParse<SkyQuality>(v, out var sq) && Enum.IsDefined(sq): s.SkyQuality = sq; break;
                    case "lighting" when Enum.TryParse<LightingQuality>(v, out var lq) && Enum.IsDefined(lq): s.Lighting = lq; break;
                    case "impact_decals" when Enum.TryParse<ImpactDecalQuality>(v, out var iq) && Enum.IsDefined(iq): s.ImpactDecals = iq; break;
                    case "frenzy_visuals" when Enum.TryParse<FrenzyVisualMode>(v, true, out var fv) && Enum.IsDefined(fv): s.FrenzyVisuals = fv; break;
                    case "blood_overlay" when Enum.TryParse<BloodMode>(v, true, out var bo) && Enum.IsDefined(bo): s.BloodOverlay = bo; break;
                    case "blood_colour" when Enum.TryParse<BloodColour>(v, true, out var bc) && Enum.IsDefined(bc): s.BloodColour = bc; break;
                    case "gore" when Enum.TryParse<GoreMode>(v, true, out var gm) && Enum.IsDefined(gm): s.Gore = gm; break;
                    case "frenzy_gunsound" when Enum.TryParse<FrenzyGunSoundMode>(v, true, out var fg) && Enum.IsDefined(fg): s.FrenzyGunSound = fg; break;
                    case var k when k.StartsWith("bind_") && Enum.TryParse<GameAction>(k[5..], out var action)
                                    && action < GameAction.COUNT && int.TryParse(v, out var code):
                        s.Binds[(int)action] = code;
                        break;
                    case "binds_version" when int.TryParse(v, out var bv): bindsVersion = bv; break;
                }
            }
            var moved = Keybinds.Migrate(s.Binds, bindsVersion);
            if (moved.Count > 0)
                Console.WriteLine($"[settings] keybinds updated to the new defaults: {string.Join(", ", moved.Select(a => $"{a} -> {(Brutal.GlfwApi.GlfwKey)s.Bind(a)}"))}");
        }
        catch
        {
            // Unreadable: defaults.
        }
        return s;
    }

    public void Save() => Save(FilePath);

    internal void Save(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        try
        {
            SavePaths.WriteLines(
                path,
                [
                    $"name={Name}",
                    $"sensitivity={Sensitivity.ToString(inv)}",
                    $"fov={Fov.ToString(inv)}",
                    $"invert_y={(InvertY ? 1 : 0)}",
                    $"smart_rotation={(SmartRotation ? 1 : 0)}",
                    $"snap_build={(SnapBuild ? 1 : 0)}",
                    $"hold_to_paint={(HoldToPaint ? 1 : 0)}",
                    $"smart_rotation_learned={string.Join(",", SmartRotationLearned)}",
                    $"volume={Volume.ToString(inv)}",
                    $"music_volume={MusicVolume.ToString(inv)}",
                    $"effects_volume={EffectsVolume.ToString(inv)}",
                    $"footstep_volume={FootstepVolume.ToString(inv)}",
                    $"own_gun_volume={OwnGunVolume.ToString(inv)}",
                    $"music_style={MusicChoice}",
                    $"frenzy_music={(FrenzyMusic ? 1 : 0)}",
                    $"frenzy_volume={FrenzyVolume.ToString(inv)}",
                    $"vsync={(VSync ? 1 : 0)}",
                    $"max_fps={MaxFps.ToString(inv)}",
                    $"max_fps_menu={MaxFpsMenu.ToString(inv)}",
                    $"max_fps_background={MaxFpsBackground.ToString(inv)}",
                    $"voice_mode={VoiceMode}",
                    $"voice_transmit={(VoiceTransmit ? 1 : 0)}",
                    $"voice_activation={(VoiceActivation ? 1 : 0)}",
                    $"voice_threshold={VoiceThresholdDb.ToString(inv)}",
                    $"voice_device={VoiceDevice}",
                    $"voice_volume={VoiceVolume.ToString(inv)}",
                    $"voice_mute_all={(VoiceMuteAll ? 1 : 0)}",
                    $"allow_voice={(AllowVoice ? 1 : 0)}",
                    $"dead_chat={(DeadChatPrivate ? "dead" : "all")}",
                    $"show_fps={(ShowFps ? 1 : 0)}",
                    $"weapon_tweaks={(AllowWeaponTweaks ? 1 : 0)}",
                    $"dev_tools={(AllowDevTools ? 1 : 0)}",
                    $"net_stats={(ShowNetStats ? 1 : 0)}",
                    $"bots={Bots}",
                    $"difficulty={Difficulty}",
                    $"bot_gear={CallOfSlop.BotGear.Key(BotGear)}",
                    $"port={Port}",
                    $"upnp={(UseUpnp ? 1 : 0)}",
                    $"host_bots={HostBots}",
                    $"join_address={JoinAddress}",
                    $"map={MapName}",
                    $"map_random={(MapRandom ? 1 : 0)}",
                    $"map_pack={MapPack}",
                    $"share_on_lists={(ShareOnLists ? 1 : 0)}",
                    $"auto_list={(AutoListServer ? 1 : 0)}",
                    $"run_list={(RunListServer ? 1 : 0)}",
                    $"stun={(UseStun ? 1 : 0)}",
                    $"frenzy_buffs={(FrenzyBuffs ? 1 : 0)}",
                    $"destruction={(Destruction ? 1 : 0)}",
                    $"server_name={ServerName}",
                    $"master={MasterServer}",
                    $"update_check={(UpdateCheck ? 1 : 0)}",
                    $"steam_name={(SteamName ? 1 : 0)}",
                    $"steam_lobby={SteamLobby}",
                    $"steam_bans={string.Join(",", SteamBans.Select(b => b.ToString(inv)))}",
                    $"update_skip={UpdateSkip}",
                    $"update_cleanup={(UpdateCleanup ? 1 : 0)}",
                    $"update_keep_previous={(UpdateKeepPrevious ? 1 : 0)}",
                    $"mode={Mode}",
                    $"auto_start={AutoStart}",
                    $"gravity={Gravity}",
                    $"zombie_horde_cap={ZombieHordeCap}",
                    $"zombie_start_round={ZombieStartRound}",
                    $"zombie_test_kit={(ZombieTestKit ? 1 : 0)}",
                    $"zombie_stress_size={ZombieStressSize}",
                    $"zombie_stress_endless={(ZombieStressEndless ? 1 : 0)}",
                    $"team_select={(TeamSelect == TeamSelection.AutoOnly ? "auto" : "manual")}",
                    $"team_max_diff={TeamMaxDiff}",
                    $"physics={Physics.ToString().ToLowerInvariant()}",
                    $"score_limit={ScoreLimit.ToString().ToLowerInvariant()}",
                    $"team_balance={Game.TeamBalanceKey(TeamBalance)}",
                    $"humans_team={HumansTeam}",
                    $"lobby_return={(LobbyReturn ? 1 : 0)}",
                    $"match_minutes={MatchMinutes}",
                    $"display={Display}",
                    $"width={Width}",
                    $"height={Height}",
                    $"ui_scale={UiScale.ToString(inv)}",
                    $"hud_width={HudWidth}",
                    $"foliage={FoliageQuality}",
                    $"sky={SkyQuality}",
                    $"lighting={Lighting}",
                    $"impact_decals={ImpactDecals}",
                    $"frenzy_visuals={FrenzyVisuals}",
                    $"gore={Gore}",
                    $"frenzy_gunsound={FrenzyGunSound}",
                    $"blood_overlay={BloodOverlay}",
                    $"blood_colour={BloodColour}",
                    $"head_bob={HeadBob}",
                    $"name_tags={NameTags}",
                    $"name_tag_health={(NameTagHealth ? 1 : 0)}",
                    $"wb_show_incompatible={(WorkbenchShowIncompatible ? 1 : 0)}",
                    $"wb_show_all_grips={(WorkbenchShowAllGrips ? 1 : 0)}",
                    $"binds_version={Keybinds.DefaultsVersion}",
                    .. Enumerable.Range(0, (int)GameAction.COUNT).Select(i => $"bind_{(GameAction)i}={Binds[i]}"),
                    .. VoicePlayerLines(),
                ],
                SaveVersion
            );
        }
        catch
        {
            // Read-only install folder: settings just won't persist.
        }
    }
}
