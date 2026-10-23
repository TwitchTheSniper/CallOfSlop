using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// A server list (the master server logic, <see cref="MasterServerApp"/>) running inside the game on a background
/// thread with its own socket: either because the player ticked "Run a server list for friends" (rank 0, kept while the
/// game runs), or because they host and none of their lists answered (rank 1, <see cref="HostListing"/>, kept while they
/// host). Port <see cref="MasterServerApp.DefaultPort"/> or the next free one; the router port is opened with UPnP like
/// the game port.
/// </summary>
public sealed class ListServer : IDisposable
{
    public ushort Port { get; }
    public MasterServerApp.Options Options { get; }

    /// <summary>Started by a host because no list answered (hands over to a better list when one appears).</summary>
    public bool Automatic => Options.Rank != 0;

    /// <summary>Games registered right now (read from any thread).</summary>
    public int GameCount => _gameCount;

    private volatile int _gameCount;
    private volatile bool _stop;
    private readonly NetPeer _net;
    private readonly Thread _thread;
    private readonly Action<string> _log;
    private Task<UpnpMapping?>? _upnp;

    private static readonly List<ListServer> RunningList = new();

    /// <summary>The lists running in this process (the browser and the host's probe include them).</summary>
    public static IReadOnlyList<ListServer> Running
    {
        get
        {
            lock (RunningList)
                return RunningList.ToArray();
        }
    }

    private ListServer(NetPeer net, ushort port, byte rank, bool upnp, Action<string> log)
    {
        _net = net;
        Port = port;
        _log = log;
        Options = new MasterServerApp.Options { Rank = rank, LanIp = UpnpMapping.GetLocalIPv4()?.ToString(), PublicIp = PublicAddress.Ip };
        if (upnp)
            _upnp = Task.Run(() => UpnpMapping.TryCreate(port, log));
        _thread = new Thread(Loop) { IsBackground = true, Name = $"Server list {port}" };
        lock (RunningList)
            RunningList.Add(this);
        _thread.Start();
    }

    /// <summary>
    /// Starts a list on the first free UDP port of <paramref name="firstPort"/> .. +<paramref name="ports"/>-1, or
    /// returns null with <paramref name="error"/> saying why.
    /// </summary>
    public static ListServer? TryStart(ushort firstPort, int ports, byte rank, bool upnp, Action<string> log, out string error)
    {
        for (var i = 0; i < Math.Max(1, ports); i++)
        {
            var port = (ushort)(firstPort + i);
            NetPeer net;
            try
            {
                net = NetPeer.Host(port, 1);
            }
            catch (Exception)
            {
                continue; // in use (another copy, or a --master window): try the next one
            }
            log($"[list] {(rank == 0 ? "server list for friends" : "no server list answered, so this game runs one")} on UDP {port}");
            error = "";
            return new ListServer(net, port, rank, upnp, log);
        }
        error = $"UDP ports {firstPort}-{firstPort + Math.Max(1, ports) - 1} are all in use.";
        log($"[list] could not start a server list: {error}");
        return null;
    }

    private void Loop()
    {
        var games = new Dictionary<string, (string Ip, ushort Port, DateTime Seen)>();
        var nextAddr = DateTime.MinValue;
        while (!_stop)
        {
            try
            {
                if (DateTime.UtcNow >= nextAddr)
                {
                    nextAddr = DateTime.UtcNow.AddSeconds(2);
                    if (PublicAddress.Ip is { } pub)
                        Options.PublicIp = pub;
                    if (_upnp is { IsCompletedSuccessfully: true, Result: { ExternalIp: { } ext, BehindSecondNat: false } } && Options.PublicIp == null)
                        Options.PublicIp = ext;
                }
                MasterServerApp.Pump(_net, games, Options);
                foreach (var dead in MasterServerApp.Expire(games))
                    _log($"[list] - {dead}");
                _gameCount = games.Count;
            }
            catch (Exception ex)
            {
                _log($"[list] error: {ex.Message}"); // keep serving
            }
            Thread.Sleep(15);
        }
    }

    public void Dispose()
    {
        if (_stop)
            return;
        _stop = true;
        lock (RunningList)
            RunningList.Remove(this);
        _thread.Join(1000);
        _net.Dispose();
        if (_upnp is { } u)
            _ = u.ContinueWith(t => (t.IsCompletedSuccessfully ? t.Result : null)?.Dispose(), TaskScheduler.Default); // never wait on the router here
        _log($"[list] server list on UDP {Port} stopped");
    }
}

