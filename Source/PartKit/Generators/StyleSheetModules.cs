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

    private static Kit Mod(string item, string section, string letter, string title, string desc, double H) =>
        Reactor("", 1, desc, H, $"Style {item} {letter} {title}", section);

    /// <summary>The battery's block: a profile across x with the top front and back edges chamfered.</summary>
    private static void Block(Kit k, double y0, double y1, double hx = W, double hz = W, string col = Mid, double ch = 0.12) =>
        k.Profile(-hx, hx, [(y0, -hz), (y0, hz), (y1 - ch, hz), (y1, hz - ch), (y1, -hz + ch), (y1 - ch, -hz)], col, "painted steel");

    /// <summary>The battery's louvres down the +-x sides: a dark recess and slats.</summary>
    private static void Louvres(Kit k, double y0, double y1, int rows, double hx = W, double hz = W)
    {
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * hx - 0.012, y0, -hz + 0.12, sx * hx + 0.004 * sx, y1, hz - 0.12, Dark, "dirty steel");
            for (var i = 0; i < rows; i++)
            {
                var y = y0 + 0.04 + i * (y1 - y0 - 0.1) / Math.Max(1, rows - 1);
                k.Box(sx * hx, y, -hz + 0.14, sx * (hx + 0.04), y + 0.022, hz - 0.14, Body, "painted steel");
            }
        }
    }

    /// <summary>The battery's status strip: a dark bar with lit pips, the first <paramref name="lit"/> in the part's colour.</summary>
    private static void Strip(Kit k, double x0, double x1, double y, int lit, string on, double zf = W)
    {
        k.Box(x0, y, zf - 0.004, x1, y + 0.07, zf + 0.014, Dark, "plastic");
        var n = (int)((x1 - x0 - 0.03) / 0.06);
        for (var i = 0; i < n; i++)
            k.Glow(x0 + 0.03 + i * 0.06, y + 0.02, zf + 0.008, x0 + 0.07 + i * 0.06, y + 0.05, zf + 0.02, i < lit ? on : Amber);
    }

    /// <summary>A vertical gauge: a dark backing, <paramref name="segs"/> segments, the lowest <paramref name="lit"/> lit.</summary>
    private static void Gauge(Kit k, double x0, double x1, double y0, double y1, int segs, int lit, string on, double zf = W)
    {
        k.Box(x0, y0, zf - 0.006, x1, y1, zf + 0.012, Dark, "plastic");
        var step = (y1 - y0 - 0.03) / segs;
        for (var i = 0; i < segs; i++)
        {
            var (a, b) = (y0 + 0.02 + i * step, y0 + 0.02 + (i + 1) * step - 0.012);
            if (i < lit)
                k.Glow(x0 + 0.015, a, zf + 0.006, x1 - 0.015, b, zf + 0.018, on);
            else
                k.Paint(x0 + 0.015, a, zf + 0.006, x1 - 0.015, b, zf + 0.018, Mid);
        }
    }

    /// <summary>The battery's hazard band round the foot, with locker E's chevrons in dark on its sides and front corners.</summary>
    private static void Foot(Kit k, double hx = W, double hz = W)
    {
        k.Box(-hx - 0.006, 0.078, -hz - 0.006, hx + 0.006, 0.15, hz + 0.006, HazardY, "hazard paint");
        var n = (int)((2 * hz - 0.08) / 0.11);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            for (var i = 0; i < n; i++)
            {
                var z = -n * 0.11 / 2 + i * 0.11 + 0.01;
                k.Profile(sx * (hx + 0.002), sx * (hx + 0.013), [(0.092, z), (0.092, z + 0.03), (0.136, z + 0.07), (0.136, z + 0.04)], Dark, "dirty steel");
            }
            if (hx > 0.3) // (a chevron each side of the coupling block)
            {
                var x = sx > 0 ? 0.25 : -0.32;
                k.Face(hz + 0.002, hz + 0.013, [(x, 0.092), (x + 0.03, 0.092), (x + 0.07, 0.136), (x + 0.04, 0.136)], Dark, "dirty steel");
            }
        }
    }

    /// <summary>A deck over the hazard band's top, for parts with no block standing on it (else the band reads as a yellow floor).</summary>
    private static void Deck(Kit k) => k.Box(-W + 0.004, 0.146, -W + 0.004, W - 0.004, 0.164, W - 0.004, Body, "dirty steel");

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

    // ---------------------------------------------------------------- Capacitor banks (ice)
    private const string Caps = "Capacitor banks";

    private static Part CapA()
    {
        const double H = 1.1;
        var k = Mod("Capacitor", Caps, "A", "Block", "A: the battery block, shorter: a sealed service hatch, a tall charge gauge in ice under a lit hood, terminal posts on top.", H);
        Block(k, 0.078, H);
        Louvres(k, 0.2, H - 0.2, 5);
        Hatch(k, -0.16, 0.15, 0.27, 0.78, Ice);
        Gauge(k, 0.08, 0.26, 0.27, 0.78, 8, 6, Ice);
        Hood(k, -0.34, 0.34, 0.84, Ice);
        Strip(k, -0.27, 0.27, 0.9, 6, Ice);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Hull(Cyl(sx * 0.17, -0.08, 0.05, H - 0.01, H + 0.06, 8), Dark, "plastic");
            k.Hull(Cyl(sx * 0.17, -0.08, 0.032, H + 0.056, H + 0.1, 8), sx < 0 ? Trim : HazardY, "steel");
        }
        Foot(k);
        return k.P;
    }

    private static Part CapB()
    {
        var k = Mod("Capacitor", Caps, "B", "Stacked", "B: three armoured capacitor slabs stacked between corner posts, the charge glowing ice in the gaps, a lit pull and tag on each.", 1.08);
        for (var i = 0; i < 3; i++)
        {
            var y = 0.16 + i * 0.3;
            k.Paint(-W + 0.02, y, -W + 0.02, W - 0.02, y + 0.24, W - 0.02, i == 1 ? Body : Mid);
            if (i < 2)
                k.Glow(-W + 0.06, y + 0.234, -W + 0.06, W - 0.06, y + 0.306, W - 0.06, Ice);
            Pull(k, -0.2, y + 0.04, y + 0.2, Ice, W - 0.02);
            k.Box(-0.1, y + 0.08, W - 0.024, 0.1, y + 0.16, W - 0.006, Trim, "steel"); // (a label plate)
            k.Glow(0.16, y + 0.09, W - 0.024, 0.22, y + 0.15, W - 0.008, i == 2 ? Amber : Ice);
        }
        k.Box(-W + 0.03, 0.148, -W + 0.03, W - 0.03, 0.164, W - 0.03, Dark, "dirty steel"); // (the slabs' seat)
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * (W - 0.035), 0.146, sz * (W - 0.035), sx * (W + 0.012), 1.06, sz * (W + 0.012), Body, "painted steel");
        Foot(k);
        return k.P;
    }

    private static Part CapC()
    {
        const double H = 1.32;
        var k = Mod("Capacitor", Caps, "C", "Twin cans", "C: the battery block cut down to a plinth with a sealed hatch, two upright capacitor cans on it with lit rings, bussed across the top.", H);
        Block(k, 0.078, 0.56, ch: 0.06);
        Louvres(k, 0.18, 0.48, 3);
        Hatch(k, -0.17, 0.15, 0.27, 0.5, Ice, bar: false);
        Strip(k, 0.02, 0.32, 0.36, 3, Ice);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.17;
            k.Hull(Cyl(x, 0, 0.15, 0.556, 0.64, 8), Body, "painted steel");
            k.Hull(Cyl(x, 0, 0.135, 0.636, 1.24, 8), Mid, "painted steel");
            k.Hull(Cyl(x, 0, 0.143, 0.88, 0.94, 8), Ice, "", glow: true);
            k.Hull(Cyl(x, 0, 0.1, 1.236, 1.29, 8), Trim, "painted steel");
        }
        k.Box(-0.2, 1.286, -0.03, 0.2, 1.32, 0.03, Amber, "brass");
        Foot(k);
        return k.P;
    }

    private static Part CapD()
    {
        const double H = 1.0;
        var k = Mod("Capacitor", Caps, "D", "Heat sink", "D: the battery block crowned with a heat-sink fin array, one big sealed high-voltage hatch with an ice discharge pull beside it.", H + 0.2);
        Block(k, 0.078, H, ch: 0.08);
        Louvres(k, 0.2, 0.84, 4);
        Hatch(k, -0.06, 0.26, 0.27, 0.76, Ice);
        k.Box(-0.11, 0.62, W + 0.022, -0.01, 0.7, W + 0.034, HazardY, "hazard paint"); // (the HV plate)
        Pull(k, 0.29, 0.3, 0.74, Ice);
        Strip(k, -0.27, 0.27, 0.82, 5, Ice);
        for (var i = 0; i < 7; i++)
        {
            var x = -0.27 + i * 0.09;
            k.Box(x - 0.012, H - 0.006, -0.25, x + 0.012, H + 0.19, 0.25, Bare, "steel");
        }
        k.Box(-0.3, H + 0.19, -0.03, 0.3, H + 0.214, 0.03, Body, "painted steel"); // (the fins' tie bar)
        Foot(k);
        return k.P;
    }

    private static Part CapE()
    {
        var k = Mod("Capacitor", Caps, "E", "Rack", "E: an open rack of four slide-in capacitor cartridges, each with a lit pull channel and charge lamps, under a lit hood.", 1.34);
        k.Box(-W, 0.078, -W, W, 0.12, W, Body, "painted steel");
        k.Box(-W, 1.26, -W, W, 1.3, W, Body, "painted steel");
        k.Box(-0.33, 0.116, -W + 0.004, 0.33, 1.264, -0.34, Dark, "dirty steel");
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.33, 0.116, sz * 0.33, sx * (W + 0.004), 1.264, sz * (W + 0.004), Mid, "painted steel");
        for (var i = 0; i < 4; i++)
        {
            var y = 0.15 + i * 0.275;
            k.Paint(-0.31, y, -0.32, 0.31, y + 0.23, 0.33, i % 2 == 0 ? Mid : Body);
            foreach (var sx in new[] { -1.0, 1.0 })
                k.Box(sx * 0.305, y - 0.024, -0.32, sx * 0.322, y + 0.004, 0.32, Dark, "steel"); // (its rail)
            k.Box(-0.2, y + 0.075, 0.326, 0.2, y + 0.155, 0.346, Dark, "plastic"); // (the pull channel, across)
            k.Glow(-0.17, y + 0.105, 0.336, 0.17, y + 0.125, 0.352, Ice);
            k.Glow(0.24, y + 0.09, 0.326, 0.28, y + 0.14, 0.342, i < 3 ? Green : Amber);
        }
        Hood(k, -W, W, 1.3, Ice, W - 0.02);
        return k.P;
    }

    // ---------------------------------------------------------------- Life support (teal)
    private const string Life = "Life support";

    /// <summary>A front fan: a dark recess, a teal glow behind horizontal grille slats and a hub.</summary>
    private static void FrontFan(Kit k, double cx, double cy, double r, double zf = W)
    {
        k.Box(cx - r - 0.04, cy - r - 0.04, zf - 0.01, cx + r + 0.04, cy + r + 0.04, zf + 0.002, Dark, "dirty steel");
        k.Hull(CylZ(cx, cy, r, zf - 0.006, zf + 0.006), Teal, "", glow: true);
        for (var i = 0; i < 5; i++)
        {
            var y = cy - r + 0.02 + i * (2 * r - 0.06) / 4;
            k.Box(cx - r - 0.02, y, zf + 0.008, cx + r + 0.02, y + 0.022, zf + 0.022, Bare, "steel");
        }
        k.Hull(CylZ(cx, cy, 0.045, zf + 0.004, zf + 0.036), Body, "painted steel");
    }

    private static Part LifeA()
    {
        const double H = 1.3;
        var k = Mod("Life Support", Life, "A", "Block", "A: the battery block with a big fan behind a grille on its front, washed teal by a hood over it, an outlet vent on top.", H);
        Block(k, 0.078, H);
        Louvres(k, 0.2, H - 0.2, 6);
        FrontFan(k, 0, 0.55, 0.2);
        Hood(k, -0.3, 0.3, 0.84, Teal);
        Strip(k, -0.27, 0.27, 0.92, 5, Teal);
        k.Box(-0.2, H - 0.006, -0.2, 0.2, H + 0.05, 0.12, Body, "painted steel");
        for (var i = 0; i < 4; i++)
            k.Box(-0.18, H + 0.046, -0.17 + i * 0.075, 0.18, H + 0.066, -0.13 + i * 0.075, Dark, "dirty steel");
        Deck(k);
        Foot(k);
        return k.P;
    }

    private static Part LifeB()
    {
        var k = Mod("Life Support", Life, "B", "Scrubber", "B: a battery-block plinth with a sealed hatch, two scrubber canisters on it with lit pull channels down their fronts, piped over the top.", 1.44);
        Block(k, 0.078, 0.56, ch: 0.06);
        Louvres(k, 0.18, 0.48, 3);
        Hatch(k, -0.17, 0.15, 0.27, 0.5, Teal, bar: false);
        Strip(k, 0.02, 0.32, 0.36, 3, Teal);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.17;
            k.Hull(Cyl(x, 0.02, 0.13, 0.556, 1.34, 8), Trim, "painted steel");
            k.Hull(Cyl(x, 0.02, 0.137, 0.64, 0.68, 8), Body, "painted steel");
            k.Hull(Cyl(x, 0.02, 0.137, 1.22, 1.26, 8), Body, "painted steel");
            Pull(k, x, 0.7, 1.2, Green, 0.14);
            k.Hull(Cyl(x, 0.02, 0.06, 1.336, 1.4, 8), Body, "painted steel");
        }
        k.Box(-0.2, 1.37, -0.01, 0.2, 1.43, 0.05, Coolant, "painted steel");
        k.Box(-0.03, 1.37, -0.3, 0.03, 1.43, 0.05, Coolant, "painted steel");
        k.Box(-0.03, 0.556, -0.3, 0.03, 1.43, -0.24, Coolant, "painted steel");
        Foot(k);
        return k.P;
    }

    private static Part LifeC()
    {
        const double H = 1.75, hx = 0.3;
        var k = Mod("Life Support", Life, "C", "Tower", "C: a tall narrow battery block: intake louvres up top, a tall sealed service door on a hinge ram, a lit outlet grille at its foot.", H);
        Block(k, 0.078, H, hx, W, ch: 0.1);
        Louvres(k, 0.2, H - 0.2, 8, hx);
        k.Box(-0.24, 1.34, W - 0.008, 0.24, 1.6, W + 0.004, Dark, "dirty steel");
        for (var i = 0; i < 4; i++)
            k.Box(-0.23, 1.36 + i * 0.06, W + 0.004, 0.23, 1.382 + i * 0.06, W + 0.04, Body, "painted steel");
        Hatch(k, 0, 0.22, 0.56, 1.26, Green);
        Ram(k, -0.27, W + 0.06, 0.66, 1.16, -0.31);
        k.Box(-0.24, 0.24, W - 0.008, 0.24, 0.46, W + 0.004, Dark, "dirty steel");
        k.Glow(-0.21, 0.27, W - 0.004, 0.21, 0.43, W + 0.008, Teal);
        for (var i = 0; i < 3; i++)
            k.Box(-0.23, 0.29 + i * 0.05, W + 0.01, 0.23, 0.312 + i * 0.05, W + 0.024, Bare, "steel");
        Foot(k, hx);
        return k.P;
    }

    private static Part LifeD()
    {
        const double H = 0.86;
        var k = Mod("Life Support", Life, "D", "Twin fans", "D: a low battery block with two fans set in its top, teal under the grilles, two small sealed hatches and the strip on the front.", H + 0.1);
        Block(k, 0.078, H, ch: 0.06);
        Louvres(k, 0.2, H - 0.12, 4);
        foreach (var sx in new[] { -1.0, 1.0 })
            Hatch(k, sx * 0.19, 0.13, 0.28, 0.6, Teal, bar: false);
        Strip(k, -0.27, 0.27, 0.68, 4, Teal);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.165;
            k.Hull(Cyl(x, -0.04, 0.165, H - 0.006, H + 0.06, 8), Body, "painted steel");
            k.Hull(Cyl(x, -0.04, 0.13, H + 0.04, H + 0.07, 8), Teal, "", glow: true);
            k.Box(x - 0.15, H + 0.074, -0.055, x + 0.15, H + 0.094, -0.025, Bare, "steel");
            k.Box(x - 0.015, H + 0.074, -0.19, x + 0.015, H + 0.094, 0.11, Bare, "steel");
        }
        Foot(k);
        return k.P;
    }

    private static Part LifeE()
    {
        const double H = 1.25, hx = 0.215;
        var k = Mod("Life Support", Life, "E", "Bottles", "E: a narrow battery block with oxygen bottles strapped down both sides in place of the louvres, a fan under a hood and a gauge on its front.", H);
        Block(k, 0.078, H, hx, W, ch: 0.1);
        FrontFan(k, 0, 0.8, 0.12);
        Hood(k, -hx + 0.02, hx - 0.02, 1.02, Teal);
        Gauge(k, -0.15, -0.03, 0.26, 0.6, 5, 4, Teal);
        Pull(k, 0.09, 0.26, 0.6, Teal);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var cy in new[] { 0.36, 0.64 })
            {
                var x = sx * 0.29;
                k.Hull(CylZ(x, cy, 0.1, -0.3, 0.3), Trim, "painted steel");
                k.Hull(CylZ(x, cy, 0.104, 0.0, 0.04), Green, "", glow: true);
                foreach (var z in new[] { -0.2, 0.16 })
                    k.Hull(CylZ(x, cy, 0.107, z, z + 0.035), Dark, "dirty steel");
                k.Hull(CylZ(x, cy, 0.03, 0.296, 0.35), Bare, "steel");
            }
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var z in new[] { -0.2, 0.16 })
                k.Box(sx * 0.212, 0.24, z + 0.005, sx * 0.226, 0.76, z + 0.03, Dark, "steel"); // (the strap's back plate)
        Foot(k, hx);
        return k.P;
    }

    // ---------------------------------------------------------------- Sensors (cyan)
    private const string Sens = "Sensors";

    private static Part SensA()
    {
        const double H = 0.9;
        var k = Mod("Sensor", Sens, "A", "Block", "A: the battery block, short, two sealed hatches and a cyan slot, a mast on top carrying a small dish and a beacon.", 1.75);
        Block(k, 0.078, H, ch: 0.08);
        Louvres(k, 0.2, 0.74, 4);
        foreach (var sx in new[] { -1.0, 1.0 })
            Hatch(k, sx * 0.21, 0.12, 0.28, 0.64, Cyan, bar: false);
        Pull(k, 0, 0.28, 0.64, Cyan);
        Strip(k, -0.27, 0.27, 0.7, 5, Cyan);
        k.Hull(Cyl(0, -0.1, 0.09, H - 0.006, H + 0.08, 8), Body, "painted steel");
        k.Hull(Cyl(0, -0.1, 0.035, H + 0.076, 1.66, 8), Bare, "steel");
        k.Hull(TipX(CylZ(0, 1.42, 0.22, -0.1, -0.05), 1.42, -0.1, 0.35), Trim, "painted steel");
        k.Hull(TipX(CylZ(0, 1.42, 0.025, -0.054, 0.12), 1.42, -0.1, 0.35), Dark, "plastic");
        k.Hull(TipX(CylZ(0, 1.42, 0.02, 0.116, 0.15), 1.42, -0.1, 0.35), Cyan, "", glow: true);
        k.Hull(Cyl(0, -0.1, 0.03, 1.656, 1.7, 8), Red, "", glow: true);
        Foot(k);
        return k.P;
    }

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

    private static Part SensB()
    {
        const double H = 0.78;
        var k = Mod("Sensor", Sens, "B", "Dome", "B: a low battery block under a radome, a cyan band round the dome's foot, a hood and strip over a sealed hatch.", 1.15);
        Block(k, 0.078, H, ch: 0.08);
        Louvres(k, 0.2, 0.66, 3);
        Hatch(k, -0.12, 0.2, 0.27, 0.56, Cyan);
        Gauge(k, 0.16, 0.3, 0.27, 0.56, 4, 3, Cyan);
        Hood(k, -0.34, 0.34, 0.6, Cyan);
        k.Hull(Cyl(0, 0, 0.3, H - 0.006, H + 0.05, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.305, H + 0.046, H + 0.08, 8), Cyan, "", glow: true);
        k.Hull(Dome(0.29, H + 0.076), Trim, "plastic");
        Foot(k);
        return k.P;
    }

    private static Part SensC()
    {
        const double H = 0.7;
        var k = Mod("Sensor", Sens, "C", "Array", "C: a low battery block with a tilted phased-array panel on top, its tiles edged in cyan, a sealed hatch and strip below.", 1.4);
        Block(k, 0.078, H, ch: 0.06);
        Louvres(k, 0.2, 0.6, 3);
        Hatch(k, -0.13, 0.2, 0.27, 0.52, Cyan, bar: false);
        Strip(k, 0.12, 0.33, 0.42, 3, Cyan);
        Pull(k, 0.22, 0.27, 0.4, Cyan);
        const double py = 0.98, pz = 0.0, tilt = 0.5;
        IEnumerable<double[]> Slab(double x0, double x1, double y0, double y1, double z0, double z1) =>
            TipX(new[] { x0, x1 }.SelectMany(x => new[] { y0, y1 }.SelectMany(y => new[] { z0, z1 }.Select(z => new[] { x, y, z }))), py, pz, tilt);
        k.Hull(Slab(-0.34, 0.34, py - 0.3, py + 0.3, -0.03, 0.03), Body, "painted steel");
        k.Hull(Slab(-0.32, 0.32, py - 0.28, py + 0.28, 0.026, 0.038), Cyan, "", glow: true);
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                k.Hull(Slab(-0.3 + i * 0.2 + 0.006, -0.3 + (i + 1) * 0.2 - 0.006, py - 0.26 + j * 0.173 + 0.006, py - 0.26 + (j + 1) * 0.173 - 0.006, 0.03, 0.05), Dark, "plastic");
        k.Box(-0.06, H - 0.006, -0.2, 0.06, 0.95, -0.08, Body, "painted steel"); // (its strut)
        Foot(k);
        return k.P;
    }

    private static Part SensD()
    {
        const double H = 0.5;
        var k = Mod("Sensor", Sens, "D", "Mast", "D: a squat battery block under a lattice mast, a crossarm with cyan lamps at its ends and a red beacon on top.", 2.0);
        Block(k, 0.078, H, ch: 0.06);
        Louvres(k, 0.18, 0.42, 3);
        Hatch(k, -0.15, 0.17, 0.26, 0.44, Cyan, bar: false);
        Strip(k, 0.06, 0.32, 0.32, 3, Cyan);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.06 - 0.014, H - 0.006, sz * 0.06 - 0.014, sx * 0.06 + 0.014, 1.86, sz * 0.06 + 0.014, Bare, "steel");
        for (var i = 0; i < 5; i++)
        {
            var y = H + 0.18 + i * 0.27;
            k.Box(-0.08, y, -0.08, 0.08, y + 0.03, 0.08, Body, "painted steel");
        }
        k.Box(-0.36, 1.56, -0.02, 0.36, 1.6, 0.02, Body, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.33, 1.6, -0.025, sx * 0.37, 1.65, 0.025, Dark, "plastic");
            k.Glow(sx * 0.335, 1.646, -0.02, sx * 0.365, 1.68, 0.02, Cyan);
            k.Box(sx * 0.2 - 0.006, 1.6, -0.006, sx * 0.2 + 0.006, 1.8, 0.006, Bare, "steel"); // (a whip)
        }
        k.Box(-0.09, 1.856, -0.09, 0.09, 1.88, 0.09, Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.035, 1.876, 1.93, 8), Red, "", glow: true);
        Foot(k);
        return k.P;
    }

    private static Part SensE()
    {
        const double H = 0.6;
        var k = Mod("Sensor", Sens, "E", "Dish", "E: a battery block with a turntable and yoke carrying a tracking dish, an elevation ram at its back, a cyan feed horn.", 1.5);
        Block(k, 0.078, H, ch: 0.06);
        Louvres(k, 0.18, 0.52, 3);
        Hatch(k, -0.13, 0.2, 0.26, 0.5, Cyan, bar: false);
        Strip(k, 0.12, 0.33, 0.4, 3, Cyan);
        k.Hull(Cyl(0, 0, 0.26, H - 0.006, H + 0.06, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.262, H + 0.012, H + 0.03, 8), Cyan, "", glow: true);
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * 0.19, H + 0.056, -0.05, sx * 0.23, 1.06, 0.05, Mid, "painted steel");
        const double cy = 1.02, tilt = 0.5;
        k.Hull(TipX(RingZ(0, cy, 0.33, 0.07).Concat(RingZ(0, cy, 0.13, -0.07)), cy, 0, tilt), Trim, "painted steel");
        k.Hull(TipX(RingZ(0, cy, 0.29, 0.066).Concat(RingZ(0, cy, 0.29, 0.08)), cy, 0, tilt), Dark, "plastic");
        k.Hull(TipX(CylZ(0, cy, 0.02, 0.076, 0.3), cy, 0, tilt), Bare, "steel");
        k.Hull(TipX(CylZ(0, cy, 0.04, 0.296, 0.35), cy, 0, tilt), Cyan, "", glow: true);
        k.Hull(TipX(CylZ(0, cy, 0.06, -0.2, -0.066), cy, 0, tilt), Body, "painted steel");
        Ram(k, 0, -0.26, H + 0.08, 0.86, 0);
        Foot(k);
        return k.P;
    }

    // ---------------------------------------------------------------- Fuel tanks (amber)
    private const string Fuel = "Fuel tanks";

    private static Part FuelA()
    {
        const double H = 1.3;
        var k = Mod("Fuel", Fuel, "A", "Block", "A: the battery block as a tank: an amber sight gauge under a lit hood, a sealed hatch over the pump, a red filler cap on top.", H);
        Block(k, 0.078, H);
        Louvres(k, 0.2, H - 0.2, 6);
        Hatch(k, -0.16, 0.15, 0.27, 0.82, Amber);
        Gauge(k, 0.08, 0.26, 0.27, 0.82, 9, 6, Amber);
        Hood(k, -0.34, 0.34, 0.88, Amber);
        Strip(k, -0.27, 0.27, 0.95, 6, Amber);
        k.Hull(Cyl(0.18, -0.16, 0.07, H - 0.01, H + 0.05, 8), Body, "painted steel");
        k.Hull(Cyl(0.18, -0.16, 0.055, H + 0.046, H + 0.09, 8), RedPaint, "painted steel");
        Foot(k);
        return k.P;
    }

    private static Part FuelB()
    {
        var k = Mod("Fuel", Fuel, "B", "Lying tank", "B: a tank lying across two battery-block saddles, amber band at its middle, a valve box with a lit window and red wheel on the front.", 0.84);
        k.Hull(CylX(0.5, 0, 0.3, -0.34, 0.34), Mid, "painted steel");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Hull(CylX(0.5, 0, 0.22, sx * 0.336, sx * 0.372), Body, "painted steel");
            k.Profile(sx * 0.19, sx * 0.26, [(0.078, -0.32), (0.078, 0.32), (0.3, 0.32), (0.36, 0.26), (0.36, -0.26), (0.3, -0.32)], Body, "painted steel");
            k.Hull(CylX(0.5, 0, 0.31, sx * 0.196, sx * 0.24), Dark, "dirty steel");
        }
        k.Hull(CylX(0.5, 0, 0.306, -0.04, 0.04), Amber, "", glow: true);
        k.Box(-0.11, 0.26, 0.22, 0.11, 0.44, 0.33, Body, "painted steel");
        k.Box(-0.08, 0.3, 0.326, 0.0, 0.4, 0.34, Dark, "plastic");
        k.Glow(-0.07, 0.31, 0.334, -0.01, 0.39, 0.344, Amber);
        k.Hull(CylZ(0.055, 0.35, 0.04, 0.326, 0.37), RedPaint, "painted steel");
        Deck(k);
        Foot(k);
        return k.P;
    }

    private static Part FuelC()
    {
        var k = Mod("Fuel", Fuel, "C", "Caged tank", "C: an upright tank in a square cage of corner posts, an amber level gauge down its front, a hood at the cage's head.", 1.52);
        k.Box(-W, 0.078, -W, W, 0.12, W, Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.3, 0.116, 1.38, 8), Mid, "painted steel");
        k.Hull(Cyl(0, 0, 0.21, 1.376, 1.44, 8), Body, "painted steel");
        k.Hull(Cyl(0, 0, 0.306, 0.3, 0.34, 8), Dark, "dirty steel");
        k.Hull(Cyl(0, 0, 0.306, 1.2, 1.24, 8), Dark, "dirty steel");
        Gauge(k, -0.06, 0.06, 0.4, 1.16, 8, 5, Amber, 0.277);
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                k.Box(sx * 0.32, 0.116, sz * 0.32, sx * (W + 0.004), 1.5, sz * (W + 0.004), Body, "painted steel");
        k.Box(-W, 1.46, -W, W, 1.5, -0.32, Body, "painted steel");
        k.Box(-W, 1.462, -0.32, -0.32, 1.498, 0.32, Body, "painted steel");
        k.Box(0.32, 1.462, -0.32, W, 1.498, 0.32, Body, "painted steel");
        Hood(k, -0.32, 0.32, 1.46, Amber, 0.34);
        k.Hull(Cyl(0.1, -0.1, 0.05, 1.436, 1.5, 8), RedPaint, "painted steel");
        Deck(k);
        Foot(k);
        return k.P;
    }

    private static Part FuelD()
    {
        var k = Mod("Fuel", Fuel, "D", "Bottles", "D: two tall fuel bottles at the back piped into a battery-block pump housing at the front, with a sealed hatch and amber flow gauge.", 1.4);
        k.Profile(-0.34, 0.34, [(0.078, 0.0), (0.078, W), (0.62, W), (0.7, W - 0.08), (0.7, 0.0)], Mid, "painted steel");
        Hatch(k, -0.14, 0.17, 0.27, 0.58, Amber);
        Gauge(k, 0.12, 0.28, 0.27, 0.58, 5, 3, Amber);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * 0.17;
            k.Hull(Cyl(x, -0.18, 0.15, 0.078, 1.24, 8), Trim, "painted steel");
            k.Hull(Cyl(x, -0.18, 0.155, 0.9, 0.96, 8), Amber, "", glow: true);
            k.Hull(Cyl(x, -0.18, 0.1, 1.236, 1.3, 8), Body, "painted steel");
            k.Box(x - 0.025, 1.296, -0.2, x + 0.025, 1.34, 0.1, Bare, "steel");
            k.Box(x - 0.025, 0.696, 0.05, x + 0.025, 1.344, 0.1, Bare, "steel");
        }
        Foot(k);
        return k.P;
    }

    private static Part FuelE()
    {
        var k = Mod("Fuel", Fuel, "E", "Sphere", "E: a pressure sphere in a collar on a battery-block plinth, amber band round it, the plinth's hatch and strip below.", 1.24);
        Block(k, 0.078, 0.42, ch: 0.05);
        Louvres(k, 0.17, 0.38, 2);
        Hatch(k, -0.17, 0.15, 0.25, 0.38, Amber, bar: false);
        Strip(k, 0.03, 0.31, 0.28, 3, Amber);
        k.Hull(Cyl(0, 0, 0.24, 0.416, 0.56, 8), Body, "painted steel");
        Sphere(k, 0.33, 0.84, Amber);
        k.Hull(Cyl(0, 0, 0.05, 1.15, 1.22, 8), Trim, "painted steel");
        k.Hull(Cyl(0, 0, 0.04, 1.216, 1.25, 8), RedPaint, "painted steel");
        Foot(k);
        return k.P;
    }

    // ---------------------------------------------------------------- Magazines (red)
    private const string Mags = "Magazines";

    private static Part MagA()
    {
        const double H = 1.1;
        var k = Mod("Magazine", Mags, "A", "Block", "A: the battery block as a magazine: a big sealed loading hatch with a red seal, a round-count gauge, a hood, a top hatch rimmed in hazard paint.", H);
        Block(k, 0.078, H);
        Louvres(k, 0.2, H - 0.2, 5);
        Hatch(k, -0.09, 0.22, 0.27, 0.78, Red);
        Gauge(k, 0.18, 0.3, 0.27, 0.78, 8, 5, Amber);
        Hood(k, -0.34, 0.34, 0.84, Red);
        Strip(k, -0.27, 0.27, 0.9, 4, Green);
        k.Box(-0.18, H - 0.006, -0.18, 0.18, H + 0.03, 0.08, HazardY, "hazard paint");
        k.Box(-0.15, H + 0.026, -0.15, 0.15, H + 0.05, 0.05, Body, "painted steel");
        Foot(k);
        return k.P;
    }

    private static Part MagB()
    {
        var k = Mod("Magazine", Mags, "B", "Drum", "B: a rotary drum magazine between two chamfered end plates, a red-lit hub on the front, the battery's strip and louvres on the plates.", 0.94);
        foreach (var sz in new[] { -1.0, 1.0 })
            k.Profile(-0.34, 0.34, [(0.078, sz * 0.296), (0.078, sz * 0.336), (0.92, sz * 0.336), (0.92, sz * 0.296)], Mid, "painted steel");
        k.Hull(CylZ(0, 0.52, 0.3, -0.3, 0.3), Body, "painted steel");
        foreach (var z in new[] { -0.2, -0.07, 0.06, 0.19 })
            k.Hull(CylZ(0, 0.52, 0.306, z, z + 0.03), Dark, "dirty steel");
        k.Hull(CylZ(0, 0.52, 0.12, 0.332, 0.346), Red, "", glow: true);
        k.Hull(CylZ(0, 0.52, 0.08, 0.334, 0.37), Bare, "steel");
        Strip(k, -0.3, 0.3, 0.8, 4, Green, 0.336);
        k.Box(-0.33, 0.86, -0.3, 0.33, 0.914, 0.3, Body, "painted steel"); // (the feed housing over the drum)
        Pull(k, -0.27, 0.26, 0.7, Red, 0.336);
        Pull(k, 0.27, 0.26, 0.7, Red, 0.336);
        Deck(k);
        Foot(k);
        return k.P;
    }

    private static Part MagC()
    {
        const double H = 1.2;
        var k = Mod("Magazine", Mags, "C", "Drawers", "C: the battery block with three ammo drawers, each with a lit pull channel across it and a state lamp (one red: empty), under a hood.", H);
        Block(k, 0.078, H);
        Louvres(k, 0.2, H - 0.2, 6);
        for (var i = 0; i < 3; i++)
        {
            var y = 0.27 + i * 0.24;
            k.Face(W - 0.004, W + 0.018, Cut(0, 0.31, y, y + 0.2, 0.04), Mid, "painted steel");
            k.Box(-0.18, y + 0.07, W + 0.014, 0.18, y + 0.13, W + 0.032, Dark, "plastic");
            k.Glow(-0.15, y + 0.09, W + 0.024, 0.15, y + 0.11, W + 0.04, Red);
            k.Glow(0.23, y + 0.08, W + 0.014, 0.27, y + 0.12, W + 0.03, i == 2 ? Red : Green);
            k.Box(-0.29, y + 0.07, W + 0.014, -0.22, y + 0.13, W + 0.026, HazardY, "hazard paint");
        }
        Hood(k, -0.34, 0.34, 1.0, Red);
        Foot(k);
        return k.P;
    }

    private static Part MagD()
    {
        const double H = 0.72;
        var k = Mod("Magazine", Mags, "D", "Belt box", "D: a low battery block with a belt feed chute rising from its back to the gun above, links showing, a sealed hatch and red ready lamp.", 1.4);
        Block(k, 0.078, H, ch: 0.06);
        Louvres(k, 0.18, 0.6, 3);
        Hatch(k, -0.1, 0.22, 0.27, 0.56, Red);
        Gauge(k, 0.2, 0.31, 0.27, 0.56, 4, 3, Amber);
        Strip(k, -0.27, 0.27, 0.6, 3, Green);
        k.Box(-0.09, H - 0.006, -0.22, 0.09, 1.3, -0.08, Mid, "painted steel");
        k.Box(-0.09, 1.22, -0.08, 0.09, 1.3, 0.16, Mid, "painted steel");
        for (var i = 0; i < 6; i++)
            k.Box(-0.065, H + 0.04 + i * 0.08, -0.084, 0.065, H + 0.08 + i * 0.08, -0.068, Amber, "brass");
        k.Glow(-0.03, 1.3 - 0.004, 0.08, 0.03, 1.32, 0.14, Red);
        Foot(k);
        return k.P;
    }

    private static Part MagE()
    {
        const double H = 1.0;
        var k = Mod("Magazine", Mags, "E", "Armoured", "E: a battery block in add-on armour: a heavy hatch on two hinge rams, red warning slit, blow-out panels on top edged in hazard paint.", H + 0.06);
        Block(k, 0.078, H, W - 0.02, W - 0.02, Body);
        foreach (var sx in new[] { -1.0, 1.0 })
            k.Box(sx * (W - 0.024), 0.18, -0.3, sx * (W + 0.012), 0.86, 0.3, Mid, "painted steel");
        Hatch(k, 0, 0.27, 0.22, 0.76, Red, W - 0.02);
        foreach (var sx in new[] { -1.0, 1.0 })
            Ram(k, sx * 0.33, W + 0.03, 0.28, 0.7, sx * 0.33);
        k.Box(-0.26, 0.82, W - 0.026, 0.26, 0.86, W - 0.008, Dark, "plastic");
        k.Glow(-0.24, 0.83, W - 0.016, 0.24, 0.85, W - 0.004, Red);
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.02, H - 0.006, -0.22, sx * 0.24, H + 0.03, 0.2, HazardY, "hazard paint");
            k.Box(sx * 0.04, H + 0.026, -0.2, sx * 0.22, H + 0.05, 0.18, Dark, "dirty steel");
        }
        Foot(k, W - 0.02, W - 0.02);
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
