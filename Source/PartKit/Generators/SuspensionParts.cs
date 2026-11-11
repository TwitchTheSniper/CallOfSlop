using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.BigDriveParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The suspension families (Constructs/Parts/Suspensions): docs/sandbox/CONSTRUCTS.md, "Suspension types". Was tools/constructs/suspension_parts.py.
///
/// Five kinds, each in a ladder of sizes from a kart to a capital body: the plain bracket (the ladder's missing sizes), the Pogo Strut (a
/// telescoping strut hung from its top end, the wheel straight under it), the Swing Arm (hinged on the chassis side, the wheel leans with
/// it), the Double Wishbone (two arms, the wheel stays upright) and the Trailing Arm (hinged ahead of the wheel; roll it 180 degrees on its
/// socket to lead instead, or for the other side of the car). Sizes go by wheel: the description says which wheels a size takes and what
/// it carries a wheel. The tables at the top are the balance. Boxes that move carry a "rig" (hub, arm, leg) and the suspension a "path":
/// the game draws them from where its wheel is. Every part is a hidden solid (what collides, what the sockets sit on, at rest) with the
/// moving boxes as details over it. It also writes the demo buggies from the Buggy blueprint (Constructs/Blueprints/Buggy.json).
/// </summary>
public sealed class SuspensionParts : IPartGenerator
{
    public string Name => "suspension_parts";

    private const string Out = "Constructs/Parts/Suspensions";
    private static readonly int[] LeftSteps = [6, 10]; // the Buggy's steps that mount a left wheel
    private const string MenuGroup = "Drivetrain", MenuSection = "Suspension"; // where these parts sit in the part menu: the one place to change it

    private static string Tonnes(object kg) => Py.D(kg) < 1000 ? $"{Py.G(Py.D(kg))} kg" : $"{Py.G(Py.D(kg) / 1000)} t";

    private sealed record Size(string Word, Cls C, string Wheels, object Load, double Travel, double Hz, double Damp, double Slew, int Kg, int Strut, int Arm);

    private static Size Big(string word, Cls c, string wheels, int kg)
    {
        var (travel, hz, load, slew) = Springs[c];
        return new(word, c, wheels, load, travel, hz, 0.8, slew, kg, 3, 3);
    }

    // name, class, wheels it takes, kg a wheel, travel m, Hz, damping, slew m/s, kg, strut cells, arm cells
    private static readonly Size[] Sizes =
    [
        new("Kart", Small, "0.2-0.3 m", 150, 0.12, 3.2, 0.75, 0, 5, 3, 2),
        new("Light", Small, "0.3-0.45 m", 400, 0.2, 2.6, 0.75, 0, 9, 4, 3),
        new("", Small, "0.4-0.55 m", 1000, 0.3, 2.2, 0.75, 0, 16, 5, 3),
        new("Heavy", Small, "0.55-0.8 m", 2500, 0.45, 1.8, 0.8, 0, 35, 6, 4),
        new("Truck", Small, "1.2 m", 8000, 0.8, 1.4, 0.8, 0, 130, 8, 6),
        Big("Medium", Medium, "1.5 m", 1500),
        Big("Large", Large, "6 m", 90000),
        Big("Capital", Capital, "24 m", 900000),
    ];

    // Each kind's words and how much it weighs against the plain bracket.
    private static readonly Dictionary<string, (string Verb, double M, string Color)> Kinds = new()
    {
        ["Pogo Strut"] = ("Drops straight down", 1.1, "#3f5f7a"),
        ["Swing Arm"] = ("Arm swings, wheel leans", 1.3, "#7a4f33"),
        ["Double Wishbone"] = ("Two arms, wheel stays upright", 1.6, "#55704a"),
        ["Trailing Arm"] = ("Arm runs back to the wheel", 1.2, "#6a5f7c"),
    };
    private const string Steel = "#8f959c"; // rods, shafts
    private const string Dark = "#34383d"; // what they bolt to
    private const string Coil = "#d0b050"; // springs, as on the plain suspension
    private const double UnderArm = -0.66; // an arm's Drive in, in half cells below the hub's middle: the half shaft runs under the arm and can pass under the mount

    private static List<object?> Config() => Py.L(
        Kw(("key", "stiffness"), ("label", "Stiffness"), ("group", "Spring"), ("unit", "Hz"), ("min", 0.5), ("max", 6), ("step", 0.05), ("tooltip", "The spring's bounce frequency: higher is stiffer.")),
        Kw(("key", "damping"), ("label", "Damping"), ("group", "Spring"), ("min", 0), ("max", 2), ("step", 0.01), ("tooltip", "How quickly bouncing dies away (1: no overshoot).")));

