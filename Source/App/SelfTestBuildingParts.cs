using System.Numerics;

namespace CallOfSlop;

public static partial class SelfTest
{

    internal const float BuildingY = 300f; // (the headless checks build up here, clear of whatever map is loaded; the walker's ground is at this height)

    /// <summary>A walker for the checks: walks along <paramref name="dir"/> (per position) at walking speed, falls under gravity to the ground at BuildingY or a construct's top, kept out of constructs by BlockPlayer.</summary>
    internal static MoveState BuildingWalk(ConstructWorld w, Vector3 start, Func<Vector3, Vector3> dir, int ticks, float height, out float highest, float speed = 4f)
    {
        var s = new MoveState { Position = start, Grounded = true };
        highest = start.Y;
        for (var t = 0; t < ticks; t++)
        {
            var d = dir(s.Position);
            // On a free construct the player lives in its frame, and the real mover walks them (ConstructRideMove.cs): BlockPlayer doesn't push a rider out.
            if (s.RideUp != Vector3.Zero && ConstructRide.Step(ref s, new InputCmd { Buttons = Buttons.Forward, Yaw = MathF.Atan2(d.X, -d.Z), SpeedPct = 100 }))
            {
                w.BlockPlayer(ref s, height, server: true);
                highest = MathF.Max(highest, s.Position.Y);
                continue;
            }
            s.Velocity.X = d.X * speed;
            s.Velocity.Z = d.Z * speed;
            s.Position.X += s.Velocity.X * World.TickDt;
            s.Position.Z += s.Velocity.Z * World.TickDt;
            s.Velocity.Y -= 20f * World.TickDt;
            s.Position.Y += s.Velocity.Y * World.TickDt;
            if (s.Position.Y <= BuildingY)
            {
                s.Position.Y = BuildingY;
                s.Velocity.Y = 0;
            }
            s.Grounded = s.Position.Y <= BuildingY;
            w.BlockPlayer(ref s, height, server: true);
            highest = MathF.Max(highest, s.Position.Y);
        }
        return s;
    }
}
