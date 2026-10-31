using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox third-person camera on this PC (Source/App/ThirdPerson.cs has the rules): the toggle and shoulder keys,
/// the seat orbit's mouse, the wheel, the camera for the frame and the aim of shots and tools from the crosshair.
/// </summary>
public static unsafe partial class Program
{
    private static uint _tpSeatKey;
    private static float _tpHeading;

    /// <summary>Before the mouse look: the keys, and which context the view is in. True when the mouse orbits the seat camera instead of turning the player.</summary>
    private static bool ThirdPersonInput()
    {
        var g = _game;
        if (!g.SandboxOn || g.Local is not { Alive: true } me || InLobbyRoom)
        {
            ThirdPerson.Active = ThirdPerson.HaveCam = false;
            g.ThirdAimDir = null;
            return false;
        }
        var seated = g.SandboxHands.Hand(me.Id).Seated;
        if (!seated)
            _tpSeatKey = 0; // (a seat sat in again starts from its own distance and heading)
        if (_mouseCaptured && Pressed(GameAction.ThirdPerson))
            ThirdPerson.Toggle(seated);
        if (_mouseCaptured && Pressed(GameAction.ThirdPersonSide))
            ThirdPerson.Side = -ThirdPerson.Side;
        ThirdPerson.Seated = seated;
        ThirdPerson.Active = seated ? ThirdPerson.SeatOn : ThirdPerson.FootOn;
        if (!ThirdPerson.Active)
        {
            ThirdPerson.HaveCam = false;
            g.ThirdAimDir = null;
        }
        return ThirdPerson.Active && seated;
    }

    /// <summary>The mouse orbiting the seat camera.</summary>
    private static void ThirdPersonOrbit(double2 delta)
    {
        var sens = BaseSensitivity * _settings.Sensitivity;
        var dy = (float)delta.Y * (_settings.InvertY ? -1 : 1);
        ThirdPerson.OrbitYaw = World.WrapAngle(ThirdPerson.OrbitYaw + (float)delta.X * sens);
        ThirdPerson.OrbitPitch = Math.Clamp(ThirdPerson.OrbitPitch - dy * sens, ThirdPerson.SeatPitchMin, ThirdPerson.SeatPitchMax);
    }

    /// <summary>The wheel zooms the camera while the third-person view is on.</summary>
    private static bool ThirdPersonScroll(double y)
    {
        if (!ThirdPerson.Active)
            return false;
        if (ThirdPerson.Seated)
            ThirdPerson.SeatDist *= y > 0 ? 0.88f : 1f / 0.88f;
        else
            ThirdPerson.FootDist -= (float)Math.Sign(y) * 0.5f;
        return true;
    }

    /// <summary>
    /// After the mouse look, before the tools: where the crosshair is aimed. On foot the shot direction (towards the
    /// crosshair's target, from the eye); in a gunner's seat the look follows the crosshair.
    /// </summary>
    private static void ThirdPersonAim()
    {
        var g = _game;
        if (!ThirdPerson.Active || !ThirdPerson.HaveCam || g.Local is not { Alive: true } me)
        {
            g.ThirdAimDir = null;
            return;
        }
        var eye = me.RenderEye;
        if (!ThirdPerson.Seated)
        {
            var fwd = g.LookDir(g.LookYaw, g.LookPitch);
            var cam = FootCameraNow(eye, fwd);
            g.ThirdAimDir = ThirdPerson.AimDir(World.Query, cam, fwd, eye, World.Range);
            return;
        }
        g.ThirdAimDir = null;
        ref var h = ref g.SandboxHands.Hand(me.Id);
        if (g.Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount || c.SeatKindOf(h.SeatPart) != SeatKind.Gunner)
            return;
        var f = g.LookDir(ThirdPerson.OrbitYaw, ThirdPerson.OrbitPitch);
        if (!g.ThirdPersonSeatPoint(out var point, out _, out _, out _, out _, out _))
            return;
        var camPos = point - f * (ThirdPerson.SeatDist * ThirdPerson.PullRatio); // (the camera as the orbit has moved it since the last frame)
        var target = ThirdPerson.AimTarget(World.Query, camPos, f, eye, World.Range);
        if (DeathCam.LookAt(eye, target) is { } look)
        {
            g.LookYaw = look.Yaw;
            g.LookPitch = Math.Clamp(look.Pitch, -1.5f, 1.5f);
        }
    }

