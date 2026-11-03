using System.Numerics;
using System.Runtime.InteropServices;

namespace CallOfSlop;

/// <summary>
/// Contacts (docs/sandbox/CONSTRUCTS.md, "Compound collision"): every solid piece of a moving construct against the map, the
/// ground and other constructs, through each rigid group's tree (<see cref="ConstructCollider"/>) and the narrow phase
/// (<see cref="ConstructNarrow"/>). A contact on a piece beyond a joint carries that joint's chain, as ever. Points are
/// reduced per rigid group to the few that hold the shape up, so a capital hull on a floor costs what a crate costs.
/// </summary>
public static partial class ConstructPhysics
{
    /// <summary>What every contact allows beyond this tick's travel (m): pieces this close make contact points.</summary>
    public const float Skin = 0.02f;

    private const float WorldFriction = 0.7f, BodyFriction = 0.6f;
    private const int MaxCand = 4096;
    private const int KeepPerNormal = 9; // the deepest and the eight furthest round it
    private const float ClusterDot = 0.95f;
    private const float MaxGroundDepth = 1f; // a piece further than this behind a sheet of ground has gone through it, or never met it
    private const int BigFootprintCells = 900; // a group over more ground cells than this asks for them leaf by leaf

    private static readonly CPoint[] _cand = new CPoint[MaxCand];
    private static readonly int[] _leafBuf = new int[1 << 16]; // (a capital deck on a floor: every piece of its underside)
    private static readonly int[] _pairBuf = new int[1 << 17];
    private static readonly int[] _cluster = new int[MaxCand];
    private static readonly bool[] _keepFlag = new bool[MaxCand];
    private static readonly List<ContactShape> _gather = new(1024);
    private static readonly ConstructNarrow.Shape _sa = new(), _sb = new();
    private static readonly QueryFilter ShapesFilter = new(QueryLayers.World & ~QueryLayers.Trunks & ~QueryLayers.Terrain);
    private static readonly QueryFilter GroundFilter = new(QueryLayers.Terrain);

    /// <summary>A world's table (made at its first step, so no later tick allocates).</summary>
    private static WarmEntry[] WarmOf(ConstructWorld w) => w.WarmTable ??= new WarmEntry[WarmSize];

    /// <summary>
    /// Last tick's normal impulses, by contact (a body pair and a spot on the first body): the solver starts from them, so a
    /// stack carries its weight from the first pass. One table a world. Not the friction: carried over, a stack's small
    /// sways fed themselves until it fell; friction is settled afresh each tick.
    /// </summary>
    internal struct WarmEntry
    {
        public ulong Key;
        public uint Tick;
        public float Jn;
    }

    private const int WarmSize = 1 << 14;
    private static WarmEntry[] _warm = [];
    private static uint _warmTick;

    private static ulong WarmKey(int idA, int idB, int seg, Vector3 local)
    {
        const float Grid = 0.05f, Offset = 0.025f;
        var x = (int)MathF.Floor((local.X + Offset) / Grid);
        var y = (int)MathF.Floor((local.Y + Offset) / Grid);
        var z = (int)MathF.Floor((local.Z + Offset) / Grid);
        var h = ((ulong)(uint)idA + 1) * 0x9E3779B97F4A7C15UL;
        h = (h ^ (h >> 29) ^ (ulong)(uint)(idB + 2)) * 0xBF58476D1CE4E5B9UL;
        h = (h ^ (h >> 32) ^ (ulong)(uint)seg) * 0x94D049BB133111EBUL;
        h = (h ^ (h >> 29) ^ (uint)x) * 0x9E3779B97F4A7C15UL;
        h = (h ^ (h >> 32) ^ (uint)y) * 0xBF58476D1CE4E5B9UL;
        h = (h ^ (h >> 29) ^ (uint)z) * 0x94D049BB133111EBUL;
        h ^= h >> 31;
        return h == 0 ? 1 : h;
    }

