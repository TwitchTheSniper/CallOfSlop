using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Drawing the co-op enemies (docs/pve/DESIGN.md): soldiers in their archetype's uniform with a helmet and their gun.
/// Level of detail keeps the cost flat with 80 alive: the full figure with the gun model and swinging legs for the
/// nearest (within 35 m, at most <see cref="PveFullDetail"/>), a 4-box figure out to 160 m, nothing behind the camera
/// or further. Dead ones tip over and sink. One line of stats in the log every 10 s (enemies drawn, boxes).
/// </summary>
public static unsafe partial class Program
{
    /// <summary>Enemies drawn with the full figure and gun at most (the nearest within 35 m).</summary>
    private const int PveFullDetail = 24;

    private static readonly Vector3 PveSkin = new(0.78f, 0.6f, 0.46f);
    private static double _pveStatsAt;
    private static int _pveDrawnFull, _pveDrawnFar;

    private static void DrawPveEnemies(double now, Vector3 eye, Vector3 fwd)
    {
        if (_game.Enemies.Count == 0)
            return;
        int full = 0, far = 0;
        foreach (var e in _game.Enemies)
        {
            var to = e.RenderPos + new Vector3(0, 1f, 0) - eye;
            var d2 = to.LengthSquared();
            if (d2 > 160f * 160f)
                continue;
            var d = MathF.Sqrt(d2);
            if (d > 4f && Vector3.Dot(to / d, fwd) < 0.35f)
                continue; // behind the camera (the view is narrower than this)
            if (d < 35f && full < PveFullDetail)
            {
                DrawEnemyFull(e, now);
                full++;
            }
            else
            {
                DrawEnemyFar(e, now);
                far++;
            }
        }
        _pveDrawnFull = full;
        _pveDrawnFar = far;
        if (now >= _pveStatsAt)
        {
            _pveStatsAt = now + 10;
            Console.WriteLine($"[coop] enemies: {_game.Enemies.Count} known, drawn {full} full + {far} far (~{full * 14 + far * 4} boxes + {full} guns)");
        }
    }

    /// <summary>Tipping over and sinking once dead; the body's transform at its feet.</summary>
    private static Matrix4x4 EnemyBody(PveEnemy e, double now, out bool gone)
    {
        gone = false;
        var pos = e.RenderPos;
        var body = Matrix4x4.CreateRotationY(-e.RenderYaw);
        if (!e.Alive)
        {
            var since = (float)(now - e.DiedAt);
            if (since > 2.2f)
                gone = true;
            body = Matrix4x4.CreateRotationX(Math.Clamp(since / 0.4f, 0, 1) * (e.KilledByHead ? -1.5f : 1.45f)) * body;
            pos.Y -= Math.Clamp(since - 1.1f, 0, 1) * 0.6f;
        }
        return body * Matrix4x4.CreateTranslation(pos);
    }

