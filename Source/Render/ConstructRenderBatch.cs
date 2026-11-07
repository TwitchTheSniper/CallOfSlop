using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// Merged meshes (docs/render/MERGED-MESHES.md): a construct's static parts drawn as a few GPU-resident draw groups instead
/// of box by box, so draw cost no longer grows with box count. Each rigid moving piece (a segment: the body, or what hangs
/// on a joint) is cut into chunks of space (<see cref="MergeCell"/> metres a side, at most <see cref="MergeChunkBoxes"/>
/// boxes); a chunk's boxes sit in the segment's frame on the GPU (<see cref="Renderer.MergedMesh"/>) and are drawn at the
/// segment's pose this frame, one call for the plain boxes and one for the contoured ones. Parts that animate or change on
/// their own (wheels, joints, rigs, rotors, levers, lamps, glass, engines, burning or holding stored parts, merged guns) are
/// walked every frame as before. A part's look (paint, damage, frost) is a texel of its own, checked every
/// <see cref="MergeCheckFrames"/> frames and rewritten alone; an edit re-plans the construct (cheap) and rebuilds only the
/// chunks whose parts changed, at most <see cref="MergeBudgetBoxes"/> boxes a frame, a chunk waiting its turn walked as
/// before. Render only: the same on host and clients, nothing on the wire or in a save. <c>--no-merge</c> turns it off.
/// Nothing allocates in a steady frame.
/// </summary>
public static partial class ConstructRender
{
    /// <summary>Parts from which a body is "big" (drawn whole from far off: ConstructRenderCull.cs).</summary>
    public const int BatchMinParts = 64;

    /// <summary>Merged meshes on (the default) or every part walked as before (<c>--no-merge</c>; the bench compares both).</summary>
    public static bool MergeOn = true;

    /// <summary>A body with fewer static boxes than this is walked: a draw of its own would cost more than its boxes in the shared batch.</summary>
    public const int MergeMinBoxes = 24;

    /// <summary>A chunk's side (metres in its segment's frame) and most boxes; boxes rebuilt a frame at most (one chunk always goes).</summary>
    public const float MergeCell = 16f;
    public const int MergeChunkBoxes = 2048, MergeBudgetBoxes = 8192;
    private const int MergeCheckFrames = 4;

    /// <summary>One chunk of a segment: its parts, its boxes on the GPU, the hashes that say whether they are still right.</summary>
    private sealed class MergeChunk
    {
        public readonly Renderer.MergedMesh Gl = new();
        public long Key; // segment and cell
        public int Sub; // the cell's overflow chunk
        public int Seg;
        public long Geo; // its parts' types, places and turns, in order
        public long Look; // their health, paint and frost
        public int[] Members = [];
        public int MemberCount, Boxes;
        public bool Dirty = true, LookDue = true;
        public Vector3 Centre; // a sphere round its boxes, in the segment's frame
        public float Radius;
    }

    private sealed class MergeSet
    {
        public int Stamp = -1;
        public long Dyn;
        public int Seen;
        public bool On; // enough static boxes to merge
        public MergeChunk[] Chunks = [];
        public int ChunkCount;
        public int[] Walked = [];
        public int WalkedCount, Boxes;
    }

    private struct MergeDraw
    {
        public Renderer.MergedMesh Mesh;
        public Matrix4x4 Frame;
    }

