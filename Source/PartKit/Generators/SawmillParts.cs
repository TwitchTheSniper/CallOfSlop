using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;
using SW = CallOfSlop.SawmillWorks;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sawmill on the block system (docs/sandbox/constructs/production.md "Refiners as block machines", "The Sawmill"; the design "Smelters
/// and refiners as block machines", drawing "Sawmill"; MachineBlocks.Squares, ConstructRefining.RefineOf, SawmillWorks, SawmillRender):
/// three parts in Constructs/Parts/Refining, beside the Smelter's and the Foundry's (this writes its files one by one and owns no folder).
/// <list type="bullet">
/// <item>The Sawmill Core, 2 x 2 x 2 m: a log bunker on top takes Chute in, the screen on the front (-z), Chute out on its free side (-x),
/// and a static cyclone tower on its back half, 7.6 m to its top, where every headrig's sawdust duct ends. Nothing on it moves. On its own
/// it is the old tier 1 Sawmill: 4 planks a second (wood to planks, planks to cloth).</item>
/// <item>The Saw Block, 2 x 2 m and 1 m tall: a steel bed, no power of its own, welded on the Core's +x side: the Core splits its blocks
/// into squares (1 x 1 up to 5 x 5) and each is one band mill. Alone (a 1 x 1, or loose) it carries its own small band mill
/// (SawmillWorks at n 1, "alone" boxes): the column, the upper wheel spinning ("spin"), the band, the track and a carriage that strokes
/// its log through the band ("stroke"). In a square of 2 or more its outer faces' dressing makes one bed ("seam"); the headrig, the
/// carriage, the track, the log deck, the plank chain and the ducts are drawn by SawmillRender.</item>
/// <item>The Plank Stacker, 2 x 2 m, the optional end piece (family "sawmill"): one per row past a square's far +x edge. Its pile grows
/// and clears (drawn by SawmillRender). Looks only.</item>
/// </list>
/// The old tier 2 to 5 Sawmills went (a saved one loads as the Core: ConstructParts.Renamed).
/// </summary>
public sealed class SawmillParts : IPartGenerator
{
    public string Name => "sawmill_parts";

    private const string Out = "Constructs/Parts/Refining";
    public const string Body = "#5e6b3a"; // the Sawmills' colour: a mill green on the Core's cladding and the beds' plates
    private const string Trim = "#3f4927";
    public const string Bark = "#5b3f28", Grain = "#c49a63", Plank = "#d2ab72", Dust = "#b89a6e";
    public const double SockY = 0.25; // the side sockets' height on the Core, a block and the Stacker (they weld at one height)
    public const double BlockTop = SW.BlockTop, CoreTop = 2.0, DeckTop = SW.Deck;
    public const int OutBin = 400, InBin = 200; // the Core's bins at x1 (the old tier 1 Sawmill's), grown by its squares' rate
    private const int MaxCells = 50, MaxSide = 10;

    private static double R3(double v) => Math.Round(v, 3);

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static PyDict Taper(string front, double taper, int segments = 20) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front), ("taper", Py.L(taper, taper)));

    private static PyDict Tube(string front, double inner, int segments = 24) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front), ("inner", inner));

    private static PyDict? Seam(PyDict? box, params string[] faces) => box?.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    private static PyDict? Fill(PyDict? box, string face) => box?.Update(("seamFill", face));

    private static PyDict? Alone(PyDict? box) => box?.Update(("alone", true));


    private static PyDict? Work(PyDict? b, string kind, string? axis = null, double travel = 0) => MachineLook.Work(b, kind, axis, travel);

    // ---- The Core ------------------------------------------------------------------------------------------------------------------

    private static Part Core()
    {
        var p = new Part("Sawmill", Small, "Sawmills",
            "A Sawmill Core: saws wood into planks and planks into cloth. On its own it makes 4 planks a second. Weld Saw Blocks on its +x side: they split into square band mills (1 x 1 up to 5 x 5) that all saw at once, a bigger square more a block (a 3 x 3 makes it x16.75), up to 50 blocks. Its cyclone takes their sawdust. Needs power: a share for it and each block.",
            role: "storage", group: "Production", color: Body);
        p.Set(("tier", 1)); // (a tier 1 machine's recipe: nothing a tier 1 machine can't make, so survival can start; Assembling.LongTail)
        const double e = 0.997;
        // Solid: the plinth, the body between four corner posts, the top deck, the log bunker (Chute in on its mouth) and the cyclone tower.
        p.Add(Box([-e, 0.003, -e], [e, 0.14, e]));
        p.Add(Box([-0.94, 0.14, -0.94], [0.94, 1.86, 0.94]));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? e - 0.12 : -e, z0 = sz > 0 ? e - 0.12 : -e;
                p.Add(Box([x0, 0.14, z0], [x0 + 0.12, 1.86, z0 + 0.12], Trim));
            }
        p.Add(Box([-e, 1.86, -e], [e, CoreTop, e], Trim));
        const double bx0 = -0.92, bx1 = -0.06, bz0 = -0.88, bz1 = 0.08, bTop = 2.72; // (the bunker's flared mouth)
        double bcx = (bx0 + bx1) / 2, bcz = (bz0 + bz1) / 2;
        p.Add(Box([bx0 + 0.08, CoreTop, bz0 + 0.08], [bx1 - 0.08, 2.3, bz1 - 0.08]));
        p.Add(Box([bx0, 2.3, bz0], [bx1, bTop, bz1]));
        double cx = SW.CycloneX, cz = SW.CycloneZ, cr = SW.CycloneR;
        // (the cyclone's solid is three hidden boxes, its round look three shaped details over them: a shape can't be solid)
        (double Y0, double Y1, double R, PyDict Shape)[] cyclone = [(3.5, 5.0, cr, Taper("-y", 0.25)), (5.0, 7.0, cr, Cyl("+y", 24)), (7.0, SW.CycloneTop, 0.14, Cyl("+y", 16))]; // (its cone narrowing down to its valve, its barrel, its outlet)
        foreach (var (y0, y1, rr, _) in cyclone)
            p.Add(Box([cx - rr, y0, cz - rr], [cx + rr, y1, cz + rr], hidden: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, SockY, 0], "+x"); // (the bed's first Saw Block)
        p.Stats(1500, 3000, 5, 8);
        var bulk = Kw(("material", "any"), ("capacity", 60000), ("min", Py.L(-0.9, 0.16, -0.55)), ("max", Py.L(-0.3, 0.95, 0.55)));
        var refine = Kw(("machine", "sawmill"), ("capacity", 30000), ("bin", InBin), ("outBin", OutBin), ("min", Py.L(bx0 + 0.1, 2.32, bz0 + 0.1)), ("max", Py.L(bx1 - 0.1, 2.6, bz1 - 0.1)));
        p.Set(("power", 1800), ("storage", Kw(("bulk", bulk), ("refine", refine))),
            ("machineBlock", Kw(("family", "sawmill"), ("role", "core"), ("origin", Py.L(0, 1, 0)), ("squares", true), ("maxCells", MaxCells), ("maxSide", MaxSide))),
            ("config", Py.L(
                Kw(("key", "recipe"), ("type", "choice"), ("label", "Recipe"), ("group", "Production"), ("tooltip", "Auto: what is in the input picks it. Set: only its input goes in.")),
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(R3(bcx), bTop, R3(bcz))), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(-e, 0.55, 0)), ("face", "-x")))),
            ("centreSockets", false), ("collider", "boxes"));
        // The control face (-z, the mills' front): the screen with its lamp and button shelf.
        var keepClear = new List<(double[], string)> { ([1, SockY, 0], "+x"), ([R3(bcx), bTop, R3(bcz)], "+y"), ([-e, 0.55, 0], "-x"), ([0, 0, 0], "-y") };
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-z"], rows: 5, width: 0.78, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Py.L(Kw(("action", "machine.recipe"), ("at", Py.L(0.02, 0.02, 0.36, 0.14)), ("label", "Recipe")));
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        foreach (var (y0, y1, rr, shape) in cyclone)
            p.Add(L([cx - rr, y0, cz - rr], [cx + rr, y1, cz + rr], MachineLook.Steel, "steel", shape: shape));
        MachineLook.Skirt(p, -e, e, -e, e, 0, 0.12, 0.012);
        const double fz = -e - 0.012;
        // A sawdust hatch low on the front beside the screen (its frame, the hatch, its latch) and a CUT HAZARD plate over it.
        p.Add(L([0.2, 0.3, fz - 0.03], [0.74, 0.36, fz], MachineLook.Base));
        p.Add(L([0.2, 0.84, fz - 0.03], [0.74, 0.9, fz], MachineLook.Base));
        p.Add(L([0.2, 0.36, fz - 0.03], [0.26, 0.84, fz], MachineLook.Base));
        p.Add(L([0.68, 0.36, fz - 0.03], [0.74, 0.84, fz], MachineLook.Base));
        p.Add(L([0.26, 0.36, fz - 0.016], [0.68, 0.84, fz], Trim, paintable: true));
        p.Add(L([0.42, 0.58, fz - 0.05], [0.52, 0.64, fz - 0.016], MachineLook.Accent)); // (its latch)
        p.Add(L([0.26, 0.96, fz - 0.004], [0.68, 1.08, fz], MachineLook.Accent)); // (CUT HAZARD: KEEP CLEAR OF THE BANDS)
        p.Add(L([0.32, 0.99, fz - 0.016], [0.62, 1.05, fz - 0.004], MachineLook.Ink));
        // Steel bands round the body on its back and free side.
        foreach (var y in new[] { 0.55, 1.45 })
        {
            p.Add(L([-0.88, y, e - 0.004], [0.88, y + 0.08, e + 0.014], MachineLook.Steel, "steel")); // (+z)
            p.Add(L([-e - 0.014, y, -0.88], [-e + 0.004, y + 0.08, -0.3], MachineLook.Steel, "steel")); // (-x, either side of the Chute out)
            p.Add(L([-e - 0.014, y, 0.3], [-e + 0.004, y + 0.08, 0.88], MachineLook.Steel, "steel"));
        }
        // The Chute out on the free side (the shared port look) and its label; the Chute in's collar on the bunker's mouth, its label.
        MachineLook.Port(p, [-e, 0.55, 0], 0, -1, 0.16, 0.12, false);
        p.Add(L([-e - 0.014, 0.98, -0.3], [-e + 0.002, 1.16, 0.3], MachineLook.Out)); // (OUT)
        p.Add(L([-e - 0.026, 1.03, -0.22], [-e - 0.014, 1.11, 0.22], MachineLook.Ink));
        MachineLook.Port(p, [R3(bcx), bTop, R3(bcz)], 1, 1, 0.2, 0.1, true);
        p.Add(L([bx0 + 0.12, 2.42, bz0 - 0.012], [bx1 - 0.12, 2.6, bz0], MachineLook.In)); // (IN, on the bunker's front)
        p.Add(L([bx0 + 0.2, 2.47, bz0 - 0.024], [bx1 - 0.2, 2.55, bz0 - 0.012], MachineLook.Ink));
        // The bunker's steel rim and its corner angles, and a log-shaped stop bar across its mouth.
        p.Add(L([bx0 - 0.014, bTop - 0.08, bz0 - 0.014], [bx1 + 0.014, bTop - 0.02, bz1 + 0.014], MachineLook.Steel, "steel"));
        foreach (var (x, z) in new[] { (bx0, bz0), (bx1, bz0), (bx0, bz1), (bx1, bz1) })
            p.Add(L([x - 0.03, 2.3, z - 0.03], [x + 0.03, bTop - 0.08, z + 0.03], MachineLook.Base));
        // ---- The cyclone: four legs and their braces from the top deck to its barrel, the rotary valve under the cone and its drop pipe into
        // the body, steel bands round the barrel, an access hatch, the inlet stub on its +x side (where the trunk duct ends: SawmillWorks) and
        // a rain cap over its outlet.
        const double leg = 0.3;
        foreach (var (lx, lz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            p.Add(L([cx + lx * leg - 0.04, CoreTop, cz + lz * leg - 0.04], [cx + lx * leg + 0.04, 5.02, cz + lz * leg + 0.04], MachineLook.Base));
        foreach (var y in new[] { 3.0, 4.2 })
        {
            p.Add(L([cx - leg - 0.02, y, cz - leg - 0.03], [cx + leg + 0.02, y + 0.06, cz - leg + 0.03], MachineLook.Base));
            p.Add(L([cx - leg - 0.02, y + 0.004, cz + leg - 0.03], [cx + leg + 0.02, y + 0.064, cz + leg + 0.03], MachineLook.Base));
            p.Add(L([cx - leg - 0.03, y + 0.008, cz - leg + 0.04], [cx - leg + 0.03, y + 0.068, cz + leg - 0.04], MachineLook.Base));
            p.Add(L([cx + leg - 0.03, y + 0.012, cz - leg + 0.04], [cx + leg + 0.03, y + 0.072, cz + leg - 0.04], MachineLook.Base));
        }
        p.Add(L([cx - 0.15, 3.2, cz - 0.15], [cx + 0.15, 3.504, cz + 0.15], MachineLook.Base, shape: Cyl("+y", 16))); // (the rotary valve)
        p.Add(L([cx - 0.17, 3.3, cz - 0.17], [cx + 0.17, 3.38, cz + 0.17], MachineLook.Accent, shape: Cyl("+y", 16))); // (its drive ring)
        p.Add(L([cx + 0.15, 3.25, cz - 0.08], [cx + 0.36, 3.45, cz + 0.08], MachineLook.Base)); // (its motor)
        p.Add(L([cx - 0.08, CoreTop - 0.02, cz - 0.08], [cx + 0.08, 3.2, cz + 0.08], MachineLook.Steel, "steel", shape: Cyl("+y", 12))); // (the drop pipe into the body)
        foreach (var y in new[] { 5.3, 6.0, 6.8 })
            p.Add(L([cx - cr - 0.025, y, cz - cr - 0.025], [cx + cr + 0.025, y + 0.06, cz + cr + 0.025], MachineLook.Base, shape: Cyl("+y", 24)));
        p.Add(L([cx - 0.16, 5.45, cz - cr - 0.03], [cx + 0.16, 5.85, cz - cr + 0.1], Trim, paintable: true)); // (an access hatch on its front)
        p.Add(L([cx - 0.04, 5.62, cz - cr - 0.05], [cx + 0.04, 5.68, cz - cr - 0.02], MachineLook.Accent)); // (its handle)
        p.Add(L([cx + cr - 0.1, SW.InletY - 0.18, cz - 0.18], [SW.InletEnd, SW.InletY + 0.18, cz + 0.18], MachineLook.Steel, "steel", shape: Cyl("+x", 16))); // (the inlet stub)
        p.Add(L([SW.InletEnd - 0.05, SW.InletY - 0.24, cz - 0.24], [SW.InletEnd - 0.002, SW.InletY + 0.24, cz + 0.24], MachineLook.Base, shape: Cyl("+x", 16))); // (its flange)
        p.Add(L([cx - 0.24, SW.CycloneTop + 0.12, cz - 0.24], [cx + 0.24, SW.CycloneTop + 0.18, cz + 0.24], MachineLook.Base, shape: Cyl("+y", 16))); // (the rain cap, on three stays)
        foreach (var (sx, sz) in new[] { (0.0, -0.17), (0.15, 0.09), (-0.15, 0.09) })
            p.Add(L([cx + sx - 0.015, SW.CycloneTop - 0.06, cz + sz - 0.015], [cx + sx + 0.015, SW.CycloneTop + 0.124, cz + sz + 0.015], MachineLook.Base));
        // Its back (+z): the gauges (band speed and duct draw), the mains box and the conduit up its corner, louvres, the maker's plate.
        foreach (var x in new[] { -0.55, -0.2 })
        {
            p.Add(L([x - 0.1, 1.5, e - 0.004], [x + 0.1, 1.7, e + 0.03], "#e8e6df", shape: Cyl("+z", 14)));
            p.Add(L([x - 0.008, 1.58, e + 0.03], [x + 0.008, 1.68, e + 0.044], MachineLook.Ink));
        }
        p.Add(L([0.25, 0.25, e - 0.004], [0.7, 0.62, e + 0.07], MachineLook.Base)); // (the mains box)
        p.Add(L([0.6, 0.62, e + 0.01], [0.66, 1.86, e + 0.06], MachineLook.Base, shape: Cyl("+y", 8))); // (its conduit)
        for (var k = 0; k < 5; k++)
            p.Add(L([0.15, 0.85 + k * 0.1, e - 0.004], [0.75, 0.89 + k * 0.1, e + 0.018], MachineLook.Base)); // (louvres)
        p.Add(L([-0.7, 0.3, e - 0.004], [-0.3, 0.45, e + 0.012], MachineLook.In)); // (the maker's plate)
        p.Add(L([-0.64, 0.34, e + 0.012], [-0.36, 0.41, e + 0.024], MachineLook.Ink));
        // The top deck's edge in steel, and bolts along the plinth.
        p.Add(L([-e - 0.012, 1.9, -e - 0.012], [e + 0.012, 1.96, -e + 0.004], MachineLook.Steel, "steel"));
        p.Add(L([-e - 0.012, 1.9, e - 0.004], [e + 0.012, 1.96, e + 0.012], MachineLook.Steel, "steel"));
        foreach (var x in new[] { -0.7, -0.3, 0.3, 0.7 })
        {
            p.Add(L([x - 0.02, 0.05, -e - 0.024], [x + 0.02, 0.09, -e - 0.008], MachineLook.Steel, "steel"));
            p.Add(L([x - 0.02, 0.05, e + 0.008], [x + 0.02, 0.09, e + 0.024], MachineLook.Steel, "steel"));
        }
        return p;
    }

    // ---- The Saw Block -----------------------------------------------------------------------------------------------------------

    private static Part SawBlock()
    {
        var p = new Part("Saw Block", Small, "Sawmills",
            "Weld it on a Sawmill Core's +x side, or beside another Saw Block: the Core splits its blocks into squares, each one band mill whose carriage strokes a log through the band, and every square saws at once; a bigger square takes a bigger log. Each block of an n x n square adds 1 + n/4 to the Core's rate (a lone block 1.25, a 3 x 3's 1.75 each), so bigger squares pay more. A wrecked block keeps its square and adds nothing until mended. No power of its own: the Core draws a share for it. The bands and carriages cut: keep clear while it works.",
            group: "Production", color: Body);
        const double e = 0.997, t = BlockTop;
        p.Add(Box([-e, 0.003, -e], [e, t, e]));
        // Each outer face: the bed's skirt, its corner columns, the cap angle round the top, a girder across and the bolts
        // ("seam": drawn only where no block of its own square is welded on). Where one is, a strip over the joint on top ("seamFill").
        foreach (var (n, sg, face) in new[] { (0, 1, "+x"), (0, -1, "-x"), (2, 1, "+z"), (2, -1, "-z") })
        {
            PyDict? On(double out0, double out1, double along0, double along1, double y0, double y1, string colour, string? finish = null, bool paint = false)
            {
                double[] mn = new double[3], mx = new double[3];
                (mn[n], mx[n]) = sg > 0 ? (e + out0, e + out1) : (-e - out1, -e - out0);
                var o = 2 - n;
                (mn[o], mx[o]) = (along0, along1);
                (mn[1], mx[1]) = (y0, y1);
                return L(mn, mx, colour, finish, paint);
            }
            // (Two squares side by side both draw the faces they meet on, 6 mm apart: the -x and -z faces' dressing sits a few millimetres
            // off the +x and +z faces' in height and along, so no two of them share a plane.)
            var v = sg < 0 ? 0.004 : 0.0;
            var w = sg < 0 ? 0.03 : 0.0;
            p.Add(Seam(On(-0.004, 0.022, -e + 0.004 + v, e - 0.004 - v, v, 0.16 - 5 * v, MachineLook.Base, "dirty steel"), face)); // (the skirt)
            foreach (var a in new[] { -e + 0.02 + w, e - 0.15 - w })
                p.Add(Seam(On(-0.004, 0.06, a, a + 0.13, 0.16 - 5 * v, t - 0.1 - v, MachineLook.Base), face)); // (corner columns)
            p.Add(Seam(On(-0.004, 0.034, -e + 0.004 + 2 * v, e - 0.004 - 2 * v, t - 0.1 + v, t - 0.004 - v, MachineLook.Base), face)); // (the cap angle)
            p.Add(Seam(On(-0.004, 0.03, -e + 0.15 + 2 * v, e - 0.15 - 2 * v, 0.46 - 10 * v, 0.58 - 10 * v, MachineLook.Base), face)); // (a girder across)
            p.Add(Seam(On(-0.004, 0.014, -e + 0.15 + 2 * v, e - 0.15 - 2 * v, 0.7 - 10 * v, 0.76 - 10 * v, Trim, paint: true), face)); // (a plate band)
            foreach (var a in new[] { -0.5, 0.0, 0.5 })
            {
                p.Add(Seam(On(0.022, 0.036, a - 0.02 + 5 * w, a + 0.02 + 5 * w, 0.06, 0.1, MachineLook.Steel, "steel"), face)); // (the skirt's bolts)
                p.Add(Seam(On(0.03, 0.044, a - 0.02 + 5 * w, a + 0.02 + 5 * w, 0.5 - 10 * v, 0.54 - 10 * v, MachineLook.Steel, "steel"), face)); // (the girder's bolts)
            }
            double[] smn = new double[3], smx = new double[3];
            (smn[n], smx[n]) = sg > 0 ? (e - 0.05, 1.0) : (-1.0, -e + 0.05);
            (smn[2 - n], smx[2 - n]) = (-1.0, 1.0);
            // (Tread plate a few millimetres under the deck plate's top, so a square's deck reads as one floor; each face's strip 2 mm off the
            // others', so neither the corners where two cross nor the joints where two blocks' meet share a plane.)
            (smn[n], smx[n]) = sg > 0 ? (0.93, 1.0) : (-1.0, -0.93);
            (smn[1], smx[1]) = (t - 0.01, DeckTop - 0.003 - (sg < 0 ? 0.004 : 0.0) - (n == 2 ? 0.002 : 0.0));
            p.Add(Fill(L(smn, smx, MachineLook.Steel, "tread plate"), face));
        }
        // The deck plate either way: tread plate, the floor the mill stands on.
        p.Add(L([-0.94, t - 0.004, -0.94], [0.94, DeckTop, 0.94], MachineLook.Steel, "tread plate"));
        // (In a square of 2 x 2 or more the mill over the deck, and its one sawdust grate under the track, are SawmillRender's.)
        // ---- Alone (a 1 x 1, or a loose block): its own small band mill, SawmillWorks at n 1 about its middle ----
        AloneMill(p);
        // A CUT HAZARD plate low on the front either way, and the maker's plate on the back.
        const double fz = -e - 0.012;
        p.Add(Seam(L([0.5, 0.24, fz - 0.004], [0.8, 0.36, fz], MachineLook.Accent), "-z"));
        p.Add(Seam(L([0.55, 0.27, fz - 0.016], [0.75, 0.33, fz - 0.004], MachineLook.Ink), "-z"));
        p.Add(Seam(L([-0.3, 0.24, e], [0.3, 0.38, e + 0.012], MachineLook.In), "+z"));
        p.Add(Seam(L([-0.24, 0.28, e + 0.012], [0.24, 0.34, e + 0.024], MachineLook.Ink), "+z"));
        p.Sock("+x", [1, SockY, 0], "+x");
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("+z", [0, SockY, 1], "+z");
        p.Sock("-z", [0, SockY, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(1200, 2400, 5, 8);
        p.Set(("machineBlock", Kw(("family", "sawmill"), ("role", "block"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// A lone Saw Block's band mill (SawmillWorks at n 1, square middle 0, 0): the track, the carriage and its log at the end of a stroke
    /// ("stroke" along -x by a stroke's travel: quickly back, slowly through the band), the band's two runs, the column with its arms, the
    /// upper wheel spinning ("spin": its tyre ring, spokes and hub), the guide over the log, the guards.
    /// </summary>
    private static void AloneMill(Part p)
    {
        const int n = 1;
        double lr = SW.LogR(n), r = SW.WheelR(n), rim = SW.RimHalf(n), len = SW.Length(n), travel = SW.Travel(n), depth = SW.CarDepth(n);
        double cut = R3(SW.ZCut(0, n)), back = R3(SW.ZBack(0, n)), logZ = R3(SW.LogZ(0, n)), wy = R3(SW.WheelY(n)), top = SW.Top(n);
        var (col0, col1) = SW.Column(0, n);
        double cx = R3(SW.CarriageX(0, n, 1f)), axleZ = R3(cut + r), colHalf = SW.ColumnHalf(n);
        double deck = DeckTop;
        PyDict? Stroke(PyDict? b) => Work(b, "stroke", "-x", travel);
        // The track: three sleepers and two rails along x, behind the band.
        double rail0 = R3(cut + 0.2), rail1 = R3(cut + depth - 0.15);
        foreach (var x in new[] { -0.6, 0.0, 0.6 })
            p.Add(Alone(L([x - 0.08, deck + 0.002, cut + 0.1], [x + 0.08, SW.RailY0, cut + depth - 0.04], Trim)));
        foreach (var z in new[] { rail0, rail1 })
            p.Add(Alone(L([-0.95, SW.RailY0, z - 0.03], [0.95, SW.RailY1, z + 0.03], MachineLook.Steel, "steel")));
        // The carriage (it strokes): its frame, two bolsters the log lies on, its knees at the back holding the log, four wheels on the rails,
        // the setworks box; and its log, bark with lighter sawn ends.
        double c0 = cx - len / 2, c1 = cx + len / 2, cz0 = R3(cut + 0.06), cz1 = R3(cut + depth);
        p.Add(Alone(Stroke(L([c0, SW.CarriageY0, cz0], [c1, SW.CarriageY1 - 0.06, cz1], MachineLook.Base))));
        foreach (var x in new[] { cx - 0.25, cx + 0.25 })
        {
            p.Add(Alone(Stroke(L([x - 0.05, SW.CarriageY1 - 0.064, cz0 + 0.02], [x + 0.05, SW.CarriageY1, cz1 - 0.02], MachineLook.Accent)))); // (a bolster)
            p.Add(Alone(Stroke(L([x - 0.04, SW.CarriageY1 - 0.004, R3(logZ + lr + 0.01)], [x + 0.04, R3(SW.CarriageY1 + 1.2 * lr), R3(logZ + lr + 0.14)], MachineLook.Steel, "steel")))); // (a knee)
            p.Add(Alone(Stroke(L([x - 0.014, R3(SW.CarriageY1 + 0.6 * lr), R3(logZ + lr - 0.02)], [x + 0.014, R3(SW.CarriageY1 + 0.6 * lr + 0.05), R3(logZ + lr + 0.012)], MachineLook.Accent)))); // (its dog, biting the log)
        }
        foreach (var x in new[] { c0 + 0.12, c1 - 0.12 })
            foreach (var z in new[] { rail0, rail1 })
                p.Add(Alone(Stroke(L([x - 0.07, SW.RailY1 - 0.01, z - 0.045], [x + 0.07, SW.CarriageY0 + 0.07, z + 0.045], MachineLook.Steel, "steel", shape: Cyl("+z", 12))))); // (a wheel)
        p.Add(Alone(Stroke(L([cx - 0.12, SW.CarriageY0 + 0.01, cz1 - 0.004], [cx + 0.12, SW.CarriageY1 - 0.07, cz1 + 0.095], MachineLook.Base)))); // (the setworks box, 5 mm clear of the back run's guard as it passes)
        p.Add(Alone(Stroke(L([c0 + 0.02, SW.CarriageY1, logZ - lr], [c1 - 0.02, R3(SW.CarriageY1 + 2 * lr), logZ + lr], Bark, shape: Cyl("+x", 16))))); // (the log)
        foreach (var (x0, x1) in new[] { (c0 + 0.006, c0 + 0.024), (c1 - 0.024, c1 - 0.006) })
            p.Add(Alone(Stroke(L([x0, SW.CarriageY1 + 0.03, logZ - lr + 0.03], [x1, R3(SW.CarriageY1 + 2 * lr - 0.03), logZ + lr - 0.03], Grain, shape: Cyl("+x", 16))))); // (its sawn end)
        // The band: the cutting run through the log's path (from its slot in the deck to the upper wheel), the back run, its guard.
        p.Add(Alone(L([-rim * 0.8, deck - 0.03, cut - 0.006], [rim * 0.8, wy, cut + 0.006], MachineLook.Steel, "steel")));
        p.Add(Alone(L([-rim * 0.8, deck - 0.03, back - 0.006], [rim * 0.8, wy, back + 0.006], MachineLook.Steel, "steel")));
        p.Add(Alone(L([-rim - 0.04, deck + 0.004, back - 0.06], [rim + 0.04, R3(wy - 0.16), back + 0.06], MachineLook.Accent, "hazard paint")));
        p.Add(Alone(L([-rim - 0.1, deck - 0.004, cut - 0.1], [rim + 0.1, deck + 0.03, cut + 0.05], MachineLook.Base))); // (the slot's guard on the deck)
        // The column behind, its cap and tension handwheel, a steel face plate, bolts; the arms forward to the upper wheel's bearings.
        p.Add(Alone(L([-colHalf, deck + 0.004, col0], [colHalf, top - 0.25, col1], MachineLook.Base)));
        p.Add(Alone(L([-colHalf + 0.04, top - 0.254, col0 + 0.05], [colHalf - 0.04, top - 0.06, col1 - 0.05], Body, paintable: true))); // (the strain housing)
        double zc = (col0 + col1) / 2;
        p.Add(Alone(L([-colHalf - 0.03, top - 0.165, zc - 0.015], [-colHalf + 0.06, top - 0.135, zc + 0.015], MachineLook.Steel, "steel"))); // (the tension screw out of the housing's -x side)
        p.Add(Alone(L([-colHalf - 0.055, top - 0.24, zc - 0.09], [-colHalf - 0.03, top - 0.06, zc + 0.09], MachineLook.Accent, shape: Cyl("+x", 16)))); // (its handwheel, clear of the duct over the column)
        p.Add(Alone(L([-colHalf + 0.04, deck + 0.4, col0 - 0.012], [colHalf - 0.04, top - 0.5, col0 + 0.004], MachineLook.Steel, "dirty steel"))); // (the face plate)
        foreach (var y in new[] { deck + 0.5, R3((deck + top) / 2), top - 0.6 })
            foreach (var x in new[] { -colHalf + 0.08, colHalf - 0.08 })
                p.Add(Alone(L([x - 0.02, y - 0.02, col0 - 0.024], [x + 0.02, y + 0.02, col0 - 0.008], MachineLook.Steel, "steel")));
        foreach (var sx in new[] { -1, 1 })
        {
            double a0 = sx > 0 ? rim + 0.03 : -rim - 0.11, a1 = sx > 0 ? rim + 0.11 : -rim - 0.03;
            p.Add(Alone(L([a0, wy - 0.12, axleZ - 0.12], [a1, wy + 0.12, col0 + 0.02], MachineLook.Base))); // (an arm)
            p.Add(Alone(L([a0 - 0.004, wy - 0.16, axleZ - 0.16], [a1 + 0.004, wy + 0.16, axleZ + 0.16], MachineLook.Steel, "steel"))); // (its bearing)
        }
        // The motor on the column's back and its belt guard up to the wheel's shaft.
        p.Add(Alone(L([-0.2, deck + 0.3, col1 - 0.004], [0.2, deck + 0.7, col1 + 0.3], MachineLook.Base, shape: Cyl("+x", 16))));
        p.Add(Alone(L([-0.08, deck + 0.7, col1 + 0.06], [0.08, R3(wy), col1 + 0.18], MachineLook.Accent)));
        // The guide over the log, hung from the +x arm on a rod.
        double gy0 = R3(SW.CarriageY1 + 2 * lr + 0.06), gy1 = gy0 + 0.14;
        p.Add(Alone(L([-rim - 0.06, gy0, cut - 0.08], [rim + 0.06, gy1, cut + 0.1], MachineLook.Base)));
        p.Add(Alone(L([rim + 0.06, gy0 + 0.02, cut + 0.0], [rim + 0.1, wy - 0.1, cut + 0.06], MachineLook.Steel, "steel")));
        // The upper wheel (it spins about x): its tyre ring, two spokes across and its hub; a steel cap over it.
        p.Add(Alone(Work(L([-rim, wy - r, axleZ - r], [rim, wy + r, axleZ + r], MachineLook.Steel, "steel", shape: Tube("+x", 0.84)), "spin", "+x")));
        p.Add(Alone(Work(L([-rim * 0.55, wy - r - 0.012, axleZ - r - 0.012], [rim * 0.55, wy + r + 0.012, axleZ + r + 0.012], MachineLook.Base, shape: Tube("+x", 0.9)), "spin", "+x"))); // (its tyre)
        p.Add(Alone(Work(L([-0.03, wy - r + 0.05, axleZ - 0.04], [0.03, wy + r - 0.05, axleZ + 0.04], MachineLook.Accent), "spin", "+x")));
        p.Add(Alone(Work(L([-0.026, wy - 0.04, axleZ - r + 0.05], [0.026, wy + 0.04, axleZ + r - 0.05], MachineLook.Accent), "spin", "+x")));
        p.Add(Alone(Work(L([-rim - 0.05, wy - 0.09, axleZ - 0.09], [rim + 0.05, wy + 0.09, axleZ + 0.09], MachineLook.Base, shape: Cyl("+x", 14)), "spin", "+x"))); // (its hub)
        p.Add(Alone(L([-rim - 0.03, R3(wy + r + 0.04), axleZ - r * 0.7], [rim + 0.03, R3(wy + r + 0.1), col0 + 0.02], MachineLook.Accent))); // (the wheel's top guard)
    }

    // ---- The Plank Stacker -------------------------------------------------------------------------------------------------------

    private static Part PlankStacker()
    {
        var p = new Part("Plank Stacker", Small, "Sawmills",
            "A Sawmill's end piece: weld one on the far (+x) edge of a band mill square, one a row. The plank chain ends on it and its pile grows and clears as the mill saws. Looks only: it adds nothing to the rate.",
            group: "Production", color: MachineLook.Base);
        const double e = 0.997, top = SW.StackerTop;
        p.Add(Box([-e, 0.003, -e], [e, top - 0.12, e], MachineLook.Base)); // (its top under a Saw Block's skirt's, which reaches over it)
        // Four stakes at the corners that hold the pile square, solid.
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? 0.86 : -0.94, z0 = sz > 0 ? 0.86 : -0.94;
                p.Add(Box([x0, top - 0.12, z0], [x0 + 0.08, 1.3, z0 + 0.08], MachineLook.Steel));
            }
        // The roller deck the pile lies on: rollers across (along z) between two side rails; a stop at the far end.
        foreach (var z in new[] { -0.9, 0.9 })
            p.Add(L([-0.86, top - 0.12, z - 0.04], [0.86, top - 0.03, z + 0.04], Trim));
        for (var k = 0; k < 7; k++)
        {
            var x = -0.75 + k * 0.25;
            p.Add(L([x - 0.05, top - 0.1, -0.86], [x + 0.05, top, 0.86], MachineLook.Steel, "steel", shape: Cyl("+z", 12)));
        }
        p.Add(L([0.8, top - 0.004, -0.8], [0.86, top + 0.5, 0.8], MachineLook.Accent)); // (the stop)
        // Hazard paint on its kerbs, the stakes' collars and the frame's bolts; a label.
        MachineLook.Hazard(p, [-e - 0.012, 0.06, -e - 0.012], [e + 0.012, 0.16, -e]);
        MachineLook.Hazard(p, [-e - 0.012, 0.06, e], [e + 0.012, 0.16, e + 0.012]);
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x = sx > 0 ? 0.9 : -0.9, z = sz > 0 ? 0.9 : -0.9;
                p.Add(L([x - 0.06, 1.0, z - 0.06], [x + 0.06, 1.06, z + 0.06], MachineLook.Accent));
            }
        foreach (var x in new[] { -0.6, 0.0, 0.6 })
            foreach (var z in new[] { -e - 0.012, e })
                p.Add(L([x - 0.03, 0.2, z], [x + 0.03, 0.26, z + 0.012], MachineLook.Steel, "steel"));
        p.Add(L([e + 0.003, 0.1, -0.3], [e + 0.015, 0.22, 0.3], MachineLook.Out)); // (OUT, on its far end)
        p.Add(L([e + 0.015, 0.13, -0.22], [e + 0.027, 0.19, 0.22], MachineLook.Ink));
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(500, 1000, 4, 5);
        p.Set(("machineBlock", Kw(("family", "sawmill"), ("role", "end"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o)
    {
        foreach (var p in new[] { Core(), SawBlock(), PlankStacker() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
