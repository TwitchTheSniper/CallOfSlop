using System.Numerics;
using System.Runtime.CompilerServices;

namespace CallOfSlop;

/// <summary>What kind of node a <see cref="SceneNode"/> is, beyond its transform.</summary>
[Flags]
public enum NodeFlags : byte
{
    None = 0,

    /// <summary>Not drawn, and neither are its children (the transform still updates).</summary>
    Hidden = 1,

    /// <summary>An attachment point ("muzzle", "grip", "optic_mount", "hand_l", "seat_0"...): a named frame, never drawn.</summary>
    Socket = 2,

    /// <summary>
    /// A gravity frame: the node's local +Y is "up" for everything under it (see <see cref="SceneNode.GravityUp"/>).
    /// Lets a later zone, planet or vehicle have its own up without anything hard-coding world +Y.
    /// </summary>
    GravityFrame = 4,
}

/// <summary>
/// One node of the presentation scene graph: a local transform (position / rotation / scale, or a whole matrix), a parent,
/// children, a cached world matrix, and optionally something to draw (<see cref="Renderable"/>, or a custom
/// <see cref="IRenderable"/>).
///
/// Conventions are System.Numerics' (row vectors, as the renderer uses): local = S * R * T, and
/// <c>World = Local * Parent.World</c>. Setting a local transform marks the node dirty and queues it on its
/// <see cref="Scene"/>; <see cref="Scene.UpdateTransforms"/> then recomputes just the dirty subtrees. Nothing allocates per
/// frame: children live in a small array that only grows while a tree is being built.
/// </summary>
public sealed class SceneNode
{
    private static readonly SceneNode[] NoChildren = [];

    /// <summary>The node's name, for lookups ("muzzle", "upper_r"...). Need not be unique.</summary>
    public readonly string Name;

    /// <summary>Flags (hidden, socket, gravity frame).</summary>
    public NodeFlags Flags;

    /// <summary>What this node draws, if anything (a box today, a mesh id from the mesh pipeline later).</summary>
    public Renderable Renderable;

    /// <summary>Optional custom drawable (called during <see cref="DrawList.Flatten"/> after <see cref="Renderable"/>).</summary>
    public IRenderable? Custom;

    /// <summary>Free slot for the owner (gameplay object, rig...). Never read by the scene graph.</summary>
    public object? Tag;

    internal SceneNode? _parent;
    internal SceneNode[] _kids = NoChildren;
    internal int _kidCount;
    internal Scene? _scene;

    private Vector3 _pos;
    private Quaternion _rot = Quaternion.Identity;
    private Vector3 _scale = Vector3.One;
    internal Matrix4x4 _local = Matrix4x4.Identity;
    internal Matrix4x4 _world = Matrix4x4.Identity;
    internal bool _localStale; // TRS changed; _local must be recomposed
    internal bool _dirty; // the world matrix of this subtree is out of date

    /// <summary>Bumped every time the world matrix is recomputed (lets consumers skip unchanged nodes).</summary>
    public int WorldVersion { get; internal set; }

    public SceneNode(string name, NodeFlags flags = NodeFlags.None)
    {
        Name = name;
        Flags = flags;
    }

    // ---- Hierarchy ------------------------------------------------------------------------

    /// <summary>The parent, or null for a root.</summary>
    public SceneNode? Parent => _parent;

    /// <summary>The scene this node belongs to (inherited from its parent), or null for a free-standing tree.</summary>
    public Scene? Scene => _scene;

    public int ChildCount => _kidCount;

    /// <summary>The i-th child, in the order they were added (which is also the draw order).</summary>
    public SceneNode Child(int i) => (uint)i < (uint)_kidCount ? _kids[i] : throw new ArgumentOutOfRangeException(nameof(i));

    /// <summary>Adds <paramref name="child"/> as the last child (detaching it from its old parent). Returns the child.</summary>
    public SceneNode Add(SceneNode child)
    {
        child.SetParent(this, keepWorld: false);
        return child;
    }

    /// <summary>Creates a child node and returns it.</summary>
    public SceneNode Add(string name, NodeFlags flags = NodeFlags.None) => Add(new SceneNode(name, flags));

    /// <summary>Creates a child node with a box (or mesh) to draw, its local transform given as a matrix.</summary>
    public SceneNode Add(string name, in Matrix4x4 local, in Renderable renderable)
    {
        var n = Add(name);
        n.SetLocalMatrix(local);
        n.Renderable = renderable;
        return n;
    }

