using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Fittings from the style sheet (Karl, 2026-11-20: "add them to the game, then clear them out of the part style sheet"): the
/// looks he liked on the Part Style Sheet, made real. The Service Locker and the Pressure Locker (the sheet's lockers A and E)
/// keep the crew locker's footprint (0.6 x 0.45 x 1.9 m), sockets and two shelves, front +z, the wall behind -z. The Ship
/// Battery Module (the sheet's battery bank, reactor round 1's D) is a 1x1 block on the standard Module Plug look of
/// docs/sandbox/design/module-sockets.md, a ship battery like the ShipPower ones. Writes Constructs/Parts/FittingLooks.
/// The style sheet's generator (StyleSheetParts) draws its drafts with this file's kit and plug.
/// </summary>
public sealed class FittingLookParts : IPartGenerator
{
    public string Name => "fitting_looks";

    private const string Out = "Constructs/Parts/FittingLooks";
    private const string Group = "Ship", Section = "Ship fittings";

    // A shared palette: graphite body, a mid grey, light trim, near-black recesses. One neon colour a variant, under a tenth of the face.
    internal const string Body = "#3a4047", Mid = "#58616b", Trim = "#a8b0b7", Dark = "#1b1e22", Bare = "#7d848b", Rubber = "#26282b";
    internal const string Cyan = "#3fe6ff", Amber = "#ffae3c", Ice = "#cfeeff", Teal = "#46f0c8", Green = "#64ff9c", Red = "#ff4a4a";
    internal const string GlassTint = "#a9d6e6", HazardY = "#d9a72a";

    internal sealed class Kit(Part p)
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


    internal static PyDict Slot(double[] mn, double[] mx) => new() { ["min"] = mn.Select(v => (object?)v).ToList(), ["max"] = mx.Select(v => (object?)v).ToList() };

