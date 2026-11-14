using System.Diagnostics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The space layer's phase 2 (Source/Space/SpaceShips.cs, docs/space/SPACE.md "Ships"): the universal Kepler drift
    /// against the rails; energy over 10,000 orbits under the symplectic step with a force hook; scripted burns against the
    /// analytic answer; a close pass under the adaptive step; a flyby through a sphere of influence (hand-overs) against a
    /// one-frame reference; on and off rails; warp; the predictor; host and client agree (snapshots, sampling,
    /// determinism); the warp rule; and the budget (200 ships, 32 bodies, no allocation).
    /// </summary>
    private static void CheckSpaceShips()
    {
        var dev = StarSystemFile.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, StarSystemFile.FolderName, "Dev.json")), "Dev.json");
        ShipsUniversalKepler();
        ShipsEnergy();
        ShipsBurns();
        ShipsClosePass();
        ShipsFlyby(dev);
        ShipsRails(dev);
        ShipsWarp(dev);
        ShipsPredictor(dev);
        ShipsClient(dev);
        ShipsWarpRule();
        ShipsBudget();
    }

    /// <summary>A one-body system: a 10 km rock (the Planetoid's GM) alone, for checks with an exact answer.</summary>
    private static StarSystem RockSystem(double mu = 1.8e9) => new()
    {
        Name = "Rock",
        Bodies = [new SpaceBody { Name = "Rock", Mu = mu, Radius = 10_000 }],
    };

    private static void ShipsUniversalKepler()
    {
        // Closed orbits: the universal drift against KeplerOrbit's rails at many times, both ways.
        double worst = 0;
        foreach (var (e, inc) in new[] { (0.0, 0.0), (0.0167, 0.3), (0.6, 1.2), (0.97, 2.5) })
        {
            var o = new KeplerOrbit(1.8e9, 60_000, e, inc, 0.7, 1.9, 0.4);
            o.StateAt(o.Epoch, out var r0, out var v0);
            for (var k = -40; k <= 40; k++)
            {
                var t = new SpaceTime((long)(o.Period * SpaceTime.NsPerSecond * k / 7.3));
                o.StateAt(t, out var rw, out var vw);
                if (!UniversalKepler.PropagateNs(o.Mu, r0, v0, t.Ns, out var r, out var v))
                    Fail($"universal kepler: no answer at e {e}, step {k}");
                worst = Math.Max(worst, (r - rw).Length / o.A + (v - vw).Length / (o.N * o.A));
            }
        }
        if (worst > 1e-9)
            Fail($"universal kepler: off the rails by {worst:g3} of the orbit's size");

        // Escapes: a hyperbola keeps its energy and angular momentum, and running it back lands where it started.
        var rh = new Vec3d(12_000, 3_000, -500);
        var vh = new Vec3d(-200, 700, 90);
        const double mu = 1.8e9;
        double En(Vec3d r, Vec3d v) => v.LengthSquared / 2 - mu / r.Length;
        foreach (var dt in new[] { 1.0, 37.5, 600, 20_000 })
        {
            UniversalKepler.Propagate(mu, rh, vh, dt, out var r1, out var v1);
            UniversalKepler.Propagate(mu, r1, v1, -dt, out var r2, out var v2);
            var dE = Math.Abs(En(r1, v1) - En(rh, vh)) / Math.Abs(En(rh, vh));
            var dH = (Vec3d.Cross(r1, v1) - Vec3d.Cross(rh, vh)).Length / Vec3d.Cross(rh, vh).Length;
            if (dE > 1e-10 || dH > 1e-10 || (r2 - rh).Length > 1e-5)
                Fail($"universal kepler: an escape {dt} s on: energy {dE:g3}, angular momentum {dH:g3}, back {(r2 - rh).Length:g3} m");
        }
    }

    /// <summary>A force hook for the energy check: an extra pull towards the origin falling off as 1/r^3 (like a flattened body's), a static field.</summary>
    private sealed class InverseCubeForce(double k) : ISpaceForce
    {
        public Vec3d Accel(SpacePos pos, Vec3d vel, SpaceTime t)
        {
            var r = pos - SpacePos.Origin;
            var l2 = r.LengthSquared;
            return r * (-k / (l2 * l2));
        }

        public double Potential(SpacePos pos) => -k / (2 * (pos - SpacePos.Origin).LengthSquared);
    }

    private static void ShipsEnergy()
    {
        // An eccentric, inclined ship round the rock with a force hook (an extra 1/r^3 pull, a thousandth of the rock's at
        // 20 km): a static field, so energy is conserved exactly, the orbit precesses, and the symplectic step's energy
        // error stays bounded over 10,000 orbits instead of drifting.
        var sys = RockSystem();
        const double mu = 1.8e9;
        var hook = new InverseCubeForce(1e-3 * mu * 20_000);
        var ships = new SpaceShips(sys, 1, default, [hook]);
        var s = ships.AddRelative(0, new Vec3d(20_000, 0, 0), new Vec3d(0, Math.Sqrt(mu / 20_000) * 1.1, 60));
        double Energy() => ships.VelocityOf(s).LengthSquared / 2 - mu / (ships.PositionOf(s) - SpacePos.Origin).Length + hook.Potential(ships.PositionOf(s));
        var e0 = Energy();
        var a = -mu / (2 * ships.KeplerEnergy(s));
        var period = 2 * Math.PI * Math.Sqrt(a * a * a / mu);
        const int orbits = 10_000;
        var stepNs = (long)(period / 4 * SpaceTime.NsPerSecond); // a quarter orbit per call (several substeps each)
        var calls = (long)orbits * 4;
        double firstK = 0, lastK = 0;
        var sw = Stopwatch.StartNew();
        for (long c = 1; c <= calls; c++)
        {
            ships.Advance(new SpaceTime(stepNs * c));
            var err = Math.Abs(Energy() - e0) / Math.Abs(e0);
            if (c <= calls / 10)
                firstK = Math.Max(firstK, err);
            else if (c > calls - calls / 10)
                lastK = Math.Max(lastK, err);
        }
        Line($"  space ships: 10,000 orbits with a force hook: energy error {firstK:g3} (first 1,000), {lastK:g3} (last 1,000), "
             + $"{ships.CoastSteps} steps in {sw.Elapsed.TotalMilliseconds:0} ms");
        if (ships.ModeOf(s) != ShipMode.Coast)
            Fail($"space ships: the hooked orbit should coast (is {ships.ModeOf(s)})");
        if (lastK > 1e-5 || lastK > 1.5 * firstK + 1e-12)
            Fail($"space ships: energy drifted over 10,000 orbits (first 1,000: {firstK:g3}, last 1,000: {lastK:g3})");
        if (ships.DriftFailures > 0)
            Fail($"space ships: {ships.DriftFailures} Kepler drifts failed");
    }

    private static void ShipsBurns()
    {
        // Free space (a star too light to matter, far away): a 2 m/s^2 burn for 100 s moves 10 km and gains 200 m/s.
        var empty = RockSystem(1e-6);
        var ships = new SpaceShips(empty, 1);
        var start = SpacePos.FromMetres(new Vec3d(1e9, 0, 0));
        var s = ships.Add(start, new Vec3d(0, 5, 0));
        ships.Thrust[s] = new Vec3d(2, 0, 0);
        for (var tick = 1; tick <= 6000; tick++)
            ships.Advance(new SpaceTime(SpaceTime.TickNs(tick)));
        var moved = ships.PositionOf(s) - start;
        var want = new Vec3d(10_000, 500, 0);
        if (ships.ModeOf(s) != ShipMode.Adaptive)
            Fail($"space ships: a burning ship should take the adaptive step (is {ships.ModeOf(s)})");
        if ((moved - want).Length > 1e-3 || (ships.VelocityOf(s) - new Vec3d(200, 5, 0)).Length > 1e-6)
            Fail($"space ships: a 100 s burn moved {moved}, speed {ships.VelocityOf(s)} (want {want}, (200, 5, 0))");

        // In orbit: 10 m/s prograde over 0.5 s at 20 km round the rock against the impulsive answer (vis-viva).
        var rock = RockSystem();
        ships = new SpaceShips(rock, 1);
        var vc = Math.Sqrt(1.8e9 / 20_000);
        s = ships.AddRelative(0, new Vec3d(20_000, 0, 0), new Vec3d(0, vc, 0));
        ships.Thrust[s] = new Vec3d(0, 20, 0);
        ships.Advance(SpaceTime.FromSeconds(0.5));
        ships.Thrust[s] = Vec3d.Zero;
        ships.Advance(SpaceTime.FromSeconds(1));
        var aWant = 1 / (2 / 20_000.0 - (vc + 10) * (vc + 10) / 1.8e9);
        var aGot = -1.8e9 / (2 * ships.KeplerEnergy(s));
        if (Math.Abs(aGot - aWant) / aWant > 2e-4)
            Fail($"space ships: a 10 m/s prograde burn gave a {aGot:0.0} m orbit, not {aWant:0.0} m");
        if (ships.ModeOf(s) == ShipMode.Adaptive)
            Fail("space ships: a ship should leave the adaptive step when the burn ends");
    }

    private static void ShipsClosePass()
    {
        // A flyby 10.5 km from the rock's centre at 200 m/s from afar: the adaptive step and the coasting step against the
        // exact hyperbola.
        var sys = RockSystem();
        var r0 = new Vec3d(-150_000, 15_400, 2_000);
        var v0 = new Vec3d(560, 0, 0);
        UniversalKepler.Propagate(1.8e9, r0, v0, 600, out var rWant, out var vWant);
        foreach (var flags in new[] { ShipFlags.ForceAdaptive, ShipFlags.NoRails })
        {
            var ships = new SpaceShips(sys, 1);
            var s = ships.AddRelative(0, r0, v0, flags);
            var closest = double.MaxValue;
            for (var tick = 1; tick <= 600 * 60; tick += 15)
            {
                ships.Advance(new SpaceTime(SpaceTime.TickNs(tick)));
                closest = Math.Min(closest, (ships.PositionOf(s) - SpacePos.Origin).Length);
            }
            ships.Advance(SpaceTime.FromSeconds(600));
            var err = (ships.PositionOf(s) - SpacePos.FromMetres(rWant)).Length;
            Line($"  space ships: close pass ({flags}): closest {closest / 1000:0.000} km, off the hyperbola by {err * 1000:0.000} mm, "
                 + $"{ships.AdaptiveSteps} adaptive steps");
            if (closest > 15_000 || closest < 10_000)
                Fail($"space ships: the close pass should come within 10 to 15 km (came {closest:0} m)");
            if (err > 0.01)
                Fail($"space ships: the close pass ({flags}) is {err:0.0000} m off the exact hyperbola");
        }
    }

    private static void ShipsFlyby(StarSystem dev)
    {
        // A ship from 8,000 km out at 1 km/s falls past the Planetoid (18 km at closest): it starts round the star, is
        // handed to the Planetoid and back. Against one-frame references (round the star the whole way, adaptive, tight).
        var home = dev.IndexOf("Planetoid");
        var t0 = SpaceTime.FromSeconds(86400 * 3.3);
        dev.StateOf(home, t0, out var hp, out var hv);
        var rel = new Vec3d(8e6, 20_000, 3_000);
        var start = hp + rel;
        var vel = hv + new Vec3d(-1000, 0, 0);
        const double span = 16_000;
        SpacePos Run(ShipFlags flags, double posTol, out long handovers, out double closest)
        {
            var ships = new SpaceShips(dev, 1, t0);
            if (flags != ShipFlags.None)
            {
                ships.PosTolerance = posTol;
                ships.RelTolerance = 0;
                ships.VelTolerance = 1e-9;
            }
            var s = ships.Add(start, vel, flags);
            if ((flags & ShipFlags.NoHandover) == 0 && ships.PrimaryOf(s) != 0)
                Fail($"space ships: 8,000 km from the Planetoid should be round the star (is {ships.PrimaryOf(s)})");
            closest = double.MaxValue;
            for (var k = 1; k <= 1600; k++)
            {
                var t = t0 + (long)(span * SpaceTime.NsPerSecond * k / 1600);
                ships.Advance(t);
                closest = Math.Min(closest, (ships.PositionOf(s) - ships.BodyPosition(home)).Length);
            }
            handovers = ships.Handovers;
            return ships.PositionOf(s);
        }
        var got = Run(ShipFlags.None, 0, out var hand, out var closestGot);
        var refPos = Run(ShipFlags.NoHandover | ShipFlags.ForceAdaptive | ShipFlags.NoRails, 1e-5, out _, out var closestRef);
        var endHome = dev.PositionOf(home, t0 + (long)(span * SpaceTime.NsPerSecond));
        var err = (got - refPos).Length;
        Line($"  space ships: flyby: closest {closestGot / 1000:0.000} km (ref {closestRef / 1000:0.000}), {hand} hand-overs, "
             + $"{(got - endHome).Length / 1000:0.0} km out at the end, {err:0.0000} m from the reference");
        if (hand < 2)
            Fail($"space ships: the flyby should be handed to the Planetoid and back ({hand} hand-overs)");
        if (closestGot > 30_000 || closestGot < 10_000)
            Fail($"space ships: the flyby should pass 10 to 30 km from the Planetoid's centre (passed {closestGot:0} m)");
        if (err > 1)
            Fail($"space ships: the flyby ended {err:0.000} m from the one-frame reference");
    }

    private static void ShipsRails(StarSystem dev)
    {
        // A ship in a circular orbit 8 km from Redrock's centre: nothing else matters there, so it goes on rails, and
        // lands where coasting (never on rails) puts it.
        var red = dev.IndexOf("Redrock");
        var mu = dev.Bodies[red].Mu;
        var r0 = new Vec3d(8_000, 0, 0);
        var v0 = new Vec3d(0, Math.Sqrt(mu / 8_000), 0);
        var rails = new SpaceShips(dev, 2);
        var a = rails.AddRelative(red, r0, v0);
        var b = rails.AddRelative(red, r0, v0, ShipFlags.NoRails);
        if (rails.ModeOf(a) != ShipMode.Rail || rails.ModeOf(b) != ShipMode.Coast)
            Fail($"space ships: a lone orbit round Redrock should be on rails ({rails.ModeOf(a)}), the NoRails one coasting ({rails.ModeOf(b)})");
        for (var tick = 1; tick <= 3600; tick++)
            rails.Advance(new SpaceTime(SpaceTime.TickNs(tick * 24))); // a day at 24x
        var gap = (rails.PositionOf(a) - rails.PositionOf(b)).Length;
        if (gap > 0.01)
            Fail($"space ships: on rails and coasting differ by {gap:0.0000} m after a day round Redrock");
        if (rails.MaxWarp < 1000)
            Fail($"space ships: a lone orbit shouldn't hold the warp under 1,000x (limit {rails.MaxWarp})");

        // At full warp for 30 days the rail is exact: where its own two-body orbit says.
        rails.Remove(b);
        rails.RelativeOf(a, out _, out var ra, out var va);
        var orbit = KeplerOrbit.FromState(mu, ra, va, rails.Time);
        var clock = new SpaceClock(rails.Time) { Warp = SpaceClock.MaxWarp };
        var ticks = (int)(30 * 86400.0 / SpaceClock.MaxWarp * 60);
        for (var tick = 0; tick < ticks; tick++)
        {
            clock.Tick();
            rails.Advance(clock.Now);
        }
        rails.RelativeOf(a, out _, out ra, out _);
        var off = (ra - orbit.PositionAt(rails.Time)).Length;
        if (rails.ModeOf(a) != ShipMode.Rail || off > 1e-4)
            Fail($"space ships: 30 days on rails at {SpaceClock.MaxWarp}x: {rails.ModeOf(a)}, {off:g3} m off its orbit");
        if (rails.MaxWarp != SpaceClock.MaxWarp)
            Fail($"space ships: ships on rails shouldn't limit the warp (limit {rails.MaxWarp})");

        // A burn takes it off the rails; when it ends it goes back on, on a new orbit.
        var e0 = rails.KeplerEnergy(a);
        rails.Thrust[a] = Vec3d.Cross(Vec3d.UnitZ, ra).Normalized() * 0.5; // prograde-ish
        var t1 = rails.Time;
        rails.Advance(t1 + SpaceTime.TickNs(1));
        if (rails.ModeOf(a) != ShipMode.Adaptive)
            Fail($"space ships: thrust should take a ship off its rails (is {rails.ModeOf(a)})");
        for (var tick = 2; tick <= 600; tick++)
            rails.Advance(t1 + SpaceTime.TickNs(tick));
        rails.Thrust[a] = Vec3d.Zero;
        rails.Advance(t1 + SpaceTime.TickNs(601));
        if (rails.ModeOf(a) != ShipMode.Rail || !(rails.KeplerEnergy(a) > e0 + 1))
            Fail($"space ships: after the burn: {rails.ModeOf(a)} (want Rail), energy {e0:0.0} -> {rails.KeplerEnergy(a):0.0} J/kg");
    }

    private static void ShipsWarp(StarSystem dev)
    {
        // A low orbit round the Planetoid (its moonlet tugs, so it coasts): 600 ticks at 1x, 60 at 10x and 6 at 100x land together.
        var home = dev.IndexOf("Planetoid");
        var mu = dev.Bodies[home].Mu;
        var r0 = new Vec3d(12_000, 0, 1_000);
        var v0 = new Vec3d(0, Math.Sqrt(mu / r0.Length), 0);
        var ends = new SpacePos[3];
        var warps = new[] { 1, 10, 100 };
        for (var k = 0; k < warps.Length; k++)
        {
            var flight = new SpaceFlight(dev, 1);
            var s = flight.Ships.AddRelative(home, r0, v0);
            if (flight.Ships.ModeOf(s) != ShipMode.Coast)
                Fail($"space ships: a low Planetoid orbit should coast (the moonlet tugs it): {flight.Ships.ModeOf(s)}");
            flight.RequestWarp(warps[k], fromHost: true);
            for (var tick = 0; tick < 600 / warps[k]; tick++)
                flight.Tick();
            if (flight.Clock.Warp != warps[k] || flight.Clock.Now != new SpaceTime(SpaceTime.NsPerSecond * 10))
                Fail($"space ships: at {warps[k]}x the clock is {flight.Clock.Warp}x and at {flight.Clock.Now}");
            ends[k] = flight.Ships.PositionOf(s);
        }
        var d1 = (ends[1] - ends[0]).Length;
        var d2 = (ends[2] - ends[0]).Length;
        if (d1 > 1e-3 || d2 > 1e-3)
            Fail($"space ships: 10 s at 1x, 10x and 100x end {d1 * 1000:0.000} and {d2 * 1000:0.000} mm apart");
    }

    private static void ShipsPredictor(StarSystem dev)
    {
        // The predicted path (one point every 100 s for 2 hours) against the ship really flying there, round the Planetoid.
        var home = dev.IndexOf("Planetoid");
        var mu = dev.Bodies[home].Mu;
        var ships = new SpaceShips(dev, 1);
        var s = ships.AddRelative(home, new Vec3d(15_000, 0, 0), new Vec3d(0, Math.Sqrt(mu / 15_000) * 1.1, 0));
        var ecl = new SpacePos[73];
        var rel = new Vec3d[73];
        var frame = new FrameRef(FrameKind.Inertial, home);
        ships.Predict(s, 100, ecl, frame, rel);
        var worst = 0.0;
        var worstRel = 0.0;
        for (var k = 0; k < ecl.Length; k++)
        {
            if (k > 0)
                for (var tick = 1; tick <= 100; tick++)
                    ships.Advance(new SpaceTime(SpaceTime.NsPerSecond * (100L * (k - 1) + tick)));
            worst = Math.Max(worst, (ships.PositionOf(s) - ecl[k]).Length);
            worstRel = Math.Max(worstRel, (rel[k] - (ecl[k] - dev.PositionOf(home, ships.Time))).Length);
        }
        Line($"  space ships: predictor: 2 hours ahead, {worst * 1000:0.000} mm from the flight");
        // The predictor takes its own step sizes (100 s calls) where the flight took 1 s calls: both are the coasting
        // step's few-centimetre-per-orbit accuracy in a low orbit the moonlet tugs, so they part by a metre or so in 2 h.
        if (worst > 3 || worstRel > 1e-3)
            Fail($"space ships: the predicted path is {worst:0.0000} m from the flight ({worstRel:0.0000} m in the Planetoid's frame)");
        // No allocation once warmed up: the least of identical rounds (SteadyAllocBytes), as a one-off on this thread (JIT
        // tier-up, a lazily made runtime structure) landed in the single measured call once on Windows CI (7152 B,
        // 2026-11-14) and never here in thousands of calls: a real per-call allocation shows in every round.
        var alloc = SteadyAllocBytes(() => ships.Predict(s, 100, ecl, frame, rel));
        if (alloc > 0)
            Fail($"space ships: the predictor allocated {alloc} B");
    }

    private static void ShipsClient(StarSystem dev)
    {
        var home = dev.IndexOf("Planetoid");
        var mu = dev.Bodies[home].Mu;
        var r0 = new Vec3d(12_000, 0, 1_000);
        var v0 = new Vec3d(0, Math.Sqrt(mu / r0.Length), 0);

        // Determinism: two hosts given the same inputs agree to the bit.
        var h1 = new SpaceShips(dev, 2);
        var h2 = new SpaceShips(dev, 2);
        foreach (var h in new[] { h1, h2 })
        {
            h.AddRelative(home, r0, v0);
            h.AddRelative(home, r0 * 2, v0 * 0.8);
        }
        for (var tick = 1; tick <= 300; tick++)
        {
            h1.Thrust[1] = h2.Thrust[1] = tick < 100 ? new Vec3d(0, 0.3, 0) : Vec3d.Zero;
            h1.Advance(new SpaceTime(SpaceTime.TickNs(tick)));
            h2.Advance(new SpaceTime(SpaceTime.TickNs(tick)));
        }
        for (var i = 0; i < 2; i++)
            if (h1.PositionOf(i) != h2.PositionOf(i) || h1.VelocityOf(i) != h2.VelocityOf(i))
                Fail($"space ships: two hosts stepping the same ship {i} disagree");

        // Snapshots round-trip exactly; a client sampling 20 Hz snapshots each frame stays on the host's path, at 1x and 1000x.
        Span<byte> buf = stackalloc byte[ShipState.Size];
        foreach (var warp in new[] { 1, 1000 })
        {
            var host = new SpaceShips(dev, 1);
            var s = host.AddRelative(home, r0, v0);
            var track = new ShipTrack();
            var worst = 0.0;
            for (var tick = 1; tick <= 1200; tick++)
            {
                var now = new SpaceTime(SpaceTime.TickNs((long)tick * warp));
                host.Advance(now);
                if (tick % 3 == 0)
                {
                    var st = ShipState.Of(host, s);
                    st.Write(buf);
                    var back = ShipState.Read(buf);
                    if (back != st)
                        Fail("space ships: a ship state didn't round-trip the wire format");
                    track.Push(back);
                }
                // The client draws 3 ticks (one snapshot) behind, like the construct interpolation.
                if (tick > 9 && track.Sample(dev, new SpaceTime(SpaceTime.TickNs((long)(tick - 3) * warp)), out var cp, out _))
                {
                    var hostThen = PastHost[(tick - 3) % PastHost.Length];
                    worst = Math.Max(worst, (cp - hostThen).Length);
                }
                PastHost[tick % PastHost.Length] = host.PositionOf(s);
            }
            Line($"  space ships: client at {warp}x: {worst * 1000:0.000} mm from the host");
            if (worst > (warp == 1 ? 1e-3 : 1.0))
                Fail($"space ships: a client sampling 20 Hz snapshots at {warp}x is {worst:0.0000} m from the host");
        }
    }

    private static readonly SpacePos[] PastHost = new SpacePos[8];

    private static void ShipsWarpRule()
    {
        var sys = RockSystem();
        var flight = new SpaceFlight(sys, 4);
        if (flight.RequestWarp(1000, fromHost: false) || flight.RequestedWarp != 1)
            Fail("space warp: a client's request shouldn't change the warp");
        flight.RequestWarp(1000, fromHost: true);
        flight.Tick();
        if (flight.Clock.Warp != 1000)
            Fail($"space warp: the host's 1000x with nobody busy and no ships gave {flight.Clock.Warp}x");
        flight.AnyoneBusy = true;
        flight.Tick();
        if (flight.Clock.Warp != 1)
            Fail($"space warp: someone busy should hold 1x (is {flight.Clock.Warp}x)");
        flight.AnyoneBusy = false;
        // A burning ship limits it.
        var s = flight.Ships.AddRelative(0, new Vec3d(20_000, 0, 0), new Vec3d(0, 300, 0));
        flight.Ships.Thrust[s] = new Vec3d(0, 1, 0);
        flight.RequestWarp(SpaceClock.MaxWarp, true);
        flight.Tick();
        flight.Tick();
        if (flight.Clock.Warp >= SpaceClock.MaxWarp || flight.Clock.Warp != Math.Min(SpaceClock.MaxWarp, flight.Clock.Warp) || flight.Clock.Warp < 1)
            Fail($"space warp: a burning ship should hold the warp under {SpaceClock.MaxWarp}x (is {flight.Clock.Warp}x, ship limit {flight.Ships.MaxWarp})");
        if (SpaceWarpRule.Effective(0, true, 5) != 0 || SpaceWarpRule.Effective(50, false, 20) != 20 || SpaceWarpRule.Effective(-3, false, 9) != 0)
            Fail("space warp: pause, the ship limit or a negative request came out wrong");
    }

    private static void ShipsBudget()
    {
        // 32 bodies (a star, 15 planetoids, 16 moons) and 200 ships: low orbits, moons, the star, a few burning.
        var bodies = new List<SpaceBody> { new() { Name = "Star", Mu = 1.32712440018e20, Radius = 7e8 } };
        for (var k = 0; k < 15; k++)
        {
            var a = StarSystemFile.Au * 0.4 * Math.Pow(1.25, k);
            bodies.Add(new SpaceBody
            {
                Name = $"P{k}", Parent = 0, Mu = 1e9 * (1 + k % 4), Radius = 8_000,
                Orbit = new KeplerOrbit(bodies[0].Mu, a, 0.02 * (k % 5), 0.01 * k, 0.4 * k, 1.1 * k, 0.7 * k),
            });
        }
        for (var k = 0; k < 16; k++)
        {
            var parent = 1 + k % 15;
            bodies.Add(new SpaceBody
            {
                Name = $"M{k}", Parent = parent, Mu = 2e6, Radius = 1_000,
                Orbit = new KeplerOrbit(bodies[parent].Mu, 60_000 + 15_000 * (k / 15), 0.03, 0.05, k, 2 * k, 3 * k),
            });
        }
        var sys = new StarSystem { Name = "Budget", Bodies = bodies.ToArray() };
        var ships = new SpaceShips(sys, 200);
        var rng = new Random(7);
        for (var n = 0; n < 200; n++)
        {
            var kind = n % 10;
            int body;
            double r;
            if (kind < 5) { body = 1 + n % 15; r = 12_000 + rng.NextDouble() * 20_000; }
            else if (kind < 7) { body = 16 + n % 16; r = 1_500 + rng.NextDouble() * 1_500; }
            else { body = 0; r = StarSystemFile.Au * (0.5 + rng.NextDouble() * 3); }
            var mu = bodies[body].Mu;
            var dir = new Vec3d(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() * 0.2 - 0.1).Normalized();
            var tang = Vec3d.Cross(Vec3d.UnitZ, dir).Normalized();
            var s = ships.AddRelative(body, dir * r, tang * Math.Sqrt(mu / r) * (0.95 + 0.1 * rng.NextDouble()));
            if (n % 20 == 3)
                ships.Thrust[s] = tang * 0.05;
        }
        // Warm up long enough for the JIT's tiered compilation to swap in optimised code (it waits ~100 ms after 30 calls).
        var warm = Stopwatch.StartNew();
        var tick0 = 0;
        while (warm.ElapsedMilliseconds < 400 && tick0 < 3000)
            ships.Advance(new SpaceTime(SpaceTime.TickNs(++tick0)));
        var times = new double[120];
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var tick = 0; tick < times.Length; tick++)
        {
            var t0 = Stopwatch.GetTimestamp();
            ships.Advance(new SpaceTime(SpaceTime.TickNs(tick0 + 1 + tick)));
            times[tick] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Array.Sort(times);
        var median = times[times.Length / 2];
        int coast = 0, rail = 0, adapt = 0;
        for (var i = 0; i < ships.Count; i++)
        {
            switch (ships.ModeOf(i))
            {
                case ShipMode.Coast: coast++; break;
                case ShipMode.Rail: rail++; break;
                default: adapt++; break;
            }
        }
        Line($"  space ships: budget: 200 ships ({coast} coasting, {rail} on rails, {adapt} adaptive), 32 bodies: median {median:0.000} ms "
             + $"a tick (worst {times[^1]:0.000}), {alloc} B allocated, warp limit {ships.MaxWarp}x");
        if (alloc > 0)
            Fail($"space ships: stepping 200 ships allocated {alloc} B over {times.Length} ticks (should be 0)");
        if (TickCpu.Available && median > 1.0)
            Fail($"space ships: 200 ships and 32 bodies take {median:0.000} ms a tick (budget 1 ms)");
        if (ships.DriftFailures > 0)
            Fail($"space ships: {ships.DriftFailures} drifts failed in the budget run");
    }
}
