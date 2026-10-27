using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies casino" (Source/Game/ZombiesCasino.cs, Casino.cs; docs/zombies/ZOMBIES-MODE.md, "Casino
/// tables"): the rules load (and a broken casino section is refused); the payout tables and house edges worked out
/// exactly (every roulette bet covers what it says and returns 36 in 37; high-low pays (1 - edge) per guess; the pass
/// line wins 244 in 495; a seeded blackjack run loses a little); the blackjack dealer's rules (totals, soft hands, the
/// dealer standing on every 17, 3:2 naturals, pushes, the dealer's peek); then Blackmoor Asylum's four tables, each its
/// prop in its own room: stacked blackjack deals (a natural, a hit to 21, a double, a soft 17, the dealer's natural, a
/// hand left to time out, two seats at once), roulette spins paying exactly what the board says for two players, high-
/// low runs and dice hands replayed against the rules, points never going below zero (and nothing taken for a refused
/// bet), forfeits (walking away, going down) and bets coming back before a deal, the client's copy of every table (the
/// dealer's hole card hidden until it's turned) and a big win everyone sees, and no allocation in the tables' tick.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesCasino()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "casino rules");
        ZLine($"casino rules: blackjack {rules.CasinoMin[0]}-{rules.CasinoMax[0]}, roulette {rules.CasinoMin[1]}-{rules.CasinoMax[1]}, high-low {rules.CasinoMin[2]}-{rules.CasinoMax[2]} (edge {rules.HighLowEdge}), dice {rules.CasinoMin[3]}-{rules.CasinoMax[3]}; use {rules.CasinoUseRange} m, forfeit past {rules.CasinoLeaveRange} m");
        var refused = 0;
        foreach (var bad in new[] { """{"blackjack": {"min": 500, "max": 100}}""", """{"useRange": 5, "leaveRange": 2}""", """{"roulette": {"most": 3}}""" })
            try
            {
                ZombiesRules.Load($$"""{"format": "call-of-slop-zombies/1", "casino": {{bad}}}""", "bad casino");
            }
            catch (WeaponFileException)
            {
                refused++;
            }
        if (refused != 3 || rules.CasinoMin.Length != 4 || rules.CasinoMax[1] < rules.CasinoMin[1])
            Fail($"the casino rules should load, and a broken casino section should be refused ({refused}/3 refused)");

        CasinoMaths(rules);
        CasinoBlackjackRules();
        CasinoTables();
        CasinoLogic();
    }

    /// <summary>A casino table is a map logic "zmachine" (machine "casino"): disabled, it's locked and takes no bets; enabled again, it plays.</summary>
    private static void CasinoLogic()
    {
        if (ZTestMap?.Zombies is not { } zt)
            return;
        var room = zt.Areas[zt.StartArea];
        var at = new Vector2((room.X0 + room.X1) / 2 + 1.5f, (room.Z0 + room.Z1) / 2 + 1.5f);
        var text = File.ReadAllText(Path.Combine(Maps.Folder, "Dev", "ZombiesTest.json")).TrimEnd();
        text = text.Replace("\"zombies\": {", $"\"zombies\": {{ \"casino\": [{{ \"game\": \"dice\", \"at\": [{at.X}, {at.Y}] }}],");
        text = text[..text.LastIndexOf('}')] + $", \"logic\": {{ \"entities\": [ {{ \"type\": \"zmachine\", \"name\": \"Dice\", \"machine\": \"casino\", \"at\": [{at.X}, {at.Y}], \"enabled\": false }} ] }} }}";
        Map map;
        try
        {
            map = MapFile.Load(text, "Dev/ZombiesTestCasino.json");
        }
        catch (MapFileException ex)
        {
            Fail($"casino logic: the test map doesn't load: {ex.Message}");
            return;
        }
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, setup: game => World.SetMap(map));
        var me = g.Local!;
        var zp = g.ZP(me);
        zp.Points = 1000;
        me.Teleport(new Vector3(at.X, 0, at.Y - 1.1f));
        var locked = g.FindZUse(me) is { Kind: ZUseKind.Casino, Text: "Locked", Blocked: true };
        var refused = g.ServerCasino(me, 0, CasinoAct.Bet, 0, 100, now) != null && zp.Points == 1000;
        g.SelfTestLogicDeliver(map.Logic!.Find("Dice"), LogicAction.Enable, LogicSignal.Pulse(me.Id));
        g.SelfTestLogicTick(); // (delivered on the next logic tick)
        var use = g.FindZUse(me);
        var bet = g.ServerCasino(me, 0, CasinoAct.Bet, 0, 100, now);
        var open = use is { Kind: ZUseKind.Casino, Blocked: false } && bet == null && zp.Points == 900;
        ZLine($"casino logic: a disabled zmachine \"casino\" locks its table {locked} and takes no bet {refused}; enabled, it plays {open}{(open ? "" : $" (prompt '{use?.Text}', bet '{bet}')")}");
        if (!locked || !refused || !open)
            Fail("a casino table should be a map logic zmachine: locked while disabled, playable once enabled");
        World.SetMap(Maps.All[0]);
    }

    // ---- The maths: every payout table and house edge, exactly ----

    private static void CasinoMaths(ZombiesRules rules)
    {
        // Roulette: every spot covers what it says, and every bet returns 36 for each 37 staked over the wheel.
        var badSpots = new List<string>();
        var splits = new HashSet<(int, int)>();
        for (var s = 0; s < Casino.SpotCount; s++)
        {
            var covered = 0;
            for (var n = 0; n <= 36; n++)
                if (Casino.Covers(s, n))
                    covered++;
            var want = s <= 36 ? 1 : s < Casino.SpotRed ? 2 : s < Casino.SpotDozen ? 18 : 12;
            if (covered != want || (Casino.Payout(s) + 1) * covered != 36 || Casino.Covers(s, 0) != (s == 0))
                badSpots.Add($"{Casino.SpotName(s)} covers {covered}, pays {Casino.Payout(s)}:1");
            if (s is >= Casino.SpotHSplit and < Casino.SpotRed)
            {
                int a = -1, b = -1;
                for (var n = 1; n <= 36; n++)
                    if (Casino.Covers(s, n))
                        (a, b) = a < 0 ? (n, -1) : (a, n);
                splits.Add((a, b));
                if (Casino.SplitSpot(a, b) != s || Casino.SplitSpot(b, a) != s)
                    badSpots.Add($"split {Casino.SpotName(s)} isn't found again from its numbers");
            }
        }
        var adjacent = 0;
        for (var a = 1; a <= 36; a++)
            for (var b = a + 1; b <= 36; b++)
                if ((b == a + 1 && a % 3 != 0) || b == a + 3)
                {
                    adjacent++;
                    if (!splits.Contains((a, b)))
                        badSpots.Add($"no split for {a}/{b}");
                }
        if (Casino.SplitSpot(3, 4) >= 0 || Casino.SplitSpot(1, 5) >= 0)
            badSpots.Add("a split between numbers that aren't side by side");
        var reds = Enumerable.Range(1, 36).Count(Casino.IsRed);
        var wheel = Casino.WheelOrder.Distinct().Count();
        ZLine($"roulette: {Casino.SpotCount} spots, {splits.Count} splits ({adjacent} side-by-side pairs), {reds} red numbers, {wheel} pockets; every bet returns 36 in 37 (house edge {1 / 37.0 * 100:0.00} percent); problems: {(badSpots.Count == 0 ? "none" : string.Join("; ", badSpots.Take(5)))}");
        if (badSpots.Count > 0 || splits.Count != 57 || adjacent != 57 || reds != 18 || wheel != 37 || Casino.WheelOrder.Length != 37)
            Fail("roulette's board should cover exactly what each bet says (straight 1, split 2, even money 18, dozens and columns 12; the zero only straight up) with the standard payouts, so each bet keeps 1 in 37 for the house");

        // High-low: every guess pays (1 - edge) over its chance, rounded down; the impossible ones can't be made.
        var worst = 0.0;
        var best = 0.0;
        var hlOk = Casino.HighLowMultiplier(12, true, rules.HighLowEdge) == 0 && Casino.HighLowMultiplier(0, false, rules.HighLowEdge) == 0;
        worst = 1;
        for (var r = 0; r <= 12; r++)
            foreach (var hi in new[] { true, false })
            {
                var m = Casino.HighLowMultiplier(r, hi, rules.HighLowEdge);
                if (m == 0)
                    continue;
                var p = (hi ? 12 - r : r) / 13.0;
                var ev = p * m / 100.0;
                worst = Math.Min(worst, ev);
                best = Math.Max(best, ev);
            }
        hlOk &= best <= 1 - rules.HighLowEdge + 1e-6 && worst >= 1 - rules.HighLowEdge - 0.02 && Casino.HighLowPot(100, 190) == 190 && Casino.HighLowPot(55, 101) == 55;
        ZLine($"high-low: a guess returns {worst:0.0000} to {best:0.0000} of the pot (edge {rules.HighLowEdge}); from a two, higher pays x{Casino.HighLowMultiplier(0, true, rules.HighLowEdge) / 100f:0.00}; from a seven either way x{Casino.HighLowMultiplier(5, true, rules.HighLowEdge) / 100f:0.00}");
        if (!hlOk)
            Fail("high-low should pay (1 - edge) over each guess's chance (never more), and refuse higher than an ace and lower than a two");

        // Bone Dice: the pass line wins exactly 244 in 495 (a 1.41 percent house edge at even money).
        var win = 0.0;
        for (var a = 1; a <= 6; a++)
            for (var b = 1; b <= 6; b++)
            {
                var point = 0;
                var res = Casino.PassLine(ref point, a + b);
                if (res > 0)
                    win += 1 / 36.0;
                else if (res == 0)
                {
                    var ways = 6 - Math.Abs(point - 7);
                    // From here: the point (ways in 36) before a 7 (6 in 36), everything else rolling on (checked with the function itself).
                    var pp = point;
                    var hit = Casino.PassLine(ref pp, point) > 0 && Casino.PassLine(ref pp, 7) < 0 && Casino.PassLine(ref pp, point == 6 ? 8 : 6) == 0;
                    if (hit)
                        win += 1 / 36.0 * ways / (ways + 6.0);
                }
            }
        ZLine($"Bone Dice: the pass line wins {win:0.00000} of hands (244/495 = {244 / 495.0:0.00000}; house edge {(1 - 2 * win) * 100:0.00} percent)");
        if (Math.Abs(win - 244 / 495.0) > 1e-9)
            Fail("the pass line should win 244 hands in 495 (7 or 11 on the come-out, then the point before a 7)");

        // Blackjack: a seeded run of hands playing the dealer's own rule loses a little to the house (no free money).
        var deck = new byte[52];
        var rng = 12345u;
        var staked = 0L;
        var back = 0L;
        Span<byte> me = stackalloc byte[12];
        Span<byte> dealer = stackalloc byte[12];
        for (var hand = 0; hand < 20000; hand++)
        {
            Casino.Shuffle(deck, ref rng);
            var k = 0;
            int nm = 0, nd = 0;
            me[nm++] = deck[k++];
            dealer[nd++] = deck[k++];
            me[nm++] = deck[k++];
            dealer[nd++] = deck[k++];
            if (!Casino.IsBlackjack(dealer[..nd]))
                while (Casino.HandTotal(me[..nm]).Total < 17 && !Casino.IsBlackjack(me[..nm]))
                    me[nm++] = deck[k++];
            if (Casino.HandTotal(me[..nm]).Total <= 21 && !Casino.IsBlackjack(me[..nm]) && !Casino.IsBlackjack(dealer[..nd]))
                while (Casino.DealerDraws(dealer[..nd]))
                    dealer[nd++] = deck[k++];
            staked += 100;
            back += Casino.BlackjackReturn(me[..nm], dealer[..nd], 100);
        }
        var edge = 1 - back / (double)staked;
        ZLine($"blackjack: 20000 seeded hands drawing to 17 return {back / (double)staked:0.0000} of the stakes (house edge {edge * 100:0.0} percent)");
        if (edge is < 0.01 or > 0.12)
            Fail($"blackjack played the dealer's way should lose a little to the house ({edge * 100:0.0} percent)");
    }

    private static byte C(string s)
    {
        // "As", "10h", "Kd": a card for the stacked decks.
        var rank = Array.IndexOf(new[] { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" }, s[..^1]);
        var suit = "hdcs".IndexOf(s[^1]);
        return (byte)(rank * 4 + suit);
    }

    private static void CasinoBlackjackRules()
    {
        (int, bool) T(params string[] cards) => Casino.HandTotal(cards.Select(C).ToArray());
        int R(string[] p, string[] d, int bet) => Casino.BlackjackReturn(p.Select(C).ToArray(), d.Select(C).ToArray(), bet);
        var checks = new (string What, bool Ok)[]
        {
            ("A K is 21, a natural", T("As", "Kd") == (21, true) && Casino.IsBlackjack(new[] { C("As"), C("Kd") })),
            ("A A 9 is a soft 21", T("As", "Ah", "9c") == (21, true)),
            ("A 6 10 is a hard 17", T("As", "6h", "10c") == (17, false)),
            ("K Q 2 busts at 22", T("Ks", "Qh", "2c").Item1 == 22),
            ("A 6 is a soft 17", T("As", "6d") == (17, true)),
            ("three cards to 21 aren't a natural", !Casino.IsBlackjack(new[] { C("7s"), C("7h"), C("7c") })),
            ("the dealer stands on a soft 17", !Casino.DealerDraws(new[] { C("As"), C("6d") })),
            ("the dealer draws on 16", Casino.DealerDraws(new[] { C("10s"), C("6d") })),
            ("the dealer stands on a hard 17", !Casino.DealerDraws(new[] { C("10s"), C("7d") })),
            ("a natural pays 3:2", R(["As", "Kd"], ["10s", "Qd"], 100) == 250),
            ("a natural against a natural pushes", R(["As", "Kd"], ["Ah", "Jd"], 100) == 100),
            ("the dealer's natural beats 21", R(["7s", "7d", "7h"], ["Ah", "Jd"], 100) == 0),
            ("20 beats 19 at even money", R(["10s", "Kd"], ["10h", "9d"], 100) == 200),
            ("20 against 20 pushes", R(["10s", "Kd"], ["10h", "Qd"], 100) == 100),
            ("a bust loses even when the dealer busts", R(["10s", "Kd", "5c"], ["10h", "6d", "Kc"], 100) == 0),
            ("18 wins when the dealer busts", R(["10s", "8d"], ["10h", "6d", "Kc"], 100) == 200),
            ("a doubled 21 pays even money on the doubled stake", R(["5s", "6d", "10c"], ["10h", "Qd"], 200) == 400),
        };
        var bad = checks.Where(c => !c.Ok).Select(c => c.What).ToList();
        ZLine($"blackjack rules: {checks.Length - bad.Count}/{checks.Length} hold{(bad.Count > 0 ? $" (wrong: {string.Join("; ", bad)})" : "")}");
        if (bad.Count > 0)
            Fail("blackjack's totals and payouts should follow the rules (soft aces, the dealer standing on every 17, naturals 3:2, pushes)");
    }

    // ---- The real tables on Blackmoor Asylum ----

    private static void CasinoTables()
    {
        if (Maps.Find("Blackmoor Asylum") is not { Zombies: { } bm } bmap)
        {
            Fail("Blackmoor Asylum is missing");
            return;
        }
        var games = bm.CasinoTables.Select(t => (CasinoGame)t.Game).ToList();
        var areas = bm.CasinoTables.Select(t => t.Area).Distinct().Count();
        var props = bm.CasinoTables.Count(t => bmap.PropInstances.Any(p => p.Def.Name == ZombiesMap.MachineProp.CasinoTableFor(t.Game) && Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(t.At.X, t.At.Z)) < 0.01f));
        ZLine($"Blackmoor Asylum: {bm.CasinoTables.Count} casino tables ({string.Join(", ", bm.CasinoTables.Select(t => $"{CasinoGames.Name(t.Game)} in {bm.Areas[t.Area].Name}"))}), {props} standing as their props");
        if (games.Distinct().Count() != 4 || areas != 4 || props != bm.CasinoTables.Count || !bm.CasinoTables.Any(t => t.Area == bm.StartArea))
            Fail("Blackmoor Asylum should have all four casino tables, each its own prop in its own room (one in the start room)");
        // The map file's "casino" list: a table's own limits read; an unknown game, limits the wrong way round or an unknown key refused.
        var mapRefused = 0;
        var scratch = ZTestMap ?? bmap;
        foreach (var entry in new[] { """{"game": "poker", "at": [0, 0]}""", """{"game": "dice", "at": [0, 0], "min": 500, "max": 100}""", """{"game": "dice", "at": [0, 0], "odds": 3}""", """{"game": "highLow", "at": [1, 2], "yaw": 90, "min": 100, "max": 2000, "power": true}""" })
        {
            using var doc = System.Text.Json.JsonDocument.Parse($$"""{"areas": [{"name": "Room", "rect": [-5, -5, 5, 5]}], "spawners": [{"area": "Room", "at": [0, 0]}], "casino": [{{entry}}]}""");
            try
            {
                var z = ZombiesMap.Read(doc.RootElement, scratch, "casino test");
                if (z.CasinoTables is not [{ Game: (int)CasinoGame.HighLow, BetMin: 100, BetMax: 2000, NeedsPower: true }])
                    Fail("a map's casino table should keep its game, limits and power flag");
            }
            catch (MapFileException)
            {
                mapRefused++;
            }
        }
        if (mapRefused != 3)
            Fail($"a map with a broken casino table should be refused ({mapRefused}/3)");
        var now = 0.0;
        using var g = ZGame(1, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: bmap);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var zp = g.ZP(me);
        var rules = g.ZRules;
        int Table(CasinoGame game) => bm.CasinoTables.FindIndex(t => t.Game == (int)game);
        void StandAt(Player p, int i, float side = 0)
        {
            var s = bm.CasinoTables[i];
            var fwd = World.FlatForward(s.Yaw);
            var right = new Vector3(-fwd.Z, 0, fwd.X);
            p.Teleport(s.At + fwd * 1.1f + right * side);
        }
        void Tick(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.25)
                g.SelfTestTickCasino(now += 0.25);
        }

        // ---- Blackjack ----
        var bj = Table(CasinoGame.Blackjack);
        var t0 = g.ZCasino[bj];
        StandAt(me, bj);
        var prompt = g.FindZUse(me);
        var (min, max) = g.CasinoLimits(bm, bj);
        zp.Points = min - 10;
        var poor = g.ServerCasino(me, bj, CasinoAct.Bet, 0, min, now);
        var poorKept = zp.Points == min - 10 && !t0.Seats.Any(s => s.Taken);
        zp.Points = 100000;
        var odd = g.ServerCasino(me, bj, CasinoAct.Bet, 0, min + 5, now);
        var over = g.ServerCasino(me, bj, CasinoAct.Bet, 0, max + 10, now);
        var held = zp.Points;
        g.SelfTestUse(me, now); // holding the interact key at a table buys nothing
        g.SelfTestUse(me, now + 2);
        var useBuysNothing = zp.Points == held;
        // A bet before the deal comes back with Leave (and when you walk away).
        var p0 = zp.Points;
        var sat = g.ServerCasino(me, bj, CasinoAct.Bet, 0, 100, now) == null && zp.Points == p0 - 100 && t0.Phase == CasinoPhase.Betting;
        var left = g.ServerCasino(me, bj, CasinoAct.Leave, 0, 0, now) == null && zp.Points == p0 && t0.Phase == CasinoPhase.Open;
        g.ServerCasino(me, bj, CasinoAct.Bet, 0, 100, now);
        me.Teleport(new Vector3(0, 0, -2));
        Tick(0.25);
        var walkedBack = zp.Points == p0 && t0.Phase == CasinoPhase.Open;
        StandAt(me, bj);
        ZLine($"blackjack table: prompt '{prompt?.Text}'; with {min - 10} points a {min} bet: '{poor}' (points kept {poorKept}); {min + 5}: '{odd}'; {max + 10}: '{over}'; holding the key buys nothing {useBuysNothing}; sit {sat}, leave with the bet {left}, walk away before the deal: bet back {walkedBack}");
        if (prompt is not { Kind: ZUseKind.Casino } || !prompt.Value.Text.Contains("Blackjack") || poor == null || !poorKept || odd == null || over == null || !useBuysNothing || !sat || !left || !walkedBack)
            Fail("a blackjack table should offer itself to the interact key, refuse bets you can't cover (taking nothing) or outside its limits and steps, and give a bet back before the deal");

        // Stacked deals (cards in dealing order: seat, dealer up, seat, dealer hole, then the draws).
        string Hand(string[] deck, Action<CasinoTable>? play, out int delta, out (CasinoOutcome Outcome, int Count, int Bet, bool Doubled) seat)
        {
            var before = zp.Points;
            g.ServerCasino(me, bj, CasinoAct.Bet, 0, 100, now);
            g.SelfTestStackedDeck = deck.Select(C).ToArray();
            Tick(rules.CasinoDealSeconds + 0.3);
            play?.Invoke(t0);
            if (t0.Phase == CasinoPhase.Playing)
                Tick(rules.CasinoTurnSeconds + 0.5);
            var st = t0.Seats[t0.SeatOf(me.Id)];
            seat = (st.Outcome, st.Count, st.Bet, st.Doubled); // (the seat clears when the table opens again)
            delta = zp.Points - before;
            var text = $"{seat.Outcome} {delta:+0;-0;0} (dealer {Casino.HandTotal(t0.DealerHand).Total} in {t0.DealerCount})";
            Tick(rules.CasinoShowSeconds + 0.5);
            return text;
        }
        var natural = Hand(["As", "9d", "Kh", "7c"], null, out var dNat, out var sNat);
        var hit21 = Hand(["10h", "10d", "6c", "7s", "5h", "Kd"], t => g.ServerCasino(me, bj, CasinoAct.Hit, 0, 0, now), out var dHit, out var sHit);
        var doubled = Hand(["5s", "10d", "6c", "6s", "10h", "Kd"], t => g.ServerCasino(me, bj, CasinoAct.Double, 0, 0, now), out var dDbl, out var sDbl);
        var soft17 = Hand(["10s", "Ad", "8c", "6s", "9h"], t => g.ServerCasino(me, bj, CasinoAct.Stand, 0, 0, now), out var dS17, out _);
        var peek = Hand(["10s", "Ad", "9c", "Ks"], null, out var dPeek, out _);
        var peekCards = t0.DealerCount; // (cleared by now)
        var timeout = Hand(["10s", "10d", "9c", "8s", "5h"], null, out var dOut, out var sOut);
        var bjOk = sNat.Outcome == CasinoOutcome.Blackjack && dNat == 150 && sHit.Outcome == CasinoOutcome.Win && dHit == 100 && sHit.Count == 3
                   && sDbl.Doubled && sDbl.Bet == 200 && dDbl == 200 && dS17 == 100 && soft17.Contains("in 2") && dPeek == -100 && peek.Contains("in 2")
                   && sOut.Outcome == CasinoOutcome.Win && dOut == 100;
        ZLine($"blackjack hands: natural {natural}; hit to 21 {hit21}; double {doubled}; dealer soft 17 {soft17}; dealer natural {peek}; left to time out (stands) {timeout}");
        if (!bjOk)
            Fail("blackjack at the table should pay a natural 3:2, deal a hit, double the stake for one card, have the dealer stand on a soft 17, end at once on the dealer's natural and stand a hand that runs out of time");

        // A forfeit (walking away mid-hand, or going down), the client copy with the hole card hidden, two seats at once.
        var bot = g.Players.Values.FirstOrDefault(p => p != me);
        var botZp = bot != null ? g.ZP(bot) : null;
        var pf = zp.Points;
        g.ServerCasino(me, bj, CasinoAct.Bet, 0, 100, now);
        var twoSeats = false;
        if (bot is { Alive: true } && botZp != null)
        {
            botZp.Points = 1000;
            StandAt(bot, bj, 0.8f);
            twoSeats = g.ServerCasino(bot, bj, CasinoAct.Bet, 0, 200, now) == null && t0.SeatOf(bot.Id) >= 0 && botZp.Points == 800;
        }
        g.SelfTestStackedDeck = new[] { "10s", "9h", "10d", "6c", "6s", "8h" }.Select(C).ToArray();
        Tick(rules.CasinoDealSeconds + 0.3);
        var w = new PacketWriter();
        g.SelfTestWriteCasino(w, bj);
        var msg = w.Span.ToArray();
        using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: bmap);
        World.SetMap(bmap);
        c.SelfTestReadCasino(msg);
        var ct = c.ZCasino[bj];
        var mine = t0.SeatOf(me.Id);
        var holeHidden = ct.DealerCount == 2 && ct.Dealer[0] == t0.Dealer[0] && ct.Dealer[1] == Casino.NoCard && !ct.HoleShown && t0.Dealer[1] != Casino.NoCard;
        var syncOk = ct.Phase == CasinoPhase.Playing && ct.Game == CasinoGame.Blackjack && ct.RoundId == t0.RoundId && ct.Seats[mine].Player == me.Id
                     && ct.Seats[mine].Hand.SequenceEqual(t0.Seats[mine].Hand) && ct.Seats[mine].Bet == 100 && MathF.Abs(ct.Left - t0.Left) <= 0.11f;
        g.ZP(me).Downed = true; // going down forfeits the hand
        Tick(0.25);
        g.ZP(me).Downed = false;
        var forfeited = t0.Seats[mine].Outcome == CasinoOutcome.Forfeit;
        if (bot != null && t0.SeatOf(bot.Id) >= 0)
            g.ServerCasino(bot, bj, CasinoAct.Stand, 0, 0, now);
        Tick(0.25);
        var botPaid = bot == null || botZp == null || !twoSeats || botZp.Points == 800 + t0.Seats[t0.SeatOf(bot.Id)].Paid;
        var lostBet = zp.Points == pf - 100;
        g.SelfTestWriteCasino(w, bj);
        c.SelfTestReadCasino(w.Span.ToArray());
        var turned = ct.HoleShown && ct.Dealer[1] == t0.Dealer[1] && ct.Seats[mine].Outcome == CasinoOutcome.Forfeit;
        Tick(rules.CasinoShowSeconds + 0.5);
        ZLine($"blackjack: two seats at once {twoSeats} (the bot paid {botPaid}); going down forfeits {forfeited} (bet lost {lostBet}); the client's copy {syncOk}, hole card hidden {holeHidden} then shown {turned}");
        if (!forfeited || !lostBet || !syncOk || !holeHidden || !turned || !botPaid || (bot is { Alive: true } && !twoSeats))
            Fail("a blackjack hand should be forfeit when its player goes down, other seats play on, and clients should see the table as the server has it with the dealer's hole card hidden until it's turned (protocol 66)");

        // ---- Roulette: several players' chips on one spin, each paid exactly what the board says ----
        var ro = Table(CasinoGame.Roulette);
        var rt = g.ZCasino[ro];
        StandAt(me, ro);
        var spots = new[] { 17, Casino.SplitSpot(8, 11), Casino.SpotRed, Casino.SpotDozen + 1, Casino.SpotColumn, Casino.SpotOdd, 0 };
        var amounts = new[] { 10, 20, 50, 30, 40, 50, 10 };
        var (rmin, rmax) = g.CasinoLimits(bm, ro);
        var spins = 0;
        var payOk = true;
        var numbers = new HashSet<int>();
        var tooMuch = g.ServerCasino(me, ro, CasinoAct.Bet, Casino.SpotRed, rmax + 10, now);
        var badSpot = g.ServerCasino(me, ro, CasinoAct.Bet, Casino.SpotCount, 10, now);
        var pc = zp.Points;
        g.ServerCasino(me, ro, CasinoAct.Bet, Casino.SpotBlack, 100, now);
        var cleared = g.ServerCasino(me, ro, CasinoAct.Clear, 0, 0, now) == null && zp.Points == pc && rt.BetCount == 0 && rt.Phase == CasinoPhase.Open;
        var botBets = bot is { Alive: true } && botZp != null;
        if (botBets)
        {
            StandAt(bot!, ro, 0.9f);
            botZp!.Points = 100000;
        }
        var bothPaid = 0;
        for (var spin = 0; spin < 30; spin++)
        {
            var before = zp.Points;
            var botBefore = botZp?.Points ?? 0;
            for (var k = 0; k < spots.Length; k++)
                payOk &= g.ServerCasino(me, ro, CasinoAct.Bet, spots[k], amounts[k], now) == null;
            if (botBets)
                g.ServerCasino(bot!, ro, CasinoAct.Bet, Casino.SpotBlack, 100, now);
            var staked = amounts.Sum();
            payOk &= zp.Points == before - staked && rt.BetsOf(me.Id) == staked;
            Tick(rules.CasinoRouletteBetSeconds + 0.3);
            payOk &= rt.Phase == CasinoPhase.Spinning && rt.Number <= 36;
            Tick(rules.CasinoSpinSeconds + 0.3);
            var n = rt.Number;
            numbers.Add(n);
            var want = 0;
            for (var k = 0; k < spots.Length; k++)
                if (Casino.Covers(spots[k], n))
                    want += amounts[k] * (Casino.Payout(spots[k]) + 1);
            payOk &= rt.Phase == CasinoPhase.Settled && zp.Points == before - staked + want;
            if (botBets && botZp!.Points == botBefore - 100 + (Casino.IsBlack(n) ? 200 : 0))
                bothPaid++;
            Tick(rules.CasinoShowSeconds + 0.3);
            spins++;
        }
        ZLine($"roulette: {spins} spins landed on {numbers.Count} different numbers; every spin paid exactly what the board says {payOk}; a second player paid on the same spin {bothPaid}/{(botBets ? spins : 0)}; over the limit '{tooMuch}', off the board '{badSpot}', chips taken back {cleared}");
        if (!payOk || numbers.Count < 10 || tooMuch == null || badSpot == null || !cleared || (botBets && bothPaid != spins))
            Fail("roulette should take chips from several players for one spin, pay each exactly as the board says, refuse bets past the table's limit or off the board, and give chips back before the spin");

        // ---- High-low: the pot follows the multipliers; a loss pays nothing; walking away forfeits ----
        var hl = Table(CasinoGame.HighLow);
        var ht = g.ZCasino[hl];
        StandAt(me, hl);
        var hands = 0;
        var hlOk = true;
        var wins = 0;
        var losses = 0;
        var cashed = 0;
        for (var h = 0; h < 40; h++)
        {
            var before = zp.Points;
            hlOk &= g.ServerCasino(me, hl, CasinoAct.Bet, 0, 100, now) == null && ht.Pot == 100;
            var s = ht.Seats[0];
            for (var guess = 0; guess < 10 && ht.Phase == CasinoPhase.Playing; guess++)
            {
                var cur = Casino.Rank(s.Cards[s.Count - 1]);
                if (h % 3 == 0 && guess == 1)
                {
                    g.ServerCasino(me, hl, CasinoAct.CashOut, 0, 0, now);
                    break;
                }
                var higher = cur < 6;
                var mult = Casino.HighLowMultiplier(cur, higher, rules.HighLowEdge);
                var pot = ht.Pot;
                g.ServerCasino(me, hl, higher ? CasinoAct.Higher : CasinoAct.Lower, 0, 0, now);
                var next = Casino.Rank(s.Cards[s.Count - 1]);
                var right = higher ? next > cur : next < cur;
                hlOk &= right ? ht.Pot == Casino.HighLowPot(pot, mult) : s.Outcome == CasinoOutcome.Lose && ht.Phase == CasinoPhase.Settled;
            }
            if (ht.Phase == CasinoPhase.Playing)
                g.ServerCasino(me, hl, CasinoAct.CashOut, 0, 0, now);
            var delta = zp.Points - before;
            hlOk &= ht.Phase == CasinoPhase.Settled && (s.Outcome == CasinoOutcome.Lose ? delta == -100 : delta == ht.Pot - 100);
            if (s.Outcome == CasinoOutcome.Lose)
                losses++;
            else if (delta > 0)
                wins++;
            else
                cashed++;
            Tick(rules.CasinoShowSeconds + 0.3);
            hands++;
        }
        // An ace can't go higher; a hand left alone cashes out; walking away loses it.
        g.ServerCasino(me, hl, CasinoAct.Bet, 0, 100, now);
        ht.Seats[0].Cards[ht.Seats[0].Count - 1] = C("As");
        var aceHigher = g.ServerCasino(me, hl, CasinoAct.Higher, 0, 0, now);
        var pt = zp.Points;
        Tick(rules.CasinoActSeconds + 0.5);
        var timedOut = ht.Seats[0].Outcome == CasinoOutcome.Push && zp.Points == pt + 100;
        Tick(rules.CasinoShowSeconds + 0.3);
        pt = zp.Points;
        g.ServerCasino(me, hl, CasinoAct.Bet, 0, 100, now);
        me.Teleport(new Vector3(0, 0, -2));
        Tick(0.25);
        var hlForfeit = ht.Seats[0].Outcome == CasinoOutcome.Forfeit && zp.Points == pt - 100;
        Tick(rules.CasinoShowSeconds + 0.3);
        ZLine($"high-low: {hands} hands ({wins} won, {cashed} cashed out even, {losses} lost), the pot followed the multipliers {hlOk}; higher than an ace '{aceHigher}'; left alone cashes out {timedOut}; walking away forfeits {hlForfeit}");
        if (!hlOk || wins == 0 || losses == 0 || aceHigher == null || !timedOut || !hlForfeit)
            Fail("high-low should multiply the pot on each right guess as its prices say, pay nothing on a wrong one (or a tie), cash out a hand left alone and forfeit one walked away from");

        // ---- Bone Dice: every hand replayed against the pass line ----
        var di = Table(CasinoGame.Dice);
        var dt = g.ZCasino[di];
        StandAt(me, di);
        var diceOk = true;
        int dw = 0, dl = 0, points = 0;
        for (var h = 0; h < 60; h++)
        {
            var before = zp.Points;
            diceOk &= g.ServerCasino(me, di, CasinoAct.Bet, 0, 50, now) == null;
            var replay = 0;
            var res = 0;
            for (var roll = 0; roll < 200 && dt.Phase == CasinoPhase.Playing; roll++)
            {
                var id = dt.RollId;
                g.ServerCasino(me, di, CasinoAct.Roll, 0, 0, now);
                diceOk &= dt.RollId == (byte)(id + 1) && dt.Die1 is >= 1 and <= 6 && dt.Die2 is >= 1 and <= 6;
                res = Casino.PassLine(ref replay, dt.Die1 + dt.Die2);
                diceOk &= res != 0 || dt.Point == replay;
            }
            if (replay != 0)
                points++;
            var s = dt.Seats[0];
            diceOk &= res > 0 ? s.Outcome == CasinoOutcome.Win && zp.Points == before + 50 : s.Outcome == CasinoOutcome.Lose && zp.Points == before - 50;
            if (res > 0)
                dw++;
            else
                dl++;
            Tick(rules.CasinoShowSeconds + 0.3);
        }
        ZLine($"Bone Dice: 60 hands ({dw} won, {dl} lost, {points} went to a point), each replayed against the pass line {diceOk}");
        if (!diceOk || dw == 0 || dl == 0 || points == 0)
            Fail("Bone Dice should play the pass line exactly: naturals and craps on the come-out, then the point before a 7, paying even money");

        // ---- A big win everyone sees; points never below zero; no allocation in the tick ----
        var wsBefore = t0.WinSeq;
        StandAt(me, bj);
        zp.Points = 5000;
        var big = Math.Min(1000, max);
        g.ServerCasino(me, bj, CasinoAct.Bet, 0, big, now);
        g.SelfTestStackedDeck = new[] { "Ah", "9d", "Qh", "7c" }.Select(C).ToArray();
        Tick(rules.CasinoDealSeconds + 0.3);
        var bigWin = t0.WinSeq != wsBefore && t0.WinPlayer == me.Id && t0.WinAmount == big * 3 / 2 && (big * 3 / 2 < rules.CasinoAnnounceAt || g.CasinoWinShowing(bj, Brutal.GlfwApi.Glfw.Time));
        g.SelfTestWriteCasino(w, bj);
        var feedBefore = c.Feed.Count;
        c.SelfTestReadCasino(w.Span.ToArray());
        var clientSaw = c.ZCasino[bj].WinSeq == t0.WinSeq && c.ZCasino[bj].WinAmount == t0.WinAmount && c.Feed.Count > feedBefore;
        Tick(rules.CasinoShowSeconds + 0.3);
        var neverNegative = zp.Points >= 0;
        zp.Points = 0;
        StandAt(me, bj);
        var broke = g.ServerCasino(me, bj, CasinoAct.Bet, 0, rmin, now);
        neverNegative &= zp.Points == 0 && broke != null;
        // The tick allocates nothing (tables open, a hand in play, chips down, a spin turning).
        zp.Points = 100000;
        g.ServerCasino(me, bj, CasinoAct.Bet, 0, 100, now);
        Tick(rules.CasinoDealSeconds + 0.3);
        StandAt(me, ro);
        g.ServerCasino(me, ro, CasinoAct.Bet, 5, 100, now);
        g.SelfTestTickCasino(now += 0.01);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 600; k++)
            g.SelfTestTickCasino(now += 0.001);
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        ZLine($"big win: a {big} natural announced {bigWin} ({t0.WinAmount} over the bet, shown from {rules.CasinoAnnounceAt}), the client showed it {clientSaw}; points never below zero {neverNegative} (broke: '{broke}'); the tables' tick allocated {alloc} bytes in 600 ticks");
        if (!bigWin || !clientSaw || !neverNegative || alloc > 0)
            Fail("a big casino win should reach every client, points should never go below zero, and the tables' tick should allocate nothing");
        if (ServerInfo.ProtocolVersion < 66)
            Fail("the casino tables changed the wire: the protocol should be at least 66");
    }
}
