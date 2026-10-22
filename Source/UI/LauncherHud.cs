using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// Launchers on the HUD (ImGui background pass, under the HUD windows): with a lock-on launcher aimed, every enemy
/// flying machine in range gets a faint diamond and the one being locked a bracket that closes in as the lock builds,
/// yellow, then red with "LOCKED"; a wire-guided rocket of yours in the air shows the guide dot; a sticky launcher shows
/// how many charges you have out and how to set them off. Allocation-free on the common path (labels are constants).
/// </summary>
public static class LauncherHud
{
    private static readonly Vector3 Lock = new(1f, 0.85f, 0.2f);
    private static readonly Vector3 Locked = new(1f, 0.2f, 0.15f);
    private static readonly string[] Charges = ["", "1 CHARGE OUT - AIM + FIRE TO DETONATE", "2 CHARGES OUT - AIM + FIRE TO DETONATE", "3 CHARGES OUT - AIM + FIRE TO DETONATE"];

    public static void DrawMarkers(Game game, double now, float scale)
    {
        var cam = Hud.Camera;
        if (!cam.Valid || game.Local is not { Alive: true } me)
            return;
        var def = game.Active.Def;
        if (!def.IsLauncher)
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var dl = ImGui.GetBackgroundDrawList();

        if (def.LauncherGuidance == LauncherGuidance.LockOn && game.AimT > 0.3f)
        {
            foreach (var e in game.StreakEntities.Values)
            {
                if (!game.StreakMachineHostile(e, me) || !LauncherTargets.IsAirborne(e))
                    continue;
                var c = e.Hitbox.Center - e.Pos + e.RenderPos;
                if (Vector3.Distance(cam.Eye, c) > def.LauncherLockRange || !Hud.Project(cam.ViewProj, c, area.X, area.Y, out var s, out _, 40f))
                    continue;
                var p = new float2(org.X + s.X, org.Y + s.Y);
                if (e.Id == game.LockTarget)
                {
                    var locked = game.Locked;
                    var half = (locked ? 18f : 18f + 30f * (1f - game.LockProgress)) * scale;
                    var col = Hud.Colour(locked ? Locked : Lock, 0.95f);
                    var k = half * 0.35f;
                    // Four corner brackets.
                    for (var sx = -1; sx <= 1; sx += 2)
                        for (var sy = -1; sy <= 1; sy += 2)
                        {
                            var corner = new float2(p.X + sx * half, p.Y + sy * half);
                            dl.AddLine(corner, new float2(corner.X - sx * k, corner.Y), col, 2.5f * scale);
                            dl.AddLine(corner, new float2(corner.X, corner.Y - sy * k), col, 2.5f * scale);
                        }
                    if (locked)
                        Hud.ShadowText(dl, new float2(p.X - Hud.TextWidth("LOCKED", 15f * scale) / 2, p.Y + half + 4 * scale), 15f * scale, "LOCKED", Locked, 1f);
                }
                else
                {
                    var r = 7f * scale;
                    var col = Hud.Colour(Lock, 0.5f);
                    dl.AddLine(new float2(p.X, p.Y - r), new float2(p.X + r, p.Y), col, 1.5f * scale);
                    dl.AddLine(new float2(p.X + r, p.Y), new float2(p.X, p.Y + r), col, 1.5f * scale);
                    dl.AddLine(new float2(p.X, p.Y + r), new float2(p.X - r, p.Y), col, 1.5f * scale);
                    dl.AddLine(new float2(p.X - r, p.Y), new float2(p.X, p.Y - r), col, 1.5f * scale);
                }
            }
        }

        if (def.LauncherGuidance == LauncherGuidance.Wire)
        {
            foreach (var r in game.Rockets.Values)
            {
                if (r.Owner != me.Id || !r.Wire || r.Hidden)
                    continue;
                // The guide dot in the middle of the screen while one of your rockets is on the wire.
                var c = new float2(org.X + area.X / 2, org.Y + area.Y / 2);
                ImDrawListExtensions.AddCircle(dl, in c, 9f * scale, Hud.Colour(Locked, 0.9f), 16, 2f * scale);
                Hud.ShadowText(dl, new float2(c.X - Hud.TextWidth("GUIDING", 13f * scale) / 2, c.Y + 14 * scale), 13f * scale, "GUIDING", Locked, 0.9f);
                break;
            }
        }

        if (def.LauncherProjectile == LauncherProjectile.Sticky)
        {
            var n = Math.Min(game.LocalStickiesOut(), Charges.Length - 1);
            if (n > 0)
            {
                var text = Charges[n];
                var size = 14f * scale;
                Hud.ShadowText(dl, new float2(org.X + area.X / 2 - Hud.TextWidth(text, size) / 2, org.Y + area.Y * 0.62f), size, text, Lock, 0.9f);
            }
        }
    }
}
