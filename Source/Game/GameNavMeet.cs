using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// A rendezvous from the command view (docs/space/COMMAND.md "Rendezvous"; Karl, 2026-11-18: "one ship to match another ship's
/// velocity or orbit and meet up alongside"). A Meet order names another craft (<see cref="NavOrder.Target"/>) and a gap
/// (<see cref="NavOrder.Radius"/>, metres clear of it). The ship flies to a point beside it, on its side (along its X, the side
/// it comes from), both their sizes plus the gap from its middle, matches its motion there and holds it until cancelled, its
/// drive turned parallel to the other's. In space that is the <see cref="NavGuidance.GoTo"/> law in the other craft's moving
/// frame, with the other's own acceleration fed forward (an orbit, a burn); round another planet it first flies the trip there,
/// as an orbit order does. In a sky both are physics bodies and its autopilot flies it to the point beside the other at the
/// other's height. A ship in a sky climbs out to a craft in space; one in space is refused a craft down in a sky.
/// </summary>
public sealed partial class Game
{
    /// <summary>The gap a rendezvous keeps between the two hulls when none is given, and the least and most it may be (m).</summary>
    public const float NavMeetGap = 40, NavMeetGapLeast = 10, NavMeetGapMost = 2000;

    /// <summary>How far off (m) a rendezvous plans its approach on the whole drive; closer in the push it plans on falls to <see cref="NavComeAccel"/> by the other craft.</summary>
    public const double NavMeetFar = 20000;

    /// <summary>Host: a player's Meet order checked; its world (the one the other craft is round) and its gap set. Null, or why it takes none.</summary>
    private string? NavMeetCheck(SpaceSession s, Construct c, ushort target, ref int body, ref double gap)
    {
        if (Constructs.Get(target) is not { Wreck: false, PartCount: > 0 } t || t == c)
            return "That ship is gone";
        if (c.CarriedBy == t.Id || t.CarriedBy == c.Id)
            return "They are docked together already";
        var now = s.Now;
        if (!NavState(c, now, out var cp, out _) || !NavState(t, now, out var tp, out _))
            return "They are in different star systems";
        if (c.SpaceShip != 0 && t.SpaceShip == 0)
            return "It is out in space and the other is in the sky: bring it down into the sky first";
        if (c.SpaceShip == 0 && t.SpaceShip != 0 && SpaceEdge <= 0)
            return "There is no way to space from here";
        body = c.SpaceShip == 0 ? s.Bubble.Body : NavGuidance.PrimaryAt(c.SpaceAway != 0 ? Galaxy.System(c.SpaceAway - 1) : s.System, tp, now);
        gap = Math.Clamp(gap > 0 ? gap : NavMeetGap, NavMeetGapLeast, NavMeetGapMost);
        return null;
    }

    /// <summary>Host, every tick of a Meet order: null while it goes on, or why it ends.</summary>
    private string? NavMeetEnds(NavOrder o, Construct c)
    {
        if (Constructs.Get(o.Target) is not { Wreck: false } t)
            return "the ship it was to meet is gone";
        if (c.SpaceShip != 0 && t.SpaceShip == 0)
            return "the ship it was to meet went down into the sky";
        if (t.CarriedBy == c.Id || c.CarriedBy == t.Id)
            return "they are docked together";
        return null;
    }

    /// <summary>Host, in space: the world a Meet order is about is the one the other craft is round now (not while a trip there is planned or flown).</summary>
    private void NavMeetBody(NavOrder o, StarSystem sys, SpacePos pos, SpaceTime t)
    {
        if (o.Cruise || o.Planning || Constructs.Get(o.Target) is not { } meet || !NavState(meet, t, out var tp, out _))
            return;
        var body = NavGuidance.PrimaryAt(sys, tp, t);
        o.Body = (byte)body;
        if (o.Handed && !NavTrip.Crosses(sys, pos, t, o.Body))
            o.Handed = false; // (in the other's planet's reach now: should it leave for another, a trip after it again)
    }

