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
        foreach (var s in new[] { -1, 1 })
        {
            double x0 = Math.Min(s * 0.045, s * 0.175), x1 = Math.Max(s * 0.045, s * 0.175);
            p.Add(Box([x0 - 0.01, 0.0, -0.15], [x1 + 0.01, 0.07, 0.09], Dark)); // feet
            p.Add(Box([x0, 0.06, -0.07], [x1, 0.47, 0.07], Joint)); // shins
            p.Add(Box([x0 - 0.005, 0.45, -0.085], [x1 + 0.005, 0.53, 0.085], Dark, detail: true)); // knees
            p.Add(Box([x0 + 0.005, 0.52, -0.075], [x1 - 0.005, 0.88, 0.075])); // thighs
            double a0 = Math.Min(s * 0.26, s * 0.37), a1 = Math.Max(s * 0.26, s * 0.37);
            p.Add(Box([a0 - 0.02, 1.36, -0.1], [a1 + 0.02, 1.53, 0.1], Joint)); // shoulders
            p.Add(Box([a0, 1.0, -0.06], [a1, 1.37, 0.06])); // upper arms
            p.Add(Box([a0 + 0.005, 0.74, -0.055], [a1 - 0.005, 1.01, 0.055], Joint)); // forearms
            p.Add(Box([a0 + 0.01, 0.64, -0.05], [a1 - 0.01, 0.75, 0.05], Dark, detail: true)); // hands
        }
        p.Add(Box([-0.2, 0.86, -0.11], [0.2, 1.02, 0.1], Dark)); // pelvis
        p.Add(Box([-0.245, 1.0, -0.135], [0.245, 1.5, 0.125])); // torso
        p.Add(Box([-0.17, 1.13, -0.16], [0.17, 1.42, -0.12], Joint, detail: true)); // chest plate
        p.Add(Box([-0.05, 1.22, -0.172], [0.05, 1.27, -0.15], Lamp, detail: true)); // status lamp
        p.Add(Box([-0.16, 1.07, 0.11], [0.16, 1.44, 0.2], Dark)); // power pack on the back
        p.Add(Box([-0.06, 1.48, -0.06], [0.06, 1.58, 0.06], Dark)); // neck
        p.Add(Box([-0.135, 1.56, -0.14], [0.135, 1.83, 0.125])); // head
        p.Add(Box([-0.11, 1.65, -0.158], [0.11, 1.73, -0.12], Visor)); // visor
        p.Add(Box([0.07, 1.82, -0.015], [0.09, 1.96, 0.015], Dark, detail: true)); // aerial
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 1.25, 0.2], "+z");
        p.Stats(38, 160, 3, 3);
        p.Set(("shell", new PyDict { ["body"] = true }), ("config", Py.L(Channel())), ("centreSockets", false));
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

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", Shell()), ("", Chair()), .. Spares()]);
    }
}
