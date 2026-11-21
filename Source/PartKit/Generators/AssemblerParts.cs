using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Assembler on the block system (design doc "Assembler on the block system", Sean 2026-11-21; MachineBlocks.cs family
/// "assembler", ConstructAssembling.cs): Constructs/Parts/Assembler. One Assembler Core (the "Assembler": hopper, tray, screen, its own
/// arm on its front deck, six upgrade slots on its back), the 2 x 2 m Assembly Bed Block welded beside it (the bed is their largest full
/// rectangle: how big a part it builds and how many side by side), the Gantry Carriage (one more arm, welded on a bed block's rail edge:
/// one more part at once) and the two upgrade modules that are no tier piece, the Overdrive Unit (speed) and the Feed Buffer (capacity).
/// The Power Coupling, Coolant Loop, Servo Rack and Plasma Conditioner (MachineTierParts) are its other four modules. The shared Mast
/// Block stands on the floor just outside the bed's corners, welded to a corner block's side (its low sockets), for clear height.
/// It has no tiers: the modules unlock what it may make (a recipe's "needs").
/// </summary>
public sealed class AssemblerParts : IPartGenerator
{
    public string Name => "assembler_parts";

    private const string Out = "Constructs/Parts/Assembler";
    private const string Section = "Assemblers";
    private const string Body = "#6b4a35"; // the Assembler's rust brown (the old tier 1's), takes paint
    private const string Trim = "#4f3727";
    private const string Bed = "#3a3f46";
    private const double E = 0.997, H = 1.997; // the cell's half side and height, 3 mm in

    /// <summary>The bed's top (m above the foot): a Bed Block's height, a Core's deck. A Mast Block's low sockets are at half of it.</summary>
    public const double BedTop = 0.4;
    private const double OriginY = BedTop / 2; // the Core names its cell's middle at the bed layer's
    private static readonly double[] SlotX = [-0.55, 0, 0.55]; // the upgrade slots across its back, two rows
    private static readonly double[] SlotFoot = [0.42, 1.22];

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "assembler"), ("role", "core"), ("origin", Py.L(0, OriginY, 0)))
        : Kw(("family", "assembler"), ("role", role));

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));
    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static List<object?> Buttons() => Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.2, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.21, 0.02, 0.39, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.4, 0.02, 0.56, 0.12)), ("label", "Clear")),
        Kw(("action", "machine.recipe"), ("at", Py.L(0.57, 0.02, 0.75, 0.12)), ("label", "Recipe")),
        Kw(("action", "view.up"), ("at", Py.L(0.76, 0.02, 0.87, 0.12)), ("label", "Up")),
        Kw(("action", "view.down"), ("at", Py.L(0.88, 0.02, 0.98, 0.12)), ("label", "Down")));

    /// <summary>
    /// The Assembler Core: a 2 m cell. A plinth at the bed's height under all of it; on its back half the cabinet: four open tray cubbies
    /// on its front, the hopper on its top (Chute in), six upgrade slots on its back. On its front half the deck its own arm works on (a
    /// part up to 1.5 m a side with no bed). Bed blocks weld to its front and sides at the foot.
    /// </summary>
    private static Part Core()
    {
        var p = new Part("Assembler", Small, Section,
            "An Assembler Core: builds parts from a recipe or work orders. Alone it builds parts up to 1.5 m on its deck. Weld Assembly Bed Blocks beside it for a bed (up to 16 x 16, 32 x 32 m): the bed sets how big a part it builds and how many fit side by side. Each Gantry Carriage on the bed is one more part at once (32 at most), and Mast Blocks beside the bed's four corners give 2 m more clear height each. Upgrade modules on its back unlock electrical, hot, precision and field work, speed and capacity. Needs power.",
            role: "storage", group: "Production", color: Body);
        const double back = -0.92, front = 0.0, shelf = 1.0, top = 1.6; // (the cabinet's back plate, its front face, the cubbies' shelf, the hopper's floor)
        // Solid: the plinth (the bed's height), the cabinet's back plate, walls, shelf and hopper floor, the arm's column.
        p.Add(Box([-E, 0.003, -E], [E, BedTop, E], MachineLook.Base));
        p.Add(Box([-E, BedTop, -E], [E, H, back]));
        p.Add(Box([-E, BedTop, back], [-0.92, top, front]));
        p.Add(Box([0.92, BedTop, back], [E, top, front]));
        p.Add(Box([-0.03, BedTop, back], [0.03, top, front - 0.02]));
        p.Add(Box([-0.92, shelf - 0.02, back], [0.92, shelf + 0.02, front - 0.02]));
        p.Add(Box([-E, top, back], [E, top + 0.04, front]));
        foreach (var (x0, x1, z0, z1) in new[] { (-E, E, back, back + 0.04), (-E, E, front - 0.04, front), (-E, -E + 0.04, back, front), (E - 0.04, E, back, front) })
            p.Add(Box([x0, top + 0.04, z0], [x1, H - 0.05, z1], Trim)); // (the hopper's walls: open on top)
        p.Add(Box([0.72, BedTop, 0.72], [0.92, 1.75, 0.92], MachineLook.Base)); // (the arm's column on the deck's far corner)
        // ---- Sockets: its foot; bed blocks on its front and sides at the bed's middle; the top; the six upgrade slots on its back.
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, OriginY, 0], "+x");
        p.Sock("-x", [-1, OriginY, 0], "-x");
        p.Sock("+z", [0, OriginY, 1], "+z");
        p.Sock("top", [0, 2, -0.46], "+y");
        for (var row = 0; row < SlotFoot.Length; row++)
            for (var k = 0; k < SlotX.Length; k++)
                p.Sock($"module{row * SlotX.Length + k + 1}", [SlotX[k], SlotFoot[row] + MachineTierParts.MountY, -1], "-z", kind: "tier"); // (any module in any slot)
        p.Stats(2200, 3000, 8, 10);
        const double c0 = 0.04, c1 = 0.89; // (a cubby's inner edges across)
        p.Set(("power", 1500), ("machineBlock", BlockKey("core")),
            ("storage", Kw(
                ("slots", Py.L(
                    Kw(("min", Py.L(-c1, BedTop + 0.02, back + 0.02)), ("max", Py.L(-c0, shelf - 0.04, front - 0.06)), ("stack", 12), ("label", "tray")),
                    Kw(("min", Py.L(c0, BedTop + 0.02, back + 0.02)), ("max", Py.L(c1, shelf - 0.04, front - 0.06)), ("stack", 12), ("label", "tray")),
                    Kw(("min", Py.L(-c1, shelf + 0.04, back + 0.02)), ("max", Py.L(-c0, top - 0.02, front - 0.06)), ("stack", 12), ("label", "tray")),
                    Kw(("min", Py.L(c0, shelf + 0.04, back + 0.02)), ("max", Py.L(c1, top - 0.02, front - 0.06)), ("stack", 12), ("label", "tray")))),
                ("assemble", Kw(("capacity", 400), ("min", Py.L(-0.9, top + 0.05, back + 0.06)), ("max", Py.L(0.9, H - 0.08, front - 0.06)), ("batches", 4),
                    ("outAt", Py.L(0, 0.06, 1)), ("outDir", "+z"))))),
            ("config", Py.L(
                Kw(("key", "recipe"), ("label", "Recipe"), ("group", "Production"), ("tooltip", "What it builds.")),
                Kw(("key", "orderCount"), ("label", "Order count"), ("group", "Orders"), ("min", 1), ("max", 999), ("step", 1), ("default", 1), ("tooltip", "Parts the Queue button on its screen asks for: one order of its Recipe.")),
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(0, 2, -0.46)), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(1, 1.25, -0.46)), ("face", "+x")))),
            ("collider", "boxes"));
        // The control face on -x (the aisle side): screen, lamp, button shelf.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 8, width: 0.76, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Buttons();
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -1, 1, -1, 1, 0, 0.14, 0.012);
        // The ports' fittings: the hopper's mouth on its top, the tray's chute on +x.
        MachineLook.Port(p, [0, 2, -0.46], 1, 1, 0.16, 0.1, true);
        MachineLook.Port(p, [1, 1.25, -0.46], 0, 1, 0.12, 0.08, false);
        // The deck: a steel work plate with T-slots, a yellow kerb round its open edges, the bench's vice.
        p.Add(L([-0.95, BedTop, 0.02], [0.95, BedTop + 0.016, 0.95], MachineLook.Steel, "steel"));
        foreach (var x in new[] { -0.6, -0.2, 0.2, 0.6 })
            p.Add(L([x - 0.02, BedTop + 0.016, 0.06], [x + 0.02, BedTop + 0.03, 0.9], MachineLook.Base));
        p.Add(L([-0.4, BedTop + 0.016, 0.12], [-0.1, BedTop + 0.14, 0.28], MachineLook.Accent)); // (the vice)
        // The Core's own arm: a boom from its column over the deck, a hoist with its tool that glows while it builds.
        p.Add(L([-0.2, 1.62, 0.76], [0.72, 1.72, 0.88], MachineLook.Accent));
        p.Add(MachineLook.Work(L([0.1, 1.42, 0.76], [0.24, 1.62, 0.88], MachineLook.Base), "stroke", "-y", 0.4));
        p.Add(MachineLook.Work(L([0.14, 1.36, 0.79], [0.2, 1.42, 0.85], MachineLook.Hot), "glow"));
        MachineLook.Hazard(p, [0.7, 1.4, 0.92], [0.94, 1.5, 0.935]);
        // The cabinet: recessed panels on the back plate's sides, a vent, a maker's plate, the hopper's rim and its sorter.
        foreach (var sx in new[] { -1, 1 })
            for (var k = 0; k < 6; k++)
            {
                var y = 0.6 + k * 0.12;
                double x0 = sx > 0 ? E - 0.002 : -E - 0.014, x1 = sx > 0 ? E + 0.014 : -E + 0.002;
                if (sx > 0)
                    p.Add(L([x0, y, -0.86], [x1, y + 0.05, -0.62], MachineLook.Base)); // (louvres on +x, under the chute out)
            }
        p.Add(L([-E - 0.014, 1.7, -0.8], [-E + 0.002, 1.86, -0.12], MachineLook.In)); // (the maker's plate high on the aisle side)
        p.Add(L([-E - 0.026, 1.75, -0.74], [-E - 0.012, 1.81, -0.18], MachineLook.Ink));
        p.Add(MachineLook.Work(L([-0.8, top + 0.04, back + 0.1], [0.8, top + 0.08, back + 0.2], MachineLook.Steel, "steel"), "stroke", "+z", 0.04)); // (the sorter's shaker tray)
        MachineLook.Hazard(p, [-E - 0.012, H - 0.06, back], [-E, H - 0.02, front]);
        // The power cabinet on +x under the chute out: a breaker panel (four labelled breakers, two gauges) and a fan grille.
        p.Add(L([E - 0.002, 0.5, -0.42], [E + 0.016, 1.0, -0.06], MachineLook.Base));
        for (var k = 0; k < 4; k++)
        {
            var z = -0.36 + k * 0.08;
            p.Add(L([E + 0.016, 0.56, z], [E + 0.04, 0.66, z + 0.05], k == 0 ? MachineLook.Hot : MachineLook.Ink)); // (a breaker, the main red)
            p.Add(L([E + 0.016, 0.68, z], [E + 0.022, 0.7, z + 0.05], MachineLook.In)); // (its label)
        }
        foreach (var z in new[] { -0.32, -0.16 })
        {
            p.Add(L([E + 0.016, 0.78, z - 0.06], [E + 0.03, 0.9, z + 0.06], MachineLook.Steel, "steel", shape: Cyl("+x", 12))); // (a gauge's bezel)
            p.Add(L([E + 0.03, 0.8, z - 0.045], [E + 0.034, 0.88, z + 0.045], MachineLook.In, shape: Cyl("+x", 12))); // (its face)
        }
        p.Add(L([E - 0.002, 1.38, -0.88], [E + 0.012, 1.58, -0.64], MachineLook.Ink)); // (the fan grille's frame)
        p.Add(MachineLook.Work(L([E + 0.012, 1.4, -0.86], [E + 0.02, 1.56, -0.66], MachineLook.Steel, "steel", shape: Cyl("+x", 12)), "spin", "+x")); // (its fan, turning at work)
        // A tool rack on the deck's -x edge with three spare heads, and a drip tray under the vice.
        p.Add(L([-0.95, BedTop + 0.016, 0.34], [-0.86, 0.95, 0.86], MachineLook.Base));
        foreach (var z in new[] { 0.44, 0.6, 0.76 })
        {
            p.Add(L([-0.86, 0.78, z - 0.02], [-0.78, 0.8, z + 0.02], MachineLook.Steel, "steel")); // (a hook)
            p.Add(L([-0.86, 0.64, z - 0.05], [-0.78, 0.78, z + 0.05], z < 0.5 ? MachineLook.Hot : MachineLook.Accent, shape: Cyl("+y", 8))); // (a spare head)
        }
        p.Add(L([-0.44, BedTop + 0.016, 0.08], [-0.06, BedTop + 0.024, 0.32], MachineLook.Ink)); // (the drip tray under the vice)
        // Lifting eyes on the cabinet's top corners.
        foreach (var x in new[] { -0.85, 0.85 })
            p.Add(L([x - 0.05, H, -0.96], [x + 0.05, H + 0.08, -0.93], MachineLook.Steel, "steel"));
        return p;
    }

    /// <summary>
    /// An Assembly Bed Block: one 2 x 2 m cell of the bed, 0.4 m deep. A steel work table on top with T-slots both ways and a lift platen
    /// in its middle; a kerb with a hazard edge and a skirt on its sides, each a seam (drawn only on the bed's open edges, so a bed of
    /// blocks reads as one table). On its +x edge the gantry's rail and the carriage socket.
    /// </summary>
    private static Part BedBlock()
    {
        var p = new Part("Assembly Bed Block", Small, Section,
            "Weld it beside an Assembler Core or another Assembly Bed Block: 2 x 2 m more bed. The bed is the largest full rectangle of blocks next to the Core (a block outside it idles): it sets the biggest part the Assembler builds and how many fit side by side. Weld a Gantry Carriage on its rail edge for one more part at once; stand Mast Blocks beside the bed's corners for height.",
            group: "Production", color: Bed);
        const double t = BedTop, k = 0.06;
        p.Add(Box([-E + k, 0.003, -E + k], [E - k, t - 0.06, E - k]));
        foreach (var a in new[] { -0.9, 0.9 })
        {
            p.Add(Box([a - 0.06, 0.003, -E], [a + 0.06, t - 0.06, E]));
            p.Add(Box([-E, 0.003, a - 0.06], [E, t - 0.06, a + 0.06]));
        }
        p.Add(Box([-E, t - 0.06, -E], [E, t, E])); // (the table's slab)
        foreach (var (side, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            double[] lo = [-E + k, 0.006, -E + k], hi = [E - k, t - 0.064, E - k];
            if (sg > 0) { lo[n] = E - k - 0.004; hi[n] = E - 0.004; } else { lo[n] = -E + 0.004; hi[n] = -E + k + 0.004; }
            p.Add(L(lo, hi, Bed, paintable: true)!.Update(("seamFill", side)));
            double[] Pt(double along, double y, double out_) { var v = new double[3]; v[n] = sg * out_; v[2 - n] = along; v[1] = y; return v; }
            PyDict? Face(double a0, double a1, double y0, double y1, double o0, double o1, string colour, string? finish = null)
            {
                var u = Pt(a0, y0, o0);
                var w = Pt(a1, y1, o1);
                return L([Math.Min(u[0], w[0]), y0, Math.Min(u[2], w[2])], [Math.Max(u[0], w[0]), y1, Math.Max(u[2], w[2])], colour, finish);
            }
            p.Add(Seam(Face(-E, E, t - 0.06, t + 0.03, E - 0.07, E + 0.012, MachineLook.Base), side)); // (the kerb)
            p.Add(Seam(Face(-E + 0.02, E - 0.02, t - 0.14, t - 0.08, E - 0.004, E + 0.014, MachineLook.Accent, "hazard paint"), side));
            p.Add(Seam(Face(-E, E, 0.003, 0.1, E - 0.004, E + 0.012, MachineLook.Base), side)); // (the skirt)
        }
        // The table: steel top, T-slots both ways, the lift platen in its middle, a bolt at each corner.
        p.Add(L([-E + 0.02, t, -E + 0.02], [E - 0.02, t + 0.012, E - 0.02], MachineLook.Steel, "steel"));
        foreach (var a in new[] { -0.5, 0.5 })
        {
            p.Add(L([a - 0.018, t + 0.012, -0.9], [a + 0.018, t + 0.026, 0.9], MachineLook.Base));
            p.Add(L([-0.9, t + 0.012, a - 0.018], [-0.52, t + 0.026, a + 0.018], MachineLook.Base));
            p.Add(L([0.52, t + 0.012, a - 0.018], [0.9, t + 0.026, a + 0.018], MachineLook.Base));
        }
        p.Add(MachineLook.Work(L([-0.4, t + 0.012, -0.4], [0.4, t + 0.026, 0.4], "#7b848c", "steel"), "glow")); // (the lift platen: lit while its machine builds)
        foreach (var (x, z) in new[] { (0.93, 0.93), (-0.93, 0.93), (0.93, -0.93), (-0.93, -0.93) })
            p.Add(L([x - 0.025, t + 0.012, z - 0.025], [x + 0.025, t + 0.03, z + 0.025], MachineLook.Base, shape: Cyl("+y", 6)));
        // A bolt row round the platen, and the cable trench along its -x edge (one trench down the bed's length).
        foreach (var a in new[] { -0.3, -0.1, 0.1, 0.3 })
            foreach (var (bx, bz) in new[] { (a, -0.44), (a, 0.44), (-0.44, a), (0.44, a) })
                p.Add(L([bx - 0.015, t + 0.012, bz - 0.015], [bx + 0.015, t + 0.024, bz + 0.015], MachineLook.Steel, "steel", shape: Cyl("+y", 6)));
        p.Add(L([-0.86, t + 0.014, -E + 0.03], [-0.62, t + 0.018, E - 0.03], MachineLook.Ink)); // (the trench's grating: a dark strip on the table)
        // The gantry rail on the +x edge, where a carriage stands.
        p.Add(L([0.74, t + 0.012, -0.98], [0.9, t + 0.05, 0.98], MachineLook.Accent));
        p.Sock("+x", [1, OriginY, 0], "+x");
        p.Sock("-x", [-1, OriginY, 0], "-x");
        p.Sock("+z", [0, OriginY, 1], "+z");
        p.Sock("-z", [0, OriginY, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("carriage", [0.82, t, 0], "+y");
        p.Stats(900, 1600, 8, 6);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// A Gantry Carriage: a trolley on the bed's rail (0.3 x 0.9 m), a hoist mast and a four-axis arm whose tool glows while it builds.
    /// Welded on an Assembly Bed Block's carriage socket, it is one more arm: one more part built at once.
    /// </summary>
    private static Part Carriage()
    {
        var p = new Part("Gantry Carriage", Small, Section,
            "Weld it on an Assembly Bed Block's rail edge (the yellow rail): one more arm, so its Assembler builds one more part at once when the bed has room for them. One a bed block, 32 on a machine at most. Each working arm draws power.",
            group: "Production", color: MachineLook.Accent);
        p.Add(Box([-0.12, 0, -0.42], [0.12, 0.22, 0.42], MachineLook.Accent));
        p.Add(Box([-0.07, 0.22, 0.2], [0.07, 1.4, 0.34], MachineLook.Base)); // (the hoist mast)
        foreach (var z in new[] { -0.3, 0.3 })
            p.Add(L([-0.14, 0.03, z - 0.07], [0.14, 0.17, z + 0.07], MachineLook.Steel, "steel", shape: Cyl("+x", 10))); // (its wheels on the rail)
        p.Add(MachineLook.Work(L([-0.06, 0.22, -0.1], [0.06, 0.32, 0.02], MachineLook.Steel, "steel", shape: Cyl("+y", 10)), "spin", "+y")); // (the arm's turntable)
        p.Add(MachineLook.Work(L([-0.05, 1.2, -0.75], [0.05, 1.3, 0.2], MachineLook.Accent), "stroke", "-z", 0.3)); // (the boom out over the bed)
        p.Add(MachineLook.Work(L([-0.04, 0.9, -0.8], [0.04, 1.2, -0.7], MachineLook.Base), "stroke", "-y", 0.3)); // (the wrist)
        p.Add(MachineLook.Work(L([-0.03, 0.84, -0.78], [0.03, 0.9, -0.72], MachineLook.Hot), "glow")); // (the tool)
        MachineLook.Hazard(p, [-0.124, 0.12, -0.4], [0.124, 0.18, -0.38]);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(140, 600, 6, 4);
        p.Set(("machineBlock", Kw(("role", "carriage"))), ("centreSockets", false), ("collider", "boxes"), ("power", 0));
        return p;
    }

    /// <summary>
    /// An upgrade module that is no tier piece: the tier pieces' mount (0.38 x 0.5 x 0.3 m, its mount on its +z face, the Core's upgrade
    /// slot), the hidden collider, a module plate on its face. <paramref name="module"/>: "overdrive" or "buffer" (MachineBlocks.BlockModule).
    /// </summary>
    private static Part Module(string name, string module, string desc, string colour)
    {
        const double w = 0.19, h = 0.5, deep = 0.3;
        var p = new Part(name, Small, "Extrusion", desc, group: "Production", color: colour);
        p.Add(Box([-w, 0, -deep], [w, h, 0], hidden: true));
        p.Add(L([-w, 0.003, -0.022], [w, h - 0.003, -0.003], MachineLook.Base));
        foreach (var (x, y) in new[] { (-w + 0.035, 0.035), (w - 0.035, 0.035), (-w + 0.035, h - 0.035), (w - 0.035, h - 0.035) })
            p.Add(L([x - 0.016, y - 0.016, -0.04], [x + 0.016, y + 0.016, -0.022], MachineLook.Steel, "steel", shape: Kw(("shape", "cylinder"), ("segments", 6), ("front", "-z"))));
        p.Sock("mount", [0, MachineTierParts.MountY, 0], "+z", kind: "tier");
        p.Stats(45, 300, 6, 8);
        p.Set(("machineBlock", Kw(("role", "tier"), ("module", module))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>The Overdrive Unit: a flywheel housing in red, the flywheel spinning in its window while its machine works; a drive belt to the mount.</summary>
    private static Part Overdrive()
    {
        var p = Module("Overdrive Unit", "overdrive",
            "An Assembler's upgrade module: bolt it on an upgrade slot on the Core's back. Its Assembler builds twice as fast and draws 1.8 times the power, for each one fitted (4 count at most). It does nothing on another machine.",
            "#8a2a22");
        p.Add(L([-0.16, 0.04, -0.27], [0.16, 0.36, -0.022], "#8a2a22", paintable: true));
        p.Add(MachineLook.Work(L([-0.12, 0.08, -0.29], [0.12, 0.32, -0.27], MachineLook.Steel, "steel", shape: Cyl("-z", 16)), "spin", "-z")); // (the flywheel in its window)
        p.Add(L([-0.03, 0.17, -0.302], [0.03, 0.23, -0.29], MachineLook.Ink, shape: Cyl("-z", 8)));
        p.Add(L([-0.15, 0.38, -0.2], [0.15, 0.46, -0.05], MachineLook.Base)); // (the belt guard)
        p.Add(MachineLook.Work(L([0.11, 0.4, -0.24], [0.16, 0.47, -0.2], "#ff5a3a"), "glow"));
        MachineLook.Hazard(p, [-0.15, 0.363, -0.276], [0.15, 0.378, -0.26]);
        return p;
    }

    /// <summary>The Feed Buffer: a grey bin with a sight window, a level lamp that glows while its machine works, and a feed pipe to the mount.</summary>
    private static Part Buffer()
    {
        var p = Module("Feed Buffer", "buffer",
            "An Assembler's upgrade module: bolt it on an upgrade slot on the Core's back. Its Assembler's hopper holds twice as many batches, for each one fitted (4 count at most). It does nothing on another machine.",
            "#5a6470");
        p.Add(L([-0.17, 0.04, -0.28], [0.17, 0.44, -0.022], "#5a6470", paintable: true));
        p.Add(L([-0.05, 0.1, -0.296], [0.05, 0.36, -0.28], "#8fc5d9"));
        p.Add(MachineLook.Work(L([-0.04, 0.11, -0.31], [0.04, 0.2, -0.298], MachineLook.Accent), "glow")); // (the level)
        p.Add(L([-0.19, 0.443, -0.27], [0.19, 0.473, -0.03], MachineLook.Base)); // (its lid)
        p.Add(L([0.08, 0.473, -0.17], [0.14, 0.5, -0.11], MachineLook.Steel, "steel", shape: Cyl("+y", 8))); // (the feed pipe's stub)
        return p;
    }

    /// <summary>
    /// A tool block's dock (design doc "Tool Bay"): a 2 x 2 m plinth at the bed's height, a "dock" socket on its -x face that meets a
    /// side socket of the Core (the Tool Bay, a side its bed doesn't use), two locating pins and a clamp on that face. What stands on it
    /// stays under 0.9 m over its near half, clear of the Core's screen and chute out. Its tool is <paramref name="tool"/> (a recipe's "tool").
    /// </summary>
    private static Part ToolBlock(string name, string tool, string desc, string colour)
    {
        var p = new Part(name, Small, Section, desc, group: "Production", color: colour);
        const double t = BedTop;
        p.Add(Box([-E, 0.003, -E], [E, t, E], MachineLook.Base)); // (the plinth)
        MachineLook.Skirt(p, -1, 1, -1, 1, 0, 0.14, 0.012);
        // The dock on -x: a steel face plate, two locating pins into the Core's side, the clamp's jaw and its lever.
        p.Add(L([-E - 0.012, 0.06, -0.8], [-E + 0.002, t - 0.04, 0.8], MachineLook.Steel, "steel"));
        foreach (var z in new[] { -0.62, 0.62 })
            p.Add(L([-E - 0.05, t * 0.5 - 0.035, z - 0.035], [-E - 0.012, t * 0.5 + 0.035, z + 0.035], MachineLook.Steel, "steel", shape: Cyl("-x", 10))); // (a locating pin)
        p.Add(L([-E - 0.04, t - 0.035, -0.22], [-E - 0.012, t + 0.06, 0.22], MachineLook.Accent)); // (the clamp's jaw over the seam)
        p.Add(L([-E + 0.02, t + 0.002, -0.3], [-E + 0.12, t + 0.05, -0.24], MachineLook.Base));
        p.Add(MachineLook.Work(L([-E + 0.04, t + 0.05, -0.29], [-E + 0.1, t + 0.32, -0.25], MachineLook.Accent), "stroke", "+x", 0.04)); // (its lever)
        MachineLook.Hazard(p, [-E - 0.026, t - 0.03, -0.8], [-E - 0.012, t - 0.005, 0.8]);
        p.Sock("dock", [-1, OriginY, 0], "-x");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("machineBlock", Kw(("role", "tool"), ("tool", tool))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// The Thruster Jig Block: a concrete test pit on the plinth with a thrust cradle holding the chamber under test (its throat glows while
    /// its Assembler builds), a thrust gauge, and a burn-off flue in the far corner (smoke at work).
    /// </summary>
    private static Part ThrusterJig()
    {
        var p = ToolBlock("Thruster Jig Block", "thruster",
            "An Assembler's tool block for thrusters, rocket engines and boosters: dock it on a free side of an Assembler Core (its Tool Bay: the dock face against the Core) and the Core builds the recipes that need a thruster jig. Store tool blocks on a Block Rack: a chute from the rack into the Core swaps in the one its recipe needs.",
            "#5e6a72");
        const double t = BedTop;
        // The pit: a concrete ring on the plinth, its floor dark; solid walls, low on the dock side.
        p.Add(Box([-0.6, t, -0.8], [0.9, t + 0.1, -0.68], "#8b8a84"));
        p.Add(Box([-0.6, t, 0.68], [0.9, t + 0.1, 0.8], "#8b8a84"));
        p.Add(Box([-0.6, t, -0.68], [-0.48, t + 0.1, 0.68], "#8b8a84"));
        p.Add(Box([0.78, t, -0.68], [0.9, t + 0.5, 0.68], "#8b8a84")); // (the blast wall: the far side, higher)
        p.Add(L([-0.48, t + 0.002, -0.68], [0.78, t + 0.014, 0.68], MachineLook.Ink));
        // The thrust cradle: two saddles on a bed, the chamber lying across them, its bell towards the blast wall.
        p.Add(L([-0.36, t + 0.014, -0.12], [0.5, t + 0.05, 0.12], MachineLook.Steel, "steel"));
        foreach (var x in new[] { -0.2, 0.3 })
            p.Add(L([x - 0.04, t + 0.05, -0.2], [x + 0.04, t + 0.2, 0.2], MachineLook.Accent));
        p.Add(L([-0.34, t + 0.14, -0.13], [0.36, t + 0.4, 0.13], "#6f747a", "steel", shape: Cyl("+x", 14))); // (the chamber)
        p.Add(L([0.36, t + 0.1, -0.18], [0.66, t + 0.44, 0.18], "#6f747a", "steel", shape: Cyl("+x", 16))); // (its bell)
        p.Add(MachineLook.Work(L([0.62, t + 0.14, -0.14], [0.66, t + 0.4, 0.14], MachineLook.Hot, shape: Cyl("+x", 14)), "glow")); // (the throat, lit at work)
        // The thrust gauge on the blast wall's top, and its cable along the pit's rim.
        p.Add(L([0.79, t + 0.5, -0.3], [0.89, t + 0.62, -0.1], MachineLook.Steel, "steel", shape: Cyl("+y", 12)));
        p.Add(L([0.8, t + 0.62, -0.28], [0.88, t + 0.632, -0.12], MachineLook.In, shape: Cyl("+y", 12)));
        p.Add(L([-0.58, t + 0.1, -0.79], [0.86, t + 0.13, -0.75], MachineLook.Ink));
        // The burn-off flue in the far +z corner: a stack with a hood and a rain cap.
        p.Add(Box([0.62, t, 0.62], [0.92, 1.8, 0.92], MachineLook.Base));
        p.Add(L([0.58, 1.8, 0.58], [0.96, 1.86, 0.96], MachineLook.Steel, "steel"));
        p.Add(MachineLook.Work(L([0.66, 1.86, 0.66], [0.88, 1.9, 0.88], MachineLook.Ink), "smoke"));
        p.Add(L([0.6, 1.93, 0.6], [0.94, 1.96, 0.94], MachineLook.Base));
        foreach (var y in new[] { 1.0, 1.4 })
            p.Add(L([0.61, y, 0.61], [0.93, y + 0.03, 0.93], MachineLook.Steel, "steel")); // (its bands)
        MachineLook.Hazard(p, [0.9, t + 0.38, -0.68], [0.914, t + 0.46, 0.68]);
        p.Stats(700, 1400, 8, 6);
        return p;
    }

    /// <summary>
    /// The Sewing Table Block: a long work table on the plinth, a cloth roll on its spindle at the far end (turning at work), and a sewing
    /// head on an arch over the table whose needle strokes and lamp glows while its Assembler builds.
    /// </summary>
    private static Part SewingTable()
    {
        var p = ToolBlock("Sewing Table Block", "tailor",
            "An Assembler's tool block for lift cells and other cloth work: dock it on a free side of an Assembler Core (its Tool Bay: the dock face against the Core) and the Core builds the recipes that need a sewing table. Store tool blocks on a Block Rack: a chute from the rack into the Core swaps in the one its recipe needs.",
            "#4a5a4c");
        const double t = BedTop, top = 0.86;
        // The table: four legs, a top with a cutting rule and a cloth sheet laid on it.
        foreach (var (x, z) in new[] { (-0.7, -0.7), (-0.7, 0.62), (0.62, -0.7), (0.62, 0.62) })
            p.Add(Box([x, t, z], [x + 0.08, top - 0.04, z + 0.08], MachineLook.Base));
        p.Add(Box([-0.74, top - 0.04, -0.74], [0.74, top, 0.74], "#6b5a44"));
        p.Add(L([-0.7, top, -0.6], [0.4, top + 0.012, 0.6], "#c9c2a8", "fabric"));
        p.Add(L([-0.72, top, 0.64], [0.72, top + 0.014, 0.68], MachineLook.Steel, "steel"));
        // The cloth roll at the far end: two brackets, the roll turning on its spindle while it works.
        foreach (var z in new[] { -0.72, 0.64 })
            p.Add(L([0.62, top, z], [0.7, top + 0.3, z + 0.08], MachineLook.Base));
        p.Add(MachineLook.Work(L([0.52, top + 0.12, -0.64], [0.8, top + 0.4, 0.64], "#b9b098", "fabric", shape: Cyl("+z", 16)), "spin", "+z"));
        // The sewing head on an arch over the middle of the table: posts, beam, head, needle (strokes) and its lamp (glows).
        foreach (var z in new[] { -0.8, 0.72 })
            p.Add(Box([0.02, top, z], [0.14, 1.62, z + 0.08], MachineLook.Accent));
        p.Add(Box([0.02, 1.62, -0.8], [0.14, 1.72, 0.8], MachineLook.Accent));
        p.Add(L([-0.08, 1.3, -0.12], [0.2, 1.62, 0.12], MachineLook.Base));
        p.Add(MachineLook.Work(L([0.04, 1.06, -0.012], [0.07, 1.3, 0.012], MachineLook.Steel, "steel"), "stroke", "-y", 0.1)); // (the needle bar)
        p.Add(MachineLook.Work(L([-0.07, 1.24, -0.1], [-0.04, 1.28, -0.04], "#ffe9a8"), "glow")); // (its lamp)
        p.Add(L([-0.1, 1.4, 0.06], [-0.08, 1.56, 0.11], MachineLook.Steel, "steel", shape: Cyl("-x", 10))); // (the handwheel)
        MachineLook.Hazard(p, [0.02, 1.72, -0.8], [0.14, 1.732, 0.8]);
        p.Stats(380, 1000, 6, 6);
        return p;
    }

    /// <summary>The Block Rack: the Die Rack at 2 m bays (one rack piece, PressParts.Rack), two bays of a tool block each, on the medium lattice.</summary>
    private static Part BlockRack()
    {
        var p = PressParts.Rack(Medium, 5, 3, 3, "Block Rack",
            "Holds an Assembler's tool blocks (Thruster Jig Block, Sewing Table Block) and press dies, two bays side by side, one kind a bay: the Die Rack at 2 m bays. A chute from it into an Assembler Core brings the tool block its recipe needs and takes the old one back; a chute into a press brings it a die.",
            bays: 2, label: "Tools and dies", folder: Section);
        // Its dressing (the rack's own geometry: Medium cells, 5 x 3 x 3, posts 0.14 m, a 0.12 m board; PressParts.Rack): top rails between the
        // posts, a steel plate on each bay's floor with hazard edges and four locating pins, a bay number plate and lifting eyes.
        const double x0 = -0.5, x1 = 4.5, y1 = 2.5, z0 = -0.5, z1 = 2.5, floor = -0.38, post = 0.14;
        const double bay = (x1 - x0 - 3 * post) / 2;
        foreach (var z in new[] { z0 + 0.03, z1 - 0.11 })
            p.Add(L([x0 + 0.02, y1 - 0.2, z], [x1 - 0.02, y1 - 0.08, z + 0.08], MachineLook.Accent));
        for (var b = 0; b < 2; b++)
        {
            var bx = x0 + post + b * (bay + post);
            p.Add(L([bx + 0.05, floor, z0 + 0.05], [bx + bay - 0.05, floor + 0.014, z1 - 0.05], MachineLook.Steel, "tread plate"));
            foreach (var z in new[] { z0 + 0.05, z1 - 0.17 })
                MachineLook.Hazard(p, [bx + 0.05, floor + 0.014, z], [bx + bay - 0.05, floor + 0.026, z + 0.12]);
            foreach (var (dx, dz) in new[] { (0.3, 0.3), (bay - 0.3, 0.3), (0.3, 2.7), (bay - 0.3, 2.7) })
                p.Add(L([bx + dx - 0.03, floor + 0.014, z0 + dz - 0.03], [bx + dx + 0.03, floor + 0.09, z0 + dz + 0.03], MachineLook.Steel, "steel", shape: Cyl("+y", 8))); // (a locating pin)
            var mid = bx + bay / 2;
            p.Add(L([mid - 0.18, y1 - 0.19, z1 - 0.034], [mid + 0.18, y1 - 0.09, z1 - 0.02], MachineLook.In)); // (the bay's number plate)
            p.Add(L([mid - 0.04 - (b == 0 ? 0 : 0.05), y1 - 0.17, z1 - 0.02], [mid + 0.04 + (b == 0 ? 0 : 0.05), y1 - 0.11, z1 - 0.008], MachineLook.Ink)); // (its figure)
        }
        foreach (var x in new[] { x0 + 0.07, x1 - 0.07 })
            foreach (var z in new[] { z0 + 0.07, z1 - 0.07 })
                p.Add(L([x - 0.05, y1, z - 0.02], [x + 0.05, y1 + 0.08, z + 0.02], MachineLook.Steel, "steel")); // (a lifting eye on a post's top)
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BedBlock()), ("", Carriage()), ("", Overdrive()), ("", Buffer()), ("", ThrusterJig()), ("", SewingTable()), ("", BlockRack())]);
}
