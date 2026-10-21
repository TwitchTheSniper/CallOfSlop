using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Cluttered pack's small map, 01 Carousel (a circle, for 10 players): the outline is a circle, the object counts
/// are above its density quota, nothing (spawns, waypoints, boxes, props, plants, objectives) lies outside the outline,
/// the objectives are hand-placed, the nav graph reaches them, and a free-for-all and a team bot match at the player
/// limit run with kills, without stuck, fallen or escaped bots, and within the tick work budget.
/// </summary>
public static partial class SelfTest
{
    /// <summary>The thresholds a Cluttered map must reach (its "density test").</summary>
    private readonly record struct SmallClutterQuota(int Props, int SolidProps, int CosmeticProps, int Breakable, int Plants, int Trees, int Boxes);

    /// <summary>01 Carousel: the small map, a circle, for 10 players.</summary>
    static partial void CheckClutteredSmall()
    {
        var pack = Maps.FindPack(ClutteredPack);
        var m = pack?.Maps.FirstOrDefault(x => x.Name == "Carousel");
        if (m == null)
        {
            Fail($"{ClutteredPack}: no map \"Carousel\" (01-Carousel.json)");
            return;
        }
        if (m.SourceFile == null || !Path.GetFileName(m.SourceFile).StartsWith("01-", StringComparison.Ordinal))
            Fail($"Carousel: file should be Maps/Cluttered/01-Carousel.json (is {m.SourceFile})");
        if (m.Boundary is not { } bd || bd.Spec.Shape != BoundaryShape.Circle)
        {
            Fail($"Carousel: boundary should be a circle (is {(m.Boundary == null ? "none (a rectangle)" : m.Boundary.Spec.Shape.ToString())})");
            return;
        }
        if (m.Size != MapSize.Small)
            Fail($"Carousel: size class {m.Size} (expected Small)");
        if (m.MaxPlayersSetting != 10 || m.MaxPlayers != 10)
            Fail($"Carousel: maxPlayers {m.MaxPlayersSetting} in the file, {m.MaxPlayers} in effect (expected 10)");
        if (m.Movers.Count != 1)
            Fail($"Carousel: {m.Movers.Count} movers (expected the carousel)");
        // Plants: 13,500 since the foliage no-go rules took the grass out of the stalls, trucks and bandstands (was 14,000).
        CheckClutteredSmallMap(m, new SmallClutterQuota(Props: 1500, SolidProps: 250, CosmeticProps: 1200, Breakable: 600, Plants: 13500, Trees: 12, Boxes: 300));
    }

