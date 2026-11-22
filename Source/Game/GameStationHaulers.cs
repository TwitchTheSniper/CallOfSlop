using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

/// <summary>
/// Stations in their worlds and the haulers between them (docs/sandbox/design/faction-controllers.md "Stations", phase S2; Karl,
/// 2026-11-18: "Haulers from other neutral factions can then send ships to buy goods and take them back to their station", a second
/// station "at a fixed place in another neutral faction's turf" over Glacier, "we will then start testing logistics runs with ships").
/// <para>
/// A station with a <c>"blueprint"</c> in Crew/Stations.json is built in its planet's world, on a circular orbit its <c>"orbit"</c> up,
/// the first time that world is played without it (Glacier Watch: the Militia's, over Glacier); it stays with that world from then on.
/// </para>
/// <para>
/// Each route in <c>"haulers"</c> keeps its <c>"count"</c> of real ships, crewed by its power: one sets out from whichever of its
/// stations is in the world being played (one a minute at most, so a lost hauler is replaced). A hauler bound for a station in this
/// world, out in its space, docks there with the command view's Dock order; one bound for a station in another world (or still out of
/// reach, parked far off) flies an Orbit order to that station's height round its planet, between planets on the real trip planner and
/// its own fuel, and trades there once it is on that circle. At each end it sells what it carries that the station buys, fills its
/// tanks, buys the leg's goods (paid from its power's stock), waits and sets off on the next leg. The stock moves between the markets
/// and the powers' books carry it. A hauler's route and leg are saved with its ship (Construct.HaulRoute), so a loaded world's haulers go on.
/// </para>
/// </summary>
public sealed partial class Game
{
    private enum HaulStage : byte { Going, Moored, Orbiting }

    private sealed class HaulRun
    {
        public ushort Ship;
        public int Route;
        public bool AtTo; // the leg it is on: false, to the route's first station ("from"); true, to its second ("to")
        public HaulStage Stage;
        public long Since, RetryAt, FuelRetry;
        public string Last = ""; // the last line logged for its leg (so a refusal repeated every try is logged once)
        public long EscortNext; // when a lost escort may be replaced
        public long FarUntil; // on a leg between planets: when it gets there (0: not on one; HaulFarOut)
        public int PutAt = -1; // a hauler of a route with neither station in this world, just built: the planet it is put at once it is in space (HaulPut)
        public long PutSince;
        public bool Adopted; // taken up from a loaded world (HaulAdopt): a leg between planets it was on is not paid for twice
    }

    /// <summary>An escort (phase 3: convoys): a warship of the route's power that holds beside its hauler with a Meet order, and off the station while the hauler docks.</summary>
    private sealed class EscortRun
    {
        public ushort Ship, Hauler; // (Hauler 0: its hauler was lost; it waits for the route's next)
        public int Route;
        public long RetryAt, DockSince;
        public bool NoBerth; // the station had no berth for it this stop: it waits off instead
        public string Last = "";
    }

    private readonly List<EscortRun> _escorts = new(8);

    /// <summary>How near its escorts must be before a hauler sets out on a leg (m), and how much longer than its wait it waits for them (s).</summary>
    private const float EscortGather = 5000, EscortGatherMost = 120;

    /// <summary>How far under or over the station's circle an escort waits while its hauler docks (m), and how near the station it keeps that circle whichever way it drifts (m).</summary>
    private const float EscortCircle = 1000, EscortOffStation = 3000;

    /// <summary>The least of a good a hauler loads (units): less is left for the next run.</summary>
    private const int HaulLeastLot = 50;

    private readonly List<HaulRun> _hauls = new(8);

    /// <summary>A hauler docks from this near its station (m); farther off it closes on a circle this much under or over the station's (m); a Dock order it gives up after this long (s).</summary>
    private const float HaulDockFrom = 6000, HaulPhaseBy = 8000, HaulDockMost = 600;
    private long[] _haulNext = []; // by route: when the next hauler may set out
    private bool _stationsBuilt; // this world's stations were looked for (StationsBuildTick)
    private bool _haulAdopt; // a world was loaded or landed in: take up its saved haulers (HaulAdopt)
    private long _stationsBuildAt;

    /// <summary>Host: the body of the station whose market is <paramref name="def"/>, in this world (0: none here).</summary>
    private ushort StationBodyOf(int def)
    {
        foreach (var m in StationMarketsHere.Values)
            if (m.Def == def && Constructs.Get(m.Body) is { Wreck: false, IsStation: true })
                return m.Body;
        return 0;
    }

    /// <summary>The body index of a station's planet in <paramref name="s"/>'s system (-1: not there).</summary>
    private static int StationPlanet(SpaceSession s, StationDef d) => d.Planet.Length > 0 ? s.System.IndexOf(d.Planet) : -1;

    /// <summary>
    /// How near a station's planet a ship with a player seated aboard comes into that planet's world (m from its centre): out to the
    /// station's orbit and 20 km past it, so they arrive in orbit beside it instead of only at the sky's edge (GameSpaceLanding.cs). 0: no station there.
    /// </summary>
    internal static double StationReach(Map m)
    {
        double reach = 0;
        foreach (var d in StationMarkets.All)
            if (d.Orbit > 0 && m.Planetoid is { } pl && d.Planet.Equals(m.Name, StringComparison.OrdinalIgnoreCase))
                reach = Math.Max(reach, pl.Radius + d.Orbit + 20_000);
        return reach;
    }

    /// <summary>
    /// Server: a station built in orbit, nobody's, going round on a circular orbit <paramref name="r"/> m from <paramref name="centre"/>,
    /// over <paramref name="up"/> with its ring level and its spindle down. Null: it didn't build (logged).
    /// </summary>
    private Construct? StationBuild(ConstructBuild.Blueprint bp, string name, Vector3 centre, Vector3 up, Vector3 fwd, float r, double mu)
    {
        var z = -fwd;
        var x = Vector3.Cross(up, z);
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        var c = ConstructBuild.Build(Constructs, bp, centre + up * r, rot, out var why);
        if (c == null)
        {
            Log($"[station] {(name.Length > 0 ? name : bp.Name)} didn't build in orbit: {why}");
            return null;
        }
        c.Rebuild();
        c.Owner = 255; // (nobody's)
        c.StationTitle = name;
        var speed = (float)Math.Sqrt(mu / r);
        c.Vel = fwd * speed;
        c.PrevPos = c.Pos;
        c.Wake();
        StationMarketOf(c);
        return c;
    }

