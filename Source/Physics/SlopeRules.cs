using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// How players (and bots) move on terrain slopes. Both movers (<see cref="ClassicMover"/>, <see cref="SweptMover"/>) and the
/// bots' navigation use these rules, so what the nav graph calls walkable is exactly what a player can walk. Terrain only:
/// boxes, oriented boxes and meshes keep the plain <see cref="World.MaxSlopeDeg"/> rule.
/// <list type="bullet">
/// <item>The ground's slope is the <see cref="SupportNormal"/>: the slope over the player's footprint (a 0.4 m radius), not the
/// single heightfield patch under the feet, so terrain noise doesn't make you stutter, stop or slide on a walkable hill.</item>
/// <item>Walkable up to <see cref="WalkLimitDeg"/> (45 degrees). Walking uphill is full speed up to <see cref="FullSpeedDeg"/>
/// (25 degrees), then gradually slower, down to <see cref="SlowAtLimit"/> of the speed at the limit (only the uphill part of the
/// move is slowed: across and down a slope is full speed).</item>
/// <item>Grip: while moving, a narrow steep patch (walkable ground <see cref="GripDistance"/> before and after it) that rises
/// no more than <see cref="World.StepHeight"/> against the slope around it is crossed. So brief steep bumps don't stop you or
/// start a slide, and small terrain lips and edges up to the step height are stepped over like box steps.</item>
/// <item>Steeper ground slides you down it. The slide is capped at <see cref="MaxSlideSpeed"/> downhill and you can steer
/// across and down it (<see cref="SlideControl"/>), so you can always steer out; you can't walk up it.</item>
/// </list>
/// Everything is plain float maths on the heightfield (no state, no randomness), so prediction and the server agree.
/// </summary>
public static class SlopeRules
{
    /// <summary>Terrain up to this steep (by <see cref="SupportNormal"/>) can be walked; steeper slides you down it.</summary>
    public const float WalkLimitDeg = 45f;

    /// <summary>cos(<see cref="WalkLimitDeg"/>).</summary>
    public const float MinWalkNormalY = 0.70710678f;

    /// <summary>tan(<see cref="WalkLimitDeg"/>): the steepest rise per metre of run.</summary>
    public const float TanWalkLimit = 1f;

    /// <summary>Walking up slopes up to this steep is full speed.</summary>
    public const float FullSpeedDeg = 25f;

    /// <summary>cos(<see cref="FullSpeedDeg"/>).</summary>
    public const float FullSpeedNormalY = 0.90630779f;

    /// <summary>The share of the speed left walking straight up a slope at <see cref="WalkLimitDeg"/> (linear in between).</summary>
    public const float SlowAtLimit = 0.6f;

    /// <summary>Radius of the footprint the ground's slope is measured over (the body's half width).</summary>
    public const float FootRadius = World.PlayerRadius;

    /// <summary>
    /// Heights in the footprint are clamped to this many metres per metre above and below the middle, so standing on the
    /// rim of a cliff top (or at the foot of a wall of rock) doesn't count as standing on the cliff. Any value between the
    /// tan of the walk limit and twice it keeps uniform slopes steeper than the limit steep and rims walkable.
    /// </summary>
    public const float SupportClamp = 1.5f * TanWalkLimit;

    /// <summary>How far past a steep patch the grip looks for walkable ground (along the move).</summary>
    public const float GripDistance = 0.45f;

    /// <summary>Fastest downhill slide on ground too steep to stand on (m/s, across the ground).</summary>
    public const float MaxSlideSpeed = 8f;

    /// <summary>Steering response while sliding (per second; walking is 14, the air 3).</summary>
    public const float SlideControl = 6f;

    /// <summary>
    /// The bots plan with a little margin: ground they route over must be walkable to cos(44.5 degrees), a bit inside the
    /// limit, so no route depends on a slope a player only just manages.
    /// </summary>
    public const float NavMinNormalY = 0.71325045f;

    /// <summary>tan(44.5 degrees): the steepest rise per metre of run a bot route takes.</summary>
    public const float NavTanLimit = 0.98269726f;

    /// <summary>Is the terrain walkable at (x, z) with the bots' planning margin (<see cref="NavMinNormalY"/>)?</summary>
    public static bool NavWalkable(ITerrainCollision t, float x, float z) => SupportNormal(t, x, z).Y >= NavMinNormalY;

