namespace CallOfSlop;

/// <summary>
/// The kill-frenzy music (Source/Audio/MusicFrenzy.cs) from the game's side: the local player's kills feed a
/// <see cref="FrenzyMeter"/>, whose level goes to the music thread's <see cref="FrenzyLayer"/>. Only in a live match,
/// only while alive (dying fades it out fast; the death cam and menus stay frenzy-free), Zombies included (zombie kills,
/// worth less, shorter hold; being downed counts as dying). Your own gunfire ducks it a little (not at the top levels).
/// Reaching level 5 shows a FRENZY banner, level 6 MAX FRENZY (the perk banner, so it looks like the rest of the HUD).
/// </summary>
public static unsafe partial class Program
{
    private static readonly FrenzyMeter _frenzy = new();
    private static Game? _frenzyGame;
    private static int _frenzyKills, _frenzyHeads, _frenzyLoggedLevel;
    private static bool _frenzyAlive;
    private static double _frenzyShot = double.NaN;
    private static int _frenzyBannerLevel;

    /// <summary>The HUD banner for a frenzy level (empty below 5).</summary>
    internal static string FrenzyBanner(int level) => level >= FrenzyMeter.MaxLevel ? "MAX FRENZY" : level >= 5 ? "FRENZY" : "";

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
        if (_frenzy.Level > _frenzyBannerLevel && alive && FrenzyBanner(_frenzy.Level) is { Length: > 0 } banner)
        {
            game.PerkBanner = banner;
            game.PerkBannerTime = now;
        }
        if (_frenzy.Level != _frenzyBannerLevel)
            _frenzyBannerLevel = _frenzy.Level;
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

    private static int _smokeFrenzyStep, _smokeFrenzyTop;
    private static double _smokeFrenzyAt;

    /// <summary>The scripted smoke spree: this many kills, this far apart (headshots), through the normal damage path.</summary>
    private const int SmokeFrenzyKills = 16;
    private const double SmokeFrenzyGap = 0.7;

    /// <summary>
    /// Smoke step on the first map (before the death cam): a scripted spree of <see cref="SmokeFrenzyKills"/> kills
    /// <see cref="SmokeFrenzyGap"/> s apart through the normal damage path (bots respawn, so some kills may be skipped),
    /// logging the frenzy level after each, then the top level the meter and the music reached (level 6 is expected);
    /// plus the levels the meter reaches for the same spree on paper.
    /// </summary>
    private static void SmokeFrenzy(double now)
    {
        if (_smokeFrenzyStep == 0)
        {
            _smokeFrenzyAt = now;
            _smokeFrenzyStep = 1;
            _smokeFrenzyTop = 0;
            Console.WriteLine($"[smoke] frenzy: scripted spree on paper ({SmokeFrenzyKills} headshot kills {SmokeFrenzyGap} s apart, then idle): {FrenzyBurstLine(SmokeFrenzyKills, SmokeFrenzyGap, true)}; music setting {(_settings.FrenzyMusic ? "on" : "off")}");
            return;
        }
        var t = now - _smokeFrenzyAt;
        _smokeFrenzyTop = Math.Max(_smokeFrenzyTop, AudioSystem.Music.Frenzy.Level);
        if (_smokeFrenzyStep <= SmokeFrenzyKills && t > 0.2 + SmokeFrenzyGap * (_smokeFrenzyStep - 1))
        {
            var line = _session?.SmokeKillNearestBot(now) ?? "no session";
            Console.WriteLine($"[smoke] frenzy: kill {_smokeFrenzyStep}: {line}; meter level {_frenzy.Level} (heat {_frenzy.Heat:0}), music level {AudioSystem.Music.Frenzy.Level}");
            _smokeFrenzyStep++;
        }
        else if (_smokeFrenzyStep == SmokeFrenzyKills + 1 && t > 0.2 + SmokeFrenzyGap * SmokeFrenzyKills + 3.0)
        {
            var layer = AudioSystem.Music.Frenzy;
            _smokeFrenzyTop = Math.Max(_smokeFrenzyTop, layer.Level);
            Console.WriteLine($"[smoke] frenzy: after the spree: meter level {_frenzy.Level} (heat {_frenzy.Heat:0}), music level {layer.Level} \"{layer.TrackName ?? "-"}\", "
                              + $"top music level {_smokeFrenzyTop} (want {FrenzyMeter.MaxLevel}), level-6 hits {layer.Hits}, banner \"{_session?.PerkBanner}\", "
                              + $"limiter min gain {AudioSystem.Music.FrenzyLimiterMinGain:0.00}, music mixing {AudioSystem.Music.RenderMsPerSecond:0.0} ms per s of audio");
            _smokeFrenzyStep++;
            _smokeStepStart += t; // the usual steps carry on where they were
        }
    }

    /// <summary>The meter on paper: <paramref name="kills"/> kills <paramref name="gap"/> s apart, then idle; the levels it passes and when.</summary>
    internal static string FrenzyBurstLine(int kills, double gap, bool headshots = false)
    {
        var m = new FrenzyMeter();
        var sb = new System.Text.StringBuilder();
        var last = 0;
        for (var i = 0; i < kills; i++)
        {
            m.OnKill(i * gap, headshots);
            sb.Append($"kill {i + 1} -> L{m.Level} ({m.Heat:0}); ");
            last = m.Level;
        }
        var end = (kills - 1) * gap;
        for (var t = end; t < end + 45; t += 0.1)
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
