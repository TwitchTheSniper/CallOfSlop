using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.BigDriveParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Fluid Tanks: Constructs/Parts/FluidTanks (docs/sandbox/CONSTRUCTS.md, "Fluid tanks"). Was tools/constructs/fluid_tank_parts.py.
/// One part kind in the fuel tanks' six sizes, with the same bodies and capacities: a tank that holds any one fluid, a pipe's input and
/// output. And the Tank Volume, one block with a box the configure tool sizes. Recipes come from the part kit's assembling (--gen-parts assembling).
/// </summary>
public sealed class FluidTankParts : IPartGenerator
{
    public string Name => "fluid_tank_parts";

    private const string Out = "Constructs/Parts/FluidTanks";
    private const string Section = "Fluid tanks";
    private const string Body = "#3f6f86", BandC = "#2c5062", Steel = "#8a9099";
    private const string About = "A pipe's input and output for any one fluid: empty, it takes what arrives first.";

    private static List<object?> Config() => Py.L(
        Kw(("key", "fluid"), ("label", "Fluid"), ("group", "Contents"), ("creative", true), ("tooltip", "What the tank holds. An empty tank takes whatever a pipe brings first.")),
        Kw(("key", "fill"), ("label", "Fill"), ("group", "Contents"), ("unit", "%"), ("min", 0), ("max", 100), ("default", 0), ("creative", true), ("tooltip", "How full it is. Pipes and burners move this as they run.")));

    // Small sizes: (name, width, height, length, litres, mass kg, health, armour, litres a second through a pipe), the fuel tanks' bodies.
    private static readonly (string Name, double W, double H, double L, int Litres, int Mass, int Hp, int Armour, int Flow)[] TankSizes =
    [
        ("Small Fluid Tank", 0.4, 0.3, 0.4, 15, 14, 60, 1, 5),
        ("Fluid Tank", 0.5, 0.5, 0.5, 40, 30, 90, 2, 10),
        ("Large Fluid Tank", 0.5, 0.5, 1.0, 150, 70, 180, 4, 25),
    ];
    private static readonly Dictionary<Cls, int> Flow = new() { [Medium] = 500, [Large] = 5000, [Capital] = 20000 };
    private const int Side = 40; // a box's longest side, metres (ConstructStoreVolume.MaxSide)

    private static List<object?> BoxConfig() => Py.L(
        Kw(("key", "sizeX"), ("label", "Width"), ("group", "Box"), ("unit", "m"), ("min", 0.2), ("max", Side), ("step", 0.1), ("default", 2), ("tooltip", "The box's size across the block (its x).")),
        Kw(("key", "sizeY"), ("label", "Height"), ("group", "Box"), ("unit", "m"), ("min", 0.2), ("max", Side), ("step", 0.1), ("default", 2), ("tooltip", "The box's size up from the block (its y). The fluid rises along it.")),
        Kw(("key", "sizeZ"), ("label", "Length"), ("group", "Box"), ("unit", "m"), ("min", 0.2), ("max", Side), ("step", 0.1), ("default", 2), ("tooltip", "The box's size along the block (its z).")),
        Kw(("key", "atX"), ("label", "Centre x"), ("group", "Box"), ("unit", "m"), ("min", -Side), ("max", Side), ("step", 0.05), ("default", 0), ("tooltip", "Where the box's centre is from the block (x).")),
        Kw(("key", "atY"), ("label", "Centre y"), ("group", "Box"), ("unit", "m"), ("min", -Side), ("max", Side), ("step", 0.05), ("default", 1.2), ("tooltip", "Where the box's centre is from the block (y; above it by default).")),
        Kw(("key", "atZ"), ("label", "Centre z"), ("group", "Box"), ("unit", "m"), ("min", -Side), ("max", Side), ("step", 0.05), ("default", 0), ("tooltip", "Where the box's centre is from the block (z).")));

    /// <summary>Where the two pipes leave: a stub on the side (out) and one on the top (in), and a filler cap.</summary>
    private static void Stubs(Part p, double x, double y, double z, int unit)
    {
        var s = 0.05 * unit;
        var t = 0.05 * unit;
        p.Add(Box([x, -s, -s], [x + t, s, s], Steel, detail: true));
        p.Add(Box([-s, y, -s], [s, y + t, s], Steel, detail: true));
        p.Add(Box([0.4 * x - s, y, 0.6 * z - s], [0.4 * x + s, y + 0.8 * t, 0.6 * z + s], "#d8d8d8", detail: true));
    }

