using System.Text.Json;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>A station's market as data (Crew/Stations.json, "call-of-slop-stations/1"): its name, the power holding it and its goods.</summary>
public sealed class StationDef
{
    public string Name = "", Power = "", Planet = "";
    public StationGood[] Goods = [];
    public StationPart[] Parts = []; // parts it trades (Karl, 2026-11-20: parts are physical and carry a value); their stock follows the goods' in a market
    public float Orbit; // its height over its planet (m): where a hauler meets it when its world isn't the one being played (0: unknown)
    public string Blueprint = ""; // built in its planet's world the first time that world is played without it (empty: a ship start builds it)
    public string ListedPower = ""; // the power the file gives it (a sale changes Power in play: StationMarkets.ResetRunTime puts it back)
    public MarketState State; // open, being built, for sale, closing (GameStationLife.cs)
    public int Kind = -1; // a station made in play: its kind (StationLife.Kinds); -1 a listed one
    public int Built, Steps; // being built: the blueprint's steps fitted, of all of them
    public (byte Material, float Units)[] Upkeep = []; // what an hour of running takes from its own stock (the kind's, else the file's "stationRules")
    public float Labour; // and from its power's number an hour
    public string Owner = ""; // a player faction that bought it (its name; Power is then ""): it trades with its members only, for nothing (GameStationLife.cs)
    public int Storage, Berths; // its growth (Karl, 2026-11-22: "more storage when it keeps maxing out, more berths when docking is refused")
    public int[] BaseMost = []; // each good's most before its storage grew (StationLife.ApplyStorage)
}

/// <summary>Where a station stands in its life (economy step 11; Karl, 2026-11-22: "stations are dynamic").</summary>
public enum MarketState : byte { Open, Building, ForSale, Closing, Closed }

/// <summary>
/// A good a station trades: what it pays and asks a unit at its "want" (value on a tab: Game/GameTabs.cs), what it starts with and holds at most;
/// what it uses and makes an hour of play (its needs: Karl, 2026-11-20, "incorporating needs to the world"), and the stock it wants (0: half its
/// most). Prices follow the stock against the want (<see cref="Game.StationMarket.PriceFactor"/>).
/// </summary>
public readonly record struct StationGood(byte Material, float Buy, float Sell, int Stock, int Most, float Use = 0, float Make = 0, int Want = 0)
{
    public int Target => Want > 0 ? Want : Math.Max(1, Most / 2);

    /// <summary>What a unit made takes (Karl, 2026-11-22: a station makes only from what it holds): its goods lines and how much of each ("from").</summary>
    public StationInput[] From { get; init; } = [];
}

/// <summary>An input to a station's making: one of its goods lines (by index in <see cref="StationDef.Goods"/>) and the units of it a unit made takes.</summary>
public readonly record struct StationInput(int Line, float Per);

/// <summary>
/// A part a station trades (a construct part by id): what it pays and asks for one (by default its value, <see cref="TradeValues.Part"/>, times the
/// file's "partBuy" and "partSell"), and its needs as a good's: how many it uses and makes an hour, and the stock it wants (0: half its most).
/// </summary>
public readonly record struct StationPart(ushort Part, float Buy, float Sell, int Stock, int Most, float Use = 0, float Make = 0, int Want = 0)
{
    public int Target => Want > 0 ? Want : Math.Max(1, Most / 2);

    /// <summary>What one made takes: its Assembling recipe's bulk materials (down through the parts in it), from the station's goods lines.</summary>
    public StationInput[] From { get; init; } = [];
}

/// <summary>
/// What goods and parts are worth (Karl, 2026-11-20: no currency; parts are physical and carry a value). A material's value is the file's
/// "values" (Crew/Stations.json, in the old credit prices); a part's is what its Assembling recipe takes, materials at their value and parts
/// in it at theirs, times "work" (the labour in it). A part with no recipe is worth "noRecipe". Made once a part, never in a tick.
/// </summary>
public static class TradeValues
{
    internal static readonly float[] Material = new float[SandboxInventory.MaxMaterials];
    internal static float Work = 1.25f, NoRecipe = 2f, DefaultMaterial = 0.5f;
    private static readonly Dictionary<int, float> _parts = new();

    /// <summary>A unit of material <paramref name="id"/>.</summary>
    public static float Of(byte id) => id < Material.Length && Material[id] > 0 ? Material[id] : DefaultMaterial;

    /// <summary>One part of def <paramref name="d"/>.</summary>
    public static float Part(ConstructPartDef d) => PartAt(d, 0, out _);

    /// <summary>What pressing a catalogue part takes, as units of its cheapest preset's blank material (<see cref="PressTable.CostCapOf"/>, the blank and its bay 2 kit as mass), or (null, 0) when no die presses it.</summary>
    internal static (byte? Material, float Units) PressedUnits(ConstructPartDef d)
    {
        var t = PressTable.Current;
        if (t.CostCapOf(d) is not (> 0 and var kg) || t.PresetKeyOf(d) is not { } key || Pressed.SpecOf(key) is not { } spec)
            return (null, 0);
        var mat = spec.Blank.Material;
        return (mat, kg / MathF.Max(RecyclingTable.MinKgPerUnit, RecyclingTable.Current.WeightOf(mat)));
    }

