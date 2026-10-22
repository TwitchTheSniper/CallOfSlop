using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The World Tour pack's Finland map, "Vuorikoski" (Maps/World/06-Finland.json, tools/world/finland.py): a large boreal
/// forest with two lake villages, a church hamlet and a copper mine in the middle with a tunnel network (terrain caves)
/// under it. Checks the file and its size class, the outline and terrain, the objectives (hand-placed, inside the
/// outline, reachable on the bots' nav graph), the tunnels (a roofed cave floor at the right height with a nav node on
/// it and no plants in it at every probe; walking in through an adit and up from the pit floor to the shaft station),
/// the shaft cage, fairness (the map is a mirror image about x = 0: nav walks from each team's spawns to every hill and
/// to the enemy flag within 6%), no line of sight between the teams' spawns, and the draw cost (boxes drawn one by one,
/// trees, plants, props, buildings). No bot matches (user rule): the nav graph and short movement runs only.
/// </summary>
public static partial class SelfTest
{
    private const string FinlandMap = "Vuorikoski";

    /// <summary>Points in the tunnels (x, z, the floor's height there) from tools/world/finland.py.</summary>
    private static readonly (string Name, float X, float Z, float Floor)[] FinlandTunnelProbes =
    [
        ("main drift west", -30f, -4f, -8f), ("main drift east", 30f, -4f, -8f), ("shaft station", 0f, -1f, -8f),
        ("crusher chamber", 0f, -38f, -8f), ("pump station", -22f, 14f, -8f), ("magazine", 22f, 14f, -8f),
        ("haulage loop west", -11f, 16f, -8f), ("haulage loop east", 11f, 16f, -8f), ("pit drift", 0f, 25f, -8f),
        ("west adit", -50f, -14f, -5.5f), ("east adit", 50f, -14f, -5.5f), ("village drift west", -48f, 10f, -8f),
        ("village drift east", 48f, 10f, -8f), ("church adit west", -16f, -50f, -5f), ("church adit east", 16f, -50f, -5f),
        ("north drift", 0f, -25f, -8f),
    ];

    private static void CheckWorldFinland()
    {
        var m = Maps.All.FirstOrDefault(x => x.Name == FinlandMap);
        if (m == null)
        {
            if (Directory.Exists(Maps.Folder))
                Fail($"no map \"{FinlandMap}\" (Maps/World/06-Finland.json)");
            return;
        }
        var previous = World.Map;
        var previousMover = World.Mover;
        try
        {
            World.Mover = ClassicMover.Instance;
            CheckWorldFinlandMap(m);
        }
        finally
        {
            World.Mover = previousMover;
            World.SetMap(previous);
        }
    }

    private static void CheckWorldFinlandMap(Map m)
    {
        if (m.SourceFile == null || !m.SourceFile.Replace('\\', '/').EndsWith("World/06-Finland.json", StringComparison.OrdinalIgnoreCase))
            Fail($"{m.Name}: file should be Maps/World/06-Finland.json (is {m.SourceFile})");
        if (m.Order != 506)
            Fail($"{m.Name}: order {m.Order} (expected 506)");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Smooth || bd.Spec.Noise is not { Amplitude: > 1f })
        {
            Fail($"{m.Name}: the outline should be a wavy (noisy) smooth shape, not a rectangle");
            return;
        }
        if (m.Size != MapSize.Large || bd.Area is < 28000 or > 40000)
            Fail($"{m.Name}: size class {m.Size}, {bd.Area:0} m2 (expected Large, 28,000-40,000 m2)");
        if (m.MaxPlayersSetting != 24 || m.MaxPlayers != 24)
            Fail($"{m.Name}: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 24)");
        if (m.Terrain is not { Voids: { } voids } terrain)
        {
            Fail($"{m.Name}: no terrain with caves");
            return;
        }
        if (m.Movers.Count != 1)
            Fail($"{m.Name}: {m.Movers.Count} movers (expected the shaft cage)");

