using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ship design bodies (DesignParts.cs): each body's design elements merged into one mesh in its frame, drawn with the body's pose
/// (Renderer.Design.cs). The merge is made again only when the body's design parts change (a part shot off, a split): it is keyed to
/// <see cref="Construct.StructStamp"/> and, past that, to the parts it holds, so a wire or a setting doesn't remake it. A body gone (or
/// not drawn for <see cref="DesignForgetFrames"/> frames) lets its buffers go.
/// <para>
/// What moves is drawn on its own (the walkable designs plan, package F), so it moves without merging the ship again: the parts on each
/// moving segment (a lift's car, a turret) as one mesh in the body's rest frame, drawn at that segment's live pose (blended between ticks, as
/// every part on a joint is: ConstructRender.SegMatrix); and each door part and part on a door's leaf as its own mesh in its part's frame,
/// drawn at its part's place and its leaf's move (ConstructDoors.DrawPose), but only while that door moves (or a held one stands open).
/// Every other door part on the body's frame is drawn in one more merged mesh, the settled doors' (<see cref="DesignSet.DoorGpu"/>), made
/// again from the doors alone when a door starts or settles (<see cref="Construct.DoorPoseStamp"/>) or the set that moves changes, so
/// shut doors cost one draw a body, never one each, and a door moving never merges the ship again.
/// </para>
/// </summary>
public static partial class ConstructRender
{
    private sealed class DesignSet
    {
        public int Stamp = int.MinValue, Seen;
        public ulong Key;
        public Renderer.DesignGpu? Gpu; // (the parts that stand still)
        public Renderer.DesignGpu? GlassGpu; // (their glass: drawn blended after everything solid, DrawDesignGlass)
        public readonly List<DesignMover> Movers = [];
        public Renderer.DesignGpu? DoorGpu; // (the door parts on the body's frame that don't move now, merged)
        public int DoorStamp = int.MinValue;
        public ulong DoorActive = ulong.MaxValue; // (a hash of the door movers drawn on their own when DoorGpu was made)
        public bool AnyDoor;
    }

    /// <summary>A mesh of a design body that moves on its own: a moving segment's parts (Part -1), or one door part (or leaf).</summary>
    private sealed class DesignMover
    {
        public int Seg = -1, Part = -1;
        public bool OnJoint; // (a door part on a moving segment: always drawn on its own, at the segment's live pose)
        public Renderer.DesignGpu? Gpu;
    }

    private const int DesignForgetFrames = 600;
    private static readonly Dictionary<Construct, DesignSet> _designs = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Construct> _designGone = new(8);
    private static int _designFrame;

    /// <summary>Triangles of design meshes drawn last frame (the F9 window can read it).</summary>
    public static int LastDesignTris;

    /// <summary>Design meshes merged again since the start (a check can read how often a door or a lift remade the ship's mesh).</summary>
    public static int DesignMerges;

