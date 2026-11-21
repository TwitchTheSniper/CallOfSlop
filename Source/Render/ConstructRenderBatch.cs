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
    public const float MergeCell = 32f;
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
        public int[] SlotPart = []; // slot k's member (its place in Members: an edit can renumber the parts) and box
        public byte[] SlotBox = [];
        public bool Dirty = true, LookDue = true;
        public bool Lamps; // a member is a lamp: its looks are checked every check
        public Vector3 Centre; // a sphere round its boxes, in the segment's frame
        public float Radius;
        public Vector3 Min, Max; // its boxes' bounds in the segment's frame (the occlusion query's box)
        public Vector3 EyeAt; // the eye in its own frame when its query was last asked for, and in which occlusion frame
        public int EyeFrame = -1;
    }

    private sealed class MergeSet
    {
        public int Stamp = -1, Doors;
        public long Dyn;
        public int Seen, Draws, Parts;
        public bool On; // enough static boxes to merge
        public MergeChunk[] Chunks = [];
        public int ChunkCount;
        public int[] Walked = [];
        public int WalkedCount, Boxes;
        public int[] Extra = []; // merged parts whose stored parts or glass panes are drawn alone each frame
        public int ExtraCount;
        public Vector4[] WalkedSphere = [], ExtraSphere = []; // each one's sphere in the body's frame, for the view test; radius below 0: always drawn (it moves, burns or smokes)
    }

    private struct MergeDraw
    {
        public Renderer.MergedMesh Mesh;
        public Matrix4x4 Frame;
        public int Detail; // the smallest size class drawn (the far detail level)
        public bool Test; // drawn only where last frame's occlusion query saw its box (ConstructRenderOcclusion.cs)
        public bool Use; // the eye has hardly moved against this chunk since that query: its result still speaks for it
        public Vector3 Min, Max; // the query's box, in the frame
    }

    private static readonly Dictionary<Construct, MergeSet> _merges = new();
    private static readonly List<Construct> _mergeGone = new();
    private static readonly Dictionary<ConstructPartDef, int> _defBoxes = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ConstructPartDef>); // a type's drawn boxes, or -1: it moves or changes on its own
    private static readonly Dictionary<long, int> _open = new();
    private static readonly Dictionary<(long, int), int> _oldChunk = new();
    private static int[] _partEntry = [], _wheelMark = [], _memberAt = [];
    private static int _wheelToken;
    private static int[] _partWaiting = [];
    private static int _waitToken;
    private static long[] _eKey = [], _eGeo = [];
    private static int[] _eSub = [], _eSeg = [], _eCount = [], _eBoxes = [], _eFirst = [];
    private static bool[] _oldClaimed = [];
    private static float[] _mergeXform = [], _mergeLook = [];
    private static long[] _slotKey = [];
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

    /// <summary>(RuntimeParts) Freed run-time parts are let go (the cache fills again as types are drawn).</summary>
    internal static void ForgetRuntime()
    {
        _defBoxes.Clear();
        _defPanes.Clear();
    }

    private static readonly HashSet<ConstructPartDef> _defPanes = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ConstructPartDef>); // merged types with glass panes to draw alone

    /// <summary>Drawn boxes of a type that never moves or changes on its own, or -1.</summary>
    internal static int StaticBoxes(ConstructPartDef d) // (also the preview export's cost line)
    {
        if (_defBoxes.TryGetValue(d, out var n))
            return n;
        n = 0;
        // (a lamp merges with its lit look in the chunk's looks; designed ordnance with its wings folded, as it sits until it drops;
        // a part with glass merges its opaque boxes and only the panes are walked: GlassOnly)
        if (d.Role == PartRole.Joint || d.IsRail || d.HasRig || d.HasDial || d.IsBeacon || d.IsTerminal || d.IsSpawner
            || d.HasWork || d.HasRouterBoxes || d.HasEngineBoxes || d.Rotor != null || d.TrackRays > 0 || d.Logic is CLogic.AnalogLever || d.Surface is { Control: > 0 })
            n = -1;
        else
        {
            var panes = false;
            foreach (var box in d.Boxes.AsSpan())
            {
                if (box.Rig != BoxRig.None || box.Shows != 0 || box.Spin || box.Roll || box.IsLeaf || box.Pump != 0)
                {
                    n = -1;
                    break;
                }
                panes |= box.IsGlass && !box.Hidden;
                if (!box.Hidden && !box.IsGlass)
                    n++;
            }
            if (panes && d.Logic == CLogic.Lamp)
                n = -1; // (a lit lamp's glass glows with the rest of it: DrawPart's order, so it is walked)
            if (panes && n >= 0)
                _defPanes.Add(d); // (by its boxes as they are now, not HasGlass: a run-time def's boxes arrive later)
        }
        _defBoxes[d] = n;
        return n;
    }

    /// <summary>
    /// Boxes part <paramref name="i"/> puts in a merged mesh, or -1: it is walked whole. A part that holds stored parts or has
    /// glass panes merges its own opaque boxes all the same; what it holds and its panes are drawn alone (<see cref="MergeSet.Extra"/>).
    /// </summary>
    private static int MergeBoxes(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (p.Burn > 0 || c.GantryHold || c.MergeOf[i] != 0 || c.Rides(i) || c.PartScale[i] > 1 || c.EngineOf[i] != 0 || _wheelMark[i] == _wheelToken)
            return -1;
        if (p.D.ShellBody)
            return -1; // (a standing shell: walked, drawn as a worn one (ConstructRenderShellRigs.cs) or with its fitted modules from their parts, ShellFittedLooks)
        if (c.DoorCount > 0 && c.DoorOfListed(i) >= 0)
            return -1; // (an event door's parts are always walked, at their moving pose while it moves: settling needs no remerge)
        if (p.D.Logic == CLogic.Lamp && ConstructConfig.Get(p, p.D, CfgKey.Blink, 0) > 0)
            return -1; // (a blinking lamp: its look changes faster than a chunk's looks are checked)
        return StaticBoxes(p.D);
    }

    /// <summary>A merged lamp's look (as <see cref="DrawPart"/> draws a lamp that doesn't blink): lit, and in what colour.</summary>
    private static bool LampLook(Construct c, int i, out Vector3 glow)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        glow = new Vector3(1f, 0.93f, 0.6f);
        var lit = ConstructLamps.Lit(c, i);
        var preview = PreviewBody == c && PreviewPart == i;
        if (!p.Broken && preview)
            lit = true;
        if (ConstructConfig.Has(d, CfgKey.Colour))
            glow = preview
                ? ConstructConfig.Rgb(PreviewValue(d, CfgKey.Colour, 0xFFED99)) * (PreviewValue(d, CfgKey.Brightness, 100) / 100f)
                : ConstructConfig.Rgb(ConstructConfig.Get(p, d, CfgKey.Colour, 0xFFED99)) * (ConstructConfig.Get(p, d, CfgKey.Brightness, 100) / 100f);
        return lit;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Mix(long h, long v) => (h ^ v) * 0x100000001B3L + (h >> 29);

    /// <summary>What decides which parts are merged, beyond a rebuild: the types, fires and stored parts.</summary>
    private static long DynKey(Construct c)
    {
        var h = (long)c.PartCount + (c.GantryHold ? 1L << 50 : 0); // (a shell in a gantry's clamp is walked: its modules come off)
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            h = Mix(h, RuntimeHelpers.GetHashCode(p.D) | (p.Burn > 0 ? 1L << 40 : 0) | (p.Store != 0 ? 1L << 41 : 0) | (p.D.ShellBody ? (long)ShellFittedLooks.FittedMask(in p, p.D) << 42 : 0) | (p.D.HasSeams && i < c.BlockJoined.Length ? (long)c.BlockJoined[i] << 50 : 0)); // (a shell's fitted modules: walked once one is)
            if (p.D.Logic == CLogic.Lamp && ConstructConfig.Get(p, p.D, CfgKey.Blink, 0) > 0)
                h = Mix(h, i); // (a lamp set blinking is walked)
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
            h = Mix(Mix(h, BitConverter.SingleToInt32Bits(p.Health) ^ ((long)p.Paint << 32) ^ (p.Broken ? 1L << 62 : 0) ^ (Frosted(c, ch.Members[k]) ? 1L << 61 : 0)), p.Finish); // (the texture paint tool: a finish rewrites the looks alone, ConstructFinish.cs)
            if (p.D.Logic == CLogic.Lamp && LampLook(c, ch.Members[k], out var glow))
                h = Mix(Mix(Mix(h, BitConverter.SingleToInt32Bits(glow.X) + 1), BitConverter.SingleToInt32Bits(glow.Y)), BitConverter.SingleToInt32Bits(glow.Z)); // (lit, and its colour)
        }
        return h;
    }

    private static void Grow<T>(ref T[] a, int n)
    {
        if (a.Length < n)
            Array.Resize(ref a, Math.Max(n, a.Length * 3 / 2)); // (keeps what is in it: the plan and the shape scratch grow as they fill)
    }

    /// <summary>
    /// A walked part's sphere in the body's frame, for the view test (walked parts off screen are skipped). Radius -1: always
    /// drawn, because it moves on a joint, a door or a wheel, burns, or smokes (its particles leave from it seen or not).
    /// </summary>
    private static Vector4 WalkSphere(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        if (p.Seg != 0 || p.Burn > 0 || d.HasWork || d.Logic == CLogic.AnalogLever || d.Rotor != null || d.Role is PartRole.Joint or PartRole.Suspension || d.IsRail || d.HasRig || d.TrackRays > 0
            || _wheelMark[i] == _wheelToken || c.DoorCount > 0 && c.DoorOfListed(i) >= 0 || c.MergeOf[i] != 0 || c.EngineOf[i] != 0 || d.ShellBody)
            return new Vector4(0, 0, 0, -1);
        c.PartBounds(i, out var mn, out var mx);
        var r = (mx - mn).Length() * 0.5f + (p.Store != 0 ? 2f : 0.25f); // (stored parts pile on its slots: room for them)
        return new Vector4((mn + mx) * 0.5f, r);
    }

    /// <summary>Is a sphere in the body's frame wholly outside the view?</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool OffScreen(in Vector4 sphere, in Matrix4x4 body, Vector3 origin)
    {
        if (sphere.W < 0)
            return false;
        var at = Vector3.Transform(new Vector3(sphere.X, sphere.Y, sphere.Z), body) + origin;
        for (var q = 0; q < 6; q++)
        {
            var pl = _planes[q];
            if (pl.X * at.X + pl.Y * at.Y + pl.Z * at.Z + pl.W < -sphere.W)
                return true;
        }
        return false;
    }

    /// <summary>
    /// The far detail level (docs/render/MERGED-MESHES.md "Far detail"): a merged box's size class by the square root of its
    /// two biggest sides (a long rod counts by its length as well as its width); a glowing box two classes bigger, so lit
    /// lamps still read from afar. A chunk past a class's distance doesn't draw that class: a box that would be under
    /// <see cref="LodPixels"/> pixels on screen isn't drawn.
    /// </summary>
    public const int LodClasses = 5;
    private static readonly float[] LodClassTop = [float.PositiveInfinity, 4f, 1f, 0.25f, 0.0625f]; // metres: class k's biggest box
    public static float LodPixels = 1f;
    public static bool LodPixelsForced; // (--lod-px: the Video settings leave it alone)
    public static bool ChunkLodOn = true; // (--no-chunk-lod, the bench)
    public static bool ChunkLodSetting = true; // (the Video settings' Far detail: High draws every box)
    private static float _lodPixelsAt; // pixels a metre at a metre away, this frame (the projection's scale times half the view's height)

    private static int SizeClass(in CPartBox box, bool lamp)
    {
        var a = MathF.Abs(box.Half.X) * 2;
        var b = MathF.Abs(box.Half.Y) * 2;
        var c = MathF.Abs(box.Half.Z) * 2;
        var big = MathF.Max(a, MathF.Max(b, c));
        var mid = MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));
        var m = MathF.Sqrt(big * mid);
        var k = m >= 4f ? 0 : m >= 1f ? 1 : m >= 0.25f ? 2 : m >= 0.0625f ? 3 : 4;
        return box.Glow || lamp ? Math.Max(0, k - 2) : k;
    }

    /// <summary>The smallest size class a chunk this far off (metres to its nearest side) draws.</summary>
    private static int DetailAt(float dist)
    {
        if (!ChunkLodOn || !ChunkLodSetting || dist <= 0 || _lodPixelsAt <= 0)
            return LodClasses - 1;
        var k = LodClasses - 1;
        while (k > 0 && LodClassTop[k] * _lodPixelsAt / dist < LodPixels)
            k--;
        return k;
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
        Grow(ref set.Extra, n);
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
        set.WalkedCount = set.ExtraCount = 0;
        for (var i = 0; i < n; i++)
        {
            var nb = MergeBoxes(c, i);
            if (nb > 0 && c.PocketHost.Length == n && c.PocketHost[i] != 0) // (a length off the part count: numbers moved, wait for the rebuild)
            {
                // A surface a flush pod sinks into: walked, so its boxes alone take the cut mark (Renderer.Cuts.cs).
                _partEntry[i] = -1;
                set.Walked[set.WalkedCount++] = i;
                continue;
            }
            if (nb < 0)
            {
                _partEntry[i] = -1;
                if (!c.Rides(i)) // (a rider is drawn on its leg segment: ConstructRenderLegs.cs)
                    set.Walked[set.WalkedCount++] = i;
                continue;
            }
            if (c.Parts[i].Store != 0 || _defPanes.Contains(c.Parts[i].D))
                set.Extra[set.ExtraCount++] = i; // (its own opaque boxes merged; what it holds and its panes drawn alone)
            if (nb == 0)
            {
                _partEntry[i] = -1;
                continue;
            }
            ref var p = ref c.Parts[i];
            var seg = p.Seg > 0 && p.Seg <= c.JointCount ? p.Seg : 0;
            var key = (long)seg << 48 | (long)((int)MathF.Floor(p.Pos.X / MergeCell) & 0xFFFF) << 32
                | (long)((int)MathF.Floor(p.Pos.Y / MergeCell) & 0xFFFF) << 16 | (long)((int)MathF.Floor(p.Pos.Z / MergeCell) & 0xFFFF);
            if (p.Fuse != 0)
                key = long.MinValue | (long)(seg & 0xFF) << 55 | (long)(p.Fuse & 0x7F) << 48 | key & 0xFFFFFFFFFFFFL; // (a fused unit's parts are chunks of their own: an edit beside it never plans them again, ConstructFuse.cs)
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
            _eGeo[e] = Mix(_eGeo[e], PartGeo(in p) ^ (p.D.HasSeams && i < c.BlockJoined.Length ? (long)c.BlockJoined[i] << 56 : 0)); // (a block machine's welded faces: its seam boxes change with them, MachineBlocks.cs)
            total += nb;
        }
        set.Boxes = total;
        Grow(ref set.WalkedSphere, set.WalkedCount);
        Grow(ref set.ExtraSphere, set.ExtraCount);
        for (var k = 0; k < set.WalkedCount; k++)
            set.WalkedSphere[k] = WalkSphere(c, set.Walked[k]);
        for (var k = 0; k < set.ExtraCount; k++)
            set.ExtraSphere[k] = WalkSphere(c, set.Extra[k]);
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
            ch.Lamps = false;
            for (var m = 0; m < ch.MemberCount && !ch.Lamps; m++)
                ch.Lamps = c.Parts[ch.Members[m]].D.Logic == CLogic.Lamp;
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

    /// <summary>A chunk's looks into the scratch, slot by slot (the order <see cref="BuildChunk"/> laid them in).</summary>
    private static int FillLooks(Construct c, MergeChunk ch)
    {
        var n = ch.Gl.Slots;
        Grow(ref _mergeLook, n * Renderer.MergeLookFloats);
        var look = _mergeLook;
        var lastPart = -1;
        float shade = 1, wear = 0;
        var frost = false;
        var lit = false;
        var lampGlow = Vector3.Zero;
        for (var s = 0; s < n; s++)
        {
            var i = ch.Members[ch.SlotPart[s]];
            ref var p = ref c.Parts[i];
            var d = p.D;
            if (i != lastPart)
            {
                lastPart = i;
                var health = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
                shade = p.Broken ? 0.25f : 0.55f + 0.45f * health;
                wear = p.Broken ? 1f : 1f - health;
                frost = Frosted(c, i);
                lit = d.Logic == CLogic.Lamp && LampLook(c, i, out lampGlow);
            }
            ref readonly var box = ref d.Boxes[ch.SlotBox[s]];
            Vector3 tint;
            float code;
            if (lit)
            {
                tint = lampGlow; // (a lamp that is on: every box glows in its colour, as DrawPart draws it)
                code = -1f;
            }
            else if (box.Glow)
            {
                tint = box.Color * shade;
                code = -1f;
            }
            else
            {
                tint = ConstructPaint.BoxColour(p.Paint, in box) * shade;
                if (frost)
                    tint = Vector3.Lerp(tint, Frost, 0.45f);
                code = Surfaces.Code(ConstructFinish.BoxRow(in box, p.Paint, p.Finish), wear);
            }
            var o = s * 4;
            look[o] = tint.X;
            look[o + 1] = tint.Y;
            look[o + 2] = tint.Z;
            look[o + 3] = code;
        }
        return n;
    }

    /// <summary>
    /// A chunk's boxes onto the GPU, grouped by shape (plain boxes first, then each contoured shape: one instanced draw a
    /// group), each slot's part and box kept so its look can be written again alone.
    /// </summary>
    private static void BuildChunk(Renderer gfx, Construct c, MergeChunk ch)
    {
        var n = ch.Boxes;
        Grow(ref _mergeXform, n * Renderer.MergeXformFloats);
        Grow(ref _slotKey, n);
        Grow(ref ch.SlotPart, n);
        Grow(ref ch.SlotBox, n);
        var keys = _slotKey;
        var s = 0;
        for (var k = 0; k < ch.MemberCount; k++)
        {
            var i = ch.Members[k];
            var boxes = c.Parts[i].D.Boxes;
            // (BuildChunk's slot key: shape from bit 40, size class 36-39, member 8-35, box 0-7)
            for (var b = 0; b < boxes.Length; b++)
                if (!boxes[b].Hidden && !boxes[b].IsGlass)
                    keys[s++] = (long)boxes[b].Shape << 40 | (long)SizeClass(in boxes[b], c.Parts[i].D.Logic == CLogic.Lamp) << 36 | (long)k << 8 | (long)b; // (shape, its size class biggest first: the far detail level draws a group's first slots; then the member's place, then box)
        }
        Array.Sort(keys, 0, s);
        var xf = _mergeXform;
        var mn = new Vector3(float.MaxValue);
        var mx = new Vector3(float.MinValue);
        var gl = ch.Gl;
        gl.Groups = 0;
        gl.ClassCount = LodClasses;
        var lastShape = -1;
        for (var k = 0; k < s; k++)
        {
            var shape = (int)(keys[k] >> 40);
            var member = (int)(keys[k] >> 8 & 0xFFFFFFF);
            var i = ch.Members[member];
            var b = (int)(keys[k] & 0xFF);
            ref var p = ref c.Parts[i];
            ref readonly var box = ref p.D.Boxes[b];
            if (shape != lastShape)
            {
                Grow(ref gl.GroupShape, gl.Groups + 1);
                Grow(ref gl.GroupFirst, gl.Groups + 1);
                Grow(ref gl.GroupCount, gl.Groups + 1);
                Grow(ref gl.GroupClassEnd, (gl.Groups + 1) * LodClasses);
                for (var q = 0; q < LodClasses; q++)
                    gl.GroupClassEnd[gl.Groups * LodClasses + q] = 0;
                gl.GroupShape[gl.Groups] = shape;
                gl.GroupFirst[gl.Groups] = k;
                gl.GroupCount[gl.Groups] = 0;
                gl.Groups++;
                lastShape = shape;
            }
            gl.GroupCount[gl.Groups - 1]++;
            var cls = (int)(keys[k] >> 36 & 0xF);
            for (var q = cls; q < LodClasses; q++)
                gl.GroupClassEnd[(gl.Groups - 1) * LodClasses + q]++; // (slots of class q or bigger: the group's first that many)
            ch.SlotPart[k] = member;
            ch.SlotBox[k] = (byte)b;
            c.BoxOf(i, b, out var centre, out var half);
            mn = Vector3.Min(mn, centre - half);
            mx = Vector3.Max(mx, centre + half);
            var m = box.Model(p.Rot, centre);
            if (p.D.IsOrdnance)
                FoldWing(p.D, b, in box, p.Rot, centre, ref m); // (a designed weapon's wings, folded as it sits until it drops)
            if (box.Seam != 0 && i < c.BlockJoined.Length && (c.BlockJoined[i] >> (box.Seam - 1) & 1) != 0)
                m = Matrix4x4.CreateScale(0f) * Matrix4x4.CreateTranslation(centre); // (a block machine's seam box on a welded face: kept in the chunk at no size, so the counts stand: MachineBlocks.cs)
            var o = k * Renderer.MergeXformFloats;
            xf[o] = m.M11; xf[o + 1] = m.M12; xf[o + 2] = m.M13; xf[o + 3] = m.M41;
            xf[o + 4] = m.M21; xf[o + 5] = m.M22; xf[o + 6] = m.M23; xf[o + 7] = m.M42;
            xf[o + 8] = m.M31; xf[o + 9] = m.M32; xf[o + 10] = m.M33; xf[o + 11] = m.M43;
        }
        gfx.UploadMergedXforms(gl, xf, s);
        var looks = FillLooks(c, ch); // (after the slots: it reads them, and it can grow the scratch)
        gfx.UploadMergedLooks(gl, _mergeLook, looks);
        ch.Look = ChunkLook(c, ch);
        ch.LookDue = false;
        ch.Centre = s > 0 ? (mn + mx) * 0.5f : Vector3.Zero;
        ch.Radius = s > 0 ? (mx - mn).Length() * 0.5f + 0.05f : 0f;
        ch.Min = s > 0 ? mn : Vector3.Zero;
        ch.Max = s > 0 ? mx : Vector3.Zero;
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
                    if (!box.Hidden && !box.IsGlass && box.Shape != 0)
                        shaped++;
        return (boxes, set.WalkedCount, shaped);
    }

    /// <summary>The bench (<c>--bench-walked</c>): the walked parts of <paramref name="c"/> by type, most boxes first, and why each type is walked.</summary>
    public static List<(string Name, int Parts, int Boxes, string Why)> WalkedTypes(Construct c)
    {
        var set = new MergeSet();
        Plan(null, set, c);
        var by = new Dictionary<ConstructPartDef, (int Parts, int Boxes)>();
        var why = new Dictionary<ConstructPartDef, string>();
        for (var k = 0; k < set.WalkedCount; k++)
        {
            var i = set.Walked[k];
            ref var p = ref c.Parts[i];
            var d = p.D;
            by.TryGetValue(d, out var v);
            by[d] = (v.Parts + 1, v.Boxes + d.Boxes.Length);
            if (!why.ContainsKey(d))
                why[d] = StaticBoxes(d) < 0 ? WalkWhy(d) : p.Burn > 0 ? "burning" : p.Store != 0 ? "stores parts" : c.GantryHold ? "gantry" : c.MergeOf[i] != 0 ? "merged gun"
                    : c.Rides(i) ? "rides" : c.PartScale[i] > 1 ? "scaled" : c.EngineOf[i] != 0 ? "engine" : c.DoorCount > 0 && c.DoorOfListed(i) >= 0 ? "door"
                    : c.PocketHost.Length == c.PartCount && c.PocketHost[i] != 0 ? "pod cut" : d.ShellBody ? "shell" : "?";
        }
        return by.Select(kv => (kv.Key.Name, kv.Value.Parts, kv.Value.Boxes, why[kv.Key])).OrderByDescending(t => t.Item3).ToList();
    }

    private static string WalkWhy(ConstructPartDef d) =>
        d.Role == PartRole.Joint ? "joint" : d.IsRail ? "rail" : d.HasRig ? "rig" : d.HasDial ? "dial" : d.IsBeacon ? "beacon"
        : d.IsTerminal ? "terminal" : d.IsSpawner ? "spawner" : d.IsOrdnance ? "ordnance" : d.HasWork ? "work" : d.HasRouterBoxes ? "router"
        : d.HasEngineBoxes ? "engine" : d.Rotor != null ? "rotor" : d.TrackRays > 0 ? "track" : d.Logic is CLogic.AnalogLever ? "lever" : d.Logic is CLogic.Lamp ? "lamp"
        : d.Surface is { Control: > 0 } ? "surface" : "box flags";

    /// <summary>
    /// Draws <paramref name="c"/> through its merged meshes, the walked parts box by box. Returns false (nothing drawn) when
    /// the body has too few static boxes to merge: the caller walks it all.
    /// </summary>
    private static bool DrawMergedBody(Renderer gfx, ConstructWorld world, Construct c, in Matrix4x4 body, float alpha, bool cull, ref int drawn)
    {
        if (!_merges.TryGetValue(c, out var set))
            _merges[c] = set = new MergeSet();
        set.Seen = _mergeFrame;
        var check = (++set.Draws + c.Id) % MergeCheckFrames == 0; // (counted in its own draws: a camera pass or a frame it is culled in doesn't skip a check)
        if (set.Stamp != c.RebuildStamp || set.Parts != c.PartCount || set.Doors != c.DoorEpoch) // (parts taken out and not yet rebuilt: the indices moved; the door list was built again)
        {
            c.EnsureDoors();
            Plan(gfx, set, c);
            set.Stamp = c.RebuildStamp;
            set.Parts = c.PartCount;
            set.Doors = c.DoorEpoch;
            set.Dyn = DynKey(c);
        }
        else if (check && DynKey(c) is var dyn && dyn != set.Dyn)
        {
            Plan(gfx, set, c);
            set.Dyn = dyn;
        }
        if (!set.On)
            return false;
        AllocProfile.Mark("cr: merged chunks");
        if (++_segTok == int.MaxValue)
        {
            Array.Clear(_segToken);
            _segTok = 1;
        }
        var n = 0;
        var origin = gfx.Origin;
        if (++_waitToken == int.MaxValue)
        {
            Array.Clear(_partWaiting);
            _waitToken = 1;
        }
        var previewed = PreviewBody == c; // (a lamp's panel is open: its chunk's looks follow the colour being picked every frame)
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
                Grow(ref _partWaiting, c.PartCount);
                for (var m = 0; m < ch.MemberCount; m++)
                    if (ch.Members[m] < c.PartCount)
                    {
                        _partWaiting[ch.Members[m]] = _waitToken;
                        HostCut(gfx, c, ch.Members[m]);
                        n += DrawPart(gfx, world, c, ch.Members[m], body, alpha, 1f);
                    }
                continue;
            }
            if (ch.LookDue || ch.Lamps && (check || previewed) || check && ((k + set.Draws / MergeCheckFrames) & 1) == 0) // (half the chunks each check: each every 8 draws; a chunk with lamps every check, so a row of lamps across chunks switches together)
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
            var detail = LodClasses - 1;
            var test = false;
            var use = false;
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
                var away = Vector3.Distance(at, _lodEye) - ch.Radius;
                detail = DetailAt(away);
                test = OcclusionWanted(ch, away); // (ConstructRenderOcclusion.cs: big enough to be worth a query, and the eye not by it)
                if (test)
                    use = OcclusionEyeSteady(gfx, ch, in frame);
            }
            if (_mergeQueued == _mergeQueue.Length)
                Array.Resize(ref _mergeQueue, _mergeQueue.Length * 2); // (grows to the busiest frame once)
            _mergeQueue[_mergeQueued++] = new MergeDraw { Mesh = ch.Gl, Frame = frame, Detail = detail, Test = test, Use = use, Min = ch.Min, Max = ch.Max };
            LastMergedChunks++;
            var drawnSlots = ch.Gl.SlotsUpTo(detail);
            LastBatchBoxes += drawnSlots;
            LastLodDropped += ch.Gl.Slots - drawnSlots;
            n += drawnSlots;
        }
        AllocProfile.Mark("cr: walked parts"); // (the benchmark's --trace-frame)
        for (var k = 0; k < set.WalkedCount; k++)
        {
            var i = set.Walked[k];
            if (i >= c.PartCount)
                continue;
            if (cull && OffScreen(in set.WalkedSphere[k], in body, origin))
            {
                LastWalkedCulled++;
                continue;
            }
            HostCut(gfx, c, i);
            n += DrawPart(gfx, world, c, i, body, alpha, 1f);
            if (c.Parts[i].Store != 0)
                n += DrawStored(gfx, world, c, i, body, alpha);
        }
        LastBatchWalked += set.WalkedCount;
        for (var k = 0; k < set.ExtraCount; k++)
        {
            // Merged parts that hold stored parts or have panes: only those drawn here.
            var i = set.Extra[k];
            if (i >= c.PartCount || cull && OffScreen(in set.ExtraSphere[k], in body, origin))
                continue;
            HostCut(gfx, c, i);
            if (_defPanes.Contains(c.Parts[i].D) && !(_partWaiting.Length > i && _partWaiting[i] == _waitToken))
                n += DrawPanes(gfx, c, i, body, alpha); // (not while its chunk waits its rebuild: DrawPart drew its panes above)
            if (c.Parts[i].Store != 0)
                n += DrawStored(gfx, world, c, i, body, alpha);
        }
        drawn += n;
        return true;
    }

    /// <summary>A merged part's glass panes, as <see cref="DrawPart"/> draws them (its opaque boxes are in its chunk).</summary>
    private static int DrawPanes(Renderer gfx, Construct c, int i, in Matrix4x4 body, float alpha)
    {
        ref var p = ref c.Parts[i];
        var d = p.D;
        var seg = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, alpha) * body : body;
        var health = Math.Clamp(p.Health / MathF.Max(1, d.Health), 0, 1);
        var shade = p.Broken ? 0.25f : 0.55f + 0.45f * health;
        var drawn = 0;
        for (var b = 0; b < d.Boxes.Length; b++)
        {
            ref readonly var box = ref d.Boxes[b];
            if (box.Hidden || !box.IsGlass)
                continue;
            c.BoxOf(i, b, out var centre, out _);
            var model = box.Model(p.Rot, centre) * seg;
            if (box.Glow)
                gfx.Draw(model, box.Color * shade, 1f, mode: 2, shape: box.Shape);
            else if (!QueueGlass(model, box.Color * shade, box.Glass, box.Shape))
                gfx.Draw(model, box.Color * shade, 1f, mode: 0, shape: box.Shape); // (more panes than the list holds: opaque)
            drawn++;
        }
        return drawn;
    }

    /// <summary>The merged meshes as they stand: bodies, chunks, box slots and bytes on the GPU (the bench; walks them, so not every frame).</summary>
    public static (int Bodies, int Chunks, int Slots, long Bytes) MergedStats()
    {
        int bodies = 0, chunks = 0, slots = 0;
        long bytes = 0;
        foreach (var (_, s) in _merges)
        {
            if (!s.On)
                continue;
            bodies++;
            for (var k = 0; k < s.ChunkCount; k++)
            {
                chunks++;
                slots += s.Chunks[k].Gl.Slots;
                bytes += s.Chunks[k].Gl.Bytes;
            }
        }
        return (bodies, chunks, slots, bytes);
    }

    /// <summary>Starts a frame's merged drawing (the rebuild budget, the counters, the far detail level's scale).</summary>
    private static void BeginMergedFrame(Renderer gfx)
    {
        _mergeBudget = MergeBudgetBoxes;
        _mergeQueued = 0;
        LastMergedChunks = LastMergedBuilt = LastMergedWaiting = 0;
        LastLodDropped = LastWalkedCulled = 0;
        _lodEye = gfx.CameraPosition;
        _lodPixelsAt = MathF.Abs(gfx.Proj.M22) * gfx.ViewportHeight * 0.5f;
        OcclusionBegin(gfx);
    }

    private static Vector3 _lodEye;

    /// <summary>Merged box slots the far detail level left out last frame, and walked parts skipped off screen (the F9 window, the bench).</summary>
    public static int LastLodDropped { get; private set; }
    public static int LastWalkedCulled { get; private set; }

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
            ref var q = ref _mergeQueue[k];
            gfx.DrawMerged(q.Mesh, in q.Frame, q.Detail, q.Test && q.Use && _occlusionUse);
        }
        gfx.Relative = rel;
        gfx.EndMerged();
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
