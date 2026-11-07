using System.Numerics;

namespace CallOfSlop;

/// <summary>What a fuse request does (CActKind.Fuse).</summary>
public enum FuseMode : byte
{
    Parts, // fuse the parts listed (the selector's selection on that construct): each linked group of two or more in it is one unit
    Piece, // fuse the parts linked to the aimed one on its segment, out to the whole rigid piece
    Unfuse, // un-fuse the unit the aimed part is in
}

/// <summary>The fuse tool on the server (ConstructFuse.cs; docs/sandbox/CONSTRUCTS.md, "Fusing"): free and instant, in creative and survival.</summary>
public sealed partial class ConstructEdits
{
    /// <summary>The most parts one request names (a construct's cap).</summary>
    public const int MaxFuseParts = Construct.MaxParts;

    /// <summary>
    /// Server: the fuse tool on construct <paramref name="body"/>, aimed at part <paramref name="index"/>. Fuse
    /// <paramref name="parts"/> (<see cref="FuseMode.Parts"/>), the aimed part's rigid piece (<see cref="FuseMode.Piece"/>),
    /// or un-fuse the aimed part's unit. The paint tool's rules: no seat, the tool out, the aimed part in reach, decision 19's
    /// contested-construct check. Parts already in a unit join the new one. Null with <paramref name="done"/> saying what
    /// happened, or why not.
    /// </summary>
    public string? Fuse(ConstructWorld world, byte player, Vector3 eye, ushort body, FuseMode mode, int index, ReadOnlySpan<ushort> parts, out string done)
    {
        done = "";
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Fuse)
            return "take out the fuse tool (the tool wheel) to fuse parts";
        if (world.Get(body) is not { } c)
            return "that construct is gone";
        if (mode > FuseMode.Unfuse)
            return "no such fuse mode";
        if (index < 0 || index >= c.PartCount)
            return "that part is gone";
        if (Vector3.Distance(eye, ReachPoint(c, index, eye)) > ConstructBuild.Reach + 1f)
            return "too far away";
        if (Contested(c, player) is { } defended)
            return defended;
        if (c.Dirty)
            c.Rebuild();
        var seg = c.Parts[index].Seg;
        if (mode == FuseMode.Unfuse)
        {
            var tag = c.Parts[index].Fuse;
            if (tag == 0)
                return "that part isn't fused";
            var n = 0;
            for (var i = 0; i < c.PartCount; i++)
                if (c.Parts[i].Fuse == tag)
                {
                    c.Parts[i].Fuse = 0;
                    n++;
                }
            Changed(world, c);
            done = $"un-fused {n} parts";
            return null;
        }
        var list = new List<int>();
        if (mode == FuseMode.Piece)
            PieceOf(c, index, list);
        else
        {
            var seen = new bool[c.PartCount];
            foreach (var i in parts)
                if (i < c.PartCount && !seen[i])
                {
                    if (c.Parts[i].Seg != seg)
                        return "a unit is on one moving piece: these parts are on different sides of a joint or weld";
                    seen[i] = true;
                    list.Add(i);
                }
        }
        if (list.Count < 2)
            return mode == FuseMode.Piece ? "nothing is attached to it to fuse with" : "select two or more parts to fuse";
        var newTag = ConstructFuse.NewTag();
        var inList = new bool[c.PartCount];
        foreach (var i in list)
        {
            c.Parts[i].Fuse = newTag;
            inList[i] = true;
        }
        Changed(world, c);
        c.Rebuild(); // (a selection that isn't one piece comes apart into its linked groups here, and a part left alone isn't fused)
        var units = 0;
        var fused = 0;
        for (var u = 0; u < c.UnitCount; u++)
        {
            var mem = c.UnitMembers(u);
            if (mem.Length > 0 && inList[mem[0]] && c.Parts[mem[0]].Fuse != 0)
            {
                units++;
                fused += mem.Length;
            }
        }
        if (fused == 0)
            return "none of those parts are attached to each other";
        done = units == 1 ? $"fused {fused} parts into one unit" : $"fused {fused} parts into {units} units (the selection wasn't one piece)";
        return null;
    }

    /// <summary>The parts linked to <paramref name="from"/> over links on its segment (the rigid piece it is in).</summary>
    private static void PieceOf(Construct c, int from, List<int> into)
    {
        var seg = c.Parts[from].Seg;
        var seen = new bool[c.PartCount];
        seen[from] = true;
        into.Add(from);
        for (var head = 0; head < into.Count; head++)
            for (var e = c.FirstLink(into[head]); e >= 0; e = c.NextLink(e))
            {
                var o = c.OtherEnd(e);
                if (seen[o] || c.Parts[o].Seg != seg)
                    continue;
                seen[o] = true;
                into.Add(o);
            }
    }

    /// <summary>The units changed: rebuilt (fully: the collider and the mass shares) and sent to every client as a structure change.</summary>
    private static void Changed(ConstructWorld world, Construct c)
    {
        c.Dirty = true;
        c.Wake();
        world.StructureDirty.Add(c.Id);
    }
}
