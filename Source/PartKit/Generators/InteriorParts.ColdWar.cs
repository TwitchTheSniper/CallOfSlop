using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Ministry's fittings (docs/sandbox/constructs/buildings.md, "Inside: the cold war"): what a government building of the
/// last century was fitted out with and never refitted. Old light switches that work (a wire from one to a lamp), strip lights
/// and bulkhead lamps on the mains, a radiator, a filing cabinet, a wall clock, a teleprinter and a cabinet of tape reels. Hung
/// pieces go on a wall's face socket by their "back" (the part stands in +z, into the room); furniture stands on a floor socket
/// by its "bottom" and faces +z.
/// </summary>
public sealed partial class InteriorParts
{
    private const string Bakelite = "#3b2a20", OldCream = "#d9d2b8", FileGreen = "#2f5a3c", CastIron = "#4a4d4f", Enamel = "#e9e6da", TapeBrown = "#5a3b28";

    private static PyDict? Cyl(double[] mn, double[] mx, string color, string front, bool paintable = true, int segments = 12) =>
        paintable ? B(mn, mx, color, true, ("shape", "cylinder"), ("segments", segments), ("front", front))
            : B(mn, mx, color, true, ("shape", "cylinder"), ("segments", segments), ("front", front), ("paintable", false));

    private static PyDict? LitBox(double[] mn, double[] mx, string colour) => B(mn, mx, colour, true, ("paintable", false), ("glow", true));

    /// <summary>A round brown light switch on a wall: a Switch in every way but its looks.</summary>
    private static Part BakeliteSwitch()
    {
        var p = Hung("Bakelite Switch", "An old round light switch. On or off, with a clunk: wire it to a lamp.", role: "logic", color: Bakelite, mass: 0.5, health: 20, armour: 0, finish: "plastic");
        p.Add(B([-0.05, -0.05, 0.0], [0.05, 0.05, 0.012], OldCream, false, ("finish", "gloss paint"))); // the plate
        p.Add(Cyl([-0.038, -0.038, 0.012], [0.038, 0.038, 0.034], Bakelite, "+z"));
        p.Add(B([-0.006, -0.004, 0.034], [0.006, 0.022, 0.052], "#a58a4e", true, ("paintable", false), ("finish", "brass"))); // the dolly
        return p.Set(("logic", new PyDict { ["kind"] = "switch" }), ("config", new List<object?>
        {
            new PyDict { ["key"] = "startOn", ["label"] = "Starts on", ["group"] = "General", ["default"] = false, ["tooltip"] = "A newly built switch starts in the on position." },
        }));
    }

    /// <summary>A fluorescent batten, 1.2 m: the light every corridor of the Ministry is the colour of.</summary>
    private static Part StripLight()
    {
        var p = Hung("Strip Light", "A fluorescent strip on the mains. It hums. Lights while the power circuit reaches it; a wire into it switches it.",
            role: "logic", color: Enamel, mass: 3, health: 30, armour: 0);
        p.Add(B([-0.6, -0.03, 0.0], [0.6, 0.04, 0.07], Enamel, false, ("finish", "gloss paint")));
        foreach (var x in new[] { -0.6, 0.57 })
            p.Add(B([x, -0.07, 0.01], [x + 0.03, -0.03, 0.06], CastIron, true)); // the tube's ends
        p.Add(GlowBox([-0.57, -0.065, 0.02], [0.57, -0.035, 0.05]));
        return MainsLamp(p, 30);
    }

    /// <summary>A round caged lamp for an outside wall or a stair.</summary>
    private static Part BulkheadLight()
    {
        var p = Hung("Bulkhead Light", "A caged lamp for an outside wall. Lights while the power circuit reaches it; a wire into it switches it.",
            role: "logic", color: CastIron, mass: 5, health: 80, armour: 3);
        p.Add(B([-0.13, -0.13, 0.0], [0.13, 0.13, 0.05], CastIron, false, ("finish", "dirty steel")));
        p.Add(GlowBox([-0.1, -0.1, 0.05], [0.1, 0.1, 0.1]));
        foreach (var x in new[] { -0.045, 0.03 })
            p.Add(B([x, -0.12, 0.1], [x + 0.015, 0.12, 0.115], CastIron, true, ("paintable", false))); // the cage's bars
        p.Add(B([-0.12, -0.008, 0.115], [0.12, 0.008, 0.13], CastIron, true, ("paintable", false)));
        return MainsLamp(p, 25);
    }

