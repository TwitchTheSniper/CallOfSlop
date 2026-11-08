using System.Numerics;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace CallOfSlop;

/// <summary>
/// The survival opening on this machine (docs/story/OPENING.md; the server's side is Game/GameSandboxOpening.cs): the
/// camera of someone coming down, lying at the crash site and waking in the colony's cell block, the black, the blinks
/// and the words over the screen, and the one key that works meanwhile (jump: skip). The beat comes from the server.
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
            case OpeningStage.Crash:
            {
                // Coming down: the view tumbles and shakes harder until the black.
                var k = Math.Clamp(t / length, 0, 1);
                var spin = t * (1.5f + 2.5f * k);
                var side = Vector3.Normalize(Vector3.Cross(u, MathF.Abs(u.X) < 0.9f ? Vector3.UnitX : Vector3.UnitZ));
                var ahead = Vector3.Cross(side, u);
                fwd = Vector3.Normalize(ahead * MathF.Cos(spin) + side * MathF.Sin(spin) - u * (0.5f + 0.4f * MathF.Sin(t * 1.7f)));
                var jitter = (0.04f + 0.18f * k) * new Vector3(MathF.Sin(t * 53f), MathF.Sin(t * 61f + 1), MathF.Sin(t * 47f + 2));
                eye = local.RenderEye + jitter;
                up = Vector3.Transform(Upright(fwd, u), Quaternion.CreateFromAxisAngle(fwd, MathF.Sin(t * 2.3f) * (0.4f + 0.8f * k)));
                return true;
            }
            case OpeningStage.ComeTo:
            case OpeningStage.Out:
            case OpeningStage.Under:
            {
                // On their back at the crash site, looking up at whoever leans over them, the view swaying.
                eye = feet + u * OpeningLieEye;
                var to = focus - eye;
                var toF = to.LengthSquared() > 0.01f ? Vector3.Normalize(to) : u;
                fwd = Vector3.Normalize(Vector3.Lerp(u, toF, 0.95f + 0.05f * MathF.Sin(t * 0.7f)));
                up = Vector3.Transform(Upright(fwd, u), Quaternion.CreateFromAxisAngle(fwd, 0.22f * MathF.Sin(t * 0.9f)));
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

    /// <summary>The opening over the HUD (in the ImGui pass): the black and the blinks, the edges, the caption, the last line said, the skip key.</summary>
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
        var red = 0f;
        switch (stage)
        {
            case OpeningStage.Crash:
            {
                // Flashes of fire as it comes down, then black for the last second.
                var flicker = 0.5f + 0.5f * MathF.Sin(t * 37f) * MathF.Sin(t * 13f + 1);
                red = 0.25f + 0.35f * flicker;
                black = Math.Clamp((t - (length - 1f)) / 0.8f, 0, 1);
                vignette = 0.6f;
                if (t > length - 1.1f && t < length - 0.95f)
                    dl.AddRectFilled(new float2(0, 0), screen, new ImColor8(255, 240, 220, 255)); // (the impact)
                break;
            }
            case OpeningStage.ComeTo:
            {
                // Eyes open slowly, blink twice, and close again.
                black = 1 - Math.Clamp(t / 1.2f, 0, 1) * 0.75f;
                black = MathF.Max(black, Blink(t, 3.0f, 0.35f));
                black = MathF.Max(black, Blink(t, 5.2f, 0.45f));
                black = MathF.Max(black, Math.Clamp((t - (length - 1.3f)) / 1.2f, 0, 1));
                vignette = 1f;
                red = 0.12f;
                break;
            }
            case OpeningStage.Wake:
                black = 1 - Math.Clamp(t / 2.2f, 0, 1);
                vignette = 0.8f * (1 - Math.Clamp(t / length, 0, 1));
                break;
            default:
                black = 1;
                break;
        }
        if (red > 0.01f)
            dl.AddRectFilled(new float2(0, 0), screen, new ImColor8(150, 30, 10, (byte)(red * 120)));
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

    /// <summary>How shut the eyes are in a blink at <paramref name="at"/> s lasting <paramref name="length"/> s (0 open, 1 shut).</summary>
    private static float Blink(float t, float at, float length)
    {
        var x = (t - at) / length;
        return x is <= 0 or >= 1 ? 0 : MathF.Sin(x * MathF.PI);
    }
}
