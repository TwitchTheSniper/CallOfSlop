using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the map's movers (see <see cref="MoverDef"/>) at their pose for this frame. Movers are few (a map may have
/// at most 32, with up to 16 boxes each), so each box is one draw with the shared box shader; static boxes,
/// terrain and foliage keep their own batched paths. Lit without the world-space texture, which would swim over
/// a moving surface; give movers stripes of colour (several boxes) so their motion reads.
/// </summary>
public static class MoverRender
{
    /// <summary>Boxes drawn last frame (for the smoke log).</summary>
    public static int LastBoxes { get; private set; }

    /// <summary>Draws every mover of <paramref name="map"/> at the (fractional) server tick <paramref name="tick"/>.</summary>
    public static void Draw(Renderer gfx, Map map, double tick)
    {
        if (ScenePresentation.Enabled) // mover frame -> part boxes (Source/Scene/Rigs/MoverRig.cs)
            LastBoxes = ScenePresentation.DrawMovers(gfx, map, tick);
        else
            DrawImmediate(gfx, map, tick);
    }

    /// <summary>The old immediate drawing (the scene graph's reference in the self-test, and its fallback).</summary>
    public static void DrawImmediate(Renderer gfx, Map map, double tick)
    {
        var n = 0;
        foreach (var m in map.Movers)
        {
            var pose = m.PoseAt(tick);
            foreach (var p in m.Parts)
            {
                var model = Matrix4x4.CreateScale(p.Half * 2)
                            * Matrix4x4.CreateRotationY(-(pose.Yaw + p.Yaw)) // our yaw turns +X towards +Z; CreateRotationY turns the other way
                            * Matrix4x4.CreateTranslation(pose.ToWorld(p.Center));
                gfx.Draw(model, p.Color, 1f, mode: 0);
                n++;
            }
        }
        LastBoxes = n;
    }
}
