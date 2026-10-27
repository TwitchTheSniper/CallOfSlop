using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Bots and map logic doors (GameLogic.cs): the nav graph is built from the map as built, with every logic door shut, and
/// each door's nav link (MapLogic.NavGates, also a declared link in Map.NavLinks) joins its two sides without a walk. While
/// a door is shut its link's edges are switched off (<see cref="NavGraph.EdgeOff"/>, which A* and the horde's flow fields
/// already skip), and on while it is open. So the graph itself never changes and "Nav determinism" builds the same one; only
/// the switches follow the doors. A graph that was swapped, or whose switches were reset (destruction's patches), gets them
/// again on the next tick.
/// </summary>
public sealed partial class Game
{
    private NavGraph? _navGateGraph;
    private bool[]? _navGateEdgeOff;
    private int[][] _navGateEdges = [];
    private bool[] _navGateOpen = [];

    /// <summary>Server, each logic tick: the doors' nav links on or off as the doors are.</summary>
    private void SyncNavGates(LogicState s)
    {
        var gates = s.Def.NavGates;
        if (gates.Count == 0)
            return;
        var g = NavGraph.Current;
        if (g == NavGraph.Empty || g.MapName != s.Map.Name)
            return;
        var force = false;
        if (!ReferenceEquals(g, _navGateGraph))
        {
            _navGateGraph = g;
            _navGateEdges = FindGateEdges(g, gates);
            _navGateOpen = new bool[gates.Count];
            force = true;
        }
        if (g.EdgeOff.Length < g.EdgeTo.Length)
            g.EdgeOff = new bool[g.EdgeTo.Length]; // (once per graph: nothing was switched off yet)
        if (!ReferenceEquals(g.EdgeOff, _navGateEdgeOff))
        {
            _navGateEdgeOff = g.EdgeOff;
            force = true;
        }
        foreach (var d in s.Def.Doors)
        {
            var gate = s.Def.Entities[d].NavGate;
            if (gate < 0)
                continue;
            var open = s.Map.IsLogicDoorOpen(d);
            if (!force && open == _navGateOpen[gate])
                continue;
            _navGateOpen[gate] = open;
            foreach (var e in _navGateEdges[gate])
                g.EdgeOff[e] = !open;
        }
    }

    /// <summary>The edges of each gate's link: every edge between the nodes at its two ends (both ways).</summary>
    private static int[][] FindGateEdges(NavGraph g, List<(Vector3 A, Vector3 B)> gates)
    {
        var result = new int[gates.Count][];
        for (var k = 0; k < gates.Count; k++)
        {
            int a = Nearest(g, gates[k].A), b = Nearest(g, gates[k].B);
            var list = new List<int>();
            if (a >= 0 && b >= 0 && a != b)
            {
                for (var e = g.EdgeStart[a]; e < g.EdgeStart[a + 1]; e++)
                    if (g.EdgeTo[e] == b)
                        list.Add(e);
                for (var e = g.EdgeStart[b]; e < g.EdgeStart[b + 1]; e++)
                    if (g.EdgeTo[e] == a)
                        list.Add(e);
            }
            result[k] = list.ToArray();
        }
        return result;
    }

    private static int Nearest(NavGraph g, Vector3 p)
    {
        var best = -1;
        var bestD = 1.5f * 1.5f;
        var count = Math.Min(g.Nodes.Length, g.EdgeStart.Length - 1);
        for (var i = 0; i < count; i++)
        {
            var n = g.Nodes[i];
            if (MathF.Abs(n.Y - p.Y) > 0.8f)
                continue;
            var d = (n.X - p.X) * (n.X - p.X) + (n.Z - p.Z) * (n.Z - p.Z);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Self-test: how many edges each door's link has in the current graph, and whether they're on now.</summary>
    internal (int Edges, int On)[] SelfTestNavGates()
    {
        if (_logic is not { } s)
            return [];
        SyncNavGates(s);
        var g = NavGraph.Current;
        return _navGateEdges.Select(es => (es.Length, es.Count(e => g.EdgeOff.Length == 0 || !g.EdgeOff[e]))).ToArray();
    }
}
