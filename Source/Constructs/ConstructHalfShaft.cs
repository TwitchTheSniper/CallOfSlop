using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// A half shaft (docs/sandbox/CONSTRUCTS.md "Half shafts"): a shaft link that ends on a suspension's Drive in, a port on its
/// moving hub. Its nodes stay on the output's side and only its end moves: a CV joint and a plunge joint, so it may cross the
/// suspension's travel and a steering pivot, and is drawn each frame to where the hub is. The rest frame (joints at 0, the hub at
/// mid-travel) is what the routing rules check, with the two ends of the travel checked for the angle.
/// </summary>
public sealed partial class Construct
{
    /// <summary>What a point on suspension <paramref name="part"/>'s hub does at rest becomes now (no wheel on it: stays).</summary>
    public Vector3 HubPoint(int part, Vector3 rest)
    {
        for (var k = 0; k < WheelCount; k++)
            if (Wheels[k].Suspension == part)
            {
                HubMotion(k, out var shift, out var angle);
                return Leaned(k, rest, shift, angle);
            }
        return rest;
    }

    /// <summary>Where a point on suspension <paramref name="part"/>'s hub is with the hub <paramref name="rise"/> above mid-travel, and the way its lean turns a direction.</summary>
    public Vector3 HubPointAt(int part, Vector3 rest, float rise, out Vector3 leanAxis, out float lean)
    {
        leanAxis = Vector3.UnitZ;
        lean = 0;
        for (var k = 0; k < WheelCount; k++)
            if (Wheels[k].Suspension == part)
            {
                ref var w = ref Wheels[k];
                var keep = w.Compression;
                w.Compression = w.Travel * 0.5f + rise;
                HubMotion(k, out var shift, out var angle);
                w.Compression = keep;
                lean = angle * w.ArmTilt;
                leanAxis = w.ArmAxis;
                return Leaned(k, rest, shift, angle);
            }
        return rest + new Vector3(0, rise, 0);
    }

    private Vector3 Leaned(int k, Vector3 rest, Vector3 shift, float angle)
    {
        ref var w = ref Wheels[k];
        var lean = angle * w.ArmTilt;
        if (lean == 0)
            return rest + shift;
        PartBounds(w.Part, out var mn, out var mx);
        var hub = (mn + mx) * 0.5f;
        return hub + shift + Vector3.Transform(rest - hub, Quaternion.CreateFromAxisAngle(w.ArmAxis, lean));
    }
}

public static partial class ConstructRoutes
{
    public const float HalfShaftMaxDeg = 30f; // a half shaft's angle into its hub, at either end of the travel (it is a CV joint)
    public const float HalfShaftMinReach = 4f; // the reach of a small suspension's half shaft (the size of the part scales it)
    public const float HalfShaftMaxReach = 200f; // (a part's own reach: ConstructParts.MaxPartReach)
    public static readonly float HalfShaftSteerRad = 45f * MathF.PI / 180f; // steered: a CV joint's angle (a CV joint part's)

    /// <summary>Does a shaft ending on part <paramref name="to"/> make a half shaft?</summary>
    public static bool IsHalfShaft(Construct c, int to) => to >= 0 && to < c.PartCount && c.DefOf(to).Role == PartRole.Suspension;

    /// <summary>Is link <paramref name="r"/> a half shaft?</summary>
    public static bool IsHalfShaft(Construct c, in CRoute r) => r.Type == RouteType.Shaft && IsHalfShaft(c, r.To);

    /// <summary>The whole link's length at most, for a shaft into this suspension: bigger classes reach further.</summary>
    public static float HalfShaftReach(ConstructPartDef d)
    {
        var e = d.BoundsMax - d.BoundsMin;
        return Math.Clamp(4f * MathF.Max(e.X, MathF.Max(e.Y, e.Z)), HalfShaftMinReach, HalfShaftMaxReach);
    }

    /// <summary>Why a half shaft can't go from <paramref name="from"/> to suspension <paramref name="susp"/>'s side of a joint, or null: on the same moving piece, or the suspension on a steering pivot on it.</summary>
    public static string? HalfShaftCross(Construct c, int from, int susp)
    {
        var ms = c.MoveSeg(c.Parts[from].Seg);
        var mt = c.MoveSeg(c.Parts[susp].Seg);
        if (ms == mt)
            return null;
        if (mt > 0 && c.Joints[mt - 1].Type == JointType.Steering && c.MoveSeg(c.Joints[mt - 1].ParentSeg) == ms)
            return null;
        return "a half shaft crosses a suspension's travel or a steering pivot, no other joint: fit a U joint or CV joint there";
    }

