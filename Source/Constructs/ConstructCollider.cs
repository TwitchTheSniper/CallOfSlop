using System.Numerics;

namespace CallOfSlop;

/// <summary>Where a frame's axes and origin are in the world (or in another frame): p => X * p.X + Y * p.Y + Z * p.Z + T.</summary>
internal struct CFrame
{
    public Vector3 X, Y, Z, T;

    public static CFrame Of(Quaternion q, Vector3 t)
    {
        var m = Matrix4x4.CreateFromQuaternion(q);
        return new CFrame { X = new Vector3(m.M11, m.M12, m.M13), Y = new Vector3(m.M21, m.M22, m.M23), Z = new Vector3(m.M31, m.M32, m.M33), T = t };
    }

    public readonly Vector3 Dir(Vector3 v) => X * v.X + Y * v.Y + Z * v.Z;
    public readonly Vector3 Point(Vector3 p) => X * p.X + Y * p.Y + Z * p.Z + T;
    public readonly Vector3 LocalDir(Vector3 v) => new(Vector3.Dot(v, X), Vector3.Dot(v, Y), Vector3.Dot(v, Z));
    public readonly Vector3 Local(Vector3 p) => LocalDir(p - T);

    /// <summary>The half size in this frame's axes of a box with half size <paramref name="h"/> and the axes of <paramref name="o"/> (given in this frame's terms).</summary>
    public static Vector3 Abs(in CFrame o, Vector3 h) => Vector3.Abs(o.X) * h.X + Vector3.Abs(o.Y) * h.Y + Vector3.Abs(o.Z) * h.Z;
}

/// <summary>A solid box or convex piece in the world: its centre, axes and half size.</summary>
internal struct WBox
{
    public Vector3 C, X, Y, Z, H;
    public int Hull; // the construct's hull index, -1 for a plain box
}

/// <summary>
/// A construct's collider (docs/sandbox/CONSTRUCTS.md, "Compound collision"): every solid box of every part, welded,
/// freeform and jointed pieces alike, in one bounding volume tree per rigid group (the parts one joint moves; the body's
/// own and its freeform welds are group 0). A group's tree is built in that group's frame, so a joint moving costs
/// nothing: the tree moves with the frame. Rebuilt only when the parts change, on first use, so a client never pays for it.
/// </summary>
internal sealed partial class ConstructCollider
{
    public struct Leaf
    {
        public Vector3 Centre, Half; // in its group's frame; turned: along AX, AY, AZ
        public Vector3 AX, AY, AZ;
        public ushort Part;
        public byte Seg;
        public bool Turned, Wheel; // a freeform weld's box; a raycast wheel (it stays out of the ground collision)
        public bool Stub; // a box of a wheel that has a cylinder: it stands for the part, it doesn't collide
        public bool Dead; // a hole a part that went left (ConstructRebuild.cs): in no node, skipped by the walks over a group
        public bool Scoop; // a box of a scoop: while its edge is in the ground the ground gives way to it (ConstructScoops.cs)
        public int Hull; // index in Hulls, -1: a plain box
        public int WheelIx; // this leaf is wheel WheelIx's cylinder, drawn wherever the hub is now (ConstructWheelShape.cs), and its bounds its whole travel; -1: no
    }

    public struct Node
    {
        public Vector3 Centre, Half;
        public int Start, Count; // inner (Count 0): the left child, the right after it; leaf: Leaves[Start..Start + Count]
    }

    public struct Group
    {
        public byte Seg; // the joint's segment (0: the body), Construct.MoveSeg of everything in it
        public int Root;
        public int First, Count;
    }

    private const int LeafSize = 2;

    public Leaf[] Leaves = new Leaf[1];
    public int LeafCount;
    public Node[] Nodes = new Node[1];
    public int NodeCount;
    public Group[] Groups = new Group[1]; // (grows with the joints: most bodies have none)
    public int GroupCount;
    public ConvexHull[] Hulls = new ConvexHull[0];
    private float[] _keys = [];
    private int[] _wheelOf = [];

    /// <summary>The boxes of the construct as it is now (rest pose; the joints' frames carry them after that).</summary>
    /// <summary>Colliders built (every body, every thread, roughly: a harness's count).</summary>
    internal static int Builds;

