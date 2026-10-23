using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace CallOfSlop.MapKit;

/// <summary>What <see cref="MapChecks.Check"/> found: failures (the map is wrong), notes (numbers worth reading) and the walk table.</summary>
public sealed class CheckReport
{
    public readonly string MapName;
    public readonly List<string> Failures = new();
    public readonly List<string> Notes = new();

    /// <summary>Per objective: the average route length from each team's spawns (NaN: no route).</summary>
    public readonly List<(string Target, float Blue, float Red)> Walks = new();

    public double NavBuildMs;
    public int NavNodes, NavMainNodes, NavEdges, Props, DecalsPlaced, DecalsRequested, Boxes, HiddenBoxes, Meshes, Spawns, Waypoints, StairFlights;
    public uint Fingerprint;

    public CheckReport(string mapName) => MapName = mapName;

    public bool Passed => Failures.Count == 0;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"{MapName}: {(Passed ? "OK" : $"{Failures.Count} problem(s)")}; {Boxes} boxes ({HiddenBoxes} hidden), {Meshes} meshes, {Props} props, decals {DecalsPlaced}/{DecalsRequested}, ")
          .Append($"{Spawns} spawns, {Waypoints} waypoints; nav {NavNodes} nodes ({NavMainNodes} main), {NavEdges} edges, built in {NavBuildMs:0} ms; {StairFlights} stair flight(s); fingerprint {Fingerprint:x8}\n");
        if (Walks.Count > 0)
            sb.Append("  walks Blue/Red: ").Append(string.Join(", ", Walks.Select(w => $"{w.Target} {w.Blue:0}/{w.Red:0} m"))).Append('\n');
        foreach (var n in Notes)
            sb.Append("  ").Append(n).Append('\n');
        foreach (var f in Failures)
            sb.Append("  FAIL ").Append(f).Append('\n');
        return sb.ToString().TrimEnd();
    }
}

public sealed class CheckOptions
{
    /// <summary>Routes from the two teams to each objective may differ by this fraction (the Space sections use 12%).</summary>
    public float WalkFairness = 0.12f;

    /// <summary>At least this share of the nav nodes must be in the main (connected) area.</summary>
    public float MainAreaShare = 0.75f;

    /// <summary>Walk the stairs with the real movers (the "Stairs" audit); slow on big maps.</summary>
    public bool Stairs = true;

    /// <summary>The nav graph may take this long to build (ms) before it counts as a failure (0: report only).</summary>
    public double NavBuildLimitMs = 0;

    /// <summary>The least props and placed decals a dressed map should have (0: don't check).</summary>
    public int MinProps, MinDecals;
}

/// <summary>
/// The point of a C# map kit: a generated map is checked against the real game before anyone plays it. <see cref="Check"/>
/// builds the physics world, the nav graph and the objectives of a loaded <see cref="Map"/> and reports what the bespoke
/// Space self-test sections and the all-maps sections check: objectives inside the outline and not in geometry, spawns
/// standing on clear ground, the horizon fade close past the floor (<see cref="HorizonFade"/>, shared with the "Map
/// horizons" section), nav reach (the main area, nodes over the void), walk fairness between the teams per hill and
/// flag, stairs climbable (the "Stairs" audit), nothing outside the outline, prop and decal counts, and the nav build
/// time. It never runs a bot match: those stay in the self-test.
/// </summary>
public static class MapChecks
{
    public static CheckReport Check(Map m, CheckOptions? options = null)
    {
        var o = options ?? new CheckOptions();
        var r = new CheckReport(m.Name);
        var before = World.Map;
        try
        {
            m.Validate();
            r.Fingerprint = m.ContentHash;
            r.Boxes = m.Solid.Count;
            r.HiddenBoxes = m.HiddenBoxCount;
            r.Meshes = m.MeshPlacements.Count;
            r.Props = m.PropInstances.Count;
            r.Spawns = m.Spawns.Count;
            r.Waypoints = m.Waypoints.Count;
            var bake = DecalBaker.Bake(m);
            r.DecalsPlaced = bake.Placed;
            r.DecalsRequested = bake.Requested;
            if (o.MinProps > 0 && r.Props < o.MinProps || o.MinDecals > 0 && r.DecalsPlaced < o.MinDecals)
                r.Failures.Add($"too plain: {r.Props} props, {r.DecalsPlaced} decals placed (wanted {o.MinProps} / {o.MinDecals})");
            if (bake.Placed == 0 && bake.Requested > 0)
                r.Failures.Add($"none of the {bake.Requested} decals found a surface to land on");
            else if (bake.Missed > 0)
                r.Notes.Add($"{bake.Missed} of {bake.Requested} decals found no surface to land on (scatters over round or open ground miss some)");

            Objectives(m, r);
            Outline(m, r);
            World.SetMap(m);
            SpawnsStand(m, r);
            if (m.Boundary != null && !m.HasTerrain && m.FallKillY == null)
            {
                var fade = HorizonFade(m);
                r.Failures.AddRange(fade.Failures);
                r.Notes.Add($"horizon fade starts at most {fade.Worst:0.0} m past the floor (direction {fade.WorstAt}{(fade.Notched ? ", notched outline" : "")}; limit {HorizonFadeLimit:0} m)");
            }
            var g = Nav(m, r, o);
            if (g != null && m.PointGravity == null)
                Walks(m, g, r, o);
            else if (m.PointGravity != null)
                r.Notes.Add("a planet: walk fairness isn't measured (routes go round the sphere)");
            if (o.Stairs)
            {
                var problems = SelfTest.StairProblems(m, out var flights);
                r.StairFlights = flights;
                r.Failures.AddRange(problems.Select(p => "stairs: " + p));
            }
        }
        catch (Exception ex) when (ex is MapFileException or InvalidOperationException)
        {
            r.Failures.Add($"the map doesn't build: {ex.Message}");
        }
        finally
        {
            World.SetMap(before);
        }
        return r;
    }

