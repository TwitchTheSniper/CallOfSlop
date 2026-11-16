namespace CallOfSlop.Space;

/// <summary>
/// Trips between planets (docs/space/COMMAND.md "Between planets"; Karl, 2026-11-16: ships coast between planets in real
/// time, flip-and-burn realism, and the command view can speed time up). A trip is planned as a transfer round the star:
/// a burn out, a coast on the conic the burn puts the ship on, and a burn in. <see cref="Lambert"/> finds the conic that
/// joins two places in a given time; <see cref="Plan"/> picks the quickest time whose two burns fit the share of the
/// ship's change of speed (dV) a trip may spend. The ship's drive is strong (the Magpie pushes at 4 g) and its burns last
/// minutes against a coast of weeks, so the burns are planned as kicks and flown closed-loop (GameNav.cs re-solves each
/// tick while it burns and now and then while it coasts). Pure functions, no allocation.
/// </summary>
public static class NavTrip
{
    /// <summary>The share of a ship's dV one trip may spend (Karl's call, 2026-11-16 decision card: half, so it can get home).</summary>
    public const double TripShare = 0.5;

    /// <summary>A ship that burns no fuel (on power) is planned as if it had this much dV (m/s).</summary>
    public const double PowerDv = 100_000;

    /// <summary>The longest trip planned (s): about four years of game time.</summary>
    public const double LongestTrip = 4 * 365.25 * 86400;

    /// <summary>The burns together may take at most this share of the trip, so treating them as kicks holds.</summary>
    public const double BurnShare = 0.2;

    /// <summary>
    /// The conic round a body of <paramref name="mu"/> that leaves <paramref name="r1"/> and is at <paramref name="r2"/>
    /// <paramref name="tof"/> seconds later, the short way round or the long (less than one turn: universal variables,
    /// bisection on psi, as Vallado's algorithm 58). Gives the speeds at both ends. False when there is no such conic or
    /// the two places are nearly in line with the body (the plane is then undefined).
    /// </summary>
    public static bool Lambert(Vec3d r1, Vec3d r2, double tof, double mu, bool longWay, out Vec3d v1, out Vec3d v2)
    {
        v1 = v2 = Vec3d.Zero;
        var n1 = r1.Length;
        var n2 = r2.Length;
        if (!(tof > 0) || !(n1 > 1) || !(n2 > 1) || !(mu > 0))
            return false;
        var cos = Math.Clamp(Vec3d.Dot(r1, r2) / (n1 * n2), -1, 1);
        var a = (longWay ? -1 : 1) * Math.Sqrt(n1 * n2 * (1 + cos));
        if (Math.Abs(a) < 1e-6 * (n1 + n2))
            return false;
        double lo = -4 * Math.PI * Math.PI, hi = 4 * Math.PI * Math.PI, psi = 0, y = 0;
        var sqMu = Math.Sqrt(mu);
        for (var it = 0; it < 200; it++)
        {
            Stumpff(psi, out var c2, out var c3);
            y = n1 + n2 + a * (psi * c3 - 1) / Math.Sqrt(c2);
            if (a > 0 && y < 0)
            {
                // Not reachable at this psi: up it until y is positive.
                lo = psi;
                psi = 0.5 * (lo + hi);
                continue;
            }
            var chi = Math.Sqrt(y / c2);
            var t = (chi * chi * chi * c3 + a * Math.Sqrt(y)) / sqMu;
            if (Math.Abs(t - tof) < 1e-7 * tof)
                break;
            if (t <= tof)
                lo = psi;
            else
                hi = psi;
            psi = 0.5 * (lo + hi);
            if (hi - lo < 1e-12)
                break;
        }
        if (!(y > 0))
            return false;
        var f = 1 - y / n1;
        var g = a * Math.Sqrt(y / mu);
        var gDot = 1 - y / n2;
        if (Math.Abs(g) < 1e-9)
            return false;
        v1 = (r2 - r1 * f) / g;
        v2 = (r2 * gDot - r1) / g;
        return double.IsFinite(v1.X + v1.Y + v1.Z + v2.X + v2.Y + v2.Z);
    }

    private static void Stumpff(double psi, out double c2, out double c3)
    {
        if (psi > 1e-6)
        {
            var s = Math.Sqrt(psi);
            c2 = (1 - Math.Cos(s)) / psi;
            c3 = (s - Math.Sin(s)) / (s * psi);
        }
        else if (psi < -1e-6)
        {
            var s = Math.Sqrt(-psi);
            c2 = (1 - Math.Cosh(s)) / psi;
            c3 = (Math.Sinh(s) - s) / (s * -psi);
        }
        else
        {
            c2 = 0.5 - psi / 24;
            c3 = 1.0 / 6 - psi / 120;
        }
    }

