namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The shape and look of each machine tier: <see cref="MachineTiers"/> asks this what a line is at a tier. Was
/// tools/constructs/tier_style.py; docs/sandbox/constructs/production.md, "Machine tiers".
///
/// The numbers (rates, bins, power, mass, health, sockets, ports, widths, the bit's reach) are MachineTiers' and don't change here.
/// This holds the bodies, in the same units as the line's sockets (a module's size, a building's width): each tier of each line has
/// its own solid shape, its own proportions and its own drawn details, and MachineTiers grows them by the tier's size.
///
/// - Palette: a tier's colours by role.
/// - Look(name, tier): "solid" (the boxes that collide), "detail" (drawn only) and "hopper" (where the input bin sits, y from and to,
///   when the body moves it). A solid made by S(..., shape) draws as that shape, its box staying, hidden, as the collider. H() is a
///   hidden solid box with nothing of its own drawn (a step under a big shape).
/// - The machines' files say "collider": "boxes", so a wedge or a cylinder here never becomes a hull.
///
/// The five looks: Scrap (lashed-up: rust, mismatched plates, timber props, squat and crooked), Workshop (tidy blue paint on cast iron,
/// cream trim, rivets, one proper stack), Industrial (concrete grey and safety yellow, big pipes, hazard stripes, ladders), Military
/// (olive armour on slopes, pale stencil bands, sandbags, no bare pipework), Plasma (dark rounded shells, glowing rings and a violet
/// core). A line still reads as itself at every tier: the drill's bit and tower, the saw's bin and blades, the mill's two beds and blade
/// frame, the furnace's hopper, stack and mouth, the foundry's twin stacks and the assembler's open gantry and arm. Details stay clear of
/// sockets, ports, bins, aisles and the assembler's open front.
/// </summary>
public static class TierStyle
{
    /// <summary>A box of a look: corners in the line's units, a colour role, the shape keys drawn as given, and for a solid drawn as a
    /// shape the look that draws it (its own box stays, hidden, as the collider).</summary>
    public sealed class TB
    {
        public required double[] Min;
        public required double[] Max;
        public required string Role;
        public PyDict Keys = new();
        public PyDict? Look;
        public bool Hidden;

        public TB With(double[] min, double[] max) => new() { Min = min, Max = max, Role = Role, Keys = Keys, Look = Look, Hidden = Hidden };
    }

    public sealed class Shape
    {
        public required List<TB> Solid;
        public required List<TB> Detail;
        public (double Y0, double Y1)? Hopper;
    }

    public static readonly string[] ShapeKeys = ["shape", "front", "segments", "inner", "ends", "taper", "shift", "radius", "edges", "angle", "dir"];

    private static Dictionary<string, string> Pal(params string[] kv)
    {
        string[] roles = ["body", "trim", "frame", "accent", "edge", "dark", "core", "heat", "motor", "light", "plate", "plate2", "timber", "rivet", "pipe", "stripe", "sand", "coil"];
        var d = new Dictionary<string, string>();
        for (var i = 0; i < roles.Length; i++)
            d[roles[i]] = kv[i];
        return d;
    }

    public static readonly Dictionary<int, Dictionary<string, string>> Palette = new()
    {
        [1] = Pal("#6b4a35", "#83563a", "#5a4f46", "#94702f", "#a59c8e", "#2f2a26", "#7a4432", "#ff7a1f", "#4e5a42", "#c89a3a",
            "#76835f", "#8b857a", "#9a7442", "#b3a48c", "#6e6257", "#a8542a", "#7c6f5c", "#514438"),
        [2] = Pal("#2f5f8a", "#d8c690", "#3a3f46", "#d8c690", "#c9ced6", "#2a2e33", "#3f6f9c", "#ff8a2a", "#26496b", "#d8c690",
            "#3a6d9b", "#274f74", "#3a3f46", "#d8c690", "#3a3f46", "#d8c690", "#2a2e33", "#5a6068"),
        [3] = Pal("#8f918d", "#74777a", "#5e6266", "#f0c21a", "#dfe4ea", "#26282b", "#a3a29a", "#ffa030", "#c9a21c", "#ffae20",
            "#a7a89f", "#74777a", "#5e6266", "#f0c21a", "#b8bcc0", "#16171a", "#74777a", "#c0301e"),
        [4] = Pal("#4b5436", "#59603f", "#3e4530", "#d9d5c0", "#a8aeb4", "#1e211c", "#3f4731", "#ff4a20", "#3a4230", "#ff3020",
            "#5a6442", "#6a6f60", "#3a4230", "#2a2e24", "#2a2e24", "#d9d5c0", "#9a8c62", "#2a2e24"),
        [5] = Pal("#1a1f2e", "#2c3448", "#242b3e", "#35e0ff", "#6ff0ff", "#0e1119", "#20263a", "#b050ff", "#2c3448", "#35e0ff",
            "#2c3448", "#39445c", "#10131c", "#4a5672", "#10131c", "#35e0ff", "#10131c", "#b050ff"),
    };

    /// <summary>The roles that glow at a tier.</summary>
    public static readonly Dictionary<int, string[]> Glows = new()
    {
        [3] = ["light"],
        [4] = ["light", "heat"],
        [5] = ["light", "heat", "edge", "accent", "stripe", "coil"],
    };

    private static readonly double[] Uneven = [1.0, 1.14, 0.84, 1.08, 0.92, 1.2, 0.88]; // Scrap's solid boxes: no two plates from the same batch

    /// <summary>A tier 1 solid box's colour: the role's, a little lighter or darker by the box.</summary>
    public static string ScrapTone(string colour, int i)
    {
        var k = Uneven[i % Uneven.Length];
        var s = "#";
        foreach (var n in new[] { 1, 3, 5 })
            s += Math.Min(255, (int)(Convert.ToInt32(colour.Substring(n, 2), 16) * k)).ToString("x2");
        return s;
    }

    private static PyDict Kv((string, object?)[] kv) => new PyDict().Update(kv);

    private static TB D(double[] mn, double[] mx, string role = "plate", params (string, object?)[] shape) =>
        new() { Min = [.. mn], Max = [.. mx], Role = role, Keys = Kv(shape) };

    private static TB Cyl(double[] mn, double[] mx, string role = "pipe", string front = "+y", params (string, object?)[] s) =>
        D(mn, mx, role, [("shape", "cylinder"), ("front", front), .. s]);

    private static TB Ring(double[] mn, double[] mx, string role = "coil", string front = "+y", double inner = 0.72, params (string, object?)[] s) =>
        D(mn, mx, role, [("shape", "tube"), ("front", front), ("inner", inner), .. s]);

    private static TB Wedge(double[] mn, double[] mx, string role, string front, List<object?> taper, params (string, object?)[] s) =>
        D(mn, mx, role, [("shape", "wedge"), ("front", front), ("taper", taper), .. s]);

    private static TB Ball(double[] mn, double[] mx, string role = "rivet") => D(mn, mx, role, ("shape", "sphere"));

    private static TB RoundBox(double[] mn, double[] mx, string role = "plate", string front = "-z", double radius = 0.3) =>
        D(mn, mx, role, ("shape", "rounded"), ("front", front), ("radius", radius), ("edges", "long"));

    private static TB Sandbag(double[] mn, double[] mx) => D(mn, mx, "sand", ("shape", "capsule"), ("front", "+x"));

    private static List<object?> L(params object?[] v) => [.. v];

    /// <summary>Round heads on a face.</summary>
    private static List<TB> Rivets(double[][] points, double size) =>
        [.. points.Select(p => Ball([.. p.Select(c => c - size)], [.. p.Select(c => c + size)]))];

    /// <summary>A yellow band with n black blocks across it, along its longest side. On a face: the thin side is its depth.</summary>
    private static List<TB> Hazard(double[] mn, double[] mx, int n)
    {
        double[] size = [mx[0] - mn[0], mx[1] - mn[1], mx[2] - mn[2]];
        var thin = Array.IndexOf(size, size.Min());
        var along = Array.IndexOf(size, size.Max());
        var o = new List<TB> { D(mn, mx, "accent") };
        var step = size[along] / (2 * n + 1);
        for (var k = 0; k < n; k++)
        {
            double[] lo = [.. mn], hi = [.. mx];
            lo[along] = mn[along] + step * (2 * k + 1);
            hi[along] = lo[along] + step;
            lo[thin] -= 0.002;
            hi[thin] += 0.002;
            var other = 3 - thin - along;
            lo[other] += 0.004;
            hi[other] -= 0.004;
            o.Add(D(lo, hi, "stripe"));
        }
        return o;
    }

