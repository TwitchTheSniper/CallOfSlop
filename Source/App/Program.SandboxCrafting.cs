namespace CallOfSlop;

/// <summary>
/// Sandbox: the Crafting Terminal's panel in the match (UiCrafting.cs; docs/sandbox/constructs/production.md "Crafting terminal").
/// The use key on the terminal (not on a stack in its tray, which takes the part) opens it. It holds the mouse while open and closes
/// when its terminal goes, the player dies or walks off, on Esc and on a local reset.
/// </summary>
public static partial class Program
{
    /// <summary>The use key on a Crafting Terminal's desk: opens the panel (nothing is sent until Order). The press is spent by the caller's branch.</summary>
    private static void OpenCraftingTerminal(Construct c, int part)
    {
        if (MenusOpen)
            return;
        if (c.DefOf(part).IsBuildStation && c.Parts[part].Station is { State: StationState.Building } && _game is { Local: { } me } sg && sg.SandboxHands.Hand(me.Id).Held != ConstructParts.None)
        {
            sg.SandboxStation(c.Id, part, StationOp.Give); // (a Building Station that is building, a part in the hands: it goes to the build)
            return;
        }
        Ui.OpenCrafting(c.Id, part);
        SetMouseCaptured(false);
    }

    /// <summary>The panel over the match (the Sandbox ImGui pass); it closes (the mouse goes back to aiming) when it should.</summary>
    private static void DrawCraftingInMatch(double now)
    {
        if (!Ui.CraftingOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var (body, part) = Ui.CraftingTarget;
        var c = g?.Constructs.Get(body);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || c == null || _mouseCaptured
            || part < 0 || part >= c.PartCount || !c.DefOf(part).IsCraftTerminal
            || System.Numerics.Vector3.Distance(me!.Position, c.WorldCom) > c.Radius + ConstructConfig.Reach + 4;
        if (!gone && Ui.DrawCrafting(g!, c!, part, now))
            return;
        Ui.CloseCrafting();
        if (!MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also fire the tool)
    }
}
