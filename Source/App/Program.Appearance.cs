using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The APPEARANCE screen's 3D model (Source/UI/UiAppearance.cs): drawn behind the UI like the gunsmith's gun (the window
/// has no ImGui background; this paints it), in its own viewport, with the same immediate-mode player drawing the game
/// uses for other players (<see cref="PlayerRender.DrawPreview"/>): your look, your hat, the default primary in your hands.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>A player that only exists to be drawn on the APPEARANCE screen (never in a session).</summary>
    private static readonly Player _lookPlayer = new() { Id = 1, Team = Game.NoTeam };

    private static void DrawAppearancePreview(AppearancePreview pv, double now)
    {
        var fb = _window.FramebufferSize;
        var ws = _window.Size;
        if (fb.X <= 0 || fb.Y <= 0 || ws.X <= 0 || ws.Y <= 0)
            return;
        float sx = fb.X / (float)ws.X, sy = fb.Y / (float)ws.Y;
        SkyRenderer.UseDefaultLight(); // lit the same whatever the map behind the menu

        // Backdrop over the whole window, and a soft stage behind the model.
        Gfx.SetCamera(Matrix4x4.Identity, Matrix4x4.CreateOrthographicOffCenter(0, fb.X, 0, fb.Y, -1, 1));
        Gfx.SetDepthTest(false);
        Gfx.SetBlend(true);
        float wx = pv.WinMin.X * sx, wy = fb.Y - pv.WinMax.Y * sy, ww = (pv.WinMax.X - pv.WinMin.X) * sx, wh = (pv.WinMax.Y - pv.WinMin.Y) * sy;
        Gfx.Rect(wx, wy, ww, wh, new Vector3(0.035f, 0.04f, 0.05f), 0.97f);
        float vx = pv.ViewMin.X * sx, vy = fb.Y - pv.ViewMax.Y * sy, vw = (pv.ViewMax.X - pv.ViewMin.X) * sx, vh = (pv.ViewMax.Y - pv.ViewMin.Y) * sy;
        for (var i = 0; i < 6; i++)
        {
            var inset = i * MathF.Min(vw, vh) * 0.06f;
            Gfx.Rect(vx + inset, vy + inset, vw - 2 * inset, vh - 2 * inset, new Vector3(0.09f, 0.1f, 0.13f), 0.18f);
        }
        Gfx.SetBlend(false);
        if (vw < 8 || vh < 8)
            return;

        var p = _lookPlayer;
        p.Name = pv.Name;
        p.Look = pv.Look;
        p.Hat = pv.Hat;
        p.Prestige = (byte)Math.Clamp(pv.Prestige, 0, Cosmetics.MaxPrestige);
        p.Alive = true;
        p.Health = World.MaxHealth;
        p.RenderPosition = Vector3.Zero;
        p.RenderYaw = pv.Yaw;
        p.RenderPitch = 0;
        p.RenderStance = Stance.Stand;
        p.RenderVelocity = Vector3.Zero;
        p.RenderGrounded = true;
        p.RenderMantling = false;

        // Frame the body plus the hat (the Ridiculous Top Hat is tall), feet to crown, a little from above.
        var hatH = HatRender.HatFor(p) is { } hat ? hat.Height * (BodyModel.Stances[0].HeadSize.X / HatRender.ModelHeadWidth) : 0f;
        var top = World.PlayerHeight + hatH + 0.1f;
        var cy = top / 2;
        var aspect = vw / MathF.Max(vh, 1);
        const float fov = 0.55f;
        var half = MathF.Max(top / 2 + 0.15f, 0.75f / aspect); // the gun and arms reach out sideways
        var dist = half / MathF.Tan(fov / 2);
        var view = Matrix4x4.CreateLookAt(new Vector3(0, cy + dist * 0.12f, -dist), new Vector3(0, cy, 0), Vector3.UnitY);

        Gfx.SetViewport((int)vx, (int)vy, (int)vw, (int)vh);
        Gfx.ClearDepthBuffer();
        Gfx.SetDepthTest(true);
        Gfx.SetCamera(view, Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.05f, 60f));
        Gfx.Draw(Matrix4x4.CreateScale(1.6f, 0.04f, 1.6f) * Matrix4x4.CreateTranslation(0, -0.02f, 0), new Vector3(0.16f, 0.17f, 0.2f)); // a plinth
        PlayerRender.DrawPreview(Gfx, p, now);
        Gfx.SetViewport(0, 0, fb.X, fb.Y);
    }
}
