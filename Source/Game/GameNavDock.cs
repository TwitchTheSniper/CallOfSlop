using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Docking from the command view (docs/space/COMMAND.md "Docking"; Karl, 2026-11-17: "select a ship, right click on another
/// with the option to dock. If there is no docking port available, it will show 'no port available'"). A Dock order names
/// another craft (<see cref="NavOrder.Target"/>) and one of its docking ports (<see cref="NavOrder.TargetPart"/>): a Docking
/// Clamp, Magnetic Lock or Hangar Bay (any part with a <c>"dock"</c>) that is working, holds nothing and can take the ship's
/// mass. The ship flies to a point <see cref="NavDockOver"/> off the port's box (over it, or in front of a hangar bay's
/// mouth), then closes on the box at <see cref="NavDockSink"/> m/s. In a sky the port takes it as it takes any craft (an
/// armed clamp's tractor or a bay's box: ConstructCarrier.cs, ConstructCargo.cs); in space, where a ship is a particle and
/// its physics sleeps, the host hands it to the port itself once it is in the port's reach and slow. The order ends when the
/// port holds it.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far off a port's box a docking ship lines up (m), and how fast it closes on it from there (m/s).</summary>
    public const float NavDockOver = 30, NavDockSink = 2;

    /// <summary>How slow against the port a ship must be (m/s) for the host to dock it in space, and how long it may wait at the port before the order gives up (ticks).</summary>
    public const float NavDockSlow = 3;
    public const int NavDockGiveUp = 60 * World.TickRate;

    /// <summary>
    /// Any peer: the docking port on <paramref name="target"/> that <paramref name="ship"/> would dock at: a working one that
    /// holds nothing and takes its mass, armed ones first, then the nearest. -1: no port available. With <paramref name="orders"/>, a port another
    /// ship's Dock order is flying to is passed over while another will do (2026-11-20: three Traders haulers, each picking the same snuggest
    /// Magpie berth at the Highport, met in its lane and knocked one another kilometres off).
    /// </summary>
    public static int NavDockFor(ConstructWorld world, Construct target, Construct ship, List<NavOrder>? orders = null)
    {
        var free = NavDockPick(world, target, ship, orders);
        return free >= 0 || orders == null ? free : NavDockPick(world, target, ship, null);
    }

    private static bool NavDockClaimed(List<NavOrder> orders, Construct target, int part, Construct ship)
    {
        foreach (var o in orders)
            if (o.Kind == NavKind.Dock && o.Target == target.Id && o.TargetPart == part && o.Construct != ship.Id)
                return true;
        return false;
    }

    private static int NavDockPick(ConstructWorld world, Construct target, Construct ship, List<NavOrder>? orders)
    {
        if (target == ship || target.Wreck || !target.HasLogic || target.PartCount == 0)
            return -1;
        var best = -1;
        var bestD = float.MaxValue;
        var bestArmed = false;
        var bestFit = false;
        var from = ship.WorldCom;
        for (var i = 0; i < target.PartCount; i++)
        {
            if (!NavDockFree(world, target, i, ship) || orders != null && NavDockClaimed(orders, target, i, ship))
                continue;
            var armed = ConstructCargo.IsFrozen(target.Parts[i]);
            var d = Vector3.DistanceSquared(NavDockCentre(target, i, out _, out _), from);
            // A station's berths are each made for a ship's size (Karl 2026-11-18: berth classes by ship size, not by ship): only one
            // the ship fits takes it, the snuggest of those (a Magpie goes to a Magpie's berth, not a frigate's); none fits: no berth.
            var fit = false;
            if (target.IsStation)
            {
                if (!BerthFits(target, i, ship, out var room))
                    continue;
                fit = true;
                d = room;
            }
            if (fit && !bestFit || fit == bestFit && (armed && !bestArmed || armed == bestArmed && d < bestD))
                (best, bestD, bestArmed, bestFit) = (i, d, armed, fit);
        }
        return best;
    }

    /// <summary>Any peer: is a station's lack of a berth for <paramref name="ship"/> only that it fits none of the free ones (it is too big for them)?</summary>
    public static bool NavDockNoFit(ConstructWorld world, Construct station, Construct ship)
    {
        for (var i = 0; i < station.PartCount; i++)
            if (NavDockFree(world, station, i, ship))
                return true;
        return false;
    }

    /// <summary>Does <paramref name="ship"/> fit berth <paramref name="part"/>'s box (its sizes, longest to shortest, each within the box's), and the box's room to spare (m³)?</summary>
    public static bool BerthFits(Construct station, int part, Construct ship, out float room)
    {
        ConstructCargo.Box(station, part, out _, out var half);
        var s = ship.LocalMax - ship.LocalMin;
        Span<float> a = [half.X * 2, half.Y * 2, half.Z * 2];
        Span<float> b = [s.X, s.Y, s.Z];
        a.Sort();
        b.Sort();
        room = a[0] * a[1] * a[2] - b[0] * b[1] * b[2];
        return b[0] <= a[0] * 1.05f && b[1] <= a[1] * 1.05f && b[2] <= a[2] * 1.05f;
    }

    /// <summary>Can <paramref name="ship"/> dock at part <paramref name="part"/> of <paramref name="target"/>: a working port, strong enough, holding nothing else?</summary>
    public static bool NavDockFree(ConstructWorld world, Construct target, int part, Construct ship)
    {
        if (part >= target.PartCount || target.Parts[part].Broken)
            return false;
        var d = target.DefOf(part);
        if (!d.IsDock || ship.Mass > d.DockMaxMass)
            return false;
        return !ConstructCargo.DockTaken(world, target, part, ship);
    }

    /// <summary>
    /// The middle of a port's box (World), the way a ship comes in to it (World, unit: up out of a clamp or a lock, out of a
    /// hangar bay's mouth) and how far the box reaches that way from its middle (m).
    /// </summary>
    public static Vector3 NavDockCentre(Construct target, int part, out Vector3 axis, out float along)
    {
        ConstructCargo.Box(target, part, out var centre, out var half);
        ref var p = ref target.Parts[part];
        target.WorldFrame(p.Seg, out var q, out var t);
        var local = p.D.DockEject > 0 ? Rot24.Apply(p.Rot, -Vector3.UnitZ) : Rot24.Apply(p.Rot, Vector3.UnitY); // (a bay is flown into by its mouth, where it ejects)
        if (target.IsStation && target.DockLanesFor == target.RebuildStamp && target.DockLanes is { } lanes && part < lanes.Length && lanes[part] >= 0)
            local = Game.LaneAxis(lanes[part]); // (a station's berth is flown into along its clear way in: Game.StationLanes)
        along = MathF.Abs(local.X) * half.X + MathF.Abs(local.Y) * half.Y + MathF.Abs(local.Z) * half.Z;
        axis = Vector3.Transform(local, q);
        return t + Vector3.Transform(centre, q);
    }

    /// <summary>Host: a player's Dock order checked. Null (and the port), or why it takes none.</summary>
    private string? NavDockCheck(Construct c, ushort target, out int port)
    {
        port = -1;
        if (Constructs.Get(target) is not { Wreck: false, PartCount: > 0 } t || t == c)
            return "That ship is gone";
        if (c.SpaceAway != 0 || t.SpaceAway != 0)
            return "They are too far apart: bring them to the same world first";
        if (c.SpaceShip != 0 && t.SpaceShip == 0)
            return "It is out in space and the other is in the sky: bring it down into the sky first";
        if (c.CargoCount > 0 && !t.IsStation)
            return "It carries a craft or cargo of its own: no port takes it so"; // (ConstructCargo.Catchable: no cargo in cargo; a station's berth moors it instead: GameStations.cs)
        if (c.MooredTo != 0)
            return "It is moored at a station: give it another order to cast off";
        StationLanes(t); // (a station's berths' ways in, once: GameStations.cs)
        port = NavDockFor(Constructs, t, c, NavOrders);
        if (port < 0 && t.IsStation)
            StationRefused(t); // (turned away: a station that keeps turning ships away grows a berth, GameStationLife.cs)
        return port >= 0 ? null : t.IsStation && NavDockNoFit(Constructs, t, c) ? $"No berth on {t.DisplayName()} fits it" : "No port available on " + t.DisplayName();
    }

    /// <summary>
    /// Host, every tick of a Dock order: the port is checked again once a second (another one is taken if it was), the ship
    /// climbs out of its sky first when the other ship is in space, and is flown to the port. False: the order ended here.
    /// </summary>
    private bool NavDockTick(SpaceSession s, NavOrder o, Construct c)
    {
        if (Constructs.Get(o.Target) is not { Wreck: false } t || t.SpaceAway != 0)
        {
            NavEnd(o, c, "the ship it was to dock with is gone");
            return false;
        }
        if (c.SpaceShip != 0 && t.SpaceShip == 0)
        {
            NavEnd(o, c, "the ship it was to dock with went down into the sky");
            return false;
        }
        StationLanes(t); // (a station rebuilt: its lanes again)
        if (o.TargetPart >= t.PartCount || !t.DefOf(o.TargetPart).IsDock)
            o.DockLook = World.TickRate; // (the other ship was edited: its port is looked for again now, before anything reads it)
        if (++o.DockLook >= World.TickRate)
        {
            o.DockLook = 0;
            if (!NavDockFree(Constructs, t, o.TargetPart, c))
            {
                var port = NavDockFor(Constructs, t, c, NavOrders);
                if (port < 0)
                {
                    NavEnd(o, c, "no port available on " + t.DisplayName() + " any more");
                    return false;
                }
                o.TargetPart = (ushort)port;
                o.Phase = NavPhase.None; // (lines up afresh; everyone hears the new port)
            }
        }
        if (c.SpaceShip == 0 && t.SpaceShip != 0)
        {
            NavInSky(s, o, c); // (a climb out to space first)
            return true;
        }
        if (c.SpaceShip == 0)
            NavDockSky(o, c, t);
        else
            NavInSpace(s, o, c);
        return o.Kind != NavKind.None;
    }

    /// <summary>Where along the way in the ship aims now (m out from the box's middle), and the phase for it, as the ship comes in.</summary>
    private static float NavDockStep(NavOrder o, bool armed, float along, float near, float slip)
    {
        var stage = along + NavDockOver;
        if (o.Phase is not (NavPhase.Docking or NavPhase.DockWait))
        {
            o.DockAlt = stage;
            if (near < 5 && slip < 2)
            {
                o.Phase = NavPhase.Docking;
                o.Still = 0;
            }
            else
                o.Phase = NavPhase.Flying;
            return stage;
        }
        if (!armed)
        {
            o.Phase = NavPhase.DockWait; // (it waits where it is: the port isn't armed, or has no power)
            o.Still++;
            return o.DockAlt;
        }
        o.Phase = NavPhase.Docking;
        if (near < 4)
            o.DockAlt = MathF.Max(0, o.DockAlt - NavDockSink * World.TickDt); // (it follows the point in, no faster than it keeps up)
        if (o.DockAlt <= 0)
            o.Still++;
        return o.DockAlt;
    }

    /// <summary>Host: in a sky the stabilizer's autopilot flies it over the point in front of the port and then in, and the port takes it.</summary>
    private void NavDockSky(NavOrder o, Construct c, Construct t)
    {
        c.NavThrottle = -1;
        c.NavClimb = 0;
        c.NavLand = 0;
        int part = o.TargetPart;
        var centre = NavDockCentre(t, part, out var axis, out var along);
        var armed = ConstructCargo.IsFrozen(t.Parts[part]) && ConstructPower.On(t, part);
        if (armed && o.Phase == NavPhase.Docking && t.Parts[part].D.DockPull > 0 && (ushort)t.Parts[part].Mem == c.Id) // (only once lined up over it: drawn in from the side or below, a craft is dragged against the other ship's hull and tips)
        {
            // The clamp's tractor has it: the autopilot only keeps it level and on its heading (it would hold the ship off the
            // middle the tractor draws it to; let go altogether, the stabilizer stops levelling and the craft can tip onto its back).
            if (!o.DockTractor)
            {
                ConstructFlight.NavEngage(Constructs, c, FlightMode.Heading, default, 0);
                o.DockTractor = true;
                o.Engaged = false;
            }
            o.Phase = NavPhase.Docking;
            o.Distance = Vector3.Distance(centre, c.WorldCom);
            if (++o.Still >= NavDockGiveUp)
                NavEnd(o, c, "the port didn't take it (is the ship too big for its box?)");
            return;
        }
        if (o.DockTractor)
        {
            o.DockTractor = false; // (the tractor let it go: the autopilot flies it in again)
            o.Engaged = false;
        }
        var here = c.WorldCom;
        var aim = centre + axis * (o.Phase is NavPhase.Docking or NavPhase.DockWait ? o.DockAlt : along + NavDockOver);
        var up = ConstructPhysics.HoldUp(here);
        var off = aim - here;
        var flat = off - up * Vector3.Dot(off, up);
        var slip = (c.Vel - t.PointVel(here)).Length();
        aim = centre + axis * NavDockStep(o, armed, along, flat.Length(), slip); // (lined up over the ground: a craft on repulsors holds its own height, and the port's pull reaches it)
        o.Distance = Vector3.Distance(aim, here);
        o.SpeedError = slip;
        if (o.Phase == NavPhase.Flying)
            c.DockAway = Math.Max(c.DockAway, Constructs.CarrierClock + 2); // (until it is lined up no port's tractor takes it: drawn in from the side or below, a craft is dragged against the other ship's hull and tips over)
        // The autopilot's point and height, written only when they have moved a few metres, and at most twice a second (each
        // write rebuilds the ship and sends its structure): lining up it follows a moving ship by 6 m, closing in by 2 m.
        var move = o.Phase == NavPhase.Docking ? 2f : NavComeMove;
        if (!o.Engaged || o.DockLook % 30 == 0 && Vector3.DistanceSquared(aim, o.WaypointSent) > move * move)
        {
            o.Engaged = ConstructFlight.NavEngage(Constructs, c, FlightMode.Waypoint, aim, ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(aim)));
            o.WaypointSent = aim;
        }
        if (o.Still >= NavDockGiveUp)
            NavEnd(o, c, armed ? "the port didn't take it (is the ship too big for its box?)" : "the port is not armed, or has no power");
    }

    /// <summary>
    /// Host: in space, the push that brings the ship to the point in front of the port and then in, matching the other ship's
    /// motion (Ecliptic axes); within the port's reach and slow, it is docked (<see cref="NavDockCatch"/>) and the order ends.
    /// </summary>
    private Vec3d NavDockInSpace(SpaceSession s, SpaceFlight f, int i, StarSystem sys, NavOrder o, Construct c, Construct t, SpacePos pos, Vec3d vel, SpaceTime time, double aMax)
    {
        NavSpeedCap = 1; // (close work with another ship: real time)
        int part = o.TargetPart;
        var centre = NavDockCentre(t, part, out var axis, out var along);
        var armed = ConstructCargo.IsFrozen(t.Parts[part]) && ConstructPower.On(t, part);
        var d = t.DefOf(part);
        var com = c.WorldCom;
        var slip = (c.Vel - t.PointVel(com)).Length();
        if (armed && slip < MathF.Max(NavDockSlow, d.DockSettle) && (t.IsStation ? StationMoorable(t, c, d) : ConstructCargo.Catchable(Constructs, t, c, d)) && NavDockFree(Constructs, t, part, c)
            && (d.DockPull > 0 ? Vector3.Distance(com, centre) < MathF.Min(along + NavDockOver * 0.5f, along + d.DockPull) : ConstructCargo.MostlyInside(t, part, c)))
        {
            if (t.IsStation)
            {
                StationMoor(f, i, c, t, part); // (a station's berth moors it, its own craft aboard: GameStations.cs)
                NavEnd(o, c, $"it has docked with {StationName(t)}", docked: true);
            }
            else
                NavDockCatch(f, i, o, c, t, part, centre);
            return Vec3d.Zero;
        }
        var pose = s.Bubble.PoseAt(time);
        var aimAt = centre + axis * (o.Phase is NavPhase.Docking or NavPhase.DockWait ? o.DockAlt : along + NavDockOver);
        var r = pos - pose.ToEcliptic(Vec3d.From(aimAt));
        aimAt = centre + axis * NavDockStep(o, armed, along, (float)r.Length, slip);
        // A station's berth has a lane (Karl, 2026-11-18: "a landing path for each station, such that the way to get into each dock is as
        // you'd expect"): the ship flies round the station to the lane's mouth, out past the station along the berth's clear way in, then
        // comes down the lane on its small jets, turned as it will lie in the berth, instead of straight at the port across the station.
        var lane = false;
        if (t.IsStation && o.Phase is not (NavPhase.Docking or NavPhase.DockWait))
        {
            if (StationLaneAim(t, centre, axis, along, com, out var mouth))
                lane = true; // (in the lane: down it to the point over the berth)
            else
            {
                aimAt = mouth; // (to the lane's mouth, or round the station towards it)
                o.Phase = NavPhase.Flying;
            }
        }
        if (o.Phase is NavPhase.Docking or NavPhase.DockWait || lane || r.Length < NavDockRcsNear && slip < NavDockRcsSlip)
        {
            NavDockRcs(f, i, pose, o, c, t, part, aimAt, o.Phase is NavPhase.Docking or NavPhase.DockWait ? NavDockSink + 1 : lane ? NavDockLaneSpeed : NavDockRcsSlip);
            if (o.Still >= NavDockGiveUp)
                NavEnd(o, c, armed ? "the port didn't take it" : "the port is not armed, or has no power");
            return Vec3d.Zero;
        }
        if (c.ThrustPuppet)
            NavBoardJetsOut(c); // (back out of close work: its main drive flies it again)
        var at = Vec3d.From(aimAt);
        var pp = pose.ToEcliptic(at);
        r = pos - pp;
        var v = vel - pose.VelocityToEcliptic(at, Vec3d.From(t.PointVel(aimAt)));
        var gravity = NavGuidance.Gravity(sys, pos, time) - NavGuidance.Gravity(sys, pp, time);
        var way = r.Length > 0.5 ? -r.Normalized() : Vec3d.Zero;
        var a = NavGuidance.GoTo(r, v, Vec3d.Zero, way, 0, gravity, Math.Min(aMax, NavComeAccel), out o.Distance, out _);
        o.SpeedError = slip;
        if (o.Still >= NavDockGiveUp)
            NavEnd(o, c, armed ? "the port didn't take it" : "the port is not armed, or has no power");
        return a;
    }

    /// <summary>Close to the port in space a ship comes in on its small jets, not by turning its main drive to each correction
    /// (it twirled at its full turn rate over the port and swept its hull through the other ship): within this far of the point
    /// it aims at (m) and this slow against it (m/s), and at most this hard (m/s²).</summary>
    public const float NavDockRcsNear = 20, NavDockRcsSlip = 4, NavDockRcsAccel = 1.5f;

    /// <summary>How fast a ship comes down a station's lane (m/s), and how far off the lane's line it may be and still count as in it (m).</summary>
    public const float NavDockLaneSpeed = 8, NavDockLaneWidth = 25;

    /// <summary>
    /// Host, close work in space: the ship's speed against the port is steered straight to close on <paramref name="aim"/> (its
    /// flight's velocity changed, as its RCS would), it turns upright with the port and stops turning, and its jets are lit for it
    /// (<see cref="Construct.ThrustPuppet"/>: the thrust step pushes nothing meanwhile).
    /// </summary>
    private static void NavDockRcs(SpaceFlight f, int i, in FramePose pose, NavOrder o, Construct c, Construct t, int part, Vector3 aim, float most)
    {
        var dt = World.TickDt;
        var here = c.WorldCom;
        var to = aim - here;
        var d = to.Length();
        var speed = MathF.Min(most, MathF.Sqrt(NavDockRcsAccel * d)); // (half its push to brake with)
        var want = t.PointVel(here) + (d > 1e-3f ? to / d * speed : Vector3.Zero);
        var dv = want - c.Vel;
        var step = NavDockRcsAccel * dt;
        if (dv.LengthSquared() > step * step)
            dv = Vector3.Normalize(dv) * step;
        f.Ships.RelativeOf(i, out var primary, out var rel, out var vel);
        f.Ships.SetState(i, primary, rel, vel + pose.Rot.Rotate(Vec3d.From(dv)));
        c.NavThrottle = 0;
        c.NavAim = Vector3.Zero;
        // Upright with the port, turning with the other ship and no faster than 0.5 rad/s; its heading stays its own.
        t.WorldFrame(t.Parts[part].Seg, out var q, out _);
        var upB = Vector3.Transform(Vector3.UnitY, c.Rot);
        var upC = Vector3.Transform(Vector3.UnitY, q);
        var cross = Vector3.Cross(upB, upC);
        var sin = cross.Length();
        var angle = MathF.Atan2(sin, Vector3.Dot(upB, upC));
        var spin = sin > 1e-4f ? cross / sin * MathF.Min(angle * 1.5f, 0.5f) : Vector3.Zero;
        if (t.IsStation)
        {
            // Turned as it will lie in the berth (its up the berth's, its long way along the box's), so it slides in clear of the arms.
            var err = Quaternion.Normalize(StationMoorRot(t, part, c) * Quaternion.Conjugate(c.Rot));
            if (err.W < 0)
                err = new Quaternion(-err.X, -err.Y, -err.Z, -err.W);
            var vs = new Vector3(err.X, err.Y, err.Z);
            var vl = vs.Length();
            var turn = 2 * MathF.Atan2(vl, err.W);
            spin = vl > 1e-5f ? vs / vl * MathF.Min(turn * 1.5f, 0.5f) : Vector3.Zero;
        }
        var dw = t.AngVel + spin - c.AngVel;
        var turnStep = 2f * dt;
        if (dw.LengthSquared() > turnStep * turnStep)
            dw = Vector3.Normalize(dw) * turnStep;
        c.AngVel += dw;
        o.Distance = d;
        NavBoardJets(c, dv / dt * 3, dw / dt); // (its flames, as a Board order's pod: GameNavBoard.cs)
    }

    /// <summary>
    /// Host: a ship in space docked: out of the flight (a body again, as one coming back into a sky is), set in the middle of the
    /// port's box upright with it as a clamp's tractor leaves a craft, and held there by the port from now on.
    /// </summary>
    private void NavDockCatch(SpaceFlight f, int i, NavOrder o, Construct c, Construct t, int part, Vector3 centre)
    {
        f.Ships.Remove(i);
        c.SpaceShip = 0;
        t.WorldFrame(t.Parts[part].Seg, out var q, out _);
        var upB = Vector3.Transform(Vector3.UnitY, c.Rot);
        var upC = Vector3.Transform(Vector3.UnitY, q);
        var cross = Vector3.Cross(upB, upC);
        var sin = cross.Length();
        var cos = Vector3.Dot(upB, upC);
        if (sin > 1e-4f)
            c.Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(cross / sin, MathF.Atan2(sin, cos)) * c.Rot);
        else if (cos < 0)
            c.Rot = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitX, c.Rot), MathF.PI) * c.Rot);
        if (t.DefOf(part).DockPull > 0)
            c.Pos = centre - Vector3.Transform(c.Com, c.Rot); // (a pull dock draws a craft to its middle: ConstructCarrier.Tractor)
        c.Vel = t.PointVel(c.WorldCom);
        c.AngVel = t.AngVel;
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        c.VelBefore = c.VelThen = c.Vel;
        c.Accel = Vector3.Zero;
        c.HoverOn = false;
        c.Wake();
        ConstructCargo.Catch(Constructs, t, part, c);
        NavEnd(o, c, $"it has docked with {t.DisplayName()}", docked: true);
    }
}
