using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The hydraulics' parts (Constructs/Parts/Hydraulics: docs/sandbox/CONSTRUCTS.md, "Hydraulics"), and the cable trays and clamps. Was
/// tools/constructs/hydraulics_parts.py.
///
/// A pump (a shaft, or power with none linked) feeds a circuit: pipe links that carry oil, through a valve, to a cylinder's head. A valve
/// is a logic part whose signal, -1 to 1, is how far open it is and which way: a Lever Valve is the Analog Lever's handle, sprung to the
/// middle, and a Digital Valve Block takes a data signal. A head is the part a cylinder's tool leaves at its first point ("toolOnly"): it
/// carries the ports, and the attach tool's Cylinder mode names it by its size. Force is the pump's pressure over the head's area, speed
/// the pump's flow over the area. Recipes come from the part kit's assembling (--gen-parts assembling).
/// </summary>
public sealed class HydraulicsParts : IPartGenerator
{
    public string Name => "hydraulics_parts";

    private const string Out = "Constructs/Parts/Hydraulics";
    private const string Section = "Hydraulics";
    private const string Body = "#3f4a55", Dark = "#23272c", Oil = "#b5531c", Brass = "#c8a13a", Steel = "#a9b0b8", Screen = "#10181c";
    private const double Pitch = 0.14; // lanes 14 cm apart at the small class, wide enough for a hose (10 cm) and the router's clearance
    private const string TraySection = "Hoses and leads";

    // (class, bar, litres a second at rated rpm (int or float), rated rpm, power W with no shaft, kg, health)
    private static readonly (Cls, int, object, int, int, int, int)[] Pumps =
    [
        (Small, 120, 1.5, 1800, 4000, 60, 300),
        (Medium, 180, 12, 1800, 40000, 700, 1500),
        (Large, 250, 120, 1500, 400000, 9000, 9000),
        (Capital, 350, 1400, 1200, 1000000, 120000, 60000),
    ];

    private static readonly (Cls, int, int)[] Valves = [(Small, 8, 60), (Medium, 90, 300), (Large, 900, 1800), (Capital, 9000, 12000)];

    // (size name, half width of the head, kg, health, bore cm2, top speed m/s, electro force N, electro speed m/s, electro W)
    private static readonly (string, double, int, int, int, double, int, double, int)[] Heads =
    [
        ("S", 0.07, 6, 120, 25, 1.0, 5_000, 0.2, 300),
        ("M", 0.13, 30, 300, 150, 1.0, 40_000, 0.15, 2_000),
        ("L", 0.24, 200, 1200, 1400, 0.8, 400_000, 0.12, 20_000),
        ("XL", 0.42, 1500, 6000, 14000, 0.6, 4_000_000, 0.1, 200_000),
    ];

    // (class, length m, lanes, kg, health)
    private static readonly (Cls, int, int, int, int)[] Trays =
    [
        (Small, 1, 3, 4, 200), (Small, 2, 3, 8, 400), (Small, 1, 6, 8, 300), (Small, 2, 6, 16, 600),
        (Medium, 3, 3, 60, 1200), (Medium, 6, 3, 120, 2400),
        (Large, 12, 3, 800, 8000),
        (Capital, 24, 3, 8000, 40000),
    ];

    // (class, lanes, kg, health)
    private static readonly (Cls, int, double, int)[] Clamps = [(Small, 1, 0.5, 60), (Small, 2, 1, 80), (Small, 3, 1.5, 100), (Medium, 3, 10, 400), (Large, 3, 80, 2500), (Capital, 3, 800, 15000)];

    private static PyDict Port(string name, string direction, object?[] at, string face) =>
        Kw(("type", "pipe"), ("fluid", "oil"), ("name", name), ("dir", direction), ("at", at.ToList()), ("face", face));

    private static (double, double) Sorted(double a, double b) => a <= b ? (a, b) : (b, a);

