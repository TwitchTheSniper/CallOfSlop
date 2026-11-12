using System.Text;
using TB = CallOfSlop.PartKit.Generators.TierStyle.TB;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's machine tiers (Constructs/Parts/Gathering, Refining and Assembling). Was tools/constructs/machine_tiers.py;
/// docs/sandbox/constructs/production.md, "Machine tiers".
///
/// Six lines of machine (Drill Module, Saw Module, Sawmill, Smelter, Foundry, Assembler, with the Thruster Assembler and the Tailor
/// beside it), five tiers each, following the threat ladder: 1 Scrap, 2 Workshop, 3 Industrial, 4 Military, 5 Plasma. The two
/// gathering modules are vehicle-sized (the Drill Module a compact rig with a small buffer); the others are buildings, sized in BigWidth.
///
/// Higher tiers are physically larger: every box, bin and work box grows by the tier's Size from the part's origin (the centre of its
/// bottom socket). Only what hangs below the floor keeps tier 1's length: a bit reaches the same 1.2 m under the part at every tier,
/// so a bigger drill still fits the ground clearance of a vehicle a small one fits. Sockets stay on the 0.25 m frame grid: scaled, then
/// snapped; a socket the body doesn't reach stands on a small solid mounting pad. The curves are the Curve table: higher tiers work
/// faster, hold more, are tougher, draw more power and weigh more. The look of a tier (colours and drawn details) is <see cref="TierStyle"/>'s.
///
/// The files have their own layout (<see cref="Layout"/>): one key a line, one box, socket, port, config entry and bin a line.
/// </summary>
public sealed class MachineTiers : IPartGenerator
{
    public string Name => "machine_tiers";

    private const string Format = "call-of-slop-construct-part/1";
    private const string Group = "Production";
    private static readonly string[] Tiers = ["", "Scrap", "Workshop", "Industrial", "Military", "Plasma"];
    private static readonly string[] Prefix = ["", "", "Workshop ", "Industrial ", "Military ", "Plasma "];
    private const double Grid = 0.25; // the frame step: sockets stay on it
    private static readonly double[] Size = [0, 1.0, 1.25, 1.5, 2.0, 2.5]; // linear size by tier

    /// <summary>tier: multipliers on tier 1's numbers.</summary>
    private static readonly (int Rate, double Power, double Cap, double Mass, double Health, double Armour, double Attach)[] Curve =
    [
        default,
        (1, 1.0, 1, 1.0, 1.0, 1.0, 1.0),
        (2, 1.8, 2, 1.5, 1.5, 1.5, 1.25),
        (4, 3.2, 3.5, 2.25, 2.25, 2.0, 1.5),
        (8, 5.6, 8, 4.0, 4.0, 3.0, 1.75),
        (16, 9.6, 16, 6.25, 6.25, 4.0, 2.0),
    ];

    private static List<object?> L(params object?[] v) => [.. v];

    private static PyDict Sk(string name, List<object?> at, string dir) => new() { ["name"] = name, ["at"] = at, ["dir"] = dir };

    /// <summary>A production machine's recipe (docs/sandbox/constructs/production.md "Production lines"): Auto, or one its tier makes.</summary>
    private static readonly PyDict Recipe = new()
    {
        ["key"] = "recipe", ["type"] = "choice", ["label"] = "Recipe", ["group"] = "Production", ["tooltip"] = "Auto: what is in the input picks it. Set: only its input goes in.",
    };

    private static readonly PyDict AsmOrderCount = new()
    {
        ["key"] = "orderCount", ["label"] = "Order count", ["group"] = "Orders", ["min"] = 1, ["max"] = 999, ["step"] = 1, ["default"] = 1,
        ["tooltip"] = "Parts the Queue button on its screen asks for: one order of its Recipe.",
    };

    private static readonly PyDict Ink = new()
    {
        ["key"] = "ink", ["type"] = "colour", ["label"] = "Screen colour", ["group"] = "Display", ["default"] = "#000000", ["tooltip"] = "The colour of what its screen shows. Black: its own colours.",
    };

    private sealed class Bin
    {
        public required double[] Min;
        public required double[] Max;
    }

