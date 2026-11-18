using System.Text.Json;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>A station's market as data (Crew/Stations.json, "call-of-slop-stations/1"): its name, the power holding it and its goods.</summary>
public sealed class StationDef
{
    public string Name = "", Power = "", Planet = "";
    public StationGood[] Goods = [];
}

/// <summary>A good a station trades: what it pays and asks a unit (credits), what it starts with and holds at most.</summary>
public readonly record struct StationGood(byte Material, float Buy, float Sell, int Stock, int Most);

/// <summary>The stations' markets (Crew/Stations.json). A bad file is refused with a reason in the log, and no station has a market.</summary>
public static class StationMarkets
{
    public const string Format = "call-of-slop-stations/1";
    public const int MaxGoods = 32;

    private static StationDef[]? _all;
    private static float _margin = 0.25f;

    public static StationDef[] All => _all ??= Load(Path.Combine(AppFolders.Data, "Crew", "Stations.json"));

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
                };
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
            Console.WriteLine($"[stations] {list.Count} station market(s) from {path}");
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

    /// <summary>Host: the market of station <paramref name="st"/>, made from its def the first time (null: it has none).</summary>
    public StationMarket? StationMarketOf(Construct st)
    {
        if (StationMarketsHere.TryGetValue(st.Id, out var m))
            return m;
        if (!IsServer || !st.IsStation)
            return null;
        var def = StationMarkets.Find(StationName(st));
        if (def < 0)
            return null;
        m = new StationMarket { Body = st.Id, Def = def, Stock = [.. StationMarkets.All[def].Goods.Select(g => g.Stock)] };
        StationMarketsHere[st.Id] = m;
        StationsVersion++;
        return m;
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
                StationMarketsHere[m.Body] = m; // (a market this peer's data doesn't match is left out)
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

    /// <summary>Server: <paramref name="p"/> sells the ore and goods aboard moored ship <paramref name="shipId"/> to its station. Null: sold; else why not.</summary>
    private string? StationSellAct(Player p, ushort shipId)
    {
        if (Constructs.Get(shipId) is not { Wreck: false } ship)
            return "that ship is gone";
        if (ship.MooredTo == 0 || Constructs.Get(ship.MooredTo) is not { } st || StationMarketOf(st) is not { } m)
            return "dock at a station's berth first";
        if (!StationAboard(p, ship))
            return "you need to be aboard the docked ship to sell its cargo";
        var name = StationName(st);
        var power = StationPower(m);
        if (power != 0 && CrewStanding(power, p.Id) < 0)
            return $"{CrewPowers.Of(power)?.Name ?? "they"} won't trade with you";
        var goods = StationMarkets.All[m.Def].Goods;
        Span<int> sold = stackalloc int[goods.Length];
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
        var total = 0;
        foreach (var s in sold)
            total += s;
        if (total == 0)
            return $"nothing aboard is wanted at {name} (or it holds all it can of what you carry)";
        var credits = (long)Math.Floor(value);
        CreditsGive(p.Id, credits);
        if (power != 0 && Controllers.Powers[power] is { Fallen: false } pw)
        {
            var share = value * StationMarkets.Margin;
            pw.Stock += share; // (its trade on what it bought: the books keep it as earned)
            pw.Earned += share;
        }
        StationsVersion++;
        var what = new System.Text.StringBuilder();
        for (var k = 0; k < goods.Length; k++)
            if (sold[k] > 0)
                what.Append(what.Length > 0 ? ", " : "").Append(sold[k].ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(RecyclingTable.Current.NameOf(goods[k].Material));
        SandboxHands.Notices.Add((p.Id, $"Sold {what} to {name} for {credits} credits."));
        Log($"[station] {p.Name} sold {what} from construct {ship.Id} to {name} for {credits} credits");
        return null;
    }
}
