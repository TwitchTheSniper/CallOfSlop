using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Self-test section "Zombie railings" (board item dev-zombies-stuck-railings-zr01): zombies on stairs, railed balconies
/// and galleries. Zombies route over the bots' nav graph through the horde's shared flow fields (Source/Game/Horde/); they
/// used to push into pews and railings their field told them to climb over, walk straight at thin railings the sight
/// rays slipped past, walk off a tower stair into the stairwell (given the landing above as their route node) and grind
/// on corners off their route's line. A small railing lab (built here):
/// <list type="bullet">
/// <item>the body-wide straight test (<see cref="HordeController{T}.WalkClear"/>) sees a top rail between the two sight
/// rays and a gap between two posts too narrow for a body, and refuses a line with no floor under it;</item>
/// <item>up: zombies on the ground below the player's railed balcony walk round a pew, up the stairs and along the balcony
/// to the player in time, with no stuck spell, every one by the stairs, the same twice, allocating nothing;</item>
/// <item>down: from the balcony to a player on the ground below it, by the stairs again (never over the railing);</item>
/// <item>the stuck watch: a zombie shut in a small room goes through every stage, its spot goes in the stuck-spot report,
/// and it is never nudged through a wall.</item>
/// </list>
/// Then Ashgrave Cathedral: up the bell tower's flights round the open stairwell to the Roof Walk, and through the nave's
/// pews up to the Organ Loft gallery, in time and without falling.
/// </summary>
public static partial class SelfTest
{
    // The lab: a balcony (deck at 3.2 m) along a wall, a top rail 0.8-1.1 m above the deck with posts every 1.5 m (the two
    // sight rays at 0.6 and 1.3 m see through it), stairs at its east end (their open side railed), a 1.1 m pew on the
    // ground in the way, two posts 0.6 m apart, and a small shut room.
    private const string RailingLabJson = """
        { "name": "Railing Lab", "halfX": 24, "halfZ": 24, "ops": [
          { "op": "Ground", "color": [0.4, 0.4, 0.4] },
          { "op": "Block", "x0": -24, "y0": 0, "z0": 9, "x1": 24, "y1": 6, "z1": 9.4, "color": "#707070" },
          { "op": "Block", "x0": -6, "y0": 3.0, "z0": 6, "x1": 12, "y1": 3.2, "z1": 9, "color": "#808080" },
          { "op": "StairsX", "z0": 6, "z1": 7.6, "x": 15.6, "dir": -1, "y0": 0, "height": 3.2, "color": "#606060" },
          { "op": "Collider", "x0": -6, "y0": 4.0, "z0": 5.94, "x1": 12, "y1": 4.3, "z1": 6.06 },
          { "op": "Collider", "x0": -6.06, "y0": 3.2, "z0": 5.94, "x1": -5.94, "y1": 4.3, "z1": 9 },
          { "op": "Collider", "x0": -4.56, "y0": 3.2, "z0": 5.94, "x1": -4.44, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": -3.06, "y0": 3.2, "z0": 5.94, "x1": -2.94, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": -1.56, "y0": 3.2, "z0": 5.94, "x1": -1.44, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": -0.06, "y0": 3.2, "z0": 5.94, "x1": 0.06, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 1.44, "y0": 3.2, "z0": 5.94, "x1": 1.56, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 2.94, "y0": 3.2, "z0": 5.94, "x1": 3.06, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 4.44, "y0": 3.2, "z0": 5.94, "x1": 4.56, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 5.94, "y0": 3.2, "z0": 5.94, "x1": 6.06, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 7.44, "y0": 3.2, "z0": 5.94, "x1": 7.56, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 8.94, "y0": 3.2, "z0": 5.94, "x1": 9.06, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 10.44, "y0": 3.2, "z0": 5.94, "x1": 10.56, "y1": 4.0, "z1": 6.06 },
          { "op": "Collider", "x0": 11.94, "y0": 3.2, "z0": 5.94, "x1": 12.06, "y1": 4.3, "z1": 6.06 },
          { "op": "Collider", "x0": 12, "y0": 0, "z0": 5.94, "x1": 15.6, "y1": 4.3, "z1": 6.0 },
          { "op": "Block", "x0": 2, "y0": 0, "z0": -1, "x1": 2.6, "y1": 1.1, "z1": 4.6, "color": "#5b3a20" },
          { "op": "Collider", "x0": -10.42, "y0": 0, "z0": -5.06, "x1": -10.3, "y1": 1.2, "z1": -4.94 },
          { "op": "Collider", "x0": -9.7, "y0": 0, "z0": -5.06, "x1": -9.58, "y1": 1.2, "z1": -4.94 },
          { "op": "Block", "x0": -16.2, "y0": 0, "z0": -11.2, "x1": -16, "y1": 2.5, "z1": -8.8, "color": "#707070" },
          { "op": "Block", "x0": -14, "y0": 0, "z0": -11.2, "x1": -13.8, "y1": 2.5, "z1": -8.8, "color": "#707070" },
          { "op": "Block", "x0": -16, "y0": 0, "z0": -11.2, "x1": -14, "y1": 2.5, "z1": -11, "color": "#707070" },
          { "op": "Block", "x0": -16, "y0": 0, "z0": -9, "x1": -14, "y1": 2.5, "z1": -8.8, "color": "#707070" },
          { "op": "Spawn", "x": -10, "z": -14 }, { "op": "Spawn", "x": 10, "z": -14 },
          { "op": "AutoWaypoints", "spacing": 2.5 },
          { "op": "Waypoint", "x": -15, "z": -10, "y": 0 },
          { "op": "Waypoint", "x": -4, "z": 7.5, "y": 3.2 }, { "op": "Waypoint", "x": 0, "z": 7.5, "y": 3.2 },
          { "op": "Waypoint", "x": 4, "z": 7.5, "y": 3.2 }, { "op": "Waypoint", "x": 8, "z": 7.5, "y": 3.2 },
          { "op": "Waypoint", "x": 11.5, "z": 6.8, "y": 3.2 }, { "op": "Waypoint", "x": 13.8, "z": 6.8, "y": 1.6 },
          { "op": "Waypoint", "x": 16.4, "z": 6.8, "y": 0 } ],
          "zombies": { "start": "Lab", "areas": [ { "name": "Lab", "rect": [-20, -20, 20, 8.9], "y": [-1, 6] } ],
            "windows": [ { "area": "Lab", "box": [-0.8, 0.9, -20.3, 0.8, 2.1, -20], "outside": [0, -21.4], "inside": [0, -19.1], "boards": 6 } ],
            "spawners": [ { "window": 0, "at": [0, -22.5] } ] } }
        """;

