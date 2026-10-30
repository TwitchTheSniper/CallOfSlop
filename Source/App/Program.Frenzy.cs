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
    private static int _frenzyKills, _frenzyHeads;
    private static bool _frenzyAlive;
    private static double _frenzyShot = double.NaN;
    private static int _frenzyBannerLevel;
    private static WeaponClass _frenzyClassSeen = WeaponClass.AssaultRifle;

    /// <summary>The HUD banner for a frenzy level (empty below 5).</summary>
    internal static string FrenzyBanner(int level) => level >= FrenzyMeter.MaxLevel ? "MAX FRENZY" : level >= 5 ? "FRENZY" : "";

    /// <summary>Once per frame, after the game update.</summary>
    private static void UpdateFrenzy(double now)
    {
        var layer = AudioSystem.Music.Frenzy;
        var preview = Ui.FrenzyPreviewLevel(now); // the Audio mixer's "Preview frenzy" (UiAudioMixer.cs), -1 when off
        layer.Preview = preview >= 0;
        layer.Enabled = _settings.FrenzyMusic || preview >= 0;
        layer.Volume = _settings.FrenzyVolume;
        var game = _session;
        if (game == null || game.Phase != MatchPhase.Playing || game.Local is not { } me)
        {
            _frenzy.Reset();
            _frenzyGame = null;
            layer.Target = Math.Max(preview, 0);
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
            _frenzyClassSeen = me.FrenzyClass;
            layer.Class = me.FrenzyClass;
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
        if (newKills > 0)
            layer.Class = zombies ? me.WeaponDef.Class : game.LocalKillClass; // the class character: the latest kill's weapon (predicted)
        if (me.FrenzyClass != _frenzyClassSeen)
        {
            _frenzyClassSeen = me.FrenzyClass; // the server's Player.FrenzyClass (same rule) wins once it arrives
            layer.Class = me.FrenzyClass;
        }
        _frenzyKills = kills;
        _frenzyHeads = heads;
        _frenzy.Update(now);
        layer.Target = preview >= 0 ? preview : alive ? _frenzy.Level : 0;
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
