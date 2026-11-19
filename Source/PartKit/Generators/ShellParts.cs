using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The robot shells: Constructs/Parts/Shells (the Robot Shell and the Shell Control Chair that wakes you in one). Was
/// tools/constructs/shell_parts.py. (The Depot Lander and Ministry Courier blueprints went with the penal colony, 2026-11-17.)
/// </summary>
public sealed class ShellParts : IPartGenerator
{
    public string Name => "shell_parts";

    private const string Out = "Constructs/Parts/Shells";
    private const string Section = "Shells";
    private const string Steel = "#8b949c", Dark = "#30363d", Joint = "#56606a", Visor = "#38d0e8", Lamp = "#f2a93b", Seat = "#3b4652";
    private const int Range = 3000; // m: the chair's reach 

    private static PyDict Channel() => new()
    {
        ["key"] = "channel", ["label"] = "Channel", ["group"] = "General", ["min"] = 1, ["max"] = 99, ["step"] = 1, ["default"] = 1,
        ["tooltip"] = "A chair wakes you in the nearest free shell on its channel.",
    };

    /// <summary>A standing robot about a person's size, its feet on its origin and its visor towards -z (a part's front).</summary>
    private static Part Shell()
    {
        var p = new Part("Robot Shell", Small, Section, $"A robot body. Sit in a Shell Control Chair on its channel, within {Py.G(Range / 1000.0)} km, to wake in it: if it is destroyed you only wake in the chair again.", tool: "wrench", group: "Logic", color: Steel);
        Robot(p, 1, drill: false);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 1.25, 0.2], "+z");
        p.Stats(38, 160, 3, 3);
        p.Set(("shell", new PyDict { ["body"] = true }), ("config", Py.L(Channel())), ("centreSockets", false));
        return p;
    }

    /// <summary>
    /// The robot's body at <paramref name="k"/> times a person's size (the Robot Shell is 1; the Mining Shell, Constructs/Shells.json's
    /// "scale", 1.5). <paramref name="drill"/>: the right arm's forearm and hand are left off for the drill arm.
    /// </summary>
    private static void Robot(Part p, double k, bool drill)
    {
        PyDict? B(double[] lo, double[] hi, string? c = null, bool detail = false) => Box([lo[0] * k, lo[1] * k, lo[2] * k], [hi[0] * k, hi[1] * k, hi[2] * k], c, detail: detail);
        foreach (var s in new[] { -1, 1 })
        {
            double x0 = Math.Min(s * 0.045, s * 0.175), x1 = Math.Max(s * 0.045, s * 0.175);
            p.Add(B([x0 - 0.01, 0.0, -0.15], [x1 + 0.01, 0.07, 0.09], Dark)); // feet
            p.Add(B([x0, 0.06, -0.07], [x1, 0.47, 0.07], Joint)); // shins
            p.Add(B([x0 - 0.005, 0.45, -0.085], [x1 + 0.005, 0.53, 0.085], Dark, detail: true)); // knees
            p.Add(B([x0 + 0.005, 0.52, -0.075], [x1 - 0.005, 0.88, 0.075])); // thighs
            double a0 = Math.Min(s * 0.26, s * 0.37), a1 = Math.Max(s * 0.26, s * 0.37);
            p.Add(B([a0 - 0.02, 1.36, -0.1], [a1 + 0.02, 1.53, 0.1], Joint)); // shoulders
            p.Add(B([a0, 1.0, -0.06], [a1, 1.37, 0.06])); // upper arms
            if (drill && s > 0)
                continue; // (the right forearm is the drill: MiningShell)
            p.Add(B([a0 + 0.005, 0.74, -0.055], [a1 - 0.005, 1.01, 0.055], Joint)); // forearms
            p.Add(B([a0 + 0.01, 0.64, -0.05], [a1 - 0.01, 0.75, 0.05], Dark, detail: true)); // hands
        }
        p.Add(B([-0.2, 0.86, -0.11], [0.2, 1.02, 0.1], Dark)); // pelvis
        p.Add(B([-0.245, 1.0, -0.135], [0.245, 1.5, 0.125])); // torso
        p.Add(B([-0.17, 1.13, -0.16], [0.17, 1.42, -0.12], Joint, detail: true)); // chest plate
        p.Add(B([-0.05, 1.22, -0.172], [0.05, 1.27, -0.15], Lamp, detail: true)); // status lamp
        if (!drill)
            p.Add(B([-0.16, 1.07, 0.11], [0.16, 1.44, 0.2], Dark)); // power pack on the back (the Mining Shell's hopper is its own)
        p.Add(B([-0.06, 1.48, -0.06], [0.06, 1.58, 0.06], Dark)); // neck
        p.Add(B([-0.135, 1.56, -0.14], [0.135, 1.83, 0.125])); // head
        p.Add(B([-0.11, 1.65, -0.158], [0.11, 1.73, -0.12], Visor)); // visor
        p.Add(B([0.07, 1.82, -0.015], [0.09, 1.96, 0.015], Dark, detail: true)); // aerial
    }

    /// <summary>The hopper's bulk box (the part's storage): inside its walls, open at the top. Also where the game draws the ore.</summary>
    public static readonly double[] HopperMin = [-0.31, 1.395, 0.245], HopperMax = [0.31, 2.18, 0.755];

    public const int HopperUnits = 5400; // the Ore Hopper's load: three minutes of its drill arm at 30 a second (the user, 2026-11-17; a Materials Bin holds 600)

    /// <summary>
    /// The Mining Shell (the user, 2026-11-17; docs/sandbox/design/mining-shell.md): the robot at 1.5x, an open ore hopper on its back
    /// (the part's own bin: worn, it is the hopper) and a drill for a right forearm. Frame "Mining" in Constructs/Shells.json.
    /// </summary>
    private static Part MiningShell()
    {
        const string Ore = "#5a4a3c", Bit = "#c9ced4", Hazard = "#e0a020";
        var p = new Part("Mining Shell", Small, Section, $"A heavy robot body half again a person's size, an ore hopper on its back and a drill for a right arm: the ground drill's scoops tap ore deposits into the hopper. Wake in it from a Shell Control Chair on its channel. Crouch to pass doors.", role: "storage", tool: "wrench", group: "Logic", color: Steel);
        Robot(p, 1.5, drill: true);
        // The hopper: a floor, four walls (the sides lower than the ends), a lip, and two straps over the shoulders.
        p.Add(Box([-0.36, 1.35, 0.2], [0.36, 1.39, 0.8], Dark)); // floor
        p.Add(Box([-0.36, 1.393, 0.2], [0.36, 2.25, 0.24], Ore)); // front wall, against the back
        p.Add(Box([-0.36, 1.393, 0.76], [0.36, 2.25, 0.8], Ore)); // back wall
        p.Add(Box([-0.358, 1.393, 0.243], [-0.322, 2.2, 0.757], Ore)); // side walls
        p.Add(Box([0.322, 1.393, 0.243], [0.358, 2.2, 0.757], Ore));
        p.Add(Box([-0.364, 2.253, 0.196], [0.364, 2.28, 0.245], Hazard, detail: true)); // the front wall's lip
        p.Add(Box([-0.364, 2.253, 0.755], [0.364, 2.28, 0.804], Hazard, detail: true)); // the back wall's lip
        foreach (var x in new[] { -0.25, 0.17 })
            p.Add(Box([x, 2.0, -0.02], [x + 0.08, 2.32, 0.197], Joint, detail: true)); // straps over the shoulders
        p.Add(Box([-0.2, 1.55, 0.805], [0.2, 1.64, 0.83], Lamp, detail: true)); // a load band on its back
        // The drill arm, out of the right shoulder: a housing down the forearm, a motor collar, a stepped bit forward.
        p.Add(Box([0.4, 1.26, -0.42], [0.545, 1.497, 0.08], Joint)); // the housing
        p.Add(Box([0.385, 1.24, -0.12], [0.56, 1.52, 0.03], Dark)); // the motor collar
        p.Add(Box([0.425, 1.3, -0.62], [0.52, 1.46, -0.41], Bit)); // the bit's root
        p.Add(Box([0.445, 1.33, -0.8], [0.5, 1.43, -0.61], Bit)); // its middle
        p.Add(Box([0.46, 1.355, -0.9], [0.485, 1.405, -0.79], Bit, detail: true)); // its point
        p.Add(Box([0.39, 1.505, -0.41], [0.555, 1.53, -0.2], Hazard, detail: true)); // a hazard band on the housing
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 1.9, 0.8], "+z");
        p.Stats(140, 260, 6, 5);
        p.Set(("shell", new PyDict { ["body"] = true }), ("config", Py.L(Channel())), ("centreSockets", false),
            ("storage", new PyDict { ["bulk"] = new PyDict { ["material"] = "any", ["capacity"] = HopperUnits, ["min"] = Py.L(HopperMin.Cast<object?>().ToArray()), ["max"] = Py.L(HopperMax.Cast<object?>().ToArray()) } }));
        return p;
    }

    /// <summary>
    /// A link couch, not a passenger seat (Karl, 2026-11-14): a padded couch on a machine plinth, a spine column up its back
    /// carrying the cables, consoles with small lamps on both arms and the link helmet hung from the spine, down over the
    /// sitter's head. The solid boxes are the old chair's (the seated pose fits them); the machinery is detail. Faces that
    /// meet are 3 mm or more apart (z-fighting.md). The link-up seen from inside it is Ui/UiShellLink.cs.
    /// </summary>
    private static Part Chair()
    {
        const string Pad = "#262c33", Metal = "#4a535c", Cable = "#14171a", Green = "#56e07a";
        var p = new Part("Shell Control Chair", Small, Section, $"A link couch: sit in it and its helmet wakes you in the nearest free Robot Shell on its channel, up to {Py.G(Range / 1000.0)} km away. Your body waits here.", role: "seat", tool: "wrench", group: "Logic", color: Seat);
        p.Add(Box([-0.28, 0, -0.3], [0.28, 0.42, 0.3], hidden: true)); // the couch's solid (the seated pose fits it); drawn below as a pedestal
        p.Add(Box([-0.28, 0.4, 0.22], [0.28, 1.15, 0.34], Dark)); // back (solid)
        p.Add(Box([-0.36, 0.3, -0.25], [-0.29, 0.62, 0.2], hidden: true)); // arms (solid), drawn as rails
        p.Add(Box([0.29, 0.3, -0.25], [0.36, 0.62, 0.2], hidden: true));
        p.Add(Box([-0.3, 0.003, -0.32], [0.3, 0.06, 0.36], Cable, detail: true)); // the wide foot
        p.Add(Box([-0.2, 0.02, -0.324], [0.2, 0.045, -0.32], Visor, detail: true)); // its lamp strip
        p.Add(Box([-0.12, 0.06, -0.15], [0.12, 0.303, 0.15], Metal, detail: true)); // the pedestal
        p.Add(Box([-0.27, 0.3, -0.29], [0.27, 0.42, 0.217], Dark, detail: true)); // the couch's pan
        p.Add(Box([-0.25, 0.423, -0.27], [0.25, 0.47, 0.2], Pad, detail: true)); // its pad
        p.Add(Box([-0.23, 0.473, 0.18], [0.23, 1.1, 0.217], Pad, detail: true)); // the back's pad
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            double X(double v) => sx * v;
            double[] Lo(double x0, double y, double z, double x1) => [Math.Min(X(x0), X(x1)), y, z];
            double[] Hi(double x0, double y, double z, double x1) => [Math.Max(X(x0), X(x1)), y, z];
            p.Add(Box(Lo(0.3, 0.6, -0.25, 0.35), Hi(0.3, 0.63, 0.18, 0.35), Joint, detail: true)); // the arm rail
            p.Add(Box(Lo(0.315, 0.42, 0.06, 0.335), Hi(0.315, 0.6, 0.09, 0.335), Joint, detail: true)); // its strut
            p.Add(Box(Lo(0.29, 0.633, -0.25, 0.36), Hi(0.29, 0.67, 0.0, 0.36), Joint, detail: true)); // the console
            p.Add(Box(Lo(0.31, 0.06, 0.26, 0.27), Hi(0.31, 1.08, 0.3, 0.27), Cable, detail: true)); // a cable down outside the back
            p.Add(Box(Lo(0.31, 1.04, 0.2, 0.23), Hi(0.31, 1.08, 0.26, 0.23), Cable, detail: true)); // into the helmet's side
            p.Add(Box(Lo(0.235, 1.03, -0.2, 0.175), Hi(0.235, 1.347, 0.22, 0.175), Dark, detail: true)); // the helmet's side
            p.Add(Box(Lo(0.235, 1.24, -0.2, 0.239), Hi(0.235, 1.27, 0.22, 0.239), Visor, detail: true)); // its lamp band
        }
        p.Add(Box([0.31, 0.673, -0.22], [0.34, 0.682, -0.19], Lamp, detail: true)); // console lamps: the link lamp, amber
        p.Add(Box([0.31, 0.673, -0.16], [0.34, 0.682, -0.13], Green, detail: true));
        p.Add(Box([0.31, 0.673, -0.1], [0.34, 0.682, -0.07], Visor, detail: true));
        p.Add(Box([-0.34, 0.673, -0.22], [-0.31, 0.682, -0.19], Green, detail: true));
        p.Add(Box([-0.34, 0.673, -0.16], [-0.31, 0.682, -0.13], Green, detail: true));
        p.Add(Box([-0.07, 0.06, 0.343], [0.07, 1.52, 0.45], Metal, detail: true)); // the spine column
        p.Add(Box([-0.035, 0.06, 0.453], [0.035, 1.44, 0.49], Cable, detail: true)); // its cable bundle
        p.Add(Box([-0.03, 0.006, 0.455], [0.03, 0.063, 0.7], Cable, detail: true)); // the conduit along the floor
        foreach (var (y, c) in new[] { (0.7, Visor), (0.85, Green), (1.0, Visor), (1.15, Green) })
            p.Add(Box([0.045, y, 0.45], [0.062, y + 0.03, 0.455], c, detail: true)); // lamps down the spine
        p.Add(Box([-0.08, 1.444, -0.04], [0.08, 1.52, 0.343], Metal, detail: true)); // the helmet's arm
        p.Add(Box([-0.23, 1.35, -0.2], [0.23, 1.44, 0.22], Dark, detail: true)); // the helmet: crown
        p.Add(Box([-0.175, 1.06, 0.16], [0.175, 1.347, 0.22], Dark, detail: true)); // its back
        p.Add(Box([-0.175, 1.24, 0.22], [0.175, 1.27, 0.224], Visor, detail: true)); // the lamp band round its back
        p.Add(Box([-0.175, 1.28, -0.2], [0.175, 1.347, -0.14], Dark, detail: true)); // its brow
        p.Add(Box([-0.172, 1.06, -0.17], [0.172, 1.14, -0.135], Visor, detail: true)); // its visor over the eyes
        p.Add(Box([-0.12, 1.523, -0.06], [0.12, 1.54, 0.12], Joint, detail: true)); // the vent plate on the arm
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.6, 0.34], "+z");
        p.Sock("+x", [0.36, 0.2, 0], "+x");
        p.Sock("-x", [-0.36, 0.2, 0], "-x");
        p.Stats(60, 200, 3, 3);
        p.Set(("seat", new PyDict { ["at"] = Py.L(0, -0.5, 0) }), ("shell", new PyDict { ["range"] = Range }), ("config", Py.L(Channel())));
        return p;
    }

    // ---- Spare works (Karl, 2026-11-17: "think about all of the parts such as actuators, shell, processor etc. and add them as robot
    // parts which you fit with the current set of tools"): carried items that put one of a Robot Shell's worn works back to new, fitted
    // with the attach tool at a standing shell (Game/GameShellWear.cs). Made with the production system, not on the Assembler
    // (Assembling.cs skips the Shells/Spares folder; which machines make them is for TwitchTheSniper's system). Names are read by
    // ShellWear.SpareName.

    private static Part Spare(string name, string desc, double mass, Action<Part> look)
    {
        var p = new Part(name, Small, Section, desc + " Fit it to a standing Robot Shell with the attach tool: the worn work is new again.", tool: "wrench", group: "Logic", color: Joint);
        look(p);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(mass, 40, 1, 1);
        return p;
    }

    private static IEnumerable<(string, Part)> Spares()
    {
        const string Copper = "#b87333", Green = "#2f8f4e";
        yield return ("Spares", Spare("Shell Leg Actuator", "A robot shell's knee and hip drive: a motor, a gearbox and the shin's push rod.", 4.5, p =>
        {
            p.Add(Box([-0.05, 0, -0.05], [0.05, 0.36, 0.05])); // the push rod's housing
            p.Add(Box([-0.075, 0.3, -0.075], [0.075, 0.44, 0.075], Dark)); // the motor
            p.Add(Box([-0.08, 0.04, -0.06], [0.08, 0.12, 0.06], Dark, detail: true)); // the gearbox
            p.Add(Box([-0.02, 0.443, -0.02], [0.02, 0.47, 0.02], Copper, detail: true)); // its terminal
        }));
        yield return ("Spares", Spare("Shell Arm Actuator", "A robot shell's shoulder and elbow drive: a slim motor and its gear train.", 2.5, p =>
        {
            p.Add(Box([-0.04, 0, -0.04], [0.04, 0.3, 0.04]));
            p.Add(Box([-0.055, 0.24, -0.055], [0.055, 0.34, 0.055], Dark));
            p.Add(Box([-0.06, 0.03, -0.045], [0.06, 0.09, 0.045], Dark, detail: true));
            p.Add(Box([-0.015, 0.343, -0.015], [0.015, 0.365, 0.015], Copper, detail: true));
        }));
        yield return ("Spares", Spare("Shell Power Cell", "A robot shell's internal battery pack, the size of a lunch box.", 6, p =>
        {
            p.Add(Box([-0.14, 0, -0.08], [0.14, 0.2, 0.08], Dark));
            p.Add(Box([-0.12, 0.203, -0.06], [-0.07, 0.23, -0.01], Copper, detail: true)); // the terminals
            p.Add(Box([0.07, 0.203, -0.06], [0.12, 0.23, -0.01], Copper, detail: true));
            p.Add(Box([-0.1, 0.06, -0.084], [0.1, 0.1, -0.07], Lamp, detail: true)); // its charge band
        }));
        yield return ("Spares", Spare("Shell Processor", "A robot shell's brain: a sealed board in a finned case.", 1.2, p =>
        {
            p.Add(Box([-0.1, 0, -0.07], [0.1, 0.04, 0.07], Green));
            p.Add(Box([-0.06, 0.043, -0.045], [0.06, 0.07, 0.045], Dark, detail: true)); // the case
            for (var k = 0; k < 4; k++)
                p.Add(Box([-0.056 + 0.03 * k, 0.073, -0.04], [-0.042 + 0.03 * k, 0.09, 0.04], Steel, detail: true)); // fins
        }));
        yield return ("Spares", Spare("Shell Optics", "A robot shell's visor unit: the camera bar and its lenses.", 0.9, p =>
        {
            p.Add(Box([-0.11, 0, -0.04], [0.11, 0.08, 0.04], Dark));
            p.Add(Box([-0.09, 0.02, -0.044], [0.09, 0.06, -0.03], Visor, detail: true)); // the lens strip
        }));
        yield return ("Spares", Spare("Shell Plating", "A set of a robot shell's armour plates, bolted on over the old.", 8, p =>
        {
            p.Add(Box([-0.22, 0, -0.16], [0.22, 0.05, 0.16]));
            p.Add(Box([-0.2, 0.053, -0.14], [0.2, 0.09, 0.14], Steel, detail: true));
            p.Add(Box([-0.18, 0.093, -0.12], [0.18, 0.12, 0.12], Joint, detail: true));
        }));
    }


    // ---- The Shell Gantry (the user, 2026-11-18; docs/sandbox/design/mining-shell.md "Shell Gantry layout"): a square bay 5 m each
    // way, open at the front, five arms on its rear frame (drawn by GantryRender.cs from "shellGantry.arms"), and a parts table of its
    // own placed behind the rear frame in the arms' reach. Modules off a shell are whole parts that lie on the table (Constructs/Shells.json
    // names each module's "part"). The look (look.md "Machine look"): dark frame, the bay's front in hazard, orange along the bay, white
    // at the ways in.

    /// <summary>The arms' shoulders on the rear frame, in ConstructGantry.ArmNames order: left upper, left lower, right upper, right lower, head.</summary>
    public static readonly double[][] GantryArms = [[-1.4, 3.4, 2.4], [-1.1, 1.0, 2.4], [1.4, 3.4, 2.4], [1.1, 1.0, 2.4], [0, 4.6, 2.4]];

    private static Part Gantry()
    {
        const double h = 0.08, w = 2.5, post = 0.22, top = 5.36, beam = 0.2; // the deck's top; the bay's half width and depth; the frame
        var p = new Part("Shell Gantry", Small, Section, "A vertical gantry a robot shell stands in, facing out of its open front: its five arms take a module off the shell and fit another from a Shell Parts Table placed behind it. Walk a shell in and leave it, then use the gantry's panel. Needs power.",
            role: "panel", tool: "welder", group: "Logic", color: MachineLook.Base);
        // The deck, a stand mat in the middle and hazard along the open front.
        p.Add(Box([-w - post, 0, -w - post], [w + post, h, w + post], MachineLook.Base));
        p.Add(MachineLook.D([-0.45, h - 0.002, -0.45], [0.45, h + 0.006, 0.45], MachineLook.In));
        MachineLook.Hazard(p, [-w, h - 0.002, -w - post + 0.01], [w, h + 0.01, -w + 0.15]);
        // Four corner posts and the top ring of beams between them (3 mm inside the posts' faces).
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                p.Add(Box([Math.Min(sx * w, sx * (w + post)), h, Math.Min(sz * w, sz * (w + post))], [Math.Max(sx * w, sx * (w + post)), top, Math.Max(sz * w, sz * (w + post))], Dark));
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([Math.Min(sx * (w + 0.003), sx * (w + post - 0.003)), top - beam, -w + 0.003], [Math.Max(sx * (w + 0.003), sx * (w + post - 0.003)), top - 0.003, w - 0.003], Joint));
        foreach (var sz in new[] { -1.0, 1.0 })
            p.Add(Box([-w + 0.003, top - beam, Math.Min(sz * (w + 0.003), sz * (w + post - 0.003))], [w - 0.003, top - 0.003, Math.Max(sz * (w + 0.003), sz * (w + post - 0.003))], Joint));
        p.Add(MachineLook.D([-w, top - beam + 0.04, -w - post - 0.008], [w, top - 0.04, -w - post + 0.004], MachineLook.Out)); // (orange across the front beam: the bay)
        // The rear frame: two uprights and three rails, where the arms' shoulders sit (the lower arms at 1 m, the upper at 3.4 m, the head's at 4.6 m).
        foreach (var x in new[] { -0.75, 0.6 })
            p.Add(Box([x, h + 0.003, w + 0.03], [x + 0.15, top - beam - 0.003, w + post - 0.03], Joint));
        foreach (var (y0, y1) in new[] { (0.93, 1.07), (3.32, 3.48), (4.55, 4.68) })
            p.Add(Box([-w + 0.003, y0, w + 0.02], [w - 0.003, y1, w + post - 0.02], Dark));
        foreach (var a in GantryArms)
            p.Add(MachineLook.D([a[0] - 0.2, a[1] - 0.2, w + post - 0.015], [a[0] + 0.2, a[1] + 0.2, w + post + 0.006], MachineLook.Accent)); // (a mount plate behind each shoulder)
        // The control console outside the front right post, its screen towards the front.
        double cx0 = w + post + 0.05, cx1 = cx0 + 0.55;
        p.Add(Box([cx0, h, -w - post], [cx1, 1.1, -w + 0.25], MachineLook.Base));
        p.Add(Box([cx0 + 0.03, 1.1, -w - post + 0.03], [cx1 - 0.03, 1.5, -w - post + 0.15], Dark));
        p.Add(MachineLook.D([cx0 + 0.06, 1.14, -w - post + 0.018], [cx1 - 0.06, 1.46, -w - post + 0.03], Visor));
        p.Add(MachineLook.D([cx0 + 0.08, 1.1, -w - post + 0.2], [cx0 + 0.2, 1.12, -w - post + 0.32], MachineLook.Accent));
        p.Add(MachineLook.D([cx1 - 0.2, 1.1, -w - post + 0.2], [cx1 - 0.08, 1.12, -w - post + 0.32], MachineLook.Stop));
        p.Add(MachineLook.D([cx0 + 0.04, h + 0.02, -w - post - 0.008], [cx1 - 0.04, h + 0.14, -w - post + 0.004], MachineLook.In)); // (its kick plate: the way to it)
        p.Sock("bottom", [0, 0, 0], "-y");
        foreach (var (n, x, z) in new[] { ("bottom fl", -2.0, -2.0), ("bottom fr", 2.0, -2.0), ("bottom bl", -2.0, 2.0), ("bottom br", 2.0, 2.0) })
            p.Sock(n, [x, 0, z], "-y");
        p.Stats(2200, 2400, 6, seconds: 15.0);
        p.Set(("power", 120), ("onBreak", "disable"),
            ("shellGantry", new PyDict
            {
                ["bay"] = Py.L(Py.L(-w, h, -w), Py.L(w, h + 5, w)),
                ["arms"] = Py.L(GantryArms.Select(a => (object?)Py.L(a[0], a[1], a[2])).ToArray()),
                ["reach"] = 4.0,
            }));
        return p;
    }

    /// <summary>The Shell Parts Table: a steel table that holds whole modules for a Shell Gantry (a Piece Rack by its storage.pieces), with a Piece in at its -z end.</summary>
    private static Part Table()
    {
        var p = new Part("Shell Parts Table", Small, Section, "Holds robot shell modules for a Shell Gantry: place it behind the gantry's rear frame, on the same base, within its arms' reach. Put modules on it by hand or by conveyor; F takes one off.",
            role: "storage", tool: "welder", group: "Logic", color: MachineLook.Base);
        const double x = 0.6, z = 1.2, y = 0.86, t = 0.06;
        foreach (var sx in new[] { -1.0, 1.0 })
            foreach (var sz in new[] { -1.0, 1.0 })
                p.Add(Box([Math.Min(sx * (x - 0.02), sx * (x - 0.12)), 0, Math.Min(sz * (z - 0.02), sz * (z - 0.12))], [Math.Max(sx * (x - 0.02), sx * (x - 0.12)), y + 0.003, Math.Max(sz * (z - 0.02), sz * (z - 0.12))], Dark));
        p.Add(Box([-x, y, -z], [x, y + t, z], MachineLook.Base));
        p.Add(MachineLook.D([-x + 0.04, y + t - 0.002, -z + 0.04], [x - 0.04, y + t + 0.006, z - 0.04], MachineLook.Steel, "steel"));
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(MachineLook.D([Math.Min(sx * (x - 0.004), sx * (x - 0.034)), y + t - 0.004, -z + 0.004], [Math.Max(sx * (x - 0.004), sx * (x - 0.034)), y + t + 0.04, z - 0.004], MachineLook.Accent));
        for (var k = 1; k < 4; k++)
            p.Add(MachineLook.D([-x + 0.06, y + t + 0.004, -z + k * 0.6 - 0.012], [x - 0.06, y + t + 0.01, -z + k * 0.6 + 0.012], MachineLook.Ink)); // (slot marks across the top)
        p.Add(MachineLook.D([-0.4, y + 0.004, -z - 0.008], [0.4, y + t - 0.004, -z + 0.004], MachineLook.In)); // (the way in at the -z end)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(120, 500, 3);
        p.Set(("shellGantry", new PyDict { ["table"] = true }),
            ("storage", Kw(("pieces", Kw(("section", Py.L(1.0, 0.95)), ("length", 2.2), ("count", 60),
                ("hold", Py.L(Py.L(-0.55, Py.Round(y + t + 0.012, 3), -1.15), Py.L(0.55, 1.9, 1.15))))))),
            ("ports", Py.L(Kw(("name", "Piece in"), ("type", "conveyor"), ("dir", "in"), ("at", Py.L(0, Py.Round(y + t * 0.5, 3), -z)), ("face", "-z"), ("opening", Py.L(1.0, 0.9))))));
        return p;
    }

    /// <summary>A robot shell module as a part (Constructs/Shells.json "part"): what a Shell Gantry takes off and fits, made on the Assembler.</summary>
    private static Part Module(string name, string desc, double mass, Action<Part> look)
    {
        var p = new Part(name, Small, Section, desc + " A Shell Gantry fits it to a robot shell from its parts table.", tool: "wrench", group: "Logic", color: Steel);
        look(p);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(mass, 60, 2, 2);
        return p;
    }

    private static void Leg(Part p, double k, bool runner)
    {
        PyDict? B(double[] lo, double[] hi, string? c = null, bool detail = false) => Box([lo[0] * k, lo[1] * k, lo[2] * k], [hi[0] * k, hi[1] * k, hi[2] * k], c, detail: detail);
        p.Add(B([-0.075, 0.0, -0.15], [0.075, 0.07, 0.09], Dark)); // foot
        p.Add(B([-0.065, 0.06, -0.07], [0.065, 0.47, 0.07], Joint)); // shin
        p.Add(B([-0.07, 0.45, -0.085], [0.07, 0.53, 0.085], Dark, detail: true)); // knee
        p.Add(B([-0.06, 0.52, -0.075], [0.06, 0.88, 0.075])); // thigh
        p.Add(B([-0.045, 0.86, -0.045], [0.045, 0.94, 0.045], Dark, detail: true)); // the hip's coupling
        if (runner)
            p.Add(B([-0.02, 0.1, 0.073], [0.02, 0.44, 0.1], Lamp, detail: true)); // a spring strut behind the shin
        if (k > 1)
            p.Add(B([-0.07, 0.15, -0.088], [0.07, 0.4, -0.072], Joint, detail: true)); // a shin plate
    }

    private static void Head(Part p, bool plated)
    {
        p.Add(Box([-0.06, 0, -0.06], [0.06, 0.1, 0.06], Dark)); // neck
        p.Add(Box([-0.135, 0.08, -0.14], [0.135, 0.35, 0.125])); // head
        p.Add(Box([-0.11, 0.17, -0.158], [0.11, 0.25, -0.12], Visor, detail: true)); // visor
        p.Add(Box([0.07, 0.34, -0.015], [0.09, 0.46, 0.015], Dark, detail: true)); // aerial
        if (!plated)
            return;
        p.Add(Box([-0.15, 0.27, -0.16], [0.15, 0.37, 0.142], Joint, detail: true)); // a brow plate over the crown
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([Math.Min(sx * 0.137, sx * 0.157), 0.1, -0.12], [Math.Max(sx * 0.137, sx * 0.157), 0.3, 0.11], Joint, detail: true)); // cheek plates
    }

    private static IEnumerable<(string, Part)> Modules()
    {
        yield return ("Modules", Module("Shell Optics Head", "A robot shell's standard head: the visor, its cameras and an aerial.", 6, p => Head(p, plated: false)));
        yield return ("Modules", Module("Shell Plated Head", "A robot shell's head under armour plates: the shell takes a tenth less from hits.", 11, p => Head(p, plated: true)));
        yield return ("Modules", Module("Shell Power Pack", "A Robot Shell's power pack, worn on its back.", 9, p =>
        {
            p.Add(Box([-0.16, 0, -0.045], [0.16, 0.37, 0.045], Dark));
            p.Add(Box([-0.12, 0.29, -0.049], [0.12, 0.32, -0.04], Lamp, detail: true));
        }));
        yield return ("Modules", Module("Shell Hand", "A robot shell's forearm and hand, which hold its tools.", 4, p =>
        {
            p.Add(Box([-0.05, 0, -0.05], [0.05, 0.11, 0.05], Dark)); // hand
            p.Add(Box([-0.055, 0.1, -0.055], [0.055, 0.37, 0.055], Joint)); // forearm
            p.Add(Box([-0.065, 0.35, -0.065], [0.065, 0.4, 0.065], Dark, detail: true)); // the elbow's coupling
        }));
        yield return ("Modules", Module("Shell Standard Leg", "A Robot Shell's leg: hip to foot.", 8, p => Leg(p, 1, runner: false)));
        yield return ("Modules", Module("Shell Heavy Leg", "A Mining Shell's leg, half again the size of a standard one.", 24, p => Leg(p, 1.5, runner: false)));
        yield return ("Modules", Module("Shell Runner Leg", "A sprung robot shell leg: a pair of them make the shell a tenth faster.", 7, p => Leg(p, 1, runner: true)));
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", Shell()), ("", MiningShell()), ("", Chair()), .. Spares(), ("Gantry", Gantry()), ("Gantry", Table()), .. Modules()]);
    }
}
