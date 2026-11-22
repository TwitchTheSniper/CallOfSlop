using System.Text.Json;

namespace CallOfSlop;

/// <summary>
/// A kind of station a power can put up in play (Crew/Stations.json "kinds"; economy step 11, design doc section 8: "dynamic stations need
/// station kinds as templates"): the blueprint it is built from, its height over its planet, the goods it trades once open (a station's
/// "goods" lines), its upkeep, the value an hour it is expected to make as a base, the key-site kinds on a planet it suits, and the supply
/// route it opens to its power's nearest station.
/// </summary>
public sealed class StationKind
{
    public string Name = "", Blueprint = "";
    public float Orbit; // m over its planet
    public StationGood[] Goods = [];
    public (byte Material, float Units)[] Upkeep = [];
    public float Labour, Expect;
    public string[] For = []; // site kinds (Crew/Territory.json "kinds") it suits; empty: any planet
    public string RouteShip = "";
    public int RouteCount;
    public byte[] RouteGoods = [], RouteBack = []; // materials its route carries to it, and back to the power's station
}

/// <summary>
/// The rules of stations coming and going (Crew/Stations.json "stationRules"; Karl, 2026-11-22: "stations are dynamic ... powers invest where
/// needed, pay build and upkeep; underused ones are sold, else decommissioned"). Hours are hours of play.
/// </summary>
public static class StationLife
{
    public static StationKind[] Kinds { get; private set; } = [];

    public static float Payback = 24; // h: a power builds where the gain it expects over this beats the bill
    public static int Window = 6; // h of the ledger (gain against upkeep)
    public static int SaleAfter = 6, CloseAfter = 6, SalvageFor = 6; // h losing before it goes up for sale; for sale before it closes; closed before the Salvagers take it
    public static int Builders = 2; // builder ships a route
    public static string BuilderShip = "";
    public static float HaulSave = 0.1f; // the share of a planet's site goods' value an hour a station over it saves in hauling
    public static float LabourShare = 0.2f; // the labour of a build, as a share of its bill's value (from the power's number)
    public static float SalePrice = 0.5f; // what a station sells for, as a share of its bill's value
    public static float SalvageShare = 0.25f; // the dismantler's share of a closed station's recovered goods
    public static float BillScale = 1; // a station's bill as a share of its blueprint's (a tuning number)
    public static int MaxNew = 8; // stations made in play at most (open or not)
    public static (byte Material, float Units)[] Upkeep = []; // a listed station's upkeep an hour, unless its own "upkeep" says
    public static float Labour; // and its labour an hour
    public static float SaleBelow = 0.5f; // a station is losing when its ledger is under this share of its upkeep over the window, below nothing (a hair under breaks no station)
    public static int HoldAfter = 24; // h a power keeps a station it bought or built before it may sell it
    public static bool SaleListed = true; // may a listed station (Crew/Stations.json "stations") go up for sale and close, as one made in play may

