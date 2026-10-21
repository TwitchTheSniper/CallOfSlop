using System.Text;

namespace CallOfSlop;

/// <summary>One part placed in a build, with the parts plugged into its sockets.</summary>
public sealed class BuildNode
{
    public BuildNode(PartDef part) => Part = part;

    public PartDef Part { get; }

    /// <summary>The node this one plugs into (null for the receiver at the root).</summary>
    public BuildNode? Parent { get; private set; }

    /// <summary>The parent's socket it plugs into.</summary>
    public PartSocket? ParentSocket { get; private set; }

    private readonly Dictionary<string, BuildNode> _children = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The part plugged into <paramref name="socket"/>, if any.</summary>
    public BuildNode? Child(string socket) => _children.GetValueOrDefault(socket);

    /// <summary>Children in the order of this part's sockets (deterministic: stats and models walk the tree this way).</summary>
    public IEnumerable<(PartSocket Socket, BuildNode Node)> Children()
    {
        foreach (var s in Part.Sockets)
            if (_children.TryGetValue(s.Name, out var c))
                yield return (s, c);
    }

    /// <summary>Plugs <paramref name="child"/> into <paramref name="socket"/> (replacing whatever was there). No rule checks: see <see cref="BuildRules.WhyNot"/>.</summary>
    public BuildNode Attach(string socket, BuildNode child)
    {
        var s = Part.Sockets.FirstOrDefault(x => x.Name.Equals(socket, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{Part.Name} has no socket \"{socket}\"");
        child.Detach();
        if (_children.TryGetValue(s.Name, out var old))
        {
            old.Parent = null; // the replaced part is fully off the build (a half-detached node broke Path())
            old.ParentSocket = null;
        }
        _children[s.Name] = child;
        child.Parent = this;
        child.ParentSocket = s;
        return child;
    }

    /// <inheritdoc cref="Attach(string, BuildNode)"/>
    public BuildNode Attach(string socket, PartDef part) => Attach(socket, new BuildNode(part));

    /// <summary>Takes this node (and everything on it) off its parent.</summary>
    public void Detach()
    {
        if (Parent != null && ParentSocket != null)
            Parent._children.Remove(ParentSocket.Name);
        Parent = null;
        ParentSocket = null;
    }

    /// <summary>This node and everything below it, depth first in socket order.</summary>
    public IEnumerable<BuildNode> Walk()
    {
        yield return this;
        foreach (var (_, c) in Children())
            foreach (var n in c.Walk())
                yield return n;
    }

    /// <summary>Splitters between this node and the receiver.</summary>
    public int SplitterDepth()
    {
        var d = 0;
        for (var p = Parent; p != null; p = p.Parent)
            if (p.Part.Category == PartCategory.Splitter)
                d++;
        return d;
    }

    /// <summary>The receiver this node belongs to (itself if it is one), or null for parts on the grip.</summary>
    public BuildNode? ReceiverNode()
    {
        for (var n = this; n != null; n = n.Parent)
            if (n.Part.Category == PartCategory.Receiver)
                return n;
        return null;
    }

    /// <summary>A path to this node from the root ("barrel/barrel_2/muzzle"), for saves and UI selection.</summary>
    public string Path()
    {
        var parts = new List<string>();
        for (var n = this; n is { ParentSocket: not null, Parent: not null }; n = n.Parent)
            parts.Add(n.ParentSocket.Name);
        parts.Reverse();
        return string.Join('/', parts);
    }

    public BuildNode Clone()
    {
        var copy = new BuildNode(Part);
        foreach (var (s, c) in Children())
            copy.Attach(s.Name, c.Clone());
        return copy;
    }
}

