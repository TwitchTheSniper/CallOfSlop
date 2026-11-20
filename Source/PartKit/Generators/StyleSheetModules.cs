namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Style sheet step 3, five 1x1 module drafts A to E (magazines 2x2), each on the standard Module Plug of Twitch's module
/// sockets (docs/sandbox/design/module-sockets.md) with the glands of the couplings its kind takes. Round 4 (Karl, 2026-11-20:
/// "more that the parts would be like you'd expect on a ship, and less cheesy"): plain working ship equipment, cabinets,
/// tanks, pipework, racks and placards, light only in small indicator lamps and gauge faces.
/// </summary>
public sealed partial class StyleSheetParts
{
    private const double W = 0.376; // (the battery's half width: inside the plate's corner lugs)
    private const string RedPaint = "#b8402f";

    /// <summary>A module draft on its plug, with the glands of the couplings its kind takes (power crosses through the frame).</summary>
    private static Kit Mod(string item, string section, string letter, string title, string desc, double H, int n = 1)
    {
        string[] ports = section switch { Fuel => ["Fuel A", "Data 1"], Mags => ["Ammo", "Data 1"], _ => ["Data 1"] };
        var k = Reactor("", n, desc, H, $"Style {item} {letter} {title}", section, ports);
        if (n > 1)
            k.P.Stats(4000, 900 * n * n, 12, 6.0); // (under the by-hand limit, 5 t: module-sockets.md 6.1)
        return k;
    }

    private static (double X, double Y)[] Cut(double cx, double w, double y0, double y1, double c) =>
        [(cx - w + c, y0), (cx + w - c, y0), (cx + w, y0 + c), (cx + w, y1 - c), (cx + w - c, y1), (cx - w + c, y1), (cx - w, y1 - c), (cx - w, y0 + c)];

    /// <summary>Locker E's hatch: a cut-corner door on the face at z = zf, a neon seal round it, a raised panel and a release bar.</summary>
    private static void Ram(Kit k, double x, double z, double y0, double y1, double bx)
    {
        var ym = y0 + (y1 - y0) * 0.55;
        k.Box(bx - 0.01, y1 - 0.05, z - 0.04, bx + 0.01, y1 + 0.05, z + 0.04, Dark, "steel");
        k.Box(bx - 0.01, y0 - 0.05, z - 0.04, bx + 0.01, y0 + 0.05, z + 0.04, Dark, "steel");
        k.Hull(Cyl(x, z, 0.024, ym, y1, 8), Bare, "steel");
        k.Hull(Cyl(x, z, 0.012, y0, ym + 0.004, 8), Trim, "chrome");
    }

    /// <summary>A prism along x (a lying tank): centre (cy, cz), radius r.</summary>
    private static IEnumerable<double[]> CylX(double cy, double cz, double r, double x0, double x1, int sides = 8)
    {
        for (var i = 0; i < sides; i++)
        {
            var a = (i + 0.5) * 2 * Math.PI / sides;
            yield return [x0, cy + r * Math.Cos(a), cz + r * Math.Sin(a)];
            yield return [x1, cy + r * Math.Cos(a), cz + r * Math.Sin(a)];
        }
    }

    /// <summary>A ring of points in a plane of constant z (one end of a prism or frustum along z).</summary>
    private static IEnumerable<double[]> RingZ(double cx, double cy, double r, double z, int sides = 8)
    {
        for (var i = 0; i < sides; i++)
        {
            var a = (i + 0.5) * 2 * Math.PI / sides;
            yield return [cx + r * Math.Cos(a), cy + r * Math.Sin(a), z];
        }
    }

    /// <summary>A prism along z (a drum, a bottle, a port): centre (cx, cy), radius r.</summary>
    private static IEnumerable<double[]> CylZ(double cx, double cy, double r, double z0, double z1, int sides = 8) =>
        RingZ(cx, cy, r, z0, sides).Concat(RingZ(cx, cy, r, z1, sides));

    /// <summary>Points turned about the x axis through (py, pz) by <paramref name="a"/> radians (+: the front tips up).</summary>
    private static IEnumerable<double[]> TipX(IEnumerable<double[]> pts, double py, double pz, double a) =>
        pts.Select(q =>
        {
            var (y, z) = (q[1] - py, q[2] - pz);
            return new[] { q[0], py + y * Math.Cos(a) + z * Math.Sin(a), pz - y * Math.Sin(a) + z * Math.Cos(a) };
        });


    // ---- Step 3, round 2 (Karl, 2026-11-19: "add more variety to them, getting away from the generic bits such as the shape
    // and the power bar"): each variant has its own silhouette, not the battery block, and no pip strip. What stays shared is
    // the palette, one accent a part, locker E's sealed hatches and rams, locker A's hoods and pulls, used where they fit.

    /// <summary>An octagon in plan (a box with its upright corners cut by c), from y0 to y1.</summary>
    private static IEnumerable<double[]> Oct(double hx, double hz, double c, double y0, double y1)
    {
        foreach (var y in new[] { y0, y1 })
            foreach (var sx in new[] { -1.0, 1.0 })
                foreach (var sz in new[] { -1.0, 1.0 })
                {
                    yield return [sx * hx, y, sz * (hz - c)];
                    yield return [sx * (hx - c), y, sz * hz];
                }
    }

    /// <summary>A square frustum: half sizes (hx0, hz0) at y0 to (hx1, hz1) at y1.</summary>
    private static IEnumerable<double[]> Frustum(double hx0, double hz0, double hx1, double hz1, double y0, double y1)
    {
        foreach (var (hx, hz, y) in new[] { (hx0, hz0, y0), (hx1, hz1, y1) })
            foreach (var sx in new[] { -1.0, 1.0 })
                foreach (var sz in new[] { -1.0, 1.0 })
                    yield return [sx * hx, y, sz * hz];
    }

    /// <summary>A strut from p0 to p1, t thick (a square section across x and z: for upright or sloping members).</summary>
    private static IEnumerable<double[]> Brace(double[] p0, double[] p1, double t)
    {
        foreach (var p in new[] { p0, p1 })
            foreach (var dx in new[] { -t, t })
                foreach (var dz in new[] { -t, t })
                    yield return [p[0] + dx, p[1], p[2] + dz];
    }

    /// <summary>A round dial on a face at z = zf: dark bezel, lit face, a needle.</summary>
    private static void Dial(Kit k, double x, double y, double r, string col, double zf = W)
    {
        k.Hull(CylZ(x, y, r, zf - 0.004, zf + 0.014), Dark, "plastic");
        k.Hull(CylZ(x, y, r * 0.74, zf + 0.008, zf + 0.02), col, "", glow: true);
        k.Box(x - 0.006, y - 0.006, zf + 0.016, x + r * 0.6, y + 0.006, zf + 0.028, Trim, "steel");
    }

    /// <summary>One lamp in a dark bezel on a face at z = zf.</summary>
    private static void Lamp(Kit k, double x, double y, string col, double zf = W, double s = 0.03)
    {
        k.Box(x - s, y - s, zf - 0.004, x + s, y + s, zf + 0.012, Dark, "plastic");
        k.Glow(x - s * 0.6, y - s * 0.6, zf + 0.006, x + s * 0.6, y + s * 0.6, zf + 0.018, col);
    }

