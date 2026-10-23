namespace CallOfSlop.MapKit;

/// <summary>
/// Walkable structures built from hidden colliders and cosmetic meshes, with the dimensions the movers and the self-tests
/// need (Maps/README.md "Void maps", the "Stairs" and "Slopes" sections): railings are a collider band 0.45-1.1 m above
/// the deck (bullets, grenades and prone shots go under, people don't) drawn as panels and posts; stairs have risers of
/// at most 0.55 m (the step-up height) and at least 0.05 m, treads of 0.15-1.6 m and a clear width of at least 1.0 m;
/// ramps and gangways are chains of colliders stepping up at most 4 cm at a time under a drawn sloped slab.
/// </summary>
public static class Structures
{
    public const double RailLow = 0.45, RailHigh = 1.1;
    public const double StepMax = 0.55, StepMin = 0.05, TreadMin = 0.15, TreadMax = 1.6, FlightWidthMin = 1.0, LandingMin = 1.2, Headroom = 2.1;
    public const double RampStep = 0.04;

    public static void CheckFlightWidth(double width, string what)
    {
        if (width < FlightWidthMin - 1e-6)
            throw new MapKitException($"{what}: {width:0.00} m wide, flights must be at least {FlightWidthMin} m clear (the Stairs rule)");
    }

    /// <summary>
    /// A box mesh <paramref name="length"/> along the unit direction (dx, dz), <paramref name="width"/> across, whose top
    /// face's centre is at (cx, ytop, cz), tilted so its top rises by <paramref name="rise"/> over its length.
    /// </summary>
    public static void Slab(Kit k, double cx, double cz, double ytop, double length, double width, double thick, double dx, double dz, double rise, Color color, string? material = "Metal")
    {
        var yaw = Geometry.YawAlong(dx, dz);
        var roll = Math.Atan2(rise, length) * 180 / Math.PI;
        var top = Geometry.Turn(new System.Numerics.Vector3(0, (float)thick, 0), yaw, 0, roll);
        k.Mesh("box", cx - top.X, cz - top.Z, ytop - top.Y, color, width: length / Math.Cos(roll * Math.PI / 180) + 0.02, height: thick, depth: width, yaw: yaw, roll: roll, material: material);
    }

