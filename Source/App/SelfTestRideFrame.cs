using System.Diagnostics;
using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// "Ride frame" (board item dev-sandbox-ride-frame-rf01; ConstructRide.cs): a player standing on a construct is kept in
    /// its frame. Seeded and scripted, on the Construct Yard, with a deck moved kinematically (its pose, velocity and turn
    /// set each tick, as the physics would): a rider on a deck driving a 20 m circle at 10 m/s stays within 2 cm of their
    /// spot; a jump in it lands within 10 cm of where it took off; stepping off at speed keeps the deck's velocity; a deck
    /// pitching to 20 degrees doesn't slide them; a grip plate holds a rider at 80 degrees (an ordinary plate doesn't) and
    /// lets them go when they step off; a crash throws them off (a gentle stop doesn't); and over the real transport with
    /// 150 ms of simulated lag, a client riding a turning deck agrees with the server in the deck's frame (no correction
    /// snaps) and its view turns with the deck.
    /// </summary>
    private static void CheckRideFrame()
    {
        if (Maps.Find(ConstructYardMap) is not { } yard)
        {
            Fail($"ride frame: the {ConstructYardMap} map is missing");
            return;
        }
        yard.RestoreProps();
        World.SetMap(yard);
        RideCircle();
        RideStepOff();
        RidePitch();
        RideGrip();
        RideCrash();
        RideOverWire(yard);
    }

    /// <summary>A flat deck of <paramref name="plates"/> plates in a row along +z (local z -1 to 2 * plates - 1), its top <paramref name="lift"/> above the ground at (x, z).</summary>
    private static Construct? RideDeck(ConstructWorld w, float x, float z, float lift, int plates = 2, string part = "Deck Plate")
    {
        var c = ConstructBuild.PlaceNew(w, ConstructParts.Need(part), ConstructWorld.Ground(World.Query, x, z), 0);
        if (c == null)
        {
            Fail($"ride frame: a {part} doesn't place");
            return null;
        }
        for (var k = 1; k < plates; k++)
            if (Fit(w, c, k - 1, "+z", part, "-z") < 0)
                return null;
        c.Rebuild();
        RidePose(w, c, c.Pos + new Vector3(0, lift, 0), c.Rot, Vector3.Zero, Vector3.Zero);
        c.PrevPos = c.NetPos = c.NetFromPos = c.Pos;
        return c;
    }

    /// <summary>A deck moved as the physics would move it: its pose, its velocity and turn, its acceleration noted (ConstructRide.AfterPhysics).</summary>
    private static void RidePose(ConstructWorld w, Construct c, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 ang)
    {
        ConstructRide.BeforePhysics(w);
        c.PrevPos = c.Pos;
        c.PrevRot = c.Rot;
        c.Pos = pos;
        c.Rot = Quaternion.Normalize(rot);
        c.Vel = vel;
        c.AngVel = ang;
        c.Asleep = false;
        c.Anchored = false;
        ConstructRide.AfterPhysics(w, World.TickDt);
    }

    /// <summary>One tick of a player's movement on the constructs, as the server runs it.</summary>
    private static void RideTick(ConstructWorld w, ref MoveState s, Buttons b = Buttons.None, float yaw = 0)
    {
        World.Simulate(ref s, new InputCmd { Buttons = b, Yaw = yaw, SpeedPct = 100 });
        w.BlockPlayer(ref s, World.HeightOf(s.Stance), server: true, who: 1);
    }

    /// <summary>A player dropped onto the deck's top at local point <paramref name="local"/> and settled there (riding it).</summary>
    private static MoveState RideBoard(ConstructWorld w, Construct c, Vector3 local)
    {
        var s = new MoveState { Position = c.ToWorld(local) + new Vector3(0, 0.03f, 0) };
        for (var t = 0; t < 20; t++)
            RideTick(w, ref s);
        return s;
    }

    private static Vector3 DeckTop(float x, float z) => new(x, 0.05f, z);

    /// <summary>The deck running at <paramref name="vel"/> for a while first, then a player dropped onto it (matched on landing: board assist 1).</summary>
    private static MoveState RideBoardMoving(ConstructWorld w, Construct c, Vector3 local, Vector3 vel)
    {
        for (var t = 0; t < 30; t++)
            RidePose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero); // (its acceleration settles: no crash)
        var s = new MoveState { Position = c.ToWorld(local) + new Vector3(0, 0.03f, 0), Velocity = vel };
        for (var t = 0; t < 30; t++)
        {
            RidePose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);
            RideTick(w, ref s);
        }
        return s;
    }

    private static void RideCircle()
    {
        var w = new ConstructWorld();
        if (RideDeck(w, -6, -6, 6f) is not { } c)
            return;
        var s = RideBoard(w, c, DeckTop(0.2f, 1.4f));
        var boarded = s.RideBody == c.Id;
        var spot = s.RideLocal;
        // Round a 10 m radius: speeding up at 3 m/s^2 to 10 m/s, then on round at 10 m/s.
        const float radius = 10f;
        var pos0 = c.Pos;
        var rot0 = c.Rot;
        var pivot = c.WorldCom + new Vector3(radius, 0, 0);
        var theta = 0f;
        var omega = 0f;
        var worst = 0f;
        var yaw0 = theta;
        var jumped = false;
        var takeOff = Vector3.Zero;
        var landed = Vector3.Zero;
        var airTicks = 0;
        var inFrameAir = true;
        for (var t = 0; t < 60 * 6; t++)
        {
            omega = MathF.Min(omega + 0.3f * World.TickDt, 1f); // (3 m/s^2 at 10 m)
            theta += omega * World.TickDt;
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, theta);
            var pos = pivot + Vector3.Transform(pos0 - pivot, turn);
            var com = pos + Vector3.Transform(c.Com, turn * rot0);
            var ang = new Vector3(0, omega, 0);
            RidePose(w, c, pos, turn * rot0, Vector3.Cross(ang, com - pivot), ang);
            // At full speed, a jump: it should land where it took off.
            var jump = !jumped && omega >= 1f && t > 60 * 4;
            if (jump)
            {
                jumped = true;
                takeOff = s.RideLocal;
            }
            RideTick(w, ref s, jump ? Buttons.Jump : Buttons.None);
            if (jumped && landed == Vector3.Zero)
            {
                airTicks++;
                if (s.RideBody != c.Id)
                    inFrameAir = false;
                if (airTicks > 3 && s.Grounded && s.RideAir == 0 && s.RideBody == c.Id)
                    landed = s.RideLocal;
            }
            else if (!jumped)
                worst = MathF.Max(worst, Vector3.Distance(s.RideLocal, spot));
        }
        var drift = Vector3.Distance(ConstructRide.ToLocal(c, 0, s.Position), s.RideLocal);
        var jumpMiss = landed == Vector3.Zero ? float.MaxValue : Vector3.Distance(landed with { Y = 0 }, takeOff with { Y = 0 });
        Line($"ride frame: a 20 m circle at {omega * radius:0.0} m/s: boarded {boarded}, off the spot by {worst * 100:0.00} cm at worst (frame vs world {drift * 1000:0.0} mm), turned {(theta - yaw0) * 180 / MathF.PI:0} deg; a jump at speed: {airTicks} ticks in the air, in the frame throughout {inFrameAir}, landed {jumpMiss * 100:0.0} cm from its take-off");
        if (!boarded || worst > 0.02f || drift > 0.005f)
            Fail($"ride frame: a rider on a deck driving a 20 m circle at 10 m/s left their spot ({worst * 100:0.00} cm; boarded {boarded})");
        if (jumpMiss > 0.10f || !inFrameAir)
            Fail($"ride frame: a jump on the moving deck didn't land where it took off ({jumpMiss * 100:0.0} cm, in the frame {inFrameAir})");
    }

    private static void RideStepOff()
    {
        var w = new ConstructWorld();
        if (RideDeck(w, -6, -6, 3f) is not { } c)
            return;
        // The deck at 10 m/s along +z (already moving: the rider matched its speed on landing), then the rider walks off its +x side.
        var vel = new Vector3(0, 0, 10f);
        var s = RideBoardMoving(w, c, DeckTop(0.3f, 1f), vel);
        var boarded = s.RideBody == c.Id;
        var leftAt = -1;
        var leaving = Vector3.Zero;
        var later = Vector3.Zero;
        var walkYaw = MathF.PI / 2; // (facing +x)
        for (var t = 0; t < 150; t++)
        {
            RidePose(w, c, c.Pos + vel * World.TickDt, c.Rot, vel, Vector3.Zero);
            var was = s.RideBody;
            RideTick(w, ref s, t >= 30 ? Buttons.Forward : Buttons.None, walkYaw);
            if (leftAt < 0 && was != 0 && s.RideBody == 0)
            {
                leftAt = t;
                leaving = s.Velocity;
            }
            if (leftAt >= 0 && t == leftAt + 15)
                later = s.Velocity;
        }
        Line($"ride frame: stepping off a deck at 10 m/s: left it after {(leftAt - 30) / 60f:0.00} s at {leaving.Z:0.00} m/s along its way ({leaving.X:0.0} across), 0.25 s later {later.Z:0.00} m/s");
        if (!boarded || leftAt < 0 || leaving.Z < 9.5f || later.Z < 7.5f)
            Fail($"ride frame: stepping off a moving deck didn't keep its velocity (left {leftAt >= 0}, {leaving.Z:0.00} m/s, then {later.Z:0.00} m/s)");
    }

    private static void RidePitch()
    {
        var w = new ConstructWorld();
        if (RideDeck(w, -6, -6, 4f) is not { } c)
            return;
        var s = RideBoard(w, c, DeckTop(0.4f, 2.2f));
        var spot = s.RideLocal;
        var rot0 = c.Rot;
        var com0 = c.WorldCom;
        var worst = 0f;
        var pitch = 0f;
        for (var t = 0; t < 120; t++)
        {
            var rate = t < 60 ? 20f * MathF.PI / 180f : 0f; // (to 20 degrees in a second, then held)
            pitch += rate * World.TickDt;
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch) * rot0;
            RidePose(w, c, com0 - Vector3.Transform(c.Com, rot), rot, Vector3.Zero, new Vector3(rate, 0, 0));
            RideTick(w, ref s);
            worst = MathF.Max(worst, Vector3.Distance(s.RideLocal, spot));
        }
        Line($"ride frame: a deck pitching to {pitch * 180 / MathF.PI:0} degrees: the rider {(s.RideBody == c.Id ? "rides it" : "fell off")}, off the spot by {worst * 100:0.00} cm at worst");
        if (s.RideBody != c.Id || worst > 0.02f)
            Fail($"ride frame: a rider slid on a deck pitching to 20 degrees ({worst * 100:0.00} cm)");
    }

    private static void RideGrip()
    {
        (bool Held, float Off, bool Released, float Fell) Tilted(string part)
        {
            var w = new ConstructWorld();
            if (RideDeck(w, -6, -6, 6f, 1, part) is not { } c)
                return (false, 99, false, 0);
            var com = c.WorldCom;
            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 80f * MathF.PI / 180f) * c.Rot;
            RidePose(w, c, com - Vector3.Transform(c.Com, rot), rot, Vector3.Zero, Vector3.Zero);
            var n = Vector3.Transform(Vector3.UnitY, rot);
            var s = new MoveState { Position = c.ToWorld(DeckTop(0, 0)) + n * 0.02f };
            var start = s.Position;
            for (var t = 0; t < 10; t++)
                RideTick(w, ref s);
            var spot = s.RideLocal;
            var off = 0f;
            for (var t = 0; t < 120; t++)
            {
                RideTick(w, ref s);
                off = MathF.Max(off, s.RideBody == c.Id ? Vector3.Distance(s.RideLocal, spot) : 99f);
            }
            var held = s.RideBody == c.Id && off < 0.05f;
            // Then walking along its edge (world x) off its side: let go, falling.
            var released = false;
            for (var t = 0; t < 150 && !released; t++)
            {
                RideTick(w, ref s, Buttons.Right);
                released = s.RideBody == 0;
            }
            for (var t = 0; t < 30; t++)
                RideTick(w, ref s);
            return (held, off, released, start.Y - s.Position.Y);
        }
        var grip = Tilted("Grip Deck");
        var plain = Tilted("Deck Plate");
        Line($"ride frame: a grip plate at 80 degrees: held {grip.Held} (off the spot {grip.Off * 100:0.0} cm), let go stepping off {grip.Released} (fell {grip.Fell:0.0} m); an ordinary plate at 80 degrees: held {plain.Held}");
        if (!grip.Held || !grip.Released || grip.Fell < 0.5f)
            Fail("ride frame: a grip plate at 80 degrees didn't hold its rider (or didn't let them go when they stepped off)");
        if (plain.Held)
            Fail("ride frame: an ordinary plate held a rider at 80 degrees (only grip parts should)");
        var d = ConstructParts.Need("Grip Deck");
        if (!ConstructConfig.Has(d, CfgKey.Grip) || !ConstructConfig.Has(ConstructParts.Need("Deck Plate"), CfgKey.Grip) || !ConstructConfig.Has(ConstructParts.Need("Panel"), CfgKey.Grip))
            Fail("ride frame: the Grip Deck, Deck Plate and Panel should have the grip setting");
    }

    private static void RideCrash()
    {
        (bool Thrown, float Vel) Stop(float decel)
        {
            var w = new ConstructWorld();
            if (RideDeck(w, -6, -6, 3f) is not { } c)
                return (false, 0);
            var v = 10f;
            var s = RideBoardMoving(w, c, DeckTop(0, 1f), new Vector3(0, 0, v));
            if (s.RideBody != c.Id)
                return (false, -1);
            var thrown = false;
            var vel = 0f;
            for (var t = 0; t < 90 && !thrown; t++)
            {
                v = MathF.Max(0, v - decel * World.TickDt);
                RidePose(w, c, c.Pos + new Vector3(0, 0, v) * World.TickDt, c.Rot, new Vector3(0, 0, v), Vector3.Zero);
                RideTick(w, ref s);
                if (s.RideBody == 0)
                {
                    thrown = true;
                    vel = s.Velocity.Z;
                }
            }
            return (thrown, vel);
        }
        var crash = Stop(300f); // (10 m/s to rest in two ticks)
        var brake = Stop(5f); // (half a g)
        Line($"ride frame: a crash from 10 m/s: thrown off {crash.Thrown} at {crash.Vel:0.0} m/s; braking at 0.5 g: thrown off {brake.Thrown}");
        if (!crash.Thrown || crash.Vel < 8f)
            Fail("ride frame: a crash didn't throw the rider off at the deck's speed");
        if (brake.Thrown)
            Fail("ride frame: braking at half a g threw the rider off");
    }

    /// <summary>A host and a client over the transport, 150 ms of simulated lag: a client riding a turning deck agrees with the server in its frame.</summary>
    private static void RideOverWire(Map yard)
    {
        const ushort port = 26391;
        var clock = Stopwatch.StartNew();
        var now = 0.0;
        World.SetMap(yard);
        var hostPeer = NetPeer.Host(port, 4);
        using var server = new Game(hostPeer, new Audio(), "Host", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id, bots: 0, mapId: yard.Id) { MapVoteEnabled = false };
        server.Mode = GameMode.Sandbox;
        var peer = NetPeer.Join("127.0.0.1", port);
        using var a = new Game(peer, new Audio(), "Alpha", Weapons.DefaultPrimary.Id, Weapons.DefaultSecondary.Id);
        Pump(server, [a], 1.0, ref now, clock);
        for (var wait = 0; wait < 40 && server.Phase != MatchPhase.Playing; wait++)
            PumpFast(server, [a], 0.25, ref now);
        Pump(server, [a], 0.3, ref now, clock);
        if (a.LocalId == byte.MaxValue || !server.Players.TryGetValue(a.LocalId, out var sp))
        {
            Fail("ride frame (wire): the client didn't join");
            return;
        }
        if (!sp.Alive)
            server.SelfTestRespawn(sp, now);
        sp.ImmuneUntil = 1e9;
        if (server.Local is { } host)
            host.ImmuneUntil = 1e9;
        var lag = hostPeer.SimulateBadNetwork(75, 0, 0) & peer.SimulateBadNetwork(75, 0, 0);
        var w = server.Constructs;
        if (RideDeck(w, -6, 14, 25f) is not { } c) // (high above the yard: nothing on it to crash into)
            return;
        w.StructureDirty.Add(c.Id);
        Pump(server, [a], 0.5, ref now, clock);
        sp.Teleport(c.ToWorld(DeckTop(0.3f, 1.2f)) + new Vector3(0, 0.03f, 0));
        var pos0 = c.Pos;
        var rot0 = c.Rot;
        const float radius = 10f;
        var pivot = c.WorldCom + new Vector3(radius, 0, 0);
        var start = clock.Elapsed.TotalSeconds;
        double Theta(double t) => t < 3.3 ? 0.15 * t * t : 0.15 * 3.3 * 3.3 + (t - 3.3) * 0.99; // (speeding up at 3 m/s^2 to ~10 m/s at 10 m)
        void Drive()
        {
            var t = clock.Elapsed.TotalSeconds - start - 1.0; // (a second standing still first: boarded, heard)
            var th = (float)(t <= 0 ? 0 : Theta(t));
            var om = t <= 0 ? 0f : (float)Math.Min(0.3 * t, 0.99);
            var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, th);
            var pos = pivot + Vector3.Transform(pos0 - pivot, turn);
            c.Pos = pos;
            c.Rot = Quaternion.Normalize(turn * rot0);
            c.AngVel = new Vector3(0, om, 0);
            c.Vel = Vector3.Cross(c.AngVel, c.WorldCom - pivot);
            c.Asleep = false;
        }
        var throws0 = ConstructRide.Throws;
        var worst = 0f;
        var agree = 0f;
        var checks = 0;
        var riding = 0;
        var clientYaw0 = 0f;
        var look0 = 0f;
        var yawSet = false;
        var turned = 0f;
        var looked = 0f;
        while (clock.Elapsed.TotalSeconds < start + 7.5)
        {
            Drive();
            Pump(server, [a], 0.004, ref now, clock);
            var t = clock.Elapsed.TotalSeconds - start;
            if (t < 5.0 || a.Local is not { } me || a.Constructs.Get(c.Id) is not { } cc)
                continue;
            checks++;
            if (me.Sim.RideBody == c.Id && sp.Sim.RideBody == c.Id)
            {
                riding++;
                worst = MathF.Max(worst, a.LastCorrection);
                agree = MathF.Max(agree, Vector3.Distance(me.Sim.RideLocal with { Y = 0 }, sp.Sim.RideLocal with { Y = 0 }));
            }
            var f = Vector3.Transform(-Vector3.UnitZ, cc.Rot);
            var yaw = MathF.Atan2(f.X, -f.Z);
            if (!yawSet)
            {
                (clientYaw0, look0, yawSet) = (yaw, a.LookYaw, true);
                continue;
            }
            turned += World.WrapAngle(yaw - clientYaw0);
            looked += World.WrapAngle(a.LookYaw - look0);
            (clientYaw0, look0) = (yaw, a.LookYaw);
        }
        Line($"ride frame (wire, {(lag ? "150 ms simulated lag" : "no lag simulator in this RakNet build")}): the client rode the turning deck in {riding} of {checks} checks; corrections {worst * 100:0.00} cm at worst, client vs server in the deck's frame {agree * 100:0.00} cm; the deck turned {turned * 180 / MathF.PI:0} deg, the client's view {looked * 180 / MathF.PI:0} deg");
        if (checks < 50 || riding < checks * 9 / 10)
            Fail($"ride frame (wire): the client and the server didn't both have the player riding the deck ({riding} of {checks}; now server {sp.Sim.RideBody} air {sp.Sim.RideAir}, client {a.Local?.Sim.RideBody} air {a.Local?.Sim.RideAir}, deck {c.Id}, {ConstructRide.Throws - throws0} throws, deck accel {c.Accel.Length():0.0})");
        if (worst > 0.02f || agree > 0.03f)
            Fail($"ride frame (wire): the client's rider disagreed with the server in the deck's frame (corrections {worst * 100:0.00} cm, apart {agree * 100:0.00} cm)");
        if (MathF.Abs(turned) < 0.5f || MathF.Abs(looked - turned) > 0.05f)
            Fail($"ride frame (wire): the client's view didn't turn with the deck (deck {turned:0.00} rad, view {looked:0.00} rad)");
    }
}
