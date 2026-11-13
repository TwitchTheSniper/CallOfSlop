using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Press family: Constructs/Parts/Press (presses, Die Lathes, dies, Die Racks, housings), Constructs/Pressing.json and the Press Line
/// blueprint (docs/sandbox/CONSTRUCTS.md, "The Press"). Was tools/constructs/press_parts.py.
///
/// A press stamps parts out of extruded pieces. It has 2 or 3 bays and a die slot; the die sets the family, the material the tier, a
/// piece's size across or its length the size or the count. Presses, lathes and dies come in the four size classes, and a die fits a press
/// of its own class. All sizes are in cells of the class, so a new class or a new die family is a row here. The table is flat on purpose:
/// one row per family, material and size band, so the coverage of the Assembler's list can be counted. Run the part kit's assembling (--gen-parts assembling)
/// after it for the machines' recipes.
/// </summary>
public sealed class PressParts : IPartGenerator
{
    public string Name => "press_parts";

    private const string Out = "Constructs/Parts/Press";
    private const string Table = "Constructs/Pressing.json";
    private const string Blueprint = "Constructs/Blueprints/PressLine.json";
    private const string Format = "call-of-slop-pressing/2";
    private const string About = "The stamping table (docs/sandbox/CONSTRUCTS.md, 'The Press'). Written by the part kit (--gen-parts press_parts): fix it, not this file. "
        + "'lathe' rows are a Die Lathe's, chosen as a setting: each bay gives its 'sections' (Extrusion presets, round, pipe), 'materials', "
        + "'minAcross' and 'maxAcross' (m, the piece's smaller side) and what a cut takes: 'use' (m). "
        + "'dies' (the Press rework, Pressed.cs) say what a die makes of a whole blank: its blanks, materials, bounds in cm, features in the blank's unit frame and patterns (a like part and a preset blank); "
        + "'presets' name the catalogue part that is a die's product of one standard blank. A die presses a blank bay's whole piece into one part (the preset's catalogue part when the blank is one), its pattern the press's Pattern setting or the blank's own; a pattern's 'bay2' is the kit it takes from bay 2, 'scale' the numbers that grow with the blank.";
    private const string Section = "Presses", Dies = "Dies";
    private const string BodyC = "#4d5a66", Trim = "#39424b", Dark = "#23272c", Gold = "#c8a13a", Bed = "#2f363d";
    private const string Blue = "#2f5f8f", LatheBlue = "#2a6878"; // the Press family's body colours (look.md, "Machine look")

    // (class, bays, speed, W, kg, health)
    private static readonly (Cls C, int Bays, int Speed, int Power, int Mass, int Health)[] Presses =
    [
        (Small, 2, 1, 900, 520, 800),
        (Medium, 3, 2, 3600, 18000, 6000),
        (Large, 3, 4, 14400, 200000, 40000),
        (Capital, 3, 8, 57600, 1800000, 240000),
    ];

    // Dies: family, classes it is made in, what it is cut from, die tier (1: a starter), colour
    private static readonly (string Family, Cls[] Classes, string Block, int Tier, string Colour)[] Families =
    [
        ("Button", [Small, Medium], "metal", 1, "#c06040"),
        ("Bracket", [Small, Medium, Large, Capital], "metal", 1, "#7f8a94"),
        ("Gear", [Small], "metal", 1, "#a8763a"),
        ("Board", [Small], "steel", 2, "#3f9b5c"),
        ("Piston", [Small], "steel", 2, "#6f7d8c"),
        ("Housing", [Small, Medium, Large, Capital], "steel", 2, "#55708a"),
        ("Cylinder", [Small, Medium, Large, Capital], "steel", 3, "#8a5570"),
        ("Plate", [Small, Medium, Large, Capital], "metal", 1, "#8c96a0"),
        ("Drive", [Small, Medium, Large, Capital], "steel", 2, "#4f7a5a"),
        ("Shaft", [Small, Medium], "steel", 2, "#6b7f8f"),
        ("Actuator", [Small, Medium, Large], "steel", 3, "#8f6b3f"),
        ("Spring", [Small, Medium, Large], "steel", 2, "#5f8f6b"),
        ("Cable", [Small, Medium], "metal", 1, "#b0703a"),
        ("Control", [Small, Medium, Large], "steel", 2, "#3f6f9f"),
        ("Tank", [Small, Medium, Large, Capital], "steel", 2, "#9f4a3f"),
        ("Beam", [Small, Medium, Large, Capital], "metal", 1, "#7a7f88"),
        ("Barrel", [Small, Medium, Large], "steel", 2, "#4a4d52"),
        ("Receiver", [Small, Medium], "steel", 2, "#4a5146"),
        ("Engine", [Small, Medium, Large, Capital], "steel", 3, "#6a4f3a"),
    ];

    // The dies whose stroke presses a whole blank to one part (Pressing.json "dies", the Press rework): they have no old rows.
    private static readonly string[] SizedFamilies = ["Bracket", "Housing", "Plate", "Button", "Gear", "Board", "Piston", "Cylinder", "Drive", "Shaft", "Actuator", "Spring", "Cable", "Control", "Tank", "Beam", "Barrel", "Receiver", "Engine"];

    private static double MaxAcross(Cls c) => c.Key switch { "small" => 0.45, "medium" => 1.8, "large" => 7.2, _ => 28.8 }; // what a class's bays take: 1.8 cells

    private static int Rank(Cls c) => Array.IndexOf(Classes, c);

    // (side m, int or float; class; cells)
    private static readonly (object Side, Cls C, int N)[] Housings = [(0.25, Small, 1), (0.5, Small, 2), (1, Medium, 1), (2, Medium, 2), (4, Large, 1), (8, Large, 2), (16, Capital, 1)];

    /// <summary>A preset blank's size across (cm): the part's own side, held to what the class it was always pressed on takes, so every preset comes off some press.</summary>
    private static int PresetAcross(int sideCm, Cls c) => Math.Min(sideCm, (int)Math.Round(MaxAcross(c) * 100));

    // Tiers of a family are materials: (material, name prefix, mass x, health x, armour x, colour); the factors keep their Python type.
    private static readonly (string Material, string Prefix, object Mx, object Hx, object Ax, string Colour)[] MaterialTiers =
    [
        ("metal", "", 1, 1, 1, "#7c838c"), ("steel", "Steel ", 1.25, 1.8, 1.5, "#6f7d8c"), ("bronze", "Bronze ", 1.4, 1.4, 1.2, "#a8763a"),
    ];

    private static string NameOf(Cls c, string b) => c.Name.Length > 0 ? $"{c.Name} {b}" : b;

    private static double Min(double a, double b) => b < a ? b : a;

    private static double R3(double v) => Py.Round(v, 3);

    private static Dictionary<string, (int, int)> Lim(int n, params string[] dirs) => dirs.ToDictionary(d => d, _ => (n, n));

    private sealed record Frame(double X0, double X1, double Y0, double Y1, double Z0, double Z1, double Base, double Crown, double Wall, double Y, List<double> Centres);

