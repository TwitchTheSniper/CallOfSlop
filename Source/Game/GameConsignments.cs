namespace CallOfSlop;

/// <summary>
/// Consignments (economy step 8; Karl, 2026-11-22: logistics "like packet routing", put on "a nearby bigger ship" or paid onto another
/// hauler's freighter). A station short of a good that no hauler route brings it straight from a seller asks for a consignment: goods
/// taken out of another station's stock (its own power's, or a friendly one's, paid at its price), routed over the hauler routes
/// (Crew/Stations.json "haulers", both ways) by the fewest hops, a route of another power costing more for the space it sells. A
/// consignment waits at a station, off its stock, until a hauler on the next hop's route calls; it rides aboard to the next station and
/// is routed again from there, so a route gone quiet is gone round. Space on another power's hauler is paid from the owner's books by
/// the goods' value, the hop and how full that ship already is (Karl, 2026-11-22: "distance, risk and demand"). A consignment aboard a
/// hauler that is lost is lost with it. The goods aren't in the hauler's bins (they ride as a note), so a raid takes the ship, not the
/// load. Host only; kept in the markets section of the main world (GameStationSave.cs).
/// </summary>
public sealed partial class Game
{
    private struct Consignment
    {
        public byte Owner, Material;
        public int Units, From, To, At; // stations by index in StationMarkets.All
        public ushort Ship; // aboard this hauler (0: waiting at At)
        public int Next; // the station it is on its way to aboard Ship
        public long Since; // when it was made (ticks)
        public long Waits; // when it last got off at a station (ticks)
        public int Route; // the hauler route it rides (with Ship)
    }

    private const int MaxConsignments = 32, ConsignMost = 2000, ConsignLeast = 50, ConsignWaitMost = 2 * 60 * 60 * World.TickRate; // (a consignment waiting longer than two hours of play goes back into its station's stock)
    private const float ConsignFee = 0.05f, ConsignForeign = 2f; // the space's price, a share of the goods' value a hop; another power's route counts this many hops
    private readonly List<Consignment> _consigns = new(MaxConsignments);
    private int[] _routeDist = [], _routeVia = [];

