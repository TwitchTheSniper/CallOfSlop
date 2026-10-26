using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies slots" (Source/Game/ZombiesSlots.cs; docs/zombies/ZOMBIES-MODE.md, "Slot machines"): the
/// rules and the "slotTiers" table load; the reels always read as what was paid (and the wonder gem only shows in jackpot
/// rounds); seeded odds by round (the rarity mix rises with the round, nothing outside the table comes up, wonder parts
/// only in jackpot rounds); heat moves the odds up and resets on a rare part; then a real machine on Zombies Test: the
/// price, the prompt, the spin, the payouts (parts into your loose parts, scrap, points back), at most three blanks in a
/// row, auto-scrapped duplicates, no builds touched, the power-gated machine, the network copy, and no allocations in the
/// machines' tick or the rolls. Blackmoor Asylum's machines stand as their props.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesSlots()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "slot rules");
        var tables = RunTables.Current;
        foreach (var e in RunTables.LoadErrors)
            Fail($"drop tables: {e}");
        if (tables.SlotTiers.Count < 3 || rules.SlotCost(1) != rules.SlotCostBase || rules.SlotCost(1000) != rules.SlotCostMax || !rules.SlotJackpot(5) || rules.SlotJackpot(4))
            Fail($"slots: the slotTiers table ({tables.SlotTiers.Count} rows) or the prices / jackpot rounds aren't as the rules say");
        foreach (var bad in new[] { """{"format": "call-of-slop-drops/1", "slotTiers": [{"fromRound": 1, "jackpot": 3}]}""", """{"format": "call-of-slop-drops/1", "slotTiers": [{"fromRound": 1, "nothing": -1, "rare": 2}]}""" })
            try
            {
                RunTables.Load(bad, "bad slots");
                Fail($"a broken slotTiers table loaded: {bad}");
            }
            catch (WeaponFileException)
            {
            }

        // ---- The reels read as what was paid ----
        Span<byte> sym = stackalloc byte[3];
        var misread = 0;
        var wonderShown = 0;
        var shapes = new HashSet<int>();
        foreach (var jackpot in new[] { false, true })
            for (var res = ZSlotResult.Nothing; res <= ZSlotResult.Part; res++)
                for (var tier = Rarity.Common; tier <= (jackpot ? Rarity.Wonder : Rarity.Legendary); tier++)
                {
                    if (res != ZSlotResult.Part && tier != Rarity.Common)
                        continue;
                    for (var seed = 1u; seed <= 400; seed++)
                    {
                        Game.SlotSymbols(res, tier, jackpot, seed * 7919u, sym);
                        if (Game.ReadSlotSymbols(sym) != (res, tier))
                            misread++;
                        var gemTop = Game.SlotSymGem + (int)(jackpot ? Rarity.Wonder : Rarity.Legendary);
                        if (sym[0] > gemTop || sym[1] > gemTop || sym[2] > gemTop)
                            misread++;
                        if (!jackpot && (sym[0] == Game.SlotSymGem + (int)Rarity.Wonder || sym[1] == Game.SlotSymGem + (int)Rarity.Wonder || sym[2] == Game.SlotSymGem + (int)Rarity.Wonder))
                            wonderShown++;
                        shapes.Add(sym[0] | sym[1] << 8 | sym[2] << 16);
                    }
                }
        ZLine($"slots: reels misread {misread}, wonder gem outside a jackpot round {wonderShown}, {shapes.Count} different reel lines");
        if (misread > 0 || wonderShown > 0 || shapes.Count < 60)
            Fail("slot machine reels should always read as what was paid (three of a gem: that tier; two: one lower), with variety, and the wonder gem only in jackpot rounds");

        // ---- Odds by round (seeded): the rarity mix rises, nothing outside the table comes up ----
        const int Rolls = 20000;
        (float MeanTier, float RarePlus, int[] Count) Mix(int round, int heat)
        {
            var count = new int[RunTables.SlotKeys.Length];
            var sum = 0f;
            var parts = 0;
            var rare = 0;
            for (var i = 0u; i < Rolls; i++)
            {
                var (r, t) = Game.RollSlotOutcome(rules, tables, round, heat, i * 2654435761u + 17u);
                var k = r switch { ZSlotResult.Nothing => 0, ZSlotResult.Scrap => 1, ZSlotResult.Points => 2, _ => RunTables.SlotPart + (int)t };
                count[k]++;
                if (r == ZSlotResult.Part)
                {
                    parts++;
                    sum += (int)t;
                    if (t >= Rarity.Rare)
                        rare++;
                }
            }
            return (parts > 0 ? sum / parts : 0, rare / (float)Rolls, count);
        }
        Span<float> w = stackalloc float[RunTables.SlotKeys.Length];
        var outside = 0;
        var line = "";
        var last = -1f;
        var rising = true;
        foreach (var (round, heat) in new[] { (1, 0), (6, 0), (11, 0), (16, 0), (26, 0), (36, 0), (25, 0), (1, 5), (1, 15) })
        {
            var (mean, rare, count) = Mix(round, heat);
            Game.SlotWeights(rules, tables, round, heat, w);
            for (var k = 0; k < count.Length; k++)
                if (count[k] > 0 && w[k] <= 0)
                    outside++;
            var wonders = count[RunTables.SlotPart + (int)Rarity.Wonder];
            if (!rules.SlotJackpot(round) && wonders > 0)
                outside++;
            line += $"; r{round}{(heat > 0 ? $" heat {heat}" : "")}{(rules.SlotJackpot(round) ? " (jackpot)" : "")}: mean tier {mean:0.00}, rare+ {rare * 100:0.0}%, wonder {wonders}, nothing {count[0] * 100 / Rolls}%";
            if (heat == 0 && !rules.SlotJackpot(round))
            {
                rising &= mean > last;
                last = mean;
            }
        }
        var (m1, r1, _) = Mix(1, 0);
        var (m1h, r1h, _) = Mix(1, 5);
        var (m1hh, _, _) = Mix(1, 15);
        var (_, _, jack) = Mix(25, 0);
        ZLine($"slot odds ({Rolls} rolls each){line}");
        if (!rising || outside > 0)
            Fail($"slot machine odds should rise with the round and stay inside the table (rising {rising}, {outside} outcome(s) outside it)");
        if (!(m1h > m1 && m1hh > m1h && r1h > r1) || jack[RunTables.SlotPart + (int)Rarity.Wonder] == 0)
            Fail("machine heat should move the odds up a tier per step, and a jackpot round should pay wonder parts");

        // ---- A real machine (Zombies Test, start room) ----
        var now = 0.0;
        var zt = ZTestMap;
        if (zt?.Zombies is not { SlotMachines.Count: >= 2 } ztm)
        {
            Fail("Zombies Test should have two slot machines (one needing the power)");
            return;
        }
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var zp = g.ZP(me);
            var spot = ztm.SlotMachines[0];
            me.Teleport(spot.At + World.FlatForward(spot.Yaw) * 0.9f);
            zp.Points = 100;
            var prompt = g.FindZUse(me);
            g.SelfTestUse(me, now);
            var poorRefused = g.ZSlots[0].State == ZSlotState.Idle && zp.Points == 100;
            g.ZRound = 3;
            var cost = g.SlotCost(ztm, 0);
            var builds = string.Join("|", zp.Slots.Select(b => b?.ToText() ?? "-"));
            zp.Points = 1_000_000;
            var counts = new int[4];
            var tiers = new int[6];
            var worstBlankRun = 0;
            var blankRun = 0;
            var heatResetOk = true;
            var payOk = true;
            var spinOk = true;
            for (var pull = 0; pull < 150; pull++)
            {
                var points = zp.Points;
                var scrap = zp.Scrap;
                var loose = zp.Parts.Values.Sum();
                var heat = g.ZSlots[0].Heat;
                now += 0.01;
                g.SelfTestUse(me, now);
                var m = g.ZSlots[0];
                spinOk &= m.State == ZSlotState.Spinning && zp.Points == points - cost && m.Owner == me.Id;
                g.SelfTestTickSlots(now + rules.SlotSpinSeconds * 0.5); // still spinning: nothing paid yet
                spinOk &= m.State == ZSlotState.Spinning && zp.Scrap == scrap && zp.Parts.Values.Sum() == loose;
                now += rules.SlotSpinSeconds + 0.01;
                g.SelfTestTickSlots(now);
                var busy = g.FindZUse(me) is { Kind: ZUseKind.SlotMachine, Blocked: true };
                counts[(int)m.Result]++;
                switch (m.Result)
                {
                    case ZSlotResult.Part:
                        tiers[(int)m.Tier]++;
                        payOk &= m.AutoScrapped ? zp.Scrap == scrap + Scrap.SalvageValue(Parts.All[m.Part]) : zp.Parts.Values.Sum() == loose + 1 && zp.Parts.GetValueOrDefault(Parts.All[m.Part].Name) > 0;
                        heatResetOk &= m.Tier >= rules.SlotPityTier ? m.Heat == 0 : m.Heat == heat + 1;
                        break;
                    case ZSlotResult.Scrap:
                        payOk &= zp.Scrap == scrap + rules.SlotScrap;
                        heatResetOk &= m.Heat == heat + 1;
                        break;
                    case ZSlotResult.Points:
                        payOk &= zp.Points > points - cost;
                        heatResetOk &= m.Heat == heat + 1;
                        break;
                    default:
                        payOk &= zp.Points == points - cost && zp.Scrap == scrap;
                        heatResetOk &= m.Heat == heat + 1;
                        break;
                }
                payOk &= busy && Game.ReadSlotSymbols(m.Symbols) == (m.Result, m.Result == ZSlotResult.Part ? m.Tier : Rarity.Common);
                blankRun = m.Result == ZSlotResult.Nothing ? blankRun + 1 : 0;
                worstBlankRun = Math.Max(worstBlankRun, blankRun);
                now += rules.SlotShowSeconds + 0.01;
                g.SelfTestTickSlots(now);
                spinOk &= m.State == ZSlotState.Idle;
            }
            var buildsKept = string.Join("|", zp.Slots.Select(b => b?.ToText() ?? "-")) == builds;
            // A part you already hold plenty of turns into scrap.
            var common = Parts.All.First(p => p.Rarity == Rarity.Common);
            zp.Parts[common.Name] = rules.SlotAutoScrapAt;
            var dupScrap = zp.Scrap;
            var forced = g.ZSlots[0];
            forced.State = ZSlotState.Spinning;
            forced.Owner = me.Id;
            forced.Result = ZSlotResult.Part;
            forced.Tier = Rarity.Common;
            forced.Part = (short)Array.IndexOf(Parts.All, common);
            forced.Until = now;
            g.SelfTestTickSlots(now + 0.01);
            var autoScrapped = forced.AutoScrapped && zp.Parts[common.Name] == rules.SlotAutoScrapAt && zp.Scrap == dupScrap + Scrap.SalvageValue(common);
            now += 10;
            g.SelfTestTickSlots(now);

            // The power-gated machine; the network copy.
            var pspot = ztm.SlotMachines[1];
            me.Teleport(pspot.At + World.FlatForward(pspot.Yaw) * 0.9f);
            var dark = g.FindZUse(me);
            g.ZPower = true;
            var litUse = g.FindZUse(me);
            g.SelfTestUse(me, now);
            var w2 = new PacketWriter();
            g.WriteZEnts(w2);
            var ents = w2.Span.ToArray();
            g.WriteZWorld(w2);
            var world = w2.Span.ToArray();
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(zt);
            c.SelfTestReadZombies(ents, world);
            var cm = c.ZSlots[1];
            var sm = g.ZSlots[1];
            var netOk = cm.State == sm.State && cm.SpinId == sm.SpinId && cm.Heat == sm.Heat && cm.Owner == sm.Owner && cm.Symbols.SequenceEqual(sm.Symbols) && cm.Result == sm.Result
                        && cm.Part == sm.Part && MathF.Abs(cm.Left - sm.Left) <= 0.11f && c.ZSlots[0].Heat == g.ZSlots[0].Heat && ServerInfo.ProtocolVersion >= 51;

            // No allocations: the machines' tick (idle and spinning) and the rolls.
            long Measure(Span<byte> s)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 2000; i++)
                    g.SelfTestTickSlots(now + i * 1e-6);
                var tick = GC.GetAllocatedBytesForCurrentThread() - before;
                for (var i = 0u; i < 2000; i++)
                {
                    Game.RollSlotOutcome(rules, tables, 12, (int)(i % 20), i);
                    Game.SlotSymbols(ZSlotResult.Part, Rarity.Epic, true, i, s);
                    Game.ReadSlotSymbols(s);
                }
                var all = GC.GetAllocatedBytesForCurrentThread() - before;
                Line($"  slot allocations: tick {tick} bytes, all {all} bytes");
                return all;
            }
            Measure(sym); // (warm-up: the first calls compile)
            var alloc = Measure(sym);
            ZLine($"slot machine (round 3, {cost} a pull): too poor refused {poorRefused} ('{prompt?.Text}'); 150 pulls: nothing {counts[0]}, scrap {counts[1]}, points {counts[2]}, parts {counts[3]} "
                  + $"(common {tiers[0]}, uncommon {tiers[1]}, rare {tiers[2]}, epic {tiers[3]}, legendary {tiers[4]}, wonder {tiers[5]}); longest run of nothing {worstBlankRun}; spin {spinOk}, paid right {payOk}, heat {heatResetOk}; "
                  + $"builds untouched {buildsKept}; duplicate auto-scrapped {autoScrapped}; power machine '{dark?.Text}' then '{litUse?.Text}'; client copy {netOk} ({Game.ZSlotBytes} bytes a machine, protocol {ServerInfo.ProtocolVersion}); {alloc} bytes allocated");
            if (!poorRefused || prompt is not { Kind: ZUseKind.SlotMachine, Blocked: false })
                Fail("a slot machine should prompt with its price and refuse a pull you can't pay for");
            if (!spinOk || !payOk || !heatResetOk || counts[3] == 0 || counts[1] == 0)
                Fail("a slot machine pull should take the price, spin, then pay what its reels show (parts to your loose parts, scrap, points) and keep its heat");
            if (worstBlankRun > rules.SlotMaxNothing)
                Fail($"a slot machine gave nothing {worstBlankRun} times in a row (at most {rules.SlotMaxNothing})");
            if (!buildsKept || !autoScrapped)
                Fail("a slot machine should never touch your builds, and a part you hold plenty of should turn into scrap");
            if (dark is not { Blocked: true } || litUse is not { Blocked: false })
                Fail("a slot machine marked \"power\" should only work with the power on");
            if (!netOk)
                Fail("a client should see the slot machines as the server has them (protocol 51)");
            if (alloc > 0)
                Fail($"the slot machines' tick and rolls should allocate nothing ({alloc} bytes)");
        }

        // ---- Blackmoor Asylum: two machines, one behind the power, standing as Reel Cabinet props ----
        if (Maps.Find("Blackmoor Asylum") is { Zombies: { } bm } bmap)
        {
            var props = bm.SlotMachines.Count(s => bmap.PropInstances.Any(p => p.Def.Name == ZombiesMap.MachineProp.SlotMachine && Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(s.At.X, s.At.Z)) < 0.01f));
            var early = bm.SlotMachines.Count(s => s.Area == bm.StartArea && !s.NeedsPower);
            var powered = bm.SlotMachines.Count(s => s.NeedsPower);
            ZLine($"Blackmoor Asylum: {bm.SlotMachines.Count} slot machines ({early} in {bm.Areas[bm.StartArea].Name}, {powered} on the power), {props} standing as the {ZombiesMap.MachineProp.SlotMachine} prop");
            if (bm.SlotMachines.Count != 2 || early != 1 || powered != 1 || props != 2)
                Fail("Blackmoor Asylum should have two slot machines (one in the start room, one behind the power), each a Reel Cabinet prop");
        }
        else
            Fail("Blackmoor Asylum is missing");
    }
}
