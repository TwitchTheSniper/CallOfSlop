using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The flying parts: Constructs/Parts/Wings, Constructs/Parts/Rotors and the demo aircraft (Light Plane, Small Helicopter, Fan Quad).
/// docs/sandbox/CONSTRUCTS.md, "Wings" and "Propellers and rotors". Was tools/constructs/wing_parts.py.
///
/// A wing is a frame with a "surface" block, a control surface one with a "control" inside it, a rotor a part of role "rotor": what they
/// do in the air is in their numbers, so a new one is a row here. Each family comes in the size classes. Run the part kit's assembling (--gen-parts assembling)
/// after it for their recipes.
/// </summary>
public sealed class WingParts : IPartGenerator
{
    public string Name => "wing_parts";

    private const string Wings = "Constructs/Parts/Wings", Rotors = "Constructs/Parts/Rotors", Blueprints = "Constructs/Blueprints";
    private const string Skin = "#b8bec6", Rib = "#7c838c", Cloth = "#d9cfae", Dark = "#30343a";

    // (name, chord m, span m, kg/m2 at small, cl0, aspect of the wing it makes, stall deg); chord and span keep their Python type.
    private static readonly (string Name, object Chord, object Span, int KgM2, double Cl0, int Aspect, int Stall)[] Segments =
    [
        ("Wing Segment", 1.5, 2, 9, 0.25, 7, 15),
        ("Short Wing Segment", 1.5, 1, 9, 0.25, 7, 15),
        ("Narrow Wing Segment", 1, 2, 8, 0.25, 8, 15),
        ("Tail Plane", 1, 2, 8, 0.0, 4, 14),
    ];

    private static string NameOf(Cls c, string b) => c.Name.Length > 0 ? $"{c.Name} {b}" : b;

    private static string? GroupOf(Cls c) => c == Small ? "Small" : null;

    private static double Pw(double a, double b) => Math.Pow(a, b);

    /// <summary>A wing's section along x: a solid slab, a rounded nose and a thin trailing edge on it.</summary>
    private static void WingBody(Part p, double x, double z, double thick, string? colour = null)
    {
        p.Add(Box([-x, -thick / 2, -z], [x, thick / 2, z], hidden: true));
        p.Add(Box([-x, -thick * 0.46, -z * 0.6], [x, thick * 0.46, z * 0.5], colour, detail: true));
        p.Add(Box([-x + 0.01, -thick * 0.38, -z], [x - 0.01, thick * 0.38, -z * 0.55], colour, detail: true, shape: Kw(("shape", "rounded"), ("radius", 0.5), ("edges", "long"))));
        p.Add(Box([-x + 0.01, -thick * 0.3, z * 0.45], [x - 0.01, thick * 0.3, z], colour ?? Rib, detail: true, shape: Kw(("shape", "wedge"), ("taper", Py.L(1, 0.1)), ("front", "+z"))));
    }

    /// <summary>A product of Python numbers: an int while every factor is.</summary>
    private static object Mul(params object[] f) =>
        f.All(v => v is int) ? (object)f.Aggregate(1, (a, v) => a * (int)v) : f.Aggregate(1.0, (a, v) => a * Py.D(v));

    private static (string, Part) Segment(Cls c, string b, object chordO, object spanO, int kgm2, double cl0, int aspect, int stall)
    {
        double chord = Py.D(chordO), span = Py.D(spanO);
        double x = span * c.X / 2, z = chord * c.X / 2;
        var areaO = Mul(chordO, spanO, c.X, c.X);
        var area = Py.D(areaO);
        var p = new Part(NameOf(c, b), c, "Wings", $"{Py.G(area)} m2 of wing, {Py.G(span * c.X)} m of span. Join them along their span; a wing's lift is the sum of its segments, each at its own angle.",
            tool: "welder", color: Skin, group: GroupOf(c));
        var thick = 0.12 * c.X;
        WingBody(p, x, z, thick);
        p.Sock("+x", [x, 0, 0], "+x");
        p.Sock("-x", [-x, 0, 0], "-x");
        p.Sock("+y", [0, thick / 2, 0], "+y");
        p.Sock("-y", [0, -thick / 2, 0], "-y");
        p.Sock("back", [0, 0, z], "+z");
        p.Sock("front", [0, 0, -z], "-z");
        var mass = kgm2 * area * Pw(c.X, 0.5);
        p.Stats(mass, 40 * Pw(area, 0.8) * Pw(c.X, 0.3) + 60, c.Armour / 4, 2.0 * Pw(c.X, 0.5));
        p.Set(("surface", Kw(("area", areaO), ("at", Py.L(0, 0, Py.Round(-z / 2, 4))), ("lift", "+y"), ("forward", "-z"), ("aspect", aspect), ("cl0", cl0), ("stall", stall), ("cd0", 0.018))));
        return ("", p);
    }

