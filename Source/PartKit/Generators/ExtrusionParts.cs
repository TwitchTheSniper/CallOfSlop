using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Extrusion parts (Constructs/Parts/Extrusion) and the Extruder Line blueprint (docs/sandbox/CONSTRUCTS.md, "Extrusion" and "The Wheel
/// Former"). Was tools/constructs/extrusion_parts.py.
///
/// An Extruder pushes bulk material through a die; a Wheel Former builds a wheel up on its hub, wider or bigger; a Fan Former a whole prop,
/// an Airframe Former a wing. The Extruder is on the block system (MachineBlocks.cs): one Core, Die Blocks beside it for a bigger die, one
/// Run-out Table; the Formers still come in the four size classes (until the Grow Former). The sizes are part data (storage.extrude, storage.wheel), so a new line of machines is a row here. Run
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

    // Each row as the Python's tuple; numbers keep their Python type (int or float).
    // Wheel: (class, widest, biggest across, first width, first across, m/s, N of push, bin units, W, kg, health, cells x y z, the hub's row of cells)
    private static readonly object?[][] Formers =
    [
        [Small, 1.5, 3, 0.1, 0.4, 0.1, 6000, 400, 900, 520, 700, null, null],
        [Medium, 6, 12, 0.5, 2, 0.2, 24000, 2000, 3600, 18000, 6000, new[] { 5, 14, 4 }, 6],
        [Large, 12, 24, 1, 4, 0.4, 96000, 8000, 14400, 200000, 40000, new[] { 3, 6, 3 }, 3],
        [Capital, 20, 48, 2, 8, 0.8, 384000, 32000, 57600, 1800000, 240000, new[] { 3, 3, 2 }, 1],
    ];

    // Fan: (class, biggest across, first across, m/s across, N of push, bin units, W, kg, health, cells x y z, the hub's row of cells)
    private static readonly object?[][] FanFormers =
    [
        [Small, 3, 0.5, 0.1, 6000, 400, 900, 520, 700, null, null],
        [Medium, 12, 2, 0.2, 24000, 2000, 3600, 18000, 6000, new[] { 5, 14, 4 }, 6],
        [Large, 48, 4, 0.8, 96000, 8000, 14400, 240000, 48000, new[] { 3, 8, 3 }, 6],
        [Capital, 64, 8, 1.6, 384000, 32000, 57600, 2400000, 300000, new[] { 3, 5, 2 }, 3],
    ];

    // Airframe: (class, longest span, widest chord, first span, first chord, m/s of span, N of push, bin units, W, kg, health, cells x y z, the hub's row of cells)
    private static readonly object?[][] AirframeFormers =
    [
        [Small, 6, 3, 0.5, 0.3, 0.25, 6000, 400, 900, 520, 700, null, null],
        [Medium, 24, 8, 2, 1, 0.5, 24000, 2000, 3600, 18000, 6000, new[] { 5, 14, 4 }, 6],
        [Large, 96, 24, 4, 2, 1, 96000, 8000, 14400, 240000, 48000, new[] { 3, 8, 3 }, 6],
        [Capital, 192, 32, 8, 4, 2, 384000, 32000, 57600, 2400000, 300000, new[] { 3, 5, 2 }, 3],
    ];

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
        const double front = Face - 0.2, back = BlockBack + 0.2; // (the platens' inner faces: the container runs between them)
        // The body: the container between the platens, its four long edges notched for the tie rods; the platens full square.
        p.Add(Box([-e, n, back], [e, 2 - n, front]));
        p.Add(Box([-e + n, 0.003, back], [e - n, 1.997, front]));
        p.Add(Box([-e, 0.003, front], [e, 1.997, Face])); // (the front platen: its face is the die face, where the out bed socket sits)
        p.Add(Box([-e, 0.003, BlockBack + 0.003], [e, 1.997, back]));
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
        // The back platen's frame, the same way: a dark frame round the back face.
        p.Add(Seam(L([0.94, 0, BlockBack - 0.02], [1, 2, BlockBack + 0.2], MachineLook.Base), "+x"));
        p.Add(Seam(L([-1, 0, BlockBack - 0.02], [-0.94, 2, BlockBack + 0.2], MachineLook.Base), "-x"));
        p.Add(Seam(L([-0.94, 1.94, BlockBack - 0.02], [0.94, 2, BlockBack + 0.2], MachineLook.Base), "+y"));
        p.Add(Seam(L([-0.94, 0, BlockBack - 0.02], [0.94, 0.06, BlockBack + 0.2], MachineLook.Base), "-y"));
        // The tie rods: one in each corner's notch, platen to platen, a hex nut proud of each platen; only at the machine's corners.
        // Where a block is welded on either side, the notch is filled in the body's own tone instead, so no groove runs between blocks.
        foreach (var (sx, sy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            var cx = sx * (1 - n / 2);
            var cy = sy > 0 ? 2 - n / 2 : n / 2;
            string[] sides = [sx > 0 ? "+x" : "-x", sy > 0 ? "+y" : "-y"];
            var r = 0.032;
            p.Add(Seam(L([cx - r, cy - r, back - 0.03], [cx + r, cy + r, front + 0.03], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+z"))), sides));
            var h = 0.042;
            p.Add(Seam(L([cx - h, cy - h, Face + 0.016], [cx + h, cy + h, Face + 0.062], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "+z"))), sides));
            p.Add(Seam(L([cx - h, cy - h, BlockBack - 0.062], [cx + h, cy + h, BlockBack - 0.016], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-z"))), sides));
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
            p.Add(Seam(L([coil[0], 1.5, -1.4], [coil[1], 1.68, -1.22], MachineLook.Base), side)); // (its terminal box)
        }
        p.Add(Seam(L([-0.85, 1.993, -2.15], [0.85, 2.009, -1.45], MachineLook.Base), "+y"));
        foreach (var z in new[] { -2.05, -1.85, -1.65 })
            p.Add(Seam(MachineLook.Work(L([-0.8, 2.005, z], [0.8, 2.017, z + 0.1], MachineLook.Hot), "glow")!, "+y"));
        // The oil lines down the machine's right side, red feed and blue return, clipped to the body every metre; a junction block at the back.
        foreach (var (y, colour) in new[] { (1.76, "#b8322a"), (1.83, "#2f62b5") })
            p.Add(Seam(L([0.975, y - 0.02, back + 0.05], [1.015, y + 0.02, front - 0.05], colour, shape: Kw(("shape", "cylinder"), ("segments", 8), ("front", "+z"))), "+x"));
        foreach (var z in new[] { -2.6, -0.9, 0.2 })
            p.Add(Seam(L([0.993, 1.725, z - 0.03], [1.017, 1.865, z + 0.03], MachineLook.Base), "+x"));
        p.Add(Seam(L([0.993, 1.71, back + 0.004], [1.017, 1.88, back + 0.2], MachineLook.Base), "+x"));
        // The shear along the machine's top edge: a guillotine bar over the die plate, down and up after each cut.
        p.Add(Seam(MachineLook.Work(L([-0.93, 1.86, Face + 0.022], [0.93, 1.935, Face + 0.046], MachineLook.Steel, "steel"), "shear", "-y", 0.3)!, "+y"));
        p.Add(Seam(MachineLook.Work(L([-0.93, 1.835, Face + 0.026], [0.93, 1.86, Face + 0.044], MachineLook.Accent, "hazard paint"), "shear", "-y", 0.3)!, "+y"));
        // The skirt at its foot, a seam.
        p.Add(Seam(L([-0.94, 0, back + 0.25], [0.94, 0.14, front - 0.1], MachineLook.Base), "-y"));
        // The back: the ram's housing and its rod, which slides in as the piece grows and comes back for the next billet each metre;
        // two hoses from the housing to the platen, a pressure gauge, grease points and a maker's plate.
        p.Add(L([-0.45, 0.55, BlockBack - 0.7], [0.45, 1.45, BlockBack + 0.006], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "-z"))));
        p.Add(L([-0.5, 0.5, BlockBack - 0.73], [0.5, 1.5, BlockBack - 0.69], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 16), ("front", "-z"))));
        p.Add(MachineLook.Work(L([-0.18, 0.82, BlockBack - 1.0], [0.18, 1.18, BlockBack - 0.66], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z"))), "ram", "+z", 0.5));
        p.Add(MachineLook.Work(L([-0.24, 0.76, BlockBack - 1.06], [0.24, 1.24, BlockBack - 0.98], MachineLook.Base), "ram", "+z", 0.5)); // (the rod's end plate)
        foreach (var (x, colour) in new[] { (-0.32, "#b8322a"), (0.32, "#2f62b5") })
            p.Add(L([x - 0.025, 1.33, BlockBack - 0.6], [x + 0.025, 1.38, BlockBack + 0.006], colour)); // (a hose from the housing into the platen)
        p.Add(L([0.62, 1.48, BlockBack - 0.036], [0.78, 1.64, BlockBack + 0.006], "#e8e6df", shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "-z")))); // (the gauge's face)
        p.Add(L([0.68, 1.54, BlockBack - 0.048], [0.72, 1.62, BlockBack - 0.032], MachineLook.Ink)); // (its needle)
        p.Add(L([-0.82, 1.5, BlockBack - 0.024], [-0.56, 1.66, BlockBack + 0.006], MachineLook.In)); // (the maker's plate)
        p.Add(L([-0.79, 1.56, BlockBack - 0.036], [-0.59, 1.6, BlockBack - 0.02], MachineLook.Ink));
        foreach (var (x, y) in new[] { (-0.7, 0.3), (0.7, 0.3) })
            p.Add(L([x - 0.02, y - 0.02, BlockBack - 0.03], [x + 0.02, y + 0.02, BlockBack + 0.006], MachineLook.Accent)); // (grease points)
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

    private static PyDict Seam(PyDict? box, string[] faces) => box!.Update(("seam", Py.L(faces[0], faces[1])));

    private sealed record Head(double X0, double X1, double Y, double Y1, double Z0, double Z, double Z1, double T);

    /// <summary>A big machine's block: origin the centre of cell 0, 0, 0, a hopper let into the back of its top.</summary>
    private static Head MakeHead(Part p, int[] cells, int hopperCells = 1)
    {
        var c = p.Cls;
        int nx = cells[0], ny = cells[1], nz = cells[2];
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        var t = c.Cell * 0.1;
        var z = z0 + hopperCells * c.Cell;
        var y = y1 - c.Cell * 0.5;
        p.Add(Box([x0, y0, z0], [x1, y, z1]));
        p.Add(Box([x0, y, z], [x1, y1, z1]));
        p.Add(Box([x0, y, z0], [x0 + t, y1, z], Trim));
        p.Add(Box([x1 - t, y, z0], [x1, y1, z], Trim));
        p.Add(Box([x0 + t, y, z0], [x1 - t, y1, z0 + t], Trim));
        // The shared look (look.md, "Machine look"): a dark skirt and side panels, the hopper's rim banded white (in), the die's face banded orange (out) over hazard paint.
        var m = Math.Min(t * 0.2, 0.03); // (how far a marking stands off its face)
        p.Add(MachineLook.D([x0 - m, y0 + t * 3, z0 + t], [x0, y - t, z1 - t], MachineLook.Base));
        p.Add(MachineLook.D([x1, y0 + t * 3, z0 + t], [x1 + m, y - t, z1 - t], MachineLook.Base));
        MachineLook.Skirt(p, x0, x1, z0, z1, y0, y0 + t * 2, m * 1.5);
        p.Add(MachineLook.D([x0 - m, y1 - t * 1.6, z0 - m], [x1 + m, y1 - t * 0.4, z0], MachineLook.In));
        p.Add(MachineLook.D([x0 - m, y1 - t * 1.6, z0], [x0, y1 - t * 0.4, z - t * 0.2], MachineLook.In));
        p.Add(MachineLook.D([x1, y1 - t * 1.6, z0], [x1 + m, y1 - t * 0.4, z - t * 0.2], MachineLook.In));
        MachineLook.Arrow(p, 2, z0 - m, -1, 1, -1, (x0 + x1) / 2, y1 - t * 3.6, t * 3, t * 3, MachineLook.In, m);
        p.Add(MachineLook.D([x0, y1 - t * 1.6, z1], [x1, y1 - t * 0.4, z1 + m], MachineLook.Out));
        MachineLook.Hazard(p, [x0, y0 + t * 0.2, z1], [x1, y0 + t * 1.6, z1 + m]);
        return new Head(x0, x1, y, y1, z0, z, z1, t);
    }

    private static PyDict BinOf(int units, Head hd)
    {
        var m = 0.05 * hd.T + hd.T;
        return Kw(("material", "any"), ("capacity", units),
            ("min", Py.L(Py.Round(hd.X0 + m, 3), Py.Round(hd.Y + 0.05 * hd.T, 3), Py.Round(hd.Z0 + m, 3))),
            ("max", Py.L(Py.Round(hd.X1 - m, 3), Py.Round(hd.Y1 - 0.05 * hd.T, 3), Py.Round(hd.Z - 0.05 * hd.T, 3))));
    }

    private static Dictionary<string, (int, int)> Lim5() => new[] { "+x", "-x", "+y", "-y", "-z" }.ToDictionary(d => d, _ => (3, 3));

    private static List<object?> FanConfig(object across, object start) => Py.L(
        Kw(("key", "fanKind"), ("type", "choice"), ("label", "Makes"), ("group", "Prop"), ("options", FanKinds()), ("default", 0),
            ("tooltip", "What it makes. A prop runs on a shaft, an electric one or a ducted fan on its own motor, a helicopter rotor takes collective and cyclic.")),
        Kw(("key", "fanBlades"), ("label", "Blades"), ("group", "Prop"), ("min", 2), ("max", 16), ("step", 1), ("default", 3),
            ("tooltip", "How many blades, 2 to 16. Each has its own health: more take a little more power, push a little harder, and are steadier when one goes.")),
        Kw(("key", "bladeStyle"), ("type", "choice"), ("label", "Blade style"), ("group", "Prop"), ("options", BladeStyles()), ("default", 0),
            ("tooltip", "The blade's shape: a small trade of push, power and top speed. The prop keeps it.")),
        Kw(("key", "fanDiameter"), ("label", "Across"), ("group", "Prop"), ("unit", "m"), ("min", start), ("max", across), ("step", 0.05), ("default", start),
            ("tooltip", "How far across. It parts off by itself at this size. Power and speed follow from the size.")));

    private static List<object?> AirframeConfig(object span, object chord, object s0, object c0) => Py.L(
        Kw(("key", "wingKind"), ("type", "choice"), ("label", "Cuts"), ("group", "Airframe"), ("options", Airframes()), ("default", 0),
            ("tooltip", "What it cuts. Its bin's material is the skin: cloth, wood, planks, laminate, metal or steel. Cloth is light and tears.")),
        Kw(("key", "wingChord"), ("label", "Chord"), ("group", "Airframe"), ("unit", "m"), ("min", c0), ("max", chord), ("step", 0.05), ("default", OMin(chord, OMax(c0, 1.5))),
            ("tooltip", "From leading edge to trailing edge. It stays what it was when the piece started.")),
        Kw(("key", "wingSpan"), ("label", "Span"), ("group", "Airframe"), ("unit", "m"), ("min", s0), ("max", span), ("step", 0.05), ("default", OMin(span, OMax(s0, 2))),
            ("tooltip", "How long. It parts off by itself at this length. Weld pieces along their span for a longer wing.")));

    private static Part Former(object?[] row, bool fan = false, bool air = false)
    {
        Cls c;
        object across, width, d0, w0, speed;
        int push, units, power, mass, health;
        int[]? cells;
        int hubRow;
        string desc;
        int I(object? v) => (int)v!;
        if (air)
        {
            (c, across, width, d0, w0, speed) = ((Cls)row[0]!, row[1]!, row[2]!, row[3]!, row[4]!, row[5]!);
            (push, units, power, mass, health, cells, hubRow) = (I(row[6]), I(row[7]), I(row[8]), I(row[9]), I(row[10]), (int[]?)row[11], row[12] is int h ? h : 0);
            desc = $"Cuts a wing or a control surface from its settings, up to {Py.G(Py.D(across))} m of span and {Py.G(Py.D(width))} m of chord, of what is in its bin. It parts it off when it is done. Needs power.";
        }
        else if (fan)
        {
            (c, across, d0, speed) = ((Cls)row[0]!, row[1]!, row[2]!, row[3]!);
            (push, units, power, mass, health, cells, hubRow) = (I(row[4]), I(row[5]), I(row[6]), I(row[7]), I(row[8]), (int[]?)row[9], row[10] is int h ? h : 0);
            (width, w0) = (0.05, 0.05);
            desc = $"Makes a whole prop on its hub from its settings, up to {Py.G(Py.D(across))} m across: a propeller, a rotor or a ducted fan. It parts it off when it is done. Needs power.";
        }
        else
        {
            (c, width, across, w0, d0, speed) = ((Cls)row[0]!, row[1]!, row[2]!, row[3]!, row[4]!, row[5]!);
            (push, units, power, mass, health, cells, hubRow) = (I(row[6]), I(row[7]), I(row[8]), I(row[9]), I(row[10]), (int[]?)row[11], row[12] is int h ? h : 0);
            desc = $"Builds a wheel up on its hub: wider, or bigger across, up to {Py.G(Py.D(width))} m wide and {Py.G(Py.D(across))} m across. A Cut signal parts it off. Needs power.";
        }
        var p = new Part(NameOf(c, air ? "Airframe Former" : fan ? "Fan Former" : "Wheel Former"), c, Section, desc, role: "storage", group: "Production", color: Violet);
        List<object?> hub;
        PyDict bulk;
        if (c == Small)
        {
            p.Add(Box([-0.6, 0, -0.75], [0.6, 2.05, 0.75]));
            p.Add(Box([-0.6, 2.05, -0.05], [0.6, 2.2, 0.75]));
            p.Add(Box([-0.6, 2.05, -0.75], [-0.56, 2.2, -0.05], Trim));
            p.Add(Box([0.56, 2.05, -0.75], [0.6, 2.2, -0.05], Trim));
            p.Add(Box([-0.56, 2.05, -0.75], [0.56, 2.2, -0.71], Trim));
            hub = Py.L(0, 1.6, 0.75);
            bulk = Kw(("material", "any"), ("capacity", units), ("min", Py.L(-0.555, 2.055, -0.705)), ("max", Py.L(0.555, 2.195, -0.055)));
            SmallFormerLook(p, air || fan);
            p.Sock("bottom", [0, 0, 0], "-y");
            p.Sock("+x", [0.6, 0.75, 0], "+x");
            p.Sock("-x", [-0.6, 0.75, 0], "-x");
            p.Sock("top", [0, 2.2, 0.35], "+y");
            p.Sock("back", [0, 0.75, -0.75], "-z");
            p.Sock("die", [0, 1.6, 0.75], "+z", kind: "die");
            p.Stats(mass, health, 6, 4);
        }
        else
        {
            int nx = cells![0], ny = cells[1], nz = cells[2];
            var hd = MakeHead(p, cells);
            double hx = (nx - 1) / 2.0 * c.Cell, hy = hubRow * c.Cell;
            hub = Py.L(hx, hy, hd.Z1);
            bulk = BinOf(units, hd);
            var r = c.Cell * 0.4;
            var hm = Math.Min(hd.T * 0.2, 0.05);
            p.Add(MachineLook.D([hx - r * 1.6, hy - r * 1.6, hd.Z1 - hd.T * 0.1], [hx + r * 1.6, hy + r * 1.6, hd.Z1 + hm], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 20))));
            p.Add(MachineLook.Work(MachineLook.D([hx - r, hy - r * 0.16, hd.Z1 + hm * 0.5], [hx + r, hy + r * 0.16, hd.Z1 + hm * 1.6], MachineLook.Accent), "spin", "+z"));
            p.Add(MachineLook.Work(MachineLook.D([hx - r * 0.16, hy - r, hd.Z1 + hm * 0.5], [hx + r * 0.16, hy + r, hd.Z1 + hm * 1.7], MachineLook.Accent), "spin", "+z"));
            p.Add(MachineLook.D([hx - r * 0.3, hy - r * 0.3, hd.Z1 + hm * 0.4], [hx + r * 0.3, hy + r * 0.3, hd.Z1 + hm * 2], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12))));
            BlockSockets(p, nx, ny, nz, limit: Lim5(), skip: ["+z"]);
            p.Sock("die", [hx, hy, hd.Z1], "+z", kind: "die");
            p.Stats(mass, health, c.Armour);
        }
        var wheel = Kw(("speed", speed), ("at", hub), ("maxWidth", width), ("maxDiameter", across), ("startWidth", w0), ("startDiameter", d0), ("pushForce", push));
        if (fan)
            wheel["fan"] = true;
        if (air)
            wheel["airframe"] = true;
        p.Set(("power", power), ("storage", Kw(("bulk", bulk), ("wheel", wheel))),
            ("config", air ? AirframeConfig(across, width, d0, w0) : fan ? FanConfig(across, d0) : FormerConfig(across, width, d0, w0)),
            ("collider", "boxes")); // (the hub is a look: its cylinder must never become a hull)
        return WithScreen(p, orders: true, recipe: fan || air, width: c == Small ? 0.97 : null); // (every former takes work orders; a Fan or Airframe Former keeps its Recipe button among them. A small one's screen keeps its 0.97 m: it was sized from bounds a misplaced arrow had widened, and shrank when the arrow was put right)
    }

    /// <summary>The small Formers' look: a headstock on a dark column, a big bare hub with a chuck that turns, the hopper banded white, hazard paint round the hub.</summary>
    private static void SmallFormerLook(Part p, bool plain)
    {
        MachineLook.Skirt(p, -0.6, 0.6, -0.75, 0.75, 0, 0.16, 0.012);
        // Dark columns up the front corners and a dark apron under the hub: the body colour is left as a headstock round it.
        p.Add(L([-0.612, 0.16, 0.5], [-0.42, 2.04, 0.762], MachineLook.Base));
        p.Add(L([0.42, 0.16, 0.5], [0.612, 2.04, 0.762], MachineLook.Base));
        p.Add(L([-0.42, 0.16, 0.75], [0.42, 0.95, 0.763], MachineLook.Base));
        MachineLook.Hazard(p, [-0.42, 0.95, 0.75], [0.42, 1.07, 0.764]);
        p.Add(L([-0.42, 2.07, 0.75], [0.42, 2.19, 0.764], MachineLook.Out));
        // The hub: a bare steel face plate, a boss, and a yellow chuck that turns while it runs.
        p.Add(L([-0.4, 1.2, 0.74], [0.4, 2.0, 0.768], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 20))));
        p.Add(MachineLook.Work(L([-0.3, 1.56, 0.76], [0.3, 1.64, 0.782], MachineLook.Accent), "spin", "+z"));
        p.Add(MachineLook.Work(L([-0.04, 1.3, 0.76], [0.04, 1.9, 0.784], MachineLook.Accent), "spin", "+z"));
        p.Add(L([-0.1, 1.5, 0.75], [0.1, 1.7, 0.79], MachineLook.Base, shape: Kw(("shape", "cylinder"), ("segments", 12))));
        // The hopper's rim, white: the in side.
        p.Add(L([-0.612, 2.08, -0.762], [0.612, 2.19, -0.75], MachineLook.In));
        p.Add(L([0.6, 2.08, -0.75], [0.612, 2.19, -0.05], MachineLook.In));
        p.Add(L([-0.612, 2.08, -0.75], [-0.6, 2.19, -0.05], MachineLook.In));
        MachineLook.Arrow(p, 2, -0.75, -1, 1, -1, 1.8, 0, 0.3, 0.3, MachineLook.In); // (down into the hopper, under its rim: y 1.8, x 0. The two were swapped until 2026-11-14, so the arrow sat half in the floor at x 1.8, its top on the floor's plane)
        // The drive on the right: a dark motor and a guard.
        p.Add(L([0.6, 1.3, -0.6], [0.72, 1.9, -0.1], MachineLook.Base));
        p.Add(L([0.6, 0.2, -0.36], [0.63, 1.3, -0.3], MachineLook.Base));
        if (plain)
            return;
        p.Add(MachineLook.Work(L([0.72, 1.44, -0.51], [0.76, 1.76, -0.19], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))), "spin", "+x"));
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
        parts.AddRange(Formers.Select(r => ("", Former(r))));
        parts.AddRange(FanFormers.Select(r => ("", Former(r, fan: true))));
        parts.AddRange(AirframeFormers.Select(r => ("", Former(r, air: true))));
        WriteParts(o, Out, parts);
        o.File(Blueprint, PyJson.Dumps(Line(), 2) + "\n");
    }
}