    // ---- objectives and outline -----------------------------------------------------------------------------------------

    private static void Objectives(Map m, CheckReport r)
    {
        var o = m.Objectives;
        if (o.TeamSpawns[0].Count == 0 || o.TeamSpawns[1].Count == 0)
            r.Failures.Add($"a team has no spawns ({o.TeamSpawns[0].Count} + {o.TeamSpawns[1].Count})");
        if (o.Hills.Count == 0)
            r.Failures.Add("no hill");
        foreach (var (h, i) in o.Hills.Select((h, i) => (h, i)))
            if (m.IsBlocked(h.Center))
                r.Failures.Add($"hill {i + 1} at {P(h.Center)} is inside geometry");
        if (o.Flags.Length != 2)
            r.Failures.Add($"{o.Flags.Length} flags (two needed)");
        foreach (var (f, i) in o.Flags.Select((f, i) => (f, i)))
            if (m.IsBlocked(f))
                r.Failures.Add($"flag {i} at {P(f)} is inside geometry");
        if (o.Flags.Length == 2)
        {
            var gap = m.PointGravity != null ? Vector3.Distance(o.Flags[0], o.Flags[1]) : new Vector2(o.Flags[0].X - o.Flags[1].X, o.Flags[0].Z - o.Flags[1].Z).Length();
            var span = MathF.Min(m.HalfX, m.HalfZ) * 2;
            if (gap < span * 0.35f)
                r.Failures.Add($"the flags are only {gap:0} m apart (less than 35% of the {span:0} m span)");
            if (o.TeamSpawns[0].Count > 0 && o.TeamSpawns[1].Count > 0)
            {
                var c0 = o.TeamSpawns[0].Aggregate(Vector3.Zero, (a, b) => a + b) / o.TeamSpawns[0].Count;
                var c1 = o.TeamSpawns[1].Aggregate(Vector3.Zero, (a, b) => a + b) / o.TeamSpawns[1].Count;
                if (Vector3.Distance(c0, c1) < span * 0.2f)
                    r.Failures.Add($"the team spawn groups overlap (centres {Vector3.Distance(c0, c1):0} m apart)");
            }
        }
        if (o.TeamSpawnsGenerated || o.HillsGenerated || o.FlagsGenerated)
            r.Notes.Add($"generated objectives: {(o.TeamSpawnsGenerated ? "team spawns " : "")}{(o.HillsGenerated ? "hills " : "")}{(o.FlagsGenerated ? "flags" : "")}".TrimEnd());
    }