    private static void WarmStart(float dt)
    {
        var mask = _warm.Length - 1;
        if (mask < 0)
            return;
        // (before any of them pushes: closing faster than resting things do)
        var hit = -2f * GravityAccel * dt - 0.5f;
        for (var k = 0; k < _contactCount; k++)
        {
            ref var ct = ref _contacts[k];
            if (ct.Key != 0)
                ct.Hit = Vector3.Dot(Rel(ct), ct.Normal) < hit;
        }
        for (var k = 0; k < _contactCount; k++)
        {
            ref var ct = ref _contacts[k];
            if (ct.Key == 0 || ct.MassN <= 0)
                continue;
            for (var probe = 0; probe < 4; probe++)
            {
                ref readonly var e = ref _warm[(int)(ct.Key + (ulong)probe) & mask];
                if (e.Key != ct.Key)
                    continue;
                if (e.Tick == _warmTick - 1)
                {
                    ct.Jn = e.Jn;
                    Push2(ct, ct.Normal * ct.Jn);
                }
                break;
            }
        }
    }

    private static void WarmStore()
    {
        if (_contactCount == 0)
            return;
        var mask = _warm.Length - 1;
        for (var k = 0; k < _contactCount; k++)
        {
            ref var ct = ref _contacts[k];
            if (ct.Key == 0 || ct.Jn <= 0 || ct.Hit)
                continue; // (a blow isn't a load: starting next tick from it would throw the body back)
            var slot = (int)ct.Key & mask;
            // (the first free or old slot of the four; an old contact's place is taken)
            for (var probe = 0; probe < 4; probe++)
            {
                var at = (int)(ct.Key + (ulong)probe) & mask;
                if (_warm[at].Key == ct.Key || _warm[at].Tick != _warmTick)
                {
                    slot = at;
                    break;
                }
            }
            ref var e = ref _warm[slot];
            e.Key = ct.Key;
            e.Tick = _warmTick;
            e.Jn = ct.Jn;
        }
    }

    /// <summary>How a group moves this tick: the velocity at <paramref name="at"/> and the spin (the speed its motors may reach counts, so a stalled arm still looks ahead).</summary>
    private static void GroupMotion(Construct c, int index, int seg, Vector3 at, out Vector3 v, out Vector3 w)
    {
        v = Vector3.Zero;
        w = Vector3.Zero;
        if (_dyn[index])
        {
            v = c.Vel + Vector3.Cross(c.AngVel, at - c.WorldCom);
            w = c.AngVel;
        }
        if (!_jact[index])
            return;
        for (var guard = 0; seg > 0 && seg <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            var at2 = seg - 1;
            ref var j = ref c.Joints[at2];
            seg = j.ParentSeg;
            if (j.Freeform)
                continue;
            var slot = _jBase[index] + at2;
            var jv = _jMotorMax[slot] > 0 && MathF.Abs(_jWant[slot]) > MathF.Abs(j.Vel) ? _jWant[slot] : j.Vel;
            if (j.Slides)
                v += _jAxis[slot] * jv;
            else
            {
                v += Vector3.Cross(_jAxis[slot], at - _jPivot[slot]) * jv;
                w += _jAxis[slot] * jv;
            }
        }
    }

    /// <summary>The narrow test of two world pieces (hulls through their shapes), appending to the candidates.</summary>
    private static int Narrow(in WBox a, ConstructCollider ca, in CFrame fa, in WBox b, ConstructCollider? cb, in CFrame fb, float margin, int n)
    {
        if (a.Hull < 0 && b.Hull < 0)
            return ConstructNarrow.BoxBox(a, b, margin, _cand, n);
        if (a.Hull >= 0)
            _sa.FromHull(ca.Hulls[a.Hull], fa);
        else
            _sa.FromBox(a);
        if (b.Hull >= 0)
            _sb.FromHull(cb!.Hulls[b.Hull], fb);
        else
            _sb.FromBox(b);
        return ConstructNarrow.HullHull(_sa, _sb, margin, _cand, n);
    }

