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
    Land, // fly to a spot on the ground of the bubble's world (World coordinates), from its sky or from space, and set down on it
    Come, // fly to the player who gave it (Point: the spot beside them, World coordinates, as last worked out): set down there, or in space hold beside them
    Dock, // dock with another craft (Target) at one of its docking ports (TargetPart): GameNavDock.cs
    Board, // a carrier's docked boarding pod flies to another ship's door (seals on it) or hull (cuts in): Point (target id, part, 1 for a door), the order on the pod (GameNavBoard.cs)
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
    Wait, // between planets: in orbit, drive off, until its launch window (NavOrder.DepartNs)
    Descend, // in space, coming down into the sky over its spot (a Land or Come order)
    Landing, // over its spot, coming down onto it on its autopilot (the order ends when it is down)
    Docking, // a Dock order: lined up with the port, closing on it (the order ends when the port holds it)
    DockWait, // a Dock order: lined up with the port, waiting for it to be armed (or powered)
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

    /// <summary>A Dock order: the craft it docks with and the port on it (a part index; the host may move it to another free one).</summary>
    public ushort Target, TargetPart;

    /// <summary>Host: a Dock order's way in (m out from the port's box), its look at the port (ticks) and the port clients were last told.</summary>
    internal float DockAlt;
    internal int DockLook;
    internal ushort TargetPartSent;
    internal bool DockTractor; // (a Dock order in a sky: the clamp's tractor has it, the autopilot only levels it)

    /// <summary>Host: what clients were last told, and whether the autopilot was set for the phase it is in.</summary>
    internal NavPhase PhaseSent;
    internal long ArriveSent;
    internal float DvSent;
    internal bool Engaged;

    /// <summary>Host: metres still to go and the speed still to change (m/s), for the log and the checks.</summary>
    public double Distance, SpeedError;

    /// <summary>
    /// A trip between planets (NavTrip.cs): when it arrives (space time, ns; 0: no trip planned) and the dV it spends
    /// (m/s; while <see cref="NavPhase.Short"/>, the least any trip would take). Sent to clients for the ship card.
    /// </summary>
    public long ArriveNs, DepartNs;
    public float DvNeed;

    /// <summary>Host: a trip between planets is being flown (planned), or it is over and the order's own law has the ship now.</summary>
    internal bool Cruise, Handed;

    /// <summary>Host: a plan being made over a few ticks (NavTrip.Search), from the planet the ship was at.</summary>
    internal bool Planning;
    internal NavTrip.Search Search;
    internal int PlanFrom;

    /// <summary>Host: the circle a ship keeps while it plans or waits for its window (GameNavTrip.NavPark).</summary>
    internal int ParkBody = -1;
    internal double ParkRadius;
    internal bool Parked;

    /// <summary>Host: ticks a landing ship has sat still while asked down (it is down at <see cref="Game.NavDownTicks"/>).</summary>
    internal int Still;

    /// <summary>Host: the waypoint its autopilot was last given (a Come order moves it after its player).</summary>
    internal Vector3 WaypointSent;

    /// <summary>Host: a Come order's player was out in space when last seen (it holds beside them there, not lands).</summary>
    internal bool ComeSpace;

    /// <summary>Host, a Board order (GameNavBoard.cs): the ship boarded, the frame its spot is kept in (that part's segment), the
    /// spot on its skin and the way out of it there (that frame), the door to open (-1: cut in), and ticks in the order's stage.</summary>
    internal ushort BoardTarget, BoardFrom;
    internal byte BoardSeg;
    internal System.Numerics.Vector3 BoardSpot, BoardOut, BoardUp, BoardDrop;
    internal System.Numerics.Quaternion BoardRot;
    internal sbyte BoardSide; // host: which way (+1/-1 along the carrier's X) the pod leaves from under it, chosen once (0: not yet)
    internal int BoardDropFor; // host: the ticks its drop out of the dock may take
    internal int BoardDoor = -1, BoardTicks, BoardAll;
}