    private static void CheckZombieRailings()
    {
        ZNotes.Clear();
        try
        {
            CheckZombieRailingsBody();
        }
        finally
        {
            Line("::warning title=Zombie railings (results)::" + string.Join("%0A", ZNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
            ZNotes.Clear();
            World.SetMap(Maps.All[0]);
        }
    }

    private static void CheckZombieRailingsBody()
    {
        Map lab;
        try
        {
            lab = MapFile.Load(RailingLabJson, "railing-lab.json");
        }
        catch (MapFileException ex)
        {
            Fail($"the railing lab doesn't load: {ex.Message}");
            return;
        }
        lab.Validate();
        World.SetMap(lab);

        // ---- The straight test: a top rail, a gap between posts, the floor under the line ----
        Vector3 deck = new(-0.75f, 3.2f, 7f), air = new(-0.75f, 3.2f, 4f); // across the railing between two posts
        Vector3 gateA = new(-10f, 0, -3f), gateB = new(-10f, 0, -7f); // between the two posts 0.6 m apart
        bool sightRail = NavGraph.Clear(deck, air), bodyRail = HordeController<Zombie>.WalkClear(deck, air);
        bool sightGate = NavGraph.Clear(gateA, gateB), bodyGate = HordeController<Zombie>.WalkClear(gateA, gateB);
        var alongDeck = HordeController<Zombie>.WalkClear(new Vector3(-4, 3.2f, 7.5f), new Vector3(8, 3.2f, 7.5f));
        var offEdge = HordeController<Zombie>.WalkClear(new Vector3(17.5f, 0, 2f), new Vector3(17.5f, 0, -21f)); // (control: open ground)
        var intoAir = HordeController<Zombie>.WalkClear(new Vector3(11f, 3.2f, 8.5f), new Vector3(16.5f, 3.2f, 8.5f)); // off the balcony's end past the stairs
        ZLine($"railing lab: the sight rays see through the top rail {sightRail} and the 0.6 m gap between posts {sightGate}; the body test: rail {bodyRail}, gap {bodyGate}, along the deck {alongDeck}, open ground {offEdge}, off the balcony's end into the air {intoAir}");
        if (!sightRail || bodyRail)
            Fail("the body-wide straight test should see a top rail the two sight rays pass under and over");
        if (!sightGate || bodyGate)
            Fail("the body-wide straight test should see a gap between posts too narrow for a body (its side rays)");
        if (!alongDeck || !offEdge)
            Fail("the body-wide straight test should let a zombie walk along the open balcony and across open ground");
        if (intoAir)
            Fail("the body-wide straight test should refuse a line with no floor under it");

        // ---- Up: from the ground under the balcony, round the pew, up the stairs, along the balcony ----
        var now = 0.0;
        var top = new Vector3(-4f, 3.2f, 7.6f);
        var ground = new[] { new Vector3(-3f, 0, 2f), new Vector3(-1f, 0, 0f), new Vector3(0f, 0, 3.5f) };
        var up = RailingRun(lab, top, ground, ref now, 30);
        var again = RailingRun(lab, top, ground, ref now, 30);
        ZLine($"  up: {ground.Length} zombies from the ground under the balcony (a pew in the way) to the player at its far end: all there in {Secs(up.Seconds)} (allowed 25), {up.Stuck} stuck spell(s), {up.Stairs} by the stairs; replay {up.Digest:x16} / {again.Digest:x16}; {up.Alloc} bytes allocated after the first second");
        if (up.Seconds > 25)
            Fail($"railing lab: zombies should get from the ground up the stairs and along the railed balcony within 25 s ({Secs(up.Seconds)})");
        if (up.Stuck > 0)
            Fail($"railing lab: zombies got stuck {up.Stuck} time(s) on the way up (stairs, railing, pew)");
        if (up.Stairs < ground.Length)
            Fail($"railing lab: every zombie should go up by the stairs ({up.Stairs} of {ground.Length})");
        if (up.Digest != again.Digest)
            Fail("railing lab: the same seeded run played out differently");
        if (up.Alloc > 0)
            Fail($"railing lab: walking up the stairs and along the balcony allocated {up.Alloc} bytes");

        // ---- Down: from the balcony to the player on the ground below it, by the stairs (not over the railing) ----
        var onDeck = new[] { new Vector3(-4f, 3.2f, 7.6f), new Vector3(-2f, 3.2f, 8.2f), new Vector3(0f, 3.2f, 7.2f) };
        var down = RailingRun(lab, new Vector3(-3f, 0, 2.5f), onDeck, ref now, 30);
        ZLine($"  down: {onDeck.Length} zombies from the balcony to the player on the ground below it: all there in {Secs(down.Seconds)} (allowed 25), {down.Stuck} stuck spell(s), {down.Stairs} by the stairs (none over the railing)");
        if (down.Seconds > 25)
            Fail($"railing lab: zombies should come down the stairs to a player below the balcony within 25 s ({Secs(down.Seconds)})");
        if (down.Stuck > 0)
            Fail($"railing lab: zombies got stuck {down.Stuck} time(s) on the way down");
        if (down.Stairs < onDeck.Length)
            Fail($"railing lab: every zombie should come down by the stairs, not over the railing ({down.Stairs} of {onDeck.Length})");

        StuckWatchStages(lab, ref now);
        CathedralClimbs(ref now);
    }

    private static string Secs(double s) => s < double.MaxValue ? $"{s:0.0} s" : "never";

    /// <summary>
    /// The player held at <paramref name="target"/>, zombies (running) from <paramref name="from"/>: the seconds until the
    /// last is within 2.5 m (MaxValue if not within <paramref name="seconds"/>), their stuck spells, how many crossed the
    /// stairs, a digest of the run and the bytes allocated in its ticks after the first second.
    /// </summary>
    private static (double Seconds, int Stuck, int Stairs, ulong Digest, long Alloc) RailingRun(Map lab, Vector3 target, Vector3[] from, ref double now, double seconds)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, setup: _ => World.SetMap(lab));
        NavGraph.Pump(double.MaxValue);
        var zm = lab.Zombies!;
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        me.Teleport(target);
        var at = now;
        var list = from.Select(p => ZPlace(g, zm, p, 0, ZSpeed.Run, at)).ToList();
        var stairs = new bool[list.Count];
        var spells0 = g.ZStuckSpells;
        var done = double.MaxValue;
        var digest = 14695981039346656037UL;
        long alloc = 0;
        var t0 = now;
        for (var t = 0; t < seconds * 60; t++)
        {
            now += World.TickDt;
            me.Teleport(target);
            var a0 = GC.GetAllocatedBytesForCurrentThread();
            g.SelfTestZombiesTick(now);
            if (t >= 60)
                alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
            var all = true;
            for (var i = 0; i < list.Count; i++)
            {
                var p = list[i].Position;
                if (p.X > 12.2f && p.X < 15.6f && p.Z > 5.9f && p.Z < 7.7f && p.Y > 0.5f && p.Y < 3.0f)
                    stairs[i] = true;
                all &= Vector3.Distance(p, me.Position) < 2.5f;
                digest = (digest ^ (uint)BitConverter.SingleToInt32Bits(p.X) ^ ((ulong)(uint)BitConverter.SingleToInt32Bits(p.Z) << 32)) * 1099511628211UL;
            }
            if (all)
            {
                done = now - t0;
                break;
            }
        }
        return (done, g.ZStuckSpells - spells0, stairs.Count(s => s), digest, alloc);
    }

