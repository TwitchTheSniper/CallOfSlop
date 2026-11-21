using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Bake Oven on the block system (design page drawing 19; MachineBlocks.cs, ConstructBakeOven.cs, BakeOvenRender.cs):
/// Constructs/Parts/BakeOven. One Oven Core (the "Bake Oven": its screen, Mode, Run, power and status lamp, its burner stack and
/// gas train) and any number of identical 2 x 2 m Oven Bed Blocks welded beside it, the bed being their largest full rectangle.
/// The shared 2 m Mast Block stacks on the bed's four corner cells; the machine draws its hood, the hood's screw columns and its
/// own bridge between those masts (BakeOvenRender.cs), so nothing here moves. A bed block's skirt, kerb, side panels and hazard
/// edge are "seam" boxes: drawn only on the machine's open edges, so a bed of blocks reads as one platen.
/// </summary>
public sealed class BakeOvenParts : IPartGenerator
{
    public string Name => "bake_oven_parts";

    private const string Out = "Constructs/Parts/BakeOven";
    private const string Section = "Ovens";
    private const string Body = "#3a3f46"; // graphite: the oven's frame and cabinet (design page drawing 4)
    private const string Trim = "#2c3036";
    private const string Ceramic = "#6b6259"; // the platen's heater tiles between the steel bars