    private static float Thin(in WBox b) => MathF.Min(b.H.X, MathF.Min(b.H.Y, b.H.Z));

    /// <summary>
    /// Two pieces, swept when one could pass the other within this tick: tried along the way (a few steps, only for a fast
    /// thin piece that the test where it stands found nothing for). What it finds is moved back to now, with the gap it closes
    /// on the way, so the solve lets it come that far and no further. <paramref name="rel"/>: a's velocity against b's,
    /// <paramref name="spin"/>: a's spin against b's.
    /// </summary>
    private static int Swept(in WBox a, ConstructCollider ca, in CFrame fa, in WBox b, ConstructCollider? cb, in CFrame fb, Vector3 rel, Vector3 spin, float margin, float dt, int n)
    {
        var got = Narrow(a, ca, fa, b, cb, fb, margin, n);
        if (got > n)
            return got;
        var travel = rel.Length() * dt + spin.Length() * dt * a.H.Length();
        var thin = 2 * (Thin(a) + Thin(b));
        if (travel < 0.5f * thin || travel < 0.02f)
            return n;
        var steps = Math.Clamp((int)MathF.Ceiling(travel / MathF.Max(thin, 0.05f)), 1, 6);
        for (var s = 1; s <= steps; s++)
        {
            var f = dt * s / steps;
            var m = a;
            m.Hull = -1; // (a hull goes along as its box)
            var turn = spin * f;
            m.C += rel * f;
            if (turn != Vector3.Zero)
            {
                m.X = Vector3.Normalize(m.X + Vector3.Cross(turn, m.X));
                m.Y = Vector3.Normalize(m.Y + Vector3.Cross(turn, m.Y));
                m.Z = Vector3.Normalize(m.Z + Vector3.Cross(turn, m.Z));
            }
            var o = b;
            o.Hull = -1;
            got = ConstructNarrow.BoxBox(m, o, Skin, _cand, n);
            if (got == n)
                continue;
            var kept = n;
            for (var k = n; k < got; k++)
            {
                ref var p = ref _cand[k];
                var close = Vector3.Dot(rel, p.N);
                if (close >= 0)
                    continue; // (it moves away along this normal: nothing to stop)
                var q = p;
                q.P -= rel * f;
                q.Gap = MathF.Max(0, p.Gap) - close * f;
                _cand[kept++] = q;
            }
            return kept;
        }
        return n;
    }

    /// <summary>
    /// Every how many pieces a walk takes so that <paramref name="found"/> of them share what is left of the candidate points
    /// from <paramref name="n"/>: a capital hull flat on a floor keeps points all over it, not only its first thousand pieces.
    /// </summary>
    private static int Spread(int found, int n) => Math.Max(1, found / Math.Max(1, (MaxCand - 16 - n) / ConstructNarrow.MaxPoints));

    /// <summary>Does <paramref name="wheels"/>'s wheel stand on <paramref name="other"/>, on its ray, as on the map (<see cref="DeckMassShare"/>)?</summary>
    private static bool WheelGround(Construct wheels, Construct other) => other.Anchored || other.Mass >= wheels.Mass * DeckMassShare;

