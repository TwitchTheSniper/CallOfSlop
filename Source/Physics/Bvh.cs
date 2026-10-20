using System.Numerics;

namespace CallOfSlop;

/// <summary>Called for each item whose leaf a ray reaches. Returns the new closest distance (or <paramref name="best"/> unchanged).</summary>
public interface IBvhRayVisitor
{
    float Visit(int item, float best);
}

/// <summary>Called for each item whose leaf overlaps the query box. Return true to stop the search.</summary>
public interface IBvhOverlapVisitor
{
    bool Visit(int item);
}

/// <summary>
/// A bounding volume hierarchy over items given as axis-aligned bounds (started from the spike's AabbBvh): a binary tree,
/// median split on the longest axis, up to 4 items per leaf, flat arrays, no allocation per query.
///
/// <para>Deterministic: the build sorts by centre with the item index as a tie-break, so the same input always gives the
/// same tree, and queries hand items to visitors that decide by value (closest distance, lowest index on ties), so the
/// answers never depend on the order the tree is walked in.</para>
///
/// <para>Exact: node bounds are padded slightly and tested with the same slab rule as <see cref="Box.Raycast"/>, so a
/// node is never skipped when one of its items would be hit; the visitor does the exact item test.</para>
/// </summary>
public sealed class Bvh
{
    private struct Node
    {
        public Vector3 Min, Max;
        public int Start; // leaf: first slot in _order; inner: index of the left child (the right one follows it)
        public int Count; // leaf: number of items (> 0); inner: 0
    }

    private const int LeafSize = 4;
    private const float Pad = 1e-4f;

    private readonly Vector3[] _min, _max; // item bounds (padded)
    private readonly int[] _order; // items grouped by leaf
    private Node[] _nodes;
    private int _nodeCount;

    public int ItemCount => _order.Length;
    public int NodeCount => _nodeCount;

    /// <summary>Depth of the deepest leaf (the root is depth 1).</summary>
    public int Depth { get; private set; }

