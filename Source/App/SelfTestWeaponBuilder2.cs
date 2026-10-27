using System.Globalization;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The user's decisions on the weapon builder (docs/zombies/WEAPON-BUILDER.md, "Decisions"): the grip fixes the fire
    /// style; every splitter conserves damage (rarity buys accuracy); damage multipliers and receiver upgrades do what
    /// they say; the shared feed pools rounds by its rules; weight relief and the late-game frames; the standard builds
    /// and drop tables load; the weapon-slot upgrade; the push cart's physics (rolls with you, collides, held at the bar,
    /// curbs yes, steps and steep slopes no) and its parking as a turret; melee builds (stats, reach, cleave, stun);
    /// standard multiplayer melee unchanged; determinism throughout.
    /// </summary>
    private static void CheckBuilder2()
    {
        CheckFixedFireStyle();
        CheckSplittersConserve();
        CheckMultipliersAndUpgrades();
        CheckSharedFeed();
        CheckWeightProgression();
        CheckStandardBuilds();
        CheckDropTables();
        CheckSlotUpgrade();
        CheckCartPhysics();
        CheckCartParking();
        CheckMeleeBuilds();
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static WeaponBuild BuildOf(string tree, string name = "Test")
    {
        var dropped = new List<string>();
        var b = WeaponBuild.Parse($"{name}|grip|{tree}", Parts.Find, dropped); // "grip": the grip's default fire style
        if (dropped.Count > 0)
            Fail($"test build '{name}': dropped {string.Join("; ", dropped)}");
        return b;
    }

    private static float RayTotal(WeaponDef d)
    {
        float sum = 0;
        foreach (var r in Game.ShotRays(d, new Vector3(0, 1.6f, 0), -Vector3.UnitZ, 0.01f, 0))
            sum += r.Damage;
        return sum;
    }

    private static void CheckFixedFireStyle()
    {
        var grips = Parts.All.Where(p => p.Frame != null).ToList();
        foreach (var g in grips)
            if (new WeaponBuild("x", g).Style != g.Frame!.Style)
                Fail($"fire style: a build on the {g.Name} doesn't use the grip's style");
        Line("fire styles: " + string.Join(", ", grips.Select(g => $"{g.Name} {g.Frame!.Style.ToString().ToLowerInvariant()}")));
        foreach (var (name, want) in new[] { ("Pistol Grip", FireStyle.Volley), ("Rifle Grip", FireStyle.Volley), ("Exo Harness", FireStyle.Rotate) })
            if (Parts.Find(name)?.Frame?.Style != want)
                Fail($"fire style: the {name} should default to {want}");
        // The same receivers on the same grip: the build's Chain fire choice decides (the old twin grips are gone).
        var tree = "receiver_1:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:FMJ},receiver_2:Plinker Receiver{barrel:Standard Barrel,feed:Stick Mag,ammo:FMJ}";
        var volley = WeaponBuilder.Assemble(BuildOf($"Pistol Grip{{{tree}}}")).Def;
        var chained = BuildOf($"Pistol Grip{{{tree}}}");
        chained.StyleChoice = FireStyle.Rotate;
        var rotate = WeaponBuilder.Assemble(chained).Def;
        if (volley == null || rotate == null || volley.RotateGroups || !rotate.RotateGroups || rotate.Rpm <= volley.Rpm || !chained.ChainFire)
            Fail("fire style: the same receivers should volley with Chain fire off and take turns (faster) with it on");
    }

    private static void CheckSplittersConserve()
    {
        const string rx = "Rifle Grip{receiver_1:Workhorse Receiver{barrel:%B%,feed:Box Mag,ammo:FMJ}}";
        var single = WeaponBuilder.Assemble(BuildOf(rx.Replace("%B%", "Stub Barrel"))).Def!;
        var splitters = Parts.InCategory(PartCategory.Splitter).OrderBy(p => p.Rarity).ToList();
        var lastSpread = float.MaxValue;
        foreach (var s in splitters)
        {
            var outs = s.Sockets.Where(x => x.Type == "barrel").Select(x => $"{x.Name}:Stub Barrel");
            var d = WeaponBuilder.Assemble(BuildOf(rx.Replace("%B%", $"{s.Name}{{{string.Join(",", outs)}}}"), s.Name)).Def;
            if (d == null)
            {
                Fail($"splitters: a Workhorse through the {s.Name} isn't legal");
                continue;
            }
            var n = s.Sockets.Count(x => x.Type == "barrel");
            var rays = RayTotal(d);
            Line($"splitter {s.Name} ({s.Rarity}, {n} outputs, spread x{F(s.Split!.SpreadMult)}): {F(d.ShotDamage)} per shot, rays {F(rays)} (one barrel {F(single.ShotDamage)})");
            if (d.BarrelCount != n || MathF.Abs(d.ShotDamage - single.ShotDamage) > 0.5f || MathF.Abs(rays - RayTotal(single)) > 0.5f)
                Fail($"splitters: the {s.Name} doesn't conserve damage ({F(d.ShotDamage)} vs {F(single.ShotDamage)}, {d.BarrelCount} barrels)");
            if (s.Split.SpreadMult > lastSpread + 1e-4f)
                Fail($"splitters: the {s.Name} ({s.Rarity}) is less accurate than a commoner splitter");
            lastSpread = MathF.Min(lastSpread, s.Split.SpreadMult);
        }
        // Nested: a Party Tray of Tridents is still one shot's damage over 12 barrels.
        var tray = Parts.Find("Party Tray Splitter")!;
        var nested = string.Join(",", tray.Sockets.Where(x => x.Type == "barrel").Select(x => $"{x.Name}:Trident Splitter{{barrel_1:Stub Barrel,barrel_2:Stub Barrel,barrel_3:Stub Barrel}}"));
        var pp = WeaponBuilder.Assemble(BuildOf(rx.Replace("%B%", $"Party Tray Splitter{{{nested}}}"))).Def;
        if (pp == null || pp.BarrelCount != 12 || MathF.Abs(pp.ShotDamage - single.ShotDamage) > 0.5f)
            Fail("splitters: nested splitters should conserve damage too");
    }

    private static float MulOf(PartDef p, string stat) =>
        p.Effects.Where(e => e.Field.Name.Equals(stat, StringComparison.OrdinalIgnoreCase) && e.Op == "mul").Aggregate(1f, (a, e) => a * Convert.ToSingle(e.Value, CultureInfo.InvariantCulture));

    private static void CheckMultipliersAndUpgrades()
    {
        const string rx = "Rifle Grip{receiver_1:Workhorse Receiver{barrel:Standard Barrel,feed:Box Mag,ammo:FMJ%X%}}";
        var baseDef = WeaponBuilder.Assemble(BuildOf(rx.Replace("%X%", ""))).Def!;
        foreach (var m in Parts.InCategory(PartCategory.Multiplier))
        {
            var d = WeaponBuilder.Assemble(BuildOf(rx.Replace("%X%", $",amp:{m.Name}"), m.Name)).Def;
            var want = MulOf(m, nameof(WeaponDef.Damage));
            var got = d == null ? 0 : d.ShotDamage / baseDef.ShotDamage;
            Line($"damage multiplier {m.Name} ({m.Rarity}): x{F(got)} (part says x{F(want)})");
            if (want <= 1f || MathF.Abs(got - want) > want * 0.04f)
                Fail($"damage multiplier {m.Name}: damage x{F(got)}, expected x{F(want)}");
        }
        foreach (var u in Parts.InCategory(PartCategory.Upgrade))
        {
            foreach (var socket in (string[])["upgrade_rear", "upgrade_side"])
            {
                var d = WeaponBuilder.Assemble(BuildOf(rx.Replace("%X%", $",{socket}:{u.Name}"), u.Name)).Def;
                var rpm = d == null ? 0 : d.Rpm / (float)baseDef.Rpm;
                var dmg = d == null ? 0 : d.ShotDamage / baseDef.ShotDamage;
                if (socket == "upgrade_rear")
                    Line($"receiver upgrade {u.Name} ({u.Rarity}): rpm x{F(rpm)}, damage x{F(dmg)}");
                if (MathF.Abs(rpm - MulOf(u, nameof(WeaponDef.Rpm))) > 0.02f || MathF.Abs(dmg - MulOf(u, nameof(WeaponDef.Damage))) > 0.05f)
                    Fail($"receiver upgrade {u.Name} on {socket}: rpm x{F(rpm)}, damage x{F(dmg)} (part says rpm x{F(MulOf(u, "Rpm"))}, damage x{F(MulOf(u, "Damage"))})");
                if (MulOf(u, "Rpm") <= 1f && MulOf(u, "Damage") <= 1f)
                    Fail($"receiver upgrade {u.Name} raises neither fire rate nor damage");
            }
        }
        // Rear and side stack; a rotary receiver's rate per barrel scales the same way.
        var both = WeaponBuilder.Assemble(BuildOf(rx.Replace("%X%", ",upgrade_rear:Gas Overclock,upgrade_side:Lightweight Bolt"))).Def!;
        if (MathF.Abs(both.Rpm / (float)baseDef.Rpm - 1.25f * 1.08f) > 0.02f)
            Fail($"receiver upgrades: rear + side should stack (rpm {both.Rpm} vs {baseDef.Rpm})");
        var mg = "Rifle Grip{receiver_1:Hellwheel Rotary Receiver{" + string.Join(",", Enumerable.Range(1, 6).Select(i => $"barrel_{i}:Standard Barrel")) + ",feed:Backpack Hopper,ammo:FMJ%X%}}";
        var mg0 = WeaponBuilder.Assemble(BuildOf(mg.Replace("%X%", ""))).Def!;
        var mg1 = WeaponBuilder.Assemble(BuildOf(mg.Replace("%X%", ",upgrade_rear:Lightweight Bolt"))).Def!;
        Line($"receiver upgrades: Workhorse {baseDef.Rpm} -> {both.Rpm} rpm (Gas Overclock + Lightweight Bolt); minigun {mg0.Rpm} -> {mg1.Rpm} rpm (Lightweight Bolt)");
        if (MathF.Abs(mg1.Rpm - mg0.Rpm * 1.08f) > 3)
            Fail($"receiver upgrades: a rotary receiver should scale its rate per barrel ({mg0.Rpm} -> {mg1.Rpm})");
        // Deterministic.
        var again = WeaponBuilder.Assemble(BuildOf(rx.Replace("%X%", ",upgrade_rear:Gas Overclock,upgrade_side:Lightweight Bolt"))).Def!;
        if (WeaponFile.ToJson(again) != WeaponFile.ToJson(both))
            Fail("receiver upgrades: assembling twice gave different numbers");
    }

    private static void CheckSharedFeed()
    {
        var triplets = WeaponBuilder.Assemble(BuildOf("Mule Frame{receiver_1:Workhorse Receiver{barrel:Standard Barrel},receiver_2:Workhorse Receiver{barrel:Standard Barrel},receiver_3:Workhorse Receiver{barrel:Standard Barrel},hopper:Manifold Hopper{ammo:FMJ}}"));
        var fed = triplets.Build.HopperFed().Count;
        Line($"shared feed: Manifold Hopper feeds {fed} receivers, volley mag {triplets.Def?.Mag} (pool 120 / 3), problems: {string.Join("; ", triplets.Problems.Select(p => p.Message))}");
        if (triplets.Def == null || fed != 3 || triplets.Def.Mag != 40)
            Fail("shared feed: three receivers on a Manifold Hopper should be complete with a volley magazine of 120 / 3 = 40");
        // Capacity: a fourth receiver needs its own feed.
        var four = BuildOf("Mule Frame{receiver_1:Workhorse Receiver{barrel:Standard Barrel},receiver_2:Workhorse Receiver{barrel:Standard Barrel},receiver_3:Workhorse Receiver{barrel:Standard Barrel},receiver_4:Workhorse Receiver{barrel:Standard Barrel,ammo:FMJ},hopper:Manifold Hopper{ammo:FMJ}}");
        var p4 = BuildRules.Check(four);
        if (four.HopperFed().Count != 3 || !p4.Any(p => p.Message.Contains("needs a feed") && p.Message.Contains("at most")))
            Fail($"shared feed: a fourth receiver past the hopper's capacity should need a feed ({string.Join("; ", p4.Select(p => p.Message))})");
        // Bores: a shell receiver can't drink from a rifle hopper.
        var shell = BuildOf("Mule Frame{receiver_1:Farmhand Pump Receiver{barrel:Scattermouth Barrel,ammo:Buckshot},hopper:Manifold Hopper{ammo:FMJ}}");
        var ps = BuildRules.Check(shell);
        if (shell.HopperFed().Count != 0 || !ps.Any(p => p.Message.Contains("can't feed it")))
            Fail($"shared feed: a shell receiver shouldn't be fed by a rifle hopper ({string.Join("; ", ps.Select(p => p.Message))})");
        // Ammo is universal: a hopper takes any round type (Buckshot in a rifle hopper: every fed rifle fires a spread).
        var hopperBuck = BuildOf("Mule Frame{hopper:Manifold Hopper}");
        if (Parts.Find("Buckshot") is { } buck && BuildRules.WhyNot(hopperBuck, "hopper", "ammo", buck) is { } whyBuck)
            Fail($"shared feed: Buckshot should load into the Manifold Hopper (ammo fits every receiver), got: {whyBuck}");
        // An empty hopper: its receivers need ammo.
        var dry = BuildOf("Mule Frame{receiver_1:Workhorse Receiver{barrel:Standard Barrel},hopper:Manifold Hopper}");
        if (!BuildRules.Check(dry).Any(p => p.Message.Contains("needs ammo")))
            Fail("shared feed: receivers on an unloaded hopper should need ammo");
        // Rotating: the pool counts once next to the receivers' own magazines.
        var stick = WeaponBuilder.Assemble(BuildOf("Rifle Grip{receiver_1:Workhorse Receiver{barrel:Stub Barrel,feed:Stick Mag,ammo:FMJ}}")).Def!.Mag;
        var exo = StandardBuilds.Find("Exo Twelve") is { } eb ? WeaponBuilder.Assemble(eb.Build()).Def : null;
        Line($"shared feed: Exo Twelve (rotate, 6 on the manifold, 6 on stick mags of {stick}): mag {exo?.Mag} (expected {Math.Min(1000, 6 * stick + 300)})");
        if (exo == null || exo.Mag != Math.Min(1000, 6 * stick + 300))
            Fail("shared feed: a rotating build should add the pool once to its own magazines");
    }

    private static void CheckWeightProgression()
    {
        var byTier = Parts.All.Where(p => p.Frame != null && p.Rarity < Rarity.Wonder).GroupBy(p => p.Rarity).OrderBy(g => g.Key)
            .Select(g => (Tier: g.Key, Carry: g.Max(p => p.Frame!.Carry), Cap: g.Max(p => p.ReceiverCapacity))).ToList();
        Line("frames by tier: " + string.Join(", ", byTier.Select(t => $"{t.Tier} carry {F(t.Carry)} kg / {t.Cap} receivers")));
        for (var i = 1; i < byTier.Count; i++)
            if (byTier[i].Carry <= byTier[i - 1].Carry)
                Fail($"weight: {byTier[i].Tier} frames should carry more than {byTier[i - 1].Tier} ones");
        if (Parts.Find("Gantry Rig") is not { } gantry || gantry.ReceiverCapacity != 16 || gantry.Frame!.Carry < 40)
            Fail("weight: the late-game Gantry Rig should hold 16 receivers and carry 40 kg or more");
        // Weight relief: a third Hellwheel is too heavy for a Pistol Grip, until an Anti-Grav Coil goes on.
        var heavy = BuildOf("Pistol Grip{receiver_1:Hellwheel Rotary Receiver,receiver_2:Hellwheel Rotary Receiver}");
        ExpectWhyNot(heavy, "", "receiver_3", "Hellwheel Rotary Receiver", "too heavy");
        heavy.Root!.Child("receiver_1")!.Attach("utility", Parts.Find("Anti-Grav Coil")!);
        var why = BuildRules.WhyNot(heavy, "", "receiver_3", Parts.Find("Hellwheel Rotary Receiver")!);
        Line($"weight relief: Pistol Grip carries {F(heavy.CarryWeight)} kg with an Anti-Grav Coil; third Hellwheel: {why ?? "fits"}");
        if (heavy.CarryWeight != Parts.Find("Pistol Grip")!.Frame!.Carry + 12 || why != null)
            Fail("weight relief: an Anti-Grav Coil should add 12 kg of carry and let the third Hellwheel on");
    }

    private static void CheckStandardBuilds()
    {
        var all = StandardBuilds.All;
        foreach (var e in StandardBuilds.LoadErrors)
            Fail($"standard build: {e}");
        Line($"{all.Length} standard build(s): " + string.Join(", ", Enum.GetValues<Rarity>().Select(r => $"{r} {all.Count(b => b.Tier == r)}"))
             + "; " + string.Join(", ", StandardBuilds.SourceNames.Select(s => $"{s} {StandardBuilds.From(s).Count()}")) + $"; melee {all.Count(b => b.IsMelee)}");
        if (all.Length < 20)
            Fail($"only {all.Length} standard builds (expected 20 or more)");
        foreach (var s in StandardBuilds.SourceNames)
            if (!StandardBuilds.From(s).Any())
                Fail($"no standard builds from '{s}'");
        foreach (var r in Enum.GetValues<Rarity>())
            if (!all.Any(b => b.Tier == r))
                Fail($"no {r} standard builds");
        foreach (var b in all)
        {
            var a = WeaponBuilder.Assemble(b.Build());
            if (!a.Ok)
                Fail($"standard build '{b.Name}' isn't complete and legal");
            else if (a.Def is { } d && WeaponFile.ToJson(WeaponBuilder.Assemble(b.Build()).Def!) != WeaponFile.ToJson(d))
                Fail($"standard build '{b.Name}' isn't deterministic");
            if (b.Name.Any(ch => ch > '~') || b.Flavor.Any(ch => ch > '~' || ch < ' ') || b.Flavor.Length == 0)
                Fail($"standard build '{b.Name}': flavor missing or not plain ASCII");
        }
        foreach (var name in (string[])["Wall Rifle", "Shopping Spree", "Gantry Sixteen", "Hopper Triplets", "Machete", "Chainsaw Sword"])
            if (StandardBuilds.Find(name) == null)
                Fail($"standard builds: missing '{name}'");
        CheckBoxBuilds();
        try
        {
            StandardBuilds.Load("""{ "format": "call-of-slop-build/1", "name": "Half", "tier": "common", "build": "Rifle Grip{receiver_1:Workhorse Receiver}" }""", "half.json", Parts.Find);
            Fail("standard builds: an incomplete build file was accepted");
        }
        catch (WeaponFileException ex) when (ex.Message.Contains("complete"))
        {
        }
    }

    /// <summary>
    /// The mystery box pool (docs/zombies/WEAPON-BUILDER.md, "Standard builds"): every box build's tier is within one of
    /// its rarest part's (common parts don't make a legendary, a wonder part doesn't come in an uncommon), every tier the
    /// box rolls has a healthy pool with guns in it, and the numbers stay sane for the tier: no zero-DPS duds, and nothing
    /// at the low tiers that wipes a room in one pull (body damage per trigger pull and body DPS caps per tier).
    /// </summary>
    private static void CheckBoxBuilds()
    {
        // The box gives guns only; the slot machines pay the melee builds (2026-10-26): both pools get the tier checks.
        var box = StandardBuilds.From("box").Concat(StandardBuilds.From("slots")).Distinct().ToList();
        // Per tier, common .. wonder: most body damage per trigger pull and body DPS for a gun, most damage for a melee hit.
        float[] maxPull = [250, 250, 450, 1500, 3000, 5000]; // (epic and up raised with the late-game power curve, 2026-10-27)
        float[] maxDps = [600, 600, 1200, 3000, 8000, 12000];
        float[] maxMelee = [150, 150, 200, 250, 400, 400];
        foreach (var b in box)
        {
            var build = b.Build();
            var t = (int)b.Tier;
            var rarest = build.Nodes.Max(n => n.Part.Rarity);
            if (Math.Abs(t - (int)rarest) > 1)
                Fail($"box build '{b.Name}' is {b.Tier} but its rarest part is {rarest} (a build sits within one tier of its rarest part)");
            var a = WeaponBuilder.Assemble(build);
            if (a.Melee is { } m)
            {
                Line($"box {b.Tier} '{b.Name}': melee {F(m.Damage)} dmg, {F(m.Swing)} s, cleave {m.Cleave}, stun {F(m.Stun)} s, {F(m.Dps)} DPS [{string.Join(",", m.Flags)}]");
                if (m.Dps < 30 || m.Damage > maxMelee[t])
                    Fail($"box build '{b.Name}' ({b.Tier} melee): {F(m.Damage)} damage, {F(m.Dps)} DPS is outside the tier's band (30 DPS or more, {maxMelee[t]} damage at most)");
            }
            else if (a.Def is { } d)
            {
                Line($"box {b.Tier} '{b.Name}': {build.Receivers.Count} receiver(s){(build.ChainFire && build.Receivers.Count > 1 ? " chain fire" : "")}, {F(d.ShotDamage)} per pull, {d.Rpm} rpm {d.Mode}, {F(d.Dps)} DPS, mag {d.Mag}, reload {F(d.Reload)} s, mobility {F(d.Mobility)}, {F(build.Weight)}/{F(build.CarryWeight * 2)} kg [{string.Join(",", d.BuildFlags)}]");
                if (d.Dps < 60 || d.ShotDamage < 1)
                    Fail($"box build '{b.Name}' ({b.Tier}): {F(d.Dps)} DPS is a dud (60 or more)");
                if (d.ShotDamage > maxPull[t] || d.Dps > maxDps[t])
                    Fail($"box build '{b.Name}' ({b.Tier}): {F(d.ShotDamage)} per pull, {F(d.Dps)} DPS is too much for the tier (at most {maxPull[t]} per pull, {maxDps[t]} DPS)");
            }
            else
                Fail($"box build '{b.Name}' doesn't assemble");
        }
        for (var t = Rarity.Uncommon; t <= Rarity.Wonder; t++)
        {
            var guns = box.Count(b => b.Tier == t && b.Sources.Contains("box") && !b.IsMelee);
            if (guns < 5)
                Fail($"the mystery box has only {guns} {t} guns; every tier it rolls wants 5 or more");
        }
        Line($"box and slot pools: {box.Count} builds, " + string.Join(", ", Enum.GetValues<Rarity>().Select(r => $"{r} {box.Count(b => b.Tier == r && !b.IsMelee)} guns + {box.Count(b => b.Tier == r && b.IsMelee)} melee")));
    }

    private static void CheckDropTables()
    {
        var t = RunTables.Current;
        foreach (var e in RunTables.LoadErrors)
            Fail($"drop tables: {e}");
        if (t.PartDrops.Count == 0 || t.BoxTiers.Count == 0 || t.DropChance.Count == 0)
        {
            Fail("drop tables: empty");
            return;
        }
        float Mean(List<RoundRow> rows, int round) => (float)Enumerable.Range(0, 2000).Average(i => (int)RunTables.Roll(rows, round, (uint)i));
        var r1 = Mean(t.PartDrops, 1);
        var r15 = Mean(t.PartDrops, 15);
        var r35 = Mean(t.PartDrops, 35);
        Line($"drop tables: mean part tier round 1 {F(r1)}, round 15 {F(r15)}, round 35 {F(r35)}; box round 1 {F(Mean(t.BoxTiers, 1))}, round 25 {F(Mean(t.BoxTiers, 25))}; slot upgrade {t.SlotUpgradeCost}");
        if (!(r1 < r15 && r15 < r35) || r35 - r1 < 2f)
            Fail("drop tables: later rounds should roll clearly rarer parts");
        if (Enumerable.Range(0, 2000).Any(i => RunTables.Roll(t.PartDrops, 1, (uint)i) >= Rarity.Rare))
            Fail("drop tables: round 1 should drop nothing rarer than uncommon");
        if (!Enumerable.Range(0, 4000).Any(i => RunTables.Roll(t.PartDrops, 40, (uint)i) == Rarity.Wonder))
            Fail("drop tables: late rounds should be able to drop wonder parts");
        if (RunTables.Roll(t.BoxTiers, 12, 77) != RunTables.Roll(t.BoxTiers, 12, 77))
            Fail("drop tables: a roll isn't deterministic");
    }

    private static void CheckSlotUpgrade()
    {
        var rifle = Example("Plain Rifle");
        var triple = Example("Triple Pistol");
        var shotgun = Example("Twin Splitter Shotgun");
        var mg = Example("Minigun");
        if (rifle == null || triple == null || shotgun == null || mg == null)
            return;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.BuilderRange = true;
        var me = game.Local!;
        me.Alive = true;
        game.UseBuiltWeapons(rifle, triple, shotgun, mg, null, extraSlots: false);
        var ids = new[] { me.Primary, me.Secondary, me.BuiltExtra[0]!.Id, me.BuiltExtra[1]!.Id };
        var locked = game.WeaponSlots;
        game.SwitchTo(2, 10);
        var lockedSlot = game.ActiveSlot;
        var ownsLocked = me.Owns(ids[2]);
        game.SlotStation = me.Position;
        game.SelfTestInteract(me, 20); // the station: unlocks slots 3-4 for this run
        var unlocked = game.WeaponSlots;
        game.SwitchTo(2, 30);
        var slot3 = game.Active.Def;
        Line($"slot upgrade: {locked} slots -> {unlocked} after the station; slot 3 refused while locked ({lockedSlot}), then {slot3.Name}; ids {string.Join(",", ids)}");
        if (locked != 2 || lockedSlot != 0 || ownsLocked)
            Fail("slot upgrade: slots 3-4 should be closed until the upgrade");
        if (unlocked != 4 || !me.ExtraSlots || game.ActiveSlot != 2 || slot3.Name != shotgun.Name || !me.Owns(ids[2]) || !me.Owns(ids[3]))
            Fail("slot upgrade: the station should unlock slots 3-4 and slot 3 should hold its build");
        if (ids.Distinct().Count() != 4)
            Fail("slot upgrade: the four built weapons should have four different ids");
        if (game.NextSlot != 3)
            Fail("slot upgrade: the swap key should cycle on to slot 4");
        // The sandbox toggle: unlocked from the start.
        game.UseBuiltWeapons(rifle, triple, shotgun, null, null, extraSlots: true);
        if (game.WeaponSlots != 3)
            Fail($"slot upgrade: with the toggle on and three builds there should be 3 slots ({game.WeaponSlots})");
        // Standard matches: 2 slots.
        using var normal = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        if (normal.WeaponSlots != 2)
            Fail("slot upgrade: a standard match should have 2 weapon slots");
    }

    /// <summary>A flat floor that rises toward -Z from <see cref="Z0"/> at <see cref="SlopeDeg"/> (for the cart's slope rule).</summary>
    private sealed class SlopeWorld : IWorldQuery
    {
        public float SlopeDeg, Z0;
        public ITerrainCollision? Terrain => null;
        private float Height(float z) => MathF.Max(0, Z0 - z) * MathF.Tan(SlopeDeg * MathF.PI / 180f);

        public float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter)
        {
            if (dir.Y > -0.99f)
                return maxDist;
            var d = origin.Y - Height(origin.Z);
            return d < 0 ? 0 : MathF.Min(d, maxDist);
        }

        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool CapsuleCast(in Capsule capsule, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit)
        {
            hit = default;
            return false;
        }

        public bool OverlapBox(in Obb box, in QueryFilter filter) => false;
        public bool OverlapCapsule(in Capsule capsule, in QueryFilter filter) => false;
        public bool OverlapBody(Vector3 feet, float height, in QueryFilter filter) => false;
        public bool PointInside(Vector3 point, float radius, in QueryFilter filter) => false;
    }

    /// <summary>Pushes a cart from <paramref name="feet"/> along -Z for <paramref name="ticks"/> ticks at <paramref name="speed"/> m/s; returns the body, the feet and the highest the cart got.</summary>
    private static (CartBody Body, Vector3 Feet, float MaxY) Push(in CartShape s, Vector3 feet, int ticks, float speed, IWorldQuery q)
    {
        var body = CartPhysics.Place(s, feet, 0, q);
        var maxY = body.Position.Y;
        for (var i = 0; i < ticks; i++)
        {
            feet.Z -= speed * World.TickDt; // the player walks forward (yaw 0 = -Z)
            feet = CartPhysics.Step(ref body, s, feet, 0, World.TickDt, q);
            maxY = MathF.Max(maxY, body.Position.Y);
        }
        return (body, feet, maxY);
    }

    private static void CheckCartPhysics()
    {
        if (Example("Shopping Spree") is not { BuiltMesh: { } mesh } cart || Maps.Find("Workbench Range") is not { } range)
        {
            Fail("cart: no Shopping Spree example or no Workbench Range");
            return;
        }
        World.SetMap(range);
        var q = World.Query;
        var s = CartShape.From(mesh);
        Line($"cart shape: half {F(s.Half.X)} x {F(s.Half.Y)} x {F(s.Half.Z)} m, push distance {F(s.PushDist)} m, bar at {F(s.BarHeight)} m");
        if (s.Half.Y < 0.35f || s.BarHeight is < 0.75f or > 1.3f || s.Half.Z < 0.4f)
            Fail("cart: the Push Cart should be life size (bar about waist height, a basket you could sit in)");

        // Moves with the player on open ground.
        var (b1, f1, _) = Push(s, new Vector3(-12f, 0, 63f), 40, 4f, q);
        var gap = (b1.Position.Z + s.PushDist) - f1.Z;
        Line($"cart: open floor: rolled {F(b1.Rolled)} m in 40 ticks at 4 m/s, still {F(gap)} m ahead of its spot, blocked {b1.Blocked}");
        if (b1.Rolled < 2.4f || MathF.Abs(gap) > 0.05f || b1.Blocked)
            Fail("cart: it should roll along with the player on open floor");

        // The lane: rolls over the 8 cm curb, stops at the 30 cm step, and holds the player at the bar.
        var (b2, f2, maxY) = Push(s, new Vector3(-12f, 0, 63f), 400, 4f, q);
        var front = b2.Position.Z - s.Half.Z;
        var held = f2.Z - (b2.Position.Z + s.PushDist);
        Line($"cart: lane: over the curb (rose to {F(maxY)} m), stopped with its front at z {F(front)} (step face at 52.0), blocked {b2.Blocked}, player held {F(held)} m behind the bar");
        if (maxY < 0.07f || maxY > CartPhysics.StepLimit + 0.01f)
            Fail("cart: it should roll over the low curb");
        if (front < 51.9f || !b2.Blocked || held > CartPhysics.Slack + 0.01f || f2.Z < 52.5f)
            Fail("cart: it should stop at the step and hold the player at the bar");

        // A wall (the shooting bench): it stops short of it.
        var (b3, f3, _) = Push(s, new Vector3(0f, 0, 63f), 300, 4f, q);
        Line($"cart: bench: front at z {F(b3.Position.Z - s.Half.Z)} (bench back at 58.2), blocked {b3.Blocked}, player at z {F(f3.Z)}");
        if (b3.Position.Z - s.Half.Z < 58.1f || !b3.Blocked || f3.Z < 58.2f)
            Fail("cart: it should collide with the bench and not go through it");

        // Slopes: a gentle one yes, a steep one no.
        var gentle = Push(s, new Vector3(0, 0, 10f), 240, 3f, new SlopeWorld { SlopeDeg = 10, Z0 = 8 });
        var steep = Push(s, new Vector3(0, 0, 10f), 240, 3f, new SlopeWorld { SlopeDeg = 32, Z0 = 8 });
        Line($"cart: 10 degree slope: climbed to {F(gentle.Body.Position.Y)} m; 32 degree slope: {F(steep.Body.Position.Y)} m, blocked {steep.Body.Blocked}");
        if (gentle.Body.Position.Y < 0.5f || gentle.Body.Blocked)
            Fail("cart: it should roll up a gentle slope");
        if (steep.Body.Position.Y > 0.6f || !steep.Body.Blocked)
            Fail("cart: it shouldn't climb a steep slope");

        // Turning: it follows the player's yaw slowly.
        var body = CartPhysics.Place(s, new Vector3(-12, 0, 63), 0, q);
        CartPhysics.Step(ref body, s, new Vector3(-12, 0, 63), 1.5f, World.TickDt, q);
        if (MathF.Abs(body.Yaw - CartPhysics.TurnRate * World.TickDt) > 1e-4f)
            Fail($"cart: it should turn at {F(CartPhysics.TurnRate)} rad/s (turned {F(body.Yaw)} in a tick)");

        // Deterministic.
        var again = Push(s, new Vector3(-12f, 0, 63f), 400, 4f, q);
        if (again.Body.Position != b2.Position || again.Body.Yaw != b2.Yaw || again.Body.Rolled != b2.Rolled || again.Feet != f2)
            Fail("cart: the same pushes gave a different result");

        // The state message round-trips.
        var w = new PacketWriter();
        var ent = new CartEntity { Owner = 3, Mode = CartMode.Manned, Body = b2, AimYaw = -0.5f, AimPitch = 0.2f };
        Game.WriteCartState(w, [ent]);
        var r = new PacketReader(w.Span[1..]);
        var got = Game.ReadCartState(ref r);
        if (w.Span[0] != (byte)Msg.CartState || got.Count != 1 || got[0].Owner != 3 || got[0].Mode != CartMode.Manned || got[0].Body.Position != b2.Position
            || got[0].Body.Yaw != b2.Yaw || got[0].AimYaw != -0.5f || got[0].AimPitch != 0.2f || w.Length != 2 + Game.CartStateBytes)
            Fail("cart: the cart state message didn't round-trip");
        else
            Line($"cart: state message {w.Length} bytes for one cart (protocol {ServerInfo.ProtocolVersion})");
    }

    private static void CheckCartParking()
    {
        var cart = Example("Shopping Spree");
        var mg = Example("Minigun");
        var tp = Example("Triple Pistol");
        if (cart == null || mg == null || tp == null)
        {
            Fail("cart: the Shopping Spree, Minigun or Triple Pistol example is missing");
            return;
        }
        // The traverse limits on their own.
        var (cy, cp) = Game.ClampCartAim(0.3f, 0.3f + 2.0f, 1.0f);
        var (ly, lp) = Game.ClampCartAim(0.3f, 0.3f - 0.4f, -1.0f);
        if (MathF.Abs(cy - (0.3f + Game.CartTraverseYaw)) > 1e-5f || cp != Game.CartPitchUp || MathF.Abs(ly - (0.3f - 0.4f)) > 1e-5f || lp != Game.CartPitchDown)
            Fail("cart gun: the traverse limits don't clamp the aim (+-60 deg yaw, -15..+30 deg pitch)");
        var first = CartScenario(cart, mg, tp, log: true);
        var second = CartScenario(cart, mg, tp, log: false);
        Line($"cart gun: the same run twice gave the same result: {first == second}");
        if (first != second)
            Fail($"cart gun: not deterministic ({first} vs {second})");
    }

    /// <summary>
    /// Push, park (it must not fire on its own for 10 s), man (aim clamped, fire hits a dummy), step off (back to the
    /// weapon held before), hold to push it again. Returns a summary of the state at each step (for the determinism check).
    /// </summary>
    private static string CartScenario(WeaponDef cart, WeaponDef mg, WeaponDef tp, bool log)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 2, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.BuilderRange = true;
        var me = game.Local!;
        var bots = game.Players.Values.Where(p => p.Id != me.Id).OrderBy(p => p.Id).ToList();
        var (ahead, side) = (bots[0], bots[1]);
        game.SelfTestPlace(me, new Vector3(0, 0, 45), 0); // facing -Z, down the range
        game.UseBuiltWeapons(cart, mg, tp, null, null, extraSlots: true);
        cart = game.Slots[0].Def; // the slot's own copy (its id may have been moved off a clash)
        var sum = new System.Text.StringBuilder();
        var t = 100.0;
        void Tick(int n)
        {
            for (var i = 0; i < n; i++)
                game.SelfTestTickCarts(t += World.TickDt);
        }
        void Sync() => me.Weapon = game.Active.Def.Id; // the next input would carry the switch
        game.SelfTestTickCarts(t);
        if (!game.Carts.TryGetValue(me.Id, out var c) || c.Mode != CartMode.Pushed)
        {
            Fail("cart: holding the cart build should make a pushed cart");
            return "";
        }
        // Pushing: it doesn't fire (both hands on the bar).
        var tracers = game.Tracers.Count;
        game.TestServerShot(cart, -Vector3.UnitZ, 1);
        var firedPushing = game.Tracers.Count != tracers;

        // Parked: it stands where it is, you go back to another weapon, and it never fires on its own.
        game.SelfTestInteract(me, t);
        var parked = c.Mode;
        var away = game.Active.Def;
        Sync();
        var spot = c.Body.Position;
        game.SelfTestPlace(ahead, new Vector3(3, 0, 30), MathF.PI); // in front of the cart (between the 25 m target boards), facing it
        game.SelfTestPlace(side, c.Stand + new Vector3(6f, 0, 0.5f), MathF.PI); // 90 degrees to the right of it
        tracers = game.Tracers.Count;
        Tick(600);
        var idleHp = ahead.Health;
        var idleTracers = game.Tracers.Count - tracers;
        var stayed = Vector3.Distance(c.Body.Position, spot) < 1e-4f && c.Mode == CartMode.Idle;
        if (log)
            Line($"cart parked: interact -> {parked}, you hold the {away.Name}; 10 s later: dummy in front {idleHp} HP, {idleTracers} tracer(s), cart still {(stayed ? "standing idle" : "MOVED")}");
        if (firedPushing)
            Fail("cart: it shouldn't fire while you push it");
        if (parked != CartMode.Idle || Game.IsCart(away))
            Fail("cart: E while pushing should park it and switch you to another weapon");
        if (idleHp != World.MaxHealth || idleTracers != 0 || !stayed)
            Fail("cart: a parked cart with nobody manning it must not fire on its own");
        sum.Append($"{parked} {idleTracers} {c.Body.Position} {c.Body.Yaw}; ");

        // Pick the triple pistol (slot 3), then walk up and tap E: you man the cart.
        game.SwitchTo(2, t);
        Sync();
        var before = game.Active.Def;
        game.SelfTestPlace(me, c.Bar + World.FlatForward(c.Body.Yaw) * -0.7f + new Vector3(0.3f, 0, 0), c.Body.Yaw);
        game.SelfTestInteract(me, t);
        var manned = c.Mode;
        Sync();
        Tick(2);
        var atBar = Vector2.Distance(new Vector2(me.Position.X, me.Position.Z), new Vector2(c.Stand.X, c.Stand.Z));
        // Aim at the dummy in front and fire the cart's build through the real server shot path.
        var to = Vector3.Normalize(ahead.Chest - me.Eye);
        tracers = game.Tracers.Count;
        game.TestServerShot(cart, to, 7);
        var hitHp = ahead.Alive ? ahead.Health : 0;
        var firedTracers = game.Tracers.Count - tracers;
        if (log)
            Line($"cart manned: interact at the bar -> {manned}, holding {game.Active.Def.Name}, feet {F(atBar)} m from the bar spot; one shot at the dummy in front: "
                 + $"{firedTracers} tracer(s), {World.MaxHealth} -> {hitHp} HP");
        if (manned != CartMode.Manned || !Game.IsCart(game.Active.Def) || atBar > 0.05f)
            Fail("cart: tapping E at a parked cart's bar should man it (you stand at the bar holding its gun)");
        if (firedTracers == 0 || hitHp >= World.MaxHealth)
            Fail("cart: firing a manned cart should hit the dummy in front of it");
        sum.Append($"{manned} hit {hitHp < World.MaxHealth} {me.Position}; "); // (the bot's random perks, e.g. Last Stand, decide its HP)

        // Traverse: looking 90 degrees right, the gun stops at 60; a shot straight at the dummy on the right is turned back to 60 and misses it.
        me.Yaw = c.Body.Yaw + MathF.PI / 2;
        me.Pitch = 1.2f;
        Tick(1);
        var aimYaw = c.AimYaw;
        var aimPitch = c.AimPitch;
        var sideHp = side.Health;
        var toSide = Vector3.Normalize(side.Chest - me.Eye);
        game.TestServerShot(cart, toSide, 8);
        if (log)
            Line($"cart traverse: look 90 deg right / 69 deg up -> gun {F(aimYaw * 180 / MathF.PI)} deg / {F(aimPitch * 180 / MathF.PI)} deg; a shot at the dummy 90 deg right: {side.Health} HP; yaw stays {F(c.Body.Yaw)}");
        if (MathF.Abs(aimYaw - Game.CartTraverseYaw) > 1e-4f || MathF.Abs(aimPitch - Game.CartPitchUp) > 1e-4f)
            Fail("cart: the manned gun's aim should clamp to the traverse limits");
        if (side.Health != sideHp)
            Fail("cart: a shot outside the traverse limits should be turned back inside them (it hit the dummy at 90 degrees)");
        sum.Append($"{aimYaw} {aimPitch}; ");

        // Step off: back to the triple pistol you held before (not just the first other slot).
        me.Yaw = c.Body.Yaw;
        me.Pitch = 0;
        game.SelfTestInteract(me, t);
        var left = c.Mode;
        Sync();
        if (log)
            Line($"cart leave: interact while manning -> {left}, holding {game.Active.Def.Name} (held {before.Name} before)");
        if (left != CartMode.Idle || !ReferenceEquals(game.Active.Def, before))
            Fail("cart: E while manning should step off and give back the weapon you held before");
        Tick(90); // past the mode-change grace: switching away doesn't pick it up again

        // Hold interact at the bar: push it again, and it rolls.
        game.SelfTestInteract(me, t, hold: true);
        var pushed = c.Mode;
        Sync();
        var rolled = c.Body.Rolled;
        var feet = me.Position;
        for (var i = 0; i < 30; i++)
        {
            feet += World.FlatForward(c.Body.Yaw) * (3f * World.TickDt);
            game.SelfTestPlace(me, feet, c.Body.Yaw);
            Tick(1);
            feet = me.Position;
        }
        if (log)
            Line($"cart push again: hold interact -> {pushed}, holding {game.Active.Def.Name}, rolled {F(c.Body.Rolled - rolled)} m in 0.5 s");
        if (pushed != CartMode.Pushed || !Game.IsCart(game.Active.Def) || c.Body.Rolled - rolled < 0.8f)
            Fail("cart: holding E at a parked cart should push it again");
        sum.Append($"{pushed} {c.Body.Position} {c.Body.Rolled}");

        // Dead owners lose their cart; it comes back in front of them.
        me.Alive = false;
        game.SelfTestTickCarts(t += 1);
        if (game.Carts.ContainsKey(me.Id))
            Fail("cart: a dead owner's cart should go");
        return sum.ToString();
    }

    private static void CheckMeleeBuilds()
    {
        var examples = BuilderExamples.Melee();
        var defs = new Dictionary<string, MeleeDef>();
        foreach (var (b, _) in examples)
        {
            var a = WeaponBuilder.Assemble(b);
            if (a.Melee is not { } m)
            {
                Fail($"melee: '{b.Name}' isn't complete: {string.Join("; ", a.Problems.Select(p => p.Message))}");
                continue;
            }
            defs[b.Name] = m;
            Line($"melee {m}, {F(m.Dps)} DPS");
            if (WeaponBuilder.Assemble(b.Clone()).Melee?.ToString() != m.ToString())
                Fail($"melee: '{b.Name}' isn't deterministic");
            if (a.Model == null || a.Model.Mesh.Parts.Length < 2 || m.View.BuiltMesh == null)
                Fail($"melee: '{b.Name}' has no model");
            var back = WeaponBuild.Parse(b.ToText(), Parts.Find);
            if (back.ToText() != b.ToText() || !back.IsMelee)
                Fail($"melee: '{b.Name}' didn't round-trip");
        }
        if (examples.Count < 6)
            Fail($"melee: only {examples.Count} example melee builds");
        if (defs.TryGetValue("Machete", out var mach) && defs.TryGetValue("Sledgehammer", out var sledge) && defs.TryGetValue("Chainsaw Sword", out var saw) && defs.TryGetValue("The Codfather", out var fish))
        {
            if (!(sledge.Damage > mach.Damage && sledge.Swing > mach.Swing && sledge.Stun > 0.5f))
                Fail("melee: the sledgehammer should hit harder, swing slower and stun");
            if (saw.Cleave < 3 || saw.Swing >= mach.Swing)
                Fail("melee: the chainsaw sword should be fast and cleave three or more");
            if (fish.Reach <= mach.Reach || fish.Stun < 1.5f)
                Fail("melee: the fish on a rod should reach far and stun long");
        }
        // The rules: a gun part can't go on a hilt, a melee build needs a head.
        var hilt = new WeaponBuild("Bare", Parts.Find("Wooden Handle")!);
        if (!BuildRules.Check(hilt).Any(p => p.Incomplete && p.Message.Contains("needs a head")))
            Fail("melee: a bare hilt should need a head");
        ExpectWhyNot(hilt, "", "head", "Standard Barrel", "mounts on a barrel socket");
        ExpectWhyNot(new WeaponBuild("Gun", Parts.Find("Rifle Grip")!), "", "receiver_1", "Wooden Handle", "melee");

        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 3, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        var me = game.Local!;
        var bots = game.Players.Values.Where(p => p.Id != me.Id).OrderBy(p => p.Id).ToList();
        var t = 200.0;
        int Swing(MeleeDef? m, params Vector3[] spots)
        {
            game.SelfTestPlace(me, new Vector3(0, 0, 40), 0);
            me.MeleeBuild = m;
            for (var i = 0; i < bots.Count; i++)
            {
                game.SelfTestPlace(bots[i], i < spots.Length ? spots[i] : new Vector3(10 + i * 2, 0, 20), MathF.PI); // facing you
                bots[i].StunnedUntil = 0;
            }
            game.SelfTestMelee(me, 0, 0, t += 5);
            return bots.Count(b => !b.Alive || b.Health < World.MaxHealth);
        }
        var arc = new[] { new Vector3(0, 0, 38.4f), new Vector3(-0.7f, 0, 38.0f), new Vector3(0.7f, 0, 38.0f) }; // the middle one is nearest
        if (defs.TryGetValue("Chainsaw Sword", out var cs))
        {
            var hit = Swing(cs, arc);
            Line($"melee cleave: the chainsaw sword (cleave {cs.Cleave}) hit {hit} of 3 dummies in front, {World.MaxHealth - bots[0].Health} damage each");
            if (hit != Math.Min(3, cs.Cleave) || World.MaxHealth - bots[0].Health != (int)cs.Damage)
                Fail("melee: the chainsaw sword should hit every dummy in its arc for its damage");
        }
        if (defs.TryGetValue("Frying Pan", out var pan))
        {
            var hit = Swing(pan, arc);
            var stunned = bots.Count(b => b.StunnedUntil > t);
            Line($"melee: the frying pan (cleave 1) hit {hit}, stunned {stunned} for {F(pan.Stun)} s");
            if (hit != 1 || stunned != 1 || bots[0].Health == World.MaxHealth)
                Fail("melee: a single-target weapon should hit only the nearest dummy, and stun it");
        }
        if (defs.TryGetValue("Sledgehammer", out var sl))
        {
            var half = World.Hitbox(Vector3.Zero).Max.Z;
            var inReach = Swing(sl, new Vector3(0, 0, 40 - (sl.Reach - 0.3f) - half));
            var outReach = Swing(sl, new Vector3(0, 0, 40 - (sl.Reach + 0.3f) - half));
            Line($"melee reach: sledgehammer reach {F(sl.Reach)} m: a dummy 0.3 m inside it {(inReach == 1 ? "hit" : "missed")}, 0.3 m outside {(outReach == 1 ? "hit" : "missed")}");
            if (inReach != 1 || outReach != 0)
                Fail("melee: reach should decide what a swing hits");
        }
        // Standard multiplayer melee (no melee build): one hit kills a player, one target, 2.4 m, 0.8 s; 55 to zombies and props.
        var std = Swing(null, arc);
        Line($"standard melee: hit {std}, nearest dummy killed {!bots[0].Alive} (constants {F(Game.MeleeDamage)} dmg, {F(Game.MeleeObjectDamage)} to zombies, {F(Game.MeleeRange)} m, {Game.MeleeCooldown} s)");
        if (std != 1 || bots[0].Alive || Game.MeleeDamage < World.MaxHealth || Game.MeleeObjectDamage != 55f || Game.MeleeRange != 2.4f || Game.MeleeCooldown != 0.8)
            Fail("standard melee changed");
        // Stunned bots stand still.
        if (defs.TryGetValue("The Codfather", out var cod))
        {
            Swing(cod, arc);
            if (!(bots[0].StunnedUntil > t + 1))
                Fail("melee: the fish should stun for over a second");
        }
    }
}