    /// <summary>A gas strut: it rides half way down its travel whatever it carries, so its arm sits level (up to its load).</summary>
    private static PyDict Spring(Size s, PyDict? path = null)
    {
        var d = Kw(("travel", s.Travel), ("frequency", s.Hz), ("damping", s.Damp), ("maxLoad", s.Load), ("ride", Ride));
        if (s.Slew != 0)
            d["slew"] = s.Slew;
        if (path is not null)
            d["path"] = path;
        return d;
    }

    private static string NameOf(string size, string kind) => size.Length > 0 ? $"{size} {kind}" : kind;

    private static (Part P, Cls C, double H, double Cell) Start(Size size, string kind)
    {
        var (verb, m, color) = Kinds[kind];
        var cls = size.C;
        var p = new Part(NameOf(size.Word, kind), cls, MenuSection, $"{verb}. {size.Wheels} wheels, {Tonnes(size.Load)}.", role: "suspension", tool: "wrench", color: color, group: MenuGroup);
        var mass = size.Kg * m;
        p.Stats(mass, Math.Max(60, Health(mass / 1000) * (cls == Small ? 0.5 : 1)), cls == Small ? 3 : cls.Armour, cls == Small ? 1.5 : null);
        p.Set(("config", Config()), ("collider", "boxes")); // (its round pieces are rigged: they move with the wheel, and its solid boxes stand for the part)
        return (p, cls, cls.Half, cls.Cell);
    }

    /// <summary>The cell the wheel bolts to: hidden and solid (the knuckle over it moves).</summary>
    private static void HubCell(Part p, double h, double x, double z) => p.Add(Box([x - h, -h, z - h], [x + h, h, z + h], hidden: true));

    private static PyDict Cyl(string front) => Kw(("shape", "cylinder"), ("front", front));

    private static void Rigged(Part p, PyDict? b, params (string, object?)[] rig)
    {
        b!.Update(rig);
        p.Add(b);
    }

    /// <summary>Where a half shaft goes in: a stub on the hub, moving with it, from the hidden cell's face (the port) to the hub drawn.</summary>
    private static void PortStub(Part p, double h, double x0, double x1, double y, double z)
    {
        var r = 0.3 * h;
        Rigged(p, Box([x0, y - r, z - r], [x1, y + r, z + r], Steel, detail: true, shape: Cyl("+x")), ("rig", "hub"));
    }

    /// <summary>A damper standing on the arm: a rod with a sleeve round its top half.</summary>
    private static void Leg(Part p, double h, double x, double y0, double y1, List<object?> pivot)
    {
        foreach (var (r, color, span) in new[] { (0.12, Steel, Py.L(0, 1)), (0.26, Coil, Py.L(0, 0.6)) })
        {
            var a = r * h;
            Rigged(p, Box([x - a, y1, -a], [x + a, y0, a], color, detail: true, shape: Cyl("+y")),
                ("rig", "leg"), ("from", Py.L(x, y0, 0)), ("to", Py.L(x, y1, 0)), ("span", span), ("pivot", pivot));
        }
    }

    private static Part Pogo(Size size)
    {
        var (p, c, h, cell) = Start(size, "Pogo Strut");
        var travel = size.Travel;
        var top = c.Hi(size.Strut);
        var cap = top - cell; // the cap's underside
        var knuckle = 0.7 * h;
        var tube = knuckle + travel / 2 + 0.05 * h; // (the tube's bottom clears the knuckle at the top of its travel)
        if (!(cap - tube > 0.04))
            throw new InvalidOperationException(size.Word);
        var rod = Math.Min(cap, tube + travel / 2 + 0.2 * h);
        HubCell(p, h, 0, 0);
        p.Add(Box([-0.2 * h, h, -0.2 * h], [0.2 * h, cap, 0.2 * h], hidden: true));
        p.Add(Box([-h, cap, -0.8 * h], [h, top, 0.8 * h], Dark));
        p.Add(Box([-0.42 * h, tube, -0.42 * h], [0.42 * h, cap + 0.02 * h, 0.42 * h], null, detail: true, shape: Cyl("+y")));
        Rigged(p, Box([-0.2 * h, 0, -0.2 * h], [0.2 * h, rod, 0.2 * h], Steel, detail: true, shape: Cyl("+y")), ("rig", "hub"));
        Rigged(p, Box([-0.65 * h, -knuckle, -0.6 * h], [h, knuckle, 0.6 * h], Dark, detail: true), ("rig", "hub"));
        // The coil: rings that close up as the strut does.
        var rings = 4;
        for (var k = 0; k < rings; k++)
        {
            var s = 0.08 + 0.84 * k / rings;
            var a = 0.66 * h;
            Rigged(p, Box([-a, -0.1 * h, -a], [a, 0.1 * h, a], Coil, detail: true, shape: Cyl("+y")),
                ("rig", "leg"), ("from", Py.L(0, cap, 0)), ("to", Py.L(0, knuckle, 0)), ("span", Py.L(Py.Round(s, 3), Py.Round(s + 0.07, 3))));
        }
        p.Sock("mount", [0, top, 0], "+y");
        p.Sock("side", [-h, top - cell / 2, 0], "-x");
        p.Sock("inner", [-h, 0, 0], "-x"); // (onto a differential's end or a steering pivot's leaf)
        p.Sock("axle", [h, 0, 0], "+x", kind: "axle");
        PortStub(p, h, -h, -0.6 * h, 0, 0);
        DriveIn(p, [-h, 0, 0]);
        p.Set(("suspension", Spring(size, Kw(("kind", "line")))));
        return p;
    }

