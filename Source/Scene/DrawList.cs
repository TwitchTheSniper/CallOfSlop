using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>One draw: a world matrix and what to draw with it.</summary>
public struct DrawItem
{
    public Matrix4x4 World;
    public Vector3 Color;
    public float Alpha;

    /// <summary>-1 = the renderer's unit cube; otherwise a mesh id for the <see cref="IMeshDrawer"/>.</summary>
    public int MeshId;

    public byte Mode;
    public Material Material;
    public RenderFlags Flags;
}

/// <summary>
/// Flattens visible scene nodes into a draw list (depth first, children in order, so the draw order is exactly the order
/// the parts were built in) and submits it to the existing <see cref="Renderer"/>.
///
/// A node's entry is just a reference to the node: its world matrix and <see cref="Renderable"/> are read at
/// <see cref="Submit"/> time, so flattening copies 8 bytes per visible node instead of a whole matrix. That means a list
/// is valid until the next <see cref="Scene.UpdateTransforms"/> or renderable change (flatten, then submit). Custom
/// <see cref="IRenderable"/>s add self-contained items (<see cref="Add(in Matrix4x4, in Renderable)"/>). The arrays only grow
/// and are reused, so a warmed-up list never allocates. Today every entry is one <see cref="Renderer.Draw"/>; the list is
/// also the natural input for instanced batching once the mesh pipeline lands.
/// </summary>
public sealed class DrawList
{
    private SceneNode?[] _nodes = new SceneNode?[256]; // the node to draw, or null for a self-contained item in _items
    private DrawItem[] _items = new DrawItem[256];

    public int Count { get; private set; }

    /// <summary>Mesh items skipped by the last <see cref="Submit"/> because no mesh drawer was given.</summary>
    public int SkippedMeshes { get; private set; }

    public void Clear() => Count = 0;

    private void Grow()
    {
        Array.Resize(ref _nodes, _nodes.Length * 2);
        Array.Resize(ref _items, _nodes.Length);
    }

    /// <summary>Appends a self-contained item and returns it to fill in (for <see cref="IRenderable"/>s).</summary>
    public ref DrawItem Add()
    {
        if (Count == _nodes.Length)
            Grow();
        _nodes[Count] = null;
        return ref _items[Count++];
    }

    /// <summary>Appends a self-contained box (or mesh) item.</summary>
    public void Add(in Matrix4x4 world, in Renderable r)
    {
        ref var it = ref Add();
        it.World = world;
        it.Color = r.Color;
        it.Alpha = r.Alpha;
        it.MeshId = r.Kind == RenderKind.Mesh ? r.MeshId : -1;
        it.Mode = r.Mode;
        it.Material = r.Material;
        it.Flags = r.Flags;
    }

    /// <summary>The i-th entry as a self-contained item (copies; for tests and tools).</summary>
    public DrawItem Item(int i)
    {
        if ((uint)i >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(i));
        if (_nodes[i] is not { } n)
            return _items[i];
        ref readonly var r = ref n.Renderable;
        return new DrawItem
        {
            World = n._world, Color = r.Color, Alpha = r.Alpha, MeshId = r.Kind == RenderKind.Mesh ? r.MeshId : -1, Mode = r.Mode, Material = r.Material,
            Flags = r.Flags,
        };
    }

    /// <summary>
    /// Appends every visible drawable in <paramref name="root"/>'s subtree. World matrices must be up to date
    /// (<see cref="Scene.UpdateTransforms"/>). Hidden nodes skip their whole subtree.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Flatten(SceneNode root)
    {
        var st = _walk;
        var bottom = st.Count;
        st.Push(root, false, true);
        while (st.Count > bottom)
        {
            var n = st.Items[--st.Count].Node;
            if ((n.Flags & NodeFlags.Hidden) != 0)
                continue;
            if (n.Renderable.Kind != RenderKind.None)
                AddNode(n);
            n.Custom?.Emit(this, n);
            var kids = n._kids;
            for (var i = n._kidCount - 1; i >= 0; i--)
                st.Push(kids[i], false, true);
        }
    }

