using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The immediate-mode drawing of another player's box body (torso and head from <see cref="BodyModel"/>), gun and arms.
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
        var hat = HatRender.HatFor(p); // prestige hat (HatRender.cs)

        if (!p.Alive)
        {
            if (Ragdolls.OwnsBody(p.Id))
                return; // the body is a ragdoll (Source/Render/Ragdolls.cs), drawn with the debris
            g.Draw(Matrix4x4.CreateScale(0.8f, 0.25f, 1.8f) * Matrix4x4.CreateTranslation(0, 0.125f, 0) * rot * at, color * 0.4f);
            if (hat != null)
                HatRender.Draw(g, hat, HatRender.OnGround(hat) * rot * at);
            return;
        }

        var body = rot * at;
        var legs = LegAnimator.Animate(p, time);
        var up = legs.Upper * body; // torso, head, gun and arms ride on the hips (Source/Render/LegAnimator.cs)
        // Torso and head (Source/Render/BodyModel.cs), in the old torso and head boxes: prone lies flat along the facing,
        // body behind, head and gun out front.
        var stance = BodyModel.StanceIndex(p.RenderStance);
        var s = BodyModel.Stances[stance];
        BodyModel.DrawImmediate(g, p, stance, up, hat != null);
        if (hat != null)
            HatRender.Draw(g, hat, HatRender.OnHead(hat, s.HeadSize, p, time) * Matrix4x4.CreateTranslation(s.HeadAt) * up);
        DrawLegs(g, legs, body, color);
        var (gunAt, barY) = stance switch
        {
            2 => (new Vector3(0.25f, 0.3f, -0.9f), 0.9f),
            1 => (new Vector3(0.3f, 0.82f, -0.3f), 1.45f),
            _ => (new Vector3(0.3f, 1.3f, -0.25f), 2.05f),
        };
        var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(gunAt) * up;
        DrawArmed(g, p, gunPose, up, time);

        var frac = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f);
        g.Draw(Matrix4x4.CreateScale(0.8f * frac, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, barY, 0) * rot * at,
            Vector3.Lerp(new Vector3(1, 0.1f, 0.1f), new Vector3(0.2f, 1, 0.2f), frac), 1f, mode: 2);
    }

    /// <summary>The legs posed by <see cref="LegAnimator.Animate"/>: pelvis, then per leg thigh, knee pad, shin, boot, sole (the rig's order).</summary>
    public static void DrawLegs(Renderer g, LegPose legs, in Matrix4x4 body, Vector3 color)
    {
        var c = LegColors.For(color);
        var pelvis = legs.PelvisLocal * body;
        var pp = LegAnimator.PelvisPart;
        g.Draw(Matrix4x4.CreateScale(pp.Size) * Matrix4x4.CreateTranslation(pp.Center) * pelvis, c.Of(pp.Paint));
        var parts = LegAnimator.LegParts;
        for (var i = 0; i < 2; i++)
        {
            var thigh = legs.ThighLocal[i] * pelvis;
            var shin = legs.ShinLocal[i] * thigh;
            var foot = legs.FootLocal[i] * shin;
            foreach (var part in parts)
            {
                var bone = part.Bone switch { 1 => thigh, 2 => shin, _ => foot };
                g.Draw(Matrix4x4.CreateScale(part.Size) * Matrix4x4.CreateTranslation(part.Center) * bone, c.Of(part.Paint));
            }
        }
    }

    /// <summary>Another player's gun with arms holding it (Source/Render/ThirdPersonArms.cs).</summary>
    private static void DrawArmed(Renderer g, Player p, in Matrix4x4 gunPose, in Matrix4x4 body, double time)
    {
        var def = p.WeaponDef;
        GunModel.Draw(g, def, ThirdPersonArms.Animate(p, def, gunPose, body, time), false, (float)time);
        ThirdPersonArms.Draw(g);
    }
}
