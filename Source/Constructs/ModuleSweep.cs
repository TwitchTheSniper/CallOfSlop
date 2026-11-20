using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The moving parts' sweep (docs/sandbox/design/module-sockets.md 3.3): each joint of a module about to be fitted is swept through its
/// range (a turntable or swivel with no limits a full turn in 15 degree steps, a hinge its limits, a piston or slider every 0.25 m),
/// the other joints held at rest, and the solid boxes of everything it carries are posed at each step in the ship's frame and tested
/// against the ship's solids (an oriented box against each axis-aligned one). The first clash names both parts and the pose. Runs at a
/// fit only (a server edit, or the Creative ghost's one look), never per tick; at most <see cref="MaxPoses"/> poses a module.
/// </summary>
public static class ModuleSweep
{
    public const float Step = MathF.PI / 12f; // 15 degrees
    public const float SlideStep = 0.25f; // m
    public const int MaxPoses = 24 * 13;
    private const float Skin = 0.004f; // m: boxes that only touch don't clash

    /// <summary>
    /// The first clash of module <paramref name="mi"/> of <paramref name="mover"/>, set onto <paramref name="ship"/> at
    /// <paramref name="bodyTurn"/> / <paramref name="rel"/> (the mover's body frame in the ship's), against the ship's own solids; null:
    /// it clears. The plate of socket <paramref name="si"/> is left out (the module sits on it).
    /// </summary>
    // The last few answers: a ghost asks again whenever its key changes (a drifting ship moves its pose, not the fit), so one fit's
    // sweep runs once, not every frame. Keyed by both bodies and their rebuilds, the socket, the module, the turn and the place (cm).
    private readonly record struct Key(Construct Ship, int ShipStamp, int Si, Construct Mover, int MoverStamp, int Mi, byte Turn, int X, int Y, int Z, bool Only);
    private static readonly Key[] _keys = new Key[4];
    private static readonly string?[] _answers = new string?[4];
    private static int _next;

    public static string? Clash(Construct ship, int si, Construct mover, int mi, byte bodyTurn, Vector3 rel, bool moduleOnly)
    {
        if (mover.JointCount == 0)
            return null;
        var key = new Key(ship, ship.RebuildStamp, si, mover, mover.RebuildStamp, mi, bodyTurn, (int)MathF.Round(rel.X * 100), (int)MathF.Round(rel.Y * 100), (int)MathF.Round(rel.Z * 100), moduleOnly);
        lock (_keys)
            for (var i = 0; i < _keys.Length; i++)
                if (_keys[i] == key)
                    return _answers[i];
        var answer = Sweep(ship, si, mover, mi, bodyTurn, rel, moduleOnly);
        lock (_keys)
        {
            _keys[_next] = key;
            _answers[_next] = answer;
            _next = (_next + 1) % _keys.Length;
        }
        return answer;
    }

    private static string? Sweep(Construct ship, int si, Construct mover, int mi, byte bodyTurn, Vector3 rel, bool moduleOnly)
    {
        var plate = ship.ModSockets[si].Plate;
        var of = mover.ModuleOf;
        var turn = Rot24.Quat(bodyTurn);
        var poses = 0;
        for (var k = 0; k < mover.JointCount; k++)
        {
            ref var jt = ref mover.Joints[k];
            if (jt.Freeform || jt.Type is JointType.Fixed or JointType.Flex or JointType.Steering)
                continue;
            var seg = k + 1;
            float lo, hi, step;
            var jd = jt.DrivePart < mover.PartCount ? mover.DefOf(jt.DrivePart) : null;
            if (jt.Slides)
                (lo, hi, step) = (jt.Min, jt.Max, SlideStep);
            else if (jt.Type is JointType.Turntable or JointType.Swivel && (jd is not { JointLimited: true } || jt.Max - jt.Min >= 2 * MathF.PI - 0.05f))
                (lo, hi, step) = (0, 2 * MathF.PI - Step * 0.5f, Step);
            else
                (lo, hi, step) = (jt.Min, jt.Max, Step);
            if (!(hi > lo) || !float.IsFinite(lo) || !float.IsFinite(hi))
                continue;
            for (var a = lo; a <= hi + 1e-4f && poses < MaxPoses; a += step, poses++)
            {
                if (MathF.Abs(a) < 1e-4f)
                    continue; // (the rest pose: the fit's own overlap test)
                for (var j = 0; j < mover.PartCount; j++)
                {
                    var ps = mover.Parts[j].Seg;
                    if (ps == 0 || !Under(mover, ps, seg) || moduleOnly && of != null && of[j] != mi || mover.Parts[j].Broken)
                        continue;
                    PoseWith(mover, ps, k, a, out var q, out var t);
                    var bq = turn * q;
                    var d = mover.DefOf(j);
                    for (var b = 0; b < d.SolidBoxes; b++)
                    {
                        mover.BoxOf(j, b, out var bc, out var bh);
                        var centre = rel + Vector3.Transform(t + Vector3.Transform(bc, q), turn);
                        if (Hits(ship, plate, centre, bq, bh) is var hit and >= 0)
                        {
                            var what = jt.Slides ? FormattableString.Invariant($"at {a:0.00} m") : FormattableString.Invariant($"at {a * 180 / MathF.PI:0} degrees");
                            return $"the {d.Name} hits the {HullHoles.Shown(ship.DefOf(hit))} (part {hit}) {what} of its {mover.DefOf(jt.DrivePart < mover.PartCount ? jt.DrivePart : j).Name}";
                        }
                    }
                }
            }
        }
        return null;
    }