    /// <summary>Draws body <paramref name="c"/>'s design elements, if it has any, at <paramref name="body"/> (its moving parts at <paramref name="alpha"/> between ticks). Returns the draws.</summary>
    private static int DrawDesignBody(Renderer gfx, Construct c, in Matrix4x4 body, float alpha)
    {
        if (!c.HasDesign)
            return 0;
        if (!_designs.TryGetValue(c, out var set))
            _designs[c] = set = new DesignSet();
        set.Seen = _designFrame;
        if (set.Stamp != c.StructStamp)
        {
            set.Stamp = c.StructStamp;
            var key = DesignKey(c);
            if (key != set.Key || set.Gpu == null)
            {
                set.Key = key;
                MergeDesign(gfx, c, set);
            }
        }
        var n = 0;
        if (set.Gpu is { } gpu)
        {
            gfx.DrawDesign(gpu, body, depthFirst: true); // (its depth first: lit once a pixel, Renderer.DrawDesign)
            LastDesignTris += gpu.Count / 3;
            n++;
        }
        if (set.GlassGpu is { Count: > 0 } glass && _designGlassCount < _designGlass.Length)
            _designGlass[_designGlassCount++] = (glass, body); // (its windows: after everything solid, DrawGlass)
        if (set.AnyDoor)
        {
            var act = DoorActiveHash(c, set);
            if (act != set.DoorActive || set.DoorStamp != c.DoorPoseStamp)
            {
                (set.DoorActive, set.DoorStamp) = (act, c.DoorPoseStamp);
                set.DoorGpu = UploadDoors(gfx, c, set);
            }
            if (set.DoorGpu is { Count: > 0 } dg)
            {
                gfx.DrawDesign(dg, body, depthFirst: true);
                LastDesignTris += dg.Count / 3;
                n++;
            }
        }
        foreach (var m in set.Movers)
        {
            if (m.Gpu is not { Count: > 0 } mg)
                continue;
            Matrix4x4 frame;
            if (m.Part >= 0)
            {
                if (m.Part >= c.PartCount || !m.OnJoint && c.MovingDoorOf(m.Part) < 0)
                    continue; // (settled: in the doors' merged mesh)
                ref var p = ref c.Parts[m.Part];
                // Its part's place, on its segment, moved by its door's leaf while the door moves (as ConstructRender.DrawPart does).
                var place = Matrix4x4.CreateFromQuaternion(Rot24.Quat(p.Rot)) * Matrix4x4.CreateTranslation(p.Pos);
                var seg = p.Seg > 0 && p.Seg <= c.JointCount ? SegMatrix(c, p.Seg, alpha) : Matrix4x4.Identity;
                if ((c.DoorMovingCount | c.DoorHeldCount) > 0 && c.MovingDoorOf(m.Part) is var door and >= 0)
                    seg = ConstructDoors.DrawPose(c, door) * seg;
                frame = place * seg * body;
            }
            else
            {
                if (m.Seg > c.JointCount)
                    continue;
                // The segment's rest frame (where its mesh was merged) to where it is now.
                c.RestPose(m.Seg, out var rq, out var rt);
                Matrix4x4.Invert(Matrix4x4.CreateFromQuaternion(rq) * Matrix4x4.CreateTranslation(rt), out var back);
                frame = back * SegMatrix(c, m.Seg, alpha) * body;
            }
            gfx.DrawDesign(mg, frame, depthFirst: true);
            LastDesignTris += mg.Count / 3;
            n++;
        }
        return n == 0 ? 0 : 1;
    }

    // The design bodies' glass this frame (their still parts' windows), drawn by DrawGlass once everything solid is down.
    private static readonly (Renderer.DesignGpu Gpu, Matrix4x4 Body)[] _designGlass = new (Renderer.DesignGpu, Matrix4x4)[64];
    private static int _designGlassCount;

    /// <summary>
    /// The design bodies' windows (Karl, 2026-11-27: they were drawn solid, a pale panel inside and out): blended over what is behind them,
    /// with depth writes off, as DrawGlass leaves the state. Each body's glass is one mesh; the bodies go farthest first.
    /// </summary>
    private static void DrawDesignGlass(Renderer gfx, Vector3 eye)
    {
        var n = _designGlassCount;
        _designGlassCount = 0;
        if (n == 0)
            return;
        if (n > 1)
            Array.Sort(_designGlass, 0, n, DesignGlassFar.Eye(eye));
        for (var k = 0; k < n; k++)
        {
            gfx.DrawDesign(_designGlass[k].Gpu, _designGlass[k].Body);
            _designGlass[k] = default;
        }
    }

    private sealed class DesignGlassFar : IComparer<(Renderer.DesignGpu Gpu, Matrix4x4 Body)>
    {
        private static readonly DesignGlassFar One = new();
        private Vector3 _eye;

        public static DesignGlassFar Eye(Vector3 eye)
        {
            One._eye = eye;
            return One;
        }

        public int Compare((Renderer.DesignGpu Gpu, Matrix4x4 Body) a, (Renderer.DesignGpu Gpu, Matrix4x4 Body) b) =>
            Vector3.DistanceSquared(b.Body.Translation, _eye).CompareTo(Vector3.DistanceSquared(a.Body.Translation, _eye));
    }

