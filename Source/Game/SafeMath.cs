namespace CallOfSlop;

/// <summary>
/// Clamps whose bounds are worked out at run time (board item dev-crash-sweep-cs01). <c>Math.Clamp</c> throws when its lower
/// bound is above its upper one, and bounds made from the same numbers (a centre plus and minus a half, less an inset) can
/// end up a hair inverted by float rounding: that crashed the game on F at a narrow part (ConstructRide.GrabOn). These never
/// throw: inverted bounds give their middle (a float) or the lower bound (an int), which is what a face no wider than the
/// inset, or an empty list, means.
/// </summary>
public static class SafeMath
{
    /// <summary><paramref name="v"/> kept within lo..hi; inverted (or NaN) bounds give their middle.</summary>
    public static float Clamp(float v, float lo, float hi) => lo <= hi ? Math.Clamp(v, lo, hi) : float.IsNaN(lo) || float.IsNaN(hi) ? v : (lo + hi) * 0.5f;

    /// <summary><paramref name="v"/> kept within lo..hi; inverted bounds (an empty range: 0 .. count - 1 with no items) give lo.</summary>
    public static int Clamp(int v, int lo, int hi) => lo <= hi ? Math.Clamp(v, lo, hi) : lo;
}
