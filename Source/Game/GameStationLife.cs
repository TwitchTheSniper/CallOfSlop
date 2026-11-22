using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Stations come and go (economy step 11, design doc section 8; Karl, 2026-11-22: "stations are dynamic: powers invest where needed, pay build
/// and upkeep; underused ones are sold, else decommissioned with an NPC dismantling job returning materials"; "you see it go up piece by
/// piece"). On the host, an hour of play at a time:
/// <list type="bullet">
/// <item><b>Running.</b> Every open station takes its upkeep (Crew/Stations.json "upkeep": goods an hour from its own stock, "labour" from its
/// power's number) and keeps a ledger: what it earned (its power's margin on its trade, its shoppers, the value its making adds, the hauling a
/// station over its planet saves its sites' goods) against its upkeep, over the last <see cref="StationLife.Window"/> hours.</item>
/// <item><b>Selling.</b> A station whose ledger has been under nothing for <see cref="StationLife.SaleAfter"/> hours goes up for sale. A power
/// that holds its planet (or a key site on it) and can pay buys it, for <see cref="StationLife.SalePrice"/> of its bill's value.</item>
/// <item><b>Closing.</b> One nobody buys in <see cref="StationLife.CloseAfter"/> hours closes: its stock goes to its power's other station with
/// the most room, its routes stop, and it is left open for salvage (a person may cut it apart and keep what they take). After
/// <see cref="StationLife.SalvageFor"/> hours the Salvagers take what is left: the construct goes, and its bill's goods, less their share,
/// go back to the power.</item>
/// <item><b>Building.</b> Each power scores the planets it holds that have no station of its: the kind that suits the key sites it holds
/// there (Crew/Stations.json "kinds"), the gain it expects an hour (the kind's "expect" and the hauling its sites' goods would save) over
/// <see cref="StationLife.Payback"/> hours against the bill (the kind's blueprint's materials, <see cref="ControllerRules.GoodsOf"/>, and
/// its labour). Where the gain wins, it lays a site: a market that buys only the bill's goods, and builder routes from its stations that
/// sell them. The site goes up step by step as the goods arrive (in its planet's world a real construct, its blueprint's steps fitted in
/// order), and opens as its kind once the bill is in: its goods, its upkeep, and a supply route to its power's nearest station.</item>
/// </list>
/// A station made in play is saved with the markets (GameStationSave.cs) and sent to clients with them (<see cref="Msg.Stations"/>).
/// </summary>
public sealed partial class Game
{
    /// <summary>A site in this world going up: its construct and each fitted step's part.</summary>
    private sealed class SiteBuild
    {
        public ushort Body;
        public ConstructBuild.Blueprint Bp = null!;
        public int[] PartOf = [];
        public int Fitted;
    }

    private readonly Dictionary<int, SiteBuild> _siteBuilds = new();
    private int _lifeMinutes;
    private bool _hostOfStations; // this game is a host that counts in StationMarkets.HostGames (Dispose takes it off)
    private readonly HashSet<string> _investWeighed = new();
    private readonly Dictionary<string, ((byte Material, float Units)[] Goods, double Value)> _kindBills = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Host: a station earned <paramref name="value"/> for its power (its ledger's gain).</summary>
    private void StationGain(StationMarket m, double value)
    {
        if (value > 0 && double.IsFinite(value))
            m.Gain += value;
    }

    /// <summary>A kind's bill: its blueprint's materials, scaled by <see cref="StationLife.BillScale"/>, and their value. Made once a kind.</summary>
    private ((byte Material, float Units)[] Goods, double Value) KindBill(StationKind k)
    {
        if (_kindBills.TryGetValue(k.Name, out var had))
            return had;
        (byte, float)[] goods = [];
        double value = 0;
        if (ConstructBuild.FindBlueprint(k.Blueprint) is { } bp)
        {
            goods = [.. Controllers.Rules.GoodsOf(bp).Select(g => (g.Material, MathF.Ceiling(g.Units * StationLife.BillScale)))];
            foreach (var (mat, units) in goods)
                value += units * TradeValues.Of(mat);
        }
        _kindBills[k.Name] = (goods, value);
        return (goods, value);
    }

    /// <summary>Host, once a minute from the needs step: sites' progress by the goods they hold; once an hour, upkeep, the ledgers, sales, closing and building.</summary>
    private void StationLifeMinute()
    {
        var all = StationMarkets.All;
        for (var d = 0; d < all.Length; d++)
            if (all[d].State == MarketState.Building)
                SiteProgress(d);
        if (++_lifeMinutes < 60)
            return;
        _lifeMinutes = 0;
        StationLifeHour();
    }

    /// <summary>Host: how much of its bill a site holds (the least share of any line), as steps of its blueprint; open once it is all in and built.</summary>
    private void SiteProgress(int d)
    {
        var def = StationMarkets.All[d];
        var m = MarketOfDef(d);
        var share = 1.0;
        for (var k = 0; k < def.Goods.Length && k < m.Stock.Length; k++)
            share = Math.Min(share, m.Stock[k] / (double)Math.Max(1, def.Goods[k].Most));
        var target = (int)Math.Floor(share * def.Steps);
        if (!_siteBuilds.ContainsKey(d) && target > def.Built)
        {
            def.Built = target; // (out of sight: the steps are numbers; in its world they are fitted a few a second, StationSiteTick)
            StationsVersion++;
        }
        if (share >= 1 && def.Built >= def.Steps)
            SiteOpen(d);
    }

    /// <summary>Host: a site with its whole bill in and every step built opens as its kind.</summary>
    private void SiteOpen(int d)
    {
        var def = StationMarkets.All[d];
        if (def.Kind < 0 || def.Kind >= StationLife.Kinds.Length)
            return;
        var kind = StationLife.Kinds[def.Kind];
        def.Goods = kind.Goods;
        (def.BaseMost, def.Storage, def.Berths) = ([], 0, 0); // (its site's bill lines go: it grows from its kind's goods)
        def.Upkeep = kind.Upkeep;
        def.Labour = kind.Labour;
        def.State = MarketState.Open;
        var old = MarketOfDef(d);
        var m = _marketByDef[d] = new StationMarket { Def = d, Body = old.Body, Stock = [.. kind.Goods.Select(g => g.Stock)], HoldHours = StationLife.HoldAfter };
        m.Moored.AddRange(old.Moored);
        if (old.Body != 0)
            StationMarketsHere[old.Body] = m;
        if (_siteBuilds.Remove(d, out var sb) && Constructs.Get(sb.Body) is { } c)
        {
            var run = new ConstructBuild.BuildRun();
            ConstructBuild.FinishBuild(Constructs, c, sb.Bp, sb.PartOf, run);
            c.Rebuild();
            c.Wake();
        }
        var power = CrewPowers.Find(def.Power);
        for (var r = 0; r < StationMarkets.Haulers.Length; r++)
            if (StationMarkets.Haulers[r] is { Builder: true } h && h.To == d)
                h.Count = 0; // (its builders' work is done: they go off the route at their next station, HaulTrade)
        // Its supply route: to its power's station that sells most of what the route carries.
        var from = -1;
        var best = -1L;
        for (var o = 0; o < StationMarkets.All.Length; o++)
        {
            var od = StationMarkets.All[o];
            if (o == d || !StationLife.Trades(od) || CrewPowers.Find(od.Power) != power)
                continue;
            long have = 0;
            var om = MarketOfDef(o);
            for (var k = 0; k < od.Goods.Length; k++)
                if (od.Goods[k].Sell > 0 && Array.IndexOf(kind.RouteGoods, od.Goods[k].Material) >= 0)
                    have += om.Stock[k] + 1;
            if (have > best)
                (best, from) = (have, o);
        }
        var ship = kind.RouteShip.Length > 0 ? kind.RouteShip : StationLife.BuilderShip;
        if (from >= 0 && kind.RouteCount > 0 && ship.Length > 0)
        {
            var fd = StationMarkets.All[from];
            var route = new StationHaulerDef
            {
                Power = def.Power, Ship = ship, From = from, To = d, Count = kind.RouteCount, ListedCount = kind.RouteCount, Needs = true,
                Goods = [.. kind.RouteGoods.Where(g => fd.Goods.Any(x => x.Material == g && x.Sell > 0))],
                Back = kind.RouteBack,
            };
            if (StationMarkets.Haulers.Length < sbyte.MaxValue)
            {
                StationMarkets.AddRoute(route);
                Log($"[station] {def.Name}: its supply route from {fd.Name} opens ({route.Count} {ship})");
            }
        }
        StationsVersion++;
        Log($"[station] {def.Name} ({kind.Name}, {def.Power}) is built and opens round {def.Planet}");
    }

    /// <summary>Host, an hour of play: each station's upkeep and ledger, then sales, closing and salvage, then each power's building.</summary>
    private void StationLifeHour()
    {
        var all = StationMarkets.All;
        for (var d = 0; d < all.Length; d++)
        {
            var def = all[d];
            var m = MarketOfDef(d);
            if (def.State == MarketState.Building)
                continue;
            if (def.State == MarketState.Closing)
            {
                if (++m.SaleHours >= StationLife.CloseAfter + StationLife.SalvageFor)
                    StationSalvaged(d);
                continue;
            }
            if (def.State == MarketState.Closed)
                continue;
            // Upkeep: its goods from its own stock (what it hasn't got wears it: counted twice), its labour from its power's number.
            double cost = 0;
            foreach (var (mat, units) in def.Upkeep)
            {
                var k = Array.FindIndex(def.Goods, g => g.Material == mat);
                if (k < 0)
                    continue;
                var need = (int)MathF.Ceiling(units);
                var took = Math.Min(need, m.Stock[k]);
                m.Stock[k] -= took;
                cost += TradeValues.Of(mat) * (took + 2.0 * (need - took));
                if (took > 0 && m.Body != 0)
                    StationsVersion++;
            }
            var power = CrewPowers.Find(def.Power);
            if (def.Labour > 0 && power > 0 && Controllers.Powers[power] is { Fallen: false } pw)
            {
                pw.Stock -= def.Labour;
                pw.Spent += def.Labour;
                cost += def.Labour;
            }
            if (m.Net.Length != StationLife.Window)
                (m.Net, m.NetN) = (new double[StationLife.Window], 0);
            m.Net[m.NetN++ % m.Net.Length] = m.Gain - cost;
            m.Gain = 0;
            m.LastCost = cost;
            var full = m.NetN >= m.Net.Length;
            var sum = 0.0;
            foreach (var v in m.Net)
                sum += v;
            m.Ledger = sum;
            m.CostSum = m.CostSum * (m.Net.Length - 1) / m.Net.Length + cost; // (about the window's upkeep)
            if (m.HoldHours > 0)
                m.HoldHours--;
            m.LoseHours = full && m.HoldHours == 0 && sum < -StationLife.SaleBelow * m.CostSum ? m.LoseHours + 1 : 0;
            StationGrowHour(d, def, m);
            if (def.Owner.Length > 0)
            {
                if (!FactionStands(def.Owner))
                    StationAbandoned(d, def, m);
                continue;
            } // (a player faction's: they keep it or leave it, its ledger only shows)
            if (def.State == MarketState.Open && m.LoseHours >= StationLife.SaleAfter && (def.Kind >= 0 || StationLife.SaleListed))
            {
                def.State = MarketState.ForSale;
                m.SaleHours = 0;
                StationsVersion++;
                Log(FormattableString.Invariant($"[station] {def.Name} ({def.Power}) goes up for sale: its ledger is {sum:0} over {m.Net.Length} hours"));
            }
            else if (def.State == MarketState.ForSale)
            {
                if (m.LoseHours == 0)
                {
                    def.State = MarketState.Open;
                    StationsVersion++;
                    Log($"[station] {def.Name} ({def.Power}) pays its way again: it is off the market");
                }
                else if (!StationSold(d) && ++m.SaleHours >= StationLife.CloseAfter)
                    StationClose(d);
            }
        }
        StationInvest();
    }

    /// <summary>
    /// Host, an hour of play for an open station: it grows with demand (Karl, 2026-11-22: "more berths when docking is refused, more storage
    /// when it keeps maxing out"). Full (a good it buys at its most) or turning ships away (no berth fitted or free) for
    /// <see cref="StationLife.GrowAfter"/> hours of the last stretch, it pays a growth's bill (its power's stations; a player faction's: its own
    /// stock) and grows: each good's most by <see cref="StationLife.StorageStep"/>, or a standoff berth (a ship turned away trades from beside it).
    /// </summary>
    private void StationGrowHour(int d, StationDef def, StationMarket m)
    {
        var fullNow = false;
        for (var k = 0; k < def.Goods.Length && k < m.Stock.Length; k++)
            fullNow |= def.Goods[k].Buy > 0 && m.Stock[k] >= def.Goods[k].Most;
        m.FullHours = fullNow ? m.FullHours + 1 : Math.Max(0, m.FullHours - 1);
        m.RefusedHours = m.Refused > 0 ? m.RefusedHours + 1 : Math.Max(0, m.RefusedHours - 1);
        var refused = m.Refused;
        m.Refused = 0;
        if (m.FullHours >= StationLife.GrowAfter && def.Storage < StationLife.MaxStorage && StationGrowPay(d, StationLife.StorageBill, "storage"))
        {
            def.Storage++;
            StationLife.ApplyStorage(def);
            m.FullHours = 0;
            StationsVersion++;
            Log(FormattableString.Invariant($"[station] {def.Name} grows its storage (growth {def.Storage}): it was full {StationLife.GrowAfter} hours; each good now holds {1 + StationLife.StorageStep * def.Storage:0.00} times its first"));
        }
        if (m.RefusedHours >= StationLife.GrowAfter && def.Berths < StationLife.MaxBerths && StationGrowPay(d, StationLife.BerthBill, "a berth"))
        {
            def.Berths++;
            m.RefusedHours = 0;
            StationsVersion++;
            Log($"[station] {def.Name} grows a standoff berth ({def.Berths} now): it turned ships away {StationLife.GrowAfter} hours ({refused} in the last)");
        }
    }

    /// <summary>Host: a growth's bill paid for station <paramref name="d"/> (its power's stations, else its own stock). Paid?</summary>
    private bool StationGrowPay(int d, (byte Material, float Units)[] bill, string what)
    {
        var def = StationMarkets.All[d];
        var power = CrewPowers.Find(def.Power);
        if (bill.Length == 0)
            return true;
        if (power > 0)
        {
            if (((IPowerHost)this).TakeGoods(power, bill, 1, out var shortOf))
                return true;
            if (_investWeighed.Add($"grow:{d}:{what}"))
                Log($"[station] {def.Name} would grow {what} but its power's stations are short of {RecyclingTable.Current.NameOf(shortOf)}");
            return false;
        }
        var m = MarketOfDef(d);
        foreach (var (mat, units) in bill)
            if (Array.FindIndex(def.Goods, g => g.Material == mat) is var k && (k < 0 || m.Stock[k] < (int)MathF.Ceiling(units)))
            {
                if (_investWeighed.Add($"grow:{d}:{what}"))
                    Log($"[station] {def.Name} would grow {what} but holds too little {RecyclingTable.Current.NameOf(mat)}");
                return false;
            }
        foreach (var (mat, units) in bill)
            m.Stock[Array.FindIndex(def.Goods, g => g.Material == mat)] -= (int)MathF.Ceiling(units);
        return true;
    }

    /// <summary>Host: a ship found no berth at station body <paramref name="st"/> (its Dock order refused): counted toward its growing a berth.</summary>
    private void StationRefused(Construct st)
    {
        if (IsServer && StationMarketsHere.TryGetValue(st.Id, out var m))
            m.Refused++;
    }

    /// <summary>Host: has station <paramref name="def"/> a standoff berth free for a hauler turned away (its grown berths, less the haulers trading from them now)?</summary>
    private bool StandoffFree(int def)
    {
        var used = 0;
        foreach (var r in _hauls)
            if (r.Stage == HaulStage.Orbiting && (r.AtTo ? StationMarkets.Haulers[r.Route].To : StationMarkets.Haulers[r.Route].From) == def)
                used++;
        return StationMarkets.All[def].Berths > used;
    }

    /// <summary>Host: a station for sale bought by a power that holds its planet (or a key site on it) and can pay. Did one?</summary>
    private bool StationSold(int d)
    {
        var def = StationMarkets.All[d];
        var seller = CrewPowers.Find(def.Power);
        var price = SalePrice(def);
        var c = Controllers;
        var pl = c.Territory.Planets.FindIndex(x => x.Equals(def.Planet, StringComparison.OrdinalIgnoreCase));
        byte buyer = 0;
        var most = double.MinValue;
        for (byte p = 1; p <= PowerControllers.N; p++)
        {
            if (p == seller || c.Powers[p] is not { Fallen: false } pw || pw.Stock < price || CrewPowers.Of(p) is null)
                continue;
            var holds = pl >= 0 && pl < c.PlanetCount && c.PlanetHolder[pl] == p;
            for (var k = 0; k < c.SiteCount && !holds; k++)
                holds = c.SiteHolder[k] == p && c.Territory.Sites[k].Planet.Equals(def.Planet, StringComparison.OrdinalIgnoreCase);
            if (holds && pw.Stock > most)
                (most, buyer) = (pw.Stock, p);
        }
        if (buyer == 0)
            return false;
        var bp = c.Powers[buyer];
        bp.Stock -= price;
        bp.Spent += price;
        if (seller > 0 && c.Powers[seller] is { Fallen: false } sp)
        {
            sp.Stock += price;
            sp.Earned += price;
        }
        for (var r = 0; r < StationMarkets.Haulers.Length; r++)
            if (StationMarkets.Haulers[r] is var h && (h.To == d || h.From == d) && CrewPowers.Find(h.Power) == seller)
                h.Count = 0; // (the seller's routes there stop)
        var was = def.Power;
        def.Power = CrewPowers.Of(buyer)!.Name;
        def.State = MarketState.Open;
        var m = MarketOfDef(d);
        (m.LoseHours, m.SaleHours, m.NetN, m.HoldHours) = (0, 0, 0, StationLife.HoldAfter);
        Array.Clear(m.Net);
        StationsVersion++;
        Log(FormattableString.Invariant($"[station] {def.Name} is sold by {was} to {def.Power} for {price:0} (value)"));
        return true;
    }

    /// <summary>Is <paramref name="p"/> a member of the player faction that holds this station (bought, StationPurchaseAct)?</summary>
    public bool StationMember(StationMarket m, Player p) => StationMember(m, p.Id);

    /// <summary>Whether player <paramref name="id"/> is in the player faction that holds the station.</summary>
    public bool StationMember(StationMarket m, byte id) =>
        m.Def >= 0 && m.Def < StationMarkets.All.Length && StationMarkets.All[m.Def].Owner is { Length: > 0 } owner
        && FactionTable.Of[id] is > 0 and var f && owner.Equals(FactionTable.Names[f], StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a player faction of that name still stands (one with no members left is cleared by the faction table).</summary>
    private bool FactionStands(string name)
    {
        for (var f = 1; f < FactionTable.Max; f++)
            if (FactionTable.Names[f].Length > 0 && name.Equals(FactionTable.Names[f], StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Host, an hour: a station whose player faction is gone (disbanded, or none of it came back with the world) is nobody's: the holder of its
    /// planet takes it on, or, with nobody holding the planet, it closes for salvage. Else it would stand locked for good, or go to the next
    /// faction that took the old name.
    /// </summary>
    private void StationAbandoned(int d, StationDef def, StationMarket m)
    {
        var was = def.Owner;
        def.Owner = "";
        var pl = Controllers.Territory.Planets.FindIndex(x => x.Equals(def.Planet, StringComparison.OrdinalIgnoreCase));
        var holder = pl >= 0 && pl < Controllers.PlanetCount ? Controllers.PlanetHolder[pl] : (byte)0;
        if (holder > 0 && Controllers.Powers[holder] is { Fallen: false } && CrewPowers.Of(holder)?.Name is { } name)
        {
            (def.Power, def.State, m.LoseHours, m.HoldHours) = (name, MarketState.Open, 0, StationLife.HoldAfter);
            StationsVersion++;
            Log($"[station] {def.Name}: {was} are gone, and {name}, who hold {def.Planet}, take it on");
        }
        else
        {
            Log($"[station] {def.Name}: {was} are gone, and nobody holds {def.Planet}");
            StationClose(d);
        }
    }

    /// <summary>
    /// Server: <paramref name="p"/>'s faction buys the station for sale that moored ship <paramref name="shipId"/> is at (Karl, 2026-11-22: player
    /// factions buy what powers offload, in neutral or allied space): its seller and its planet's holder must not be hostile to them, and the
    /// price comes off their tab with the seller (else in goods from the ship's bins). The station is theirs: the seller's routes stop, it pays
    /// its goods upkeep from its own stock, it is never sold or closed by the powers, and it trades with the faction's members only, for nothing.
    /// </summary>
    private string? StationPurchaseAct(Player p, ushort shipId)
    {
        if (StationTradeCheck(p, shipId, out var ship, out var st, out var m) is { } why)
            return why;
        var def = StationMarkets.All[m.Def];
        if (def.State != MarketState.ForSale)
            return $"{def.Name} isn't for sale";
        if (FactionTable.Of[p.Id] is not (> 0 and var f) || FactionTable.Names[f] is not { Length: > 0 } faction)
            return "found or join a faction first: a station is bought by a faction";
        var seller = CrewPowers.Find(def.Power);
        var pl = Controllers.Territory.Planets.FindIndex(x => x.Equals(def.Planet, StringComparison.OrdinalIgnoreCase));
        var holder = pl >= 0 && pl < Controllers.PlanetCount ? Controllers.PlanetHolder[pl] : (byte)0;
        if (holder != 0 && holder != seller && CrewStanding(holder, p.Id) < 0)
            return $"{def.Planet} is held by the {CrewPowers.Of(holder)?.Name}, who are hostile to you: buy where the space is neutral or allied";
        var price = (long)Math.Ceiling(SalePrice(def));
        if (TabOrGoods(p.Id, seller, price, ship, out var how) is { } cant)
            return cant;
        if (seller > 0 && Controllers.Powers[seller] is { Fallen: false } sp)
        {
            sp.Stock += price;
            sp.Earned += price;
        }
        for (var r = 0; r < StationMarkets.Haulers.Length; r++)
            if (StationMarkets.Haulers[r] is var h && (h.To == m.Def || h.From == m.Def))
                h.Count = 0; // (the seller's routes there stop)
        var was = def.Power;
        (def.Power, def.Owner, def.State) = ("", faction, MarketState.Open);
        (m.LoseHours, m.SaleHours, m.NetN) = (0, 0, 0);
        Array.Clear(m.Net);
        StationsVersion++;
        SandboxHands.Notices.Add((p.Id, $"{faction} bought {def.Name} from the {was} for {how}. It is yours: its stock is your faction's."));
        Log($"[station] {def.Name} is bought by the player faction {faction} ({p.Name}) from {was} for {how}");
        return null;
    }

    /// <summary>Window tool, a test state: the station moored ship <paramref name="shipId"/> is at goes up for sale now.</summary>
    internal string StationForSaleTest(ushort shipId, Player? pay)
    {
        if (Constructs.Get(shipId) is not { MooredTo: not 0 } s || Constructs.Get(s.MooredTo) is not { } st || StationMarketOf(st) is not { } m)
            return "the picked craft isn't moored at a station";
        var def = StationMarkets.All[m.Def];
        (def.State, m.SaleHours) = (MarketState.ForSale, 0);
        StationsVersion++;
        if (pay != null)
            TabGive(pay.Id, CrewPowers.Find(def.Power), (long)Math.Ceiling(SalePrice(def)));
        return FormattableString.Invariant($"{def.Name} is for sale by {def.Power} for {SalePrice(def):0}");
    }

    /// <summary>What a station sells for: a share of its kind's bill (a listed one: of its blueprint's).</summary>
    private double SalePrice(StationDef def)
    {
        if (def.Kind >= 0 && def.Kind < StationLife.Kinds.Length)
            return KindBill(StationLife.Kinds[def.Kind]).Value * StationLife.SalePrice;
        if (ConstructBuild.FindBlueprint(def.Blueprint.Length > 0 ? def.Blueprint : "Caledon Highport") is { } bp)
        {
            double v = 0;
            foreach (var (mat, units) in Controllers.Rules.GoodsOf(bp))
                v += units * TradeValues.Of(mat);
            return v * StationLife.SalePrice;
        }
        return 0;
    }

    /// <summary>Host: a station nobody bought closes: its stock goes to its power's other station with room, its routes stop, and it is left for salvage.</summary>
    private void StationClose(int d)
    {
        var def = StationMarkets.All[d];
        var m = MarketOfDef(d);
        var moved = StationStockOut(d, def.Goods.Select((g, k) => (g.Material, m.Stock[k])).ToArray());
        Array.Clear(m.Stock);
        for (var r = 0; r < StationMarkets.Haulers.Length; r++)
            if (StationMarkets.Haulers[r] is var h && (h.To == d || h.From == d))
                h.Count = 0;
        def.State = MarketState.Closing;
        StationsVersion++;
        Log($"[station] {def.Name} ({def.Power}) closes: nobody bought it; {moved} units of its stock go to its power's other stations, and it is open for salvage");
    }

    /// <summary>Host: goods into station <paramref name="d"/>'s power's other trading stations, the one with the most room for each first. Units moved.</summary>
    private long StationStockOut(int d, (byte Material, int Units)[] goods)
    {
        var power = CrewPowers.Find(StationMarkets.All[d].Power);
        long moved = 0;
        foreach (var (mat, units) in goods)
        {
            var left = units;
            for (var o = 0; o < StationMarkets.All.Length && left > 0; o++)
            {
                var od = StationMarkets.All[o];
                if (o == d || !StationLife.Trades(od) || CrewPowers.Find(od.Power) != power)
                    continue;
                var k = Array.FindIndex(od.Goods, g => g.Material == mat);
                if (k < 0)
                    continue;
                var om = MarketOfDef(o);
                var put = Math.Min(left, od.Goods[k].Most - om.Stock[k]);
                if (put <= 0)
                    continue;
                om.Stock[k] += put;
                left -= put;
                moved += put;
            }
        }
        if (moved > 0)
            StationsVersion++;
        return moved;
    }

    /// <summary>Host: the Salvagers take a closed station: its construct goes, and its bill's goods less their share go back to its power.</summary>
    private void StationSalvaged(int d)
    {
        var def = StationMarkets.All[d];
        var bill = def.Kind >= 0 && def.Kind < StationLife.Kinds.Length ? KindBill(StationLife.Kinds[def.Kind]).Goods
            : ConstructBuild.FindBlueprint(def.Blueprint.Length > 0 ? def.Blueprint : "Caledon Highport") is { } lbp ? [.. Controllers.Rules.GoodsOf(lbp)] : [];
        var back = bill.Select(g => (g.Material, (int)(g.Units * (1 - StationLife.SalvageShare)))).ToArray();
        var moved = StationStockOut(d, back);
        var body = StationBodyOf(d);
        if (body == 0)
            foreach (var c in Constructs.Bodies)
                if (c.IsStation && !c.Wreck && StationName(c).Equals(def.Name, StringComparison.OrdinalIgnoreCase))
                    body = c.Id;
        if (Constructs.Get(body) is { } sc)
        {
            StationMarketsHere.Remove(sc.Id);
            Constructs.Remove(sc);
        }
        _siteBuilds.Remove(d);
        def.State = MarketState.Closed;
        StationsVersion++;
        Log($"[station] {def.Name} is dismantled by the Salvagers: {moved} units of what it was made of go back to {def.Power}");
    }

    /// <summary>
    /// Host, an hour of play: each power with a station weighs the planets it holds that have no station of its, and lays a site where the
    /// gain it expects over the payback beats the bill; one site a power at a time, <see cref="StationLife.MaxNew"/> stations made in play at most.
    /// </summary>
    private void StationInvest()
    {
        if (StationLife.Kinds.Length == 0 || !PowersActive)
            return;
        var all = StationMarkets.All;
        var made = 0;
        foreach (var d in all)
            if (d.Kind >= 0 && d.State != MarketState.Closed)
                made++;
        if (made >= StationLife.MaxNew)
            return;
        var c = Controllers;
        for (byte p = 1; p <= PowerControllers.N; p++)
        {
            if (c.Powers[p] is not { Fallen: false } pw || CrewPowers.Of(p) is not { } pdef || !HasStation(p))
                continue;
            var building = false;
            foreach (var d in all)
                building |= d.State == MarketState.Building && CrewPowers.Find(d.Power) == p;
            if (building)
                continue;
            int bestPl = -1, bestKind = -1;
            double bestGain = 0;
            for (var pl = 0; pl < c.PlanetCount; pl++)
            {
                if (c.PlanetHolder[pl] != p)
                    continue;
                var planet = c.Territory.Planets[pl];
                var has = false;
                foreach (var d in all)
                    has |= d.State != MarketState.Closed && CrewPowers.Find(d.Power) == p && d.Planet.Equals(planet, StringComparison.OrdinalIgnoreCase);
                if (has)
                    continue;
                // The kind that suits the sites it holds there, and what its sites' goods would save in hauling an hour.
                var kind = -1;
                double save = 0;
                for (var k = 0; k < c.SiteCount; k++)
                {
                    var site = c.Territory.Sites[k];
                    if (c.SiteHolder[k] != p || !site.Planet.Equals(planet, StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var (mat, perHour) in site.Goods)
                        save += perHour * TradeValues.Of(mat) * StationLife.HaulSave;
                    var siteKind = site.Kind >= 0 && site.Kind < c.Territory.Kinds.Count ? c.Territory.Kinds[site.Kind].Name : "";
                    for (var i = 0; i < StationLife.Kinds.Length && kind < 0; i++)
                        if (StationLife.Kinds[i].For.Any(f => f.Equals(siteKind, StringComparison.OrdinalIgnoreCase)))
                            kind = i;
                }
                for (var i = 0; i < StationLife.Kinds.Length && kind < 0; i++)
                    if (StationLife.Kinds[i].For.Length == 0)
                        kind = i;
                if (kind < 0)
                    continue;
                var kd = StationLife.Kinds[kind];
                var (_, billValue) = KindBill(kd);
                var gain = (kd.Expect + save) * StationLife.Payback - billValue * (1 + StationLife.LabourShare);
                if (gain <= 0 && _investWeighed.Add($"{p}:{planet}:{kd.Name}")) // (once a game for a planet and a kind: why a power doesn't build there)
                    Log(FormattableString.Invariant($"[station] {pdef.Name} weighs building the kind {kd.Name} over {planet} and doesn't: {kd.Expect:0} expected and {save:0} saved an hour over {StationLife.Payback:0} h against a bill of {billValue:0} and labour {billValue * StationLife.LabourShare:0}"));
                if (billValue > 0 && gain > bestGain)
                    (bestGain, bestPl, bestKind) = (gain, pl, kind);
            }
            if (bestPl < 0)
                continue;
            var labour = KindBill(StationLife.Kinds[bestKind]).Value * StationLife.LabourShare;
            if (pw.Stock < labour)
            {
                if (_investWeighed.Add($"{p}:labour"))
                    Log(FormattableString.Invariant($"[station] {pdef.Name} would build over {c.Territory.Planets[bestPl]} but can't pay its labour: {labour:0} against its {pw.Stock:0}"));
                continue;
            }
            if (SiteLay(p, c.Territory.Planets[bestPl], bestKind, labour) && ++made >= StationLife.MaxNew)
                return;
        }
    }

    /// <summary>Host: power <paramref name="p"/> lays a site of kind <paramref name="kind"/> over <paramref name="planet"/>, with builder routes from its stations. Did it?</summary>
    private bool SiteLay(byte p, string planet, int kind, double labour)
    {
        var kd = StationLife.Kinds[kind];
        if (ConstructBuild.FindBlueprint(kd.Blueprint) is not { } bp)
            return false;
        var (bill, value) = KindBill(kd);
        var power = CrewPowers.Of(p)!.Name;
        // Its bill: what its power's stations sell (a good none of them sells can't be brought: it is left out, and said).
        var goods = new List<StationGood>();
        var sources = new Dictionary<int, List<byte>>();
        var left = new List<string>();
        foreach (var (mat, units) in bill)
        {
            int src = -1, srcStock = -1;
            for (var o = 0; o < StationMarkets.All.Length; o++)
            {
                var od = StationMarkets.All[o];
                if (!StationLife.Trades(od) || CrewPowers.Find(od.Power) != p)
                    continue;
                var k = Array.FindIndex(od.Goods, g => g.Material == mat && g.Sell > 0);
                if (k >= 0 && MarketOfDef(o).Stock[k] > srcStock)
                    (src, srcStock) = (o, MarketOfDef(o).Stock[k]);
            }
            if (src < 0)
            {
                left.Add(RecyclingTable.Current.NameOf(mat));
                continue;
            }
            var n = Math.Clamp((int)units, 1, 10_000_000);
            goods.Add(new StationGood(mat, TradeValues.Of(mat), 0, 0, n, 0, 0, n));
            (sources.TryGetValue(src, out var l) ? l : sources[src] = new List<byte>()).Add(mat);
        }
        if (goods.Count == 0 || sources.Count == 0)
            return false;
        var name = $"{planet} {kd.Name}";
        for (var i = 2; StationMarkets.Find(name) >= 0; i++)
            name = $"{planet} {kd.Name} {i}";
        var onPlanet = 0;
        foreach (var d in StationMarkets.All)
            if (d.Planet.Equals(planet, StringComparison.OrdinalIgnoreCase) && d.State != MarketState.Closed)
                onPlanet++;
        var def = new StationDef
        {
            Name = name, Power = power, ListedPower = power, Planet = planet, Orbit = kd.Orbit + 4000 * onPlanet, Blueprint = kd.Blueprint,
            Kind = kind, State = MarketState.Building, Steps = bp.Steps.Count, Goods = [.. goods],
        };
        var d0 = StationMarkets.Add(def);
        var pw = Controllers.Powers[p];
        pw.Stock -= labour;
        pw.Spent += labour;
        foreach (var (src, mats) in sources.Take(3))
        {
            if (StationMarkets.Haulers.Length >= sbyte.MaxValue || StationLife.BuilderShip.Length == 0)
                break;
            StationMarkets.AddRoute(new StationHaulerDef
            {
                Power = power, Ship = StationLife.BuilderShip, From = src, To = d0, Count = StationLife.Builders, ListedCount = StationLife.Builders,
                Needs = true, Builder = true, Goods = [.. mats],
            });
            Log($"[station] {name}: builders run from {StationMarkets.All[src].Name} with {string.Join(", ", mats.Select(x => RecyclingTable.Current.NameOf(x)))}");
        }
        StationsVersion++;
        Log(FormattableString.Invariant($"[station] {power} lays {name} ({kd.Name}, {def.Orbit / 1000:0} km up round {planet}): its bill is {value:0} (value: {string.Join(", ", goods.Select(g => $"{g.Most} {RecyclingTable.Current.NameOf(g.Material)}"))}), {bp.Steps.Count} steps; labour {labour:0}")
            + (left.Count > 0 ? $"; none of its stations sells {string.Join(", ", left)}, so the site goes without" : ""));
        return true;
    }

    /// <summary>
    /// Host, once a second: stations made in play over this world's planet as constructs: a site's steps fitted as its goods come (up to
    /// <see cref="SiteStepsASecond"/> a second, so it is seen going up), an open one built whole if this world hasn't got it.
    /// </summary>
    private void StationSiteTick()
    {
        if (!SandboxOn || World.Map.Planetoid is not { } pl || GravityFrames.Point is not { } planet || !StarSystems.ForMap(World.Map.Name, out var e, out var body))
            return;
        var all = StationMarkets.All;
        for (var d = StationMarkets.Listed; d < all.Length; d++)
        {
            var def = all[d];
            if (def.State is MarketState.Closed or MarketState.Closing || !def.Planet.Equals(e.System.Bodies[body].Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!_siteBuilds.TryGetValue(d, out var sb))
            {
                if (def.State == MarketState.Building && Math.Max(def.Built, SiteTarget(d)) <= 0)
                    continue; // (nothing to put up yet: an empty body isn't kept by the world, so the site's first step makes it)
                var had = StationBodyOf(d);
                foreach (var c0 in Constructs.Bodies)
                    if (had == 0 && !c0.Wreck && c0.StationTitle.Equals(def.Name, StringComparison.OrdinalIgnoreCase))
                        had = c0.Id;
                if (had != 0 && def.State != MarketState.Building)
                    continue; // (an open one this world kept)
                if (ConstructBuild.FindBlueprint(def.Blueprint) is not { } bp)
                    continue;
                if (Constructs.Get(had) is { } stale)
                {
                    StationMarketsHere.Remove(stale.Id);
                    Constructs.Remove(stale); // (a site saved part built: put up again from its steps, which are known only as they are fitted)
                }
                var up = -planet.Center;
                up = up.LengthSquared() > 1 ? Vector3.Normalize(up) : Vector3.UnitY;
                var side = Vector3.Cross(up, Vector3.UnitY);
                if (side.LengthSquared() < 1e-4f)
                    side = Vector3.Cross(up, Vector3.UnitX);
                var fwd = Vector3.Normalize(side);
                var r = (float)pl.Radius + def.Orbit;
                if (def.State != MarketState.Building)
                {
                    if (StationBuild(bp, def.Name, planet.Center, up, fwd, r, e.System.Bodies[body].Mu) is { } st)
                        Log($"[station] {def.Name} built as construct {st.Id}, {def.Orbit / 1000:0.0} km up over {World.Map.Name}'s middle, held by {def.Power}");
                    continue;
                }
                var z = -fwd;
                var x = Vector3.Cross(up, z);
                var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
                if (Constructs.Create(planet.Center + up * r, rot) is not { } c)
                    continue;
                c.Owner = 255;
                c.StationTitle = def.Name;
                c.Vel = fwd * (float)Math.Sqrt(e.System.Bodies[body].Mu / r);
                c.PrevPos = c.Pos;
                sb = _siteBuilds[d] = new SiteBuild { Body = c.Id, Bp = bp, PartOf = new int[Math.Max(1, bp.Steps.Count)] };
                Log($"[station] {def.Name}'s site is construct {c.Id}, {def.Orbit / 1000:0.0} km up over {World.Map.Name}: {def.Built} of {def.Steps} steps to put up again");
            }
            if (Constructs.Get(sb.Body) is not { Wreck: false } sc)
            {
                _siteBuilds.Remove(d); // (gone: a raid, a cut: put up again from its steps next second)
                continue;
            }
            var target = def.State == MarketState.Building ? Math.Max(def.Built, SiteTarget(d)) : def.Steps;
            var fitted = 0;
            var catchUp = sb.Fitted < def.Built; // (a site put up again: its built steps at once)
            while (sb.Fitted < target && sb.Fitted < sb.Bp.Steps.Count && (catchUp ? sb.Fitted < def.Built : fitted < SiteStepsASecond))
            {
                if (ConstructBuild.FitStep(Constructs, sc, sb.Bp, sb.Fitted, sb.PartOf) is { } why)
                {
                    Log($"[station] {def.Name}: step {sb.Fitted} doesn't fit: {why}; the site is built no further here");
                    sb.Fitted = sb.Bp.Steps.Count;
                    break;
                }
                sb.Fitted++;
                fitted++;
            }
            if (fitted > 0)
            {
                sc.Rebuild();
                sc.Wake();
                if (sb.Fitted > def.Built)
                {
                    def.Built = Math.Min(sb.Fitted, def.Steps);
                    StationsVersion++;
                }
            }
        }
    }

    /// <summary>The steps of its blueprint a site's goods have paid for.</summary>
    private int SiteTarget(int d)
    {
        var def = StationMarkets.All[d];
        var m = MarketOfDef(d);
        var share = 1.0;
        for (var k = 0; k < def.Goods.Length && k < m.Stock.Length; k++)
            share = Math.Min(share, m.Stock[k] / (double)Math.Max(1, def.Goods[k].Most));
        return (int)Math.Floor(share * def.Steps);
    }

    /// <summary>Host: hauler <paramref name="i"/>, whose route stopped, leaves it at the station it is at: the ship goes (back to its power's yard), its escorts with it.</summary>
    private void HaulRetire(int i)
    {
        var run = _hauls[i];
        var h = StationMarkets.Haulers[run.Route];
        var at = run.AtTo ? h.To : h.From;
        for (var k = 0; k < _consigns.Count; k++)
            if (_consigns[k] is { } cn && cn.Ship == run.Ship && cn.Route == run.Route)
            {
                (cn.Ship, cn.At, cn.Waits) = (0, at, ServerTick); // (what it carried for others gets off here and waits, as at a hop: GameConsignments.cs)
                _consigns[k] = cn;
            }
        for (var e = _escorts.Count - 1; e >= 0; e--)
            if (_escorts[e].Hauler == run.Ship || _escorts[e].Hauler == 0 && _escorts[e].Route == run.Route)
            {
                if (Constructs.Get(_escorts[e].Ship) is { } es)
                    Constructs.Remove(es);
                _escorts.RemoveAt(e);
            }
        if (Constructs.Get(run.Ship) is { } c)
        {
            if (c.MooredTo != 0)
                c.MooredTo = 0;
            Constructs.Remove(c);
        }
        _hauls.RemoveAt(i);
        StationsVersion++;
        Log($"[haul] construct {run.Ship} ({h.Power}) goes off its stopped route {StationMarkets.All[h.From].Name} - {StationMarkets.All[h.To].Name}{(h.Builder && StationMarkets.All[h.To].State == MarketState.Open ? ": its builders' work is done" : "")}");
    }

    /// <summary>Steps a site puts up a second in its world, at most (so it is seen going up).</summary>
    private const int SiteStepsASecond = 2;

    /// <summary>The station card's first line for a station that isn't simply open: building, for sale, closing (host-worded: StationLines).</summary>
    private string? StationLifeLine(int d)
    {
        var def = StationMarkets.All[d];
        var m = MarketOfDef(d);
        return def.State switch
        {
            _ when def.Owner.Length > 0 => m.NetN > 0 ? FormattableString.Invariant($"HELD BY {def.Owner.ToUpperInvariant()}: LEDGER {(m.Ledger >= 0 ? "+" : "")}{m.Ledger:0}") : $"HELD BY {def.Owner.ToUpperInvariant()}",
            MarketState.Building => $"BEING BUILT BY {def.Power.ToUpperInvariant()}: {def.Built} OF {def.Steps} STEPS",
            MarketState.ForSale => FormattableString.Invariant($"FOR SALE BY {def.Power.ToUpperInvariant()}: {SalePrice(def):0}"),
            MarketState.Closing => "CLOSED: OPEN FOR SALVAGE",
            MarketState.Closed => "DISMANTLED",
            _ => m.NetN > 0 ? FormattableString.Invariant($"LEDGER {(m.Ledger >= 0 ? "+" : "")}{m.Ledger:0} OVER {Math.Min(m.NetN, m.Net.Length)} H") : null,
        };
    }

    /// <summary>Both sides: a new game or a load: only the listed stations and routes, and no site going up.</summary>
    private void StationLifeReset()
    {
        if (IsServer && !_hostOfStations)
        {
            _hostOfStations = true;
            StationMarkets.HostGames++;
        }
        if (IsServer || StationMarkets.HostGames == 0)
            StationMarkets.ResetRunTime(); // (a client beside a host in one process reads the host's own list: the stations are static)
        if (_marketByDef.Length > StationMarkets.Listed)
            Array.Resize(ref _marketByDef, StationMarkets.Listed);
        _siteBuilds.Clear();
        _investWeighed.Clear();
        _lifeMinutes = 0;
    }

    // ---- Saves and the wire ---------------------------------------------------------------------------------------------------------

    /// <summary>Host, saving the markets: every station's life (state, power, ledger) and the stations and routes made in play.</summary>
    private void StationLifeWrite(SaveWriter w)
    {
        var all = StationMarkets.All;
        w.I32(all.Length);
        foreach (var def in all)
        {
            var d = Array.IndexOf(all, def);
            var m = MarketOfDef(d);
            w.Str(def.Name).Str(def.Power).U8((byte)def.State).I32(def.Kind).Str(def.Kind >= 0 && def.Kind < StationLife.Kinds.Length ? StationLife.Kinds[def.Kind].Name : "")
                .Str(def.Planet).F32(def.Orbit).I32(def.Built).I32(def.Steps).F64(m.Gain).I32(m.NetN).I32(m.LoseHours).I32(m.SaleHours).I32(m.HoldHours).F64(m.CostSum)
                .Str(def.Owner).I32(def.Storage).I32(def.Berths); // (version 3)
            w.I32(m.Net.Length);
            foreach (var v in m.Net)
                w.F64(v);
            var site = def.State == MarketState.Building;
            w.I32(site ? def.Goods.Length : 0);
            if (site)
                foreach (var g in def.Goods)
                    w.Str(RecyclingTable.Current.NameOf(g.Material)).I32(g.Most);
        }
        var hs = StationMarkets.Haulers;
        w.I32(hs.Length);
        foreach (var h in hs)
        {
            w.Str(h.Power).Str(h.Ship).Str(all[h.From].Name).Str(all[h.To].Name).I32(h.Count).I32(h.ListedCount).U8((byte)((h.Builder ? 1 : 0) | (h.Needs ? 2 : 0)));
            w.I32(h.Goods.Length);
            foreach (var g in h.Goods)
                w.Str(RecyclingTable.Current.NameOf(g));
            w.I32(h.Back.Length);
            foreach (var g in h.Back)
                w.Str(RecyclingTable.Current.NameOf(g));
        }
        w.I32(_lifeMinutes);
    }

    /// <summary>Host, loading the markets: stations made in play made again (from their kind, or a site's bill), the listed ones' state and power, the routes.</summary>
    private void StationLifeRead(SaveReader s, int version)
    {
        StationLifeReset();
        var n = s.Count(16);
        var ledgers = new List<(int Def, double Gain, int NetN, int Lose, int Sale, double[] Net, int Hold, double CostSum)>();
        for (var i = 0; i < n; i++)
        {
            var name = s.Str();
            var power = s.Str();
            var state = (MarketState)Math.Min((int)s.U8(), (int)MarketState.Closed);
            var kindAt = s.I32();
            var kindName = s.Str();
            var planet = s.Str();
            var orbit = s.F32();
            var built = s.I32();
            var steps = s.I32();
            var gain = s.F64();
            var netN = s.I32();
            var lose = s.I32();
            var sale = s.I32();
            var hold = s.I32();
            var costSum = s.F64();
            var owner = version >= 3 ? s.Str() : "";
            var storage = version >= 3 ? Math.Clamp(s.I32(), 0, StationLife.MaxStorage) : 0;
            var berths = version >= 3 ? Math.Clamp(s.I32(), 0, StationLife.MaxBerths) : 0;
            var net = new double[Math.Clamp(s.Count(8), 0, 48)];
            for (var k = 0; k < net.Length; k++)
                net[k] = s.F64();
            var lines = s.Count(8);
            var bill = new List<StationGood>();
            for (var k = 0; k < lines; k++)
            {
                var mat = RecyclingTable.Current.IdOf(s.Str());
                var most = s.I32();
                if (mat != 0)
                    bill.Add(new StationGood(mat, TradeValues.Of(mat), 0, 0, Math.Max(1, most), 0, 0, Math.Max(1, most)));
            }
            var d = StationMarkets.Find(name);
            if (d < 0 && kindAt >= 0)
            {
                var kind = StationLife.Find(kindName);
                if (kind < 0)
                {
                    Log($"[station] the saved station \"{name}\" is dropped: Crew/Stations.json has no kind \"{kindName}\"");
                    continue;
                }
                var kd = StationLife.Kinds[kind];
                d = StationMarkets.Add(new StationDef
                {
                    Name = name, Power = power, ListedPower = power, Planet = planet, Orbit = orbit, Blueprint = kd.Blueprint, Kind = kind, State = state,
                    Built = built, Steps = steps, Goods = state == MarketState.Building ? [.. bill] : kd.Goods, Upkeep = kd.Upkeep, Labour = kd.Labour,
                });
            }
            else if (d >= 0)
            {
                StationMarkets.All[d].State = state;
                if (CrewPowers.Find(power) != 0)
                    StationMarkets.All[d].Power = power;
            }
            if (d >= 0)
            {
                var sd = StationMarkets.All[d];
                (sd.Owner, sd.Storage, sd.Berths) = (owner, storage, berths);
                if (owner.Length > 0)
                    sd.Power = ""; // (a player faction's: no power holds it)
                StationLife.ApplyStorage(sd);
                ledgers.Add((d, gain, netN, lose, sale, net, hold, costSum));
            }
        }
        foreach (var (d, gain, netN, lose, sale, net, hold, costSum) in ledgers)
        {
            var m = MarketOfDef(d);
            (m.Gain, m.NetN, m.LoseHours, m.SaleHours, m.Net) = (gain, netN, lose, sale, net);
            (m.HoldHours, m.CostSum) = (Math.Clamp(hold, 0, 1000), double.IsFinite(costSum) ? Math.Max(0, costSum) : 0);
        }
        var routes = s.Count(16);
        for (var i = 0; i < routes; i++)
        {
            var power = s.Str();
            var ship = s.Str();
            var from = StationMarkets.Find(s.Str());
            var to = StationMarkets.Find(s.Str());
            var count = s.I32();
            var listedCount = s.I32();
            var flags = s.U8();
            var goods = new byte[s.Count(4)];
            for (var k = 0; k < goods.Length; k++)
                goods[k] = RecyclingTable.Current.IdOf(s.Str());
            var back = new byte[s.Count(4)];
            for (var k = 0; k < back.Length; k++)
                back[k] = RecyclingTable.Current.IdOf(s.Str());
            if (i < StationMarkets.ListedRoutes)
            {
                if (StationMarkets.Haulers[i].Power == power && StationMarkets.Haulers[i].From == from && StationMarkets.Haulers[i].To == to)
                    StationMarkets.Haulers[i].Count = Math.Clamp(count, 0, 8); // (a listed route stopped by a sale or a closing stays stopped)
                continue;
            }
            if (from < 0 || to < 0 || from == to)
            {
                Log($"[station] a saved route of {power} is dropped: one of its stations isn't there");
                StationMarkets.AddRoute(new StationHaulerDef { Power = power, Ship = ship, Count = 0, From = Math.Max(0, from), To = Math.Max(0, to) }); // (its place kept: haulers go by a route's number)
                continue;
            }
            StationMarkets.AddRoute(new StationHaulerDef
            {
                Power = power, Ship = ship, From = from, To = to, Count = Math.Clamp(count, 0, 8), ListedCount = listedCount, Builder = (flags & 1) != 0, Needs = (flags & 2) != 0,
                Goods = [.. goods.Where(g => g != 0)], Back = [.. back.Where(g => g != 0)],
            });
        }
        _lifeMinutes = Math.Clamp(s.I32(), 0, 59);
        if (StationMarkets.All.Length > StationMarkets.Listed)
            Log($"[station] {StationMarkets.All.Length - StationMarkets.Listed} station(s) made in play and {StationMarkets.Haulers.Length - StationMarkets.ListedRoutes} route(s) loaded");
    }

    /// <summary><see cref="Msg.Stations"/>' head: the stations made in play (name, power, planet, state, kind, steps, goods lines) and the listed ones' power and state.</summary>
    private void WriteStationDefs(PacketWriter w)
    {
        var all = StationMarkets.All;
        var made = Math.Min(255, all.Length - StationMarkets.Listed);
        w.U8((byte)made);
        for (var d = StationMarkets.Listed; d < StationMarkets.Listed + made; d++)
        {
            var def = all[d];
            w.Str(def.Name).Str(def.Power).Str(def.Planet).U8((byte)def.State).U8((byte)Math.Clamp(def.Kind, 0, 255)).U16((ushort)Math.Min(def.Built, ushort.MaxValue)).U16((ushort)Math.Min(def.Steps, ushort.MaxValue))
                .Str(def.Owner).U8((byte)def.Berths); // (its goods' most below has its storage growth in)
            w.U8((byte)Math.Min(def.Goods.Length, StationMarkets.MaxGoods));
            for (var k = 0; k < def.Goods.Length && k < StationMarkets.MaxGoods; k++)
            {
                var g = def.Goods[k];
                w.U8(g.Material).F32(g.Buy).F32(g.Sell).U32((uint)g.Most).U32((uint)g.Want);
            }
        }
        var listed = Math.Min(255, StationMarkets.Listed);
        w.U8((byte)listed);
        for (var d = 0; d < listed; d++)
            w.Str(all[d].Power).U8((byte)all[d].State).Str(all[d].Owner).U8((byte)all[d].Storage).U8((byte)all[d].Berths);
    }

    /// <summary>Client: the head of <see cref="Msg.Stations"/> into this peer's stations (the host's word on what was made in play).</summary>
    private void ReadStationDefs(ref PacketReader r)
    {
        if (!r.Has(1))
            return;
        int made = r.U8();
        var defs = new List<StationDef>(made);
        for (var i = 0; i < made && r.Has(2); i++)
        {
            var def = new StationDef { Name = r.Str(), Power = r.Str(), Planet = r.Str() };
            def.ListedPower = def.Power;
            def.State = (MarketState)Math.Min((int)r.U8(), (int)MarketState.Closed);
            def.Kind = r.U8();
            def.Built = r.U16();
            def.Steps = r.U16();
            def.Owner = r.Str();
            def.Berths = r.U8();
            int goods = r.U8();
            var list = new StationGood[goods];
            for (var k = 0; k < goods && r.Has(17); k++)
            {
                var mat = r.U8();
                var buy = r.F32();
                var sell = r.F32();
                var most = (int)Math.Min(r.U32(), 10_000_000u);
                var want = (int)Math.Min(r.U32(), (uint)most);
                list[k] = new StationGood(mat, buy, sell, 0, most, 0, 0, want);
            }
            def.Goods = list;
            defs.Add(def);
        }
        var powers = new List<(string Power, MarketState State, string Owner, int Storage, int Berths)>();
        if (r.Has(1))
        {
            int listed = r.U8();
            for (var i = 0; i < listed && r.Has(3); i++)
                powers.Add((r.Str(), (MarketState)Math.Min((int)r.U8(), (int)MarketState.Closed), r.Str(), Math.Min((int)r.U8(), StationLife.MaxStorage), Math.Min((int)r.U8(), StationLife.MaxBerths)));
        }
        if (StationMarkets.HostGames > 0)
            return; // (a host in this process keeps the list: a bench or a check that joins itself)
        StationMarkets.ResetRunTime();
        foreach (var def in defs)
            StationMarkets.Add(def);
        for (var d = 0; d < powers.Count && d < StationMarkets.Listed; d++)
        {
            var sd = StationMarkets.All[d];
            (sd.Power, sd.State, sd.Owner, sd.Storage, sd.Berths) = powers[d];
            StationLife.ApplyStorage(sd); // (its goods' most as the host grew them)
        }
    }
}
