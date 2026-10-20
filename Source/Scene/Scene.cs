using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>
/// A registry of scene-graph roots plus the queue of dirty subtrees. Setting a node's local transform queues it (unless
/// it's already dirty); <see cref="UpdateTransforms"/> recomputes each queued subtree once, from its topmost dirty
/// ancestor, so a frame costs only what moved. Deterministic: the queue is processed in the order nodes were dirtied,
/// and nothing depends on hashing or allocation order.
/// </summary>
public sealed class Scene
{
    private readonly List<SceneNode> _roots = new();
    private SceneNode[] _dirty = new SceneNode[64];
    private int _dirtyCount;

    /// <summary>Nodes recomputed by the last <see cref="UpdateTransforms"/>.</summary>
    public int LastUpdated { get; private set; }

    /// <summary>The scene's roots, in the order they were created.</summary>
    public IReadOnlyList<SceneNode> Roots => _roots;

    /// <summary>Creates a root node in this scene.</summary>
    public SceneNode CreateRoot(string name, NodeFlags flags = NodeFlags.None)
    {
        var n = new SceneNode(name, flags);
        AddRoot(n);
        return n;
    }

    /// <summary>Adds an existing tree (detached from any parent) as a root of this scene.</summary>
    public void AddRoot(SceneNode root)
    {
        if (root.Parent != null)
            root.Detach();
        if (!_roots.Contains(root))
            _roots.Add(root);
        root.SetSceneRecursive(this); // queues any already-dirty nodes
        root.MarkDirty();
    }

    /// <summary>Removes a root (and its subtree) from the scene.</summary>
    public void RemoveRoot(SceneNode root)
    {
        _roots.Remove(root);
        root.SetSceneRecursive(null);
    }

    internal void ForgetRoot(SceneNode root) => _roots.Remove(root);

    internal void Enqueue(SceneNode n)
    {
        if (_dirtyCount == _dirty.Length)
        {
            // Drop entries already cleaned by DrawList.UpdateAndFlatten before growing, so the queue stays as small as
            // the number of dirty nodes even if UpdateTransforms is called rarely.
            var k = 0;
            for (var i = 0; i < _dirtyCount; i++)
                if (_dirty[i]._dirty && ReferenceEquals(_dirty[i]._scene, this))
                    _dirty[k++] = _dirty[i];
            Array.Clear(_dirty, k, _dirtyCount - k);
            _dirtyCount = k;
            if (_dirtyCount > _dirty.Length / 2)
                Array.Resize(ref _dirty, _dirty.Length * 2);
        }
        _dirty[_dirtyCount++] = n;
    }

    /// <summary>Recomputes the world matrices of every dirty subtree. Returns the number of nodes updated.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public int UpdateTransforms()
    {
        var total = 0;
        for (var i = 0; i < _dirtyCount; i++)
        {
            var n = _dirty[i];
            _dirty[i] = null!;
            if (!n._dirty || !ReferenceEquals(n._scene, this))
                continue; // already done as part of an ancestor's subtree, or moved to another scene
            var top = n;
            for (var a = n._parent; a != null; a = a._parent)
                if (a._dirty)
                    top = a;
            total += top.UpdateWorld();
        }
        _dirtyCount = 0;
        LastUpdated = total;
        return total;
    }

    /// <summary>The first node called <paramref name="name"/> under any root (depth first, roots in order), or null.</summary>
    public SceneNode? Find(string name)
    {
        foreach (var r in _roots)
            if (r.Find(name) is { } hit)
                return hit;
        return null;
    }

    /// <summary>Total number of nodes under all roots.</summary>
    public int CountNodes()
    {
        var n = 0;
        foreach (var r in _roots)
            n += r.CountNodes();
        return n;
    }
}