    /// <summary>The map and the ground against every piece of a moving construct.</summary>
    private static void WorldCompound(Construct c, short index, IWorldQuery query, float dt)
    {
        var col = c.Collider;
        var n = 0;
        for (var g = 0; g < col.GroupCount; g++)
        {
            int seg = col.Groups[g].Seg;
            if (!(_dyn[index] || _jact[index] && seg != 0))
                continue; // (an anchored base: only what its joints swing)
            c.WorldFrame(seg, out var q, out var t);
            var f = CFrame.Of(q, t);
            ref var root = ref col.Nodes[col.Groups[g].Root];
            var mid = f.Point(root.Centre);
            GroupMotion(c, index, seg, mid, out var v, out var w);
            var travel = (v.Length() + w.Length() * root.Half.Length()) * dt;
            var margin = Skin + travel;
            var reach = CFrame.Abs(f, root.Half) + new Vector3(margin);
            var start = n;
            query.Gather(mid - reach, mid + reach, ShapesFilter, _gather);
            n = ShapesAgainst(c, col, g, f, mid, v, w, query, margin, dt, n);
            if (query.Terrain is { } terrain)
            {
                var size = reach * 2;
                var cell = terrain.Data.Cell;
                if (size.X * size.Z <= BigFootprintCells * cell * cell)
                {
                    query.Gather(mid - reach, mid + reach, GroundFilter, _gather);
                    n = ShapesAgainst(c, col, g, f, mid, v, w, query, margin, dt, n);
                }
                else
                    n = GroundLeafByLeaf(c, col, g, f, mid, v, w, query, margin, dt, n);
            }
            n = start + ReducePoints(_cand.AsSpan(start, n - start));
        }
        for (var k = 0; k < n; k++)
        {
            ref var p = ref _cand[k];
            ref var leaf = ref col.Leaves[p.LeafA];
            if (_contactCount >= MaxContacts - 1)
                return;
            var u0 = _chainCount;
            var un = _jact[index] && leaf.Seg != 0 ? Chain(c, index, leaf.Seg, p.P, out _) : 0;
            if (un < 0)
                return;
            if (Add(index, -1, p.P, p.N, p.Gap, WorldFriction, u0, un))
            {
                _chainCount += un;
                _contacts[_contactCount - 1].Key = WarmKey(c.Id, -1, leaf.Seg, c.ToLocal(p.P));
            }
        }
    }

    /// <summary>A big construct over fine ground: each leaf's own patch of it, so the cells under the whole of it are never walked.</summary>
    private static int GroundLeafByLeaf(Construct c, ConstructCollider col, int g, in CFrame f, Vector3 mid, Vector3 v, Vector3 w, IWorldQuery query, float margin, float dt, int n)
    {
        var d = query.Terrain!.Data;
        ref var gr = ref col.Groups[g];
        for (var k = gr.First; k < gr.First + gr.Count && n < MaxCand - 64; k++)
        {
            if (col.Leaves[k].Wheel)
                continue;
            col.World(k, f, out var box);
            var e = CFrame.Abs(new CFrame { X = box.X, Y = box.Y, Z = box.Z }, box.H) + new Vector3(margin);
            if (box.C.Y - e.Y > d.HighestGround + 1 || box.C.Y + e.Y < d.LowestGround)
                continue;
            query.Gather(box.C - e, box.C + e, GroundFilter, _gather);
            n = ShapesAgainst(c, col, g, f, mid, v, w, query, margin, dt, n, k);
        }
        return n;
    }

