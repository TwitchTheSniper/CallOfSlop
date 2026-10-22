using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// The per-weapon-class frenzy visuals (Source/Render/FrenzyFx) from the game's side: feeds <see cref="FrenzyFx"/> your
/// frenzy level (the same meter as the frenzy music, Program.Frenzy.cs), class, kills and weapon draws each frame, steps
/// its particles, and draws its two passes (world, then your gun). The screen edge effects and the HUD meter are in
/// UiFrenzy.cs. Plus a smoke step that shows four classes at level 6 and logs their cost.
/// </summary>
public static unsafe partial class Program
{
    private static readonly FrenzyFxRenderer _frenzyFx = new();
    private static int _frenzyFxKills;
    private static Game? _frenzyFxGame;
    private static double _frenzyFxEquip;

    /// <summary>Once per frame, after <see cref="UpdateFrenzy"/>.</summary>
    private static void UpdateFrenzyFx(double now)
    {
        FrenzyFx.Mode = _settings.FrenzyVisuals;
        FrenzyFx.MusicLevel = _settings.FrenzyMusic ? AudioSystem.Music.Frenzy.Level : _frenzy.Level;
        var game = _session;
        if (game == null || game.Phase != MatchPhase.Playing || game.Local is not { Alive: true })
        {
            FrenzyFx.LocalLevel = 0;
            if (game == null)
                FrenzyParticles.Clear();
            FrenzyFx.Update((float)FrameDt);
            return;
        }
        FrenzyFx.LocalLevel = _frenzy.Level;
        // The class: the weapon of your most recent kill once the server replicates it (frenzy-buffs); until then the gun in hand.
        FrenzyFx.LocalClass = FrenzyFx.LocalClassOf(game.Local, game.Active.Def.Class);
        if (_frenzyFxGame != game)
        {
            _frenzyFxGame = game;
            _frenzyFxKills = _frenzyKills;
            _frenzyFxEquip = game.EquipUntil;
        }
        if (_frenzyKills > _frenzyFxKills)
            FrenzyFx.OnLocalKill(now);
        _frenzyFxKills = _frenzyKills;
        if (game.EquipUntil != _frenzyFxEquip)
        {
            if (game.EquipUntil > now)
                FrenzyFx.OnEquip(now);
            _frenzyFxEquip = game.EquipUntil;
        }
        FrenzyFx.Update((float)FrameDt);
    }

    /// <summary>The viewmodel pass: your gun's frenzy glow (after the gun is drawn), faded out as you aim down the sights.</summary>
    private static void DrawFrenzyFxViewModel(double now)
    {
        var def = _game.Active.Def;
        if (Game.IsCart(def) && _game.MannedCart != null)
            return;
        var fb = _window.FramebufferSize;
        var vmProj = Sights.ViewModelProjection(fb.X / (float)fb.Y);
        FrenzyFx.ViewModelAim = _game.AimT;
        _frenzyFx.DrawViewModel(Gfx, def, _vmAnim.GunPose, vmProj, now);
    }

    // ---- Smoke ---------------------------------------------------------------------------------------

    private static readonly WeaponClass[] SmokeFrenzyFxClasses = [WeaponClass.Pistol, WeaponClass.SMG, WeaponClass.Sniper, WeaponClass.Special];
    private static int _smokeFrenzyFxStep;
    private static double _smokeFrenzyFxAt, _smokeFrenzyFxShot;

    /// <summary>Seconds per class in the smoke step.</summary>
    private const double SmokeFrenzyFxPhase = 1.6;

    /// <summary>
    /// Smoke step on the first map (after the frenzy music spree): four classes at frenzy level 6 (a smoke-only override
    /// on every player), everyone firing at 10 shots a second, a screenshot smoke_frenzy_CLASS.bmp of each and a line
    /// with the cost (streaks, draw calls, CPU and GPU ms per frame).
    /// </summary>
    private static void SmokeFrenzyFx(double now)
    {
        var game = _session;
        if (game == null)
        {
            _smokeFrenzyFxStep = SmokeFrenzyFxClasses.Length;
            return;
        }
        if (_smokeFrenzyFxAt == 0)
        {
            _smokeFrenzyFxAt = now;
            FrenzyParticles.Clear();
            _frenzyFx.ResetWindow(now);
        }
        var cls = SmokeFrenzyFxClasses[_smokeFrenzyFxStep];
        var t = now - _smokeFrenzyFxAt - _smokeFrenzyFxStep * SmokeFrenzyFxPhase;
        if (FrenzyFx.Override?.Class != cls)
        {
            FrenzyFx.Override = (FrenzyMeter.MaxLevel, cls);
            FrenzyFx.OnLocalKill(now); // the sniper pulse and the pistol glint show too
            FrenzyFx.OnEquip(now);
        }
        if (now - _smokeFrenzyFxShot >= 0.1)
        {
            _smokeFrenzyFxShot = now;
            game.SmokeFrenzyShots(now);
        }
        if (t >= SmokeFrenzyFxPhase - 0.35 && t - FrameDt < SmokeFrenzyFxPhase - 0.35)
        {
            FrenzyFx.KillAt = now - FrenzyFx.KillPulseSeconds * 0.5; // the pulse at its peak in the picture
            SmokeShot("frenzy_" + cls.ToString().ToLowerInvariant());
        }
        if (t >= SmokeFrenzyFxPhase)
        {
            var w = _frenzyFx.Window;
            Console.WriteLine($"[smoke] frenzy visuals: {FrenzyLooks.Get(cls).Name} level 6, {game.Players.Count} players firing: {w.AvgWorld:0} world + {w.AvgView:0} viewmodel streaks/frame "
                + $"in {w.AvgDraws:0.0} draw calls, {FrenzyParticles.Count} particles live, CPU {w.AvgCpuMs:0.000} ms, GPU {(w.AvgGpuMs >= 0 ? $"{w.AvgGpuMs:0.000} ms" : "n/a")} per frame "
                + $"(renderer {(_frenzyFx.Ready ? "on" : "off")}, looks {FrenzyLooks.Source})");
            _frenzyFx.ResetWindow(now);
            _smokeFrenzyFxStep++;
            if (_smokeFrenzyFxStep >= SmokeFrenzyFxClasses.Length)
            {
                FrenzyFx.Override = null;
                FrenzyParticles.Clear();
                _smokeStepStart += now - _smokeFrenzyFxAt; // the usual steps carry on where they were
            }
        }
    }
}
