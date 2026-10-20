using System.Numerics;
using System.Reflection;

namespace CallOfSlop;

/// <summary>
/// "Props": the prop library (every file loads and validates, unique names, the starter count, sane bounds and meshes),
/// readable errors, placement and collision (solid props block rays and movement, cosmetic ones don't, turned props
/// follow their outline), Clutter (deterministic, keeps clear of spawns, waypoints, routes, objectives and gaps),
/// fingerprints, the nav graph and bots around props, the render batches, and breakable props (each kind of damage,
/// the break event and the broken set over the network, collision gone once broken, debris, a new match restores).
/// </summary>
public static partial class SelfTest
{
    /// <summary>The starter library ships at least this many props.</summary>
    private const int StarterProps = 30;

    private static void CheckProps()
    {
        CheckPropLibrary();
        CheckPropFileErrors();
        CheckPropCollision();
        CheckClutter();
        CheckPropMapsAndNav();
        CheckBreakables();
        CheckDebris();
    }

    private static void CheckPropLibrary()
    {
        var all = Props.All;
        foreach (var e in Props.LoadErrors)
            Fail("prop file problem: " + e);
        Line($"{all.Length} props in {Props.Categories.Count()} categories ({string.Join(", ", Props.Categories)}) from {Props.Folder}: "
             + $"{all.Count(p => p.IsSolid)} solid, {all.Count(p => !p.IsSolid)} cosmetic, {all.Count(p => p.Breakable != null)} breakable "
             + $"({all.Count(p => p.Breakable is { ExplodeRadius: > 0 })} explosive)");
        if (all.Length < StarterProps)
            Fail($"only {all.Length} props loaded from {Props.Folder} (the starter library has at least {StarterProps})");
        if (all.Select(p => p.Name.ToLowerInvariant()).Distinct().Count() != all.Length)
            Fail("two props share a name");
        if (!File.Exists(Path.Combine(Props.Folder, "README.md")))
            Fail("Props/README.md (the prop authoring guide) wasn't copied next to the game");
        foreach (var p in all)
        {
            var size = p.Bounds.Half * 2;
            if (p.Parts.Count == 0 || size.X <= 0.01f || size.Y <= 0.002f || size.Z <= 0.01f || size.X > PropFile.MaxSize || size.Y > PropFile.MaxSize || size.Z > PropFile.MaxSize)
                Fail($"{p.Name}: odd bounds {size}");
            if (p.Bounds.Min.Y < -0.5f)
                Fail($"{p.Name}: sinks {-p.Bounds.Min.Y:0.00} m into the ground");
            if (p.IsSolid != p.Colliders.Length > 0)
                Fail($"{p.Name}: {(p.IsSolid ? "solid without colliders" : "cosmetic with colliders")}");
            foreach (var c in p.Colliders)
                if (c.Min.X < p.Bounds.Min.X - 0.3f || c.Max.X > p.Bounds.Max.X + 0.3f || c.Min.Z < p.Bounds.Min.Z - 0.3f || c.Max.Z > p.Bounds.Max.Z + 0.3f || c.Max.Y > p.Bounds.Max.Y + 0.3f || c.Min.Y < 0)
                    Fail($"{p.Name}: collider {c.Min}..{c.Max} reaches well outside the model {p.Bounds.Min}..{p.Bounds.Max}");
            if (p.Breakable is { } b && (b.Health <= 0 || b.BreakBy == PropDamage.None))
                Fail($"{p.Name}: breakable with no health or no damage kinds");
            if (!p.IsSolid && p.FadeDistance < 5f)
                Fail($"{p.Name}: cosmetic prop without a fade distance");
            if (p.Index < 0 || p.Index >= all.Length || all[p.Index] != p)
                Fail($"{p.Name}: wrong library index {p.Index}");
        }
        // The kinds of props the brief asked for are all there.
        foreach (var want in new[] { "Trash Can", "Dumpster", "Wheelie Bin", "Recycling Bin", "Loose Bricks", "Brick Pile", "Cinder Blocks", "Newspaper", "Newspaper Stack",
                     "Cardboard Box", "Cardboard Stack", "Wooden Crate", "Pallet", "Pallet Stack", "Oil Drum", "Traffic Cone", "Road Barrier", "Bench", "Street Lamp",
                     "Fire Hydrant", "Mailbox", "Tyre", "Tyre Stack", "Bottles", "Soda Cans", "Plastic Bag", "Sandbag Wall", "Plastic Chair", "Table", "Shopping Cart",
                     "Pipe Stack", "Plank", "Rubble Pile", "Stop Sign", "Vending Machine" })
            if (Props.Find(want) == null)
                Fail($"the starter library has no '{want}'");

        var meshes = PropMeshes.Build(all);
        var parts = all.Sum(p => p.Parts.Count);
        var bad = meshes.Indices.Count(i => i >= meshes.Vertices.Length / PropMeshes.FloatsPerVertex);
        Line($"prop meshes: {meshes.Vertices.Length / PropMeshes.FloatsPerVertex} vertices, {meshes.Triangles} triangles for {parts} parts "
             + $"(largest prop {all.Max(p => p.Parts.Count)} parts, {all.Max(p => p.Parts.Count) * 12} triangles)");
        if (meshes.Vertices.Length / PropMeshes.FloatsPerVertex != parts * 24 || meshes.Indices.Length != parts * 36 || bad > 0 || meshes.Ranges.Length != all.Length)
            Fail($"prop meshes don't add up ({bad} indices out of range)");
        if (meshes.Vertices.Any(v => !float.IsFinite(v)))
            Fail("prop meshes have a NaN");
    }

