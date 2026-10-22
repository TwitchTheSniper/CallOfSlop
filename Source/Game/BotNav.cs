using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A walkable-surface graph for one map, built once (a slice per tick, see <see cref="Pump"/>) the first time bots play on it.
///
/// Nodes sit on a grid over every surface a player can stand on (the ground and the tops of boxes), plus the
/// map's own waypoints and spawns. Edges join nearby nodes when a player could really walk the straight line
/// between them: every ~0.6 m the ground is found with a downward ray and the body is checked for room with
/// <see cref="World.Blocked"/>, allowing steps (up to <see cref="World.StepHeight"/>), mantles (up to
/// <see cref="World.MantleReach"/>, flagged so the bot presses Jump+Forward) and drops (one way). Only the shared
/// collision queries are used, so whatever geometry the world collides with (boxes today, terrain later) shapes
/// the graph: a cliff simply gets no edge.
///
/// Plain arrays (CSR adjacency) and no allocations after the build, so searches are cheap for many bots.
/// </summary>
public sealed class NavGraph
{
    public const float SampleStep = 0.6f; // must stay under 2 x PlayerRadius so no wall slips between samples
    public const float MaxDrop = 6f; // bots won't plan jumps down from higher than this
    public const byte FlagMantle = 1, FlagDrop = 2;

    public string MapName = "";
    public uint MapHash;
    public float Spacing;

    /// <summary>Feet positions of the nodes.</summary>
    public Vector3[] Nodes = [];

    /// <summary>How open the view is from each node (0..8 directions with a long sightline), for marksmen and anchors.</summary>
    public byte[] Openness = [];

    /// <summary>Edges of node i are EdgeTo[EdgeStart[i] .. EdgeStart[i+1]).</summary>
    public int[] EdgeStart = [0];

    public int[] EdgeTo = [];
    public float[] EdgeCost = [];
    public byte[] EdgeFlags = [];

    /// <summary>For mantle edges: where the climb is (the first spot on the higher surface).</summary>
    public Vector3[] EdgeLedge = [];

    /// <summary>Connected group of each node (edges treated as two-way) and the biggest group.</summary>
    public int[] Component = [];

    public int MainComponent;

    /// <summary>Nodes in the biggest connected group: where bots pick roaming goals.</summary>
    public int[] MainNodes = [];

    public double BuildMs;
    public int EdgeCount => EdgeTo.Length;
    public int MantleEdges { get; private set; }

    // Spatial lookup: nodes bucketed into square cells of size Spacing.
    private float _minX, _minZ;
    private int _cols, _rows;
    private int[] _cellStart = [0];
    private int[] _cellNodes = [];

    // ---- Cache and incremental building ----------------------------------------------------------

    private static readonly Dictionary<(string, uint), NavGraph> Cache = new();
    private static Map? _lastMap;
    private static NavGraph? _last;
    private static IEnumerator<NavGraph?>? _builder;
    private static Map? _builderMap;
    private static Map? _hashMap;
    private static uint _hash;

    /// <summary>An empty graph, used while the real one is being built: bots steer straight at their goals meanwhile.</summary>
    public static readonly NavGraph Empty = CreateEmpty();

    private static NavGraph CreateEmpty()
    {
        var g = new NavGraph { Spacing = 1 };
        g._cols = g._rows = 1;
        g._cellStart = [0, 0];
        return g;
    }

    /// <summary>
    /// The graph for the map being played (<see cref="World.Map"/>), or <see cref="Empty"/> while it's still being
    /// built (see <see cref="Pump"/>). Graphs are cached per map name + fingerprint.
    /// </summary>
    public static NavGraph Current
    {
        get
        {
            var map = World.Map;
            if (_last != null && ReferenceEquals(_lastMap, map))
                return _last;
            if (!ReferenceEquals(_hashMap, map))
            {
                _hashMap = map; // the fingerprint walks every box: work it out once per map, not every tick of a build
                _hash = map.ContentHash;
            }
            if (!Cache.TryGetValue((map.Name, _hash), out var cached))
                return Empty;
            _lastMap = map;
            _last = cached;
            return cached;
        }
    }

