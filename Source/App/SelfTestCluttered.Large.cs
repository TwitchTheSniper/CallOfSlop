using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's large map, "Sprawl": an irregular smooth outline with noise, bays and peninsulas, thousands of
/// props and plants, terrain, 32 players. It isn't mirrored, so instead of twin checks it checks fairness by route:
/// the nav-graph walk from each team's spawns to every hill and to the enemy flag is within 10% for both teams, and
/// both teams' routes to the hills pass about as much cover.
/// </summary>
public static partial class SelfTest
{
    private const string ClutteredLargeMap = "Sprawl";

    static partial void CheckClutteredLarge()
    {
        var m = Maps.Find(ClutteredLargeMap);
        if (m == null)
        {
            Fail($"no {ClutteredLargeMap} map in the {ClutteredPack} pack (Maps/Cluttered/03-Sprawl.json)");
            return;
        }
        if (m.Pack?.Name != ClutteredPack || m.Size != MapSize.Large)
            Fail($"{m.Name}: pack {m.Pack?.Name}, size {m.Size} (expected the {ClutteredPack} pack, Large)");
        if (!CheckClutteredBasics(m, BoundaryShape.Smooth, 32, minProps: 3000, minSolidProps: 400, minPlants: 60000))
            return;
        var b = m.Boundary!;
        if (b.Spec.Points.Length is < 10 or > 16 || b.Spec.Noise is not { Amplitude: > 0 })
            Fail($"{m.Name}: the outline should be a noisy smooth blob from 10-16 control points ({b.Spec.Points.Length} points, noise {b.Spec.Noise?.Amplitude ?? 0:0.0} m)");
        if (!m.HasTerrain)
            Fail($"{m.Name}: no terrain");

        // Counts (the density stress test) and the draw-call side of them.
        var props = m.PropInstances;
        int solid = props.Count(p => p.Def.IsSolid), breakable = props.Count(p => p.IsBreakable);
        var cosmetic = props.Count - solid;
        var plants = m.FoliageInstances;
        var trees = plants.Count(f => Foliage.IsTree(f.Type));
        var boxes = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var byType = string.Join(", ", plants.GroupBy(f => f.Type).OrderBy(g => g.Key).Select(g => $"{g.Key.ToString().ToLowerInvariant()} {g.Count()}"));
        ClutteredLine($"{m.Name}: {b.SegmentCount} outline segments, {b.Area:0} m2, {m.Size}, max {m.MaxPlayers}; boxes {boxes} drawn ({m.Solid.Count} solid incl. prop colliders), "
            + $"props {props.Count} ({m.ClutterStart} by hand, {props.Count - m.ClutterStart} clutter; {solid} solid, {cosmetic} cosmetic, {breakable} breakable), "
            + $"plants {plants.Length} ({byType}), meshes {m.MeshPlacements.Count}, spawns {m.Spawns.Count}, waypoints {m.Waypoints.Count}");
        if (trees is < 100 or > 450)
            Fail($"{m.Name}: {trees} trees (expected 100-450: each trunk is a box)");
        if (boxes > 1400)
            Fail($"{m.Name}: {boxes} boxes drawn one by one (keep it under 1400; use props, meshes and foliage for the rest)");

        var o = m.Objectives;
        if (o.Hills.Count < 3 || o.TeamSpawns[0].Count < 14 || o.TeamSpawns[1].Count < 14)
            Fail($"{m.Name}: {o.Hills.Count} hills, team spawns {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} (expected 3+ hills, 14+ spawns a team)");

        // Fairness by route: the nav graph's walk from each team's spawns (averaged) to each hill and the enemy flag.
        var g = ClutteredNav(m);
        var search = new NavSearch();
        var path = new int[1024];
        var routeNodes = new[] { new List<Vector3>(), new List<Vector3>() };
        float Walk(int team, Vector3 goal, bool keepRoute)
        {
            var total = 0f;
            var n = 0;
            var gi = g.Nearest(goal, needSight: false);
            foreach (var s in o.TeamSpawns[team])
            {
                var si = g.Nearest(s, needSight: false);
                var count = search.Find(g, si, gi, path, maxExpand: 20000);
                if (count == 0)
                    return float.PositiveInfinity;
                var len = Vector3.Distance(s, g.Nodes[path[0]]) + Vector3.Distance(g.Nodes[path[count - 1]], goal);
                for (var k = 1; k < count; k++)
                    len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
                if (keepRoute)
                    for (var k = 0; k < count; k++)
                        routeNodes[team].Add(g.Nodes[path[k]]);
                total += len;
                n++;
            }
            return n > 0 ? total / n : float.PositiveInfinity;
        }
        var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, true)).ToList();
        var walks = new List<string>();
        foreach (var (name, at, keep) in targets)
        {
            float blue = Walk(0, at, keep), red = Walk(1, at, keep);
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0;-0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.1f)
                Fail($"{m.Name}: {name} at ({at.X:0},{at.Z:0}) is a {blue:0} m walk for Blue and {red:0} m for Red (more than 10% apart)");
        }
        {
            float blue = Walk(0, o.Flags[1], false), red = Walk(1, o.Flags[0], false);
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"enemy flag {blue:0}/{red:0} m ({diff * 100:+0;-0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.1f)
                Fail($"{m.Name}: the enemy flag is a {blue:0} m walk for Blue and {red:0} m for Red (more than 10% apart)");
        }

        // Cover parity: solid cover at least 0.9 m tall within 6 m of each team's routes to the hills.
        var cover = new List<Vector3>();
        foreach (var p in props.Where(p => p.Def.IsSolid && p.Bounds.Max.Y - m.GroundHeight(p.Position.X, p.Position.Z) >= 0.9f))
            cover.Add(p.Bounds.Center);
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && m.Solid[i].Max.Y - m.GroundHeight(m.Solid[i].Center.X, m.Solid[i].Center.Z) >= 0.9f)
                cover.Add(m.Solid[i].Center);
        int CoverNear(List<Vector3> route)
        {
            var c = 0;
            foreach (var q in cover)
                foreach (var r in route)
                    if ((q.X - r.X) * (q.X - r.X) + (q.Z - r.Z) * (q.Z - r.Z) < 36f)
                    {
                        c++;
                        break;
                    }
            return c;
        }
        int coverBlue = CoverNear(routeNodes[0]), coverRed = CoverNear(routeNodes[1]);
        var coverRatio = MathF.Min(coverBlue, coverRed) / (float)Math.Max(1, Math.Max(coverBlue, coverRed));
        ClutteredLine($"{m.Name} balance (Blue/Red, nav walk averaged over team spawns): {string.Join(", ", walks)}; cover within 6 m of the routes to the hills {coverBlue}/{coverRed} ({coverRatio:P0})");
        if (coverRatio < 0.7f)
            Fail($"{m.Name}: uneven cover on the routes to the hills (Blue {coverBlue}, Red {coverRed})");

        // The nav graph reaches every spawn, hill and flag.
        var missing = new List<string>();
        void Reach(string what, Vector3 p)
        {
            var reach = MathF.Max(3f, g.Spacing * 1.5f);
            for (var i = 0; i < g.Nodes.Length; i++)
            {
                var q = g.Nodes[i];
                if (g.Component[i] == g.MainComponent && MathF.Abs(q.Y - p.Y) < 1.2f && (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) < reach * reach)
                    return;
            }
            missing.Add($"{what} ({p.X:0},{p.Z:0})");
        }
        foreach (var s in m.Spawns)
            Reach("spawn", s);
        foreach (var team in o.TeamSpawns)
            foreach (var s in team)
                Reach("team spawn", s);
        foreach (var h in o.Hills)
            Reach("hill", h.Center);
        for (var i = 0; i < o.Flags.Length; i++)
            Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
        ClutteredLine($"{m.Name} nav: {g.Nodes.Length} nodes, {g.EdgeCount} edges, main area {100f * g.MainNodes.Length / Math.Max(1, g.Nodes.Length):0}%, built in {g.BuildMs:0} ms");
        if (missing.Count > 0)
            Fail($"{m.Name}: the nav graph doesn't reach {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? $" and {missing.Count - 5} more" : "")}");

        // A full 32-player bot match: the server tick stays under budget with every prop and plant in place.
        ClutteredBotMatch(m, m.MaxPlayers, 60, needKills: false);
    }
}
