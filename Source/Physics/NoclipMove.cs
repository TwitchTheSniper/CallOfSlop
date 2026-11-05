using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Creative Sandbox's noclip flight (docs/sandbox/CONSTRUCTS.md, "Noclip"): no gravity and no collision, the look decides
/// the direction. It is a branch of <see cref="World.Simulate"/>, so the server, the client's prediction and its replays run
/// the same step from the same inputs. <see cref="InputCmd.Noclip"/> is the player's wish, sent with every input and
/// filtered by whoever runs the step (<see cref="Game.NoclipAllowed"/>); <see cref="MoveState.Noclip"/> is what is actually
/// happening. The speed lives in <see cref="MoveState.Velocity"/>, so a long Sprint needs no state of its own and a
/// snapshot carries it.
/// </summary>
public static class NoclipMove
{
    /// <summary>Flying speed (m/s), and with Sprint held.</summary>
    public const float Speed = 10f, FastSpeed = 40f;

    /// <summary>
    /// Sprint held on past <see cref="FastSpeed"/>: the speed keeps building towards this (m/s; 25 km in about 2 minutes), with no
    /// mouse wheel to learn. A second or so stays near <see cref="FastSpeed"/>, for short hops; about 5 s reaches the top.
    /// </summary>
    public const float MaxSpeed = 240f;

    /// <summary>The build-up: speed over <see cref="RampFrom"/> grows by this much of itself per second.</summary>
    public const float RampRate = 1f, RampFrom = 38f;

    /// <summary>How quickly the velocity follows the keys (1/s): 0.25 s to a stop.</summary>
    public const float Control = 12f;

    /// <summary>How far above the highest ground and below the lowest the flight goes on a flat map (m).</summary>
    public const float Ceiling = 2000f, Floor = 200f;

    /// <summary>Leaving inside something solid: the search goes up this far at most (m; a capital ship is a few hundred metres).</summary>
    public const float MaxRise = 800f;

    /// <summary>
    /// Leaving noclip keeps the speed. <see cref="MoveState.RideFree"/> at this value doesn't count down: it holds until the
    /// player lands, with air steering of <see cref="FlungControl"/> (1/s), nearly none, so the flight carries on as a throw.
    /// </summary>
    public const byte Flung = 255;

    /// <summary>Air steering while <see cref="Flung"/> (1/s): gravity and what they hit decide the rest.</summary>
    public const float FlungControl = 0.1f;

    /// <summary>A mover's tick start: <see cref="MoveState.RideFree"/> counts down, a flung player's ends on landing.</summary>
    public static void TickFree(ref MoveState s)
    {
        if (s.RideFree == Flung)
            s.RideFree = s.Grounded ? (byte)0 : Flung;
        else if (s.RideFree > 0)
            s.RideFree--;
    }

