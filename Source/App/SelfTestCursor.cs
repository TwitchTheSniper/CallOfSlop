namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>The mouse capture rule (CursorPolicy): every phase x open window x click combination.</summary>
    private static void CheckCursorPolicy()
    {
        var bad = 0;
        var cases = 0;
        foreach (var inSession in new[] { false, true })
        foreach (var menu in new[] { false, true })
        foreach (var results in new[] { false, true })
        foreach (var focused in new[] { false, true })
        foreach (var captured in new[] { false, true })
        foreach (var overUi in new[] { false, true })
        {
            cases++;
            var s = new CursorState(inSession, menu, results, focused);
            var free = inSession && !menu && !results; // the only state the mouse may be captured in
            void Check(bool got, bool want, string what)
            {
                if (got == want)
                    return;
                bad++;
                if (bad <= 8)
                    Fail($"Cursor: {what} = {got}, expected {want} (session {inSession}, menu {menu}, results {results}, focused {focused}, captured {captured}, over UI {overUi})");
            }
            Check(CursorPolicy.MayCapture(s), free, "may capture");
            Check(CursorPolicy.ClickCaptures(s, captured, overUi), free && !captured && !overUi, "click captures");
            Check(CursorPolicy.RecaptureAfterResults(true, s, captured), free && !captured && focused, "recapture after results");
            Check(CursorPolicy.RecaptureAfterResults(false, s, captured), false, "recapture without results");
        }

        // The user's bug: a left click anywhere on the map vote screen (on a card or on empty space) never captures.
        var vote = new CursorState(true, false, true, true);
        var voteClick = CursorPolicy.ClickCaptures(vote, false, false) || CursorPolicy.ClickCaptures(vote, false, true);
        // Pause menu opened and closed again over the vote (Esc, Esc): still free.
        var afterPause = CursorPolicy.MayCapture(vote with { MenuOpen = false });
        // Vote over -> warmup with the window focused: captured again; unfocused or the loadout open: stays free.
        var next = CursorPolicy.RecaptureAfterResults(true, vote with { ResultsScreen = false }, false);
        var unfocused = CursorPolicy.RecaptureAfterResults(true, new CursorState(true, false, false, false), false);
        var loadout = CursorPolicy.RecaptureAfterResults(true, new CursorState(true, true, false, true), false);
        Line($"{cases} cases, {bad} wrong; vote click captures {voteClick}, Esc-Esc on the vote captures {afterPause}, " +
             $"next match recaptures {next} (unfocused {unfocused}, loadout open {loadout})");
        if (voteClick || afterPause)
            Fail("Cursor: the map vote screen can lose the mouse");
        if (!next || unfocused || loadout)
            Fail("Cursor: wrong recapture when the results screen ends");
    }
}
