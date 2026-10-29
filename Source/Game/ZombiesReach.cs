using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The one reach rule every Zombies interactable uses (board item dev-use-height-limit-uh01): besides each thing's own
/// flat range, the player's eye must be within a head's height of the thing's height band (from
/// <see cref="ReachBelow"/> under its bottom to <see cref="ReachAbove"/> over its top) and nothing solid may stand between
/// the eye and the thing (no buying a slot machine through the floor above or below, or through a wall). Both sides use
/// it: the client for the prompt, the server for every buy (<see cref="FindZUse"/> runs on the server's own copy of the
/// player), so a forged use from the wrong floor does nothing. Co-op bots buy through <see cref="FindZUse"/> too.
/// </summary>
public sealed partial class Game
{
    /// <summary>How far over the top of a thing's height band the eye may be.</summary>
    public const float ReachAbove = 1.2f;

    /// <summary>How far under the bottom of a thing's height band the eye may be.</summary>
    public const float ReachBelow = 0.3f;

    /// <summary>The height band of a thing standing on the floor at its spot (a machine, a table, a pile, a pickup): spot to this far up.</summary>
    public const float FloorBand = 1.2f;

    /// <summary>A prop hit this close (flat) to the thing being reached is the thing's own body (a machine's cabinet), not a wall.</summary>
    private const float OwnPropRadius = 1.5f;

    /// <summary>What blocks a reach: everything solid, but not the Zombies window openings (you rebuild boards through them).</summary>
    private static QueryFilter ReachFilter => new(QueryLayers.World & ~(QueryLayers.Clip | QueryLayers.Boundary));

    /// <summary>
    /// Can <paramref name="p"/> reach a thing at <paramref name="at"/> whose height band runs from
    /// <c>at.Y + bandLo</c> to <c>at.Y + bandHi</c>? (Height and sight only: the caller checks its own flat range.)
    /// </summary>
    public bool ZReach(Player p, Vector3 at, float bandLo, float bandHi) => ZReachFrom(p.Eye, at, bandLo, bandHi);

    /// <summary>A thing standing on the floor at <paramref name="spot"/> (<see cref="FloorBand"/>).</summary>
    public bool ZReachSpot(Player p, Vector3 spot) => ZReachFrom(p.Eye, spot, 0, FloorBand);

    /// <summary><see cref="ZReach"/> from an eye position.</summary>
    public static bool ZReachFrom(Vector3 eye, Vector3 at, float bandLo, float bandHi)
    {
        var lo = at.Y + bandLo;
        var hi = at.Y + bandHi;
        if (eye.Y < lo - ReachBelow || eye.Y > hi + ReachAbove)
            return false;
        return ZClearSight(eye, new Vector3(at.X, Math.Clamp(eye.Y, lo, hi), at.Z));
    }

    /// <summary>
    /// Is the straight line from <paramref name="eye"/> to <paramref name="target"/> clear of world geometry? It stops a
    /// little short of the target (a wall buy's chalk is on the wall), and a prop hit right at the target is the thing's
    /// own body, so props are looked past there (the wall or floor behind it still counts).
    /// </summary>
    public static bool ZClearSight(Vector3 eye, Vector3 target)
    {
        var to = target - eye;
        var dist = to.Length();
        if (dist < 0.3f)
            return true;
        var dir = to / dist;
        var max = dist - 0.2f;
        var filter = ReachFilter;
        if (!World.Query.Raycast(eye, dir, max, filter, out var hit))
            return true;
        if (hit.Shape.Kind != ShapeKind.Prop)
            return false;
        var off = hit.Point - target;
        if (off.X * off.X + off.Z * off.Z > OwnPropRadius * OwnPropRadius)
            return false; // a prop out in the way (a wall of shelves, a pillar)
        return !World.Query.Raycast(eye, dir, max, filter.Without(QueryLayers.Props), out _);
    }
}
