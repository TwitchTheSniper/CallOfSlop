using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Building Station on the block system (the Building Station Lab's rover design, Sean 2026-11-24; docs/sandbox/constructs/production.md,
/// "Building station"; MachineBlocks.cs): the station's Core, the Bay Block and the Dock Expansions. The Core ("Building Station") is a
/// flat 4 x 2 m dock pad for one rover, its deck level with the Bay Blocks, with a console at its back end (the screen on its back face, towards
/// the player on the works floor, the keys, the stop, the lamp), a link-stock hopper (Chute in) and the fluid inlet; it holds the job and the power and has no
/// hold (<c>noHold</c>: every piece goes to its pick pads). Its mouth (+x.0) welds to the bay. A Dock Expansion (small 4 x 2 m, medium
/// 6 x 4, large 12 x 8) is a dock pad for one more rover of its class, its mouth on the bay's edge. Bay Blocks are 2 x 2 m floor tiles
/// welded beside it and to each other: the bay is the largest full rectangle of them. It has no height of its own: what
/// is clear over it and the rovers' reach limit a build (ConstructStation.BaySize, LoaderRefusal). The rovers are drawn, not parts (StationRender.cs).
/// The look (look.md "Machine look"): white at the ways in, the shared port fitting at every port, the body in the family's green,
/// three tones; a floor tile's edge and corner trim are seams, drawn only where no tile of the same bay is welded; a dock's deck is dark
/// with yellow bay lines, a chevron, a charge plate and its class's wheel marks.
/// </summary>
public sealed class BuildStationParts : IPartGenerator
{
    public string Name => "build_station_parts";

    private const string Out = "Constructs/Parts/BuildingStation";
    private const string Body = "#3f7d3a", Screen = "#10161c", Trim = "#356a31"; // (the family's body colour: it takes paint; the rest is MachineLook's tones)
        // The bay's floor: a Bay Block's top, level with the roller runs and the raised Pad Tiles beside it (Sean 2026-11-24), so a piece
    // rolls off a run onto a pick pad and the crane lifts it across at one height.
    private const double Floor = OutTableParts.WorkY;
    private const double SideY = 0.1; // the side sockets' height, a Pad Tile's: a bay welds to the pad it butts on, at one level
            // The station's own block limits (Sean 2026-11-21, the Zero Day works: one station builds the whole 53 m frigate): a bay up to 32 cells
    // a side (64 m) and 512 cells in all, masts included, past the other machines' 16 and 128 (MachineBlocks.cs, "maxSide" and "maxCells").
    private const int MaxSide = 32, MaxCells = 512;
    private const int LiftKg = 120000; // the heaviest piece its largest rover lifts off a pick pad beside the bay (ConstructStationRovers.cs)

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "station"), ("role", "core"), ("origin", Py.L(0, Floor / 2, 0)), ("maxSide", MaxSide), ("maxCells", MaxCells))
        : Kw(("family", "station"), ("role", role));

    private static PyDict Seam(PyDict? box, params string[] faces) => box!.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    /// <summary>
    /// A dock pad (Sean 2026-11-24, the rover docks: the Building Station Lab's approved design): a flat plate level with the Bay Blocks'
    /// deck, its mouth at +x (x = 1, where it welds to the bay's edge) and its back <paramref name="len"/> behind, <paramref name="wid"/>
    /// across (z). Painted bay lines down both sides and across the back, a chevron pointing out of the mouth, a charge plate flush in the
    /// deck, the parked rover's wheel marks for its class (a dark rubber pad with a white corner frame under each wheel, a stop bar ahead of
    /// the front pair) and the centre line it lines up on. Paint stands 4 to 8 mm proud of the plate (z-fighting rule).
    /// </summary>
    private static void DockPad(Part p, double len, double wid, int cls, bool core)
    {
        double x0 = 1 - len, x1 = 1, hz = wid / 2, cx = 1 - len / 2, h = Floor;
        const double e = 0.003; // (the slab stops 3 mm short of its cells, as a Bay Block's does)
        p.Add(Box([x0 + e, 0, -hz + e], [x1 - e, h - 0.024, hz - e], MachineLook.Base));
        p.Add(Box([x0 + e + 0.01, h - 0.028, -hz + e + 0.01], [x1 - e - 0.01, h, hz - e - 0.01], core ? "#3a3f45" : "#33383d"));
        // The skirt's hazard band and drip edge, as a Bay Block's, on the three sides that face the works floor (not the mouth).
        MachineLook.Hazard(p, [x0 + e - 0.012, 0.1, -hz + 0.1], [x0 + e + 0.004, 0.18, hz - 0.1]);
        foreach (var sz in new[] { -1, 1 })
            MachineLook.Hazard(p, [x0 + 0.1, 0.1, sz > 0 ? hz - e - 0.004 : -hz + e - 0.012], [x1 - 0.1, 0.18, sz > 0 ? hz - e + 0.012 : -hz + e + 0.004]);
        double top = h + 0.006, mid = h + 0.009;
        // Bay lines: yellow down both sides and across the back.
        foreach (var sz in new[] { -1, 1 })
            p.Add(L([x0 + 0.3, h - 0.012, sz * (hz - 0.35) - 0.06], [x1 - 0.3, top, sz * (hz - 0.35) + 0.06], MachineLook.Accent, "hazard paint"));
        p.Add(L([x0 + 0.29, h - 0.012, -hz + 0.29], [x0 + 0.41, top, hz - 0.29], MachineLook.Accent, "hazard paint"));
        // The chevron by the mouth, pointing out into the bay: two stepped arms meeting on the centre line.
        for (var k = 0; k < 5; k++)
            foreach (var sz in new[] { -1, 1 })
            {
                var x = x1 - 0.6 - k * 0.09;
                var z = sz * (0.06 + k * 0.08);
                p.Add(L([x - 0.06, h - 0.012, Math.Min(z, z + sz * 0.1)], [x + 0.06, top, Math.Max(z, z + sz * 0.1)], MachineLook.Accent, "hazard paint"));
            }
        // The charge plate the rover's shoe rests on, flush in the deck behind the middle.
        p.Add(L([cx - 0.4 - 0.4, h - 0.012, -0.6], [cx - 0.4 + 0.4, h + 0.004, 0.6], "#c9a227", "steel"));
        // Wheel marks for the class's parked rover (lab: small 4 wheels, medium 6, large 8), centred on the pad.
        var (lat, along, ww, wd) = cls switch
        {
            3 => (3.33, new[] { -3.78, -1.26, 1.26, 3.78 }, 0.9, 2.0),
            2 => (1.35, new[] { -1.3, 0.0, 1.3 }, 0.36, 0.8),
            _ => (0.92, new[] { -0.85, 0.85 }, 0.3, 0.64),
        };
        foreach (var sz in new[] { -1, 1 })
            foreach (var wa in along)
            {
                double wx = cx + wa, wz = sz * lat, pd = wd + 0.2, pw = ww + 0.16;
                p.Add(L([wx - pd / 2, h - 0.012, wz - pw / 2], [wx + pd / 2, h + 0.003, wz + pw / 2], "#1c1e21"));
                foreach (var (ox, oz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) }) // (the white corner frame: an L at each corner)
                {
                    double ax = wx + ox * (pd / 2 - 0.06), az = wz + oz * (pw / 2 + 0.02);
                    p.Add(L([ax - 0.08, h - 0.012, az - 0.025], [ax + 0.08, mid, az + 0.025], "#e8e8e2"));
                    double bx = wx + ox * (pd / 2 + 0.02), bz = wz + oz * (pw / 2 - 0.06);
                    p.Add(L([bx - 0.025, h - 0.012, bz - 0.08], [bx + 0.025, mid, bz + 0.08], "#e8e8e2"));
                }
            }
        var front = cx + along[^1] + wd / 2 + 0.25; // (the stop bars just ahead of the front wheels, towards the mouth)
        foreach (var sz in new[] { -1, 1 })
            p.Add(L([front - 0.05, h - 0.012, sz * lat - (ww + 0.3) / 2], [front + 0.05, mid + 0.003, sz * lat + (ww + 0.3) / 2], "#e8e8e2"));
        p.Add(L([x0 + 0.7, h - 0.012, -0.03], [x1 - 0.7, top - 0.002, 0.03], "#8a8f95")); // (the centre line the rover lines up on)
        // Sockets: its foot under the mouth's cells and the mouth itself, one a 2 m cell, at a Bay Block's side height.
        var cells = (int)Math.Round(wid / 2);
        for (var k = 0; k < cells; k++)
        {
            var z = -hz + 1 + 2 * k;
            p.Sock($"+x.{k}", [x1, SideY, z], "+x");
            p.Sock(k == 0 ? "-y.0.0" : $"-y.0.{k}", [0, 0, z], "-y");
        }
        p.Set(("centreSockets", false), ("collider", "boxes"));
    }

    private static Part Core()
    {
        var p = new Part("Building Station", Small, "Extrusion",
            "A Building Station's Core: the dock for its first arm rover (a small one: 9 m reach, 8 t), and the console that builds a blueprint in its bay, part by part. Weld its mouth to the edge of a bay of Bay Blocks (up to 32 x 32) and weld Dock Expansions along the bay's edge for more rovers. A bay builds as high as is clear over it and its rovers can reach. Rovers take pieces off pick pads and Part Pads beside the bay. Pick a blueprint on its screen. Needs power.",
            role: "storage", tool: "welder", color: Body, group: "Production");
        DockPad(p, 4, 2, 1, true);
        // ---- The console at the pad's back end: a cabinet on the deck, its screen on the back face, towards the works floor where a player
        // stands (Sean 2026-11-26: not over the rover's deck), at a standing eye's height off the floor; the keys on a shelf under it, the job lamp on top.
        double h = Floor, cx0 = -2.94, cx1 = -2.46, cz0 = -0.97, cz1 = 0.3; // (the bezel stays inside the pad's back edge: a Mast Block may stand just past it)
        p.Add(Box([cx0, h - 0.004, cz0], [cx1, h + 1.0, cz1]));
        p.Add(L([cx1 - 0.004, h + 0.1, cz0 + 0.06], [cx1 + 0.012, h + 0.16, cz1 - 0.06], MachineLook.Accent, "hazard paint")); // (a kick band)
        // The screen: a solid bezel proud of the back face (a screen with buttons stands on a solid box, so the crosshair's ray finds it), its glass face 4 mm out.
        double sy0 = h + 0.18, sy1 = h + 0.94;
        p.Add(Box([cx0 - 0.03, sy0, cz0 + 0.04], [cx0 + 0.01, sy1, cz1 - 0.04], Screen));
        p.Add(L([cx0 - 0.05, sy1 - 0.004, cz0 + 0.02], [cx0 + 0.004, sy1 + 0.04, cz1 - 0.02], MachineLook.Base)); // (its hood)
        p.Add(L([cx0 - 0.17, h + 0.1, cz0 + 0.1], [cx0 - 0.004, h + 0.114, cz1 - 0.1], MachineLook.Steel, "steel")); // (a work shelf under the screen)
        for (var k = 0; k < 3; k++) // (three keys and a stop on the shelf)
            p.Add(L([cx0 - 0.14, h + 0.114, cz0 + 0.2 + k * 0.22], [cx0 - 0.02, h + 0.134, cz0 + 0.34 + k * 0.22], MachineLook.Accent));
        p.Add(L([cx0 - 0.14, h + 0.114, cz1 - 0.3], [cx0 - 0.02, h + 0.146, cz1 - 0.16], MachineLook.Stop));
        p.Add(L([cx0 + 0.16, h + 1.0, -0.46], [cx0 + 0.32, h + 1.11, -0.26], MachineLook.Accent)); // (the job lamp's hood)
        // The power meter and the maker's plate on its front (the rover side), a cable gland where the charge line runs under the deck to the plate.
        p.Add(L([cx1 - 0.004, h + 0.4, -0.6], [cx1 + 0.016, h + 0.66, -0.34], "#e8e6df", shape: Cyl("+x", 14)));
        p.Add(L([cx1 - 0.004, h + 0.24, -0.1], [cx1 + 0.016, h + 0.34, 0.1], MachineLook.In));
        p.Add(L([cx1 - 0.004, h - 0.004, -0.06], [cx1 + 0.18, h + 0.04, 0.06], MachineLook.Steel, "steel"));
        // The link stock hopper beside it (copper for wires, steel for pipes): a post with the chute's port on its top.
        p.Add(Box([cx0, h - 0.004, 0.38], [cx0 + 0.42, h + 1.16, 0.92]));
        MachineLook.Port(p, [cx0 + 0.21, h + 1.16, 0.65], 1, 1, 0.16, 0.1, true);
        // The fluid inlet on the console's side (a pipe for the fill stage), clear of the screen.
        MachineLook.Port(p, [Py.Round((cx0 + cx1) / 2, 3), h + 0.6, cz0], 2, -1, 0.08, 0.1, true);
        p.Stats(1400, 1600, 4, 10.0);
        p.Set(("power", 600), ("onBreak", "disable"));
        p.Set(("panels", Py.L(Kw(("source", "station"), ("at", Py.L(Py.Round(cx0 - 0.034, 3), Py.Round((sy0 + sy1) / 2, 3), Py.Round((cz0 + cz1) / 2, 3))), ("size", Py.L(1.14, 0.72)), ("face", "-x"), ("up", "+y"), ("rows", 8), ("range", 12)))));
        static PyDict Fill(string key, string label, string tip) => Kw(("key", key), ("label", label), ("group", "Fill"), ("min", 0), ("max", 100), ("step", 5), ("default", 100), ("unit", "%"),
            ("tooltip", "Survival: a build is filled to this share. " + tip + " 0: none, 100: full."));
        var config = Py.L(
            Fill("fillFuel", "Fuel and tanks", "Fuel and fluid tanks, gases too: from tanks piped to Fluid in, gas from linked bins."),
            Fill("fillAmmo", "Ammo", "Ammo boxes, Ammo Containers and fitted magazines: from bulk ammo in linked bins."),
            Fill("fillCharge", "Battery charge", "Batteries: charged from the station's own power, which takes time."),
            Kw(("key", "fillWait"), ("label", "Wait until filled"), ("group", "Fill"), ("type", "toggle"), ("default", false),
                ("tooltip", "On: a build stays in the bay until it is filled. Off: it finishes with what it got and says what is short.")),
            Kw(("key", "subJobs"), ("label", "Sub-jobs"), ("group", "Bay"), ("type", "choice"), ("options", Py.L("Off", "Auto", "Linked")), ("default", 1),
                ("tooltip", "Auto: when idle it builds other stations' sub-blueprint uses on this construct. Linked: also uses sent over a logistics link (the module goes by cargo ship). Off: only your own builds.")),
            Kw(("key", "dressSalt"), ("label", "Dress salt"), ("group", "Dress"), ("min", 0), ("max", 99), ("step", 1), ("default", 0),
                ("tooltip", "Which of the style's panel patterns goes where: another number is another layout. Both sides of a ship match on one salt.")),
            Kw(("key", "dressExtras"), ("label", "Dress extras"), ("group", "Dress"), ("type", "choice"), ("options", Py.L("All", "No conduit", "No lights", "Bolts and vents", "None")), ("default", 0),
                ("tooltip", "The extra bits it lays with the armour: A bolt rows and chevrons, B vents, D lights, F conduit. All: every one; None: armour, rails and the strip only.")));
        // The Dress job's style (HullLoft.Dress.cs, docs/sandbox/design/hull-dress.md): a choice once Constructs/DressStyles.json has two (a choice
        // takes 2 to 16 names); with one, every skin gets that one.
        if (DressStyles.Current.Count >= 2)
            config.Insert(5, Kw(("key", "dressStyle"), ("label", "Dress style"), ("group", "Dress"), ("type", "choice"),
                ("options", Py.L([.. DressStyles.Current.Values.Take(16).Select(x => (object?)x.Label)])), ("default", 0),
                ("tooltip", "The Dress job (pick \"Dress skins\" as the blueprint): the faction look it lays on each plain hull skin in the bay, as subparts a Bake Oven bakes in.")));
        // How its orders are shared over its lines (Sean, 2026-11-27: a station with an extrusion line on each side of its pick pads sent
        // every extrusion job to one side). Last in the list, so a saved station's other settings keep their slots.
        config.Add(Kw(("key", "lineSplit"), ("label", "Line split"), ("group", "Lines"), ("type", "choice"), ("options", Py.L("Share", "Whole orders", "First line")), ("default", 0),
            ("tooltip", "How orders are shared over alike machines on its lines. Share: split so each line finishes together. Whole orders: each to the least busy line. First line: all to the first that takes it.")));
        p.Set(("config", config));
        // No hold (the rover docks: the old Core box and its hold went): its store is only the terminal's count of what is on its way; every
        // piece is ordered onto a pick pad or a Part Pad beside the bay ("noHold"), where a rover takes it. "lift": the largest rover's.
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(0.3, 0.3)), ("length", 0.4), ("count", 400), ("crafting", true), ("station", true), ("lift", LiftKg), ("noHold", true),
            ("hold", Py.L(Py.L(cx0 + 0.05, h + 0.1, cz0 + 0.05), Py.L(cx1 - 0.05, h + 0.6, cz1 - 0.05))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Fluid in"), ("type", "pipe"), ("dir", "in"), ("at", Py.L(Py.Round((cx0 + cx1) / 2, 3), Py.Round(h + 0.6, 3), cz0)), ("face", "-z")),
            Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(Py.Round(cx0 + 0.21, 3), Py.Round(h + 1.16, 3), 0.65)), ("face", "+y")))));
        p.Set(("machineBlock", BlockKey("core")));
        return p;
    }

    /// <summary>A Dock Expansion (the rover docks): one more dock pad welded mouth first to the bay's edge, holding one more arm rover of its class.</summary>
    private static Part Dock(string name, string cls, int n, double len, double wid, double mass, string desc)
    {
        var p = new Part(name, Small, "Extrusion", desc, group: "Production", color: Body);
        DockPad(p, len, wid, n, false);
        p.Stats(mass, mass, 4, 8.0);
        p.Set(("stationDock", cls));
        return p;
    }

    /// <summary>
    /// A Bay Block: one 2 x 2 m cell of a Building Station's bay floor, a 1 m deck (level with the roller runs), tread plate on top. Its edge trim (a kerb with a hazard
    /// strip, its bolts, an amber floor lamp) and its corner caps are seams: drawn only where no tile of the same bay is welded, so a bay reads
    /// as one floor with one hazard border. In the middle, a steel seat for a mast (welded on its top socket), four tie-down rings and a drain.
    /// </summary>
    private static Part BayBlock()
    {
        var p = new Part("Bay Block", Small, "Extrusion",
            "A 2 x 2 m tile of a Building Station's bay. Weld it beside the station's Core or beside another Bay Block: the bay is the largest full rectangle of them, up to 32 x 32 m. A bay has no height of its own: a build fits while nothing stands over it and its rovers reach.",
            group: "Production", color: Body);
        const double e = 0.997, h = Floor, k = 0.12; // (the slab stops 3 mm short of the cell; k: the kerb's width)
        p.Add(Box([-e, 0, -e], [e, h - 0.02, e], MachineLook.Base));
        p.Add(Box([-e + 0.01, h - 0.024, -e + 0.01], [e - 0.01, h, e - 0.01], "#2b3036")!.Update(("finish", "tread plate"))); // (a dark floor, a step off the skirt's base: the Core keeps the body colour)
        // The kerb on each outer side: a dark rail with a hazard strip on its top, bolted down, and a sunk amber lamp in the middle.
        foreach (var (face, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            int u = 2 - n;
            PyDict? Edge(double n0, double n1, double u0, double u1, double y0, double y1, string colour, string? finish = null)
            {
                double[] mn = new double[3], mx = new double[3];
                (mn[n], mx[n]) = sg > 0 ? (e - n1, e - n0) : (-e + n0, -e + n1);
                (mn[u], mx[u]) = (u0, u1);
                (mn[1], mx[1]) = (y0, y1);
                return L(mn, mx, colour, finish);
            }
            p.Add(Seam(Edge(0.004, k, -e + k, e - k, h - 0.006, h + 0.03, MachineLook.Base), face));
            p.Add(Seam(Edge(0.02, k - 0.02, -e + k, e - k, h + 0.026, h + 0.042, MachineLook.Accent, "hazard paint"), face));
            foreach (var t in new[] { -0.6, -0.2, 0.2, 0.6 })
                p.Add(Seam(Edge(k / 2 - 0.016, k / 2 + 0.016, t - 0.016, t + 0.016, h + 0.04, h + 0.054, MachineLook.Steel, "steel"), face));
            p.Add(Seam(Edge(-0.002, 0.014, -0.12, 0.12, h - 0.2, h - 0.06, MachineLook.Accent), face)); // (the floor lamp let into the slab's edge)
            p.Add(Seam(Edge(-0.002, 0.012, -0.9, 0.9, 0.1, 0.18, MachineLook.Accent, "hazard paint"), face)); // (a hazard band round the deck's foot, as a Pad Tile's)
            p.Add(Seam(Edge(-0.002, 0.012, -0.9, 0.9, 0.02, 0.05, MachineLook.Base), face)); // (the slab's drip edge)
        }
        // The corner caps: a dark block with a steel plate on top where two outer kerbs meet.
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
        {
            string fx = sx > 0 ? "+x" : "-x", fz = sz > 0 ? "+z" : "-z";
            double ax0 = sx > 0 ? e - k : -e + 0.004, ax1 = sx > 0 ? e - 0.004 : -e + k, az0 = sz > 0 ? e - k : -e + 0.004, az1 = sz > 0 ? e - 0.004 : -e + k;
            p.Add(Seam(L([ax0, h - 0.006, az0], [ax1, h + 0.05, az1], MachineLook.Base), fx, fz));
            p.Add(Seam(L([ax0 + 0.02, h + 0.046, az0 + 0.02], [ax1 - 0.02, h + 0.062, az1 - 0.02], MachineLook.Steel, "steel"), fx, fz));
        }
        // The mast seat in the middle: a steel plate with eight bolts; four tie-down rings round it; a drain slot.
        p.Add(L([-0.4, h - 0.004, -0.4], [0.4, h + 0.016, 0.4], MachineLook.Steel, "steel"));
        foreach (var (bx, bz) in new[] { (-0.32, -0.32), (0.32, -0.32), (-0.32, 0.32), (0.32, 0.32), (0, -0.32), (0, 0.32), (-0.32, 0), (0.32, 0) })
            p.Add(L([bx - 0.022, h + 0.012, bz - 0.022], [bx + 0.022, h + 0.032, bz + 0.022], MachineLook.Base, shape: Cyl("+y", 6)));
        foreach (var (rx, rz) in new[] { (-0.62, -0.62), (0.62, -0.62), (-0.62, 0.62), (0.62, 0.62) })
        {
            p.Add(L([rx - 0.07, h - 0.004, rz - 0.07], [rx + 0.07, h + 0.012, rz + 0.07], MachineLook.Base, shape: Cyl("+y", 10)));
            p.Add(L([rx - 0.045, h + 0.008, rz - 0.012], [rx + 0.045, h + 0.05, rz + 0.012], MachineLook.Accent)); // (the ring, standing up)
        }
        p.Add(L([-0.5, h - 0.004, 0.66], [0.5, h + 0.008, 0.74], MachineLook.Base));
        foreach (var x in new[] { -0.4, -0.2, 0.0, 0.2, 0.4 })
            p.Add(L([x - 0.03, h + 0.004, 0.672], [x + 0.03, h + 0.012, 0.728], MachineLook.Steel, "steel")); // (the drain's grate)
        p.Sock("+x", [1, SideY, 0], "+x");
        p.Sock("-x", [-1, SideY, 0], "-x");
        p.Sock("+z", [0, SideY, 1], "+z");
        p.Sock("-z", [0, SideY, -1], "-z");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(900, 1200, 6, 6.0);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BayBlock()),
        ("", Dock("Dock Expansion", "small", 1, 4, 2, 1200, "A dock pad for one more small arm rover (9 m reach, 8 t, 5 m/s) for a Building Station: weld its mouth to the edge of the station's bay. 2 x 4 m.")),
        ("", Dock("Medium Dock Expansion", "medium", 2, 6, 4, 3200, "A dock pad for one medium arm rover (15 m reach, 52 t, 3 m/s) for a Building Station: weld its mouth to the edge of the station's bay. 4 x 6 m.")),
        ("", Dock("Large Dock Expansion", "large", 3, 12, 8, 11000, "A dock pad for one large arm rover (26 m reach, 120 t, 1.5 m/s) for a Building Station: weld its mouth to the edge of the station's bay. 8 x 12 m."))]);
}
