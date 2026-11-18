using System.Text.Json;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>A station's market as data (Crew/Stations.json, "call-of-slop-stations/1"): its name, the power holding it and its goods.</summary>
public sealed class StationDef
{
    public string Name = "", Power = "", Planet = "";
    public StationGood[] Goods = [];
    public float Orbit; // its height over its planet (m): where a hauler meets it when its world isn't the one being played (0: unknown)
    public string Blueprint = ""; // built in its planet's world the first time that world is played without it (empty: a ship start builds it)
}

/// <summary>A good a station trades: what it pays and asks a unit (credits), what it starts with and holds at most.</summary>
public readonly record struct StationGood(byte Material, float Buy, float Sell, int Stock, int Most);

/// <summary>A hauler route (Crew/Stations.json "haulers"): a power's ships buying goods at one station and selling them at another, and back.</summary>
public sealed class StationHaulerDef
{
    public string Power = "", Ship = "";
    public int From, To, Count = 1; // (stations by index in StationMarkets.All)
    public byte[] Goods = [], Back = []; // materials loaded at From, and at To for the run home
    public float Wait = 30; // seconds moored at each end
}

/// <summary>The stations' markets (Crew/Stations.json). A bad file is refused with a reason in the log, and no station has a market.</summary>
public static class StationMarkets
{
    public const string Format = "call-of-slop-stations/1";
    public const int MaxGoods = 32;

    private static StationDef[]? _all;
    private static float _margin = 0.25f;

    public static StationDef[] All => _all ??= Load(Path.Combine(AppFolders.Data, "Crew", "Stations.json"));

    private static StationHaulerDef[] _haulers = [];

    /// <summary>The hauler routes (empty when the file was refused).</summary>
    public static StationHaulerDef[] Haulers { get { _ = All; return _haulers; } }

    /// <summary>The share of what a station pays that its power counts in its stock.</summary>
    public static float Margin { get { _ = All; return _margin; } }