    /// <summary>Two rails and rungs standing proud of an x face (x is the wall; depth signed, outwards).</summary>
    private static List<TB> SideLadder(double z0, double z1, double y0, double y1, double x, int rungs, double depth = 0.02)
    {
        double x0 = Math.Min(x, x + depth), x1 = Math.Max(x, x + depth);
        var w = Math.Abs(depth) * 0.6;
        var o = new List<TB> { D([x0, y0, z0], [x1, y1, z0 + w], "accent"), D([x0, y0, z1 - w], [x1, y1, z1], "accent") };
        for (var k = 0; k < rungs; k++)
        {
            var y = y0 + (y1 - y0) * (k + 0.5) / rungs;
            var xm = x0 + (x1 - x0) * 0.25;
            o.Add(D([xm, y, z0 + w], [xm + Math.Abs(depth) * 0.5, y + w, z1 - w], "accent"));
        }
        return o;
    }

    /// <summary>A solid box; with a shape, drawn as that shape (the box stays, hidden, as the collider).</summary>
    private static TB S(double[] mn, double[] mx, string role = "body", string? shape = null, params (string, object?)[] s)
    {
        var b = new TB { Min = [.. mn], Max = [.. mx], Role = role };
        if (shape is not null)
            b.Look = Kv([("shape", shape), .. s]);
        return b;
    }

    /// <summary>A solid box that isn't drawn: a step of the collider under a bigger shape.</summary>
    private static TB H(double[] mn, double[] mx) => new() { Min = [.. mn], Max = [.. mx], Role = "body", Hidden = true };

    /// <summary>Four walls round an open room.</summary>
    private static List<TB> Walls(double x0, double x1, double y0, double y1, double z0, double z1, double t, string role = "frame") =>
    [
        S([x0, y0, z0], [x1, y1, z0 + t], role), S([x0, y0, z1 - t], [x1, y1, z1], role),
        S([x0, y0, z0 + t], [x0 + t, y1, z1 - t], role), S([x1 - t, y0, z0 + t], [x1, y1, z1 - t], role),
    ];

    private static TB Floor() => S([-0.5, 0, -0.5], [0.5, 0.02, 0.5]);

    /// <summary>The product tray across the aisle (a Smelter's or a Foundry's): the same at every tier, the aisle's far side.</summary>
    private static List<TB> Tray(double hw) =>
        [S([-hw, 0.02, 0.28], [hw, 0.024, 0.5]), .. Walls(-hw, hw, 0.02, 0.24, 0.26, 0.5, 0.04, "body").Take(2),
            S([-hw, 0.02, 0.3], [-hw + 0.02, 0.24, 0.46]), S([hw - 0.02, 0.02, 0.3], [hw, 0.24, 0.46])];

    /// <summary>The solid service blocks the side and back sockets and the chute port stand on.</summary>
    private static List<TB> Plinths(double z, string role = "frame", double top = 0.46) =>
    [
        S([0.44, 0.02, z - 0.1], [0.5, top, z + 0.1], role), S([-0.5, 0.02, z - 0.1], [-0.44, top, z + 0.1], role),
        S([-0.1, 0.02, -0.5], [0.1, top, -0.44], role),
    ];

    // ---- The drill: a bit under a tower, a hopper on a block

    private static TB Bit() => S([-0.07, -1.2, 0.61], [0.07, -0.3, 0.75], "edge");

    private static List<TB> ModuleBase(string role = "body", double top = 0.46) => [S([-0.25, 0, -0.25], [0.25, top, 0.25], role)];

    private static List<TB> HopperRing() => Walls(-0.19, 0.19, 0.46, 0.62, -0.19, 0.19, 0.03);

