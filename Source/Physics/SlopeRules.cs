using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// MEASUREMENT SHIM (baseline commit only): the old terrain rule (the single heightfield patch, 42 degrees) under the names the
/// "Slopes" self-test uses, so the same harness can record the movement and bots before the slope rules land.
/// </summary>
public static class SlopeRules
{
    public const float WalkLimitDeg = 42f;
    public const float FullSpeedDeg = 25f;
    public const float SlowAtLimit = 0.6f;
    public const float MaxSlideSpeed = 8f;

    public static bool Walkable(ITerrainCollision t, float x, float z) => t.NormalAt(x, z).Y >= World.MinWalkNormalY;
}
