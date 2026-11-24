using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Foundry on the block system (docs/sandbox/constructs/production.md "Refiners as block machines"; the design "Smelters and refiners as
/// block machines", drawing "Works-size Foundry"; MachineBlocks.Squares, ConstructRefining.RefineOf, FoundryWorks, FoundryRender): three
/// parts in Constructs/Parts/Refining, beside the Smelter's and the Sawmill's tier files (machine_tiers' own, so this writes its files one by
/// one and owns no folder).
/// <list type="bullet">
/// <item>The Foundry Core, 2 x 2 x 2 m: two mixer hoppers on top, fed through one Chute in on the manifold between them, the screen on the
/// front (-z), Chute out on its free side (-x). Nothing on it moves but the mixers' paddles. On its own it is the old tier 1 Foundry: 2 steel a
/// second, every mixing recipe (up to 4 inputs each).</item>
/// <item>The Crucible Block, 2 x 2 m and 1.6 m tall: a steel pit frame, no power of its own, welded on the Core's +x side: the Core splits its
/// blocks into squares (1 x 1 up to 5 x 5) and each is one crucible. Alone (a 1 x 1, or loose) it carries a small crucible of its own on two
/// trunnion stands, which tilts toward the front to pour while its machine works ("alone" boxes, "work": "hinge"; its melt glows). In a
/// square of 2 or more its outer faces' cladding makes one pit ("seam"); the crucible over the square, the hood, the stack, the launder and
/// the pours are drawn by FoundryRender.</item>
/// <item>The Mould Line, 2 x 2 m and 1 m tall, the optional end piece (family "foundry"): one per row past a square's far +x edge. Its moulds
/// take the output's colour as each pour reaches them (drawn by FoundryRender). Looks only.</item>
/// </list>
/// The old tier 2 to 5 Foundries went (a saved one loads as the Core: ConstructParts.Renamed).
/// </summary>
public sealed class FoundryParts : IPartGenerator
{
    public string Name => "foundry_parts";

    private const string Out = "Constructs/Parts/Refining";
    public const string Body = "#6b4a35"; // the Foundries' colour: the Core's cladding, the pit frames' plates
    private const string Trim = "#4a3324";
    private const string Lining = "#8a7f72"; // refractory: a crucible's rim, the pits' lining
    private const string Sand = "#7a6a55", Soot = "#2b2b2b", Crust = "#3a2418";
    public const double SockY = 0.25; // the side sockets' height on the Core, a block and the Mould Line (they weld at one height)
    public const double BlockTop = FoundryWorks.PitTop, CoreTop = 2.0, LineTop = FoundryWorks.MouldTop;
    public const int OutBin = 400, InBin = 200; // the Core's bins at x1 (the old tier 1 Foundry's: its input, of each material), grown by its squares' rate
    private const int MaxCells = 50, MaxSide = 10;

    /// <summary>A lone Crucible Block's crucible (its frame): radius, bottom and top, the trunnions' height (the hinge line, along x) and how far it tips toward the front.</summary>
    public static readonly double CrucibleR = FoundryWorks.Radius(1), CrucibleY0 = R3(FoundryWorks.Bottom(1)), CrucibleY1 = R3(CrucibleY0 + FoundryWorks.Height(1)),
        TrunnionY = R3(FoundryWorks.Trunnion(1)), Tilt = FoundryWorks.Tilt(1), SpoutOut = R3(FoundryWorks.Spout(1)); // (FoundryWorks at n 1)

    private static double R3(double v) => Math.Round(v, 3);

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static PyDict? Seam(PyDict? box, params string[] faces) => box?.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    private static PyDict? Fill(PyDict? box, string face) => box?.Update(("seamFill", face));

    private static PyDict? Alone(PyDict? box) => box?.Update(("alone", true));

    private static PyDict? Merged(PyDict? box) => box?.Update(("merged", true));

    private static PyDict? Work(PyDict? b, string kind, string? axis = null) => MachineLook.Work(b, kind, axis);

    /// <summary>A box of the lone crucible: it tips toward the front (-z) about the trunnions' line while its machine works.</summary>
    private static PyDict? Tips(PyDict? b) => MachineLook.Work(b, "hinge", "-z", Tilt)?.Update(("pivot", Py.L(0, TrunnionY, 0)));

    // ---- The Core ------------------------------------------------------------------------------------------------------------------

    private static Part Core()
    {
        var p = new Part("Foundry", Small, "Foundries",
            "A Foundry Core: mixes materials into new ones, up to four at once (metal and stone to steel, stone and sand to concrete, planks and glass to laminate, planks and sand to rubber, copper and metal to bronze, copper and gold to circuits, steel and bronze to ammo): a chute for each into its hoppers. On its own it makes 2 steel a second. Weld Crucible Blocks on its +x side: they split into square crucibles (1 x 1 up to 5 x 5) that all pour at once, a bigger square more a block (a 3 x 3 makes it x16.75), up to 50 blocks. Needs power: a share for it and each block.",
            role: "storage", group: "Production", color: Body);
        p.Set(("tier", 1)); // (a tier 1 machine's recipe: nothing a tier 1 machine can't make, so survival can start; Assembling.LongTail)
        const double e = 0.997;
        // Solid: the plinth, the body between four corner posts, the top deck, the two mixer hoppers and the feed manifold between them.
        p.Add(Box([-e, 0.003, -e], [e, 0.14, e]));
        p.Add(Box([-0.94, 0.14, -0.94], [0.94, 1.86, 0.94]));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? e - 0.12 : -e, z0 = sz > 0 ? e - 0.12 : -e;
                p.Add(Box([x0, 0.14, z0], [x0 + 0.12, 1.86, z0 + 0.12], Trim));
            }
        p.Add(Box([-e, 1.86, -e], [e, CoreTop, e], Trim));
        foreach (var sz in new[] { -1, 1 })
        {
            double z0 = sz > 0 ? 0.26 : -0.88, z1 = sz > 0 ? 0.88 : -0.26;
            p.Add(Box([-0.42, CoreTop, z0 + 0.1], [0.42, 2.18, z1 - 0.1])); // (a hopper's neck)
            p.Add(Box([-0.6, 2.18, z0], [0.6, 2.52, z1])); // (its bowl)
        }
        p.Add(Box([-0.28, CoreTop, -0.2], [0.28, 2.62, 0.2], Trim)); // (the feed manifold: Chute in on its top, a spout into each hopper)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, SockY, 0], "+x"); // (the bed's first Crucible Block)
        p.Stats(3500, 7000, 10, 10);
        var bulk = Kw(("material", "any"), ("capacity", 60000), ("min", Py.L(-0.9, 0.16, -0.55)), ("max", Py.L(-0.3, 0.95, 0.55)));
        var refine = Kw(("machine", "foundry"), ("capacity", 30000), ("bin", InBin), ("outBin", OutBin), ("min", Py.L(-0.54, 2.2, -0.84)), ("max", Py.L(0.54, 2.42, -0.3))); // (under the hopper's rim band)
        p.Set(("power", 3500), ("storage", Kw(("bulk", bulk), ("refine", refine))),
            ("machineBlock", Kw(("family", "foundry"), ("role", "core"), ("origin", Py.L(0, 1, 0)), ("squares", true), ("maxCells", MaxCells), ("maxSide", MaxSide))),
            ("config", Py.L(
                Kw(("key", "recipe"), ("type", "choice"), ("label", "Recipe"), ("group", "Production"), ("tooltip", "Auto: what is in the input picks it. Set: only its inputs go in.")),
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(0, 2.62, 0)), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(-e, 0.55, 0)), ("face", "-x")))),
            ("centreSockets", false), ("collider", "boxes"));
        // The control face (-z, the crucibles' front): the screen with its lamp and button shelf.
        var keepClear = new List<(double[], string)> { ([1, SockY, 0], "+x"), ([0, 2.62, 0], "+y"), ([-e, 0.55, 0], "-x"), ([0, 0, 0], "-y") };
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-z"], rows: 5, width: 0.78, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Py.L(Kw(("action", "machine.recipe"), ("at", Py.L(0.02, 0.02, 0.36, 0.14)), ("label", "Recipe")));
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -e, e, -e, e, 0, 0.12, 0.012);
        const double fz = -e - 0.012;
        // A sight glass low on the front beside the screen: the melt seen through it glows while it works; a POUR warning plate.
        p.Add(L([0.2, 0.36, fz - 0.03], [0.74, 0.42, fz], MachineLook.Base));
        p.Add(L([0.2, 0.74, fz - 0.03], [0.74, 0.8, fz], MachineLook.Base));
        p.Add(L([0.2, 0.42, fz - 0.03], [0.26, 0.74, fz], MachineLook.Base));
        p.Add(L([0.68, 0.42, fz - 0.03], [0.74, 0.74, fz], MachineLook.Base));
        p.Add(Work(L([0.26, 0.42, fz - 0.016], [0.68, 0.74, fz], MachineLook.Hot), "glow"));
        p.Add(L([0.26, 0.86, fz - 0.004], [0.68, 0.98, fz], MachineLook.Accent)); // (POUR: KEEP CLEAR)
        p.Add(L([0.32, 0.89, fz - 0.016], [0.62, 0.95, fz - 0.004], MachineLook.Ink));
        // Steel bands round the body on its back and free side, bolted at the posts.
        foreach (var y in new[] { 0.55, 1.45 })
        {
            p.Add(L([-0.88, y, e - 0.004], [0.88, y + 0.08, e + 0.014], MachineLook.Steel, "steel")); // (+z)
            p.Add(L([-e - 0.014, y, -0.88], [-e + 0.004, y + 0.08, -0.3], MachineLook.Steel, "steel")); // (-x, either side of the Chute out)
            p.Add(L([-e - 0.014, y, 0.3], [-e + 0.004, y + 0.08, 0.88], MachineLook.Steel, "steel"));
        }
        // The Chute out on the free side (the shared port look) and its label; the Chute in's collar on the manifold, its label.
        MachineLook.Port(p, [-e, 0.55, 0], 0, -1, 0.16, 0.12, false);
        p.Add(L([-e - 0.014, 0.98, -0.3], [-e + 0.002, 1.16, 0.3], MachineLook.Out)); // (OUT)
        p.Add(L([-e - 0.026, 1.03, -0.22], [-e - 0.014, 1.11, 0.22], MachineLook.Ink));
        MachineLook.Port(p, [0, 2.62, 0], 1, 1, 0.12, 0.08, true);
        p.Add(L([-0.22, 2.3, -0.212], [0.22, 2.44, -0.2], MachineLook.In)); // (IN, on the manifold's front)
        p.Add(L([-0.16, 2.34, -0.224], [0.16, 2.4, -0.212], MachineLook.Ink));
        // The manifold's two spouts into the hoppers, and each hopper's rim, its mixer's motor and the paddle turning over its lid.
        foreach (var sz in new[] { -1, 1 })
        {
            double zi = sz * 0.2, zo = sz * 0.3; // (from the manifold's face out over the hopper's inner rim)
            p.Add(L([-0.1, 2.44, Math.Min(zi, zo) - (sz < 0 ? 0.004 : 0)], [0.1, 2.56, Math.Max(zi, zo) + (sz > 0 ? 0.004 : 0)], MachineLook.Steel, "steel"));
            double z0 = sz > 0 ? 0.26 : -0.88, z1 = sz > 0 ? 0.88 : -0.26, zm = (z0 + z1) / 2;
            p.Add(L([-0.614, 2.44, z0 - 0.014], [0.614, 2.5, z1 + 0.014], MachineLook.Steel, "steel")); // (its rim band)
            p.Add(L([0.3, 2.52, zm - 0.12], [0.52, 2.72, zm + 0.12], MachineLook.Base, shape: Cyl("+y", 14))); // (the mixer's motor)
            p.Add(L([0.36, 2.72, zm - 0.06], [0.46, 2.76, zm + 0.06], MachineLook.Accent)); // (its cap)
            p.Add(L([-0.03, 2.52, zm - 0.03], [0.03, 2.66, zm + 0.03], MachineLook.Steel, "steel")); // (the paddle's shaft)
            p.Add(Work(L([-0.26, 2.6, zm - 0.03], [0.26, 2.64, zm + 0.03], MachineLook.Accent), "spin", "+y")); // (the paddle, turning while it mixes)
            p.Add(L([0.16, 2.6, zm - 0.02], [0.3, 2.62, zm + 0.02], MachineLook.Base)); // (the drive arm to it)
        }
        // Its back (+z): the gauges (melt and blast), the mains box and the conduit up its corner, louvres, the maker's plate.
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

    // ---- The Crucible Block ------------------------------------------------------------------------------------------------------

    private static Part CrucibleBlock()
    {
        var p = new Part("Crucible Block", Small, "Foundries",
            "Weld it on a Foundry Core's +x side, or beside another Crucible Block: the Core splits its blocks into squares, each one crucible that tips to pour, and every square works at once. Each block of an n x n square adds 1 + n/4 to the Core's rate (a lone block 1.25, a 3 x 3's 1.75 each), so bigger squares pay more. A wrecked block keeps its square and adds nothing until mended. No power of its own: the Core draws a share for it.",
            group: "Production", color: Body);
        const double e = 0.997, t = BlockTop;
        p.Add(Box([-e, 0.003, -e], [e, t, e]));
        // Each outer face: the pit frame's skirt, its corner columns, the cap angle round the top, a girder across and the plinth's bolts
        // ("seam": drawn only where no block of its own square is welded on). Where one is, a strip over the joint on top ("seamFill")
        // closes the 6 mm between them, so a square reads as one pit.
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
            p.Add(Seam(On(-0.004, 0.022, -e + 0.004 + v, e - 0.004 - v, v, 0.2 - 5 * v, MachineLook.Base, "dirty steel"), face)); // (the skirt)
            foreach (var a in new[] { -e + 0.02 + w, e - 0.15 - w })
                p.Add(Seam(On(-0.004, 0.06, a, a + 0.13, 0.2 - 5 * v, t - 0.1 - v, MachineLook.Base), face)); // (corner columns)
            p.Add(Seam(On(-0.004, 0.034, -e + 0.004 + 2 * v, e - 0.004 - 2 * v, t - 0.1 + v, t - 0.004 - v, MachineLook.Base), face)); // (the cap angle)
            p.Add(Seam(On(-0.004, 0.03, -e + 0.15 + 2 * v, e - 0.15 - 2 * v, 0.82 - 10 * v, 0.94 - 10 * v, MachineLook.Base), face)); // (a girder across)
            p.Add(Seam(On(-0.004, 0.014, -e + 0.15 + 2 * v, e - 0.15 - 2 * v, 1.12 - 10 * v, 1.2 - 10 * v, Trim, paint: true), face)); // (a plate band)
            foreach (var a in new[] { -0.5, 0.0, 0.5 })
            {
                p.Add(Seam(On(0.022, 0.036, a - 0.02 + 5 * w, a + 0.02 + 5 * w, 0.08, 0.12, MachineLook.Steel, "steel"), face)); // (the plinth's bolts)
                p.Add(Seam(On(0.03, 0.044, a - 0.02 + 5 * w, a + 0.02 + 5 * w, 0.86 - 10 * v, 0.9 - 10 * v, MachineLook.Steel, "steel"), face)); // (the girder's bolts)
            }
            // The joint strip on top, where a block of its own square is welded on this face.
            double[] smn = new double[3], smx = new double[3];
            (smn[n], smx[n]) = sg > 0 ? (e - 0.05, 1.0) : (-1.0, -e + 0.05);
            (smn[2 - n], smx[2 - n]) = (-1.0, 1.0);
            (smn[1], smx[1]) = (t - 0.004, t + 0.02);
            p.Add(Fill(L(smn, smx, Body, paintable: true), face));
        }
        // ---- Alone (a 1 x 1, or a loose block): its pit's lining and its own small crucible on two trunnion stands ----
        p.Add(Alone(L([-0.92, t - 0.004, -0.92], [0.92, t + 0.016, 0.92], Lining, paintable: false))); // (the pit's refractory lining)
        double r = Math.Round(CrucibleR, 3), y0 = CrucibleY0, y1 = CrucibleY1, ty = TrunnionY, h = y1 - y0;
        // Its shell: steel, three courses widening to the rim (FoundryWorks.TaperBottom, TaperMiddle), a dark band over each joint and a dark rim.
        var rs = new[] { Math.Round(r * FoundryWorks.TaperBottom, 3), Math.Round(r * FoundryWorks.TaperMiddle, 3), r };
        for (var k = 0; k < 3; k++)
        {
            var rk = rs[k];
            p.Add(Alone(Tips(L([-rk, y0 + h * k / 3 + (k > 0 ? 0.004 : 0), -rk], [rk, y0 + h * (k + 1) / 3 - (k < 2 ? 0.004 : 0), rk], MachineLook.Steel, "steel", shape: Cyl("+y", 20))))); // (a course)
            if (k > 0)
                p.Add(Alone(Tips(L([-rk - 0.03, y0 + h * k / 3 - 0.05, -rk - 0.03], [rk + 0.03, y0 + h * k / 3 + 0.05, rk + 0.03], MachineLook.Base, shape: Cyl("+y", 20))))); // (a band over the joint)
        }
        p.Add(Alone(Tips(L([-r - 0.03, y1 - 0.12, -r - 0.03], [r + 0.03, y1 - 0.04, r + 0.03], MachineLook.Base, shape: Cyl("+y", 20))))); // (the band round its top)
        p.Add(Alone(Tips(L([-r + 0.04, y1 - 0.008, -r + 0.04], [r - 0.04, y1 + 0.004, r - 0.04], Lining, shape: Cyl("+y", 20))))); // (its refractory rim)
        p.Add(Alone(Tips(L([-r + 0.08, y1 + 0.002, -r + 0.08], [r - 0.08, y1 + 0.016, r - 0.08], Crust, shape: Cyl("+y", 20))))); // (the crust round the melt's edge)
        p.Add(Alone(Tips(L([-r + 0.14, y1 + 0.008, -r + 0.14], [r - 0.14, y1 + 0.022, r - 0.14], MachineLook.Hot, shape: Cyl("+y", 20))))); // (the melt: it glows while its machine works)
        p.Add(Alone(Tips(L([-0.12, y1 - 0.1, -r - SpoutOut], [0.12, y1 - 0.006, -r + 0.02], MachineLook.Base)))); // (the lip's spout, to the front)
        p.Add(Alone(Tips(L([-0.08, y1 - 0.09, -r - SpoutOut + 0.02], [0.08, y1 - 0.02, -r - 0.02], MachineLook.Hot)))); // (its channel)
        foreach (var sx in new[] { -1, 1 })
        {
            double x0 = sx > 0 ? r + 0.04 : -r - 0.04, x1 = sx > 0 ? 0.94 : -0.94;
            p.Add(Alone(L([Math.Min(x0, x1), t, -0.14], [Math.Max(x0, x1), ty - 0.1, 0.14], MachineLook.Base))); // (a trunnion stand)
            p.Add(Alone(L([Math.Min(x0, x1) - 0.004, ty - 0.1, -0.16], [Math.Max(x0, x1) + 0.004, ty + 0.12, 0.16], MachineLook.Steel, "steel"))); // (its bearing)
            p.Add(Alone(L([sx > 0 ? rs[1] - 0.02 : -0.94 - 0.006, ty - 0.07, -0.07], [sx > 0 ? 0.94 + 0.006 : -rs[1] + 0.02, ty + 0.07, 0.07], MachineLook.Steel, "steel", shape: Cyl("+x", 12)))); // (the trunnion pin)
        }
        p.Add(Alone(L([0.946, ty - 0.3, -0.3], [0.97, ty + 0.3, 0.3], MachineLook.Accent, shape: Cyl("+x", 16)))); // (the tilt gear's sector, on the +x stand)
        p.Add(Alone(L([0.7, t, 0.3], [0.94, t + 0.24, 0.6], MachineLook.Base))); // (its motor)
        // ---- Merged (a block of a square of 2 x 2 or more): a heat-blackened deck over its pit, the crucible above it drawn by FoundryRender ----
        p.Add(Merged(L([-0.94, t - 0.004, -0.94], [0.94, t + 0.012, 0.94], Soot, "soot")));
        // A HOT plate low on the front either way, and the maker's plate on the back.
        const double fz = -e - 0.012;
        p.Add(Seam(Plate([0.5, 0.3, fz - 0.004], [0.8, 0.42, fz]), "-z"));
        p.Add(Seam(L([0.55, 0.33, fz - 0.016], [0.75, 0.39, fz - 0.004], MachineLook.Ink), "-z"));
        p.Add(Seam(L([-0.3, 0.3, e], [0.3, 0.44, e + 0.012], MachineLook.In), "+z"));
        p.Add(Seam(L([-0.24, 0.34, e + 0.012], [0.24, 0.4, e + 0.024], MachineLook.Ink), "+z"));
        p.Sock("+x", [1, SockY, 0], "+x");
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("+z", [0, SockY, 1], "+z");
        p.Sock("-z", [0, SockY, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(3000, 5600, 10, 9);
        p.Set(("machineBlock", Kw(("family", "foundry"), ("role", "block"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    private static PyDict? Plate(double[] mn, double[] mx) => MachineLook.D(mn, mx, MachineLook.Accent);

    // ---- The Mould Line ----------------------------------------------------------------------------------------------------------

    /// <summary>The moulds on a Mould Line (its frame: x, z from its middle; FoundryRender fills them): two rows of four along x.</summary>
    public static readonly (double X0, double Z0, double X1, double Z1)[] Moulds = MakeMoulds();

    private static (double, double, double, double)[] MakeMoulds()
    {
        var list = new List<(double, double, double, double)>();
        foreach (var z in new[] { -0.42, 0.42 })
            foreach (var x in new[] { -0.66, -0.22, 0.22, 0.66 })
                list.Add((x - 0.18, z - 0.28, x + 0.18, z + 0.28));
        return [.. list];
    }

    private static Part MouldLine()
    {
        var p = new Part("Mould Line", Small, "Foundries",
            "A Foundry's end piece: weld one on the far (+x) edge of a crucible square, one a row. Its moulds take the colour of what the Foundry makes as each pour reaches them. Looks only: it adds nothing to the rate.",
            group: "Production", color: MachineLook.Base);
        const double e = 0.997, top = LineTop;
        p.Add(Box([-e, 0.003, -e], [e, 0.194, e], MachineLook.Base)); // (its top 6 mm under a Crucible Block's skirt's, which reaches over it)
        p.Add(Box([-0.9, 0.194, -0.9], [0.9, top - 0.06, 0.9], Trim));
        p.Add(Box([-0.94, top - 0.06, -0.94], [0.94, top - 0.03, 0.94], MachineLook.Base)); // (the deck the moulds stand on)
        // The moulds: a dark sand tray each, on the deck; the rails either side they run on and their rollers.
        foreach (var (x0, z0, x1, z1) in Moulds)
        {
            p.Add(L([x0, top - 0.034, z0], [x1, top - 0.004, z1], MachineLook.Steel, "dirty steel")); // (its steel box)
            p.Add(L([x0 + 0.03, top - 0.008, z0 + 0.03], [x1 - 0.03, top + 0.004, z1 - 0.03], Sand)); // (its sand: FoundryRender fills it)
        }
        foreach (var z in new[] { -0.97, 0.97 })
        {
            p.Add(L([-e, top - 0.12, z - 0.026], [e, top - 0.03, z + 0.026], MachineLook.Steel, "steel")); // (a rail)
            for (var k = 0; k < 5; k++)
            {
                var x = -0.8 + k * 0.4;
                p.Add(L([x - 0.06, top - 0.2, z - 0.03 + (z > 0 ? 0.004 : -0.004)], [x + 0.06, top - 0.08, z + 0.03 + (z > 0 ? 0.004 : -0.004)], MachineLook.Base, shape: Cyl(z > 0 ? "+z" : "-z", 12))); // (a roller's end)
            }
        }
        // Hazard paint on its rim (it burns), HOT on its far end, the frame's bolts and its legs' plates.
        MachineLook.Hazard(p, [-e - 0.012, 0.06, -e - 0.012], [e + 0.012, 0.18, -e]);
        MachineLook.Hazard(p, [-e - 0.012, 0.06, e], [e + 0.012, 0.18, e + 0.012]);
        p.Add(L([e + 0.003, 0.3, -0.3], [e + 0.015, 0.42, 0.3], MachineLook.Accent)); // (HOT)
        p.Add(L([e + 0.015, 0.33, -0.22], [e + 0.027, 0.39, 0.22], MachineLook.Ink));
        foreach (var x in new[] { -0.6, 0.0, 0.6 })
            foreach (var z in new[] { -0.9 - 0.012, 0.9 })
                p.Add(L([x - 0.03, 0.5, z], [x + 0.03, 0.56, z + 0.012], MachineLook.Steel, "steel")); // (its frame's bolts)
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(1000, 1800, 6, 6);
        p.Set(("machineBlock", Kw(("family", "foundry"), ("role", "end"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o)
    {
        foreach (var p in new[] { Core(), CrucibleBlock(), MouldLine() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