    /// <summary>One tick of a noclip player: going in, flying, or coming out (<see cref="MoveState.NoclipExit"/>: the game then finds room).</summary>
    public static void Step(ref MoveState s, in InputCmd cmd)
    {
        if (!cmd.Noclip)
        {
            s.Noclip = false;
            s.NoclipExit = true;
            s.RideFree = Flung; // (the speed stays: a flick at something, carried until they land)
            return;
        }
        if (!s.Noclip)
        {
            s.Noclip = true;
            s.Velocity = Vector3.Zero;
            s.BoostTicks = s.KnockTicks = s.RideFree = 0;
        }
        s.Grounded = false;
        s.Stance = Stance.Stand;
        s.MantleTicks = 0;
        ConstructRide.Clear(ref s); // (the position is the world's already; a frame can't hold someone flying)

        var at = s.Position;
        var b = cmd.Buttons;
        var wish = Vector3.Zero;
        if ((b & Buttons.Forward) != 0) wish += GravityFrames.Forward(at, cmd.Yaw, cmd.Pitch);
        if ((b & Buttons.Back) != 0) wish -= GravityFrames.Forward(at, cmd.Yaw, cmd.Pitch);
        if ((b & Buttons.Right) != 0) wish += GravityFrames.FlatRight(at, cmd.Yaw);
        if ((b & Buttons.Left) != 0) wish -= GravityFrames.FlatRight(at, cmd.Yaw);
        if ((b & Buttons.Jump) != 0) wish += GravityFrames.UpAt(at);
        if ((b & (Buttons.Crouch | Buttons.Prone)) != 0) wish -= GravityFrames.UpAt(at);
        if (wish.LengthSquared() > 1e-6f)
            wish = Vector3.Normalize(wish);
        else
            wish = Vector3.Zero;

        var blend = 1f - MathF.Exp(-Control * World.TickDt);
        var speed = Speed;
        if ((b & Buttons.Sprint) != 0)
        {
            var now = s.Velocity.Length();
            // (the step below takes `blend` of the gap, so the gap is the wanted rise over blend)
            speed = MathF.Min(MaxSpeed, MathF.Max(FastSpeed, now + (now - RampFrom) * RampRate * World.TickDt / blend));
        }
        s.Velocity += (wish * speed - s.Velocity) * blend;
        s.Position = Bounds(at + s.Velocity * World.TickDt);
    }

    /// <summary>
    /// The space a noclip flight has: the map's rectangle (the snapshots, the streamed ground and every mover assume players
    /// stay in it) from well under the ground to well over it; on a planet, the sky's edge players have anyway
    /// (<see cref="World.ClampToSky"/>, which is under the edge of the space bubble) and any depth.
    /// </summary>
    public static Vector3 Bounds(Vector3 feet)
    {
        var map = World.Map;
        if (GravityFrames.Point is { } planet)
        {
            var d = feet - planet.Center;
            var len = d.Length();
            var max = MathF.Max(map.HalfX, map.HalfZ);
            if (len > max)
                return planet.Center + d / len * max;
            return len < 2f ? planet.Center + (len > 1e-3f ? d / len : Vector3.UnitY) * 2f : feet; // (the planet's very centre has no up)
        }
        feet.X = Math.Clamp(feet.X, -map.HalfX + World.PlayerRadius, map.HalfX - World.PlayerRadius);
        feet.Z = Math.Clamp(feet.Z, -map.HalfZ + World.PlayerRadius, map.HalfZ - World.PlayerRadius);
        var terrain = map.TerrainCollision;
        feet.Y = Math.Clamp(feet.Y, (terrain?.Data.LowestGround ?? 0f) - Floor, (terrain?.Data.HighestGround ?? 0f) + Ceiling);
        return feet;
    }

    /// <summary>
    /// Where someone leaving noclip ends up: where they are if there's room for a standing body, else the first room straight
    /// up (out of the ground, through a roof, out of a hull). The arena's rules hold first: inside the map and on top of the ground.
    /// </summary>
    public static Vector3 ExitSpot(Vector3 feet, uint tick, ConstructWorld? bodies)
    {
        feet = World.ClampToArena(feet);
        var up = GravityFrames.UpAt(feet);
        for (var rise = 0f; rise <= MaxRise; rise = MathF.Max(rise + 0.25f, rise * 1.1f))
        {
            var p = feet + up * rise;
            if (!Solid(p, up, tick, bodies))
                return p;
        }
        return feet;
    }

    private static bool Solid(Vector3 feet, Vector3 up, uint tick, ConstructWorld? bodies)
    {
        var solid = GravityFrames.Curved
            ? World.Physics.OverlapCapsule(SweptMover.CapsuleAt(feet, Stance.Stand, up), new QueryFilter(QueryLayers.Static | QueryLayers.Movers | QueryLayers.Boundary, tick))
            : MoverPhysics.BlockedAll(feet, World.PlayerHeight, tick);
        return solid || bodies != null && bodies.BodyIn(feet, World.PlayerHeight);
    }
}
