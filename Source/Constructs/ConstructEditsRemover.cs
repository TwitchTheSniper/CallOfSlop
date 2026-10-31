using System.Numerics;

namespace CallOfSlop;

/// <summary>One thing the remover tool deletes: part (or, with <see cref="Route"/>, routed link) <see cref="Index"/> of construct <see cref="Body"/>.</summary>
public readonly record struct RemoveTarget(ushort Body, bool Route, byte Index);

/// <summary>
/// The remover tool on the server (board item dev-sandbox-remover-tool-rt01; docs/sandbox/CONSTRUCTS.md, "The remover tool").
/// The client selects parts and links and asks for their deletion once (<see cref="CActKind.Remover"/>); the server checks the
/// tool, the reach and who may, then destroys them outright: nothing comes back to anyone, routed links and wires on them go,
/// pieces cut off from the root split off as wrecks, and what a deleted container held spills as pickups. Creative: free
/// and instant. Survival: your own constructs, and loose pieces and wrecks nobody defends; never a defended enemy construct
/// (decision 19); the map's can be turned off (<see cref="SandboxMap.RemoverInSurvival"/>).
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>The most things one request may delete.</summary>
    public const int MaxRemoveTargets = 1024;

    /// <summary>Survival: may the remover be used at all (the map's <c>removerInSurvival</c>; the server sets it every tick).</summary>
    public bool RemoverInSurvival = true;

    /// <summary>
    /// Why this player may not delete construct <paramref name="c"/> with the remover, or null (the shared rule: the aim
    /// highlight asks it too). <paramref name="contested"/> is decision 19's reason (survival only; null when not defended).
    /// </summary>
    public static string? RemoverRefusal(Construct c, byte player, bool creative, string? contested)
    {
        if (c.Driver != 255)
            return "someone is driving it";
        for (var i = 0; i < c.PartCount; i++)
            if (c.Parts[i].SitterPlus1 != 0)
                return "someone is in it";
        if (creative || c.Owner == player)
            return null;
        if (contested != null)
            return contested;
        if (c.Loose || c.Wreck)
            return null;
        return c.Owner == 255 ? "that belongs to the map: salvage it with the wrench or welder" : "that is someone else's: salvage it with the wrench or welder";
    }

    /// <summary>
    /// The remover deletes <paramref name="targets"/> (parts and links, many constructs). All or nothing: any one refused or
    /// out of reach refuses the lot, so what the player confirmed is what goes. Null, or why not; <paramref name="parts"/>
    /// and <paramref name="links"/> say how many went.
    /// </summary>
    public string? Remover(ConstructWorld world, byte player, Vector3 eye, ReadOnlySpan<RemoveTarget> targets, out int parts, out int links)
    {
        parts = links = 0;
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Remover)
            return "take out the remover (the tool wheel) to delete things";
        if (!InfiniteParts && !RemoverInSurvival)
            return "the remover is switched off on this server";
        if (targets.Length == 0)
            return "nothing is selected";
        if (targets.Length > MaxRemoveTargets)
            return "too many things selected";
        // Group by construct: the marks of each (ids in first-seen order, so every run deletes in the same order).
        var ids = new List<ushort>();
        foreach (var t in targets)
            if (!ids.Contains(t.Body))
                ids.Add(t.Body);
        ids.Sort();
        var partMarks = new bool[ids.Count][];
        var linkMarks = new bool[ids.Count][];
        for (var k = 0; k < ids.Count; k++)
        {
            if (world.Get(ids[k]) is not { } c)
                return "something you selected is gone";
            var refused = RemoverRefusal(c, player, InfiniteParts, Contested(c, player));
            if (refused != null)
                return refused;
            if (Vector3.Distance(eye, c.WorldCom) > ConstructBuild.Reach + 4f + c.Radius)
                return "too far away";
            partMarks[k] = new bool[Construct.MaxParts];
            linkMarks[k] = new bool[Construct.MaxRoutes];
        }
        foreach (var t in targets)
        {
            var k = ids.BinarySearch(t.Body);
            var c = world.Get(t.Body)!;
            if (t.Route ? t.Index >= c.RouteCount : t.Index >= c.PartCount)
                return "something you selected is gone";
            (t.Route ? linkMarks : partMarks)[k][t.Index] = true;
        }
        for (var k = 0; k < ids.Count; k++)
        {
            var c = world.Get(ids[k])!;
            links += ConstructRoutes.RemoveMany(world, c, linkMarks[k]);
            var n = 0;
            foreach (var m in partMarks[k].AsSpan(0, c.PartCount))
                n += m ? 1 : 0;
            if (n > 0 && n >= c.PartCount)
                ReleaseGrabs(world, c);
            parts += world.RemoveParts(c, partMarks[k]);
            if (links > 0 && world.IsLive(c))
                c.Rebuild();
        }
        Done.Add($"player {player}: remover deleted {parts} part(s) and {links} link(s) on {ids.Count} construct(s)");
        return null;
    }

    private void ReleaseGrabs(ConstructWorld world, Construct c)
    {
        for (var pid = 0; pid < Hands.Length; pid++)
            if (Hands[pid].GrabBody == c.Id)
                Release(world, (byte)pid);
    }
}
