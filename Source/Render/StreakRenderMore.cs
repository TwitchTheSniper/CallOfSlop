using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the later streaks' world objects: Hunter Drones, Gunships, Supply Drop crates, napalm fire and the Juggernaut
/// suit's armour plates. Box-built like the rest (a handful of draws each, only while one is out).
/// </summary>
public static partial class StreakRender
{
    private static readonly Vector3 Olive = new(0.3f, 0.34f, 0.24f);
    private static readonly Vector3 CrateWood = new(0.55f, 0.42f, 0.24f);

    internal static void DrawMoreEntity(Renderer gfx, StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        switch (e.Kind)
        {
            case StreakId.HunterDrone:
                DrawHunter(gfx, e, stripe, friendly, now);
                break;
            case StreakId.Gunship:
                DrawGunship(gfx, e, stripe, friendly, now);
                break;
            case StreakId.SupplyDrop:
                DrawCrate(gfx, e, stripe, now);
                break;
        }
    }

    /// <summary>A small dart: body, four fins, a light that blinks faster as it dives (red for an enemy's).</summary>
    private static void DrawHunter(Renderer gfx, StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        var body = Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(e.RenderPos);
        gfx.Draw(Matrix4x4.CreateScale(0.22f, 0.22f, 0.7f) * body, DarkMetal, 1f, material: Material.Metal);
        gfx.Draw(Matrix4x4.CreateScale(0.24f, 0.08f, 0.3f) * Matrix4x4.CreateTranslation(0, 0.08f, 0.05f) * body, stripe, 1f);
        gfx.Draw(Matrix4x4.CreateScale(0.8f, 0.03f, 0.18f) * Matrix4x4.CreateTranslation(0, 0, 0.25f) * body, Gunmetal, 1f);
        gfx.Draw(Matrix4x4.CreateScale(0.03f, 0.6f, 0.18f) * Matrix4x4.CreateTranslation(0, 0, 0.25f) * body, Gunmetal, 1f);
        var rate = e.Firing ? 12 : 3;
        var on = ((int)(now * rate) & 1) == 0;
        var light = friendly ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.15f, 0.1f);
        gfx.Draw(Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0, 0, -0.38f) * body, light * (on ? 1.8f : 0.4f), 1f, mode: 2);
        HealthBar(gfx, e, e.RenderPos + new Vector3(0, 0.5f, 0), friendly);
    }

    /// <summary>A box-built helicopter gunship: hull, cockpit, tail boom, spinning rotors and a cannon pod under the nose.</summary>
    private static void DrawGunship(Renderer gfx, StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        var body = Matrix4x4.CreateRotationZ(0.12f * MathF.Sign(e.OrbitDir == 0 ? 1 : e.OrbitDir)) * Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(e.RenderPos);
        gfx.Draw(Matrix4x4.CreateScale(1.6f, 1.4f, 3.4f) * body, Olive, 1f, material: Material.Metal);
        gfx.Draw(Matrix4x4.CreateScale(1.62f, 0.25f, 2f) * Matrix4x4.CreateTranslation(0, 0.3f, 0.2f) * body, stripe, 1f);
        gfx.Draw(Matrix4x4.CreateScale(1.2f, 0.9f, 1.2f) * Matrix4x4.CreateTranslation(0, 0.1f, -2f) * body, new Vector3(0.15f, 0.2f, 0.28f), 1f); // cockpit glass
        gfx.Draw(Matrix4x4.CreateScale(0.45f, 0.45f, 4.2f) * Matrix4x4.CreateTranslation(0, 0.3f, 3.6f) * body, Olive * 0.9f, 1f); // tail boom
        gfx.Draw(Matrix4x4.CreateScale(0.12f, 1.3f, 0.8f) * Matrix4x4.CreateTranslation(0, 0.9f, 5.5f) * body, Olive * 0.85f, 1f); // fin
        gfx.Draw(Matrix4x4.CreateScale(2.6f, 0.25f, 0.5f) * Matrix4x4.CreateTranslation(0, -0.2f, 0) * body, DarkMetal, 1f); // stub wings
        gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.5f, 0.3f) * Matrix4x4.CreateTranslation(0, 0.95f, 0) * body, DarkMetal, 1f); // mast
        var spin = (float)now * 22f;
        for (var i = 0; i < 2; i++)
            gfx.Draw(Matrix4x4.CreateScale(9f, 0.05f, 0.35f) * Matrix4x4.CreateRotationY(spin + i * MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 1.2f, 0) * body,
                new Vector3(0.18f, 0.18f, 0.2f), 1f);
        gfx.Draw(Matrix4x4.CreateScale(0.05f, 1.6f, 0.2f) * Matrix4x4.CreateRotationX(spin * 1.5f) * Matrix4x4.CreateTranslation(0.3f, 0.9f, 5.5f) * body, new Vector3(0.18f, 0.18f, 0.2f), 1f);
        // Cannon pod, pitched at its target
        var pod = Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(e.RenderPos - new Vector3(0, 1.1f, 0));
        gfx.Draw(Matrix4x4.CreateScale(0.4f, 0.4f, 0.5f) * pod, DarkMetal, 1f, material: Material.Metal);
        gfx.Draw(Matrix4x4.CreateScale(0.12f, 0.12f, 1.2f) * Matrix4x4.CreateTranslation(0, 0, -0.7f) * pod, DarkMetal, 1f);
        var light = friendly ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.15f, 0.1f);
        var blink = ((int)(now * 2) & 1) == 0;
        gfx.Draw(Matrix4x4.CreateScale(0.2f) * Matrix4x4.CreateTranslation(0, -0.75f, 1.2f) * body, light * (blink ? 1.8f : 0.5f), 1f, mode: 2);
        if (e.Firing && ((int)(now * 30) & 1) == 0)
            gfx.Draw(Matrix4x4.CreateScale(0.5f) * Matrix4x4.CreateTranslation(0, 0, -1.4f) * pod, new Vector3(1f, 0.85f, 0.4f), 1f, mode: 2);
        HealthBar(gfx, e, e.RenderPos + new Vector3(0, 2.2f, 0), friendly);
    }

    /// <summary>A wooden crate with the caller's colour band, under a parachute while it falls, with a beacon once it's down.</summary>
    private static void DrawCrate(Renderer gfx, StreakEntity e, Vector3 stripe, double now)
    {
        var m = Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(e.RenderPos + new Vector3(0, 0.6f, 0));
        gfx.Draw(Matrix4x4.CreateScale(1.2f) * m, CrateWood, 1f, material: Material.Wood);
        gfx.Draw(Matrix4x4.CreateScale(1.22f, 0.2f, 1.22f) * m, stripe, 1f);
        gfx.Draw(Matrix4x4.CreateScale(1.24f, 1.24f, 0.12f) * m, CrateWood * 0.7f, 1f);
        if (e.Firing)
        {
            // Parachute canopy and lines
            gfx.Draw(Matrix4x4.CreateScale(4f, 0.4f, 4f) * Matrix4x4.CreateTranslation(0, 4.5f, 0) * m, new Vector3(0.85f, 0.85f, 0.8f), 1f);
            for (var i = 0; i < 4; i++)
            {
                var a = MathF.PI / 4 + i * MathF.PI / 2;
                gfx.Draw(Matrix4x4.CreateScale(0.03f, 4f, 0.03f) * Matrix4x4.CreateRotationZ(0.35f) * Matrix4x4.CreateRotationY(a)
                         * Matrix4x4.CreateTranslation(MathF.Cos(a) * 0.9f, 2.5f, MathF.Sin(a) * 0.9f) * m, new Vector3(0.6f, 0.6f, 0.6f), 1f);
            }
        }
        else
        {
            var pulse = 0.5f + 0.5f * MathF.Sin((float)now * 5f);
            gfx.Draw(Matrix4x4.CreateScale(0.12f, 3f, 0.12f) * Matrix4x4.CreateTranslation(e.RenderPos + new Vector3(0, 2.7f, 0)),
                new Vector3(1f, 0.85f, 0.3f) * (1f + pulse), 1f, mode: 2);
        }
    }

    /// <summary>Napalm: a few flickering flame boxes per patch, fading in as it lands and out as it burns down. Called with blending on.</summary>
    private static void DrawFireZones(Renderer gfx, Game game, double now)
    {
        foreach (var z in game.FireZones)
        {
            if (now < z.Start)
                continue;
            var age = (float)(now - z.Start);
            var fade = Math.Clamp((float)(z.Until - now) / 1.5f, 0f, 1f) * Math.Clamp(age / 0.3f, 0f, 1f);
            var seed = (int)(z.Pos.X * 13 + z.Pos.Z * 7);
            for (var i = 0; i < 6; i++)
            {
                var a = seed + i * 2.1f;
                var r = KillStreaks.FireRadius * (0.25f + 0.5f * ((i * 37 + seed) & 7) / 7f);
                var flick = 0.6f + 0.4f * MathF.Sin((float)now * (9f + i) + i);
                var h = (0.8f + 0.9f * flick) * fade;
                var p = z.Pos + new Vector3(MathF.Cos(a) * r, h * 0.5f, MathF.Sin(a) * r);
                gfx.Draw(Matrix4x4.CreateScale(0.9f, h, 0.9f) * Matrix4x4.CreateRotationY(a) * Matrix4x4.CreateTranslation(p), new Vector3(1f, 0.45f + 0.25f * flick, 0.1f), 0.45f * fade, mode: 2);
            }
            gfx.Draw(Matrix4x4.CreateScale(KillStreaks.FireRadius * 1.8f, 0.05f, KillStreaks.FireRadius * 1.8f) * Matrix4x4.CreateTranslation(z.Pos + new Vector3(0, 0.05f, 0)),
                new Vector3(1f, 0.35f, 0.05f), 0.3f * fade, mode: 2);
        }
    }

    /// <summary>The Juggernaut suit: heavy plates over the chest and shoulders of anyone wearing it (yourself excluded: first person).</summary>
    private static void DrawJuggernauts(Renderer gfx, Game game, double now)
    {
        if (game.StreakEffects.Count == 0)
            return;
        foreach (var fx in game.StreakEffects)
        {
            if (fx.Kind != StreakId.Juggernaut || fx.Owner == game.LocalId || now >= fx.Until || !game.Players.TryGetValue(fx.Owner, out var p) || !p.Alive)
                continue;
            var hb = World.Hitbox(p.RenderPosition, p.RenderStance, p.RenderYaw);
            var rot = Matrix4x4.CreateRotationY(-p.RenderYaw);
            var chest = hb.Center + new Vector3(0, hb.Half.Y * 0.35f, 0);
            var plate = new Vector3(0.2f, 0.21f, 0.22f);
            gfx.Draw(Matrix4x4.CreateScale(hb.Half.X * 2.3f, hb.Half.Y * 0.8f, hb.Half.Z * 2.3f) * rot * Matrix4x4.CreateTranslation(chest), plate, 1f, material: Material.Metal);
            for (var s = -1; s <= 1; s += 2)
                gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.18f, 0.4f) * Matrix4x4.CreateTranslation(s * hb.Half.X * 1.2f, hb.Half.Y * 0.45f, 0) * rot * Matrix4x4.CreateTranslation(chest),
                    plate * 1.2f, 1f, material: Material.Metal);
            gfx.Draw(Matrix4x4.CreateScale(hb.Half.X * 2.35f, 0.08f, hb.Half.Z * 2.35f) * rot * Matrix4x4.CreateTranslation(chest), p.Color, 1f);
        }
    }
}
