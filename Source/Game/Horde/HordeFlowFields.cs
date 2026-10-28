using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Shared routes for a horde (docs/zombies/ZOMBIES-MODE.md, "Horde controller"): a flow field per goal node of the
/// bots' nav graph (<see cref="NavGraph"/>), the cost of the way to the goal from every node and the next node on it,
/// worked out once with a Dijkstra over the reversed edges and read by every member heading there (one lookup instead
/// of an A* search per member). A small cache (least recently used goes), at most <see cref="BuildsPerTick"/> new
/// fields a tick (a member whose field isn't ready yet steers straight meanwhile); fields older than
/// <see cref="RefreshTicks"/> are built again when next asked for (doors and destruction change the graph). Edges
/// destruction took away are left out; the extra edges it opened aren't used (members fall back on straight steering
/// and their stuck watch there). No allocation once the arrays have grown for the map's graph.
/// </summary>
public sealed class HordeFlowFields
{
    /// <summary>One field: the goal node, the cost to it and the next node from every node (-1: none / unreachable).</summary>
    public sealed class Field
    {
        public int Goal = -1;
        public float[] Cost = [];
        public int[] Next = [];
        public long BuiltTick = long.MinValue, UsedTick;
    }

    public int Capacity = 24, BuildsPerTick = 1;
    public long RefreshTicks = 60;

    private Field[] _fields = [];
    private NavGraph? _graph;
    private int[] _revStart = [0];
    private int[] _revFrom = [];
    private float[] _revCost = [];
    private int[] _revEdge = [];
    private int[] _heapNode = new int[256];
    private float[] _heapKey = new float[256];
    private int _heapCount;
    private int _builtThisTick;
    private long _tick;

    /// <summary>Fields built in all, ms spent building them, and requests that had to wait for the budget (log, self-test).</summary>
    public int Built, Deferred;

    public double BuildMs;

    /// <summary>The graph the fields are for (a new graph drops every field).</summary>
    public NavGraph? Graph => _graph;

    /// <summary>Call once per tick before any <see cref="Get"/>: the graph in use and the tick (the build budget restarts).</summary>
    public void BeginTick(NavGraph g, long tick)
    {
        _tick = tick;
        _builtThisTick = 0;
        if (ReferenceEquals(g, _graph))
            return;
        _graph = g;
        if (_fields.Length != Capacity)
        {
            _fields = new Field[Capacity];
            for (var i = 0; i < _fields.Length; i++)
                _fields[i] = new Field();
        }
        foreach (var f in _fields)
        {
            f.Goal = -1;
            f.BuiltTick = long.MinValue;
            if (f.Cost.Length < g.Nodes.Length)
            {
                f.Cost = new float[g.Nodes.Length];
                f.Next = new int[g.Nodes.Length];
            }
        }
        BuildReverse(g);
        if (_heapNode.Length < g.Nodes.Length)
        {
            // The open list sized for the graph once (a build's heap can hold every node), so a field built mid-round
            // never grows it (a 4 KB allocation inside a tick: the "Zombies cathedral" siege check).
            _heapNode = new int[g.Nodes.Length];
            _heapKey = new float[g.Nodes.Length];
        }
    }

    /// <summary>Forget every field (a new run; the graph is kept).</summary>
    public void Clear()
    {
        foreach (var f in _fields)
        {
            f.Goal = -1;
            f.BuiltTick = long.MinValue;
        }
    }

    private void BuildReverse(NavGraph g)
    {
        var n = g.Nodes.Length;
        var built = Math.Min(g.EdgeStart.Length > 0 ? g.EdgeStart[^1] : 0, g.EdgeTo.Length);
        if (_revStart.Length < n + 1)
            _revStart = new int[n + 1];
        Array.Clear(_revStart, 0, n + 1);
        if (_revFrom.Length < built)
        {
            _revFrom = new int[built];
            _revCost = new float[built];
            _revEdge = new int[built];
        }
        for (var u = 0; u < n; u++)
            for (var e = g.EdgeStart[u]; e < g.EdgeStart[u + 1] && e < built; e++)
                _revStart[g.EdgeTo[e] + 1]++;
        for (var v = 0; v < n; v++)
            _revStart[v + 1] += _revStart[v];
        var fill = new int[n]; // once per graph
        Array.Copy(_revStart, fill, n);
        for (var u = 0; u < n; u++)
            for (var e = g.EdgeStart[u]; e < g.EdgeStart[u + 1] && e < built; e++)
            {
                var v = g.EdgeTo[e];
                var k = fill[v]++;
                _revFrom[k] = u;
                _revCost[k] = g.EdgeCost[e];
                _revEdge[k] = e;
            }
    }