    /// <summary>Is segment <paramref name="seg"/> <paramref name="top"/> or carried by it?</summary>
    private static bool Under(Construct c, int seg, int top)
    {
        for (var guard = 0; seg > 0 && seg <= c.JointCount && guard <= Construct.MaxJoints; guard++)
        {
            if (seg == top)
                return true;
            seg = c.Joints[seg - 1].ParentSeg;
        }
        return false;
    }

    /// <summary>Segment <paramref name="seg"/>'s pose (rest frame to body frame) with joint <paramref name="k"/> at <paramref name="angle"/> and every other at rest.</summary>
    private static void PoseWith(Construct c, int seg, int k, float angle, out Quaternion q, out Vector3 t)
    {
        q = Quaternion.Identity;
        t = Vector3.Zero;
        if (seg <= 0 || seg > c.JointCount)
            return;
        ref var j = ref c.Joints[seg - 1];
        PoseWith(c, j.ParentSeg, k, angle, out var pq, out var pt);
        if (j.Freeform)
        {
            q = pq * j.FixRot;
            t = pt + Vector3.Transform(j.FixPos, pq);
            return;
        }
        var a = seg - 1 == k ? angle : 0f;
        if (j.Slides)
        {
            q = pq;
            t = pt + Vector3.Transform(j.Axis * a, pq);
            return;
        }
        var turn = Quaternion.CreateFromAxisAngle(j.Axis, a);
        q = pq * turn;
        t = pt + Vector3.Transform(j.Pivot - Vector3.Transform(j.Pivot, turn), pq);
    }

    /// <summary>The first solid part of <paramref name="ship"/>'s own frame (not <paramref name="plate"/>) an oriented box meets, or -1.</summary>
    private static int Hits(Construct ship, int plate, Vector3 c, Quaternion q, Vector3 h)
    {
        var m = Matrix4x4.CreateFromQuaternion(q);
        Vector3 ax = new(m.M11, m.M12, m.M13), ay = new(m.M21, m.M22, m.M23), az = new(m.M31, m.M32, m.M33);
        var ext = new Vector3(
            MathF.Abs(ax.X) * h.X + MathF.Abs(ay.X) * h.Y + MathF.Abs(az.X) * h.Z,
            MathF.Abs(ax.Y) * h.X + MathF.Abs(ay.Y) * h.Y + MathF.Abs(az.Y) * h.Z,
            MathF.Abs(ax.Z) * h.X + MathF.Abs(ay.Z) * h.Y + MathF.Abs(az.Z) * h.Z);
        var hits = Construct.PartScratch;
        var n = ship.PartsInBox(c - ext, c + ext, hits);
        for (var i = 0; i < n; i++)
        {
            var p = hits[i];
            if (p == plate || !ship.Rigid(ship.Parts[p].Seg) || ship.Parts[p].Broken)
                continue; // (a joint's moving parts move; a freeform weld's are rigid: in the rest frame)
            var d = ship.DefOf(p);
            for (var b = 0; b < d.SolidBoxes; b++)
            {
                ship.BoxOf(p, b, out var bc, out var bh);
                ModuleSockets.RestAabb(ship, ship.Parts[p].Seg, ref bc, ref bh);
                if (ObbAabb(c - bc, ax, ay, az, h - new Vector3(Skin), bh - new Vector3(Skin)))
                    return p;
            }
        }
        return -1;
    }

    /// <summary>Does an oriented box (its middle <paramref name="d"/> from an axis-aligned box's, axes, half sizes) overlap the axis-aligned one (<paramref name="e"/>)? The separating axis test, 15 axes.</summary>
    private static bool ObbAabb(Vector3 d, Vector3 ax, Vector3 ay, Vector3 az, Vector3 h, Vector3 e)
    {
        h = Vector3.Max(h, Vector3.Zero);
        e = Vector3.Max(e, Vector3.Zero);
        Span<Vector3> a = [ax, ay, az];
        Span<float> hh = [h.X, h.Y, h.Z];
        Span<Vector3> w = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
        bool Sep(Vector3 l, ReadOnlySpan<Vector3> a2, ReadOnlySpan<float> h2)
        {
            if (l.LengthSquared() < 1e-8f)
                return false;
            var ra = MathF.Abs(l.X) * e.X + MathF.Abs(l.Y) * e.Y + MathF.Abs(l.Z) * e.Z;
            var rb = MathF.Abs(Vector3.Dot(l, a2[0])) * h2[0] + MathF.Abs(Vector3.Dot(l, a2[1])) * h2[1] + MathF.Abs(Vector3.Dot(l, a2[2])) * h2[2];
            return MathF.Abs(Vector3.Dot(l, d)) > ra + rb;
        }
        for (var i = 0; i < 3; i++)
            if (Sep(w[i], a, hh) || Sep(a[i], a, hh))
                return false;
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                if (Sep(Vector3.Cross(w[i], a[j]), a, hh))
                    return false;
        return true;
    }
}
