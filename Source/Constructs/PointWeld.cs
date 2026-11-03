using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The welder's point weld (board item dev-point-weld-pw01; docs/sandbox/CONSTRUCTS.md, "Point weld"): the player clicks a
/// point on the piece to move (its place and the surface normal there are the weld's origin, a face point in the piece's
/// body frame) and a point on the target (a face point in that part's segment rest frame); the piece goes face to face,
/// its point on the target's, its normal opposite the target's, spun about the target normal. It is the socket weld with a
/// face point on both sides (<see cref="SocketWeld"/>: the same pose, rules, refusals, overlap, time and apply), so every
/// rule the socket weld has holds (a joint's leaf hangs the piece on the joint, the lock refusal, the freeform limits). Where
/// the result happens to line sockets up (<see cref="Resolve"/>: a free socket of the clicked piece part on a free socket
/// of the clicked target part, or a wheel's axle near a suspension's) it IS that socket weld, so it can merge on the grid
/// and a wheel rolls on its suspension. Nothing here allocates.
/// </summary>
public static class PointWeld
{
    public const float SnapReach = 0.02f; // metres: sockets this near after the face-to-face pose are welded as sockets
    public const float SnapDot = -0.9986f; // ...facing each other within about 3 degrees
    public const float GridSnap = 0.5f; // degrees: a spin this near a 90 degree step onto sockets is that step

    /// <summary>
    /// The point the crosshair is on as the weld's origin on the piece: the flat face of part <paramref name="part"/> under
    /// <paramref name="world"/> (<see cref="SocketWeld.FaceAt"/>), in the body frame. False: no flat face there, or the part
    /// isn't on the piece's main body.
    /// </summary>
    public static bool OriginAt(Construct c, int part, Vector3 world, Vector3 normal, out WeldTarget src)
    {
        if (!SocketWeld.FaceAt(c, part, world, normal, out src) || c.Parts[part].Seg != 0)
            return false;
        src.Body = c.Id;
        return true;
    }

    /// <summary>The spin (degrees) whose <see cref="SocketWeld.Turn"/> is nearest the body turn <paramref name="relRot"/> (any angle).</summary>
    public static float SpinOf(byte sourceDir, byte targetDir, Quaternion relRot)
    {
        var q = Quaternion.Normalize(relRot * Quaternion.Conjugate(Rot24.Quat(SocketWeld.BaseTurn(sourceDir, targetDir))));
        var axis = ConstructParts.DirVector(targetDir);
        var s = q.X * axis.X + q.Y * axis.Y + q.Z * axis.Z;
        return SocketWeld.Wrap(2f * MathF.Atan2(s, q.W) * 180f / MathF.PI);
    }

    /// <summary>
    /// Sockets lined up by the point weld's pose: with a face point on the piece (<paramref name="src"/>), the pose at
    /// <paramref name="degrees"/> puts a free socket of the clicked piece part within <see cref="SnapReach"/> of a free
    /// socket of the clicked target part, facing it (or, a wheel and a suspension, their free axle sockets within
    /// <see cref="Freeform.AxleReach"/>, facing): the weld becomes that socket weld (<paramref name="src"/>,
    /// <paramref name="w"/> and <paramref name="degrees"/> change), so it can merge on the grid and a wheel rolls. The client's
    /// ghost and the server both call this on the same numbers. True when it snapped.
    /// </summary>
    public static bool Resolve(Construct mover, ref WeldTarget src, Construct target, ref WeldTarget w, ref float degrees)
    {
        if (!src.Face || src.Part >= mover.PartCount || w.Part >= target.PartCount || mover.Parts[src.Part].Seg != 0)
            return false;
        if (!SocketWeld.Pose(mover, src, target, w, degrees, out var relPos, out var relRot, out _, out _))
            return false;
        int part = src.Part, tPart = w.Part;
        var md = mover.DefOf(part);
        var td = target.DefOf(tPart);
        var rm = mover.Parts[part].D.Role;
        var rt = target.Parts[tPart].D.Role;
        var wheelPair = rm == PartRole.Wheel && rt == PartRole.Suspension || rm == PartRole.Suspension && rt == PartRole.Wheel;
        int bs = -1, bt = -1;
        var bestD = float.MaxValue;
        var bestAxle = false;
        for (var s = 0; s < md.Sockets.Length; s++)
        {
            if (mover.SocketUsed(part, s))
                continue;
            mover.SocketAt(part, s, out var sa, out var sd);
            var p = relPos + Vector3.Transform(sa, relRot);
            var n = Vector3.Transform(ConstructParts.DirVector(sd), relRot);
            for (var t = 0; t < td.Sockets.Length; t++)
            {
                if (target.SocketUsed(tPart, t))
                    continue;
                var axle = wheelPair && md.Sockets[s].Kind == SocketKind.Axle && td.Sockets[t].Kind == SocketKind.Axle;
                target.SocketAt(tPart, t, out var ta, out var tdir);
                var d = Vector3.Distance(p, ta);
                var dot = Vector3.Dot(n, ConstructParts.DirVector(tdir));
                if (d > (axle ? Freeform.AxleReach : SnapReach) || dot > (axle ? -0.7f : SnapDot))
                    continue;
                if (axle && !bestAxle || axle == bestAxle && d < bestD)
                {
                    bs = s;
                    bt = t;
                    bestD = d;
                    bestAxle = axle;
                }
            }
        }
        if (bs < 0)
            return false;
        mover.SocketAt(part, bs, out _, out var sDir);
        target.SocketAt(tPart, bt, out _, out var tDir);
        var a = bestAxle ? SocketWeld.AngleOf(sDir, tDir, relRot) : SpinOf(sDir, tDir, relRot);
        var grid = MathF.Round(a / 90f) * 90f;
        if (MathF.Abs(a - grid) <= GridSnap)
            a = grid;
        degrees = SocketWeld.FromWire(SocketWeld.WireAngle(a));
        src = new WeldTarget { Body = src.Body, Part = (ushort)part, Socket = (byte)bs };
        w = new WeldTarget { Body = w.Body, Part = (ushort)tPart, Socket = (byte)bt };
        return true;
    }
}