    /// <summary>
    /// The field towards <paramref name="goal"/> (a node of the current graph), built now if the tick's budget allows,
    /// else the stale one if there is one, else null (steer straight this time).
    /// </summary>
    public Field? Get(int goal)
    {
        var g = _graph;
        if (g == null || goal < 0 || goal >= g.Nodes.Length || _fields.Length == 0)
            return null;
        Field? have = null, oldest = null;
        foreach (var f in _fields)
        {
            if (f.Goal == goal)
            {
                have = f;
                break;
            }
            if (oldest == null || f.UsedTick < oldest.UsedTick)
                oldest = f;
        }
        if (have != null)
        {
            have.UsedTick = _tick;
            if (_tick - have.BuiltTick > RefreshTicks && _builtThisTick < BuildsPerTick)
                Build(have, goal);
            return have;
        }
        if (_builtThisTick >= BuildsPerTick)
        {
            Deferred++;
            return null;
        }
        Build(oldest!, goal);
        oldest!.UsedTick = _tick;
        return oldest;
    }

    /// <summary>Is a field for <paramref name="goal"/> cached (no build)?</summary>
    public Field? Peek(int goal)
    {
        foreach (var f in _fields)
            if (f.Goal == goal)
                return f;
        return null;
    }

    private void Build(Field f, int goal)
    {
        var t0 = Stopwatch.GetTimestamp();
        _builtThisTick++;
        Built++;
        var g = _graph!;
        var n = g.Nodes.Length;
        if (f.Cost.Length < n)
        {
            f.Cost = new float[n];
            f.Next = new int[n];
        }
        var cost = f.Cost;
        var next = f.Next;
        Array.Fill(cost, float.PositiveInfinity, 0, n);
        Array.Fill(next, -1, 0, n);
        var nodeOff = g.NodeOff;
        var edgeOff = g.EdgeOff;
        cost[goal] = 0;
        _heapCount = 0;
        Push(goal, 0);
        while (_heapCount > 0)
        {
            var (v, key) = Pop();
            if (key > cost[v])
                continue;
            for (var k = _revStart[v]; k < _revStart[v + 1]; k++)
            {
                var u = _revFrom[k];
                if ((nodeOff.Length != 0 && nodeOff[u]) || (edgeOff.Length != 0 && edgeOff[_revEdge[k]]))
                    continue;
                var c = key + _revCost[k];
                if (c >= cost[u])
                    continue;
                cost[u] = c;
                next[u] = v;
                Push(u, c);
            }
        }
        f.Goal = goal;
        f.BuiltTick = _tick;
        BuildMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    private void Push(int node, float key)
    {
        if (_heapCount == _heapNode.Length)
        {
            Array.Resize(ref _heapNode, _heapNode.Length * 2);
            Array.Resize(ref _heapKey, _heapKey.Length * 2);
        }
        var i = _heapCount++;
        while (i > 0)
        {
            var p = (i - 1) >> 1;
            if (_heapKey[p] <= key)
                break;
            _heapNode[i] = _heapNode[p];
            _heapKey[i] = _heapKey[p];
            i = p;
        }
        _heapNode[i] = node;
        _heapKey[i] = key;
    }

    private (int Node, float Key) Pop()
    {
        var top = (_heapNode[0], _heapKey[0]);
        var last = --_heapCount;
        if (last > 0)
        {
            var node = _heapNode[last];
            var key = _heapKey[last];
            var i = 0;
            while (true)
            {
                var l = i * 2 + 1;
                if (l >= last)
                    break;
                var r = l + 1;
                var m = r < last && _heapKey[r] < _heapKey[l] ? r : l;
                if (_heapKey[m] >= key)
                    break;
                _heapNode[i] = _heapNode[m];
                _heapKey[i] = _heapKey[m];
                i = m;
            }
            _heapNode[i] = node;
            _heapKey[i] = key;
        }
        return top;
    }

    /// <summary>The cost from node <paramref name="from"/> to the field's goal (infinity: no way).</summary>
    public static float CostFrom(Field f, int from) => from >= 0 && from < f.Cost.Length ? f.Cost[from] : float.PositiveInfinity;
}
