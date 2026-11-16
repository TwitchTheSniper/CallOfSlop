using System.Numerics;
using Brutal.RakNetApi;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>What a ship was told to do from the command view (a wire byte: add at the end).</summary>
public enum NavKind : byte
{
    None, // no order (on the wire: the order is cancelled)
    GoTo, // fly to a point in space (metres from a body's centre, the ecliptic's axes) and stay there
    Orbit, // a circle round a body, a radius from its centre
    Ground, // fly to a point over the ground of the world it is on (World coordinates), on its autopilot
}

/// <summary>How far an order has got (a wire byte: add at the end).</summary>
public enum NavPhase : byte
{
    None,
    Climb, // in the sky still, climbing out on its autopilot
    Transfer, // on its way
    Insert, // near the circle, matching its speed
    Orbiting, // on the circle: the drive is off
    Hold, // at the point, holding there against gravity
    Flying, // over the ground, on its autopilot towards the waypoint
    Arrived, // over the waypoint, hovering
    NoDrive, // nothing it has can push it now (no fuel, no power, wrecked)
    Depart, // between planets: the burn out, onto the conic round the star (NavTrip.cs)
    Coast, // between planets: drive off, coasting round the star (time can be sped up)
    Correct, // between planets: a small burn back onto the planned conic
    Brake, // between planets: flipped over, braking into the target's neighbourhood
    Short, // between planets: no trip in reach fits the dV a trip may spend (NavOrder.DvNeed says what it would take)
}

/// <summary>One ship's order (the host's; clients hear a copy for the command view).</summary>
public sealed class NavOrder
{
    public ushort Construct;
    public NavKind Kind;
    public byte Body;
    public Vec3d Point;
    public double Radius;
    public NavPhase Phase;

    /// <summary>The player who gave it.</summary>
    public byte By;

    /// <summary>Host: what clients were last told, and whether the autopilot was set for the phase it is in.</summary>
    internal NavPhase PhaseSent;
    internal long ArriveSent;
    internal bool Engaged;

    /// <summary>Host: metres still to go and the speed still to change (m/s), for the log and the checks.</summary>
    public double Distance, SpeedError;

    /// <summary>
    /// A trip between planets (NavTrip.cs): when it arrives (space time, ns; 0: no trip planned) and the dV it spends
    /// (m/s; while <see cref="NavPhase.Short"/>, the least any trip would take). Sent to clients for the ship card.
    /// </summary>
    public long ArriveNs;
    public float DvNeed;

    /// <summary>Host: a trip between planets is being flown (planned), or it is over and the order's own law has the ship now.</summary>
    internal bool Cruise, Handed;
}

