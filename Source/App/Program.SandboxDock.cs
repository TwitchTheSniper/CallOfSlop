namespace CallOfSlop;

/// <summary>
/// Sandbox: the Ship Docking Station's console (GameSandboxDock.cs). The use key on it opens the Ship Designer over the match on the
/// design ship docked there (UiShipDesigner.cs, <see cref="Ui.OpenShipDesignerDocked"/>); it holds the mouse while open and closes on Esc,
/// on a local reset, and when the player dies, walks off or the station goes. APPLY TO SHIP sends the edited design to the server, which
/// rebuilds the ship where it lies.
/// </summary>
public static unsafe partial class Program
{
    private static (ushort Station, ushort Part, ushort Ship) _dockAt;
    private static bool _dockOpen;

    /// <summary>The use key on a Ship Docking Station: the docked ship in the designer, or why not. The press is spent by the caller's branch.</summary>
    private static void OpenShipDock(Game g, Construct c, int part)
    {
        if (MenusOpen)
            return;
        if (g.DockedShip(c, part, out var design, out var why) is not { } ship)
        {
            g.SandboxSay(why);
            return;
        }
        if (g.DockEditRefusal(ship, g.LocalId) is { } no)
        {
            g.SandboxSay(no);
            return;
        }
        if (!Ui.OpenShipDesignerDocked(design!.Text, c.Id, (ushort)part, ship.Id))
        {
            g.SandboxSay("the docked ship's design couldn't be opened (see the log)");
            return;
        }
        _dockAt = (c.Id, (ushort)part, ship.Id);
        _dockOpen = true;
        SetMouseCaptured(false);
    }

    /// <summary>The designer over the match while docked (the Sandbox ImGui pass); an apply is sent, and the mouse goes back when it closes.</summary>
    private static void DrawShipDockInMatch(double now)
    {
        if (!_dockOpen)
            return;
        var g = _game;
        var me = g?.Local;
        var st = g?.Constructs.Get(_dockAt.Station);
        var gone = _session == null || g is not { SandboxOn: true } || me is not { Alive: true } || g.Phase != MatchPhase.Playing || st == null
            || _dockAt.Part >= st.PartCount || st.DefOf(_dockAt.Part).ShipDockRange <= 0
            || System.Numerics.Vector3.Distance(me!.Position, Game.DockPad(st, _dockAt.Part)) > st.Radius + Game.DockReach;
        if (!gone && Ui.ShipDesignerDocked)
            Ui.DrawShipDesigner(now);
        if (Ui.ShipDesignerApply is { } json && g != null)
        {
            Ui.ShipDesignerApply = null;
            g.SandboxDockSend(_dockAt.Station, _dockAt.Part, _dockAt.Ship, json);
        }
        if (!gone && Ui.ShipDesignerDocked)
            return;
        CloseShipDock();
    }

    /// <summary>The docked designer shuts (Esc, a reset, the player walked off): nothing is sent, the mouse goes back to aiming.</summary>
    private static void CloseShipDock()
    {
        if (Ui.ShipDesignerDock != null)
            Ui.ForceCloseShipDesigner();
        Ui.ShipDesignerApply = null; // (an apply never waits for a later session)
        if (!_dockOpen)
            return;
        _dockOpen = false;
        if (_session != null && !MenusOpen)
            SetMouseCaptured(true);
        _swallowClick = true; // (the click that closed it mustn't also fire the tool)
    }
}
