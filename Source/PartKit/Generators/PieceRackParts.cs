using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Piece Rack: Constructs/Parts/PieceRacks (docs/sandbox/constructs/production.md, "Conveyors"). A low steel cradle on two
/// runners, posts and an amber top rail down each side, that takes whole pieces off a conveyor at its Piece in (its -z end) and
/// holds them by kind (<c>storage.pieces</c>: the largest section and length and how many). What it holds is drawn lying on its
/// slats inside its posts, packed by volume into its hold (<c>storage.pieces.hold</c>, written here from the frame: ConstructRackPack.cs).
/// </summary>
public sealed class PieceRackParts : IPartGenerator
{
    public string Name => "piece_rack_parts";

    private const string Out = "Constructs/Parts/PieceRacks";
    private const string Frame = "#35556b", Bar = MachineLook.Base; // (the family's body colour takes paint; the rest is MachineLook's tones)
    // The hold: inside the posts and rails, 3 mm over the slats, past the sill and short of the far posts.
    private const double Side = 0.115, Bed = 0.133, EndIn = 0.074, EndOut = 0.02, Top = 0.01;

    /// <summary>A rack nx by nz small cells (0.25 m) on the ground, ny high, holding pieces up to section by length, count of them.</summary>
    private static Part Rack(string name, int nx, int ny, int nz, double section, double length, int count, string desc)
    {
        var c = Small;
        var p = new Part(name, c, "Extrusion", desc, role: "storage", tool: "welder", color: Frame, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        const double runner = 0.15, high = 0.12, post = 0.08, rail = 0.06;
        // Two runners along its length, and cross bars between them every 1.5 m (into the runners, under their tops).
        p.Add(Box([x0, y0, z0], [x0 + runner, y0 + high, z1]));
        p.Add(Box([x1 - runner, y0, z0], [x1, y0 + high, z1]));
        var bays = Math.Max(2, (int)Math.Round((z1 - z0) / 1.5));
        for (var k = 0; k <= bays; k++)
        {
            var z = z0 + 0.06 + (z1 - z0 - 0.18) * k / bays;
            p.Add(Box([x0 + runner - 0.005, y0 + 0.01, z], [x1 - runner + 0.005, y0 + 0.11, z + 0.12], Bar));
            // Posts each side, from inside the runner to inside the top rail.
            p.Add(Box([x0 + 0.02, y0 + high - 0.005, z], [x0 + 0.02 + post, y1 - rail + 0.005, z + post]));
            p.Add(Box([x1 - 0.02 - post, y0 + high - 0.005, z], [x1 - 0.02, y1 - rail + 0.005, z + post]));
        }
        // Slats the pieces lie on, along its length between the cross bars' tops and the runners' tops.
        for (var k = 1; k < 4; k++)
        {
            var x = x0 + runner + (x1 - x0 - 2 * runner) * k / 4;
            p.Add(MachineLook.D([x - 0.03, y0 + 0.105, z0 + 0.04], [x + 0.03, y0 + 0.13, z1 - 0.04], MachineLook.Steel, "steel"));
        }
        // The amber top rails: what bites a shin in a factory.
        p.Add(MachineLook.D([x0 + 0.01, y1 - rail, z0 + 0.02], [x0 + 0.03 + post, y1, z1 - 0.02], MachineLook.Accent));
        p.Add(MachineLook.D([x1 - 0.03 - post, y1 - rail, z0 + 0.02], [x1 - 0.01, y1, z1 - 0.02], MachineLook.Accent));
        // Fill marks along its length: a dark mark across each rail at every quarter, and a steel load plate on the first post each side.
        for (var k = 1; k < 4; k++)
        {
            var z = z0 + (z1 - z0) * k / 4;
            p.Add(MachineLook.D([x0 + 0.004, y1 - rail - 0.004, z - 0.02], [x0 + 0.032 + post, y1 + 0.004, z + 0.02], MachineLook.Ink));
            p.Add(MachineLook.D([x1 - 0.032 - post, y1 - rail - 0.004, z - 0.02], [x1 - 0.004, y1 + 0.004, z + 0.02], MachineLook.Ink));
        }
        var plateY = y0 + high + (y1 - rail - y0 - high) * 0.55;
        p.Add(MachineLook.D([x0 + 0.006, plateY, z0 + 0.045], [x0 + 0.022, plateY + 0.14, z0 + 0.155], MachineLook.Steel, "steel"));
        p.Add(MachineLook.D([x1 - 0.022, plateY, z0 + 0.045], [x1 - 0.006, plateY + 0.14, z0 + 0.155], MachineLook.Steel, "steel"));
        // The way in: an end sill at -z with a pale mark where the conveyor meets it.
        p.Add(Box([x0 + runner - 0.005, y0 + 0.005, z0], [x1 - runner + 0.005, y0 + 0.1, z0 + 0.05], Bar));
        // (the in side's white band across the sill, hazard paint over it: look.md "Machine look")
        p.Add(MachineLook.D([x0 + runner + 0.01, y0 + 0.02, z0 - 0.006], [x1 - runner - 0.01, y0 + 0.078, z0 + 0.008], MachineLook.In));
        MachineLook.Hazard(p, [x0 + runner + 0.01, y0 + 0.082, z0 - 0.006], [x1 - runner - 0.01, y0 + 0.098, z0 + 0.008]);
        MachineLook.Arrow(p, 1, y0 + high - 0.002, 1, 2, 1, z0 + 0.3, x0 + runner / 2, 0.3, runner * 0.46, MachineLook.In);
        MachineLook.Arrow(p, 1, y0 + high - 0.002, 1, 2, 1, z0 + 0.3, x1 - runner / 2, 0.3, runner * 0.46, MachineLook.In);
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = nx * c.Cell;
        var deep = nz * c.Cell;
        p.Stats(wide * deep * 30, 400 + 40 * wide * deep, 4);
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, section)), ("length", length), ("count", count),
            ("hold", Py.L(Py.L(Py.Round(x0 + Side, 3), Py.Round(y0 + Bed, 3), Py.Round(z0 + EndIn, 3)), Py.L(Py.Round(x1 - Side, 3), Py.Round(y1 - Top, 3), Py.Round(z1 - EndOut, 3)))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + x1) / 2, 3), Py.Round(y0 + 0.05, 3), z0)), ("face", "-z"),
                ("opening", Py.L(section, section))))));
        return p;
    }

    /// <summary>
    /// The Lay-down Pad (docs/sandbox/design/arms-and-cranes.md 3.5, lifters phase 4): an open steel deck, no bars, where pieces
    /// too wide for a rack wait and where Loader Arms pick and put. A Piece Rack by its <c>storage.pieces</c> block (pieces up to
    /// section by up, length, packed by ConstructRackPack in its hold over the deck); four corner posts mark how high it stacks.
    /// </summary>
    private static Part Pad(string name, int nx, int ny, int nz, double section, double up, double length, int count, string desc)
    {
        var c = Small;
        var p = new Part(name, c, "Extrusion", desc, role: "storage", tool: "welder", color: Frame, group: "Production");
        double x0 = c.Lo(), x1 = c.Hi(nx), y0 = c.Lo(), y1 = c.Hi(ny), z0 = c.Lo(), z1 = c.Hi(nz);
        const double deck = 0.1, post = 0.12, kerb = 0.1;
        var top = y0 + deck;
        // The deck: one plate, and a raised kerb round its edge in hazard paint (set 4 mm inside the deck's sides, its foot in the deck).
        p.Add(Box([x0, y0, z0], [x1, top, z1]));
        MachineLook.Hazard(p, [x0 + 0.004, top - 0.005, z0 + 0.004 + kerb], [x0 + 0.004 + kerb, top + 0.035, z1 - 0.004 - kerb]);
        MachineLook.Hazard(p, [x1 - 0.004 - kerb, top - 0.005, z0 + 0.004 + kerb], [x1 - 0.004, top + 0.035, z1 - 0.004 - kerb]);
        // (the -z end's kerb is the in side's white band, hazard over its top: look.md "Machine look"; the +z end's is hazard)
        p.Add(MachineLook.D([x0 + 0.004, top - 0.005, z0 + 0.004], [x1 - 0.004, top + 0.03, z0 + 0.004 + kerb], MachineLook.In));
        MachineLook.Hazard(p, [x0 + 0.01, top + 0.03, z0 + 0.008], [x1 - 0.01, top + 0.042, z0 + kerb]);
        MachineLook.Hazard(p, [x0 + 0.004, top - 0.005, z1 - 0.004 - kerb], [x1 - 0.004, top + 0.035, z1 - 0.004]);
        // Corner posts to the stacking height, each with an amber cap and a dark mark at every metre.
        foreach (var (px, pz) in new[] { (x0 + 0.02, z0 + 0.02), (x1 - 0.02 - post, z0 + 0.02), (x0 + 0.02, z1 - 0.02 - post), (x1 - 0.02 - post, z1 - 0.02 - post) })
        {
            p.Add(Box([px, top + 0.03, pz], [px + post, y1 - 0.1, pz + post], Bar));
            p.Add(MachineLook.D([px - 0.006, y1 - 0.1 - 0.005, pz - 0.006], [px + post + 0.006, y1, pz + post + 0.006], MachineLook.Accent));
            for (var m = 1.0; m < y1 - y0 - 0.3; m += 1.0)
                p.Add(MachineLook.D([px - 0.004, y0 + m - 0.02, pz - 0.004], [px + post + 0.004, y0 + m + 0.02, pz + post + 0.004], MachineLook.Ink));
        }
        // Deck seams every 2 m across: thin dark strips laid on it.
        for (var z = z0 + 2.0; z < z1 - 0.5; z += 2.0)
            p.Add(MachineLook.D([x0 + kerb + 0.02, top - 0.002, z - 0.015], [x1 - kerb - 0.02, top + 0.006, z + 0.015], MachineLook.Ink));
        BlockSockets(p, nx, ny, nz, faces: "y", skip: ["+y"], limit: new() { ["-y"] = (3, 6) });
        var wide = nx * c.Cell;
        var deep = nz * c.Cell;
        p.Stats(wide * deep * 40, 300 + 30 * wide * deep, 4);
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, up)), ("length", length), ("count", count),
            ("hold", Py.L(Py.L(Py.Round(x0 + kerb + 0.01, 3), Py.Round(top + 0.045, 3), Py.Round(z0 + kerb + 0.01, 3)), Py.L(Py.Round(x1 - kerb - 0.01, 3), Py.Round(y1 - 0.01, 3), Py.Round(z1 - kerb - 0.01, 3)))), ("open", true))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(Py.Round((x0 + x1) / 2, 3), Py.Round(top + 0.05, 3), z0)), ("face", "-z"),
                ("opening", Py.L(Math.Min(section, 2.0), Math.Min(up, 2.0)))))));
        return p;
    }

    // ---- Pad Tiles (design page drawing 23: Piece Racks fold into Pad Tiles) -------------------------------------------------

    private const string PadsOut = "Constructs/Parts/Pads";
    private const double Deck = 0.2, KerbIn = 0.1, HoldLo = 0.23, PortY = 0.14; // the deck's depth, its kerb's width, the hold's floor over the deck's foot
    // How far the deck stands up on its skirt (Sean, 2026-11-23: the pads raised to the rollers' height): the hold's floor is level with a
    // Roller Tile's rollers (FactoryShellParts.PlinthHigh, 1 m), so a run rolls its pieces straight onto the pad.
    public const double TileRise = 1.0 - HoldLo;

    private static PyDict? Seam(PyDict? box, params string[] faces)
    {
        if (box is null)
            return null;
        box["seam"] = faces.Length == 1 ? faces[0] : Py.L([.. faces.Cast<object?>()]);
        return box;
    }

    /// <summary>Boxes a call adds to <paramref name="p"/> (a port's fitting), made seams on <paramref name="face"/>: hidden where a tile is welded on.</summary>
    private static void SeamAdded(Part p, string face, Action add)
    {
        var from = p.Boxes.Count;
        add();
        for (var k = from; k < p.Boxes.Count; k++)
            Seam(p.Boxes[k], face);
    }

    /// <summary>
    /// The Pad Tile (drawing 23; ConstructPads.cs): a 2 m steel deck on a skirt, its hold's floor 1 m up (level with a Roller Tile run's rollers,
    /// Sean 2026-11-23), no Core. Tiles welded side by side are one pad: the
    /// largest rectangle of them holding the lowest-index tile (at most 16 x 16 and 128 tiles) is one store, 500 pieces a tile, its
    /// hold the whole rectangle. Its stack is 1 m with no mast and 2 m more for each Mast Block of the tallest column stood on it.
    /// Four tread-plate panels on a dark cross of joists; a hazard kerb round each edge and a conveyor port on three of them (the
    /// fourth is its Piece out), all seams: welded tiles show one kerb round the pad and ports only on its open edges. Tie-down rings
    /// and a load plate on the deck. Its sockets: top (a Mast Block or a Rack Side's foot), bottom, and the four sides.
    /// </summary>
    private static Part Tile()
    {
        var p = new Part("Pad Tile", Small, "Extrusion",
            "A 2 m deck where pieces wait, its top level with a Roller Tile run's rollers so a run rolls pieces straight on. Weld tiles side by side into one pad (up to 16 x 16 tiles): one store of 500 pieces a tile, stacked 1 m high, 2 m more for each Mast Block on it. Conveyors end at any open edge; Loader Arms and cranes pick and put; F takes the piece you look at.",
            role: "storage", tool: "welder", color: Frame, group: "Production");
        const double e = 1.0, i = 0.997;
        p.Add(Box([-e, 0, -e], [e, Deck, e], hidden: true));
        // The frame under the deck (dark), the cross of joists over it, and four tread-plate panels between them.
        p.Add(MachineLook.D([-0.99, 0.006, -0.99], [0.99, 0.15, 0.99], Bar));
        p.Add(MachineLook.D([-0.016, 0.148, -0.99], [0.016, 0.18, 0.99], Bar));
        p.Add(MachineLook.D([-0.99, 0.148, -0.016], [-0.016, 0.178, 0.016], Bar));
        p.Add(MachineLook.D([0.016, 0.148, -0.016], [0.99, 0.178, 0.016], Bar));
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            double x0 = sx > 0 ? 0.022 : -0.988, x1 = sx > 0 ? 0.988 : -0.022, z0 = sz > 0 ? 0.022 : -0.988, z1 = sz > 0 ? 0.988 : -0.022;
            p.Add(MachineLook.D([x0, 0.15, z0], [x1, 0.184, z1], Frame, "tread plate", paintable: true));
            // A tie-down ring near each corner, and a drain slot by it.
            double rx = sx * 0.72, rz = sz * 0.72;
            p.Add(MachineLook.D([rx - 0.04, 0.184, rz - 0.012], [rx + 0.04, 0.2, rz + 0.012], MachineLook.Steel, "steel"));
            p.Add(MachineLook.D([rx - 0.012, 0.184, rz - 0.04], [rx + 0.012, 0.198, rz + 0.04], MachineLook.Steel, "steel"));
            p.Add(MachineLook.D([sx * 0.45 - 0.09, 0.184, sz * 0.84 - 0.016], [sx * 0.45 + 0.09, 0.189, sz * 0.84 + 0.016], MachineLook.Ink));
        }
        // The load plate: white, two dark lines (what it takes), by the -x/+z corner.
        p.Add(MachineLook.D([-0.62, 0.184, 0.3], [-0.3, 0.19, 0.46], MachineLook.In));
        p.Add(MachineLook.D([-0.6, 0.19, 0.34], [-0.32, 0.195, 0.36], MachineLook.Ink));
        p.Add(MachineLook.D([-0.6, 0.19, 0.4], [-0.4, 0.195, 0.42], MachineLook.Ink));
        // The kerb round each edge (hazard paint over a dark channel), a seam on its face; the z edges run the tile's whole width, the x
        // edges between them, so kerbs meet end to end along a pad's edge. A white band where a conveyor meets an in edge, and the port.
        foreach (var (n, sg, face) in new[] { (2, -1, "-z"), (2, 1, "+z"), (0, -1, "-x"), (0, 1, "+x") })
        {
            double a0 = n == 2 ? -i : -(e - KerbIn) + 0.004, a1 = -a0; // (along the edge)
            double b0 = sg > 0 ? e - KerbIn : -i, b1 = sg > 0 ? i : -(e - KerbIn); // (across it, from the edge in)
            double[] Mn(double y) => n == 2 ? [a0, y, b0] : [b0, y, a0];
            double[] Mx(double y) => n == 2 ? [a1, y, b1] : [b1, y, a1];
            var channel = MachineLook.D(Mn(0.148), Mx(0.19), Bar);
            p.Add(Seam(channel, face));
            var kerb = MachineLook.D(Mn(0.19), Mx(0.226), MachineLook.Accent, "hazard paint");
            p.Add(Seam(kerb, face));
            if (face == "+z")
                continue; // (the out edge: no in band, no port fitting; the pad's Piece out is there)
            double[] bmn = n == 2 ? [-0.3, 0.226, b0 + 0.01] : [b0 + 0.01, 0.226, -0.3];
            double[] bmx = n == 2 ? [0.3, 0.232, b1 - 0.01] : [b1 - 0.01, 0.232, 0.3];
            p.Add(Seam(MachineLook.D(bmn, bmx, MachineLook.In), face));
            double[] at = n == 2 ? [0, PortY, sg * (e - 0.055)] : [sg * (e - 0.055), PortY, 0];
            SeamAdded(p, face, () => MachineLook.Port(p, at, n, sg, 0.03, 0.05, true));
        }
        // A maker's plate on the -z face of the frame.
        p.Add(Seam(MachineLook.D([0.4, 0.04, -0.996], [0.7, 0.12, -0.99], MachineLook.Steel, "steel"), "-z"));
        // The deck stands on its skirt: everything so far goes up by TileRise, and a dark skirt with a hazard band at its foot carries it
        // (each tile its own: no seams, its sides 5 mm inside the deck's, its top 4 mm into the deck's frame).
        const double R = TileRise, sk = 0.985;
        foreach (var b in p.Boxes)
            foreach (var key in new[] { "min", "max" })
                if (b[key] is List<object?> v)
                    v[1] = Math.Round(Py.D(v[1]) + R, 4);
        p.Add(MachineLook.D([-sk, 0, -sk], [sk, R + 0.01, sk], Bar));
        foreach (var (n, sg) in new[] { (2, -1), (2, 1), (0, -1), (0, 1) })
        {
            double[] mn = n == 2 ? [-sk + 0.02, 0.1, sg > 0 ? sk - 0.002 : -sk - 0.006] : [sg > 0 ? sk - 0.002 : -sk - 0.006, 0.1, -sk + 0.02];
            double[] mx = n == 2 ? [sk - 0.02, 0.18, sg > 0 ? sk + 0.006 : -sk + 0.002] : [sg > 0 ? sk + 0.006 : -sk + 0.002, 0.18, sk - 0.02];
            p.Add(MachineLook.D(mn, mx, MachineLook.Accent, "hazard paint")); // (a hazard band round its foot)
        }
        p.Add(Box([-e, 0, -e], [e, R + 0.004, e], hidden: true)); // (solid down to the floor)
        p.Sock("top", [0, R + Deck, 0], "+y"); // (the sides' sockets stay at the foot: a tile welds to its neighbour's or a Roller Tile's low side)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [e, 0.1, 0], "+x");
        p.Sock("-x", [-e, 0.1, 0], "-x");
        p.Sock("+z", [0, 0.1, e], "+z");
        p.Sock("-z", [0, 0.1, -e], "-z");
        foreach (var (f, x, z) in new[] { ("+x", e, 0.0), ("-x", -e, 0.0), ("+z", 0.0, e), ("-z", 0.0, -e) })
            p.Sock(f + " deck", [x, R + 0.1, z], f); // (at the raised deck's edge: a Rack Side's foot, so its bars stand over the deck)
        p.Stats(160, 420, 4);
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(1.66, 1.0)), ("length", 1.66), ("count", 500),
            ("hold", Py.L(Py.L(-0.83, TileRise + HoldLo, -0.83), Py.L(0.83, TileRise + HoldLo + 1.0, 0.83))), ("open", true))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, Py.Round(TileRise + PortY, 3), -e)), ("face", "-z"), ("opening", Py.L(2.0, 2.0))),
            Kw(("name", "Piece in -x"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(-e, Py.Round(TileRise + PortY, 3), 0)), ("face", "-x"), ("opening", Py.L(2.0, 2.0))),
            Kw(("name", "Piece in +x"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(e, Py.Round(TileRise + PortY, 3), 0)), ("face", "+x"), ("opening", Py.L(2.0, 2.0))))));
        // (the cell origin: a tile's middle rounds to cell 0 and a Mast Block stood on its top to cell 1, as before the rise)
        p.Set(("machineBlock", Kw(("family", "pad"), ("role", "block"), ("cell", 2), ("seed", true), ("origin", Py.L(0, Py.Round(-0.8 + TileRise * 0.75, 3), 0)))), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// The Rack Side (drawing 23): drawn only, a 2 m side of bars welded to a Pad Tile's edge, so a pad reads as a rack where pieces
    /// mustn't roll off. Its foot plate bolts to the tile's side; posts, a mid rail and an amber top rail, steel bars between, a fill mark
    /// at every half metre. Sides welded end to end show one post at each joint: its -z post is a seam (<c>"seamSame"</c>).
    /// </summary>
    private static Part RackSide()
    {
        var p = new Part("Rack Side", Small, "Extrusion",
            "A 2 m side of bars for a Pad Tile's edge: weld it to the tile's side so pieces stay on. Sides in a row join into one fence. It holds nothing itself.",
            tool: "welder", color: Frame, group: "Production");
        const double t = 0.12, h = 1.24, e = 1.0;
        p.Add(Box([0, 0, -e], [t, h, e], hidden: true));
        // The foot plate on the tile's side, bolted every half metre.
        p.Add(MachineLook.D([0.003, 0.012, -0.996], [0.028, 0.19, 0.996], Bar));
        for (var z = -0.75; z <= 0.76; z += 0.5)
            p.Add(MachineLook.D([0.028, 0.08, z - 0.02], [0.044, 0.12, z + 0.02], MachineLook.Steel, "steel"));
        // Posts: the -z end's a seam (a side welded on there brings its +z post), the middle one, the +z end's.
        p.Add(Seam(MachineLook.D([0.03, 0.02, -0.996], [0.112, h - 0.065, -0.91], Frame, paintable: true), "-z"));
        p.Add(MachineLook.D([0.03, 0.02, -0.04], [0.112, h - 0.065, 0.04], Frame, paintable: true));
        p.Add(MachineLook.D([0.03, 0.02, 0.91], [0.112, h - 0.065, 0.996], Frame, paintable: true));
        // Rails: a mid rail in the body colour, the amber top rail along the whole side.
        p.Add(MachineLook.D([0.036, 0.66, -0.905], [0.106, 0.72, -0.045], Frame, paintable: true));
        p.Add(MachineLook.D([0.036, 0.66, 0.045], [0.106, 0.72, 0.905], Frame, paintable: true));
        p.Add(MachineLook.D([0.024, h - 0.065, -0.998], [0.118, h - 0.004, 0.998], MachineLook.Accent));
        // Steel bars between the posts, and a fill mark across each post at every half metre.
        foreach (var zc in new[] { -0.7, -0.475, -0.25, 0.25, 0.475, 0.7 })
            p.Add(MachineLook.D([0.058, 0.2, zc - 0.014], [0.084, h - 0.065, zc + 0.014], MachineLook.Steel, "steel"));
        foreach (var y in new[] { 0.5, 1.0 })
        {
            p.Add(MachineLook.D([0.026, y - 0.016, -0.06], [0.116, y + 0.016, 0.06], MachineLook.Ink));
            p.Add(MachineLook.D([0.026, y - 0.016, 0.89], [0.116, y + 0.016, 0.998], MachineLook.Ink));
        }
        p.Sock("in", [0, 0.1, 0], "-x");
        p.Sock("+z", [t / 2, 0.6, e], "+z");
        p.Sock("-z", [t / 2, 0.6, -e], "-z");
        p.Stats(40, 160, 4);
        p.Set(("seamSame", true), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    // ---- Roller Tiles and the Turntable Tile (drawing 23): what carries a piece a conveyor can't ----------------------------------

    public const double RollTop = OutTableParts.WorkY; // the rollers' tops: the bed machines' work height, so an Out Table butts straight on
    private const double RollE = 0.997, RollFrame = 0.8, RollR = 0.07, RollBeam = 0.1;
    public const double RunY = RollTop - 0.06; // a run's line (ConstructConveyor.BedTop under the rollers' tops): its ports' height

    /// <summary>The frame, side beams, feet and kick plates a Roller Tile and a Turntable Tile share; the beams' outer faces are seams.</summary>
    private static void RollerBody(Part p, bool beams)
    {
        const double e = RollE;
        p.Add(Box([-e, 0.003, -e], [e, RunY + 0.01, e], hidden: true)); // (solid to under the rollers: a person stands on the bed)
        p.Add(MachineLook.D([-e + 0.004, 0.003, -e + 0.004], [e - 0.004, RollFrame, e - 0.004], Bar));
        p.Add(MachineLook.D([-e + RollBeam + 0.004, RollFrame, -e + 0.03], [e - RollBeam - 0.004, RollFrame + 0.014, e - 0.03], MachineLook.Steel, "tread plate"));
        foreach (var (sx, side) in new[] { (1, "+x"), (-1, "-x") })
        {
            double[] Xs(double a, double b) => sx > 0 ? [a, b] : [-b, -a];
            var bx = Xs(e - RollBeam, e - 0.004);
            if (beams)
                p.Add(Seam(MachineLook.D([bx[0], RollFrame - 0.004, -e + 0.004], [bx[1], RollTop - 0.015, e - 0.004], Frame, paintable: true), side)); // (the rollers' bearings)
            // An orange band under the beam (the way the pieces go) with an arrow on it, and the feet's kick plate.
            p.Add(Seam(MachineLook.D([sx > 0 ? e : -e - 0.012, 0.56, -e + 0.06], [sx > 0 ? e + 0.012 : -e, 0.7, e - 0.06], MachineLook.Out), side));
            SeamAdded(p, side, () => MachineLook.Arrow(p, 0, sx * e + sx * 0.012, sx, 2, 1, 0, 0.63, 0.5, 0.09, MachineLook.Ink));
            p.Add(Seam(MachineLook.D([sx > 0 ? e : -e - 0.014, 0.003, -e + 0.004], [sx > 0 ? e + 0.014 : -e, 0.13, e - 0.004], Bar), side));
            for (var z = -0.85; z < 0.9; z += 0.34)
            {
                var bt = Xs(e - 0.004, e + 0.012);
                p.Add(Seam(MachineLook.D([bt[0], 0.885, z - 0.02], [bt[1], 0.925, z + 0.02], MachineLook.Steel, "steel"), side));
            }
        }
        // The ends' kick plates and a hazard edge along each end's top, drawn while nothing is butted on there.
        foreach (var (sz, face) in new[] { (1, "+z"), (-1, "-z") })
        {
            double[] Zs(double a, double b) => sz > 0 ? [a, b] : [-b, -a];
            var kz = Zs(e, e + 0.014);
            p.Add(Seam(MachineLook.D([-e + 0.004, 0.003, kz[0]], [e - 0.004, 0.13, kz[1]], Bar), face));
            var hz = Zs(e, e + 0.012);
            p.Add(Seam(MachineLook.D([-e + 0.02, RollFrame - 0.1, hz[0]], [e - 0.02, RollFrame - 0.02, hz[1]], MachineLook.Accent, "hazard paint"), face));
        }
    }

    /// <summary>How far under a Roller Tile's rollers' tops its "-z table" socket is, and a Run-out Table's "roller" socket under the table's top.</summary>
    internal const double TableSock = 0.2;

    private static void RollerSockets(Part p)
    {
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("-z", [0, 0.5, -1], "-z");
        p.Sock("+z", [0, 0.5, 1], "+z");
        p.Sock("+x", [1, 0.5, 0], "+x");
        p.Sock("-x", [-1, 0.5, 0], "-x");
        // (and at a Pad Tile's socket height, so a run's end butted on a pad welds to it)
        p.Sock("-z low", [0, 0.1, -1], "-z");
        p.Sock("+z low", [0, 0.1, 1], "+z");
        p.Sock("+x low", [1, 0.1, 0], "+x");
        p.Sock("-x low", [-1, 0.1, 0], "-x");
    }

    /// <summary>
    /// The Roller Tile: a 2 x 2 m bed of powered rollers turning about x, so pieces go its +z way. Tiles welded together are one run
    /// (MachineBlocks family "roller", seeded as a pad is): one conveyor as wide as its tiles across, from its -z end to its +z end,
    /// laid by the server (RollerRuns.cs). Its in and out ends are seams: a run reads as one bed. The drive sits on its -x side.
    /// </summary>
    private static Part RollerTile()
    {
        var p = new Part("Roller Tile", Small, "Extrusion",
            "A 2 m bed of powered rollers that carries pieces too wide for a conveyor. Weld tiles in a run, side by side for a wider one: a run is one conveyor as wide as its tiles (a 3-tile-wide run takes a 6 m skin), going the arrows' way at 1 m/s. Butt an Out Table on its in end and a machine or a pad on its out end, or link conveyors to them.",
            tool: "welder", color: Frame, group: "Production");
        RollerBody(p, beams: true);
        const double e = RollE, rollY = RollTop - RollR;
        var cyl = Kw(("shape", "cylinder"), ("segments", 12), ("front", "+x"));
        for (var k = 0; k < 8; k++)
        {
            var z = -e + 0.125 + k * 0.2493;
            p.Add(MachineLook.Work(MachineLook.D([-e + RollBeam + 0.008, rollY - RollR, z - RollR], [e - RollBeam - 0.008, rollY + RollR, z + RollR], MachineLook.Steel, "steel", shape: new PyDict(cyl)), "spin", "+x"));
        }
        // The roller drive on the -x side: a gear motor, its coupling turning, a cable to a junction box.
        p.Add(Seam(MachineLook.D([-e - 0.16, 0.22, -0.45], [-e - 0.004, 0.48, -0.1], Bar), "-x"));
        p.Add(Seam(MachineLook.Work(MachineLook.D([-e - 0.12, 0.27, -0.1], [-e - 0.02, 0.43, 0.02], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 10), ("front", "+z"))), "spin", "+z")!, "-x"));
        p.Add(Seam(MachineLook.D([-e - 0.15, 0.3, -0.75], [-e - 0.006, 0.36, -0.45], Bar), "-x"));
        p.Add(Seam(MachineLook.D([-e - 0.16, 0.2, -0.95], [-e - 0.004, 0.46, -0.75], Bar), "-x"));
        // The ends: the shared port look, the in end white and the out end orange, each a seam (butted, it is under the next bed).
        SeamAdded(p, "-z", () => MachineLook.Port(p, [0, 0.42, -e], 2, -1, 0.2, 0.12, true));
        SeamAdded(p, "+z", () => MachineLook.Port(p, [0, 0.42, e], 2, 1, 0.2, 0.12, false));
        RollerSockets(p);
        // (its in end on a Run-out Table's far end, the rollers' tops level with the table's: an Extruder's pieces ride on along the run)
        p.Sock("-z table", [0, RollTop - TableSock, -1], "-z");
        p.Stats(260, 700, 5, 6);
        p.Set(("ports", Py.L(
                Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, RunY, -e)), ("face", "-z"), ("opening", Py.L(2.0, 2.0))),
                // (the run's own ends, laid by the server: its middle, and on the +x edge for a run an even number of tiles wide)
                Kw(("name", "Run start"), ("type", "conveyor"), ("dir", "out"), ("at", Py.L(0, RunY, -e)), ("face", "-z"), ("opening", Py.L(2.0, 2.0))),
                Kw(("name", "Run start +x"), ("type", "conveyor"), ("dir", "out"), ("at", Py.L(0.99, RunY, -e)), ("face", "-z"), ("opening", Py.L(2.0, 2.0))),
                Kw(("name", "Run end"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, RunY, e)), ("face", "+z"), ("opening", Py.L(2.0, 2.0))),
                Kw(("name", "Run end +x"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0.99, RunY, e)), ("face", "+z"), ("opening", Py.L(2.0, 2.0))),
                Kw(("name", "Piece out"), ("type", "conveyor"), ("dir", "out"), ("at", Py.L(0, RunY, e)), ("face", "+z"), ("opening", Py.L(2.0, 2.0))))),
            ("machineBlock", Kw(("family", "roller"), ("role", "block"), ("cell", 2), ("seed", true), ("origin", Py.L(0, -0.5, 0)))),
            ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    /// <summary>
    /// The Turntable Tile: a junction for runs. A run butted on any side ends in it, and a run that starts at any other side takes the
    /// piece on, turned to its way (a quarter turn onto a crossing run). Its disc of rollers turns while a piece crosses it.
    /// </summary>
    private static Part TurntableTile()
    {
        var p = new Part("Turntable Tile", Small, "Extrusion",
            "A 2 m tile that turns a piece onto a crossing run: butt Roller Tile runs on its sides; a piece from one goes on to the others in turn, turned to their way. It is the junction for pieces too wide for a Splitter or a Join.",
            tool: "welder", color: Frame, group: "Production");
        RollerBody(p, beams: false);
        const double e = RollE, disc = 0.96;
        // The ring it turns in, the disc, its rollers and the king pin, all turning about y while a piece crosses.
        var ring = Kw(("shape", "cylinder"), ("segments", 32), ("front", "+y"));
        p.Add(MachineLook.D([-disc - 0.02, RollFrame + 0.014, -disc - 0.02], [disc + 0.02, RollFrame + 0.05, disc + 0.02], MachineLook.Base, shape: new PyDict(ring)));
        p.Add(MachineLook.Work(MachineLook.D([-disc, RollFrame + 0.05, -disc], [disc, RollFrame + 0.08, disc], Frame, shape: new PyDict(ring)), "spin", "+y"));
        foreach (var x in new[] { -0.45, 0.0, 0.45 })
        {
            var half = MathF.Sqrt((float)(disc * disc - x * x)) - 0.12;
            p.Add(MachineLook.Work(MachineLook.D([x - 0.18, RollTop - 0.12, -half], [x + 0.18, RollTop - 0.004, half], MachineLook.Steel, "steel"), "spin", "+y"));
        }
        p.Add(MachineLook.Work(MachineLook.D([-0.06, RollTop - 0.008, -0.06], [0.06, RollTop + 0.006, 0.06], MachineLook.Accent, shape: Kw(("shape", "cylinder"), ("segments", 12), ("front", "+y"))), "spin", "+y"));
        // A white band on every side: any side takes a run in or out.
        foreach (var (n, sg, face) in new[] { (2, -1, "-z"), (2, 1, "+z"), (0, -1, "-x"), (0, 1, "+x") })
        {
            double[] mn = n == 2 ? [-0.5, 0.74, sg > 0 ? e : -e - 0.012] : [sg > 0 ? e : -e - 0.012, 0.74, -0.5];
            double[] mx = n == 2 ? [0.5, 0.78, sg > 0 ? e + 0.012 : -e] : [sg > 0 ? e + 0.012 : -e, 0.78, 0.5];
            p.Add(Seam(MachineLook.D(mn, mx, MachineLook.In), face));
        }
        RollerSockets(p);
        p.Stats(300, 800, 5, 6);
        var ports = new List<object>();
        foreach (var (face, at) in new[] { ("-z", Py.L(0, RunY, -e)), ("+z", Py.L(0, RunY, e)), ("-x", Py.L(-e, RunY, 0)), ("+x", Py.L(e, RunY, 0)) })
        {
            ports.Add(Kw(("name", $"In {face}"), ("type", "conveyor"), ("dir", "in"), ("at", at), ("face", face), ("opening", Py.L(2.0, 2.0))));
            ports.Add(Kw(("name", $"Out {face}"), ("type", "conveyor"), ("dir", "out"), ("at", at), ("face", face), ("opening", Py.L(2.0, 2.0))));
        }
        p.Set(("ports", Py.L(ports.ToArray())), ("turntable", true), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o)
    {
        MakeRacks(o);
        WriteParts(o, PadsOut, [("", Tile()), ("", RackSide()), ("", RollerTile()), ("", TurntableTile())]);
    }

    private void MakeRacks(PartOutput o) => WriteParts(o, Out,
    [
        ("", Rack("Piece Rack", 6, 4, 26, 1.0, 6.0, 2000, "Takes whole pieces off a conveyor at its Piece in and holds them: up to 1 x 1 m and 6 m long, as many as fill it. F takes the one you look at.")),
        ("", Rack("Long Piece Rack", 12, 6, 52, 2.0, 12.0, 6000, "Takes whole pieces off a conveyor: up to 2 x 2 m and 12 m long, as many as fill it. F takes the one you look at.")),
        ("", Pad("Lay-down Pad", 16, 8, 32, 3.6, 1.8, 7.6, 4000, "An open deck where pieces wait: up to 3.6 m across, 1.8 m high and 7.6 m long, stacked to 2 m. Loader Arms in reach pick from it and put on it; a conveyor may end at it.")),
        ("", Pad("Large Lay-down Pad", 32, 16, 64, 7.6, 3.8, 15.6, 16000, "An open deck for large pieces: up to 7.6 m across, 3.8 m high and 15.6 m long, stacked to 4 m. Loader Arms in reach pick from it and put on it.")),
    ]);
}
