using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// STAND-IN (placeholder, Source/StandIns/README.md): draws the stand-in craft at this frame's server tick, a box at a
/// time (a few craft of a dozen boxes each), in the opaque pass beside the map's movers.
/// </summary>
public static class StandInRender
{
    public static void Draw(Renderer gfx, Game? game)
    {
        if (game == null || game.StandInCount == 0 || !game.StandInsOf(World.Map))
            return;
        var tick = game.MoverRenderTick;
        var seconds = (float)(tick / World.TickRate);
        for (var i = 0; i < game.StandInCount; i++)
        {
            ref var c = ref game.StandIns[i];
            if (!StandInPath.Pose(ref c, tick, out var frame, out var thrust, out var flash))
                continue;
            if (flash > 0)
            {
                // The jump: the craft shrinks into a white flash that swells and is gone.
                var size = 2f + 30f * flash;
                gfx.Draw(Matrix4x4.CreateScale(size, size, size * 3f) * frame, new Vector3(1f, 0.95f, 0.85f), 1f, mode: 2);
                if (flash > 0.4f)
                    continue;
                frame = Matrix4x4.CreateScale(1f - flash * 2f) * frame;
            }
            foreach (var box in StandInShapes.Of(c.Kind))
            {
                var model = Matrix4x4.CreateScale(box.Half * 2);
                if (box.Spin)
                    model *= Matrix4x4.CreateRotationY(seconds * 25f * (0.3f + thrust));
                model *= Matrix4x4.CreateTranslation(box.Centre) * frame;
                if (box.Glow)
                    gfx.Draw(model, box.Colour * (0.35f + 0.65f * thrust), 1f, mode: 2);
                else
                    gfx.Draw(model, box.Colour, 1f, mode: 0);
            }
        }
    }
}
