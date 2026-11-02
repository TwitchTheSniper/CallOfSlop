using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Overhangs over a body on ground with holes, caves or dug craters (<see cref="ITerrainCollision.HasVoids"/>): how far a
/// body standing at a point would reach into the ground over it. Looked at over the body's footprint (the middle and eight
/// points near its radius), not just the middle, so a player walking up the ground under a low lip meets it as a wall
/// (crouch to go in) instead of getting a head wedged in it (Karl, 2026-11-02, two grenades in one crater).
/// </summary>
public static class TerrainHeadroom
{
    /// <summary>Footprint points: the middle, then eight around it at this share of the player's radius.</summary>
    private const float Reach = World.PlayerRadius * 0.9f;

    private const float Diag = Reach * 0.70710678f;

    private static ReadOnlySpan<float> Dx => [0f, Reach, -Reach, 0f, 0f, Diag, Diag, -Diag, -Diag];

    private static ReadOnlySpan<float> Dz => [0f, 0f, 0f, Reach, -Reach, Diag, -Diag, Diag, -Diag];

    /// <summary>
    /// How far the top of a body <paramref name="height"/> tall standing at <paramref name="feet"/> goes into the ground
    /// above it (negative: the room left; -infinity: open sky). The ground is looked for from just above the feet and from
    /// step height (a footprint point under a rising slope is inside the ground at the feet).
    /// </summary>
    public static float Over(ITerrainCollision terrain, Vector3 feet, float height)
    {
        var over = float.NegativeInfinity;
        var top = feet.Y + height;
        for (var k = 0; k < 9; k++)
        {
            float x = feet.X + Dx[k], z = feet.Z + Dz[k];
            var c = MathF.Min(terrain.CeilingAbove(x, z, feet.Y + 0.1f), terrain.CeilingAbove(x, z, feet.Y + World.StepHeight));
            over = MathF.Max(over, top - c);
        }
        return over;
    }
}
