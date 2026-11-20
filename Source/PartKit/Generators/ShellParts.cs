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
    private const string Light = "#c4c9ce"; // the modules' second, lighter plating tone (look.md "Rules of thumb": not all dark grey)
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
    /// The Half Shell (docs/sandbox/design/half-shell.md): the Robot Shell at half a person's size, 0.9 m standing, for crawl spaces,
    /// ducts and the space under low decks. Frame "Half" in Constructs/Shells.json (scale 0.5): its modules are the Robot Shell's.
    /// </summary>
    private static Part HalfShell()
    {
        var p = new Part("Half Shell", Small, Section, $"A small robot body half a person's size: it walks under low decks and through ducts and small hatches, lifts half what a person lifts and is too small for seats. Wake in it from a Shell Control Chair on its channel, within {Py.G(Range / 1000.0)} km. It docks in a Tiny Shell Pod.", tool: "wrench", group: "Logic", color: Steel);
        Robot(p, 0.5, drill: false);
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.625, 0.1], "+z");
        p.Stats(12, 80, 2, 2);
        p.Set(("shell", new PyDict { ["body"] = true }), ("config", Py.L(Channel())), ("centreSockets", false));
        return p;
    }

    /// <summary>
    /// The robot's body at <paramref name="k"/> times a person's size (the Robot Shell is 1; the Mining Shell, Constructs/Shells.json's
    /// "scale", 1.5). <paramref name="drill"/>: the right arm's forearm and hand are left off for the drill arm.
    /// </summary>
    private static void Robot(Part p, double k, bool drill, bool forearms = true)
    {
        PyDict? B(double[] lo, double[] hi, string? c = null, bool detail = false) => Box([lo[0] * k, lo[1] * k, lo[2] * k], [hi[0] * k, hi[1] * k, hi[2] * k], c, detail: detail);
        foreach (var s in new[] { -1, 1 })
        {
            double x0 = Math.Min(s * 0.045, s * 0.175), x1 = Math.Max(s * 0.045, s * 0.175);
            p.Add(B([x0 - 0.01, 0.0, -0.15], [x1 + 0.01, 0.07, 0.09], Dark)); // feet
            p.Add(B([x0, 0.06, -0.07], [x1, 0.47, 0.07], Joint)); // shins
            p.Add(B([x0 - 0.005, 0.45, -0.085], [x1 + 0.005, 0.53, 0.085], Dark, detail: true)); // knees
            p.Add(B([x0 + 0.005, 0.52, -0.075], [x1 - 0.005, 0.88, 0.075], drill ? Light : null)); // thighs (the Mining Shell's in the lighter plate tone: look review 2026-11-19, "still mostly dark grey")
            double a0 = Math.Min(s * 0.26, s * 0.37), a1 = Math.Max(s * 0.26, s * 0.37);
            p.Add(B([a0 - 0.02, 1.36, -0.1], [a1 + 0.02, 1.53, 0.1], Joint)); // shoulders
            p.Add(B([a0, 1.0, -0.06], [a1, 1.37, 0.06], drill ? Light : null)); // upper arms
            if (drill && s > 0 || !forearms)
                continue; // (the right forearm is the drill: MiningShell; the Construction Shell's are its tool arms)
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
        p.Add(B([-0.135, 1.56, -0.14], [0.135, 1.83, 0.125], drill ? Light : null)); // head
        p.Add(B([-0.11, 1.65, -0.158], [0.11, 1.73, -0.12], Visor)); // visor
        p.Add(B([0.07, 1.82, -0.015], [0.09, 1.96, 0.015], Dark, detail: true)); // aerial
    }

    /// <summary>The hopper's bulk box (the part's storage): inside its walls, open at the top. Also where the game draws the ore.</summary>
    public static readonly double[] HopperMin = [-0.31, 1.395, 0.245], HopperMax = [0.31, 2.18, 0.755];

    /// <summary>
    /// The drill arm's bit (Mining Shell metres; its axis at x 0.4725, y 1.38, forward -z): a collar ('a', the shell's accent on the worn
    /// rig: ShellKitNodes.DrillArm takes these same boxes), a cone stepping down in four square sections ('b') with a spiral of flutes
    /// round it ('d'), and the point in hazard ('h') (the reviewer, 2026-11-19: "the drill reads as a yellow paddle").
    /// </summary>
    public static readonly (double[] Lo, double[] Hi, char Paint, bool Detail)[] DrillBit = MakeBit();

    private static (double[] Lo, double[] Hi, char Paint, bool Detail)[] MakeBit()
    {
        const double ax = 0.4725, ay = 1.38;
        (double[], double[], char, bool) Sq(double h, double z0, double z1, char c, bool d = false) => ([ax - h, ay - h, z0], [ax + h, ay + h, z1], c, d);
        (double[], double[], char, bool) Fl(double x0, double x1, double y0, double y1, double z0, double z1) => ([ax + x0, ay + y0, z0], [ax + x1, ay + y1, z1], 'd', true);
        return
        [
            Sq(0.088, -0.45, -0.39, 'a'), // the collar
            Sq(0.079, -0.474, -0.454, 'h', true), // a hazard collar where the cone leaves it (look review 2026-11-19: mark what bites)
            Sq(0.07, -0.57, -0.445, 'b'), Sq(0.054, -0.69, -0.565, 'b'), Sq(0.038, -0.8, -0.685, 'b'), Sq(0.024, -0.885, -0.795, 'b'), // the cone
            Sq(0.011, -0.935, -0.88, 'h', true), // the point
            Fl(-0.02, 0.02, -0.082, -0.066, -0.51, -0.455), Fl(0.066, 0.082, -0.02, 0.02, -0.555, -0.5), // the flutes, turning round it
            Fl(-0.02, 0.02, 0.05, 0.066, -0.66, -0.6), Fl(-0.05, -0.034, -0.016, 0.016, -0.77, -0.72), Fl(-0.012, 0.012, -0.036, -0.022, -0.86, -0.82),
        ];
    }

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
        p.Add(Box([-0.36, 1.393, 0.2], [0.36, 2.25, 0.24], Joint)); // front wall, against the back
        p.Add(Box([-0.36, 1.393, 0.76], [0.36, 2.25, 0.8], Light)); // back wall (light plate, the ribs on it steel: the ore inside is what is brown)
        foreach (var x in new[] { -0.3, 0.26 })
            p.Add(Box([x, 1.42, 0.797], [x + 0.04, 2.2, 0.826], Steel, detail: true)); // ribs up its back
        p.Add(Box([-0.358, 1.393, 0.243], [-0.322, 2.2, 0.757], Light)); // side walls (ShellKitNodes' hopper takes these colours)
        p.Add(Box([0.322, 1.393, 0.243], [0.358, 2.2, 0.757], Light));
        p.Add(Box([-0.364, 2.253, 0.196], [0.364, 2.28, 0.245], Hazard, detail: true)); // the front wall's lip
        p.Add(Box([-0.364, 2.253, 0.755], [0.364, 2.28, 0.804], Hazard, detail: true)); // the back wall's lip
        foreach (var x in new[] { -0.25, 0.17 })
            p.Add(Box([x, 2.0, -0.02], [x + 0.08, 2.32, 0.197], Joint, detail: true)); // straps over the shoulders
        p.Add(Box([-0.2, 1.55, 0.805], [0.2, 1.64, 0.83], Lamp, detail: true)); // a load band on its back
        // The drill arm, out of the right shoulder: a housing down the forearm, a motor collar, a stepped bit forward.
        p.Add(Box([0.4, 1.26, -0.42], [0.545, 1.497, 0.08], Joint)); // the housing
        p.Add(Box([0.385, 1.24, -0.12], [0.56, 1.52, 0.03], Dark)); // the motor collar
        foreach (var (lo, hi, c, detail) in DrillBit)
            p.Add(c == 'h' ? MachineLook.D(lo, hi, Hazard, "hazard paint") : Box(lo, hi, c switch { 'a' => Lamp, 'd' => Dark, _ => Bit }, detail: detail)); // the bit: a collar, a hazard collar, a stepped cone with spiral flutes, a hazard point
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

    private const string DeckPlate = "#7a8188";

    private static Part Gantry()
    {
        const double h = 0.08, w = 2.5, post = 0.22, top = 5.36, beam = 0.2; // the deck's top; the bay's half width and depth; the frame
        var p = new Part("Shell Gantry", Small, Section, "A vertical gantry a robot shell stands in, facing out of its open front: its five arms take a module off the shell and fit another from a Shell Parts Table placed behind it. Walk a shell in and leave it, then use the gantry's panel. Needs power.",
            role: "panel", tool: "welder", group: "Logic", color: MachineLook.Base);
        // The deck, a stand mat in the middle and hazard along the open front.
        if (Box([-w - post, 0, -w - post], [w + post, h, w + post], DeckPlate) is { } deck)
        {
            deck["finish"] = "deck plate"; // (light deck plate, so the dark frame and the shell stand off the floor: look review 2026-11-19)
            p.Add(deck);
        }
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
        var p = new Part("Shell Parts Table", Small, Section, "Holds robot shell modules for a Shell Gantry: place it behind the gantry's rear frame, within its arms' reach. Put modules on it by hand or by conveyor; F takes one off.",
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

    // A worn shell draws each module from these boxes (Scene/Rigs/ShellModuleNodes.cs), so each look is told apart at a glance (the user,
    // 2026-11-19: "more visual variance between them"): a leg by its "joints" in Constructs/Shells.json (the heights its rig bones take
    // its boxes at; keep a box's middle in its bone's band), a head on the person's head, an arm's elbow coupling at its top and its
    // tool at its foot. Boxes that meet overlap by 3 mm or more, never on one plane (z-fighting.md).

    /// <summary>The Standard Leg: a person's leg in steel. Joints: hip 0.9, knee 0.49, ankle 0.09.</summary>
    private static void StandardLeg(Part p)
    {
        p.Add(Box([-0.07, 0.0, -0.15], [0.07, 0.05, 0.08], Dark)); // sole
        p.Add(Box([-0.06, 0.047, -0.13], [0.06, 0.095, 0.06], Joint)); // the foot's top
        p.Add(Box([-0.045, 0.07, -0.04], [0.045, 0.13, 0.04], Dark, detail: true)); // ankle
        p.Add(Box([-0.055, 0.11, -0.06], [0.055, 0.47, 0.06], Joint)); // shin
        p.Add(Box([-0.065, 0.44, -0.08], [0.065, 0.54, 0.075], Dark, detail: true)); // knee
        p.Add(Box([-0.05, 0.46, -0.092], [0.05, 0.53, -0.078], Light, detail: true)); // knee cap
        p.Add(Box([-0.045, 0.58, -0.078], [0.045, 0.84, -0.067], Light, detail: true)); // a plate down the thigh's front
        p.Add(Box([-0.062, 0.52, -0.07], [0.062, 0.88, 0.07])); // thigh
        p.Add(Box([-0.045, 0.86, -0.045], [0.045, 0.94, 0.045], Dark, detail: true)); // the hip's coupling
    }

    /// <summary>
    /// The Heavy Leg (built at the Mining Shell's 1.5x): thick and armoured, about half again a Standard Leg's width on the rig, a wide flat
    /// foot, plates over the shin, plates standing out at the knee and thigh, and a ram behind the shin in hazard paint (look.md "mark
    /// what bites"). Joints: hip 1.35, knee 0.735, ankle 0.15.
    /// </summary>
    private static void HeavyLeg(Part p)
    {
        const string Hazard = "#e0a020";
        p.Add(Box([-0.21, 0.0, -0.3], [0.21, 0.06, 0.19], Dark)); // the wide sole
        p.Add(Box([-0.18, 0.057, -0.24], [0.18, 0.14, 0.15], Joint)); // the foot's top
        p.Add(Box([-0.16, 0.04, -0.31], [0.16, 0.12, -0.235], Steel, detail: true)); // the toe plate
        p.Add(Box([-0.13, 0.12, -0.12], [0.13, 0.22, 0.12], Dark)); // ankle block
        p.Add(Box([-0.13, 0.2, -0.11], [0.13, 0.72, 0.11], Joint)); // shin core
        p.Add(Box([-0.16, 0.24, -0.15], [0.16, 0.67, -0.105], Light)); // the shin plate
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([Math.Min(sx * 0.127, sx * 0.158), 0.27, -0.095], [Math.Max(sx * 0.127, sx * 0.158), 0.64, 0.095], Steel, detail: true)); // side plates
        p.Add(Box([-0.04, 0.26, 0.105], [0.04, 0.66, 0.165], Hazard)); // the ram behind the shin, in hazard stripes (dark bands round the yellow)
        for (var k = 0; k < 4; k++)
            p.Add(Box([-0.043, 0.3 + 0.09 * k, 0.102], [0.043, 0.34 + 0.09 * k, 0.168], Dark, detail: true));
        p.Add(Box([-0.15, 0.69, -0.14], [0.15, 0.8, 0.135], Dark)); // knee
        p.Add(Box([-0.12, 0.65, -0.19], [0.12, 0.84, -0.137], Light, detail: true)); // the knee guard
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([Math.Min(sx * 0.147, sx * 0.205), 0.66, -0.1], [Math.Max(sx * 0.147, sx * 0.205), 0.83, 0.1], Joint)); // knee plates standing out
        p.Add(Box([-0.17, 0.78, -0.14], [0.17, 1.3, 0.14])); // thigh
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([Math.Min(sx * 0.167, sx * 0.2), 0.86, -0.12], [Math.Max(sx * 0.167, sx * 0.2), 1.24, 0.12], Joint, detail: true)); // thigh plates
        p.Add(Box([-0.1, 1.28, -0.1], [0.1, 1.41, 0.1], Dark, detail: true)); // the hip's coupling
    }

    /// <summary>
    /// The Runner Leg: slim and digitigrade (the rig walks it with its hock well up and behind the toes, Shells.json "hock"): a short
    /// thigh with a spring, a short shin, the hock joint halfway down, a long metatarsal with amber springs and coil rings along it, and
    /// three clawed toes it stands on. Standing on its table it is straight, joint over joint. Joints: hip 0.94, knee 0.74, hock 0.5, ankle 0.09.
    /// </summary>
    private static void RunnerLeg(Part p)
    {
        // The toes: pads on the ground at the front only, each toe rising back to the ball, so light shows under the heel.
        p.Add(Box([-0.024, 0.0, -0.215], [0.024, 0.035, -0.11], Dark)); // the middle toe's pad
        p.Add(Box([-0.02, 0.03, -0.135], [0.02, 0.075, -0.02], Dark)); // the middle toe, rising to the ball
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            p.Add(Box([Math.Min(sx * 0.035, sx * 0.075), 0.0, -0.17], [Math.Max(sx * 0.035, sx * 0.075), 0.03, -0.09], Dark)); // a side toe's pad
            p.Add(Box([Math.Min(sx * 0.03, sx * 0.065), 0.026, -0.11], [Math.Max(sx * 0.03, sx * 0.065), 0.065, -0.02], Dark)); // the side toe, rising
            p.Add(Box([Math.Min(sx * 0.04, sx * 0.07), 0.008, -0.192], [Math.Max(sx * 0.04, sx * 0.07), 0.026, -0.165], Steel, detail: true)); // its claw
        }
        p.Add(Box([-0.02, 0.01, -0.238], [0.02, 0.03, -0.21], Steel, detail: true)); // the middle claw
        p.Add(Box([-0.04, 0.06, -0.04], [0.04, 0.12, 0.04], Joint)); // the ball joint, clear of the ground
        p.Add(Box([-0.03, 0.08, -0.03], [0.03, 0.49, 0.03])); // the long metatarsal
        foreach (var sz in new[] { -1.0, 1.0 })
            p.Add(Box([-0.012, 0.125, Math.Min(sz * 0.028, sz * 0.048)], [0.012, 0.45, Math.Max(sz * 0.028, sz * 0.048)], Lamp, detail: true)); // springs along it, front and back
        foreach (var y in new[] { 0.16, 0.28, 0.4 })
            p.Add(Box([-0.036, y, -0.036], [0.036, y + 0.02, 0.036], Dark, detail: true)); // coil rings
        p.Add(Box([-0.045, 0.47, -0.045], [0.045, 0.53, 0.045], Dark)); // the hock (its middle on the hock's height: it turns with the shin)
        p.Add(Box([-0.035, 0.52, -0.04], [0.035, 0.74, 0.04], Joint)); // the short shin
        p.Add(Box([-0.05, 0.72, -0.055], [0.05, 0.78, 0.055], Dark, detail: true)); // knee
        p.Add(Box([-0.055, 0.76, -0.06], [0.055, 0.92, 0.06], Light)); // the short thigh
        p.Add(Box([-0.014, 0.78, -0.085], [0.014, 0.9, -0.058], Lamp, detail: true)); // its spring, in front
        p.Add(Box([-0.04, 0.9, -0.04], [0.04, 0.97, 0.04], Dark, detail: true)); // the hip's coupling
    }

    /// <summary>
    /// The Optics Head: a narrow camera head told from the Plated Head from any side: a big sensor dome on top ringed in cyan, three lens
    /// barrels standing out past the face, camera pods on both sides, an aerial and a vent block at the back.
    /// </summary>
    private static void OpticsHead(Part p)
    {
        p.Add(Box([-0.055, 0, -0.055], [0.055, 0.1, 0.055], Dark)); // neck
        p.Add(Box([-0.105, 0.08, -0.12], [0.105, 0.27, 0.12])); // head
        p.Add(Box([-0.095, 0.11, -0.133], [0.095, 0.255, -0.117], Dark, detail: true)); // the face plate
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            p.Add(Box([Math.Min(sx * 0.03, sx * 0.09), 0.17, -0.215], [Math.Max(sx * 0.03, sx * 0.09), 0.23, -0.125], Joint)); // an eye's lens barrel
            p.Add(Box([Math.Min(sx * 0.038, sx * 0.082), 0.178, -0.221], [Math.Max(sx * 0.038, sx * 0.082), 0.222, -0.209], Visor, detail: true)); // its lens
        }
        p.Add(Box([-0.022, 0.115, -0.25], [0.022, 0.155, -0.125], Joint)); // the long middle barrel, under the eyes
        p.Add(Box([-0.016, 0.12, -0.256], [0.016, 0.15, -0.244], Visor, detail: true)); // its lens
        p.Add(Box([0.1, 0.16, -0.235], [0.155, 0.215, 0.05], Joint)); // a long side camera, standing out past the head's right side
        p.Add(Box([0.108, 0.168, -0.241], [0.147, 0.207, -0.229], Visor, detail: true)); // its lens
        // The dome: stepped up from the head's top in four rounds, ringed in cyan, so it reads as a dome from any side.
        p.Add(Box([-0.1, 0.265, -0.11], [0.1, 0.305, 0.11], Light)); // its base
        p.Add(Box([-0.103, 0.282, -0.113], [0.103, 0.296, 0.113], Visor, detail: true)); // the cyan ring round it
        p.Add(Box([-0.082, 0.3, -0.09], [0.082, 0.34, 0.09], Light));
        p.Add(Box([-0.058, 0.336, -0.064], [0.058, 0.37, 0.064], Light));
        p.Add(Box([-0.03, 0.366, -0.034], [0.03, 0.39, 0.034], Dark, detail: true)); // its sensor cap
        p.Add(Box([-0.08, 0.29, 0.07], [-0.062, 0.5, 0.088], Dark, detail: true)); // aerial, at the back left
        p.Add(Box([-0.084, 0.48, 0.066], [-0.058, 0.512, 0.092], Visor, detail: true)); // its tip lamp
        p.Add(Box([-0.07, 0.12, 0.115], [0.07, 0.24, 0.14], Dark, detail: true)); // the vent block at the back
    }

    /// <summary>The Plated Head: a wide armoured helm, a slit visor under a heavy brow, a jaw guard, cheek plates and a crest.</summary>
    private static void PlatedHead(Part p)
    {
        p.Add(Box([-0.065, 0, -0.065], [0.065, 0.1, 0.065], Dark)); // neck
        p.Add(Box([-0.15, 0.07, -0.15], [0.15, 0.33, 0.14], Joint)); // the helm
        p.Add(Box([-0.165, 0.27, -0.172], [0.165, 0.345, 0.155], Light)); // the brow plate over the crown
        p.Add(Box([-0.12, 0.195, -0.158], [0.12, 0.222, -0.146], Visor, detail: true)); // the slit visor
        p.Add(Box([-0.13, 0.075, -0.175], [0.13, 0.185, -0.12])); // the jaw guard
        foreach (var sx in new[] { -1.0, 1.0 })
            p.Add(Box([Math.Min(sx * 0.147, sx * 0.172), 0.09, -0.135], [Math.Max(sx * 0.147, sx * 0.172), 0.268, 0.125], Steel, detail: true)); // cheek plates
        p.Add(Box([-0.03, 0.342, -0.16], [0.03, 0.385, 0.14], Dark, detail: true)); // the crest
        p.Add(Box([-0.1, 0.1, -0.18], [-0.04, 0.12, -0.172], Dark, detail: true)); // vent slots on the jaw guard
        p.Add(Box([0.04, 0.1, -0.18], [0.1, 0.12, -0.172], Dark, detail: true));
    }

    /// <summary>
    /// An arm module's elbow coupling at its top (the rig turns it about there): 0.4 m up, in the arm's own colour <paramref name="own"/>,
    /// a steel pivot boss on each side with its bolt. Its first box: the rig reads the arm's colour from it for the shoulder
    /// (ShellModuleNodes.OwnColour; look review 2026-11-19: a dark cap with a slot on every arm copied the head's visor).
    /// </summary>
    private static void Elbow(Part p, double w, string own)
    {
        p.Add(Box([-w, 0.36, -w], [w, 0.42, w], own)); // the elbow's coupling
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            double X(double v) => sx * v;
            p.Add(Box([Math.Min(X(w - 0.004), X(w + 0.012)), 0.369, -0.03], [Math.Max(X(w - 0.004), X(w + 0.012)), 0.411, 0.03], Steel, detail: true)); // the pivot boss: two crossed plates, near round
            p.Add(Box([Math.Min(X(w - 0.004), X(w + 0.013)), 0.375, -0.036], [Math.Max(X(w - 0.004), X(w + 0.013)), 0.405, 0.036], Steel, detail: true));
            p.Add(Box([Math.Min(X(w + 0.008), X(w + 0.024)), 0.382, -0.008], [Math.Max(X(w + 0.008), X(w + 0.024)), 0.398, 0.008], Dark, detail: true)); // its bolt
        }
    }

    private const string ArmHazard = "#e0a020", ArmOrange = "#e07020", ArmCopper = "#b87333", ArmGlow = "#7fe0ff", ArmField = "#2f8fa0";

    /// <summary>The Shell Welder Arm's look (module metres: its tool at y 0, its elbow coupling at the top, 0.42 m).</summary>
    private static void WelderArm(Part p)
    {
        Elbow(p, 0.06, ArmOrange);
        p.Add(Box([-0.055, 0.2, -0.055], [0.055, 0.365, 0.055], ArmOrange)); // the housing, the welder's orange
        p.Add(Box([-0.058, 0.26, -0.058], [0.058, 0.285, 0.058], ArmHazard, detail: true)); // a hazard band
        p.Add(Box([0.052, 0.21, -0.03], [0.095, 0.35, 0.03], Joint, detail: true)); // the gas bottle along its side
        p.Add(Box([0.062, 0.347, -0.018], [0.088, 0.37, 0.018], ArmCopper, detail: true)); // its valve
        p.Add(Box([-0.025, 0.08, -0.025], [0.025, 0.205, 0.025], Dark)); // the torch's neck
        p.Add(Box([-0.045, 0.03, -0.045], [0.045, 0.085, 0.045], Steel)); // the torch cup
        p.Add(Box([-0.05, 0.025, -0.05], [0.05, 0.04, 0.05], ArmCopper, detail: true)); // its rim
        p.Add(Box([-0.012, 0.0, -0.012], [0.012, 0.033, 0.012], ArmCopper)); // the nozzle
    }

    /// <summary>The Shell Grip Arm's look (module metres, as <see cref="WelderArm"/>).</summary>
    private static void GripArm(Part p)
    {
        Elbow(p, 0.06, ArmField);
        p.Add(Box([-0.055, 0.15, -0.055], [0.055, 0.365, 0.055], Joint)); // the forearm
        p.Add(Box([-0.08, 0.1, -0.08], [0.08, 0.155, 0.08], Dark)); // the claw ring
        p.Add(Box([-0.035, 0.07, -0.035], [0.035, 0.105, 0.035], ArmGlow)); // the emitter
        p.Add(Box([-0.015, 0.0, -0.085], [0.015, 0.105, -0.06], Steel)); // the claws: front, and one each side behind
        p.Add(Box([-0.085, 0.0, 0.03], [-0.06, 0.105, 0.06], Steel));
        p.Add(Box([0.06, 0.0, 0.03], [0.085, 0.105, 0.06], Steel));
        p.Add(Box([-0.058, 0.25, -0.058], [0.058, 0.27, 0.058], ArmGlow, detail: true)); // a field band
    }

    /// <summary>
    /// An arm module's look set onto a standing shell's body (the Construction Shell's fixed arms): its boxes times <paramref name="k"/>,
    /// moved so its tool end hangs at the robot's hand height (0.64 m a person's) at <paramref name="x"/> (a person's metres).
    /// </summary>
    private static void ArmOnBody(Part p, Action<Part> look, double k, double x)
    {
        var t = new Part("arm", Small, Section, "");
        look(t);
        foreach (var b in t.Boxes)
        {
            var mn = ((List<object?>)b["min"]!).Select(Py.D).ToArray();
            var mx = ((List<object?>)b["max"]!).Select(Py.D).ToArray();
            double[] lo = [(mn[0] + x) * k, (mn[1] + 0.62) * k, mn[2] * k], hi = [(mx[0] + x) * k, (mx[1] + 0.62) * k, mx[2] * k];
            p.Add(Box(lo, hi, b.Get("color") as string, detail: Py.Truthy(b.Get("detail"))));
        }
    }

    /// <summary>
    /// The Construction Shell (docs/sandbox/design/construction-shell.md): the robot at 2.8x, 5.04 m standing, for pieces too big for the
    /// survival physics gun (manipulators.md). Frame "Construction" in Constructs/Shells.json: a fixed Grip Arm (right) and Welder Arm
    /// (left), Heavy Legs. Its standing part shows both arms in place of forearms and hands.
    /// </summary>
    private static Part ConstructionShell()
    {
        const double K = 2.8;
        var p = new Part("Construction Shell", Small, Section, $"A 5 m robot body for big pieces: a grip arm lifts up to 15 t and a welder arm welds what it holds onto a build. Wake in it from a Shell Control Chair on its channel, within {Py.G(Range / 1000.0)} km. Too big for doors, seats and pods: it works outside and in hangars.", tool: "wrench", group: "Logic", color: Steel);
        Robot(p, K, drill: false, forearms: false);
        ArmOnBody(p, GripArm, K, 0.315); // (the right arm: +x, as the Mining Shell's drill)
        ArmOnBody(p, WelderArm, K, -0.315);
        foreach (var x in new[] { -0.235, 0.18 })
            p.Add(Box([x * K, 1.2 * K, -0.142 * K], [(x + 0.055) * K, 1.235 * K, -0.13 * K], ArmHazard, detail: true)); // hazard flashes on the torso's front, either side of its chest plate
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, Py.Round(1.25 * K, 3), Py.Round(0.2 * K, 3)], "+z");
        p.Stats(900, 600, 8, 10);
        p.Set(("shell", new PyDict { ["body"] = true }), ("config", Py.L(Channel())), ("centreSockets", false));
        return p;
    }

    /// <summary>The tool arms (the user, 2026-11-19: "add a few more arms for the different tools in the game"): each is its tool while worn.</summary>
    private static IEnumerable<(string, Part)> ToolArms()
    {
        const string Hazard = ArmHazard, Blade = "#c9ced4", Red = "#c03030";
        const string Paint = "#3a7fd0", NozzleCup = "#98a1a9"; // (the Grip Arm's teal is ArmField; the Sprayer's paint blue; its nozzle's steel cup)
        yield return ("Modules", Module("Shell Welder Arm", "A robot shell arm that is an attach tool: a torch on a long neck, its cup and nozzle, and a gas bottle. Worn, it fits and welds parts.", 6, WelderArm));
        yield return ("Modules", Module("Shell Cutter Arm", "A robot shell arm that is a remover: a saw disc under a red guard. Worn, it cuts parts away.", 7, p =>
        {
            // (Look review 2026-11-19: the old disc and guard were a big flat grey slab hiding the red arm. The disc is half that area
            // now, its edge and a band across it in hazard paint, and the guard a narrow red hood over its back quarter.)
            Elbow(p, 0.06, Red);
            p.Add(Box([-0.055, 0.24, -0.055], [0.055, 0.365, 0.055], Red)); // the forearm, red
            p.Add(Box([-0.05, 0.17, -0.06], [0.05, 0.245, 0.06], Dark)); // the motor
            p.Add(Box([-0.01, 0.0, -0.07], [0.01, 0.21, 0.07], Blade)); // the disc, edge on (two crossed boxes: near round)
            p.Add(Box([-0.009, 0.035, -0.105], [0.009, 0.175, 0.105], Blade));
            p.Add(Box([-0.018, 0.085, -0.02], [0.018, 0.125, 0.02], Dark, detail: true)); // its hub
            p.Add(MachineLook.D([-0.013, 0.04, -0.109], [0.013, 0.17, -0.093], Hazard, "hazard paint")); // the teeth's edge in front, in hazard paint
            p.Add(MachineLook.D([-0.0135, 0.003, -0.065], [0.0135, 0.019, 0.065], Hazard, "hazard paint")); // and along its bottom edge
            p.Add(MachineLook.D([-0.0125, 0.059, -0.088], [0.0125, 0.077, -0.024], Hazard, "hazard paint")); // a band across the blade's faces
            foreach (var sx in new[] { -1.0, 1.0 })
                p.Add(Box([Math.Min(sx * 0.016, sx * 0.028), 0.105, 0.03], [Math.Max(sx * 0.016, sx * 0.028), 0.235, 0.125], Red, detail: true)); // the guard's sides over its back quarter
            p.Add(Box([-0.03, 0.213, 0.0], [0.03, 0.247, 0.13], Red)); // the guard's hood over the top
            p.Add(Box([-0.03, 0.085, 0.108], [0.03, 0.23, 0.135], Red, detail: true)); // and down its back
        }));
        yield return ("Modules", Module("Shell Grip Arm", "A robot shell arm that is a physics gun: three claws round a field emitter. Worn, it grabs and moves whole constructs.", 6, GripArm));
        yield return ("Modules", Module("Shell Sprayer Arm", "A robot shell arm that is a paint tool: a paint can and its nozzle. Worn, it colours parts.", 5, p =>
        {
            Elbow(p, 0.06, Paint);
            p.Add(Box([-0.055, 0.22, -0.055], [0.055, 0.365, 0.055], Joint)); // the forearm
            p.Add(Box([-0.07, 0.07, -0.07], [0.07, 0.225, 0.07], Paint)); // the paint can
            p.Add(Box([-0.073, 0.12, -0.073], [0.073, 0.16, 0.073], "#e0e4e8", detail: true)); // its label band
            p.Add(Box([-0.025, 0.02, -0.025], [0.025, 0.075, 0.025], Dark)); // the nozzle's body
            p.Add(Box([-0.032, 0.0, -0.032], [0.032, 0.024, 0.032], NozzleCup)); // its steel cup (look review 2026-11-19: a magenta tip read as a missing texture)
        }));
    }

    private static IEnumerable<(string, Part)> Modules()
    {
        yield return ("Modules", Module("Shell Optics Head", "A robot shell's standard head: two camera lenses, a sensor dome and an aerial.", 6, OpticsHead));
        yield return ("Modules", Module("Shell Plated Head", "A robot shell's armoured helm with a slit visor: the shell takes a tenth less from hits.", 11, PlatedHead));
        yield return ("Modules", Module("Shell Power Pack", "A Robot Shell's power pack, worn on its back.", 9, p =>
        {
            p.Add(Box([-0.16, 0, -0.06], [0.16, 0.37, 0.06], Dark)); // the case
            foreach (var x in new[] { -0.105, 0.035 })
                p.Add(Box([x, 0.367, -0.04], [x + 0.07, 0.41, 0.04], Light)); // two cell caps on top
            p.Add(Box([-0.12, 0.29, -0.066], [0.12, 0.32, -0.057], Lamp, detail: true)); // the charge band
            for (var k = 0; k < 4; k++)
                p.Add(Box([-0.12, 0.07 + 0.045 * k, -0.068], [0.12, 0.09 + 0.045 * k, -0.057], Joint, detail: true)); // vent fins
            p.Add(Box([0.157, 0.05, -0.03], [0.175, 0.3, 0.03], Joint, detail: true)); // a cable down its side
        }));
        yield return ("Modules", Module("Shell Hand", "A robot shell's forearm and hand, which hold its tools.", 4, p =>
        {
            p.Add(Box([-0.05, 0, -0.05], [0.05, 0.11, 0.05], Dark)); // hand
            p.Add(Box([-0.055, 0.1, -0.055], [0.055, 0.37, 0.055], Joint)); // forearm
            p.Add(Box([-0.065, 0.35, -0.065], [0.065, 0.4, 0.065], Dark, detail: true)); // the elbow's coupling
        }));
        yield return ("Modules", Module("Shell Standard Leg", "A Robot Shell's leg: hip to foot.", 8, StandardLeg));
        yield return ("Modules", Module("Shell Heavy Leg", "A Mining Shell's armoured leg, half again the size of a standard one, on a wide foot.", 24, HeavyLeg));
        yield return ("Modules", Module("Shell Runner Leg", "A sprung, digitigrade robot shell leg on clawed toes: a pair of them make the shell a tenth faster.", 7, RunnerLeg));
        foreach (var a in ToolArms())
            yield return a;
    }

    // ---- Shell storage pods (the user, 2026-11-18; docs/sandbox/design/shell-pod.md): one shell a pod, in three size classes by the
    // shell's standing height. Each size is a pair: bolted onto the surface, or flush (its body below the socket, recessed into the
    // surface it is welded to, only its lid 4 mm proud). Placed on any face: a floor, a wall or a ceiling. The pod's body is drawn
    // here; its lid, the frame that rises out of it and the platform the shell stands on move, so ShellPodRender.cs draws them from
    // "shellPod". Its solid is one hidden box (the closed pod; flush: the lid's slab only, so it doesn't meet the surface it sinks into).
    // A Medium pod is also a bin: a Mining Shell's hopper empties into it, and its Chute out takes the ore on.

    /// <summary>The sizes: name, class, half width, depth (about half the class's shipped shell), bin units (0: none; the bolted-on pod's only).</summary>
    private static readonly (string Name, string Class, double W, double D, int Bin)[] PodSizes =
        [("Tiny", "tiny", 0.35, 0.55, 0), ("Small", "small", 0.62, 1.0, 0), ("Medium", "medium", 0.95, 1.5, HopperUnits)];

    private const string Housing = "#3b4148"; // a flush pod's body
    private const string Trim = "#155a64"; // a pod's rim trim (deep teal: a brighter one goes near-white on its sunlit side)

    private static Part Pod((string Name, string Class, double W, double D, int Bin) z, bool flush)
    {
        var (w, dp) = (z.W, z.D);
        var top = flush ? ConstructShellPod.FlushLid : dp; // the lid's top: flush, its solid's top 3 cm over the surface, where its flaps are drawn (ShellPodRender.cs)
        var bot = top - dp;
        var name = flush ? $"{z.Name} Flush Shell Pod" : $"{z.Name} Shell Pod";
        var size = z.Class switch { "tiny" => "1 m", "small" => "2 m", _ => "5 m" };
        var desc = $"Parks a robot shell (up to {size} tall) when nobody wears it: walk the shell into the frame that rises out of it and press F: you wake in your chair and the pod folds the shell away. Wake in it again from a Shell Control Chair. A docked shell charges from the grid and can't be damaged or taken"
            + (z.Bin > 0 && !flush ? "; a Mining Shell's hopper empties into the pod, and its Chute out takes the ore on." : z.Bin > 0 ? "; a Mining Shell keeps its hopper's load in it (the bolted-on pod empties it)." : ".")
            + (flush ? " Flush: the pod sinks into the floor, wall or ceiling it is welded to, its lid in a hazard coaming 3 cm over the surface; what it goes in must be as deep as the pod (stacked floors, a foundation, a hull)." : " Weld it onto a floor, a wall or a ceiling.");
        var bin = flush ? 0 : z.Bin; // (a flush pod's body is in the surface, so no bin: a part's bin fills inside its solid; its Mining Shell keeps its load)
        var p = new Part(name, Small, Section, desc, role: bin > 0 ? "storage" : "panel", tool: "welder", group: "Logic", color: MachineLook.Base);
        // The walls' thickness and the rim's width are the game's (a flush pod's pocket stays inside them: ConstructShellPod.Pocket).
        const double t = ConstructShellPod.Wall, rim = ConstructShellPod.Rim;
        var rimTop = flush ? top + 0.003 : top - 0.003; // (ConstructShellPod.RimTop: flush, a coaming 3 mm over the closed flaps)
        // The solid: the closed pod (flush: a 3 cm slab over the surface, where its lid is: a thinner box would be only a detail).
        p.Add(Box([-w, 0, -w], [w, top, w], hidden: true));
        if (flush)
            p.Add(Box([-w, bot, -w], [w, -0.003, w], hidden: true)); // (its body, solid: proud of a thin surface it is a housing a player bumps into, and nothing goes into it; placing tests the lid alone, ConstructBuild.Blocked)
        var shell = flush ? Housing : MachineLook.Base; // (a flush pod's body, seen from the far side of a thin surface: mid steel, not a hole)
        // The body: four walls, a floor inside, a dark lining (3 mm in from the walls), the rim round the opening in hazard.
        p.Add(MachineLook.D([-w, bot, -w], [w, bot + t, w], shell)); // floor
        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Add(MachineLook.D([Math.Min(s * w, s * (w - t)), bot + t + 0.003, -w], [Math.Max(s * w, s * (w - t)), top - rim * 0.5, w], shell)); // the x walls
            p.Add(MachineLook.D([-w + t + 0.003, bot + t + 0.003, Math.Min(s * w, s * (w - t))], [w - t - 0.003, top - rim * 0.5, Math.Max(s * w, s * (w - t))], shell)); // the z walls
        }
        var lining = bot + ConstructShellPod.Lining; // (the lining floor's top)
        p.Add(MachineLook.D([-w + t + 0.006, bot + t + 0.003, -w + t + 0.006], [w - t - 0.006, lining, w - t - 0.006], "#22262b", "tread plate")); // the lining's floor, under the platform: deck plate
        // The pocket's lined walls: painted plating a step lighter than the lid, 4 mm in front of the walls, from the lining's floor up under the rim, so an open
        // pod reads as a lined cubby, not a box sunk through the floor (the user, 2026-11-19).
        foreach (var s in new[] { -1.0, 1.0 })
        {
            double i0 = w - t - 0.012, i1 = w - t - 0.004;
            p.Add(MachineLook.D([Math.Min(s * i0, s * i1), lining + 0.003, -(w - t - 0.006)], [Math.Max(s * i0, s * i1), top - rim * 0.5 - 0.003, w - t - 0.006], Joint)); // x sides
            p.Add(MachineLook.D([-(w - t - 0.014), lining + 0.003, Math.Min(s * i0, s * i1)], [w - t - 0.014, top - rim * 0.5 - 0.003, Math.Max(s * i0, s * i1)], Joint)); // z sides
        }
        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Add(MachineLook.D([Math.Min(s * (w - rim), s * (w + 0.004)), top - rim * 0.5 + 0.003, -w - 0.004], [Math.Max(s * (w - rim), s * (w + 0.004)), rimTop, w + 0.004], MachineLook.Accent, "hazard paint")); // the rim, x sides
            p.Add(MachineLook.D([-w + rim + 0.003, top - rim * 0.5 + 0.003, Math.Min(s * (w - rim), s * (w + 0.004))], [w - rim - 0.003, rimTop, Math.Max(s * (w - rim), s * (w + 0.004))], MachineLook.Accent, "hazard paint")); // z sides
            // A trim inlaid along the rim's inner edge, 3 mm proud of it: the opening reads at a glance (a deep teal: the visor's cyan
            // went near-white in low sun).
            double l0 = w - rim + 0.012, l1 = w - rim + 0.03;
            p.Add(MachineLook.D([Math.Min(s * l0, s * l1), rimTop - 0.004, -l1], [Math.Max(s * l0, s * l1), rimTop + 0.003, l1], Trim)); // x sides
            p.Add(MachineLook.D([-(l0 - 0.003), rimTop - 0.004, Math.Min(s * l0, s * l1)], [l0 - 0.003, rimTop + 0.003, Math.Max(s * l0, s * l1)], Trim)); // z sides
        }
        if (!flush)
        {
            // Bolted on: a flange round the foot with a bolt at each corner, and a status lamp and a vent on its front.
            p.Add(MachineLook.D([-w - 0.06, 0, -w - 0.06], [w + 0.06, 0.035, w + 0.06], Dark));
            foreach (var sx in new[] { -1.0, 1.0 })
                foreach (var sz in new[] { -1.0, 1.0 })
                    p.Add(MachineLook.D([sx * (w + 0.03) - 0.018, 0.035, sz * (w + 0.03) - 0.018], [sx * (w + 0.03) + 0.018, 0.06, sz * (w + 0.03) + 0.018], MachineLook.Steel, "steel"));
            p.Add(MachineLook.D([-0.1, top * 0.55, -w - 0.012], [0.1, top * 0.55 + 0.04, -w + 0.004], Visor)); // the status lamp
            p.Add(MachineLook.D([-w * 0.6, top * 0.18, -w - 0.01], [w * 0.6, top * 0.18 + 0.12, -w + 0.004], Joint)); // a vent plate
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Stats(z.Class switch { "tiny" => 45, "small" => 180, _ => 420 }, z.Class switch { "tiny" => 300, "small" => 700, _ => 1200 }, 6, seconds: z.Class == "medium" ? 8.0 : 4.0);
        var pod = new PyDict { ["class"] = z.Class, ["half"] = w, ["top"] = Py.Round(top, 3), ["depth"] = dp, ["flush"] = flush };
        p.Set(("power", z.Class switch { "tiny" => 30, "small" => 80, _ => 150 }), ("onBreak", "disable"), ("shellPod", pod));
        if (bin > 0)
        {
            var lo = Py.L(-w + 0.12, Py.Round(bot + 0.09, 3), -w + 0.12);
            var hi = Py.L(w - 0.12, Py.Round(bot + 0.3, 3), w - 0.12);
            p.Set(("storage", new PyDict { ["bulk"] = new PyDict { ["material"] = "any", ["capacity"] = z.Bin, ["min"] = lo, ["max"] = hi } }),
                ("ports", Py.L(Kw(("name", "Chute out"), ("type", "chute"), ("dir", "out"), ("at", Py.L(w, Py.Round(bot + dp * 0.3, 3), 0)), ("face", "+x")))));
        }
        return p;
    }

    private static IEnumerable<(string, Part)> Pods()
    {
        foreach (var z in PodSizes)
        {
            yield return ("Pods", Pod(z, flush: false));
            yield return ("Pods", Pod(z, flush: true));
        }
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", Shell()), ("", MiningShell()), ("", HalfShell()), ("", ConstructionShell()), ("", Chair()), .. Spares(), ("Gantry", Gantry()), ("Gantry", Table()), .. Modules(), .. Pods()]);
    }
}
