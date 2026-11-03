namespace CallOfSlop.Space;

/// <summary>
/// A position in a star system's ecliptic frame (ECL, docs/space/SPACE.md) as 64-bit fixed point: whole steps of
/// <see cref="Unit"/> (0.1 mm) on each axis. The step is the same everywhere, so a position 6,000 AU from the star is as
/// exact as one at the star, and the difference of two positions is exact (a long subtraction) before it becomes a
/// <see cref="Vec3d"/> in metres. Reach is ±<see cref="Reach"/> (about 9.2 x 10^14 m, 6,000 AU): a whole system, with room.
/// Whole star systems are placed in the galaxy tier separately.
/// </summary>
public readonly record struct SpacePos(long X, long Y, long Z)
{
    /// <summary>Metres per step.</summary>
    public const double Unit = 1e-4;

    /// <summary>Steps per metre.</summary>
    public const double PerMetre = 1e4;

    /// <summary>The largest distance from the star on an axis, in metres (long.MaxValue steps, rounded down).</summary>
    public const double Reach = long.MaxValue * Unit;

    /// <summary>The farthest any body or frame is allowed (leaves headroom so sums of two in-range positions can't overflow).</summary>
    public const double SafeReach = Reach / 4;

    public static readonly SpacePos Origin = new(0, 0, 0);

    /// <summary>The nearest fixed-point position to a point in metres (refused past <see cref="Reach"/>).</summary>
    public static SpacePos FromMetres(Vec3d m) => new(Steps(m.X), Steps(m.Y), Steps(m.Z));

    /// <summary>This position in metres (as a double: below 0.1 mm exact only within about 9 x 10^11 m of the star).</summary>
    public Vec3d ToMetres() => new(X * Unit, Y * Unit, Z * Unit);

    /// <summary>The offset from <paramref name="b"/> to <paramref name="a"/> in metres; the subtraction itself is exact.</summary>
    public static Vec3d operator -(SpacePos a, SpacePos b) => new((a.X - b.X) * Unit, (a.Y - b.Y) * Unit, (a.Z - b.Z) * Unit);

    /// <summary>This position moved by an offset in metres (rounded to the nearest step).</summary>
    public static SpacePos operator +(SpacePos a, Vec3d d) => new(a.X + Steps(d.X), a.Y + Steps(d.Y), a.Z + Steps(d.Z));

    public static SpacePos operator -(SpacePos a, Vec3d d) => new(a.X - Steps(d.X), a.Y - Steps(d.Y), a.Z - Steps(d.Z));

    /// <summary>Metres to the nearest whole step.</summary>
    public static long Steps(double metres)
    {
        var s = Math.Round(metres * PerMetre, MidpointRounding.AwayFromZero);
        if (!(Math.Abs(s) < 9.2e18)) // also NaN
            throw new ArgumentOutOfRangeException(nameof(metres), metres, "outside the space position range");
        return (long)s;
    }

    public override string ToString() => ToMetres().ToString();
}

/// <summary>
/// Space time (the doc's BubbleTime): a 64-bit count of nanoseconds since the system's epoch. Integer, so it never drifts
/// however long the game runs or how fast time is warped; it reaches about 292 years. The 60 Hz game tick isn't a whole
/// number of nanoseconds, so <see cref="TickNs"/> gives the exact floor of n / 60 s, and 60 ticks are exactly a second.
/// </summary>
public readonly record struct SpaceTime(long Ns) : IComparable<SpaceTime>
{
    public const long NsPerSecond = 1_000_000_000;

    public static readonly SpaceTime Epoch = new(0);

    public double Seconds => Ns / (double)NsPerSecond;

    public static SpaceTime FromSeconds(double s) => new(checked((long)Math.Round(s * NsPerSecond)));

    public static SpaceTime operator +(SpaceTime t, long ns) => new(t.Ns + ns);

    /// <summary>Seconds from <paramref name="b"/> to <paramref name="a"/> (exact in ns, then one rounding).</summary>
    public static double operator -(SpaceTime a, SpaceTime b) => (a.Ns - b.Ns) / (double)NsPerSecond;

    /// <summary>The nanoseconds in <paramref name="ticks"/> game ticks (floor of ticks x 10^9 / 60), with no overflow in the clock's range.</summary>
    public static long TickNs(long ticks)
    {
        // 10^9 / 60 = 50,000,000 / 3: split so the product never overflows.
        var whole = ticks / 3;
        var rest = ticks - whole * 3;
        return checked(whole * 50_000_000 + rest * 50_000_000 / 3);
    }

    public int CompareTo(SpaceTime other) => Ns.CompareTo(other.Ns);

    public override string ToString()
    {
        var s = Seconds;
        var days = Math.Floor(s / 86400);
        return FormattableString.Invariant($"day {days:0} {TimeSpan.FromSeconds(s - days * 86400):hh\\:mm\\:ss}");
    }
}

/// <summary>
/// The space clock: stepped once per game tick, with a whole-number time warp. Time is the time at the last warp change
/// plus the warped ticks since, computed from integers every time, so changing warp or running for days never drifts.
/// </summary>
public sealed class SpaceClock
{
    /// <summary>The fastest warp allowed (the doc's 100,000x).</summary>
    public const int MaxWarp = 100_000;

    private long _baseNs;
    private long _ticks;
    private int _warp = 1;

    public SpaceClock(SpaceTime start = default) => _baseNs = start.Ns;

    public SpaceTime Now => new(_baseNs + SpaceTime.TickNs(_ticks * _warp));

    /// <summary>How many times faster than real time space time runs (1 to <see cref="MaxWarp"/>; 0 pauses it).</summary>
    public int Warp
    {
        get => _warp;
        set
        {
            var w = Math.Clamp(value, 0, MaxWarp);
            if (w == _warp)
                return;
            _baseNs = Now.Ns;
            _ticks = 0;
            _warp = w;
        }
    }

    /// <summary>One game tick (1/60 s of real time).</summary>
    public void Tick() => _ticks++;

    /// <summary>Jumps to a time (a loaded save, a host's clock).</summary>
    public void Set(SpaceTime t)
    {
        _baseNs = t.Ns;
        _ticks = 0;
    }
}
