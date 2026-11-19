using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The style grading sheet (Karl, 2026-11-19): for one kind of item, several distinct design interpretations of his brief,
/// built as real parts so the sheet shows them through the game's own build rules (--preview-gallery DIR --group Style).
/// He grades them and picks one; the picked look is then built into the item's real generator, and these drafts go.
/// Group "Style" is unlisted (Constructs/Categories.json), so none of them show in the Parts tab. Writes Constructs/Parts/Styles.
///
/// Lockers, the first item. Brief: "sci fi, but functional. Some light neon lights, but you can tell it's a locker."
/// Each variant keeps the crew locker's footprint (0.6 x 0.45 x 1.9 m), sockets and two storage shelves, so whichever is
/// picked can stand where a locker stands now. The front is +z, the wall behind is -z.
/// </summary>
public sealed class StyleSheetParts : IPartGenerator
{
    public string Name => "style_sheet";

    private const string Out = "Constructs/Parts/Styles";
    private const string Group = "Style", Lockers = "Lockers";

    // A shared palette: graphite body, a mid grey, light trim, near-black recesses. One neon colour a variant, under a tenth of the face.
    private const string Body = "#3a4047", Mid = "#58616b", Trim = "#a8b0b7", Dark = "#1b1e22", Bare = "#7d848b", Rubber = "#26282b";
    private const string Cyan = "#3fe6ff", Amber = "#ffae3c", Ice = "#cfeeff", Teal = "#46f0c8", Green = "#64ff9c", Red = "#ff4a4a";
    private const string GlassTint = "#a9d6e6", HazardY = "#d9a72a";

