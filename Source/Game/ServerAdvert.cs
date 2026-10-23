namespace CallOfSlop;

/// <summary>
/// Hosting side of the server browser: keeps RakNet's offline ping response up to date (so LAN
/// broadcasts and browser pings see the name, map and player count), and, when advertising is on,
/// sends the master server a heartbeat every 20 seconds from the game socket.
/// </summary>
public sealed partial class Game
{
    /// <summary>Shown in server browsers. Set by Program when hosting.</summary>
    public string ServerName = "";

    /// <summary>The UDP port this game listens on (what joiners connect to).</summary>
    public ushort GamePort;

    /// <summary>Master server to advertise to (null: don't advertise; LAN browsers still see the game).</summary>
    public (string Host, ushort Port)? AdvertiseTo;

    private double _nextInfoUpdate, _nextHeartbeat;

    private void TickServerAdvert(double now)
    {
        if (!IsServer || Net.IsOffline)
            return;
        if (now >= _nextInfoUpdate)
        {
            _nextInfoUpdate = now + 1;
            var info = new ServerInfo(ServerName, World.Map.Name, (byte)Players.Count, (byte)MapMaxPlayers, (byte)Players.Values.Count(p => p.Bot != null),
                Phase, GamePort, ServerInfo.ProtocolVersion, Mode, World.PhysicsMode, TrueRandom ? MapPack.TrueRandomName : "",
                BuildInfo.IsRelease ? BuildInfo.Version : ""); // the release, for "your build is older" in browsers (BuildInfo.cs)
            Net.SetOfflineInfo(info.Encode());
        }
        if (AdvertiseTo is { } m && now >= _nextHeartbeat)
        {
            _nextHeartbeat = now + 20;
            Net.Advertise(m.Host, m.Port, MasterServerApp.Heartbeat(GamePort));
        }
    }
}
