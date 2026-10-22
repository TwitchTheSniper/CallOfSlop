using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws launcher projectiles: a rocket (body, nose in the launcher's accent colour, fins), a grenade or a sticky charge,
/// each turned along its flight; then, in the blended pass, the motor's flame and the smoke trail (a dozen puffs from
/// the trail points, growing and fading with age). A handful of boxes per projectile and only a few are ever in the air,
/// so they use the per-box draws like the streak machines; the motor sparks go through the frenzy particle pool's one
/// instanced draw. Called from Program.Render.
/// </summary>
public static class LauncherRender
{
    private static readonly Vector3 Olive = new(0.3f, 0.33f, 0.22f);
    private static readonly Vector3 Dark = new(0.13f, 0.13f, 0.14f);
    private static readonly Vector3 Smoke = new(0.72f, 0.72f, 0.7f);

    /// <summary>Opaque pass: the projectiles themselves.</summary>
    public static void Draw(Renderer gfx, Game game, double now)
    {
        foreach (var r in game.Rockets.Values)
            DrawBody(gfx, r, now);
        foreach (var r in game.PredictedRockets)
            DrawBody(gfx, r, now);
    }

    /// <summary>Blended pass (depth write off): flames and smoke.</summary>
    public static void DrawGlow(Renderer gfx, Game game, double now)
    {
        foreach (var r in game.Rockets.Values)
            DrawTrail(gfx, r, now);
        foreach (var r in game.PredictedRockets)
            DrawTrail(gfx, r, now);
    }

    private static Matrix4x4 Along(Vector3 dir, Vector3 pos)
    {
        var yaw = MathF.Atan2(dir.X, -dir.Z);
        var pitch = MathF.Asin(Math.Clamp(dir.Y, -1f, 1f));
        return Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(-yaw) * Matrix4x4.CreateTranslation(pos);
    }

    private static void DrawBody(Renderer gfx, Rocket r, double now)
    {
        if (r.Hidden || r.Def == null)
            return;
        var d = r.Def;
        var dir = r.Vel.LengthSquared() > 1e-4f ? Vector3.Normalize(r.Vel) : -Vector3.UnitZ;
        switch (d.LauncherProjectile)
        {
            case LauncherProjectile.Rocket:
            {
                var m = Along(dir, r.RenderPos);
                var w = 0.05f + d.Caliber * 0.04f;
                gfx.Draw(Matrix4x4.CreateScale(w, w, 0.55f) * m, Olive);
                gfx.Draw(Matrix4x4.CreateScale(w * 1.5f, w * 1.5f, 0.16f) * Matrix4x4.CreateTranslation(0, 0, -0.3f) * m, d.Accent); // warhead
                gfx.Draw(Matrix4x4.CreateScale(w * 2.6f, 0.012f, 0.1f) * Matrix4x4.CreateTranslation(0, 0, 0.24f) * m, Dark); // fins
                gfx.Draw(Matrix4x4.CreateScale(0.012f, w * 2.6f, 0.1f) * Matrix4x4.CreateTranslation(0, 0, 0.24f) * m, Dark);
                break;
            }
            case LauncherProjectile.Grenade:
            {
                var spin = Matrix4x4.CreateRotationX((float)(now - r.Born) * 14f);
                gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.07f, 0.1f) * spin * Along(dir, r.RenderPos), Olive);
                gfx.Draw(Matrix4x4.CreateScale(0.075f, 0.075f, 0.03f) * Matrix4x4.CreateTranslation(0, 0, -0.05f) * spin * Along(dir, r.RenderPos), d.Accent);
                break;
            }
            default:
            {
                // A sticky charge: a squat puck with a blinking light (faster once it has stuck).
                var blink = ((int)((now - r.Born) * (r.Stuck ? 4 : 2)) & 1) == 0;
                gfx.Draw(Matrix4x4.CreateScale(0.11f, 0.06f, 0.11f) * Matrix4x4.CreateTranslation(r.RenderPos), d.Accent * 0.8f);
                gfx.Draw(Matrix4x4.CreateScale(0.03f) * Matrix4x4.CreateTranslation(r.RenderPos + new Vector3(0, 0.04f, 0)), blink ? new Vector3(1f, 0.2f, 0.15f) : Dark, 1f, mode: blink ? 2 : 0);
                break;
            }
        }
    }

    private static void DrawTrail(Renderer gfx, Rocket r, double now)
    {
        if (r.Def == null || r.Stuck)
            return;
        var rocket = r.Def.LauncherProjectile == LauncherProjectile.Rocket;
        if (!r.Hidden && rocket)
        {
            // The motor's flame, flickering.
            var dir = r.Vel.LengthSquared() > 1e-4f ? Vector3.Normalize(r.Vel) : -Vector3.UnitZ;
            var f = 0.8f + 0.2f * MathF.Sin((float)now * 90f + r.Id);
            gfx.Draw(Matrix4x4.CreateScale(0.09f * f, 0.09f * f, 0.35f * f) * Matrix4x4.CreateTranslation(0, 0, 0.45f) * Along(dir, r.RenderPos), new Vector3(1f, 0.65f, 0.25f), 0.9f, mode: 2);
        }
        // Smoke: older puffs are bigger and fainter (grenades leave only a thin wisp).
        var n = r.TrailCount;
        for (var i = 1; i < n; i += 1)
        {
            var t = i / (float)Rocket.TrailLength;
            var size = (rocket ? 0.12f + 0.35f * t : 0.05f + 0.08f * t);
            var a = (rocket ? 0.45f : 0.2f) * (1f - t);
            if (a <= 0.01f)
                continue;
            gfx.Draw(Matrix4x4.CreateScale(size) * Matrix4x4.CreateRotationY(i * 0.7f) * Matrix4x4.CreateTranslation(r.TrailAt(i) + new Vector3(0, 0.1f * t, 0)), Smoke, a);
        }
    }
}
