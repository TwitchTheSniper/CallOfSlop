using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The command view's key (Home by default, rebindable; Esc closes it) and its draw call (Source/UI/UiCommand.cs,
/// docs/space/COMMAND.md). The view counts as a menu, so the cursor is free and the game takes no input while it's open;
/// it joins <see cref="SandboxLocalReset"/> and the Esc chain.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>A key press for the command view: its bind toggles it (Sandbox, on a map in a star system), Esc closes it. Returns true when it used the key.</summary>
    private static bool CommandKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.CommandOpen)
        {
            if (!Ui.CommandEsc()) // (an open order menu goes first)
                SetCommandOpen(false);
            return true;
        }
        if (key == GlfwKey.Unknown || !_settings.HasBind(GameAction.CommandView, Keybinds.Key(key)) || Ui.ChatOpen || Ui.WantsTextInput || _session is not { SandboxOn: true } g)
            return false;
        if (!Ui.CommandOpen && g.Space == null)
        {
            g.SandboxSay("The command view needs a star system: this map isn't in one");
            return true;
        }
        SetCommandOpen(!Ui.CommandOpen);
        return true;
    }

    internal static void SetCommandOpen(bool open)
    {
        if (open ? Ui.PauseOpen : !Ui.CommandOpen)
            return; // (never over the pause menu: single player is stopped under it, and no order would move)
        Ui.SetCommandOpen(open, _session, Glfw.Time);
        CommandCursor();
    }

    /// <summary>The cursor is free while the view is open, and goes back to the game when it closes.</summary>
    private static void CommandCursor()
    {
        if (_session == null)
            return;
        if (Ui.CommandOpen)
            SetMouseCaptured(false);
        else if (CursorPolicy.MayCapture(CursorNow()))
        {
            SetMouseCaptured(true);
            _swallowClick = true;
        }
    }

    /// <summary>The command view (closed: one check).</summary>
    private static void DrawCommandUi(double now)
    {
        if (!Ui.CommandOpen)
            return;
        Ui.DrawCommand(_session, now, Keybinds.Name(_settings.Bind(GameAction.CommandView)));
        if (!Ui.CommandOpen)
            CommandCursor(); // (it closed itself: the map changed under it)
    }
}