    /// <summary>The bed's top (m above the foot): a mast stands on it, and the hood comes down onto it (ConstructBakeOven.BedTop).</summary>
    public const double BedTop = 0.6;
    private const double OriginY = BedTop / 2; // the Core names its cell's middle at the bed layer's, so bed blocks are its layer and masts the ones over it
    private const int BlockKw = 3500, CoreMass = 2600, BlockMass = 1800;

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "oven"), ("role", "core"), ("origin", Py.L(0, OriginY, 0)))
        : Kw(("family", "oven"), ("role", role));

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));
    private static PyDict Seam(PyDict? box, string a, string b) => box!.Update(("seam", Py.L(a, b)));
    private static PyDict Cyl(int segments, string front) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    /// <summary>
    /// The Oven Core: a 2 m control cabinet with its burner. On the -x face (the aisle side) the screen, its lamp and the button shelf,
    /// a HOT sign and a maker's plate; on +z the gas train (a pipe with two valves and a pressure gauge) down to the burner box; on top
    /// the flue stack, which smokes while it bakes, a louvred vent that glows, and the cable conduit to the bed. Bed blocks weld to any
    /// of its four sides at the foot.
    /// </summary>
    private static Part Core()
    {
        var p = new Part("Bake Oven", Small, Section,
            "An Oven Core: bakes a welded group of subparts set on its bed under a heavy hood. Weld Oven Bed Blocks beside it (the bed is their rectangle, up to 32 x 32 m) and stack Mast Blocks on the bed's four corners: the shortest corner mast sets the clear height under the hood. Loaded from above, by a crane, a Lifter or the physics gun. The hood comes down on whatever is on the bed. Needs power.",
            role: "storage", group: "Production", color: Body);
        const double e = 0.997, h = 1.997;
        // The cabinet: a plinth, the body sunk 6 cm between a corner post at each edge, a cornice on top.
        p.Add(Box([-e, 0.003, -e], [e, 0.16, e], MachineLook.Base)!.Update(("paintable", false)));
        p.Add(Box([-e + 0.06, 0.16, -e + 0.06], [e - 0.06, 1.86, e - 0.06]));
        foreach (var (sx, sz) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            p.Add(Box([sx > 0 ? e - 0.12 : -e, 0.16, sz > 0 ? e - 0.12 : -e], [sx > 0 ? e : -e + 0.12, 1.86, sz > 0 ? e : -e + 0.12], Trim));
        p.Add(Box([-e, 1.86, -e], [e, h, e], Trim));
        // Its sockets: on the floor, and a bed block's at the foot of each side (the bed blocks' own height), a mast or a stack on top.
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, OriginY, 0], "+x");
        p.Sock("-x", [-1, OriginY, 0], "-x");
        p.Sock("+z", [0, OriginY, 1], "+z");
        p.Sock("-z", [0, OriginY, -1], "-z");
        p.Sock("top", [0, 2, 0], "+y");
        p.Stats(CoreMass, 3000, 8, 10);
        p.Set(("power", 2000), ("storage", Kw(("bake", Kw(("bedTop", BedTop), ("kwBlock", BlockKw / 1000.0), ("hoodSpeed", 1), ("partSeconds", 2), ("partsBlock", 16), ("maxParts", 255))))),
            ("machineBlock", BlockKey("core")),
            ("config", Py.L(
                Kw(("key", "mode"), ("label", "Mode"), ("group", "Oven"), ("options", Py.L("Bake", "Un-bake")), ("default", 0),
                    ("tooltip", "Bake: the welded group on the bed becomes one part. Un-bake: a baked part on the bed is set back out as its subparts.")),
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("collider", "boxes"));
        // The control face on -x: screen, lamp, button shelf.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 4, width: 0.84, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        p.Set(("panels", Py.L(panel)));
        // ---- The look ----
        MachineLook.Skirt(p, -1, 1, -1, 1, 0, 0.16, 0.012);
        // Recessed panels on the three plain sides: a raised frame round a sunk panel, a stiffener rib across the middle.
        foreach (var (n, sg) in new[] { (0, 1), (2, 1), (2, -1) })
        {
            var f = sg * (e - 0.06); // the body's face
            double[] P(double a, double y, double depth) { var v = new double[3]; v[n] = f + sg * depth; v[2 - n] = a; v[1] = y; return v; }
            void Slab(double a0, double a1, double y0, double y1, double d0, double d1, string colour, bool paint = false)
            {
                var lo = P(a0, y0, Math.Min(d0, d1));
                var hi = P(a1, y1, Math.Max(d0, d1));
                p.Add(L([Math.Min(lo[0], hi[0]), lo[1], Math.Min(lo[2], hi[2])], [Math.Max(lo[0], hi[0]), hi[1], Math.Max(lo[2], hi[2])], colour, paintable: paint));
            }
            Slab(-0.86, 0.86, 1.7, 1.78, 0.004, 0.03, Body, true); // (the frame round the panel)
            Slab(-0.86, 0.86, 0.26, 0.34, 0.004, 0.03, Body, true);
            Slab(-0.86, -0.78, 0.34, 1.7, 0.004, 0.03, Body, true);
            Slab(0.78, 0.86, 0.34, 1.7, 0.004, 0.03, Body, true);
            Slab(-0.78, 0.78, 0.98, 1.04, 0.004, 0.022, Body, true); // (the stiffener rib)
            foreach (var a in new[] { -0.82, 0.82 })
                foreach (var y in new[] { 0.3, 1.0, 1.74 })
                    Slab(a - 0.016, a + 0.016, y - 0.016, y + 0.016, 0.03, 0.046, MachineLook.Steel); // (bolt heads on the frame)
        }
        // A louvred vent low on +x, glowing while it bakes; the burner's air intake.
        for (var k = 0; k < 6; k++)
        {
            var y = 0.44 + k * 0.08;
            p.Add(L([e - 0.05, y, -0.55], [e + 0.01, y + 0.035, -0.12], MachineLook.Base));
        }
        p.Add(MachineLook.Work(L([e - 0.058, 0.42, -0.56], [e - 0.046, 0.92, -0.11], MachineLook.Hot), "glow"));
        // The gas train on +z: a supply pipe down the face to the burner box, two valves with yellow handwheels, a pressure gauge.
        p.Add(L([0.42, 0.34, e + 0.03], [0.52, 1.7, e + 0.13], "#b8322a", shape: Cyl(10, "+y")));
        p.Add(L([0.36, 0.28, e - 0.004], [0.94, 0.62, e + 0.2], MachineLook.Base)); // (the burner box)
        p.Add(L([0.4, 0.62, e + 0.05], [0.88, 0.66, e + 0.16], MachineLook.Steel, "steel")); // (its lid's lip)
        foreach (var y in new[] { 0.9, 1.35 })
        {
            p.Add(L([0.39, y - 0.06, e + 0.02], [0.55, y + 0.06, e + 0.14], MachineLook.Base)); // (a valve body)
            p.Add(L([0.36, y - 0.11, e + 0.15], [0.58, y + 0.11, e + 0.18], MachineLook.Accent, shape: Cyl(10, "+z"))); // (its handwheel)
        }
        p.Add(L([0.6, 1.4, e + 0.004], [0.8, 1.6, e + 0.044], "#e8e6df", shape: Cyl(12, "+z"))); // (the gauge's face)
        p.Add(L([0.69, 1.47, e + 0.044], [0.73, 1.56, e + 0.06], MachineLook.Ink)); // (its needle)
        p.Add(L([0.52, 1.47, e + 0.04], [0.6, 1.53, e + 0.08], MachineLook.Steel, "steel")); // (its tap off the pipe)
        // The cable conduit from the cabinet down to the bed's side, clipped at three points, with a junction box.
        p.Add(L([-0.5, 0.18, e + 0.03], [-0.42, 1.62, e + 0.11], MachineLook.Base, shape: Cyl(8, "+y")));
        foreach (var y in new[] { 0.4, 0.95, 1.5 })
            p.Add(L([-0.53, y - 0.025, e - 0.004], [-0.39, y + 0.025, e + 0.12], MachineLook.Steel));
        p.Add(L([-0.74, 1.6, e - 0.004], [-0.2, 1.82, e + 0.1], MachineLook.Base));
        // The HOT sign on the aisle face, under the screen's shelf: yellow plate, a black border, the word in black bars.
        const double sx0 = -e - 0.032;
        p.Add(L([sx0, 0.3, -0.42], [-e + 0.004, 0.62, 0.42], MachineLook.Ink));
        p.Add(L([sx0 - 0.012, 0.32, -0.4], [sx0 + 0.002, 0.6, 0.4], MachineLook.Accent));
        void Bar(double z0, double z1, double y0, double y1) => p.Add(L([sx0 - 0.026, y0, z0], [sx0 - 0.01, y1, z1], MachineLook.Ink));
        // H (its strokes read from the aisle, so the word runs from +z to -z)
        Bar(0.24, 0.28, 0.37, 0.55); Bar(0.12, 0.16, 0.37, 0.55); Bar(0.16, 0.24, 0.445, 0.475);
        // O
        Bar(0.02, 0.06, 0.37, 0.55); Bar(-0.1, -0.06, 0.37, 0.55); Bar(-0.06, 0.02, 0.37, 0.4); Bar(-0.06, 0.02, 0.52, 0.55);
        // T
        Bar(-0.32, -0.16, 0.52, 0.55); Bar(-0.26, -0.22, 0.37, 0.52);
        // The maker's plate beside the sign, two rivets on it.
        p.Add(L([-e - 0.03, 0.3, 0.5], [-e + 0.004, 0.46, 0.8], MachineLook.In));
        p.Add(L([-e - 0.044, 0.36, 0.53], [-e - 0.03, 0.4, 0.77], MachineLook.Ink));
        // On top: the flue stack (it smokes while it bakes), its rain cap, and a vent hood over the cabinet with a glowing slot.
        p.Add(L([0.35, h, -0.65], [0.65, 2.7, -0.35], MachineLook.Base, shape: Cyl(14, "+y")));
        p.Add(L([0.31, 2.48, -0.69], [0.69, 2.52, -0.31], MachineLook.Steel, "steel", shape: Cyl(14, "+y"))); // (a band round it)
        p.Add(MachineLook.Work(L([0.3, 2.78, -0.7], [0.7, 2.82, -0.3], MachineLook.Base, shape: Cyl(14, "+y")), "smoke"));
        foreach (var (x, z) in new[] { (0.36, -0.68), (0.64, -0.68), (0.36, -0.32), (0.64, -0.32) })
            p.Add(L([x - 0.012, 2.7, z - 0.012], [x + 0.012, 2.78, z + 0.012], MachineLook.Base)); // (the cap's legs)
        p.Add(L([-0.8, h, -0.4], [-0.1, 2.2, 0.5], Trim, paintable: true));
        p.Add(MachineLook.Work(L([-0.7, 2.2, -0.3], [-0.2, 2.214, 0.4], MachineLook.Hot), "glow"));
        foreach (var z in new[] { -0.15, 0.05, 0.25 })
            p.Add(L([-0.72, 2.214, z - 0.02], [-0.18, 2.23, z + 0.02], MachineLook.Base)); // (bars over the slot)
        // Lifting eyes at the cornice's corners.
        foreach (var (x, z) in new[] { (0.85, 0.85), (-0.85, 0.85), (0.85, -0.85), (-0.85, -0.85) })
            p.Add(L([x - 0.05, h, z - 0.015], [x + 0.05, h + 0.08, z + 0.015], MachineLook.Steel, "steel"));
        // A hazard band round the cornice: it bakes and burns whatever is on its bed.
        MachineLook.Hazard(p, [-e - 0.014, 1.88, -e - 0.014], [e + 0.014, 1.97, -e]);
        MachineLook.Hazard(p, [-e - 0.014, 1.88, e], [e + 0.014, 1.97, e + 0.014]);
        return p;
    }

    /// <summary>
    /// An Oven Bed Block: one 2 x 2 m cell of the bed, 0.6 m deep. A steel platen on top laid with ceramic heater tiles between bars,
    /// bolted at its corners; a kerb, an orange heated edge, a hazard edge and a skirt on its sides, each a seam (drawn only on the
    /// machine's open edges); sunk side panels with louvres and a terminal box. A mast or another block welds on any face.
    /// </summary>
    private static Part BedBlock()
    {
        var p = new Part("Oven Bed Block", Small, Section,
            "Weld it beside an Oven Core, or beside another Oven Bed Block: 2 x 2 m more heated bed. The bed is the largest full rectangle of blocks next to the Core; a block outside it idles. Stack Mast Blocks on the bed's four corners for the hood.",
            group: "Production", color: Body);
        const double e = 0.997, t = BedTop, k = 0.07; // (k: how far the side panels are sunk)
        // The body: a core sunk k on every side, with full-width ribs at its ends and its middle, and the platen on top.
        p.Add(Box([-e + k, 0.003, -e + k], [e - k, t - 0.1, e - k]));
        foreach (var a in new[] { -0.9, 0.0, 0.9 })
        {
            p.Add(Box([a - 0.06, 0.003, -e], [a + 0.06, t - 0.1, e]));
            p.Add(Box([-e, 0.003, a - 0.06], [e, t - 0.1, a + 0.06]));
        }
        p.Add(Box([-e, t - 0.1, -e], [e, t, e])); // (the platen's slab: a mast stands on it)
        foreach (var (side, n, sg) in new[] { ("+x", 0, 1), ("-x", 0, -1), ("+z", 2, 1), ("-z", 2, -1) })
        {
            // A fill in the body's tone closes the sunk panel where a block is welded on, so no slot runs between blocks.
            double[] lo = [-e + k, 0.006, -e + k], hi = [e - k, t - 0.104, e - k];
            if (sg > 0) { lo[n] = e - k - 0.004; hi[n] = e - 0.004; } else { lo[n] = -e + 0.004; hi[n] = -e + k + 0.004; }
            p.Add(L(lo, hi, Body, paintable: true)!.Update(("seamFill", side)));
            // On an open edge: the kerb's lip proud of the platen, its heated band (orange: hot), the hazard edge and the skirt.
            double[] Pt(double along, double y, double out_) { var v = new double[3]; v[n] = sg * out_; v[2 - n] = along; v[1] = y; return v; }
            PyDict? Face(double a0, double a1, double y0, double y1, double o0, double o1, string colour, string? finish = null)
            {
                var u = Pt(a0, y0, o0);
                var w = Pt(a1, y1, o1);
                return L([Math.Min(u[0], w[0]), y0, Math.Min(u[2], w[2])], [Math.Max(u[0], w[0]), y1, Math.Max(u[2], w[2])], colour, finish);
            }
            p.Add(Seam(Face(-e, e, t - 0.1, t + 0.05, e - 0.08, e + 0.012, MachineLook.Base), side)); // (the kerb)
            p.Add(Seam(Face(-e + 0.02, e - 0.02, t - 0.16, t - 0.1, e - 0.004, e + 0.016, MachineLook.Out), side)); // (the heated edge band)
            p.Add(Seam(Face(-e + 0.02, e - 0.02, t - 0.24, t - 0.16, e - 0.004, e + 0.014, MachineLook.Accent, "hazard paint"), side));
            p.Add(Seam(Face(-e, e, 0.003, 0.12, e - 0.004, e + 0.012, MachineLook.Base), side)); // (the skirt)
            // In the sunk panels between the ribs: louvres in one, a terminal box with its cable in the other.
            foreach (var y in new[] { 0.17, 0.23, 0.29 })
                p.Add(Seam(Face(-0.8, -0.1, y, y + 0.03, e - k - 0.006, e - k + 0.02, MachineLook.Base), side));
            p.Add(Seam(Face(0.25, 0.6, 0.14, 0.34, e - k - 0.006, e - k + 0.05, MachineLook.Base), side));
            p.Add(Seam(Face(0.36, 0.48, 0.34, 0.39, e - k - 0.004, e - k + 0.03, MachineLook.Steel), side)); // (its gland)
        }
        // The platen: steel bars across, ceramic heater tiles between them (glowing while it bakes), a bolt at each corner.
        p.Add(L([-e + 0.02, t, -e + 0.02], [e - 0.02, t + 0.016, e - 0.02], MachineLook.Steel, "steel"));
        foreach (var z in new[] { -0.75, -0.25, 0.25, 0.75 })
        {
            p.Add(L([-0.9, t + 0.016, z - 0.2], [0.9, t + 0.03, z + 0.2], Ceramic));
            p.Add(MachineLook.Work(L([-0.86, t + 0.03, z - 0.04], [0.86, t + 0.042, z + 0.04], MachineLook.Hot), "glow"));
        }
        foreach (var (x, z) in new[] { (0.93, 0.93), (-0.93, 0.93), (0.93, -0.93), (-0.93, -0.93) })
            p.Add(L([x - 0.025, t + 0.016, z - 0.025], [x + 0.025, t + 0.04, z + 0.025], MachineLook.Base, shape: Cyl(6, "+y")));
        p.Sock("+x", [1, OriginY, 0], "+x");
        p.Sock("-x", [-1, OriginY, 0], "-x");
        p.Sock("+z", [0, OriginY, 1], "+z");
        p.Sock("-z", [0, OriginY, -1], "-z");
        p.Sock("top", [0, t, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(BlockMass, 2000, 8, 8);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BedBlock())]);
}