    /// <summary>Adds an attachment point: a named, never drawn frame at <paramref name="position"/> / <paramref name="rotation"/>.</summary>
    public SceneNode AddSocket(string name, Vector3 position, Quaternion rotation)
    {
        var s = Add(name, NodeFlags.Socket);
        s.SetLocal(position, rotation, Vector3.One);
        return s;
    }

    /// <inheritdoc cref="AddSocket(string, Vector3, Quaternion)"/>
    public SceneNode AddSocket(string name, Vector3 position) => AddSocket(name, position, Quaternion.Identity);

    /// <summary>
    /// Moves this node under <paramref name="parent"/> (null = make it a root). With <paramref name="keepWorld"/> its local
    /// transform is changed so it stays exactly where it is in the world (uses the current, updated world matrices);
    /// otherwise it keeps its local transform and moves with the new parent.
    /// </summary>
    public void SetParent(SceneNode? parent, bool keepWorld)
    {
        if (ReferenceEquals(parent, _parent))
            return;
        for (var a = parent; a != null; a = a._parent)
            if (ReferenceEquals(a, this))
                throw new InvalidOperationException($"can't parent '{Name}' under its own descendant '{parent!.Name}'");

        Matrix4x4 keep = default;
        if (keepWorld)
        {
            keep = _world;
            if (parent != null)
            {
                if (!Matrix4x4.Invert(parent._world, out var inv))
                    inv = Matrix4x4.Identity;
                keep = _world * inv;
            }
        }

        if (_parent == null && parent != null)
            _scene?.ForgetRoot(this);
        _parent?.RemoveChild(this);
        _parent = parent;
        if (parent != null)
        {
            if (parent._kidCount == parent._kids.Length)
                Array.Resize(ref parent._kids, Math.Max(4, parent._kidCount * 2));
            parent._kids[parent._kidCount++] = this;
        }
        SetSceneRecursive(parent?._scene);

        if (keepWorld)
        {
            if (Matrix4x4.Decompose(keep, out var s, out var r, out var t))
                SetLocal(t, r, s);
            else
                SetLocalMatrix(keep);
        }
        MarkDirty();
    }

    /// <summary>Detaches this node from its parent (it becomes a free root; its scene keeps no reference to it).</summary>
    public void Detach() => SetParent(null, keepWorld: false);

    private void RemoveChild(SceneNode child)
    {
        for (var i = 0; i < _kidCount; i++)
            if (ReferenceEquals(_kids[i], child))
            {
                Array.Copy(_kids, i + 1, _kids, i, _kidCount - i - 1);
                _kids[--_kidCount] = null!;
                return;
            }
    }

    /// <summary>Removes every child (they become free roots).</summary>
    public void ClearChildren()
    {
        while (_kidCount > 0)
            _kids[_kidCount - 1].Detach();
    }

    internal void SetSceneRecursive(Scene? scene)
    {
        if (ReferenceEquals(_scene, scene))
            return;
        _scene = scene;
        if (scene != null && _dirty)
            scene.Enqueue(this);
        for (var i = 0; i < _kidCount; i++)
            _kids[i].SetSceneRecursive(scene);
    }

    // ---- Local transform --------------------------------------------------------------------

    public Vector3 LocalPosition
    {
        get => _pos;
        set
        {
            _pos = value;
            _localStale = true;
            MarkDirty();
        }
    }

    public Quaternion LocalRotation
    {
        get => _rot;
        set
        {
            _rot = value;
            _localStale = true;
            MarkDirty();
        }
    }

    public Vector3 LocalScale
    {
        get => _scale;
        set
        {
            _scale = value;
            _localStale = true;
            MarkDirty();
        }
    }

    /// <summary>Sets position, rotation and scale at once (local = S * R * T).</summary>
    public void SetLocal(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _pos = position;
        _rot = rotation;
        _scale = scale;
        _localStale = true;
        MarkDirty();
    }

    /// <summary>
    /// Sets the local transform as a whole matrix (any affine matrix, including shear from a non-uniform parent).
    /// <see cref="LocalPosition"/> / <see cref="LocalRotation"/> / <see cref="LocalScale"/> are not updated; setting any of
    /// them later switches back to TRS.
    /// </summary>
    public void SetLocalMatrix(in Matrix4x4 local)
    {
        _local = local;
        _localStale = false;
        MarkDirty();
    }

