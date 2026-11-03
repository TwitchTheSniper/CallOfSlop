using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Space bubble" (phase 3, docs/space/SPACE.md "The bubble"): the Planetoid map is the Dev system's Planetoid (same
    /// centre and radius); the sun's direction in the game is the star's real way (straight up at the sub-solar point,
    /// straight down at its antipode, the same point <c>--space-sim</c> prints); the Dev system starts in the Planetoid's
    /// northern summer so its polar spawn has the sun up; the equator has two sunrises and two sunsets in 48 h; the
    /// moonlet is where and as big as its orbit says; the sky follows the bubble and gives its own sun back. Then a real
    /// host and client over the transport with lag and loss: the client's bubble and clock match the host's, the host's
    /// warp reaches it and drops to 1x while a construct is awake (the provisional rule), a client can't warp, 40 ships
    /// (three packets' worth) reach it within a metre, removed ships go, a late joiner gets everything; a map in no system
    /// has no bubble; and the host's space tick, the ships packet and the sky allocate nothing.
    /// </summary>
    private static void CheckSpaceBubble()
    {
        StarSystems.Reload();
        if (Maps.Find("Planetoid") is not { Planetoid: { } pl } map)
        {
            Fail("space bubble: the Planetoid map is missing");
            return;
        }
        if (!StarSystems.ForMap(map.Name, out var entry, out var body))
        {
            Fail($"space bubble: no star system names the Planetoid map ({string.Join("; ", StarSystems.Errors)})");
            return;
        }
        var sys = entry.System;
        var bubble = new SpaceBubble(sys, body);
        Line($"space bubble: {map.Name} is {sys.Bodies[body].Name} in {sys.Name} ({sys.File}, hash {entry.Hash:x8}); moon: {(bubble.Moon >= 0 ? sys.Bodies[bubble.Moon].Name : "none")}");

        // ---- A system's start time: read, and refused with a reason when bad -------------------------------------------
        const string mini = "\"bodies\": [{ \"name\": \"S\", \"gm\": 1e20, \"radius\": 1e8 }]";
        if (StarSystemFile.Load($"{{ \"start\": {{ \"hours\": 30 }}, {mini} }}", "start.json").Start.Ns != 30 * 3600 * SpaceTime.NsPerSecond)
            Fail("space bubble: a system's \"start\": { \"hours\": 30 } wasn't read as 30 hours");
        foreach (var bad in new[] { "{ \"days\": -1 }", "{ \"days\": 1, \"hours\": 2 }", "{ \"weeks\": 1 }", "12" })
            try
            {
                StarSystemFile.Load($"{{ \"start\": {bad}, {mini} }}", "bad.json");
                Fail($"space bubble: a system with \"start\": {bad} should be refused");
            }
            catch (StarSystemException)
            {
            }

        // ---- The map is the body ---------------------------------------------------------------------------------------
        var t0 = sys.Start;
        var centre = bubble.BodyInGame(body, t0);
        if ((centre - Vec3d.From(pl.Center)).Length > 1e-3 || Math.Abs(sys.Bodies[body].Radius - pl.Radius) > 1e-6
            || (bubble.GameCentre - Vec3d.From(map.PlanetCenter)).Length > 1e-3)
            Fail($"space bubble: the body's centre in the game {centre} (radius {sys.Bodies[body].Radius}) isn't the map's planetoid centre {pl.Center} (radius {pl.Radius})");

        // ---- The sun ---------------------------------------------------------------------------------------------------
        // Independently of the bubble's own maths: the sub-solar point in the body's turning frame (as --space-sim works it
        // out), put on the game's ground with the bubble's axes, has the sun straight overhead; its antipode straight under.
        foreach (var hours in new[] { 0.0, 5.5, 17.25, 24 * 40 + 3 })
        {
            var t = t0 + (long)(hours * 3600 * SpaceTime.NsPerSecond);
            var fixedPose = sys.PoseOf(new FrameRef(FrameKind.BodyFixed, body), t);
            var sub = fixedPose.Rot.InverseRotate(SpacePos.Origin - sys.PositionOf(body, t)).Normalized();
            var (aUp, aNorth, aEast) = StarSystem.SurfaceAxes(bubble.Latitude, bubble.Longitude);
            Vector3 OnGround(Vec3d dirFixed) => (bubble.GameCentre + new Vec3d(Vec3d.Dot(dirFixed, aEast), Vec3d.Dot(dirFixed, aUp), -Vec3d.Dot(dirFixed, aNorth)) * (pl.Radius + 2)).ToVector3();
            var over = bubble.SunElevation(t, OnGround(sub));
            var under = bubble.SunElevation(t, OnGround(-sub));
            if (over < 89.9 || under > -89.9)
                Fail($"space bubble: {hours} h in, the sun should be overhead at the sub-solar point (lat {Math.Asin(sub.Z) * 180 / Math.PI:0.0}) and underfoot at its antipode (elevations {over:0.00} and {under:0.00})");
        }
        var pole = new Vector3(0, 2, 0);
        var poleSun = bubble.SunElevation(t0, pole);
        Line($"space bubble: at the start ({t0}) the sun stands {poleSun:0.0} degrees over the polar spawn");
        if (poleSun < 20)
            Fail($"space bubble: the Dev system should start in the Planetoid's northern summer (the sun {poleSun:0.0} degrees over the pole, want 20 or more)");
        // The equator: two sunrises and two sunsets in 48 hours, the sun high at noon and low at midnight.
        var equator = new Vector3((float)(pl.Radius + 2), (float)bubble.GameCentre.Y, 0);
        int rises = 0, sets = 0;
        double hi = -90, lo = 90, prev = bubble.SunElevation(t0, equator);
        for (var m = 10; m <= 48 * 60; m += 10)
        {
            var e = bubble.SunElevation(t0 + m * 60 * SpaceTime.NsPerSecond, equator);
            if (prev < 0 && e >= 0)
                rises++;
            if (prev >= 0 && e < 0)
                sets++;
            hi = Math.Max(hi, e);
            lo = Math.Min(lo, e);
            prev = e;
        }
        Line($"space bubble: on the equator over 48 h: {rises} sunrises, {sets} sunsets, the sun from {lo:0} to {hi:0} degrees");
        if (rises != 2 || sets != 2 || hi < 60 || lo > -60)
            Fail($"space bubble: the equator should see two sunrises and two sunsets in 48 h with a high noon sun ({rises}, {sets}, {lo:0} to {hi:0} degrees)");

        // ---- The moon --------------------------------------------------------------------------------------------------
        if (bubble.Moon < 0 || !bubble.Look(bubble.Moon, t0, pole, out var moonDir, out var moonDist, out var moonAr))
            Fail("space bubble: the Planetoid's moonlet should be its sky's moon");
        else
        {
            var mb = sys.Bodies[bubble.Moon];
            var fromCentre = (bubble.BodyInGame(bubble.Moon, t0) - bubble.GameCentre).Length;
            Line($"space bubble: the moonlet {moonDist / 1000:0.0} km from the pole, {fromCentre / 1000:0.0} km from the centre, {2 * moonAr * 180 / Math.PI:0.00} degrees across");
            if (fromCentre < mb.Orbit.Periapsis - 1 || fromCentre > mb.Orbit.Apoapsis + 1 || Math.Abs(Math.Sin(moonAr) * moonDist - mb.Radius) > 1e-3 || Math.Abs(moonDir.Length() - 1) > 1e-5)
                Fail($"space bubble: the moonlet isn't where its orbit puts it ({fromCentre:0} m from the centre, orbit {mb.Orbit.Periapsis:0}-{mb.Orbit.Apoapsis:0} m)");
        }

        // ---- The sky follows, and gives its sun back -----------------------------------------------------------------
        SpaceBubbleSky(map, sys, entry.Hash, bubble);

        // ---- A map in no system ----------------------------------------------------------------------------------------
        {
            var now = 0.0;
            var plain = Maps.All.First(m => !StarSystems.ForMap(m.Name, out _, out _));
            using var g = ModeGame(0, GameMode.FreeForAll, plain.Id);
            Run(g, ref now, 0.2);
            if (g.Space != null)
                Fail($"space bubble: {plain.Name} is in no system but has a bubble");
        }

        SpaceBubbleNet(map, pl, entry);
    }

    /// <summary>The sky takes the bubble's sun and moon, gives the map's own back after, and allocates nothing a frame.</summary>
    private static void SpaceBubbleSky(Map map, StarSystem sys, uint hash, SpaceBubble bubble)
    {
        if (map.Skybox is not { } sky)
        {
            Fail("space bubble: the Planetoid map has no sky to follow the sun");
            return;
        }
        var before = (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize);
        var session = new SpaceSession(bubble, hash, map.Name, new SpaceFlight(sys, 4, sys.Start));
        var cam = new Vector3(3, 4, -2);
        SpaceSky.Update(map, session, sys.Start, cam);
        var want = bubble.SunDirection(sys.Start, cam);
        if (!SpaceSky.Active || Vector3.Distance(sky.SunDir, want) > 1e-5f || !sky.Moon || sky.MoonSize is < 1f or > 3f)
            Fail($"space bubble: the sky didn't take the bubble's sun and moon (sun {sky.SunDir} want {want}, moon {sky.Moon} {sky.MoonSize:0.00} degrees)");
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 600; k++)
            SpaceSky.Update(map, session, sys.Start + k * SpaceTime.NsPerSecond, cam);
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        if (alloc != 0)
            Fail($"space bubble: the sky following the bubble allocated {alloc} B in 600 frames");
        SpaceSky.Update(map, null, default, cam); // no bubble: the map's own sky again
        if (SpaceSky.Active || (sky.SunDirection, sky.MoonDirection, sky.Moon, sky.MoonSize) != before)
            Fail("space bubble: the map's own sun and moon didn't come back when the bubble went");
    }

    /// <summary>A real host and client (and a late joiner) over the transport with lag and loss: bubble, clock, warp, ships.</summary>
    private static void SpaceBubbleNet(Map map, Planetoid pl, StarSystems.Entry entry)
    {
        const ushort port = 26511;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        map.RestoreProps();
        World.SetMap(map);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: map.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue)
        {
            Fail("space bubble: the client didn't join");
            return;
        }
        var lagged = server.Net.SimulateBadNetwork(60, 12, 5) & a.Net.SimulateBadNetwork(60, 12, 5);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        // Clean ground: nothing awake that would hold the warp at 1x.
        foreach (var c in server.Constructs.Bodies.ToList())
            server.Constructs.Remove(c);
        Pump(server, [a], 1.0, ref now, clock);
        var hs = server.Space;
        Want(hs?.Flight != null && hs.Bubble.Body == server.Space!.System.IndexOf("Planetoid"), "the host has the Planetoid's bubble");
        Want(a.Space is { IsHost: false } cs && cs.Hash == entry.Hash && cs.Bubble.Body == hs?.Bubble.Body && ReferenceEquals(cs.System, entry.System),
            $"the client has the same bubble ({(a.Space == null ? "none" : $"{a.Space.System.File} body {a.Space.Bubble.Body}")})");
        if (hs?.Flight is not { } flight || a.Space is not { } cl)
        {
            Fail($"space bubble net: {string.Join("; ", problems)}");
            return;
        }
        double Gap() => Math.Abs(cl.Now - hs.Now);
        Line($"space bubble net: lag and loss {(lagged ? "on" : "NOT on")}; host time {hs.Now}, the client {Gap():0.000} s apart at 1x");
        Want(Gap() < 0.3, $"at 1x the client's clock is within 0.3 s of the host's ({Gap():0.000} s)");
        Want(!a.RequestSpaceWarp(1000) && cl.Warp == 1, "a client can't set the warp");

        // Warp from the host, then a construct falling keeps it at 1x (the provisional rule), then it sleeps and warp is back.
        server.RequestSpaceWarp(1000);
        Pump(server, [a], 1.0, ref now, clock);
        Want(flight.Clock.Warp == 1000 && cl.Warp == 1000, $"the host's 1000x reaches the client (host {flight.Clock.Warp}x, client {cl.Warp}x, someone busy {server.SpaceAnyoneBusy()})");
        Want(Gap() < 300, $"at 1000x the client's clock is within 300 s of space time of the host's ({Gap():0.0} s)");
        var ground = pl.GroundUnder(pl.Center + new Vector3(0, (float)pl.Radius + 400, 0)) + new Vector3(6, 0, 6);
        var block = ConstructBuild.PlaceNew(server.Constructs, ConstructParts.Need("Frame Block"), ground + new Vector3(0, 3, 0), 0);
        block?.Wake();
        Pump(server, [a], 0.5, ref now, clock);
        var busyWarp = flight.Clock.Warp;
        var clientBusyWarp = cl.Warp;
        for (var wait = 0; wait < 24 && server.Constructs.Bodies.Any(c => !c.Asleep); wait++)
            Pump(server, [a], 0.25, ref now, clock);
        Pump(server, [a], 0.6, ref now, clock);
        Line($"space bubble net: 1000x asked; a block falling: host {busyWarp}x, client {clientBusyWarp}x; once it sleeps: host {flight.Clock.Warp}x, client {cl.Warp}x");
        Want(block != null && busyWarp == 1 && clientBusyWarp == 1, "an awake construct holds the warp at 1x on host and client");
        Want(flight.Clock.Warp == 1000 && cl.Warp == 1000, "the warp comes back once everything sleeps");
        server.RequestSpaceWarp(1);
        Pump(server, [a], 0.6, ref now, clock);

        // Ships: 40 in orbit round the Planetoid (three packets' worth), checked at the host's time within a metre.
        var mu = flight.System.Bodies[hs.Bubble.Body].Mu;
        for (var k = 0; k < 40; k++)
        {
            var r = 14_000.0 + k * 400;
            var (s, c) = Math.SinCos(k * 0.7);
            var inc = k * 0.05;
            var pos = new Vec3d(r * c, r * s * Math.Cos(inc), r * s * Math.Sin(inc));
            var speed = Math.Sqrt(mu / r);
            var vel = new Vec3d(-speed * s, speed * c * Math.Cos(inc), speed * c * Math.Sin(inc));
            flight.Ships.AddRelative(hs.Bubble.Body, pos, vel);
        }
        Pump(server, [a], 2.5, ref now, clock);
        double ShipsWorst(out int missing)
        {
            missing = 0;
            var worst = 0.0;
            var t = hs.Now;
            for (var i = 0; i < flight.Ships.Count; i++)
            {
                if (!cl.SampleShip(flight.Ships.Ids[i], t, out var p, out _))
                {
                    missing++;
                    continue;
                }
                worst = Math.Max(worst, (p - flight.Ships.PositionOf(i)).Length);
            }
            return worst;
        }
        var worst1 = ShipsWorst(out var missing1);
        Line($"space bubble net: 40 ships: the client knows {cl.Ships.Count}, {missing1} missing, the worst {worst1:0.000} m from the host's");
        Want(missing1 == 0 && cl.Ships.Count == 40 && worst1 < 1.0, $"every ship reaches the client within a metre ({missing1} missing, {cl.Ships.Count} known, worst {worst1:0.000} m)");
        for (var k = 0; k < 5; k++)
            flight.Ships.Remove(0);
        Pump(server, [a], 4.0, ref now, clock);
        Want(cl.Ships.Count == 35, $"ships the host removed go from the client ({cl.Ships.Count} left, want 35)");

        // A late joiner.
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        b.Net.SimulateBadNetwork(60, 12, 5);
        for (var wait = 0; wait < 24 && (b.Space == null || b.Space.Ships.Count < 35); wait++)
            Pump(server, [a, b], 0.25, ref now, clock);
        Pump(server, [a, b], 0.5, ref now, clock);
        Want(b.Space is { } bs && bs.Bubble.Body == hs.Bubble.Body && Math.Abs(bs.Now - hs.Now) < 0.3 && bs.Ships.Count == 35,
            $"a late joiner gets the bubble, the clock and the ships ({(b.Space is { } x ? $"{x.Ships.Count} ships, {Math.Abs(x.Now - hs.Now):0.000} s apart" : "no bubble")})");

        // The host's space tick and the ships packet allocate nothing.
        var w = new PacketWriter();
        var cursor = 0;
        server.SpaceAnyoneBusy();
        flight.Tick();
        Game.WriteSpaceShips(w, flight.Ships, ref cursor, 1);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 300; k++)
        {
            flight.AnyoneBusy = server.SpaceAnyoneBusy();
            flight.Tick();
            Game.WriteSpaceShips(w, flight.Ships, ref cursor, (uint)k);
        }
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        Want(alloc == 0, $"the host's space tick and ships packets allocate nothing ({alloc} B in 300 ticks)");

        foreach (var p in problems)
            Fail($"space bubble net: {p}");
    }
}
