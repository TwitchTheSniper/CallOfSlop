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
    public const byte Chute = 1, Glider = 2, Jet = 4; // MoveState.Gear's bits
    public const byte OpenChute = 1, OpenGlider = 2, Burning = 3; // MoveState.Canopy
    public const float OpenFall = 5f; // m/s down before a canopy will open: a jump isn't a fall
    public const float ChuteSink = 5.5f, ChuteSteer = 5f;
    public const float GlideSpeed = 17f, GlideDive = 25f, GlideFlare = 10f, GlideSink = 3f, GlideDiveSink = 7f, GlideFlareSink = 2.2f, GlideTurn = 1.2f;
    public const float JetClimb = 12f, JetAccel = 34f, JetSteer = 10f;
    public const ushort JetFull = 720, JetBurn = 3; // a full tank: four seconds alight, twelve on the ground to fill

    /// <summary>The ticks from an ejection seat's throw until its parachute opens.</summary>
    public const byte EjectDelay = 45;

    public static void Step(ref MoveState s, in InputCmd cmd)
    {
        var jump = (cmd.Buttons & Buttons.Jump) != 0;
        var press = jump && !s.GearJump;
        s.GearJump = jump;
        if (s.Grounded && s.JetFuel < JetFull)
            s.JetFuel++;
        if (s.Grounded || s.RideBody != 0 || s.MantleTicks > 0)
        {
            s.Canopy = 0;
            s.GearDelay = 0;
            return;
        }
        if (s.GearDelay > 0 && --s.GearDelay == 0)
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
}