    /// <summary>
    /// Builds the current map's graph a slice at a time: call once per server tick with a time budget. The build runs
    /// on the game thread in small steps (no threads, so it's safe with any collision backend), and bots get the
    /// graph when it's done, typically within a second or two of a map change; until then they steer straight.
    /// </summary>
    public static void Pump(double budgetMs)
    {
        var map = World.Map;
        if (_last != null && ReferenceEquals(_lastMap, map))
            return;
        if (Current != Empty)
            return;
        if (_builder != null && !ReferenceEquals(_builderMap, map))
            _builder = null; // the map changed under it
        if (_builder == null)
        {
            _builderMap = map;
            _builder = BuildSteps(map).GetEnumerator();
        }
        var start = Stopwatch.GetTimestamp();
        try
        {
            while (_builder.MoveNext())
            {
                if (_builder.Current is { } g)
                {
                    Store(map, g);
                    _builder = null;
                    return;
                }
                if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= budgetMs)
                    return;
            }
            _builder = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[bots] nav graph for {map.Name} failed: {ex.Message}");
            Store(map, CreateEmpty()); // don't retry every tick
            _builder = null;
        }
    }

    private static void Store(Map map, NavGraph g)
    {
        Cache[(map.Name, map.ContentHash)] = g;
        Console.WriteLine($"[bots] nav graph for {map.Name}: {g.Nodes.Length} nodes, {g.EdgeCount} edges ({g.MantleEdges} mantles), "
            + $"{g.MainNodes.Length} in the main area, {g.BuildMs:0} ms of work");
    }

    /// <summary>Builds the whole graph for <paramref name="map"/> right now (it must be <see cref="World.Map"/>).</summary>
    public static NavGraph Build(Map map)
    {
        foreach (var g in BuildSteps(map))
            if (g != null)
                return g;
        throw new InvalidOperationException("nav graph build ended without a result");
    }

    // ---- Building -------------------------------------------------------------------------------

    /// <summary>
    /// The build as a sequence of small steps: yields null after each slice of work and the finished graph last.
    /// The map must stay <see cref="World.Map"/> throughout (the collision queries use it); <see cref="Pump"/> drops
    /// the build if it changes. <see cref="BuildMs"/> counts only the time spent working.
    /// </summary>
    private static IEnumerable<NavGraph?> BuildSteps(Map map)
    {
        if (!ReferenceEquals(World.Map, map))
            throw new InvalidOperationException("NavGraph.Build needs the map to be the current World.Map");
        var work = 0.0;
        var t0 = Stopwatch.GetTimestamp();
        var g = new NavGraph { MapName = map.Name, MapHash = map.ContentHash };
        var c = World.Physics; // the map's collision world: every probe below goes through its BVH (map is World.Map)
        var s = Math.Clamp(MathF.Sqrt(4 * map.HalfX * map.HalfZ / 1000f), 3f, 7f);
        g.Spacing = s;

        var top = 2f;
        foreach (var b in map.Solid)
            top = MathF.Max(top, b.Max.Y);
        top = MathF.Min(top + 1f, 59f);
        if (map.TerrainCollision is { } terrain)
            top = MathF.Max(top, terrain.Data.MaxHeight + 2f); // hills can rise above every box

        // ---- Nodes: every standable surface at each grid point, plus the map's waypoints and spawns ----
        var nodes = new List<Vector3>();
        for (var x = -map.HalfX + s / 2; x < map.HalfX; x += s)
        {
            for (var z = -map.HalfZ + s / 2; z < map.HalfZ; z += s)
            {
                AddColumn(map, c, nodes, x, z, s, top);
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }
        var gridCount = nodes.Count;
        var extra = 0;
        // Waypoints and spawns, then points along every terrain cave (TerrainVoids.cs) so tunnels get nodes even where the
        // grid misses them.
        foreach (var w in map.Waypoints.Concat(map.Spawns).Concat(map.Terrain?.Voids?.NavPoints ?? Array.Empty<Vector3>()))
        {
            var y = Ground(c, w.X, w.Z, w.Y + 1f, 3f);
            if (float.IsFinite(y) && Place(map, c, new Vector3(w.X, y, w.Z), 1.2f) is { } p && !NearExisting(nodes, p, 0, 0.8f))
                nodes.Add(p);
            if (++extra % 4 == 0)
            {
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }

        // ---- Edges between nearby nodes ----
        g.Nodes = nodes.ToArray();
        g.BuildCells(map);
        var edges = new List<(int From, int To, float Cost, byte Flags, Vector3 Ledge)>(nodes.Count * 11); // one big allocation, not a series
        var radius = s * 1.5f;
        var near = new List<int>();
        for (var i = 0; i < g.Nodes.Length; i++)
        {
            var a = g.Nodes[i];
            var r = i < gridCount ? radius : s * 2.1f;
            near.Clear();
            g.Within(a, r, near);
            foreach (var j in near)
            {
                // Grid nodes link to later grid nodes; waypoint nodes (a wider reach) link to every grid node and later waypoints.
                var link = i < gridCount ? j > i && j < gridCount : j < gridCount || j > i;
                if (!link || !TryLink(g, c, i, j, edges))
                    continue;
                // A slice per link walked, not per node: a node on a busy spot can have dozens of neighbours.
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }

        // ---- Drop nodes nobody can reach or leave, then pack into CSR ----
        var degree = new int[g.Nodes.Length];
        foreach (var e in edges)
        {
            degree[e.From]++;
            degree[e.To]++;
        }
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
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        // CSR: count each node's edges, then drop every edge into its node's slice (a counting sort, O(edges)).
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
        for (var k = 0; k < edges.Count; k++)
        {
            var e = edges[k];
            var at = slot[remap[e.From]]++;
            g.EdgeTo[at] = remap[e.To];
            g.EdgeCost[at] = e.Cost;
            g.EdgeFlags[at] = e.Flags;
            g.EdgeLedge[at] = e.Ledge;
            if ((e.Flags & FlagMantle) != 0)
                g.MantleEdges++;
        }
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        g.BuildCells(map);
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        g.BuildComponents();
        if (map.Terrain?.Voids != null)
            g.DropTraps(map.Terrain);
        work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return null;
        t0 = Stopwatch.GetTimestamp();

        // ---- How open the view is from each node ----
        g.Openness = new byte[count];
        for (var i = 0; i < count; i++)
        {
            g.Openness[i] = OpennessAt(c, g.Nodes[i]);
            if (i % 4 == 3)
            {
                work += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                yield return null;
                t0 = Stopwatch.GetTimestamp();
            }
        }
        g.BuildMs = work + Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        yield return g;
    }

    /// <summary>Adds a node for every surface a player can stand on at grid point (x, z).</summary>
    private static void AddColumn(Map map, PhysicsWorld c, List<Vector3> nodes, float x, float z, float s, float top)
    {
        Span<float> heights = stackalloc float[16];
        var n = 0;
        var columnStart = nodes.Count;
        // Whatever the world says is the highest ground here (covers non-box ground too).
        var t = c.Raycast(new Vector3(x, top, z), -Vector3.UnitY, top + 6f, QueryFilter.Navigation);
        if (t > 0.001f && t < top + 6f)
            heights[n++] = top - t;
        n += c.TopsAt(x, z, top, heights[n..]); // the top of every box over this point
        // A roofed terrain cave under this point (TerrainVoids.cs): its floor is a surface too.
        if (map.Terrain is { Voids: not null } data && n < heights.Length
            && data.Column(x, z, out var caveFloor, out _, out _, out var hollow) == TerrainVoids.Cave && hollow)
            heights[n++] = caveFloor;
        for (var i = 0; i < n; i++)
            if (Place(map, c, new Vector3(x, heights[i], z), s) is { } p && !NearExisting(nodes, p, columnStart, s * 0.6f))
                nodes.Add(p);
    }

    private static bool NearExisting(List<Vector3> nodes, Vector3 p, int from, float within = 0.5f)
    {
        for (var i = Math.Max(0, from); i < nodes.Count; i++)
            if (MathF.Abs(nodes[i].Y - p.Y) < 0.5f && MathF.Abs(nodes[i].X - p.X) < within && MathF.Abs(nodes[i].Z - p.Z) < within)
                return true;
        return false;
    }

    /// <summary>A spot near <paramref name="p"/> on the same surface where a standing player fits, or null.</summary>
    private static Vector3? Place(Map map, PhysicsWorld col, Vector3 p, float spread)
    {
        ReadOnlySpan<float> offsets = [0f, 0.3f, -0.3f];
        foreach (var ox in offsets)
        foreach (var oz in offsets)
        {
            var c = new Vector3(p.X + ox * spread, p.Y, p.Z + oz * spread);
            if (MathF.Abs(c.X) > map.HalfX - 0.5f || MathF.Abs(c.Z) > map.HalfZ - 0.5f)
                continue;
            if (map.Boundary is { } boundary && !boundary.Inside(c.X, c.Z, 0.6f))
                continue; // never a node outside (or hard against) a shaped map's outline
            var y = Ground(col, c.X, c.Z, p.Y + 0.3f, 0.6f);
            if (!float.IsFinite(y) || MathF.Abs(y - p.Y) > 0.05f)
                continue;
            c.Y = y;
            // On the terrain: only where a player can stand (SlopeRules), so no node sits on a bank or a ramp's edge.
            if (col.Terrain?.Layer(y) is { } t && y - t.HeightAt(c.X, c.Z) < 0.05f && !SlopeRules.NavWalkable(t, c.X, c.Z))
                continue;
            if (!col.BlockedNav(c + new Vector3(0, 0.01f, 0))) // (building doors don't count: bots open them)
                return c;
        }
        return null;
    }

    /// <summary>
    /// Height of the first surface below (x, fromY, z) within <paramref name="depth"/>: NaN if there's none,
    /// +infinity if the start point is already inside something (a wall too tall to climb).
    /// </summary>
    private static float Ground(PhysicsWorld c, float x, float z, float fromY, float depth)
    {
        var t = c.Raycast(new Vector3(x, fromY, z), -Vector3.UnitY, depth, QueryFilter.Navigation);
        if (t >= depth)
            return float.NaN;
        return t < 0.001f ? float.PositiveInfinity : fromY - t;
    }

    /// <summary>Links nodes i and j where a player can walk between them. False if they were rejected without a walk (cheap).</summary>
    private static bool TryLink(NavGraph g, PhysicsWorld c, int i, int j, List<(int, int, float, byte, Vector3)> edges)
    {
        var a = g.Nodes[i];
        var b = g.Nodes[j];
        if (MathF.Abs(a.Y - b.Y) > MaxDrop)
            return false;
        if (TooSteep(a, b) || LeavesBoundary(a, b))
            return true;
        Walk(c, a, b, out var fwd, out var back, out var fFlags, out var bFlags, out var fLedge, out var bLedge);
        var len = Vector3.Distance(a, b);
        if (fwd)
            edges.Add((i, j, SlopeCost(a, b, len) + ((fFlags & FlagMantle) != 0 ? 2f : 0f), fFlags, fLedge));
        if (back)
            edges.Add((j, i, SlopeCost(b, a, len) + ((bFlags & FlagMantle) != 0 ? 2f : 0f), bFlags, bLedge));
        return true;
    }

    /// <summary>
    /// Terrain maps: a link that runs along the ground over a slope players can't walk (<see cref="SlopeRules"/>, the same
    /// rules the movers use: they slide off those) is no use to a bot. Only where the line is on the terrain (not on a box
    /// floor above it). Box steps and ledges are left to <see cref="Walk"/>.
    /// </summary>
    private static bool TooSteep(Vector3 a, Vector3 b)
    {
        if (World.Map.TerrainCollision is not { } ground)
            return false;
        var t = ground.Layer(MathF.Min(a.Y, b.Y));
        if (!ground.HasVoids)
            return !SlopeRules.LineWalkable(t, a, b, onlyNearLine: true);
        // Holes (TerrainVoids.cs): a hole's edge is a wall like a box's side, a ledge for Walk to judge (drop in, mantle out
        // of a shallow pit), so only the stretches on either side of each edge must be walkable ground.
        var data = ground.Data;
        var len = Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
        var n = Math.Clamp((int)MathF.Ceiling(len / 0.25f), 1, 512);
        var margin = len > 0.01f ? 0.35f / len : 1f;
        var start = 0f;
        var hole = data.IsHole(a.X, a.Z);
        for (var k = 1; k <= n; k++)
        {
            var f = k / (float)n;
            var p = Vector3.Lerp(a, b, f);
            var now = data.IsHole(p.X, p.Z);
            if (now == hole && k < n)
                continue;
            var end = now != hole ? (k - 0.5f) / n : 1f;
            float s0 = start > 0 ? start + margin : 0f, s1 = end < 1 ? end - margin : 1f;
            if (s1 > s0 && !SlopeRules.LineWalkable(t, Vector3.Lerp(a, b, s0), Vector3.Lerp(a, b, s1), onlyNearLine: true))
                return true;
            start = end;
            hole = now;
        }
        return false;
    }

    /// <summary>A shaped map: a link that passes outside its outline, or hugs it closer than a body width, is no use to a bot.</summary>
    private static bool LeavesBoundary(Vector3 a, Vector3 b)
    {
        if (World.Map.Boundary is not { } boundary)
            return false;
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z)) / 0.5f));
        for (var k = 0; k <= steps; k++)
        {
            var p = Vector3.Lerp(a, b, k / (float)steps);
            if (!boundary.Inside(p.X, p.Z, 0.3f))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Terrain maps: walking up a slope is slower (<see cref="SlopeRules.SpeedFactor"/>), so an uphill link costs the time it
    /// takes: routes prefer gentler ways up when they're about as short.
    /// </summary>
    private static float SlopeCost(Vector3 from, Vector3 to, float len)
    {
        if (World.Map.TerrainCollision == null)
            return len;
        var run = MathF.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Z - from.Z) * (to.Z - from.Z));
        return len * SlopeRules.UphillTimeFactor(to.Y - from.Y, run);
    }

    /// <summary>
    /// Can a player walk the straight line a -> b (and b -> a)? Samples the ground every <see cref="SampleStep"/>,
    /// allowing steps, mantles and drops, and checks there's room for the body above the step height.
    /// </summary>
    private static void Walk(PhysicsWorld c, Vector3 a, Vector3 b, out bool forward, out bool backward, out byte fFlags, out byte bFlags,
        out Vector3 fLedge, out Vector3 bLedge)
    {
        forward = backward = false;
        fFlags = bFlags = 0;
        fLedge = bLedge = Vector3.Zero;
        var d = new Vector2(b.X - a.X, b.Z - a.Z);
        var len = d.Length();
        var n = Math.Clamp((int)MathF.Ceiling(len / SampleStep), 1, 63);
        Span<float> h = stackalloc float[n + 1];
        Span<bool> blocked = stackalloc bool[n + 1];
        h[0] = a.Y;
        var y = a.Y;
        for (var k = 1; k <= n; k++)
        {
            var f = k / (float)n;
            float x = a.X + d.X * f, z = a.Z + d.Y * f;
            var from = y + World.MantleReach + 0.05f;
            var g = Ground(c, x, z, from, World.MantleReach + 0.05f + MaxDrop);
            if (!float.IsFinite(g))
                return; // a wall too tall to climb, or a hole
            h[k] = g;
            y = g;
            blocked[k] = c.BlockedNav(new Vector3(x, g + World.StepHeight + 0.02f, z), World.PlayerHeight - World.StepHeight - 0.02f);
        }
        if (MathF.Abs(h[n] - b.Y) > 0.35f)
            return; // ended up on a different floor

        // A blocked sample is fine right next to a ledge (pressed against the face before a mantle, or still
        // standing on the edge before a drop): the real movement handles those. Anywhere else it's a wall.
        for (var k = 1; k <= n; k++)
        {
            if (!blocked[k])
                continue;
            var nearLedge = MathF.Abs(h[k] - h[k - 1]) > World.StepHeight || (k < n && MathF.Abs(h[k + 1] - h[k]) > World.StepHeight);
            if (!nearLedge)
                return;
        }

        forward = true;
        backward = true;
        for (var k = 1; k <= n; k++)
        {
            var rise = h[k] - h[k - 1];
            var f = k / (float)n;
            var at = new Vector3(a.X + d.X * f, h[k], a.Z + d.Y * f);
            if (rise > World.StepHeight)
            {
                if ((fFlags & FlagMantle) == 0)
                    fLedge = at;
                fFlags |= FlagMantle;
                bFlags |= FlagDrop;
            }
            else if (rise < -World.StepHeight)
            {
                fFlags |= FlagDrop;
                if (-rise > World.MantleReach - 0.05f)
                    backward = false; // too high to climb back up
                bFlags |= FlagMantle;
            }
        }
        // Walking b -> a, the first ledge met is the one nearest b.
        if ((bFlags & FlagMantle) != 0)
        {
            for (var k = n; k >= 1; k--)
            {
                if (h[k - 1] - h[k] > World.StepHeight)
                {
                    var f = (k - 1) / (float)n;
                    bLedge = new Vector3(a.X + d.X * f, h[k - 1], a.Z + d.Y * f);
                    break;
                }
            }
        }
    }

    private void BuildCells(Map map)
    {
        _minX = map.HalfX * -1 - 1;
        _minZ = map.HalfZ * -1 - 1;
        _cols = Math.Max(1, (int)MathF.Ceiling((map.HalfX * 2 + 2) / Spacing));
        _rows = Math.Max(1, (int)MathF.Ceiling((map.HalfZ * 2 + 2) / Spacing));
        var counts = new int[_cols * _rows + 1];
        foreach (var p in Nodes)
            counts[CellOf(p) + 1]++;
        for (var i = 0; i < _cols * _rows; i++)
            counts[i + 1] += counts[i];
        _cellStart = (int[])counts.Clone();
        _cellNodes = new int[Nodes.Length];
        var fill = (int[])counts.Clone();
        for (var i = 0; i < Nodes.Length; i++)
            _cellNodes[fill[CellOf(Nodes[i])]++] = i;
    }

    private int CellOf(Vector3 p)
    {
        var cx = Math.Clamp((int)((p.X - _minX) / Spacing), 0, _cols - 1);
        var cz = Math.Clamp((int)((p.Z - _minZ) / Spacing), 0, _rows - 1);
        return cz * _cols + cx;
    }

    /// <summary>Adds every node within horizontal distance <paramref name="r"/> of <paramref name="p"/> to <paramref name="into"/>.</summary>
    public void Within(Vector3 p, float r, List<int> into)
    {
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
                var q = Nodes[i];
                var dx = q.X - p.X;
                var dz = q.Z - p.Z;
                if (dx * dx + dz * dz <= r * r)
                    into.Add(i);
            }
        }
    }

    /// <summary>
    /// The best node to start from or head for near <paramref name="p"/>: close, on about the same level, and (when
    /// <paramref name="needSight"/>) in plain view at knee height. -1 if none within a couple of cells.
    /// </summary>
    public int Nearest(Vector3 p, bool needSight = true)
    {
        for (var ring = 1; ring <= 3; ring++)
        {
            var r = Spacing * ring;
            int best = -1, second = -1;
            float bestScore = float.MaxValue, secondScore = float.MaxValue;
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
                    var q = Nodes[i];
                    var dy = q.Y - p.Y;
                    if (dy > World.MantleReach || dy < -MaxDrop)
                        continue;
                    var score = (q.X - p.X) * (q.X - p.X) + (q.Z - p.Z) * (q.Z - p.Z) + dy * dy * 9f;
                    if (score < bestScore)
                    {
                        second = best;
                        secondScore = bestScore;
                        best = i;
                        bestScore = score;
                    }
                    else if (score < secondScore)
                    {
                        second = i;
                        secondScore = score;
                    }
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

    /// <summary>Straight-line sight at knee and chest height between two feet positions.</summary>
    public static bool Clear(Vector3 a, Vector3 b)
    {
        static bool Ray(Vector3 from, Vector3 to)
        {
            var d = to - from;
            var len = d.Length();
            return len < 0.01f || World.Query.Raycast(from, d / len, len, QueryFilter.Navigation) >= len - 0.05f;
        }
        var knee = new Vector3(0, 0.6f, 0);
        var chest = new Vector3(0, 1.3f, 0);
        return Ray(a + knee, b + knee) && Ray(a + chest, b + chest);
    }

    private void BuildComponents()
    {
        var parent = new int[Nodes.Length];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;
        int Find(int x)
        {
            while (parent[x] != x)
                x = parent[x] = parent[parent[x]];
            return x;
        }
        for (var i = 0; i < Nodes.Length; i++)
            for (var e = EdgeStart[i]; e < EdgeStart[i + 1]; e++)
            {
                int ra = Find(i), rb = Find(EdgeTo[e]);
                if (ra != rb)
                    parent[ra] = rb;
            }
        Component = new int[Nodes.Length];
        var size = new Dictionary<int, int>();
        for (var i = 0; i < Nodes.Length; i++)
        {
            Component[i] = Find(i);
            size[Component[i]] = size.GetValueOrDefault(Component[i]) + 1;
        }
        MainComponent = size.Count == 0 ? -1 : size.MaxBy(kv => kv.Value).Key;
        MainNodes = Enumerable.Range(0, Nodes.Length).Where(i => Component[i] == MainComponent).ToArray();
    }

    /// <summary>
    /// Terrain with holes (TerrainVoids.cs): a pit you can drop into but not climb out of joins the main area through its
    /// one-way drop edges. Bots must never pick a goal in one, so the main area keeps only nodes that can both be reached
    /// from and get back to a node on open ground (the main area's middle). Maps without holes are left exactly as they were.
    /// </summary>
    private void DropTraps(TerrainData terrain)
    {
        if (MainNodes.Length == 0)
            return;
        var seed = -1;
        var best = float.MaxValue;
        foreach (var i in MainNodes)
        {
            var p = Nodes[i];
            if (terrain.KindAt(p.X, p.Z) != TerrainVoids.Solid)
                continue;
            var d = p.X * p.X + p.Z * p.Z;
            if (d < best)
                (best, seed) = (d, i);
        }
        if (seed < 0)
            return;
        var n = Nodes.Length;
        var forward = new bool[n];
        var backward = new bool[n];
        var stack = new Stack<int>();
        forward[seed] = true;
        stack.Push(seed);
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            for (var e = EdgeStart[i]; e < EdgeStart[i + 1]; e++)
                if (!forward[EdgeTo[e]])
                {
                    forward[EdgeTo[e]] = true;
                    stack.Push(EdgeTo[e]);
                }
        }
        // Reverse edges as a CSR too.
        var rstart = new int[n + 1];
        foreach (var to in EdgeTo)
            rstart[to + 1]++;
        for (var i = 0; i < n; i++)
            rstart[i + 1] += rstart[i];
        var rfrom = new int[EdgeTo.Length];
        var fill = (int[])rstart.Clone();
        for (var i = 0; i < n; i++)
            for (var e = EdgeStart[i]; e < EdgeStart[i + 1]; e++)
                rfrom[fill[EdgeTo[e]]++] = i;
        backward[seed] = true;
        stack.Push(seed);
        while (stack.Count > 0)
        {
            var i = stack.Pop();
            for (var e = rstart[i]; e < rstart[i + 1]; e++)
                if (!backward[rfrom[e]])
                {
                    backward[rfrom[e]] = true;
                    stack.Push(rfrom[e]);
                }
        }
        var before = MainNodes.Length;
        MainNodes = MainNodes.Where(i => forward[i] && backward[i]).ToArray();
        TrapNodes = before - MainNodes.Length;
    }

    /// <summary>Nodes left out of <see cref="MainNodes"/> because a bot couldn't get back out from them (holes).</summary>
    public int TrapNodes { get; private set; }

    private static byte OpennessAt(PhysicsWorld c, Vector3 feet)
    {
        var eye = feet + new Vector3(0, World.EyeHeight, 0);
        var open = 0;
        for (var k = 0; k < 8; k++)
        {
            var a = k * MathF.Tau / 8;
            if (c.Raycast(eye, new Vector3(MathF.Cos(a), 0, MathF.Sin(a)), 25f, QueryFilter.Navigation) >= 25f)
                open++;
        }
        return (byte)open;
    }
}

