using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's freeform mode on the server (board item dev-sandbox-freeform-weld-fw01; ConstructFreeform.cs): weld two
/// bodies where they are, and cut a freeform seam free again. The usual welder rules: out of a seat, the welder out,
/// within reach, your own constructs (or wrecks, loose parts and the map's), decision 19's contested rule, and in survival
/// the weld takes <see cref="Freeform.WeldSeconds"/> (checked again when it is done: the pieces may have moved).
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>The freeform weld's rules for this player (physical ones, then who may). Null, or why not.</summary>
    public string? FreeformRefusal(ConstructWorld world, byte player, Vector3 eye, Construct a, int pa, Construct b, int pb)
    {
        if (pa < 0 || pa >= a.PartCount || pb < 0 || pb >= b.PartCount)
            return "that part is gone";
        for (var k = 0; k < 2; k++)
        {
            var c = k == 0 ? a : b;
            Freeform.PartBox(c, k == 0 ? pa : pb, out _, out var centre, out var half);
            if (Vector3.Distance(eye, centre) > ConstructBuild.Reach + 0.75f + half.Length())
                return "too far away";
        }
        if (Freeform.Rules(world, Query, a, pa, b, pb, Feet(world), out _) is { } why)
            return why;
        if ((Contested(a, player) ?? Contested(b, player)) is { } defended)
            return defended;
        if (SalvageRefusal(a, player) != null || SalvageRefusal(b, player) != null)
            return "that construct is someone else's: you can only weld your own (or wrecks and loose parts)";
        return null;
    }

    /// <summary>Starts a freeform weld of part <paramref name="pa"/> of <paramref name="bodyA"/> to part <paramref name="pb"/> of <paramref name="bodyB"/> (creative: done at once). Null, or why not.</summary>
    public string? BeginFreeform(ConstructWorld world, byte player, Vector3 eye, ushort bodyA, int pa, ushort bodyB, int pb, double now)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the welder (4) to weld freeform";
        if (world.Get(bodyA) is not { } a || world.Get(bodyB) is not { } b)
            return "that construct is gone";
        if (FreeformRefusal(world, player, eye, a, pa, b, pb) is { } why)
            return why;
        h.Edit = EditKind.Weld;
        h.Body = bodyA;
        h.Part = (byte)pa;
        h.Body2 = bodyB;
        h.Part2 = (byte)pb;
        h.Start = now;
        h.Duration = Survival ? Freeform.WeldSeconds : 0;
        return FinishNow(world, player, eye);
    }

    private string CompleteFreeform(ConstructWorld world, byte player, ref SandboxHand h)
    {
        if (h.Tool != ConstructTool.Welder || world.Get(h.Body) is not { } a || world.Get(h.Body2) is not { } b)
            return "failed: that construct is gone";
        if (Freeform.Rules(world, Query, a, h.Part, b, h.Part2, Feet(world), out var snapped) is { } why)
            return $"failed: {why}";
        if ((Contested(a, player) ?? Contested(b, player)) is { } defended)
            return $"failed: {defended}";
        if (snapped && Freeform.AxleSnap(a, h.Part, b, h.Part2, out var snap))
        {
            // A wheel onto a suspension's axle: the socket weld (on the grid, linked), so it rolls (board item wheel-weld).
            for (var i = 0; i < Hands.Length; i++)
                if (i != player && Hands[i].Edit != EditKind.None && (Hands[i].Body == snap.Mover.Id || Hands[i].Body2 == snap.Mover.Id))
                    Hands[i].Edit = EditKind.None; // (edits on the piece that goes stop)
            var piece = snap.Mover.Id;
            if (world.SocketWeldApply(snap.Keep, snap.Mover, snap.Part, snap.Socket, snap.Target, snap.Degrees) is { } failed)
                return $"failed: {failed}";
            snap.Keep.Owner = player;
            return $"ok: construct {piece} welded onto construct {snap.Keep.Id} by its axle (it rolls on the {snap.Keep.DefOf(snap.Target.Part).Name})";
        }
        var first = Freeform.KeepsFirst(a, b);
        var keep = first ? a : b;
        var mover = first ? b : a;
        int anchor = first ? h.Part : h.Part2, welded = first ? h.Part2 : h.Part;
        for (var i = 0; i < Hands.Length; i++)
            if (Hands[i].Edit is EditKind.Attach or EditKind.Remove or EditKind.Weld && (Hands[i].Body == mover.Id || Hands[i].Body2 == mover.Id) && i != player)
                Hands[i].Edit = EditKind.None; // (edits on the piece that goes stop)
        var from = mover.Id;
        var joint = world.FreeformWeld(keep, anchor, mover, welded);
        if (joint < 0)
            return "failed: they can't be welded";
        keep.Owner = player; // (both were the player's, or free for anyone to take)
        return $"ok: construct {from} freeform-welded onto construct {keep.Id} (weld {joint})";
    }

    /// <summary>The welder on a freeform seam: weld <paramref name="joint"/> of the construct (on part <paramref name="part"/>) is cut, and what it held falls away where it is. Null, or why not.</summary>
    public string? FreeformCut(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, int joint)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the welder (4) to cut a freeform weld";
        if (world.Get(body) is not { } c || joint < 0 || joint >= c.JointCount || !c.Joints[joint].Freeform
            || part >= c.PartCount || c.Joints[joint].Hinge != part && c.Joints[joint].Child != part)
            return "that weld is gone";
        if (c.Driver != 255)
            return "someone is driving it";
        if (SalvageRefusal(c, player) is { } theirs)
            return theirs;
        if (Contested(c, player) is { } defended)
            return defended;
        Freeform.PartBox(c, part, out _, out var centre, out var half);
        if (Vector3.Distance(eye, centre) > ConstructBuild.Reach + 0.75f + half.Length())
            return "too far away";
        for (var i = 0; i < Hands.Length; i++)
            if (Hands[i].Edit != EditKind.None && (Hands[i].Body == c.Id || Hands[i].Body2 == c.Id))
                Hands[i].Edit = EditKind.None; // (part indices are about to change)
        var piece = world.FreeformCut(c, joint);
        Done.Add($"player {player}: cut freeform weld {joint} of construct {body}{(piece != null ? $": construct {piece.Id} cut free" : "")}");
        return null;
    }
}