    private static Part Swing(Size size)
    {
        var (p, c, h, cell) = Start(size, "Swing Arm");
        var x = (size.Arm - 1) * cell;
        var high = cell;
        var top = c.Hi(2);
        var at = 0.65 * x;
        HubCell(p, h, x, 0);
        p.Add(Box([0.4 * h, -0.3 * h, -0.3 * h], [x - h + 0.01 * h, 0.3 * h, 0.3 * h], hidden: true));
        p.Add(Box([-h, -h, -h], [0.4 * h, top, h], Dark));
        Rigged(p, Box([0, -0.28 * h, -0.4 * h], [x, 0.28 * h, 0.4 * h], null, detail: true), ("rig", "arm"), ("pivot", Py.L(0, 0, 0)));
        Rigged(p, Box([x - 0.55 * h, -0.7 * h, -0.6 * h], [x + h, 0.7 * h, 0.6 * h], Dark, detail: true), ("rig", "hub"));
        p.Add(Box([0.4 * h, high - 0.3 * h, -0.3 * h], [at + 0.32 * h, high + 0.3 * h, 0.3 * h], Dark, detail: true));
        Leg(p, h, at, high - 0.3 * h, 0.25 * h, Py.L(0, 0, 0));
        p.Sock("mount", [-h, 0, 0], "-x");
        p.Sock("mount-high", [-h, cell, 0], "-x");
        p.Sock("top", [0, top, 0], "+y");
        p.Sock("axle", [x + h, 0, 0], "+x", kind: "axle");
        PortStub(p, h, x - h, x - 0.5 * h, UnderArm * h, 0);
        DriveIn(p, [x - h, UnderArm * h, 0]);
        p.Set(("suspension", Spring(size, Kw(("kind", "arc"), ("pivot", Py.L(0, 0, 0)), ("axis", "+z"), ("tilt", 1)))));
        return p;
    }

    private static Part Wishbone(Size size)
    {
        var (p, c, h, cell) = Start(size, "Double Wishbone");
        var x = (size.Arm - 1) * cell;
        var top = c.Hi(3);
        var high = 2 * cell;
        var at = 0.65 * x;
        HubCell(p, h, x, 0);
        p.Add(Box([0.4 * h, -0.2 * h, -0.2 * h], [x - h + 0.01 * h, 0.2 * h, 0.2 * h], hidden: true));
        p.Add(Box([-h, -h, -h], [0.4 * h, top, h], Dark));
        foreach (var yo in new object[] { 0, cell })
        {
            var y = Py.D(yo);
            foreach (var z in new[] { -0.6 * h, 0.6 * h })
                Rigged(p, Box([0, y - 0.16 * h, z - 0.13 * h], [x, y + 0.16 * h, z + 0.13 * h], null, detail: true), ("rig", "arm"), ("pivot", Py.L(0, yo, 0)));
        }
        Rigged(p, Box([x - 0.3 * h, -0.7 * h, -0.55 * h], [x + 0.3 * h, cell + 0.4 * h, 0.55 * h], Dark, detail: true), ("rig", "hub"));
        Rigged(p, Box([x + 0.28 * h, -0.5 * h, -0.45 * h], [x + h, 0.5 * h, 0.45 * h], Steel, detail: true), ("rig", "hub"));
        p.Add(Box([0.4 * h, high - 0.3 * h, -0.3 * h], [at + 0.32 * h, high + 0.3 * h, 0.3 * h], Dark, detail: true));
        Leg(p, h, at, high - 0.3 * h, 0.2 * h, Py.L(0, 0, 0));
        p.Sock("mount", [-h, 0, 0], "-x");
        p.Sock("mount-mid", [-h, cell, 0], "-x");
        p.Sock("mount-high", [-h, 2 * cell, 0], "-x");
        p.Sock("top", [0, top, 0], "+y");
        p.Sock("axle", [x + h, 0, 0], "+x", kind: "axle");
        PortStub(p, h, x - h, x - 0.25 * h, UnderArm * h, 0);
        DriveIn(p, [x - h, UnderArm * h, 0]);
        p.Set(("suspension", Spring(size, Kw(("kind", "arc"), ("pivot", Py.L(0, 0, 0)), ("axis", "+z"), ("length", x), ("tilt", 0)))));
        return p;
    }