    private static float PartAt(ConstructPartDef d, int depth, out bool cut)
    {
        cut = false;
        if (_parts.TryGetValue(d.Id, out var had))
            return had;
        var v = NoRecipe;
        if (AssemblingTable.Current.Of((ushort)d.Id) is { } r)
        {
            if (depth >= 6)
            {
                cut = true; // (a chain this deep, or a loop: priced as no recipe here, and not kept, so the part's own price is whole)
                return v;
            }
            float sum = 0;
            var per = Math.Max(1, r.Count);
            foreach (var (mat, n) in r.Materials)
                sum += Of(mat) * n / per;
            foreach (var (part, n) in r.Parts)
            {
                sum += PartAt(part, depth + 1, out var c) * n / per;
                cut |= c;
            }
            v = MathF.Max(0.01f, sum * Work);
        }
        else if (PressedUnits(d) is ({ } mat, > 0 and var units)) // (a pressed part has no Assembler row: its preset's blank and kit, as units of the blank's material)
            v = MathF.Max(0.01f, Of(mat) * units * Work);
        if (!cut || depth == 0)
            _parts[d.Id] = v;
        return v;
    }
}

/// <summary>A hauler route (Crew/Stations.json "haulers"): a power's ships buying goods at one station and selling them at another, and back.</summary>
public sealed class StationHaulerDef
{
    public string Power = "", Ship = "";
    public int From, To, Count = 1; // (stations by index in StationMarkets.All)
    public byte[] Goods = [], Back = []; // materials loaded at From, and at To for the run home
    public float Wait = 30; // seconds moored at each end
    public bool Needs; // also loads any good its far station is short of that this one sells, most needed first (phase 3)
    public bool Parts; // also carries parts in its Storage Volumes: sells the station what it buys, loads what the far one is short of (a store's stock)
    public string Escort = ""; // the blueprint of its escorts (phase 3: convoys): "" none
    public int Escorts; // how many fly with each hauler
    public float EscortGap = 150; // m they hold clear of it (and three times that off a station while it docks)
    public int ListedCount; // the file's count (a route made in play: its first; a retired route's Count is 0)
    public bool Builder; // a builder's route: carries a new station's bill to it (GameStationLife.cs)
}

/// <summary>The stations' markets (Crew/Stations.json). A bad file is refused with a reason in the log, and no station has a market.</summary>
public static class StationMarkets
{
    public const string Format = "call-of-slop-stations/1";
    public const int MaxGoods = 200; // (goods and parts: one byte on the wire, Msg.Stations; a store's stock is long)

    /// <summary>The most lines a station's card lists (UI/UiCommandStation.cs): a store's long stock is on its shelves, not all on the card.</summary>
    public const int MaxListed = 32;

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

    /// <summary>
    /// A station's or a kind's "goods" (each a material it trades: buy, sell, stock, most, uses, makes, want). Making (Karl, 2026-11-22: "I want
    /// it to be inputs, so that it would need to be collected first"): a good it makes names what a unit takes ("from": material and units, each
    /// one of its own goods). Nothing is made from nothing, so a line that makes with no inputs is refused.
    /// </summary>
    private static List<StationGood> GoodsOf(JsonElement arr, string who)
    {
        var goods = new List<StationGood>();
        var froms = new List<JsonElement?>();
        foreach (var g in arr.EnumerateArray())
        {
            froms.Add(g.TryGetProperty("from", out var fr) ? fr : null);
            var good = g.GetProperty("good").GetString() ?? "";
            var mat = RecyclingTable.Current.IdOf(good);
            if (mat == 0)
                throw new FormatException($"{who}: no material named \"{good}\" (Constructs/Recycling.json)");
            if (goods.Any(x => x.Material == mat))
                throw new FormatException($"{who}: \"{good}\" is listed twice");
            var most = Math.Clamp(g.TryGetProperty("most", out var mo) ? mo.GetInt32() : 100000, 0, 10_000_000);
            goods.Add(new StationGood(mat, Math.Clamp(g.GetProperty("buy").GetSingle(), 0, 10000), Math.Clamp(g.GetProperty("sell").GetSingle(), 0, 10000),
                Math.Clamp(g.TryGetProperty("stock", out var st) ? st.GetInt32() : 0, 0, most), most,
                Math.Clamp(g.TryGetProperty("uses", out var us) ? us.GetSingle() : 0, 0, 1_000_000), Math.Clamp(g.TryGetProperty("makes", out var mk) ? mk.GetSingle() : 0, 0, 1_000_000),
                Math.Clamp(g.TryGetProperty("want", out var wa) ? wa.GetInt32() : 0, 0, most)));
        }
        for (var k = 0; k < goods.Count; k++)
        {
            if (goods[k].Make <= 0)
                continue;
            var name = RecyclingTable.Current.NameOf(goods[k].Material);
            if (froms[k] is not { ValueKind: JsonValueKind.Object } fr || !fr.EnumerateObject().Any())
                throw new FormatException($"{who}: it makes \"{name}\" from nothing (give it a \"from\": what a unit takes)");
            var ins = fr.EnumerateObject().Select(x => InputOf(goods, x.Name, Math.Clamp(x.Value.GetSingle(), 0.001f, 100_000f), who, $"\"{name}\"")).ToArray();
            if (ins.Any(x => x.Line == k))
                throw new FormatException($"{who}: \"{name}\" is made from itself");
            goods[k] = goods[k] with { From = ins };
        }
        return goods;
    }

    private static StationInput InputOf(List<StationGood> goods, string mat, float per, string who, string what)
    {
        var id = RecyclingTable.Current.IdOf(mat);
        var line = goods.FindIndex(x => id != 0 && x.Material == id);
        if (line < 0)
            throw new FormatException($"{who}: {what} is made from \"{mat}\", which the station doesn't trade (add it to its \"goods\")");
        return new StationInput(line, per);
    }

    /// <summary>What an hour's upkeep takes ({"steel": 20, ...}: units of its goods an hour).</summary>
    private static (byte Material, float Units)[] UpkeepOf(JsonElement? e, string who)
    {
        if (e is not { ValueKind: JsonValueKind.Object } o)
            return [];
        return [.. o.EnumerateObject().Select(x => RecyclingTable.Current.IdOf(x.Name) is var id and > 0 ? (id, Math.Clamp(x.Value.GetSingle(), 0f, 100_000f))
            : throw new FormatException($"{who}: upkeep names no material \"{x.Name}\""))];
    }

