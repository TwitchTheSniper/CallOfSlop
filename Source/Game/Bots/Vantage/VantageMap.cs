using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>The weapon range bands vantage points are scored for (BotVantage.cs picks one from the bot's gun).</summary>
public enum VantageBand : byte
{
    /// <summary>LMGs (and launchers hunting a kill streak): 12-40 m, cover matters most.</summary>
    Medium,

    /// <summary>DMRs: 20-60 m.</summary>
    MediumLong,

    /// <summary>Sniper rifles: 35-100 m.</summary>
    Long,
}

/// <summary>One precomputed vantage point: a nav node with height over its surroundings, cover, and long sightlines.</summary>
public struct VantageSpot
{
    /// <summary>The nav node it sits on (<see cref="NavGraph.Nodes"/>).</summary>
    public int Node;

    /// <summary>Feet position.</summary>
    public Vector3 Pos;

    /// <summary>Metres above the average of the nav nodes within <see cref="VantageMap.HeightRadius"/> (a rooftop, a hill, an upper floor).</summary>
    public float Height;

    /// <summary>0..1: share of the nearby nodes more than 2.5 m below it (a roof edge, a ridge).</summary>
    public float Overlook;

    /// <summary>Cover score: low walls to peek over, edges to duck back from, side walls, a roof overhead; negative in the open.</summary>
    public float Cover;

    /// <summary>The 8 yaw sectors (bit k = yaw k x 45 degrees) its sightlines over the likely routes and objectives look into.</summary>
    public byte Sectors;

    /// <summary>Sectors with low cover (blocked at crouch height, open at eye height): duck behind it, stand to peek.</summary>
    public byte LowCover;

    /// <summary>Something overhead within 4 m (inside an upper floor, under a roof): hidden from above, no use against aircraft.</summary>
    public bool Roofed;

    /// <summary>Bit i (i &lt; 12): in sight of hill zone i; bits 12 and 13: in sight of flag stand 0 / 1.</summary>
    public ushort Objectives;

    /// <summary>Visible interest points weighted for each band (<see cref="VantageBand"/>).</summary>
    public float SightMedium, SightMediumLong, SightLong;

    /// <summary>Final scores per band (0: not a vantage point for that band).</summary>
    public float ScoreMedium, ScoreMediumLong, ScoreLong;

    public readonly float Score(VantageBand band) => band switch { VantageBand.Medium => ScoreMedium, VantageBand.MediumLong => ScoreMediumLong, _ => ScoreLong };

    public readonly float Sight(VantageBand band) => band switch { VantageBand.Medium => SightMedium, VantageBand.MediumLong => SightMediumLong, _ => SightLong };
}

/// <summary>
/// The vantage points of one map's nav graph, worked out once per graph on a worker thread (the first time bots play the
/// map) and cached with the graph. Candidates are the main-area nav nodes with the most height over their surroundings
/// (rooftops, upper floors, hills, ridges) or the most open view; each gets a ray cast to every interest point (the hill
/// zones and flag stands, and a ground-level node in each cell of an 8 x 8 grid over the map, standing in for the routes
/// players walk), short rays round it for cover (a parapet or sandbag to peek over, a roof edge, walls, a roof), and a score per
/// weapon range band. The best few dozen per map are kept, at least 5 m apart.
///
/// Derived data only: nothing here goes into the map, its fingerprint or the network. Only static geometry is queried
/// (through the <see cref="PhysicsWorld"/> captured on the game thread, whose BVH is already built), so the worker never
/// touches movers, players or <see cref="World"/>'s statics.
/// </summary>
public sealed class VantageMap
{
    public const float HeightRadius = 14f;
    public const int MaxCandidates = 360;
    public const int MaxSpots = 96;
    public const float MinSpacing = 5f;
    public const float MaxSight = 120f;

    public NavGraph Graph = NavGraph.Empty;
    public VantageSpot[] Spots = [];
    public int Candidates, InterestPoints, Rays;

    /// <summary>Wall-clock time of the build (on the worker thread).</summary>
    public double BuildMs;

    // ---- Cache and the worker ---------------------------------------------------------------------------------

    private sealed class Job
    {
        public Task? Task;
        public volatile VantageMap? Result;
    }

    private static readonly object Lock = new();
    private static readonly Dictionary<NavGraph, Job> Jobs = new(ReferenceEqualityComparer.Instance);
    private static NavGraph? _lastGraph;
    private static VantageMap? _last;

    /// <summary>The finished vantage points for <paramref name="g"/>, or null while they're being worked out (or never asked for). Cheap.</summary>
    public static VantageMap? For(NavGraph g)
    {
        if (ReferenceEquals(g, _lastGraph))
            return _last;
        lock (Lock)
        {
            if (!Jobs.TryGetValue(g, out var job) || job.Result == null)
                return null;
            _lastGraph = g;
            _last = job.Result;
            return _last;
        }
    }

    /// <summary>
    /// Starts working out <paramref name="g"/>'s vantage points on a worker thread if that hasn't been done (game thread: the
    /// map must be <see cref="World.Map"/>, so its physics world and objectives can be captured). With <paramref name="wait"/>,
    /// blocks until they're ready (a "build it now" moment: self-tests, so seeded matches play the same every run).
    /// </summary>
    public static void Ensure(Map map, NavGraph g, bool wait = false)
    {
        if (g.Nodes.Length == 0 || ReferenceEquals(g, _lastGraph))
            return;
        Job? job;
        lock (Lock)
        {
            if (!Jobs.TryGetValue(g, out job))
            {
                job = new Job();
                Jobs[g] = job;
                var input = Capture(map, g);
                if (wait)
                    job.Result = SafeBuild(input);
                else
                {
                    var j = job;
                    job.Task = Task.Run(() => j.Result = SafeBuild(input));
                }
            }
        }
        if (wait && job.Result == null)
            job.Task?.Wait();
    }

    /// <summary>What the worker needs, captured on the game thread.</summary>
    private readonly record struct Input(NavGraph Graph, PhysicsWorld Physics, string MapName, float HalfX, float HalfZ, Vector3[] Hills, Vector3[] Flags);

    private static Input Capture(Map map, NavGraph g)
    {
        var physics = World.Physics;
        _ = physics.Tree; // the BVH is built here, on the game thread; the worker only reads it
        var o = map.Objectives;
        var hills = new Vector3[Math.Min(o.Hills.Count, 12)];
        for (var i = 0; i < hills.Length; i++)
            hills[i] = o.Hills[i].Center;
        return new Input(g, physics, map.Name, map.HalfX, map.HalfZ, hills, (Vector3[])o.Flags.Clone());
    }

    private static VantageMap SafeBuild(Input input)
    {
        try
        {
            var v = Build(input);
            Console.WriteLine($"[bots] vantage points for {input.MapName}: {v.Spots.Length} spots from {v.Candidates} candidates, "
                              + $"{v.InterestPoints} interest points, {v.Rays} rays, {v.BuildMs:0} ms (worker thread)");
            return v;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[bots] vantage points for {input.MapName} failed: {ex.Message}");
            return new VantageMap { Graph = input.Graph };
        }
    }

    /// <summary>Self-test: works the vantage points out again for the current map's graph on this thread (not cached).</summary>
    public static VantageMap BuildNow(Map map, NavGraph g) => Build(Capture(map, g));

    /// <summary>Self-test: scores one nav node exactly as the build scores a candidate (to compare a street node with the picks).</summary>
    public static VantageSpot Evaluate(Map map, NavGraph g, int node)
    {
        var input = Capture(map, g);
        var adv = new float[g.Nodes.Length];
        var below = new float[g.Nodes.Length];
        var near = new List<int>(256);
        foreach (var i in g.MainNodes)
            HeightAdvantage(g, i, near, out adv[i], out below[i]);
        var points = InterestPointsFor(input, adv, near, out var weights, out var objBits);
        var rays = 0;
        return Score(input, node, adv[node], below[node], points, weights, objBits, ref rays);
    }

    // ---- Building ------------------------------------------------------------------------------------------------

    private static readonly QueryFilter Sight = new(QueryLayers.Static & ~QueryLayers.Clip);
    private static readonly QueryFilter Solid = new(QueryLayers.Static);

    private static VantageMap Build(Input input)
    {
        var t0 = Stopwatch.GetTimestamp();
        var g = input.Graph;
        var n = g.Nodes.Length;
        var v = new VantageMap { Graph = g };
        if (g.MainNodes.Length == 0)
            return v;

        // 1. Height over the surroundings, for every main-area node.
        var adv = new float[n];
        var below = new float[n];
        var near = new List<int>(256);
        foreach (var i in g.MainNodes)
            HeightAdvantage(g, i, near, out adv[i], out below[i]);

        // 2. Candidates: the most elevated or most open nodes (cheap pre-score, deterministic order) that a bot can both
        // get to and get back from (a roof you can only drop off is in the main area, but nobody walks up to it).
        var reach = TwoWayReach(g);
        var main = g.MainNodes;
        var pre = new float[main.Length];
        var order = new int[main.Length];
        for (var k = 0; k < main.Length; k++)
        {
            var i = main[k];
            pre[k] = reach[i] ? MathF.Max(adv[i], 0f) + below[i] * 2f + g.Openness[i] * 0.35f : -1f;
            order[k] = k;
        }
        Array.Sort(order, (a, b) => pre[b] != pre[a] ? pre[b].CompareTo(pre[a]) : a.CompareTo(b));
        var count = Math.Min(main.Length, MaxCandidates);

        // 3. Interest points: objectives, and a ground-level node per cell of an 8 x 8 grid (the routes people walk).
        var points = InterestPointsFor(input, adv, near, out var weights, out var objBits);
        v.InterestPoints = points.Length;

        // 4. Score each candidate: sightlines per band, cover, height.
        var scored = new List<VantageSpot>(count);
        var rays = 0;
        for (var k = 0; k < count; k++)
        {
            var i = main[order[k]];
            if (pre[order[k]] <= 0.3f)
                break; // the rest are flat and boxed in
            var s = Score(input, i, adv[i], below[i], points, weights, objBits, ref rays);
            v.Candidates++;
            if (s.ScoreMedium > 0 || s.ScoreMediumLong > 0 || s.ScoreLong > 0)
                scored.Add(s);
        }

        // 5. Keep the best, spread out (greedy: best first, nothing within MinSpacing of a kept one).
        scored.Sort((a, b) =>
        {
            var sa = MathF.Max(a.ScoreLong, MathF.Max(a.ScoreMediumLong, a.ScoreMedium));
            var sb = MathF.Max(b.ScoreLong, MathF.Max(b.ScoreMediumLong, b.ScoreMedium));
            return sb != sa ? sb.CompareTo(sa) : a.Node.CompareTo(b.Node);
        });
        var kept = new List<VantageSpot>(MaxSpots);
        foreach (var s in scored)
        {
            var clash = false;
            foreach (var q in kept)
                if (MathF.Abs(q.Pos.Y - s.Pos.Y) < 2.5f && Flat(q.Pos - s.Pos) < MinSpacing)
                {
                    clash = true;
                    break;
                }
            if (clash)
                continue;
            kept.Add(s);
            if (kept.Count == MaxSpots)
                break;
        }
        v.Spots = kept.ToArray();
        v.Rays = rays;
        v.BuildMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        return v;
    }

    private static float Flat(Vector3 d) => MathF.Sqrt(d.X * d.X + d.Z * d.Z);

    /// <summary>
    /// The nodes of the graph's biggest strongly connected group: every one can be walked to from every other, following
    /// edges one way (a drop can't be climbed back up, so a roof you can only jump off isn't in it). Kosaraju's two passes,
    /// iterative, O(edges).
    /// </summary>
    private static bool[] TwoWayReach(NavGraph g)
    {
        var n = g.Nodes.Length;
        var both = new bool[n];
        if (n == 0)
            return both;
        // Pass 1: finish order of a depth-first walk over the edges.
        var order = new int[n];
        var oc = 0;
        var seen = new bool[n];
        var stackNode = new int[n];
        var stackEdge = new int[n];
        for (var s0 = 0; s0 < n; s0++)
        {
            if (seen[s0])
                continue;
            seen[s0] = true;
            stackNode[0] = s0;
            stackEdge[0] = g.EdgeStart[s0];
            var sp = 1;
            while (sp > 0)
            {
                var v = stackNode[sp - 1];
                var e = stackEdge[sp - 1];
                if (e < g.EdgeStart[v + 1])
                {
                    stackEdge[sp - 1]++;
                    var w = g.EdgeTo[e];
                    if (seen[w])
                        continue;
                    seen[w] = true;
                    stackNode[sp] = w;
                    stackEdge[sp] = g.EdgeStart[w];
                    sp++;
                }
                else
                {
                    order[oc++] = v;
                    sp--;
                }
            }
        }
        // Pass 2: over the reversed edges, latest finisher first; each walk is one group.
        var rstart = new int[n + 1];
        foreach (var to in g.EdgeTo)
            rstart[to + 1]++;
        for (var i = 0; i < n; i++)
            rstart[i + 1] += rstart[i];
        var rfrom = new int[g.EdgeTo.Length];
        var fill = (int[])rstart.Clone();
        for (var i = 0; i < n; i++)
            for (var e = g.EdgeStart[i]; e < g.EdgeStart[i + 1]; e++)
                rfrom[fill[g.EdgeTo[e]]++] = i;
        var comp = new int[n];
        Array.Fill(comp, -1);
        int groups = 0, biggest = -1, biggestSize = 0;
        for (var k = n - 1; k >= 0; k--)
        {
            var v = order[k];
            if (comp[v] >= 0)
                continue;
            comp[v] = groups;
            stackNode[0] = v;
            var sp = 1;
            var size = 0;
            while (sp > 0)
            {
                var u = stackNode[--sp];
                size++;
                for (var r = rstart[u]; r < rstart[u + 1]; r++)
                {
                    var x = rfrom[r];
                    if (comp[x] >= 0)
                        continue;
                    comp[x] = groups;
                    stackNode[sp++] = x;
                }
            }
            if (size > biggestSize)
                (biggestSize, biggest) = (size, groups);
            groups++;
        }
        for (var i = 0; i < n; i++)
            both[i] = comp[i] == biggest;
        return both;
    }

    /// <summary>Height over the mean of the main-area nodes within <see cref="HeightRadius"/>, and the share of them 2.5+ m below.</summary>
    private static void HeightAdvantage(NavGraph g, int i, List<int> near, out float adv, out float below)
    {
        var p = g.Nodes[i];
        near.Clear();
        g.Within(p, HeightRadius, near);
        double sum = 0;
        int cnt = 0, low = 0;
        foreach (var j in near)
        {
            if (j == i || g.Component[j] != g.MainComponent)
                continue;
            var dy = g.Nodes[j].Y - p.Y;
            if (dy > 20f || dy < -20f)
                continue;
            sum += g.Nodes[j].Y;
            cnt++;
            if (dy < -2.5f)
                low++;
        }
        adv = cnt > 0 ? p.Y - (float)(sum / cnt) : 0f;
        below = cnt > 0 ? low / (float)cnt : 0f;
    }

    /// <summary>
    /// The points sightlines are scored against: hill zones and flag stands (weight 3), and the most ground-level node near
    /// the middle of each cell of an 8 x 8 grid over the map (weight 1), all at chest height. <paramref name="adv"/> must
    /// hold the height advantage of every main-area node (unset nodes count as ground level).
    /// </summary>
    private static Vector3[] InterestPointsFor(Input input, float[] adv, List<int> near, out float[] weights, out ushort[] objBits)
    {
        var g = input.Graph;
        var pts = new List<Vector3>(80);
        var w = new List<float>(80);
        var bits = new List<ushort>(80);
        for (var h = 0; h < input.Hills.Length; h++)
        {
            pts.Add(input.Hills[h] + new Vector3(0, 1.2f, 0));
            w.Add(3f);
            bits.Add((ushort)(1 << h));
        }
        for (var f = 0; f < input.Flags.Length && f < 2; f++)
        {
            pts.Add(input.Flags[f] + new Vector3(0, 1.2f, 0));
            w.Add(3f);
            bits.Add((ushort)(1 << (12 + f)));
        }
        const int grid = 8;
        float cw = input.HalfX * 2 / grid, ch = input.HalfZ * 2 / grid;
        for (var gx = 0; gx < grid; gx++)
        for (var gz = 0; gz < grid; gz++)
        {
            var centre = new Vector3(-input.HalfX + (gx + 0.5f) * cw, 0, -input.HalfZ + (gz + 0.5f) * ch);
            var best = -1;
            var bestScore = float.MaxValue;
            near.Clear();
            g.Within(centre, MathF.Max(cw, ch) * 0.6f, near);
            foreach (var j in near)
            {
                if (g.Component[j] != g.MainComponent)
                    continue;
                // Ground level (not a roof), near the cell's middle.
                var score = MathF.Max(adv[j], 0f) * 6f + Flat(g.Nodes[j] - centre) * 0.1f;
                if (score < bestScore || (score == bestScore && j < best))
                {
                    bestScore = score;
                    best = j;
                }
            }
            if (best < 0)
                continue;
            pts.Add(g.Nodes[best] + new Vector3(0, 1.2f, 0));
            w.Add(1f);
            bits.Add(0);
        }
        weights = w.ToArray();
        objBits = bits.ToArray();
        return pts.ToArray();
    }

    /// <summary>How much a sightline of length <paramref name="d"/> counts for a band: a trapezoid (0 outside, 1 in the sweet spot).</summary>
    public static float BandWeight(VantageBand band, float d) =>
        band switch
        {
            VantageBand.Medium => Trapezoid(d, 10f, 15f, 40f, 60f),
            VantageBand.MediumLong => Trapezoid(d, 15f, 25f, 60f, 85f),
            _ => Trapezoid(d, 20f, 35f, 100f, MaxSight),
        };

    private static float Trapezoid(float d, float a, float b, float c, float e) =>
        d <= a || d >= e ? 0f : d < b ? (d - a) / (b - a) : d <= c ? 1f : (e - d) / (e - c);

    /// <summary>The longest sightline a band cares about.</summary>
    public static float BandMax(VantageBand band) => band switch { VantageBand.Medium => 60f, VantageBand.MediumLong => 85f, _ => MaxSight };

    private static VantageSpot Score(Input input, int node, float adv, float below, Vector3[] points, float[] weights, ushort[] objBits, ref int rays)
    {
        var c = input.Physics;
        var feet = input.Graph.Nodes[node];
        var eye = feet + new Vector3(0, World.EyeHeight, 0);
        var s = new VantageSpot { Node = node, Pos = feet, Height = adv, Overlook = below };

        // Sightlines to the interest points.
        Span<float> bins = stackalloc float[8];
        bins.Clear();
        for (var k = 0; k < points.Length; k++)
        {
            var d = points[k] - eye;
            var dist = d.Length();
            if (dist < 8f || dist > MaxSight)
                continue;
            rays++;
            if (c.Raycast(eye, d / dist, dist, Sight) < dist - 0.3f)
                continue;
            var w = weights[k];
            s.SightMedium += w * BandWeight(VantageBand.Medium, dist);
            s.SightMediumLong += w * BandWeight(VantageBand.MediumLong, dist);
            s.SightLong += w * BandWeight(VantageBand.Long, dist);
            s.Objectives |= objBits[k];
            var yaw = MathF.Atan2(d.X, -d.Z);
            var bin = ((int)MathF.Round(yaw / (MathF.Tau / 8)) % 8 + 8) % 8;
            bins[bin] += w;
        }
        var top = 0f;
        foreach (var b in bins)
            top = MathF.Max(top, b);
        for (var k = 0; k < 8; k++)
            if (top > 0 && bins[k] >= top * 0.2f)
                s.Sectors |= (byte)(1 << k);

        // Cover: a parapet to peek over (blocked low, open at the eye), walls, roof edges to duck back from, a roof.
        int lowCover = 0, walls = 0, edges = 0;
        for (var k = 0; k < 8; k++)
        {
            var a = k * MathF.Tau / 8;
            var dir = new Vector3(MathF.Sin(a), 0, -MathF.Cos(a)); // yaw k x 45 degrees (World.FlatForward's convention)
            rays += 2;
            var lowHit = c.Raycast(feet + new Vector3(0, 0.75f, 0), dir, 1.6f, Solid) < 1.6f;
            var highHit = c.Raycast(feet + new Vector3(0, 1.5f, 0), dir, 1.6f, Solid) < 1.6f;
            if (lowHit && !highHit)
            {
                lowCover++;
                s.LowCover |= (byte)(1 << k);
            }
            else if (lowHit)
                walls++;
            else
            {
                // An edge: no floor within 2.5 m a body width out (a roof edge, the lip of a ridge).
                rays++;
                var probe = feet + dir * 1.8f + new Vector3(0, 0.5f, 0);
                if (c.Raycast(probe, -Vector3.UnitY, 3f, Solid) >= 3f)
                    edges++;
            }
        }
        rays++;
        s.Roofed = c.Raycast(feet + new Vector3(0, 1.0f, 0), Vector3.UnitY, 3f, Solid) < 3f;
        var cover = MathF.Min(lowCover, 3) * 1.0f + MathF.Min(walls, 4) * 0.35f + MathF.Min(edges, 3) * 0.5f + (s.Roofed ? 0.6f : 0f);
        if (lowCover + walls + edges == 0)
            cover -= 1.5f; // in the open
        if (walls >= 6)
            cover -= 2f; // boxed in
        s.Cover = cover;

        // Scores per band: sightlines first, then height and cover (LMGs: cover counts more, height less).
        var h = Math.Clamp(adv, 0f, 10f);
        float Band(float sight, float hw, float cw) =>
            sight < 2f ? 0f : MathF.Max(0.01f, MathF.Min(sight, 14f) * 1.2f + h * hw + below * 1.5f + cover * cw);
        s.ScoreMedium = Band(s.SightMedium, 0.3f, 1.5f);
        s.ScoreMediumLong = Band(s.SightMediumLong, 0.6f, 1f);
        s.ScoreLong = Band(s.SightLong, 0.7f, 1f);
        // Long guns want height: a sniper spot must be up on something (a roof, an upper floor, a hill, a ridge); a DMR spot
        // a little raised, or behind a parapet it can peek over. LMG spots go on cover alone.
        if (adv < 1.2f && below < 0.2f)
            s.ScoreLong = 0;
        if (adv < 0.8f && below < 0.12f && !(s.LowCover != 0 && cover >= 2f))
            s.ScoreMediumLong = 0;
        return s;
    }
}
