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
        public Renderer.MergedMesh? Dorm; // its idle machine blocks' work boxes (ConstructRenderBatch: DormAfter)
        public bool Ever; // built at least once (before that, Centre and Radius are a rough sphere round its parts' places: WaitUnbuilt)
        public bool DormDirty, DormWake;
        public byte[] SlotMask = []; // slot k's faces covered by another box of the chunk (bit 2 * axis + side, side 1 the + face: HideCovered)
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
        public int[] ExtraClass = []; // a merged machine block's biggest work box's size class (FarGlow)
        public Vector4[] WalkedSphere = [], ExtraSphere = []; // each one's sphere in the body's frame, for the view test; radius below 0: it moves (WalkedSeen)
        public Vector4[] WalkedSeen = [], ExtraSeen = []; // a moving one's sphere as last drawn, in the body's frame (radius 0: not drawn yet)
        public int[] WalkedDrawn = [], ExtraDrawn = []; // the frame (_mergeFrame) each walked part was last drawn in
        public int[] WalkedChunk = []; // the chunk whose occlusion box holds it (-1: none, -2: not looked for yet)

        // Idle machine blocks (DormAfter): each extra's work boxes as last drawn (in the body's frame), how many frames they stayed
        // the same, whether they are awake (0), waiting to go into their chunk's Dorm mesh (2) or drawn from it (1), and that chunk.
        public Renderer.Captured[][] ExtraCap = [];
        public int[] ExtraCapN = [], ExtraSame = [], ExtraChunk = [];
        public byte[] ExtraState = [];
        public int ChunkMapStamp = -1;
    }

    private struct MergeDraw
    {
        public Renderer.MergedMesh Mesh;
        public Matrix4x4 Frame;
        public int Detail; // the smallest size class drawn (the far detail level)
        public float Away; // its distance from the eye (FlushMerged draws nearest first)
        public bool Test; // drawn only where last frame's occlusion query saw its box (ConstructRenderOcclusion.cs)
        public bool Use; // the eye has hardly moved against this chunk since that query: its result still speaks for it
        public Vector3 Min, Max; // the query's box, in the frame
        public Vector3 Eye; // the eye in the chunk's frame (the CPU query's answer is kept with it)
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
        _defWork.Clear();
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
        var split = WorkSplits(d);
        if (d.Role == PartRole.Joint || d.IsRail || d.HasRig || d.HasDial || d.IsBeacon || d.IsTerminal || d.IsSpawner
            || d.HasWork && !split || d.HasRouterBoxes || d.HasEngineBoxes || d.Rotor != null || d.TrackRays > 0 || d.Logic is CLogic.AnalogLever || d.Surface is { Control: > 0 })
            n = -1;
        else
        {
            var panes = false;
            for (var b = 0; b < d.Boxes.Length; b++)
            {
                ref readonly var box = ref d.Boxes[b];
                if (box.Rig != BoxRig.None || box.Shows != 0 || box.Spin || box.Roll || box.IsLeaf || box.Pump != 0)
                {
                    n = -1;
                    break;
                }
                panes |= box.IsGlass && !box.Hidden;
                if (!box.Hidden && !box.IsGlass && !(split && WorkBox(d, b)))
                    n++;
            }
            if (panes && d.Logic == CLogic.Lamp)
                n = -1; // (a lit lamp's glass glows with the rest of it: DrawPart's order, so it is walked)
            if (panes && n >= 0)
                _defPanes.Add(d); // (by its boxes as they are now, not HasGlass: a run-time def's boxes arrive later)
            if (split && n >= 0)
                _defWork.Add(d);
        }
        _defBoxes[d] = n;
        return n;
    }

    private static readonly HashSet<ConstructPartDef> _defWork = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ConstructPartDef>); // merged types whose "work" boxes and status lamp are drawn alone

    /// <summary>
    /// A machine block's cell, a Roller Tile or a Crane Rail Block: a type whose only moving or changing boxes are its "work"
    /// boxes and its status lamp, everything else standing still in the cell. Such a type merges its other boxes into the
    /// body's chunks and only those are drawn part by part (<see cref="MergeSet.Extra"/>, DrawPart with <see cref="_workOnly"/>).
    /// A works is thousands of them (the Zero Day Works: 1,442 Mast Blocks, each 4 lamps among some 60 boxes), and walked
    /// whole they cost most of its frame. A Core, a Gantry Carriage and a tier piece stay walked whole: their looks draw more
    /// than their work boxes (a blank on the bed, the carriage on its rail).
    /// </summary>
    private static bool WorkSplits(ConstructPartDef d) =>
        d.HasWork && (d.BlockRole is BlockRole.Block or BlockRole.Shared or BlockRole.End || d.IsRollerTile || d.CraneRail)
        && d.Logic != CLogic.Lamp && !d.IsLaserCutter && !d.IsPanelFormer && !d.IsExtruder && !d.ShellBody && !d.IsOrdnance;

    /// <summary>A box of a <see cref="WorkSplits"/> type drawn part by part: its work boxes and its status lamp.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool WorkBox(ConstructPartDef d, int b) => d.Boxes[b].Work != BoxWork.None || b == d.StatusBox;

    /// <summary>Is box <paramref name="b"/> of this type in its part's merged chunk?</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool InChunk(ConstructPartDef d, int b) => !d.Boxes[b].Hidden && !d.Boxes[b].IsGlass && !(_defWork.Contains(d) && WorkBox(d, b));

    /// <summary>
    /// Boxes part <paramref name="i"/> puts in a merged mesh, or -1: it is walked whole. A part that holds stored parts or has
    /// glass panes merges its own opaque boxes all the same; what it holds and its panes are drawn alone (<see cref="MergeSet.Extra"/>).
    /// </summary>
    private static int MergeBoxes(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (c.Growing(i))
            return -1; // (a piece growing on a die: walked, drawn at its length now, so its new length plans nothing: Construct.GrowStamp)
        if (p.Burn > 0 || c.GantryHold || c.MergeOf[i] != 0 || c.Rides(i) || c.PartScale[i] > 1 || c.EngineOf[i] != 0 || _wheelMark[i] == _wheelToken)
            return -1;
        if (ModulePullLook.Walks(c, i))
            return -1; // (a fitted module the physics gun strains on: walked, rocking on its seat, ModulePullLook.cs)
        if (p.D.ModPart != ModPart.None && ModuleSockets.HiddenLook(c, i))
            return -1; // (a hidden socket connector: walked, and DrawPart draws nothing)
        if (p.D.HasMiddle && ModuleSockets.FrameLook(c, i))
            return -1; // (an Opening socket's frame, or the plug on one: walked, DrawPart leaves its middle out)
        if (p.D.BlockRole == BlockRole.Block && ConstructPress.Shell(c, i, out _))
            return -1; // (a Press's clamshell segment: walked, swung about its hinge while the press works, ConstructPressShells.cs)
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
        if (ModulePullLook.Count > 0 && ModulePullLook.DynOf(c) is var pulled && pulled != 0)
            h = Mix(h, pulled); // (a module strained on by the physics gun is walked while it is; other bodies' keys stay as they were)
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            h = Mix(h, RuntimeHelpers.GetHashCode(p.D) | (p.Burn > 0 ? 1L << 40 : 0) | (p.Store != 0 ? 1L << 41 : 0) | (p.D.ShellBody ? (long)ShellFittedLooks.FittedMask(in p, p.D) << 42 : 0) | (p.D.HasSeams && i < c.BlockJoined.Length ? (long)c.BlockJoined[i] << 50 : 0)); // (a shell's fitted modules: walked once one is)
            if (p.D.Logic == CLogic.Lamp && ConstructConfig.Get(p, p.D, CfgKey.Blink, 0) > 0)
                h = Mix(h, i); // (a lamp set blinking is walked)
            else if (p.D.ModPart != ModPart.None && ModuleSockets.HiddenLook(c, i))
                h = Mix(h, ~i); // (a hidden socket connector is walked, drawing nothing)
            else if (p.D.HasMiddle && ModuleSockets.FrameLook(c, i))
                h = Mix(h, i * 7919L + 3); // (a frame is walked, its middle left out)
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
        if (p.Seg != 0 || p.Burn > 0 || d.HasWork && !d.WorkGlowOnly || d.Logic == CLogic.AnalogLever || d.Rotor != null || d.Role is PartRole.Joint or PartRole.Suspension || d.IsRail || d.HasRig || d.TrackRays > 0
            || _wheelMark[i] == _wheelToken || c.DoorCount > 0 && c.DoorOfListed(i) >= 0 || c.MergeOf[i] != 0 || c.EngineOf[i] != 0 || d.ShellBody
            || d.BlockRole == BlockRole.Block && ConstructPress.Shell(c, i, out _) // (a Press's shell swings out past its welded cell)
            || ModulePullLook.Walks(c, i) // (a module rocking under the physics gun's pull)
            || c.Growing(i)) // (a piece growing on a die: longer each step than the sphere it had when the body was planned)
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

    /// <summary>
    /// A captured box's size class (an idle machine block's work box in its chunk's idle mesh, DormBuild), as <see cref="SizeClass"/>
    /// takes a part box's: from its model's three axes (a box's are its sizes: <see cref="CPartBox.Model"/>), a glowing one two
    /// classes bigger. 2026-11-23: the idle meshes were drawn whole at every distance, 110 of a Mast Block's 115 boxes among them.
    /// </summary>
    private static int CapturedClass(in Renderer.Captured box)
    {
        ref readonly var m = ref box.Model;
        var a = new Vector3(m.M11, m.M12, m.M13).Length();
        var b = new Vector3(m.M21, m.M22, m.M23).Length();
        var c = new Vector3(m.M31, m.M32, m.M33).Length();
        var big = MathF.Max(a, MathF.Max(b, c));
        var mid = MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));
        var s = MathF.Sqrt(big * mid);
        var k = s >= 4f ? 0 : s >= 1f ? 1 : s >= 0.25f ? 2 : s >= 0.0625f ? 3 : 4;
        return box.Code < 0 ? Math.Max(0, k - 2) : k; // (Code -1: drawn glowing)
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
            if (c.Parts[i].Store != 0 || _defPanes.Contains(c.Parts[i].D) || _defWork.Contains(c.Parts[i].D))
                set.Extra[set.ExtraCount++] = i; // (its own opaque boxes merged; what it holds, its panes and its work boxes drawn alone)
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
        Grow(ref set.ExtraClass, set.ExtraCount);
        Grow(ref set.WalkedSeen, set.WalkedCount);
        Grow(ref set.WalkedDrawn, set.WalkedCount);
        Grow(ref set.WalkedChunk, set.WalkedCount);
        for (var k = 0; k < set.WalkedCount; k++)
        {
            set.WalkedSphere[k] = WalkSphere(c, set.Walked[k]);
            (set.WalkedSeen[k], set.WalkedDrawn[k], set.WalkedChunk[k]) = (Vector4.Zero, int.MinValue / 2, -2);
        }
        Grow(ref set.ExtraSeen, set.ExtraCount);
        Grow(ref set.ExtraDrawn, set.ExtraCount);
        Grow(ref set.ExtraCapN, set.ExtraCount);
        Grow(ref set.ExtraSame, set.ExtraCount);
        Grow(ref set.ExtraChunk, set.ExtraCount);
        Grow(ref set.ExtraState, set.ExtraCount);
        if (set.ExtraCap.Length < set.ExtraCount)
            Array.Resize(ref set.ExtraCap, Math.Max(set.ExtraCount, set.ExtraCap.Length * 3 / 2));
        set.ChunkMapStamp = -1; // (each extra's chunk is looked up again, and every chunk's idle mesh emptied: DormMap)
        for (var k = 0; k < set.ExtraCount; k++)
        {
            (set.ExtraSeen[k], set.ExtraDrawn[k], set.ExtraCapN[k], set.ExtraSame[k], set.ExtraState[k]) = (Vector4.Zero, int.MinValue / 2, 0, 0, 0);
            set.ExtraSphere[k] = WalkSphere(c, set.Extra[k]);
            set.ExtraClass[k] = WorkClass(c.Parts[set.Extra[k]].D);
        }
        var on = gfx != null && total >= MergeMinBoxes;
        if (!on)
        {
            for (var k = 0; k < set.ChunkCount; k++)
            {
                gfx?.FreeMerged(set.Chunks[k].Gl);
                if (set.Chunks[k].Dorm != null)
                    gfx?.FreeMerged(set.Chunks[k].Dorm!);
            }
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
            if (!ch.Ever)
                ch.Radius = 0; // (WaitUnbuilt's rough sphere is made again from the new members)
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
            {
                gfx!.FreeMerged(old[k].Gl);
                if (old[k].Dorm != null)
                    gfx.FreeMerged(old[k].Dorm!); // (its idle mesh with it: a kept chunk's is emptied by DormMap)
            }
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
            look[o + 3] = ch.SlotMask[s] == 0 ? code : code < 0 ? code - ch.SlotMask[s] * Renderer.MergeMaskStep : code + ch.SlotMask[s] * Renderer.MergeMaskStep; // (covered faces ride on the code: the shader drops them)
        }
        return n;
    }

    private static Vector3[] _bLo = [], _bHi = [];
    private static bool[] _bOcc = [];
    private static byte[] _bKind = []; // 0 a plain box, 1 a contoured shape, 2 left out already (a welded seam)
    private static int[] _gStart = new int[513], _gItems = [], _gFill = new int[512];

    /// <summary>Faces hidden and boxes left out by <see cref="HideCovered"/> in the chunks built so far (the bench, the F9 window).</summary>
    public static long HiddenFaces, HiddenBoxes;

    /// <summary>
    /// The faces of a chunk's <paramref name="n"/> slots that lie inside another of its plain opaque boxes, with that box's
    /// material beyond them, are marked in <see cref="MergeChunk.SlotMask"/> (the shader doesn't draw them); a slot whose six
    /// faces are all covered is left out at no size, as a welded seam box is. On the Zero Day works a fifth of all faces and a
    /// ninth of the boxes are inside others (2026-11-23). A face on another box's own face, both facing out, is never hidden, so
    /// two boxes in one place still show. Allocates nothing once its scratch has grown.
    /// </summary>
    private static void HideCovered(MergeChunk ch, int n)
    {
        if (n < 2)
            return;
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        for (var k = 0; k < n; k++)
            if (_bOcc[k])
            {
                lo = Vector3.Min(lo, _bLo[k]);
                hi = Vector3.Max(hi, _bHi[k]);
            }
        if (lo.X > hi.X)
            return;
        // Occluders into an 8 x 8 x 8 grid over their bounds (a box into every cell it overlaps).
        const int G = 8;
        var cell = Vector3.Max((hi - lo) / G, new Vector3(0.01f));
        Array.Clear(_gFill);
        var items = 0;
        for (var k = 0; k < n; k++)
            if (_bOcc[k])
            {
                Cells(_bLo[k], _bHi[k], lo, cell, out var a, out var b);
                for (var x = a.X; x <= b.X; x++)
                    for (var y = a.Y; y <= b.Y; y++)
                        for (var z = a.Z; z <= b.Z; z++)
                        {
                            _gFill[(x * G + y) * G + z]++;
                            items++;
                        }
            }
        Grow(ref _gItems, items);
        _gStart[0] = 0;
        for (var q = 0; q < G * G * G; q++)
        {
            _gStart[q + 1] = _gStart[q] + _gFill[q];
            _gFill[q] = _gStart[q];
        }
        for (var k = 0; k < n; k++)
            if (_bOcc[k])
            {
                Cells(_bLo[k], _bHi[k], lo, cell, out var a, out var b);
                for (var x = a.X; x <= b.X; x++)
                    for (var y = a.Y; y <= b.Y; y++)
                        for (var z = a.Z; z <= b.Z; z++)
                            _gItems[_gFill[(x * G + y) * G + z]++] = k;
            }
        const float e = 1e-4f;
        var xf = _mergeXform;
        for (var k = 0; k < n; k++)
        {
            var a0 = _bLo[k];
            var a1 = _bHi[k];
            if (_bKind[k] == 2)
                continue;
            var mask = 0;
            for (var f = 0; f < 6; f++)
            {
                var ax = f >> 1;
                var plus = (f & 1) != 0;
                var pl = plus ? a1[ax] : a0[ax];
                var mid = (a0 + a1) * 0.5f;
                mid[ax] = pl;
                if (mid.X < lo.X || mid.Y < lo.Y || mid.Z < lo.Z || mid.X > hi.X || mid.Y > hi.Y || mid.Z > hi.Z)
                    continue;
                var gx = Math.Clamp((int)((mid.X - lo.X) / cell.X), 0, G - 1);
                var gy = Math.Clamp((int)((mid.Y - lo.Y) / cell.Y), 0, G - 1);
                var gz = Math.Clamp((int)((mid.Z - lo.Z) / cell.Z), 0, G - 1);
                var q = (gx * G + gy) * G + gz;
                for (var t = _gStart[q]; t < _gStart[q + 1]; t++)
                {
                    var o = _gItems[t];
                    if (o == k)
                        continue;
                    var b0 = _bLo[o];
                    var b1 = _bHi[o];
                    // (its material beyond the face, outward: a face on the other's own face facing the same way stays)
                    if (plus ? pl < b0[ax] - e || pl >= b1[ax] - e : pl > b1[ax] + e || pl <= b0[ax] + e)
                        continue;
                    var o1 = ax == 0 ? 1 : 0;
                    var o2 = ax == 2 ? 1 : 2;
                    if (a0[o1] < b0[o1] - e || a1[o1] > b1[o1] + e || a0[o2] < b0[o2] - e || a1[o2] > b1[o2] + e)
                        continue;
                    mask |= 1 << f;
                    break;
                }
            }
            if (mask != 63 && _bKind[k] != 0)
                mask = 0;
            if (mask == 63)
            {
                var c = (a0 + a1) * 0.5f;
                var x = k * Renderer.MergeXformFloats;
                (xf[x], xf[x + 1], xf[x + 2], xf[x + 3], xf[x + 4], xf[x + 5], xf[x + 6], xf[x + 7], xf[x + 8], xf[x + 9], xf[x + 10], xf[x + 11]) = (0, 0, 0, c.X, 0, 0, 0, c.Y, 0, 0, 0, c.Z); // (inside others: kept at no size)
                HiddenBoxes++;
                mask = 0;
            }
            else
                HiddenFaces += System.Numerics.BitOperations.PopCount((uint)mask);
            ch.SlotMask[k] = (byte)mask;
        }
    }

    private static void Cells(Vector3 b0, Vector3 b1, Vector3 lo, Vector3 cell, out (int X, int Y, int Z) a, out (int X, int Y, int Z) b)
    {
        const int G = 8;
        a = (Math.Clamp((int)((b0.X - lo.X) / cell.X), 0, G - 1), Math.Clamp((int)((b0.Y - lo.Y) / cell.Y), 0, G - 1), Math.Clamp((int)((b0.Z - lo.Z) / cell.Z), 0, G - 1));
        b = (Math.Clamp((int)((b1.X - lo.X) / cell.X), 0, G - 1), Math.Clamp((int)((b1.Y - lo.Y) / cell.Y), 0, G - 1), Math.Clamp((int)((b1.Z - lo.Z) / cell.Z), 0, G - 1));
    }

    /// <summary>
    /// A chunk's boxes onto the GPU, grouped by shape (plain boxes first, then each contoured shape: one instanced draw a
    /// group), each slot's part and box kept so its look can be written again alone.
    /// </summary>
    private static void BuildChunk(Renderer gfx, Construct c, MergeChunk ch)
    {
        ch.Ever = true;
        var n = ch.Boxes;
        Grow(ref _mergeXform, n * Renderer.MergeXformFloats);
        Grow(ref _slotKey, n);
        Grow(ref ch.SlotPart, n);
        Grow(ref ch.SlotBox, n);
        Grow(ref ch.SlotMask, n);
        Grow(ref _bLo, n);
        Grow(ref _bHi, n);
        Grow(ref _bOcc, n);
        Grow(ref _bKind, n);
        var keys = _slotKey;
        var s = 0;
        for (var k = 0; k < ch.MemberCount; k++)
        {
            var i = ch.Members[k];
            var boxes = c.Parts[i].D.Boxes;
            // (BuildChunk's slot key: shape from bit 40, size class 36-39, member 8-35, box 0-7)
            for (var b = 0; b < boxes.Length; b++)
                if (InChunk(c.Parts[i].D, b))
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
            var gone = (box.Seam != 0 || box.InMachine || box.Bare) && box.SeamHidden(i < c.BlockJoined.Length ? c.BlockJoined[i] : (byte)0);
            if (gone)
                m = Matrix4x4.CreateScale(0f) * Matrix4x4.CreateTranslation(centre); // (a block machine's seam box on a welded face: kept in the chunk at no size, so the counts stand: MachineBlocks.cs)
            (_bLo[k], _bHi[k]) = (centre - half, centre + half);
            _bKind[k] = (byte)(gone ? 2 : shape == 0 ? 0 : 1); // (a contoured shape is only ever left out whole: its triangles' normals aren't the cube's faces)
            _bOcc[k] = !gone && shape == 0 && !box.Glow && !p.D.IsOrdnance && c.PartScale[i] <= 1; // (a plain opaque box that sits still: it may hide others' faces)
            ch.SlotMask[k] = 0;
            var o = k * Renderer.MergeXformFloats;
            xf[o] = m.M11; xf[o + 1] = m.M12; xf[o + 2] = m.M13; xf[o + 3] = m.M41;
            xf[o + 4] = m.M21; xf[o + 5] = m.M22; xf[o + 6] = m.M23; xf[o + 7] = m.M42;
            xf[o + 8] = m.M31; xf[o + 9] = m.M32; xf[o + 10] = m.M33; xf[o + 11] = m.M43;
        }
        HideCovered(ch, s);
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
        if (set.Stamp != c.StructStamp || set.Parts != c.PartCount || set.Doors != c.DoorEpoch) // (parts taken out and not yet rebuilt: the indices moved; the door list was built again)
        {
            c.EnsureDoors();
            Plan(gfx, set, c);
            set.Stamp = c.StructStamp;
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
                var undrawn = cull && !ch.Ever && WaitUnbuilt(c, ch, body, alpha, origin);
                Grow(ref _partWaiting, c.PartCount);
                if (undrawn)
                {
                    for (var m = 0; m < ch.MemberCount; m++)
                        if (ch.Members[m] < c.PartCount)
                            _partWaiting[ch.Members[m]] = _waitToken; // (its panes and machine work wait with it, not drawn floating)
                    continue;
                }
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
            if (ch.Gl.CpuQuery.Id != 0)
                gfx.PollCpuQuery(ch.Gl); // (the walked parts in its box go by its answer: WalkedHidden)
            ref var frame = ref _segFrame[ch.Seg];
            if (_segToken[ch.Seg] != _segTok)
            {
                frame = ch.Seg == 0 ? body : SegMatrix(c, ch.Seg, alpha) * body;
                _segToken[ch.Seg] = _segTok;
            }
            var detail = LodClasses - 1;
            var test = false;
            var use = false;
            var awayAt = 0f;
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
                awayAt = away;
                detail = DetailAt(away);
                test = OcclusionWanted(ch, away); // (ConstructRenderOcclusion.cs: big enough to be worth a query, and the eye not by it)
                if (test)
                    use = OcclusionEyeSteady(gfx, ch, in frame);
            }
            if (_mergeQueued == _mergeQueue.Length)
                Array.Resize(ref _mergeQueue, _mergeQueue.Length * 2); // (grows to the busiest frame once)
            _mergeQueue[_mergeQueued++] = new MergeDraw { Mesh = ch.Gl, Frame = frame, Detail = detail, Test = test, Use = use, Min = ch.Min, Max = ch.Max, Eye = ch.EyeAt, Away = awayAt };
            if (ch.Dorm is { Slots: > 0 } dorm)
            {
                if (_mergeQueued == _mergeQueue.Length)
                    Array.Resize(ref _mergeQueue, _mergeQueue.Length * 2);
                _mergeQueue[_mergeQueued++] = new MergeDraw { Mesh = dorm, Frame = frame, Detail = detail, Away = awayAt }; // (its idle machine blocks' work boxes: not tested, its own boxes' test speaks for the chunk; the chunk's far detail level, as its own boxes)
                var dormSlots = dorm.SlotsUpTo(detail);
                LastLodDropped += dorm.Slots - dormSlots;
            }
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
            // A part that moves is tested by where it was last drawn, a margin round it, and drawn anyway every WalkRefresh
            // frames so that stays true; one that burns is always drawn (its flames leave from it seen or not).
            var sphere = set.WalkedSphere[k];
            var moves = sphere.W < 0;
            if (moves)
                sphere = set.WalkedSeen[k] is { W: > 0 } seen && c.Parts[i].Burn <= 0 && _mergeFrame - set.WalkedDrawn[k] < WalkRefresh + (k & 3)
                    ? seen with { W = seen.W + WalkMargin } : new Vector4(0, 0, 0, -1);
            _walkDetail = LodClasses - 1;
            if (cull && sphere.W >= 0)
            {
                if (OffScreen(in sphere, in body, origin))
                {
                    LastWalkedCulled++;
                    continue;
                }
                if (WalkedHidden(gfx, set, k, in sphere))
                {
                    LastWalkedHidden++;
                    continue;
                }
                _walkDetail = DetailAt(Vector3.Distance(Vector3.Transform(new Vector3(sphere.X, sphere.Y, sphere.Z), body) + origin, _lodEye) - sphere.W);
            }
            HostCut(gfx, c, i);
            if (moves)
                gfx.TrackBegin();
            _fxEmitted = false;
            n += DrawPart(gfx, world, c, i, body, alpha, 1f);
            if (c.Parts[i].Store != 0)
                n += DrawStored(gfx, world, c, i, body, alpha);
            _walkDetail = LodClasses - 1;
            set.WalkedDrawn[k] = _mergeFrame;
            if (moves && gfx.TrackEnd(out var lo, out var hi) && Matrix4x4.Invert(body, out var toBody))
            {
                var mid = Vector3.Transform((lo + hi) * 0.5f, toBody); // (drawn in the body's matrix: back into its frame, where the next frames test it)
                set.WalkedSeen[k] = new Vector4(mid, _fxEmitted ? 0f : (hi - lo).Length() * 0.5f); // (smoking or sparking: drawn every frame, seen or not, as its plume rises past the walls)
            }
        }
        LastBatchWalked += set.WalkedCount;
        if (set.ChunkMapStamp != set.Stamp)
            DormMap(gfx, c, set);
        if (!Matrix4x4.Invert(body, out var invBody))
            invBody = Matrix4x4.Identity;
        for (var k = 0; k < set.ExtraCount; k++)
        {
            // Merged parts that hold stored parts or have panes: only those drawn here.
            var i = set.Extra[k];
            if (i >= c.PartCount)
                continue;
            var sphere = set.ExtraSphere[k];
            var moves = sphere.W < 0; // (a machine block's work: tested where it was last drawn, as walked parts that move are)
            if (moves)
                sphere = set.ExtraSeen[k] is { W: > 0 } seen && c.Parts[i].Burn <= 0 && _mergeFrame - set.ExtraDrawn[k] < WalkRefresh + (k & 3)
                    ? seen with { W = seen.W + WalkMargin } : new Vector4(0, 0, 0, -1);
            if (cull && OffScreen(in sphere, in body, origin))
                continue;
            HostCut(gfx, c, i);
            _fxEmitted = false;
            if (moves)
                gfx.TrackBegin();
            if (_defPanes.Contains(c.Parts[i].D) && !(_partWaiting.Length > i && _partWaiting[i] == _waitToken))
                n += DrawPanes(gfx, c, i, body, alpha); // (not while its chunk waits its rebuild: DrawPart drew its panes above)
            var asleep = false;
            if (_defWork.Contains(c.Parts[i].D) && !(_partWaiting.Length > i && _partWaiting[i] == _waitToken)
                && !(cull && c.Parts[i].D.WorkGlowOnly && FarGlow(in set.ExtraSphere[k], set.ExtraClass[k], in body, origin)))
            {
                var q = set.ExtraChunk[k];
                var dormOk = DormOn && q >= 0 && q < set.ChunkCount && !set.Chunks[q].Dirty && set.Chunks[q].Seg == 0 && c.Parts[i].Burn <= 0;
                _fxEmitted = false;
                if (!dormOk && set.ExtraState[k] != 0)
                {
                    // (burning, or its chunk waits a rebuild: walked, out of the idle mesh)
                    if (set.ExtraState[k] == 1 && q >= 0 && q < set.ChunkCount)
                        (set.Chunks[q].DormDirty, set.Chunks[q].DormWake) = (true, true);
                    (set.ExtraState[k], set.ExtraSame[k]) = (0, 0);
                }
                if (dormOk && set.ExtraState[k] == 1 && ((_mergeFrame + k) & (DormCheck - 1)) != 0)
                {
                    asleep = true; // (idle: its work boxes are in its chunk's idle mesh; looked at again every DormCheck frames)
                    LastDormant++;
                }
                else
                {
                    _workOnly = true; // (its work boxes and status lamp, at the machine's state; the rest is in its chunk)
                    var checking = dormOk && set.ExtraState[k] == 1; // (asleep, its check frame: looked at without drawing, as its idle mesh draws it)
                    if (dormOk)
                        gfx.CaptureBegin(in invBody, draw: !checking);
                    var drew = DrawPart(gfx, world, c, i, body, alpha, 1f);
                    if (dormOk && DormLook(gfx, set, k, q, _fxEmitted) && checking)
                    {
                        LastDormant++;
                        asleep = true;
                    }
                    else if (checking)
                        drew = DrawPart(gfx, world, c, i, body, alpha, 1f); // (woke: drawn as it is now; its chunk's idle mesh is made again without it this frame)
                    n += drew;
                    _workOnly = false;
                }
            }
            if (c.Parts[i].Store != 0)
                n += DrawStored(gfx, world, c, i, body, alpha);
            if (asleep)
            {
                if (moves)
                    gfx.TrackEnd(out _, out _);
                continue;
            }
            set.ExtraDrawn[k] = _mergeFrame;
            if (moves)
            {
                if (gfx.TrackEnd(out var lo, out var hi) && Matrix4x4.Invert(body, out var toBody))
                    set.ExtraSeen[k] = new Vector4(Vector3.Transform((lo + hi) * 0.5f, toBody), _fxEmitted ? 0f : (hi - lo).Length() * 0.5f); // (smoking: drawn every frame, its plume rises seen or not)
                else
                    set.ExtraSeen[k] = Vector4.Zero; // (nothing drawn: a far glow, a chunk still waiting)
            }
        }
        for (var q = 0; q < set.ChunkCount; q++)
            if (set.Chunks[q].DormDirty && (set.Chunks[q].DormWake || _dormBudget-- > 0))
                DormBuild(gfx, set, q);
        drawn += n;
        return true;
    }

    /// <summary>
    /// Is a merged machine block's glow-only work (a Mast Block's corner lamps) so far off that the far detail level leaves
    /// out boxes of its biggest work box's size class (<paramref name="cls"/>, <see cref="DetailAt"/>)? Then it isn't drawn:
    /// from there it is under a pixel, as the chunk's own boxes that size are.
    /// </summary>
    private static bool FarGlow(in Vector4 sphere, int cls, in Matrix4x4 body, Vector3 origin)
    {
        if (sphere.W < 0)
            return false;
        var at = Vector3.Transform(new Vector3(sphere.X, sphere.Y, sphere.Z), body) + origin;
        return DetailAt(Vector3.Distance(at, _lodEye) - sphere.W) < cls;
    }

    /// <summary>The size class of a type's biggest work box or status lamp (its smallest class number), or the last class when it has none.</summary>
    private static int WorkClass(ConstructPartDef d)
    {
        var cls = LodClasses - 1;
        if (!_defWork.Contains(d))
            return cls;
        for (var b = 0; b < d.Boxes.Length; b++)
            if (!d.Boxes[b].Hidden && WorkBox(d, b))
                cls = Math.Min(cls, SizeClass(in d.Boxes[b], false));
        return cls;
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
        _dormBudget = DormBudget;
        _mergeQueued = 0;
        LastMergedChunks = LastMergedBuilt = LastMergedWaiting = 0;
        LastLodDropped = LastWalkedCulled = LastWalkedHidden = LastDormant = LastMergedUnbuiltWaiting = 0;
        _lodEye = gfx.CameraPosition;
        _lodPixelsAt = MathF.Abs(gfx.Proj.M22) * gfx.ViewportHeight * 0.5f;
        OcclusionBegin(gfx);
    }

    private static Vector3 _lodEye;

    /// <summary>Merged box slots the far detail level left out last frame, and walked parts skipped off screen and behind walls (the F9 window, the bench).</summary>
    public static int LastLodDropped { get; private set; }
    public static int LastWalkedCulled { get; private set; }
    public static int LastWalkedHidden { get; private set; }

    /// <summary>
    /// Walked parts that move (machines' work, rigs, doors, joints): tested against the view by the sphere they were last drawn
    /// in grown by <see cref="WalkMargin"/> metres, and drawn anyway at least every <see cref="WalkRefresh"/> frames (a few more,
    /// spread by part) so that sphere stays true: at 8 frames a piece would have to move faster than 15 m/s to leave it.
    /// </summary>
    public const float WalkMargin = 1.5f;
    public const int WalkRefresh = 8;

    /// <summary>The smallest size class the walked part being drawn draws (the far detail level, as merged chunks have it; the last class: all).</summary>
    private static int _walkDetail = LodClasses - 1;

    /// <summary>
    /// A chunk never built yet (a body just loaded or come into view: every chunk of the Zero Day works at once, 2026-11-23, a
    /// 95 ms first frame as 250,000 boxes were walked part by part while the budget built four chunks) is drawn walked only when
    /// it is on screen and within <see cref="WaitNear"/>; past that it waits undrawn for its turn, with its panes and machine work
    /// (<see cref="MergeBudgetBoxes"/> a frame: about 30 frames for the whole works). True when it waits undrawn.
    /// </summary>
    private static bool WaitUnbuilt(Construct c, MergeChunk ch, in Matrix4x4 body, float alpha, Vector3 origin)
    {
        if (ch.Radius <= 0)
        {
            var mn = new Vector3(float.MaxValue);
            var mx = new Vector3(float.MinValue);
            for (var m = 0; m < ch.MemberCount; m++)
                if (ch.Members[m] < c.PartCount)
                {
                    mn = Vector3.Min(mn, c.Parts[ch.Members[m]].Pos);
                    mx = Vector3.Max(mx, c.Parts[ch.Members[m]].Pos);
                }
            if (mn.X > mx.X)
                return true;
            (ch.Centre, ch.Radius) = ((mn + mx) * 0.5f, (mx - mn).Length() * 0.5f + WaitPad); // (rough: parts' places, padded by a big part's reach)
        }
        var frame = ch.Seg == 0 ? body : SegMatrix(c, ch.Seg, alpha) * body;
        var at = Vector3.Transform(ch.Centre, frame) + origin;
        for (var q = 0; q < 6; q++)
        {
            var pl = _planes[q];
            if (pl.X * at.X + pl.Y * at.Y + pl.Z * at.Z + pl.W < -ch.Radius)
                return true;
        }
        if (Vector3.Distance(at, _lodEye) - ch.Radius <= WaitNear)
            return false;
        LastMergedUnbuiltWaiting++;
        return true;
    }

    public const float WaitNear = 24f, WaitPad = 6f;

    /// <summary>Chunks not built yet left undrawn last frame (far or off screen, waiting their first build: WaitUnbuilt).</summary>
    public static int LastMergedUnbuiltWaiting { get; private set; }

    // ---- Idle machine blocks (2026-11-23: on the Zero Day works their work boxes, walked one block at a time, were 4 ms of a 5 ms
    // constructs pass, most of them standing still) ----------------------------------------------------------------------------

    /// <summary>
    /// A machine block whose work boxes (and status lamp) came out the same for <see cref="DormAfter"/> frames goes into its
    /// chunk's idle mesh (<see cref="MergeChunk.Dorm"/>, drawn with the chunk in one call a shape) and isn't walked; every
    /// <see cref="DormCheck"/> frames it is drawn again as before and compared, and if anything changed (it started working,
    /// its lamp changed colour, it was painted) it leaves the idle mesh that frame. So a machine starts to move at most
    /// DormCheck frames late. At most <see cref="DormBudget"/> chunks' idle meshes are made again a frame for blocks going idle;
    /// a block waking always makes its chunk's again at once. <c>--no-idle-merge</c> turns it off.
    /// </summary>
    public static bool DormOn = true;
    public const int DormAfter = 30, DormCheck = 8, DormBudget = 4;
    private static int _dormBudget;

    /// <summary>Machine blocks drawn from idle meshes last frame (the F9 window, the bench).</summary>
    public static int LastDormant { get; private set; }

    private static int[] _partChunk = [];
    private static Renderer.Captured[] _dormScratch = [];
    private static long[] _dormKey = [];

    /// <summary>After a plan: each extra's chunk (the chunk its part is a member of), and every chunk's idle mesh emptied.</summary>
    private static void DormMap(Renderer gfx, Construct c, MergeSet set)
    {
        set.ChunkMapStamp = set.Stamp;
        Grow(ref _partChunk, c.PartCount);
        Array.Fill(_partChunk, -1, 0, c.PartCount);
        for (var q = 0; q < set.ChunkCount; q++)
        {
            var ch = set.Chunks[q];
            for (var m = 0; m < ch.MemberCount; m++)
                if (ch.Members[m] < c.PartCount)
                    _partChunk[ch.Members[m]] = q;
            if (ch.Dorm is { Slots: > 0 } dorm)
                gfx.FreeMerged(dorm);
            ch.DormDirty = ch.DormWake = false;
        }
        for (var k = 0; k < set.ExtraCount; k++)
            set.ExtraChunk[k] = set.Extra[k] < c.PartCount ? _partChunk[set.Extra[k]] : -1;
    }

    /// <summary>
    /// Extra <paramref name="k"/> was just drawn (or, asleep, only looked at) and captured: kept, compared with last time, and put
    /// to sleep or woken. A block that made smoke or sparks this frame (<paramref name="emitted"/>) counts as changed: its plume
    /// only rises while it is drawn. True when it is the same as before.
    /// </summary>
    private static bool DormLook(Renderer gfx, MergeSet set, int k, int q, bool emitted)
    {
        var ok = gfx.CaptureEnd(out var boxes);
        var cap = set.ExtraCap[k];
        var same = ok && !emitted && boxes.Length > 0 && cap != null && set.ExtraCapN[k] == boxes.Length;
        for (var b = 0; same && b < boxes.Length; b++)
            same = Same(in cap![b], in boxes[b]);
        if (!same)
        {
            // (kept only when it changed: a slow crane creeping under a millimetre a frame is compared with where its run of
            // like frames began, so it stays awake rather than sleeping and jumping)
            if (cap == null || cap.Length < boxes.Length)
                set.ExtraCap[k] = cap = new Renderer.Captured[Math.Max(boxes.Length, 4)]; // (once a block, grown to its most boxes)
            boxes.CopyTo(cap);
            set.ExtraCapN[k] = ok ? boxes.Length : 0;
        }
        var ch = set.Chunks[q];
        if (set.ExtraState[k] != 0)
        {
            if (same)
                return true;
            // Woke: out of the idle mesh this very frame (it was drawn walked above), and awake until it settles again.
            var wasIn = set.ExtraState[k] == 1;
            (set.ExtraState[k], set.ExtraSame[k]) = (0, 0);
            if (wasIn)
                (ch.DormDirty, ch.DormWake) = (true, true);
            return false;
        }
        set.ExtraSame[k] = same ? set.ExtraSame[k] + 1 : 0;
        if (set.ExtraSame[k] >= DormAfter)
        {
            set.ExtraState[k] = 2; // (walked until its chunk's idle mesh is made again with it)
            ch.DormDirty = true;
        }
        return same;
    }

    private static bool Same(in Renderer.Captured a, in Renderer.Captured b)
    {
        if (a.Shape != b.Shape || a.Code != b.Code || Vector3.DistanceSquared(a.Color, b.Color) > 1e-8f)
            return false;
        ref readonly var m = ref a.Model;
        ref readonly var n = ref b.Model;
        var turn = MathF.Abs(m.M11 - n.M11) + MathF.Abs(m.M12 - n.M12) + MathF.Abs(m.M13 - n.M13) + MathF.Abs(m.M21 - n.M21) + MathF.Abs(m.M22 - n.M22)
            + MathF.Abs(m.M23 - n.M23) + MathF.Abs(m.M31 - n.M31) + MathF.Abs(m.M32 - n.M32) + MathF.Abs(m.M33 - n.M33);
        var at = MathF.Abs(m.M41 - n.M41) + MathF.Abs(m.M42 - n.M42) + MathF.Abs(m.M43 - n.M43);
        return turn < 1e-4f && at < 1e-3f; // (the body's matrix and back again rounds a little: under a millimetre is the same)
    }

    /// <summary>Chunk <paramref name="q"/>'s idle mesh made again from its extras that sleep (or wait to), sorted by shape.</summary>
    private static void DormBuild(Renderer gfx, MergeSet set, int q)
    {
        var ch = set.Chunks[q];
        (ch.DormDirty, ch.DormWake) = (false, false);
        var n = 0;
        for (var k = 0; k < set.ExtraCount; k++)
            if (set.ExtraChunk[k] == q && set.ExtraState[k] != 0 && set.ExtraCap[k] is { } cap)
            {
                set.ExtraState[k] = 1;
                Grow(ref _dormScratch, n + set.ExtraCapN[k]);
                for (var b = 0; b < set.ExtraCapN[k]; b++)
                    _dormScratch[n++] = cap[b];
            }
        ch.Dorm ??= new Renderer.MergedMesh();
        var gl = ch.Dorm;
        if (n == 0)
        {
            gl.Slots = gl.Groups = 0;
            return;
        }
        Grow(ref _dormKey, n);
        for (var b = 0; b < n; b++)
            _dormKey[b] = (long)_dormScratch[b].Shape << 36 | (long)CapturedClass(in _dormScratch[b]) << 32 | (uint)b; // (shape, then its size class biggest first, as a chunk's slots: the far detail level draws a group's first slots)
        Array.Sort(_dormKey, 0, n);
        Grow(ref _mergeXform, n * Renderer.MergeXformFloats);
        Grow(ref _mergeLook, n * Renderer.MergeLookFloats);
        var xf = _mergeXform;
        var look = _mergeLook;
        gl.Groups = 0;
        gl.ClassCount = LodClasses;
        var last = -1;
        for (var s = 0; s < n; s++)
        {
            ref readonly var box = ref _dormScratch[(int)(_dormKey[s] & 0xFFFFFFFF)];
            if (box.Shape != last)
            {
                Grow(ref gl.GroupShape, gl.Groups + 1);
                Grow(ref gl.GroupFirst, gl.Groups + 1);
                Grow(ref gl.GroupCount, gl.Groups + 1);
                Grow(ref gl.GroupClassEnd, (gl.Groups + 1) * LodClasses);
                for (var q2 = 0; q2 < LodClasses; q2++)
                    gl.GroupClassEnd[gl.Groups * LodClasses + q2] = 0;
                (gl.GroupShape[gl.Groups], gl.GroupFirst[gl.Groups], gl.GroupCount[gl.Groups]) = (box.Shape, s, 0);
                gl.Groups++;
                last = box.Shape;
            }
            gl.GroupCount[gl.Groups - 1]++;
            for (var q2 = (int)(_dormKey[s] >> 32 & 0xF); q2 < LodClasses; q2++)
                gl.GroupClassEnd[(gl.Groups - 1) * LodClasses + q2]++; // (slots of class q2 or bigger: the group's first that many)
            ref readonly var m = ref box.Model;
            var o = s * Renderer.MergeXformFloats;
            xf[o] = m.M11; xf[o + 1] = m.M12; xf[o + 2] = m.M13; xf[o + 3] = m.M41;
            xf[o + 4] = m.M21; xf[o + 5] = m.M22; xf[o + 6] = m.M23; xf[o + 7] = m.M42;
            xf[o + 8] = m.M31; xf[o + 9] = m.M32; xf[o + 10] = m.M33; xf[o + 11] = m.M43;
            var l = s * Renderer.MergeLookFloats;
            (look[l], look[l + 1], look[l + 2], look[l + 3]) = (box.Color.X, box.Color.Y, box.Color.Z, box.Code);
        }
        gfx.UploadMergedXforms(gl, xf, n);
        gfx.UploadMergedLooks(gl, look, n);
    }

    /// <summary>
    /// Is walked part <paramref name="k"/> (its <paramref name="sphere"/> in the body's frame) inside a merged chunk's occlusion box
    /// that the CPU query last found hidden, with the eye still where it was asked from? Then it isn't drawn: a part in a room
    /// behind a wall costs nothing, as the room's merged boxes don't. Only chunks of the body's own frame (segment 0) hold parts.
    /// </summary>
    private static bool WalkedHidden(Renderer gfx, MergeSet set, int k, in Vector4 sphere)
    {
        if (!_occlusionUse)
            return false;
        var q = set.WalkedChunk[k];
        if (q >= 0 && (q >= set.ChunkCount || !Holds(set.Chunks[q], in sphere)))
            q = set.WalkedChunk[k] = -2;
        if (q == -2)
        {
            q = -1;
            for (var j = 0; j < set.ChunkCount && q < 0; j++)
                if (set.Chunks[j].Seg == 0 && !set.Chunks[j].Dirty && Holds(set.Chunks[j], in sphere))
                    q = j;
            set.WalkedChunk[k] = q;
        }
        if (q < 0)
            return false;
        var ch = set.Chunks[q];
        var m = ch.Gl;
        return m.CpuHidden && m.CpuAt >= 0 && gfx.OcclusionFrame - m.CpuAt <= 6 && ch.EyeFrame == gfx.OcclusionFrame
            && Vector3.DistanceSquared(ch.EyeAt, m.CpuEye) < 1f;
    }

    /// <summary>Does a chunk's occlusion box (its bounds and the query's pad) hold a sphere wholly?</summary>
    private static bool Holds(MergeChunk ch, in Vector4 sphere)
    {
        var at = new Vector3(sphere.X, sphere.Y, sphere.Z);
        var r = new Vector3(sphere.W);
        return ch.Radius > 0 && Vector3.Min(at - r, ch.Min - new Vector3(OcclusionPad)) == ch.Min - new Vector3(OcclusionPad)
            && Vector3.Max(at + r, ch.Max + new Vector3(OcclusionPad)) == ch.Max + new Vector3(OcclusionPad);
    }

    /// <summary>The chunks queued this frame, in one run of the merged shader (opaque, so their order with the box batch doesn't matter).</summary>
    private static float[] _mergeOrderKey = [];
    private static int[] _mergeOrder = [];

    private static void FlushMerged(Renderer gfx)
    {
        if (_mergeQueued == 0)
            return;
        gfx.BeginMerged();
        var rel = gfx.Relative;
        gfx.Relative = true; // (the frames are relative to the render origin already)
        // Nearest first (2026-11-23): the depth test then throws away what nearer chunks hide before its costly lighting runs.
        Grow(ref _mergeOrderKey, _mergeQueued);
        Grow(ref _mergeOrder, _mergeQueued);
        for (var k = 0; k < _mergeQueued; k++)
            (_mergeOrderKey[k], _mergeOrder[k]) = (_mergeQueue[k].Away, k);
        Array.Sort(_mergeOrderKey, _mergeOrder, 0, _mergeQueued);
        for (var o = 0; o < _mergeQueued; o++)
        {
            ref var q = ref _mergeQueue[_mergeOrder[o]];
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
            {
                gfx.FreeMerged(s.Chunks[k].Gl);
                if (s.Chunks[k].Dorm != null)
                    gfx.FreeMerged(s.Chunks[k].Dorm!);
            }
            _merges.Remove(c);
        }
        _mergeGone.Clear();
        MergedBytes = bytes;
    }
}
