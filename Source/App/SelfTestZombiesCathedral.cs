using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombies cathedral" (board item dev-zombies-cathedral-zc01; docs/zombies/ZOMBIES-MODE.md, "Ashgrave
/// Cathedral" and "The bell: a siege up the tower"): what the map's own "test" block can't say about Ashgrave Cathedral.
/// <list type="bullet">
/// <item>The layout: the Zombies pack, a full set (every perk, a slot machine for every part category and the classic one,
/// every casino game, workbenches, box spots, the power, the slot upgrade, wall-tagged wall buys), every area reachable
/// through the doors and every door a real way through on its own floor, the windows and spawners sound, the machines
/// solid, and every machine, wall buy and spawner inside its area on its own floor (areas with heights).</item>
/// <item>Zombies get in through every window, and run from every area to a player in the belfry and in the dungeons'
/// inquisitor's chamber (up and down the tower, the crypt and dungeon stairs) with every door open.</item>
/// <item>The bell siege (a seeded round 10): ringing the bell starts the climb and the challenge; the horde outside
/// leaves the windows for the tower's north face, builds the pyramid and climbs over the parapet; a shot body falls and
/// the ones above slide down; surviving pays points, parts and a Max Ammo; when it ends the face is cleared and the horde
/// goes back to the windows. No allocation in the climb's ticks, the tick cost, a client's copy (the climbing state and
/// heights), and the same siege twice plays out the same.</item>
/// </list>
/// </summary>
public static partial class SelfTest
{
    private const string CathedralName = "Ashgrave Cathedral";

