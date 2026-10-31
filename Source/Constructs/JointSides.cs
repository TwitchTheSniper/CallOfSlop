using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The two sides of a joint part (board item dev-joint-side-indicators-js01; docs/sandbox/CONSTRUCTS.md, "Joint sides"): the BASE
/// is what attaches to the construct and stays with it (every socket but the leaf), the LEAF is what moves (the part's
/// <see cref="ConstructPartDef.LeafIndex"/> socket, which carries the new joint's segment). Part files tag boxes
/// <c>"side": "base"</c> or <c>"leaf"</c> (drawn in a fixed dark colour and a moving-side orange); the building markers, the
/// ghost, the look line and the aim highlight read the sides from here. Nothing in this class allocates per call.
/// </summary>
public static class JointSides
{
    public const byte Base = 1, Leaf = 2;

    /// <summary>Marker colours: the fixed side (cool steel) and the moving side (safety orange).</summary>
    public static readonly Vector3 BaseColour = new(0.55f, 0.68f, 0.85f), LeafColour = new(1f, 0.55f, 0.08f);

    /// <summary>Which side socket <paramref name="socket"/> of <paramref name="d"/> is: <see cref="Base"/>, <see cref="Leaf"/>, or 0 for a part that is no joint.</summary>
    public static byte SocketSide(ConstructPartDef d, int socket) => !d.HasJoint || d.LeafIndex < 0 ? (byte)0 : socket == d.LeafIndex ? Leaf : Base;

    /// <summary>The marker label: "base", "moving side" (empty for a part with no joint).</summary>
    public static string Label(byte side) => side == Base ? "base" : side == Leaf ? "moving side" : "";

    /// <summary>Is part <paramref name="part"/> of <paramref name="c"/> a real joint part with its joint made (a freeform weld's anchor is not)? <paramref name="joint"/>: its joint's index.</summary>
    public static bool IsJoint(Construct c, int part, out int joint)
    {
        joint = -1;
        if ((uint)part >= (uint)c.PartCount || !c.Parts[part].D.HasJoint)
            return false;
        joint = c.JointOf(part);
        return joint >= 0;
    }

    /// <summary>How many parts are on each side of joint <paramref name="joint"/>: the moving side is every part on its segment or hanging from it, the base is the rest of the construct (the joint part itself counts for neither).</summary>
    public static void Count(Construct c, int joint, out int baseParts, out int leafParts)
    {
        baseParts = leafParts = 0;
        var part = c.Joints[joint].Hinge;
        for (var i = 0; i < c.PartCount; i++)
        {
            if (i == part)
                continue;
            if (c.Under(i, joint + 1))
                leafParts++;
            else
                baseParts++;
        }
    }

    /// <summary>Which side part <paramref name="i"/> is on for joint <paramref name="joint"/>: <see cref="Leaf"/>, <see cref="Base"/>, or 0 for the joint part itself.</summary>
    public static byte SideOf(Construct c, int joint, int i) => i == c.Joints[joint].Hinge ? (byte)0 : c.Under(i, joint + 1) ? Leaf : Base;

    /// <summary>The look line: "Hinge - Base: fixed to construct 7 (12 parts) | Moving side: 3 parts".</summary>
    public static string Look(Construct c, int part, int joint)
    {
        Count(c, joint, out var b, out var l);
        var d = c.Parts[part].D;
        return $"{d.Name} - Base: fixed to construct {c.Id} ({b} part{(b == 1 ? "" : "s")}) | Moving side: {l} part{(l == 1 ? "" : "s")}";
    }

    /// <summary>The ghost line while a part goes on or is a joint: what the held part's socket and the target's socket are. Null when neither is a joint.</summary>
    public static string? Ghost(ConstructPartDef target, int targetSocket, ConstructPartDef held, int heldSocket)
    {
        var t = SocketSide(target, targetSocket);
        var h = SocketSide(held, heldSocket);
        if (t == 0 && h == 0)
            return null;
        var a = t == Leaf ? $"the {target.Name}'s moving side" : t == Base ? $"the {target.Name}'s base" : $"the {target.Name}";
        var b = h == Leaf ? $"the {held.Name}'s moving side" : h == Base ? $"the {held.Name}'s base (fixed)" : $"the {held.Name}";
        return t == Leaf && h == 0 ? $"{b} will ride on {a}"
            : t == Base && h == 0 ? $"{b} will be fixed to {a}"
            : $"{b} mounts on {a}";
    }
}
