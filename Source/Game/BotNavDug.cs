using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Nav nodes over dug or filled ground (docs/sandbox/TERRAIN-DIGGING.md). A graph is built from the ground as built and
/// kept while players dig, fill and flatten, so a node that stood on the ground keeps the old height: in a crater it hangs
/// in the air, under a fill it is buried. Bots read a node's height through <see cref="At"/>, which puts a node that stood
/// on the built ground of a changed column onto that column's ground as it is now. Otherwise a bot in a crater found no
/// node within reach (they were all above it), counted itself off the graph and was put back on a node at the old height,
/// and fell straight back in. One column lookup, and nothing at all while the ground is untouched.
/// </summary>
public sealed partial class NavGraph
{
    /// <summary>How near the built ground a node must be (m) to count as standing on it.</summary>
    private const float OnBuiltGround = 0.35f;

    /// <summary>Node <paramref name="i"/> where a bot finds it now: on the ground as it is, if the node stood on ground since changed.</summary>
    public Vector3 At(int i) => OnGroundNow(Nodes[i]);

    /// <summary><paramref name="q"/> (a node's place) moved onto the ground as it is now, if it stood on built ground that was dug or filled.</summary>
    public static Vector3 OnGroundNow(Vector3 q)
    {
        if (World.Map.Terrain?.EditsIfMade is not { Log.Count: > 0 } edits || GravityFrames.Curved)
            return q;
        if (!edits.TryBuiltTop(q.X, q.Z, out var built) || MathF.Abs(q.Y - built) > OnBuiltGround || !edits.TryColumn(q.X, q.Z, out var spans) || spans.Length < 2)
            return q;
        q.Y += TerrainEdits.Top(spans) - built; // (kept as far above the ground as it was)
        return q;
    }

    /// <summary>Is <paramref name="p"/> in a column players dug (a pit can go far below every node)?</summary>
    public static bool InDugColumn(Vector3 p) =>
        World.Map.Terrain?.EditsIfMade is { Log.Count: > 0 } edits && edits.TryBuiltTop(p.X, p.Z, out var built) && p.Y < built;
}
