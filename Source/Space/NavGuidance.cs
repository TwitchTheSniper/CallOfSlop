namespace CallOfSlop.Space;

/// <summary>
/// The nav computer's steering in space (docs/space/COMMAND.md "How a ship flies an order"): pure functions from a
/// ship's place and speed to the acceleration it should ask of its drive. Everything is relative to one body (the order's):
/// <c>r</c> and <c>v</c> are the ship's offset and velocity from that body, in the ecliptic's axes. Two laws:
/// <list type="bullet">
/// <item><see cref="Orbit"/>: a velocity field that ends in a circle of a given radius. At any radius it asks for the
/// circular speed there, round the way the ship already goes, plus a climb or sink towards the wanted radius no faster
/// than the drive can stop; the missing centripetal pull is fed forward, so a ship slower than the circle is held up while
/// it gathers speed. On the circle it asks for nothing: gravity does the turning.</item>
/// <item><see cref="GoTo"/>: fly at a point as fast as the drive can still stop from, round any body in the way
/// (<see cref="Avoid"/>), with the pull of gravity cancelled, so it arrives still and stays.</item>
/// </list>
/// The caller turns the drive towards the answer and throttles it (Game/GameNav.cs). No allocation.
/// </summary>
public static class NavGuidance
{
    /// <summary>How quickly a speed error is taken out (s): well over a thruster's spool, or the loop chases its own lag.</summary>
    public const double Tau = 4;

    /// <summary>The share of the drive's push the approach speed is planned on (the rest is margin for gravity and turning).</summary>
    public const double BrakeShare = 0.35;

    /// <summary>A speed error under this asks for nothing (m/s): an engine that can't throttle to zero mustn't hunt.</summary>
    public const double SpeedDeadband = 0.15;

    /// <summary>
    /// The acceleration to ask for (ecliptic axes) to end on a circle of <paramref name="radius"/> round a body of
    /// <paramref name="mu"/>. <paramref name="settled"/>: on the circle, within <paramref name="radius"/> / 200 (at least
    /// 20 m) and half a metre a second, so the drive can stop.
    /// </summary>
    public static Vec3d Orbit(Vec3d r, Vec3d v, double mu, double radius, double aMax, out bool settled, out double speedError)
    {
        settled = false;
        speedError = 0;
        var dist = r.Length;
        if (!(dist > 1) || !(aMax > 0))
            return Vec3d.Zero;
        var up = r / dist;
        var normal = OrbitNormal(up, r, v, mu);
        var along = Vec3d.Cross(normal, up); // (the way round)
        var circle = Math.Sqrt(mu / dist);
        var off = radius - dist;
        var climb = Math.Sign(off) * Math.Min(Math.Abs(off) / (5 * Tau), Math.Sqrt(2 * BrakeShare * aMax * Math.Abs(off)));
        var want = along * circle + up * climb;
        var dv = want - v;
        speedError = dv.Length;
        var round = Vec3d.Dot(v, along);
        var hold = mu / (dist * dist) - round * round / dist; // (what gravity pulls that its speed round doesn't use)
        settled = Math.Abs(off) < Math.Max(20, radius / 200) && speedError < 0.5;
        if (settled || speedError < SpeedDeadband && Math.Abs(hold) < 0.02 * aMax)
            return Vec3d.Zero;
        return dv / Tau + up * hold;
    }

    /// <summary>
    /// The plane an orbit is flown in (its normal, square to <paramref name="up"/>): the ship's own while it goes round at
    /// all (a tenth of the circular speed), else the one nearest the ecliptic, anticlockwise seen from its north.
    /// </summary>
    public static Vec3d OrbitNormal(Vec3d up, Vec3d r, Vec3d v, double mu)
    {
        var h = Vec3d.Cross(r, v);
        var dist = r.Length;
        if (h.Length > 0.1 * Math.Sqrt(mu * dist))
            return h.Normalized();
        var n = Vec3d.UnitZ - up * Vec3d.Dot(Vec3d.UnitZ, up);
        if (n.Length < 0.05)
            n = Vec3d.UnitX - up * Vec3d.Dot(Vec3d.UnitX, up); // (straight over the pole: any plane will do)
        return n.Normalized();
    }