    /// <summary>
    /// Host: in space, the push (ecliptic axes) that brings the ship alongside the other craft and holds it there, matching its
    /// motion. Close in the approach is planned on no more than <see cref="NavComeAccel"/>: it turns to brake, and an overshoot
    /// would run into it. Holding still beside it, the drive is turned parallel to the other's.
    /// </summary>
    private Vec3d NavMeetInSpace(SpaceSession s, StarSystem sys, NavOrder o, Construct c, Construct t, SpacePos pos, Vec3d vel, SpacePos tp, Vec3d tv, SpaceTime time, double aMax)
    {
        var r = pos - tp;
        var v = vel - tv;
        var away = c.SpaceAway != 0 || t.SpaceAway != 0;
        var rot = t.SpaceAway != 0 ? AwayRot : s.Bubble.PoseAt(time).Rot; // (the other's pose is in its own frame's axes)
        // Its side (the other's X), the one it comes from, chosen once so it doesn't swap sides as it passes.
        var side = rot.Rotate(Vec3d.From(Vector3.Transform(Vector3.UnitX, t.Rot)));
        if (o.MeetAbove)
        {
            side = rot.Rotate(Vec3d.From(Vector3.Transform(Vector3.UnitY, t.Rot)));
            o.MeetSide = 1;
        }
        else if (o.MeetSide == 0)
            o.MeetSide = (sbyte)(Vec3d.Dot(r, side) < 0 ? -1 : 1);
        var hold = c.Radius + t.Radius + o.Radius;
        var to = side * (o.MeetSide * hold);
        var gap = to - r;
        var way = gap.Length > 0.5 ? gap.Normalized() : Vec3d.Zero;
        // Round the other craft (coming from its far side the straight line runs through it) and round the world it is near.
        way = NavGuidance.Avoid(r, to, way, Vec3d.Zero, Math.Min(c.Radius + t.Radius + 0.5 * o.Radius, 0.92 * to.Length));
        sys.StateOf(o.Body, time, out var bp, out _);
        if (o.Body > 0)
            way = NavGuidance.Avoid(r, to, way, bp - tp, Math.Min(NavMinOrbit(sys, o.Body) - NavEdgeMargin * 0.5, 0.92 * (to - (bp - tp)).Length));
        // The other's own acceleration, measured from its velocity tick to tick (its gravity and any burn of its own), fed forward;
        // until there are two looks (or time ran fast between them) the gravity where it is.
        var dt = (time.Ns - o.MeetNs) / (double)SpaceTime.NsPerSecond;
        if (o.MeetNs != 0 && dt > 1e-3 && dt < 0.5)
        {
            var now = (tv - o.MeetVel) / dt;
            o.MeetAccel = o.MeetAccelOk ? o.MeetAccel + (now - o.MeetAccel) * 0.25 : now;
            o.MeetAccelOk = true;
        }
        else
            o.MeetAccelOk = false;
        o.MeetVel = tv;
        o.MeetNs = time.Ns;
        var theirs = o.MeetAccelOk ? o.MeetAccel : NavGuidance.Gravity(sys, tp, time);
        var gravity = NavGuidance.Gravity(sys, pos, time) - theirs;
        var dist = gap.Length;
        var plan = Math.Min(aMax, Math.Max(NavComeAccel, aMax * dist / NavMeetFar));
        var a = NavGuidance.GoTo(r, v, to, way, 0, gravity, plan, out o.Distance, out o.SpeedError);
        o.Phase = o.Distance < Math.Max(30, 0.25 * o.Radius) && o.SpeedError < 1.5 ? NavPhase.Hold
            : o.Handed ? NavPhase.Brake : NavPhase.Transfer;
        if (o.Phase == NavPhase.Hold && a.Length < 1e-4 && !away)
        {
            // Still beside it: its drive parallel to the other's (looked for once a second: one pass over its thrusters).
            if (o.MeetLook-- <= 0 || o.MeetAxis == Vector3.Zero)
            {
                o.MeetLook = World.TickRate;
                ConstructPhysics.NavDrive(t, out o.MeetAxis, out _, out _);
                if (o.MeetAxis == Vector3.Zero)
                    o.MeetAxis = Vector3.UnitZ;
            }
            c.NavAim = Vector3.Normalize(Vector3.Transform(o.MeetAxis, t.Rot));
        }
        return a;
    }

    /// <summary>Host: both in a sky, the autopilot flies the ship to the point beside the other craft, at the other's height.</summary>
    private void NavMeetSky(NavOrder o, Construct c, Construct t)
    {
        c.NavThrottle = -1;
        c.NavClimb = 0;
        var here = c.WorldCom;
        var there = t.WorldCom;
        var up = ConstructPhysics.HoldUp(there);
        var side = Vector3.Transform(Vector3.UnitX, t.Rot);
        side -= up * Vector3.Dot(side, up);
        if (side.LengthSquared() < 0.01f)
            side = Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX); // (on its side: any way across)
        side = Vector3.Normalize(side);
        if (o.MeetSide == 0)
            o.MeetSide = (sbyte)(Vector3.Dot(here - there, side) < 0 ? -1 : 1);
        var aim = there + side * (o.MeetSide * (float)(c.Radius + t.Radius + o.Radius));
        var off = aim - here;
        o.Distance = (off - up * Vector3.Dot(off, up)).Length();
        o.SpeedError = (c.Vel - t.Vel).Length();
        o.Phase = o.Distance < 15 && o.SpeedError < 2 ? NavPhase.Hold : NavPhase.Flying;
        // Written only when the point has moved a few metres, at most twice a second (each write rebuilds the ship and sends its structure).
        var look = ++o.MeetLook >= 30;
        if (look)
            o.MeetLook = 0;
        if (!o.Engaged || look && Vector3.DistanceSquared(aim, o.WaypointSent) > NavComeMove * NavComeMove)
        {
            o.Engaged = ConstructFlight.NavEngage(Constructs, c, FlightMode.Waypoint, aim, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(aim)));
            o.WaypointSent = aim;
        }
    }
}