    /// <summary>The shapes gathered against group <paramref name="g"/>'s pieces (or the one leaf <paramref name="only"/>).</summary>
    private static int ShapesAgainst(Construct c, ConstructCollider col, int g, in CFrame f, Vector3 mid, Vector3 v, Vector3 w, IWorldQuery query, float margin, float dt, int n, int only = -1)
    {
        var shapes = CollectionsMarshal.AsSpan(_gather);
        var leaves = _leafBuf.AsSpan();
        var noFrame = default(CFrame);
        for (var si = 0; si < shapes.Length; si++)
        {
            ref readonly var s = ref shapes[si];
            int found;
            if (only >= 0)
            {
                leaves[0] = only;
                found = 1;
            }
            else if (s.Type == ContactShape.TypeBox)
            {
                var o = new CFrame { X = f.LocalDir(s.Box.AxisX), Y = f.LocalDir(s.Box.AxisY), Z = f.LocalDir(s.Box.AxisZ) };
                found = col.Collect(g, f.Local(s.Box.Center), CFrame.Abs(o, s.Box.Half) + new Vector3(margin), leaves);
            }
            else
            {
                // (the triangle's bounds in the group's frame)
                Vector3 a = f.Local(s.A), b = f.Local(s.B), e = f.Local(s.C);
                var lo = Vector3.Min(a, Vector3.Min(b, e));
                var hi = Vector3.Max(a, Vector3.Max(b, e));
                found = col.Collect(g, (lo + hi) * 0.5f, (hi - lo) * 0.5f + new Vector3(margin), leaves);
            }
            if (found == 0)
                continue;
            if (s.Type == ContactShape.TypeBox)
            {
                var other = new WBox { C = s.Box.Center, X = s.Box.AxisX, Y = s.Box.AxisY, Z = s.Box.AxisZ, H = s.Box.Half, Hull = -1 };
                var every = Spread(found, n);
                for (var i = 0; i < found && n < MaxCand - 16; i += every)
                {
                    var k = leaves[i];
                    if (col.Leaves[k].Wheel)
                        continue;
                    col.World(k, f, out var box);
                    var from = n;
                    n = Swept(box, col, f, other, null, noFrame, v + Vector3.Cross(w, box.C - mid), w, margin, dt, n);
                    for (; from < n; from++)
                        _cand[from].LeafA = k;
                }
                continue;
            }
            var nt = Vector3.Cross(s.B - s.A, s.C - s.A);
            var len = nt.LengthSquared();
            if (len < 1e-12f)
                continue;
            nt /= MathF.Sqrt(len);
            var step = Spread(found, n);
            for (var i = 0; i < found && n < MaxCand - 16; i += step)
            {
                var k = leaves[i];
                if (col.Leaves[k].Wheel)
                    continue;
                col.World(k, f, out var box);
                if (box.Hull >= 0)
                    _sa.FromHull(col.Hulls[box.Hull], f);
                else
                    _sa.FromBox(box);
                var side = nt;
                var deepest = MaxGroundDepth;
                if (!s.OneSided)
                {
                    // A sheet with two sides (a dug wall, an open mesh): the side the piece is on.
                    if (Vector3.Dot(nt, box.C - s.A) < 0)
                        side = -nt;
                    deepest = 0.25f + Thin(box);
                }
                var from = n;
                n = ConstructNarrow.PieceTriangle(_sa, s.A, s.B, s.C, side, margin, deepest, _cand, n);
                var kept = from;
                for (var j = from; j < n; j++)
                {
                    ref var p = ref _cand[j];
                    // (behind a steep neighbour's sheet but above the ground: no contact)
                    if (p.Gap < -0.03f && s.Id.Kind == ShapeKind.Terrain && query.Terrain is { } tr && tr.GroundBelow(p.P.X, p.P.Z, p.P.Y) <= p.P.Y)
                        continue;
                    p.LeafA = k;
                    if (kept != j)
                        _cand[kept] = p;
                    kept++;
                }
                n = kept;
            }
        }
        return n;
    }