    private sealed class Line
    {
        public required string File;
        public required string Name;
        public required string Section;
        public required double Mass, Health, Armour, Power, Attach;
        public required List<PyDict> Sockets;
        public required string Description;
        public Bin? Bulk;
        public double Capacity;
        public (string Kind, int Rate, Bin Box)? Gather;
        public List<PyDict>? Ports;
        public (string Machine, double Capacity, Bin Box)? Refine;
        public List<(Bin Box, int Stack, string Label)>? Slots;
        public (double Capacity, Bin Box, double[] OutAt, string OutDir, string? Machine)? Assemble;
        public List<object?>? Config;
        public Dictionary<int, string>? Gated;
    }

    private static Bin B(double[] min, double[] max) => new() { Min = min, Max = max };

    private static List<PyDict> ModuleSockets() =>
    [
        Sk("bottom", L(0, 0, 0), "-y"), Sk("+x", L(0.5, 0.3, 0), "+x"), Sk("-x", L(-0.5, 0.3, 0), "-x"), Sk("back", L(0, 0.3, -0.5), "-z"),
    ];

    private static readonly Bin ModuleBin = B([-0.435, 0.055, -0.435], [0.435, 0.64, 0.435]);

    /// <summary>A Drill Module is a compact rig: no bin of its own, a small hopper as its buffer, and a named Chute out it must be connected
    /// through to keep working. The gearhead on top takes the optional drive shaft. tier: (rated rpm of the shaft, N m it takes off it).</summary>
    private static readonly (int Rpm, int Torque)[] DrillShaft = [default, (1800, 40), (1600, 80), (1400, 160), (1200, 320), (1000, 640)];

    /// <summary>The big machines' widths in metres by tier: buildings at every tier, tier 1 included. Their boxes are in units of the
    /// machine's width (x and z run -0.5 to 0.5); sockets stay on the grid because every width is a multiple of 0.5 m.</summary>
    private static readonly Dictionary<string, double[]> BigWidth = new()
    {
        ["Refining/Smelter"] = [0, 3.5, 4.5, 6.0, 8.0, 10.0],
        ["Refining/Sawmill"] = [0, 3.0, 4.0, 5.0, 7.0, 9.0],
        ["Refining/Foundry"] = [0, 4.0, 5.0, 6.5, 8.0, 10.0],
        ["Assembling/Assembler"] = [0, 2.0, 2.5, 3.5, 4.5, 6.0],
        ["Assembling/ThrusterAssembler"] = [0, 2.0, 2.5, 3.5, 4.5, 6.0],
        ["Assembling/Tailor"] = [0, 2.0, 2.5, 3.5, 4.5, 6.0],
    };

    private static List<PyDict> ChutePorts(List<object?> inAt, string inFace, List<object?> outAt, string outFace) =>
    [
        new PyDict { ["name"] = "Chute in", ["type"] = "chute", ["dir"] = "in", ["at"] = inAt, ["face"] = inFace },
        new PyDict { ["name"] = "Chute out", ["type"] = "chute", ["dir"] = "out", ["at"] = outAt, ["face"] = outFace },
    ];

    private static List<PyDict> BigSockets(double side, double sideZ) =>
    [
        Sk("bottom", L(0, 0, 0), "-y"), Sk("+x", L(0.5, side, sideZ), "+x"), Sk("-x", L(-0.5, side, sideZ), "-x"), Sk("back", L(0, 0.25, -0.5), "-z"),
    ];

