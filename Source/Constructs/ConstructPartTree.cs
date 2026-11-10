using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A tree over a construct's parts (the capital parts, docs/sandbox/CONSTRUCTS.md, "Size classes"), so a ray, a player's
/// blocking or a placement test looks at the parts near it, not at all 4,000 of them. Built when first asked for after a
/// rebuild, in the body's rest frame, from the rigid parts; a part on a moving joint is always handed back (there are few,
/// and they move), and so is one added since the tree was built. A construct of fewer than <see cref="TreeMinParts"/> parts
/// walks the list as it always did, in part order. No query allocates; a build allocates only when the construct has grown.
/// </summary>
public sealed partial class Construct
{
    public const int TreeMinParts = 32;
    private const int TreeLeaf = 4, TreeTail = 256;

    private int[] _treeOrder = [], _treeLoose = [];
    private Vector3[] _treeMin = [], _treeMax = [], _partMin = [], _partMax = [];
    private int[] _treeLeft = [], _treeStart = [], _treeCount = []; // (a leaf: _treeLeft -1, its parts are _treeOrder[start .. start + count])
    private int _treeNodes, _treeLooseCount, _treeParts = -1;
    private int _treeOrderCount, _treeDead; // (entries of _treeOrder in the tree; of them, parts gone since: -1, ConstructRebuild.cs)

    /// <summary>The parts' bounds or joints changed: the tree is built again when next asked for.</summary>
    internal void TreeStale() => _treeParts = -1;

    private bool TreeUsable()
    {
        if (PartCount < TreeMinParts)
            return false;
        if (_treeParts < 0 || PartCount < _treeParts || PartCount - _treeParts > TreeTail)
            TreeBuild(); // (parts added since the last build are handed back as they are: a hull being built isn't built again each part)
        return true;
    }

    private void TreeBuild()
    {
        var n = PartCount;
        if (_treeOrder.Length < n)
        {
            var cap = Math.Max(n, _treeOrder.Length * 2);
            _treeOrder = new int[cap];
            _treeLoose = new int[cap];
            _partMin = new Vector3[cap];
            _partMax = new Vector3[cap];
            _treeMin = new Vector3[cap * 2];
            _treeMax = new Vector3[cap * 2];
            _treeLeft = new int[cap * 2];
            _treeStart = new int[cap * 2];
            _treeCount = new int[cap * 2];
        }
        var rigid = 0;
        _treeLooseCount = 0;
        for (var i = 0; i < n; i++)
        {
            if (!Rigid(Parts[i].Seg) || Parts[i].D is { IsExtrusion: true, ExKey.Growing: true }) // (a piece on a die grows without a rebuild: RefreshShape)
            {
                _treeLoose[_treeLooseCount++] = i;
                continue;
            }
            RestBounds(i, out var mn, out var mx);
            if (MergeOf[i] != 0 || Parts[i].D.IsEnvelope)
            {
                ShapeBounds(i, out var smn, out var smx); // (a merged receiver's one box reaches past its members, a lift cell's envelope past its block)
                mn = Vector3.Min(mn, smn);
                mx = Vector3.Max(mx, smx);
            }
            _partMin[i] = mn;
            _partMax[i] = mx;
            _treeOrder[rigid++] = i;
        }
        _treeNodes = 0;
        if (rigid > 0)
            TreeNode(0, rigid);
        _treeParts = n;
        _treeOrderCount = rigid;
        _treeDead = 0;
        _treeSerial = _editSerial;
    }

    private int TreeNode(int start, int count)
    {
        var node = _treeNodes++;
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        for (var k = start; k < start + count; k++)
        {
            var i = _treeOrder[k];
            mn = Vector3.Min(mn, _partMin[i]);
            mx = Vector3.Max(mx, _partMax[i]);
        }
        _treeMin[node] = mn;
        _treeMax[node] = mx;
        _treeStart[node] = start;
        _treeCount[node] = count;
        _treeLeft[node] = -1;
        if (count <= TreeLeaf)
            return node;
        var size = mx - mn;
        var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
        TreeSelect(start, start + count - 1, start + count / 2, axis);
        var left = TreeNode(start, count / 2);
        _ = TreeNode(start + count / 2, count - count / 2);
        _treeLeft[node] = left; // (the right child follows the left's subtree: kept in _treeCount's split, see below)
        _treeStart[node] = _rightOf(left, count / 2);
        return node;
    }

