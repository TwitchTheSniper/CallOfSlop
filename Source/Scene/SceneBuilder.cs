using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Builds a node tree from a flat list of node records, the shape glTF (and most model formats) store hierarchies in:
/// each record has a name, a local TRS (or matrix), an optional renderable and a parent index that may point anywhere in
/// the list (before or after the child). <see cref="Build"/> creates the nodes and links them in record order, so the
/// child order (= draw order) is deterministic.
///
/// For glTF: node.translation / rotation / scale map straight to TRS (glTF's T * R * S with column vectors is our
/// S * R * T with row vectors); a node.matrix must be transposed (glTF is column-major column-vector, which reads as
/// row-major row-vector after a transpose). A mesh node gets <see cref="Renderable.Mesh"/> with the mesh pipeline's id;
/// a mesh with several primitives can use one child record per primitive. Unnamed nodes get "node_{index}".
/// </summary>
public sealed class SceneBuilder
{
    private struct Record
    {
        public string Name;
        public int Parent;
        public bool HasMatrix;
        public Vector3 T;
        public Quaternion R;
        public Vector3 S;
        public Matrix4x4 M;
        public Renderable Renderable;
        public NodeFlags Flags;
    }

    private readonly List<Record> _records = new();
    private SceneNode[] _nodes = [];

    public int Count => _records.Count;

    /// <summary>Adds a node with a TRS local transform. <paramref name="parent"/> is another record's index, or -1 for a root.</summary>
    public int Add(string? name, int parent, Vector3 translation, Quaternion rotation, Vector3 scale, Renderable renderable = default, NodeFlags flags = NodeFlags.None)
    {
        _records.Add(new Record
        {
            Name = string.IsNullOrEmpty(name) ? "node_" + _records.Count : name, Parent = parent, T = translation, R = rotation, S = scale,
            Renderable = renderable, Flags = flags,
        });
        return _records.Count - 1;
    }

    /// <summary>Adds a node with a local matrix (row-vector convention).</summary>
    public int Add(string? name, int parent, in Matrix4x4 local, Renderable renderable = default, NodeFlags flags = NodeFlags.None)
    {
        _records.Add(new Record
        {
            Name = string.IsNullOrEmpty(name) ? "node_" + _records.Count : name, Parent = parent, HasMatrix = true, M = local, Renderable = renderable, Flags = flags,
        });
        return _records.Count - 1;
    }

    /// <summary>Adds an attachment point (e.g. a glTF empty called "muzzle" or "seat_0").</summary>
    public int AddSocket(string name, int parent, Vector3 translation, Quaternion rotation) =>
        Add(name, parent, translation, rotation, Vector3.One, default, NodeFlags.Socket);

    /// <summary>
    /// Creates the nodes. Records whose parent is -1 (or invalid) go under <paramref name="root"/>; records are linked in
    /// order. Throws on a parent cycle. Returns the nodes by record index.
    /// </summary>
    public SceneNode[] Build(SceneNode root)
    {
        var n = _records.Count;
        _nodes = new SceneNode[n];
        for (var i = 0; i < n; i++)
        {
            var r = _records[i];
            var node = new SceneNode(r.Name, r.Flags) { Renderable = r.Renderable };
            if (r.HasMatrix)
                node.SetLocalMatrix(r.M);
            else
                node.SetLocal(r.T, r.R, r.S);
            _nodes[i] = node;
        }
        // Cycle check: walk each chain at most n steps.
        for (var i = 0; i < n; i++)
        {
            var p = _records[i].Parent;
            for (var steps = 0; (uint)p < (uint)n; steps++)
            {
                if (steps > n || p == i)
                    throw new InvalidDataException($"scene node '{_records[i].Name}' is its own ancestor");
                p = _records[p].Parent;
            }
        }
        for (var i = 0; i < n; i++)
        {
            var p = _records[i].Parent;
            ((uint)p < (uint)n ? _nodes[p] : root).Add(_nodes[i]);
        }
        return _nodes;
    }

    /// <summary>The node built for record <paramref name="index"/> (after <see cref="Build"/>).</summary>
    public SceneNode this[int index] => _nodes[index];
}