    /// <summary>How many stations and routes the file lists: the rest of <see cref="All"/> and <see cref="Haulers"/> are a game's own (GameStationLife.cs).</summary>
    public static int Listed { get; private set; }

    /// <summary>Host games alive in this process: while any is, a client beside it leaves the run-time stations (the host's) alone.</summary>
    public static int HostGames;
    public static int ListedRoutes { get; private set; }

    /// <summary>A station made in play (a power's new one, GameStationLife.cs) on the end of <see cref="All"/>: its index.</summary>
    public static int Add(StationDef d)
    {
        _all = [.. All, d];
        return _all.Length - 1;
    }

    /// <summary>A route made in play (a builder's or a new station's) on the end of <see cref="Haulers"/>: its index.</summary>
    public static int AddRoute(StationHaulerDef h)
    {
        _ = All;
        _haulers = [.. _haulers, h];
        return _haulers.Length - 1;
    }

    /// <summary>A new game, a load or a joiner: only the listed stations and routes, as the file has them.</summary>
    public static void ResetRunTime()
    {
        var all = All;
        if (all.Length > Listed)
            _all = all[..Listed];
        if (_haulers.Length > ListedRoutes)
            _haulers = _haulers[..ListedRoutes];
        foreach (var d in _all!)
        {
            d.Power = d.ListedPower;
            d.State = MarketState.Open;
            d.Owner = "";
            (d.Storage, d.Berths) = (0, 0);
            StationLife.ApplyStorage(d);
        }
        foreach (var h in _haulers)
            h.Count = h.ListedCount;
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
            float partBuy = 0.8f, partSell = 1.25f;
            if (root.TryGetProperty("values", out var vals))
            {
                Array.Clear(TradeValues.Material);
                foreach (var v in vals.EnumerateObject())
                {
                    if (v.Name == "work")
                        TradeValues.Work = Math.Clamp(v.Value.GetSingle(), 0.1f, 10f);
                    else if (v.Name == "noRecipe")
                        TradeValues.NoRecipe = Math.Clamp(v.Value.GetSingle(), 0.01f, 10000f);
                    else if (v.Name == "partBuy")
                        partBuy = Math.Clamp(v.Value.GetSingle(), 0f, 10f);
                    else if (v.Name == "partSell")
                        partSell = Math.Clamp(v.Value.GetSingle(), 0f, 10f);
                    else if (RecyclingTable.Current.IdOf(v.Name) is var id and > 0 && id < TradeValues.Material.Length)
                        TradeValues.Material[id] = Math.Clamp(v.Value.GetSingle(), 0f, 10000f);
                    else
                        throw new FormatException($"values: no material named \"{v.Name}\" (Constructs/Recycling.json)");
                }
            }
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
                var goods = GoodsOf(s.GetProperty("goods"), d.Name);
                var parts = new List<StationPart>();
                if (s.TryGetProperty("parts", out var ps))
                    foreach (var g in ps.EnumerateArray())
                    {
                        var pn = g.GetProperty("part").GetString() ?? "";
                        if (ConstructParts.Find(pn) is not { } pd)
                            throw new FormatException($"{d.Name}: no construct part named \"{pn}\"");
                        if (parts.Any(x => x.Part == pd.Id))
                            throw new FormatException($"{d.Name}: \"{pn}\" is listed twice");
                        var value = TradeValues.Part(pd);
                        var most = Math.Clamp(g.TryGetProperty("most", out var mo) ? mo.GetInt32() : 20, 0, 9999);
                        parts.Add(new StationPart((ushort)pd.Id, Math.Clamp(g.TryGetProperty("buy", out var pb) ? pb.GetSingle() : value * partBuy, 0, 1_000_000),
                            Math.Clamp(g.TryGetProperty("sell", out var pse) ? pse.GetSingle() : value * partSell, 0, 1_000_000),
                            Math.Clamp(g.TryGetProperty("stock", out var st) ? st.GetInt32() : 0, 0, most), most,
                            Math.Clamp(g.TryGetProperty("uses", out var us) ? us.GetSingle() : 0, 0, 100_000), Math.Clamp(g.TryGetProperty("makes", out var mk) ? mk.GetSingle() : 0, 0, 100_000),
                            Math.Clamp(g.TryGetProperty("want", out var wa) ? wa.GetInt32() : 0, 0, most)));
                    }
                if (goods.Count + parts.Count > MaxGoods)
                    throw new FormatException($"{d.Name}: at most {MaxGoods} goods and parts");
                for (var k = 0; k < parts.Count; k++)
                {
                    if (parts[k].Make <= 0)
                        continue;
                    // (a part's inputs come from the part kit's recipes, which change under the file: a part it can't make from its goods is left
                    // unmade with a log line, so a regenerated recipe never takes every station's market down with it)
                    var pd = ConstructParts.Def(parts[k].Part);
                    var sum = new Dictionary<byte, float>();
                    ControllerRules.PartGoods(pd, 1, 0, sum);
                    var missing = sum.Keys.Where(x => sum[x] > 0 && !goods.Any(g => g.Material == x)).Select(x => RecyclingTable.Current.NameOf(x)).ToArray();
                    if (sum.Count == 0 || missing.Length > 0)
                    {
                        Console.WriteLine($"[stations] {d.Name} makes no \"{pd.Name}\": " + (sum.Count == 0 ? "it has no Assembling recipe" : $"it is made from {string.Join(", ", missing)}, which the station doesn't trade"));
                        parts[k] = parts[k] with { Make = 0 };
                        continue;
                    }
                    parts[k] = parts[k] with { From = [.. sum.Where(x => x.Value > 0).Select(x => InputOf(goods, RecyclingTable.Current.NameOf(x.Key), x.Value, d.Name, $"\"{pd.Name}\""))] };
                }
                d.Goods = [.. goods];
                d.Parts = [.. parts];
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
                        Needs = h.TryGetProperty("needs", out var nd) && nd.GetBoolean(),
                        Parts = h.TryGetProperty("parts", out var hp) && hp.GetBoolean(),
                    };
                    d.ListedCount = d.Count;
                    if (h.TryGetProperty("escort", out var es))
                    {
                        d.Escort = es.GetProperty("ship").GetString() ?? "";
                        d.Escorts = Math.Clamp(es.TryGetProperty("count", out var ec) ? ec.GetInt32() : 1, 0, 4);
                        d.EscortGap = Math.Clamp(es.TryGetProperty("gap", out var eg) ? eg.GetSingle() : 150f, Game.NavMeetGapLeast, Game.NavMeetGapMost / 3);
                        if (d.Escort.Length == 0)
                            throw new FormatException("haulers: an \"escort\" names no \"ship\"");
                    }
                    if (d.From == d.To)
                        throw new FormatException("haulers: \"from\" and \"to\" are the same station");
                    d.Goods = Mats("goods", list[d.From]);
                    d.Back = Mats("back", list[d.To]);
                    haulers.Add(d);
                }
            _haulers = [.. haulers];
            StationLife.Load(root, list, GoodsOf, UpkeepOf);
            foreach (var d in list)
                d.ListedPower = d.Power;
            Listed = list.Count;
            ListedRoutes = _haulers.Length;
            Console.WriteLine($"[stations] {list.Count} station market(s), {_haulers.Length} hauler route(s) and {StationLife.Kinds.Length} station kind(s) from {path}");
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
/// (<see cref="StationMarket"/>), takes a docked ship's ore for value on a tab (GameTabs.cs) (<see cref="FactionOp.StationSell"/>: someone aboard the moored ship asks),
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
        public double[] Frac = []; // host: what its needs used or made short of a whole unit, by good
        public double[] Owed = []; // host: what its making is owed short of a whole unit, by line (not saved: under one unit a line)
        public bool[] Stalled = []; // host: a line it makes that last stopped for want of an input (its log line is said once)
        public string[] Lines = []; // a client: its supply lines and consignments bound for it, as the host last told (StationLines)
        public double Gain, Ledger, LastCost; // host: its power's gain this hour, its ledger over the window, its last hour's upkeep (GameStationLife.cs)
        public double[] Net = []; // host: each hour's gain less upkeep, a ring over StationLife.Window
        public int NetN, LoseHours, SaleHours, HoldHours; // (HoldHours: hours its power keeps it before it may sell it, after a sale or its opening)
        public double CostSum;
        public int FullHours, RefusedHours, Refused; // host: its growth's counts (GameStationLife.cs StationGrowHour); Refused this hour // host: about its upkeep over the window (a running sum) // host: hours in the ring, hours its ledger has been under nothing, hours for sale or closed

        /// <summary>
        /// How a good's prices stand against its stock (Karl, 2026-11-20: needs in the world): 1 at the stock it wants, up to 1.6 as it runs
        /// out, down to 0.6 at twice its want and past. Both sides work it from the stock they have, so no price goes on the wire.
        /// </summary>
        public float PriceFactor(int k)
        {
            var goods = StationMarkets.All[Def].Goods;
            if (k < 0 || k >= goods.Length || k >= Stock.Length)
                return 1;
            var r = Stock[k] / (float)goods[k].Target;
            return Math.Clamp(1.6f - 0.6f * r, 0.6f, 1.6f);
        }

        /// <summary>
        /// The price factor of good <paramref name="k"/> averaged over its stock running from <paramref name="a"/> to <paramref name="b"/> (either
        /// way): a trade is priced across the stock it moves, so a load bought whole and sold back a bin at a time come out the same.
        /// </summary>
        public double FactorOver(int k, double a, double b)
        {
            if (a > b)
                (a, b) = (b, a);
            if (b - a < 0.5)
                return PriceFactor(k);
            double t = StationMarkets.All[Def].Goods[k].Target;
            var c = 5.0 / 3.0 * t; // (where the line 1.6 - 0.6 s / t meets the 0.6 floor)
            double Line(double lo, double hi) => 1.6 * (hi - lo) - 0.3 * (hi * hi - lo * lo) / t;
            double sum = 0;
            if (a < c)
                sum += Line(a, Math.Min(b, c));
            if (b > c)
                sum += 0.6 * (b - Math.Max(a, c));
            return sum / (b - a);
        }

        /// <summary>What it pays for <paramref name="n"/> units of good <paramref name="k"/> taken in on its stock now.</summary>
        public double BuyValue(int k, int n) => n <= 0 ? 0 : n * (double)StationMarkets.All[Def].Goods[k].Buy * FactorOver(k, Stock[k], Stock[k] + n);

        /// <summary>What it asks for <paramref name="n"/> units of good <paramref name="k"/> taken out of its stock now.</summary>
        public double SellCost(int k, int n) => n <= 0 ? 0 : n * (double)StationMarkets.All[Def].Goods[k].Sell * FactorOver(k, Stock[k] - n, Stock[k]);

        /// <summary>The most units of good <paramref name="k"/> that <paramref name="budget"/> buys out of its stock now.</summary>
        public int Afford(int k, double budget)
        {
            int lo = 0, hi = Math.Max(0, Stock[k]);
            while (lo < hi)
            {
                var mid = lo + (hi - lo + 1) / 2;
                if (SellCost(k, mid) <= budget)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        /// <summary>What it pays for a unit of good <paramref name="k"/> now.</summary>
        public float BuyAt(int k) => StationMarkets.All[Def].Goods[k].Buy * PriceFactor(k);

        /// <summary>What it asks for a unit of good <paramref name="k"/> now.</summary>
        public float SellAt(int k) => StationMarkets.All[Def].Goods[k].Sell * PriceFactor(k);

        /// <summary>Is good <paramref name="k"/> one it uses and is short of (under half its want).</summary>
        public bool Short(int k)
        {
            var goods = StationMarkets.All[Def].Goods;
            return k < goods.Length && k < Stock.Length && goods[k].Use > goods[k].Make && Stock[k] < goods[k].Target / 2;
        }
    }

    /// <summary>A minute of play (ticks) between the stations' needs steps.</summary>
    private const int NeedsEvery = 60 * World.TickRate;
    private long _needsAt;

    /// <summary>
    /// Host, once a minute: every station's needs (Crew/Stations.json "uses" and "makes" an hour), whether its world is the one being played or
    /// not (a market is kept by its entry). What it uses goes from its stock. What it makes it makes only from what it holds (Karl, 2026-11-22:
    /// "it would need to be collected first"): each unit takes its inputs ("from", or a part's recipe) out of its own goods, and a line stops
    /// when an input runs out or its stock reaches its want, so a shortage spreads down the chain.
    /// </summary>
    private void StationNeedsTick()
    {
        if (ServerTick < _needsAt)
            return;
        var first = _needsAt == 0;
        _needsAt = ServerTick + NeedsEvery;
        if (first)
            return; // (the first minute starts the clock)
        for (var d = 0; d < StationMarkets.All.Length; d++)
        {
            var def = StationMarkets.All[d];
            if (!StationLife.Trades(def))
                continue; // (a site, or one closed: no needs, no making)
            var goods = def.Goods;
            var parts = def.Parts;
            var lines = goods.Length + parts.Length;
            var m = MarketOfDef(d);
            if (m.Frac.Length != lines)
                m.Frac = new double[lines];
            if (m.Owed.Length != lines)
            {
                m.Owed = new double[lines];
                m.Stalled = new bool[lines];
            }
            for (var k = 0; k < lines; k++)
            {
                var use = k < goods.Length ? goods[k].Use : parts[k - goods.Length].Use;
                if (use <= 0)
                    continue;
                var before = m.Stock[k];
                Move(m, k, -use / 60.0, k < goods.Length ? goods[k].Most : parts[k - goods.Length].Most);
                if (k >= goods.Length && m.Stock[k] < before)
                    StationShoppers(m, (before - m.Stock[k]) * (double)parts[k - goods.Length].Sell);
            }
            for (var k = 0; k < lines; k++)
            {
                var (make, target, most, from) = k < goods.Length ? (goods[k].Make, goods[k].Target, goods[k].Most, goods[k].From)
                    : (parts[k - goods.Length].Make, parts[k - goods.Length].Target, parts[k - goods.Length].Most, parts[k - goods.Length].From);
                if (make <= 0 || from.Length == 0)
                    continue;
                var owed = m.Owed[k] + make / 60.0;
                var n = (int)owed;
                n = Math.Min(n, Math.Max(0, Math.Min(target, most) - m.Stock[k])); // (it makes for the stock it wants, not past it)
                var shortOf = -1;
                foreach (var inp in from)
                {
                    var can = (int)Math.Floor((m.Stock[inp.Line] + m.Frac[inp.Line]) / inp.Per + 1e-9);
                    if (can < n)
                    {
                        n = Math.Max(0, can);
                        shortOf = inp.Line;
                    }
                }
                m.Owed[k] = Math.Min(owed - n, 1); // (a line held up by its want or its inputs banks no more than one unit)
                if (n > 0)
                {
                    var added = k < goods.Length ? TradeValues.Of(goods[k].Material) : (double)parts[k - goods.Length].Sell;
                    foreach (var inp in from)
                    {
                        Move(m, inp.Line, -inp.Per * n, goods[inp.Line].Most);
                        added -= inp.Per * TradeValues.Of(goods[inp.Line].Material);
                    }
                    Move(m, k, n, most);
                    StationGain(m, added * n); // (the value its making adds: its ledger, GameStationLife.cs)
                }
                var stalled = shortOf >= 0 && m.Stock[k] < target;
                if (stalled != m.Stalled[k])
                {
                    m.Stalled[k] = stalled;
                    var what = k < goods.Length ? RecyclingTable.Current.NameOf(goods[k].Material) : ConstructParts.Def(parts[k - goods.Length].Part).Name;
                    Log(stalled ? $"[station] {def.Name} stops making {what}: it is out of {RecyclingTable.Current.NameOf(goods[shortOf].Material)}"
                        : $"[station] {def.Name} makes {what} again");
                }
            }
        }
        StationLifeMinute(); // (sites, upkeep and ledgers, sales and closing, building: GameStationLife.cs)
        HardshipTick(); // (each planet's hardship and the raider bands it forms: GameCamps.cs)
        StationsVersion++; // (once a minute the cards hear the supply lines again: a line goes late with no other change, GameSupplyLines.cs)
        ConsignTick(); // (shortages no route answers straight: consignments routed over the haulers, GameConsignments.cs)

        // (a line's stock moved by an amount, whole units into the stock and the rest kept, inside 0 and its most)
        void Move(StationMarket m, int k, double by, int most)
        {
            m.Frac[k] += by;
            var whole = (int)Math.Truncate(m.Frac[k]);
            if (whole == 0)
                return;
            m.Frac[k] -= whole;
            var was = m.Stock[k];
            m.Stock[k] = Math.Clamp(m.Stock[k] + whole, 0, most);
            if (m.Stock[k] != was && m.Body != 0)
                StationsVersion++;
        }
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
        var d = StationMarkets.All[def];
        return _marketByDef[def] ??= new StationMarket { Def = def, Stock = [.. d.Goods.Select(g => g.Stock), .. d.Parts.Select(g => g.Stock)] }; // (goods, then parts)
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
        _storeFills.Clear(); // (a store furnishes its bays again in a new world: GameStoreFill.cs)
        StoreShopReset(); // (...its shelves, baskets and trolleys: GameStoreShop.cs)
        StoreLifeReset(); // (...its people: GameStoreLife.cs)
        StationMarketsHere.Clear();
        var travel = _landing != null; // (a landing keeps the markets: GameSpaceLanding.cs)
        foreach (var m in _marketByDef)
            if (m != null)
            {
                m.Body = 0;
                m.Moored.Clear();
            }
        if (!travel)
        {
            _marketByDef = [];
            StationLifeReset(); // (only the listed stations and routes: a save's made ones come back with its markets, GameStationLife.cs)
            _consigns.Clear(); // (a new game: GameConsignments.cs)
            CampsReset(); // (GameCamps.cs)
        }
        Array.Clear(_routeHadHauler);
        Array.Clear(_routeArrived); // (a route's haulers are seen again in the new world: GameSupplyLines.cs)
        _hauls.Clear(); // (the haulers here went with the world left behind; this world's saved ones are taken up again: HaulAdopt)
        _escorts.Clear(); // (and their escorts: an id kept from the old world would name another body here)
        _haulAdopt = true;
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
        StationMoorsAdopt(); // (a loaded world's moorings, once its bodies are in: GameStationSave.cs)
        TrespassTick(); // (what players' constructs mined on a held planet: GameTrespass.cs)
        StationNeedsTick();
        StationSiteTick(); // (stations made in play over this world's planet, going up: GameStationLife.cs)
        foreach (var c in Constructs.Bodies)
            if (c.IsStation && !c.Wreck)
                StationMarketOf(c);
        StoreFillTick(); // (a store's showroom furnished from its stock: GameStoreFill.cs)
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

    /// <summary><see cref="Msg.Stations"/>: u8 n, each (u16 body, u8 def, u8 goods, each i32 stock, u8 moored, each (u16 ship, u16 berth), u8 lines, each str).</summary>
    private void WriteStations(PacketWriter w)
    {
        w.Begin(Msg.Stations);
        WriteStationDefs(w); // (the stations made in play first: a market's def may be one, GameStationLife.cs)
        w.U8((byte)Math.Min(255, StationMarketsHere.Count));
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
            StationLines(m.Def, _linesBuf); // (its supply lines and consignments: GameSupplyLines.cs)
            w.U8((byte)_linesBuf.Count);
            foreach (var l in _linesBuf)
                w.Str(l);
        }
    }

    /// <summary>Server: the markets to one joiner.</summary>
    private void SendStationsTo(RakNetGUID to)
    {
        if (StationMarketsHere.Count == 0 && StationMarkets.All.Length == StationMarkets.Listed)
            return; // (nothing here, and nothing made in play: but a listed station sold or closed still goes, as its card's line)
        WriteStations(SbW);
        Net.SendTo(to, SbW.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Client: the markets and moorings (after the id). Moorings are set on this peer's constructs, so its berth counts and cards read them.</summary>
    internal void ReadStations(ref PacketReader r)
    {
        if (IsServer || !r.Has(1))
            return;
        ReadStationDefs(ref r);
        if (!r.Has(1))
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
            if (r.Has(1))
            {
                var lines = Math.Min((int)r.U8(), MaxSupplyLines);
                m.Lines = new string[lines];
                for (var k = 0; k < lines; k++)
                    m.Lines[k] = r.Has(2) ? r.Str() : ""; // (a short packet leaves no hole)
            }
            if (m.Def < StationMarkets.All.Length && StationMarkets.All[m.Def].Goods.Length + StationMarkets.All[m.Def].Parts.Length == m.Stock.Length)
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
    /// Any peer: what the station would pay for what <paramref name="ship"/>'s bins hold of the goods it buys (value, and units), up to the
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
        Span<int> take = stackalloc int[goods.Length];
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (!ship.DefOf(i).IsBin || Constructs.StoreOf(ship, i) is not { Amount: > 0 } st)
                continue;
            for (var k = 0; k < goods.Length; k++)
                if (goods[k].Material == st.Material)
                {
                    var n = Math.Min(st.Amount, room[k]);
                    room[k] -= n;
                    take[k] += n;
                    units += n;
                    break;
                }
        }
        double value = 0;
        for (var k = 0; k < goods.Length; k++)
            value += m.BuyValue(k, take[k]); // (priced across the stock the sale moves, as the sale itself is)
        return value;
    }

    /// <summary>
    /// Host: the station buys what <paramref name="ship"/>'s bins hold of the goods it buys, up to the room it has for each; <paramref name="sold"/>
    /// gets the units by good. Returns what it pays (value on a tab); the power holding it counts its margin on that as earned. A hauler of the
    /// station's own power (<paramref name="own"/>) only brings its power's goods home: it is paid nothing (goods are the only wealth, 2026-11-22).
    /// </summary>
    private double StationTakeCargo(StationMarket m, Construct ship, Span<int> sold, bool own = false)
    {
        var goods = StationMarkets.All[m.Def].Goods;
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (!ship.DefOf(i).IsBin || Constructs.StoreOf(ship, i) is not { Amount: > 0 } store)
                continue;
            for (var k = 0; k < goods.Length; k++)
                if (goods[k].Material == store.Material && goods[k].Buy > 0)
                {
                    sold[k] += Constructs.Withdraw(ship, i, Math.Min(store.Amount, Math.Max(0, goods[k].Most - m.Stock[k] - sold[k])));
                    break;
                }
        }
        double value = 0;
        for (var k = 0; k < goods.Length && k < sold.Length; k++)
        {
            value += m.BuyValue(k, sold[k]); // (across the stock the sale moves: the card's offer counts it the same way)
            m.Stock[k] += sold[k];
        }
        if (own)
            return 0; // (its own power's hauler: its goods come home, nobody is paid)
        StationMargin(m, value);
        return value;
    }

    /// <summary>
    /// Host: up to <paramref name="most"/> units of good <paramref name="k"/> from the station's stock into <paramref name="ship"/>'s bins (those that
    /// take it, an empty one or one holding it already). Returns the units loaded; they cost its asking price each.
    /// </summary>
    private int StationLoadCargo(StationMarket m, Construct ship, int k, int most, out double cost, bool own = false)
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
        cost = own ? 0 : m.SellCost(k, put); // (across the stock the load moves; its own power's hauler takes its goods for nothing)
        m.Stock[k] -= put;
        if (put > 0)
        {
            StationMargin(m, cost);
            StationsVersion++;
        }
        return put;
    }

    /// <summary>
    /// Host: a store's shoppers took parts worth <paramref name="value"/> at its asking price (a part line's "uses": Hexkey Orbital's furniture), and
    /// the station's power is paid it (2026-11-22, from the long check: the Traders paid the Militia for the flat-pack works' steel and nothing came back).
    /// </summary>
    private void StationShoppers(StationMarket m, double value)
    {
        if (value <= 0 || StationPower(m) is not (> 0 and var power) || Controllers.Powers[power] is not { Fallen: false } pw)
            return;
        pw.Stock += value;
        pw.Earned += value;
        StationGain(m, value);
    }

    /// <summary>Host: the station's power counts its margin on a trade of <paramref name="value"/> (its stock and its books).</summary>
    private void StationMargin(StationMarket m, double value)
    {
        if (value <= 0 || StationPower(m) is not (> 0 and var power) || Controllers.Powers[power] is not { Fallen: false } pw)
            return;
        var share = value * StationMarkets.Margin;
        pw.Stock += share; // (its trade on it: the books keep it as earned)
        pw.Earned += share;
        StationGain(m, share);
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
        if (m.Def >= 0 && m.Def < StationMarkets.All.Length && !StationLife.Trades(StationMarkets.All[m.Def]))
            return "the station is closed: it trades no more";
        if (StationMarkets.All[m.Def].Owner is { Length: > 0 } owner && !StationMember(m, p))
            return $"{owner} hold this station: it trades with their members only";
        if (!StationAboard(p, s))
            return "you need to be aboard the docked ship to trade";
        var power = StationPower(m);
        if (power != 0 && CrewStanding(power, p.Id) < 0)
            return $"{CrewPowers.Of(power)?.Name ?? "they"} won't trade with you";
        return null;
    }

    /// <summary>Server: <paramref name="p"/> sells the ore and goods aboard moored ship <paramref name="shipId"/> to its station. Null: sold; else why not.</summary>
    /// <summary>
    /// Any peer: how a station's power sees <paramref name="player"/> moves its prices to them (Karl, 2026-11-22: "5 or 10% to start"): over 1 for
    /// a friend (it pays them more and asks less), under 1 for one it dislikes, by <c>contracts.standingPrice</c> in Crew/Relations.json at
    /// standing 100. What a person is paid is times this; what they pay is divided by it.
    /// </summary>
    public double StandingRate(byte power, byte player) =>
        power == 0 ? 1 : 1 + RelationRules.Current.StandingPrice * Math.Clamp(IsServer || !CrewStandingOn ? CrewStanding(power, player) : Relations.Heard(power, player), -100, 100) / 100.0;

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
        var power = StationPower(m);
        var owed = (long)Math.Floor(value * StandingRate(power, p.Id)); // (a friend is paid more: StandingRate)
        TabGive(p.Id, power, owed); // (barter: the goods for value on your tab with the station's power, spendable only with them)
        StationsVersion++;
        var what = StationGoodsText(m, sold);
        if (StationMember(m, p))
        {
            SandboxHands.Notices.Add((p.Id, $"Stored {what} at {name}, your faction's station."));
            Log($"[station] {p.Name} stored {what} from construct {ship.Id} at their faction's {name}");
            return null;
        }
        SandboxHands.Notices.Add((p.Id, $"Traded {what} to {name}: the {TabPowerName(power)} owe you {owed} more."));
        Log($"[station] {p.Name} traded {what} from construct {ship.Id} to {name} for {owed} on their tab");
        return null;
    }

    /// <summary>
    /// Server: <paramref name="p"/> buys good <paramref name="k"/> of moored ship <paramref name="shipId"/>'s station: as much as its stock, the ship's
    /// bins and their tab with its power allow (Karl, 2026-11-18: the market shows "whatever goods are there to trade"). Null: bought; else why not.
    /// </summary>
    private string? StationBuyAct(Player p, ushort shipId, int k)
    {
        if (StationTradeCheck(p, shipId, out var ship, out var st, out var m) is { } why)
            return why;
        var goods = StationMarkets.All[m.Def].Goods;
        var name = StationName(st);
        if (k >= goods.Length)
            return StationBuyPart(p, ship, st, m, k - goods.Length);
        if (goods[k].Sell <= 0)
            return $"{name} doesn't sell that";
        if (m.Stock[k] <= 0)
            return $"{name} has none left";
        var power = StationPower(m);
        var rate = StandingRate(power, p.Id);
        var price = m.SellAt(k) / rate;
        var member = StationMember(m, p); // (your faction's own station: its stock is yours, GameStationLife.cs)
        var afford = SandboxInfiniteParts || member ? int.MaxValue : m.Afford(k, TabOf(p.Id, power) * rate); // (priced across the stock the load moves; a friend pays less: StandingRate)
        if (afford <= 0)
            return $"a unit takes {price:0.00} off your tab with the {TabPowerName(power)}, who owe you {TabOf(p.Id, power)}: trade them goods first";
        var units = StationLoadCargo(m, ship, k, afford, out var paid);
        var good = RecyclingTable.Current.NameOf(goods[k].Material);
        if (units == 0)
            return $"no bin aboard has room for {good}";
        var cost = member ? 0 : (long)Math.Ceiling(paid / rate);
        if (!member && TabPay(p.Id, power, Math.Min(cost, TabOf(p.Id, power))) is { } cant) // (it can't fail: afford was counted from the same tab; a rounding cent is let go)
            return cant;
        SandboxHands.Notices.Add((p.Id, $"Took {units:N0} {good} from {name} for {cost} off your tab."));
        Log($"[station] {p.Name} took {units} {good} into construct {ship.Id} from {name} for {cost} off their tab");
        return null;
    }

    /// <summary>Any peer: how many of part <paramref name="part"/> the Storage Volumes and part stores of <paramref name="ship"/> hold (as heard).</summary>
    public int StationShipParts(Construct ship, ushort part)
    {
        Span<CHolding> held = stackalloc CHolding[CStore.MaxKinds + 1];
        var n = 0;
        for (var i = 0; i < ship.PartCount; i++)
        {
            if (Constructs.StoreOf(ship, i) is null)
                continue;
            var k = Constructs.Holdings(ship, i, held);
            for (var j = 0; j < k; j++)
                if (held[j].IsPart && held[j].Id == part)
                    n += held[j].Count;
        }
        return n;
    }

    /// <summary>
    /// Server: one of part <paramref name="j"/> of the station's parts into a Storage Volume of moored <paramref name="ship"/> (parts are
    /// physical: Karl, 2026-11-20), off <paramref name="p"/>'s tab with its power.
    /// </summary>
    private string? StationBuyPart(Player p, Construct ship, Construct st, StationMarket m, int j)
    {
        var def = StationMarkets.All[m.Def];
        var name = StationName(st);
        if (j < 0 || j >= def.Parts.Length || def.Parts[j].Sell <= 0)
            return $"{name} doesn't sell that";
        var k = def.Goods.Length + j;
        var part = def.Parts[j];
        if (ConstructParts.Def(part.Part) is not { } pd)
            return $"{name} doesn't sell that";
        if (m.Stock[k] <= 0)
            return $"{name} has no {pd.Name} left";
        var power = StationPower(m);
        var cost = StationMember(m, p) ? 0 : (long)Math.Ceiling(part.Sell / StandingRate(power, p.Id));
        if (cost > 0 && !SandboxInfiniteParts && TabOf(p.Id, power) < cost)
            return TabPay(p.Id, power, cost);
        var put = -1;
        string? refusal = null;
        for (var i = 0; i < ship.PartCount && put < 0; i++)
            if (ship.DefOf(i).IsVolumeStore)
            {
                if (Constructs.PutPart(ship, i, pd) is { } why)
                    refusal ??= why;
                else
                    put = i;
            }
        if (put < 0)
            return refusal != null ? $"no Storage Volume aboard takes a {pd.Name} ({refusal})" : $"a {pd.Name} goes in a Storage Volume, and this ship has none";
        TabPay(p.Id, power, cost);
        m.Stock[k]--;
        StationMargin(m, part.Sell);
        StationsVersion++;
        SandboxHands.Notices.Add((p.Id, $"Took a {pd.Name} into the hold from {name} for {cost} off your tab."));
        Log($"[station] {p.Name} took a {pd.Name} into construct {ship.Id} from {name} for {cost} off their tab");
        return null;
    }

    /// <summary>
    /// Server: every one of a station's parts that moored <paramref name="shipId"/>'s stores hold (parts are physical: Karl, 2026-11-20), up to
    /// the room the station has, for value on <paramref name="p"/>'s tab with its power. <paramref name="good"/> counts the station's goods first,
    /// as its market's stock does. Null: traded; else why not.
    /// </summary>
    private string? StationSellPartAct(Player p, ushort shipId, int good)
    {
        if (StationTradeCheck(p, shipId, out var ship, out var st, out var m) is { } why)
            return why;
        var def = StationMarkets.All[m.Def];
        var j = good - def.Goods.Length;
        var name = StationName(st);
        if (j < 0 || j >= def.Parts.Length || def.Parts[j].Buy <= 0 || ConstructParts.Def(def.Parts[j].Part) is not { } pd)
            return $"{name} doesn't take that";
        var part = def.Parts[j];
        var k = def.Goods.Length + j;
        var room = Math.Max(0, part.Most - m.Stock[k]);
        if (room <= 0)
            return $"{name} holds all the {pd.Name}s it wants";
        var took = 0;
        var what = new CHolding(true, part.Part, 0);
        for (var i = 0; i < ship.PartCount && took < room; i++)
            if (Constructs.StoreOf(ship, i) is not null)
                took += Constructs.TakeHolding(ship, i, what, room - took);
        if (took == 0)
            return $"no store aboard holds a {pd.Name}";
        m.Stock[k] += took;
        var power = StationPower(m);
        var owed = (long)Math.Floor(took * (double)part.Buy * StandingRate(power, p.Id));
        TabGive(p.Id, power, owed);
        StationsVersion++;
        SandboxHands.Notices.Add((p.Id, $"Traded {took} {pd.Name} to {name}: the {TabPowerName(power)} owe you {owed} more."));
        Log($"[station] {p.Name} traded {took} {pd.Name} from construct {ship.Id} to {name} for {owed} on their tab");
        return null;
    }
}