    /// <summary>Once a frame: buffers of bodies gone or long unseen go.</summary>
    private static void DesignFrame(Renderer gfx, ConstructWorld world)
    {
        _designFrame++;
        _designGlassCount = 0;
        LastDesignTris = 0;
        if (_designs.Count == 0 || (_designFrame & 63) != 0)
            return;
        foreach (var (c, set) in _designs)
            if (_designFrame - set.Seen > DesignForgetFrames || c.PartCount == 0 || !world.Bodies.Contains(c))
                _designGone.Add(c);
        foreach (var c in _designGone)
        {
            var set = _designs[c];
            if (set.Gpu is { } g)
                gfx.FreeDesign(g);
            if (set.GlassGpu is { } gg)
                gfx.FreeDesign(gg);
            FreeMovers(gfx, set);
            _designs.Remove(c);
        }
        _designGone.Clear();
    }

    private static void FreeMovers(Renderer gfx, DesignSet set)
    {
        foreach (var m in set.Movers)
            if (m.Gpu is { } g)
                gfx.FreeDesign(g);
        set.Movers.Clear();
        if (set.DoorGpu is { } dg)
            gfx.FreeDesign(dg);
        set.DoorGpu = null;
        set.AnyDoor = false;
        set.DoorActive = ulong.MaxValue;
    }

    /// <summary>Which door movers are drawn on their own now (moving, or a held one open): a hash of their parts. One compare a mover while none moves.</summary>
    private static ulong DoorActiveHash(Construct c, DesignSet set)
    {
        var h = 14695981039346656037ul;
        foreach (var m in set.Movers)
            if (m.Part >= 0 && !m.OnJoint && m.Part < c.PartCount && c.MovingDoorOf(m.Part) >= 0)
                h = (h ^ (uint)m.Part) * 1099511628211ul;
        return h;
    }

    /// <summary>The door parts on the body's frame that don't move now, at their settled places, as one mesh in its rest frame (reusing <see cref="DesignSet.DoorGpu"/>).</summary>
    private static Renderer.DesignGpu? UploadDoors(Renderer gfx, Construct c, DesignSet set)
    {
        var n = 0;
        foreach (var m in set.Movers)
            if (Settled(c, m) && DesignParts.LookOf(c.Parts[m.Part].D) is { } l)
                n += l.Vertices;
        var pos = new float[n * 3];
        var nor = new float[n * 3];
        var col = new uint[n];
        var at = 0;
        foreach (var m in set.Movers)
            if (Settled(c, m) && DesignParts.LookOf(c.Parts[m.Part].D) is { } l)
                at = AddLook(c, m.Part, l, pos, nor, col, at, n);
        return gfx.UploadDesign(set.DoorGpu, pos, nor, col, n);

        static bool Settled(Construct c, DesignMover m) => m.Part >= 0 && !m.OnJoint && m.Part < c.PartCount && c.MovingDoorOf(m.Part) < 0;
    }

    private static ulong DesignKey(Construct c)
    {
        var h = 14695981039346656037ul;
        for (var i = 0; i < c.PartCount; i++)
        {
            ref var p = ref c.Parts[i];
            if (!p.D.IsNamedRuntime || !DesignParts.IsName(p.D.Name))
                continue;
            h = (h ^ p.Def) * 1099511628211ul;
            h = (h ^ (uint)p.Pos.GetHashCode()) * 1099511628211ul;
            h = (h ^ p.Rot ^ (ulong)p.Seg << 8) * 1099511628211ul;
        }
        return h;
    }

    /// <summary>
    /// Where a design part is drawn: -1 with the parts that stand still (the body, a freeform weld's segment, a fixed joint), its own mesh
    /// (-2: a door part or a part on a door's leaf), else the moving segment it rides (its parts merged together).
    /// </summary>
    private static int DesignGroup(Construct c, int i)
    {
        ref var p = ref c.Parts[i];
        if (p.D.Door != null || c.EnsureDoors() && c.DoorOfListed(i) >= 0)
            return -2;
        var seg = c.MoveSeg(p.Seg);
        return seg == 0 ? -1 : seg;
    }