    /// <summary>
    /// Host, once a second, a few seconds into a world: a station listed with a blueprint for this world's planet that isn't here is built,
    /// its orbit up over the map's middle (Glacier Watch over Glacier). Once a world.
    /// </summary>
    private void StationsBuildTick()
    {
        if (_stationsBuilt || ServerTick < _stationsBuildAt || !SandboxOn)
            return;
        _stationsBuilt = true;
        if (World.Map.Planetoid is not { } pl || GravityFrames.Point is not { } planet || !StarSystems.ForMap(World.Map.Name, out var e, out var body))
            return;
        var all = StationMarkets.All;
        for (var d = 0; d < all.Length; d++)
        {
            var def = all[d];
            if (def.Blueprint.Length == 0 || d >= StationMarkets.Listed || def.State == MarketState.Closed || !def.Planet.Equals(e.System.Bodies[body].Name, StringComparison.OrdinalIgnoreCase))
                continue; // (one made in play: StationSiteTick; a dismantled one isn't put up again)
            foreach (var c in Constructs.Bodies)
                if (c.IsStation && !c.Wreck)
                    StationMarketOf(c); // (a station this world kept has its market already)
            if (StationBodyOf(d) != 0)
                continue;
            if (ConstructBuild.FindBlueprint(def.Blueprint) is not { } bp)
            {
                Log($"[station] {def.Name}: no blueprint {def.Blueprint}");
                continue;
            }
            var up = -planet.Center;
            up = up.LengthSquared() > 1 ? Vector3.Normalize(up) : Vector3.UnitY;
            var side = Vector3.Cross(up, Vector3.UnitY);
            if (side.LengthSquared() < 1e-4f)
                side = Vector3.Cross(up, Vector3.UnitX);
            var r = (float)pl.Radius + def.Orbit;
            if (StationBuild(bp, def.Name, planet.Center, up, Vector3.Normalize(side), r, e.System.Bodies[body].Mu) is { } st)
                Log($"[station] {def.Name} ({def.Blueprint}) built as construct {st.Id}, {def.Orbit / 1000:0.0} km up over {World.Map.Name}'s middle, held by {def.Power}");
        }
    }