/// <summary>
/// Orders from the command view (docs/space/COMMAND.md): a player picks a ship with a working flight computer (a Flight
/// Stabilizer) and tells it where to go, and the host flies it there with nobody at the controls. In space the nav computer
/// steers the ship's main drive (<see cref="NavGuidance"/>: the ship turns to where the push is wanted, burns its own
/// fuel or power through <see cref="ConstructPhysics.SpaceThrust"/>, and is a test particle as ever); inside a world's sky the
/// stabilizer's own autopilot flies it (a waypoint over the ground, or a climb to the sky's edge on the way to space).
/// A driver who touches the controls takes the ship back. Orders aren't saved, and a move to another world drops them.
/// A Land or Come order sets the ship down: from space it comes down into the sky over its spot, flies there on its
/// autopilot and comes down on it (<see cref="Construct.NavLand"/>), and the order ends once it is down, its stabilizer at rest.
/// <para>
/// The wire (protocol 341): <c>CActKind.NavOrder</c> from a client, and <see cref="Msg.Nav"/> from the host to everyone
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

    /// <summary>How fast an ordered landing comes down (m/s) until the ground is near, and how far down it looks for the ground (m).</summary>
    public const float NavLandSpeed = 40, NavLandLook = 600;

    /// <summary>How near its spot a landing starts to come down (m along the ground), and how far from the player a ship that comes to them sets down (m past its own size).</summary>
    public const float NavLandNear = 20, NavComeBeside = 8;

    /// <summary>How long a landing ship sits still near the ground while asked down before it counts as down, and how long anywhere before the order gives up and leaves it hovering (ticks).</summary>
    public const int NavDownTicks = 60, NavStuckTicks = 600;

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
            // A client's copy of a ship in space is the host's ship state, not its own lagging pose of the construct
            // (2026-11-16: at 100,000x the card said "falls back" for a ship the host had 2 km over Glacier).
            if (!IsServer && SpaceShipIdOf(c) is var ship and not 0 && s.SampleShip(ship, t, out pos, out vel))
                return true;
            // The host's likewise: its construct is posed at the last tick, and a frame drawn between ticks at
            // 100,000x is up to half an hour later, so the path drawn from the pose against bodies at the frame's time
            // jumped about every frame (2026-11-17). The ship's own orbit carried to the time moves with them.
            if (IsServer && c.SpaceShip != 0 && s.Flight is { } hf && hf.Ships.IndexOfId(c.SpaceShip) is var hi and >= 0)
            {
                hf.Ships.StateAt(hi, t, out pos, out vel);
                return true;
            }
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
            f.Ships.StateAt(i, t, out pos, out vel); // (carried to the time asked: a frame between ticks)
            return true;
        }
        return s.SampleShip(c.SpaceShip, t, out pos, out vel);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Giving an order
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>Client (or the host itself): an order for a ship, or <see cref="NavKind.None"/> to cancel its order.</summary>
    public void NavSend(ushort construct, NavKind kind, int body, Vec3d point, double radius, ushort target = 0)
    {
        var w = SbW;
        w.Begin(Msg.CAct).U8((byte)CActKind.NavOrder).U16(construct).U8((byte)kind).U8((byte)body).F64(point.X).F64(point.Y).F64(point.Z).F64(radius).U16(target);
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
        if (!r.Has(2 + 1 + 1 + 8 * 4 + 2))
            return null;
        var id = r.U16();
        var kind = (NavKind)r.U8();
        int body = r.U8();
        var point = new Vec3d(r.F64(), r.F64(), r.F64());
        var radius = r.F64();
        var target = r.U16();
        if (Space is not { } s || Constructs.Get(id) is not { } c)
            return "That ship is gone";
        if (kind == NavKind.None)
        {
            if (NavOf(id) is { } had)
                NavEnd(had, c, "cancelled");
            return null;
        }
        if (kind > NavKind.Board || !double.IsFinite(point.X + point.Y + point.Z + radius))
            return null;
        if (kind == NavKind.Board)
            return NavBoardAct(s, c, point, p);
        return NavApply(s, c, kind, body, point, radius, p.Id, p.Name, p, out _, target);
    }

    /// <summary>
    /// Host: an order set on a ship (a player's from the command view, or a Route Computer's leg: GameSandboxLinked.cs), checked as a
    /// player's is, with everything a new order starts from reset (a cruise, a hand-over, a trip's plan). Null, or why it takes none.
    /// </summary>
    internal string? NavApply(SpaceSession s, Construct c, NavKind kind, int body, Vec3d point, double radius, byte by, string who, Player? p, out NavOrder? order, ushort target = 0)
    {
        order = null;
        var id = c.Id;
        if (NavProblem(c) is { } why)
            return "It takes no orders: " + why;
        var port = -1;
        if (kind == NavKind.Dock)
        {
            if (NavDockCheck(c, target, out port) is { } cant)
                return cant;
            body = s.Bubble.Body;
            point = Vec3d.Zero;
        }
        else
            target = 0;
        var sys = c.SpaceAway != 0 ? Galaxy.System(c.SpaceAway - 1) : s.System;
        if (body >= sys.Count)
            return null;
        if (kind == NavKind.Dock)
        {
            // (checked above: the other ship and its port)
        }
        else if (kind == NavKind.Ground)
        {
            if (c.SpaceShip != 0 || body != s.Bubble.Body)
                return "It is in space: fly it down into the sky first, or give it an orbit";
        }
        else if (kind is NavKind.Land or NavKind.Come)
        {
            if (c.SpaceAway != 0 || body != s.Bubble.Body)
                return "It can land only on the world you are on";
            if (kind == NavKind.Come)
            {
                if (p == null)
                    return "Only a player can call it to come"; // (a route leg never asks it: GameSandboxLinked.cs)
                if (!p.Alive)
                    return "You are dead: it has nobody to come to";
                if (p.Sim.RideBody == c.Id)
                    return "You are aboard it";
                point = NavBeside(c, p.Position, Vector3.Zero);
            }
            if ((kind == NavKind.Land || p == null || !NavPlayerInSpace(p)) && NavCantLand(c) is { } cant) // (a player out in space: it comes and holds beside them)
                return cant;
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
        o.By = by;
        o.Phase = NavPhase.None;
        o.PhaseSent = NavPhase.None;
        o.Engaged = false;
        o.Cruise = o.Handed = o.Planning = o.Parked = false;
        o.ParkBody = -1;
        o.ArriveNs = o.DepartNs = 0;
        o.DvNeed = 0;
        o.Still = 0;
        o.ComeSpace = kind == NavKind.Come && p != null && NavPlayerInSpace(p);
        o.Target = target;
        o.TargetPart = o.TargetPartSent = (ushort)Math.Max(0, port);
        o.DockAlt = 0;
        o.DockLook = 0;
        o.DockTractor = false;
        c.NavLand = 0; // (any new order, a player's or a route leg's, ends a landing's hold)
        order = o;
        Console.WriteLine($"[nav] {who} ordered construct {id}: {NavText(sys, o)}");
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
            NavKind.Land => "land on a spot on " + name,
            NavKind.Come => "come to its player",
            NavKind.Dock => "dock with construct " + o.Target,
            NavKind.Board => o.Point.Z >= 0.5 ? "board a ship by its door" : "board a ship through its hull",
            _ => "none",
        };
    }

    private static string NavFar(double m) => m >= 1.496e9
        ? FormattableString.Invariant($"{m / 1.496e11:0.00} AU")
        : m >= 1e7 ? FormattableString.Invariant($"{m / 1e6:0.0} thousand km") : FormattableString.Invariant($"{m / 1000:0.0} km");

    /// <summary>Host: an order is over (done with, cancelled, or its ship is gone): the drive is let go and everyone hears.</summary>
    private void NavEnd(NavOrder o, Construct? c, string why, bool down = false, bool docked = false)
    {
        if (c != null)
        {
            if (c.ThrustPuppet)
                NavBoardJetsOut(c); // (a pod's jets lit by its Board order: GameNavBoard.cs)
            c.NavThrottle = -1;
            c.NavClimb = 0;
            c.NavLand = 0;
            c.NavAim = Vector3.Zero;
            if (c.SpaceShip != 0)
                c.AngVel = Vector3.Zero; // (the order's turn isn't a spin to hand to a ship that turns by its jets)
            if (down)
                ConstructFlight.NavSetDown(Constructs, c); // (down: its autopilot off and its stabilizer at rest, as a pilot leaves it)
            else if (docked)
                ConstructFlight.NavRelease(Constructs, c); // (held by the port now: its autopilot let go, as a route's ship leaves it docked)
            else if (c.SpaceShip == 0 && o.Engaged)
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
        NavPlanLeft = NavPlanAllTicks;
        if (NavOrders.Count == 0)
            return;
        for (var k = NavOrders.Count - 1; k >= 0; k--)
        {
            var o = NavOrders[k];
            var c = Constructs.Get(o.Construct);
            if (o.Kind == NavKind.Board)
            {
                NavBoardTick(o, c); // (a pod: no flight computer of its own needed, and nobody at its controls: GameNavBoard.cs)
                if (o.Kind != NavKind.None)
                    NavPhaseOut(o);
                continue;
            }
            if (o.Kind == NavKind.Dock && c != null && c.CarriedBy != 0 && c.CarriedBy == o.Target)
            {
                NavEnd(o, c, "it has docked", docked: true); // (in a sky the port took it: a clamp's tractor or a bay's box)
                continue;
            }
            if (c == null || NavProblem(c) != null || !SandboxOn)
            {
                NavEnd(o, c, "the ship can't fly it any more");
                continue;
            }
            if (Constructs.HumanDriver(c) && ((c.DriveKeys & NavManualKeys) != 0 || c.LiftInput != 0) && o.Phase != NavPhase.None)
            {
                NavEnd(o, c, "its driver took the controls");
                continue;
            }
            if (c.Dirty)
                c.Rebuild();
            if (o.Kind == NavKind.Come && NavComeMode(o, c) is { } comeEnds)
            {
                NavEnd(o, c, comeEnds);
                continue;
            }
            if (o.Kind == NavKind.Dock)
            {
                if (!NavDockTick(s, o, c))
                    continue;
            }
            else if (c.SpaceShip == 0)
                NavInSky(s, o, c);
            else
                NavInSpace(s, o, c);
            if (o.Kind == NavKind.None)
                continue; // (it ended there: a landing is down)
            NavPhaseOut(o);
        }
    }

    /// <summary>Host: a change of an order's phase (or a Dock order's port) goes out to everyone.</summary>
    private void NavPhaseOut(NavOrder o)
    {
        if (o.Phase != o.PhaseSent || o.ArriveNs != o.ArriveSent || o.DvNeed != o.DvSent || o.TargetPart != o.TargetPartSent)
        {
            o.PhaseSent = o.Phase;
            o.TargetPartSent = o.TargetPart;
            o.ArriveSent = o.ArriveNs;
            o.DvSent = o.DvNeed; // (a Short order's least dV changes as the planets move)
            WriteNav(_w, o);
            Net.Broadcast(_w.Span, PacketReliability.ReliableOrdered);
        }
    }

    /// <summary>Inside a world's sky the stabilizer's autopilot flies it: to the waypoint, or straight up to the edge on the way to space.</summary>
    private void NavInSky(SpaceSession s, NavOrder o, Construct c)
    {
        c.NavThrottle = -1;
        var climbs = o.Kind is NavKind.GoTo or NavKind.Orbit or NavKind.Dock || o.Kind == NavKind.Come && o.ComeSpace; // (a Dock order comes here only to climb out to a ship in space)
        c.NavClimb = climbs ? NavClimbSpeed : 0; // (only a climb out to space hurries)
        var here = c.WorldCom;
        if (!climbs && o.Kind is NavKind.Land or NavKind.Come)
        {
            NavLandSky(o, c);
            return;
        }
        if (o.Kind == NavKind.Ground)
        {
            var to = o.Point.ToVector3();
            if (!o.Engaged)
                o.Engaged = NavWaypoint(c, to);
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
        c.NavLand = 0;
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
        if (o.Kind == NavKind.Dock)
        {
            if (Constructs.Get(o.Target) is not { } dockAt)
            {
                c.NavThrottle = 0;
                return;
            }
            a = NavDockInSpace(s, f, i, sys, o, c, dockAt, pos, f.Ships.VelocityOf(i), t, aMax);
            if (o.Kind == NavKind.None)
                return; // (docked, or given up: NavEnd let its drive go)
        }
        else if (!o.Handed && (o.Cruise || NavTrip.Crosses(sys, pos, t, o.Body)) && NavCruise(s, f, o, c, pos, t, aMax, out a))
        {
            // Between planets: the trip's own burns (GameNavTrip.cs).
        }
        else if (o.Kind == NavKind.Come && o.ComeSpace && Players.GetValueOrDefault(o.By) is { Alive: true } who)
            a = NavBesideInSpace(s, sys, o, c, who, pos, f.Ships.VelocityOf(i), t, aMax); // (its player is out in space: it comes and holds beside them)
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
            if (o.Kind is NavKind.Land or NavKind.Come)
                to = NavSkyOver(s, o, t) - bp;
            else if (o.Kind == NavKind.Orbit)
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
            o.Phase = o.Kind == NavKind.GoTo && o.Distance < 30 && o.SpeedError < 1.5 ? NavPhase.Hold
                : o.Kind is NavKind.Land or NavKind.Come && o.Distance < NavDescendNear ? NavPhase.Descend
                : o.Handed ? NavPhase.Brake : NavPhase.Transfer;
        }
        var ask = a.Length;
        // Near the target everything is real time: an insertion's push dips while the ship turns or between its
        // corrections, and one sped-up tick there (half an hour at 100,000x) flings the ship out of the planet's reach.
        if (ask >= 1e-4 || o.Phase is NavPhase.Insert or NavPhase.Transfer or NavPhase.Brake or NavPhase.Hold or NavPhase.Descend)
            NavSpeedCap = 1;
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
            .U64((ulong)o.ArriveNs).U64((ulong)o.DepartNs).F32(o.DvNeed).U16(o.Target).U16(o.TargetPart);

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
        if (IsServer || !r.Has(2 + 4 + 8 * 4 + 8 + 8 + 4 + 4))
            return;
        var id = r.U16();
        var kind = (NavKind)r.U8();
        var body = r.U8();
        var phase = (NavPhase)r.U8();
        var by = r.U8();
        var point = new Vec3d(r.F64(), r.F64(), r.F64());
        var radius = r.F64();
        var arriveNs = (long)r.U64();
        var departNs = (long)r.U64();
        var dvNeed = r.F32();
        var target = r.U16();
        var targetPart = r.U16();
        var o = NavOf(id);
        if (kind == NavKind.None || kind > NavKind.Board)
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
        o.DepartNs = departNs;
        o.DvNeed = dvNeed;
        o.Target = target;
        o.TargetPart = targetPart;
    }

    /// <summary>The bubble changed (another map): construct ids start again, so no order outlives it.</summary>
    private void NavReset()
    {
        foreach (var o in NavOrders)
            if (Constructs.Get(o.Construct) is { } c)
            {
                c.NavThrottle = -1;
                c.NavClimb = 0;
                c.NavLand = 0;
                c.NavAim = Vector3.Zero;
            }
        NavOrders.Clear();
    }
}
