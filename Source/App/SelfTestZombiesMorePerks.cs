using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies more perks" (developer request, 2026-10-27: "Add five to eight more perks into the zombie
/// mode. And make machines for them", then "make each of the perk machines more unique"): the rules list twelve perks,
/// each with a known HUD icon; every perk is on sale at its machine on Zombies Test and the prices double past the 4th
/// (no cap below the perk count); each new perk's effect, measured in a short seeded scenario (Fleetfoot Fizz's speed,
/// Marksman Malt's headshot damage and points, Bloodroot Tonic's melee damage and healing, Windfall Soda's power-ups
/// over the cap, Hammerhand Ale's boards, Salvage Stout's part drops and salvage, Bulwark Brew's plate); a down loses
/// them all while Second Wind still gets you up; the 16-bit perk flags and the plate reach a client; the perk code
/// allocates nothing per tick; and every perk has a machine of its own shape on both Zombies maps (solid props on
/// Blackmoor Asylum), inside the plain machine's footprint.
/// </summary>
public static partial class SelfTest
{
    private static readonly string[] NewPerks = ["fleetfoot", "marksman", "bloodroot", "windfall", "hammerhand", "salvage", "bulwark"];

    private static void CheckZombiesMorePerks()
    {
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        var defaults = ZombiesRules.Defaults();
        var icons = rules.Perks.Count(p => ZombiePerkIcons.Known(p.Icon));
        var sameAsBuiltIn = rules.Perks.Count == defaults.Perks.Count && rules.Perks.Zip(defaults.Perks).All(t => t.First.Key == t.Second.Key && t.First.Cost == t.Second.Cost && t.First.Icon == t.Second.Icon);
        var badOrder = rules.BotPerkOrder.Where(k => rules.PerkIndex(k) < 0).ToList();
        ZLine($"more perks: {rules.Perks.Count} perks ({string.Join(", ", rules.Perks.Select(p => $"{p.Key} {p.Cost}"))}), {icons} with a known icon, maxPerks {rules.MaxPerks}, the built-in rules match {sameAsBuiltIn}, bot order {string.Join(">", rules.BotPerkOrder)}");
        if (rules.Perks.Count != 12 || NewPerks.Any(k => rules.PerkIndex(k) < 0) || icons != rules.Perks.Count || rules.MaxPerks < rules.Perks.Count || !sameAsBuiltIn || badOrder.Count > 0)
            Fail("the rules should list 12 perks (the 7 new ones), each with a known icon, maxPerks at least the perk count, the built-in rules the same, and only real perks in the bots' order");
        if (rules.Perks.Select(p => p.Icon).Distinct().Count() != rules.Perks.Count || rules.Perks.Select(p => p.Color).Distinct().Count() != rules.Perks.Count)
            Fail("every perk should have its own icon and colour");

        CheckPerkMachines();

        if (ZTestMap?.Zombies is not { } zm)
        {
            Fail("Zombies Test is missing");
            return;
        }
        var now = 0.0;
        using var g = ZGame(0, ref now, seed: 777, tweak: r =>
        {
            r.FirstRoundDelay = 9999;
            r.PowerFirstAt = 0; // (no points schedule: only Windfall Soda's own chance drops power-ups)
            r.PowerDropChance = 0;
        });
        var me = g.Local!;
        var zp = g.ZP(me);
        var R = g.ZRules;
        int K(string key) => R.PerkIndex(key);

        // ---- Every perk on sale; the price doubles past the 4th ----
        g.ZPower = true;
        zp.Perks = 0;
        zp.Points = 10_000_000;
        var paid = new List<string>();
        var priceOk = true;
        foreach (var m in zm.PerkMachines.OrderBy(m => K(m.Perk)))
        {
            var k = K(m.Perk);
            var held = Game.PerkCount(zp);
            var before = zp.Points;
            me.Teleport(m.At + World.FlatForward(m.Yaw) * 1.0f);
            g.SelfTestUse(me, now);
            var cost = before - zp.Points;
            var want = R.Perks[k].Cost * (held < R.FullPricePerks ? 1 : 1 << (held - R.FullPricePerks + 1));
            paid.Add($"{m.Perk} {cost}");
            priceOk &= zp.Has(k) && cost == want;
        }
        ZLine($"buying every perk in turn: {string.Join(", ", paid)}; held {Game.PerkCount(zp)}");
        if (!priceOk || Game.PerkCount(zp) != R.Perks.Count)
            Fail("every perk should be on sale at its machine, the 5th at 2x, the 6th at 4x ... (each step doubling), with no cap below the perk count");

        // ---- The flags reach a client (16 bits; the plate bit) ----
        {
            zp.PlateReadyAt = 0;
            g.SelfTestPerkTick(me, now);
            var w = new PacketWriter();
            g.WriteZEnts(w);
            var ents = w.Span.ToArray();
            var w2 = new PacketWriter();
            g.WriteZWorld(w2);
            var world = w2.Span.ToArray();
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(ZTestMap!);
            c.SelfTestReadZombies(ents, world);
            var cz = c.ZP(me.Id);
            ZLine($"replication: perks {zp.Perks:x4} -> {cz.Perks:x4}, plate {zp.PlateReady} -> {cz.PlateReady}, protocol {ServerInfo.ProtocolVersion}");
            if (cz.Perks != zp.Perks || !cz.PlateReady || ServerInfo.ProtocolVersion < 57)
                Fail("all 12 perk flags and the plate should reach a client (protocol 57)");
        }

        // ---- No allocation per tick with every perk held ----
        {
            for (var i = 0; i < 50; i++)
                PerkTickWork(g, me, now + i * World.TickDt);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++)
                PerkTickWork(g, me, now + i * World.TickDt);
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            ZLine($"perk ticks: 1000 ticks of every perk's lookups and the player's tick: {bytes} bytes");
            if (bytes > 0)
                Fail($"the perk code should allocate nothing per tick ({bytes} bytes in 1000)");
        }

