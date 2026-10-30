using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Seated riders" (docs/sandbox/CONSTRUCTS.md "Seats"): in a real (offline) Sandbox game on the yard, a driver and a
    /// passenger in the wired buggy and a gunner on the Gunner Turret stay at their seats' local offsets (within a few mm,
    /// every frame between ticks) while the buggy drives and turns and the turret turns; nobody walks or steps; the camera
    /// is at the seat's head; the look turns with the seat inside each seat type's limits; dying seated starts a limp
    /// seated ragdoll with the buggy's velocity; nothing allocates. Then over a loopback: two clients see the driver at the
    /// seat of their own copies of the buggy and agree on the seat and the look.
    /// </summary>
    private static void CheckSeated()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"seated: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        SeatedOffline(yard);
        SeatedLoopback(yard);
    }

    /// <summary>The worst distance (m) between a seated player's drawn feet and eye and where the seat puts them, and the turn between their drawn body frame and the seat's, in the construct drawn as <see cref="ConstructRender.Draw"/> draws it (matrices, not the seat pass's quaternions).</summary>
    private static (float Feet, float Eye, float Turn) SeatError(Construct c, int part, Player p, float alpha, bool blendTicks)
    {
        ref var sp = ref c.Parts[part];
        var pos = blendTicks ? Vector3.Lerp(c.PrevPos, c.Pos, alpha) : c.Pos;
        var rot = blendTicks ? Quaternion.Slerp(c.PrevRot, c.Rot, alpha) : c.Rot;
        var body = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);
        var seg = sp.Seg > 0 ? ConstructRender.SegMatrix(c, sp.Seg, blendTicks ? alpha : 1f) : Matrix4x4.Identity;
        var frame = Matrix4x4.CreateFromQuaternion(Rot24.Quat(sp.Rot)) * Matrix4x4.CreateTranslation(sp.Pos) * seg * body; // the seat part's frame -> world
        Matrix4x4.Invert(frame, out var inv);
        var at = sp.D.SeatAt;
        var feet = Vector3.Distance(Vector3.Transform(p.RenderPosition, inv), at);
        var eye = Vector3.Distance(Vector3.Transform(p.RenderEye, inv), at + new Vector3(0, p.RenderEyeHeight, 0));
        var f0 = Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, frame));
        var u0 = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, frame));
        var f1 = Vector3.Transform(-Vector3.UnitZ, p.RenderSeatRot);
        var u1 = Vector3.Transform(Vector3.UnitY, p.RenderSeatRot);
        var turn = MathF.Max(MathF.Acos(Math.Clamp(Vector3.Dot(f0, f1), -1f, 1f)), MathF.Acos(Math.Clamp(Vector3.Dot(u0, u1), -1f, 1f)));
        return (feet, eye, turn);
    }

    private static void SeatedOffline(Map yard)
    {
        var now = 0.0;
        using var g = ModeGame(0, GameMode.Sandbox, yard.Id);
        Run(g, ref now, Game.CountdownSeconds + 0.3);
        g.RequestRespawn();
        Run(g, ref now, 1.0);
        if (g.Local is not { Alive: true } me)
        {
            Fail("seated: no local player");
            return;
        }
        me.ImmuneUntil = 1e9;
        var car = g.Constructs.Bodies.FirstOrDefault(b => b.CountRole(PartRole.Seat) == 2 && b.CanDrive);
        var turret = BuildBlueprint(g.Constructs, "Gunner Turret", -14, 14);
        if (car == null || turret == null)
        {
            Fail("seated: no wired buggy on the yard (or the Gunner Turret doesn't build)");
            return;
        }
        var other = PartNamed(car, "Seat", 1);
        var gunSeat = PartNamed(turret, "Seat");
        var passenger = g.SelfTestAddHuman();
        var gunner = g.SelfTestAddHuman();
        g.SelfTestRespawn(passenger, now);
        g.SelfTestRespawn(gunner, now);
        passenger.ImmuneUntil = gunner.ImmuneUntil = 1e9;
        var feet0 = ConstructEdits.SeatFeet(car, car.DriveSeat);
        me.Teleport(ConstructWorld.Ground(World.Query, feet0.X - 1.6f, feet0.Z));
        passenger.Teleport(ConstructWorld.Ground(World.Query, feet0.X - 1.6f, feet0.Z - 1f));
        uint seq = 0;
        float passengerYaw = 0, gunnerYaw = 0;
        // (remote players move only on the inputs they send: still ones every tick, looking where the test says)
        void Frame(double dt)
        {
            seq++;
            passenger.InputQueue.Add(new InputCmd { Seq = seq, Yaw = passengerYaw });
            gunner.InputQueue.Add(new InputCmd { Seq = seq, Yaw = gunnerYaw });
            now += dt;
            g.Update(now, dt);
        }
        for (var k = 0; k < 12; k++)
            Frame(World.TickDt);
        g.SandboxSend(CActKind.Use, body: car.Id, part: (byte)car.DriveSeat);
        var rideWhy = g.SandboxHands.Use(g.Constructs, passenger.Id, passenger.Eye, car.Id, other);
        var gunWhy = g.SandboxHands.Use(g.Constructs, gunner.Id, ConstructRender.PartCentre(turret, gunSeat) + new Vector3(0, 0.5f, 1.2f), turret.Id, gunSeat);
        for (var k = 0; k < 12; k++)
            Frame(World.TickDt);
        if (rideWhy != null || gunWhy != null || car.Driver != me.Id)
        {
            Fail($"seated: couldn't sit (driver {car.Driver}, passenger \"{rideWhy}\", gunner \"{gunWhy}\")");
            return;
        }
        ConstructRender.SeatPose(car, car.DriveSeat, 1, true, out var dq, out _);
        ConstructRender.SeatPose(car, other, 1, true, out var pq, out _);
        ConstructRender.SeatPose(turret, gunSeat, 1, true, out var gq, out _);
        g.LookYaw = World.WrapAngle(Game.SeatYaw(dq) + 0.4f); // the driver looks a little right
        passengerYaw = World.WrapAngle(Game.SeatYaw(pq) + 2.8f); // past a passenger's limit
        gunnerYaw = World.WrapAngle(Game.SeatYaw(gq) + 2.8f); // a gunner looks all the way round
        turret.Parts[PartNamed(turret, "Lever")].State = 1; // (the turntable turns)
        turret.Wake();
        for (var k = 0; k < 6; k++)
            Frame(World.TickDt);
        var kinds = $"{me.RenderSeat}/{passenger.RenderSeat}/{gunner.RenderSeat}";
        var passengerLook = passenger.RenderLookYaw;
        var gunnerLook = gunner.RenderLookYaw;

        // Drive forward, then turn right, at a frame rate that isn't a multiple of the tick (every blend between ticks).
        var steps = new Footsteps();
        var stepCount = 0;
        Action<StepEvent> emit = _ => stepCount++;
        float worstFeet = 0, worstEye = 0, worstTurn = 0, worstLook = 0, maxSpeed = 0, worstLegs = 0;
        var yaw0 = Game.SeatYaw(car.Rot * Rot24.Quat(car.Parts[car.DriveSeat].Rot));
        var tt0 = turret.Joints[0].Angle;
        var p0 = car.WorldCom;
        const double frameDt = 1.0 / 97;
        for (var f = 0; f < 97 * 3; f++)
        {
            g.HeldButtons = f < 97 * 1.4 ? Buttons.Forward : Buttons.Forward | Buttons.Right;
            Frame(frameDt);
            foreach (var (p, c, part) in new[] { (me, car, car.DriveSeat), (passenger, car, other), (gunner, turret, gunSeat) })
            {
                var e = SeatError(c, part, p, g.TickAlpha, true);
                worstFeet = MathF.Max(worstFeet, e.Feet);
                worstEye = MathF.Max(worstEye, e.Eye);
                worstTurn = MathF.Max(worstTurn, e.Turn);
                var legs = LegAnimator.Animate(p, now);
                worstLegs = MathF.Max(worstLegs, legs.Moving + legs.Speed + legs.Phase);
                steps.Update(p, now, emit);
            }
            worstLook = MathF.Max(worstLook, MathF.Abs(me.RenderLookYaw - 0.4f));
            maxSpeed = MathF.Max(maxSpeed, me.RenderVelocity.Length());
        }
        g.HeldButtons = Buttons.Jump;
        for (var k = 0; k < 30; k++)
            Frame(World.TickDt);
        var turned = MathF.Abs(World.WrapAngle(Game.SeatYaw(car.Rot * Rot24.Quat(car.Parts[car.DriveSeat].Rot)) - yaw0));
        var turretTurned = MathF.Abs(turret.Joints[0].Angle - tt0);
        var drove = Vector3.Distance(p0, car.WorldCom);
        // The driver's look past the limit stops at it.
        ConstructRender.SeatPose(car, car.DriveSeat, g.TickAlpha, true, out dq, out _);
        g.LookYaw = World.WrapAngle(Game.SeatYaw(dq) + 3f);
        Frame(World.TickDt);
        var driverLimit = me.RenderLookYaw;
        Line($"seated: {kinds} (driver/passenger/gunner); drove {drove:0.0} m at up to {maxSpeed:0.0} m/s and turned {turned * 180 / MathF.PI:0} deg, the turret turned {turretTurned * 180 / MathF.PI:0} deg: "
             + $"the riders stayed within {worstFeet * 1000:0.0} mm of their seats (eyes {worstEye * 1000:0.0} mm, turn {worstTurn * 180 / MathF.PI:0.00} deg), legs {worstLegs:0.000} (moving + speed + phase), {stepCount} footsteps; "
             + $"the driver's look held {worstLook * 180 / MathF.PI:0.0} deg off its seat-relative 23 deg, stopped at {driverLimit * 180 / MathF.PI:0} deg; a passenger looking 160 deg round at {passengerLook * 180 / MathF.PI:0}, the gunner at {gunnerLook * 180 / MathF.PI:0}");
        if (kinds != "Driver/Passenger/Gunner")
            Fail($"seated: the seat types are {kinds}, not Driver/Passenger/Gunner");
        if (drove < 5 || maxSpeed < 3 || turned < 0.3f || turretTurned < 0.5f)
            Fail("seated: the buggy didn't drive and turn (or the turret didn't turn) far enough to test");
        if (worstFeet > 0.004f || worstEye > 0.004f || worstTurn > 0.005f)
            Fail("seated: a seated player isn't drawn at the seat's local offset (or the camera isn't at the seat's head) while the construct moves");
        if (worstLegs > 1e-4f || stepCount > 0)
            Fail("seated: a seated player walk-animates or takes footsteps");
        if (worstLook > 0.01f)
            Fail("seated: the driver's look doesn't turn with the seat");
        if (MathF.Abs(driverLimit - Game.SeatLookLimit(SeatKind.Driver)) > 0.01f || MathF.Abs(passengerLook - Game.SeatLookLimit(SeatKind.Passenger)) > 0.01f || MathF.Abs(gunnerLook) < Game.SeatLookLimit(SeatKind.Passenger) + 0.2f)
            Fail("seated: the look limits (driver, passenger) or the gunner's full look don't hold");

        // No allocations: the seat pass and the seated legs.
        var least = long.MaxValue;
        for (var rep = 0; rep < 5; rep++)
        {
            var b0 = GC.GetAllocatedBytesForCurrentThread();
            for (var k = 0; k < 60; k++)
            {
                g.UpdateSandboxSeats(now + k * 0.01, 0.01f);
                LegAnimator.Animate(me, now + k * 0.01);
                LegAnimator.Animate(passenger, now + k * 0.01);
            }
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - b0);
        }
        Line($"seated: the seat pass and the seated legs allocate {least} bytes per 60 frames");
        if (least > 0)
            Fail("seated: the seat pass or the seated legs allocate");

        // Dying seated: a limp seated ragdoll with the buggy's velocity (backing up: the rescaled buggy's wider turn ends nose to the yard's kerb).
        g.HeldButtons = Buttons.Back;
        for (var k = 0; k < 75; k++)
            Frame(World.TickDt);
        var ragdolls = Ragdolls.Enabled;
        Ragdolls.Clear();
        var vel = passenger.RenderVelocity;
        var seatRot = passenger.RenderSeatRot;
        var feet = passenger.RenderPosition;
        passenger.Alive = false;
        Ragdolls.SpawnPlayerForTest(g, passenger, new KillPacket(me.Id, passenger.Id, false, 0), now);
        passenger.Alive = true;
        RagdollBody? body = null;
        for (var i = 0; i < Ragdolls.Capacity; i++)
            if (Ragdolls.Body(i) is { Live: true } b && b.Owner == passenger.Id)
                body = b;
        if (ragdolls && body == null)
            Fail("seated: dying seated left no ragdoll");
        else if (body != null)
        {
            var sim = body.Sim;
            var toSeat = Quaternion.Conjugate(Quaternion.CreateFromYawPitchRoll(-Game.SeatYaw(seatRot), 0, 0));
            var hip = Vector3.Transform((sim.Pos[RagdollRig.HipR] + sim.Pos[RagdollRig.HipL]) * 0.5f - feet, toSeat);
            var knee = Vector3.Transform((sim.Pos[RagdollRig.KneeR] + sim.Pos[RagdollRig.KneeL]) * 0.5f - feet, toSeat);
            var dv = Vector3.Distance((sim.Velocity(RagdollRig.HipR) + sim.Velocity(RagdollRig.HipL)) * 0.5f, vel);
            Line($"seated: dying seated at {vel.Length():0.0} m/s: the ragdoll's hips {hip.Y:0.00} m up, knees {hip.Z - knee.Z:0.00} m forward of them, its velocity {dv:0.00} m/s off the buggy's, {(body.Act.Active ? "an active reaction" : "limp")}");
            if (vel.Length() < 2 || MathF.Abs(hip.Y - LegAnimator.SeatedHips.Y + 0.04f) > 0.08f || hip.Z - knee.Z < 0.3f || dv > 0.6f || body.Act.Active)
                Fail("seated: dying seated doesn't start a seated ragdoll with the construct's velocity");
        }
        Ragdolls.Clear();
        g.HeldButtons = Buttons.None;
        g.SandboxSend(CActKind.Stand);
        Run(g, ref now, 0.2);
        if (me.RenderSeat != SeatKind.None)
            Fail("seated: getting out left the local player drawn seated");
    }

    private static void SeatedLoopback(Map yard)
    {
        const ushort port = 26301;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        yard.RestoreProps();
        World.SetMap(yard);
        using var server = new Game(NetPeer.Host(port, 4), new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        using var a = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        using var b = new Game(NetPeer.Join("127.0.0.1", port), new Audio(), "Bravo", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Game[] clients = [a, b];
        Pump(server, clients, 1.5, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            Pump(server, clients, 0.25, ref now, clock);
        Pump(server, clients, 0.5, ref now, clock);
        if (!server.Players.TryGetValue(a.LocalId, out var sa) || !server.Players.TryGetValue(b.LocalId, out var sb))
        {
            Fail("seated loopback: the clients didn't join");
            return;
        }
        foreach (var sp in new[] { sa, sb })
            if (!sp.Alive)
                server.SelfTestRespawn(sp, now);
        var car = server.Constructs.Bodies.First(x => x.CountRole(PartRole.Seat) == 2 && x.CanDrive);
        var other = PartNamed(car, "Seat", 1);
        var at = ConstructEdits.SeatFeet(car, car.DriveSeat);
        sa.Teleport(ConstructWorld.Ground(World.Query, at.X - 1.6f, at.Z));
        sb.Teleport(ConstructWorld.Ground(World.Query, at.X - 1.6f, at.Z - 1f));
        Pump(server, clients, 0.4, ref now, clock);
        a.SandboxSend(CActKind.Use, body: car.Id, part: (byte)car.DriveSeat);
        b.SandboxSend(CActKind.Use, body: car.Id, part: (byte)other);
        Pump(server, clients, 0.6, ref now, clock);
        // The driver looks 30 degrees right of the seat; then drives and turns.
        if (a.Constructs.Get(car.Id) is { } ac)
        {
            ConstructRender.SeatPose(ac, car.DriveSeat, 1, false, out var q, out _);
            a.LookYaw = World.WrapAngle(Game.SeatYaw(q) + 0.52f);
        }
        float worstA = 0, worstB = 0, worstLook = 0;
        var frames = 0;
        var seen = "";
        var until = clock.Elapsed.TotalSeconds + 2.4;
        var start = clock.Elapsed.TotalSeconds;
        while (clock.Elapsed.TotalSeconds < until)
        {
            var t = clock.Elapsed.TotalSeconds;
            var dt = Math.Clamp(t - now, 0.001, 0.1);
            now = t;
            a.HeldButtons = t - start < 1.2 ? Buttons.Forward : Buttons.Forward | Buttons.Left;
            server.Update(now, dt);
            a.Update(now, dt);
            b.Update(now, dt);
            Thread.Sleep(2);
            // Client a's own driver, and client b's view of them (each at its own copy of the buggy).
            if (a.Local is { } am && a.Constructs.Get(car.Id) is { } ca && am.RenderSeat != SeatKind.None)
                worstA = MathF.Max(worstA, SeatError(ca, car.DriveSeat, am, 1, false).Feet);
            if (b.Players.TryGetValue(a.LocalId, out var bm) && b.Constructs.Get(car.Id) is { } cb && bm.RenderSeat != SeatKind.None)
            {
                var e = SeatError(cb, car.DriveSeat, bm, 1, false);
                worstB = MathF.Max(worstB, MathF.Max(e.Feet, e.Turn * 0.01f));
                if (a.Local is { } am2 && t - start > 0.4)
                    worstLook = MathF.Max(worstLook, MathF.Abs(World.WrapAngle(bm.RenderLookYaw - am2.RenderLookYaw)));
                frames++;
            }
            if (b.Players.TryGetValue(a.LocalId, out var bm3))
                seen = $"{bm3.RenderSeat}";
        }
        a.HeldButtons = Buttons.Jump;
        Pump(server, clients, 0.5, ref now, clock);
        a.HeldButtons = Buttons.None;
        var bSelf = b.Local?.RenderSeat ?? SeatKind.None;
        Line($"seated loopback: client a's driver within {worstA * 1000:0.0} mm of its seat; client b sees them as {seen} within {worstB * 1000:0.0} mm of its own copy's seat over {frames} frames, "
             + $"their seat-relative look within {worstLook * 180 / MathF.PI:0.0} deg of the driver's own; client b rides as {bSelf}");
        if (seen != "Driver" || bSelf != SeatKind.Passenger || frames < 20)
            Fail("seated loopback: the clients don't agree on who sits where");
        if (worstA > 0.004f || worstB > 0.004f)
            Fail("seated loopback: a client draws a seated player off the seat of its copy of the construct");
        if (worstLook > 0.2f)
            Fail("seated loopback: a client doesn't see a seated player's look relative to the seat");
        a.SandboxSend(CActKind.Stand);
        b.SandboxSend(CActKind.Stand);
        Pump(server, clients, 0.3, ref now, clock);
    }
}