    public Bvh(ReadOnlySpan<Vector3> min, ReadOnlySpan<Vector3> max)
    {
        var n = min.Length;
        _min = new Vector3[n];
        _max = new Vector3[n];
        for (var i = 0; i < n; i++)
        {
            _min[i] = min[i] - new Vector3(Pad);
            _max[i] = max[i] + new Vector3(Pad);
        }
        _order = new int[n];
        for (var i = 0; i < n; i++)
            _order[i] = i;
        _nodes = new Node[Math.Max(1, 2 * n)];
        _nodeCount = 1;
        if (n == 0)
        {
            _nodes[0] = new Node { Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue), Start = 0, Count = 0 };
            Depth = 0;
            return;
        }
        Build(0, 0, n, 1);
    }

    private void Build(int node, int start, int count, int depth)
    {
        Depth = Math.Max(Depth, depth);
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var i = start; i < start + count; i++)
        {
            lo = Vector3.Min(lo, _min[_order[i]]);
            hi = Vector3.Max(hi, _max[_order[i]]);
        }
        if (count <= LeafSize)
        {
            _nodes[node] = new Node { Min = lo, Max = hi, Start = start, Count = count };
            return;
        }
        var ext = hi - lo;
        var axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
        var mins = _min;
        var maxs = _max;
        Array.Sort(_order, start, count, Comparer<int>.Create((a, b) =>
        {
            var ca = mins[a][axis] + maxs[a][axis];
            var cb = mins[b][axis] + maxs[b][axis];
            var c = ca.CompareTo(cb);
            return c != 0 ? c : a.CompareTo(b);
        }));
        var left = _nodeCount;
        _nodeCount += 2;
        _nodes[node] = new Node { Min = lo, Max = hi, Start = left, Count = 0 };
        var half = count / 2;
        Build(left, start, half, depth + 1);
        Build(left + 1, start + half, count - half, depth + 1);
    }

    /// <summary>
    /// New bounds for every item (same count, same tree): node bounds are recomputed bottom-up. For items that moved a little
    /// or shrank (a broken prop); the tree's shape stays, so queries stay exact but may get slower if items move far.
    /// </summary>
    public void Refit(ReadOnlySpan<Vector3> min, ReadOnlySpan<Vector3> max)
    {
        for (var i = 0; i < _min.Length; i++)
        {
            _min[i] = min[i] - new Vector3(Pad);
            _max[i] = max[i] + new Vector3(Pad);
        }
        // Children always come after their parent, so a reverse walk sees children first.
        for (var k = _nodeCount - 1; k >= 0; k--)
        {
            ref var n = ref _nodes[k];
            var lo = new Vector3(float.MaxValue);
            var hi = new Vector3(float.MinValue);
            if (n.Count > 0)
            {
                for (var i = n.Start; i < n.Start + n.Count; i++)
                {
                    lo = Vector3.Min(lo, _min[_order[i]]);
                    hi = Vector3.Max(hi, _max[_order[i]]);
                }
            }
            else if (_order.Length > 0)
            {
                lo = Vector3.Min(_nodes[n.Start].Min, _nodes[n.Start + 1].Min);
                hi = Vector3.Max(_nodes[n.Start].Max, _nodes[n.Start + 1].Max);
            }
            n.Min = lo;
            n.Max = hi;
        }
    }

    /// <summary>
    /// Walks the leaves a ray (unit direction) can reach before <paramref name="maxDist"/>, nearer subtrees first, handing
    /// each item to the visitor, which returns the closest distance so far. Returns the final closest distance.
    /// </summary>
    public float Raycast<T>(Vector3 origin, Vector3 dir, float maxDist, ref T visitor) where T : struct, IBvhRayVisitor
    {
        var best = maxDist;
        if (_order.Length == 0)
            return best;
        var inv = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z);
        var px = MathF.Abs(dir.X) < Geometry.ParallelEpsilon;
        var py = MathF.Abs(dir.Y) < Geometry.ParallelEpsilon;
        var pz = MathF.Abs(dir.Z) < Geometry.ParallelEpsilon;
        Span<int> stack = stackalloc int[96];
        var sp = 0;
        if (!(Enter(_nodes[0], origin, inv, px, py, pz, best) <= best))
            return best;
        stack[sp++] = 0;
        while (sp > 0)
        {
            ref readonly var n = ref _nodes[stack[--sp]];
            if (!(Enter(n, origin, inv, px, py, pz, best) <= best))
                continue;
            if (n.Count > 0)
            {
                for (var i = n.Start; i < n.Start + n.Count; i++)
                    best = visitor.Visit(_order[i], best);
                continue;
            }
            var l = n.Start;
            var tl = Enter(_nodes[l], origin, inv, px, py, pz, best);
            var tr = Enter(_nodes[l + 1], origin, inv, px, py, pz, best);
            // Push the farther child first so the nearer one is visited first (it may shrink best and prune the other).
            if (tl <= tr)
            {
                if (tr <= best)
                    stack[sp++] = l + 1;
                if (tl <= best)
                    stack[sp++] = l;
            }
            else
            {
                if (tl <= best)
                    stack[sp++] = l;
                if (tr <= best)
                    stack[sp++] = l + 1;
            }
        }
        return best;
    }

    /// <summary>Entry distance of the ray into a node's box, or +infinity if it misses it within <paramref name="limit"/>.</summary>
    private static float Enter(in Node n, Vector3 o, Vector3 inv, bool px, bool py, bool pz, float limit)
    {
        float tMin = 0, tMax = limit;
        if (!Slab(o.X, inv.X, px, n.Min.X, n.Max.X, ref tMin, ref tMax)
            || !Slab(o.Y, inv.Y, py, n.Min.Y, n.Max.Y, ref tMin, ref tMax)
            || !Slab(o.Z, inv.Z, pz, n.Min.Z, n.Max.Z, ref tMin, ref tMax))
            return float.PositiveInfinity;
        return tMin;
    }

    private static bool Slab(float o, float inv, bool parallel, float lo, float hi, ref float tMin, ref float tMax)
    {
        if (parallel)
            return o >= lo && o <= hi;
        var t1 = (lo - o) * inv;
        var t2 = (hi - o) * inv;
        if (t1 > t2)
            (t1, t2) = (t2, t1);
        tMin = MathF.Max(tMin, t1);
        tMax = MathF.Min(tMax, t2);
        return tMin <= tMax;
    }

    /// <summary>Hands every item whose leaf overlaps the box min..max (inclusive) to the visitor, until it returns true.</summary>
    public bool Overlap<T>(Vector3 min, Vector3 max, ref T visitor) where T : struct, IBvhOverlapVisitor
    {
        if (_order.Length == 0)
            return false;
        Span<int> stack = stackalloc int[96];
        var sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            ref readonly var n = ref _nodes[stack[--sp]];
            if (n.Min.X > max.X || n.Max.X < min.X || n.Min.Y > max.Y || n.Max.Y < min.Y || n.Min.Z > max.Z || n.Max.Z < min.Z)
                continue;
            if (n.Count > 0)
            {
                for (var i = n.Start; i < n.Start + n.Count; i++)
                {
                    var item = _order[i];
                    if (_min[item].X > max.X || _max[item].X < min.X || _min[item].Y > max.Y || _max[item].Y < min.Y || _min[item].Z > max.Z || _max[item].Z < min.Z)
                        continue;
                    if (visitor.Visit(item))
                        return true;
                }
                continue;
            }
            stack[sp++] = n.Start + 1;
            stack[sp++] = n.Start;
        }
        return false;
    }
}
