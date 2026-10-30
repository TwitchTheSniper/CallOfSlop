using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Prestige hats on players' heads (models from Cosmetics/hats.json, Source/Cosmetics/Cosmetics.cs). A hat is a few
/// boxes parented to the head, so it follows the body lean, the stance and the legs' hip bob like the head does, and is
/// never drawn in your own first-person view (only your legs are). Cosmetic only: no collision, hitboxes come from
/// <see cref="World"/> and never look at hats. A dead player's hat lies on the ground by the corpse.
/// The ridiculous top hat sways with movement: a pure function of the drawn velocity and the clock (no per-player state),
/// so the scene graph and the immediate fallback draw the same thing.
/// </summary>
public static class HatRender
{
    /// <summary>The head width the hat models are made for (the standing head box).</summary>
    public const float ModelHeadWidth = 0.45f;

    /// <summary>The hat to draw on <paramref name="p"/>: bots never wear one; people only a hat their prestige allows.</summary>
    public static HatDef? HatFor(Player p)
    {
        if (p.Bot != null || p.Hat == HatId.None || !Cosmetics.IsUnlocked(p.Hat, p.Prestige))
            return null;
        return Cosmetics.Get(p.Hat);
    }

    /// <summary>
    /// The hat's local matrix in the head node's space (origin at the head box's centre): scaled to the head, sitting on
    /// its top, swaying if the hat wobbles.
    /// </summary>
    public static Matrix4x4 OnHead(HatDef def, Vector3 headSize, Player p, double time)
    {
        var s = headSize.X / ModelHeadWidth;
        return Matrix4x4.CreateScale(s) * Sway(def, p, time) * Matrix4x4.CreateTranslation(0, headSize.Y / 2, 0);
    }

    /// <summary>A lean against the direction of travel plus a little wobble that grows with speed (identity for rigid hats).</summary>
    public static Matrix4x4 Sway(HatDef def, Player p, double time)
    {
        if (def.Wobble <= 0)
            return Matrix4x4.Identity;
        // the drawn velocity in the body's frame (-Z = facing)
        var (sin, cos) = MathF.SinCos(p.RenderYaw);
        var v = p.RenderVelocity;
        var forward = v.X * sin - v.Z * cos; // speed along the facing
        var right = v.X * cos + v.Z * sin;
        var speed = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
        var t = (float)(time % 1000.0);
        var wobble = def.Wobble * (0.03f + 0.012f * MathF.Min(speed, 8f)) * MathF.Sin(t * 5.3f + p.Id * 1.7f);
        var pitch = Math.Clamp(forward * 0.03f * def.Wobble, -0.3f, 0.3f) + wobble; // +X turns up towards +Z: moving forward tips it back
        var roll = Math.Clamp(right * 0.03f * def.Wobble, -0.3f, 0.3f) + 0.6f * def.Wobble * (0.02f + 0.01f * MathF.Min(speed, 8f)) * MathF.Sin(t * 4.1f + p.Id);
        return Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationZ(roll);
    }

    /// <summary>Where a dead player's hat lies (in the player's space): tipped over on its side past the head end of the corpse.</summary>
    public static Matrix4x4 OnGround(HatDef def)
    {
        var rest = MathF.Max(-def.Min.X, def.Max.X); // after the quarter turn about Z, the model's X extent is its height
        return Matrix4x4.CreateRotationZ(-MathF.PI / 2) * Matrix4x4.CreateRotationY(0.5f) * Matrix4x4.CreateTranslation(0.35f, rest, -1.15f);
    }

    /// <summary>Immediate mode: draws the hat's boxes under <paramref name="frame"/> (hat space to world).</summary>
    public static void Draw(Renderer g, HatDef def, in Matrix4x4 frame)
    {
        foreach (var b in def.Boxes)
            g.Draw(b.Local * frame, b.Color);
    }

    /// <summary>Scene graph: replaces <paramref name="node"/>'s children with the hat's boxes (allocates; call when the hat changes).</summary>
    public static void Build(SceneNode node, HatDef? def)
    {
        node.ClearChildren();
        if (def == null)
            return;
        foreach (var b in def.Boxes)
            node.Add("hat_box", b.Local, Renderable.Box(b.Color));
    }
}
