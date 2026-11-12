using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the Panel Designer's panel in the match (UiPanelDesigner.cs; docs/sandbox/constructs/production.md "Panel Designer") and its
/// turning 3D preview. The use key on the designer's desk opens the panel; it holds the mouse while open and closes when its designer goes,
/// the player dies or walks off, on Esc and on a local reset. The preview is drawn after the UI pass, in the panel's own rectangle and its
/// own viewport: the panel as it will be formed, a 1 m bar, a player-height figure, its edge sockets and, when asked, a beam frame under it.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>The use key on a Panel Designer's desk: opens the panel (nothing is sent until Save or Order). The press is spent by the caller's branch.</summary>
    private static void OpenPanelDesigner(Construct c, int part)
    {
        if (MenusOpen)
            return;
        Ui.OpenDesigner(c.Id, part);
        SetMouseCaptured(false);
    }

    /// <summary>The panel over the match (the Sandbox ImGui pass); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawDesignerInMatch(double now)
    {
        if (!Ui.DesignerOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var (body, part) = Ui.DesignerTarget;
        var c = g?.Constructs.Get(body);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || c == null || _mouseCaptured
            || part < 0 || part >= c.PartCount || !c.DefOf(part).IsPanelDesigner
            || Vector3.Distance(me!.Position, c.WorldCom) > c.Radius + ConstructConfig.Reach + 4;
        if (!gone && Ui.DrawDesigner(g!, c!, part, now))
            return;
        Ui.CloseDesigner();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also fire the tool)
    }

    private static readonly Vector3 PdFloor = new(0.16f, 0.18f, 0.22f), PdBar = new(0.95f, 0.69f, 0.12f), PdFigure = new(0.36f, 0.5f, 0.66f), PdSocket = new(0.95f, 0.45f, 0.1f), PdBeam = new(0.3f, 0.33f, 0.37f);

    private static void PdBox(Vector3 mn, Vector3 mx, Vector3 colour) =>
        Gfx.Draw(Matrix4x4.CreateScale(mx - mn) * Matrix4x4.CreateTranslation((mn + mx) * 0.5f), colour);

    /// <summary>The designer's preview, after the UI pass: its own viewport and depth, the default light (the same whatever the map's sky).</summary>
    private static void DrawDesignerPreview(double now)
    {
        var (min, max) = Ui.DesignerView;
        var fb = _window.FramebufferSize;
        var ws = _window.Size;
        if (!Ui.DesignerOpen || max.X - min.X < 8 || max.Y - min.Y < 8 || fb.X <= 0 || fb.Y <= 0 || ws.X <= 0 || ws.Y <= 0 || Ui.DesignerPreview.Shown is not { } d)
            return;
        float sx = fb.X / (float)ws.X, sy = fb.Y / (float)ws.Y;
        int vx = (int)(min.X * sx) + 2, vy = (int)(fb.Y - max.Y * sy) + 2, vw = (int)((max.X - min.X) * sx) - 4, vh = (int)((max.Y - min.Y) * sy) - 4;
        if (vw < 8 || vh < 8)
            return;
        SkyRenderer.UseDefaultLight();
        Gfx.SetViewport(vx, vy, vw, vh);
        OpenGl.Scissor(vx, vy, vw, vh);
        OpenGl.Enable(GlCapability.ScissorTest);
        Gfx.ClearDepthBuffer();
        Gfx.SetDepthTest(true);
        Gfx.SetBlend(false);
        Gfx.SetFog(Vector3.Zero, 0, 0);
        // The panel stands with its lowest point 0.9 m over the floor (bodywork height), or on its frame; the camera frames it with the figure.
        var size = d.BoundsMax - d.BoundsMin;
        var mid = (d.BoundsMax + d.BoundsMin) * 0.5f;
        const float deck = 0.9f;
        var lift = new Vector3(-mid.X, deck - d.BoundsMin.Y, -mid.Z);
        var radius = MathF.Max(1.25f, new Vector3(size.X + 1.2f, size.Y + deck, size.Z).Length() * 0.5f);
        var target = new Vector3(0.25f, (deck + size.Y) * 0.55f, 0);
        var spin = now - Ui.DesignerTouched > 2.5 ? (float)((now - Ui.DesignerTouched - 2.5) * 0.25) : 0f;
        var yaw = Ui.DesignerYaw + spin;
        var pitch = Ui.DesignerPitch;
        const float fov = 0.6f;
        var aspect = vw / (float)vh;
        var dist = radius / MathF.Tan(fov * 0.5f) / MathF.Min(1f, aspect) * 1.05f * Ui.DesignerZoom;
        var eye = target + new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * dist;
        Gfx.SetCamera(Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY), Matrix4x4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, dist + radius * 4 + 10));
        // The floor, the 1 m bar in front of the panel and a player-height figure beside it.
        var half = MathF.Max(size.X, size.Z) * 0.5f + 1.3f;
        PdBox(new Vector3(-half, -0.04f, -half), new Vector3(half, 0, half), PdFloor);
        var barZ = size.Z * 0.5f + 0.35f;
        PdBox(new Vector3(-0.5f, 0.002f, barZ), new Vector3(0.5f, 0.03f, barZ + 0.06f), PdBar);
        PdBox(new Vector3(-0.5f, 0.002f, barZ - 0.05f), new Vector3(-0.485f, 0.06f, barZ + 0.11f), PdBar);
        PdBox(new Vector3(0.485f, 0.002f, barZ - 0.05f), new Vector3(0.5f, 0.06f, barZ + 0.11f), PdBar);
        var fx = size.X * 0.5f + 0.75f;
        PdBox(new Vector3(fx - 0.11f, 0.002f, -0.09f), new Vector3(fx - 0.01f, 0.86f, 0.09f), PdFigure); // legs
        PdBox(new Vector3(fx + 0.01f, 0.002f, -0.09f), new Vector3(fx + 0.11f, 0.86f, 0.09f), PdFigure);
        PdBox(new Vector3(fx - 0.2f, 0.86f, -0.11f), new Vector3(fx + 0.2f, 1.5f, 0.11f), PdFigure * 1.15f); // body
        PdBox(new Vector3(fx - 0.29f, 0.9f, -0.06f), new Vector3(fx - 0.21f, 1.46f, 0.06f), PdFigure); // arms
        PdBox(new Vector3(fx + 0.21f, 0.9f, -0.06f), new Vector3(fx + 0.29f, 1.46f, 0.06f), PdFigure);
        PdBox(new Vector3(fx - 0.11f, 1.54f, -0.11f), new Vector3(fx + 0.11f, 1.8f, 0.11f), PdFigure * 1.3f); // head: its top is 1.8 m
        if (Ui.DesignerFrame)
        {
            // A plain beam frame: two rails under the panel's sides, cross beams, four legs to the floor.
            float hx = MathF.Max(0.15f, size.X * 0.5f - 0.08f), hz = MathF.Max(0.15f, size.Z * 0.5f - 0.05f), top = deck - 0.012f;
            for (var s = -1; s <= 1; s += 2)
            {
                PdBox(new Vector3(s * hx - 0.05f, top - 0.1f, -hz), new Vector3(s * hx + 0.05f, top, hz), PdBeam);
                PdBox(new Vector3(-hx, top - 0.09f, s * hz - 0.04f), new Vector3(hx, top - 0.01f, s * hz + 0.04f), PdBeam * 0.9f);
                for (var e = -1; e <= 1; e += 2)
                    PdBox(new Vector3(s * hx - 0.04f, 0.002f, e * hz - 0.04f), new Vector3(s * hx + 0.04f, top - 0.1f, e * hz + 0.04f), PdBeam * 0.8f);
            }
        }
        else
            PdBox(new Vector3(-0.04f, 0.002f, -0.04f), new Vector3(0.04f, deck - 0.004f, 0.04f), PdBeam); // a stand under its middle
        // The panel: its drawn boxes (the facets), as a construct draws them.
        var place = Matrix4x4.CreateTranslation(lift);
        foreach (var b in d.Boxes)
            if (!b.Hidden)
                Gfx.Draw(b.Model(0, b.Center) * place, b.Color, 1f, mode: 0, shape: b.Shape);
        if (Ui.DesignerSockets)
            foreach (var s in d.Sockets)
            {
                var at = s.At + lift + ConstructParts.DirVector(s.Dir) * 0.02f;
                PdBox(at - new Vector3(0.022f), at + new Vector3(0.022f), PdSocket);
            }
        OpenGl.Native.glDisable(GlCapability.ScissorTest);
        Gfx.SetViewport(0, 0, fb.X, fb.Y);
    }
}
