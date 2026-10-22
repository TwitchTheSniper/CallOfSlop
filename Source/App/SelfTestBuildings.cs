using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Brutal.RakNetApi;

namespace CallOfSlop;

/// <summary>
/// The buildings system (Source/Buildings, Source/Maps/MapBuildings.cs, Source/Game/GameBuildings.cs): every building in
/// the Buildings folder builds in every variant and turn, deterministically; its rooms all join up from the front door;
/// furniture stays in its room, out of walls and out of doorways; window glass stops bodies but not bullets and breaks;
/// doors open and shut (players and bots), block while shut, break from rifle fire and grenades and stay open; door and
/// window state replicates (events and the join-in-progress state); no foliage grows inside; bots can reach every room
/// and climb every staircase on the Building Yard; maps without buildings are unchanged; and what it all costs.
/// (Building Yard also plays the 90 s bot match in "Matches", like every map: kills, no stuck bots.)
/// </summary>
public static partial class SelfTest
{
    private static void CheckBuildings()
    {
        var defs = Buildings.All;
        Line($"library: {defs.Length} buildings, {Buildings.Rules.Count} shared furnishing rules, {Buildings.LoadErrors.Count} load error(s)");
        foreach (var e in Buildings.LoadErrors)
            Fail($"building file: {e}");
        if (defs.Length < 10)
            Fail($"only {defs.Length} buildings (the starter set has 10)");
        foreach (var cat in new[] { "residential", "commercial", "industrial" })
            if (!defs.Any(d => d.Category == cat))
                Fail($"no {cat} building");

        CheckBuildingPlans(defs);
        if (Maps.Find("Building Yard") is not { } yard)
        {
            Fail("Maps/Dev/BuildingYard.json ('Building Yard') didn't load");
            return;
        }
        CheckBuildingYard(yard);
        CheckBuildingWindows(yard);
        CheckBuildingDoors(yard);
        CheckBuildingState(yard);
        CheckBuildingNav(yard);
        // Maps without Building ops are unchanged: no building boxes, no building fingerprint (the pinned fingerprints
        // of every shipped map are checked in "Pack assets").
        var plain = Maps.All.Where(m => m.PlacedBuildings.Count == 0).ToList();
        var touched = plain.Count(m => m.BuildingHash != 0 || Enumerable.Range(0, m.Solid.Count).Any(m.IsBuildingBox));
        Line($"{plain.Count} maps without buildings: {touched} with building boxes or a building fingerprint");
        if (touched > 0)
            Fail($"{touched} maps without Building ops have building state");
    }