/// <summary>
/// This PC's public IPv4 as last learned (UPnP's external address or STUN), shared by invite codes and in-game server
/// lists. Unknown (null) until something finds it.
/// </summary>
public static class PublicAddress
{
    private static volatile string? _upnp, _stun;
    private static DateTime _stunAt = DateTime.MinValue;
    private static Task? _stunTask;
    private static readonly object Gate = new();

    /// <summary>The public address: UPnP's when the router gave a public one, else STUN's, else null.</summary>
    public static string? Ip => _upnp ?? _stun;

    /// <summary>What STUN said (the address the internet sees), or null.</summary>
    public static string? StunIp => _stun;

    /// <summary>What the router said over UPnP (a public address only), or null.</summary>
    public static string? UpnpIp => _upnp;

    /// <summary>
    /// True when the router's public address and the one the internet sees differ: carrier-grade NAT or a second router,
    /// so nobody outside can reach a game here directly (only LAN play or a friend hosting works).
    /// </summary>
    public static bool Mismatch => _upnp != null && _stun != null && _upnp != _stun;

    public static void SetUpnp(string? ip) => _upnp = NetAddr.KindOf(ip) == NetAddr.Kind.Public ? ip : null;

    /// <summary>Asks STUN in the background (at most every 10 minutes). Never blocks.</summary>
    public static void RefreshStun(IReadOnlyList<(string Host, int Port)>? servers = null)
    {
        lock (Gate)
        {
            if (_stunTask is { IsCompleted: false } || (DateTime.UtcNow - _stunAt < TimeSpan.FromMinutes(10) && _stun != null))
                return;
            _stunAt = DateTime.UtcNow;
            _stunTask = Task.Run(async () =>
            {
                var ep = await Stun.QueryAsync(servers ?? Stun.DefaultServers, TimeSpan.FromSeconds(3));
                if (ep != null && NetAddr.KindOf(ep.Address.ToString()) == NetAddr.Kind.Public)
                {
                    _stun = ep.Address.ToString();
                    Console.WriteLine($"[net] public address {_stun} (STUN)");
                }
                else
                    Console.WriteLine("[net] STUN: no answer (public address unknown; invite codes use the router's or the LAN address)");
            });
        }
    }

    /// <summary>Self-test: forget everything.</summary>
    internal static void Reset()
    {
        _upnp = _stun = null;
        _stunAt = DateTime.MinValue;
    }
}

/// <summary>A list server that answered a probe (the address as configured, plus what it said about itself).</summary>
public readonly record struct ListReply(string Host, ushort Port, string Ip, byte Rank, uint Id, int Games);

/// <summary>Asks server lists whether they're there (a list query each, answers within the timeout). Runs off the UI thread.</summary>
public static class ListProbe
{
    public static Task<List<ListReply>> RunAsync(IReadOnlyList<(string Host, ushort Port)> lists, TimeSpan timeout) => Task.Run(() => Run(lists, timeout));

    public static List<ListReply> Run(IReadOnlyList<(string Host, ushort Port)> lists, TimeSpan timeout)
    {
        var replies = new List<ListReply>();
        if (lists.Count == 0)
            return replies;
        var wanted = new Dictionary<string, (string Host, ushort Port)>();
        foreach (var (host, port) in lists.Take(8))
        {
            var ip = Resolve(host);
            if (ip != null)
                wanted.TryAdd($"{ip}:{port}", (host, port));
        }
        if (wanted.Count == 0)
            return replies;
        using var net = NetPeer.Discovery();
        foreach (var key in wanted.Keys)
        {
            var colon = key.LastIndexOf(':');
            net.Advertise(key[..colon], ushort.Parse(key[(colon + 1)..]), MasterServerApp.ListQuery);
        }
        var until = DateTime.UtcNow + timeout;
        var seen = new HashSet<string>();
        while (DateTime.UtcNow < until && seen.Count < wanted.Count)
        {
            net.PollRaw((from, data) =>
            {
                try
                {
                    if ((DefaultMessageIDTypes)data[0] != DefaultMessageIDTypes.AdvertiseSystem)
                        return;
                    var ip = from.ToString(false, '|');
                    var key = $"{ip}:{from.DebugPort}";
                    if (!wanted.TryGetValue(key, out var cfg) || !seen.Add(key))
                        return;
                    if (MasterServerApp.ParseListFull(data, out var rank, out var id) is { } list)
                        replies.Add(new ListReply(cfg.Host, cfg.Port, ip, rank, id, list.Count));
                }
                catch (Exception ex)
                {
                    NetSafety.Drop("a server-list probe reply", data, ex);
                }
            });
            Thread.Sleep(10);
        }
        return replies;
    }