    private static Shape Drill(int t)
    {
        if (t == 1) // a timber derrick: four legs round the bit, lashed to a block, a drum for a motor
        {
            var legs = new List<TB>();
            foreach (var x in new[] { -0.17, 0.1 })
                foreach (var z in new[] { 0.5, 0.79 })
                    legs.Add(S([x, -0.3, z], [x + 0.07, 0.92, z + 0.07], "timber"));
            return new Shape
            {
                Solid = [.. ModuleBase(), .. HopperRing(), S([-0.1, 0.2, 0.25], [0.1, 0.34, 0.5], "timber"), .. legs,
                    S([-0.17, 0.86, 0.5], [0.17, 0.92, 0.86], "timber"), S([-0.13, 0.92, 0.55], [0.13, 1.0, 0.81], "motor"), Bit()],
                Detail = [
                    D([-0.17, -0.28, 0.555], [0.17, -0.22, 0.61], "timber"), D([-0.1, 0.1, 0.57], [-0.04, 0.16, 0.79], "timber"),
                    D([-0.2, 0.3, -0.262], [-0.08, 0.44, -0.25], "plate"), D([0.05, 0.05, 0.25], [0.22, 0.2, 0.262], "plate2"), D([-0.262, 0.04, 0.08], [-0.25, 0.14, 0.22], "plate"),
                    Cyl([0.16, 0.5, 0.56], [0.34, 0.82, 0.76], "motor"), D([0.27, 0.82, 0.62], [0.31, 0.98, 0.66], "pipe"),
                    Cyl([-0.1, -0.62, 0.58], [0.1, -0.5, 0.78], "pipe"), Cyl([-0.085, -1.02, 0.595], [0.085, -0.94, 0.765], "dark"),
                ],
            };
        }
        if (t == 2) // a pillar drill: one round column, a head, a table round the bit, a flywheel
            return new Shape
            {
                Solid = [.. ModuleBase(), .. HopperRing(),
                    S([-0.12, 0.24, 0.25], [0.12, 0.46, 0.56], "frame"),
                    S([-0.09, -0.3, 0.59], [0.09, 0.7, 0.77], "frame", "cylinder", ("front", "+y"), ("segments", 20)),
                    S([-0.16, 0.7, 0.5], [0.16, 0.92, 0.86], "body"), S([-0.13, 0.92, 0.55], [0.13, 1.0, 0.81], "motor"),
                    S([-0.17, -0.3, 0.5], [0.17, -0.22, 0.86], "frame"), Bit()],
                Detail = [
                    D([-0.168, 0.84, 0.492], [0.168, 0.88, 0.868], "trim"), D([-0.258, 0.4, -0.258], [0.258, 0.44, 0.258], "trim"), D([-0.258, 0.02, -0.258], [0.258, 0.09, 0.258], "frame"),
                    Cyl([0.17, 0.66, 0.56], [0.21, 0.94, 0.8], "dark", "+x", ("segments", 20)), Ring([0.205, 0.7, 0.6], [0.225, 0.9, 0.76], "trim", "+x", 0.72, ("ends", "closed")),
                    D([-0.235, 0.78, 0.2], [0.235, 0.8, 0.235], "trim"), D([-0.235, 0.62, 0.2], [-0.2, 0.78, 0.235], "frame"), D([0.2, 0.62, 0.2], [0.235, 0.78, 0.235], "frame"),
                    .. Rivets([[-0.1, 0.78, 0.864], [0.1, 0.78, 0.864]], 0.012),
                ],
            };
        if (t == 3) // a stepped lattice tower with landings, pipes and a ladder
            return new Shape
            {
                Solid = [.. ModuleBase(), .. HopperRing(),
                    S([-0.12, 0.24, 0.25], [0.12, 0.46, 0.5], "frame"),
                    S([-0.19, -0.3, 0.5], [0.19, 0.3, 0.89]), S([-0.15, 0.3, 0.52], [0.15, 0.62, 0.84], "body"), S([-0.11, 0.62, 0.56], [0.11, 0.92, 0.8], "trim"),
                    S([-0.2, 0.3, 0.49], [0.2, 0.34, 0.9], "accent"), S([-0.16, 0.62, 0.51], [0.16, 0.66, 0.85], "accent"),
                    S([-0.13, 0.92, 0.55], [0.13, 1.0, 0.81], "motor"), Bit()],
                Detail = [
                    Cyl([-0.1, -0.85, 0.58], [0.1, -0.78, 0.78], "frame"), Cyl([-0.1, -0.65, 0.58], [0.1, -0.58, 0.78], "frame"), Cyl([-0.1, -0.45, 0.58], [0.1, -0.38, 0.78], "frame"),
                    .. Hazard([-0.17, -0.25, 0.892], [0.17, -0.1, 0.902], 3),
                    Cyl([0.15, 0.34, 0.62], [0.2, 0.62, 0.67], "pipe"), Ring([0.145, 0.46, 0.61], [0.205, 0.5, 0.68], "coil", "+y", 0.5, ("ends", "closed")),
                    .. SideLadder(0.6, 0.76, 0.34, 0.9, -0.15, 3, -0.02), D([0.11, 0.86, 0.66], [0.17, 0.9, 0.72], "light"),
                ],
            };
        if (t == 4) // a casemate: one low armoured hull over block and bit, a thin mast for the shaft
        {
            var struts = new List<TB>();
            foreach (var x in new[] { -0.1, 0.085 })
                foreach (var z in new[] { 0.53, 0.815 })
                    struts.Add(D([x, -1.0, z], [x + 0.015, -0.34, z + 0.015], "dark"));
            return new Shape
            {
                Solid = [.. ModuleBase("trim"), .. HopperRing(),
                    S([-0.21, -0.3, 0.25], [0.21, 0.5, 0.9], "body", "bevel", ("radius", 0.25), ("edges", "long"), ("front", "+z")),
                    S([-0.07, 0.5, 0.61], [0.07, 0.92, 0.75], "frame"), S([-0.13, 0.92, 0.55], [0.13, 1.0, 0.81], "motor"), Bit()],
                Detail = [
                    Wedge([-0.2, 0.5, 0.5], [0.2, 0.68, 0.61], "plate", "+y", L(0.6, 0.2), ("shift", L(0, 0.3))),
                    Wedge([-0.2, 0.5, 0.75], [0.2, 0.68, 0.89], "plate", "+y", L(0.6, 0.2), ("shift", L(0, -0.3))),
                    D([-0.213, 0.2, 0.3], [0.213, 0.27, 0.885], "accent"), D([-0.255, 0.3, -0.256], [0.255, 0.36, 0.256], "accent"),
                    D([-0.1, -1.02, 0.52], [0.1, -1.0, 0.84], "dark"),
                    .. struts,
                    D([-0.11, -0.72, 0.52], [0.11, -0.7, 0.84], "dark"),
                    Sandbag([-0.24, 0.46, 0.195], [0.24, 0.52, 0.245]), D([0.1, 0.42, 0.895], [0.16, 0.46, 0.91], "light"),
                ],
            };
        }
        return new Shape // a slim pod on a thin neck, rings up the bit
        {
            Solid = [S([-0.25, 0, -0.25], [0.25, 0.46, 0.25], "body", "rounded", ("radius", 0.28), ("edges", "long"), ("front", "+y")), .. HopperRing(),
                S([-0.05, 0.3, 0.25], [0.05, 0.4, 0.56], "frame", "cylinder", ("front", "+z")),
                S([-0.12, -0.3, 0.56], [0.12, 0.92, 0.8], "body", "rounded", ("radius", 0.48), ("edges", "long"), ("front", "+y")),
                S([-0.13, 0.92, 0.55], [0.13, 1.0, 0.81], "plate2", "rounded", ("radius", 0.4), ("edges", "long"), ("front", "+y")), Bit()],
            Detail = [
                Ring([-0.12, -0.42, 0.56], [0.12, -0.36, 0.8], "coil", "+y", 0.7, ("segments", 24)), Ring([-0.11, -0.66, 0.57], [0.11, -0.6, 0.79], "stripe", "+y", 0.72, ("segments", 24)), Ring([-0.1, -0.9, 0.58], [0.1, -0.84, 0.78], "coil", "+y", 0.72, ("segments", 24)),
                Ball([-0.07, 0.24, 0.76], [0.07, 0.42, 0.83], "coil"),
                Ring([-0.16, 0.62, 0.52], [0.16, 0.66, 0.84], "stripe", "+y", 0.86, ("segments", 32)), Ring([-0.16, 0.06, 0.52], [0.16, 0.1, 0.84], "stripe", "+y", 0.86, ("segments", 32)),
                D([-0.2, 0.2, 0.254], [0.2, 0.235, 0.262], "stripe"), D([-0.264, 0.2, -0.2], [-0.254, 0.235, 0.2], "stripe"), D([0.254, 0.2, -0.2], [0.264, 0.235, 0.2], "stripe"),
            ],
        };
    }

    // ---- The saw: a bin, an arm, a blade bar across the front

    private static TB Bar() => S([-0.5, 0.24, 1.0], [0.5, 0.32, 1.36], "edge");

    private static List<TB> Discs(string role, double[] xs, double r = 0.165) =>
        [.. xs.Select(x => Cyl([x - r, 0.326, 1.18 - r], [x + r, 0.345, 1.18 + r], role, "+y", ("segments", 24)))];

    private static TB SawFloor(string role = "body") => S([-0.5, 0, -0.5], [0.5, 0.05, 0.5], role);

