using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's linear joints (Constructs/Parts/Linear): rail sections with a carriage, free, electric and shaft driven
/// (docs/sandbox/CONSTRUCTS.md, "Linear joints"; Source/Constructs/ConstructLinear.cs). Was tools/constructs/linear_parts.py.
///
/// A section's axis is +z: its middle is where the carriage rests (the leaf socket "top"), its ends the sockets "end-" and "end+" that a
/// stack joins by. The travel of a section is its length, so three 2 m sections are a 6 m rail. The carriage's boxes are "side": "leaf"
/// details: the game moves them with what hangs on it, and shows one carriage on a stack.
/// Kinds: Free Slider (a "slider" joint: no motor), Linear Actuator (a piston's motor and strength model, on the circuit), Shaft Linear
/// Drive (the same motor turned by a drive shaft through a lead screw). Sizes: three gauges of small (1x1, 2x2, 3x3 cells of 0.25 m), and
/// one of Medium, Large and Capital, a few travels each. tools/constructs/make_joint_sides.py leaves these alone: they draw their own sides.
/// </summary>
public sealed class LinearParts : IPartGenerator
{
    public string Name => "linear_parts";

    private const string Out = "Constructs/Parts/Linear";
    private const string BaseC = "#3b4046", Guide = "#a9b0b8", Collar = "#f08a12", Screw = "#c7a23a", Housing = "#2c3238";

    // (class, gauge in cells, travels in m, rated force N, top speed m/s, shaft lead m/turn)
    private static readonly (Cls C, int G, double[] Travels, long Force, double Top, double Lead)[] Sizes =
    [
        (Small, 1, [0.5, 1, 2], 6_000, 2.0, 0.05),
        (Small, 2, [1, 2, 4], 24_000, 2.0, 0.05),
        (Small, 3, [2, 4], 60_000, 2.0, 0.05),
        (Medium, 1, [4, 8], 300_000, 2.0, 0.2),
        (Large, 1, [16, 32], 5_000_000, 3.0, 0.8),
        (Capital, 1, [32, 64], 200_000_000, 4.0, 3.2),
    ];
    private const int SideLoad = 5; // N across a driven rail, times its rating: a rail is made to carry what rides on it

    private static PyDict Num(string key, string label, string group, string unit, object lo, object hi, object step, string tip, object? def = null)
    {
        var e = Kw(("key", key), ("label", label), ("group", group), ("unit", unit), ("min", lo), ("max", hi), ("step", step), ("tooltip", tip));
        if (def is not null)
            e["default"] = def;
        return e;
    }

    private static List<object?> Stops() => Py.L(
        Num("stopLow", "Low stop", "Stops", "%", 0, 100, 1, "Where the low end stop sits along the whole rail, a stack too.", 0),
        Num("stopHigh", "High stop", "Stops", "%", 0, 100, 1, "Where the high end stop sits along the whole rail, a stack too.", 100));

    private static string Word(Cls c, int g) => c == Small ? $"{g}x{g}" : c.Name;

    /// <summary>max(100, force / 100) as Python gives it: the int 100, or the float when it is larger.</summary>
    private static object Step(long force) => force / 100.0 > 100 ? force / 100.0 : (object)100;

    private static PyDict Leaf(params (string, object?)[] more) => Kw([("side", "leaf"), .. more]);
    private static PyDict BaseSide(params (string, object?)[] more) => Kw([("side", "base"), .. more]);