    private readonly WalkStack _walk = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AddNode(SceneNode n)
    {
        if (Count == _nodes.Length)
            Grow();
        _nodes[Count++] = n;
    }

    /// <summary>
    /// <see cref="Scene.UpdateTransforms"/> for <paramref name="root"/>'s subtree and <see cref="Flatten"/> in a single walk
    /// (each node is touched once, which is what a frame costs: memory, not maths). Dirty ancestors above the root are
    /// brought up to date first. Their queue entries are skipped by the next <see cref="Scene.UpdateTransforms"/>.
    /// Returns the number of nodes whose world matrix was recomputed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int UpdateAndFlatten(SceneNode root)
    {
        SceneNode? top = null;
        for (var a = root._parent; a != null; a = a._parent)
            if (a._dirty)
                top = a;
        var updated = top?.UpdateWorld() ?? 0;

        // One depth-first walk with an explicit stack (children pushed in reverse, so they come out in order).
        var st = _walk;
        var bottom = st.Count;
        st.Push(root, false, true);
        while (st.Count > bottom)
        {
            ref readonly var e = ref st.Items[--st.Count];
            var n = e.Node;
            var force = e.Force;
            var visible = e.Visible;
            if (force || n._dirty)
            {
                n.Recompute();
                force = true;
                updated++;
            }
            if ((n.Flags & NodeFlags.Hidden) != 0)
                visible = false;
            if (visible)
            {
                if (n.Renderable.Kind != RenderKind.None)
                    AddNode(n);
                n.Custom?.Emit(this, n); // may flatten more into this list (the stack is re-entrant)
            }
            var kids = n._kids;
            for (var i = n._kidCount - 1; i >= 0; i--)
                st.Push(kids[i], force, visible);
        }
        return updated;
    }

    /// <summary>
    /// Draws every entry in order. Assumes (and leaves) blending off and depth writes on, the state the opaque passes run
    /// in; switches only when an entry needs something else.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public void Submit(Renderer g, IMeshDrawer? meshes = null)
    {
        var blend = false;
        var depthWrite = true;
        var skipped = 0;
        var nodes = _nodes;
        for (var i = 0; i < Count; i++)
        {
            var n = nodes[i];
            RenderFlags flags;
            int mesh;
            if (n != null)
            {
                flags = n.Renderable.Flags;
                mesh = n.Renderable.Kind == RenderKind.Mesh ? n.Renderable.MeshId : -1;
            }
            else
            {
                flags = _items[i].Flags;
                mesh = _items[i].MeshId;
            }
            var wantBlend = (flags & RenderFlags.Blend) != 0;
            var wantDepth = (flags & RenderFlags.NoDepthWrite) == 0;
            if (wantBlend != blend)
            {
                g.SetBlend(wantBlend);
                blend = wantBlend;
            }
            if (wantDepth != depthWrite)
            {
                g.SetDepthWrite(wantDepth);
                depthWrite = wantDepth;
            }
            if (mesh >= 0 && meshes == null)
            {
                skipped++;
                continue;
            }
            if (n != null)
            {
                ref readonly var r = ref n.Renderable;
                if (mesh < 0)
                    g.Draw(n._world, r.Color, r.Alpha, r.Mode, r.Material);
                else
                    meshes!.DrawMesh(mesh, n._world, r.Color, r.Alpha, r.Mode, r.Material);
            }
            else
            {
                ref readonly var it = ref _items[i];
                if (mesh < 0)
                    g.Draw(it.World, it.Color, it.Alpha, it.Mode, it.Material);
                else
                    meshes!.DrawMesh(mesh, it.World, it.Color, it.Alpha, it.Mode, it.Material);
            }
        }
        if (!depthWrite)
            g.SetDepthWrite(true);
        if (blend)
            g.SetBlend(false);
        SkippedMeshes = skipped;
    }
}