    private static Shape Saw(int t)
    {
        if (t == 1) // a slatted crate on skids, a crooked boom, two mismatched blades
        {
            var slats = new List<TB>();
            foreach (var (y0, y1) in new[] { (0.05, 0.3), (0.38, 0.66) })
                slats.AddRange(Walls(-0.5, 0.5, y0, y1, -0.5, 0.5, 0.06, "timber"));
            return new Shape
            {
                Solid = [SawFloor(), .. slats,
                    S([-0.5, 0.3, -0.5], [-0.44, 0.38, -0.44], "timber"), S([0.44, 0.3, 0.44], [0.5, 0.38, 0.5], "timber"),
                    S([-0.09, 0.2, 0.5], [0.09, 0.36, 1.0], "frame"), Bar()],
                Detail = [.. Discs("edge", [-0.3]), .. Discs("plate2", [0.28], 0.14),
                    D([-0.34, 0.1, 0.5], [-0.1, 0.42, 0.512], "plate"), D([0.5, 0.42, 0.1], [0.512, 0.62, 0.42], "plate2"), D([-0.1, 0.42, -0.512], [0.3, 0.62, -0.5], "plate"),
                    Cyl([0.1, 0.36, 0.58], [0.3, 0.6, 0.78], "motor"),
                    D([-0.5, 0.0, -0.53], [-0.42, 0.04, 0.53], "timber"), D([0.42, 0.0, -0.53], [0.5, 0.04, 0.53], "timber")],
            };
        }
        if (t == 2) // a square painted bin, a motor on a pedestal, a straight arm under a cast guard
            return new Shape
            {
                Solid = [SawFloor(), .. Walls(-0.5, 0.5, 0.05, 0.7, -0.5, 0.5, 0.06, "body"),
                    S([-0.09, 0.2, 0.5], [0.09, 0.36, 1.0], "frame"), S([-0.14, 0.36, 0.6], [0.14, 0.56, 0.88], "motor"), Bar()],
                Detail = [.. Discs("edge", [-0.33, 0, 0.33]),
                    D([-0.506, 0.66, -0.506], [0.506, 0.69, -0.438], "trim"), D([-0.506, 0.66, 0.438], [0.506, 0.69, 0.506], "trim"),
                    D([-0.508, 0.03, 0.494], [0.508, 0.12, 0.508], "frame"), D([-0.508, 0.03, -0.508], [0.508, 0.12, -0.494], "frame"), D([-0.52, 0.355, 0.98], [0.52, 0.375, 1.02], "trim"),
                    D([-0.52, 0.355, 1.0], [-0.5, 0.42, 1.36], "frame"), D([0.5, 0.355, 1.0], [0.52, 0.42, 1.36], "frame"), D([-0.52, 0.42, 1.0], [0.52, 0.44, 1.02], "trim"),
                    .. Rivets([.. new[] { -0.4, -0.2, 0.2, 0.4 }.Select(x => new[] { x, 0.55, 0.512 })], 0.012)],
            };
        if (t == 3) // a raised loading cab at the back, twin arms and a yellow gearbox
            return new Shape
            {
                Solid = [SawFloor(), .. Walls(-0.5, 0.5, 0.05, 0.7, -0.5, 0.5, 0.06, "body"),
                    S([-0.5, 0.7, -0.5], [0.5, 0.8, -0.34], "trim"),
                    S([-0.34, 0.2, 0.5], [-0.2, 0.36, 1.0], "frame"), S([0.2, 0.2, 0.5], [0.34, 0.36, 1.0], "frame"), S([-0.2, 0.2, 0.86], [0.2, 0.36, 1.0], "accent"), Bar()],
                Detail = [.. Discs("edge", [-0.33, 0, 0.33]),
                    .. Hazard([-0.52, 0.355, 0.97], [0.52, 0.4, 1.0], 4), Cyl([-0.45, 0.46, 0.505], [0.45, 0.53, 0.575], "pipe", "+x"),
                    Ring([0.3, 0.44, 0.565], [0.36, 0.55, 0.59], "coil", "+z", 0.6, ("ends", "closed")), .. SideLadder(-0.3, -0.06, 0.02, 0.76, -0.5, 3, -0.03),
                    D([0.3, 0.8, -0.44], [0.36, 0.84, -0.38], "light")],
            };
        if (t == 4) // a low hull with a closed nose: the arm is a wide armoured wedge, sandbags on the rim
            return new Shape
            {
                Solid = [SawFloor(), .. Walls(-0.5, 0.5, 0.05, 0.66, -0.5, 0.5, 0.06, "body"),
                    S([-0.5, 0.1, 0.5], [0.5, 0.36, 1.0], "body", "wedge", ("front", "+z"), ("taper", L(0.96, 0.4)), ("shift", L(0, -0.28))), Bar()],
                Detail = [
                    Wedge([-0.52, 0.32, 1.0], [0.52, 0.46, 1.38], "plate", "+z", L(0.92, 0.3), ("shift", L(0, -0.3))), D([-0.512, 0.44, -0.512], [0.512, 0.5, -0.5], "accent"), D([-0.512, 0.44, 0.5], [0.512, 0.5, 0.512], "accent"), D([-0.512, 0.44, -0.5], [-0.5, 0.5, 0.5], "accent"), D([0.5, 0.44, -0.5], [0.512, 0.5, 0.5], "accent"),
                    Sandbag([-0.46, 0.66, -0.5], [0.46, 0.75, -0.42]), Sandbag([-0.46, 0.66, 0.42], [0.46, 0.75, 0.5]), D([-0.5, 0.2, 1.37], [0.5, 0.31, 1.385], "dark"),
                    D([0.16, 0.34, 0.6], [0.22, 0.4, 0.66], "light")],
            };
        return new Shape // a capsule roof over the bin's back, two slim booms, a core between them, rings for blades
        {
            Solid = [SawFloor("trim"), .. Walls(-0.5, 0.5, 0.05, 0.7, -0.5, 0.5, 0.06, "body"),
                S([-0.44, 0.7, -0.48], [0.44, 0.82, -0.14], "plate2", "rounded", ("radius", 0.48), ("edges", "long"), ("front", "+x")),
                S([-0.46, 0.22, 0.5], [-0.36, 0.34, 1.0], "plate2", "rounded", ("radius", 0.48), ("edges", "long"), ("front", "+z")), S([0.36, 0.22, 0.5], [0.46, 0.34, 1.0], "plate2", "rounded", ("radius", 0.48), ("edges", "long"), ("front", "+z")),
                S([-0.5, 0.24, 1.0], [0.5, 0.32, 1.36], "edge", "rounded", ("radius", 0.45), ("edges", "long"), ("front", "+x"))],
            Detail = [
                Ring([-0.495, 0.33, 1.015], [-0.165, 0.37, 1.345], "coil", "+y", 0.88, ("segments", 32)), Ring([-0.165, 0.33, 1.015], [0.165, 0.37, 1.345], "coil", "+y", 0.88, ("segments", 32)), Ring([0.165, 0.33, 1.015], [0.495, 0.37, 1.345], "coil", "+y", 0.88, ("segments", 32)),
                Ball([-0.09, 0.2, 0.62], [0.09, 0.38, 0.8], "coil"), D([-0.36, 0.27, 0.68], [0.36, 0.29, 0.74], "stripe"),
                D([-0.42, 0.705, 0.47], [0.42, 0.72, 0.5], "stripe"), D([-0.42, 0.705, -0.5], [0.42, 0.72, -0.47], "stripe"), D([-0.36, 0.3, 0.5], [0.36, 0.33, 0.512], "stripe"),
                D([-0.33, 0.1, -0.512], [-0.31, 0.64, -0.5], "stripe"), D([0.31, 0.1, -0.512], [0.33, 0.64, -0.5], "stripe"), D([-0.31, 0.56, -0.512], [0.31, 0.58, -0.5], "stripe")],
        };
    }

    // ---- The sawmill: two beds and a blade frame between them

    private static List<TB> Beds(string role = "trim", double top = 0.45) =>
        [.. Walls(-0.5, 0.5, 0.02, top, -0.5, -0.1, 0.06, role), .. Walls(-0.5, 0.5, 0.02, top, 0.1, 0.5, 0.06, role)];

    private static List<TB> Blades() => [D([-0.015, 0.52, -0.28], [0.015, 0.98, 0.28], "edge"), D([-0.018, 0.46, -0.18], [0.018, 1.03, 0.18], "edge"), D([-0.12, 0.72, -0.03], [0.12, 0.78, 0.03], "dark")];

    private static List<TB> LowBlades() => [D([-0.015, 0.46, -0.24], [0.015, 0.82, 0.24], "edge"), D([-0.018, 0.46, -0.16], [0.018, 0.86, 0.16], "edge"), D([-0.12, 0.62, -0.03], [0.12, 0.68, 0.03], "dark")];

