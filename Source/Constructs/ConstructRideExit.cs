using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Getting out of a seat on a moving construct (docs/sandbox/CONSTRUCTS.md, "Getting out on the move"): the search for a
/// place to stand on the construct. It works in the seat's own directions and the rider's up, so a vehicle that turns,
/// climbs or rolls is the same search, and it runs once per exit.
/// </summary>
public static partial class ConstructRide
{
    /// <summary>How far from the seat (m) a spot is looked for, nearest first; how far above the seat's feet the search starts, and how far down it reaches.</summary>
    private const float ExitAbove = 1f, ExitDrop = 2.2f;

    private static readonly float[] ExitReach = [0.75f, 1.1f, 1.5f, 2f, 2.6f, 3.2f];

    /// <summary>The ways out of a seat: x to its right, y to its back. Left first, as the plain exit.</summary>
    private static readonly Vector2[] ExitWays = [new(-1, 0), new(1, 0), new(0, 1), new(0, -1), new(-0.7f, 0.7f), new(0.7f, 0.7f), new(-0.7f, -0.7f), new(0.7f, -0.7f)];

    /// <summary>The feet can leave the deck for a surface this much (m/s) off the seat's speed: more and it isn't the same vehicle.</summary>
    private const float ExitMatch = 2f;

    /// <summary>Is there the map's ground (or a floor) close under these feet?</summary>
    public static bool GroundUnder(Vector3 feet)
    {
        const float reach = 3f;
        var up = GravityFrames.UpAt(feet);
        return World.Query.Raycast(feet + up * 0.5f, -up, reach, QueryFilter.All) < reach - 0.01f;
    }

    /// <summary>
    /// A plain exit's spot on a moving construct: to the side of its way, not in line with it (a body whose side faces its
    /// way would put the rider in front of it, or on a client's copy of it, a round trip behind).
    /// </summary>
    public static Vector3 OffTheLine(Construct c, Vector3 feet, Vector3 vel)
    {
        var com = c.WorldCom;
        var off = new Vector3(feet.X - com.X, 0, feet.Z - com.Z);
        var way = new Vector3(vel.X, 0, vel.Z);
        if (off.LengthSquared() < 1e-4f || way.LengthSquared() < 1e-4f)
            return feet;
        way = Vector3.Normalize(way);
        if (MathF.Abs(Vector3.Dot(Vector3.Normalize(off), way)) < 0.7f)
            return feet;
        var clear = MathF.Max(MathF.Max(c.LocalMax.X - c.LocalMin.X, c.LocalMax.Z - c.LocalMin.Z), 1.5f) * 0.5f + 0.9f;
        return new Vector3(com.X, feet.Y, com.Z) + new Vector3(-way.Z, 0, way.X) * clear;
    }

    /// <summary>
    /// A spot to stand on near a seat: something that moves with the seat under the feet (this construct, or one it stands
    /// on, a car on a ferry), room for the body, no map geometry in the way. One in sight of the seat first, then any.
    /// </summary>
    public static bool ExitDeck(ConstructWorld w, Construct c, int seat, float height, Vector3 seatVel, out Construct deck, out int seg, out Vector3 feet)
    {
        deck = c;
        seg = 0;
        feet = default;
        ref var p = ref c.Parts[seat];
        c.WorldFrame(p.Seg, out var q, out _);
        var from = ConstructEdits.SeatFeet(c, seat);
        var worldUp = GravityFrames.UpAt(from);
        var up = Vector3.Normalize(Vector3.Transform(UpFor(c, worldUp), QuatOf(c, c.MoveSeg(p.Seg))));
        var fwd = Vector3.Transform(Rot24.Apply(p.Rot, -Vector3.UnitZ), q);
        fwd -= up * Vector3.Dot(fwd, up);
        if (fwd.LengthSquared() < 1e-3f)
            fwd = Vector3.Cross(up, MathF.Abs(up.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ); // (a seat laid on its back)
        fwd = Vector3.Normalize(fwd);
        var right = Vector3.Cross(fwd, up);
        var chest = from + up * (height * 0.5f);
        for (var pass = 0; pass < 2; pass++)
            foreach (var reach in ExitReach)
                foreach (var way in ExitWays)
                {
                    var over = from + (right * way.X - fwd * way.Y) * reach;
                    if (pass == 0 && w.Raycast(chest, Vector3.Normalize(over - from), reach, out _))
                        continue; // (through a wall: only if there's nothing on this side)
                    if (!w.Raycast(over + up * ExitAbove, -up, ExitDrop, out var hit) || hit.Part < 0 || hit.Part >= hit.Body.PartCount || hit.Body.IsRaycastWheel(hit.Part))
                        continue;
                    var body = hit.Body;
                    var on = body.Parts[hit.Part].Seg;
                    if (Vector3.Dot(hit.Normal, up) < World.MinWalkNormalY)
                        continue;
                    var spot = hit.Point + up * 0.01f;
                    if (Vector3.DistanceSquared(SurfaceVel(body, on, spot), seatVel) > ExitMatch * ExitMatch || !BodyFits(w, c, body, spot, up, height))
                        continue;
                    (deck, seg, feet) = (body, on, spot);
                    return true;
                }
        return false;
    }

    private static bool BodyFits(ConstructWorld w, Construct c, Construct deck, Vector3 feet, Vector3 up, float height)
    {
        var r = World.PlayerRadius * 0.9f;
        var a = feet + up * (r + 0.02f); // (the bottom of the body clears the floor by this)
        var b = feet + up * MathF.Max(r + 0.02f, height - r);
        if (!w.CapsuleClear(c, a, b, r) || (deck != c && !w.CapsuleClear(deck, a, b, r)))
            return false;
        var foot = up * WorldFootSlack; // (as the frame's own check: a thin plate on rough ground)
        var capsule = new Capsule(feet + foot + up * r, feet + up * MathF.Max(r + WorldFootSlack, height - r), r);
        return !World.Physics.OverlapCapsule(capsule, new QueryFilter(QueryLayers.Static | QueryLayers.Movers | QueryLayers.Boundary, MoverPhysics.Clock));
    }
}
