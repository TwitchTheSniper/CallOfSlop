using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Space pack, 11 Frostvault (Maps/Space/11-Frostvault.json) and 12 Vesper Strip (Maps/Space/12-VesperStrip.json):
/// the cryo-vault facility (a hexagon, turned 180 degrees about the centre) and the spaceport bazaar (a bean, mirrored
/// about x = 0). For each: the outline's shape, size and player limit; prop, decal and mesh counts above a floor and
/// the drawn box count under a ceiling; nothing outside the outline; hand-placed objectives on the symmetry line and
/// on level floor; every solid prop with its symmetric twin; the levels (the vault's pit, podium and gallery; the
/// strip's rooftops) really there and reachable by bots; equal walks for both teams; a short seeded bot match.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> SpaceVbNotes = new();

    private static void SpaceVbLine(string text)
    {
        Line(text);
        SpaceVbNotes.Add(text);
    }

    private static void CheckSpaceVaultBazaar()
    {
        if (!Directory.Exists(Maps.Folder))
            return;
        SpaceVbNotes.Clear();
        CheckFrostvault();
        CheckVesperStrip();
        World.SetMap(Maps.All[0]);
        if (SpaceVbNotes.Count > 0)
            Line("::notice title=Space: vault and bazaar::" + string.Join("%0A", SpaceVbNotes.Select(n => n.Replace("%", "%25").Replace("\r", "").Replace("\n", " "))));
    }

    /// <summary>The checks both maps share. <paramref name="mirrorX"/>: twins are at (-x, z) with yaw negated; otherwise at (-x, -z) with yaw + 180.</summary>
    private static NavGraph? SpaceVbCommon(Map m, bool mirrorX, int minSolid, int minCosmetic, int minDecals, int maxBoxes, int players)
    {
        var b = m.Boundary!;
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var cosmetic = props.Count - solid;
        var breakable = props.Count(p => p.IsBreakable);
        var boxes = Enumerable.Range(0, m.Solid.Count).Count(i => m.IsDrawnBox(i) && !m.IsTrunk(i));
        var meshes = m.MeshPlacements.Count - m.BoundaryMeshCount;
        SpaceVbLine($"{m.Name}: {m.Size}, {b.Spec.Shape.ToString().ToLowerInvariant()} {b.Area:0} m2 ({m.HalfX * 2:0} x {m.HalfZ * 2:0} m), max {m.MaxPlayers}; "
            + $"{props.Count} props ({solid} solid, {cosmetic} cosmetic, {breakable} breakable, {props.Count - m.ClutterStart} clutter; "
            + $"{props.Count(p => p.Def.Pack != null)} pack-local), {m.DecalCount} decals, {meshes} meshes, {boxes} boxes drawn, {m.Solid.Count} solid boxes, "
            + $"{m.Spawns.Count} spawns, {m.Waypoints.Count} waypoints; by set: "
            + string.Join(", ", props.GroupBy(p => p.Def.Category).OrderByDescending(g => g.Count()).Take(8).Select(g => $"{g.Key} {g.Count()}")));
        if (m.Size != MapSize.Medium || m.MaxPlayers != players || m.MaxPlayersSetting != players)
            Fail($"{m.Name}: {m.Size}, {m.MaxPlayers} players (expected Medium, \"maxPlayers\": {players})");
        if (solid < minSolid || cosmetic < minCosmetic || m.DecalCount < minDecals)
            Fail($"{m.Name}: not dressed enough (solid props {solid}/{minSolid}, cosmetic {cosmetic}/{minCosmetic}, decals {m.DecalCount}/{minDecals})");
        if (boxes > maxBoxes)
            Fail($"{m.Name}: {boxes} boxes drawn (each is a draw call; keep it under {maxBoxes} and use props and meshes)");

        // Nothing outside the outline.
        var outside = new List<string>();
        foreach (var p in props)
            if (!b.Contains(p.Position.X, p.Position.Z))
                outside.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0})");
        for (var i = 0; i < m.Solid.Count; i++)
            if (m.IsDrawnBox(i) && !b.Contains(m.Solid[i].Center.X, m.Solid[i].Center.Z))
                outside.Add($"box ({m.Solid[i].Center.X:0.0},{m.Solid[i].Center.Z:0.0})");
        foreach (var w in m.Waypoints)
            if (!b.Contains(w.X, w.Z))
                outside.Add($"waypoint ({w.X:0.0},{w.Z:0.0})");
        foreach (var s in m.Spawns)
            if (!b.Contains(s.X, s.Z))
                outside.Add($"spawn ({s.X:0.0},{s.Z:0.0})");
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(5))}");

        // Objectives: hand-placed, on the symmetry line, not inside anything solid.
        var o = m.Objectives;
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: team spawns, hills and flags should all be hand-placed");
        if (o.Hills.Count != 3 || o.Flags.Length != 2 || o.TeamSpawns[0].Count < players / 2 || o.TeamSpawns[1].Count < players / 2)
            Fail($"{m.Name}: {o.Hills.Count} hills, {o.Flags.Length} flags, {o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count} team spawns (expected 3, 2, {players / 2} + {players / 2})");
        if (o.Hills.Any(h => MathF.Abs(h.Center.X) > 0.05f))
            Fail($"{m.Name}: the hills should sit on the symmetry line x = 0");
        if ((m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count) < players)
            Fail($"{m.Name}: {m.AuthoredSpawns} hand-placed spawns ({players} players need {players})");
        static float Gap(Box bx, Vector3 p)
        {
            var dx = MathF.Max(0, MathF.Max(bx.Min.X - p.X, p.X - bx.Max.X));
            var dz = MathF.Max(0, MathF.Max(bx.Min.Z - p.Z, p.Z - bx.Max.Z));
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        var blocked = new List<string>();
        foreach (var p in props.Where(p => p.Def.IsSolid))
            for (var k = p.SolidStart; k < p.SolidStart + p.SolidCount; k++)
            {
                var bx = m.Solid[k];
                var why = m.Spawns.Any(s => Gap(bx, s) < 0.8f && bx.Max.Y > s.Y + 0.6f && bx.Min.Y < s.Y + 1.5f) ? "a spawn"
                    : o.Hills.Any(h => Gap(bx, h.Center) < 1.5f && bx.Max.Y > h.Center.Y + 0.6f && bx.Min.Y < h.Center.Y + 1.5f) ? "a hill's centre"
                    : o.Flags.Any(f => Gap(bx, f) < 1.5f && bx.Max.Y > f.Y + 0.6f && bx.Min.Y < f.Y + 1.5f) ? "a flag" : null;
                if (why != null)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on {why}");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");

        // Fairness: every hand-placed solid prop has its symmetric twin (Clutter mirrors its own).
        var unmatched = new List<string>();
        var solidIdx = Enumerable.Range(0, m.ClutterStart).Where(i => props[i].Def.IsSolid).ToList();
        var used = new HashSet<int>();
        foreach (var i in solidIdx)
        {
            if (used.Contains(i))
                continue;
            var a = props[i];
            var onAxis = mirrorX ? MathF.Abs(a.Position.X) < 0.05f : MathF.Abs(a.Position.X) < 0.05f && MathF.Abs(a.Position.Z) < 0.05f;
            if (onAxis)
            {
                used.Add(i);
                continue;
            }
            var twin = solidIdx.FirstOrDefault(j => j != i && !used.Contains(j) && props[j].Def == a.Def
                && MathF.Abs(props[j].Position.X + a.Position.X) < 0.05f
                && MathF.Abs(mirrorX ? props[j].Position.Z - a.Position.Z : props[j].Position.Z + a.Position.Z) < 0.05f
                && MathF.Abs(props[j].Position.Y - a.Position.Y) < 0.05f && MathF.Abs(props[j].Scale - a.Scale) < 0.01f
                && MathF.Abs(MathF.IEEERemainder(mirrorX ? props[j].Yaw + a.Yaw : props[j].Yaw - a.Yaw - MathF.PI, MathF.Tau)) < 0.02f, -1);
            if (twin < 0)
                unmatched.Add($"{a.Def.Name} ({a.Position.X:0.0},{a.Position.Z:0.0})");
            else
            {
                used.Add(i);
                used.Add(twin);
            }
        }
        if (unmatched.Count > 0)
            Fail($"{m.Name}: {unmatched.Count} solid prop(s) without a {(mirrorX ? "mirrored" : "rotated")} twin: {string.Join(", ", unmatched.Take(5))}");

        // Nav: everything on the main area, equal walks for both teams.
        World.SetMap(m);
        for (var wait = 0; wait < 4000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
        var search = new NavSearch();
        var path = new int[g.Nodes.Length + 1];
        float Walk(Vector3 from, Vector3 to)
        {
            var n = search.Find(g, g.Nearest(from, needSight: false), g.Nearest(to, needSight: false), path, maxExpand: g.Nodes.Length);
            if (n == 0)
                return float.NaN;
            var len = 0f;
            for (var k = 1; k < n; k++)
                len += Vector3.Distance(g.Nodes[path[k - 1]], g.Nodes[path[k]]);
            return len;
        }
        float TeamWalk(int team, Vector3 to) => o.TeamSpawns[team].Sum(s => Walk(s, to)) / o.TeamSpawns[team].Count;
        var walks = new List<string>();
        void Compare(string what, float blue, float red)
        {
            walks.Add($"{what} {blue:0}/{red:0} m");
            if (!float.IsFinite(blue) || !float.IsFinite(red))
                Fail($"{m.Name}: no route to {what} (blue {blue:0}, red {red:0})");
            else if (MathF.Abs(blue - red) > 0.1f * MathF.Max(blue, red))
                Fail($"{m.Name}: unfair walk to {what}: blue {blue:0.0} m, red {red:0.0} m (over 10% apart)");
        }
        for (var h = 0; h < o.Hills.Count; h++)
            Compare($"hill {h + 1}", TeamWalk(0, o.Hills[h].Center), TeamWalk(1, o.Hills[h].Center));
        if (o.Flags.Length == 2)
            Compare("enemy flag", TeamWalk(0, o.Flags[1]), TeamWalk(1, o.Flags[0]));
        var mainPct = g.Nodes.Length == 0 ? 0 : 100f * g.MainNodes.Length / g.Nodes.Length;
        SpaceVbLine($"{m.Name}: nav {g.Nodes.Length} nodes, {g.EdgeCount} edges, {g.MantleEdges} mantles, main {mainPct:0}%, built in {g.BuildMs:0} ms "
            + $"({g.WalkChecked} links walked); walks blue/red: {string.Join(", ", walks)}");
        var offMain = m.Spawns.Concat(o.Hills.Select(h => h.Center)).Concat(o.Flags)
            .Where(p => g.Nearest(p, needSight: false) is var k && (k < 0 || g.Component[k] != g.MainComponent)).ToList();
        if (offMain.Count > 0)
            Fail($"{m.Name}: {offMain.Count} spawn(s) or objective(s) off the main nav area: {string.Join(", ", offMain.Take(4).Select(p => $"({p.X:0},{p.Y:0.0},{p.Z:0})"))}");
        if (mainPct < 90)
            Fail($"{m.Name}: only {mainPct:0}% of the nav nodes are on the main area (pockets bots can't leave?)");
        // DEBUG
        var weak = Enumerable.Range(0, g.Nodes.Length).Where(i => g.Component[i] == g.MainComponent && !g.MainNodes.Contains(i)).ToList();
        Line($"  DEBUG {weak.Count} nodes in the main component but not strongly connected: " + string.Join(" ", weak.Take(80).Select(i => $"({g.Nodes[i].X:0},{g.Nodes[i].Y:0.0},{g.Nodes[i].Z:0})")));
        foreach (var t in new[] { 0, 1 })
        {
            var n = search.Find(g, g.Nearest(o.TeamSpawns[t][0], needSight: false), g.Nearest(o.Hills[0].Center, needSight: false), path, maxExpand: g.Nodes.Length);
            Line($"  DEBUG team {t} path to hill 1 ({n} nodes): " + string.Join(" ", Enumerable.Range(0, n).Select(k => $"({g.Nodes[path[k]].X:0},{g.Nodes[path[k]].Y:0.0},{g.Nodes[path[k]].Z:0})")));
        }
        foreach (var pt in new[] { new Vector3(-18, 0, 0), new Vector3(18, 0, 0), new Vector3(-13, 1.2f, 0), new Vector3(13, 1.2f, 0), new Vector3(-22, 0, -4), new Vector3(22, 0, 4), new Vector3(-16.2f, 0, 0), new Vector3(16.2f, 0, 0) })
        {
            var k = g.Nearest(pt, needSight: false);
            Line($"  DEBUG node near ({pt.X},{pt.Y},{pt.Z}): ({g.Nodes[k].X:0.0},{g.Nodes[k].Y:0.00},{g.Nodes[k].Z:0.0}) links: "
                 + string.Join(" ", Enumerable.Range(g.EdgeStart[k], g.EdgeStart[k + 1] - g.EdgeStart[k]).Select(e => $"({g.Nodes[g.EdgeTo[e]].X:0.0},{g.Nodes[g.EdgeTo[e]].Y:0.0},{g.Nodes[g.EdgeTo[e]].Z:0.0})")));
        }
        return g;
    }

    /// <summary>A named level of a map: the floor at (x, z) found from above must be at y (within 0.1 m), and a bot must be able to get there.</summary>
    private static void SpaceVbLevels(Map m, NavGraph g, (string Name, float X, float Z, float Y)[] spots)
    {
        var phys = World.Physics;
        var o = m.Objectives;
        var search = new NavSearch();
        var path = new int[g.Nodes.Length + 1];
        var wrong = new List<string>();
        var unreached = new List<string>();
        foreach (var (name, x, z, y) in spots)
        {
            var t = phys.Raycast(new Vector3(x, y + 1.2f, z), -Vector3.UnitY, 3f, QueryFilter.Navigation);
            var floor = t >= 3f ? float.NaN : y + 1.2f - t;
            if (!(MathF.Abs(floor - y) < 0.1f))
                wrong.Add($"{name} ({x:0},{z:0}): floor at {floor:0.00}, expected {y:0.00}");
            var at = new Vector3(x, y, z);
            var k = g.Nearest(at, needSight: false);
            if (k < 0 || Vector3.Distance(g.Nodes[k], at) > 3f || g.Component[k] != g.MainComponent)
                unreached.Add($"{name} ({x:0},{y:0.0},{z:0}): {(k < 0 ? "no node" : $"nearest node ({g.Nodes[k].X:0.0},{g.Nodes[k].Y:0.00},{g.Nodes[k].Z:0.0}) {(g.Component[k] != g.MainComponent ? "off the main area" : "too far")}")}");
            else if (search.Find(g, g.Nearest(o.TeamSpawns[0][0], needSight: false), k, path, maxExpand: g.Nodes.Length) == 0)
                unreached.Add($"{name}: no route from Blue's first spawn");
        }
        if (wrong.Count > 0)
            Fail($"{m.Name}: levels not where expected: {string.Join("; ", wrong)}");
        if (unreached.Count > 0)
            Fail($"{m.Name}: bots can't reach {string.Join("; ", unreached)}");
    }

    /// <summary>A short seeded bot match (free-for-all, a full server): no crash, no one below the floor or outside, the tick within budget.</summary>
    private static void SpaceVbBotMatch(Map m, int seconds)
    {
        var sw = Stopwatch.StartNew();
        using var game = ModeGame(m.MaxPlayers - 1, GameMode.FreeForAll, m.Id);
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
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
                cpu.Stop(ms, count: i > 60 && !jit);
                if (i > 60 && !jit)
                    maxTickMs = Math.Max(maxTickMs, ms);
            }
        }
        catch (Exception ex)
        {
            Fail($"{m.Name}: bot match crashed: {ex}");
            return;
        }
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        var fallen = game.Players.Values.Count(p => p.Alive && p.Position.Y < -0.5f);
        var escaped = game.Players.Values.Count(p => p.Alive && !m.Boundary!.Inside(p.Position.X, p.Position.Z, -0.5f));
        SpaceVbLine($"{m.Name}: {seconds} s free-for-all with {game.Players.Count} players: {kills} kills, {fallen} below the floor, {escaped} outside, "
                    + $"worst tick {maxTickMs:0.00} ms (work {cpu.WorstText}; {jitTicks} JIT ticks not counted), {m.BrokenProps.Count} props broken ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != m.MaxPlayers)
            Fail($"{m.Name}: the match has {game.Players.Count} players (expected {m.MaxPlayers})");
        if (fallen > 0 || escaped > 0)
            Fail($"{m.Name}: {fallen} player(s) below the floor, {escaped} outside the outline");
        if (TickCpu.Available && cpu.WorstMs > 8)
            Fail($"{m.Name}: a server tick did {cpu.WorstMs:0.0} ms of work (budget 8 ms): {cpu.WorstDetail}");
        m.RestoreProps();
    }

    // ---------------------------------------------------------------------------------------------------- 11 Frostvault

    private static void CheckFrostvault()
    {
        if (Maps.Find("Frostvault") is not { } m)
        {
            Fail("Maps/Space/11-Frostvault.json ('Frostvault') didn't load");
            return;
        }
        if (m.Pack?.Name != "Space")
            Fail($"{m.Name} isn't in the Space pack");
        var b = m.Boundary;
        if (b == null || b.Spec.Shape != BoundaryShape.Polygon || m.Terrain != null)
        {
            Fail($"{m.Name}: expected a flat map with a \"polygon\" (hexagon) boundary, got {(b == null ? "none" : b.Spec.Shape.ToString())}");
            return;
        }
        // A hexagon: the corners at (+-64, 0) are inside, the square's corners (+-60, +-50) are not, and the area is a hexagon's.
        if (!b.Contains(-62, 0) || !b.Contains(62, 0) || !b.Contains(0, -53) || b.Contains(-60, -50) || b.Contains(60, 50) || b.Area is < 10000 or > 11200)
            Fail($"{m.Name}: not the hexagon expected (area {b.Area:0} m2)");
        var g = SpaceVbCommon(m, mirrorX: false, minSolid: 180, minCosmetic: 60, minDecals: 300, maxBoxes: 400, players: 16);
        if (g == null)
            return;
        // Pack-local props and decals are used.
        var packProps = m.PropInstances.Count(p => p.Def.Pack == "Space");
        if (packProps < 100 || !m.PropInstances.Any(p => p.Def.Name == "Cryo Pod"))
            Fail($"{m.Name}: {packProps} pack-local props placed (expected the Frostvault set, cryo pods among them)");
        if (!m.DecalRequests.Any(r => r.Names.Any(n => n.StartsWith("Frostvault Sign", StringComparison.Ordinal))) || !m.DecalRequests.Any(r => r.Names.Contains("Frostvault Vault Ring")))
            Fail($"{m.Name}: the Frostvault signs and vault ring decals aren't placed");
        // Levels: the pit (0), the podium (1.2), the corridor (0), the gallery and lab roofs (3.5), the labs (0).
        SpaceVbLevels(m, g,
        [
            ("pit", 0, -3, 0f), ("podium NW", -11, -11, 1.2f), ("podium SE", 11, 11, 1.2f), ("corridor N", 0, -17.5f, 0f), ("corridor E", 17.5f, 5, 0f),
            ("gallery W", -17.5f, 0, 3.5f), ("gallery S", 0, 17.5f, 3.5f), ("gallery NE corner", 16.5f, -17.5f, 3.5f), ("lab N roof", 0, -42, 3.5f),
            ("lab S roof", 0, 42, 3.5f), ("lab bridge N", 0, -28, 3.5f), ("stair landing NW", -23, -15, 3.5f), ("stair landing SE", 23, 15, 3.5f),
            ("lab N", 0, -42, 0f), ("pod hall A", -27, -32, 0f), ("pod hall B", 27, 32, 0f), ("server room NE", 26, -33, 0f), ("server room SW", -26, 33, 0f),
            ("intake W", -52, 0, 0f), ("intake E", 52, 0, 0f), ("north passage", -6, -52, 0f), ("west alley", -40, -30, 0f),
        ]);
        // The pit really is sunken: its floor is 1.2 m under the podium all round, and the hill sits in it.
        if (m.Objectives.Hills.Count == 3 && (m.Objectives.Hills[0].Center.Y > 0.05f || MathF.Abs(m.Objectives.Hills[0].Radius - 6) > 0.1f))
            Fail($"{m.Name}: hill 1 should be the pit floor (y 0, radius 6), got y {m.Objectives.Hills[0].Center.Y:0.0} radius {m.Objectives.Hills[0].Radius:0.0}");
        SpaceVbBotMatch(m, 8);
    }

    // ---------------------------------------------------------------------------------------------------- 12 Vesper Strip

    private static void CheckVesperStrip()
    {
        if (Maps.Find("Vesper Strip") is not { } m)
        {
            Line("  (Maps/Space/12-VesperStrip.json not present yet)");
            return;
        }
        if (m.Pack?.Name != "Space")
            Fail($"{m.Name} isn't in the Space pack");
        var b = m.Boundary;
        if (b == null || b.Spec.Shape != BoundaryShape.Smooth || m.Terrain != null)
        {
            Fail($"{m.Name}: expected a flat map with a \"smooth\" (bean) boundary, got {(b == null ? "none" : b.Spec.Shape.ToString())}");
            return;
        }
        var lobes = b.Contains(-50, 20) && b.Contains(50, 20);
        var dent = !b.Contains(0, 30) && b.Contains(0, 15);
        if (!lobes || !dent || b.Area is < 8500 or > 11000)
            Fail($"{m.Name}: not the bean expected (lobes inside {lobes}, the dent between them outside {dent}, area {b.Area:0} m2)");
        var g = SpaceVbCommon(m, mirrorX: true, minSolid: 200, minCosmetic: 120, minDecals: 250, maxBoxes: 400, players: 16);
        if (g == null)
            return;
        var packProps = m.PropInstances.Count(p => p.Def.Pack == "Space");
        if (packProps < 60 || !m.PropInstances.Any(p => p.Def.Name == "Docked Shuttle"))
            Fail($"{m.Name}: {packProps} pack-local props placed (expected the Vesper set, docked shuttles among them)");
        if (!m.DecalRequests.Any(r => r.Names.Any(n => n.StartsWith("Vesper Neon", StringComparison.Ordinal))))
            Fail($"{m.Name}: the Vesper neon sign decals aren't placed");
        SpaceVbLevels(m, g, VesperLevels);
        SpaceVbBotMatch(m, 8);
    }

    /// <summary>Vesper Strip's levels (filled in by the map's generator notes): the plaza, the pads, the rooftops and the bases.</summary>
    private static readonly (string Name, float X, float Z, float Y)[] VesperLevels =
    [
        ("plaza", 0, -8, 0f), ("centre pad", 0, -34, 0f), ("customs", 0, 16, 0f), ("west base", -56, 0, 0f), ("east base", 56, 0, 0f),
    ];
}
