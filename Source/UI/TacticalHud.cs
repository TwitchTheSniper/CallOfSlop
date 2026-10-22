using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The tactical equipment on the HUD: the flashbang white-out and the stun's edge tint (GL rects, drawn from
/// Program.DrawHud), and in the ImGui pass the motion sensor pings, the decoys' fake name tags (for their owner's
/// enemies), and labels on your own devices. Plus <see cref="Icon"/>, the loadout card icons.
/// <para>
/// Accessibility: the white-out never flickers (TacticalGear.FlashAlpha only holds then falls), is slightly warm
/// instead of pure white, and you keep full control of movement and shooting the whole time.
/// </para>
/// </summary>
public static class TacticalHud
{
    private static readonly Vector3 FlashWhite = new(0.97f, 0.97f, 0.93f);
    private static readonly Vector3 StunTint = new(0.95f, 0.8f, 0.35f);

    /// <summary>Full-screen effects on the local player (GL pass, blending on, after the damage flash).</summary>
    public static void DrawOverlay(Renderer gfx, Game game, double now, float w, float h)
    {
        if (game.Local is not { Alive: true })
            return;
        var stun = game.LocalStun(now);
        if (stun > 0)
        {
            // Soft warm bands closing in from the edges (four rects per band, three bands): a vignette, never a flash.
            for (var i = 0; i < 3; i++)
            {
                var t = (i + 1) * 0.06f * MathF.Min(w, h);
                var a = 0.12f * stun;
                gfx.Rect(0, 0, w, t, StunTint, a);
                gfx.Rect(0, h - t, w, t, StunTint, a);
                gfx.Rect(0, t, t, h - 2 * t, StunTint, a);
                gfx.Rect(w - t, t, t, h - 2 * t, StunTint, a);
            }
        }
        var flash = game.LocalFlashAlpha(now);
        if (flash > 0.002f)
            gfx.Rect(0, 0, w, h, FlashWhite, flash);
    }

    /// <summary>ImGui pass (background draw list, under the HUD windows): pings, decoy tags, your devices.</summary>
    public static void DrawMarkers(Game game, double now, float scale)
    {
        var cam = Hud.Camera;
        if (!cam.Valid || game.Local is not { } me)
            return;
        var flashed = game.LocalFlashHidesHud(now);
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var dl = ImGui.GetBackgroundDrawList();

        // Motion sensor pings: a red diamond where the enemy was, fading out.
        if (!flashed)
            foreach (ref readonly var ping in game.Pings.AsSpan())
            {
                var age = (float)(now - ping.At);
                if (ping.At <= 0 || age < 0 || age > TacticalGear.PingShow)
                    continue;
                if (!Hud.Project(cam.ViewProj, ping.Pos + new Vector3(0, 1.1f, 0), area.X, area.Y, out var s, out _, 30f))
                    continue;
                var a = 1f - age / (float)TacticalGear.PingShow;
                var r = (7f + 5f * (1f - a)) * scale;
                var c = new float2(org.X + s.X, org.Y + s.Y);
                var col = Hud.Colour(new Vector3(1f, 0.25f, 0.2f), 0.85f * a);
                ImDrawListExtensions.AddNgonFilled(dl, in c, r, col, 4);
                ImDrawListExtensions.AddCircle(dl, in c, r * 1.6f, Hud.Colour(new Vector3(1f, 0.4f, 0.3f), 0.5f * a), 4, 1.5f * scale);
            }

        foreach (var e in game.TacDevices)
        {
            var mine = e.Owner == me.Id;
            var friendly = TacticalRender.IsFriendly(game, e.Owner);
            if (e.Kind == ThrowableKind.Decoy && !friendly && !flashed && me.Alive)
            {
                // The decoy's lie: an enemy name tag with its owner's name, like the real ones (Source/UI/NameTags.cs).
                var top = e.Pos + new Vector3(0, 2.0f, 0);
                var d2 = Vector3.DistanceSquared(cam.Eye, top);
                if (d2 < TacticalGear.DecoyTagRange * TacticalGear.DecoyTagRange && Vector3.Dot(top - cam.Eye, cam.Forward) > 0)
                {
                    var to = e.Pos + new Vector3(0, 0.3f, 0) - cam.Eye;
                    var len = to.Length();
                    if (len < 0.1f || World.Query.Raycast(cam.Eye, to / len, len, QueryFilter.All) >= len - 0.2f)
                        Hud.WorldLabel(top, game.NameOf(e.Owner), NameTagRules.Default.EnemyColour, 0.9f, 15f * scale);
                }
            }
            else if (mine && !flashed)
            {
                var label = e.Kind switch
                {
                    ThrowableKind.Trophy => $"Trophy {e.Charges}/{TacticalGear.TrophyCharges}",
                    ThrowableKind.Insertion => "Your spawn",
                    ThrowableKind.Cover => $"Cover {e.Health * 100 / Math.Max(1, e.MaxHealth)}%",
                    _ => null,
                };
                if (label != null)
                    Hud.WorldLabel(e.Pos + new Vector3(0, e.Kind == ThrowableKind.Cover ? 1.45f : 1.0f, 0), label, new Vector3(0.45f, 0.95f, 0.6f), 0.8f, 13f * scale, 30f);
            }
        }
    }

