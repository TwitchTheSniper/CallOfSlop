using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Orders to another planet (docs/space/COMMAND.md "Between planets"; Karl, 2026-11-16: ships coast between planets in
/// real time, flip-and-burn realism, the command view speeds time up). The host plans the trip once the ship is in space
/// (<see cref="NavTrip.Plan"/>: the quickest conic round the star whose two burns fit <see cref="NavTrip.TripShare"/> of
/// the ship's dV), burns out onto it (<see cref="NavPhase.Depart"/>), coasts with its drive off (<see cref="NavPhase.Coast"/>,
/// looking again every half second and burning back onto the plan when it has drifted: <see cref="NavPhase.Correct"/>),
/// and once it is as close as its drive needs to stop it hands the ship to the order's own law
/// (<see cref="NavGuidance.GoTo"/> or <see cref="NavGuidance.Orbit"/>), which flips it and brakes (<see cref="NavPhase.Brake"/>).
/// While a ship coasts towards its brake the time speed is held so that it gets there in no less than
/// <see cref="NavLeadSeconds"/> of real time (<see cref="NavSpeedCap"/>): at 100,000x a tick is half an hour of space.
/// <para>
/// <b>Jump drives</b> (Karl, 2026-11-16: an option, but most ships don't carry one and a jump is costly; the cost is not
/// decided): a jump within a system is not built yet. <see cref="NavJumpCost"/> is where its price goes, and the order
/// menu will offer a jump only when it gives a cost.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>Host: the fastest time speed the ships under orders allow this tick (int.MaxValue: no limit).</summary>
    internal int NavSpeedCap = int.MaxValue;

    /// <summary>
    /// Host settings (Settings > Gameplay, Karl 2026-11-16: a slider for fuel and speed): the share of a ship's dV a trip to
    /// another planet may spend (more fuel, a quicker trip; <see cref="NavTrip.TripShare"/> by default), and the fastest
    /// time speed the command view may run at.
    /// </summary>
    public double TripShare = NavTrip.TripShare;
    public int TimeSpeedMost = SpaceClock.MaxSpeed;

    /// <summary>The Trip fuel slider's ends (percent of the ship's dV).</summary>
    public const int TripFuelLeast = 10, TripFuelMost = 95;

    /// <summary>
    /// Host setting (Settings > Gameplay "Fuel use in space"): the percent of their fuel thrusters burn in space
    /// (<see cref="ConstructPhysics.SpaceFuelScale"/>, set from it each tick and sent with the space clock).
    /// </summary>
    public int SpaceFuelPercent = 100;

    /// <summary>The Fuel use in space slider's low end (percent): a tenth of the fuel, ten times the dV.</summary>
    public const int SpaceFuelLeast = 10;

    /// <summary>A coasting ship gets to its brake in no less than this much real time (s), however fast time runs.</summary>
    public const double NavLeadSeconds = 1.5;

    /// <summary>A coasting ship off its plan by more than this (m/s, or this share of its speed past the target) burns back onto it.</summary>
    public const double NavCorrectAt = 3, NavCorrectShare = 0.003;

    /// <summary>A burn onto the plan ends under this much left to change (m/s).</summary>
    public const double NavBurnDone = 0.4;

    /// <summary>How often (ticks) a coasting ship looks at its plan again, and how often a ship short of dV plans again.</summary>
    public const int NavCoastCheck = 30, NavShortRetry = 600;

    /// <summary>How many departure times a plan looks at a tick (each a scan of trip times: about 0.17 ms).</summary>
    public const int NavPlanPerTick = 8;

    /// <summary>
    /// Host: while a ship plans or waits for its window it keeps a circular orbit round the body it is at, at the height it
    /// has (no lower than that body's lowest), so a ship that was hovering or falling doesn't come down meanwhile.
    /// </summary>
    /// <summary>A parked ship's orbit is safe once its low point is this many times the least orbit (and stays so above 1x).</summary>
    private const double NavParkMargin = 1.15;

    /// <summary>The low and high points (distances from the centre) of the conic through r, v; the high one is infinite past escape.</summary>
    private static void NavApsides(Vec3d r, Vec3d v, double mu, out double low, out double high)
    {
        var dist = r.Length;
        var energy = 0.5 * v.LengthSquared - mu / dist;
        var h = Vec3d.Cross(r, v).Length;
        var e = Math.Sqrt(Math.Max(0, 1 + 2 * energy * h * h / (mu * mu)));
        var p = h * h / mu;
        low = p / (1 + e);
        high = e < 1 ? p / (1 - e) : double.PositiveInfinity;
    }

    private static void NavPark(StarSystem sys, NavOrder o, SpacePos pos, Vec3d vel, SpaceTime t, double aMax, out Vec3d a)
    {
        a = Vec3d.Zero;
        var body = NavGuidance.PrimaryAt(sys, pos, t);
        if (body == 0)
            return; // (round the star itself: it coasts)
        sys.StateOf(body, t, out var bp, out var bv);
        var r = pos - bp;
        if (o.ParkBody != body)
        {
            o.ParkBody = body;
            o.ParkRadius = Math.Max(r.Length, 1.2 * NavMinOrbit(sys, body)); // (chosen once: a circle chased at the height it has now never settles)
            o.Parked = false;
        }
        // A safe orbit is left alone, drive off, so time may run fast: its low point clear of the ground and its high
        // point well inside the world's reach. Only an unsafe one is trimmed (towards the circle), and it rests again
        // with a margin, so the drive doesn't wake on every wobble (a trim never ends on a ship that turns to push).
        var mu = sys.Bodies[body].Mu;
        var v = vel - bv;
        var least = NavMinOrbit(sys, body);
        var reach = SpaceShips.SphereOfInfluence(sys, body);
        NavApsides(r, v, mu, out var low, out var high);
        // (on rails a moon doesn't tug its planet, so its full pull wobbles a low orbit's shape by kilometres over the
        // days, Slopolis round Glacier: the orbit may wander as it likes, and is trimmed only near the sky's edge, under
        // which the ship would be a physics body in the bubble again)
        var safe = o.Parked ? low > least && high < 0.6 * reach : low > NavParkMargin * least && high < 0.5 * reach;
        o.Parked = safe;
        if (!safe)
            a = NavGuidance.Orbit(r, v, mu, o.ParkRadius, aMax, out _, out _);
    }

    /// <summary>The handover to the order's own law comes this much sooner than the drive needs to stop (a share).</summary>
    public const double NavBrakeMargin = 1.25;

    /// <summary>
    /// Host: a ship between planets. Sets the acceleration to ask for (ecliptic axes) and returns true while the trip
    /// flies it; false once it is near enough for the order's own law (it is then handed over for good).
    /// </summary>
    private bool NavCruise(SpaceSession s, SpaceFlight f, NavOrder o, Construct c, SpacePos pos, SpaceTime t, double aMax, out Vec3d a)
    {
        a = Vec3d.Zero;
        var sys = f.System;
        var i = f.Ships.IndexOfId(c.SpaceShip);
        var vel = f.Ships.VelocityOf(i);
        var offset = o.Kind == NavKind.GoTo ? o.Point : Vec3d.Zero;
        if (!o.Cruise)
        {
            if (o.Phase == NavPhase.Short && !o.Planning && (ServerTick + o.Construct) % NavShortRetry != 0)
                return true; // (drive off: it can't go; it looks again now and then, as the planets move)
            var dv = NavDvLeft(c, aMax);
            var share = Math.Clamp(TripShare, TripFuelLeast / 100.0, TripFuelMost / 100.0);
            if (!o.Planning)
            {
                o.PlanFrom = NavTrip.PlanetOf(sys, NavGuidance.PrimaryAt(sys, pos, t));
                o.Search = NavTrip.Search.Begin(sys, o.PlanFrom, o.Body, t);
                o.Planning = true;
            }
            // The plan looks at every departure from now to the next window, a few a tick (a whole plan is tens of ms).
            NavSpeedCap = 1;
            o.Search.Step(sys, o.PlanFrom, pos, vel, o.Body, offset, aMax, dv * share, NavPlanPerTick);
            NavPark(sys, o, pos, vel, t, aMax, out a);
            if (!o.Search.Done)
                return true;
            o.Planning = false;
            if (o.Search.BestArrive == 0)
            {
                if (o.Phase != NavPhase.Short)
                    Console.WriteLine(FormattableString.Invariant($"[nav] construct {c.Id} can't reach {sys.Bodies[o.Body].Name}: the least a trip takes is {o.Search.Need / 1000:0.0} km/s, it may spend {dv * share / 1000:0.0} of its {dv / 1000:0.0}"));
                o.Phase = NavPhase.Short;
                o.DvNeed = double.IsFinite(o.Search.Need) ? (float)o.Search.Need : 0;
                o.ArriveNs = o.DepartNs = 0;
                return true;
            }
            o.Cruise = true;
            o.DepartNs = o.Search.BestDepart;
            o.ArriveNs = o.Search.BestArrive;
            o.DvNeed = (float)o.Search.BestSpend;
            o.Phase = o.DepartNs - t.Ns > SpaceTime.NsPerSecond * 2 ? NavPhase.Wait : NavPhase.Depart;
            Console.WriteLine(FormattableString.Invariant($"[nav] construct {c.Id} to {sys.Bodies[o.Body].Name}: leaves in {(o.DepartNs - t.Ns) / 1e9 / 86400:0.0} days, {(o.ArriveNs - o.DepartNs) / 1e9 / 86400:0.0} days on the way, {o.DvNeed / 1000:0.00} km/s of its {dv / 1000:0.0}"));
        }
        if (o.Phase == NavPhase.Wait)
        {
            // In orbit, drive off, until its window: time may run fast, but not past the departure.
            var toGo = (o.DepartNs - t.Ns) / (double)SpaceTime.NsPerSecond;
            if (toGo > 0)
            {
                NavSpeedCap = Math.Min(NavSpeedCap, (int)Math.Clamp(toGo / NavLeadSeconds, 1, int.MaxValue));
                NavPark(sys, o, pos, vel, t, aMax, out a);
                return true;
            }
            o.Phase = NavPhase.Depart;
            Console.WriteLine($"[nav] construct {c.Id}'s window to {sys.Bodies[o.Body].Name}: it burns");
        }

        // Near enough to stop on its own law? (as fast as it closes, the distance its drive needs to stop from that, with margin)
        sys.StateOf(o.Body, t, out var tp, out var tv);
        var rel = pos - (tp + offset);
        var relV = vel - tv;
        var dist = rel.Length;
        var speed = relV.Length;
        var reach = o.Kind == NavKind.Orbit ? o.Radius : 0;
        var brake = NavBrakeMargin * speed * speed / (2 * NavGuidance.BrakeShare * aMax) + reach + 2 * NavMinOrbit(sys, o.Body);
        o.Distance = dist;
        if (dist < brake || o.ArriveNs - t.Ns < 0)
        {
            o.Handed = true;
            o.Cruise = false;
            Console.WriteLine(FormattableString.Invariant($"[nav] construct {c.Id} flips and brakes for {sys.Bodies[o.Body].Name}: {dist / 1000:0} km out at {speed:0} m/s"));
            return false;
        }
        var closing = dist > 1 ? -Vec3d.Dot(relV, rel) / dist : 0;
        var toBrake = (dist - brake) / Math.Max(closing, 1);
        NavSpeedCap = Math.Min(NavSpeedCap, (int)Math.Clamp(toBrake / NavLeadSeconds, 1, int.MaxValue));

        var burning = o.Phase is NavPhase.Depart or NavPhase.Correct;
        if (burning)
            NavSpeedCap = 1; // (a burn is real time, also while it turns to it: time racing on would move its aim every tick)
        if (!burning && (ServerTick + o.Construct) % NavCoastCheck != 0)
            return true;
        if (!NavTrip.Burn(sys, pos, vel, t, o.Body, offset, new SpaceTime(o.ArriveNs), out var dvOut, out _))
        {
            o.Phase = NavPhase.Coast; // (no conic now: it coasts on and looks again)
            return true;
        }
        o.SpeedError = dvOut.Length;
        if (o.SpeedError < (burning ? NavBurnDone : Math.Max(NavCorrectAt, NavCorrectShare * speed)))
        {
            if (burning)
                Console.WriteLine(FormattableString.Invariant($"[nav] construct {c.Id} coasts: {(o.ArriveNs - t.Ns) / 1e9 / 86400:0.0} days to {sys.Bodies[o.Body].Name}"));
            o.Phase = NavPhase.Coast;
            return true;
        }
        if (!burning)
        {
            o.Phase = NavPhase.Correct;
            NavSpeedCap = 1;
        }
        var way = dvOut / o.SpeedError;
        // Round any body in the way (a burn out of a low orbit may point at the ground).
        for (var b = 1; b < sys.Count; b++)
        {
            var centre = sys.PositionOf(b, t) - pos;
            var keep = NavMinOrbit(sys, b) - NavEdgeMargin * 0.5;
            if (centre.LengthSquared < 400 * keep * keep)
                way = NavGuidance.Avoid(Vec3d.Zero, way * 1e12, way, centre, keep);
        }
        a = way * Math.Min(o.SpeedError / NavGuidance.Tau, aMax * 2);
        return true;
    }

    /// <summary>The change of speed a ship's fuel buys at full thrust (m/s): a drive on power is planned on <see cref="NavTrip.PowerDv"/>.</summary>
    internal static double NavDvLeft(Construct c, double aMax)
    {
        ConstructPhysics.NavDrive(c, out _, out _, out var burn);
        return burn > 0 ? aMax * c.FuelLeft / burn : NavTrip.PowerDv;
    }

    /// <summary>
    /// The price of a jump within the system to <paramref name="body"/> (Karl, 2026-11-16: costly, the cost to be decided).
    /// Null: no jump on offer, which is every ship for now. When the cost is decided it goes here (and what pays it: power,
    /// a fuel, a part worn), and the order menu offers a jump for a ship with a working Jump Drive when this gives one.
    /// </summary>
    public static string? NavJumpCost(Construct c, StarSystem sys, int body) => null;
}
