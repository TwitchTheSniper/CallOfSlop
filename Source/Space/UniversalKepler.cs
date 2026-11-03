namespace CallOfSlop.Space;

/// <summary>
/// Two-body propagation for any conic (circle, ellipse, parabola, hyperbola) with the universal variable (Stumpff
/// functions, solved with Laguerre-Conway): the exact "drift" of the ships' symplectic step and the rails a coasting ship
/// is put on (docs/space/SPACE.md, "Ships"). <see cref="KeplerOrbit"/> covers closed orbits only; ships also escape.
/// No allocation.
/// </summary>
public static class UniversalKepler
{
    /// <summary>The Stumpff functions C(z) and S(z).</summary>
    public static void Stumpff(double z, out double c, out double s)
    {
        if (Math.Abs(z) < 1e-2)
        {
            // Series: the closed forms lose digits to cancellation near zero.
            var z2 = z * z;
            c = 1.0 / 2 - z / 24 + z2 / 720 - z2 * z / 40320 + z2 * z2 / 3628800;
            s = 1.0 / 6 - z / 120 + z2 / 5040 - z2 * z / 362880 + z2 * z2 / 39916800;
        }
        else if (z > 0)
        {
            var sz = Math.Sqrt(z);
            var (sn, cs) = Math.SinCos(sz);
            c = (1 - cs) / z;
            s = (sz - sn) / (z * sz);
        }
        else
        {
            var sz = Math.Sqrt(-z);
            c = (Math.Cosh(sz) - 1) / -z;
            s = (Math.Sinh(sz) - sz) / (-z * sz);
        }
    }