    /// <summary>A zombie shut in a small room short of its target: every stage of the stuck watch, the spot in the stuck-spot report, never nudged through a wall.</summary>
    private static void StuckWatchStages(Map lab, ref double now)
    {
        using var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, setup: _ => World.SetMap(lab));
        NavGraph.Pump(double.MaxValue);
        var zm = lab.Zombies!;
        var me = g.Local!;
        me.ImmuneUntil = 1e9;
        var target = new Vector3(-10f, 0, -10f);
        me.Teleport(target);
        var spots0 = StuckSpots.Session.On(lab.Name).Count(s => s.Modes.Contains(Game.ZombieStuckMode));
        var z = ZPlace(g, zm, new Vector3(-14.6f, 0, -10.2f), 0, ZSpeed.Walk, now);
        var maxStage = 0;
        var outside = false;
        for (var t = 0; t < 60 * 7; t++)
        {
            now += World.TickDt;
            me.Teleport(target);
            g.SelfTestZombiesTick(now);
            maxStage = Math.Max(maxStage, z.StuckStage);
            var p = z.Position;
            outside |= p.X < -16f || p.X > -14f || p.Z < -11f || p.Z > -9f;
        }
        var spots = StuckSpots.Session.On(lab.Name).Count(s => s.Modes.Contains(Game.ZombieStuckMode)) - spots0;
        ZLine($"  stuck watch: a zombie shut in a small room: furthest stage {maxStage}, side-steps {g.ZStuckSideSteps}, back to its route {g.ZStuckBacks}, spells {g.ZStuckSpells}, nudges {g.ZStuckNudges}; {spots} spot(s) in the stuck-spot report (\"{Game.ZombieStuckMode}\"); got out through a wall {outside}");
        if (maxStage < 4 || g.ZStuckSpells < 1 || g.ZStuckSideSteps < 2 || g.ZStuckBacks < 2)
            Fail("stuck watch: a zombie that can't get anywhere should go through every stage (side-step, back to its route, the other side, a nudge)");
        if (spots < 1)
            Fail("stuck watch: a zombie stuck on the map's geometry should put its spot in the stuck-spot report");
        if (outside)
            Fail("stuck watch: a nudge should never move a zombie through a wall");
    }

    /// <summary>Ashgrave Cathedral: up the bell tower's flights to the Roof Walk, and through the nave's pews up to the Organ Loft gallery.</summary>
    private static void CathedralClimbs(ref double now)
    {
        if (Maps.Find("Ashgrave Cathedral") is not { Zombies: { } zm } map)
        {
            Fail("Ashgrave Cathedral is missing");
            return;
        }
        foreach (var (name, from, target, allowed) in new[]
                 {
                     ("the tower stairs (the Nave to the Roof Walk)", new[] { new Vector3(-27.5f, 12f, -15f), new Vector3(-25.5f, 12f, -12.5f) }, "Roof Walk", 30.0),
                     ("the nave's pews to the Organ Loft gallery", new[] { new Vector3(-12f, 12f, -1.5f), new Vector3(-6.3f, 12f, -5.3f), new Vector3(-13.9f, 12f, -6.5f) }, "Organ Loft", 30.0),
                 })
            using (var g = ZGame(0, ref now, tweak: r => r.FirstRoundDelay = 9999, onMap: map))
            {
                for (var d = 0; d < zm.Doors.Count; d++)
                    g.OpenDoor(d, null);
                var me = g.Local!;
                me.ImmuneUntil = 1e9;
                var spot = FreeSpot(map, zm.Areas[zm.AreaIndex(target)]);
                me.Teleport(spot);
                var at = now;
                var list = from.Select(p => ZPlace(g, zm, p, zm.AreaIndex("Nave"), ZSpeed.Run, at)).ToList();
                var t0 = now;
                var done = double.MaxValue;
                var falls = 0;
                for (var t = 0; t < allowed * 60; t++)
                {
                    now += World.TickDt;
                    me.Teleport(spot);
                    g.SelfTestZombiesTick(now);
                    foreach (var z in list)
                        if (z.Sim.Velocity.Y < -7f)
                            falls++; // (falling faster than a 2.5 m drop ends: off a stair or a gallery)
                    if (list.All(z => Vector3.Distance(z.Position, me.Position) < 2.5f))
                    {
                        done = now - t0;
                        break;
                    }
                }
                var left = list.Where(z => Vector3.Distance(z.Position, me.Position) >= 2.5f).Select(z => $"({z.Position.X:0.0},{z.Position.Y:0.0},{z.Position.Z:0.0})").ToList();
                ZLine($"  {map.Name}, {name}: {list.Count - left.Count} of {list.Count} there in {Secs(done)} (allowed {allowed:0}), falling ticks {falls}, stuck spells {g.ZStuckSpells}{(left.Count > 0 ? "; left at " + string.Join(", ", left) : "")}");
                if (left.Count > 0)
                    Fail($"{map.Name}: zombies should get through {name} within {allowed:0} s (left at {string.Join(", ", left)})");
                if (falls > 0)
                    Fail($"{map.Name}: zombies fell off {name} ({falls} falling ticks)");
            }
    }
}