    /// <summary>A port for any fluid names none.</summary>
    private static List<object?> Ports(double x, double y) => Py.L(
        Kw(("name", "Fluid out"), ("type", "pipe"), ("dir", "out"), ("at", Py.L(x, 0, 0)), ("face", "+x")),
        Kw(("name", "Fluid in"), ("type", "pipe"), ("dir", "in"), ("at", Py.L(0, y, 0)), ("face", "+y")));

    private static void SixSockets(Part p, double x, double y, double z)
    {
        foreach (var (d, at) in new (string, double[])[] { ("+x", [x, 0, 0]), ("-x", [-x, 0, 0]), ("+y", [0, y, 0]), ("-y", [0, -y, 0]), ("+z", [0, 0, z]), ("-z", [0, 0, -z]) })
            p.Sock(d, at, d);
    }

    /// <summary>One block with the box's settings: it holds what the box holds, 1,000 l a cubic metre, and draws the fluid in the box.</summary>
    private static Part TankVolume()
    {
        var p = new Part("Tank Volume", Small, Section, "A box you size: one fluid, inside your walls. This block is its pipes' way in and out.", role: "fluidtank", tool: "wrench", color: Body, group: "Small");
        var h = 0.2;
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-h - 0.01, -h - 0.01, -0.03], [h + 0.01, h + 0.01, 0.03], BandC, detail: true));
        p.Add(Box([h, -0.07, -0.07], [h + 0.05, 0.07, 0.07], "#c8863a", detail: true));
        p.Add(Box([-h - 0.05, -0.07, -0.07], [-h, 0.07, 0.07], "#4f9f5a", detail: true));
        SixSockets(p, h, h, h);
        p.Stats(14, 400, 5, 2.0);
        p.Set(("fluidTank", Kw(("volume", true), ("flow", 5000))), ("config", (List<object?>)[.. BoxConfig(), .. Config()]), ("ports", Py.L(
            Kw(("name", "Fluid out"), ("type", "pipe"), ("dir", "out"), ("at", Py.L(h, 0, 0)), ("face", "+x")),
            Kw(("name", "Fluid in"), ("type", "pipe"), ("dir", "in"), ("at", Py.L(-h, 0, 0)), ("face", "-x")))));
        return p;
    }

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)> { ("", TankVolume()) };
        foreach (var (name, w, h, l, litres, mass, hp, armour, flow) in TankSizes)
        {
            var p = new Part(name, Small, Section, $"Holds {litres} l. {About}", role: "fluidtank", tool: "wrench", color: Body, group: "Small");
            double x = w / 2, y = h / 2, z = l / 2;
            p.Add(Box([-x, -y, -z], [x, y, z]));
            p.Add(Box([-x - 0.01, -y - 0.01, -0.03], [x + 0.01, y + 0.01, 0.03], BandC, detail: true));
            Stubs(p, x, y, z, 1);
            SixSockets(p, x, y, z);
            p.Stats(mass, hp, armour, 2.0);
            p.Set(("fluidTank", Kw(("capacity", litres), ("flow", flow))), ("ports", Ports(x, y)), ("config", Config()));
            parts.Add(("", p));
        }
        foreach (var c in new[] { Medium, Large, Capital })
        {
            var (n, t, litres) = Tanks[c];
            var p = new Part($"{c.Name} Fluid Bunker", c, Section, $"Holds {Py.G(litres / 1000.0)} kl. {About}", role: "fluidtank", tool: "wrench", color: Body);
            Block(p, n, n, n);
            Bands(p, n, n, n, BandC, 2);
            var x = Half(n, c);
            Stubs(p, x, x, x, c.X);
            Faces(p, n, n, n);
            p.Stats(t * 1000, Health(t), c.Armour / 2);
            p.Set(("fluidTank", Kw(("capacity", litres), ("flow", Flow[c]))), ("ports", Ports(x, x)), ("config", Config()));
            parts.Add(("", p));
        }
        WriteParts(o, Out, parts);
    }
}
