using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Smelter on the block system (docs/sandbox/constructs/production.md "Refiners as block machines"; the design "Smelters and refiners as
/// block machines", drawings "Smelter sizes" and "Smelter works"; MachineBlocks.Squares, ConstructRefining.RefineOf, SmelterRender): three
/// parts in Constructs/Parts/Refining, beside the Foundry's and the Sawmill's tier files (machine_tiers' own, so this writes its files one by
/// one and owns no folder).
/// <list type="bullet">
/// <item>The Smelter Core, 2 x 2 x 2 m: a stepped hopper on top takes Chute in, the screen and its own small glowing hearth on the front (-z),
/// Chute out on its free side (-x). Nothing on it moves. On its own it is the old tier 1 Smelter: 2 metal a second from iron ore.</item>
/// <item>The Hearth Block, 2 x 2 m and 3 m tall, no power of its own, welded on the Core's +x side: the Core splits its blocks into squares
/// (1 x 1 up to 5 x 5) and each is one furnace. Alone (a 1 x 1, or loose) it is a small furnace: a stub stack 2 m over its top and a
/// glowing door ("alone" boxes). In a square of 2 or more its outer faces' cladding makes one mass ("seam"), and each front-row block shows
/// a charge door ("merged"); the stacks, the taphole, the runner, the charging belt and the hot-blast main are drawn by SmelterRender.</item>
/// <item>The Casting Bed, 2 x 2 m and 0.5 m tall, the optional end piece (family "smelter"): one per row past a square's far +x edge. Its
/// pig moulds fill orange on each tap and cool to grey (drawn by SmelterRender). Looks only.</item>
/// </list>
/// The old tier 2 to 5 Smelters went (a saved one loads as the Core: ConstructParts.Renamed).
/// </summary>
public sealed class SmelterParts : IPartGenerator
{
    public string Name => "smelter_parts";

    private const string Out = "Constructs/Parts/Refining";
    public const string Body = "#9a4a2c"; // the Smelters' colour: fire brick on the furnaces, the Core's cladding
    private const string Trim = "#6e3420";
    private const string Brick = "#7d3a24"; // the furnace's darker courses
    private const string Sand = "#a08a64", Soot = "#2b2b2b";
    public const double SockY = 0.25; // the side sockets' height on the Core, a block and the Casting Bed (they weld at one height)
    public const double BlockTop = 3.0, CoreTop = 2.0, BedTop = 0.42; // (SmelterRender draws over these)
    public const int OutBin = 400, InBin = 200; // the Core's bins at x1 (the old tier 1 Smelter's), grown by its squares' rate
    private const int MaxCells = 50, MaxSide = 10;

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static PyDict? Seam(PyDict? box, params string[] faces) => box?.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    private static PyDict? Fill(PyDict? box, string face) => box?.Update(("seamFill", face));

    private static PyDict? Alone(PyDict? box) => box?.Update(("alone", true));

    private static PyDict? Merged(PyDict? box) => box?.Update(("merged", true));

    private static PyDict? Work(PyDict? b, string kind) => MachineLook.Work(b, kind);

    // ---- The Core ------------------------------------------------------------------------------------------------------------------

    private static Part Core()
    {
        var p = new Part("Smelter", Small, "Smelters",
            "A Smelter Core: smelts ore to metal, copper, gold and levite, sand to glass, dirt to hydrogen and stone to oxidiser. On its own it makes 2 metal a second. Weld Hearth Blocks on its +x side: they split into square furnaces (1 x 1 up to 5 x 5) that all work at once, a bigger square more a block (a 3 x 3 makes it x16.75), up to 50 blocks. Needs power: a share for it and each block.",
            role: "storage", group: "Production", color: Body);
        p.Set(("tier", 1)); // (a tier 1 machine's recipe: nothing a tier 1 machine can't make, so survival can start; Assembling.LongTail)
        const double e = 0.997;
        // Solid: the plinth, the body between four corner posts, the top deck and the stepped hopper (Chute in on its lid).
        p.Add(Box([-e, 0.003, -e], [e, 0.14, e]));
        p.Add(Box([-0.94, 0.14, -0.94], [0.94, 1.86, 0.94]));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? e - 0.12 : -e, z0 = sz > 0 ? e - 0.12 : -e;
                p.Add(Box([x0, 0.14, z0], [x0 + 0.12, 1.86, z0 + 0.12], Trim));
            }
        p.Add(Box([-e, 1.86, -e], [e, CoreTop, e], Trim));
        p.Add(Box([-0.42, CoreTop, -0.42], [0.42, 2.22, 0.42]));
        p.Add(Box([-0.55, 2.22, -0.55], [0.55, 2.45, 0.55]));
        p.Add(Box([-0.66, 2.45, -0.66], [0.66, 2.68, 0.66]));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, SockY, 0], "+x"); // (the bed's first Hearth Block)
        p.Stats(3000, 6000, 10, 9);
        var bulk = Kw(("material", "any"), ("capacity", 60000), ("min", Py.L(-0.9, 0.16, -0.55)), ("max", Py.L(-0.3, 0.95, 0.55)));
        var refine = Kw(("machine", "smelter"), ("capacity", 30000), ("bin", InBin), ("outBin", OutBin), ("min", Py.L(-0.38, 2.02, -0.38)), ("max", Py.L(0.38, 2.64, 0.38)));
        p.Set(("power", 3000), ("storage", Kw(("bulk", bulk), ("refine", refine))),
            ("machineBlock", Kw(("family", "smelter"), ("role", "core"), ("origin", Py.L(0, 1, 0)), ("squares", true), ("maxCells", MaxCells), ("maxSide", MaxSide))),
            ("config", Py.L(
                Kw(("key", "recipe"), ("type", "choice"), ("label", "Recipe"), ("group", "Production"), ("tooltip", "Auto: what is in the input picks it. Set: only its input goes in.")),
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(
                Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(0, 2.68, 0)), ("face", "+y")),
                Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(-e, 0.55, 0)), ("face", "-x")))),
            ("centreSockets", false), ("collider", "boxes"));
        // The control face (-z, the furnaces' front): the screen with its lamp and button shelf over the hearth.
        var keepClear = new List<(double[], string)> { ([1, SockY, 0], "+x"), ([0, 2.68, 0], "+y"), ([-e, 0.55, 0], "-x"), ([0, 0, 0], "-y") };
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-z"], rows: 5, width: 0.78, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        panel["touch"] = Py.L(Kw(("action", "machine.recipe"), ("at", Py.L(0.02, 0.02, 0.36, 0.14)), ("label", "Recipe")));
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -e, e, -e, e, 0, 0.12, 0.012);
        // Its small hearth on the front, low beside the screen: a cast frame, the fire door's glow (while it works), a sill and a HOT sign.
        const double hz = -e - 0.012;
        p.Add(L([0.12, 0.3, hz - 0.03], [0.78, 0.36, hz], MachineLook.Base)); // (the sill)
        p.Add(L([0.12, 0.88, hz - 0.03], [0.78, 0.94, hz], MachineLook.Base)); // (the lintel)
        p.Add(L([0.12, 0.36, hz - 0.03], [0.18, 0.88, hz], MachineLook.Base));
        p.Add(L([0.72, 0.36, hz - 0.03], [0.78, 0.88, hz], MachineLook.Base));
        p.Add(Work(L([0.18, 0.36, hz - 0.016], [0.72, 0.88, hz], MachineLook.Hot), "glow"));
        foreach (var x in new[] { 0.3, 0.45, 0.6 })
            p.Add(L([x - 0.012, 0.4, hz - 0.04], [x + 0.012, 0.84, hz - 0.016], MachineLook.Base)); // (its grate's bars)
        p.Add(L([0.28, 0.98, hz - 0.004], [0.62, 1.1, hz], MachineLook.Accent)); // (HOT)
        p.Add(L([0.34, 1.01, hz - 0.016], [0.56, 1.07, hz - 0.004], MachineLook.Ink));
        // Steel bands round the body (the furnace's own tie straps) on its back and free side, bolted at the posts.
        foreach (var y in new[] { 0.55, 1.45 })
        {
            p.Add(L([-0.88, y, e - 0.004], [0.88, y + 0.08, e + 0.014], MachineLook.Steel, "steel")); // (+z)
            p.Add(L([-e - 0.014, y, -0.88], [-e + 0.004, y + 0.08, -0.3], MachineLook.Steel, "steel")); // (-x, either side of the Chute out)
            p.Add(L([-e - 0.014, y, 0.3], [-e + 0.004, y + 0.08, 0.88], MachineLook.Steel, "steel"));
        }
        // The Chute out on the free side (the shared port look) and its label; the Chute in's collar on the hopper's lid, its label.
        MachineLook.Port(p, [-e, 0.55, 0], 0, -1, 0.16, 0.12, false);
        p.Add(L([-e - 0.014, 0.98, -0.3], [-e + 0.002, 1.16, 0.3], MachineLook.Out)); // (OUT)
        p.Add(L([-e - 0.026, 1.03, -0.22], [-e - 0.014, 1.11, 0.22], MachineLook.Ink));
        MachineLook.Port(p, [0, 2.68, 0], 1, 1, 0.2, 0.1, true);
        p.Add(L([-0.4, 2.47, -0.672], [0.4, 2.62, -0.66], MachineLook.In)); // (IN, on the hopper's front)
        p.Add(L([-0.3, 2.51, -0.684], [0.3, 2.58, -0.672], MachineLook.Ink));
        // The hopper's discharge spout on its +x side: where the charging belt (SmelterRender) takes its ore up onto the furnaces.
        p.Add(L([0.42, 2.06, -0.22], [0.62, 2.2, 0.22], MachineLook.Base));
        p.Add(L([0.55, 2.22, -0.25], [0.7, 2.3, 0.25], MachineLook.Steel, "steel"));
        // Its back (+z): the gauges (furnace and blast temperature), the mains box and the conduit up its corner, louvres.
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
        // The top deck's edge in steel, a hand rail stub, and bolts along the plinth.
        p.Add(L([-e - 0.012, 1.9, -e - 0.012], [e + 0.012, 1.96, -e + 0.004], MachineLook.Steel, "steel"));
        p.Add(L([-e - 0.012, 1.9, e - 0.004], [e + 0.012, 1.96, e + 0.012], MachineLook.Steel, "steel"));
        foreach (var x in new[] { -0.7, -0.3, 0.3, 0.7 })
        {
            p.Add(L([x - 0.02, 0.05, -e - 0.024], [x + 0.02, 0.09, -e - 0.008], MachineLook.Steel, "steel"));
            p.Add(L([x - 0.02, 0.05, e + 0.008], [x + 0.02, 0.09, e + 0.024], MachineLook.Steel, "steel"));
        }
        return p;
    }

    // ---- The Hearth Block --------------------------------------------------------------------------------------------------------

    private static Part HearthBlock()
    {
        var p = new Part("Hearth Block", Small, "Smelters",
            "Weld it on a Smelter Core's +x side, or beside another Hearth Block: the Core splits its blocks into squares, each one furnace, and every square works at once. Each block of an n x n square adds 1 + n/4 to the Core's rate (a lone block 1.25, a 3 x 3's 1.75 each), so bigger squares pay more. A wrecked block keeps its square and adds nothing until mended. No power of its own: the Core draws a share for it.",
            group: "Production", color: Body);
        const double e = 0.997, t = BlockTop;
        p.Add(Box([-e, 0.003, -e], [e, t, e]));
        // Each outer face: the furnace's steel skirt, its buckstays at the face's ends, the cap angle round the top, a tie strap and the
        // plinth's bolts ("seam": drawn only where no block of its own square is welded on). Where one is, a strip over the joint on top
        // ("seamFill") closes the 6 mm between them, so a square reads as one furnace.
        foreach (var (n, sg, face) in new[] { (0, 1, "+x"), (0, -1, "-x"), (2, 1, "+z"), (2, -1, "-z") })
        {
            PyDict? On(double out0, double out1, double along0, double along1, double y0, double y1, string colour, string? finish = null, bool paint = false)
            {
                // out0..out1: out from the body's face (0 on it); along: the other horizontal axis.
                double[] mn = new double[3], mx = new double[3];
                (mn[n], mx[n]) = sg > 0 ? (e + out0, e + out1) : (-e - out1, -e - out0);
                var o = 2 - n;
                (mn[o], mx[o]) = (along0, along1);
                (mn[1], mx[1]) = (y0, y1);
                return L(mn, mx, colour, finish, paint);
            }
            p.Add(Seam(On(-0.004, 0.022, -e, e, 0.0, 0.24, MachineLook.Base, "dirty steel"), face));
            foreach (var a in new[] { -e + 0.02, e - 0.15 })
                p.Add(Seam(On(-0.004, 0.06, a, a + 0.13, 0.24, t - 0.1, MachineLook.Base), face)); // (buckstays)
            p.Add(Seam(On(-0.004, 0.034, -e, e, t - 0.1, t - 0.004, MachineLook.Base), face)); // (the cap angle)
            p.Add(Seam(On(-0.004, 0.02, -e + 0.15, e - 0.15, 1.62, 1.7, MachineLook.Steel, "steel"), face)); // (a tie strap)
            p.Add(Seam(On(-0.004, 0.014, -e + 0.15, e - 0.15, 0.9, 0.98, Brick, paint: true), face)); // (a darker course)
            p.Add(Seam(On(-0.004, 0.014, -e + 0.15, e - 0.15, 2.3, 2.38, Brick, paint: true), face));
            foreach (var a in new[] { -0.5, 0.0, 0.5 })
                p.Add(Seam(On(0.022, 0.036, a - 0.02, a + 0.02, 0.1, 0.14, MachineLook.Steel, "steel"), face)); // (the plinth's bolts)
            // The joint strip on top, where a block of its own square is welded on this face.
            double[] smn = new double[3], smx = new double[3];
            (smn[n], smx[n]) = sg > 0 ? (e - 0.05, 1.0) : (-1.0, -e + 0.05);
            (smn[2 - n], smx[2 - n]) = (-1.0, 1.0);
            (smn[1], smx[1]) = (t - 0.004, t + 0.02);
            p.Add(Fill(L(smn, smx, Body, paintable: true), face));
        }
        // The top: a darker crown of brick with a raised rim round it (looks over the body's top).
        p.Add(L([-0.8, t - 0.004, -0.8], [0.8, t + 0.016, 0.8], Brick, paintable: true));
        // ---- Alone (a 1 x 1, or a loose block): its own small furnace ----
        const double fz = -e - 0.012; // (the front face, -z)
        p.Add(Alone(Seam(L([-0.42, 0.8, fz - 0.05], [0.42, 0.86, fz], MachineLook.Base), "-z"))); // (the door's frame)
        p.Add(Alone(Seam(L([-0.42, 1.56, fz - 0.05], [0.42, 1.62, fz], MachineLook.Base), "-z")));
        p.Add(Alone(Seam(L([-0.42, 0.86, fz - 0.05], [-0.36, 1.56, fz], MachineLook.Base), "-z")));
        p.Add(Alone(Seam(L([0.36, 0.86, fz - 0.05], [0.42, 1.56, fz], MachineLook.Base), "-z")));
        p.Add(Alone(Seam(Work(L([-0.36, 0.86, fz - 0.02], [0.36, 1.56, fz], MachineLook.Hot), "glow"), "-z")));
        p.Add(Alone(Seam(L([-0.06, 1.0, fz - 0.07], [0.06, 1.12, fz - 0.05], MachineLook.Accent), "-z"))); // (its latch)
        p.Add(Alone(Seam(L([-0.14, 0.26, fz - 0.16], [0.14, 0.34, fz], MachineLook.Base), "-z"))); // (the taphole's spout)
        p.Add(Alone(Seam(Work(L([-0.06, 0.34, fz - 0.03], [0.06, 0.46, fz], MachineLook.Hot), "glow"), "-z"))); // (the taphole)
        // Its stub stack 2 m over the top at the back: a base collar, the stack, two collars and a sooty lip; smoke while it works.
        const double sz = 0.45, sr = 0.24;
        p.Add(Alone(L([-0.34, t, sz - 0.34], [0.34, t + 0.16, sz + 0.34], MachineLook.Base, shape: Cyl("+y", 16))));
        p.Add(Alone(L([-sr, t + 0.16, sz - sr], [sr, t + 2.0, sz + sr], Body, paintable: true, shape: Cyl("+y", 16))));
        foreach (var y in new[] { t + 0.75, t + 1.4 })
            p.Add(Alone(L([-sr - 0.04, y, sz - sr - 0.04], [sr + 0.04, y + 0.08, sz + sr + 0.04], MachineLook.Base, shape: Cyl("+y", 16))));
        p.Add(Alone(L([-sr - 0.02, t + 1.88, sz - sr - 0.02], [sr + 0.02, t + 2.0, sz + sr + 0.02], Soot, "soot", shape: Cyl("+y", 16))));
        p.Add(Alone(Work(L([-sr + 0.02, t + 2.0, sz - sr + 0.02], [sr - 0.02, t + 2.04, sz + sr - 0.02], Soot, "soot", shape: Cyl("+y", 16)), "smoke")));
        // ---- Merged (a block of a square of 2 x 2 or more): a charge door on the furnace's front, one a column ----
        p.Add(Merged(Seam(L([-0.42, 1.58, fz - 0.06], [0.42, 1.64, fz], MachineLook.Base), "-z")));
        p.Add(Merged(Seam(L([-0.42, 2.22, fz - 0.06], [0.42, 2.28, fz], MachineLook.Base), "-z")));
        p.Add(Merged(Seam(L([-0.42, 1.64, fz - 0.06], [-0.36, 2.22, fz], MachineLook.Base), "-z")));
        p.Add(Merged(Seam(L([0.36, 1.64, fz - 0.06], [0.42, 2.22, fz], MachineLook.Base), "-z")));
        p.Add(Merged(Seam(Work(L([-0.36, 1.64, fz - 0.022], [0.36, 2.22, fz], MachineLook.Hot), "glow"), "-z")));
        p.Add(Merged(Seam(L([-0.08, 1.8, fz - 0.08], [0.08, 1.9, fz - 0.06], MachineLook.Accent), "-z"))); // (its handle)
        // A HOT plate low on the front either way, and the maker's plate on the back.
        p.Add(Seam(L([0.5, 0.42, fz - 0.004], [0.8, 0.54, fz], MachineLook.Accent), "-z"));
        p.Add(Seam(L([0.55, 0.45, fz - 0.016], [0.75, 0.51, fz - 0.004], MachineLook.Ink), "-z"));
        p.Add(Seam(L([-0.3, 0.4, e], [0.3, 0.54, e + 0.012], MachineLook.In), "+z"));
        p.Add(Seam(L([-0.24, 0.44, e + 0.012], [0.24, 0.5, e + 0.024], MachineLook.Ink), "+z"));
        p.Sock("+x", [1, SockY, 0], "+x");
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("+z", [0, SockY, 1], "+z");
        p.Sock("-z", [0, SockY, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(2600, 4800, 10, 9);
        p.Set(("machineBlock", Kw(("family", "smelter"), ("role", "block"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    // ---- The Casting Bed ---------------------------------------------------------------------------------------------------------

    /// <summary>The pig moulds on a Casting Bed (its frame: x, z from its middle; SmelterRender fills them): the sow along x at z, and pigs either side.</summary>
    public static readonly (double X0, double Z0, double X1, double Z1)[] Moulds = MakeMoulds();

    private static (double, double, double, double)[] MakeMoulds()
    {
        var list = new List<(double, double, double, double)> { (-0.9, -0.08, 0.9, 0.08) }; // (the sow, the runner's end along the bed)
        foreach (var x in new[] { -0.66, -0.22, 0.22, 0.66 })
            foreach (var s in new[] { -1, 1 })
                list.Add((x - 0.14, s > 0 ? 0.14 : -0.76, x + 0.14, s > 0 ? 0.76 : -0.14));
        return [.. list];
    }

    private static Part CastingBed()
    {
        var p = new Part("Casting Bed", Small, "Smelters",
            "A Smelter's end piece: weld one on the far (+x) edge of a furnace square, one a row. Its pig moulds fill orange as each furnace taps and cool to grey. Looks only: it adds nothing to the rate.",
            group: "Production", color: MachineLook.Base);
        const double e = 0.997;
        p.Add(Box([-e, 0.003, -e], [e, 0.3, e], MachineLook.Base));
        p.Add(Box([-0.94, 0.3, -0.94], [0.94, BedTop, 0.94], Sand));
        // The sand bed's steel kerb round it, hazard paint on its rim (it burns), the moulds pressed into the sand, a HOT plate.
        p.Add(L([-e, 0.3, -e], [e, BedTop + 0.04, -0.94], MachineLook.Steel, "dirty steel"));
        p.Add(L([-e, 0.3, 0.94], [e, BedTop + 0.04, e], MachineLook.Steel, "dirty steel"));
        p.Add(L([-e, 0.3, -0.94], [-0.94, BedTop + 0.04, 0.94], MachineLook.Steel, "dirty steel"));
        p.Add(L([0.94, 0.3, -0.94], [e, BedTop + 0.04, 0.94], MachineLook.Steel, "dirty steel"));
        MachineLook.Hazard(p, [-e - 0.012, 0.12, -e - 0.012], [e + 0.012, 0.26, -e]);
        MachineLook.Hazard(p, [-e - 0.012, 0.12, e], [e + 0.012, 0.26, e + 0.012]);
        foreach (var (x0, z0, x1, z1) in Moulds)
            p.Add(L([x0 - 0.02, BedTop - 0.004, z0 - 0.02], [x1 + 0.02, BedTop + 0.012, z1 + 0.02], "#5d5247")); // (a mould's dark sand)
        p.Add(L([0.97 + 0.03, 0.14, -0.3], [1.0 + 0.012, 0.24, 0.3], MachineLook.Accent)); // (HOT, on its far end)
        foreach (var z in new[] { -0.6, 0.0, 0.6 })
            p.Add(L([-0.06, 0.05, z - 0.02], [0.06, 0.09, z + 0.02], MachineLook.Steel, "steel")); // (its anchor bolts' plate line)
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(900, 1600, 6, 6);
        p.Set(("machineBlock", Kw(("family", "smelter"), ("role", "end"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o)
    {
        foreach (var p in new[] { Core(), HearthBlock(), CastingBed() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
