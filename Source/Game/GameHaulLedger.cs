using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Haulers on the ledger (Karl, 2026-11-22: far things as "a datapoint", so a world can run its economy for years). A station
/// hauler that no person is near flies no leg: it takes the leg's time on a schedule (<see cref="HaulLedgerMinutes"/>: the
/// phasing between its stations' circles round one planet, or a leg between planets in play time as <c>HaulFarOut</c> takes it),
/// burns its share of fuel, and at the leg's end is put beside its station (on the station's orbit a little behind it, its
/// escorts with it) and trades there as one on its circle does, with its own bins and tanks: the same <c>HaulTrade</c>. Its
/// escorts fly nothing while it is on the ledger. A person coming within <see cref="LedgerRange"/> of it ends the leg there and
/// then (it is put at its station), and from then on it flies its legs as before; with a person by the station it comes up
/// <see cref="LedgerBehindSeen"/> behind it, out of sight, and flies in to dock. <see cref="LedgerAll"/> (the long check's fast
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
            if (ServerTick >= run.LedgerUntil || !LedgerAll && LedgerSeen(run, c))
                LedgerArrive(s, run, c, def, d, planet);
            return true;
        }
        if (LedgerOff && !LedgerAll || run.PutAt >= 0 || run.FarUntil != 0 || planet < 0 || d.Orbit <= 0 || c.SpaceAway != 0 && c.SpaceAway - 1 != s.GalaxyIndex)
            return false;
        if (!LedgerAll && (!OutOfBubble(run, c) || LedgerSeen(run, c) || Constructs.Get(StationBodyOf(def)) is { } to && LedgerSeesAt(to.WorldCom, LedgerRange + LedgerBehindSeen)))
            return false; // (seen, or bound for a station someone is by: it flies there, and they see it come)
        var h = StationMarkets.Haulers[run.Route];
        var leaving = StationMarkets.All[run.AtTo ? h.From : h.To];
        var from = StationPlanet(s, leaving);
        var far = from >= 0 && from != planet;
        var minutes = HaulLedgerMinutes(s, from >= 0 ? from : planet, leaving.Orbit, planet, d.Orbit);
        var resumed = run.Adopted && c.MooredTo == 0; // (a loaded world's hauler out on a leg: half a leg more, its fuel spent already, as HaulFarOut)
        run.Adopted = false;
        if (resumed)
            minutes *= 0.5;
        if (c.MooredTo != 0)
            StationCastOff(c);
        if (NavOf(c.Id) is { } o)
            NavEnd(o, c, "it goes on the ledger: nobody is near");
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } ec && NavOf(ec.Id) is { } eo)
                NavEnd(eo, ec, "its hauler goes on the ledger");
        if (!resumed)
            c.Fuel = MathF.Max(0, c.Fuel - c.FuelCapacity() * (far ? HaulFarFuel : LedgerNearFuel));
        run.Ledger = true;
        run.LedgerUntil = ServerTick + (long)(minutes * 60 * World.TickRate);
        run.RetryAt = 0;
        LedgerLegs++;
        HaulSay(run, c, FormattableString.Invariant($"sets out for {d.Name} on the ledger: {minutes:0} minutes of play away"));
        RouteArrived(run.Route); // (set out: its supply line runs, as a flown leg's)
        return true;
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
        if (LedgerSeesAt(c.WorldCom, LedgerRange))
            return true;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } ec && LedgerSeesAt(ec.WorldCom, LedgerRange))
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
        // A person by the station: it comes up out of sight behind it and flies the rest (its Dock order: HaulStep), so nothing appears beside them.
        var stBody = StationBodyOf(def);
        var seen = !LedgerAll && Constructs.Get(stBody) is { } stc && LedgerSeesAt(stc.WorldCom, LedgerRange + LedgerBehindSeen);
        if (s.Flight is { } f && f.Ships.IndexOfId(c.SpaceShip) is var i and >= 0)
        {
            Vec3d r, v;
            var mu = s.System.Bodies[planet].Mu;
            var st = stBody;
            if (st != 0 && Constructs.Get(st) is { SpaceShip: not 0, SpaceAway: 0 } sb && f.Ships.IndexOfId(sb.SpaceShip) is var si and >= 0 && f.Ships.PrimaryOf(si) == planet)
            {
                f.Ships.RelativeOf(si, out _, out var sr, out var sv);
                var n = Vec3d.Cross(sr, sv);
                var q = QuatD.AxisAngle(n.LengthSquared > 0 ? n.Normalized() : Vec3d.UnitY, -(seen ? LedgerBehindSeen : HaulFarBehind) / Math.Max(1, sr.Length));
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
            run.RetryAt = 0;
            HaulSay(run, c, $"comes up behind {d.Name} off the ledger: someone is near it");
            return; // (still Going: its Dock order next second)
        }
        HaulSay(run, c, $"comes to {d.Name} on the ledger");
        run.Stage = HaulStage.Orbiting;
        run.Since = ServerTick;
        RouteArrived(run.Route);
        HaulTrade(run, c, StationMarkets.Haulers[run.Route], def, MarketOfDef(def));
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
    /// consignments and camps, and the powers' controllers once with that much time. Waits and legs end on the step after they are due,
    /// so a step longer than a few seconds stretches each stop by up to a step. Headless only.
    /// </summary>
    public void EconomyFastSecond(int seconds = 1)
    {
        LedgerAll = true;
        var ticks = Math.Max(1, seconds) * World.TickRate;
        ServerTick += (uint)ticks; // (a uint: 2.2 years of play at 60 Hz, so the long check stops short of that)
        StationMarketTick(); // (needs once a minute, consignments, camps, hardship: GameStationMarket.cs)
        if (Space != null)
            StationHaulTick(Space); // (every hauler's leg and trade, on the ledger)
        Controllers.Tick += ticks - 1; // (its Step counts the last tick: the step's time for its far groups and thinks)
        CrewPowersTick();
    }
}
