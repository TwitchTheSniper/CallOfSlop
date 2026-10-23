using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The workbench's barrels, receivers and grips (docs/zombies/WEAPON-BUILDER.md, "Barrels and receivers", "Grips and
    /// Chain fire"): the screenshot build (a Stampede under a Thumper chain gun) takes a barrel on its own receiver, and
    /// a refusal explains the real reason; every socket is judged against the receiver it hangs under; every barrel fits
    /// every receiver (and splitter outputs, and muzzle devices on top); the receiver sizes the barrel (pistol &lt; rifle
    /// &lt; heavy) with the muzzle point and devices following; barrel stats scale on top of the receiver's calibre;
    /// double-click removal with undo; every grip slot count 1..9, the Chain fire checkbox (saves, undo, old twin grips
    /// migrated); the new receivers across every rarity assemble and size their barrels.
    /// </summary>
    private static void CheckWorkbenchBarrels()
    {
        PartDef P(string name) => Parts.Find(name) ?? throw new InvalidOperationException($"barrels test needs the part '{name}'");
        var std = P("Standard Barrel");

        // ---- The screenshot build -------------------------------------------------------------------------------
        const string shot = "Minigun|volley|Pistol Grip{receiver_1:Thumper Chain Gun Receiver{barrel:Over-Under Splitter{barrel_1:Long Barrel,barrel_2:Long Barrel},feed:Heavy Box,ammo:.50 BMG},"
                            + "receiver_2:Stampede Receiver{feed:Stick Mag},receiver_3:Stampede Receiver{feed:Stick Mag}}";
        var shotDropped = new List<string>();
        var shotBuild = WeaponBuild.Parse(shot, Parts.Find, shotDropped); // saved before actions were parts: each receiver gets its stock action
        var sm = new BenchModel(shotBuild, BenchStock.Unlimited);
        int r2 = sm.SpotOf("receiver_2", "barrel"), r3 = sm.SpotOf("receiver_3", "barrel");
        var why2 = r2 >= 0 ? BuildRules.SocketWhy(sm.Spots[r2].Parent, sm.Spots[r2].Socket, std) : "no spot";
        var why3 = r3 >= 0 ? BuildRules.SocketWhy(sm.Spots[r3].Parent, sm.Spots[r3].Socket, std) : "no spot";
        var nValid = sm.ValidSpots(std, new bool[sm.Spots.Count]);
        var nowhere = sm.WhyNowhere(std);
        Line($"screenshot build: {sm.Build.PartCount} parts, {sm.Build.Weight:0.0} kg (carry {sm.Build.CarryWeight:0.#}); Standard Barrel on receiver 2: {why2 ?? "fits"}, receiver 3: {why3 ?? "fits"}; {nValid} valid sockets");
        if (shotDropped.Count > 0 || sm.Build.PartCount != 14 || shotBuild.ActionsAdded != 3 || why2 != null || why3 != null || nValid < 2)
            Fail($"barrels: the screenshot build's Stampede receivers should take a Standard Barrel ({string.Join("; ", shotDropped)}; {why2}; {why3})");
        var placed2 = sm.Place(r2, std, out _);
        var placed3 = sm.Place(sm.SpotOf("receiver_3", "barrel"), std, out _);
        Line($"  placed on receiver 2: {placed2 ?? "ok"}, receiver 3: {placed3 ?? "ok"}; still needs: {string.Join("; ", sm.Asm.Problems.Select(p => p.Message))}");
        if (placed2 != null || placed3 != null || sm.Build.At("receiver_2/barrel")?.Part != std || sm.Build.At("receiver_3/barrel")?.Part != std)
            Fail("barrels: placing a Standard Barrel on the screenshot build's receivers 2 and 3 failed");
        // Each socket against its own receiver: the Stampedes take 9mm and refuse .50 (naming the Stampede), the Thumper the reverse.
        int a1 = sm.SpotOf("receiver_1", "ammo"), a2 = sm.SpotOf("receiver_2", "ammo");
        var nineOn2 = BuildRules.SocketWhy(sm.Spots[a2].Parent, sm.Spots[a2].Socket, P("9mm Ball"));
        var fiftyOn2 = BuildRules.SocketWhy(sm.Spots[a2].Parent, sm.Spots[a2].Socket, P(".50 BMG"));
        var nineOn1 = BuildRules.SocketWhy(sm.Spots[a1].Parent, sm.Spots[a1].Socket, P("9mm Ball"));
        var needs = sm.Asm.Problems.Where(p => p.Incomplete).Select(p => p.Message).ToList();
        Line($"  per receiver: 9mm on receiver 2 {nineOn2 ?? "fits"}; .50 on receiver 2: {fiftyOn2}; 9mm on receiver 1: {nineOn1}; needs: {string.Join("; ", needs)}");
        if (nineOn2 != null || fiftyOn2 == null || !fiftyOn2.Contains("Stampede") || nineOn1 == null || !nineOn1.Contains("Thumper")
            || needs.Count(n => n.Contains("Stampede Receiver needs ammo")) != 2 || needs.Any(n => n.Contains("barrel")))
            Fail("barrels: every socket should be judged against the receiver it hangs under (and the needs list name it)");
        // A refusal says the real reason: the weight, not another receiver's bore.
        const string heavyText = "Heavy|volley|Pistol Grip{receiver_1:Thumper Chain Gun Receiver{barrel:Over-Under Splitter{barrel_1:Long Barrel,barrel_2:Long Barrel},feed:Heavy Box,ammo:.50 BMG},"
                                 + "receiver_2:Thumper Chain Gun Receiver{feed:Heavy Box,ammo:.50 BMG}}";
        var hm = new BenchModel(WeaponBuild.Parse(heavyText, Parts.Find), BenchStock.Unlimited);
        var bigWhy = hm.WhyNowhere(P("Big Iron Barrel"));
        var bigShort = hm.WhyNowhereShort(P("Big Iron Barrel"));
        Line($"  too heavy for a Big Iron ({hm.Build.Weight:0.0} kg of {hm.Build.CarryWeight * 2:0.#}): '{bigWhy}' / short '{bigShort}'");
        if (!bigWhy.Contains("Too heavy") || bigWhy.Contains("made for") || bigShort.Contains("bore"))
            Fail("barrels: a barrel refused by the weight limit should say so, not blame a receiver's bore");

        // ---- Every barrel fits every receiver (and splitter outputs, and every muzzle device on it) ----------------
        var receivers = Parts.InCategory(PartCategory.Receiver).ToList();
        var barrels = Parts.InCategory(PartCategory.Barrel).ToList();
        var muzzles = Parts.InCategory(PartCategory.Muzzle).ToList();
        var y = P("Y-Splitter");
        int pairs = 0, refused = 0;
        foreach (var r in receivers)
        {
            var root = new BuildNode(P("Rifle Grip"));
            var rn = root.Attach("receiver_1", r);
            foreach (var s in r.Sockets.Where(x => x.Type == "barrel"))
                foreach (var bp in barrels)
                {
                    pairs++;
                    if (BuildRules.SocketWhy(rn, s, bp) is { } w)
                    {
                        if (refused++ < 3)
                            Fail($"barrels: {bp.Name} refused on the {r.Name}'s {s.Name}: {w}");
                        continue;
                    }
                    var bn = rn.Attach(s.Name, bp);
                    foreach (var ms in bp.Sockets.Where(x => x.Type == "muzzle"))
                        foreach (var mz in muzzles)
                            if (BuildRules.SocketWhy(bn, ms, mz) is { } mw && refused++ < 3)
                                Fail($"barrels: {mz.Name} refused on a {bp.Name} on the {r.Name}: {mw}");
                    if (s.Accepts.Length == 0 || s.Accepts.Contains(PartCategory.Splitter))
                    {
                        var sn = rn.Attach(s.Name, y);
                        if (BuildRules.SocketWhy(sn, y.Sockets[0], bp) is { } sw && refused++ < 3)
                            Fail($"barrels: {bp.Name} refused on a Y-Splitter on the {r.Name}: {sw}");
                    }
                }
        }
        Line($"every barrel x every receiver socket: {pairs} pairs ({barrels.Count} barrels, {receivers.Count} receivers, muzzle devices and splitter outputs too), {refused} refused");

        // ---- The receiver sizes the barrel ------------------------------------------------------------------------
        (Vector3 Scale, float Width, float Length, Vector3 Muzzle, float DeviceGap) Sized(string receiver, string barrel, string? device)
        {
            var b = new WeaponBuild("Size", P("Rifle Grip"));
            var rn = b.Root!.Attach("receiver_1", P(receiver));
            var bn = rn.Attach(P(receiver).Sockets.First(s => s.Type == "barrel").Name, P(barrel));
            BuildNode? dn = device != null ? bn.Attach("muzzle", P(device)) : null;
            var model = BuildModel.Build(b, Vector3.One);
            var (from, _) = BuildModel.NodeRange(model, bn);
            var part = model.Mesh.Parts[from];
            var scale = BuildRules.BarrelScale(bn);
            var gap = 0f;
            if (dn != null)
            {
                var socket = model.Nodes[bn].ToWorld(bn.Part.Sockets.First(s => s.Type == "muzzle").Pos * scale);
                gap = Vector3.Distance(socket, model.Nodes[dn].WorldPosition);
            }
            return (scale, MathF.Min(part.Sx, part.Sy), part.Sz, model.Muzzles[0] - model.Nodes[bn].WorldPosition, gap);
        }
        var pistol = Sized("Plinker Receiver", "Standard Barrel", null);
        var rifleS = Sized("Workhorse Receiver", "Standard Barrel", null);
        var heavyS = Sized("Thumper Chain Gun Receiver", "Standard Barrel", null);
        var cannon = Sized("Hammerfall Autocannon Receiver", "Standard Barrel", null);
        var bigOnHeavy = Sized("Thumper Chain Gun Receiver", "Big Iron Barrel", null);
        Line($"barrel size (Standard Barrel): pistol {pistol.Width * 1000:0.0} x {pistol.Length * 1000:0} mm, rifle {rifleS.Width * 1000:0.0} x {rifleS.Length * 1000:0}, heavy {heavyS.Width * 1000:0.0} x {heavyS.Length * 1000:0}, "
             + $"autocannon {cannon.Width * 1000:0.0} x {cannon.Length * 1000:0}; muzzle {-pistol.Muzzle.Z * 1000:0} / {-rifleS.Muzzle.Z * 1000:0} / {-heavyS.Muzzle.Z * 1000:0} mm out; Big Iron on a heavy receiver scale {bigOnHeavy.Scale}");
        if (!(pistol.Width < rifleS.Width && rifleS.Width < heavyS.Width && heavyS.Width < cannon.Width) || !(pistol.Length < rifleS.Length && rifleS.Length < heavyS.Length)
            || MathF.Abs(rifleS.Length - 0.2f) > 1e-4f || rifleS.Scale != Vector3.One || bigOnHeavy.Scale != Vector3.One
            || !(-pistol.Muzzle.Z < -rifleS.Muzzle.Z && -rifleS.Muzzle.Z < -heavyS.Muzzle.Z) || MathF.Abs(-pistol.Muzzle.Z - 0.2f * pistol.Scale.Z) > 1e-4f)
            Fail("barrels: the receiver should size its barrel (pistol < rifle < heavy < autocannon; a rifle as drawn; a barrel drawn for its own class unchanged) and move the muzzle point with it");
        // A muzzle device sits on the scaled muzzle, and the shot comes out of the device.
        var dp = Sized("Plinker Receiver", "Standard Barrel", "Suppressor");
        var dh = Sized("Thumper Chain Gun Receiver", "Long Barrel", "Suppressor");
        Line($"suppressor on the scaled barrel: gap to the barrel's muzzle {dp.DeviceGap * 1000:0.000} / {dh.DeviceGap * 1000:0.000} mm; shot point {-dp.Muzzle.Z * 1000:0} mm (pistol) and {-dh.Muzzle.Z * 1000:0} mm (heavy Long Barrel) out");
        if (dp.DeviceGap > 1e-4f || dh.DeviceGap > 1e-4f || MathF.Abs(-dp.Muzzle.Z - (0.2f * dp.Scale.Z + 0.18f)) > 1e-3f || MathF.Abs(-dh.Muzzle.Z - (0.36f * dh.Scale.Z + 0.18f)) > 1e-3f)
            Fail("barrels: a muzzle device should sit on the barrel's scaled muzzle point and the shot leave from its end");

        // ---- Stats: the barrel's modifiers on top of the receiver's calibre ------------------------------------------
        float RangeOf(string grip, string receiver, string barrel, string feed, string ammo) =>
            WeaponBuilder.Assemble(BuildOf($"{grip}{{receiver_1:{receiver}{{barrel:{barrel},feed:{feed},ammo:{ammo}}}}}")).Def?.RangeM ?? -1;
        var reg = Parts.Registry;
        float k(string bore) => reg.ClassOf(bore).Strength;
        var pStd = RangeOf("Service Pistol Grip", "Plinker Receiver", "Standard Barrel", "Stick Mag", "9mm Ball");
        var pLong = RangeOf("Service Pistol Grip", "Plinker Receiver", "Long Barrel", "Stick Mag", "9mm Ball");
        var rStd = RangeOf("Service Rifle Grip", "Workhorse Receiver", "Standard Barrel", "Box Mag", "5.56 FMJ");
        var rLong = RangeOf("Service Rifle Grip", "Workhorse Receiver", "Long Barrel", "Box Mag", "5.56 FMJ");
        var hStd = RangeOf("Rifle Grip", "Thumper Chain Gun Receiver", "Standard Barrel", "Heavy Box", ".50 BMG");
        var hStub = RangeOf("Rifle Grip", "Thumper Chain Gun Receiver", "Stub Barrel", "Heavy Box", ".50 BMG");
        Line($"range: pistol {pStd:0.#} -> Long Barrel {pLong:0.#} m (x{pLong / pStd:0.###}, strength {k("pistol")}), rifle {rStd:0.#} -> {rLong:0.#} (x{rLong / rStd:0.###}), heavy {hStd:0.#} -> Stub {hStub:0.#} (x{hStub / hStd:0.###}, strength {k("heavy")})");
        if (MathF.Abs(pLong / pStd - (1 + 0.4f * k("pistol"))) > 0.01f || MathF.Abs(rLong / rStd - 1.4f) > 0.01f || MathF.Abs(hStub / hStd - (1 - 0.3f * k("heavy"))) > 0.01f || pLong >= rLong || hStub <= rStd)
            Fail("barrels: a Long Barrel should make a longer-range pistol (still a pistol), a Stub Barrel a shorter autocannon-class gun (still long), scaled by the bore class's strength");
        int Pellets(string text) => WeaponBuilder.Assemble(BuildOf(text)).Def?.Pellets ?? -1;
        var rifleBell = Pellets("Service Rifle Grip{receiver_1:Workhorse Receiver{barrel:Blunderbuss Bell,feed:Box Mag,ammo:5.56 FMJ}}");
        var shotBell = Pellets("Service Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Blunderbuss Bell,feed:Shell Tube,ammo:00 Buckshot}}");
        var shotStd = Pellets("Service Rifle Grip{receiver_1:Farmhand Pump Receiver{barrel:Standard Barrel,feed:Shell Tube,ammo:00 Buckshot}}");
        Line($"pellets: Blunderbuss Bell on a rifle {rifleBell}, on a pump shotgun {shotBell} (Standard Barrel {shotStd})");
        if (rifleBell != 1 || shotBell <= shotStd)
            Fail("barrels: a barrel's pellet change should count on shotguns only (a rifle keeps one bullet)");

        // ---- Standard builds still assemble ---------------------------------------------------------------------------
        var stdBuilds = StandardBuilds.All;
        var broken = stdBuilds.Where(sb => !WeaponBuilder.Assemble(sb.Build()).Ok).Select(sb => sb.Name).ToList();
        Line($"standard builds: {stdBuilds.Length} load ({StandardBuilds.LoadErrors.Count} load errors), {broken.Count} don't assemble");
        if (broken.Count > 0 || StandardBuilds.LoadErrors.Count > 0)
            Fail($"barrels: standard builds should all assemble ({string.Join(", ", broken)}; {string.Join("; ", StandardBuilds.LoadErrors)})");

        // ---- Double-click removal, with undo ---------------------------------------------------------------------------
        var stock = BenchStock.Of(sm.Build.Nodes.GroupBy(n => n.Part).Select(g => (g.Key, g.Count())));
        var dm = new BenchModel(sm.Build.Clone(), stock);
        var before = dm.Build.ToText();
        var row = dm.Rows.FindIndex(r => r.Node?.Path() == "receiver_2");
        var freeBefore = (dm.Stock.Free(P("Stampede Receiver"), dm.Build), dm.Stock.Free(P("Stick Mag"), dm.Build), dm.Stock.Free(std, dm.Build));
        var dWhy = dm.DoubleClickRow(row, out var off);
        var freeAfter = (dm.Stock.Free(P("Stampede Receiver"), dm.Build), dm.Stock.Free(P("Stick Mag"), dm.Build), dm.Stock.Free(std, dm.Build));
        var gripWhy = dm.DoubleClickRow(0, out var gripOff);
        var needRow = dm.Rows.FindIndex(r => r.Kind == BenchRowKind.Needed);
        var needWhy = dm.DoubleClickRow(needRow, out _);
        var spotWhy = dm.DoubleClickSpot(dm.SpotOf("receiver_3", "barrel"), out var spotOff);
        var afterSpot = dm.Build.At("receiver_3/barrel");
        dm.Undo();
        var undoSpot = dm.Build.At("receiver_3/barrel") != null;
        dm.Undo();
        Line($"double-click: receiver 2 row -> {dWhy ?? $"off ({off?.Walk().Count()} parts)"}; free Stampede/Stick Mag/Standard Barrel {freeBefore} -> {freeAfter}; grip row: {gripWhy}; needed row: {needWhy}; "
             + $"barrel on the gun: {spotWhy ?? "off"}; undo x2 back to start {dm.Build.ToText() == before}");
        if (dWhy != null || off?.Part.Name != "Stampede Receiver" || dm.Build.ToText() != before || freeAfter != (freeBefore.Item1 + 1, freeBefore.Item2 + 1, freeBefore.Item3 + 1)
            || gripWhy == null || gripOff != null || needWhy == null || spotWhy != null || spotOff?.Part != std || afterSpot != null || !undoSpot)
            Fail("barrels: double-clicking a part of the build should take it (and what's on it) back to your parts, the grip and empty rows refused, undo restoring it");

        // ---- Grips: every slot count, the Chain fire checkbox, old twin grips migrated ----------------------------------
        var gripCounts = Enumerable.Range(1, 9).Select(n => (n, Parts.All.Where(p => p.Frame != null && p.ReceiverCapacity == n).Select(p => p.Name).ToList())).ToList();
        Line("grips by slots: " + string.Join("; ", gripCounts.Select(g => $"{g.n}: {string.Join(", ", g.Item2)}")));
        if (gripCounts.Any(g => g.Item2.Count == 0))
            Fail($"barrels: every receiver slot count 1..9 should have a grip (missing {string.Join(", ", gripCounts.Where(g => g.Item2.Count == 0).Select(g => g.n))})");
        foreach (var (rg, slots) in new[] { ("Twin Rifle Grip", 2), ("Triple Rifle Grip", 3) })
            if (Parts.Find(rg) is not { Frame: { } rf } rp || rp.ReceiverCapacity != slots || rf.Hands != "two" || rp.Sockets.All(x => x.Name != "stock") || rp.Sockets.All(x => x.Name != "foregrip"))
                Fail($"barrels: {rg} should be a two-handed {slots}-slot grip with a stock and a foregrip socket");
        var chainModel = new BenchModel(BuildOf("Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball},receiver_2:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball}}"), BenchStock.Unlimited);
        var volleyDef = chainModel.Asm.Def;
        var changed = chainModel.SetChainFire(true);
        var chainText = chainModel.Build.ToText();
        var chainDef = chainModel.Asm.Def;
        var reparsed = WeaponBuild.Parse(chainText, Parts.Find);
        chainModel.Undo();
        Line($"chain fire: toggled {changed}, text '{chainText[..Math.Min(chainText.Length, 30)]}...', rpm volley {volleyDef?.Rpm} -> chain {chainDef?.Rpm} (turns {chainDef?.RotateGroups}); reparsed chain {reparsed.ChainFire}; undo -> chain {chainModel.Build.ChainFire}");
        if (!changed || !chainText.Contains("|rotate|") || !reparsed.ChainFire || chainDef is not { RotateGroups: true } || volleyDef is not { RotateGroups: false } || chainModel.Build.ChainFire)
            Fail("barrels: the Chain fire checkbox should switch the build to chain fire, save in its text, and undo");
        var migDropped = new List<string>();
        var mig = WeaponBuild.Parse("Old Turns|rotate|Competition Pistol Grip{receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball},receiver_2:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:9mm Ball},utility:Laser Sight}", Parts.Find, migDropped);
        var mig2 = WeaponBuild.Parse("Old Rack|rotate|Tactical Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:5.56 FMJ},utility:Laser Sight}", Parts.Find, migDropped);
        Line($"migration: Competition Pistol Grip -> {mig.Root?.Part.Name} (chain {mig.ChainFire}, complete {BuildRules.IsComplete(mig)}), Tactical Rifle Grip -> {mig2.Root?.Part.Name} (chain {mig2.ChainFire}); dropped: {string.Join("; ", migDropped)}");
        if (mig.Root?.Part.Name != "Pistol Grip" || !mig.ChainFire || !BuildRules.IsComplete(mig) || mig2.Root?.Part.Name != "Rifle Grip" || !mig2.ChainFire || migDropped.Count > 0)
            Fail("barrels: a build on an old chain-fire grip should load on the merged grip with Chain fire on, nothing dropped");

        // ---- The receivers: every rarity, each assembles and sizes its barrels ---------------------------------------------
        var byRarity = Enum.GetValues<Rarity>().Select(r => (r, receivers.Count(p => p.Rarity == r))).ToList();
        Line("receivers by rarity: " + string.Join(", ", byRarity.Select(x => $"{x.r} {x.Item2}")));
        if (byRarity.Any(x => x.Item2 == 0))
            Fail("barrels: there should be receivers at every rarity, common to wonder");
        var notBuilt = new List<string>();
        foreach (var r in receivers)
        {
            var bm = new BenchModel(new WeaponBuild("R", P("Rifle Grip")), BenchStock.Unlimited);
            bm.Place(bm.SpotOf("", "receiver_1"), r, out _);
            bm.AutoComplete(Parts.All);
            // Rotary receivers with optional barrel sockets: give them their minimum of Standard Barrels.
            if (!bm.Build.Nodes.Any(n => n.Part.Category == PartCategory.Barrel))
                foreach (var s in r.Sockets.Where(x => x.Type == "barrel").Take(Math.Max(1, r.Receiver!.MinBarrels)))
                    bm.Place(bm.SpotOf("receiver_1", s.Name), std, out _);
            var bn = bm.Build.Nodes.FirstOrDefault(n => n.Part.Category == PartCategory.Barrel);
            var want = (r.Receiver!.BarrelScale ?? reg.ClassOf(r.Receiver.Bore).Scale) / (bn != null ? reg.ClassOf(bn.Part.SizedFor).Scale : Vector3.One);
            if (!bm.Asm.Ok || bn == null || Vector3.Distance(BuildRules.BarrelScale(bn), want) > 1e-4f)
                notBuilt.Add($"{r.Name} ({string.Join("; ", bm.Asm.Problems.Select(p => p.Message))})");
        }
        Line($"every receiver auto-completes on a Rifle Grip with its barrels sized: {receivers.Count - notBuilt.Count}/{receivers.Count}");
        if (notBuilt.Count > 0)
            Fail($"barrels: receivers that don't assemble or size their barrel: {string.Join(", ", notBuilt)}");
        // The registry table matches the built-in one (the file is the source; the built-in is the fallback).
        foreach (var bore in reg.Bores)
            if (PartRegistry.BuiltInClasses.TryGetValue(bore, out var bi) && reg.ClassOf(bore) != bi)
                Fail($"barrels: Parts/_Registry.json's {bore} barrel class differs from the built-in table");
    }
}
