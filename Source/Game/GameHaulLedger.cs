using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Haulers on the ledger (Karl, 2026-11-22: far things as "a datapoint", so a world can run its economy for years). A station
/// hauler that no person is near flies no leg: it takes the leg's time on a schedule (<see cref="HaulLedgerMinutes"/>: the
/// phasing between its stations' circles round one planet, or a leg between planets in play time as <c>HaulFarOut</c> takes it),
/// burns its share of fuel, and at the leg's end is put beside its station (on the station's orbit a little behind it, its
/// escorts with it) and trades there as one on its circle does, with its own bins and tanks: the same <c>HaulTrade</c>. Its
/// escorts fly nothing while it is on the ledger. A person coming within <see cref="LedgerRange"/> of it ends the ledger leg where
/// it is and it flies the rest; with a person by the station it comes up behind it out of their sight (<see cref="LedgerOutOfSight"/>)
/// and flies in to dock. A leg once seen is flown to its end. A lost escort is replaced at the hauler's next real docking. <see cref="LedgerAll"/> (the long check's fast
/// clock, <see cref="EconomyFastSecond"/>) puts every hauler on it, near anyone or not.
/// </summary>
public sealed partial class Game
{
    /// <summary>No hauler goes on the ledger within this many metres of a person (a ship farther off is a dot, and the bubble's edge is past it).</summary>
    private const float LedgerRange = 12000;

    /// <summary>A leg round one planet on the ledger: the docking's minutes on top of the phasing, and the least and most it takes.</summary>
    private const double LedgerDockMinutes = 3, LedgerLeast = 5, LedgerMost = 60;

    /// <summary>The share of a full tank a leg round one planet burns (one between planets burns <c>HaulFarFuel</c>).</summary>
    private const float LedgerNearFuel = 0.1f;

    /// <summary>Every hauler on the ledger, people near or not (the long check's fast clock).</summary>
    public bool LedgerAll;

    /// <summary>No hauler on the ledger in play (a tool's switch, <c>--economy-sim --no-ledger</c>, to compare).</summary>
    public bool LedgerOff;

    /// <summary>Legs taken on the ledger since the game began (a tool's count).</summary>
    public int LedgerLegs;

    private readonly Vector3[] _ledgerPeople = new Vector3[64];

