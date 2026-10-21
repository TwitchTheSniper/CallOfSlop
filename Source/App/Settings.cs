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
}

/// <summary>Player preferences, saved to settings.txt next to the game.</summary>
public sealed class Settings
{
    // Player
    public string Name = DefaultName();
    public float Sensitivity = 1f; // multiplier
    public float Fov = 75f;
    public bool InvertY;

    // Audio / video
    public float Volume = 0.8f;
    public float MusicVolume = 0.5f; // menu and lobby music, on top of the master volume (0 = off)
    public float EffectsVolume = 1f; // gunfire, hits and every other sound effect, on top of the master volume
    public float FootstepVolume = 1f; // footsteps, jumps and landings, on top of the effects volume
    public MusicChoice MusicChoice = MusicChoice.Mixed; // Settings > Audio > Music style
    public bool VSync = true;

    /// <summary>A small fps counter in the top-right corner.</summary>
    public bool ShowFps = true;
    public bool ShowNetStats = true;
    public DisplayMode Display = DisplayMode.Windowed;
    public int Width = 1600; // windowed size, or fullscreen resolution
    public int Height = 900;
    public float UiScale = 1f; // multiplier on the automatic scale (which follows window height)
    public HudWidth HudWidth = HudWidth.Wide16x9; // in-match HUD kept inside a centred area this wide
    public FoliageQuality FoliageQuality = FoliageQuality.Medium; // grass, bushes and tree canopies (Off draws none)
    public SkyQuality SkyQuality = SkyQuality.High; // maps with a sky: Low = gradient, sun and moon; High = plus clouds and stars

    /// <summary>Camera head bob when moving, in percent (0 = none, the steady camera; 100 = full). See HeadBob.</summary>
    public int HeadBob = 100;

    /// <summary>Set by the settings screen when display options change; Program applies them.</summary>
    [NonSerialized] public bool DisplayDirty;

    // Controls
    public readonly int[] Binds = (int[])Keybinds.Defaults.Clone();

    public int Bind(GameAction a) => Binds[(int)a];

    // Single player
    public int Bots = 3; // 0 = "No bots (test mode)": you alone, the match never ends on its own
    public BotDifficulty Difficulty = BotDifficulty.Normal;

    // Multiplayer
    public ushort Port = Program.DefaultPort;
    public bool UseUpnp = true;
    public int HostBots;
    public string JoinAddress = "127.0.0.1";
    public string MapName = "Proto Box"; // map for single player and hosting (when not random)
    public bool MapRandom = true; // default: a random map each time you start
    public string MapPack = "Classic"; // the map pack "random" picks from (and the lobby lists); "*all" = True Random (MapPack.TrueRandomKey)
    public bool Advertise; // list my hosted game on the public server list
    public string ServerName = ""; // empty: "<name>'s game"
    public string MasterServer = ""; // host[:port] of a master server for the public list (empty: LAN only)

    // Matches (single player and hosting)
    public GameMode Mode = GameMode.FreeForAll;
    public int AutoStart; // hosting: start N seconds after the first person readies up (0: wait for everyone)
    public PhysicsMode Physics = PhysicsMode.Classic; // hosting: player movement (classic, or the swept capsule controller); joiners use the host's

    public string ServerNameOrDefault => ServerName.Length > 0 ? ServerName : $"{Name}'s game";

