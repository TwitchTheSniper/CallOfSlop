using Brutal.GlfwApi;

namespace CallOfSlop;

/// <summary>
/// The Story window's key (F4 by default, rebindable; Esc closes it) and its draw call (Source/UI/UiStory.cs,
/// docs/story/STORY.md). The window counts as a menu, so the cursor is free while it's open.
/// </summary>
public static unsafe partial class Program
{
    /// <summary>A key press for the Story window: its bind toggles it, Esc closes it. Returns true when it used the key.</summary>
    private static bool StoryKey(GlfwKey key)
    {
        if (key == GlfwKey.Escape && Ui.StoryOpen)
        {
            SetStoryOpen(false);
            return true;
        }
        if (key == GlfwKey.Unknown || Keybinds.Key(key) != _settings.Bind(GameAction.StoryWindow) || Ui.ChatOpen || Ui.WantsTextInput || _session == null)
            return false;
        SetStoryOpen(!Ui.StoryOpen);
        return true;
    }

    private static void SetStoryOpen(bool open)
    {
        Ui.StoryOpen = open;
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

    /// <summary>The Story window (closed: one check).</summary>
    private static void DrawStoryUi()
    {
        if (!Ui.StoryOpen)
            return;
        Ui.DrawStory(_session, _settings.Bind(GameAction.StoryWindow));
        if (!Ui.StoryOpen)
            SetStoryOpen(false); // closed with its own X
    }
}
