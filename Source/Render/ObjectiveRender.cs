using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the game-mode objectives in the world. The pieces (rings, halos, beams, a flag) are here; which to draw where is
/// the mode's call (KingOfTheHillModule: a glowing ring with a halo of light rising out of the ground in the holder's
/// colour, orange while contested, plus a light beam so it can be found from anywhere; CaptureTheFlagModule: flags with
/// their base rings and halos and a beam over a flag that's carried or dropped). Rings and halos follow the ground on
/// terrain maps. Called from Program.Render inside the blended, no-depth-write pass. A handful of unlit cubes per frame:
/// no measurable cost.
/// </summary>
public static class ObjectiveRender
{
    private const int RingSegments = 28;
    private const int HaloSegments = 24, HaloBands = 5;

    /// <summary>The active mode draws its objectives (<see cref="GameModeModule.DrawObjectives"/>) with the helpers below.</summary>
    public static void Draw(Renderer gfx, Game game, double now) => game.Module.DrawObjectives(gfx, game, now);

    /// <summary>
    /// The height a ring or halo segment stands on at (x, z): the objective's own height, or on a terrain map the ground
    /// there (so a ring on a slope follows the slope instead of sinking into it or floating over it).
    /// </summary>
    private static float BaseAt(Vector3 center, float x, float z) =>
        World.Map.HasTerrain ? World.Map.GroundHeight(x, z) : center.Y;

    /// <summary>A ring of thin glowing boxes lying on the ground (following it on terrain).</summary>
    internal static void Ring(Renderer gfx, Vector3 center, float radius, Vector3 color, float alpha, float height, float width = 0.14f)
    {
        using var _ = gfx.StandAt(center); // (a planet: on its curve)
        var seg = MathF.Tau * radius / RingSegments * 1.08f;
        for (var i = 0; i < RingSegments; i++)
        {
            var a = i * MathF.Tau / RingSegments;
            float x = center.X + MathF.Cos(a) * radius, z = center.Z + MathF.Sin(a) * radius;
            var pos = new Vector3(x, BaseAt(center, x, z) + 0.04f + height / 2, z);
            var model = Matrix4x4.CreateScale(width, height, seg) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(pos);
            gfx.Draw(model, color, alpha, mode: 2);
        }
    }

    /// <summary>
    /// A see-through wall of light rising out of the ground around the objective, fading out towards the top, so the
    /// zone reads as a cylinder you can spot over a rise or past a bump in the terrain. Each segment starts a little
    /// below the lower of the ground and the objective's own height, and reaches above the higher of the two, so
    /// small mismatches between the zone and the terrain never leave a gap. Stacked bands give the fade with the
    /// existing unlit cube (24 x 5 small draws per objective).
    /// </summary>
    internal static void Halo(Renderer gfx, Vector3 center, float radius, float height, Vector3 color, float alpha)
    {
        using var _ = gfx.StandAt(center); // (a planet: on its curve)
        var seg = MathF.Tau * radius / HaloSegments * 1.04f;
        for (var i = 0; i < HaloSegments; i++)
        {
            var a = (i + 0.5f) * MathF.Tau / HaloSegments;
            float x = center.X + MathF.Cos(a) * radius, z = center.Z + MathF.Sin(a) * radius;
            var ground = BaseAt(center, x, z);
            var lo = MathF.Min(ground, center.Y) - 0.35f;
            var hi = MathF.Max(ground, center.Y) + height;
            var band = (hi - lo) / HaloBands;
            var rot = Matrix4x4.CreateRotationY(-a);
            for (var k = 0; k < HaloBands; k++)
            {
                var fade = 1f - k / (float)HaloBands;
                var model = Matrix4x4.CreateScale(0.03f, band, seg) * rot
                            * Matrix4x4.CreateTranslation(new Vector3(x, lo + band * (k + 0.5f), z));
                gfx.Draw(model, color, alpha * fade * fade, mode: 2);
            }
        }
    }

    internal static void Beam(Renderer gfx, Vector3 foot, float width, float height, Vector3 color, float alpha)
    {
        using var _ = gfx.StandAt(foot); // (a planet: on its curve)
        gfx.Draw(Matrix4x4.CreateScale(width, height, width) * Matrix4x4.CreateTranslation(foot + new Vector3(0, height / 2, 0)), color, alpha, mode: 2);
    }

    /// <summary>A pole and a waving cloth.</summary>
    internal static void Flag(Renderer gfx, Vector3 foot, Vector3 color, double now)
    {
        using var _ = gfx.StandAt(foot); // (a planet: on its curve)
        gfx.Draw(Matrix4x4.CreateScale(0.06f, 2.2f, 0.06f) * Matrix4x4.CreateTranslation(foot + new Vector3(0, 1.1f, 0)), new Vector3(0.75f, 0.75f, 0.78f), 1f, mode: 2);
        var wave = MathF.Sin((float)now * 3f) * 0.15f;
        gfx.Draw(Matrix4x4.CreateScale(0.75f, 0.45f, 0.04f) * Matrix4x4.CreateRotationY(wave) * Matrix4x4.CreateTranslation(foot + new Vector3(0.4f, 1.9f, 0)), color, 1f, mode: 2);
    }
}
