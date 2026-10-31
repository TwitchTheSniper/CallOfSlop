using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox third-person camera (board item dev-third-person-tp01). Sandbox only: other modes have no third person
/// (fair play), and Drop item keeps T there. The view mode is local to each player (nothing is sent; others just see you)
/// and is remembered per context: on foot, and in a seat.
///
/// On foot: an over-the-shoulder orbit camera, <see cref="FootDist"/> behind the eye and <see cref="Shoulder"/> to the
/// side; the mouse turns the view as always; the camera is a sphere cast from the eye, so a wall pulls it in and it never
/// sits inside geometry. The crosshair is where the screen centre hits, but anything the player does from it (a shot, a
/// ghost part, the linking tool's ray) starts at the point of the camera ray nearest the eye and a shot leaves the eye
/// towards that point, so the camera can't shoot round a corner the player can't.
///
/// In a seat: a free orbit camera round the seat's orbit point (the construct's centre of mass raised by half its height,
/// moved by the seat's "Third-person camera" settings); the mouse orbits, the wheel zooms, a wall pulls it in. A gunner's
/// look follows the crosshair.
///
/// The functions here are pure (a world query in, a position out) so the self-test drives them; the per-frame glue is
/// in Program.ThirdPerson.cs. One sphere cast and one ray a frame, no allocations.
/// </summary>
public static class ThirdPerson
{
    public const float FootMin = 1.5f, FootMax = 8f, FootDefault = 3f;
    public const float ShoulderOffset = 0.5f;
    public const float CameraRadius = 0.25f;
    public const float SeatPitchMin = -1.4f, SeatPitchMax = 1.3f;

    /// <summary>The remembered mode per context.</summary>
    public static bool FootOn, SeatOn;

    /// <summary>The shoulder the camera sits over: +1 right, -1 left.</summary>
    public static float Side = 1f;

    public static float FootDist = FootDefault;

    /// <summary>The seat orbit: heading, pitch (looking down is negative) and distance.</summary>
    public static float OrbitYaw, OrbitPitch = -0.25f, SeatDist = 10f;

    // ---- per frame ---------------------------------------------------------------------------------------------------

    /// <summary>Is the third-person view on this frame (the mode of the context the local player is in)?</summary>
    public static bool Active;

    /// <summary>On this frame: seated (the orbit camera) rather than on foot.</summary>
    public static bool Seated;

    /// <summary>The solved camera of the last frame drawn: position and forward.</summary>
    public static Vector3 Cam, CamFwd;

    /// <summary>Is <see cref="Cam"/> a solved camera (a frame was drawn in third person)?</summary>
    public static bool HaveCam;

    /// <summary>How much of the wanted camera distance the last frame's cast left (1: free, 0: against the eye); the input between frames places the camera from it without a cast.</summary>
    public static float PullRatio = 1f;

    /// <summary>Flip the view of a context: on foot, or (<paramref name="seated"/>) in a seat. Each remembers its own mode.</summary>
    public static void Toggle(bool seated)
    {
        if (seated)
            SeatOn = !SeatOn;
        else
            FootOn = !FootOn;
    }

    /// <summary>
    /// A seat's orbit point in the world, for the construct at <paramref name="pos"/> and <paramref name="rot"/>: the
    /// centre of mass raised by half the construct's height, moved by the seat's settings (in the construct's frame, z forward).
    /// </summary>
    public static Vector3 SeatPoint(Construct c, int part, Vector3 pos, Quaternion rot, out float dist, out float zoomMin, out float zoomMax, out bool follow)
    {
        ref var sp = ref c.Parts[part];
        var d = sp.D;
        var local = c.Com + new Vector3(0, (c.LocalMax.Y - c.LocalMin.Y) * 0.5f, 0)
                    + new Vector3(ConstructConfig.Get(sp, d, CfgKey.OrbitX, 0), ConstructConfig.Get(sp, d, CfgKey.OrbitY, 0), -ConstructConfig.Get(sp, d, CfgKey.OrbitZ, 0));
        dist = ConstructConfig.Get(sp, d, CfgKey.OrbitDist, 10);
        zoomMin = ConstructConfig.Get(sp, d, CfgKey.ZoomMin, 3);
        zoomMax = MathF.Max(zoomMin, ConstructConfig.Get(sp, d, CfgKey.ZoomMax, 40));
        follow = ConstructConfig.Get(sp, d, CfgKey.FollowYaw, 0) >= 0.5f;
        return pos + Vector3.Transform(local, rot);
    }

