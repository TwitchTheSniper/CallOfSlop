using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The Logbook's key (F1 by default, rebindable; Esc closes it) and its draw call (Source/UI/UiLogbook.cs, docs/story/FRAGMENTS.md).
/// The window counts as a menu, so the cursor is free while it's open.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>A key press for the Logbook: its bind toggles it (Sandbox only), Esc closes it. Returns true when it used the key.</summary>
    private static bool LogbookKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.LogbookOpen)
        {
            SetLogbookOpen(false);
            return true;
        }
        if (key == GlfwKey.Unknown || !_settings.HasBind(GameAction.Logbook, Keybinds.Key(key)) || Ui.ChatOpen || Ui.WantsTextInput || _session is not { SandboxOn: true })
            return false;
        SetLogbookOpen(!Ui.LogbookOpen);
        return true;
    }

    private static void SetLogbookOpen(bool open)
    {
        Ui.LogbookOpen = open;
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

    /// <summary>The Logbook (closed: one check).</summary>
    private static void DrawLogbookUi()
    {
        if (!Ui.LogbookOpen)
            return;
        Ui.DrawLogbook(_session, _settings.Bind(GameAction.Logbook));
        if (!Ui.LogbookOpen)
            SetLogbookOpen(false); // closed with its own X
    }
}
