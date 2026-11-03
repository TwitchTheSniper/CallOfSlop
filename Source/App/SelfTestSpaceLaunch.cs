using System.Diagnostics;
using System.Numerics;
using CallOfSlop.Space;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Space launch" (docs/space/SPACE.md, "Leaving the bubble"): a real host and client over the transport with lag and
    /// loss, Sandbox on the Planetoid. The Orbiter blueprint builds; the host sits in it and holds Shift: the hover hold climbs
    /// along the local up, faster the longer it is held. Put just under the sky's edge going up and round, it leaves the
    /// bubble: it becomes a ship round the Planetoid with the construct's state, its physics sleeps, the driver stays
    /// seated and can't get out, and the client sees it where the host has it and an "In space" orbit line. Coasting, the
    /// construct follows the ship along its two-body orbit, level over the ground; the host's warp runs while the driver only
    /// coasts and drops to 1x on a burn; W adds speed along its heading and D turns it. Sent falling back, it comes in under
    /// the edge as a physics body with the ship's speed and the hover catches it; and the launch tick allocates nothing.
    /// </summary>
    private static void CheckSpaceLaunch()
    {
        StarSystems.Reload();
        if (Maps.Find("Planetoid") is not { Planetoid: { } pl } map || ConstructBuild.FindBlueprint("Orbiter") is not { } bp)
        {
            Fail("space launch: the Planetoid map or the Orbiter blueprint is missing");
            return;
        }
        const ushort port = 26517;
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
        PumpFast(server, [a], 0.3, ref now);
        if (a.LocalId == byte.MaxValue || server.Local is not { } me || server.Space?.Flight is not { } flight || a.Space == null)
        {
            Fail($"space launch: no host player, client or bubble (client {a.LocalId}, bubble {server.Space != null}/{a.Space != null})");
            return;
        }
        var lagged = server.Net.SimulateBadNetwork(40, 8, 3) & a.Net.SimulateBadNetwork(40, 8, 3);
        var problems = new List<string>();
        void Want(bool ok, string what)
        {
            if (!ok)
                problems.Add(what);
        }
        me.ImmuneUntil = now + 1e6;
        server.RequestRespawn();
        for (var wait = 0; wait < 40 && !me.Alive; wait++)
            PumpFast(server, [a], 0.1, ref now);
        if (!me.Alive)
        {
            Fail("space launch: the host's player didn't spawn");
            return;
        }
        foreach (var c0 in server.Constructs.Bodies.ToList())
            server.Constructs.Remove(c0);
        var centre = pl.Center;
        var edge = server.SpaceEdge;
        Want(edge > pl.Radius + 1000, $"the bubble has an edge above the ground ({edge:0} m from the centre, radius {pl.Radius:0})");

        // ---- Build it at the pole and sit in it ------------------------------------------------------------------------
        var pad = pl.GroundUnder(centre + new Vector3(20, (float)pl.Radius + 400, 20));
        var ship = ConstructBuild.Build(server.Constructs, bp, pad + new Vector3(0, 2.6f, 0), Quaternion.Identity, out var why);
        if (ship == null)
        {
            Fail($"space launch: the Orbiter didn't build: {why}");
            return;
        }
        ship.Rebuild();
        var seat = -1;
        for (var i = 0; i < ship.PartCount; i++)
            if (ship.Parts[i].D.Role == PartRole.Seat)
                seat = i;
        var lifters = 0;
        for (var k = 0; k < ship.ThrusterCount; k++)
            lifters += ship.Thrusters[k].Lifts ? 1 : 0;
        Want(seat >= 0 && ship.ThrusterCount == 6 && lifters == 4 && ship.Fuel > 100,
            $"the Orbiter has a seat, 6 jets (4 lifting) and fuel (seat {seat}, {ship.ThrusterCount} jets, {lifters} lifting, {ship.Fuel:0} l, {ship.Mass:0} kg)");
        if (seat < 0)
        {
            Fail($"space launch: {string.Join("; ", problems)}");
            return;
        }
        ref var hand = ref server.SandboxHands.Hand(me.Id);
        hand.SeatBody = ship.Id;
        hand.SeatPart = (ushort)seat;
        ship.Parts[seat].SitterPlus1 = (byte)(me.Id + 1);
        ship.Driver = me.Id;
        ship.DriverSeat = seat;
        ship.Wake();
        PumpFast(server, [a], 2.0, ref now);

        // ---- The planetoid's hover: Shift climbs along the local up, faster the longer it is held ------------------------
        var h0 = ConstructPhysics.HoldHeight(ship.WorldCom);
        server.HeldButtons = Buttons.Sprint;
        PumpFast(server, [a], 3.0, ref now);
        var h3 = ConstructPhysics.HoldHeight(ship.WorldCom);
        PumpFast(server, [a], 6.0, ref now);
        server.HeldButtons = Buttons.None;
        var h9 = ConstructPhysics.HoldHeight(ship.WorldCom);
        var tilt = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitY, ship.Rot), ConstructPhysics.HoldUp(ship.WorldCom)), -1, 1)) * 180 / MathF.PI;
        Line($"space launch: Shift held: {h3 - h0:0} m up in 3 s, {h9 - h0:0} m in 9 s (holding {ConstructPhysics.HoldShown(ship.HoverY):0} m, tilt {tilt:0.0} deg, thrust {ship.ThrustShow * 100:0}%)");
        Want(ship.HoverOn && h9 - h0 > 150 && h9 - h3 > 3 * (h3 - h0) && tilt < 5, $"Shift climbs faster the longer it is held, level ({h3 - h0:0} m in 3 s, {h9 - h0:0} m in 9 s, tilt {tilt:0.0})");
        Want(ConstructPhysics.AirAt(centre + new Vector3(0, edge, 0)) < 0.2f, "the air is thin at the sky's edge");

        // ---- Out of the bubble, flown: Shift and W on until it is past the edge -------------------------------------------
        var body = server.Space!.Bubble.Body;
        var mu = flight.System.Bodies[body].Mu;
        var r0 = edge - 40f;
        var around = (float)Math.Sqrt(mu / r0);
        var launchAt = centre + new Vector3(0, r0, 0);
        server.HeldButtons = Buttons.Sprint | Buttons.Forward;
        var flown = 0.0;
        var worstTilt = 0f;
        for (; flown < 50 && ship.SpaceShip == 0; flown += 0.25)
        {
            PumpFast(server, [a], 0.25, ref now);
            worstTilt = MathF.Max(worstTilt, MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitY, ship.Rot), ConstructPhysics.HoldUp(ship.WorldCom)), -1, 1)) * 180 / MathF.PI);
        }
        server.HeldButtons = Buttons.None;
        Line($"space launch: flown with Shift and W: out of the bubble after {flown:0.0} s more at {ConstructPhysics.HoldShown(ConstructPhysics.HoldHeight(ship.WorldCom)):0} m, {ship.Vel.Length():0} m/s, worst tilt {worstTilt:0.0} deg, {ship.Fuel:0} l left");
        Want(ship.SpaceShip != 0 && worstTilt < 15, $"Shift and W fly the Orbiter out of the bubble, level (in space {ship.SpaceShip != 0} after {flown:0.0} s, worst tilt {worstTilt:0.0} deg)");
        var si = flight.Ships.IndexOfId(ship.SpaceShip);
        Want(ship.SpaceShip != 0 && si >= 0 && flight.Ships.PrimaryOf(si) == body, $"past the edge the Orbiter is a ship round the Planetoid (ship {ship.SpaceShip}, index {si})");
        if (si < 0)
        {
            Fail($"space launch: {string.Join("; ", problems)}");
            return;
        }
        // Its state is the construct's: the same place, and the speed in the game plus the ground's turn.
        var pose = server.Space.Bubble.PoseAt(flight.Ships.Time);
        var shipAt = pose.FromEcliptic(flight.Ships.PositionOf(si));
        var shipVel = pose.VelocityFromEcliptic(flight.Ships.PositionOf(si), flight.Ships.VelocityOf(si));
        Want((shipAt - Vec3d.From(ship.WorldCom)).Length < 0.05 && (shipVel - Vec3d.From(ship.Vel)).Length < 0.05,
            $"the ship and its construct agree ({(shipAt - Vec3d.From(ship.WorldCom)).Length:0.000} m, {(shipVel - Vec3d.From(ship.Vel)).Length:0.000} m/s apart)");
        Want(server.SandboxSeated(me) && server.SpaceSeated(me), "the driver is still in the seat, in space");
        var refused = server.SelfTestAct(me, [(byte)CActKind.Stand]);
        Want(refused != null && server.SpaceSeated(me), $"you can't get out in space ({refused ?? "it let them out"})");

        // ---- Into orbit: W along the level heading until its lowest point clears the ground by a kilometre ----------------
        double Low(int i)
        {
            flight.Ships.RelativeOf(i, out _, out var r, out var v);
            var en = v.LengthSquared / 2 - mu / r.Length;
            var hh = Vec3d.Cross(r, v).Length;
            var ecc = Math.Sqrt(Math.Max(0, 1 + 2 * en * hh * hh / (mu * mu)));
            return hh * hh / mu / (1 + ecc) - pl.Radius;
        }
        server.HeldButtons = Buttons.Forward;
        var burnt = 0.0;
        for (; burnt < 20 && ship.SpaceShip != 0 && flight.Ships.IndexOfId(ship.SpaceShip) is var bi and >= 0 && Low(bi) < 1000; burnt += 0.05)
            PumpFast(server, [a], 0.05, ref now);
        server.HeldButtons = Buttons.None;
        PumpFast(server, [a], 1.0, ref now); // (the jets spool down)
        si = flight.Ships.IndexOfId(ship.SpaceShip);
        var low = si >= 0 ? Low(si) : double.NaN;
        Line($"space launch: W for {burnt:0.0} s in space: the orbit's lowest point {low / 1000:0.00} km up, {ship.Vel.Length():0} m/s");
        Want(si >= 0 && low > 900, $"W puts it in orbit (its lowest point {low:0} m up after {burnt:0.0} s)");
        if (si < 0)
        {
            Fail($"space launch: {string.Join("; ", problems)}");
            return;
        }

        // ---- Coasting: the construct follows the ship's orbit, level; warp runs ------------------------------------------
        flight.Ships.RelativeOf(si, out _, out var rr, out var rv);
        var tStart = flight.Ships.Time;
        var track = new List<Vector3>(512);
        for (var k = 0; k < 120; k++)
        {
            PumpFast(server, [a], 0.05, ref now);
            track.Add(ship.WorldCom); // (the host's path: the client, a lagged round trip behind in this sped-up run, draws a point on it)
        }
        si = flight.Ships.IndexOfId(ship.SpaceShip);
        var t1 = flight.Ships.Time;
        UniversalKepler.PropagateNs(mu, rr, rv, t1.Ns - tStart.Ns, out var want, out _);
        var bodyNow = flight.Ships.BodyPosition(body);
        pose = server.Space.Bubble.PoseAt(t1);
        var drawn = pose.ToEcliptic(Vec3d.From(ship.WorldCom)) - bodyNow;
        var off = (drawn - want).Length;
        var height = Vector3.Distance(ship.WorldCom, centre) - (float)pl.Radius;
        tilt = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Transform(Vector3.UnitY, ship.Rot), Vector3.Normalize(ship.WorldCom - centre)), -1, 1)) * 180 / MathF.PI;
        Line($"space launch: 6 s coasting: {height:0} m up, {off:0.000} m from the two-body orbit, tilt {tilt:0.0} deg, mode {(si >= 0 ? flight.Ships.ModeOf(si) : default)}");
        Want(si >= 0 && off < 2 && tilt < 1, $"the coasting construct follows its orbit, level ({off:0.000} m off, tilt {tilt:0.0} deg)");
        Want(ship.SpaceShip != 0 && server.SandboxSeated(me), "still in space and seated");
        // The client sees it where the host has it (a pose behind, at most), and its HUD says it is in space.
        var copy = a.Constructs.Get(ship.Id);
        var seen = float.MaxValue;
        if (copy != null)
            for (var k = 1; k < track.Count; k++)
                seen = MathF.Min(seen, SegmentDistance(copy.WorldCom, track[k - 1], track[k]));
        var line = copy == null ? null : a.SpaceOrbitLine(copy);
        Line($"space launch: the client's Orbiter {seen:0.00} m from the host's path ({ship.Vel.Length():0} m/s); its line: {line ?? "none"}; lag and loss {(lagged ? "on" : "NOT on")}");
        Want(seen < 1.5f, $"the client draws the ship on the host's path ({seen:0.00} m off it)");
        Want(line != null && line.StartsWith("In space"), $"the client's driver line says it is in space ({line ?? "none"})");
        // Warp while coasting; a burn drops it to 1x.
        server.RequestSpaceWarp(50);
        PumpFast(server, [a], 1.0, ref now);
        var coastWarp = flight.Clock.Warp;
        server.RequestSpaceWarp(1);
        PumpFast(server, [a], 0.2, ref now);
        si = flight.Ships.IndexOfId(ship.SpaceShip);
        var e0 = si >= 0 ? flight.Ships.KeplerEnergy(si) : 0;
        var fuel0 = ship.Fuel;
        server.HeldButtons = Buttons.Forward;
        PumpFast(server, [a], 2.0, ref now);
        var burnBusy = server.SpaceAnyoneBusy();
        server.HeldButtons = Buttons.Right;
        var yaw0 = Vector3.Transform(-Vector3.UnitZ, ship.Rot);
        PumpFast(server, [a], 1.0, ref now);
        server.HeldButtons = Buttons.None;
        PumpFast(server, [a], 1.0, ref now);
        var yaw1 = Vector3.Transform(-Vector3.UnitZ, ship.Rot);
        si = flight.Ships.IndexOfId(ship.SpaceShip);
        var e1 = si >= 0 ? flight.Ships.KeplerEnergy(si) : 0;
        var turned = MathF.Acos(Math.Clamp(Vector3.Dot(yaw0, yaw1), -1, 1)) * 180 / MathF.PI;
        Line($"space launch: warp coasting {coastWarp}x; W for 2 s: energy {e0:0} to {e1:0} J/kg, {fuel0 - ship.Fuel:0.0} l burnt, busy {burnBusy}; D for 1 s turned it {turned:0} deg");
        Want(coastWarp == 50, $"the host's warp runs while the driver coasts ({coastWarp}x)");
        Want(burnBusy && Math.Abs(e1 - e0) > 500 && fuel0 - ship.Fuel > 0.1f, $"W burns: the orbit changes, fuel goes and the warp holds at 1x (energy {e0:0} to {e1:0}, busy {burnBusy})");
        Want(turned > 25 && turned < 70, $"D turns the ship ({turned:0} deg in 1 s)");

        // ---- Back in -----------------------------------------------------------------------------------------------------
        if (si >= 0)
        {
            flight.Ships.RelativeOf(si, out var pb, out var nr, out _);
            flight.Ships.SetState(si, pb, nr.Normalized() * (edge + 100), nr.Normalized() * -150); // (100 m over the edge, falling straight back)
        }
        for (var wait = 0; wait < 200 && ship.SpaceShip != 0; wait++)
            PumpFast(server, [a], 0.05, ref now);
        var back = Vector3.Distance(ship.WorldCom, centre);
        var inSpeed = Vector3.Dot(ship.Vel, Vector3.Normalize(ship.WorldCom - centre));
        Line($"space launch: back in {back - pl.Radius:0} m up, {inSpeed:0} m/s along up, hover {ship.HoverOn}, ships left {flight.Ships.Count}");
        Want(ship.SpaceShip == 0 && back < edge - Game.SpaceEdgeHysteresis + 5 && inSpeed < -100 && ship.HoverOn && flight.Ships.Count == 0,
            $"falling back under the edge it is a physics body with the ship's speed, the hover on, the ship gone (ship {ship.SpaceShip}, {inSpeed:0} m/s, hover {ship.HoverOn}, {flight.Ships.Count} ships)");
        PumpFast(server, [a], 6.0, ref now);
        var caught = Vector3.Dot(ship.Vel, Vector3.Normalize(ship.WorldCom - centre));
        Want(MathF.Abs(caught) < 20 && server.SandboxSeated(me), $"the hover catches it after it comes in ({caught:0} m/s along up after 6 s)");

        // ---- No allocation in the launch tick with a ship out --------------------------------------------------------------
        ship.Pos = launchAt + new Vector3(0, 200, 0) - Vector3.Transform(ship.Com, ship.Rot);
        ship.Vel = new Vector3(around, 0, 0);
        ship.Wake();
        for (var wait = 0; wait < 40 && ship.SpaceShip == 0; wait++)
            PumpFast(server, [a], 0.05, ref now);
        server.SpaceLaunchBefore(flight);
        flight.Tick();
        server.SpaceLaunchAfter(flight);
        var alloc = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 300; k++)
        {
            server.SpaceLaunchBefore(flight);
            flight.Tick();
            server.SpaceLaunchAfter(flight);
        }
        alloc = GC.GetAllocatedBytesForCurrentThread() - alloc;
        Want(ship.SpaceShip != 0 && alloc == 0, $"the launch tick with a ship out allocates nothing ({alloc} B in 300 ticks, ship {ship.SpaceShip})");

        foreach (var p in problems)
            Fail($"space launch: {p}");
    }

    private static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var t = ab.LengthSquared() > 1e-9f ? Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1) : 0;
        return Vector3.Distance(p, a + ab * t);
    }
}
