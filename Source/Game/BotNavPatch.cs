using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Runtime patches to a nav graph for destructible buildings (Buildings/README.md, "Destruction"; Game's
/// GameDestructionNav.cs drives them). The graph is built once from the map as built and cached; a match then patches it
/// as walls and floors go, a slice per tick, and the patches are undone at the next match:
/// <list type="bullet">
/// <item>Nodes whose floor fell are switched off (<see cref="NodeOff"/>); existing links across the changed area are walked
/// again and switched off where a player can't walk them any more (<see cref="EdgeOff"/>).</item>
/// <item>A hole blasted at floor level gets nodes on both sides (parked spare nodes, added at build time on destructible maps,
/// so the node count never changes and every per-node array stays valid) linked to everything they can walk to; the new
/// links are appended to the edge arrays (<see cref="ExtraHead"/> / <see cref="ExtraNext"/>), so A* finds the way through.</item>
/// </list>
/// Maps without destruction never get spares or patches (their graphs are exactly as before).
/// </summary>
public sealed partial class NavGraph
{
    /// <summary>Spare nodes a destructible map's graph keeps for holes (activated one per side of a hole, at most this many a match).</summary>
    public const int SpareNodeCount = 160;

    /// <summary>Where a spare node waits until a hole needs it: far outside every map, so no query ever finds it.</summary>
    public static readonly Vector3 SparePark = new(1e5f, -1000f, 1e5f);

    /// <summary>Edges as built: extra edges (opened by destruction) are [BuiltEdges .. EdgeTo.Length).</summary>
    public int BuiltEdges;

    /// <summary>The spare nodes are [SpareStart .. SpareStart + SpareCount); <see cref="SparesUsed"/> are active this match.</summary>
    public int SpareStart, SpareCount, SparesUsed;

    /// <summary>Per node: switched off (a parked spare, or a node whose floor fell). Empty: nothing ever off.</summary>
    public bool[] NodeOff = [];

    /// <summary>Per edge: switched off by destruction. Empty until the first patch.</summary>
    public bool[] EdgeOff = [];

    /// <summary>Extra edges out of each node: the first is ExtraHead[node] (-1: none), the next ExtraNext[edge - BuiltEdges].</summary>
    public int[] ExtraHead = [];

    public int[] ExtraNext = [];

    /// <summary>Goes up with every patch step that changed something (0: as built).</summary>
    public int PatchVersion;

    /// <summary>Who patched it this match (a game and its match number): another game or match undoes the patches first.</summary>
    public object? PatchOwner;

    public int PatchMatch = -1;

    /// <summary>The map's destruction state when the build started (<see cref="DestructionStamp"/>).</summary>
    public long DestructionAtStart;

    /// <summary>Was something broken while (or before) the graph was built? Then it holds holes that won't be there next match: build again.</summary>
    public bool Tainted;

    /// <summary>Nodes switched off, edges switched off and extra edges, by the patches this match (stats and the self-test).</summary>
    public int NodesOff, EdgesOff, EdgesAdded;

    private bool _cellsDirty;
    private Map? _cellMap;

    /// <summary>A stamp of a map's destruction state: changes whenever a chunk breaks or comes back (0 without destruction).</summary>
    public static long DestructionStamp(Map map) =>
        map.Destruction is { } d ? ((long)d.Version << 20) ^ d.Structures.Sum(s => (long)s.BrokenCount) + 1 : 0;

    /// <summary>Build: appends the parked spare nodes (no edges) after the packed graph.</summary>
    private void AddSpareNodes()
    {
        SpareStart = Nodes.Length;
        SpareCount = SpareNodeCount;
        var nodes = new Vector3[SpareStart + SpareCount];
        Array.Copy(Nodes, nodes, SpareStart);
        for (var i = SpareStart; i < nodes.Length; i++)
            nodes[i] = SparePark;
        Nodes = nodes;
        var start = new int[nodes.Length + 1];
        Array.Copy(EdgeStart, start, EdgeStart.Length);
        for (var i = EdgeStart.Length; i < start.Length; i++)
            start[i] = EdgeStart[^1];
        EdgeStart = start;
    }

    /// <summary>Build, last step: the runtime arrays (empty on maps without destruction, so nothing changes for them).</summary>
    private void FinishRuntime(Map map)
    {
        BuiltEdges = EdgeTo.Length;
        if (map.Destruction == null)
            return;
        NodeOff = new bool[Nodes.Length];
        for (var i = SpareStart; i < SpareStart + SpareCount; i++)
        {
            NodeOff[i] = true;
            Component[i] = -1;
        }
        MainNodes = MainNodes.Where(i => i < SpareStart || i >= SpareStart + SpareCount).ToArray();
        ExtraHead = new int[Nodes.Length];
        Array.Fill(ExtraHead, -1);
        _cellMap = map;
        Tainted = DestructionAtStart != DestructionStamp(map) || map.Destruction.Structures.Any(s => s.BrokenCount > 0);
    }

    /// <summary>Forgets the cached graph of <paramref name="map"/> (it's built again the next time bots need it).</summary>
    public static void Forget(Map map)
    {
        Cache.Remove((map.Name, map.ContentHash));
        if (ReferenceEquals(_lastMap, map))
        {
            _last = null;
            _lastMap = null;
        }
    }

    /// <summary>Undoes every patch: spares parked, nothing off, the extra edges gone. No-op on an unpatched graph.</summary>
    public void ResetRuntime()
    {
        if (PatchVersion == 0 || ExtraHead.Length == 0)
            return;
        for (var i = SpareStart; i < SpareStart + SpareCount; i++)
        {
            Nodes[i] = SparePark;
            Component[i] = -1;
            Openness[i] = 0;
        }
        Array.Clear(NodeOff);
        for (var i = SpareStart; i < SpareStart + SpareCount; i++)
            NodeOff[i] = true;
        SparesUsed = 0;
        if (EdgeTo.Length != BuiltEdges)
        {
            Array.Resize(ref EdgeTo, BuiltEdges);
            Array.Resize(ref EdgeCost, BuiltEdges);
            Array.Resize(ref EdgeFlags, BuiltEdges);
            Array.Resize(ref EdgeLedge, BuiltEdges);
        }
        EdgeOff = [];
        ExtraNext = [];
        Array.Fill(ExtraHead, -1);
        NodesOff = EdgesOff = EdgesAdded = 0;
        PatchVersion = 0;
        if (_cellMap != null)
            BuildCells(_cellMap);
    }

    /// <summary>
    /// One patch as small steps (yields after each walk): <paramref name="region"/> is where chunks broke or fell, and
    /// <paramref name="holes"/> the floor-level spots either side of holes in walls. The map must be <see cref="World.Map"/>.
    /// </summary>
    public IEnumerable<bool> PatchSteps(Map map, Box region, Vector3[] holes)
    {
        if (ExtraHead.Length == 0 || !ReferenceEquals(World.Map, map))
            yield break;
        var c = World.Physics;
        _cellMap = map;
        if (EdgeOff.Length < EdgeTo.Length)
            Array.Resize(ref EdgeOff, EdgeTo.Length);
        var near = new List<int>();
        var reach = MathF.Max(region.Half.X, region.Half.Z) + Spacing * 1.5f;
        Within(region.Center, reach, near);
        var lo = region.Min.Y - 1f;
        var hi = region.Max.Y + 1f;

        // 1. Nodes whose floor went.
        foreach (var i in near)
        {
            var p = Nodes[i];
            if (NodeOff[i] || p.Y < lo || p.Y > hi)
                continue;
            var y = Ground(c, p.X, p.Z, p.Y + 0.3f, 0.6f);
            if (!float.IsFinite(y) || MathF.Abs(y - p.Y) > 0.06f)
            {
                NodeOff[i] = true;
                NodesOff++;
                PatchVersion++;
            }
            yield return false;
        }

        // 2. Links across the changed area: walk them again; off where a player can't any more.
        var grown = region with { Half = region.Half + new Vector3(0.6f) };
        foreach (var i in near)
        {
            if (NodeOff[i] || Nodes[i].Y < lo || Nodes[i].Y > hi)
                continue;
            for (var e = EdgeStart[i]; e < EdgeStart[i + 1]; e++)
            {
                if (EdgeOff[e] || NodeOff[EdgeTo[e]] || !Crosses(grown, Nodes[i], Nodes[EdgeTo[e]]))
                    continue;
                Walk(c, Nodes[i], Nodes[EdgeTo[e]], out var fwd, out _, out _, out _, out _, out _, out _);
                if (!fwd)
                {
                    EdgeOff[e] = true;
                    EdgesOff++;
                    PatchVersion++;
                }
                yield return false;
            }
        }

        // 3. Holes: a node either side (parked spares come out), linked to everything around that a player can walk to.
        var added = new List<int>();
        foreach (var h in holes)
        {
            if (SparesUsed >= SpareCount)
                break;
            var y = Ground(c, h.X, h.Z, h.Y + 0.4f, 0.8f);
            if (!float.IsFinite(y))
                continue;
            if (Place(map, c, new Vector3(h.X, y, h.Z), 0.4f) is not { } spot || NearNode(spot, 0.7f))
                continue;
            var k = SpareStart + SparesUsed++;
            Nodes[k] = spot;
            NodeOff[k] = false;
            Openness[k] = OpennessAt(c, spot);
            added.Add(k);
            _cellsDirty = true;
            PatchVersion++;
            yield return false;
        }
        if (_cellsDirty)
        {
            BuildCells(map);
            _cellsDirty = false;
        }
        var links = new List<(int, int, float, byte, Vector3)>();
        var failed = new List<(int, int, float, byte, Vector3)>(); // (no last resorts for blasted holes: those are extra ways)
        foreach (var k in added)
        {
            near.Clear();
            Within(Nodes[k], Spacing * 2.1f, near);
            foreach (var j in near)
            {
                if (j == k || NodeOff[j] || (added.Contains(j) && j < k))
                    continue; // each pair of new nodes once
                if (TryLink(this, c, k, j, links, failed))
                    yield return false;
            }
        }
        AppendExtraEdges(links);
        yield return true;
    }

    /// <summary>Does the segment a-b pass through the box (sampled every 0.5 m)?</summary>
    private static bool Crosses(in Box b, Vector3 a, Vector3 z)
    {
        var steps = Math.Clamp((int)MathF.Ceiling(Vector3.Distance(a, z) / 0.5f), 1, 64);
        for (var k = 0; k <= steps; k++)
        {
            var p = Vector3.Lerp(a, z, k / (float)steps);
            if (p.X >= b.Min.X && p.X <= b.Max.X && p.Y >= b.Min.Y - 2f && p.Y <= b.Max.Y && p.Z >= b.Min.Z && p.Z <= b.Max.Z)
                return true;
        }
        return false;
    }

    private bool NearNode(Vector3 p, float within)
    {
        var near = new List<int>();
        Within(p, within, near);
        foreach (var i in near)
            if (!NodeOff[i] && MathF.Abs(Nodes[i].Y - p.Y) < 0.5f)
                return true;
        return false;
    }

    /// <summary>Appends new links as extra edges (and gives a new node the component of what it joined).</summary>
    private void AppendExtraEdges(List<(int From, int To, float Cost, byte Flags, Vector3 Ledge)> links)
    {
        if (links.Count == 0)
            return;
        var at = EdgeTo.Length;
        var total = at + links.Count;
        Array.Resize(ref EdgeTo, total);
        Array.Resize(ref EdgeCost, total);
        Array.Resize(ref EdgeFlags, total);
        Array.Resize(ref EdgeLedge, total);
        Array.Resize(ref EdgeOff, total);
        Array.Resize(ref ExtraNext, total - BuiltEdges);
        foreach (var (from, to, cost, flags, ledge) in links)
        {
            EdgeTo[at] = to;
            EdgeCost[at] = cost;
            EdgeFlags[at] = flags;
            EdgeLedge[at] = ledge;
            ExtraNext[at - BuiltEdges] = ExtraHead[from];
            ExtraHead[from] = at;
            if (Component[from] < 0 && Component[to] >= 0)
                Component[from] = Component[to];
            if (Component[to] < 0 && Component[from] >= 0)
                Component[to] = Component[from];
            at++;
        }
        EdgesAdded += links.Count;
        PatchVersion++;
    }

    /// <summary>Every edge out of <paramref name="node"/> that's on (built and extra), for tools and the self-test.</summary>
    public IEnumerable<int> LiveEdges(int node)
    {
        for (var e = EdgeStart[node]; e < EdgeStart[node + 1]; e++)
            if ((EdgeOff.Length == 0 || !EdgeOff[e]) && (NodeOff.Length == 0 || !NodeOff[EdgeTo[e]]))
                yield return e;
        for (var e = ExtraHead.Length != 0 ? ExtraHead[node] : -1; e >= 0; e = ExtraNext[e - BuiltEdges])
            if ((EdgeOff.Length == 0 || !EdgeOff[e]) && !NodeOff[EdgeTo[e]])
                yield return e;
    }
}