    /// <summary>A swept panel: its tip sits back of its root. Left and right are mirror images, so there are two.</summary>
    private static (string, Part) Swept(Cls c, string side)
    {
        var sign = side == "R" ? 1 : -1;
        double span = 2.0 * c.X, chord = 1.5 * c.X;
        const int Sweep = 30;
        var back = 0.577 * span;
        double x = span / 2, z = chord / 2;
        var area = chord * span;
        var p = new Part(NameOf(c, $"Swept Wing {side}"), c, "Wings",
            $"{Py.G(area)} m2 of wing swept back {Sweep} degrees: less lift a degree, a later stall, less drag fast. A {(side == "R" ? "right" : "left")} wing.",
            tool: "welder", color: Skin, group: GroupOf(c));
        const int N = 4;
        for (var k = 0; k < N; k++)
        {
            double x0 = -x + k * span / N, x1 = -x + (k + 1) * span / N;
            var off = back * ((k + 0.5) / N - 0.5) * sign;
            p.Add(Box([x0 + 0.003 * k, -0.06 * c.X, off * sign * sign - z], [x1, 0.06 * c.X, off + z]));
        }
        p.Sock("+x", [x, 0, back * 0.375 * sign], "+x");
        p.Sock("-x", [-x, 0, -back * 0.375 * sign], "-x");
        p.Sock("+y", [0, 0.06 * c.X, 0], "+y");
        p.Sock("-y", [0, -0.06 * c.X, 0], "-y");
        p.Stats(9 * area * Pw(c.X, 0.5), 40 * Pw(area, 0.8) * Pw(c.X, 0.3) + 60, c.Armour / 4, 2.0 * Pw(c.X, 0.5));
        p.Set(("surface", Kw(("area", area), ("lift", "+y"), ("forward", "-z"), ("aspect", 6), ("sweep", Sweep), ("cl0", 0.2), ("stall", 19), ("cd0", 0.014))));
        return ("", p);
    }

    private static (string, Part) Fabric(Cls c)
    {
        double x = 1.0 * c.X, z = 0.75 * c.X, thick = 0.05 * c.X;
        var area = 4 * x * z;
        var p = new Part(NameOf(c, "Fabric Wing Segment"), c, "Wings",
            $"{Py.G(area)} m2 of cloth on a frame: a third of a metal wing's weight, and it lifts less as it tears. Light, and it won't take much.",
            tool: "welder", material: "cloth", color: Cloth, group: GroupOf(c));
        p.Add(Box([-x, -thick / 2, -z], [x, thick / 2, z]));
        foreach (var at in new[] { -0.7, 0, 0.7 })
        {
            var w = 0.03 * c.X;
            p.Add(Box([at * x - w, -thick, -z - 0.01], [at * x + w, thick, z + 0.01], Rib, detail: true));
        }
        p.Sock("+x", [x, 0, 0], "+x");
        p.Sock("-x", [-x, 0, 0], "-x");
        p.Sock("+y", [0, thick / 2, 0], "+y");
        p.Sock("-y", [0, -thick / 2, 0], "-y");
        p.Sock("back", [0, 0, z], "+z");
        p.Sock("front", [0, 0, -z], "-z");
        p.Stats(3 * area * Pw(c.X, 0.5), 12 * Pw(area, 0.8) + 20, 0, 1.5 * Pw(c.X, 0.5));
        p.Set(("onBreak", "disable"), ("surface", Kw(("area", area), ("lift", "+y"), ("forward", "-z"), ("aspect", 6), ("cl0", 0.3), ("stall", 14), ("cd0", 0.03), ("tears", true))));
        return ("", p);
    }