/// <summary>
/// Orders from the command view (docs/space/COMMAND.md): a player picks a ship with a working flight computer (a Flight
/// Stabilizer) and tells it where to go, and the host flies it there with nobody at the controls. In space the nav computer
/// steers the ship's main drive (<see cref="NavGuidance"/>: the ship turns to where the push is wanted, burns its own
/// fuel or power through <see cref="ConstructPhysics.SpaceThrust"/>, and is a test particle as ever); inside a world's sky the
/// stabilizer's own autopilot flies it (a waypoint over the ground, or a climb to the sky's edge on the way to space).
/// A driver who touches the controls takes the ship back. Orders aren't saved, and a move to another world drops them.
/// <para>
/// The wire (protocol 324): <c>CActKind.NavOrder</c> from a client, and <see cref="Msg.Nav"/> from the host to everyone
/// when an order starts, changes phase or ends, and to each joiner.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>The orders (host: the truth; a client: what it heard). A handful at most, so a list.</summary>
    public readonly List<NavOrder> NavOrders = new(MaxNavOrders + 1); // (its full size: a network-fed list never grows in a steady frame)

    /// <summary>The most ships under orders at once.</summary>
    public const int MaxNavOrders = 64;

    /// <summary>How fast an order turns a ship in space (rad/s), and how well its drive must point before it burns (the cosines: nothing, everything).</summary>
    public const float NavTurnRate = 0.6f, NavAlignNone = 0.8f, NavAlignFull = 0.97f;

    /// <summary>How far past a sky's edge the lowest orbit sits (m), and how far over the edge a climb aims.</summary>
    public const double NavEdgeMargin = 400, NavClimbOver = 600;

    /// <summary>How fast an order climbs a ship out of a sky (m/s) when its stabilizer's own climb limit is lower: nobody is aboard to mind, and at a ship's 10 m/s Glacier's sky took four minutes.</summary>
    public const float NavClimbSpeed = 60;

    private const Buttons NavManualKeys = Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right;

    /// <summary>The order a construct flies under (null: none).</summary>
    public NavOrder? NavOf(ushort construct)
    {
        foreach (var o in NavOrders)
            if (o.Construct == construct)
                return o;
        return null;
    }

    /// <summary>Does this construct take orders (a working Flight Stabilizer, and it isn't a wreck or held)? Null, or why not.</summary>
    public static string? NavProblem(Construct c) =>
        c.Wreck ? "it is a wreck"
        : c.StabCount == 0 ? "it has no flight computer (a Flight Stabilizer)"
        : c.Stabs[0].Part >= c.PartCount || !c.Parts[c.Stabs[0].Part].Working ? "its flight computer is wrecked"
        : c.Anchored || c.Frozen || c.Pinned || c.CarriedBy != 0 ? "it is held down"
        : null;

    /// <summary>
    /// The lowest orbit round a body (m from its centre): over the sky of a world that has a map (inside it a ship is a
    /// physics body again, or lands), else a little clear of the ground. Worked out once a system.
    /// </summary>
    public static double NavMinOrbit(StarSystem sys, int body)
    {
        if (!NavLeast.TryGetValue(sys, out var least))
            NavLeast[sys] = least = NavMinOrbits(sys);
        return least[body];
    }

    private static readonly Dictionary<StarSystem, double[]> NavLeast = new(Galaxy.MaxSystems);

    private static double[] NavMinOrbits(StarSystem sys)
    {
        var least = new double[sys.Count];
        for (var body = 0; body < sys.Count; body++)
        {
            var b = sys.Bodies[body];
            least[body] = b.Map is { } name && Maps.Find(name) is { Planetoid: { } pl } m
                ? Math.Max(Math.Max(m.HalfX, m.HalfZ), pl.Radius + 500) + NavEdgeMargin
                : body == 0 ? b.Radius * 2 : b.Radius * 1.1 + 200;
        }
        return least;
    }

    /// <summary>The system a construct is in and, on the host, the flight that steps its ship (null: it isn't a ship in space).</summary>
    private SpaceFlight? NavFlightOf(SpaceSession s, Construct c) =>
        c.SpaceShip == 0 ? null : c.SpaceAway != 0 ? s.FlightOf(c.SpaceAway - 1) : s.Flight;

    /// <summary>
    /// Any peer: where a construct is in its star system now, and how fast (the ecliptic's axes). In the World it is its
    /// pose through the bubble's frame; parked far out, its ship's state (a client: as last heard). False when unknown, or
    /// it is in another system than the bubble's.
    /// </summary>
    public bool NavState(Construct c, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        pos = default;
        vel = default;
        if (Space is not { } s)
            return false;
        if (c.SpaceAway == 0)
        {
            var pose = s.Bubble.PoseAt(t);
            var com = Vec3d.From(c.WorldCom);
            pos = pose.ToEcliptic(com);
            vel = pose.VelocityToEcliptic(com, Vec3d.From(c.Vel));
            return true;
        }
        if (c.SpaceAway - 1 != s.GalaxyIndex)
            return false;
        if (s.Flight is { } f)
        {
            var i = f.Ships.IndexOfId(c.SpaceShip);
            if (i < 0)
                return false;
            pos = f.Ships.PositionOf(i);
            vel = f.Ships.VelocityOf(i);
            return true;
        }
        return s.SampleShip(c.SpaceShip, t, out pos, out vel);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Giving an order
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Client (or the host itself): an order for a ship, or <see cref="NavKind.None"/> to cancel its order.</summary>
    public void NavSend(ushort construct, NavKind kind, int body, Vec3d point, double radius)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.NavOrder).U16(construct).U8((byte)kind).U8((byte)body).F64(point.X).F64(point.Y).F64(point.Z).F64(radius);
        if (IsServer)
        {
            if (Local is { } me)
            {
                var r = new PacketReader(w.Span[1..]);
                if (SandboxActGuarded(me, ref r) is { } why)
                    SandboxSay(why);
            }
            return;
        }
        Net.SendToServer(w.Span, PacketReliability.ReliableOrdered);
    }

    /// <summary>Host: a player's order (after the act's kind byte). Returns what to tell them, or null.</summary>
    private string? NavOrderAct(Player p, ref PacketReader r)
    {
        if (!r.Has(2 + 1 + 1 + 8 * 4))
            return null;
        var id = r.U16();
        var kind = (NavKind)r.U8();
        int body = r.U8();
        var point = new Vec3d(r.F64(), r.F64(), r.F64());
        var radius = r.F64();
        if (Space is not { } s || Constructs.Get(id) is not { } c)
            return "That ship is gone";
        if (kind == NavKind.None)
        {
            if (NavOf(id) is { } had)
                NavEnd(had, c, "cancelled");
            return null;
        }
        if (kind > NavKind.Ground || !double.IsFinite(point.X + point.Y + point.Z + radius))
            return null;
        if (NavProblem(c) is { } why)
            return "It takes no orders: " + why;
        var sys = c.SpaceAway != 0 ? Galaxy.System(c.SpaceAway - 1) : s.System;
        if (body >= sys.Count)
            return null;
        if (kind == NavKind.Ground)
        {
            if (c.SpaceShip != 0 || body != s.Bubble.Body)
                return "It is in space: fly it down into the sky first, or give it an orbit";
        }
        else
        {
            var least = NavMinOrbit(sys, body);
            if (kind == NavKind.Orbit)
                radius = Math.Clamp(radius, least, Math.Max(least, 0.8 * SpaceShips.SphereOfInfluence(sys, Math.Max(body, 1))));
            else if (point.Length < least)
                point = point.Length > 1 ? point * (least / point.Length) : new Vec3d(least, 0, 0); // (inside the world: its edge, on the same side)
            if (c.SpaceShip == 0 && SpaceEdge <= 0)
                return "There is no way to space from here";
        }
        var o = NavOf(id);
        if (o == null)
        {
            if (NavOrders.Count >= MaxNavOrders)
                return "Too many ships are under orders";
            NavOrders.Add(o = new NavOrder { Construct = id });
        }
        o.Kind = kind;
        o.Body = (byte)body;
        o.Point = point;
        o.Radius = radius;
        o.By = p.Id;
        o.Phase = NavPhase.None;
        o.PhaseSent = NavPhase.None;
        o.Engaged = false;
        o.Cruise = o.Handed = false;
        o.ArriveNs = 0;
        o.DvNeed = 0;
        Console.WriteLine($"[nav] {p.Name} ordered construct {id}: {NavText(sys, o)}");
        return null;
    }

    /// <summary>An order in words (ASCII), for the log and the view.</summary>
    public static string NavText(StarSystem sys, NavOrder o)
    {
        var name = o.Body < sys.Count ? sys.Bodies[o.Body].Name : "?";
        return o.Kind switch
        {
            NavKind.Orbit => FormattableString.Invariant($"orbit {name} at {(o.Radius - sys.Bodies[Math.Min(o.Body, sys.Count - 1)].Radius) / 1000:0.0} km"),
            NavKind.GoTo => "go to a point " + NavFar(Math.Max(0, o.Point.Length - sys.Bodies[Math.Min(o.Body, sys.Count - 1)].Radius)) + " from " + name, // (from its surface, as heights are told)
            NavKind.Ground => "go to a waypoint on " + name,
            _ => "none",
        };
    }

    private static string NavFar(double m) => m >= 1.496e9
        ? FormattableString.Invariant($"{m / 1.496e11:0.00} AU")
        : m >= 1e7 ? FormattableString.Invariant($"{m / 1e6:0.0} thousand km") : FormattableString.Invariant($"{m / 1000:0.0} km");

    /// <summary>Host: an order is over (done with, cancelled, or its ship is gone): the drive is let go and everyone hears.</summary>
    private void NavEnd(NavOrder o, Construct? c, string why)
    {
        if (c != null)
        {
            c.NavThrottle = -1;
            c.NavClimb = 0;
            c.NavAim = Vector3.Zero;
            if (c.SpaceShip != 0)
                c.AngVel = Vector3.Zero; // (the order's turn isn't a spin to hand to a ship that turns by its jets)
            if (c.SpaceShip == 0 && o.Engaged)
                ConstructFlight.NavEngage(Constructs, c, FlightMode.Hover, default, 0); // (it stays where it is, hovering, until someone takes it)
            Console.WriteLine($"[nav] construct {c.Id}'s order ended: {why}");
        }
        NavOrders.Remove(o);
        o.Kind = NavKind.None;
        WriteNav(_w, o);
        Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Flying the orders (host, every tick, before the ships step)
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Host, every tick: each order steers its ship, and a change of phase goes out. Allocation-free.</summary>
    private void ServerTickNav(SpaceSession s)
    {
        NavSpeedCap = int.MaxValue;
        if (NavOrders.Count == 0)
            return;
        for (var k = NavOrders.Count - 1; k >= 0; k--)
        {
            var o = NavOrders[k];
            var c = Constructs.Get(o.Construct);
            if (c == null || NavProblem(c) != null || !SandboxOn)
            {
                NavEnd(o, c, "the ship can't fly it any more");
                continue;
            }
            if (c.Driver != 255 && ((c.DriveKeys & NavManualKeys) != 0 || c.LiftInput != 0) && o.Phase != NavPhase.None)
            {
                NavEnd(o, c, "its driver took the controls");
                continue;
            }
            if (c.Dirty)
                c.Rebuild();
            if (c.SpaceShip == 0)
                NavInSky(s, o, c);
            else
                NavInSpace(s, o, c);
            if (o.Phase != o.PhaseSent || o.ArriveNs != o.ArriveSent)
            {
                o.PhaseSent = o.Phase;
                o.ArriveSent = o.ArriveNs;
                WriteNav(_w, o);
                Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
            }
        }
    }

    /// <summary>Inside a world's sky the stabilizer's autopilot flies it: to the waypoint, or straight up to the edge on the way to space.</summary>
    private void NavInSky(SpaceSession s, NavOrder o, Construct c)
    {
        c.NavThrottle = -1;
        c.NavClimb = o.Kind == NavKind.Ground ? 0 : NavClimbSpeed;
        var here = c.WorldCom;
        if (o.Kind == NavKind.Ground)
        {
            var to = o.Point.ToVector3();
            if (!o.Engaged)
            {
                // Off the ground first when it sits still on it, so it doesn't drag itself along (the height is a guess: it can't see the hills).
                var shown = ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(here));
                var resting = !c.HoverOn && c.Vel.LengthSquared() < 1f;
                o.Engaged = ConstructFlight.NavEngage(Constructs, c, FlightMode.Waypoint, to, resting ? shown + 80f : 0f);
            }
            var up = ConstructPhysics.HoldUp(here);
            var off = to - here;
            off -= up * Vector3.Dot(off, up);
            o.Distance = off.Length();
            o.Phase = o.Distance < 25 ? NavPhase.Arrived : NavPhase.Flying;
            return;
        }
        var edge = SpaceEdge;
        if (edge <= 0)
        {
            o.Phase = NavPhase.NoDrive;
            return;
        }
        if (!o.Engaged || o.Phase != NavPhase.Climb)
        {
            var top = ConstructPhysics.HoldShown((float)(edge + NavClimbOver));
            o.Engaged = ConstructFlight.NavEngage(Constructs, c, FlightMode.Hover, default, top);
        }
        o.Phase = NavPhase.Climb;
        o.Distance = Math.Max(0, edge - ConstructPhysics.HoldHeight(here));
    }

    /// <summary>In space the nav computer steers its main drive (<see cref="NavGuidance"/>).</summary>
    private void NavInSpace(SpaceSession s, NavOrder o, Construct c)
    {
        c.NavClimb = 0;
        if (o.Engaged)
        {
            // Out of the sky: the autopilot that climbed it is let go, or it would hold that height the next time it came down.
            o.Engaged = false;
            ConstructFlight.NavRelease(Constructs, c);
        }
        if (o.Kind == NavKind.Ground || NavFlightOf(s, c) is not { } f || f.Ships.IndexOfId(c.SpaceShip) is var i && i < 0 || o.Body >= f.System.Count)
        {
            c.NavThrottle = -1;
            o.Phase = NavPhase.NoDrive;
            return;
        }
        var sys = f.System;
        var t = f.Ships.Time;
        var pos = f.Ships.PositionOf(i);
        sys.StateOf(o.Body, t, out var bp, out var bv);
        var r = pos - bp;
        var v = f.Ships.VelocityOf(i) - bv;
        var force = ConstructPhysics.NavDrive(c, out var axis, out var lift, out _);
        var aMax = c.Mass > 0 ? force / c.Mass : 0;
        if (!(aMax > 0.01))
        {
            c.NavThrottle = 0;
            o.Phase = NavPhase.NoDrive;
            return;
        }
        c.NavAxis = axis;
        c.NavLift = lift;
        Vec3d a;
        var mu = sys.Bodies[o.Body].Mu;
        if (!o.Handed && (o.Cruise || NavTrip.Crosses(sys, pos, t, o.Body)) && NavCruise(s, f, o, c, pos, t, aMax, out a))
        {
            // Between planets: the trip's own burns (GameNavTrip.cs).
        }
        else if (o.Kind == NavKind.Orbit && (o.Body == 0 || r.Length < 0.9 * SpaceShips.SphereOfInfluence(sys, o.Body)))
        {
            var was = o.Phase == NavPhase.Orbiting;
            a = NavGuidance.Orbit(r, v, mu, o.Radius, aMax, out var settled, out o.SpeedError);
            o.Distance = Math.Abs(o.Radius - r.Length);
            // On the circle the drive rests, and wakes again only when the ship has drifted well off it (a moon's tug).
            if (was && o.Distance < 0.05 * o.Radius && o.SpeedError < 5)
                a = Vec3d.Zero;
            else
                o.Phase = settled ? NavPhase.Orbiting : NavPhase.Insert;
        }
        else
        {
            var to = o.Point;
            var arrive = 0.0;
            if (o.Kind == NavKind.Orbit)
            {
                // From afar: at the circle's edge as seen from here, going round the way an orbit does.
                var side = Vec3d.Cross(Vec3d.UnitZ, -r);
                to = (side.Length > 1 ? side.Normalized() : Vec3d.UnitX) * o.Radius;
                arrive = Math.Sqrt(mu / o.Radius);
            }
            var gap = to - r;
            var way = gap.Length > 0.5 ? gap.Normalized() : Vec3d.Zero;
            for (var b = 0; b < sys.Count; b++)
            {
                var centre = b == o.Body ? Vec3d.Zero : sys.PositionOf(b, t) - bp;
                var keep = Math.Min(NavMinOrbit(sys, b) - NavEdgeMargin * 0.5, 0.92 * (to - centre).Length);
                way = NavGuidance.Avoid(r, to, way, centre, keep);
            }
            var gravity = NavGuidance.Gravity(sys, pos, t) - NavGuidance.Gravity(sys, bp, t, o.Body);
            a = NavGuidance.GoTo(r, v, to, way, arrive, gravity, aMax, out o.Distance, out o.SpeedError);
            o.Phase = o.Kind == NavKind.GoTo && o.Distance < 30 && o.SpeedError < 1.5 ? NavPhase.Hold : o.Handed ? NavPhase.Brake : NavPhase.Transfer;
        }
        var ask = a.Length;
        if (ask < 1e-4)
        {
            c.NavThrottle = 0;
            return;
        }
        // The World's axes in the ecliptic: the bubble's frame, or a parked ship's fixed ones (GameSpaceJump.cs).
        var want = c.SpaceAway != 0 ? AwayRot.InverseRotate(a / ask) : s.Bubble.PoseAt(t).Rot.InverseRotate(a / ask);
        c.NavAim = Vector3.Normalize(want.ToVector3());
        var cos = Vector3.Dot(Vector3.Transform(axis, c.Rot), c.NavAim);
        var aligned = Math.Clamp((cos - NavAlignNone) / (NavAlignFull - NavAlignNone), 0f, 1f);
        c.NavThrottle = (float)Math.Min(1, ask / aMax) * aligned;
    }

    /// <summary>The turn that brings a ship's drive towards where its order wants the push, a tick's worth (GameSpaceLaunch.cs poses it).</summary>
    private static Quaternion NavTurn(Construct c, float dt)
    {
        var mine = Vector3.Transform(c.NavAxis, c.Rot);
        var axis = Vector3.Cross(mine, c.NavAim);
        var sin = axis.Length();
        var cos = Vector3.Dot(mine, c.NavAim);
        if (sin < 1e-5f)
        {
            if (cos > 0)
                return Quaternion.Identity;
            axis = Vector3.Cross(mine, MathF.Abs(mine.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX); // (dead astern: any way round)
            sin = axis.Length();
        }
        return Quaternion.CreateFromAxisAngle(axis / sin, MathF.Min(MathF.Atan2(sin, cos) is var ang && ang > 0 ? ang : MathF.PI, NavTurnRate * dt));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The wire
    // ---------------------------------------------------------------------------------------------------------------

    private static void WriteNav(PacketWriter w, NavOrder o) =>
        w.Begin(Msg.Nav).U16(o.Construct).U8((byte)o.Kind).U8(o.Body).U8((byte)o.Phase).U8(o.By).F64(o.Point.X).F64(o.Point.Y).F64(o.Point.Z).F64(o.Radius)
            .U64((ulong)o.ArriveNs).F32(o.DvNeed);

    /// <summary>Host: a joiner hears every order.</summary>
    private void SendNavState(RakNetGUID to)
    {
        foreach (var o in NavOrders)
        {
            WriteNav(_w, o);
            Net.SendTo(to, _w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Client: an order began, changed or (kind None) ended.</summary>
    private void ReadNav(ref PacketReader r)
    {
        if (IsServer || !r.Has(2 + 4 + 8 * 4 + 8 + 4))
            return;
        var id = r.U16();
        var kind = (NavKind)r.U8();
        var body = r.U8();
        var phase = (NavPhase)r.U8();
        var by = r.U8();
        var point = new Vec3d(r.F64(), r.F64(), r.F64());
        var radius = r.F64();
        var arriveNs = (long)r.U64();
        var dvNeed = r.F32();
        var o = NavOf(id);
        if (kind == NavKind.None || kind > NavKind.Ground)
        {
            if (o != null)
                NavOrders.Remove(o);
            return;
        }
        if (o == null)
        {
            if (NavOrders.Count >= MaxNavOrders)
                return;
            NavOrders.Add(o = new NavOrder { Construct = id });
        }
        o.Kind = kind;
        o.Body = body;
        o.Phase = phase;
        o.By = by;
        o.Point = point;
        o.Radius = radius;
        o.ArriveNs = arriveNs;
        o.DvNeed = dvNeed;
    }

    /// <summary>The bubble changed (another map): construct ids start again, so no order outlives it.</summary>
    private void NavReset()
    {
        foreach (var o in NavOrders)
            if (Constructs.Get(o.Construct) is { } c)
            {
                c.NavThrottle = -1;
                c.NavClimb = 0;
                c.NavAim = Vector3.Zero;
            }
        NavOrders.Clear();
    }
}