    private static Shape Sawmill(int t)
    {
        if (t == 1) // a timber trestle: splayed posts, a motor on the floor with a belt up to the arbor
            return new Shape
            {
                Solid = [Floor(), .. Beds("timber", 0.36),
                    S([-0.3, 0.02, -0.08], [-0.24, 0.4, 0.08], "timber"), S([-0.25, 0.4, -0.08], [-0.19, 0.72, 0.08], "timber"), S([-0.2, 0.72, -0.08], [-0.14, 0.93, 0.08], "timber"),
                    S([0.24, 0.02, -0.08], [0.3, 0.4, 0.08], "timber"), S([0.19, 0.4, -0.08], [0.25, 0.72, 0.08], "timber"), S([0.14, 0.72, -0.08], [0.2, 0.93, 0.08], "timber"),
                    S([-0.24, 0.93, -0.12], [0.24, 0.99, 0.12], "frame"), S([0.3, 0.02, -0.09], [0.44, 0.2, 0.09], "motor")],
                Detail = [D([-0.015, 0.45, -0.22], [0.015, 0.9, 0.22], "edge"), D([-0.018, 0.45, -0.14], [0.018, 0.92, 0.14], "edge"), D([-0.19, 0.62, -0.03], [0.19, 0.68, 0.03], "dark"),
                    D([0.2, 0.2, -0.02], [0.22, 0.64, 0.02], "dark"), D([-0.5, 0.02, -0.512], [-0.1, 0.26, -0.5], "plate"), D([0.04, 0.1, -0.512], [0.42, 0.32, -0.5], "plate2"), D([-0.3, 0.08, 0.5], [0.1, 0.3, 0.512], "plate2"),
                    D([-0.28, 0.99, -0.14], [0.3, 1.03, 0.16], "plate2")],
            };
        if (t == 2) // a cast frame with a beam, the motor on the frame, a flywheel on the arbor
            return new Shape
            {
                Solid = [Floor(), .. Beds("body"),
                    S([-0.18, 0.02, -0.1], [-0.12, 1.05, 0.1]), S([0.12, 0.02, -0.1], [0.18, 1.05, 0.1]), S([-0.2, 1.05, -0.16], [0.2, 1.12, 0.16], "accent"), S([0.18, 0.6, -0.12], [0.46, 0.88, 0.12], "motor")],
                Detail = [.. Blades(),
                    D([-0.506, 0.45, -0.506], [0.506, 0.47, -0.437], "trim"), D([-0.506, 0.45, 0.437], [0.506, 0.47, 0.506], "trim"),
                    Cyl([-0.34, 0.54, -0.14], [-0.28, 0.82, 0.14], "dark", "-x", ("segments", 24)), Ring([-0.345, 0.58, -0.1], [-0.275, 0.78, 0.1], "trim", "-x", 0.72, ("ends", "closed")), D([-0.28, 0.66, -0.03], [-0.18, 0.72, 0.03], "frame"),
                    D([-0.206, 0.02, -0.126], [-0.1, 0.12, 0.126], "frame"), D([0.1, 0.02, -0.126], [0.206, 0.12, 0.126], "frame"),
                    .. Rivets([[-0.15, 0.9, 0.1], [0.15, 0.9, 0.1], [-0.15, 0.3, 0.1], [0.15, 0.3, 0.1]], 0.012)],
            };
        if (t == 3) // a gantry the width of the mill: tall outer legs, an overhead beam, the motor hung from it
            return new Shape
            {
                Solid = [Floor(), .. Beds("trim"),
                    S([-0.5, 0.02, -0.07], [-0.42, 1.12, 0.07], "accent"), S([0.42, 0.02, -0.07], [0.5, 1.12, 0.07], "accent"), S([-0.42, 1.04, -0.1], [0.42, 1.12, 0.1], "frame"),
                    S([-0.16, 0.88, -0.1], [0.16, 1.04, 0.1], "motor"), S([-0.06, 0.02, -0.08], [0.06, 0.42, 0.08], "frame")],
                Detail = [.. Blades(),
                    .. Hazard([-0.505, 0.455, -0.505], [0.505, 0.475, -0.44], 3), D([-0.505, 0.455, 0.44], [0.505, 0.475, 0.505], "accent"),
                    Cyl([0.16, 0.94, -0.04], [0.42, 1.02, 0.04], "pipe", "+x"),
                    .. SideLadder(-0.07, 0.07, 0.48, 1.04, -0.42, 4, 0.02), D([0.3, 1.12, -0.03], [0.36, 1.16, 0.03], "light")],
            };
        if (t == 4) // a low armoured housing over the blade, sandbagged beds
            return new Shape
            {
                Solid = [Floor(), .. Beds("trim", 0.4),
                    S([-0.34, 0.02, -0.1], [-0.12, 0.84, 0.1], "body", "bevel", ("radius", 0.3), ("edges", "long"), ("front", "+x")), S([0.12, 0.02, -0.1], [0.34, 0.84, 0.1], "body", "bevel", ("radius", 0.3), ("edges", "long"), ("front", "+x")),
                    S([-0.34, 0.84, -0.14], [0.34, 0.92, 0.14], "plate")],
                Detail = [.. LowBlades(),
                    Wedge([-0.34, 0.92, -0.14], [0.34, 1.0, 0.14], "plate2", "+y", L(0.7, 0.4)), D([-0.345, 0.5, -0.105], [-0.12, 0.57, 0.105], "accent"), D([0.12, 0.5, -0.105], [0.345, 0.57, 0.105], "accent"),
                    Sandbag([-0.5, 0.4, -0.5], [0.5, 0.46, -0.44]), Sandbag([-0.5, 0.4, 0.44], [0.5, 0.46, 0.5]), Sandbag([-0.5, 0.4, -0.16], [0.5, 0.46, -0.1]), Sandbag([-0.5, 0.4, 0.1], [0.5, 0.46, 0.16]),
                    D([0.26, 0.7, 0.1], [0.32, 0.74, 0.115], "light")],
            };
        return new Shape // the blade in a standing ring, two slim pylons
        {
            Solid = [Floor(), .. Beds("body"),
                H([-0.42, 0.02, -0.05], [-0.34, 0.8, 0.05]), H([0.34, 0.02, -0.05], [0.42, 0.8, 0.05]), H([-0.34, 0.86, -0.05], [0.34, 1.1, 0.05])],
            Detail = [
                Ring([-0.44, 0.26, -0.05], [0.44, 1.14, 0.05], "plate2", "+z", 0.86, ("segments", 48)),
                Ring([-0.36, 0.34, -0.06], [0.36, 1.06, 0.06], "coil", "+z", 0.92, ("segments", 48)),
                D([-0.015, 0.5, -0.26], [0.015, 0.94, 0.26], "stripe"), Ball([-0.1, 0.64, -0.08], [0.1, 0.84, 0.08], "coil"),
                D([-0.46, 0.452, -0.48], [0.46, 0.468, -0.46], "stripe"), D([-0.46, 0.452, 0.46], [0.46, 0.468, 0.48], "stripe"), D([-0.46, 0.452, -0.14], [0.46, 0.468, -0.12], "stripe"), D([-0.46, 0.452, 0.12], [0.46, 0.468, 0.14], "stripe")],
        };
    }

    // ---- The smelter: a furnace with a mouth over the aisle, a hopper and a stack

    /// <summary>The input's hopper: walls round the room the input bin fills.</summary>
    private static List<TB> Hop(double y0, double y1, string role = "frame", double x0 = -0.34, double x1 = 0.1) => Walls(x0, x1, y0, y1, -0.44, -0.11, 0.04, role);