    /// <summary>
    /// How far from the hub nothing clips a half shaft: the usual grace, and what the bracket is bolted to when the port's face is
    /// on that joint (a plain bracket's frame), since the shaft has to come through it. An arm's mount is not forgiven.
    /// </summary>
    public static float HalfShaftGrace(Construct c, int part, byte port, Vector3 at, Vector3 towards, bool inRest)
    {
        var g = Grace(Radius(RouteType.Shaft)) + ExitAlongIn(c, part, at, towards, inRest);
        if (PortOf(c.DefOf(part), RouteType.Shaft, port) is not { } np)
            return g;
        var worst = 0f;
        for (var e = c.FirstLink(part); e >= 0; e = c.NextLink(e))
        {
            ref var l = ref c.Links[e >> 1];
            var mine = l.A == part ? l.SA : l.SB;
            var other = l.A == part ? l.B : l.A;
            var sk = c.DefOf(part).Sockets[mine];
            if (sk.Dir == np.Face && Vector3.DistanceSquared(sk.At, np.At) < 1e-4f)
                worst = MathF.Max(worst, ExitAlongIn(c, other, at, towards, inRest));
        }
        return g + worst;
    }

    /// <summary>The port's point now: a suspension's Drive in rides on its hub. Any other port: <see cref="PortPoint"/>.</summary>
    public static Vector3 LivePort(Construct c, int part, byte port, RouteType t)
    {
        var p = PortPoint(c, part, port, t);
        return t == RouteType.Shaft && IsHalfShaft(c, part) ? c.HubPoint(part, p) : p;
    }

    /// <summary><see cref="Point"/> with a half shaft's end where the hub is now (drawing, hits).</summary>
    public static Vector3 LivePoint(Construct c, int r, int i, out int seg)
    {
        var p = Point(c, r, i, out seg);
        ref var rt = ref c.Routes[r];
        return i >= rt.Count && IsHalfShaft(c, rt) ? c.HubPoint(rt.To, p) : p;
    }

    /// <summary>The angle rule at a half shaft's end: at rest, then at both ends of the travel (<paramref name="last"/> the point before the port, <paramref name="end"/> the port, in the path's frame).</summary>
    public static string? HalfShaftAngle(Construct c, int part, Vector3 last, Vector3 end, Vector3 into, float bendRest, bool inRest)
    {
        if (bendRest > HalfShaftMaxDeg + AngleSlackDeg)
            return $"a half shaft goes into its hub at most {HalfShaftMaxDeg:0} degrees off its line ({bendRest:0} here)";
        var travel = c.DefOf(part).Travel * 0.5f;
        var seg = c.Parts[part].Seg;
        var rest = inRest ? FromRest(c, seg, last) : last;
        var port = inRest ? FromRest(c, seg, end) : end;
        var axis = into;
        if (inRest && seg > 0)
        {
            c.RestPose(seg, out var q, out _);
            axis = Vector3.Transform(into, Quaternion.Conjugate(q));
        }
        for (var s = -1; s <= 1; s += 2)
        {
            var at = c.HubPointAt(part, port, s * travel, out var leanAxis, out var lean);
            var d = at - rest;
            if (d.LengthSquared() < 1e-6f)
                continue;
            var turned = lean == 0 ? axis : Vector3.Transform(axis, Quaternion.CreateFromAxisAngle(leanAxis, lean));
            var bend = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(d), turned), -1f, 1f)) * 180f / MathF.PI;
            if (bend > HalfShaftMaxDeg + AngleSlackDeg)
                return $"at the end of its travel the half shaft is {bend:0} degrees off the hub (at most {HalfShaftMaxDeg:0}): come in straighter, or from further away";
        }
        return null;
    }

    private static Vector3 FromRest(Construct c, int seg, Vector3 p)
    {
        if (c.FreeformCount == 0 || seg <= 0)
            return p;
        c.RestPose(seg, out var q, out var t);
        return Vector3.Transform(p - t, Quaternion.Conjugate(q));
    }

    /// <summary>The look line of a half shaft that drives nothing: why the line ends there. Null for any other link, or one that drives its wheel.</summary>
    public static string? HalfShaftNote(Construct c, int route)
    {
        ref var r = ref c.Routes[route];
        if (!IsHalfShaft(c, r))
            return null;
        return Wheelless(c, r.To) ?? (Driven(c, r.To, out var caster) ? null : caster ? "a caster wheel can't be driven" : c.DriveProblem.Length > 0 ? c.DriveProblem : "nothing on the drive line turns this shaft");
    }

    /// <summary>The look line of a suspension with a half shaft in and no wheel on it, or null.</summary>
    public static string? HalfShaftLook(Construct c, int part)
    {
        for (var k = 0; k < c.RouteCount; k++)
            if (c.Routes[k].To == part && IsHalfShaft(c, c.Routes[k]))
                return Wheelless(c, part);
        return null;
    }

    private static string? Wheelless(Construct c, int part)
    {
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Suspension == part)
                return null;
        return "no wheel on it: the half shaft ends here";
    }

    private static bool Driven(Construct c, int part, out bool caster)
    {
        caster = false;
        for (var k = 0; k < c.WheelCount; k++)
            if (c.Wheels[k].Suspension == part)
            {
                caster = !c.Wheels[k].Drivable;
                return c.Wheels[k].Driven;
            }
        return false;
    }
}