/// <summary>
/// A* over a <see cref="NavGraph"/> with reusable buffers (no allocations per search). One per game; searches
/// are rationed per tick by the caller.
/// </summary>
public sealed class NavSearch
{
    private float[] _g = [];
    private int[] _from = [];
    private int[] _stamp = [];
    private bool[] _closed = [];
    private int _gen;
    private int[] _heapNode = new int[64];
    private float[] _heapKey = new float[64];
    private int _heapCount;
    private int[] _reverse = new int[256];

    /// <summary>Nodes expanded by the last search (for the self-test's cost report).</summary>
    public int LastExpanded;

    /// <summary>JIT-compiles <see cref="Find"/> now (a loading moment), not in the first tick that searches.</summary>
    public static void Prepare() =>
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NavSearch).GetMethod(nameof(Find))!.MethodHandle);

    /// <summary>
    /// Finds a path from node <paramref name="start"/> to node <paramref name="goal"/> and writes it (start first) into
    /// <paramref name="path"/>, truncated to its length. Returns the node count, or 0 if there's no way.
    /// <paramref name="cost"/> can add a per-edge penalty (flanking routes, edges a bot got stuck on).
    /// </summary>
    // Fully optimized from the first call: left to tiering, a long search on the game thread compiled an
    // on-stack-replacement copy of this loop mid-tick (5-8 ms of JIT each time). Prepare() compiles it up front.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    public int Find(NavGraph g, int start, int goal, int[] path, Func<int, int, int, float>? cost = null, int maxExpand = 4000)
    {
        LastExpanded = 0;
        if (start < 0 || goal < 0 || start >= g.Nodes.Length || goal >= g.Nodes.Length)
            return 0;
        var n = g.Nodes.Length;
        if (_g.Length < n)
        {
            _g = new float[n];
            _from = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            _gen = 0;
        }
        if (++_gen == int.MaxValue)
        {
            Array.Clear(_stamp);
            _gen = 1;
        }
        _heapCount = 0;
        Visit(start, 0, -1);
        Push(start, H(g, start, goal));
        var found = false;
        while (_heapCount > 0)
        {
            var cur = Pop();
            if (_closed[cur])
                continue;
            _closed[cur] = true;
            if (cur == goal)
            {
                found = true;
                break;
            }
            if (++LastExpanded > maxExpand)
                break;
            var gc = _g[cur];
            for (var e = g.EdgeStart[cur]; e < g.EdgeStart[cur + 1]; e++)
            {
                var to = g.EdgeTo[e];
                var extra = cost?.Invoke(cur, to, e) ?? 0f;
                if (float.IsPositiveInfinity(extra))
                    continue;
                var ng = gc + g.EdgeCost[e] + extra;
                if (_stamp[to] == _gen && (ng >= _g[to] || _closed[to]))
                    continue;
                Visit(to, ng, cur);
                Push(to, ng + H(g, to, goal));
            }
        }
        if (!found)
            return 0;

        var len = 0;
        for (var c = goal; c >= 0; c = _from[c])
        {
            if (len == _reverse.Length)
                Array.Resize(ref _reverse, len * 2);
            _reverse[len++] = c;
        }
        var count = Math.Min(len, path.Length);
        for (var i = 0; i < count; i++)
            path[i] = _reverse[len - 1 - i];
        return count;
    }

    private static float H(NavGraph g, int a, int b) => Vector3.Distance(g.Nodes[a], g.Nodes[b]);

    private void Visit(int node, float gScore, int from)
    {
        _stamp[node] = _gen;
        _g[node] = gScore;
        _from[node] = from;
        _closed[node] = false;
    }

    private void Push(int node, float key)
    {
        if (_heapCount == _heapNode.Length)
        {
            Array.Resize(ref _heapNode, _heapCount * 2);
            Array.Resize(ref _heapKey, _heapCount * 2);
        }
        var i = _heapCount++;
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (_heapKey[parent] <= key)
                break;
            _heapNode[i] = _heapNode[parent];
            _heapKey[i] = _heapKey[parent];
            i = parent;
        }
        _heapNode[i] = node;
        _heapKey[i] = key;
    }

    private int Pop()
    {
        var top = _heapNode[0];
        var lastNode = _heapNode[--_heapCount];
        var lastKey = _heapKey[_heapCount];
        var i = 0;
        while (true)
        {
            var c = i * 2 + 1;
            if (c >= _heapCount)
                break;
            if (c + 1 < _heapCount && _heapKey[c + 1] < _heapKey[c])
                c++;
            if (_heapKey[c] >= lastKey)
                break;
            _heapNode[i] = _heapNode[c];
            _heapKey[i] = _heapKey[c];
            i = c;
        }
        if (_heapCount > 0)
        {
            _heapNode[i] = lastNode;
            _heapKey[i] = lastKey;
        }
        return top;
    }
}