    private static (string, Part) Pump(Cls cls, int bar, object flow, int rpm, int watts, int mass, int health)
    {
        var name = $"{cls.Name} Hydraulic Pump".Trim();
        var p = new Part(name, cls, Section, $"Turned by a shaft, or run on power with none linked. {bar} bar, {Py.G(Py.D(flow))} L/s. Its oil goes through valves to cylinders.", color: Body, group: "Joints and Motion");
        var h = cls.Cell; // (2 cells a side)
        p.Add(Box([-h, 0, -h], [h, 2 * h, h]));
        p.Add(Box([-0.7 * h, 2 * h, -0.7 * h], [0.7 * h, 2.3 * h, 0.7 * h], Dark, detail: true));
        p.Add(Box([h, 0.7 * h, -0.3 * h], [1.12 * h, 1.3 * h, 0.3 * h], Oil, detail: true));
        p.Add(Box([-0.4 * h, 0.6 * h, h], [0.4 * h, 1.4 * h, 1.12 * h], Brass, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("drive", [0, h, -h], "-z");
        p.Sock("+x", [h, h, 0], "+x");
        p.Sock("-x", [-h, h, 0], "-x");
        p.Stats(mass, health, 6);
        p.Set(("power", watts), ("ports", Py.L(
                Port("Oil out", "out", [h, h, 0], "+x"),
                Kw(("name", "Drive in"), ("type", "shaft"), ("dir", "in"), ("socket", "drive"), ("at", Py.L(0, h, -h)), ("face", "-z")))),
            ("hydraulics", Kw(("kind", "pump"), ("bar", bar), ("flow", flow), ("ratedRpm", rpm))));
        return ("", p);
    }

    private static List<object?> ValvePorts(double h) => Py.L(Port("Oil in", "in", [0, 0, -h], "-z"), Port("Oil out", "out", [0, 0, h], "+z"));

    private static (string, Part) LeverValve(Cls cls, int mass, int health)
    {
        var s = cls.Cell / Small.Cell;
        var name = $"{cls.Name} Lever Valve".Trim();
        var (_, p) = ControlParts.Lever(name, cls, s, mass, health, 2, cls == Small ? "wrench" : "welder");
        p.D["description"] = "A spool on a handle: push it, and how far is how fast. Oil in from a pump, out to a cylinder. Stack them for a lever bank.";
        p.D["section"] = Section;
        p.D["group"] = "Joints and Motion";
        var config = ControlParts.Config();
        foreach (var e in config.Cast<PyDict>())
            e["default"] = (string)e["key"]! switch
            {
                "value" => 0.5, "low" => -1, "high" => 1, "spring" => true, "rest" => 0.5, _ => e["default"],
            };
        p.Set(("config", config), ("ports", ValvePorts(0.1 * s)), ("hydraulics", Kw(("kind", "valve"))));
        return ("", p);
    }

    private static (string, Part) DigitalValve(Cls cls, int mass, int health)
    {
        var s = cls.Cell / Small.Cell;
        var h = 0.1 * s;
        var name = $"{cls.Name} Digital Valve Block".Trim();
        var p = new Part(name, cls, Section, "A spool on a solenoid: a signal of -1 to 1 opens it, each way. Oil in from a pump, out to a cylinder.", role: "logic",
            tool: cls == Small ? "wrench" : "welder", color: Body, group: "Joints and Motion");
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-0.6 * h, h - 0.01 * s, -0.6 * h], [0.6 * h, h + 0.03 * s, 0.6 * h], Screen, detail: true));
        p.Add(Box([-0.3 * h, h + 0.03 * s - 0.004 * s, -0.3 * h], [0.3 * h, h + 0.045 * s, 0.3 * h], Oil, detail: true));
        foreach (var (n, at) in new (string, double[])[] { ("+x", [h, 0, 0]), ("-x", [-h, 0, 0]), ("-y", [0, -h, 0]) })
            p.Sock(n, at, n);
        p.Stats(mass, health, 2, cls == Small ? 1.0 : null);
        p.Set(("logic", Kw(("kind", "scale"), ("settings", Py.L(1, -1, 0.5)), ("labels", Py.L("x 1", "x -1", "x 0.5")), ("setting", 0))),
            ("config", Py.L(
                Kw(("key", "factor"), ("label", "Multiply by"), ("group", "Valve"), ("min", -10), ("max", 10), ("step", 0.01), ("tooltip", "The signal is multiplied by this: -1 turns the cylinder's way round.")),
                Kw(("key", "offset"), ("label", "Then add"), ("group", "Valve"), ("min", -1), ("max", 1), ("step", 0.01), ("default", 0), ("tooltip", "Added after the multiply.")),
                Kw(("key", "low"), ("label", "Most in"), ("group", "Valve"), ("min", -1), ("max", 1), ("step", 0.01), ("default", -1), ("tooltip", "How far it opens one way.")),
                Kw(("key", "high"), ("label", "Most out"), ("group", "Valve"), ("min", -1), ("max", 1), ("step", 0.01), ("default", 1), ("tooltip", "How far it opens the other way.")))),
            ("ports", ValvePorts(h)), ("hydraulics", Kw(("kind", "valve"))));
        return ("", p);
    }

    private static (string, Part) Head(double h, int mass, int health, string kind, PyDict keys, string name, int? power = null)
    {
        var p = new Part(name, Small, "Heads", "What a cylinder pivots on. The attach tool's Cylinder mode leaves it at the first point.", role: kind == "electro" ? "logic" : "frame",
            tool: "wrench", color: Dark, group: "Joints and Motion");
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-0.5 * h, h, -0.5 * h], [0.5 * h, 1.3 * h, 0.5 * h], kind != "electro" ? Oil : Brass, detail: true));
        p.Sock("+y", [0, h, 0], "+y");
        p.Stats(mass, health, 2, 1.0);
        p.Set(("toolOnly", true), ("hydraulics", new PyDict(keys) { ["kind"] = kind }), ("collider", "boxes"));
        if (kind == "electro")
            p.Set(("power", power), ("logic", Kw(("kind", "scale"), ("settings", Py.L(1)), ("labels", Py.L("x 1")), ("setting", 0))),
                ("config", Py.L(Kw(("key", "factor"), ("label", "Set"), ("group", "Signal"), ("min", -10), ("max", 10), ("step", 0.01), ("tooltip", "The Set signal, 0 to 1 of its stroke, is multiplied by this.")))));
        else
            p.Set(("ports", Py.L(Port("Oil in", "in", [h, 0, 0], "+x"))));
        return ("", p);
    }

    private static string SizeWord(Cls c) => c.Key switch { "small" => "", "medium" => "Medium ", "large" => "Large ", _ => "Capital " };

    /// <summary>A Cable Tray: a plate and two low walls, open at the ends and the top. Links snap into its lanes, and it guards them.</summary>
    private static (string, Part) Tray(Cls cls, int length, int lanes, int mass, int health)
    {
        var name = $"{SizeWord(cls)}Cable Tray {length} m x{lanes}";
        var p = new Part(name, cls, TraySection, $"A node set on it snaps to one of its {lanes} lanes, the auto-router prefers them, and a link in it takes a fifth of the damage.", color: Dark, group: "Storage and Logistics");
        var s = cls.Cell / 0.25;
        var pitch = Pitch * s;
        var half = pitch * lanes / 2;
        var end = length / 2.0;
        double plate = 0.02 * s, wall = 0.09 * s, lane = 0.09 * s;
        p.Add(Box([-half - 0.02 * s, 0, -end], [half + 0.02 * s, plate, end]));
        foreach (var x in new[] { -1, 1 })
        {
            var (lo, hi) = Sorted(x * half, x * (half + 0.02 * s + 0.004 * s));
            p.Add(Box([lo, 0.002 * s, -end + 0.004 * s], [hi, wall, end - 0.004 * s], Steel, detail: true));
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+z", [0, 0.01 * s, end], "+z");
        p.Sock("-z", [0, 0.01 * s, -end], "-z");
        p.Stats(mass, health, 2, s <= 1 ? 1.0 : null);
        p.Set(("collider", "boxes"), ("tray", Kw(("at", Py.L(0, lane, 0)), ("axis", Py.L(0, 0, 1)), ("across", Py.L(1, 0, 0)), ("lanes", lanes), ("pitch", pitch), ("length", length), ("guard", length))));
        return ("", p);
    }

    /// <summary>A Cable Clamp: a saddle and a strap over one, two or three lanes. Short, but it guards a stretch of the run either side.</summary>
    private static (string, Part) Clamp(Cls cls, int lanes, double mass, int health)
    {
        var name = $"{SizeWord(cls)}Cable Clamp x{lanes}";
        var s = cls.Cell / 0.25;
        var p = new Part(name, cls, TraySection, "A link set on it snaps to its lane and is held there: the metre of run through it takes a fifth of the damage.", color: Steel, group: "Storage and Logistics");
        var pitch = Pitch * s;
        var half = pitch * lanes / 2;
        var depth = 0.05 * s;
        var lane = 0.09 * s;
        p.Add(Box([-half - 0.02 * s, 0, -depth], [half + 0.02 * s, 0.02 * s, depth], Dark));
        foreach (var x in new[] { -1, 1 })
        {
            var (lo, hi) = Sorted(x * half, x * (half + 0.018 * s));
            p.Add(Box([lo, 0.002 * s, -depth + 0.004 * s], [hi, 0.17 * s, depth - 0.004 * s], Steel, detail: true));
        }
        p.Add(Box([-half - 0.024 * s, 0.17 * s, -depth + 0.008 * s], [half + 0.024 * s, 0.19 * s, depth - 0.008 * s], Steel, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(mass, health, 2, s <= 1 ? 1.0 : null);
        p.Set(("collider", "boxes"), ("tray", Kw(("at", Py.L(0, lane, 0)), ("axis", Py.L(0, 0, 1)), ("across", Py.L(1, 0, 0)), ("lanes", lanes), ("pitch", pitch), ("length", 2 * depth), ("guard", 1.0 * s))));
        return ("", p);
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var (cls, length, lanes, mass, health) in Trays)
            parts.Add(Tray(cls, length, lanes, mass, health));
        foreach (var (cls, lanes, mass, health) in Clamps)
            parts.Add(Clamp(cls, lanes, mass, health));
        foreach (var (cls, bar, flow, rpm, watts, mass, health) in Pumps)
            parts.Add(Pump(cls, bar, flow, rpm, watts, mass, health));
        foreach (var (cls, mass, health) in Valves)
        {
            parts.Add(LeverValve(cls, mass, health));
            parts.Add(DigitalValve(cls, mass, health));
        }
        foreach (var (size, h, mass, health, bore, speed, force, slow, watts) in Heads)
        {
            parts.Add(Head(h, mass, health, "cylinder", Kw(("area", bore), ("speed", speed)), $"Hydraulic Cylinder Head {size}"));
            parts.Add(Head(h, mass, health, "electro", Kw(("force", force), ("speed", slow)), $"Electric Cylinder Head {size}", watts));
        }
        WriteParts(o, Out, parts);
    }
}
