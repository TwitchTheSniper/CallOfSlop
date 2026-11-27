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
/// drawn at its part's place and its leaf's move (ConstructDoors.DrawPose). A settled door's part moves its Pos, which the key below holds,
/// so a door settling merges the rest once, never a moving door each frame.
/// </para>
/// </summary>
public static partial class ConstructRender
{
    private sealed class DesignSet
    {
        public int Stamp = int.MinValue, Seen;
        public ulong Key;
        public Renderer.DesignGpu? Gpu; // (the parts that stand still)
        public readonly List<DesignMover> Movers = [];
    }

    /// <summary>A mesh of a design body that moves on its own: a moving segment's parts (Part -1), or one door part (or leaf).</summary>
    private sealed class DesignMover
    {
        public int Seg = -1, Part = -1;
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
            gfx.DrawDesign(gpu, body);
            LastDesignTris += gpu.Count / 3;
            n++;
        }
        foreach (var m in set.Movers)
        {
            if (m.Gpu is not { Count: > 0 } mg)
                continue;
            Matrix4x4 frame;
            if (m.Part >= 0)
            {
                if (m.Part >= c.PartCount)
                    continue;
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
            gfx.DrawDesign(mg, frame);
            LastDesignTris += mg.Count / 3;
            n++;
        }
        return n == 0 ? 0 : 1;
    }

    /// <summary>Once a frame: buffers of bodies gone or long unseen go.</summary>
    private static void DesignFrame(Renderer gfx, ConstructWorld world)
    {
        _designFrame++;
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
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (DesignParts.LookOf(c.Parts[i].D) is { Vertices: > 0 } l)
            {
                var g = DesignGroup(c, i);
                if (g == -1)
                    n += l.Vertices;
                else if (g == -2)
                    set.Movers.Add(new DesignMover { Part = i, Gpu = gfx.UploadDesign(null, l.Pos, l.Normal, l.Colour, l.Vertices) }); // (in its part's frame already)
                else if (!set.Movers.Exists(m => m.Seg == g))
                    set.Movers.Add(new DesignMover { Seg = g });
            }
        set.Gpu = Upload(gfx, c, set.Gpu, -1, n);
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

    /// <summary>The looks of the parts in group <paramref name="group"/> (<see cref="DesignGroup"/>) as one mesh in the body's rest frame.</summary>
    private static Renderer.DesignGpu? Upload(Renderer gfx, Construct c, Renderer.DesignGpu? into, int group, int n)
    {
        var pos = new float[n * 3];
        var nor = new float[n * 3];
        var col = new uint[n];
        var at = 0;
        for (var i = 0; i < c.PartCount && at < n; i++)
        {
            if (DesignParts.LookOf(c.Parts[i].D) is not { } l || DesignGroup(c, i) != group)
                continue;
            ref var p = ref c.Parts[i];
            c.RestPose(p.Seg, out var sq, out var st);
            var q = sq * Rot24.Quat(p.Rot);
            var t = st + Vector3.Transform(p.Pos, sq);
            for (var v = 0; v < l.Vertices && at < n; v++, at++)
            {
                var w = t + Vector3.Transform(new Vector3(l.Pos[v * 3], l.Pos[v * 3 + 1], l.Pos[v * 3 + 2]), q);
                var m = Vector3.Transform(new Vector3(l.Normal[v * 3], l.Normal[v * 3 + 1], l.Normal[v * 3 + 2]), q);
                (pos[at * 3], pos[at * 3 + 1], pos[at * 3 + 2]) = (w.X, w.Y, w.Z);
                (nor[at * 3], nor[at * 3 + 1], nor[at * 3 + 2]) = (m.X, m.Y, m.Z);
                col[at] = l.Colour[v];
            }
        }
        return gfx.UploadDesign(into, pos, nor, col, n);
    }
}
