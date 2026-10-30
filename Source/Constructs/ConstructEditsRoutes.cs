using System.Numerics;

namespace CallOfSlop;

/// <summary>What the linking tool's edit modes do to a routed link (CAct.RouteEdit).</summary>
public enum RouteEditOp : byte
{
    Delete, // the link and every branch off it
    MoveNode, // one node to a new place (the link re-checked)
    Repair, // every segment back to full health (the wrench or the welder)
}

/// <summary>
/// The linking tool's routed links on the server (decision 20; docs/sandbox/CONSTRUCTS.md, "Routed links"): a whole link
/// as the client routed it (CAct.Route), re-checked with the same rules (ConstructRoutes.Check) from where the player
/// stands; deleting, moving a node and repairing (CAct.RouteEdit).
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>The linking tool: a routed link. Null, or why not.</summary>
    public string? Route(ConstructWorld world, byte player, Vector3 eye, ushort body, RouteType type, byte style, int from, byte fromPort, int parent, int parentSeg,
        Vector3 junction, ReadOnlySpan<CRouteNode> nodes, int to, byte toPort)
    {
        if (Linking(world, player, eye, body, to, out var c) is { } why)
            return why;
        if (parent == 255 && Linking(world, player, eye, body, from, out _) is { } why2)
            return why2;
        if (ConstructRoutes.Check(world, c!, type, style, from, fromPort, parent, parentSeg, junction, nodes, to, toPort) is { } bad)
            return bad;
        if (ConstructRoutes.Add(world, c!, type, style, from, fromPort, parent, parentSeg, junction, nodes, to, toPort) < 0)
            return "no room for another link";
        c!.Rebuild();
        Done.Add($"player {player}: {ConstructRoutes.Name(type, style)} {(parent == 255 ? c.DefOf(from).Name : "branch")} -> {c.DefOf(to).Name} on construct {c.Id} ({nodes.Length} nodes)");
        return null;
    }

    /// <summary>
    /// The linking tool's edit modes, and repairs with the wrench or the welder: <paramref name="op"/> on link
    /// <paramref name="route"/> (<paramref name="source"/> and <paramref name="to"/> must still match it: the client's
    /// copy may be a moment old). Null, or why not.
    /// </summary>
    public string? RouteEdit(ConstructWorld world, byte player, Vector3 eye, ushort body, RouteEditOp op, int route, int node, byte source, byte to, Vector3 pos, byte host)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (world.Get(body) is not { } c || route < 0 || route >= c.RouteCount)
            return "that link is gone";
        ref var r = ref c.Routes[route];
        if (r.Source != source || r.To != to)
            return "that link has changed";
        if (op == RouteEditOp.Repair ? h.Tool is not (ConstructTool.Wrench or ConstructTool.Welder) : h.Tool != ConstructTool.Linker)
            return op == RouteEditOp.Repair ? "take out the wrench (3) or the welder (4) to repair a link" : "take out the linking tool (5) to edit links";
        // In reach: the nearest point of the link.
        var near = float.MaxValue;
        for (var i = -1; i <= r.Count; i++)
            near = MathF.Min(near, Vector3.Distance(eye, ConstructRoutes.WorldPoint(c, route, i)));
        if (near > ConstructBuild.Reach + 1f)
            return "too far away";
        if (Contested(c, player) is { } defended)
            return defended; // (decision 19: an enemy's construct while its owner is near)
        switch (op)
        {
            case RouteEditOp.Delete:
                var name = ConstructRoutes.Name(r.Type, r.Style);
                ConstructRoutes.Remove(world, c, route);
                c.Rebuild();
                Done.Add($"player {player}: deleted a {name} on construct {c.Id}");
                return null;
            case RouteEditOp.MoveNode:
                if (host >= c.PartCount)
                    return "a node must sit on this construct";
                if (ConstructRoutes.MoveNode(world, c, route, node, pos, host) is { } why)
                    return why;
                c.Rebuild();
                return null;
            case RouteEditOp.Repair:
                if (!ConstructRoutes.Repair(world, c, route))
                    return $"that {ConstructRoutes.Name(r.Type, r.Style)} isn't damaged";
                c.Rebuild();
                Done.Add($"player {player}: repaired a {ConstructRoutes.Name(r.Type, r.Style)} on construct {c.Id}");
                return null;
        }
        return "no such edit";
    }
}
