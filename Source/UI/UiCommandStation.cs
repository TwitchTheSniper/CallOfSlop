using System.Globalization;
using System.Numerics;
using Brutal.ImGuiApi;
using CallOfSlop.Space;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The command view's station card (docs/sandbox/constructs/station.md "Markets"; Karl, 2026-11-18: a station "should show up on the friendly
/// market as whatever goods are there to trade", and the first loop is docking there and selling mined ore): who holds it and whether they
/// trade with you, its berths, its market (each good's stock, what it pays and asks), and, when you are aboard a ship moored at one of its
/// berths, TRADE CARGO with what the station would put on your tab for what that ship's bins hold, and its parts to take or trade (no credits:
/// what a station's power owes you is your tab with it, Game/GameTabs.cs).
/// </summary>
public sealed partial class Ui
{
    private struct CmdStationText
    {
        public ushort Id;
        public int Version;
        public double Read;
        public string Held, Trade;
        public bool Trades;
        public string[] Goods;
        public bool[] Buys; // good by good: can the local player buy it into the moored ship (stock left, the station sells it, they trade)
        public bool[] Sells; // a part by its row: does the local player carry one the station takes (goods' rows: false)
        public string Tab; // what the station's power owes the local player
        public string Makes, Uses; // its needs: what it makes and uses (Crew/Stations.json "makes", "uses"; "" for none)
        public List<string> Lines; // its supply lines and the consignments bound for it (Game.StationLines; economy step 10)
        public int GoodCount;
        public ushort Ship; // the moored ship the local player is aboard (0: none)
        public string ShipLine, SellLabel;
        public bool CanSell;
        public bool ForSale; // the station is for sale: aboard a ship moored here, BUY THIS STATION (your faction buys it: GameStationLife.cs)
    }

    private static readonly string MarketHead = $"{"GOOD",-12}{"STOCK",9}   {"BUYS",6}   {"SELLS",6}   VALUE A UNIT";

    private CmdStationText _cmdStation = new() { Held = "", Trade = "", Goods = new string[StationMarkets.MaxListed], Buys = new bool[StationMarkets.MaxListed], Sells = new bool[StationMarkets.MaxListed], Tab = "", Makes = "", Uses = "", ShipLine = "", SellLabel = "", Lines = new(Game.MaxSupplyLines) };

