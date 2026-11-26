using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Ship design bodies (DesignParts.cs): each body's design elements merged into one mesh in its frame, drawn with the body's pose
/// (Renderer.Design.cs). The merge is made again only when the body's design parts change (a part shot off, a split): it is keyed to
/// <see cref="Construct.StructStamp"/> and, past that, to the parts it holds, so a wire or a setting doesn't remake it. A body gone (or
/// not drawn for <see cref="DesignForgetFrames"/> frames) lets its buffers go.
/// </summary>
public static partial class ConstructRender
{
    private sealed class DesignSet
    {
        public int Stamp = int.MinValue, Seen;
        public ulong Key;
        public Renderer.DesignGpu? Gpu;
    }

    private const int DesignForgetFrames = 600;
    private static readonly Dictionary<Construct, DesignSet> _designs = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Construct> _designGone = new(8);
    private static int _designFrame;

    /// <summary>Triangles of design meshes drawn last frame (the F9 window can read it).</summary>
    public static int LastDesignTris;

    /// <summary>Draws body <paramref name="c"/>'s design elements, if it has any, at <paramref name="body"/>. Returns the triangles drawn.</summary>
    private static int DrawDesignBody(Renderer gfx, Construct c, in Matrix4x4 body)
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
                set.Gpu = MergeDesign(gfx, c, set.Gpu);
            }
        }
        if (set.Gpu is not { } gpu)
            return 0;
        gfx.DrawDesign(gpu, body);
        LastDesignTris += gpu.Count / 3;
        return 1;
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
            if (_designs[c].Gpu is { } g)
                gfx.FreeDesign(g);
            _designs.Remove(c);
        }
        _designGone.Clear();
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

    /// <summary>The body's design elements' looks into one mesh in its rest frame, to the GPU.</summary>
    private static Renderer.DesignGpu? MergeDesign(Renderer gfx, Construct c, Renderer.DesignGpu? into)
    {
        var n = 0;
        for (var i = 0; i < c.PartCount; i++)
            if (DesignParts.LookOf(c.Parts[i].D) is { } l)
                n += l.Vertices;
        var pos = new float[n * 3];
        var nor = new float[n * 3];
        var col = new uint[n];
        var at = 0;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (DesignParts.LookOf(c.Parts[i].D) is not { } l)
                continue;
            ref var p = ref c.Parts[i];
            c.RestPose(p.Seg, out var sq, out var st);
            var q = sq * Rot24.Quat(p.Rot);
            var t = st + Vector3.Transform(p.Pos, sq);
            for (var v = 0; v < l.Vertices; v++, at++)
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
