using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// What a pilot wears out of the backpack (docs/sandbox/CONSTRUCTS.md, "Pilot safety"): a parachute, a glider and a jetpack, on the
/// jump key in the air. Shared movement like the rest of <see cref="MoveState"/>: the client predicts it and the server runs it. The
/// server sets <see cref="MoveState.Gear"/> from the backpack; it works only out of a construct's ride frame (off a deck, a rider
/// falls out of the frame first), and a landing, a deck or a seat takes it off. Jump: the jetpack while it is held and has fuel;
/// else pressed, falling, the glider or the parachute; pressed with one open, it cuts away. Before the mover's tick: the movers
/// leave a pilot's way across the air to this code while <see cref="MoveState.Canopy"/> is set.
/// </summary>
public static class PilotGear
{
    public const byte Chute = 1, Glider = 2, Jet = 4, Mmu = 8; // MoveState.Gear's bits (Mmu: a Manned Manoeuvring Unit on a space suit, docs/sandbox/EVA.md; it stands in for the rest)
    public const byte OpenChute = 1, OpenGlider = 2, Burning = 3; // MoveState.Canopy
    public const float OpenFall = 5f; // m/s down before a canopy will open: a jump isn't a fall
    public const float ChuteSink = 5.5f, ChuteSteer = 5f;
    public const float GlideSpeed = 17f, GlideDive = 25f, GlideFlare = 10f, GlideSink = 3f, GlideDiveSink = 7f, GlideFlareSink = 2.2f, GlideTurn = 1.2f;
    public const float JetClimb = 12f, JetAccel = 34f, JetSteer = 10f;
    public const ushort JetFull = 720, JetBurn = 3; // a full tank: four seconds alight, twelve on the ground to fill
    public const float MmuAccel = 2.5f; // m/s2 the pack gives a spacewalker, any way (MmuBurnEvery ticks of it burn one of JetFuel's units: Eva.MmuFull)
    public const int MmuBurnEvery = 20; // thrusting ticks to a unit of the pack's tank (Karl, 2026-11-18: twenty times the fuel; a unit a tick would pass a ushort)

    /// <summary>The move keys a walking mover reads while the MMU is worn: none (Karl, 2026-11-18: it is a vehicle; no walking, no crouching, no jumps).</summary>
    public const Buttons MmuHeld = Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right | Buttons.Jump | Buttons.Sprint | Buttons.Crouch | Buttons.Prone;

    /// <summary>Is this a worn MMU out in vacuum, where it is a vehicle: no walking, no deck's frame, the pack flies them (World.SimulateMmu)?</summary>
    public static bool MmuFree(in MoveState s) => (s.Gear & Mmu) != 0 && !s.Noclip && Eva.Vacuum(s.Position);

    /// <summary>Does this command fire the pack (a move key, jump or crouch held)?</summary>
    public static bool MmuThrusting(in InputCmd cmd) => (cmd.Buttons & (Buttons.Forward | Buttons.Back | Buttons.Left | Buttons.Right | Buttons.Jump | Buttons.Crouch)) != 0;

    /// <summary>
    /// A spacewalker's axes from a look and its roll (docs/sandbox/EVA.md "Flying"): forward along the yaw and pitch at
    /// <paramref name="at"/>, right and up turned about it by <paramref name="roll"/> (positive: rolled right, the up leaning
    /// towards the right). The client's attitude is measured back into these by <see cref="MmuRollOf"/>.
    /// </summary>
    public static void MmuAxes(Vector3 at, float yaw, float pitch, float roll, out Vector3 fwd, out Vector3 right, out Vector3 up)
    {
        fwd = GravityFrames.Forward(at, yaw, pitch);
        var r0 = GravityFrames.FlatRight(at, yaw);
        var u0 = Vector3.Cross(r0, fwd);
        float c = MathF.Cos(roll), sn = MathF.Sin(roll);
        right = r0 * c - u0 * sn;
        up = u0 * c + r0 * sn;
    }

    /// <summary>The roll <see cref="MmuAxes"/> needs for <paramref name="up"/> about the look at that yaw and pitch.</summary>
    public static float MmuRollOf(Vector3 at, float yaw, float pitch, Vector3 up)
    {
        var fwd = GravityFrames.Forward(at, yaw, pitch);
        var r0 = GravityFrames.FlatRight(at, yaw);
        var u0 = Vector3.Cross(r0, fwd);
        return MathF.Atan2(Vector3.Dot(up, r0), Vector3.Dot(up, u0));
    }