    /// <summary>
    /// Server: a hauler for route <paramref name="ri"/> beside station <paramref name="st"/>, a kilometre and a half (and more for each
    /// one out already) behind it on its orbit, going round with it, nobody's and crewed by the route's power. Null: none was built.
    /// </summary>
    private HaulRun? HaulerOut(int ri, Construct st, bool atTo)
    {
        var h = StationMarkets.Haulers[ri];
        var power = CrewPowers.Find(h.Power);
        if (power == 0 || ConstructBuild.FindBlueprint(h.Ship) is not { } bp || GravityFrames.Point is not { } planet)
        {
            Log($"[haul] route {ri} ({h.Power}): {(power == 0 ? "no such power" : "no blueprint " + h.Ship)}");
            return null;
        }
        if (st.Dirty)
            st.Rebuild();
        var up = Vector3.Normalize(st.WorldCom - planet.Center);
        var fwd = st.Vel.LengthSquared() > 1 ? Vector3.Normalize(st.Vel - up * Vector3.Dot(st.Vel, up)) : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));
        var z = -fwd;
        var x = Vector3.Cross(up, z);
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        var c = ConstructBuild.Build(Constructs, bp, st.WorldCom - fwd * (1500f + 900f * _hauls.Count), rot, out var why);
        if (c == null)
        {
            Log($"[haul] a {h.Ship} for {h.Power} didn't build in orbit: {why}");
            return null;
        }
        c.Rebuild();
        c.Owner = 255; // (nobody's: the power's)
        c.Vel = st.Vel;
        c.PrevPos = c.Pos;
        c.Wake();
        var crew = Crew.Crew(c, ShipAiMode.Strangers, faction: power);
        if (Crew.Events.Count > 0)
            CrewLog();
        c.HaulRoute = (sbyte)ri;
        c.HaulAtTo = atTo;
        var r = new HaulRun { Ship = c.Id, Route = ri, AtTo = atTo, RetryAt = ServerTick + 5 * World.TickRate };
        _hauls.Add(r);
        RouteSeen(r.Route); // (its supply line runs: GameSupplyLines.cs)
        Log($"[haul] construct {c.Id}, a {h.Ship} crewed by {h.Power} ({(crew != null ? Crew.Alive(crew) : 0)} aboard), sets out from {StationName(st)} on the run between {StationMarkets.All[h.From].Name} and {StationMarkets.All[h.To].Name}");
        EscortsAdopt(r); // (a lost hauler's escorts first)
        for (var k = EscortsOf(r); k < h.Escorts; k++)
            if (EscortOut(r, c, k) == null)
                break;
        return r;
    }

    /// <summary>Host, once after a world is loaded or landed in: its saved haulers (Construct.HaulRoute) go on with their legs, so none is lost track of and no extra one sets out.</summary>
    private void HaulAdopt(int routes)
    {
        _haulAdopt = false;
        foreach (var c in Constructs.Bodies)
        {
            if (c.HaulRoute < 0 || c.PartCount == 0 || c.Wreck)
                continue;
            if (c.HaulRoute >= routes)
            {
                c.HaulRoute = -1; // (a route the data no longer has: a plain crewed ship now)
                c.HaulEscort = false;
                continue;
            }
            if (c.HaulEscort)
            {
                var had = false;
                foreach (var e in _escorts)
                    had |= e.Ship == c.Id;
                if (!had)
                {
                    _escorts.Add(new EscortRun { Ship = c.Id, Route = c.HaulRoute }); // (its hauler is found again: EscortStep)
                    Log($"[haul] construct {c.Id} goes on escorting on route {c.HaulRoute}");
                }
                continue;
            }
            var known = false;
            foreach (var run in _hauls)
                known |= run.Ship == c.Id;
            if (known)
                continue;
            _hauls.Add(new HaulRun { Ship = c.Id, Route = c.HaulRoute, AtTo = c.HaulAtTo, RetryAt = ServerTick + 5 * World.TickRate, EscortNext = ServerTick + 60 * World.TickRate, Adopted = true });
            RouteSeen(c.HaulRoute);
            Log($"[haul] construct {c.Id} goes on hauling (route {c.HaulRoute}, bound for {StationMarkets.All[c.HaulAtTo ? StationMarkets.Haulers[c.HaulRoute].To : StationMarkets.Haulers[c.HaulRoute].From].Name})");
        }
    }

    /// <summary>The route a hauler runs and the station it is bound for, for the command view's card (null: it is no hauler).</summary>
    public string? HaulLine(ushort ship)
    {
        foreach (var run in _hauls)
            if (run.Ship == ship)
            {
                var h = StationMarkets.Haulers[run.Route];
                var to = StationMarkets.All[run.AtTo ? h.To : h.From].Name.ToUpperInvariant();
                return run.Stage == HaulStage.Going ? "HAULING TO " + to : "TRADING AT " + to;
            }
        foreach (var e in _escorts)
            if (e.Ship == ship)
                return e.Hauler != 0 ? $"ESCORTING HAULER {e.Hauler}" : "ESCORT: WAITING FOR A HAULER";
        return null;
    }

    /// <summary>Host, once a second while space is up: the stations this world builds, each route's haulers, and each hauler's leg and trade.</summary>
    private void StationHaulTick(SpaceSession s)
    {
        StationsBuildTick();
        var routes = StationMarkets.Haulers;
        if (routes.Length == 0)
            return;
        if (_haulNext.Length != routes.Length)
        {
            _haulNext = new long[routes.Length];
            Array.Fill(_haulNext, ServerTick + 10 * World.TickRate); // (the first set out once the world has settled)
        }
        if (_haulAdopt)
        {
            HaulAdopt(routes.Length);
            foreach (var run in _hauls)
                EscortsAdopt(run); // (a loaded world's escorts with its haulers, before any is replaced)
        }
        for (var i = _hauls.Count - 1; i >= 0; i--)
            if (Constructs.Get(_hauls[i].Ship) is not { Wreck: false, PartCount: > 0 })
            {
                Log($"[haul] construct {_hauls[i].Ship} is gone: another sets out in a minute");
                _haulNext[_hauls[i].Route] = Math.Max(_haulNext[_hauls[i].Route], ServerTick + 60 * World.TickRate);
                _hauls.RemoveAt(i);
                StationsVersion++; // (a supply line may be cut: the cards hear it, GameSupplyLines.cs)
            }
            else if (routes[_hauls[i].Route].Count == 0 && _hauls[i].Stage != HaulStage.Going)
                HaulRetire(i); // (its route stopped: it goes off it at a station, GameStationLife.cs)
        for (var ri = 0; ri < routes.Length; ri++)
        {
            if (ServerTick < _haulNext[ri])
                continue;
            var live = 0;
            foreach (var run in _hauls)
                if (run.Route == ri)
                    live++;
            if (live >= routes[ri].Count)
                continue;
            _haulNext[ri] = ServerTick + 60 * World.TickRate;
            var from = StationBodyOf(routes[ri].From);
            var to = StationBodyOf(routes[ri].To);
            if (Constructs.Get(from != 0 ? from : to) is { SpaceAway: 0, SpaceShip: not 0 } st) // (a station here, in this world's space)
                HaulerOut(ri, st, from == 0);
            else if (from == 0 && to == 0 && StationPlanet(s, StationMarkets.All[routes[ri].From]) is var fp and >= 0 && AnyStationHere() is { } any
                && HaulerOut(ri, any, false) is { } far)
                (far.PutAt, far.PutSince) = (fp, ServerTick); // (neither end here: it runs out of sight, from its first station: HaulPut)
        }
        for (var i = _escorts.Count - 1; i >= 0; i--)
            if (Constructs.Get(_escorts[i].Ship) is not { Wreck: false, PartCount: > 0 })
            {
                Log($"[haul] escort {_escorts[i].Ship} is gone");
                foreach (var run in _hauls)
                    if (run.Ship == _escorts[i].Hauler)
                        run.EscortNext = Math.Max(run.EscortNext, ServerTick + 60 * World.TickRate);
                _escorts.RemoveAt(i);
            }
        foreach (var run in _hauls)
            if (Constructs.Get(run.Ship) is { } c)
            {
                HaulStep(s, run, c);
                // A lost escort is replaced at the hauler's next station in this world, a minute on at least.
                if (run.Stage == HaulStage.Moored && ServerTick >= run.EscortNext && EscortsOf(run) < routes[run.Route].Escorts)
                {
                    run.EscortNext = ServerTick + 60 * World.TickRate;
                    EscortsAdopt(run);
                    if (EscortsOf(run) < routes[run.Route].Escorts)
                        EscortOut(run, c, EscortsOf(run));
                }
            }
        foreach (var e in _escorts)
            if (Constructs.Get(e.Ship) is { } c)
                EscortStep(s, e, c);
    }

    /// <summary>Escorts on <paramref name="run"/>'s route with no hauler (theirs was lost, or a loaded world's) join it, up to the route's count.</summary>
    private void EscortsAdopt(HaulRun run)
    {
        var most = StationMarkets.Haulers[run.Route].Escorts;
        foreach (var e in _escorts)
        {
            if (EscortsOf(run) >= most)
                return;
            if (e.Route != run.Route || e.Hauler == run.Ship)
                continue;
            var has = false;
            foreach (var r in _hauls)
                has |= r.Ship == e.Hauler;
            if (has)
                continue;
            e.Hauler = run.Ship;
            e.RetryAt = 0;
            Log($"[haul] construct {e.Ship} now escorts hauler {run.Ship}");
        }
    }

    /// <summary>How many escorts fly with <paramref name="run"/>'s hauler now.</summary>
    private int EscortsOf(HaulRun run)
    {
        var n = 0;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship)
                n++;
        return n;
    }

    /// <summary>
    /// Server: an escort for <paramref name="run"/>'s hauler <paramref name="hauler"/>, built off its side (and further for each one it has),
    /// going round with it, nobody's and crewed by the route's power. Null: none was built (logged).
    /// </summary>
    private Construct? EscortOut(HaulRun run, Construct hauler, int k)
    {
        var h = StationMarkets.Haulers[run.Route];
        var power = CrewPowers.Find(h.Power);
        if (power == 0 || h.Escort.Length == 0 || ConstructBuild.FindBlueprint(h.Escort) is not { } bp || GravityFrames.Point is not { } planet)
        {
            if (h.Escort.Length > 0)
                Log($"[haul] route {run.Route} ({h.Power}): {(power == 0 ? "no such power" : "no blueprint " + h.Escort)} for its escort");
            return null;
        }
        if (hauler.Dirty)
            hauler.Rebuild();
        var up = Vector3.Normalize(hauler.WorldCom - planet.Center);
        var fwd = hauler.Vel.LengthSquared() > 1 ? Vector3.Normalize(hauler.Vel - up * Vector3.Dot(hauler.Vel, up)) : Vector3.Normalize(Vector3.Cross(up, Vector3.UnitX));
        var z = -fwd;
        var x = Vector3.Cross(up, z);
        var rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, up.X, up.Y, up.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
        // Off its side across the orbit (and up, clear of a station it is moored at), so it doesn't build inside either.
        var at = hauler.WorldCom + x * ((k % 2 == 0 ? 1 : -1) * (hauler.Radius + 200f + h.EscortGap + 250f * (k / 2))) + up * 300f;
        var c = ConstructBuild.Build(Constructs, bp, at, rot, out var why);
        if (c == null)
        {
            Log($"[haul] an escort {h.Escort} for {h.Power} didn't build in orbit: {why}");
            return null;
        }
        c.Rebuild();
        c.Owner = 255; // (nobody's: the power's)
        c.Vel = hauler.Vel;
        c.PrevPos = c.Pos;
        c.Wake();
        var crew = Crew.Crew(c, ShipAiMode.Strangers, faction: power);
        if (Crew.Events.Count > 0)
            CrewLog();
        c.HaulRoute = (sbyte)run.Route;
        c.HaulEscort = true;
        _escorts.Add(new EscortRun { Ship = c.Id, Hauler = run.Ship, Route = run.Route, RetryAt = ServerTick + 3 * World.TickRate });
        Log($"[haul] construct {c.Id}, a {h.Escort} crewed by {h.Power} ({(crew != null ? Crew.Alive(crew) : 0)} aboard), escorts hauler {run.Ship}");
        return c;
    }

    /// <summary>
    /// Host: one escort's second. With its hauler out on a leg it holds beside it (a Meet order: between worlds too, on the trip planner);
    /// while the hauler closes on, docks at or lies moored at a station in this world, it holds off that station instead, clear of the
    /// berths. One that lost its hauler joins the route's hauler with the fewest escorts.
    /// </summary>
    private void EscortStep(SpaceSession s, EscortRun e, Construct c)
    {
        var h = StationMarkets.Haulers[e.Route];
        HaulRun? run = null;
        foreach (var r in _hauls)
            if (r.Ship == e.Hauler)
                run = r;
        if (run == null)
        {
            e.Hauler = 0;
            var fewest = int.MaxValue;
            foreach (var r in _hauls)
                if (r.Route == e.Route && EscortsOf(r) < Math.Min(fewest, h.Escorts))
                    (run, fewest) = (r, EscortsOf(r));
            if (run == null)
                return; // (it keeps its last order until the route's next hauler sets out)
            e.Hauler = run.Ship;
            Log($"[haul] construct {c.Id} now escorts hauler {run.Ship}");
        }
        if (ServerTick < e.RetryAt || Constructs.Get(run.Ship) is not { } hauler)
            return;
        var d = StationMarkets.All[run.AtTo ? h.To : h.From];
        var st = StationBodyOf(run.AtTo ? h.To : h.From);
        var o = NavOf(c.Id);
        string? why;
        string line;
        var docking = st != 0 && Constructs.Get(st) is { SpaceAway: 0, SpaceShip: not 0 } stb && hauler.SpaceAway == 0
            && (hauler.MooredTo == st || Vector3.DistanceSquared(stb.WorldCom, hauler.WorldCom) < HaulDockFrom * HaulDockFrom);
        var resting = hauler.SpaceAway == 0 && NavOf(hauler.Id) is { Kind: NavKind.Orbit, Phase: NavPhase.Orbiting or NavPhase.Short } ho && ho.Body == s.Bubble.Body; // (on a circle, or waiting there for a trip in reach)
        // With its hauler moored, it docks too where the station has a berth it fits (Karl, 2026-11-21: "make sure NPC frigates can dock and
        // supply the stations"; Caledon Highport's frigate collar, Hexkey Orbital's pier), and waits off the station only where there is none.
        if (docking && hauler.MooredTo == st && c.SpaceAway == 0 && !e.NoBerth)
        {
            if (c.MooredTo == st)
                return; // (moored beside it)
            if (o is { Kind: NavKind.Dock } && o.Target == st && ServerTick - e.DockSince < HaulDockMost * World.TickRate)
                return; // (on its way in)
            if (c.Dirty)
                c.Rebuild();
            why = NavApply(s, c, NavKind.Dock, 0, Vec3d.Zero, 0, 255, h.Power + " escort", null, out _, st);
            if (why == null)
            {
                e.DockSince = ServerTick;
                e.RetryAt = ServerTick + 10 * World.TickRate;
                line = $"docks at {d.Name} beside hauler {hauler.Id}";
                if (line != e.Last)
                {
                    e.Last = line;
                    Log($"[haul] escort {c.Id} {line}");
                }
                return;
            }
            e.NoBerth = true;
            Log($"[haul] escort {c.Id} can't dock at {d.Name} ({why}): it waits off");
        }
        if (!docking)
            e.NoBerth = false; // (a berth is looked for again at the next stop)
        if (c.MooredTo != 0 && !(docking && hauler.MooredTo == st))
            StationCastOff(c); // (its hauler has cast off: so does it)
        if ((docking || resting) && c.SpaceAway == 0 && GravityFrames.Point is { } gp && Constructs.Get(docking ? st : hauler.Id) is { } sb)
        {
            // While the hauler docks (or rests on a circle), off the station (or it) on a circle of its own a kilometre under or over its (under when the station
            // is ahead, so it gains on it; over when behind), its drive at rest: held beside a point by the station on a single drive it
            // turned about all the while (2026-11-21), and beside the hauler it would hold inside the station's ring.
            var stR = (sb.WorldCom - gp.Center).Length();
            var off = sb.WorldCom - c.WorldCom;
            var near = off.LengthSquared() < EscortOffStation * EscortOffStation;
            var radius = stR + (Vector3.Dot(off, c.Vel) > 0 ? -1 : 1) * EscortCircle;
            if (o is { Kind: NavKind.Orbit } && o.Body == s.Bubble.Body && (near || Math.Abs(o.Radius - radius) < EscortCircle))
                return; // (on its circle; far off, it changes circle only when the station has passed it)
            if (c.Dirty)
                c.Rebuild();
            why = NavApply(s, c, NavKind.Orbit, s.Bubble.Body, Vec3d.Zero, radius, 255, h.Power + " escort", null, out _);
            var what = docking ? d.Name : "hauler " + hauler.Id;
            line = why != null ? $"can't wait off {what}: {why}" : FormattableString.Invariant($"waits off {what} on a circle {(radius - stR) / 1000:+0.0;-0.0} km from its{(docking ? $" while hauler {hauler.Id} docks" : "")}");
        }
        else
        {
            if (o is { Kind: NavKind.Meet } && o.Target == hauler.Id)
                return; // (with it)
            if (c.Dirty)
                c.Rebuild();
            why = NavApply(s, c, NavKind.Meet, 0, Vec3d.Zero, h.EscortGap, 255, h.Power + " escort", null, out _, hauler.Id);
            line = why != null ? $"can't hold with hauler {hauler.Id}: {why}" : $"flies with hauler {hauler.Id}";
        }
        e.RetryAt = ServerTick + 10 * World.TickRate;
        if (line != e.Last)
        {
            e.Last = line;
            Log($"[haul] escort {c.Id} {line}");
        }
    }

    /// <summary>Is every escort of <paramref name="run"/> within <see cref="EscortGather"/> of its hauler (a convoy waits for them before a leg)?</summary>
    private bool EscortsGathered(HaulRun run, Construct hauler)
    {
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } c && (c.SpaceAway != hauler.SpaceAway
                || c.SpaceAway == 0 && Vector3.DistanceSquared(c.WorldCom, hauler.WorldCom) > EscortGather * EscortGather))
                return false;
        return true;
    }

    /// <summary>Host: one hauler's second: moored or on its station's circle, it trades and waits; else it is given its leg's order.</summary>
    private void HaulStep(SpaceSession s, HaulRun run, Construct c)
    {
        var h = StationMarkets.Haulers[run.Route];
        var def = run.AtTo ? h.To : h.From;
        var d = StationMarkets.All[def];
        var st = StationBodyOf(def);
        var o = NavOf(c.Id);
        var planet = StationPlanet(s, d);
        var here = st != 0 && d.State != MarketState.Building && Constructs.Get(st) is { SpaceAway: 0, SpaceShip: not 0 }; // (the station is a body in this world's space; a site has no berth: its builders trade on its circle)
        var circling = !here && o is { Kind: NavKind.Orbit, Phase: NavPhase.Orbiting } && o.Body == planet;
        if (run.Stage == HaulStage.Going && (st != 0 && c.MooredTo == st || circling))
        {
            run.Stage = circling ? HaulStage.Orbiting : HaulStage.Moored;
            run.Since = ServerTick;
            RouteArrived(run.Route); // (its supply line runs: GameSupplyLines.cs)
            HaulTrade(run, c, h, def, st != 0 && c.MooredTo == st ? StationMarketOf(Constructs.Get(st)!) : MarketOfDef(def));
            return;
        }
        if (run.Stage != HaulStage.Going)
        {
            if (ServerTick - run.Since < (long)(h.Wait * World.TickRate))
                return;
            if (ServerTick - run.Since < (long)((h.Wait + AshoreMost + OffFor) * World.TickRate) && StoreCrewAshore(c.Id))
            {
                HaulSay(run, c, "waits for its crew shopping aboard the store");
                return; // (its shoppers come back aboard first: GameStoreLife.cs)
            }
            if (ServerTick - run.Since < (long)((h.Wait + EscortGatherMost) * World.TickRate) && !EscortsGathered(run, c))
            {
                HaulSay(run, c, "waits for its escorts");
                return; // (a convoy sets out together)
            }
            if (c.Fuel < c.FuelCapacity() * HaulFuelLeast)
            {
                // (fuel isn't free: a hauler with too little for a leg waits at the station for its hydrogen, rather than set out and drift dry)
                if (ServerTick >= run.FuelRetry)
                {
                    run.FuelRetry = ServerTick + 30 * World.TickRate;
                    HaulFuel(c, MarketOfDef(def), Controllers.Powers[CrewPowers.Find(h.Power)], h.Power, d.Name, "construct", quiet: true);
                }
                if (c.Fuel < c.FuelCapacity() * HaulFuelLeast)
                {
                    HaulSay(run, c, "waits for fuel: its station has no hydrogen to spare"); // (a literal: said each second it waits, logged once)
                    return;
                }
            }
            run.AtTo = !run.AtTo; // (the next leg)
            c.HaulAtTo = run.AtTo;
            run.Stage = HaulStage.Going;
            run.RetryAt = 0;
            HaulStep(s, run, c);
            return;
        }
        if (run.PutAt >= 0)
        {
            HaulPut(s, run, c);
            return;
        }
        if (run.FarUntil != 0)
        {
            if (ServerTick >= run.FarUntil)
                HaulFarArrive(s, run, c, def, d, planet, st, here);
            return;
        }
        if (ServerTick < run.RetryAt)
            return;
        if (HaulFarOut(s, run, c, d, planet))
            return;
        // Its order for this leg. A station here with the hauler out in this world's space: a circle a little under or over the station's
        // (under when the station is ahead of it, so it gains on it; over when behind), which costs little fuel and brings it up beside it
        // as time goes round, then the Dock order from a few kilometres off (2026-11-18: a Dock order flown straight across 40 km of
        // orbit burned a Magpie's whole tank). Else the circle round the station's planet at its height.
        var dock = here && c.SpaceAway == 0 && c.SpaceShip != 0;
        var radius = planet >= 0 ? s.System.Bodies[planet].Radius + d.Orbit : 0;
        var phasing = false;
        if (dock && Constructs.Get(st) is { } sb && GravityFrames.Point is { } gp)
        {
            if (sb.Dirty)
                sb.Rebuild();
            var gap = sb.WorldCom - c.WorldCom;
            var along = c.Vel.LengthSquared() > 1 ? MathF.Abs(Vector3.Dot(gap, Vector3.Normalize(c.Vel))) : gap.Length();
            if (gap.Length() > HaulDockFrom && (along > HaulDockFrom || gap.Length() > HaulDockFrom + HaulPhaseBy * 1.5f)) // (along its way: on a circle under or over, it passes the station a circle's step off, so it docks from there, 2026-11-22: it swapped circles for ever)
            {
                var stR = (sb.WorldCom - gp.Center).Length();
                var ahead = Vector3.Dot(gap, c.Vel) > 0;
                radius = stR + (ahead ? -HaulPhaseBy : HaulPhaseBy);
                planet = s.Bubble.Body;
                phasing = true;
                dock = false;
            }
        }
        if (dock ? o is { Kind: NavKind.Dock } && o.Target == st && ServerTick - run.Since < HaulDockMost * World.TickRate
            : o is { Kind: NavKind.Orbit } && o.Body == planet && o.Phase != NavPhase.Short && (!phasing || Math.Abs(o.Radius - radius) < HaulPhaseBy * 2))
            return; // (on its way)
        if (dock && o is { Kind: NavKind.Dock })
            NavEnd(o, c, "it didn't dock in time: it tries again"); // (a Dock order that stalls beside the port)
        if (!dock && (planet < 0 || d.Orbit <= 0))
        {
            HaulSay(run, c, $"{d.Name} is in another world and its planet or orbit isn't known: it waits");
            run.RetryAt = ServerTick + 60 * World.TickRate;
            return;
        }
        if (c.Dirty)
            c.Rebuild();
        if (c.MooredTo != 0)
            StationCastOff(c); // (an order isn't taken by a moored ship: NavDockCheck)
        if (dock)
            run.Since = ServerTick; // (when its Dock order was given)
        var why = dock ? NavApply(s, c, NavKind.Dock, 0, Vec3d.Zero, 0, 255, h.Power + " hauler", null, out _, st)
            : NavApply(s, c, NavKind.Orbit, planet, Vec3d.Zero, radius, 255, h.Power + " hauler", null, out _);
        run.RetryAt = ServerTick + 30 * World.TickRate; // (an order that ends short of its station is given again after this)
        HaulSay(run, c, why != null ? $"can't set out for {d.Name}: {why}"
            : dock ? $"docks at {d.Name}"
            : phasing ? FormattableString.Invariant($"closes on {d.Name} on a circle {(radius - s.System.Bodies[planet].Radius) / 1000:0} km up")
            : FormattableString.Invariant($"sets out for {d.Name}, {d.Orbit / 1000:0} km up round {d.Planet}"));
    }

    /// <summary>A leg between planets takes this much play, more the farther apart they are now (minutes; Karl, 2026-11-22: "far legs in play time").</summary>
    private const double HaulFarLeast = 20, HaulFarMost = 40;

    /// <summary>The share of a full tank a leg between planets burns.</summary>
    private const float HaulFarFuel = 0.3f;

    /// <summary>A hauler come to a station in this world from another planet is put this far behind it on its orbit (m).</summary>
    private const double HaulFarBehind = 4000;

    /// <summary>
    /// Host: a leg to a station round another planet (Karl, 2026-11-22: "far legs in play time"). Station needs run on hours of play,
    /// and a real trip between planets takes a year or more of space time, so at the normal time speed nothing would arrive: the hauler
    /// sets out (round the planet being played, it climbs away on a circle a little over its station's), burns <see cref="HaulFarFuel"/>
    /// of its tank and gets there after <see cref="HaulFarMinutes"/> of play, whatever the time speed (<see cref="HaulFarArrive"/>).
    /// A leg round the same planet is flown for real. False: this leg is not between planets.
    /// </summary>
    private bool HaulFarOut(SpaceSession s, HaulRun run, Construct c, StationDef d, int planet)
    {
        if (planet < 0 || d.Orbit <= 0 || c.SpaceShip == 0 || c.SpaceAway != 0 && c.SpaceAway - 1 != s.GalaxyIndex || s.Flight is not { } f || f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0))
            return false; // (in this system's flight: here, or parked far out in it, GameSpaceJump.cs)
        var at = f.Ships.PrimaryOf(i);
        if (at == planet)
            return false;
        var minutes = HaulFarMinutes(s.System, at, planet, f.Ships.Time);
        var resumed = run.Adopted && c.MooredTo == 0; // (a loaded world's hauler out on a leg: half a leg more, its fuel spent already; the leg's own time isn't saved)
        run.Adopted = false;
        if (resumed)
            minutes *= 0.5;
        if (c.MooredTo != 0)
            StationCastOff(c);
        if (NavOf(c.Id) is { } o)
            NavEnd(o, c, "it sets out for another planet");
        if (at == s.Bubble.Body)
        {
            f.Ships.RelativeOf(i, out _, out var r, out _);
            NavApply(s, c, NavKind.Orbit, at, Vec3d.Zero, r.Length + HaulPhaseBy * 2, 255, StationMarkets.Haulers[run.Route].Power + " hauler", null, out _); // (seen leaving: it climbs away)
        }
        if (!resumed)
            c.Fuel = MathF.Max(0, c.Fuel - c.FuelCapacity() * HaulFarFuel);
        run.FarUntil = ServerTick + (long)(minutes * 60 * World.TickRate);
        run.RetryAt = 0;
        HaulSay(run, c, FormattableString.Invariant($"sets out for {d.Name} round {d.Planet}: {minutes:0} minutes of play away"));
        RouteArrived(run.Route); // (set out: its supply line runs)
        return true;
    }

    /// <summary>Host: a station body in this world's space (where a hauler of a route with neither end here is built), or null.</summary>
    private Construct? AnyStationHere()
    {
        foreach (var m in StationMarketsHere.Values)
            if (Constructs.Get(m.Body) is { Wreck: false, IsStation: true, SpaceAway: 0, SpaceShip: not 0 } c)
                return c;
        return null;
    }

    /// <summary>
    /// Host: a hauler of a route with neither station in this world (Slopolis Exchange to Hexkey Orbital while Caledon is played), built
    /// beside a station here: once it and its escorts are in space (or after a minute) they are put on its first station's circle,
    /// out of sight, and it trades there as a hauler come from another planet does (<see cref="HaulFarArrive"/>).
    /// </summary>
    private void HaulPut(SpaceSession s, HaulRun run, Construct c)
    {
        var ready = c.SpaceShip != 0;
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { SpaceShip: 0 })
                ready = false;
        if (!ready && (c.SpaceShip == 0 || ServerTick - run.PutSince < 60 * World.TickRate))
            return;
        var planet = run.PutAt;
        run.PutAt = -1;
        run.AtTo = false;
        c.HaulAtTo = false;
        run.Stage = HaulStage.Going;
        var def = StationMarkets.Haulers[run.Route].From;
        HaulFarArrive(s, run, c, def, StationMarkets.All[def], planet, 0, here: false);
    }

    /// <summary>Minutes of play a leg from body <paramref name="from"/> to <paramref name="to"/> takes, by how far apart they are now against the system's widest span.</summary>
    private static double HaulFarMinutes(StarSystem sys, int from, int to, SpaceTime t)
    {
        double far = 0;
        var sun = sys.PositionOf(0, t);
        for (var b = 1; b < sys.Bodies.Length; b++)
            far = Math.Max(far, (sys.PositionOf(b, t) - sun).Length);
        var gap = (sys.PositionOf(to, t) - sys.PositionOf(from, t)).Length;
        return far > 0 ? HaulFarLeast + (HaulFarMost - HaulFarLeast) * Math.Clamp(gap / (2 * far), 0, 1) : HaulFarLeast;
    }

    /// <summary>
    /// Host: a hauler's leg between planets is over. Its station in this world: it is put on the station's orbit a little behind it, and
    /// docks as a hauler round the same planet does. Its station round another planet: it is put on that station's circle and trades
    /// there, as one circling it does. Its escorts come with it.
    /// </summary>
    private void HaulFarArrive(SpaceSession s, HaulRun run, Construct c, int def, StationDef d, int planet, ushort st, bool here)
    {
        run.FarUntil = 0;
        if (s.Flight is not { } f || f.Ships.IndexOfId(c.SpaceShip) is not (var i and >= 0))
            return;
        if (NavOf(c.Id) is { } o)
            NavEnd(o, c, $"it has come to {d.Planet}");
        Vec3d r, v;
        var mu = s.System.Bodies[planet].Mu;
        if (here && Constructs.Get(st) is { SpaceShip: not 0 } sb && f.Ships.IndexOfId(sb.SpaceShip) is var si and >= 0 && f.Ships.PrimaryOf(si) == planet)
        {
            f.Ships.RelativeOf(si, out _, out var sr, out var sv);
            var n = Vec3d.Cross(sr, sv);
            var q = QuatD.AxisAngle(n.LengthSquared > 0 ? n.Normalized() : Vec3d.UnitY, -HaulFarBehind / Math.Max(1, sr.Length));
            (r, v) = (q.Rotate(sr), q.Rotate(sv));
        }
        else
        {
            var radius = s.System.Bodies[planet].Radius + d.Orbit;
            var (sin, cos) = Math.SinCos(run.Ship * 2.399963); // (each hauler its own place on the circle, so those of one route aren't put on top of each other)
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
                if (NavOf(ec.Id) is { } eo)
                    NavEnd(eo, ec, $"it has come to {d.Planet} with its hauler"); // (its next order is given there: EscortStep)
            }
        HaulSay(run, c, $"comes to {d.Planet} from another planet");
        if (!here)
        {
            run.Stage = HaulStage.Orbiting;
            run.Since = ServerTick;
            RouteArrived(run.Route);
            HaulTrade(run, c, StationMarkets.Haulers[run.Route], def, MarketOfDef(def));
        }
    }

    private void HaulSay(HaulRun run, Construct c, string line)
    {
        if (line == run.Last)
            return;
        run.Last = line;
        Log($"[haul] construct {c.Id} {line}");
    }

    /// <summary>Host: a hauler at its station (moored there, or on its circle when the station is in another world): it sells what it carries that the station buys, fills its tanks, then loads the leg's goods, paying from its power.</summary>
    /// <summary>Litres of fuel a unit of a station's hydrogen makes (fuel is no longer free: economy step 11; a tuning number).</summary>
    private const float FuelPerHydrogen = 40; // (10 until the long check, 2026-11-22: the haulers burned four times what Cinder makes)

    /// <summary>The share of a full tank a hauler needs to set out on a leg (a leg between planets takes most of a tank).</summary>
    private const float HaulFuelLeast = 0.6f;

    /// <summary>
    /// Host: <paramref name="c"/>'s tank filled at a station from its hydrogen stock (Karl, 2026-11-22: no making from nothing), as far as the stock
    /// goes; a hauler of another power pays the station's asking price from its books. A station that trades no hydrogen fills it as before.
    /// </summary>
    private void HaulFuel(Construct c, StationMarket m, PowerControllers.Power? pw, string power, string name, string what, bool quiet = false)
    {
        var cap = c.FuelCapacity();
        if (c.Fuel >= cap)
            return;
        var goods = StationMarkets.All[m.Def].Goods;
        var k = -1;
        for (var i = 0; i < goods.Length && k < 0; i++)
            if (goods[i].Material == _hydrogenId)
                k = i;
        if (k < 0)
        {
            Log($"[haul] {what} {c.Id} took on {cap - c.Fuel:0} L of fuel at {name}");
            c.Fuel = cap;
            return;
        }
        var want = (int)MathF.Ceiling((cap - c.Fuel) / FuelPerHydrogen);
        var units = Math.Min(want, m.Stock[k]);
        var foreign = StationPower(m) != CrewPowers.Find(power);
        if (foreign && units > 0)
        {
            units = Math.Min(units, pw is { Fallen: false } ? m.Afford(k, Math.Max(0, pw.Stock)) : 0);
            if (units > 0)
            {
                var cost = m.SellCost(k, units);
                pw!.Stock -= cost;
                pw.Spent += cost;
                StationMargin(m, cost);
            }
        }
        if (units <= 0)
        {
            if (!quiet)
                Log($"[haul] {what} {c.Id} gets no fuel at {name}: {(m.Stock[k] <= 0 ? "it is out of hydrogen" : "its power can't pay for it")} ({c.Fuel:0} of {cap:0} L aboard)");
            return;
        }
        m.Stock[k] -= units;
        StationsVersion++;
        var was = c.Fuel;
        c.Fuel = Math.Min(cap, c.Fuel + units * FuelPerHydrogen);
        Log($"[haul] {what} {c.Id} took on {c.Fuel - was:0} L of fuel at {name} for {units} hydrogen{(units < want ? " (all it had)" : "")}");
    }

    private byte _hydrogenId => RecyclingTable.Current.IdOf("hydrogen");

    private void HaulTrade(HaulRun run, Construct c, StationHaulerDef h, int def, StationMarket? m)
    {
        if (m == null || StationMarkets.All[def].State is MarketState.Closing or MarketState.Closed)
            return; // (a closed station trades with nobody: the hauler goes off its stopped route here)
        var name = StationMarkets.All[def].Name;
        var where = run.Stage == HaulStage.Orbiting ? $"{name} (on its circle: the station is in another world)" : name;
        var pw = Controllers.Powers[CrewPowers.Find(h.Power)];
        HaulFuel(c, m, pw, h.Power, name, "construct"); // (a station fills its haulers' tanks from its hydrogen: a leg between planets takes most of a tank)
        foreach (var e in _escorts)
            if (e.Hauler == run.Ship && Constructs.Get(e.Ship) is { } es)
                HaulFuel(es, m, pw, h.Power, name, "escort"); // (its escorts' too, wherever they hold)
        ConsignAtStation(c, run.Route, h, def, run.AtTo ? h.From : h.To); // (consignments off here and on for the next leg: GameConsignments.cs)
        Span<int> sold = stackalloc int[StationMarkets.All[def].Goods.Length];
        var own = pw != null && StationPower(m) == CrewPowers.Find(h.Power); // (its own power's station: goods move between its stations for nothing, 2026-11-22)
        var value = StationTakeCargo(m, c, sold, own);
        var unloaded = 0;
        foreach (var n in sold)
            unloaded += n;
        if (own && unloaded > 0)
        {
            StationsVersion++;
            Log($"[haul] construct {c.Id} ({h.Power}) unloaded {StationGoodsText(m, sold)} at its own {where}");
        }
        if (value > 0)
        {
            if (pw is { Fallen: false })
            {
                pw.Stock += value;
                pw.Earned += value;
            }
            StationsVersion++;
            Log($"[haul] construct {c.Id} ({h.Power}) sold {StationGoodsText(m, sold)} to {where} for {value:0} (value)");
        }
        var goods = StationMarkets.All[def].Goods;
        Span<int> bought = stackalloc int[goods.Length];
        double cost = 0;
        // What it loads for the next leg, most needed at the far end first (phase 3: supply lines by what a station lacks): the route's own
        // goods, and with "needs" any good this station sells that the far one is short of (under its want). A good the far one is full of stays.
        var far = run.AtTo ? h.From : h.To;
        var fm = MarketOfDef(far);
        var fg = StationMarkets.All[far].Goods;
        Span<int> pick = stackalloc int[goods.Length];
        Span<float> want = stackalloc float[goods.Length];
        var np = 0;
        var listed = run.AtTo ? h.Back : h.Goods;
        for (var k = 0; k < goods.Length; k++)
        {
            if (goods[k].Sell <= 0 || m.Stock[k] <= 0)
                continue;
            var fk = -1;
            for (var j = 0; j < fg.Length && fk < 0; j++)
                if (fg[j].Material == goods[k].Material)
                    fk = j;
            var need = fk >= 0 && fk < fm.Stock.Length && fg[fk].Buy > 0 ? 1f - fm.Stock[fk] / (float)fg[fk].Target : -1f; // (above 0: short of its want)
            var mine = Array.IndexOf(listed, goods[k].Material) >= 0;
            if (mine ? fk >= 0 && fk < fm.Stock.Length && fm.Stock[fk] >= fg[fk].Most : !h.Needs || need <= 0 || m.Stock[k] <= Keeps(goods[k]))
                continue; // (a good taken for the far end's needs only from what this station has past what it keeps: the Militia's steel run bought Slopolis' steel back to Glacier, 2026-11-22)
            pick[np] = k;
            want[np++] = need + (mine ? 0.01f : 0); // (a tie goes to the route's own)
        }
        for (var i = 1; i < np; i++) // (most needed first: a few goods, an insertion sort)
            for (var j = i; j > 0 && want[j] > want[j - 1]; j--)
            {
                (want[j], want[j - 1]) = (want[j - 1], want[j]);
                (pick[j], pick[j - 1]) = (pick[j - 1], pick[j]);
            }
        for (var i = 0; i < np; i++)
        {
            var k = pick[i];
            var spare = Array.IndexOf(listed, goods[k].Material) >= 0 ? m.Stock[k] : Math.Max(0, m.Stock[k] - Keeps(goods[k]));
            var afford = own ? Math.Max(0, m.Stock[k] - Keeps(goods[k])) : pw is { Fallen: false } ? Math.Min(spare, m.Afford(k, Math.Max(0, pw.Stock - cost))) : 0; // (priced across the stock the load moves; at its own station, what it has past what it keeps, as a consignment takes)
            if (afford < HaulLeastLot)
                continue; // (a handful isn't worth a bin: what its power can afford of it, or the station has)
            var n = StationLoadCargo(m, c, k, afford, out var paid, own);
            bought[k] += n;
            cost += paid;
        }
        if (h.Parts)
        {
            var (pv, pc) = HaulParts(run, c, h, def, m, pw, cost, where, own);
            value += pv;
            cost += pc;
        }
        var took = 0;
        foreach (var n in bought)
            took += n;
        if (cost > 0 && pw != null)
        {
            pw.Stock -= cost;
            pw.Spent += cost;
            Log($"[haul] construct {c.Id} ({h.Power}) bought {(StationGoodsText(m, bought) is { Length: > 0 } t ? t : "parts")} at {where} for {cost:0} (value)");
        }
        else if (own && took > 0)
            Log($"[haul] construct {c.Id} ({h.Power}) loaded {StationGoodsText(m, bought)} at its own {where}");
        else if (value <= 0 && unloaded == 0 && !h.Parts)
            Log($"[haul] construct {c.Id} ({h.Power}) traded nothing at {where}");
    }

    /// <summary>
    /// Host: a parts hauler at its station (route "parts": a store's supply, Karl 2026-11-21: "make sure NPC frigates can dock and supply the
    /// stations"): it sells the station every part its stores hold that the station buys, up to the station's room for it, then loads into its
    /// Storage Volumes the parts this station sells that the far one is short of (under its want), most needed first, up to the shortfall and
    /// what its power can afford. The value it earned (already in its power's books) and what the load cost (for the caller to book).
    /// </summary>
    /// <summary>Host: up to <paramref name="most"/> pieces of a kind off a piece hold (a heavy container's), each off the top of its stack; how many.</summary>
    private int HaulRackTake(Construct b, int part, ushort def, int most)
    {
        if (Constructs.StoreOf(b, part) is not { } st)
            return 0;
        ConstructRackPack.Ensure(ConstructPads.RackDef(b, part), st);
        var n = 0;
        while (n < most && ConstructRackPack.LastOf(st, def, out var slab, out var lane) && Constructs.RackTakeAt(b, part, slab, lane) != null)
            n++;
        return n;
    }

    private (double Value, double Cost) HaulParts(HaulRun run, Construct c, StationHaulerDef h, int def, StationMarket m, PowerControllers.Power? pw, double spent, string where, bool own)
    {
        var sd = StationMarkets.All[def];
        var parts = sd.Parts;
        var ng = sd.Goods.Length;
        double value = 0, cost = 0;
        var sold = 0;
        var (mostTook, mostPart) = (0, ConstructParts.None);
        // its holds: its own Storage Volumes and those of the cargo it carries (the Packet's standard containers)
        var holds = new List<Construct> { c };
        foreach (var b in Constructs.Bodies)
            if (b.CarriedBy == c.Id && !b.Wreck)
                holds.Add(b);
        // their stores and volumes, found once a stop (not once per part kind or unit)
        var stores = new List<(Construct B, int P)>();
        var vols = new List<(Construct B, int P)>();
        var cargo = new List<(Construct B, int P)>(); // (heavy containers' piece holds: what a volume can't take, Karl 2026-11-22)
        foreach (var b in holds)
            for (var i = 0; i < b.PartCount; i++)
            {
                if (Constructs.StoreOf(b, i) is not null)
                    stores.Add((b, i));
                if (b.DefOf(i).IsVolumeStore)
                    vols.Add((b, i));
                if (b.DefOf(i).IsCargoHold)
                    cargo.Add((b, i));
            }
        for (var j = 0; j < parts.Length; j++)
        {
            if (parts[j].Buy <= 0 || ng + j >= m.Stock.Length)
                continue;
            var room = Math.Max(0, parts[j].Most - m.Stock[ng + j]);
            if (room <= 0)
                continue;
            var what = new CHolding(true, parts[j].Part, 0);
            var took = 0;
            foreach (var (b, i) in stores)
                if (took < room)
                    took += b.DefOf(i).IsPieceRack ? HaulRackTake(b, i, parts[j].Part, room - took) : Constructs.TakeHolding(b, i, what, room - took);
            if (took == 0)
                continue;
            if (took > mostTook)
                (mostTook, mostPart) = (took, parts[j].Part);
            m.Stock[ng + j] += took;
            value += took * (double)parts[j].Buy;
            sold += took;
        }
        if (sold > 0)
        {
            if (own)
                value = 0; // (its own store: nobody is paid)
            else if (pw is { Fallen: false })
            {
                pw.Stock += value;
                pw.Earned += value;
            }
            StationsVersion++;
            StoreDelivered(m.Body, mostPart); // (a store's Spotlight room shows it: GameStoreShop.cs)
            Log($"[haul] construct {c.Id} ({h.Power}) delivered {sold} parts to {where} for {value:0} (value)");
        }
        // What the far station is short of, most needed first.
        var far = run.AtTo ? h.From : h.To;
        var fd = StationMarkets.All[far];
        var fm = MarketOfDef(far);
        var fng = fd.Goods.Length;
        Span<int> pick = stackalloc int[parts.Length];
        Span<float> need = stackalloc float[parts.Length];
        Span<int> short_ = stackalloc int[parts.Length];
        var np = 0;
        for (var j = 0; j < parts.Length; j++)
        {
            if (parts[j].Sell <= 0 || ng + j >= m.Stock.Length || m.Stock[ng + j] <= 0)
                continue;
            var fj = Array.FindIndex(fd.Parts, x => x.Part == parts[j].Part);
            if (fj < 0 || fd.Parts[fj].Buy <= 0 || fng + fj >= fm.Stock.Length)
                continue;
            var gap = fd.Parts[fj].Target - fm.Stock[fng + fj];
            if (gap <= 0)
                continue;
            pick[np] = j;
            short_[np] = gap;
            need[np++] = gap / (float)fd.Parts[fj].Target;
        }
        for (var i = 1; i < np; i++)
            for (var k = i; k > 0 && need[k] > need[k - 1]; k--)
            {
                (need[k], need[k - 1]) = (need[k - 1], need[k]);
                (pick[k], pick[k - 1]) = (pick[k - 1], pick[k]);
                (short_[k], short_[k - 1]) = (short_[k - 1], short_[k]);
            }
        int loaded = 0, vi = 0, ci = 0;
        string? refusal = null;
        for (var i = 0; i < np; i++)
        {
            var j = pick[i];
            if (ConstructParts.Def(parts[j].Part) is not { } pd)
                continue;
            var n = Math.Min(short_[i], m.Stock[ng + j]);
            for (var u = 0; u < n; u++)
            {
                if (!own && (pw is not { Fallen: false } || pw.Stock - spent - cost < parts[j].Sell))
                    break;
                var put = false;
                var heavy = pd.PhysicsOnly || pd.Role == PartRole.Storage; // (no Storage Volume takes it: a heavy container's hold does)
                for (var t = 0; t < cargo.Count && !put && heavy; t++)
                {
                    var (b, p) = cargo[(ci + t) % cargo.Count];
                    if (Constructs.RackPut(b, p, pd, ordered: true))
                    {
                        put = true;
                        ci = (ci + t) % cargo.Count;
                    }
                }
                if (heavy && !put && refusal == null)
                    refusal = cargo.Count == 0 ? $"the {pd.Name} is {(pd.PhysicsOnly ? $"physics-only ({pd.PhysicsWhy})" : "a store")} and it has no heavy container" : ConstructRackPack.Fit(cargo[0].B.DefOf(cargo[0].P), pd, out _, out _, out _) ? "its heavy containers are full" : $"the {pd.Name} is too big for its heavy containers";
                for (var t = 0; t < vols.Count && !put && !heavy; t++)
                {
                    var (b, p) = vols[(vi + t) % vols.Count]; // (from the volume that took the last one)
                    if (Constructs.PutPart(b, p, pd) is { } why)
                        refusal ??= why;
                    else
                    {
                        put = true;
                        vi = (vi + t) % vols.Count;
                    }
                }
                if (!put)
                    break;
                m.Stock[ng + j]--;
                if (!own)
                    cost += parts[j].Sell;
                loaded++;
            }
        }
        if (loaded > 0)
        {
            StationsVersion++;
            Log($"[haul] construct {c.Id} ({h.Power}) loaded {loaded} parts at {where} for {StationMarkets.All[far].Name}");
        }
        else if (np > 0)
            HaulSay(run, c, $"loads no parts at {where}: {refusal ?? "its power can't afford them, or it has no Storage Volume"}");
        return (value, cost);
    }
}