    // A node's right child sits right after its left child's subtree, whose size follows from its count: 2 * leaves - 1 is not
    // fixed (leaves hold up to TreeLeaf), so the build stores it: an inner node's _treeStart holds its right child.
    private int _rightOf(int left, int leftCount)
    {
        _ = leftCount;
        return _treeLastEnd(left);
    }

    private int _treeLastEnd(int node)
    {
        if (_treeLeft[node] < 0)
            return node + 1;
        return _treeLastEnd(_treeStart[node]);
    }

    /// <summary>
    /// Part <paramref name="part"/> moved (an event door settling: Construct.Doors.cs): its bounds are now
    /// <paramref name="min"/>..<paramref name="max"/>, and the nodes over it, found by its old bounds, are refitted. The tree keeps
    /// its shape. A part handed back loose needs nothing. False when it wasn't found where it was (build the tree again).
    /// </summary>
    internal bool TreeMove(int part, Vector3 oldMin, Vector3 oldMax, Vector3 min, Vector3 max)
    {
        if (_treeParts < 0 || part >= _treeParts || _treeNodes == 0)
            return true; // (no tree yet, or the part came after it: handed back loose)
        for (var k = 0; k < _treeLooseCount; k++)
            if (_treeLoose[k] == part)
                return true;
        _partMin[part] = min;
        _partMax[part] = max;
        return TreeRefit(0, part, oldMin, oldMax);
    }

    private bool TreeRefit(int node, int part, Vector3 qMin, Vector3 qMax)
    {
        var a = _treeMin[node];
        var b = _treeMax[node];
        if (a.X > qMax.X || b.X < qMin.X || a.Y > qMax.Y || b.Y < qMin.Y || a.Z > qMax.Z || b.Z < qMin.Z)
            return false;
        Vector3 mn, mx;
        if (_treeLeft[node] >= 0)
        {
            int left = _treeLeft[node], right = _treeStart[node];
            if (!TreeRefit(left, part, qMin, qMax) && !TreeRefit(right, part, qMin, qMax))
                return false;
            mn = Vector3.Min(_treeMin[left], _treeMin[right]);
            mx = Vector3.Max(_treeMax[left], _treeMax[right]);
        }
        else
        {
            var found = false;
            mn = new Vector3(float.MaxValue);
            mx = new Vector3(float.MinValue);
            for (int k = _treeStart[node], e = k + _treeCount[node]; k < e; k++)
            {
                var i = _treeOrder[k];
                if (i < 0)
                    continue;
                found |= i == part;
                mn = Vector3.Min(mn, _partMin[i]);
                mx = Vector3.Max(mx, _partMax[i]);
            }
            if (!found)
                return false;
        }
        _treeMin[node] = mn; // (exact: the node's box from its children as they are now)
        _treeMax[node] = mx;
        return true;
    }

    private float TreeKey(int part, int axis) => _partMin[part][axis] + _partMax[part][axis];

    private void TreeSelect(int lo, int hi, int nth, int axis)
    {
        while (lo < hi)
        {
            var pivot = TreeKey(_treeOrder[(lo + hi) / 2], axis);
            int a = lo, b = hi;
            while (a <= b)
            {
                while (TreeKey(_treeOrder[a], axis) < pivot)
                    a++;
                while (TreeKey(_treeOrder[b], axis) > pivot)
                    b--;
                if (a <= b)
                {
                    (_treeOrder[a], _treeOrder[b]) = (_treeOrder[b], _treeOrder[a]);
                    a++;
                    b--;
                }
            }
            if (nth <= b)
                hi = b;
            else if (nth >= a)
                lo = a;
            else
                return;
        }
    }

    /// <summary>A scratch list for one query at a time on the game thread, for code with no list of its own.</summary>
    internal static readonly int[] PartScratch = new int[MaxParts];