    /// <summary>The master server as (host, port), or null if none is set.</summary>
    public (string Host, ushort Port)? Master
    {
        get
        {
            var m = MasterServer.Trim();
            if (m.Length == 0)
                return null;
            var colon = m.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(m[(colon + 1)..], out var p))
                return (m[..colon], p);
            return (m, MasterServerApp.DefaultPort);
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

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.txt");

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
    public static string CleanServerName(string name) => new string(name.Where(c => c >= 32 && c < 127).Take(32).ToArray()).Trim();

    /// <summary>Addresses (join address, master server): printable ASCII without spaces, up to 127 characters (fits the lobby's text box).</summary>
    public static string CleanAddress(string address) => new string(address.Where(c => c > 32 && c < 127).Take(127).ToArray());

    public static Settings Load() => Load(FilePath);

    /// <summary>Loads a settings file from <paramref name="path"/> (the self-test uses a temporary one).</summary>
    public static Settings Load(string path)
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(path))
                return s;
            foreach (var line in File.ReadAllLines(path))
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
                    case "sensitivity" when float.TryParse(v, inv, out var f): s.Sensitivity = Math.Clamp(f, 0.1f, 5f); break;
                    case "fov" when float.TryParse(v, inv, out var f): s.Fov = Math.Clamp(f, 60f, 110f); break;
                    case "invert_y": s.InvertY = v == "1"; break;
                    case "volume" when float.TryParse(v, inv, out var f): s.Volume = Math.Clamp(f, 0f, 1f); break;
                    case "music_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.MusicVolume = Math.Clamp(f, 0f, 1f); break;
                    case "effects_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.EffectsVolume = Math.Clamp(f, 0f, 1f); break;
                    case "footstep_volume" when float.TryParse(v, inv, out var f) && float.IsFinite(f): s.FootstepVolume = Math.Clamp(f, 0f, 1f); break;
                    case "music_style" when Enum.TryParse<MusicChoice>(v, true, out var mc) && Enum.IsDefined(mc): s.MusicChoice = mc; break;
                    case "vsync": s.VSync = v == "1"; break;
                    case "show_fps": s.ShowFps = v == "1"; break;
                    case "net_stats": s.ShowNetStats = v == "1"; break;
                    case "bots" when int.TryParse(v, out var i): s.Bots = Math.Clamp(i, 0, Game.MaxPlayers - 1); break;
                    case "difficulty" when Enum.TryParse<BotDifficulty>(v, out var d): s.Difficulty = d; break;
                    case "port" when ushort.TryParse(v, out var p) && p > 0: s.Port = p; break;
                    case "upnp": s.UseUpnp = v == "1"; break;
                    case "host_bots" when int.TryParse(v, out var i): s.HostBots = Math.Clamp(i, 0, Game.MaxPlayers - 2); break;
                    case "join_address": s.JoinAddress = CleanAddress(v); break;
                    case "map" when Maps.Find(v) != null: s.MapName = v; break;
                    case "map_random": s.MapRandom = v == "1"; break;
                    case "map_pack" when v.Length > 0: s.MapPack = v; break; // kept even if the pack is missing now (it may come back)
                    case "advertise": s.Advertise = v == "1"; break;
                    case "server_name": s.ServerName = CleanServerName(v); break;
                    case "master": s.MasterServer = CleanAddress(v); break;
                    case "mode" when Enum.TryParse<GameMode>(v, out var gm) && Enum.IsDefined(gm): s.Mode = gm; break;
                    case "auto_start" when int.TryParse(v, out var i): s.AutoStart = Math.Clamp(i, 0, 300); break;
                    case "physics" when Enum.TryParse<PhysicsMode>(v, true, out var pm) && Enum.IsDefined(pm): s.Physics = pm; break;
                    case "display" when Enum.TryParse<DisplayMode>(v, out var dm): s.Display = dm; break;
                    case "width" when int.TryParse(v, out var i): s.Width = Math.Clamp(i, 640, 16384); break;
                    case "height" when int.TryParse(v, out var i): s.Height = Math.Clamp(i, 360, 16384); break;
                    case "ui_scale" when float.TryParse(v, inv, out var f): s.UiScale = Math.Clamp(f, 0.6f, 2.5f); break;
                    case "hud_width" when Enum.TryParse<HudWidth>(v, out var hw) && Enum.IsDefined(hw): s.HudWidth = hw; break;
                    case "head_bob" when int.TryParse(v, out var i): s.HeadBob = Math.Clamp(i, 0, 100); break;
                    case "foliage" when Enum.TryParse<FoliageQuality>(v, out var fq) && Enum.IsDefined(fq): s.FoliageQuality = fq; break;
                    case "sky" when Enum.TryParse<SkyQuality>(v, out var sq) && Enum.IsDefined(sq): s.SkyQuality = sq; break;
                    case var k when k.StartsWith("bind_") && Enum.TryParse<GameAction>(k[5..], out var action)
                                    && action < GameAction.COUNT && int.TryParse(v, out var code):
                        s.Binds[(int)action] = code;
                        break;
                }
            }
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
            File.WriteAllLines(
                path,
                [
                    $"name={Name}",
                    $"sensitivity={Sensitivity.ToString(inv)}",
                    $"fov={Fov.ToString(inv)}",
                    $"invert_y={(InvertY ? 1 : 0)}",
                    $"volume={Volume.ToString(inv)}",
                    $"music_volume={MusicVolume.ToString(inv)}",
                    $"effects_volume={EffectsVolume.ToString(inv)}",
                    $"footstep_volume={FootstepVolume.ToString(inv)}",
                    $"music_style={MusicChoice}",
                    $"vsync={(VSync ? 1 : 0)}",
                    $"show_fps={(ShowFps ? 1 : 0)}",
                    $"net_stats={(ShowNetStats ? 1 : 0)}",
                    $"bots={Bots}",
                    $"difficulty={Difficulty}",
                    $"port={Port}",
                    $"upnp={(UseUpnp ? 1 : 0)}",
                    $"host_bots={HostBots}",
                    $"join_address={JoinAddress}",
                    $"map={MapName}",
                    $"map_random={(MapRandom ? 1 : 0)}",
                    $"map_pack={MapPack}",
                    $"advertise={(Advertise ? 1 : 0)}",
                    $"server_name={ServerName}",
                    $"master={MasterServer}",
                    $"mode={Mode}",
                    $"auto_start={AutoStart}",
                    $"physics={Physics.ToString().ToLowerInvariant()}",
                    $"display={Display}",
                    $"width={Width}",
                    $"height={Height}",
                    $"ui_scale={UiScale.ToString(inv)}",
                    $"hud_width={HudWidth}",
                    $"foliage={FoliageQuality}",
                    $"sky={SkyQuality}",
                    $"head_bob={HeadBob}",
                    .. Enumerable.Range(0, (int)GameAction.COUNT).Select(i => $"bind_{(GameAction)i}={Binds[i]}"),
                ]
            );
        }
        catch
        {
            // Read-only install folder: settings just won't persist.
        }
    }
}