    private static Shape Smelter(int t)
    {
        List<TB> floor = [Floor(), .. Tray(0.36)];
        if (t == 1) // a squat kiln, lashed together, a short crooked flue
            return new Shape
            {
                Hopper = (0.685, 0.86),
                Solid = [.. floor, .. Plinths(-0.25, "trim", 0.4).Take(2), .. Hop(0.68, 0.88, "timber"),
                    S([-0.44, 0.02, -0.5], [0.44, 0.5, -0.05], "core"), S([-0.36, 0.5, -0.46], [0.36, 0.68, -0.09]), S([0.14, 0.68, -0.38], [0.3, 1.0, -0.22], "dark")],
                Detail = [
                    D([-0.3, 0.16, -0.06], [0.3, 0.4, -0.04], "heat"), D([-0.4, 0.42, -0.05], [-0.08, 0.49, -0.04], "plate"), D([0.14, 0.04, -0.05], [0.42, 0.14, -0.04], "plate2"),
                    D([-0.45, 0.06, -0.47], [-0.44, 0.17, -0.37], "plate"), D([-0.42, 0.02, -0.53], [-0.37, 0.72, -0.5], "timber"), D([0.37, 0.02, -0.53], [0.42, 0.66, -0.5], "timber"),
                    D([-0.46, 0.44, -0.53], [0.46, 0.49, -0.5], "timber"), Cyl([0.39, 0.5, -0.21], [0.49, 0.66, -0.11], "motor"), D([0.1, 1.0, -0.42], [0.28, 1.06, -0.2], "coil"), D([0.13, 0.8, -0.39], [0.31, 0.88, -0.21], "plate2")],
            };
        if (t == 2) // a brick-bodied furnace: one proper stack, a cream-banded door
            return new Shape
            {
                Solid = [.. floor, .. Hop(0.8, 1.0),
                    S([-0.5, 0.02, -0.5], [0.5, 0.6, -0.05], "core"), S([-0.38, 0.6, -0.46], [0.38, 0.8, -0.09]), S([0.16, 0.8, -0.38], [0.32, 1.34, -0.22], "dark"), S([0.14, 1.34, -0.4], [0.34, 1.4, -0.2])],
                Detail = [
                    D([-0.3, 0.18, -0.05], [0.3, 0.42, -0.03], "heat"), D([-0.508, 0.03, -0.508], [0.508, 0.1, -0.042], "frame"), D([-0.508, 0.54, -0.508], [0.508, 0.57, -0.042], "trim"), D([-0.386, 0.77, -0.466], [0.386, 0.79, -0.084], "trim"),
                    Ring([0.135, 1.0, -0.405], [0.345, 1.04, -0.195], "trim", "+y", 0.74, ("ends", "closed")), Ring([0.135, 1.2, -0.405], [0.345, 1.24, -0.195], "trim", "+y", 0.74, ("ends", "closed")),
                    D([-0.33, 0.43, -0.047], [0.33, 0.46, -0.03], "trim"), D([-0.33, 0.14, -0.05], [-0.3, 0.46, -0.028], "frame"), D([0.3, 0.14, -0.05], [0.33, 0.46, -0.028], "frame"),
                    .. Rivets([.. new[] { -0.44, -0.38, 0.38, 0.44 }.Select(x => new[] { x, 0.5, -0.044 })], 0.012)],
            };
        if (t == 3) // a tall blast furnace: a narrow shaft, a stepped charging ramp up its side, a downcomer pipe
            return new Shape
            {
                Hopper = (1.065, 1.24),
                Solid = [.. floor, .. Hop(1.06, 1.26),
                    S([0.44, 0.02, -0.35], [0.5, 0.46, -0.15], "frame"), S([-0.1, 0.02, -0.5], [0.1, 0.46, -0.44], "frame"),
                    S([-0.36, 0.02, -0.46], [0.12, 0.5, -0.05], "core"), S([-0.34, 0.5, -0.46], [0.1, 1.06, -0.09], "core"),
                    S([-0.5, 0.02, -0.34], [-0.36, 0.42, -0.14], "trim"), S([-0.44, 0.42, -0.34], [-0.34, 0.74, -0.14], "trim"),
                    S([0.18, 0.02, -0.38], [0.32, 1.4, -0.24], "dark"), S([0.1, 0.84, -0.34], [0.18, 0.96, -0.28], "dark")],
                Detail = [
                    D([-0.3, 0.14, -0.05], [0.08, 0.4, -0.03], "heat"), .. Hazard([-0.36, 0.42, -0.044], [0.12, 0.48, -0.034], 3), .. SideLadder(-0.36, -0.2, 0.5, 1.06, 0.1, 3, 0.016),
                    Ring([0.16, 1.06, -0.4], [0.34, 1.1, -0.22], "accent", "+y", 0.8, ("ends", "closed")), Ring([0.16, 0.6, -0.4], [0.34, 0.64, -0.22], "accent", "+y", 0.8, ("ends", "closed")),
                    Wedge([-0.5, 0.42, -0.33], [-0.44, 0.74, -0.15], "plate2", "+y", L(0.2, 1), ("shift", L(0.4, 0))), D([0.14, 0.2, -0.46], [0.17, 0.26, -0.4], "light")],
            };
        if (t == 4) // a low bunker: the hopper let into the roof, a sloped front, a stub flue, sandbags
            return new Shape
            {
                Hopper = (0.465, 0.64),
                Solid = [.. floor, .. Hop(0.46, 0.66, "trim"),
                    S([-0.5, 0.02, -0.5], [0.5, 0.46, -0.05], "core", "bevel", ("radius", 0.12), ("edges", "long"), ("front", "+x")), S([0.16, 0.46, -0.38], [0.32, 0.82, -0.22], "dark", "bevel", ("radius", 0.3), ("edges", "long"), ("front", "+y"))],
                Detail = [
                    D([-0.3, 0.14, -0.05], [0.3, 0.3, -0.03], "heat"), D([-0.52, 0.3, -0.05], [0.52, 0.34, -0.03], "accent"),
                    Wedge([-0.5, 0.46, -0.2], [-0.34, 0.6, -0.05], "plate", "+y", L(0.6, 0.4)), Wedge([0.34, 0.46, -0.2], [0.5, 0.6, -0.05], "plate", "+y", L(0.6, 0.4)),
                    Ring([0.13, 0.6, -0.41], [0.35, 0.66, -0.19], "plate2", "+y", 0.7, ("ends", "closed")), Sandbag([-0.5, 0.02, -0.56], [-0.1, 0.07, -0.505]), Sandbag([0.1, 0.02, -0.56], [0.5, 0.07, -0.505]), Sandbag([-0.46, 0.07, -0.56], [-0.14, 0.12, -0.505]), Sandbag([0.14, 0.07, -0.56], [0.46, 0.12, -0.505]),
                    D([0.38, 0.4, -0.03], [0.44, 0.44, -0.02], "light")],
            };
        return new Shape // a standing ring round a violet core, a slim emitter for a stack
        {
            Hopper = (1.045, 1.2),
            Solid = [.. floor, .. Hop(1.04, 1.22, "body"), .. Plinths(-0.25, "trim", 0.46),
                H([-0.32, 0.02, -0.38], [0.32, 0.12, -0.12]), H([-0.48, 0.3, -0.38], [-0.38, 0.8, -0.12]), H([0.38, 0.3, -0.38], [0.48, 0.8, -0.12]), H([-0.34, 0.96, -0.46], [0.34, 1.04, -0.09]),
                S([0.16, 1.0, -0.38], [0.32, 1.32, -0.22], "plate2", "rounded", ("radius", 0.48), ("edges", "long"), ("front", "+y"))],
            Detail = [
                Ring([-0.49, 0.05, -0.39], [0.49, 1.03, -0.11], "plate2", "+z", 0.8, ("segments", 48)), Ring([-0.39, 0.15, -0.26], [0.39, 0.93, -0.06], "stripe", "+z", 0.94, ("segments", 48)),
                Ball([-0.2, 0.34, -0.36], [0.2, 0.74, -0.04], "coil"), Ring([0.13, 1.06, -0.41], [0.35, 1.1, -0.19], "stripe", "+y", 0.82, ("segments", 32)), Ring([0.13, 1.2, -0.41], [0.35, 1.24, -0.19], "coil", "+y", 0.82, ("segments", 32)),
                D([-0.36, 0.236, 0.5], [0.36, 0.246, 0.51], "stripe")],
        };
    }

    // ---- The foundry: two furnaces, a mouth between them, twin stacks, a long hopper

    private static List<TB> FoundryHop(double y0, double y1, string role = "frame") =>
        [.. Walls(-0.42, 0.42, y0, y1, -0.44, -0.11, 0.04, role), S([-0.05, y0, -0.4], [0.05, y1, -0.15], role)];

