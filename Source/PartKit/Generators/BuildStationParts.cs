using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Building Station on the block system (design page drawing 18; docs/sandbox/constructs/production.md, "Building station";
/// MachineBlocks.cs): the station's Core and the Bay Block. The Core ("Building Station", 2 m across and 6 m long, three cells) holds
/// the screen, the job, the hold, the ports and the power: an intake housing at its back (the conveyor's mouth, the chute's hopper, the
/// fluid inlet), the hold along its middle that parts ride into from the back (a Piece Rack's, <c>storage.pieces</c> with
/// <c>crafting</c> and <c>station</c>: ConstructStation.cs), and the control cabinet at its front. Bay Blocks are 2 x 2 m floor tiles
/// welded beside it and to each other: the bay is the largest full rectangle of them. The shared 2 m Mast Block stacked on the
/// rectangle's four corners sets the bay's height, and the station draws its crane bridges across the masts (StationRender.cs).
/// The look (look.md "Machine look"): white at the ways in, the shared port fitting at every port, the body in the family's green,
/// three tones; a floor tile's edge and corner trim are seams, drawn only where no tile of the same bay is welded.
/// </summary>
public sealed class BuildStationParts : IPartGenerator
{
    public string Name => "build_station_parts";

    private const string Out = "Constructs/Parts/BuildingStation";
    private const string Body = "#3f7d3a", Screen = "#10161c", Trim = "#356a31"; // (the family's body colour: it takes paint; the rest is MachineLook's tones)
    private const double Plinth = 0.25; // the Core's plinth and a Bay Block are this high: the bay's floor
    private const double Hz0 = -2.55, Hz1 = 2.27; // the hold, along the Core (4.82 m: a 4.8 m piece lies in it)
    private const double Back = -2.58, Front = 2.3; // the intake housing's inner face, the control cabinet's back

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "station"), ("role", "core"), ("origin", Py.L(0, Plinth / 2, 0)))
        : Kw(("family", "station"), ("role", role));

    private static PyDict Seam(PyDict? box, params string[] faces) => box!.Update(("seam", faces.Length == 1 ? faces[0] : Py.L([.. faces])));

    private static PyDict Cyl(string front, int segments = 12) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    private static Part Core()
    {
        var p = new Part("Building Station", Small, "Extrusion",
            "A Building Station's Core: builds a blueprint in its bay, part by part, from what its linked stores and machines send and what you bring by hand. Weld Bay Blocks beside it for the bay and stack Mast Blocks on its corners for height. Pick a blueprint on its screen. Needs power.",
            role: "storage", tool: "welder", color: Body, group: "Production");
        const double x0 = -1, x1 = 1, z0 = -3, z1 = 3, top = 1.5; // (top: the hold's rails)
        // ---- The plinth: the Core stands on it, flush with the Bay Blocks' floor; a skirt and a hazard edge on the bay side.
        p.Add(Box([x0, 0, z0], [x1, Plinth, z1], MachineLook.Base));
        MachineLook.Hazard(p, [x1 - 0.004, Plinth - 0.06, z0 + 0.1], [x1 + 0.012, Plinth - 0.01, z1 - 0.1]);
        MachineLook.Hazard(p, [x0 - 0.012, Plinth - 0.06, z0 + 0.1], [x0 + 0.004, Plinth - 0.01, z1 - 0.1]);
        foreach (var z in new[] { -2.5, -1.5, -0.5, 0.5, 1.5, 2.5 })
            foreach (var sx in new[] { -1, 1 })
                p.Add(L([sx > 0 ? x1 - 0.002 : x0 - 0.018, 0.06, z - 0.02], [sx > 0 ? x1 + 0.018 : x0 + 0.002, 0.1, z + 0.02], MachineLook.Steel, "steel")); // (the plinth's bolts)
        // ---- The intake housing at the back: the conveyor's mouth through it, the chute's hopper on its top, the fluid inlet on its side.
        p.Add(Box([x0, Plinth - 0.004, z0], [x1, 1.75, Back]));
        p.Add(Box([x0 + 0.06, 1.746, z0 + 0.04], [x1 - 0.06, 1.8, Back - 0.04], MachineLook.Base)); // (its lid)
        foreach (var x in new[] { -0.7, 0.7 }) // (louvres on its back, either side of the mouth's collar)
            for (var k = 0; k < 4; k++)
                p.Add(L([x - 0.08, 1.2 + k * 0.1, z0 - 0.016], [x + 0.08, 1.24 + k * 0.1, z0 + 0.004], MachineLook.Base));
        // The conveyor's mouth (Piece in): the shared port fitting, square round the 1.7 m opening, its sill at the belt's line.
        MachineLook.Port(p, [0, 0.9, z0], 2, -1, 0.85, 0.12, true);
        MachineLook.Arrow(p, 1, 1.8, 1, 2, 1, -2.79, 0, 0.3, 0.26, MachineLook.In); // (on the lid: in, along the hold)
        // The chute's hopper: the shared port on the lid, over the hold's back end.
        MachineLook.Port(p, [0, 1.8, -2.79], 1, 1, 0.16, 0.1, true);
        // The fluid inlet on its -x side, and the second conveyor's mouth on the cabinet's -x side.
        MachineLook.Port(p, [x0, 1.05, -2.79], 0, -1, 0.08, 0.1, true);
        // ---- The hold along the middle: runners on the plinth, slats, posts and accent rails; parts ride in at the back, are lifted from above.
        const double post = 0.07, rail = 0.06;
        for (var k = 0; k <= 3; k++)
        {
            var z = Hz0 + 0.02 + (Hz1 - Hz0 - 0.12) * k / 3;
            p.Add(Box([x0 + 0.02, Plinth - 0.004, z], [x0 + 0.02 + post, top - rail + 0.004, z + post], MachineLook.Base));
            p.Add(Box([x1 - 0.02 - post, Plinth - 0.004, z], [x1 - 0.02, top - rail + 0.004, z + post], MachineLook.Base));
        }
        for (var k = 1; k < 5; k++)
        {
            var x = x0 + 0.1 + (x1 - x0 - 0.2) * k / 5;
            p.Add(L([x - 0.03, Plinth - 0.004, Hz0 + 0.03], [x + 0.03, Plinth + 0.02, Hz1 - 0.03], MachineLook.Steel, "steel")); // (the slats parts lie on)
        }
        p.Add(L([x0 + 0.01, top - rail, Hz0], [x0 + 0.03 + post, top, Hz1], MachineLook.Accent));
        p.Add(L([x1 - 0.03 - post, top - rail, Hz0], [x1 - 0.01, top, Hz1], MachineLook.Accent));
        // The hold's -x side is a mesh guard (a frame and three bars); its +x side, towards the bay, stays open for the arms, a white lip on it.
        p.Add(L([x0 + 0.004, Plinth + 0.3, Hz0 + 0.1], [x0 + 0.018, Plinth + 0.34, Hz1 - 0.1], MachineLook.Base));
        p.Add(L([x0 + 0.004, top - 0.3, Hz0 + 0.1], [x0 + 0.018, top - 0.26, Hz1 - 0.1], MachineLook.Base));
        foreach (var z in new[] { -1.4, 0.0, 1.1 })
            p.Add(L([x0 + 0.004, Plinth + 0.34, z - 0.02], [x0 + 0.018, top - 0.3, z + 0.02], MachineLook.Base));
        p.Add(L([x1 - 0.016, Plinth + 0.02, Hz0 + 0.1], [x1 + 0.002, Plinth + 0.08, Hz1 - 0.1], MachineLook.In));
        // A cable tray along the guard from the cabinet to the housing: the power and the Orders line, clipped every metre.
        p.Add(L([x0 - 0.06, top - 0.24, Hz0], [x0 + 0.004, top - 0.18, Hz1], MachineLook.Base));
        foreach (var (y, colour) in new[] { (top - 0.215, "#b8322a"), (top - 0.195, "#2f62b5") })
            p.Add(L([x0 - 0.05, y - 0.012, Hz0 - 0.02], [x0 - 0.026, y + 0.012, Hz1 + 0.02], colour, shape: Cyl("+z", 8)));
        foreach (var z in new[] { -2.0, -1.0, 0.0, 1.0, 2.0 })
            p.Add(L([x0 - 0.066, top - 0.25, z - 0.02], [x0 - 0.016, top - 0.17, z + 0.02], MachineLook.Steel, "steel"));
        // ---- The control cabinet at the front (the control face, on the aisle): a work top at 1.15 m, the screen's middle at 1.5 m.
        p.Add(Box([x0, Plinth - 0.004, Front], [x1, MachineLook.HandY, z1]));
        p.Add(Box([x0 + 0.06, MachineLook.HandY - 0.01, Front + 0.02], [x1 - 0.06, 1.98, Front + 0.2], MachineLook.Base)); // (the screen's housing)
        p.Add(L([x0 + 0.04, MachineLook.HandY, Front + 0.22], [x1 - 0.04, MachineLook.HandY + 0.014, z1 - 0.03], MachineLook.Steel, "steel")); // (the work top)
        p.Add(L([x0 + 0.12, 1.2, Front + 0.196], [x1 - 0.12, 1.82, Front + 0.21], Screen)); // (what its panel stands on)
        for (var k = 0; k < 4; k++) // (four keys and a stop: what a hand goes to)
            p.Add(L([x0 + 0.2 + k * 0.26, MachineLook.HandY + 0.014, z1 - 0.24], [x0 + 0.38 + k * 0.26, MachineLook.HandY + 0.034, z1 - 0.1], MachineLook.Accent));
        p.Add(L([x1 - 0.42, MachineLook.HandY + 0.014, z1 - 0.26], [x1 - 0.22, MachineLook.HandY + 0.046, z1 - 0.08], MachineLook.Stop));
        p.Add(L([x0 + 0.02, Plinth, z1 - 0.008], [x1 - 0.02, Plinth + 0.16, z1 + 0.008], MachineLook.Base)); // (a kick plate)
        // The cabinet's front: two doors with handles, a vent grille, the power meter and the maker's plate.
        p.Add(L([-0.008, Plinth + 0.2, z1 - 0.004], [0.008, MachineLook.HandY - 0.08, z1 + 0.008], MachineLook.Base)); // (the doors' meeting line)
        foreach (var x in new[] { -0.12, 0.12 })
            p.Add(L([x - 0.015, 0.55, z1 + 0.004], [x + 0.015, 0.8, z1 + 0.04], MachineLook.Steel, "steel")); // (handles)
        for (var k = 0; k < 5; k++)
            p.Add(L([-0.85, 0.4 + k * 0.07, z1 - 0.004], [-0.45, 0.43 + k * 0.07, z1 + 0.012], MachineLook.Base)); // (the vent)
        p.Add(L([0.5, 0.62, z1 - 0.004], [0.8, 0.92, z1 + 0.02], "#e8e6df", shape: Cyl("+z", 14))); // (the power meter's face)
        p.Add(L([0.64, 0.74, z1 + 0.016], [0.66, 0.88, z1 + 0.032], MachineLook.Ink)); // (its needle)
        p.Add(L([0.45, 0.4, z1 - 0.004], [0.85, 0.54, z1 + 0.012], MachineLook.In)); // (the maker's plate)
        p.Add(L([0.49, 0.45, z1 + 0.008], [0.81, 0.49, z1 + 0.024], MachineLook.Ink));
        // On the screen housing: the job lamp over it, a crane warning plate beside it (the crane never stops for anyone in the bay).
        p.Add(L([-0.1, 1.98, Front + 0.06], [0.1, 2.08, Front + 0.16], MachineLook.Accent)); // (the lamp's hood)
        MachineLook.Hazard(p, [x1 - 0.05, 1.3, Front + 0.04], [x1 + 0.006, 1.7, Front + 0.18]);
        // The second conveyor's mouth on the cabinet's -x side (Piece in 2), and its arrow.
        MachineLook.Port(p, [x0, 0.3, 2.65], 0, -1, 0.25, 0.08, true);
        MachineLook.Arrow(p, 0, x0, -1, 2, -1, 0.75, 2.65, 0.3, 0.2, MachineLook.In);
        // On the bay side (+x) of the cabinet and housing: the out band and an arrow towards the bay.
        p.Add(L([x1 - 0.004, 0.9, Front + 0.1], [x1 + 0.012, 1.0, z1 - 0.1], MachineLook.Out));
        p.Add(L([x1 - 0.004, 1.3, z0 + 0.06], [x1 + 0.012, 1.4, Back - 0.06], MachineLook.Out));
        // Lifting eyes on the housing's lid and the cabinet's top: where a crane takes the Core.
        foreach (var (x, z) in new[] { (-0.8, -2.9), (0.8, -2.9), (-0.85, 2.92), (0.85, 2.92) })
        {
            var y = z < 0 ? 1.8 : MachineLook.HandY + 0.014;
            p.Add(L([x - 0.05, y - 0.004, z - 0.02], [x + 0.05, y + 0.1, z + 0.02], MachineLook.Steel, "steel"));
        }
        // ---- Sockets: its foot, and its sides at the floor's height for Bay Blocks (one a cell).
        for (var k = 0; k < 3; k++) // (named by cell from the back: "-y.0.0" is the foot blueprints have stood it by)
        {
            var z = -2.0 + 2 * k;
            p.Sock($"-y.0.{k}", [0, 0, z], "-y");
            p.Sock($"+x.{k}", [x1, Plinth / 2, z], "+x");
            p.Sock($"-x.{k}", [x0, Plinth / 2, z], "-x");
        }
        p.Stats(1600, 1600, 4, 10.0);
        p.Set(("power", 600), ("onBreak", "disable"), ("centreSockets", false), ("collider", "boxes"));
        p.Set(("panels", Py.L(Kw(("source", "station"), ("at", Py.L(0, MachineLook.EyeY, Py.Round(Front + 0.213, 3))), ("size", Py.L(1.4, 0.56)), ("face", "+z"), ("up", "+y"), ("rows", 8), ("range", 12)))));
        static PyDict Fill(string key, string label, string tip) => Kw(("key", key), ("label", label), ("group", "Fill"), ("min", 0), ("max", 100), ("step", 5), ("default", 100), ("unit", "%"),
            ("tooltip", "Survival: a build is filled to this share. " + tip + " 0: none, 100: full."));
        p.Set(("config", Py.L(
            Fill("fillFuel", "Fuel and tanks", "Fuel and fluid tanks, gases too: from tanks piped to Fluid in, gas from linked bins."),
            Fill("fillAmmo", "Ammo", "Ammo boxes, Ammo Containers and fitted magazines: from bulk ammo in linked bins."),
            Fill("fillCharge", "Battery charge", "Batteries: charged from the station's own power, which takes time."),
            Kw(("key", "fillWait"), ("label", "Wait until filled"), ("group", "Fill"), ("type", "toggle"), ("default", false),
                ("tooltip", "On: a build stays in the bay until it is filled. Off: it finishes with what it got and says what is short.")),
            Kw(("key", "subJobs"), ("label", "Sub-jobs"), ("group", "Bay"), ("type", "choice"), ("options", Py.L("Off", "Auto", "Linked")), ("default", 1),
                ("tooltip", "Auto: when idle it builds other stations' sub-blueprint uses on this construct. Linked: also uses sent over a logistics link (the module goes by cargo ship). Off: only your own builds.")))));
        const double section = 1.8, length = 4.8;
        p.Set(("storage", Kw(("pieces", Kw(("section", Py.L(section, 1.2)), ("length", length), ("count", 400), ("crafting", true), ("station", true),
            ("hold", Py.L(Py.L(x0 + 0.05, Plinth + 0.024, Hz0), Py.L(x1 - 0.05, top - 0.01, Hz1))))))));
        p.Set(("ports", Py.L(
            Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, 0.05, z0)), ("face", "-z"), ("opening", Py.L(1.7, 1.7))),
            Kw(("name", "Piece in 2"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(x0, 0.05, 2.65)), ("face", "-x"), ("opening", Py.L(0.5, 0.5))),
            Kw(("name", "Fluid in"), ("type", "pipe"), ("dir", "in"), ("at", Py.L(x0, 1.05, -2.79)), ("face", "-x")),
            Kw(("name", "Chute in"), ("type", "chute"), ("dir", "in"), ("at", Py.L(0, 1.8, -2.79)), ("face", "+y")))));
        p.Set(("machineBlock", BlockKey("core")));
        return p;
    }

    /// <summary>
    /// A Bay Block: one 2 x 2 m cell of a Building Station's bay floor, 25 cm thick, tread plate on top. Its edge trim (a kerb with a hazard
    /// strip, its bolts, an amber floor lamp) and its corner caps are seams: drawn only where no tile of the same bay is welded, so a bay reads
    /// as one floor with one hazard border. In the middle, a steel seat for a mast (welded on its top socket), four tie-down rings and a drain.
    /// </summary>
    private static Part BayBlock()
    {
        var p = new Part("Bay Block", Small, "Extrusion",
            "A 2 x 2 m tile of a Building Station's bay. Weld it beside the station's Core or beside another Bay Block: the bay is the largest full rectangle of them, up to 32 x 32 m. Stack Mast Blocks on the bay's four corners to build higher.",
            group: "Production", color: Body);
        const double e = 0.997, h = Plinth, k = 0.12; // (the slab stops 3 mm short of the cell; k: the kerb's width)
        p.Add(Box([-e, 0, -e], [e, h - 0.02, e], MachineLook.Base));
        p.Add(Box([-e + 0.01, h - 0.024, -e + 0.01], [e - 0.01, h, e - 0.01])!.Update(("finish", "tread plate")));
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
            p.Add(Seam(Edge(-0.002, 0.014, -0.12, 0.12, 0.06, h - 0.06, MachineLook.Accent), face)); // (the floor lamp let into the slab's edge)
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
        p.Sock("+x", [1, h / 2, 0], "+x");
        p.Sock("-x", [-1, h / 2, 0], "-x");
        p.Sock("+z", [0, h / 2, 1], "+z");
        p.Sock("-z", [0, h / 2, -1], "-z");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(900, 1200, 6, 6.0);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BayBlock())]);
}
