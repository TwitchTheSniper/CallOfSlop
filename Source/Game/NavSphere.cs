using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bot navigation on a planet (point gravity, MapPlanet.cs). Nodes are spread evenly over the whole surface (a Fibonacci
/// sphere, found with rays towards the centre, so roofs and rocks are surfaces too), plus the map's waypoints and spawns;
/// edges join neighbours a player could walk between along the curve (<see cref="World.CanWalkCurved"/>): ground under every
/// sample, steps no higher than the step height, walkable slopes relative to the local up, and room for the body.
/// Searches, path following and the bots' movement then work as everywhere else, in each bot's own ground plane.
/// </summary>
public sealed partial class NavGraph
{
    /// <summary>The planet build (called from <see cref="BuildSteps"/> on point-gravity maps).</summary>
    private static IEnumerable<NavGraph?> BuildCurvedSteps(Map map, PointGravity planet)
    {
        var work = 0.0;
        var t0 = Stopwatch.GetTimestamp();
        var g = new NavGraph { MapName = map.Name, MapHash = HashOf(map), DestructionAtStart = DestructionStamp(map) };
        var c = World.Physics;
        var radius = map.PlanetRadius > 0 ? map.PlanetRadius : MathF.Max(map.HalfX, map.HalfZ) * 0.7f;
        var s = Math.Clamp(radius * 0.085f, 3f, 6f); // 4.8 m on a 56 m planet: about 1,700 nodes
        g.Spacing = s;
        var reach = map.MaxHeightAbovePlanet(radius);

        // ---- Nodes ----
        var nodes = new List<Vector3>();
        var k = 0;
        foreach (var dir in Map.FibonacciSphere(radius, s))
        {
            if (SurfaceNode(c, planet, planet.Center + dir * (radius + reach), dir, reach + 6f) is { } p)
                nodes.Add(p);
            if (++k % 16 == 0)
            {
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }
        var gridCount = nodes.Count;
        foreach (var w in map.Waypoints.Concat(map.Spawns))
        {
            var up = GravityFrames.UpOf(planet, w);
            if (SurfaceNode(c, planet, w + up * 1.0f, up, 3f) is { } p && !nodes.Any(q => Vector3.DistanceSquared(q, p) < 0.8f * 0.8f))
                nodes.Add(p);
            if (++k % 4 == 0)
            {
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null; // (each looks through every node so far)
                t0 = Stopwatch.GetTimestamp();
            }
        }
        g.Nodes = nodes.ToArray();
        g.BuildCells(map);
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        // ---- Edges: neighbours within 1.6 spacings, walked along the curve ----
        var edges = new List<(int From, int To, float Cost)>(nodes.Count * 8);
        var near = new List<int>();
        var linkR = s * 1.6f;
        for (var i = 0; i < g.Nodes.Length; i++)
        {
            var a = g.Nodes[i];
            near.Clear();
            g.Within(a, linkR, near); // (a column superset on a sphere; the real distance is checked here)
            foreach (var j in near)
            {
                if (j <= i)
                    continue;
                var b = g.Nodes[j];
                var len = Vector3.Distance(a, b);
                if (len > linkR || len < 0.05f)
                    continue;
                if (Clear(a, b) && World.CanWalkCurved(a, b)) // (the cheap straight look first: a wall between them, no walk)
                {
                    edges.Add((i, j, len));
                    edges.Add((j, i, len));
                }
            }
            if (i % 4 == 3)
            {
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }

        // ---- Drop unlinked nodes, pack into CSR ----
        var degree = new int[g.Nodes.Length];
        foreach (var e in edges)
            degree[e.From]++;
        var remap = new int[g.Nodes.Length];
        var kept = new List<Vector3>();
        for (var i = 0; i < g.Nodes.Length; i++)
        {
            remap[i] = degree[i] > 0 ? kept.Count : -1;
            if (degree[i] > 0)
                kept.Add(g.Nodes[i]);
        }
        g.Nodes = kept.ToArray();
        var count = g.Nodes.Length;
        g.EdgeStart = new int[count + 1];
        foreach (var e in edges)
            g.EdgeStart[remap[e.From] + 1]++;
        for (var i = 0; i < count; i++)
            g.EdgeStart[i + 1] += g.EdgeStart[i];
        g.EdgeTo = new int[edges.Count];
        g.EdgeCost = new float[edges.Count];
        g.EdgeFlags = new byte[edges.Count];
        g.EdgeLedge = new Vector3[edges.Count];
        var slot = (int[])g.EdgeStart.Clone();
        foreach (var e in edges)
        {
            var at = slot[remap[e.From]]++;
            g.EdgeTo[at] = remap[e.To];
            g.EdgeCost[at] = e.Cost;
        }
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();
        g.BuildCells(map);
        g.BuildComponents();
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        // ---- Openness: long sightlines across the local ground plane ----
        g.Openness = new byte[g.Nodes.Length];
        for (var i = 0; i < g.Nodes.Length; i++)
        {
            g.Openness[i] = OpennessCurved(c, g.Nodes[i]);
            if (i % 8 == 7)
            {
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }
        g.BuildMs = work + Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        g.FinishRuntime(map);
        yield return g;
    }

    /// <summary>
    /// The first surface a ray from <paramref name="from"/> towards the centre meets within <paramref name="depth"/>, if a
    /// standing player fits there and it's walkable (its slope against the local up); null otherwise.
    /// </summary>
    private static Vector3? SurfaceNode(PhysicsWorld c, PointGravity planet, Vector3 from, Vector3 up, float depth)
    {
        if (!c.Raycast(from, -up, depth, QueryFilter.Navigation, out var hit) || hit.Distance < 0.001f)
            return null;
        var feet = hit.Point + up * 0.03f;
        var u = GravityFrames.UpOf(planet, feet);
        if (Vector3.Dot(hit.Normal, u) < World.MinWalkNormalY)
            return null;
        return World.BodyFits(feet, u) ? feet : null;
    }

    private static byte OpennessCurved(PhysicsWorld c, Vector3 feet)
    {
        GravityFrames.Frame(feet, out var r, out var up, out var n);
        var eye = feet + up * World.EyeHeight;
        var open = 0;
        for (var k = 0; k < 8; k++)
        {
            var a = k * MathF.Tau / 8;
            // Slightly down: on a small planet a level line leaves the ground, so "open" means seeing along it 20 m.
            var d = Vector3.Normalize(r * MathF.Cos(a) + n * MathF.Sin(a) - up * 0.12f);
            if (c.Raycast(eye, d, 20f, QueryFilter.Navigation) >= 20f)
                open++;
        }
        return (byte)open;
    }

    /// <summary>The planet version of <see cref="Nearest"/>: the closest node by straight distance, in sight if asked.</summary>
    private int NearestCurved(Vector3 p, bool needSight)
    {
        for (var ring = 1; ring <= 3; ring++)
        {
            var r = Spacing * ring;
            int best = -1, second = -1;
            float bestD = r * r, secondD = r * r;
            var c0 = Math.Clamp((int)((p.X - r - _minX) / Spacing), 0, _cols - 1);
            var c1 = Math.Clamp((int)((p.X + r - _minX) / Spacing), 0, _cols - 1);
            var r0 = Math.Clamp((int)((p.Z - r - _minZ) / Spacing), 0, _rows - 1);
            var r1 = Math.Clamp((int)((p.Z + r - _minZ) / Spacing), 0, _rows - 1);
            for (var cz = r0; cz <= r1; cz++)
            for (var cx = c0; cx <= c1; cx++)
            {
                var cell = cz * _cols + cx;
                for (var k = _cellStart[cell]; k < _cellStart[cell + 1]; k++)
                {
                    var i = _cellNodes[k];
                    if (NodeOff.Length != 0 && NodeOff[i])
                        continue;
                    var d = Vector3.DistanceSquared(Nodes[i], p);
                    if (d < bestD)
                    {
                        (second, secondD) = (best, bestD);
                        (best, bestD) = (i, d);
                    }
                    else if (d < secondD)
                        (second, secondD) = (i, d);
                }
            }
            if (best < 0)
                continue;
            if (!needSight || Clear(p, Nodes[best]))
                return best;
            if (second >= 0 && Clear(p, Nodes[second]))
                return second;
            if (ring == 3)
                return best;
        }
        return -1;
    }
}

public static partial class World
{
    /// <summary>Is there room for a standing body at <paramref name="feet"/> with the given up (static geometry, doors ignored)?</summary>
    public static bool BodyFits(Vector3 feet, Vector3 up) =>
        !Physics.OverlapCapsule(SweptMover.CapsuleAt(feet + up * 0.02f, Stance.Stand, up), new QueryFilter(QueryLayers.Nav & ~QueryLayers.MoverSweeps));

    /// <summary>
    /// Could a player walk from <paramref name="from"/> to <paramref name="to"/> (feet on a planet) along the surface? Every
    /// ~0.6 m along the arc between them: ground below within a step, rising or falling no more than the step height from the
    /// last sample, walkable against the local up, and room for the body above the step height. Bots and the nav graph.
    /// </summary>
    public static bool CanWalkCurved(Vector3 from, Vector3 to)
    {
        if (GravityFrames.Point is not { } planet)
            return false;
        var c = planet.Center;
        var ra = Vector3.Distance(from, c);
        var rb = Vector3.Distance(to, c);
        var ua = GravityFrames.UpOf(planet, from);
        var ub = GravityFrames.UpOf(planet, to);
        var arc = MathF.Acos(Math.Clamp(Vector3.Dot(ua, ub), -1f, 1f)) * MathF.Max(ra, rb);
        var n = Math.Clamp((int)MathF.Ceiling(MathF.Max(arc, MathF.Abs(rb - ra)) / NavGraph.SampleStep), 1, 128);
        var prevH = ra;
        for (var k = 1; k <= n; k++)
        {
            var f = k / (float)n;
            var up = Slerp(ua, ub, f);
            var guess = float.Lerp(ra, rb, f);
            var probeTop = MathF.Max(guess, prevH) + StepHeight + 0.05f;
            var start = c + up * probeTop;
            var depth = StepHeight * 2 + 0.6f;
            if (!Physics.Raycast(start, -up, depth, QueryFilter.Navigation, out var hit))
                return false; // nothing underfoot: a hole or a cliff
            if (hit.Distance < 0.001f)
                return false; // the probe started inside something: a wall
            var h = probeTop - hit.Distance;
            if (MathF.Abs(h - prevH) > StepHeight || Vector3.Dot(hit.Normal, up) < MinWalkNormalY)
                return false;
            var feet = c + up * h;
            // Room for the body above the step height.
            var body = SweptMover.CapsuleAt(feet + up * (StepHeight + 0.02f), Stance.Crouch, up);
            if (Physics.OverlapCapsule(new Capsule(body.A, feet + up * (PlayerHeight - PlayerRadius), body.Radius), new QueryFilter(QueryLayers.Nav & ~QueryLayers.Doors)))
                return false;
            prevH = h;
        }
        return MathF.Abs(prevH - rb) < 0.6f;
    }

    /// <summary>
    /// The gravity-zone version of <see cref="CanWalkCurved"/>: could a player walk the straight line from <paramref name="from"/>
    /// to <paramref name="to"/> (feet) when up may differ from +Y, or change, along it? Every ~0.6 m: the local up there, ground
    /// below the previous feet's level within a step (probed along that up), rising or falling no more than the step height
    /// measured along it, walkable against it, and room for the body. Where the up changes between two samples (a zone
    /// boundary) the same rule applies in the new frame, so a 45-degree stair reads as a floor on both sides and a plain wall
    /// doesn't. Deterministic: a pure function of the two points and the map.
    /// </summary>
    public static bool CanWalkField(Vector3 from, Vector3 to)
    {
        if (!GravityFrames.Curved)
            return false;
        var len = Vector3.Distance(from, to);
        var n = Math.Clamp((int)MathF.Ceiling(len / NavGraph.SampleStep), 1, 128);
        var prevFeet = from;
        for (var k = 1; k <= n; k++)
        {
            var guess = Vector3.Lerp(from, to, k / (float)n);
            var up = GravityFrames.UpAt(guess);
            var lift = MathF.Max(0f, Vector3.Dot(prevFeet - guess, up)); // the last feet above the chord, along this up
            var start = guess + up * (lift + StepHeight + 0.05f);
            var depth = lift + StepHeight * 2 + 0.6f;
            if (!Physics.Raycast(start, -up, depth, QueryFilter.Navigation, out var hit))
                return false; // nothing underfoot: a hole or a cliff
            if (hit.Distance < 0.001f)
                return false; // the probe started inside something: a wall
            var feet = hit.Point;
            var upAtFeet = GravityFrames.UpAt(feet);
            if (upAtFeet != up)
            {
                // The ground found lies in another zone than the probe's start: measure it in its own frame.
                up = upAtFeet;
                if (Vector3.Dot(hit.Normal, up) < MinWalkNormalY)
                    return false;
            }
            else if (Vector3.Dot(hit.Normal, up) < MinWalkNormalY)
                return false;
            if (MathF.Abs(Vector3.Dot(feet - prevFeet, up)) > StepHeight)
                return false;
            feet += up * 0.02f;
            // Room for the body above the step height.
            var body = SweptMover.CapsuleAt(feet + up * (StepHeight + 0.02f), Stance.Crouch, up);
            if (Physics.OverlapCapsule(new Capsule(body.A, feet + up * (PlayerHeight - PlayerRadius), body.Radius), new QueryFilter(QueryLayers.Nav & ~QueryLayers.Doors)))
                return false;
            prevFeet = feet;
        }
        var upEnd = GravityFrames.UpAt(to);
        return MathF.Abs(Vector3.Dot(prevFeet - to, upEnd)) < 0.6f;
    }

    /// <summary>Spherical interpolation between unit vectors.</summary>
    public static Vector3 Slerp(Vector3 a, Vector3 b, float t)
    {
        var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        var theta = MathF.Acos(dot) * t;
        var rel = b - a * dot;
        var len = rel.Length();
        if (len < 1e-6f)
            return a;
        rel /= len;
        return a * MathF.Cos(theta) + rel * MathF.Sin(theta);
    }
}