    private static Shape Foundry(int t)
    {
        List<TB> floor = [Floor(), .. Tray(0.38)];
        List<TB> twin = [S([-0.5, 0.02, -0.5], [-0.12, 0.62, -0.05], "core"), S([0.12, 0.02, -0.5], [0.5, 0.62, -0.05], "core")];
        var back = S([-0.12, 0.02, -0.5], [0.12, 0.3, -0.12], "dark");
        if (t == 1) // two squat kilns under one timber beam, odd stacks
            return new Shape
            {
                Hopper = (0.585, 0.74),
                Solid = [.. floor, .. FoundryHop(0.58, 0.76, "timber"),
                    S([-0.5, 0.02, -0.48], [-0.1, 0.5, -0.06], "core"), S([0.1, 0.02, -0.48], [0.5, 0.46, -0.06], "core"), back, S([-0.46, 0.5, -0.46], [0.46, 0.58, -0.09]),
                    S([-0.5, 0.58, -0.44], [-0.42, 0.98, -0.34], "dark"), S([0.42, 0.58, -0.3], [0.5, 0.84, -0.2], "dark")],
                Detail = [
                    D([-0.11, 0.05, -0.12], [0.11, 0.28, -0.1], "heat"), D([-0.46, 0.3, -0.05], [-0.2, 0.48, -0.04], "plate"), D([0.18, 0.08, -0.05], [0.44, 0.3, -0.04], "plate2"), D([-0.2, 0.32, -0.07], [0.2, 0.38, -0.055], "timber"),
                    D([-0.44, 0.02, -0.51], [-0.39, 0.58, -0.48], "timber"), D([0.39, 0.02, -0.51], [0.44, 0.58, -0.48], "timber"), D([-0.5, 0.42, -0.52], [0.5, 0.47, -0.49], "timber"), D([-0.52, 0.98, -0.46], [-0.4, 1.02, -0.32], "coil"), D([0.4, 0.84, -0.32], [0.52, 0.88, -0.18], "plate2")],
            };
        if (t == 2) // a matched pair of painted towers, two round stacks on the bridge, a cream arch
            return new Shape
            {
                Solid = [.. floor, .. FoundryHop(0.72, 0.9), .. twin, back, S([-0.46, 0.62, -0.46], [0.46, 0.72, -0.09]),
                    S([-0.3, 0.9, -0.34], [-0.16, 1.22, -0.2], "dark", "cylinder", ("front", "+y"), ("segments", 20)), S([0.16, 0.9, -0.34], [0.3, 1.22, -0.2], "dark", "cylinder", ("front", "+y"), ("segments", 20))],
                Detail = [
                    D([-0.11, 0.05, -0.12], [0.11, 0.28, -0.1], "heat"), D([-0.508, 0.03, -0.508], [-0.114, 0.1, -0.042], "frame"), D([0.114, 0.03, -0.508], [0.508, 0.1, -0.042], "frame"),
                    D([-0.508, 0.54, -0.508], [-0.114, 0.57, -0.042], "trim"), D([0.114, 0.54, -0.508], [0.508, 0.57, -0.042], "trim"), D([-0.14, 0.3, -0.135], [0.14, 0.34, -0.1], "trim"),
                    Ring([-0.32, 1.17, -0.36], [-0.14, 1.21, -0.18], "trim", "+y", 0.74, ("ends", "closed")), Ring([0.14, 1.17, -0.36], [0.32, 1.21, -0.18], "trim", "+y", 0.74, ("ends", "closed"))],
            };
        if (t == 3) // stepped: a tall tower left, a low one right, stacks at both ends, a pipe bridge
            return new Shape
            {
                Hopper = (0.925, 1.08),
                Solid = [.. floor, .. FoundryHop(0.92, 1.1),
                    S([-0.5, 0.02, -0.5], [-0.12, 0.92, -0.05], "core"), S([0.12, 0.02, -0.5], [0.5, 0.46, -0.05], "core"), S([0.12, 0.46, -0.46], [0.46, 0.92, -0.09], "trim"), back, S([-0.12, 0.62, -0.46], [0.12, 0.92, -0.09]),
                    S([-0.5, 0.92, -0.5], [-0.42, 1.22, -0.44], "dark"), S([0.46, 0.46, -0.3], [0.5, 1.22, -0.2], "dark")],
                Detail = [
                    D([-0.11, 0.05, -0.12], [0.11, 0.28, -0.1], "heat"), .. Hazard([-0.508, 0.06, -0.04], [-0.12, 0.12, -0.03], 2), .. Hazard([0.12, 0.06, -0.04], [0.508, 0.12, -0.03], 2),
                    Cyl([-0.42, 1.14, -0.3], [0.46, 1.2, -0.24], "pipe", "+x"), Ring([-0.035, 1.11, -0.325], [0.035, 1.23, -0.215], "coil", "+x", 0.6, ("ends", "closed")), .. SideLadder(-0.42, -0.2, 0.46, 0.92, 0.46, 2, 0.016), D([0.38, 0.46, -0.05], [0.44, 0.52, -0.02], "light")],
            };
        if (t == 4) // one low bunker: a long roof with sloped flanks, stub stacks at the ends
            return new Shape
            {
                Hopper = (0.525, 0.68),
                Solid = [.. floor, .. FoundryHop(0.52, 0.7, "trim"),
                    S([-0.5, 0.02, -0.5], [0.5, 0.42, -0.22], "core"), S([-0.5, 0.02, -0.22], [-0.16, 0.26, -0.05], "core"), S([0.16, 0.02, -0.22], [0.5, 0.26, -0.05], "core"), S([-0.42, 0.42, -0.46], [0.42, 0.52, -0.09], "body", "bevel", ("radius", 0.2), ("edges", "long"), ("front", "+x")),
                    S([-0.5, 0.42, -0.44], [-0.42, 0.82, -0.3], "dark", "bevel", ("radius", 0.3), ("edges", "long"), ("front", "+y")), S([0.42, 0.42, -0.44], [0.5, 0.82, -0.3], "dark", "bevel", ("radius", 0.3), ("edges", "long"), ("front", "+y"))],
                Detail = [
                    D([-0.13, 0.05, -0.235], [0.13, 0.26, -0.215], "heat"), D([-0.512, 0.32, -0.512], [0.512, 0.37, -0.215], "accent"), Wedge([-0.5, 0.26, -0.22], [-0.16, 0.42, -0.05], "plate", "+z", L(1, 0.1), ("shift", L(0, -0.45))), Wedge([0.16, 0.26, -0.22], [0.5, 0.42, -0.05], "plate", "+z", L(1, 0.1), ("shift", L(0, -0.45))),
                    Sandbag([-0.5, 0.02, -0.56], [-0.1, 0.07, -0.505]), Sandbag([0.1, 0.02, -0.56], [0.5, 0.07, -0.505]), Sandbag([-0.46, 0.07, -0.56], [-0.14, 0.12, -0.505]), D([0.3, 0.18, -0.05], [0.36, 0.22, -0.04], "light")],
            };
        return new Shape // twin capsule pods joined by a coil round a violet core
        {
            Hopper = (0.925, 1.08),
            Solid = [.. floor, .. FoundryHop(0.92, 1.1, "body"), .. Plinths(-0.25, "trim", 0.46).Skip(2),
                S([-0.5, 0.02, -0.46], [-0.14, 0.92, -0.09], "core", "rounded", ("radius", 0.45), ("edges", "long"), ("front", "+y")), S([0.14, 0.02, -0.46], [0.5, 0.92, -0.09], "core", "rounded", ("radius", 0.45), ("edges", "long"), ("front", "+y")),
                S([-0.42, 1.1, -0.32], [-0.3, 1.22, -0.2], "plate2", "cylinder", ("front", "+y"), ("segments", 24)),
                S([0.3, 1.1, -0.32], [0.42, 1.22, -0.2], "plate2", "cylinder", ("front", "+y"), ("segments", 24))],
            Detail = [
                Ring([-0.5, 0.56, -0.47], [-0.14, 0.6, -0.08], "stripe", "+y", 0.9, ("segments", 32)), Ring([0.14, 0.56, -0.47], [0.5, 0.6, -0.08], "stripe", "+y", 0.9, ("segments", 32)),
                Ring([-0.5, 0.26, -0.47], [-0.14, 0.3, -0.08], "stripe", "+y", 0.9, ("segments", 32)), Ring([0.14, 0.26, -0.47], [0.5, 0.3, -0.08], "stripe", "+y", 0.9, ("segments", 32)),
                Ball([-0.1, 0.06, -0.26], [0.1, 0.26, -0.06], "coil"), Ring([-0.15, 0.02, -0.12], [0.15, 0.32, -0.09], "stripe", "+z", 0.86, ("segments", 32))],
        };
    }

    // ---- The assembler: an open gantry with a low hopper at the back and an arm over the tray

    private static TB AsmFloor() => S([-0.5, 0, -0.5], [0.5, 0.03, 0.5]);

    private static List<TB> AsmHopper(string role = "dark") => Walls(-0.46, 0.46, 0.03, 0.34, -0.46, -0.04, 0.02, role);

    private static List<TB> Posts(Func<double, double, TB> post, double[] xs, double[] zs)
    {
        var o = new List<TB>();
        foreach (var x in xs)
            foreach (var z in zs)
                o.Add(post(x, z));
        return o;
    }

