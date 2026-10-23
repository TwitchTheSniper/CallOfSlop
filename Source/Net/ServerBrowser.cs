using System.Diagnostics;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// What a hosted game tells the world, answered automatically by RakNet to unconnected pings
/// (the "offline ping response"). Starts with a magic tag so it can be found inside RakNet's pong.
/// </summary>
public readonly record struct ServerInfo(string Name, string MapName, byte Players, byte MaxPlayers, byte Bots, MatchPhase Phase, ushort Port, byte Version,
    GameMode Mode = GameMode.FreeForAll, PhysicsMode Physics = PhysicsMode.Classic, string Rotation = "", string Build = "")
{
    // (Build, the host's release version such as "0.2026.1022.5" (BuildInfo.cs; "" for dev builds and older hosts), rides
    // after the rotation as 'B', a length and ASCII. With a build and no rotation, a 0 rotation length comes first.
    // Older builds stop reading after the rotation, so it needs no version bump either; the browser uses it to say
    // "your build is older / newer" next to a game.)
    // 3: perks and melee (loadout and snapshot packets changed); 4: weapon files (weapon-set fingerprint in the Welcome);
    // 5: kill packet carries the weapon kit, killer HP and damage source; 6: kill streaks (loadout packet + streak messages);
    // 7: game modes and ready-up (mode state, ready and mode event packets, warmup phase, mode in the server info);
    // 8: per-streak stored kills in the streak state packet (using a streak empties it); 9: suicide request message;
    // 10: sprint speed 9 -> 13.5 m/s (shared movement sim);
    // 11: up to 32 players: quantised, split snapshots with a presence mask (Snapshot.cs), server-full message,
    //     per-map player limits (max players in this info is the map's), TDM kill limit in the mode state
    // 12: breakable props (prop break and prop state messages)
    // 13: physics mode (classic | swept movement) in the Welcome and at the end of this info
    // (Rotation, the host's map rotation such as "True Random", rides after the physics byte as an optional trailing string:
    // older builds stop reading before it and newer ones read "" from older hosts, so it needs no version bump.)
    // 14: terrain slope rules (SlopeRules: footprint slope, walkable to 45 degrees, slower uphill, grip and terrain steps,
    //     capped steerable slides) in both movers: client prediction must run the same movement as the server
    // 15: Last Stand shield charges and a regenerating flag in the snapshot's per-player flags byte, shield hit message
    // 16: Zombies preview: push carts (cart use, cart state messages) and per-run flags (run state message: weapon-slot upgrade)
    // 17: the push cart is a manned gun, not a turret: cart use carries tap / hold, cart state carries the gun's aim
    //     (yaw offset and pitch) instead of turret seconds, and mode 2 is "manned"
    // 18: the Zombies mode: game mode 5, zombie / run / event / parts / build / interact / workbench messages (ZUse..ZBench at
    //     the end of Msg), built weapons replicated to clients as build text, the window-opening collision layer (Clip)
    // 19: Zombies dropped builds: bleeding out drops your built weapons as pickups (ZPickup at the end of Msg: appear / gone)
    // 20: team selection: TeamPick / TeamReply at the end of Msg, the team rules and every player's pick at the end of the mode state
    // 21: score limits scale with the player count (ScoreLimits.cs): the mode state's last U16 is the current mode's score
    //     limit (free-for-all kills, team kills, hill points or captures), no longer only the TDM kill limit
    // 22: game modes are modules (Source/Game/Modes): the mode state no longer carries the hill and the flags for every
    //     mode; after the team picks comes the active mode's own tail (a version byte, a U16 length, then whatever the
    //     mode's GameModeModule.WriteState writes: KotH 7 bytes, CTF 36, others none). Game mode 6 is the Dev-only template.
    // 23: buildings (Buildings folder, the Building op with a "name"): BuildingUse / BuildingEvent / BuildingState at the end
    //     of Msg (doors open, shut, take damage and break, windows break; the join-in-progress state), the Doors physics layer
    // 24: prestige hats (GameCosmetics.cs): the Hello carries prestige and level after the name; every roster entry ends
    //     with prestige, level and hat (3 bytes)
    // 25: cosmetic loadout (GameCosmeticLoadout.cs): Msg.Loadout ends with the hat byte (after the kill streaks); the
    //     server keeps it only if the player's reported prestige unlocks it, and the roster carries it (protocol 24).
    // 26: frenzy per weapon class (Source/Game/Frenzy): every snapshot slow entry ends with the player's frenzy level
    //     and frenzy class (+ the host's frenzy-buffs bit), 2 bytes (Snapshot.SlowBytes 16 -> 18).
    // 27: tactical equipment (GameTactical.cs): ThrowableKind gains Flash .. Stim (Msg.Throw / Loadout bytes), and the
    //     new Msg.TacState (devices with the snapshots) and Msg.TacEvent (flash / stun, intercepts, pings...) at the end.
    // 28: ragdolls and blast knockback (Knockback.cs): Msg.Knock at the end of Msg (server -> the pushed client: input
    //     sequence + impulse, put into the prediction replay); blasts change living players' velocity on the server.
    // 29: eight more kill streaks (KillStreaksMore.cs): Msg.StreakState carries 16 earned-streak bits and armour (8 -> 12
    //     bytes), Msg.StreakEvent gains kinds 2-4 (effect ended, crate taken, streak blast), StreakIds 8-15 on the wire.
    // 30: launchers (Source/Game/Launchers): WeaponClass.Launcher weapons, the new Msg.RocketSpawn / RocketSync /
    //     RocketBoom / RocketDetonate at the end, and the Fire packet's trailing lock-on byte.
    // 31: landmines and prop respawns (GameMines.cs): Msg.PropBreak's kind byte gains Game.PropRestoreFlag (0x40, a prop
    //     placed with "respawn" is back), and kill id 254 (Game.LandmineKillId, "Landmine") for neutral blasts.
    // 32: the pre-match lobby room (PreMatchLobby.cs): MatchPhase.Lobby (4) in snapshots, the server info and MatchStart,
    //     and Msg.LobbyState / ChatSay / ChatLine at the end of Msg (lobby settings and pings, lobby chat).
    // 33: blood marks (BodyBlood.cs): every snapshot fast entry ends with the player's 32-bit blood word (hit body parts,
    //     generation, last hit direction; Snapshot.FastBytes 24 -> 28).
    // 34: meaty gibs (Gibs.cs, GameGibs.cs): bit 1 of Msg.Kill's headshot byte is "gibbed" (blown apart by a blast).
    // 35: destructible buildings (GameDestruction.cs): Msg.StructBreak / StructState at the end of Msg; destructible maps'
    //     fingerprints include their chunks (MapDestruction.Map.cs).
    // 36: in-match text chat and voice chat (LobbyChat.cs, GameVoice.cs): ChatSay gains a flags byte (team), ChatLine gains
    //     flags (team / dead / system) and the sender's team; Msg.VoiceUp / VoiceDown / VoiceCtl / VoiceInfo at the end of Msg.
    public const byte ProtocolVersion = 36;
    private static readonly byte[] Magic = "COS1"u8.ToArray();

    public byte[] Encode()
    {
        var name = Encoding.ASCII.GetBytes(Name.Length > 32 ? Name[..32] : Name);
        var map = Encoding.UTF8.GetBytes(MapName.Length > 40 ? MapName[..40] : MapName);
        var b = new List<byte>(Magic);
        b.AddRange([Version, (byte)Mode, Players, MaxPlayers, Bots, (byte)Phase, (byte)Port, (byte)(Port >> 8), (byte)name.Length]);
        b.AddRange(name);
        b.Add((byte)map.Length);
        b.AddRange(map);
        b.Add((byte)Physics);
        var rotation = Encoding.ASCII.GetBytes(Rotation.Length > 24 ? Rotation[..24] : Rotation);
        var build = Encoding.ASCII.GetBytes(Build.Length > 23 ? Build[..23] : Build);
        if (rotation.Length > 0 || build.Length > 0)
        {
            b.Add((byte)rotation.Length);
            b.AddRange(rotation);
        }
        if (build.Length > 0)
        {
            b.Add((byte)'B');
            b.Add((byte)build.Length);
            b.AddRange(build);
        }
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
        var physics = rest.Length >= 1 && rest.Length >= 2 + rest[0] && rest[1 + rest[0]] == (byte)PhysicsMode.Swept ? PhysicsMode.Swept : PhysicsMode.Classic;
        var rotation = "";
        var build = "";
        if (rest.Length >= 1 && rest.Length >= 3 + rest[0])
        {
            var tail = rest[(2 + rest[0])..]; // after the map name and the physics byte
            if (tail.Length >= 1 + tail[0])
            {
                rotation = Encoding.ASCII.GetString(tail.Slice(1, tail[0]));
                var after = tail[(1 + tail[0])..];
                if (after.Length >= 2 && after[0] == (byte)'B' && after.Length >= 2 + after[1])
                    build = BuildInfo.ParseRelease(Encoding.ASCII.GetString(after.Slice(2, after[1])))?.ToString() ?? "";
            }
        }
        // Anyone can answer a ping: names are capped printable ASCII before the browser shows them.
        return new ServerInfo(NetSafety.CleanText(name, 32), NetSafety.CleanText(map, 40), d[2], d[3], d[4], (MatchPhase)d[5], (ushort)(d[6] | (d[7] << 8)), d[0],
            (GameMode)d[1], physics, NetSafety.CleanText(rotation, 24), build);
    }
}

