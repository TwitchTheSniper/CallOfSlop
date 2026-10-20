using System.Numerics;

namespace JoltSpike;

/// <summary>
/// The scene graph node proposed in the design doc, at prototype size: parent/children, local TRS, a cached
/// world matrix and a dirty flag. Setting a local transform marks the node dirty; UpdateWorld() walks the tree
/// once and recomputes only dirty subtrees. Static nodes are walked once at load and then skipped.
/// </summary>
public sealed class Node(string name)
{
    public string Name = name;
    public Node? Parent { get; private set; }
    public readonly List<Node> Children = new();
    Vector3 _pos;
    Quaternion _rot = Quaternion.Identity;
    Vector3 _scale = Vector3.One;
    bool _dirty = true;
    public Matrix4x4 World = Matrix4x4.Identity;
    public int Recomputed; // for the prototype's measurements

    public Vector3 LocalPosition { get => _pos; set { _pos = value; _dirty = true; } }
    public Quaternion LocalRotation { get => _rot; set { _rot = value; _dirty = true; } }
    public Vector3 LocalScale { get => _scale; set { _scale = value; _dirty = true; } }

    public Matrix4x4 Local => Matrix4x4.CreateScale(_scale) * Matrix4x4.CreateFromQuaternion(_rot) * Matrix4x4.CreateTranslation(_pos);

    public Node Add(Node child)
    {
        child.Parent?.Children.Remove(child);
        child.Parent = this;
        child._dirty = true;
        Children.Add(child);
        return child;
    }

    /// <summary>Recomputes world matrices under this node; a dirty parent forces its whole subtree.</summary>
    public void UpdateWorld(bool parentChanged = false)
    {
        var changed = _dirty || parentChanged;
        if (changed)
        {
            World = Parent == null ? Local : Local * Parent.World;
            _dirty = false;
            Recomputed++;
        }
        foreach (var c in Children)
            c.UpdateWorld(changed);
    }

    public Node? Find(string name)
    {
        if (Name == name)
            return this;
        foreach (var c in Children)
            if (c.Find(name) is { } n)
                return n;
        return null;
    }

    public Vector3 WorldPosition => World.Translation;
}