    private static Part Section(string kind, Cls c, int g, double travel, long force, double top, double lead)
    {
        var w = g * c.Cell;
        var h = c == Small && g == 1 ? w : w * 0.5; // the rail's height (a 1x1 is square: a small part welds to its side)
        var plate = 0.1 * w; // the carriage's plate
        var lng = Math.Min(travel * 0.45, 2 * w); // the carriage's length
        var lip = 0.008 * c.X;
        var y = h / 2;
        var head = kind switch { "free" => "Free Slider", "electric" => "Linear Actuator", _ => "Shaft Linear Drive" };
        var name = $"{head} {Word(c, g)} {Fmt(travel)} m";
        var kn = $"{Py.G(force / 1000.0)} kN";
        var desc = kind switch
        {
            "free" => $"{Fmt(travel)} m of free rail. Stacks end to end. Lock signal holds it.",
            "electric" => $"{Fmt(travel)} m. Set 0..1 of travel; stacks end to end. Rated {kn}.",
            _ => $"{Fmt(travel)} m. Shaft turns it: {Fmt(lead)} m a turn. Holds with the engine off unless set to back-drive. Rated {kn}.",
        };
        var p = new Part(name, c, "Linear", desc, role: "joint", tool: "wrench", color: BaseC, group: "Joints");

        // The rail, and its two guides (long details just proud of it, so one run of sections reads as one rail).
        var z = travel / 2;
        p.Add(Box([-w / 2, -y, -z], [w / 2, y, z], BaseC, shape: BaseSide()));
        var topY = y + plate + lip * 2.5;
        p.Add(Box([-w * 0.3, y - lip, -z], [w * 0.3, topY, z], hidden: true, shape: BaseSide())); // (a socket stands on a solid face: the carriage's travel, never drawn)
        p.Add(Box([-w / 2 - lip, -y * 0.2, -z + lip / 2], [w / 2 + lip, y * 0.45, z - lip / 2], Guide, detail: true, shape: BaseSide(("paintable", false))));
        if (kind == "shaft")
        {
            p.Add(Box([-w * 0.08, y, -z + lip / 2], [w * 0.08, y + lip * 2, z - lip / 2], Screw, detail: true, shape: BaseSide(("paintable", false))));
            p.Add(Box([w * 0.3, -y * 0.8, -w * 0.3], [w / 2 + lip * 1.5, y * 0.8, w * 0.3], Housing, detail: true, shape: BaseSide(("paintable", false)))); // the gear case at Drive in
        }
        if (kind == "electric")
        {
            p.Add(Box([-w * 0.08, y, -z + lip / 2], [w * 0.08, y + lip * 2, z - lip / 2], Guide, detail: true, shape: BaseSide(("paintable", false))));
            p.Add(Box([-w / 2 - lip * 1.5, -y * 0.8, -w * 0.3], [-w * 0.3, y * 0.8, w * 0.3], Housing, detail: true, shape: BaseSide(("paintable", false)))); // the motor
        }

        // The carriage (its side is the leaf: it travels), wrapped over the rail.
        p.Add(Box([-w / 2 - lip * 2.5, y + lip * 2.5, -lng / 2], [w / 2 + lip * 2.5, topY, lng / 2], Collar, detail: true, shape: Leaf(("paintable", false))));
        foreach (var sx in new[] { -1, 1 })
        {
            double a = sx * (w / 2 + lip * 1.5), b = sx * (w / 2 + lip * 2.5);
            p.Add(Box([Math.Min(a, b), -y * 0.5, -lng / 2 + lip], [Math.Max(a, b), y + lip * 2.5 + 0.002 * c.X, lng / 2 - lip], Collar, detail: true, shape: Leaf(("paintable", false))));
        }
        // A cue on its top: a bar along the axis, for which way it goes.
        p.Add(Box([-w * 0.05, topY, -lng * 0.42], [w * 0.05, topY + lip * 0.8, lng * 0.42], "#1c1c1c", detail: true, shape: Leaf(("paintable", false))));

        p.Sock("-x", [-w / 2, 0, 0], "-x");
        const string driveSocket = "+x";
        p.Sock("+x", [w / 2, 0, 0], "+x");
        p.Sock("end-", [0, 0, -z], "-z");
        p.Sock("end+", [0, 0, z], "+z");
        p.Sock("top", [0, topY, 0], "+y");
        // Underneath, on the cell centres (the ends stay on the frame's lattice), the middle ones first; and the sides.
        var cells = (int)Math.Max(1, Py.RoundInt(travel / c.Cell));
        var ats = cells % 2 == 0
            ? Enumerable.Range(0, cells).Select(k => (k + 0.5) * c.Cell - z).ToList()
            : Enumerable.Range(0, cells).Select(k => k * c.Cell - (cells / 2) * c.Cell).ToList();
        ats = [.. ats.OrderBy(Math.Abs)];
        for (var n = 0; n < Math.Min(6, ats.Count); n++)
            p.Sock(n == 0 ? "base" : $"base.{n}", [0, -y, ats[n]], "-y");
        var kc = g * g * travel / c.Cell;
        double mass, health;
        int armour;
        double seconds;
        if (c == Small)
        {
            mass = (kind == "free" ? 1.0 : kind == "electric" ? 1.4 : 1.3) * 0.3 * kc;
            health = 100 + 8 * kc;
            armour = 4 + g;
            seconds = 1.5 + 0.5 * g;
        }
        else
        {
            var area = 4 * w * travel;
            mass = area * c.KgM2;
            health = area * c.HealthM2;
            armour = c.Armour + 8;
            seconds = c.Seconds * 1.5;
        }
        p.Stats(mass, health, armour, seconds);

        var joint = Kw(("type", kind == "free" ? "slider" : "piston"), ("axis", "+z"), ("min", R(-z)), ("max", R(z)), ("leaf", "top"));
        var speed = top * 0.5;
        if (kind != "free")
            joint.Update(("speed", speed), ("strength", force), ("give", R(0.25 * travel)), ("sideLoad", SideLoad * force), ("overloadDamage", 0));
        var rail = Kw(("gauge", g), ("drive", kind));
        joint["rail"] = rail;
        if (kind == "shaft")
        {
            joint["speed"] = top;
            rail["lead"] = lead;
        }
        var config = new List<object?>();
        if (kind == "electric")
            config.AddRange([
                Kw(("key", "setMode"), ("type", "choice"), ("label", "Set signal is"), ("group", "Motor"), ("options", Py.L("Position", "Speed")), ("default", "Position"),
                    ("tooltip", "Position: 0 is one end, 1 the other. Speed: -1 to 1 of its speed, 0 holds.")),
                Num("rest", "Rest position", "Motor", "%", 0, 100, 1, "Where it sits with nothing wired into Set: % along the rail.", 50),
                Num("speed", "Speed", "Motor", "m/s", 0.05, top, 0.05, $"How fast it slides, up to {Py.G(top)} m/s."),
                Num("strength", "Force", "Motor", "N", force / 10.0, force, Step(force), $"What it holds, up to {force} N."),
            ]);
        if (kind == "shaft")
            config.AddRange([
                Num("lead", "Lead", "Screw", "m/turn", R(lead / 5), R(lead * 4), R(lead / 10), "How far a turn of the shaft moves it. Longer: faster, less force.", lead),
                Num("strength", "Force", "Screw", "N", force / 10.0, force, Step(force), $"What it holds, up to {force} N."),
                Kw(("key", "backDrive"), ("type", "toggle"), ("label", "Back-drive"), ("group", "Screw"), ("default", false),
                    ("tooltip", "Off: the screw holds a load with the engine off. On: a load pushes it back unless the shaft is turning it.")),
            ]);
        config.AddRange(Stops());
        p.Set(("joint", joint), ("config", config), ("centreSockets", false));
        if (kind == "electric")
            p.Set(("power", c == Small ? R(10 * kc / 16) : R(force * top / 20000)), ("ports", Py.L(Kw(("name", "Power in"), ("type", "power"), ("dir", "in"), ("at", Py.L(R(-w / 2), 0.0, 0.0)), ("face", "-x")))));
        else if (kind == "shaft")
            p.Set(("power", 0), ("ports", Py.L(Kw(("name", "Drive in"), ("type", "shaft"), ("dir", "in"), ("socket", driveSocket)))));
        else
            p.Set(("power", 0));
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var (c, g, travels, force, top, lead) in Sizes)
            foreach (var travel in travels)
                foreach (var (kind, folder) in new[] { ("free", "Free"), ("electric", "Electric"), ("shaft", "Shaft") })
                    parts.Add((folder, Section(kind, c, g, travel, force, top, lead)));
        WriteParts(o, Out, parts);
    }
}
