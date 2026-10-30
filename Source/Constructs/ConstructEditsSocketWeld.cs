using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's three-step socket weld on the server (board item dev-sandbox-socket-weld-3step-sw01, decision 26;
/// SocketWeld.cs): the client sends the piece's socket, the target (a socket or a face point) and the angle, and the server
/// checks the final pose: the usual welder rules (out of a seat, the welder out, in reach, your own constructs or wrecks,
/// loose parts and the map's, decision 19's contested rule), then <see cref="SocketWeld.Check"/>. Survival: the weld takes
/// the piece's fit time and is checked again when it is done; creative: at once.
/// </summary>
public sealed partial class ConstructEdits
{
    /// <summary>The socket weld's rules for this player (who may and reach, then the physical ones). Null, or why not.</summary>
    public string? SocketWeldRefusal(ConstructWorld world, byte player, Vector3 eye, Construct mover, int part, int socket, Construct target, in WeldTarget w, float degrees)
    {
        if (part < 0 || part >= mover.PartCount || w.Part >= target.PartCount)
            return "that part is gone";
        if (SocketWeld.TargetFrame(target, w, out var tAt, out _))
        {
            target.WorldFrame(target.Parts[w.Part].Seg, out var q, out var t);
            if (Reachable(eye, t + Vector3.Transform(tAt, q), ConstructBuild.Reach) is { } far)
                return far;
        }
        if (SocketWeld.SourceFrame(mover, part, socket, out var sAt, out _) && Reachable(eye, mover.ToWorld(sAt), ConstructBuild.Reach + 1.5f) is { } farPiece)
            return farPiece;
        if (target.Driver != 255 || mover.Driver != 255)
            return "someone is driving it";
        if (SocketWeld.Check(world, Query, mover, part, socket, target, w, degrees, Feet(world)) is { } why)
            return why;
        if ((Contested(target, player) ?? Contested(mover, player)) is { } defended)
            return defended;
        if (SalvageRefusal(target, player) != null || SalvageRefusal(mover, player) != null)
            return "that construct is someone else's: you can only weld your own (or wrecks and loose parts)";
        return null;
    }

    /// <summary>Starts a socket weld of the piece <paramref name="body"/> (by socket <paramref name="socket"/> of part <paramref name="part"/>) onto <paramref name="w"/> at <paramref name="degrees"/>. Null, or why not.</summary>
    public string? BeginSocketWeld(ConstructWorld world, byte player, Vector3 eye, ushort body, int part, int socket, in WeldTarget w, float degrees, double now)
    {
        ref var h = ref Hands[player];
        if (h.Seated)
            return "get out of the seat first";
        if (h.Tool != ConstructTool.Welder)
            return "take out the welder (4) to socket-weld a piece on";
        if (world.Get(body) is not { } mover || world.Get(w.Body) is not { } target)
            return "that construct is gone";
        degrees = SocketWeld.FromWire(SocketWeld.WireAngle(degrees)); // (the angle as the wire carries it: both ends the same)
        if (SocketWeldRefusal(world, player, eye, mover, part, socket, target, w, degrees) is { } why)
            return why;
        h.Edit = EditKind.SocketWeld;
        h.Body = body;
        h.Part = (byte)part;
        h.Socket = (byte)socket;
        h.Body2 = w.Body;
        h.Part2 = w.Part;
        h.Variant = w.Socket;
        h.At = w.At;
        h.PlaceTurn = w.Dir;
        h.Yaw = degrees;
        h.Start = now;
        h.Duration = Survival ? ConstructBuild.AttachSeconds(mover.DefOf(part)) : 0;
        return FinishNow(world, player, eye);
    }

    private string CompleteSocketWeld(ConstructWorld world, byte player, ref SandboxHand h, Vector3 eye)
    {
        if (h.Tool != ConstructTool.Welder || world.Get(h.Body) is not { } mover || world.Get(h.Body2) is not { } target)
            return "failed: that construct is gone";
        var w = new WeldTarget { Body = h.Body2, Part = h.Part2, Socket = h.Variant, At = h.At, Dir = h.PlaceTurn };
        if (SocketWeldRefusal(world, player, eye, mover, h.Part, h.Socket, target, w, h.Yaw) is { } why)
            return $"failed: {why}";
        for (var i = 0; i < Hands.Length; i++)
            if (i != player && Hands[i].Edit != EditKind.None && (Hands[i].Body == mover.Id || Hands[i].Body2 == mover.Id))
                Hands[i].Edit = EditKind.None; // (edits on the piece that goes stop)
        var grid = SocketWeld.OnRotGrid(mover, h.Part, h.Socket, target, w, h.Yaw);
        var from = mover.Id;
        if (world.SocketWeldApply(target, mover, h.Part, h.Socket, w, h.Yaw) is { } failed)
            return $"failed: {failed}";
        target.Owner = player; // (both were the player's, or free for anyone to take)
        return $"ok: construct {from} socket-welded onto construct {target.Id} at {h.Yaw:0.##} deg ({(grid ? "on the grid" : "freeform segment")})";
    }
}
