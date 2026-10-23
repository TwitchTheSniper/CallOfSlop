using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies mode" (docs/zombies/ZOMBIES-MODE.md): the rules file and the round curve, the map section
/// (Zombies Test), the window openings' collision layer, spawners only in unlocked areas and the alive cap, doors, boards,
/// wall buys and ammo, the mystery box's odds and the teddy bear, perks, downs, revives, bleeding out, the game over,
/// points per action, part drops and the workbench, replication, determinism, the AI cost with 24 alive and a bots-only
/// run through rounds 1-3.
/// </summary>
public static partial class SelfTest
{
    private static Map? ZTestMap => Maps.Find("Zombies Test");

    /// <summary>A Zombies game on Zombies Test (offline), live and playing, with the given rules tweaks.</summary>
    private static Game ZGame(int bots, ref double now, uint seed = 1234, Action<ZombiesRules>? tweak = null, bool hostIn = true, BotDifficulty skill = BotDifficulty.Normal, Map? onMap = null)
    {
        var map = onMap ?? ZTestMap!;
        World.SetMap(map);
        var g = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: bots, botSkill: skill, mapId: map.Id)
        {
            MapVoteEnabled = false,
        };
        var rules = ZombiesRules.Load(File.ReadAllText(ZombiesRules.PathName), "test rules");
        tweak?.Invoke(rules);
        g.ZRules = rules;
        g.ZSeed = seed;
        g.ZHostSitsOut = !hostIn;
        g.Mode = GameMode.Zombies;
        Run(g, ref now, Game.CountdownSeconds + 0.2);
        if (hostIn)
        {
            g.RequestRespawn();
            Run(g, ref now, 0.1);
        }
        return g;
    }

    private static void ZTicks(Game g, ref double now, double seconds)
    {
        for (var t = 0.0; t < seconds; t += World.TickDt)
        {
            now += World.TickDt;
            g.SelfTestZombiesTick(now);
        }
    }

    private static readonly List<string> ZNotes = new();

    /// <summary>A result line, also kept for the section's one GitHub notice (the report's numbers).</summary>
    private static void ZLine(string s)
    {
        Line(s);
        ZNotes.Add(s);
    }

    private static void CheckZombiesMode()
    {
        ZNotes.Clear();
        try
        {
            CheckZombiesModeBody();
        }
        finally
        {
            Line("::warning title=Zombies mode (results)::" + string.Join("%0A", ZNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
        }
    }

    private static void CheckZombiesModeBody()
    {
        // ---- The rules and the round curve ----
        var rules = ZombiesRules.Current;
        foreach (var e in ZombiesRules.LoadErrors)
            Fail($"zombies rules: {e}");
        var counts = Enumerable.Range(1, 30).Select(r => rules.ZombieCount(r, 1)).ToArray();
        var health = Enumerable.Range(1, 30).Select(rules.ZombieHealth).ToArray();
        ZLine($"round curve (solo): zombies {string.Join(" ", counts.Take(12))} ... r20 {counts[19]} r30 {counts[29]}; health r1 {health[0]} r5 {health[4]} r9 {health[8]} r10 {health[9]} r20 {health[19]}");
        ZLine($"  4 players: r1 {rules.ZombieCount(1, 4)} r5 {rules.ZombieCount(5, 4)} r10 {rules.ZombieCount(10, 4)}; sprinters r4 {rules.SpeedMix(4).Sprint:0.00} r5 {rules.SpeedMix(5).Sprint:0.00} r10 {rules.SpeedMix(10).Sprint:0.00}; runners r2 {rules.SpeedMix(2).Run:0.00} r3 {rules.SpeedMix(3).Run:0.00}; spawn delay r1 {rules.SpawnDelayFor(1):0.00} s r20 {rules.SpawnDelayFor(20):0.00} s");
        if (counts[0] is < 4 or > 8 || health[0] != 150 || health[8] != 630 || health[9] <= health[8])
            Fail("the round curve isn't World at War like (round 1: a handful at 150 health, +100 a round to round 9, then growing)");
        for (var i = 1; i < 30; i++)
            if (counts[i] < counts[i - 1] || health[i] < health[i - 1])
                Fail($"the round curve goes down at round {i + 1}");
        if (rules.SpeedMix(4).Sprint > 0 || rules.SpeedMix(5).Sprint <= 0 || rules.SpeedMix(2).Run > 0 || rules.ZombieCount(1, 4) <= counts[0])
            Fail("sprinters should start at round 5 and runners at round 3, and more players should mean more zombies");
        if (rules.Perks.Count != 5 || rules.Perk("jugg")?.DamageTaken >= 1 || rules.Perk("speed")?.Reload >= 1 || rules.Perk("doubletap")?.FireRate <= 1 || rules.Perk("revive") is not { SelfRevive: false, Revive: < 1f }
            || rules.Perk("secondwind") is not { SelfRevive: true })
            Fail("the four World at War perks and Second Wind (the only self-revive) aren't in the rules as expected");
        foreach (var bad in new[] { """{"format": "call-of-slop-zombies/1", "rounds": {"countBase": -3}}""", """{"format": "call-of-slop-zombies/1", "zombie": {"bite": 3}}""", """{"format": "x"}""" })
            try
            {
                ZombiesRules.Load(bad, "bad rules");
                Fail($"broken rules loaded: {bad}");
            }
            catch (WeaponFileException)
            {
            }

        // ---- The map section ----
        if (ZTestMap is not { Zombies: { } zm } map)
        {
            Fail("Maps/Dev/ZombiesTest.json is missing or has no \"zombies\" section");
            return;
        }
        ZLine($"Zombies Test: {zm.Areas.Count} areas, {zm.Doors.Count} doors, {zm.Windows.Count} windows, {zm.Spawners.Count} spawners, {zm.WallBuys.Count} wall buys, {zm.BoxSpots.Count} box spots, "
             + $"{zm.Benches.Count} workbenches, {zm.PerkMachines.Count} perk machines, slot upgrade {zm.SlotUpgrade != null}, power {zm.Power != null}, {zm.Salvage.Count} salvage");
        if (zm.Areas.Count < 4 || zm.Doors.Count < 3 || zm.Windows.Count < 3 || zm.BoxSpots.Count < 3 || zm.Benches.Count < 1 || zm.PerkMachines.Count < 4 || zm.SlotUpgrade == null || zm.Power == null)
            Fail("Zombies Test is missing something it should exercise");
        foreach (var wb in zm.WallBuys)
            if (StandardBuilds.Find(wb.Build) is not { } sb || !sb.Sources.Contains("wall"))
                Fail($"wall buy '{wb.Build}' isn't a standard build tagged \"wall\"");
        foreach (var m in zm.PerkMachines)
            if (rules.Perk(m.Perk) == null)
                Fail($"perk machine '{m.Perk}' isn't a perk in the rules");
        if (!GameModes.Fits(GameMode.Zombies, map) || GameModes.Fits(GameMode.Zombies, Maps.All.First(m => m.Zombies == null)) || GameModes.All.Contains(GameMode.Zombies)
            || !GameModes.ForMap(map).Contains(GameMode.Zombies))
            Fail("Zombies should be pickable on Zombies maps only");
        map.CloseAllDoors(); // (other modes open them: one big map)
        var copy = MapFile.Load(MapFile.ToJson(map), "zombies round trip", map.PackDir ?? "");
        if (copy.Zombies is not { } zc || zc.Doors.Count != zm.Doors.Count || zc.Windows.Count != zm.Windows.Count || copy.ContentHash != map.ContentHash || !copy.Solid.SequenceEqual(map.Solid))
            Fail("the zombies section didn't survive a map file round trip");
        foreach (var bad in new[]
                 {
                     """{"areas": []}""", """{"areas": [{"name": "A", "rect": [0, 0, 5, 5]}], "spawners": [{"at": [1, 1]}], "doors": [{"box": [0, 0, 0, 1, 2, 1], "areas": ["A", "B"]}]}""",
                     """{"areas": [{"name": "A", "rect": [0, 0, 5, 5]}], "spawners": [{"at": [1, 1]}], "teleporter": 1}""",
                 })
            try
            {
                MapFile.Load($$"""{ "name": "ZBad", "halfX": 20, "halfZ": 20, "ops": [ { "op": "Spawn", "x": 1, "z": 1 } ], "zombies": {{bad}} }""", "bad zombies");
                Fail($"a broken zombies section loaded: {bad}");
            }
            catch (MapFileException)
            {
            }

        // ---- Window openings: bodies blocked, bullets through; doors: solid until open, the fingerprint stays ----
        World.SetMap(map);
        map.CloseAllDoors();
        var w0 = zm.Windows[0];
        var through = World.Query.Raycast(w0.Outside + new Vector3(0, 1.5f, 0), Vector3.Normalize(w0.Inside - w0.Outside), Vector3.Distance(w0.Inside, w0.Outside), QueryFilter.Shots);
        var blockedBody = World.Blocked(w0.Opening.Center with { Y = w0.Opening.Min.Y + 0.05f }, 1.0f);
        var hash = map.ContentHash;
        var doorCenter = zm.Doors[0].Box.Center;
        var shut = World.Blocked(doorCenter with { Y = 0.05f });
        map.SetDoorOpen(0, true);
        var openNow = !World.Blocked(doorCenter with { Y = 0.05f });
        var hashOpen = map.ContentHash;
        map.CloseAllDoors();
        var shutAgain = World.Blocked(doorCenter with { Y = 0.05f });
        ZLine($"window opening: bullets through {through >= Vector3.Distance(w0.Inside, w0.Outside) - 0.01f}, a body blocked {blockedBody}; door: shut {shut}, open {openNow}, shut again {shutAgain}, fingerprint kept {hash == hashOpen}");
        if (through < Vector3.Distance(w0.Inside, w0.Outside) - 0.01f || !blockedBody)
            Fail("window openings should block bodies but not bullets");
        if (!shut || !openNow || !shutAgain || hash != hashOpen)
            Fail("doors should be solid until opened, then passable, and opening one mustn't change the map fingerprint");
        if (map.IsDrawnBox(zm.Doors[0].SolidIndex) || map.IsDrawnBox(w0.SolidIndex))
            Fail("Zombies doors and windows are drawn as plain map boxes");

        var now = 0.0;
        CheckZombieRuns(map, zm, ref now);
    }

    private static void CheckZombieRuns(Map map, ZombiesMap zm, ref double now)
    {
        // ---- Spawners only in unlocked areas; the alive cap and the queue ----
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 0.5f; r.SpawnDelay = 0.1f; r.SpawnDelayMin = 0.05f; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var seenAreas = new HashSet<int>();
            var seen = new HashSet<ushort>();
            void Watch()
            {
                foreach (var z in g.Horde)
                    if (seen.Add(z.Id))
                        seenAreas.Add(z.Area);
            }
            for (var i = 0; i < 60 * 6; i++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                Watch();
            }
            var startOnly = seenAreas.SetEquals([zm.StartArea]);
            g.OpenDoor(0, null); // the Hall
            foreach (var z in g.Horde)
                g.DamageZombie(me, z, 100000, false, false, now);
            g.SelfTestBeginRound(9, now);
            var maxAlive = 0;
            for (var i = 0; i < 60 * 12; i++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                Watch();
                maxAlive = Math.Max(maxAlive, g.AliveZombies);
            }
            var hall = zm.AreaIndex("Hall");
            ZLine($"spawners: closed doors -> areas {string.Join(",", seenAreas.Select(a => zm.Areas[a].Name))}; {seen.Count} spawned; alive at most {maxAlive} (cap {g.ZRules.MaxAlive}), {g.SelfTestToSpawn} still queued in round 9");
            if (!startOnly)
                Fail("with every door shut, zombies came from outside the start area");
            if (!seenAreas.Contains(hall) || seenAreas.Any(a => !g.AreaOpen(a)))
                Fail("opening the Hall door should bring its spawners in (and only unlocked areas spawn)");
            if (maxAlive > g.ZRules.MaxAlive || g.SelfTestToSpawn <= 0)
                Fail("the alive cap or the spawn queue doesn't work");
        }

        // ---- Doors cost points and unlock; wall buys and ammo; the box and the teddy bear; perks and power ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            var zp = g.ZP(me);
            var start = zp.Points;
            var door = zm.Doors[0];
            me.Teleport(door.SideA);
            zp.Points = 100;
            g.SelfTestUse(me, now);
            var refused = !map.IsDoorOpen(0) && zp.Points == 100;
            zp.Points = 2000;
            g.SelfTestUse(me, now);
            var opened = map.IsDoorOpen(0) && zp.Points == 2000 - door.Cost && g.AreaOpen(door.AreaB);
            ZLine($"door: start points {start}, refused with 100 {refused}, opened with 2000 -> {zp.Points} (cost {door.Cost}), {zm.Areas[door.AreaB].Name} unlocked {g.AreaOpen(door.AreaB)}");
            if (start != g.ZRules.StartPoints || !refused || !opened)
                Fail("a door should cost its price, refuse when you can't pay, and unlock the area behind it");

            // Wall buy: the Wall SMG (a new gun in your hands), then ammo for the Wall Rifle you start with.
            var smg = zm.WallBuys.First(w => w.Build == "Wall SMG");
            me.Weapon = me.Secondary; // the pistol in hand: the SMG replaces it, the rifle stays
            g.SwitchTo(1, now);
            me.Teleport(smg.At with { Y = 0 } - World.FlatForward(smg.Yaw) * -0.7f);
            zp.Points = 3000;
            var smgPrompt = g.FindZUse(me);
            g.SelfTestUse(me, now);
            var smgSlot = Game.OwnsBuild(me, "Wall SMG");
            var afterSmg = zp.Points;
            var inHand = g.Active.Def.Name;
            var rifle = zm.WallBuys.First(w => w.Build == "Wall Rifle");
            var rifleSlot = Game.OwnsBuild(me, "Wall Rifle");
            if (rifleSlot >= 0)
            {
                g.Slots[rifleSlot].Reserve = 0;
                g.Slots[rifleSlot].Ammo = 1;
            }
            me.Teleport(rifle.At with { Y = 0 } - World.FlatForward(rifle.Yaw) * -0.7f);
            var prompt = g.FindZUse(me);
            g.SelfTestUse(me, now);
            var ammoPaid = afterSmg - zp.Points;
            var refilled = rifleSlot >= 0 && g.Slots[rifleSlot].Reserve > 0 && g.Slots[rifleSlot].Ammo == g.Slots[rifleSlot].Def.Mag;
            ZLine($"wall buy: Wall SMG in slot {smgSlot + 1} for {3000 - afterSmg}; Wall Rifle owned (slot {rifleSlot + 1}): '{prompt?.Text}', paid {ammoPaid}, refilled {refilled}; in hand {inHand}");
            if (smgSlot < 0 || 3000 - afterSmg != g.WallCost(smg) || inHand != "Wall SMG")
                Fail($"buying a wall gun should cost its price and put it in your hands (slot {smgSlot}, paid {3000 - afterSmg}, in hand {inHand}, prompt there '{smgPrompt?.Text}')");
            if (prompt?.Kind != ZUseKind.WallAmmo || ammoPaid != g.WallAmmoCost(rifle) || ammoPaid >= g.WallCost(rifle) || !refilled)
                Fail($"a wall gun you already have should sell ammo (cheaper) and refill it ('{prompt?.Text}', paid {ammoPaid}, refilled {refilled})");

            // The box: odds follow DropTables.json's boxTiers.
            var tables = RunTables.Current;
            foreach (var round in new[] { 1, 8, 25 })
            {
                var row = RunTables.RowFor(tables.BoxTiers, round)!;
                var n = new int[6];
                const int trials = 4000;
                for (var s = 0u; s < trials; s++)
                {
                    var b = g.RollBoxBuild(null, round, s * 7919u + 13);
                    if (b >= 0)
                        n[(int)StandardBuilds.All[b].Tier]++;
                }
                var total = row.Weights.Sum();
                var worst = Enumerable.Range(0, 6).Max(i => MathF.Abs(n[i] / (float)trials - row.Weights[i] / total));
                ZLine($"box odds round {round}: {string.Join(" ", Enumerable.Range(0, 6).Select(i => $"{(Rarity)i} {n[i] * 100f / trials:0.0}%/{row.Weights[i] * 100 / total:0}%"))} (worst off {worst * 100:0.0}%)");
                if (worst > 0.04f)
                    Fail($"the mystery box's tiers don't follow DropTables.json in round {round}");
            }
            var spot0 = zm.BoxSpots[g.BoxSpot];
            me.Teleport(spot0.At + new Vector3(0, 0, -1.3f));
            zp.Points = 5000;
            g.SelfTestUse(me, now);
            var spinning = g.BoxState == ZBoxState.Spinning && zp.Points == 5000 - g.BoxCost(zm);
            ZTicks(g, ref now, g.ZRules.BoxSpinSeconds + 0.1);
            var offered = g.BoxState == ZBoxState.Offering ? g.BoxBuild : -1;
            g.SelfTestUse(me, now);
            var took = offered >= 0 && (StandardBuilds.All[offered].IsMelee ? me.MeleeBuild?.Name == StandardBuilds.All[offered].Name || me.MeleeBuild != null : Game.OwnsBuild(me, StandardBuilds.All[offered].Name) >= 0);
            // The teddy bear: this spin moves the box (and refunds it).
            var oldSpot = g.BoxSpot;
            g.SelfTestBoxMoveAfter = 0;
            var before = zp.Points;
            g.SelfTestUse(me, now);
            ZTicks(g, ref now, g.ZRules.BoxSpinSeconds + 0.1);
            var teddy = g.BoxState == ZBoxState.Teddy && zp.Points == before;
            ZTicks(g, ref now, g.ZRules.BoxMoveSeconds + 0.1);
            ZLine($"box: spin {spinning}, offered {(offered >= 0 ? StandardBuilds.All[offered].Name : "nothing")}, taken {took}; teddy {teddy} (refunded), moved from spot {oldSpot} to {g.BoxSpot}, {g.BoxState}");
            if (!spinning || !took || !teddy || g.BoxSpot == oldSpot || g.BoxState != ZBoxState.Idle)
                Fail("the mystery box should spin for its price, offer a build to take, and the teddy bear should refund and move it");

            // Perks need the power; Juggernog cuts zombie damage; going down loses every perk.
            var jugg = zm.PerkMachines.First(m => m.Perk == "jugg");
            me.Teleport(jugg.At + World.FlatForward(jugg.Yaw) * 1.0f);
            zp.Points = 10000;
            g.SelfTestUse(me, now);
            var noPower = zp.Perks == 0 && zp.Points == 10000;
            me.Teleport(zm.Power!.At + World.FlatForward(zm.Power.Yaw) * 0.8f);
            g.SelfTestUse(me, now);
            var power = g.ZPower;
            me.Teleport(jugg.At + World.FlatForward(jugg.Yaw) * 1.0f);
            g.SelfTestUse(me, now);
            var k = g.ZRules.PerkIndex("jugg");
            var hasJugg = zp.Has(k) && zp.Points == 10000 - g.ZRules.Perks[k].Cost;
            me.Health = World.MaxHealth;
            me.ImmuneUntil = 0;
            g.ZombieHurt(me, g.ZRules.ZombieDamage, now);
            var juggHit = World.MaxHealth - me.Health;
            g.SelfTestDown(me, now);
            ZLine($"perks: refused without power {noPower}, power on {power}, Juggernog {hasJugg}; a zombie hit does {juggHit} with it ({g.ZRules.ZombieDamage} without); down -> perks {zp.Perks}");
            if (!noPower || !power || !hasJugg || juggHit >= g.ZRules.ZombieDamage || zp.Perks != 0)
                Fail("perk machines should need the power, Juggernog should cut zombie damage, and going down should lose every perk");
            // Speed Cola and Double Tap change the reload and the fire rate.
            zp.Downed = false;
            zp.Perks = (byte)((1 << g.ZRules.PerkIndex("speed")) | (1 << g.ZRules.PerkIndex("doubletap")));
            if (g.ZombieReloadMult(me) >= 1 || g.ZombieFireIntervalMult(me) >= 1)
                Fail("Speed Cola / Double Tap don't change reload / fire rate");
            zp.Perks = 0;

            // The slot upgrade: 2 -> 4 slots for its price.
            me.Teleport(zm.SlotUpgrade!.At + World.FlatForward(zm.SlotUpgrade.Yaw) * 0.9f - new Vector3(0.6f, 0, 0));
            zp.Points = 5000;
            var su = g.FindZUse(me);
            g.SelfTestUse(me, now);
            ZLine($"slot upgrade: '{su?.Text}' -> {(me.ExtraSlots ? 4 : 2)} slots, paid {5000 - zp.Points}");
            if (!me.ExtraSlots || 5000 - zp.Points != g.SlotCost(zm))
                Fail("the weapon-slot upgrade should cost its price and give slots 3-4");
        }

        // ---- Boards: torn off by a zombie, put back by a player for points ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            me.Teleport(new Vector3(6, 0, 3));
            var sp = zm.Spawners.FindIndex(s => s.Window == 0);
            var z = g.SelfTestSpawnZombie(sp, now);
            var full = g.ZBoards[0];
            var minBoards = (int)full;
            for (var i = 0; i < 60 * 20 && z.State != ZState.Climbing && z.State != ZState.Chase; i++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                minBoards = Math.Min(minBoards, g.ZBoards[0]);
            }
            var climbed = z.State is ZState.Climbing or ZState.Chase;
            g.DamageZombie(me, z, 100000, false, false, now);
            var points = g.ZP(me).Points;
            me.Teleport(zm.Windows[0].Inside);
            g.ServerSetUse(me, true, now);
            ZTicks(g, ref now, g.ZRules.RepairSeconds * full + 1);
            g.ServerSetUse(me, false, now);
            var repaired = g.ZBoards[0];
            var earned = g.ZP(me).Points - points;
            ZLine($"boards: {full} -> {minBoards} (zombie climbed in {climbed}); rebuilt to {repaired} for {earned} points ({g.ZRules.RepairPoints} a board)");
            if (minBoards > 0 || !climbed || repaired != full || earned != g.ZRules.RepairPoints * full)
                Fail("a zombie should tear every board off and climb in, and a player should rebuild them for points");
        }

        // ---- Points per action ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            var zp = g.ZP(me);
            var tk = now;
            int Kill(bool head, bool melee)
            {
                var z = g.SelfTestSpawnZombie(0, tk);
                var p0 = zp.Points;
                g.DamageZombie(me, z, 1, false, false, tk); // a hit
                var hit = zp.Points - p0;
                g.DamageZombie(me, z, 100000, head, melee, tk);
                return hit * 10000 + (zp.Points - p0 - hit);
            }
            var body = Kill(false, false);
            var head = Kill(true, false);
            var knife = Kill(false, true);
            ZLine($"points: hit {body / 10000}, kill {body % 10000}, headshot kill {head % 10000}, melee kill {knife % 10000}; zombie kills {zp.Kills}, scrap {zp.Scrap}");
            if (body / 10000 != g.ZRules.HitPoints || body % 10000 != g.ZRules.KillPoints || head % 10000 != g.ZRules.HeadshotKillPoints || knife % 10000 != g.ZRules.MeleeKillPoints || zp.Kills != 3)
                Fail("points per hit / kill / headshot kill / melee kill don't match the rules");

            // A shot through the normal path at a zombie in the room: one damage event, a hit's points.
            var z2 = g.SelfTestSpawnZombie(0, now);
            me.Teleport(new Vector3(5, 0, -3));
            z2.Sim.Position = new Vector3(5, 0, 2);
            for (var i = 0; i < z2.History.Length; i++)
                z2.History[i] = z2.Sim.Position;
            var p1 = zp.Points;
            g.SelfTestServerShot(me, me.Eye, Vector3.Normalize(z2.Position + new Vector3(0, 1.0f, 0) - me.Eye), now);
            ZLine($"a shot at a zombie: health {z2.Health}/{z2.MaxHealth}, points +{zp.Points - p1}");
            if (z2.Health >= z2.MaxHealth || zp.Points - p1 != g.ZRules.HitPoints)
                Fail("a bullet didn't hit a zombie through the server's shot path");

            // Part drops: rolled from DropTables.json; walking over one picks it up.
            for (var i = 0; i < 400; i++)
                g.SelfTestRollDrop(me.Position + new Vector3(3, 0, 0), 30, now);
            var drops = g.ZDrops.Count;
            me.Teleport(me.Position + new Vector3(3, 0, 0));
            g.SelfTestZombiesTick(now);
            var picked = zp.Parts.Values.Sum();
            ZLine($"part drops: {drops} of 400 rolls at round 30 (chance {RunTables.RowFor(RunTables.Current.DropChance, 30)?.Chance:0.00}), picked up {picked}");
            if (drops is < 15 or > 80 || picked < 1)
                Fail("part drops don't follow the drop chance, or can't be picked up");
        }

        // ---- Revive, bleeding out, game over ----
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.BleedoutSeconds = 5; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            g.SelfTestBeginRound(1, now);
            g.SelfTestDown(mate, now);
            var down = g.IsDowned(mate) && mate.Alive;
            me.Teleport(mate.Position + new Vector3(0.8f, 0, 0));
            var use = g.FindZUse(me);
            g.ServerSetUse(me, true, now);
            ZTicks(g, ref now, g.ZRules.ReviveSeconds + 0.3);
            g.ServerSetUse(me, false, now);
            var revived = !g.IsDowned(mate) && mate.Alive;
            g.SelfTestDown(mate, now);
            me.Teleport(new Vector3(6, 0, 3));
            ZTicks(g, ref now, 5.5);
            var bledOut = !mate.Alive;
            var playing = g.Phase == MatchPhase.Playing;
            me.ImmuneUntil = 0;
            g.SelfTestDown(me, now);
            ZTicks(g, ref now, 0.2);
            ZLine($"revive: downed {down}, prompt '{use?.Text}', revived {revived}; bled out {bledOut} (run goes on {playing}); last one down -> {g.Phase}: '{g.MatchResult}'");
            if (!down || use?.Kind != ZUseKind.Revive || !revived || !bledOut || !playing)
                Fail("downed players should be revivable with the interact key and bleed out after the bleedout time");
            if (g.Phase != MatchPhase.Ended || !g.MatchResult.StartsWith("Game over", StringComparison.Ordinal))
                Fail("the run should end when nobody is left standing");
        }
        // Second Wind (the self-revive perk, start room): bought, down -> back up on your own after the delay, used up,
        // bought again; solo and co-op. Quick Revive alone no longer gets you up.
        foreach (var coop in new[] { false, true })
            using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
            {
                var me = g.Local!;
                var zp = g.ZP(me);
                Player? mate = null;
                if (coop)
                {
                    mate = g.SelfTestAddHuman();
                    g.SelfTestSpawn(mate);
                    mate.Teleport(new Vector3(-5, 0, -1));
                    mate.ImmuneUntil = 1e9;
                }
                me.ImmuneUntil = 1e9;
                var k = g.ZRules.PerkIndex("secondwind");
                var machine = zm.PerkMachines.FirstOrDefault(m => m.Perk == "secondwind");
                if (machine == null || zm.Areas[zm.StartArea].Contains(machine.At) != true || machine.NeedsPower)
                {
                    Fail("Zombies Test should have a Second Wind machine in the start room that works without the power");
                    break;
                }
                var stand = machine.At + World.FlatForward(machine.Yaw) * 1.0f;
                var cost = g.ZRules.Perks[k].Price(!coop);
                var results = new List<string>();
                var ok = true;
                for (var buy = 0; buy < 2; buy++)
                {
                    me.Teleport(stand);
                    zp.Points = 5000;
                    var prompt = g.FindZUse(me);
                    g.SelfTestUse(me, now);
                    var bought = zp.Has(k) && zp.Points == 5000 - cost;
                    g.SelfTestDown(me, now);
                    var downed = g.IsDowned(me);
                    var used = !zp.Has(k);
                    ZTicks(g, ref now, g.ZRules.SelfReviveSeconds - 0.5);
                    var stillDown = g.IsDowned(me);
                    ZTicks(g, ref now, 0.8);
                    var up = !g.IsDowned(me) && me.Alive && g.Phase == MatchPhase.Playing;
                    results.Add($"buy {buy + 1}: '{prompt?.Text}' bought {bought}, down {downed}, used up {used}, still down at {g.ZRules.SelfReviveSeconds - 0.5:0.0} s {stillDown}, up after {up}");
                    ok &= bought && downed && used && stillDown && up;
                }
                // Without it: down stays down (solo: the game over; Quick Revive doesn't self-revive any more).
                zp.Perks = (byte)(1 << g.ZRules.PerkIndex("revive"));
                g.SelfTestBeginRound(1, now); // (the game over needs a round on)
                g.SelfTestDown(me, now);
                ZTicks(g, ref now, g.ZRules.SelfReviveSeconds + 0.3);
                var noSelf = coop ? g.IsDowned(me) : g.Phase == MatchPhase.Ended;
                ZLine($"Second Wind ({(coop ? "co-op" : "solo")}, {cost} points): {string.Join("; ", results)}; Quick Revive alone keeps you down {noSelf}");
                if (!ok || !noSelf)
                    Fail($"Second Wind should get you back up on your own once per buy ({(coop ? "co-op" : "solo")}), and Quick Revive alone shouldn't");
            }

        CheckZombieDroppedBuilds(map, zm, ref now);

        // ---- Workbench: change a build with your own parts; scrap ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            var zp = g.ZP(me);
            me.Teleport(zm.Benches[0].At + new Vector3(0, 0, -1.2f));
            var slot0 = zp.Slots[0]!;
            var edited = slot0.Clone();
            var notOwned = g.ServerBench(me, 0, 0, Edit(edited, "Red Dot"));
            zp.Parts["Red Dot"] = 1;
            var ok = g.ServerBench(me, 0, 0, Edit(slot0.Clone(), "Red Dot"));
            var fitted = zp.Slots[0]!.Nodes.Any(n => n.Part.Name == "Red Dot") && !zp.Parts.ContainsKey("Red Dot");
            zp.Parts["Hot Load Kit"] = 2;
            var salvage = g.ServerBench(me, 1, 0, "Hot Load Kit");
            var scrap = zp.Scrap;
            zp.Scrap = 1000;
            var craftRare = g.ServerBench(me, 2, 0, "Gas Overclock"); // round 0-1 crafts common/uncommon only
            var craftCommon = g.ServerBench(me, 2, 0, "Lightweight Bolt");
            ZLine($"workbench: without the part '{notOwned}', with it {ok ?? "ok"} (fitted {fitted}); salvage {salvage ?? "ok"} -> {scrap} scrap; craft rare '{craftRare}', common {craftCommon ?? "ok"}");
            if (notOwned == null || ok != null || !fitted || salvage != null || scrap <= 0 || craftRare == null || craftCommon != null || zp.Parts.GetValueOrDefault("Lightweight Bolt") != 1)
                Fail("the workbench should only fit parts you own, salvage for scrap and craft within the round's tier");
            me.Teleport(new Vector3(-30, 0, -30));
            if (g.ServerBench(me, 1, 0, "Hot Load Kit") == null)
                Fail("the workbench worked away from a workbench");
        }

        // ---- Replication: zombies, the run, a build ----
        using (var g = ZGame(1, ref now, tweak: r => { r.FirstRoundDelay = 0.3f; r.SpawnDelay = 0.1f; r.SpawnDelayMin = 0.05f; }))
        {
            g.Local!.ImmuneUntil = 1e9;
            ZTicks(g, ref now, 4);
            g.OpenDoor(0, null);
            var w = new PacketWriter();
            g.WriteZEnts(w);
            var ents = w.Span.ToArray();
            var w2 = new PacketWriter();
            g.WriteZWorld(w2);
            var world = w2.Span.ToArray();
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(map);
            g.OpenDoor(0, null);
            c.SelfTestReadZombies(ents, world);
            var worst = 0f;
            foreach (var z in g.Horde)
                if (c.Horde.FirstOrDefault(x => x.Id == z.Id) is { } cz)
                    worst = MathF.Max(worst, Vector3.Distance(cz.Position, z.Position));
                else
                    worst = 99;
            var bot = g.Players.Values.First(p => p.Bot != null);
            var botPoints = g.ZP(bot).Points;
            g.ZP(bot).Points = botPoints;
            var w3 = new PacketWriter();
            var build = StandardBuilds.Find("Minigun")!.Build();
            var gl = g.Local!;
            g.GiveBuild(gl, 0, build, full: true);
            Game.SelfTestWriteBuild(w3, gl, 0, build);
            var applied = c.SelfTestApplyBuild(w3.Span);
            ZLine($"replication: {g.Horde.Count} zombies in {ents.Length} bytes (worst {worst * 100:0.0} cm), run state {world.Length} bytes: round {c.ZRound}, boards {string.Join(",", c.ZBoards)}, "
                 + $"points {c.ZP(bot.Id).Points}/{botPoints}; build '{c.Local?.BuiltPrimary?.Name}' applied {applied} (DPS {c.Local?.BuiltPrimary?.Dps:0} vs {gl.BuiltPrimary?.Dps:0}), protocol {ServerInfo.ProtocolVersion}");
            if (c.Horde.Count != g.Horde.Count || worst > 0.05f || c.ZRound != g.ZRound || !c.ZBoards.SequenceEqual(g.ZBoards) || c.ZP(bot.Id).Points != botPoints)
                Fail("the zombies or the run state didn't replicate");
            if (!applied || c.Local?.BuiltPrimary?.Name != "Minigun" || c.Local.BuiltPrimary.Dps != gl.BuiltPrimary!.Dps || c.Slots[0].Def.Name != "Minigun")
                Fail("a built weapon didn't replicate to a client");
            if (ents.Length > PacketWriter.Mtu || world.Length > PacketWriter.Mtu)
                Fail("a Zombies message is bigger than one datagram");
        }

        // ---- The AI cost with 24 zombies alive, chasing ----
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            for (var i = 0; i < 24; i++)
            {
                var z = g.SelfTestSpawnZombie(zm.Spawners.FindIndex(s => s.Window == 0), now);
                z.State = ZState.Chase;
                z.Window = -1;
                z.Area = zm.StartArea;
                z.Sim.Position = new Vector3(-7f + (i % 6) * 2.4f, 0, new[] { -5f, -3.6f, 2.5f, 4.5f }[i / 6]);
                z.Speed = (ZSpeed)(i % 3);
            }
            ZTicks(g, ref now, 0.5); // warm up
            g.ZAiMs = g.ZAiZombieTicks = g.ZAiTicks = 0;
            var sw = Stopwatch.StartNew();
            ZTicks(g, ref now, 4);
            var perZombie = g.ZAiMs * 1000 / Math.Max(1, g.ZAiZombieTicks);
            var perTick = g.ZAiMs / Math.Max(1, g.ZAiTicks);
            ZLine($"AI cost: {g.AliveZombies} alive, {perZombie:0.0} us per zombie per tick, {perTick:0.00} ms per tick ({sw.Elapsed.TotalMilliseconds / 240:0.00} ms per whole tick)");
            if (g.AliveZombies != 24)
                Fail("the 24 test zombies didn't stay alive (the host is immune)");
            if (perZombie > 150 || perTick > 3)
                Fail($"the zombie AI is too slow: {perZombie:0.0} us per zombie per tick");
        }

        // ---- Determinism: the same run twice ----
        string RunHash()
        {
            var t = 0.0; // the same clock both times (doubles round differently at other bases)
            using var g = ZGame(2, ref t, seed: 777, tweak: r => r.FirstRoundDelay = 1, hostIn: false);
            for (var i = 0; i < 60 * 40; i++)
            {
                t += World.TickDt;
                g.Update(t, World.TickDt);
            }
            var h = 17L;
            foreach (var z in g.Horde)
                h = h * 31 + z.Id * 7 + (long)(z.Position.X * 100) * 3 + (long)(z.Position.Z * 100);
            foreach (var p in g.Players.Values.OrderBy(p => p.Id))
                h = h * 31 + g.ZP(p).Points * 13 + g.ZP(p).Kills;
            return $"{g.ZRound}/{g.Horde.Count}/{g.Players.Values.Sum(p => g.ZP(p).Kills)}/{h:x}";
        }
        var h1 = RunHash();
        var h2 = RunHash();
        ZLine($"determinism: {h1} vs {h2}");
        if (h1 != h2)
            Fail("the same Zombies run (same seed) played out differently");

        // ---- A bots-only run survives rounds 1-3 ----
        using (var g = ZGame(3, ref now, tweak: r => r.FirstRoundDelay = 2, hostIn: false, skill: BotDifficulty.Hard))
        {
            var sw = Stopwatch.StartNew();
            var t0 = now;
            while (now - t0 < 420 && g.Phase == MatchPhase.Playing && g.ZRound < 4)
            {
                now += World.TickDt;
                g.Update(now, World.TickDt);
            }
            var bots = g.Players.Values.Where(p => p.Bot != null).ToList();
            ZLine($"bots-only run: round {g.ZRound} after {now - t0:0} s ({sw.Elapsed.TotalSeconds:0.0} s to simulate), {g.Phase}, kills {string.Join("/", bots.Select(b => g.ZP(b).Kills))}, "
                 + $"downs {bots.Sum(b => g.ZP(b).Downs)}, points {string.Join("/", bots.Select(b => g.ZP(b).Points))}, boards {string.Join(",", g.ZBoards)}");
            if (g.ZRound < 4 || g.Phase != MatchPhase.Playing)
                Fail($"three bots didn't survive rounds 1-3 on Zombies Test ({g.ZLastResult}; kills {string.Join("/", bots.Select(b => g.ZP(b).Kills))}, downs {bots.Sum(b => g.ZP(b).Downs)})");
        }

        // ---- Player-visible text is plain ASCII ----
        var texts = new List<string> { GameModes.Name(GameMode.Zombies), GameModes.Rules(GameMode.Zombies), Ui.RoundTally(3), Ui.RoundTally(12) };
        texts.AddRange(ZombiesRules.Current.Perks.SelectMany(p => new[] { p.Name, p.Blurb }));
        texts.AddRange(zm.Doors.Select(d => d.Name));
        texts.AddRange(Enumerable.Range(0, (int)ZPowerKind.COUNT).Select(k => Game.PowerName((ZPowerKind)k)));
        foreach (var s in texts)
            if (s.Any(ch => ch < 32 || ch > 126))
                Fail($"Zombies text isn't plain ASCII: {s}");

        CheckZombiesMilestone3(ref now); // SelfTestZombiesMap.cs: Blackmoor Asylum, machine props, power-ups, special ammo
    }

    /// <summary>
    /// Dropped builds (ZombiesDrops.cs): bleeding out drops every build (not the untouched start builds) at the spot you
    /// went down, spread out and never inside geometry; a teammate picks one up (swapping out the gun in hand, which drops
    /// with the same time left), a free slot takes one without a swap, the melee build goes to the melee slot, and they all
    /// despawn exactly 300 s after the drop. Plus the message round trip and determinism.
    /// </summary>
    private static void CheckZombieDroppedBuilds(Map map, ZombiesMap zm, ref double now)
    {
        var deathAt = new Vector3(2, 0, -3); // a spawn point in the start room

        // A teammate with the Minigun (slot 1), the Auto Shotgun (slot 3) and the Fire Axe (melee) bleeds out there.
        Game BleedOut(ref double t, out Player mate, out int pointsBefore)
        {
            var g = ZGame(0, ref t, tweak: r => { r.FirstRoundDelay = 9999; r.BleedoutSeconds = 5; });
            g.Local!.ImmuneUntil = 1e9;
            g.Local.Teleport(new Vector3(6, 0, 3));
            mate = g.SelfTestAddHuman();
            g.SelfTestSpawn(mate);
            mate.ExtraSlots = true;
            g.GiveBuild(mate, 0, StandardBuilds.Find("Minigun")!.Build(), full: true);
            g.GiveBuild(mate, 2, StandardBuilds.Find("Auto Shotgun")!.Build(), full: true);
            g.GiveBuild(mate, 4, StandardBuilds.Find("Fire Axe")!.Build(), full: true);
            g.ZP(mate).Points = 1234;
            pointsBefore = 1234;
            mate.Teleport(deathAt);
            g.SelfTestDown(mate, t);
            ZTicks(g, ref t, g.ZRules.BleedoutSeconds + 0.5);
            return g;
        }

        using (var g = BleedOut(ref now, out var mate, out var before))
        {
            var me = g.Local!;
            var drops = g.ZWeaponDrops.ToList();
            var names = string.Join(", ", drops.Select(d => d.Build.Name).OrderBy(n => n));
            var tDrop = drops.Count > 0 ? drops[0].Until - g.ZRules.BuildDropSeconds : now;
            var spread = drops.Count < 2 ? 9f : drops.SelectMany((a, i) => drops.Skip(i + 1).Select(b => Vector3.Distance(a.At, b.At))).Min();
            var far = drops.Count == 0 ? 0 : drops.Max(d => Vector3.Distance(d.At, deathAt));
            var solid = drops.Count(d => World.Blocked(d.At + new Vector3(0, 0.02f, 0)) || !World.CanWalkStraight(deathAt, d.At) || MathF.Abs(d.At.Y - deathAt.Y) > 0.05f);
            var lost = before - g.ZP(mate).Points;
            ZLine($"dropped builds: bled out {!mate.Alive}, dropped [{names}] (start pistol kept out), {g.ZRules.BuildDropSeconds:0} s each, spread {spread:0.00} m apart, at most {far:0.00} m from the spot, {solid} in geometry; points lost {lost} of {before}");
            if (mate.Alive || names != "Auto Shotgun, Fire Axe, Minigun" || drops.Any(d => Math.Abs(d.Until - (tDrop + g.ZRules.BuildDropSeconds)) > 1e-6) || !drops.Single(d => d.Build.Name == "Fire Axe").Melee)
                Fail($"bleeding out should drop every build but the start ones where you went down, for {g.ZRules.BuildDropSeconds} s ({names})");
            if (spread < 0.45f || far > 1.2f || solid > 0)
                Fail("dropped builds should be spread out around the spot, on the ground, never in geometry");
            if (lost != (int)(before * g.ZRules.BleedoutPointLoss) || g.ZRules.BuildDropSeconds != 300 || !g.ZRules.LosePerksOnDown)
                Fail("bleeding out should still cost 10 percent of your points, and the rules file should say 300 s and perks lost on down");

            // A teammate picks up the Minigun with both slots full: it swaps with the Wall Rifle in hand, which drops there with the same time left.
            me.Weapon = me.Primary;
            var mini = drops.Single(d => d.Build.Name == "Minigun");
            me.Teleport(mini.At);
            var prompt = g.FindZUse(me);
            g.SelfTestUse(me, now);
            var left = g.ZWeaponDrops.FirstOrDefault(d => d.Build.Name == "Wall Rifle");
            var swapped = Game.OwnsBuild(me, "Minigun") == 0 && g.ZP(me).Slots[0]?.Name == "Minigun" && g.Active.Def.Name == "Minigun" && left != null
                          && Vector3.Distance(left.At, mini.At) < 0.01f && left.Until == mini.Until && !left.Full && g.ZWeaponDrops.Count == 3 && !g.ZWeaponDrops.Contains(mini);
            ZLine($"  teammate pickup: '{prompt?.Text}' -> swapped {swapped} (Wall Rifle left there with {(left != null ? left.Until - now : 0):0} s, not a fresh timer)");
            if (prompt?.Kind != ZUseKind.Pickup || !prompt.Value.Text.Contains("Minigun") || !prompt.Value.Text.Contains("swap for your Wall Rifle") || !swapped)
                Fail("a teammate should pick up a dropped build, swapping it with the gun in hand (which drops with the remaining time)");

            // A free slot (slots 3-4 bought): the Auto Shotgun goes there, nothing drops.
            me.ExtraSlots = true;
            var auto = g.ZWeaponDrops.Single(d => d.Build.Name == "Auto Shotgun");
            me.Teleport(auto.At);
            g.SelfTestUse(me, now);
            var free = Game.OwnsBuild(me, "Auto Shotgun") == 2 && Game.OwnsBuild(me, "Minigun") == 0 && g.ZWeaponDrops.Count == 2;
            ZLine($"  free slot: Auto Shotgun into slot {Game.OwnsBuild(me, "Auto Shotgun") + 1}, {g.ZWeaponDrops.Count} left on the floor");
            if (!free)
                Fail("a dropped build should go into a free slot without a swap");

            // Back next round, the owner picks up their own Fire Axe: the melee slot, the Machete drops in its place.
            g.SelfTestSpawn(mate);
            var axe = g.ZWeaponDrops.Single(d => d.Build.Name == "Fire Axe");
            mate.Teleport(axe.At);
            var own = g.FindZUse(mate);
            var gunsBefore = $"{mate.BuiltPrimary?.Name}/{mate.BuiltSecondary?.Name}";
            g.SelfTestUse(mate, now);
            var machete = g.ZWeaponDrops.FirstOrDefault(d => d.Build.Name == "Machete");
            var melee = mate.MeleeBuild?.Name == "Fire Axe" && g.ZP(mate).Slots[4]?.Name == "Fire Axe" && machete is { Melee: true } && machete.Until == axe.Until
                        && $"{mate.BuiltPrimary?.Name}/{mate.BuiltSecondary?.Name}" == gunsBefore;
            ZLine($"  own pickup: '{own?.Text}' -> melee slot {mate.MeleeBuild?.Name}, guns kept {gunsBefore}, Machete on the floor {machete != null}");
            if (own?.Text.Contains("your Fire Axe") != true || !melee)
                Fail("the owner should pick up their own melee build into the melee slot (the old one swapped out, the guns untouched)");

            // Replication: a client's copy reads every drop and the removals.
            var msgs = new List<byte[]>();
            foreach (var d in g.ZWeaponDrops)
            {
                var w = new PacketWriter();
                Game.WriteWeaponDrop(w, d, d.Until - now);
                msgs.Add(w.Span.ToArray());
            }
            var serverDrops = g.ZWeaponDrops.ToList();
            var snow = now; // (a ref parameter can't go into the lambda below)
            using (var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999))
            {
                World.SetMap(map);
                var cnow = 5000.0;
                foreach (var m in msgs)
                    c.SelfTestReadWeaponDrop(m, cnow);
                var same = c.ZWeaponDrops.Count == serverDrops.Count && serverDrops.All(s => c.ZWeaponDrops.FirstOrDefault(x => x.Id == s.Id) is { } cd && cd.At == s.At && cd.Build.ToText() == s.Build.ToText()
                    && cd.Owner == s.Owner && cd.OwnerName == s.OwnerName && cd.Full == s.Full && cd.Melee == s.Melee && Math.Abs((cd.Until - cnow) - (s.Until - snow)) < 0.01);
                var prompted = false;
                if (c.ZWeaponDrops.Count > 0 && c.Local is { } cl)
                {
                    cl.Teleport(c.ZWeaponDrops[0].At);
                    prompted = c.FindZUse(cl)?.Kind == ZUseKind.Pickup;
                }
                var gw = new PacketWriter();
                Game.WriteWeaponDropGone(gw, serverDrops[0].Id, me.Id);
                c.SelfTestReadWeaponDrop(gw.Span, cnow);
                var gone = c.ZWeaponDrops.Count == serverDrops.Count - 1 && c.ZWeaponDrops.All(x => x.Id != serverDrops[0].Id);
                ZLine($"  replication: {msgs.Count} drop messages ({string.Join("/", msgs.Select(m => m.Length))} bytes), same on the client {same}, client prompt {prompted}, removal {gone}, protocol {ServerInfo.ProtocolVersion}");
                if (!same || !prompted || !gone)
                    Fail("dropped builds didn't replicate to a client (appear, prompt, go)");
            }
            World.SetMap(map);

            // Despawn: exactly 300 s after the drop (simulated time), the swapped ones too (they kept the timer).
            var ids = g.ZWeaponDrops.Count;
            var until = mini.Until; // = the bleed-out time + 300 s (every drop shares it: the swapped ones kept it)
            var exact = Math.Abs(until - tDrop - 300) < 1e-6 && g.ZWeaponDrops.All(d => d.Until == until);
            g.SelfTestZombiesTick(until - World.TickDt);
            var stillThere = g.ZWeaponDrops.Count;
            g.SelfTestZombiesTick(until);
            var after = g.ZWeaponDrops.Count;
            now = Math.Max(now, until);
            ZLine($"  despawn: {stillThere} of {ids} still there one tick before {g.ZRules.BuildDropSeconds:0} s, {after} at {g.ZRules.BuildDropSeconds:0} s");
            if (ids == 0 || !exact || stillThere != ids || after != 0)
                Fail("dropped builds should despawn exactly 300 s after the drop (and not before)");
        }

        // Never inside geometry: drops at every spawn and at every area's corners (where a player can stand), 5 at a time.
        {
            using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            World.SetMap(map);
            var bases = new List<Vector3>(map.Spawns);
            foreach (var a in zm.Areas)
                foreach (var (x, z) in new[] { (a.X0 + 0.45f, a.Z0 + 0.45f), (a.X1 - 0.45f, a.Z0 + 0.45f), (a.X0 + 0.45f, a.Z1 - 0.45f), (a.X1 - 0.45f, a.Z1 - 0.45f) })
                    bases.Add(new Vector3(x, 0, z));
            int tested = 0, bad = 0, stacked = 0;
            foreach (var b in bases)
            {
                if (World.Blocked(b + new Vector3(0, 0.02f, 0)))
                    continue; // nobody can go down there
                tested++;
                var area = zm.Areas.FindIndex(a => a.Contains(b));
                var spots = Game.DropSpots(b, 5, tested);
                foreach (var s in spots)
                {
                    if (Vector3.Distance(s, b) < 0.01f)
                        stacked++;
                    if (World.Blocked(s + new Vector3(0, 0.02f, 0)) || MathF.Abs(s.X) > map.HalfX || MathF.Abs(s.Z) > map.HalfZ || (area >= 0 && !zm.Areas[area].Contains(s)) || !World.CanWalkStraight(b, s) && Vector3.Distance(s, b) > 0.01f)
                    {
                        bad++;
                        Fail($"a dropped build went into geometry or out of its area: {s} from {b}");
                    }
                }
            }
            ZLine($"  placement: {tested} spots (spawns and area corners) x 5 drops: {bad} in geometry / out of the area, {stacked} on the spot itself (no room)");
            if (tested < 8)
                Fail("too few spots to test dropped-build placement on Zombies Test");
        }

        // Determinism: the same bleed-out twice drops the same builds at the same spots with the same ids.
        string DropHash()
        {
            var t = 0.0;
            using var g = BleedOut(ref t, out _, out _);
            return string.Join(";", g.ZWeaponDrops.Select(d => $"{d.Id}:{d.Build.Name}@{d.At.X:0.000},{d.At.Y:0.000},{d.At.Z:0.000}/{d.Until:0.000}"));
        }
        var h1 = DropHash();
        var h2 = DropHash();
        ZLine($"  determinism: {(h1 == h2 ? "same" : "DIFFERENT")} ({h1.Length} chars)");
        if (h1 != h2 || h1.Length == 0)
            Fail($"the same bleed-out dropped builds differently: {h1} vs {h2}");
    }

    /// <summary>The build with <paramref name="part"/> on its grip's optic socket.</summary>
    private static string Edit(WeaponBuild b, string part)
    {
        var p = Parts.Find(part)!;
        var socket = b.Root!.Part.Sockets.First(s => s.Type == p.Mount);
        b.Root.Attach(socket.Name, p);
        return b.ToText();
    }
}