    private static List<Line> Lines()
    {
        var drill = new Line
        {
            File = "Gathering/DrillModule", Name = "Drill Module", Section = "Drills", Mass = 200, Health = 400, Armour = 6, Power = 900, Attach = 4.0,
            Sockets = [Sk("bottom", L(0, 0, 0), "-y"), Sk("+x", L(0.25, 0.25, 0), "+x"), Sk("-x", L(-0.25, 0.25, 0), "-x"), Sk("back", L(0, 0.25, -0.25), "-z"), Sk("drive", L(0, 1, 0.75), "+y")],
            Bulk = B([-0.155, 0.465, -0.155], [0.155, 0.615, 0.155]), Capacity = 20,
            Description = "Drills a vein: needs power or a shaft, and a chute out.",
            Gather = ("drill", 10, B([-0.1, -1.2, 0.55], [0.1, -0.6, 0.8])),
            Ports =
            [
                new PyDict { ["name"] = "Chute out", ["type"] = "chute", ["dir"] = "out", ["at"] = L(0.19, 0.54, 0), ["face"] = "+x" },
                new PyDict { ["name"] = "Drive in", ["type"] = "shaft", ["dir"] = "in", ["socket"] = "drive", ["at"] = L(0, 1, 0.68), ["face"] = "+y" },
            ],
            // What a low tier can't work yet (Constructs/Resources.json and Refining.json: "minTier").
            Gated = new() { [1] = "Copper needs tier 2 and gold tier 3.", [2] = "Gold needs tier 3." },
        };
        var saw = new Line
        {
            File = "Gathering/SawModule", Name = "Saw Module", Section = "Saws", Mass = 260, Health = 400, Armour = 6, Power = 900, Attach = 4.0,
            Sockets = ModuleSockets(), Bulk = ModuleBin, Capacity = 400,
            Description = "Cuts trees: needs power. Wood lands in its bin.",
            Gather = ("saw", 12, B([-0.7, 0.1, 0.55], [0.7, 1.5, 2.2])),
        };
        var sawmill = new Line
        {
            File = "Refining/Sawmill", Name = "Sawmill", Section = "Sawmills", Mass = 240, Health = 500, Armour = 5, Power = 1800, Attach = 8.0,
            Sockets = BigSockets(0.25, -0.3),
            Ports = ChutePorts(L(0.5, 0.25, -0.33), "+x", L(0.5, 0.25, 0.33), "+x"),
            Bulk = B([-0.435, 0.03, 0.165], [0.435, 0.44, 0.435]), Capacity = 400,
            Description = "Saws wood into planks, planks into cloth. Needs power.",
            Refine = ("sawmill", 200, B([-0.435, 0.03, -0.435], [0.435, 0.44, -0.165])),
        };
        var smelter = new Line
        {
            File = "Refining/Smelter", Name = "Smelter", Section = "Smelters", Mass = 420, Health = 800, Armour = 10, Power = 3000, Attach = 9.0,
            Sockets = BigSockets(0.25, -0.25),
            Ports = ChutePorts(L(0.5, 0.4, -0.25), "+x", L(0.36, 0.12, 0.38), "+x"),
            Bulk = B([-0.335, 0.025, 0.305], [0.335, 0.22, 0.455]), Capacity = 400,
            Description = "Smelts ore to metal, copper and gold, sand to glass. Needs power.",
            Refine = ("smelter", 200, B([-0.295, 0.805, -0.395], [0.055, 0.98, -0.155])),
            Gated = new() { [1] = "Copper ore needs tier 2 and gold ore tier 3.", [2] = "Gold ore needs tier 3." },
        };
        var foundry = new Line
        {
            File = "Refining/Foundry", Name = "Foundry", Section = "Foundries", Mass = 480, Health = 900, Armour = 10, Power = 3500, Attach = 10.0,
            Sockets = BigSockets(0.25, -0.25),
            Ports = ChutePorts(L(0.5, 0.4, -0.25), "+x", L(0.38, 0.12, 0.38), "+x"),
            Bulk = B([-0.355, 0.025, 0.305], [0.355, 0.22, 0.455]), Capacity = 400,
            Description = "Mixes two materials into one: a chute for each. Needs power.",
            Refine = ("foundry", 200, B([-0.375, 0.725, -0.395], [-0.055, 0.88, -0.155])),
            Gated = new() { [1] = "Bronze needs tier 2 and circuits tier 3.", [2] = "Circuits need tier 3." },
        };
        Line Assembler(string file, string name, string description, string? machine, List<PyDict> sockets) => new()
        {
            File = file, Name = name, Section = "Assemblers", Mass = 150, Health = 500, Armour = 6, Power = 1500, Attach = 6.0,
            Sockets = sockets,
            Ports = ChutePorts(L(0.5, 0.25, -0.3), "+x", L(0.5, 0.25, 0.3), "+x"),
            Slots = [(B([-0.455, 0.035, 0.015], [-0.01, 0.56, 0.48]), 12, "tray"), (B([0.01, 0.035, 0.015], [0.455, 0.56, 0.48]), 12, "tray")],
            Description = description,
            Assemble = (200, B([-0.435, 0.035, -0.435], [0.435, 0.32, -0.065]), [0, 0.03, 0.5], "+z", machine),
            Config = [new PyDict { ["key"] = "recipe", ["label"] = "Recipe", ["group"] = "Production", ["tooltip"] = "What it builds." }],
        };
        var asmSockets = BigSockets(0.25, -0.25);
        // Assemblers of a family (AssemblingTable.Machines): the same machine, with their own recipes.
        return
        [
            drill, saw, sawmill, smelter, foundry,
            Assembler("Assembling/Assembler", "Assembler", "Builds a recipe set with the configure tool. Needs power.", null, asmSockets),
            Assembler("Assembling/ThrusterAssembler", "Thruster Assembler", "Builds jets, rockets, boosters and repulsors from their components: set a recipe with the configure tool. Needs power.", "thruster", asmSockets),
            Assembler("Assembling/Tailor", "Tailor", "Sews lift cells from cloth: set a recipe with the configure tool. Needs power.", "tailor", asmSockets),
        ];
    }

