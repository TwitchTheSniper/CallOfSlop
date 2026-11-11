using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The IT Department's parts: Constructs/Parts/Office (docs/vehiclekit/VEHICLEKIT.md, "The IT style"). Was tools/constructs/office_parts.py.
///
/// What the IT Department bolts on where bandits hang skulls and spikes: a beige monitor on a stalk, a potted plant, a desk fan, an
/// Employee of the Month plaque, a satellite dish, a network switch and a cable reel as trophies, and router antennas down the flanks as
/// spikes. In its rooms: server racks (loot on their shelves), and the Computer and the Fax Machine, the only line to the IT Department
/// (docs/story/STORY.md, "The IT Department's fax"). Furniture stands on feet on the floor's quarter grid and faces its own +z, into
/// the room, as BodyParts' does. Each trophy stands on one "base" socket and points up its own +y, so the kit turns
/// it outward, as the bandit parts do (BanditParts.cs). Leaves and soil aren't paintable; the rest takes the style's paint. Faces that
/// meet are kept a few millimetres apart (CLAUDE.md, "No coplanar faces"). Recipes come from the part kit's assembling (--gen-parts assembling); run it after this one.
/// </summary>
public sealed class OfficeParts : IPartGenerator
{
    public string Name => "office_parts";

    private const string Out = "Constructs/Parts/Office";
    private const string Beige = "#d8d0b8", BeigeDark = "#b8ae94", Screen = "#1d2a33", Grey = "#8e9296", Dark = "#2c2e31";
    private const string Pot = "#b85c38", Soil = "#3a2a1e", Leaf = "#3f8a3a", LeafDark = "#2e6b2c";
    private const string Gold = "#c8a018", Blue = "#1f4e8c";
    private const string Section = "Body panels";
    private const string Group = "Lights and Details";
    private const string Office = "Office";
    private const string LedG = "#3fd04a", LedB = "#3aa0ff", LedA = "#f0a020", Paper = "#f2efe6", Cable = "#e86a1a";

    private static void BaseSocket(Part p) => p.Sock("base", [0, 0, 0], "-y");

    /// <summary>A square post from the base up to top, under whatever it holds.</summary>
    private static void Stalk(Part p, double top, string colour = Grey) => p.Add(Box([-0.02, 0, -0.02], [0.02, top, 0.02], colour));

    private static PyDict NoPaint => Kw(("paintable", false));

    private static PyDict Round(string shape, int segments, string? front = null, double? taper = null, bool paintable = true)
    {
        var d = Kw(("shape", shape), ("segments", segments));
        if (taper is { } t)
            d["taper"] = Py.L(t, t);
        if (front is not null)
            d["front"] = front;
        if (!paintable)
            d["paintable"] = false;
        return d;
    }

    private static Part Monitor()
    {
        var p = new Part("CRT Monitor", Small, Section, "A beige monitor on a pole. Still logged in to something important.", color: Beige, group: Group);
        var top = 0.18;
        Stalk(p, top);
        p.Add(Box([-0.07, top, -0.06], [0.07, top + 0.02, 0.06], BeigeDark)); // (the foot)
        p.Add(Box([-0.17, top + 0.02, -0.12], [0.17, top + 0.3, 0.14], Beige)); // (the case: deep, as they were)
        p.Add(Box([-0.14, top + 0.05, -0.124], [0.14, top + 0.27, -0.1], Screen, detail: true, shape: NoPaint)); // (the glass, 4 mm proud of the case's face)
        p.Add(Box([0.1, top + 0.03, -0.126], [0.13, top + 0.045, -0.11], "#3fd04a", detail: true, shape: NoPaint)); // (the power light)
        BaseSocket(p);
        p.Stats(9, 30, 1, 0.5);
        return p;
    }

    private static Part Plant()
    {
        var p = new Part("Office Plant", Small, Section, "A potted plant nobody waters. It has outlived three reorganisations.", color: Pot, group: Group);
        p.Add(Box([-0.11, 0, -0.11], [0.11, 0.22, 0.11], hidden: true));
        p.Add(Box([-0.11, 0, -0.11], [0.11, 0.22, 0.11], Pot, detail: true, shape: Round("cylinder", 12, "+y", 0.8)));
        p.Add(Box([-0.095, 0.2, -0.095], [0.095, 0.225, 0.095], Soil, detail: true, shape: Round("cylinder", 12, "+y", paintable: false)));
        p.Add(Box([-0.16, 0.24, -0.16], [0.16, 0.52, 0.16], Leaf, detail: true, shape: Round("sphere", 10, paintable: false)));
        p.Add(Box([-0.1, 0.44, -0.12], [0.12, 0.64, 0.1], LeafDark, detail: true, shape: Round("sphere", 8, paintable: false)));
        BaseSocket(p);
        p.Stats(5, 20, 0, 0.5);
        return p;
    }