    public void Build(Construct c)
    {
        Builds++;
        _dead = 0;
        _mainLeaves = _mainNodes = 0;
        _extraGroup = -1;
        if (c.FuseFitDue)
            c.FuseFitNow(); // (fused units' rough shapes: fitted here, so only a body that collides pays for it; ConstructFuseFit.cs)
        var total = c.WheelCount;
        for (var u = 0; u < c.UnitCount; u++)
            total += c.Unit(u).RoughCount;
        var hulls = 0;
        if (_wheelOf.Length < c.PartCount)
            _wheelOf = new int[c.PartCount];
        Array.Fill(_wheelOf, -1, 0, c.PartCount);
        for (var k = c.WheelCount - 1; k >= 0; k--)
            _wheelOf[c.Wheels[k].Part] = k;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.FuseRough(i) || c.Rides(i))
                continue; // (inside its unit's rough shape; a rider is on a leg segment, not the body: ConstructWalkRiders.cs)
            total += Math.Max(1, c.SolidCount(i));
            if (c.MergeOf[i] == 0)
            {
                var hs = ConstructHulls.Of(c.Parts[i].D);
                hulls += hs.Length;
                total += hs.Length;
            }
        }
        if (Leaves.Length < total)
            Leaves = new Leaf[total];
        if (Hulls.Length < hulls)
            Array.Resize(ref Hulls, hulls);
        if (Groups.Length < c.JointCount + 1)
            Groups = new Group[c.JointCount + 1];
        LeafCount = 0;
        var hullAt = 0;
        GroupCount = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (c.FuseRough(i) || c.Rides(i))
                continue; // (its unit's rough boxes stand for it: below)
            ref var p = ref c.Parts[i];
            int seg = p.Seg;
            var gs = c.MoveSeg(seg);
            // The part's frame in its group's: every freeform weld between them.
            var rq = Quaternion.Identity;
            var rt = Vector3.Zero;
            var turned = seg != gs;
            for (int s = seg, guard = 0; s != gs && guard <= Construct.MaxJoints; guard++)
            {
                ref var j = ref c.Joints[s - 1];
                rt = j.FixPos + Vector3.Transform(rt, j.FixRot);
                rq = j.FixRot * rq;
                s = j.ParentSeg;
            }
            var wheel = _wheelOf[i] >= 0;
            var first = LeafCount;
            var boxes = c.SolidCount(i);
            var hs = c.MergeOf[i] == 0 ? ConstructHulls.Of(p.D) : [];
            var any = false;
            for (var b = 0; b < boxes; b++)
            {
                if (hs.Length > 0 && ConstructHulls.Covered(p.D, b))
                    continue;
                c.SolidBox(i, b, out var centre, out var half);
                Add(centre, half, rq, rt, turned, i, seg, wheel, -1);
                any = true;
            }
            for (var h = 0; h < hs.Length; h++)
            {
                ReuseHull(hullAt, hs[h]);
                var hull = hs[h].MovedInto(Hulls[hullAt], rq * Rot24.Quat(p.Rot), rt + Vector3.Transform(p.Pos, rq), c.ScaleOf(i));
                hull.Bounds(out var mn, out var mx);
                Hulls[hullAt] = hull;
                Add((mn + mx) * 0.5f, (mx - mn) * 0.5f, Quaternion.Identity, Vector3.Zero, false, i, seg, wheel, hullAt++);
                any = true;
            }
            if (!any && c.MergeOf[i] == 0)
            {
                // (no solid box at all: its bounds stand for it)
                c.PartBounds(i, out var mn, out var mx);
                Add((mn + mx) * 0.5f, (mx - mn) * 0.5f, rq, rt, turned, i, seg, wheel, -1);
            }
            if (p.D.IsScoop)
                for (var l = first; l < LeafCount; l++)
                    Leaves[l].Scoop = true;
            if (wheel && !c.Wheels[_wheelOf[i]].Track)
            {
                for (var l = first; l < LeafCount; l++)
                    Leaves[l].Stub = true;
                AddCylinder(c, _wheelOf[i], i, seg);
            }
        }
        AddRough(c);
        // Group by joint, in segment order, and build each group's tree.
        var nodesNeeded = Math.Max(1, LeafCount * 2);
        if (Nodes.Length < nodesNeeded)
            Nodes = new Node[nodesNeeded];
        if (_keys.Length < LeafCount)
            _keys = new float[LeafCount];
        if (LeafCount >= FastMin)
        {
            ReserveExtra(c);
            BuildFast(c);
            return;
        }
        for (var k = 0; k < LeafCount; k++)
            _keys[k] = c.MoveSeg(Leaves[k].Seg);
        Array.Sort(_keys, Leaves, 0, LeafCount);
        NodeCount = 0;
        for (var at = 0; at < LeafCount;)
        {
            var gs = c.MoveSeg(Leaves[at].Seg);
            var end = at + 1;
            while (end < LeafCount && c.MoveSeg(Leaves[end].Seg) == gs)
                end++;
            ref var g = ref Groups[GroupCount++];
            g.Seg = (byte)gs;
            g.First = at;
            g.Count = end - at;
            g.Root = NodeCount++;
            BuildNode(g.Root, at, end - at);
            at = end;
        }
    }

    /// <summary>
    /// The hull kept in slot <paramref name="at"/> from the last build is reused when it has <paramref name="shape"/>'s faces; when a part came out
    /// before it, every later part's hulls sit a few slots on, so the matching one is looked for a little ahead and swapped in. Without this a part
    /// taken off a rack of 240 designed bombs (four hull shapes each) made nearly every later hull new: about a thousand hulls allocated a change.
    /// </summary>
    private void ReuseHull(int at, ConvexHull shape)
    {
        if (Hulls[at] is { } have && ReferenceEquals(have.FaceIdx, shape.FaceIdx))
            return;
        // (the last shift first: the parts after one taken out all moved by the same few slots)
        var k = at + _hullShift;
        if (_hullShift > 0 && k < Hulls.Length && Hulls[k] is { } next && ReferenceEquals(next.FaceIdx, shape.FaceIdx))
        {
            (Hulls[at], Hulls[k]) = (next, Hulls[at]);
            return;
        }
        var end = Math.Min(Hulls.Length, at + HullReuseAhead);
        for (k = at + 1; k < end; k++)
            if (Hulls[k] is { } cand && ReferenceEquals(cand.FaceIdx, shape.FaceIdx))
            {
                (Hulls[at], Hulls[k]) = (cand, Hulls[at]);
                _hullShift = k - at;
                return;
            }
    }

    private int _hullShift;

    private const int HullReuseAhead = 96; // slots looked through: a part's hulls, a few times over

    /// <summary>
    /// Leaves from which the trees are built from Morton order instead (docs/perf/LIMITS.md, "Rebuilds"): each group's
    /// leaf centres on a lattice of 1,024 cells along its longest side (cubes: a long hull is cut along its length), interleaved, radix sorted (no compare, so no branch a tree-ordered list can
    /// fool), then cut where the codes first differ all the way down (a lattice cell's boundary), the bounds made bottom-up: O(n), the leaves moved once. Below this
    /// the old sort is kept, so small bodies' trees (and the contacts they give) are what they were.
    /// </summary>
    internal const int FastMin = 2048;

    private int[] _order = [], _orderB = [];
    private uint[] _code = [], _codeB = [];
    private Leaf[] _spare = [];

    private void BuildFast(Construct c)
    {
        if (_order.Length < LeafCount)
        {
            var cap = Math.Max(LeafCount, _order.Length * 2);
            _order = new int[cap];
            _orderB = new int[cap];
            _code = new uint[cap];
            _codeB = new uint[cap];
        }
        // By group (a stable count by segment: the body's first, then each joint's in order).
        Span<int> start = stackalloc int[Construct.MaxJoints + 2];
        start.Clear();
        for (var k = 0; k < LeafCount; k++)
            start[c.MoveSeg(Leaves[k].Seg) + 1]++;
        for (var s = 1; s < start.Length; s++)
            start[s] += start[s - 1];
        Span<int> fill = stackalloc int[Construct.MaxJoints + 1];
        start[..fill.Length].CopyTo(fill);
        for (var k = 0; k < LeafCount; k++)
            _order[fill[c.MoveSeg(Leaves[k].Seg)]++] = k;
        NodeCount = 0;
        for (var s = 0; s <= Construct.MaxJoints; s++)
        {
            var count = start[s + 1] - start[s];
            if (count == 0)
                continue;
            MortonSort(start[s], count);
            ref var g = ref Groups[GroupCount++];
            g.Seg = (byte)s;
            g.First = start[s];
            g.Count = count;
            g.Root = NodeCount++;
        }
        // The leaves in tree order, moved once; then each group's tree over them.
        if (_spare.Length < Leaves.Length)
            _spare = new Leaf[Leaves.Length];
        for (var k = 0; k < LeafCount; k++)
            _spare[k] = Leaves[_order[k]];
        (Leaves, _spare) = (_spare, Leaves);
        for (var g = 0; g < GroupCount; g++)
            FastNode(Groups[g].Root, Groups[g].First, Groups[g].Count);
        _mainLeaves = LeafCount;
        _mainNodes = NodeCount;
        _extraGroup = -1;
    }

    // ---- Parts added by the journal (ConstructRebuild.cs): a small tree of their own beside the big one ----------------

    /// <summary>Leaves the added parts' own group may hold before the collider is built again.</summary>
    private const int ExtraMax = 512;

    private int _mainLeaves, _mainNodes, _extraGroup = -1;

    /// <summary>Room for <see cref="ExtraMax"/> more leaves, their nodes and their group, made with the big tree (never in a tick).</summary>
    private void ReserveExtra(Construct c)
    {
        var leaves = LeafCount + ExtraMax;
        if (Leaves.Length < leaves)
            Array.Resize(ref Leaves, leaves);
        if (Nodes.Length < leaves * 2)
            Nodes = new Node[leaves * 2];
        if (_keys.Length < leaves)
            _keys = new float[leaves];
        if (Groups.Length < c.JointCount + 2)
            Array.Resize(ref Groups, c.JointCount + 2);
    }

    /// <summary>
    /// Plain parts <paramref name="from"/> to <paramref name="to"/> (just added) into a group of the body's own segment
    /// with a small tree of its own (the big one stays as it is). False when there's no room for them, or a part has a
    /// convex hull: built again then.
    /// </summary>
    public bool AddParts(Construct c, int from, int to)
    {
        if (_mainLeaves < FastMin || _mainLeaves > LeafCount)
            return false;
        for (var i = from; i < to; i++)
            if (ConstructHulls.Of(c.Parts[i].D).Length > 0 || c.Parts[i].Seg != 0)
                return false;
        if (_extraGroup < 0 && (GroupCount >= Groups.Length || GroupCount > Construct.MaxJoints))
            return false;
        // The group's leaves so far, without the holes parts that went left.
        var at = _mainLeaves;
        for (var k = _mainLeaves; k < LeafCount; k++)
            if (!Leaves[k].Dead)
                Leaves[at++] = Leaves[k];
            else
                _dead--;
        LeafCount = at;
        for (var i = from; i < to; i++)
        {
            var boxes = c.SolidCount(i);
            if (LeafCount + Math.Max(1, boxes) > Math.Min(Leaves.Length, _mainLeaves + ExtraMax))
                return false;
            for (var b = 0; b < boxes; b++)
            {
                c.SolidBox(i, b, out var centre, out var half);
                Add(centre, half, Quaternion.Identity, Vector3.Zero, false, i, 0, false, -1);
            }
            if (boxes == 0)
            {
                c.PartBounds(i, out var mn, out var mx);
                Add((mn + mx) * 0.5f, (mx - mn) * 0.5f, Quaternion.Identity, Vector3.Zero, false, i, 0, false, -1);
            }
        }
        var count = LeafCount - _mainLeaves;
        if (_extraGroup < 0)
            _extraGroup = GroupCount++;
        NodeCount = _mainNodes;
        if (Nodes.Length < NodeCount + count * 2 + 1)
            return false;
        ref var g = ref Groups[_extraGroup];
        g.Seg = 0;
        g.First = _mainLeaves;
        g.Count = count;
        g.Root = NodeCount++;
        if (count == 0)
        {
            ref var n = ref Nodes[g.Root];
            n.Centre = Vector3.Zero;
            n.Half = new Vector3(-1e30f);
            n.Start = g.First;
            n.Count = 0;
            return false; // (an empty group's root would read as inner: built again)
        }
        BuildNode(g.Root, g.First, count);
        return true;
    }

    /// <summary>Sorts _order[start .. start + count] by the Morton code of each leaf's centre in the group's centre bounds (stable: ties stay in part order).</summary>
    private void MortonSort(int start, int count)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var k = start; k < start + count; k++)
        {
            var m = Leaves[_order[k]].Centre;
            lo = Vector3.Min(lo, m);
            hi = Vector3.Max(hi, m);
        }
        var ext = hi - lo;
        var scale = new Vector3(1023f / MathF.Max(MathF.Max(ext.X, ext.Y), MathF.Max(ext.Z, 1e-6f))); // (one scale for every axis: a long hull is cut along its length first)
        for (var k = start; k < start + count; k++)
        {
            var q = (Leaves[_order[k]].Centre - lo) * scale;
            _code[k] = Spread((uint)Math.Clamp((int)q.X, 0, 1023)) | Spread((uint)Math.Clamp((int)q.Y, 0, 1023)) << 1 | Spread((uint)Math.Clamp((int)q.Z, 0, 1023)) << 2;
        }
        Span<int> bucket = stackalloc int[257];
        for (var shift = 0; shift < 30; shift += 8)
        {
            bucket.Clear();
            for (var k = start; k < start + count; k++)
                bucket[(int)(_code[k] >> shift & 255) + 1]++;
            for (var b = 1; b < 257; b++)
                bucket[b] += bucket[b - 1];
            for (var k = start; k < start + count; k++)
            {
                var at = start + bucket[(int)(_code[k] >> shift & 255)]++;
                _codeB[at] = _code[k];
                _orderB[at] = _order[k];
            }
            Array.Copy(_codeB, start, _code, start, count);
            Array.Copy(_orderB, start, _order, start, count);
        }
    }

    /// <summary>Ten bits spread to every third bit (for a Morton code).</summary>
    private static uint Spread(uint v)
    {
        v = (v | v << 16) & 0x030000FF;
        v = (v | v << 8) & 0x0300F00F;
        v = (v | v << 4) & 0x030C30C3;
        v = (v | v << 2) & 0x09249249;
        return v;
    }

    /// <summary>A node over Leaves[start .. start + count] in Morton order: cut where the codes first differ, its bounds from its children's.</summary>
    private void FastNode(int node, int start, int count)
    {
        Vector3 lo, hi;
        if (count <= LeafSize)
        {
            lo = new Vector3(float.MaxValue);
            hi = new Vector3(float.MinValue);
            for (var i = start; i < start + count; i++)
            {
                ref var l = ref Leaves[i];
                var e = Extent(l);
                lo = Vector3.Min(lo, l.Centre - e);
                hi = Vector3.Max(hi, l.Centre + e);
            }
            ref var n = ref Nodes[node];
            n.Centre = (lo + hi) * 0.5f;
            n.Half = (hi - lo) * 0.5f;
            n.Start = start;
            n.Count = count;
            return;
        }
        // Split where the codes first differ (a cell boundary of the lattice: the two halves' boxes barely overlap), or in
        // the middle when they are all the same cell.
        var half = count / 2;
        var first = _code[start];
        var last = _code[start + count - 1];
        if (first != last)
        {
            var bit = 1u << (31 - System.Numerics.BitOperations.LeadingZeroCount(first ^ last));
            int a0 = start, a1 = start + count - 1; // (the first code with that bit set: the codes are sorted)
            while (a0 < a1)
            {
                var mid = (a0 + a1) >> 1;
                if ((_code[mid] & bit) != 0)
                    a1 = mid;
                else
                    a0 = mid + 1;
            }
            half = a0 - start;
        }
        var left = NodeCount;
        NodeCount += 2;
        FastNode(left, start, half);
        FastNode(left + 1, start + half, count - half);
        ref var a = ref Nodes[left];
        ref var b = ref Nodes[left + 1];
        lo = Vector3.Min(a.Centre - a.Half, b.Centre - b.Half);
        hi = Vector3.Max(a.Centre + a.Half, b.Centre + b.Half);
        ref var p = ref Nodes[node];
        p.Centre = (lo + hi) * 0.5f;
        p.Half = (hi - lo) * 0.5f;
        p.Start = left;
        p.Count = 0;
    }

    /// <summary>A wheel's cylinder: one leaf whose bounds hold it through its whole travel, so the tree isn't touched as the hub moves.</summary>
    private void AddCylinder(Construct c, int k, int part, int seg)
    {
        ref var w = ref c.Wheels[k];
        var a = Vector3.Abs(w.Axle);
        var round = w.Radius + w.HalfWidth * 0.5f * (w.ArmTilt > 0 ? 1 : 0);
        var size = a * w.HalfWidth + (Vector3.One - a) * round + (w.ArmTilt > 0 ? new Vector3(w.Radius * 0.25f) : Vector3.Zero);
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        for (var s = 0; s <= 4; s++)
        {
            var hub = c.HubAt(k, w.Travel * s / 4);
            lo = Vector3.Min(lo, hub - size);
            hi = Vector3.Max(hi, hub + size);
        }
        Add((lo + hi) * 0.5f, (hi - lo) * 0.5f, Quaternion.Identity, Vector3.Zero, false, part, seg, true, -1);
        Leaves[LeafCount - 1].WheelIx = k;
    }

    private void Add(Vector3 centre, Vector3 half, Quaternion rq, Vector3 rt, bool turned, int part, int seg, bool wheel, int hull)
    {
        ref var l = ref Leaves[LeafCount++];
        l = default;
        l.Part = (ushort)part;
        l.Seg = (byte)seg;
        l.Wheel = wheel;
        l.Hull = hull;
        l.WheelIx = -1;
        l.Turned = turned;
        l.Half = half;
        l.AX = Vector3.UnitX;
        l.AY = Vector3.UnitY;
        l.AZ = Vector3.UnitZ;
        l.Centre = centre;
        if (turned)
        {
            l.Centre = rt + Vector3.Transform(centre, rq);
            l.AX = Vector3.Transform(Vector3.UnitX, rq);
            l.AY = Vector3.Transform(Vector3.UnitY, rq);
            l.AZ = Vector3.Transform(Vector3.UnitZ, rq);
        }
    }

    /// <summary>
    /// Every fused unit's rough boxes (ConstructFuse.cs), in its segment's group as its parts' boxes would be, turned the same
    /// way for a freeform-welded unit. A rough leaf stands for its unit's first member: what a leaf's part decides (friction,
    /// no-collide pairs) is a plain part's, the same for every member.
    /// </summary>
    private void AddRough(Construct c)
    {
        for (var u = 0; u < c.UnitCount; u++)
        {
            var unit = c.Unit(u);
            if (unit.RoughCount == 0)
                continue;
            int seg = unit.Seg;
            var gs = c.MoveSeg(seg);
            var rq = Quaternion.Identity;
            var rt = Vector3.Zero;
            for (int s = seg, guard = 0; s != gs && guard <= Construct.MaxJoints; guard++)
            {
                ref var j = ref c.Joints[s - 1];
                rt = j.FixPos + Vector3.Transform(rt, j.FixRot);
                rq = j.FixRot * rq;
                s = j.ParentSeg;
            }
            var rep = -1;
            foreach (var i in c.UnitMembers(u))
                if (c.FuseCovers(i))
                {
                    rep = i;
                    break;
                }
            if (rep < 0)
                continue;
            for (var k = 0; k < unit.RoughCount; k++)
                Add((unit.RMin[k] + unit.RMax[k]) * 0.5f, (unit.RMax[k] - unit.RMin[k]) * 0.5f, rq, rt, seg != gs, rep, seg, false, -1);
        }
    }

    /// <summary>A leaf's axis-aligned half size in its group's frame.</summary>
    private static Vector3 Extent(in Leaf l) => l.Turned ? Vector3.Abs(l.AX) * l.Half.X + Vector3.Abs(l.AY) * l.Half.Y + Vector3.Abs(l.AZ) * l.Half.Z : l.Half;

    private void BuildNode(int node, int start, int count)
    {
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        var cLo = lo;
        var cHi = hi;
        for (var i = start; i < start + count; i++)
        {
            ref var l = ref Leaves[i];
            var e = Extent(l);
            lo = Vector3.Min(lo, l.Centre - e);
            hi = Vector3.Max(hi, l.Centre + e);
            cLo = Vector3.Min(cLo, l.Centre);
            cHi = Vector3.Max(cHi, l.Centre);
        }
        ref var n = ref Nodes[node];
        n.Centre = (lo + hi) * 0.5f;
        n.Half = (hi - lo) * 0.5f;
        if (count <= LeafSize)
        {
            n.Start = start;
            n.Count = count;
            return;
        }
        var ext = cHi - cLo;
        var axis = ext.X >= ext.Y && ext.X >= ext.Z ? 0 : ext.Y >= ext.Z ? 1 : 2;
        // (the same parts in the same order always give the same tree)
        for (var i = start; i < start + count; i++)
            _keys[i] = Leaves[i].Centre[axis];
        Array.Sort(_keys, Leaves, start, count);
        var half = count / 2;
        var left = NodeCount;
        NodeCount += 2;
        Nodes[node].Start = left;
        Nodes[node].Count = 0;
        BuildNode(left, start, half);
        BuildNode(left + 1, start + half, count - half);
    }

    /// <summary>The leaves of group <paramref name="g"/> whose bounds reach the box at <paramref name="centre"/>, half size <paramref name="half"/> (the group's frame), into <paramref name="into"/>. Returns how many (stops when full).</summary>
    public int Collect(int g, Vector3 centre, Vector3 half, Span<int> into)
    {
        Span<int> stack = stackalloc int[64];
        var sp = 0;
        stack[sp++] = Groups[g].Root;
        var found = 0;
        var nodes = Nodes;
        while (sp > 0)
        {
            ref var n = ref nodes[stack[--sp]];
            var d = Vector3.Abs(n.Centre - centre) - n.Half - half;
            if (d.X > 0 || d.Y > 0 || d.Z > 0)
                continue;
            if (n.Count > 0)
            {
                for (var i = n.Start; i < n.Start + n.Count && found < into.Length; i++)
                    into[found++] = i;
                continue;
            }
            if (sp + 2 > stack.Length)
                continue;
            stack[sp++] = n.Start + 1;
            stack[sp++] = n.Start;
        }
        return found;
    }

    /// <summary>
    /// The leaf pairs of group <paramref name="ga"/> and <paramref name="other"/>'s group <paramref name="gb"/> whose bounds
    /// come within <paramref name="margin"/>, as flat pairs in <paramref name="into"/>. <paramref name="ba"/>: b's group frame in a's.
    /// </summary>
    public int Pairs(int ga, ConstructCollider other, int gb, in CFrame ba, float margin, Span<int> into)
    {
        Span<int> stack = stackalloc int[128];
        var sp = 0;
        stack[sp++] = Groups[ga].Root;
        stack[sp++] = other.Groups[gb].Root;
        var found = 0;
        var na = Nodes;
        var nb = other.Nodes;
        var m = new Vector3(margin);
        while (sp > 0)
        {
            var ib = stack[--sp];
            var ia = stack[--sp];
            ref var a = ref na[ia];
            ref var b = ref nb[ib];
            var d = Vector3.Abs(ba.Point(b.Centre) - a.Centre) - a.Half - CFrame.Abs(ba, b.Half) - m;
            if (d.X > 0 || d.Y > 0 || d.Z > 0)
                continue;
            if (a.Count > 0 && b.Count > 0)
            {
                for (var i = a.Start; i < a.Start + a.Count; i++)
                    for (var j = b.Start; j < b.Start + b.Count && found + 2 <= into.Length; j++)
                    {
                        into[found++] = i;
                        into[found++] = j;
                    }
                continue;
            }
            if (sp + 4 > stack.Length)
                continue;
            // The bigger one first.
            var splitA = b.Count > 0 || a.Count == 0 && a.Half.LengthSquared() >= b.Half.LengthSquared();
            if (splitA)
            {
                stack[sp++] = a.Start + 1;
                stack[sp++] = ib;
                stack[sp++] = a.Start;
                stack[sp++] = ib;
            }
            else
            {
                stack[sp++] = ia;
                stack[sp++] = b.Start + 1;
                stack[sp++] = ia;
                stack[sp++] = b.Start;
            }
        }
        return found / 2;
    }

    /// <summary>Leaf <paramref name="k"/> in the world, its group's frame being <paramref name="f"/>.</summary>
    public void World(int k, in CFrame f, out WBox w)
    {
        ref var l = ref Leaves[k];
        w.C = f.Point(l.Centre);
        w.H = l.Half;
        w.Hull = l.Hull;
        if (l.Turned)
        {
            w.X = f.Dir(l.AX);
            w.Y = f.Dir(l.AY);
            w.Z = f.Dir(l.AZ);
        }
        else
        {
            w.X = f.X;
            w.Y = f.Y;
            w.Z = f.Z;
        }
    }
}

public sealed partial class Construct
{
    private ConstructCollider? _collider;
    private bool _colliderStale = true;

    /// <summary>The compound collider, as the parts are now (built on first use after a change).</summary>
    internal ConstructCollider Collider
    {
        get
        {
            if (_colliderStale || _collider == null)
            {
                _collider ??= new ConstructCollider();
                _collider.Build(this);
                _colliderStale = false;
                _colliderSerial = _editSerial;
            }
            return _collider;
        }
    }

    /// <summary>The parts changed: the collider is built again when next asked for.</summary>
    internal void ColliderChanged() => _colliderStale = true;
}