    /// <summary>A number as the files hold it: 4 decimals, an int when whole.</summary>
    private static object Num(double v)
    {
        v = Py.Round(v + 0.0, 4);
        return v == Math.Floor(v) && Math.Abs(v) < 1e15 ? (object)(long)v : v;
    }

    private static List<object?> Vec(IEnumerable<double> v) => [.. v.Select(c => (object?)Num(c))];

    private static double[] D3(object? v) => [.. ((List<object?>)v!).Select(Py.D)];

    /// <summary>A point at a tier: x and z grow; y grows above the floor, and keeps tier 1's value below it (a bit's reach).</summary>
    private static double[] Scaled(double[] v, double size) => [v[0] * size, v[1] > 0 ? v[1] * size : v[1], v[2] * size];

    private static double OnGrid(double v) => (int)(v / Grid + (v >= 0 ? 0.5 : -0.5)) * Grid;

    /// <summary>The next grid line out from a face.</summary>
    private static double OutToGrid(double v)
    {
        var n = Math.Abs(v) / Grid;
        var whole = (int)n;
        return (n - whole < 1e-6 ? whole : whole + 1) * Grid * (v >= 0 ? 1 : -1);
    }

    private static double SizeOf(Line line, int tier) => BigWidth.TryGetValue(line.File, out var w) ? w[tier] : Size[tier];

    /// <summary>The sockets at a tier, on the grid, and the solid pads that hold any the body doesn't reach.</summary>
    private static (List<PyDict> Sockets, List<TB> Pads) ScaledSockets(Line line, int tier)
    {
        var size = SizeOf(line, tier);
        if (size == 1.0)
            return (line.Sockets, []);
        var o = new List<PyDict>();
        var pads = new List<TB>();
        foreach (var sk in line.Sockets)
        {
            var at = Scaled(D3(sk["at"]), size);
            var dir = (string)sk["dir"]!;
            var axis = "xyz".IndexOf(dir[1]);
            if ((string)sk["name"]! == "bottom")
            {
                o.Add(new PyDict(sk) { ["at"] = Vec(at) });
                continue;
            }
            var face = at[axis];
            var snapped = at.Select(OnGrid).ToArray();
            snapped[axis] = OutToGrid(face);
            o.Add(new PyDict(sk) { ["at"] = Vec(snapped) });
            if (Math.Abs(snapped[axis] - face) > 1e-6)
            {
                double[] lo = [.. snapped.Select(c => c - 0.15)], hi = [.. snapped.Select(c => c + 0.15)];
                if (dir[0] == '+')
                {
                    lo[axis] = face - 0.03;
                    hi[axis] = snapped[axis];
                }
                else
                {
                    lo[axis] = snapped[axis];
                    hi[axis] = face + 0.03;
                }
                pads.Add(new TB { Min = lo, Max = hi, Role = "frame" });
            }
        }
        return (o, pads);
    }