    /// <summary>Hidden collider band along a line (heights ya to yb), in short pieces so each box stays thin.</summary>
    public static void RailColliders(Kit k, double x0, double z0, double ya, double x1, double z1, double yb, double piece = 0.7, double t = 0.12)
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        var n = Math.Max(1, (int)Math.Ceiling(len / piece));
        if (Math.Abs(x1 - x0) < 0.01 || Math.Abs(z1 - z0) < 0.01)
            n = Math.Max(1, (int)Math.Ceiling(Math.Abs(yb - ya) / 0.15)); // axis-aligned: only cut where the height changes
        for (var i = 0; i < n; i++)
        {
            double f0 = (double)i / n, f1 = (double)(i + 1) / n;
            double ax = x0 + (x1 - x0) * f0, az = z0 + (z1 - z0) * f0, bx = x0 + (x1 - x0) * f1, bz = z0 + (z1 - z0) * f1;
            var y = ya + (yb - ya) * (f0 + f1) / 2;
            k.Collider(Math.Min(ax, bx) - t / 2, y + RailLow, Math.Min(az, bz) - t / 2, Math.Max(ax, bx) + t / 2, y + RailHigh, Math.Max(az, bz) + t / 2);
        }
    }

    /// <summary>The drawn part of a railing: a panel, a top rail and posts.</summary>
    public static void RailVisual(Kit k, double x0, double z0, double ya, double x1, double z1, double yb, Color panel, Color top, Color post, double postEvery = 2.5)
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        if (len < 0.2)
            return;
        double dx = (x1 - x0) / len, dz = (z1 - z0) / len, cx = (x0 + x1) / 2, cz = (z0 + z1) / 2, ym = (ya + yb) / 2;
        Slab(k, cx, cz, ym + 1.0, len, 0.05, 0.55, dx, dz, yb - ya, panel, material: "");
        Slab(k, cx, cz, ym + 1.12, len, 0.14, 0.1, dx, dz, yb - ya, top, material: "Metal");
        var n = Math.Max(1, (int)Math.Round(len / postEvery));
        for (var i = 0; i <= n; i++)
        {
            var f = (double)i / n;
            k.Mesh("cylinder", x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, ya + (yb - ya) * f, post, width: 0.1, height: 1.12, depth: 0.1, segments: 8);
        }
    }

    /// <summary>
    /// A railing from (x0, z0) to (x1, z1) (deck heights ya to yb), with <paramref name="gaps"/> ((distance along, half
    /// width), ...) left open where bridges and stairs meet it.
    /// </summary>
    public static void Railing(Kit k, double x0, double z0, double ya, double x1, double z1, double? yb = null, Color? panel = null, Color? top = null, Color? post = null,
        double postEvery = 2.5, IEnumerable<(double At, double HalfWidth)>? gaps = null)
    {
        var yEnd = yb ?? ya;
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        if (len < 0.05)
            return;
        var cuts = new List<(double A, double B)> { (0, len) };
        foreach (var (at, hw) in gaps ?? [])
        {
            var next = new List<(double, double)>();
            foreach (var (a, b) in cuts)
            {
                double g0 = at - hw, g1 = at + hw;
                if (g1 <= a || g0 >= b)
                {
                    next.Add((a, b));
                    continue;
                }
                if (g0 > a) next.Add((a, g0));
                if (g1 < b) next.Add((g1, b));
            }
            cuts = next;
        }
        foreach (var (a, b) in cuts)
        {
            if (b - a < 0.15)
                continue;
            double fa = a / len, fb = b / len;
            double ax = x0 + (x1 - x0) * fa, az = z0 + (z1 - z0) * fa, ay = ya + (yEnd - ya) * fa;
            double bx = x0 + (x1 - x0) * fb, bz = z0 + (z1 - z0) * fb, by = ya + (yEnd - ya) * fb;
            RailVisual(k, ax, az, ay, bx, bz, by, panel ?? "#b8ad99", top ?? "#8d7458", post ?? "#6c6f73", postEvery);
            RailColliders(k, ax, az, ay, bx, bz, by);
        }
    }

    /// <summary>
    /// A straight flight climbing <paramref name="rise"/> from its foot at (x, z, y0) along the axis-aligned unit direction
    /// (dx, dz): one hidden collider and one instanced mesh box per step (all the same size: one mesh, drawn instanced), a
    /// stringer each side and waypoints at the foot, the middle and the top. Risers and treads are checked against the
    /// movers' rules. Returns (x, z, y) where the top landing starts.
    /// </summary>
    public static (double X, double Z, double Y) Stairs(Kit k, double x, double z, double y0, double rise, double dx, double dz, double width = 1.6, int? steps = null,
        double riser = 0.3, double tread = 0.32, Color? color = null, Color? side = null, bool waypoints = true)
    {
        CheckFlightWidth(width, "stairs");
        var n = steps ?? (int)Math.Round(rise / riser);
        if (n < 1)
            throw new MapKitException("stairs: no steps");
        riser = rise / n;
        if (riser > StepMax + 1e-6 || riser < StepMin - 1e-6)
            throw new MapKitException($"stairs: {riser:0.000} m risers; the step-up rule allows {StepMin} to {StepMax} m");
        if (tread < TreadMin - 1e-6 || tread > TreadMax + 1e-6)
            throw new MapKitException($"stairs: {tread:0.00} m treads; the flight detector wants {TreadMin} to {TreadMax} m");
        if (Math.Abs(dx) > 1e-9 && Math.Abs(dz) > 1e-9)
            throw new MapKitException("stairs: the direction must be along X or Z");
        double px = -dz, pz = dx, hw = width / 2;
        var col = color ?? "#8c8a86";
        for (var i = 0; i < n; i++)
        {
            double a = i * tread, b = (i + 1) * tread, top = y0 + (i + 1) * riser;
            double ax = x + dx * a, az = z + dz * a, bx = x + dx * b, bz = z + dz * b;
            double x0 = Math.Min(ax, bx) - Math.Abs(px) * hw, x1 = Math.Max(ax, bx) + Math.Abs(px) * hw;
            double z0 = Math.Min(az, bz) - Math.Abs(pz) * hw, z1 = Math.Max(az, bz) + Math.Abs(pz) * hw;
            k.Collider(x0, top - riser - 0.02, z0, x1, top, z1);
            k.MeshAt("box", (ax + bx) / 2, top - riser / 2 - 0.01, (az + bz) / 2, col, width: Math.Abs(dx) > 0.5 ? tread + 0.02 : width,
                height: riser + 0.02, depth: Math.Abs(dx) > 0.5 ? width : tread + 0.02, material: "Metal");
        }
        var len = n * tread;
        foreach (var s in new[] { -1, 1 })
            Slab(k, x + dx * len / 2 + px * s * (hw + 0.04), z + dz * len / 2 + pz * s * (hw + 0.04), y0 + rise / 2 + 0.2, len + 0.3, 0.08, 0.5, dx, dz, rise, side ?? "#5e574c");
        if (waypoints)
        {
            k.Waypoint(x - dx * 0.8, z - dz * 0.8, y0);
            k.Waypoint(x + dx * len / 2, z + dz * len / 2, y0 + rise / 2);
            k.Waypoint(x + dx * (len + 0.8), z + dz * (len + 0.8), y0 + rise);
        }
        return (x + dx * len, z + dz * len, y0 + rise);
    }

    /// <summary>
    /// Hidden deck colliders from (x0, z0) at height y0 to (x1, z1) at y1, <paramref name="width"/> wide, in pieces that
    /// step up at most <see cref="RampStep"/> (a ramp, not a staircase, to the stairs audit). Axis-aligned.
    /// </summary>
    public static void RampColliders(Kit k, double x0, double z0, double y0, double x1, double z1, double y1, double width, double thick = 0.5)
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        if (len < 0.05)
            return;
        double ux = (x1 - x0) / len, uz = (z1 - z0) / len;
        if (Math.Abs(ux) > 0.01 && Math.Abs(uz) > 0.01)
            throw new MapKitException("ramp colliders must run along X or Z");
        double px = -uz, pz = ux, hw = width / 2;
        var n = Math.Max(1, (int)Math.Ceiling(Math.Abs(y1 - y0) / RampStep));
        for (var i = 0; i < n; i++)
        {
            double f0 = (double)i / n, f1 = (double)(i + 1) / n;
            double ax = x0 + (x1 - x0) * f0, az = z0 + (z1 - z0) * f0, bx = x0 + (x1 - x0) * f1, bz = z0 + (z1 - z0) * f1;
            var y = Math.Min(y0 + (y1 - y0) * f0, y0 + (y1 - y0) * f1); // the lower end: the drawn slope stays on top
            k.Collider(Math.Min(ax, bx) - Math.Abs(px) * hw, y - thick, Math.Min(az, bz) - Math.Abs(pz) * hw, Math.Max(ax, bx) + Math.Abs(px) * hw, y, Math.Max(az, bz) + Math.Abs(pz) * hw);
        }
    }

    /// <summary>
    /// A sloped walkway (axis-aligned) from (x0, y0, z0) to (x1, y1, z1): hidden colliders stepping up under a drawn
    /// sloped slab, railings both sides and waypoints along it. Checks the slope stays walkable (under 30 degrees).
    /// </summary>
    public static void Gangway(Kit k, double x0, double z0, double y0, double x1, double z1, double y1, double width, Color? color = null, bool rails = true,
        string material = "Grid", bool waypoints = true)
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        var rise = y1 - y0;
        if (Math.Abs(rise) / Math.Max(len, 1e-6) > Math.Tan(30 * Math.PI / 180))
            throw new MapKitException($"gangway: {Math.Atan2(Math.Abs(rise), len) * 180 / Math.PI:0} degrees is too steep (comfortable ramps are 15-30)");
        double ux = (x1 - x0) / len, uz = (z1 - z0) / len, px = -uz, pz = ux, hw = width / 2;
        RampColliders(k, x0, z0, y0, x1, z1, y1, width, 0.45);
        Slab(k, (x0 + x1) / 2, (z0 + z1) / 2, (y0 + y1) / 2 + 0.02, len + 0.4, width, 0.3, ux, uz, rise, color ?? "#8c8a86", material);
        if (rails)
            foreach (var s in new[] { -1, 1 })
                Railing(k, x0 + px * s * (hw - 0.08), z0 + pz * s * (hw - 0.08), y0, x1 + px * s * (hw - 0.08), z1 + pz * s * (hw - 0.08), y1);
        if (waypoints)
        {
            var m = Math.Max(1, (int)Math.Ceiling(len / 4.0));
            for (var i = 0; i <= m; i++)
            {
                var f = (double)i / m;
                k.Waypoint(x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, y0 + rise * f);
            }
        }
    }

    /// <summary>
    /// A level catwalk or bridge deck between two points (any direction): a drawn slab over hidden colliders (diagonal
    /// decks are cut into thin slices), railings both sides unless <paramref name="rails"/> is false, waypoints every 4 m.
    /// </summary>
    public static void Catwalk(Kit k, double x0, double z0, double x1, double z1, double y, double width, Color? color = null, double thick = 0.25, bool rails = true, string material = "Grid")
    {
        var len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        if (len < 0.05)
            return;
        double ux = (x1 - x0) / len, uz = (z1 - z0) / len, px = -uz, pz = ux, hw = width / 2;
        if (Math.Abs(ux) < 0.01 || Math.Abs(uz) < 0.01)
            k.Collider(Math.Min(x0, x1) - Math.Abs(px) * hw, y - thick, Math.Min(z0, z1) - Math.Abs(pz) * hw, Math.Max(x0, x1) + Math.Abs(px) * hw, y, Math.Max(z0, z1) + Math.Abs(pz) * hw);
        else
        {
            // Diagonal: slices across the dominant axis, each the part of the strip inside it.
            var alongX = Math.Abs(ux) >= Math.Abs(uz);
            double lo = alongX ? Math.Min(x0, x1) : Math.Min(z0, z1), hi = alongX ? Math.Max(x0, x1) : Math.Max(z0, z1);
            var n = Math.Max(1, (int)Math.Ceiling((hi - lo) / 0.3));
            var half = (hw - 0.04) / (alongX ? Math.Abs(ux) : Math.Abs(uz));
            for (var i = 0; i < n; i++)
            {
                double a = lo + (hi - lo) * i / n, b = lo + (hi - lo) * (i + 1) / n;
                double Cross(double v)
                {
                    var f = (v - (alongX ? x0 : z0)) / (alongX ? x1 - x0 : z1 - z0);
                    return alongX ? z0 + (z1 - z0) * f : x0 + (x1 - x0) * f;
                }
                double c0 = Math.Max(Cross(a), Cross(b)) - half, c1 = Math.Min(Cross(a), Cross(b)) + half;
                if (c1 - c0 < 0.3)
                    continue;
                if (alongX) k.Collider(a, y - thick, c0, b, y, c1);
                else k.Collider(c0, y - thick, a, c1, y, b);
            }
        }
        Slab(k, (x0 + x1) / 2, (z0 + z1) / 2, y + 0.02, len + 0.4, width, thick, ux, uz, 0, color ?? "#8c8a86", material);
        if (rails)
            foreach (var s in new[] { -1, 1 })
                Railing(k, x0 + px * s * (hw - 0.08), z0 + pz * s * (hw - 0.08), y, x1 + px * s * (hw - 0.08), z1 + pz * s * (hw - 0.08), y);
        var m = Math.Max(1, (int)Math.Ceiling(len / 4.0));
        for (var i = 0; i <= m; i++)
        {
            var f = (double)i / m;
            k.Waypoint(x0 + (x1 - x0) * f, z0 + (z1 - z0) * f, y);
        }
    }

    /// <summary>
    /// A lane: cover dropped along a path at a spacing, alternating sides, each piece set back <paramref name="offset"/>
    /// metres and turned to follow the path. <paramref name="place"/> gets (x, z, yaw, index) and places the piece (a prop,
    /// a block...). Skips the first and last <paramref name="margin"/> metres so the ends stay open.
    /// </summary>
    public static int Lane(IReadOnlyList<(double X, double Z)> path, double spacing, double offset, Action<double, double, double, int> place, double margin = 2.0, bool alternate = true)
    {
        var pts = Geometry.Along(path, spacing, margin);
        var total = 0.0;
        for (var i = 0; i + 1 < path.Count; i++)
            total += Math.Sqrt((path[i + 1].X - path[i].X) * (path[i + 1].X - path[i].X) + (path[i + 1].Z - path[i].Z) * (path[i + 1].Z - path[i].Z));
        var placed = 0;
        var walked = margin;
        foreach (var (x, z) in pts)
        {
            if (walked > total - margin)
                break;
            // The path direction here: the nearest segment.
            var (dx, dz) = DirectionAt(path, x, z);
            var side = alternate && placed % 2 == 1 ? -1 : 1;
            place(x - dz * offset * side, z + dx * offset * side, Geometry.YawFacing(dx, dz), placed);
            placed++;
            walked += spacing;
        }
        return placed;
    }

    private static (double, double) DirectionAt(IReadOnlyList<(double X, double Z)> path, double x, double z)
    {
        var best = double.MaxValue;
        var dir = (1.0, 0.0);
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var (ax, az) = path[i];
            var (bx, bz) = path[i + 1];
            double dx = bx - ax, dz = bz - az, ll = dx * dx + dz * dz;
            var t = ll == 0 ? 0 : Math.Clamp(((x - ax) * dx + (z - az) * dz) / ll, 0, 1);
            var d = (ax + t * dx - x) * (ax + t * dx - x) + (az + t * dz - z) * (az + t * dz - z);
            if (d < best)
            {
                best = d;
                var len = Math.Sqrt(ll);
                dir = len == 0 ? (1.0, 0.0) : (dx / len, dz / len);
            }
        }
        return dir;
    }
}
