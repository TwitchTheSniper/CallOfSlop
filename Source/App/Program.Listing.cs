using System.Collections.Concurrent;

namespace CallOfSlop;

/// <summary>
/// Playing with friends without setup (docs/net/SERVER-DISCOVERY.md): the host's addresses and invite code (UPnP, STUN),
/// the host's server lists (<see cref="HostListing"/>: register on one, or run one when none answers), the hand-started
/// "Run a server list for friends", invite links on the command line, and server lists learned when joining.
/// </summary>
public static unsafe partial class Program
{
    private static HostListing? _hostListing;
    private static ListServer? _manualList;
    private static bool _manualListWanted;
    private static string _hostAddressLine = ""; // "1.2.3.4 (internet) or 192.168.1.5 (LAN)"; the invite is added to it
    private static (string Ip, ushort Port, ushort ListPort) _inviteFor;
    private static readonly ConcurrentQueue<string> LearnedLists = new();

    /// <summary>Hosting began: work out the addresses (UPnP and STUN in the background) for the address line and invite code.</summary>
    private static void BeginHostAddressing()
    {
        var lan = UpnpMapping.GetLocalIPv4()?.ToString() ?? "this PC's IP";
        var suffix = _settings.Port == DefaultPort ? "" : $" port {_settings.Port}";
        _hostAddressLine = _joinAddress = lan + suffix + " (LAN)";
        _inviteFor = default;
        if (_settings.UseStun)
            PublicAddress.RefreshStun(); // invite codes need the public address; a few seconds, in the background
        if (_settings.UseUpnp && !_forceNoUpnp)
        {
            // Router discovery takes a few seconds; don't freeze the game while it happens.
            var port = _settings.Port;
            _hostAddressLine = _joinAddress = _hostAddressLine + " - opening router port...";
            _upnpTask = Task.Run(() =>
            {
                var m = UpnpMapping.TryCreate(port, Console.WriteLine);
                _upnp = m;
                PublicAddress.SetUpnp(m is { BehindSecondNat: false } ? m.ExternalIp : null);
                _hostAddressLine = m is { ExternalIp: { } ext, BehindSecondNat: false }
                    ? $"{ext}{suffix} (internet) or {lan}{suffix} (LAN)"
                    : m is { BehindSecondNat: true }
                        ? $"{lan}{suffix} (LAN only - your connection is behind a second NAT)"
                        : $"{lan}{suffix} (LAN only - UPnP unavailable, forward UDP {port} for internet play)";
                _joinAddress = _hostAddressLine;
                _inviteFor = default; // rebuild the line with the invite
            });
        }
    }

    /// <summary>The host's game exists: find (or start) a server list for it.</summary>
    private static void StartHostListing(Game game)
    {
        StopHostListing();
        if (!_settings.ShareOnLists)
            return;
        _hostListing = new HostListing(() => _settings.AllMasters, new HostListing.Config
        {
            AutoStart = _settings.AutoListServer,
            Upnp = _settings.UseUpnp && !_forceNoUpnp,
        });
        Console.WriteLine($"[host] sharing '{game.ServerName}' on server lists ({_settings.AllMasters.Count} known; auto list {(_settings.AutoListServer ? "on" : "off")})");
    }

    private static void StopHostListing()
    {
        _hostListing?.Dispose(); // stops a list this game was running for the group (its other games drop out and re-probe)
        _hostListing = null;
        Ui.HostInvite = "";
        Ui.ListStatus = "";
    }

    /// <summary>Every frame: server lists, the invite code, and lists learned from hosts.</summary>
    private static void UpdateListing(double now)
    {
        // "Run a server list for friends" (Settings > Multiplayer): started and stopped with the tick box.
        if (_settings.RunListServer != _manualListWanted)
        {
            _manualListWanted = _settings.RunListServer;
            _manualList?.Dispose();
            _manualList = null;
            if (_manualListWanted)
            {
                _manualList = ListServer.TryStart(MasterServerApp.DefaultPort, 10, 0, _settings.UseUpnp && !_forceNoUpnp, Console.WriteLine, out var error);
                if (_settings.UseStun)
                    PublicAddress.RefreshStun();
                Ui.ManualListError = error;
            }
            else
                Ui.ManualListError = "";
        }
        Ui.ManualListPort = _manualList?.Port ?? 0;
        Ui.ManualListGames = _manualList?.GameCount ?? 0;

        while (LearnedLists.TryDequeue(out var learned))
            if (_settings.LearnMaster(learned))
            {
                Console.WriteLine($"[list] learned server list {learned} from the host");
                _settings.Save();
            }

        if (_session is not { IsServer: true, Net.IsOffline: false } game)
            return;
        if (_hostListing is { } listing)
        {
            listing.Tick(now);
            game.AdvertiseTo = listing.Targets;
            game.ListHost = listing.ListHost;
            game.ListPort = listing.ListPort;
            Ui.ListStatus = listing.Status;
        }
        // The invite: the public address when known (router or STUN), else the LAN one; plus the list on this PC.
        var ip = PublicAddress.Ip ?? UpnpMapping.GetLocalIPv4Cached();
        var listPort = game.ListHost.Length == 0 ? game.ListPort : (ushort)0;
        if (ip != null && (ip, game.GamePort, listPort) != _inviteFor && System.Net.IPAddress.TryParse(ip, out var addr))
        {
            _inviteFor = (ip, game.GamePort, listPort);
            var code = InviteCode.Encode(new Invite(addr, game.GamePort, ServerInfo.ProtocolVersion, listPort));
            Ui.HostInvite = code;
            Ui.InviteIsLan = NetAddr.IsLocal(ip);
            var warn = PublicAddress.Mismatch ? " - carrier-grade NAT: friends outside can't reach you directly" : "";
            _joinAddress = $"{_hostAddressLine}   Invite: {code}{(Ui.InviteIsLan ? " (LAN)" : "")}{warn}";
            Console.WriteLine($"[invite] {InviteCode.Link(code)} ({ip}:{game.GamePort}{(listPort != 0 ? $", list {listPort}" : "")})");
        }
    }

    /// <summary>
    /// Joining: learn the host's server list in the background (from its server info), so the group converges on one
    /// list. <paramref name="invite"/>: an invite already says where the list is.
    /// </summary>
    private static void LearnListFromHost(string host, ushort port, Invite? invite = null)
    {
        if (invite is { } inv && ListLearner.FromInvite(inv) is { } fromInvite)
            LearnedLists.Enqueue(fromInvite);
        _ = ListLearner.ProbeHostAsync(host, port, TimeSpan.FromSeconds(2)).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result is { } list)
                LearnedLists.Enqueue(list);
        }, TaskScheduler.Default);
    }

    /// <summary>A code or callofslop:// link from the command line (a clicked invite link): join, or say what's wrong.</summary>
    private static void JoinFromInvite(string text)
    {
        if (!InviteCode.TryParseJoin(text, _settings.Port, out var host, out var port, out var invite, out var error))
        {
            Console.WriteLine($"[invite] {error}");
            _lobbyMessage = error;
            return;
        }
        _settings.JoinAddress = host;
        _settings.Port = port;
        Console.WriteLine($"[invite] joining {host}:{port} from an invite");
        StartSession(LobbyAction.Join, invite);
    }
}