    /// <summary>The local matrix (child-to-parent), composed from TRS if needed.</summary>
    public Matrix4x4 LocalMatrix
    {
        get
        {
            if (_localStale)
                ComposeLocal();
            return _local;
        }
    }

    /// <summary>S * R * T without the three matrix products (bit-identical to them: the rows are only scaled).</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)] // hot: full JIT optimisation from the first call, no tier-0 phase
    private void ComposeLocal()
    {
        var m = Matrix4x4.CreateFromQuaternion(_rot);
        m.M11 *= _scale.X;
        m.M12 *= _scale.X;
        m.M13 *= _scale.X;
        m.M21 *= _scale.Y;
        m.M22 *= _scale.Y;
        m.M23 *= _scale.Y;
        m.M31 *= _scale.Z;
        m.M32 *= _scale.Z;
        m.M33 *= _scale.Z;
        m.M41 = _pos.X;
        m.M42 = _pos.Y;
        m.M43 = _pos.Z;
        _local = m;
        _localStale = false;
    }

    // ---- World transform ---------------------------------------------------------------------

    /// <summary>
    /// The cached world matrix (local-to-world). Up to date after <see cref="Scene.UpdateTransforms"/> (or
    /// <see cref="UpdateWorld"/> for a tree that isn't in a scene).
    /// </summary>
    public ref readonly Matrix4x4 World => ref _world;

    /// <summary>World position of the node's origin.</summary>
    public Vector3 WorldPosition => _world.Translation;

    /// <summary>The node's local +Y in world space (normalised): its "up".</summary>
    public Vector3 Up => SafeNormalize(new Vector3(_world.M21, _world.M22, _world.M23), Vector3.UnitY);

    /// <summary>The node's local -Z in world space (normalised): its "forward".</summary>
    public Vector3 Forward => SafeNormalize(-new Vector3(_world.M31, _world.M32, _world.M33), -Vector3.UnitZ);

    /// <summary>
    /// "Up" for this node: the up of the nearest <see cref="NodeFlags.GravityFrame"/> at or above it, or world +Y when
    /// there is none.
    /// </summary>
    public Vector3 GravityUp
    {
        get
        {
            for (var n = this; n != null; n = n._parent)
                if ((n.Flags & NodeFlags.GravityFrame) != 0)
                    return n.Up;
            return Vector3.UnitY;
        }
    }

    /// <summary>Transforms a point from this node's space to world space.</summary>
    public Vector3 ToWorld(Vector3 local) => Vector3.Transform(local, _world);

    /// <summary>Transforms a world point into this node's space.</summary>
    public Vector3 ToLocal(Vector3 world) => Matrix4x4.Invert(_world, out var inv) ? Vector3.Transform(world, inv) : world - _world.Translation;

    public bool Visible
    {
        get => (Flags & NodeFlags.Hidden) == 0;
        set => Flags = value ? Flags & ~NodeFlags.Hidden : Flags | NodeFlags.Hidden;
    }

    /// <summary>Marks the node's subtree as needing a world update and queues it on the scene.</summary>
    public void MarkDirty()
    {
        if (_dirty)
            return;
        _dirty = true;
        _scene?.Enqueue(this);
    }

    /// <summary>
    /// Recomputes this node's and all its descendants' world matrices from the parent's current world matrix. Returns the
    /// number of nodes updated. <see cref="Scene.UpdateTransforms"/> calls this for each dirty subtree.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)] // hot: full JIT optimisation from the first call, no tier-0 phase
    public int UpdateWorld()
    {
        // An explicit stack instead of recursion: no call (and SIMD register save) per node.
        var st = t_walk ??= new WalkStack();
        var bottom = st.Count;
        st.Push(this, true, false);
        var n = 0;
        while (st.Count > bottom)
        {
            var node = st.Items[--st.Count].Node;
            node.Recompute();
            n++;
            var kids = node._kids;
            for (var i = node._kidCount - 1; i >= 0; i--)
                st.Push(kids[i], true, false);
        }
        return n;
    }

    [ThreadStatic]
    private static WalkStack? t_walk;

    /// <summary>World = local * parent world (local composed first if its TRS changed); clears the dirty flag.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Recompute()
    {
        if (_localStale)
            ComposeLocal();
        if (_parent == null)
            _world = _local;
        else
            Multiply(_local, _parent._world, ref _world);
        _dirty = false;
        WorldVersion++;
    }

    // ---- Lookup and traversal ------------------------------------------------------------------