    internal static void Load(JsonElement root, List<StationDef> list, Func<JsonElement, string, List<StationGood>> goodsOf,
        Func<JsonElement?, string, (byte Material, float Units)[]> upkeepOf)
    {
        Kinds = [];
        if (root.TryGetProperty("stationRules", out var r))
        {
            float F(string key, float def, float lo, float hi) => r.TryGetProperty(key, out var v) ? Math.Clamp(v.GetSingle(), lo, hi) : def;
            Payback = F("payback", 24, 1, 1000);
            Window = (int)F("window", 6, 1, 48);
            SaleAfter = (int)F("saleAfter", 6, 1, 1000);
            CloseAfter = (int)F("closeAfter", 6, 1, 1000);
            SalvageFor = (int)F("salvageFor", 6, 1, 1000);
            Builders = (int)F("builders", 2, 1, 8);
            BuilderShip = r.TryGetProperty("builderShip", out var bs) ? bs.GetString() ?? "" : "";
            HaulSave = F("haulSave", 0.1f, 0, 1);
            LabourShare = F("labourShare", 0.2f, 0, 10);
            SalePrice = F("salePrice", 0.5f, 0, 10);
            SalvageShare = F("salvageShare", 0.25f, 0, 1);
            BillScale = F("billScale", 1, 0.01f, 10);
            MaxNew = (int)F("maxNew", 8, 0, 64);
            Upkeep = upkeepOf(r.TryGetProperty("upkeep", out var u) ? u : null, "stationRules");
            Labour = F("labour", 0, 0, 100_000);
            SaleBelow = F("saleBelow", 0.5f, 0, 10);
            HoldAfter = (int)F("holdAfter", 24, 0, 1000);
            SaleListed = !r.TryGetProperty("saleListed", out var sl0) || sl0.GetBoolean();
        }
        // A listed station's upkeep: its own, else the rules'.
        var k = 0;
        foreach (var s in root.GetProperty("stations").EnumerateArray())
        {
            var d = list[k++];
            d.Upkeep = s.TryGetProperty("upkeep", out var su) ? upkeepOf(su, d.Name) : Upkeep;
            d.Labour = s.TryGetProperty("labour", out var sl) ? Math.Clamp(sl.GetSingle(), 0, 100_000) : Labour;
            foreach (var (mat, _) in d.Upkeep)
                if (!d.Goods.Any(g => g.Material == mat))
                    throw new FormatException($"{d.Name}: its upkeep takes {RecyclingTable.Current.NameOf(mat)}, which it doesn't trade");
        }
        if (!root.TryGetProperty("kinds", out var ks))
            return;
        var kinds = new List<StationKind>();
        foreach (var e in ks.EnumerateArray())
        {
            var name = e.GetProperty("kind").GetString() ?? "";
            if (name.Length == 0 || name.Any(ch => ch > 127))
                throw new FormatException("kinds: a kind needs an ASCII \"kind\" name");
            var goods = goodsOf(e.GetProperty("goods"), name);
            if (goods.Count > StationMarkets.MaxGoods)
                throw new FormatException($"{name}: at most {StationMarkets.MaxGoods} goods");
            byte[] Mats(string key, bool sells) => e.TryGetProperty("route", out var rt) && rt.TryGetProperty(key, out var gs) ? [.. gs.EnumerateArray().Select(g =>
            {
                var mat = RecyclingTable.Current.IdOf(g.GetString() ?? "");
                return mat != 0 && (!sells || goods.Any(x => x.Material == mat && x.Sell > 0)) ? mat
                    : throw new FormatException($"{name}: its route's \"{key}\" names {g.GetString()}, which {(sells ? "it doesn't sell" : "is no material")}");
            })] : [];
            var kd = new StationKind
            {
                Name = name,
                Blueprint = e.GetProperty("blueprint").GetString() ?? "",
                Orbit = Math.Clamp(e.TryGetProperty("orbit", out var ob) ? ob.GetSingle() * 1000f : 30_000f, 5000f, 150_000f),
                Goods = [.. goods],
                Upkeep = upkeepOf(e.TryGetProperty("upkeep", out var ku) ? ku : null, name),
                Labour = e.TryGetProperty("labour", out var kl) ? Math.Clamp(kl.GetSingle(), 0, 100_000) : Labour,
                Expect = e.TryGetProperty("expect", out var ex) ? Math.Clamp(ex.GetSingle(), 0, 1_000_000) : 0,
                For = e.TryGetProperty("for", out var fo) ? [.. fo.EnumerateArray().Select(x => x.GetString() ?? "")] : [],
                RouteShip = e.TryGetProperty("route", out var ro) && ro.TryGetProperty("ship", out var rs) ? rs.GetString() ?? "" : "",
                RouteCount = e.TryGetProperty("route", out var rc) && rc.TryGetProperty("count", out var rn) ? Math.Clamp(rn.GetInt32(), 0, 8) : 1,
                RouteGoods = Mats("goods", false),
                RouteBack = Mats("back", true),
            };
            foreach (var (mat, _) in kd.Upkeep)
                if (!kd.Goods.Any(g => g.Material == mat))
                    throw new FormatException($"{name}: its upkeep takes {RecyclingTable.Current.NameOf(mat)}, which it doesn't trade");
            if (kd.Blueprint.Length == 0)
                throw new FormatException($"{name}: a kind needs a \"blueprint\"");
            kinds.Add(kd);
        }
        Kinds = [.. kinds];
    }

    /// <summary>The kind by its name (-1: none).</summary>
    public static int Find(string name)
    {
        for (var i = 0; i < Kinds.Length; i++)
            if (Kinds[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>Is a station in a state where it trades (open, or open and for sale)?</summary>
    public static bool Trades(StationDef d) => d.State is MarketState.Open or MarketState.ForSale;
}
