using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Blood by health: the screen overlay for your own health (Source/UI/BloodOverlay.cs) and the smoke-test step.</summary>
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

    // ---- Smoke ---------------------------------------------------------------------------------------

    private static int _smokeBloodStep;
    private static double _smokeBloodStart;
    private static byte _smokeBloodBot = 255;

    /// <summary>
    /// Smoke step on the first map (after the ragdolls): your health set to 30% after a hit from the right, screenshot
    /// smoke_blood_overlay.bmp; then you're healed and a bot 3 m in front of you at 20% health with hits on its chest, head
    /// and right arm, screenshot smoke_blood_body.bmp; a [smoke] line with the overlay's GPU cost and the body blood's CPU.
    /// </summary>
    private static void SmokeBlood(double now)
    {
        if (!_game.IsServer || _game.Local is not { Alive: true } me)
        {
            _smokeBloodStep = 9;
            return;
        }
        var t = now - _smokeBloodStart;
        var fwd = World.FlatForward(_game.LookYaw);
        if (_smokeBloodStep == 0)
        {
            _smokeBloodStart = now;
            t = 0;
            if (!_game.Players.Values.Any(p => p.Bot != null && p.Alive))
                _game.AddBot(BotDifficulty.Easy, quiet: true);
            _smokeBloodBot = _game.Players.Values.FirstOrDefault(p => p.Bot != null && p.Alive)?.Id ?? 255;
            // You: 30% after a hit from the right (the overlay sees a health drop and the replicated direction).
            me.Health = (int)MathF.Round(World.MaxHealth * 0.3f);
            me.Blood = BloodMarks.AddHit(BloodMarks.Cleared(me.Blood), BloodPart.ArmR, 4);
            _smokeBloodStep = 1;
        }
        me.LastHurtTime = now; // no regeneration during the step
        _game.LookPitch = -0.1f;
        if (_game.Players.TryGetValue(_smokeBloodBot, out var bot))
        {
            var free = World.RaycastWorld(me.Position + new Vector3(0, World.EyeHeight, 0), fwd, 8f);
            _game.SelfTestPlace(bot, me.Position + fwd * Math.Clamp(free - 1f, 2f, 3f), _game.LookYaw + MathF.PI); // facing you
            bot.Health = (int)MathF.Round(World.MaxHealth * 0.2f);
            bot.Blood = BloodMarks.AddHit(BloodMarks.AddHit(BloodMarks.AddHit(0, BloodPart.Front, 0), BloodPart.Head, 0), BloodPart.ArmR, 4);
            bot.LastHurtTime = now;
            bot.ImmuneUntil = now + 0.5;
        }
        switch (_smokeBloodStep)
        {
            case 1 when t > 1.0:
                SmokeShot("blood_overlay");
                Console.WriteLine($"[smoke] blood overlay at 30% health: {BloodOverlayRenderer.LastInstances} instances in 1 draw call, GPU "
                    + (BloodOverlayRenderer.LastGpuMs >= 0 ? $"{BloodOverlayRenderer.LastGpuMs:0.000} ms" : "n/a (first window)") + $", {_bloodOverlay.HitsAdded} hit splatters so far");
                me.Health = World.MaxHealth; // healed: the overlay fades for the body shot
                _smokeBloodStep = 2;
                break;
            case 2 when t > 2.2:
                SmokeShot("blood_body");
                Console.WriteLine($"[smoke] blood body: bot at 20% health, {BodyBloodRender.Stats}; GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                if (bot != null)
                {
                    bot.Health = World.MaxHealth;
                    bot.Blood = BloodMarks.Cleared(bot.Blood);
                }
                me.Blood = BloodMarks.Cleared(me.Blood);
                _smokeStepStart += t; // give the usual steps their clock back
                _smokeBloodStep = 9;
                break;
        }
    }
}
