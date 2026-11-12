using System.Numerics;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The survival opening on this machine (docs/story/OPENING.md; the server's side is Game/GameSandboxOpening.cs): the
/// placeholder card for the crash landing, the camera of someone waking in the colony's cell block, the black and the
/// words over the screen, and the one key that works meanwhile (jump: skip). The beat comes from the server.
/// </summary>
public static partial class Program
{
    private const float OpeningLieEye = 0.22f; // m: the eye of someone lying on their back

    /// <summary>One frame of input in the opening: nothing moves or acts; the jump key skips. False: not in it.</summary>
    private static bool OpeningInput(double now)
    {
        if (!_game.OpeningActive)
            return false;
        if (_mouseCaptured && !Ui.ChatOpen && Pressed(GameAction.Jump) && _game.OpeningNow - _game.OpeningLocalFrom > 0.3)
        {
            _game.OpeningSkip();
            _holdJumpUntilRelease = true; // (the same press doesn't jump when they're up)
        }
        _crouchToggle = _proneToggle = false;
        _game.HeldButtons = Buttons.None;
        _game.AimHeld = false;
        _game.UpdateTrigger(now, false, false);
        return true;
    }

    /// <summary>The opening's camera: true when it has the view this frame.</summary>
    private static bool OpeningCamera(Player local, double now, ref Vector3 eye, ref Vector3 fwd, ref Vector3 up)
    {
        var stage = _game.OpeningLocal;
        if (stage == OpeningStage.None || !local.Alive)
            return false;
        var t = (float)(_game.OpeningNow - _game.OpeningLocalFrom);
        var length = MathF.Max(0.1f, _game.OpeningLocalTime);
        var feet = local.RenderPosition;
        var u = GravityFrames.UpAt(feet);
        var focus = _game.OpeningLocalFocus;
        switch (stage)
        {
            case OpeningStage.Out:
            {
                // The placeholder card: on their back in the cell block, under the black.
                eye = feet + u * OpeningLieEye;
                fwd = u;
                up = Upright(fwd, u); // (square to the up: its fallback)
                return true;
            }
            case OpeningStage.Wake:
            {
                // In the cell block, on their back, then up on their feet over the last second.
                var lying = feet + u * OpeningLieEye;
                var to = focus - lying;
                var toF = to.LengthSquared() > 0.01f ? Vector3.Normalize(to) : u;
                var lieFwd = Vector3.Normalize(Vector3.Lerp(u, toF, 0.55f));
                var k = Math.Clamp((t - (length - 1.2f)) / 1.2f, 0, 1);
                k = k * k * (3 - 2 * k);
                var standFwd = fwd;
                eye = Vector3.Lerp(lying, eye, k);
                fwd = Vector3.Normalize(Vector3.Lerp(lieFwd, standFwd, k));
                if (fwd.LengthSquared() < 0.5f)
                    fwd = standFwd;
                up = Upright(fwd, u);
                return true;
            }
        }
        return false;
    }

    /// <summary>The camera's up for a look along <paramref name="fwd"/>: the world's up made square to it.</summary>
    private static Vector3 Upright(Vector3 fwd, Vector3 u)
    {
        var up = u - fwd * Vector3.Dot(u, fwd);
        return up.LengthSquared() > 1e-4f ? Vector3.Normalize(up) : Vector3.Normalize(Vector3.Cross(fwd, MathF.Abs(fwd.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
    }

    /// <summary>The opening over the HUD (in the ImGui pass): the black, the edges, the caption, the last line said, the skip key.</summary>
    private static void DrawOpeningUi(Game g, double now)
    {
        var stage = g.OpeningLocal;
        if (stage == OpeningStage.None || g.Local is not { Alive: true })
            return;
        var t = (float)(g.OpeningNow - g.OpeningLocalFrom);
        var length = MathF.Max(0.1f, g.OpeningLocalTime);
        var screen = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetForegroundDrawList();
        float black;
        var vignette = 0f;
        switch (stage)
        {
            case OpeningStage.Wake:
                black = 1 - Math.Clamp(t / 2.2f, 0, 1);
                vignette = 0.8f * (1 - Math.Clamp(t / length, 0, 1));
                break;
            default:
                black = 1;
                break;
        }
        if (vignette > 0.01f)
        {
            // Dark edges closing in: a band down each side and across the top and bottom, fading inwards.
            var dark = new ImColor8(0, 0, 0, (byte)(vignette * 235));
            var clear = new ImColor8(0, 0, 0, 0);
            var bx = screen.X * 0.28f;
            var by = screen.Y * 0.3f;
            dl.AddRectFilledMultiColor(new float2(0, 0), new float2(bx, screen.Y), dark, clear, clear, dark);
            dl.AddRectFilledMultiColor(new float2(screen.X - bx, 0), screen, clear, dark, dark, clear);
            dl.AddRectFilledMultiColor(new float2(0, 0), new float2(screen.X, by), dark, dark, clear, clear);
            dl.AddRectFilledMultiColor(new float2(0, screen.Y - by), screen, clear, clear, dark, dark);
        }
        if (black > 0.004f)
            dl.AddRectFilled(new float2(0, 0), screen, new ImColor8(0, 0, 0, (byte)(Math.Clamp(black, 0, 1) * 255)));

        var font = ImGui.GetFont();
        var big = 26f * Ui.Scale;
        var small = 18f * Ui.Scale;
        if (g.OpeningCaption.Length > 0)
        {
            var a = (byte)(Math.Clamp(MathF.Min(t / 0.6f, (length - t) / 0.5f), 0, 1) * 230);
            var size = ImGui.CalcTextSize(g.OpeningCaption) * (big / ImGui.GetFontSize());
            var at = new float2((screen.X - size.X) * 0.5f, screen.Y * 0.42f);
            dl.AddText(font, big, at, new ImColor8(220, 214, 200, a), g.OpeningCaption);
            if (stage == OpeningStage.Out)
            {
                // (a placeholder until the crash-landing scene is made again: Karl, 2026-11-12)
                const string tag = "[ placeholder: crash landing ]";
                var ts = ImGui.CalcTextSize(tag) * (small / ImGui.GetFontSize());
                dl.AddText(font, small, new float2((screen.X - ts.X) * 0.5f, screen.Y * 0.42f - ts.Y - 14 * Ui.Scale), new ImColor8(150, 150, 150, a), tag);
            }
        }
        if (g.OpeningSubtitle.Length > 0 && g.OpeningNow - g.OpeningSubtitleAt < 3.2)
        {
            var size = ImGui.CalcTextSize(g.OpeningSubtitle) * (small / ImGui.GetFontSize());
            var at = new float2((screen.X - size.X) * 0.5f, screen.Y * 0.78f);
            dl.AddText(font, small, at + new float2(1.5f, 1.5f), new ImColor8(0, 0, 0, 200), g.OpeningSubtitle);
            dl.AddText(font, small, at, new ImColor8(240, 236, 220, 255), g.OpeningSubtitle);
        }
        var hint = $"{SandboxKeys.JumpKey}: {Game.OpeningSkipHint}";
        var hs = ImGui.CalcTextSize(hint);
        dl.AddText(new float2(screen.X - hs.X - 18 * Ui.Scale, screen.Y - hs.Y - 14 * Ui.Scale), new ImColor8(200, 200, 200, 150), hint);
    }
}
