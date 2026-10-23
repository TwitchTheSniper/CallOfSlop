using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The "Space: shipyard and outpost" section: Space 05 Keelspar Drydock (an orbital shipyard over the void, written by
/// tools/space/keelspar_drydock.py) and Space 06 Regolith Reach (a lunar mining outpost on terrain, written by
/// tools/space/regolith_reach.py). For each: it loads under its file name with a non-rectangular outline and the
/// expected size; team spawns, hills and flags are hand-placed and inside the outline, as are the spawns, waypoints and
/// props; the dressing is above a floor (props, solid props, decals, meshes); solid props keep off spawns, hill zones and
/// flags, explosives off spawns; every named area has floor where the map says (and nothing below the fall-kill
/// height, no nav link over the void, on the drydock); the nav graph reaches every spawn and objective and the walk
/// from each team's spawns to every hill and to the enemy flag is fair (within 10%); the graph builds in reasonable
/// time; and a short seeded bot match (20 s, 12 players) runs without anyone leaving the map or a tick over budget.
/// </summary>
public static partial class SelfTest
{
    private sealed record SpaceYardSpec(string Name, string File, bool Void, bool Terrain, int MinArea, int MaxArea, int Props, int SolidProps,
                                        int Decals, int Meshes, int MaxDrawn, (string Name, float X, float Z, float Y)[] Probes);

    private static readonly SpaceYardSpec[] SpaceYardMaps =
    [
        new("Keelspar Drydock", "05-KeelsparDrydock.json", Void: true, Terrain: false, MinArea: 8000, MaxArea: 14000, Props: 180, SolidProps: 120,
            Decals: 150, Meshes: 1200, MaxDrawn: 120,
            Probes:
            [
                ("bow dock", -60, -4, 20f), ("stern dock", 60, 4, 20f), ("north quay west", -40, -20, 20f), ("north quay east", 40, -20, 20f),
                ("south quay", 0, 20, 20f), ("keel deck west ribs", -32, 0, 18f), ("keel deck inside", 0, -2, 18f), ("keel deck east ribs", 32, 0, 18f),
                ("north scaffold", -10, -8, 24f), ("south scaffold", 10, 8, 24f), ("spine west", -30, 0, 28f), ("crown deck", 0, 0, 28f),
                ("spine east", 30, 0, 28f), ("west gantry north end", -20, -26, 28f), ("west gantry south end", -20, 26, 28f),
                ("east gantry", 20, 10, 28f), ("west hatch landing", -12.7f, 3, 23f), ("east hatch landing", 12.7f, -3, 23f),
            ]),
        new("Regolith Reach", "06-RegolithReach.json", Void: false, Terrain: true, MinArea: 9000, MaxArea: 16000, Props: 260, SolidProps: 140,
            Decals: 120, Meshes: 80, MaxDrawn: 80,
            Probes:
            [
                ("pit floor", 0, 0, -3.5f), ("west base pad", -50, -4, 1.2f), ("east base pad", 50, 4, 1.2f), ("west tunnel", -40, -10, 1.2f),
                ("east tunnel", 40, 10, 1.2f), ("north ore dock", 0, -33, 0.4f), ("south ore dock", 0, 33, 0.4f), ("west landing pad", -26, 30, 0.6f),
                ("east landing pad", 26, -30, 0.6f), ("west solar farm", -26, -43, 0.5f), ("east solar farm", 26, 43, 0.5f),
            ]),
    ];

    private static void CheckSpaceYardAndOutpost()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        var pack = Maps.FindPack("Space");
        if (pack == null)
        {
            Fail("no Space pack (Maps/Space)");
            return;
        }
        var notes = new List<string>();
        foreach (var spec in SpaceYardMaps)
        {
            var m = pack.Maps.FirstOrDefault(x => x.Name == spec.Name);
            if (m == null)
            {
                Fail($"Space: no map \"{spec.Name}\" (Maps/Space/{spec.File})");
                continue;
            }
            CheckSpaceYardMap(m, spec, notes);
        }
        World.SetMap(Maps.All[0]);
        if (notes.Count > 0)
            Line("::notice title=Space shipyard and outpost::" + string.Join("%0A", notes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    private static void CheckSpaceYardMap(Map m, SpaceYardSpec spec, List<string> notes)
    {
        void Note(string text)
        {
            Line(text);
            notes.Add(text);
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).Equals(spec.File, StringComparison.Ordinal))
            Fail($"{m.Name}: file should be Maps/Space/{spec.File} (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape == BoundaryShape.Rect)
        {
            Fail($"{m.Name}: the outline should be a non-rectangular boundary");
            return;
        }
        if (bd.Area < spec.MinArea || bd.Area > spec.MaxArea)
            Fail($"{m.Name}: {bd.Area:0} m2 inside the outline (expected {spec.MinArea}-{spec.MaxArea})");
        if (m.MaxPlayers != 16)
            Fail($"{m.Name}: max players {m.MaxPlayers} (expected 16)");
        if (spec.Void && (m.FallKillY == null || m.GroundIndex >= 0 || m.HasTerrain))
            Fail($"{m.Name}: should be a void map (fallKill, no ground slab, no terrain)");
        if (spec.Terrain && !m.HasTerrain)
            Fail($"{m.Name}: should have terrain");
        if (!spec.Void && m.FallKillY != null)
            Fail($"{m.Name}: shouldn't have a fall-kill height");

        var o = m.Objectives;
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var drawn = Enumerable.Range(0, m.Solid.Count).Count(m.IsDrawnBox);
        var bake = DecalBaker.Bake(m);
        Note($"{m.Name}: {bd.Spec.Shape.ToString().ToLowerInvariant()} outline, {bd.SegmentCount} segments, {bd.Area:0} m2 ({m.Size}), max {m.MaxPlayers}; "
             + $"{m.Solid.Count} boxes ({m.HiddenBoxCount} hidden, {drawn} drawn one by one), {m.PlacedBuildings.Count} buildings, {props.Count} props ({solid} solid, "
             + $"{props.Count(p => p.IsBreakable)} breakable, {props.Count - m.ClutterStart} clutter), {m.MeshPlacements.Count} meshes, decals {bake.Placed}/{bake.Requested}, "
             + $"{m.FoliageInstances.Length} plants, spawns {m.Spawns.Count} (teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}), hills {o.Hills.Count}, waypoints {m.Waypoints.Count}");
        void AtLeast(string what, int have, int need)
        {
            if (have < need)
                Fail($"{m.Name}: {have} {what} (the dressing floor is {need})");
        }
        AtLeast("props", props.Count, spec.Props);
        AtLeast("solid props", solid, spec.SolidProps);
        AtLeast("decals placed", bake.Placed, spec.Decals);
        AtLeast("meshes", m.MeshPlacements.Count, spec.Meshes);
        if (bake.Placed < bake.Requested * 0.8)
            Fail($"{m.Name}: only {bake.Placed} of {bake.Requested} decals found a surface");
        if (drawn > spec.MaxDrawn)
            Fail($"{m.Name}: {drawn} boxes drawn one by one (keep them under {spec.MaxDrawn}: big decks, meshes over colliders, props)");
        if (props.Count(p => p.Def.Category == "Space") < 20)
            Fail($"{m.Name}: only {props.Count(p => p.Def.Category == "Space")} props from the Space set (Props/Space)");

        // Objectives for every mode, placed by hand; enough spawns.
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");
        if (o.TeamSpawns[0].Count < 8 || o.TeamSpawns[1].Count < 8 || o.TeamSpawns[0].Count != o.TeamSpawns[1].Count)
            Fail($"{m.Name}: team spawn lists {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} (want at least 8 each, the same number)");
        if (o.Hills.Count < 3)
            Fail($"{m.Name}: only {o.Hills.Count} hill zones");
        if (m.Spawns.Count < m.MaxPlayers)
            Fail($"{m.Name}: {m.Spawns.Count} spawns for {m.MaxPlayers} players");
        // Point symmetry: Red's spawns, the flags and the hills are Blue's turned 180 degrees about the centre.
        var unpaired = o.TeamSpawns[0].Count(a => !o.TeamSpawns[1].Any(b => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(-b.X, -b.Z)) < 0.3f));
        if (unpaired > 0)
            Fail($"{m.Name}: {unpaired} Blue spawn(s) have no Red twin at (-x, -z)");
        if (Vector2.Distance(new Vector2(o.Flags[0].X, o.Flags[0].Z), new Vector2(-o.Flags[1].X, -o.Flags[1].Z)) > 0.3f)
            Fail($"{m.Name}: the flags aren't point-symmetric");
        var unpairedHills = o.Hills.Count(a => !o.Hills.Any(b => Vector2.Distance(new Vector2(a.Center.X, a.Center.Z), new Vector2(-b.Center.X, -b.Center.Z)) < 0.3f));
        if (unpairedHills > 0)
            Fail($"{m.Name}: {unpairedHills} hill(s) have no twin at (-x, -z)");

        // Everything inside the outline.
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
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z, h.Radius * 0.5f);
        for (var i = 0; i < o.Flags.Length; i++)
            In(i == 0 ? "blue flag" : "red flag", o.Flags[i].X, o.Flags[i].Z, 0.5f);
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(6))}");

        // Solid props keep off spawns, hill zones and flags; explosives keep away from spawns.
        static float Dist(Box b, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(b.Min.X - p.X, p.X - b.Max.X));
            var dz = MathF.Max(0, MathF.Max(b.Min.Z - p.Z, p.Z - b.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        foreach (var p in props.Where(p => p.Def.IsSolid))
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
            {
                var b = m.Solid[k];
                string? why = null;
                foreach (var sp in m.Spawns)
                    if (why == null && Dist(b, sp) < 1f && b.Max.Y > sp.Y + 0.05f && b.Min.Y < sp.Y + World.PlayerHeight)
                        why = $"spawn ({sp.X:0.0},{sp.Z:0.0})";
                foreach (var h in o.Hills)
                    if (why == null && Dist(b, h.Center) < h.Radius && b.Max.Y > h.Center.Y - 1f && b.Min.Y < h.Center.Y + 3f)
                        why = $"hill zone ({h.Center.X:0},{h.Center.Z:0})";
                foreach (var f in o.Flags)
                    if (why == null && Dist(b, f) < 2f && b.Max.Y > f.Y - 1f && b.Min.Y < f.Y + 3f)
                        why = $"flag ({f.X:0},{f.Z:0})";
                if (why != null)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on the {why}");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");
        foreach (var e in props.Where(p => p.Def.Breakable is { ExplodeRadius: > 0 }))
            if (m.Spawns.Any(s => Vector3.Distance(s, e.Position) < 10f))
                Fail($"{m.Name}: explosive {e.Def.Name} at ({e.Position.X:0.0},{e.Position.Z:0.0}) is within 10 m of a spawn");

        // The nav graph: built whole, reaching every spawn and objective, fair walks.
        World.SetMap(m);
        var swNav = System.Diagnostics.Stopwatch.StartNew();
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var phys = World.Physics;
        float Floor(float x, float z, float fromY, float depth = 3f)
        {
            var t = phys.Raycast(new Vector3(x, fromY, z), -Vector3.UnitY, depth, QueryFilter.Navigation);
            return t >= depth ? float.NaN : fromY - t;
        }
        var search = new NavSearch();
        var path = new int[4096];
        float Walk(Vector3 a, Vector3 b)
        {
            var n = search.Find(g, g.Nearest(a, needSight: false), g.Nearest(b, needSight: false), path, maxExpand: 40000);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var i = 1; i < n; i++)
                len += Vector3.Distance(g.Nodes[path[i - 1]], g.Nodes[path[i]]);
            return len;
        }
        var missing = new List<string>();
        void Reach(string what, Vector3 p)
        {
            var k = g.Nearest(p, needSight: false);
            if (k < 0 || Vector3.Distance(g.Nodes[k], p) > MathF.Max(3f, g.Spacing * 1.5f) || g.Component[k] != g.MainComponent)
                missing.Add($"{what} ({p.X:0},{p.Z:0})");
        }
        foreach (var s in m.Spawns)
            Reach("spawn", s);
        foreach (var h in o.Hills)
            Reach("hill", h.Center);
        for (var i = 0; i < o.Flags.Length; i++)
            Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
        foreach (var (name, x, z, y) in spec.Probes)
        {
            var floor = Floor(x, z, y + 0.6f, 1.2f);
            if (!(MathF.Abs(floor - y) < 0.1f))
                Fail($"{m.Name}: no deck at {name} ({x:0},{z:0}): expected {y:0.0} m, found {floor:0.00}");
            Reach(name, new Vector3(x, y, z));
        }
        if (missing.Count > 0)
            Fail($"{m.Name}: the nav graph's main area doesn't reach {string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? $" and {missing.Count - 8} more" : "")}");
        var walks = new List<string>();
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
        var targets = o.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, h.Center)).ToList();
        targets.Add(("enemy flag", o.Flags[1], o.Flags[0]));
        foreach (var (label, forBlue, forRed) in targets)
        {
            float blue = Average(o.TeamSpawns[0], forBlue), red = Average(o.TeamSpawns[1], forRed);
            walks.Add($"{label} {blue:0}/{red:0} m");
            if (float.IsNaN(blue) || float.IsNaN(red))
                Fail($"{m.Name}: no route from a team's spawns to {label}");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"{m.Name}: {label} is {blue:0} m from Blue and {red:0} m from Red (more than 10% apart)");
        }
        if (g.BuildMs > 3000)
            Fail($"{m.Name}: the nav graph took {g.BuildMs:0} ms to build (over 3000: mover walks {g.WalkChecked} links)");

        // Void map: no nav link crosses the void, nothing below the fall-kill height.
        int links = 0, voidLinks = 0;
        if (spec.Void && m.FallKillY is { } killY)
        {
            var example = "";
            for (var i = 0; i < g.Nodes.Length; i++)
                for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
                {
                    var j = g.EdgeTo[e];
                    if (j < i)
                        continue;
                    links++;
                    Vector3 a = g.Nodes[i], b = g.Nodes[j];
                    var top = MathF.Max(a.Y, b.Y) + World.MantleReach + 0.05f;
                    var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)) / 0.5f));
                    for (var s = 0; s <= steps; s++)
                    {
                        var q = Vector3.Lerp(a, b, s / (float)steps);
                        if (!float.IsNaN(Floor(q.X, q.Z, top, World.MantleReach + 0.05f + NavGraph.MaxDrop)))
                            continue;
                        if (voidLinks++ == 0)
                            example = $"({a.X:0.0},{a.Y:0.0},{a.Z:0.0}) -> ({b.X:0.0},{b.Y:0.0},{b.Z:0.0}) at ({q.X:0.0},{q.Z:0.0})";
                        break;
                    }
                }
            if (voidLinks > 0)
                Fail($"{m.Name}: {voidLinks} of {links} nav links cross the void, e.g. {example}");
            var low = g.Nodes.Count(n => n.Y < killY + 1f) + m.Spawns.Count(s => s.Y < killY + 1f);
            if (low > 0)
                Fail($"{m.Name}: {low} nav nodes or spawns below the fall-kill height");
        }
        Note($"{m.Name} nav: {g.Nodes.Length} nodes ({Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent)} in the main area), "
             + $"{g.EdgeCount} edges, built in {g.BuildMs:0} ms ({swNav.Elapsed.TotalSeconds:0.0} s waited){(spec.Void ? $", {voidLinks}/{links} links over the void" : "")}; "
             + $"walks Blue/Red: {string.Join(", ", walks)}");

        // A short seeded bot match: nobody leaves the map, no tick over the work budget.
        SpaceYardBotMatch(m, 12, 20, Note);
    }

    private static void SpaceYardBotMatch(Map m, int players, double seconds, Action<string> note)
    {
        Game.SeedNextGame = 6500 + m.Id * 17 + players;
        using var game = new Game(NetPeer.Offline(), new Audio(), "SelfTest", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id,
            bots: players - 1, botSkill: BotDifficulty.Normal, mapId: m.Id);
        game.MapVoteEnabled = false;
        NavGraph.Pump(double.MaxValue);
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var cpu = new TickCpu();
        int outside = 0, fallen = 0, deaths = 0;
        var b = m.Boundary!;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            for (var i = 0; i < seconds * World.TickRate; i++)
            {
                now += dt;
                var jit0 = System.Runtime.JitInfo.GetCompilationTime(currentThread: true);
                cpu.Start();
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                game.Update(now, dt);
                var ms = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                var jit = (System.Runtime.JitInfo.GetCompilationTime(currentThread: true) - jit0).TotalMilliseconds > 1;
                cpu.Stop(ms, count: i > 60 && !jit);
                foreach (var p in game.Players.Values)
                {
                    if (!p.Alive)
                        continue;
                    if (!float.IsFinite(p.Position.X + p.Position.Y + p.Position.Z))
                    {
                        Fail($"{m.Name}: player {p.Id} position is NaN");
                        return;
                    }
                    if (b.SignedDistance(p.Position.X, p.Position.Z) > -World.PlayerRadius + 0.01f)
                        outside++;
                    if (m.FallKillY is { } kill ? p.Position.Y < kill - 0.5f : p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z) - 0.5f)
                        fallen++;
                }
            }
        }
        catch (Exception ex)
        {
            Fail($"{m.Name}: the bot match crashed: {ex}");
            return;
        }
        deaths = game.Players.Values.Sum(p => (int)p.Deaths);
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        note($"{m.Name} bot match ({seconds:0} s, {game.Players.Count} players, {game.Phase}): kills {kills}, deaths {deaths}, fall kills {game.FallKills}, "
             + $"player-ticks outside {outside}, below the floor {fallen}, worst tick work {cpu.WorstText} ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != players)
            Fail($"{m.Name}: {game.Players.Count} players in the bot match, expected {players}");
        if (outside > 0 || fallen > 0)
            Fail($"{m.Name}: players left the map ({outside} player-ticks outside the outline, {fallen} below the floor)");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a server tick with {game.Players.Count} players did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        m.RestoreProps();
    }
}
