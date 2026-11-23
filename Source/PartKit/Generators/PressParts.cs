using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Press family: Constructs/Parts/Press (presses, Die Lathes, dies, Die Racks, housings), Constructs/Pressing.json and the Press Line
/// blueprint (docs/sandbox/CONSTRUCTS.md, "The Press"). Was tools/constructs/press_parts.py.
///
/// A press stamps parts out of extruded pieces. It has 2 or 3 bays and a die slot; the die sets the family, the material the tier, a
/// piece's size across or its length the size or the count. The Press is one Core and its blocks (its size comes from them); a die is one
/// 0.5 m cassette per family that fits it at any size, and one Die Lathe cuts any die from a steel bar (Press part 2, design page drawing
/// 17: dies lost their size classes). A new die family is a row here. The table is flat on purpose:
/// one row per family, material and size band, so the coverage of the Assembler's list can be counted. Run the part kit's assembling (--gen-parts assembling)
/// after it for the machines' recipes.
/// </summary>
public sealed partial class PressParts : IPartGenerator
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

    // The one Die Lathe (Press part 2: dies have no size classes, so one lathe cuts them all): (speed, W, kg, health). The Press itself is
    // one Press Core and any number of Press Blocks (PressCore, PressBlock; design page drawings 17 and 17b).
    private const int LatheSpeed = 1, LathePower = 900, LatheMass = 520, LatheHealth = 800;

    // Dies: family, die tier (1: a starter), colour. One die a family (Press part 2): a 0.5 m cassette, cut from a 0.25 m steel bar.
    private static readonly (string Family, int Tier, string Colour)[] Families =
    [
        ("Button", 1, "#c06040"),
        ("Bracket", 1, "#7f8a94"),
        ("Gear", 1, "#a8763a"),
        ("Board", 2, "#3f9b5c"),
        ("Piston", 2, "#6f7d8c"),
        ("Housing", 2, "#55708a"),
        ("Cylinder", 3, "#8a5570"),
        ("Plate", 1, "#8c96a0"),
        ("Drive", 2, "#4f7a5a"),
        ("Shaft", 2, "#6b7f8f"),
        ("Actuator", 3, "#8f6b3f"),
        ("Spring", 2, "#5f8f6b"),
        ("Cable", 1, "#b0703a"),
        ("Control", 2, "#3f6f9f"),
        ("Tank", 2, "#9f4a3f"),
        ("Beam", 1, "#7a7f88"),
        ("Barrel", 2, "#4a4d52"),
        ("Receiver", 2, "#4a5146"),
        ("Engine", 3, "#6a4f3a"),
    ];

    // The dies whose stroke presses a whole blank to one part (Pressing.json "dies", the Press rework): they have no old rows.
    private static readonly string[] SizedFamilies = ["Bracket", "Housing", "Plate", "Button", "Gear", "Board", "Piston", "Cylinder", "Drive", "Shaft", "Actuator", "Spring", "Cable", "Control", "Tank", "Beam", "Barrel", "Receiver", "Engine"];

    private static double MaxAcross(Cls c) => c.Key switch { "small" => 0.45, "medium" => 1.8, "large" => 7.2, _ => 28.8 }; // what a class's bays take: 1.8 cells

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

    // ---- The Press on the block system (design page drawings 17 and 17b; ConstructPressBlocks.cs) -------------------------------
    // A Press Core, a 2 m cube (the die slot, the screen, the bays: bay 1 for the blank, bay 2 the kit hopper), and one Press Block, a
    // 2 m cube, welded beside it and to each other: blocks on the Core's layer make the bed, blocks stacked on the bed's two long edges its
    // walls. The machine reads its daylight, width, bites and speed from them at each rebuild.
    private const int CoreCells = 8; // the Core: 8 Small cells a side, 2 m
    private const int CorePower = 900, CoreMass = 900, CoreHealth = 1200;
    private const int BlockMass = 4000, BlockHealth = 2500;

    private static PyDict BlockKey(string role, double[]? origin = null) => origin is null
        ? Kw(("family", "press"), ("role", role))
        : Kw(("family", "press"), ("role", role), ("origin", Py.L(origin[0], origin[1], origin[2])));

    private static Part PressCore()
    {
        var c = Small;
        var k = c.Cell;
        const int n = CoreCells;
        var p = new Part("Press", c, Section, "A Press Core: stamps parts out of extruded pieces between two shells that slam shut over the blank. Alone it takes blanks up to 0.45 m across in its own small clamshell; weld Press Blocks beside it for a bed (along the flow from the Core; its width across the widest blank), stack blocks on the bed's two long edges for the shells (1.6 m of daylight, 2 m more a block; each side needs a clear strip as wide as its shells are tall), and it works faster with every block. The shells never stop for anyone. A die in its slot sets what it makes; blanks go in bay 1, a die's kit in bay 2. Needs power.",
            role: "storage", group: "Production", color: Blue);
        double x0 = c.Lo(), x1 = c.Hi(n), y0 = c.Lo(), y1 = c.Hi(n), z0 = c.Lo(), z1 = c.Hi(n);
        double mid = (x0 + x1) / 2, my = (y0 + y1) / 2, zm = (z0 + z1) / 2;
        const double cabH = 1.2, rim = 0.06, deckT = 0.05, leafH = 0.6, leafT = 0.08;
        var cab = y0 + cabH; // (the cabinet's top: the deck lies on it)
        var deck = cab + deckT; // (the bed: the die and the tray stand on it)
        var bayY = y0 + 0.6; // (the bays' sockets on the back: each Piece in 11 cm under, ConstructPorts.cs)
        var mk = 0.015;
        // The cabinet and the deck: the only collision (the leaves are looks that swing).
        p.Add(Box([x0, y0, z0], [x1, cab, z1]));
        p.Add(Box([x0 + rim, cab, z0 + rim], [x1 - rim, deck, z1 - rim], MachineLook.Base)!.Update(("paintable", false), ("finish", "tread plate")));
        foreach (var px in new[] { x0, x1 - 0.06 }) // (a steel post up each corner to the cell's top: the Core fills its 2 m cell, and the leaves swing between them)
            foreach (var pz in new[] { z0, z1 - 0.06 })
                p.Add(Box([px, cab, pz], [px + 0.06, y1, pz + 0.06], MachineLook.Steel)!.Update(("paintable", false), ("finish", "steel")));
        // The plinth and the cap band, front and back (a side's are seams: a Press Block welded there hides them).
        foreach (var (zl, zh) in new[] { (z0 - mk, z0), (z1, z1 + mk) })
        {
            p.Add(MachineLook.D([x0 + 0.004, y0, zl], [x1 - 0.004, y0 + 0.15, zh], MachineLook.Base));
            p.Add(MachineLook.D([x0 + 0.004, cab - 0.075, zl], [x1 - 0.004, cab - 0.01, zh], MachineLook.Base));
        }
        foreach (var (side, xl, xh) in new[] { ("+x", x1, x1 + mk), ("-x", x0 - mk, x0) })
        {
            p.Add(Seam(MachineLook.D([xl, y0, z0 + 0.004], [xh, y0 + 0.15, z1 - 0.004], MachineLook.Base)!, side));
            p.Add(Seam(MachineLook.D([xl, cab - 0.075, z0 + 0.004], [xh, cab - 0.01, z1 - 0.004], MachineLook.Base)!, side));
            for (var i = 0; i < 5; i++) // (louvres)
            {
                var ly = y0 + 0.4 + i * 0.09;
                p.Add(Seam(MachineLook.D([side == "+x" ? x1 - 0.004 : x0 - 0.02, ly, z0 + 0.45], [side == "+x" ? x1 + 0.02 : x0 + 0.004, ly + 0.04, z1 - 0.45], MachineLook.Base)!, side));
            }
        }
        // The small clamshell on the deck: two leaves hinged along the deck's front and back edges, each a side plate and a roof half that
        // meet over the die; they swing open flat out and slam shut while it works ("hinge": ConstructPressShells.cs), shut at rest.
        foreach (var sg in new[] { -1, 1 })
        {
            var edge = sg < 0 ? z0 + rim : z1 - rim; // (the hinge line, over the deck's edge)
            var inner = edge - sg * leafT;
            var top = deck + leafH;
            double[] pivot = [R3(mid), R3(deck), R3(edge)];
            var axis = sg < 0 ? "-z" : "+z";
            PyDict Leaf(double[] lo, double[] hi, string colour, string? finish = null, bool paintable = false)
            {
                var bx = MachineLook.Work(MachineLook.D(lo, hi, colour, finish, paintable), "hinge", axis, 95)!;
                bx["pivot"] = Py.L(pivot[0], pivot[1], pivot[2]);
                return bx;
            }
            double Zs(double a, double b) => Math.Min(a, b);
            double Zb(double a, double b) => Math.Max(a, b);
            p.Add(Leaf([x0 + 0.1, deck + 0.004, Zs(inner, edge)], [x1 - 0.1, top - 0.04, Zb(inner, edge)], Blue, paintable: true)); // (its side plate)
            var tip = zm + sg * 0.003; // (3 mm short of the middle: the two roof halves never share a face)
            p.Add(Leaf([x0 + 0.1, top - leafT, Zs(edge - sg * 0.04, tip)], [x1 - 0.1, top, Zb(edge - sg * 0.04, tip)], MachineLook.Base)); // (its roof half)
            var outer = sg < 0 ? edge - 0.012 : edge + 0.012;
            p.Add(Leaf([x0 + 0.12, top - 0.2, Zs(edge - sg * 0.004, outer)], [x1 - 0.12, top - 0.1, Zb(edge - sg * 0.004, outer)], MachineLook.Accent, "hazard paint")); // (a hazard band on its outer face)
            foreach (var rx in new[] { mid - 0.55, mid, mid + 0.55 })
                p.Add(Leaf([rx - 0.03, deck + 0.04, Zs(edge - sg * 0.004, outer + sg * 0.004)], [rx + 0.03, top - 0.22, Zb(edge - sg * 0.004, outer + sg * 0.004)], MachineLook.Base)); // (its ribs)
            for (var t = 0; t < 4; t++) // (red latch teeth along the roof half's leading edge)
            {
                var tx = x0 + 0.3 + t * 0.47;
                p.Add(Leaf([tx - 0.06, top - leafT - 0.05, Zs(tip - sg * 0.06, tip - sg * 0.006)], [tx + 0.06, top - leafT + 0.004, Zb(tip - sg * 0.06, tip - sg * 0.006)], "#c0392b"));
            }
            p.Add(MachineLook.D([x0 + 0.14, deck - 0.02, edge - 0.03], [x1 - 0.14, deck + 0.04, edge + 0.03], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+x")))); // (its hinge pin, which stays)
        }
        // The front: the die screen (Panels), the status lamp over it, an orange band and arrow down to the Piece out, a run-off lip,
        // a pressure gauge, the maker's plate and corner bolts.
        p.Add(MachineLook.D([x0 + 0.12, y0 + 0.17, z1], [x1 - 0.12, y0 + 0.27, z1 + mk], MachineLook.Out));
        MachineLook.Arrow(p, 2, z1 + mk, 1, 1, -1, y0 + 0.44, mid, k * 0.4, k * 0.6, MachineLook.Ink, mk);
        p.Add(MachineLook.D([mid - k * 1.2, y0 + 0.004, z1 - 0.004], [mid + k * 1.2, y0 + 0.1 - 0.004, z1 + k * 0.2 - 0.003], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([x0 + 0.1, y0 + 0.4, z1 - 0.004], [x0 + 0.28, y0 + 0.58, z1 + 0.036], "#e8e6df", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z"))));
        p.Add(MachineLook.D([x0 + 0.08, y0 + 0.38, z1 - 0.006], [x0 + 0.3, y0 + 0.6, z1 + 0.024], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z")))); // (its bezel)
        p.Add(MachineLook.D([x1 - 0.42, y0 + 0.42, z1 - 0.004], [x1 - 0.12, y0 + 0.56, z1 + 0.02], MachineLook.In)); // (the maker's plate)
        foreach (var bx in new[] { x0 + 0.035, x1 - 0.065 })
            foreach (var by in new[] { y0 + 0.2, cab - 0.13 })
                p.Add(MachineLook.D([bx, by, z1 - 0.004], [bx + 0.03, by + 0.03, z1 + 0.026], MachineLook.Steel, "steel"));
        // The back: each bay's Piece in with the one port look (MachineLook.Port), a white band under the bays' cards, oil lines up its
        // corners with a clamp each.
        var span = x1 - x0 - k;
        var centres = Enumerable.Range(0, 2).Select(i => R3(x1 - k * 0.5 - span * (i + 0.5) / 2)).ToList(); // (bay 1 on the left as you stand at the back)
        foreach (var bx in centres)
            MachineLook.Port(p, [bx, bayY - 0.11, z0], 2, -1, 0.13, 0.1, true);
        p.Add(MachineLook.D([x0 + 0.1, y0 + 0.8, z0 - mk], [x1 - 0.1, y0 + 0.83, z0], MachineLook.In));
        foreach (var hx in new[] { x0 + 0.03, x1 - 0.075 })
        {
            p.Add(MachineLook.D([hx, y0 + 0.17, z0 - 0.045], [hx + 0.045, cab - 0.09, z0 - 0.006], Hose, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"))));
            p.Add(MachineLook.D([hx - 0.012, y0 + 0.55, z0 - 0.055], [hx + 0.057, y0 + 0.61, z0 - 0.002], MachineLook.Steel, "steel"));
        }
        // Sockets: its feet (and blocks under it), the first Press Block of its bed beside it, the bays, the die on the deck.
        BlockSockets(p, n, n, n, faces: "y", limit: Lim(3, "+y", "-y"), skip: ["+y"]); // (nothing stands on its clamshell)
        p.Sock("+x", [R3(x1), R3(my), R3(zm)], "+x");
        p.Sock("-x", [R3(x0), R3(my), R3(zm)], "-x");
        for (var i = 0; i < centres.Count; i++)
            p.Sock($"bay{i + 1}", [centres[i], R3(bayY), R3(z0)], "-z", kind: "die");
        p.Sock("dieslot", [R3(mid), R3(deck), R3(zm)], "+y", kind: "pressdie");
        p.Stats(CoreMass, CoreHealth, c.Armour, 4);
        var bayList = centres.Select((bx, i) => (object?)Kw(("socket", $"bay{i + 1}"), ("min", Py.L(R3(bx - k * 0.95), R3(bayY - 0.3), R3(z0 - k * 1.5))), ("max", Py.L(R3(bx + k * 0.95), R3(bayY + 0.3), R3(z0 + k * 1.5))))).ToList();
        // (a Frame for the shared screens: the bays' cards on the back over their mouths and the die's on the front, both 0.85 m up; the lamp over the die's)
        var f = new Frame(x0, x1, y0, cab, z0, z1, deck, y0 + 0.75, cab, bayY, centres);
        var width = Min(k * 3.2, (x1 - x0 - k) / 2 - k * 0.1);
        p.Set(("power", CorePower), ("config", Py.L(Kw(("key", "recipe"), ("label", "Pattern"), ("group", "Work"),
                ("tooltip", "What its die makes of a blank: Auto lets the blank choose, or one of the die's patterns. On its die screen, Recipe opens a picker of them and Pattern steps through them.")))),
            ("storage", Kw(
                ("slots", Tray(x0 + k * 0.55, x1 - k * 0.55, R3(deck + k * 0.01), R3(zm + k * 1.3), R3(z1 - rim - leafT - 0.02))),
                ("press", Kw(("bays", bayList),
                    ("die", Kw(("socket", "dieslot"), ("min", Py.L(R3(mid - k * 2), R3(deck), R3(zm))), ("max", Py.L(R3(mid + k * 2), R3(deck + leafH - leafT), R3(z1 + k * 2))))),
                    ("maxAcross", MaxAcross(c)), ("speed", 1), ("outAt", Py.L(R3(mid), R3(y0), R3(z1 + k * 0.2))), ("outDir", "+z"))))),
            ("machineBlock", BlockKey("core", [R3(mid), R3(my), R3(zm)])),
            ("panels", Panels(p, f, width, pattern: true)));
        p.Add(StatusLamp(p, f)); // (its line state over its own screen: ProductionLines)
        return p;
    }

    private const string Hose = "#1d1f22";

    /// <summary>
    /// A Press Block: one 2 m cell of a press's bed or wall. Sean (2026-11-23): the beds and the frame pieces looked the same, so a press
    /// read as one giant black box. A block is drawn by what it is, from its seams (MachineBlocks: "seam", "seamFill" on "-y"):
    /// <list type="bullet">
    /// <item><b>A bed block</b> (nothing of its press under it): a framed machine table: a deck slab with the tread plate and the hazard
    /// edge and a leg at each corner, in the press's paint, and a dark body set well back under the deck. Welded to a block of its press, the bodies join legs to legs and
    /// the edge goes, so a bed reads as one table.</item>
    /// <item><b>A wall block</b> (a block of its press under it: a clamshell segment, which the game swings about its hinge,
    /// ConstructPressShells.cs): a solid steel lid, unpainted so it never takes the bed's colour, with a dark cap, two stiffeners across
    /// its top and a hazard band round its foot.</item>
    /// </list>
    /// Both solid (no hollow sections: Sean wants polygons saved). Its collision is the whole cell (a hidden box), so the bed, the walls,
    /// the swing check and the crush test are as they were.
    /// </summary>
    private static Part PressBlock()
    {
        var p = new Part("Press Block", Small, Section,
            "Weld it beside a Press Core, or beside or on another Press Block: blocks on the Core's layer make the bed (2 m a block, the widest blank it takes), blocks stacked on the bed's two long edges make the walls (the tallest). The bed is the largest full rectangle of blocks next to the Core; a block outside it idles.",
            group: "Production", color: Blue);
        const double e = 0.997; // (the cell less 3 mm, so two blocks' faces never meet on one plane)
        const double deck = 1.72; // (the deck slab's underside: the slab is 28 cm, the body under it set back)
        const double leg = 0.26; // (the legs' side, and how far the body is set back from an open side)
        const double inset = e - leg; // (the body's open face, from the middle)
        // Its collision: the whole cell, never drawn (the bed and the walls keep their size and swing).
        p.Add(Box([-e, 0.003, -e], [e, 1.997, e], hidden: true));
        // ---- A bed block: drawn while no block of its press is under it ("seam": "-y") ----
        PyDict BedBox(PyDict? box, string? side = null) => box!.Update(("seam", side == null ? "-y" : Py.L(side, "-y")));
        p.Add(BedBox(MachineLook.D([-e, deck, -e], [e, 1.957, e], Blue, paintable: true))); // (the deck slab)
        p.Add(BedBox(MachineLook.D([-e + 0.004, 1.957, -e + 0.004], [e - 0.004, 1.997, e - 0.004], Bed, "tread plate"))); // (the bed's top: the dark working tone)
        p.Add(BedBox(MachineLook.D([-inset, 0.003, -inset], [inset, deck - 0.004, inset], MachineLook.Base))); // (the body, set back between the legs: dark, so the painted frame stands out)
        foreach (var x in new[] { -e, e - leg })
            foreach (var z in new[] { -e, e - leg })
                p.Add(BedBox(MachineLook.D([x, 0.003, z], [x + leg, deck - 0.004, z + leg], Blue, paintable: true))); // (a leg, in the press's paint as the deck is)
        foreach (var (side, axis, sign) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            var u = axis == 0 ? 2 : 0;
            // Lo and hi of a box on this side: out0..out1 outward from the middle, along a0..a1 the side, y0..y1 up.
            PyDict? On(double out0, double out1, double a0, double a1, double y0, double y1, string colour, string? finish = null, bool paint = false)
            {
                double[] lo = new double[3], hi = new double[3];
                (lo[axis], hi[axis]) = sign > 0 ? (out0, out1) : (-out1, -out0);
                (lo[u], hi[u], lo[1], hi[1]) = (a0, a1, y0, y1);
                return MachineLook.D(lo, hi, colour, finish, paint);
            }
            // Welded to a block of its press: the body runs on to the neighbour's under the deck, legs to legs (on a wall block it is
            // inside the lid, unseen).
            p.Add(On(inset - 0.01, e - 0.004, -inset + 0.004, inset - 0.004, 0.007, deck - 0.008, MachineLook.Base)!.Update(("seamFill", side)));
            // Open: the deck's hazard edge, a row of bolts along the slab, two ribs and a kick band on the set-back body, and a dark
            // band just under the deck (its shadow line, which reads as the frame's depth).
            p.Add(BedBox(On(e - 0.004, e + 0.008, -e + 0.03, e - 0.03, 1.80, 1.90, MachineLook.Accent, "hazard paint"), side));
            foreach (var c in new[] { -0.75, -0.25, 0.25, 0.75 })
                p.Add(BedBox(On(e - 0.004, e + 0.022, c - 0.0175, c + 0.0175, 1.745, 1.78, MachineLook.Steel, "steel"), side));
            foreach (var c in new[] { -0.3, 0.3 })
                p.Add(BedBox(On(inset - 0.004, inset + 0.04, c - 0.03, c + 0.03, 0.16, deck - 0.06, MachineLook.Steel, "steel"), side));
            p.Add(BedBox(On(inset - 0.004, inset + 0.016, -inset + 0.004, inset - 0.004, 0.007, 0.14, MachineLook.Steel, "steel"), side));
            p.Add(BedBox(On(inset - 0.004, inset + 0.012, -inset + 0.004, inset - 0.004, deck - 0.05, deck - 0.012, Hose), side));
            // ---- A wall block (a block of its press under it: "seamFill": "-y"): the lid's hazard band round its foot ----
            var off = axis == 0 ? 0 : 0.004; // (the x sides' bands and the z sides' cross at the corners: their tops and feet a few mm apart)
            p.Add(On(e - 0.008, e + 0.004, -e + 0.004, e - 0.004, 0.08 + off, 0.22 - off, MachineLook.Accent, "hazard paint")!.Update(("seamFill", "-y")));
        }
        // The lid: one solid steel body (unpainted: its own tone, never the bed's), a dark cap, two stiffeners across its top.
        p.Add(MachineLook.D([-e, 0.006, -e], [e, 1.88, e], Lid, "steel")!.Update(("seamFill", "-y")));
        p.Add(MachineLook.D([-e + 0.004, 1.88, -e + 0.004], [e - 0.004, 1.93, e - 0.004], MachineLook.Base)!.Update(("seamFill", "-y")));
        foreach (var x in new[] { -0.5, 0.5 })
            p.Add(MachineLook.D([x - 0.06, 1.93, -e + 0.03], [x + 0.06, 1.994, e - 0.03], Lid, "steel")!.Update(("seamFill", "-y")));
        p.Sock("+x", [1, 1, 0], "+x");
        p.Sock("-x", [-1, 1, 0], "-x");
        p.Sock("+z", [0, 1, 1], "+z");
        p.Sock("-z", [0, 1, -1], "-z");
        p.Sock("top", [0, 2, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(BlockMass, BlockHealth, 8, 20);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false));
        return p;
    }

    private const string Lid = "#7d8690"; // a Press Block's shell lid: bare gunmetal, lighter than any bed paint

    private static PyDict Seam(PyDict box, string face) => box.Update(("seam", face));

    // ---- The Die Lathe (Press part 2, design page drawing 17): one fixed machine that cuts any die from 0.25 m of steel bar --------------
    // A bench lathe on a cabinet: the headstock on the left with its chuck, the bed's two ways, a carriage with its tool post that feeds
    // along the bar while it cuts, the tailstock on the right, the lead screw under the ways. The bar goes in at the back through the
    // headstock (bay 1, the shared port look); the cut die lands in the tray at the front, under the machine's screen.
    private static Part Lathe()
    {
        var c = Small;
        var k = c.Cell;
        int nx = 7, ny = 6, nz = 5;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        var p = new Part("Die Lathe", c, Section, "Cuts press dies from a steel bar, any die from 0.25 m of bar. Choose the die with the Recipe button on its screen (or the configure tool), and set the bar in its bay at the back. Needs power.",
            role: "storage", group: "Production", color: LatheBlue);
        var cab = y0 + 0.75; // the cabinet's top
        const double splash = 0.06; // the back splash's thickness
        var zb = z0 + splash; // the bed's back
        var hsX = x0 + 0.45; // the headstock's face
        var zc = R3((zb + z1 - 0.2) / 2); // the spindle's line across the bed
        var ax = R3(cab + 0.33); // its height
        // The body: the cabinet (paintable), the back splash up to the bounds' top (the bay's wall), the headstock.
        p.Add(Box([x0, y0, z0], [x1, cab, z1]));
        p.Add(Box([x0 + 0.06, cab, z0], [x1 - 0.06, y1, zb]));
        p.Add(Box([x0, cab, zb], [hsX, cab + 0.6, z1 - 0.2]));
        MachineLook.Skirt(p, x0, x1, z0, z1, y0, y0 + 0.08, 0.004);
        // The bed: a chip pan between two steel ways, the lead screw along the front edge (it turns while the lathe cuts).
        p.Add(MachineLook.D([hsX + 0.004, cab + 0.003, zc - 0.26], [x1 - 0.05, cab + 0.018, zc + 0.26], MachineLook.Base));
        foreach (var wz in new[] { zc - 0.17, zc + 0.11 })
            p.Add(MachineLook.D([hsX + 0.004, cab + 0.018, wz], [x1 - 0.05, cab + 0.075, wz + 0.06], MachineLook.Steel, "steel"));
        p.Add(MachineLook.Work(MachineLook.D([hsX + 0.06, cab + 0.022, zc + 0.2], [x1 - 0.11, cab + 0.062, zc + 0.24], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+x"))), "spin", "+x"));
        foreach (var bx in new[] { hsX + 0.008, x1 - 0.108 }) // (the screw's two bearing blocks, in front of the ways)
            p.Add(MachineLook.D([bx, cab + 0.018, zc + 0.19], [bx + 0.05, cab + 0.08, zc + 0.25], MachineLook.Base));
        // The chuck on the headstock's face, its three jaws, and the spindle nose behind it: they turn while it cuts.
        p.Add(MachineLook.D([hsX - 0.002, ax - 0.09, zc - 0.09], [hsX + 0.03, ax + 0.09, zc + 0.09], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))));
        p.Add(MachineLook.Work(MachineLook.D([hsX + 0.03, ax - 0.13, zc - 0.13], [hsX + 0.12, ax + 0.13, zc + 0.13], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x"))), "spin", "+x"));
        // (the jaws are two bars across the chuck's face, each centred on the spindle: a spin box turns about its own middle)
        p.Add(MachineLook.Work(MachineLook.D([hsX + 0.12, ax - 0.11, zc - 0.022], [hsX + 0.16, ax + 0.11, zc + 0.022], MachineLook.Accent), "spin", "+x"));
        p.Add(MachineLook.Work(MachineLook.D([hsX + 0.124, ax - 0.022, zc - 0.07], [hsX + 0.156, ax + 0.022, zc + 0.07], MachineLook.Accent), "spin", "+x"));
        // The carriage: a saddle across the ways, the cross slide, the tool post and its cutter; they feed along the bar while it cuts.
        var cx = x0 + 0.95;
        const double feed = 0.22;
        p.Add(MachineLook.Work(MachineLook.D([cx - 0.15, cab + 0.075, zc - 0.24], [cx + 0.15, cab + 0.135, zc + 0.24], MachineLook.Base), "stroke", "+x", feed));
        p.Add(MachineLook.Work(MachineLook.D([cx - 0.1, cab + 0.135, zc - 0.05], [cx + 0.1, cab + 0.18, zc + 0.2], LatheBlue, paintable: true), "stroke", "+x", feed));
        p.Add(MachineLook.Work(MachineLook.D([cx - 0.05, cab + 0.18, zc + 0.07], [cx + 0.05, cab + 0.28, zc + 0.17], MachineLook.Steel, "steel"), "stroke", "+x", feed));
        p.Add(MachineLook.Work(MachineLook.D([cx - 0.015, ax - 0.02, zc + 0.02], [cx + 0.015, ax + 0.004, zc + 0.1], MachineLook.Accent), "stroke", "+x", feed)); // (the cutter, at the spindle's height)
        p.Add(MachineLook.Work(MachineLook.D([cx - 0.04, cab + 0.06, zc + 0.24], [cx + 0.04, cab + 0.12, zc + 0.27], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+z"))), "stroke", "+x", feed)); // (the apron's handwheel)
        // The tailstock on the right of the ways, its quill and centre pointing at the chuck.
        var tx = x1 - 0.32;
        p.Add(MachineLook.D([tx, cab + 0.075, zc - 0.15], [tx + 0.24, ax + 0.08, zc + 0.15], LatheBlue, paintable: true));
        p.Add(MachineLook.D([tx - 0.12, ax - 0.04, zc - 0.04], [tx, ax + 0.04, zc + 0.04], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "-x"))));
        p.Add(MachineLook.D([tx + 0.24, ax - 0.05, zc - 0.05], [tx + 0.27, ax + 0.05, zc + 0.05], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+x")))); // (its handwheel)
        // The headstock's dressing: a speed gauge and bezel, the maker's plate, louvres down its outer side, a cap band.
        var hf = z1 - 0.2;
        p.Add(MachineLook.D([x0 + 0.13, ax - 0.065, hf], [x0 + 0.29, ax + 0.095, hf + 0.012], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z"))));
        p.Add(MachineLook.D([x0 + 0.145, ax - 0.05, hf + 0.012], [x0 + 0.275, ax + 0.08, hf + 0.02], MachineLook.In, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+z"))));
        p.Add(MachineLook.D([x0 + 0.2, ax + 0.012, hf + 0.02], [x0 + 0.215, ax + 0.07, hf + 0.033], MachineLook.Stop)); // (its needle)
        p.Add(MachineLook.D([x0 + 0.09, cab + 0.09, hf], [x0 + 0.33, cab + 0.18, hf + 0.008], Gold, "brass"));
        for (var i = 0; i < 4; i++)
            p.Add(MachineLook.D([x0 - 0.012, cab + 0.12 + i * 0.09, zb + 0.12], [x0, cab + 0.16 + i * 0.09, hf - 0.12], MachineLook.Base));
        p.Add(MachineLook.D([x0 - 0.004, cab + 0.56, zb], [hsX + 0.004, cab + 0.604, hf + 0.004], MachineLook.Base));
        // The coolant line: down the splash's front from a pump box to a nozzle over the cutter's path.
        p.Add(MachineLook.D([x1 - 0.42, cab + 0.42, zb], [x1 - 0.26, cab + 0.56, zb + 0.1], MachineLook.Base));
        p.Add(MachineLook.D([x1 - 0.36, cab + 0.5, zb + 0.1], [x1 - 0.33, cab + 0.53, zc - 0.02], Hose));
        p.Add(MachineLook.D([cx - 0.02, cab + 0.47, zc - 0.05], [x1 - 0.33, cab + 0.5, zc - 0.02], Hose));
        p.Add(MachineLook.D([cx - 0.02, ax + 0.09, zc - 0.05], [cx + 0.01, cab + 0.47, zc - 0.02], MachineLook.Steel, "steel")); // (the nozzle)
        // The switch box on the right side: a stop button and the conduit up to the splash.
        p.Add(MachineLook.D([x1, cab - 0.35, z1 - 0.4], [x1 + 0.06, cab - 0.12, z1 - 0.15], MachineLook.Base));
        p.Add(MachineLook.D([x1 + 0.06, cab - 0.27, z1 - 0.31], [x1 + 0.085, cab - 0.2, z1 - 0.24], MachineLook.Stop, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+x"))));
        p.Add(MachineLook.D([x1 + 0.012, cab - 0.12, z1 - 0.3], [x1 + 0.04, cab + 0.004, z1 - 0.27], Hose));
        // Corner bolts on the cabinet's front, and the bed's oil cups.
        foreach (var bx in new[] { x0 + 0.05, x1 - 0.08 })
            foreach (var by in new[] { y0 + 0.13, cab - 0.18 })
                p.Add(MachineLook.D([bx, by, z1], [bx + 0.03, by + 0.03, z1 + 0.014], MachineLook.Steel, "steel"));
        foreach (var oz in new[] { zc - 0.14, zc + 0.14 })
            p.Add(MachineLook.D([x1 - 0.1, cab + 0.075, oz - 0.015], [x1 - 0.07, cab + 0.105, oz + 0.015], Gold, "brass"));
        // The tray at the front, where the cut die lands: a steel pan with a lip.
        double tx0 = hsX + 0.05, tx1 = x1 - 0.05, tz0 = zc + 0.29, tz1 = z1 - 0.03;
        p.Add(MachineLook.D([tx0, cab + 0.003, tz0], [tx1, cab + 0.015, tz1], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([tx0, cab + 0.015, tz1 - 0.015], [tx1, cab + 0.06, tz1], MachineLook.Steel, "steel"));
        // In and out: the bay's port at the back (the bar goes in through the headstock), a white band on the splash, the orange band on
        // the cabinet's front over the screen.
        var bay = new[] { R3(x0 + 0.225), ax, z0 };
        p.Sock("bay1", bay, "-z", kind: "die");
        MachineLook.Port(p, [bay[0], R3(ax - 0.11), z0], 2, -1, 0.13, 0.1, true);
        p.Add(MachineLook.D([x0 + 0.5, cab + 0.03, z0 - 0.012], [x1 - 0.1, cab + 0.09, z0], MachineLook.In));
        p.Add(MachineLook.D([x0 + 0.06, cab - 0.12, z1], [x1 - 0.06, cab - 0.02, z1 + 0.012], MachineLook.Out));
        MachineLook.Arrow(p, 2, z1 + 0.012, 1, 1, -1, cab - 0.07, (x0 + x1) / 2, 0.08, 0.12, MachineLook.Ink, 0.012);
        BlockSockets(p, nx, ny, nz, faces: "xy", limit: Lim(3, "+x", "-x", "+y", "-y"));
        p.Stats(LatheMass * 0.6, LatheHealth * 0.7, c.Armour, 4);
        var proud = 0.004;
        var card = Kw(("source", "press.die"), ("at", Py.L(R3((x0 + x1) / 2), R3(y0 + 0.4), Py.Round(z1 + proud, 4))), ("size", Py.L(R3(k * 5.8), R3(k * 1.2))), ("face", "+z"), ("up", "+y"), ("rows", 4));
        card["touch"] = Py.L(Kw(("action", "machine.recipe"), ("at", Py.L(0.72, 0.02, 0.98, 0.24)), ("label", "Recipe")));
        var bayCard = Kw(("source", "press.bay"), ("slot", 0), ("at", Py.L(R3(x0 + 1.05), R3(cab + 0.42), Py.Round(z0 - proud, 4))), ("size", Py.L(0.9, 0.4)), ("face", "-z"), ("up", "+y"), ("rows", 5));
        p.Set(("power", LathePower), ("config", Py.L(Kw(("key", "recipe"), ("label", "Cuts"), ("group", "Work"), ("tooltip", "The die it cuts from the bar in its bay. It cuts any die from 0.25 m of steel bar.")))),
            ("storage", Kw(
                ("slots", Py.L(Kw(("min", Py.L(R3(tx0 + 0.01), R3(cab + 0.02), R3(tz0))), ("max", Py.L(R3(tx1 - 0.01), R3(cab + 0.58), R3(tz1 - 0.015))), ("stack", 12), ("label", "Tray")))), // (one slot the tray's width and 0.56 m up: a 0.5 m die cassette stands in it on edge)
                ("press", Kw(("lathe", true),
                    ("bays", Py.L(Kw(("socket", "bay1"), ("min", Py.L(R3(bay[0] - 0.24), R3(ax - 0.24), R3(z0 - 0.375))), ("max", Py.L(R3(bay[0] + 0.24), R3(ax + 0.24), R3(z0 + 0.375)))))),
                    ("maxAcross", MaxAcross(Small)), ("speed", LatheSpeed), ("outAt", Py.L(R3((x0 + x1) / 2), R3(y0), R3(z1 + k * 0.2))), ("outDir", "+z"))))),
            ("panels", Py.L(bayCard, card)));
        var lamp = MachineLook.D([R3((x0 + x1) / 2 - 0.62), R3(y0 + 0.57), R3(z1 + 0.003)], [R3((x0 + x1) / 2 + 0.62), R3(y0 + 0.6), R3(z1 + 0.012)], "#2b3036");
        if (lamp is not null)
            lamp["status"] = true;
        p.Add(lamp); // (its line state over its own screen: ProductionLines)
        return p;
    }

    /// <summary>
    /// A die: a 0.5 m cassette (Press part 2), the same for every family and any press. A lower shoe on the mount, four guide posts, the
    /// upper shoe with the family's coloured punch on top, two grab handles and the family's label plate on its front.
    /// </summary>
    private static Part Die(string family, string colour)
    {
        var c = Small;
        const double s = 0.25, lo = 0.07, hi0 = 0.15, hi1 = 0.2;
        var what = Array.IndexOf(SizedFamilies, family) >= 0
            ? $"A press die cassette. Fit it to a press of any size and it presses a whole piece into one {family.ToLowerInvariant()} part, sized by the piece. A standard piece gives the standard part."
            : $"A press die cassette. Fit it to a press of any size and it stamps {family.ToLowerInvariant()} parts. Every material a {family.ToLowerInvariant()} part comes in uses this die.";
        var p = new Part($"{family} Die", c, Dies, what, role: "panel", tool: "wrench", group: "Production", color: Bed);
        p.Add(Box([-s, 0, -s], [s, lo, s]));
        p.Add(Box([-s + 0.02, hi0, -s + 0.02], [s - 0.02, hi1, s - 0.02]));
        p.Add(Box([-s * 0.55, hi1, -s * 0.55], [s * 0.55, hi1 + 0.05, s * 0.55], colour));
        foreach (var gx in new[] { -s + 0.03, s - 0.07 })
            foreach (var gz in new[] { -s + 0.03, s - 0.07 })
                p.Add(MachineLook.D([gx, lo - 0.004, gz], [gx + 0.04, hi0 + 0.004, gz + 0.04], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+y"))));
        foreach (var sg in new[] { -1, 1 })
        {
            var fx = sg < 0 ? -s : s;
            p.Add(MachineLook.D([sg < 0 ? fx - 0.05 : fx, lo * 0.5 - 0.015, -0.12], [sg < 0 ? fx : fx + 0.05, lo * 0.5 + 0.015, -0.09], MachineLook.Base));
            p.Add(MachineLook.D([sg < 0 ? fx - 0.05 : fx, lo * 0.5 - 0.015, 0.09], [sg < 0 ? fx : fx + 0.05, lo * 0.5 + 0.015, 0.12], MachineLook.Base));
            p.Add(MachineLook.D([sg < 0 ? fx - 0.065 : fx + 0.035, lo * 0.5 - 0.02, -0.12], [sg < 0 ? fx - 0.035 : fx + 0.065, lo * 0.5 + 0.02, 0.12], MachineLook.Accent));
        }
        p.Add(MachineLook.D([-0.15, 0.015, s], [0.15, lo - 0.015, s + 0.008], colour));
        p.Add(MachineLook.D([-0.17, hi0 + 0.012, s - 0.02], [0.17, hi1 - 0.012, s - 0.012], MachineLook.Accent, "hazard paint"));
        p.Sock("mount", [0, 0, 0], "-y", kind: "pressdie");
        p.Stats(12, 300, c.Armour, 2);
        p.Set(("pressDie", family), ("centreSockets", false));
        return p;
    }

    /// <summary>
    /// A rack of shelves on posts: the Die Rack, and the Assembler's Block Rack, the same piece at 2 m bays (Sean 2026-11-21: "the die
    /// rack and the block rack the same piece"). <paramref name="bays"/> shelves side by side along x, each a slot of its own (a shelf holds
    /// one kind), split by a post. Either rack holds dies and tool blocks alike, whatever fits a bay; a chute from it into a press brings
    /// the press a die, and one into an Assembler brings the tool block its recipe needs.
    /// </summary>
    internal static Part Rack(Cls c, int nx, int ny, int nz, string? name = null, string? desc = null, int bays = 1, string label = "Dies", string folder = Section)
    {
        var k = c.Cell;
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        var p = new Part(name ?? NameOf(c, "Die Rack"), c, folder, desc ?? "Holds press dies on a shelf. A chute from it into a press brings a die to an empty slot.", role: "storage", group: "Production", color: Trim);
        var board = k * 0.12;
        var post = k * 0.14;
        p.Add(Box([x0, y0, z0], [x1, y0 + board, z1], Bed));
        foreach (var x in new[] { x0, x1 - post })
            foreach (var z in new[] { z0, z1 - post })
                p.Add(Box([x, y0 + board, z], [x + post, y1, z + post]));
        var bay = (x1 - x0 - 2 * post - (bays - 1) * post) / bays; // (each bay's inner width, between its posts)
        for (var b = 1; b < bays; b++)
        {
            var x = x0 + post + b * bay + (b - 1) * post;
            foreach (var z in new[] { z0, z1 - post })
                p.Add(Box([x, y0 + board, z], [x + post, y1, z + post]));
        }
        BlockSockets(p, nx, ny, nz, faces: "xy", limit: Lim(2, "+x", "-x", "+y", "-y"));
        p.Stats(60 * c.X, 200 * c.X, c.Armour, c == Small ? 3 : null);
        var gap = 0.015 * c.X;
        var slots = Py.L();
        for (var b = 0; b < bays; b++)
        {
            var bx0 = x0 + post + b * (bay + post);
            slots.Add(Kw(("min", Py.L(R3(bx0 + gap), R3(y0 + board + gap), R3(z0 + gap))), ("max", Py.L(R3(bx0 + bay - gap), R3(y1 - gap), R3(z1 - gap))), ("rack", true), ("label", label)));
        }
        p.Set(("storage", Kw(("slots", slots))));
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

    private static PyDict Pattern(string id, string like, string label, string preset, string[]? blanks = null, List<object?>? features = null, PyDict? bay2 = null, List<object?>? scale = null, string? along = null, bool nearest = false, bool auto = true)
    {
        var p = Kw(("id", id), ("like", like), ("label", label), ("preset", preset));
        if (!auto) // (never Auto's: the Pattern setting or an order reaches it, sized parts phase 6)
            p["auto"] = false;
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
        bracketPatterns.AddRange(More("bracket")); // (Assembler parts to the press, PressParts.ToPress.cs: at the end of the list, as every die's new patterns)
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
        var housingPatterns = Py.L(Pattern("housing", "Housing 0.5 m", "housing", "hollow 45x45x50 metal")).And(More("housing"));
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
            Pattern("grate", "Mesh Grate 1 m", "ribbed plate", "plate 100x10x100 steel", features: ribs)).And(More("plate"));
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

    private const int EngineAcrossMax = 2880, EngineLengthMax = 3200; // (the Engine die from 20 cm, about 0.7 of the Small files' across, design 6.2; a 10 cm injector rail squashed its 2 mm gaps to half a mm)

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
    private static string? EngineBlank(string part, string section, string along, Cls c)
    {
        var d = CallOfSlop.ConstructParts.Find(part) ?? throw new InvalidOperationException($"press_parts: no engine piece {part}");
        var size = d.BoundsMax - d.BoundsMin;
        var ax = along == "y" ? 1 : along == "x" ? 0 : 2;
        static int Lattice(double m) => Math.Max(10, (int)Math.Round(m * 100 / 5, MidpointRounding.AwayFromZero) * 5);
        var len = Lattice(ax == 0 ? size.X : ax == 1 ? size.Y : size.Z);
        var across = PresetAcross(Lattice(ax == 0 ? Math.Max(size.Y, size.Z) : ax == 1 ? Math.Max(size.X, size.Z) : Math.Max(size.X, size.Y)), c); // (held to its class's press, as every die's presets: a Small crank from a Small press)
        if (across > EngineAcrossMax || len > EngineLengthMax)
            return null;
        var wall = Math.Clamp(Lattice(across / 900.0), 5, 200); // (a ninth of across, to the lattice)
        string Text(int l) => section == "pipe" ? $"pipe{wall} {across}x{across}x{l} steel" : $"{section} {across}x{across}x{l} steel";
        // The preset comes out as the file part at its own mass: its blank alone (bay 2 not counted) weighs at least that, or it would recycle
        // for more than it cost (review, 2026-11-13: the Small Flywheel and Turbo). Longer by 5 cm steps until it does.
        while (len + 5 <= EngineLengthMax && CallOfSlop.Pressed.ParseBlank(Text(len)) is { } k && CallOfSlop.Pressed.VolumeMass(k).Mass < d.Mass)
            len += 5;
        return Text(len);
    }

    /// <summary>The four plain cylinders' presets (those the Cylinder die can press).</summary>
    private static IEnumerable<(string Name, string Blank)> CylinderPresets() =>
        EngineSizes.Select((s, i) => ($"{s} Cylinder", EngineBlank($"{s} Cylinder", "pipe", "y", Classes[i]))).Where(p => p.Item2 != null).Select(p => (p.Item1, p.Item2!));

    /// <summary>An engine pattern's presets.</summary>
    private static IEnumerable<(string Name, string Blank)> EnginePresets(string piece, string section, string along) =>
        EngineSizes.Select((s, i) => ($"{s} {piece}", EngineBlank($"{s} {piece}", section, along, Classes[i]))).Where(p => p.Item2 != null).Select(p => (p.Item1, p.Item2!));

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
            Pattern("switch", "Switch", "switch", "plate 10x10x15 laminate", bay2: copper)).And(More("button", copper)));
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
        // Sized parts phase 5: a like body (the nearest preset's cylinder stretched; its bore across, its stroke along y), Piston Sets by
        // volume; no die features (a like body is the file's own model). Its band stays the one presses used before phase 5 (20 cm up, 1 to 16 times as long
        // as across), so every key made before still loads (review, 2026-11-13); the stroke ratio is held to design 6.3's 0.6 to 1.6 (Pressed.EngineNumbers).
        var cylPresets = CylinderPresets().ToList();
        yield return SizedDie("Cylinder", ["pipe"], ["steel"], [20, EngineAcrossMax], [25, EngineLengthMax], [1, 16], 6, 0.01, Py.L(), Py.L(
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
            DrivePatterns.Concat(MoreDrivePatterns).Select(d => (object?)Pattern(d.Id, d.Like, d.Label, d.Blank, bay2: gears, scale: torque)).ToList(), FaceSockets());
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

    /// <summary>
    /// Sized parts phase 6: the generated receivers with no recipe (ConstructWeaponParts: a builder receiver at twice its handheld size;
    /// the Firebreather, Whaler, Field Gun and Howitzer have hand Assembler recipes at their tiers, so they stay the Assembler's) as patterns
    /// after the hand-made three. Each preset is its part's own bounds on the 5 cm lattice (across the wider of x and y, length along z),
    /// held to the die's ranges, solid. They are never Auto's ("auto": false): Auto makes of a blank what it made before, and the Pattern
    /// setting or an order reaches these. Their order is the loaded table's, new ones appended by name, since a Press saves its Pattern
    /// setting as 1 + the pattern's place (review, 2026-11-13).
    /// </summary>
    private static List<(string Id, string Like, string Label, string Blank, bool Auto)> AllReceiverPatterns()
    {
        var all = ReceiverPatterns.Select(r => (r.Id, r.Like, r.Label, r.Blank, true)).ToList();
        var ids = all.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        static int Lattice(double m) => Math.Max(5, (int)Math.Round(m * 100 / 5, MidpointRounding.AwayFromZero) * 5);
        var hand = ReceiverPatterns.Select(r => CallOfSlop.ConstructParts.Find(r.Like)?.GunReceiver ?? "").ToHashSet(StringComparer.Ordinal);
        var made = Assembling.HandOutputs();
        var was = CallOfSlop.PressTable.Current.Die("receiver")?.Patterns.Select(p => p.Like.Name).ToList() ?? [];
        int Place(string name) => was.IndexOf(name) is var i and >= 0 ? i : int.MaxValue;
        foreach (var d in CallOfSlop.ConstructParts.All.Where(d => d.Generated && d.IsReceiverGun && !hand.Contains(d.GunReceiver) && !made.Contains(d.Name))
                     .OrderBy(d => Place(d.Name)).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            var size = d.BoundsMax - d.BoundsMin;
            var across = Math.Clamp(Lattice(Math.Max(size.X, size.Y)), 15, 120);
            var len = Math.Clamp(Lattice(size.Z), Math.Max(30, across), 600);
            var blank = $"solid {across}x{across}x{len} steel";
            var words = d.Name.ToLowerInvariant().Split(' ').Where(w => w != "receiver");
            var id = string.Join("", words.Select(w => new string(w.Where(ch => ch is >= 'a' and <= 'z' or '-').ToArray())));
            id = id.Length > 16 ? id[..16] : id;
            for (var n = 2; !ids.Add(id); n++)
                id = (id.Length > 14 ? id[..14] : id) + n;
            var label = d.Name.ToLowerInvariant(); // (1 to 24 characters: "gravedigger auto receiver" loses its last word)
            if (label.Length > 24)
                label = label.Replace(" receiver", "");
            all.Add((id, d.Name, label.Length > 24 ? label[..24].TrimEnd() : label, blank, false));
        }
        return all;
    }

    private static PyDict ReceiverDie()
    {
        var gears = Kit("Gear Set", true, 1, 0.02, "mass", 20); // (as the Drive die: the action's gearing)
        var die = SizedDie("Receiver", ["solid", "hollow"], ["steel"], [15, 120], [30, 600], [1, 1], 4, 0.02, Py.L(),
            AllReceiverPatterns().Select(r => (object?)Pattern(r.Id, r.Like, r.Label, r.Blank, bay2: gears, along: "z", auto: r.Auto)).ToList());
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
            Pattern("pivot", "Free Pivot 1x1", "free pivot", "solid 30x25x30 steel", blanks: ["solid"], bay2: Kit("Bearing", true, 1, 2, "across", 16), along: "y")).And(More("actuator", Kit("Bearing", true, 1, 2, "across", 16), along: "y")));
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
            Pattern("light", "Light Suspension", "light suspension", "solid 25x25x30 steel", bay2: springBearings, scale: load, along: "y")).And(More("spring", springBearings, load, along: "")));
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
            Pattern("fuel", "Fuel Line", "fuel line", "pipe5 15x15x50 steel", blanks: ["pipe"], bay2: rubberByLength, along: "x")).And(More("cable")));
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
            [.. gates.Select(g => (object?)Pattern(g.Id, g.Like, g.Label, "solid 20x20x25 laminate", bay2: boards, along: "y")), .. More("control", boards, along: "y")]);
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
        yield return SizedDie("Beam", ["solid", "hollow", "i-beam", "c-channel", "t-bar", "round", "pipe", "l-angle"], ["steel", "metal", "bronze", "wood", "concrete"], [10, 2880], [50, 12800], [2, 64], 2, 0.002, beam, Py.L(
            Pattern("girder", "Frame Beam", "girder", "i-beam 50x50x200 steel"),
            Pattern("column", "Frame Beam", "column", "hollow 50x50x200 steel", features: Py.L(
                Feature("base", [-0.2, -0.2, -0.03, 1.2, 1.2, 0], "dark", proud: "z", clamp: "z", min: 1, max: 30),
                Feature("base", [-0.2, -0.2, 1, 1.2, 1.2, 1.03], "dark", proud: "z", clamp: "z", min: 1, max: 30))),
            Pattern("strut", "Frame Beam", "strut", "solid 20x20x100 steel", features: Py.L(
                Feature("band", [-0.06, -0.06, 0.47, 1.06, 1.06, 0.53], "dark", proud: "xy")))).And(More("beam")));
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
        // Sized parts phase 5: the Engine die's presets (phase 6: their Assembler recipes go, Assembling.PressedNow).
        foreach (var p in EnginePatterns)
            foreach (var (name, blank) in EnginePresets(p.Piece, p.Section, p.Along))
                Add(name, "engine", p.Id, blank);
        foreach (var d in DrivePatterns.Concat(MoreDrivePatterns))
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
        // Sized gun parts phase 1: the plain barrels are the Barrel die's knots (phase 6: their Assembler recipes go).
        foreach (var (part, _, blank) in PlainBarrels)
            Add(part, "barrel", "plain", blank);
        // Phase 2: the other barrels (their items: the builder part's name, ConstructWeaponParts) and the two artillery tubes.
        foreach (var (id, builder, blank) in PatternBarrels())
            Add(builder, "barrel", id, blank);
        foreach (var (builder, blank) in ArtilleryTubes)
            Add(builder, "barrel", "tube", blank);
        // Phase 3: the hand-made receivers are the Receiver die's presets; phase 6 the generated ones (their first recipe). A receiver is an
        // assembly by the sort's rule (role gun), so the hand-made three keep their Assembler recipes beside their presets.
        foreach (var (id, like, _, blank, _) in AllReceiverPatterns())
            Add(like, "receiver", id, blank);
        // Assembler parts to the press (Sean, 2026-11-22; PressParts.ToPress.cs): after every preset before them.
        foreach (var (part, die, pattern, blank) in MoreToPress())
            Add(part, die, pattern, blank);
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

    /// <summary>The catalogue parts the named dies press from a preset (sized parts phase 6: the Barrel and Engine dies' leave the Assembler).</summary>
    internal static HashSet<string> PresetParts(params string[] dies) =>
        Presets().Cast<PyDict>().Where(p => dies.Contains((string)p["die"]!)).Select(p => (string)p["part"]!).ToHashSet(StringComparer.Ordinal);

    private static List<object?> LatheRows()
    {
        var r = new List<object?>();
        foreach (var (family, tier, _) in Families)
            r.Add(Kw(("output", $"{family} Die"), ("seconds", 15 * tier),
                ("bays", Py.L(BayRow("Bar", ["solid"], ["steel"], 0.25, MaxAcross(Small), use: 0.25)))));
        return r;
    }

    /// <summary>The Press Line: an Extruder cutting a plate strip into a press by chute, with its power; dies and the second bay's rod are the player's.</summary>
    private static PyDict Line() => Kw(
        ("format", "call-of-slop-construct-blueprint/1"),
        ("name", "Press Line"),
        ("description", "An Extruder chuted into a Press, with a bin and a battery: fit a die and tip in material."),
        ("steps", Py.L(
            Kw(("part", "Foundation")),
            Kw(("part", "Extruder"), ("on", 0), ("socket", "top11"), ("by", "bottom"), ("config", Kw(("profile", 1), ("width", 0.3), ("height", 0.3), ("cutLength", 2)))),
            Kw(("part", "Foundation"), ("on", 0), ("socket", "z-1"), ("by", "z+1")), // (behind the Extruder: its feed and power, clear of its 2 x 1.5 m)
            Kw(("part", "Materials Bin"), ("on", 2), ("socket", "top01"), ("by", "bottom")),
            Kw(("part", "Battery"), ("on", 2), ("socket", "top02"), ("by", "-y")),
            Kw(("part", "Foundation"), ("on", 0), ("socket", "x+1"), ("by", "x-1")),
            Kw(("part", "Press"), ("on", 5), ("socket", "top00"), ("by", "-y.0.0")))),
        ("links", Py.L(Kw(("type", "chute"), ("from", 3), ("to", 1)), Kw(("type", "chute"), ("from", 1), ("to", 6)))));

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)>();
        parts.Add(("", PressCore()));
        parts.Add(("", PressBlock()));
        parts.Add(("", Lathe()));
        foreach (var (family, _, colour) in Families)
            parts.Add(("Dies", Die(family, colour)));
        parts.Add(("", Rack(Small, 6, 6, 3)));
        foreach (var (side, c, n) in Housings)
            foreach (var t in MaterialTiers)
                parts.Add(("Housings", Housing(side, c, n, t)));
        WriteParts(o, Out, parts);
        var presets = Presets();
        var dies = SizedDies();
        var table = Kw(("format", Format), ("about", About), ("lathe", LatheRows()),
            ("strength", Kw(("of", "healthPerM3"), ("ref", 5000))), ("dies", dies), ("presets", presets));
        o.File(Table, PyJson.Dumps(table, 1) + "\n");
        o.File(Blueprint, PyJson.Dumps(Line(), 2) + "\n");
    }
}