    /// <summary>Mirrored top to bottom, so 180 degrees about its socket (either side of the car) gives the same arm the other way along.</summary>
    private static Part Trailing(Size size)
    {
        var (p, c, h, cell) = Start(size, "Trailing Arm");
        var z = (size.Arm - 1) * cell;
        HubCell(p, h, 0, z);
        p.Add(Box([-0.3 * h, -0.3 * h, 0.8 * h], [0.3 * h, 0.3 * h, z - h + 0.01 * h], hidden: true));
        p.Add(Box([-h, -h, -h], [-0.1 * h, h, 0.8 * h], Dark));
        Rigged(p, Box([-0.1 * h, -0.42 * h, 0], [0.42 * h, 0.42 * h, z], null, detail: true), ("rig", "arm"), ("pivot", Py.L(0, 0, 0)));
        Rigged(p, Box([-0.12 * h, -0.65 * h, -0.65 * h], [0.5 * h, 0.65 * h, 0.65 * h], Steel, detail: true, shape: Cyl("+x")), ("rig", "arm"), ("pivot", Py.L(0, 0, 0)));
        Rigged(p, Box([-0.3 * h, -0.65 * h, z - 0.65 * h], [h, 0.65 * h, z + 0.65 * h], Dark, detail: true), ("rig", "hub"));
        p.Sock("mount", [-h, 0, 0], "-x");
        p.Sock("top", [-0.5 * h, h, 0], "+y");
        p.Sock("bottom", [-0.5 * h, -h, 0], "-y");
        p.Sock("axle", [h, 0, z], "+x", kind: "axle");
        PortStub(p, h, -h, -0.25 * h, 0, z);
        DriveIn(p, [-h, 0, z]);
        p.Set(("suspension", Spring(size, Kw(("kind", "arc"), ("pivot", Py.L(0, 0, 0)), ("axis", "+x"), ("tilt", 0)))));
        return p;
    }

    /// <summary>The plain bracket's missing sizes (Suspension and the Medium, Large and Capital ones are older parts, kept as they are).</summary>
    private static Part Plain(Size size)
    {
        var cls = size.C;
        var s = Kw(("travel", size.Travel), ("frequency", size.Hz), ("damping", size.Damp), ("maxLoad", size.Load));
        var p = Mount(cls, $"{size.Word} Suspension", MenuSection, $"Sprung bracket. {size.Wheels} wheels, {Tonnes(size.Load)}.", "#b08a2e", size.Kg / 1000.0, s);
        p.D["group"] = MenuGroup;
        p.Stats(size.Kg, Math.Max(60, Health(size.Kg / 1000.0) * 0.5), 3, 1.5);
        p.Set(("config", Config()));
        DriveIn(p, [-cls.Half, 0, 0]);
        return p;
    }

    // The demo buggies: the Buggy blueprint on each kind. name, part, the socket it bolts on by, a left one's spin.
    private static readonly (string Name, string Part, string By, int Left)[] Demos =
    [
        ("Pogo Buggy", "Pogo Strut", "inner", 0),
        ("Swing Arm Buggy", "Swing Arm", "mount", 0),
        ("Wishbone Buggy", "Double Wishbone", "mount", 0),
        ("Trailing Arm Buggy", "Trailing Arm", "mount", 3), // (rolled over, so its hub trails on the left too)
    ];

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        parts.AddRange(Sizes.Select(s => ("PogoStruts", Pogo(s))));
        parts.AddRange(Sizes.Select(s => ("SwingArms", Swing(s))));
        parts.AddRange(Sizes.Select(s => ("DoubleWishbones", Wishbone(s))));
        parts.AddRange(Sizes.Select(s => ("TrailingArms", Trailing(s))));
        parts.AddRange(Sizes.Where(s => s.Word is "Kart" or "Light" or "Heavy" or "Truck").Select(s => ("Plain", Plain(s))));
        WriteParts(o, Out, parts);
        foreach (var (name, part, by, left) in Demos)
        {
            var d = (PyDict)o.ReadJson("Constructs/Blueprints/Buggy.json")!; // a fresh copy each time
            d["name"] = name;
            d["description"] = $"The Buggy on {part}s.";
            var steps = ((List<object?>)d["steps"]!).Cast<PyDict>().ToList();
            for (var i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if ((string?)s["part"] != "Suspension")
                    continue;
                s["part"] = part;
                s["by"] = by;
                if (left != 0 && LeftSteps.Contains(i))
                    s["spin"] = left;
            }
            o.File("Constructs/Blueprints/" + FileName(name), PyJson.Dumps(d, 2) + "\n");
        }
    }
}
