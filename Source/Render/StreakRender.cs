using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Draws the kill streak world objects: sentry turrets and attack drones (box-built, a light on each shows friend or foe),
/// the red marker of a called-in strike, incoming mortar shells, the strafe-run jet, and the local player's targeting
/// preview. A few dozen boxes at most, so it uses the same per-box draws as everything else. Called once per frame from
/// Program.Render after the opaque world.
/// </summary>
public static partial class StreakRender
{
    private static readonly Vector3 Gunmetal = new(0.22f, 0.24f, 0.26f);
    private static readonly Vector3 DarkMetal = new(0.12f, 0.13f, 0.14f);
    private static readonly Vector3 Danger = new(1f, 0.18f, 0.12f);
    private static readonly Vector3 Friendly = new(0.2f, 0.8f, 1f);

    /// <summary>
    /// Is a strike called by <paramref name="owner"/> friendly to the local viewer (their own, or a teammate's in a team
    /// mode)? Hostile, unknown, or no local player: false, so the marker stays the red danger colour.
    /// </summary>
    public static bool StrikeIsFriendly(Game game, byte owner) =>
        game.Local is { } me && (owner == me.Id || (game.Players.TryGetValue(owner, out var o) && !game.StreakIsEnemy(o, me)));

    public static void Draw(Renderer gfx, Game game, double now)
    {
        var local = game.Local;
        foreach (var e in game.StreakEntities.Values)
        {
            var owner = game.Players.GetValueOrDefault(e.Owner);
            var friendly = local != null && owner != null && !game.StreakIsEnemy(owner, local);
            var stripe = owner?.Color ?? new Vector3(0.6f, 0.6f, 0.6f);
            if (e.Kind is not (StreakId.SentryTurret or StreakId.AttackDrone))
                DrawMoreEntity(gfx, e, stripe, friendly, now); // Hunter Drone, Gunship, crate (StreakRenderMore.cs)
            else if (ScenePresentation.Enabled) // turret base -> head -> barrels, drone body -> rotors/pod (Source/Scene/Rigs/StreakRigs.cs)
                ScenePresentation.DrawStreakEntity(gfx, e, stripe, friendly, now);
            else if (e.Kind == StreakId.SentryTurret)
                DrawTurret(gfx, e, stripe, friendly, now);
            else
                DrawDrone(gfx, e, stripe, friendly, now);
        }

        DrawJuggernauts(gfx, game, now); // armour plates on anyone in the suit (StreakRenderMore.cs)
        var anyGlow = game.StrikeMarkers.Count > 0 || game.Strikes.Count > 0 || game.StreakTargeting != StreakId.None || game.FireZones.Count > 0;
        if (!anyGlow)
            return;

        // Jets are solid; the rest glows.
        foreach (var m in game.StrikeMarkers)
            if (m.Kind is StreakId.StrafeRun or StreakId.NapalmStrike)
                DrawJet(gfx, m, now);

        gfx.SetBlend(true);
        gfx.SetDepthWrite(false);
        DrawFireZones(gfx, game, now);
        foreach (var m in game.StrikeMarkers)
        {
            var pulse = 0.5f + 0.5f * MathF.Sin((float)(now - m.Born) * 9f);
            // A flare column where it was called in, seen from anywhere: red for a hostile strike, cyan for your own
            // or your team's (decided from the viewer's team, see StrikeIsFriendly).
            var friendly = StrikeIsFriendly(game, m.Owner);
            var c = friendly ? Friendly : Danger;
            gfx.Draw(Matrix4x4.CreateScale(0.18f, 5f, 0.18f) * Matrix4x4.CreateTranslation(m.Pos + new Vector3(0, 2.5f, 0)), c, 0.35f + 0.3f * pulse, mode: 2);
            gfx.Draw(Matrix4x4.CreateScale(0.4f) * Matrix4x4.CreateTranslation(m.Pos + new Vector3(0, 0.2f, 0)), friendly ? new Vector3(0.45f, 0.9f, 1f) : new Vector3(1f, 0.4f, 0.3f), 0.8f, mode: 2);
            if (m.Kind == StreakId.MortarBarrage)
                Ring(gfx, m.Pos, KillStreaks.MortarSpread, c, 0.25f + 0.25f * pulse);
            else
                LineMarks(gfx, m.Kind, m.Pos, m.Yaw, c, 0.2f + 0.25f * pulse, exact: true);
        }
        foreach (var s in game.Strikes)
        {
            if (s.Kind != StreakId.MortarBarrage)
                continue;
            var t = (float)(s.At - now);
            if (t is < 0 or > 0.9f)
                continue;
            // The shell dropping in, with a short bright trail.
            var p = s.Pos + new Vector3(0, t * 45f, 0);
            gfx.Draw(Matrix4x4.CreateScale(0.14f, 0.45f, 0.14f) * Matrix4x4.CreateTranslation(p), DarkMetal, 1f);
            gfx.Draw(Matrix4x4.CreateScale(0.08f, 2.5f, 0.08f) * Matrix4x4.CreateTranslation(p + new Vector3(0, 1.5f, 0)), new Vector3(1f, 0.75f, 0.4f), 0.45f, mode: 2);
        }
        if (game.StreakTargeting != StreakId.None && game.StreakAimValid && local is { Alive: true })
        {
            var c = new Vector3(1f, 0.55f, 0.15f);
            var pulse = 0.5f + 0.5f * MathF.Sin((float)now * 7f);
            if (KillStreaks.Get(game.StreakTargeting).Aim == StreakAim.Line)
                LineMarks(gfx, game.StreakTargeting, game.StreakAimPoint, game.LookYaw, c, 0.35f + 0.25f * pulse, exact: false);
            else
                Ring(gfx, game.StreakAimPoint, KillStreaks.MortarSpread, c, 0.35f + 0.25f * pulse);
            gfx.Draw(Matrix4x4.CreateScale(0.12f, 2f, 0.12f) * Matrix4x4.CreateTranslation(game.StreakAimPoint + new Vector3(0, 1f, 0)), c, 0.6f, mode: 2);
        }
        gfx.SetDepthWrite(true);
        gfx.SetBlend(false);
    }

