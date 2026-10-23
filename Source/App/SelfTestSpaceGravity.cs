using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// "Space: gravity maps" self-test: the Space pack's gravity-zone maps, Keelhaul Yard (03) and Twin Shard (04). Each
/// loads with zones, a shaped outline over a void and hand-placed objectives; the areas with their own up have nav nodes
/// in the main area and bots can route from both teams' spawns to every hill and the enemy flag about equally far; a
/// scripted player stands in each kind of gravity; and a short seeded bot match (12 s) runs with nobody stuck, fallen
/// or outside. No long matches.
/// </summary>
public static partial class SelfTest
{
    private static readonly List<string> SpaceGravityNotes = new();

    private static void SpaceGravityLine(string s)
    {
        Line(s);
        SpaceGravityNotes.Add(s);
    }

    private static void CheckSpaceGravityMaps()
    {
        SpaceGravityNotes.Clear();
        var before = World.Map;
        try
        {
            KeelhaulYardChecks();
            TwinShardChecks();
        }
        finally
        {
            World.SetMap(before);
            if (SpaceGravityNotes.Count > 0)
                Line("::notice title=Space: gravity maps (results)::" + string.Join("%0A", SpaceGravityNotes.Select(n => n.Replace("%", "%25"))));
        }
    }

    /// <summary>What every gravity map must have; returns the nav graph (built in full) or null.</summary>
    private static NavGraph? GravityMapCommon(Map m, string name, int minProps, int minDecals, int minSpawns)
    {
        var o = m.Objectives;
        var ok = true;
        if (m.GravityZones is not { } zones || zones.Zones.Length < 2)
        {
            Fail($"{name}: should have gravity zones");
            return null;
        }
        if (m.FallKillY == null || m.Boundary == null || m.Boundary.Spec.Shape == BoundaryShape.Rect || m.Terrain != null)
        {
            Fail($"{name}: should be a void map with a shaped outline (fallKill, a non-rectangular boundary, no terrain)");
            ok = false;
        }
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated || o.Hills.Count < 3 || o.Flags.Length != 2)
        {
            Fail($"{name}: team spawns, three or more hills and the flags should all be hand-placed");
            ok = false;
        }
        if (o.Hills.Any(h => MathF.Abs(h.Center.Z) > 0.05f))
            Fail($"{name}: the hills should be on the mirror line z = 0");
        if (m.Spawns.Count < minSpawns || m.MaxPlayers < 12)
        {
            Fail($"{name}: {m.Spawns.Count} spawns, {m.MaxPlayers} players: too few");
            ok = false;
        }
        var outside = o.Hills.Select(h => h.Center).Concat(o.Flags).Concat(m.Spawns).Where(p => !m.Boundary!.Inside(p.X, p.Z, 0.5f)).ToList();
        if (outside.Count > 0)
            Fail($"{name}: {outside.Count} objectives or spawns outside the outline, e.g. {outside[0]}");
        foreach (var sp in m.Spawns)
            if (!m.PlainUpAtPoint(sp))
                Fail($"{name}: spawn at {sp} is in a gravity zone with its own up");
        var bake = DecalBaker.Bake(m);
        if (m.PropInstances.Count < minProps || bake.Placed < minDecals)
            Fail($"{name}: too plain: {m.PropInstances.Count} props, {bake.Placed} decals placed");
        World.SetMap(m);
        var sw = Stopwatch.StartNew();
        var g = NavGraph.Build(m);
        var buildMs = sw.Elapsed.TotalMilliseconds;
        var lowNodes = g.Nodes.Count(n => n.Y < m.FallKillY + 0.5f);
        if (lowNodes > 0)
            Fail($"{name}: {lowNodes} nav nodes below the fall-kill height");
        SpaceGravityLine($"{name}: {m.Size} {m.HalfX * 2:0} x {m.HalfZ * 2:0} m ({m.PlayArea:0} m2 inside the outline), max {m.MaxPlayers} players, {zones.Zones.Length} gravity zones, "
                         + $"{m.Solid.Count} boxes, {m.SolidMeshes.Count} solid meshes of {m.MeshPlacements.Count}, {m.PropInstances.Count} props, decals {bake.Placed}/{bake.Requested}, "
                         + $"{m.Spawns.Count} spawns, {m.Waypoints.Count} waypoints; nav {g.Nodes.Length} nodes ({g.MainNodes.Length} main), {g.EdgeCount} edges, built in {buildMs:0} ms, fingerprint {m.ContentHash:x8}");
        if (g.MainNodes.Length < g.Nodes.Length * 0.75f)
            Fail($"{name}: only {g.MainNodes.Length} of {g.Nodes.Length} nav nodes are in the main area");
        return ok ? g : null;
    }

    /// <summary>Routes from both teams' spawns to each hill and the enemy flag: all there, and within 12% of each other.</summary>
    private static void GravityMapWalks(Map m, NavGraph g, string name)
    {
        var o = m.Objectives;
        var search = new NavSearch();
        var path = new int[4096];
        float Walk(Vector3 a, Vector3 b)
        {
            int ka = g.Nearest(a, needSight: false), kb = g.Nearest(b, needSight: false);
            if (ka < 0 || kb < 0 || Vector3.Distance(g.Nodes[kb], b) > 4f)
                return float.NaN;
            var n = search.Find(g, ka, kb, path);
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
                Fail($"{name}: no route from a team's spawns to {label} at {forBlue}: "
                     + string.Join(", ", o.TeamSpawns.SelectMany((l, t) => l.Where(sp => float.IsNaN(Walk(sp, t == 0 ? forBlue : forRed)))
                         .Select(sp => g.Nearest(sp, needSight: false) is var k && k >= 0
                             ? $"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) node ({g.Nodes[k].X:0.0},{g.Nodes[k].Y:0.00},{g.Nodes[k].Z:0.0}) c{g.Component[k]}/{g.MainComponent}"
                             : $"({sp.X:0.0},{sp.Y:0.0},{sp.Z:0.0}) no node"))));
            else if (MathF.Abs(blue - red) > 0.12f * MathF.Max(blue, red))
                Fail($"{name}: {label} is {blue:0} m from Blue and {red:0} m from Red (more than 12% apart)");
        }
        SpaceGravityLine($"{name} walks Blue/Red: " + string.Join(", ", walks));
    }

    /// <summary>A short seeded bot match: 12 simulated seconds at the player limit, nobody stuck, fallen or outside, ticks cheap.</summary>
    private static void GravityMapBotMatch(Map m, string name, GameMode mode)
    {
        var sw = Stopwatch.StartNew();
        using var game = ModeGame(m.MaxPlayers - 1, mode, m.Id);
        Game.SettleMemory();
        const double dt = 1.0 / World.TickRate;
        var now = 0.0;
        var lastPos = new Dictionary<byte, Vector3>();
        var moved = new Dictionary<byte, float>();
        var cpu = new TickCpu();
        var maxTickMs = 0.0;
        var jitTicks = 0;
        var zonesUsed = new HashSet<int>();
        try
        {
            for (var i = 0; i < 12 * World.TickRate; i++)
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
                if (i % 30 == 0)
                    foreach (var p in game.Players.Values.Where(p => p.Bot != null && p.Alive))
                    {
                        if (lastPos.TryGetValue(p.Id, out var lp))
                            moved[p.Id] = moved.GetValueOrDefault(p.Id) + Vector3.Distance(lp, p.Position);
                        lastPos[p.Id] = p.Position;
                        zonesUsed.Add(m.GravityZones!.ZoneAt(p.Position));
                    }
            }
        }
        catch (Exception ex)
        {
            Fail($"{name}: {mode} bot match crashed: {ex}");
            return;
        }
        var kills = game.Players.Values.Sum(p => (int)p.Kills);
        var fallen = game.Players.Values.Count(p => p.Alive && p.Position.Y < m.FallKillY);
        var escaped = game.Players.Values.Count(p => p.Alive && !m.Boundary!.Inside(p.Position.X, p.Position.Z, -0.5f));
        var stuck = moved.Count(kv => kv.Value < 8f);
        var avgMove = moved.Count == 0 ? 0 : moved.Values.Average();
        SpaceGravityLine($"{name} bots {mode} {game.Players.Count} players, 12 s: kills {kills}, avg bot travel {avgMove:0} m, stuck {stuck}, fallen {fallen}, outside {escaped}, "
                         + $"fall kills {game.FallKills}, stuck kills {game.BotStuckKills}, zones visited {zonesUsed.Count}, worst tick {maxTickMs:0.00} ms (work {cpu.WorstText}; {jitTicks} JIT ticks not counted) ({sw.Elapsed.TotalSeconds:0.0} s)");
        if (game.Players.Count != m.MaxPlayers)
            Fail($"{name}: {mode} match has {game.Players.Count} players (expected {m.MaxPlayers})");
        if (fallen > 0 || escaped > 0)
            Fail($"{name}: {fallen} player(s) below the fall-kill height, {escaped} outside the outline after a {mode} bot match");
        if (stuck > 2)
            Fail($"{name}: {stuck} bots barely moved in a {mode} match");
        if (cpu.WorstMs > MaxTickWorkMs)
            Fail($"{name}: a {mode} server tick did {cpu.WorstText} of work (limit {MaxTickWorkMs} ms)");
        m.RestoreProps();
    }

    /// <summary>A player dropped at <paramref name="at"/> settles on ground whose up is <paramref name="up"/> (within 6 degrees).</summary>
    private static bool StandsWithUp(Vector3 at, Vector3 up, string name, string where)
    {
        var s = Settle(at, 90);
        var u = GravityFrames.UpAt(s.Position);
        var ok = s.Grounded && Vector3.Dot(u, up) > 0.994f && World.BodyFits(s.Position, u);
        if (!ok)
            Fail($"{name}: a player at {where} ({at}) doesn't stand with up {up} (grounded {s.Grounded}, up {u}, at {s.Position})");
        return ok;
    }

    private static void KeelhaulYardChecks()
    {
        const string name = "Keelhaul Yard";
        if (Maps.Find(name) is not { } m)
        {
            Fail($"Maps/Space/03-KeelhaulYard.json ('{name}') didn't load");
            return;
        }
        if (GravityMapCommon(m, name, minProps: 80, minDecals: 120, minSpawns: 16) is not { } g)
            return;
        // Nodes in each kind of gravity, all in the main area: the hull wall (up -X), the rock (up away from its centre),
        // the hold (low gravity, up +Y) and the bowl.
        var zones = m.GravityZones!;
        int Main(Func<Vector3, bool> where) => g.MainNodes.Count(i => where(g.Nodes[i]));
        var wall = Main(n => n.X > 29.5f && n.X < 30.2f && n.Y > 1f && zones.UpAt(n) == -Vector3.UnitX);
        var rock = Main(n => Vector3.Distance(n, new Vector3(0, 9, 0)) < 6.6f);
        var rockUnder = Main(n => Vector3.Distance(n, new Vector3(0, 9, 0)) < 6.6f && n.Y < 7f);
        var hold = Main(n => n.X < -17f && n.Y < -2.5f);
        var bowl = Main(n => zones.ZoneAt(n) is var z && z >= 0 && zones.Zones[z].Name.StartsWith("bowl"));
        var masts = Main(n => n.Y > 14.8f && MathF.Abs(n.X) < 4f && MathF.Abs(n.Z) > 2f && MathF.Abs(n.Z) < 21f);
        SpaceGravityLine($"{name} nav by area (main): hull wall {wall}, bowl {bowl}, rock {rock} ({rockUnder} under it), hold {hold}, masts and piers {masts}");
        if (wall < 30 || bowl < 3 || rock < 20 || rockUnder < 4 || hold < 40 || masts < 6)
            Fail($"{name}: the bots' graph misses an area (wall {wall}, bowl {bowl}, rock {rock}/{rockUnder}, hold {hold}, masts {masts})");
        GravityMapWalks(m, g, name);

        // A player stands in each kind of gravity, and a sprinting jump in the hold clears the void hatch.
        var stands = StandsWithUp(new Vector3(0, 0.3f, -20), Vector3.UnitY, name, "the yard")
                     & StandsWithUp(new Vector3(29.6f, 16, 5), -Vector3.UnitX, name, "the hull")
                     & StandsWithUp(new Vector3(-31, -2.7f, 15), Vector3.UnitY, name, "the hold")
                     & StandsWithUp(new Vector3(0, 15.4f, 0), Vector3.UnitY, name, "the rock's top")
                     & StandsWithUp(new Vector3(0, 2.4f, 0), -Vector3.UnitY, name, "the rock's underside")
                     & StandsWithUp(new Vector3(6.3f, 9, 0), Vector3.UnitX, name, "the rock's side");
        var j = Settle(new Vector3(-20.5f, -2.7f, 0), 30);
        var west = GravityFrames.YawOf(j.Position, -Vector3.UnitX);
        for (var i = 0; i < 20; i++)
            World.Simulate(ref j, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = west, SpeedPct = 100 });
        var takeoff = j.Position;
        World.Simulate(ref j, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint | Buttons.Jump, Yaw = west, SpeedPct = 100 });
        var air = 0;
        var lowest = j.Position.Y;
        while (!j.Grounded && air++ < 400)
        {
            World.Simulate(ref j, new InputCmd { Buttons = Buttons.Forward | Buttons.Sprint, Yaw = west, SpeedPct = 100 });
            lowest = MathF.Min(lowest, j.Position.Y);
        }
        var jumped = takeoff.X - j.Position.X;
        var landedOnFloor = j.Grounded && j.Position.X < -40f && j.Position.Y > -3.1f && lowest > m.FallKillY!.Value;
        SpaceGravityLine($"{name}: stands in every gravity {stands}; a sprint jump across the hold's hatch (18 m) covers {jumped:0.0} m in {air * World.TickDt:0.00} s and lands on the far floor {landedOnFloor} at ({j.Position.X:0.0}, {j.Position.Y:0.0})");
        if (!landedOnFloor)
            Fail($"{name}: the low-gravity jump across the hatch failed (covered {jumped:0.0} m, grounded {j.Grounded}, at {j.Position}, lowest {lowest:0.0})");
        GravityMapBotMatch(m, name, GameMode.TeamDeathmatch);
    }

    private static void TwinShardChecks()
    {
        const string name = "Twin Shard";
        if (Maps.Find(name) is not { } m)
        {
            Fail($"Maps/Space/04-TwinShard.json ('{name}') didn't load");
            return;
        }
        if (GravityMapCommon(m, name, minProps: 60, minDecals: 60, minSpawns: 16) is not { } g)
            return;
        GravityMapWalks(m, g, name);
        GravityMapBotMatch(m, name, GameMode.KingOfTheHill);
    }
}
