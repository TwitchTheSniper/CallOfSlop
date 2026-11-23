using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Panel Former on the block system: Constructs/Parts/PanelFormer (design page drawing 15; docs/sandbox/constructs/production.md "The
/// Panel Former"; MachineBlocks.cs). A Panel Former Core holds the machine's brain (screen and queue, Orders, Run, power, the offcut bin,
/// Piece in, where one blank a panel comes in, and the in rollers); identical 2 x 2 m Bed Blocks welded on its far side make the bed, and an Out Table (OutTableParts) closes
/// the bed's far end. The largest panel is the rectangle of blocks. Each Bed Block is a pin bed: a field of steel pins below that rise in a
/// wave to the panel's shape, an upper platen above that comes down and presses the sheet over them. All of that happens behind walls
/// (Sean, 2026-11-23): between the lower bed and the crosshead a block is walled on every free side and nothing inside is drawn, so the
/// panel's forming is hidden. A block's skirts, walls, corner pillars and side fittings are drawn only where no cell of its
/// machine is welded ("seam"), so a bed of blocks reads as one machine: one closed box, pillars at its corners, one oil manifold down one side and
/// a hazard edge round the outside. Born in the Machine look (look.md): three tones, bare steel working parts, white in, orange out.
/// </summary>
public sealed class PanelFormerParts : IPartGenerator
{
    public string Name => "panel_former_parts";

    private const string Out = "Constructs/Parts/PanelFormer";
    private const string Green = "#5d7f3c"; // the Panel Formers' body colour
    private const string Trim = "#46602d";
    private const double E = 0.997; // a cell's body stops 3 mm short of its 2 m, so two cells' faces never meet on one plane
    private const double H = 3.197; // a cell's top (3.2 m: the bed under, the platen and its crosshead over)
    private const double WorkY = OutTableParts.WorkY; // the pins' tops at rest and the in rollers' nip: the sheet's height on the bed
    private const double DeckY = 0.86; // the lower bed's deck the pins come up through
    private const double Head = 2.96; // the crosshead's underside
    private const int BlockPower = 900, CorePower = 1200, CoreBin = 60000; // (the Core's bin is its offcut: what blanks leave over their panels)
    private const double OffX = -0.5, OffY = 2.8; // the Offcut port, on the bin's back wall

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));
    private static PyDict Seam(PyDict? box, string a, string b) => box!.Update(("seam", Py.L(a, b)));
    private static PyDict Fill(PyDict? box, string face) => box!.Update(("seamFill", face));

    private static List<object?> Buttons() => Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.2, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.21, 0.02, 0.39, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.4, 0.02, 0.56, 0.12)), ("label", "Clear")),
        Kw(("action", "machine.recipe"), ("at", Py.L(0.57, 0.02, 0.75, 0.12)), ("label", "Preset")), // (the preset its Queue orders, from the recipe picker)
        Kw(("action", "view.up"), ("at", Py.L(0.76, 0.02, 0.87, 0.12)), ("label", "Up")),
        Kw(("action", "view.down"), ("at", Py.L(0.88, 0.02, 0.98, 0.12)), ("label", "Down")));

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    /// <summary>
    /// The Panel Former Core: one 2 x 2 m cell, 3.2 m tall, at the bed's in end. The offcut bin on its back half (the trim a blank leaves:
    /// out by chute from its Offcut port), Piece in low on its back (a blank a panel off a conveyor), the control face on its left (the
    /// aisle side), and on its front, where the first Bed Block welds, a slot at the work height with the in rollers that feed the blank onto the bed.
    /// </summary>
    private static Part Core()
    {
        var p = new Part("Panel Former", Small, "Extrusion",
            "A Panel Former Core: forms body panels by order, each from one blank, on the bed of Bed Blocks welded to its front (2 x 2 m each, up to 32 x 32 m), an Out Table on the bed's far end. The largest panel is the bed's rectangle. A blank is a flat extruded sheet of the panel's material and thickness, its flat size to 25% over, in by conveyor at the white end; the trim drops into its offcut bin. Needs power.",
            role: "storage", group: "Production", color: Green);
        const double slotLo = WorkY - 0.16, slotHi = WorkY + 0.16, slotD = 0.32, cab = 2.4; // (the in slot's height and depth, the cabinet's top)
        // Solid: the cabinet (its front slotted for the in rollers), the offcut bin's walls on the back half of its top, a deck on the front half.
        p.Add(Box([-E, 0.003, -E], [E, slotLo, E]));
        p.Add(Box([-E, slotLo, -E], [E, slotHi, E - slotD]));
        p.Add(Box([-E, slotLo, E - slotD], [-E + 0.08, slotHi, E])); // (the slot's two cheeks)
        p.Add(Box([E - 0.08, slotLo, E - slotD], [E, slotHi, E]));
        p.Add(Box([-E, slotHi, -E], [E, cab, E]));
        const double hz1 = 0.0, wall = 0.06;
        p.Add(Box([-E, cab, -E], [E, H, -E + wall], Trim));
        p.Add(Box([-E, cab, hz1 - wall], [E, H, hz1], Trim));
        p.Add(Box([-E, cab, -E + wall], [-E + wall, H, hz1 - wall], Trim));
        p.Add(Box([E - wall, cab, -E + wall], [E, H, hz1 - wall], Trim));
        p.Add(Box([-E, cab, hz1], [E, cab + 0.24, E])!.Update(("finish", "tread plate")));
        var bulk = Kw(("material", "any"), ("capacity", CoreBin), ("min", Py.L(R(-E + wall + 0.005), R(cab + 0.005), R(-E + wall + 0.005))), ("max", Py.L(R(E - wall - 0.005), R(H - 0.005), R(hz1 - wall - 0.005))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+z", [0, 0.5, 1], "+z"); // (the first Bed Block: the bed runs on from its front)
        p.Sock("-z", [0, 0.5, -1], "-z"); // (a Roller Tile's run butted on its Piece in: RollerRuns.Butted)
        p.Stats(2400, 2200, 6, 10);
        p.Set(("power", CorePower), ("storage", Kw(("bulk", bulk),
                ("panel", Kw(("speed", 1), ("at", Py.L(0, WorkY, 0)), ("maxWidth", 32), ("maxLength", 32), ("blockPower", BlockPower))))),
            ("machineBlock", Kw(("family", "panel"), ("role", "core"), ("origin", Py.L(0, 1.6, 0)))),
            ("config", Py.L(
                Kw(("key", "recipe"), ("label", "Preset"), ("group", "Orders"), ("options", Py.L([.. HullLoft.PanelPresets.Select(x => (object?)x.Label)])), ("default", 0),
                    ("tooltip", "The panel its Queue button orders, at the preset's own size, in steel; Queue also asks a linked Extruder for its blanks. The Preset button on its screen opens a picker of them.")),
                ExtrusionParts.OrderCount,
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, 0.5, -E)), ("face", "-z"), ("opening", Py.L(32, 0.6))), // (a blank as wide as the widest bed's panel)
                Kw(("name", "Offcut"), ("type", "chute"), ("dir", "out"), ("at", Py.L(OffX, OffY, -E)), ("face", "-z")))),
            ("collider", "boxes"));
        // The control face on its left: screen at a standing eye, a lamp over it, the button shelf under it.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 8, width: 0.84, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Buttons();
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -E, E, -E, E, 0.003, 0.14, 0.012);
        // Piece in on its back, the shared port look, an arrow in at its mouth and the in band (the white side).
        MachineLook.Port(p, [0, 0.5, -E], 2, -1, 0.3, 0.12, true);
        MachineLook.Arrow(p, 2, -E - 0.02, -1, 0, -1, 0.64, 0.5, 0.36, 0.22, MachineLook.In); // (beside the mouth, pointing at it: in)
        p.Add(L([-E - 0.014, 1.1, -E - 0.014], [E + 0.014, 1.22, -E], MachineLook.In)); // (the in band across its back)
        // The offcut bin: a dark rim with a hazard edge, its grating, a level gauge, its Offcut port (the shared port look, orange: out) and its label.
        p.Add(L([-E - 0.004, H - 0.06, -E - 0.006], [E + 0.004, H + 0.004, -E + wall], MachineLook.Base));
        p.Add(L([-E - 0.006, H - 0.06, -E + wall], [-E + wall, H + 0.004, hz1 + 0.004], MachineLook.Base));
        p.Add(L([E - wall, H - 0.06, -E + wall], [E + 0.006, H + 0.004, hz1 + 0.004], MachineLook.Base));
        MachineLook.Hazard(p, [-E - 0.006, H - 0.12, -E - 0.012], [E + 0.006, H - 0.064, -E - 0.006]);
        foreach (var x in new[] { -0.5, 0, 0.5 })
            p.Add(L([x - 0.015, H - 0.04, -E + wall], [x + 0.015, H - 0.012, hz1 - wall], MachineLook.Base));
        MachineLook.Port(p, [OffX, OffY, -E], 2, -1, 0.14, 0.12, false);
        p.Add(L([0.12, cab + 0.12, -E - 0.02], [0.16, H - 0.16, -E], MachineLook.Base)); // (its level gauge's tube, the bin's height)
        p.Add(L([0.105, cab + 0.16, -E - 0.032], [0.175, cab + 0.36, -E - 0.02], MachineLook.Accent)); // (its float)
        p.Add(L([0.3, cab + 0.3, -E - 0.016], [0.8, cab + 0.5, -E], MachineLook.Out)); // (its label: "OFFCUT", out)
        p.Add(L([0.34, cab + 0.37, -E - 0.028], [0.76, cab + 0.43, -E - 0.016], MachineLook.Ink));
        // The in rollers in the slot: lower rollers level with the bed's pins, pinch rollers over them; they turn while it works.
        var rr = 0.06;
        foreach (var z in new[] { E - 0.24, E - 0.09 })
        {
            p.Add(MachineLook.Work(L([-E + 0.084, WorkY - 2 * rr, z - rr], [E - 0.084, WorkY, z + rr], MachineLook.Steel, "steel", shape: Cyl("+x")), "spin", "+x"));
            p.Add(MachineLook.Work(L([-E + 0.09, WorkY + 0.012, z - rr * 0.8], [E - 0.09, WorkY + 0.012 + 1.6 * rr, z + rr * 0.8], MachineLook.Accent, shape: Cyl("+x")), "spin", "-x"));
        }
        p.Add(L([-E + 0.084, slotLo + 0.004, E - slotD + 0.004], [E - 0.084, WorkY - 2 * rr - 0.01, E - 0.01], MachineLook.Base)); // (the rollers' trough)
        // Over the slot: hazard paint (it takes hands as well as sheet), the orange band of the bed's way and a lintel with bolts.
        MachineLook.Hazard(p, [-E + 0.08, slotHi + 0.02, E], [E - 0.08, slotHi + 0.14, E + 0.012]);
        MachineLook.Hazard(p, [-E + 0.08, slotLo - 0.14, E], [E - 0.08, slotLo - 0.02, E + 0.012]);
        p.Add(L([-E, 2.0, E], [E, 2.12, E + 0.014], MachineLook.Out));
        MachineLook.Arrow(p, 2, E + 0.014, 1, 0, 1, 0, 2.06, 0.5, 0.08, MachineLook.Ink);
        foreach (var x in new[] { -0.8, -0.4, 0.4, 0.8 })
            p.Add(L([x - 0.02, slotHi + 0.17, E - 0.004], [x + 0.02, slotHi + 0.21, E + 0.016], MachineLook.Steel));
        // The hydraulic power unit on its right side: a tank, the pump's motor and its fan turning, the pressure line up to the deck.
        p.Add(L([E, 0.14, -0.85], [E + 0.24, 0.8, -0.15], MachineLook.Base));
        p.Add(L([E + 0.24, 0.2, -0.75], [E + 0.29, 0.74, -0.25], Trim, paintable: true)); // (the tank's sight panel)
        p.Add(L([E + 0.248, 0.3, -0.6], [E + 0.3, 0.62, -0.56], "#3a8fd0")); // (its oil level, behind glass)
        p.Add(L([E, 0.2, 0.0], [E + 0.3, 0.56, 0.36], MachineLook.Base, shape: Cyl("+x", 14))); // (the motor)
        p.Add(MachineLook.Work(L([E + 0.3, 0.24, 0.04], [E + 0.34, 0.52, 0.32], MachineLook.Accent, shape: Cyl("+x", 10)), "spin", "+x")); // (its fan cowl's spinner)
        p.Add(L([E + 0.06, 0.8, -0.5], [E + 0.12, 2.38, -0.44], MachineLook.Steel, "steel")); // (the pressure line, up)
        p.Add(L([E + 0.04, 1.3, -0.54], [E + 0.14, 1.36, -0.4], MachineLook.Base)); // (its clamps)
        p.Add(L([E + 0.04, 1.9, -0.54], [E + 0.14, 1.96, -0.4], MachineLook.Base));
        p.Add(L([E + 0.02, 1.0, -0.36], [E + 0.06, 1.24, -0.12], "#e8e6df", shape: Cyl("+x"))); // (its pressure gauge)
        p.Add(L([E + 0.06, 1.1, -0.25], [E + 0.075, 1.2, -0.23], MachineLook.Ink));
        // The electrical cabinet on its right, over the pump: a door with its frame, louvres, a handle and a warning plate.
        p.Add(L([E, 1.0, 0.1], [E + 0.03, 2.2, 0.85], Trim, paintable: true));
        p.Add(L([E + 0.03, 1.04, 0.14], [E + 0.046, 2.16, 0.81], Green, paintable: true));
        foreach (var y in new[] { 1.85, 1.92, 1.99 })
            p.Add(L([E + 0.046, y, 0.24], [E + 0.06, y + 0.03, 0.7], MachineLook.Base));
        p.Add(L([E + 0.046, 1.45, 0.72], [E + 0.08, 1.6, 0.76], MachineLook.Accent));
        p.Add(L([E + 0.046, 1.2, 0.3], [E + 0.06, 1.4, 0.55], MachineLook.Accent, "hazard paint")); // (high voltage)
        // Cable runs: from the cabinet up the right side to the deck, and along the deck's edge to the front (the bed's power).
        p.Add(L([E + 0.006, 2.2, 0.4], [E + 0.08, H - 0.4, 0.5], MachineLook.Base));
        p.Add(L([0.6, cab + 0.24, hz1 + 0.06], [0.72, cab + 0.3, E - 0.02], MachineLook.Base));
        // Cladding: sunk-panel frames on its back and right (the body's tone, so they take its paint), a maker's plate on its back.
        foreach (var x in new[] { -0.5, 0.5 })
            p.Add(L([x - 0.03, 0.16, -E - 0.016], [x + 0.03, 2.38, -E], Trim, paintable: true));
        p.Add(L([-E, 2.26, -E - 0.016], [E, 2.34, -E], Trim, paintable: true));
        p.Add(L([0.55, 0.7, -E - 0.024], [0.85, 0.9, -E], MachineLook.In));
        p.Add(L([0.58, 0.78, -E - 0.036], [0.82, 0.82, -E - 0.02], MachineLook.Ink));
        // On its deck: the screw jacks' drive box and a beacon by the lamp's side, and a crush warning on the deck's front edge.
        p.Add(L([-0.85, cab + 0.24, 0.3], [-0.35, cab + 0.6, 0.8], MachineLook.Base));
        p.Add(MachineLook.Work(L([-0.35, cab + 0.34, 0.45], [-0.25, cab + 0.5, 0.65], MachineLook.Accent, shape: Cyl("+x", 10)), "spin", "+x"));
        p.Add(L([0.2, cab + 0.24, 0.5], [0.4, cab + 0.6, 0.7], MachineLook.Base, shape: Cyl("+y")));
        p.Add(MachineLook.Work(L([0.18, cab + 0.6, 0.48], [0.42, cab + 0.66, 0.72], MachineLook.Base, shape: Cyl("+y")), "smoke")); // (the oil cooler's vent: it steams while it works)
        MachineLook.Hazard(p, [-E, cab + 0.12, E], [E, cab + 0.22, E + 0.012]);
        return p;
    }

    /// <summary>
    /// A Bed Block: one 2 x 2 m cell of a Panel Former's bed, 3.2 m tall. A lower bed and a crosshead, walled between on every free side
    /// (the pins and platen inside are not drawn), and its own power. Welds to the Core, to another block's four sides,
    /// or to an Out Table.
    /// </summary>
    private static Part BedBlock()
    {
        var p = new Part("Bed Block", Small, "Extrusion",
            "Weld it on a Panel Former Core's front, or beside another Bed Block: 2 x 2 m more bed. The bed is the largest full rectangle of blocks next to the Core, an Out Table on its far end; a block outside it idles.",
            group: "Production", color: Green);
        const double d = 0.07, rib = 0.13; // (the lower body's sides are sunk between ribs: its frame at the edges and a cross rib at the middle)
        // Solid: the lower bed (a sunk body, framed by ribs) and the crosshead over it. Between them only the walls are drawn.
        p.Add(Box([-E + d, 0.003, -E + d], [E - d, DeckY, E - d]));
        foreach (var (a, b) in new[] { (-E, -E + rib), (-0.065, 0.065), (E - rib, E) })
        {
            p.Add(Box([-E, 0.003, a], [E, DeckY, b]));
            p.Add(Box([a, 0.003, -E], [b, DeckY, E]));
        }
        p.Add(Box([-E, Head, -E], [E, H, E], Trim)!.Update(("paintable", true)));
        // Where a block is welded on, a fill in the body's tone closes the sunk panels, so no slot runs between two blocks.
        foreach (var (side, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            double[] mn = [-E + rib, 0.003, -E + rib], mx = [E - rib, DeckY - 0.004, E - rib];
            (mn[n], mx[n]) = sg > 0 ? (E - d - 0.01, E - 0.004) : (-E + 0.004, -E + d + 0.01);
            p.Add(Fill(L(mn, mx, Green, paintable: true), side));
        }
        // Closed (Sean, 2026-11-23): between the lower bed and the crosshead the block is walled on every free side, so nothing of the
        // forming shows (no pins, platen or ram are drawn inside: what is hidden costs no draw). The panel being formed is hidden with it;
        // the bed's two ends, welded to the Core and an Out Table, are closed by PanelFormerRender's end curtains.
        // The ram's head on the crosshead stays (it's outside).
        p.Add(Seam(L([-0.22, H - 0.004, -0.22], [0.22, H + 0.52, 0.22], MachineLook.Base, shape: Cyl("+y", 16)), "+y"));
        p.Add(Seam(L([-0.25, H + 0.52, -0.25], [0.25, H + 0.58, 0.25], MachineLook.Steel, "steel", shape: Cyl("+y", 16)), "+y"));
        foreach (var (x, z) in new[] { (0.2, 0.2), (-0.2, 0.2), (0.2, -0.2), (-0.2, -0.2) })
            p.Add(Seam(L([x - 0.018, H + 0.58, z - 0.018], [x + 0.018, H + 0.61, z + 0.018], MachineLook.Base), "+y")); // (its head's bolts)
        p.Add(L([0.3, H - 0.004, -0.04], [0.85, H + 0.06, 0.04], MachineLook.Steel, "steel")); // (its oil line along the crosshead)
        // A corner pillar where two free sides meet (the bed's outer corners), closing the walls' corner.
        foreach (var (sx, xs) in new[] { (1, "+x"), (-1, "-x") })
            foreach (var (sz, zs) in new[] { (1, "+z"), (-1, "-z") })
            {
                double[] X(double a, double b) => sx > 0 ? [a, b] : [-b, -a];
                double[] Z(double a, double b) => sz > 0 ? [a, b] : [-b, -a];
                var px = X(E - 0.06, E + 0.024);
                var pz = Z(E - 0.06, E + 0.024);
                p.Add(Seam(L([px[0], DeckY - 0.008, pz[0]], [px[1], Head + 0.014, pz[1]], MachineLook.Base), xs, zs));
            }
        // On each side, drawn while nothing of its machine is welded there: the skirt, the hazard edge along the bed's lip, louvres in the
        // sunk panels, bolts along the crosshead and a cable tray on it.
        foreach (var (side, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            int u = 2 - n;
            PyDict? Face(double out0, double out1, double u0, double u1, double y0, double y1, string colour, string? finish = null)
            {
                double[] mn = new double[3], mx = new double[3];
                (mn[n], mx[n]) = sg > 0 ? (E + out0, E + out1) : (-E - out1, -E - out0);
                (mn[u], mx[u]) = (u0, u1);
                (mn[1], mx[1]) = (y0, y1);
                return L(mn, mx, colour, finish);
            }
            p.Add(Seam(Face(0, 0.014, -E, E, 0.003, 0.14, MachineLook.Base), side));
            // The wall from the lower bed's lip to the crosshead, two stiffeners on it and a crush warning plate.
            p.Add(Seam(Face(-0.03, 0.006, -E - 0.002, E + 0.002, DeckY - 0.01, Head + 0.012, Green, null)!.Update(("paintable", true)), side));
            foreach (var t in new[] { -0.5, 0.5 })
                p.Add(Seam(Face(0.006, 0.03, t - 0.04, t + 0.04, DeckY + 0.06, Head - 0.04, Trim, null)!.Update(("paintable", true)), side));
            p.Add(Seam(Face(0.006, 0.018, -0.3, 0.3, 1.86, 2.1, MachineLook.Accent, "hazard paint"), side));
            p.Add(Seam(Face(0.018, 0.03, -0.22, 0.22, 1.95, 2.01, MachineLook.Ink), side));
            p.Add(Seam(Face(0, 0.014, -E + 0.02, E - 0.02, DeckY - 0.12, DeckY - 0.02, MachineLook.Accent, "hazard paint"), side));
            foreach (var (a, b) in new[] { (-E + rib + 0.06, -0.065 - 0.06), (0.065 + 0.06, E - rib - 0.06) })
                foreach (var y in new[] { 0.36, 0.46, 0.56 })
                    p.Add(Seam(Face(-d + 0.004, -d + 0.03, a, b, y, y + 0.04, MachineLook.Base), side)); // (louvres in the sunk panel)
            foreach (var t in new[] { -0.75, -0.25, 0.25, 0.75 })
                p.Add(Seam(Face(-0.004, 0.012, t - 0.02, t + 0.02, Head + 0.1, Head + 0.14, MachineLook.Steel), side));
            p.Add(Seam(Face(-0.004, 0.014, -E, E, Head + 0.016, Head + 0.04, MachineLook.Out), side)); // (the out side's band along its crosshead)
        }
        // The oil manifold down its right side (one side of the bed): a pipe with a valve block, a gauge and a hose up to the ram.
        p.Add(Seam(L([E + 0.01, 0.48, -E + 0.004], [E + 0.09, 0.56, E - 0.004], MachineLook.Steel, "steel", shape: Cyl("+z")), "+x"));
        p.Add(Seam(L([E + 0.004, 0.4, -0.22], [E + 0.14, 0.64, 0.02], MachineLook.Base), "+x"));
        p.Add(Seam(L([E + 0.14, 0.44, -0.18], [E + 0.18, 0.6, -0.02], MachineLook.Accent), "+x")); // (its valve's hand wheel)
        p.Add(Seam(L([E + 0.06, 0.64, 0.08], [E + 0.11, DeckY + 1.3, 0.13], MachineLook.Base), "+x")); // (the hose up)
        p.Add(Seam(L([E + 0.004, 0.68, 0.3], [E + 0.03, 0.84, 0.46], "#e8e6df", shape: Cyl("+x")), "+x")); // (its gauge)
        p.Add(Seam(L([E + 0.03, 0.75, 0.37], [E + 0.044, 0.82, 0.39], MachineLook.Ink), "+x"));
        // A cable tray along its left side's crosshead (the bed's power from the Core), and a maker's plate under it.
        p.Add(Seam(L([-E - 0.1, Head + 0.004, -E + 0.004], [-E - 0.004, Head + 0.08, E - 0.004], MachineLook.Base), "-x"));
        p.Add(Seam(L([-E - 0.024, 0.66, 0.3], [-E - 0.004, 0.8, 0.6], MachineLook.In), "-x"));
        p.Add(Seam(L([-E - 0.036, 0.71, 0.33], [-E - 0.022, 0.75, 0.57], MachineLook.Ink), "-x"));
        p.Sock("+x", [1, 0.5, 0], "+x");
        p.Sock("-x", [-1, 0.5, 0], "-x");
        p.Sock("+z", [0, 0.5, 1], "+z");
        p.Sock("-z", [0, 0.5, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, 3.2, 0], "+y"); // (a Mast Block on each of the bed's corners: 2 m more stroke a level, ConstructPanelFormer.PinDepth)
        p.Stats(5200, 2600, 8, 20);
        p.Set(("machineBlock", Kw(("family", "panel"), ("role", "block"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BedBlock())]);
}