    private static void CheckPropFileErrors()
    {
        (string Json, string Expect)[] cases =
        [
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1] } ], \"colour\": \"red\" }", "unknown key"),
            ("{ \"name\": \"X\", \"parts\": [ { \"offset\": [0, 1, 0] } ] }", "needs \"size\""),
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1], \"color\": \"mauve\" } ] }", "colours are a name"),
            ("{ \"name\": \"X\", \"parts\": [] }", "needs \"parts\""),
            ("{ \"parts\": [ { \"size\": [1, 1, 1] } ] }", "missing \"name\""),
            ("{ \"name\": \"X\", \"collision\": \"bouncy\", \"parts\": [ { \"size\": [1, 1, 1] } ] }", "\"solid\" or \"cosmetic\""),
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1] } ], \"breakable\": { \"breakBy\": [\"lasers\"] } }", "unknown damage"),
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [20, 1, 1] } ] }", "0.004 to 12"),
            ("{ \"name\": \"X\", \"format\": \"call-of-slop-prop/9\", \"parts\": [ { \"size\": [1, 1, 1] } ] }", "different version"),
            ("{ \"name\": \"X\", \"parts\": [ { \"size\": [1, 1, 1], \"material\": \"cheese\" } ] }", "unknown material"),
        ];
        var ok = 0;
        foreach (var (json, expect) in cases)
        {
            try
            {
                PropFile.Load(json, "test.json");
                Fail($"a broken prop file loaded without complaint (expected \"{expect}\"): {json}");
            }
            catch (PropFileException ex) when (ex.Message.Contains(expect))
            {
                ok++;
            }
            catch (Exception ex)
            {
                Fail($"a broken prop file gave the wrong error (expected \"{expect}\"): {ex.Message}");
            }
        }
        // And in a map file: an unknown prop names the closest ones.
        try
        {
            MapFile.Load(PropTestMap("{ \"op\": \"Prop\", \"name\": \"Trash Cann\", \"x\": 0, \"z\": 0 }"), "test map");
            Fail("a map with an unknown prop loaded");
        }
        catch (MapFileException ex) when (ex.Message.Contains("unknown prop") && ex.Message.Contains("Trash Can"))
        {
            ok++;
        }
        Line($"{ok} of {cases.Length + 1} broken prop and map files rejected with the right message");
    }

    /// <summary>A small flat test map (20 x 20 m) with the given extra ops.</summary>
    private static string PropTestMap(string ops, string extra = "") => $$"""
        { "name": "Prop Test", "halfX": 20, "halfZ": 20, {{extra}}
          "ops": [ { "op": "Ground", "color": "#666666" }, { "op": "Perimeter", "height": 3, "color": "#888888" },
                   {{ops}},
                   { "op": "RingSpawns", "count": 8, "frac": 0.85 }, { "op": "AutoWaypoints", "spacing": 6 } ] }
        """;

    private static void CheckPropCollision()
    {
        var m = MapFile.Load(PropTestMap("""
            { "op": "Prop", "name": "Wooden Crate", "x": 0, "z": 0 },
            { "op": "PlaceProp", "name": "Newspaper", "x": 6, "z": 0 },
            { "op": "PlaceProp", "name": "Wooden Crate", "x": 0, "z": 8, "yaw": 45 },
            { "op": "Block", "x0": 10, "y0": 0, "z0": -3, "x1": 13, "y1": 0.15, "z1": 3, "color": "#999999" },
            { "op": "PlaceProp", "name": "Trash Can", "x": 11.5, "z": 0 }
            """), "prop test");
        m.Validate();
        World.SetMap(m);
        if (m.PropInstances.Count != 4 || m.Ops.Count(o => o.Name == nameof(Map.PlaceProp)) != 4)
            Fail($"prop test map: {m.PropInstances.Count} props from {m.Ops.Count(o => o.Name == nameof(Map.PlaceProp))} PlaceProp ops (expected 4, the \"Prop\" with a name too)");
        var crate = m.PropInstances[0];
        var hit = World.RaycastWorld(new Vector3(-6, 0.5f, 0), Vector3.UnitX, 12);
        var paper = World.RaycastWorld(new Vector3(3, 0.01f, 0.05f), Vector3.UnitX, 6);
        var blockedSolid = World.Blocked(new Vector3(0, 0, 0));
        var blockedPaper = World.Blocked(new Vector3(6, 0, 0));
        Line($"crate: a ray stops at {hit:0.00} m (its face is at {6 - crate.Def.Bounds.Half.X:0.00}), standing in it blocked {blockedSolid}; "
             + $"newspaper: ray goes {paper:0.00} of 6 m, standing on it blocked {blockedPaper}");
        if (MathF.Abs(hit - (6 - 0.45f)) > 0.06f)
            Fail($"a solid prop doesn't stop a ray at its face ({hit:0.00} m)");
        if (!blockedSolid)
            Fail("a solid prop doesn't block movement");
        if (paper < 5.99f || blockedPaper)
            Fail("a cosmetic prop blocks rays or movement");
        // Turned 45 degrees: several boxes following the diamond outline, so the empty corners of its square bounds are open.
        var turned = m.PropInstances[2];
        var centre = World.RaycastWorld(new Vector3(0, 0.4f, 2), Vector3.UnitZ, 10);
        Line($"crate turned 45 deg: {turned.SolidCount} collider boxes, a ray at it stops at {2 + centre:0.00} (the diamond's tip is at {8 - 0.45f * MathF.Sqrt(2):0.00})");
        if (turned.SolidCount < 2 || MathF.Abs(2 + centre - (8 - 0.45f * MathF.Sqrt(2))) > 0.3f)
            Fail("a turned solid prop doesn't follow its turned outline");
        // Ground snap: the trash can stands on the 0.15 m kerb under it.
        var can = m.PropInstances[3];
        if (MathF.Abs(can.Position.Y - 0.15f) > 0.001f)
            Fail($"a prop placed on a kerb stands at y {can.Position.Y:0.000}, not on the kerb (0.15)");
        // Collider boxes are drawn by the prop renderer, not as boxes.
        var hidden = Enumerable.Range(0, m.Solid.Count).Count(m.IsPropCollider);
        if (hidden != m.PropInstances.Sum(p => p.SolidCount))
            Fail($"{hidden} boxes flagged as prop colliders, {m.PropInstances.Sum(p => p.SolidCount)} expected");

        // Fingerprints: a prop moves the fingerprint, a map without props has PropHash 0 and keeps its old one.
        var plain = MapFile.Load(PropTestMap("{ \"op\": \"Block\", \"x0\": 30, \"y0\": 0, \"z0\": 0, \"x1\": 31, \"y1\": 1, \"z1\": 1, \"color\": \"#999999\" }"), "plain");
        var moved = MapFile.Load(PropTestMap("""{ "op": "PlaceProp", "name": "Newspaper", "x": 6.5, "z": 0 }"""), "moved");
        var paperOnly = MapFile.Load(PropTestMap("""{ "op": "PlaceProp", "name": "Newspaper", "x": 6, "z": 0 }"""), "paper");
        if (plain.PropHash != 0 || paperOnly.PropHash == 0 || moved.ContentHash == paperOnly.ContentHash)
            Fail("prop fingerprints: maps without props must have PropHash 0, and moving a prop must change the fingerprint");
        var withoutProps = Maps.All.Where(x => x.PropInstances.Count == 0).ToList();
        if (withoutProps.Any(x => x.PropHash != 0 || Enumerable.Range(0, x.Solid.Count).Any(x.IsPropCollider)))
            Fail("a map without props has a prop fingerprint or prop boxes");
        Line($"fingerprints: {withoutProps.Count} of {Maps.All.Length} maps have no props and keep their fingerprints; a moved newspaper changes it "
             + $"({paperOnly.ContentHash:x8} -> {moved.ContentHash:x8})");
    }

    private static void CheckClutter()
    {
        var file = Maps.Find("Prop Yard")?.SourceFile is { } rel ? Path.Combine(Maps.Folder, rel) : null;
        if (file == null || !File.Exists(file))
        {
            Fail("Maps/Dev/PropYard.json ('Prop Yard') didn't load");
            return;
        }
        var json = File.ReadAllText(file);
        var a = MapFile.Load(json, "yard a");
        var b = MapFile.Load(json, "yard b");
        var same = a.PropInstances.Count == b.PropInstances.Count && a.PropInstances.Zip(b.PropInstances).All(p => p.First.Def == p.Second.Def && p.First.Position == p.Second.Position
            && p.First.Yaw == p.Second.Yaw && p.First.Scale == p.Second.Scale) && a.ContentHash == b.ContentHash;
        var reseeded = MapFile.Load(json.Replace("\"seed\": 5", "\"seed\": 55"), "yard c");
        var changed = reseeded.PropInstances.Skip(reseeded.ClutterStart).Select(p => p.Position).SequenceEqual(a.PropInstances.Skip(a.ClutterStart).Select(p => p.Position));
        var clutter = a.PropInstances.Skip(a.ClutterStart).ToList();
        Line($"Prop Yard clutter: {clutter.Count} props scattered ({clutter.Count(p => p.Def.IsSolid)} solid) after {a.ClutterStart} placed by hand; "
             + $"same file twice gives the same props {same}, another seed moves them {!changed}");
        if (!same)
            Fail("Clutter isn't deterministic: the same map file gave different props");
        if (changed)
            Fail("Clutter ignores its seed");
        if (clutter.Count < 60)
            Fail($"Clutter placed only {clutter.Count} props on Prop Yard");

        // Solid clutter keeps clear of spawns, waypoints, hills, flags and walls.
        var obj = a.Objectives;
        var problems = 0;
        foreach (var p in clutter.Where(p => p.Def.IsSolid))
        {
            var c = new Vector2(p.Position.X, p.Position.Z);
            var r = new Vector2(p.Bounds.Half.X, p.Bounds.Half.Z).Length();
            string? why = null;
            if (a.Spawns.Any(s => Vector2.Distance(new Vector2(s.X, s.Z), c) < Map.ClutterSpawnClearance + r - 0.01f))
                why = "next to a spawn";
            else if (a.Waypoints.Any(w => Vector2.Distance(new Vector2(w.X, w.Z), c) < Map.ClutterWaypointClearance + r - 0.01f))
                why = "on a waypoint";
            else if (obj.Hills.Any(h => Vector2.Distance(new Vector2(h.Center.X, h.Center.Z), c) < h.Radius + r))
                why = "on a hill";
            else if (obj.Flags.Any(f => Vector2.Distance(new Vector2(f.X, f.Z), c) < 3f + r - 0.01f))
                why = "by a flag";
            else
            {
                var gap = Map.ClutterClearance - 0.02f;
                for (var i = 0; i < a.Solid.Count && why == null; i++)
                {
                    if (i >= p.SolidStart && i < p.SolidStart + p.SolidCount)
                        continue;
                    var s = a.Solid[i];
                    if (s.Max.Y > p.Position.Y + World.StepHeight && s.Min.Y < p.Position.Y + World.PlayerHeight
                        && s.Max.X > p.Bounds.Min.X - gap && s.Min.X < p.Bounds.Max.X + gap && s.Max.Z > p.Bounds.Min.Z - gap && s.Min.Z < p.Bounds.Max.Z + gap)
                        why = $"closer than {Map.ClutterClearance} m to a box at {s.Center}";
                }
            }
            if (why != null && problems++ < 5)
                Fail($"Prop Yard: clutter {p.Def.Name} at ({p.Position.X:0.0}, {p.Position.Z:0.0}) is {why}");
        }
        // Nothing (cosmetic or solid) inside a wall.
        var inside = clutter.Count(p => a.Solid.Where((s, i) => !a.IsPropCollider(i)).Any(s =>
            s.Max.Y > p.Position.Y + 0.05f && s.Min.Y < p.Position.Y + p.Def.Bounds.Max.Y * p.Scale - 0.05f
            && s.Max.X > p.Bounds.Min.X + 0.05f && s.Min.X < p.Bounds.Max.X - 0.05f && s.Max.Z > p.Bounds.Min.Z + 0.05f && s.Min.Z < p.Bounds.Max.Z - 0.05f));
        if (inside > 0)
            Fail($"Prop Yard: {inside} clutter props overlap a wall or box");
        Line($"solid clutter keeps clear of spawns, waypoints and routes, hills, flags and walls: {(problems == 0 ? "yes" : $"{problems} problem(s)")}; {inside} inside boxes");
    }

    private static void CheckPropMapsAndNav()
    {
        foreach (var name in new[] { "Prop Gallery", "Prop Yard" })
        {
            if (Maps.Find(name) is not { } map)
            {
                Fail($"map '{name}' didn't load");
                continue;
            }
            World.SetMap(map);
            var g = NavGraph.Build(map);
            var inside = g.Nodes.Count(n => map.PropInstances.Any(p => p.Def.IsSolid && Enumerable.Range(p.SolidStart, p.SolidCount).Any(k =>
            {
                var s = map.Solid[k];
                return n.X > s.Min.X + 0.05f && n.X < s.Max.X - 0.05f && n.Z > s.Min.Z + 0.05f && n.Z < s.Max.Z - 0.05f && n.Y < s.Max.Y - 0.05f && n.Y + World.PlayerHeight > s.Min.Y;
            })));
            var search = new NavSearch();
            var path = new int[512];
            var pairs = 0;
            var unreachable = 0;
            foreach (var s0 in map.Spawns)
                foreach (var s1 in map.Spawns)
                {
                    if (s0 == s1)
                        continue;
                    pairs++;
                    if (search.Find(g, g.Nearest(s0), g.Nearest(s1, needSight: false), path) == 0)
                        unreachable++;
                }
            var batches = PropBatches.Build(map, PropMeshes.Build(Props.All));
            Line($"{name}: {map.PropInstances.Count} props ({map.PropInstances.Count(p => p.Def.IsSolid)} solid, {map.PropInstances.Sum(p => p.SolidCount)} collider boxes), "
                 + $"{batches.Chunks.Length} render chunks of {batches.Types} types; nav graph {g.Nodes.Length} nodes, {inside} inside a prop, {unreachable} of {pairs} spawn pairs unreachable");
            if (inside > 0)
                Fail($"{name}: {inside} nav nodes inside solid props");
            if (unreachable > map.Spawns.Count)
                Fail($"{name}: {unreachable} spawn pairs have no path (props block the way?)");
            if (batches.Count != map.PropInstances.Count || batches.Chunks.Sum(c => c.Count) != batches.Count || batches.SlotOf.Any(s => s < 0))
                Fail($"{name}: render batches don't cover every prop");
            if (name == "Prop Gallery" && map.PropInstances.Count != Props.All.Length)
                Fail($"Prop Gallery shows {map.PropInstances.Count} props but the library has {Props.All.Length}");
        }
        Line("(both maps also play the full bot match in \"Matches\" above, like every map)");
    }

    private static void CheckBreakables()
    {
        if (Maps.Find("Prop Gallery") is not { } map)
            return;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id);
        var me = game.Players[0];
        var hash = map.ContentHash;
        int Find(string n) => Enumerable.Range(0, map.PropInstances.Count).First(i => map.PropInstances[i].Def.Name == n);
        Vector3 Centre(int i) => map.PropInstances[i].Bounds.Center;
        float Shoot(int i, float damage, Vector3 from)
        {
            var dir = Vector3.Normalize(Centre(i) - from);
            var dist = World.RaycastWorld(from, dir, 30);
            game.ShotProps(me, from, dir, dist, damage, 0);
            return dist;
        }

        // Bullets: a crate takes exactly its health.
        var crate = Find("Wooden Crate");
        var health = map.PropInstances[crate].Def.Breakable!.Health;
        var from = Centre(crate) + new Vector3(0, 0.1f, 5f);
        var shots = 0;
        var firstDist = 0f;
        while (!map.IsPropBroken(crate) && shots < 100)
        {
            var d = Shoot(crate, 10, from);
            if (shots == 0)
                firstDist = d;
            shots++;
        }
        var afterDist = World.RaycastWorld(from, Vector3.Normalize(Centre(crate) - from), 30);
        var standIn = World.Blocked(new Vector3(Centre(crate).X, map.PropInstances[crate].Position.Y + 0.01f, Centre(crate).Z));
        Line($"bullets: the {map.PropInstances[crate].Def.Name} ({health} hp) broke on shot {shots} of 10 damage; the bullet stopped at {firstDist:0.00} m before, "
             + $"goes {afterDist:0.00} m after; blocked where it stood {standIn}");
        if (shots != (health + 9) / 10)
            Fail($"a {health} hp crate broke after {shots} shots of 10");
        if (firstDist > 5f || afterDist <= firstDist + 0.3f || standIn)
            Fail("a broken prop still blocks rays or movement (or the shot didn't stop at it before)");
        if (map.ContentHash != hash)
            Fail("breaking a prop changed the map's fingerprint");

        // Melee: one swing breaks a small crate (40 hp vs 55).
        var small = Find("Small Crate");
        var eye = Centre(small) + new Vector3(0, 0.6f, 1.6f);
        game.MeleeProps(me, eye, Vector3.Normalize(Centre(small) - eye), 0);
        // Explosions only: bullets don't hurt a brick pile, a grenade blast does.
        var bricks = Find("Brick Pile");
        for (var i = 0; i < 20; i++)
            Shoot(bricks, 50, Centre(bricks) + new Vector3(0, 0.5f, 4));
        var bricksAfterBullets = map.IsPropBroken(bricks);
        game.BlastProps(me, Centre(bricks) + new Vector3(0, 0.3f, 0.8f), 5f, 100f, PropDamage.Explosion, 0);
        // Kill streaks: a newspaper stack (melee, explosions, streaks) breaks from a strike far bigger than its health.
        var papers = Find("Newspaper Stack");
        game.BlastProps(me, Centre(papers) + new Vector3(0, 0.5f, 1.2f), 8f, 150f, PropDamage.Streak, 0);
        // Cosmetic breakables (a bottle) shatter as a bullet passes through, and the bullet carries on.
        var bottle = Find("Bottle");
        var bfrom = Centre(bottle) + new Vector3(0, 0, -3f);
        var bdist = World.RaycastWorld(bfrom, Vector3.UnitZ, 6);
        game.ShotProps(me, bfrom, Vector3.UnitZ, bdist, 5, 0);
        Line($"melee breaks the small crate {map.IsPropBroken(small)}; brick pile after 20 bullets {bricksAfterBullets}, after a grenade {map.IsPropBroken(bricks)}; "
             + $"newspaper stack after a strike {map.IsPropBroken(papers)}; bottle shot through {map.IsPropBroken(bottle)} (the bullet went {bdist:0.0} of 6 m)");
        if (!map.IsPropBroken(small))
            Fail("a melee swing didn't break a small crate");
        if (bricksAfterBullets || !map.IsPropBroken(bricks))
            Fail("a brick pile (explosions only) broke from bullets, or didn't break from a grenade");
        if (!map.IsPropBroken(papers))
            Fail("a kill-streak blast didn't break a newspaper stack");
        if (!map.IsPropBroken(bottle) || bdist < 5.9f)
            Fail("a shot didn't break the bottle it passed through (or the bottle stopped it)");

        // Explosive barrel: breaks, goes off (a blast effect), and its blast breaks what's near.
        var barrel = Find("Explosive Barrel");
        var nearBefore = map.BrokenProps.Count;
        game.Blasts.Clear();
        Shoot(barrel, 50, Centre(barrel) + new Vector3(0, 0, 5));
        Line($"explosive barrel: broken {map.IsPropBroken(barrel)}, blast effects {game.Blasts.Count}, {map.BrokenProps.Count - nearBefore - 1} more props broken by it");
        if (!map.IsPropBroken(barrel) || game.Blasts.Count == 0)
            Fail("an explosive barrel didn't break and go off");

        // The break event and the broken set round-trip through packets onto another copy of the map.
        var w = new PacketWriter();
        Game.WritePropBreak(w, crate, PropDamage.Bullet, from);
        var r = new PacketReader(w.Span[1..]);
        var ok = w.Span[0] == (byte)Msg.PropBreak && r.U16() == crate && (PropDamage)r.U8() == PropDamage.Bullet && r.Vec() == from;
        var copy = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, map.SourceFile!)), "client copy");
        var packets = 0;
        Game.WritePropState(w, map.BrokenProps, pw =>
        {
            packets++;
            var pr = new PacketReader(pw.Span[1..]);
            Game.ReadPropState(copy, ref pr);
        });
        var sameSet = copy.BrokenProps.OrderBy(x => x).SequenceEqual(map.BrokenProps.OrderBy(x => x));
        Line($"break event {1 + 2 + 1 + 12} bytes round-trips {ok}; the broken set ({map.BrokenProps.Count} props) reaches a joining client in {packets} packet(s): {sameSet}");
        if (!ok || !sameSet)
            Fail("the prop break event or the broken set doesn't round-trip");
        // Hundreds of broken props still fit (several packets), and a later "first" packet resets the client.
        var many = new List<int>();
        foreach (var i in copy.BreakableProps)
            many.Add(i);
        while (many.Count < 700)
            many.Add(many[many.Count % Math.Max(1, copy.BreakableProps.Length)]);
        packets = 0;
        var fresh = MapFile.Load(File.ReadAllText(Path.Combine(Maps.Folder, map.SourceFile!)), "client copy 2");
        Game.WritePropState(w, many, pw =>
        {
            packets++;
            var pr = new PacketReader(pw.Span[1..]);
            Game.ReadPropState(fresh, ref pr);
            if (pw.Length > 1400)
                Fail("a prop state packet is bigger than 1400 bytes");
        });
        if (packets < 2 || fresh.BrokenProps.Count != copy.BreakableProps.Length)
            Fail($"a broken set of 700 entries took {packets} packet(s) and left {fresh.BrokenProps.Count} props broken ({copy.BreakableProps.Length} expected)");

        // A new match puts everything back (and the collision with it).
        var onMatchStarted = typeof(Game).GetMethod("OnMatchStarted", BindingFlags.NonPublic | BindingFlags.Instance);
        onMatchStarted?.Invoke(game, null);
        var restoredRay = World.RaycastWorld(from, Vector3.Normalize(Centre(crate) - from), 30);
        Line($"new match: {map.BrokenProps.Count} props still broken, crate blocks the shot again at {restoredRay:0.00} m, debris {PropDebris.Count}");
        if (onMatchStarted == null || map.BrokenProps.Count != 0 || MathF.Abs(restoredRay - firstDist) > 0.01f || map.PropHealth(crate) != health)
            Fail("a new match didn't restore the broken props");
        if (map.ContentHash != hash)
            Fail("restoring props changed the map's fingerprint");
    }

    private static void CheckDebris()
    {
        if (Maps.Find("Prop Gallery") is not { } map)
            return;
        World.SetMap(map);
        PropDebris.Clear();
        var before = PropDebris.Spawned;
        foreach (var i in map.BreakableProps)
            map.BreakProp(i); // as in the game: the prop is gone before its pieces fly
        foreach (var i in map.BreakableProps)
            for (var k = 0; k < 6; k++)
                PropDebris.Spawn(map.PropInstances[i], map.PropInstances[i].Bounds.Center + new Vector3(0, 0, 3), PropDamage.Explosion, i * 10 + k);
        var spawned = PropDebris.Spawned - before;
        var capped = PropDebris.Count;
        var dt = 1f / 60f;
        for (var f = 0; f < 120; f++)
            PropDebris.Update(dt); // warm up (first calls may JIT)
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var f = 0; f < 60; f++)
            PropDebris.Update(dt);
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        var (resting, lowest) = PropDebris.Inspect();
        var at3 = PropDebris.Count;
        for (var f = 0; f < 60 * 6; f++)
            PropDebris.Update(dt);
        var buffer = new float[PropDebris.Capacity * PropDebris.FloatsPerPiece];
        Line($"debris: {spawned} pieces spawned, {capped} alive at most (cap {PropDebris.Capacity}); after 3 s {at3} alive, {resting} resting, lowest {lowest:0.000} m above its floor; "
             + $"after 9 s {PropDebris.Count} alive; {alloc} bytes allocated per second of updates");
        if (capped > PropDebris.Capacity || capped == 0)
            Fail($"debris count {capped} is over the cap or nothing spawned");
        if (lowest < -0.02f)
            Fail($"debris sank {-lowest:0.00} m below the floor");
        if (PropDebris.Count != 0 || PropDebris.Write(buffer) != 0)
            Fail($"{PropDebris.Count} debris pieces never despawned");
        if (alloc > 1024)
            Fail($"debris updates allocate ({alloc} bytes in 60 frames)");
        map.RestoreProps();
    }
}
