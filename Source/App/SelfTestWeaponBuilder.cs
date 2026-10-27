using System.Globalization;
using System.Numerics;
using Brutal.RakNetApi;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The Zombies weapon builder (Source/Zombies, docs/zombies/WEAPON-BUILDER.md): every part file loads and validates,
    /// bad part files are refused with readable messages, the starter set covers every category and tier, the example
    /// builds are legal with sane, deterministic numbers, the assembly rules refuse illegal builds with the right
    /// reasons (grip capacity, bores, actions, splitter depth, limits), the splitter damage rule, multi-barrel and
    /// multi-receiver shots (projectile counts, muzzles, turns) including through the real server shot path, standard
    /// weapons firing exactly as before, models with hand anchors and sight lines, one trigger pull using one Last Stand charge, and saves round-tripping.
    /// </summary>
    private static void CheckWeaponBuilder()
    {
        BuildStore.ReadOnly = true;
        var parts = Parts.All;
        Line($"{parts.Length} part(s): " + string.Join(", ", Enum.GetValues<PartCategory>().Select(c => $"{c} {parts.Count(p => p.Category == c)}")));
        Line("tiers: " + string.Join(", ", Enum.GetValues<Rarity>().Select(r => $"{r} {parts.Count(p => p.Rarity == r)}")));
        foreach (var e in Parts.LoadErrors)
            Fail($"part file: {e}");
        if (parts.Length < 40)
            Fail($"only {parts.Length} parts in the starter set (expected 40 or more)");
        foreach (var c in Enum.GetValues<PartCategory>())
            if (!parts.Any(p => p.Category == c))
                Fail($"no {c} parts in the starter set");
        foreach (var r in Enum.GetValues<Rarity>())
            if (!parts.Any(p => p.Rarity == r))
                Fail($"no {r} parts in the starter set");
        if (parts.Select(p => p.Name.ToLowerInvariant()).Distinct().Count() != parts.Length)
            Fail("part names aren't unique");
        foreach (var must in (string[])["Y-Splitter", "Trident Splitter", "Belt Box", "Drum Mag", "Hellwheel Rotary Receiver", "Thumper Chain Gun Receiver", "Barrel of Barrels",
                     "Toaster Feed", "Pistol Grip", "Rifle Grip", "Chainsaw Grip", "Push Cart"])
            if (Parts.Find(must) == null)
                Fail($"starter set is missing '{must}'");
        if (Parts.Find("Pistol Grip") is { } pg && pg.ReceiverCapacity != 3)
            Fail($"Pistol Grip holds {pg.ReceiverCapacity} receivers (expected 3)");
        if (Parts.Find("Rifle Grip") is { } rg && rg.ReceiverCapacity != 9)
            Fail($"Rifle Grip holds {rg.ReceiverCapacity} receivers (expected 9)");
        foreach (var p in parts)
        {
            if (p.Flavor.Length == 0)
                Fail($"{p.Name}: no flavor text");
            if (p.Name.Any(ch => ch > '~') || p.Flavor.Any(ch => ch > '~' || ch < ' '))
                Fail($"{p.Name}: text isn't plain ASCII");
        }

        CheckPartFileErrors();
        CheckBuilderExamples();
        CheckBuilderRules();
        CheckBuilderShots();
        CheckBuilderServerShots();
        CheckBuilderLastStand();
        CheckBuilderSaves();
        CheckBuilder2(); // SelfTestWeaponBuilder2.cs: the user's decisions (fire style, splitters, upgrades, shared feed, builds, cart, melee, slots)

        if (Maps.Find("Workbench Range") is not { } range)
            Fail("no Workbench Range map");
        else
            Line($"Workbench Range: {range.Spawns.Count} spawn(s), {range.Solid.Count} boxes, max {range.WantedMaxPlayers} players");
    }

    /// <summary>
    /// Last Stand and built weapons: one trigger pull is one hit per victim, however many barrels or receivers land
    /// (the server sums every projectile's damage per victim before it calls DamagePlayer, like a shotgun blast).
    /// </summary>
    private static void CheckBuilderLastStand()
    {
        World.SetMap(Maps.All[0]);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 1);
        var human = game.Players[0];
        var bot = game.Players.Values.First(p => p.Id != 0);
        var lastStand = new PerkSet(PerkId.None, PerkId.LastStand, PerkId.None);
        var t = 10.0;
        foreach (var name in (string[])["Triple Pistol", "Nine Lives", "Twin Splitter Shotgun"])
        {
            if (Example(name) is not { } def)
            {
                Fail($"Last Stand: no example '{name}'");
                continue;
            }
            void Setup(PerkSet victimPerks)
            {
                game.SelfTestPerks(human, PerkSet.Empty);
                game.SelfTestPerks(bot, victimPerks);
                game.SelfTestPlace(human, new Vector3(5, 0, 5), MathF.PI); // facing +Z, at the victim
                game.SelfTestPlace(bot, new Vector3(5, 0, 8), 0);
                human.BuiltPrimary = def;
                human.Primary = human.Weapon = def.Id;
                human.PrimaryKit = 0;
            }
            Setup(PerkSet.Empty);
            game.SelfTestShoot(human, Vector3.Normalize(bot.Chest - human.Eye), t += 5);
            var unshielded = World.MaxHealth - (bot.Alive ? bot.Health : 0);
            Setup(lastStand);
            game.SelfTestDamage(human, bot, 150, DamageKind.Bullet, t += 5);
            game.SelfTestShoot(human, Vector3.Normalize(bot.Chest - human.Eye), t += 0.5);
            var after = (bot.Alive, bot.Health, bot.ShieldCharges);
            Line($"Last Stand: {name} ({def.BarrelCount} barrel(s) x {def.Pellets} pellet(s)): {unshielded} damage unshielded; shielded -> alive {after.Alive}, {after.Health} HP, {after.ShieldCharges} charge(s) left");
            if (unshielded <= def.Damage * 1.5f)
                Fail($"Last Stand: {name} didn't land several projectiles ({unshielded} damage), so it proves nothing");
            if (!after.Alive || after.Health != 1 || after.ShieldCharges != Perks.LastStandCharges - 1)
                Fail($"Last Stand: one pull of {name} should use exactly one shield charge");
        }
        human.BuiltPrimary = null;
    }

    private static void ExpectPartError(string json, string expect, string what)
    {
        try
        {
            PartFile.Load(json, "test.json");
            Fail($"part file with {what} was accepted");
        }
        catch (WeaponFileException ex)
        {
            if (!ex.Message.Contains(expect, StringComparison.OrdinalIgnoreCase))
                Fail($"part file with {what}: message \"{ex.Message}\" doesn't mention \"{expect}\"");
        }
    }

    private static void CheckPartFileErrors()
    {
        ExpectPartError("""{ "name": "X", "category": "stock", "rarity": "common", "colour": 1 }""", "unknown key", "a typo'd key");
        ExpectPartError("""{ "name": "X", "rarity": "common" }""", "category", "no category");
        ExpectPartError("""{ "name": "X", "category": "receiver", "rarity": "common", "sockets": [{ "name": "barrel", "type": "barrel", "pos": [0,0,0] }] }""", "receiver", "a receiver without a receiver block");
        ExpectPartError("""{ "name": "X", "category": "grip", "rarity": "common", "hand": [0,0,0], "frame": { "hands": "one" } }""", "receiver", "a root grip without receiver sockets");
        ExpectPartError("""{ "name": "X", "category": "barrel", "rarity": "common", "bores": ["laser"], "muzzles": [[0,0,-0.2]] }""", "bore", "an unknown bore");
        ExpectPartError("""{ "name": "X", "category": "barrel", "rarity": "common" }""", "muzzles", "a barrel without muzzles");
        ExpectPartError("""{ "name": "X", "category": "splitter", "rarity": "rare", "split": { "splay": 0 }, "sockets": [{ "name": "barrel_1", "type": "barrel", "pos": [0,0,0] }] }""", "two", "a splitter with one output");
        ExpectPartError("""{ "name": "X", "category": "splitter", "rarity": "rare", "split": { "outputMult": 0.7 }, "sockets": [{ "name": "barrel_1", "type": "barrel", "pos": [0,0,0] }, { "name": "barrel_2", "type": "barrel", "pos": [0,0,0] }] }""", "conserves damage", "a splitter that multiplies damage");
        ExpectPartError("""{ "name": "X", "category": "head", "rarity": "common" }""", "melee", "a melee head without a melee block");
        ExpectPartError("""{ "name": "X", "category": "hilt", "rarity": "common", "hand": [0,0,0], "hilt": { "damage": 10 } }""", "head", "a hilt without a head socket");
        ExpectPartError("""{ "name": "X", "category": "feed", "rarity": "rare", "mount": "hopper" }""", "shared", "a hopper feed without a shared block");
        ExpectPartError("""{ "name": "X", "category": "grip", "rarity": "common", "hand": [0,0,0], "frame": { "fireStyle": "volley", "fireStyles": ["rotate"] }, "sockets": [{ "name": "receiver_1", "type": "receiver", "pos": [0,0,0] }] }""", "once", "a grip with two fire styles");
        ExpectPartError("""{ "name": "X", "category": "stock", "rarity": "common", "stats": [{ "stat": "rnage", "mul": 2 }] }""", "unknown stat", "an unknown stat");
        ExpectPartError("""{ "name": "X{1}", "category": "stock", "rarity": "common" }""", "name", "braces in the name");
        ExpectPartError("""{ "name": "X", "category": "optic", "rarity": "common" }""", "optic", "an optic without a look");
        ExpectPartError("""{ "name": "X", "category": "stock", "rarity": "mythic" }""", "rarity", "an unknown tier");
        Line("bad part files: refused with readable messages");
    }

    private static string StatLine(WeaponDef d) => string.Create(CultureInfo.InvariantCulture,
        $"dmg {d.Damage}x{d.Pellets} barrels {d.BarrelCount} shot {d.ShotDamage:0} rpm {d.Rpm} {d.Mode} mag {d.Mag} reload {d.Reload:0.0} spread {d.HipSpreadDeg:0.00} range {d.RangeM:0} recoil {d.RecoilDeg:0.00} mob {d.Mobility:0.00} ads {d.AdsTime:0.00}{(d.NoAds ? " (no ADS)" : "")} DPS {d.Dps:0}");

    private static void CheckBuilderExamples()
    {
        var examples = BuilderExamples.All();
        if (examples.Count < 10)
            Fail($"only {examples.Count} example builds could be made from the starter set (expected 10)");
        foreach (var ex in examples)
        {
            var a = WeaponBuilder.Assemble(ex.Build);
            if (!a.Ok)
            {
                Fail($"example '{ex.Build.Name}' isn't legal: {string.Join("; ", a.Problems.Select(p => p.Message))}");
                continue;
            }
            var d = a.Def!;
            Line($"{ex.Build.Name}: {ex.Build.Receivers.Count} receiver(s), {ex.Build.PartCount} parts, {ex.Build.Weight:0.0} kg: {StatLine(d)}");
            if (d.Dps < ex.MinDps || d.Dps > ex.MaxDps)
                Fail($"example '{ex.Build.Name}': DPS {d.Dps:0} outside its documented band {ex.MinDps:0}-{ex.MaxDps:0}");
            foreach (var (field, (min, max)) in WeaponFile.Ranges)
            {
                var v = Convert.ToSingle(typeof(WeaponDef).GetField(field)!.GetValue(d), CultureInfo.InvariantCulture);
                if (!(v >= min && v <= max))
                    Fail($"example '{ex.Build.Name}': {field} {v} outside {min}-{max}");
            }
            try
            {
                Recoil.Validate(d, ex.Build.Name);
            }
            catch (WeaponFileException e)
            {
                Fail($"example '{ex.Build.Name}': {e.Message}");
            }
            // Deterministic: the same tree gives the same numbers and the same muzzles.
            var again = WeaponBuilder.Assemble(WeaponBuild.Parse(ex.Build.ToText(), Parts.Find)).Def;
            if (again == null || WeaponFile.ToJson(again) != WeaponFile.ToJson(d) || !(again.MuzzlePoints ?? []).SequenceEqual(d.MuzzlePoints ?? []))
                Fail($"example '{ex.Build.Name}': assembling it again gave different numbers");
            CheckBuiltModel(ex.Build.Name, a);
        }

        // Splitter rule: a conserving splitter keeps the total damage; multipliers multiply it by the documented amount.
        var twin = examples.FirstOrDefault(e => e.Build.Name == "Twin Splitter Shotgun");
        if (twin != null && WeaponBuilder.Assemble(twin.Build).Def is { } t)
        {
            var single = twin.Build.Clone();
            var r = single.Receivers[0];
            r.Attach("barrel", Parts.Find("Scattermouth Barrel")!);
            var s = WeaponBuilder.Assemble(single).Def!;
            Line($"splitter rule: one barrel {s.ShotDamage:0} per shot, Y-Splitter twin {t.ShotDamage:0} per shot (outputMult 0.5)");
            if (MathF.Abs(t.ShotDamage - s.ShotDamage) > t.Pellets * t.BarrelCount)
                Fail($"Y-Splitter should conserve damage: {s.ShotDamage} vs {t.ShotDamage}");
        }
        var platter = examples.FirstOrDefault(e => e.Build.Name == "Party Platter");
        if (platter != null && WeaponBuilder.Assemble(platter.Build).Def is { } pp)
        {
            var single = platter.Build.Clone();
            single.Receivers[0].Attach("barrel", Parts.Find("Stub Barrel")!);
            var s = WeaponBuilder.Assemble(single).Def!;
            var expect = s.ShotDamage; // splitters conserve: 4 x 3 outputs at 1/12 each
            Line($"splitter rule: Party Tray x Trident = 12 barrels, {pp.ShotDamage:0.0} per shot vs {expect:0.0} for one barrel (conserved)");
            if (pp.BarrelCount != 12 || MathF.Abs(pp.ShotDamage - expect) > 0.5f)
                Fail($"Party Platter: {pp.BarrelCount} barrels, {pp.ShotDamage} per shot (expected 12 barrels, about {expect})");
        }
    }

    private static void CheckBuiltModel(string name, AssembledWeapon a)
    {
        var d = a.Def!;
        var m = a.Model!;
        var mesh = m.Mesh;
        if (mesh.Parts.Length == 0 || mesh.Parts.Length != mesh.Matrices.Length)
            Fail($"{name}: model has {mesh.Parts.Length} boxes / {mesh.Matrices.Length} matrices");
        if (mesh.Matrices.Any(x => !float.IsFinite(x.M11 + x.M22 + x.M33 + x.M41 + x.M42 + x.M43)))
            Fail($"{name}: model has a broken matrix");
        if (!ReferenceEquals(GunModel.Mesh(d), mesh))
            Fail($"{name}: GunModel doesn't draw the built body");
        var leaves = BuildStats.AllLeaves(a.Build).Count;
        if (m.Muzzles.Length != leaves || (d.MuzzlePoints != null && d.MuzzlePoints.Length != leaves))
            Fail($"{name}: {m.Muzzles.Length} muzzles for {leaves} barrels");
        for (var i = 1; i <= leaves; i++)
            if (m.Root.Find($"muzzle_{i}") is not { } mz || (mz.Flags & NodeFlags.Socket) == 0)
                Fail($"{name}: no muzzle_{i} socket in the scene graph");
        for (var i = 1; i <= a.Build.Receivers.Count; i++)
            if (m.Root.FindSocket($"receiver_{i}") == null)
                Fail($"{name}: no receiver_{i} socket in the scene graph");
        if (d.MuzzlePoints is { } pts && pts.Select(p => p.Muzzle).Distinct().Count() < Math.Min(pts.Length, 2))
            Fail($"{name}: all barrels share one muzzle position");
        // Hand anchors: on the model, and what the arms use.
        var (mn, mx) = BuildModel.Bounds(m.Root, true);
        mn -= new Vector3(0.12f);
        mx += new Vector3(0.12f);
        bool Inside(Vector3 p) => p.X >= mn.X && p.Y >= mn.Y && p.Z >= mn.Z && p.X <= mx.X && p.Y <= mx.Y && p.Z <= mx.Z;
        if (!Inside(m.Grip) || !Inside(m.Support) || !Inside(m.MagWell))
            Fail($"{name}: a hand anchor is off the model (grip {m.Grip}, support {m.Support}, mag {m.MagWell})");
        if (Vector3.Distance(m.Grip, BuildModel.GripAnchor) > 1e-4f)
            Fail($"{name}: trigger hand at {m.Grip}, not at the grip anchor");
        var arms = ArmAnchors.HandPoints(d);
        if (arms.Grip != m.Grip || arms.Support != m.Support || arms.MagWell != m.MagWell)
            Fail($"{name}: the arms don't use the built model's hand anchors");
        try
        {
            _ = ArmAnchors.For(d);
            var rig = new GunRig();
            rig.Build(d);
            if (rig.PartCount < mesh.Parts.Length)
                Fail($"{name}: the scene graph gun rig has {rig.PartCount} boxes for {mesh.Parts.Length}");
            if (GunModel.RecordParts(d, 0f).Count < mesh.Parts.Length)
                Fail($"{name}: recorded fewer parts than the model has");
        }
        catch (Exception ex)
        {
            Fail($"{name}: drawing paths threw {ex.GetType().Name}: {ex.Message}");
        }
        // The optic's sight line sits on the optic mount.
        if (d.Optic != OpticKind.Iron)
        {
            if (Sights.Lens(d) == null)
                Fail($"{name}: optic {d.Optic} has no lens");
            if (GunModel.SightHeight(d) <= mesh.Mount)
                Fail($"{name}: sight line {GunModel.SightHeight(d):0.000} isn't above the optic mount {mesh.Mount:0.000}");
        }
    }

    private static void ExpectWhyNot(WeaponBuild b, string parent, string socket, string part, string expect)
    {
        var p = Parts.Find(part);
        if (p == null)
        {
            Fail($"rules test needs part '{part}'");
            return;
        }
        var why = BuildRules.WhyNot(b, parent, socket, p);
        if (why == null)
            Fail($"rules: {part} into {(parent.Length == 0 ? "the grip" : parent)}/{socket} was allowed (expected \"{expect}\")");
        else if (!why.Contains(expect, StringComparison.OrdinalIgnoreCase))
            Fail($"rules: {part} into {parent}/{socket}: \"{why}\" doesn't say \"{expect}\"");
        else
            Line($"  refused: {why}");
    }

    private static void CheckBuilderRules()
    {
        var pistol = Parts.Find("Pistol Grip")!;
        var b = new WeaponBuild("Rules", pistol);
        // Replacing or removing a part takes it fully off the build (a half-detached node crashed the workbench tree).
        {
            var t = new WeaponBuild("Swap", pistol);
            var swapped = t.Root!.Attach("receiver_1", Parts.Find("Plinker Receiver")!);
            t.Root.Attach("receiver_1", Parts.Find("Plinker Receiver")!);
            if (swapped.Parent != null || swapped.ParentSocket != null || swapped.Path() != "")
                Fail("rules: a replaced receiver is still half attached");
            var second = t.Root.Child("receiver_1")!;
            second.Detach();
            if (t.Root.Child("receiver_1") != null || second.Path() != "")
                Fail("rules: a removed receiver is still on the grip");
        }
        if (BuildRules.Check(b).FirstOrDefault() is not { Incomplete: true } first || !first.Message.Contains("receiver"))
            Fail("rules: an empty grip should be incomplete for want of a receiver");
        var plinker = Parts.Find("Plinker Receiver")!;
        for (var i = 1; i <= 3; i++)
            if (BuildRules.WhyNot(b, "", $"receiver_{i}", plinker) is { } why)
                Fail($"rules: a Plinker into receiver_{i} of the Pistol Grip was refused: {why}");
            else
                b.Root!.Attach($"receiver_{i}", plinker);
        ExpectWhyNot(b, "", "receiver_4", "Plinker Receiver", "holds 3 receivers");
        var incomplete = BuildRules.Check(b);
        if (!incomplete.Any(p => p.Incomplete && p.Message.Contains("needs a barrel")) || !incomplete.Any(p => p.Message.Contains("needs a feed")) || !incomplete.Any(p => p.Message.Contains("needs ammo"))
            || !incomplete.Any(p => p.Incomplete && p.Message.Contains("needs an action")))
            Fail("rules: receivers with nothing on them should list a missing barrel, feed, ammo and action");
        ExpectWhyNot(b, "receiver_1", "feed", "Box Mag", "made for rifle rounds");
        // Ammo is universal (2026-10-23): Buckshot goes into a pistol receiver (it becomes a spread of pistol pellets).
        if (Parts.Find("Buckshot") is not { } buck)
            Fail("rules test needs part 'Buckshot'");
        else if (BuildRules.WhyNot(b, "receiver_1", "ammo", buck) is { } whyBuck)
            Fail($"rules: Buckshot should fit the Plinker (ammo fits every receiver), got: {whyBuck}");
        ExpectWhyNot(b, "receiver_1", "barrel", "Vertical Foregrip", "mounts on a foregrip socket");
        ExpectWhyNot(b, "receiver_1", "barrel", "Rifle Grip", "start a new build");
        ExpectWhyNot(b, "", "receiver_1", "Red Dot", "mounts on an optic socket");

        var rifle = new WeaponBuild("Rules 2", Parts.Find("Rifle Grip")!);
        var burst = rifle.Root!.Attach("receiver_1", Parts.Find("Reaper Battle Rifle Receiver")!);
        if (BuildRules.WhyNot(rifle, "receiver_1", "feed", Parts.Find("Belt Box")!) is { } noActionYet)
            Fail($"rules: a belt on a receiver with no action yet should go on (the action decides): {noActionYet}");
        burst.Attach("action", Parts.Find("Three-Round Burst Group")!);
        ExpectWhyNot(rifle, "receiver_1", "feed", "Belt Box", "needs full auto, chain or rotary action");
        // ...and the other way round: an action that can't pull the belt already on the receiver.
        var belted = new WeaponBuild("Rules 3", Parts.Find("Rifle Grip")!);
        belted.Root!.Attach("receiver_1", Parts.Find("Workhorse Receiver")!).Attach("feed", Parts.Find("Belt Box")!);
        ExpectWhyNot(belted, "receiver_1", "action", "Turn Bolt", "can't work the Belt Box");
        if (BuildRules.WhyNot(belted, "receiver_1", "action", Parts.Find("Full-Auto Sear")!) is { } autoWhy)
            Fail($"rules: a full-auto sear should pull a belt: {autoWhy}");
        ExpectWhyNot(belted, "receiver_1", "feed", "Full-Auto Sear", "mounts on an action socket");
        var tray = burst.Attach("barrel", Parts.Find("Party Tray Splitter")!);
        tray.Attach("barrel_1", Parts.Find("Trident Splitter")!);
        ExpectWhyNot(rifle, "receiver_1/barrel/barrel_1", "barrel_1", "Y-Splitter", "stack 2 deep");
        var wheel = rifle.Root.Attach("receiver_2", Parts.Find("Hellwheel Rotary Receiver")!);
        ExpectWhyNot(rifle, "receiver_2", "barrel_1", "Y-Splitter", "takes barrels only");
        ExpectWhyNot(rifle, "receiver_2", "barrel_1", "Barrel of Barrels", "too many barrels on the hellwheel");
        wheel.Attach("barrel_1", Parts.Find("Standard Barrel")!);
        if (!BuildRules.Check(rifle).Any(p => p.Message.Contains("at least 3 barrels")))
            Fail("rules: a Hellwheel with one barrel should need at least 3");

        // Weight: a pistol grip carries 5 kg (10 at most).
        var heavy = new WeaponBuild("Too Heavy", pistol);
        heavy.Root!.Attach("receiver_1", Parts.Find("Hellwheel Rotary Receiver")!).Attach("action", Parts.Find("Electric Rotary Drive")!);
        heavy.Root.Attach("receiver_2", Parts.Find("Hellwheel Rotary Receiver")!).Attach("action", Parts.Find("Electric Rotary Drive")!);
        ExpectWhyNot(heavy, "", "receiver_3", "Hellwheel Rotary Receiver", "too heavy");

        // The fire style is the build's Chain fire choice, saved in the style field ("grip" or anything else: the grip's default).
        var oldStyle = WeaponBuild.Parse("Old|rotate|Pistol Grip{receiver_1:Plinker Receiver}", Parts.Find);
        var gripStyle = WeaponBuild.Parse("Old|grip|Pistol Grip{receiver_1:Plinker Receiver}", Parts.Find);
        if (oldStyle.Style != FireStyle.Rotate || !oldStyle.ToText().StartsWith("Old|rotate|", StringComparison.Ordinal) || gripStyle.Style != FireStyle.Volley || gripStyle.StyleChoice != null)
            Fail("rules: a saved style field should be the build's fire style (\"grip\" = the grip's default)");
        // A build must start from a grip.
        var dropped = new List<string>();
        var parsed = WeaponBuild.Parse("Odd|volley|Workhorse Receiver{barrel:Standard Barrel}", Parts.Find, dropped);
        if (parsed.Root != null || dropped.Count == 0)
            Fail("rules: a saved build rooted on a receiver should be dropped");
    }

    private static WeaponDef? Example(string name) =>
        BuilderExamples.All().FirstOrDefault(e => e.Build.Name == name) is { } ex ? WeaponBuilder.Assemble(ex.Build).Def : null;

    private static void CheckBuilderShots()
    {
        var dir = -Vector3.UnitZ;
        var eye = new Vector3(0, 1.6f, 0);

        // Standard weapons: exactly the old rays (one per pellet from the eye, seeded by pellet index, full damage).
        var changed = 0;
        foreach (var w in Weapons.All)
        {
            var rays = Game.ShotRays(w, eye, dir, 0.02f, 1234);
            if (w.MuzzlePoints != null || w.BuiltMesh != null || w.RotateGroups || w.SpoolTime != 0 || w.NoAds || w.ShotDamage != w.Damage * w.Pellets || rays.Count != w.Pellets)
                changed++;
            for (var i = 0; i < rays.Count; i++)
                if (rays[i].Origin != eye || rays[i].Dir != Weapons.SpreadDirection(dir, 0.02f, 1234, i) || rays[i].Damage != w.Damage)
                    changed++;
        }
        if (changed > 0)
            Fail($"standard weapons: {changed} difference(s) from the old shot path");
        else
            Line($"standard weapons: all {Weapons.All.Length} fire exactly as before");

        void Expect(string name, int shots, int perShot, int origins)
        {
            if (Example(name) is not { } d)
            {
                Fail($"shots: no example '{name}'");
                return;
            }
            var seen = new HashSet<Vector3>();
            for (uint s = 0; s < shots; s++)
            {
                var rays = Game.ShotRays(d, eye, dir, 0.01f, s);
                if (rays.Count != perShot)
                    Fail($"shots: {name} shot {s} fired {rays.Count} projectile(s), expected {perShot}");
                foreach (var r in rays)
                    seen.Add(r.Origin);
            }
            Line($"shots: {name}: {perShot} projectile(s) per shot from {seen.Count} muzzle position(s) over {shots} shot(s)");
            if (seen.Count != origins)
                Fail($"shots: {name} fired from {seen.Count} positions, expected {origins}");
        }
        Expect("Plain Rifle", 3, 1, 1);
        Expect("Twin Splitter Shotgun", 1, 16, 2);
        Expect("Triple Pistol", 1, 3, 3);
        Expect("Nine Lives", 1, 9, 9);
        Expect("Minigun", 6, 1, 6); // rotary: one barrel per shot, all six in turn
        Expect("Lumberjack", 1, 48, 6);
        Expect("Shopping Spree", 1, 16, 16);
        Expect("Party Platter", 1, 12, 12);

        // A rotating grip: the receivers take turns.
        var comp = BuilderExamples.All().First(e => e.Build.Name == "Triple Pistol").Build.Clone();
        var turns = new WeaponBuild("Turns", Parts.Find("Pistol Grip")!) { StyleChoice = FireStyle.Rotate }; // Chain fire on
        foreach (var (s, n) in comp.Root!.Children())
            turns.Root!.Attach(s.Name, n.Clone());
        if (WeaponBuilder.Assemble(turns).Def is not { } td)
            Fail("shots: the rotating Triple Pistol isn't legal");
        else
        {
            var groups = Enumerable.Range(0, 3).Select(s => Game.ShotRays(td, eye, dir, 0, (uint)s)).ToList();
            if (groups.Any(g => g.Count != 1) || groups.Select(g => g[0].Barrel).Distinct().Count() != 3)
                Fail("shots: a rotating grip should fire one receiver per shot, each in turn");
            else
                Line($"shots: rotating grip: receivers {string.Join(", ", groups.Select(g => g[0].Barrel))} in turn; rpm {td.Rpm} (volley {Example("Triple Pistol")?.Rpm})");
        }
        if (Example("Minigun") is { } mg && (mg.SpoolTime <= 0 || mg.SpoolStart >= 1 || mg.Rpm != 2400))
            Fail($"Minigun: spool {mg.SpoolTime} from {mg.SpoolStart}, rpm {mg.Rpm} (expected a spool-up to 2400)");
        if (Example("Shopping Spree") is { } cart && !cart.NoAds)
            Fail("Push Cart builds should be hip fire only");
    }

    private static void CheckBuilderServerShots()
    {
        var triple = Example("Triple Pistol");
        var platter = Example("Party Platter");
        if (triple == null || platter == null)
            return;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: Maps.Find("Workbench Range")?.Id ?? 0);
        game.UseBuiltWeapons(triple, platter);
        var me = game.Local!;
        me.Alive = true;
        if (me.DefFor(me.Primary) != triple || me.DefFor(me.Secondary) != platter || game.Slots[0].Def != triple)
            Fail("server: the built weapons didn't become the player's loadout");
        foreach (var (def, want) in new[] { (triple, 3), (platter, 12) })
        {
            var before = game.Tracers.Count;
            game.TestServerShot(def, -Vector3.UnitZ, 7);
            var added = game.Tracers.Skip(before).ToList();
            var from = added.Select(t => t.From).Distinct().Count();
            Line($"server: {def.Name}: {added.Count} projectile(s) from {from} muzzle position(s)");
            if (added.Count != want || from != want)
                Fail($"server: {def.Name} resolved {added.Count} projectiles from {from} positions (expected {want} from {want})");
        }
    }

    private static void CheckBuilderSaves()
    {
        foreach (var ex in BuilderExamples.All())
        {
            var text = ex.Build.ToText();
            var back = WeaponBuild.Parse(text, Parts.Find);
            if (back.ToText() != text)
                Fail($"save: '{ex.Build.Name}' didn't round-trip:\n    {text}\n    {back.ToText()}");
        }
        var keepSlots = BuildStore.Slots.ToArray();
        var ex0 = BuilderExamples.All();
        for (var i = 0; i < BuildStore.SlotCount; i++)
            BuildStore.Slots[i] = ex0[i + 4].Build;
        BuildStore.Selected = 2;
        BuildStore.Second = 3;
        var lines = BuildStore.Lines();
        BuildStore.Load(lines, Parts.Find, out var dropped);
        if (dropped.Count > 0 || !BuildStore.Lines().SequenceEqual(lines) || BuildStore.Selected != 2 || BuildStore.Second != 3)
            Fail("save: workbench slots didn't round-trip");
        // A save naming a part that's gone loads without it.
        BuildStore.Load(["slot1=Old|volley|Pistol Grip{receiver_1:Plinker Receiver{barrel:Gold Plated Barrel},receiver_9:Plinker Receiver}", "slot2=garbage"], Parts.Find, out dropped);
        if (BuildStore.Slots[0]?.Receivers.Count != 1 || dropped.Count < 2)
            Fail($"save: an old build with a missing part and an extra receiver should load with them dropped ({string.Join("; ", dropped)})");
        else
            Line($"save: old build loaded, dropped: {string.Join("; ", dropped)}");
        Array.Copy(keepSlots, BuildStore.Slots, keepSlots.Length);
    }
}
