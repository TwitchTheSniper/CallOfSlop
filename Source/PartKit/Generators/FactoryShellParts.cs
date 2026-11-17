using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The factory kit's own parts (docs/sandbox/design/factory-generator.md 3.5, part kit generator <c>factory_shell</c>), in Constructs/Parts/Factory.
/// Phase 1: the ore bunkers, open-topped concrete bins with chute ports that a generated works keeps its raw and refined stock in. A sized Storage
/// Volume standing in the open spills over its open sides as soon as it is a quarter full (ConstructSeal: an open face's lowest corner under the
/// level pours), so a heap fed by a hauler or a smelter ran out onto the floor as Material Bundles; a bunker is a bin, so it holds what it is rated.
/// The Tipping Bunker is low (its top 1 m up) so a side-tipping Ore Hauler's lip pours over it from the road.
/// Run the part kit's assembling (--gen-parts assembling) after it for the recipes.
/// </summary>
public sealed class FactoryShellParts : IPartGenerator
{
    public string Name => "factory_shell";

    private const string Out = "Constructs/Parts/Factory";
    private const string Concrete = "#8e8a80", Rim = "#5c5a55", Hazard = "#f2b01e";
    private const int UnitsPerM3 = 400; // (Recycling.json binUnitsPerCubicMetre: a bunker holds what a Storage Volume of its inside would)

