using System.Numerics;

namespace CallOfSlop;

/// <summary>Name tags over players' heads (Source/UI/NameTags.cs decides, Ui.DrawNameTags draws) and their smoke-test step.</summary>
public static unsafe partial class Program
{
    private static readonly NameTags _nameTags = new();
    private static Game? _nameTagsGame;

    /// <summary>Called from Render with the frame's camera: stores it for HUD world labels and decides this frame's name tags.</summary>
    private static void UpdateNameTags(double now, Vector3 eye, Vector3 fwd, in Matrix4x4 view, in Matrix4x4 proj)
    {
        Hud.Camera = new HudCamera(view * proj, eye, fwd, true);
        if (!ReferenceEquals(_nameTagsGame, _session))
        {
            _nameTags.Reset(); // new session: no focus or cached line of sight carried over
            _nameTagsGame = _session;
        }
        var killer = NameTags.None;
        if (_game.Local is { Alive: false } && _deathCam.Active && _deathCam.HasTarget && _deathCam.Info is { } info)
            killer = info.KillerId; // the death cam turns to your killer: tag them
        _nameTags.Update(_game, eye, fwd, _settings.NameTags, now, killer);
    }

    /// <summary>Draws the name tags (ImGui pass, before the HUD windows so they sit under them).</summary>
    private static void DrawNameTags(Game g)
    {
        if (Ui.MenuOpen || _nameTags.Count == 0 || g.LocalFlashHidesHud(Brutal.GlfwApi.Glfw.Time))
            return; // (flashed: washed out, GameTactical.cs)
        var scoped = g.Local is { Alive: true } && g.AimT > 0.5f && Sights.Magnified(g.Active.Def);
        Ui.DrawNameTags(g, _nameTags, _settings.NameTagHealth, scoped);
    }

    // ---- Smoke test ------------------------------------------------------------------------------

    private static int _smokeNameTagStep;
    private static double _smokeNameTagStart;
    private static byte _smokeTagEnemy = NameTags.None;
    private static readonly byte[] _smokeTagMates = [NameTags.None, NameTags.None];

    /// <summary>
    /// Smoke step on the first map (after the kill streaks): one bot is put in front of your crosshair (with a clear view) and two
    /// more to the sides counted as teammates, then a screenshot (smoke_name_tags.bmp) shows both kinds of tag. Logs what was tagged.
    /// </summary>
    private static void SmokeNameTags(double now)
    {
        var t = now - _smokeNameTagStart;
        if (_smokeNameTagStep == 0)
        {
            _smokeNameTagStart = now;
            t = 0;
            _smokeTagEnemy = _smokeTagMates[0] = _smokeTagMates[1] = NameTags.None;
            NameTags.SmokeFriends = 0;
            if (_game.Local is { } me0)
                foreach (var p in _game.Players.Values)
                {
                    if (p.Bot == null)
                        continue;
                    if (_smokeTagEnemy == NameTags.None && _game.AreEnemies(me0, p))
                        _smokeTagEnemy = p.Id;
                    else if (_smokeTagMates[0] == NameTags.None)
                        _smokeTagMates[0] = p.Id;
                    else if (_smokeTagMates[1] == NameTags.None)
                        _smokeTagMates[1] = p.Id;
                }
            foreach (var id in _smokeTagMates)
                if (id < 64)
                    NameTags.SmokeFriends |= 1UL << id;
            _smokeNameTagStep = 1;
        }
        if (_smokeNameTagStep <= 2 && _game.IsServer && _game.Local is { Alive: true } me)
        {
            // Hold the bots in place every frame (their AI would walk off).
            _game.LookPitch = 0;
            var fwd = World.FlatForward(_game.LookYaw);
            var right = World.FlatRight(_game.LookYaw);
            var free = World.RaycastWorld(me.Position + new Vector3(0, World.EyeHeight, 0), fwd, 14f);
            var ahead = Math.Clamp(free - 1.5f, 3f, 12f);
            if (_game.Players.TryGetValue(_smokeTagEnemy, out var enemy))
                _game.SelfTestPlace(enemy, me.Position + fwd * ahead, _game.LookYaw + MathF.PI);
            for (var i = 0; i < _smokeTagMates.Length; i++)
                if (_game.Players.TryGetValue(_smokeTagMates[i], out var mate))
                    _game.SelfTestPlace(mate, me.Position + fwd * MathF.Min(ahead, 7f) + right * (i == 0 ? -2.5f : 2.5f), _game.LookYaw + MathF.PI);
        }
        switch (_smokeNameTagStep)
        {
            case 1 when t > 0.7:
                SmokeShot("name_tags");
                _smokeNameTagStep++;
                break;
            case 2 when t > 0.8:
                var enemyTagged = false;
                var mates = 0;
                for (var i = 0; i < _nameTags.Count; i++)
                {
                    if (_nameTags.Tags[i].Id == _smokeTagEnemy)
                        enemyTagged = true;
                    else if (_nameTags.Tags[i].Friendly)
                        mates++;
                }
                Console.WriteLine($"[smoke] name tags: {_nameTags.Count} shown (mode {_settings.NameTags}), aimed enemy {(_smokeTagEnemy == NameTags.None ? "none (no enemy bot)" : enemyTagged ? "tagged" : "NOT tagged")}, "
                    + $"teammates tagged {mates}, rays so far {_nameTags.RaysTotal}");
                NameTags.SmokeFriends = 0;
                _smokeStepStart += t; // give the usual steps their clock back
                _smokeNameTagStep++;
                break;
        }
    }
}