    /// <summary>
    /// The acceleration to ask for (ecliptic axes) to arrive at <paramref name="to"/> (from the body, as <paramref name="r"/>)
    /// with speed <paramref name="arrive"/> along the way there (0: stop). <paramref name="gravity"/> is what pulls the
    /// ship as seen from the body (its pull less the body's own fall): it is cancelled. <paramref name="way"/> is the
    /// direction to fly (unit; the caller bends it round bodies with <see cref="Avoid"/>).
    /// </summary>
    public static Vec3d GoTo(Vec3d r, Vec3d v, Vec3d to, Vec3d way, double arrive, Vec3d gravity, double aMax, out double distance, out double speedError)
    {
        distance = (to - r).Length;
        speedError = 0;
        if (!(aMax > 0))
            return Vec3d.Zero;
        var speed = Math.Sqrt(arrive * arrive + 2 * BrakeShare * aMax * distance);
        if (distance < 60)
            speed = Math.Min(speed, arrive + distance / Tau); // (the last metres: ease in, don't overshoot)
        var dv = way * speed - v;
        speedError = dv.Length;
        var a = dv / Tau - gravity;
        return speedError < SpeedDeadband && gravity.Length < 0.002 * aMax ? Vec3d.Zero : a;
    }

    /// <summary>
    /// Bends the way from <paramref name="from"/> to <paramref name="to"/> round a ball at <paramref name="centre"/> the
    /// ship must stay <paramref name="keep"/> out of: when the straight line would pass inside it, the way goes to the
    /// ball's edge on the side the line already leans to. Returns the way to fly (unit). All in one frame.
    /// </summary>
    public static Vec3d Avoid(Vec3d from, Vec3d to, Vec3d way, Vec3d centre, double keep)
    {
        var w = centre - from;
        var gap = w.Length;
        var ahead = Vec3d.Dot(w, way);
        var reach = (to - from).Length;
        keep *= 1.08;
        if (gap < keep)
        {
            // Inside the ball already: out first, and round.
            var outward = gap > 1 ? -w / gap : way;
            var side = way - outward * Vec3d.Dot(way, outward);
            return (outward + (side.Length > 0.05 ? side.Normalized() * 0.5 : Vec3d.Zero)).Normalized();
        }
        if (ahead <= 0 || ahead >= reach)
            return way;
        var miss = w - way * ahead; // (from the line's nearest point to the centre)
        if (miss.Length >= keep)
            return way;
        var toC = w / gap;
        var lean = miss.Length > 1 ? -miss / miss.Length : Vec3d.Cross(Vec3d.UnitZ, way).Normalized();
        var across = lean - toC * Vec3d.Dot(lean, toC);
        if (across.Length < 1e-6)
            return way;
        across = across.Normalized();
        var sin = Math.Min(1, keep / gap);
        var cos = Math.Sqrt(1 - sin * sin);
        return (toC * cos + across * sin).Normalized();
    }

    /// <summary>Gravity at a place in a system at a time: the pull of every body there (m/s2, ecliptic axes).</summary>
    public static Vec3d Gravity(StarSystem sys, SpacePos at, SpaceTime t, int except = -1)
    {
        var a = Vec3d.Zero;
        for (var b = 0; b < sys.Count; b++)
        {
            if (b == except)
                continue;
            var d = sys.PositionOf(b, t) - at;
            var d2 = d.LengthSquared;
            if (d2 > 1)
                a += d * (sys.Bodies[b].Mu / (d2 * Math.Sqrt(d2)));
        }
        return a;
    }

    /// <summary>The smallest body whose sphere of influence holds a place (the frame a path there is best drawn in).</summary>
    public static int PrimaryAt(StarSystem sys, SpacePos at, SpaceTime t)
    {
        var best = 0;
        var bestSoi = double.PositiveInfinity;
        for (var b = 1; b < sys.Count; b++)
        {
            var soi = SpaceShips.SphereOfInfluence(sys, b);
            if (soi < bestSoi && (sys.PositionOf(b, t) - at).Length < soi)
            {
                best = b;
                bestSoi = soi;
            }
        }
        return best;
    }
}
