namespace CallOfSlop;

/// <summary>How wide the in-match HUD may spread on wide screens.</summary>
public enum HudWidth
{
    /// <summary>HUD kept inside a centred 16:9 area (the default).</summary>
    Wide16x9,

    /// <summary>HUD kept inside a centred 21:9 area.</summary>
    Wide21x9,

    /// <summary>HUD uses the whole screen width.</summary>
    Full,
}

/// <summary>
/// The HUD safe area: a rectangle centred on the screen, full height, whose width is capped at
/// a maximum aspect ratio of the screen height. On screens narrower than that it is the whole screen.
/// Pure math, so the self-test can check it without a window.
/// </summary>
public static class HudLayout
{
    /// <summary>The widest aspect ratio (width / height) the HUD may use, or +infinity for full width.</summary>
    public static float MaxAspect(HudWidth mode) => mode switch
    {
        HudWidth.Wide21x9 => 21f / 9f,
        HudWidth.Full => float.PositiveInfinity,
        _ => 16f / 9f,
    };

    /// <summary>Label for the settings screen.</summary>
    public static string Label(HudWidth mode) => mode switch
    {
        HudWidth.Wide21x9 => "21:9",
        HudWidth.Full => "Full width",
        _ => "16:9",
    };

    /// <summary>
    /// Horizontal extent of the safe area for a screen of <paramref name="width"/> x <paramref name="height"/>
    /// starting at x = 0: (left edge, width). The safe area always spans the full height.
    /// </summary>
    public static (float Left, float Width) Span(float width, float height, HudWidth mode)
    {
        var safe = MathF.Min(width, height * MaxAspect(mode));
        if (!(safe > 0)) // NaN or degenerate screen: fall back to the whole width
            safe = MathF.Max(width, 0);
        return (MathF.Round((width - safe) / 2), safe);
    }
}