    /// <summary>An open-topped bin: walls <paramref name="t"/> thick round a floor, chute in and out on its rim, a socket under the middle.</summary>
    private static Part Bunker(string name, double w, double h, double t, string desc, bool hazard)
    {
        var c = Small;
        var p = new Part(name, c, "Factory", desc, role: "storage", material: "concrete", color: Concrete, group: "Production");
        double x0 = -w / 2, x1 = w / 2;
        p.Add(Box([x0, 0, x0], [x1, t, x1])); // the floor
        foreach (var (a, b) in new[] { (x0, x0 + t), (x1 - t, x1) })
            p.Add(Box([a, t, x0], [b, h, x1])); // the walls along z
        foreach (var (a, b) in new[] { (x0, x0 + t), (x1 - t, x1) })
            p.Add(Box([x0 + t, t, a], [x1 - t, h - 0.004, b])); // the walls along x, a few mm under the others' tops
        // A steel rim round the top (drawn), and on a tipping bunker a hazard band on the road side's face.
        foreach (var (a, b) in new[] { (x0 - 0.012, x0 + t + 0.012), (x1 - t - 0.012, x1 + 0.012) })
            p.Add(Box([a, h - 0.003, x0 - 0.012], [b, h + 0.03, x1 + 0.012], Rim, detail: true));
        foreach (var (a, b) in new[] { (x0 - 0.009, x0 + t + 0.009), (x1 - t - 0.009, x1 + 0.009) })
            p.Add(Box([x0 + t + 0.013, h - 0.003, a], [x1 - t - 0.013, h + 0.027, b], Rim, detail: true));
        if (hazard)
            p.Add(Box([x0 + 0.05, h * 0.35, x1 + 0.003], [x1 - 0.05, h * 0.65, x1 + 0.009], Hazard, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [x1, h * 0.5, 0], "+x");
        p.Sock("-x", [x0, h * 0.5, 0], "-x");
        double i0 = x0 + t + 0.005, i1 = x1 - t - 0.005;
        var volume = (i1 - i0) * (i1 - i0) * (h - 0.05 - t - 0.005);
        p.Set(("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", (int)Math.Round(volume * UnitsPerM3)), ("chuteLoad", 160),
                ("min", Vec(i0, t + 0.005, i0)), ("max", Vec(i1, h - 0.05, i1)))))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Vec(x0 + t * 0.5, h, 0)), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Vec(x1 - t * 0.5, h, 0)), ("face", "+y")))));
        var area = w * w + 4 * w * h;
        p.Stats(area * t * 2400 * 0.5, area * 900, 20);
        return p;
    }

    /// <summary>The shed's eaves: a portal column's height, over the tallest Workshop refiner (6.3 m) on a 0.3 m floor with the rafter and a margin.</summary>
    public const double Eaves = 8.0, ColumnW = 0.3, RafterH = 0.35, RafterW = 0.2, Bay = 4.0, SheetT = 0.04, Lift = 0.003;

    /// <summary>The spans a portal rafter comes in: a machine band and its aisle, less half a metre (factory kit: columns 0.25 m in from the road and the band's back).</summary>
    public static readonly double[] Spans = [5.5, 7.5, 9.5, 11.5, 13.5];

    private const string Steel = "#3a3f46", Sheet = "#6f7a6a", Plate = "#2a2e33";

    /// <summary>A portal column: an H-section steel post, its foot on a floor socket, its head carrying a rafter.</summary>
    private static Part Column()
    {
        var c = Small;
        double h = Eaves, w = ColumnW, f = 0.04;
        var p = new Part($"Portal Column {h:0} m", c, "Factory shell", "A steel H column for a shed or hall: stand it on the floor, a Portal Rafter on its head.", color: Steel, group: "Building");
        p.Add(Box([-w / 2, 0, -w / 2], [w / 2, h, -w / 2 + f])); // a flange
        p.Add(Box([-w / 2, 0, w / 2 - f], [w / 2, h, w / 2])); // the other flange
        p.Add(Box([-f / 2, 0, -w / 2 + f], [f / 2, h - 0.004, w / 2 - f])); // the web
        p.Add(Box([-0.25, 0.003, -0.25], [0.25, 0.025, 0.25], Plate, detail: true)); // the base plate
        p.Add(Box([-w / 2 - 0.01, h - 0.02, -w / 2 - 0.01], [w / 2 + 0.01, h + 0.002, w / 2 + 0.01], Plate, detail: true)); // the cap
        p.Sock("foot", [0, 0, 0], "-y");
        p.Sock("head", [0, h + 0.002, 0], "+y");
        p.Stats(h * 0.022 * 7850, 1800, 18);
        return p;
    }

    /// <summary>A portal rafter <paramref name="span"/> long (its own z): a seat under each end on a column's head, a top socket over each end for the roof.</summary>
    private static Part Rafter(double span)
    {
        var c = Small;
        double h = RafterH, w = RafterW, f = 0.035;
        var p = new Part($"Portal Rafter {span:0.0} m", c, "Factory shell", "A steel rafter from column head to column head: roof sheets lie on it.", color: Steel, group: "Building");
        p.Add(Box([-w / 2, 0, 0], [w / 2, f, span])); // the bottom flange
        p.Add(Box([-w / 2, h - f, 0], [w / 2, h, span])); // the top flange
        p.Add(Box([-0.015, f, 0.004], [0.015, h - f, span - 0.004])); // the web
        p.Sock("seatA", [0, 0, 0], "-y");
        p.Sock("seatB", [0, 0, span], "-y");
        p.Sock("topA", [0, h, 0], "+y");
        p.Sock("topB", [0, h, span], "+y");
        p.Stats(span * 0.009 * 7850, 900, 12);
        return p;
    }

    /// <summary>A roof sheet one bay wide (its own x, 4 m) over a rafter's span (its own z, with a quarter metre over each end), corrugated on top.</summary>
    private static Part RoofSheet(double span)
    {
        var c = Small;
        double l = span + 0.5;
        var p = new Part($"Roof Sheet {Bay:0}x{l:0} m", c, "Factory shell", "A corrugated roof sheet a bay wide: lay it from one Portal Rafter to the next.", color: Sheet, group: "Building");
        p.Add(Box([0, Lift, -0.25], [Bay, Lift + SheetT, span + 0.25])); // the sheet, a few mm over the rafters it lies on
        for (var k = 0; k < 8; k++)
        {
            var x = (k + 0.5) * Bay / 8;
            p.Add(Box([x - 0.05, Lift + SheetT, -0.247], [x + 0.05, Lift + SheetT + 0.025, span + 0.247], detail: true)); // a corrugation
        }
        p.Sock("rest", [0, 0, 0], "-y");
        p.Stats(Bay * l * 9, Bay * l * 60, 3);
        return p;
    }

    public void Make(PartOutput o)
    {
        var shell = new List<(string, Part)> { ("", Column()) };
        foreach (var s in Spans)
        {
            shell.Add(("", Rafter(s)));
            shell.Add(("", RoofSheet(s)));
        }
        WriteParts(o, Out,
        [
            .. shell,
            ("", Bunker("Ore Bunker 2 m", 2.0, 1.5, 0.12, "An open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Ore Bunker 3 m", 3.0, 2.0, 0.15, "A big open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Tipping Bunker 3 m", 3.0, 1.0, 0.15, "A low open bin a tipper pours into from the road beside it: its top is 1 m up. Chute it on to the stock.", true)),
        ]);
    }
}
