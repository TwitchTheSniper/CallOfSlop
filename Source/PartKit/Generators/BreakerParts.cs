using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;
using BW = CallOfSlop.BreakerWorks;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Scrap Breaker, the block version of the Recycler beside it (docs/sandbox/constructs/production.md "The Scrap Breaker"; the design
/// "Smelters and refiners as block machines", drawing "Scrap Breaker"; MachineBlocks.Squares, BreakerWorks, BreakerRender): two parts in
/// Constructs/Parts/Storage beside the Recycler (this writes its files one by one and owns no folder). The Recycler stays as it is, for
/// small ships.
/// <list type="bullet">
/// <item>The Breaker Core, 2 x 2 x 2 m, 1 t, 0.6 kW, olive like the Recycler: its own 1.6 m mouth on top (a hopper with a white rim and a
/// grizzly over its throat), its bin inside, Chute out on its free side (-x), the screen on its front (-z) and, under it, the hatch the
/// collecting belt brings the yield in by. Nothing on it moves. On its own it is a Recycler with a bigger mouth: a 1 t part in 15 s.</item>
/// <item>The Shredder Block, 2 x 2 m and 2 m tall, 2 t, no power of its own, welded on the Core's +x side: the Core splits its blocks into
/// squares (1 x 1 up to 5 x 5), each one shredder, and the mouth is the whole bed. A block is solid to BreakerWorks.Lid (a hidden box:
/// a wreck rests on the rotors' teeth), its walls up to the rim drawn on its outer faces only ("seam"), so a square reads as one open pit.
/// Alone (a 1 x 1, or loose) it has its own small twin rotors ("alone": BreakerWorks at n 1), spinning while it shreds, the belt under
/// them, the bearings and the drive; in a square of 2 or more one pair of rotors across the square, the belts and the funnel skirt round
/// the bed are BreakerRender's.</item>
/// </list>
/// </summary>
public sealed class BreakerParts : IPartGenerator
{
    public string Name => "breaker_parts";

    private const string Out = "Constructs/Parts/Storage";
    public const string Body = "#5f7a34", Rim = "#7d9a4a"; // the Recycler's olive and its rim
    private const string Rubber = "#1b1d1f";
    public const double SockY = 0.25; // the side sockets' height on the Core and a block (they weld at one height)
    public const int Bin = BW.UnitsPerJob; // the Core's bin at x1 (the Recycler's), grown by its squares' rate up to 60,000
    private const int MaxCells = 50, MaxSide = 10;
    private const double E = 0.997;

    private static double R3(double v) => Math.Round(v, 3);

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static PyDict? Seam(PyDict? box, params string[] faces) => box?.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    private static PyDict? Alone(PyDict? box) => box?.Update(("alone", true));

    private static PyDict? Spin(PyDict? b) => MachineLook.Work(b, "spin", "+x");

    // ---- The Core ------------------------------------------------------------------------------------------------------------------

    private static Part Core()
    {
        var p = new Part("Breaker Core", Small, "Recycling",
            "A Scrap Breaker Core: breaks wrecked parts into material, like a Recycler with a 1.6 m mouth. On its own a 1 t part takes 15 s. Weld Shredder Blocks on its +x side: they split into square shredders (1 x 1 up to 5 x 5), the mouth is the whole bed, and a bigger square shreds more a block (a 2 x 2 makes it x7, its bin 4,200), up to 50 blocks. A chute takes the yield from its Chute out. Needs power: a share for it and each block.",
            role: "storage", group: "Storage", color: Body);
        const double e = E, m = 0.8, top = 1.5;
        var rim = R3(BW.Rim); // (the mouth's half-width, the rim, the body's top under the hopper)
        // Solid: the plinth, the body, its four corner posts, the hopper's four walls round the mouth up to the rim, and the throat's
        // hidden floor where a wreck rests (the grizzly drawn over it).
        p.Add(Box([-e, 0.003, -e], [e, 0.14, e]));
        p.Add(Box([-0.94, 0.14, -0.94], [0.94, top, 0.94]));
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double x0 = sx > 0 ? e - 0.12 : -e, z0 = sz > 0 ? e - 0.12 : -e;
                p.Add(Box([x0, 0.14, z0], [x0 + 0.12, top, z0 + 0.12], MachineLook.Base));
            }
        p.Add(Box([-e, top, -e], [e, rim, -m], Rim));
        p.Add(Box([-e, top, m], [e, rim, e], Rim));
        p.Add(Box([-e, top, -m + 0.004], [-m, rim - 0.004, m - 0.004], Rim));
        p.Add(Box([m, top, -m + 0.004], [e, rim - 0.004, m - 0.004], Rim));
        p.Add(Box([-m + 0.004, top, -m + 0.004], [m - 0.004, R3(BW.Lid), m - 0.004], hidden: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, SockY, 0], "+x"); // (the bed's first Shredder Block)
        p.Stats(1000, 2000, 8, 6);
        var bulk = Kw(("material", "any"), ("capacity", 60000), ("min", Py.L(-0.9, 0.16, -0.9)), ("max", Py.L(0.9, 1.45, 0.9)));
        var recycler = Kw(("min", Py.L(-m, top, -m)), ("max", Py.L(m, R3(BW.MouthTop), m)), ("bin", Bin));
        p.Set(("power", 600), ("storage", Kw(("bulk", bulk), ("recycler", recycler))),
            ("machineBlock", Kw(("family", "breaker"), ("role", "core"), ("origin", Py.L(0, 1, 0)), ("squares", true), ("maxCells", MaxCells), ("maxSide", MaxSide))),
            ("config", Py.L(Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("ports", Py.L(Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(-e, 0.62, 0)), ("face", "-x")))),
            ("centreSockets", false), ("collider", "boxes"));
        // The control face (-z): the screen with its lamp and button shelf, over the belt's hatch.
        var keepClear = new List<(double[], string)> { ([1, SockY, 0], "+x"), ([-e, 0.62, 0], "-x"), ([0, 0, 0], "-y") };
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-z"], rows: 5, width: 0.78, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -e, e, -e, e, 0, 0.12, 0.012);
        const double fz = -e - 0.012;
        // The mouth: the white rim round its top, hazard paint on the front edge a hand reaches over, the throat's dark liner, the grizzly
        // bars across it (the wreck rests on them), and steel angles at the rim's corners.
        p.Add(L([-e - 0.004, rim - 0.004, -e - 0.004], [e + 0.004, rim + 0.024, -m + 0.06], MachineLook.Accent, "hazard paint"));
        p.Add(L([-e - 0.004, rim - 0.004, m - 0.06], [e + 0.004, rim + 0.024, e + 0.004], MachineLook.In));
        p.Add(L([-e - 0.004, rim - 0.008, -m + 0.064], [-m + 0.06, rim + 0.02, m - 0.064], MachineLook.In));
        p.Add(L([m - 0.06, rim - 0.008, -m + 0.064], [e + 0.004, rim + 0.02, m - 0.064], MachineLook.In));
        foreach (var (a0, a1, b0, b1) in new[] { (-m - 0.012, -m + 0.004, -m, m), (m - 0.004, m + 0.012, -m, m) })
        {
            p.Add(L([a0 + 0.016, top + 0.004, b0 + 0.02], [a1 + 0.016, rim - 0.03, b1 - 0.02], MachineLook.Base)); // (the throat's liner, x walls)
            p.Add(L([b0 + 0.02, top + 0.004, a0 + 0.016], [b1 - 0.02, rim - 0.03, a1 + 0.016], MachineLook.Base)); // (z walls)
        }
        p.Add(L([-m + 0.02, top + 0.002, -m + 0.02], [m - 0.02, top + 0.02, m - 0.02], MachineLook.Base, "dirty steel")); // (the throat's floor)
        for (var k = 0; k < 7; k++)
        {
            var z = -0.66 + k * 0.22;
            p.Add(L([-m + 0.03, R3(BW.Lid) - 0.05, z - 0.03], [m - 0.03, R3(BW.Lid) - 0.004, z + 0.03], MachineLook.Steel, "steel")); // (a grizzly bar)
        }
        foreach (var x in new[] { -0.5, 0.0, 0.5 })
            p.Add(L([x - 0.04, R3(BW.Lid) - 0.12, -m + 0.03], [x + 0.04, R3(BW.Lid) - 0.052, m - 0.03], MachineLook.Base)); // (its bearers)
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            p.Add(L([x > 0 ? e - 0.1 : -e - 0.012, top, z > 0 ? e - 0.1 : -e - 0.012], [x > 0 ? e + 0.012 : -e + 0.1, rim - 0.01, z > 0 ? e + 0.012 : -e + 0.1], MachineLook.Steel, "steel"));
        // The belt's hatch low on the front (+x half), where the collecting belt brings the yield in: its frame, a dark opening, an IN band.
        var hx = R3(BW.CoreInletX);
        p.Add(L([hx - 0.34, 0.16, fz - 0.03], [hx + 0.34, 0.2, fz], MachineLook.Base));
        p.Add(L([hx - 0.34, 0.5, fz - 0.03], [hx + 0.34, 0.54, fz], MachineLook.Base));
        p.Add(L([hx - 0.34, 0.2, fz - 0.03], [hx - 0.3, 0.5, fz], MachineLook.Base));
        p.Add(L([hx + 0.3, 0.2, fz - 0.03], [hx + 0.34, 0.5, fz], MachineLook.Base));
        p.Add(L([hx - 0.3, 0.2, fz - 0.006], [hx + 0.3, 0.5, fz], Rubber));
        p.Add(L([hx - 0.3, 0.56, fz - 0.004], [hx + 0.3, 0.64, fz], MachineLook.In));
        p.Add(L([hx - 0.22, 0.585, fz - 0.016], [hx + 0.22, 0.615, fz - 0.004], MachineLook.Ink));
        // KEEP CLEAR plate by it (the rotors), on the front's -x half.
        p.Add(L([-0.85, 0.2, fz - 0.004], [-0.3, 0.36, fz], MachineLook.Accent));
        p.Add(L([-0.8, 0.24, fz - 0.016], [-0.35, 0.32, fz - 0.004], MachineLook.Ink));
        // Steel bands round the body on its back and free side.
        foreach (var y in new[] { 0.42, 1.25 })
        {
            p.Add(L([-0.88, y, e - 0.004], [0.88, y + 0.08, e + 0.014], MachineLook.Steel, "steel")); // (+z)
            p.Add(L([-e - 0.014, y, -0.88], [-e + 0.004, y + 0.08, -0.32], MachineLook.Steel, "steel")); // (-x, either side of the Chute out)
            p.Add(L([-e - 0.014, y, 0.32], [-e + 0.004, y + 0.08, 0.88], MachineLook.Steel, "steel"));
        }
        // The Chute out on the free side (the shared port look) and its OUT label.
        MachineLook.Port(p, [-e, 0.62, 0], 0, -1, 0.16, 0.12, false);
        p.Add(L([-e - 0.014, 1.0, -0.3], [-e + 0.002, 1.16, 0.3], MachineLook.Out));
        p.Add(L([-e - 0.026, 1.04, -0.22], [-e - 0.014, 1.12, 0.22], MachineLook.Ink));
        // Its back (+z): two gauges (bin level and the rotors' load), the mains box and its conduit up the corner, louvres, the maker's plate.
        foreach (var x in new[] { -0.55, -0.2 })
        {
            p.Add(L([x - 0.1, 0.95, e - 0.004], [x + 0.1, 1.15, e + 0.03], "#e8e6df", shape: Cyl("+z", 14)));
            p.Add(L([x - 0.008, 1.03, e + 0.03], [x + 0.008, 1.13, e + 0.044], MachineLook.Ink));
        }
        p.Add(L([0.25, 0.22, e - 0.004], [0.7, 0.4, e + 0.07], MachineLook.Base)); // (the mains box)
        p.Add(L([0.6, 0.4, e + 0.01], [0.66, top - 0.02, e + 0.06], MachineLook.Base, shape: Cyl("+y", 8))); // (its conduit)
        for (var k = 0; k < 4; k++)
            p.Add(L([0.15, 0.6 + k * 0.1, e - 0.004], [0.75, 0.64 + k * 0.1, e + 0.018], MachineLook.Base)); // (louvres)
        p.Add(L([-0.7, 0.22, e - 0.004], [-0.3, 0.36, e + 0.012], MachineLook.In)); // (the maker's plate)
        p.Add(L([-0.64, 0.26, e + 0.012], [-0.36, 0.32, e + 0.024], MachineLook.Ink));
        // Bolts along the plinth, both faces.
        foreach (var x in new[] { -0.7, -0.3, 0.3, 0.7 })
        {
            p.Add(L([x - 0.02, 0.05, -e - 0.024], [x + 0.02, 0.09, -e - 0.008], MachineLook.Steel, "steel"));
            p.Add(L([x - 0.02, 0.05, e + 0.008], [x + 0.02, 0.09, e + 0.024], MachineLook.Steel, "steel"));
        }
        return p;
    }

    // ---- The Shredder Block ---------------------------------------------------------------------------------------------------------

    private static Part ShredderBlock()
    {
        var p = new Part("Shredder Block", Small, "Recycling",
            "Weld it on a Breaker Core's +x side, or beside another Shredder Block: the Core splits its blocks into squares, each one shredder with a pair of toothed rotors, and its mouth is the whole bed: a wreck whose middle lands over any square is taken in. Each block of an n x n square adds 1 + n/4 to the Core's rate (a lone block 1.25, a 3 x 3's 1.75 each), so bigger squares pay more. A wrecked block keeps its square and adds nothing until mended. No power of its own: the Core draws a share for it. The rotors hurt: keep clear while it works.",
            group: "Storage", color: Body);
        const double e = E;
        double rim = R3(BW.Rim), b = R3(BW.BaseTop);
        // Solid: the base under the belt, and the hidden box up to the lid where a wreck rests on the teeth (its walls are looks).
        p.Add(Box([-e, 0.003, -e], [e, b, e], MachineLook.Base));
        p.Add(Box([-e + 0.004, b, -e + 0.004], [e - 0.004, R3(BW.Lid), e - 0.004], hidden: true));
        // Each outer face (drawn only where no block of its own square is welded on: "seam"): the wall plate up to the rim, its ribs and
        // their bolts, the rim's cap angle and hazard paint on its lip, a plate band; the corner posts go with either face.
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
            // (-x and -z faces' dressing sits a few millimetres off the +x and +z faces' in height and along: two squares side by side both
            // draw the faces they meet on, 6 mm apart, and none of their boxes may share a plane.)
            var v = sg < 0 ? 0.004 : 0.0;
            p.Add(Seam(On(-0.06, 0.004, -e + 0.12 + v, e - 0.12 - v, b + v, rim - 0.006 - v, Body, paint: true), face)); // (the wall plate)
            foreach (var a in new[] { -0.45, 0.45 })
            {
                p.Add(Seam(On(0.004, 0.05, a - 0.05 + v, a + 0.05 + v, b + 0.004, rim - 0.08, MachineLook.Base), face)); // (a rib)
                foreach (var y in new[] { 0.7, 1.4 })
                    p.Add(Seam(On(0.05, 0.064, a - 0.02 + v, a + 0.02 + v, y - 0.02 + v, y + 0.02 + v, MachineLook.Steel, "steel"), face)); // (its bolts)
            }
            p.Add(Seam(On(0.004, 0.018, -e + 0.15 + v, -0.52 + v, 1.0 + v, 1.12 + v, Rim, paint: true), face)); // (a plate band, between the ribs)
            p.Add(Seam(On(0.004, 0.018, 0.52 + v, e - 0.15 - v, 1.0 + v, 1.12 + v, Rim, paint: true), face));
            p.Add(Seam(On(-0.07, 0.03, -e + 0.004 + v, e - 0.004 - v, rim - 0.08 + v, rim - 0.004 - v, MachineLook.Base), face)); // (the rim's cap angle)
            p.Add(Seam(On(-0.07, 0.034, -e + 0.008 + v, e - 0.008 - v, rim - 0.002 - v, rim + 0.016 - v, MachineLook.Accent, "hazard paint"), face)); // (hazard paint on its lip)
        }
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            double x0 = sx > 0 ? e - 0.12 : -e - 0.012, z0 = sz > 0 ? e - 0.12 : -e - 0.012, w = sx < 0 || sz < 0 ? 0.003 : 0.0;
            p.Add(Seam(L([x0, b + 0.002 + w, z0], [x0 + 0.132, rim - 0.01 - w, z0 + 0.132], MachineLook.Base), sx > 0 ? "+x" : "-x", sz > 0 ? "+z" : "-z")); // (a corner post)
        }
        // The pit floor either way: a dark liner on the base, the floor every square's belt runs on.
        p.Add(L([-0.94, b - 0.004, -0.94], [0.94, b + 0.002, 0.94], MachineLook.Base, "dirty steel"));
        // KEEP CLEAR on the front and the maker's plate on the back, either way (on each block of a square's outer faces).
        const double fz = -e - 0.012;
        p.Add(Seam(L([0.42, 1.24, fz - 0.004], [0.86, 1.4, fz + 0.004], MachineLook.Accent), "-z"));
        p.Add(Seam(L([0.47, 1.28, fz - 0.016], [0.81, 1.36, fz - 0.004], MachineLook.Ink), "-z"));
        p.Add(Seam(L([-0.3, 0.42, e - 0.004], [0.3, 0.56, e + 0.012], MachineLook.In), "+z"));
        p.Add(Seam(L([-0.24, 0.46, e + 0.012], [0.24, 0.52, e + 0.024], MachineLook.Ink), "+z"));
        // ---- Alone (a 1 x 1, or a loose block): its own small shredder, BreakerWorks at n 1 about its middle ----
        AloneShredder(p);
        p.Sock("+x", [1, SockY, 0], "+x");
        p.Sock("-x", [-1, SockY, 0], "-x");
        p.Sock("+z", [0, SockY, 1], "+z");
        p.Sock("-z", [0, SockY, -1], "-z");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(2000, 3200, 8, 6);
        p.Set(("machineBlock", Kw(("family", "breaker"), ("role", "block"))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// A lone Shredder Block's shredder (BreakerWorks at n 1, square middle 0, 0): two toothed drums along x either side of the middle
    /// ("spin": each tooth bar turns about the drum's axis through its own middle), their bearing blocks, the drive's gearbox at their +x
    /// end with its motor on the rim, the belt under them along z to the front face and its discharge slot there.
    /// </summary>
    private static void AloneShredder(Part p)
    {
        const int n = 1;
        double r = R3(BW.RotorR(n)), t = R3(BW.Teeth(n)), ay = R3(R3(BW.AxisY(n))), gap = R3(R3(BW.Gap(n))), len = R3(BW.RotorLen(n));
        var rows = BW.TeethRows(n);
        double x0 = -len / 2, pitch = len / rows;
        foreach (var (z, back) in new[] { (-gap, false), (gap, true) })
        {
            p.Add(Alone(Spin(L([x0, ay - r * 0.8, z - r * 0.8], [-x0, ay + r * 0.8, z + r * 0.8], MachineLook.Steel, "dirty steel", shape: Cyl("+x", 14))))); // (the drum)
            var count = back ? rows - 1 : rows;
            for (var k = 0; k < count; k++)
            {
                var x = R3(x0 + (k + (back ? 1.0 : 0.5)) * pitch);
                var colour = k % 2 == 0 ? MachineLook.Base : MachineLook.Accent;
                p.Add(Alone(Spin(L([x - 0.035, ay - t, z - 0.06], [x + 0.035, ay + t, z + 0.06], colour)))); // (a tooth bar, up)
                p.Add(Alone(Spin(L([x - 0.031, ay - 0.056, z - t], [x + 0.031, ay + 0.056, z + t], colour)))); // (and across)
            }
            foreach (var sx in new[] { -1, 1 })
            {
                var bx = sx * (len / 2 + 0.12);
                p.Add(Alone(L([bx - 0.1, ay - 0.16, z - 0.16], [bx + 0.1, ay + 0.16, z + 0.16], MachineLook.Base))); // (a bearing block)
                p.Add(Alone(L([bx - 0.1, b0(), z - 0.08], [bx + 0.1, ay - 0.16, z + 0.08], MachineLook.Base))); // (its stand)
            }
        }
        // The drive: its motor on the rim's back corner, a yellow chain guard down from it to the back drum's +x bearing.
        p.Add(Alone(L([0.7, R3(ay + 0.1), R3(gap + 0.17)], [0.86, R3(BW.Rim) - 0.08, 0.6], MachineLook.Accent)));
        p.Add(Alone(L([0.45, R3(BW.Rim) + 0.02, 0.62], [0.9, R3(BW.Rim) + 0.3, 0.9], MachineLook.Base, shape: Cyl("+x", 14)))); // (the motor)
        p.Add(Alone(L([0.36, R3(BW.Rim) + 0.02, 0.66], [0.45, R3(BW.Rim) + 0.26, 0.86], MachineLook.Steel, "steel"))); // (its end bell)
        // The belt under the rotors, along z out of the front face: its two end rollers and three cleats; the discharge slot's dark mouth.
        p.Add(Alone(L([-0.45, R3(BW.BeltY0), -0.92], [0.45, R3(BW.BeltY1), 0.92], Rubber)));
        foreach (var z in new[] { -0.86, 0.86 })
            p.Add(Alone(L([-0.47, R3(BW.BeltY0) - 0.004, z - 0.03], [0.47, R3(BW.BeltY1) + 0.004, z + 0.03], MachineLook.Steel, "steel", shape: Cyl("+x", 10))));
        foreach (var z in new[] { -0.4, 0.0, 0.4 })
            p.Add(Alone(L([-0.43, R3(BW.BeltY1), z - 0.015], [0.43, R3(BW.BeltY1) + 0.03, z + 0.015], MachineLook.Base)));
        p.Add(Alone(Seam(L([-0.42, R3(BW.BaseTop) + 0.004, -E - 0.018], [0.42, 0.5, -E - 0.006], Rubber), "-z")));
        static double b0() => R3(BW.BaseTop) + 0.002;
    }

    public void Make(PartOutput o)
    {
        foreach (var p in new[] { Core(), ShredderBlock() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