    /// <summary>How close above the terrain the feet must be to count as on it (speed and slide rules).</summary>
    public const float OnGround = 0.05f;

    /// <summary>
    /// The ground's normal over a player's footprint at (x, z): a 3 x 3 Sobel stencil of the heights within
    /// <see cref="FootRadius"/>, each clamped to <see cref="SupportClamp"/> of the middle's height per metre.
    /// </summary>
    public static Vector3 SupportNormal(ITerrainCollision t, float x, float z)
    {
        const float r = FootRadius;
        const float c = SupportClamp * FootRadius;
        var h0 = t.HeightAt(x, z);
        float lo = h0 - c, hi = h0 + c;
        var nw = Math.Clamp(t.HeightAt(x - r, z - r), lo, hi);
        var n = Math.Clamp(t.HeightAt(x, z - r), lo, hi);
        var ne = Math.Clamp(t.HeightAt(x + r, z - r), lo, hi);
        var w = Math.Clamp(t.HeightAt(x - r, z), lo, hi);
        var e = Math.Clamp(t.HeightAt(x + r, z), lo, hi);
        var sw = Math.Clamp(t.HeightAt(x - r, z + r), lo, hi);
        var s = Math.Clamp(t.HeightAt(x, z + r), lo, hi);
        var se = Math.Clamp(t.HeightAt(x + r, z + r), lo, hi);
        var gx = ((ne + 2f * e + se) - (nw + 2f * w + sw)) * (1f / (8f * r));
        var gz = ((sw + 2f * s + se) - (nw + 2f * n + ne)) * (1f / (8f * r));
        return Vector3.Normalize(new Vector3(-gx, 1f, -gz));
    }

    /// <summary>Is a support normal walkable?</summary>
    public static bool Walkable(Vector3 support) => support.Y >= MinWalkNormalY;

    /// <summary>Is the terrain walkable at (x, z) (by its <see cref="SupportNormal"/>)?</summary>
    public static bool Walkable(ITerrainCollision t, float x, float z) => SupportNormal(t, x, z).Y >= MinWalkNormalY;

    /// <summary>
    /// Grip on a steep patch at (x, z) while moving along (dirX, dirZ): the patch is narrow (the ground
    /// <see cref="GripDistance"/> before and after it is walkable) and, against the slope around it (the trend of the next
    /// stretch out on each side), it rises or drops no more than <see cref="World.StepHeight"/>: a bump in a hillside or a
    /// terrain lip, not a bank (taller banks need a mantle). False when not moving.
    /// </summary>
    public static bool Grip(ITerrainCollision t, float x, float z, float dirX, float dirZ)
    {
        var len2 = dirX * dirX + dirZ * dirZ;
        if (len2 < 1e-6f)
            return false;
        var inv = 1f / MathF.Sqrt(len2);
        float dx = dirX * inv * GripDistance, dz = dirZ * inv * GripDistance;
        if (!Walkable(t, x + dx, z + dz) || !Walkable(t, x - dx, z - dz))
            return false;
        var ahead = t.HeightAt(x + dx, z + dz);
        var behind = t.HeightAt(x - dx, z - dz);
        var trend = 0.5f * ((t.HeightAt(x + 3f * dx, z + 3f * dz) - ahead) + (behind - t.HeightAt(x - 3f * dx, z - 3f * dz)));
        return MathF.Abs(ahead - behind - trend) <= World.StepHeight;
    }

    /// <summary>Can a player stand (keep walking) at (x, z): walkable there, or grip while moving along (dirX, dirZ)?</summary>
    public static bool CanStand(ITerrainCollision t, float x, float z, float dirX, float dirZ) =>
        Walkable(t, x, z) || Grip(t, x, z, dirX, dirZ);

    /// <summary>
    /// Speed multiplier for walking with the unit <paramref name="wish"/> direction on ground with this support normal:
    /// 1 up to <see cref="FullSpeedDeg"/>, then less the more uphill the move is, down to <see cref="SlowAtLimit"/> straight
    /// up a slope at the limit (and steeper, while grip holds). Across and down slopes: 1.
    /// </summary>
    public static float SpeedFactor(Vector3 support, Vector3 wish)
    {
        if (support.Y >= FullSpeedNormalY)
            return 1f;
        // The normal leans away from the uphill direction: uphill is -(n.x, n.z).
        var hl = MathF.Sqrt(support.X * support.X + support.Z * support.Z);
        if (hl < 1e-6f)
            return 1f;
        var up = -(wish.X * support.X + wish.Z * support.Z) / hl;
        if (up <= 0)
            return 1f;
        var k = MathF.Min(1f, (FullSpeedNormalY - support.Y) * (1f / (FullSpeedNormalY - MinWalkNormalY)));
        return 1f - MathF.Min(up, 1f) * k * (1f - SlowAtLimit);
    }