    /// <summary>A ring of glowing pegs on the ground (strike danger zone).</summary>
    private static void Ring(Renderer gfx, Vector3 center, float radius, Vector3 color, float alpha)
    {
        const int n = 20;
        for (var i = 0; i < n; i++)
        {
            var a = i * MathF.Tau / n;
            var p = center + new Vector3(MathF.Cos(a) * radius, 0.08f, MathF.Sin(a) * radius);
            gfx.Draw(Matrix4x4.CreateScale(0.9f, 0.08f, 0.25f) * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(p), color, alpha, mode: 2);
        }
    }

    /// <summary>Glowing marks along a strike line (on the real landing spots when <paramref name="exact"/>, else flat). Smoke Screen's runs across the facing.</summary>
    private static void LineMarks(Renderer gfx, StreakId kind, Vector3 center, float yaw, Vector3 color, float alpha, bool exact)
    {
        var (length, count, across) = KillStreaks.LineShape(kind);
        var lineYaw = across ? yaw + MathF.PI / 2 : yaw;
        var dir = World.FlatForward(lineYaw);
        for (var i = 0; i < count; i++)
        {
            var off = -length / 2 + i * length / (count - 1);
            var p = center + dir * off;
            p = exact ? KillStreaks.GroundAt(p.X, p.Z, center.Y) : p;
            gfx.Draw(Matrix4x4.CreateScale(0.5f, 0.08f, 1.6f) * Matrix4x4.CreateRotationY(-lineYaw) * Matrix4x4.CreateTranslation(p + new Vector3(0, 0.08f, 0)), color, alpha, mode: 2);
        }
    }