    /// <summary>The on-foot camera for this frame's look without a cast: last frame's pull applied to the wanted spot.</summary>
    private static Vector3 FootCameraNow(Vector3 eye, Vector3 fwd)
    {
        var ideal = ThirdPerson.FootIdeal(eye, fwd, GravityFrames.UpAt(eye), ThirdPerson.Clamp(ThirdPerson.FootDist, ThirdPerson.FootMin, ThirdPerson.FootMax), ThirdPerson.Side);
        return eye + (ideal - eye) * ThirdPerson.PullRatio;
    }

    /// <summary>The Sandbox crosshair ray: the eye and the look, or in third person the camera ray's point nearest the eye and the camera's look.</summary>
    private static void SandboxAimRay(Game g, Player me, out Vector3 eye, out Vector3 dir)
    {
        eye = me.RenderEye;
        dir = g.LookDir(g.LookYaw, g.LookPitch);
        if (ThirdPerson.Active && ThirdPerson.HaveCam && !ThirdPerson.Seated)
            eye = ThirdPerson.AimOrigin(FootCameraNow(eye, dir), dir, eye);
    }

    /// <summary>In the frame: puts the camera where the third-person view wants it (one sphere cast). True when it did (no bob, no recoil roll, no first-person gun).</summary>
    private static bool ThirdPersonCamera(Player local, ref Vector3 eye, ref Vector3 fwd)
    {
        if (!ThirdPerson.Active || !local.Alive || !_game.SandboxOn)
        {
            ThirdPerson.HaveCam = false;
            return false;
        }
        var q = World.Query;
        Vector3 cam, f, from, ideal;
        if (!ThirdPerson.Seated)
        {
            from = local.RenderEye;
            f = _game.LookDir(_game.LookYaw, _game.LookPitch);
            ThirdPerson.FootDist = ThirdPerson.Clamp(ThirdPerson.FootDist, ThirdPerson.FootMin, ThirdPerson.FootMax);
            ideal = ThirdPerson.FootIdeal(from, f, GravityFrames.UpAt(from), ThirdPerson.FootDist, ThirdPerson.Side);
        }
        else
        {
            if (!_game.ThirdPersonSeatPoint(out from, out var heading, out var dist, out var zoomMin, out var zoomMax, out var follow))
            {
                ThirdPerson.HaveCam = false;
                return false;
            }
            ref var h = ref _game.SandboxHands.Hand(local.Id);
            var key = ((uint)h.SeatBody << 8) | (uint)(h.SeatPart + 1);
            if (key != _tpSeatKey)
            {
                _tpSeatKey = key;
                ThirdPerson.SeatDist = dist;
                ThirdPerson.OrbitYaw = _game.LookYaw;
                ThirdPerson.OrbitPitch = -0.25f;
                _tpHeading = heading;
            }
            if (follow)
                ThirdPerson.OrbitYaw = World.WrapAngle(ThirdPerson.OrbitYaw + World.WrapAngle(heading - _tpHeading));
            _tpHeading = heading;
            ThirdPerson.SeatDist = ThirdPerson.Clamp(ThirdPerson.SeatDist, zoomMin, zoomMax);
            f = _game.LookDir(ThirdPerson.OrbitYaw, ThirdPerson.OrbitPitch);
            ideal = from - f * ThirdPerson.SeatDist;
        }
        cam = ThirdPerson.Pull(q, from, ideal);
        var want = Vector3.Distance(from, ideal);
        ThirdPerson.PullRatio = want > 1e-3f ? Math.Clamp(Vector3.Distance(from, cam) / want, 0f, 1f) : 1f;
        ThirdPerson.Cam = cam;
        ThirdPerson.CamFwd = f;
        ThirdPerson.HaveCam = true;
        eye = cam;
        fwd = f;
        return true;
    }
}
