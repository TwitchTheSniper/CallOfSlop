using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies category slots" (Source/Game/ZombiesSlots.cs, ZombiesSlotWin.cs; docs/zombies/ZOMBIES-MODE.md,
/// "Slot machines"): Zombies Test's Arcade has a machine for every part category, each standing as its own cabinet (a
/// different silhouette per category); every machine pays only its own category (the nearest tier it has), the blade
/// only on the melee categories, keeps its own heat, prices by category, wonder parts in jackpot rounds; a win shows to
/// the host and every client (the message, the model over the machine, a feed line in the rarity colour); no allocations.
/// </summary>
public static partial class SelfTest
{
    private static void CheckZombiesCategorySlots()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "slot rules");
        var categories = Enum.GetValues<PartCategory>();
        var zt = ZTestMap;
        if (zt?.Zombies is not { } ztm)
        {
            Fail("Zombies Test is missing");
            return;
        }

        // ---- The machines and their cabinets ----
        var machines = new int[categories.Length];
        Array.Fill(machines, -1);
        for (var i = 0; i < ztm.SlotMachines.Count; i++)
            if (ztm.SlotMachines[i].SlotCategory >= 0)
                machines[ztm.SlotMachines[i].SlotCategory] = i;
        var missing = categories.Where(c => machines[(int)c] < 0).ToList();
        var basic = Props.Find(ZombiesMap.MachineProp.SlotMachine);
        var shapes = new HashSet<string>();
        var badProps = new List<string>();
        foreach (var c in categories)
        {
            var name = ZombiesMap.MachineProp.SlotMachineFor((int)c);
            if (Props.Find(name) is not { } def || basic == null || def.Parts.Count <= basic.Parts.Count)
            {
                badProps.Add(name);
                continue;
            }
            shapes.Add(string.Join(";", def.Parts.Skip(basic.Parts.Count + 3).Select(p => $"{p.Size}{p.Offset}{p.RotationDeg}")));
        }
        var arcade = ztm.SlotMachines.Where(s => s.SlotCategory >= 0).Select(s => s.Area).Distinct().ToList();
        var prices = string.Join(" ", categories.Select(c => $"{c} {rules.SlotCost(1, (int)c)}"));
        ZLine($"category slots: Zombies Test has {categories.Length - missing.Count} of {categories.Length} categories (missing: {string.Join(", ", missing)}) in {string.Join(", ", arcade.Select(a => ztm.Areas[a].Name))}; "
              + $"{shapes.Count} different cabinet silhouettes, bad props [{string.Join(", ", badProps)}]; round 1 prices: {prices}");
        if (missing.Count > 0 || arcade.Count != 1)
            Fail("Zombies Test should have a slot machine for every part category, in a row in one area");
        if (badProps.Count > 0 || shapes.Count != categories.Length)
            Fail("every part category should have its own Reel Cabinet prop with a different silhouette on top");
        if (categories.Any(c => rules.SlotCost(1, (int)c) <= 0) || rules.SlotCost(1, -1) != rules.SlotCost(1) || !rules.SlotPaysMelee(-1)
            || categories.Any(c => rules.SlotPaysMelee((int)c) != c is PartCategory.Hilt or PartCategory.Head or PartCategory.MeleeMod))
            Fail("the rules should price every category's machine and put the blade only on the classic and melee machines");

        // ---- The part picks: own category only, the nearest tier it has, wonder only in a jackpot round ----
        var wrongCat = 0;
        var wrongTier = 0;
        var wonderOutside = 0;
        var jackpotWonders = 0;
        foreach (var c in categories)
            for (var jackpot = 0; jackpot < 2; jackpot++)
                for (var tier = Rarity.Common; tier <= (jackpot == 1 ? Rarity.Wonder : Rarity.Legendary); tier++)
                {
                    var have = Parts.All.Any(p => p.Category == c && p.Rarity == tier);
                    for (var seed = 1u; seed <= 20; seed++)
                    {
                        var k = Game.PickSlotPart(tier, seed * 977u, (int)c, jackpot == 1);
                        if (k < 0 || Parts.All[k].Category != c)
                        {
                            wrongCat++;
                            continue;
                        }
                        if (have && Parts.All[k].Rarity != tier)
                            wrongTier++;
                        if (jackpot == 0 && Parts.All[k].Rarity == Rarity.Wonder)
                            wonderOutside++;
                        if (jackpot == 1 && tier == Rarity.Wonder && Parts.All[k].Rarity == Rarity.Wonder)
                            jackpotWonders++;
                    }
                }
        ZLine($"category slots: part picks of the wrong category {wrongCat}, wrong tier {wrongTier}, wonder outside a jackpot round {wonderOutside}, jackpot wonders {jackpotWonders}");
        if (wrongCat > 0 || wrongTier > 0 || wonderOutside > 0 || jackpotWonders == 0)
            Fail("a category machine's part should be of its category, of the rolled tier when it has one (else the nearest), wonder only in a jackpot round");

        // ---- Real pulls on every category machine (round 12): only its parts, its own heat, the win for everyone ----
        var now = 0.0;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        using var c1 = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        using var c2 = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
        World.SetMap(zt);
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var zp = g.ZP(me);
        g.ZPower = true;
        g.ZRound = 12;
        var outsiders = 0;
        var bladeOnGun = 0;
        var heatLeaks = 0;
        var wins = 0;
        var pulls = 0;
        var meleeWins = 0;
        var notShown = 0;
        var clientsOk = true;
        var feedOk = true;
        var w = new PacketWriter();
        var perMachine = new List<string>();
        foreach (var c in categories)
        {
            var i = machines[(int)c];
            if (i < 0)
                continue;
            var spot = ztm.SlotMachines[i];
            me.Teleport(spot.At + World.FlatForward(spot.Yaw) * 0.9f);
            var use = g.FindZUse(me);
            if (use is not { Kind: ZUseKind.SlotMachine } || use.Value.Index != i || !use.Value.Text.Contains(Game.SlotMachineName((int)c)) || use.Value.Cost != g.SlotCost(ztm, i))
                Fail($"the {Game.SlotMachineName((int)c)}'s prompt should name it and its price ('{use?.Text}')");
            var others = g.ZSlots.Select(m => m.Heat).ToArray();
            var mine = 0;
            for (var pull = 0; pull < 16; pull++)
            {
                zp.Points = 1_000_000;
                foreach (var name in zp.Parts.Keys.ToList())
                    zp.Parts[name] = 0; // (no auto-scrapping: every part won stays a part)
                now += 0.01;
                g.SelfTestUse(me, now);
                var m = g.ZSlots[i];
                if (m.State != ZSlotState.Spinning)
                    break;
                pulls++;
                var seq = m.WinSeq;
                now += rules.SlotSpinSeconds + 0.01;
                g.SelfTestTickSlots(now);
                if (m.Result == ZSlotResult.Part && Parts.All[m.Part].Category != c)
                    outsiders++;
                if (m.Result == ZSlotResult.Melee && !rules.SlotPaysMelee((int)c))
                    bladeOnGun++;
                if (m.Result is ZSlotResult.Part or ZSlotResult.Melee)
                {
                    wins++;
                    mine++;
                    if (m.Result == ZSlotResult.Melee)
                        meleeWins++;
                    var clock = Brutal.GlfwApi.Glfw.Time;
                    if (m.WinSeq == seq || m.WinIndex != m.Part || m.WinTier != m.Tier || m.WinPlayer != me.Id || !g.SlotWinShowing(i, clock))
                        notShown++;
                    var line = g.Feed.Count > 0 ? g.Feed[^1] : default;
                    feedOk &= line.Colour == PartDef.RarityColor(m.Tier) && line.Text.Contains(m.Result == ZSlotResult.Part ? Parts.All[m.Part].Name : StandardBuilds.All[m.Part].Name) && line.Text.Contains(Game.SlotMachineName((int)c));
                    // Every client: the message, then the same win over the same machine and the same coloured line.
                    g.SelfTestWriteSlotWin(w, i);
                    var msg = w.Span.ToArray();
                    foreach (var cl in new[] { c1, c2 })
                    {
                        var before = cl.ZSlots[i].WinSeq;
                        cl.SelfTestReadSlotWin(msg);
                        var cm = cl.ZSlots[i];
                        var cline = cl.Feed.Count > 0 ? cl.Feed[^1] : default;
                        clientsOk &= cm.WinSeq != before && cm.WinIndex == m.Part && cm.WinTier == m.Tier && cm.WinResult == m.Result && cm.WinPlayer == me.Id && cl.SlotWinShowing(i, Brutal.GlfwApi.Glfw.Time)
                                     && cline.Colour == PartDef.RarityColor(m.Tier) && cline.Text.Contains(Game.SlotMachineName((int)c));
                    }
                }
                now += rules.SlotShowSeconds + 0.01;
                g.SelfTestTickSlots(now);
            }
            for (var k = 0; k < g.ZSlots.Length; k++)
                if (k != i && g.ZSlots[k].Heat != others[k])
                    heatLeaks++;
            perMachine.Add($"{c} {mine}/heat {g.ZSlots[i].Heat}");
        }
        ZLine($"category slots: {pulls} pulls on {perMachine.Count} machines (round 12), wins {wins} ({meleeWins} melee builds): other categories paid {outsiders}, blades on a gun machine {bladeOnGun}, heat leaked to other machines {heatLeaks}; "
              + $"win shown on the host {wins - notShown}/{wins}, feed line {feedOk}, two clients see every win {clientsOk}; per machine: {string.Join(", ", perMachine)}");
        if (pulls < categories.Length * 14 || wins == 0)
            Fail("every category machine should take pulls and pay parts");
        if (outsiders > 0 || bladeOnGun > 0)
            Fail("a category machine should only pay parts of its own category (whole melee builds only on the melee machines)");
        if (heatLeaks > 0)
            Fail("each slot machine should keep its own heat");
        if (notShown > 0 || !feedOk || !clientsOk)
            Fail("a slot machine win should show over the machine on the host and every client, with a feed line in the rarity colour naming the machine and the part");

        // ---- No allocations in the category rolls ----
        long Measure()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0u; k < 4000; k++)
                Game.PickSlotPart((Rarity)(k % 6), k, (int)(k % (uint)categories.Length), (k & 1) == 0);
            for (var k = 0; k < 500; k++)
                g.SelfTestTickSlots(now + k * 1e-6);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Measure();
        Measure();
        var alloc = Math.Min(Measure(), Math.Min(Measure(), Measure()));
        ZLine($"category slots: {alloc} bytes allocated by the category part picks and the machines' tick; protocol {ServerInfo.ProtocolVersion}");
        if (alloc > 0)
            Fail($"the category slot rolls and the machines' tick should allocate nothing ({alloc} bytes)");
        if (ServerInfo.ProtocolVersion < 55)
            Fail("the slot machine win message needs protocol 55");
    }
}