    /// <summary>The ticks from an ejection seat's throw until its parachute opens.</summary>
    public const byte EjectDelay = 45;

    public static void Step(ref MoveState s, in InputCmd cmd)
    {
        var jump = (cmd.Buttons & Buttons.Jump) != 0;
        var press = jump && !s.GearJump;
        s.GearJump = jump;
        if ((s.Gear & Mmu) != 0)
        {
            MmuStep(ref s, cmd);
            return;
        }
        if (s.Grounded && s.JetFuel < JetFull)
            s.JetFuel++;
        if (s.Grounded || s.RideBody != 0 || s.MantleTicks > 0)
        {
            s.Canopy = 0;
            s.GearDelay = 0;
            return;
        }
        var held = false;
        if ((s.GearDelay != 0 || s.Canopy is OpenChute or OpenGlider || press) && Eva.Vacuum(s.Position))
        {
            // No air to hold a canopy (past the space bubble's edge, or the sky's top): a parachute or a glider never opens
            // there, and one open stows; the suit and its MMU are what a spacewalker has (docs/sandbox/EVA.md). The jetpack burns.
            // An ejection seat's chute waits, its delay held, and opens once they fall into air.
            held = true;
            if (s.Canopy is OpenChute or OpenGlider)
                s.Canopy = 0;
            press = false;
        }
        if (!held && s.GearDelay > 0 && --s.GearDelay == 0)
            s.Canopy = OpenChute;
        if (s.Canopy == Burning)
            s.Canopy = 0;
        var up = GravityFrames.UpAt(s.Position);
        var climb = Vector3.Dot(s.Velocity, up);
        var across = s.Velocity - up * climb;
        if (s.Canopy != 0 && press)
        {
            s.Canopy = 0; // (cut away)
            return;
        }
        if (s.Canopy == 0 && s.GearDelay == 0)
        {
            if (jump && (s.Gear & Jet) != 0 && s.JetFuel >= JetBurn)
                s.Canopy = Burning;
            else if (press && climb < -OpenFall)
                s.Canopy = (s.Gear & Glider) != 0 ? OpenGlider : (s.Gear & Chute) != 0 ? OpenChute : (byte)0;
            if (s.Canopy != 0)
                s.RideFree = 0; // (off a moving construct: the gear steers now)
        }
        if (s.Canopy == 0)
            return;
        var dt = World.TickDt;
        var fall = World.Gravity * World.GravityScale * dt; // (the mover's own tick takes this off again)
        switch (s.Canopy)
        {
            case OpenChute:
                across += (MoveInput.Wish(cmd.Buttons, cmd.Yaw) * ChuteSteer - across) * (1f - MathF.Exp(-1.2f * dt));
                climb += (-ChuteSink - climb) * (1f - MathF.Exp(-2.5f * dt)) + fall;
                break;
            case OpenGlider:
            {
                var speed = across.Length();
                var way = speed > 4f ? across / speed : World.FlatForward(cmd.Yaw);
                var turn = ((cmd.Buttons & Buttons.Right) != 0 ? 1f : 0f) - ((cmd.Buttons & Buttons.Left) != 0 ? 1f : 0f);
                if (turn != 0)
                    way = Vector3.Transform(way, Quaternion.CreateFromAxisAngle(up, -turn * GlideTurn * dt));
                var dive = (cmd.Buttons & Buttons.Forward) != 0;
                var flare = (cmd.Buttons & Buttons.Back) != 0;
                var wantSpeed = dive ? GlideDive : flare ? GlideFlare : GlideSpeed;
                var sink = dive ? GlideDiveSink : flare ? GlideFlareSink : GlideSink;
                speed += (wantSpeed - speed) * (1f - MathF.Exp(-1f * dt));
                across = way * speed;
                climb += (-sink - climb) * (1f - MathF.Exp(-3f * dt)) + fall;
                break;
            }
            default:
                across += (MoveInput.Wish(cmd.Buttons, cmd.Yaw) * JetSteer - across) * (1f - MathF.Exp(-2f * dt));
                if (climb < JetClimb)
                    climb = MathF.Min(JetClimb, climb + JetAccel * dt);
                s.JetFuel -= JetBurn;
                break;
        }
        s.Velocity = across + up * climb;
    }