    /// <summary>The first direct child called <paramref name="name"/>, or null.</summary>
    public SceneNode? FindChild(string name)
    {
        for (var i = 0; i < _kidCount; i++)
            if (string.Equals(_kids[i].Name, name, StringComparison.Ordinal))
                return _kids[i];
        return null;
    }

    /// <summary>The first descendant (depth first, this node included) called <paramref name="name"/>, or null.</summary>
    public SceneNode? Find(string name)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal))
            return this;
        for (var i = 0; i < _kidCount; i++)
            if (_kids[i].Find(name) is { } hit)
                return hit;
        return null;
    }

    /// <summary>The first socket below this node called <paramref name="name"/>, or null.</summary>
    public SceneNode? FindSocket(string name)
    {
        for (var i = 0; i < _kidCount; i++)
        {
            var k = _kids[i];
            if ((k.Flags & NodeFlags.Socket) != 0 && string.Equals(k.Name, name, StringComparison.Ordinal))
                return k;
            if (k.FindSocket(name) is { } hit)
                return hit;
        }
        return null;
    }

    /// <summary>
    /// A name -> node map of this subtree (first node wins on duplicate names), for O(1) lookups on prefab instances.
    /// Allocates: build it once when the tree is built.
    /// </summary>
    public Dictionary<string, SceneNode> BuildIndex()
    {
        var map = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
        Index(map);
        return map;
    }

    private void Index(Dictionary<string, SceneNode> map)
    {
        map.TryAdd(Name, this);
        for (var i = 0; i < _kidCount; i++)
            _kids[i].Index(map);
    }

    /// <summary>
    /// Depth-first walk (parents before children, children in order). The visitor is a struct passed by reference, so
    /// nothing is boxed or allocated. <see cref="ISceneVisitor.Enter"/> returning false skips the node's children.
    /// </summary>
    public void Visit<TVisitor>(ref TVisitor visitor) where TVisitor : struct, ISceneVisitor
    {
        if (!visitor.Enter(this))
            return;
        for (var i = 0; i < _kidCount; i++)
            _kids[i].Visit(ref visitor);
        visitor.Leave(this);
    }

    /// <summary>Number of nodes in this subtree (this one included).</summary>
    public int CountNodes()
    {
        var n = 1;
        for (var i = 0; i < _kidCount; i++)
            n += _kids[i].CountNodes();
        return n;
    }

    public override string ToString() => $"{Name} ({_kidCount} children)";

    /// <summary>
    /// <paramref name="r"/> = <paramref name="a"/> * <paramref name="b"/> (row vectors), one SIMD row at a time, written in
    /// place (no 64-byte temporaries): the hot loop of a transform update.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Multiply(in Matrix4x4 a, in Matrix4x4 b, ref Matrix4x4 r)
    {
        ref readonly var b1 = ref Unsafe.As<float, Vector4>(ref Unsafe.AsRef(in b.M11));
        ref readonly var b2 = ref Unsafe.As<float, Vector4>(ref Unsafe.AsRef(in b.M21));
        ref readonly var b3 = ref Unsafe.As<float, Vector4>(ref Unsafe.AsRef(in b.M31));
        ref readonly var b4 = ref Unsafe.As<float, Vector4>(ref Unsafe.AsRef(in b.M41));
        Unsafe.As<float, Vector4>(ref r.M11) = b1 * a.M11 + b2 * a.M12 + b3 * a.M13 + b4 * a.M14;
        Unsafe.As<float, Vector4>(ref r.M21) = b1 * a.M21 + b2 * a.M22 + b3 * a.M23 + b4 * a.M24;
        Unsafe.As<float, Vector4>(ref r.M31) = b1 * a.M31 + b2 * a.M32 + b3 * a.M33 + b4 * a.M34;
        Unsafe.As<float, Vector4>(ref r.M41) = b1 * a.M41 + b2 * a.M42 + b3 * a.M43 + b4 * a.M44;
    }

    internal static Vector3 SafeNormalize(Vector3 v, Vector3 fallback)
    {
        var l = v.Length();
        return l > 1e-12f ? v / l : fallback;
    }
}

/// <summary>A depth-first visitor for <see cref="SceneNode.Visit{TVisitor}"/> (implement it on a struct).</summary>
public interface ISceneVisitor
{
    /// <summary>Called before the node's children; return false to skip them (and <see cref="Leave"/>).</summary>
    bool Enter(SceneNode node);

    /// <summary>Called after the node's children.</summary>
    void Leave(SceneNode node);
}
