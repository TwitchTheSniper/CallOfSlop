using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Zombies milestone 3 (part of the "Zombies mode" section): the first purpose-built map, Blackmoor Asylum (it loads, it's in
/// a normal pack, every area is reachable through its doors, the spawners and windows are sound, the machines are solid
/// props, zombies get in through every window and through every door without sticking), the power-ups (they drop with
/// a per-round cap, apply, expire and replicate) and the special ammo effects (each does its damage, none stacks), plus
/// the AI cost on the new map within the same budget. Short scripted scenarios only.
/// </summary>
public static partial class SelfTest
{
    private const string BlackmoorName = "Blackmoor Asylum";

    private static float ZFlat(Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    private static void CheckZombiesMilestone3(ref double now)
    {
        if (Maps.Find(BlackmoorName) is not { Zombies: { } zm } map)
        {
            Fail($"Maps/Zombies/01-BlackmoorAsylum.json ('{BlackmoorName}') is missing or has no \"zombies\" section");
            return;
        }
        CheckBlackmoorLayout(map, zm);
        CheckBlackmoorZombies(map, zm, ref now);
        CheckZombiePowerUps(ref now);
        CheckZombieAmmoEffects(ref now);
    }

    // ---- The map: loads, validates, areas through doors, spawners, machines ------------------------------------

    private static void CheckBlackmoorLayout(Map map, ZombiesMap zm)
    {
        World.SetMap(map);
        map.CloseAllDoors();
        var rules = ZombiesRules.Current;
        var unlockable = zm.Areas.Count - 1;
        var groundSpawners = zm.Spawners.Count(s => s.Window < 0);
        var powerPerks = zm.PerkMachines.Count(m => m.NeedsPower);
        var powerDoors = zm.Doors.Count(d => d.NeedsPower);
        var buyAreas = zm.WallBuys.Select(w => w.Area).Distinct().Count();
        var costs = zm.Doors.Where(d => !d.NeedsPower).Select(d => d.Cost).ToList();
        ZLine($"{map.Name} ({map.Pack?.Name} pack): {zm.Areas.Count} areas ({unlockable} to unlock), {zm.Doors.Count} doors ({string.Join("/", zm.Doors.Select(d => d.Cost))}), {zm.Windows.Count} windows, "
             + $"{zm.Spawners.Count} spawners ({groundSpawners} in the ground), {zm.BoxSpots.Count} box spots, {zm.WallBuys.Count} wall buys in {buyAreas} areas, {zm.PerkMachines.Count} perks ({powerPerks} need power), "
             + $"{zm.Benches.Count} workbenches, {zm.Salvage.Count} salvage, slot upgrade {zm.SlotUpgrade != null}, power {zm.Power != null}, {map.PropInstances.Count} props, {map.Solid.Count} boxes, "
             + $"boundary {map.Boundary != null}, terrain {map.HasTerrain}, sky {map.Skybox != null}");
        if (map.Pack is not { } pack || pack.Name == "Dev" || !GameModes.Fits(GameMode.Zombies, map) || !GameModes.ForMap(map).Contains(GameMode.Zombies))
            Fail($"{map.Name} should be in a normal (non-Dev) pack and offer the Zombies mode");
        if (unlockable is < 5 or > 7 || zm.Windows.Count < 12 || zm.BoxSpots.Count < 4 || zm.WallBuys.Count < 5 || buyAreas < 4 || zm.PerkMachines.Count < 5
            || powerPerks < 2 || powerDoors < 1 || zm.Benches.Count != 2 || zm.Salvage.Count < 3 || zm.SlotUpgrade == null || zm.Power == null || groundSpawners < 2)
            Fail($"{map.Name} is missing part of a full Zombies map (5-7 areas to unlock, plenty of windows, box spots, wall buys, five perks with some on the power, a shutter on the power, two workbenches, salvage, the slot upgrade, ground spawners)");
        if (costs.Count < 2 || costs.Max() <= costs.Min())
            Fail($"{map.Name}: door prices should rise ({string.Join("/", costs)})");
        if (map.Boundary == null || !map.HasTerrain || map.Skybox == null || map.PropInstances.Count < 60)
            Fail($"{map.Name} should have a shaped outline, terrain, a sky and real dressing (props)");
        foreach (var wb in zm.WallBuys)
            if (StandardBuilds.Find(wb.Build) is not { } sb || !sb.Sources.Contains("wall"))
                Fail($"{map.Name}: wall buy '{wb.Build}' isn't a standard build tagged \"wall\"");
        foreach (var m in zm.PerkMachines)
            if (rules.Perk(m.Perk) == null)
                Fail($"{map.Name}: perk machine '{m.Perk}' isn't a perk in the rules");
        if (rules.Perks.Any(p => zm.PerkMachines.All(m => rules.Perk(m.Perk) != p)))
            Fail($"{map.Name}: not every perk has a machine");
        if (zm.PerkMachines.Any(m => m.Perk is "revive" or "secondwind" && (m.NeedsPower || m.Area != zm.StartArea)))
            Fail($"{map.Name}: Quick Revive and Second Wind should be in the start room without the power");

        // Every area is reachable through doors (the door graph), and every door is a real way through (shut: blocked).
        var reach = new HashSet<int> { zm.StartArea };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var d in zm.Doors)
                if (reach.Contains(d.AreaA) != reach.Contains(d.AreaB))
                {
                    reach.Add(d.AreaA);
                    reach.Add(d.AreaB);
                    grew = true;
                }
        }
        var badDoors = new List<string>();
        for (var i = 0; i < zm.Doors.Count; i++)
        {
            var d = zm.Doors[i];
            var shut = World.Blocked(d.Box.Center with { Y = d.Box.Min.Y + 0.05f }) && !World.CanWalkStraight(d.SideA, d.SideB);
            map.SetDoorOpen(i, true);
            var open = World.CanWalkStraight(d.SideA, d.SideB) && !World.Blocked(d.SideA + new Vector3(0, 0.05f, 0)) && !World.Blocked(d.SideB + new Vector3(0, 0.05f, 0));
            map.SetDoorOpen(i, false);
            if (!shut || !open || !zm.Areas[d.AreaA].Contains(d.SideA) || !zm.Areas[d.AreaB].Contains(d.SideB))
                badDoors.Add($"{d.Name} (shut {shut}, open {open})");
        }
        ZLine($"  areas reachable through doors: {reach.Count} of {zm.Areas.Count}; doors that are a real way through: {zm.Doors.Count - badDoors.Count} of {zm.Doors.Count}");
        if (reach.Count != zm.Areas.Count)
            Fail($"{map.Name}: areas not reachable through doors: {string.Join(", ", Enumerable.Range(0, zm.Areas.Count).Where(a => !reach.Contains(a)).Select(a => zm.Areas[a].Name))}");
        foreach (var b in badDoors)
            Fail($"{map.Name}: door {b} isn't a clean way between its two areas");

        // Spawners and windows: behind a window = outside every area, inside the outline, room to stand, a straight walk
        // to the window; in the ground = in its area; window inside points in the area and clear, openings block bodies.
        var bad = new List<string>();
        foreach (var s in zm.Spawners)
        {
            var at = s.At + new Vector3(0, 0.05f, 0);
            if (World.Blocked(at) || !map.InsideBounds(s.At.X, s.At.Z, 0.5f))
                bad.Add($"spawner at {s.At} is blocked or outside the map");
            else if (s.Window >= 0 && (zm.Inside(s.At) || !World.CanWalkStraight(s.At, zm.Windows[s.Window].Outside)))
                bad.Add($"spawner at {s.At} for window {s.Window} is inside an area or can't walk to its window");
            else if (s.Window < 0 && !zm.Areas[s.Area].Contains(s.At))
                bad.Add($"ground spawner at {s.At} isn't in {zm.Areas[s.Area].Name}");
        }
        for (var i = 0; i < zm.Windows.Count; i++)
        {
            var w = zm.Windows[i];
            if (!zm.Spawners.Any(s => s.Window == i))
                bad.Add($"window {i} has no spawner");
            if (World.Blocked(w.Inside + new Vector3(0, 0.05f, 0)) || World.Blocked(w.Outside + new Vector3(0, 0.05f, 0)) || zm.Inside(w.Outside))
                bad.Add($"window {i}: its inside or outside spot is blocked (or outside is in an area)");
            if (!World.Blocked(w.Opening.Center with { Y = w.Opening.Min.Y + 0.05f }, 1.0f))
                bad.Add($"window {i}: the opening doesn't block bodies");
        }
        ZLine($"  spawners and windows: {zm.Spawners.Count} spawners, {zm.Windows.Count} windows, {bad.Count} problem(s)");
        foreach (var b in bad)
            Fail($"{map.Name}: {b}");

        // Machines: each one stands as its solid prop (players can't walk through it); Zombies Test keeps them collision-free.
        var machines = zm.Machines().ToList();
        var props = map.PropInstances.Where(p => p.Def.Category == "Zombies").ToList();
        var notSolid = machines.Count(m => !World.Blocked(m.Spot.At + new Vector3(0, 0.05f, 0), 0.5f));
        var matched = machines.Count(m => props.Any(p => p.Def.Name == m.Prop && Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(m.Spot.At.X, m.Spot.At.Z)) < 0.01f));
        ZLine($"  machine props: {machines.Count} machines, {matched} with their prop, {machines.Count - notSolid} solid; Zombies Test machine props {ZTestMap?.Zombies?.MachineProps}");
        if (!zm.MachineProps || matched != machines.Count || notSolid > 0)
            Fail($"{map.Name}: every machine should stand as its solid prop ({matched} of {machines.Count} placed, {notSolid} not solid)");
        if (ZTestMap?.Zombies?.MachineProps != false)
            Fail("Zombies Test should keep its machines without props");
        var copy = MapFile.Load(MapFile.ToJson(map), "blackmoor round trip", map.PackDir ?? "");
        if (copy.ContentHash != map.ContentHash || copy.PropInstances.Count != map.PropInstances.Count || !copy.Solid.SequenceEqual(map.Solid))
            Fail($"{map.Name}: the map (with its machine props) didn't survive a map file round trip");
    }

    // ---- Zombies on the map: through every window, through every door, the AI cost ---------------------------

    private static Zombie ZPlace(Game g, ZombiesMap zm, Vector3 at, int area, ZSpeed speed, double now)
    {
        var z = g.SelfTestSpawnZombie(zm.Spawners.FindIndex(s => s.Window >= 0), now);
        z.State = ZState.Chase;
        z.Window = -1;
        z.Area = area;
        z.Speed = speed;
        z.Sim.Position = z.PrevPos = at;
        for (var i = 0; i < z.History.Length; i++)
            z.History[i] = at;
        return z;
    }

    /// <summary>A free spot to stand near the middle of <paramref name="a"/> (props and machines stand in the way).</summary>
    private static Vector3 FreeSpot(Map map, ZArea a)
    {
        var c = a.Center;
        for (var r = 0f; r < 8f; r += 0.5f)
            for (var k = 0; k < 16; k++)
            {
                var p = new Vector3(c.X + MathF.Sin(k * MathF.Tau / 16) * r, 0, c.Z + MathF.Cos(k * MathF.Tau / 16) * r);
                p.Y = map.GroundHeight(p.X, p.Z);
                if (a.Contains(p) && a.Distance(p) <= 0 && p.X > a.X0 + 0.6f && p.X < a.X1 - 0.6f && p.Z > a.Z0 + 0.6f && p.Z < a.Z1 - 0.6f && !World.Blocked(p + new Vector3(0, 0.05f, 0)))
                    return p;
            }
        return c;
    }

    private static void CheckBlackmoorZombies(Map map, ZombiesMap zm, ref double now)
    {
        // Every window: a zombie from its spawner walks up, finds the boards gone and climbs in.
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
            for (var i = 0; i < g.ZBoards.Length; i++)
                g.ZBoards[i] = 0;
            var list = new List<(Zombie Z, int Window)>();
            for (var s = 0; s < zm.Spawners.Count; s++)
                if (zm.Spawners[s].Window >= 0)
                    list.Add((g.SelfTestSpawnZombie(s, now), zm.Spawners[s].Window));
            var inside = new HashSet<Zombie>();
            for (var t = 0; t < 60 * 20 && inside.Count < list.Count; t++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                foreach (var (z, _) in list)
                    if (z.State is ZState.Climbing or ZState.Chase or ZState.Attack)
                        inside.Add(z);
            }
            var stuck = list.Where(x => !inside.Contains(x.Z)).Select(x => $"window {x.Window} (zombie {x.Z.State} at {x.Z.Position.X:0.0},{x.Z.Position.Z:0.0})").ToList();
            ZLine($"  {map.Name} windows: {inside.Count} of {list.Count} zombies climbed in within 20 s{(stuck.Count > 0 ? ": stuck at " + string.Join(", ", stuck) : "")}");
            if (stuck.Count > 0)
                Fail($"{map.Name}: zombies didn't get in through {string.Join(", ", stuck)}");
        }

        // Every door: all open, a zombie in every area runs to a player at the far end (two targets, so every door is crossed).
        foreach (var target in new[] { "Research Lab", "Generator House" })
            using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
            {
                for (var d = 0; d < zm.Doors.Count; d++)
                    g.OpenDoor(d, null);
                var me = g.Local!;
                me.ImmuneUntil = 1e9;
                var ta = zm.AreaIndex(target);
                me.Teleport(FreeSpot(map, zm.Areas[ta]));
                var list = new List<Zombie>();
                for (var a = 0; a < zm.Areas.Count; a++)
                    if (a != ta)
                        list.Add(ZPlace(g, zm, FreeSpot(map, zm.Areas[a]), a, ZSpeed.Sprint, now));
                var arrived = new HashSet<Zombie>();
                for (var t = 0; t < 60 * 40 && arrived.Count < list.Count; t++)
                {
                    now += World.TickDt;
                    g.SelfTestZombiesTick(now);
                    foreach (var z in list)
                        if (z.Alive && (ZFlat(z.Position - me.Position) < 3f || zm.AreaAt(z.Position) == ta && zm.Inside(z.Position)))
                            arrived.Add(z);
                }
                var stuck = list.Where(z => !arrived.Contains(z)).Select(z => $"from {zm.Areas[zm.AreaAt(z.Sim.Position)].Name} ({z.Position.X:0.0},{z.Position.Z:0.0})").ToList();
                ZLine($"  {map.Name} doors -> {target}: {arrived.Count} of {list.Count} zombies arrived within 40 s{(stuck.Count > 0 ? "; stuck " + string.Join(", ", stuck) : "")}");
                if (stuck.Count > 0)
                    Fail($"{map.Name}: zombies got stuck on the way to the {target}: {string.Join(", ", stuck)}");
            }

        // The AI cost on the new map: 24 zombies chasing through the open ward and offices (the same budget as Zombies Test).
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
        {
            g.OpenDoor(0, null);
            g.OpenDoor(1, null);
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
            var areas = new[] { zm.AreaIndex("East Ward"), zm.AreaIndex("West Offices"), zm.StartArea };
            for (var i = 0; i < 24; i++)
            {
                var a = zm.Areas[areas[i % 3]];
                var p = FreeSpot(map, a);
                p += new Vector3((i / 3 % 4 - 1.5f) * 1.1f, 0, (i / 12 - 0.5f) * 1.2f);
                if (World.Blocked(p + new Vector3(0, 0.05f, 0)) || !a.Contains(p))
                    p = FreeSpot(map, a);
                ZPlace(g, zm, p, areas[i % 3], (ZSpeed)(i % 3), now);
            }
            ZTicks(g, ref now, 0.5);
            g.ZAiMs = g.ZAiZombieTicks = g.ZAiTicks = 0;
            ZTicks(g, ref now, 4);
            var perZombie = g.ZAiMs * 1000 / Math.Max(1, g.ZAiZombieTicks);
            var perTick = g.ZAiMs / Math.Max(1, g.ZAiTicks);
            ZLine($"  {map.Name} AI cost: {g.AliveZombies} alive, {perZombie:0.0} us per zombie per tick, {perTick:0.00} ms per tick");
            if (perZombie > 150 || perTick > 3)
                Fail($"the zombie AI is too slow on {map.Name}: {perZombie:0.0} us per zombie per tick");
        }
    }

    // ---- Power-ups -------------------------------------------------------------------------------------------

    private static void CheckZombiePowerUps(ref double now)
    {
        var rules = ZombiesRules.Current;
        if (rules.PowerMaxPerRound < 1 || rules.PowerDropChance <= 0 || rules.PowerLifetime != 30 || rules.PowerWeights.Length != (int)ZPowerKind.COUNT)
            Fail("the power-up rules (drop chance, per-round cap, a 30 s lifetime, a weight per kind) aren't in Parts/Tables/ZombiesRules.json");
        foreach (var bad in new[] { """{"format": "call-of-slop-zombies/1", "powerUps": {"weights": {"raygun": 1}}}""", """{"format": "call-of-slop-zombies/1", "ammoEffects": {"burnSeconds": -1}}""" })
            try
            {
                ZombiesRules.Load(bad, "bad rules");
                Fail($"broken rules loaded: {bad}");
            }
            catch (WeaponFileException)
            {
            }

        // Drops: every kill rolls (chance 1 here), at most the cap a round; a new round resets the cap.
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.PowerDropChance = 1f; }))
        {
            var me = g.Local!;
            me.Teleport(new Vector3(6, 0, 3));
            g.SelfTestBeginRound(1, now);
            for (var i = 0; i < 12; i++)
            {
                var z = g.SelfTestSpawnZombie(0, now);
                z.Sim.Position = new Vector3(-5 + i * 0.7f, 0, -2);
                g.DamageZombie(me, z, 100000, false, false, now);
            }
            var first = g.ZPowerUps.Count;
            var kinds = string.Join(",", g.ZPowerUps.Select(p => Game.PowerName(p.Kind)));
            var allInside = g.ZPowerUps.All(p => g.ZMap!.Inside(p.At));
            g.SelfTestBeginRound(2, now);
            var z2 = g.SelfTestSpawnZombie(0, now);
            g.DamageZombie(me, z2, 100000, false, false, now);
            var afterRound = g.ZPowerUps.Count;
            ZLine($"power-ups: 12 kills at chance 1 -> {first} dropped (cap {g.ZRules.PowerMaxPerRound}: {kinds}), all inside the areas {allInside}; next round +{afterRound - first}");
            if (first != g.ZRules.PowerMaxPerRound || afterRound != first + 1 || !allInside)
                Fail("power-ups should drop on kills up to the per-round cap (reset each round), inside the areas (a kill at a window drops it at the window's inside)");
        }

        // Each one applies; pickups by walking into them; lifetime and the timers run out.
        using (var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.PowerDropChance = 0f; }))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            var zp = g.ZP(me);
            var spot = new Vector3(5, 0, -2);
            me.Teleport(spot);
            var results = new List<string>();
            var ok = true;

            // Max Ammo: every slot's reserve full again.
            g.Slots[0].Reserve = 0;
            g.Slots[1].Reserve = 1;
            g.SpawnPowerUp(ZPowerKind.MaxAmmo, spot, now);
            g.SelfTestTickPowerUps(now);
            var maxAmmo = g.ZPowerUps.Count == 0 && g.Slots[0].Reserve == g.Slots[0].Def.Mag * g.ZRules.ReserveMags && g.Slots[1].Reserve == g.Slots[1].Def.Mag * g.ZRules.ReserveMags;
            results.Add($"Max Ammo {maxAmmo}");
            ok &= maxAmmo;

            // Insta-Kill: a 1-damage hit kills a full-health zombie; after the duration it doesn't.
            g.SpawnPowerUp(ZPowerKind.InstaKill, spot, now);
            g.SelfTestTickPowerUps(now);
            var zi = g.SelfTestSpawnZombie(0, now);
            var insta = g.DamageZombie(me, zi, 1, false, false, now);
            var zj = g.SelfTestSpawnZombie(0, now);
            var later = now + g.ZRules.PowerDuration + 0.1;
            var instaOver = !g.DamageZombie(me, zj, 1, false, false, later) && g.InstaKillLeft(later) == 0;
            results.Add($"Insta-Kill {insta}, over after {g.ZRules.PowerDuration:0} s {instaOver}");
            ok &= insta && instaOver;

            // Double Points: a hit pays twice (Insta-Kill off first, or the hit would be a kill).
            g.ZInstaKillUntil = 0;
            g.SpawnPowerUp(ZPowerKind.DoublePoints, spot, now);
            g.SelfTestTickPowerUps(now);
            var zd = g.SelfTestSpawnZombie(0, now);
            var p0 = zp.Points;
            g.DamageZombie(me, zd, 1, false, false, now);
            var doubled = zp.Points - p0;
            var p1 = zp.Points;
            g.DamageZombie(me, zd, 1, false, false, later);
            var single = zp.Points - p1;
            results.Add($"Double Points +{doubled} then +{single}");
            ok &= doubled == 2 * g.ZRules.HitPoints && single == g.ZRules.HitPoints;

            // Nuke: every zombie alive dies, everyone gets its points (no kill points).
            var horde = new List<Zombie>();
            for (var i = 0; i < 5; i++)
                horde.Add(g.SelfTestSpawnZombie(0, now));
            var kills = zp.Kills;
            var p2 = zp.Points;
            g.SpawnPowerUp(ZPowerKind.Nuke, spot, now);
            g.SelfTestTickPowerUps(now);
            var nuked = horde.All(z => !z.Alive) && g.AliveZombies == 0 && zp.Points - p2 == g.ZRules.NukePoints && zp.Kills == kills;
            results.Add($"Nuke {nuked} (+{zp.Points - p2})");
            ok &= nuked;

            // Carpenter: every board back, everyone gets its points.
            for (var i = 0; i < g.ZBoards.Length; i++)
                g.ZBoards[i] = 1;
            var p3 = zp.Points;
            var kind = g.PickPowerKind(0.999f);
            g.SpawnPowerUp(ZPowerKind.Carpenter, spot, now);
            g.SelfTestTickPowerUps(now);
            var carp = g.ZBoards.Select((b, i) => b == g.ZMap!.Windows[i].Boards).All(x => x) && zp.Points - p3 == g.ZRules.CarpenterPoints;
            var noCarp = g.PickPowerKind(0.999f);
            results.Add($"Carpenter {carp} (picked with boards down: {Game.PowerName(kind)}, all up: {Game.PowerName(noCarp)})");
            ok &= carp && kind == ZPowerKind.Carpenter && noCarp != ZPowerKind.Carpenter;

            // Out of reach: it stays; after its lifetime it's gone (blinking first).
            var far = g.SpawnPowerUp(ZPowerKind.MaxAmmo, new Vector3(-6, 0, -4), now);
            g.SelfTestTickPowerUps(now + 1);
            var stays = g.ZPowerUps.Contains(far);
            g.SelfTestTickPowerUps(far.Until - 0.01);
            var stillThere = g.ZPowerUps.Contains(far);
            g.SelfTestTickPowerUps(far.Until);
            var gone = !g.ZPowerUps.Contains(far);
            results.Add($"out of reach stays {stays}, there until {g.ZRules.PowerLifetime:0} s {stillThere}, gone at {g.ZRules.PowerLifetime:0} s {gone}");
            ok &= stays && stillThere && gone;
            ZLine($"  power-ups apply: {string.Join("; ", results)}");
            if (!ok)
                Fail("every power-up should apply (Max Ammo, Insta-Kill, Double Points, Nuke, Carpenter), be taken by walking into it and expire");

            // Replication: the floating ones and the timers reach a client.
            g.SpawnPowerUp(ZPowerKind.Nuke, new Vector3(-6, 0, -4), now);
            g.SpawnPowerUp(ZPowerKind.InstaKill, spot, now);
            g.SelfTestTickPowerUps(now); // (the Insta-Kill under the player is taken: its timer runs)
            g.SelfTestZombiesTick(now);
            var w = new PacketWriter();
            g.WriteZEnts(w);
            var ents = w.Span.ToArray();
            var w2 = new PacketWriter();
            g.WriteZWorld(w2);
            var world = w2.Span.ToArray();
            var serverLeft = g.InstaKillLeft(now);
            var tnow = now;
            var serverUps = g.ZPowerUps.Select(p => (p.Id, p.Kind, Left: p.Until - tnow)).ToList();
            using var c = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999);
            c.SelfTestReadZombies(ents, world);
            var same = c.ZPowerUps.Count == serverUps.Count && serverUps.All(s => c.ZPowerUps.Any(x => x.Id == s.Id && x.Kind == s.Kind && Math.Abs((x.Until - 0) - s.Left) < 0.11))
                       && Math.Abs(c.InstaKillLeft(0) - serverLeft) < 0.11;
            ZLine($"  power-up replication: {serverUps.Count} floating, Insta-Kill {serverLeft:0.0} s left -> client {c.ZPowerUps.Count}, {c.InstaKillLeft(0):0.0} s; ZWorld {world.Length} bytes; same {same}");
            if (!same || world.Length > PacketWriter.Mtu)
                Fail("power-ups and their timers didn't replicate to a client");
        }
    }

    // ---- Special ammo effects --------------------------------------------------------------------------------

    private static void CheckZombieAmmoEffects(ref double now)
    {
        // Every flag a part carries either has a Zombies effect or is known to have none.
        var known = new HashSet<string>(Game.ZombieEffectFlags, StringComparer.OrdinalIgnoreCase) { "suppressed", "cart" };
        var unknown = Parts.All.SelectMany(p => p.Flags).Distinct().Where(f => !known.Contains(f)).ToList();
        ZLine($"special ammo: {Game.ZombieEffectFlags.Count} flags with a Zombies effect ({string.Join(", ", Game.ZombieEffectFlags.Order())}); part flags without one: {(unknown.Count == 0 ? "none" : string.Join(", ", unknown))}");
        if (unknown.Count > 0)
            Fail($"part flags with no Zombies effect (add one or list them as none): {string.Join(", ", unknown)}");

        using var g = ZGame(0, ref now, tweak: r => { r.FirstRoundDelay = 9999; r.PowerDropChance = 0f; });
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(new Vector3(0, 0, 4.5f));
        var zm = g.ZMap!;
        var start = zm.StartArea;
        var t = now;
        Zombie Z(float x, float z)
        {
            var zz = ZPlace(g, zm, new Vector3(x, 0, z), start, ZSpeed.Walk, t);
            zz.Health = zz.MaxHealth = 5000;
            return zz;
        }
        void Clear()
        {
            foreach (var zz in g.Horde)
                zz.State = ZState.Dead;
            g.SelfTestTickHorde(t += 2); // (the dead are gone after 1.5 s)
        }
        var lines = new List<string>();
        var ok = true;
        void Check(string what, bool pass, string detail)
        {
            lines.Add($"{what} {(pass ? "ok" : "FAILED")} ({detail})");
            ok &= pass;
        }

        // Incendiary: a burn of 60 percent of the hit (at least 40) over 3 s, for the shooter; again: refreshed, not doubled.
        var a = Z(-3, -3);
        g.SelfTestEffects(me, ["incendiary"], a, 100, t);
        var afterHit = a.Health;
        g.SelfTestEffects(me, ["incendiary", "incendiary"], a, 100, t); // two incendiary parts: still one burn
        var afterHit2 = a.Health;
        var pts = g.ZP(me).Points;
        for (var i = 0; i < 60 * 4; i++)
            g.SelfTestTickHorde(t += World.TickDt);
        var burned = afterHit2 - a.Health;
        var want = (int)MathF.Round(MathF.Max(g.ZRules.BurnMin, 100 * g.ZRules.BurnFraction));
        Check("incendiary", Math.Abs(burned - want) <= 4 && g.ZP(me).Points == pts && afterHit - afterHit2 == 100, $"burned {burned} of {want} over {g.ZRules.BurnSeconds:0} s after two hits, no points for the ticks");
        Clear();

        // Explosive: the zombies round the one hit take a splash (walls shield; one splash per shot).
        var b = Z(-3, -3);
        var b2 = Z(-2.2f, -3);
        var b3 = Z(3, 2); // out of range
        g.SelfTestEffects(me, ["explosive"], b, 200, t);
        Check("explosive", b2.Health < b2.MaxHealth && b2.MaxHealth - b2.Health <= 200 * g.ZRules.SplashFraction + 1 && b3.Health == b3.MaxHealth, $"next to it -{b2.MaxHealth - b2.Health}, far away -{b3.MaxHealth - b3.Health}");
        Clear();

        // Shock: arcs to the 3 nearest others in range, stunning them; not further.
        var s0 = Z(-3, -3);
        var near = new[] { Z(-2, -3), Z(-3, -2), Z(-4, -3), Z(-3, -4.2f) };
        var farS = Z(4, 2);
        g.SelfTestEffects(me, ["shock"], s0, 200, t);
        var zapped = near.Count(z => z.Health < z.MaxHealth && z.StunnedUntil > t);
        Check("shock", zapped == g.ZRules.ShockTargets && farS.Health == farS.MaxHealth && near.All(z => z.MaxHealth - z.Health <= 200 * g.ZRules.ShockFraction + 1), $"{zapped} of {near.Length} near ones zapped (most {g.ZRules.ShockTargets}), the far one untouched {farS.Health == farS.MaxHealth}");
        Clear();

        // Penetrating: the round goes on through the zombies behind along the ray.
        var p1z = Z(0, 0);
        var p2z = Z(0, -2);
        var p3z = Z(0, -4);
        var p4z = Z(0, -6);
        g.SelfTestEffects(me, ["penetrating"], p1z, 100, t);
        var pierced = new[] { p2z, p3z, p4z }.Count(z => z.Health < z.MaxHealth);
        Check("penetrating", pierced == g.ZRules.PierceTargets, $"{pierced} behind hit (most {g.ZRules.PierceTargets})");
        Clear();

        // Bouncy + squeaky (the rubber duck): one bounce to the nearest other, a short stun.
        var d0 = Z(-3, -3);
        var d1 = Z(-1.5f, -3);
        var d2 = Z(1, -3);
        g.SelfTestEffects(me, ["bouncy", "squeaky"], d0, 100, t);
        Check("bouncy + squeaky", d1.Health < d1.MaxHealth && d2.Health == d2.MaxHealth && d0.StunnedUntil > t, $"bounced to the nearest {d1.Health < d1.MaxHealth}, not the next {d2.Health == d2.MaxHealth}, stunned {d0.StunnedUntil > t}");
        Clear();

        // Plasma: slowed (it covers less ground), zapped.
        var slow = Z(-4, -4);
        var fast = Z(4, -4);
        g.SelfTestEffects(me, ["plasma"], slow, 10, t);
        var s0p = slow.Position;
        var f0p = fast.Position;
        for (var i = 0; i < 60; i++)
            g.SelfTestTickHorde(t += World.TickDt);
        var slowMoved = ZFlat(slow.Position - s0p);
        var fastMoved = ZFlat(fast.Position - f0p);
        Check("plasma", slow.FxZapped && slowMoved < fastMoved * 0.75f, $"moved {slowMoved:0.00} m vs {fastMoved:0.00} m in 1 s");
        Clear();

        // Melee flags: bonk stuns, the fish and the rocket knock back, nails and the chainsaw bleed.
        var k0 = Z(0, 1.5f);
        var k0p = k0.Position;
        g.SelfTestEffects(me, ["fish"], k0, 10, t);
        for (var i = 0; i < 20; i++)
            g.SelfTestTickHorde(t += World.TickDt);
        var knocked = ZFlat(k0.Position - k0p);
        var bonk = Z(-4, 1);
        g.SelfTestEffects(me, ["bonk"], bonk, 10, t);
        var bleed = Z(4, 1);
        g.SelfTestEffects(me, ["nails"], bleed, 200, t);
        var hb = bleed.Health;
        for (var i = 0; i < 60 * 4; i++)
            g.SelfTestTickHorde(t += World.TickDt);
        Check("fish / bonk / nails", knocked > 0.4f && bonk.StunnedUntil >= t - 4 + g.ZRules.BonkStun - 0.01 && hb - bleed.Health >= (int)(200 * g.ZRules.BleedFraction) - 4,
            $"knocked back {knocked:0.00} m, bonk stun {g.ZRules.BonkStun:0.0} s, bled {hb - bleed.Health}");
        Clear();

        // Party and refreshing: a kill floats away on balloons and gives a sip of health.
        var pk = Z(-3, -3);
        pk.Health = 5;
        me.Health = 50;
        g.SelfTestEffects(me, ["party", "refreshing"], pk, 100, t);
        var y0 = pk.Sim.Position.Y;
        for (var i = 0; i < 30; i++)
            g.SelfTestTickHorde(t += World.TickDt);
        Check("party / refreshing", pk.Floaty && pk.Sim.Position.Y > y0 + 0.5f && me.Health == 50 + g.ZRules.RefreshHealth, $"floated {pk.Sim.Position.Y - y0:0.00} m, health {me.Health}");
        Clear();

        // A real shot: a gun whose build carries a flag sets its effect off through the server's shot path.
        var gun = me.WeaponDef;
        var oldFlags = gun.BuildFlags;
        gun.BuildFlags = ["incendiary"];
        var target = Z(0, 0.5f);
        g.SelfTestServerShot(me, me.Eye, Vector3.Normalize(target.Position + new Vector3(0, 1.0f, 0) - me.Eye), t);
        gun.BuildFlags = oldFlags;
        Check("a real shot", target.BurnUntil > t && target.Health < target.MaxHealth, $"hit {target.MaxHealth - target.Health}, burning {target.BurnUntil > t}");
        Clear();
        now = t;
        ZLine($"  special ammo: {string.Join("; ", lines)}");
        if (!ok)
            Fail($"a special ammo effect didn't do its damage: {string.Join("; ", lines.Where(l => l.Contains("FAILED")))}");
    }
}