    /// <summary>IPv4 for a name or literal (a blocking lookup of at most 2 s: only call off the UI thread).</summary>
    internal static string? Resolve(string host)
    {
        if (IPAddress.TryParse(host, out var lit))
            return lit.AddressFamily == AddressFamily.InterNetwork ? lit.ToString() : null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cts.Token).GetAwaiter().GetResult().FirstOrDefault()?.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>A host's heartbeats to its server lists: every 20 s, and straight away when the lists change.</summary>
public sealed class Heartbeats
{
    private double _next;
    private object? _last;

    public void Tick(NetPeer net, ushort gamePort, IReadOnlyList<(string Host, ushort Port)> targets, double now, double every = 20)
    {
        if (!ReferenceEquals(targets, _last))
        {
            _last = targets;
            _next = now;
        }
        if (now < _next || targets.Count == 0)
            return;
        _next = now + every;
        foreach (var (host, port) in targets.Take(8))
        {
            if (HostNames.TryResolve(host) is { } ip)
                net.Advertise(ip, port, MasterServerApp.Heartbeat(gamePort));
            else
                _next = Math.Min(_next, now + 2); // a name still being looked up: try again soon
        }
    }
}

/// <summary>
/// The hosting side of "play with friends without setting anything up": when hosting starts it asks the player's
/// server lists (short probe, in the background). If one answers the game registers there; if none does (and Auto is
/// on) it runs a list itself (<see cref="ListServer"/>, rank 1) and registers its own game on it. Every
/// <see cref="Config.ReprobeSeconds"/> it asks again: a list that stopped answering is dropped (none left: start one),
/// and a better list (dedicated or hand-started, or an automatic one with a lower id) makes this game stop its own, so
/// a group doesn't keep two. The list it's on goes into the server info and the invite code, so players who join learn
/// it (<see cref="ListLearner"/>) and the group converges on one list without anyone typing addresses.
/// </summary>
public sealed class HostListing : IDisposable
{
    public sealed class Config
    {
        public TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1.5);
        public double ReprobeSeconds = 60;
        public ushort FirstListPort = MasterServerApp.DefaultPort;
        public int ListPorts = 10;
        public bool AutoStart = true;
        public bool Upnp;
        public Action<string> Log = Console.WriteLine;
    }

    private readonly Func<IReadOnlyList<(string Host, ushort Port)>> _lists;
    private readonly Config _cfg;
    private Task<List<ListReply>>? _probe;
    private double _nextProbe;

    /// <summary>The list this game runs itself (null: it's on someone else's, or none).</summary>
    public ListServer? OwnList { get; private set; }

    /// <summary>Where to send heartbeats (a new list instance whenever it changes, see <see cref="Heartbeats"/>).</summary>
    public IReadOnlyList<(string Host, ushort Port)> Targets { get; private set; } = [];

    /// <summary>The list to tell players about (server info 'L'): "" = on this host's own address.</summary>
    public string ListHost { get; private set; } = "";

    public ushort ListPort { get; private set; }

    /// <summary>One line for the host's screen.</summary>
    public string Status { get; private set; } = "Looking for a server list...";

    /// <summary>Probes finished so far (the self-test waits on it).</summary>
    public int Probes { get; private set; }

    public HostListing(Func<IReadOnlyList<(string Host, ushort Port)>> lists, Config? config = null)
    {
        _lists = lists;
        _cfg = config ?? new Config();
    }

    public void Tick(double now)
    {
        if (_probe is { IsCompleted: true } p)
        {
            _probe = null;
            Apply(p.IsCompletedSuccessfully ? p.Result : []);
            _nextProbe = now + _cfg.ReprobeSeconds;
            Probes++;
        }
        if (_probe == null && now >= _nextProbe)
        {
            var own = OwnList?.Port ?? 0;
            var lists = _lists().Where(l => !(own != 0 && l.Port == own && NetAddr.KindOf(HostNames.TryResolve(l.Host)) == NetAddr.Kind.Loopback)).ToList();
            _nextProbe = double.MaxValue;
            _probe = ListProbe.RunAsync(lists, _cfg.ProbeTimeout);
        }
    }

    private static bool Better(byte rankA, uint idA, byte rankB, uint idB) => rankA != rankB ? rankA < rankB : idA < idB;

