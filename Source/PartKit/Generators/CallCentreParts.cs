using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Plain equipment: Constructs/Parts/CallCentre (docs/vehiclekit/VEHICLEKIT.md, "The Scam style"). Was tools/constructs/callcentre_parts.py.
///
/// Loudhailers (down the flanks as spikes), a phone mast for the roofs and a water cooler for the rooms. (The scam call centre's pieces,
/// desk phones, headsets, GIFT CARDS signs, the prize wheel and the cubicle desk, went with the old factions, 2026-11-17.) Furniture stands on feet on the floor's quarter grid and faces
/// its own +z, into the room, as OfficeParts' does. Each trophy stands on one "base" socket and points up its own +y, its face to -z,
/// so the kit turns it outward. Faces that meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces").
/// Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// </summary>
public sealed class CallCentreParts : IPartGenerator
{
    public string Name => "callcentre_parts";

    private const string Out = "Constructs/Parts/CallCentre";
    private const string Black = "#25272a", Grey = "#8e9296", Fabric = "#5f7f8f", Desk = "#b9b2a2", White = "#eeeeea";
    private const string Teal = "#1a8c8c", Yellow = "#ffd400", Pink = "#ff3d8b", Green = "#21c45a", Red = "#d8322a";
    private const string Water = "#7cc4f0";
    private const string Section = "Body panels";
    private const string Group = "Lights and Details";

    private static void BaseSocket(Part p) => p.Sock("base", [0, 0, 0], "-y");

    /// <summary>A square post from the base up to top, under whatever it holds.</summary>
    private static void Stalk(Part p, double top, string colour = Grey) => p.Add(Box([-0.02, 0, -0.02], [0.02, top, 0.02], colour));

    /// <summary>A small lamp on a face: unlit, bright, not paint.</summary>
    private static void Lit(Part p, double[] mn, double[] mx, string colour) =>
        p.Add(Box(mn, mx, colour, detail: true, shape: Kw(("paintable", false), ("glow", true))));

    private static PyDict NoPaint => Kw(("paintable", false));

    private static void Feet(Part p, double[] xs, double[] zs)
    {
        for (var i = 0; i < xs.Length; i++)
            for (var k = 0; k < zs.Length; k++)
                p.Sock($"foot{i}{k}", [xs[i], 0, zs[k]], "-y");
    }

    private static Part Loudhailer()
    {
        var p = new Part("Loudhailer", Small, Section, "A loudhailer on a post, for when nobody picks up: CONGRATULATIONS, YOU HAVE BEEN SELECTED.", color: White, group: Group);
        var top = 0.12;
        p.Add(Box([-0.05, 0, -0.04], [0.05, top, 0.04], Black)); // (its foot)
        p.Add(Box([-0.06, top, -0.05], [0.06, top + 0.1, 0.07], Grey)); // (the driver)
        p.Add(Box([-0.13, top - 0.06, -0.3], [0.13, top + 0.16, -0.05], hidden: true));
        p.Add(Box([-0.13, top - 0.06, -0.3], [0.13, top + 0.16, -0.05], White, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 12), ("taper", Py.L(0.35, 0.35)), ("front", "+z")))); // (the horn: wide at the mouth)
        BaseSocket(p);
        p.Stats(3, 25, 0, 0.5);
        return p;
    }

    private static Part Mast()
    {
        var p = new Part("Phone Mast", Small, Section, "A phone mast, bolted to the roof. Four bars of signal, all of it outgoing.", color: Grey, group: Group);
        var h = 0.9;
        p.Add(Box([-0.06, 0, -0.06], [0.06, 0.04, 0.06], Black)); // (its foot)
        p.Add(Box([-0.025, 0.04, -0.025], [0.025, h, 0.025], Grey)); // (the pole)
        foreach (var (x, z) in new[] { (0.0, -0.06), (0.06, 0.0), (0.0, 0.06), (-0.06, 0.0) }) // (the panel antennas round its top, clear of the pole)
        {
            var (sx, sz) = x == 0 ? (0.03, 0.012) : (0.012, 0.03);
            p.Add(Box([x - sx, h - 0.3, z - sz], [x + sx, h - 0.04, z + sz], White, detail: true));
        }
        Lit(p, [-0.02, h + 0.002, -0.02], [0.02, h + 0.04, 0.02], Red); // (the beacon on top)
        BaseSocket(p);
        p.Stats(5, 30, 1, 0.5);
        return p;
    }

    private static Part Cooler()
    {
        var p = new Part("Water Cooler", Small, "Furniture", "A water cooler. Where the team lead tells everyone today's numbers.", tool: "wrench", color: White, group: "Vehicle Body");
        p.Add(Box([-0.15, 0, -0.15], [0.15, 0.9, 0.15], White)); // (the cabinet)
        p.Add(Box([-0.06, 0.62, 0.152], [0.06, 0.7, 0.18], Grey, detail: true)); // (the taps' block, proud of its face)
        Lit(p, [-0.045, 0.66, 0.182], [-0.015, 0.68, 0.186], "#3aa0ff");
        Lit(p, [0.015, 0.66, 0.182], [0.045, 0.68, 0.186], Red);
        p.Add(Box([-0.12, 0.902, -0.12], [0.12, 1.32, 0.12], hidden: true));
        p.Add(Box([-0.12, 0.902, -0.12], [0.12, 1.32, 0.12], Water, detail: true, glass: true, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y"), ("paintable", false)))); // (the bottle)
        Feet(p, [0.0], [0.0]);
        p.Stats(20, 40, 0, 1.0);
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Loudhailer()), ("", Mast()), ("", Cooler()),
    ]);
}