    private static void CheckZombiesCathedral()
    {
        var now = 0.0;
        if (Maps.Find(CathedralName) is not { Zombies: { } zm } map)
        {
            Fail($"Maps/Zombies/02-AshgraveCathedral.json ('{CathedralName}') is missing or has no \"zombies\" section");
            return;
        }
        var saved = ZNotes.ToList();
        ZNotes.Clear();
        try
        {
            CathedralLayout(map, zm);
            CathedralWalks(map, zm, ref now);
            var a = CathedralSiege(map, zm, ref now, full: true);
            var b = CathedralSiege(map, zm, ref now, full: false);
            var c = CathedralSiege(map, zm, ref now, full: false);
            ZLine($"  siege replay: {b:x16} / {c:x16}");
            if (b != c)
                Fail($"{CathedralName}: the same seeded siege played out differently ({b:x16} vs {c:x16})");
            _ = a;
        }
        finally
        {
            Line("::warning title=Zombies cathedral (results)::" + string.Join("%0A", ZNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
            ZNotes.Clear();
            ZNotes.AddRange(saved);
        }
    }

    // ---- The layout ---------------------------------------------------------------------------------------------------

    private static void CathedralLayout(Map map, ZombiesMap zm)
    {
        World.SetMap(map);
        map.CloseAllDoors();
        var rules = ZombiesRules.Current;
        var floors = zm.Areas.Select(a => a.Y0 + 0.5f).Distinct().OrderBy(y => y).ToList();
        ZLine($"{map.Name} ({map.Pack?.Name} pack): {zm.Areas.Count} areas on {floors.Count} floors ({string.Join(", ", floors.Select(y => $"{y:0.#}"))}), {zm.Doors.Count} doors ({zm.Doors.Min(d => d.Cost)}-{zm.Doors.Max(d => d.Cost)}, {zm.Doors.Count(d => d.NeedsPower)} on the power), "
              + $"{zm.Windows.Count} windows, {zm.Spawners.Count} spawners ({zm.Spawners.Count(s => s.Window < 0)} in the ground), {zm.BoxSpots.Count} box spots, {zm.WallBuys.Count} wall buys, {zm.PerkMachines.Count} perks, "
              + $"{zm.SlotMachines.Count} slot machines, {zm.CasinoTables.Count} casino tables, {zm.Benches.Count} workbenches, {zm.Salvage.Count} salvage, {map.PropInstances.Count} props");
        if (map.Pack is not { } pack || pack.Name != "Zombies" || !GameModes.Fits(GameMode.Zombies, map) || !GameModes.ForMap(map).Contains(GameMode.Zombies))
            Fail($"{map.Name} should be in the Zombies pack and offer the Zombies mode");
        if (zm.Areas.Count < 16 || floors.Count < 5 || zm.Windows.Count < 20 || zm.BoxSpots.Count < 10 || zm.Benches.Count < 4 || zm.SlotUpgrade == null || zm.Power == null
            || zm.Salvage.Count < 4 || zm.Spawners.Count(s => s.Window < 0) < 10 || zm.Doors.Count(d => d.NeedsPower) < 1)
            Fail($"{map.Name} is missing part of the plan (16+ areas on 5+ floors, 20+ windows, 10+ box spots, 4+ workbenches, the slot upgrade, the power, salvage, ground spawners on every level, a door on the power)");
        foreach (var wb in zm.WallBuys)
            if (StandardBuilds.Find(wb.Build) is not { } sb || !sb.Sources.Contains("wall"))
                Fail($"{map.Name}: wall buy '{wb.Build}' isn't a standard build tagged \"wall\"");
        var missingPerks = rules.Perks.Where(p => zm.PerkMachines.All(m => rules.Perk(m.Perk) != p)).Select(p => p.Key).ToList();
        if (missingPerks.Count > 0)
            Fail($"{map.Name}: no machine for the perks {string.Join(", ", missingPerks)}");
        var missingSlots = ZombiesMap.SlotCategories.Where(c => zm.SlotMachines.All(m => m.SlotCategory != c)).ToList();
        if (missingSlots.Count > 0 || zm.SlotMachines.All(m => m.SlotCategory >= 0))
            Fail($"{map.Name}: slot machines missing for {string.Join(", ", missingSlots)} (or no classic one)");
        if (Enumerable.Range(0, CasinoGames.Keys.Length).Any(gm => zm.CasinoTables.All(t => t.Game != gm)))
            Fail($"{map.Name}: not every casino game has a table");
        if (zm.PerkMachines.Any(m => m.Perk is "revive" or "secondwind" && (m.NeedsPower || m.Area != zm.StartArea)))
            Fail($"{map.Name}: Mender's Mead and Second Wind should be in the start area without the power");

        ZombiesMapSound(map, zm);

        // Areas with heights: every machine, wall buy (its floor) and ground spawner is inside an area, on its own floor.
        var outside = new List<string>();
        foreach (var (prop, spot) in zm.Machines())
            if (!zm.Areas[spot.Area].Contains(spot.At + new Vector3(0, 0.1f, 0)))
                outside.Add($"{prop} at {spot.At}");
        foreach (var wb in zm.WallBuys)
            if (!zm.Areas[wb.Area].Contains(wb.At - new Vector3(0, 1.2f, 0)))
                outside.Add($"wall buy {wb.Build} at {wb.At}");
        foreach (var s in zm.Spawners)
            if (s.Window < 0 && !zm.Areas[s.Area].Contains(s.At + new Vector3(0, 0.1f, 0)))
                outside.Add($"ground spawner at {s.At}");
        ZLine($"  areas with heights: {zm.Machines().Count() + zm.WallBuys.Count + zm.Spawners.Count(s => s.Window < 0) - outside.Count} machines, wall buys and ground spawners inside their own area's floor");
        foreach (var o in outside)
            Fail($"{map.Name}: {o} isn't inside its area (on its floor)");
    }

    // ---- Zombies through the windows and the doors ----------------------------------------------------------------

    private static void CathedralWalks(Map map, ZombiesMap zm, ref double now)
    {
        using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
        {
            var me = g.Local!;
            me.ImmuneUntil = 1e9;
            me.Teleport(FreeSpot(map, zm.Areas[zm.StartArea]));
            for (var i = 0; i < g.ZBoards.Length; i++)
                g.ZBoards[i] = 0;
            var list = new List<(Zombie Z, int Window)>();
            for (var s = 0; s < zm.Spawners.Count; s++)
                if (zm.Spawners[s].Window >= 0 && list.All(x => x.Window != zm.Spawners[s].Window))
                    list.Add((g.SelfTestSpawnZombie(s, now), zm.Spawners[s].Window));
            var inside = new HashSet<Zombie>();
            for (var t = 0; t < 60 * 25 && inside.Count < list.Count; t++)
            {
                now += World.TickDt;
                g.SelfTestZombiesTick(now);
                foreach (var (z, _) in list)
                    if (z.State is ZState.Climbing or ZState.Chase or ZState.Attack)
                        inside.Add(z);
            }
            var stuck = list.Where(x => !inside.Contains(x.Z)).Select(x => $"window {x.Window} (zombie {x.Z.State} at {x.Z.Position.X:0.0},{x.Z.Position.Y:0.0},{x.Z.Position.Z:0.0})").ToList();
            ZLine($"  windows: {inside.Count} of {list.Count} zombies climbed in within 25 s{(stuck.Count > 0 ? ": stuck at " + string.Join(", ", stuck) : "")}");
            if (stuck.Count > 0)
                Fail($"{map.Name}: zombies didn't get in through {string.Join(", ", stuck)}");
        }

        // Every door open, a zombie in every other area runs to a player in the belfry, then in the inquisitor's chamber.
        foreach (var (target, spot) in new[] { ("Bell Tower", new Vector3(-31.5f, 30f, -19.5f)), ("Inquisitor's Chamber", new Vector3(-20f, 2f, 15f)) })
            using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
            {
                for (var d = 0; d < zm.Doors.Count; d++)
                    g.OpenDoor(d, null);
                var me = g.Local!;
                me.ImmuneUntil = 1e9;
                var ta = zm.AreaIndex(target);
                me.Teleport(spot);
                var list = new List<Zombie>();
                for (var a = 0; a < zm.Areas.Count; a++)
                    if (a != ta)
                        list.Add(ZPlace(g, zm, FreeSpot(map, zm.Areas[a]), a, ZSpeed.Sprint, now));
                var arrived = new HashSet<Zombie>();
                for (var t = 0; t < 60 * 60 && arrived.Count < list.Count; t++)
                {
                    now += World.TickDt;
                    me.Teleport(spot);
                    g.SelfTestZombiesTick(now);
                    foreach (var z in list)
                        if (z.Alive && (Vector3.Distance(z.Position, me.Position) < 3f || zm.Areas[ta].Contains(z.Position)))
                            arrived.Add(z);
                }
                var stuck = list.Where(z => !arrived.Contains(z)).Select(z => $"from {zm.Areas[z.Area].Name} at ({z.Position.X:0.0},{z.Position.Y:0.0},{z.Position.Z:0.0})").ToList();
                ZLine($"  doors -> {target}: {arrived.Count} of {list.Count} zombies arrived within 60 s{(stuck.Count > 0 ? "; stuck " + string.Join(", ", stuck) : "")}");
                if (stuck.Count > 0)
                    Fail($"{map.Name}: zombies got stuck on the way to the {target}: {string.Join(", ", stuck)}");
            }
    }

    // ---- The bell siege -----------------------------------------------------------------------------------------------

    /// <summary>
    /// A seeded round-10 siege: 40 zombies outside, the host in the belfry rings the bell. <paramref name="full"/>: the whole
    /// challenge with every check; otherwise 20 s for the replay digest (returned).
    /// </summary>
    private static ulong CathedralSiege(Map map, ZombiesMap zm, ref double nowRef, bool full)
    {
        var now = nowRef;
        try
        {
            return CathedralSiegeRun(map, zm, ref now, full);
        }
        finally
        {
            nowRef = now;
        }
    }

    private static ulong CathedralSiegeRun(Map map, ZombiesMap zm, ref double nowRef, bool full)
    {
        var now = nowRef;
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
        var c = g.ZRules.Climb;
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var belfry = new Vector3(-31.6f, 30f, -19.4f);
        me.Teleport(belfry);
        g.SelfTestBeginRound(10, now);
        int bell = g.SelfTestLogicIndex("Bell"), climb = g.SelfTestLogicIndex("Climb"), siege = g.SelfTestLogicIndex("Siege");
        if (bell < 0 || climb < 0 || siege < 0)
        {
            Fail($"{map.Name}: the bell's logic (Bell, Climb, Siege) is missing");
            nowRef = now;
            return 0;
        }
        var events = new List<(int E, LogicEvent Ev, double At)>();
        var t0 = now;
        g.LogicEventTap = (e, ev, _) => events.Add((e, ev, now - t0));
        bool Saw(int e, LogicEvent ev) => events.Any(x => x.E == e && x.Ev == ev);
        // The horde outside: one zombie at each of 40 window spawners, walking to their windows.
        var horde = new List<Zombie>();
        for (var s = 0; s < zm.Spawners.Count && horde.Count < 40; s++)
            if (zm.Spawners[s].Window >= 0)
                horde.Add(g.SelfTestSpawnZombie(s, now));
        var points0 = g.ZP(me).Points;
        var parts0 = g.ZP(me).Parts.Values.Sum();
        void Tick()
        {
            now += World.TickDt;
            me.Teleport(belfry);
            g.SelfTestSiegeTick(now);
        }
        for (var i = 0; i < 30; i++)
            Tick();
        var toSpawn0 = g.SelfTestToSpawn;
        g.SelfTestLogicEmit(bell, LogicEvent.Pressed, LogicSignal.Pulse(me.Id));
        Tick();
        var extra = g.SelfTestToSpawn - toSpawn0;
        int maxOn = 0, maxSlots = 0;
        float maxPile = 0;
        double reachedAt = -1;
        var digest = 1469598103934665603ul;
        var fellChecked = false;
        string fallNote = "";
        long allocBefore = 0, allocAfter = 0;
        double tickMs = 0;
        var tickN = 0;
        var seconds = full ? 80.0 : 20.0;
        for (var tick = 0; tick < (int)(seconds * World.TickRate); tick++)
        {
            var at = tick * World.TickDt;
            if (full && tick == 60 * 30)
                allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.GetTimestamp();
            Tick();
            if (full && tick >= 60 * 30 && tick < 60 * 32)
            {
                tickMs += Stopwatch.GetElapsedTime(sw).TotalMilliseconds;
                tickN++;
            }
            if (full && tick == 60 * 32 - 1)
                allocAfter = GC.GetAllocatedBytesForCurrentThread();
            maxOn = Math.Max(maxOn, g.ClimbCount);
            maxPile = MathF.Max(maxPile, g.ClimbPileHeight);
            maxSlots = g.ClimbSlots;
            if (reachedAt < 0 && g.ClimbOver > 0)
                reachedAt = at;
            foreach (var z in g.Horde)
                digest = (digest ^ (ulong)(int)MathF.Round(z.Position.X * 100) ^ ((ulong)(int)MathF.Round(z.Position.Y * 100) << 21) ^ ((ulong)(int)MathF.Round(z.Position.Z * 100) << 42) ^ (ulong)z.State) * 1099511628211ul;
            // Shoot a body low in the pyramid: it falls, and the next one up slides down into its place.
            if (full && !fellChecked && g.SelfTestClimbers.Count(z => z.ScaleRank < g.ClimbSlots) >= 6 && at > 8)
            {
                var pile = g.SelfTestClimbers.Where(z => z.ScaleRank < g.ClimbSlots).ToList();
                var low = pile[0];
                var next = pile[^1];
                var y0 = low.Position.Y;
                var nextRank = next.ScaleRank;
                var dmg = Math.Max(1, low.Health / 4);
                g.DamageZombie(me, low, dmg, false, false, now);
                var fell = low.State == ZState.Chase && low.SiegeOut;
                for (var k = 0; k < 30; k++)
                    Tick();
                var down = low.Position.Y < y0 + 0.05f || low.Sim.Grounded;
                var slid = next.State == ZState.Scaling && next.ScaleRank == nextRank - 1;
                fallNote = $"shot body (rank 0, {y0 - map.Zombies!.Areas[0].Y0:0.0}) fell {fell} and came down {down}; the top body moved down a place {slid}";
                if (!fell || !down || !slid)
                    Fail($"{map.Name}: a shot body didn't fall off the pyramid or the ones above didn't slide down ({fallNote})");
                fellChecked = true;
            }
        }
        g.LogicEventTap = null;
        nowRef = now;
        if (!full)
            return digest;

        var recruited = g.ClimbRecruited;
        var survived = Saw(siege, LogicEvent.Survived);
        var given = Saw(climb, LogicEvent.Given);
        var stopped = Saw(climb, LogicEvent.Stopped);
        var gained = g.ZP(me).Points - points0;
        var parts = g.ZP(me).Parts.Values.Sum() - parts0;
        var ammo = g.ZPowerUps.Any(p => p.Kind == ZPowerKind.MaxAmmo && Vector3.Distance(p.At, g.ClimbTop) < 3f);
        var stillOn = g.Horde.Count(z => z.State == ZState.Scaling);
        ZLine($"  bell: started {Saw(climb, LogicEvent.Started)} (the round {extra:+0;-0} zombies), challenge started {Saw(siege, LogicEvent.Started)}; {recruited} recruited from the windows, {g.ClimbJoined} reached the face, "
              + $"at most {maxOn} on it ({maxSlots} places in the pyramid, {maxPile:0.0} m high); first over the parapet after {reachedAt:0.0} s ({g.ClimbOver} over, reached event {Saw(climb, LogicEvent.Reached)}); {g.ClimbFell} fell");
        ZLine($"  {fallNote}");
        ZLine($"  end: survived {survived}, reward given {given} (+{gained} points, +{parts} parts, Max Ammo on the belfry {ammo}), climb stopped {stopped}, {stillOn} still on the face, {g.ClimbReturned} sent back to the windows");
        ZLine($"  climb tick: {tickMs / Math.Max(1, tickN):0.000} ms a tick mean over {tickN} ticks mid-siege ({g.AliveZombies} alive), {allocAfter - allocBefore} bytes allocated");
        if (!Saw(climb, LogicEvent.Started) || !Saw(siege, LogicEvent.Started) || extra < c.ExtraFor(10) - 1)
            Fail($"{map.Name}: ringing the bell didn't start the siege (climb {Saw(climb, LogicEvent.Started)}, challenge {Saw(siege, LogicEvent.Started)}, {extra} zombies added)");
        if (recruited < 20 || g.ClimbJoined < 15 || maxPile < 2f || reachedAt < 0 || !Saw(climb, LogicEvent.Reached))
            Fail($"{map.Name}: the horde didn't climb the tower ({recruited} recruited, {g.ClimbJoined} on the face, pile {maxPile:0.0} m, first over {reachedAt:0.0} s)");
        if (!survived || !given || gained < c.RewardFor(10) || parts < 1 || !ammo)
            Fail($"{map.Name}: surviving the siege didn't pay (survived {survived}, given {given}, +{gained} points, +{parts} parts, Max Ammo {ammo})");
        if (!stopped || stillOn > 0 || g.ClimbActive)
            Fail($"{map.Name}: the siege didn't end cleanly (stopped {stopped}, {stillOn} still on the face)");
        if (allocAfter - allocBefore > 0)
            Fail($"{map.Name}: the siege's ticks allocate ({allocAfter - allocBefore} bytes in 2 s)");
        if (tickMs / Math.Max(1, tickN) > 4)
            Fail($"{map.Name}: the siege's ticks are too slow ({tickMs / Math.Max(1, tickN):0.00} ms mean)");

        // A client's copy: a zombie on the face reads back climbing, at its height.
        using (var g2 = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
        {
            g2.SelfTestBeginRound(10, now);
            var z = g2.SelfTestSpawnZombie(zm.Spawners.FindIndex(s => s.Window >= 0), now);
            z.State = ZState.Scaling;
            z.Window = -1;
            z.Sim.Position = new Vector3(-29f, 20.5f, -26.5f);
            z.Yaw = MathF.PI;
            var w = new PacketWriter();
            g2.WriteZEnts(w);
            var ents = w.Span.ToArray();
            var w2 = new PacketWriter();
            g2.WriteZWorld(w2);
            using var cl = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map);
            cl.SelfTestReadZombies(ents, w2.Span.ToArray());
            var copy = cl.Horde.FirstOrDefault(x => x.Id == z.Id);
            cl.ZombieAnimInputFor(copy ?? z, now, out var anim);
            ZLine($"  client: the climbing zombie reads back as {copy?.State} at y {copy?.Sim.Position.Y:0.00} (drawn {anim.Mode})");
            if (copy == null || copy.State != ZState.Scaling || MathF.Abs(copy.Sim.Position.Y - 20.5f) > 0.05f || anim.Mode != ZAnimMode.Scaling)
                Fail($"{map.Name}: a client doesn't see a zombie climbing the tower as climbing ({copy?.State}, y {copy?.Sim.Position.Y})");
        }
        nowRef = now;
        return digest;
    }
}