    /// <summary>A locker variant: the shell a hand or a round meets (hidden), the sockets, the two shelves. Its looks are the caller's.</summary>
    private static Kit Locker(string name, string desc, string color)
    {
        var p = new Part(name, Small, Section, desc, role: "storage", tool: "wrench", material: "steel", color: color, group: Group);
        p.Set(("finish", "painted steel"), ("collider", "boxes")); // (the hidden shell collides; the looks' hulls don't)
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

    /// <summary>The service locker (the sheet's A): one tall door, a lit hood over it, the pull a lit channel.</summary>
    private static Part ServiceLocker()
    {
        var k = Locker("Service Locker", "A crew locker: one tall door under a lit hood. The pull is a recessed channel with a cyan strip in it; a status lamp and vents.", Body);
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

    /// <summary>The pressure locker (the sheet's E): a cut-corner sealed door, a green seal light, a hinge ram.</summary>
    private static Part PressureLocker()
    {
        var k = Locker("Pressure Locker", "A sealed crew locker with a cut-corner door, a green seal light round its edge and a hinge ram on the side. Chevrons on the base.", Body);
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
        k.Glow(-0.03, 0.81, 0.22, 0.03, 0.84, 0.232, Red); // (the locked lamp)
        // The hinge ram on the -x side: a cylinder with its rod.
        k.Box(-0.318, 1.25, 0.08, -0.298, 1.55, 0.18, Dark, "steel"); // (its bracket)
        k.Hull(Ring(-0.33, 1.28, 0.15, 0.022, 0.6, 1.5), Bare, "steel");
        k.Hull(Ring(-0.33, 0.6, 0.15, 0.011, 0.56, 0.9), Trim, "chrome");
        k.Box(-0.318, 0.55, 0.08, -0.298, 0.65, 0.18, Dark, "steel");
        return k.P;
    }


    internal static IEnumerable<double[]> Ring(double x, double y1, double z, double r, double yA, double yB)
    {
        _ = y1;
        for (var i = 0; i < 8; i++)
        {
            var a = (i + 0.5) * Math.PI / 4;
            yield return [x + r * Math.Cos(a), yA, z + r * Math.Sin(a)];
            yield return [x + r * Math.Cos(a), yB, z + r * Math.Sin(a)];
        }
    }

    internal const string Plasma = "#6fe9ff", Hot = "#ffd27a", Violet = "#b98cff", Coolant = "#3f7fd8";

    internal static double R4(double v) => Py.Round(v, 4);

    /// <summary>
    /// A module's part (docs/sandbox/design/module-sockets.md, approved 2026-11-19): the Module Plug in its standard look, which
    /// every maker keeps (sections 2.1, 2.6, 4.3): an 80 mm plate on the whole-metre footprint, bolts every 0.5 m round the rim,
    /// a lifting lug in each corner inside the footprint. Its couplings sit on the inner face (2.5), so nothing hangs off the
    /// edge; where the module takes a coupling, a gland on the plug's top face marks the port it plugs into, at the table's
    /// place (13.1: Data 1 at 0.15/0.15, Fuel A at 0.85/0.15, Coolant A 0.85/0.5, Chute A the middle, Ammo 0.5/0.85, from the
    /// back-left corner). Power needs no gland: it crosses through the frame. Proud style; a hidden solid is what collides.
    /// </summary>
    internal static Kit Reactor(string look, int n, string desc, double height, string? name = null, string section = "Reactors", string[]? ports = null, string role = "frame", string group = Group)
    {
        name ??= $"{look} {n}x{n}";
        var p = new Part(name, Medium, section, desc, role: role, tool: "welder", material: "steel", color: Body, group: group);
        p.Set(("finish", "painted steel"), ("collider", "boxes"));
        var h = n / 2.0;
        p.Add(Box([-h, 0, -h], [h, R4(height * 0.92), h], Body, hidden: true)); // (what collides: the module's bulk)
        var k = new Kit(p);
        k.Box(-h, 0, -h, h, 0.08, h, Dark, "dirty steel");
        for (var i = 0; i < n * 2; i++)
        {
            var u = -h + 0.25 + i * 0.5;
            foreach (var (x, z) in new[] { (u, -h + 0.06), (u, h - 0.06), (-h + 0.06, u), (h - 0.06, u) })
                k.Box(x - 0.022, 0.07, z - 0.022, x + 0.022, 0.1, z + 0.022, Bare, "steel");
        }
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
            {
                k.Box(sx * (h - 0.1), 0.07, sz * (h - 0.1), sx * (h - 0.035), 0.16, sz * (h - 0.035), HazardY, "hazard paint");
                k.Box(sx * (h - 0.085), 0.156, sz * (h - 0.085), sx * (h - 0.05), 0.2, sz * (h - 0.05), Dark, "steel"); // (the eye)
            }
        foreach (var port in ports ?? ["Data 1", "Fuel A"])
            Gland(k, n, port);
        p.Sock("bottom", [0, 0, 0], "-y", Medium);
        p.Stats(1500 * n * n, 900 * n * n, 12, 6.0);
        return k;
    }

    /// <summary>Where a coupling's port sits on an n x n plug (module-sockets.md 13.1), from the back-left corner.</summary>
    internal static (double X, double Z) PortAt(int n, string port)
    {
        var (u, v) = port switch
        {
            "Data 1" => (0.15, 0.15), "Fuel A" => (0.85, 0.15), "Coolant A" => (0.85, 0.5), "Chute A" => (0.5, 0.5), "Ammo" => (0.5, 0.85),
            _ => throw new ArgumentException($"no port {port}"),
        };
        return (R4(-n / 2.0 + u * n), R4(-n / 2.0 + v * n));
    }

    /// <summary>A port's gland on the plug's top face, in the coupling's colour.</summary>
    internal static void Gland(Kit k, int n, string port)
    {
        var (x, z) = PortAt(n, port);
        var (col, glow) = port switch { "Data 1" => (Plasma, true), "Fuel A" => (Amber, false), "Coolant A" => (Coolant, false), "Ammo" => (HazardY, false), _ => (Mid, false) };
        var r = port is "Ammo" or "Chute A" ? 0.09 : 0.045;
        k.Hull(Cyl(x, z, r, 0.076, 0.11, 8), Dark, "dirty steel");
        k.Hull(Cyl(x, z, r * 0.6, 0.106, 0.124, 8), col, glow ? "" : "painted steel", glow);
    }

    /// <summary>An upright prism's points round (cx, cz): <paramref name="sides"/> corners at radius r, from y0 to y1.</summary>
    internal static IEnumerable<double[]> Cyl(double cx, double cz, double r, double y0, double y1, int sides = 8, double turn = 0.5)
    {
        for (var i = 0; i < sides; i++)
        {
            var a = (i + turn) * 2 * Math.PI / sides;
            yield return [cx + r * Math.Cos(a), y0, cz + r * Math.Sin(a)];
            yield return [cx + r * Math.Cos(a), y1, cz + r * Math.Sin(a)];
        }
    }


    private const double ModuleKj = 8000; // (a cube about the Ship Battery M's volume, a little more: 6 MJ there)

    /// <summary>The Ship Battery Module (the sheet's battery bank: reactor round 1's D, Karl's favourite look, 2026-11-19).</summary>
    private static Part BatteryModule(int n = 1)
    {
        var H = 0.9 + 0.5 * n;
        var k = Reactor("", n, $"A ship battery block on a module plate: {Py.G(ModuleKj / 1000)} MJ for the circuit it is on (the lamps, the logic, the starter, and what the capacitors refill from). Louvred radiator sides, two bolted access panels, a lit charge slot and a status strip. Weld it to a deck by its plate.", H, "Ship Battery Module", Section, ["Data 1"], role: "battery");
        var h = n / 2.0 - 0.124; // (inside the corner lugs)
        k.Profile(-h, h, [(0.078, -h), (0.078, h), (H - 0.12, h), (H, h - 0.12), (H, -h + 0.12), (H - 0.12, -h)], Mid, "painted steel");
        // Louvres down the +-x sides.
        var rows = 4 + 3 * n;
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * h - 0.012, 0.2, -h + 0.12, sx * h + 0.004 * sx, H - 0.2, h - 0.12, Dark, "dirty steel");
            for (var i = 0; i < rows; i++)
            {
                var y = 0.24 + i * (H - 0.5) / Math.Max(1, rows - 1);
                k.Box(sx * h, y, -h + 0.14, sx * (h + 0.04), y + 0.022, h - 0.14, Body, "painted steel");
            }
        }
        // The front (+z): two bolted access panels, the inspection slot between them, a status strip.
        var pw = (h - 0.16) / 2; // (the battery's panels smaller and inside the face: Karl, they overlapped)
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            k.Box(sx * 0.08, 0.2, h - 0.004, sx * (0.08 + pw * 2), H * 0.7, h + 0.016, Body, "painted steel");
            foreach (var by in new[] { 0.24, H * 0.7 - 0.05 })
                foreach (var bx in new[] { 0.11, 0.05 + pw * 2 })
                    k.Box(sx * bx - 0.015, by, h + 0.012, sx * bx + 0.015, by + 0.03, h + 0.026, Bare, "steel");
        }
        k.Box(-0.06, 0.24, h - 0.006, 0.06, H * 0.7 - 0.04, h + 0.012, Dark, "plastic");
        k.Glow(-0.035, 0.28, h + 0.006, 0.035, H * 0.7 - 0.08, h + 0.018, Hot); // (the slot: the core's glow seen through it)
        k.Box(-h + 0.1, H * 0.78, h - 0.004, h - 0.1, H * 0.78 + 0.07, h + 0.014, Dark, "plastic");
        for (var i = 0; i < 6; i++)
            k.Glow(-h + 0.13 + i * 0.06, H * 0.78 + 0.02, h + 0.008, -h + 0.17 + i * 0.06, H * 0.78 + 0.05, h + 0.02, i < 4 ? Green : Amber);
        // Hazard band round the foot.
        k.Box(-h - 0.006, 0.078, -h - 0.006, h + 0.006, 0.15, h + 0.006, HazardY, "hazard paint");
        var px = n / 2.0; // (a port sits on the part's own face: its bounds are the plate's)
        k.P.Set(("battery", Kw(("capacity", ModuleKj * n * n))),
            ("ports", Py.L(
                Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", new object[] { px, R4(H / 2), 0 }), ("face", "+x")),
                Kw(("name", "Power in"), ("type", "power"), ("dir", "in"), ("noEffect", true), ("at", new object[] { -px, R4(H / 2), 0 }), ("face", "-x")))));
        return k.P.Stats(420 * n * n, 900 * n * n, 4, 6.0);
    }


    public void Make(PartOutput o)
    {
        o.Own(Out);
        var all = new List<Part> { ServiceLocker(), PressureLocker(), BatteryModule() };
        foreach (var p in all)
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
