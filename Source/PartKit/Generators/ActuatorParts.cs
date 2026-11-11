namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's actuator size families (Constructs/Parts/Actuators): pistons, motor hinges, turntables and flexible joints. Board item
/// dev-sandbox-actuator-sizes-as01; docs/sandbox/CONSTRUCTS.md, "Actuator sizes and strength". Was tools/constructs/actuator_parts.py,
/// with make_joint_sides.py's pass (<see cref="JointSides"/>) applied to every file it writes.
///
/// Each family comes in three widths (1x1, 2x2 and 3x3 cells of the 0.25 m frame step) and three lengths (short, medium, long):
/// a piston's width sets its force and how much side load it takes, its length its stroke (and so its top speed); a motor hinge's or
/// turntable's width sets its torque, its length its speed (a longer housing holds a bigger motor, so it turns faster); mass, health,
/// armour, power and fitting time grow with the size.
///
/// The rating (the most force or torque it holds rigidly) is the top of its Strength setting, and the top of its Speed setting is the
/// size's top speed; both start from a tenth of that. Above its rating a joint gives way (non-linearly, "give" is how far it has given at
/// twice the rating) and recovers at its speed when the load drops (ConstructPhysics.cs, "the strength model"). "overloadDamage" stays 0.
///
/// The three parts that were there before (Piston, Motor Hinge, Turntable) are one size each (2x2 long, 1x1 medium, 2x2 short): they keep
/// their names, shapes, sockets and default settings, so existing blueprints and constructs still fit.
/// </summary>
public sealed class ActuatorParts : IPartGenerator
{
    public string Name => "actuator_parts";

    private const double C = 0.25; // the frame step (one cell)
    private const string Out = "Constructs/Parts/Actuators";
    private static readonly string[] Lengths = ["", "Short", "Medium", "Long"];
    private const string Gold = "#c7a23a";

    private static double R(double v) => PartKitMath.R(v);

    private static List<object?> Vec(params double[] v) => PartKitMath.Vec(v);

    private static PyDict Sock(string name, double[] at, string d) => new() { ["name"] = name, ["at"] = Vec(at), ["dir"] = d };

    private static List<object?> Sides(double hx, double hz, double y = 0.0) =>
        [Sock("+x", [hx, y, 0], "+x"), Sock("-x", [-hx, y, 0], "-x"), Sock("+z", [0, y, hz], "+z"), Sock("-z", [0, y, -hz], "-z")];

    private static PyDict Num(string key, string label, string group, string unit, double lo, double hi, object step, string tip, double? def = null)
    {
        var e = new PyDict { ["key"] = key, ["label"] = label, ["group"] = group, ["unit"] = unit, ["min"] = R(lo), ["max"] = R(hi), ["step"] = step, ["tooltip"] = tip };
        if (def is { } dv)
            e["default"] = R(dv);
        return e;
    }

    /// <summary>A rotary joint's speed setting: its rated speed in the middle of a range from stopped to twice as fast.</summary>
    private static PyDict SpeedSetting(int rated) =>
        Num("speed", "Speed", "Motor", "deg/s", 0, 2 * rated, 1, $"How fast it turns: rated {rated} deg/s (0%), from stopped (-100%) to {2 * rated} deg/s (+100%).");

    // ---- Pistons

    private static readonly Dictionary<int, int> PistonForce = new() { [1] = 6000, [2] = 24000, [3] = 60000 }; // N: the rating
    private static readonly Dictionary<int, int> PistonSide = new() { [1] = 2000, [2] = 8000, [3] = 20000 }; // N across its axis before it gives
    private static readonly Dictionary<int, double> PistonStroke = new() { [1] = 0.5, [2] = 1.0, [3] = 2.0 }; // m