    /// <summary>
    /// The state <paramref name="dt"/> seconds on (or back, if negative) from (r0, v0) about a centre of GM
    /// <paramref name="mu"/>. Whole periods of a closed orbit are taken off first. False (state unchanged) if it doesn't
    /// converge, which only happens for absurd inputs (a hyperbola thousands of radii out in one step).
    /// </summary>
    public static bool Propagate(double mu, Vec3d r0, Vec3d v0, double dt, out Vec3d r, out Vec3d v)
    {
        var r0m = r0.Length;
        var alpha = 2 / r0m - v0.LengthSquared / mu;
        if (alpha > 0)
        {
            var period = 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha);
            if (Math.Abs(dt) > period / 2)
                dt = Math.IEEERemainder(dt, period);
        }
        return Solve(mu, r0, v0, r0m, alpha, dt, out r, out v);
    }

    /// <summary>
    /// Like <see cref="Propagate"/>, for a time span in nanoseconds: whole periods come off in integer nanoseconds
    /// (<see cref="KeplerOrbit.PhaseSeconds"/>), so a ship on rails for years is where its phase says.
    /// </summary>
    public static bool PropagateNs(double mu, Vec3d r0, Vec3d v0, long ns, out Vec3d r, out Vec3d v)
    {
        var r0m = r0.Length;
        var alpha = 2 / r0m - v0.LengthSquared / mu;
        var dt = alpha > 0
            ? KeplerOrbit.PhaseSeconds(ns, 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha))
            : ns / (double)SpaceTime.NsPerSecond;
        return Solve(mu, r0, v0, r0m, alpha, dt, out r, out v);
    }

    private static bool Solve(double mu, Vec3d r0, Vec3d v0, double r0m, double alpha, double dt, out Vec3d r, out Vec3d v)
    {
        if (dt == 0 || !(r0m > 0) || !(mu > 0))
        {
            r = r0;
            v = v0;
            return dt == 0;
        }
        var sqmu = Math.Sqrt(mu);
        var rv = Vec3d.Dot(r0, v0) / sqmu;
        var k = 1 - alpha * r0m;

        // A first guess: Laguerre-Conway converges from almost anywhere, a good start saves iterations.
        double chi;
        if (alpha > 1e-12 / r0m)
            chi = sqmu * dt * alpha;
        else if (alpha < -1e-12 / r0m && Math.Abs(dt) * v0.Length > r0m)
        {
            // Far along a hyperbola (Vallado's guess; it's poor for short steps, where the first-order one is good).
            var a = 1 / alpha;
            var sg = Math.Sign(dt);
            chi = sg * Math.Sqrt(-a) * Math.Log(-2 * mu * alpha * dt / (Vec3d.Dot(r0, v0) + sg * Math.Sqrt(-mu * a) * k));
            if (!double.IsFinite(chi))
                chi = sqmu * dt / r0m;
        }
        else
            chi = sqmu * dt / r0m;

        double c = 0.5, s = 1.0 / 6, z = 0;
        var ok = false;
        var lastD = double.MaxValue;
        for (var it = 0; it < 60; it++)
        {
            z = alpha * chi * chi;
            Stumpff(z, out c, out s);
            var chi2 = chi * chi;
            var f = rv * chi2 * c + k * chi2 * chi * s + r0m * chi - sqmu * dt;
            var fp = rv * chi * (1 - z * s) + k * chi2 * c + r0m; // = the radius at chi
            var fpp = rv * (1 - z * c) + k * chi * (1 - z * s);
            const double n = 5;
            var disc = Math.Sqrt(Math.Abs((n - 1) * (n - 1) * fp * fp - n * (n - 1) * f * fpp));
            var den = fp + (fp >= 0 ? disc : -disc);
            if (den == 0 || !double.IsFinite(den))
                break;
            var d = n * f / den;
            chi -= d;
            // Converged when the step is at rounding level (f itself is only good to a few ulps of sqrt(mu) dt), or
            // when it stops shrinking once already tiny.
            var ad = Math.Abs(d);
            if (ad <= 1e-14 * Math.Max(1, Math.Abs(chi)) || (ad >= lastD && ad <= 1e-10 * Math.Max(1, Math.Abs(chi))))
            {
                ok = true;
                break;
            }
            lastD = ad;
        }
        if (!ok || !double.IsFinite(chi))
        {
            // Fallback: f rises with chi (its slope is the radius), so bracket the root and bisect.
            if (!Bisect(sqmu, rv, k, r0m, alpha, dt, out chi))
            {
                r = r0;
                v = v0;
                return false;
            }
        }
        z = alpha * chi * chi;
        Stumpff(z, out c, out s);
        var x2 = chi * chi;
        var fl = 1 - x2 / r0m * c;
        var gl = dt - x2 * chi / sqmu * s;
        r = r0 * fl + v0 * gl;
        var rm = r.Length;
        var fdot = sqmu / (rm * r0m) * chi * (z * s - 1);
        var gdot = 1 - x2 / rm * c;
        v = r0 * fdot + v0 * gdot;
        return true;
    }

    private static double F(double sqmu, double rv, double k, double r0m, double alpha, double dt, double chi)
    {
        Stumpff(alpha * chi * chi, out var c, out var s);
        var chi2 = chi * chi;
        return rv * chi2 * c + k * chi2 * chi * s + r0m * chi - sqmu * dt;
    }

    private static bool Bisect(double sqmu, double rv, double k, double r0m, double alpha, double dt, out double chi)
    {
        double lo = 0, hi = 0, step = Math.Max(1e-6, Math.Abs(sqmu * dt / r0m));
        if (dt > 0)
            for (var n = 0; n < 200 && !(F(sqmu, rv, k, r0m, alpha, dt, hi) > 0); n++, step *= 2)
                hi += step;
        else
            for (var n = 0; n < 200 && !(F(sqmu, rv, k, r0m, alpha, dt, lo) < 0); n++, step *= 2)
                lo -= step;
        chi = 0.5 * (lo + hi);
        for (var n = 0; n < 200; n++)
        {
            chi = 0.5 * (lo + hi);
            if (chi <= lo || chi >= hi)
                break;
            var f = F(sqmu, rv, k, r0m, alpha, dt, chi);
            if (!double.IsFinite(f))
                return false;
            if (f > 0)
                hi = chi;
            else
                lo = chi;
        }
        return double.IsFinite(chi);
    }
}
