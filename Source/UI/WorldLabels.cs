using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>The 3D camera of the frame being drawn, as the HUD needs it to place text at world positions.</summary>
public readonly record struct HudCamera(Matrix4x4 ViewProj, Vector3 Eye, Vector3 Forward, bool Valid);

/// <summary>
/// HUD text at 3D positions: a world-to-screen projection (pure math, so the self-test can check it) and
/// <see cref="WorldLabel"/>, which draws a small shadowed label above a world point on ImGui's background draw list
/// (over the 3D scene, under every HUD window). Program sets <see cref="Camera"/> once per frame from the real view
/// and projection, so labels line up with the picture at any FOV, zoom or aspect ratio (16:9, 21:9, 32:9).
/// Used by the name tags (<see cref="NameTags"/>) and the Zombies dropped-build labels.
/// </summary>
public static class Hud
{
    /// <summary>Points closer to the eye plane than this (clip w, metres) count as behind the camera.</summary>
    public const float NearW = 0.05f;

    /// <summary>The camera of the frame being drawn (set by Program.Render; <c>Valid</c> is false outside a match).</summary>
    public static HudCamera Camera;

    /// <summary>
    /// How many times HUD text was asked for outside an ImGui frame (and skipped). ImGui has no current font between
    /// <c>ImGui.Render</c> and the next <c>ImGui.NewFrame</c>, so text measured or drawn there dereferences null in native
    /// code: a crash no managed handler sees (board item dev-link-tool-mp-crash-lc01, reopened: the linking tool's port
    /// markers were drawn from the input phase, before the frame's NewFrame). The self-tests want this to stay 0.
    /// </summary>
    public static int OutOfFrame;

    /// <summary>Is an ImGui frame open (a context, between NewFrame and Render), so text can be measured and drawn?</summary>
    public static bool CanDraw()
    {
        var ctx = ImGui.GetCurrentContext();
        if (ctx.IsNull() || !ctx.WithinFrameScope)
        {
            OutOfFrame++;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Projects a world point to screen pixels (origin top-left, y down) on a <paramref name="width"/> x
    /// <paramref name="height"/> screen. Returns false when the point is behind the camera (or not finite), or more
    /// than <paramref name="margin"/> pixels outside the screen; <paramref name="screen"/> is still filled in front of it.
    /// <paramref name="depth"/> is the distance in front of the eye plane (clip w).
    /// </summary>
    public static bool Project(in Matrix4x4 viewProj, Vector3 world, float width, float height, out Vector2 screen, out float depth, float margin = 0f)
    {
        var c = Vector4.Transform(new Vector4(world, 1f), viewProj);
        depth = c.W;
        screen = default;
        if (!(c.W > NearW) || !float.IsFinite(c.X + c.Y))
            return false;
        var inv = 1f / c.W;
        var x = (c.X * inv * 0.5f + 0.5f) * width;
        var y = (0.5f - c.Y * inv * 0.5f) * height;
        screen = new Vector2(x, y);
        return x >= -margin && x <= width + margin && y >= -margin && y <= height + margin;
    }

    /// <summary>A colour plus alpha (0..1) as ImGui's packed colour.</summary>
    public static ImColor8 Colour(Vector3 c, float alpha) =>
        new(B(c.X), B(c.Y), B(c.Z), B(alpha));

    private static byte B(float v) => (byte)(Math.Clamp(float.IsFinite(v) ? v : 0f, 0f, 1f) * 255f + 0.5f);

    /// <summary>Draws text with a 1-2 px dark shadow at <paramref name="topLeft"/>, <paramref name="size"/> pixels tall. ImDrawList text is never printf-formatted.</summary>
    public static void ShadowText(ImDrawListPtr dl, float2 topLeft, float size, ImString text, Vector3 colour, float alpha)
    {
        if (!float.IsFinite(topLeft.X + topLeft.Y + size) || size <= 0f || !CanDraw())
            return; // (outside a frame there is no font: native code would read null)
        var off = MathF.Max(1f, MathF.Round(size / 14f));
        dl.AddText(ImGui.GetFont(), size, new float2(topLeft.X + off, topLeft.Y + off), Colour(Vector3.Zero, alpha * 0.8f), text);
        dl.AddText(ImGui.GetFont(), size, topLeft, Colour(colour, alpha), text);
    }

    /// <summary>Width in pixels of <paramref name="text"/> drawn <paramref name="size"/> pixels tall.</summary>
    public static float TextWidth(ImString text, float size) => CanDraw() ? ImGui.CalcTextSize(text).X * size / MathF.Max(ImGui.GetFontSize(), 1f) : 0f;

    /// <summary>
    /// Draws <paramref name="text"/> centred just above the world point <paramref name="pos"/>, kept inside the screen.
    /// <paramref name="size"/> is the text height in pixels (0 = the current HUD font size); <paramref name="maxDistance"/>
    /// (metres, 0 = any) hides it further away. Returns false (nothing drawn) when the point is behind the camera,
    /// off screen or too far, so a caller can fall back to a HUD line. Call between ImGui's NewFrame and Render.
    /// </summary>
    public static bool WorldLabel(Vector3 pos, ImString text, Vector3 colour, float alpha = 1f, float size = 0f, float maxDistance = 0f)
    {
        var cam = Camera;
        if (!cam.Valid || alpha <= 0f || !float.IsFinite(pos.X + pos.Y + pos.Z) || !CanDraw())
            return false;
        if (maxDistance > 0f && Vector3.DistanceSquared(cam.Eye, pos) > maxDistance * maxDistance)
            return false;
        var vp = ImGui.GetMainViewport();
        float2 org = vp.WorkPos, area = vp.WorkSize;
        if (!Project(cam.ViewProj, pos, area.X, area.Y, out var s, out _))
            return false;
        if (size <= 0f)
            size = ImGui.GetFontSize();
        var w = TextWidth(text, size);
        var (x, y) = NameTagLayout.Clamp(s.X - w / 2, s.Y - size, w, size, area.X, area.Y, 4f);
        ShadowText(ImGui.GetBackgroundDrawList(), new float2(org.X + x, org.Y + y), size, text, colour, alpha);
        return true;
    }
}
