using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The linking tool's world links on the server (board item wl01; docs/sandbox/CONSTRUCTS.md, "World links"): a whole
/// link as the client routed it (CAct.WorldRoute), re-checked with the same rules (ConstructWorld.CheckWorldLink) from
/// where the player stands, and deleting one (CAct.WorldRouteEdit). Hoses are in ConstructHoses.cs (F on their parts).
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>The linking tool: a world link (the end the player clicked last must be in reach, its start within the link's reach). Null, or why not.</summary>
    public string? WorldRoute(ConstructWorld world, IWorldQuery? q, byte player, Vector3 eye, RouteType type, byte style, ushort fromBody, int from, byte fromPort, ReadOnlySpan<WNode> nodes,
        ushort toBody, int to, byte toPort)
    {
        if (Linking(world, player, eye, toBody, to, out var ct) is { } why)
            return why;
        if (world.Get(fromBody) is not { } cf || from < 0 || from >= cf.PartCount)
            return "that part is gone";
        if (Contested(cf, player) is { } defended)
            return defended;
        if (Vector3.Distance(eye, ConstructWorld.PortWorld(cf, from, fromPort)) > ConstructRoutes.MaxLength + ConstructBuild.Reach)
            return "too far away";
        if (world.CheckWorldLink(q, type, style, cf, from, fromPort, nodes, ct!, to, toPort) is { } bad)
            return bad;
        if (world.AddWorldLink(q, type, style, cf, from, fromPort, nodes, ct!, to, toPort) < 0)
            return "no room for another link";
        Done.Add($"player {player}: world {ConstructRoutes.Name(type, style)} {cf.DefOf(from).Name} on construct {cf.Id} -> {ct!.DefOf(to).Name} on construct {ct.Id} ({nodes.Length} nodes)");
        return null;
    }

    /// <summary>The linking tool's delete mode on world link <paramref name="link"/> (its ends must still match: the client's copy may be a moment old). Null, or why not.</summary>
    public string? WorldRouteDelete(ConstructWorld world, byte player, Vector3 eye, int link, ushort fromBody, byte from, ushort toBody, byte to)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Linker)
            return "take out the linking tool (5) to edit links";
        if (link < 0 || link >= world.WorldLinkCount)
            return "that link is gone";
        ref var l = ref world.WorldLinks[link];
        if (l.BodyFrom != fromBody || l.From != from || l.BodyTo != toBody || l.To != to)
            return "that link has changed";
        var near = float.MaxValue;
        for (var i = -1; i <= l.Count; i++)
            if (world.WorldPoint(link, i, out var p))
                near = MathF.Min(near, Vector3.Distance(eye, p));
        if (near > ConstructBuild.Reach + 1f)
            return "too far away";
        var name = ConstructRoutes.Name(l.Type, l.Style);
        world.RemoveWorldLink(link);
        Done.Add($"player {player}: deleted a world {name}");
        return null;
    }
}
