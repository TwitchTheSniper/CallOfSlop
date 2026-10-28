namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies slot consolation" (board items "slots: a consolation part instead of nothing" and "no limit
/// on banked parts", 2026-10-27; Source/Game/ZombiesSlots.cs, docs/zombies/ZOMBIES-MODE.md "Slot machines"): the
/// consolation tiers load per bet level; the consolation rarity follows the bet, ignores the round's table (commons at
/// round 35 at 1x) and never goes above the main roll's floor; a real machine never pays nothing, every blank pays a part
/// of the machine's own category, frees the machine at once and never resets the heat; a client sees the consolation (the
/// machine and the win message); winning the same part 300 times keeps every copy, on the server and on a client, and
/// the run message carries every part kind with big counts. Nothing in the consolation roll allocates.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesSlotConsolation()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "slot rules");
        var tables = RunTables.Current;
        var rowsOk = rules.SlotConsolation.Length == 3 && rules.SlotDupePointsAt == 0
                     && rules.SlotConsolationFor(0).Select((x, t) => t == 0 ? x > 0 : x == 0).All(b => b)
                     && rules.SlotConsolationFor(9) == rules.SlotConsolation[^1];
        foreach (var bad in new[] { "[]", "[{\"shiny\": 3}]", "[{\"common\": 0}]" })
            try
            {
                ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName).Replace("\"maxNothing\": 3,", $"\"maxNothing\": 3, \"consolation\": {bad},"), "bad consolation"); // (read before the shipped one)
                rowsOk = false;
                Line($"  a broken consolation list loaded: {bad}");
            }
            catch (WeaponFileException)
            {
            }

        // ---- The rarity by bet level, at every round ----
        var seen = new int[3, 6];
        var aboveFloor = 0;
        for (var bet = 0; bet < 3; bet++)
            for (var round = 1; round <= 40; round++)
            {
                Span<float> w = stackalloc float[RunTables.SlotKeys.Length];
                Game.SlotWeights(rules, tables, round, 0, w, rules.SlotBetStep(bet));
                var floor = 0;
                while (floor < 5 && w[RunTables.SlotPart + floor] <= 0)
                    floor++;
                for (var i = 0u; i < 300; i++)
                {
                    var t = Game.RollSlotConsolation(rules, tables, round, bet, i * 2654435761u + 5u);
                    seen[bet, (int)t]++;
                    if ((int)t > floor)
                        aboveFloor++;
                }
            }
        int Seen(int bet, Rarity t) => seen[bet, (int)t];
        var total = 40 * 300;
        var betOk = Seen(0, Rarity.Common) == total // 1x: always common, even in round 35 when the main table has none
                    && Seen(1, Rarity.Common) > total / 4 && Seen(1, Rarity.Uncommon) > total / 5 && Seen(1, Rarity.Common) + Seen(1, Rarity.Uncommon) == total
                    && Seen(2, Rarity.Common) == 0 && Seen(2, Rarity.Uncommon) > total * 7 / 10 && Seen(2, Rarity.Rare) > 0 && Seen(2, Rarity.Uncommon) + Seen(2, Rarity.Rare) == total
                    && aboveFloor == 0;
        // The floor caps it: a rules file asking for epic consolations at 1x still pays round 1's lowest part tier (common).
        var greedy = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "greedy");
        greedy.SlotConsolation = [[0f, 0f, 0f, 100f]];
        var capped = Enumerable.Range(0, 200).All(i => Game.RollSlotConsolation(greedy, tables, 1, 0, (uint)i) == Rarity.Common);
        ZLine($"slot consolation tiers: 1x {Seen(0, Rarity.Common)} common of {total} (rounds 1-40); 2x common {Seen(1, Rarity.Common)}, uncommon {Seen(1, Rarity.Uncommon)}; "
              + $"5x common {Seen(2, Rarity.Common)}, uncommon {Seen(2, Rarity.Uncommon)}, rare {Seen(2, Rarity.Rare)}; above the main floor {aboveFloor}; epic asked at 1x pays common {capped}; rules rows {rowsOk}");
        if (!rowsOk)
            Fail("the slots rules should hold a consolation row per bet level (1x common only), refuse broken rows, and keep every copy of a part (dupePointsAt 0)");
        if (!betOk || !capped)
            Fail("a consolation part's rarity should follow the bet level (1x common at every round, 2x common or uncommon, 5x mostly uncommon with some rare), never above the main roll's floor");

        // ---- A real machine: no pull pays nothing; consolations from the machine's category, free at once, heat kept ----
        var now = 0.0;
        if (ZTestMap?.Zombies is not { SlotMachines.Count: > 0 } ztm)
        {
            Fail("Zombies Test should have a slot machine");
            return;
        }
        var spot = ztm.SlotMachines[0];
        var oldCategory = spot.SlotCategory;
        try
        {
            using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var zp = g.ZP(me);
            spot.SlotCategory = (int)PartCategory.Barrel;
            me.Teleport(spot.At + World.FlatForward(spot.Yaw) * 0.9f);
            zp.Points = 100_000_000;
            var m = g.ZSlots[0];
            var nothing = 0;
            var cons = new int[3];
            var consTiers = new int[6];
            var catOk = true;
            var freeOk = true;
            var heatOk = true;
            var textOk = true;
            var clientOk = true;
            var clientChecked = 0;
            var w = new PacketWriter();
            var ents = Array.Empty<byte>();
            for (var bet = 0; bet < 3; bet++)
            {
                m.Bet = (byte)bet;
                g.ZRound = bet == 0 ? 35 : 3; // (1x late: the main table has no commons left)
                for (var pull = 0; pull < 120; pull++)
                {
                    var heat = m.Heat;
                    var loose = zp.Parts.Values.Sum();
                    now += 0.01;
                    g.SelfTestUse(me, now);
                    now += rules.SlotSpinSeconds + 0.01;
                    g.SelfTestTickSlots(now);
                    if (m.Result == ZSlotResult.Nothing)
                        nothing++;
                    if (!m.Consolation)
                    {
                        if (m.State == ZSlotState.Showing)
                            g.SelfTestTickSlots(now += rules.SlotShowSeconds + 0.01);
                        continue;
                    }
                    cons[bet]++;
                    consTiers[(int)m.Tier]++;
                    var part = m.Part >= 0 && m.Part < Parts.All.Length ? Parts.All[m.Part] : null;
                    catOk &= part is { Category: PartCategory.Barrel } && m.Result == ZSlotResult.Part && zp.Parts.Values.Sum() == loose + 1
                             && (bet != 0 || m.Tier == Rarity.Common);
                    freeOk &= m.State == ZSlotState.Idle && m.Owner == byte.MaxValue && g.FindZUse(me) is { Kind: ZUseKind.SlotMachine, Blocked: false };
                    heatOk &= m.Heat == Math.Min(255, heat + 1); // (a rare consolation too: it isn't a pity-tier win)
                    textOk &= g.SlotResultText(m).StartsWith("Consolation: ") && part != null && g.SlotResultText(m).Contains(part.Name);
                    if (clientChecked < 5)
                    {
                        // The client: the machine in ZWorld, and the win everyone sees (Msg.ZSlotWin) as a consolation.
                        clientChecked++;
                        g.WriteZEnts(w);
                        ents = w.Span.ToArray();
                        g.WriteZWorld(w);
                        c.SelfTestReadZombies(ents, w.Span.ToArray());
                        g.SelfTestWriteSlotWin(w, 0);
                        c.SelfTestReadSlotWin(w.Span.ToArray());
                        var cm = c.ZSlots[0];
                        clientOk &= cm.Consolation && cm.Result == ZSlotResult.Part && cm.Part == m.Part && cm.Tier == m.Tier && cm.State == ZSlotState.Idle
                                    && cm.WinConsolation && cm.WinIndex == m.Part && cm.WinResult == ZSlotResult.Part;
                    }
                }
            }
            ZLine($"slot consolation on a Barrel machine: 360 pulls (1x round 35, 2x and 5x round 3): nothing {nothing}, consolations {string.Join("/", cons)} "
                  + $"(common {consTiers[0]}, uncommon {consTiers[1]}, rare {consTiers[2]}, above {consTiers[3] + consTiers[4] + consTiers[5]}); its category {catOk}, free at once {freeOk}, heat kept rising {heatOk}, "
                  + $"'Consolation: <part>' {textOk}, client sees it {clientOk} ({clientChecked} checked; protocol {ServerInfo.ProtocolVersion})");
            if (nothing > 0 || cons.Any(n => n == 0))
                Fail($"a slot machine pull should never pay nothing ({nothing} did) and blanks should pay consolation parts at every bet ({string.Join("/", cons)})");
            if (!catOk || !freeOk || !heatOk || !textOk || consTiers[3] + consTiers[4] + consTiers[5] > 0)
                Fail("a consolation part should be a part of the machine's own category at the bet's tier (common at 1x even late), free the machine at once, never reset the heat and read 'Consolation: <part>'");
            if (!clientOk || clientChecked == 0 || ServerInfo.ProtocolVersion < 69)
                Fail("a client should see a consolation part as the server paid it (the machine and the win message; protocol 71)");

            // ---- Unlimited banking: the same part won 300 times keeps every copy, here and on a client ----
            spot.SlotCategory = oldCategory;
            var common = Parts.All.First(p => p.Rarity == Rarity.Common);
            zp.Parts.Clear();
            for (var i = 0; i < 300; i++)
            {
                m.Paid = 500;
                m.State = ZSlotState.Spinning;
                m.Owner = me.Id;
                m.Result = ZSlotResult.Part;
                m.Consolation = (i & 1) == 0; // (half as consolations: they bank the same way)
                m.Refunded = false;
                m.Tier = common.Rarity;
                m.Part = (short)Array.IndexOf(Parts.All, common);
                m.Until = now;
                g.SelfTestTickSlots(now += 0.01);
                g.SelfTestTickSlots(now += 10);
            }
            var serverHas = zp.Parts.GetValueOrDefault(common.Name);
            var big = new PacketWriter(64 * 1024);
            g.SelfTestWriteRun(big, me);
            c.SelfTestReadRun(big.Span.ToArray());
            var clientHas = c.ZP(c.LocalId).Parts.GetValueOrDefault(common.Name);
            // Every part kind at once (more than the old 200-kind cap), with counts past a byte and a u16.
            foreach (var p in Parts.All)
                zp.Parts[p.Name] = 300;
            zp.Parts[common.Name] = 100_000;
            g.SelfTestWriteRun(big, me);
            var bytes = big.Span.Length;
            c.SelfTestReadRun(big.Span.ToArray());
            var cp = c.ZP(c.LocalId).Parts;
            var allKinds = cp.Count == zp.Parts.Count && zp.Parts.All(kv => cp.GetValueOrDefault(kv.Key) == kv.Value);
            // The workbench's stock counts every copy too.
            var stock = BenchStock.FromRun(zp.Parts, null, Parts.Find);
            var stockOk = stock.Owned(common) == 100_000;
            ZLine($"unlimited parts: the same part won 300 times -> server {serverHas}, client {clientHas}; all {zp.Parts.Count} kinds (one 100000) in a {bytes}-byte run message -> client {allKinds}; bench stock {stockOk}");
            if (serverHas != 300 || clientHas != 300)
                Fail($"winning the same part 300 times should keep every copy (server {serverHas}, client {clientHas})");
            if (!allKinds || zp.Parts.Count <= 200 || !stockOk)
                Fail("the run message should carry every part kind and any count (no 200-kind or 255-copy cap), and the workbench should count them all");

            // No allocations in the consolation roll.
            long alloc = long.MaxValue;
            for (var round = 0; round < 4; round++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0u; i < 2000; i++)
                    Game.RollSlotConsolation(rules, tables, (int)(i % 40) + 1, (int)(i % 3), i);
                alloc = Math.Min(alloc, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            if (alloc > 0)
                Fail($"the consolation roll should allocate nothing ({alloc} bytes)");
        }
        finally
        {
            spot.SlotCategory = oldCategory;
        }
    }
}