    private int TreeAll(int[] into)
    {
        for (var i = 0; i < PartCount; i++)
            into[i] = i;
        return PartCount;
    }

    /// <summary>The parts that may meet a box in the rest frame (all of them on a small construct, in part order). Returns how many went into <paramref name="into"/> (<see cref="MaxParts"/> long).</summary>
    public int PartsInBox(Vector3 min, Vector3 max, int[] into)
    {
        if (!TreeUsable())
            return TreeAll(into);
        var n = 0;
        Span<int> stack = stackalloc int[64];
        var sp = 0;
        if (_treeNodes > 0)
            stack[sp++] = 0;
        while (sp > 0)
        {
            var node = stack[--sp];
            var a = _treeMin[node];
            var b = _treeMax[node];
            if (a.X > max.X || b.X < min.X || a.Y > max.Y || b.Y < min.Y || a.Z > max.Z || b.Z < min.Z)
                continue;
            if (_treeLeft[node] >= 0)
            {
                stack[sp++] = _treeStart[node];
                stack[sp++] = _treeLeft[node];
                continue;
            }
            for (int k = _treeStart[node], e = k + _treeCount[node]; k < e; k++)
            {
                var i = _treeOrder[k];
                if (i < 0)
                    continue; // (a part that went: ConstructRebuild.cs)
                var pa = _partMin[i];
                var pb = _partMax[i];
                if (pa.X <= max.X && pb.X >= min.X && pa.Y <= max.Y && pb.Y >= min.Y && pa.Z <= max.Z && pb.Z >= min.Z)
                    into[n++] = i;
            }
        }
        for (var k = 0; k < _treeLooseCount; k++)
            into[n++] = _treeLoose[k];
        for (var i = _treeParts; i < PartCount; i++)
            into[n++] = i;
        return n;
    }

    /// <summary>The parts a ray in the rest frame may meet within <paramref name="maxDist"/> (see <see cref="PartsInBox"/>).</summary>
    public int PartsOnRay(Vector3 origin, Vector3 dir, float maxDist, int[] into)
    {
        if (!TreeUsable())
            return TreeAll(into);
        var n = 0;
        Span<int> stack = stackalloc int[64];
        var sp = 0;
        if (_treeNodes > 0)
            stack[sp++] = 0;
        var inv = new Vector3(1f / dir.X, 1f / dir.Y, 1f / dir.Z); // (an axis of 0 gives an infinity, which the slab test handles)
        while (sp > 0)
        {
            var node = stack[--sp];
            if (!Slab(_treeMin[node], _treeMax[node], origin, inv, maxDist))
                continue;
            if (_treeLeft[node] >= 0)
            {
                stack[sp++] = _treeStart[node];
                stack[sp++] = _treeLeft[node];
                continue;
            }
            for (int k = _treeStart[node], e = k + _treeCount[node]; k < e; k++)
            {
                var i = _treeOrder[k];
                if (i >= 0 && Slab(_partMin[i], _partMax[i], origin, inv, maxDist))
                    into[n++] = i;
            }
        }
        for (var k = 0; k < _treeLooseCount; k++)
            into[n++] = _treeLoose[k];
        for (var i = _treeParts; i < PartCount; i++)
            into[n++] = i;
        return n;
    }

    private static bool Slab(Vector3 min, Vector3 max, Vector3 o, Vector3 inv, float far)
    {
        var a = (min - o) * inv;
        var b = (max - o) * inv;
        var lo = Vector3.Min(a, b);
        var hi = Vector3.Max(a, b);
        // (a ray in a slab's plane gives NaN here: such a part is kept, not missed)
        var t0 = MathF.Max(MathF.Max(float.IsNaN(lo.X) ? 0 : lo.X, float.IsNaN(lo.Y) ? 0 : lo.Y), float.IsNaN(lo.Z) ? 0 : lo.Z);
        var t1 = MathF.Min(MathF.Min(float.IsNaN(hi.X) ? far : hi.X, float.IsNaN(hi.Y) ? far : hi.Y), float.IsNaN(hi.Z) ? far : hi.Z);
        return t1 >= MathF.Max(t0, 0f) && t0 <= far;
    }
}