    /// <summary>
    /// The checks every Cluttered map gets: counts against its quota, everything inside the outline, hand-placed
    /// objectives for every mode, the nav graph reaching them, and a free-for-all and a team bot match at the map's
    /// player limit (kills, no stuck or fallen bots, the 8 ms tick work budget).
    /// </summary>
    private static void CheckClutteredSmallMap(Map m, SmallClutterQuota quota)
    {
        var bd = m.Boundary!;
        var props = m.PropInstances;
        var solid = props.Count(p => p.Def.IsSolid);
        var breakable = props.Count(p => p.IsBreakable);
        var plants = m.FoliageInstances;
        var trees = plants.Count(f => Foliage.IsTree(f.Type));
        var byType = string.Join(", ", Enum.GetValues<FoliageType>().Select(t => $"{plants.Count(f => f.Type == t)} {t.ToString().ToLowerInvariant()}"));
        var o = m.Objectives;
        ClutteredLine($"{m.Name}: {bd.Spec.Shape} outline, {bd.SegmentCount} segments, {bd.Area:0} m2 ({m.Size}), max {m.MaxPlayers} players, "
                      + $"spawns {m.Spawns.Count} ({(m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count)} placed; teams {o.TeamSpawns[0].Count}+{o.TeamSpawns[1].Count}), "
                      + $"hills {o.Hills.Count}, waypoints {m.Waypoints.Count}, movers {m.Movers.Count}");
        ClutteredLine($"  objects: {m.Solid.Count} boxes, {props.Count} props ({solid} solid, {props.Count - solid} cosmetic, {breakable} breakable; "
                      + $"{m.ClutterStart} placed by hand, {props.Count - m.ClutterStart} clutter), {plants.Length} plants ({byType}), {m.MeshPlacements.Count} meshes");
        void AtLeast(string what, int have, int need)
        {
            if (have < need)
                Fail($"{m.Name}: {have} {what} (the density test wants at least {need})");
        }
        AtLeast("props", props.Count, quota.Props);
        AtLeast("solid props", solid, quota.SolidProps);
        AtLeast("cosmetic props", props.Count - solid, quota.CosmeticProps);
        AtLeast("breakable props", breakable, quota.Breakable);
        AtLeast("plants", plants.Length, quota.Plants);
        AtLeast("trees", trees, quota.Trees);
        AtLeast("boxes", m.Solid.Count, quota.Boxes);

        // Nothing outside the outline: the floor slab is the only box allowed to reach past it.
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
        foreach (var f in plants)
            In(f.Type.ToString(), f.Position.X, f.Position.Z);
        foreach (var b in m.Solid)
            if (b.Max.X - b.Min.X < 2f * bd.Max.X)
                In("box", (b.Min.X + b.Max.X) / 2, (b.Min.Z + b.Max.Z) / 2);
        foreach (var h in o.Hills)
            In("hill", h.Center.X, h.Center.Z, h.Radius * 0.5f);
        for (var i = 0; i < o.Flags.Length; i++)
            In(i == 0 ? "blue flag" : "red flag", o.Flags[i].X, o.Flags[i].Z, 0.5f);
        if (outside.Count > 0)
            Fail($"{m.Name}: {outside.Count} thing(s) outside the outline: {string.Join(", ", outside.Take(6))}");

        // Spawns and objectives for every mode, placed by hand.
        var authored = m.AuthoredSpawns >= 0 ? m.AuthoredSpawns : m.Spawns.Count;
        if (authored < m.MaxPlayers)
            Fail($"{m.Name}: {authored} hand-placed spawns for {m.MaxPlayers} players");
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            Fail($"{m.Name}: objectives should be hand-placed (team spawns {!o.TeamSpawnsGenerated}, hills {!o.HillsGenerated}, flags {!o.FlagsGenerated})");
        if (o.TeamSpawns[0].Count < m.MaxPlayers / 2 - 2 || o.TeamSpawns[1].Count < m.MaxPlayers / 2 - 2)
            Fail($"{m.Name}: team spawn lists are short ({o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count})");
        if (o.Hills.Count < (m.Size == MapSize.Small ? 2 : 3))
            Fail($"{m.Name}: only {o.Hills.Count} hill zone(s)");
        if (m.FoliageSection == null)
            Fail($"{m.Name}: no foliage");

        // Solid props keep off spawns, hill zones and flags.
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
                    if (why == null && Dist(b, f) < 2f)
                        why = $"flag ({f.X:0},{f.Z:0})";
                if (why != null)
                {
                    blocked.Add($"{p.Def.Name} ({p.Position.X:0.0},{p.Position.Z:0.0}) on the {why}");
                    break;
                }
            }
        if (blocked.Count > 0)
            Fail($"{m.Name}: {blocked.Count} solid prop(s) in the way: {string.Join("; ", blocked.Take(4))}");
        var explosive = props.Where(p => p.Def.Breakable is { ExplodeRadius: > 0 }).ToList();
        foreach (var e in explosive)
            if (m.Spawns.Any(s => Vector2.Distance(new Vector2(s.X, s.Z), new Vector2(e.Position.X, e.Position.Z)) < 10f))
                Fail($"{m.Name}: explosive {e.Def.Name} at ({e.Position.X:0.0},{e.Position.Z:0.0}) is within 10 m of a spawn");

