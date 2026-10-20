using System.Diagnostics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// What a hosted game tells the world, answered automatically by RakNet to unconnected pings
/// (the "offline ping response"). Starts with a magic tag so it can be found inside RakNet's pong.
/// </summary>
public readonly record struct ServerInfo(string Name, string MapName, byte Players, byte MaxPlayers, byte Bots, MatchPhase Phase, ushort Port, byte Version)
{
    public const byte ProtocolVersion = 3; // 3: perks and melee (loadout and snapshot packets changed)
    private static readonly byte[] Magic = "COS1"u8.ToArray();

    public byte[] Encode()
    {
        var name = Encoding.ASCII.GetBytes(Name.Length > 32 ? Name[..32] : Name);
        var map = Encoding.UTF8.GetBytes(MapName.Length > 40 ? MapName[..40] : MapName);
        var b = new List<byte>(Magic);
        b.AddRange([Version, 0, Players, MaxPlayers, Bots, (byte)Phase, (byte)Port, (byte)(Port >> 8), (byte)name.Length]);
        b.AddRange(name);
        b.Add((byte)map.Length);
        b.AddRange(map);
        return b.ToArray();
    }

    /// <summary>Finds and decodes server info anywhere in a packet (RakNet puts a header and timestamp before it).</summary>
    public static ServerInfo? Find(ReadOnlySpan<byte> packet)
    {
        var at = packet.IndexOf(Magic);
        if (at < 0)
            return null;
        var d = packet[(at + Magic.Length)..];
        if (d.Length < 9 || d.Length < 9 + d[8])
            return null;
        var name = Encoding.ASCII.GetString(d.Slice(9, d[8]));
        var rest = d[(9 + d[8])..];
        var map = rest.Length >= 1 && rest.Length >= 1 + rest[0] ? Encoding.UTF8.GetString(rest.Slice(1, rest[0])) : "?";
        return new ServerInfo(name, map, d[2], d[3], d[4], (MatchPhase)d[5], (ushort)(d[6] | (d[7] << 8)), d[0]);
    }
}

/// <summary>
/// Finds games to join:
///   - on your network, by broadcasting an unconnected ping to the usual game ports;
///   - on the internet, by asking a master server (if one is set in Settings) for its list of
///     advertised games, then pinging each of them directly for live details and latency.
/// </summary>
public sealed class ServerBrowser : IDisposable
{
    public sealed class Entry
    {
        public required string Address;
        public required ServerInfo Info;
        public int PingMs;
        public double LastSeen;
        public bool FromMaster;
    }

    private readonly NetPeer _net = NetPeer.Discovery();
    private readonly Dictionary<string, Entry> _servers = new();
    private readonly Dictionary<string, double> _pingSent = new(); // "ip:port" or ":port" (LAN broadcast) -> time
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _nextRefresh;
    private readonly HashSet<string> _fromMaster = new();

    public IEnumerable<Entry> Servers => _servers.Values.OrderBy(e => e.PingMs);
    public string MasterStatus { get; private set; } = "";
    public bool Searching => Now - _lastRefresh < 1.5;
    private double _lastRefresh = -10;

    private double Now => _clock.Elapsed.TotalSeconds;

    /// <summary>Ask again now (also happens every few seconds on its own).</summary>
    public void Refresh(Settings s)
    {
        _lastRefresh = Now;
        _nextRefresh = Now + 6;
        // LAN: the game port people use plus a few next to it
        foreach (var port in new[] { s.Port, Program.DefaultPort, (ushort)(Program.DefaultPort + 1), (ushort)(Program.DefaultPort + 2) }.Distinct())
        {
            _pingSent[$":{port}"] = Now;
            _net.PingHost("255.255.255.255", port);
        }
        // Internet: master server list
        if (s.Master is { } m)
        {
            _net.Advertise(m.Host, m.Port, MasterServerApp.ListQuery);
            if (MasterStatus.Length == 0)
                MasterStatus = $"Asking {m.Host}:{m.Port} for public games...";
        }
        else
        {
            MasterStatus = "";
        }
        // Re-ping everything we already know so pings and player counts stay fresh
        foreach (var e in _servers.Values.ToList())
            PingServer(e.Address, e.Info.Port);
        // Forget games that stopped answering
        foreach (var gone in _servers.Where(kv => Now - kv.Value.LastSeen > 15).Select(kv => kv.Key).ToList())
            _servers.Remove(gone);
    }

    private void PingServer(string ip, ushort port)
    {
        _pingSent[$"{ip}:{port}"] = Now;
        _net.PingHost(ip, port);
    }

    public void Tick(Settings s)
    {
        if (Now >= _nextRefresh)
            Refresh(s);
        _net.PollRaw((from, data) =>
        {
            var ip = from.ToString(false, '|');
            switch ((DefaultMessageIDTypes)data[0])
            {
                case DefaultMessageIDTypes.UnconnectedPong when ServerInfo.Find(data) is { } info && info.Version == ServerInfo.ProtocolVersion:
                {
                    var key = $"{ip}:{info.Port}";
                    var sent = _pingSent.TryGetValue(key, out var t) ? t : _pingSent.GetValueOrDefault($":{info.Port}", _lastRefresh);
                    var ping = (int)Math.Clamp((Now - sent) * 1000, 1, 9999);
                    if (_servers.TryGetValue(key, out var e))
                    {
                        e.Info = info;
                        e.PingMs = ping;
                        e.LastSeen = Now;
                    }
                    else
                    {
                        _servers[key] = new Entry { Address = ip, Info = info, PingMs = ping, LastSeen = Now, FromMaster = _fromMaster.Contains(key) };
                    }
                    break;
                }
                case DefaultMessageIDTypes.AdvertiseSystem when MasterServerApp.ParseList(data) is { } list:
                {
                    MasterStatus = list.Count == 0 ? "No public games right now." : $"{list.Count} public game(s) listed.";
                    foreach (var (addr, port) in list)
                    {
                        _fromMaster.Add($"{addr}:{port}");
                        PingServer(addr, port);
                    }
                    break;
                }
            }
        });
    }

