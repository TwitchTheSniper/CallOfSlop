// The game project (CallOfSlop.csproj) compiles every .cs under the repo, so the spike's code only exists
// when JOLT_SPIKE is defined (JoltSpike.csproj defines it).
#if JOLT_SPIKE
using Brutal.Numerics;

namespace JoltSpike;

/// <summary>
/// The fallback "BvhWorldQuery" from the design doc, boxes only: a binary AABB tree (median split on the
/// longest axis, 4 boxes per leaf) with a closest-hit ray query. Flat arrays, no allocation per query.
/// </summary>
public sealed class AabbBvh
{
    struct Node
    {
        public float3 Min, Max;
        public int Left, Right; // child indices; Left = -1 for a leaf
        public int Start, Count; // leaf: range in _order
    }

    readonly (float3 Min, float3 Max)[] _boxes;
    readonly int[] _order;
    readonly List<Node> _nodes = new();
    readonly int[] _stack = new int[64];

    public int NodeCount => _nodes.Count;

    public AabbBvh(IReadOnlyList<(float3 Min, float3 Max)> boxes)
    {
        _boxes = boxes.ToArray();
        _order = Enumerable.Range(0, _boxes.Length).ToArray();
        Build(0, _order.Length);
    }

    int Build(int start, int count)
    {
        var min = new float3(float.MaxValue);
        var max = new float3(float.MinValue);
        for (var i = start; i < start + count; i++)
        {
            min = float3.Min(min, _boxes[_order[i]].Min);
            max = float3.Max(max, _boxes[_order[i]].Max);
        }
        var index = _nodes.Count;
        _nodes.Add(new Node { Min = min, Max = max, Left = -1, Start = start, Count = count });
        if (count <= 4)
            return index;
        var ext = max - min;
        var axis = ext.X > ext.Y && ext.X > ext.Z ? 0 : ext.Y > ext.Z ? 1 : 2;
        float Center(int b) => axis == 0 ? _boxes[b].Min.X + _boxes[b].Max.X : axis == 1 ? _boxes[b].Min.Y + _boxes[b].Max.Y : _boxes[b].Min.Z + _boxes[b].Max.Z;
        Array.Sort(_order, start, count, Comparer<int>.Create((a, b) => Center(a).CompareTo(Center(b))));
        var half = count / 2;
        var left = Build(start, half);
        var right = Build(start + half, count - half);
        var n = _nodes[index];
        n.Left = left;
        n.Right = right;
        _nodes[index] = n;
        return index;
    }

    /// <summary>Closest hit distance along a unit ray, or maxDist on a miss (same answer as the brute-force loop).</summary>
    public float Raycast(float3 o, float3 d, float maxDist)
    {
        var inv = new float3(1f / d.X, 1f / d.Y, 1f / d.Z);
        var best = maxDist;
        var sp = 0;
        _stack[sp++] = 0;
        var nodes = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_nodes);
        while (sp > 0)
        {
            ref var n = ref nodes[_stack[--sp]];
            if (Slab(o, inv, n.Min, n.Max, best) >= best)
                continue;
            if (n.Left < 0)
            {
                for (var i = n.Start; i < n.Start + n.Count; i++)
                {
                    var (bmin, bmax) = _boxes[_order[i]];
                    var t = Slab(o, inv, bmin, bmax, best);
                    if (t < best)
                        best = t;
                }
                continue;
            }
            _stack[sp++] = n.Right;
            _stack[sp++] = n.Left;
        }
        return best;
    }

    /// <summary>Entry distance of a ray into a box, or +inf (or >= limit) if it misses.</summary>
    static float Slab(float3 o, float3 inv, float3 min, float3 max, float limit)
    {
        var t1 = (min - o) * inv;
        var t2 = (max - o) * inv;
        var lo = float3.Min(t1, t2);
        var hi = float3.Max(t1, t2);
        var tMin = MathF.Max(MathF.Max(lo.X, lo.Y), MathF.Max(lo.Z, 0));
        var tMax = MathF.Min(MathF.Min(hi.X, hi.Y), MathF.Min(hi.Z, limit));
        return tMin <= tMax ? tMin : float.PositiveInfinity;
    }
}
#endif
