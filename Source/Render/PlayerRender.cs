using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The immediate-mode drawing of another player's box body, gun and arms (moved here unchanged from Program.DrawPlayer).
/// The game now draws players through the scene graph (<see cref="PlayerRig"/>); this stays as the reference the
/// self-test compares the scene graph with, and as the fallback when the scene graph is switched off.
/// </summary>
public static class PlayerRender
{
    /// <summary>Draws <paramref name="p"/> at its render pose. <paramref name="time"/> is the game clock (Glfw.Time).</summary>
    public static void DrawImmediate(Renderer g, Player p, double time)
    {
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        var color = p.Color;

        if (!p.Alive)
        {
            g.Draw(Matrix4x4.CreateScale(0.8f, 0.25f, 1.8f) * Matrix4x4.CreateTranslation(0, 0.125f, 0) * rot * at, color * 0.4f);
            return;
        }

        var visorColor = new Vector3(0.05f, 0.05f, 0.08f);
        float barY;
        switch (p.RenderStance)
        {
            case Stance.Prone:
            {
                // Lying flat along their facing: body behind, head and gun out front.
                g.Draw(Matrix4x4.CreateScale(0.6f, 0.4f, 1.3f) * Matrix4x4.CreateTranslation(0, 0.2f, 0.15f) * rot * at, color);
                g.Draw(Matrix4x4.CreateScale(0.4f, 0.38f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.3f, -0.75f) * rot * at, color * 1.1f);
                g.Draw(Matrix4x4.CreateScale(0.34f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 0.36f, -0.96f) * rot * at, visorColor);
                var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.25f, 0.3f, -0.9f) * rot * at;
                DrawArmed(g, p, gunPose, rot * at, time);
                barY = 0.9f;
                break;
            }
            case Stance.Crouch:
            {
                const float body = 0.85f, top = 1.2f;
                g.Draw(Matrix4x4.CreateScale(0.8f, body, 0.6f) * Matrix4x4.CreateTranslation(0, body / 2, 0) * rot * at, color);
                g.Draw(Matrix4x4.CreateScale(0.45f, top - body, 0.45f) * Matrix4x4.CreateTranslation(0, (body + top) / 2, -0.05f) * rot * at, color * 1.1f);
                g.Draw(Matrix4x4.CreateScale(0.38f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 1.07f, -0.29f) * rot * at, visorColor);
                var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.3f, 0.82f, -0.3f) * rot * at;
                DrawArmed(g, p, gunPose, rot * at, time);
                barY = 1.45f;
                break;
            }
            default:
            {
                g.Draw(Matrix4x4.CreateScale(0.8f, World.HeadStart, 0.5f) * Matrix4x4.CreateTranslation(0, World.HeadStart / 2, 0) * rot * at, color);
                var headH = World.PlayerHeight - World.HeadStart;
                g.Draw(Matrix4x4.CreateScale(0.45f, headH, 0.45f) * Matrix4x4.CreateTranslation(0, World.HeadStart + headH / 2, 0) * rot * at, color * 1.1f);
                g.Draw(Matrix4x4.CreateScale(0.38f, 0.1f, 0.06f) * Matrix4x4.CreateTranslation(0, 1.63f, -0.24f) * rot * at, visorColor);
                var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.3f, 1.3f, -0.25f) * rot * at;
                DrawArmed(g, p, gunPose, rot * at, time);
                barY = 2.05f;
                break;
            }
        }

        var frac = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f);
        g.Draw(Matrix4x4.CreateScale(0.8f * frac, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, barY, 0) * rot * at,
            Vector3.Lerp(new Vector3(1, 0.1f, 0.1f), new Vector3(0.2f, 1, 0.2f), frac), 1f, mode: 2);
    }

    /// <summary>Another player's gun with arms holding it (Source/Render/ThirdPersonArms.cs).</summary>
    private static void DrawArmed(Renderer g, Player p, in Matrix4x4 gunPose, in Matrix4x4 body, double time)
    {
        var def = p.WeaponDef;
        GunModel.Draw(g, def, ThirdPersonArms.Animate(p, def, gunPose, body, time), false, (float)time);
        ThirdPersonArms.Draw(g);
    }
}