    private void CmdStationRead(Game g, Construct c, double now)
    {
        ref var k = ref _cmdStation;
        k.Id = c.Id;
        k.Version = g.StationsVersion;
        k.Read = now;
        k.GoodCount = 0;
        k.Ship = 0;
        k.CanSell = false;
        k.ForSale = false;
        var inv = CultureInfo.InvariantCulture;
        if (!g.StationMarketsHere.TryGetValue(c.Id, out var m) || m.Def >= StationMarkets.All.Length)
        {
            k.Held = "NOBODY";
            k.Trade = "NO MARKET HERE";
            k.Trades = false;
            return;
        }
        var power = Game.StationPower(m);
        var me = g.Local;
        var sdef = StationMarkets.All[m.Def];
        k.Held = (sdef.Owner.Length > 0 ? sdef.Owner : CrewPowers.Of(power)?.Name ?? "NOBODY").ToUpperInvariant();
        k.Trades = sdef.Owner.Length > 0 ? me != null && g.StationMember(m, me) : power == 0 || me == null || g.CrewStanding(power, me.Id) >= 0;
        k.ForSale = sdef.State == MarketState.ForSale;
        var rate = me == null ? 1 : g.StandingRate(power, me.Id); // (how its power sees you moves its prices to you: Game.StandingRate)
        var pct = (int)Math.Round((rate - 1) * 100);
        k.Trade = !k.Trades ? "WON'T TRADE WITH YOU" : pct == 0 ? "TRADES WITH YOU" : string.Create(inv, $"TRADES WITH YOU, PRICES {(pct > 0 ? "BETTER" : "WORSE")} BY {Math.Abs(pct)}%");
        k.Tab = me == null || power == 0 ? "" : g.TabUnlimited ? "UNLIMITED (CREATIVE)" : string.Create(inv, $"THEY OWE YOU {g.TabOf(me.Id, power):N0}");
        var goods = StationMarkets.All[m.Def].Goods;
        for (var i = 0; i < goods.Length && i < m.Stock.Length && k.GoodCount < k.Goods.Length; i++)
        {
            var name = RecyclingTable.Current.NameOf(goods[i].Material).ToUpperInvariant();
            var full = goods[i].Buy > 0 && m.Stock[i] >= goods[i].Most;
            k.Buys[k.GoodCount] = k.Trades && goods[i].Sell > 0 && m.Stock[i] > 0;
            k.Sells[k.GoodCount] = false;
            k.Goods[k.GoodCount++] = string.Create(inv, $"{name,-12}{m.Stock[i],9:N0}   {(full ? "FULL" : (m.BuyAt(i) * rate).ToString("0.00", inv)),6}   {m.SellAt(i) / rate,6:0.00}{(m.Short(i) ? "  SHORT" : "")}");
        }
        // Its needs (Karl, 2026-11-20): what it makes and uses an hour; a good it uses and is short of is marked SHORT above, and pays more.
        var makes = new System.Text.StringBuilder();
        var uses = new System.Text.StringBuilder();
        foreach (var gd in goods)
        {
            var gn = RecyclingTable.Current.NameOf(gd.Material).ToUpperInvariant();
            if (gd.Make > 0)
                makes.Append(makes.Length > 0 ? ", " : "").Append(gn);
            if (gd.Use > 0)
                uses.Append(uses.Length > 0 ? ", " : "").Append(gn);
        }
        k.Makes = makes.ToString();
        k.Uses = uses.ToString();
        if (g.IsServer)
            g.StationLines(m.Def, k.Lines);
        else
        {
            k.Lines.Clear();
            k.Lines.AddRange(m.Lines);
        }
        // Its parts (Karl, 2026-11-20: parts are physical and carry a value): BUY takes one into a Storage Volume of the moored ship you are
        // aboard, SELL trades every one its stores hold.
        var parts = StationMarkets.All[m.Def].Parts;
        Construct? aboard = null;
        if (me != null)
            foreach (var (ship, _) in m.Moored)
                if (g.Constructs.Get(ship) is { } sh && g.StationAboard(me, sh))
                    aboard = sh;
        for (var j = 0; j < parts.Length && goods.Length + j < m.Stock.Length && k.GoodCount < k.Goods.Length; j++)
        {
            var i = goods.Length + j;
            var pd = ConstructParts.Def(parts[j].Part);
            var name = (pd?.Name ?? "?").ToUpperInvariant();
            if (name.Length > 12)
                name = name[..12];
            var full = parts[j].Buy > 0 && m.Stock[i] >= parts[j].Most;
            k.Buys[k.GoodCount] = k.Trades && parts[j].Sell > 0 && m.Stock[i] > 0;
            k.Sells[k.GoodCount] = k.Trades && parts[j].Buy > 0 && !full && aboard != null && g.StationShipParts(aboard, parts[j].Part) > 0;
            k.Goods[k.GoodCount++] = string.Create(inv, $"{name,-12}{m.Stock[i],9:N0}   {(full ? "FULL" : (parts[j].Buy * rate).ToString("0.0", inv)),6}   {parts[j].Sell / rate,6:0.0}");
        }
        // Aboard a ship moored here: what the station would pay for its bins.
        if (me == null)
            return;
        foreach (var (ship, _) in m.Moored)
        {
            if (g.Constructs.Get(ship) is not { } s || !g.StationAboard(me, s))
                continue;
            k.Ship = ship;
            var value = g.StationOffer(m, s, out var units) * rate;
            k.ShipLine = "YOU ARE ABOARD " + CmdName(s) + ", DOCKED HERE";
            k.CanSell = k.Trades && units > 0;
            k.SellLabel = !k.Trades ? "THEY WON'T BUY FROM YOU"
                : units == 0 ? "NOTHING ABOARD IS WANTED HERE"
                : string.Create(inv, $"TRADE CARGO: {units:N0} UNITS FOR {Math.Floor(value):N0} ON YOUR TAB");
            break;
        }
    }

    private void CmdStationCard(ImDrawListPtr dl, Game g, SpaceSession s, Construct c, in CmdCraft cr, float fade, double now)
    {
        if (_cmdCard.Id != c.Id || now - _cmdCard.Read > 0.25 || _cmdCard.Name == null)
            CmdCardRead(g, s, c, cr, now);
        if (_cmdStation.Id != c.Id || _cmdStation.Version != g.StationsVersion || now - _cmdStation.Read > 0.5 || _cmdStation.Held.Length == 0)
            CmdStationRead(g, c, now);
        ref var k = ref _cmdCard;
        ref var t = ref _cmdStation;
        var pad = 22 * _scale;
        var w = 540 * _scale; // (room for a part row's SELL beside its BUY)
        var line = 19 * _scale;
        var rows = 6 + (t.Tab.Length > 0 ? 1 : 0) + (t.Makes.Length > 0 ? 1 : 0) + (t.Uses.Length > 0 ? 1 : 0) + t.Lines.Count + (k.Orbit.Length > 0 ? 1 : 0) + (t.GoodCount > 0 ? t.GoodCount + 2 : 0) + (t.Ship != 0 ? t.ForSale ? 5 : 3 : 1);
        var p0 = new float2(_cvOrg.X + _cvSize.X - pad - w, _cvOrg.Y + pad);
        var p1 = p0 + new float2(w, 64 * _scale + rows * line + 60 * _scale);
        CmdPlate(dl, p0, p1, fade, CmdStation);
        CmdPanelArea(p0, p1);
        var x = p0.X + 16 * _scale;
        var y = p0.Y + 10 * _scale;
        CmdText(dl, new float2(x, y), 22, CmdWhite, fade, k.Name, 0, false);
        y += 30 * _scale;
        CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, k.Class, 0, false);
        y += 22 * _scale;
        CmdLine(dl, new float2(x, y), new float2(p1.X - 16 * _scale, y), CmdStation, 0.4f * fade);
        y += 8 * _scale;
        var col = 92 * _scale;

