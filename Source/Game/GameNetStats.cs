using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// Connection quality for the network stats line and the perf menu (feat/perf-menu's F9 window: call
/// <see cref="ConnectionStats"/> for each row). The transport fills what it knows: RakNet the ping, Steam sockets the
/// ping, quality, rates, queues and whether the route is relayed (<see cref="NetConnectionStats"/>).
/// </summary>
public sealed partial class Game
{
    /// <summary>A client: the connection to the host. The host: the connection of player <paramref name="id"/>
    /// (false for the host itself, bots and unknown ids). False when the transport has nothing (offline).</summary>
    public bool ConnectionStats(byte id, out NetConnectionStats stats)
    {
        stats = default;
        if (Net.IsOffline)
            return false;
        if (!IsServer)
            return Net.StatsFor(Net.ServerGuid, out stats);
        foreach (var (guid, pid) in _clients.Values)
            if (pid == id)
                return Net.StatsFor(guid, out stats);
        return false;
    }

    /// <summary>The transport underneath ("raknet", "steam", "raknet+steam", "offline").</summary>
    public string TransportKind => Net.Kind;

    /// <summary>Every connected client's stats, for a host's perf menu (allocation free: fills the list given).</summary>
    public void AllConnectionStats(List<(byte Id, NetConnectionStats Stats)> into)
    {
        into.Clear();
        if (!IsServer || Net.IsOffline)
            return;
        foreach (var (guid, pid) in _clients.Values)
            if (Net.StatsFor(guid, out var s))
                into.Add((pid, s));
    }
}
