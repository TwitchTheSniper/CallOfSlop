namespace CallOfSlop;

/// <summary>
/// The kill-frenzy music (Source/Audio/MusicFrenzy.cs) from the game's side: the local player's kills feed a
/// <see cref="FrenzyMeter"/>, whose level goes to the music thread's <see cref="FrenzyLayer"/>. Only in a live match,
/// only while alive (dying fades it out fast; the death cam and menus stay frenzy-free), Zombies included (zombie kills,
/// worth less, shorter hold; being downed counts as dying). Your own gunfire ducks it a little.
/// </summary>
public static unsafe partial class Program
{
    private static readonly FrenzyMeter _frenzy = new();
    private static Game? _frenzyGame;
    private static int _frenzyKills, _frenzyHeads, _frenzyLoggedLevel;
    private static bool _frenzyAlive;
    private static double _frenzyShot = double.NaN;

    /// <summary>Once per frame, after the game update.</summary>
    private static void UpdateFrenzy(double now)
    {
        var layer = AudioSystem.Music.Frenzy;
        layer.Enabled = _settings.FrenzyMusic;
        layer.Volume = _settings.FrenzyVolume;
        var game = _session;
        if (game == null || game.Phase != MatchPhase.Playing || game.Local is not { } me)
        {
            _frenzy.Reset();
            _frenzyGame = null;
            layer.Target = 0;
            return;
        }
        var zombies = game.Mode == GameMode.Zombies;
        var zp = zombies && game.ZPlayers.TryGetValue(me.Id, out var z) ? z : null;
        var kills = zombies ? zp?.Kills ?? 0 : game.LocalKillCount;
        var heads = zombies ? zp?.Headshots ?? 0 : game.LocalHeadshotCount;
        var alive = me.Alive && !(zombies && game.IsDowned(me));
        if (_frenzyGame != game)
        {
            // A new game or match: start counting from here.
            _frenzyGame = game;
            _frenzy.Reset();
            _frenzyKills = kills;
            _frenzyHeads = heads;
            _frenzyAlive = alive;
            _frenzyShot = game.LastShotTime;
        }
        _frenzy.Zombies = zombies;
        if (_frenzyAlive && !alive)
        {
            _frenzy.OnDeath(now);
            layer.Died();
        }
        _frenzyAlive = alive;
        var newKills = kills - _frenzyKills;
        var newHeads = heads - _frenzyHeads;
        if (newKills < 0 || newKills > 50)
            newKills = 0; // a counter reset (new zombies run): resync
        for (var i = 0; i < newKills; i++)
            _frenzy.OnKill(now, i < newHeads);
        _frenzyKills = kills;
        _frenzyHeads = heads;
        _frenzy.Update(now);
        layer.Target = alive ? _frenzy.Level : 0;
        if (game.LastShotTime != _frenzyShot)
        {
            _frenzyShot = game.LastShotTime;
            layer.Shot();
        }
        if (_smoke && _frenzy.Level != _frenzyLoggedLevel)
        {
            Console.WriteLine($"[smoke] frenzy: meter level {_frenzy.Level} (heat {_frenzy.Heat:0}), music level {layer.Level} \"{layer.TrackName ?? "-"}\"");
            _frenzyLoggedLevel = _frenzy.Level;
        }
    }

    private static int _smokeFrenzyStep;
    private static double _smokeFrenzyAt;

    /// <summary>
    /// Smoke step on the first map (before the death cam): a scripted burst of five kills 0.8 s apart through the normal
    /// damage path, logging the frenzy level after each, then the level the music reached; plus the levels the meter
    /// reaches for the same burst on paper.
    /// </summary>
    private static void SmokeFrenzy(double now)
    {
        if (_smokeFrenzyStep == 0)
        {
            _smokeFrenzyAt = now;
            _smokeFrenzyStep = 1;
            Console.WriteLine($"[smoke] frenzy: scripted burst on paper (5 kills 0.8 s apart, then 25 s idle): {FrenzyBurstLine(5, 0.8)}; music setting {(_settings.FrenzyMusic ? "on" : "off")}");
            return;
        }
        var t = now - _smokeFrenzyAt;
        if (_smokeFrenzyStep <= 5 && t > 0.2 + 0.8 * (_smokeFrenzyStep - 1))
        {
            var line = _session?.SmokeKillNearestBot(now) ?? "no session";
            Console.WriteLine($"[smoke] frenzy: kill {_smokeFrenzyStep}: {line}; meter level {_frenzy.Level} (heat {_frenzy.Heat:0})");
            _smokeFrenzyStep++;
        }
        else if (_smokeFrenzyStep == 6 && t > 6.5)
        {
            var layer = AudioSystem.Music.Frenzy;
            Console.WriteLine($"[smoke] frenzy: after the burst: meter level {_frenzy.Level} (heat {_frenzy.Heat:0}), music level {layer.Level} \"{layer.TrackName ?? "-"}\", "
                              + $"music mixing {AudioSystem.Music.RenderMsPerSecond:0.0} ms per s of audio");
            _smokeFrenzyStep++;
            _smokeStepStart += t; // the usual steps carry on where they were
        }
    }

    /// <summary>The meter on paper: <paramref name="kills"/> kills <paramref name="gap"/> s apart, then idle; the levels it passes and when.</summary>
    internal static string FrenzyBurstLine(int kills, double gap)
    {
        var m = new FrenzyMeter();
        var sb = new System.Text.StringBuilder();
        var last = 0;
        for (var i = 0; i < kills; i++)
        {
            m.OnKill(i * gap, false);
            sb.Append($"kill {i + 1} -> L{m.Level} ({m.Heat:0}); ");
            last = m.Level;
        }
        var end = (kills - 1) * gap;
        for (var t = end; t < end + 25; t += 0.1)
        {
            m.Update(t);
            if (m.Level != last)
            {
                sb.Append($"L{m.Level} at +{t - end:0.0} s; ");
                last = m.Level;
            }
        }
        return sb.ToString().TrimEnd(' ', ';');
    }
}
