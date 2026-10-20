namespace CallOfSlop;

/// <summary>One pending node of a depth-first walk, with what it inherits from its parent.</summary>
internal struct WalkEntry
{
    public SceneNode Node;

    /// <summary>An ancestor was recomputed, so this node must be too.</summary>
    public bool Force;

    /// <summary>No ancestor is hidden.</summary>
    public bool Visible;
}

/// <summary>
/// The explicit stack the transform and flatten walks use instead of recursion (a call per node costs more than the
/// matrix maths). Walks are re-entrant: each one only pops what it pushed above the count it started at.
/// </summary>
internal sealed class WalkStack
{
    public WalkEntry[] Items = new WalkEntry[64];
    public int Count;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public void Push(SceneNode node, bool force, bool visible)
    {
        if (Count == Items.Length)
            Array.Resize(ref Items, Items.Length * 2);
        ref var e = ref Items[Count++];
        e.Node = node;
        e.Force = force;
        e.Visible = visible;
    }
}