    private static void DrawEnemyFull(PveEnemy e, double now)
    {
        var at = EnemyBody(e, now, out var gone);
        if (gone)
            return;
        var def = e.Def;
        var crouch = e.Crouched && e.Alive;
        var hip = crouch ? 0.55f : 0.86f;
        var swing = e.Moving && e.Alive ? MathF.Sin(e.Gait) * 0.6f : 0f;
        var pants = def.Uniform * 0.8f;
        // Legs about the hips (bent forward when crouched).
        for (var side = -1; side <= 1; side += 2)
        {
            var a = crouch ? -1.1f : side * swing;
            Gfx.Draw(Matrix4x4.CreateScale(0.2f, crouch ? 0.6f : 0.86f, 0.22f) * Matrix4x4.CreateTranslation(0, crouch ? -0.3f : -0.43f, 0) * Matrix4x4.CreateRotationX(a)
                     * Matrix4x4.CreateTranslation(side * 0.13f, hip, 0) * at, pants);
            if (crouch)
                Gfx.Draw(Matrix4x4.CreateScale(0.19f, 0.5f, 0.2f) * Matrix4x4.CreateTranslation(side * 0.13f, 0.25f, -0.38f) * at, pants * 0.9f); // shins down to the ground
        }
        var upper = Matrix4x4.CreateRotationX(crouch ? -0.25f : -0.05f) * Matrix4x4.CreateTranslation(0, hip, 0) * at;
        Gfx.Draw(Matrix4x4.CreateScale(0.5f, 0.62f, 0.28f) * Matrix4x4.CreateTranslation(0, 0.31f, 0) * upper, def.Uniform); // torso
        Gfx.Draw(Matrix4x4.CreateScale(0.54f, 0.4f, 0.33f) * Matrix4x4.CreateTranslation(0, 0.36f, 0) * upper, def.Accent); // vest
        Gfx.Draw(Matrix4x4.CreateScale(0.24f, 0.26f, 0.25f) * Matrix4x4.CreateTranslation(0, 0.78f, 0) * upper, PveSkin); // head
        Gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.13f, 0.32f) * Matrix4x4.CreateTranslation(0, 0.93f, 0.01f) * upper, e.Arch == PveArch.Officer ? def.Accent : def.Accent * 0.75f); // helmet / beret
        if (e.Arch == PveArch.Heavy)
            Gfx.Draw(Matrix4x4.CreateScale(0.62f, 0.2f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.56f, 0) * upper, def.Accent * 0.8f); // shoulder armour
        if (e.Named && e.Alive)
            Gfx.Draw(Matrix4x4.CreateScale(0.12f) * Matrix4x4.CreateTranslation(0, 1.25f + 0.05f * MathF.Sin((float)now * 4), 0) * upper, new Vector3(1f, 0.25f, 0.2f), 1f, mode: 2); // the named target's marker
        if (!e.Alive)
            return;
        // The gun at the shoulder, pointing where it aims; arms from the shoulders to it.
        var gunPose = Matrix4x4.CreateScale(1.25f) * Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateTranslation(0.22f, 0.5f, -0.25f) * upper;
        GunModel.Draw(Gfx, def.Def, gunPose, false, (float)now);
        for (var side = -1; side <= 1; side += 2)
            Gfx.Draw(Matrix4x4.CreateScale(0.11f, 0.11f, 0.48f) * Matrix4x4.CreateTranslation(0, 0, -0.2f) * Matrix4x4.CreateRotationY(side * 0.35f) * Matrix4x4.CreateRotationX(e.RenderPitch * 0.8f)
                     * Matrix4x4.CreateTranslation(side * 0.24f, 0.52f, -0.05f) * upper, def.Uniform * 0.9f);
    }

    private static void DrawEnemyFar(PveEnemy e, double now)
    {
        var at = EnemyBody(e, now, out var gone);
        if (gone)
            return;
        var def = e.Def;
        var h = e.Crouched && e.Alive ? 0.7f : 1f;
        Gfx.Draw(Matrix4x4.CreateScale(0.42f, 0.86f * h, 0.24f) * Matrix4x4.CreateTranslation(0, 0.43f * h, 0) * at, def.Uniform * 0.8f);
        Gfx.Draw(Matrix4x4.CreateScale(0.54f, 0.62f * h, 0.32f) * Matrix4x4.CreateTranslation(0, (0.86f + 0.31f) * h, 0) * at, def.Accent);
        Gfx.Draw(Matrix4x4.CreateScale(0.28f, 0.3f, 0.28f) * Matrix4x4.CreateTranslation(0, 1.65f * h + 0.05f, 0) * at, def.Accent * 0.75f);
        if (e.Alive)
            Gfx.Draw(Matrix4x4.CreateScale(0.08f, 0.08f, 0.8f) * Matrix4x4.CreateTranslation(0.22f, 1.35f * h, -0.35f) * at, new Vector3(0.12f));
    }
}
