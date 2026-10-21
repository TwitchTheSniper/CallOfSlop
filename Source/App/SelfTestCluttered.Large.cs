using System.Diagnostics;
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
        if (!ClutteredLargeBasics(m, BoundaryShape.Smooth, 32, minProps: 3000, minSolidProps: 400, minPlants: 60000))
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
        var g = ClutteredLargeNav(m);
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
            walks.Add($"{name} {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
            if (!float.IsFinite(blue + red) || MathF.Abs(diff) > 0.1f)
                Fail($"{m.Name}: {name} at ({at.X:0},{at.Z:0}) is a {blue:0} m walk for Blue and {red:0} m for Red (more than 10% apart)");
        }
        {
            float blue = Walk(0, o.Flags[1], false), red = Walk(1, o.Flags[0], false);
            var diff = (blue - red) / MathF.Max(blue, red);
            walks.Add($"enemy flag {blue:0}/{red:0} m ({diff * 100:+0.0;-0.0;0.0}%)");
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
        ClutteredLargeBotMatch(m, m.MaxPlayers, 60, needKills: false);
    }

    /// <summary>
    /// The large map's basic checks (written to suit any map of the pack): the boundary shape, the player limit and hand-placed spawns for it, hand-placed
    /// objectives (with level hill zones and no solid prop on a spawn, hill or flag), density thresholds and that every
    /// spawn, waypoint, prop, plant and objective is inside the outline. Returns false if the map can't be checked further.
    /// </summary>
    private static bool ClutteredLargeBasics(Map m, BoundaryShape shape, int maxPlayers, int minProps, int minSolidProps, int minPlants)
    {
        var b = m.Boundary;
        if (b == null || b.Spec.Shape == BoundaryShape.Rect)
        {
            Fail($"{m.Name}: needs a {shape} boundary, has {(b == null ? "the plain rectangle" : "a rect boundary")}");
            return false;
        }
        if (b.Spec.Shape != shape)
            Fail($"{m.Name}: boundary is {b.Spec.Shape}, expected {shape}");
        if (m.MaxPlayers != maxPlayers || m.MaxPlayersSetting != maxPlayers)
            Fail($"{m.Name}: player limit {m.MaxPlayers} (file {m.MaxPlayersSetting}), expected {maxPlayers}");
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < maxPlayers)
            Fail($"{m.Name}: {authored} hand-placed spawns for {maxPlayers} players");
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");

        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var plants = m.FoliageInstances.Length;
        if (props.Count < minProps || solid < minSolidProps || plants < minPlants)
            Fail($"{m.Name}: not cluttered enough ({props.Count} props, {solid} solid, {plants} plants; expected at least {minProps}, {minSolidProps}, {minPlants})");

        // Everything inside the outline (the cosmetic scenery meshes beyond the edge are deliberate, so they don't count).
        var outside = new List<string>();
        void In(string what, float x, float z)
        {
            if (!b.Inside(x, z))
                outside.Add($"{what} ({x:0.0},{z:0.0})");
        }
        foreach (var s in m.Spawns)
            In("spawn", s.X, s.Z);
        foreach (var w in m.Waypoints)
            In("waypoint", w.X, w.Z);
        foreach (var p in props)
            In(p.Def.Name, p.Position.X, p.Position.Z);
        foreach (var f in m.FoliageInstances)
            In(f.Type.ToString(), f.Position.X, f.Position.Z);
        foreach (var team in o.TeamSpawns)
            foreach (var s in team)
                In("team spawn", s.X, s.Z);
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z);
        foreach (var f in o.Flags)
            In("flag", f.X, f.Z);
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(5))}");

        // Hill zones on level ground with no solid prop or box in them; nothing solid on a spawn or a flag.
        foreach (var h in o.Hills)
            for (var k = 0; k < 8; k++)
            {
                var a = k * MathF.PI / 4;
                var p = h.Center + new Vector3(MathF.Cos(a), 0, MathF.Sin(a)) * h.Radius * 0.8f;
                var dy = m.GroundHeight(p.X, p.Z) - h.Center.Y;
                if (dy is < -1f or > 3f)
                {
                    Fail($"{m.Name}: hill at ({h.Center.X:0},{h.Center.Z:0}) is on ground too uneven for its zone ({dy:0.0} m at its edge)");
                    break;
                }
            }
        static float Dist(Box bx, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(bx.Min.X - p.X, p.X - bx.Max.X));
            var dz = MathF.Max(0, MathF.Max(bx.Min.Z - p.Z, p.Z - bx.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        for (var i = 0; i < m.Solid.Count; i++)
        {
            if (m.Boundary != null && i == m.GroundIndex)
                continue;
            var bx = m.Solid[i];
            string? why = null;
            foreach (var sp in m.Spawns)
                if (why == null && Dist(bx, sp) < 0.5f && bx.Max.Y > sp.Y + 0.6f && bx.Min.Y < sp.Y + World.PlayerHeight)
                    why = $"spawn ({sp.X:0.0},{sp.Z:0.0})";
            foreach (var h in o.Hills)
                if (why == null && Dist(bx, h.Center) < h.Radius && bx.Max.Y > h.Center.Y - 1f + World.StepHeight && bx.Min.Y < h.Center.Y + 3f)
                    why = $"hill zone ({h.Center.X:0},{h.Center.Z:0})";
            foreach (var f in o.Flags)
                if (why == null && Dist(bx, f) < 1.5f && bx.Max.Y > f.Y + 0.6f)
                    why = $"flag ({f.X:0},{f.Z:0})";
            if (why != null)
                blocked.Add($"{(m.IsPropCollider(i) ? "prop" : "box")} at ({bx.Center.X:0.0},{bx.Center.Z:0.0}) on the {why}");
        }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid thing(s) in the way: {string.Join("; ", blocked.Take(4))}");
        return true;
    }

    /// <summary>Waits for the map's nav graph (built in the background) and returns it.</summary>
    private static NavGraph ClutteredLargeNav(Map m)
    {
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        return NavGraph.Current;
    }

    /// <summary>
    /// A bot match on the map with <paramref name="players"/> players (host + bots) for <paramref name="seconds"/>:
    /// nobody stuck, outside the outline or under the ground, and no server tick over the CPU budget. Returns the kills.
    /// </summary>
    private static int ClutteredLargeBotMatch(Map m, int players, double seconds, bool needKills)
    {
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: players - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
        game.MapVoteEnabled = false;
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var moved = new Dictionary<byte, float>();
        var last = new Dictionary<byte, Vector3>();
        int outside = 0, fallen = 0, kills = 0, lastKills = 0, lastMatch = game.MatchNumber;
        var cpu = new TickCpu();
        var wall = new List<double>();
        var b = m.Boundary;
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < seconds * World.TickRate; i++)
        {
            now += dt;
            cpu.Start();
            var t0 = Stopwatch.GetTimestamp();
            game.Update(now, dt);
            var ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            var tick = i;
            cpu.Stop(ms, count: i > 60, () => $"t={tick / 60.0:0.00} s, {game.Phase}");
            if (i > 60)
                wall.Add(ms);
            if (game.MatchNumber != lastMatch)
            {
                kills += lastKills;
                lastMatch = game.MatchNumber;
            }
            lastKills = game.Players.Values.Sum(p => (int)p.Kills);
            foreach (var p in game.Players.Values)
            {
                if (!p.Alive)
                    continue;
                if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                {
                    Fail($"{m.Name}: player {p.Id} position is NaN");
                    return kills;
                }
                if (b != null && b.SignedDistance(p.Position.X, p.Position.Z) > -World.PlayerRadius + 0.01f)
                    outside++;
                if (p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z) - 0.5f)
                    fallen++;
            }
            if (i % 60 == 0)
                foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                {
                    if (last.TryGetValue(p.Id, out var lp))
                        moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                    last[p.Id] = p.Position;
                }
        }
        kills += lastKills;
        wall.Sort();
        var stuck = moved.Count(kv => kv.Value < 15f);
        ClutteredLine($"bot match on {m.Name} ({seconds:0} s, {game.Players.Count} players): kills {kills}, avg bot travel {(moved.Count == 0 ? 0 : moved.Values.Average()):0} m, "
            + $"stuck {stuck}, player-ticks outside {outside}, below ground {fallen}, tick wall median {(wall.Count > 0 ? wall[wall.Count / 2] : 0):0.00} ms, "
            + $"p99 {(wall.Count > 0 ? wall[(int)(wall.Count * 0.99)] : 0):0.00} ms, worst work {cpu.WorstText} ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != players)
            Fail($"{m.Name}: {game.Players.Count} players in the bot match, expected {players}");
        if (stuck > Math.Max(1, players / 16))
            Fail($"{m.Name}: {stuck} bots barely moved (stuck on clutter?)");
        if (outside > 0 || fallen > 0)
            Fail($"{m.Name}: players left the map ({outside} player-ticks outside the outline, {fallen} below the ground)");
        if (needKills && kills == 0)
            Fail($"{m.Name}: no kills in {seconds:0} s with {players - 1} bots");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a server tick with {game.Players.Count} players did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        return kills;
    }
}