    /// <summary>
    /// Host, from <c>HaulStep</c> on a hauler's Going leg: its ledger leg started, ended or still running (true: nothing else this
    /// second), or false: it flies the leg (a person is near, or it can't go on the ledger).
    /// </summary>
    private bool HaulLedger(SpaceSession s, HaulRun run, Construct c, int def, StationDef d, int planet)
    {
        if (run.LedgerUntil != 0)
        {
            if (!LedgerAll && LedgerSeen(run, c))
                LedgerBreak(run, c); // (a person comes near part way: it flies the rest from where it is, nothing moved before their eyes)
            else if (run.Trip is { } going)
            {
                if (LedgerAll || s.Flight is not { } tf || !(going.Far ? LedgerFarStep(tf, run, c, going, def, planet) : LedgerTripStep(tf, run, c, going, planet))) // (on the fast clock space time stands still: it is put at its station)
                    LedgerArrive(s, run, c, def, d, planet); // (its path's end, or its path lost: it is put at its station)
            }
            else if (ServerTick >= run.LedgerUntil)
                LedgerArrive(s, run, c, def, d, planet);
            return run.LedgerUntil != 0 || run.Stage != HaulStage.Going;
        }
        if (run.Flying || LedgerOff && !LedgerAll || run.PutAt >= 0 || run.FarUntil != 0 || planet < 0 || d.Orbit <= 0 || c.SpaceAway != 0 && c.SpaceAway - 1 != s.GalaxyIndex)
            return false;
        if (!LedgerAll)
        {
            if (!OutOfBubble(run, c))
                return false; // (it flies out of the bubble first; the ledger takes the rest of the leg)
            if (LedgerSeen(run, c) || Constructs.Get(StationBodyOf(def)) is { } to && LedgerSeesAt(to.WorldCom, LedgerRange + LedgerBehindSeen))
            {
                run.Flying = true; // (seen, or bound for a station someone is by: it flies this leg, and they see it come)
                return false;
            }
        }
        var h = StationMarkets.Haulers[run.Route];
        var leaving = StationMarkets.All[run.AtTo ? h.From : h.To];
        var from = StationPlanet(s, leaving);
        var far = from >= 0 && from != planet;
        var minutes = HaulLedgerMinutes(s, from >= 0 ? from : planet, leaving.Orbit, planet, d.Orbit);
        var resumed = run.Adopted && c.MooredTo == 0; // (a loaded world's hauler out on a leg: half a leg more, its fuel spent already, as HaulFarOut)
        run.Adopted = false;
        if (resumed)
            minutes *= 0.5;
        // Flown part way already (out of the bubble): that time and fuel come off the leg's.
        var flown = run.LegAt != 0 && !resumed ? (ServerTick - run.LegAt) / (60.0 * World.TickRate) : 0;
        minutes = Math.Max(1, minutes - flown);
        if (c.MooredTo != 0)
            StationCastOff(c);
        if (NavOf(c.Id) is { } o)
            NavEnd(o, c, "it goes on the ledger: nobody is near");
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } ec && NavOf(ec.Id) is { } eo)
                NavEnd(eo, ec, "its hauler goes on the ledger");
        if (!resumed)
        {
            var burned = run.LegAt != 0 ? MathF.Max(0, run.LegFuel - c.Fuel) : 0;
            c.Fuel = MathF.Max(0, c.Fuel - MathF.Max(0, c.FuelCapacity() * (far ? HaulFarFuel : LedgerNearFuel) - burned));
        }
        run.Ledger = true;
        run.LedgerUntil = ServerTick + (long)(minutes * 60 * World.TickRate);
        run.RetryAt = 0;
        LedgerLegs++;
        if (!far && !LedgerAll && LedgerPlan(s, run, c, def, planet) is { } trip)
        {
            run.Trip = trip; // (round one planet in play: its path is real, in space time: LedgerPlan)
            HaulSay(run, c, FormattableString.Invariant($"sets out for {d.Name} on the ledger: a burn of {trip.Dv1.Length:0} m/s, {(trip.Arrive - trip.Start) / 6e10:0} minutes' coast and {trip.Dv2.Length:0} m/s to brake, at {trip.Accel / 9.81:0.0} g"));
            RouteArrived(run.Route);
            return true;
        }
        if (far && !LedgerAll && LedgerFarPlan(s, run, c, def, from, planet, minutes) is { } route)
        {
            run.Trip = route; // (between planets in play: the real route round the star at the leg's pace, LedgerFarPlan)
            HaulSay(run, c, FormattableString.Invariant($"sets out for {d.Name} on the ledger: {route.Tof / 86400:0.0} days' transfer round {s.System.Bodies[route.Centre].Name}, flown in {minutes:0} minutes of play"));
            RouteArrived(run.Route);
            return true;
        }
        HaulSay(run, c, FormattableString.Invariant($"sets out for {d.Name} on the ledger: {minutes:0} minutes of play away"));
        RouteArrived(run.Route); // (set out: its supply line runs, as a flown leg's)
        return true;
    }

    /// <summary>
    /// Host: a person has come near a hauler part way along its ledger leg. It stays where it is (it coasted on, out of the bubble, from where
    /// it set out) and flies the rest of the leg for real; its escorts take their orders again.
    /// </summary>
    private void LedgerBreak(HaulRun run, Construct c)
    {
        run.LedgerUntil = 0;
        if (run.Trip != null)
        {
            if (run.Trip.Far)
            {
                if (Space?.Flight is { } f)
                    LedgerFarRealSpeed(f, run, c, run.Trip); // (it goes on at the transfer's own speed, not the leg's pace)
                run.FarUntil = Math.Max(ServerTick + World.TickRate, run.Trip.ToTick); // (its leg's time goes on as a flown leg between planets, paid for already: not a new one)
            }
            LedgerCoasting(run, c, false);
        }
        run.Trip = null;
        run.Ledger = false;
        run.Flying = true;
        run.RetryAt = 0;
        HaulSay(run, c, "comes off the ledger part way: someone is near it");
    }

    /// <summary>
    /// Are the hauler and its escorts ships out of the bubble (posed by their flights, out of the physics)? In play one goes on the ledger only
    /// then: a hauler parked in the bubble, its crew and escorts with it, costs the physics and crews more than one flying (2026-11-22: 2.5 times the
    /// tick in the long check), so it flies out of the bubble first as before.
    /// </summary>
    private bool OutOfBubble(HaulRun run, Construct c)
    {
        if (c.SpaceShip == 0)
            return false;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: 0 })
                return false;
        return true;
    }

    /// <summary>How far behind its station (on the station's orbit) a ledger hauler comes up when a person is near the station (m).</summary>
    private const float LedgerBehindSeen = 20000;

    /// <summary>Is a person within <see cref="LedgerRange"/> of the hauler or one of its escorts?</summary>
    private bool LedgerSeen(HaulRun run, Construct c)
    {
        // (a ship parked far out, GameSpaceJump.cs, sits on a parking grid with every other: its place there says nothing of who is near it)
        if (c.SpaceAway == 0 && LedgerSeesAt(c.WorldCom, LedgerRange))
            return true;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceAway: 0 } ec && LedgerSeesAt(ec.WorldCom, LedgerRange))
                return true;
        return false;
    }

    /// <summary>Is a person within <paramref name="range"/> of <paramref name="at"/>?</summary>
    private bool LedgerSeesAt(Vector3 at, float range)
    {
        var n = InterestPoints(_ledgerPeople);
        for (var k = 0; k < n; k++)
            if (Vector3.DistanceSquared(_ledgerPeople[k], at) < range * range)
                return true;
        return false;
    }

    /// <summary>
    /// Minutes of play a ledger leg takes: between planets as <c>HaulFarMinutes</c> has it; round one planet, the time a circle a phasing
    /// step under or over the station's takes to come round half a turn on it (half the synodic period of the two circles), and the docking.
    /// </summary>
    private static double HaulLedgerMinutes(SpaceSession s, int from, float fromOrbit, int to, float toOrbit)
    {
        if (from != to && s.Flight is { } f)
            return HaulFarMinutes(s.System, from, to, f.Ships.Time);
        var body = s.System.Bodies[to];
        var r1 = body.Radius + Math.Max(0, fromOrbit);
        var r2 = body.Radius + toOrbit;
        if (Math.Abs(r1 - r2) < HaulPhaseBy)
            r1 = r2 + HaulPhaseBy; // (one circle: it phases on a circle a step over the station's, as a flown leg does)
        double W(double r) => Math.Sqrt(body.Mu / (r * r * r));
        var gap = Math.Abs(W(r1) - W(r2));
        var phasing = gap > 0 ? Math.PI / gap / 60 : LedgerMost; // (half a turn on average: Pi over the rate the two circles part)
        return Math.Clamp(phasing + LedgerDockMinutes, LedgerLeast, LedgerMost);
    }

    /// <summary>
    /// Host: a ledger leg's end. The hauler is put on its station's orbit a little behind it (its station in this world's space) or on
    /// the station's circle (in another world), its escorts behind it, as a leg between planets ends (<c>HaulFarArrive</c>); a hauler not
    /// in this system's flight stays where it is. It trades there as one on its circle does, and waits its time before the next leg.
    /// </summary>
    private void LedgerArrive(SpaceSession s, HaulRun run, Construct c, int def, StationDef d, int planet)
    {
        run.LedgerUntil = 0;
        if (run.Trip != null)
            LedgerCoasting(run, c, false);
        run.Trip = null;
        // A person by the station: it comes up out of sight behind it and flies the rest (its Dock order: HaulStep), so nothing appears beside them.
        var stBody = StationBodyOf(def);
        var stc = Constructs.Get(stBody);
        var seen = !LedgerAll && stc != null && LedgerSeesAt(stc.WorldCom, LedgerRange + LedgerBehindSeen);
        if (s.Flight is { } f && f.Ships.IndexOfId(c.SpaceShip) is var i and >= 0)
        {
            Vec3d r, v;
            var mu = s.System.Bodies[planet].Mu;
            var st = stBody;
            if (st != 0 && Constructs.Get(st) is { SpaceShip: not 0, SpaceAway: 0 } sb && f.Ships.IndexOfId(sb.SpaceShip) is var si and >= 0 && f.Ships.PrimaryOf(si) == planet)
            {
                f.Ships.RelativeOf(si, out _, out var sr, out var sv);
                var n = Vec3d.Cross(sr, sv);
                var behind = seen ? LedgerOutOfSight(stc!.WorldCom, sr.Length) : HaulFarBehind;
                var q = QuatD.AxisAngle(n.LengthSquared > 0 ? n.Normalized() : Vec3d.UnitY, -behind / Math.Max(1, sr.Length));
                (r, v) = (q.Rotate(sr), q.Rotate(sv));
            }
            else
            {
                var radius = s.System.Bodies[planet].Radius + d.Orbit;
                var (sin, cos) = Math.SinCos(run.Ship * 2.399963); // (each hauler its own place on the circle)
                r = new Vec3d(radius * cos, 0, radius * sin);
                v = new Vec3d(-sin, 0, cos) * Math.Sqrt(mu / radius);
            }
            f.Ships.SetState(i, planet, r, v);
            var k = 0;
            foreach (var e in _escorts)
                if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: not 0 } ec && f.Ships.IndexOfId(ec.SpaceShip) is var ei and >= 0)
                {
                    var q = QuatD.AxisAngle(Vec3d.Cross(r, v).Normalized(), -(1500.0 * ++k) / r.Length); // (a little behind it, on its orbit)
                    f.Ships.SetState(ei, planet, q.Rotate(r), q.Rotate(v));
                }
        }
        if (seen)
        {
            run.Ledger = false;
            run.Flying = true;
            run.RetryAt = 0;
            HaulSay(run, c, $"comes up behind {d.Name} off the ledger: someone is near it");
            return; // (still Going: its Dock order next second)
        }
        HaulSay(run, c, $"comes to {d.Name} on the ledger");
        run.Ledger = LedgerAll; // (in play its escorts take their orders again at the station; on the fast clock they stay put beside it)
        run.Stage = HaulStage.Orbiting;
        run.Since = ServerTick;
        RouteArrived(run.Route);
        HaulTrade(run, c, StationMarkets.Haulers[run.Route], def, MarketOfDef(def));
    }


    // ---- A ledger leg's path round one planet (Karl, 2026-11-22: "the increase in speed from the station and decrease at an expected point") ----

    /// <summary>
    /// A ledger leg round one planet in play, as a function of space time: a burn out of <see cref="Dv1"/> at the ship's own acceleration from
    /// <see cref="Start"/>, a coast on the conic it puts the ship on (the flight carries it on its Kepler rail, so it moves on the map and is
    /// where it should be at any moment), and a braking burn of <see cref="Dv2"/> centred on <see cref="Arrive"/>, which brings it up
    /// <c>HaulFarBehind</c> behind its station at the station's speed; then it docks (<see cref="End"/>). Its escorts fly the same path
    /// turned back round the planet by <see cref="EscortGap"/> metres each (a turn about the planet's centre keeps a path a path).
    /// </summary>
    private sealed class LedgerTrip
    {
        public long Start, Arrive, End, Last; // space time (ns): set out, the braking burn's middle, docked, and when the burns were last stepped
        public Vec3d Dv1, Dv2, Axis; // the burns (the hauler's planet frame), and the axis its escorts' paths are turned about
        public double Accel, Done1, Done2; // the hauler's acceleration (m/s/s) and how much of each burn is done (m/s)
        public bool Snapped;
        public ushort Station; // the station body it is bound for
        public int Fixes; // the course corrections made on the coast (at LedgerFixAt)
        // A leg between planets (Far): the transfer conic round the star from where the planet it leaves was to where the one it is bound
        // for was at set-out (R1, V1 from the star; Tof its real time), flown at the leg's pace in play ticks (FromTick to ToTick).
        public bool Far;
        public int From, To;
        public int Centre, AtDef; // the body the transfer goes round, and the station it is bound for (StationMarkets.All)
        public Vec3d R1, V1, R2, A0;
        public double Tof;
        public long FromTick, ToTick;
        public const double EscortGap = 1500;
    }

    /// <summary>Where along a ledger path's coast its course is corrected (shares of the coast: a burn stepped a second at a time, on a transfer near half a turn, can miss by kilometres).</summary>
    private static readonly double[] LedgerFixAt = { 0.25, 0.6, 0.9 };

    /// <summary>The transfer times tried for a ledger path (between the least and most a leg takes, less the docking).</summary>
    private const int LedgerPlanTries = 48;

    /// <summary>
    /// Host, rare: a ledger path for a hauler round its station's planet, both in this system's flight: the transfer to a place
    /// <c>HaulFarBehind</c> behind the station, the time picked for the least change of speed (two Lambert burns), with the burns at most a
    /// fifth of the coast. Its escorts are put on the same path behind it. Null: no path (not both in the flight round that planet, no drive,
    /// or no transfer fits), and the leg runs as a time alone.
    /// </summary>
    private LedgerTrip? LedgerPlan(SpaceSession s, HaulRun run, Construct c, int def, int planet)
    {
        if (s.Flight is not { } f || f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0) || f.Ships.PrimaryOf(i) != planet || c.SpaceAway != 0 && c.SpaceAway - 1 != s.GalaxyIndex
            || Constructs.Get(StationBodyOf(def)) is not { SpaceShip: not 0, SpaceAway: 0 } sb || f.Ships.IndexOfId(sb.SpaceShip) is not (var si and >= 0) || f.Ships.PrimaryOf(si) != planet)
            return null;
        var accel = c.Mass > 0 ? ConstructPhysics.NavDrive(c, out _, out _, out _) / c.Mass : 0;
        if (!(accel > 0.01))
            return null;
        f.Ships.RelativeOf(i, out _, out var r0, out var v0);
        f.Ships.RelativeOf(si, out _, out var sr, out var sv);
        var mu = s.System.Bodies[planet].Mu;
        var sn = Vec3d.Cross(sr, sv);
        var axis = sn.LengthSquared > 0 ? sn.Normalized() : Vec3d.UnitY;
        double best = double.MaxValue, bestT = 0;
        Vec3d b1 = Vec3d.Zero, b2 = Vec3d.Zero;
        var least = Math.Max(60, (LedgerLeast - LedgerDockMinutes) * 60);
        var most = (LedgerMost - LedgerDockMinutes) * 60;
        for (var k = 0; k < LedgerPlanTries; k++)
        {
            var t = least + (most - least) * k / (LedgerPlanTries - 1);
            if (!UniversalKepler.Propagate(mu, sr, sv, t, out var rs, out var vs))
                continue;
            var q = QuatD.AxisAngle(axis, -HaulFarBehind / Math.Max(1, rs.Length));
            var (rt, vt) = (q.Rotate(rs), q.Rotate(vs));
            if (Vec3d.Dot(r0, rt) / (r0.Length * rt.Length) < -0.985)
                continue; // (near half a turn the transfer's plane is ill set: a small error at the burn misses by kilometres)
            for (var way = 0; way < 2; way++)
            {
                if (!NavTrip.Lambert(r0, rt, t, mu, way == 1, out var v1, out var v2))
                    continue;
                var (d1, d2) = (v1 - v0, vt - v2);
                var cost = d1.Length + d2.Length;
                if (cost < best && cost / accel < t * NavTrip.BurnShare)
                    (best, bestT, b1, b2) = (cost, t, d1, d2);
            }
        }
        if (bestT <= 0)
            return null;
        var now = f.Ships.Time.Ns;
        var trip = new LedgerTrip
        {
            Start = now, Last = now, Accel = accel, Dv1 = b1, Dv2 = b2, Axis = axis, Station = sb.Id,
            Arrive = now + (long)(bestT * SpaceTime.NsPerSecond),
        };
        trip.End = trip.Arrive + (long)((b2.Length / accel / 2 + LedgerDockMinutes * 60) * SpaceTime.NsPerSecond);
        // Its escorts on its path, behind it (they were by it; nobody is near to see them close up).
        var n = 0;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: not 0 } ec && f.Ships.IndexOfId(ec.SpaceShip) is var ei and >= 0)
            {
                var q = QuatD.AxisAngle(axis, -(LedgerTrip.EscortGap * ++n) / Math.Max(1, r0.Length));
                f.Ships.SetState(ei, planet, q.Rotate(r0), q.Rotate(v0));
            }
        LedgerCoasting(run, c, true);
        LedgerTripStep(f, run, c, trip, planet); // (the burn's first part now)
        return trip;
    }

    /// <summary>
    /// Host, each second of a ledger path: the burns as far as space time has come (the ship's acceleration times the time since the last step,
    /// so a fast time speed makes them kicks), the hauler and its escorts snapped onto their places behind the station once braked (the
    /// burns stepped a second at a time land them a little off). False: the path is done (docked) or lost.
    /// </summary>
    private bool LedgerTripStep(SpaceFlight f, HaulRun run, Construct c, LedgerTrip trip, int planet)
    {
        if (f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0) || f.Ships.PrimaryOf(i) != planet)
            return false;
        var now = f.Ships.Time.Ns;
        var dt = (now - trip.Last) / (double)SpaceTime.NsPerSecond;
        var brakeFrom = trip.Arrive - (long)(trip.Dv2.Length / trip.Accel / 2 * SpaceTime.NsPerSecond);
        var push = Vec3d.Zero;
        if (trip.Done1 < trip.Dv1.Length)
        {
            var amount = Math.Min(trip.Dv1.Length - trip.Done1, trip.Accel * Math.Max(dt, 1.0 / World.TickRate));
            trip.Done1 += amount;
            push = trip.Dv1.Normalized() * amount;
        }
        var braking = !trip.Snapped && now >= brakeFrom;
        var brakeDt = braking ? Math.Max(1.0 / World.TickRate, (now - Math.Max(trip.Last, brakeFrom)) / (double)SpaceTime.NsPerSecond) : 0;
        trip.Last = now;
        if (push.LengthSquared > 0)
            LedgerPush(f, run, i, planet, push, trip.Axis);
        if (trip.Done1 >= trip.Dv1.Length && trip.Fixes < LedgerFixAt.Length && now >= trip.Start + (long)((trip.Arrive - trip.Start) * LedgerFixAt[trip.Fixes]))
        {
            trip.Fixes++;
            LedgerFix(f, run, i, planet, trip, now);
        }
        if (braking)
        {
            // The braking burn closed-loop: towards the speed of its place behind the station, at the ship's acceleration, until matched.
            if (Constructs.Get(trip.Station) is { SpaceShip: not 0 } sb && f.Ships.IndexOfId(sb.SpaceShip) is var si and >= 0 && f.Ships.PrimaryOf(si) == planet)
            {
                f.Ships.RelativeOf(si, out _, out var sr, out var sv);
                f.Ships.RelativeOf(i, out _, out _, out var v);
                var need = QuatD.AxisAngle(trip.Axis, -HaulFarBehind / Math.Max(1, sr.Length)).Rotate(sv) - v;
                var amount = Math.Min(need.Length, trip.Accel * brakeDt);
                if (amount > 0)
                {
                    trip.Done2 += amount;
                    LedgerPush(f, run, i, planet, need.Normalized() * amount, trip.Axis);
                }
                if (need.Length - amount < 0.5 && now >= trip.Arrive)
                {
                    trip.Snapped = true;
                    LedgerAlongside(f, run, c, i, planet, trip.Axis);
                }
            }
            else
                trip.Snapped = true;
        }
        return now < trip.End;
    }

    /// <summary>
    /// A course correction on a ledger path's coast: the transfer from where the hauler is now to its place behind the station at the
    /// path's arrival, solved again, its small change of speed made at once and the braking burn made new.
    /// </summary>
    private void LedgerFix(SpaceFlight f, HaulRun run, int i, int planet, LedgerTrip trip, long now)
    {
        var left = (trip.Arrive - now) / (double)SpaceTime.NsPerSecond;
        if (!(left > 10) || trip.Done2 > 0 || Constructs.Get(trip.Station) is not { SpaceShip: not 0 } sb || f.Ships.IndexOfId(sb.SpaceShip) is not (var si and >= 0) || f.Ships.PrimaryOf(si) != planet)
            return;
        var mu = f.System.Bodies[planet].Mu;
        f.Ships.RelativeOf(i, out _, out var r, out var v);
        f.Ships.RelativeOf(si, out _, out var sr, out var sv);
        if (!UniversalKepler.Propagate(mu, sr, sv, left, out var rs, out var vs))
            return;
        var q = QuatD.AxisAngle(trip.Axis, -HaulFarBehind / Math.Max(1, rs.Length));
        var (rt, vt) = (q.Rotate(rs), q.Rotate(vs));
        // (the way round that needs least: a path that set out the long way round is the short way once past half a turn)
        var ok1 = NavTrip.Lambert(r, rt, left, mu, false, out var s1, out var s2);
        var ok2 = NavTrip.Lambert(r, rt, left, mu, true, out var l1, out var l2);
        if (!ok1 && !ok2)
            return;
        var (v1, v2) = !ok2 || ok1 && (s1 - v).LengthSquared <= (l1 - v).LengthSquared ? (s1, s2) : (l1, l2);
        var dv = v1 - v;
        if (dv.Length > trip.Dv1.Length)
            return; // (a correction bigger than the burn out: something else went wrong, and the snap at the end puts it right)
        LedgerPush(f, run, i, planet, dv, trip.Axis);
        trip.Dv2 = vt - v2;
    }


    // ---- A ledger leg between planets (Karl's decision card, 2026-11-22: "real route, game-time pace") ----

    /// <summary>The share of a leg between planets at its end that the ship spends at its place by the station, off the route.</summary>
    private const double LedgerFarEnds = 0.03;

    /// <summary>The body a transfer between two bodies goes round: the nearest both are round (the star for two planets, the planet for a planet and its moon or two of its moons).</summary>
    private static int LedgerCentre(StarSystem sys, int a, int b)
    {
        for (var x = a; x >= 0; x = sys.Bodies[x].Parent)
            for (var y = b; y >= 0; y = sys.Bodies[y].Parent)
                if (x == y)
                    return x;
        return -1;
    }

    /// <summary>A body's place from the transfer's centre now (the centre itself: nothing, the ship's end place is given apart).</summary>
    private static Vec3d LedgerRel(StarSystem sys, int body, int centre, SpaceTime t) => body == centre ? Vec3d.Zero : sys.PositionOf(body, t) - sys.PositionOf(centre, t);

    /// <summary>
    /// Host, rare: the route of a ledger leg between planets: the transfer conic round the body both are round (the star, or a planet for
    /// its moons) from where the ship is now to where the far planet is now (or, bound for the centre itself, to its place there), its time
    /// picked near a Hohmann transfer's for the least change of speed against the bodies', none near half a turn. The ship is flown along it
    /// in <paramref name="minutes"/> of play, speeding up from its orbit and braking at the far end (a smooth step: a flip at the middle), and
    /// comes off it onto its place behind the station there. Null: no route (not in the flight, or no conic found), and the leg runs as a time alone.
    /// </summary>
    private LedgerTrip? LedgerFarPlan(SpaceSession s, HaulRun run, Construct c, int def, int from, int to, double minutes)
    {
        if (from < 0 || s.Flight is not { } f || f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0) || c.SpaceAway != 0 && c.SpaceAway - 1 != s.GalaxyIndex)
            return null; // (in this system's flight: here, or parked far out in it)
        var sys = s.System;
        var centre = LedgerCentre(sys, from, to);
        if (centre < 0)
            return null;
        var t = f.Ships.Time;
        var trip = new LedgerTrip { Far = true, From = from, To = to, Centre = centre, AtDef = def, Station = StationBodyOf(def), Accel = 1, Start = t.Ns, Last = t.Ns };
        sys.StateOf(centre, t, out var cp, out var cv);
        f.Ships.RelativeOf(i, out var prim, out var sr0, out var sv0);
        sys.StateOf(prim, t, out var pp, out var pv);
        var r1 = pp + sr0 - cp; // (from its primary as the system has it: see LedgerPose)
        var v0 = pv + sv0 - cv;
        var endAt = LedgerFarEnd(f, trip, run, StationMarkets.All[def], to, out var endVel);
        var r2 = endAt - cp; // (the route ends at its place by the station, not at the planet's middle)
        var vb = endVel - cv;
        var mu = sys.Bodies[centre].Mu;
        if (!(r1.Length > 1) || !(r2.Length > 1) || Vec3d.Dot(r1, r2) / (r1.Length * r2.Length) < -0.985)
            return null; // (either side of the centre: the transfer's plane is ill set)
        var hohmann = Math.PI * Math.Sqrt(Math.Pow((r1.Length + r2.Length) / 2, 3) / mu);
        double best = double.MaxValue, bestT = 0;
        var v1Best = Vec3d.Zero;
        for (var k = 0; k < 16; k++)
        {
            var tof = hohmann * (0.4 + 1.2 * k / 15);
            for (var way = 0; way < 2; way++)
            {
                if (!NavTrip.Lambert(r1, r2, tof, mu, way == 1, out var v1, out var v2)
                    || !UniversalKepler.Propagate(mu, r1, v1, tof, out var reach, out _) || (reach - r2).Length > 1e-4 * r2.Length)
                    continue; // (a conic that doesn't come to the far end: the solver didn't settle there)
                var cost = (v1 - v0).Length + (vb - v2).Length;
                if (cost < best)
                    (best, bestT, v1Best) = (cost, tof, v1);
            }
        }
        if (bestT <= 0)
            return null;
        UniversalKepler.Propagate(mu, r1, v1Best, bestT, out var rEnd, out _);
        (trip.R1, trip.V1, trip.Tof, trip.R2) = (r1, v1Best, bestT, rEnd); // (where the conic itself ends: the end's pull onto its place is measured from there)
        trip.A0 = LedgerRel(sys, from, centre, t);
        trip.FromTick = ServerTick;
        trip.ToTick = ServerTick + (long)(minutes * 60 * World.TickRate);
        LedgerCoasting(run, c, true);
        LedgerFarStep(f, run, c, trip, def, to);
        return trip;
    }

    /// <summary>
    /// The ledger ship's place on its route between planets at <paramref name="tau"/> of the leg (the ecliptic frame): along the conic at the
    /// leg's pace (a smooth step, so it speeds up and brakes), its start moved with the planet it left and its end onto <paramref name="endAt"/>
    /// (its place by the station now).
    /// </summary>
    private static SpacePos LedgerFarAt(StarSystem sys, LedgerTrip trip, SpaceTime t, double tau, SpacePos endAt)
    {
        tau = Math.Clamp(tau, 0, 1);
        var u = tau * tau * (3 - 2 * tau); // (speeding up to the middle, then braking: the flip at the midpoint)
        var mu = sys.Bodies[trip.Centre].Mu;
        var cp = sys.PositionOf(trip.Centre, t);
        if (!UniversalKepler.Propagate(mu, trip.R1, trip.V1, u * trip.Tof, out var r, out _))
            r = trip.R1 + (trip.R2 - trip.R1) * u;
        // The route's ends follow what has moved since it was planned: its start the planet it left, its end its place by the station.
        r = r + (LedgerRel(sys, trip.From, trip.Centre, t) - trip.A0) * (1 - u) + (endAt - cp - trip.R2) * u;
        return cp + r;
    }

    /// <summary>Where a ledger ship ends its leg between planets: behind its station there (in this world's flight), or on the station's circle.</summary>
    private SpacePos LedgerFarEnd(SpaceFlight f, LedgerTrip trip, HaulRun run, StationDef d, int planet, out Vec3d vel)
    {
        var sys = f.System;
        sys.StateOf(planet, f.Ships.Time, out var bp, out var bv);
        if (Constructs.Get(trip.Station) is { SpaceShip: not 0, SpaceAway: 0 } sb && f.Ships.IndexOfId(sb.SpaceShip) is var si and >= 0 && f.Ships.PrimaryOf(si) == planet)
        {
            f.Ships.RelativeOf(si, out _, out var sr, out var sv);
            var n = Vec3d.Cross(sr, sv);
            var q = QuatD.AxisAngle(n.LengthSquared > 0 ? n.Normalized() : Vec3d.UnitY, -HaulFarBehind / Math.Max(1, sr.Length));
            vel = bv + q.Rotate(sv);
            return bp + q.Rotate(sr);
        }
        var radius = sys.Bodies[planet].Radius + d.Orbit;
        var (sin, cos) = Math.SinCos(run.Ship * 2.399963);
        vel = bv + new Vec3d(-sin, 0, cos) * Math.Sqrt(sys.Bodies[planet].Mu / radius);
        return bp + new Vec3d(radius * cos, 0, radius * sin);
    }

    /// <summary>
    /// Host, each second of a ledger leg between planets: the ship and its escorts posed on the route (their speed is the route's pace, so the
    /// flight carries them on between the seconds). False: the leg's time is up (or the ship is gone from the flight).
    /// </summary>
    private bool LedgerFarStep(SpaceFlight f, HaulRun run, Construct c, LedgerTrip trip, int def, int planet)
    {
        if (f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0))
            return false;
        var span = Math.Max(1, trip.ToTick - trip.FromTick);
        var tau = (ServerTick - trip.FromTick) / (double)span;
        var sys = f.System;
        var t = f.Ships.Time;
        var endAt = LedgerFarEnd(f, trip, run, StationMarkets.All[def], planet, out var endVel);
        if (tau >= 1)
        {
            Log(FormattableString.Invariant($"[haul] construct {c.Id} comes off its route at {StationMarkets.All[def].Name}, {(f.Ships.PositionOf(i) - f.Ships.BodyPosition(planet) - (endAt - sys.PositionOf(planet, t))).Length:0} m and {(f.Ships.VelocityOf(i) - endVel).Length:0.0} m/s off its place"));
            return false;
        }
        // The route is flown over the leg less its last part, which it spends at its place by the station (on its orbit there),
        // so it comes off the route there with nothing to make up.
        var along = tau / (1 - LedgerFarEnds);
        if (along >= 1)
        {
            LedgerPose(f, run, i, endAt, endVel, planet);
            return true;
        }
        var pos = LedgerFarAt(sys, trip, t, along, endAt);
        var ahead = LedgerFarAt(sys, trip, t + SpaceTime.FromSeconds(1).Ns, along + World.TickRate / (span * (1 - LedgerFarEnds)), endAt);
        LedgerPose(f, run, i, pos, ahead - pos); // (its speed the route's pace a second on: the flight carries it between the seconds)
        return true;
    }

    /// <summary>A ledger ship and its escorts posed at <paramref name="pos"/> with <paramref name="vel"/> (the ecliptic frame), each round whatever body holds it there; escorts behind it along its way.</summary>
    private void LedgerPose(SpaceFlight f, HaulRun run, int i, SpacePos pos, Vec3d vel, int round = -1)
    {
        // (round its primary as the system has it now: the flight's own copy of a body's place is drawn between steps and can lie a long way off it)
        var p = round >= 0 ? round : f.Ships.PrimaryAt(pos);
        f.System.StateOf(p, f.Ships.Time, out var bp, out var bv);
        f.Ships.SetState(i, p, pos - bp, vel - bv);
        var back = vel.LengthSquared > 1e-6 ? vel.Normalized() * -LedgerTrip.EscortGap : new Vec3d(-LedgerTrip.EscortGap, 0, 0);
        var n = 0;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: not 0 } ec && f.Ships.IndexOfId(ec.SpaceShip) is var ei and >= 0)
            {
                var at = pos + back * ++n;
                var ep = round >= 0 ? round : f.Ships.PrimaryAt(at);
                f.System.StateOf(ep, f.Ships.Time, out var ebp, out var ebv);
                f.Ships.SetState(ei, ep, at - ebp, vel - ebv);
            }
    }

    /// <summary>A ledger ship met part way between planets goes on at the transfer conic's own speed where it is, so it flies the rest for real.</summary>
    private void LedgerFarRealSpeed(SpaceFlight f, HaulRun run, Construct c, LedgerTrip trip)
    {
        if (f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0))
            return;
        var sys = f.System;
        var along = (ServerTick - trip.FromTick) / (double)Math.Max(1, trip.ToTick - trip.FromTick) / (1 - LedgerFarEnds);
        if (along >= 1)
            return; // (at its place by the station already, at its speed)
        along = Math.Max(0, along);
        var u = along * along * (3 - 2 * along);
        if (!UniversalKepler.Propagate(sys.Bodies[trip.Centre].Mu, trip.R1, trip.V1, u * trip.Tof, out _, out var v))
            return;
        f.Ships.RelativeOf(i, out var p, out var r, out _);
        var t = f.Ships.Time;
        sys.StateOf(p, t, out var bp, out _);
        sys.StateOf(trip.Centre, t, out _, out var cv);
        // The conic's speed mid-route, the planet's it left near the start and its place's by the station near the end (the route's own
        // braking: met close to the station, it doesn't go on past it at the transfer's arrival speed).
        sys.StateOf(trip.From, t, out _, out var av);
        LedgerFarEnd(f, trip, run, StationMarkets.All[trip.AtDef], trip.To, out var endVel);
        var mid = 4 * u * (1 - u);
        LedgerPose(f, run, i, bp + r, (cv + v) * mid + (av * (1 - u) + endVel * u) * (1 - mid), p);
    }

    /// <summary>
    /// Host, each second of a flown leg between planets (<c>HaulFarOut</c>: someone saw it set out): once it and its escorts are out of the
    /// bubble with nobody near, nor by its station, the rest of the leg goes on the real route at its pace (<see cref="LedgerFarPlan"/>),
    /// its fuel paid already. True: it is on the route now.
    /// </summary>
    private bool LedgerFarTake(SpaceSession s, HaulRun run, Construct c, int def, StationDef d, int planet)
    {
        if (LedgerAll || LedgerOff || run.RouteTried || run.LedgerUntil != 0 || planet < 0 || !OutOfBubble(run, c) || LedgerSeen(run, c)
            || Constructs.Get(StationBodyOf(def)) is { } to && LedgerSeesAt(to.WorldCom, LedgerRange + LedgerBehindSeen)
            || s.Flight is not { } f || f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0))
            return false;
        var left = (run.FarUntil - ServerTick) / (60.0 * World.TickRate);
        var from = f.Ships.PrimaryOf(i);
        if (left < 1 || from == planet)
            return false;
        if (LedgerFarPlan(s, run, c, def, from, planet, left) is not { } route)
        {
            run.RouteTried = true;
            return false;
        }
        if (NavOf(c.Id) is { } o)
            NavEnd(o, c, "it goes on the ledger: nobody is near");
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } ec && NavOf(ec.Id) is { } eo)
                NavEnd(eo, ec, "its hauler goes on the ledger");
        run.FarUntil = 0;
        run.Trip = route;
        run.Ledger = true;
        run.LedgerUntil = ServerTick + (long)(left * 60 * World.TickRate);
        run.RetryAt = 0;
        LedgerLegs++;
        HaulSay(run, c, FormattableString.Invariant($"goes on the ledger to {d.Name}: {route.Tof / 86400:0.0} days' transfer round {s.System.Bodies[route.Centre].Name}, the last {left:0} minutes of play"));
        return true;
    }

    /// <summary>The hauler and its escorts' drives held off their ships (on a ledger path) or let go again.</summary>
    private void LedgerCoasting(HaulRun run, Construct c, bool on)
    {
        c.LedgerCoast = on;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } ec)
                ec.LedgerCoast = on;
    }

    /// <summary>A change of speed to the hauler and its escorts (each escort's turned with its path).</summary>
    private void LedgerPush(SpaceFlight f, HaulRun run, int i, int planet, Vec3d dv, Vec3d axis)
    {
        f.Ships.RelativeOf(i, out _, out var r, out var v);
        f.Ships.SetState(i, planet, r, v + dv);
        var n = 0;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: not 0 } ec && f.Ships.IndexOfId(ec.SpaceShip) is var ei and >= 0)
            {
                var q = QuatD.AxisAngle(axis, -(LedgerTrip.EscortGap * ++n) / Math.Max(1, r.Length));
                f.Ships.RelativeOf(ei, out _, out var er, out var ev);
                f.Ships.SetState(ei, planet, er, ev + q.Rotate(dv));
            }
    }

    /// <summary>Braked: the hauler <c>HaulFarBehind</c> behind its station at its speed, its escorts behind it, where its path brought it within a few hundred metres.</summary>
    private void LedgerAlongside(SpaceFlight f, HaulRun run, Construct c, int i, int planet, Vec3d axis)
    {
        var h = StationMarkets.Haulers[run.Route];
        if (Constructs.Get(StationBodyOf(run.AtTo ? h.To : h.From)) is not { SpaceShip: not 0 } sb || f.Ships.IndexOfId(sb.SpaceShip) is not (var si and >= 0) || f.Ships.PrimaryOf(si) != planet)
            return;
        f.Ships.RelativeOf(si, out _, out var sr, out var sv);
        var q = QuatD.AxisAngle(axis, -HaulFarBehind / Math.Max(1, sr.Length));
        var (r, v) = (q.Rotate(sr), q.Rotate(sv));
        f.Ships.RelativeOf(i, out _, out var hr, out var hv);
        Log(FormattableString.Invariant($"[haul] construct {c.Id} brakes behind its station on the ledger, {(hr - r).Length:0} m and {(hv - v).Length:0.0} m/s off its place"));
        f.Ships.SetState(i, planet, r, v);
        var n = 0;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: not 0 } ec && f.Ships.IndexOfId(ec.SpaceShip) is var ei and >= 0)
            {
                var qe = QuatD.AxisAngle(axis, -(LedgerTrip.EscortGap * ++n) / Math.Max(1, r.Length));
                f.Ships.SetState(ei, planet, qe.Rotate(r), qe.Rotate(v));
            }
    }

    /// <summary>
    /// How far behind the station along its orbit (m) a hauler comes up when a person is by it: the least of 20 km, 30 km, ... (to a quarter turn)
    /// that every person is at least <see cref="LedgerRange"/> nearer or farther from the station than (so, by the triangle, that far from the hauler).
    /// </summary>
    private float LedgerOutOfSight(Vector3 station, double orbitRadius)
    {
        var n = InterestPoints(_ledgerPeople);
        var most = (float)Math.Max(LedgerBehindSeen, orbitRadius * Math.PI / 2);
        for (var at = LedgerBehindSeen; at < most; at += 10000)
        {
            var clear = true;
            for (var k = 0; k < n && clear; k++)
                clear = MathF.Abs(Vector3.Distance(_ledgerPeople[k], station) - at * 0.98f) >= LedgerRange; // (the chord a little short of the arc)
            if (clear)
                return at;
        }
        return most;
    }

    /// <summary>Is the escort's hauler on the ledger (it flies nothing then)?</summary>
    private bool EscortOnLedger(EscortRun e)
    {
        foreach (var run in _hauls)
            if (run.Ship == e.Hauler)
                return run.Ledger;
        return false;
    }

    /// <summary>
    /// The long check's fast clock (<c>--economy-sim --fast</c>): <paramref name="seconds"/> of play for the economy alone in one step, the
    /// world not stepped (no physics, crews, flights or space clock): the stations' needs and markets, every hauler on the ledger,
    /// consignments and camps, and the powers' relations and controllers in turns as short as a power's turn to think in play. Waits and legs end on the step after they are due,
    /// so a step longer than a few seconds stretches each stop by up to a step. Headless only.
    /// </summary>
    public void EconomyFastSecond(int seconds = 1)
    {
        LedgerAll = true;
        var think = Math.Max(1, (int)(Controllers.Rules.ThinkSeconds * World.TickRate) / Math.Max(1, CrewPowers.Count)); // (the powers' turn: each thinks as often as in play)
        for (var sec = 0; sec < Math.Max(1, seconds); sec++)
        {
            for (var t = 0; t < World.TickRate; t += think)
            {
                var ticks = Math.Min(think, World.TickRate - t);
                ServerTick += (uint)ticks; // (a uint: 2.2 years of play at 60 Hz, so the long check stops short of that)
                Relations.Tick += ticks - 1; // (each Step counts the last tick: the chunk's time for their thinks and far groups)
                CrewFactionsTick();
                Controllers.Tick += ticks - 1;
                CrewPowersTick();
            }
            StationMarketTick(); // (needs once a minute, consignments, camps, hardship: GameStationMarket.cs)
            if (Space != null)
                StationHaulTick(Space); // (every hauler's leg and trade, on the ledger)
        }
    }
}
