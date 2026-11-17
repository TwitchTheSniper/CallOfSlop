using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Setting a ship down from the command view (docs/space/COMMAND.md "Landing"): a Land order's spot is on the ground of
/// the bubble's world, a Come order's beside the player who gave it (worked out again as they move, until the ship starts
/// down). In space the nav computer brings it down into the sky over the spot (<see cref="NavPhase.Descend"/>); in the sky
/// the stabilizer's autopilot flies it over the spot at its own height and then comes down on it
/// (<see cref="Construct.NavLand"/>, ConstructFlight's height loop), and the order ends once it sits still on the ground.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// From space a landing aims this far under the sky's edge (m): just past the line where it is a physics body again,
    /// so it comes into the sky slowly (it brakes for this point, and the sky takes it 200 m before).
    /// </summary>
    public const double NavDescendUnder = 300;

    /// <summary>In space, within this of its point over the spot a landing is coming down (m), not on its way.</summary>
    public const double NavDescendNear = 50_000;

    /// <summary>
    /// In space a ship that comes to a player holds this far further off than it would land (m), and plans its approach on
    /// no more than <see cref="NavComeAccel"/> (m/s2): it turns to brake, and a ship that overshot would run them down.
    /// </summary>
    public const double NavComeStandoff = 40, NavComeAccel = 5;

    /// <summary>How far a Come order's spot may drift (m) before the ship's waypoint is moved after its player.</summary>
    private const float NavComeMove = 6;

    /// <summary>Host: a Land or Come order in a sky: over its spot at its own height, then down onto it.</summary>
    private void NavLandSky(NavOrder o, Construct c)
    {
        var here = c.WorldCom;
        var up = ConstructPhysics.HoldUp(here);
        if (o.Kind == NavKind.Come && o.Phase != NavPhase.Landing && Players.GetValueOrDefault(o.By) is { Alive: true } p && p.Sim.RideBody != c.Id)
            o.Point = NavBeside(c, p.Position, here); // (a player who walks off is followed; once it is coming down, or they have come aboard, the spot stays)
        var spot = o.Point.ToVector3();
        if (!o.Engaged)
        {
            o.Engaged = NavWaypoint(c, spot);
            o.WaypointSent = spot;
        }
        else if (Vector3.DistanceSquared(spot, o.WaypointSent) > NavComeMove * NavComeMove)
        {
            ConstructFlight.NavMoveWaypoint(Constructs, c, spot); // (a config write and a rebuild: only when the spot has moved a few metres)
            o.WaypointSent = spot;
        }
        var off = spot - here;
        off -= up * Vector3.Dot(off, up);
        o.Distance = off.Length();
        var vel = c.Vel;
        var climb = Vector3.Dot(vel, up);
        if (o.Phase != NavPhase.Landing)
        {
            var drift = (vel - up * climb).Length();
            if (o.Distance > NavLandNear || drift > 4)
            {
                o.Phase = NavPhase.Flying;
                c.NavLand = 0;
                return;
            }
            o.Phase = NavPhase.Landing;
            o.Still = -60; // (a second to start down before sitting still counts)
            Console.WriteLine($"[nav] construct {c.Id} is over its spot: coming down");
        }
        c.NavLand = NavLandSpeed;
        // Down: it sits still near the ground while it is asked down. Still for long anywhere else (a repulsor field or
        // its gas holding it up, or something the ray down doesn't see), the order gives up and leaves it hovering: let
        // go in the air, it would fall.
        o.Still = climb > -0.3f && climb < 0.5f ? o.Still + 1 : Math.Min(o.Still, 0);
        var gap = c.StabCount > 0 ? c.Stabs[0].Gap : 0;
        if (gap < 4 && o.Still >= NavDownTicks)
            NavEnd(o, c, "it has landed", down: true);
        else if (o.Still >= NavStuckTicks)
            NavEnd(o, c, "it can't come down any further here");
    }

    /// <summary>
    /// Is a player out in space: over the sky's edge (and, once out, until 150 m back under it, so a player at the line
    /// doesn't flip the order to and fro)? A Come order then holds beside them there instead of landing.
    /// </summary>
    private bool NavPlayerInSpace(Player p, bool was = false)
    {
        var edge = SpaceEdge;
        return edge > 0 && ConstructPhysics.HoldHeight(p.Position) > edge - (was ? 150f : 0f);
    }

    /// <summary>Why this ship can't set down on a spot (null: it can).</summary>
    private string? NavCantLand(Construct c)
    {
        ref var sp = ref c.Parts[c.Stabs[0].Part];
        return ConstructFlight.KeysOf(c, sp, sp.D) is FlightPattern.Plane or FlightPattern.Rocket ? "It can't set down on a spot: a plane or a rocket needs somewhere to land on its own"
            : c.SpaceShip != 0 && SpaceEdge <= 0 ? "There is no way down from here"
            : c.SpaceShip != 0 && LandOverLimit(c, out _) ? "It is too heavy to land on this world"
            : null;
    }

    /// <summary>
    /// Host, each tick before a Come order flies: does it hold beside its player in space (<see cref="NavOrder.ComeSpace"/>)
    /// or land beside them? A change resets the autopilot's mode. Returns why the order ends, or null: its player is gone
    /// or came aboard while it held beside them in space, or it can't land where they have gone down to.
    /// </summary>
    private string? NavComeMode(NavOrder o, Construct c)
    {
        if (o.Phase == NavPhase.Landing)
            return null; // (coming down: it finishes on the spot it has)
        if (Players.GetValueOrDefault(o.By) is not { Alive: true } p)
            return o.ComeSpace ? "its player is gone" : null; // (on the ground it lands on the last spot it had)
        if (o.ComeSpace && p.Sim.RideBody == c.Id)
            return "its player is aboard"; // (it would hold off its own deck)
        var space = NavPlayerInSpace(p, o.ComeSpace);
        if (space == o.ComeSpace)
            return null;
        if (!space && NavCantLand(c) is { } cant)
            return cant;
        o.ComeSpace = space;
        c.NavLand = 0;
        if (c.SpaceShip == 0)
            o.Engaged = false; // (in a sky: a climb out, or a waypoint to land at, set again next)
        Console.WriteLine($"[nav] construct {c.Id}'s player is {(space ? "out in space: it holds beside them" : "back in the sky: it lands beside them")}");
        return null;
    }

    /// <summary>
    /// Host: in space, beside a player who is out there (on a spacewalk, or on another ship): the push that brings the ship
    /// to its own size plus <see cref="NavComeBeside"/> from them and holds it there, matching their motion (both fall
    /// alike, so only the difference in gravity is cancelled). Ecliptic axes.
    /// </summary>
    private Vec3d NavBesideInSpace(SpaceSession s, StarSystem sys, NavOrder o, Construct c, Player p, SpacePos pos, Vec3d vel, SpaceTime t, double aMax)
    {
        var pose = s.Bubble.PoseAt(t);
        var at = Vec3d.From(p.Position);
        var moving = p.Sim.Velocity + (p.Sim.RideBody != 0 && Constructs.Get(p.Sim.RideBody) is { } ride ? ConstructRide.RiderSurfaceVel(ride, p.Sim, p.Position) : Vector3.Zero); // (a rider's own is in what they ride)
        var pp = pose.ToEcliptic(at);
        var r = pos - pp;
        var v = vel - pose.VelocityToEcliptic(at, Vec3d.From(moving));
        sys.StateOf(o.Body, t, out var bp, out _);
        var centre = bp - pp;
        var hold = c.Radius + NavComeBeside + NavComeStandoff;
        var to = (r.Length > 1 ? r.Normalized() : Vec3d.UnitX) * hold; // (on the side it comes from)
        if ((to - centre).Length < SpaceEdge - SpaceEdgeHysteresis + 50 && centre.Length > 1)
            to = -centre.Normalized() * hold; // (a player just under the edge: over them, or it would be back in the sky)
        var gap = to - r;
        var way = gap.Length > 0.5 ? gap.Normalized() : Vec3d.Zero;
        way = NavGuidance.Avoid(r, to, way, centre, Math.Min(NavMinOrbit(sys, o.Body) - NavEdgeMargin * 0.5, 0.92 * (to - centre).Length));
        var gravity = NavGuidance.Gravity(sys, pos, t) - NavGuidance.Gravity(sys, pp, t);
        var a = NavGuidance.GoTo(r, v, to, way, 0, gravity, Math.Min(aMax, NavComeAccel), out o.Distance, out o.SpeedError);
        o.Phase = o.Distance < 30 && o.SpeedError < 1.5 ? NavPhase.Hold : NavPhase.Transfer;
        return a;
    }

    /// <summary>Host: the autopilot set to fly to a point at its own height (lifting off first when it sits on the ground). False: it has no working stabilizer.</summary>
    private bool NavWaypoint(Construct c, Vector3 to)
    {
        // Off the ground first when it sits still on it, so it doesn't drag itself along (the height is a guess: it can't see the hills).
        var shown = ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(c.WorldCom));
        var resting = !c.HoverOn && c.Vel.LengthSquared() < 1f;
        return ConstructFlight.NavEngage(Constructs, c, FlightMode.Waypoint, to, resting ? shown + 80f : 0f);
    }

    /// <summary>Where a ship that comes to a player sets down: beside them, on the side it comes from, clear of them by its own size.</summary>
    private static Vec3d NavBeside(Construct c, Vector3 player, Vector3 from)
    {
        var up = ConstructPhysics.HoldUp(player);
        var side = (from == Vector3.Zero ? c.WorldCom : from) - player;
        side -= up * Vector3.Dot(side, up);
        if (side.LengthSquared() < 1)
            side = Vector3.Cross(up, MathF.Abs(up.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX); // (straight overhead: any side)
        return Vec3d.From(player + Vector3.Normalize(side) * (c.Radius + NavComeBeside));
    }

    /// <summary>Host: the point in the sky over a landing's spot, for the nav computer in space (the ecliptic's axes, the system's origin). It turns with the ground.</summary>
    private SpacePos NavSkyOver(SpaceSession s, NavOrder o, SpaceTime t)
    {
        var centre = s.Bubble.GameCentre;
        var out1 = o.Point - centre;
        var up = out1.Length > 1 ? out1.Normalized() : Vec3d.UnitY;
        double edge = SpaceEdge;
        var radius = s.System.Bodies[o.Body].Radius;
        var h = Math.Max(edge - NavDescendUnder, radius + 0.5 * (edge - radius)); // (a small world's sky is thin: still under the line)
        if (o.Kind == NavKind.Come && Players.GetValueOrDefault(o.By) is { Alive: true } p)
        {
            // From space too it heads for the player as they are now.
            var at = Vec3d.From(p.Position) - centre;
            if (at.Length > 1)
                up = at.Normalized();
        }
        return s.Bubble.PoseAt(t).ToEcliptic(centre + up * h);
    }
}