    private static Part Fan()
    {
        var p = new Part("Desk Fan", Small, Section, "A desk fan on a stalk. Moves the air, not the deadline.", color: Beige, group: Group);
        var top = 0.22;
        Stalk(p, top);
        p.Add(Box([-0.05, top, -0.05], [0.05, top + 0.06, 0.06], BeigeDark)); // (the motor)
        p.Add(Box([-0.15, top - 0.09, -0.08], [0.15, top + 0.21, -0.05], Grey, detail: true, shape: Round("cylinder", 12, "-z"))); // (the guard: 12 sides, so it gets a hull; 16 keep their boxes)
        p.Add(Box([-0.03, top + 0.03, -0.085], [0.03, top + 0.09, -0.078], Dark, detail: true, shape: Round("cylinder", 8, "-z"))); // (the hub, in front of the guard)
        BaseSocket(p);
        p.Stats(3, 20, 0, 0.5);
        return p;
    }

    private static Part Plaque()
    {
        var p = new Part("Employee of the Month", Small, Section, "A gold plaque on a post. Everyone has had it twice.", color: Gold, group: Group);
        var top = 0.25;
        Stalk(p, top, Dark);
        p.Add(Box([-0.16, top, -0.03], [0.16, top + 0.22, 0.0], "#5a3a22")); // (the board)
        p.Add(Box([-0.12, top + 0.03, -0.034], [0.12, top + 0.19, -0.03], Gold, detail: true)); // (the plate, 4 mm proud)
        p.Add(Box([-0.08, top + 0.13, -0.038], [0.08, top + 0.16, -0.034], Blue, detail: true, shape: NoPaint)); // (the name, in the house blue)
        BaseSocket(p);
        p.Stats(3, 25, 1, 0.5);
        return p;
    }

    private static Part Antenna(string name, double length, string desc, double mass)
    {
        var p = new Part(name, Small, Section, desc, color: Dark, group: Group);
        var r = 0.016;
        p.Add(Box([-0.05, 0, -0.035], [0.05, 0.04, 0.035], Dark)); // (its foot)
        p.Add(Box([-r, 0.04, -r], [r, length, r], hidden: true));
        p.Add(Box([-r, 0.04, -r], [r, length, r], Dark, detail: true, shape: Round("cylinder", 8, "+y", 0.7)));
        p.Add(Box([-0.012, 0.012, -0.039], [0.012, 0.024, -0.035], "#3fd04a", detail: true, shape: NoPaint)); // (the link light, on the foot's face)
        BaseSocket(p);
        p.Stats(mass, 25, 1, 0.5);
        return p;
    }

    private static void Feet(Part p, double[] xs, double[] zs)
    {
        for (var i = 0; i < xs.Length; i++)
            for (var k = 0; k < zs.Length; k++)
                p.Sock($"foot{i}{k}", [xs[i], 0, zs[k]], "-y");
    }

    private static PyDict RackBox(double[] mn, double[] mx) =>
        Kw(("min", mn.Cast<object?>().ToList()), ("max", mx.Cast<object?>().ToList()), ("rack", true), ("label", "shelf"));

    /// <summary>A small lamp on a face: unlit, bright, not paint.</summary>
    private static void Lit(Part p, double[] mn, double[] mx, string colour) =>
        p.Add(Box(mn, mx, colour, detail: true, shape: Kw(("paintable", false), ("glow", true))));

