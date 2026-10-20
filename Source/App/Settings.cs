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
    public bool VSync = true;
    public bool ShowNetStats = true;
    public DisplayMode Display = DisplayMode.Windowed;
    public int Width = 1600; // windowed size, or fullscreen resolution
    public int Height = 900;
    public float UiScale = 1f; // multiplier on the automatic scale (which follows window height)

    /// <summary>Set by the settings screen when display options change; Program applies them.</summary>
    [NonSerialized] public bool DisplayDirty;

    // Controls
    public readonly int[] Binds = (int[])Keybinds.Defaults.Clone();

    public int Bind(GameAction a) => Binds[(int)a];

    // Single player
    public int Bots = 3;
    public BotDifficulty Difficulty = BotDifficulty.Normal;

    // Multiplayer
    public ushort Port = Program.DefaultPort;
    public bool UseUpnp = true;
    public int HostBots;
    public string JoinAddress = "127.0.0.1";
    public string MapName = "Proto Box"; // map for single player and hosting (when not random)
    public bool MapRandom = true; // default: a random map each time you start
    public string MapPack = "Classic"; // the map pack "random" picks from (and the lobby lists)
    public bool Advertise; // list my hosted game on the public server list
    public string ServerName = ""; // empty: "<name>'s game"
    public string MasterServer = ""; // host[:port] of a master server for the public list (empty: LAN only)

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

    /// <summary>The map to start: the chosen one, or a random one from the selected pack.</summary>
    public Map PickMap() => PickMap(Maps.Packs, Maps.All, Random.Shared);

    /// <summary>
    /// <see cref="PickMap()"/> over a given set of packs and maps (the self-test uses its own). A pack that's gone
    /// falls back to all maps, and so does a chosen map that's gone.
    /// </summary>
    public Map PickMap(IReadOnlyList<MapPack> packs, IReadOnlyList<Map> all, Random rng)
    {
        if (!MapRandom && all.FirstOrDefault(m => m.Name.Equals(MapName, StringComparison.OrdinalIgnoreCase)) is { } chosen)
            return chosen;
        var pack = packs.FirstOrDefault(p => p.Name.Equals(MapPack, StringComparison.OrdinalIgnoreCase) && p.Maps.Count > 0);
        return pack != null ? pack.PickRandom(rng) : all[rng.Next(all.Count)];
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

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            if (!File.Exists(FilePath))
                return s;
            foreach (var line in File.ReadAllLines(FilePath))
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
                    case "vsync": s.VSync = v == "1"; break;
                    case "net_stats": s.ShowNetStats = v == "1"; break;
                    case "bots" when int.TryParse(v, out var i): s.Bots = Math.Clamp(i, 1, Game.MaxPlayers - 1); break;
                    case "difficulty" when Enum.TryParse<BotDifficulty>(v, out var d): s.Difficulty = d; break;
                    case "port" when ushort.TryParse(v, out var p) && p > 0: s.Port = p; break;
                    case "upnp": s.UseUpnp = v == "1"; break;
                    case "host_bots" when int.TryParse(v, out var i): s.HostBots = Math.Clamp(i, 0, Game.MaxPlayers - 2); break;
                    case "join_address": s.JoinAddress = v; break;
                    case "map" when Maps.Find(v) != null: s.MapName = v; break;
                    case "map_random": s.MapRandom = v == "1"; break;
                    case "map_pack" when v.Length > 0: s.MapPack = v; break; // kept even if the pack is missing now (it may come back)
                    case "advertise": s.Advertise = v == "1"; break;
                    case "server_name": s.ServerName = CleanServerName(v); break;
                    case "master": s.MasterServer = v.Trim(); break;
                    case "display" when Enum.TryParse<DisplayMode>(v, out var dm): s.Display = dm; break;
                    case "width" when int.TryParse(v, out var i): s.Width = Math.Clamp(i, 640, 16384); break;
                    case "height" when int.TryParse(v, out var i): s.Height = Math.Clamp(i, 360, 16384); break;
                    case "ui_scale" when float.TryParse(v, inv, out var f): s.UiScale = Math.Clamp(f, 0.6f, 2.5f); break;
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

    public void Save()
    {
        var inv = CultureInfo.InvariantCulture;
        try
        {
            File.WriteAllLines(
                FilePath,
                [
                    $"name={Name}",
                    $"sensitivity={Sensitivity.ToString(inv)}",
                    $"fov={Fov.ToString(inv)}",
                    $"invert_y={(InvertY ? 1 : 0)}",
                    $"volume={Volume.ToString(inv)}",
                    $"vsync={(VSync ? 1 : 0)}",
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
                    $"display={Display}",
                    $"width={Width}",
                    $"height={Height}",
                    $"ui_scale={UiScale.ToString(inv)}",
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
