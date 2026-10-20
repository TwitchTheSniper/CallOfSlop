using System.Numerics;

namespace CallOfSlop;

/// <summary>One flattened draw: a world matrix and what to draw with it.</summary>
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
/// Flattens visible scene nodes into a flat array of <see cref="DrawItem"/>s (depth first, children in order, so the draw
/// order is exactly the order the parts were built in) and submits them to the existing <see cref="Renderer"/>.
/// The array only grows (doubling) and is reused, so a warmed-up list never allocates.
///
/// Today every item is one <see cref="Renderer.Draw"/>; the list is also the natural input for instanced batching
/// (sort by mesh and state, upload the matrices) once the mesh pipeline lands.
/// </summary>
public sealed class DrawList
{
    private DrawItem[] _items = new DrawItem[256];

    public int Count { get; private set; }

    /// <summary>Mesh items skipped by the last <see cref="Submit"/> because no mesh drawer was given.</summary>
    public int SkippedMeshes { get; private set; }

    public Span<DrawItem> Items => _items.AsSpan(0, Count);

    public void Clear() => Count = 0;

    /// <summary>Appends an item and returns it to fill in.</summary>
    public ref DrawItem Add()
    {
        if (Count == _items.Length)
            Array.Resize(ref _items, _items.Length * 2);
        return ref _items[Count++];
    }

    /// <summary>Appends a box (or mesh) item.</summary>
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

    /// <summary>
    /// Appends every visible drawable in <paramref name="root"/>'s subtree. World matrices must be up to date
    /// (<see cref="Scene.UpdateTransforms"/>). Hidden nodes skip their whole subtree.
    /// </summary>
    public void Flatten(SceneNode root)
    {
        if ((root.Flags & NodeFlags.Hidden) != 0)
            return;
        if (root.Renderable.Kind != RenderKind.None)
            Add(root._world, root.Renderable);
        root.Custom?.Emit(this, root);
        var kids = root._kids;
        for (var i = 0; i < root._kidCount; i++)
            Flatten(kids[i]);
    }

    /// <summary>
    /// Draws every item in order. Assumes (and leaves) blending off and depth writes on, the state the opaque passes run
    /// in; switches only when an item needs something else.
    /// </summary>
    public void Submit(Renderer g, IMeshDrawer? meshes = null)
    {
        var blend = false;
        var depthWrite = true;
        var skipped = 0;
        var items = _items;
        for (var i = 0; i < Count; i++)
        {
            ref readonly var it = ref items[i];
            var wantBlend = (it.Flags & RenderFlags.Blend) != 0;
            var wantDepth = (it.Flags & RenderFlags.NoDepthWrite) == 0;
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
            if (it.MeshId < 0)
                g.Draw(it.World, it.Color, it.Alpha, it.Mode, it.Material);
            else if (meshes != null)
                meshes.DrawMesh(it.MeshId, it.World, it.Color, it.Alpha, it.Mode, it.Material);
            else
                skipped++;
        }
        if (!depthWrite)
            g.SetDepthWrite(true);
        if (blend)
            g.SetBlend(false);
        SkippedMeshes = skipped;
    }
}
