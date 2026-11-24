using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Panel Former on the block system, a stretch former (Sean, 2026-11-24: the Panel Former Lab): Constructs/Parts/PanelFormer
/// (docs/sandbox/constructs/production.md "The Panel Former"; MachineBlocks.cs). A slim "Stack" Core holds the machine's brain (screen and
/// queue, Orders, Run, power, the offcut bin, Piece in) and stands beside the bed's head row, its screen facing the in-feed, tied to the
/// bed's side by a low ribbed base link under the parked side jaw. Identical 2 x 2 m Bed Blocks welded beside it make the bed, a Roller
/// Tile run butted on its head rolls each blank in at 1 m (the Core's Bed in) and an Out Table closes its far end. Everything over and in
/// the bed is drawn across the whole rectangle by PanelFormerRender, as one machine: roller lanes along both long sides at the roll line,
/// the post zones outside them with the side jaws' out-rigger rails through slots, the closed pocket under the floor between the lanes, the
/// die of 0.3 m columns that rises from it, the swing-out end gates and the side jaws. A Bed Block's own boxes are its collision (solid to
/// the roll line, as a Press Bed Block is), the skirt round the bed's outline and, on a block that is in no machine, a plain bed cell's look.
/// Born in the Machine look (look.md): three tones, bare steel working parts, white in, orange out.
/// </summary>
public sealed class PanelFormerParts : IPartGenerator
{
    public string Name => "panel_former_parts";

