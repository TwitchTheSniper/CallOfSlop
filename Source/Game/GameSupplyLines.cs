namespace CallOfSlop;

/// <summary>
/// Supply lines on the station card (economy step 10; design doc section 11: "each station's short goods, each supply line's state (running,
/// late, cut) and the convoys out"). The host words each station's lines: every hauler route that calls there, by the other end, RUNNING when
/// one of its haulers called at either end in the last <see cref="LateAfter"/>, LATE when a hauler is on the route but none called so long,
/// CUT when it had haulers here and has none now, NOT SEEN HERE when none of its haulers is in this world; then the consignments bound for
/// it, where they wait or what they ride. Shortages are marked SHORT in the market already. <see cref="Msg.Stations"/> carries the lines to
/// clients after the moorings.
/// </summary>
public sealed partial class Game
{
    public const int MaxSupplyLines = 10;
    private const long LateAfter = 60L * 60 * World.TickRate; // (an hour of play: a leg between planets takes 20 to 40 minutes, GameStationHaulers.HaulFarMinutes)
    private long[] _routeArrived = [];
    private bool[] _routeHadHauler = [];
    private readonly List<string> _linesBuf = new(MaxSupplyLines);

    /// <summary>Host: a hauler of route <paramref name="route"/> called at a station.</summary>
    private void RouteArrived(int route)
    {
        RoutesSized();
        _routeArrived[route] = ServerTick == 0 ? 1 : ServerTick;
    }

    /// <summary>Host: a hauler of route <paramref name="route"/> is out in this world (set out or taken up again).</summary>
    private void RouteSeen(int route)
    {
        RoutesSized();
        if (route < 0 || route >= _routeHadHauler.Length)
            return;
        _routeHadHauler[route] = true;
        if (_routeArrived[route] == 0)
            _routeArrived[route] = Math.Max(1, ServerTick); // (just set out: running until it is late)
        StationsVersion++;
    }

    private void RoutesSized()
    {
        var n = StationMarkets.Haulers.Length;
        if (_routeArrived.Length != n)
            (_routeArrived, _routeHadHauler) = (new long[n], new bool[n]);
    }

    /// <summary>The lines of station <paramref name="def"/> into <paramref name="into"/>: the host words them; a client reads the host's.</summary>
    internal void StationLines(int def, List<string> into)
    {
        into.Clear();
        if (!IsServer || def < 0 || def >= StationMarkets.All.Length)
            return;
        RoutesSized();
        var all = StationMarkets.All;
        var hs = StationMarkets.Haulers;
        for (var r = 0; r < hs.Length && into.Count < MaxSupplyLines; r++)
        {
            var h = hs[r];
            if (h.From != def && h.To != def)
                continue;
            var other = all[h.From == def ? h.To : h.From].Name;
            var running = false;
            foreach (var run in _hauls)
                running |= run.Route == r;
            var state = !running ? _routeHadHauler[r] ? "CUT" : "NOT SEEN HERE"
                : _routeArrived[r] != 0 && ServerTick - _routeArrived[r] < LateAfter ? "RUNNING" : "LATE";
            into.Add($"{h.Power.ToUpperInvariant()} TO {other.ToUpperInvariant()}: {state}");
        }
        foreach (var cn in _consigns)
        {
            if (cn.To != def || into.Count >= MaxSupplyLines)
                continue;
            var what = $"{cn.Units} {RecyclingTable.Current.NameOf(cn.Material).ToUpperInvariant()}";
            into.Add(cn.Ship != 0 ? $"{what} COMING, BOUND FOR {all[cn.Next].Name.ToUpperInvariant()}"
                : $"{what} COMING, WAITS AT {all[cn.At].Name.ToUpperInvariant()}");
        }
    }
}
