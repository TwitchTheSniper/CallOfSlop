using System.Numerics;

namespace CallOfSlop;

/// <summary>
/// Sandbox: the Shell Gantry's panel in the match (UiShellGantry.cs; ConstructGantry.cs). The use key on the gantry opens it; it holds the
/// mouse while open and closes when the gantry goes, the player dies or walks off, on Esc and on a local reset.
/// </summary>
public static partial class Program
{
    private const float ShellGantryPanelReach = 8f; // (ConstructConfig.Reach: the server's own reach for the act)

    /// <summary>Is the crosshair's part a Shell Gantry whose panel this eye may open? (the use key's branch)</summary>
    private static bool ShellGantryUse(Construct c, int part, float distance) =>
        part >= 0 && part < c.PartCount && c.DefOf(part).Gantry != null && distance <= ShellGantryPanelReach;

    private static void OpenShellGantryPanel(Construct c, int part)
    {
        if (MenusOpen)
            return;
        Ui.OpenShellGantry(c.Id, part);
        SetMouseCaptured(false);
    }

    /// <summary>The panel over the match (the Sandbox ImGui pass); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawShellGantryInMatch(double now)
    {
        if (!Ui.ShellGantryOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var (body, part) = Ui.ShellGantryTarget;
        var c = g?.Constructs.Get(body);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || c == null || _mouseCaptured
            || part < 0 || part >= c.PartCount || c.DefOf(part).Gantry == null
            || Vector3.Distance(me!.Position, ConstructEdits.ReachPoint(c, part, me.Position)) > ShellGantryPanelReach + 4;
        if (!gone && Ui.DrawShellGantry(g!, c!, part, now))
            return;
        Ui.CloseShellGantry();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also fire the tool)
    }

    private static string _shellGantryLook = "";
    private static double _shellGantryLookAt;
    private static long _shellGantryLookKey;

    /// <summary>The gantry's look line, made again at most four times a second (it walks the bodies for the bay's shell).</summary>
    private static string ShellGantryLook(Game g, Construct c, int part)
    {
        var now = ConstructConveyor.Clock;
        var key = ((long)c.Id << 20) ^ part ^ ((long)g.Constructs.GantryJobCount << 40);
        if (key != _shellGantryLookKey || now - _shellGantryLookAt > 0.25)
        {
            (_shellGantryLookKey, _shellGantryLookAt) = (key, now);
            _shellGantryLook = ConstructGantry.Look(g.Constructs, c, part);
        }
        return _shellGantryLook;
    }
}
