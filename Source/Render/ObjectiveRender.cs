using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the game-mode objectives in the world: the King of the Hill zone (a glowing ring in the holder's colour,
/// orange while contested, plus a light beam so it can be found from anywhere), and Capture the Flag flags with their
/// base rings and a beam over a flag that's carried or dropped. Called from Program.Render inside the blended,
/// no-depth-write pass. A handful of unlit cubes per frame: no measurable cost.
/// </summary>
public static class ObjectiveRender
{
    private const int RingSegments = 28;

    public static void Draw(Renderer gfx, Game game, double now)
    {
        switch (game.Mode)
        {
            case GameMode.KingOfTheHill when game.ActiveHill is { } hill:
            {
                var color = game.HillContested
                    ? new Vector3(1f, 0.55f, 0.15f) * (0.75f + 0.25f * MathF.Sin((float)now * 10f))
                    : GameModes.TeamColor(game.HillOwner);
                Ring(gfx, hill.Center, hill.Radius, color, 0.85f, 0.12f);
                Ring(gfx, hill.Center, hill.Radius * 0.97f, color, 0.25f, 0.02f, 0.5f); // soft inner edge
                Beam(gfx, hill.Center, 0.35f, 40f, color, 0.18f);
                break;
            }
            case GameMode.CaptureTheFlag:
            {
                for (byte t = 0; t < 2; t++)
                {
                    var c = GameModes.TeamColors[t];
                    var home = game.Objectives.Flags[t];
                    Ring(gfx, home, GameModes.FlagTouchRadius, c, 0.6f, 0.08f);
                    var f = game.Flags[t];
                    var at = f.Position;
                    if (f.Carried && game.Players.TryGetValue(f.Carrier, out var carrier))
                    {
                        at = carrier.RenderPosition + new Vector3(0, 0.9f, 0); // on the carrier's back
                        if (carrier.Id == game.LocalId && carrier.Alive)
                            continue; // you know you've got it (the HUD says so)
                    }
                    Flag(gfx, at, c, now);
                    if (!f.Home)
                    {
                        // Seen through walls: carriers and dropped flags can't hide.
                        gfx.SetDepthTest(false);
                        Beam(gfx, at, 0.18f, 30f, c, f.Carried ? 0.35f : 0.22f);
                        gfx.SetDepthTest(true);
                    }
                }
                break;
            }
        }
    }

    /// <summary>A ring of thin glowing boxes lying on the ground.</summary>
    private static void Ring(Renderer gfx, Vector3 center, float radius, Vector3 color, float alpha, float height, float width = 0.14f)
    {
        var seg = MathF.Tau * radius / RingSegments * 1.08f;
        for (var i = 0; i < RingSegments; i++)
        {
            var a = i * MathF.Tau / RingSegments;
            var pos = center + new Vector3(MathF.Cos(a) * radius, 0.04f + height / 2, MathF.Sin(a) * radius);
            var model = Matrix4x4.CreateScale(width, height, seg) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(pos);
            gfx.Draw(model, color, alpha, mode: 2);
        }
    }

    private static void Beam(Renderer gfx, Vector3 foot, float width, float height, Vector3 color, float alpha) =>
        gfx.Draw(Matrix4x4.CreateScale(width, height, width) * Matrix4x4.CreateTranslation(foot + new Vector3(0, height / 2, 0)), color, alpha, mode: 2);

    /// <summary>A pole and a waving cloth.</summary>
    private static void Flag(Renderer gfx, Vector3 foot, Vector3 color, double now)
    {
        gfx.Draw(Matrix4x4.CreateScale(0.06f, 2.2f, 0.06f) * Matrix4x4.CreateTranslation(foot + new Vector3(0, 1.1f, 0)), new Vector3(0.75f, 0.75f, 0.78f), 1f, mode: 2);
        var wave = MathF.Sin((float)now * 3f) * 0.15f;
        gfx.Draw(Matrix4x4.CreateScale(0.75f, 0.45f, 0.04f) * Matrix4x4.CreateRotationY(wave) * Matrix4x4.CreateTranslation(foot + new Vector3(0.4f, 1.9f, 0)), color, 1f, mode: 2);
    }
}
