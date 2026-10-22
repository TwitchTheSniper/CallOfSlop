using System.Numerics;

namespace CallOfSlop;

/// <summary>Prestige hats: the smoke-test line-up (Source/Render/HatRender.cs).</summary>
public static unsafe partial class Program
{
    private static int _smokeHatStep;
    private static double _smokeHatStart;
    private static readonly List<byte> _smokeHatBots = new();

    /// <summary>
    /// Smoke step on the first map (after the name tags): five bots lined up in front of you, each wearing one of the five
    /// prestige hats through the test-only <see cref="HatRender.TestOverride"/> (bots never wear hats in real play), then a
    /// screenshot (smoke_prestige_hats.bmp). Adds bots if the match has fewer than five.
    /// </summary>
    private static void SmokeHats(double now)
    {
        var t = now - _smokeHatStart;
        if (_smokeHatStep == 0)
        {
            _smokeHatStart = now;
            t = 0;
            if (_game.IsServer)
                for (var tries = 0; tries < 5 && _game.Players.Values.Count(p => p.Bot != null) < 5; tries++)
                    _game.AddBot(BotDifficulty.Easy, quiet: true);
            _smokeHatBots.Clear();
            _smokeHatBots.AddRange(_game.Players.Values.Where(p => p.Bot != null).Select(p => p.Id).Take(5));
            var hats = new Dictionary<byte, HatId>();
            for (var i = 0; i < _smokeHatBots.Count; i++)
                hats[_smokeHatBots[i]] = (HatId)(i + 1);
            HatRender.TestOverride = p => hats.GetValueOrDefault(p.Id); // test only: never set outside the smoke test / self-test
            _smokeHatStep = 1;
        }
        if (_smokeHatStep <= 2 && _game.IsServer && _game.Local is { Alive: true } me)
        {
            // Hold the line-up every frame (their AI would walk off), facing you.
            _game.LookPitch = 0;
            var fwd = World.FlatForward(_game.LookYaw);
            var right = World.FlatRight(_game.LookYaw);
            var free = World.RaycastWorld(me.Position + new Vector3(0, World.EyeHeight, 0), fwd, 12f);
            var ahead = Math.Clamp(free - 1.5f, 3f, 6f);
            for (var i = 0; i < _smokeHatBots.Count; i++)
                if (_game.Players.TryGetValue(_smokeHatBots[i], out var bot))
                    _game.SelfTestPlace(bot, me.Position + fwd * ahead + right * ((i - (_smokeHatBots.Count - 1) / 2f) * 1.25f), _game.LookYaw + MathF.PI);
        }
        switch (_smokeHatStep)
        {
            case 1 when t > 0.7:
                SmokeShot("prestige_hats");
                _smokeHatStep++;
                break;
            case 2 when t > 0.8:
                Console.WriteLine($"[smoke] prestige hats: {_smokeHatBots.Count} bots lined up wearing "
                    + string.Join(", ", _smokeHatBots.Select((id, i) => Cosmetics.Get((HatId)(i + 1))?.Name ?? "?"))
                    + $" (test override); catalog {(Cosmetics.LoadError ?? "loaded")}, tallest {Cosmetics.Hats.Max(h => h.Height):0.00} m");
                HatRender.TestOverride = null;
                _smokeStepStart += t; // give the usual steps their clock back
                _smokeHatStep++;
                break;
        }
    }
}