    /// <summary>
    /// A Manned Manoeuvring Unit (docs/sandbox/EVA.md): out of any ride frame (World.Simulate takes them out of one when they
    /// fire), the move keys push along the look (W and S along it, pitch and all; A and D across), jump up and crouch down
    /// along the look's own up, rolled by <see cref="InputCmd.Roll"/>, at <see cref="MmuAccel"/> while its tank lasts; a push
    /// lifts them off what they stand on. With its dampeners on (<see cref="InputCmd.MmuCoast"/> false, the default) and no key
    /// held, it brakes them to the nearest craft's motion (<see cref="MmuHoldBody"/>) at the same push; off, what they have they
    /// keep. Its fuel is its own (the dock fills it), so the ground doesn't refill it; a unit lasts <see cref="MmuBurnEvery"/>
    /// thrusting (or braking) ticks.
    /// </summary>
    private static void MmuStep(ref MoveState s, in InputCmd cmd)
    {
        if (s.RideBody != 0)
        {
            s.Canopy = 0;
            return;
        }
        var push = MmuPush(ref s, cmd);
        if (push == Vector3.Zero)
        {
            if (!cmd.MmuCoast && ConstructRide.Bodies is { } w && MmuHoldBody(w, s.Position) is { } c)
            {
                var rel = s.Velocity - ConstructRide.SurfaceVel(c, 0, s.Position);
                s.Velocity -= MmuBrake(ref s, cmd, rel);
            }
            return;
        }
        s.Velocity += push;
        s.Grounded = false; // (a push off the floor: the pack flies, it doesn't walk)
    }

    public const float MmuHoldReach = 200f; // m from a craft's hull within which the dampeners hold a spacewalker to its motion
    public const float MmuHoldSize = 3f; // m: the least a body's radius may be to be held to (a craft, not a loose bolt)
    public const float MmuStill = 0.03f; // m/s: slower than this against what they're held to, the dampeners rest (no fuel)

    /// <summary>The craft a spacewalker out of any frame is held to by the dampeners: the nearest hull within <see cref="MmuHoldReach"/>, or null.</summary>
    public static Construct? MmuHoldBody(ConstructWorld w, Vector3 at)
    {
        Construct? best = null;
        var bestGap = MmuHoldReach;
        var bodies = w.Bodies;
        for (var k = 0; k < bodies.Count; k++)
        {
            var c = bodies[k];
            if (c.Radius < MmuHoldSize || c.PartCount == 0)
                continue;
            var gap = Vector3.Distance(at, c.WorldCom) - c.Radius;
            if (gap < bestGap)
                (best, bestGap) = (c, gap);
        }
        return best;
    }

    /// <summary>
    /// The dampeners' brake this tick against <paramref name="rel"/> (the velocity to take away, in the caller's frame), at most
    /// the pack's push; fuel burned and puffs shown while it brakes, nothing below <see cref="MmuStill"/> or with the tank dry.
    /// </summary>
    public static Vector3 MmuBrake(ref MoveState s, in InputCmd cmd, Vector3 rel)
    {
        var speed = rel.Length();
        if (speed < MmuStill || s.JetFuel == 0 || s.MantleTicks > 0 || !float.IsFinite(speed))
            return Vector3.Zero;
        if (cmd.Seq % MmuBurnEvery == 0)
            s.JetFuel--;
        s.Canopy = Burning;
        return rel * (MathF.Min(speed, MmuAccel * World.TickDt) / speed);
    }

    /// <summary>This tick's push from the pack (a world velocity change; zero when no key is held or the tank is dry), its fuel burned and its puffs shown.</summary>
    public static Vector3 MmuPush(ref MoveState s, in InputCmd cmd)
    {
        s.Canopy = 0;
        if (s.MantleTicks > 0 || s.JetFuel == 0)
            return Vector3.Zero;
        var b = cmd.Buttons;
        float f = ((b & Buttons.Forward) != 0 ? 1 : 0) - ((b & Buttons.Back) != 0 ? 1 : 0);
        float r = ((b & Buttons.Right) != 0 ? 1 : 0) - ((b & Buttons.Left) != 0 ? 1 : 0);
        float u = ((b & Buttons.Jump) != 0 ? 1 : 0) - ((b & Buttons.Crouch) != 0 ? 1 : 0);
        if (f == 0 && r == 0 && u == 0)
            return Vector3.Zero;
        MmuAxes(s.Position, cmd.Yaw, cmd.Pitch, cmd.Roll, out var fwd, out var right, out var upLook); // (the look in the gravity frame there: a planet's or an orbit's)
        var wish = fwd * f + right * r + upLook * u;
        if (cmd.Seq % MmuBurnEvery == 0)
            s.JetFuel--;
        s.Canopy = Burning; // (others see its puffs)
        return Vector3.Normalize(wish) * (MmuAccel * World.TickDt);
    }
}
