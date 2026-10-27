using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// How every receiver fired before the fire type became a part (the "receiver" blocks of the part files up to
    /// 2026-10-23): fire type, rpm, burst, spool-up, rpm per barrel. The regression check builds each old standard build
    /// with these numbers in place of its new action and compares.
    /// </summary>
    private static readonly Dictionary<string, ActionInfo> LegacyReceiverActions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bastille LMG Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 650 },
        ["Bucket Launcher Receiver"] = new() { Kind = ActionKind.Semi, Rpm = 60 },
        ["Buzzbox Machine Pistol Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 1100 },
        ["Courier Carbine Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 720 },
        ["Farmhand Pump Receiver"] = new() { Kind = ActionKind.Pump, Rpm = 70 },
        ["Gravedigger Auto Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 260 },
        ["Hammerfall Autocannon Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 200 },
        ["Hellwheel Rotary Receiver"] = new() { Kind = ActionKind.Rotary, Rpm = 1800, RpmPerBarrel = 400, SpoolTime = 1.2f, SpoolStart = 0.25f },
        ["Kingpin Revolver Receiver"] = new() { Kind = ActionKind.Semi, Rpm = 150 },
        ["Longbow DMR Receiver"] = new() { Kind = ActionKind.Semi, Rpm = 300 },
        ["Longreach Bolt Receiver"] = new() { Kind = ActionKind.Bolt, Rpm = 45 },
        ["Pepperbox Receiver"] = new() { Kind = ActionKind.Rotary, Rpm = 900, RpmPerBarrel = 150, SpoolTime = 0.3f, SpoolStart = 0.5f },
        ["Plinker Receiver"] = new() { Kind = ActionKind.Semi, Rpm = 420 },
        ["Railspike Coil Receiver"] = new() { Kind = ActionKind.Bolt, Rpm = 50 },
        ["Ratchet Burst SMG Receiver"] = new() { Kind = ActionKind.Burst, Rpm = 1000, BurstCount = 3, BurstDelay = 0.18f },
        ["Reaper Battle Rifle Receiver"] = new() { Kind = ActionKind.Semi, Rpm = 380 },
        ["Sawmill Receiver"] = new() { Kind = ActionKind.Chain, Rpm = 360, SpoolTime = 0.6f, SpoolStart = 0.4f },
        ["Sparkplug Energy Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 700 },
        ["Stampede Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 900 },
        ["Street Sweeper Receiver"] = new() { Kind = ActionKind.Semi, Rpm = 200 },
        ["Thumper Chain Gun Receiver"] = new() { Kind = ActionKind.Chain, Rpm = 450, SpoolTime = 0.4f, SpoolStart = 0.5f },
        ["Triad Burst Receiver"] = new() { Kind = ActionKind.Burst, Rpm = 800, BurstCount = 3, BurstDelay = 0.25f },
        ["Workhorse Receiver"] = new() { Kind = ActionKind.Auto, Rpm = 600 },
    };

    /// <summary>
    /// Receivers whose stock action changed their fire rate on purpose when actions became parts (one action serves
    /// several receivers): the receiver and why. Each stays within 15% of its old DPS.
    /// </summary>
    private static readonly Dictionary<string, string> IntendedActionChanges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Bastille LMG Receiver"] = "shares the Full-Auto Sear with the Workhorse: 650 -> 600 rpm",
        ["Plinker Receiver"] = "shares the Semi-Auto Trigger with the Reaper: 420 -> 400 rpm",
        ["Reaper Battle Rifle Receiver"] = "shares the Semi-Auto Trigger with the Plinker: 380 -> 400 rpm",
        ["Railspike Coil Receiver"] = "shares the Turn Bolt with the Longreach: 50 -> 45 rpm",
        ["Sparkplug Energy Receiver"] = "shares the Carbine Sear with the Courier: 700 -> 720 rpm",
    };

    /// <summary>
    /// The fire type is a part (docs/zombies/WEAPON-BUILDER.md, "Actions: the fire type is a part"): every receiver has one
    /// required action socket and names a stock action; every action fits every receiver and touches it on the model;
    /// receivers carry no fire type; old builds (workbench.txt, the old standard build texts) migrate to their stock
    /// actions and keep their fire mode and about their DPS; every fire type fires as it should through the real trigger
    /// loop (auto, semi, burst, pump, lever, bolt, single shot, muzzle-loaded, rotary and chain spool-up); a missing
    /// action is a missing part; the server takes builds as sent; actions drop in a run.
    /// </summary>
    private static void CheckWorkbenchActions()
    {
        BuildStore.ReadOnly = true;
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"actions test needs the part '{name}'");
        var receivers = Parts.InCategory(PartCategory.Receiver).ToList();
        var actions = Parts.InCategory(PartCategory.Action).ToList();
        Line($"{actions.Count} actions: " + string.Join(", ", actions.Select(a => $"{a.Name} ({a.Rarity}, {a.Action!.FireType}, {(a.Action.RpmPerBarrel > 0 ? $"{a.Action.RpmPerBarrel}/barrel" : $"{a.Action.Rpm}")})")));
        foreach (var k in Enum.GetValues<ActionKind>())
            if (!actions.Any(a => a.Action!.Kind == k))
                Fail($"actions: no action part fires {ActionInfo.Name(k)}");
        foreach (var r in Enum.GetValues<Rarity>())
            if (!actions.Any(a => a.Rarity == r))
                Fail($"actions: no {r} action");

        // ---- Receivers: one required action socket, a stock action, every action fits ----------------------------
        foreach (var r in receivers)
        {
            var sockets = r.Sockets.Where(s => s.Type == "action").ToList();
            if (sockets.Count != 1 || !sockets[0].Required)
                Fail($"actions: the {r.Name} should have exactly one required action socket (has {sockets.Count})");
            if (ActionMigration.StockActionOf(r, Parts.Find) is not { } stock)
                Fail($"actions: the {r.Name}'s stockAction '{r.Receiver!.StockAction}' isn't an action part");
            else if (stock.Rarity > r.Rarity)
                Fail($"actions: the {r.Name} ({r.Rarity}) comes with the {stock.Name} ({stock.Rarity}): its stock action shouldn't be rarer than it");
            // (A receiver added after actions became parts has no legacy entry: it never fired on its own.)
            if (sockets.Count == 0)
                continue;
            var root = new BuildNode(P("Rifle Grip"));
            var rn = root.Attach("receiver_1", r);
            foreach (var a in actions)
                if (BuildRules.SocketWhy(rn, sockets[0], a) is { } why)
                    Fail($"actions: the {a.Name} should fit the {r.Name}: {why}");
        }
        Line($"{receivers.Count} receivers: one required action socket each, a stock action, every action fits every one");

        // ---- Models: each action touches each receiver (no floating trigger groups) --------------------------------
        var modelled = 0;
        foreach (var r in receivers)
            foreach (var a in actions)
            {
                var b = new WeaponBuild("Action Fit", P("Rifle Grip"));
                b.Root!.Attach("receiver_1", r).Attach("action", a);
                CheckBuildConnected($"{a.Name} on the {r.Name}", b);
                modelled++;
            }
        Line($"models: {modelled} receiver x action pairs, every action touching its receiver");

        // ---- Old builds migrate: the old standard build texts give today's files, complete ---------------------------
        var migrated = 0;
        // Standard builds whose receivers all use their stock action are the old guns; the rest (a receiver on another
        // action, like the Burst Marksman since its burst receiver went, 2026-10-27) never had a text without actions.
        bool StockActions(StandardBuild sb) => sb.Build().Receivers.All(r => WeaponBuild.ActionNodeOf(r) is { } an && an.Part == ActionMigration.StockActionOf(r.Part, Parts.Find));
        foreach (var sb in StandardBuilds.All.Where(s => !s.IsMelee && StockActions(s)))
        {
            var now = sb.Build();
            var oldTree = sb.Tree;
            foreach (var a in actions)
                oldTree = oldTree.Replace($",action:{a.Name}", "").Replace($"action:{a.Name},", "");
            var old = WeaponBuild.Parse($"{sb.Name}|{(sb.ChainFire is { } c ? c ? "rotate" : "volley" : "grip")}|{oldTree}", Parts.Find);
            if (oldTree.Contains("action:") || old.ActionsAdded != now.Receivers.Count || old.ToText() != now.ToText() || !BuildRules.IsComplete(old))
                Fail($"actions: the old text of '{sb.Name}' should load with one stock action per receiver, exactly today's build ({old.ActionsAdded} added):\n    {old.ToText()}\n    {now.ToText()}");
            else
                migrated++;
        }
        // A workbench.txt line from before actions: loads with them, flagged to save back; an undo step is taken as written.
        BuildStore.Load(["slot1=Old Pistol|volley|Service Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}"], Parts.Find, out var droppedOld);
        var slot = BuildStore.Slots[0];
        var asIs = WeaponBuild.Parse("Old Pistol|volley|Service Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}", Parts.Find, migrate: false);
        var partly = WeaponBuild.Parse("Two|volley|Pistol Grip{receiver_1:Plinker Receiver{action:Hair Trigger},receiver_2:Plinker Receiver}", Parts.Find);
        Line($"migration: {migrated} old standard build texts give today's builds; an old workbench.txt line -> {slot?.ToText()} ({slot?.ActionsAdded} added, dropped {droppedOld.Count}); "
             + $"as written (undo, server): needs '{BuildRules.Check(asIs).FirstOrDefault().Message}'; a build that has actions already gets none added ({partly.ActionsAdded})");
        if (slot == null || slot.ActionsAdded != 1 || WeaponBuild.ActionNodeOf(slot.Receivers[0])?.Part.Name != "Semi-Auto Trigger" || droppedOld.Count > 0 || !BuildStore.Lines()[1].Contains("action:Semi-Auto Trigger"))
            Fail("actions: an old workbench.txt build should load with its stock action and save with it");
        if (asIs.ActionsAdded != 0 || BuildRules.IsComplete(asIs) || !BuildRules.Check(asIs).Any(p => p.Incomplete && p.Message == "Plinker Receiver needs an action."))
            Fail("actions: a build taken as written (undo steps, what a client sends the server) with no action should need one, not get one");
        if (partly.ActionsAdded != 0 || partly.Receivers[1].Child("action") != null)
            Fail("actions: a build with an action anywhere is current: an empty action socket stays empty (the player took it off)");
        Array.Clear(BuildStore.Slots);

        // ---- The old builds keep their feel: fire mode the same, DPS close (or a listed change) --------------------------
        var legacyParts = new Dictionary<string, PartDef>(StringComparer.OrdinalIgnoreCase);
        PartDef Legacy(BuildNode receiver)
        {
            if (legacyParts.TryGetValue(receiver.Part.Name, out var lp))
                return lp;
            var stock = WeaponBuild.ActionNodeOf(receiver)!.Part;
            // The old receiver's own numbers as an action, as heavy as the stock action it gave its weight to.
            return legacyParts[receiver.Part.Name] = new PartDef
            {
                Name = "Legacy " + receiver.Part.Name, Category = PartCategory.Action, Mount = "action", Weight = stock.Weight,
                Action = LegacyReceiverActions[receiver.Part.Name], Boxes = stock.Boxes,
            };
        }
        var compared = 0;
        foreach (var sb in StandardBuilds.All.Where(s => !s.IsMelee && StockActions(s) && s.Build().Receivers.All(r => LegacyReceiverActions.ContainsKey(r.Part.Name))))
        {
            var now = WeaponBuilder.Assemble(sb.Build()).Def;
            var before = sb.Build();
            foreach (var r in before.Receivers)
                if (LegacyReceiverActions.ContainsKey(r.Part.Name) && WeaponBuild.ActionNodeOf(r) is { } an)
                    r.Attach(an.ParentSocket!.Name, Legacy(r));
            var old = WeaponBuilder.Assemble(before).Def;
            if (now == null || old == null)
            {
                Fail($"actions: '{sb.Name}' doesn't assemble before ({old != null}) or after ({now != null})");
                continue;
            }
            compared++;
            var ratio = now.Dps / MathF.Max(1e-3f, old.Dps);
            var listed = sb.Build().Receivers.Select(r => r.Part.Name).Where(IntendedActionChanges.ContainsKey).Distinct().ToList();
            var note = listed.Count > 0 ? $" (intended: {string.Join("; ", listed.Select(n => IntendedActionChanges[n]))})" : "";
            Line($"  {sb.Name}: {old.ModeName} {old.Rpm} rpm, DPS {old.Dps:0} -> {now.ModeName} {now.Rpm} rpm, DPS {now.Dps:0} (x{ratio:0.000}){note}");
            if (now.Mode != old.Mode || now.BurstCount != old.BurstCount || MathF.Abs(now.SpoolTime - old.SpoolTime) > 1e-4f || now.Mag != old.Mag || MathF.Abs(now.Reload - old.Reload) > 1e-3f)
                Fail($"actions: '{sb.Name}' fires differently since actions became parts ({old.ModeName}/{old.BurstCount}/{old.SpoolTime}/{old.Mag} -> {now.ModeName}/{now.BurstCount}/{now.SpoolTime}/{now.Mag})");
            if (MathF.Abs(ratio - 1) > 0.15f || (MathF.Abs(ratio - 1) > 0.002f && listed.Count == 0))
                Fail($"actions: '{sb.Name}' DPS {old.Dps:0} -> {now.Dps:0} (x{ratio:0.000}): an unlisted or too big change");
            if (MathF.Abs(sb.Build().Weight - before.Weight) > 1e-3f)
                Fail($"actions: '{sb.Name}' weighs {sb.Build().Weight:0.00} kg, {before.Weight:0.00} kg before (the receiver gave its stock action its weight)");
        }
        Line($"feel kept: {compared} standard builds compared with their receivers' old numbers");

        // ---- Each fire type through the real trigger loop ---------------------------------------------------------------
        string Rifle(string action) => $"Service Rifle Grip{{receiver_1:Workhorse Receiver{{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ,action:{action}}}}}";
        World.SetMap(Maps.Find("Workbench Range") ?? Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.BuilderRange = true;
        var me = game.Local!;
        me.Alive = true;
        game.SelfTestPerks(me, PerkSet.Empty);
        const double Frame = 1.0 / 240;
        var clock = 1000.0;
        // Fires def for `seconds`: the trigger held throughout, pulled once at the start (or every frame: spam).
        List<double> Fire(WeaponDef def, double seconds, bool spam)
        {
            game.UseBuiltWeapons(def, def);
            game.EquipUntil = 0;
            game.NextFireTime = 0;
            game.Active.Ammo = game.Active.Def.Mag;
            game.Active.ReloadUntil = 0;
            // Let any spool wind down (a gun without one leaves it full; the loop steps a quarter second at most).
            for (var i = 0; i < 40; i++)
                game.UpdateTrigger(clock += 0.25, false, false);
            var shots = new List<double>();
            var start = clock;
            for (var t = 0.0; t < seconds; t += Frame)
            {
                var now = start + t;
                game.SelfTestWeaponFrame(now, (float)Frame);
                var before = game.LastShotTime;
                var ammo = game.Active.Ammo;
                game.UpdateTrigger(now, true, spam || t == 0);
                for (var i = game.Active.Ammo; i < ammo; i++)
                    shots.Add(t);
                if (shots.Count == 0 && game.LastShotTime != before)
                    shots.Add(t);
            }
            clock = start + seconds;
            return shots;
        }
        WeaponDef Def(string text) => WeaponBuilder.Assemble(BuildOf(text)).Def ?? throw new InvalidOperationException($"actions test: '{text}' isn't complete");
        void Expect(string what, List<double> shots, int min, int max)
        {
            Line($"  {what}: {shots.Count} shot(s) at {string.Join(" ", shots.Take(8).Select(s => s.ToString("0.000")))}{(shots.Count > 8 ? " ..." : "")}");
            if (shots.Count < min || shots.Count > max)
                Fail($"actions: {what}: {shots.Count} shots, expected {min}-{max}");
        }
        try
        {
            var auto = Def(Rifle("Full-Auto Sear"));
            Expect("full auto, 600 rpm, held 1 s", Fire(auto, 1.0, false), 10, 11);
            var semi = Def(Rifle("Semi-Auto Trigger"));
            Expect("semi-auto, held 1 s (one pull)", Fire(semi, 1.0, false), 1, 1);
            Expect("semi-auto, 400 rpm, pulled every frame for 1 s", Fire(semi, 1.0, true), 6, 7);
            var burst = Def(Rifle("Three-Round Burst Group"));
            var oneBurst = Fire(burst, 1.0, false);
            Expect("3-round burst, one pull", oneBurst, 3, 3);
            // (The first round may come a frame early: the trigger loop lets a shot catch up by up to 20 ms.)
            if (oneBurst.Count == 3 && MathF.Abs((float)(oneBurst[2] - oneBurst[1]) - burst.FireInterval) > 0.01f)
                Fail($"actions: a burst's rounds should be {burst.FireInterval:0.000} s apart ({string.Join(" ", oneBurst.Select(x => x.ToString("0.000")))})");
            // Pulling all the time: 3 rounds, then the burst delay, again: cycle 3 x 0.075 + 0.25 = 0.475 s.
            Expect("3-round burst, pulled every frame for 2 s", Fire(burst, 2.0, true), 12, 15);
            Expect("2-round binary trigger, one pull", Fire(Def(Rifle("Binary Trigger")), 1.0, false), 2, 2);
            Expect("pump, 70 rpm, pulled every frame for 2 s", Fire(Def(Rifle("Pump Slide")), 2.0, true), 3, 3);
            Expect("lever, 110 rpm, pulled every frame for 2 s", Fire(Def(Rifle("Lever Action")), 2.0, true), 4, 4);
            Expect("bolt, 45 rpm, pulled every frame for 2 s", Fire(Def(Rifle("Turn Bolt")), 2.0, true), 2, 2);
            foreach (var name in (string[])["Break Action", "Muzzleloader Ramrod"])
            {
                var d = Def(Rifle(name));
                var shots = Fire(d, d.Reload + 0.4, true);
                Expect($"{d.ModeName} {name} (mag {d.Mag}, reload {d.Reload:0.00} s, {d.Damage} damage vs {auto.Damage}), pulled every frame", shots, 2, 2);
                if (d.Mag != 1 || shots.Count == 2 && shots[1] - shots[0] < d.Reload - 0.01 || d.Damage <= auto.Damage)
                    Fail($"actions: the {name} should hold one round, reload after every shot ({d.Reload:0.00} s) and hit harder");
            }
            var muzzle = Def(Rifle("Muzzleloader Ramrod"));
            var single = Def(Rifle("Break Action"));
            if (!(muzzle.Reload > single.Reload * 3 && muzzle.Damage > single.Damage))
                Fail($"actions: muzzle-loading should be the slow, big hit ({muzzle.Reload:0.0} s / {muzzle.Damage} vs a break action's {single.Reload:0.0} s / {single.Damage})");
            // Spool-up: the minigun fires slowly at first, then at full rate.
            var minigun = Def(StandardBuilds.Find("Minigun")!.Tree);
            var spin = Fire(minigun, 2.5, false);
            int In(List<double> s, double a, double b) => s.Count(x => x >= a && x < b);
            Line($"  rotary (Minigun, {minigun.Rpm} rpm, spool {minigun.SpoolTime} s from {minigun.SpoolStart:0.00}): {In(spin, 0, 0.5)} shots in the first 0.5 s, {In(spin, 2, 2.5)} in the last");
            if (!(In(spin, 0, 0.5) < In(spin, 2, 2.5) * 0.7f) || MathF.Abs(In(spin, 2, 2.5) - minigun.Rpm / 120f) > 3)
                Fail($"actions: a rotary action should spin up to its full rate ({minigun.Rpm} rpm, spool {minigun.SpoolTime} from {minigun.SpoolStart}: {In(spin, 0, 0.5)} shots in the first 0.5 s, {In(spin, 2, 2.5)} in the last, {spin.Count} in all)");
            var chain = Def("Rifle Grip{receiver_1:Thumper Chain Gun Receiver{barrel:Big Iron Barrel,feed:Belt Box,ammo:Armour Piercing,action:Chain Drive Motor}}");
            var wind = Fire(chain, 1.5, false);
            var firstGap = wind.Count > 2 ? wind[1] - wind[0] : 0;
            var lastGap = wind.Count > 2 ? wind[^1] - wind[^2] : 0;
            Line($"  chain (Thumper, {chain.Rpm} rpm, spool {chain.SpoolTime} s): {wind.Count} shots in 1.5 s, first gap {firstGap:0.000} s, last {lastGap:0.000} s (full rate {chain.FireInterval:0.000} s)");
            if (wind.Count < 4 || firstGap < lastGap * 1.5 || MathF.Abs((float)lastGap - chain.FireInterval) > 0.01f)
                Fail("actions: a chain action should wind up to its full rate");
        }
        catch (InvalidOperationException ex)
        {
            Fail(ex.Message);
        }

        // ---- A cluster receiver with a plain action: all barrels at once, conserving the shot ----------------------------
        var six = string.Join(",", Enumerable.Range(1, 6).Select(i => $"barrel_{i}:Standard Barrel"));
        var rotaryAsm = WeaponBuilder.Assemble(BuildOf($"Rifle Grip{{receiver_1:Hellwheel Rotary Receiver{{{six},feed:Belt Box,ammo:FMJ,action:Electric Rotary Drive}},foregrip:Chaingun Handle}}"));
        var volleyAsm = WeaponBuilder.Assemble(BuildOf($"Rifle Grip{{receiver_1:Hellwheel Rotary Receiver{{{six},feed:Belt Box,ammo:FMJ,action:Full-Auto Sear}},foregrip:Chaingun Handle}}"));
        var (rotary, volley) = (rotaryAsm.Def, volleyAsm.Def);
        if (rotary == null || volley == null)
            Fail($"actions: a Hellwheel with a rotary or a full-auto action should both be legal ({string.Join("; ", rotaryAsm.Problems.Concat(volleyAsm.Problems).Select(p => p.Message))})");
        else
        {
            var rr = Game.ShotRays(rotary, Vector3.Zero, -Vector3.UnitZ, 0, 0).Count;
            var vr = Game.ShotRays(volley, Vector3.Zero, -Vector3.UnitZ, 0, 0);
            var vTotal = vr.Sum(r => r.Damage);
            Line($"Hellwheel: rotary action {rr} barrel per shot at {rotary.Rpm} rpm; full-auto sear {vr.Count} barrels at once at {volley.Rpm} rpm, {vTotal:0.0} damage a shot (one barrel {rotary.Damage})");
            if (rr != 1 || vr.Count != 6 || MathF.Abs(vTotal - rotary.Damage) > 1f || volley.Rpm != 600)
                Fail("actions: a cluster receiver should take turns with a rotary action, and fire every barrel at 1/6 with any other");
        }

        // ---- The stats panel and the workbench ---------------------------------------------------------------------------
        var bm = new BenchModel(BuildOf("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ}}"), BenchStock.Unlimited);
        var shown = bm.FireType;
        var removeWhy = bm.Remove(bm.Build.At("receiver_1/action")!);
        var bare = (bm.FireType, bm.Asm.Ok, bm.Rows.Any(r => r.Kind == BenchRowKind.Needed && r.Socket?.Type == "action"));
        bm.Undo();
        Line($"workbench: fire type '{shown}'; action taken off ({removeWhy ?? "ok"}): '{bare.Item1}', complete {bare.Item2}, a needed action row {bare.Item3}; undo -> '{bm.FireType}'");
        if (shown != "full auto (Full-Auto Sear)" || removeWhy != null || bare.Item1 != "no action" || bare.Item2 || !bare.Item3 || bm.FireType != shown)
            Fail("actions: the workbench should show the fire type, and a receiver with its action taken off should need one (and fire nothing)");

        // ---- Runs: actions drop -------------------------------------------------------------------------------------------
        var t2 = RunTables.Current;
        var firstRound = Enum.GetValues<Rarity>().Where(r => actions.Any(a => a.Rarity == r))
            .Select(r => (r, Enumerable.Range(1, 60).FirstOrDefault(round => Enumerable.Range(0, 400).Any(i => RunTables.Roll(t2.PartDrops, round, (uint)i) == r)))).ToList();
        Line("actions drop from round: " + string.Join(", ", firstRound.Select(x => $"{x.r} {x.Item2}")));
        if (firstRound.Any(x => x.Item2 == 0) || firstRound.First(x => x.r == Rarity.Common).Item2 != 1)
            Fail("actions: every action tier should be able to drop in a run (common ones from round 1)");
        foreach (var sb in StandardBuilds.From("start").Where(s => !s.IsMelee))
            if (!BuildRules.IsComplete(sb.Build()))
                Fail($"actions: the starting gun '{sb.Name}' should be complete");
    }
}
