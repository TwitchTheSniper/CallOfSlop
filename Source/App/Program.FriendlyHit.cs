using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The friendly hit marker (Source/Game/FriendlyHits.cs): when your hit only reached a teammate, a cyan "no entry" sign (a ring
/// with a slash) replaces the red/yellow corner marks at the crosshair, with a small "FRIENDLY" under it.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>The friendly marker's colour: a cyan that reads apart from the red (body) and yellow (headshot) markers.</summary>
    private static readonly Vector3 FriendlyMarkerColour = new(0.3f, 0.85f, 1f);

    private const int FriendlyRingDots = 32;

    /// <summary>GL HUD pass: the ring and slash at the crosshair, built from small squares, with a dark outline; fades out at the end.</summary>
    private static void DrawFriendlyHitMarker(float cx, float cy, float k, double now)
    {
        var alpha = FriendlyMarkerAlpha(_game, now);
        var r = 13f * k;
        var dot = MathF.Max(2f, 3f * k);
        for (var pass = 0; pass < 2; pass++)
        {
            // Pass 0: a slightly bigger dark outline under everything; pass 1: the colour.
            var size = pass == 0 ? dot + 2f : dot;
            var colour = pass == 0 ? Vector3.Zero : FriendlyMarkerColour;
            var a = pass == 0 ? alpha * 0.55f : alpha;
            for (var i = 0; i < FriendlyRingDots; i++)
            {
                var t = i * MathF.Tau / FriendlyRingDots;
                Gfx.Rect(cx + MathF.Cos(t) * r - size / 2, cy + MathF.Sin(t) * r - size / 2, size, size, colour, a);
            }
            // The slash, top-left to bottom-right (the HUD's y points up).
            var steps = (int)MathF.Ceiling(2f * r * 0.72f / (dot * 0.6f));
            for (var i = 0; i <= steps; i++)
            {
                var u = (i / (float)steps * 2f - 1f) * r * 0.72f;
                Gfx.Rect(cx + u - size / 2, cy - u - size / 2, size, size, colour, a);
            }
        }
    }

    /// <summary>ImGui pass: "FRIENDLY" in small capitals under the friendly marker.</summary>
    private static void DrawFriendlyHitText(Game g, double now)
    {
        if (!g.HitMarkerFriendly || now >= g.HitMarkerUntil || Ui.MenuOpen || g.Local is not { Alive: true })
            return;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        var px = 13f * Ui.Scale;
        var width = Hud.TextWidth("FRIENDLY"u8, px);
        var top = new float2(org.X + area.X / 2 - width / 2, org.Y + area.Y / 2 + 20f * Ui.Scale);
        Hud.ShadowText(ImGui.GetBackgroundDrawList(), top, px, "FRIENDLY"u8, FriendlyMarkerColour, FriendlyMarkerAlpha(g, now));
    }

    /// <summary>Full for most of the marker's time, fading over its last 0.1 s.</summary>
    private static float FriendlyMarkerAlpha(Game g, double now) => (float)Math.Clamp((g.HitMarkerUntil - now) / 0.1, 0.0, 1.0);
}
