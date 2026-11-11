using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Walker stabilisers (docs/sandbox/GIANTS.md, phase 4): gyro housings that steady a walker's ride, Constructs/Parts/Stabilisers. Was
/// tools/constructs/stabiliser_parts.py.
///
/// A bare walker thumps, shakes and sways as it steps; each stabiliser on its body steadies "steady" kg of it, so the footfalls, the sway
/// and the tilt its riders feel, and how far loose parts on it are thrown, shrink with the count and size of them
/// (Source/Constructs/ConstructWalkFeel.cs). One size a class; the part is a base plate and a gyro drum on it, two cells across and one
/// high, with sockets under the base and on its sides.
/// </summary>
public sealed class StabiliserParts : IPartGenerator
{
    public string Name => "stabiliser_parts";

    private const string Out = "Constructs/Parts/Stabilisers";
    private const string Section = "Legs"; // (Constructs/Categories.json: Wheels and Suspension, Legs)

    // Class, kg of walker each one steadies (a Titan Walker of about 31,000 t is mostly steady on five Capital ones).
    private static readonly (Cls, int)[] Sizes = [(Small, 600), (Medium, 40_000), (Large, 1_500_000), (Capital, 6_000_000)];

    private static Part Stabiliser(Cls c, int steady)
    {
        var h = c.Half;
        var w = c.Cell; // (two cells across: -w..w; one high: -h..h)
        var p = new Part($"{c.Name} Stabiliser".Trim(), c, Section, "A gyro stabiliser: steadies a walker's ride. More and bigger ones take out more of the "
            + "footfalls' thump, the shake and the sway its riders feel, and keep loose cargo from being thrown about.", color: "#56606a", group: c == Small ? "Walkers" : null);
        var top = -h + h * 0.6;
        p.Add(Box([-w, -h, -w], [w, top, w])); // the base plate: solid
        var gap = 0.004 * c.X;
        p.Add(Box([-w * 0.62, top + gap, -w * 0.62], [w * 0.62, h, w * 0.62], "#2f3a44", detail: true)); // the gyro drum
        p.Add(Box([-w * 0.2, h + gap, -w * 0.2], [w * 0.2, h + h * 0.25, w * 0.2], "#c8a040", detail: true)); // its spindle cap
        p.Sock("-y", [0, -h, 0], "-y");
        var mid = (-h + top) / 2;
        foreach (var (name, at) in new (string, double[])[] { ("+x", [w, mid, 0]), ("-x", [-w, mid, 0]), ("+z", [0, mid, w]), ("-z", [0, mid, -w]) })
            p.Sock(name, at, name);
        p.Set(("stabiliser", Kw(("steady", steady))));
        return p.Stats(steady * 0.04, 2.5 * c.HealthM2 * Math.Pow(2 * w, 2), c.Armour);
    }

    public void Make(PartOutput o) => WriteParts(o, Out, Sizes.Select(s => ("", Stabiliser(s.Item1, s.Item2))));
}