    /// <summary>Every building, every variant: builds, joins up, furniture placement rules, determinism, cost.</summary>
    private static void CheckBuildingPlans(BuildingDef[] defs)
    {
        var scope = Props.Global;
        var totalMs = 0.0;
        var builds = 0;
        foreach (var d in defs)
        {
            var variants = new List<(BuildingVariant V, int Seed)> { (new BuildingVariant(), 0), (new BuildingVariant { Mirror = true }, 0), (new BuildingVariant(), 7), (new BuildingVariant(), 12345) };
            foreach (var (name, _) in d.Palettes.Skip(1))
                variants.Add((new BuildingVariant { Palette = name }, 3));
            if (d.MaxFloors > d.MinFloors)
            {
                variants.Add((new BuildingVariant { Floors = d.MinFloors }, 0));
                variants.Add((new BuildingVariant { Floors = Math.Min(d.MaxFloors, d.MinFloors + 3) }, 0));
            }
            variants.Add((new BuildingVariant { Furnish = false, Doors = "open" }, 0));
            BuildingPlan? first = null;
            var problems = new List<string>();
            foreach (var (v, seed) in variants)
                for (var q = 0; q < 4; q++)
                {
                    var label = $"{d.Name} (palette {v.Palette ?? "default"}, floors {(v.Floors > 0 ? v.Floors : d.DefaultFloors)}, mirror {v.Mirror}, seed {seed}, turn {q * 90})";
                    BuildingPlan plan;
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        plan = BuildingGen.Generate(d, v, seed, new Vector3(5, 0, -3), q, scope);
                    }
                    catch (BuildingFileException ex)
                    {
                        problems.Add($"{label}: {ex.Message}");
                        continue;
                    }
                    totalMs += sw.Elapsed.TotalMilliseconds;
                    builds++;
                    first ??= plan;
                    problems.AddRange(PlanProblems(plan, label, v.Furnish));
                }
            foreach (var p in problems.Take(12))
                Fail(p);
            if (problems.Count > 12)
                Fail($"{d.Name}: {problems.Count - 12} more problem(s)");
            if (first == null)
                continue;
            // Deterministic: the same building, seed and placement gives exactly the same plan; another seed other furniture.
            var again = BuildingGen.Generate(d, null, 0, new Vector3(5, 0, -3), 0, scope);
            var other = BuildingGen.Generate(d, null, 999, new Vector3(5, 0, -3), 0, scope);
            var same = PlanHash(first) == PlanHash(again);
            var differs = PlanHash(other) != PlanHash(first);
            var solid = first.Props.Count(p => p.Def.IsSolid && p.Room != "exterior");
            Line($"  {d.Name,-18} {d.Category,-11} {first.Floors} floor(s) {first.Rooms.Count,2} rooms {first.Shell.Count,4} boxes {first.Doors.Count,2} doors {first.Windows.Count,2} windows "
                 + $"{first.Props.Count(p => p.Room != "exterior"),3} furniture ({solid} solid), {first.Waypoints.Count} waypoints, {first.Notes.Count} room note(s); "
                 + $"{variants.Count * 4} variants/turns build; same again {same}, another seed differs {differs}");
            if (!same)
                Fail($"{d.Name}: two builds with the same seed differ");
            if (!differs && first.Props.Count(p => p.Room != "exterior") > 3)
                Fail($"{d.Name}: another seed gave exactly the same furniture");
            if (first.Props.Count(p => p.Room != "exterior") < first.Rooms.Count)
                Fail($"{d.Name}: only {first.Props.Count(p => p.Room != "exterior")} pieces of furniture in {first.Rooms.Count} rooms (furnish every room)");
        }
        Line($"generation: {builds} plans in {totalMs:0} ms, {totalMs / Math.Max(1, builds):0.00} ms per building (geometry + furniture)");
        if (builds > 0 && totalMs / builds > 25)
            Fail($"building generation takes {totalMs / builds:0.0} ms per building (budget 25 ms)");
    }

    private static uint PlanHash(BuildingPlan p)
    {
        var h = 2166136261u;
        void F(float f)
        {
            h ^= BitConverter.SingleToUInt32Bits(MathF.Round(f * 1000f) / 1000f);
            h *= 16777619u;
        }
        foreach (var b in p.Shell)
        {
            F(b.Box.Center.X); F(b.Box.Center.Y); F(b.Box.Center.Z); F(b.Box.Half.X); F(b.Box.Half.Y); F(b.Box.Half.Z);
        }
        foreach (var pr in p.Props)
        {
            F(pr.Position.X); F(pr.Position.Y); F(pr.Position.Z); F(pr.Yaw);
            foreach (var ch in pr.Def.Name)
                F(ch);
        }
        return h;
    }

    /// <summary>A plan's rule breaks: unreachable rooms, furniture in doorways, in walls or outside its room, stairs too steep.</summary>
    private static List<string> PlanProblems(BuildingPlan plan, string label, bool furnished)
    {
        var problems = new List<string>();
        // Rooms joined up from the front door.
        var keys = plan.Rooms.Select(r => $"{r.Floor}:{r.Id}").ToHashSet();
        if (plan.FrontRoom == null)
            problems.Add($"{label}: no door to the outside on the ground floor");
        else
        {
            var seen = new HashSet<string> { plan.FrontRoom };
            var todo = new Stack<string>();
            todo.Push(plan.FrontRoom);
            while (todo.Count > 0)
            {
                var k = todo.Pop();
                foreach (var (a, b) in plan.Links)
                {
                    var next = a == k ? b : b == k ? a : null;
                    if (next != null && next != "outside" && seen.Add(next))
                        todo.Push(next);
                }
            }
            foreach (var k in keys.Where(x => !seen.Contains(x)))
                problems.Add($"{label}: room {k} can't be reached from the front door");
        }
        // Stairs: each climbs one storey (the generator keeps steps to 0.36 m; players step 0.55 m).
        foreach (var (bottom, top) in plan.Stairs)
            if (top.Y - bottom.Y < 1f || top.Y - bottom.Y > 8f)
                problems.Add($"{label}: stairs from {bottom} to {top} don't climb a storey");
        if (!furnished)
            return problems;
        // Furniture: inside its room, not in a wall (or the stairs), not in a doorway.
        var walls = plan.Shell.Select(b => b.Box).ToList();
        foreach (var p in plan.Props)
        {
            if (p.Room == "exterior")
                continue;
            var room = plan.Rooms.FirstOrDefault(r => r.Id == p.Room && p.Position.Y >= r.FloorY - 0.05f && p.Position.Y <= r.CeilingY);
            if (room == null)
            {
                problems.Add($"{label}: {p.Def.Name} has no room");
                continue;
            }
            var b = Map.WorldBounds(p.Def.Bounds, p.Position, p.Yaw, p.Scale);
            var c = b.Center;
            if (!room.Inside.Any(r => c.X >= r.Min.X - 0.4f && c.X <= r.Max.X + 0.4f && c.Z >= r.Min.Z - 0.4f && c.Z <= r.Max.Z + 0.4f))
                problems.Add($"{label}: {p.Def.Name} stands outside room \"{room.Id}\"");
            var lo = b.Min + new Vector3(0.07f, 0, 0.07f);
            var hi = b.Max - new Vector3(0.07f, 0, 0.07f);
            var y0 = MathF.Max(b.Min.Y, room.FloorY + 0.2f);
            var y1 = MathF.Min(b.Max.Y, room.CeilingY - 0.1f);
            if (p.Def.IsSolid && y1 > y0 && hi.X > lo.X && hi.Z > lo.Z)
            {
                foreach (var w in walls)
                    if (w.Min.X < hi.X && w.Max.X > lo.X && w.Min.Z < hi.Z && w.Max.Z > lo.Z && w.Min.Y < y1 && w.Max.Y > y0)
                    {
                        problems.Add($"{label}: {p.Def.Name} in room \"{room.Id}\" is in a wall or the stairs");
                        break;
                    }
                if (p.Position.Y - room.FloorY < 0.3f)
                    foreach (var z in room.DoorZones)
                        if (z.Min.X < hi.X && z.Max.X > lo.X && z.Min.Z < hi.Z && z.Max.Z > lo.Z)
                        {
                            problems.Add($"{label}: solid {p.Def.Name} stands in a doorway of room \"{room.Id}\"");
                            break;
                        }
            }
        }
        return problems;
    }

    /// <summary>The demo map: every starter building on it, foliage kept out, costs (load, boxes, draw calls).</summary>
    private static void CheckBuildingYard(Map yard)
    {
        var sw = Stopwatch.StartNew();
        var copy = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, yard.SourceFile!)), "load timing");
        var loadMs = sw.Elapsed.TotalMilliseconds;
        var names = yard.PlacedBuildings.Select(b => b.Def.Name).Distinct().ToList();
        var shell = yard.PlacedBuildings.Sum(b => b.ShellCount);
        var (_, runs) = BuildingRenderer.BuildStatic(yard);
        var props = yard.PropInstances.Count;
        var drawn = Enumerable.Range(0, yard.Solid.Count).Count(yard.IsDrawnBox);
        Line($"Building Yard: {yard.PlacedBuildings.Count} buildings ({names.Count} different), {shell} shell boxes in {runs.Length} renderer runs "
             + $"(at most {runs.Length} + 2 draw calls, 3 when everything is in view), {yard.BuildingDoors.Count} doors, {yard.BuildingWindows.Count} windows, "
             + $"{props} props, {drawn} boxes still drawn one by one; loads in {loadMs:0} ms ({loadMs / Math.Max(1, yard.PlacedBuildings.Count):0.0} ms per building), "
             + $"fingerprint {yard.ContentHash:x8}, same on a second load {copy.ContentHash == yard.ContentHash}");
        foreach (var d in Buildings.All)
            if (!names.Contains(d.Name) && !d.Folder.StartsWith('_'))
                Fail($"Building Yard doesn't show {d.Name}");
        if (copy.ContentHash != yard.ContentHash)
            Fail("Building Yard's fingerprint differs between two loads");
        if (runs.Length != yard.PlacedBuildings.Count || runs.Sum(r => r.Count) != shell)
            Fail("the building renderer's runs don't cover every shell box");
        if (Enumerable.Range(0, yard.Solid.Count).Any(i => yard.IsBuildingBox(i) && yard.IsDrawnBox(i)))
            Fail("a building box is also drawn as a map box (one draw call each)");
        // Building-local props reach the prop renderer's library.
        var lib = yard.PropLibrary;
        var local = yard.PropInstances.Where(p => p.Def.Pack?.StartsWith("Buildings/") == true).Select(p => p.Def).Distinct().ToList();
        var batches = PropBatches.Build(yard, PropMeshes.Build(lib));
        Line($"building-local props on it: {local.Count} kinds ({string.Join(", ", local.Select(p => p.Name))}); render batches cover {batches.Count} of {yard.PropInstances.Count} props");
        if (local.Count == 0 || local.Any(p => !lib.Contains(p)) || batches.Count != yard.PropInstances.Count)
            Fail("building-local props are missing from the prop renderer's library");
        // No foliage inside a building (roofed or not).
        var inside = yard.FoliageInstances.Count(f => yard.InsideBuilding(f.Position.X, f.Position.Z));
        Line($"foliage: {yard.FoliageInstances.Length} plants, {inside} inside a building; {yard.FoliageStats}");
        if (yard.FoliageInstances.Length == 0)
            Fail("Building Yard has no foliage (it should have grass and trees outside)");
        if (inside > 0)
            Fail($"{inside} plants grow inside buildings");
        // Spawns: none ended up inside a wall or a piece of furniture.
        var blocked = yard.Spawns.Count(s => yard.IsBlocked(s));
        if (blocked > 0)
            Fail($"{blocked} Building Yard spawns are inside geometry");
    }

    /// <summary>A whole window at least 1.2 m wide on an exterior ground-floor wall of the first building called <paramref name="building"/>.</summary>
    private static int FindWindow(Map map, string building)
    {
        foreach (var b in map.PlacedBuildings.Where(b => b.Def.Name == building))
            for (var i = b.WindowStart; i < b.WindowStart + b.WindowCount; i++)
            {
                var w = map.BuildingWindows[i];
                var g = w.Plan.Glass;
                if (!w.Broken && w.Plan.Exterior && w.Plan.Floor == 0 && MathF.Max(g.Half.X, g.Half.Z) * 2 >= 1.15f)
                    return i;
            }
        return -1;
    }

    /// <summary>Window glass: bodies stop, bullets pass and break it, the hole can be climbed through; the break replicates.</summary>
    private static void CheckBuildingWindows(Map yard)
    {
        World.SetMap(yard);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id);
        var me = game.Players[0];
        var i = FindWindow(yard, "Small House");
        if (i < 0)
        {
            Fail("no wide ground-floor window on the Small House");
            return;
        }
        var w = yard.BuildingWindows[i];
        var g = w.Plan.Glass;
        var mid = g.Center;
        var n = g.Half.X < g.Half.Z ? Vector3.UnitX : Vector3.UnitZ; // through the glass, from outside in
        if (Vector3.Dot(mid - yard.PlacedBuildings[w.Building].Plan.Bounds.Center, n) > 0)
            n = -n;
        var from = mid - n * 1.5f;
        var bodyAt = new Vector3(mid.X, g.Min.Y + 0.05f, mid.Z);
        var bodyH = MathF.Min(1.0f, g.Max.Y - g.Min.Y - 0.1f);
        var bodyBlocked = World.Blocked(bodyAt, bodyH);
        var shotDist = World.Query.Raycast(from, n, 3f, QueryFilter.Shots);
        var sightDist = World.Query.Raycast(from, n, 3f, QueryFilter.All);
        Line($"window {i} ({g.Half.X * 2:0.00} x {g.Half.Y * 2:0.00} x {g.Half.Z * 2:0.00} m, {w.Health} hp): a body in it blocked {bodyBlocked}; "
             + $"a bullet from 1.5 m goes {shotDist:0.00} m (the glass is at 1.5), a body ray stops at {sightDist:0.00} m");
        if (!bodyBlocked)
            Fail("window glass doesn't block bodies");
        if (shotDist < 1.6f)
            Fail("window glass stops bullets");
        if (sightDist > 1.55f)
            Fail("window glass isn't solid to bodies (the clip layer)");
        var hash = yard.ContentHash;
        game.ShotBuildings(me, from, n, shotDist, 30, 0);
        var bodyAfter = World.Blocked(bodyAt, bodyH);
        Line($"one rifle bullet: broken {w.Broken}, a body fits through the frame now {!bodyAfter}, debris pieces {PropDebris.Count}, fingerprint unchanged {yard.ContentHash == hash}");
        if (!w.Broken || bodyAfter)
            Fail("a bullet didn't shatter the window (or the empty frame still blocks)");
        if (yard.ContentHash != hash)
            Fail("breaking a window changed the map's fingerprint");
        // A broken ground-floor window is a mantle: walk at it from outside and climb through.
        var sill = w.Plan.Sill;
        var outside = new Vector3(mid.X, yard.PlacedBuildings[w.Building].Plan.BaseY, mid.Z) - n * 0.9f;
        var through = MantleThrough(outside, n, 1.6f);
        Line($"climbing through the empty frame (sill {sill:0.00} m): got through {through}");
        if (sill <= 1.2f && !through)
            Fail("a player can't climb through a broken ground-floor window");
        // A grenade shatters every window in reach that has no wall in between.
        var j = FindWindow(yard, "Corner Shop");
        if (j >= 0)
        {
            var gw = yard.BuildingWindows[j].Plan.Glass;
            var gn = gw.Half.X < gw.Half.Z ? Vector3.UnitX : Vector3.UnitZ;
            var outward = Vector3.Dot(gw.Center - yard.PlacedBuildings[yard.BuildingWindows[j].Building].Plan.Bounds.Center, gn) > 0 ? gn : -gn;
            game.BlastBuildings(me, gw.Center + outward * 2.5f, 9f, 200f, PropDamage.Explosion, 0);
            Line($"a frag grenade 2.5 m outside a shop window: broken {yard.BuildingWindows[j].Broken}");
            if (!yard.BuildingWindows[j].Broken)
                Fail("a grenade didn't break a window 2.5 m away");
        }
        yard.RestoreProps();
        if (w.Broken || World.Query.Raycast(from, n, 3f, QueryFilter.All) > 1.55f)
            Fail("a new match didn't put the window glass back");
    }

    /// <summary>Walks a player along <paramref name="dir"/> from <paramref name="start"/> (holding jump at first) for 2.5 s: did it get <paramref name="dist"/> m in?</summary>
    private static bool MantleThrough(Vector3 start, Vector3 dir, float dist)
    {
        var s = new MoveState { Position = start, Grounded = true };
        var yaw = MathF.Atan2(dir.X, -dir.Z);
        for (var t = 0; t < 150; t++)
            World.Simulate(ref s, new InputCmd { Buttons = Buttons.Forward | (t < 40 ? Buttons.Jump : Buttons.None), Yaw = yaw, SpeedPct = 100 }); // as the Courtyard window test
        return Vector3.Dot(s.Position - start, dir) >= dist;
    }

    /// <summary>The first door of a building of that name made of <paramref name="material"/> (any if null), or -1.</summary>
    private static int FindDoor(Map map, string building, DoorMaterial? material = null, bool front = false)
    {
        foreach (var b in map.PlacedBuildings.Where(b => b.Def.Name == building))
            for (var i = b.DoorStart; i < b.DoorStart + b.DoorCount; i++)
            {
                var d = map.BuildingDoors[i];
                if ((material == null || d.Plan.Material == material) && (!front || d.Plan.Front) && !d.Open && !d.Broken && d.Plan.Floor == 0)
                    return i;
            }
        return -1;
    }

    /// <summary>Doors: shut they block bodies and bullets; E opens and shuts them; they won't shut on you; bots open them; damage breaks them.</summary>
    private static void CheckBuildingDoors(Map yard)
    {
        World.SetMap(yard);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id);
        var me = game.Players[0];
        me.Alive = true;
        var i = FindDoor(yard, "Small House", DoorMaterial.Wood, front: true);
        if (i < 0)
        {
            Fail("no shut front door on the Small House");
            return;
        }
        var d = yard.BuildingDoors[i];
        var outside = d.Plan.OtherSide;
        var inside = d.Plan.SwingSide;
        var eyeOut = outside + new Vector3(0, 1.2f, 0);
        var across = Vector3.Normalize(inside - outside);
        var centre = new Vector3(d.Center.X, d.Plan.Closed.Min.Y + 0.01f, d.Center.Z);
        bool Blocked() => World.Blocked(centre, 1.7f);
        float Shot() => World.Query.Raycast(eyeOut, across, 3f, QueryFilter.Shots);
        var shutBlocks = Blocked();
        var shutShot = Shot();
        me.Position = outside;
        game.LookYaw = MathF.Atan2(across.X, -across.Z);
        game.LookPitch = -0.35f;
        var aimed = game.AimedDoor();
        var hint = game.DoorHint();
        var opened = game.ServerUseDoor(me, i, 0);
        var openBlocks = Blocked();
        var openShot = Shot();
        Line($"door {i} ({d.Plan.Material}, {d.Plan.Health} hp, {d.Plan.Width:0.00} x {d.Plan.Height:0.00} m): shut it blocks a body {shutBlocks} and a bullet stops at {shutShot:0.00} m (door at 0.9); "
             + $"looking at it from outside picks door {aimed} (hint \"{hint}\"); E opens it {opened}: blocks a body {openBlocks}, a bullet goes {openShot:0.00} m");
        if (!shutBlocks || shutShot > 1.0f)
            Fail("a shut door doesn't block bodies and bullets");
        if (aimed != i || hint != "open the door")
            Fail($"looking at the door from 0.9 m picked door {aimed} with hint \"{hint}\"");
        if (!opened || !d.Open || openBlocks || openShot < 1.5f)
            Fail("the interact key didn't open the door (or an open door still blocks)");
        // It won't shut on someone standing in the doorway; it does once they step out.
        me.Position = centre;
        var shutOnMe = game.ServerSetDoor(i, false, 0);
        me.Position = outside;
        var shutAgain = game.ServerUseDoor(me, i, 0);
        Line($"shutting it on a player in the doorway {shutOnMe} (refused), once they stepped out {shutAgain}; blocks again {Blocked()}");
        if (shutOnMe || !shutAgain || !Blocked())
            Fail("a door shut on a player in the doorway, or didn't shut afterwards");
        // A bot walking into a shut door opens it.
        var botOpen = typeof(Game).GetMethod("BotOpenDoors", BindingFlags.NonPublic | BindingFlags.Instance);
        me.Position = outside + across * 0.45f;
        botOpen?.Invoke(game, [me, 0.0]);
        Line($"a bot pressed against it opens it {d.Open}");
        if (botOpen == null || !d.Open)
            Fail("a bot walking into a shut door didn't open it");
        game.ServerSetDoor(i, false, 0);
        // Rifle bullets wear it down: 150 hp is 5 hits of 30; the doorway is open for good after.
        me.Position = outside - across * 2f;
        var hits = 0;
        while (!d.Broken && hits < 30)
        {
            game.ShotBuildings(me, eyeOut - across, across, Shot() + 1f, 30, 0);
            hits++;
        }
        var reshut = yard.SetBuildingDoor(i, false);
        Line($"rifle hits of 30 to break it: {hits} (expected {(d.Plan.Health + 29) / 30}); broken {d.Broken}, the doorway stays open (shut again {reshut}), a bullet goes {Shot():0.00} m");
        if (!d.Broken || hits != (d.Plan.Health + 29) / 30 || reshut || Blocked())
            Fail($"a {d.Plan.Health} hp door broke after {hits} hits of 30, or a broken doorway still blocks");
        // One frag grenade at the foot of a door breaks it, wood or metal.
        foreach (var (bname, mat) in new[] { ("Garage", DoorMaterial.Metal), ("Two-Storey House", DoorMaterial.Wood) })
        {
            var k = FindDoor(yard, bname, mat);
            if (k < 0)
            {
                Fail($"no shut {mat} door on the {bname}");
                continue;
            }
            var dk = yard.BuildingDoors[k];
            game.BlastBuildings(me, dk.Plan.OtherSide + new Vector3(0, 0.3f, 0) + Vector3.Normalize(dk.Plan.SwingSide - dk.Plan.OtherSide) * -0.1f, 9f, 200f, PropDamage.Explosion, 0);
            Line($"a frag grenade 1 m from the {bname}'s {mat.ToString().ToLowerInvariant()} door ({dk.Plan.Health} hp): broken {dk.Broken}");
            if (!dk.Broken)
                Fail($"one grenade didn't break a {mat} door");
        }
        // A new match: every door shut and whole again.
        yard.RestoreProps();
        Line($"new match: door {i} broken {d.Broken}, open {d.Open}, health {d.Health}, blocks {Blocked()}");
        if (d.Broken || d.Open || d.Health != d.Plan.Health || !Blocked())
            Fail("a new match didn't put the doors back");
    }

    /// <summary>Door and window state through packets: the events and the join-in-progress state onto another copy of the map.</summary>
    private static void CheckBuildingState(Map yard)
    {
        World.SetMap(yard);
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id);
        var me = game.Players[0];
        var rng = new Random(5);
        // Some doors open, some damaged, some broken; some windows broken.
        for (var i = 0; i < yard.BuildingDoors.Count; i++)
        {
            var r = rng.Next(4);
            if (r == 1)
                yard.SetBuildingDoor(i, true);
            else if (r == 2)
                yard.DamageBuildingDoor(i, 40);
            else if (r == 3 && yard.DamageBuildingDoor(i, 5000))
                yard.BreakBuildingDoor(i);
        }
        for (var i = 0; i < yard.BuildingWindows.Count; i += 3)
            yard.BreakBuildingWindow(i);
        var copy = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, yard.SourceFile!)), "client copy");
        var w = new PacketWriter();
        var packets = 0;
        var biggest = 0;
        Game.WriteBuildingState(w, yard, pw =>
        {
            packets++;
            biggest = Math.Max(biggest, pw.Length);
            var pr = new PacketReader(pw.Span[1..]);
            Game.ReadBuildingState(copy, ref pr);
        });
        var doorsSame = Enumerable.Range(0, yard.BuildingDoors.Count).All(i => copy.BuildingDoors[i].Open == yard.BuildingDoors[i].Open
            && copy.BuildingDoors[i].Broken == yard.BuildingDoors[i].Broken && copy.BuildingDoors[i].Health == yard.BuildingDoors[i].Health);
        var windowsSame = Enumerable.Range(0, yard.BuildingWindows.Count).All(i => copy.BuildingWindows[i].Broken == yard.BuildingWindows[i].Broken);
        var solidSame = Enumerable.Range(0, yard.Solid.Count).All(i => yard.Solid[i] == copy.Solid[i]);
        Line($"join in progress: {yard.BuildingDoors.Count(d => d.Open)} doors open ({yard.BuildingDoors.Count(d => d.Broken)} broken), {yard.BuildingWindows.Count(x => x.Broken)} windows broken; "
             + $"state in {packets} packet(s) (biggest {biggest} bytes): doors match {doorsSame}, windows match {windowsSame}, collision boxes match {solidSame}");
        if (!doorsSame || !windowsSame || !solidSame)
            Fail("the join-in-progress building state doesn't reproduce the doors and windows");
        if (biggest > 1400)
            Fail($"a building state packet is {biggest} bytes (over 1400)");
        // Events: open, damage, break and a window, written and applied on a third copy.
        var copy2 = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, yard.SourceFile!)), "client copy 2");
        var events = new (BuildingEventKind Kind, int Index, int Health)[] { (BuildingEventKind.DoorOpen, 0, 150), (BuildingEventKind.DoorDamage, 1, 90), (BuildingEventKind.DoorBreak, 2, 0), (BuildingEventKind.WindowBreak, 3, 0) };
        foreach (var (kind, index, health) in events)
        {
            Game.WriteBuildingEvent(w, kind, index, health, new Vector3(1, 2, 3));
            var pr = new PacketReader(w.Span[1..]);
            if (w.Span[0] != (byte)Msg.BuildingEvent || w.Length != 1 + Game.BuildingEventBytes)
                Fail($"a building event is {w.Length} bytes with id {w.Span[0]}");
            game.ApplyBuildingEvent(copy2, (BuildingEventKind)pr.U8(), pr.U16(), pr.U16(), pr.Vec());
        }
        var ok = copy2.BuildingDoors[0].Open && copy2.BuildingDoors[1].Health == 90 && copy2.BuildingDoors[2].Broken && copy2.BuildingWindows[3].Broken;
        Line($"events ({1 + Game.BuildingEventBytes} bytes each) open / damage / break a door and break a window on a client: {ok}; protocol {ServerInfo.ProtocolVersion}");
        if (!ok)
            Fail("building events don't apply on a client");
        // Garbage packets don't throw (the fuzzing in "Paste and packets" sends random bytes too).
        var junk = new byte[] { (byte)Msg.BuildingState, 0, 0xFF, 0xFF, 0xFF, 0xFF, 1 };
        var jr = new PacketReader(junk.AsSpan(1));
        Game.ReadBuildingState(copy2, ref jr);
        yard.RestoreProps();
    }

    /// <summary>Bots: the nav graph reaches every room of every building on the Building Yard from outside, and up every staircase.</summary>
    private static void CheckBuildingNav(Map yard)
    {
        World.SetMap(yard);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[4096];
        int rooms = 0, reached = 0, stairs = 0, climbed = 0, noNode = 0;
        var missing = new List<string>();
        foreach (var b in yard.PlacedBuildings)
        {
            var front = b.Plan.Doors.FirstOrDefault(d => d.Front) ?? b.Plan.Doors.FirstOrDefault();
            var startAt = front?.OtherSide ?? b.Plan.Waypoints[0];
            var start = g.Nearest(startAt, needSight: false);
            int Node(Vector3 p)
            {
                var k = g.Nearest(p, needSight: false);
                if (k < 0 || MathF.Abs(g.Nodes[k].Y - p.Y) > 0.4f || Vector2.Distance(new Vector2(g.Nodes[k].X, g.Nodes[k].Z), new Vector2(p.X, p.Z)) > 1.6f)
                    return -1;
                return k;
            }
            foreach (var r in b.Plan.Rooms)
            {
                rooms++;
                var goal = Node(r.Waypoint);
                if (goal < 0)
                {
                    noNode++;
                    missing.Add($"{b.Def.Name} #{b.Index} room {r.Floor}:{r.Id} (no nav node at its waypoint)");
                    continue;
                }
                if (start >= 0 && search.Find(g, start, goal, path, maxExpand: 60000) > 0)
                    reached++;
                else
                    missing.Add($"{b.Def.Name} #{b.Index} room {r.Floor}:{r.Id}");
            }
            foreach (var (_, top) in b.Plan.Stairs)
            {
                stairs++;
                var goal = Node(top);
                if (goal >= 0 && start >= 0 && search.Find(g, start, goal, path, maxExpand: 60000) > 0)
                    climbed++;
                else
                    missing.Add($"{b.Def.Name} #{b.Index} stairs up to {top.Y:0.0} m");
            }
        }
        Line($"bots on Building Yard: nav graph {g.Nodes.Length} nodes, {g.EdgeCount} edges ({g.BuildMs:0} ms); rooms reached from the front door {reached} of {rooms} ({noNode} without a node), "
             + $"stair tops reached {climbed} of {stairs}");
        foreach (var m in missing.Take(10))
            Line("  unreachable: " + m);
        if (g.Nodes.Length == 0)
            Fail("no nav graph for Building Yard");
        else if (reached < rooms || climbed < stairs)
            Fail($"bots can't reach {rooms - reached} room(s) and {stairs - climbed} stair top(s) on Building Yard");
    }
}