    internal static void DrawTurret(Renderer gfx, StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        var pos = e.RenderPos;
        var hub = pos + new Vector3(0, 0.62f, 0);
        // Tripod legs
        for (var i = 0; i < 3; i++)
        {
            var a = e.BaseYaw + i * MathF.Tau / 3;
            gfx.Draw(Matrix4x4.CreateScale(0.07f, 0.78f, 0.07f) * Matrix4x4.CreateTranslation(0, -0.39f, 0) * Matrix4x4.CreateRotationX(0.5f)
                     * Matrix4x4.CreateRotationY(-a) * Matrix4x4.CreateTranslation(hub), DarkMetal, 1f, material: Material.Metal);
        }
        gfx.Draw(Matrix4x4.CreateScale(0.18f, 0.5f, 0.18f) * Matrix4x4.CreateTranslation(hub + new Vector3(0, 0.15f, 0)), Gunmetal, 1f, material: Material.Metal);
        // Head: turns and pitches with the gun
        var head = Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(pos + new Vector3(0, 1.05f, 0));
        gfx.Draw(Matrix4x4.CreateScale(0.5f, 0.3f, 0.55f) * head, Gunmetal, 1f, material: Material.Metal);
        gfx.Draw(Matrix4x4.CreateScale(0.52f, 0.07f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.12f, 0.05f) * head, stripe, 1f); // owner's colour
        gfx.Draw(Matrix4x4.CreateScale(0.3f, 0.2f, 0.3f) * Matrix4x4.CreateTranslation(0, -0.02f, 0.38f) * head, DarkMetal, 1f); // ammo box
        for (var s = -1; s <= 1; s += 2)
            gfx.Draw(Matrix4x4.CreateScale(0.06f, 0.06f, 0.62f) * Matrix4x4.CreateTranslation(s * 0.09f, 0, -0.55f) * head, DarkMetal, 1f);
        // Sensor eye: green for yours and friends', red for enemies'; brighter while it has a target
        var lit = e.Target != byte.MaxValue;
        var eye = friendly ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.15f, 0.1f);
        gfx.Draw(Matrix4x4.CreateScale(0.12f, 0.08f, 0.04f) * Matrix4x4.CreateTranslation(0, 0.05f, -0.29f) * head, eye * (lit ? 1.6f : 0.8f), 1f, mode: 2);
        if (e.Firing && ((int)(now * 30) & 1) == 0)
            gfx.Draw(Matrix4x4.CreateScale(0.22f) * Matrix4x4.CreateTranslation(0, 0, -0.95f) * head, new Vector3(1f, 0.85f, 0.4f), 1f, mode: 2);
        HealthBar(gfx, e, pos + new Vector3(0, 1.55f, 0), friendly);
    }

    internal static void DrawDrone(Renderer gfx, StreakEntity e, Vector3 stripe, bool friendly, double now)
    {
        var body = Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(e.RenderPos);
        gfx.Draw(Matrix4x4.CreateScale(0.55f, 0.2f, 0.7f) * body, Gunmetal, 1f, material: Material.Metal);
        gfx.Draw(Matrix4x4.CreateScale(0.57f, 0.06f, 0.4f) * Matrix4x4.CreateTranslation(0, 0.11f, 0) * body, stripe, 1f);
        for (var i = 0; i < 2; i++)
            gfx.Draw(Matrix4x4.CreateScale(1.9f, 0.06f, 0.09f) * Matrix4x4.CreateRotationY(MathF.PI / 4 + i * MathF.PI / 2) * body, DarkMetal, 1f);
        var spin = (float)now * 45f;
        for (var i = 0; i < 4; i++)
        {
            var a = MathF.PI / 4 + i * MathF.PI / 2;
            var hubPos = new Vector3(MathF.Cos(a) * 0.67f, 0.1f, MathF.Sin(a) * 0.67f);
            gfx.Draw(Matrix4x4.CreateScale(0.1f, 0.12f, 0.1f) * Matrix4x4.CreateTranslation(hubPos) * body, DarkMetal, 1f);
            gfx.Draw(Matrix4x4.CreateScale(0.62f, 0.015f, 0.07f) * Matrix4x4.CreateRotationY(spin + i) * Matrix4x4.CreateTranslation(hubPos + new Vector3(0, 0.07f, 0)) * body,
                new Vector3(0.5f, 0.5f, 0.52f), 1f);
        }
        // Gun pod under the body, pointing at its target
        var pod = Matrix4x4.CreateRotationX(e.RenderPitch) * Matrix4x4.CreateRotationY(-e.RenderYaw) * Matrix4x4.CreateTranslation(e.RenderPos - new Vector3(0, 0.25f, 0));
        gfx.Draw(Matrix4x4.CreateScale(0.16f, 0.14f, 0.5f) * Matrix4x4.CreateTranslation(0, 0, -0.1f) * pod, DarkMetal, 1f);
        var light = friendly ? new Vector3(0.3f, 1f, 0.4f) : new Vector3(1f, 0.15f, 0.1f);
        var blink = ((int)(now * 3) & 1) == 0;
        gfx.Draw(Matrix4x4.CreateScale(0.1f) * Matrix4x4.CreateTranslation(0, -0.08f, -0.38f) * body, light * (blink ? 1.6f : 0.5f), 1f, mode: 2);
        if (e.Firing && ((int)(now * 30) & 1) == 0)
            gfx.Draw(Matrix4x4.CreateScale(0.25f) * Matrix4x4.CreateTranslation(0, 0, -0.45f) * pod, new Vector3(1f, 0.85f, 0.4f), 1f, mode: 2);
        HealthBar(gfx, e, e.RenderPos + new Vector3(0, 0.6f, 0), friendly);
    }

    /// <summary>A small bar over a turret or drone: always for its owner and their team, for enemies once it's damaged.</summary>
    private static void HealthBar(Renderer gfx, StreakEntity e, Vector3 at, bool friendly)
    {
        var f = Math.Clamp(e.Health / (float)e.MaxHealth, 0f, 1f);
        if (f >= 0.999f && !friendly)
            return;
        gfx.Draw(Matrix4x4.CreateScale(0.8f, 0.06f, 0.06f) * Matrix4x4.CreateTranslation(at), new Vector3(0.1f, 0.1f, 0.1f), 1f, mode: 2);
        gfx.Draw(Matrix4x4.CreateScale(0.8f * f, 0.07f, 0.07f) * Matrix4x4.CreateTranslation(at + new Vector3(-0.4f * (1 - f), 0, 0)),
            Vector3.Lerp(new Vector3(1f, 0.2f, 0.15f), new Vector3(0.3f, 1f, 0.35f), f), 1f, mode: 2);
    }

    /// <summary>The strafe-run jet: a box-built plane flying along the line, low over the target, when it's near.</summary>
    private static void DrawJet(Renderer gfx, StrikeMarker m, double now)
    {
        var pass = m.Born + KillStreaks.JetPassTime(m.Kind);
        var along = (float)(now - pass) * KillStreaks.JetSpeed(m.Kind);
        if (MathF.Abs(along) > 260)
            return;
        var dir = World.FlatForward(m.Yaw);
        var p = m.Pos + dir * along + new Vector3(0, KillStreaks.StrafeAltitude, 0);
        var jet = Matrix4x4.CreateRotationX(-0.05f) * Matrix4x4.CreateRotationY(-m.Yaw) * Matrix4x4.CreateTranslation(p);
        var hull = new Vector3(0.42f, 0.45f, 0.48f);
        gfx.Draw(Matrix4x4.CreateScale(1.1f, 1f, 8f) * jet, hull, 1f, material: Material.Metal);
        gfx.Draw(Matrix4x4.CreateScale(0.6f, 0.6f, 1.6f) * Matrix4x4.CreateTranslation(0, 0, -4.4f) * jet, hull * 0.9f, 1f); // nose
        gfx.Draw(Matrix4x4.CreateScale(0.7f, 0.4f, 1.4f) * Matrix4x4.CreateTranslation(0, 0.55f, -2.2f) * jet, new Vector3(0.15f, 0.2f, 0.3f), 1f); // canopy
        gfx.Draw(Matrix4x4.CreateScale(10f, 0.18f, 2.6f) * Matrix4x4.CreateTranslation(0, -0.1f, 0.4f) * jet, hull * 0.85f, 1f, material: Material.Metal); // wings
        gfx.Draw(Matrix4x4.CreateScale(3.6f, 0.15f, 1.2f) * Matrix4x4.CreateTranslation(0, 0.1f, 3.6f) * jet, hull * 0.85f, 1f); // tailplane
        gfx.Draw(Matrix4x4.CreateScale(0.18f, 1.8f, 1.5f) * Matrix4x4.CreateTranslation(0, 1.2f, 3.4f) * jet, hull * 0.8f, 1f); // fin
        gfx.Draw(Matrix4x4.CreateScale(0.7f, 0.6f, 0.4f) * Matrix4x4.CreateTranslation(0, 0, 4.1f) * jet, new Vector3(1f, 0.55f, 0.2f), 1f, mode: 2); // afterburner
    }
}
