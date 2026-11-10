using System.Numerics;

namespace CallOfSlop.Space;

/// <summary>
/// The galaxy tier (phase 6, docs/space/GALAXY.md; the doc's tier 1, GAL): every star system of the game
/// (<see cref="StarSystems.All"/>, at most <see cref="MaxSystems"/>: Karl, 2026-11-04, for gameplay) placed at a point in
/// light-years. Every system's ecliptic shares the galaxy's axes, so the way from one system to another is the same in
/// both, and a system is its own fixed-point world (SpacePos) inside: the galaxy only says where the stars are, how far
/// apart, and where a jump arrives. A system's index in <see cref="StarSystems.All"/> is its galaxy index (the wire's
/// byte; peers agree on the order through <see cref="Hash"/>). Pure functions of the files; no allocation per query.
/// </summary>
public static class Galaxy
{
    /// <summary>The most star systems a galaxy holds (Karl, 2026-11-04: ten at most, for gameplay); more files are refused.</summary>
    public const int MaxSystems = 10;

    /// <summary>How far from the galaxy's middle a system may sit (light-years, per axis).</summary>
    public const double MaxLy = 1000;

    /// <summary>Two systems closer than this (light-years) are refused: the later file of the two.</summary>
    public const double MinSeparationLy = 0.05;

    /// <summary>One light-year (m): the IAU's, c x a Julian year.</summary>
    public const double LightYear = 9_460_730_472_580_800.0;

    public static int Count => StarSystems.All.Count;

    public static StarSystem System(int index) => StarSystems.All[index].System;

    /// <summary>A system's galaxy index (by reference), or -1.</summary>
    public static int IndexOf(StarSystem sys)
    {
        var all = StarSystems.All;
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i].System, sys))
                return i;
        return -1;
    }

    /// <summary>Every system's file hash in galaxy order, mixed (a client follows the host's other systems only when this matches).</summary>
    public static uint Hash
    {
        get
        {
            var h = 2166136261u;
            foreach (var e in StarSystems.All)
                h = (h ^ e.Hash) * 16777619u;
            return h;
        }
    }

    /// <summary>Light-years between two systems.</summary>
    public static double DistanceLy(int a, int b) => (System(a).GalaxyLy - System(b).GalaxyLy).Length;

    /// <summary>The way from one system's star to another's (unit, the shared ecliptic axes).</summary>
    public static Vec3d DirectionTo(int from, int to) => (System(to).GalaxyLy - System(from).GalaxyLy).Normalized();

    /// <summary>
    /// The <paramref name="nth"/> nearest other system to <paramref name="from"/> (1: the nearest), and how far. False
    /// when there aren't that many. Ties go to the lower index, so every peer picks the same one.
    /// </summary>
    public static bool Nearest(int from, int nth, out int to, out double ly)
    {
        to = -1;
        ly = 0;
        var n = Count;
        if ((uint)from >= (uint)n || nth < 1 || nth >= n)
            return false;
        Span<int> order = stackalloc int[MaxSystems];
        Span<double> dist = stackalloc double[MaxSystems];
        var k = 0;
        for (var i = 0; i < n && k < MaxSystems; i++)
        {
            if (i == from)
                continue;
            var d = DistanceLy(from, i);
            var j = k++;
            while (j > 0 && dist[j - 1] > d)
            {
                order[j] = order[j - 1];
                dist[j] = dist[j - 1];
                j--;
            }
            order[j] = i;
            dist[j] = d;
        }
        if (nth > k)
            return false;
        to = order[nth - 1];
        ly = dist[nth - 1];
        return true;
    }

    /// <summary>
    /// Where a jump from <paramref name="from"/> arrives in <paramref name="to"/> at a time: <see cref="StarSystem.Arrival"/>
    /// from its star (or from the world its file names: <see cref="StarSystem.ArrivalBody"/>), on the side facing the system it came from (flattened onto the ecliptic), in a circular orbit round
    /// it going east (so it neither falls in nor drifts off while its crew gets their bearings). Ecliptic, the
    /// arrival system's.
    /// </summary>
    public static void Arrive(int from, int to, SpaceTime t, out SpacePos pos, out Vec3d vel)
    {
        var sys = System(to);
        var dir = from == to ? Vec3d.UnitX : DirectionTo(to, from);
        dir = new Vec3d(dir.X, dir.Y, 0);
        dir = dir.Length > 1e-9 ? dir.Normalized() : Vec3d.UnitX;
        var r = sys.Arrival;
        var b = sys.ArrivalBody;
        sys.StateOf(b, t, out var centre, out var centreVel);
        pos = centre + dir * r;
        vel = centreVel + Vec3d.Cross(Vec3d.UnitZ, dir) * Math.Sqrt(sys.Bodies[b].Mu / r);
    }

    /// <summary>
    /// The default arrival distance: a quarter past the farthest the star's own bodies get (so a jump never lands on a
    /// planet's doorstep), at least half an AU, and well clear of the star.
    /// </summary>
    public static double DefaultArrival(StarSystem sys)
    {
        var far = 0.0;
        for (var b = 1; b < sys.Count; b++)
            if (sys.Bodies[b].Parent == 0)
                far = Math.Max(far, sys.Bodies[b].Orbit.Apoapsis);
        return Math.Max(Math.Max(far * 1.25, 0.5 * StarSystemFile.Au), sys.Bodies[0].Radius * 4);
    }

    /// <summary>How bright a star looks from another system (0-1.5, for the sky's point): its luminosity (from its mass, L ~ M^3.5) over the distance squared.</summary>
    public static float ApparentBrightness(StarSystem star, double ly)
    {
        var m = star.Bodies[0].Mu / 1.32712440018e20; // (solar masses)
        var l = Math.Pow(Math.Max(m, 0.05), 3.5);
        return (float)Math.Clamp(0.9 * l / Math.Max(ly * ly / 16, 0.05), 0.06, 1.5);
    }

    private static readonly Vector3 SunWhite = new(1f, 0.93f, 0.82f);

    /// <summary>A star's colour as a point in another system's sky: its "color", else a white from its mass (lighter stars redder).</summary>
    public static Vector3 StarColor(StarSystem star)
    {
        if (star.Bodies[0].Color is { } c)
            return c;
        var m = star.Bodies[0].Mu / 1.32712440018e20;
        return m < 0.6 ? new Vector3(1f, 0.62f, 0.42f) : m > 1.5 ? new Vector3(0.78f, 0.86f, 1f) : SunWhite;
    }
}
