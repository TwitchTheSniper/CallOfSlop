using System.Globalization;
using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.BigDriveParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The lift parts: Constructs/Parts/Air and the demo airships (docs/sandbox/CONSTRUCTS.md, "Air and lift"). Was tools/constructs/air_parts.py.
/// Lift cells are Tank Volumes whose box is a solid envelope, soft or armoured, in four sizes, and two rigid Vacuum Cells. Burners heat the
/// cell up their flame. Recipes come from tools/constructs/assembling.py. Airship is shared with FlightParts.
/// </summary>
public sealed class AirParts : IPartGenerator
{
    public string Name => "air_parts";

    private const string Out = "Constructs/Parts/Air";
    private const string Section = "Lift cells";
    private const string Burners = "Burners";
    private const string Soft = "#c9c2ae", Armoured = "#6b7178", Rigid = "#8c9299", BandC = "#4a4f55", Flame = "#d9682e";
    // (cls, name prefix, cells a side of its block, the longest side of its box in metres)
    private static readonly (Cls C, string Prefix, int N, int Side)[] Sizes = [(Small, "", 2, 40), (Medium, "Medium ", 1, 80), (Large, "Large ", 1, 160), (Capital, "Capital ", 1, 320)];
    private const double SoftSkin = 0.25; // kg/m2
    private const double ArmourSkin = 1.6;
    private const double VacuumSkin = 0.9;
    private const int SoftArmour = 2;
    private const int HardArmour = 40; // stops anything up to this much damage a hit
    // Burners: (cls, name, cells a side, mass kg, m3 of cell it keeps at full heat, l/s)
    private static readonly (Cls C, string Name, int N, int Mass, long Heat, object Rate)[] BurnerSizes =
    [
        (Small, "Balloon Burner", 2, 18, 8000, 0.5),
        (Medium, "Medium Burner", 1, 240, 60000, 5),
        (Large, "Large Burner", 1, 2400, 800000, 50),
        (Capital, "Capital Burner", 1, 24000, 12000000, 500),
    ];

    private static PyDict Setting(string key, string label, string group, string unit, object lo, object hi, object step, object def, string tip) =>
        Kw(("key", key), ("label", label), ("group", group), ("unit", unit), ("min", lo), ("max", hi), ("step", step), ("default", def), ("tooltip", tip));

    private static List<object?> Config(int side, double y) => Py.L(
        Setting("sizeX", "Width", "Box", "m", 0.2, side, 0.1, 6, "The cell's size across the block (its x)."),
        Setting("sizeY", "Height", "Box", "m", 0.2, side, 0.1, 6, "The cell's size up from the block (its y)."),
        Setting("sizeZ", "Length", "Box", "m", 0.2, side, 0.1, 6, "The cell's size along the block (its z)."),
        Setting("atX", "Centre x", "Box", "m", -side, side, 0.05, 0, "Where the cell's centre is from the block (x)."),
        Setting("atY", "Centre y", "Box", "m", -side, side, 0.05, y, "Where the cell's centre is from the block (y; above it by default)."),
        Setting("atZ", "Centre z", "Box", "m", -side, side, 0.05, 0, "Where the cell's centre is from the block (z)."),
        Kw(("key", "fluid"), ("label", "Gas"), ("group", "Contents"), ("creative", true), ("tooltip", "What the cell holds. Empty, a pipe's gas or a burner's heat fills it.")),
        Kw(("key", "fill"), ("label", "Fill"), ("group", "Contents"), ("unit", "%"), ("min", 0), ("max", 100), ("default", 0), ("creative", true), ("tooltip", "How full, or for hot air how hot.")),
        Kw(("key", "hole"), ("hidden", true), ("creative", true), ("unit", "m2"), ("min", 0), ("max", 10000), ("default", 0)));

