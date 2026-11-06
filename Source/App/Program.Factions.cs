using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The Faction window's key (F2 by default, rebindable; Esc closes it) and its draw call (Source/UI/UiFactions.cs,
/// docs/sandbox/CONSTRUCTS.md "Factions"). The window counts as a menu, so the cursor is free while it's open.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>A key press for the Faction window: its bind toggles it (Sandbox only), Esc closes it. Returns true when it used the key.</summary>
    private static bool FactionKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.FactionsOpen)
        {
            SetFactionOpen(false);
            return true;
        }
        if (key == GlfwKey.Unknown || Keybinds.Key(key) != _settings.Bind(GameAction.FactionWindow) || Ui.ChatOpen || Ui.WantsTextInput || _session is not { SandboxOn: true })
            return false;
        SetFactionOpen(!Ui.FactionsOpen);
        return true;
    }

    private static void SetFactionOpen(bool open)
    {
        Ui.FactionsOpen = open;
        if (_session == null)
            return;
        if (open)
            SetMouseCaptured(false);
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    /// <summary>The Faction window (closed: one check).</summary>
    private static void DrawFactionUi()
    {
        if (!Ui.FactionsOpen)
            return;
        Ui.DrawFactions(_session, _settings.Bind(GameAction.FactionWindow));
        if (!Ui.FactionsOpen)
            SetFactionOpen(false); // closed with its own X
    }
}