    private sealed class Kit(Part p)
    {
        public readonly Part P = p;
        /// <summary>Every look box at least 11 mm on each side (the game refuses one under 1 cm): a thin one grows back from its far face.</summary>
        private static (double[], double[]) Thick(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            double[] mn = [Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)], mx = [Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)];
            for (var a = 0; a < 3; a++)
                if (mx[a] - mn[a] < 0.011)
                    mn[a] = Py.Round(mx[a] - 0.011, 4);
            return (mn, mx);
        }
        public void Box(double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish) =>
            P.Add(Look(Thick(x0, y0, z0, x1, y1, z1), col,
                Kw(("finish", finish), ("paintable", false))));
        public void Paint(double x0, double y0, double z0, double x1, double y1, double z1, string col, string finish = "painted steel") =>
            P.Add(Look(Thick(x0, y0, z0, x1, y1, z1), col,
                Kw(("finish", finish))));
        public void Glow(double x0, double y0, double z0, double x1, double y1, double z1, string col) =>
            P.Add(Look(Thick(x0, y0, z0, x1, y1, z1), col,
                Kw(("glow", true), ("paintable", false))));
        public void Glass(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var (mn, mx) = Thick(x0, y0, z0, x1, y1, z1);
            P.Add(PartKitMath.Box(mn, mx, GlassTint, detail: true, glass: true));
        }
        private static PyDict? Look((double[] Mn, double[] Mx) b, string col, PyDict shape) => PartKitMath.Box(b.Mn, b.Mx, col, detail: true, shape: shape);
        /// <summary>A faceted piece: the hull round the points (any convex shape).</summary>
        public void Hull(IEnumerable<double[]> pts, string col, string finish, bool glow = false)
        {
            var all = pts.Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
            double[] mn = [.. Enumerable.Range(0, 3).Select(k => all.Min(q => q[k]))];
            double[] mx = [.. Enumerable.Range(0, 3).Select(k => all.Max(q => q[k]))];
            var shape = Kw(("shape", "hull"), ("points", all.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false));
            if (glow)
                shape["glow"] = true;
            else
                shape["finish"] = finish;
            P.Add(PartKitMath.Box(mn, mx, col, detail: true, shape: shape));
        }
        /// <summary>A side profile (y, z pairs) pushed out across x from x0 to x1.</summary>
        public void Profile(double x0, double x1, (double Y, double Z)[] yz, string col, string finish, bool glow = false) =>
            Hull(yz.Select(q => new[] { x0, q.Y, q.Z }).Concat(yz.Select(q => new[] { x1, q.Y, q.Z })), col, finish, glow);
        /// <summary>A front outline (x, y pairs) pushed out along z from z0 to z1.</summary>
        public void Face(double z0, double z1, (double X, double Y)[] xy, string col, string finish, bool glow = false) =>
            Hull(xy.Select(q => new[] { q.X, q.Y, z0 }).Concat(xy.Select(q => new[] { q.X, q.Y, z1 })), col, finish, glow);
    }

    private static PyDict Slot(double[] mn, double[] mx) => new() { ["min"] = mn.Select(v => (object?)v).ToList(), ["max"] = mx.Select(v => (object?)v).ToList() };

    /// <summary>A locker variant: the shell a hand or a round meets (hidden), the sockets, the two shelves. Its looks are the caller's.</summary>
    private static Kit Locker(string name, string desc, string color)
    {
        var p = new Part(name, Small, Lockers, desc, role: "storage", tool: "wrench", material: "steel", color: color, group: Group);
        p.Set(("finish", "painted steel"));
        foreach (var (mn, mx) in new (double[], double[])[]
        {
            ([-0.3, 0, -0.25], [0.3, 0.085, 0.2]), ([-0.3, 1.75, -0.25], [0.3, 1.9, 0.2]), ([-0.3, 0.085, -0.25], [0.3, 1.75, -0.215]),
            ([-0.3, 0.085, -0.215], [-0.27, 1.75, 0.2]), ([0.27, 0.085, -0.215], [0.3, 1.75, 0.2]), ([-0.27, 0.085, 0.175], [0.27, 1.75, 0.2]),
            ([-0.27, 0.925, -0.215], [0.27, 0.975, 0.175]),
        })
            p.Add(Box(mn, mx, Body, hidden: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 1.9, 0], "+y");
        p.Sock("back", [0, 0.95, -0.25], "-z");
        p.Sock("+x", [0.3, 0.95, 0], "+x");
        p.Sock("-x", [-0.3, 0.95, 0], "-x");
        p.Sock("wall", [0, 0.125, -0.25], "-z");
        p.Set(("storage", new PyDict { ["slots"] = Py.L(Slot([-0.26, 0.09, -0.21], [0.26, 0.92, 0.17]), Slot([-0.26, 0.98, -0.21], [0.26, 1.74, 0.17])) }));
        p.Stats(60, 320, 6, 2.0);
        return new Kit(p);
    }

    /// <summary>A: the service locker. The plainest reading: one tall door, a lit hood over it, the pull a lit channel.</summary>
    private static Part ServiceLocker()
    {
        var k = Locker("Style Locker A Service", "A: one tall door under a lit hood. The pull is a recessed channel with a cyan strip in it; a status lamp and vents.", Body);
        // Carcass: square sides, the top raked back above the hood.
        k.Profile(-0.3, 0.3, [(0.08, -0.25), (0.08, 0.2), (1.8, 0.2), (1.9, 0.12), (1.9, -0.25)], Body, "painted steel");
        k.Box(-0.28, 0, -0.23, 0.28, 0.084, 0.16, Dark, "dirty steel"); // (the kick recess under it)
        k.Glow(-0.24, 0.006, 0.157, 0.24, 0.014, 0.164, Cyan); // (a floor wash from the kick)
        // The door, 12 mm proud, a pressed panel in it.
        k.Paint(-0.268, 0.11, 0.196, 0.268, 1.74, 0.212, Mid);
        k.Box(-0.22, 0.2, 0.208, 0.14, 1.62, 0.216, Body, "painted steel");
        // The pull: a vertical channel on the latch side with the strip down it.
        k.Box(0.17, 0.72, 0.204, 0.235, 1.26, 0.22, Dark, "plastic");
        k.Glow(0.193, 0.76, 0.214, 0.212, 1.22, 0.224, Cyan);
        // Vents low on the door.
        for (var i = 0; i < 4; i++)
            k.Box(-0.18, 0.26 + i * 0.045, 0.214, 0.1, 0.282 + i * 0.045, 0.222, Dark, "dirty steel");
        // Status: a small screen and a green lamp over the pull.
        k.Box(0.158, 1.32, 0.21, 0.246, 1.46, 0.226, Dark, "plastic");
        k.Glow(0.168, 1.37, 0.222, 0.236, 1.45, 0.23, Ice);
        k.Glow(0.19, 1.334, 0.222, 0.214, 1.354, 0.232, Green);
        // The hood: a lip over the door with a cyan downlight under it.
        k.Paint(-0.305, 1.745, 0.19, 0.305, 1.79, 0.265, Trim);
        k.Glow(-0.27, 1.74, 0.205, 0.27, 1.749, 0.255, Cyan);
        // A number plate on the hood.
        k.Box(-0.08, 1.755, 0.262, 0.08, 1.782, 0.27, Dark, "plastic");
        return k.P;
    }

    /// <summary>B: the twin crew locker. Two narrow doors, the familiar gym-locker look; amber light down the seam between them.</summary>
    private static Part TwinLocker()
    {
        var k = Locker("Style Locker B Twin", "B: two narrow doors side by side, louvred at the top, a name tag each. Amber light glows through the seam between them.", Body);
        k.Profile(-0.3, 0.3, [(0.08, -0.25), (0.08, 0.2), (1.86, 0.2), (1.9, 0.16), (1.9, -0.25)], Body, "painted steel");
        k.Box(-0.29, 0, -0.24, 0.29, 0.084, 0.185, Rubber, "rubber"); // (the plinth)
        k.Glow(-0.006, 0.13, 0.197, 0.006, 1.73, 0.204, Amber); // (the seam light, seen between the doors)
        foreach (var s in new[] { -1.0, 1.0 })
        {
            double In(double v) => s * v; // (from the seam outward)
            k.Paint(In(0.009), 0.115, 0.199, In(0.27), 1.745, 0.213, Mid);
            // Louvres in the top of each door.
            for (var i = 0; i < 5; i++)
                k.Box(In(0.05), 1.5 + i * 0.04, 0.21, In(0.23), 1.518 + i * 0.04, 0.222, Dark, "dirty steel");
            // A lit name tag under them.
            k.Box(In(0.06), 1.4, 0.21, In(0.22), 1.45, 0.22, Dark, "plastic");
            k.Glow(In(0.07), 1.41, 0.216, In(0.21), 1.44, 0.225, Amber);
            // A lever pull by the seam and its lock.
            k.Box(In(0.025), 0.86, 0.21, In(0.06), 1.16, 0.232, Trim, "steel");
            k.Box(In(0.022), 0.83, 0.21, In(0.063), 0.85, 0.225, Dark, "plastic");
            // A pressed rib low on each door.
            k.Box(In(0.06), 0.25, 0.21, In(0.22), 0.66, 0.217, Body, "painted steel");
        }
        // A cornice strip across the top, with a thin amber line.
        k.Paint(-0.305, 1.76, 0.195, 0.305, 1.8, 0.232, Trim);
        k.Glow(-0.27, 1.775, 0.229, 0.27, 1.785, 0.236, Amber);
        return k.P;
    }

    /// <summary>C: the armoured gear cabinet. Industrial: a heavy bolted frame round a deep-set door, cross straps, a light bar in the head.</summary>
    private static Part ArmouredLocker()
    {
        var k = Locker("Style Locker C Armoured", "C: a heavy welded frame round a deep-set door, two latched straps across it, a keypad. One white light bar in the frame's head.", Mid);
        k.Box(-0.297, 0.083, -0.25, 0.297, 1.9, 0.17, Body, "painted steel"); // (the carcass, the door sits back in its frame)
        k.Box(-0.29, 0, -0.24, 0.29, 0.084, 0.16, Dark, "dirty steel");
        // The frame: chamfered stiles, a deep head and sill.
        foreach (var s in new[] { -1.0, 1.0 })
            k.Face(0.168, 0.235, [(s * 0.302, 0.087), (s * 0.245, 0.087), (s * 0.245, 1.895), (s * 0.302, 1.895)], Mid, "painted steel");
        k.Profile(-0.305, 0.305, [(1.7, 0.165), (1.7, 0.235), (1.86, 0.25), (1.905, 0.2), (1.905, 0.165)], Mid, "painted steel");
        k.Profile(-0.305, 0.305, [(0.077, 0.165), (0.077, 0.25), (0.15, 0.235), (0.15, 0.165)], Mid, "painted steel");
        // The light bar in the head, under its lip.
        k.Glow(-0.22, 1.71, 0.232, 0.22, 1.735, 0.246, Ice);
        // Bolt heads down the stiles.
        foreach (var s in new[] { -1.0, 1.0 })
            for (var i = 0; i < 7; i++)
                k.Box(s * 0.264, 0.24 + i * 0.22, 0.232, s * 0.284, 0.26 + i * 0.22, 0.242, Bare, "steel");
        // The door, deep-set, and two straps across it with their latches.
        k.Paint(-0.243, 0.152, 0.168, 0.243, 1.698, 0.19, Body);
        foreach (var y in new[] { 0.5, 1.3 })
        {
            k.Box(-0.236, y, 0.186, 0.236, y + 0.07, 0.205, Trim, "painted steel");
            k.Box(0.13, y - 0.012, 0.2, 0.2, y + 0.082, 0.222, Dark, "steel"); // (the latch)
            k.Box(-0.2, y + 0.022, 0.2, -0.14, y + 0.048, 0.214, HazardY, "hazard paint"); // (its tell-tale)
        }
        // The keypad and its lamp, between the straps.
        k.Box(0.09, 0.95, 0.186, 0.21, 1.15, 0.204, Dark, "plastic");
        for (var r = 0; r < 3; r++)
            for (var c = 0; c < 3; c++)
                k.Box(0.105 + c * 0.034, 0.97 + r * 0.04, 0.2, 0.127 + c * 0.034, 0.996 + r * 0.04, 0.21, Bare, "plastic");
        k.Glow(0.12, 1.11, 0.2, 0.18, 1.13, 0.21, Green);
        // A stencilled band on the door.
        k.Box(-0.2, 0.86, 0.186, 0.02, 0.9, 0.195, Trim, "worn paint");
        return k.P;
    }

    /// <summary>D: the display locker. You see what's inside: a lit window over a drawer.</summary>
    private static Part DisplayLocker()
    {
        var k = Locker("Style Locker D Display", "D: a glazed upper door showing what's stowed, its inside lit teal, over a deep drawer. Square and clean, light only behind the glass.", Trim);
        // Carcass as panels, hollow, so the window shows the inside: back, sides, top, the shelf deck between drawer and cabinet.
        k.Paint(-0.3, 0.08, -0.25, 0.3, 1.9, -0.212, Trim, "gloss paint");
        foreach (var s in new[] { -1.0, 1.0 })
            k.Paint(s * 0.3, 0.08, -0.212, s * 0.262, 1.9, 0.198, Trim, "gloss paint");
        k.Paint(-0.262, 1.86, -0.212, 0.262, 1.9, 0.198, Trim, "gloss paint");
        k.Paint(-0.262, 0.08, -0.212, 0.262, 0.957, 0.19, Trim, "gloss paint"); // (the drawer's body)
        k.Box(-0.29, 0, -0.24, 0.29, 0.084, 0.185, Dark, "dirty steel");
        // Upper door: a frame of four bars round the window.
        k.Paint(-0.272, 0.962, 0.197, 0.272, 1.02, 0.214, Body);
        k.Paint(-0.272, 1.8, 0.197, 0.272, 1.856, 0.214, Body);
        foreach (var s in new[] { -1.0, 1.0 })
            k.Paint(s * 0.272, 1.025, 0.198, s * 0.22, 1.795, 0.213, Body);
        k.Glass(-0.218, 1.018, 0.203, 0.218, 1.802, 0.208);
        // Inside, behind the glass: a lit back wall, a shelf, a helmet and a folded jacket.
        k.Glow(-0.255, 0.97, -0.21, 0.255, 1.85, -0.2, Teal); // (the lit back wall)
        k.Box(-0.255, 1.36, -0.2, 0.255, 1.38, 0.15, Bare, "steel"); // (the shelf)
        k.Box(-0.12, 1.385, -0.12, 0.12, 1.6, 0.08, "#d9dcd6", "gloss paint"); // (a helmet)
        k.Box(-0.09, 1.44, 0.075, 0.09, 1.54, 0.09, "#2f3a44", "chrome"); // (its visor)
        k.Box(-0.18, 0.965, -0.16, 0.18, 1.2, 0.1, "#556b5a", "fabric"); // (a jacket on the floor of it)
        k.Glow(-0.2, 1.835, 0.15, 0.2, 1.845, 0.19, Teal); // (the strip over the glass, inside)
        // The window's pull, on the frame.
        k.Box(0.226, 1.2, 0.21, 0.258, 1.6, 0.226, Dark, "plastic");
        // Lower: a drawer with a full-width handle slot and a fill gauge.
        k.Paint(-0.272, 0.1, 0.193, 0.272, 0.945, 0.214, Mid);
        k.Box(-0.2, 0.83, 0.205, 0.2, 0.88, 0.222, Dark, "plastic");
        k.Glow(-0.16, 0.845, 0.214, 0.16, 0.852, 0.226, Teal);
        for (var i = 0; i < 5; i++) // (a gauge: three of five lit)
            if (i < 3)
                k.Glow(-0.24 + i * 0.03, 0.16, 0.21, -0.22 + i * 0.03, 0.2, 0.22, Teal);
            else
                k.Box(-0.24 + i * 0.03, 0.16, 0.21, -0.22 + i * 0.03, 0.2, 0.22, Dark, "plastic");
        return k.P;
    }

    /// <summary>E: the pressure locker. The most sci-fi: a cut-corner door, a green neon seal round its edge, a hinge ram on the side.</summary>
    private static Part PressureLocker()
    {
        var k = Locker("Style Locker E Pressure", "E: a sealed locker with a cut-corner door, a green seal light round its edge and a hinge ram on the side. Chevrons on the base.", Body);
        // Carcass with chamfered front corners top and bottom.
        k.Profile(-0.3, 0.3, [(0.08, -0.25), (0.08, 0.14), (0.14, 0.2), (1.8, 0.2), (1.9, 0.1), (1.9, -0.25)], Body, "painted steel");
        k.Box(-0.29, 0, -0.24, 0.29, 0.084, 0.12, Dark, "dirty steel");
        for (var i = 0; i < 4; i++) // (chevrons on the base)
            k.Face(0.118, 0.13, [(-0.2 + i * 0.11, 0.015), (-0.17 + i * 0.11, 0.015), (-0.13 + i * 0.11, 0.06), (-0.16 + i * 0.11, 0.06)], HazardY, "hazard paint");
        // The seal light: a cut-corner outline just bigger than the door, behind it.
        (double X, double Y)[] Cut(double w, double y0, double y1, double c) => [(-w + c, y0), (w - c, y0), (w, y0 + c), (w, y1 - c), (w - c, y1), (-w + c, y1), (-w, y1 - c), (-w, y0 + c)];
        k.Face(0.196, 0.204, Cut(0.262, 0.16, 1.74, 0.09), Green, "", glow: true);
        k.Face(0.198, 0.218, Cut(0.25, 0.172, 1.728, 0.085), Mid, "painted steel"); // (the door)
        k.Face(0.214, 0.224, Cut(0.19, 0.3, 1.6, 0.06), Body, "painted steel"); // (its raised panel)
        // A release bar across the door and its lamp.
        k.Box(-0.15, 0.92, 0.22, 0.15, 0.98, 0.244, Trim, "steel");
        k.Box(-0.17, 0.9, 0.218, -0.14, 1.0, 0.236, Dark, "plastic");
        k.Box(0.14, 0.9, 0.218, 0.17, 1.0, 0.236, Dark, "plastic");
        k.Glow(-0.03, 1.06, 0.22, 0.03, 1.09, 0.232, Green);
        k.Glow(-0.03, 0.81, 0.22, 0.03, 0.84, 0.232, Red); // (the locked lamp, dark when open: shown lit here for the sheet)
        // The hinge ram on the -x side: a cylinder with its rod.
        k.Box(-0.318, 1.25, 0.08, -0.298, 1.55, 0.18, Dark, "steel"); // (its bracket)
        k.Hull(Ring(-0.33, 1.28, 0.15, 0.022, 0.6, 1.5), Bare, "steel");
        k.Hull(Ring(-0.33, 0.6, 0.15, 0.011, 0.56, 0.9), Trim, "chrome");
        k.Box(-0.318, 0.55, 0.08, -0.298, 0.65, 0.18, Dark, "steel");
        return k.P;
    }

    /// <summary>A vertical cylinder's points (eight sides), from y0 to y1.</summary>
    private static IEnumerable<double[]> Ring(double x, double y1, double z, double r, double yA, double yB)
    {
        _ = y1;
        for (var i = 0; i < 8; i++)
        {
            var a = (i + 0.5) * Math.PI / 4;
            yield return [x + r * Math.Cos(a), yA, z + r * Math.Sin(a)];
            yield return [x + r * Math.Cos(a), yB, z + r * Math.Sin(a)];
        }
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var p in new[] { ServiceLocker(), TwinLocker(), ArmouredLocker(), DisplayLocker(), PressureLocker() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