    /// <summary>The market of a station by its name (-1: none).</summary>
    public static int Find(string name)
    {
        var all = All;
        for (var i = 0; i < all.Length; i++)
            if (all[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    public static StationDef[] Load(string path)
    {
        if (!File.Exists(path))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = doc.RootElement;
            if (!root.TryGetProperty("format", out var f) || f.GetString() != Format)
                throw new FormatException($"\"format\" is not \"{Format}\"");
            if (root.TryGetProperty("margin", out var m))
                _margin = Math.Clamp(m.GetSingle(), 0, 1);
            var list = new List<StationDef>();
            foreach (var s in root.GetProperty("stations").EnumerateArray())
            {
                var d = new StationDef
                {
                    Name = s.GetProperty("name").GetString() ?? "",
                    Power = s.TryGetProperty("power", out var pw) ? pw.GetString() ?? "" : "",
                    Planet = s.TryGetProperty("planet", out var pl) ? pl.GetString() ?? "" : "",
                    Orbit = Math.Clamp(s.TryGetProperty("orbit", out var ob) ? ob.GetSingle() * 1000f : 0f, 0f, 150_000f),
                    Blueprint = s.TryGetProperty("blueprint", out var bpn) ? bpn.GetString() ?? "" : "",
                };
                if (d.Blueprint.Length > 0 && (d.Orbit < 5000 || d.Planet.Length == 0))
                    throw new FormatException($"{d.Name}: a station built in its world needs a \"planet\" and an \"orbit\" of at least 5 km");
                if (d.Name.Length == 0 || d.Name.Any(ch => ch > 127))
                    throw new FormatException("a station needs an ASCII \"name\"");
                var goods = new List<StationGood>();
                foreach (var g in s.GetProperty("goods").EnumerateArray())
                {
                    var good = g.GetProperty("good").GetString() ?? "";
                    var mat = RecyclingTable.Current.IdOf(good);
                    if (mat == 0)
                        throw new FormatException($"{d.Name}: no material named \"{good}\" (Constructs/Recycling.json)");
                    if (goods.Any(x => x.Material == mat))
                        throw new FormatException($"{d.Name}: \"{good}\" is listed twice");
                    var most = Math.Clamp(g.TryGetProperty("most", out var mo) ? mo.GetInt32() : 100000, 0, 10_000_000);
                    goods.Add(new StationGood(mat, Math.Clamp(g.GetProperty("buy").GetSingle(), 0, 10000), Math.Clamp(g.GetProperty("sell").GetSingle(), 0, 10000),
                        Math.Clamp(g.TryGetProperty("stock", out var st) ? st.GetInt32() : 0, 0, most), most));
                }
                if (goods.Count > MaxGoods)
                    throw new FormatException($"{d.Name}: at most {MaxGoods} goods");
                d.Goods = [.. goods];
                list.Add(d);
            }
            if (list.Count > 255)
                throw new FormatException("at most 255 stations");
            var haulers = new List<StationHaulerDef>();
            if (root.TryGetProperty("haulers", out var hs))
                foreach (var h in hs.EnumerateArray())
                {
                    int At(string key)
                    {
                        var n = h.GetProperty(key).GetString() ?? "";
                        var i = list.FindIndex(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                        return i >= 0 ? i : throw new FormatException($"haulers: no station named \"{n}\" for \"{key}\"");
                    }
                    byte[] Mats(string key, StationDef at) => h.TryGetProperty(key, out var gs) ? [.. gs.EnumerateArray().Select(g =>
                    {
                        var mat = RecyclingTable.Current.IdOf(g.GetString() ?? "");
                        return mat != 0 && at.Goods.Any(x => x.Material == mat && x.Sell > 0) ? mat
                            : throw new FormatException($"haulers: {at.Name} sells no \"{g.GetString()}\" (\"{key}\")");
                    })] : [];
                    var d = new StationHaulerDef
                    {
                        Power = h.GetProperty("power").GetString() ?? "",
                        Ship = h.GetProperty("ship").GetString() ?? "",
                        From = At("from"),
                        To = At("to"),
                        Count = Math.Clamp(h.TryGetProperty("count", out var cn) ? cn.GetInt32() : 1, 0, 8),
                        Wait = Math.Clamp(h.TryGetProperty("wait", out var wt) ? wt.GetSingle() : 30f, 5f, 600f),
                    };
                    if (d.From == d.To)
                        throw new FormatException("haulers: \"from\" and \"to\" are the same station");
                    d.Goods = Mats("goods", list[d.From]);
                    d.Back = Mats("back", list[d.To]);
                    haulers.Add(d);
                }
            _haulers = [.. haulers];
            Console.WriteLine($"[stations] {list.Count} station market(s) and {_haulers.Length} hauler route(s) from {path}");
            return [.. list];
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            Console.WriteLine($"[stations] REFUSED {path}: {ex.Message}");
            return [];
        }
    }
}

/// <summary>
/// Stations' markets in play (docs/sandbox/constructs/station.md "Markets"). The host keeps each station's stock of its goods
/// (<see cref="StationMarket"/>), buys a docked ship's ore for credits (<see cref="FactionOp.StationSell"/>: someone aboard the moored ship asks),
/// and tells everyone the markets and who is moored where (<see cref="Msg.Stations"/>) when they change, at most once a second, and each joiner.
/// </summary>
public sealed partial class Game
{
    /// <summary>A station's market as this peer has it: its def (StationMarkets.All), its stock by good, and the ships moored at its berths.</summary>
    public sealed class StationMarket
    {
        public ushort Body;
        public int Def;
        public int[] Stock = [];
        public readonly List<(ushort Ship, ushort Part)> Moored = new(16);
    }

    /// <summary>The markets by station body (on a client, the host's last word).</summary>
    public readonly Dictionary<ushort, StationMarket> StationMarketsHere = new(8);

    /// <summary>Bumped when a market or a mooring changes (the host sends; the view reads).</summary>
    public int StationsVersion;
    private int _stationsSent = -1;
    private long _stationsSentAt;

    /// <summary>Host: every station's market by its def (StationMarkets.All), kept across worlds: a landing keeps them, a new game starts them again.</summary>
    private StationMarket?[] _marketByDef = [];

    /// <summary>Host: the market of station def <paramref name="def"/>, made from its data the first time (its stock as the data starts it).</summary>
    private StationMarket MarketOfDef(int def)
    {
        if (_marketByDef.Length != StationMarkets.All.Length)
            Array.Resize(ref _marketByDef, StationMarkets.All.Length);
        return _marketByDef[def] ??= new StationMarket { Def = def, Stock = [.. StationMarkets.All[def].Goods.Select(g => g.Stock)] };
    }

    /// <summary>Host: the market of station <paramref name="st"/> (null: it has none, or another station of its name holds it here).</summary>
    public StationMarket? StationMarketOf(Construct st)
    {
        if (StationMarketsHere.TryGetValue(st.Id, out var m))
            return m;
        if (!IsServer || !st.IsStation)
            return null;
        var def = StationMarkets.Find(StationName(st));
        if (def < 0)
            return null;
        m = MarketOfDef(def);
        if (m.Body != 0 && m.Body != st.Id && Constructs.Get(m.Body) is { Wreck: false, IsStation: true })
            return null; // (two stations of one name: the first keeps the market)
        m.Body = st.Id;
        StationMarketsHere[st.Id] = m;
        StationsVersion++;
        return m;
    }

    /// <summary>Both sides, a new world (OpeningReset): the markets leave this world's bodies; a new game (not a landing) starts them and the haulers again.</summary>
    private void StationsReset()
    {
        StationMarketsHere.Clear();
        var travel = _landing != null; // (a landing keeps the markets: GameSpaceLanding.cs)
        foreach (var m in _marketByDef)
            if (m != null)
            {
                m.Body = 0;
                m.Moored.Clear();
            }
        if (!travel)
            _marketByDef = [];
        _hauls.Clear(); // (the haulers here went with the world left behind: new ones set out, StationHaulTick)
        _haulNext = [];
        _stationsBuilt = false;
        _stationsBuildAt = ServerTick + 5 * World.TickRate;
        StationsVersion++;
    }

    /// <summary>The power holding a station's market (0: none).</summary>
    public static byte StationPower(StationMarket m) => m.Def >= 0 && m.Def < StationMarkets.All.Length ? CrewPowers.Find(StationMarkets.All[m.Def].Power) : (byte)0;

    /// <summary>Host, once a second from the moor step: the moorings into the markets, and the markets to everyone when they changed.</summary>
    private void StationMarketTick()
    {
        foreach (var c in Constructs.Bodies)
            if (c.IsStation && !c.Wreck)
                StationMarketOf(c);
        foreach (var m in StationMarketsHere.Values)
        {
            var was = m.Moored.Count;
            var hash = 0;
            foreach (var (ship, part) in m.Moored)
                hash = hash * 31 + ship * 7 + part;
            m.Moored.Clear();
            var now = 0;
            foreach (var c in Constructs.Bodies)
                if (c.MooredTo == m.Body)
                {
                    m.Moored.Add((c.Id, c.MoorPart));
                    now = now * 31 + c.Id * 7 + c.MoorPart;
                }
            if (now != hash || was != m.Moored.Count)
                StationsVersion++;
        }
        if (StationsVersion != _stationsSent && ServerTick - _stationsSentAt >= World.TickRate && !Net.IsOffline)
        {
            _stationsSent = StationsVersion;
            _stationsSentAt = ServerTick;
            WriteStations(SbW);
            Net.Broadcast(SbW.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary><see cref="Msg.Stations"/>: u8 n, each (u16 body, u8 def, u8 goods, each i32 stock, u8 moored, each (u16 ship, u16 berth)).</summary>
    private void WriteStations(PacketWriter w)
    {
        w.Begin(Msg.Stations).U8((byte)Math.Min(255, StationMarketsHere.Count));
        var n = 0;
        foreach (var m in StationMarketsHere.Values)
        {
            if (++n > 255)
                break;
            w.U16(m.Body).U8((byte)m.Def).U8((byte)m.Stock.Length);
            foreach (var s in m.Stock)
                w.U32((uint)s);
            w.U8((byte)Math.Min(255, m.Moored.Count));
            for (var k = 0; k < m.Moored.Count && k < 255; k++)
                w.U16(m.Moored[k].Ship).U16(m.Moored[k].Part);
        }
    }

    /// <summary>Server: the markets to one joiner.</summary>
    private void SendStationsTo(RakNetGUID to)
    {
        if (StationMarketsHere.Count == 0)
            return;
        WriteStations(SbW);
        Net.SendTo(to, SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: the markets and moorings (after the id). Moorings are set on this peer's constructs, so its berth counts and cards read them.</summary>
    internal void ReadStations(ref PacketReader r)
    {
        if (IsServer || !r.Has(1))
            return;
        foreach (var c in Constructs.Bodies)
            c.MooredTo = 0;
        StationMarketsHere.Clear();
        int n = r.U8();
        for (var i = 0; i < n && r.Has(4); i++)
        {
            var m = new StationMarket { Body = r.U16(), Def = r.U8() };
            int goods = r.U8();
            m.Stock = new int[goods];
            for (var k = 0; k < goods && r.Has(4); k++)
                m.Stock[k] = (int)r.U32();
            if (!r.Has(1))
                break;
            int moored = r.U8();
            for (var k = 0; k < moored && r.Has(4); k++)
            {
                var (ship, part) = (r.U16(), r.U16());
                m.Moored.Add((ship, part));
                if (Constructs.Get(ship) is { } c)
                    (c.MooredTo, c.MoorPart) = (m.Body, part);
            }
            if (m.Def < StationMarkets.All.Length && StationMarkets.All[m.Def].Goods.Length == m.Stock.Length)
            {
                StationMarketsHere[m.Body] = m; // (a market this peer's data doesn't match is left out)
                if (Constructs.Get(m.Body) is { } sb)
                    sb.StationTitle = StationMarkets.All[m.Def].Name; // (its name, as the host's: a second Highport goes by its own)
            }
        }
        StationsVersion++;
    }

    /// <summary>Is <paramref name="p"/> aboard <paramref name="ship"/> (standing on it or seated in it)?</summary>
    public bool StationAboard(Player p, Construct ship)
    {
        if (!p.Alive)
            return false;
        if (p.Sim.RideBody == ship.Id)
            return true;
        ref var h = ref SandboxHands.Hand(p.Id);
        return h.Seated && h.SeatBody == ship.Id;
    }

    /// <summary>
    /// Any peer: what the station would pay for what <paramref name="ship"/>'s bins hold of the goods it buys (credits, and units), up to the
    /// room it has for each. The host's sale and the card's preview both count it here.
    /// </summary>
    public double StationOffer(StationMarket m, Construct ship, out int units)
    {
        units = 0;
        if (m.Def >= StationMarkets.All.Length)
            return 0;
        var goods = StationMarkets.All[m.Def].Goods;
        Span<int> room = stackalloc int[goods.Length];
        for (var k = 0; k < goods.Length; k++)
            room[k] = goods[k].Buy > 0 ? Math.Max(0, goods[k].Most - m.Stock[k]) : 0;
        double value = 0;
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (!ship.DefOf(i).IsBin || Constructs.StoreOf(ship, i) is not { Amount: > 0 } st)
                continue;
            for (var k = 0; k < goods.Length; k++)
                if (goods[k].Material == st.Material)
                {
                    var take = Math.Min(st.Amount, room[k]);
                    room[k] -= take;
                    units += take;
                    value += take * (double)goods[k].Buy;
                    break;
                }
        }
        return value;
    }

    /// <summary>
    /// Host: the station buys what <paramref name="ship"/>'s bins hold of the goods it buys, up to the room it has for each; <paramref name="sold"/>
    /// gets the units by good. Returns what it pays (credits); the power holding it counts its margin on that as earned.
    /// </summary>
    private double StationTakeCargo(StationMarket m, Construct ship, Span<int> sold)
    {
        var goods = StationMarkets.All[m.Def].Goods;
        double value = 0;
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (!ship.DefOf(i).IsBin || Constructs.StoreOf(ship, i) is not { Amount: > 0 } store)
                continue;
            for (var k = 0; k < goods.Length; k++)
                if (goods[k].Material == store.Material && goods[k].Buy > 0)
                {
                    var take = Constructs.Withdraw(ship, i, Math.Min(store.Amount, Math.Max(0, goods[k].Most - m.Stock[k])));
                    m.Stock[k] += take;
                    sold[k] += take;
                    value += take * (double)goods[k].Buy;
                    break;
                }
        }
        StationMargin(m, value);
        return value;
    }

    /// <summary>
    /// Host: up to <paramref name="most"/> units of good <paramref name="k"/> from the station's stock into <paramref name="ship"/>'s bins (those that
    /// take it, an empty one or one holding it already). Returns the units loaded; they cost its asking price each.
    /// </summary>
    private int StationLoadCargo(StationMarket m, Construct ship, int k, int most)
    {
        var good = StationMarkets.All[m.Def].Goods[k];
        var left = Math.Min(most, m.Stock[k]);
        var put = 0;
        for (var pass = 0; pass < 2 && left > 0; pass++) // (bins already holding it first, then empty ones)
            for (var i = 0; i < ship.PartCount && left > 0; i++)
            {
                if (!ship.DefOf(i).IsBin || Constructs.StoreOf(ship, i) is not { } store || (pass == 0) != (store.Amount > 0))
                    continue;
                var n = Constructs.Deposit(ship, i, left, good.Material);
                left -= n;
                put += n;
            }
        m.Stock[k] -= put;
        if (put > 0)
        {
            StationMargin(m, put * (double)good.Sell);
            StationsVersion++;
        }
        return put;
    }

    /// <summary>Host: the station's power counts its margin on a trade of <paramref name="value"/> credits (its stock and its books).</summary>
    private void StationMargin(StationMarket m, double value)
    {
        if (value <= 0 || StationPower(m) is not (> 0 and var power) || Controllers.Powers[power] is not { Fallen: false } pw)
            return;
        var share = value * StationMarkets.Margin;
        pw.Stock += share; // (its trade on it: the books keep it as earned)
        pw.Earned += share;
    }

    private static string StationGoodsText(StationMarket m, ReadOnlySpan<int> units)
    {
        var goods = StationMarkets.All[m.Def].Goods;
        var what = new System.Text.StringBuilder();
        for (var k = 0; k < goods.Length && k < units.Length; k++)
            if (units[k] > 0)
                what.Append(what.Length > 0 ? ", " : "").Append(units[k].ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(RecyclingTable.Current.NameOf(goods[k].Material));
        return what.ToString();
    }

    /// <summary>Server: why <paramref name="p"/> can't trade from moored ship <paramref name="shipId"/> (null: they can), with its station's market.</summary>
    private string? StationTradeCheck(Player p, ushort shipId, out Construct ship, out Construct st, out StationMarket m)
    {
        ship = st = null!;
        m = null!;
        if (Constructs.Get(shipId) is not { Wreck: false } s)
            return "that ship is gone";
        ship = s;
        if (s.MooredTo == 0 || Constructs.Get(s.MooredTo) is not { } station || StationMarketOf(station) is not { } market)
            return "dock at a station's berth first";
        (st, m) = (station, market);
        if (!StationAboard(p, s))
            return "you need to be aboard the docked ship to trade";
        var power = StationPower(m);
        if (power != 0 && CrewStanding(power, p.Id) < 0)
            return $"{CrewPowers.Of(power)?.Name ?? "they"} won't trade with you";
        return null;
    }

    /// <summary>Server: <paramref name="p"/> sells the ore and goods aboard moored ship <paramref name="shipId"/> to its station. Null: sold; else why not.</summary>
    private string? StationSellAct(Player p, ushort shipId)
    {
        if (StationTradeCheck(p, shipId, out var ship, out var st, out var m) is { } why)
            return why;
        var name = StationName(st);
        Span<int> sold = stackalloc int[StationMarkets.All[m.Def].Goods.Length];
        var value = StationTakeCargo(m, ship, sold);
        var total = 0;
        foreach (var s in sold)
            total += s;
        if (total == 0)
            return $"nothing aboard is wanted at {name} (or it holds all it can of what you carry)";
        var credits = (long)Math.Floor(value);
        CreditsGive(p.Id, credits);
        StationsVersion++;
        var what = StationGoodsText(m, sold);
        SandboxHands.Notices.Add((p.Id, $"Sold {what} to {name} for {credits} credits."));
        Log($"[station] {p.Name} sold {what} from construct {ship.Id} to {name} for {credits} credits");
        return null;
    }

    /// <summary>
    /// Server: <paramref name="p"/> buys good <paramref name="k"/> of moored ship <paramref name="shipId"/>'s station: as much as its stock, the ship's
    /// bins and their credits allow (Karl, 2026-11-18: the market shows "whatever goods are there to trade"). Null: bought; else why not.
    /// </summary>
    private string? StationBuyAct(Player p, ushort shipId, int k)
    {
        if (StationTradeCheck(p, shipId, out var ship, out var st, out var m) is { } why)
            return why;
        var goods = StationMarkets.All[m.Def].Goods;
        var name = StationName(st);
        if (k >= goods.Length || goods[k].Sell <= 0)
            return $"{name} doesn't sell that";
        if (m.Stock[k] <= 0)
            return $"{name} has none left";
        var price = (double)goods[k].Sell;
        var afford = SandboxInfiniteParts ? int.MaxValue : (int)Math.Min(int.MaxValue, Math.Floor(CreditsOf(p.Id) / price));
        if (afford <= 0)
            return $"you can't afford a unit ({price:0.00} credits)";
        var units = StationLoadCargo(m, ship, k, afford);
        var good = RecyclingTable.Current.NameOf(goods[k].Material);
        if (units == 0)
            return $"no bin aboard has room for {good}";
        var cost = (long)Math.Ceiling(units * price);
        if (CreditsPay(p.Id, cost) is { } cant) // (it can't fail: afford was counted from the same balance)
            return cant;
        SandboxHands.Notices.Add((p.Id, $"Bought {units:N0} {good} from {name} for {cost} credits."));
        Log($"[station] {p.Name} bought {units} {good} into construct {ship.Id} from {name} for {cost} credits");
        return null;
    }
}
