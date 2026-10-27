using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The parts the late-game pass added (2026-10-27): each must load, fit, measure and draw in one piece.</summary>
    private static readonly string[] LateGameParts =
    [
        "Peashooter Receiver", "Hand Mortar Receiver", "Bunker Buster Receiver", "Parade Cannon Receiver", "Sunbeam Lance Receiver",
        "Thunderclap Hammer", "Avalanche Burst Group", "Perpetual Sear",
        "Depleted Core", "Thermobaric", "Bowling Ball", "Pocket Sun",
        "Conga Drum", "Clown Car Hopper", "Shell Silo",
        "Titan Harness", "Parade Float", "Third Arm",
        "All-Seeing Eye", "Lighthouse 6x",
        "Exo Spine Stock", "Jetpack Stock",
        "Doom Hammer", "Overkill Coprocessor",
        "Tripler Chamber", "Five-Alarm Chamber",
        "Gravity Sock", "Exo Sling",
        "Lamp Post", "Turbine Hilt", "Wrecking Ball", "Swordfish", "Grandfather Clock", "Jet Engine", "Tesla Wrap",
    ];

    private static readonly string[] TierNames = ["common", "uncommon", "rare", "epic", "legendary", "wonder"];

    /// <summary>
    /// The rarity power curve (docs/zombies/WEAPON-BUILDER.md, "The power curve"): in every category the best part of a
    /// tier is never weaker than the best of the tier below, and in the damage rows each tier's best reaches its floor
    /// times the best common part. Then the late tiers come late (seeded rolls): zombie drops, the mystery box and the slot
    /// machines (at full heat, the biggest bet and in jackpot rounds) pay a tier only from the round its row starts.
    /// </summary>
    private static void CheckPowerCurve()
    {
        foreach (var (row, tiers) in PowerCurve.Table())
        {
            Line($"{row.Name} ({row.Measures}): " + string.Join(" | ", tiers.Select((t, i) => t.Count == 0 ? $"{TierNames[i]} -" :
                $"{TierNames[i]} best {t[0].Score:0.###} ({t[0].Part.Name}), {t.Count} parts: " + string.Join(", ", t.Select(x => $"{x.Part.Name} {x.Score:0.###}")))));
            var common = tiers[0].Count > 0 ? tiers[0][0].Score : 0f;
            var last = 0f;
            var lastTier = -1;
            for (var i = 0; i < tiers.Length; i++)
            {
                if (tiers[i].Count == 0)
                    continue;
                var best = tiers[i][0].Score;
                if (lastTier >= 0 && best < last * 0.999f)
                    Fail($"power curve: {row.Name}: the best {TierNames[i]} part ({tiers[i][0].Part.Name}, {best:0.###}) is weaker than the best {TierNames[lastTier]} one ({last:0.###})");
                if (row.Damage && common > 0 && best < common * PowerCurve.DamageFloor[i] * 0.999f)
                    Fail($"power curve: {row.Name}: the best {TierNames[i]} part ({tiers[i][0].Part.Name}, {best:0.###}) should be at least x{PowerCurve.DamageFloor[i]} the best common ({common:0.###})");
                last = best;
                lastTier = i;
            }
        }
        // Every part of the rows is measured (a part the curve can't fit to its reference is a broken part or a broken curve).
        var unmeasured = Parts.All.Where(p => PowerCurve.Rows.Any(r => r.Has(p)) && PowerCurve.Score(p) == null).Select(p => p.Name).ToList();
        if (unmeasured.Count > 0)
            Fail($"power curve: no score for {string.Join(", ", unmeasured)} (they don't fit the reference build)");

        // ---- The late tiers come late (seeded) ----------------------------------------------------------------------
        var tables = RunTables.Current;
        var rules = ZombiesRules.Current;
        const int Rolls = 6000;
        int FirstRow(List<RoundRow> rows, Rarity t) => rows.FirstOrDefault(r => r.Weights[(int)t] > 0)?.FromRound ?? int.MaxValue;
        // (round, tier) seen at all, from rolls
        int[] FirstSeen(Func<int, uint, Rarity> roll)
        {
            var first = Enumerable.Repeat(int.MaxValue, 6).ToArray();
            for (var round = 1; round <= 40; round++)
                for (var i = 0u; i < Rolls; i++)
                {
                    var t = (int)roll(round, i * 2654435761u + (uint)round * 977u + 5u);
                    first[t] = Math.Min(first[t], round);
                }
            return first;
        }
        var drops = FirstSeen((round, seed) => RunTables.Roll(tables.PartDrops, round, seed));
        var box = FirstSeen((round, seed) => RunTables.Roll(tables.BoxTiers, round, seed));
        // Slots at their most generous: full heat, the biggest bet, jackpot bonuses on the jackpot rounds.
        var maxHeat = rules.SlotHeatMaxSteps * rules.SlotHeatStep;
        var maxBet = rules.SlotBetStep(rules.SlotBets.Length - 1);
        var slots = FirstSeen((round, seed) =>
        {
            var (res, t) = Game.RollSlotOutcome(rules, tables, round, maxHeat, seed, maxBet);
            return res == ZSlotResult.Part ? t : Rarity.Common;
        });
        int SlotFirst(Rarity t) => tables.SlotTiers.FirstOrDefault(r => r.Top >= t)?.FromRound ?? int.MaxValue;
        string Show(int[] f) => string.Join(", ", f.Select((r, i) => $"{TierNames[i]} r{(r == int.MaxValue ? "-" : r)}"));
        Line($"first round each tier came up in {Rolls} seeded rolls a round: drops {Show(drops)}; box {Show(box)}; slots at full heat and the biggest bet {Show(slots)}");
        foreach (var t in new[] { Rarity.Legendary, Rarity.Wonder })
        {
            var d = FirstRow(tables.PartDrops, t);
            var b = FirstRow(tables.BoxTiers, t);
            var s = SlotFirst(t);
            // A slot wonder also needs a jackpot round: the first jackpot round at or after its row.
            if (t == Rarity.Wonder)
                while (s < 100 && !rules.SlotJackpot(s))
                    s++;
            if (drops[(int)t] != d || box[(int)t] != b || slots[(int)t] != s)
                Fail($"power curve: {t} should first come up in round {d} from drops (came up r{drops[(int)t]}), {b} from the box (r{box[(int)t]}) and {s} from the slots (r{slots[(int)t]})");
        }
        if (FirstRow(tables.PartDrops, Rarity.Legendary) < 15 || FirstRow(tables.PartDrops, Rarity.Wonder) < 20 || SlotFirst(Rarity.Legendary) < 15 || SlotFirst(Rarity.Wonder) < 20)
            Fail("power curve: legendary parts should take until round 15 or later and wonder parts until round 20 or later (drops and slots)");
        // Bad "top" values are refused with a reason.
        foreach (var bad in new[] { """{"format": "call-of-slop-drops/1", "slotTiers": [{"fromRound": 1, "top": "huge", "points": 3}]}""",
                     """{"format": "call-of-slop-drops/1", "slotTiers": [{"fromRound": 1, "top": "rare", "epic": 3}]}""" })
            try
            {
                RunTables.Load(bad, "test");
                Fail($"power curve: a slot row with a bad \"top\" should be refused: {bad}");
            }
            catch (WeaponFileException)
            {
            }
    }

    /// <summary>
    /// The late-game pass (2026-10-27): the receivers that were only a fire rate are gone and every old build on one loads
    /// on its replacement with the action it fired with; every new part loads, fits, measures on the curve and draws in
    /// one piece on a build; the All-Seeing Eye marks zombies; "launch" is tested with the other effects (Zombies Blackmoor).
    /// </summary>
    private static void CheckLateGameParts()
    {
        BuildStore.ReadOnly = true;
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"late-game test needs the part '{name}'");
        // ---- Removed receivers ----------------------------------------------------------------------------------------
        foreach (var (old, (now, action)) in WeaponBuild.RemovedReceivers)
        {
            if (Parts.Find(old) != null)
                Fail($"late-game: the {old} should be gone (it was only a fire rate)");
            if (StandardBuilds.All.Any(s => s.Tree.Contains(old)))
                Fail($"late-game: a standard build still names the {old}");
            var bore = P(now).Receiver!.Bore;
            var feed = bore == "pistol" ? "Stick Mag" : "Box Mag";
            // Before actions were parts (no action anywhere): the replacement with the old receiver's action.
            var pre = WeaponBuild.Parse($"Old|volley|Service Rifle Grip{{receiver_1:{old}{{barrel:Standard Barrel,feed:{feed},ammo:FMJ}}}}", Parts.Find);
            // A current text (an action in it): the replacement, the action as written.
            var cur = WeaponBuild.Parse($"Cur|volley|Service Rifle Grip{{receiver_1:{old}{{barrel:Standard Barrel,feed:{feed},ammo:FMJ,action:Hair Trigger}}}}", Parts.Find, migrate: false);
            var preDef = WeaponBuilder.Assemble(pre).Def;
            var want = P(action).Action!;
            Line($"removed {old}: an old text loads as {pre.ToText()} ({preDef?.ModeName}, {preDef?.Rpm} rpm); a current one as {cur.ToText()}");
            if (pre.Receivers.Count != 1 || pre.Receivers[0].Part.Name != now || WeaponBuild.ActionNodeOf(pre.Receivers[0])?.Part.Name != action || preDef == null
                || preDef.Mode != BuildStats.ModeOf(want.Kind) || preDef.Rpm != want.Rpm || pre.ReceiversMigrated != 1)
                Fail($"late-game: an old build on the {old} should load on the {now} with the {action}, complete");
            if (cur.Receivers.Count != 1 || cur.Receivers[0].Part.Name != now || WeaponBuild.ActionNodeOf(cur.Receivers[0])?.Part.Name != "Hair Trigger")
                Fail($"late-game: a current build on the {old} should load on the {now} with its own action");
        }
        // workbench.txt: an old line loads and is flagged to save back.
        BuildStore.Load(["slot1=Old Burst|volley|Sport Rifle Grip{receiver_1:Triad Burst Receiver{barrel:Long Barrel,feed:Extended Mag,ammo:5.56 FMJ}}"], Parts.Find, out var dropped);
        var slot = BuildStore.Slots[0];
        if (slot == null || dropped.Count > 0 || slot.ReceiversMigrated != 1 || !BuildStore.Lines().Any(l => l.Contains("Workhorse Receiver{") && l.Contains("action:Three-Round Burst Group")))
            Fail($"late-game: an old workbench.txt build on a removed receiver should load ({slot?.ToText()}, dropped {string.Join("; ", dropped)}) and save back on its replacement");
        Array.Clear(BuildStore.Slots);

        // ---- Every new part: loads, measures on the curve, fits and draws in one piece --------------------------------
        var counts = new Dictionary<PartCategory, int>();
        foreach (var name in LateGameParts)
        {
            if (Parts.Find(name) is not { } p)
            {
                Fail($"late-game: the part '{name}' didn't load");
                continue;
            }
            counts[p.Category] = counts.GetValueOrDefault(p.Category) + 1;
            if (p.Rarity < Rarity.Epic && p.Name != "Peashooter Receiver" && p.Name != "Gravity Sock")
                Fail($"late-game: the {p.Name} is {p.Rarity}: the late-game parts are epic and up");
            if (PowerCurve.Score(p) == null)
                Fail($"late-game: the {p.Name} doesn't fit its reference build");
            CheckBuildConnected($"late-game {p.Name}", Showcase(p));
        }
        Line($"{LateGameParts.Length} late-game parts: " + string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => $"{PartDef.CategoryName(kv.Key)} {kv.Value}")));
        // The PvP note: builds only exist in Zombies (and the range), where they meet players only through friendly fire.
        var marks = WeaponBuilder.Assemble(Showcase(P("All-Seeing Eye"))).Def;
        if (marks == null || !marks.BuildFlags.Contains("marks"))
        {
            Fail("late-game: a build with the All-Seeing Eye should carry the 'marks' flag");
            return;
        }
        World.SetMap(Maps.Find("Workbench Range") ?? Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.BuilderRange = true;
        var plain = WeaponBuilder.Assemble(PowerCurve.Reference()).Def!;
        game.UseBuiltWeapons(marks, plain);
        var on = game.MarksZombies;
        game.UseBuiltWeapons(plain, marks);
        var off = game.MarksZombies;
        Line($"All-Seeing Eye: marks zombies while held {on}, not with the other gun out {!off}");
        if (!on || off)
            Fail("late-game: the All-Seeing Eye should mark zombies only while its gun is in hand");
    }

    /// <summary>A complete build showing <paramref name="p"/>: the reference rifle with it fitted, a grip filled with receivers, or a melee weapon.</summary>
    private static WeaponBuild Showcase(PartDef p)
    {
        PartDef P(string name) => Parts.Find(name)!;
        if (p.Category == PartCategory.Grip && p.IsRoot)
        {
            var g = new WeaponBuild("Showcase", p);
            foreach (var s in p.Sockets.Where(s => s.Type == "receiver"))
            {
                var r = g.Root!.Attach(s.Name, P("Plinker Receiver"));
                r.Attach("barrel", P("Standard Barrel"));
                r.Attach("feed", P("Stick Mag"));
                r.Attach("ammo", P("FMJ"));
                r.Attach("action", P("Semi-Auto Trigger"));
            }
            return g;
        }
        if (p.Category is PartCategory.Hilt or PartCategory.Head or PartCategory.MeleeMod)
        {
            var m = new WeaponBuild("Showcase", p.Category == PartCategory.Hilt ? p : P("Wooden Handle"));
            m.Root!.Attach("head", p.Category == PartCategory.Head ? p : P("Machete Blade"));
            if (p.Category == PartCategory.MeleeMod)
                m.Root.Attach("mod_1", p);
            return m;
        }
        if (p.Category == PartCategory.Receiver)
        {
            var b = new WeaponBuild("Showcase", P("Rifle Grip"));
            var r = b.Root!.Attach("receiver_1", p);
            r.Attach("barrel", P("Standard Barrel"));
            r.Attach("ammo", P("FMJ"));
            r.Attach("action", ActionMigration.StockActionOf(p, Parts.Find)!);
            r.Attach("feed", Parts.InCategory(PartCategory.Feed).First(f => f.Shared == null && f.RequiresAction.Length == 0 && f.FitsBore(p.Receiver!.Bore)));
            return b;
        }
        if (p.Category == PartCategory.Feed && p.Shared != null)
        {
            var b = new WeaponBuild("Showcase", P("Rifle Grip"));
            var bore = p.Bores.FirstOrDefault() ?? "rifle";
            var rec = PowerCurve.BoreReference(bore)!;
            var r = b.Root!.Attach("receiver_1", rec);
            r.Attach("barrel", P("Standard Barrel"));
            r.Attach("ammo", P("FMJ"));
            r.Attach("action", ActionMigration.StockActionOf(rec, Parts.Find)!);
            b.Root.Attach("hopper", p);
            return b;
        }
        var socket = p.Category switch
        {
            PartCategory.Action => "action", PartCategory.Ammo => "ammo", PartCategory.Multiplier => "amp", PartCategory.Upgrade => "upgrade_rear",
            PartCategory.Utility => "utility", PartCategory.Grip => "foregrip", PartCategory.Stock => "stock", PartCategory.Optic => "optic",
            PartCategory.Feed => "feed", _ => "",
        };
        var onGrip = p.Category is PartCategory.Grip or PartCategory.Stock or PartCategory.Optic;
        return PowerCurve.Reference((g, r) =>
        {
            if (p.Category == PartCategory.Feed && !p.FitsBore("rifle"))
            {
                var rec = PowerCurve.BoreReference(p.Bores[0])!;
                var nr = g.Attach("receiver_1", rec);
                nr.Attach("barrel", P("Standard Barrel"));
                nr.Attach("ammo", P("FMJ"));
                nr.Attach("action", ActionMigration.StockActionOf(rec, Parts.Find)!);
                nr.Attach("feed", p);
                return;
            }
            (onGrip ? g : r).Attach(socket, p);
        });
    }
}