    /// <summary>A column radiator under a window: hung by its back, low on the wall.</summary>
    private static Part Radiator()
    {
        var p = Hung("Cast Iron Radiator", "A column radiator. Lukewarm since the Ministry was founded.", color: OldCream, mass: 60, health: 200, armour: 4, finish: "worn paint");
        p.Add(B([-0.5, -0.05, 0.0], [0.5, 0.55, 0.17], OldCream));
        for (var k = 0; k < 9; k++) // (the gaps between the columns)
        {
            var x = -0.4 + k * 0.1;
            p.Add(B([x - 0.012, 0.0, 0.17], [x + 0.012, 0.5, 0.176], CastIron, true, ("paintable", false)));
        }
        p.Add(B([0.5, 0.0, 0.07], [0.56, 0.05, 0.13], CastIron, true, ("paintable", false))); // the valve
        p.Add(B([-0.45, -0.1, 0.08], [-0.41, -0.05, 0.12], CastIron, true, ("paintable", false))); // the pipe down
        return p;
    }

    /// <summary>Four drawers of files.</summary>
    private static Part FilingCabinet()
    {
        double w = 0.47, h = 1.32, d = 0.62;
        var p = Furniture("Filing Cabinet", "Four drawers of files. Everything is in here somewhere, under something else.", "storage", w, h, d, FileGreen, mass: 50, health: 220, finish: "worn paint");
        Cabinet(p, w, h, d, FileGreen, [0.35, 0.67, 0.99]);
        var slots = new List<PyDict>();
        for (var k = 0; k < 4; k++)
        {
            var y = 0.03 + k * 0.32;
            p.Add(B([-w / 2 + 0.035, y + 0.02, d / 2 - 0.02], [w / 2 - 0.035, y + 0.28, d / 2 + 0.004], FileGreen, true)); // a drawer's front
            p.Add(B([-0.07, y + 0.2, d / 2 + 0.004], [0.07, y + 0.225, d / 2 + 0.024], "#b9bcbd", true, ("paintable", false), ("finish", "steel"))); // its handle
            p.Add(B([-0.05, y + 0.09, d / 2 + 0.004], [0.05, y + 0.15, d / 2 + 0.016], Enamel, true, ("paintable", false))); // its label
            slots.Add(Slot([-0.19, y + 0.03, -0.26], [0.19, y + 0.27, 0.27], "files", 4));
        }
        p.Set(("storage", Storage([.. slots])));
        return p;
    }

    /// <summary>A round clock: stopped.</summary>
    private static Part WallClock()
    {
        var p = Hung("Wall Clock", "A Ministry clock. Right twice a day, which is above target.", color: CastIron, mass: 2, health: 20, armour: 0);
        p.Add(B([-0.17, -0.17, 0.0], [0.17, 0.17, 0.03], CastIron)); // (its box: what is hit)
        p.Add(Cyl([-0.19, -0.19, 0.03], [0.19, 0.19, 0.05], CastIron, "+z", segments: 16));
        // (the part kit drops a box thinner than 10 mm, so the face, the marks and the hands are each at least that deep)
        p.Add(Cyl([-0.165, -0.165, 0.05], [0.165, 0.165, 0.062], Enamel, "+z", false, 16));
        foreach (var (x, y) in new[] { (0.0, 0.135), (0.135, 0.0), (0.0, -0.135), (-0.135, 0.0) }) // the marks at twelve, three, six and nine
            p.Add(B([x - (x == 0 ? 0.006 : 0.015), y - (y == 0 ? 0.006 : 0.015), 0.062], [x + (x == 0 ? 0.006 : 0.015), y + (y == 0 ? 0.006 : 0.015), 0.074], "#1c1c1c", true, ("paintable", false)));
        p.Add(B([-0.006, -0.02, 0.062], [0.006, 0.12, 0.074], "#1c1c1c", true, ("paintable", false))); // the minute hand, at twelve
        p.Add(B([-0.02, -0.006, 0.074], [0.08, 0.006, 0.086], "#1c1c1c", true, ("paintable", false))); // the hour hand, at three
        return p;
    }

