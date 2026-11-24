using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.MachineLookShort;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Bake Oven, a roller hearth tunnel on the block system (Bake Oven Lab, Sean 2026-11-24; MachineBlocks.cs, ConstructBakeOven.cs,
/// BakeOvenRender.cs): Constructs/Parts/BakeOven. One Oven Core (the "Bake Oven", the Recuperator: its screen, Mode, Tunnel height,
/// Run, power and status lamp, its heat drums, blower and gas pipe) and any number of identical 2 x 2 m Oven Bed Blocks welded beside
/// it, the bed being their largest full rectangle. The machine draws the tunnel over the bed (its hearth rollers, walls, roof, ribbon
/// windows and swing doors) and the Core's duct to it (BakeOvenRender.cs); it has no crane of its own. A bed block's skirt, kerb, side
/// panels and hazard edge are "seam" boxes: drawn only on the machine's open edges, so a bed of blocks reads as one platen.
/// </summary>
public sealed class BakeOvenParts : IPartGenerator
{
    public string Name => "bake_oven_parts";

    private const string Out = "Constructs/Parts/BakeOven";
    private const string Section = "Ovens";
    private const string Body = "#3a3f46"; // graphite: the oven's frame and cabinet (design page drawing 4)
    private const string Trim = "#2c3036";
    private const string Ceramic = "#6b6259"; // the platen's heater tiles between the steel bars

    /// <summary>The bed's top (m above the foot), and the hearth rollers' tops over it: a Roller Tile's, so a kit rolls in level.</summary>
    public const double BedTop = 0.6, RollTop = OutTableParts.WorkY;
    private const double OriginY = BedTop / 2; // the Core names its cell's middle at the bed layer's, so bed blocks are its layer and masts the ones over it
    private const int BlockKw = 3500, CoreMass = 2600, BlockMass = 1800;

    private static PyDict BlockKey(string role) => role == "core"
        ? Kw(("family", "oven"), ("role", "core"), ("origin", Py.L(0, OriginY, 0)))
        : Kw(("family", "oven"), ("role", role));

    private static PyDict Seam(PyDict? box, string face) => box!.Update(("seam", face));
    private static PyDict Seam(PyDict? box, string a, string b) => box!.Update(("seam", Py.L(a, b)));
    private static PyDict Cyl(int segments, string front) => Kw(("shape", "cylinder"), ("segments", segments), ("front", front));

    /// <summary>
    /// The Oven Core, the Recuperator (Bake Oven Lab, Sean 2026-11-24): twin insulated heat drums on a plinth with a header over them and
    /// a stub up to where the duct leaves for the tunnel's wall (BakeOvenRender.cs draws the duct, its yellow flange and the blower's fan,
    /// which spins faster with the heat); a blower at its foot; on the -x face (the aisle) an instrument cabinet with a big screen on a
    /// frame, its lamp and buttons, a HOT sign; a gauge on the drums, a red gas pipe with a valve and a beacon on a post that glows while
    /// it bakes. Bed blocks weld to any of its four sides at the foot.
    /// </summary>
    private static Part Core()
    {
        var p = new Part("Bake Oven", Small, Section,
            "An Oven Core, the Recuperator: bakes a welded group of subparts in a roller hearth tunnel. Weld Oven Bed Blocks beside it (the bed is their rectangle, up to 32 x 32 m): the oven draws a tunnel over the bed, along the side the Core is welded to, with swing doors at both ends. A kit rolls in on a Roller Tile run that meets the end nearer the Core, the doors shut, it bakes, the doors open, and it rolls out along a run that starts at the far end (or waits for a crane or an arm). Its Tunnel height setting is the clear height over the rollers. Needs power.",
            role: "storage", group: "Production", color: Body);
        const double e = 0.997;
        const string Drum = "#a9aeb2";
        // The plinth, a hazard edge on its aisle side.
        p.Add(Box([-e, 0.003, -e], [e, 0.2, e], MachineLook.Base)!.Update(("paintable", false)));
        MachineLook.Hazard(p, [-e - 0.012, 0.2, -e], [-e + 0.02, 0.28, e]);
        // The instrument cabinet on the aisle side, under the screen (a solid box: the screen's buttons are touched on it).
        p.Add(Box([-e, 0.2, -e + 0.04], [-0.45, 1.98, -0.08]));
        p.Add(L([-e - 0.004, 1.98, -e + 0.02], [-0.43, 2.05, -0.06], Trim, paintable: true)); // (its cap)
        // The twin heat drums: insulated steel, banded, capped; the header over both; the stub up to the duct's elbow.
        foreach (var x in new[] { 0.15, 0.65 })
        {
            p.Add(L([x - 0.25, 0.2, 0.0], [x + 0.25, 3.3, 0.5], Drum, shape: Cyl(20, "+y")));
            foreach (var y in new[] { 0.6, 1.4, 2.2, 3.0 })
                p.Add(L([x - 0.27, y - 0.025, -0.02], [x + 0.27, y + 0.025, 0.52], MachineLook.Base, shape: Cyl(20, "+y")));
            p.Add(L([x - 0.15, 3.3, 0.1], [x + 0.15, 3.36, 0.4], MachineLook.Base, shape: Cyl(14, "+y")));
        }
        p.Add(Box([-0.08, 0.2, 0.02], [0.88, 3.28, 0.48], hidden: true)); // (the drums' solid, for collision: they are round looks)
        p.Add(Box([-0.05, 3.3, -0.05], [0.85, 3.62, 0.55], Drum)); // (the header)
        foreach (var x in new[] { -0.03, 0.83 })
            foreach (var z in new[] { -0.03, 0.53 })
                p.Add(L([x - 0.02, 3.36, z - 0.02], [x + 0.02, 3.6, z + 0.02], MachineLook.Steel, "steel")); // (its corner bolts)
        p.Add(L([0.19, 3.62, 0.04], [0.61, 4.3, 0.46], Drum, "steel", shape: Cyl(16, "+y"))); // (the stub up to the elbow: the duct leaves it for the tunnel)
        p.Add(L([0.17, 4.3, 0.02], [0.63, 4.52, 0.48], MachineLook.Base, shape: Cyl(16, "+y"))); // (the elbow's cap)
        // The blower at the foot: its housing, the yellow ring round its intake, its motor, the shaft. (The fan is drawn spinning.)
        p.Add(L([0.15, 0.35, -1.0], [0.65, 1.15, -0.2], MachineLook.Base, shape: Cyl(18, "+x")));
        p.Add(L([0.11, 0.37, -0.98], [0.15, 1.13, -0.22], MachineLook.Accent, shape: Cyl(18, "+x")));
        p.Add(L([0.55, 0.2, -0.95], [0.95, 0.5, -0.25], Body, paintable: true));
        p.Add(L([0.15, 0.72, -0.63], [0.75, 0.78, -0.57], MachineLook.Steel, "steel", shape: Cyl(8, "+x")));
        // The screen's frame posts beside the cabinet.
        foreach (var z in new[] { -0.9, -0.3 })
            p.Add(L([-0.45, 0.2, z - 0.04], [-0.37, 2.0, z + 0.04], Trim));
        // A gauge on the near drum, the red gas pipe up its side with a valve, the beacon on its post.
        p.Add(L([-0.33, 2.65, 0.1], [-0.28, 2.95, 0.4], "#e8e6df", shape: Cyl(14, "+x")));
        p.Add(L([-0.345, 2.78, 0.24], [-0.33, 2.9, 0.26], MachineLook.Ink));
        p.Add(L([-0.1, 2.78, 0.22], [-0.33, 2.82, 0.28], MachineLook.Steel, "steel")); // (its tap)
        p.Add(L([-0.2, 0.2, 0.8], [-0.1, 2.2, 0.9], "#b8322a", shape: Cyl(10, "+y")));
        p.Add(L([-0.25, 1.5, 0.88], [-0.05, 1.7, 0.92], MachineLook.Accent, shape: Cyl(12, "+z")));
        p.Add(L([-0.24, 1.55, 0.78], [-0.06, 1.65, 0.88], MachineLook.Base)); // (the valve's body)
        p.Add(L([-0.65, 0.2, -0.65], [-0.55, 2.9, -0.55], Trim));
        p.Add(MachineLook.Work(L([-0.68, 2.9, -0.68], [-0.52, 3.1, -0.52], MachineLook.Out, shape: Cyl(12, "+y")), "glow"));
        // Its sockets: on the floor, and a bed block's at the foot of each side (the bed blocks' own height).
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("+x", [1, OriginY, 0], "+x");
        p.Sock("-x", [-1, OriginY, 0], "-x");
        p.Sock("+z", [0, OriginY, 1], "+z");
        p.Sock("-z", [0, OriginY, -1], "-z");
        p.Stats(CoreMass, 3000, 8, 10);
        p.Set(("power", 2000), ("storage", Kw(("bake", Kw(("bedTop", BedTop), ("rollTop", RollTop), ("kwBlock", BlockKw / 1000.0), ("doorSeconds", 3), ("partSeconds", 2), ("partsBlock", 16), ("maxParts", 255))))),
            ("machineBlock", BlockKey("core")),
            ("config", Py.L(
                Kw(("key", "mode"), ("label", "Mode"), ("group", "Oven"), ("options", Py.L("Bake", "Un-bake")), ("default", 0),
                    ("tooltip", "Bake: the welded group in the tunnel becomes one part. Un-bake: a baked part in the tunnel is set back out as its subparts.")),
                Kw(("key", "height"), ("label", "Tunnel height"), ("group", "Oven"), ("unit", "m"), ("min", 1.5), ("max", 12), ("step", 0.5), ("default", 3),
                    ("tooltip", "The tunnel's clear height over the rollers: the tallest kit it bakes.")),
                Kw(("key", "ink"), ("type", "colour"), ("label", "Screen colour"), ("group", "Display"), ("default", "#000000"), ("tooltip", "The colour of what its screen shows. Black: its own colours.")))),
            ("collider", "boxes"));
        // The big screen on the aisle face, facing the in feed, its lamp and button shelf.
        var keepClear = p.Sockets.Select(k => (new[] { Py.D(((List<object?>)k["at"]!)[0]), Py.D(((List<object?>)k["at"]!)[1]), Py.D(((List<object?>)k["at"]!)[2]) }, (string)k["dir"]!)).ToList();
        var (plate, panel, lamp) = PartScreens.Mount(p.Boxes, keepClear, faces: ["-x"], rows: 4, width: 0.8, lamp: true);
        PartScreens.Fit(p.Boxes, plate, most: 200);
        MachineLook.Buttons(p, plate, (string)panel["face"]!);
        if (lamp is not null)
            p.Boxes.Add(lamp);
        p.Set(("panels", Py.L(panel)));
        // The HOT sign on the cabinet, under the screen: yellow plate, a black border, the word in black bars.
        const double sx0 = -e - 0.032;
        p.Add(L([sx0, 0.3, -0.86], [-e + 0.004, 0.56, -0.22], MachineLook.Ink));
        p.Add(L([sx0 - 0.012, 0.32, -0.84], [sx0 + 0.002, 0.54, -0.24], MachineLook.Accent));
        void Bar(double z0, double z1, double y0, double y1) => p.Add(L([sx0 - 0.026, y0, -0.54 - z1], [sx0 - 0.01, y1, -0.54 - z0], MachineLook.Ink));
        Bar(0.18, 0.21, 0.36, 0.5); Bar(0.09, 0.12, 0.36, 0.5); Bar(0.12, 0.18, 0.415, 0.445);
        Bar(0.01, 0.04, 0.36, 0.5); Bar(-0.08, -0.05, 0.36, 0.5); Bar(-0.05, 0.01, 0.36, 0.39); Bar(-0.05, 0.01, 0.47, 0.5);
        Bar(-0.25, -0.12, 0.47, 0.5); Bar(-0.2, -0.17, 0.36, 0.47);
        // A maker's plate on the cabinet's end.
        p.Add(L([-0.8, 1.2, -e + 0.004], [-0.55, 1.34, -e + 0.04], MachineLook.In));
        return p;
    }

    /// <summary>
    /// An Oven Bed Block: one 2 x 2 m cell of the bed, 0.6 m deep. A steel platen on top laid with ceramic heater tiles between bars,
    /// bolted at its corners; a kerb, an orange heated edge, a hazard edge and a skirt on its sides, each a seam (drawn only on the
    /// machine's open edges); sunk side panels with louvres and a terminal box. Another block welds on any face.
    /// </summary>
    private static Part BedBlock()
    {
        var p = new Part("Oven Bed Block", Small, Section,
            "Weld it beside an Oven Core, or beside another Oven Bed Block: 2 x 2 m more heated bed, and the tunnel over it 2 m longer or wider. The bed is the largest full rectangle of blocks next to the Core; a block outside it idles.",
            group: "Production", color: Body);
        const double e = 0.997, t = BedTop, k = 0.07; // (k: how far the side panels are sunk)
        // The body: a core sunk k on every side, with full-width ribs at its ends and its middle, and the platen on top.
        p.Add(Box([-e + k, 0.003, -e + k], [e - k, t - 0.1, e - k]));
        foreach (var a in new[] { -0.9, 0.0, 0.9 })
        {
            p.Add(Box([a - 0.06, 0.003, -e], [a + 0.06, t - 0.1, e]));
            p.Add(Box([-e, 0.003, a - 0.06], [e, t - 0.1, a + 0.06]));
        }
        p.Add(Box([-e, t - 0.1, -e], [e, t, e])); // (the platen's slab)
        p.Add(Box([-e, t + 0.003, -e], [e, RollTop - 0.003, e], hidden: true)); // (solid to the rollers' tops: a kit rests on the roll line)
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
        p.Sock("top", [0, RollTop, 0], "+y");
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(BlockMass, 2000, 8, 8);
        p.Set(("machineBlock", BlockKey("block")), ("centreSockets", false), ("collider", "boxes"));
        return p;
    }

    public void Make(PartOutput o) => WriteParts(o, Out, [("", Core()), ("", BedBlock())]);
}