    private static PyDict BuiltBox(int tier, TB b, int index, bool detail = false)
    {
        var o = new PyDict { ["min"] = Vec(b.Min), ["max"] = Vec(b.Max) };
        var pal = TierStyle.Palette[tier];
        var colour = pal[b.Role];
        if (tier == 1 && !detail)
            colour = TierStyle.ScrapTone(colour, index);
        if (colour != pal["body"])
            o["color"] = colour;
        if (detail)
            o["detail"] = true;
        if (TierStyle.Glows.TryGetValue(tier, out var glows) && glows.Contains(b.Role))
        {
            o["glow"] = true;
            o["paintable"] = false;
        }
        foreach (var k in TierStyle.ShapeKeys)
            if (b.Keys.Has(k))
                o[k] = b.Keys[k];
        return o;
    }

    /// <summary>A gathering module's status lamp: a bar on top of the highest box along its back (a hopper's or a bin's back wall), most of
    /// its width and 6 cm high at tier 1 (scaled with the module), read from any side and from above where a module works on an arm.</summary>
    private static PyDict ModuleLamp(List<PyDict> boxes, double size)
    {
        var solid = boxes.Where(b => !b.Has("detail")).Select(b => (Min: D3(b["min"]), Max: D3(b["max"]))).ToList();
        var back = solid.Min(b => b.Min[2]);
        var depth = solid.Max(b => b.Max[2]) - back;
        var rim = solid.Where(b => b.Min[2] <= back + 0.15 * depth).OrderByDescending(b => b.Max[1]).First();
        var w = (rim.Max[0] - rim.Min[0]) * 0.35;
        var mx = (rim.Min[0] + rim.Max[0]) / 2;
        var z0 = rim.Min[2] + 0.003;
        var z1 = Math.Min(rim.Max[2] - 0.003, z0 + 0.06 * size);
        var y = rim.Max[1] + 0.002;
        return new PyDict
        {
            ["min"] = Vec([mx - w, y, z0]), ["max"] = Vec([mx + w, y + 0.06 * size, z1]),
            ["color"] = "#2b3036", ["detail"] = true, ["paintable"] = false, ["status"] = true,
        };
    }

    private static PyDict Fill(Bin b, double size) => new() { ["min"] = Vec(Scaled(b.Min, size)), ["max"] = Vec(Scaled(b.Max, size)) };

