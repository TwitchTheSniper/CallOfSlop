using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The one way game code asks "what does this ray or shape hit": bullets, line of sight, grenades, explosions, bot
/// perception and navigation probes, mantle and spawn checks, prop placement, the movers. Directions are unit vectors.
/// <see cref="PhysicsWorld"/> implements it today (a BVH over the map's colliders plus the terrain heightfield and the
/// movers); an engine-backed world can implement it later and be compared against it in the self-test.
/// </summary>
public interface IWorldQuery
{
    /// <summary>The terrain heightfield, if the map has one.</summary>
    ITerrainCollision? Terrain { get; }

    /// <summary>Distance to the closest hit along the ray, or <paramref name="maxDist"/> if nothing is hit before it.</summary>
    float Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter);

    /// <summary>The closest hit along the ray (distance, point, normal, shape). False if nothing is hit before <paramref name="maxDist"/>.</summary>
    bool Raycast(Vector3 origin, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit);

    /// <summary>A sphere swept along the ray: the first contact (the hit point is on the surface).</summary>
    bool SphereCast(Vector3 origin, float radius, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit);

    /// <summary>A capsule swept along a direction: the first contact.</summary>
    bool CapsuleCast(in Capsule capsule, Vector3 dir, float maxDist, in QueryFilter filter, out RayHit hit);

    /// <summary>Does the box overlap anything (touching doesn't count)?</summary>
    bool OverlapBox(in Obb box, in QueryFilter filter);

    /// <summary>Does the capsule overlap anything (touching doesn't count)?</summary>
    bool OverlapCapsule(in Capsule capsule, in QueryFilter filter);

    /// <summary>
    /// Would a player's classic collision body (a square column of half width <see cref="World.PlayerRadius"/> from the feet
    /// up <paramref name="height"/>) overlap anything, or be under the terrain? Exactly the rules the classic movement uses.
    /// </summary>
    bool OverlapBody(Vector3 feet, float height, in QueryFilter filter);

    /// <summary>Is the point inside something grown by <paramref name="radius"/> (the small-projectile test)?</summary>
    bool PointInside(Vector3 point, float radius, in QueryFilter filter);
}