    /// <summary>A surface with a hinge in the part: its leaf turns by its wire, its first box stays what it collides with.</summary>
    private static (string, Part) Control(Cls c, string b, double chord, object spanO, string about, int maxDeg, double effect, double cl0 = 0.0, bool vertical = false, int sign = 1)
    {
        var span = Py.D(spanO);
        double w = span * c.X / 2, ch = chord * c.X, t = 0.06 * c.X;
        var area = span * chord * c.X * c.X;
        var p = new Part(NameOf(c, b), c, "Control surfaces", $"{Py.G(area)} m2. {about} Wire its angle, -1 to 1 of {maxDeg} degrees.", tool: "wrench", color: Skin, group: GroupOf(c));
        string lift;
        if (vertical)
        {
            p.Add(Box([-t, -w, 0], [t, w, ch], hidden: true));
            p.Add(Box([-t * 0.8, -w + 0.01, 0], [t * 0.8, w - 0.01, ch], Skin, detail: true, shape: Kw(("side", "leaf"))));
            p.Add(Box([-t * 1.2, -w, -0.02 * c.X], [t * 1.2, w, 0.02 * c.X], Dark, detail: true));
            p.Sock("hinge", [0, 0, 0], "-z");
            p.Sock("+y", [0, w, ch / 2], "+y");
            p.Sock("-y", [0, -w, ch / 2], "-y");
            lift = "-x"; // (a plus signal swings the nose to the right, as the seat's steering)
        }
        else
        {
            p.Add(Box([-w, -t, 0], [w, t, ch], hidden: true));
            p.Add(Box([-w + 0.01, -t * 0.8, 0], [w - 0.01, t * 0.8, ch], Skin, detail: true, shape: Kw(("side", "leaf"))));
            p.Add(Box([-w, -t * 1.2, -0.02 * c.X], [w, t * 1.2, 0.02 * c.X], Dark, detail: true));
            p.Sock("hinge", [0, 0, 0], "-z");
            p.Sock("+x", [w, 0, ch / 2], "+x");
            p.Sock("-x", [-w, 0, ch / 2], "-x");
            lift = "+y";
        }
        p.Stats(8 * area * Pw(c.X, 0.5), 30 * Pw(area, 0.8) + 30, c.Armour / 4, 1.5 * Pw(c.X, 0.5));
        var control = Kw(("max", maxDeg), ("rate", 90), ("effect", effect), ("hinge", Py.L(0, 0, 0)));
        if (sign < 0)
            control["sign"] = -1;
        p.Set(("surface", Kw(("area", area), ("at", Py.L(0, 0, ch / 2)), ("lift", lift), ("forward", "-z"), ("aspect", 5), ("cl0", cl0), ("stall", 16), ("cd0", 0.02), ("control", control))),
            ("config", Py.L(
                Kw(("key", "reverse"), ("type", "toggle"), ("label", "Reverse"), ("group", "Surface"), ("tooltip", "It turns the other way for the same signal: a pair of ailerons, one each way.")),
                Kw(("key", "trim"), ("label", "Trim"), ("group", "Surface"), ("unit", "deg"), ("min", -maxDeg), ("max", maxDeg), ("step", 0.5), ("default", 0), ("tooltip", "Where it sits with no signal.")))));
        return ("", p);
    }