        // Costs: boxes drawn one by one (map blocks, not trunks or buildings), trees, plants, props, buildings, meshes.
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(i => m.IsDrawnBox(i) && !m.IsTrunk(i));
        var trees = m.FoliageInstances.Count(f => Foliage.IsTree(f.Type));
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var buildings = m.PlacedBuildings.Count;
        var shell = m.PlacedBuildings.Sum(b => b.ShellCount);
        var explosive = props.Where(p => p.Def.Breakable is { ExplodeRadius: > 0 }).ToList();
        var o = m.Objectives;
        var line = $"{m.Name}: {bd.Area:0} m2 ({m.Size}), {bd.SegmentCount} outline segments, max {m.MaxPlayers}; terrain {terrain.CountX}x{terrain.CountZ}, "
                   + $"{voids.CaveCells} cave cells, {voids.OpenCells} open cells, {voids.NavPoints.Length} cave nav points; {drawn} boxes drawn one by one, "
                   + $"{trees} trees (trunk boxes), {m.FoliageInstances.Length} plants, {props.Count} props ({solid} solid, {props.Count(p => p.IsBreakable)} breakable, "
                   + $"{explosive.Count} explosive), {buildings} buildings ({shell} shell boxes), {m.MeshPlacements.Count} meshes; spawns {m.Spawns.Count} "
                   + $"(teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}), hills {o.Hills.Count}, waypoints {m.Waypoints.Count}";
        Line(line);
        Line("::warning title=World: Vuorikoski (results)::" + line.Replace("%", "%25"));
        if (drawn > 200)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep it under 200: use props, meshes and buildings)");
        if (trees is < 300 or > 900)
            Fail($"{m.Name}: {trees} trees (expected 300-900: a forest, but every trunk is a box)");
        if (m.FoliageInstances.Length < 40000)
            Fail($"{m.Name}: only {m.FoliageInstances.Length} plants");
        if (props.Count < 3000 || solid < 300)
            Fail($"{m.Name}: {props.Count} props, {solid} solid (expected at least 3,000 and 300)");
        if (buildings is < 18 or > 30 || shell > 10000)
            Fail($"{m.Name}: {buildings} buildings, {shell} shell boxes (expected 18-30 and under 10,000)");

        // Everything inside the outline; objectives placed by hand.
        var outside = new List<string>();
        void In(string what, float x, float z, float margin = 0f)
        {
            if (!bd.Inside(x, z, margin))
                outside.Add($"{what} ({x:0.0},{z:0.0})");
        }
        foreach (var s in m.Spawns)
            In("spawn", s.X, s.Z, 0.3f);
        foreach (var w in m.Waypoints)
            In("waypoint", w.X, w.Z);
        foreach (var p in props)
            In(p.Def.Name, p.Position.X, p.Position.Z);
        foreach (var f in m.FoliageInstances)
            In(f.Type.ToString(), f.Position.X, f.Position.Z);
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z, h.Radius * 0.5f);
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(6))}");
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < m.MaxPlayers)
            Fail($"{m.Name}: {authored} hand-placed spawns for {m.MaxPlayers} players");
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated || o.Hills.Count != 3)
            Fail($"{m.Name}: objectives should be hand-placed with three hills");
        foreach (var e in explosive)
            if (m.Spawns.Any(s => Vector2.Distance(new Vector2(s.X, s.Z), new Vector2(e.Position.X, e.Position.Z)) < 10f))
                Fail($"{m.Name}: explosive {e.Def.Name} at ({e.Position.X:0.0},{e.Position.Z:0.0}) is within 10 m of a spawn");
        foreach (var p in props.Where(p => p.Def.IsSolid))
        {
            var b = p.Bounds;
            float Dist(Vector3 q) => MathF.Sqrt(MathF.Pow(MathF.Max(0, MathF.Max(b.Min.X - q.X, q.X - b.Max.X)), 2) + MathF.Pow(MathF.Max(0, MathF.Max(b.Min.Z - q.Z, q.Z - b.Max.Z)), 2));
            if (m.Spawns.Any(s => Dist(s) < 0.8f && b.Max.Y > s.Y + 0.05f && b.Min.Y < s.Y + World.PlayerHeight)
                || o.Hills.Any(h => Dist(h.Center) < h.Radius && b.Max.Y > h.Center.Y - 1f && b.Min.Y < h.Center.Y + 3f) || o.Flags.Any(f => Dist(f) < 2f))
                Fail($"{m.Name}: solid {p.Def.Name} at ({p.Position.X:0.0},{p.Position.Z:0.0}) is on a spawn, hill zone or flag");
        }

        // The nav graph reaches every spawn, hill and flag.
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        bool NodeNear(Vector3 p, float reach)
        {
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < 1.2f && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < reach * reach)
                    return true;
            }
            return false;
        }
        var missing = new List<string>();
        var reachR = MathF.Max(3f, g.Spacing * 1.5f);
        foreach (var s in m.Spawns)
            if (!NodeNear(s, reachR))
                missing.Add($"spawn ({s.X:0},{s.Z:0})");
        foreach (var h in o.Hills)
            if (!NodeNear(h.Center, reachR))
                missing.Add($"hill ({h.Center.X:0},{h.Center.Z:0})");
        foreach (var f in o.Flags)
            if (!NodeNear(f, reachR))
                missing.Add($"flag ({f.X:0},{f.Z:0})");
        if (missing.Count > 0)
            Fail($"{m.Name}: the nav graph doesn't reach {string.Join(", ", missing.Take(6))}");

        // The tunnels: a roofed cave floor at the right height, headroom, rock over it, a nav node on it, no plants in it.
        var notes = new List<string>();
        foreach (var (name, x, z, floor) in FinlandTunnelProbes)
        {
            var kind = terrain.Column(x, z, out var fl, out var ceil, out var top, out var hollow);
            var plants = m.FoliageInstances.Count(f => MathF.Abs(f.Position.X - x) < 1.5f && MathF.Abs(f.Position.Z - z) < 1.5f && f.Position.Y < ceil);
            notes.Add($"{name} {fl:0.0}/{ceil - fl:0.0}");
            if (kind != TerrainVoids.Cave || !hollow || MathF.Abs(fl - floor) > 0.6f || ceil - fl < 2.6f || top - ceil < TerrainVoids.RoofMin - 0.01f)
                Fail($"{m.Name}: {name} ({x:0},{z:0}): kind {kind}, floor {fl:0.00} (expected {floor:0.00}), ceiling {ceil:0.00}, surface {top:0.00}");
            if (plants > 0)
                Fail($"{m.Name}: {name}: {plants} plant(s) growing in the tunnel");
            if (!NodeNear(new Vector3(x, fl, z), 3.5f))
                Fail($"{m.Name}: {name} ({x:0},{z:0}): no bot nav node on the tunnel floor");
        }
        Line($"{m.Name} tunnels (floor/headroom): {string.Join(", ", notes)}");

        // Walk in at the west adit to the shaft station, and up the pit drift from the pit floor.
        Vector3 At(float x, float z, float? y = null) => new(x, y is { } yy ? m.GroundHeight(x, z, yy + 1f) : m.GroundHeight(x, z), z);
        foreach (var (label, start, route) in new[]
                 {
                     ("west adit", At(-66, -24), new[] { At(-62, -21.5f), At(-56, -18, -3f), At(-50, -14, -5.5f), At(-44, -8, -7f), At(-40, -4, -8f), At(-28, -4, -8f), At(-14, -4, -8f), At(-3, -2, -8f) }),
                     ("pit drift", At(0, 56), new[] { At(0, 51), At(0, 46, -9f), At(0, 40, -8.5f), At(0, 30, -8f), At(0, 18, -8f), At(0, 8, -8f), At(0, 1, -8f) }),
                 })
        {
            var underRoof = false;
            var ok = HoleWalk(start, route, 45f, st =>
            {
                var k = terrain.Column(st.Position.X, st.Position.Z, out _, out var c, out _, out var hol);
                if (k == TerrainVoids.Cave && hol && st.Position.Y < c)
                    underRoof = true;
            }, out var end);
            Line($"{m.Name}: walk in through the {label}: {(ok ? "made it" : "stuck")} at ({end.Position.X:0.0}, {end.Position.Y:0.00}, {end.Position.Z:0.0}), under the roof {underRoof}");
            if (!ok || !underRoof || MathF.Abs(end.Position.Y + 8f) > 0.5f)
                Fail($"{m.Name}: couldn't walk through the {label} to the shaft station (ended at ({end.Position.X:0.0}, {end.Position.Y:0.00}, {end.Position.Z:0.0}), under the roof {underRoof})");
        }

        // No line of sight between the two teams' spawns (eye height: terrain, boxes, trunks, props, buildings).
        var seen = new List<string>();
        foreach (var a in o.TeamSpawns[0])
            foreach (var b in o.TeamSpawns[1])
            {
                Vector3 ea = a + new Vector3(0, 1.6f, 0), eb = b + new Vector3(0, 1.6f, 0);
                var d = Vector3.Distance(ea, eb);
                if (World.RaycastWorld(ea, (eb - ea) / d, d) >= d - 0.05f)
                    seen.Add($"({a.X:0},{a.Z:0})-({b.X:0},{b.Z:0})");
            }
        if (seen.Count > 0)
            Fail($"{m.Name}: {seen.Count} Blue/Red spawn pair(s) can see each other: {string.Join(", ", seen.Take(4))}");

        // Fairness: the map is a mirror image, so the nav walks to every hill and to the enemy flag match within 6%.
        var search = new NavSearch();
        var path = new int[4096];
        float Walk(int team, Vector3 goal)
        {
            var total = 0f;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var count = search.Find(g, g.Nearest(s, needSight: false), gi, path, maxExpand: 60000);
                if (count == 0)
                    return float.PositiveInfinity;
                var len = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                for (var k = 1; k < count; k++)
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                total += len;
            }
            return total / Math.Max(1, o.TeamSpawns[team].Count);
        }
        var walks = new List<string>();
        void Check(string name, float blue, float red)
        {
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.06f)
                Fail($"{m.Name}: {name} is a {blue:0} m walk for Blue and {red:0} m for Red (more than 6% apart)");
        }
        for (var i = 0; i < o.Hills.Count; i++)
            Check($"hill {i + 1}", Walk(0, o.Hills[i].Center), Walk(1, o.Hills[i].Center));
        Check("enemy flag", Walk(0, o.Flags[1]), Walk(1, o.Flags[0]));
        var balance = $"{m.Name} balance (Blue/Red nav walk): {string.Join(", ", walks)}; spawn pairs in sight {seen.Count}; nav graph {g.Nodes.Length} nodes";
        Line(balance);
        Line("::warning title=Vuorikoski balance (results)::" + balance.Replace("%", "%25"));
    }
}
