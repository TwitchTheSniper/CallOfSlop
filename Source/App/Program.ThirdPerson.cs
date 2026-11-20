using System.Numerics;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The Sandbox third-person camera on this PC (Source/App/ThirdPerson.cs has the rules): the toggle and shoulder keys,
/// the seat orbit's mouse, the wheel, the camera for the frame and the aim of shots and tools from the crosshair.
/// </summary>
public static unsafe partial class Program
{
    private static ulong _tpSeatKey;
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
        var piloting = seated && g.LocalDrone() != null; // (its own mode: a seat's remembered third person would be the chair, not the drone)
        if (_mouseCaptured && Pressed(GameAction.ThirdPerson))
            ThirdPerson.Toggle(seated, piloting);
        if (_mouseCaptured && Pressed(GameAction.ThirdPersonSide))
            ThirdPerson.Side = -ThirdPerson.Side;
        ThirdPerson.Seated = seated;
        ThirdPerson.Active = piloting ? ThirdPerson.PilotOn : seated ? ThirdPerson.SeatOn : ThirdPerson.FootOn;
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
        if (g.Constructs.Get(h.SeatBody) is { } sc && h.SeatPart < sc.PartCount && g.LocalDrone() is { } d)
        {
            PilotThirdPersonAim(g, sc, h.SeatPart, d);
            return;
        }
        if (g.Constructs.Get(h.SeatBody) is not { } c || h.SeatPart >= c.PartCount)
            return;
        if (ConstructFlight.MouseSteers(c, h.SeatPart))
        {
            // A ship that steers by the look: the camera is the look, so the nose comes round to where the camera points (its first tilt counts as level).
            var pitch = Math.Clamp(ThirdPerson.OrbitPitch - ThirdPerson.SteerTilt * MathF.Max(0, 1 - MathF.Abs(ThirdPerson.OrbitPitch) / 1.5f), -1.5f, 1.5f); // (the camera straight up or down is the nose straight up or down)
            if (g.SeatOrbitRot(out var sr))
                g.SetLookDir(Vector3.Transform(World.Forward(ThirdPerson.OrbitYaw, pitch), sr)); // (in the ship's frame: GameSandbox.cs, SeatOrbitRot)
            else
            {
                g.LookYaw = ThirdPerson.OrbitYaw;
                g.LookPitch = pitch;
            }
            return;
        }
        if (c.SeatKindOf(h.SeatPart) != SeatKind.Gunner)
            return;
        var f = SeatOrbitDir(g);
        if (!g.ThirdPersonSeatPoint(out var point, out _, out _, out _, out _, out _))
            return;
        var camPos = point - f * (ThirdPerson.SeatDist * ThirdPerson.PullRatio); // (the camera as the orbit has moved it since the last frame)
        var target = GunnerAimTarget(g, me, c, h.SeatPart, camPos, f, eye);
        if (GravityFrames.Curved && (target - eye).LengthSquared() > 1e-6f)
        {
            var (yaw, pitch) = GravityFrames.Angles(g.LookFrame, target - eye); // (the look's own frame: flat angles on a planet put the guns off the crosshair)
            g.LookYaw = yaw;
            g.LookPitch = Math.Clamp(pitch, -1.5f, 1.5f);
        }
        else if (DeathCam.LookAt(eye, target) is { } look)
        {
            g.LookYaw = look.Yaw;
            g.LookPitch = Math.Clamp(look.Pitch, -1.5f, 1.5f);
        }
    }

    /// <summary>
    /// What a gunner seat's third-person crosshair is on: the first thing the camera's ray meets (the map, another construct, a missile in the
    /// air), never the gunner's own ship or what it carries, out to the reach of the seat's guns. The seat's look is then the way from the eye
    /// to that point, so the server's look from the eye (ConstructWeapons.GunnerPoint) meets the same thing and the guns converge where the
    /// crosshair is. Only the map was cast before, out to 200 m: in space a ship under the crosshair wasn't seen, and the guns converged on a
    /// point off it by the camera's offset from the eye (tens of metres on a frigate).
    /// </summary>
    private static Vector3 GunnerAimTarget(Game g, Player me, Construct c, int seat, Vector3 camPos, Vector3 f, Vector3 eye)
    {
        var reach = 0f;
        for (var k = 0; k < c.GunCount; k++)
            if (c.Guns[k].Seat == seat && c.Guns[k].Def != null)
                reach = MathF.Max(reach, c.Guns[k].MaxRange);
        if (reach <= 0)
            reach = World.Range;
        var o = ThirdPerson.AimOrigin(camPos, f, eye);
        // Cast where the bodies are at the tick (the host draws between ticks: at orbital speed a tick is metres), placed back on the drawn ray.
        var to = o;
        var tf = f;
        g.AimToTick(me, ref to, ref tf);
        var dist = World.Query.Raycast(to, tf, reach, QueryFilter.Shots);
        if (g.Constructs.Raycast(to, tf, dist, out var hit, c, ignoreCargo: true) && hit.Distance < dist)
            dist = hit.Distance;
        if (g.Constructs.ShellCount > 0 && g.Constructs.RayShell(to, tf, dist, out var sd, out _) && sd < dist)
            dist = sd;
        return o + f * dist;
    }

    /// <summary>
    /// Third person on a drone whose receiver aims guns: the look follows the orbit's crosshair, as a gunner seat's does. The guns are aimed from the
    /// remote sight (ConstructWeapons.RemoteSight), so the look is the way from there to what the camera's ray meets (other bodies too, never the drone),
    /// turned back from the receiver's frame into the console seat's (the inverse of ConstructRemote.MapLook).
    /// </summary>
    private static void PilotThirdPersonAim(Game g, Construct c, int seat, Construct d)
    {
        var rx = d.Receiver;
        if (rx < 0 || rx >= d.PartCount || !d.SeatAims(rx) || !ConstructWeapons.RemoteSight(d, rx, g.TickAlpha, true, out var sight, g.IsServer))
            return;
        var f = g.LookDir(ThirdPerson.OrbitYaw, ThirdPerson.OrbitPitch);
        if (!g.ThirdPersonSeatPoint(out var point, out _, out _, out _, out _, out _))
            return;
        var camPos = point - f * (ThirdPerson.SeatDist * ThirdPerson.PullRatio);
        var o = ThirdPerson.AimOrigin(camPos, f, sight);
        var dist = World.Query.Raycast(o, f, World.Range, QueryFilter.Shots);
        if (g.Constructs.Raycast(o, f, dist, out var hit, d, ignoreCargo: true) && hit.Distance < dist)
            dist = hit.Distance;
        var w = ThirdPerson.DirTo(o + f * dist, sight, f);
        ref var r = ref d.Parts[rx];
        d.WorldFrame(r.Seg, out var rq, out _);
        ref var s = ref c.Parts[seat];
        c.WorldFrame(s.Seg, out var sq, out _);
        var look = Vector3.Transform(Vector3.Transform(w, Quaternion.Conjugate(rq * Rot24.Quat(r.Rot))), sq * Rot24.Quat(s.Rot));
        g.LookYaw = MathF.Atan2(look.X, -look.Z);
        g.LookPitch = Math.Clamp(MathF.Asin(Math.Clamp(look.Y, -1f, 1f)), -1.5f, 1.5f);
    }

    /// <summary>The seat orbit camera's way: in a ship's frame (<see cref="Game.SeatOrbitRot"/>), else measured against the planet's up.</summary>
    private static Vector3 SeatOrbitDir(Game g) =>
        g.SeatOrbitRot(out var r) ? Vector3.Transform(World.Forward(ThirdPerson.OrbitYaw, ThirdPerson.OrbitPitch), r) : g.LookDir(ThirdPerson.OrbitYaw, ThirdPerson.OrbitPitch);

    /// <summary>The on-foot camera for this frame's look without a cast: last frame's pull applied to the wanted spot.</summary>
    private static Vector3 FootCameraNow(Vector3 eye, Vector3 fwd)
    {
        ThirdPerson.Size = _game.Local is { } me ? MathF.Max(1f, ShellFrames.ScaleOf(me)) : 1f; // (a big shell frame: framed as a person is)
        var ideal = ThirdPerson.FootIdeal(eye, fwd, GravityFrames.UpAt(eye), ThirdPerson.Clamp(ThirdPerson.FootDist, ThirdPerson.FootMin, ThirdPerson.FootMax), ThirdPerson.Side);
        return eye + (ideal - eye) * ThirdPerson.PullRatio;
    }

    /// <summary>The Sandbox crosshair ray: the eye and the look, or in third person the camera ray's point nearest the eye and the camera's look.</summary>
    private static void SandboxAimRay(Game g, Player me, out Vector3 eye, out Vector3 dir)
    {
        if (GantryOn)
        {
            GantryRay(out eye, out dir); // (the Build Gantry's camera: along the cursor's ray)
            return;
        }
        eye = me.RenderEye;
        dir = g.LookDir(g.LookYaw, g.LookPitch);
        if (ThirdPerson.Active && ThirdPerson.HaveCam && !ThirdPerson.Seated)
            eye = ThirdPerson.AimOrigin(FootCameraNow(eye, dir), dir, eye);
        g.AimToTick(me, ref eye, ref dir); // (the host: from the drawn frame to the tick's, where constructs are hit and reach is measured)
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
            ThirdPerson.Size = MathF.Max(1f, ShellFrames.ScaleOf(local)); // (a big shell frame: framed as a person is)
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
            var drone = _game.LocalDrone();
            var key = drone != null ? 1UL << 40 | drone.Id : (ulong)h.SeatBody << 16 | (uint)(h.SeatPart + 1); // (a part index is up to 16 bits: the old body << 8 mixed seats past part 255)
            var shipFrame = _game.SeatOrbitRot(out var sr); // (in a ship the orbit turns with it: its yaw and pitch are in the ship's frame, GameSandbox.cs)
            if (key != _tpSeatKey)
            {
                _tpSeatKey = key;
                ThirdPerson.SeatDist = dist;
                if (shipFrame)
                    ConstructRide.LookIn(sr, _game.LookDir(_game.LookYaw, _game.LookPitch), out ThirdPerson.OrbitYaw, out _);
                else
                    ThirdPerson.OrbitYaw = drone != null ? heading : _game.LookYaw; // (a drone's starts from behind it)
                ThirdPerson.OrbitPitch = ThirdPerson.SteerTilt;
                _tpHeading = heading;
            }
            if (follow && !shipFrame)
                ThirdPerson.OrbitYaw = World.WrapAngle(ThirdPerson.OrbitYaw + World.WrapAngle(heading - _tpHeading));
            _tpHeading = heading;
            ThirdPerson.SeatDist = ThirdPerson.Clamp(ThirdPerson.SeatDist, zoomMin, zoomMax);
            f = SeatOrbitDir(_game);
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
