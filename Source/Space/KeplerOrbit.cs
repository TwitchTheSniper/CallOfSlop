namespace CallOfSlop.Space;

/// <summary>
/// A closed (elliptic or circular) Kepler orbit about a parent of gravitational parameter <see cref="Mu"/>: the
/// "rails" a body moves on (docs/space/SPACE.md, the doc's tier 3, ORB). Positions are exact functions of time, so a
/// planet on rails never drifts however long it runs. Coordinates are the parent's inertial frame, axes along the
/// system's ecliptic frame (x towards zero longitude, z the ecliptic's north).
/// </summary>
public readonly struct KeplerOrbit
{
    /// <summary>Parent's GM in m^3/s^2.</summary>
    public readonly double Mu;
    /// <summary>Semi-major axis, metres.</summary>
    public readonly double A;
    /// <summary>Eccentricity, 0 to below 1.</summary>
    public readonly double E;
    /// <summary>Inclination, longitude of the ascending node and argument of periapsis (radians).</summary>
    public readonly double I, Lan, ArgP;
    /// <summary>Mean anomaly at <see cref="Epoch"/> (radians).</summary>
    public readonly double M0;
    public readonly SpaceTime Epoch;

    /// <summary>Mean motion (rad/s) and period (s).</summary>
    public readonly double N, Period;

    /// <summary>The perifocal frame's axes in the parent's frame: P towards periapsis, Q 90 degrees on in the motion, W the orbit's normal.</summary>
    public readonly Vec3d P, Q, W;

    /// <summary>The perifocal frame as a rotation (Rz(lan) Rx(i) Rz(argp)).</summary>
    public readonly QuatD Rotation;

    public KeplerOrbit(double mu, double a, double e, double i, double lan, double argp, double m0, SpaceTime epoch = default)
    {
        if (!(mu > 0) || !(a > 0) || !(e >= 0 && e < 1) || !double.IsFinite(i + lan + argp + m0))
            throw new ArgumentException($"not a closed orbit: mu {mu}, a {a}, e {e}");
        Mu = mu; A = a; E = e; I = i; Lan = lan; ArgP = argp; M0 = m0; Epoch = epoch;
        N = Math.Sqrt(mu / (a * a * a));
        Period = 2 * Math.PI / N;
        Rotation = (QuatD.RotZ(lan) * QuatD.RotX(i) * QuatD.RotZ(argp)).Normalized();
        P = Rotation.Rotate(Vec3d.UnitX);
        Q = Rotation.Rotate(Vec3d.UnitY);
        W = Rotation.Rotate(Vec3d.UnitZ);
    }

    public double Periapsis => A * (1 - E);
    public double Apoapsis => A * (1 + E);

    /// <summary>Specific orbital energy (J/kg): -mu / 2a.</summary>
    public double Energy => -Mu / (2 * A);

    /// <summary>Mean anomaly at a time, in (-pi, pi]: whole periods are taken off before the multiply, so it stays exact for centuries.</summary>
    public double MeanAnomalyAt(SpaceTime t)
    {
        return WrapPi(M0 + N * PhaseSeconds(t.Ns - Epoch.Ns, Period));
    }

    /// <summary>
    /// <paramref name="ns"/> nanoseconds less the nearest whole number of periods, in seconds (within half a period).
    /// The period is split into whole and fractional nanoseconds so the subtraction is exact in integers: a double count
    /// of seconds would lose a few millimetres of orbit a few decades in.
    /// </summary>
    public static double PhaseSeconds(long ns, double periodSeconds)
    {
        var pNs = periodSeconds * SpaceTime.NsPerSecond;
        if (!(pNs >= 1) || pNs > 4e18)
            return ns / (double)SpaceTime.NsPerSecond;
        var whole = Math.Floor(pNs);
        var frac = pNs - whole;
        var k = (long)Math.Round(ns / pNs);
        var rem = (double)(ns - k * (long)whole) - k * frac;
        return rem / SpaceTime.NsPerSecond;
    }

    /// <summary>Solves Kepler's equation E - e sin E = M for the eccentric anomaly (Newton, to 1e-15 rad).</summary>
    public static double EccentricAnomaly(double m, double e)
    {
        m = WrapPi(m);
        var ea = e < 0.8 ? m + e * Math.Sin(m) : Math.PI * Math.Sign(m == 0 ? 1 : m);
        for (var k = 0; k < 50; k++)
        {
            var (s, c) = Math.SinCos(ea);
            var f = ea - e * s - m;
            var d = f / (1 - e * c);
            ea -= d;
            if (Math.Abs(d) < 1e-15)
                break;
        }
        return ea;
    }

    /// <summary>Position (m) and velocity (m/s) relative to the parent at a time.</summary>
    public void StateAt(SpaceTime t, out Vec3d pos, out Vec3d vel)
    {
        var ea = EccentricAnomaly(MeanAnomalyAt(t), E);
        var (s, c) = Math.SinCos(ea);
        var b = Math.Sqrt(1 - E * E);
        var x = A * (c - E);
        var y = A * b * s;
        var k = N * A / (1 - E * c);
        pos = P * x + Q * y;
        vel = P * (-s * k) + Q * (b * c * k);
    }

    public Vec3d PositionAt(SpaceTime t)
    {
        StateAt(t, out var p, out _);
        return p;
    }

    /// <summary>
    /// The orbit through a state (position and velocity relative to the parent) at a time. Equatorial orbits take the
    /// node at zero longitude; circular ones put periapsis at the node. Refused when the state isn't on a closed orbit.
    /// </summary>
    public static KeplerOrbit FromState(double mu, Vec3d r, Vec3d v, SpaceTime t)
    {
        var rl = r.Length;
        var h = Vec3d.Cross(r, v);
        var hl = h.Length;
        var energy = v.LengthSquared / 2 - mu / rl;
        if (!(energy < 0) || !(hl > 0))
            throw new ArgumentException("not a closed orbit (escape speed or straight up)");
        var a = -mu / (2 * energy);
        var ev = Vec3d.Cross(v, h) / mu - r / rl;
        var e = ev.Length;
        var i = Math.Acos(Math.Clamp(h.Z / hl, -1, 1));
        var node = new Vec3d(-h.Y, h.X, 0); // z x h
        var nl = node.Length;
        const double tiny = 1e-11;
        var equatorial = nl < tiny * hl;
        var circular = e < tiny;
        var lan = equatorial ? 0 : Math.Atan2(node.Y, node.X);
        var nodeDir = equatorial ? Vec3d.UnitX : node / nl;
        var inPlane = Vec3d.Cross(h / hl, nodeDir); // 90 degrees on from the node in the motion
        double argp, nu;
        if (circular)
        {
            argp = 0;
            nu = Math.Atan2(Vec3d.Dot(r, inPlane), Vec3d.Dot(r, nodeDir));
            e = 0;
        }
        else
        {
            argp = Math.Atan2(Vec3d.Dot(ev, inPlane), Vec3d.Dot(ev, nodeDir));
            var pDir = ev / e;
            var qDir = Vec3d.Cross(h / hl, pDir);
            nu = Math.Atan2(Vec3d.Dot(r, qDir), Vec3d.Dot(r, pDir));
        }
        var ea = 2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
        var m = ea - e * Math.Sin(ea);
        return new KeplerOrbit(mu, a, e, i, lan, argp, m, t);
    }

    /// <summary>An angle wrapped to (-pi, pi].</summary>
    public static double WrapPi(double a)
    {
        a = Math.IEEERemainder(a, 2 * Math.PI);
        return a <= -Math.PI ? a + 2 * Math.PI : a;
    }
}
