using System.Numerics;

namespace CallOfSlop;

public sealed partial class Game
{
    /// <summary>Sandbox third person (Source/App/ThirdPerson.cs): the direction a shot or throw leaves the eye in, towards what the crosshair is on; null in first person.</summary>
    public Vector3? ThirdAimDir;

    /// <summary>The direction the local player's shots and throws take: the look, or in third person towards the crosshair's target.</summary>
    public Vector3 AimDir() => ThirdAimDir ?? LookDir(LookYaw, LookPitch);

    /// <summary>
    /// The local player's seat orbit point (world) and the construct's heading, for the seat camera: the centre of mass
    /// raised by half the construct's height, moved by the seat's third-person settings (in the construct's frame, z forward),
    /// at the construct as it is drawn this frame. False when not seated.
    /// </summary>
    public bool ThirdPersonSeatPoint(out Vector3 point, out float heading, out float dist, out float zoomMin, out float zoomMax, out bool follow)
    {
        point = default;
        heading = 0;
        dist = 10;
        zoomMin = 3;
        zoomMax = 40;
        follow = false;
        if (!SandboxOn)
            return false;
        ref var h = ref SandboxHands.Hand(LocalId);
        if (h.SeatBody == 0 || Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || c.Parts[h.SeatPart].D.Role != PartRole.Seat)
            return false;
        var pos = IsServer ? Vector3.Lerp(c.PrevPos, c.Pos, _alpha) : c.Pos;
        var rot = IsServer ? Quaternion.Slerp(c.PrevRot, c.Rot, _alpha) : c.Rot;
        point = ThirdPerson.SeatPoint(c, h.SeatPart, pos, rot, out dist, out zoomMin, out zoomMax, out follow);
        heading = SeatYaw(rot);
        return true;
    }
}