        // ---- Down: every perk is lost; Second Wind still gets you up ----
        g.SelfTestDown(me, now);
        var lostAll = zp.Perks == 0;
        var selfUp = zp.SelfReviveAt > 0;
        ZLine($"down with all 12: perks left {zp.Perks}, Second Wind gets them up {selfUp} (losePerksOnDown {R.LosePerksOnDown})");
        if (!lostAll || !selfUp)
            Fail("going down should lose every perk (the new ones too) while Second Wind still gets you up");
        zp.Downed = false;
        zp.SelfReviveAt = -1;
        me.Health = World.MaxHealth;
        me.ImmuneUntil = 0;

        CheckPerkEffects(g, me, zp, zm, now);
    }

    /// <summary>What a tick does for a player with perks (the lookups and the player's own tick).</summary>
    private static float PerkTickWork(Game g, Player me, double t)
    {
        g.SelfTestPerkTick(me, t);
        return g.ZombieMoveMult(me) + g.ZombieReloadMult(me) + g.ZombieFireIntervalMult(me) + Game.PerkCount(g.ZP(me));
    }

    private static void CheckPerkEffects(Game g, Player me, ZPlayer zp, ZombiesMap zm, double now)
    {
        var R = g.ZRules;
        void Only(params string[] keys)
        {
            zp.Perks = 0;
            foreach (var k in keys)
                zp.Perks |= (ushort)(1 << R.PerkIndex(k));
            zp.PlateReadyAt = now;
        }

        // A zombie to hit, standing still 1.5 m in front of the player in the start room.
        var z = g.SelfTestSpawnZombie(0, now);
        var start = new Vector3(0, 0, 0);
        me.Teleport(start);
        me.Yaw = 0;
        var at = start + World.FlatForward(0) * 1.5f;
        void Place()
        {
            z.Sim.Position = at;
            z.Yaw = MathF.PI;
            Array.Fill(z.History, at);
            z.State = ZState.Chase;
            z.Health = z.MaxHealth = 1_000_000;
        }

        // Fleetfoot Fizz: 20 percent faster on foot (the input's speed, as the client sends it and the server allows it).
        Only();
        var plainMove = g.ZombieMoveMult(me);
        Only("fleetfoot");
        var fleetMove = g.ZombieMoveMult(me);
        float Walk(float mult)
        {
            var from = new Vector3(0, 0, -3); // (towards the Hall door, +Z: nothing in the way for half a second)
            var s = me.Sim;
            s.Position = from;
            s.Velocity = Vector3.Zero;
            for (var i = 0; i < 30; i++)
                World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = MathF.PI, SpeedPct = (byte)MathF.Round(100 * mult), Tick = (uint)(i + 1) });
            var d = s.Position - from;
            return MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        }
        var plainWalk = Walk(plainMove);
        var fleetWalk = Walk(fleetMove);
        ZLine($"Fleetfoot Fizz: speed x{fleetMove:0.00} (x{plainMove:0.00} without); half a second's walk {fleetWalk:0.00} m vs {plainWalk:0.00} m");
        if (MathF.Abs(fleetMove - 1.2f) > 0.001f || plainMove != 1f || fleetWalk < plainWalk * 1.15f)
            Fail("Fleetfoot Fizz should make you move 20 percent faster");

        // Marksman Malt: headshots do 1.5x; a headshot kill pays 50 more.
        var head = at + new Vector3(0, World.HeadStart + 0.15f, 0);
        var eye = head - World.FlatForward(0) * 1.2f;
        int HeadShot()
        {
            Place();
            me.ShotClock = 0;
            g.SelfTestServerShot(me, eye, Vector3.Normalize(head - eye), now);
            return z.MaxHealth - z.Health;
        }
        Only();
        var plainHead = HeadShot();
        Only("marksman");
        var markHead = HeadShot();
        int KillPoints(bool marksman)
        {
            Only(marksman ? new[] { "marksman" } : Array.Empty<string>());
            Place();
            z.Health = 1;
            var before = zp.Points;
            me.ShotClock = 0;
            g.SelfTestServerShot(me, eye, Vector3.Normalize(head - eye), now);
            return zp.Points - before;
        }
        var plainKill = KillPoints(false);
        var markKill = KillPoints(true);
        ZLine($"Marksman Malt: a headshot does {markHead} ({plainHead} without); a headshot kill pays {markKill} ({plainKill} without)");
        if (plainHead <= 0 || MathF.Abs(markHead - plainHead * 1.5f) > 2 || markKill != plainKill + 50)
            Fail("Marksman Malt should make headshots do 50 percent more and pay 50 more points a headshot kill");

        // Bloodroot Tonic: melee does double, and a swing that hits heals 30.
        int Swing(bool bloodroot, out int healed)
        {
            Only(bloodroot ? new[] { "bloodroot" } : Array.Empty<string>());
            Place();
            me.Health = 40;
            g.SelfTestZombieMelee(me, World.FlatForward(0), 100, now);
            healed = me.Health - 40;
            return z.MaxHealth - z.Health;
        }
        var plainMelee = Swing(false, out var plainHeal);
        var rootMelee = Swing(true, out var rootHeal);
        ZLine($"Bloodroot Tonic: a melee swing does {rootMelee} ({plainMelee} without) and heals {rootHeal} ({plainHeal} without)");
        if (plainMelee <= 0 || rootMelee != plainMelee * 2 || rootHeal != 30 || plainHeal != 0)
            Fail("Bloodroot Tonic should double melee damage and heal 30 on a hit");
        me.Health = World.MaxHealth;

        // Windfall Soda: power-ups from your kills (none without it here), up to 2 over the round's cap.
        int Kills(bool perk, string key, Func<int> count, Action reset)
        {
            Only(perk ? new[] { key } : Array.Empty<string>());
            reset();
            var n = 0;
            for (var i = 0; i < 300; i++)
            {
                Place();
                g.DamageZombie(me, z, 2_000_000, false, false, now);
                n += count();
                reset();
            }
            return n;
        }
        g.ZRound = 5;
        var powersPlain = 0;
        var powersLucky = 0;
        {
            g.ZPowerUps.Clear();
            g.SelfTestResetPowerCount();
            for (var i = 0; i < 300; i++)
            {
                Only();
                Place();
                g.DamageZombie(me, z, 2_000_000, false, false, now);
            }
            powersPlain = g.ZPowerUps.Count;
            g.ZPowerUps.Clear();
            g.SelfTestResetPowerCount();
            for (var i = 0; i < 300; i++)
            {
                Only("windfall");
                Place();
                g.DamageZombie(me, z, 2_000_000, false, false, now);
            }
            powersLucky = g.ZPowerUps.Count;
            g.ZPowerUps.Clear();
        }
        var cap = R.PowerMaxPerRound + R.Perks[R.PerkIndex("windfall")].PowerUpExtra;
        ZLine($"Windfall Soda: 300 kills drop {powersLucky} power-ups ({powersPlain} without; the round's cap {R.PowerMaxPerRound}, +{cap - R.PowerMaxPerRound} with it)");
        if (powersPlain != 0 || powersLucky != cap)
            Fail("Windfall Soda's kills should drop power-ups on their own chance, up to its extra over the round's cap");

        // Salvage Stout: parts drop twice as often from your kills; a salvage pile gives one more.
        var dropsPlain = Kills(false, "salvage", () => g.ZDrops.Count, () => g.ZDrops.Clear());
        var dropsSalvage = Kills(true, "salvage", () => g.ZDrops.Count, () => g.ZDrops.Clear());
        int Search(int pile, bool perk)
        {
            Only(perk ? new[] { "salvage" } : Array.Empty<string>());
            var s = zm.Salvage[pile];
            me.Teleport(s.At + new Vector3(0.6f, 0, 0));
            var before = zp.Parts.Values.Sum();
            g.SelfTestUse(me, now);
            return zp.Parts.Values.Sum() - before;
        }
        var pilePlain = Search(0, false);
        var pileSalvage = Search(1, true);
        ZLine($"Salvage Stout: 300 kills drop {dropsSalvage} parts ({dropsPlain} without); a salvage pile gives {pileSalvage} ({pilePlain} without)");
        if (dropsPlain <= 0 || dropsSalvage < dropsPlain * 1.6f || pileSalvage != pilePlain + 1)
            Fail("Salvage Stout should make your kills drop parts about twice as often and a salvage pile give a part more");

        // Hammerhand Ale: boards go up 2.5x as fast, for double the points.
        (int Boards, int Points) Rebuild(bool perk)
        {
            Only(perk ? new[] { "hammerhand" } : Array.Empty<string>());
            var w = zm.Windows[0];
            g.ZBoards[0] = 0;
            zp.RepairPointsThisRound = 0;
            me.Teleport(w.Inside);
            var before = zp.Points;
            g.ServerSetUse(me, true, now);
            var t = now;
            for (var i = 0; i < 90; i++)
                g.SelfTestPerkTick(me, t += World.TickDt);
            g.ServerSetUse(me, false, t);
            return (g.ZBoards[0], zp.Points - before);
        }
        var plainBoards = Rebuild(false);
        var hammerBoards = Rebuild(true);
        ZLine($"Hammerhand Ale: 1.5 s at a window puts up {hammerBoards.Boards} boards for {hammerBoards.Points} points ({plainBoards.Boards} for {plainBoards.Points} without)");
        if (plainBoards.Boards <= 0 || hammerBoards.Boards < plainBoards.Boards * 2 || hammerBoards.Points != hammerBoards.Boards * R.RepairPoints * 2)
            Fail("Hammerhand Ale should put boards up 2.5 times as fast, each for double the points");

        // Bulwark Brew: the plate takes a whole hit, then grows back 12 s later.
        Only("bulwark");
        me.Health = World.MaxHealth;
        me.ImmuneUntil = 0;
        g.ZombieHurt(me, R.ZombieDamage, now);
        var first = World.MaxHealth - me.Health;
        g.SelfTestPerkTick(me, now);
        var plateDown = !zp.PlateReady;
        g.ZombieHurt(me, R.ZombieDamage, now + 0.1);
        var second = World.MaxHealth - me.Health;
        me.Health = World.MaxHealth;
        var later = now + R.Perks[R.PerkIndex("bulwark")].PlateSeconds + 0.1;
        g.SelfTestPerkTick(me, later);
        var plateBack = zp.PlateReady;
        g.ZombieHurt(me, R.ZombieDamage, later);
        var third = World.MaxHealth - me.Health;
        ZLine($"Bulwark Brew: hits take {first}, then {second} (plate down {plateDown}), then {third} once it grew back (up {plateBack})");
        if (first != 0 || second != R.ZombieDamage || !plateDown || !plateBack || third != 0)
            Fail("Bulwark Brew's plate should take a whole zombie hit, then grow back");
        me.Health = World.MaxHealth;
        zp.Perks = 0;
    }

    /// <summary>Every perk has its own machine prop (distinct, inside the footprint, the shared face), on both Zombies maps.</summary>
    private static void CheckPerkMachines()
    {
        var rules = ZombiesRules.Current;
        var defs = rules.Perks.Select(p => Props.Find(ZombiesMap.MachineProp.PerkFor(p.Key))).ToList();
        var missing = rules.Perks.Where((p, i) => defs[i] == null).Select(p => p.Key).ToList();
        var shapes = defs.Where(d => d != null).Select(d => string.Join(";", d!.Parts.Select(q => $"{q.Size.X:0.00},{q.Size.Y:0.00},{q.Size.Z:0.00}@{q.Offset.X:0.00},{q.Offset.Y:0.00},{q.Offset.Z:0.00}"))).ToList();
        var outside = defs.Where(d => d != null && (d.Bounds.Half.X + MathF.Abs(d.Bounds.Center.X) > 0.475f || d.Bounds.Center.Z - d.Bounds.Half.Z < -0.415f || d.Bounds.Center.Z + d.Bounds.Half.Z > 0.415f || d.Collision != PropCollision.Solid)).Select(d => d!.Name).ToList();
        var noFace = defs.Where(d => d != null && !d.Parts.Any(q => MathF.Abs(q.Offset.Y - 1.62f) < 0.01f && MathF.Abs(q.Offset.Z + 0.325f) < 0.01f && q.Size.X >= 0.7f)).Select(d => d!.Name).ToList();
        ZLine($"perk machines: {defs.Count(d => d != null)} of {rules.Perks.Count} perks have their own machine ({string.Join(", ", defs.Where(d => d != null).Select(d => $"{d!.Name} {d.Parts.Count} parts, {d.Bounds.Center.Y + d.Bounds.Half.Y:0.00} m"))}); "
              + $"{shapes.Distinct().Count()} distinct shapes; outside the footprint or not solid: {outside.Count}; without the face: {noFace.Count}");
        if (missing.Count > 0 || shapes.Distinct().Count() != shapes.Count || outside.Count > 0 || noFace.Count > 0)
            Fail($"every perk should have a solid machine of its own shape with the shared face inside the plain machine's footprint (missing {string.Join(", ", missing)}; outside {string.Join(", ", outside)}; no face {string.Join(", ", noFace)})");

        foreach (var name in new[] { "Zombies Test", "Blackmoor Asylum" })
        {
            if (Maps.Find(name) is not { Zombies: { } zm } map)
            {
                Fail($"{name} is missing");
                continue;
            }
            var unplaced = rules.Perks.Where(p => zm.PerkMachines.All(m => m.Perk != p.Key)).Select(p => p.Key).ToList();
            var wrongProp = zm.PerkMachines.Where(m => m.PropName != ZombiesMap.MachineProp.PerkFor(m.Perk) && Props.Find(m.PropName)?.Name != Props.Find(ZombiesMap.MachineProp.PerkFor(m.Perk))?.Name).Select(m => m.Perk).ToList();
            var standing = zm.MachineProps
                ? zm.PerkMachines.Count(m => map.PropInstances.Any(pi => pi.Def.Name.Equals(m.PropName, StringComparison.OrdinalIgnoreCase) && Vector2.Distance(new Vector2(pi.Position.X, pi.Position.Z), new Vector2(m.At.X, m.At.Z)) < 0.01f))
                : -1;
            var powered = zm.PerkMachines.Count(m => NewPerks.Contains(m.Perk) && m.NeedsPower);
            ZLine($"{name}: {zm.PerkMachines.Count} perk machines, {powered} of the new ones on the power; unplaced {unplaced.Count}, wrong prop {wrongProp.Count}, standing as solid props {(standing < 0 ? "n/a (no machine props)" : standing.ToString())}");
            if (unplaced.Count > 0 || wrongProp.Count > 0 || (zm.MachineProps && standing != zm.PerkMachines.Count) || powered is 0 || powered == NewPerks.Length)
                Fail($"{name}: every perk needs its own machine (some behind the power, some not){(zm.MachineProps ? ", standing as a solid prop" : "")}");
        }
    }
}