    /// <summary>The body's design elements' looks: the still ones into one mesh in its rest frame, the moving ones apart (<see cref="DesignMover"/>), to the GPU.</summary>
    private static void MergeDesign(Renderer gfx, Construct c, DesignSet set)
    {
        DesignMerges++;
        FreeMovers(gfx, set);
        int n = 0, ng = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (DesignParts.LookOf(c.Parts[i].D) is { Vertices: > 0 } l)
            {
                var g = DesignGroup(c, i);
                if (g == -1)
                {
                    n += l.Vertices - l.GlassVertices;
                    ng += l.GlassVertices;
                }
                else if (g == -2)
                {
                    set.Movers.Add(new DesignMover { Part = i, OnJoint = c.MoveSeg(c.Parts[i].Seg) != 0, Gpu = gfx.UploadDesign(null, l.Pos, l.Normal, l.Colour, l.Vertices) }); // (in its part's frame already)
                    set.AnyDoor = true;
                }
                else if (!set.Movers.Exists(m => m.Seg == g))
                    set.Movers.Add(new DesignMover { Seg = g });
            }
        set.Gpu = Upload(gfx, c, set.Gpu, -1, n, Glass.Solid);
        set.GlassGpu = ng > 0 || set.GlassGpu != null ? Upload(gfx, c, set.GlassGpu, -1, ng, Glass.Only) : null; // (its windows, drawn blended)
        foreach (var m in set.Movers)
            if (m.Part < 0)
            {
                var k = 0;
                for (var i = 0; i < c.PartCount; i++)
                    if (DesignParts.LookOf(c.Parts[i].D) is { } l && DesignGroup(c, i) == m.Seg)
                        k += l.Vertices;
                m.Gpu = Upload(gfx, c, null, m.Seg, k);
            }
    }

    /// <summary>Which of a look's vertices a mesh takes: all (a moving part's glass is drawn with it, solid), the solid ones, or the glass.</summary>
    private enum Glass : byte { All, Solid, Only }

    /// <summary>The looks of the parts in group <paramref name="group"/> (<see cref="DesignGroup"/>) as one mesh in the body's rest frame.</summary>
    private static Renderer.DesignGpu? Upload(Renderer gfx, Construct c, Renderer.DesignGpu? into, int group, int n, Glass which = Glass.All)
    {
        var pos = new float[n * 3];
        var nor = new float[n * 3];
        var col = new uint[n];
        var at = 0;
        for (var i = 0; i < c.PartCount && at < n; i++)
        {
            if (DesignParts.LookOf(c.Parts[i].D) is not { } l || DesignGroup(c, i) != group)
                continue;
            at = AddLook(c, i, l, pos, nor, col, at, n, which);
        }
        return gfx.UploadDesign(into, pos, nor, col, n);
    }

    /// <summary>Part <paramref name="i"/>'s look at its place in the body's rest frame, from vertex <paramref name="at"/> on (at most <paramref name="n"/>): the next free one.</summary>
    private static int AddLook(Construct c, int i, DesignParts.Look l, float[] pos, float[] nor, uint[] col, int at, int n, Glass which = Glass.All)
    {
        ref var p = ref c.Parts[i];
        c.RestPose(p.Seg, out var sq, out var st);
        var q = sq * Rot24.Quat(p.Rot);
        var t = st + Vector3.Transform(p.Pos, sq);
        for (var v = 0; v < l.Vertices && at < n; v++)
        {
            if (which != Glass.All && l.GlassVertices > 0 && DesignParts.IsGlass(l.Colour[v]) != (which == Glass.Only))
                continue;
            var w = t + Vector3.Transform(new Vector3(l.Pos[v * 3], l.Pos[v * 3 + 1], l.Pos[v * 3 + 2]), q);
            var m = Vector3.Transform(new Vector3(l.Normal[v * 3], l.Normal[v * 3 + 1], l.Normal[v * 3 + 2]), q);
            (pos[at * 3], pos[at * 3 + 1], pos[at * 3 + 2]) = (w.X, w.Y, w.Z);
            (nor[at * 3], nor[at * 3 + 1], nor[at * 3 + 2]) = (m.X, m.Y, m.Z);
            col[at] = which == Glass.All ? l.Colour[v] | 0xff000000u : l.Colour[v]; // (glass drawn with the solid parts is solid)
            at++;
        }
        return at;
    }
}