    private static Part ServerRack()
    {
        var p = new Part("Server Rack", Small, "Furniture", "A rack of humming servers. Blinking means it works. Probably. Spare parts on the shelves.", role: "storage", tool: "wrench", color: Dark, group: "Vehicle Body");
        double w = 0.3, d = 0.275, h = 1.5;
        p.Add(Box([-w, 0, -d], [-w + 0.03, h, d])); // (the sides)
        p.Add(Box([w - 0.03, 0, -d], [w, h, d]));
        p.Add(Box([-w + 0.032, h - 0.03, -d], [w - 0.032, h, d])); // (the top)
        p.Add(Box([-w + 0.032, 0, -d], [w - 0.032, 0.08, d])); // (the plinth)
        (double, double)[] bands = [(0.08, 0.5), (0.53, 0.95), (0.98, 1.4)];
        for (var k = 0; k < bands.Length; k++)
        {
            var (y0, y1) = bands[k];
            p.Add(Box([-w + 0.034, y0 + 0.002, -d + 0.004], [w - 0.034, y1, -0.02], "#202225")); // (the servers, at the back)
            for (var j = 0; j < 3; j++) // (a row of lights on each server's face)
            {
                var yy = y0 + 0.06 + j * (y1 - y0 - 0.08) / 3;
                Lit(p, [-0.22, yy, -0.02], [-0.12, yy + 0.015, -0.006], (j + k) % 2 == 0 ? LedG : LedB);
                Lit(p, [0.14, yy, -0.02], [0.2, yy + 0.015, -0.006], j == 1 && k == 1 ? LedA : LedG);
            }
            if (k < 2)
                p.Add(Box([-w + 0.034, y1, -0.018], [w - 0.034, y1 + 0.03, d - 0.004], Grey)); // (a shelf in front)
        }
        p.Add(Box([-w + 0.034, 1.4, -d + 0.004], [w - 0.034, h - 0.032, d - 0.004], Blue, detail: true)); // (the badge band)
        Feet(p, [-0.125, 0.125], [-0.125, 0.125]);
        p.Stats(38, 180, 3, 2.0);
        p.Set(("storage", Kw(("slots", Py.L(RackBox([-0.26, 0.08, 0.0], [0.26, 0.5, 0.26]), RackBox([-0.26, 0.56, 0.0], [0.26, 0.95, 0.26]), RackBox([-0.26, 1.01, 0.0], [0.26, 1.4, 0.26]))))));
        return p;
    }

    // The adverts on a computer's screen (OfficeParts.FillComputer picks their words; Story/Office/Scammers.json): two banners under the text.
    private static List<object?> Adverts() => Py.L(
        Kw(("action", "office.advert"), ("at", Py.L(0.02, 0.2, 0.98, 0.36)), ("label", "AD")),
        Kw(("action", "office.advert2"), ("at", Py.L(0.02, 0.03, 0.98, 0.19)), ("label", "AD")));

