using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Call Centre's parts: Constructs/Parts/CallCentre (docs/vehiclekit/VEHICLEKIT.md, "The Call Centre style"). Was
/// tools/constructs/callcentre_parts.py.
///
/// The Scammers (docs/story/STORY.md, "The Scammers") run a call centre on wheels. Where bandits hang skulls they bolt on desk phones,
/// headsets on stands, GIFT CARDS signs and a prize wheel; loudhailers down the flanks as spikes; a phone mast on the roof. In their
/// rooms: cubicle desks (a drawer of loot in each) and a water cooler. Furniture stands on feet on the floor's quarter grid and faces
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

    private static Part Phone()
    {
        var p = new Part("Office Phone", Small, Section, "A desk phone on a post, every line blinking. Your call is important to them.", color: Black, group: Group);
        var top = 0.16;
        Stalk(p, top);
        p.Add(Box([-0.13, top, -0.1], [0.13, top + 0.06, 0.1], Black)); // (the base)
        p.Add(Box([-0.13, top + 0.062, 0.0], [0.13, top + 0.1, 0.1], Black)); // (the raised back, where the handset sits)
        p.Add(Box([-0.15, top + 0.102, 0.01], [0.15, top + 0.15, 0.08], "#34373b", detail: true)); // (the handset)
        for (var k = 0; k < 4; k++) // (the line buttons: all lit, all holding)
        {
            var x = -0.11 + k * 0.06;
            Lit(p, [x, top + 0.062, -0.08], [x + 0.04, top + 0.074, -0.04], k == 1 ? Red : Green);
        }
        p.Add(Box([0.155, top + 0.02, 0.03], [0.175, top + 0.12, 0.05], "#34373b", detail: true)); // (the curly cord, down its side)
        BaseSocket(p);
        p.Stats(3, 25, 0, 0.5);
        return p;
    }

    private static Part Headset()
    {
        var p = new Part("Call Headset", Small, Section, "A headset on a stand. The microphone has heard every script ever written.", color: Black, group: Group);
        var top = 0.2;
        Stalk(p, top, Black);
        p.Add(Box([-0.12, top, -0.02], [0.12, top + 0.03, 0.02], Grey)); // (the stand's bar)
        p.Add(Box([-0.12, top + 0.032, -0.025], [0.12, top + 0.25, 0.025], hidden: true));
        p.Add(Box([-0.12, top + 0.032, -0.025], [0.12, top + 0.25, 0.025], Black, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z")))); // (the band)
        foreach (var x in new[] { -0.15, 0.11 })
            p.Add(Box([x, top + 0.02, -0.06], [x + 0.04, top + 0.12, 0.06], Fabric, detail: true, shape: NoPaint)); // (the cups, outside the band)
        p.Add(Box([0.12, top + 0.04, -0.16], [0.135, top + 0.055, -0.06], Black, detail: true)); // (the boom)
        Lit(p, [0.115, top + 0.03, -0.19], [0.14, top + 0.065, -0.162], Red); // (the mic: always on)
        BaseSocket(p);
        p.Stats(2, 20, 0, 0.5);
        return p;
    }

    private static Part GiftSign()
    {
        var p = new Part("Gift Card Sign", Small, Section, "GIFT CARDS ACCEPTED. Gift cards preferred. Gift cards only, actually.", color: Pink, group: Group);
        var top = 0.3;
        Stalk(p, top, Black);
        p.Add(Box([-0.3, top, -0.03], [0.3, top + 0.3, 0.0], Pink)); // (the board)
        string[] cards = [Yellow, Green, Teal, White];
        for (var k = 0; k < cards.Length; k++) // (four cards, fanned across it, 4 mm proud)
        {
            var x = -0.26 + k * 0.13;
            p.Add(Box([x, top + 0.06 + (k % 2) * 0.03, -0.034], [x + 0.11, top + 0.2 + (k % 2) * 0.03, -0.03], cards[k], detail: true, shape: NoPaint));
        }
        p.Add(Box([-0.26, top + 0.235, -0.034], [0.26, top + 0.27, -0.03], Yellow, detail: true, shape: NoPaint)); // (the headline)
        BaseSocket(p);
        p.Stats(3, 25, 0, 0.5);
        return p;
    }

    private static Part PrizeWheel()
    {
        var p = new Part("Prize Wheel", Small, Section, "A wheel of prizes on a post. Every slice says WINNER. None of them are.", color: Yellow, group: Group);
        var top = 0.22;
        Stalk(p, top, Black);
        var r = 0.24;
        p.Add(Box([-r, top, -0.03], [r, top + 2 * r, 0.0], hidden: true));
        p.Add(Box([-r, top, -0.03], [r, top + 2 * r, 0.0], Yellow, detail: true, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z")))); // (the wheel: 12 sides, so it gets a hull; 16 keep their boxes)
        var cy = top + r;
        string[] slices = [Pink, Green, Teal, Red];
        for (var k = 0; k < slices.Length; k++) // (the slices: four bars across its face, 4 mm proud)
        {
            var y = cy - 0.15 + k * 0.08;
            p.Add(Box([-0.15 + Math.Abs(k - 1.5) * 0.03, y, -0.034], [0.15 - Math.Abs(k - 1.5) * 0.03, y + 0.06, -0.03], slices[k], detail: true, shape: NoPaint));
        }
        p.Add(Box([-0.03, cy - 0.03, -0.042], [0.03, cy + 0.03, -0.034], White, detail: true, shape: NoPaint)); // (the hub)
        p.Add(Box([-0.02, top + 2 * r + 0.004, -0.02], [0.02, top + 2 * r + 0.06, 0.0], Red, detail: true, shape: NoPaint)); // (the pointer)
        BaseSocket(p);
        p.Stats(4, 25, 0, 0.5);
        return p;
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

    private static Part Cubicle()
    {
        var p = new Part("Cubicle Desk", Small, "Furniture", "A cubicle: a desk, a phone, a headset and a script taped to the wall. A drawer of whatever they got today.",
            role: "storage", tool: "wrench", color: Fabric, group: "Vehicle Body");
        double w = 0.3, d = 0.25, top = 0.72; // (as narrow as a rack: a van's room fits a row of them)
        p.Add(Box([-w, top, -d], [w, top + 0.03, d], Desk)); // (the desk top)
        p.Add(Box([-w, 0, -d], [-w + 0.03, top - 0.002, d], Grey)); // (the left leg panel)
        // The drawer pedestal, on the right: open at the front, round the drawer's slot.
        var px = w - 0.3;
        p.Add(Box([px, 0.0, -d + 0.02], [px + 0.015, top - 0.002, d - 0.03], Grey));
        p.Add(Box([w - 0.015, 0.0, -d + 0.02], [w - 0.002, top - 0.002, d - 0.03], Grey));
        p.Add(Box([px + 0.017, 0.0, -d + 0.02], [w - 0.017, 0.05, d - 0.03], Grey));
        p.Add(Box([px + 0.017, 0.62, -d + 0.02], [w - 0.017, top - 0.002, d - 0.03], Grey));
        p.Add(Box([px + 0.017, 0.052, -d + 0.02], [w - 0.017, 0.618, -d + 0.035], "#6f7378"));
        p.Add(Box([-w, top + 0.032, -d], [w, 1.2, -d + 0.04], Fabric)); // (the partition behind it)
        p.Add(Box([-w, top + 0.032, -d + 0.042], [-w + 0.03, 1.05, d], Fabric)); // (the side partition)
        p.Add(Box([-0.16, top + 0.2, -d + 0.044], [0.04, top + 0.4, -d + 0.048], White, detail: true, shape: NoPaint)); // (the script, taped up)
        p.Add(Box([-0.25, top + 0.032, -0.16], [-0.1, top + 0.07, -0.04], Black, detail: true)); // (the phone)
        Lit(p, [-0.23, top + 0.072, -0.1], [-0.19, top + 0.08, -0.06], Red);
        p.Add(Box([0.0, top + 0.032, -0.2], [0.24, top + 0.22, -0.04], "#d8d0b8", detail: true)); // (a monitor, much like IT's)
        p.Add(Box([0.02, top + 0.055, -0.036], [0.22, top + 0.2, -0.032], "#1d2a33", detail: true, shape: NoPaint));
        p.Add(Box([-0.08, top + 0.032, 0.04], [0.18, top + 0.05, 0.16], "#cfc6aa", detail: true)); // (the keyboard)
        p.Add(Box([-0.26, top + 0.032, 0.06], [-0.18, top + 0.1, 0.14], Black, detail: true)); // (the headset, dropped on the desk)
        Feet(p, [-0.125, 0.125], [0.0]);
        p.Stats(26, 60, 1, 2.0);
        p.Set(("storage", Kw(("slots", Py.L(Kw(
            ("min", Py.L(Py.Round(px + 0.02, 3), 0.06, Py.Round(-d + 0.04, 3))),
            ("max", Py.L(Py.Round(w - 0.02, 3), 0.6, Py.Round(d - 0.05, 3))),
            ("rack", true), ("label", "drawer")))))));
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
        ("", Phone()), ("", Headset()), ("", GiftSign()), ("", PrizeWheel()), ("", Loudhailer()), ("", Mast()), ("", Cubicle()), ("", Cooler()),
    ]);
}
