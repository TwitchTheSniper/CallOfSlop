using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Space pack, 07 Canopy Deck (the derelict generation ship's arboretum, tools/space/arboretum.py) and 08 Ignition Hall
/// (the fusion reactor hall, tools/space/reactor.py): each has a non-rectangular outline, hand-placed objectives for
/// every mode inside it, prop and decal counts above a floor, spawns and objectives the nav graph reaches, walks to the
/// hills and the enemy flag that are fair for both teams (within 10%), and a short seeded bot match with kills, no
/// stuck or escaped bots and the tick work budget. No long bot matches.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> SpaceBNotes = new();

    private static void SpaceBLine(string text)
    {
        Line(text);
        SpaceBNotes.Add(text);
    }

    private static void CheckSpaceArboretumReactor()
    {
        SpaceBNotes.Clear();
        CheckSpaceBMap("Canopy Deck", "07-CanopyDeck.json", BoundaryShape.Ellipse, minProps: 180, minSolidProps: 90, minDecals: 150, minPlants: 3000, minTrees: 20, maxDrawnBoxes: 420);
        CheckSpaceBMap("Ignition Hall", "08-IgnitionHall.json", BoundaryShape.Polygon, minProps: 180, minSolidProps: 90, minDecals: 150, minPlants: 0, minTrees: 0, maxDrawnBoxes: 420);
        World.SetMap(Maps.All[0]);
        if (SpaceBNotes.Count > 0)
            Line("::notice title=Space: arboretum and reactor::" + string.Join("%0A", SpaceBNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    private static void CheckSpaceBMap(string name, string file, BoundaryShape shape, int minProps, int minSolidProps, int minDecals, int minPlants, int minTrees, int maxDrawnBoxes)
    {
        var m = Maps.Find(name);
        if (m == null)
        {
            Fail($"Maps/Space/{file} ('{name}') didn't load");
            return;
        }
        if (m.SourceFile == null || !m.SourceFile.Replace('\\', '/').EndsWith("Space/" + file, StringComparison.OrdinalIgnoreCase))
            Fail($"{name}: file should be Maps/Space/{file} (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != shape)
        {
            Fail($"{name}: boundary should be a {shape} (is {(m.Boundary == null ? "none (a rectangle)" : m.Boundary.Spec.Shape.ToString())})");
            return;
        }
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var plants = m.FoliageInstances;
        var trees = plants.Count(f => Foliage.IsTree(f.Type));
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var o = m.Objectives;
        var bake = DecalBaker.Bake(m);
        SpaceBLine($"{name}: {bd.Spec.Shape} outline, {bd.SegmentCount} segments, {bd.Area:0} m2 ({m.Size}), max {m.MaxPlayers} players, order {m.Order}; "
                   + $"spawns {m.Spawns.Count} (teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}), hills {o.Hills.Count}, waypoints {m.Waypoints.Count}");
        SpaceBLine($"  objects: {m.Solid.Count} boxes ({drawn} drawn one by one, {m.HiddenBoxCount} hidden colliders), {props.Count} props ({solid} solid, {props.Count - solid} cosmetic, "
                   + $"{props.Count(p => p.IsBreakable)} breakable), {plants.Length} plants ({trees} trees), {m.MeshPlacements.Count} meshes, decals {bake.Placed}/{bake.Requested} placed");
        void AtLeast(string what, int have, int need)
        {
            if (have < need)
                Fail($"{name}: {have} {what} (wanted at least {need})");
        }
        AtLeast("props", props.Count, minProps);
        AtLeast("solid props", solid, minSolidProps);
        AtLeast("decals placed", bake.Placed, minDecals);
        AtLeast("plants", plants.Length, minPlants);
        AtLeast("trees", trees, minTrees);
        if (bake.Placed < bake.Requested * 0.75)
            Fail($"{name}: only {bake.Placed} of {bake.Requested} decals landed on a surface");
        if (drawn > maxDrawnBoxes)
            Fail($"{name}: {drawn} boxes drawn one by one (keep it under {maxDrawnBoxes})");
        if (m.Order != (name == "Canopy Deck" ? 607 : 608))
            Fail($"{name}: order {m.Order} (Space pack maps use 600 + the file number)");

        // Objectives: hand-placed, for every mode, inside the outline; spawns enough for a full server.
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");
        if (o.TeamSpawns[0].Count < m.MaxPlayers / 2 || o.TeamSpawns[1].Count < m.MaxPlayers / 2)
            Fail($"{name}: team spawn lists are short ({o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} for {m.MaxPlayers} players)");
        if (o.Hills.Count < 3)
            Fail($"{name}: only {o.Hills.Count} hill zone(s)");
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < m.MaxPlayers)
            Fail($"{name}: {authored} hand-placed spawns for {m.MaxPlayers} players");
        var outside = new List<string>();
        void In(string what, float x, float z, float margin = 0f)
        {
            if (!bd.Inside(x, z, margin))
                outside.Add($"{what} ({x:0.0},{z:0.0})");
        }
        foreach (var s in m.Spawns)
            In("spawn", s.X, s.Z, 0.3f);
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z, h.Radius * 0.5f);
        for (var i = 0; i < o.Flags.Length; i++)
            In(i == 0 ? "blue flag" : "red flag", o.Flags[i].X, o.Flags[i].Z, 0.5f);
        foreach (var p in props)
            In(p.Def.Name, p.Position.X, p.Position.Z);
        if (outside.Count > 0)
            Fail($"{name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(6))}");
        // Point symmetry of the objectives: the hills mirror each other (or sit on the centre), the flags are twins.
        if (Vector2.Distance(new Vector2(o.Flags[0].X, o.Flags[0].Z), -new Vector2(o.Flags[1].X, o.Flags[1].Z)) > 0.1f)
            Fail($"{name}: the flags aren't each other's twin through the centre");

        // The nav graph reaches every spawn, hill and flag; walks are fair.
        World.SetMap(m);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var missing = new List<string>();
        void Reach(string what, Vector3 p)
        {
            var reach = MathF.Max(3f, g.Spacing * 1.5f);
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < 1.5f && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < reach * reach)
                    return;
            }
            missing.Add($"{what} ({p.X:0},{p.Y:0.0},{p.Z:0})");
        }
        foreach (var s in m.Spawns)
            Reach("spawn", s);
        foreach (var h in o.Hills)
            Reach("hill", h.Center);
        for (var i = 0; i < o.Flags.Length; i++)
            Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
        var mainNodes = Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent);
        var highNodes = Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent && g.Nodes[i].Y > 2.0f);
        if (missing.Count > 0)
            Fail($"{name}: the nav graph doesn't reach {string.Join(", ", missing.Take(6))}{(missing.Count > 6 ? $" and {missing.Count - 6} more" : "")}");
        if (highNodes < 10)
            Fail($"{name}: only {highNodes} nav nodes above 2 m in the main area (the raised floors should be on the graph)");
        if (g.BuildMs > 3000)
            Fail($"{name}: the nav graph took {g.BuildMs:0} ms to build");

        var search = new NavSearch();
        var path = new int[4096];
        float Walk(Vector3 a, Vector3 b)
        {
            var n = search.Find(g, g.Nearest(a, needSight: false), g.Nearest(b, needSight: false), path);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var i = 1; i < n; i++)
                len += Vector3.Distance(g.Nodes[path[i - 1]], g.Nodes[path[i]]);
            return len;
        }
        float Average(List<Vector3> from, Vector3 to)
        {
            var sum = 0f;
            foreach (var s in from)
            {
                var w = Walk(s, to);
                if (float.IsNaN(w))
                    return float.NaN;
                sum += w;
            }
            return sum / Math.Max(1, from.Count);
        }
        var walks = new List<string>();
        var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, h.Center)).ToList();
        targets.Add(("enemy flag", o.Flags[1], o.Flags[0]));
        foreach (var (label, forBlue, forRed) in targets)
        {
            float blue = Average(o.TeamSpawns[0], forBlue), red = Average(o.TeamSpawns[1], forRed);
            walks.Add($"{label} {blue:0}/{red:0} m");
            if (float.IsNaN(blue) || float.IsNaN(red))
                Fail($"{name}: no route from a team's spawns to {label}");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"{name}: {label} is {blue:0} m from Blue and {red:0} m from Red (more than 10% apart)");
        }
        SpaceBLine($"  nav: {g.Nodes.Length} nodes ({mainNodes} in the main area, {highNodes} above 2 m), built in {g.BuildMs:0} ms; walks Blue/Red: {string.Join(", ", walks)}");

        // A short seeded bot match: 12 players, 12 simulated seconds of team deathmatch after a 3 s warm-up.
        SpaceBBotMatch(m, GameMode.TeamDeathmatch, Math.Min(m.MaxPlayers - 1, 11), 15);
    }

    private static void SpaceBBotMatch(Map m, GameMode mode, int bots, int seconds)
    {
        var sw = Stopwatch.StartNew();
        using var game = ModeGame(bots, mode, m.Id);
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var lastPos = new Dictionary<byte, Vector3>();
        var moved = new Dictionary<byte, float>();
        var cpu = new TickCpu();
        var maxTickMs = 0.0;
        var jitTicks = 0;
        try
        {
            for (var i = 0; i < seconds * World.TickRate; i++)
            {
                now += dt;
                var jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
                cpu.Start();
                var t0 = Stopwatch.GetTimestamp();
                game.Update(now, dt);
                var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                var jit = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jit0).TotalMilliseconds > 1;
                if (jit)
                    jitTicks++;
                var measured = i > 3 * World.TickRate && !jit;
                cpu.Stop(ms, count: measured);
                if (measured)
                    maxTickMs = Math.Max(maxTickMs, ms);
                if (i % 60 == 0)
                    foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                    {
                        if (lastPos.TryGetValue(p.Id, out var lp))
                            moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                        lastPos[p.Id] = p.Position;
                    }
            }
        }
        catch (Exception ex)
        {
            Fail($"{m.Name}: {mode} bot match crashed: {ex}");
            return;
        }
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        var fallen = game.Players.Values.Count(p => p.Alive && p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z) - 0.5f);
        var escaped = game.Players.Values.Count(p => p.Alive && !m.Boundary!.Inside(p.Position.X, p.Position.Z, -0.5f));
        var stuck = moved.Count(kv => kv.Value < 8f);
        SpaceBLine($"  bots {mode} {game.Players.Count} players, {seconds} s: kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, stuck {stuck}, fallen {fallen}, outside {escaped}, "
                   + $"worst tick {maxTickMs:0.00} ms (work {cpu.WorstText}; {jitTicks} JIT ticks not counted), broken props {m.BrokenProps.Count} ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != bots + 1)
            Fail($"{m.Name}: {mode} match has {game.Players.Count} players (expected {bots + 1})");
        if (fallen > 0 || escaped > 0)
            Fail($"{m.Name}: {fallen} player(s) below the floor, {escaped} outside the outline after a {mode} bot match");
        if (stuck > 2)
            Fail($"{m.Name}: {stuck} bots barely moved in a {mode} match");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a {mode} server tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        m.RestoreProps();
    }
}
