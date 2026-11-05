using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The wheel's path through its travel (docs/sandbox/CONSTRUCTS.md "Suspension types"). The raycast wheel stays what it is: one ray
/// straight down the body, one spring on how far the ground has pushed the wheel up. A suspension with an arm (<c>"path"</c> in
/// its part file: an arc) only changes where that ray lies and where the wheel is drawn: as the hub rises it moves in or out along
/// the arm, and leans by the arm's angle. Spring, damping and load don't change, so a swing arm carries what a strut does.
/// </summary>
public sealed partial class Construct
{
    /// <summary>A wheel's arm in the body's frame, from where the hinge and the hub sit at rest. An arm stood on end (a hinge running up, or no way to reach the travel) leaves the wheel on a straight line.</summary>
    private static void BuildArm(ref CWheel w, in CPart s, ConstructPartDef sd, Vector3 hub)
    {
        if (!sd.PathArc || w.Up.Y < 0.999f)
            return; // (welded on at an angle the arm's plane isn't level: the wheel moves in a straight line)
        var axis = ConstructParts.DirVector(Rot24.ApplyDir(s.Rot, sd.PathAxis));
        if (MathF.Abs(axis.Y) > 0.5f)
            return;
        var arm = hub - (s.Pos + Rot24.Apply(s.Rot, sd.PathPivot));
        var way = Vector3.Cross(axis, Vector3.UnitY);
        if (Vector3.Dot(way, arm) < 0)
            way = -way;
        var reach = Vector3.Dot(way, arm);
        var drop = arm.Y;
        var len = sd.PathLength > 0 ? sd.PathLength : MathF.Sqrt(reach * reach + drop * drop);
        if (reach < 1e-3f || len < MathF.Abs(drop) + w.Travel * 0.5f + 0.01f)
            return;
        w.ArmOut = way;
        w.ArmAxis = Vector3.Cross(way, Vector3.UnitY);
        w.ArmLen = len;
        w.ArmDrop = drop;
        w.ArmRest = MathF.Sqrt(len * len - drop * drop);
        w.ArmAngle = MathF.Asin(drop / len);
        w.ArmTilt = sd.PathTilt;
    }

    /// <summary>How far an arm's hub is out from its place at rest at this compression (nothing for a straight path).</summary>
    private static float ArmReach(in CWheel w) => ArmReachAt(in w, w.Compression);

    private static float ArmReachAt(in CWheel w, float compression)
    {
        if (w.ArmLen <= 0)
            return 0;
        var y = w.ArmDrop + compression - w.Travel * 0.5f;
        return MathF.Sqrt(MathF.Max(0, w.ArmLen * w.ArmLen - y * y)) - w.ArmRest;
    }

    /// <summary>What wheel <paramref name="k"/>'s hub has done from its place at rest (mid-travel): how far it moved (in the frame the wheel hangs in) and the arm's angle.</summary>
    public void HubMotion(int k, out Vector3 shift, out float angle)
    {
        ref var w = ref Wheels[k];
        var rise = w.Compression - w.Travel * 0.5f;
        shift = w.ArmOut * ArmReach(in w) + w.Up * rise;
        angle = w.ArmLen > 0 ? MathF.Asin(Math.Clamp((w.ArmDrop + rise) / w.ArmLen, -1, 1)) - w.ArmAngle : 0;
    }

    /// <summary>Wheel <paramref name="k"/>'s hub as drawn, in the frame of its segment at rest, and its axle with the arm's lean: the centre and axis of its cylinder.</summary>
    public void HubFrame(int k, out Vector3 hub, out Vector3 axle)
    {
        ref var w = ref Wheels[k];
        HubMotion(k, out var shift, out var angle);
        hub = w.Centre - w.Up * (w.Travel * 0.5f) + shift;
        var lean = angle * w.ArmTilt;
        axle = lean == 0 ? w.Axle : Vector3.Transform(w.Axle, Quaternion.CreateFromAxisAngle(w.ArmAxis, lean));
    }

    /// <summary>The hub at <paramref name="compression"/> (<see cref="HubFrame"/>), for the bounds of its whole travel.</summary>
    internal Vector3 HubAt(int k, float compression)
    {
        ref var w = ref Wheels[k];
        return w.Centre + w.Up * (compression - w.Travel) + w.ArmOut * ArmReachAt(in w, compression);
    }

    /// <summary>Is this suspension an arm whose wheel can't swing, because it was fitted with its hinge running up?</summary>
    public bool ArmOnEnd(int part)
    {
        if (!Parts[part].D.PathArc)
            return false;
        for (var k = 0; k < WheelCount; k++)
            if (Wheels[k].Suspension == part)
                return Wheels[k].ArmLen <= 0;
        return false;
    }
}
