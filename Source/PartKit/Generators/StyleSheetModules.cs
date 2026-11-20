namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Style sheet step 3 (Karl, 2026-11-19: "Base designs off the battery bank and run a full set of five parts", then "take into
/// account the locker A and E as well, because that looks great"): five 1x1 modules, A to E each, in one language. From the
/// battery bank: a chamfered graphite block on the module plate, louvred radiator sides, a status strip of pips, a hazard band
/// round the foot. From locker E: cut-corner hatches sealed by a neon outline, a release bar, hinge rams, chevrons. From
/// locker A: a lit hood washing the face below it and recessed lit pull channels. One accent colour a part: capacitors ice,
/// life support teal, sensors cyan, fuel amber, magazines red.
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
    private static void Hatch(Kit k, double cx, double w, double y0, double y1, string seal, double zf = W, bool bar = true)
    {
        var c = Math.Min(0.08, w * 0.35);
        k.Face(zf - 0.008, zf + 0.004, Cut(cx, w + 0.012, y0 - 0.012, y1 + 0.012, c + 0.005), seal, "", glow: true);
        k.Face(zf - 0.002, zf + 0.018, Cut(cx, w, y0, y1, c), Mid, "painted steel");
        k.Face(zf + 0.012, zf + 0.024, Cut(cx, w - 0.045, y0 + 0.045, y1 - 0.045, c * 0.7), Body, "painted steel");
        if (!bar)
            return;
        var ym = (y0 + y1) / 2;
        k.Box(cx - w * 0.55, ym - 0.022, zf + 0.022, cx + w * 0.55, ym + 0.022, zf + 0.044, Trim, "steel");
        k.Box(cx - w * 0.62, ym - 0.04, zf + 0.02, cx - w * 0.5, ym + 0.04, zf + 0.036, Dark, "plastic");
        k.Box(cx + w * 0.5, ym - 0.04, zf + 0.02, cx + w * 0.62, ym + 0.04, zf + 0.036, Dark, "plastic");
    }

    /// <summary>Locker A's hood: a light lip over the face with a downlight washing what is under it.</summary>
    private static void Hood(Kit k, double x0, double x1, double y, string col, double zf = W)
    {
        k.Paint(x0, y, zf - 0.03, x1, y + 0.045, zf + 0.05, Trim);
        k.Glow(x0 + 0.03, y - 0.008, zf - 0.01, x1 - 0.03, y + 0.004, zf + 0.04, col);
    }

    /// <summary>Locker A's pull: a recessed channel with a lit strip down it.</summary>
    private static void Pull(Kit k, double x, double y0, double y1, string col, double zf = W)
    {
        k.Box(x - 0.032, y0, zf - 0.004, x + 0.032, y1, zf + 0.016, Dark, "plastic");
        k.Glow(x - 0.01, y0 + 0.035, zf + 0.008, x + 0.01, y1 - 0.035, zf + 0.02, col);
    }

    /// <summary>Locker E's hinge ram: a cylinder and its rod between two brackets, upright at (x, z).</summary>
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

    // ---------------------------------------------------------------- Capacitor banks (ice)
    private const string Caps = "Capacitor banks";

    private static Part CapA()
    {
        var k = Mod("Capacitor", Caps, "A", "Coil stack", "A: a stack of armoured discs on an octagonal base, the charge glowing ice between them, insulators and a terminal on top.", 1.38);
        k.Hull(Oct(0.36, 0.36, 0.11, 0.078, 0.2), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.24, 0.196, 1.06, 8), Ice, "", glow: true);
        for (var i = 0; i < 5; i++)
        {
            var y = 0.26 + i * 0.18;
            k.Hull(Cyl(0, 0, i % 2 == 0 ? 0.31 : 0.29, y, y + 0.1, 8), i % 2 == 0 ? Mid : Body, "painted steel");
        }
        k.Hull(Cyl(0, 0, 0.14, 1.056, 1.12, 8), Trim, "painted steel");
        k.Hull(Cyl(0, 0, 0.1, 1.116, 1.18, 8), Dark, "plastic");
        k.Hull(Cyl(0, 0, 0.12, 1.176, 1.22, 8), Trim, "painted steel");
        k.Hull(Cyl(0, 0, 0.04, 1.216, 1.32, 8), Bare, "steel");
        k.Hull(Cyl(0, 0, 0.07, 1.316, 1.37, 8), HazardY, "hazard paint");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.27 - 0.04, 0.12, 0.356, sx * 0.27 + 0.04, 0.17, 0.372, HazardY, "hazard paint");
        return k.P;
    }

    private static Part CapB()
    {
        var k = Mod("Capacitor", Caps, "B", "Slabs", "B: three slabs of different depths stacked off-line in two side frames, ice light in the gaps, a lit pull on each.", 1.12);
        Skids(k, 0.33);
        double[] th = [0.26, 0.2, 0.3], dz = [0.03, -0.04, 0.0], hw = [0.3, 0.32, 0.28];
        var y = 0.15;
        for (var i = 0; i < 3; i++)
        {
            k.Paint(-hw[i], y, -0.27 + dz[i], hw[i], y + th[i], 0.27 + dz[i], i == 1 ? Body : Mid);
            if (i < 2)
                k.Glow(-0.24, y + th[i] - 0.006, -0.22, 0.24, y + th[i] + 0.066, 0.22, Ice);
            Pull(k, -hw[i] + 0.08 + i * 0.16, y + 0.04, y + th[i] - 0.04, Ice, 0.27 + dz[i]);
            y += th[i] + 0.06;
        }
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.335, 0.126, sz * 0.28, sx * 0.365, 1.1, sz * 0.24, Body, "painted steel");
            k.Box(sx * 0.335, 1.07, -0.28, sx * 0.365, 1.1, 0.28, Body, "painted steel");
            k.Box(sx * 0.326, 0.5, -0.24, sx * 0.338, 0.54, 0.24, Dark, "steel"); // (a tie)
        }
        return k.P;
    }

    private static Part CapC()
    {
        var k = Mod("Capacitor", Caps, "C", "Triple cans", "C: three upright capacitor cans on an octagonal sled, lit rings at staggered heights, brass bus bars to a central terminal.", 1.3);
        k.Hull(Oct(0.36, 0.36, 0.12, 0.078, 0.18), Body, "painted steel");
        (double X, double Z, double Ring)[] cans = [(0, -0.17, 0.5), (-0.17, 0.12, 0.72), (0.17, 0.12, 0.94)];
        foreach (var (x, z, ring) in cans)
        {
            k.Hull(Cyl(x, z, 0.135, 0.176, 0.26, 8), Dark, "dirty steel");
            k.Hull(Cyl(x, z, 0.12, 0.256, 1.12, 8), Mid, "painted steel");
            k.Hull(Cyl(x, z, 0.127, ring, ring + 0.06, 8), Ice, "", glow: true);
            k.Hull(Cyl(x, z, 0.085, 1.116, 1.17, 8), Trim, "painted steel");
        }
        k.Box(-0.19, 1.166, 0.1, 0.19, 1.2, 0.14, Amber, "brass");
        k.Box(-0.02, 1.166, -0.19, 0.02, 1.2, 0.12, Amber, "brass");
        k.Hull(Cyl(0, 0.0, 0.04, 1.16, 1.28, 8), Bare, "steel");
        return k.P;
    }

    private static Part CapD()
    {
        var k = Mod("Capacitor", Caps, "D", "Wedge sink", "D: a wedge with a raked front holding a glowing discharge tube in clamps, a sealed HV hatch below, heat-sink fins along its back.", 1.16);
        k.Profile(-W, W, [(0.078, -W), (0.078, W), (0.48, W), (0.94, 0.0), (0.94, -W)], Mid, "painted steel");
        Hatch(k, -0.04, 0.24, 0.27, 0.44, Ice, bar: false);
        k.Box(-0.09, 0.33, W + 0.012, 0.01, 0.39, W + 0.03, HazardY, "hazard paint");
        Lamp(k, 0.3, 0.36, Ice);
        var (ty, tz) = (0.71, 0.188);
        k.Hull(CylX(ty, tz, 0.045, -0.3, 0.3), Ice, "", glow: true);
        foreach (var x in new[] { -0.33, -0.05, 0.25 })
            k.Hull(CylX(ty, tz, 0.06, x, x + 0.08), Bare, "steel");
        for (var i = 0; i < 8; i++)
        {
            var x = -0.315 + i * 0.09;
            k.Box(x - 0.012, 0.934, -0.34, x + 0.012, 1.14, -0.03, Bare, "steel");
        }
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Hull(TipX(CylZ(sx * 0.2, 0.62, 0.05, -0.02, 0.05), 0.62, 0.0, 0), Dark, "plastic"); // (cable glands on the slope's foot)
        return k.P;
    }

    private static Part CapE()
    {
        var k = Mod("Capacitor", Caps, "E", "Cartridges", "E: a cabinet of four upright slide-in cartridges, one half drawn out, each with an ice edge light and a grip, under a lit hood.", 1.34);
        k.Box(-W, 0.078, -W, W, 0.13, W, Body, "painted steel");
        k.Box(-W, 1.24, -W, W, 1.29, W, Body, "painted steel");
        k.Box(-0.35, 0.126, -W + 0.004, 0.35, 1.244, -0.33, Dark, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.35, 0.126, -W + 0.004, sx * (W + 0.004), 1.244, W - 0.004, Mid, "painted steel");
        double[] xs = [-0.255, -0.085, 0.085, 0.255];
        for (var i = 0; i < 4; i++)
        {
            var out_ = i == 2 ? 0.12 : 0.0;
            k.Paint(xs[i] - 0.07, 0.15, -0.31 + out_, xs[i] + 0.07, 1.2, 0.33 + out_, i % 2 == 0 ? Mid : Body);
            k.Glow(xs[i] + 0.04, 0.3, 0.326 + out_, xs[i] + 0.056, 1.0, 0.34 + out_, i == 2 ? Amber : Ice);
            k.Box(xs[i] - 0.05, 1.06, 0.326 + out_, xs[i] + 0.03, 1.1, 0.37 + out_, Bare, "steel");
            Lamp(k, xs[i] - 0.02, 0.22, i == 2 ? Amber : Green, 0.33 + out_, 0.022);
        }
        Hood(k, -W, W, 1.29, Ice, W - 0.02);
        return k.P;
    }

    // ---------------------------------------------------------------- Life support (teal)
    private const string Life = "Life support";

    private static Part LifeA()
    {
        var k = Mod("Life Support", Life, "A", "Fan drum", "A: an upright octagonal drum with a big teal-lit fan in its top, an intake ring at its foot, a sealed hatch and dial on its front face.", 0.96);
        k.Hull(Cyl(0, 0, 0.35, 0.078, 0.88, 8), Mid, "painted steel");
        k.Hull(Cyl(0, 0, 0.356, 0.2, 0.32, 8), Dark, "dirty steel");
        k.Hull(Cyl(0, 0, 0.36, 0.25, 0.27, 8), Teal, "", glow: true);
        k.Hull(Cyl(0, 0, 0.36, 0.876, 0.92, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.29, 0.9, 0.93, 8), Teal, "", glow: true);
        foreach (var t in new[] { -0.2, 0.0, 0.2 })
        {
            k.Box(-0.3, 0.926, t - 0.012, 0.3, 0.944, t + 0.012, Bare, "steel");
            k.Box(t - 0.012, 0.926, -0.3, t + 0.012, 0.944, 0.3, Bare, "steel");
        }
        k.Hull(Cyl(0, 0, 0.06, 0.94, 0.97, 8), Body, "painted steel");
        Hatch(k, 0, 0.11, 0.36, 0.7, Teal, 0.323, bar: false);
        Dial(k, 0, 0.78, 0.045, Teal, 0.323);
        return k.P;
    }

    private static Part LifeB()
    {
        var k = Mod("Life Support", Life, "B", "Scrubbers", "B: three scrubber canisters on an open frame deck, green lit pulls down them, a blue manifold over the top, a blower under the deck.", 1.4);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.3, 0.078, sz * 0.3, sx * 0.34, 0.3, sz * 0.34, Body, "painted steel");
        k.Box(-0.35, 0.296, -0.35, 0.35, 0.34, 0.35, Mid, "painted steel");
        k.Box(-0.2, 0.12, -0.2, 0.2, 0.3, 0.2, Dark, "dirty steel");
        Lamp(k, 0, 0.21, Teal, 0.2);
        foreach (var x in new[] { -0.22, 0.0, 0.22 })
        {
            k.Hull(Cyl(x, 0.04, 0.1, 0.336, 1.18, 8), Trim, "painted steel");
            k.Hull(Cyl(x, 0.04, 0.106, 0.42, 0.46, 8), Body, "painted steel");
            Pull(k, x, 0.5, 1.1, Green, 0.13);
            k.Hull(Cyl(x, 0.04, 0.04, 1.176, 1.26, 8), Bare, "steel");
        }
        k.Box(-0.3, 1.25, 0.0, 0.3, 1.32, 0.08, Coolant, "painted steel");
        k.Box(0.27, 0.34, -0.03, 0.33, 1.32, 0.03, Coolant, "painted steel");
        return k.P;
    }

    private static Part LifeC()
    {
        var k = Mod("Life Support", Life, "C", "Taper tower", "C: a tall tower narrowing to the top, intake louvres across its head, a tall sealed door on a hinge ram, a lit outlet grille at its foot.", 1.75);
        k.Hull(Frustum(0.36, 0.34, 0.24, 0.34, 0.078, 1.7), Mid, "painted steel");
        k.Box(-0.25, 1.66, -0.3, 0.25, 1.75, 0.3, Body, "painted steel");
        for (var i = 0; i < 4; i++)
            k.Box(-0.2, 1.34 + i * 0.07, 0.336, 0.2, 1.364 + i * 0.07, 0.37, Body, "painted steel");
        k.Box(-0.21, 1.32, 0.33, 0.21, 1.62, 0.346, Dark, "dirty steel");
        Hatch(k, 0.02, 0.16, 0.56, 1.24, Green, 0.34);
        Ram(k, -0.2, 0.39, 0.66, 1.14, -0.2);
        k.Box(-0.27, 0.2, 0.332, 0.27, 0.44, 0.346, Dark, "dirty steel");
        k.Glow(-0.24, 0.23, 0.336, 0.24, 0.41, 0.35, Teal);
        for (var i = 0; i < 3; i++)
            k.Box(-0.26, 0.25 + i * 0.05, 0.352, 0.26, 0.272 + i * 0.05, 0.366, Bare, "steel");
        return k.P;
    }

    private static Part LifeD()
    {
        var k = Mod("Life Support", Life, "D", "Side fans", "D: a lying drum with a big fan in each end, teal behind the grilles, a sealed hatch and dial on its belly-front, on two saddles.", 0.88);
        const double cy = 0.47, r = 0.34;
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.08, 0.078, -0.24, sx * 0.2, 0.24, 0.24, Body, "painted steel");
        k.Hull(CylX(cy, 0, r, -0.27, 0.27), Mid, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Hull(CylX(cy, 0, 0.32, sx * 0.266, sx * 0.34), Body, "painted steel");
            k.Hull(CylX(cy, 0, 0.26, sx * 0.336, sx * 0.348), Teal, "", glow: true);
            foreach (var t in new[] { -0.16, -0.05, 0.06, 0.17 })
                k.Box(sx * 0.344, cy + t - 0.012, -0.25, sx * 0.362, cy + t + 0.012, 0.25, Bare, "steel");
            k.Hull(CylX(cy, 0, 0.05, sx * 0.34, sx * 0.37), Body, "painted steel");
        }
        Hatch(k, -0.06, 0.12, cy - 0.11, cy + 0.11, Teal, 0.314, bar: false);
        Dial(k, 0.15, cy, 0.05, Teal, 0.314);
        return k.P;
    }

    private static Part LifeE()
    {
        var k = Mod("Life Support", Life, "E", "Bottle rack", "E: six upright oxygen bottles racked either side of a slim spine, green bands at their shoulders, a regulator head with teal dials.", 1.22);
        k.Box(-0.08, 0.078, -0.32, 0.08, 1.06, 0.32, Mid, "painted steel");
        k.Box(-0.34, 1.02, -0.1, 0.34, 1.12, 0.1, Body, "painted steel");
        k.Box(-W, 0.078, -0.34, W, 0.12, 0.34, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            foreach (var z in new[] { -0.22, 0.0, 0.22 })
            {
                var x = sx * 0.23;
                k.Hull(Cyl(x, z, 0.1, 0.116, 0.86, 8), Trim, "painted steel");
                k.Hull(Cyl(x, z, 0.104, 0.74, 0.78, 8), Green, "", glow: true);
                k.Hull(Cyl(x, z, 0.06, 0.856, 0.93, 8), Body, "painted steel");
                k.Hull(Cyl(x, z, 0.02, 0.926, 1.024, 8), Bare, "steel");
            }
            k.Box(sx * 0.32, 0.36, -0.34, sx * 0.345, 0.4, 0.34, Dark, "steel"); // (the rack's strap)
        }
        Dial(k, -0.035, 1.07, 0.035, Teal, 0.1);
        Dial(k, 0.045, 0.9, 0.035, Teal, 0.32);
        Lamp(k, 0.0, 0.6, Green, 0.32, 0.024);
        return k.P;
    }

    // ---------------------------------------------------------------- Sensors (cyan)
    private const string Sens = "Sensors";

    /// <summary>A dome: rings at 0, 30 and 60 degrees and the crown point (25 points).</summary>
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

    /// <summary>A small dish on a mast's head: its back, the feed, a cyan horn (tipped up by tilt).</summary>
    private static void SmallDish(Kit k, double cy, double cz, double r, double tilt)
    {
        k.Hull(TipX(RingZ(0, cy, r, cz + 0.05).Concat(RingZ(0, cy, r * 0.35, cz - 0.04)), cy, cz, tilt), Trim, "painted steel");
        k.Hull(TipX(CylZ(0, cy, 0.018, cz + 0.046, cz + 0.2), cy, cz, tilt), Dark, "plastic");
        k.Hull(TipX(CylZ(0, cy, 0.026, cz + 0.196, cz + 0.23), cy, cz, tilt), Cyan, "", glow: true);
    }

    private static Part SensA()
    {
        var k = Mod("Sensor", Sens, "A", "Pedestal", "A: a tapered pedestal with a service box on its front, a collar, a mast with a small dish and a red beacon.", 1.72);
        k.Hull(Frustum(0.34, 0.34, 0.17, 0.17, 0.078, 0.62), Mid, "painted steel");
        k.Box(-0.13, 0.2, 0.12, 0.13, 0.44, 0.31, Body, "painted steel");
        Hatch(k, 0, 0.09, 0.23, 0.41, Cyan, 0.31, bar: false);
        k.Hull(Cyl(0, 0, 0.2, 0.616, 0.68, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.205, 0.636, 0.656, 8), Cyan, "", glow: true);
        k.Hull(Cyl(0, 0, 0.035, 0.676, 1.64, 8), Bare, "steel");
        SmallDish(k, 1.38, 0.0, 0.2, 0.35);
        k.Hull(Cyl(0, 0, 0.03, 1.636, 1.68, 8), Red, "", glow: true);
        return k.P;
    }

    private static Part SensB()
    {
        var k = Mod("Sensor", Sens, "B", "Radome", "B: a radome on a vented octagonal drum, a cyan band where they meet, a small sealed hatch and lamps on the drum.", 0.86);
        k.Hull(Cyl(0, 0, 0.34, 0.078, 0.5, 8), Mid, "painted steel");
        foreach (var y in new[] { 0.16, 0.22, 0.28 })
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.31, y, -0.08, sx * 0.322, y + 0.03, 0.08, Dark, "dirty steel");
        k.Hull(Cyl(0, 0, 0.346, 0.46, 0.5, 8), Cyan, "", glow: true);
        k.Hull(Dome(0.32, 0.496), Trim, "plastic");
        Hatch(k, -0.04, 0.08, 0.27, 0.42, Cyan, 0.314, bar: false);
        Lamp(k, 0.09, 0.36, Green, 0.314, 0.02);
        Lamp(k, 0.09, 0.28, Cyan, 0.314, 0.02);
        return k.P;
    }

    private static Part SensC()
    {
        var k = Mod("Sensor", Sens, "C", "Array", "C: a tilted phased-array panel, its tiles edged in cyan, on a raked wedge base with three lamps across its front.", 1.3);
        k.Profile(-W, W, [(0.078, -W), (0.078, W), (0.36, W), (0.58, -0.1), (0.58, -W)], Mid, "painted steel");
        foreach (var (x, c) in new[] { (-0.2, Cyan), (0.0, Cyan), (0.2, Green) })
            Lamp(k, x, 0.3, c);
        const double py = 0.98, pz = 0.0, tilt = 0.5;
        IEnumerable<double[]> Slab(double x0, double x1, double y0, double y1, double z0, double z1) =>
            TipX(new[] { x0, x1 }.SelectMany(x => new[] { y0, y1 }.SelectMany(y => new[] { z0, z1 }.Select(z => new[] { x, y, z }))), py, pz, tilt);
        k.Hull(Slab(-0.34, 0.34, py - 0.3, py + 0.3, -0.03, 0.03), Body, "painted steel");
        k.Hull(Slab(-0.32, 0.32, py - 0.28, py + 0.28, 0.026, 0.038), Cyan, "", glow: true);
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                k.Hull(Slab(-0.3 + i * 0.2 + 0.006, -0.3 + (i + 1) * 0.2 - 0.006, py - 0.26 + j * 0.173 + 0.006, py - 0.26 + (j + 1) * 0.173 - 0.006, 0.03, 0.05), Dark, "plastic");
        k.Box(-0.06, 0.576, -0.24, 0.06, 0.95, -0.08, Body, "painted steel"); // (its strut)
        return k.P;
    }

    private static Part SensD()
    {
        var k = Mod("Sensor", Sens, "D", "Braced mast", "D: a lattice mast on a low octagonal base, braced by four struts, a crossarm with cyan lamps and whips, a red beacon on top.", 2.0);
        k.Hull(Oct(0.34, 0.34, 0.12, 0.078, 0.26), Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
            {
                k.Box(sx * 0.06 - 0.014, 0.256, sz * 0.06 - 0.014, sx * 0.06 + 0.014, 1.86, sz * 0.06 + 0.014, Bare, "steel");
                k.Hull(Brace([sx * 0.26, 0.256, sz * 0.26], [sx * 0.08, 0.9, sz * 0.08], 0.012), Mid, "painted steel");
            }
        for (var i = 0; i < 5; i++)
        {
            var y = 0.42 + i * 0.29;
            k.Box(-0.08, y, -0.08, 0.08, y + 0.03, 0.08, Body, "painted steel");
        }
        k.Box(-0.1, 0.256, 0.1, 0.1, 0.46, 0.24, Mid, "painted steel");
        Lamp(k, 0, 0.36, Cyan, 0.24, 0.022);
        k.Box(-0.36, 1.56, -0.02, 0.36, 1.6, 0.02, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.33, 1.6, -0.025, sx * 0.37, 1.65, 0.025, Dark, "plastic");
            k.Glow(sx * 0.335, 1.646, -0.02, sx * 0.365, 1.68, 0.02, Cyan);
            k.Box(sx * 0.2 - 0.006, 1.6, -0.006, sx * 0.2 + 0.006, 1.8, 0.006, Bare, "steel"); // (a whip)
        }
        k.Box(-0.09, 1.856, -0.09, 0.09, 1.88, 0.09, Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.035, 1.876, 1.93, 8), Red, "", glow: true);
        return k.P;
    }

    private static Part SensE()
    {
        var k = Mod("Sensor", Sens, "E", "Tracker", "E: a squat turret drum with a lit turntable, a yoke carrying a big tracking dish, an elevation ram behind, a cyan feed horn.", 1.42);
        const double H = 0.4;
        k.Hull(Cyl(0, 0, 0.34, 0.078, H, 8), Mid, "painted steel");
        Lamp(k, 0.0, 0.32, Cyan, 0.314, 0.024);
        k.Hull(Cyl(0, 0, 0.3, H - 0.004, H + 0.07, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.305, H + 0.02, H + 0.04, 8), Cyan, "", glow: true);
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.19, H + 0.066, -0.05, sx * 0.23, 0.98, 0.05, Mid, "painted steel");
        const double cy = 0.94, tilt = 0.5;
        k.Hull(TipX(RingZ(0, cy, 0.34, 0.07).Concat(RingZ(0, cy, 0.13, -0.07)), cy, 0, tilt), Trim, "painted steel");
        k.Hull(TipX(RingZ(0, cy, 0.3, 0.066).Concat(RingZ(0, cy, 0.3, 0.08)), cy, 0, tilt), Dark, "plastic");
        k.Hull(TipX(CylZ(0, cy, 0.02, 0.076, 0.3), cy, 0, tilt), Bare, "steel");
        k.Hull(TipX(CylZ(0, cy, 0.04, 0.296, 0.35), cy, 0, tilt), Cyan, "", glow: true);
        k.Hull(TipX(CylZ(0, cy, 0.06, -0.2, -0.066), cy, 0, tilt), Body, "painted steel");
        Ram(k, 0, -0.24, H + 0.1, 0.8, 0);
        return k.P;
    }

    // ---------------------------------------------------------------- Fuel tanks (amber)
    private const string Fuel = "Fuel tanks";

    private static Part FuelA()
    {
        var k = Mod("Fuel", Fuel, "A", "Can", "A: a tall cut-corner fuel can with X ribs pressed in its sides, carry handles and a red filler on top, an amber sight tube down its front.", 1.3);
        k.Hull(Oct(0.3, 0.36, 0.08, 0.078, 1.2), Mid, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Hull(Brace([sx * 0.31, 0.2, -0.26], [sx * 0.31, 1.08, 0.26], 0.016), Body, "painted steel");
            k.Hull(Brace([sx * 0.314, 0.2, 0.26], [sx * 0.314, 1.08, -0.26], 0.016), Body, "painted steel");
        }
        foreach (var z in new[] { -0.16, 0.0, 0.16 })
            k.Box(-0.16, 1.196, z - 0.015, 0.16, 1.25, z + 0.015, Body, "painted steel");
        k.Hull(Cyl(0.0, -0.28, 0.06, 1.196, 1.27, 8), Bare, "steel");
        k.Hull(Cyl(0.0, -0.28, 0.07, 1.266, 1.3, 8), RedPaint, "painted steel");
        k.Box(0.12, 0.22, 0.356, 0.22, 1.08, 0.372, Dark, "plastic");
        k.Glow(0.145, 0.25, 0.366, 0.195, 0.78, 0.38, Amber);
        Hatch(k, -0.1, 0.13, 0.27, 0.6, Amber, 0.36, bar: false);
        return k.P;
    }

    private static Part FuelB()
    {
        var k = Mod("Fuel", Fuel, "B", "Lying tank", "B: a tank lying across two saddles on skids, an amber band at its middle, a valve box with a lit window and red wheel on the front.", 0.84);
        k.Hull(CylX(0.5, 0, 0.3, -0.34, 0.34), Mid, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Hull(CylX(0.5, 0, 0.22, sx * 0.336, sx * 0.372), Body, "painted steel");
            k.Profile(sx * 0.19, sx * 0.26, [(0.126, -0.32), (0.126, 0.32), (0.3, 0.32), (0.36, 0.26), (0.36, -0.26), (0.3, -0.32)], Body, "painted steel");
            k.Hull(CylX(0.5, 0, 0.31, sx * 0.196, sx * 0.24), Dark, "dirty steel");
        }
        Skids(k, 0.225);
        k.Hull(CylX(0.5, 0, 0.306, -0.04, 0.04), Amber, "", glow: true);
        k.Box(-0.11, 0.26, 0.22, 0.11, 0.44, 0.33, Body, "painted steel");
        k.Box(-0.08, 0.3, 0.326, 0.0, 0.4, 0.34, Dark, "plastic");
        k.Glow(-0.07, 0.31, 0.334, -0.01, 0.39, 0.344, Amber);
        k.Hull(CylZ(0.055, 0.35, 0.04, 0.326, 0.37), RedPaint, "painted steel");
        return k.P;
    }

    private static Part FuelC()
    {
        var k = Mod("Fuel", Fuel, "C", "Caged tank", "C: an upright tank in a square cage of corner posts, an amber sight tube on the front post, a hood at the cage's head.", 1.52);
        k.Box(-W, 0.078, -W, W, 0.12, W, Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.3, 0.116, 1.38, 8), Mid, "painted steel");
        k.Hull(Cyl(0, 0, 0.21, 1.376, 1.44, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.306, 0.3, 0.34, 8), Dark, "dirty steel");
        k.Hull(Cyl(0, 0, 0.306, 1.2, 1.24, 8), Dark, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.32, 0.116, sz * 0.32, sx * (W + 0.004), 1.5, sz * (W + 0.004), Body, "painted steel");
        k.Box(-W, 1.46, -W, W, 1.5, -0.32, Body, "painted steel");
        k.Box(-W, 1.462, -0.32, -0.32, 1.498, 0.32, Body, "painted steel");
        k.Box(0.32, 1.462, -0.32, W, 1.498, 0.32, Body, "painted steel");
        k.Box(0.326, 0.26, W + 0.002, 0.372, 1.3, W + 0.024, Dark, "plastic");
        k.Glow(0.338, 0.29, W + 0.012, 0.36, 0.9, W + 0.03, Amber);
        Hood(k, -0.32, 0.32, 1.46, Amber, 0.34);
        k.Hull(Cyl(0.1, -0.1, 0.05, 1.436, 1.5, 8), RedPaint, "painted steel");
        return k.P;
    }

    private static Part FuelD()
    {
        var k = Mod("Fuel", Fuel, "D", "Bottles", "D: two tall fuel bottles at the back piped into a round pump with a finned motor on top, an amber dial and sealed hatch on the pump.", 1.36);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.17;
            k.Hull(Cyl(x, -0.18, 0.15, 0.078, 1.24, 8), Trim, "painted steel");
            k.Hull(Cyl(x, -0.18, 0.155, 0.9, 0.96, 8), Amber, "", glow: true);
            k.Hull(Cyl(x, -0.18, 0.1, 1.236, 1.3, 8), Body, "painted steel");
            k.Box(x - 0.025, 1.296, -0.2, x + 0.025, 1.34, 0.08, Bare, "steel");
            k.Box(x - 0.025, 0.62, 0.03, x + 0.025, 1.344, 0.08, Bare, "steel");
        }
        k.Hull(Cyl(0, 0.1, 0.2, 0.078, 0.5, 8), Mid, "painted steel");
        k.Box(-0.15, 0.496, 0.0, 0.15, 0.66, 0.2, Body, "painted steel");
        for (var i = 0; i < 4; i++)
            k.Box(-0.17, 0.52 + i * 0.035, -0.01, 0.17, 0.536 + i * 0.035, 0.21, Bare, "steel");
        Hatch(k, 0.0, 0.07, 0.14, 0.33, Amber, 0.285, bar: false);
        Dial(k, 0.0, 0.42, 0.04, Amber, 0.285);
        return k.P;
    }

    private static Part FuelE()
    {
        var k = Mod("Fuel", Fuel, "E", "Sphere", "E: a pressure sphere held in a collar on four struts over an octagonal pedestal, an amber band round it, a valve crown.", 1.2);
        k.Hull(Oct(0.34, 0.34, 0.12, 0.078, 0.28), Body, "painted steel");
        Lamp(k, 0.22, 0.2, Amber, 0.34, 0.022);
        foreach (var (x, z) in new[] { (0.0, 0.26), (0.0, -0.26), (0.26, 0.0), (-0.26, 0.0) })
            k.Box(x - 0.025, 0.276, z - 0.025, x + 0.025, 0.47, z + 0.025, Mid, "painted steel");
        k.Hull(Cyl(0, 0, 0.3, 0.44, 0.5, 8), Body, "painted steel");
        Sphere(k, 0.33, 0.8, Amber);
        k.Hull(Cyl(0, 0, 0.05, 1.11, 1.18, 8), Trim, "painted steel");
        k.Hull(Cyl(0, 0, 0.04, 1.176, 1.21, 8), RedPaint, "painted steel");
        return k.P;
    }

    // ---------------------------------------------------------------- Magazines (red), 2x2
    // A 1x1 plug has no Ammo coupling (module-sockets.md 2.5: it starts at 2x2), so magazines are 2x2 modules, each designed at
    // that size, not scaled. Each feeds the Ammo port at the front middle of the plug through a hazard-rimmed feed throat.
    private const string Mags = "Magazines";

    /// <summary>The feed throat over the Ammo port: a box down onto the plug, a hazard rim and a ready lamp.</summary>
    private static void Throat(Kit k, double z0, double y1)
    {
        var (_, z) = PortAt(2, "Ammo");
        k.Box(-0.16, 0.078, z0, 0.16, y1, z + 0.14, Body, "painted steel");
        k.Box(-0.17, y1 - 0.05, z0, 0.17, y1 - 0.02, z + 0.15, HazardY, "hazard paint");
        Lamp(k, 0, y1 - 0.12, Red, z + 0.14, 0.025);
    }

    private static Part MagA()
    {
        var k = Mod("Magazine", Mags, "A", "Stepped crate", "A (2x2): a two-step ammo crate: a heavy lower box with two red-sealed loading hatches, a stepped-back top with a round counter, a hazard-rimmed lid on rams, the feed throat down to the Ammo port.", 1.72, 2);
        k.Box(-0.85, 0.078, -0.85, 0.85, 0.9, 0.55, Mid, "painted steel");
        k.Box(-0.7, 0.896, -0.75, 0.7, 1.6, 0.35, Body, "painted steel");
        Throat(k, 0.5, 0.46);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            Hatch(k, sx * 0.52, 0.24, 0.28, 0.78, Red, 0.55);
            k.Box(sx * 0.85 - 0.004 * sx, 0.5, -0.2, sx * 0.88, 0.56, 0.2, Bare, "steel"); // (a carry handle)
        }
        Dial(k, -0.3, 1.25, 0.12, Amber, 0.35);
        Lamp(k, 0.1, 1.25, Green, 0.35, 0.04);
        Lamp(k, 0.3, 1.25, Red, 0.35, 0.04);
        k.Box(-0.6, 1.596, -0.66, 0.6, 1.63, 0.26, HazardY, "hazard paint");
        k.Box(-0.56, 1.626, -0.62, 0.56, 1.66, 0.22, Dark, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            Ram(k, sx * 0.62, -0.79, 1.0, 1.5, sx * 0.62);
        return k.P;
    }

    private static Part MagB()
    {
        var k = Mod("Magazine", Mags, "B", "Twin drums", "B (2x2): two rotary drums side by side between end plates on rails, red-lit hubs on the front plate, a feed housing across the top and the feed throat down to the Ammo port.", 1.32, 2);
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.62, 0.078, -0.9, sx * 0.74, 0.14, 0.9, Body, "painted steel");
        k.Paint(-0.95, 0.136, -0.72, 0.95, 1.2, -0.64, Mid);
        k.Paint(-0.95, 0.136, 0.4, 0.95, 1.2, 0.48, Mid);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.46;
            k.Hull(CylZ(x, 0.66, 0.42, -0.646, 0.404), Body, "painted steel");
            foreach (var z in new[] { -0.45, -0.2, 0.05 })
                k.Hull(CylZ(x, 0.66, 0.426, z, z + 0.05), Dark, "dirty steel");
            k.Hull(CylZ(x, 0.66, 0.16, 0.476, 0.49), Red, "", glow: true);
            k.Hull(CylZ(x, 0.66, 0.1, 0.478, 0.53), Bare, "steel");
            Pull(k, sx * 0.88, 0.3, 1.0, Red, 0.48);
        }
        k.Box(-0.9, 1.196, -0.68, 0.9, 1.3, 0.44, Body, "painted steel");
        Throat(k, 0.476, 0.4);
        return k.P;
    }

    private static Part MagC()
    {
        var k = Mod("Magazine", Mags, "C", "Tiered", "C (2x2): three wide tiers stepping back like stairs, two ammo drawers in each riser with red lit pulls and state lamps (one red: empty), the Ammo port fed under the bottom tier.", 1.36, 2);
        for (var i = 0; i < 3; i++)
        {
            var (y, zf) = (0.078 + i * 0.42, 0.85 - i * 0.36);
            k.Paint(-0.9 + i * 0.03, y, -0.9, 0.9 - i * 0.03, y + 0.424, zf, i == 1 ? Body : Mid);
            foreach (var sx in new[] { -1.0, 1.0 })
            {
                var cx = sx * 0.43;
                k.Face(zf - 0.004, zf + 0.018, Cut(cx, 0.36, y + 0.1, y + 0.36, 0.05), Body, "painted steel");
                k.Box(cx - 0.22, y + 0.2, zf + 0.014, cx + 0.16, y + 0.26, zf + 0.03, Dark, "plastic");
                k.Glow(cx - 0.2, y + 0.218, zf + 0.022, cx + 0.14, y + 0.242, zf + 0.036, Red);
                Lamp(k, cx + 0.25, y + 0.395, i == 0 && sx > 0 ? Red : Green, zf, 0.02);
            }
        }
        return k.P;
    }

    private static Part MagD()
    {
        var k = Mod("Magazine", Mags, "D", "Belt feed", "D (2x2): a wide raked belt box, twin feed chutes rising from its back to the guns above with brass links showing, the feed throat down to the Ammo port in front, a sealed hatch.", 2.4, 2);
        k.Profile(-0.88, 0.88, [(0.078, -0.88), (0.078, 0.46), (0.62, 0.46), (1.0, -0.2), (1.0, -0.88)], Mid, "painted steel");
        Throat(k, 0.42, 0.42);
        Hatch(k, -0.55, 0.24, 0.22, 0.56, Red, 0.46);
        Dial(k, 0.55, 0.38, 0.1, Amber, 0.46);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.4;
            k.Box(x - 0.11, 0.996, -0.7, x + 0.11, 2.3, -0.5, Body, "painted steel");
            k.Box(x - 0.11, 2.2, -0.5, x + 0.11, 2.3, -0.2, Body, "painted steel");
            for (var i = 0; i < 14; i++)
                k.Box(x - 0.08, 1.06 + i * 0.08, -0.504, x + 0.08, 1.1 + i * 0.08, -0.488, Amber, "brass");
            k.Glow(x - 0.04, 2.296, -0.36, x + 0.04, 2.32, -0.26, Red);
        }
        return k.P;
    }

    private static Part MagE()
    {
        var k = Mod("Magazine", Mags, "E", "Armoured", "E (2x2): a faceted armoured hull like a turret's, a heavy hatch between two hinge rams, four blow-out panels on top edged in hazard paint, the feed throat under its brow.", 1.66, 2);
        var pts = new List<double[]>();
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
            {
                pts.Add([sx * 0.9, 0.078, sz > 0 ? 0.5 : -0.9]);
                pts.Add([sx * 0.9, 0.72, sz > 0 ? 0.5 : -0.9]);
                pts.Add([sx * 0.62, 1.6, sz > 0 ? 0.1 : -0.7]);
            }
        k.Hull(pts, Mid, "painted steel");
        Throat(k, 0.46, 0.4);
        Hatch(k, 0, 0.3, 0.42, 0.68, Red, 0.5, bar: false);
        foreach (var sx in new[] { -1.0, 1.0 })
            Ram(k, sx * 0.42, 0.54, 0.44, 0.66, sx * 0.42);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { 0.0, 1.0 })
            {
                var (z0, z1) = (-0.64 + sz * 0.38, -0.3 + sz * 0.38);
                k.Box(sx * 0.04, 1.596, z0, sx * 0.54, 1.63, z1, HazardY, "hazard paint");
                k.Box(sx * 0.07, 1.626, z0 + 0.03, sx * 0.51, 1.654, z1 - 0.03, Dark, "dirty steel");
            }
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Glow(sx * 0.2 - 0.12, 0.74, 0.47, sx * 0.2 + 0.12, 0.77, 0.51, Red);
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
