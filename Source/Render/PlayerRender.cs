using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The immediate-mode drawing of another player's box body (torso and head from <see cref="BodyModel"/>), gun and arms.
/// The game now draws players through the scene graph (<see cref="PlayerRig"/>); this stays as the reference the
/// self-test compares the scene graph with, and as the fallback when the scene graph is switched off.
/// </summary>
public static class PlayerRender
{
    /// <summary>
    /// A player's body frame (feet at the origin, -Z = facing) in the world: turned by the render yaw (on a planet, standing
    /// on its curve), or seated (Sandbox) in the seat's frame, tilting and turning with the construct.
    /// </summary>
    public static Matrix4x4 BodyFrame(Player p)
    {
        if (p.RenderSeat != SeatKind.None)
            return Matrix4x4.CreateFromQuaternion(p.RenderSeatRot) * Matrix4x4.CreateTranslation(p.RenderPosition);
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        var at = Matrix4x4.CreateTranslation(p.RenderPosition);
        return GravityFrames.Curved ? rot * GravityFrames.Basis(p.RenderPosition) * at : rot * at; // a planet: standing on its curve
    }

    /// <summary>Draws <paramref name="p"/> at its render pose. <paramref name="time"/> is the game clock (Glfw.Time).</summary>
    public static void DrawImmediate(Renderer g, Player p, double time)
    {
        var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
        if (GravityFrames.Curved)
            rot *= GravityFrames.Basis(p.RenderPosition); // a planet: standing on its curve
        if (p.RenderSeat != SeatKind.None && p.Alive)
            rot = Matrix4x4.CreateFromQuaternion(p.RenderSeatRot); // seated: in the seat's frame
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
        BodyBloodRender.PlacePlayer(p, legs, body, stance); // blood marks by health (Source/Render/BodyBloodRender.cs)
        BodyBloodRender.Bind(g);
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
        var gunPose = (p.RenderSeat != SeatKind.None ? ThirdPersonArms.SeatedGun(p) : Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(gunAt)) * up;
        if (p.RenderSeat == SeatKind.Driver)
        {
            // Both hands on the steering wheel, no gun (Sandbox).
            ThirdPersonArms.Animate(p, p.WeaponDef, gunPose, up, time);
            ThirdPersonArms.WheelHands(p, up);
            ThirdPersonArms.Draw(g);
        }
        else
            DrawArmed(g, p, gunPose, up, time);

        var frac = Math.Clamp(p.Health / (float)World.MaxHealth, 0f, 1f);
        g.Draw(Matrix4x4.CreateScale(0.8f * frac, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(0, barY, 0) * rot * at,
            Vector3.Lerp(new Vector3(1, 0.1f, 0.1f), new Vector3(0.2f, 1, 0.2f), frac), 1f, mode: 2);
        BodyBloodRender.Unbind(g);
    }

    /// <summary>
    /// The APPEARANCE screen's model (Program.Appearance.cs): <paramref name="p"/> standing at the origin turned by its
    /// render yaw, drawn exactly like another player (the same torso, head, headwear, legs, hat, gun and arms) but with every
    /// detail part, no blood, no health bar and no foot IK against the world behind the menu.
    /// </summary>
    public static void DrawPreview(Renderer g, Player p, double time)
    {
        var viewer = LegAnimator.Viewer;
        LegAnimator.Viewer = new Vector3(1e7f); // far away: no foot rays into whatever map is behind the menu
        try
        {
            var body = Matrix4x4.CreateRotationY(-p.RenderYaw) * Matrix4x4.CreateTranslation(p.RenderPosition);
            var legs = LegAnimator.Animate(p, time);
            var up = legs.Upper * body;
            var s = BodyModel.Stances[0];
            var hat = HatRender.HatFor(p);
            BodyBloodRender.Reset();
            BodyModel.DrawImmediate(g, p, 0, up, hat != null, detail: true);
            if (hat != null)
                HatRender.Draw(g, hat, HatRender.OnHead(hat, s.HeadSize, p, time) * Matrix4x4.CreateTranslation(s.HeadAt) * up);
            DrawLegs(g, legs, body, p.Color);
            var gunPose = Matrix4x4.CreateScale(1.3f) * Matrix4x4.CreateRotationX(p.RenderPitch) * Matrix4x4.CreateTranslation(0.3f, 1.3f, -0.25f) * up;
            DrawArmed(g, p, gunPose, up, time);
        }
        finally
        {
            LegAnimator.Viewer = viewer;
        }
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
        var shot = ThirdPersonArms.ShotFlash; // the muzzles of their last shot flash (Source/Game/GameMuzzle.cs)
        GunModel.FlashBarrels = shot != 0 ? shot : GunModel.AllBarrels;
        GunModel.Draw(g, def, ThirdPersonArms.Animate(p, def, gunPose, body, time), shot != 0, (float)time);
        GunModel.FlashBarrels = GunModel.AllBarrels;
        ThirdPersonArms.Draw(g);
    }
}
