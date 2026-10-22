namespace CallOfSlop;

/// <summary>The tactical equipment's smoke-test step (TacticalGear.cs, GameTactical.cs, TacticalRender.cs, TacticalHud.cs).</summary>
public static unsafe partial class Program
{
    private const int SmokeTacSteps = 5;
    private static int _smokeTacStep;
    private static double _smokeTacStart;

    /// <summary>
    /// Smoke step on the first map (after the frenzy step, before the death cam): one of each placed device is laid out
    /// in front of you and screenshotted (smoke_tactical_devices.bmp), then you're flashed (strength 0.7, the overlay held
    /// at its peak) and screenshotted again (smoke_tactical_flash.bmp). Logs what went down and the overlay's opacity.
    /// </summary>
    private static void SmokeTactical(double now)
    {
        var game = _session;
        if (game is not { IsServer: true, Local: { Alive: true } me })
        {
            Console.WriteLine("[smoke] tactical gear: skipped (not hosting, or not alive)");
            _smokeTacStep = SmokeTacSteps;
            return;
        }
        var t = now - _smokeTacStart;
        switch (_smokeTacStep)
        {
            case 0:
                _smokeTacStart = now;
                _game.LookPitch = -0.2f;
                var placed = game.SmokePlaceTactical(me, _game.LookYaw, now);
                Console.WriteLine($"[smoke] tactical gear: {placed} devices placed ({string.Join(", ", game.TacDevices.Select(d => Throwables.Get(d.Kind).Name))})");
                _smokeTacStep++;
                break;
            case 1 when t > 0.8:
                _game.LookPitch = -0.2f;
                SmokeShot("tactical_devices");
                _smokeTacStep++;
                break;
            case 2 when t > 1.1:
                game.SelfTestLocalAfflict(ThrowableKind.Flash, 0.7f, now);
                _smokeTacStep++;
                break;
            case 3 when t > 1.35:
                Console.WriteLine($"[smoke] tactical gear: flash overlay {game.LocalFlashAlpha(now):0.00} opacity {t - 1.1:0.00} s in (hold {TacticalGear.FlashHold(0.7f):0.00} s, "
                    + $"total {TacticalGear.FlashTotal(0.7f):0.00} s), deafen {AudioSystem.Deafen:0.00}");
                SmokeShot("tactical_flash");
                _smokeTacStep++;
                break;
            case 4 when t > 1.6:
                game.SelfTestClearTactical();
                _smokeStepStart += t; // give the usual steps their clock back
                _smokeTacStep++;
                break;
        }
    }
}
