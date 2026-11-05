using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>One rider's exit, as one machine saw it: the numbers a case is judged on.</summary>
    private sealed class ExitLog
    {
        public bool Out, FirstRode, Rode = true;
        public double OutAt;
        public Vector3 FirstVel, Spot, Last;
        public bool HasSpot, HasLast;
        public float Drift, Step, Back;
        public int Samples;

        public void Sample(bool seated, bool riding, Vector3 vel, double t)
        {
            if (seated)
                return;
            if (!Out)
                (Out, OutAt, FirstRode, FirstVel) = (true, t, riding, vel);
            Rode &= riding;
        }

        /// <summary>A rider's feet in the deck's frame, every frame once it has settled: no drift, no jumps.</summary>
        public void Track(Vector3 local)
        {
            Samples++;
            if (!HasSpot)
                (HasSpot, Spot) = (true, local);
            Drift = MathF.Max(Drift, Vector3.Distance(local, Spot));
            if (HasLast)
                Step = MathF.Max(Step, Vector3.Distance(local, Last));
            (HasLast, Last) = (true, local);
        }

        public void Forward(Vector3 at, Vector3 along)
        {
            if (HasLast)
                Back = MathF.Max(Back, -Vector3.Dot(at - Last, along));
            (HasLast, Last) = (true, at);
            Samples++;
        }
    }

    /// <summary>
    /// "Ride frame", getting out of a seat on the move (ConstructRideExit.cs): over the real transport with lag, jitter and
    /// loss, a host and a client get out of seats on a deck driven at speed (straight, turning, climbing while it rolls, a
    /// turret's seat that turns, a seat welded on off grid) and stand on it from their first tick; a third peer watches.
    /// With nothing to stand on they leave at the seat's speed and fall. Then a real buggy, on its physics.
    /// </summary>
    private static void RideSeatExit(Map yard)
    {
        const ushort port = 26393;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(yard);
        var hostPeer = NetPeer.Host(port, 4);
        using var server = new Game(hostPeer, new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        var peerA = NetPeer.Join("127.0.0.1", port);
        using var a = new Game(peerA, new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        var peerB = NetPeer.Join("127.0.0.1", port);
        using var b = new Game(peerB, new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] clients = [a, b];
        Pump(server, clients, 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, clients, 0.25, ref now);
        Pump(server, clients, 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp) || server.Local is not { } host)
        {
            Fail("seat exit: the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        if (!host.Alive)
            server.SelfTestRespawn(host, now);
        sp.ImmuneUntil = host.ImmuneUntil = 1e9;
        var bad = hostPeer.SimulateBadNetwork(75, 10, 2) & peerA.SimulateBadNetwork(75, 10, 2) & peerB.SimulateBadNetwork(75, 10, 2);
        Line($"seat exit (wire): {(bad ? "150 ms lag, 10 ms jitter, 2% loss" : "no lag simulator in this RakNet build")}");
        var w = server.Constructs;

        // Motions: where the deck is t seconds in, as the physics would put it.
        (Vector3 Pos, Quaternion Rot, Vector3 Vel, Vector3 Ang) Straight(Vector3 p0, Quaternion r0, float t) => (p0 + new Vector3(12, 0, 0) * t, r0, new Vector3(12, 0, 0), Vector3.Zero);
        (Vector3, Quaternion, Vector3, Vector3) Turn(Vector3 p0, Quaternion r0, float t)
        {
            const float radius = 14f, om = 0.75f;
            var pivot = p0 + new Vector3(radius, 0, 0);
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, om * t);
            var ang = new Vector3(0, om, 0);
            var pos = pivot + Vector3.Transform(p0 - pivot, turn);
            return (pos, Quaternion.Normalize(turn * r0), Vector3.Cross(ang, pos - pivot), ang);
        }
        (Vector3, Quaternion, Vector3, Vector3) ClimbRoll(Vector3 p0, Quaternion r0, float t)
        {
            const float rollRate = 0.35f, pitch = 0.3f;
            var v = new Vector3(10, 3.1f, 0);
            var climb = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, pitch);
            var along = Vector3.Normalize(v);
            var roll = Quaternion.CreateFromAxisAngle(along, rollRate * t);
            return (p0 + v * t, Quaternion.Normalize(roll * climb * r0), v, along * rollRate);
        }

        // A deck with the two seats on it: Fit's own failure says what went wrong.
        (Construct, int, int)? PlainDeck(float z)
        {
            if (RideDeck(w, -19, z, 25f, plates: 3) is not { } c)
                return null;
            var s0 = Fit(w, c, 0, "top11", "Seat", "bottom");
            var s1 = Fit(w, c, 2, "top11", "Seat", "bottom");
            return s0 < 0 || s1 < 0 ? null : (c, s0, s1);
        }
        (Construct, int, int)? TurretDeck(float z)
        {
            if (RideDeck(w, -19, z, 25f, plates: 3) is not { } c)
                return null;
            var tt = Fit(w, c, 0, "top11", "Turntable", "base");
            var s0 = tt < 0 ? -1 : Fit(w, c, tt, "top", "Seat", "bottom");
            var plate = 2;
            var pp = c.Parts[plate];
            var s1 = s0 < 0 ? -1 : w.AddFreeform(c, plate, ConstructParts.Need("Seat"), pp.Pos + new Vector3(0.2f, 0.07f, 0.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f));
            c.Rebuild();
            return s0 < 0 || s1 < 0 ? null : (c, s0, s1);
        }
        (Construct, int, int)? BareSeats(float z)
        {
            if (RideDeck(w, -19, z, 25f, plates: 1, part: "Frame Block") is not { } c)
                return null;
            var s0 = Fit(w, c, 0, "+y", "Seat", "bottom");
            return s0 < 0 ? null : (c, s0, s0);
        }

        bool Case(string name, (Construct, int, int)? built, Func<Vector3, Quaternion, float, (Vector3, Quaternion, Vector3, Vector3)> move, bool onDeck, bool turret = false, Construct? hostBody = null)
        {
            if (built is not { } v)
                return false;
            var (c, hostSeat, clientSeat) = v;
            var hc = hostBody ?? c; // (over the side: a seat each, a body each, or one would stand on the other's)
            w.StructureDirty.Add(c.Id);
            w.StructureDirty.Add(hc.Id);
            Pump(server, clients, 0.4, ref now, clock);
            var pos0 = c.Pos;
            var rot0 = c.Rot;
            var hostFrom = hc.Pos - c.Pos;
            var eye = ConstructEdits.SeatFeet(hc, hostSeat) + new Vector3(0, 1.2f, 0);
            var why = server.SandboxHands.Use(w, host.Id, eye, hc.Id, hostSeat);
            eye = ConstructEdits.SeatFeet(c, clientSeat) + new Vector3(0, 1.2f, 0);
            why ??= server.SandboxHands.Use(w, sp.Id, eye, c.Id, clientSeat);
            if (why != null)
            {
                Fail($"seat exit ({name}): couldn't sit: {why}");
                return false;
            }
            var start = clock.Elapsed.TotalSeconds;
            void Drive()
            {
                var t = (float)(clock.Elapsed.TotalSeconds - start);
                var (pos, rot, vel, ang) = move(pos0, rot0, t);
                c.Pos = pos;
                c.Rot = rot;
                c.Vel = vel;
                c.AngVel = ang;
                c.Asleep = false;
                c.Anchored = false;
                if (hc != c)
                {
                    hc.Pos = pos + hostFrom;
                    hc.Vel = vel;
                    hc.Asleep = hc.Anchored = false;
                }
                if (turret)
                {
                    ref var j = ref c.Joints[0];
                    j.Angle = j.Ref = 0.8f * t;
                    j.RefSet = true;
                    j.Vel = 0.8f;
                }
            }
            PumpWith(server, clients, 1.2, ref now, clock, Drive);
            if (!server.SandboxHands.Hand(sp.Id).Seated || !a.SandboxHands.Hand(a.LocalId).Seated)
            {
                Fail($"seat exit ({name}): the client didn't sit (server {server.SandboxHands.Hand(sp.Id).Seated}, client {a.SandboxHands.Hand(a.LocalId).Seated})");
                return false;
            }
            server.SandboxSend(CActKind.Stand);
            a.SandboxSend(CActKind.Stand);
            ExitLog hs = new(), cs = new(), cc = new(), hv = new(), cw = new(), hw = new();
            var agree = 0f;
            var along = c.Vel.LengthSquared() > 1 ? Vector3.Normalize(c.Vel) : Vector3.UnitX;
            var clientVel = c.PointVel(ConstructEdits.SeatFeet(c, clientSeat));
            var until = clock.Elapsed.TotalSeconds + 2.4;
            var thrown0 = ConstructRide.Throws;
            float? awayAt1 = null;
            while (clock.Elapsed.TotalSeconds < until)
            {
                PumpWith(server, clients, 0.004, ref now, clock, Drive);
                var t = clock.Elapsed.TotalSeconds;
                var me = a.Local;
                if (!onDeck && cs.Out && t > cs.OutAt + 0.7 && awayAt1 == null)
                    awayAt1 = Vector3.Dot(sp.Position - c.Pos, along);
                var ccopy = a.Constructs.Get(c.Id);
                hs.Sample(server.SandboxHands.Hand(host.Id).Seated, host.Sim.RideBody == hc.Id, host.Sim.Velocity + (host.Sim.RideBody != 0 ? c.PointVel(host.Position) : Vector3.Zero), t);
                cs.Sample(server.SandboxHands.Hand(sp.Id).Seated, sp.Sim.RideBody == c.Id, sp.Sim.Velocity + (sp.Sim.RideBody != 0 ? c.PointVel(sp.Position) : Vector3.Zero), t);
                if (me == null || ccopy == null)
                    continue;
                cc.Sample(a.SandboxHands.Hand(a.LocalId).Seated, me.Sim.RideBody == c.Id, me.Sim.Velocity, t);
                if (b.Players.TryGetValue(sp.Id, out var seen) && b.Constructs.Get(c.Id) is { } bcopy && cs.Out && t > cs.OutAt + 0.6)
                {
                    if (onDeck)
                    {
                        if (seen.SnapRideBody == c.Id)
                            cw.Track(b.SelfTestDrawnLocal(bcopy, seen.SnapRideSeg, seen.RenderPosition));
                    }
                    else
                        cw.Forward(seen.RenderPosition, along);
                }
                if (b.Players.TryGetValue(host.Id, out var seenHost) && b.Constructs.Get(c.Id) is { } bcopy2 && hs.Out && t > hs.OutAt + 0.6 && onDeck && seenHost.SnapRideBody == c.Id)
                    hw.Track(b.SelfTestDrawnLocal(bcopy2, seenHost.SnapRideSeg, seenHost.RenderPosition));
                if (onDeck)
                {
                    if (cc.Out && t > cc.OutAt + 0.4 && me.Sim.RideBody == c.Id)
                    {
                        hv.Track(a.SelfTestDrawnLocal(ccopy, me.Sim.RideSeg, me.RenderPosition));
                        if (sp.Sim.RideBody == c.Id && sp.Sim.RideSeg == me.Sim.RideSeg)
                            agree = MathF.Max(agree, Vector3.Distance(me.Sim.RideLocal with { Y = 0 }, sp.Sim.RideLocal with { Y = 0 }));
                    }
                }
                else if (cc.Out && t > cc.OutAt + 0.4)
                    hv.Forward(me.RenderPosition, along);
            }
            var thrown = ConstructRide.Throws - thrown0;
            if (onDeck)
            {
                Line($"seat exit ({name}): server first tick rode host {hs.FirstRode} client {cs.FirstRode}, client saw {cc.FirstRode}; all along host {hs.Rode} client {cs.Rode}/{cc.Rode}; on the deck for 2 s: client's own {hv.Drift * 100:0.0} cm (step {hv.Step * 100:0.0}), the watcher's of the client {cw.Drift * 100:0.0} (step {cw.Step * 100:0.0}), of the host {hw.Drift * 100:0.0} (step {hw.Step * 100:0.0}), client vs server {agree * 100:0.0} cm, {hv.Samples}/{cw.Samples}/{hw.Samples} samples, {thrown} thrown");
                if (!hs.Out || !cs.Out || !cc.Out)
                    Fail($"seat exit ({name}): someone didn't get out (host {hs.Out}, client {cs.Out}, client's copy {cc.Out})");
                if (!hs.FirstRode || !cs.FirstRode || !cc.FirstRode)
                    Fail($"seat exit ({name}): a rider wasn't in the deck's frame on their first tick out (host {hs.FirstRode}, client {cs.FirstRode}, the client's prediction {cc.FirstRode})");
                if (!hs.Rode || !cs.Rode || !cc.Rode || thrown > 0)
                    Fail($"seat exit ({name}): a rider came off the deck (host {hs.Rode}, client {cs.Rode}, the client's prediction {cc.Rode}, {thrown} thrown)");
                if (hv.Drift > 0.06f || hv.Step > 0.03f)
                    Fail($"seat exit ({name}): the client's feet moved on the deck as they stood: {hv.Drift * 100:0.0} cm in all, {hv.Step * 100:0.0} cm in a frame");
                if (cw.Samples < 40 || hw.Samples < 40 || cw.Drift > 0.08f || cw.Step > 0.04f || hw.Drift > 0.08f || hw.Step > 0.04f)
                    Fail($"seat exit ({name}): the third peer saw a rider move on the deck: client {cw.Drift * 100:0.0} cm (step {cw.Step * 100:0.0}, {cw.Samples}), host {hw.Drift * 100:0.0} cm (step {hw.Step * 100:0.0}, {hw.Samples})");
                if (agree > 0.04f)
                    Fail($"seat exit ({name}): the client and the server disagree in the deck's frame by {agree * 100:0.0} cm");
            }
            else
            {
                var deckSpeed = clientVel.Length();
                var away = awayAt1 ?? -99f;
                Line($"seat exit ({name}): left at {cs.FirstVel.Length():0.0} m/s (host {hs.FirstVel.Length():0.0}, client's copy {cc.FirstVel.Length():0.0}; the seat {deckSpeed:0.0}); 0.7 s on the rider is {away:0.0} m along the deck's way from it; back-steps client {hv.Back * 100:0.0} cm, watcher {cw.Back * 100:0.0} cm ({hv.Samples}/{cw.Samples} samples)");
                if (!cs.Out || !hs.Out || !cc.Out)
                    Fail($"seat exit ({name}): someone didn't get out");
                if (Vector3.Distance(cs.FirstVel, clientVel) > 1f || Vector3.Distance(cc.FirstVel with { Y = 0 }, clientVel with { Y = 0 }) > 3f || cs.FirstRode) // (the client: after its replay of a round trip in the air)
                    Fail($"seat exit ({name}): the rider should leave at the seat's velocity {clientVel} (server {cs.FirstVel}, client {cc.FirstVel}, riding {cs.FirstRode})");
                if (hs.FirstVel.Length() < deckSpeed - 1f)
                    Fail($"seat exit ({name}): the host left at {hs.FirstVel.Length():0.0} m/s, not the seat's {deckSpeed:0.0}");
                if (hv.Back > 0.03f || cw.Back > 0.05f)
                    Fail($"seat exit ({name}): the rider stepped back along their flight (client {hv.Back * 100:0.0} cm, watcher {cw.Back * 100:0.0} cm)");
                if (away < -4f)
                    Fail($"seat exit ({name}): the rider was left {-away:0.0} m behind the deck");
                if (sp.Position.Y > pos0.Y - 8f) // (0.5 g t^2 from the deck's height: they do fall)
                    Fail($"seat exit ({name}): the rider didn't fall (height {sp.Position.Y:0.0}, the deck's start {pos0.Y:0.0})");
            }
            sp.Teleport(new Vector3(0, 1, 0));
            host.Teleport(new Vector3(2, 1, 0));
            c.Anchored = true;
            c.Vel = c.AngVel = Vector3.Zero;
            c.Pos = pos0;
            c.Rot = rot0;
            hc.Vel = hc.AngVel = Vector3.Zero;
            hc.Anchored = true;
            return true;
        }

        Case("straight at 12 m/s", PlainDeck(-20), Straight, onDeck: true);
        Case("a 14 m turn at 10.5 m/s", PlainDeck(-13), Turn, onDeck: true);
        Case("climbing and rolling", PlainDeck(-6), ClimbRoll, onDeck: true);
        Case("a turret's seat and a seat off grid", TurretDeck(1), Straight, onDeck: true, turret: true);
        if (BareSeats(15) is { } hostBare)
            Case("over the side", BareSeats(8), Straight, onDeck: false, hostBody: hostBare.Item1);
        RideLandOnWire(server, a, clients, sp, clock, ref now, -30);
        hostPeer.SimulateBadNetwork(0, 0, 0);
        peerA.SimulateBadNetwork(0, 0, 0);
        peerB.SimulateBadNetwork(0, 0, 0);
    }

    /// <summary>A real buggy on its physics: full throttle, then out: on it, or away at its speed, never left standing.</summary>
    private static void RideSeatExitBuggy(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me || g.Constructs.Bodies.FirstOrDefault(b => b.CountRole(PartRole.Seat) == 2 && b.CanDrive) is not { } car)
        {
            Fail("seat exit (buggy): no local player or no wired buggy on the yard");
            return;
        }
        me.ImmuneUntil = 1e9;
        var feet = ConstructEdits.SeatFeet(car, car.DriveSeat);
        me.Teleport(ConstructWorld.Ground(World.Query, feet.X - 1.6f, feet.Z));
        void Frame()
        {
            now += World.TickDt;
            g.Update(now, World.TickDt);
        }
        for (var k = 0; k < 12; k++)
            Frame();
        g.SandboxSend(CActKind.Use, body: car.Id, part: (byte)car.DriveSeat);
        for (var k = 0; k < 12; k++)
            Frame();
        if (car.Driver != me.Id)
        {
            Fail("seat exit (buggy): couldn't sit");
            return;
        }
        g.HeldButtons = Buttons.Forward;
        for (var k = 0; k < 150; k++)
            Frame();
        g.HeldButtons = Buttons.None;
        var speed = car.Vel.Length();
        g.SandboxSend(CActKind.Stand);
        Frame();
        var rode = me.Sim.RideBody == car.Id;
        var vel = rode ? car.PointVel(me.Position) + me.Sim.Velocity : me.Sim.Velocity;
        Line($"seat exit (buggy): out at {speed:0.0} m/s, {(rode ? "on the buggy" : "off it")}, at {vel.Length():0.0} m/s");
        if (speed < 4f)
            Fail($"seat exit (buggy): the buggy only reached {speed:0.0} m/s");
        if (vel.Length() < speed * 0.8f)
            Fail($"seat exit (buggy): the driver lost the buggy's speed: {vel.Length():0.0} of {speed:0.0} m/s");
    }
}
