using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Laser Cutter on the block system: Constructs/Parts/LaserCutter (docs/sandbox/constructs/production.md, "The Laser Cutter"; design
/// page drawing 16; MachineBlocks.cs, ConstructLaserCutterBed.cs). Two parts: the Laser Cutter Core, 2 m long and 1 m across, that stands
/// on a long side of the bed (the laser source, screen and queue, Orders, Run, power, the offcut bin with its Offcut port, the fume
/// extractor and its stack), and the Laser Bed Block, one identical 2 x 2 m cell of the slatted bed. The bed is the largest full rectangle
/// of blocks beside the Core; it sets the largest blank. A block's frame, rails, ducts and hazard edge are drawn only on the machine's
/// outer faces ("seam"), so a bed reads as one machine: one frame round it, a rail along each long edge, the fume duct along the far
/// side, and one bridge across its first row that runs the bed's length with one yellow head per block across (bridge, head and beam
/// work: they follow the cut's own outline). Replaces the Laser Cutter, Medium, Large and Capital Laser Cutters. Born in the Machine look.
/// </summary>
public sealed class LaserCutterParts : IPartGenerator
{
    public string Name => "laser_cutter_parts";

    private const string Out = "Constructs/Parts/LaserCutter";
    private const string Body = "#a8365e"; // the Laser Cutters' colour: the Core's cabinet and the bridge
    private const string Trim = "#7f2846";
    private const string Frame = "#3a3f46"; // the bed's dark frame
    private const string Blue = "#2f62b5", Red = "#b8322a"; // coolant return and feed
    private const double Slat = 0.98; // the slats' top: where a blank lies
    private const double RailTop = 3.175; // the rails' running strip
    private const double HeadKw = 2.4; // one head's cutting power: the Small cutter's of old (seconds = 2 + 0.36 x edge x mm / (kW x heads))
    private const int HeadW = 2400, CoreBin = 200, BlockBin = 100;

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "laser"), ("role", "core"), ("origin", Py.L(-0.5, 1.2, 0)), ("coreSeams", false))
        : Kw(("family", "laser"), ("role", role));

    private static List<object?> Buttons() => Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.2, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.21, 0.02, 0.39, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.4, 0.02, 0.56, 0.12)), ("label", "Clear")),
        Kw(("action", "machine.recipe"), ("at", Py.L(0.57, 0.02, 0.75, 0.12)), ("label", "Design")), // (the world's cut designs, from the recipe picker: a pick orders one)
        Kw(("action", "view.up"), ("at", Py.L(0.76, 0.02, 0.87, 0.12)), ("label", "Up")),
        Kw(("action", "view.down"), ("at", Py.L(0.88, 0.02, 0.98, 0.12)), ("label", "Down")));

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static PyDict Seam(PyDict? box, params string[] faces) => box!.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    // ---- The Core ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The Laser Cutter Core: x -0.5..0.5 (its +x face welds to the bed's first block), z -1..1 along the bed. Its -z half is the laser
    /// source's cabinet (screen on the aisle side, vents, gauges, the chiller and the resonator on top, the beam pipe out to the bridge);
    /// its +z half is the offcut bin below (Offcut port) and the fume extractor above it, with its stack.
    /// </summary>
    private static Part Core()
    {
        var p = new Part("Laser Cutter", Small, "Extrusion",
            "A Laser Cutter Core: cuts openings, notches and corners out of a formed panel or a flat extruded piece, by order. Weld Laser Bed Blocks beside its long side: the bed is their rectangle, up to 32 x 32 m, and its bridge carries one head per block across, so a wider bed cuts faster. Blanks come in by conveyor; the offcut drops into its bin. Needs power.",
            role: "storage", group: "Production", color: Body);
        const double e = 0.497, cz = 0.06; // (its body stops 3 mm inside its half cell; cz: where the cabinet meets the bin)
        // Solid: the plinth, the source cabinet (to 2.25 m) and the bin and extractor (to 2.0 m).
        p.Add(Box([-e, 0, -0.997], [e, 0.14, 0.997]));
        p.Add(Box([-e + 0.05, 0.14, -0.997 + 0.05], [e - 0.05, 2.25, cz]));
        p.Add(Box([-e, 0.14, -0.997], [-e + 0.05, 2.25, cz]));
        p.Add(Box([e - 0.05, 0.14, -0.997], [e, 2.25, cz]));
        p.Add(Box([-e + 0.05, 0.14, -0.997], [e - 0.05, 2.25, -0.947]));
        p.Add(Box([-e, 0.14, cz + 0.006], [e, 1.1, 0.997]));
        p.Add(Box([-e + 0.04, 1.1, cz + 0.046], [e - 0.04, 2.0, 0.957]));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [0.5, 0.5, 0], "+x"); // (the bed's first Laser Bed Block)
        p.Stats(2200, 2200, 6, 10);
        var bulkCap = CoreBin + BlockBin * 128;
        var bulk = Kw(("material", "any"), ("capacity", bulkCap), ("min", Py.L(-0.4, 0.2, R(cz + 0.08))), ("max", Py.L(0.4, 1.0, 0.9)));
        p.Set(("power", HeadW), ("storage", Kw(("bulk", bulk),
                ("cut", Kw(("kw", HeadKw), ("at", Py.L(0, Slat, 0)), ("maxWidth", 32), ("maxLength", 32), ("maxHeight", 2.0), ("bin", CoreBin), ("binBlock", BlockBin))))),
            ("machineBlock", BlockKey("core")),
            ("config", Py.L(
                ExtrusionParts.OrderCount,
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, 0.5, -0.997)), ("face", "-z"), ("opening", Py.L(6, 2))),
                Kw(("name", "Offcut"), ("type", "chute"), ("dir", "out"), ("at", Py.L(-0.497, 0.45, 0.55)), ("face", "-x")))),
            ("centreSockets", false), ("collider", "boxes"));
        // The control face on the aisle side (-x): the screen, its lamp and the button shelf, on the source cabinet.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        keepClear.Add(([-0.497, 0.45, 0.55], "-x"));
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 8, width: 0.84, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Buttons();
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -e, e, -0.997, 0.997, 0, 0.12, 0.012);
        // The cabinet's frame: its corner posts stand proud of the sunk side panels; a seam line where its doors meet.
        foreach (var x in new[] { -e - 0.012, e - 0.038 })
            foreach (var z in new[] { -0.997, cz - 0.05 })
                p.Add(L([x, 0.14, z], [x + 0.05, 2.25, z + 0.05], Trim, paintable: true));
        p.Add(L([-e - 0.014, 0.14, -0.48], [-e + 0.002, 2.25, -0.46], MachineLook.Base)); // (the doors' meeting line)
        foreach (var y in new[] { 0.6, 1.9 })
            p.Add(L([-e - 0.03, y, -0.43], [-e - 0.004, y + 0.16, -0.39], MachineLook.Steel, "steel")); // (the door's hinges, on the aisle face)
        p.Add(L([-e - 0.04, 1.0, -0.53], [-e - 0.004, 1.3, -0.5], MachineLook.Base)); // (its handle)
        // The vents on the cabinet's far face (+x) and its in end (-z): louvres in a dark frame.
        for (var k = 0; k < 7; k++)
        {
            var y = 0.4 + k * 0.1;
            p.Add(L([e - 0.004, y, -0.85], [e + 0.018, y + 0.04, -0.15], MachineLook.Base));
            p.Add(L([-0.35, y, -1.015], [0.35, y + 0.04, -0.993], MachineLook.Base));
        }
        p.Add(L([e - 0.006, 0.36, -0.88], [e + 0.008, 1.12, -0.86], Trim, paintable: true));
        p.Add(L([e - 0.006, 0.36, -0.14], [e + 0.008, 1.12, -0.12], Trim, paintable: true));
        // Gauges on the far face: coolant temperature and pressure, white faces with a needle; the maker's plate and a laser warning sign.
        foreach (var z in new[] { -0.75, -0.45 })
        {
            p.Add(L([e - 0.004, 1.5, z - 0.1], [e + 0.03, 1.7, z + 0.1], "#e8e6df", shape: Cyl("+x", 14)));
            p.Add(L([e + 0.03, 1.58, z - 0.008], [e + 0.044, 1.68, z + 0.008], MachineLook.Ink));
        }
        p.Add(L([e - 0.004, 1.3, -0.82], [e + 0.012, 1.42, -0.38], MachineLook.In));
        p.Add(L([e + 0.012, 1.34, -0.78], [e + 0.024, 1.38, -0.42], MachineLook.Ink));
        p.Add(L([-0.3, 1.5, -1.013], [0.3, 1.95, -0.993], MachineLook.Accent, "hazard paint")); // (the in end: a laser warning panel)
        p.Add(L([-0.12, 1.62, -1.025], [0.12, 1.83, -1.011], MachineLook.Ink));
        // The cable runs: a conduit up the cabinet's far corner to the resonator, and the mains box at its foot.
        p.Add(L([e - 0.02, 0.2, -0.93], [e + 0.04, 2.25, -0.89], MachineLook.Base, shape: Cyl("+y", 8)));
        p.Add(L([e - 0.004, 0.18, -0.85], [e + 0.06, 0.4, -0.6], MachineLook.Base));
        foreach (var y in new[] { 0.7, 1.25, 1.8 })
            p.Add(L([e - 0.006, y, -0.945], [e + 0.05, y + 0.03, -0.875], MachineLook.Steel, "steel")); // (its clips)
        // On top of the cabinet: the chiller with its fan (spinning while it cuts), the resonator tube on two saddles, the coolant lines.
        p.Add(L([-0.45, 2.25, -0.95], [0.05, 2.62, -0.5], MachineLook.Base));
        for (var k = 0; k < 4; k++)
            p.Add(L([-0.44, 2.3 + k * 0.08, -0.962], [0.04, 2.33 + k * 0.08, -0.948], MachineLook.Steel, "steel")); // (its fins on the in end)
        p.Add(L([-0.39, 2.62, -0.9], [-0.01, 2.65, -0.55], MachineLook.Base, shape: Cyl("+y", 16)));
        p.Add(MachineLook.Work(L([-0.36, 2.65, -0.87], [-0.04, 2.67, -0.58], MachineLook.Steel, "steel", shape: Cyl("+y", 6)), "spin", "+y"));
        foreach (var z in new[] { -0.4, 0.0 })
            p.Add(L([0.06, 2.25, z - 0.06], [0.38, 2.4, z + 0.06], MachineLook.Base));
        p.Add(L([0.06, 2.4, -0.5], [0.38, 2.72, 0.04], Body, paintable: true, shape: Cyl("+z", 16))); // (the resonator)
        p.Add(L([0.04, 2.38, -0.53], [0.4, 2.74, -0.49], MachineLook.Steel, "steel", shape: Cyl("+z", 16))); // (its end caps)
        p.Add(L([0.04, 2.38, 0.0], [0.4, 2.74, 0.04], MachineLook.Steel, "steel", shape: Cyl("+z", 16)));
        foreach (var z in new[] { -0.35, -0.2, -0.05 })
            p.Add(L([0.05, 2.39, z - 0.012], [0.39, 2.73, z + 0.012], MachineLook.Base, shape: Cyl("+z", 16))); // (its bands)
        foreach (var (y, colour) in new[] { (2.45, Red), (2.53, Blue) })
            p.Add(L([0.05, y - 0.02, -0.75], [0.12, y + 0.02, -0.52], colour)); // (the coolant lines, chiller to resonator)
        // The beam pipe: from the resonator's end up and out over the bed to the bridge's rail, on a bracket.
        p.Add(L([0.14, 2.42, 0.04], [0.3, 2.58, 0.3], MachineLook.Steel, "steel"));
        p.Add(L([0.14, 2.42, 0.3], [0.3, 3.26, 0.46], MachineLook.Steel, "steel"));
        p.Add(L([0.14, 3.26, 0.3], [0.497, 3.42, 0.46], MachineLook.Steel, "steel"));
        p.Add(L([0.12, 3.24, 0.28], [0.32, 3.27, 0.48], MachineLook.Base)); // (its elbow's flange)
        p.Add(L([0.12, 2.6, 0.28], [0.32, 2.63, 0.48], MachineLook.Base));
        p.Add(L([0.13, 2.25, 0.15], [0.31, 2.42, 0.25], MachineLook.Base)); // (its bracket)
        // The offcut bin: a hopper's sloped front, a sight glass on a level gauge, its Offcut port (the shared port look), a hazard edge on its rim.
        p.Add(L([-e - 0.012, 1.0, cz + 0.006], [e + 0.012, 1.1, 0.997], MachineLook.Base)); // (its rim)
        MachineLook.Hazard(p, [-e - 0.024, 1.02, cz + 0.04], [-e - 0.012, 1.08, 0.97]);
        p.Add(L([-e - 0.012, 0.2, 0.86], [-e + 0.004, 0.95, 0.9], MachineLook.Base)); // (its level gauge's tube)
        p.Add(L([-e - 0.024, 0.28, 0.865], [-e - 0.012, 0.6, 0.895], MachineLook.Accent));
        MachineLook.Port(p, [-0.497, 0.45, 0.55], 0, -1, 0.14, 0.12, false);
        p.Add(L([-e - 0.014, 0.75, 0.25], [-e + 0.002, 0.95, 0.75], MachineLook.In)); // (its label: "OFFCUT")
        p.Add(L([-e - 0.026, 0.82, 0.3], [-e - 0.014, 0.88, 0.7], MachineLook.Ink));
        // The fume extractor over the bin: its fan housing, louvres on the aisle side, the duct in from the bed's plenum and its stack (smoking while it cuts).
        for (var k = 0; k < 6; k++)
            p.Add(L([-e + 0.024, 1.25 + k * 0.11, cz + 0.12], [-e + 0.044, 1.29 + k * 0.11, 0.88], MachineLook.Base));
        p.Add(L([e - 0.06, 1.2, cz + 0.2], [e + 0.004, 1.55, 0.6], MachineLook.Base)); // (the duct's collar from under the bed)
        p.Add(L([-0.2, 2.0, 0.4], [0.2, 2.15, 0.8], MachineLook.Base, shape: Cyl("+y", 16)));
        p.Add(L([-0.14, 2.15, 0.46], [0.14, 3.4, 0.74], MachineLook.Steel, "steel", shape: Cyl("+y", 14)));
        foreach (var y in new[] { 2.6, 3.1 })
            p.Add(L([-0.16, y, 0.44], [0.16, y + 0.04, 0.76], MachineLook.Base, shape: Cyl("+y", 14))); // (its bands)
        p.Add(MachineLook.Work(L([-0.17, 3.4, 0.43], [0.17, 3.46, 0.77], MachineLook.Base, shape: Cyl("+y", 14)), "smoke"));
        // Bolts along its plinth, both long sides.
        foreach (var z in new[] { -0.8, -0.4, 0.0, 0.4, 0.8 })
            foreach (var x in new[] { -e - 0.024, e + 0.004 })
                p.Add(L([x, 0.05, z - 0.02], [x + 0.02, 0.09, z + 0.02], MachineLook.Steel, "steel"));
        return p;
    }

    // ---- The Bed Block ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A Laser Bed Block: one 2 x 2 m cell of the bed. A dark frame (corner posts, a rim and a plinth round sunk side panels) under steel
    /// slats on cross bars over the fume plenum; a rail girder on a post along each long (x) edge, the cable chain's tray on the far rail,
    /// the fume duct along the far side, and on the first row (no block on its -z face) the bridge's slice with its trucks and this block's
    /// head. Each outer thing is a seam: drawn only where no block of the same machine is welded on that face.
    /// </summary>
    private static Part BedBlock()
    {
        var p = new Part("Laser Bed Block", Small, "Extrusion",
            "Weld it beside a Laser Cutter Core's long side, or beside another bed block: 2 x 2 m more bed, and one more head on the bridge for each block across. The bed is the largest full rectangle of blocks beside the Core; a block outside it idles.",
            group: "Production", color: Frame);
        const double e = 0.997, d = 0.06, post = 0.12, top = 0.9, rim = 0.8, plinth = 0.12;
        // Solid: the core of the frame (its sides sunk d), the corner posts, the rim and the plinth.
        p.Add(Box([-e + d, 0.003, -e + d], [e - d, top, e - d]));
        p.Add(Box([-e, rim, -e], [e, top, e]));
        p.Add(Box([-e, 0.003, -e], [e, plinth, e]));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? e - post : -e, z0 = sz > 0 ? e - post : -e;
                p.Add(Box([x0, plinth, z0], [x0 + post, rim, z0 + post]));
            }
        // Each side's sunk panel: filled in the frame's tone where a block is welded on, else its stiffeners and the plenum's louvres show.
        foreach (var (n, sg, face) in new[] { (0, 1, "+x"), (0, -1, "-x"), (2, 1, "+z"), (2, -1, "-z") })
        {
            double[] At(double a0, double a1, double along0, double along1, double y0, double y1)
            {
                // a0..a1: out from the sunk face (0 at the sunk face, d at the cell's); along: the other horizontal axis.
                var mn = new double[3];
                var mx = new double[3];
                var (lo, hi) = sg > 0 ? (e - d + a0, e - d + a1) : (-(e - d + a1), -(e - d + a0));
                (mn[n], mx[n]) = (lo, hi);
                var o = 2 - n;
                (mn[o], mx[o]) = (along0, along1);
                (mn[1], mx[1]) = (y0, y1);
                return [.. mn, .. mx];
            }
            PyDict? Look(double a0, double a1, double along0, double along1, double y0, double y1, string colour, string? finish = null, bool paint = false)
            {
                var b = At(a0, a1, along0, along1, y0, y1);
                return L([b[0], b[1], b[2]], [b[3], b[4], b[5]], colour, finish, paint);
            }
            p.Add(Look(-0.004, d - 0.004, -e + post + 0.004, e - post - 0.004, plinth + 0.004, rim - 0.004, Frame, paint: true)!.Update(("seamFill", face)));
            foreach (var t in new[] { -0.45, 0.45 })
                p.Add(Seam(Look(-0.006, 0.03, t - 0.03, t + 0.03, plinth + 0.004, rim - 0.004, Frame, paint: true), face)); // (stiffeners)
            for (var k = 0; k < 5; k++)
                p.Add(Seam(Look(-0.006, 0.02, -0.3, 0.3, 0.22 + k * 0.08, 0.25 + k * 0.08, MachineLook.Base), face)); // (the plenum's louvres)
            p.Add(Seam(Look(-0.006, 0.014, 0.56, 0.78, 0.5, 0.62, MachineLook.In), face)); // (a rating plate)
            p.Add(Seam(Look(0.014, 0.026, 0.59, 0.75, 0.54, 0.58, MachineLook.Ink), face));
            // The rim's hazard edge round the outside, the skirt at its foot, and bolts along the rim.
            p.Add(Seam(Look(d - 0.004, d + 0.01, -e, e, rim + 0.02, top - 0.02, MachineLook.Accent, "hazard paint"), face));
            p.Add(Seam(Look(d - 0.004, d + 0.012, -e, e, 0.0, plinth - 0.02, MachineLook.Base), face));
            foreach (var t in new[] { -0.75, -0.25, 0.25, 0.75 })
                p.Add(Seam(Look(d + 0.01, d + 0.026, t - 0.02, t + 0.02, top - 0.02, top - 0.004, MachineLook.Steel, "steel"), face)); // (bolt heads on the rim's top edge)
        }
        // The corner posts' caps and the notch fill where blocks meet: a post is a corner only at the machine's corners.
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? e - post : -e, z0 = sz > 0 ? e - post : -e;
                string fx = sx > 0 ? "+x" : "-x", fz = sz > 0 ? "+z" : "-z";
                p.Add(Seam(L([x0 - 0.006, plinth, z0 - 0.006], [x0 + post + 0.006, plinth + 0.05, z0 + post + 0.006], MachineLook.Base), fx, fz)); // (its foot)
            }
        // The bed: the plenum's dark grating under the slats, two cross bars, and seven steel slats along it (the blank lies on them).
        p.Add(L([-e + 0.01, top, -e + 0.01], [e - 0.01, top + 0.012, e - 0.01], MachineLook.Base));
        foreach (var z in new[] { -0.55, 0.0, 0.55 })
            p.Add(L([-e + 0.03, top + 0.012, z - 0.03], [e - 0.03, top + 0.05, z + 0.03], MachineLook.Base));
        for (var k = 0; k < 7; k++)
        {
            var x = -0.81 + k * 0.27;
            p.Add(L([x - 0.012, top + 0.05, -0.985], [x + 0.012, Slat, 0.985], MachineLook.Steel, "steel"));
        }
        // The rails: a girder on a post along each long edge, its running strip bare steel; the post's foot hazard-painted, a laser
        // warning sign on its outer face. On the far side (+x) the cable chain's tray lies along the rail, and the fume duct runs along the frame.
        foreach (var (sx, face) in new[] { (1, "+x"), (-1, "-x") })
        {
            double[] Xs(double a, double b) => sx > 0 ? [a, b] : [-b, -a]; // (a span in from the cell's x edge at e, mirrored)
            var g = Xs(e - 0.12, e);
            var web = Xs(e - 0.075, e - 0.045);
            var strip = Xs(e - 0.085, e - 0.035);
            p.Add(Seam(L([g[0], 2.99, -e], [g[1], 3.02, e], MachineLook.Base), face));
            p.Add(Seam(L([web[0], 3.02, -e + 0.003], [web[1], 3.13, e - 0.003], MachineLook.Base), face));
            p.Add(Seam(L([g[0], 3.13, -e], [g[1], 3.16, e], MachineLook.Base), face));
            p.Add(Seam(L([strip[0], 3.16, -e + 0.005], [strip[1], RailTop, e - 0.005], MachineLook.Steel, "steel"), face));
            var pz = 0.08;
            p.Add(Seam(L([g[0] + 0.01, top, -pz], [g[1] - 0.01, 2.99, pz], MachineLook.Base), face)); // (the post)
            p.Add(Seam(L([g[0] + 0.004, top, -pz - 0.08], [g[1] - 0.004, top + 0.04, pz + 0.08], MachineLook.Base), face)); // (its base plate)
            p.Add(Seam(MachineLook.D([g[0] + 0.006, top + 0.04, -pz - 0.006], [g[1] - 0.006, top + 0.3, pz + 0.006], MachineLook.Accent, "hazard paint"), face));
            foreach (var z in new[] { -pz - 0.05, pz + 0.05 })
                p.Add(Seam(L([g[0] + 0.02, top + 0.04, z - 0.015], [g[1] - 0.02, top + 0.06, z + 0.015], MachineLook.Steel, "steel"), face)); // (its anchor bolts)
            var sign = Xs(e - 0.03, e - 0.016);
            p.Add(Seam(L([sign[0], 1.8, -0.07], [sign[1], 2.05, 0.07], MachineLook.Accent), face)); // (a laser warning sign on the post's outer face)
            var ink = Xs(e - 0.016, e - 0.004);
            p.Add(Seam(L([ink[0], 1.86, -0.035], [ink[1], 1.98, 0.035], MachineLook.Ink), face));
            // Gussets from the post up under the girder, along it.
            foreach (var (z0, z1) in new[] { (-0.3, -pz), (pz, 0.3) })
                p.Add(Seam(L([g[0] + 0.03, 2.76, z0], [g[1] - 0.03, 2.99, z1], MachineLook.Base), face));
        }
        // The cable chain's tray along the far rail, inside it, and the chain lying in it.
        p.Add(Seam(L([e - 0.2, 3.02, -e], [e - 0.125, 3.045, e], MachineLook.Base), "+x"));
        p.Add(Seam(L([e - 0.202, 3.045, -e], [e - 0.19, 3.12, e], MachineLook.Base), "+x"));
        p.Add(Seam(L([e - 0.185, 3.045, -e + 0.01], [e - 0.14, 3.08, e - 0.01], "#2b3036"), "+x"));
        // The fume duct along the far side's frame, its flanges at the block's ends.
        p.Add(Seam(L([e - 0.2, 0.32, -e], [e, 0.52, e], MachineLook.Steel, "steel", shape: Cyl("+z", 14)), "+x"));
        foreach (var z in new[] { -e, e - 0.04 })
            p.Add(Seam(L([e - 0.215, 0.305, z], [e + 0.0, 0.535, z + 0.04], MachineLook.Base, shape: Cyl("+z", 14)), "+x"));
        // ---- The bridge: on the first row only (no block on the -z face); it runs along the bed and its head across its block. ----
        const double bz0 = -0.86, bz1 = -0.56, by0 = 3.21, by1 = 3.58;
        p.Add(Seam(Work(L([-1, by0, bz0], [1, by1, bz1], Body, paintable: true), "bridge"), "-z"));
        p.Add(Seam(Work(L([-1, by1, bz0 + 0.02], [1, by1 + 0.03, bz1 - 0.02], MachineLook.Steel, "steel"), "bridge"), "-z")); // (its top cover)
        p.Add(Seam(Work(MachineLook.D([-1, by0 - 0.03, bz0 + 0.03], [1, by0, bz1 - 0.03], MachineLook.Accent, "hazard paint"), "bridge"), "-z")); // (hazard paint under it: it runs over the bed)
        foreach (var x in new[] { -0.5, 0.5 })
            p.Add(Seam(Work(L([x - 0.025, by0 + 0.02, bz0 - 0.014], [x + 0.025, by1 - 0.02, bz0], Trim, paintable: true), "bridge"), "-z")); // (its stiffeners, on the back)
        p.Add(Seam(Work(L([-0.94, by1 - 0.12, bz1], [0.94, by1 - 0.06, bz1 + 0.012], MachineLook.Base), "bridge"), "-z")); // (the heads' rail on its front)
        p.Add(Seam(Work(L([-0.94, by0 + 0.06, bz1], [0.94, by0 + 0.12, bz1 + 0.012], MachineLook.Base), "bridge"), "-z"));
        // Its end trucks on the rails: the truck, two wheels on the running strip, buffers at both ends; on the far end the drive motor
        // and the cable chain's loop down into its tray.
        foreach (var (sx, face) in new[] { (1, "+x"), (-1, "-x") })
        {
            double[] Xs(double a, double b) => sx > 0 ? [a, b] : [-b, -a];
            var t = Xs(e - 0.14, e);
            p.Add(Seam(Work(L([t[0], 3.3, -0.98], [t[1], by1 + 0.02, -0.44], Trim, paintable: true), "bridge"), "-z", face));
            var w = Xs(e - 0.1, e - 0.02);
            foreach (var z in new[] { -0.88, -0.54 })
                p.Add(Seam(Work(L([w[0], RailTop + 0.003, z - 0.055], [w[1], RailTop + 0.113, z + 0.055], MachineLook.Base, shape: Cyl(sx > 0 ? "+x" : "-x", 12)), "bridge"), "-z", face));
            var bf = Xs(e - 0.1, e - 0.04);
            foreach (var (z0, z1) in new[] { (-1.0, -0.98), (-0.44, -0.42) })
                p.Add(Seam(Work(L([bf[0], 3.36, z0], [bf[1], 3.42, z1], MachineLook.Accent, shape: Cyl("+z", 10)), "bridge"), "-z", face)); // (buffers)
        }
        p.Add(Seam(Work(L([e - 0.13, by1 + 0.02, -0.82], [e - 0.01, by1 + 0.18, -0.6], MachineLook.Base), "bridge"), "-z", "+x")); // (the drive motor)
        p.Add(Seam(Work(L([e - 0.11, by1 + 0.18, -0.78], [e - 0.03, by1 + 0.21, -0.64], MachineLook.Accent), "bridge"), "-z", "+x"));
        p.Add(Seam(Work(L([e - 0.185, 3.08, -0.66], [e - 0.14, 3.3, -0.6], "#2b3036"), "bridge"), "-z", "+x")); // (the cable chain's loop)
        // This block's head: a yellow carriage on the bridge's front, the head's housing and nozzle under it, a cable over the top; the
        // beam (lit only under the head that is cutting) down to the slats.
        const double hz0 = -0.555, hz1 = -0.405, hz = -0.48;
        p.Add(Seam(Work(L([-0.17, by0 + 0.02, hz0], [0.17, by1 - 0.02, hz1 - 0.02], MachineLook.Accent), "head"), "-z"));
        p.Add(Seam(Work(L([-0.13, by0 + 0.1, hz1 - 0.02], [0.13, by0 + 0.2, hz1 - 0.008], MachineLook.Ink), "head"), "-z")); // (its warning label)
        p.Add(Seam(Work(L([-0.08, 3.05, hz - 0.08], [0.08, by0 + 0.06, hz + 0.08], MachineLook.Base, shape: Cyl("+y", 14)), "head"), "-z"));
        p.Add(Seam(Work(L([-0.095, 3.1, hz - 0.095], [0.095, 3.13, hz + 0.095], MachineLook.Steel, "steel", shape: Cyl("+y", 14)), "head"), "-z")); // (its clamp ring)
        p.Add(Seam(Work(L([-0.035, 2.995, hz - 0.035], [0.035, 3.052, hz + 0.035], MachineLook.Steel, "steel", shape: Cyl("+y", 10)), "head"), "-z")); // (the nozzle)
        p.Add(Seam(Work(L([-0.02, 2.975, hz - 0.02], [0.02, 2.997, hz + 0.02], MachineLook.Hot, shape: Cyl("+y", 8)), "beam"), "-z")); // (its lens, lit while it cuts)
        p.Add(Seam(Work(L([-0.008, Slat + 0.004, hz - 0.008], [0.008, 2.975, hz + 0.008], MachineLook.Hot, shape: Cyl("+y", 6)), "beam"), "-z")); // (the beam)
        p.Add(Seam(Work(L([-0.03, by1 - 0.02, hz0 - 0.05], [0.03, by1 + 0.08, hz1 - 0.05], MachineLook.Base), "head"), "-z")); // (its cable)
        p.Sock("+x", [1, 0.5, 0], "+x");
        p.Sock("-x", [-1, 0.5, 0], "-x");
        p.Sock("+z", [0, 0.5, 1], "+z");
        p.Sock("-z", [0, 0.5, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(2400, 1600, 6, 12);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    private static PyDict? Work(PyDict? b, string kind) => MachineLook.Work(b, kind);

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BedBlock())]);
}