    public void Dispose() => _net.Dispose();
}

/// <summary>
/// The master server: a tiny headless program ("CallOfSlop.exe --master [port]") that keeps the
/// public game list. Hosts that tick "Advertise" send it a heartbeat every 20 s from their game
/// socket; browsers ask it for the list. It only stores addresses: everything else (name, map,
/// players, ping) comes straight from each game when the browser pings it.
/// Run it on any always-on PC with its UDP port reachable (it tries UPnP itself).
/// </summary>
public static class MasterServerApp
{
    public const ushort DefaultPort = 27050;
    public static readonly byte[] ListQuery = "COLQ"u8.ToArray();
    private static readonly byte[] HeartbeatTag = "COHB"u8.ToArray();
    private static readonly byte[] ListTag = "COLS"u8.ToArray();

    /// <summary>Heartbeat payload a host sends (its game port).</summary>
    public static byte[] Heartbeat(ushort gamePort) => [.. HeartbeatTag, (byte)gamePort, (byte)(gamePort >> 8)];

    public static List<(string Address, ushort Port)>? ParseList(ReadOnlySpan<byte> packet)
    {
        var at = packet.IndexOf(ListTag);
        if (at < 0)
            return null;
        var d = packet[(at + ListTag.Length)..];
        if (d.Length < 1)
            return null;
        var list = new List<(string, ushort)>();
        var count = d[0];
        var p = 1;
        for (var i = 0; i < count && p < d.Length; i++)
        {
            int len = d[p++];
            if (p + len + 2 > d.Length)
                break;
            var addr = Encoding.ASCII.GetString(d.Slice(p, len));
            p += len;
            var port = (ushort)(d[p] | (d[p + 1] << 8));
            p += 2;
            list.Add((addr, port));
        }
        return list;
    }

    /// <summary>Handles everything waiting on the master's socket: heartbeats from hosts and list queries from browsers.</summary>
    public static void Pump(NetPeer net, Dictionary<string, (string Ip, ushort Port, DateTime Seen)> games)
    {
        net.PollRaw((from, data) =>
        {
            if ((DefaultMessageIDTypes)data[0] != DefaultMessageIDTypes.AdvertiseSystem)
                return;
            var ip = from.ToString(false, '|');
            var hb = data.IndexOf(HeartbeatTag);
            if (hb >= 0 && data.Length >= hb + 6)
            {
                var gamePort = (ushort)(data[hb + 4] | (data[hb + 5] << 8));
                var key = $"{ip}:{gamePort}";
                if (!games.ContainsKey(key))
                    Console.WriteLine($"[master] + {key}");
                games[key] = (ip, gamePort, DateTime.UtcNow);
                return;
            }
            if (data.IndexOf(ListQuery) >= 0)
            {
                var reply = new List<byte>(ListTag);
                var live = games.Values.Where(g => DateTime.UtcNow - g.Seen < TimeSpan.FromSeconds(60)).Take(60).ToList();
                reply.Add((byte)live.Count);
                foreach (var g in live)
                {
                    var a = Encoding.ASCII.GetBytes(g.Ip);
                    reply.Add((byte)a.Length);
                    reply.AddRange(a);
                    reply.Add((byte)g.Port);
                    reply.Add((byte)(g.Port >> 8));
                }
                net.Advertise(ip, from.DebugPort, reply.ToArray());
            }
        });
    }

    public static int Run(string[] args)
    {
        var port = args.SkipWhile(a => a != "--master").Skip(1).Select(a => ushort.TryParse(a, out var p) ? p : (ushort)0).FirstOrDefault(p => p > 0);
        if (port == 0)
            port = DefaultPort;
        Console.WriteLine($"[master] Call of Slop master server on UDP {port}. Close the window/process to stop.");
        using var upnp = UpnpMapping.TryCreate(port, Console.WriteLine);
        using var net = NetPeer.Host(port, 1);
        var games = new Dictionary<string, (string Ip, ushort Port, DateTime Seen)>();
        var lastLog = DateTime.MinValue;
        while (true)
        {
            Pump(net, games);
            foreach (var dead in games.Where(kv => DateTime.UtcNow - kv.Value.Seen > TimeSpan.FromSeconds(60)).Select(kv => kv.Key).ToList())
            {
                games.Remove(dead);
                Console.WriteLine($"[master] - {dead}");
            }
            if (DateTime.UtcNow - lastLog > TimeSpan.FromMinutes(5))
            {
                lastLog = DateTime.UtcNow;
                Console.WriteLine($"[master] {games.Count} game(s) listed");
            }
            Thread.Sleep(20);
        }
    }
}
