namespace CallOfSlop;

/// <summary>
/// Sandbox: a linked logistics part's panel in the match (UiLinked.cs; docs/sandbox/design/linked-logistics.md). The use key on a link
/// end, a Cargo Dock, a Cargo Port, a Route Computer or a Logistics Dispatcher opens it (the command view's ROUTE opens a ship's Route
/// Computer's from afar). It holds the mouse while open and closes when its part goes, the player dies or walks off, on Esc and on a
/// local reset.
/// </summary>
public static partial class Program
{
    /// <summary>Is the crosshair's part a linked logistics part whose panel this eye may open? (the use key's branch)</summary>
    private static bool LinkedUse(Construct c, int part, float distance) =>
        part >= 0 && part < c.PartCount && c.DefOf(part).LinkKind != LinkKind.None && distance <= ConstructBuild.UseReach + 1.4f;

    private static void OpenLinkedPanel(Construct c, int part, bool remote = false)
    {
        if (MenusOpen && !remote)
            return;
        Ui.OpenLinked(c.Id, part, remote);
        SetMouseCaptured(false);
    }

    /// <summary>The panel over the match (the Sandbox ImGui pass); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawLinkedInMatch(double now)
    {
        if (!Ui.LinkedOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var (body, part) = Ui.LinkedTarget;
        var c = g?.Constructs.Get(body);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || c == null || _mouseCaptured && !Ui.CommandOpen
            || part < 0 || part >= c.PartCount || c.DefOf(part).LinkKind == LinkKind.None
            || !Ui.LinkedRemote && System.Numerics.Vector3.Distance(me!.Position, c.WorldCom) > c.Radius + ConstructConfig.Reach + 4;
        if (!gone && Ui.DrawLinked(g!, c!, part, now))
            return;
        Ui.CloseLinked();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also fire the tool)
    }
}