    /// <summary>The frame every press and lathe shares, in cells.</summary>
    private static Frame PressBody(Part p, int nx, int ny, int nz, int bays, bool die)
    {
        var c = p.Cls;
        var k = c.Cell;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        var t = k * 0.12;
        var bse = y0 + k * 1.2;
        var crown = y1 - k * 2;
        p.Add(Box([x0, y0, z0], [x1, bse, z1], MachineLook.Base)!.Update(("paintable", false), ("finish", "tread plate"))); // (the bed: the dark structural tone, whatever the paint)
        foreach (var x in new[] { x0, x1 - k * 0.5 })
            foreach (var z in new[] { z0, z1 - k * 0.5 })
                p.Add(Box([x, bse, z], [x + k * 0.5, crown, z + k * 0.5]));
        p.Add(Box([x0, crown, z0], [x1, y1, z1]));
        var wall = crown - k * 1.7;
        p.Add(Box([x0 + k * 0.5, wall, z0], [x1 - k * 0.5, crown, z0 + t])); // the back wall, under the crown: the bays' panels stand on it
        if (die)
        {
            var mid = (x0 + x1) / 2;
            // The ram: a bare steel cylinder and a head plate with a hazard edge, stroking down onto the die while it stamps.
            var zc = (z0 + z1) / 2;
            var stroke = k * 1.3;
            p.Add(MachineLook.D([mid - k * 1.1, crown - k * 0.5, zc - k * 1.1], [mid + k * 1.1, crown + k * 0.02, zc + k * 1.1], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12))));
            p.Add(MachineLook.Work(MachineLook.D([mid - k * 0.7, crown - k * 1.6, zc - k * 0.7], [mid + k * 0.7, crown - k * 0.05, zc + k * 0.7], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 12))), "stroke", "-y", stroke));
            p.Add(MachineLook.Work(MachineLook.D([mid - k * 1.5, crown - k * 1.95, zc - k * 1.2], [mid + k * 1.5, crown - k * 1.6, zc + k * 1.2], MachineLook.Steel, "steel"), "stroke", "-y", stroke));
            p.Add(MachineLook.Work(MachineLook.D([mid - k * 1.52, crown - k * 1.9, zc - k * 1.22], [mid + k * 1.52, crown - k * 1.68, zc + k * 1.22], MachineLook.Accent, "hazard paint"), "stroke", "-y", stroke));
        }
        // The shared look: the crown's dark cap and pinch-edge hazard, the out side orange with an arrow down to the conveyor, the bays' side white (in).
        var mk = Math.Min(k * 0.06, 0.03) < 0.012 ? 0.012 : Math.Min(k * 0.06, 0.03);
        p.Add(MachineLook.D([x0 - mk, y1 - k * 0.3, z0 - mk], [x1 + mk, y1 - k * 0.04, z1 + mk], MachineLook.Base));
        MachineLook.Hazard(p, [x0 + k * 0.5, crown - k * 0.02, z1 - k * 0.02], [x1 - k * 0.5, crown + k * 0.28, z1 + mk]);
        p.Add(MachineLook.D([x0 + k * 0.5, bse - k * 0.55, z1], [x1 - k * 0.5, bse - k * 0.1, z1 + mk], MachineLook.Out));
        MachineLook.Arrow(p, 2, z1 + mk, 1, 1, -1, bse - k * 0.33, (x0 + x1) / 2, k * 0.4, k * 0.6, MachineLook.Ink, mk);
        p.Add(MachineLook.D([x0 + k * 0.5, bse - k * 0.55, z0 - mk], [x1 - k * 0.5, bse - k * 0.1, z0], MachineLook.In));
        foreach (var px in new[] { x0, x1 - k * 0.5 }) // (steel guide rods up the front posts: the frame reads as a press)
            p.Add(MachineLook.D([px + k * 0.17, bse, z1 - k * 0.02], [px + k * 0.33, crown, z1 + k * 0.12], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"))));
        var span = x1 - x0 - k;
        var centres = Enumerable.Range(0, bays).Select(i => R3(x1 - k * 0.5 - span * (i + 0.5) / bays)).ToList(); // (bay 1 on the left as you stand at the back, feeding it)
        var y = (bse + wall) / 2;
        foreach (var bx in centres)
        {
            var h = k * 0.95;
            p.Add(MachineLook.D([bx - h, wall - t, z0 - t * 0.4], [bx + h, wall, z0 + t], MachineLook.In));
            p.Add(MachineLook.D([bx - h, bse, z0 - t * 0.4], [bx + h, bse + t, z0 + t], MachineLook.Accent, "hazard paint"));
        }
        // Where the conveyors meet it (ConstructPorts.cs: a Piece in 11 cm under each bay's socket on the back plane, the Piece out 10 cm
        // above the underside, 0.2 cells out of the front): a steel feed table on two brackets in each mouth, its top at the conveyor's
        // line, and a run-off lip out of the bed's front to the port. Looks only, inside the bounds; nothing proud of the back plane.
        foreach (var bx in centres)
        {
            var top = y - 0.11 - 0.004;
            var thick = Math.Max(0.02, k * 0.16);
            if (top - thick < bse + t + 0.02)
                continue; // (the mouth's own sill is there: a small Die Lathe)
            double w = k * 0.85, deep = k * 0.6, br = Math.Max(0.012, k * 0.12);
            p.Add(MachineLook.D([bx - w, top - thick, z0 + 0.004], [bx + w, top, z0 + deep], MachineLook.Steel, "steel"));
            foreach (var ex in new[] { bx - w + 0.004, bx + w - 0.004 - br })
                p.Add(MachineLook.D([ex, bse - 0.004, z0 + 0.008], [ex + br, top - thick + 0.003, z0 + deep - 0.004], MachineLook.Base));
        }
        {
            var mid = (x0 + x1) / 2;
            p.Add(MachineLook.D([mid - k * 1.2, y0 + 0.004, z1 - 0.004], [mid + k * 1.2, y0 + 0.1 - 0.004, z1 + k * 0.2 - 0.003], MachineLook.Steel, "steel"));
        }
        return new Frame(x0, x1, y0, y1, z0, z1, bse, crown, wall, y, centres);
    }

    /// <summary>The bays' cards, over their mouths on the back wall; the machine's own on the front of the crown.</summary>
    private static List<object?> Panels(Part p, Frame f, double baysWidth, bool pattern = false)
    {
        var c = p.Cls;
        var k = c.Cell;
        var proud = 0.004 * Math.Max(1, Math.Pow(c.X, 0.5));
        var h = k * 1.25;
        var o = new List<object?>();
        // Three bays share the wall, so a card is 1.9 cells wide, not the Small Press's 2.9: a lower card (smaller text, still larger in metres
        // than a Small Press's) holds 31 characters a line, the longest a card writes ("Makes Limited-Slip Differential", "Sized small fluid tank").
        if (f.Centres.Count >= 3)
            h = k * 0.5;
        for (var i = 0; i < f.Centres.Count; i++)
            o.Add(Kw(("source", "press.bay"), ("slot", i), ("at", Py.L(f.Centres[i], R3((f.Wall + f.Crown) / 2), Py.Round(f.Z0 - proud, 4))), ("size", Py.L(R3(baysWidth), R3(h))), ("face", "-z"), ("up", "+y"), ("rows", 5)));
        var die = Kw(("source", "press.die"), ("at", Py.L(R3((f.X0 + f.X1) / 2), R3((f.Crown + f.Y1) / 2), Py.Round(f.Z1 + proud, 4))), ("size", Py.L(R3(k * 5.8), R3(k * 1.2))), ("face", "+z"), ("up", "+y"), ("rows", 4)); // (31 characters: "Auto: Limited-Slip Differential")
        // Recipe opens the recipe picker on the machine (Program.MachineRecipePicker: a press's patterns, a lathe's dies); a press's Pattern
        // steps to its die's next pattern, then Auto, as before.
        var recipe = Kw(("action", "machine.recipe"), ("at", pattern ? Py.L(0.44, 0.02, 0.70, 0.24) : Py.L(0.72, 0.02, 0.98, 0.24)), ("label", "Recipe"));
        die["touch"] = pattern
            ? Py.L(recipe, Kw(("action", "press.pattern"), ("at", Py.L(0.72, 0.02, 0.98, 0.24)), ("label", "Pattern")))
            : Py.L(recipe);
        o.Add(die);
        return o;
    }

    /// <summary>The status lamp: a bar the die screen's width on the front of the crown's dark cap, just over the screen, a few mm proud of it
    /// (ProductionLines: its colour and blink are the line state; the box only says where).</summary>
    private static PyDict? StatusLamp(Part p, Frame f)
    {
        var k = p.Cls.Cell;
        var mx = (f.X0 + f.X1) / 2;
        var mk = Math.Min(k * 0.06, 0.03) < 0.012 ? 0.012 : Math.Min(k * 0.06, 0.03); // (the dark cap's overhang: PressBody)
        var b = MachineLook.D([R3(mx - k * 2.5), R3(f.Y1 - k * 0.3 + 0.004), R3(f.Z1 + mk - 0.004)], [R3(mx + k * 2.5), R3(f.Y1 - k * 0.04 - 0.004), R3(f.Z1 + mk + 0.009)], "#2b3036");
        if (b is not null)
            b["status"] = true;
        return b;
    }

    private static List<object?> Tray(double x0, double x1, double y, double z0, double z1)
    {
        var gap = (x1 - x0) * 0.01;
        var mid = (x0 + x1) / 2;
        return Py.L(
            Kw(("min", Py.L(R3(x0 + gap), y, z0)), ("max", Py.L(R3(mid - gap), R3(y + (z1 - z0)), z1)), ("stack", 12), ("label", "Tray")),
            Kw(("min", Py.L(R3(mid + gap), y, z0)), ("max", Py.L(R3(x1 - gap), R3(y + (z1 - z0)), z1)), ("stack", 12), ("label", "Tray")));
    }

    private static Part Press((Cls C, int Bays, int Speed, int Power, int Mass, int Health) row)
    {
        var (c, bays, speed, power, mass, health) = row;
        var k = c.Cell;
        int nx = 7, ny = 8, nz = 6;
        var p = new Part(NameOf(c, "Press"), c, Section, $"Stamps parts out of extruded pieces. {bays} bays and a die slot: the die sets what it makes. Set pieces in the bays with the physics gun or by chute. Needs power.",
            role: "storage", group: "Production", color: Blue);
        var f = PressBody(p, nx, ny, nz, bays, true);
        BlockSockets(p, nx, ny, nz, faces: "xy", limit: Lim(3, "+x", "-x", "+y", "-y"));
        for (var i = 0; i < f.Centres.Count; i++)
            p.Sock($"bay{i + 1}", [f.Centres[i], f.Y, f.Z0], "-z", kind: "die");
        var mid = (f.X0 + f.X1) / 2;
        var zm = (f.Z0 + f.Z1) / 2;
        p.Sock("dieslot", [mid, f.Base, zm], "+y", kind: "pressdie");
        p.Stats(mass, health, c.Armour, c != Small ? null : 4);
        var bayList = f.Centres.Select((bx, i) => (object?)Kw(("socket", $"bay{i + 1}"), ("min", Py.L(R3(bx - k * 0.95), R3(f.Base), R3(f.Z0 - k * 1.5))), ("max", Py.L(R3(bx + k * 0.95), R3(f.Wall), R3(f.Z0 + k * 1.5))))).ToList();
        var width = Min(k * 3.2, (f.X1 - f.X0 - k) / bays - k * 0.1);
        var tz0 = R3(zm + k * 1.3);
        var tz1 = R3(f.Z1 - k * 0.55);
        p.Set(("power", power), ("config", Py.L(Kw(("key", "recipe"), ("label", "Pattern"), ("group", "Work"),
                ("tooltip", "What its die makes of a blank: Auto lets the blank choose, or one of the die's patterns. On its die screen, Recipe opens a picker of them and Pattern steps through them.")))),
            ("storage", Kw(
                ("slots", Tray(f.X0 + k * 0.55, f.X1 - k * 0.55, R3(f.Base + k * 0.01), tz0, tz1)),
                ("press", Kw(("bays", bayList),
                    ("die", Kw(("socket", "dieslot"), ("min", Py.L(R3(mid - k * 2), R3(f.Base), R3(zm))), ("max", Py.L(R3(mid + k * 2), R3(f.Wall), R3(f.Z1 + k * 2))))),
                    ("maxAcross", MaxAcross(c)), ("speed", speed), ("outAt", Py.L(R3(mid), R3(f.Y0), R3(f.Z1 + k * 0.2))), ("outDir", "+z"))))),
            ("panels", Panels(p, f, width, pattern: true)));
        p.Add(StatusLamp(p, f)); // (its line state over its own screen: ProductionLines)
        return p;
    }

    private static Part Lathe((Cls C, int Bays, int Speed, int Power, int Mass, int Health) row)
    {
        var (c, _, speed, power, mass, health) = row;
        var k = c.Cell;
        int nx = 7, ny = 6, nz = 5;
        var p = new Part(NameOf(c, "Die Lathe"), c, Section, "Cuts press dies from a solid block. Choose the die with the Recipe button on its screen (or the configure tool), and set the block in its bay. Needs power.",
            role: "storage", group: "Production", color: LatheBlue);
        var f = PressBody(p, nx, ny, nz, 1, false);
        BlockSockets(p, nx, ny, nz, faces: "xy", limit: Lim(3, "+x", "-x", "+y", "-y"));
        var c0 = f.Centres[0];
        p.Sock("bay1", [c0, f.Y, f.Z0], "-z", kind: "die");
        p.Add(MachineLook.Work(MachineLook.D([c0 - k * 0.7, f.Y - k * 0.7, f.Z0 + k], [c0 + k * 0.7, f.Y + k * 0.7, f.Z0 + k * 2], MachineLook.Steel, "steel"), "spin", "+z")); // (the chuck: it turns while it cuts)
        p.Add(MachineLook.D([c0 - k * 1.1, f.Y - k * 1.1, f.Z0 + k * 2], [c0 + k * 1.1, f.Y + k * 1.1, f.Z0 + k * 2.18], MachineLook.Base)); // (the headstock behind it)
        p.Add(MachineLook.Work(MachineLook.D([c0 - k * 0.5, f.Y - k * 0.12, f.Z0 + k * 0.85], [c0 + k * 0.5, f.Y + k * 0.12, f.Z0 + k], MachineLook.Accent), "spin", "+z"));
        p.Stats(mass * 0.6, health * 0.7, c.Armour, c != Small ? null : 4);
        var width = Min(k * 3.2, f.X1 - f.X0 - k - k * 0.1);
        p.Set(("power", power), ("config", Py.L(Kw(("key", "recipe"), ("label", "Cuts"), ("group", "Work"), ("tooltip", "The die it cuts from the block in its bay. A lathe cuts dies of its own size.")))),
            ("storage", Kw(
                ("slots", Tray(f.X0 + k * 0.55, f.X1 - k * 0.55, R3(f.Base + k * 0.01), R3(f.Z0 + k * 2.2), R3(f.Z1 - k * 0.55))),
                ("press", Kw(("lathe", true),
                    ("bays", Py.L(Kw(("socket", "bay1"), ("min", Py.L(R3(c0 - k * 0.95), R3(f.Base), R3(f.Z0 - k * 1.5))), ("max", Py.L(R3(c0 + k * 0.95), R3(f.Wall), R3(f.Z0 + k * 1.5)))))),
                    ("maxAcross", MaxAcross(c)), ("speed", speed), ("outAt", Py.L(R3((f.X0 + f.X1) / 2), R3(f.Y0), R3(f.Z1 + k * 0.2))), ("outDir", "+z"))))),
            ("panels", Panels(p, f, width)));
        p.Add(StatusLamp(p, f)); // (its line state over its own screen: ProductionLines)
        return p;
    }

    private static Part Die(string family, Cls c, string colour)
    {
        var k = c.Cell;
        var s = k * 1.2;
        var h = k * 0.45;
        var nm = NameOf(c, $"{family} Die");
        var mass = c == Small ? 12 : c == Medium ? 36 : c == Large ? 2000 : 60000;
        var press = $"Fits a {(c.Name.Length > 0 ? c.Name.ToLowerInvariant() + " " : "")}press";
        var what = Array.IndexOf(SizedFamilies, family) >= 0
            ? $"{press} and makes it press a whole piece into one {family.ToLowerInvariant()} part, sized by the piece. A standard piece gives the standard part."
            : $"{press} and makes it stamp {family.ToLowerInvariant()} parts. Every material a {family.ToLowerInvariant()} part comes in uses this die.";
        var p = new Part(nm, c, Dies, what,
            role: "panel", tool: "wrench", group: "Production", color: Bed);
        p.Add(Box([-s, 0, -s], [s, h, s]));
        p.Add(Box([-s * 0.55, h, -s * 0.55], [s * 0.55, h * 1.5, s * 0.55], colour));
        p.Sock("mount", [0, 0, 0], "-y", kind: "pressdie");
        p.Stats(mass, 300 * c.X, c.Armour, c == Small ? 2 : null);
        p.Set(("pressDie", family), ("centreSockets", false));
        return p;
    }

    private static Part Rack(Cls c, int nx, int ny, int nz)
    {
        var k = c.Cell;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        var p = new Part(NameOf(c, "Die Rack"), c, Section, "Holds press dies on a shelf. A chute from it into a press brings a die to an empty slot.", role: "storage", group: "Production", color: Trim);
        var board = k * 0.12;
        var post = k * 0.14;
        p.Add(Box([x0, y0, z0], [x1, y0 + board, z1], Bed));
        foreach (var x in new[] { x0, x1 - post })
            foreach (var z in new[] { z0, z1 - post })
                p.Add(Box([x, y0 + board, z], [x + post, y1, z + post]));
        BlockSockets(p, nx, ny, nz, faces: "xy", limit: Lim(2, "+x", "-x", "+y", "-y"));
        p.Stats(60 * c.X, 200 * c.X, c.Armour, c == Small ? 3 : null);
        var gap = 0.015 * c.X;
        p.Set(("storage", Kw(("slots", Py.L(Kw(("min", Py.L(R3(x0 + post + gap), R3(y0 + board + gap), R3(z0 + gap))), ("max", Py.L(R3(x1 - post - gap), R3(y1 - gap), R3(z1 - gap))), ("rack", true), ("label", "Dies")))))));
        return p;
    }

    private static Part Housing(object sideO, Cls c, int n, (string Material, string Prefix, object Mx, object Hx, object Ax, string Colour) tier)
    {
        var side = Py.D(sideO);
        double x0 = c.Lo(), x1 = c.Hi(n);
        var nm = $"{tier.Prefix}Housing {Py.G(side)} m";
        var p = new Part(nm, c, "Housings", $"A stamped {tier.Material} box with open ends, to close over a mechanism. Its size follows the bar that made it; its material, its strength.",
            material: "steel", color: tier.Colour, group: "Structure");
        var t = Math.Max(0.02, side * 0.08);
        p.Add(Box([x0, x0, x0], [x1, x0 + t, x1]));
        p.Add(Box([x0, x1 - t, x0], [x1, x1, x1]));
        p.Add(Box([x0, x0 + t, x0], [x0 + t, x1 - t, x1]));
        p.Add(Box([x1 - t, x0 + t, x0], [x1, x1 - t, x1]));
        BlockSockets(p, n, n, n, limit: Lim(2, "+x", "-x", "+y", "-y", "+z", "-z"));
        object armour = tier.Ax is int ai ? (object)(c.Armour * ai) : Py.Round(c.Armour * Py.D(tier.Ax), 1);
        p.Stats(Py.Round(4 * side * side * c.KgM2 * Py.D(tier.Mx), 1), Py.RoundInt(4 * side * side * c.HealthM2 * Py.D(tier.Hx)), armour, c != Small ? null : 2);
        return p;
    }

    private static PyDict BayRow(string label, string[] sections, string[] materials, object lo, object hi, object? use = null, object? across = null)
    {
        var b = Kw(("label", label), ("sections", sections.Cast<object?>().ToList()), ("materials", materials.Cast<object?>().ToList()), ("minAcross", lo), ("maxAcross", hi));
        if (use is not null)
            b["use"] = use;
        else
            b["useAcross"] = across;
        return b;
    }


    // ---- Sized dies (the Press rework, phase 1: Pressed.cs). A feature's box is in the blank's unit frame (x its width, y its height, z its
    // length); "proud" axes move a face outward off a shared plane, "clamp" axes hold its size to minCm..maxCm. Structure only: no bay 2.

    private static PyDict Feature(string tag, double[] box, string look, string? proud = null, string? clamp = null, double? min = null, double? max = null, string[]? blanks = null)
    {
        var f = Kw(("tag", tag), ("box", box.Cast<object?>().ToList()));
        if (proud != null)
            f["proud"] = proud;
        if (clamp != null)
            f["clamp"] = clamp;
        if (min != null)
            f["minCm"] = min;
        if (max != null)
            f["maxCm"] = max;
        f["look"] = look;
        if (blanks != null)
            f["blanks"] = blanks.Cast<object?>().ToList();
        return f;
    }

    private static PyDict Pattern(string id, string like, string label, string preset, string[]? blanks = null, List<object?>? features = null, PyDict? bay2 = null, List<object?>? scale = null, string? along = null, bool nearest = false)
    {
        var p = Kw(("id", id), ("like", like), ("label", label), ("preset", preset));
        if (nearest) // (its presets are knots: a blank takes the nearest one's part as its like, sized-parts design 6.2)
            p["likeBy"] = "nearest";
        if (along != null) // (a like body: the like part's own boxes stretched to the blank, its length along this axis of the part; "" its longest)
        {
            p["body"] = "like";
            if (along.Length > 0)
                p["along"] = along;
        }
        if (blanks != null)
            p["blanks"] = blanks.Cast<object?>().ToList();
        if (bay2 != null)
            p["bay2"] = bay2;
        if (scale != null)
            p["scale"] = scale;
        if (features != null)
            p["features"] = features;
        return p;
    }

    /// <summary>A bay 2 kit by formula: <c>clamp(ceil(base + rate x measure), 1, max)</c> of a part or a bulk material.</summary>
    private static PyDict Kit(string what, bool part, double @base, double rate, string by, int? max = null)
    {
        var k = Kw((part ? "part" : "material", what), ("base", @base), ("rate", rate), ("by", by));
        if (max != null)
            k["max"] = max;
        return k;
    }

    /// <summary>A number scaled by the blank: <c>like x (measure / preset's)^power x (S / preset's S)</c>; <paramref name="ref"/> stands in for a like value of 0.</summary>
    private static PyDict Scale(string field, string by, double power, bool strength, double? @ref = null)
    {
        var k = Kw(("field", field), ("by", by), ("power", power), ("strength", strength));
        if (@ref != null)
            k["ref"] = @ref;
        return k;
    }

    /// <summary>A die's sockets at the middle of each face of the blank: what a drive part links by.</summary>
    private static List<object?> FaceSockets() => Py.L(
        Kw(("name", "+x"), ("at", Py.L(1, 0.5, 0.5)), ("dir", "+x")), Kw(("name", "-x"), ("at", Py.L(0, 0.5, 0.5)), ("dir", "-x")),
        Kw(("name", "+y"), ("at", Py.L(0.5, 1, 0.5)), ("dir", "+y")), Kw(("name", "-y"), ("at", Py.L(0.5, 0, 0.5)), ("dir", "-y")),
        Kw(("name", "+z"), ("at", Py.L(0.5, 0.5, 1)), ("dir", "+z")), Kw(("name", "-z"), ("at", Py.L(0.5, 0.5, 0)), ("dir", "-z")));

    private static PyDict SizedDie(string die, string[] blanks, string[] materials, int[] across, int[] length, double[] aspect, double secBase, double perKg, List<object?> features, List<object?> patterns, List<object?>? sockets = null) =>
        Kw(("die", die), ("blanks", blanks.Cast<object?>().ToList()), ("materials", materials.Cast<object?>().ToList()), ("across", Py.L(across[0], across[1])),
            ("length", Py.L(length[0], length[1])), ("aspect", Py.L(aspect[0], aspect[1])), ("seconds", Kw(("base", secBase), ("perKg", perKg))),
            ("features", features), ("sockets", sockets ?? Py.L()), ("patterns", patterns));

    private static List<object?> SizedDies()
    {
        string[] metals = ["metal", "steel", "bronze"];
        // Bracket: a web in an angle's corner and bolt bosses on its legs; a plate's flanges along two edges; a bar's two bosses.
        var bracket = Py.L(
            Feature("web", [0.25, 0.25, 0.15, 0.42, 0.42, 0.85], "bare", proud: "xy", blanks: ["l-angle"]),
            Feature("boss", [-0.04, 0.6, 0.14, 0.02, 0.72, 0.26], "dark", proud: "x", clamp: "x", min: 1, max: 6, blanks: ["l-angle"]),
            Feature("boss", [-0.04, 0.6, 0.74, 0.02, 0.72, 0.86], "dark", proud: "x", clamp: "x", min: 1, max: 6, blanks: ["l-angle"]),
            Feature("boss", [0.6, -0.04, 0.14, 0.72, 0.02, 0.26], "dark", proud: "y", clamp: "y", min: 1, max: 6, blanks: ["l-angle"]),
            Feature("boss", [0.6, -0.04, 0.74, 0.72, 0.02, 0.86], "dark", proud: "y", clamp: "y", min: 1, max: 6, blanks: ["l-angle"]),
            Feature("flange", [0, 0.3, 0, 0.08, 0.7, 1], "bare", proud: "y", clamp: "x", min: 1, max: 10, blanks: ["plate"]),
            Feature("flange", [0.08, 0.3, 0, 1, 0.7, 0.08], "bare", proud: "y", clamp: "z", min: 1, max: 10, blanks: ["plate"]),
            Feature("boss", [0.35, 0.98, 0.14, 0.65, 1.06, 0.26], "dark", proud: "y", blanks: ["solid"]),
            Feature("boss", [0.35, 0.98, 0.74, 0.65, 1.06, 0.86], "dark", proud: "y", blanks: ["solid"]));
        var bracketPatterns = Py.L(
            Pattern("l", "L Bracket", "L bracket", "l-angle 15x15x15 steel", blanks: ["l-angle", "solid"]),
            Pattern("corner", "Corner Bracket", "corner bracket", "l-angle 25x25x25 steel", blanks: ["l-angle"]),
            Pattern("gusset", "Gusset 0.25 m", "gusset", "plate 25x10x25 steel", blanks: ["plate"]));
        // Housing: a flange round each open end, a lid seam at 0.9 of its height on its sides, four bosses on its long edges.
        var housing = new List<object?>();
        foreach (var (z0, z1) in new[] { (0.0, 0.06), (0.94, 1.0) })
        {
            housing.Add(Feature("flange", [-0.04, 0.94, z0, 1.04, 1.04, z1], "bare", proud: "xy", clamp: "z", min: 1, max: 40));
            housing.Add(Feature("flange", [-0.04, -0.04, z0, 1.04, 0.06, z1], "bare", proud: "xy", clamp: "z", min: 1, max: 40));
            housing.Add(Feature("flange", [-0.04, 0.06, z0, 0.06, 0.94, z1], "bare", proud: "xy", clamp: "z", min: 1, max: 40));
            housing.Add(Feature("flange", [0.94, 0.06, z0, 1.04, 0.94, z1], "bare", proud: "xy", clamp: "z", min: 1, max: 40));
        }
        housing.Add(Feature("seam", [-0.01, 0.89, 0.1, 0.03, 0.91, 0.9], "dark", proud: "x", clamp: "y", min: 1, max: 4));
        housing.Add(Feature("seam", [0.97, 0.89, 0.1, 1.01, 0.91, 0.9], "dark", proud: "x", clamp: "y", min: 1, max: 4));
        foreach (var (x0, y0) in new[] { (-0.03, -0.03), (0.94, -0.03), (-0.03, 0.94), (0.94, 0.94) })
            housing.Add(Feature("boss", [x0, y0, 0.44, x0 + 0.09, y0 + 0.09, 0.56], "dark", proud: "xy"));
        var housingPatterns = Py.L(Pattern("housing", "Housing 0.5 m", "housing", "hollow 45x45x50 metal"));
        // Plate: a rolled lip round the rim (proud of both faces); per pattern a field of treads, rivets or ribs on its top face.
        var plate = Py.L(
            Feature("lip", [0, 0.3, 0, 0.04, 0.7, 1], "bare", proud: "y", clamp: "x", min: 1, max: 10),
            Feature("lip", [0.96, 0.3, 0, 1, 0.7, 1], "bare", proud: "y", clamp: "x", min: 1, max: 10),
            Feature("lip", [0.04, 0.3, 0, 0.96, 0.7, 0.04], "bare", proud: "y", clamp: "z", min: 1, max: 10),
            Feature("lip", [0.04, 0.3, 0.96, 0.96, 0.7, 1], "bare", proud: "y", clamp: "z", min: 1, max: 10));
        double[] field = [0.2, 0.4, 0.6, 0.8];
        var treads = new List<object?>();
        for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
            {
                var (hx, hz) = (i + j) % 2 == 0 ? (0.06, 0.025) : (0.025, 0.06); // (the diamond plate's alternate bars)
                treads.Add(Feature("tread", [field[i] - hx, 0.6, field[j] - hz, field[i] + hx, 0.66, field[j] + hz], "bare", proud: "y"));
            }
        var rivets = new List<object?>();
        foreach (var x in new[] { 0.1, 0.9 })
            foreach (var z in new[] { 0.1, 0.5, 0.9 })
                rivets.Add(Feature("rivet", [x - 0.02, 0.6, z - 0.02, x + 0.02, 0.67, z + 0.02], "dark", proud: "y", clamp: "xz", min: 1, max: 8));
        var ribs = new List<object?>();
        foreach (var x in field)
            ribs.Add(Feature("rib", [x - 0.015, 0.6, 0.08, x + 0.015, 0.68, 0.92], "dark", proud: "y", clamp: "x", min: 1, max: 6));
        var platePatterns = Py.L(
            Pattern("sheet", "Sheet Steel 1 m", "sheet", "plate 100x10x100 steel"),
            Pattern("plate", "Plate Steel 1 m", "plate", "plate 100x10x100 steel"),
            Pattern("steel", "Steel Plate", "steel plate", "plate 50x10x50 steel"),
            Pattern("tread", "Tread Plate 1 m", "tread plate", "plate 100x10x100 steel", features: treads),
            Pattern("armour", "Armour Plate 1 m", "armour plate", "plate 100x20x100 steel", features: rivets),
            Pattern("grate", "Mesh Grate 1 m", "ribbed plate", "plate 100x10x100 steel", features: ribs));
        var dies = Py.L(
            SizedDie("Bracket", ["l-angle", "plate", "solid"], metals, [10, 2880], [10, 800], [0.5, 8], 1, 0.02, bracket, bracketPatterns),
            SizedDie("Housing", ["hollow", "solid"], metals, [15, 2880], [15, 3200], [0.5, 8], 2, 0.01, housing, housingPatterns),
            SizedDie("Plate", ["plate"], ["metal", "steel", "bronze", "copper", "laminate", "wood", "planks"], [10, 2880], [25, 6400], [1, 64], 1, 0.01, plate, platePatterns));
        dies.AddRange(WorkingDies());
        return dies;
    }

    // ---- The working dies (the Press rework, phase 3): their parts work as their like part does, a kit from bay 2, numbers by the blank.
    // The old rows' parts are their presets: a Button from a 10 cm laminate strip, a Gear Set from a 15 cm steel rod, and so on.

    private static readonly (string Id, string Like, string Label, string Blank)[] DrivePatterns =
    [
        ("gearbox", "5-Speed Gearbox", "gearbox", "solid 50x50x75 steel"),
        ("cvt", "CVT", "CVT", "solid 50x50x70 steel"),
        ("auto", "Automatic Gearbox", "auto gearbox", "solid 50x50x80 steel"),
        ("reversing", "Reversing Gearbox", "reversing gearbox", "solid 50x50x65 steel"),
        ("diff", "Differential", "differential", "hollow 50x50x60 steel"),
        ("lsd", "Limited-Slip Differential", "limited-slip diff", "hollow 50x50x55 steel"),
        ("locking", "Locking Differential", "locking diff", "hollow 50x50x50 steel"),
    ];

    // ---- Sized parts phase 5 (docs/sandbox/design/sized-gun-engine-parts.md 6.2): engine pieces off the press. Each pattern's presets are
    // the four size files (Small to Capital), at their own bounds on the 5 cm lattice, and its knots: a blank takes the nearest one's part as
    // its like body ("likeBy": "nearest") and its engine scale runs through them (Pressed.EngineScale). A size the dies can't press (past a
    // Capital press's 28.8 m across or 32 m long) has no preset and keeps its Assembler recipe; the curve's end segment carries on past it.

    private const int EngineAcrossMax = 2880, EngineLengthMax = 3200; // (from about 0.7 of the Small files' across, design 6.2: the Cylinder die 30 cm, the Engine die 20; a 10 cm injector rail squashed its 2 mm gaps to half a mm)

    /// <summary>
    /// An engine piece pattern: id, the file's name after its size word, its blank's section, the like part's long axis (the blank's length
    /// goes along it), and its bay 2 kit.
    /// </summary>
    private static readonly (string Id, string Piece, string Section, string Along, Func<PyDict?> Bay2)[] EnginePatterns =
    [
        ("crank", "Crank Section", "solid", "z", () => Kit("Bearing", true, 2, 0.5, "length", 20)),
        ("flywheel", "Flywheel", "round", "z", () => Kit("Bearing", true, 1, 0, "length", 1)),
        ("carburettor", "Carburettor", "hollow", "y", () => null),
        ("injector", "Injector Rail", "solid", "y", () => Kit("Circuit Board", true, 1, 0, "length", 1)),
        ("turbo", "Turbo", "hollow", "y", () => Kit("Bearing", true, 2, 0, "length", 2)),
        ("supercharger", "Supercharger", "round", "y", () => Kit("Bearing", true, 2, 0, "length", 2)),
        ("starter", "Starter Motor", "round", "y", () => Kit("Copper Coil", true, 1, 0, "length", 1)),
        ("exhaust", "Exhaust Outlet", "pipe", "y", () => null),
        ("stack", "Smokestack", "pipe", "z", () => null),
    ];

    private static readonly string[] EngineSizes = ["Small", "Medium", "Large", "Capital"];

    /// <summary>A size file's preset blank on its bounds (across its two short axes' larger, length along <paramref name="along"/>), or null
    /// past what a die presses.</summary>
    private static string? EngineBlank(string part, string section, string along)
    {
        var d = CallOfSlop.ConstructParts.Find(part) ?? throw new InvalidOperationException($"press_parts: no engine piece {part}");
        var size = d.BoundsMax - d.BoundsMin;
        var ax = along == "y" ? 1 : along == "x" ? 0 : 2;
        static int Lattice(double m) => Math.Max(10, (int)Math.Round(m * 100 / 5, MidpointRounding.AwayFromZero) * 5);
        var len = Lattice(ax == 0 ? size.X : ax == 1 ? size.Y : size.Z);
        var across = Lattice(ax == 0 ? Math.Max(size.Y, size.Z) : ax == 1 ? Math.Max(size.X, size.Z) : Math.Max(size.X, size.Y));
        if (across > EngineAcrossMax || len > EngineLengthMax)
            return null;
        var wall = Math.Clamp(Lattice(across / 900.0), 5, 200); // (a ninth of across, to the lattice)
        return section == "pipe" ? $"pipe{wall} {across}x{across}x{len} steel" : $"{section} {across}x{across}x{len} steel";
    }

    /// <summary>The four plain cylinders' presets (those the Cylinder die can press).</summary>
    private static IEnumerable<(string Name, string Blank)> CylinderPresets() =>
        EngineSizes.Select(s => ($"{s} Cylinder", EngineBlank($"{s} Cylinder", "pipe", "y"))).Where(p => p.Item2 != null).Select(p => (p.Item1, p.Item2!));

    /// <summary>An engine pattern's presets.</summary>
    private static IEnumerable<(string Name, string Blank)> EnginePresets(string piece, string section, string along) =>
        EngineSizes.Select(s => ($"{s} {piece}", EngineBlank($"{s} {piece}", section, along))).Where(p => p.Item2 != null).Select(p => (p.Item1, p.Item2!));

    /// <summary>The Engine die (design 6.2): every other engine piece, a like body, its nearest preset's.</summary>
    private static PyDict EngineDie() =>
        SizedDie("Engine", ["solid", "round", "hollow", "pipe"], ["steel"], [20, EngineAcrossMax], [10, EngineLengthMax], [0.25, 6], 4, 0.01, Py.L(),
            EnginePatterns.Select(p => (object?)Pattern(p.Id, $"Small {p.Piece}", p.Piece.ToLowerInvariant(), EnginePresets(p.Piece, p.Section, p.Along).First().Blank,
                blanks: [p.Section], bay2: p.Bay2(), along: p.Along, nearest: true)).ToList());

    private static IEnumerable<object?> WorkingDies()
    {
        // Button: a raised cap on the top face (on a bar's end for a round), a bezel lip under it; copper for its contacts, by length.
        var button = Py.L(
            Feature("cap", [0.2, 1.0, 0.2, 0.8, 1.3, 0.8], "accent", proud: "y", clamp: "y", min: 0.5, max: 5, blanks: ["plate", "solid"]),
            Feature("lip", [0.12, 0.96, 0.12, 0.88, 1.04, 0.88], "dark", proud: "y", clamp: "y", min: 0.3, max: 2, blanks: ["plate", "solid"]),
            Feature("cap", [0.2, 0.2, 1.0, 0.8, 0.8, 1.15], "accent", proud: "z", clamp: "z", min: 0.5, max: 5, blanks: ["round"]));
        var copper = Kit("copper", false, 1, 2, "length", 99);
        yield return SizedDie("Button", ["plate", "solid", "round"], ["laminate", "metal", "steel", "wood"], [10, 180], [10, 800], [0.25, 16], 1, 0.02, button, Py.L(
            Pattern("button", "Button", "button", "plate 10x10x10 laminate", bay2: copper),
            Pattern("switch", "Switch", "switch", "plate 10x10x15 laminate", bay2: copper)));
        // Gear: a toothed rim round the middle of the rod and a hub at each end.
        var gear = Py.L(
            Feature("teeth", [-0.08, -0.08, 0.25, 1.08, 1.08, 0.75], "bare", proud: "xy"),
            Feature("hub", [0.35, 0.35, -0.06, 0.65, 0.65, 0.0], "dark", proud: "z", clamp: "z", min: 0.5, max: 4),
            Feature("hub", [0.35, 0.35, 1.0, 0.65, 0.65, 1.06], "dark", proud: "z", clamp: "z", min: 0.5, max: 4));
        yield return SizedDie("Gear", ["round", "solid"], ["steel", "bronze"], [10, 45], [5, 50], [0.2, 4], 3, 0.05, gear, Py.L(
            Pattern("set", "Gear Set", "gear set", "round 15x15x15 steel")));
        // Board: two rows of pads on its top face and an edge connector; copper for its tracks, by area.
        var board = new List<object?>();
        foreach (var x in new[] { 0.2, 0.4, 0.6, 0.8 })
            foreach (var z in new[] { 0.3, 0.6 })
                board.Add(Feature("pad", [x - 0.06, 1.0, z - 0.08, x + 0.06, 1.15, z + 0.08], "accent", proud: "y", clamp: "y", min: 0.3, max: 2));
        board.Add(Feature("edge", [0.1, 0.2, 0.92, 0.9, 0.8, 1.04], "bare", proud: "z", clamp: "z", min: 0.3, max: 3));
        yield return SizedDie("Board", ["plate"], ["laminate"], [20, 45], [20, 100], [0.5, 4], 3, 0.05, board, Py.L(
            Pattern("board", "Circuit Board", "circuit board", "plate 20x10x20 laminate", bay2: Kit("copper", false, 1, 10, "area", 99))));
        // Piston: ring grooves near its crown (proud rings) and a wrist pin boss across it; bronze for its rings, by volume.
        var piston = Py.L(
            Feature("ring", [-0.04, -0.04, 0.70, 1.04, 1.04, 0.74], "dark", proud: "xy"),
            Feature("ring", [-0.04, -0.04, 0.80, 1.04, 1.04, 0.84], "dark", proud: "xy"),
            Feature("pin", [-0.06, 0.4, 0.3, 1.06, 0.6, 0.45], "bare", proud: "x"));
        yield return SizedDie("Piston", ["round"], ["steel"], [10, 45], [10, 50], [0.5, 3], 5, 0.05, piston, Py.L(
            Pattern("set", "Piston Set", "piston set", "round 10x10x20 steel", bay2: Kit("bronze", false, 1, 20, "volume", 99))));
        // Cylinder: an end cap on each end of the tube and two ports; rubber for its seals, by size across.
        var cylinder = Py.L(
            Feature("cap", [-0.05, -0.05, 0.0, 1.05, 1.05, 0.06], "dark", proud: "xyz"),
            Feature("cap", [-0.05, -0.05, 0.94, 1.05, 1.05, 1.0], "dark", proud: "xyz"),
            Feature("port", [0.4, 1.0, 0.12, 0.6, 1.12, 0.2], "bare", proud: "y"),
            Feature("port", [0.4, 1.0, 0.8, 0.6, 1.12, 0.88], "bare", proud: "y"));
        // Sized parts phase 5: a like body (the nearest preset's cylinder stretched; its bore across, its stroke along y), Piston Sets by
        // volume; the aspect is 0.6 to 1.6 times the presets' (design 6.3: the stroke ratio's range); no die features (a like body is the file's own model).
        var cylPresets = CylinderPresets().ToList();
        var aspect = Calibres(cylPresets[0].Blank);
        yield return SizedDie("Cylinder", ["pipe"], ["steel"], [30, EngineAcrossMax], [15, EngineLengthMax], [Py.Round(0.6 * aspect, 2), Py.Round(1.6 * aspect, 2)], 6, 0.01, Py.L(), Py.L(
            Pattern("cylinder", "Small Cylinder", "cylinder", cylPresets[0].Blank, bay2: Kit("Piston Set", true, 1, 20, "volume", 20), along: "y", nearest: true)));
        yield return EngineDie();
        // Drive: bearing caps on its shaft ends, a split seam along it, a sump under it; Gear Sets by mass; torque grows as d^3 and strength.
        var drive = Py.L(
            Feature("cap", [0.325, 0.325, -0.04, 0.675, 0.675, 0.0], "bare", proud: "z", clamp: "z", min: 1, max: 20),
            Feature("cap", [0.325, 0.325, 1.0, 0.675, 0.675, 1.04], "bare", proud: "z", clamp: "z", min: 1, max: 20),
            Feature("seam", [-0.01, 0.48, 0.05, 1.01, 0.52, 0.95], "dark", proud: "x", clamp: "y", min: 0.5, max: 4),
            Feature("sump", [0.2, -0.1, 0.2, 0.8, 0.0, 0.8], "dark", proud: "y"));
        var gears = Kit("Gear Set", true, 1, 0.02, "mass", 20);
        // (no stand-in rating: every gearbox and differential in the catalogue is unrated, 0 = no limit, and a 900 N m stand-in capped every
        // pressed one there, so a pressed gearbox drove a truck at 11.5 m/s where its catalogue twin gave 17.9: Press phase 5. A rated like part scales.)
        var torque = Py.L(Scale("maxTorque", "across", 3, true));
        yield return SizedDie("Drive", ["hollow", "solid"], ["steel", "metal", "bronze"], [20, 720], [20, 1600], [0.5, 8], 3, 0.02, drive,
            DrivePatterns.Select(d => (object?)Pattern(d.Id, d.Like, d.Label, d.Blank, bay2: gears, scale: torque)).ToList(), FaceSockets());
        foreach (var die in WorkingDies3b())
            yield return die;
        yield return BarrelDie();
        yield return ReceiverDie();
    }

    // ---- Sized gun parts, phase 3 (docs/sandbox/design/sized-gun-engine-parts.md 4): the Receiver die. One free dimension, its across: a blank
    // keeps its preset's shape ("aspect": "preset"), and its scale s (across over the preset's) sizes the gun by the merge's law read with a
    // real number (ReceiverMerge.json's powers; ConstructMerge.ScaleGun), so a Rifle Receiver at 40x40x80 is today's 2x2 merge. The three
    // hand-made receivers first; the 26 generated ones are later data. Rifle and Shell share a size, so the Shell's preset is the hollow blank.

    private static readonly (string Id, string Like, string Label, string Blank)[] ReceiverPatterns =
    [
        ("rifle", "Rifle Receiver", "rifle receiver", "solid 20x20x40 steel"),
        ("heavy", "Heavy Receiver", "heavy receiver", "solid 25x25x50 steel"),
        ("shell", "Shell Receiver", "shell receiver", "hollow 20x20x40 steel"),
    ];

    private static PyDict ReceiverDie()
    {
        var gears = Kit("Gear Set", true, 1, 0.02, "mass", 20); // (as the Drive die: the action's gearing)
        var die = SizedDie("Receiver", ["solid", "hollow"], ["steel"], [15, 120], [30, 600], [1, 1], 4, 0.02, Py.L(),
            ReceiverPatterns.Select(r => (object?)Pattern(r.Id, r.Like, r.Label, r.Blank, bay2: gears, along: "z")).ToList());
        die["aspect"] = "preset";
        return die;
    }

    // ---- Sized gun parts, phase 1 (docs/sandbox/design/sized-gun-engine-parts.md 3): the Barrel die and its plain pattern. A barrel's length
    // is read in calibres (length over across), and its numbers are curves whose knots are the catalogue barrels: Stub (2 calibres), Standard
    // (5) and Long (9), read from their builder part files here, so tuning Long Barrel moves the curve. Each curve is held to the widest value
    // any catalogue barrel has, so no sized barrel leaves the range the builder is balanced for.

    private static readonly (string Part, string Builder, string Blank)[] PlainBarrels =
    [
        ("Stub Barrel", "Stub Barrel", "round 10x10x20 steel"),
        ("Gun Barrel", "Standard Barrel", "round 10x10x50 steel"),
        ("Long Gun Barrel", "Long Barrel", "round 10x10x90 steel"),
    ];

    private static readonly (string Stat, string Op)[] BarrelCurves = [("rangeM", "mul"), ("hipSpreadDeg", "mul"), ("adsTime", "add"), ("mobility", "add")];

    /// <summary>A builder part file's change to a stat by this op (the identity when it has none).</summary>
    private static double EffectOf(PartDef p, string stat, string op)
    {
        foreach (var e in p.Effects)
            if (e.Op == op && e.Field.Name.Equals(stat, StringComparison.OrdinalIgnoreCase))
                return Py.Round(Convert.ToDouble(e.Value, System.Globalization.CultureInfo.InvariantCulture), 4);
        return op == "mul" ? 1 : 0;
    }

    private static PyDict BarrelDie()
    {
        var curves = new List<object?>();
        var barrels = CallOfSlop.Parts.InCategory(PartCategory.Barrel).OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        foreach (var (stat, op) in BarrelCurves)
        {
            var points = new List<object?>();
            foreach (var (_, builder, blank) in PlainBarrels)
            {
                var b = CallOfSlop.Parts.Find(builder) ?? throw new InvalidOperationException($"press_parts: no builder part {builder}");
                var size = blank.Split(' ')[1].Split('x');
                points.Add(Py.L(int.Parse(size[2]) / int.Parse(size[0]), EffectOf(b, stat, op)));
            }
            var values = barrels.Select(b => EffectOf(b, stat, op)).Append(op == "mul" ? 1 : 0).ToList();
            curves.Add(Kw(("field", stat), ("by", "calibres"), ("points", points), ("clamp", Py.L(values.Min(), values.Max())), ("op", op)));
        }
        var crown = Py.L(Feature("crown", [-0.1, -0.1, 0, 1.1, 1.1, 0.04], "dark", proud: "xyz", clamp: "z", min: 1, max: 10));
        var patterns = Py.L(Pattern("plain", "Gun Barrel", "barrel", "round 10x10x50 steel", scale: curves, along: "z"));
        // Phase 2 (design 3.3): every other catalogue barrel is a pattern with one preset, its own effects times the plain curve read at
        // 5 x c / c_preset ("at": 5), so its preset is its file and a longer one gains range as a plain one does.
        // Each curve is held so the barrel's own change times it stays inside the widest catalogue values (a Coil Barrel, already x2 range,
        // gains no more range by length; it still loses it when short).
        var plainPoints = new List<List<object?>>();
        foreach (var (stat, op) in BarrelCurves)
            plainPoints.Add(PlainBarrels.Select(x => (object?)Py.L(Calibres(x.Blank), EffectOf(CallOfSlop.Parts.Find(x.Builder)!, stat, op))).ToList());
        foreach (var (id, builder, blank) in PatternBarrels())
        {
            var b = CallOfSlop.Parts.Find(builder)!;
            var scale = new List<object?>();
            for (var n = 0; n < BarrelCurves.Length; n++)
            {
                var (stat, op) = BarrelCurves[n];
                var values = barrels.Select(x => EffectOf(x, stat, op)).Append(op == "mul" ? 1 : 0).ToList();
                var own = EffectOf(b, stat, op);
                var (lo, hi) = op == "mul" ? (values.Min() / own, values.Max() / own) : (values.Min() - own, values.Max() - own);
                scale.Add(Kw(("field", stat), ("by", "calibres"), ("points", plainPoints[n]), ("clamp", Py.L(Py.Round(lo, 4), Py.Round(hi, 4))), ("op", op), ("at", 5)));
            }
            patterns.Add(Pattern(id, builder, b.Name.ToLowerInvariant(), blank, scale: scale, along: "z"));
        }
        // The artillery tube: its own curve through its two knots, the Howitzer Tube and the Long Gun Tube (design 3.3).
        var tubes = new List<object?>();
        foreach (var (stat, op) in BarrelCurves)
        {
            var points = new List<object?>();
            foreach (var (builder, blank) in ArtilleryTubes)
                points.Add(Py.L(Py.Round(Calibres(blank), 4), EffectOf(CallOfSlop.Parts.Find(builder)!, stat, op)));
            var values = barrels.Select(b => EffectOf(b, stat, op)).Append(op == "mul" ? 1 : 0).ToList();
            tubes.Add(Kw(("field", stat), ("by", "calibres"), ("points", points), ("clamp", Py.L(values.Min(), values.Max())), ("op", op)));
        }
        patterns.Add(Pattern("tube", ArtilleryTubes[0].Builder, "artillery tube", ArtilleryTubes[0].Blank, scale: tubes, along: "z"));
        var die = SizedDie("Barrel", ["round", "pipe"], ["steel"], [10, 120], [10, 1920], [1, 16], 4, 0.02, crown, patterns);
        die["fit"] = Py.L(0.7, 1.5); // (a barrel's bore over its receiver's: design section 5, the user's answer 4)
        return die;
    }

    /// <summary>The artillery tube pattern's knots: builder part and preset blank (design 3.3's rule, as <see cref="PatternBarrels"/>).</summary>
    private static (string Builder, string Blank)[] ArtilleryTubes => [("Howitzer Tube", PresetBlank("Howitzer Tube")), ("Long Gun Tube", PresetBlank("Long Gun Tube"))];

    /// <summary>Not patterns: the plain three (the plain pattern's knots), the artillery tubes (their own pattern) and the Barrel of Barrels (a bundle: an assembly).</summary>
    private static readonly string[] NotPatterns = ["Stub Barrel", "Standard Barrel", "Long Barrel", "Howitzer Tube", "Long Gun Tube", "Barrel of Barrels"];

    /// <summary>Every other catalogue barrel as (pattern id, builder part, preset blank), by name.</summary>
    private static IEnumerable<(string Id, string Builder, string Blank)> PatternBarrels() =>
        CallOfSlop.Parts.InCategory(PartCategory.Barrel).Where(b => !NotPatterns.Contains(b.Name)).OrderBy(b => b.Name, StringComparer.Ordinal)
            .Select(b => (PatternId(b.Name), b.Name, PresetBlank(b.Name)));

    /// <summary>A barrel's pattern id: its name's letters, less "barrel" ("Needle Barrel" needle, "Sawn-Off Barrel" sawn-off).</summary>
    private static string PatternId(string name)
    {
        var words = name.ToLowerInvariant().Split(' ').Where(w => w != "barrel");
        var id = string.Join("", words.Select(w => new string(w.Where(ch => ch is >= 'a' and <= 'z' or '-').ToArray())));
        return id.Length > 16 ? id[..16] : id;
    }

    /// <summary>
    /// Design 3.3's preset rule: across 10 cm times its bore's barrelScale width (sizedFor; rifle 1), length 250 cm a metre of its muzzle, each
    /// to the 5 cm lattice (at least 10 cm).
    /// </summary>
    private static string PresetBlank(string builder)
    {
        var b = CallOfSlop.Parts.Find(builder) ?? throw new InvalidOperationException($"press_parts: no builder part {builder}");
        static int Lattice(double cm) => Math.Max(10, (int)Math.Round(cm / 5, MidpointRounding.AwayFromZero) * 5);
        var across = Lattice(10 * CallOfSlop.Parts.Registry.ClassOf(b.SizedFor).Scale.X);
        var length = Lattice(250 * (b.Muzzles.Length > 0 ? Math.Abs(b.Muzzles[0].Z) : 0.2));
        return $"round {across}x{across}x{length} steel";
    }

    private static double Calibres(string blank)
    {
        var size = blank.Split(' ')[1].Split('x');
        return int.Parse(size[2]) / (double)int.Parse(size[0]);
    }

    // ---- Phase 3b: Shaft, Actuator, Spring, Cable, Control, Tank and Beam (design section 3.2). The patterns whose behaviour lives in
    // their geometry (a joint's leaf side and pivot, a suspension's axle socket, a shaft's end ports, a reel's hose point) have a like body:
    // the like part's own boxes and sockets stretched to the blank (Pressed.BuildLike). Tanks, beams and the cable kits are blank bodies.

    private static IEnumerable<object?> WorkingDies3b()
    {
        string[] metals = ["steel", "metal", "bronze"];
        // Shaft: a yoke flange at each end, 0.08 of the length; Gear Sets by length; torque as d^3 and strength (the Drive die's law).
        // (Auto picks by the blank's section: 15 cm a U joint, 20 a drive shaft, 25 a CV joint, 30 and up a stub axle; the Pattern setting the rest.)
        var shaft = Py.L(
            Feature("flange", [-0.2, -0.2, 0, 1.2, 1.2, 0.08], "dark", proud: "xyz", clamp: "z", min: 0.5, max: 40),
            Feature("flange", [-0.2, -0.2, 0.92, 1.2, 1.2, 1], "dark", proud: "xyz", clamp: "z", min: 0.5, max: 40));
        // (Gear Sets, not Bearings: the user, 2026-11-13. These were tier 1 Assembler parts taking a Gear Set, and bronze is a tier 2 foundry's.)
        var bearings = Kit("Gear Set", true, 0, 0.5, "length", 12);
        var shaftTorque = Py.L(Scale("maxTorque", "across", 3, true)); // (the U Joint is rated, 4,000 N m, and scales; the unrated stay unrated, as the Drive die)
        yield return SizedDie("Shaft", ["round", "pipe"], metals, [10, 180], [25, 3200], [1, 64], 2, 0.02, shaft, Py.L(
            Pattern("shaft", "Drive Shaft 1 m", "drive shaft", "round 20x20x100 steel", bay2: bearings, scale: shaftTorque, along: "z"),
            Pattern("ujoint", "U Joint", "U joint", "round 15x15x25 steel", bay2: bearings, scale: shaftTorque, along: "x"),
            Pattern("cv", "CV Joint", "CV joint", "round 25x25x25 steel", bay2: bearings, scale: shaftTorque, along: "x"),
            Pattern("stub", "Stub Axle", "stub axle", "round 30x30x35 steel", bay2: bearings, along: "z"),
            Pattern("hub", "Wheel Hub", "wheel hub", "round 30x30x35 steel", bay2: bearings, scale: shaftTorque, along: "z")));
        // Actuator: a gland collar on the barrel and an eye under its base; Servo Motors by mass (powered) or Bearings by size (free).
        var actuator = Py.L(
            Feature("gland", [-0.05, -0.05, 0.3, 1.05, 1.05, 0.34], "dark", proud: "xy", clamp: "z", min: 0.5, max: 20),
            Feature("eye", [0.3, 0.3, -0.08, 0.7, 0.7, 0], "bare", proud: "z", clamp: "z", min: 0.5, max: 30));
        var servos = Kit("Servo Motor", true, 1, 0.01, "mass", 8);
        var linear = Py.L(Scale("motorStrength", "across", 2, true), Scale("motorSpeed", "across", -1, false), Scale("sideLoad", "across", 3, true));
        var turning = Py.L(Scale("motorStrength", "across", 3, true), Scale("motorSpeed", "across", -1, false));
        yield return SizedDie("Actuator", ["pipe", "round", "solid"], ["steel", "metal"], [10, 720], [10, 3200], [0.25, 32], 3, 0.02, actuator, Py.L(
            Pattern("piston", "Piston 1x1 Medium", "piston", "round 25x25x50 steel", bay2: servos, scale: linear, along: "y"),
            Pattern("hinge", "Motor Hinge", "motor hinge", "solid 20x25x55 steel", blanks: ["solid"], bay2: servos, scale: turning, along: "y"),
            Pattern("turntable", "Turntable", "turntable", "solid 65x65x20 steel", blanks: ["solid"], bay2: servos, scale: turning, along: "y"),
            Pattern("pivot", "Free Pivot 1x1", "free pivot", "solid 30x25x30 steel", blanks: ["solid"], bay2: Kit("Bearing", true, 1, 2, "across", 16), along: "y")));
        // Spring: wound bands from 0.25 to 0.75 of its length; Bearings by size; load as the section's area and strength, travel as the length.
        var spring = new List<object?>();
        foreach (var z in new[] { 0.25, 0.45, 0.65 })
            spring.Add(Feature("coil", [-0.05, -0.05, z, 1.05, 1.05, z + 0.06], "dark", proud: "xy"));
        var load = Py.L(Scale("maxLoad", "across", 2, true));
        var springBearings = Kit("Bearing", true, 1, 2, "across", 16);
        yield return SizedDie("Spring", ["solid", "l-angle", "t-bar", "plate"], ["steel", "metal", "bronze"], [10, 720], [25, 1600], [1, 16], 2, 0.02, spring, Py.L(
            Pattern("suspension", "Suspension", "suspension", "solid 10x30x65 steel", bay2: Kit("Gear Set", true, 0, 2, "across", 16), scale: load, along: "y"),
            Pattern("leaf", "Leaf Spring", "leaf spring", "solid 10x55x90 steel", bay2: springBearings, scale: load, along: "z"),
            Pattern("rigid", "Rigid Mount", "rigid mount", "solid 10x30x40 steel", bay2: springBearings, scale: load, along: "y"),
            Pattern("light", "Light Suspension", "light suspension", "solid 25x25x30 steel", bay2: springBearings, scale: load, along: "y")));
        // Cable: a ribbed jacket (a ring every 0.2 of its length) and a plug at each end; copper or rubber by length (the user's "cable by length").
        var cable = new List<object?>();
        foreach (var z in new[] { 0.2, 0.4, 0.6, 0.8 })
            cable.Add(Feature("rib", [-0.04, -0.04, z - 0.015, 1.04, 1.04, z + 0.015], "dark", proud: "xy", clamp: "z", min: 0.5, max: 10));
        cable.Add(Feature("plug", [0.25, 0.25, -0.05, 0.75, 0.75, 0], "bare", proud: "z", clamp: "z", min: 0.5, max: 20));
        cable.Add(Feature("plug", [0.25, 0.25, 1, 0.75, 0.75, 1.05], "bare", proud: "z", clamp: "z", min: 0.5, max: 20));
        var copperByLength = Kit("copper", false, 1, 3, "length", 99);
        var rubberByLength = Kit("rubber", false, 1, 3, "length", 99);
        var reach = Py.L(Scale("hoseLength", "length", 1, false));
        yield return SizedDie("Cable", ["round", "pipe"], ["copper", "steel", "metal"], [10, 180], [10, 1600], [0.5, 64], 1, 0.02, cable, Py.L(
            Pattern("loom", "Wiring Loom", "wiring loom", "round 10x10x30 copper", bay2: rubberByLength),
            Pattern("coil", "Copper Coil", "copper coil", "round 25x25x25 copper"),
            Pattern("power", "Power Lead Reel", "power lead reel", "round 45x45x60 steel", bay2: copperByLength, scale: reach, along: "x"),
            Pattern("data", "Data Lead Reel", "data lead reel", "round 45x45x60 steel", bay2: copperByLength, scale: reach, along: "x"),
            Pattern("hose", "Fluid Hose Reel", "fluid hose reel", "round 45x45x60 steel", bay2: rubberByLength, scale: reach, along: "x"),
            Pattern("fuel", "Fuel Line", "fuel line", "pipe5 15x15x50 steel", blanks: ["pipe"], bay2: rubberByLength, along: "x")));
        // Control: a face plate on its top end and a cable gland on its side; Circuit Boards by its largest face.
        var control = Py.L(
            Feature("face", [0.15, 0.15, 1, 0.85, 0.85, 1.03], "bare", proud: "z", clamp: "z", min: 0.3, max: 3),
            Feature("gland", [0.4, -0.08, 0.2, 0.6, 0, 0.35], "dark", proud: "y", clamp: "y", min: 0.5, max: 10));
        var boards = Kit("Circuit Board", true, 1, 4, "area", 16);
        var gates = new (string Id, string Like, string Label)[]
        {
            ("and", "AND Gate", "AND gate"), ("not", "NOT Gate", "NOT gate"), ("timer", "Timer", "timer"), ("delay", "Delay", "delay"),
            ("compare", "Compare", "compare"), ("proximity", "Proximity Sensor", "proximity sensor"), ("speed", "Speed Sensor", "speed sensor"),
        };
        yield return SizedDie("Control", ["plate", "hollow", "solid"], ["laminate", "metal", "steel"], [10, 720], [10, 800], [0.1, 8], 2, 0.02, control,
            gates.Select(g => (object?)Pattern(g.Id, g.Like, g.Label, "solid 20x20x25 laminate", bay2: boards, along: "y")).ToList());
        // Tank: a dished cap on each end, a filler neck at the middle of its top, two saddle bands; capacity as its volume.
        var tank = Py.L(
            Feature("cap", [0.1, 0.1, -0.04, 0.9, 0.9, 0], "dark", proud: "z", clamp: "z", min: 1, max: 50),
            Feature("cap", [0.1, 0.1, 1, 0.9, 0.9, 1.04], "dark", proud: "z", clamp: "z", min: 1, max: 50),
            Feature("neck", [0.42, 1, 0.45, 0.58, 1.1, 0.55], "bare", proud: "y", clamp: "y", min: 1, max: 40),
            Feature("band", [-0.03, -0.03, 0.2, 1.03, 1.03, 0.25], "dark", proud: "xy"),
            Feature("band", [-0.03, -0.03, 0.75, 1.03, 1.03, 0.8], "dark", proud: "xy"));
        var capacity = Py.L(Scale("fuelCapacity", "volume", 1, false));
        yield return SizedDie("Tank", ["pipe", "hollow"], metals, [20, 2880], [25, 6400], [0.5, 16], 3, 0.005, tank, Py.L(
            Pattern("fuel", "Fuel Tank", "fuel tank", "hollow 50x50x50 steel", scale: capacity),
            Pattern("small", "Small Fuel Tank", "small fuel tank", "hollow 40x40x40 steel", scale: capacity),
            Pattern("drum", "Fuel Drum", "fuel drum", "pipe5 45x45x90 steel", blanks: ["pipe"], scale: capacity),
            Pattern("fluid", "Fluid Tank", "fluid tank", "hollow 45x45x50 steel", scale: capacity),
            Pattern("smallfluid", "Small Fluid Tank", "small fluid tank", "hollow 40x40x40 steel", scale: capacity)), FaceSockets());
        // Beam: an end flange at each end, 0.05 of its length and 1.15 of its section; a column's base plates, a strut's middle band.
        var beam = Py.L(
            Feature("flange", [-0.075, -0.075, 0, 1.075, 1.075, 0.05], "bare", proud: "xy", clamp: "z", min: 1, max: 50),
            Feature("flange", [-0.075, -0.075, 0.95, 1.075, 1.075, 1], "bare", proud: "xy", clamp: "z", min: 1, max: 50));
        yield return SizedDie("Beam", ["solid", "hollow", "i-beam", "c-channel", "t-bar", "round"], ["steel", "metal", "bronze", "wood", "concrete"], [10, 2880], [50, 12800], [2, 64], 2, 0.002, beam, Py.L(
            Pattern("girder", "Frame Beam", "girder", "i-beam 50x50x200 steel"),
            Pattern("column", "Frame Beam", "column", "hollow 50x50x200 steel", features: Py.L(
                Feature("base", [-0.2, -0.2, -0.03, 1.2, 1.2, 0], "dark", proud: "z", clamp: "z", min: 1, max: 30),
                Feature("base", [-0.2, -0.2, 1, 1.2, 1.2, 1.03], "dark", proud: "z", clamp: "z", min: 1, max: 30))),
            Pattern("strut", "Frame Beam", "strut", "solid 20x20x100 steel", features: Py.L(
                Feature("band", [-0.06, -0.06, 0.47, 1.06, 1.06, 0.53], "dark", proud: "xy")))));
    }

    /// <summary>
    /// The catalogue parts the sized dies' standard blanks give: the Bracket die's four (in every metal it works), every Housing and three
    /// plates. Each blank is the old row's piece made whole (an L Bracket from a 15 cm angle, a Corner Bracket from a 25 cm one), held to what
    /// a press of the class that always made it takes (PresetAcross: a Housing 0.5 m is a 45 cm bar 50 cm long, on a Small Press). A press on
    /// Auto picks the pattern from the blank (PressDie.PatternFor: the first preset of that blank); its Pattern setting picks the others that
    /// share a blank (the Plate die's plate, tread and grate).
    /// </summary>
    private static List<object?> Presets()
    {
        var r = new List<object?>();
        void Add(string part, string die, string pattern, string blank) => r.Add(Kw(("part", part), ("die", die), ("pattern", pattern), ("blank", blank)));
        foreach (var m in new[] { "steel", "metal", "bronze" })
        {
            Add("L Bracket", "bracket", "l", $"l-angle 15x15x15 {m}");
            Add("Corner Bracket", "bracket", "corner", $"l-angle 25x25x25 {m}");
            Add("Gusset 0.25 m", "bracket", "gusset", $"plate 25x10x25 {m}");
            Add("Gusset 0.5 m", "bracket", "gusset", $"plate {PresetAcross(50, Small)}x10x50 {m}");
        }
        foreach (var (sideO, c, _) in Housings)
        {
            var cm = (int)Math.Round(Py.D(sideO) * 100);
            var across = PresetAcross(cm, c);
            foreach (var tier in MaterialTiers)
                Add($"{tier.Prefix}Housing {Py.G(Py.D(sideO))} m", "housing", "housing", $"hollow {across}x{across}x{cm} {tier.Material}");
        }
        Add("Button", "button", "button", "plate 10x10x10 laminate");
        Add("Switch", "button", "switch", "plate 10x10x15 laminate");
        Add("Gear Set", "gear", "set", "round 15x15x15 steel");
        Add("Circuit Board", "board", "board", "plate 20x10x20 laminate");
        Add("Piston Set", "piston", "set", "round 10x10x20 steel");
        foreach (var (name, blank) in CylinderPresets())
            Add(name, "cylinder", "cylinder", blank);
        // Sized parts phase 5: the Engine die's presets (they keep their Assembler recipes until that design's phase 6).
        foreach (var p in EnginePatterns)
            foreach (var (name, blank) in EnginePresets(p.Piece, p.Section, p.Along))
                Add(name, "engine", p.Id, blank);
        foreach (var d in DrivePatterns)
            Add(d.Like, "drive", d.Id, d.Blank);
        Add("Steel Plate", "plate", "steel", "plate 50x10x50 steel");
        Add("Sheet Steel 1 m", "plate", "sheet", "plate 100x10x100 steel");
        Add("Plate Steel 1 m", "plate", "plate", "plate 100x10x100 steel"); // (the same blank as the sheet's: a press set to this pattern makes it)
        Add("Tread Plate 1 m", "plate", "tread", "plate 100x10x100 steel");
        Add("Mesh Grate 1 m", "plate", "grate", "plate 100x10x100 steel");
        Add("Armour Plate 1 m", "plate", "armour", "plate 100x20x100 steel");
        // Press phase 5: the phase 3b dies' patterns, each from its pattern's own preset blank (several share one: the Pattern setting or an
        // order picks).
        foreach (var (part, die, pattern, blank) in Presets3b)
            Add(part, die, pattern, blank);
        // The Frame Beam is the Beam die's girder (the user, 2026-11-13, phase 5 question 3): a 2 m I-beam held to what a Small Press takes
        // (45 cm), so a start's first frame needs no tier 2 press. The other building pieces stay on the Assembler.
        Add("Frame Beam", "beam", "girder", $"i-beam {PresetAcross(50, Small)}x{PresetAcross(50, Small)}x200 steel");
        // Sized gun parts phase 1: the plain barrels are the Barrel die's knots. Their Assembler recipes stay until that design's phase 6.
        foreach (var (part, _, blank) in PlainBarrels)
            Add(part, "barrel", "plain", blank);
        // Phase 2: the other barrels (their items: the builder part's name, ConstructWeaponParts) and the two artillery tubes.
        foreach (var (id, builder, blank) in PatternBarrels())
            Add(builder, "barrel", id, blank);
        foreach (var (builder, blank) in ArtilleryTubes)
            Add(builder, "barrel", "tube", blank);
        // Phase 3: the hand-made receivers are the Receiver die's presets (their Assembler recipes stay until that design's phase 6).
        foreach (var (id, like, _, blank) in ReceiverPatterns)
            Add(like, "receiver", id, blank);
        return r;
    }

    private static readonly (string Part, string Die, string Pattern, string Blank)[] Presets3b =
    [
        ("Drive Shaft 1 m", "shaft", "shaft", "round 20x20x100 steel"),
        ("U Joint", "shaft", "ujoint", "round 15x15x25 steel"),
        ("CV Joint", "shaft", "cv", "round 25x25x25 steel"),
        ("Stub Axle", "shaft", "stub", "round 30x30x35 steel"),
        ("Wheel Hub", "shaft", "hub", "round 30x30x35 steel"),
        ("Piston 1x1 Medium", "actuator", "piston", "round 25x25x50 steel"),
        ("Motor Hinge", "actuator", "hinge", "solid 20x25x55 steel"),
        ("Turntable", "actuator", "turntable", "solid 65x65x20 steel"),
        ("Free Pivot 1x1", "actuator", "pivot", "solid 30x25x30 steel"),
        ("Suspension", "spring", "suspension", "solid 10x30x65 steel"),
        ("Leaf Spring", "spring", "leaf", "solid 10x55x90 steel"),
        ("Rigid Mount", "spring", "rigid", "solid 10x30x40 steel"),
        ("Light Suspension", "spring", "light", "solid 25x25x30 steel"),
        ("Wiring Loom", "cable", "loom", "round 10x10x30 copper"),
        ("Copper Coil", "cable", "coil", "round 25x25x25 copper"),
        ("Power Lead Reel", "cable", "power", "round 45x45x60 steel"),
        ("Data Lead Reel", "cable", "data", "round 45x45x60 steel"),
        ("Fluid Hose Reel", "cable", "hose", "round 45x45x60 steel"),
        ("Fuel Line", "cable", "fuel", "pipe5 15x15x50 steel"),
        ("AND Gate", "control", "and", "solid 20x20x25 laminate"),
        ("NOT Gate", "control", "not", "solid 20x20x25 laminate"),
        ("Timer", "control", "timer", "solid 20x20x25 laminate"),
        ("Delay", "control", "delay", "solid 20x20x25 laminate"),
        ("Compare", "control", "compare", "solid 20x20x25 laminate"),
        ("Proximity Sensor", "control", "proximity", "solid 20x20x25 laminate"),
        ("Speed Sensor", "control", "speed", "solid 20x20x25 laminate"),
        ("Fuel Tank", "tank", "fuel", "hollow 50x50x50 steel"),
        ("Small Fuel Tank", "tank", "small", "hollow 40x40x40 steel"),
        ("Fuel Drum", "tank", "drum", "pipe5 45x45x90 steel"),
        ("Fluid Tank", "tank", "fluid", "hollow 45x45x50 steel"),
        ("Small Fluid Tank", "tank", "smallfluid", "hollow 40x40x40 steel"),
    ];

    /// <summary>Every catalogue part a die presses from a standard blank (the Assembling generator moves the single-piece ones off the Assembler).</summary>
    internal static HashSet<string> PresetParts() => Presets().Select(p => (string)((PyDict)p!)["part"]!).ToHashSet(StringComparer.Ordinal);

    private static List<object?> LatheRows()
    {
        var r = new List<object?>();
        foreach (var (family, classes, block, tier, _) in Families)
            foreach (var c in classes)
            {
                object across = c.Key switch { "small" => 0.25, "medium" => 1, "large" => 4, _ => 8 };
                r.Add(Kw(("output", NameOf(c, $"{family} Die")), ("seconds", 15 * tier * (1 + Rank(c))),
                    ("bays", Py.L(BayRow("Block", ["solid"], block == "steel" ? ["steel"] : ["metal", "steel"], across, MaxAcross(c), use: across)))));
            }
        return r;
    }

    /// <summary>The Press Line: an Extruder cutting a plate strip into a press by chute, with its power; dies and the second bay's rod are the player's.</summary>
    private static PyDict Line() => Kw(
        ("format", "call-of-slop-construct-blueprint/1"),
        ("name", "Press Line"),
        ("description", "An Extruder chuted into a Press, with a bin and a battery: fit a die and tip in material."),
        ("steps", Py.L(
            Kw(("part", "Foundation")),
            Kw(("part", "Extruder"), ("on", 0), ("socket", "top21"), ("by", "bottom"), ("config", Kw(("profile", 1), ("width", 0.3), ("height", 0.3), ("cutLength", 2)))),
            Kw(("part", "Materials Bin"), ("on", 0), ("socket", "top01"), ("by", "bottom")),
            Kw(("part", "Battery"), ("on", 0), ("socket", "top02"), ("by", "-y")),
            Kw(("part", "Foundation"), ("on", 0), ("socket", "x+1"), ("by", "x-1")),
            Kw(("part", "Press"), ("on", 4), ("socket", "top00"), ("by", "-y.0.0")))),
        ("links", Py.L(Kw(("type", "chute"), ("from", 2), ("to", 1)), Kw(("type", "chute"), ("from", 1), ("to", 5)))));

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        foreach (var r in Presses)
        {
            parts.Add(("", Press(r)));
            parts.Add(("", Lathe(r)));
        }
        foreach (var (family, classes, _, _, colour) in Families)
            foreach (var c in classes)
                parts.Add(("Dies", Die(family, c, colour)));
        parts.Add(("", Rack(Small, 6, 6, 3)));
        parts.Add(("", Rack(Medium, 6, 6, 3)));
        foreach (var (side, c, n) in Housings)
            foreach (var t in MaterialTiers)
                parts.Add(("Housings", Housing(side, c, n, t)));
        WriteParts(o, Out, parts);
        var table = Kw(("format", Format), ("about", About), ("lathe", LatheRows()),
            ("strength", Kw(("of", "healthPerM3"), ("ref", 5000))), ("dies", SizedDies()), ("presets", Presets()));
        o.File(Table, PyJson.Dumps(table, 1) + "\n");
        o.File(Blueprint, PyJson.Dumps(Line(), 2) + "\n");
    }
}