    private static (string, Part) FixedFin(Cls c)
    {
        double w = 0.75 * c.X, ch = 0.5 * c.X, t = 0.05 * c.X;
        var area = 2 * w * 2 * ch;
        var p = new Part(NameOf(c, "Tail Fin"), c, "Wings", $"{Py.G(area)} m2 standing up: it keeps a craft pointing the way it flies.", tool: "welder", color: Skin, group: GroupOf(c));
        p.Add(Box([-t, -w, -ch], [t, w, ch]));
        p.Sock("-y", [0, -w, 0], "-y");
        p.Sock("+y", [0, w, 0], "+y");
        p.Sock("back", [0, 0, ch], "+z");
        p.Sock("front", [0, 0, -ch], "-z");
        p.Stats(8 * area * Pw(c.X, 0.5), 30 * Pw(area, 0.8) + 30, c.Armour / 4, 2.0);
        p.Set(("surface", Kw(("area", area), ("lift", "+x"), ("forward", "-z"), ("aspect", 2), ("cl0", 0), ("stall", 16), ("cd0", 0.02))));
        return ("", p);
    }

    private static (string, Part) LooseBlade(Cls c)
    {
        var r = 1.0 * c.X * (c == Large ? 3 : 1);
        var p = new Part(NameOf(c, "Loose Blade"), c, "Rotors", "A rotor blade, thrown off its hub. Scrap.", tool: "wrench", color: Dark, group: GroupOf(c));
        p.Add(Box([-r / 2, -0.04 * c.X, -0.02 * c.X], [r / 2, 0.04 * c.X, 0.02 * c.X]));
        p.Sock("root", [-r / 2, 0, 0], "-x");
        p.Stats(3 * c.X * c.X, 30 * c.X, 0, 1);
        p.Set(("stage", true), ("toolOnly", true));
        return ("", p);
    }

    /// <summary>A hub at its origin's end: it mounts by its back and pushes along +z, its blade one box the game repeats round the hub.</summary>
    private static (string, Part) Rotor(string name, Cls c, string about, object radiusO, int kw, int rpm, int blades, int motor = 0, bool shaft = true, bool heli = false,
        object? duct = null, int top = 95, object? spool = null, int health = 70, double? mass = null, string colour = Dark)
    {
        duct ??= 1;
        spool ??= 1.5;
        var radius = Py.D(radiusO);
        double hub = Math.Max(0.08, radius * 0.12), deep = Math.Max(0.14, radius * 0.14);
        var chord = radius * (heli ? 0.05 : 0.12);
        var mid = deep / 2;
        var p = new Part(name, c, "Rotors", about, role: "rotor", tool: "wrench", color: Rib, group: GroupOf(c));
        p.Add(Box([-hub, -hub, 0], [hub, hub, deep], hidden: true));
        var arm = Math.Max(0.02, chord * 0.3);
        p.Add(Box([-radius, -arm, mid - arm], [radius, arm, mid + arm], hidden: true));
        p.Add(Box([-arm, -radius, mid - arm], [arm, radius, mid + arm], hidden: true));
        p.Add(Box([hub, -chord / 2, mid - Math.Max(0.012, chord * 0.06)], [radius, chord / 2, mid + Math.Max(0.012, chord * 0.06)], colour, detail: true, shape: Kw(("spin", true))));
        p.Add(Box([-hub, -hub, 0.01], [hub, hub, deep], Rib, detail: true, shape: Kw(("shape", "cylinder"), ("front", "+z"))));
        if (Py.D(duct) > 1)
            p.Add(Box([-radius - 0.06, -radius - 0.06, -0.02], [radius + 0.06, radius + 0.06, deep + 0.02], "#3f4750", detail: true,
                shape: Kw(("shape", "tube"), ("inner", Py.Round(radius / (radius + 0.06), 3)), ("ends", "open"), ("front", "+z"))));
        p.Sock("base", [0, 0, 0], "-z");
        var m = mass ?? 4 * blades * Pw(radius, 2.2) + 8;
        p.Stats(m, 80 * Pw(Math.Max(1, radius), 1.5), 2, 2.5);
        var b = Kw(("radius", radiusO), ("ratedKw", kw), ("ratedRpm", rpm), ("blades", blades), ("bladeHealth", health), ("chord", Py.Round(chord, 3)), ("spool", spool),
            ("topSpeed", top), ("duct", duct), ("at", Py.L(0, 0, mid)), ("axis", "+z"), ("forward", "+y"));
        if (motor != 0)
            b["motorKw"] = motor;
        if (!shaft)
            b["shaft"] = false;
        if (heli)
        {
            b["heli"] = true;
            b["cyclic"] = 5;
            b["hubMoment"] = 0.2;
        }
        p.Set(("rotor", b));
        if (shaft)
            p.Set(("ports", Py.L(Kw(("name", "Drive in"), ("type", "shaft"), ("dir", "in"), ("socket", "base")))));
        return ("", p);
    }

