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
    private const string Format = "call-of-slop-pressing/1";
    private const string About = "The stamping table (docs/sandbox/CONSTRUCTS.md, 'The Press'). Written by the part kit (--gen-parts press_parts): fix it, not this file. "
        + "'rows' are a press's: with a die of family 'die', the first row whose bays hold what it asks runs. Each bay gives its 'sections' (Extrusion presets, round, pipe), 'materials', "
        + "'minAcross' and 'maxAcross' (m, the piece's smaller side) and what a stroke takes: 'use' (m) or 'useAcross' (times its size across). 'lathe' rows are a Die Lathe's, chosen as a setting.";
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
        ("Button", [Small], "metal", 1, "#c06040"),
        ("Bracket", [Small], "metal", 1, "#7f8a94"),
        ("Gear", [Small], "metal", 1, "#a8763a"),
        ("Board", [Small], "steel", 2, "#3f9b5c"),
        ("Piston", [Small], "steel", 2, "#6f7d8c"),
        ("Housing", [Small, Medium, Large, Capital], "steel", 2, "#55708a"),
        ("Cylinder", [Small, Medium, Large, Capital], "steel", 3, "#8a5570"),
    ];

    private static double MaxAcross(Cls c) => c.Key switch { "small" => 0.45, "medium" => 1.8, "large" => 7.2, _ => 28.8 }; // what a class's bays take: 1.8 cells

    private static int Rank(Cls c) => Array.IndexOf(Classes, c);

    // (side m, int or float; class; cells)
    private static readonly (object Side, Cls C, int N)[] Housings = [(0.25, Small, 1), (0.5, Small, 2), (1, Medium, 1), (2, Medium, 2), (4, Large, 1), (8, Large, 2), (16, Capital, 1)];

    // A housing's size follows the bar: this band of bar makes that side of housing.
    private static (object, object) HousingBand(double side) => side switch
    {
        0.25 => (0.1, 0.2), 0.5 => (0.25, 0.45), 1 => (0.5, 0.95), 2 => ((object)1, 1.8), 4 => ((object)2, 3.95), 8 => ((object)4, 7.2), _ => ((object)8, 28.8),
    };

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
        return new Frame(x0, x1, y0, y1, z0, z1, bse, crown, wall, y, centres);
    }

    /// <summary>The bays' cards, over their mouths on the back wall; the machine's own on the front of the crown.</summary>
    private static List<object?> Panels(Part p, Frame f, double baysWidth)
    {
        var c = p.Cls;
        var k = c.Cell;
        var proud = 0.004 * Math.Max(1, Math.Pow(c.X, 0.5));
        var h = k * 1.25;
        var o = new List<object?>();
        for (var i = 0; i < f.Centres.Count; i++)
            o.Add(Kw(("source", "press.bay"), ("slot", i), ("at", Py.L(f.Centres[i], R3((f.Wall + f.Crown) / 2), Py.Round(f.Z0 - proud, 4))), ("size", Py.L(R3(baysWidth), R3(h))), ("face", "-z"), ("up", "+y"), ("rows", 5)));
        o.Add(Kw(("source", "press.die"), ("at", Py.L(R3((f.X0 + f.X1) / 2), R3((f.Crown + f.Y1) / 2), Py.Round(f.Z1 + proud, 4))), ("size", Py.L(R3(k * 5), R3(k * 1.5))), ("face", "+z"), ("up", "+y"), ("rows", 4)));
        return o;
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
        p.Set(("power", power), ("storage", Kw(
                ("slots", Tray(f.X0 + k * 0.55, f.X1 - k * 0.55, R3(f.Base + k * 0.01), tz0, tz1)),
                ("press", Kw(("bays", bayList),
                    ("die", Kw(("socket", "dieslot"), ("min", Py.L(R3(mid - k * 2), R3(f.Base), R3(zm))), ("max", Py.L(R3(mid + k * 2), R3(f.Wall), R3(f.Z1 + k * 2))))),
                    ("maxAcross", MaxAcross(c)), ("speed", speed), ("outAt", Py.L(R3(mid), R3(f.Y0), R3(f.Z1 + k * 0.2))), ("outDir", "+z"))))),
            ("panels", Panels(p, f, width)));
        return p;
    }

    private static Part Lathe((Cls C, int Bays, int Speed, int Power, int Mass, int Health) row)
    {
        var (c, _, speed, power, mass, health) = row;
        var k = c.Cell;
        int nx = 7, ny = 6, nz = 5;
        var p = new Part(NameOf(c, "Die Lathe"), c, Section, "Cuts press dies from a solid block. Choose the die with the configure tool, and set the block in its bay. Needs power.",
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
        return p;
    }

    private static Part Die(string family, Cls c, string colour)
    {
        var k = c.Cell;
        var s = k * 1.2;
        var h = k * 0.45;
        var nm = NameOf(c, $"{family} Die");
        var mass = c == Small ? 12 : c == Medium ? 36 : c == Large ? 2000 : 60000;
        var p = new Part(nm, c, Dies, $"Fits a {(c.Name.Length > 0 ? c.Name.ToLowerInvariant() + " " : "")}press and makes it stamp {family.ToLowerInvariant()} parts. Every material a {family.ToLowerInvariant()} part comes in uses this die.",
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

    private static List<object?> Rows()
    {
        var r = new List<object?>();
        void Add(string d, string output, object seconds, params PyDict[] bays) =>
            r.Add(Kw(("die", d), ("output", output), ("seconds", seconds), ("bays", bays.Cast<object?>().ToList())));
        Add("Button", "Button", 1.5, BayRow("Strip", ["plate"], ["laminate"], 0.1, 0.45, use: 0.1), BayRow("Rod", ["round"], ["copper"], 0.1, 0.25, use: 0.05));
        // Bracket: the section picks the shape, the bar's size which of them. They are one tier, so every metal gives the same part.
        string[] metals = ["metal", "steel", "bronze"];
        Add("Bracket", "L Bracket", 1, BayRow("Angle", ["l-angle"], metals, 0.1, 0.2, use: 0.15));
        Add("Bracket", "Corner Bracket", 1.5, BayRow("Angle", ["l-angle"], metals, 0.25, 0.45, use: 0.25));
        Add("Bracket", "Gusset 0.25 m", 1, BayRow("Plate", ["plate"], metals, 0.25, 0.35, use: 0.25));
        Add("Bracket", "Gusset 0.5 m", 1.5, BayRow("Plate", ["plate"], metals, 0.4, 0.45, use: 0.5));
        Add("Gear", "Gear Set", 3, BayRow("Rod", ["round"], ["steel"], 0.1, 0.25, use: 0.15));
        Add("Board", "Circuit Board", 3, BayRow("Sheet", ["plate"], ["laminate"], 0.2, 0.45, use: 0.2), BayRow("Wire", ["round"], ["copper"], 0.1, 0.15, use: 0.05));
        Add("Piston", "Piston Set", 5, BayRow("Rod", ["round"], ["steel"], 0.1, 0.25, use: 0.2), BayRow("Bar", ["solid"], ["bronze"], 0.1, 0.25, use: 0.1));
        foreach (var (sideO, _, _) in Housings)
        {
            var side = Py.D(sideO);
            var (lo, hi) = HousingBand(side);
            foreach (var tier in MaterialTiers)
                Add("Housing", $"{tier.Prefix}Housing {Py.G(side)} m", 2 + side * 0.5, BayRow("Bar", ["hollow", "solid"], [tier.Material], lo, hi, across: 1));
        }
        foreach (var (c, lo, hi) in new (Cls, object, object)[] { (Small, 0.2, 0.45), (Medium, 0.5, 1.8), (Large, 2, 7.2), (Capital, 8, 28.8) })
            Add("Cylinder", c != Small ? NameOf(c, "Cylinder") : "Small Cylinder", 6, BayRow("Tube", ["pipe"], ["steel"], lo, hi, across: 1), BayRow("Plate", ["plate"], ["steel"], lo, hi, across: 0.5));
        return r;
    }

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
        var table = Kw(("format", Format), ("about", About), ("rows", Rows()), ("lathe", LatheRows()));
        o.File(Table, PyJson.Dumps(table, 1) + "\n");
        o.File(Blueprint, PyJson.Dumps(Line(), 2) + "\n");
    }
}
