using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Extrusion parts (Constructs/Parts/Extrusion) and the Extruder Line blueprint (docs/sandbox/CONSTRUCTS.md, "Extrusion" and "The Wheel
/// Former"). Was tools/constructs/extrusion_parts.py.
///
/// An Extruder pushes bulk material through a die; a Grow Former builds a wheel, a prop or a wing up on its hub. Both are on the block system
/// (MachineBlocks.cs): the Extruder a Core with Die Blocks beside it for a bigger die and Run-out Tables; the Grow Former a Core with Former
/// Blocks on the floor for its bed and Mast Blocks stacked on it for its height (design page drawing 22). The sizes are part data (storage.extrude, storage.wheel). Run
/// the part kit's assembling (--gen-parts assembling) after it for the recipes.
/// </summary>
public sealed class ExtrusionParts : IPartGenerator
{
    public string Name => "extrusion_parts";

    private const string Out = "Constructs/Parts/Extrusion";
    private const string Blueprint = "Constructs/Blueprints/ExtruderLine.json";
    private const string Section = "Extrusion";
    private const string BodyC = "#55606b", Trim = "#3f4750", Dark = "#23272c", Gold = "#c8a13a";

    internal static List<object?> Profiles() => Py.L("Solid bar", "Plate", "Wall", "Hollow box", "I-beam", "C-channel", "L-angle", "T-bar", "Cross", "Custom", "Round", "Pipe", "Airfoil", "Symmetric foil", "Thin foil");
    private static List<object?> BladeStyles() => Py.L("Straight", "Scimitar", "Broad paddle", "Swept", "Narrow"); // (Air.json's "bladeStyles", in order)
    private static List<object?> FanKinds() => Py.L("Propeller", "Electric prop", "Helicopter rotor", "Ducted fan", "Tail rotor"); // (Extrusion.json's "fanKinds", in order)
    private static List<object?> Airframes() => Py.L("Wing", "Tail plane", "Fin", "Swept wing, left", "Swept wing, right", "Aileron", "Elevator", "Rudder", "Flap"); // (Extrusion.json's "airframes")

    private static string NameOf(Cls c, string b) => c.Name.Length > 0 ? $"{c.Name} {b}" : b;

    // Python's min and max of two numbers, keeping the one chosen as it was (int or float); the first when they are equal.
    private static object OMin(object a, object b) => Py.D(b) < Py.D(a) ? b : a;
    private static object OMax(object a, object b) => Py.D(b) > Py.D(a) ? b : a;

    private static readonly PyDict Ink = Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours."));

    internal static readonly PyDict OrderCount = Kw(("key", "orderCount"), ("label", "Order count"), ("group", "Orders"), ("min", 1), ("max", 999), ("step", 1), ("default", 1),
        ("tooltip", "Pieces the Queue button on its screen asks for: one order of its settings as they stand."));

    /// <summary>The touch buttons under a machine's queue (WorkOrders.cs): Queue, Cancel, Clear, Up and Down. The Assemblers' too (MachineTiers),
    /// with <paramref name="recipe"/> a Recipe button among them (the recipe picker on this machine: Program.MachineRecipePicker).</summary>
    internal static List<object?> OrderButtons(bool recipe = false) => recipe
        ? Py.L(
            Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.2, 0.12)), ("label", "Queue")),
            Kw(("action", "orders.cancel"), ("at", Py.L(0.21, 0.02, 0.39, 0.12)), ("label", "Cancel")),
            Kw(("action", "orders.clear"), ("at", Py.L(0.4, 0.02, 0.56, 0.12)), ("label", "Clear")),
            Kw(("action", "machine.recipe"), ("at", Py.L(0.57, 0.02, 0.75, 0.12)), ("label", "Recipe")),
            Kw(("action", "view.up"), ("at", Py.L(0.76, 0.02, 0.87, 0.12)), ("label", "Up")),
            Kw(("action", "view.down"), ("at", Py.L(0.88, 0.02, 0.98, 0.12)), ("label", "Down")))
        : Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.24, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.26, 0.02, 0.48, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.5, 0.02, 0.72, 0.12)), ("label", "Clear")),
        Kw(("action", "view.up"), ("at", Py.L(0.75, 0.02, 0.86, 0.12)), ("label", "Up")), // (this player's own view of a long queue)
        Kw(("action", "view.down"), ("at", Py.L(0.87, 0.02, 0.98, 0.12)), ("label", "Down")));

    private static double At(PyDict s, int k) => Py.D(((List<object?>)s["at"]!)[k]);

    /// <summary>The part's screen: a side of its block (PartScreens), never the die's.</summary>
    private static Part WithScreen(Part p, bool orders = false, string[]? faces = null, double? width = null, bool recipe = false)
    {
        var keepClear = p.Sockets.Select(s => (new[] { At(s, 0), At(s, 1), At(s, 2) }, (string)s["dir"]!)).ToList();
        // A machine that takes work orders (WorkOrders.cs) is a machine of a production line: a status lamp over its screen, its queue on
        // eight rows and three buttons under it.
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: faces ?? ["-x", "+x", "-z"], rows: orders ? 8 : 4, width: width, lamp: orders);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        if (orders)
            MachineLook.Buttons(p, plate, (string)panel["face"]!); // (the control face: screen, lamp over it, buttons under it)
        if (lamp is not null)
            p.Boxes.Add(lamp);
        if (orders)
            panel["touch"] = OrderButtons(recipe); // (an Extruder: Recipe among them, the section picker: Program.MachineRecipePicker)
        else if (recipe) // (a Fan or Airframe Former: what it makes, from the recipe picker: Program.MachineRecipePicker)
            panel["touch"] = Py.L(Kw(("action", "machine.recipe"), ("at", Py.L(0.72, 0.02, 0.98, 0.24)), ("label", "Recipe")));
        var config = p.D.Get("config") is List<object?> cfg ? new List<object?>(cfg) : [];
        if (orders)
            config.Add(OrderCount);
        config.Add(Ink);
        p.Set(("panels", Py.L(panel)), ("config", config));
        return p;
    }

    /// <summary>An Extruder's settings. (A new one goes on the end: a saved setting is found by its key.)</summary>
    private static List<object?> ExtruderConfig(double die, object section, object length, string pieces = "Die Frames")
    {
        var frames = die < Py.D(section) ? $": weld on {pieces}." : ".";
        return Py.L(
            Kw(("key", "profile"), ("label", "Profile"), ("group", "Shape"), ("options", Profiles()), ("default", 0),
                ("tooltip", "The cross-section. Custom is the cell grid below; clicking a cell starts one from what is shown. Round and Pipe take their diameter from Width.")),
            Kw(("key", "width"), ("label", "Width"), ("group", "Shape"), ("unit", "m"), ("min", 0.1), ("max", section), ("step", 0.05), ("default", 0.25),
                ("tooltip", "Across the section (a Round's or a Pipe's diameter). The die must be at least this wide" + frames)),
            Kw(("key", "height"), ("label", "Height"), ("group", "Shape"), ("unit", "m"), ("min", 0.1), ("max", section), ("step", 0.05), ("default", 0.25),
                ("tooltip", "Up the section. The die must be at least this tall" + frames)),
            Kw(("key", "maskA"), ("label", "Cells"), ("group", "Shape"), ("tooltip", "The section as an 8 by 8 grid: click cells on or off.")),
            Kw(("key", "maskB"), ("label", "Cells"), ("group", "Shape")),
            Kw(("key", "maskC"), ("label", "Cells"), ("group", "Shape")),
            Kw(("key", "maskD"), ("label", "Cells"), ("group", "Shape")),
            Kw(("key", "wall"), ("label", "Pipe wall"), ("group", "Shape"), ("unit", "m"), ("min", 0.01), ("max", 2), ("step", 0.01), ("default", 0.03),
                ("tooltip", "A Pipe's wall thickness. It leaves a bore of at least 5 cm.")),
            Kw(("key", "cutLength"), ("label", "Cut every"), ("group", "Cutting"), ("unit", "m"), ("min", 0), ("max", length), ("step", 0.05), ("default", 0),
                ("tooltip", "Cuts the piece off at the die by itself when it is this long. 0: only when a signal comes into Cut.")));
    }

    private static List<object?> FormerConfig(object across, object width, object d0, object w0) => Py.L(
        Kw(("key", "runMode"), ("label", "Run mode"), ("group", "Run"), ("options", Py.L("Width", "Depth")), ("default", 0),
            ("tooltip", "What it builds up: Width along the axle, Depth (the diameter) out from the hub. A wire into Mode decides instead: on is Depth.")),
        Kw(("key", "width"), ("label", "Order width"), ("group", "Orders"), ("unit", "m"), ("min", w0), ("max", width), ("step", 0.05), ("default", w0),
            ("tooltip", "The wheel's width when Queue on its screen orders one.")),
        Kw(("key", "height"), ("label", "Order diameter"), ("group", "Orders"), ("unit", "m"), ("min", d0), ("max", across), ("step", 0.05), ("default", d0),
            ("tooltip", "The wheel's diameter when Queue on its screen orders one.")));

    private const string Red = "#b5452b"; // the Extruder family's body colour
    private const string Violet = "#8050a8"; // the Formers'

    // ---- The Extruder on the block system (design page drawing 15; MachineBlocks.cs, ConstructExtruderGrowth.cs) ----------------------
    // One Extruder Core, any number of identical 2 x 2 m Die Blocks welded beside it flush with its die face, and one Run-out Table.
    // The die is the largest full rectangle of blocks that holds one welded to the Core; each block in it adds its push and its draw.
    private const double CoreDieY = 0.5; // the Core's own 0.5 m die: low on its face, where a conveyor takes its pieces
    private const double Face = 0.75; // the die face (z), flush on the Core and every block
    private const double BlockBack = -3.25; // a Die Block is 4 m deep: container slice, heater and ram behind its die tile
    private const int BlockPush = 6000, BlockPower = 900, CoreBin = 32000;
    private const double TableLength = 8, TableDepth = 0.3; // a Run-out Table: 2 m wide, 8 m long, its top level with the machine's foot

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "extruder"), ("role", "core"), ("plane", "face"), ("origin", Py.L(0, 1, 0)))
        : Kw(("family", "extruder"), ("role", role), ("plane", "face"));

    private static Part ExtruderCore()
    {
        var p = new Part("Extruder", Small, Section,
            "An Extruder Core: pushes bulk material through a die as one long piece. Alone it has a 0.5 m die; weld Die Blocks beside it, flush with its die face, and the die is their rectangle, up to 32 x 32 m. Cut by signal or by length. Needs power.",
            role: "storage", group: "Production", color: Red);
        // A 2 x 2 m face, 1.5 m deep: the hopper let into the back of its top, the control board on its left side, the die low on its face.
        p.Add(Box([-1, 0, -0.75], [1, 1.7, 0.75]));
        p.Add(Box([-1, 1.7, -0.05], [1, 2, 0.75])!.Update(("finish", "tread plate")));
        p.Add(Box([-1, 1.7, -0.75], [-0.94, 2, -0.05], Trim));
        p.Add(Box([0.94, 1.7, -0.75], [1, 2, -0.05], Trim));
        p.Add(Box([-0.94, 1.7, -0.75], [0.94, 2, -0.69], Trim));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, 1, 0], "+x"); // (the first Die Block: beside it, flush with the die face; more go beside and on top of that one)
        p.Sock("back", [0, 0.85, -0.75], "-z");
        p.Sock("die", [0, CoreDieY, Face], "+z", kind: "die");
        p.Sock("outBed", [0, 0, Face], "+z", kind: "runOut"); // (a Run-out Table: under the die)
        p.Stats(1500, 1500, 6, 8);
        p.Set(("power", BlockPower), ("storage", Kw(
                ("bulk", Kw(("material", "any"), ("capacity", CoreBin), ("min", Py.L(-0.935, 1.705, -0.685)), ("max", Py.L(0.935, 1.995, -0.055)))),
                ("extrude", Kw(("speed", 0.2), ("die", Py.L(0.5, 0.5)), ("at", Py.L(0, CoreDieY, Face)), ("maxSection", Py.L(32, 32)), ("maxLength", 16), ("pushForce", BlockPush),
                    ("growLength", 192), ("blockPush", BlockPush), ("blockPower", BlockPower))))),
            ("machineBlock", BlockKey("core")),
            ("config", ExtruderConfig(0.5, 32, 192, "Die Blocks")));
        WithScreen(p, orders: true, faces: ["-x"], width: 0.84, recipe: true);
        MachineLook.Skirt(p, -1, 1, -0.75, 0.75, 0, 0.14, 0.012);
        // The hopper's grating.
        foreach (var x in new[] { -0.75, -0.45, 0.45, 0.75 })
            p.Add(L([x - 0.015, 1.965, -0.7], [x + 0.015, 1.99, -0.04], MachineLook.Base));
        foreach (var z in new[] { -0.6, -0.2 })
            p.Add(L([-0.95, 1.965, z - 0.015], [0.95, 1.99, z + 0.015], MachineLook.Base));
        // The chute's port on its back, under the hopper's arrow (the shared port look: MachineLook.Port; Sean 2026-11-21).
        MachineLook.Port(p, [0, 0.8, -0.75], 2, -1, 0.2, 0.13, true);
        p.Set(("ports", Py.L(Kw(("name", "Chute in"), ("type", "chute"), ("at", Py.L(0, 0.8, -0.75)), ("face", "-z")))));
        // The hopper's rim, white (the in side), and an arrow down into it.
        p.Add(L([0.94, 1.94, -0.75], [0.998, 1.998, -0.05], MachineLook.In));
        p.Add(L([-0.998, 1.94, -0.75], [-0.94, 1.998, -0.05], MachineLook.In));
        p.Add(L([-0.94, 1.94, -0.75], [0.94, 1.998, -0.69], MachineLook.In));
        MachineLook.Arrow(p, 2, -0.75, -1, 1, -1, 1.35, 0, 0.3, 0.3, MachineLook.In);
        // The die head: a bare steel collar round the die, the die plate dull until it works, hazard paint under the mouth, an orange
        // band (the out side) along the top of the face.
        p.Add(L([-0.31, CoreDieY - 0.31, Face], [-0.25, CoreDieY + 0.31, Face + 0.016], MachineLook.Steel, "steel"));
        p.Add(L([0.25, CoreDieY - 0.31, Face], [0.31, CoreDieY + 0.31, Face + 0.016], MachineLook.Steel, "steel"));
        p.Add(L([-0.25, CoreDieY + 0.25, Face], [0.25, CoreDieY + 0.31, Face + 0.016], MachineLook.Steel, "steel"));
        p.Add(L([-0.25, CoreDieY - 0.31, Face], [0.25, CoreDieY - 0.25, Face + 0.016], MachineLook.Steel, "steel"));
        p.Add(MachineLook.Work(L([-0.25, CoreDieY - 0.25, Face - 0.01], [0.25, CoreDieY + 0.25, Face + 0.008], MachineLook.Hot), "glow"));
        MachineLook.Hazard(p, [-1, 0.02, Face], [1, 0.12, Face + 0.014]);
        MachineLook.Hazard(p, [-0.9, 1.2, Face], [0.9, 1.5, Face + 0.014]); // (crush and burn: it never stops for anyone in front of it)
        p.Add(L([-1, 1.72, Face], [1, 1.86, Face + 0.014], MachineLook.Out));
        MachineLook.Arrow(p, 1, 1.98, 1, 2, 1, 0, 0.45, 0.6, 0.4, MachineLook.Out);
        // Cladding seams on its back (the body's own tone, so they take its paint).
        foreach (var x in new[] { -0.5, 0.5 })
            p.Add(L([x - 0.012, 0.16, -0.764], [x + 0.012, 1.6, -0.75], Trim, paintable: true));
        // The shear over its die: a guillotine blade in two guides, down and up as each new piece starts (drawing 11).
        foreach (var x in new[] { -0.4, 0.4 })
            p.Add(L([x - 0.03, 0.14, Face - 0.004], [x + 0.03, 1.16, Face + 0.03], MachineLook.Base));
        p.Add(MachineLook.Work(L([-0.37, CoreDieY + 0.33, Face + 0.006], [0.37, CoreDieY + 0.41, Face + 0.03], MachineLook.Steel, "steel"), "shear", "-y", 0.3));
        p.Add(MachineLook.Work(L([-0.37, CoreDieY + 0.314, Face + 0.01], [0.37, CoreDieY + 0.33, Face + 0.026], MachineLook.Accent, "hazard paint"), "shear", "-y", 0.3));
        // On top: the fume stack (it puffs while it works) and the feed screw's motor, its coupling turning.
        p.Add(L([0.6, 1.996, 0.3], [0.8, 2.45, 0.5], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y"))));
        p.Add(MachineLook.Work(L([0.58, 2.45, 0.28], [0.82, 2.5, 0.52], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y"))), "smoke"));
        p.Add(L([-0.85, 1.996, 0.15], [-0.6, 2.25, 0.45], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))));
        p.Add(MachineLook.Work(L([-0.6, 2.04, 0.21], [-0.5, 2.2, 0.39], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+x"))), "spin", "+x"));
        p.Add(L([-0.5, 1.996, 0.25], [-0.36, 2.16, 0.35], MachineLook.Base)); // (the screw's bearing housing, into the hopper's side)
        // On its back: a pressure gauge and the maker's plate.
        p.Add(L([0.64, 1.2, -0.786], [0.84, 1.4, -0.744], "#e8e6df", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))));
        p.Add(L([0.72, 1.28, -0.798], [0.76, 1.37, -0.782], MachineLook.Ink));
        p.Add(L([-0.86, 1.2, -0.774], [-0.6, 1.36, -0.744], MachineLook.In));
        p.Add(L([-0.83, 1.26, -0.786], [-0.63, 1.3, -0.77], MachineLook.Ink));
        return p;
    }

    /// <summary>
    /// A Die Block: one 2 x 2 m cell of an Extruder's die, 4 m deep, with its own die tile, container slice, heater and ram (design page
    /// drawings 10, 11 and 14). Built as a press frame: a front and a back platen, the container between them, a tie rod in each corner
    /// with a nut at both ends. What is on a side is drawn only while no block of the same machine is welded there (MachineBlocks:
    /// "seam"), and a corner's notch is filled where a block is ("seamFill"), so a bank of blocks reads as one machine: one outer
    /// frame with a hazard edge at its foot, one die plate across the face, tie rods at its four corners, oil lines down its right
    /// side, heater bands on its outer sides and top, a shear along its top edge, and a bank of rams behind that move as the piece grows.
    /// </summary>
    private static Part DieBlock()
    {
        var p = new Part("Die Block", Small, Section,
            "Weld it beside an Extruder Core, or beside, above or below another Die Block, flush with the die face: 2 x 2 m more die. The die is the largest full rectangle of blocks next to the Core; a block outside it idles.",
            group: "Production", color: Red);
        const double e = 0.997, n = 0.09; // (the body stops 3 mm short of the cell, so two blocks' faces never meet on one plane; n: the corner notch the tie rods run in)
        const double front = Face - 0.2, back = BlockBack + 0.253; // (the front platen's inner face, and where the back's bevel ends: the container runs between them)
        // The body: the container from the back's bevel to the front platen, its four long edges notched for the tie rods; the front platen full square.
        // Its sides and top are sunk 7 cm between raised ribs (the heater collar is one), so the body reads as cast panels, not one slab;
        // where a block is welded on, a fill in the body's tone closes the sunk panel so no slot runs between blocks.
        const double d = 0.07;
        p.Add(Box([-e + d, n, back], [e - d, 2 - n, front]));
        p.Add(Box([-e + n, 0.003, back], [e - n, 1.997 - d, front]));
        foreach (var (z0, z1) in new[] { (-2.22, -1.38), (-0.62, -0.5) })
        {
            p.Add(Box([-e, n, z0], [e, 2 - n, z1]));
            p.Add(Box([-e + n, 0.003, z0], [e - n, 1.997, z1]));
        }
        p.Add(L([e - d - 0.01, n, back + 0.004], [e - 0.004, 2 - n, front - 0.004], Red, paintable: true)!.Update(("seamFill", "+x")));
        p.Add(L([-e + 0.004, n, back + 0.004], [-e + d + 0.01, 2 - n, front - 0.004], Red, paintable: true)!.Update(("seamFill", "-x")));
        p.Add(L([-e + n, 1.997 - d - 0.01, back + 0.004], [e - n, 1.993, front - 0.004], Red, paintable: true)!.Update(("seamFill", "+y")));
        // In the sunk panels: a flat stiffener along each end panel, louvres in the middle one, a hot-surface plate by the heaters.
        foreach (var (sx, side) in new[] { (1, "+x"), (-1, "-x") })
        {
            double[] Xs(double a, double b) => sx > 0 ? [a, b] : [-b, -a];
            var bar = Xs(e - d - 0.006, e - d + 0.024);
            foreach (var (z0, z1) in new[] { (back + 0.06, -2.24), (-0.48, front - 0.02) })
                p.Add(Seam(L([bar[0], 0.94, z0], [bar[1], 1.04, z1], Red, paintable: true), side));
            var slat = Xs(e - d - 0.006, e - d + 0.018);
            foreach (var y in new[] { 0.55, 0.7, 0.85, 1.0, 1.15, 1.3 })
                p.Add(Seam(L([slat[0], y, -1.3], [slat[1], y + 0.05, -0.7], MachineLook.Base), side));
            var plate = Xs(e - d - 0.006, e - d + 0.012);
            p.Add(Seam(L([plate[0], 1.3, -0.35], [plate[1], 1.5, 0.05], MachineLook.Accent, "hazard paint"), side));
        }
        foreach (var (z0, z1) in new[] { (back + 0.06, -2.24), (-0.48, front - 0.02) })
            p.Add(Seam(L([-0.04, 1.997 - d - 0.006, z0], [0.04, 1.997 - d + 0.024, z1], Red, paintable: true), "+y"));
        p.Add(Box([-e, 0.003, front], [e, 1.997, Face])); // (the front platen: its face is the die face, where the out bed socket sits)
        Bevel(p, e, back);
        // The die plate across the whole face, one plate when blocks are welded (dull until it works), and the front frame round it, each side a seam.
        p.Add(MachineLook.Work(L([-1, 0, Face - 0.01], [1, 2, Face + 0.006], MachineLook.Hot), "glow"));
        p.Add(Seam(L([0.94, 0, Face - 0.02], [1, 2, Face + 0.02], MachineLook.Steel, "steel"), "+x"));
        p.Add(Seam(L([-1, 0, Face - 0.02], [-0.94, 2, Face + 0.02], MachineLook.Steel, "steel"), "-x"));
        p.Add(Seam(L([-0.94, 1.94, Face - 0.02], [0.94, 2, Face + 0.02], MachineLook.Steel, "steel"), "+y"));
        p.Add(Seam(MachineLook.D([-0.94, 0, Face - 0.02], [0.94, 0.06, Face + 0.02], MachineLook.Accent, "hazard paint"), "-y")); // (the frame's foot: the hazard edge under the die)
        // Bolt heads along the front frame, four a side.
        foreach (var t in new[] { -0.6, -0.2, 0.2, 0.6 })
        {
            p.Add(Seam(L([0.958, 1 + t - 0.016, Face + 0.016], [0.982, 1 + t + 0.016, Face + 0.036], MachineLook.Base), "+x"));
            p.Add(Seam(L([-0.982, 1 + t - 0.016, Face + 0.016], [-0.958, 1 + t + 0.016, Face + 0.036], MachineLook.Base), "-x"));
            p.Add(Seam(L([t - 0.016, 1.958, Face + 0.016], [t + 0.016, 1.982, Face + 0.036], MachineLook.Base), "+y"));
        }
        // The tie rods: one in each corner's notch, platen to platen, a hex nut proud of each platen; only at the machine's corners.
        // Where a block is welded on either side, the notch is filled in the body's own tone instead, so no groove runs between blocks.
        foreach (var (sx, sy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            var cx = sx * (1 - n / 2);
            var cy = sy > 0 ? 2 - n / 2 : n / 2;
            string[] sides = [sx > 0 ? "+x" : "-x", sy > 0 ? "+y" : "-y"];
            var r = 0.032;
            p.Add(Seam(L([cx - r, cy - r, back + 0.004], [cx + r, cy + r, front + 0.03], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+z"))), sides));
            var h = 0.042;
            p.Add(Seam(L([cx - h, cy - h, Face + 0.016], [cx + h, cy + h, Face + 0.062], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "+z"))), sides));
            p.Add(Seam(L([cx - h, cy - h, back + 0.008], [cx + h, cy + h, back + 0.054], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-z"))), sides)); // (on the bevel's top)
            double x0 = sx > 0 ? 1 - n - 0.004 : -e, x1 = sx > 0 ? e : -1 + n + 0.004, y0 = sy > 0 ? 2 - n - 0.004 : 0.003, y1 = sy > 0 ? 1.997 : n + 0.004;
            p.Add(L([x0, y0, back + 0.004], [x1, y1, front - 0.004], Red, paintable: true)!.Update(("seamFill", Py.L(sides[0], sides[1]))));
        }
        // The heater bands round the container slice (hot while it works) on the machine's outer sides and top: a dark ceramic band, its
        // glowing coils between ribs, and a terminal box with its cable.
        foreach (var (sx, side) in new[] { (1, "+x"), (-1, "-x") })
        {
            double[] Xs(double a, double b) => sx > 0 ? [a, b] : [-b, -a]; // (a span out from the right side, mirrored to the left)
            var band = Xs(0.993, 1.009);
            p.Add(Seam(L([band[0], 0.3, -2.15], [band[1], 1.7, -1.45], MachineLook.Base), side));
            var coil = Xs(1.005, 1.017);
            foreach (var z in new[] { -2.05, -1.85, -1.65 })
                p.Add(Seam(MachineLook.Work(L([coil[0], 0.36, z], [coil[1], 1.64, z + 0.1], MachineLook.Hot), "glow")!, side));
            var term = Xs(0.94, 1.017);
            p.Add(Seam(L([term[0], 1.5, -1.4], [term[1], 1.68, -1.22], MachineLook.Base), side)); // (its terminal box, down to the sunk panel)
        }
        p.Add(Seam(L([-0.85, 1.993, -2.15], [0.85, 2.009, -1.45], MachineLook.Base), "+y"));
        foreach (var z in new[] { -2.05, -1.85, -1.65 })
            p.Add(Seam(MachineLook.Work(L([-0.8, 2.005, z], [0.8, 2.017, z + 0.1], MachineLook.Hot), "glow")!, "+y"));
        // The oil lines down the machine's right side, red feed and blue return, clipped to the body every metre; a junction block at the back.
        foreach (var (y, colour) in new[] { (1.76, "#b8322a"), (1.83, "#2f62b5") })
            p.Add(Seam(L([0.975, y - 0.02, back + 0.05], [1.015, y + 0.02, front - 0.05], colour, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+z"))), "+x"));
        foreach (var z in new[] { -2.6, -0.9, 0.2 })
            p.Add(Seam(L([0.94, 1.725, z - 0.03], [1.017, 1.865, z + 0.03], MachineLook.Base), "+x")); // (reaching into the sunk panel)
        p.Add(Seam(L([0.94, 1.71, back + 0.004], [1.017, 1.88, back + 0.2], MachineLook.Base), "+x"));
        // The shear across the bottom row's top: a guillotine bar over the piece on the die's foot, down and up after each cut.
        p.Add(Seam(MachineLook.Work(L([-0.93, 1.86, Face + 0.022], [0.93, 1.935, Face + 0.046], MachineLook.Steel, "steel"), "shear", "-y", 0.3)!, "-y")); // (bottom row only: the piece runs out along the die's foot)
        p.Add(Seam(MachineLook.Work(L([-0.93, 1.835, Face + 0.026], [0.93, 1.86, Face + 0.044], MachineLook.Accent, "hazard paint"), "shear", "-y", 0.3)!, "-y"));
        // The skirt at its foot, a seam.
        p.Add(Seam(L([-0.94, 0, back + 0.25], [0.94, 0.14, front - 0.1], MachineLook.Base), "-y"));
        // The back: the ram's housing and its rod, which slides in as the piece grows and comes back for the next billet each metre;
        // two hoses from the housing to the platen; on the bottom row only (a big bank's inner blocks draw less), a pressure gauge, grease points and a maker's plate.
        p.Add(L([-0.45, 0.55, BlockBack - 0.7], [0.45, 1.45, BlockBack + 0.006], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "-z"))));
        p.Add(L([-0.5, 0.5, BlockBack - 0.73], [0.5, 1.5, BlockBack - 0.69], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "-z"))));
        p.Add(MachineLook.Work(L([-0.18, 0.82, BlockBack - 1.0], [0.18, 1.18, BlockBack - 0.66], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))), "ram", "+z", 0.5));
        p.Add(MachineLook.Work(L([-0.24, 0.76, BlockBack - 1.06], [0.24, 1.24, BlockBack - 0.98], MachineLook.Base), "ram", "+z", 0.5)); // (the rod's end plate)
        // Gussets from the housing out to the bevel, stepped to taper, and bolts round the housing's flange.
        foreach (var (a0, a1, b0, b1, deep) in new[] { (-0.03, 0.03, 1.43, 1.6, 0.3), (-0.03, 0.03, 1.6, 1.74, 0.14), (-0.03, 0.03, 0.4, 0.57, 0.3), (-0.03, 0.03, 0.26, 0.4, 0.14) })
            p.Add(L([a0, b0, BlockBack - deep], [a1, b1, BlockBack + 0.006], MachineLook.Base));
        foreach (var (a0, a1, deep) in new[] { (0.43, 0.6, 0.3), (0.6, 0.74, 0.14), (-0.6, -0.43, 0.3), (-0.74, -0.6, 0.14) })
            p.Add(L([a0, 0.97, BlockBack - deep], [a1, 1.03, BlockBack + 0.006], MachineLook.Base));
        for (var k = 0; k < 8; k++)
        {
            var (bx, by) = (0.43 * Math.Cos(k * Math.PI / 4 + Math.PI / 8), 1 + 0.43 * Math.Sin(k * Math.PI / 4 + Math.PI / 8));
            p.Add(L([bx - 0.022, by - 0.022, BlockBack - 0.75], [bx + 0.022, by + 0.022, BlockBack - 0.72], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-z"))));
        }
        foreach (var (x, colour) in new[] { (-0.32, "#b8322a"), (0.32, "#2f62b5") })
            p.Add(L([x - 0.025, 1.33, BlockBack - 0.6], [x + 0.025, 1.38, BlockBack + 0.006], colour)); // (a hose from the housing into the platen)
        p.Add(Seam(L([0.5, 1.48, BlockBack - 0.036], [0.66, 1.64, BlockBack + 0.006], "#e8e6df", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))), "-y")); // (the gauge's face)
        p.Add(Seam(L([0.56, 1.54, BlockBack - 0.048], [0.6, 1.62, BlockBack - 0.032], MachineLook.Ink), "-y")); // (its needle)
        p.Add(Seam(L([-0.7, 1.5, BlockBack - 0.024], [-0.44, 1.66, BlockBack + 0.006], MachineLook.In), "-y")); // (the maker's plate)
        p.Add(Seam(L([-0.67, 1.56, BlockBack - 0.036], [-0.47, 1.6, BlockBack - 0.02], MachineLook.Ink), "-y"));
        foreach (var (x, y) in new[] { (-0.7, 0.3), (0.7, 0.3) })
            p.Add(Seam(L([x - 0.02, y - 0.02, BlockBack - 0.03], [x + 0.02, y + 0.02, BlockBack + 0.006], MachineLook.Accent), "-y")); // (grease points)
        p.Sock("+x", [1, 1, 0], "+x");
        p.Sock("-x", [-1, 1, 0], "-x");
        p.Sock("top", [0, 2, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("outBed", [0, 0, Face], "+z", kind: "runOut"); // (a Run-out Table under this block's share of the die)
        p.Stats(6000, 3000, 8, 20);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));

    /// <summary>
    /// A Die Block's back end, bevelled in all round (Sean 2026-11-21) from the full outline at <paramref name="z1"/> to a face c smaller at the
    /// block's back: a flat middle, a sloped strip on each side and a mitred corner, each drawn only on the machine's outer edges. Where a
    /// block is welded on, its side's fill (and at a corner the other side's slope run on across it) carries the outline over, so a bank
    /// reads as one bevelled machine. A hidden box keeps the whole end solid.
    /// </summary>
    private static void Bevel(Part p, double e, double z1)
    {
        const double c = 0.25, lo = 0.003, hi = 1.997;
        var z0 = BlockBack + 0.003;
        PyDict Prism(List<double[]> pts)
        {
            double[] mn = [.. Enumerable.Range(0, 3).Select(i => pts.Min(a => a[i]))];
            double[] mx = [.. Enumerable.Range(0, 3).Select(i => pts.Max(a => a[i]))];
            return Box(mn, mx, Red, detail: true, shape: Kw(("shape", "hull"), ("points", pts.Select(a => (object?)a.Select(x => (object?)x).ToList()).ToList()), ("paintable", true)))!;
        }
        p.Add(Box([-e, lo, z0], [e, hi, z1], hidden: true));
        p.Add(Box([-e + c, lo + c, z0], [e - c, hi - c, z1]));
        // A point on the end: u across x (-1..1 maps to -e..e), v up y, both pulled in by c at the back.
        double X(int sx, bool inner) => sx * (inner ? e - c : e);
        double Y(int sy, bool inner) => sy > 0 ? (inner ? hi - c : hi) : (inner ? lo + c : lo);
        foreach (var sx in new[] { 1, -1 })
        {
            var side = sx > 0 ? "+x" : "-x";
            double y0 = lo + c, y1 = hi - c, xo = X(sx, false), xi = X(sx, true);
            p.Add(Prism([[xi, y0, z1], [xi, y1, z1], [xo, y0, z1], [xo, y1, z1], [xi, y0, z0], [xi, y1, z0]]).Update(("seam", side)));
            p.Add(L([Math.Min(xi, xo), y0, z0], [Math.Max(xi, xo), y1, z1 - 0.004], Red, paintable: true)!.Update(("seamFill", side)));
        }
        foreach (var sy in new[] { 1, -1 })
        {
            var side = sy > 0 ? "+y" : "-y";
            double x0 = -e + c, x1 = e - c, yo = Y(sy, false), yi = Y(sy, true);
            p.Add(Prism([[x0, yi, z1], [x1, yi, z1], [x0, yo, z1], [x1, yo, z1], [x0, yi, z0], [x1, yi, z0]]).Update(("seam", side)));
            p.Add(L([x0, Math.Min(yi, yo), z0], [x1, Math.Max(yi, yo), z1 - 0.004], Red, paintable: true)!.Update(("seamFill", side)));
        }
        foreach (var (sx, sy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            string xs = sx > 0 ? "+x" : "-x", ys = sy > 0 ? "+y" : "-y";
            double xo = X(sx, false), xi = X(sx, true), yo = Y(sy, false), yi = Y(sy, true);
            // Both sides open: the mitred corner. The x side welded: the y side's slope runs on across it; the y side welded: the x side's.
            p.Add(Prism([[xi, yi, z1], [xo, yi, z1], [xi, yo, z1], [xo, yo, z1], [xi, yi, z0]]).Update(("seam", Py.L(xs, ys))));
            double zb = z1 - 0.004, zc = z1 - 0.008; // (their joint faces 4 mm apart, so no two share a plane)
            p.Add(Prism([[xi, yi, zb], [xo, yi, zb], [xi, yo, zb], [xo, yo, zb], [xi, yi, z0], [xo, yi, z0]]).Update(("seamFill", xs)));
            p.Add(Prism([[xi, yi, zc], [xo, yi, zc], [xi, yo, zc], [xo, yo, zc], [xi, yi, z0], [xi, yo, z0]]).Update(("seamFill", ys)));
        }
    }

    private static PyDict Seam(PyDict? box, string[] faces) => box!.Update(("seam", Py.L(faces[0], faces[1])));

    private static List<object?> FanConfig(object across, object start) => Py.L(
        Kw(("key", "fanKind"), ("type", "choice"), ("label", "Makes"), ("group", "Prop"), ("options", FanKinds()), ("default", 0),
            ("tooltip", "What it makes. A prop runs on a shaft, an electric one or a ducted fan on its own motor, a helicopter rotor takes collective and cyclic.")),
        Kw(("key", "fanBlades"), ("label", "Blades"), ("group", "Prop"), ("min", 2), ("max", 16), ("step", 1), ("default", 3),
            ("tooltip", "How many blades, 2 to 16. Each has its own health: more take a little more power, push a little harder, and are steadier when one goes.")),
        Kw(("key", "bladeStyle"), ("type", "choice"), ("label", "Blade style"), ("group", "Prop"), ("options", BladeStyles()), ("default", 0),
            ("tooltip", "The blade's shape: a small trade of push, power and top speed. The prop keeps it.")),
        Kw(("key", "fanDiameter"), ("label", "Across"), ("group", "Prop"), ("unit", "m"), ("min", start), ("max", across), ("step", 0.05), ("default", OMin(across, OMax(start, 2))),
            ("tooltip", "How far across. It parts off by itself at this size. Power and speed follow from the size.")));

    private static List<object?> AirframeConfig(object span, object chord, object s0, object c0) => Py.L(
        Kw(("key", "wingKind"), ("type", "choice"), ("label", "Cuts"), ("group", "Airframe"), ("options", Airframes()), ("default", 0),
            ("tooltip", "What it cuts. Its bin's material is the skin: cloth, wood, planks, laminate, metal or steel. Cloth is light and tears.")),
        Kw(("key", "wingChord"), ("label", "Chord"), ("group", "Airframe"), ("unit", "m"), ("min", c0), ("max", chord), ("step", 0.05), ("default", OMin(chord, OMax(c0, 1.5))),
            ("tooltip", "From leading edge to trailing edge. It stays what it was when the piece started.")),
        Kw(("key", "wingSpan"), ("label", "Span"), ("group", "Airframe"), ("unit", "m"), ("min", s0), ("max", span), ("step", 0.05), ("default", OMin(span, OMax(s0, 2))),
            ("tooltip", "How long. It parts off by itself at this length. Weld pieces along their span for a longer wing.")));

    // ---- The Grow Former on the block system (design page drawing 22; MachineBlocks.cs, ConstructGrowFormer.cs) ------------------------
    // One Grow Former Core (2 x 2 x 2 m), Former Blocks on the floor beside it for its bed, and Mast Blocks (the shared 2 m cube) stacked on
    // it for the height its hub rides up to. Its Makes setting picks a wheel, a prop or a wing; an order makes what it asks.
    private const double GrowHubY = 1.2; // the hub's rest height on the Core's face
    private const double GrowFace = 1.0; // the Core's front face (z), where the hub's plate is
    private const double BedTile = 0.3, BedDeck = 0.012, BedKerb = 0.02; // a Former Block: its slab, the deck tiles on it and the hazard kerb round the bed
    private const double GrowRide = 40; // m: how far the hub's carriage and chuck may ride up the slide (the masts' column; drawn only)
    private const int GrowBlockPush = 1500, GrowBlockPower = 225, GrowBin = 13200; // each block's push (N) and draw (W); a bin of 400 and 100 a block for the biggest bed
    private static List<object?> GrowMakesNames() => Py.L("Wheel", "Prop", "Wing");

    private static PyDict GrowBlockKey(string role) => role == "core"
        ? Kw(("family", "grower"), ("role", "core"), ("origin", Py.L(0, 1, 0)))
        : Kw(("family", "grower"), ("role", role));

    /// <summary>
    /// The Grow Former Core: a 2 m cube on a frame (corner posts, a top and a foot slab, the body sunk 5 cm between them), its hub on a
    /// carriage between two slide rails on its face, the part-off saw on an arm beside it, the hopper's port on its back, the drive and
    /// its oil lines on the right, the screen on the left. The hub's plate and chuck turn while it grows a wheel or a prop and ride up the
    /// slide as far as the piece's size asks; the wing root clamps glow while it works; the saw strokes.
    /// </summary>
    private static Part GrowFormerCore()
    {
        var p = new Part("Grow Former", Small, Section,
            "A Grow Former Core: grows a wheel, a prop or a wing on its hub, by its Makes setting or by order. Weld Former Blocks on the floor beside it for its bed and stack Mast Blocks on it for height: a wheel's width fits the bed's length and its diameter the bed's width and the masts plus 2 m; a prop is up to twice the bed's width; a wing's chord fits across the bed and its span runs along it and on over Run-out Tables. Needs power.",
            role: "storage", group: "Production", color: Violet);
        const double e = 1, post = 0.16, sunk = 0.95, foot = 0.16, top = 1.84;
        // The frame: a foot slab and a top slab whole, a post up each corner, the body sunk 5 cm between them (cast panels, not one block).
        p.Add(Box([-e, 0, -e], [e, foot, e]));
        p.Add(Box([-e, top, -e], [e, 2, e]));
        p.Add(Box([-sunk, foot, -sunk], [sunk, top, sunk]));
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            double x0 = sx > 0 ? e - post : -e, z0 = sz > 0 ? e - post : -e;
            p.Add(Box([x0, foot, z0], [x0 + post, top, z0 + post]));
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 2, 0], "+y"); // (Mast Blocks: the column its hub rides up)
        p.Sock("+z", [0, BedTile / 2, e], "+z"); // (Former Blocks: the bed in front of the hub, and beside it)
        p.Sock("+x", [e, BedTile / 2, 0], "+x");
        p.Sock("-x", [-e, BedTile / 2, 0], "-x");
        p.Sock("back", [0, 1, -e], "-z");
        p.Sock("die", [0, GrowHubY, GrowFace], "+z", kind: "die");
        p.Stats(2400, 2000, 8, 12);
        p.Set(("power", GrowBlockPower), ("storage", Kw(
                ("bulk", Kw(("material", "any"), ("capacity", GrowBin), ("min", Py.L(-0.6, 0.9, -0.9)), ("max", Py.L(0.6, 1.7, -0.3)))),
                ("wheel", Kw(("grow", true), ("speed", 0.1), ("at", Py.L(0, GrowHubY, GrowFace)), ("maxWidth", 20), ("maxDiameter", 32), ("startWidth", 0.1), ("startDiameter", 0.4),
                    ("fanMax", 64), ("fanStart", 0.5), ("wingMax", Py.L(32, 192)), ("wingStart", Py.L(0.3, 0.5)), ("bedTop", BedTile + BedDeck + BedKerb),
                    ("pushForce", GrowBlockPush), ("blockPush", GrowBlockPush), ("blockPower", GrowBlockPower))))),
            ("machineBlock", GrowBlockKey("core")),
            ("config", GrowConfig()),
            ("collider", "boxes")); // (the hub and the saw are looks: their cylinders never become a hull)
        WithScreen(p, orders: true, faces: ["-x"], width: 0.84, recipe: true);
        // Its foot: a dark skirt, each side drawn only while no Former Block is welded there (where one is, the bed's kerb takes over).
        foreach (var (side, mn, mx) in new[]
        {
            ("+z", new[] { -e, 0.0, e }, new[] { e, foot - 0.02, e + 0.012 }), ("-z", new[] { -e, 0.0, -e - 0.012 }, new[] { e, foot - 0.02, -e }),
            ("+x", new[] { e, 0.0, -e + 0.003 }, new[] { e + 0.012, foot - 0.02, e - 0.003 }), ("-x", new[] { -e - 0.012, 0.0, -e + 0.003 }, new[] { -e, foot - 0.02, e - 0.003 }),
        })
            p.Add(Seam(L(mn, mx, MachineLook.Base), side));
        // Ribs across the sunk panels at mid height (the body's own tone, so they take its paint), and grease points on the posts.
        foreach (var (sx, sz) in new[] { (1, 0), (-1, 0), (0, -1) })
        {
            if (sx != 0)
            {
                var x0 = sx > 0 ? sunk : -sunk - 0.03;
                if (sx > 0)
                    p.Add(L([x0 - 0.004, 0.48, -e + post], [x0 + 0.03, 0.54, e - post], Violet, paintable: true));
            }
            else
                p.Add(L([-e + post, 0.48, -sunk - 0.03], [e - post, 0.54, -sunk + 0.004], Violet, paintable: true));
        }
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            p.Add(L([sx * (e - post / 2) - 0.02, top - 0.12, sz * e - (sz > 0 ? 0 : 0.012)], [sx * (e - post / 2) + 0.02, top - 0.08, sz * e + (sz > 0 ? 0.012 : 0)], MachineLook.Accent));
        // The top: two lifting eyes and a hazard band, only while no Mast Block stands on it.
        foreach (var x in new[] { -0.6, 0.6 })
            p.Add(Seam(L([x - 0.1, 2, -0.06], [x + 0.1, 2.14, 0.06], MachineLook.Steel, "steel"), "+y"));
        p.Add(Seam(MachineLook.D([-e + 0.003, 2, e - 0.12], [e - 0.003, 2.012, e - 0.003], MachineLook.Accent, "hazard paint"), "+y"));

        // ---- The face: the slide, the carriage and the hub ----
        const double f = GrowFace;
        foreach (var x in new[] { -0.46, 0.46 })
        {
            p.Add(L([x - 0.05, foot + 0.02, sunk + 0.004], [x + 0.05, top - 0.02, f + 0.03], MachineLook.Steel, "steel")); // (a slide rail, from the sunk face out past the posts)
            p.Add(L([x - 0.07, foot + 0.02, sunk + 0.004], [x + 0.07, foot + 0.1, f + 0.04], MachineLook.Base)); // (its foot clamp)
        }
        // The carriage the hub is mounted on: it rides the rails ("slide"), with its two gibs on them and the slide's cable.
        p.Add(MachineLook.Work(L([-0.4, GrowHubY - 0.38, f + 0.03], [0.4, GrowHubY + 0.38, f + 0.07], MachineLook.Base), "slide", "+y", GrowRide));
        foreach (var x in new[] { -0.46, 0.46 })
            p.Add(MachineLook.Work(L([x - 0.07, GrowHubY - 0.3, f + 0.03], [x + 0.07, GrowHubY + 0.3, f + 0.058], MachineLook.Accent), "slide", "+y", GrowRide));
        // The hub: a bare steel face plate, a yellow four-jaw chuck and a dark boss; all of it turns while a wheel or a prop grows, and rides the carriage.
        p.Add(MachineLook.Work(L([-0.32, GrowHubY - 0.32, f + 0.07], [0.32, GrowHubY + 0.32, f + 0.1], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 24))), "spin", "+z", GrowRide));
        p.Add(MachineLook.Work(L([-0.28, GrowHubY - 0.04, f + 0.1], [0.28, GrowHubY + 0.04, f + 0.13], MachineLook.Accent), "spin", "+z", GrowRide));
        p.Add(MachineLook.Work(L([-0.04, GrowHubY - 0.28, f + 0.1], [0.04, GrowHubY + 0.28, f + 0.134], MachineLook.Accent), "spin", "+z", GrowRide));
        p.Add(MachineLook.Work(L([-0.09, GrowHubY - 0.09, f + 0.1], [0.09, GrowHubY + 0.09, f + 0.15], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12))), "spin", "+z", GrowRide));
        // The wing root clamps over and under the hub: dull until it works, then hot (they set a wing's skin at its root).
        foreach (var y in new[] { GrowHubY + 0.46, GrowHubY - 0.46 })
        {
            p.Add(L([-0.3, y - 0.05, sunk + 0.004], [0.3, y + 0.05, f + 0.03], MachineLook.Base));
            p.Add(MachineLook.Work(L([-0.26, y - 0.03, f + 0.03], [0.26, y + 0.03, f + 0.046], MachineLook.Hot), "glow"));
        }
        // The part-off saw on its arm by the hub's right: a dark arm down the post, a hazard-banded guard and the blade, which strokes in across the hub's face.
        p.Add(L([0.62, 0.5, f], [0.78, 1.9, f + 0.08], MachineLook.Base));
        MachineLook.Hazard(p, [0.6, 1.62, f + 0.08], [0.8, 1.76, f + 0.092]);
        p.Add(MachineLook.Work(L([0.56, GrowHubY - 0.16, f + 0.08], [0.84, GrowHubY + 0.16, f + 0.11], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 20))), "stroke", "-x", 0.12));
        p.Add(MachineLook.Work(L([0.66, GrowHubY - 0.05, f + 0.11], [0.74, GrowHubY + 0.05, f + 0.13], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 8))), "stroke", "-x", 0.12));
        // The face's foot in hazard paint (it never stops for anyone in front of it), and the out band along its top in orange.
        MachineLook.Hazard(p, [-e + post, foot - 0.12, f], [e - post, foot - 0.02, f + 0.014]);
        p.Add(L([-e, top + 0.02, f], [e, top + 0.14, f + 0.014], MachineLook.Out));
        MachineLook.Arrow(p, 2, f + 0.014, 1, 1, 1, -0.75, 1.62, 0.36, 0.22, MachineLook.Out); // (up the face's left: the hub rises)

        // ---- The back: the hopper's port, its rim and arrow, a gauge and the maker's plate ----
        MachineLook.Port(p, [0, 1.3, -e], 2, -1, 0.2, 0.13, true);
        p.Set(("ports", Py.L(Kw(("name", "Chute in"), ("type", "chute"), ("at", Py.L(0, 1.3, -e)), ("face", "-z")))));
        p.Add(L([-0.5, 0.78, -e - 0.014], [0.5, 0.86, -e], MachineLook.In));
        MachineLook.Arrow(p, 2, -e - 0.014, -1, 1, -1, 0, 1.72, 0.18, 0.18, MachineLook.In);
        p.Add(L([0.52, 1.1, -e - 0.036], [0.72, 1.3, -e], "#e8e6df", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))));
        p.Add(L([0.6, 1.18, -e - 0.048], [0.64, 1.27, -e - 0.032], MachineLook.Ink));
        p.Add(L([-0.74, 1.12, -e - 0.024], [-0.48, 1.28, -e], MachineLook.In));
        p.Add(L([-0.71, 1.18, -e - 0.036], [-0.51, 1.22, -e - 0.02], MachineLook.Ink));
        foreach (var x in new[] { -0.5, 0.5 })
            p.Add(L([x - 0.012, foot + 0.04, -sunk - 0.016], [x + 0.012, 0.74, -sunk + 0.004], Trim, paintable: true)); // (cladding seams under the plate)

        // ---- The right: the drive motor, its turning coupling, the oil lines and a cable down to the foot ----
        p.Add(L([sunk - 0.004, 0.9, -0.55], [e + 0.16, 1.5, 0.05], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x"))));
        p.Add(L([e + 0.16, 1.02, -0.43], [e + 0.2, 1.38, -0.07], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "+x"))));
        p.Add(MachineLook.Work(L([e + 0.2, 1.1, -0.35], [e + 0.26, 1.3, -0.15], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+x"))), "spin", "+x"));
        foreach (var (y, colour) in new[] { (1.66, "#b8322a"), (1.74, "#2f62b5") })
            p.Add(L([sunk + 0.006, y - 0.02, -e + post + 0.02], [sunk + 0.046, y + 0.02, e - post - 0.02], colour, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+z"))));
        foreach (var z in new[] { -0.6, 0.0, 0.6 })
            p.Add(L([sunk - 0.004, 1.63, z - 0.03], [sunk + 0.06, 1.77, z + 0.03], MachineLook.Base));
        p.Add(L([sunk - 0.004, foot + 0.02, 0.4], [sunk + 0.05, 0.9, 0.46], MachineLook.Base)); // (the motor's cable to the foot)
        p.Add(L([sunk - 0.004, 0.62, 0.3], [sunk + 0.07, 0.8, 0.56], MachineLook.Base)); // (its junction box)
        return p;
    }

    /// <summary>A Grow Former's settings: its Makes, a wheel's run mode and order size, a prop's kind and size, a wing's. (A new one goes on the end: a saved setting is found by its key.)</summary>
    private static List<object?> GrowConfig()
    {
        var list = Py.L(Kw(("key", "growMakes"), ("type", "choice"), ("label", "Makes"), ("group", "Makes"), ("options", GrowMakesNames()), ("default", 0),
            ("tooltip", "What its own settings and its Queue button make: a wheel, a prop or a wing. An order makes what it asks; the piece on its hub is finished first.")));
        list.AddRange(FormerConfig(32, 20, 0.4, 0.1));
        list.AddRange(FanConfig(64, 0.5));
        list.AddRange(AirframeConfig(192, 32, 0.5, 0.3));
        return list;
    }

    /// <summary>
    /// A Former Block: one 2 x 2 m cell of a Grow Former's bed, a 30 cm slab on the floor (design page drawing 22). Its top is four tread
    /// plate tiles with clamping slots and bolts; its sides are sunk between a foot and a top lip. The hazard kerb round its top edge and the
    /// skirt along its foot are drawn only on the machine's outer edges (MachineBlocks: "seam"), so a bed of blocks reads as one floor with
    /// one kerb. A Run-out Table goes on its far end for a wing longer than the bed.
    /// </summary>
    private static Part FormerBlock()
    {
        var p = new Part("Former Block", Small, Section,
            "Weld it on the floor beside a Grow Former Core, or beside another Former Block: 2 x 2 m more bed. The bed is the largest full rectangle of blocks next to the Core; a block outside it idles. A Run-out Table on its far end lets a wing run on.",
            group: "Production", color: "#22262b"); // (a bed is Base: only the Core wears the Formers' violet)
        const double e = 0.997, h = BedTile, lip = 0.06, sunk = 0.96;
        // The slab: a foot and a top lip whole, the side sunk between them; a hidden box keeps the whole cell solid (and its bounds the cell).
        p.Add(Box([-1, 0, -1], [1, h, 1], hidden: true));
        p.Add(Box([-e, 0, -e], [e, 0.05, e]));
        p.Add(Box([-e, h - lip, -e], [e, h, e]));
        p.Add(Box([-sunk, 0.05, -sunk], [sunk, h - lip, sunk]));
        // The deck: four tread plate tiles, a groove between them.
        foreach (var (x0, x1) in new[] { (-e + 0.004, -0.006), (0.006, e - 0.004) })
            foreach (var (z0, z1) in new[] { (-e + 0.004, -0.006), (0.006, e - 0.004) })
                p.Add(L([x0, h, z0], [x1, h + BedDeck, z1], MachineLook.Steel, "tread plate"));
        // Clamping slots along each tile (a wing's root and a wheel's chocks clamp in them), and a bolt at each tile's corners.
        foreach (var x in new[] { -0.75, -0.25, 0.25, 0.75 })
            foreach (var (z0, z1) in new[] { (-0.9, -0.1), (0.1, 0.9) })
                p.Add(L([x - 0.02, h + BedDeck, z0], [x + 0.02, h + BedDeck + 0.012, z1], MachineLook.Base));
        foreach (var x in new[] { -0.92, -0.08, 0.08, 0.92 })
            foreach (var z in new[] { -0.92, -0.08, 0.08, 0.92 })
                p.Add(L([x - 0.018, h + BedDeck, z - 0.018], [x + 0.018, h + BedDeck + 0.014, z + 0.018], MachineLook.Accent));
        // The kerb: hazard paint along each outer edge of the bed's top (the x sides 4 mm proud of the z sides at the corners, so no two share a plane).
        var top = h + BedDeck;
        p.Add(Seam(MachineLook.D([-e + 0.002, top, e - 0.08], [e - 0.002, top + BedKerb - 0.004, e], MachineLook.Accent, "hazard paint"), "+z"));
        p.Add(Seam(MachineLook.D([-e + 0.002, top, -e], [e - 0.002, top + BedKerb - 0.004, -e + 0.08], MachineLook.Accent, "hazard paint"), "-z"));
        p.Add(Seam(MachineLook.D([e - 0.08, top, -e + 0.002], [e, top + BedKerb, e - 0.002], MachineLook.Accent, "hazard paint"), "+x"));
        p.Add(Seam(MachineLook.D([-e, top, -e + 0.002], [-e + 0.08, top + BedKerb, e - 0.002], MachineLook.Accent, "hazard paint"), "-x"));
        // The sunk sides: a dark skirt panel with two lifting slots, and a bolt row along the lip; only on the bed's outer edges.
        foreach (var (side, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            int u = n == 0 ? 2 : 0;
            double[] Mn(double a0, double y0, double out0) { var v = new double[3]; v[n] = sg > 0 ? sunk - 0.004 : -sunk - out0; v[u] = a0; v[1] = y0; return v; }
            double[] Mx(double a1, double y1, double out0) { var v = new double[3]; v[n] = sg > 0 ? sunk + out0 : -sunk + 0.004; v[u] = a1; v[1] = y1; return v; }
            p.Add(Seam(L(Mn(-0.9, 0.07, 0.014), Mx(0.9, h - lip - 0.02, 0.014), MachineLook.Base), side));
            foreach (var a in new[] { -0.5, 0.5 })
                p.Add(Seam(L(Mn(a - 0.12, 0.12, 0.026), Mx(a + 0.12, 0.18, 0.026), Trim), side));
        }
        p.Sock("+x", [1, h / 2, 0], "+x");
        p.Sock("-x", [-1, h / 2, 0], "-x");
        p.Sock("+z", [0, h / 2, 1], "+z");
        p.Sock("-z", [0, h / 2, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("outBed", [0, 0, 1], "+z", kind: "runOut"); // (a Run-out Table on the bed's far end: a wing's span runs on over it)
        p.Stats(1800, 1500, 8, 8);
        p.Set(("machineBlock", GrowBlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// A Run-out Table: a roller bed 2 m wide and 8 m long under an Extruder's die (the Core's or a Die Block's out bed), or on another table's
    /// far end; lay them side by side under a wide die. Its top is level with the machine's foot, so the machine stands on Foundations and the
    /// tables on the ground in front. The longest piece reaches to the farthest table's end, up to 192 m.
    /// </summary>
    private static Part RunOutTable()
    {
        var p = new Part("Run-out Table", Small, Section,
            $"Fit it under an Extruder's die (its Core or a Die Block), or on another table: its pieces run out to the far end, up to 192 m. Its top is level with the machine's foot.",
            group: "Production", color: Red);
        const double hx = 0.996, top = -0.004, len = TableLength, h = TableDepth;
        p.Add(Box([-hx, -h, 0.004], [hx, top - h * 0.3, len]));
        p.Add(Box([-hx, top - h * 0.3, 0.004], [-hx + h * 0.3, top, len])); // (the bed's two side rails; the rollers between them)
        p.Add(Box([hx - h * 0.3, top - h * 0.3, 0.004], [hx, top, len]));
        MachineLook.Skirt(p, -hx, hx, 0.004, len, -h, -h + h * 0.25, 0.012);
        var r = h * 0.12;
        for (var z = 1.0; z < len; z += 2)
            p.Add(MachineLook.Work(MachineLook.D([-hx + h * 0.3 + 0.004, top - h * 0.3 - r, z - r], [hx - h * 0.3 - 0.004, top - h * 0.3 + r, z + r], MachineLook.Steel, "steel",
                shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))), "spin", "+x")); // (they turn while the Extruder it serves works: drawing 11)
        p.Add(MachineLook.D([-hx - 0.004, top - h * 0.25, 0.1], [-hx, top - h * 0.05, len - 0.1], MachineLook.Out));
        p.Add(MachineLook.D([hx, top - h * 0.25, 0.1], [hx + 0.004, top - h * 0.05, len - 0.1], MachineLook.Out));
        p.Sock("in", [0, 0, 0.004], "-z", kind: "runOut");
        p.Sock("out", [0, 0, len], "+z", kind: "runOut");
        p.Stats(900, 450, 6, 10);
        p.Set(("runOut", len), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>The Extruder Line: a Timer wired to the Extruder's Cut, so a 2 m piece comes off every 10 s.</summary>
    private static PyDict Line() => Kw(
        ("format", "call-of-slop-construct-blueprint/1"),
        ("name", "Extruder Line"),
        ("description", "An Extruder with a bin, a battery and a timer that cuts: tip in material."),
        ("steps", Py.L(
            Kw(("part", "Foundation")),
            Kw(("part", "Extruder"), ("on", 0), ("socket", "top11"), ("by", "bottom")),
            Kw(("part", "Foundation"), ("on", 0), ("socket", "z-1"), ("by", "z+1")), // (behind it: its feed and power, clear of its 2 x 1.5 m)
            Kw(("part", "Materials Bin"), ("on", 2), ("socket", "top01"), ("by", "bottom")),
            Kw(("part", "Battery"), ("on", 2), ("socket", "top02"), ("by", "-y")),
            Kw(("part", "Timer"), ("on", 2), ("socket", "top03"), ("by", "-y"), ("config", Kw(("period", 10), ("duty", 10)))))),
        ("links", Py.L(Kw(("type", "chute"), ("from", 3), ("to", 1)), Kw(("type", "data"), ("from", 5), ("to", 1), ("toPort", "Cut")))));

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)> { ("", ExtruderCore()), ("", DieBlock()), ("", RunOutTable()) };
        parts.Add(("", GrowFormerCore()));
        parts.Add(("", FormerBlock()));
        WriteParts(o, Out, parts);
        o.File(Blueprint, PyJson.Dumps(Line(), 2) + "\n");
    }
}