    private static List<(string, Part)> RotorList() =>
    [
        Rotor("Two-Blade Prop", Small, "1.8 m across. Bolt it to an engine's Drive out, or link a shaft to it. Its hub is geared to run at its rated speed at the engine's peak.", 0.9, 110, 2400, 2),
        Rotor("Three-Blade Prop", Small, "2.2 m across, 3 blades, for a stronger engine.", 1.1, 220, 2200, 3, health: 80),
        Rotor("Electric Prop", Small, "1 m across, on its own 24 kW motor: no shaft, it runs off the circuit.", 0.5, 24, 4200, 2, motor: 24, shaft: false, health: 40, spool: 0.6),
        Rotor("Ducted Fan", Small, "1 m across in a duct, on its own 100 kW motor. Pushes hard for its size. Tilt it on a joint.", 0.5, 100, 6000, 6, motor: 100, shaft: false, duct: 1.25, top: 110, health: 30, spool: 0.5),
        Rotor("Medium Prop", Medium, "7 m across, 4 blades.", 3.5, 2200, 700, 4, health: 500, top: 110, spool: 3),
        Rotor("Medium Ducted Fan", Medium, "4 m across in a duct, on its own 1.5 MW motor.", 2, 1500, 1500, 6, motor: 1500, shaft: false, duct: 1.25, top: 110, health: 300, spool: 1.2),
        Rotor("Large Prop", Large, "24 m across, 5 blades: a flying fortress's.", 12, 40000, 220, 5, health: 6000, top: 120, spool: 8),
        Rotor("Capital Prop", Capital, "76 m across, 6 blades: it carries a city.", 38, 700000, 70, 6, health: 80000, top: 120, spool: 20),
        Rotor("Helicopter Rotor", Small, "9 m across. Inputs: collective, cyclic pitch, cyclic roll. Its torque turns the craft the other way: a tail rotor has to cancel it.", 4.5, 340, 380, 3, heli: true, spool: 4, health: 90, top: 200),
        Rotor("Tail Rotor", Small, "2 m across, for a helicopter's tail: link a shaft to it. Collective, either way. Its Torque mix setting cancels the main rotor by itself.", 1, 90, 2000, 3, heli: true, spool: 1, health: 30, top: 200),
        Rotor("Medium Helicopter Rotor", Medium, "24 m across. Inputs: collective, cyclic pitch, cyclic roll.", 12, 3500, 140, 5, heli: true, spool: 8, health: 700, top: 200),
        Rotor("Medium Tail Rotor", Medium, "4 m across, on its own 700 kW motor, for a big helicopter's tail.", 2, 700, 900, 4, motor: 700, shaft: false, heli: true, spool: 2, health: 200, top: 200),
    ];