    private static PyDict Head(string name, string desc, string section, int w, double mass, int health, int armour, string color, double? power)
    {
        var d = new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = name,
            ["description"] = desc,
            ["group"] = "Joints",
            ["section"] = section,
            ["role"] = "joint",
            ["tool"] = "wrench",
            ["attachSeconds"] = (section == "Flexible joints" ? 1.0 : 1.5) + 0.5 * w,
            ["mass"] = mass,
            ["health"] = health,
            ["armour"] = armour,
            ["material"] = "steel",
            ["color"] = color,
        };
        if (power is { } p)
            d["power"] = p;
        return d;
    }

    private static PyDict Piston(int w, int l, bool legacy)
    {
        var k = w * w * (l + 1);
        var stroke = PistonStroke[l];
        var force = PistonForce[w];
        var top = stroke * 1.0; // m/s at the top of its Speed setting
        string name;
        double s, h, speed;
        int strength;
        if (legacy)
            (name, s, h, speed, strength) = ("Piston", 0.4, 0.3, 0.8, 14000);
        else
            (name, s, h, speed, strength) = ($"Piston {w}x{w} {Lengths[l]}", C * w, C * l, 0.4 * stroke, force);
        var d = Head(name, $"Stroke {Py.G(stroke)} m. Signal 1: out, 0: in. Rated {force} N, {PistonSide[w]} N across.", "Pistons", w, R(2.5 * k), 100 + 10 * k, 4 + w, "#6d747c", R(10 * k / 16.0));
        d["boxes"] = Py.L(
            new PyDict { ["size"] = Vec(s, h, s) },
            new PyDict { ["size"] = Vec(s + 0.02, 0.06, s + 0.02), ["at"] = Vec(0, h / 2 - 0.027, 0), ["color"] = Gold, ["detail"] = true });
        d["sockets"] = new List<object?> { Sock("base", [0, -h / 2, 0], "-y"), Sock("top", [0, h / 2, 0], "+y") }.Concat(Sides(s / 2, s / 2)).ToList();
        d["joint"] = new PyDict
        {
            ["type"] = "piston", ["axis"] = "+y", ["min"] = 0, ["max"] = stroke, ["leaf"] = "top", ["speed"] = R(speed), ["strength"] = strength,
            ["give"] = R(0.25 * stroke), ["sideLoad"] = PistonSide[w], ["overloadDamage"] = 0,
        };
        d["config"] = Py.L(
            Num("min", "Pulled in to", "Travel", "m", 0, stroke, 0.01, "How far out it rests when pulled in."),
            Num("max", "Pushed out to", "Travel", "m", 0, stroke, 0.01, "How far out it goes when pushed out (a signal of 1)."),
            Num("speed", "Speed", "Motor", "m/s", 0.05, top, 0.05, $"How fast it slides, up to {Py.G(top)} m/s."),
            Num("strength", "Force", "Motor", "N", force / 10.0, force, 100, $"What it holds, up to {force} N."));
        return d;
    }

    // ---- Motor hinges

    private static readonly Dictionary<int, int> HingeTorque = new() { [1] = 4000, [2] = 12000, [3] = 30000 }; // N m
    private static readonly Dictionary<int, int> TurnSpeed = new() { [1] = 45, [2] = 90, [3] = 180 }; // deg/s at the top of its Speed setting

    private static PyDict MotorHinge(int w, int l, bool legacy)
    {
        var k = w * w * (l + 1);
        var torque = HingeTorque[w];
        var top = TurnSpeed[l];
        string name;
        double s, h;
        int speed, strength;
        if (legacy)
            (name, s, h, speed, strength) = ("Motor Hinge", 0.2, 0.5, 90, 3000);
        else
            (name, s, h, speed, strength) = ($"Motor Hinge {w}x{w} {Lengths[l]}", C * w, C * l, top, torque);
        var d = Head(name, $"Signal 1: max, -1: min, 0: shut. Rated {torque} N m.", "Motor hinges", w, R(4 * k), 100 + 20 * k, 4 + w, "#4f6a7a", R(10 * k / 3.0));
        d["boxes"] = Py.L(
            new PyDict { ["size"] = Vec(s, h, s) },
            new PyDict { ["size"] = Vec(s + 0.02, Math.Min(0.12, h * 0.4), s + 0.02), ["at"] = Vec(0, h / 4, 0), ["color"] = Gold, ["detail"] = true });
        d["sockets"] = Py.L(Sock("base", [-s / 2, 0, 0], "-x"), Sock("leaf", [s / 2, 0, 0], "+x"));
        d["joint"] = new PyDict
        {
            ["type"] = "motor", ["axis"] = "+y", ["pivot"] = Vec(s / 2, 0, 0), ["min"] = -100, ["max"] = 100, ["leaf"] = "leaf",
            ["speed"] = speed, ["strength"] = strength, ["give"] = 20, ["overloadDamage"] = 0,
        };
        d["config"] = Py.L(
            Num("min", "Lowest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of -1 turns it to."),
            Num("max", "Highest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of 1 (or opening it) turns it to."),
            SpeedSetting(speed),
            Num("strength", "Torque", "Motor", "N m", torque / 10.0, torque, 50, $"What it holds, up to {torque} N m."));
        return d;
    }

    // ---- Turntables

    private static readonly Dictionary<int, int> TurntableTorque = new() { [1] = 3000, [2] = 12000, [3] = 36000 }; // N m

    private static PyDict Turntable(int w, int l, bool legacy)
    {
        var k = w * w * (l + 1);
        var torque = TurntableTorque[w];
        var top = TurnSpeed[l];
        string name;
        double s, h;
        int speed, strength;
        if (legacy)
            (name, s, h, speed, strength) = ("Turntable", 0.6, 0.2, 45, 6000);
        else
            (name, s, h, speed, strength) = ($"Turntable {w}x{w} {Lengths[l]}", C * w, 0.1 + 0.1 * l, top, torque);
        var d = Head(name, $"Signal 1 or -1 turns it, 0 holds. Rated {torque} N m.", "Turntables", w, R(5.625 * k), 100 + 25 * k, 6 + w, "#5b6168", R(10 * k / 8.0));
        d["boxes"] = Py.L(
            new PyDict { ["size"] = Vec(s, h, s) },
            new PyDict { ["size"] = Vec(s - 0.1, 0.04, s - 0.1), ["at"] = Vec(0, h / 2, 0), ["color"] = Gold, ["detail"] = true });
        d["sockets"] = new List<object?> { Sock("base", [0, -h / 2, 0], "-y"), Sock("top", [0, h / 2, 0], "+y") }.Concat(Sides(s / 2, s / 2)).ToList();
        d["joint"] = new PyDict
        {
            ["type"] = "turntable", ["axis"] = "+y", ["pivot"] = Vec(0, h / 2, 0), ["leaf"] = "top", ["speed"] = speed, ["strength"] = strength,
            ["give"] = 20, ["overloadDamage"] = 0,
        };
        d["config"] = Py.L(
            SpeedSetting(speed),
            Num("strength", "Torque", "Motor", "N m", torque / 10.0, torque, 100, $"What it holds, up to {torque} N m."));
        return d;
    }

    // ---- Flexible joints (docs/sandbox/CONSTRUCTS.md, "Flexible joints")

    private static readonly Dictionary<int, int> FlexTorque = new() { [1] = 2000, [2] = 8000, [3] = 24000 }; // N m: the rating

    // rubber: bends a little, stiff, doesn't twist; ball: bends far, soft, twists
    private static readonly (string Kind, int Cone, int Twist, double Soft, string Ring)[] FlexKinds =
    [
        ("Flex Joint", 25, 0, 1.0, "#26282b"),
        ("Ball Joint", 50, 30, 0.4, "#8d939a"),
    ];

    private static PyDict FlexJoint((string Kind, int Cone, int Twist, double Soft, string Ring) f, int w)
    {
        var k = w * w * 2;
        double s = C * w, h = 0.1 + 0.15 * w;
        var torque = FlexTorque[w];
        var stiffness = R(f.Soft * torque / f.Cone); // N m a degree: at its rating a rubber joint just reaches its stop
        var twist = f.Twist != 0 ? $" and twists {f.Twist} degrees each way" : "";
        var d = Head($"{f.Kind} {w}x{w}", $"Bends up to {f.Cone} degrees{twist}, springs back. Rated {torque} N m. A signal locks it.", "Flexible joints", w, R(1.5 * k), 80 + 15 * k, 2 + w, f.Ring, null);
        d["boxes"] = Py.L(new PyDict { ["size"] = Vec(s, h, s), ["hidden"] = true }); // what it collides as; the joint-sides pass adds the flange, collar and bolts
        d["sockets"] = Py.L(Sock("base", [0, -h / 2, 0], "-y"), Sock("top", [0, h / 2, 0], "+y"));
        d["joint"] = new PyDict
        {
            ["type"] = "flex", ["axis"] = "+y", ["pivot"] = Vec(0, 0, 0), ["leaf"] = "top", ["cone"] = f.Cone, ["twist"] = f.Twist,
            ["stiffness"] = stiffness, ["damping"] = 0.7, ["strength"] = torque, ["overloadDamage"] = 0,
        };
        d["config"] = Py.L(
            Num("max", "Bend limit", "Flex", "deg", 2, f.Cone, 1, $"How far it bends, up to {f.Cone} degrees."),
            Num("stiffness", "Stiffness", "Flex", "N m/deg", stiffness / 10, stiffness * 2, R(Math.Max(stiffness / 50, 0.5)), "How hard it springs back."),
            Num("damping", "Damping", "Flex", "ratio", 0, 2, 0.05, "1: no overshoot. Less bounces."));
        return d;
    }

    // The parts that were there before, as the size they are (they keep their names, shapes and defaults).
    private static readonly Dictionary<string, (int, int)> Legacy = new() { ["Pistons"] = (2, 3), ["MotorHinges"] = (1, 2), ["Turntables"] = (2, 1) };

    private static string FileName(string name) => new string([.. name.Where(char.IsAsciiLetterOrDigit)]) + ".json";

    public void Make(PartOutput o)
    {
        var all = new List<(string Folder, PyDict D)>();
        foreach (var (folder, make) in new (string, Func<int, int, bool, PyDict>)[] { ("Pistons", Piston), ("MotorHinges", MotorHinge), ("Turntables", Turntable) })
            for (var w = 1; w <= 3; w++)
                for (var l = 1; l <= 3; l++)
                    all.Add((folder, make(w, l, Legacy[folder] == (w, l))));
        foreach (var kind in FlexKinds)
            for (var w = 1; w <= 3; w++)
                all.Add(("FlexJoints", FlexJoint(kind, w)));
        o.Own(Out);
        var names = new HashSet<string>();
        foreach (var (folder, d) in all)
        {
            var name = (string)d["name"]!;
            if (!names.Add(name) || name.Length > 32)
                throw new InvalidOperationException($"actuator {name}");
            o.File($"{Out}/{folder}/{FileName(name)}", JointSides.Apply(PyJson.Dumps(d, 2) + "\n"));
        }
    }
}