    /// <summary>Two skid rails along z under a part that stands on legs or a frame, chevrons on their front ends.</summary>
    private static void Skids(Kit k, double x = 0.3)
    {
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * (x - 0.03), 0.078, -0.33, sx * (x + 0.03), 0.13, 0.33, Body, "painted steel");
            k.Box(sx * (x - 0.03), 0.082, 0.326, sx * (x + 0.03), 0.126, 0.338, HazardY, "hazard paint");
        }
    }


    // ---- Round 4 (Karl, 2026-11-20: "more that the parts would be like you'd expect on a ship, and less cheesy"): working
    // ship equipment, not props. Cabinets with doors, hinges and latches, tanks with domed ends, flanged pipework, valves,
    // gauges, cable conduit, bushings and bus bars, racks and straps, stencilled placards. Light only where a real panel has
    // it: small indicator lamps and gauge faces. Pipes keep the usual service colours (fuel ochre, oxygen green, water blue).
    private const string Ceramic = "#d8d2c4", Copper = "#b8733a", Ochre = "#a8892f", O2 = "#3f7a50", Water = "#3a6fa8", White = "#d4d8db";

    /// <summary>A cabinet door on the face at z = zf: a rubber gasket, the leaf, two hinges and a handle on the latch side.</summary>
    private static void Door(Kit k, double x0, double x1, double y0, double y1, double zf, bool hingeLeft = true)
    {
        k.Box(x0 - 0.008, y0 - 0.008, zf - 0.006, x1 + 0.008, y1 + 0.008, zf + 0.006, Rubber, "rubber");
        k.Paint(x0, y0, zf - 0.002, x1, y1, zf + 0.018, Mid);
        var (hx, lx, s) = hingeLeft ? (x0, x1, -1.0) : (x1, x0, 1.0);
        foreach (var y in new[] { y0 + 0.06, y1 - 0.12 })
            k.Box(hx - 0.014, y, zf + 0.004, hx + 0.014, y + 0.06, zf + 0.03, Bare, "steel");
        var ym = (y0 + y1) / 2;
        k.Box(lx + s * 0.045 - 0.012, ym - 0.07, zf + 0.016, lx + s * 0.045 + 0.012, ym + 0.07, zf + 0.042, Bare, "steel");
        k.Box(lx + s * 0.045 - 0.02, ym - 0.08, zf + 0.014, lx + s * 0.045 + 0.02, ym - 0.05, zf + 0.026, Dark, "steel");
    }

    /// <summary>A vent grille on the face at z = zf: a dark recess and horizontal slats.</summary>
    private static void Grille(Kit k, double x0, double x1, double y0, double y1, double zf, int slats)
    {
        k.Box(x0, y0, zf - 0.01, x1, y1, zf + 0.003, Dark, "dirty steel");
        for (var i = 0; i < slats; i++)
        {
            var y = y0 + 0.015 + i * (y1 - y0 - 0.03) / Math.Max(1, slats - 1) - 0.007;
            k.Box(x0 + 0.008, y, zf - 0.004, x1 - 0.008, y + 0.014, zf + 0.012, Bare, "steel");
        }
    }

    /// <summary>A vent grille on a side face at x = sx * xf, slats running along z.</summary>
    private static void GrilleX(Kit k, double sx, double xf, double z0, double z1, double y0, double y1, int slats)
    {
        k.Box(sx * (xf - 0.01), y0, z0, sx * (xf + 0.003), y1, z1, Dark, "dirty steel");
        for (var i = 0; i < slats; i++)
        {
            var y = y0 + 0.015 + i * (y1 - y0 - 0.03) / Math.Max(1, slats - 1) - 0.007;
            k.Box(sx * (xf - 0.004), y, z0 + 0.008, sx * (xf + 0.012), y + 0.014, z1 - 0.008, Bare, "steel");
        }
    }

    /// <summary>A stencilled warning placard on a face: yellow plate, one dark bar.</summary>
    private static void Placard(Kit k, double x, double y, double zf, double w = 0.12, double h = 0.07)
    {
        k.Box(x - w / 2, y - h / 2, zf - 0.004, x + w / 2, y + h / 2, zf + 0.008, HazardY, "hazard paint");
        k.Box(x - w / 2 + 0.012, y - 0.008, zf + 0.004, x + w / 2 - 0.012, y + 0.008, zf + 0.012, Dark, "plastic");
    }

    /// <summary>A high-voltage bushing standing on a lid at (x, z): stacked ceramic sheds and a copper stud.</summary>
    private static double Bushing(Kit k, double x, double z, double y0)
    {
        var y = y0;
        for (var i = 0; i < 5; i++)
        {
            k.Hull(Cyl(x, z, i % 2 == 0 ? 0.05 : 0.034, y, y + 0.03, 8), Ceramic, "plastic");
            y += 0.026;
        }
        k.Hull(Cyl(x, z, 0.016, y, y + 0.05, 8), Copper, "brass");
        return y + 0.05;
    }

    /// <summary>An upright pipe run at (x, z), r, with a flange at each end.</summary>
    private static void PipeY(Kit k, double x, double z, double r, double y0, double y1, string col)
    {
        k.Hull(Cyl(x, z, r, y0, y1, 8), col, "painted steel");
        foreach (var y in new[] { y0 + 0.004, y1 - 0.026 })
            k.Hull(Cyl(x, z, r * 1.6, y, y + 0.022, 8), Bare, "steel");
    }

    /// <summary>A handwheel valve: its body on a pipe and a red wheel facing +z.</summary>
    private static void Valve(Kit k, double x, double y, double z, double r)
    {
        k.Box(x - r * 1.3, y - r * 1.3, z - r * 1.3, x + r * 1.3, y + r * 1.3, z + r * 1.3, Body, "painted steel");
        k.Hull(CylZ(x, y, r * 0.5, z + r * 1.2, z + r * 2.6), Bare, "steel");
        k.Hull(CylZ(x, y, r * 2.2, z + r * 2.5, z + r * 2.5 + 0.014), RedPaint, "painted steel");
    }

    /// <summary>A tank lying along x with domed ends: barrel r from -half to +half, each dome stepping in to a flat cap.</summary>
    private static void TankX(Kit k, double cy, double cz, double r, double half, string col)
    {
        k.Hull(CylX(cy, cz, r, -half, half), col, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var pts = new List<double[]>();
            foreach (var (rr, x) in new[] { (r * 0.995, half - 0.004), (r * 0.8, half + r * 0.2), (r * 0.42, half + r * 0.32) })
                for (var i = 0; i < 8; i++)
                {
                    var a = (i + 0.5) * Math.PI / 4;
                    pts.Add([sx * x, cy + rr * Math.Cos(a), cz + rr * Math.Sin(a)]);
                }
            k.Hull(pts, col, "painted steel");
        }
    }

    /// <summary>A dome on an upright tank: from radius r at y0 stepping in to a flat cap (three rings).</summary>
    private static void DomeY(Kit k, double x, double z, double r, double y0, string col)
    {
        var pts = new List<double[]>();
        foreach (var (rr, y) in new[] { (r * 0.995, y0 - 0.004), (r * 0.8, y0 + r * 0.2), (r * 0.42, y0 + r * 0.32) })
            for (var i = 0; i < 8; i++)
            {
                var a = (i + 0.5) * Math.PI / 4;
                pts.Add([x + rr * Math.Cos(a), y, z + rr * Math.Sin(a)]);
            }
        k.Hull(pts, col, "painted steel");
    }

    /// <summary>Cable conduit from the plug's Data 1 gland up the module to height y1, then in along z to the face at zIn.</summary>
    private static void DataRun(Kit k, int n, double y1, double zIn)
    {
        var (x, z) = PortAt(n, "Data 1");
        k.Hull(Cyl(x, z, 0.018, 0.12, y1 + 0.02, 8), Dark, "plastic");
        k.Box(x - 0.018, y1 - 0.018, Math.Min(z, zIn), x + 0.018, y1 + 0.018, Math.Max(z, zIn), Dark, "plastic");
    }

    // ---------------------------------------------------------------- Capacitor banks
    private const string Caps = "Capacitor banks";

    private static Part CapA()
    {
        var k = Mod("Capacitor", Caps, "A", "HV cabinet", "A: a high-voltage capacitor cabinet: twin hinged doors with latches, vent grilles, a warning placard, three ceramic bushings and a copper bus on the roof.", 1.5);
        k.Box(-0.35, 0.078, -0.31, 0.35, 0.14, 0.31, Dark, "dirty steel");
        k.Paint(-0.34, 0.136, -0.3, 0.34, 1.3, 0.3, Body);
        k.Box(-0.35, 1.296, -0.31, 0.35, 1.33, 0.31, Mid, "painted steel");
        Door(k, -0.315, -0.012, 0.2, 1.24, 0.3);
        Door(k, 0.012, 0.315, 0.2, 1.24, 0.3, hingeLeft: false);
        Grille(k, -0.26, -0.07, 0.28, 0.44, 0.318, 5);
        Grille(k, 0.07, 0.26, 0.28, 0.44, 0.318, 5);
        Placard(k, 0.17, 1.06, 0.318);
        Lamp(k, -0.2, 1.1, Green, 0.318, 0.014);
        Lamp(k, -0.15, 1.1, Amber, 0.318, 0.014);
        var top = 0.0;
        foreach (var x in new[] { -0.2, 0.0, 0.2 })
            top = Bushing(k, x, -0.1, 1.326);
        k.Box(-0.22, top - 0.024, -0.112, 0.22, top - 0.004, -0.088, Copper, "brass");
        DataRun(k, 1, 0.6, -0.29);
        return k.P;
    }

    private static Part CapB()
    {
        var k = Mod("Capacitor", Caps, "B", "Can bank", "B: six capacitor cans in a strapped frame on a tray, copper studs and bus bars across their tops, a discharge resistor box with its placard.", 0.95);
        k.Box(-0.37, 0.078, -0.33, 0.37, 0.14, 0.33, Body, "painted steel");
        foreach (var x in new[] { -0.24, 0.0, 0.24 })
            foreach (var z in new[] { -0.13, 0.13 })
            {
                k.Hull(Cyl(x, z, 0.1, 0.136, 0.78, 8), Mid, "painted steel");
                k.Hull(Cyl(x, z, 0.07, 0.776, 0.8, 8), Dark, "plastic");
                foreach (var dz in new[] { -0.04, 0.04 })
                    k.Hull(Cyl(x, z + dz, 0.016, 0.796, 0.84, 8), Copper, "brass");
            }
        foreach (var z in new[] { -0.13, 0.13 })
            foreach (var dz in new[] { -0.04, 0.04 })
                k.Box(-0.3, 0.83, z + dz - 0.014, 0.3, 0.85, z + dz + 0.014, Copper, "brass");
        foreach (var y in new[] { 0.3, 0.62 })
        {
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(-0.355, y, sz * 0.228, 0.355, y + 0.04, sz * 0.246, Dark, "steel");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.338, y, -0.246, sx * 0.356, y + 0.04, 0.246, Dark, "steel");
        }
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.336, 0.136, sz * 0.24, sx * 0.37, 0.88, sz * 0.27, Body, "painted steel");
        k.Box(-0.3, 0.136, 0.256, -0.08, 0.27, 0.32, Dark, "dirty steel");
        Placard(k, -0.19, 0.2, 0.32, 0.12, 0.06);
        return k.P;
    }

    private static Part CapC()
    {
        var k = Mod("Capacitor", Caps, "C", "Oil tank", "C: an oil-filled capacitor tank like a ship's transformer: radiator fins down both sides, bushings on the lid, a conservator, an oil gauge and drain valve.", 1.42);
        k.Box(-0.3, 0.078, -0.3, 0.3, 0.124, 0.3, Body, "painted steel");
        k.Paint(-0.24, 0.12, -0.24, 0.24, 1.0, 0.24, Mid);
        k.Box(-0.26, 0.996, -0.26, 0.26, 1.03, 0.26, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            for (var i = 0; i < 6; i++)
            {
                var z = -0.2 + i * 0.08;
                k.Box(sx * 0.236, 0.2, z - 0.008, sx * 0.36, 0.92, z + 0.008, Bare, "steel");
            }
            k.Box(sx * 0.25, 0.896, -0.22, sx * 0.35, 0.936, 0.22, Body, "painted steel");
            k.Box(sx * 0.25, 0.184, -0.22, sx * 0.35, 0.224, 0.22, Body, "painted steel");
        }
        foreach (var x in new[] { -0.15, 0.0, 0.15 })
            Bushing(k, x, 0.06, 1.026);
        foreach (var x in new[] { -0.16, 0.16 })
            k.Box(x - 0.015, 1.026, -0.19, x + 0.015, 1.1, -0.15, Body, "painted steel");
        k.Hull(CylX(1.15, -0.17, 0.065, -0.2, 0.2), Mid, "painted steel");
        k.Box(-0.12, 0.7, 0.236, 0.02, 0.8, 0.252, Trim, "steel"); // (rating plate)
        Dial(k, 0.13, 0.75, 0.045, Ice, 0.24);
        Valve(k, 0.12, 0.26, 0.25, 0.018);
        DataRun(k, 1, 0.5, -0.24);
        return k.P;
    }

    private static Part CapD()
    {
        var k = Mod("Capacitor", Caps, "D", "Rack", "D: an equipment rack of five pull-out capacitor drawers on rails, each with handles, a vent strip and status lamps, one with a small readout.", 1.52);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.3, 0.078, sz * 0.26, sx * 0.34, 1.5, sz * 0.3, Body, "painted steel");
        foreach (var y in new[] { 0.078, 1.46 })
            k.Box(-0.34, y, -0.3, 0.34, y + 0.04, 0.3, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Paint(sx * 0.336, 0.12, -0.3, sx * 0.354, 1.46, 0.26, Mid);
        k.Box(-0.3, 0.12, -0.3, 0.3, 1.46, -0.284, Dark, "dirty steel");
        for (var i = 0; i < 5; i++)
        {
            var y = 0.13 + i * 0.265;
            k.Paint(-0.29, y, -0.27, 0.29, y + 0.24, 0.27, i % 2 == 0 ? Mid : Body);
            k.Box(-0.33, y + 0.004, 0.266, 0.33, y + 0.236, 0.282, Trim, "painted steel");
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.29 - 0.012, y + 0.05, 0.278, sx * 0.29 + 0.012, y + 0.19, 0.31, Bare, "steel");
            k.Box(-0.2, y + 0.07, 0.278, 0.08, y + 0.17, 0.288, Dark, "dirty steel");
            Lamp(k, 0.14, y + 0.15, i == 3 ? Amber : Green, 0.282, 0.012);
            Lamp(k, 0.19, y + 0.15, Green, 0.282, 0.012);
            if (i == 2)
            {
                k.Box(0.11, y + 0.05, 0.278, 0.24, y + 0.11, 0.29, Dark, "plastic");
                k.Glow(0.12, y + 0.06, 0.284, 0.23, y + 0.1, 0.294, Ice);
            }
        }
        DataRun(k, 1, 1.0, -0.3);
        return k.P;
    }

    private static Part CapE()
    {
        var k = Mod("Capacitor", Caps, "E", "Pulse unit", "E: a low pulse-power unit: a cooling fan behind a filter grille on the front, side louvres, a heavy terminal block with copper straps, lifting handles on top.", 0.84);
        k.Paint(-0.34, 0.078, -0.3, 0.34, 0.62, 0.3, Mid);
        k.Box(-0.26, 0.15, 0.296, 0.06, 0.55, 0.312, Body, "painted steel");
        k.Hull(CylZ(-0.1, 0.35, 0.15, 0.306, 0.316), Dark, "dirty steel");
        k.Box(-0.25, 0.345, 0.31, 0.05, 0.357, 0.324, Bare, "steel");
        k.Box(-0.106, 0.2, 0.31, -0.094, 0.5, 0.324, Bare, "steel");
        k.Hull(CylZ(-0.1, 0.35, 0.04, 0.31, 0.33), Body, "painted steel");
        foreach (var (x, y) in new[] { (-0.24, 0.17), (0.04, 0.17), (-0.24, 0.53), (0.04, 0.53) })
            k.Box(x - 0.008, y - 0.008, 0.31, x + 0.008, y + 0.008, 0.322, Bare, "steel");
        Placard(k, 0.22, 0.48, 0.3, 0.14, 0.07);
        Lamp(k, 0.17, 0.3, Green, 0.3, 0.014);
        Lamp(k, 0.22, 0.3, Amber, 0.3, 0.014);
        foreach (var sx in new[] { -1.0, 1.0 })
            GrilleX(k, sx, 0.34, -0.22, 0.18, 0.18, 0.52, 6);
        k.Box(0.04, 0.616, -0.26, 0.3, 0.7, -0.02, Dark, "plastic");
        foreach (var x in new[] { 0.08, 0.15, 0.22 })
            k.Box(x, 0.696, -0.2, x + 0.04, 0.8, -0.08, Copper, "brass");
        foreach (var z in new[] { -0.14, 0.14 })
        {
            k.Box(-0.3, 0.616, z - 0.012, -0.27, 0.7, z + 0.012, Bare, "steel");
            k.Box(-0.06, 0.616, z - 0.012, -0.03, 0.7, z + 0.012, Bare, "steel");
            k.Box(-0.3, 0.68, z - 0.012, -0.03, 0.7, z + 0.012, Bare, "steel");
        }
        return k.P;
    }

    // ---------------------------------------------------------------- Life support
    private const string Life = "Life support";

    private static Part LifeA()
    {
        var k = Mod("Life Support", Life, "A", "Scrubber", "A: a CO2 scrubber unit in a rack frame: a blower below, flanged square ducts in and out on top, a filter panel with quarter-turn fasteners, a gauge and lamps.", 1.36);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.32, 0.078, sz * 0.27, sx * 0.355, 1.06, sz * 0.305, Body, "painted steel");
        k.Box(-0.355, 1.02, -0.305, 0.355, 1.06, 0.305, Body, "painted steel");
        k.Paint(-0.3, 0.3, -0.25, 0.3, 1.0, 0.25, Mid);
        k.Box(-0.25, 0.1, -0.2, 0.25, 0.3, 0.2, Dark, "dirty steel");
        k.Hull(CylZ(0, 0.2, 0.08, 0.196, 0.26), Body, "painted steel");
        k.Box(-0.24, 0.4, 0.246, 0.12, 0.92, 0.262, Body, "painted steel");
        foreach (var (x, y) in new[] { (-0.21, 0.44), (0.09, 0.44), (-0.21, 0.88), (0.09, 0.88) })
            k.Hull(CylZ(x, y, 0.014, 0.258, 0.27), Bare, "steel");
        Placard(k, -0.06, 0.8, 0.262, 0.16, 0.06);
        Dial(k, 0.21, 0.82, 0.04, Ice, 0.25);
        Lamp(k, 0.19, 0.7, Green, 0.25, 0.014);
        Lamp(k, 0.23, 0.7, Green, 0.25, 0.014);
        k.Box(-0.26, 0.996, -0.1, -0.06, 1.3, 0.1, Body, "painted steel");
        k.Box(-0.275, 1.056, -0.115, -0.045, 1.076, 0.115, Bare, "steel");
        k.Box(0.06, 0.996, -0.1, 0.26, 1.2, 0.1, Body, "painted steel");
        k.Box(0.06, 1.2, -0.34, 0.26, 1.3, 0.1, Body, "painted steel");
        k.Box(0.045, 1.056, -0.115, 0.275, 1.076, 0.115, Bare, "steel");
        return k.P;
    }

    private static Part LifeB()
    {
        var k = Mod("Life Support", Life, "B", "O2 plant", "B: an oxygen generator cabinet: green oxygen and blue water lines up its face into a valve panel with three gauges and two handwheels, a service door below, a vent at the top.", 1.62);
        k.Paint(-0.3, 0.078, -0.28, 0.3, 1.6, 0.28, Body);
        k.Box(-0.24, 0.9, 0.276, 0.24, 1.36, 0.29, Dark, "dirty steel");
        foreach (var x in new[] { -0.15, 0.0, 0.15 })
            Dial(k, x, 1.24, 0.045, Ice, 0.29);
        Valve(k, -0.1, 1.03, 0.296, 0.018);
        Valve(k, 0.1, 1.03, 0.296, 0.018);
        Door(k, -0.26, 0.26, 0.16, 0.8, 0.28);
        Grille(k, -0.22, 0.22, 1.42, 1.56, 0.28, 4);
        k.Hull(Cyl(-0.1, 0.31, 0.016, 0.82, 1.0, 8), O2, "painted steel");
        k.Hull(Cyl(0.1, 0.31, 0.016, 0.82, 1.0, 8), Water, "painted steel");
        foreach (var x in new[] { -0.1, 0.1 })
            k.Box(x - 0.02, 0.8, 0.27, x + 0.02, 0.84, 0.33, Bare, "steel");
        Placard(k, 0.17, 0.92, 0.29, 0.1, 0.04);
        Lamp(k, -0.2, 0.94, Green, 0.29, 0.012);
        DataRun(k, 1, 1.2, -0.28);
        return k.P;
    }

    private static Part LifeC()
    {
        var k = Mod("Life Support", Life, "C", "Air handler", "C: a low air-handling unit: a round duct collar and elbow on top, the blower motor on its flank, a pull-out filter behind a grille, a gauge and placard.", 1.3);
        k.Paint(-0.34, 0.078, -0.32, 0.3, 0.7, 0.32, Mid);
        k.Hull(Cyl(0.0, 0.0, 0.17, 0.696, 0.78, 8), Body, "painted steel");
        k.Hull(Cyl(0.0, 0.0, 0.2, 0.74, 0.76, 8), Bare, "steel");
        k.Hull(Cyl(0.0, 0.0, 0.15, 0.776, 1.1, 8), Body, "painted steel");
        k.Hull(TipX(CylZ(0, 1.12, 0.146, -0.15, 0.0), 1.12, 0.0, 0.0), Body, "painted steel");
        k.Hull(CylZ(0, 1.12, 0.15, -0.36, -0.15), Body, "painted steel");
        k.Hull(CylZ(0, 1.12, 0.18, -0.2, -0.18), Bare, "steel");
        k.Hull(CylX(0.4, 0.0, 0.11, 0.296, 0.44), Dark, "painted steel");
        foreach (var x in new[] { 0.33, 0.37, 0.41 })
            k.Hull(CylX(0.4, 0.0, 0.118, x, x + 0.014), Bare, "steel");
        Grille(k, -0.28, 0.12, 0.2, 0.6, 0.32, 9);
        foreach (var x in new[] { -0.24, 0.08 })
            k.Box(x - 0.025, 0.34, 0.33, x + 0.025, 0.46, 0.352, Bare, "steel");
        Dial(k, 0.21, 0.52, 0.04, Ice, 0.32);
        Placard(k, 0.21, 0.36, 0.32, 0.1, 0.06);
        return k.P;
    }

    private static Part LifeD()
    {
        var k = Mod("Life Support", Life, "D", "Gas bottles", "D: four oxygen and nitrogen bottles strapped to a rack, pigtails into a manifold, piped down to a regulator panel with gauges and a handwheel.", 1.5);
        k.Box(-0.37, 0.078, -0.26, 0.37, 1.3, -0.22, Body, "painted steel");
        k.Box(-0.37, 0.078, -0.26, 0.37, 0.12, 0.2, Body, "painted steel");
        double[] xs = [-0.27, -0.09, 0.09, 0.27];
        for (var i = 0; i < 4; i++)
        {
            var (x, col) = (xs[i], i == 3 ? Mid : White);
            k.Hull(Cyl(x, -0.12, 0.085, 0.116, 1.28, 8), col, "painted steel");
            k.Hull(Cyl(x, -0.12, 0.088, 1.16, 1.24, 8), i == 3 ? Dark : O2, "painted steel");
            DomeY(k, x, -0.12, 0.085, 1.28, col);
            k.Hull(Cyl(x, -0.12, 0.022, 1.3, 1.37, 8), Bare, "steel");
            k.Box(x - 0.008, 1.36, -0.13, x + 0.008, 1.45, -0.11, Bare, "steel");
        }
        k.Box(-0.3, 1.44, -0.14, 0.3, 1.47, -0.1, Bare, "steel");
        foreach (var y in new[] { 0.4, 0.95 })
            k.Box(-0.37, y, -0.04, 0.37, y + 0.04, -0.024, Dark, "steel");
        k.Box(0.31, 0.62, -0.14, 0.35, 1.47, -0.1, Bare, "steel");
        k.Box(0.31, 0.6, -0.14, 0.35, 0.64, 0.14, Bare, "steel");
        k.Paint(-0.2, 0.12, 0.1, 0.2, 0.7, 0.18, Mid);
        k.Box(0.2, 0.6, 0.1, 0.35, 0.64, 0.14, Bare, "steel");
        Dial(k, -0.1, 0.55, 0.045, Ice, 0.18);
        Dial(k, 0.08, 0.55, 0.045, Ice, 0.18);
        Valve(k, 0.0, 0.32, 0.184, 0.018);
        return k.P;
    }

    private static Part LifeE()
    {
        var k = Mod("Life Support", Life, "E", "Water still", "E: a water recovery skid: two process tanks, three filter canisters, a pump with its motor, blue water lines with flanges and two gauges.", 1.2);
        k.Box(-0.36, 0.078, -0.34, 0.36, 0.12, 0.34, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Hull(Cyl(sx * 0.2, -0.15, 0.14, 0.116, 0.92, 8), Trim, "painted steel");
            DomeY(k, sx * 0.2, -0.15, 0.14, 0.92, Trim);
            PipeY(k, sx * 0.2, 0.04, 0.02, 0.62, 1.1, Water);
            k.Box(sx * 0.2 - 0.02, 1.08, -0.15, sx * 0.2 + 0.02, 1.12, 0.06, Water, "painted steel");
        }
        foreach (var x in new[] { -0.16, 0.0, 0.16 })
        {
            k.Hull(Cyl(x, 0.2, 0.05, 0.3, 0.6, 8), Mid, "painted steel");
            k.Hull(Cyl(x, 0.2, 0.056, 0.56, 0.62, 8), Dark, "plastic");
        }
        k.Box(-0.25, 0.6, 0.18, 0.25, 0.64, 0.22, Water, "painted steel");
        k.Box(-0.22, 0.6, 0.02, -0.18, 0.64, 0.22, Water, "painted steel");
        k.Box(0.18, 0.6, 0.02, 0.22, 0.64, 0.22, Water, "painted steel");
        k.Box(-0.12, 0.116, 0.1, 0.12, 0.27, 0.3, Dark, "dirty steel");
        k.Hull(CylX(0.19, 0.2, 0.06, 0.116, 0.26), Body, "painted steel");
        Dial(k, -0.07, 0.2, 0.035, Ice, 0.3);
        Dial(k, 0.05, 0.2, 0.035, Ice, 0.3);
        return k.P;
    }

    // ---------------------------------------------------------------- Sensors
    private const string Sens = "Sensors";

    private static IEnumerable<double[]> Dome(double R, double y0, int sides = 8)
    {
        foreach (var phi in new[] { 0.0, Math.PI / 6, Math.PI / 3 })
            for (var i = 0; i < sides; i++)
            {
                var a = (i + 0.5) * 2 * Math.PI / sides;
                yield return [R * Math.Cos(phi) * Math.Cos(a), y0 + R * Math.Sin(phi), R * Math.Cos(phi) * Math.Sin(a)];
            }
        yield return [0, y0 + R, 0];
    }

    private static Part SensA()
    {
        var k = Mod("Sensor", Sens, "A", "Nav radar", "A: a navigation radar: a slotted waveguide antenna turning on a gearbox over a flanged pedestal, an access door in the pedestal, conduit down to the plug.", 1.0);
        k.Hull(Cyl(0, 0, 0.28, 0.078, 0.13, 8), Body, "painted steel");
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            k.Hull(Cyl(0.24 * Math.Cos(a), 0.24 * Math.Sin(a), 0.014, 0.126, 0.146, 8), Bare, "steel");
        }
        k.Hull(Cyl(0, 0, 0.2, 0.126, 0.6, 8), Mid, "painted steel");
        Door(k, -0.08, 0.08, 0.2, 0.5, 0.185);
        k.Box(-0.16, 0.596, -0.16, 0.16, 0.8, 0.16, Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.05, 0.796, 0.87, 8), Bare, "steel");
        k.Box(-0.48, 0.866, -0.045, 0.48, 0.98, 0.045, Trim, "painted steel");
        k.Box(-0.46, 0.89, 0.04, 0.46, 0.956, 0.052, Dark, "plastic");
        Placard(k, 0.0, 0.7, 0.16, 0.12, 0.06);
        DataRun(k, 1, 0.35, -0.17);
        return k.P;
    }

    private static Part SensB()
    {
        var k = Mod("Sensor", Sens, "B", "Radome", "B: a white radome on a squat base with a bolted access hatch, a lightning rod on top and cable conduit down to the plug.", 0.86);
        k.Paint(-0.33, 0.078, -0.33, 0.33, 0.42, 0.33, Mid);
        k.Hull(Cyl(0, 0, 0.33, 0.416, 0.46, 8), Body, "painted steel");
        k.Hull(Dome(0.31, 0.456), White, "plastic");
        k.Hull(Cyl(0, 0, 0.01, 0.76, 0.86, 6), Bare, "steel");
        Door(k, -0.14, 0.14, 0.15, 0.37, 0.33);
        Placard(k, 0.24, 0.3, 0.33, 0.1, 0.05);
        DataRun(k, 1, 0.3, -0.33);
        return k.P;
    }

    private static Part SensC()
    {
        var k = Mod("Sensor", Sens, "C", "Array", "C: a phased-array panel tilted back on hinge brackets over an equipment box with louvred sides, its face a grid of tiles in a frame.", 1.3);
        k.Paint(-0.36, 0.078, -0.3, 0.36, 0.5, 0.3, Mid);
        foreach (var sx in new[] { -1.0, 1.0 })
            GrilleX(k, sx, 0.36, -0.22, 0.22, 0.14, 0.44, 5);
        Door(k, -0.3, 0.0, 0.14, 0.44, 0.3);
        Placard(k, 0.18, 0.36, 0.3, 0.12, 0.06);
        const double py = 0.92, pz = 0.0, tilt = 0.35;
        IEnumerable<double[]> Slab(double x0, double x1, double y0, double y1, double z0, double z1) =>
            TipX(new[] { x0, x1 }.SelectMany(x => new[] { y0, y1 }.SelectMany(y => new[] { z0, z1 }.Select(z => new[] { x, y, z }))), py, pz, tilt);
        k.Hull(Slab(-0.36, 0.36, py - 0.36, py + 0.36, -0.04, 0.03), Body, "painted steel");
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
                k.Hull(Slab(-0.33 + i * 0.165 + 0.006, -0.33 + (i + 1) * 0.165 - 0.006, py - 0.33 + j * 0.165 + 0.006, py - 0.33 + (j + 1) * 0.165 - 0.006, 0.026, 0.04), (i + j) % 2 == 0 ? Dark : "#252a2f", "plastic");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.26 - 0.03, 0.496, -0.26, sx * 0.26 + 0.03, 0.7, -0.1, Body, "painted steel");
        return k.P;
    }

    private static Part SensD()
    {
        var k = Mod("Sensor", Sens, "D", "Comms mast", "D: a tubular comms mast on an equipment box, braced by struts: a yardarm with blade antennas and port and starboard lamps, a whip on top, a small dish on a side arm.", 2.0);
        k.Paint(-0.3, 0.078, -0.3, 0.3, 0.3, 0.3, Mid);
        Door(k, -0.2, 0.2, 0.12, 0.27, 0.3);
        k.Hull(Cyl(0, 0, 0.045, 0.296, 1.7, 8), Bare, "steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Hull(Brace([sx * 0.24, 0.296, sz * 0.24], [sx * 0.05, 0.9, sz * 0.05], 0.01), Body, "painted steel");
        k.Box(-0.4, 1.5, -0.02, 0.4, 1.54, 0.02, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.3 - 0.004, 1.3, -0.04, sx * 0.3 + 0.008, 1.5, 0.04, Trim, "plastic");
            k.Glow(sx * 0.38 - 0.016, 1.54, -0.016, sx * 0.38 + 0.016, 1.572, 0.016, sx < 0 ? Red : Green);
        }
        k.Hull(Cyl(0, 0, 0.008, 1.696, 1.98, 6), Bare, "steel");
        k.Box(0.04, 1.06, -0.015, 0.2, 1.09, 0.015, Body, "painted steel");
        k.Hull(TipX(RingZ(0.22, 1.08, 0.12, 0.05).Concat(RingZ(0.22, 1.08, 0.04, -0.02)), 1.08, 0.0, 0.3), Trim, "painted steel");
        k.Hull(TipX(CylZ(0.22, 1.08, 0.012, 0.046, 0.14), 1.08, 0.0, 0.3), Dark, "plastic");
        return k.P;
    }

    private static Part SensE()
    {
        var k = Mod("Sensor", Sens, "E", "Tracker", "E: a tracking dish on a yoke over a turning pedestal, a counterweight behind it, an elevation ram, a cable wrap housing and an access door.", 1.42);
        const double H = 0.4;
        k.Hull(Cyl(0, 0, 0.34, 0.078, H, 8), Mid, "painted steel");
        Door(k, -0.08, 0.08, 0.13, 0.34, 0.314);
        k.Hull(Cyl(0, 0, 0.3, H - 0.004, H + 0.07, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.12, H + 0.066, H + 0.14, 8), Dark, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.19, H + 0.066, -0.05, sx * 0.23, 0.98, 0.05, Mid, "painted steel");
        const double cy = 0.94, tilt = 0.5;
        k.Hull(TipX(RingZ(0, cy, 0.34, 0.07).Concat(RingZ(0, cy, 0.13, -0.07)), cy, 0, tilt), White, "painted steel");
        k.Hull(TipX(RingZ(0, cy, 0.3, 0.066).Concat(RingZ(0, cy, 0.3, 0.078)), cy, 0, tilt), Trim, "plastic");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Hull(TipX(Brace([sx * 0.22, cy, 0.07], [sx * 0.02, cy, 0.3], 0.008), cy, 0, tilt), Bare, "steel");
        k.Hull(TipX(CylZ(0, cy, 0.035, 0.29, 0.35), cy, 0, tilt), Dark, "plastic");
        k.Hull(TipX(CylZ(0, cy, 0.06, -0.2, -0.066), cy, 0, tilt), Body, "painted steel");
        k.Hull(TipX(CylZ(0, cy, 0.1, -0.34, -0.2), cy, 0, tilt), Dark, "steel");
        Ram(k, 0, -0.26, H + 0.15, 0.78, 0);
        return k.P;
    }

    // ---------------------------------------------------------------- Fuel tanks
    private const string Fuel = "Fuel tanks";

    /// <summary>The fuel line from the plug's Fuel A gland up to y1, then along z to zTo (with a valve on the riser).</summary>
    private static void FuelRun(Kit k, double y1, double zTo, bool valve = true)
    {
        var (x, z) = PortAt(1, "Fuel A");
        PipeY(k, x, z, 0.02, 0.12, y1 + 0.02, Ochre);
        k.Box(x - 0.02, y1 - 0.018, Math.Min(z, zTo), x + 0.02, y1 + 0.014, Math.Max(z, zTo), Ochre, "painted steel");
        if (valve)
            Valve(k, x, 0.2 + (y1 - 0.2) * 0.4, z, 0.016);
    }

    private static Part FuelA()
    {
        var k = Mod("Fuel", Fuel, "A", "Lying tank", "A: a fuel tank lying along the module with domed ends, on two saddles with steel straps, a manhole on top, a vent gooseneck and the fuel line down to Fuel A.", 0.92);
        TankX(k, 0.48, 0.03, 0.27, 0.26, Mid);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Profile(sx * 0.15, sx * 0.21, [(0.078, -0.26), (0.078, 0.3), (0.3, 0.3), (0.36, 0.22), (0.36, -0.18), (0.3, -0.26)], Body, "painted steel");
            k.Hull(CylX(0.48, 0.03, 0.276, sx * 0.16, sx * 0.2), Dark, "steel");
        }
        k.Hull(Cyl(0.0, 0.03, 0.08, 0.72, 0.78, 8), Body, "painted steel");
        for (var i = 0; i < 6; i++)
        {
            var a = i * Math.PI / 3;
            k.Hull(Cyl(0.058 * Math.Cos(a), 0.03 + 0.058 * Math.Sin(a), 0.01, 0.776, 0.792, 6), Bare, "steel");
        }
        k.Hull(Cyl(-0.2, 0.03, 0.018, 0.7, 0.88, 8), Ochre, "painted steel");
        k.Box(-0.2 - 0.018, 0.86, 0.012, -0.12, 0.896, 0.048, Ochre, "painted steel");
        Placard(k, 0.0, 0.48, 0.3, 0.16, 0.06);
        FuelRun(k, 0.32, -0.1);
        return k.P;
    }

    private static void SphereBody(Kit k, double R, double cy)
    {
        for (var part = 0; part < 3; part++)
        {
            var pts = new List<double[]>();
            foreach (var i in new[] { part * 2, part * 2 + 1, part * 2 + 2 })
            {
                var phi = -Math.PI / 2 + i * Math.PI / 6;
                var rr = Math.Max(R * Math.Cos(phi), 0.02);
                for (var lo = 0; lo < 10; lo++)
                {
                    var th = (lo + 0.5) * 2 * Math.PI / 10;
                    pts.Add([rr * Math.Cos(th), cy + R * Math.Sin(phi), rr * Math.Sin(th)]);
                }
            }
            k.Hull(pts, Trim, "painted steel");
        }
    }

    private static Part FuelB()
    {
        var k = Mod("Fuel", Fuel, "B", "Sphere", "B: a spherical pressure tank held by an equator ring on four posts from a base frame, fill and relief valves on top, the outlet piped to Fuel A.", 1.2);
        foreach (var s in new[] { -1.0, 1.0 })
        {
            k.Box(-0.34, 0.078, s * 0.3 - 0.03, 0.34, 0.13, s * 0.3 + 0.03, Body, "painted steel");
            k.Box(s * 0.3 - 0.03, 0.078, -0.27, s * 0.3 + 0.03, 0.126, 0.27, Body, "painted steel");
        }
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.27 - 0.025, 0.126, sz * 0.27 - 0.025, sx * 0.27 + 0.025, 0.66, sz * 0.27 + 0.025, Mid, "painted steel");
        SphereBody(k, 0.32, 0.66);
        k.Hull(Cyl(0, 0, 0.345, 0.64, 0.68, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.05, 0.96, 1.02, 8), Body, "painted steel");
        k.Hull(Cyl(-0.03, 0, 0.016, 1.016, 1.12, 8), Bare, "steel");
        k.Hull(Cyl(0.05, 0.0, 0.02, 1.016, 1.08, 8), RedPaint, "painted steel");
        k.Hull(Cyl(0, 0, 0.03, 0.2, 0.36, 8), Ochre, "painted steel");
        k.Box(-0.02, 0.198, -0.35, 0.02, 0.232, 0.02, Ochre, "painted steel");
        k.Box(-0.02, 0.198, -0.37, 0.35, 0.232, -0.33, Ochre, "painted steel");
        PipeY(k, 0.35, -0.35, 0.02, 0.12, 0.24, Ochre);
        Placard(k, 0.0, 0.66, 0.326, 0.14, 0.05);
        return k.P;
    }

    private static Part FuelC()
    {
        var k = Mod("Fuel", Fuel, "C", "Upright tank", "C: an upright fuel tank with a domed head in a four-post frame, banded to it, a filler neck and cap, a sight gauge tube down the front, the outlet to Fuel A.", 1.5);
        k.Box(-0.36, 0.078, -0.36, 0.36, 0.12, 0.36, Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.28, 0.116, 1.2, 8), Mid, "painted steel");
        DomeY(k, 0, 0, 0.28, 1.2, Mid);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.31, 0.116, sz * 0.31, sx * 0.35, 1.32, sz * 0.35, Body, "painted steel");
        foreach (var y in new[] { 0.4, 0.95 })
        {
            k.Hull(Cyl(0, 0, 0.29, y, y + 0.04, 8), Dark, "steel");
            foreach (var s in new[] { -1.0, 1.0 })
            {
                k.Box(-0.35, y, s * 0.31, 0.35, y + 0.04, s * 0.33, Body, "painted steel");
                k.Box(s * 0.31, y + 0.004, -0.31, s * 0.33, y + 0.036, 0.31, Body, "painted steel");
            }
        }
        k.Hull(Cyl(0.1, -0.06, 0.05, 1.27, 1.4, 8), Bare, "steel");
        k.Hull(Cyl(0.1, -0.06, 0.065, 1.396, 1.43, 8), RedPaint, "painted steel");
        k.Box(-0.03, 0.2, 0.256, 0.03, 1.12, 0.29, Bare, "steel");
        k.Box(-0.014, 0.24, 0.284, 0.014, 1.08, 0.3, Dark, "plastic");
        Placard(k, 0.0, 0.7, 0.3, 0.12, 0.06);
        FuelRun(k, 0.2, -0.2);
        return k.P;
    }

    private static Part FuelD()
    {
        var k = Mod("Fuel", Fuel, "D", "Bottles + pump", "D: two tall fuel bottles at the back, a transfer pump and motor on a skid in front, a filter canister, flanged lines and a pressure gauge.", 1.36);
        k.Box(-0.36, 0.078, -0.36, 0.36, 0.12, 0.36, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.17;
            k.Hull(Cyl(x, -0.17, 0.15, 0.116, 1.16, 8), Mid, "painted steel");
            DomeY(k, x, -0.17, 0.15, 1.16, Mid);
            k.Hull(Cyl(x, -0.17, 0.155, 0.3, 0.34, 8), Dark, "steel");
            k.Hull(Cyl(x, -0.17, 0.155, 0.9, 0.94, 8), Dark, "steel");
            k.Hull(Cyl(x, -0.17, 0.025, 1.2, 1.28, 8), Bare, "steel");
            k.Box(x - 0.02, 1.258, -0.19, x + 0.02, 1.294, 0.14, Ochre, "painted steel");
        }
        k.Box(-0.19, 1.258, 0.1, 0.19, 1.294, 0.14, Ochre, "painted steel");
        PipeY(k, 0.0, 0.12, 0.02, 0.36, 1.3, Ochre);
        k.Hull(CylZ(0.0, 0.26, 0.11, 0.08, 0.3), Mid, "painted steel");
        k.Hull(CylX(0.26, 0.19, 0.07, 0.1, 0.3), Dark, "painted steel");
        foreach (var x in new[] { 0.14, 0.19, 0.24 })
            k.Hull(CylX(0.26, 0.19, 0.076, x, x + 0.012), Bare, "steel");
        k.Hull(Cyl(-0.22, 0.2, 0.06, 0.116, 0.42, 8), Body, "painted steel");
        k.Box(-0.22, 0.3, 0.18, -0.1, 0.34, 0.22, Ochre, "painted steel");
        Dial(k, -0.22, 0.48, 0.04, Ice, 0.2);
        k.Box(-0.24, 0.44, 0.19, -0.2, 0.52, 0.205, Bare, "steel");
        FuelRun(k, 0.18, 0.0, valve: false);
        return k.P;
    }

    private static Part FuelE()
    {
        var k = Mod("Fuel", Fuel, "E", "Box tank", "E: a welded box tank stiffened by ribs, a bolted manhole cover on top, a vent gooseneck, a level gauge and the outlet line to Fuel A.", 1.2);
        k.Paint(-0.34, 0.078, -0.3, 0.34, 1.0, 0.3, Mid);
        foreach (var x in new[] { -0.17, 0.0, 0.17 })
            k.Box(x - 0.012, 0.1, 0.296, x + 0.012, 0.98, 0.33, Body, "painted steel");
        k.Box(-0.336, 0.53, 0.296, 0.336, 0.56, 0.334, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            foreach (var z in new[] { -0.15, 0.0, 0.15 })
                k.Box(sx * 0.336, 0.1, z - 0.012, sx * 0.37, 0.98, z + 0.012, Body, "painted steel");
            k.Box(sx * 0.336, 0.53, -0.296, sx * 0.374, 0.56, 0.296, Body, "painted steel");
        }
        k.Hull(Cyl(-0.1, -0.05, 0.12, 0.996, 1.03, 8), Body, "painted steel");
        for (var i = 0; i < 8; i++)
        {
            var a = (i + 0.5) * Math.PI / 4;
            k.Hull(Cyl(-0.1 + 0.1 * Math.Cos(a), -0.05 + 0.1 * Math.Sin(a), 0.01, 1.026, 1.042, 6), Bare, "steel");
        }
        k.Hull(Cyl(0.2, 0.15, 0.018, 0.996, 1.14, 8), Ochre, "painted steel");
        k.Box(0.2 - 0.018, 1.12, 0.15, 0.2 + 0.018, 1.156, 0.24, Ochre, "painted steel");
        Dial(k, 0.085, 0.8, 0.04, Ice, 0.33);
        Placard(k, -0.085, 0.8, 0.33, 0.1, 0.06);
        FuelRun(k, 0.3, -0.31);
        return k.P;
    }

    // ---------------------------------------------------------------- Magazines, 2x2
    // A 1x1 plug has no Ammo coupling (module-sockets.md 2.5: it starts at 2x2), so magazines are 2x2 modules, designed at that
    // size. Each feeds the Ammo port at the plug's front middle through a feed trunk.
    private const string Mags = "Magazines";

    /// <summary>The feed trunk over the Ammo port: a flanged box down onto the plug with an inspection cover.</summary>
    private static void Trunk(Kit k, double z0, double y1)
    {
        var (_, z) = PortAt(2, "Ammo");
        k.Box(-0.16, 0.078, z0, 0.16, y1, z + 0.14, Body, "painted steel");
        k.Box(-0.19, 0.078, z0 - 0.02, 0.19, 0.12, z + 0.17, Mid, "painted steel");
        k.Box(-0.1, y1 - 0.16, z + 0.136, 0.1, y1 - 0.04, z + 0.15, Mid, "painted steel");
        foreach (var x in new[] { -0.08, 0.08 })
            k.Box(x - 0.008, y1 - 0.104, z + 0.146, x + 0.008, y1 - 0.088, z + 0.156, Bare, "steel");
    }

    private static Part MagA()
    {
        var k = Mod("Magazine", Mags, "A", "Ready locker", "A (2x2): a ready-use ammunition locker: two tall doors with hinges and latch handles, side louvres, stencilled placards, a feed trunk down to the Ammo port.", 1.4, 2);
        k.Box(-0.82, 0.078, -0.62, 0.82, 0.14, 0.52, Dark, "dirty steel");
        k.Paint(-0.8, 0.136, -0.6, 0.8, 1.32, 0.5, Body);
        k.Box(-0.82, 1.316, -0.62, 0.82, 1.35, 0.52, Mid, "painted steel");
        Door(k, -0.76, -0.02, 0.48, 1.26, 0.5);
        Door(k, 0.02, 0.76, 0.48, 1.26, 0.5, hingeLeft: false);
        Placard(k, -0.4, 1.12, 0.518, 0.2, 0.08);
        Placard(k, 0.4, 1.12, 0.518, 0.2, 0.08);
        foreach (var sx in new[] { -1.0, 1.0 })
            GrilleX(k, sx, 0.8, -0.4, 0.3, 0.9, 1.2, 6);
        Grille(k, -0.6, -0.24, 0.22, 0.4, 0.5, 5);
        Grille(k, 0.24, 0.6, 0.22, 0.4, 0.5, 5);
        Trunk(k, 0.48, 0.44);
        Lamp(k, 0.0, 1.3, Green, 0.5, 0.012);
        return k.P;
    }

    private static Part MagB()
    {
        var k = Mod("Magazine", Mags, "B", "Linkless drum", "B (2x2): a linkless feed drum lying across the module in two cradles, ribbed end covers, a drive motor and gearbox on one end, the feed chute down to the Ammo port.", 1.4, 2);
        const double cy = 0.74, r = 0.6;
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Profile(sx * 0.46, sx * 0.54, [(0.078, -0.7), (0.078, 0.6), (0.36, 0.6), (0.5, 0.4), (0.5, -0.5), (0.36, -0.7)], Body, "painted steel");
        k.Hull(CylX(cy, -0.05, r, -0.7, 0.7), Mid, "painted steel");
        foreach (var x in new[] { -0.36, 0.0, 0.36 })
            k.Hull(CylX(cy, -0.05, r + 0.008, x - 0.02, x + 0.02), Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Hull(CylX(cy, -0.05, r * 0.92, sx * 0.696, sx * 0.74), Body, "painted steel");
        k.Hull(CylX(cy, -0.05, 0.12, 0.736, 0.86), Dark, "painted steel");
        k.Box(0.736, cy - 0.28, -0.2, 0.84, cy - 0.1, 0.06, Body, "painted steel");
        k.Box(-0.14, 0.32, 0.38, 0.14, 0.6, 0.62, Body, "painted steel");
        Trunk(k, 0.5, 0.4);
        Placard(k, -0.3, 0.3, 0.6, 0.18, 0.07);
        return k.P;
    }

    private static Part MagC()
    {
        var k = Mod("Magazine", Mags, "C", "Shell racks", "C (2x2): an open three-tier rack of shells lying nose-out behind retaining bars, brass cases at the back, a loading tray and the feed trunk to the Ammo port below.", 1.42, 2);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.8, 0.078, sz * 0.62 - 0.03, sx * 0.86, 1.4, sz * 0.62 + 0.03, Body, "painted steel");
        k.Box(-0.86, 0.078, -0.7, 0.86, 0.14, 0.66, Body, "painted steel");
        for (var t = 0; t < 3; t++)
        {
            var y = 0.18 + t * 0.4;
            k.Box(-0.8, y - 0.03, -0.6, 0.8, y, 0.6, Mid, "painted steel");
            for (var i = 0; i < 5; i++)
            {
                var x = -0.6 + i * 0.3;
                var cyS = y + 0.12;
                k.Hull(CylZ(x, cyS, 0.11, -0.6, -0.04), Copper, "brass");
                k.Hull(CylZ(x, cyS, 0.1, -0.044, 0.34), Mid, "painted steel");
                k.Hull(RingZ(x, cyS, 0.1, 0.336).Concat(RingZ(x, cyS, 0.03, 0.52)), Dark, "painted steel");
            }
            k.Box(-0.82, y + 0.2, 0.56, 0.82, y + 0.24, 0.6, Bare, "steel");
        }
        k.Box(-0.86, 1.36, -0.65, 0.86, 1.4, 0.65, Body, "painted steel");
        Trunk(k, 0.6, 0.16);
        return k.P;
    }

    private static Part MagD()
    {
        var k = Mod("Magazine", Mags, "D", "Can rack", "D (2x2): six heavy ammunition cans in an open rack, each with a clamped lid, a carry handle and a stencil, a segmented feed chute down the front to the Ammo port.", 1.18, 2);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.84, 0.078, -0.7, sx * 0.9, 1.16, 0.4, Body, "painted steel");
            foreach (var y in new[] { 0.12, 0.48, 0.84 })
                k.Box(sx * 0.82, y - 0.03, -0.7, sx * 0.86, y, 0.4, Mid, "painted steel");
        }
        k.Box(-0.9, 1.12, -0.7, 0.9, 1.16, 0.4, Body, "painted steel");
        for (var row = 0; row < 3; row++)
            foreach (var sx in new[] { -1.0, 1.0 })
            {
                var (x0, x1, y) = (sx < 0 ? -0.8 : 0.04, sx < 0 ? -0.04 : 0.8, 0.12 + row * 0.36);
                k.Paint(x0, y, -0.66, x1, y + 0.3, 0.34, row == 1 ? Body : Mid);
                k.Box(x0 - 0.006, y + 0.26, -0.67, x1 + 0.006, y + 0.31, 0.35, Dark, "steel");
                k.Box((x0 + x1) / 2 - 0.1, y + 0.306, -0.05, (x0 + x1) / 2 + 0.1, y + 0.33, -0.02, Bare, "steel");
                k.Box(x1 - 0.06, y + 0.12, 0.336, x1 - 0.03, y + 0.24, 0.36, Bare, "steel");
                Placard(k, x0 + 0.16, y + 0.12, 0.34, 0.2, 0.07);
            }
        for (var i = 0; i < 6; i++)
            k.Box(-0.12, 0.42 + i * 0.12, 0.4 + i * 0.0, 0.12, 0.52 + i * 0.12, 0.5, i % 2 == 0 ? Body : Mid, "painted steel");
        Trunk(k, 0.4, 0.44);
        return k.P;
    }

    private static Part MagE()
    {
        var k = Mod("Magazine", Mags, "E", "Armoured", "E (2x2): an armoured magazine box: plate seams and bolt rows, a heavy door on three strap hinges with a dogging wheel, flush blow-out panels in the roof, lifting eyes.", 1.42, 2);
        k.Paint(-0.86, 0.078, -0.8, 0.86, 1.3, 0.5, Body);
        foreach (var y in new[] { 0.5, 0.9 })
            k.Box(-0.864, y, -0.804, 0.864, y + 0.012, 0.504, Dark, "steel");
        for (var i = 0; i < 12; i++)
        {
            var x = -0.77 + i * 0.14;
            foreach (var y in new[] { 0.2, 1.22 })
                k.Box(x - 0.012, y - 0.012, 0.496, x + 0.012, y + 0.012, 0.512, Bare, "steel");
        }
        k.Box(-0.42, 0.3, 0.496, 0.42, 1.14, 0.53, Mid, "painted steel");
        foreach (var y in new[] { 0.38, 0.7, 1.02 })
            k.Box(-0.5, y, 0.5, -0.36, y + 0.06, 0.55, Bare, "steel");
        k.Hull(CylZ(0.22, 0.72, 0.03, 0.526, 0.58), Bare, "steel");
        k.Hull(CylZ(0.22, 0.72, 0.1, 0.576, 0.592), RedPaint, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { 0.0, 1.0 })
            {
                var (z0, z1) = (-0.7 + sz * 0.6, -0.16 + sz * 0.6);
                k.Box(sx * 0.06, 1.296, z0 - 0.012, sx * 0.76, 1.312, z1 + 0.012, Dark, "steel");
                k.Box(sx * 0.07, 1.3, z0, sx * 0.75, 1.32, z1, Mid, "painted steel");
            }
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.7 - 0.03, 1.316, sz * 0.4 - 0.15 - 0.01, sx * 0.7 + 0.03, 1.38, sz * 0.4 - 0.15 + 0.01, Bare, "steel");
        Placard(k, 0.0, 1.2, 0.53, 0.24, 0.07);
        Lamp(k, 0.6, 1.0, Red, 0.5, 0.014);
        Trunk(k, 0.46, 0.26);
        return k.P;
    }

    private static IEnumerable<Part> Modules() =>
    [
        CapA(), CapB(), CapC(), CapD(), CapE(),
        LifeA(), LifeB(), LifeC(), LifeD(), LifeE(),
        SensA(), SensB(), SensC(), SensD(), SensE(),
        FuelA(), FuelB(), FuelC(), FuelD(), FuelE(),
        MagA(), MagB(), MagC(), MagD(), MagE(),
    ];
}