    private static List<(string, Part)> WingList()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Classes)
        {
            foreach (var s in Segments)
                o.Add(Segment(c, s.Name, s.Chord, s.Span, s.KgM2, s.Cl0, s.Aspect, s.Stall));
            o.Add(Swept(c, "L"));
            o.Add(Swept(c, "R"));
            o.Add(Fabric(c));
            o.Add(FixedFin(c));
            o.Add(Control(c, "Aileron", 0.5, 2, "Plus is trailing edge down, more lift: wire a Flight Stabilizer's left and right aileron to each.", 25, 0.7));
            o.Add(Control(c, "Elevator", 0.6, 2, "Behind the centre of mass, a plus signal lifts the nose: a Flight Stabilizer's pitch needs nothing between.", 25, 0.8, sign: -1));
            o.Add(Control(c, "Rudder", 0.6, 1.5, "Behind the centre of mass and standing up, a plus signal swings the nose right: a Flight Stabilizer's yaw.", 30, 0.8, vertical: true));
            o.Add(Control(c, "Flap", 0.5, 2, "More lift and more drag: a Flight Stabilizer's flaps lower it as the speed falls.", 40, 0.7, cl0: 0.1));
        }
        return o;
    }

    // ---- The demos

    /// <summary>A blueprint's steps: Add(...) is the Python's add(**kw), returning the step's index.</summary>
    private sealed class StepList
    {
        public readonly List<object?> S = [];

        public int Add(params (string Key, object? Value)[] kw)
        {
            S.Add(Kw(kw));
            return S.Count - 1;
        }
    }

    private static PyDict Link(params (string Key, object? Value)[] kw) => Kw(kw);

    /// <summary>A tail-dragger tractor: the engine forward, wings on a block under the seat, a tail on a boom.</summary>
    private static PyDict LightPlane()
    {
        var s = new StepList();
        var root = s.Add(("part", "Frame Block"));
        var engine = s.Add(("part", "Turbine"), ("on", root), ("socket", "-z"), ("by", "-z")); // (its Drive out, +z, faces forward: the prop bolts to it)
        var prop = s.Add(("part", "Three-Blade Prop"), ("on", engine), ("socket", "+z"), ("by", "base"));
        var seat = s.Add(("part", "Seat"), ("on", root), ("socket", "+y"), ("by", "bottom"));
        var tank = s.Add(("part", "Fuel Tank"), ("on", root), ("socket", "+z"), ("by", "-z"));
        var boom = s.Add(("part", "Square Beam 2 m"), ("on", tank), ("socket", "+z"), ("by", "-z"));
        var tail = s.Add(("part", "Tail Plane"), ("on", boom), ("socket", "+z"), ("by", "front"));
        var fin = s.Add(("part", "Tail Fin"), ("on", tail), ("socket", "+y"), ("by", "-y"));
        var left = s.Add(("part", "Wing Segment"), ("on", tank), ("socket", "-x"), ("by", "+x"));
        var right = s.Add(("part", "Wing Segment"), ("on", tank), ("socket", "+x"), ("by", "-x"));
        s.Add(("part", "Battery"), ("on", tank), ("socket", "-y"), ("by", "+y")); // (the turbine's starter)
        var left2 = s.Add(("part", "Wing Segment"), ("on", left), ("socket", "-x"), ("by", "+x"));
        var right2 = s.Add(("part", "Wing Segment"), ("on", right), ("socket", "+x"), ("by", "-x"));
        var ailL = s.Add(("part", "Aileron"), ("on", left2), ("socket", "back"), ("by", "hinge"));
        var ailR = s.Add(("part", "Aileron"), ("on", right2), ("socket", "back"), ("by", "hinge"));
        var elev = s.Add(("part", "Elevator"), ("on", tail), ("socket", "back"), ("by", "hinge"));
        var rudd = s.Add(("part", "Rudder"), ("on", fin), ("socket", "back"), ("by", "hinge"));
        var stab = s.Add(("part", "Flight Stabilizer"), ("on", tank), ("socket", "+y"), ("by", "-y"), ("config", Kw(("keys", "Plane"))));
        var leg = s.Add(("part", "Square Beam 0.5 m"), ("on", root), ("socket", "-y"), ("by", "+z"));
        var axle = s.Add(("part", "Square Beam 2 m"), ("on", leg), ("socket", "-z"), ("by", "+y3"), ("spin", 1));
        foreach (var end in new[] { "-z", "+z" })
        {
            var gear = s.Add(("part", "Suspension"), ("on", axle), ("socket", end), ("by", "mount"));
            s.Add(("part", "Wheel"), ("on", gear), ("socket", "axle"), ("by", "axle"));
        }
        var block = s.Add(("part", "Frame Block"), ("on", boom), ("socket", "-y6"), ("by", "+y"));
        foreach (var face in new[] { "-x", "+x" })
        {
            var gear = s.Add(("part", "Suspension"), ("on", block), ("socket", face), ("by", "mount"));
            s.Add(("part", "Wheel"), ("on", gear), ("socket", "axle"), ("by", "axle"));
        }
        var links = Py.L(
            Link(("type", "data"), ("from", seat), ("to", stab)),
            Link(("type", "data"), ("from", stab), ("fromPort", "throttle"), ("to", prop)),
            Link(("type", "data"), ("from", stab), ("fromPort", "pitch"), ("to", elev)),
            Link(("type", "data"), ("from", stab), ("fromPort", "yaw"), ("to", rudd)),
            Link(("type", "data"), ("from", stab), ("fromPort", "left aileron"), ("to", ailL)),
            Link(("type", "data"), ("from", stab), ("fromPort", "right aileron"), ("to", ailR)),
            Link(("type", "pipe"), ("from", tank), ("to", engine), ("fromPort", "Fuel out"), ("toPort", "Fuel in")));
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Light Plane"),
            ("description", "A single-engine tail-dragger on a Flight Stabilizer. Shift and Ctrl set the throttle, S lifts the nose, A and D bank and turn."), ("steps", s.S), ("links", links));
    }

    /// <summary>Four ducted fans of opposite hands on a cross: a Flight Stabilizer's corners, with no jets and no shaft.</summary>
    private static PyDict FanQuad()
    {
        var s = new StepList();
        var root = s.Add(("part", "Square Beam 2 m"));
        var front = s.Add(("part", "Square Beam 2 m"), ("on", root), ("socket", "+y0"), ("by", "-y3"), ("spin", 1));
        var back = s.Add(("part", "Square Beam 2 m"), ("on", root), ("socket", "+y7"), ("by", "-y3"), ("spin", 1));
        var fans = new[] { (front, "+y0", 0), (front, "+y7", 1), (back, "+y0", 1), (back, "+y7", 0) }
            .Select(f => s.Add(("part", "Ducted Fan"), ("on", f.Item1), ("socket", f.Item2), ("by", "base"), ("config", Kw(("hand", f.Item3))))).ToList();
        var seat = s.Add(("part", "Seat"), ("on", root), ("socket", "+y5"), ("by", "bottom"));
        var stab = s.Add(("part", "Flight Stabilizer"), ("on", root), ("socket", "+y3"), ("by", "-y"), ("config", Kw(("keys", "Rotorcraft"))));
        s.Add(("part", "Battery Pack"), ("on", root), ("socket", "-y3"), ("by", "+y"));
        var links = Py.L(Link(("type", "data"), ("from", seat), ("to", stab)));
        string[] ports = ["front left", "front right", "rear left", "rear right"];
        for (var i = 0; i < 4; i++)
            links.Add(Link(("type", "data"), ("from", stab), ("fromPort", ports[i]), ("to", fans[i])));
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Fan Quad"),
            ("description", "Four ducted fans of opposite hands and a Flight Stabilizer: no jets, no shaft. Shift lifts off, Ctrl lands, W and S lean it on, Q and E sideways, A and D turn."), ("steps", s.S), ("links", links));
    }

    private const int SpinT = 0, SpinTail = 0;

    /// <summary>An engine straight under its rotor, a tail rotor on a boom held by a bias the driver's keys trim, wheels to stand on.</summary>
    private static PyDict Helicopter()
    {
        var s = new StepList();
        var root = s.Add(("part", "Square Beam 1 m"));
        var seat = s.Add(("part", "Seat"), ("on", root), ("socket", "-z"), ("by", "back"));
        var engine = s.Add(("part", "Turbine"), ("on", root), ("socket", "+y3"), ("by", "-z"));
        var gearbox = s.Add(("part", "T Gearbox"), ("on", engine), ("socket", "+z"), ("by", "-z"), ("spin", SpinT));
        var main = s.Add(("part", "Helicopter Rotor"), ("on", gearbox), ("socket", "+z"), ("by", "base"));
        var tank = s.Add(("part", "Fuel Tank"), ("on", root), ("socket", "-y1"), ("by", "+y"));
        s.Add(("part", "Battery"), ("on", tank), ("socket", "+z"), ("by", "-z"));
        var boom = s.Add(("part", "Square Beam 2 m"), ("on", root), ("socket", "+z"), ("by", "-z"));
        var boom2 = s.Add(("part", "Square Beam 1 m"), ("on", boom), ("socket", "+z"), ("by", "-z"));
        s.Add(("part", "Frame Block"), ("on", seat), ("socket", "+x"), ("by", "-x")); // (ballast: the boom's weight, forward of the mast)
        s.Add(("part", "Frame Block"), ("on", seat), ("socket", "-x"), ("by", "+x"));
        var post = s.Add(("part", "Square Beam 0.5 m"), ("on", boom2), ("socket", "+y3"), ("by", "-z")); // (the tail rotor level with the centre of mass)
        var tailbox = s.Add(("part", "Right-Angle Gearbox"), ("on", post), ("socket", "+x1"), ("by", "+y"), ("spin", SpinTail));
        var tail = s.Add(("part", "Tail Rotor"), ("on", tailbox), ("socket", "-y"), ("by", "base"), ("config", Kw(("torqueMix", 1), ("reverse", 1))));
        s.Add(("part", "Tail Fin"), ("on", boom2), ("socket", "+z"), ("by", "front"));
        var stab = s.Add(("part", "Flight Stabilizer"), ("on", boom), ("socket", "+y1"), ("by", "-y"), ("config", Kw(("keys", "Rotorcraft"))));
        var leg = s.Add(("part", "Square Beam 0.5 m"), ("on", tank), ("socket", "-y"), ("by", "+z"));
        var axle = s.Add(("part", "Square Beam 2 m"), ("on", leg), ("socket", "-z"), ("by", "+y3"), ("spin", 1));
        foreach (var end in new[] { "-z", "+z" })
        {
            var gear = s.Add(("part", "Suspension"), ("on", axle), ("socket", end), ("by", "mount"));
            s.Add(("part", "Wheel"), ("on", gear), ("socket", "axle"), ("by", "axle"));
        }
        var strut = s.Add(("part", "Square Beam 1 m"), ("on", boom), ("socket", "-y5"), ("by", "+z"));
        s.Add(("part", "Suspension"), ("on", strut), ("socket", "-z"), ("by", "mount"));
        var links = Py.L(
            Link(("type", "data"), ("from", seat), ("to", stab)),
            Link(("type", "data"), ("from", stab), ("fromPort", "lift"), ("to", main), ("toPort", "collective")),
            Link(("type", "data"), ("from", stab), ("fromPort", "pitch"), ("to", main), ("toPort", "cyclic pitch")),
            Link(("type", "data"), ("from", stab), ("fromPort", "roll"), ("to", main), ("toPort", "cyclic roll")),
            Link(("type", "data"), ("from", stab), ("fromPort", "yaw"), ("to", tail), ("toPort", "collective")),
            Link(("type", "shaft"), ("from", gearbox), ("fromPort", "Branch shaft"), ("to", tailbox), ("toPort", "Input shaft")),
            Link(("type", "pipe"), ("from", tank), ("to", engine), ("fromPort", "Fuel out"), ("toPort", "Fuel in")));
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", "Small Helicopter"),
            ("description", "One rotor on a turbine, a tail rotor on a shaft, a Flight Stabilizer flying both. Shift climbs, Ctrl sinks, W and S lean it on, Q and E sideways, A and D turn."), ("steps", s.S), ("links", links));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Wings, WingList());
        WriteParts(o, Rotors, [.. RotorList(), .. Classes.Select(LooseBlade)]);
        o.File($"{Blueprints}/LightPlane.json", PyJson.Dumps(LightPlane(), 2) + "\n");
        o.File($"{Blueprints}/SmallHelicopter.json", PyJson.Dumps(Helicopter(), 2) + "\n");
        o.File($"{Blueprints}/FanQuad.json", PyJson.Dumps(FanQuad(), 2) + "\n");
    }
}
