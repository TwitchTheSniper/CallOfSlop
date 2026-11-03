using System.Numerics;

namespace CallOfSlop.Space;

/// <summary>
/// The game World tied to a star system (phase 3, docs/space/SPACE.md "The bubble"): which body the map is played on and
/// where on its ground the game's origin sits. The World is the body-fixed frame there (+Y up, -Z north, +X east;
/// <see cref="StarSystem.BubbleOn"/>), so the ground never moves under the 60 Hz physics, while the sun, the moons and the
/// other bodies move across the sky as the space clock runs. Everything here is a pure function of the system and the
/// time, so every peer works it out for itself: only the clock goes over the wire. No query allocates.
/// </summary>
public sealed class SpaceBubble
{
    public readonly StarSystem System;

    /// <summary>The body the map is played on.</summary>
    public readonly int Body;

    /// <summary>Where on the body the game's origin is (radians; latitude 90 is the spin axis's north pole) and how high above its radius.</summary>
    public readonly double Latitude, Longitude, Height;

    /// <summary>The body this one's sky shows as its moon: its nearest child (smallest orbit), or -1 when it has none.</summary>
    public readonly int Moon;

    /// <summary>
    /// A bubble on <paramref name="body"/>. The default anchor (latitude 90, height 0) is the planetoid maps' own
    /// (Source/Planets): their centre is the game's (0, -radius, 0) and game +Y is the spin axis.
    /// </summary>
    public SpaceBubble(StarSystem system, int body, double latitude = Math.PI / 2, double longitude = 0, double height = 0)
    {
        if ((uint)body >= (uint)system.Count)
            throw new ArgumentOutOfRangeException(nameof(body));
        System = system;
        Body = body;
        Latitude = latitude;
        Longitude = longitude;
        Height = height;
        Moon = -1;
        for (var b = 0; b < system.Count; b++)
            if (system.Bodies[b].Parent == body && (Moon < 0 || system.Bodies[b].Orbit.A < system.Bodies[Moon].Orbit.A))
                Moon = b;
    }

    public SpaceBody BodyInfo => System.Bodies[Body];

    /// <summary>The body's centre in game coordinates (for a planetoid map, its <c>PlanetCenter</c>).</summary>
    /// (The origin is R + h straight up from the centre, and game +Y is that "up", wherever the anchor is.)
    public Vec3d GameCentre => new(0, -(System.Bodies[Body].Radius + Height), 0);

    /// <summary>The World's frame at a time (origin, game axes in ecliptic coordinates, velocity and spin).</summary>
    public FramePose PoseAt(SpaceTime t) => System.BubbleOn(Body, Latitude, Longitude, Height, t);

    /// <summary>A system position in game coordinates (metres from the game's origin, game axes) for a pose from <see cref="PoseAt"/>.</summary>
    public static Vec3d ToGame(in FramePose pose, SpacePos p) => pose.FromEcliptic(p);

    /// <summary>A game position (metres, game axes) in the system at a pose's time.</summary>
    public static SpacePos ToSystem(in FramePose pose, Vec3d game) => pose.ToEcliptic(game);

    /// <summary>A game position's velocity in the system (the ground's spin and the body's orbit added).</summary>
    public static Vec3d VelocityToSystem(in FramePose pose, Vec3d game, Vec3d gameVel) => pose.VelocityToEcliptic(game, gameVel);

    /// <summary>Where a body is in game coordinates at a time.</summary>
    public Vec3d BodyInGame(int body, SpaceTime t) => PoseAt(t).FromEcliptic(System.PositionOf(body, t));

    /// <summary>
    /// The way to the star (unit, game axes) from a point in the World. The star is far enough away that the point hardly
    /// matters, but the moon's is worked out from it (<see cref="Look"/>).
    /// </summary>
    public Vector3 SunDirection(SpaceTime t, Vector3 from = default)
    {
        Look(0, t, from, out var dir, out _, out _);
        return dir;
    }

    /// <summary>
    /// How a body looks from a point in the World: the way to it (unit, game axes), its distance (m) and its angular radius
    /// (radians). False for the bubble's own body.
    /// </summary>
    public bool Look(int body, SpaceTime t, Vector3 from, out Vector3 dir, out double distance, out double angularRadius)
    {
        dir = Vector3.UnitY;
        distance = 0;
        angularRadius = 0;
        if (body == Body || (uint)body >= (uint)System.Count)
            return false;
        var d = BodyInGame(body, t) - Vec3d.From(from);
        distance = d.Length;
        if (!(distance > 0))
            return false;
        dir = (d / distance).ToVector3();
        angularRadius = Math.Asin(Math.Min(1, System.Bodies[body].Radius / distance));
        return true;
    }

    /// <summary>
    /// How high the star stands over the local horizon at a point on the ground (degrees; the "up" there is away from the
    /// body's centre, so this works anywhere on a planetoid, not only at the pole).
    /// </summary>
    public double SunElevation(SpaceTime t, Vector3 at)
    {
        var up = Vec3d.From(at) - GameCentre;
        var l = up.Length;
        if (!(l > 0))
            return 0;
        var sun = Vec3d.From(SunDirection(t, at));
        return Math.Asin(Math.Clamp(Vec3d.Dot(sun, up / l), -1, 1)) * 180 / Math.PI;
    }
}