    private const string Out = "Constructs/Parts/PanelFormer";
    private const string Green = "#5d7f3c"; // the Panel Formers' body colour
    private const string Trim = "#46602d";
    private const double E = 0.997; // a cell's body stops 3 mm short of its 2 m, so two cells' faces never meet on one plane
    private const double WorkY = OutTableParts.WorkY; // the roll line: the lanes' rollers' tops, the sheet's height on the bed
    private const int BlockPower = 900, CorePower = 1200, CoreBin = 60000; // (the Core's bin is its offcut: what blanks leave over their panels)
    // The Stack Core in its own frame: the bed is on its +z side (the base link runs to it), the in-feed on its -x side (the screen faces
    // it), its back (-z) the aisle. Its column and the modules bolted on it:
    private const double Back = -0.37, Front = 0.25; // the modules' back and bed-side faces (z)
    private const double Deck = 2.5, CageTop = 3.2; // the tread deck's underside, the trim cage's top (the offcut bin inside the cage)
    private const double OffX = -0.2, OffY = 2.97; // the Offcut port, on the cage's back wall

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));
    private static PyDict Bare(PyDict? box) => box!.Update(("bare", true));

    private static List<object?> Buttons() => Py.L(
        Kw(("action", "orders.queue"), ("at", Py.L(0.02, 0.02, 0.2, 0.12)), ("label", "Queue")),
        Kw(("action", "orders.cancel"), ("at", Py.L(0.21, 0.02, 0.39, 0.12)), ("label", "Cancel")),
        Kw(("action", "orders.clear"), ("at", Py.L(0.4, 0.02, 0.56, 0.12)), ("label", "Clear")),
        Kw(("action", "machine.recipe"), ("at", Py.L(0.57, 0.02, 0.75, 0.12)), ("label", "Preset")), // (the preset its Queue orders, from the recipe picker)
        Kw(("action", "view.up"), ("at", Py.L(0.76, 0.02, 0.87, 0.12)), ("label", "Up")),
        Kw(("action", "view.down"), ("at", Py.L(0.88, 0.02, 0.98, 0.12)), ("label", "Down")));

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    /// <summary>
    /// The Panel Former Core, the "Stack" (Sean, 2026-11-24): one 2 x 2 m cell beside the bed's head row. Modules bolted on a lattice
    /// column: a low module (the controls' cabinet, the hydraulic unit behind it), the screen module over it, its screen facing the
    /// in-feed (-x), a tread deck and trim cage on top holding the offcut bin, a beacon on the cage, hazard bands. A low ribbed base link
    /// with its hose and cable runs ties it to the bed's side (+z) under the parked side jaw. Piece in (a blank by conveyor or arm) on its
    /// back; a run butted on the bed's head is the usual way in (the Core's Bed in).
    /// </summary>
    private static Part Core()
    {
        var p = new Part("Panel Former", Small, "Extrusion",
            "A Panel Former Core: a stretch former that forms body panels by order, each from one blank, on the bed of Bed Blocks welded beside it (2 x 2 m each, up to 32 x 32 m), its Core beside the bed's head and an Out Table on the bed's far end. A Roller Tile run butted on the bed's head rolls each blank in; the jaws grip it and a die of columns rises from the pocket under the bed and stretches it to shape. The largest panel is the bed's rectangle. A blank is a flat extruded sheet of the panel's material and thickness, its flat size to 25% over; the trim drops into its offcut bin. Needs power.",
            role: "storage", group: "Production", color: Green);
        // Solid: the column, the base link to the bed, the two modules and the tread deck under the cage.
        p.Add(Box([-0.25, 0.003, -0.2], [0.25, Deck, 0.1], Trim));
        p.Add(Box([-0.6, 0.003, 0.05], [0.6, 0.28, E], MachineLook.Base)); // (the base link: under the parked side jaw, out to the bed's side)
        p.Add(Box([-0.9, 0.35, Back], [0.9, 1.15, Front])); // (the low module: the controls' cabinet)
        p.Add(Box([-0.7, 1.5, -0.42], [0.4, 2.3, 0.3])); // (the screen module, facing the in-feed)
        p.Add(Box([-0.45, Deck, Back], [0.45, Deck + 0.24, Front])!.Update(("finish", "tread plate")));
        // The trim cage on the deck: the offcut bin's walls.
        const double wall = 0.06, cy = Deck + 0.24;
        p.Add(Box([-0.45, cy, Back], [0.45, CageTop, Back + wall], Trim));
        p.Add(Box([-0.45, cy, Front - wall], [0.45, CageTop, Front], Trim));
        p.Add(Box([-0.45, cy, Back + wall], [-0.45 + wall, CageTop, Front - wall], Trim));
        p.Add(Box([0.45 - wall, cy, Back + wall], [0.45, CageTop, Front - wall], Trim));
        var bulk = Kw(("material", "any"), ("capacity", CoreBin), ("min", Py.L(R(-0.45 + wall + 0.005), R(cy + 0.005), R(Back + wall + 0.005))), ("max", Py.L(R(0.45 - wall - 0.005), R(CageTop - 0.005), R(Front - wall - 0.005))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+z", [0, 0.5, 1], "+z"); // (the bed's head row: the Core stands beside it)
        p.Stats(1800, 2200, 6, 10);
        p.Set(("power", CorePower), ("storage", Kw(("bulk", bulk),
                ("panel", Kw(("speed", 1), ("at", Py.L(0, WorkY, 0)), ("maxWidth", 32), ("maxLength", 32), ("blockPower", BlockPower))))),
            ("machineBlock", Kw(("family", "panel"), ("role", "core"), ("origin", Py.L(0, 1.0, 0)))), // (its cell's middle level with the low Bed Blocks')
            ("config", Py.L(
                Kw(("key", "recipe"), ("label", "Preset"), ("group", "Orders"), ("options", Py.L([.. HullLoft.PanelPresets.Select(x => (object?)x.Label)])), ("default", 0),
                    ("tooltip", "The panel its Queue button orders, at the preset's own size, in steel; Queue also asks a linked Extruder for its blanks. The Preset button on its screen opens a picker of them.")),
                ExtrusionParts.OrderCount,
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0.5, 0.75, Back)), ("face", "-z"), ("opening", Py.L(32, 0.6))), // (a blank as wide as the widest bed's panel)
                Kw(("name", "Offcut"), ("type", "chute"), ("dir", "out"), ("at", Py.L(OffX, OffY, Back)), ("face", "-z")))),
            ("collider", "boxes"));
        // The control face on the screen module's in-feed side: screen at a standing eye, a lamp over it, the button shelf under it.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 6, width: 0.6, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Buttons();
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        // The column: a foot plate, its lattice rings where no module covers it, bolt flanges where each module is bolted on.
        p.Add(L([-0.9, 0.006, -0.65], [0.9, 0.06, Front + 0.004], MachineLook.Base));
        foreach (var y in new[] { 0.16, 1.22, 1.38, 2.36 })
            p.Add(L([-0.27, y, -0.222], [0.27, y + 0.05, 0.122], MachineLook.Base));
        foreach (var x in new[] { -0.2, 0.2 })
            foreach (var y in new[] { 0.3, 1.45 })
                p.Add(L([x - 0.03, y - 0.004, 0.1], [x + 0.03, y + 0.06, 0.126], MachineLook.Steel)); // (the modules' bolts into the column's face)
        // The base link: a hazard top, three ribs across it, and the hose and cable runs along it into the bed's side.
        p.Add(L([-0.596, 0.28, 0.054], [0.596, 0.3, 0.993], MachineLook.Accent, "hazard paint"));
        foreach (var x in new[] { -0.45, 0.0, 0.45 })
            p.Add(L([x - 0.04, 0.006, 0.06], [x + 0.04, 0.32, 0.99], Trim));
        p.Add(L([0.15, 0.302, 0.11], [0.25, 0.402, 0.99], MachineLook.Accent, shape: Cyl("+z"))); // (the oil hose)
        p.Add(L([0.26, 0.302, 0.11], [0.4, 0.442, 0.99], MachineLook.Ink, shape: Cyl("+z"))); // (the power cable)
        p.Add(L([0.15, 0.302, 0.11], [0.25, 0.346, 0.21], MachineLook.Accent, shape: Cyl("+y"))); // (the hose's riser up into the low module)
        // The low module: a hazard band round its foot, its cabinet door on the back with louvres, a handle and a warning plate.
        p.Add(L([-0.912, 0.35, Back - 0.012], [0.912, 0.45, Front + 0.012], MachineLook.Accent, "hazard paint"));
        p.Add(L([0.1, 0.5, Back - 0.03], [0.82, 1.1, Back], Trim, paintable: true));
        p.Add(L([0.14, 0.54, Back - 0.046], [0.78, 1.06, Back - 0.03], Green, paintable: true));
        foreach (var y in new[] { 0.9, 0.96, 1.02 })
            p.Add(L([0.24, y, Back - 0.06], [0.68, y + 0.03, Back - 0.046], MachineLook.Base));
        p.Add(L([0.7, 0.7, Back - 0.08], [0.74, 0.84, Back - 0.046], MachineLook.Accent));
        p.Add(L([0.22, 0.6, Back - 0.06], [0.42, 0.76, Back - 0.046], MachineLook.Accent, "hazard paint")); // (high voltage)
        // Piece in on the low module's back: the shared port look, white (in), an arrow in at its mouth.
        MachineLook.Port(p, [0.5, 0.75, Back], 2, -1, 0.14, 0.1, true);
        // The hydraulic unit behind the low module, on the foot plate: a tank with its sight panel, the pump's motor and its fan turning,
        // the pressure line up to the screen module, its gauge.
        p.Add(L([-0.85, 0.06, -0.62], [-0.3, 0.8, Back + 0.004], MachineLook.Base));
        p.Add(L([-0.8, 0.12, -0.636], [-0.35, 0.74, -0.62], Trim, paintable: true));
        p.Add(L([-0.7, 0.3, -0.648], [-0.64, 0.62, -0.636], "#3a8fd0")); // (its oil level, behind glass)
        p.Add(L([-0.26, 0.062, -0.6], [0.04, 0.362, -0.4], MachineLook.Base, shape: Cyl("+x", 14))); // (the motor)
        p.Add(MachineLook.Work(L([0.04, 0.09, -0.58], [0.08, 0.33, -0.42], MachineLook.Accent, shape: Cyl("+x", 10)), "spin", "+x")); // (its fan cowl's spinner)
        p.Add(L([-0.62, 0.8, -0.44], [-0.56, 1.5, -0.38], MachineLook.Steel, "steel")); // (the pressure line, up)
        p.Add(L([-0.64, 1.0, -0.452], [-0.54, 1.06, -0.37], MachineLook.Base)); // (its clamp)
        p.Add(L([-0.72, 0.84, -0.66], [-0.54, 1.02, -0.62], "#e8e6df", shape: Cyl("-z"))); // (its pressure gauge)
        p.Add(L([-0.64, 0.92, -0.672], [-0.62, 1.0, -0.66], MachineLook.Ink));
        // Between the modules: two cable loops; the screen module's orange band, bolts on its back.
        foreach (var x in new[] { 0.15, 0.3 })
            p.Add(L([x - 0.03, 1.153, 0.15], [x + 0.03, 1.497, 0.21], MachineLook.Ink, shape: Cyl("+y")));
        p.Add(L([-0.712, 2.15, -0.432], [0.412, 2.23, 0.312], MachineLook.Out));
        foreach (var x in new[] { -0.55, 0.25 })
            foreach (var y in new[] { 1.6, 2.05 })
                p.Add(L([x - 0.025, y - 0.025, -0.45], [x + 0.025, y + 0.025, -0.42], MachineLook.Steel, shape: Cyl("-z")));
        // The cage on the deck: a dark rim with a hazard edge, its grating, a level gauge, its Offcut port (orange: out) and its label.
        p.Add(L([-0.454, CageTop - 0.06, Back - 0.006], [0.454, CageTop + 0.004, Back + wall], MachineLook.Base));
        p.Add(L([-0.454, CageTop - 0.06, Front - wall], [0.454, CageTop + 0.004, Front + 0.006], MachineLook.Base));
        MachineLook.Hazard(p, [-0.456, CageTop - 0.12, Back - 0.012], [0.456, CageTop - 0.064, Back - 0.006]);
        foreach (var x in new[] { -0.2, 0.0, 0.2 })
            p.Add(L([x - 0.015, CageTop - 0.04, Back + wall], [x + 0.015, CageTop - 0.012, Front - wall], MachineLook.Base));
        MachineLook.Port(p, [OffX, OffY, Back], 2, -1, 0.1, 0.1, false);
        p.Add(L([0.32, cy + 0.04, Back - 0.02], [0.36, CageTop - 0.14, Back], MachineLook.Base)); // (its level gauge's tube)
        p.Add(L([0.305, cy + 0.06, Back - 0.032], [0.375, cy + 0.2, Back - 0.02], MachineLook.Accent)); // (its float)
        p.Add(L([0.05, cy + 0.06, Back - 0.016], [0.28, cy + 0.2, Back], MachineLook.Out)); // (its label: "OFFCUT", out)
        p.Add(L([0.07, cy + 0.11, Back - 0.028], [0.26, cy + 0.15, Back - 0.016], MachineLook.Ink));
        // A beacon on the cage's bed-side corner, turning while the machine is on.
        p.Add(L([0.28, CageTop + 0.003, Front - 0.1], [0.42, CageTop + 0.05, Front + 0.004], MachineLook.Base));
        p.Add(MachineLook.Work(L([0.3, CageTop + 0.05, Front - 0.09], [0.4, CageTop + 0.2, Front - 0.01], MachineLook.Out, shape: Cyl("+y", 10)), "spin", "+y"));
        // A maker's plate on the screen module's bed side, the in band round the low module's in-feed end.
        p.Add(L([-0.5, 1.8, 0.3], [-0.2, 2.0, 0.316], MachineLook.In));
        p.Add(L([-0.47, 1.88, 0.316], [-0.23, 1.92, 0.328], MachineLook.Ink));
        p.Add(L([-0.916, 0.98, Back - 0.016], [-0.9, 1.1, Front + 0.016], MachineLook.In));
        return p;
    }

    /// <summary>
    /// A Bed Block: one 2 x 2 m cell of a Panel Former's bed. Solid to the roll line (a person stands on the bed, a dropped panel lies on
    /// it), a skirt round the bed's outline; the lanes, post zones, pocket, die and jaws over a machine's whole bed are PanelFormerRender's.
    /// A block that is in no machine draws a plain bed cell (its "bare" boxes) so it never stands invisible.
    /// </summary>
    private static Part BedBlock()
    {
        var p = new Part("Bed Block", Small, "Extrusion",
            "Weld it beside a Panel Former Core, or beside another Bed Block: 2 x 2 m more bed. The bed is the largest full rectangle of blocks next to the Core, an Out Table on its far end; a block outside it idles. Its roller lanes, die pocket and jaws are the machine's, drawn across the whole bed.",
            group: "Production", color: Green);
        const double top = WorkY - 0.004; // (solid to 4 mm under the rollers' tops, where a piece's underside rides)
        p.Add(Box([-E, 0.003, -E], [E, top, E], hidden: true));
        // The skirt along the bed's outline: drawn on each face no cell of its machine is welded on.
        foreach (var (side, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            int u = 2 - n;
            double[] mn = new double[3], mx = new double[3];
            (mn[n], mx[n]) = sg > 0 ? (E, E + 0.014) : (-E - 0.014, -E);
            (mn[u], mx[u]) = (-E, E);
            (mn[1], mx[1]) = (0.006, 0.14);
            p.Add(Seam(L(mn, mx, MachineLook.Base), side));
        }
        // A block in no machine: a plain bed cell, a green body under a tread deck with a hazard edge.
        p.Add(Bare(L([-E + 0.004, 0.006, -E + 0.004], [E - 0.004, top - 0.03, E - 0.004], Green, paintable: true)));
        p.Add(Bare(L([-E + 0.008, top - 0.03, -E + 0.008], [E - 0.008, top - 0.004, E - 0.008], MachineLook.Steel, "tread plate")));
        p.Add(Bare(L([-E, top - 0.16, -E - 0.004], [E, top - 0.08, E + 0.004], MachineLook.Accent, "hazard paint")));
        p.Sock("+x", [1, 0.5, 0], "+x");
        p.Sock("-x", [-1, 0.5, 0], "-x");
        p.Sock("+z", [0, 0.5, 1], "+z");
        p.Sock("-z", [0, 0.5, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(3600, 2600, 8, 20);
        p.Set(("machineBlock", Kw(("family", "panel"), ("role", "block"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BedBlock())]);
}