    private static Shape Assembler(int t)
    {
        if (t == 1) // two timber frames with crossbars, no rails: a wonky motor on a beam
            return new Shape
            {
                Solid = [AsmFloor(), .. AsmHopper("trim"), .. Posts((x, z) => S([x, 0.03, z], [x + 0.04, 0.86, z + 0.05], "timber"), [-0.5, 0.46], [-0.5, 0.45]),
                    S([-0.5, 0.86, -0.5], [0.5, 0.92, -0.42], "timber"), S([-0.5, 0.86, 0.42], [0.5, 0.92, 0.5], "timber"), S([-0.5, 0.15, -0.46], [-0.46, 0.33, -0.04], "timber"), S([0.46, 0.15, -0.46], [0.5, 0.33, -0.04], "timber"),
                    S([-0.5, 0.92, -0.08], [0.5, 0.98, 0.08], "timber"), S([-0.06, 0.62, -0.1], [0.06, 0.92, 0.1], "motor")],
                Detail = [
                    D([-0.02, 0.56, -0.02], [0.02, 0.62, 0.02], "edge"), D([-0.3, 0.06, -0.482], [0.1, 0.28, -0.47], "plate"), D([0.04, 0.34, -0.47], [0.44, 0.4, -0.04], "plate2"),
                    Cyl([0.07, 0.72, -0.08], [0.2, 0.9, 0.05], "motor"), D([0.07, 0.9, 0.0], [0.2, 0.92, 0.05], "plate2")],
            };
        if (t == 2) // a machine tool: a full frame, cream rails, the arm on a rail
            return new Shape
            {
                Solid = [AsmFloor(), .. AsmHopper(), .. Posts((x, z) => S([x, 0.03, z], [x + 0.04, 0.9, z + 0.04], "frame"), [-0.5, 0.46], [-0.5, 0.46]),
                    .. Walls(-0.5, 0.5, 0.9, 0.96, -0.5, 0.5, 0.06, "body"),
                    S([-0.5, 0.15, -0.46], [-0.46, 0.35, 0.46], "frame"), S([0.46, 0.15, -0.46], [0.5, 0.35, 0.46], "frame"), S([-0.06, 0.62, -0.1], [0.06, 0.9, 0.1], "motor")],
                Detail = [
                    D([-0.44, 0.92, -0.02], [0.44, 0.94, 0.02], "accent"), D([-0.02, 0.56, -0.02], [0.02, 0.62, 0.02], "edge"), D([-0.504, 0.96, -0.504], [0.504, 0.98, -0.436], "trim"), D([-0.504, 0.96, 0.436], [0.504, 0.98, 0.504], "trim"),
                    D([-0.075, 0.64, -0.115], [0.075, 0.68, 0.115], "trim"), D([-0.5, 0.965, -0.03], [0.5, 1.0, 0.03], "frame"),
                    D([-0.512, 0.03, -0.512], [-0.458, 0.09, -0.448], "frame"), D([0.458, 0.03, -0.512], [0.512, 0.09, -0.448], "frame"), D([-0.512, 0.03, 0.448], [-0.458, 0.09, 0.512], "frame"), D([0.458, 0.03, 0.448], [0.512, 0.09, 0.512], "frame")],
            };
        if (t == 3) // a bridge crane: tall yellow legs, a heavy beam across, the arm hung from a trolley
            return new Shape
            {
                Solid = [AsmFloor(), .. AsmHopper(), .. Posts((x, z) => S([x, 0.03, z], [x + 0.04, 1.0, z + 0.06], "accent"), [-0.5, 0.46], [-0.5, 0.44]),
                    S([-0.5, 1.0, -0.5], [-0.42, 1.1, 0.5], "frame"), S([0.42, 1.0, -0.5], [0.5, 1.1, 0.5], "frame"), S([-0.42, 1.02, -0.09], [0.42, 1.1, 0.09], "accent"), S([-0.5, 0.15, -0.44], [-0.46, 0.35, 0.44], "frame"), S([0.46, 0.15, -0.44], [0.5, 0.35, 0.44], "frame"),
                    S([-0.1, 0.9, -0.12], [0.1, 1.02, 0.12], "motor"), S([-0.05, 0.62, -0.05], [0.05, 0.9, 0.05], "frame")],
                Detail = [
                    D([-0.02, 0.56, -0.02], [0.02, 0.62, 0.02], "edge"), .. Hazard([-0.505, 0.5, 0.444], [-0.49, 1.0, 0.5], 3), .. Hazard([0.49, 0.5, 0.444], [0.505, 1.0, 0.5], 3),
                    Cyl([0.32, 0.34, -0.5], [0.38, 1.02, -0.44], "pipe"), Ring([0.3, 0.7, -0.52], [0.4, 0.74, -0.42], "coil", "+y", 0.5, ("ends", "closed")), D([0.3, 1.1, -0.46], [0.36, 1.14, -0.4], "light")],
            };
        if (t == 4) // a low armoured shelter: thick posts, a sloped roof, the arm caged under it
        {
            var cage = new List<TB>();
            foreach (var x in new[] { -0.1, 0.085 })
                foreach (var z in new[] { -0.14, 0.125 })
                    cage.Add(D([x, 0.62, z], [x + 0.015, 0.72, z + 0.015], "dark"));
            return new Shape
            {
                Solid = [AsmFloor(), .. AsmHopper("trim"), .. Posts((x, z) => S([x, 0.03, z], [x + 0.04, 0.72, z + 0.08], "body", "bevel", ("radius", 0.25), ("edges", "long"), ("front", "+y")), [-0.5, 0.46], [-0.5, 0.42]),
                    S([-0.5, 0.72, -0.5], [0.5, 0.8, 0.5], "body"), S([-0.5, 0.15, -0.42], [-0.46, 0.35, 0.42], "frame"), S([0.46, 0.15, -0.42], [0.5, 0.35, 0.42], "frame"), S([-0.06, 0.6, -0.1], [0.06, 0.72, 0.1], "motor")],
                Detail = [
                    D([-0.02, 0.56, -0.02], [0.02, 0.6, 0.02], "edge"), Wedge([-0.5, 0.8, -0.5], [0.5, 0.89, 0.5], "plate", "+y", L(0.8, 0.7)), D([-0.512, 0.732, -0.512], [0.512, 0.77, 0.512], "accent"),
                    D([-0.46, 0.36, -0.512], [0.46, 0.72, -0.5], "plate"), Sandbag([-0.5, 0.03, -0.56], [0.5, 0.09, -0.505]), Sandbag([-0.5, 0.09, -0.56], [0.5, 0.15, -0.505]),
                    .. cage, D([0.36, 0.42, -0.515], [0.42, 0.46, -0.5], "light")],
            };
        }
        return new Shape // one arch over the tray, a ring for the arm
        {
            Solid = [AsmFloor(), .. AsmHopper("body"),
                H([-0.5, 0.03, -0.5], [-0.46, 0.9, -0.42]), H([0.46, 0.03, -0.5], [0.5, 0.9, -0.42]), H([-0.5, 0.03, 0.42], [-0.46, 0.9, 0.5]), H([0.46, 0.03, 0.42], [0.5, 0.9, 0.5]),
                H([-0.5, 0.15, -0.42], [-0.46, 0.33, -0.04]), H([0.46, 0.15, -0.42], [0.5, 0.33, -0.04]),
                S([-0.5, 0.9, -0.5], [0.5, 0.96, 0.5], "trim", "rounded", ("radius", 0.45), ("edges", "long"), ("front", "+x")), S([-0.06, 0.66, -0.1], [0.06, 0.9, 0.1], "plate2", "rounded", ("radius", 0.48), ("edges", "long"), ("front", "+y"))],
            Detail = [
                .. Posts((x, z) => RoundBox([x, 0.03, z], [x + 0.04, 0.9, z + 0.08], "plate2", "+y", 0.48), [-0.5, 0.46], [-0.5, 0.42]),
                Ring([-0.24, 0.62, -0.24], [0.24, 0.66, 0.24], "stripe", "+y", 0.86, ("segments", 32)), Ball([-0.05, 0.57, -0.05], [0.05, 0.67, 0.05], "coil"),
                D([-0.42, 0.965, 0.44], [0.42, 0.98, 0.47], "stripe"), D([-0.42, 0.965, -0.47], [0.42, 0.98, -0.44], "stripe"), D([-0.512, 0.36, -0.49], [-0.5, 0.88, -0.47], "stripe"), D([0.5, 0.36, -0.49], [0.512, 0.88, -0.47], "stripe"),
                D([-0.42, 0.34, -0.452], [0.42, 0.352, -0.438], "stripe")],
        };
    }

    /// <summary>An Assembler with a nozzle on its test stand, over the tray.</summary>
    private static Shape ThrusterAssembler(int t)
    {
        var look = Assembler(t);
        return new Shape { Solid = look.Solid, Hopper = look.Hopper, Detail = [.. look.Detail, Ring([-0.2, 0.44, 0.12], [0.2, 0.52, 0.42], "coil", "+y", 0.7, ("segments", 24))] };
    }

    /// <summary>An Assembler with a bolt of cloth on a rail, over the tray.</summary>
    private static Shape Tailor(int t)
    {
        var look = Assembler(t);
        return new Shape { Solid = look.Solid, Hopper = look.Hopper, Detail = [.. look.Detail, Cyl([-0.42, 0.42, 0.2], [0.42, 0.54, 0.32], "stripe")] };
    }

    /// <summary>What a line is at a tier.</summary>
    public static Shape Look(string name, int tier) => name switch
    {
        "Drill Module" => Drill(tier),
        "Saw Module" => Saw(tier),
        "Sawmill" => Sawmill(tier),
        "Smelter" => Smelter(tier),
        "Foundry" => Foundry(tier),
        "Assembler" => Assembler(tier),
        "Thruster Assembler" => ThrusterAssembler(tier),
        "Tailor" => Tailor(tier),
        _ => throw new ArgumentException($"no machine line {name}"),
    };
}
