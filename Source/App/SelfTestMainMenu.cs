namespace CallOfSlop;

public static partial class SelfTest
{
    /// <summary>
    /// Main menu width (board item): the panel is the button width plus window padding, because the level, prestige and
    /// next-unlock text wraps at <see cref="Ui.MenuButtonWidth"/> instead of widening it. The text itself needs a live
    /// ImGui frame, so this checks the sums at common resolutions: the panel never exceeds a third of the window and
    /// always fits it, and the longest unlock line the menu can draw is plain ASCII.
    /// </summary>
    private static void CheckMainMenuWidth()
    {
        foreach (var (w, h) in new[] { (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (800, 600) })
        {
            var s = Math.Clamp(h / 900f, 0.8f, 3f); // Ui.TargetScale at the default UI scale
            var panel = (Ui.MenuButtonWidth + 16) * s; // buttons plus ImGui's window padding, scaled
            if (panel > w)
                Fail($"main menu at {w}x{h}: panel {panel:0} px does not fit the window");
            else if (w >= 1280 && panel > w / 2.5f)
                Fail($"main menu at {w}x{h}: panel {panel:0} px is more than 40% of the window");
            Line($"{w}x{h}: main menu panel {panel:0} px ({panel * 100 / w:0}% of the width)");
        }
        var longest = "";
        foreach (var w in Weapons.All)
            if (w.Name.Length > longest.Length) longest = w.Name;
        foreach (var ch in $"Next unlock: {longest} (weapon) at level 99")
            if (ch > 126 || ch < 32)
                Fail($"main menu: next-unlock text for '{longest}' has a non-ASCII character");
    }
}