    /// <summary>
    /// The across-the-ground velocity a sliding player steers towards: the wish without its uphill part, plus the downhill
    /// speed they already have (gravity keeps that; <see cref="CapSlide"/> limits it).
    /// </summary>
    public static Vector2 SlideTarget(Vector3 support, Vector3 wish, float speed, Vector3 velocity)
    {
        var hl = MathF.Sqrt(support.X * support.X + support.Z * support.Z);
        if (hl < 1e-6f)
            return new Vector2(wish.X * speed, wish.Z * speed);
        float dx = support.X / hl, dz = support.Z / hl; // downhill
        var wx = wish.X;
        var wz = wish.Z;
        var down = wx * dx + wz * dz;
        if (down < 0)
        {
            wx -= dx * down; // no walking up it
            wz -= dz * down;
        }
        var keep = MathF.Max(0, velocity.X * dx + velocity.Z * dz);
        return new Vector2(wx * speed + dx * keep, wz * speed + dz * keep);
    }

    /// <summary>Limits the downhill part of a slide (across the ground) to <see cref="MaxSlideSpeed"/>.</summary>
    public static void CapSlide(Vector3 support, ref Vector3 velocity)
    {
        var hl = MathF.Sqrt(support.X * support.X + support.Z * support.Z);
        if (hl < 1e-6f)
            return;
        float dx = support.X / hl, dz = support.Z / hl;
        var down = velocity.X * dx + velocity.Z * dz;
        if (down <= MaxSlideSpeed)
            return;
        velocity.X -= dx * (down - MaxSlideSpeed);
        velocity.Z -= dz * (down - MaxSlideSpeed);
    }

    /// <summary>
    /// Could a player walk the terrain along the straight line a -> b (either way)? For the bots, so with their margin
    /// (<see cref="NavMinNormalY"/>). Samples every half metre (at most 128 samples): a sample is steep if the rise from the
    /// last one is steeper than that or the ground isn't walkable (<see cref="SupportNormal"/>). A single steep sample is fine when its rise is within a step (the movers' grip and
    /// terrain steps); two in a row are not. With <paramref name="onlyNearLine"/>, stretches where the line runs more than
    /// 1.5 m above or below the ground (on a box floor) don't count.
    /// </summary>
    public static bool LineWalkable(ITerrainCollision t, Vector3 a, Vector3 b, bool onlyNearLine)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var len = MathF.Sqrt(dx * dx + dz * dz);
        var n = Math.Clamp((int)MathF.Ceiling(len / 0.5f), 1, 128);
        var run = len / n;
        var maxRise = run * NavTanLimit;
        var prev = t.HeightAt(a.X, a.Z);
        var bad = 0;
        for (var k = 1; k <= n; k++)
        {
            var f = k / (float)n;
            float x = a.X + dx * f, z = a.Z + dz * f;
            var h = t.HeightAt(x, z);
            if (onlyNearLine && MathF.Abs(a.Y + (b.Y - a.Y) * f - h) >= 1.5f)
            {
                prev = h;
                bad = 0;
                continue;
            }
            var rise = MathF.Abs(h - prev);
            if (rise > maxRise || !NavWalkable(t, x, z))
            {
                if (rise > World.StepHeight || ++bad > 1)
                    return false;
            }
            else
                bad = 0;
            prev = h;
        }
        return true;
    }

    /// <summary>
    /// How much longer than flat ground a walk of <paramref name="run"/> metres across that climbs <paramref name="rise"/>
    /// metres takes, by <see cref="SpeedFactor"/> (1 downhill and on gentle slopes). For the bots' route costs.
    /// </summary>
    public static float UphillTimeFactor(float rise, float run)
    {
        if (rise <= 0 || run < 1e-3f)
            return 1f;
        var g = rise / run;
        var ny = 1f / MathF.Sqrt(1f + g * g);
        var f = SpeedFactor(new Vector3(0, ny, g * ny), new Vector3(0, 0, -1));
        return 1f / f;
    }
}