    private static void Outline(Map m, CheckReport r)
    {
        if (m.Boundary is not { } b)
            return;
        var o = m.Objectives;
        var outside = o.Hills.Select(h => ("hill", h.Center)).Concat(o.Flags.Select(f => ("flag", f))).Concat(m.Spawns.Select(s => ("spawn", s)))
            .Where(p => !b.Inside(p.Item2.X, p.Item2.Z, 0.5f)).ToList();
        if (outside.Count > 0)
            r.Failures.Add($"{outside.Count} objective(s) or spawn(s) outside the outline, e.g. {outside[0].Item1} at {P(outside[0].Item2)}");
        var props = m.PropInstances.Where(p => !b.Inside(p.Position.X, p.Position.Z, -0.5f)).ToList();
        if (props.Count > 0)
            r.Notes.Add($"{props.Count} prop(s) stand outside the outline as scenery, e.g. {props[0].Def.Name} at {P(props[0].Position)}");
        var wps = m.Waypoints.Where(w => !b.Inside(w.X, w.Z, -0.5f)).ToList();
        if (wps.Count > 0)
            r.Failures.Add($"{wps.Count} waypoint(s) outside the outline, e.g. {P(wps[0])}");
    }

    private static void SpawnsStand(Map m, CheckReport r)
    {
        var bad = new List<string>();
        foreach (var sp in m.Spawns)
        {
            var s = new MoveState { Position = sp, Grounded = false };
            for (var i = 0; i < 40; i++)
                World.Simulate(ref s, new InputCmd { SpeedPct = 100 });
            var drift = new Vector2(s.Position.X - sp.X, s.Position.Z - sp.Z).Length();
            var drop = m.HasTerrain ? 0f : MathF.Abs(s.Position.Y - sp.Y); // (on terrain a spawn's height is read off the ground)
            var up = GravityFrames.UpAt(s.Position);
            var fits = World.BodyFits(s.Position + (m.HasTerrain ? up * 0.3f : Vector3.Zero), up); // (a sloping heightfield grazes a capsule set right on it)
            if (!s.Grounded || drift > 0.5f || drop > 0.5f || !fits)
                bad.Add($"{P(sp)} (settled at {P(s.Position)}, grounded {s.Grounded}, room for a body {fits})");
            if (m.GravityZones is { } zones && !m.PlainUpAtPoint(sp) && zones.FieldAt(sp) is not PointGravity)
                r.Failures.Add($"spawn at {P(sp)} is in a gravity zone with a sideways up");
        }
        if (bad.Count > 0)
            r.Failures.Add($"{bad.Count} spawn(s) don't stand on clear ground, e.g. {bad[0]}");
    }

    // ---- horizon fade ---------------------------------------------------------------------------------------------------

    public const float HorizonFadeLimit = 22f;

    public readonly record struct HorizonFadeResult(float Worst, int WorstAt, bool Notched, int InsideFloor, List<string> Failures);