    private static PyDict Part(Line line, int tier)
    {
        var (rate, power, cap, mass, health, armour, attach) = Curve[tier];
        var size = SizeOf(line, tier);
        var big = BigWidth.ContainsKey(line.File);
        var (sockets, pads) = ScaledSockets(line, tier);
        if (big)
            mass = health = size * size;

        TB Grown(TB b) => b.With(Scaled(b.Min, size), Scaled(b.Max, size));

        var shape = TierStyle.Look(line.Name, tier);
        var boxes = new List<PyDict>();
        var drawn = new List<PyDict>();
        for (var i = 0; i < shape.Solid.Count; i++)
        {
            var b = shape.Solid[i];
            var solid = BuiltBox(tier, Grown(b), i);
            if (b.Look is not null) // the box stays, hidden, as the collider: the shape is drawn over it
            {
                var keys = new PyDict(b.Keys).Update(b.Look);
                drawn.Add(BuiltBox(tier, Grown(new TB { Min = b.Min, Max = b.Max, Role = b.Role, Keys = keys }), i, true));
            }
            if (b.Look is not null || b.Hidden)
                solid["hidden"] = true;
            boxes.Add(solid);
        }
        boxes.AddRange(pads.Select(b => BuiltBox(tier, b, 0)));
        boxes.AddRange(drawn);
        boxes.AddRange(shape.Detail.Select(b => BuiltBox(tier, Grown(b), 0, true)));
        if (!big && line.Gather is not null)
            boxes.Add(ModuleLamp(boxes, size)); // (a gathering module has no screen: its status lamp stands on its back rim, ProductionLines)
        PyDict? panel = null;
        if (big) //(a building has a screen; a gathering module's state is its look line, and it works out of sight on an arm)
        {
            var keepClear = sockets.Select(s => (D3(s["at"]), (string)s["dir"]!)).ToList();
            keepClear.AddRange((line.Ports ?? []).Select(p => (Scaled(D3(p["at"]), size), (string)p["face"]!)));
            if (line.Assemble is { } asm)
                keepClear.Add((Scaled(asm.OutAt, size), asm.OutDir));
            // (a refiner or an Assembler is a machine of a production line: a status lamp over its screen; an Assembler takes work orders, so
            // its queue goes on eight rows with the buttons under them: WorkOrders.cs)
            var (plate, pnl, lamp) = line.Assemble is not null
                ? PartScreens.Mount(boxes, keepClear, rows: 8, lamp: true)
                : PartScreens.Mount(boxes, keepClear, lamp: line.Refine is not null);
            if (line.Assemble is not null)
                pnl["touch"] = ExtrusionParts.OrderButtons();
            panel = pnl;
            PartScreens.Fit(boxes, plate);
            if (lamp is not null)
                boxes.Add(lamp);
        }
        PartZFight.Resolve(boxes, sockets);
        boxes = [.. boxes.Select(b => new PyDict(b) { ["min"] = Vec(D3(b["min"])), ["max"] = Vec(D3(b["max"])) })];
        if (!big && line.Gather is not null) // (a gathering module shows its work too: the bit's flights and the blades turn while it gathers)
        {
            PyDict? BuiltOf(TB t, bool detail)
            {
                double[] mn = Scaled(t.Min, size), mx = Scaled(t.Max, size);
                return boxes.FirstOrDefault(b => Py.Truthy(b.Get("detail")) == detail && !Py.Truthy(b.Get("status")) && Enumerable.Range(0, 3).All(a => Math.Abs(D3(b["min"])[a] - mn[a]) < 0.03 && Math.Abs(D3(b["max"])[a] - mx[a]) < 0.03));
            }
            void Turning(double[] mn, double[] mx, string color)
            {
                if (MachineLook.Work(MachineLook.D(mn, mx, color, color == MachineLook.Steel ? "steel" : null), "spin", "+y") is { } b)
                    boxes.Add(b);
            }
            if (line.Gather.Value.Kind == "drill" && shape.Solid.FirstOrDefault(b => b.Min[1] <= -1.19) is { } bitBox && BuiltOf(bitBox, false) is { } bit)
            {
                double[] lo = D3(bit["min"]), hi = D3(bit["max"]);
                double cx = (lo[0] + hi[0]) / 2, cz = (lo[2] + hi[2]) / 2, reach = (hi[0] - lo[0]) * 0.9, thin = Math.Max(0.012, (hi[0] - lo[0]) * 0.1);
                var k = 0;
                foreach (var f in new[] { 0.1, 0.36, 0.62, 0.84 })
                {
                    var y = lo[1] + (hi[1] - lo[1]) * f;
                    if (k++ % 2 == 0)
                        Turning([cx - reach, y, cz - thin], [cx + reach, y + 0.035, cz + thin], MachineLook.Steel);
                    else
                        Turning([cx - thin, y, cz - reach], [cx + thin, y + 0.035, cz + reach], MachineLook.Steel);
                }
            }
            if (line.Gather.Value.Kind == "saw")
                foreach (var disc in shape.Detail.Where(b => (string?)b.Keys.Get("shape") is "cylinder" or "tube" && b.Min[2] > 0.9 && b.Max[1] - b.Min[1] < 0.06))
                    if (BuiltOf(disc, true) is { } blade)
                    {
                        double[] lo = D3(blade["min"]), hi = D3(blade["max"]);
                        blade["work"] = "spin";
                        blade["axis"] = "+y";
                        double cx = (lo[0] + hi[0]) / 2, cz = (lo[2] + hi[2]) / 2, r = (hi[0] - lo[0]) * 0.45;
                        Turning([cx - r, hi[1] - 0.004, cz - r * 0.14], [cx + r, hi[1] + 0.012, cz + r * 0.14], MachineLook.Ink); // (a mark across it, so the turn shows)
                        if ((string?)disc.Keys.Get("shape") == "cylinder")
                            Turning([cx - r * 0.24, hi[1] + 0.0125, cz - r * 0.24], [cx + r * 0.24, hi[1] + 0.027, cz + r * 0.24], MachineLook.Accent); // (a hub cap)
                    }
            ModuleLook.Dress(boxes, line.Gather.Value.Kind == "drill", tier, size); // (the machine look over the settled module: look boxes only)
            boxes = [.. boxes.Select(b => new PyDict(b) { ["min"] = Vec(D3(b["min"])), ["max"] = Vec(D3(b["max"])) })];
        }
        if (big) // (the machine look, over the settled body: look boxes only, so nothing above moves)
        {
            var rooms = new List<(double[], double[])>();
            (double[], double[])? input = null;
            void Room(Bin b) => rooms.Add((Scaled(b.Min, size), Scaled(b.Max, size)));
            if (line.Bulk is not null)
                Room(line.Bulk);
            foreach (var sl in line.Slots ?? [])
                Room(sl.Box);
            if (line.Assemble is { } asmRoom)
                Room(asmRoom.Box);
            if (line.Refine is { } rf)
            {
                var rb = shape.Hopper is { } hp ? B([rf.Box.Min[0], hp.Y0, rf.Box.Min[2]], [rf.Box.Max[0], hp.Y1, rf.Box.Max[2]]) : rf.Box;
                Room(rb);
                input = rooms[^1];
            }
            var site = new TierMachine.Site
            {
                Name = line.Name, Tier = tier, Size = size, Shape = shape, Boxes = boxes, Face = (string)panel!["face"]!,
                Plate = boxes.First(b => (string?)b.Get("color") == "#14181c" && Py.Truthy(b.Get("detail"))), Lamp = boxes.FirstOrDefault(b => Py.Truthy(b.Get("status"))),
                Ports = [.. (line.Ports ?? []).Select(p => (Scaled(D3(p["at"]), size), (string)p["face"]!, (string)p["dir"]! == "in"))],
                Sockets = [.. sockets.Where(k => (string)k["name"]! != "bottom").Select(k => (D3(k["at"]), (string)k["dir"]!))],
                Rooms = rooms, Input = input,
            };
            TierMachine.Dress(site);
            boxes = [.. boxes.Select(b => new PyDict(b) { ["min"] = Vec(D3(b["min"])), ["max"] = Vec(D3(b["max"])) })];
            if (Environment.GetEnvironmentVariable("COS_LOOK_DEBUG") is not null)
                Console.Error.WriteLine($"[look] {Prefix[tier]}{line.Name}: {boxes.Count(b => Py.Truthy(b.Get("detail")))} look boxes, {site.Left} left out");
        }
        var head = $"{Tiers[tier]}, tier {tier} of 5. ";
        if (line.Gated is { } gated && tier < 3)
            head += gated[tier] + " ";
        var o = new PyDict
        {
            ["format"] = Format,
            ["name"] = Prefix[tier] + line.Name,
            ["description"] = head + line.Description.Replace("the Sawmill", "the mill").Replace("the Smelter", "the furnace"),
            ["group"] = Group,
            ["section"] = line.Section,
            ["tier"] = tier,
            ["role"] = "storage",
            ["tool"] = "welder",
            ["attachSeconds"] = Num(line.Attach * attach),
            ["mass"] = Num(line.Mass * mass),
            ["health"] = Num(line.Health * health),
            ["armour"] = Num(line.Armour * armour),
            ["material"] = "steel",
            ["power"] = Num(line.Power * power),
            ["collider"] = "boxes", // (the details are looks: a wedge or a cylinder among them must never become a hull)
            ["color"] = TierStyle.Palette[tier]["body"],
            ["boxes"] = boxes.Cast<object?>().ToList(),
            ["sockets"] = sockets.Cast<object?>().ToList(),
        };
        if (line.Ports is not null)
            o["ports"] = line.Ports.Select(p => (object?)new PyDict(p) { ["at"] = Vec(Scaled(D3(p["at"]), size)) }).ToList();
        if (panel is not null)
        {
            var config = new List<object?>(line.Config ?? []);
            if (line.Refine is not null)
                config.Add(Recipe);
            if (line.Assemble is not null)
                config.Add(AsmOrderCount);
            config.Add(Ink);
            o["panels"] = new List<object?> { panel };
            o["config"] = config;
        }
        else if (line.Config is not null)
            o["config"] = line.Config;

        if (line.Assemble is { } a)
        {
            var slots = line.Slots!.Select(s => (object?)new PyDict { ["min"] = Fill(s.Box, size)["min"], ["max"] = Fill(s.Box, size)["max"], ["stack"] = s.Stack, ["label"] = s.Label }).ToList();
            var block = new PyDict { ["capacity"] = Num(a.Capacity * cap), ["min"] = Fill(a.Box, size)["min"], ["max"] = Fill(a.Box, size)["max"] };
            if (tier > 1)
            {
                block["speed"] = rate;
                block["chuteLoad"] = 10 * rate;
            }
            block.Update(("batches", 4 * tier), ("outAt", Vec(Scaled(a.OutAt, size))), ("outDir", a.OutDir));
            if (a.Machine is not null)
                block["machine"] = a.Machine;
            o["storage"] = new PyDict { ["slots"] = slots, ["assemble"] = block };
            return o;
        }
        var bulk = new PyDict { ["material"] = "any", ["capacity"] = Num(line.Capacity * cap) };
        if (tier > 1)
            bulk["chuteLoad"] = 10 * rate;
        var storage = new PyDict { ["bulk"] = new PyDict(bulk).Update(Fill(line.Bulk!, size)) };
        if (line.Gather is { } g)
        {
            var gather = new PyDict { ["kind"] = g.Kind, ["rate"] = g.Rate * rate }.Update(Fill(g.Box, size));
            if (line.Name == "Drill Module")
            {
                gather["ratedRpm"] = DrillShaft[tier].Rpm;
                gather["shaftTorque"] = DrillShaft[tier].Torque;
            }
            storage["gather"] = gather;
        }
        else
        {
            var f = line.Refine!.Value;
            var box = f.Box;
            if (shape.Hopper is { } hop) // (the body moved it: the same room, higher or lower)
                box = B([box.Min[0], hop.Y0, box.Min[2]], [box.Max[0], hop.Y1, box.Max[2]]);
            var refine = new PyDict { ["machine"] = f.Machine, ["capacity"] = Num(f.Capacity * cap) };
            if (tier > 1)
                refine["speed"] = rate;
            refine.Update(Fill(box, size));
            storage["refine"] = refine;
        }
        o["storage"] = storage;
        return o;
    }

