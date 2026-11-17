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
    private const int MaxBulk = 60_000; // (ConstructStorage.MaxBulk: a bin's capacity stops here, so the 8 m bunker is rated under its room)
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
        p.Set(("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", (int)Math.Min(MaxBulk, Math.Round(volume * UnitsPerM3))), ("chuteLoad", 160),
                ("min", Vec(i0, t + 0.005, i0)), ("max", Vec(i1, h - 0.05, i1)))))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Vec(x0 + t * 0.5, h, 0)), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Vec(x1 - t * 0.5, h, 0)), ("face", "+y")))));
        var area = w * w + 4 * w * h;
        p.Stats(area * t * 2400 * 0.5, area * 900, 20);
        return p;
    }

    private const string Grid = "#3d4247", Spout = "#c9a227";

    /// <summary>
    /// A Tipping Grate (the user, 2026-11-17): a low steel grid over a shallow pit, flush enough to drive a tipper alongside or over.
    /// Whatever is tipped onto it (a tipper's lip, a bucket, a player's F, an Unloading Auger) lands in its pit, a bin, and its Chute out
    /// sends it on into the chute network. 0.3 m high: a frame round a pit floor, with bars across (look boxes) a few mm under the frame's top.
    /// </summary>
    private static Part Grate(double w, int cap)
    {
        var c = Small;
        const double h = 0.3, f = 0.15, floor = 0.12;
        var p = new Part($"Tipping Grate {w:0} m", c, "Factory", "A low grate over a pit: tip loose cargo onto it from a truck, a bucket or your hands, and its chute takes it on.", role: "storage", material: "steel", color: Grid, group: "Production");
        double x0 = -w / 2, x1 = w / 2;
        p.Add(Box([x0, 0, x0], [x1, floor, x1], Concrete)); // the pit's floor
        foreach (var (a, b) in new[] { (x0, x0 + f), (x1 - f, x1) })
            p.Add(Box([a, floor - 0.004, x0], [b, h, x1])); // the frame along z
        foreach (var (a, b) in new[] { (x0, x0 + f), (x1 - f, x1) })
            p.Add(Box([x0 + f, floor - 0.004, a], [x1 - f, h - 0.004, b])); // the frame along x
        var bars = (int)Math.Round((w - 2 * f) / 0.25);
        for (var k = 1; k < bars; k++)
        {
            var x = x0 + f + k * (w - 2 * f) / bars;
            p.Add(Box([x - 0.025, h - 0.06, x0 + f + 0.004], [x + 0.025, h - 0.008, x1 - f - 0.004], Grid, detail: true)); // a bar
        }
        foreach (var z in new[] { x0 + f + 0.08, x1 - f - 0.08 })
            p.Add(Box([x0 + 0.03, h + 0.002, z - 0.04], [x1 - 0.03, h + 0.012, z + 0.04], Hazard, detail: true)); // hazard strips on the frame's top
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [x1, h * 0.5, 0], "+x");
        p.Sock("-x", [x0, h * 0.5, 0], "-x");
        double i0 = x0 + f + 0.005, i1 = x1 - f - 0.005;
        p.Set(("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", cap), ("chuteLoad", 320),
                ("min", Vec(i0, floor + 0.005, i0)), ("max", Vec(i1, h - 0.07, i1)))))),
            ("ports", Py.L(
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Vec(x1 - f * 0.5, h, x1 - f * 0.5)), ("face", "+y")))));
        p.Stats(w * w * 60, w * w * 500, 25);
        return p;
    }

    /// <summary>
    /// An Unloading Auger (the user, 2026-11-17: a combine harvester's unloading auger): a hopper on a Medium turntable's top with a
    /// square tube out along its z and a spout at the end. Chute loose material or small pieces into the hopper; with power and its Run
    /// input on it pours from the spout into the store under it (ConstructAugers.cs). Swing it on a Turntable 1x1 Medium: logic turns it.
    /// </summary>
    private static Part Auger(double length)
    {
        var c = Small; // (it fits a Turntable 1x1 Medium's top, a small socket)
        const double hw = 0.32, hh = 0.7, tw = 0.16, ty0 = 0.42, ty1 = 0.74;
        var p = new Part($"Unloading Auger {length:0} m", c, "Factory", "A hopper and a swinging tube: it pours its load from the spout into a truck bed or bin under it. Fit it on a Turntable 1x1 Medium to swing it.", role: "storage", material: "steel", color: Spout, group: "Production");
        p.Add(Box([-hw, 0, -hw], [hw, 0.06, hw], Grid)); // the hopper's floor plate
        foreach (var (a, b) in new[] { (-hw, -hw + 0.04), (hw - 0.04, hw) })
            p.Add(Box([a, 0.056, -hw], [b, hh, hw])); // its sides along z
        foreach (var (a, b) in new[] { (-hw, -hw + 0.04), (hw - 0.04, hw) })
            p.Add(Box([-hw + 0.044, 0.056, a], [hw - 0.044, hh - 0.004, b])); // its sides along x
        p.Add(Box([-tw, ty0, hw + 0.004], [tw, ty1, length - 0.3])); // the tube
        p.Add(Box([-tw - 0.02, ty0 - 0.1, length - 0.3 - 0.004], [tw + 0.02, ty1 + 0.02, length])); // the head
        p.Add(Box([-0.12, 0.12, length - 0.26], [0.12, ty0 - 0.096, length - 0.04], Grid)); // the spout, down
        for (var z = hw + 0.6; z < length - 0.5; z += 0.9)
            p.Add(Box([-tw - 0.012, ty0 - 0.012, z], [tw + 0.012, ty1 + 0.012, z + 0.08], Grid, detail: true)); // a flange band
        p.Add(Box([-0.1, ty0 - 0.3, hw + 0.15], [0.1, ty0 - 0.004, hw + 0.35], Grid, detail: true)); // the drive box under the tube
        p.Sock("mount", [0, 0, 0], "-y");
        p.Set(("power", 1500.0), // (watts, as a drill's: its motor turns the screw)
            ("storage", Kw(("bulk", Kw(("material", "any"), ("capacity", 800), ("chuteLoad", 320),
                ("min", Vec(-hw + 0.045, 0.065, -hw + 0.045)), ("max", Vec(hw - 0.045, hh - 0.05, hw - 0.045)))),
                ("auger", Kw(("tip", Vec(0, 0.1, length - 0.15)), ("rate", 200))))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Vec(0, hh, -hw + 0.02)), ("face", "+y")))));
        p.Stats(120 + length * 45, 900 + length * 150, 12);
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
            ("", Bunker("Ore Bunker 4 m", 4.0, 2.5, 0.18, "A large open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Ore Bunker 5 m", 5.0, 2.5, 0.2, "A large open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Ore Bunker 6 m", 6.0, 3.0, 0.2, "A huge open concrete bin for bulk stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Bunker("Ore Bunker 8 m", 8.0, 3.0, 0.25, "A huge open concrete bin for a works' stock: chute in and out on its rim, or tip a load in from above.", false)),
            ("", Grate(4.0, 6000)),
            ("", Grate(6.0, 12000)),
            ("", Auger(4.0)),
            ("", Auger(6.0)),
        ]);
    }
}