    private void Apply(List<ListReply> replies)
    {
        var others = replies.Where(r => OwnList == null || r.Id != OwnList.Options.Id).ToList();
        var best = others.OrderBy(r => r.Rank).ThenBy(r => r.Id).FirstOrDefault();
        if (OwnList != null && others.Count > 0 && Better(best.Rank, best.Id, OwnList.Options.Rank, OwnList.Options.Id))
        {
            _cfg.Log($"[list] {best.Host}:{best.Port} answers, so this game stops its own list (games on it move over at their next check)");
            OwnList.Dispose();
            OwnList = null;
        }
        if (OwnList == null && others.Count == 0 && _cfg.AutoStart)
            OwnList = ListServer.TryStart(_cfg.FirstListPort, _cfg.ListPorts, 1, _cfg.Upnp, _cfg.Log, out _);
        var targets = others.Select(r => (r.Host, r.Port)).ToList();
        if (OwnList != null)
            targets.Add(("127.0.0.1", OwnList.Port));
        if (!targets.SequenceEqual(Targets))
            Targets = targets;
        if (OwnList != null)
            (ListHost, ListPort) = ("", OwnList.Port);
        else if (others.Count > 0)
            (ListHost, ListPort) = (NetAddr.KindOf(best.Ip) == NetAddr.Kind.Loopback ? "" : best.Host, best.Port);
        else
            (ListHost, ListPort) = ("", 0);
        Status = OwnList != null
            ? $"Running the server list for your friends (UDP {OwnList.Port}): it's in your invite code."
            : others.Count > 0
                ? $"Listed on {others.Count} server list(s)."
                : _cfg.AutoStart ? "No server list (could not start one): friends can still use your invite code." : "Not on a server list: friends can use your invite code.";
    }

    public void Dispose()
    {
        OwnList?.Dispose();
        OwnList = null;
    }
}

/// <summary>
/// The joining side: works out a host's server list (from an invite code, or from the host's server info) so the
/// player's game can add it to Settings > Multiplayer > Server lists.
/// </summary>
public static class ListLearner
{
    public static string? FromInvite(Invite invite) => invite.ListPort != 0 ? $"{invite.Address}:{invite.ListPort}" : null;

    /// <summary>The list in a game's server info, reached at <paramref name="address"/> ("" host = the game's own address).</summary>
    public static string? FromInfo(string address, ServerInfo info)
    {
        if (info.ListPort == 0)
            return null;
        var host = info.ListHost.Length > 0 ? info.ListHost : address;
        return host.Length == 0 ? null : $"{host}:{info.ListPort}";
    }

    /// <summary>Pings the host (in the background) and returns the list in its answer, or null.</summary>
    public static Task<string?> ProbeHostAsync(string host, ushort port, TimeSpan timeout) => Task.Run<string?>(() =>
    {
        var ip = ListProbe.Resolve(host);
        if (ip == null)
            return null;
        using var net = NetPeer.Discovery();
        net.PingHost(ip, port);
        string? learned = null;
        var got = false;
        var until = DateTime.UtcNow + timeout;
        var resent = false;
        while (!got && DateTime.UtcNow < until)
        {
            net.PollRaw((from, data) =>
            {
                if ((DefaultMessageIDTypes)data[0] == DefaultMessageIDTypes.UnconnectedPong && ServerInfo.Find(data) is { } info && info.Port == port)
                {
                    got = true;
                    learned = FromInfo(from.ToString(false, '|'), info);
                }
            });
            if (!resent && DateTime.UtcNow > until - timeout / 2)
            {
                resent = true;
                net.PingHost(ip, port);
            }
            Thread.Sleep(10);
        }
        return learned;
    });
}

/// <summary>
/// "CallOfSlop.exe --master" is a windowed (no console) app; on Windows this gives it a console window so whoever runs
/// tools\RunServerList.bat sees what the list is doing and can close it to stop it. Output still goes to serverlist.log.
/// </summary>
public static partial class MasterConsole
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    public static void Attach()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            if (!AllocConsole())
                return;
            var console = new StreamWriter(Console.OpenStandardOutput(), Encoding.ASCII) { AutoFlush = true };
            Console.SetOut(new TeeWriter(Console.Out, console));
        }
        catch (Exception)
        {
            // no console: the log file still has everything
        }
    }

    private sealed class TeeWriter(TextWriter a, TextWriter b) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            a.Write(value);
            Try(() => b.Write(value));
        }

        public override void Write(string? value)
        {
            a.Write(value);
            Try(() => b.Write(value));
        }

        public override void WriteLine(string? value)
        {
            a.WriteLine(value);
            Try(() => b.WriteLine(value));
        }

        private static void Try(Action write)
        {
            try
            {
                write();
            }
            catch (Exception)
            {
                // the console window was closed
            }
        }
    }
}