    /// <summary>The planet a body belongs to: itself for a planet, its planet for a moon, 0 for the star.</summary>
    public static int PlanetOf(StarSystem sys, int body)
    {
        while (body > 0 && sys.Bodies[body].Parent > 0)
            body = sys.Bodies[body].Parent;
        return Math.Max(0, body);
    }

    /// <summary>
    /// Is a trip from <paramref name="at"/> to <paramref name="body"/> one between planets (a coast round the star)? True
    /// when the target is not the star and the ship is outside the sphere of influence of the target's planet.
    /// </summary>
    public static bool Crosses(StarSystem sys, SpacePos at, SpaceTime t, int body)
    {
        var planet = PlanetOf(sys, body);
        if (planet == 0)
            return false;
        return (at - sys.PositionOf(planet, t)).Length > SpaceShips.SphereOfInfluence(sys, planet);
    }

    /// <summary>
    /// The burn wanted now to be at <paramref name="body"/> (plus <paramref name="offset"/> from it) at
    /// <paramref name="arrive"/>: the change of speed out (ecliptic axes) and what the burn in will be. False when no conic
    /// joins them (the caller coasts on and tries again).
    /// </summary>
    public static bool Burn(StarSystem sys, SpacePos at, Vec3d vel, SpaceTime t, int body, Vec3d offset, SpaceTime arrive, out Vec3d dvOut, out double dvIn)
    {
        dvOut = Vec3d.Zero;
        dvIn = 0;
        var tof = arrive - t;
        if (!(tof > 1))
            return false;
        sys.StateOf(0, t, out var s0, out var sv0);
        sys.StateOf(0, arrive, out var s1, out _);
        sys.StateOf(body, arrive, out var tp, out var tv);
        var r1 = at - s0;
        var r2 = tp + offset - s1;
        var v = vel - sv0;
        var mu = sys.Bodies[0].Mu;
        var best = double.PositiveInfinity;
        for (var way = 0; way < 2; way++)
        {
            if (!Lambert(r1, r2, tof, mu, way == 1, out var v1, out var v2))
                continue;
            var o = v1 - v;
            var i = (tv - sv0 - v2).Length; // (the star's own speed is the same at both ends to well under a mm/s)
            if (o.Length + i < best)
            {
                best = o.Length + i;
                dvOut = o;
                dvIn = i;
            }
        }
        return double.IsFinite(best);
    }

    /// <summary>
    /// The quickest trip whose burns fit <paramref name="budget"/> (m/s) at <paramref name="aMax"/> (m/s2): its arrival
    /// time and the dV it spends. When none fits, false, with <paramref name="need"/> the least any trip in reach spends.
    /// </summary>
    public static bool Plan(StarSystem sys, SpacePos at, Vec3d vel, SpaceTime t, int body, Vec3d offset, double aMax, double budget,
        out SpaceTime arrive, out double spend, out double need)
    {
        arrive = t;
        spend = 0;
        need = double.PositiveInfinity;
        if (!(aMax > 0))
            return false;
        var gap = (sys.PositionOf(body, t) + offset - at).Length;
        // The quickest a ship could cross it at all, burning all the way (flip and burn with no coast).
        var quickest = Math.Max(600, 2 * Math.Sqrt(gap / aMax));
        const int Steps = 64;
        var ratio = Math.Pow(LongestTrip / quickest, 1.0 / (Steps - 1));
        double okT = 0, badT = 0;
        var tof = quickest;
        for (var k = 0; k < Steps; k++, tof *= ratio)
        {
            var when = t + (long)(tof * SpaceTime.NsPerSecond);
            if (!Burn(sys, at, vel, t, body, offset, when, out var o, out var i))
                continue;
            var total = o.Length + i;
            need = Math.Min(need, total);
            if (total <= budget && total / aMax <= BurnShare * tof)
            {
                okT = tof;
                break;
            }
            badT = tof;
        }
        if (okT == 0)
            return false;
        // Closer to the quickest that still fits, between the last that didn't and the first that did.
        if (badT > 0)
            for (var k = 0; k < 24; k++)
            {
                var mid = 0.5 * (okT + badT);
                var fits = Burn(sys, at, vel, t, body, offset, t + (long)(mid * SpaceTime.NsPerSecond), out var o, out var i)
                           && o.Length + i <= budget && (o.Length + i) / aMax <= BurnShare * mid;
                if (fits)
                    okT = mid;
                else
                    badT = mid;
            }
        arrive = t + (long)(okT * SpaceTime.NsPerSecond);
        Burn(sys, at, vel, t, body, offset, arrive, out var dvo, out var dvi);
        spend = dvo.Length + dvi;
        need = Math.Min(need, spend);
        return true;
    }
}