/// <summary>How a listed game's build compares with ours (the server browser shows it and only lets compatible ones join).</summary>
public enum BuildCompat
{
    Same, // same protocol and the same (or an unknown) release
    OtherBuild, // same protocol, a different release: joinable
    ServerNewer, // newer protocol: update to join
    ServerOlder, // older protocol: the host must update
}

public static class BuildCompatText
{
    /// <summary>Compares a game's protocol and release (<see cref="ServerInfo.Build"/>, "" when unknown) with ours.</summary>
    public static BuildCompat Compare(byte protocol, string build, byte ourProtocol, Version? ourRelease)
    {
        var theirs = BuildInfo.ParseRelease(build);
        if (protocol != ourProtocol)
        {
            if (theirs != null && ourRelease != null && theirs != ourRelease)
                return theirs > ourRelease ? BuildCompat.ServerNewer : BuildCompat.ServerOlder;
            return protocol > ourProtocol ? BuildCompat.ServerNewer : BuildCompat.ServerOlder;
        }
        return theirs != null && ourRelease != null && theirs != ourRelease ? BuildCompat.OtherBuild : BuildCompat.Same;
    }

    /// <summary>The line under a game in the browser ("" for a game on our build).</summary>
    public static string Describe(BuildCompat c, string build) => c switch
    {
        BuildCompat.ServerNewer => $"Your build is older - update to join{(build.Length > 0 ? $" (host has v{build})" : "")}",
        BuildCompat.ServerOlder => $"The host's build is older{(build.Length > 0 ? $" (v{build})" : "")} - they must update before you can join",
        BuildCompat.OtherBuild => $"Host has v{build}, you have v{BuildInfo.Version} (compatible)",
        _ => "",
    };
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
            try
            {
                OnBrowserPacket(from, data);
            }
            catch (Exception ex)
            {
                NetSafety.Drop("a server-browser reply", data, ex); // a bad reply is dropped, the browser keeps going
            }
        });
    }

    private void OnBrowserPacket(SystemAddress from, ReadOnlySpan<byte> data)
    {
        var ip = from.ToString(false, '|');
        switch ((DefaultMessageIDTypes)data[0])
        {
            // Games on other protocol versions are listed too (greyed out, "update to join"), not hidden: BuildCompat says why.
            case DefaultMessageIDTypes.UnconnectedPong when ServerInfo.Find(data) is { } info:
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
            var addr = NetSafety.CleanText(Encoding.ASCII.GetString(d.Slice(p, len)), 64);
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
            try
            {
                OnMasterPacket(net, games, from, data);
            }
            catch (Exception ex)
            {
                NetSafety.Drop("a master-server message", data, ex);
            }
        });
    }

    private static void OnMasterPacket(NetPeer net, Dictionary<string, (string Ip, ushort Port, DateTime Seen)> games, SystemAddress from, ReadOnlySpan<byte> data)
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
