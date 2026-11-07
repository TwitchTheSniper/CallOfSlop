namespace CallOfSlop;

/// <summary>
/// Fixed limits that were reached (docs/perf/LIMITS.md): a cost budget or a pool that stops at a size says so in the log,
/// at most once every 30 s for each limit, so a big build that hits one is found from a user's log rather than guessed at.
/// Nothing allocates unless a line is written (<paramref name="what"/> is a constant string, the limiter's key).
/// </summary>
public static class Caps
{
    private static readonly LogLimiter Limit = new(30);

    /// <summary>Times any limit was reached in this run (the perf window and hand checks).</summary>
    public static int Hits;

    /// <summary>The last line written, or null.</summary>
    public static string? Last;

    /// <summary><paramref name="what"/> reached its limit <paramref name="cap"/> (<paramref name="want"/>: how many were asked for, or -1).</summary>
    public static void Hit(string what, int cap, int want = -1)
    {
        Hits++;
        if (!Limit.Allow(what, out var held))
            return;
        Last = want >= 0 ? $"[caps] {what}: {want} wanted, the limit is {cap}; the rest are left out" : $"[caps] {what}: the limit of {cap} was reached";
        Console.WriteLine(Last + LogLimiter.HeldText(held));
    }
}