    private static readonly Dictionary<Construct, MergeSet> _merges = new();
    private static readonly List<Construct> _mergeGone = new();
    private static readonly Dictionary<ConstructPartDef, int> _defBoxes = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ConstructPartDef>); // a type's drawn boxes, or -1: it moves or changes on its own
    private static readonly Dictionary<long, int> _open = new();
    private static readonly Dictionary<(long, int), int> _oldChunk = new();
    private static int[] _partEntry = [], _wheelMark = [], _memberAt = [];
    private static int _wheelToken;
    private static long[] _eKey = [], _eGeo = [];
    private static int[] _eSub = [], _eSeg = [], _eCount = [], _eBoxes = [], _eFirst = [];
    private static bool[] _oldClaimed = [];
    private static float[] _mergeXform = [], _mergeLook = [], _mergeShape = [];
    private static MergeDraw[] _mergeQueue = new MergeDraw[64];
    private static int _mergeQueued, _mergeBudget, _mergeFrame;
    private static Matrix4x4[] _segFrame = new Matrix4x4[Construct.MaxJoints + 1];
    private static int[] _segToken = new int[Construct.MaxJoints + 1];
    private static int _segTok;

    /// <summary>Static boxes in merged meshes and parts walked in merged bodies, last frame (the F9 window, the smoke test).</summary>
    public static int LastBatchBoxes { get; private set; }
    public static int LastBatchWalked { get; private set; }

    /// <summary>Merged chunks drawn last frame, chunks rebuilt, chunks waiting (walked) and bytes the merged meshes hold on the GPU.</summary>
    public static int LastMergedChunks { get; private set; }
    public static int LastMergedBuilt { get; private set; }
    public static int LastMergedWaiting { get; private set; }
    public static long MergedBytes { get; private set; }

    /// <summary>Drawn boxes of a type that never moves or changes on its own, or -1.</summary>
    private static int StaticBoxes(ConstructPartDef d)
    {
        if (_defBoxes.TryGetValue(d, out var n))
            return n;
        n = 0;
        if (d.Role == PartRole.Joint || d.IsRail || d.HasRig || d.HasGlass || d.HasDial || d.IsBeacon || d.IsTerminal || d.IsSpawner || d.IsOrdnance
            || d.HasEngineBoxes || d.Rotor != null || d.TrackRays > 0 || d.Logic is CLogic.AnalogLever or CLogic.Lamp || d.Surface is { Control: > 0 })
            n = -1;
        else
            foreach (var box in d.Boxes.AsSpan())
            {
                if (box.Rig != BoxRig.None || box.Spin || box.Roll || box.IsLeaf || box.IsGlass || box.Pump != 0)
                {
                    n = -1;
                    break;
                }
                if (!box.Hidden)
                    n++;
            }
        _defBoxes[d] = n;
        return n;
    }

    /// <summary>Boxes part <paramref name="i"/> puts in a merged mesh (0: none, it is walked or draws nothing).</summary>
    private static int MergeBoxes(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (p.Burn > 0 || p.Store != 0 || c.MergeOf[i] != 0 || c.PartScale[i] > 1 || c.EngineOf[i] != 0 || _wheelMark[i] == _wheelToken)
            return 0;
        return Math.Max(0, StaticBoxes(p.D));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Mix(long h, long v) => (h ^ v) * 0x100000001B3L + (h >> 29);

    /// <summary>What decides which parts are merged, beyond a rebuild: the types, fires and stored parts.</summary>
    private static long DynKey(Construct c)
    {
        var h = (long)c.PartCount;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            h = Mix(h, RuntimeHelpers.GetHashCode(p.D) | (p.Burn > 0 ? 1L << 40 : 0) | (p.Store != 0 ? 1L << 41 : 0));
        }
        return h;
    }

    private static long PartGeo(in CPart p) =>
        Mix(Mix(Mix(Mix(RuntimeHelpers.GetHashCode(p.D), BitConverter.SingleToInt32Bits(p.Pos.X)), BitConverter.SingleToInt32Bits(p.Pos.Y)), BitConverter.SingleToInt32Bits(p.Pos.Z)), p.Rot);

    private static bool Frosted(Construct c, int i) => (c.Frozen || c.FrozenPieces > 0) && c.PieceFrozen(i);

    private static long ChunkLook(Construct c, MergeChunk ch)
    {
        var h = 23L;
        for (var k = 0; k < ch.MemberCount; k++)
        {
            ref var p = ref c.Parts[ch.Members[k]];
            h = Mix(h, BitConverter.SingleToInt32Bits(p.Health) ^ ((long)p.Paint << 32) ^ (p.Broken ? 1L << 62 : 0) ^ (Frosted(c, ch.Members[k]) ? 1L << 61 : 0));
        }
        return h;
    }

    private static void Grow<T>(ref T[] a, int n)
    {
        if (a.Length < n)
            Array.Resize(ref a, Math.Max(n, a.Length * 3 / 2)); // (keeps what is in it: the plan and the shape scratch grow as they fill)
    }

    /// <summary>
    /// Which parts go in which chunk, as the construct is now. Chunks whose parts are the same types in the same places keep
    /// their GPU data (their part indices are updated); the others are marked to be rebuilt. Returns the static boxes.
    /// </summary>
    private static int Plan(Renderer? gfx, MergeSet set, Construct c)
    {
        var n = c.PartCount;
        Grow(ref _partEntry, n);
        Grow(ref _wheelMark, n);
        Grow(ref _memberAt, n);
        Grow(ref set.Walked, n);
        if (++_wheelToken == int.MaxValue)
        {
            Array.Clear(_wheelMark);
            _wheelToken = 1;
        }
        for (var k = 0; k < c.WheelCount; k++)
            if ((uint)c.Wheels[k].Part < (uint)n)
                _wheelMark[c.Wheels[k].Part] = _wheelToken;
        _open.Clear();
        var entries = 0;
        var total = 0;
        set.WalkedCount = 0;
        for (var i = 0; i < n; i++)
        {
            var nb = MergeBoxes(c, i);
            if (nb == 0)
            {
                _partEntry[i] = -1;
                if (StaticBoxes(c.Parts[i].D) != 0 || c.Parts[i].Store != 0 || c.Parts[i].Burn > 0)
                    set.Walked[set.WalkedCount++] = i; // (a static part with no box drawn needs no walk unless it burns or holds parts)
                continue;
            }
            ref var p = ref c.Parts[i];
            var seg = p.Seg > 0 && p.Seg <= c.JointCount ? p.Seg : 0;
            var key = (long)seg << 48 | (long)((int)MathF.Floor(p.Pos.X / MergeCell) & 0xFFFF) << 32
                | (long)((int)MathF.Floor(p.Pos.Y / MergeCell) & 0xFFFF) << 16 | (long)((int)MathF.Floor(p.Pos.Z / MergeCell) & 0xFFFF);
            if (!_open.TryGetValue(key, out var e) || _eBoxes[e] + nb > MergeChunkBoxes)
            {
                var sub = _open.ContainsKey(key) ? _eSub[e] + 1 : 0;
                Grow(ref _eKey, entries + 1);
                Grow(ref _eGeo, entries + 1);
                Grow(ref _eSub, entries + 1);
                Grow(ref _eSeg, entries + 1);
                Grow(ref _eCount, entries + 1);
                Grow(ref _eBoxes, entries + 1);
                Grow(ref _eFirst, entries + 1);
                e = entries++;
                _eKey[e] = key;
                _eSub[e] = sub;
                _eSeg[e] = seg;
                _eCount[e] = _eBoxes[e] = 0;
                _eGeo[e] = 17;
                _open[key] = e;
            }
            _partEntry[i] = e;
            _eCount[e]++;
            _eBoxes[e] += nb;
            _eGeo[e] = Mix(_eGeo[e], PartGeo(in p));
            total += nb;
        }
        set.Boxes = total;
        var on = gfx != null && total >= MergeMinBoxes;
        if (!on)
        {
            for (var k = 0; k < set.ChunkCount; k++)
                gfx?.FreeMerged(set.Chunks[k].Gl);
            set.ChunkCount = 0;
            set.On = false;
            return total;
        }
        // Members in part order, chunk by chunk.
        var at = 0;
        for (var e = 0; e < entries; e++)
        {
            _eFirst[e] = at;
            at += _eCount[e];
        }
        for (var e = 0; e < entries; e++)
            _eCount[e] = 0;
        for (var i = 0; i < n; i++)
            if (_partEntry[i] is var e and >= 0)
                _memberAt[_eFirst[e] + _eCount[e]++] = i;
        // The old chunks by place: kept where nothing in them changed.
        _oldChunk.Clear();
        Grow(ref _oldClaimed, set.ChunkCount);
        for (var k = 0; k < set.ChunkCount; k++)
        {
            _oldChunk[(set.Chunks[k].Key, set.Chunks[k].Sub)] = k;
            _oldClaimed[k] = false;
        }
        var old = set.Chunks;
        var oldCount = set.ChunkCount;
        var chunks = old.Length >= entries ? old : new MergeChunk[Math.Max(entries, 4)];
        var made = new MergeChunk[entries]; // (plan time only: an edit, never a steady frame)
        for (var e = 0; e < entries; e++)
        {
            MergeChunk ch;
            if (_oldChunk.TryGetValue((_eKey[e], _eSub[e]), out var k) && !_oldClaimed[k])
            {
                _oldClaimed[k] = true;
                ch = old[k];
                if (ch.Geo != _eGeo[e] || ch.Boxes != _eBoxes[e] || ch.MemberCount != _eCount[e] || ch.Seg != _eSeg[e])
                    ch.Dirty = true;
            }
            else
                ch = new MergeChunk { Dirty = true };
            ch.Key = _eKey[e];
            ch.Sub = _eSub[e];
            ch.Seg = _eSeg[e];
            ch.Geo = _eGeo[e];
            ch.Boxes = _eBoxes[e];
            Grow(ref ch.Members, _eCount[e]);
            Array.Copy(_memberAt, _eFirst[e], ch.Members, 0, _eCount[e]);
            ch.MemberCount = _eCount[e];
            ch.LookDue = true;
            made[e] = ch;
        }
        for (var k = 0; k < oldCount; k++)
            if (!_oldClaimed[k])
                gfx!.FreeMerged(old[k].Gl);
        if (chunks == old)
            Array.Clear(chunks);
        Array.Copy(made, chunks, entries);
        set.Chunks = chunks;
        set.ChunkCount = entries;
        set.On = true;
        return total;
    }

    /// <summary>A chunk's looks into the scratch (the same slot order as <see cref="BuildChunk"/>).</summary>
    private static int FillLooks(Construct c, MergeChunk ch)
    {
        Grow(ref _mergeLook, ch.Boxes * Renderer.MergeLookFloats);
        var look = _mergeLook;
        var s = 0;
        for (var pass = 0; pass < 2; pass++)
            for (var k = 0; k < ch.MemberCount; k++)
            {
                var i = ch.Members[k];
                ref var p = ref c.Parts[i];
                var d = p.D;
                var health = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
                var shade = p.Broken ? 0.25f : 0.55f + 0.45f * health;
                var wear = p.Broken ? 1f : 1f - health;
                var frost = Frosted(c, i);
                foreach (ref readonly var box in d.Boxes.AsSpan())
                {
                    if (box.Hidden || (box.Shape != 0) != (pass == 1))
                        continue;
                    Vector3 tint;
                    float code;
                    if (box.Glow)
                    {
                        tint = box.Color * shade;
                        code = -1f;
                    }
                    else
                    {
                        tint = ConstructPaint.BoxColour(p.Paint, in box) * shade;
                        if (frost)
                            tint = Vector3.Lerp(tint, Frost, 0.45f);
                        code = Surfaces.Code(Surfaces.OnPaint(box.Surface, p.Paint, box.Paintable), wear);
                    }
                    var o = s++ * 4;
                    look[o] = tint.X;
                    look[o + 1] = tint.Y;
                    look[o + 2] = tint.Z;
                    look[o + 3] = code;
                }
            }
        return s;
    }

    /// <summary>A chunk's boxes onto the GPU: plain boxes first (slots 0 up, drawn from gl_VertexID), then the contoured ones and their shapes' vertices.</summary>
    private static void BuildChunk(Renderer gfx, Construct c, MergeChunk ch)
    {
        Grow(ref _mergeXform, ch.Boxes * Renderer.MergeXformFloats);
        var xf = _mergeXform;
        var s = 0;
        var cubes = 0;
        var verts = 0;
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        for (var pass = 0; pass < 2; pass++)
        {
            for (var k = 0; k < ch.MemberCount; k++)
            {
                var i = ch.Members[k];
                ref var p = ref c.Parts[i];
                var d = p.D;
                for (var b = 0; b < d.Boxes.Length; b++)
                {
                    ref readonly var box = ref d.Boxes[b];
                    if (box.Hidden || (box.Shape != 0) != (pass == 1))
                        continue;
                    c.BoxOf(i, b, out var centre, out var half);
                    mn = Vector3.Min(mn, centre - half);
                    mx = Vector3.Max(mx, centre + half);
                    var m = box.Model(p.Rot, centre);
                    var o = s * Renderer.MergeXformFloats;
                    xf[o] = m.M11; xf[o + 1] = m.M12; xf[o + 2] = m.M13; xf[o + 3] = m.M41;
                    xf[o + 4] = m.M21; xf[o + 5] = m.M22; xf[o + 6] = m.M23; xf[o + 7] = m.M42;
                    xf[o + 8] = m.M31; xf[o + 9] = m.M32; xf[o + 10] = m.M33; xf[o + 11] = m.M43;
                    if (pass == 1)
                    {
                        var mesh = PartShapes.Mesh(box.Shape);
                        var nv = mesh.Length / 6;
                        Grow(ref _mergeShape, (verts + nv) * Renderer.MergeShapeFloats);
                        var sh = _mergeShape;
                        for (var v = 0; v < nv; v++)
                        {
                            var q = (verts + v) * Renderer.MergeShapeFloats;
                            Array.Copy(mesh, v * 6, sh, q, 6);
                            sh[q + 6] = s;
                        }
                        verts += nv;
                    }
                    else
                        cubes++;
                    s++;
                }
            }
        }
        gfx.UploadMergedXforms(ch.Gl, xf, s, cubes);
        gfx.UploadMergedShapes(ch.Gl, _mergeShape, verts);
        var looks = FillLooks(c, ch); // (first: it can grow the scratch)
        gfx.UploadMergedLooks(ch.Gl, _mergeLook, looks);
        ch.Look = ChunkLook(c, ch);
        ch.LookDue = false;
        ch.Centre = s > 0 ? (mn + mx) * 0.5f : Vector3.Zero;
        ch.Radius = s > 0 ? (mx - mn).Length() * 0.5f + 0.05f : 0f;
        ch.Dirty = false;
    }

    /// <summary>The self-test: how a construct splits, with no GL: static boxes in merged meshes, parts still walked each frame, contoured boxes among the merged.</summary>
    public static (int Boxes, int Walked, int Shaped) BatchCounts(Construct c)
    {
        var set = new MergeSet();
        var boxes = Plan(null, set, c);
        var shaped = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (_partEntry[i] >= 0)
                foreach (var box in c.Parts[i].D.Boxes.AsSpan())
                    if (!box.Hidden && box.Shape != 0)
                        shaped++;
        return (boxes, set.WalkedCount, shaped);
    }

    public static string WalkedNames(Construct c)
    {
        var set = new MergeSet();
        Plan(null, set, c);
        return string.Join(", ", Enumerable.Range(0, set.WalkedCount).Select(k => c.Parts[set.Walked[k]].D).GroupBy(d => d.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} x{g.Count()} ({g.First().Boxes.Count(b => !b.Hidden)} boxes, {g.First().Boxes.Count(b => !b.Hidden && b.Shape != 0)} shaped, role {g.First().Role})"));
    }

    /// <summary>
    /// Draws <paramref name="c"/> through its merged meshes, the walked parts box by box. Returns false (nothing drawn) when
    /// the body has too few static boxes to merge: the caller walks it all.
    /// </summary>
    private static bool DrawMergedBody(Renderer gfx, ConstructWorld world, Construct c, in Matrix4x4 body, float alpha, bool cull, ref int drawn)
    {
        if (!_merges.TryGetValue(c, out var set))
            _merges[c] = set = new MergeSet();
        set.Seen = _mergeFrame;
        var check = (_mergeFrame + c.Id) % MergeCheckFrames == 0;
        if (set.Stamp != c.RebuildStamp)
        {
            Plan(gfx, set, c);
            set.Stamp = c.RebuildStamp;
            set.Dyn = DynKey(c);
        }
        else if (check && DynKey(c) is var dyn && dyn != set.Dyn)
        {
            Plan(gfx, set, c);
            set.Dyn = dyn;
        }
        if (!set.On)
            return false;
        if (++_segTok == int.MaxValue)
        {
            Array.Clear(_segToken);
            _segTok = 1;
        }
        var n = 0;
        var origin = gfx.Origin;
        for (var k = 0; k < set.ChunkCount; k++)
        {
            var ch = set.Chunks[k];
            if (ch.Dirty && _mergeBudget > 0)
            {
                BuildChunk(gfx, c, ch);
                _mergeBudget -= ch.Boxes;
                LastMergedBuilt++;
            }
            if (ch.Dirty)
            {
                // Waiting its turn to be rebuilt: drawn part by part, as it was before merged meshes.
                LastMergedWaiting++;
                for (var m = 0; m < ch.MemberCount; m++)
                    if (ch.Members[m] < c.PartCount)
                        n += DrawPart(gfx, world, c, ch.Members[m], body, alpha, 1f);
                continue;
            }
            if (ch.LookDue || check && ((k + _mergeFrame) & 1) == 0)
            {
                var look = ChunkLook(c, ch);
                if (look != ch.Look || ch.LookDue)
                {
                    if (look != ch.Look && FillLooks(c, ch) is var looks)
                        gfx.UploadMergedLooks(ch.Gl, _mergeLook, looks);
                    ch.Look = look;
                    ch.LookDue = false;
                }
            }
            ref var frame = ref _segFrame[ch.Seg];
            if (_segToken[ch.Seg] != _segTok)
            {
                frame = ch.Seg == 0 ? body : SegMatrix(c, ch.Seg, alpha) * body;
                _segToken[ch.Seg] = _segTok;
            }
            if (cull)
            {
                var at = Vector3.Transform(ch.Centre, frame) + origin;
                var outside = false;
                for (var q = 0; q < 6 && !outside; q++)
                {
                    var pl = _planes[q];
                    outside = pl.X * at.X + pl.Y * at.Y + pl.Z * at.Z + pl.W < -ch.Radius;
                }
                if (outside)
                    continue;
            }
            if (_mergeQueued == _mergeQueue.Length)
                Array.Resize(ref _mergeQueue, _mergeQueue.Length * 2); // (grows to the busiest frame once)
            _mergeQueue[_mergeQueued++] = new MergeDraw { Mesh = ch.Gl, Frame = frame };
            LastMergedChunks++;
            LastBatchBoxes += ch.Gl.Slots;
            n += ch.Gl.Slots;
        }
        for (var k = 0; k < set.WalkedCount; k++)
        {
            var i = set.Walked[k];
            if (i >= c.PartCount)
                continue;
            n += DrawPart(gfx, world, c, i, body, alpha, 1f);
            if (c.Parts[i].Store != 0)
                n += DrawStored(gfx, world, c, i, body, alpha);
        }
        LastBatchWalked += set.WalkedCount;
        drawn += n;
        return true;
    }

    /// <summary>Starts a frame's merged drawing (the rebuild budget and the counters).</summary>
    private static void BeginMergedFrame()
    {
        _mergeBudget = MergeBudgetBoxes;
        _mergeQueued = 0;
        LastMergedChunks = LastMergedBuilt = LastMergedWaiting = 0;
    }

    /// <summary>The chunks queued this frame, in one run of the merged shader (opaque, so their order with the box batch doesn't matter).</summary>
    private static void FlushMerged(Renderer gfx)
    {
        if (_mergeQueued == 0)
            return;
        gfx.BeginMerged();
        var rel = gfx.Relative;
        gfx.Relative = true; // (the frames are relative to the render origin already)
        for (var k = 0; k < _mergeQueued; k++)
        {
            gfx.DrawMerged(_mergeQueue[k].Mesh, in _mergeQueue[k].Frame);
            _mergeQueue[k].Mesh = null!;
        }
        gfx.Relative = rel;
        gfx.EndMerged();
        _mergeQueued = 0;
    }

    /// <summary>After a frame: the merged meshes of bodies not drawn for a while go (their GL objects with them).</summary>
    private static void BatchSweep(Renderer gfx, ConstructWorld world)
    {
        _ = world;
        _mergeFrame++;
        if (_mergeFrame % 120 != 0 || _merges.Count == 0)
            return;
        long bytes = 0;
        foreach (var (c, s) in _merges)
        {
            if (_mergeFrame - s.Seen > 600)
                _mergeGone.Add(c);
            for (var k = 0; k < s.ChunkCount; k++)
                bytes += s.Chunks[k].Gl.Bytes;
        }
        foreach (var c in _mergeGone)
        {
            var s = _merges[c];
            for (var k = 0; k < s.ChunkCount; k++)
                gfx.FreeMerged(s.Chunks[k].Gl);
            _merges.Remove(c);
        }
        _mergeGone.Clear();
        MergedBytes = bytes;
    }
}