    private static Part Computer()
    {
        var p = new Part("Computer", Small, Office, "A beige computer on a desk. Plug it into a battery's circuit, stand at it and type in chat: it emails the IT Department. Don't click the adverts.", role: "panel", tool: "wrench", color: Beige, group: "Logic");
        var top = 0.74;
        p.Add(Box([-0.4, top, -0.3], [0.4, top + 0.03, 0.3], "#6e6a60")); // (the desk top)
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x = sx * 0.36, z = sz * 0.26;
                p.Add(Box([x - 0.025, 0, z - 0.025], [x + 0.025, top - 0.002, z + 0.025], Dark));
            }
        var t = top + 0.03;
        p.Add(Box([-0.07, t, -0.2], [0.07, t + 0.02, -0.08], BeigeDark)); // (the monitor's foot)
        p.Add(Box([-0.2, t + 0.02, -0.28], [0.2, t + 0.34, 0.04], Beige)); // (the monitor: deep, as they were)
        p.Add(Box([-0.17, t + 0.05, 0.04], [0.17, t + 0.31, 0.052], Screen, detail: true, shape: NoPaint)); // (the glass)
        Lit(p, [0.175, t + 0.025, 0.04], [0.19, t + 0.04, 0.054], LedG); // (its power light)
        p.Add(Box([-0.21, t, 0.1], [0.21, t + 0.025, 0.25], BeigeDark)); // (the keyboard)
        p.Add(Box([-0.19, t + 0.025, 0.115], [0.19, t + 0.04, 0.235], Beige, detail: true)); // (its keys)
        p.Add(Box([0.25, t, 0.14], [0.31, t + 0.025, 0.22], BeigeDark, detail: true)); // (the mouse)
        p.Add(Box([0.14, 0.03, -0.22], [0.3, 0.5, 0.2], Beige)); // (the tower, under the desk)
        p.Add(Box([0.16, 0.36, 0.2], [0.28, 0.4, 0.212], Dark, detail: true)); // (its floppy drive)
        Lit(p, [0.25, 0.44, 0.2], [0.27, 0.46, 0.212], LedG);
        p.Add(Box([0.2, 0.5, -0.14], [0.24, top - 0.002, -0.1], Dark, detail: true)); // (the cable up to the desk)
        Feet(p, [-0.25, 0.25], [0.0]);
        p.Stats(22, 60, 1, 2.0);
        p.Set(("power", 60), ("office", "computer"), ("centreSockets", false),
            ("panels", Py.L(Kw(("source", "office.computer"), ("at", Py.L(0, Py.Round(t + 0.18, 3), 0.056)), ("size", Py.L(0.33, 0.25)), ("face", "+z"), ("up", "+y"), ("rows", 8), ("range", 10), ("touch", Adverts())))));
        return p;
    }

    private static Part Fax()
    {
        var p = new Part("Fax Machine", Small, Office, "A fax on a cabinet. The only number the IT Department answers. Plug it into a battery's circuit and wait.", role: "panel", tool: "wrench", color: Beige, group: "Logic");
        double w = 0.3, d = 0.225, h = 0.7;
        p.Add(Box([-w, 0, -d], [w, h, d], Grey)); // (the cabinet)
        p.Add(Box([-0.26, 0.06, d], [0.26, 0.66, d + 0.012], Paper, detail: true, shape: NoPaint)); // (the last page, taped to its front)
        p.Add(Box([-0.22, h + 0.002, -0.18], [0.22, h + 0.14, 0.17], Beige)); // (the fax)
        p.Add(Box([-0.22, h + 0.14, -0.18], [-0.1, h + 0.18, 0.12], Dark)); // (the handset)
        p.Add(Box([-0.06, h + 0.14, -0.17], [0.18, h + 0.26, -0.15], Paper, detail: true, shape: NoPaint)); // (paper in the feeder)
        p.Add(Box([-0.08, h + 0.03, 0.17], [0.18, h + 0.045, 0.3], Paper, detail: true, shape: NoPaint)); // (a page coming out)
        p.Add(Box([0.0, h + 0.07, 0.17], [0.16, h + 0.11, 0.182], Screen, detail: true, shape: NoPaint)); // (its little screen)
        Lit(p, [0.18, h + 0.08, 0.17], [0.2, h + 0.1, 0.182], LedG);
        Feet(p, [-0.125, 0.125], [0.0]);
        p.Stats(14, 50, 1, 2.0);
        p.Set(("power", 40), ("office", "fax"), ("centreSockets", false),
            ("panels", Py.L(Kw(("source", "office.fax"), ("at", Py.L(0, 0.36, Py.Round(d + 0.016, 3))), ("size", Py.L(0.5, 0.56)), ("face", "+z"), ("up", "+y"), ("rows", 6), ("range", 8)))));
        return p;
    }

    /// <summary>
    /// The terminal in a sealed data centre (docs/story/FRAGMENTS.md; Game/GameStoryFragments.cs): a logic part, its kind "cipher".
    /// It needs power (a battery on its construct's circuit), then puts out a carrier number and waits for the answer its screen asks
    /// for on its key input, which a player builds out of logic blocks. Heavy and welder-fitted, so it isn't carried off.
    /// </summary>
    private static Part Terminal()
    {
        var p = new Part("Data Centre Terminal", Small, Office, "A sealed terminal older than anyone's records. Power it, then wire it the answer its screen asks for.", role: "logic", tool: "welder", color: Dark, group: "Logic");
        double w = 0.45, d = 0.3, h = 1.3;
        p.Add(Box([-w, 0, -d], [w, h, d], Dark)); // (the cabinet)
        p.Add(Box([-0.4, 0.66, d + 0.004], [0.4, 1.16, d + 0.02], Screen, detail: true, shape: NoPaint)); // (the glass, 4 mm proud)
        p.Add(Box([-0.4, 0.2, d + 0.004], [0.4, 0.5, d + 0.016], Grey, detail: true)); // (the key-in panel)
        for (var k = 0; k < 4; k++) // (dead lamps over the screen)
        {
            var x = -0.3 + k * 0.2;
            Lit(p, [x - 0.03, 1.2, d + 0.004], [x + 0.03, 1.24, d + 0.016], k == 0 ? LedA : LedG);
        }
        p.Add(Box([-0.06, 0.3, d + 0.018], [0.06, 0.4, d + 0.034], "#c0392b", detail: true, shape: NoPaint)); // (the key socket)
        p.Add(Box([-w + 0.04, h + 0.002, -d + 0.04], [w - 0.04, h + 0.06, d - 0.04], Grey)); // (the vent on top)
        Feet(p, [-0.25, 0.25], [0.0]);
        p.Stats(160, 600, 4, 4.0);
        p.Set(("power", 120), ("logic", Kw(("kind", "cipher"))), ("centreSockets", false),
            ("panels", Py.L(Kw(("source", "story.terminal"), ("at", Py.L(0, 0.91, Py.Round(d + 0.024, 3))), ("size", Py.L(0.78, 0.48)), ("face", "+z"), ("up", "+y"), ("rows", 6), ("range", 14)))));
        return p;
    }

    private static Part Switch()
    {
        var p = new Part("Network Switch", Small, Section, "A network switch on a post, cables hanging out of it. Do not unplug the yellow one.", color: Dark, group: Group);
        var top = 0.14;
        Stalk(p, top);
        p.Add(Box([-0.2, top, -0.08], [0.2, top + 0.06, 0.08], Dark));
        for (var k = 0; k < 6; k++)
        {
            var x = -0.17 + k * 0.06;
            Lit(p, [x, top + 0.03, -0.092], [x + 0.025, top + 0.045, -0.08], k % 3 != 0 ? LedG : LedA);
        }
        string[] cables = ["#1f6fd8", "#f2c400", "#c0392b", "#e6e2d6"];
        for (var k = 0; k < cables.Length; k++) // (patch cables, down from its face)
        {
            var x = -0.15 + k * 0.09;
            p.Add(Box([x + 0.03, top - 0.16, -0.094], [x + 0.045, top + 0.02, -0.08], cables[k], detail: true, shape: NoPaint));
        }
        BaseSocket(p);
        p.Stats(3, 30, 1, 0.5);
        return p;
    }

    private static Part Dish()
    {
        var p = new Part("Satellite Dish", Small, Section, "A satellite dish on a post. It picks up the IT Department's hold music.", color: "#c9cbc8", group: Group);
        var top = 0.22;
        Stalk(p, top, Dark);
        p.Add(Box([-0.06, top, -0.06], [0.06, top + 0.04, 0.06], Dark)); // (its mount)
        p.Add(Box([-0.25, top + 0.04, -0.25], [0.25, top + 0.12, 0.25], hidden: true));
        p.Add(Box([-0.25, top + 0.04, -0.25], [0.25, top + 0.12, 0.25], "#c9cbc8", detail: true, shape: Round("cylinder", 12, "-y", 0.35))); // (the bowl: narrow at its foot)
        p.Add(Box([-0.012, top + 0.12, 0.17], [0.012, top + 0.3, 0.194], Dark, detail: true)); // (the arm)
        p.Add(Box([-0.04, top + 0.3, 0.13], [0.04, top + 0.36, 0.21], Dark, detail: true)); // (the receiver on it)
        BaseSocket(p);
        p.Stats(4, 30, 1, 0.5);
        return p;
    }

    private static Part Reel()
    {
        var p = new Part("Cable Reel", Small, Section, "A drum of network cable, about a kilometre of it. All of it tangled.", color: Cable, group: Group);
        var top = 0.06;
        p.Add(Box([-0.08, 0, -0.06], [0.08, top, 0.06], Dark)); // (its stand)
        var r = 0.17;
        foreach (var x in new[] { -0.14, 0.11 })
            p.Add(Box([x, top, -r], [x + 0.03, top + 2 * r, r], "#7a5e40", detail: true, shape: Round("cylinder", 12, "+x"))); // (the flanges)
        p.Add(Box([-0.11, top + 0.04, -r + 0.04], [0.11, top + 2 * r - 0.04, r - 0.04], hidden: true));
        p.Add(Box([-0.11, top + 0.04, -r + 0.04], [0.11, top + 2 * r - 0.04, r - 0.04], Cable, detail: true, shape: Round("cylinder", 12, "+x", paintable: false))); // (the cable)
        BaseSocket(p);
        p.Stats(6, 30, 0, 0.5);
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
    [
        ("", Monitor()), ("", Plant()), ("", Fan()), ("", Plaque()),
        ("", Antenna("Router Antenna", 0.3, "A router's antenna, bolted on. Full bars, no internet.", 0.5)),
        ("", Antenna("Wi-Fi Mast", 0.55, "A tall Wi-Fi antenna. The password is on a sticky note somewhere.", 1)),
        ("", Switch()), ("", Dish()), ("", Reel()), ("", ServerRack()), ("", Computer()), ("", Fax()), ("", Terminal()),
    ]);
}