        void Row(ImDrawListPtr d, string head, string text, Vector3 colour, ref float yy)
        {
            CmdText(d, new float2(x, yy), 10.5f, CmdDim, fade, head, 0, false);
            CmdText(d, new float2(x + col, yy - 1 * _scale), 12, colour, fade, text, 0, false);
            yy += line;
        }

        Row(dl, "STATUS", "A STATION", CmdWhite, ref y);
        Row(dl, "FLIGHT", k.Flight, CmdCyan, ref y);
        if (k.Orbit.Length > 0)
            Row(dl, "ORBIT", k.Orbit, CmdCyan, ref y);
        Row(dl, "BERTHS", k.Systems, CmdStation, ref y);
        Row(dl, "HELD BY", t.Held, CmdWhite, ref y);
        Row(dl, "TRADE", t.Trade, t.Trades ? CmdGreen : CmdRed, ref y);
        if (t.Tab.Length > 0)
            Row(dl, "YOUR TAB", t.Tab, CmdAmber, ref y); // (no credits: what they owe you, spendable only with them: GameTabs.cs)
        if (t.Makes.Length > 0)
            Row(dl, "MAKES", t.Makes, CmdGreen, ref y);
        if (t.Uses.Length > 0)
            Row(dl, "USES", t.Uses, CmdAmber, ref y);
        for (var i = 0; i < t.Lines.Count; i++) // (its supply lines and what is on its way: a cut line shows in time to do something about it)
        {
            var ln = t.Lines[i];
            Row(dl, i == 0 ? "SUPPLY" : "", ln, ln.EndsWith("CUT", StringComparison.Ordinal) ? CmdRed : ln.EndsWith("LATE", StringComparison.Ordinal) ? CmdAmber
                : ln.EndsWith("RUNNING", StringComparison.Ordinal) ? CmdGreen : CmdCyan, ref y);
        }
        if (t.GoodCount > 0)
        {
            y += 6 * _scale;
            CmdText(dl, new float2(x, y), 10.5f, CmdDim, fade, "MARKET", 0, false);
            CmdText(dl, new float2(x + col, y), 10.5f, CmdDim, fade, MarketHead, 0, false);
            y += line;
            for (var i = 0; i < t.GoodCount; i++)
            {
                CmdText(dl, new float2(x + col, y - 1 * _scale), 11.5f, CmdCyan, fade, t.Goods[i], 0, false);
                // Aboard a ship moored here: BUY fills its bins with as much of the good as they hold and your tab pays for (a part: one, into a Storage
                // Volume aboard); SELL, on a part's row, trades every one the ship's stores hold.
                if (t.Ship != 0 && CmdButton(dl, new float2(p1.X - 66 * _scale, y - 2 * _scale), new float2(p1.X - 14 * _scale, y + 14 * _scale), "BUY",
                        t.Buys[i] ? CmdGreen : CmdDim, fade, false, 10) && t.Buys[i])
                    g.SandboxFaction(FactionOp.StationBuy, t.Ship, i);
                if (t.Ship != 0 && t.Sells[i] && CmdButton(dl, new float2(p1.X - 122 * _scale, y - 2 * _scale), new float2(p1.X - 70 * _scale, y + 14 * _scale), "SELL",
                        CmdAmber, fade, false, 10))
                    g.SandboxFaction(FactionOp.StationSellPart, t.Ship, i);
                y += line * 0.9f;
            }
            y += line * 0.3f;
        }
        y += 6 * _scale;
        CmdLine(dl, new float2(x, y), new float2(p1.X - 16 * _scale, y), CmdStation, 0.4f * fade);
        y += 8 * _scale;
        if (t.Ship != 0)
        {
            Row(dl, "DOCKED", t.ShipLine, CmdGreen, ref y);
            y += 4 * _scale;
            if (CmdButton(dl, new float2(x + col, y), new float2(x + col + 290 * _scale, y + 24 * _scale), t.SellLabel, t.CanSell ? CmdGreen : CmdDim, fade) && t.CanSell)
                g.SandboxFaction(FactionOp.StationSell, t.Ship);
            y += 30 * _scale;
            if (t.ForSale)
            {
                if (CmdButton(dl, new float2(x + col, y), new float2(x + col + 290 * _scale, y + 24 * _scale), "BUY THIS STATION FOR YOUR FACTION", CmdAmber, fade))
                    g.SandboxFaction(FactionOp.StationPurchase, t.Ship);
                y += 30 * _scale;
            }
        }
        else
            Row(dl, "DOCKING", "PICK A SHIP, THEN RIGHT CLICK HERE TO DOCK", CmdAmber, ref y);
        if (CmdButton(dl, new float2(p1.X - 96 * _scale, p1.Y - 32 * _scale), new float2(p1.X - 12 * _scale, p1.Y - 10 * _scale), "CENTRE", CmdCyan, fade, !_cmdFocusAuto && _cmdFocusShip == c.Id, 11))
        {
            _cmdFocusAuto = false;
            _cmdFocusShip = c.Id;
            _cmdFocusBody = -1;
            _cmdPan = Vec3d.Zero;
        }
    }
}