    private static Part Cell(Cls c, string prefix, int n, int side, string kind)
    {
        var (colour, skin, armour, what) = kind switch
        {
            "soft" => (Soft, SoftSkin, SoftArmour, "A soft bag you size. Hydrogen, helium or a burner's hot air. Any hit makes a hole."),
            "armoured" => (Armoured, ArmourSkin, HardArmour, "A plated bag you size: small rounds stop at it, and it weighs six times a soft one."),
            _ => (Rigid, VacuumSkin, 8, "A rigid shell its pump empties on power. The best lift of all, and the first hole collapses it."),
        };
        var name = kind switch { "soft" => $"{prefix}Lift Cell", "armoured" => $"Armoured {prefix}Lift Cell", _ => $"{prefix}Vacuum Cell" };
        var p = new Part(name, c, Section, what, role: "fluidtank", tool: "wrench", color: colour, material: kind == "soft" ? "cloth" : "steel", group: c == Small ? "Small" : null);
        Block(p, n, n, n, BandC);
        var x = Half(n, c);
        p.Add(Box([x, -0.25 * x, -0.25 * x], [x * 1.2, 0.25 * x, 0.25 * x], "#c8863a", detail: true));
        p.Add(Box([-x * 1.2, -0.25 * x, -0.25 * x], [-x, 0.25 * x, 0.25 * x], "#4f9f5a", detail: true));
        Faces(p, n, n, n);
        var mass = c == Small ? 20 : c == Medium ? 120 : c == Large ? 900 : 7000;
        p.Stats(mass, Health(mass / 1000.0) * (kind == "armoured" ? 2 : 1) + 300, armour, 2.0);
        var env = Kw(("skin", skin), ("side", side), ("patch", 0.5 * c.X));
        if (kind == "vacuum")
        {
            env["vacuum"] = true;
            p.D["power"] = 40.0 * c.X * c.X;
        }
        p.Set(("fluidTank", Kw(("volume", true), ("flow", 200 * c.X * c.X), ("envelope", env))), ("config", Config(side, Py.Round(3.0 + 2 * x, 2))), ("ports", Py.L(
            Kw(("name", "Fluid out"), ("type", "pipe"), ("dir", "out"), ("at", Py.L(x, 0, 0)), ("face", "+x")),
            Kw(("name", "Fluid in"), ("type", "pipe"), ("dir", "in"), ("at", Py.L(-x, 0, 0)), ("face", "-x")))));
        return p;
    }

    private static Part Burner(Cls c, string name, int n, int mass, long heat, object rate)
    {
        var p = new Part(name, c, Burners, $"Keeps a cell of {heat.ToString("N0", CultureInfo.InvariantCulture)} m3 at full heat, up its flame, on {Py.G(Py.D(rate))} l/s of fuel. Wire its throttle or a height.",
            role: "burner", tool: "wrench", color: "#5c6066", group: c == Small ? "Small" : null);
        Block(p, n, n, n);
        var x = Half(n, c);
        p.Add(Box([-0.5 * x, x, -0.5 * x], [0.5 * x, 1.5 * x, 0.5 * x], Flame, detail: true));
        Bands(p, n, n, n, BandC, 1);
        Faces(p, n, n, n, "+y");
        p.Stats(mass, Health(mass / 1000.0), c.Armour / 2, 2.0);
        p.Set(("burner", Kw(("heat", heat), ("burnRate", rate), ("at", Py.L(0, Py.Round(1.5 * x, 3), 0)), ("reach", 12 * c.X))), ("ports", Py.L(
            Kw(("name", "Fuel in"), ("type", "pipe"), ("dir", "in"), ("fluid", "fuel"), ("at", Py.L(-x, 0, 0)), ("face", "-x")))));
        return p;
    }