        // The nav graph reaches every spawn, hill and flag.
        World.SetMap(m);
        for (var wait = 0; wait < 2000 && NavGraph.Current == NavGraph.Empty; wait++)
            NavGraph.Pump(50);
        var g = NavGraph.Current;
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
        foreach (var h in o.Hills)
            Reach("hill", h.Center);
        for (var i = 0; i < o.Flags.Length; i++)
            Reach(i == 0 ? "blue flag" : "red flag", o.Flags[i]);
        ClutteredLine($"  nav graph: {g.Nodes.Length} nodes, {Enumerable.Range(0, g.Nodes.Length).Count(i => g.Component[i] == g.MainComponent)} in the main area");
        if (missing.Count > 0)
            Fail($"{m.Name}: the nav graph doesn't reach {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? $" and {missing.Count - 5} more" : "")}");

        // Bot matches at the map's player limit: free-for-all and team deathmatch, 90 simulated seconds each, after a
        // short unmeasured warm-up (this section runs early, so the first kills, breaks and respawns would JIT-compile
        // inside a measured tick).
        ClutteredSmallBotMatch(m, GameMode.FreeForAll, m.MaxPlayers - 1, 20, measured: false);
        foreach (var mode in new[] { GameMode.FreeForAll, GameMode.TeamDeathmatch })
            ClutteredSmallBotMatch(m, mode, m.MaxPlayers - 1, 90);
    }

    private static void ClutteredSmallBotMatch(Map m, GameMode mode, int bots, int seconds, bool measured = true)
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
        var killsDone = 0;
        var lastKillSum = 0;
        var lastMatch = game.MatchNumber;
        try
        {
            for (var i = 0; i < seconds * World.TickRate; i++)
            {
                now += dt;
                // A tick that spent over a millisecond compiling (JIT) is a one-off warm-up: counted and reported, not measured.
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
                if (game.MatchNumber != lastMatch)
                {
                    killsDone += lastKillSum;
                    lastMatch = game.MatchNumber;
                }
                lastKillSum = game.Players.Values.Sum(p => (int)p.Kills);
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
        if (!measured)
        {
            m.RestoreProps();
            return;
        }
        var kills = killsDone + game.Players.Values.Sum(p => (int)p.Kills);
        var fallen = game.Players.Values.Count(p => p.Alive && p.Position.Y < m.GroundHeight(p.Position.X, p.Position.Z) - 0.5f);
        var escaped = game.Players.Values.Count(p => p.Alive && !m.Boundary!.Inside(p.Position.X, p.Position.Z, -0.5f));
        var stuck = moved.Count(kv => kv.Value < 15f);
        var avgMove = moved.Count == 0 ? 0 : moved.Values.Average();
        // The host is an idle player (no input) on one team, which then fights a bot short: team scores lean to the other side.
        var host = game.Local;
        var teams = GameModes.IsTeam(mode)
            ? $"  score blue {game.TeamScore[0]} red {game.TeamScore[1]} (idle host on {(host?.Team == 0 ? "blue" : "red")}, died {host?.Deaths ?? 0} times)"
            : "";
        ClutteredLine($"  bots {mode,-14} {game.Players.Count} players: kills {kills}{teams}  avg bot travel {avgMove:0} m  stuck {stuck}  fallen {fallen}  outside {escaped}  "
                      + $"worst tick {maxTickMs:0.00} ms (work {cpu.WorstText}; {jitTicks} JIT warm-up ticks not counted)  broken props {m.BrokenProps.Count}  ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != bots + 1)
            Fail($"{m.Name}: {mode} match has {game.Players.Count} players (expected {bots + 1})");
        if (kills == 0 && m.Size != MapSize.Large)
            Fail($"{m.Name}: no kills in a 90 s {mode} bot match");
        if (fallen > 0 || escaped > 0)
            Fail($"{m.Name}: {fallen} player(s) below the floor, {escaped} outside the outline after a {mode} bot match");
        if (stuck > 1)
            Fail($"{m.Name}: {stuck} bots barely moved in a {mode} match (stuck on clutter?)");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{m.Name}: a {mode} server tick did {cpu.WorstMs:0.0} ms of work (over {MaxTickWorkMs} ms; {cpu.WorstDetail})");
        m.RestoreProps();
    }
}