    public static float Clamp(float dist, float min, float max) => Math.Clamp(float.IsFinite(dist) ? dist : min, min, max);

    /// <summary>Where the on-foot camera wants to be (before walls): behind the eye along the view, over the shoulder, a little up.</summary>
    public static Vector3 FootIdeal(Vector3 eye, Vector3 fwd, Vector3 up, float dist, float side)
    {
        var right = Vector3.Cross(fwd, up);
        right = right.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(right);
        return eye - fwd * dist + right * (side * ShoulderOffset) + up * 0.1f;
    }

    /// <summary>The camera between <paramref name="from"/> and <paramref name="to"/>: a sphere swept from <paramref name="from"/>, stopped short of whatever it meets.</summary>
    public static Vector3 Pull(IWorldQuery q, Vector3 from, Vector3 to, float radius = CameraRadius)
    {
        var d = to - from;
        var len = d.Length();
        if (!(len > 1e-3f) || !float.IsFinite(len))
            return from;
        d /= len;
        if (q.SphereCast(from, radius, d, len, QueryFilter.All, out var hit))
            len = Math.Clamp(hit.Distance - 0.02f, 0f, len);
        return from + d * len;
    }

    public static Vector3 FootCamera(IWorldQuery q, Vector3 eye, Vector3 fwd, Vector3 up, float dist, float side) =>
        Pull(q, eye, FootIdeal(eye, fwd, up, dist, side));

    /// <summary>The seat orbit camera: <paramref name="dist"/> back from <paramref name="point"/> along the view, pulled in by obstacles.</summary>
    public static Vector3 OrbitCamera(IWorldQuery q, Vector3 point, Vector3 fwd, float dist) => Pull(q, point, point - fwd * dist);

    /// <summary>
    /// Where a player's actions start in third person: the point of the camera's ray nearest the eye. Everything from
    /// there on is what the camera sees, and reach is measured from the player, not the camera.
    /// </summary>
    public static Vector3 AimOrigin(Vector3 cam, Vector3 camFwd, Vector3 eye) => cam + camFwd * MathF.Max(0f, Vector3.Dot(eye - cam, camFwd));

    /// <summary>What the crosshair is on: the first thing the camera's ray meets from <see cref="AimOrigin"/> (up to <paramref name="range"/>).</summary>
    public static Vector3 AimTarget(IWorldQuery q, Vector3 cam, Vector3 camFwd, Vector3 eye, float range)
    {
        var o = AimOrigin(cam, camFwd, eye);
        return o + camFwd * q.Raycast(o, camFwd, range, QueryFilter.Shots);
    }

    /// <summary>The direction a shot leaves the eye in so it meets the crosshair's target, or stops at the first wall in the way (the gun can't see round corners).</summary>
    public static Vector3 AimDir(IWorldQuery q, Vector3 cam, Vector3 camFwd, Vector3 eye, float range) => DirTo(AimTarget(q, cam, camFwd, eye, range), eye, camFwd);

    /// <summary>The unit direction from the eye to <paramref name="target"/> (the camera's own when the target is on top of the eye).</summary>
    public static Vector3 DirTo(Vector3 target, Vector3 eye, Vector3 camFwd)
    {
        var v = target - eye;
        var len = v.Length();
        return len < 0.5f || !float.IsFinite(len) ? camFwd : v / len;
    }
}