    /// <summary>
    /// The points of one rigid group against one thing, thinned: per normal, the deepest and the furthest out round it (a
    /// flat hull needs its corners, not every part's). Returns how many stay, at the start of <paramref name="p"/>.
    /// </summary>
    private static int ReducePoints(Span<CPoint> p)
    {
        var n = p.Length;
        if (n <= KeepPerNormal)
            return n;
        Span<Vector3> normals = stackalloc Vector3[16];
        Span<int> sizes = stackalloc int[16];
        var clusters = 0;
        for (var k = 0; k < n; k++)
        {
            var c = -1;
            for (var i = 0; i < clusters && c < 0; i++)
                if (Vector3.Dot(normals[i], p[k].N) > ClusterDot)
                    c = i;
            if (c < 0)
            {
                c = clusters < 16 ? clusters++ : 15;
                if (c < 15 || sizes[15] == 0)
                {
                    normals[c] = p[k].N;
                    sizes[c] = 0;
                }
            }
            sizes[c]++;
            _cluster[k] = c;
        }
        var keep = _keepFlag.AsSpan(0, n);
        keep.Clear();
        Span<Vector3> dirs = stackalloc Vector3[8];
        for (var c = 0; c < clusters; c++)
        {
            if (sizes[c] <= KeepPerNormal)
            {
                for (var k = 0; k < n; k++)
                    if (_cluster[k] == c)
                        keep[k] = true;
                continue;
            }
            var nn = normals[c];
            var t1 = Vector3.Normalize(MathF.Abs(nn.X) < 0.9f ? Vector3.Cross(nn, Vector3.UnitX) : Vector3.Cross(nn, Vector3.UnitZ));
            var t2 = Vector3.Cross(nn, t1);
            (dirs[0], dirs[1], dirs[2], dirs[3]) = (t1, -t1, t2, -t2);
            (dirs[4], dirs[5], dirs[6], dirs[7]) = (t1 + t2, t1 - t2, -t1 + t2, -t1 - t2);
            var deep = -1;
            var deepGap = float.MaxValue;
            for (var d = 0; d < 8; d++)
            {
                var best = -1;
                var far = float.MinValue;
                for (var k = 0; k < n; k++)
                {
                    if (_cluster[k] != c)
                        continue;
                    var s = Vector3.Dot(dirs[d], p[k].P);
                    if (s > far)
                        (far, best) = (s, k);
                }
                if (best >= 0)
                    keep[best] = true;
            }
            for (var k = 0; k < n; k++)
                if (_cluster[k] == c && p[k].Gap < deepGap)
                    (deepGap, deep) = (p[k].Gap, k);
            if (deep >= 0)
                keep[deep] = true;
        }
        var m = 0;
        for (var k = 0; k < n; k++)
            if (keep[k])
                p[m++] = p[k];
        return m;
    }