    /// <summary>
    /// The per-direction horizon fade of a flat shaped map (<see cref="Map.OuterFogStarts"/>): it never starts inside the
    /// floor (every outline point pushed out by the floor margin stays unfaded) and, unless the outline has deep notches,
    /// it starts within <see cref="HorizonFadeLimit"/> m past the floor on every side (a band of unfaded dark far ground
    /// shows otherwise; the Space agents hit this late because the "Map horizons" section only runs on main). The
    /// "Map horizons" self-test section calls this too.
    /// </summary>
    public static HorizonFadeResult HorizonFade(Map m, LightSetup? lightOverride = null)
    {
        var failures = new List<string>();
        if (m.Boundary is not { } b || m.OuterFogStarts is not { } starts)
            return new HorizonFadeResult(0, 0, false, 0, failures);
        var light = lightOverride ?? m.Light;
        var c = new Vector2(m.OuterFog.X, m.OuterFog.Y);
        var margin = MathF.Min(b.Spec.GroundMargin, 64f);
        const int n = Map.OuterFogDirections;
        var reach = new float[n];
        var inside = 0;
        for (var s = 0; s < b.Outline.Length; s++)
        {
            var a0 = b.Outline[s];
            var a1 = b.Outline[(s + 1) % b.Outline.Length];
            var pieces = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a0, a1)));
            for (var k = 0; k < pieces; k++)
            {
                var p = Vector2.Lerp(a0, a1, k / (float)pieces);
                for (var e = 0; e < 8; e++)
                {
                    var q = p + new Vector2(MathF.Cos(e * MathF.Tau / 8), MathF.Sin(e * MathF.Tau / 8)) * margin;
                    var d = q - c;
                    if (Lighting.OuterStart(light, d) < d.Length() - 1e-3f && inside++ == 0)
                        failures.Add($"the horizon fade starts {Lighting.OuterStart(light, d):0.0} m out towards ({q.X:0},{q.Y:0}), inside the floor ({d.Length():0.0} m)");
                }
                var pd = p - c;
                var ang = MathF.Atan2(pd.Y, pd.X) * (n / MathF.Tau);
                var j = (int)MathF.Round(ang < 0 ? ang + n : ang) % n;
                reach[j] = MathF.Max(reach[j], pd.Length() + margin);
            }
        }
        var worst = 0f;
        var worstAt = 0;
        for (var i = 0; i < n; i++)
        {
            if (reach[i] <= 0)
                continue;
            var band = starts[i] - reach[i];
            if (band > worst)
                (worst, worstAt) = (band, i);
        }
        var radii = b.Outline.Select(p => Vector2.Distance(p, c)).ToArray();
        var notched = radii.Min() < 0.45f * radii.Max();
        if (!notched && worst > HorizonFadeLimit)
            failures.Add($"the horizon fade starts {worst:0.0} m past the floor in direction {worstAt} (max {HorizonFadeLimit:0}): a band of unfaded far ground shows there");
        if (starts.Max() > m.OuterFog.Z + 1e-3f)
            failures.Add($"a horizon fade start ({starts.Max():0.0} m) is past the farthest point's radius ({m.OuterFog.Z:0.0} m)");
        return new HorizonFadeResult(worst, worstAt, notched, inside, failures);
    }

    // ---- nav and walks --------------------------------------------------------------------------------------------------

    private static NavGraph? Nav(Map m, CheckReport r, CheckOptions o)
    {
        var sw = Stopwatch.StartNew();
        var g = NavGraph.Build(m);
        r.NavBuildMs = sw.Elapsed.TotalMilliseconds;
        r.NavNodes = g.Nodes.Length;
        r.NavMainNodes = g.MainNodes.Length;
        r.NavEdges = g.EdgeCount;
        if (g.Nodes.Length == 0)
        {
            r.Failures.Add("the nav graph has no nodes");
            return null;
        }
        if (g.MainNodes.Length < g.Nodes.Length * o.MainAreaShare)
            r.Failures.Add($"only {g.MainNodes.Length} of {g.Nodes.Length} nav nodes are in the main area (an area bots can't reach)");
        if (m.FallKillY is { } fk && !m.HasTerrain) // (a terrain map's chasms may hold nodes on their floors)
        {
            var low = g.Nodes.Count(n => n.Y < fk + 0.5f);
            if (low > 0)
                r.Failures.Add($"{low} nav node(s) below the fall-kill height");
        }
        if (o.NavBuildLimitMs > 0 && r.NavBuildMs > o.NavBuildLimitMs)
            r.Failures.Add($"the nav graph took {r.NavBuildMs:0} ms to build (limit {o.NavBuildLimitMs:0})");
        var noNode = m.Spawns.Where(sp => g.Nearest(sp, needSight: false) is var k && (k < 0 || Vector3.Distance(g.Nodes[k], sp) > 4f)).ToList();
        if (noNode.Count > 0)
            r.Failures.Add($"{noNode.Count} spawn(s) have no nav node within 4 m, e.g. {P(noNode[0])}");
        return g;
    }

    /// <summary>Routes from both teams' spawns to each hill and the enemy flag: all there, and within the fairness fraction of each other.</summary>
    private static void Walks(Map m, NavGraph g, CheckReport r, CheckOptions o)
    {
        var obj = m.Objectives;
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
        var targets = obj.Hills.Select((h, i) => ($"hill {i + 1}", h.Center, h.Center)).ToList();
        if (obj.Flags.Length == 2)
            targets.Add(("enemy flag", obj.Flags[1], obj.Flags[0]));
        foreach (var (label, forBlue, forRed) in targets)
        {
            float blue = Average(obj.TeamSpawns[0], forBlue), red = Average(obj.TeamSpawns[1], forRed);
            r.Walks.Add((label, blue, red));
            if (float.IsNaN(blue) || float.IsNaN(red))
                r.Failures.Add($"no route from {(float.IsNaN(blue) ? "Blue" : "Red")}'s spawns to {label} at {P(float.IsNaN(blue) ? forBlue : forRed)}");
            else if (MathF.Abs(blue - red) > o.WalkFairness * MathF.Max(blue, red))
                r.Failures.Add($"{label} is {blue:0} m from Blue and {red:0} m from Red (more than {o.WalkFairness * 100:0}% apart)");
        }
    }

    private static string P(Vector3 v) => $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})";
}
