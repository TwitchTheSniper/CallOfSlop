using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Extrusion parts (Constructs/Parts/Extrusion) and the Extruder Line blueprint (docs/sandbox/CONSTRUCTS.md, "Extrusion" and "The Wheel
/// Former"). Was tools/constructs/extrusion_parts.py.
///
/// An Extruder pushes bulk material through a die; a Wheel Former builds a wheel up on its hub, wider or bigger; a Fan Former a whole prop,
/// an Airframe Former a wing. Each comes in the four size classes: the small Extruder widens its die with Die Frames, the bigger ones are
/// their die. The sizes are part data (storage.extrude, storage.wheel), so a new line of machines is a row here. Run
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

    // (class, die across, longest piece, m/s, N of push, bin units, W, kg, health, cells x y z)
    private static readonly (Cls, int, int, double, int, int, int, int, int, int[])[] Extruders =
    [
        (Medium, 8, 64, 0.4, 24000, 2000, 3600, 16000, 6000, [9, 9, 6]),
        (Large, 16, 128, 0.8, 96000, 8000, 14400, 180000, 40000, [5, 5, 3]),
        (Capital, 32, 192, 1.6, 384000, 32000, 57600, 1600000, 240000, [3, 3, 2]),
    ];

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
        [Capital, 96, 8, 1.6, 384000, 32000, 57600, 2400000, 300000, new[] { 3, 5, 2 }, 3],
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
    private static Part WithScreen(Part p, bool orders = false, string[]? faces = null, double? width = null)
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
            panel["touch"] = OrderButtons();
        var config = p.D.Get("config") is List<object?> cfg ? new List<object?>(cfg) : [];
        if (orders)
            config.Add(OrderCount);
        config.Add(Ink);
        p.Set(("panels", Py.L(panel)), ("config", config));
        return p;
    }

    /// <summary>An Extruder's settings. (A new one goes on the end: a saved setting is found by its key.)</summary>
    private static List<object?> ExtruderConfig(double die, object section, object length)
    {
        var frames = die < Py.D(section) ? ": weld on Die Frames." : ".";
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

    private static Part ExtruderSmall()
    {
        var p = new Part("Extruder", Small, Section, "Pushes bulk material through a die as one long piece. Cut it with a signal or by length. Needs power.", role: "storage", group: "Production", color: Red);
        p.Add(Box([-0.5, 0, -0.75], [0.5, 0.85, 0.75]));
        p.Add(Box([-0.5, 0.85, -0.05], [0.5, 1, 0.75])!.Update(("finish", "tread plate")));
        p.Add(Box([-0.5, 0.85, -0.75], [-0.46, 1, -0.05], Trim));
        p.Add(Box([0.46, 0.85, -0.75], [0.5, 1, -0.05], Trim));
        p.Add(Box([-0.46, 0.85, -0.75], [0.46, 1, -0.71], Trim));
        // The control board (look.md, "Machine look"): it stands on the hopper's left wall, on the aisle side, so the screen is at a standing eye.
        p.Add(MachineLook.D([-0.56, 0.2, -0.7], [-0.5, 1.0, -0.62], MachineLook.Base));
        p.Add(MachineLook.D([-0.56, 0.2, 0.12], [-0.5, 1.0, 0.2], MachineLook.Base));
        p.Add(MachineLook.D([-0.57, 1.0, -0.74], [-0.5, 1.86, 0.24], MachineLook.Base));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [0.5, 0.5, 0.35], "+x");
        p.Sock("-x", [-0.5, 0.5, 0.35], "-x");
        p.Sock("top", [0, 1, 0.35], "+y");
        p.Sock("back", [0, 0.5, -0.75], "-z");
        p.Sock("die", [0, 0.5, 0.75], "+z", kind: "die");
        p.Stats(380, 600, 6, 4);
        p.Set(("power", 900), ("storage", Kw(
                ("bulk", Kw(("material", "any"), ("capacity", 400), ("min", Py.L(-0.455, 0.855, -0.705)), ("max", Py.L(0.455, 0.995, -0.055)))),
                ("extrude", Kw(("speed", 0.2), ("die", Py.L(0.5, 0.5)), ("at", Py.L(0, 0.5, 0.75)), ("maxSection", Py.L(6, 6)), ("maxLength", 48), ("pushForce", 6000))))),
            ("config", ExtruderConfig(0.5, 6, 48)));
        WithScreen(p, orders: true, faces: ["-x"], width: 0.84);
        MachineLook.Skirt(p, -0.5, 0.5, -0.75, 0.75, 0, 0.14, 0.012);
        // The hopper, at the back: taller than the body, a white rim (the in side) and an arrow down each open side.
        p.Add(L([0.455, 1.0, -0.755], [0.505, 1.24, -0.045], MachineLook.Base));
        p.Add(L([-0.505, 1.0, -0.755], [0.455, 1.24, -0.705], MachineLook.Base));
        p.Add(L([-0.5, 1.0, -0.095], [0.455, 1.24, -0.045], MachineLook.Base));
        p.Add(L([0.45, 1.24, -0.76], [0.51, 1.3, -0.04], MachineLook.In));
        p.Add(L([-0.51, 1.24, -0.76], [0.45, 1.3, -0.7], MachineLook.In));
        p.Add(L([-0.5, 1.24, -0.1], [0.45, 1.3, -0.04], MachineLook.In));
        MachineLook.Arrow(p, 0, 0.505, 1, 1, -1, 1.1, -0.4, 0.2, 0.2, MachineLook.In);
        MachineLook.Arrow(p, 2, -0.755, -1, 1, -1, 1.1, 0, 0.2, 0.2, MachineLook.In);
        // The die head, at the front: a bare steel collar round the die, the die plate dull until it works, hazard paint under the mouth,
        // an orange band and arrow over it (the out side). Thin: Die Frames and the piece stand against this face.
        p.Add(L([-0.31, 0.19, 0.75], [-0.25, 0.81, 0.766], MachineLook.Steel, "steel"));
        p.Add(L([0.25, 0.19, 0.75], [0.31, 0.81, 0.766], MachineLook.Steel, "steel"));
        p.Add(L([-0.25, 0.75, 0.75], [0.25, 0.81, 0.766], MachineLook.Steel, "steel"));
        p.Add(L([-0.25, 0.19, 0.75], [0.25, 0.25, 0.766], MachineLook.Steel, "steel"));
        p.Add(MachineLook.Work(L([-0.25, 0.25, 0.74], [0.25, 0.75, 0.758], MachineLook.Hot), "glow"));
        MachineLook.Hazard(p, [-0.5, 0.02, 0.75], [0.5, 0.15, 0.764]);
        p.Add(L([-0.5, 0.86, 0.75], [0.5, 0.99, 0.764], MachineLook.Out));
        p.Add(L([-0.36, 1.0, 0.57], [0.36, 1.09, 0.756], MachineLook.Steel, "steel")); // (the head clamp over the die: the front end reads as a head from the side)
        p.Add(L([-0.3, 1.09, 0.61], [-0.22, 1.13, 0.69], MachineLook.Base));
        p.Add(L([0.22, 1.09, 0.61], [0.3, 1.13, 0.69], MachineLook.Base));
        MachineLook.Arrow(p, 1, 1.0, 1, 2, 1, 0, 0.45, 0.4, 0.3, MachineLook.Out);
        // The drive, on the right of the hopper end: a dark gearbox, a guard, and a pulley that turns while it runs.
        p.Add(L([0.5, 0.2, -0.68], [0.6, 0.62, -0.2], MachineLook.Base));
        p.Add(L([0.5, 0.62, -0.6], [0.57, 0.8, -0.28], MachineLook.Base));
        p.Add(MachineLook.Work(L([0.6, 0.25, -0.6], [0.64, 0.57, -0.28], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))), "spin", "+x"));
        p.Add(MachineLook.Work(L([0.64, 0.38, -0.56], [0.655, 0.44, -0.32], MachineLook.Ink), "spin", "+x"));
        // Cladding seams on the body's sides (the body's own tone, so they take its paint).
        foreach (var z in new[] { -0.05, 0.38 })
            p.Add(L([0.5, 0.16, z - 0.012], [0.514, 0.84, z + 0.012], Trim, paintable: true));
        p.Add(L([-0.514, 0.16, 0.36], [-0.5, 0.84, 0.384], Trim, paintable: true));
        return p;
    }

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

    private static Part Extruder((Cls, int, int, double, int, int, int, int, int, int[]) row)
    {
        var (c, die, length, speed, push, units, power, mass, health, cells) = row;
        var p = new Part(NameOf(c, "Extruder"), c, Section, $"Pushes bulk material through a {die} x {die} m die as one long piece, up to {length} m. Cut it with a signal or by length. Needs power.",
            role: "storage", group: "Production", color: BodyC);
        int nx = cells[0], ny = cells[1], nz = cells[2];
        var hd = MakeHead(p, cells);
        double cx = (nx - 1) / 2.0 * c.Cell, cy = (ny - 1) / 2.0 * c.Cell;
        var (dh, cw, cm) = (die / 2.0, hd.T * 0.6, Math.Min(hd.T * 0.16, 0.04));
        p.Add(MachineLook.Work(MachineLook.D([cx - dh, cy - dh, hd.Z1 - hd.T * 0.1], [cx + dh, cy + dh, hd.Z1 + cm * 0.6], MachineLook.Hot), "glow"));
        p.Add(MachineLook.D([cx - dh - cw, cy - dh - cw, hd.Z1], [cx - dh, cy + dh + cw, hd.Z1 + cm], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([cx + dh, cy - dh - cw, hd.Z1], [cx + dh + cw, cy + dh + cw, hd.Z1 + cm], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([cx - dh, cy + dh, hd.Z1], [cx + dh, cy + dh + cw, hd.Z1 + cm], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([cx - dh, cy - dh - cw, hd.Z1], [cx + dh, cy - dh, hd.Z1 + cm], MachineLook.Steel, "steel"));
        BlockSockets(p, nx, ny, nz, limit: Lim5(), skip: ["+z"]);
        p.Sock("die", [cx, cy, hd.Z1], "+z", kind: "die");
        p.Stats(mass, health, c.Armour);
        p.Set(("power", power), ("storage", Kw(
                ("bulk", BinOf(units, hd)),
                ("extrude", Kw(("speed", speed), ("die", Py.L(die, die)), ("at", Py.L(cx, cy, hd.Z1)), ("maxSection", Py.L(die, die)), ("maxLength", length), ("pushForce", push))))),
            ("config", ExtruderConfig(die, die, length)));
        return WithScreen(p, orders: true);
    }

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
        return WithScreen(p, orders: !fan && !air);
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
        MachineLook.Arrow(p, 2, -0.75, -1, 1, -1, 0, 1.8, 0.3, 0.3, MachineLook.In);
        // The drive on the right: a dark motor and a guard.
        p.Add(L([0.6, 1.3, -0.6], [0.72, 1.9, -0.1], MachineLook.Base));
        p.Add(L([0.6, 0.2, -0.36], [0.63, 1.3, -0.3], MachineLook.Base));
        if (plain)
            return;
        p.Add(MachineLook.Work(L([0.72, 1.44, -0.51], [0.76, 1.76, -0.19], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"))), "spin", "+x"));
    }

    private static Part DieFrame(string name, double halfX, double halfY, string desc, int mass, int health, int armour, int seconds, double gold)
    {
        var p = new Part(name, Small, Section, desc, group: "Production", color: "#4a535c");
        p.Add(Box([-halfX, -halfY, -0.4], [halfX, halfY, 0.4]));
        p.Add(Box([-halfX - 0.01, -gold, -0.2], [halfX + 0.01, gold, 0.2], Gold, detail: true));
        p.Sock("-x", [-halfX, 0, 0], "-x");
        p.Sock("+x", [halfX, 0, 0], "+x");
        p.Sock("-y", [0, -halfY, 0], "-y");
        p.Sock("+y", [0, halfY, 0], "+y");
        p.Stats(mass, health, armour, seconds);
        p.Set(("dieFrame", true), ("centreSockets", false));
        return p;
    }

    /// <summary>The Extruder Line: a Timer wired to the Extruder's Cut, so a 2 m piece comes off every 10 s.</summary>
    private static PyDict Line() => Kw(
        ("format", "call-of-slop-construct-blueprint/1"),
        ("name", "Extruder Line"),
        ("description", "An Extruder with a bin, a battery and a timer that cuts: tip in material."),
        ("steps", Py.L(
            Kw(("part", "Foundation")),
            Kw(("part", "Extruder"), ("on", 0), ("socket", "top21"), ("by", "bottom")),
            Kw(("part", "Materials Bin"), ("on", 0), ("socket", "top01"), ("by", "bottom")),
            Kw(("part", "Battery"), ("on", 0), ("socket", "top02"), ("by", "-y")),
            Kw(("part", "Timer"), ("on", 0), ("socket", "top03"), ("by", "-y"), ("config", Kw(("period", 10), ("duty", 10)))))),
        ("links", Py.L(Kw(("type", "chute"), ("from", 2), ("to", 1)), Kw(("type", "data"), ("from", 4), ("to", 1), ("toPort", "Cut")))));

    public void Make(PartOutput o)
    {
        var parts = new List<(string, Part)> { ("", ExtruderSmall()) };
        parts.AddRange(Extruders.Select(r => ("", Extruder(r))));
        parts.AddRange(Formers.Select(r => ("", Former(r))));
        parts.AddRange(FanFormers.Select(r => ("", Former(r, fan: true))));
        parts.AddRange(AirframeFormers.Select(r => ("", Former(r, air: true))));
        parts.Add(("", DieFrame("Die Frame", 0.25, 0.5, "Attach it to an Extruder to widen the die, up to 6 x 6 m.", 90, 300, 6, 3, 0.3)));
        parts.Add(("", DieFrame("Large Die Frame", 0.5, 1, "Attach it to an Extruder: 1 x 2 m more die.", 280, 700, 8, 5, 0.6)));
        WriteParts(o, Out, parts);
        o.File(Blueprint, PyJson.Dumps(Line(), 2) + "\n");
    }
}
