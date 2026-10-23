namespace CallOfSlop;

/// <summary>What the mouse rule needs to know about this frame (see <see cref="CursorPolicy"/>).</summary>
/// <param name="InSession">A match (or the lobby room) is running.</param>
/// <param name="MenuOpen">A screen that wants the cursor is open: loadout (and its TEAMS window), pause menu, a Zombies workbench, the lobby room.</param>
/// <param name="ResultsScreen">The match has ended: the results screen and map vote are showing.</param>
/// <param name="Focused">The game window has the keyboard focus.</param>
public readonly record struct CursorState(bool InSession, bool MenuOpen, bool ResultsScreen, bool Focused);

/// <summary>
/// The one rule for when the mouse is captured (hidden, turning the camera) and when it is a free, visible cursor.
/// Pure logic, so the self-test can check every combination headless.
/// The HUD chat line is not a menu: it leaves the capture state alone (typing suspends mouse look and the controls instead),
/// so chatting on the results screen keeps the free cursor and chatting in play keeps the captured one.
/// <list type="bullet">
/// <item>The mouse may only be captured in a match with no menu open and not on the results/map vote screen.</item>
/// <item>A click in the match recaptures a freed mouse, unless it lands on a UI window or capturing isn't allowed.</item>
/// <item>When the results screen ends (the next warmup/countdown starts), the mouse is recaptured by itself, if the window is focused and nothing else is open.</item>
/// </list>
/// </summary>
public static class CursorPolicy
{
    /// <summary>Whether the mouse may be captured now (any request to capture outside this is refused).</summary>
    public static bool MayCapture(in CursorState s) => s.InSession && !s.MenuOpen && !s.ResultsScreen;

    /// <summary>Whether a mouse click recaptures the mouse (<paramref name="overUi"/>: ImGui wants the mouse).</summary>
    public static bool ClickCaptures(in CursorState s, bool captured, bool overUi) => !captured && !overUi && MayCapture(s);

    /// <summary>Whether to recapture by itself this frame: the results screen just ended into a match with nothing open.</summary>
    public static bool RecaptureAfterResults(bool wasResults, in CursorState now, bool captured)
        => wasResults && !captured && now.Focused && MayCapture(now);
}
