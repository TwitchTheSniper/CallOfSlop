
namespace CallOfSlop;

/// <summary>
/// The per-weapon-class frenzy visuals (Source/Render/FrenzyFx) from the game's side: feeds <see cref="FrenzyFx"/> your
/// frenzy level (the same meter as the frenzy music, Program.Frenzy.cs), class, kills and weapon draws each frame, steps
/// its particles, and draws its two passes (world, then your gun). The screen edge effects and the HUD meter are in
/// UiFrenzy.cs.
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
        // The class: the weapon of your most recent kill (server-replicated Player.FrenzyClass) while in frenzy, else the gun in hand.
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
}
