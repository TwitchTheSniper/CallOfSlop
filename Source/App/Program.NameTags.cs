using System.Numerics;

namespace CallOfSlop;

/// <summary>Name tags over players' heads (Source/UI/NameTags.cs decides, Ui.DrawNameTags draws).</summary>
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
}