    /// <summary>
    /// The loadout icon of a tactical item at <paramref name="c"/>, radius <paramref name="r"/>, in its colour. Returns
    /// false for kinds that keep the plain grenade / hexagon icon.
    /// </summary>
    public static bool Icon(ImDrawListPtr dl, ThrowableKind kind, float2 c, float r)
    {
        if (!TacticalGear.Is(kind))
            return false;
        var col = Hud.Colour(Throwables.Get(kind).Color, 1f);
        var dark = new ImColor8(40, 42, 46, 255);
        var light = new ImColor8(235, 235, 225, 255);
        float2 P(float x, float y) => new(c.X + x * r, c.Y + y * r);
        switch (kind)
        {
            case ThrowableKind.Flash: // a can with rays
                ImDrawListExtensions.AddRectFilled(dl, P(-0.35f, -0.5f), P(0.35f, 0.7f), col, 3, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.2f, -0.75f), P(0.2f, -0.5f), dark, 1, ImDrawFlags.None);
                for (var i = 0; i < 6; i++)
                {
                    var a = i * MathF.PI / 3 + 0.3f;
                    ImDrawListExtensions.AddLine(dl, P(MathF.Cos(a) * 0.62f, MathF.Sin(a) * 0.62f), P(MathF.Cos(a) * 0.95f, MathF.Sin(a) * 0.95f), light, 2f);
                }
                break;
            case ThrowableKind.Stun: // a ball with a lightning bolt
                ImDrawListExtensions.AddCircleFilled(dl, c, r * 0.75f, col, 20);
                ImDrawListExtensions.AddLine(dl, P(0.1f, -0.55f), P(-0.15f, 0.05f), dark, 3f);
                ImDrawListExtensions.AddLine(dl, P(-0.15f, 0.05f), P(0.15f, 0.0f), dark, 3f);
                ImDrawListExtensions.AddLine(dl, P(0.15f, 0.0f), P(-0.1f, 0.55f), dark, 3f);
                break;
            case ThrowableKind.Decoy: // a grenade with sound waves
                ImDrawListExtensions.AddCircleFilled(dl, P(-0.3f, 0.1f), r * 0.45f, col, 16);
                for (var i = 1; i <= 3; i++)
                    ImDrawListExtensions.AddCircle(dl, P(-0.3f, 0.1f), r * (0.45f + 0.22f * i), Hud.Colour(Throwables.Get(kind).Color, 1f - i * 0.25f), 12, 1.5f);
                break;
            case ThrowableKind.MotionSensor: // a puck, an antenna and radar arcs
                ImDrawListExtensions.AddRectFilled(dl, P(-0.5f, 0.25f), P(0.5f, 0.6f), col, 4, ImDrawFlags.None);
                ImDrawListExtensions.AddLine(dl, P(0.25f, 0.25f), P(0.25f, -0.55f), dark, 2f);
                ImDrawListExtensions.AddCircle(dl, P(0.25f, -0.55f), r * 0.25f, col, 10, 1.5f);
                ImDrawListExtensions.AddCircle(dl, P(0.25f, -0.55f), r * 0.45f, col, 12, 1.2f);
                break;
            case ThrowableKind.Trophy: // a launcher with three lamps
                ImDrawListExtensions.AddRectFilled(dl, P(-0.55f, 0.2f), P(0.55f, 0.65f), col, 2, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.3f, -0.4f), P(0.3f, 0.2f), col, 2, ImDrawFlags.None);
                for (var i = -1; i <= 1; i++)
                    ImDrawListExtensions.AddCircleFilled(dl, P(i * 0.3f, 0.42f), r * 0.1f, new ImColor8(90, 255, 120, 255), 8);
                ImDrawListExtensions.AddLine(dl, P(0, -0.4f), P(0, -0.75f), dark, 2f);
                break;
            case ThrowableKind.Cover: // a shield wall between two posts
                ImDrawListExtensions.AddRectFilled(dl, P(-0.7f, -0.45f), P(0.7f, 0.45f), Hud.Colour(Throwables.Get(kind).Color, 0.6f), 2, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.8f, -0.55f), P(-0.62f, 0.6f), dark, 1, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(0.62f, -0.55f), P(0.8f, 0.6f), dark, 1, ImDrawFlags.None);
                break;
            case ThrowableKind.Insertion: // a flare stick with a glow
                ImDrawListExtensions.AddCircleFilled(dl, P(0, -0.45f), r * 0.32f, Hud.Colour(Throwables.Get(kind).Color, 0.5f), 14);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.1f, -0.45f), P(0.1f, 0.6f), col, 1, ImDrawFlags.None);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.4f, 0.55f), P(0.4f, 0.7f), dark, 1, ImDrawFlags.None);
                break;
            case ThrowableKind.Stim: // a syringe
                ImDrawListExtensions.AddRectFilled(dl, P(-0.6f, -0.15f), P(0.35f, 0.15f), col, 2, ImDrawFlags.None);
                ImDrawListExtensions.AddLine(dl, P(0.35f, 0f), P(0.8f, 0f), light, 2f);
                ImDrawListExtensions.AddRectFilled(dl, P(-0.75f, -0.3f), P(-0.6f, 0.3f), dark, 1, ImDrawFlags.None);
                ImDrawListExtensions.AddLine(dl, P(-0.4f, -0.15f), P(-0.4f, 0.15f), light, 1.5f);
                ImDrawListExtensions.AddLine(dl, P(-0.15f, -0.15f), P(-0.15f, 0.15f), light, 1.5f);
                break;
        }
        return true;
    }
}
