using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Blood by health: the screen overlay for your own health (Source/UI/BloodOverlay.cs).</summary>
public static unsafe partial class Program
{
    private static readonly BloodOverlay _bloodOverlay = new();
    private static Game? _bloodGame;

    /// <summary>
    /// In the HUD pass (ortho camera, blending on): applies the "Blood overlay" / "Blood colour" settings to the body blood,
    /// updates the overlay from your health and the replicated hit direction, and draws it in one call, under the HUD bars.
    /// </summary>
    private static void DrawBloodOverlay(double now, float w, float h, Player? local)
    {
        BodyBloodRender.Mode = _settings.BloodOverlay;
        BodyBloodRender.Colour = _settings.BloodColour;
        BodyBloodRender.BeginFrame(now);
        if (!ReferenceEquals(_bloodGame, _session))
        {
            _bloodGame = _session; // a new match: start clean
            _bloodOverlay.Clear();
        }
        var alive = local is { Alive: true };
        var frac = local != null ? local.Health / (float)World.MaxHealth : 1f;
        _bloodOverlay.Update(frac, alive, local != null ? BloodMarks.Sector(local.Blood) : 0, now, (float)FrameDt);
        var (spanLeft, spanW) = HudLayout.Span(w, h, _settings.HudWidth);
        BloodOverlayRenderer.Draw(Gfx, _bloodOverlay, w, h, spanLeft, spanW, _settings.BloodOverlay, _settings.BloodColour);
    }
}