    /// <summary>Every piece of the pair against every piece near it, through the trees of each rigid group of each.</summary>
    private static void PairContacts(Construct a, short ia, Construct b, short ib, float dt)
    {
        var ca = a.Collider;
        var cb = b.Collider;
        Span<CFrame> fb = stackalloc CFrame[ca.GroupCount == 1 && cb.GroupCount == 1 ? 1 : Construct.MaxJoints + 1];
        for (var g = 0; g < cb.GroupCount; g++)
        {
            b.WorldFrame(cb.Groups[g].Seg, out var q, out var t);
            fb[g] = CFrame.Of(q, t);
        }
        var n = 0;
        var pairs = _pairBuf.AsSpan();
        for (var ga = 0; ga < ca.GroupCount; ga++)
        {
            int sa = ca.Groups[ga].Seg;
            var movesA = _dyn[ia] || _jact[ia] && sa != 0;
            a.WorldFrame(sa, out var qa, out var ta);
            var fa = CFrame.Of(qa, ta);
            var midA = fa.Point(ca.Nodes[ca.Groups[ga].Root].Centre);
            GroupMotion(a, ia, sa, midA, out var va, out var wa);
            var speedA = va.Length() + wa.Length() * ca.Nodes[ca.Groups[ga].Root].Half.Length();
            for (var gb = 0; gb < cb.GroupCount; gb++)
            {
                int sb = cb.Groups[gb].Seg;
                var movesB = _dyn[ib] || _jact[ib] && sb != 0;
                if (!movesA && !movesB && !(b.Asleep && sb != 0))
                    continue;
                ref readonly var fgb = ref fb[gb];
                var midB = fgb.Point(cb.Nodes[cb.Groups[gb].Root].Centre);
                GroupMotion(b, ib, sb, midB, out var vb, out var wb);
                var speedB = vb.Length() + wb.Length() * cb.Nodes[cb.Groups[gb].Root].Half.Length();
                var margin = Skin + (speedA + speedB) * dt;
                var ba = new CFrame { X = fa.LocalDir(fgb.X), Y = fa.LocalDir(fgb.Y), Z = fa.LocalDir(fgb.Z), T = fa.Local(fgb.T) };
                var count = ca.Pairs(ga, cb, gb, ba, margin, pairs);
                var start = n;
                var every = Spread(count, n);
                for (var i = 0; i < count && n < MaxCand - 16; i += every)
                {
                    int la = pairs[i * 2], lb = pairs[i * 2 + 1];
                    ref var pa = ref ca.Leaves[la];
                    ref var pb = ref cb.Leaves[lb];
                    if (pa.Wheel && (pb.Wheel || !_dyn[ib] || WheelGround(a, b)) || pb.Wheel && (!_dyn[ia] || WheelGround(b, a)))
                        continue; // (a wheel rolls on its ray: only something loose bumps it)
                    if (_exOn && (pa.Part == _exPartI || pb.Part == _exPartJ))
                        continue; // (the hitch's own parts, coupled: they meet there)
                    ca.World(la, fa, out var wa2);
                    cb.World(lb, fgb, out var wb2);
                    var rel = va + Vector3.Cross(wa, wa2.C - midA) - vb - Vector3.Cross(wb, wb2.C - midB);
                    var from = n;
                    n = Swept(wa2, ca, fa, wb2, cb, fgb, rel, wa - wb, margin, dt, n);
                    for (var k = from; k < n; k++)
                    {
                        ref var p = ref _cand[k];
                        p.LeafA = la;
                        p.LeafB = lb;
                    }
                }
                n = start + ReducePoints(_cand.AsSpan(start, n - start));
            }
        }
        var moving = _dyn[ia] && (a.Vel.LengthSquared() >= StillSpeed2 || a.AngVel.LengthSquared() >= StillSpeed2);
        var movingB = _dyn[ib] && (b.Vel.LengthSquared() >= StillSpeed2 || b.AngVel.LengthSquared() >= StillSpeed2);
        for (var k = 0; k < n; k++)
        {
            ref var p = ref _cand[k];
            if (NearHitch(p.P) || _contactCount >= MaxContacts - 1)
                continue;
            ref var pa = ref ca.Leaves[p.LeafA];
            ref var pb = ref cb.Leaves[p.LeafB];
            if (p.Gap < Skin)
            {
                // Touching: a moving body wakes what it touches (from the next tick); something pushes a sleeping joint: it answers then.
                if (moving && b.Asleep && (!b.Anchored || pb.Seg != 0))
                    b.Wake();
                if (movingB && a.Asleep && (!a.Anchored || pa.Seg != 0))
                    a.Wake();
            }
            var ua = _chainCount;
            var na = _jact[ia] && pa.Seg != 0 ? Chain(a, ia, pa.Seg, p.P, out _) : 0;
            if (na < 0)
                return;
            _chainCount += na;
            var ub = _chainCount;
            var nb = _jact[ib] && pb.Seg != 0 ? Chain(b, ib, pb.Seg, p.P, out _) : 0;
            if (nb < 0)
                nb = 0;
            _chainCount += nb;
            if (!Add(ia, ib, p.P, p.N, p.Gap, BodyFriction, ua, na, ub, nb))
                return;
            ref var ct = ref _contacts[_contactCount - 1];
            ct.Key = WarmKey(a.Id, b.Id, pa.Seg, a.ToLocal(p.P));
            if (pb.Wheel)
                ct.Surf = TreadVel(b, pb.Part, p.P); // (a turning wheel's tread carries what it touches)
            else if (pa.Wheel)
                ct.Surf = -TreadVel(a, pa.Part, p.P);
            if (b.BeltCount > 0)
                ct.Surf += BeltVel(b, pb.Part); // (a running conveyor's belt too: ConstructDeck.cs)
            if (a.BeltCount > 0)
                ct.Surf -= BeltVel(a, pa.Part);
        }
    }

