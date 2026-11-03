using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// The space layer's phase 1 (Source/Space, docs/space/SPACE.md): fixed-point positions exact to 0.1 mm out to the
    /// system's reach, the nanosecond clock (60 ticks a second exactly, warp changes without drift, centuries without
    /// overflow), Kepler rails (periods, vis-viva and angular momentum, eccentric orbits, state round trips), the frame
    /// tree (every frame kind round trips, velocities against finite differences, body spin, the game's bubble on a
    /// planetoid matching the planetoid map), the Dev system file and refused files, and no allocation per query.
    /// </summary>
    private static void CheckSpaceFrames()
    {
        // ---- Fixed-point positions --------------------------------------------------------------------------------------
        foreach (var far in new[] { 0.0, StarSystemFile.Au, 40 * StarSystemFile.Au, SpacePos.SafeReach, -SpacePos.SafeReach })
        {
            var a = SpacePos.FromMetres(new Vec3d(far, -far / 3, far / 7));
            var b = a + new Vec3d(1e-4, -2e-4, 3.7);
            var d = b - a;
            if (Math.Abs(d.X - 1e-4) > 1e-12 || Math.Abs(d.Y + 2e-4) > 1e-12 || Math.Abs(d.Z - 3.7) > 1e-12)
                Fail($"space pos: a 0.1 mm step {far:g3} m from the star came back as {d}");
            if (b - new Vec3d(1e-4, -2e-4, 3.7) != a)
                Fail($"space pos: moving and moving back {far:g3} m out didn't land on the same step");
        }
        try
        {
            SpacePos.FromMetres(new Vec3d(SpacePos.Reach * 1.01, 0, 0));
            Fail("space pos: a position past the reach should be refused");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        if (SpacePos.Reach / StarSystemFile.Au < 6000)
            Fail($"space pos: the reach should be at least 6,000 AU (is {SpacePos.Reach / StarSystemFile.Au:0})");

        // ---- The clock --------------------------------------------------------------------------------------------------
        if (SpaceTime.TickNs(60) != SpaceTime.NsPerSecond || SpaceTime.TickNs(3) != 50_000_000 || SpaceTime.TickNs(1) != 16_666_666)
            Fail($"space time: 60 ticks should be 1 s exactly ({SpaceTime.TickNs(60)} ns), 3 ticks 50 ms ({SpaceTime.TickNs(3)})");
        for (long n = 0; n < 200; n++)
            if (SpaceTime.TickNs(n + 1) <= SpaceTime.TickNs(n))
                Fail($"space time: tick {n + 1} isn't after tick {n}");
        var century = 100L * 365 * 86400 * 60; // ticks in about a century
        if (SpaceTime.TickNs(century) != century / 60 * SpaceTime.NsPerSecond)
            Fail("space time: a century of ticks should be a whole number of seconds");
        var clock = new SpaceClock();
        for (var n = 0; n < 90; n++)
            clock.Tick();
        clock.Warp = 1000;
        for (var n = 0; n < 7; n++)
            clock.Tick();
        clock.Warp = 3;
        clock.Tick();
        var want = SpaceTime.TickNs(90) + SpaceTime.TickNs(7000) + SpaceTime.TickNs(3);
        if (clock.Now.Ns != want)
            Fail($"space clock: 90 ticks at 1x, 7 at 1000x and 1 at 3x gave {clock.Now.Ns} ns, not {want}");
        clock.Warp = 0;
        var paused = clock.Now;
        clock.Tick();
        if (clock.Now != paused)
            Fail("space clock: warp 0 should pause it");
        clock.Warp = SpaceClock.MaxWarp * 10;
        if (clock.Warp != SpaceClock.MaxWarp)
            Fail($"space clock: warp should stop at {SpaceClock.MaxWarp}");

        // ---- Kepler rails -----------------------------------------------------------------------------------------------
        const double sunMu = 1.32712440018e20;
        var circle = new KeplerOrbit(sunMu, StarSystemFile.Au, 0, 0, 0, 0, 0);
        if (Math.Abs(circle.Period / 86400 - 365.2569) > 0.01)
            Fail($"kepler: 1 AU round the Sun should take 365.26 days (got {circle.Period / 86400:0.0000})");
        foreach (var (e, inc) in new[] { (0.0, 0.0), (0.0167, 0.3), (0.6, 1.2), (0.97, 2.5) })
        {
            var o = new KeplerOrbit(1.8e9, 60_000, e, inc, 0.7, 1.9, 0.4, SpaceTime.FromSeconds(123));
            o.StateAt(o.Epoch, out var r0, out var v0);
            var h0 = Vec3d.Cross(r0, v0);
            double worstEnergy = 0, worstH = 0, worstFd = 0;
            for (var k = 1; k <= 64; k++)
            {
                var t = o.Epoch + (long)(o.Period * SpaceTime.NsPerSecond * k / 17.3);
                o.StateAt(t, out var r, out var v);
                var energy = v.LengthSquared / 2 - o.Mu / r.Length;
                worstEnergy = Math.Max(worstEnergy, Math.Abs(energy / o.Energy - 1));
                worstH = Math.Max(worstH, (Vec3d.Cross(r, v) - h0).Length / h0.Length);
                // Velocity against a finite difference of position (1 ms either side).
                var fd = (o.PositionAt(t + 1_000_000) - o.PositionAt(t + -1_000_000)) / 2e-3;
                worstFd = Math.Max(worstFd, (fd - v).Length / v.Length);
            }
            if (worstEnergy > 1e-10 || worstH > 1e-10 || worstFd > 1e-5)
                Fail($"kepler e {e}: energy off by {worstEnergy:g2}, angular momentum by {worstH:g2}, velocity vs finite difference by {worstFd:g2}");
            var back = o.PositionAt(o.Epoch + (long)Math.Round(o.Period * 5 * SpaceTime.NsPerSecond));
            if ((back - r0).Length > 1e-4)
                Fail($"kepler e {e}: five periods later it's {(back - r0).Length * 1000:0.###} mm from where it began");
            var mid = o.Epoch + (long)(o.Period * 0.37 * SpaceTime.NsPerSecond);
            o.StateAt(mid, out var rm, out var vm);
            var copy = KeplerOrbit.FromState(o.Mu, rm, vm, mid);
            var later = mid + (long)(o.Period * 0.81 * SpaceTime.NsPerSecond);
            var drift = (copy.PositionAt(later) - o.PositionAt(later)).Length;
            if (drift > 1e-3 || Math.Abs(copy.A / o.A - 1) > 1e-9 || Math.Abs(copy.E - e) > 1e-9)
                Fail($"kepler e {e}: the orbit from a state differs (a {copy.A:0.###} vs {o.A}, e {copy.E:0.######}, {drift * 1000:0.###} mm apart later)");
        }
        foreach (var e in new[] { 0.0, 0.5, 0.99 })
            for (var m = -3.1; m < 3.15; m += 0.37)
            {
                var ea = KeplerOrbit.EccentricAnomaly(m, e);
                if (Math.Abs(ea - e * Math.Sin(ea) - m) > 1e-12)
                    Fail($"kepler: E for M {m:0.00}, e {e} doesn't solve the equation");
            }
        // A planet on rails after a century is where its phase says (no accumulated error).
        var centuryT = SpaceTime.FromSeconds(100 * 365.25 * 86400);
        var phase = KeplerOrbit.WrapPi(circle.N * (centuryT - SpaceTime.Epoch));
        var expect = new Vec3d(Math.Cos(phase), Math.Sin(phase), 0) * StarSystemFile.Au;
        if ((circle.PositionAt(centuryT) - expect).Length > 5)
            Fail($"kepler: a century on, 1 AU is {(circle.PositionAt(centuryT) - expect).Length:0.0} m from its phase");

        // ---- The Dev system and its frames ------------------------------------------------------------------------------
        var file = Path.Combine(AppContext.BaseDirectory, StarSystemFile.FolderName, "Dev.json");
        StarSystem dev;
        try
        {
            dev = StarSystemFile.Load(File.ReadAllText(file), "Dev.json");
        }
        catch (Exception ex)
        {
            Fail($"Systems/Dev.json should load: {ex.Message}");
            return;
        }
        var home = dev.IndexOf("Planetoid");
        var moon = dev.IndexOf("Moonlet");
        if (dev.Count != 5 || home != 1 || moon != 2 || dev.Bodies[home].Map != "Planetoid")
            Fail($"Dev system: expected Slopstar, Planetoid (map Planetoid), Moonlet, Bluestone, Redrock ({string.Join(", ", dev.Bodies.Select(b => b.Name))})");
        else
        {
            var mapFile = Path.Combine(AppContext.BaseDirectory, "Maps", "Dev", "Planetoid.json");
            var map = File.Exists(mapFile) ? MapFile.Load(File.ReadAllText(mapFile), "Dev/Planetoid.json") : null;
            if (map?.Planetoid is not { } pl)
                Fail("the Dev map Planetoid should load with a planetoid");
            else if (Math.Abs(pl.Radius - dev.Bodies[home].Radius) > 1e-6 || Math.Abs(pl.Strength - dev.Bodies[home].SurfaceGravity) > 1e-3)
                Fail($"Dev system: Planetoid's radius and gravity ({dev.Bodies[home].Radius}, {dev.Bodies[home].SurfaceGravity:0.###}) should match the map's ({pl.Radius}, {pl.Strength})");
        }
        var moonBody = dev.Bodies[moon];
        if (Math.Abs(moonBody.RotationPeriod - moonBody.Orbit.Period) > 1e-9)
            Fail("Dev system: the locked moonlet should turn once per orbit");

        var times = new[] { SpaceTime.Epoch, SpaceTime.FromSeconds(3600.25), SpaceTime.FromSeconds(200 * 86400 + 0.016), SpaceTime.FromSeconds(30 * 365.25 * 86400) };
        var frames = new[] { FrameRef.Ecliptic, new FrameRef(FrameKind.Orbit, home), new FrameRef(FrameKind.Inertial, home), new FrameRef(FrameKind.BodyFixed, home),
            new FrameRef(FrameKind.Orbit, moon), new FrameRef(FrameKind.Inertial, moon), new FrameRef(FrameKind.BodyFixed, moon), new FrameRef(FrameKind.BodyFixed, 0) };
        var probe = new Vec3d(1234.5678, -9876.25, 42.125);
        foreach (var t in times)
        {
            foreach (var f in frames)
            {
                var pose = dev.PoseOf(f, t);
                var there = pose.FromEcliptic(pose.ToEcliptic(probe));
                if ((there - probe).Length > 2e-4)
                    Fail($"frames: {f} at {t} moved a point {(there - probe).Length * 1000:0.###} mm in a round trip");
                foreach (var g in frames)
                {
                    var other = dev.PoseOf(g, t);
                    var direct = pose.To(other, probe);
                    var via = other.FromEcliptic(pose.ToEcliptic(probe));
                    if ((direct - via).Length > 2e-4)
                        Fail($"frames: {f} -> {g} at {t}: direct and via the ecliptic differ by {(direct - via).Length * 1000:0.###} mm");
                    var back = other.To(pose, direct);
                    if ((back - probe).Length > 1e-6 * Math.Max(1, (pose.Origin - other.Origin).Length / 1e6))
                        Fail($"frames: {f} -> {g} -> back at {t} is {(back - probe).Length * 1000:0.###} mm off");
                }
                // A point still in the frame moves with it: its ecliptic velocity is the frame's, plus the spin's.
                var dtNs = 500_000_000L; // half a second either side: the fixed point's 0.1 mm steps are 1e-4 m/s
                var pa = dev.PoseOf(f, t + -dtNs).ToEcliptic(probe);
                var pb = dev.PoseOf(f, t + dtNs).ToEcliptic(probe);
                var fd = (pb - pa) / 1.0;
                var v = pose.VelocityToEcliptic(probe, Vec3d.Zero);
                if ((fd - v).Length > 2e-3)
                    Fail($"frames: {f} at {t}: a fixed point's velocity {v} differs from the finite difference {fd}");
            }
            // The moonlet goes round the Planetoid, so in Planetoid's inertial frame it's at its orbit's distance.
            var inHome = dev.PoseOf(new FrameRef(FrameKind.Inertial, home), t).FromEcliptic(dev.PositionOf(moon, t));
            var r = moonBody.Orbit.PositionAt(t).Length;
            if (Math.Abs(inHome.Length - r) > 2e-4)
                Fail($"frames: the moonlet is {inHome.Length:0.###} m from the Planetoid, its orbit says {r:0.###}");
            // Locked: the moonlet always shows the same face to the Planetoid (within the eccentricity's libration).
            var toHome = dev.PoseOf(new FrameRef(FrameKind.BodyFixed, moon), t).FromEcliptic(dev.PositionOf(home, t)).Normalized();
            var face = Math.Atan2(toHome.Y, toHome.X);
            if (t == times[0])
                _spaceFace = face;
            else if (Math.Abs(KeplerOrbit.WrapPi(face - _spaceFace)) > 2 * 2 * moonBody.Orbit.E + 0.05 + moonBody.Orbit.I)
                Fail($"frames: the locked moonlet turned its face {Math.Abs(KeplerOrbit.WrapPi(face - _spaceFace)) * 180 / Math.PI:0.0} degrees from the Planetoid");
        }
        // Half a day turns the Planetoid's ground half round in its inertial frame.
        var day = dev.Bodies[home].RotationPeriod;
        var equator = new Vec3d(dev.Bodies[home].Radius, 0, 0);
        var t0 = SpaceTime.FromSeconds(1000);
        var e0 = dev.PoseOf(new FrameRef(FrameKind.BodyFixed, home), t0);
        var e1 = dev.PoseOf(new FrameRef(FrameKind.BodyFixed, home), t0 + (long)(day / 2 * SpaceTime.NsPerSecond));
        var i0 = dev.PoseOf(new FrameRef(FrameKind.Inertial, home), t0);
        var i1 = dev.PoseOf(new FrameRef(FrameKind.Inertial, home), t0 + (long)(day / 2 * SpaceTime.NsPerSecond));
        var a0 = e0.To(i0, equator);
        var a1 = e1.To(i1, equator);
        if ((a0 + a1).Length > 1e-3)
            Fail($"frames: half a day should put an equator point on the far side ({a0} then {a1})");
        var axis = dev.Bodies[home].TiltRotation.Rotate(Vec3d.UnitZ);
        if (Math.Abs(Math.Acos(axis.Z) - dev.Bodies[home].Tilt) > 1e-12)
            Fail("frames: the Planetoid's spin axis should lean by its tilt");

        // The game's bubble at the Planetoid's north pole: the planetoid map's centre (0, -R, 0) is the body's centre,
        // +Y is up along the spin axis and the bubble's axes are right-handed.
        var R = dev.Bodies[home].Radius;
        foreach (var t in times)
        {
            var bubble = dev.BubbleOn(home, Math.PI / 2, 0, 0, t);
            var centre = bubble.ToEcliptic(new Vec3d(0, -R, 0));
            var miss = (centre - dev.PositionOf(home, t)).Length;
            if (miss > 2e-4)
                Fail($"bubble: the map's centre is {miss * 1000:0.###} mm from the Planetoid's at {t}");
            var upEcl = bubble.Rot.Rotate(Vec3d.UnitY);
            if (Vec3d.Dot(upEcl, dev.SpinRotation(home, t).Rotate(Vec3d.UnitZ)) < 1 - 1e-12)
                Fail("bubble: at the pole, game +Y should be the spin axis");
            var x = bubble.Rot.Rotate(Vec3d.UnitX);
            var y = bubble.Rot.Rotate(Vec3d.UnitY);
            var z = bubble.Rot.Rotate(Vec3d.UnitZ);
            if ((Vec3d.Cross(x, y) - z).Length > 1e-12)
                Fail("bubble: its axes should be right-handed like the game's");
        }
        foreach (var (lat, lon) in new[] { (0.0, 0.0), (0.5, 2.0), (-1.2, -2.9), (Math.PI / 2 - 1e-6, 1.0) })
        {
            var bubble = dev.BubbleOn(home, lat, lon, 25, times[2]);
            var fixedPose = dev.PoseOf(new FrameRef(FrameKind.BodyFixed, home), times[2]);
            var o = bubble.To(fixedPose, Vec3d.Zero);
            var up = bubble.To(fixedPose, Vec3d.UnitY * 10) - o;
            var north = bubble.To(fixedPose, -Vec3d.UnitZ * 10) - o;
            if (Math.Abs(o.Length - (R + 25)) > 2e-4 || Vec3d.Dot(up.Normalized(), o.Normalized()) < 1 - 1e-9 || north.Z < -1e-9 || Math.Abs(Math.Asin(o.Z / o.Length) - lat) > 1e-7)
                Fail($"bubble at lat {lat:0.00} lon {lon:0.00}: origin {o}, up {up}, north {north} aren't on the ground facing up and north");
        }

        // ---- Refused files ----------------------------------------------------------------------------------------------
        const string star = "{ \"name\": \"Star\", \"gm\": 1.3e20, \"radius\": 7e8 }";
        foreach (var (bad, why) in new[]
        {
            ("{ \"bodies\": [] }", "the star first"),
            ($"{{ \"bodies\": [ {star} ], \"stars\": 2 }}", "unknown key \"stars\""),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 1, \"e\": 1 }} }} ] }}", "closed orbits only"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Nope\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 1 }} }} ] }}", "isn't a body listed before it"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"km\": 600000 }} }} ] }}", "dips into Star"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"gm\": 9, \"orbit\": {{ \"au\": 1 }} }} ] }}", "exactly one of"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 1 }} }}, {{ \"name\": \"M\", \"parent\": \"P\", \"radius\": 100, \"gravity\": 1, \"orbit\": {{ \"km\": 90000 }} }} ] }}", "Hill radius"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 1, \"e\": 0.3 }} }}, {{ \"name\": \"Q\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 1.2 }} }} ] }}", "overlap"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"P\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 9000 }} }} ] }}", "too far"),
            ($"{{ \"bodies\": [ {star}, {{ \"name\": \"star\", \"parent\": \"Star\", \"radius\": 1e4, \"gravity\": 9, \"orbit\": {{ \"au\": 1 }} }} ] }}", "two bodies"),
            ("{ \"bodies\": [ ", "not valid JSON"),
        })
        {
            try
            {
                StarSystemFile.Load(bad, "Bad.json");
                Fail($"system files: should be refused ({why}): {bad}");
            }
            catch (StarSystemException ex) when (ex.Message.Contains(why) && ex.Message.StartsWith("Bad.json"))
            {
            }
            catch (Exception ex)
            {
                Fail($"system files: gave \"{ex.Message}\", not a reason with \"{why}\"");
            }
        }

        // ---- No allocation per query ------------------------------------------------------------------------------------
        var sink = 0.0;
        var tq = times[2];
        for (var n = 0; n < 50; n++) // warm up
            sink += dev.PoseOf(new FrameRef(FrameKind.BodyFixed, moon), tq + n).Origin.X;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 2000; n++)
        {
            var pose = dev.PoseOf(frames[n % frames.Length], tq + n * 1000L);
            sink += pose.FromEcliptic(dev.PositionOf(moon, tq)).X + dev.BubbleOn(home, 1, 1, 0, tq).Origin.Y;
        }
        var alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        if (alloc > 0)
            Fail($"space queries allocated {alloc} B over 2,000 calls (should be 0)");
        if (double.IsNaN(sink))
            Fail("space queries gave NaN");
    }

    private static double _spaceFace;
}
