namespace CallOfSlop;

/// <summary>
/// Hosting side of the server browser: keeps RakNet's offline ping response up to date (so LAN broadcasts and browser
/// pings see the name, map and player count, this hosting's id and the server list it's on), and sends each server list
/// in <see cref="AdvertiseTo"/> a heartbeat every 20 seconds (and at once when the lists change) from the game socket.
/// </summary>
public sealed partial class Game
{
    /// <summary>Shown in server browsers. Set by Program when hosting.</summary>
    public string ServerName = "";

    /// <summary>The UDP port this game listens on (what joiners connect to).</summary>
    public ushort GamePort;

    /// <summary>Server lists to advertise to (empty: don't; LAN browsers still see the game). Program sets it from <see cref="HostListing.Targets"/>.</summary>
    public IReadOnlyList<(string Host, ushort Port)> AdvertiseTo = [];

    /// <summary>The server list to tell players about in the server info ("" host: on this host's address; port 0: none).</summary>
    public string ListHost = "";

    public ushort ListPort;

    /// <summary>A random id for this hosting, so a browser that finds the game twice (LAN and a list) shows it once.</summary>
    public readonly uint InstanceId = (uint)Random.Shared.Next(1, int.MaxValue);

    private double _nextInfoUpdate;
    private (string, string, int, int, int, MatchPhase, ushort, GameMode, PhysicsMode, bool, string, ushort, int) _infoKey;

    /// <summary>The server info as last advertised (also the Steam lobby's data, Program.SteamNet.cs).</summary>
    public ServerInfo? LastInfo { get; private set; }

    /// <summary>What a hosted game tells the world right now: name, map, players, mode, phase, protocol, build, gravity.</summary>
    public ServerInfo CurrentInfo() =>
        new(ServerName, World.Map.Name, (byte)Players.Count, (byte)MapMaxPlayers, (byte)Players.Values.Count(p => p.Bot != null),
            Phase, GamePort, ServerInfo.ProtocolVersion, Mode, World.PhysicsMode, TrueRandom ? MapPack.TrueRandomName : "", InstanceId, ListHost, ListPort,
            BuildInfo.IsRelease ? BuildInfo.Version : "", // the release, for "your build is older" in browsers (BuildInfo.cs)
            (byte)GravityPercent); // the host's gravity setting (GameGravity.cs)
    private readonly Heartbeats _heartbeats = new();

    private void TickServerAdvert(double now)
    {
        if (!IsServer || Net.IsOffline)
            return;
        if (now >= _nextInfoUpdate)
        {
            _nextInfoUpdate = now + 1;
            // Made again only when something in it changed (a new ServerInfo and its bytes every second was the host's
            // steady per-frame garbage). Strings compare by reference: a changed name is a new string.
            var bots = 0;
            foreach (var p in Players.Values)
                if (p.Bot != null)
                    bots++;
            var key = (ServerName, World.Map.Name, Players.Count, MapMaxPlayers, bots, Phase, GamePort, Mode, World.PhysicsMode, TrueRandom, ListHost, ListPort, GravityPercent);
            if (LastInfo == null || !key.Equals(_infoKey))
            {
                _infoKey = key;
                var info = CurrentInfo();
                Net.SetOfflineInfo(info.Encode());
                LastInfo = info;
            }
        }
        _heartbeats.Tick(Net, GamePort, AdvertiseTo, now);
    }
}