    /// <summary>machine_tiers' layout: one key a line, one box, socket, port and config entry a line, one storage block a line; each value
    /// as json.dumps(v) (one line, ensure_ascii).</summary>
    public static string Layout(PyDict p)
    {
        var lines = new List<string>();
        foreach (var (k, v) in p)
        {
            if (k is "boxes" or "sockets" or "ports" or "config")
                lines.Add($"  \"{k}\": [\n" + string.Join(",\n", ((List<object?>)v!).Select(i => "    " + PyJson.Dumps(i))) + "\n  ]");
            else if (k == "storage")
                lines.Add("  \"storage\": {\n" + string.Join(",\n", ((PyDict)v!).Select(kv => $"    \"{kv.Key}\": {PyJson.Dumps(kv.Value)}")) + "\n  }");
            else
                lines.Add($"  \"{k}\": {PyJson.Dumps(v)}");
        }
        return "{\n" + string.Join(",\n", lines) + "\n}\n";
    }

    private static string PathOf(Line line, int tier) => $"Constructs/Parts/{line.File}{(tier > 1 ? $"T{tier}" : "")}.json";

    public void Make(PartOutput o)
    {
        foreach (var line in Lines())
            for (var tier = 1; tier <= 5; tier++)
            {
                var p = Part(line, tier);
                var boxes = ((List<object?>)p["boxes"]!).Cast<PyDict>().ToList();
                foreach (var b in boxes)
                {
                    double[] mn = D3(b["min"]), mx = D3(b["max"]);
                    if (Math.Min(mx[0] - mn[0], Math.Min(mx[1] - mn[1], mx[2] - mn[2])) < 0.0105)
                        throw new InvalidOperationException($"machine_tiers: a box thinner than 1 cm in {p["name"]}");
                }
                if (boxes.Count > 150) // (the game takes 256 since parts are merged meshes; a top tier.s look budget is about 80)
                    throw new InvalidOperationException($"machine_tiers: too many boxes: {p["name"]}");
                o.File(PathOf(line, tier), Layout(p));
            }
    }
}
