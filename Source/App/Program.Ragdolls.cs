using System.Numerics;
using Brutal.OpenGlApi;

namespace CallOfSlop;

/// <summary>Ragdolls on death and blast knockback (Source/Render/Ragdolls.cs, Source/Game/Knockback.cs): per frame, and the smoke-test step.</summary>
public static unsafe partial class Program
{
    /// <summary>Every rendered frame, before the world is drawn: new bodies for the newly dead, the 60 Hz steps.</summary>
    private static void UpdateRagdolls(double now)
    {
        (Ragdolls.ZombieShirt, Ragdolls.ZombieSkin, Ragdolls.ZombiePants) = (ZShirt, ZSkin, ZPants);
        Gibs.Mode = _settings.Gore; // Settings > Video "Gore" (Source/Props/Gibs.cs)
        Ragdolls.Frame(_game, now, (float)FrameDt);
    }

    /// <summary>After the world: the bodies box by box when the instanced prop renderer couldn't draw them.</summary>
    private static void DrawRagdollsFallback()
    {
        if (!PropGfx.Instanced && Ragdolls.HasInstances)
            Ragdolls.DrawImmediate(Gfx);
    }

    private static int _smokeRagdollStep;
    private static double _smokeRagdollStart;
    private static readonly List<byte> _smokeRagdollBots = new();
    private static Vector3 _smokeKnockFrom;

    /// <summary>
    /// Smoke step on the first map (before the death cam): five bots in a line in front of you, wearing hats (test override),
    /// killed five ways (body shot, headshot, melee, two frags), then a blast among the bodies flings the pile; a screenshot
    /// (smoke_ragdolls.bmp) once they've settled, and a grenade-strength push on one living bot (smoke_knockback log line:
    /// how far it moved). Adds bots if the match has fewer than six.
    /// </summary>
    private static void SmokeRagdolls(double now)
    {
        var t = now - _smokeRagdollStart;
        if (!_game.IsServer || _game.Local is not { Alive: true } me)
        {
            _smokeRagdollStep = 9;
            return;
        }
        var fwd = World.FlatForward(_game.LookYaw);
        var right = World.FlatRight(_game.LookYaw);
        if (_smokeRagdollStep == 0)
        {
            _smokeRagdollStart = now;
            t = 0;
            for (var tries = 0; tries < 6 && _game.Players.Values.Count(p => p.Bot != null) < 6; tries++)
                _game.AddBot(BotDifficulty.Easy, quiet: true);
            _smokeRagdollBots.Clear();
            _smokeRagdollBots.AddRange(_game.Players.Values.Where(p => p.Bot != null).Select(p => p.Id).Take(6));
            var hats = new Dictionary<byte, HatId>();
            for (var i = 0; i < _smokeRagdollBots.Count; i++)
                hats[_smokeRagdollBots[i]] = (HatId)(i % 5 + 1);
            HatRender.TestOverride = p => hats.GetValueOrDefault(p.Id); // test only
            _smokeRagdollStep = 1;
        }
        var free = World.RaycastWorld(me.Position + new Vector3(0, World.EyeHeight, 0), fwd, 14f);
        var ahead = Math.Clamp(free - 2f, 3f, 7f);
        if (_smokeRagdollStep == 1)
        {
            _game.LookPitch = -0.15f;
            for (var i = 0; i < _smokeRagdollBots.Count; i++)
                if (_game.Players.TryGetValue(_smokeRagdollBots[i], out var bot) && bot.Alive)
                    _game.SelfTestPlace(bot, me.Position + fwd * ahead + right * ((i - 2.5f) * 1.1f), _game.LookYaw + MathF.PI);
        }
        switch (_smokeRagdollStep)
        {
            case 1 when t > 0.6:
            {
                byte[] ids = [me.Primary, me.Primary, Game.MeleeKillId, (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag), (byte)(Throwables.KillIdBase + (byte)ThrowableKind.Frag)];
                var killed = 0;
                for (var i = 0; i < Math.Min(5, _smokeRagdollBots.Count); i++)
                    if (_game.Players.TryGetValue(_smokeRagdollBots[i], out var bot) && bot.Alive && _game.DebugKill(me, bot, ids[i], head: i == 1, now))
                        killed++;
                Console.WriteLine($"[smoke] ragdolls: killed {killed} bots (body shot, headshot, melee, 2 frags)");
                _smokeRagdollStep = 2;
                break;
            }
            case 2 when t > 1.5:
                // A blast among the bodies: the pile is flung (cosmetic), and the living sixth bot is pushed (server).
                var center = me.Position + fwd * (ahead - 0.6f) + new Vector3(0, 0.3f, 0);
                Ragdolls.Blast(center, 6f, Throwables.Get(ThrowableKind.Frag).Knockback, now);
                if (_smokeRagdollBots.Count >= 6 && _game.Players.TryGetValue(_smokeRagdollBots[5], out var pushed) && pushed.Alive)
                {
                    _smokeKnockFrom = pushed.Position;
                    _game.SelfTestBlastKnockback(null, pushed.Chest - right * 2.5f, 9f, Throwables.Get(ThrowableKind.Frag).Knockback);
                }
                _smokeRagdollStep = 3;
                break;
            case 3 when t > 2.1:
                if (_smokeRagdollBots.Count >= 6 && _game.Players.TryGetValue(_smokeRagdollBots[5], out var moved))
                    Console.WriteLine($"[smoke] knockback: a frag 2.5 m away moved a bot {Vector3.Distance(_smokeKnockFrom, moved.Position):0.00} m in 0.6 s ({_game.KnocksApplied} pushes so far, last {_game.LastKnock.Length():0.0} m/s)");
                _smokeRagdollStep = 4;
                break;
            case 4 when t > 3.6:
                SmokeShot("ragdolls");
                Console.WriteLine($"[smoke] ragdolls: {Ragdolls.Stats}; GL error 0x{(uint)OpenGl.Native.glGetError():X}");
                HatRender.TestOverride = null;
                _smokeStepStart += t; // give the usual steps their clock back
                _smokeRagdollStep = 9;
                break;
        }
    }
}
