using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies trader" (Source/Game/ZombiesTrader.cs; docs/zombies/ZOMBIES-MODE.md, "The trader"): the
/// rules load (a broken trader section is refused); the shelf's top tier follows the round and always sits below what the
/// round's drop table favours; prices by rarity and round; on Zombies Test (no trader spots: he stands in for a box spot
/// or workbench) he comes at rounds 5, 10 and 15 and leaves when 6, 11 and 16 start, on a fresh shelf each time with
/// distinct parts of the right tiers; purchases take the points and give the part (or the grenades, or the reserve),
/// refusals (sold out, an old shelf, too far, too poor, grenades already full) take nothing; two players buying the last
/// one in the same tick: one gets it; the client's copy follows every message; a co-op bot shops at him; and nothing is
/// allocated per tick while he's here.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesTrader()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "trader rules");
        ZLine($"trader rules: every {rules.TraderEveryRounds} rounds for {rules.TraderStayRounds}, {rules.TraderStock} parts, {rules.TraderTiersBelow} tier(s) below the favoured ({rules.TraderTopMin}-{rules.TraderTopMax}), prices {string.Join("/", rules.TraderPrices)} +{rules.TraderPricePerRound:P0} a round, grenades {rules.TraderGrenadePrice}, ammo {rules.TraderAmmoPrice}");
        var refused = 0;
        foreach (var bad in new[] { """{"topMin": "rare", "topMax": "common"}""", """{"stock": 99}""", """{"shelf": 3}""" })
            try
            {
                ZombiesRules.Load($$"""{"format": "call-of-slop-zombies/1", "trader": {{bad}}}""", "bad trader");
            }
            catch (WeaponFileException)
            {
                refused++;
            }
        if (refused != 3 || !rules.TraderComes(5) || rules.TraderComes(4) || rules.TraderComes(0))
            Fail($"the trader rules should load, and a broken trader section should be refused ({refused}/3 refused)");

        // The shelf's top tier by round: below the favoured tier of the round's part drops, within the rules' bounds.
        var tables = RunTables.Current;
        var tiers = new List<string>();
        var tierOk = true;
        foreach (var round in new[] { 5, 10, 15, 20, 25, 30, 35, 50 })
        {
            var top = rules.TraderTopTier(tables, round);
            var row = RunTables.RowFor(tables.PartDrops, round)!;
            var favoured = Array.LastIndexOf(row.Weights, row.Weights.Max());
            tiers.Add($"r{round} {top} (drops favour {(Rarity)favoured})");
            tierOk &= top >= rules.TraderTopMin && top <= rules.TraderTopMax && ((int)top < favoured || top == rules.TraderTopMin);
        }
        var p1 = rules.TraderPrice(Rarity.Rare, 1);
        var p11 = rules.TraderPrice(Rarity.Rare, 11);
        var priceOk = p1 == rules.TraderPrices[(int)Rarity.Rare] && p11 == (int)MathF.Round(p1 * (1 + rules.TraderPricePerRound * 10) / 10f) * 10 && p11 > p1 && rules.TraderMarkUp(123, 1) == 120;
        ZLine($"shelf tiers: {string.Join(", ", tiers)}; a rare part {p1} at round 1, {p11} at round 11");
        if (!tierOk || !priceOk)
            Fail("the trader's top tier should stay below what the round's drops favour (within the rules' bounds), and prices should rise by rarity and round");

        TraderVisits();
        TraderShopping();
        TraderBot();
    }

    /// <summary>Rounds 1-16 on Zombies Test: here at 5, 10, 15; gone when 6, 11, 16 start; a fresh, well-formed shelf each visit.</summary>
    private static void TraderVisits()
    {
        if (ZTestMap?.Zombies is not { } zm)
            return;
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        var here = new List<int>();
        var visits = new List<string>();
        var shelfOk = true;
        var spotsOk = true;
        var leftOk = true;
        var seenVisits = new HashSet<int>();
        for (var round = 1; round <= 16; round++)
        {
            g.SelfTestBeginRound(round, now += 1);
            var t = g.ZTrader;
            if (t.Here)
                here.Add(round);
            if (!t.Here || !seenVisits.Add(t.Visit))
            {
                leftOk &= t.Here || round % g.ZRules.TraderEveryRounds != 0;
                continue;
            }
            // A new visit: in front of a box spot or workbench (no trader spots on this map), in an open area.
            var fronts = zm.BoxSpots.Concat(zm.Benches).Select(s => s.At + World.FlatForward(s.Yaw) * 1.4f).ToList();
            var nearest = fronts.Min(f => Vector3.Distance(f, t.At));
            spotsOk &= nearest < 0.05f && g.AreaOpen(zm.AreaAt(t.At)) && t.LeaveRound == round + g.ZRules.TraderStayRounds && t.CameRound == round;
            var top = g.ZRules.TraderTopTier(RunTables.Current, round);
            var parts = Enumerable.Range(0, t.Count).Select(i => t.Stock[i]).Where(i => i.Kind == ZTraderItemKind.Part).ToList();
            var names = parts.Select(i => Parts.All[i.Part].Name).ToList();
            var distinct = names.Distinct().Count() == names.Count;
            var inTier = parts.All(i => Parts.All[i.Part].Rarity <= top && i.Price == g.ZRules.TraderPrice(Parts.All[i.Part].Rarity, round) && i.Left == 1);
            var consumables = Enumerable.Range(0, t.Count).Select(i => t.Stock[i]).Count(i => i.Kind != ZTraderItemKind.Part);
            var rarestFirst = parts.Count == 0 || Parts.All[parts[0].Part].Rarity == top;
            shelfOk &= parts.Count == g.ZRules.TraderStock && distinct && inTier && consumables == 2 && rarestFirst;
            visits.Add($"r{round}: visit {t.Visit} at {t.At.X:0.0},{t.At.Z:0.0} ({zm.Areas[zm.AreaAt(t.At)].Name}), leaves at {t.LeaveRound}; {parts.Count} parts up to {top} [{string.Join(", ", parts.Select(i => $"{Parts.All[i.Part].Name} {i.Price}"))}] + {consumables} consumables");
        }
        ZLine($"visits: here in rounds {string.Join(",", here)}; {string.Join("; ", visits)}");
        var want = new[] { 5, 10, 15 };
        if (!here.SequenceEqual(want) || !shelfOk || !spotsOk || !leftOk)
            Fail("the trader should come at rounds 5, 10 and 15 (and leave when the next round starts) at a box spot or workbench in an open area, with a fresh shelf of distinct parts up to the round's tier");
    }

    /// <summary>Purchases and refusals, the last item wanted by two at once, and the client's copy.</summary>
    private static void TraderShopping()
    {
        if (ZTestMap is not { } map || map.Zombies is null)
            return;
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var mate = g.SelfTestAddHuman();
        g.SelfTestSpawn(mate);
        mate.ImmuneUntil = 1e9;
        g.SelfTestBeginRound(5, now += 1);
        g.SelfTestEndRound(now); // (the break: no zombies to spoil the shopping)
        var t = g.ZTrader;
        if (!t.Here || t.Count < 3)
        {
            Fail("trader shopping: the trader should be here at round 5 with a shelf");
            return;
        }
        var stand = t.At + World.FlatForward(t.Yaw) * 1.1f;
        me.Teleport(stand);
        mate.Teleport(stand + new Vector3(0.3f, 0, 0));
        var zp = g.ZP(me);
        var zm = g.ZP(mate);
        zp.Points = 5000;
        zm.Points = 5000;
        var w = new PacketWriter();
        string? Buy(Player p, int item, int visit)
        {
            Game.WriteTraderBuy(w, item, visit);
            return g.SelfTestTraderPacket(p, w.Span, now);
        }

        // The prompt, then a part bought: points taken, the part in the loose parts, the shelf one short.
        var prompt = g.FindZUse(me);
        var item0 = t.Stock[0];
        var part0 = Parts.All[item0.Part];
        var why = Buy(me, 0, t.Visit);
        var bought = why == null && zp.Points == 5000 - item0.Price && zp.Parts.GetValueOrDefault(part0.Name) == 1 && t.Stock[0].SoldOut;
        var again = Buy(me, 0, t.Visit);
        var old = Buy(mate, 1, t.Visit + 1);
        zm.Points = 10;
        var poor = Buy(mate, 1, t.Visit);
        zm.Points = 5000;
        mate.Teleport(stand + new Vector3(6f, 0, 0));
        var far = Buy(mate, 1, t.Visit);
        mate.Teleport(stand + new Vector3(0.3f, 0, 0));
        var refusals = again != null && old != null && poor != null && far != null && zm.Points == 5000 && !t.Stock[1].SoldOut && zm.Parts.Count == 0;
        ZLine($"shopping: '{prompt?.Text}'; bought {part0.Name} ({part0.Rarity}) for {item0.Price}: '{why ?? "ok"}', points {zp.Points}, held {zp.Parts.GetValueOrDefault(part0.Name)}; refused: again '{again}', old shelf '{old}', poor '{poor}', far '{far}' (nothing taken {zm.Points == 5000})");
        if (prompt?.Kind != ZUseKind.Trader || !bought || !refusals)
            Fail("trader shopping: a purchase should take the points and give the part; a sold-out, old, unaffordable or out-of-reach purchase should be refused taking nothing");

        // The consumables: grenades only when short (the round's count), the reserve (Max Ammo).
        var gi = Enumerable.Range(0, t.Count).First(i => t.Stock[i].Kind == ZTraderItemKind.Grenades);
        var ai = Enumerable.Range(0, t.Count).First(i => t.Stock[i].Kind == ZTraderItemKind.Ammo);
        var full = Buy(me, gi, t.Visit);
        var pts = zp.Points;
        me.GrenadesLeft = 0;
        var refill = Buy(me, gi, t.Visit);
        var grenades = me.GrenadesLeft;
        var ammo = Buy(me, ai, t.Visit);
        var consumablesOk = full != null && refill == null && grenades == g.ZRules.GrenadesForRound(5) && ammo == null && zp.Points == pts - t.Stock[gi].Price - t.Stock[ai].Price
                            && !t.Stock[gi].SoldOut && !t.Stock[ai].SoldOut;
        ZLine($"consumables: grenades when full '{full}', when empty '{refill ?? "ok"}' -> {grenades} (round 5 gives {g.ZRules.GrenadesForRound(5)}); reserve '{ammo ?? "ok"}'; points {pts} -> {zp.Points} ({t.Stock[gi].Price} + {t.Stock[ai].Price}); still on the shelf {!t.Stock[gi].SoldOut && !t.Stock[ai].SoldOut}");
        if (!consumablesOk)
            Fail("trader shopping: the grenade refill should only sell when you're short, the reserve always, and neither should sell out");

        // Two players after the last one in the same tick: one gets it, one is told it's gone; exactly one part, one payment.
        var last = Enumerable.Range(0, t.Count).Last(i => t.Stock[i].Kind == ZTraderItemKind.Part && !t.Stock[i].SoldOut);
        var lastPart = Parts.All[t.Stock[last].Part];
        var price = t.Stock[last].Price;
        zp.Points = zm.Points = 5000;
        var a = Buy(me, last, t.Visit);
        var b = Buy(mate, last, t.Visit);
        var one = (a == null) != (b == null) && zp.Parts.GetValueOrDefault(lastPart.Name) + zm.Parts.GetValueOrDefault(lastPart.Name) == 1
                  && zp.Points + zm.Points == 10000 - price && t.Stock[last].SoldOut;
        ZLine($"the last {lastPart.Name}: host '{a ?? "ok"}', mate '{b ?? "ok"}'; parts given {zp.Parts.GetValueOrDefault(lastPart.Name) + zm.Parts.GetValueOrDefault(lastPart.Name)}, paid {10000 - zp.Points - zm.Points} (price {price})");
        if (!one)
            Fail("trader shopping: two players buying the last of something in one tick should get exactly one sale");

        // The client's copy: the whole shelf as sent, a purchase, then the departure.
        {
            var msg = new PacketWriter();
            g.SelfTestWriteTrader(msg);
            var shelf = msg.Span.ToArray();
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(map);
            c.SelfTestReadTrader(shelf, 100);
            var ct = c.ZTrader;
            var same = ct.Here && ct.Visit == t.Visit && ct.At == t.At && ct.Yaw == t.Yaw && ct.LeaveRound == t.LeaveRound && ct.CameRound == t.CameRound && ct.Count == t.Count
                       && Enumerable.Range(0, t.Count).All(i => ct.Stock[i].Kind == t.Stock[i].Kind && ct.Stock[i].Part == t.Stock[i].Part && ct.Stock[i].Price == t.Stock[i].Price && ct.Stock[i].Left == t.Stock[i].Left)
                       && Math.Abs(ct.ChangedAt - 100) < 1e-9;
            var prompted = false;
            if (c.Local is { } cl)
            {
                cl.Teleport(stand);
                prompted = c.FindZUse(cl)?.Kind == ZUseKind.Trader;
            }
            var bw = new PacketWriter();
            Game.WriteTraderBuy(bw, 1, t.Visit);
            var cbuy = g.SelfTestTraderPacket(me, bw.Span, now);
            g.SelfTestWriteTrader(msg);
            c.SelfTestReadTrader(msg.Span, 101);
            var soldSeen = cbuy == null && ct.Stock[1].SoldOut && Math.Abs(ct.ChangedAt - 100) < 1e-9; // (a purchase isn't an arrival)
            g.SelfTestBeginRound(6, now += 1);
            g.SelfTestWriteTrader(msg);
            c.SelfTestReadTrader(msg.Span, 102);
            var gone = !t.Here && !ct.Here && ct.Count == 0;
            ZLine($"client sync: the shelf ({shelf.Length} bytes, {(shelf.Length - 1 - Game.ZTraderHeader) / Game.ZTraderItemBytes} items) same {same}, prompt {prompted}, a sale seen {soldSeen}, gone at round 6 {gone}; protocol {ServerInfo.ProtocolVersion}, Msg.ZTrader {(byte)Msg.ZTrader}");
            if (!same || !prompted || !soldSeen || !gone)
                Fail("trader shopping: the client's copy should follow the shelf, its sales and the departure");
            if (ServerInfo.ProtocolVersion < 73)
                Fail("the trader changed the wire: the protocol should be at least 73");
        }
        World.SetMap(map);
    }

    /// <summary>A co-op bot plans a trader purchase when he's here and it has plenty to spare, walks there and buys one part; the tick allocates nothing.</summary>
    private static void TraderBot()
    {
        if (ZTestMap?.Zombies is null)
            return;
        var now = 0.0;
        using var g = ZGame(1, ref now, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.BreakSeconds = 120;
            r.ReadyUp = false;
            r.BotPerkOrder = [];
            r.BotWallBetter = 100;
        }, hostIn: false);
        var bot = g.Players.Values.First(p => p.Bot != null);
        var zp = g.ZP(bot);
        g.SelfTestBeginRound(5, now += 1);
        g.SelfTestEndRound(now);
        var t = g.ZTrader;
        var cheapest = Enumerable.Range(0, t.Count).Where(i => t.Stock[i].Kind == ZTraderItemKind.Part).Min(i => t.Stock[i].Price);
        zp.Points = g.ZRules.BotReserve + cheapest + g.ZRules.TraderBotSurplus + 50; // enough for one part with the surplus, not for the slot upgrade or a wall gun
        var planPoor = g.SelfTestBotPlan(bot, now);
        zp.Points = g.ZRules.BotReserve + cheapest + g.ZRules.TraderBotSurplus - 10;
        var planShort = g.SelfTestBotPlan(bot, now);
        zp.Points = g.ZRules.BotReserve + cheapest + g.ZRules.TraderBotSurplus + 50;
        var planKind = g.SelfTestBotPlan(bot, now); // (the probe leaves its plan with the bot: this one is the trader)
        var pointsBefore = zp.Points;
        var bought = false;
        var waited = 0.0;
        for (var s = 0.0; s < 25 && !bought; s += 1.0 / World.TickRate)
        {
            Run(g, ref now, 1.0 / World.TickRate);
            waited = s;
            bought = g.ZBotBought.Any(x => x.Contains("Trader"));
        }
        var sold = Enumerable.Range(0, t.Count).Count(i => t.Stock[i].Kind == ZTraderItemKind.Part && t.Stock[i].SoldOut);
        var partOk = bought && zp.Parts.Count == 1 && sold == 1 && zp.Points == pointsBefore - Enumerable.Range(0, t.Count).Where(i => t.Stock[i].SoldOut).Sum(i => t.Stock[i].Price)
                     && zp.Points >= g.ZRules.BotReserve;
        var planAfter = g.SelfTestBotPlan(bot, now);
        ZLine($"bot: plan with {g.ZRules.TraderBotSurplus} to spare {planKind}, 10 short {planShort}; bought in {waited:0.0} s {bought} ({string.Join(", ", g.ZBotBought)}): {sold} sold, holds {string.Join("+", zp.Parts.Keys)}, points {pointsBefore} -> {zp.Points}; plans again this visit {planAfter}");
        if (planPoor != ZUseKind.Trader || planKind != ZUseKind.Trader || planShort == ZUseKind.Trader || !partOk || planAfter == ZUseKind.Trader)
            Fail("a co-op bot should buy one part a visit from the trader when it has the surplus to spare (never twice a visit)");

        // Nothing allocated per tick while he's here (the break, the bot at rest).
        g.ZRules.BotsBuy = false;
        for (var i = 0; i < 30; i++)
            g.SelfTestZombiesTick(now += World.TickDt);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 300; i++)
            g.SelfTestZombiesTick(now += World.TickDt);
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        ZLine($"the Zombies tick with the trader here: {alloc} bytes in 300 ticks");
        if (alloc > 0)
            Fail("the trader should add nothing to the tick's allocations");
    }
}