    /// <summary>Host, once a minute (the needs step): new consignments for shortages no route answers straight, and those aboard a lost hauler lost.</summary>
    private void ConsignTick()
    {
        for (var i = _consigns.Count - 1; i >= 0; i--)
        {
            var cn = _consigns[i];
            if (cn.Ship != 0 && HaulerLost(cn))
            {
                Log($"[consign] {cn.Units} {RecyclingTable.Current.NameOf(cn.Material)} for {StationMarkets.All[cn.To].Name} were lost with hauler {cn.Ship}");
                _consigns.RemoveAt(i);
            }
            else if (cn.Ship == 0 && ServerTick - cn.Waits > ConsignWaitMost)
            {
                var back = ConsignStock(cn.At, cn.Material, cn.Units);
                Log($"[consign] {cn.Units} {RecyclingTable.Current.NameOf(cn.Material)} for {StationMarkets.All[cn.To].Name} waited too long at {StationMarkets.All[cn.At].Name}: {back} go back into its stock{(back < cn.Units ? ", the rest is lost (it is full or doesn't trade it)" : "")}");
                _consigns.RemoveAt(i);
            }
        }
        var all = StationMarkets.All;
        for (var d = 0; d < all.Length && _consigns.Count < MaxConsignments; d++)
        {
            if (_marketByDef.Length <= d || _marketByDef[d] is not { } m || !StationLife.Trades(all[d]))
                continue;
            var owner = CrewPowers.Find(all[d].Power);
            if (owner == 0)
                continue;
            for (var k = 0; k < all[d].Goods.Length && _consigns.Count < MaxConsignments; k++)
            {
                var g = all[d].Goods[k];
                if (!m.Short(k) || ConsignOpen(d, g.Material) || RouteBrings(d, g.Material))
                    continue;
                ConsignMake(owner, d, k);
            }
        }
    }

    /// <summary>Is a consignment of <paramref name="mat"/> to station <paramref name="to"/> open?</summary>
    private bool ConsignOpen(int to, byte mat)
    {
        foreach (var c in _consigns)
            if (c.To == to && c.Material == mat)
                return true;
        return false;
    }

    /// <summary>Does a hauler route already bring <paramref name="mat"/> to station <paramref name="to"/> straight from a station that sells it (its goods or its needs)?</summary>
    private static bool RouteBrings(int to, byte mat)
    {
        foreach (var h in StationMarkets.Haulers)
        {
            if (h.Count <= 0)
                continue; // (a route stopped brings nothing)
            if (h.To == to && (Array.IndexOf(h.Goods, mat) >= 0 || h.Needs && Sells(h.From, mat)))
                return true;
            if (h.From == to && Array.IndexOf(h.Back, mat) >= 0)
                return true;
        }
        return false;
    }

    private static bool Sells(int station, byte mat)
    {
        foreach (var g in StationMarkets.All[station].Goods)
            if (g.Material == mat && g.Sell > 0)
                return true;
        return false;
    }

    /// <summary>
    /// Is the hauler a consignment rides lost? Only where its route's haulers are (they were taken up in this world): a landing leaves them in
    /// the world behind, and a body of the same id elsewhere is not it. Lost: the route's haulers are here and it is not one of them, or a wreck.
    /// </summary>
    private bool HaulerLost(in Consignment cn)
    {
        if (_haulAdopt)
            return false; // (the haulers of a world just loaded aren't taken up yet)
        var routeHere = false;
        foreach (var r in _hauls)
        {
            if (r.Route != cn.Route)
                continue;
            if (r.Ship == cn.Ship)
                return Constructs.Get(cn.Ship) is not { Wreck: false };
            routeHere = true;
        }
        return routeHere;
    }

    /// <summary>Host: <paramref name="units"/> of <paramref name="mat"/> into station <paramref name="def"/>'s stock, up to its most. Returns what went in.</summary>
    private int ConsignStock(int def, byte mat, int units)
    {
        if (_marketByDef.Length <= def || _marketByDef[def] is not { } m)
            return 0;
        var goods = StationMarkets.All[def].Goods;
        for (var k = 0; k < goods.Length && k < m.Stock.Length; k++)
            if (goods[k].Material == mat)
            {
                var put = Math.Clamp(goods[k].Most - m.Stock[k], 0, units);
                m.Stock[k] += put;
                if (put > 0 && m.Body != 0)
                    StationsVersion++;
                return put;
            }
        return 0;
    }

    /// <summary>A consignment of station <paramref name="to"/>'s short good <paramref name="k"/> from the nearest station with some to spare that it can be routed from.</summary>
    private void ConsignMake(byte owner, int to, int k)
    {
        var all = StationMarkets.All;
        var mat = all[to].Goods[k].Material;
        RouteFrom(to, owner); // (distances to the short station: the routes run both ways)
        int best = -1, bestK = -1, bestDist = int.MaxValue;
        for (var d = 0; d < all.Length; d++)
        {
            if (d == to || _routeDist[d] == int.MaxValue || _marketByDef.Length <= d || _marketByDef[d] is not { } m || !StationLife.Trades(all[d]))
                continue;
            var seller = CrewPowers.Find(all[d].Power);
            if (seller == 0 || seller != owner && _rel.Get(owner, seller) < 0)
                continue;
            for (var j = 0; j < all[d].Goods.Length; j++)
            {
                var g = all[d].Goods[j];
                if (g.Material != mat || g.Sell <= 0 || m.Stock[j] - Keeps(g) < ConsignLeast)
                    continue;
                if (_routeDist[d] < bestDist)
                    (best, bestK, bestDist) = (d, j, _routeDist[d]);
            }
        }
        if (best < 0)
            return;
        var src = _marketByDef[best]!;
        var want = all[to].Goods[k].Target - _marketByDef[to]!.Stock[k];
        var units = Math.Min(Math.Min(want, src.Stock[bestK] - Keeps(all[best].Goods[bestK])), ConsignMost);
        if (units < ConsignLeast)
            return;
        var sellerPower = CrewPowers.Find(all[best].Power);
        if (sellerPower != owner)
        {
            var cost = src.SellCost(bestK, units);
            var pay = Controllers.Powers[owner];
            if (pay == null || pay.Stock < cost)
                return;
            pay.Stock -= cost;
            pay.Spent += cost;
            if (Controllers.Powers[sellerPower] is { } sp)
            {
                sp.Stock += cost;
                sp.Earned += cost;
            }
        }
        src.Stock[bestK] -= units;
        if (src.Body != 0)
            StationsVersion++;
        _consigns.Add(new Consignment { Owner = owner, Material = mat, Units = units, From = best, To = to, At = best, Since = ServerTick, Waits = ServerTick });
        Log($"[consign] {CrewPowers.Of(owner)?.Name}: {units} {RecyclingTable.Current.NameOf(mat)} from {all[best].Name} to {all[to].Name}, route cost {bestDist} (2 a hop on its own routes, {2 * ConsignForeign:0} on another power's)");
    }

    /// <summary>What a station keeps of a good it gives to a consignment: its want of one it uses, a quarter of it (its price's anchor) of one it only sells.</summary>
    private static int Keeps(StationGood g) => g.Use > 0 ? g.Target : g.Target / 4;

    /// <summary>
    /// The routes' distances to station <paramref name="target"/> (<see cref="_routeDist"/>, by station; <see cref="_routeVia"/>: the next
    /// station on the way there), a hop on <paramref name="owner"/>'s own route counting 2 and another power's <see cref="ConsignForeign"/> times
    /// that. A few stations and routes: worked out only when a consignment is made or moves, never a tick.
    /// </summary>
    private void RouteFrom(int target, byte owner)
    {
        var all = StationMarkets.All;
        var n = all.Length;
        if (_routeDist.Length < n)
            (_routeDist, _routeVia) = (new int[n], new int[n]); // (grown when stations are made in play: GameStationLife.cs)
        for (var i = 0; i < n; i++)
            (_routeDist[i], _routeVia[i]) = (int.MaxValue, -1);
        _routeDist[target] = 0;
        // (Bellman-Ford over the routes: at most a few dozen edges)
        for (var pass = 0; pass < n; pass++)
        {
            var changed = false;
            foreach (var h in StationMarkets.Haulers)
            {
                if (h.Count <= 0)
                    continue;
                var w = CrewPowers.Find(h.Power) == owner ? 2 : (int)(2 * ConsignForeign);
                changed |= Relax(h.From, h.To, w) | Relax(h.To, h.From, w);
            }
            if (!changed)
                break;
        }

        bool Relax(int a, int b, int w)
        {
            if (a >= n || b >= n || _routeDist[b] == int.MaxValue || _routeDist[b] + w >= _routeDist[a])
                return false;
            _routeDist[a] = _routeDist[b] + w;
            _routeVia[a] = b;
            return true;
        }
    }

    /// <summary>Host, a hauler at station <paramref name="def"/> (HaulTrade): consignments aboard it get off here, and those here routed on to <paramref name="next"/> get on.</summary>
    private void ConsignAtStation(Construct c, int route, StationHaulerDef h, int def, int next)
    {
        if (_consigns.Count == 0)
            return;
        var all = StationMarkets.All;
        for (var i = _consigns.Count - 1; i >= 0; i--)
        {
            var cn = _consigns[i];
            if (cn.Ship != c.Id || cn.Route != route || cn.Next != def)
                continue;
            cn.Ship = 0;
            cn.At = def;
            cn.Waits = ServerTick;
            if (def == cn.To)
            {
                var put = ConsignStock(def, cn.Material, cn.Units);
                Log($"[consign] {cn.Units} {RecyclingTable.Current.NameOf(cn.Material)} from {all[cn.From].Name} delivered to {all[def].Name} after {(ServerTick - cn.Since) / (60.0 * World.TickRate):0} min{(put < cn.Units ? $" ({cn.Units - put} lost: its stock is full)" : "")}");
                _consigns.RemoveAt(i);
                continue;
            }
            _consigns[i] = cn;
            Log($"[consign] {cn.Units} {RecyclingTable.Current.NameOf(cn.Material)} for {all[cn.To].Name} wait at {all[def].Name} for the next hop");
        }
        var aboard = 0;
        foreach (var cn in _consigns)
            aboard += cn.Ship == c.Id && cn.Route == route ? 1 : 0;
        var operatorPower = CrewPowers.Find(h.Power);
        for (var i = 0; i < _consigns.Count; i++)
        {
            var cn = _consigns[i];
            if (cn.Ship != 0 || cn.At != def)
                continue;
            RouteFrom(cn.To, cn.Owner); // (routed again at each hop: a route gone quiet is gone round)
            if (_routeVia[def] != next)
                continue;
            if (operatorPower != cn.Owner)
            {
                // (space on another power's hauler: the goods' value, the hop, and how full that ship is already)
                var fee = TradeValues.Of(cn.Material) * cn.Units * ConsignFee * (1 + 0.25 * aboard);
                if (Controllers.Powers[cn.Owner] is not { } payer || payer.Stock < fee)
                    continue;
                payer.Stock -= fee;
                payer.Spent += fee;
                if (Controllers.Powers[operatorPower] is { } op)
                {
                    op.Stock += fee;
                    op.Earned += fee;
                }
            }
            cn.Ship = c.Id;
            cn.Route = route;
            cn.Next = next;
            _consigns[i] = cn;
            aboard++;
            Log($"[consign] {cn.Units} {RecyclingTable.Current.NameOf(cn.Material)} for {all[cn.To].Name} go aboard {h.Power} hauler {c.Id} at {all[def].Name}, for {all[next].Name}");
        }
    }

    /// <summary>Host, saving: the open consignments (after the moorings in the markets section; an older reader stops before them).</summary>
    private void ConsignWrite(SaveWriter w)
    {
        w.I32(_consigns.Count);
        foreach (var cn in _consigns)
            w.Str(CrewPowers.Of(cn.Owner)?.Name ?? "").Str(RecyclingTable.Current.NameOf(cn.Material)).I32(cn.Units)
                .Str(StationMarkets.All[cn.From].Name).Str(StationMarkets.All[cn.To].Name).Str(StationMarkets.All[cn.At].Name).U16(cn.Ship)
                .Str(cn.Ship != 0 ? StationMarkets.All[cn.Next].Name : "").I32(cn.Route);
    }

    /// <summary>Host, loading: the saved consignments, by name; one whose power, good or stations are gone is dropped with a log line.</summary>
    private void ConsignRead(SaveReader s)
    {
        _consigns.Clear();
        if (!s.Has(4))
            return;
        var n = s.Count(16);
        for (var i = 0; i < n; i++)
        {
            string owner = s.Str(), mat = s.Str();
            var units = s.I32();
            string from = s.Str(), to = s.Str(), at = s.Str();
            var ship = s.U16();
            var next = s.Str();
            var route = s.I32();
            var cn = new Consignment { Owner = CrewPowers.Find(owner), Material = RecyclingTable.Current.IdOf(mat), Units = units, From = StationMarkets.Find(from),
                To = StationMarkets.Find(to), At = StationMarkets.Find(at), Ship = ship, Next = ship != 0 ? StationMarkets.Find(next) : 0, Since = ServerTick, Waits = ServerTick, Route = route };
            if (cn.Owner == 0 || cn.Material == 0 || units <= 0 || cn.From < 0 || cn.To < 0 || cn.At < 0 || cn.Next < 0 || ship != 0 && (route < 0 || route >= StationMarkets.Haulers.Length) || _consigns.Count >= MaxConsignments)
            {
                Log($"[consign] a saved consignment of {units} {mat} for {to} is dropped: its power, good or stations are gone");
                continue;
            }
            _consigns.Add(cn);
        }
    }
}