    /// <summary>Would the held body's piece, moved, overlap a piece of <paramref name="b"/> it doesn't overlap now? (Only a new overlap refuses a step: pr02.)</summary>
    private static bool NewOverlap(Construct c, Construct b, Vector3 move)
    {
        var ca = c.Collider;
        var cb = b.Collider;
        Span<int> pairs = _pairBuf.AsSpan();
        const float In = 0.01f;
        var travel = move.Length();
        for (var ga = 0; ga < ca.GroupCount; ga++)
        {
            c.WorldFrame(ca.Groups[ga].Seg, out var qa, out var ta);
            var fa = CFrame.Of(qa, ta);
            var moved = fa;
            moved.T += move;
            for (var gb = 0; gb < cb.GroupCount; gb++)
            {
                b.WorldFrame(cb.Groups[gb].Seg, out var qb, out var tb);
                var fb = CFrame.Of(qb, tb);
                var ba = new CFrame { X = fa.LocalDir(fb.X), Y = fa.LocalDir(fb.Y), Z = fa.LocalDir(fb.Z), T = fa.Local(fb.T) };
                var count = ca.Pairs(ga, cb, gb, ba, travel, pairs);
                for (var i = 0; i < count; i++)
                {
                    cb.World(pairs[i * 2 + 1], fb, out var wb);
                    ca.World(pairs[i * 2], moved, out var wm);
                    if (!PiecesOverlap(wm, ca, moved, wb, cb, fb, In))
                        continue;
                    ca.World(pairs[i * 2], fa, out var wa);
                    if (!PiecesOverlap(wa, ca, fa, wb, cb, fb, In))
                        return true;
                }
            }
        }
        return false;
    }

    private static bool PiecesOverlap(in WBox a, ConstructCollider ca, in CFrame fa, in WBox b, ConstructCollider cb, in CFrame fb, float depth)
    {
        if (a.Hull < 0 && b.Hull < 0)
            return ConstructNarrow.Overlapping(a, b, depth);
        return Narrow(a, ca, fa, b, cb, fb, -depth, MaxCand - 8) > MaxCand - 8;
    }

    /// <summary>Wakes the sleeping free bodies touching one of <paramref name="a"/>'s turning wheels (cs03).</summary>
    private static void WakeOnTread(ConstructWorld world, Construct a, int i, int n)
    {
        var others = world.UseGrid ? NearInSim(world, a, n) : null;
        var m = others?.Count ?? n;
        var ca = a.Collider;
        var pairs = _pairBuf.AsSpan();
        for (var oj = 0; oj < m; oj++)
        {
            var j = others != null ? others[oj].SimIndex : oj;
            if (j == i)
                continue;
            var b = _bodies[j];
            if (!b.Asleep || b.Anchored || b.CarriedBy != 0)
                continue;
            var reach = a.Radius + b.Radius;
            if (Vector3.DistanceSquared(a.WorldCom, b.WorldCom) > reach * reach)
                continue;
            var cb = b.Collider;
            for (var ga = 0; ga < ca.GroupCount && b.Asleep; ga++)
            {
                a.WorldFrame(ca.Groups[ga].Seg, out var qa, out var ta);
                var fa = CFrame.Of(qa, ta);
                for (var gb = 0; gb < cb.GroupCount && b.Asleep; gb++)
                {
                    b.WorldFrame(cb.Groups[gb].Seg, out var qb, out var tb);
                    var fb = CFrame.Of(qb, tb);
                    var ba = new CFrame { X = fa.LocalDir(fb.X), Y = fa.LocalDir(fb.Y), Z = fa.LocalDir(fb.Z), T = fa.Local(fb.T) };
                    var count = ca.Pairs(ga, cb, gb, ba, Skin, pairs);
                    for (var k = 0; k < count && b.Asleep; k++)
                    {
                        ref var la = ref ca.Leaves[pairs[k * 2]];
                        if (!la.Wheel && a.BeltCount == 0)
                            continue;
                        ca.World(pairs[k * 2], fa, out var wa);
                        cb.World(pairs[k * 2 + 1], fb, out var wb);
                        if (ConstructNarrow.Overlapping(wa, wb, -Skin) && (TreadVel(a, la.Part, wb.C) + BeltVel(a, la.Part)).LengthSquared() > 1e-4f)
                            b.Wake();
                    }
                }
            }
        }
    }
}
