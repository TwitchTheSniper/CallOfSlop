using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Vertical curves on chutes and conveyors (docs/sandbox/design/conveyor-inclines.md section 3, the user's answer of 2026-11-17: the look
/// only). Where a link's grade alone changes (level to an incline, an incline to level), the bed is drawn round an arc tangent to both
/// segments, starting and ending a tangent length either side of the node, and a riding piece is drawn following it. Nothing here
/// changes what the link carries or how fast: a piece's distance along the link, the link's length and every timing still come from
/// the straight segments between its nodes (<see cref="ConstructConveyor.Length"/>); the curve only moves where a piece inside the
/// window is drawn. A turn in plan, a corner into or out of a vertical leg and a link of any other type keep today's elbows.
/// Allocates nothing.
/// </summary>
public static class RouteCurves
{
    public const float MinRadius = 1.5f; // a conveyor's curve radius at least (m)...
    public const float WidthRadii = 3f; // ...and at least this many bed widths
    public const float ChuteRadius = 1.2f; // a chute's
    public const float MinDeg = 2f; // a change of grade under this is drawn straight (the 1 cm grid's slope)
    public const float MaxDeg = 80f; // over this it is a corner, as today
    public const float RoomShare = 0.45f; // a curve takes at most this share of either segment, so two curves never meet

    private static readonly float CosMin = MathF.Cos(MinDeg * MathF.PI / 180f), CosMax = MathF.Cos(MaxDeg * MathF.PI / 180f);

    public static float Radius(RouteType type, float width) => type == RouteType.Conveyor ? MathF.Max(MinRadius, WidthRadii * width) : ChuteRadius;

    /// <summary>
    /// The tangent length of the curve at a node between a segment along <paramref name="dIn"/> (<paramref name="lenIn"/> long) and one along
    /// <paramref name="dOut"/> (unit ways), or 0 when the node is drawn as a corner.
    /// </summary>
    public static float Tangent(RouteType type, float width, Vector3 dIn, float lenIn, Vector3 dOut, float lenOut)
    {
        if (type is not (RouteType.Conveyor or RouteType.Chute) || lenIn < 1e-3f || lenOut < 1e-3f)
            return 0f;
        var cos = Vector3.Dot(dIn, dOut);
        if (cos > CosMin || cos < CosMax || MathF.Abs(dIn.Y) > 0.995f || MathF.Abs(dOut.Y) > 0.995f || !ConstructRoutes.GradeOnly(dIn, dOut))
            return 0f;
        var half = MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 0.5f;
        return MathF.Min(Radius(type, width) * MathF.Tan(half), RoomShare * MathF.Min(lenIn, lenOut));
    }

    /// <summary>
    /// The point and the way of the curve at a node at <paramref name="u"/> (0: a tangent back along the way in, 1: a tangent on along the
    /// way out). A quadratic curve on the two tangent points and the node, which for equal tangents is within a fraction of a percent of
    /// the circle's arc and turns its way evenly from the one segment's to the other's.
    /// </summary>
    public static void At(Vector3 node, Vector3 dIn, Vector3 dOut, float t, float u, out Vector3 p, out Vector3 way)
    {
        var s = node - dIn * t;
        var e = node + dOut * t;
        var v = 1f - u;
        p = s * (v * v) + node * (2f * u * v) + e * (u * u);
        var w = dIn * v + dOut * u;
        way = w.LengthSquared() > 1e-8f ? Vector3.Normalize(w) : dOut;
    }

    /// <summary>How many straight pieces a curve is drawn as (3 to 8, about one each 5 degrees).</summary>
    public static int Pieces(Vector3 dIn, Vector3 dOut)
    {
        var deg = MathF.Acos(Math.Clamp(Vector3.Dot(dIn, dOut), -1f, 1f)) * 180f / MathF.PI;
        return Math.Clamp((int)MathF.Ceiling(deg / 5f), 3, 8);
    }
}