    /// <summary>Between a bin of bulk gas and the pipes: a bin whose hydrogen or helium leaves by its pipe as gas.</summary>
    private static Part Compressor()
    {
        var p = new Part("Gas Compressor", Small, "Gas", "Takes hydrogen or helium by chute and sends it down its pipe as gas: a unit is 50 l, 50 m3 of cell.", role: "storage", tool: "wrench", color: "#5d6b4a", group: "Small");
        p.Add(Box([-0.4, 0, -0.4], [0.4, 0.8, 0.4]));
        p.Add(Box([0.4, 0.3, -0.08], [0.46, 0.46, 0.08], "#c8863a", detail: true));
        p.Add(Box([-0.25, 0.8, -0.25], [0.25, 0.86, 0.25], BandC, detail: true));
        foreach (var (d, at) in new (string, double[])[] { ("-y", [0, 0, 0]), ("+x", [0.4, 0.4, 0]), ("-x", [-0.4, 0.4, 0]), ("+z", [0, 0.4, 0.4]), ("-z", [0, 0.4, -0.4]) })
            p.Sock(d, at, d);
        p.Stats(60, 300, 5, 2.0);
        p.Set(("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", 200), ("min", Py.L(-0.3, 0.05, -0.3)), ("max", Py.L(0.3, 0.7, 0.3)))), ("gasLitres", 50))),
            ("ports", Py.L(Kw(("name", "Gas out"), ("type", "pipe"), ("dir", "out"), ("at", Py.L(0.4, 0.38, 0)), ("face", "+x")))));
        return p;
    }

    private const string Mast = "-y7";

    /// <summary>The demo: a tank with a seat on it, a cell's block on its side and the box above, a jet each side behind.</summary>
    public static PyDict Airship(string name, string about, string cell, (int X, int Y, int Z) size, string? gas = null, int fill = 0, string? burnerPart = null, string jets = "Light Jet", int masts = 1, string? ballast = null)
    {
        var (x, y, z) = size;
        object off = ballast is not null ? 0.7 : (object)0; // (the ballast hangs from its corner: the cell's lift goes over it)
        var cfg = Kw(("sizeX", x), ("sizeY", y), ("sizeZ", z), ("atX", 0.75 + Py.D(off)), ("atY", Py.Round(y / 2.0 + 3, 2)), ("atZ", off));
        if (gas is not null)
            cfg.Update(("fluid", gas), ("fill", fill));
        var steps = Py.L(
            Kw(("part", "Fuel Tank")),
            Kw(("part", "Seat"), ("on", 0), ("socket", "+y"), ("by", "bottom")),
            Kw(("part", cell), ("on", 0), ("socket", "-x"), ("by", "+x"), ("config", cfg)),
            Kw(("part", "Fuel Tank"), ("on", 0), ("socket", "+z"), ("by", "-z")),
            Kw(("part", "Fuel Tank"), ("on", 3), ("socket", "-x"), ("by", "+x")),
            Kw(("part", "Fuel Tank"), ("on", 3), ("socket", "+x"), ("by", "-x")),
            // Masts: the push acts level with the centre of mass, which the cell's skin raises, or it would tip her.
            Kw(("part", "Square Beam 2 m"), ("on", 4), ("socket", "+y"), ("by", "-z")),
            Kw(("part", "Square Beam 2 m"), ("on", 5), ("socket", "+y"), ("by", "-z")));
        for (var k = 1; k < masts; k++)
        {
            steps.Add(Kw(("part", "Square Beam 2 m"), ("on", 4 + 2 * k), ("socket", "+z"), ("by", "-z")));
            steps.Add(Kw(("part", "Square Beam 2 m"), ("on", 5 + 2 * k), ("socket", "+z"), ("by", "-z")));
        }
        var first = steps.Count;
        steps.Add(Kw(("part", jets), ("on", 4 + 2 * masts), ("socket", Mast), ("by", "front")));
        steps.Add(Kw(("part", jets), ("on", 5 + 2 * masts), ("socket", Mast), ("by", "front")));
        if (burnerPart is not null)
            steps.Add(Kw(("part", burnerPart), ("on", 0), ("socket", "+x"), ("by", "-x")));
        if (ballast is not null)
            steps.Add(Kw(("part", ballast), ("on", 0), ("socket", "-y"), ("by", "t0_0")));
        var links = Py.L(
            Kw(("type", "pipe"), ("from", 0), ("to", first), ("fromPort", "Fuel out"), ("toPort", "Fuel in")),
            Kw(("type", "pipe"), ("from", 0), ("to", first + 1), ("fromPort", "Fuel out"), ("toPort", "Fuel in")));
        return Kw(("format", "call-of-slop-construct-blueprint/1"), ("name", name), ("description", about), ("steps", steps), ("links", links));
    }

    private static List<(string File, PyDict B)> Blueprints() =>
    [
        ("HotAirBalloon", Airship("Hot Air Balloon", "A soft cell over a Balloon Burner. Shift raises the burner, Ctrl lowers it and then vents; wire its throttle or a height instead. Two jets push it.",
            "Lift Cell", (16, 18, 16), burnerPart: "Balloon Burner")),
        ("ScoutAirship", Airship("Scout Airship", "A soft helium cell and two jets. Any hit holes it: patch it with the attach tool. Creative comes filled; survival pipes gas in.",
            "Lift Cell", (10, 12, 24), "helium", 90, ballast: "Concrete Floor 2x2 m")),
        ("ArmouredAirship", Airship("Armoured Airship", "A plated helium cell that stops small rounds, at six times the skin's weight. Creative comes filled; survival pipes gas in.",
            "Armoured Lift Cell", (16, 16, 40), "helium", 80, masts: 3, ballast: "Concrete Floor 2x2 m")),
    ];

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)> { ("", Compressor()) };
        foreach (var (c, prefix, n, side) in Sizes)
        {
            parts.Add(("", Cell(c, prefix, n, side, "soft")));
            parts.Add(("", Cell(c, prefix, n, side, "armoured")));
            if (c == Small || c == Medium)
                parts.Add(("", Cell(c, prefix, n, side, "vacuum")));
        }
        foreach (var b in BurnerSizes)
            parts.Add(("", Burner(b.C, b.Name, b.N, b.Mass, b.Heat, b.Rate)));
        WriteParts(o, Out, parts);
        foreach (var (file, b) in Blueprints())
            o.File($"Constructs/Blueprints/{file}.json", PyJson.Dumps(b, 1) + "\n");
    }
}