    /// <summary>A teleprinter on its stand, paper spilling from it.</summary>
    private static Part Teleprinter()
    {
        double w = 0.6, h = 1.0, d = 0.5;
        var p = Furniture("Teleprinter", "A teleprinter on a stand. It prints the Ministry's wires, in capitals, eventually.", "frame", w, h, d, CastIron, mass: 45, health: 160, finish: "worn paint");
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, 0.62, d / 2], CastIron)); // the stand
        p.Add(B([-0.27, 0.62, -0.22], [0.27, 0.86, 0.2], OldCream, false, ("finish", "worn paint"))); // the machine
        p.Add(B([-0.22, 0.66, 0.2], [0.22, 0.72, 0.3], "#1c1c1c", false, ("paintable", false), ("finish", "plastic"))); // the keys
        p.Add(B([-0.15, 0.86, -0.12], [0.15, h, -0.02], CastIron)); // the platen's cover
        p.Add(B([-0.12, 0.3, d / 2 + 0.002], [0.12, 0.86, d / 2 + 0.014], "#f2efe6", true, ("paintable", false))); // the paper, down its front
        p.Add(LitBox([0.2, 0.78, 0.2], [0.23, 0.81, 0.212], "#f0a020"));
        return p.Set(("power", 40));
    }

    /// <summary>A cabinet of tape reels and lamps: the Ministry's computer.</summary>
    private static Part TapeCabinet()
    {
        double w = 1.0, h = 1.9, d = 0.6;
        var p = Furniture("Tape Cabinet", "A cabinet of tape reels and lamps. It knows where everyone is, as of some years ago.", "frame", w, h, d, OldCream, mass: 220, health: 500, armour: 6, tool: "welder", seconds: 4.0, finish: "worn paint");
        p.Add(B([-w / 2, 0, -d / 2], [w / 2, h, d / 2], OldCream));
        p.Add(B([-w / 2 + 0.05, 0.95, d / 2], [w / 2 - 0.05, h - 0.08, d / 2 + 0.012], "#20262b", true, ("paintable", false), ("finish", "gloss paint"))); // the reels' window
        foreach (var x in new[] { -0.22, 0.22 })
        {
            p.Add(Cyl([x - 0.17, 1.38, d / 2 + 0.012], [x + 0.17, 1.72, d / 2 + 0.03], TapeBrown, "+z", false, 16));
            p.Add(Cyl([x - 0.05, 1.5, d / 2 + 0.03], [x + 0.05, 1.6, d / 2 + 0.05], "#b9bcbd", "+z", false));
        }
        p.Add(B([-0.14, 1.02, d / 2 + 0.012], [0.14, 1.2, d / 2 + 0.035], CastIron, true, ("paintable", false))); // the tape head
        p.Add(B([-w / 2 + 0.05, 0.5, d / 2], [w / 2 - 0.05, 0.85, d / 2 + 0.012], CastIron, true, ("paintable", false))); // the lamp panel
        string[] lamps = ["#f0a020", "#3fd04a", "#f0a020", "#d02a1e", "#3fd04a", "#f0a020"];
        for (var k = 0; k < lamps.Length; k++)
            p.Add(LitBox([-0.36 + k * 0.13, 0.7, d / 2 + 0.012], [-0.3 + k * 0.13, 0.76, d / 2 + 0.026], lamps[k]));
        p.Add(B([-w / 2 + 0.08, 0.1, d / 2], [w / 2 - 0.08, 0.4, d / 2 + 0.012], "#b9a98a", true)); // the louvre
        return p.Set(("power", 200));
    }

    /// <summary>A "wall" socket low on a piece of furniture's back: on a wall's bottom face socket (0.125 m up) it stands on the
    /// floor against the wall, where the floor's own sockets would leave it a metre out.</summary>
    private static Part Walled(Part p, double d)
    {
        p.Sock("wall", [0, 0.125, -d / 2], "-z");
        return p;
    }

    private static List<Part> ColdWarDressing() => [BakeliteSwitch(), StripLight(), BulkheadLight(), Radiator(), WallClock()];

    private static List<Part> ColdWarFurniture() => [Walled(FilingCabinet(), 0.62), Walled(Teleprinter(), 0.5), Walled(TapeCabinet(), 0.6)];
}
