using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The robot shells: Constructs/Parts/Shells (the Robot Shell and the Shell Control Chair that wakes you in one), and the Depot Lander
/// blueprint, the Orbiter without its Driving Module (the Orbital Depot's challenge is to fit and link one). Was
/// tools/constructs/shell_parts.py. The lander follows Constructs/Blueprints/Orbiter.json: run this after changing it.
/// </summary>
public sealed class ShellParts : IPartGenerator
{
    public string Name => "shell_parts";

    private const string Out = "Constructs/Parts/Shells";
    private const string Section = "Shells";
    private const string Steel = "#8b949c", Dark = "#30363d", Joint = "#56606a", Visor = "#38d0e8", Lamp = "#f2a93b", Seat = "#3b4652";
    private const int Range = 3000; // m: the chair's reach (the story's depot hangs 1.5 km over its chairs)

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
        p.Add(Box([-0.28, 0, -0.3], [0.28, 0.42, 0.3], Metal)); // the plinth and couch (solid)
        p.Add(Box([-0.28, 0.4, 0.22], [0.28, 1.15, 0.34], Dark)); // back (solid)
        p.Add(Box([-0.36, 0.3, -0.25], [-0.29, 0.62, 0.2], Dark)); // arms (solid)
        p.Add(Box([0.29, 0.3, -0.25], [0.36, 0.62, 0.2], Dark));
        p.Add(Box([-0.25, 0.423, -0.27], [0.25, 0.47, 0.2], Pad, detail: true)); // the couch's pad
        p.Add(Box([-0.23, 0.473, 0.18], [0.23, 1.1, 0.217], Pad, detail: true)); // the back's pad
        p.Add(Box([-0.3, 0.003, -0.303], [0.3, 0.05, 0.303], Cable, detail: true)); // the plinth's foot
        p.Add(Box([-0.2, 0.12, -0.304], [0.2, 0.15, -0.3], Visor, detail: true)); // the plinth's lamp strip
        p.Add(Box([-0.07, 0.3, 0.343], [0.07, 1.58, 0.45], Metal, detail: true)); // the spine column
        p.Add(Box([-0.035, 0.05, 0.453], [0.035, 1.5, 0.49], Cable, detail: true)); // its cable bundle
        p.Add(Box([-0.05, 1.5, -0.02], [0.05, 1.56, 0.343], Metal, detail: true)); // the helmet's arm from the spine
        p.Add(Box([-0.2, 1.4, -0.16], [0.2, 1.5, 0.2], Dark, detail: true)); // the helmet: crown
        p.Add(Box([-0.205, 1.08, -0.16], [-0.17, 1.397, 0.2], Dark, detail: true)); // its sides
        p.Add(Box([0.17, 1.08, -0.16], [0.205, 1.397, 0.2], Dark, detail: true));
        p.Add(Box([-0.17, 1.12, 0.165], [0.17, 1.397, 0.2], Dark, detail: true)); // its back
        p.Add(Box([-0.168, 1.17, -0.165], [0.168, 1.25, -0.13], Visor, detail: true)); // its visor over the eyes
        p.Add(Box([-0.12, 1.503, -0.06], [0.12, 1.52, 0.1], Joint, detail: true)); // the crown's vent plate
        p.Add(Box([-0.36, 0.623, -0.25], [-0.29, 0.66, 0.0], Joint, detail: true)); // the left console
        p.Add(Box([0.29, 0.623, -0.25], [0.36, 0.66, 0.0], Joint, detail: true)); // the right console
        p.Add(Box([0.31, 0.663, -0.22], [0.34, 0.672, -0.19], Lamp, detail: true)); // its lamps: the link lamp, amber
        p.Add(Box([0.31, 0.663, -0.16], [0.34, 0.672, -0.13], Green, detail: true));
        p.Add(Box([0.31, 0.663, -0.1], [0.34, 0.672, -0.07], Visor, detail: true));
        p.Add(Box([-0.34, 0.663, -0.22], [-0.31, 0.672, -0.19], Green, detail: true));
        p.Add(Box([-0.34, 0.663, -0.16], [-0.31, 0.672, -0.13], Green, detail: true));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("back", [0, 0.6, 0.34], "+z");
        p.Sock("+x", [0.36, 0.2, 0], "+x");
        p.Sock("-x", [-0.36, 0.2, 0], "-x");
        p.Stats(60, 200, 3, 3);
        p.Set(("seat", new PyDict { ["at"] = Py.L(0, -0.5, 0) }), ("shell", new PyDict { ["range"] = Range }), ("config", Py.L(Channel())));
        return p;
    }

    /// <summary>The Orbiter without its Driving Module or wires: the depot's challenge is to fit and link one.</summary>
    private static PyDict Lander(PartOutput o)
    {
        var orbiter = (PyDict)o.ReadJson("Constructs/Blueprints/Orbiter.json")!;
        var all = (List<object?>)orbiter["steps"]!;
        var steps = all.Cast<PyDict>().Where(s => (string?)s["part"] != "Driving Module").Cast<object?>().ToList();
        if (steps.Count != all.Count - 1 || !steps.Cast<PyDict>().All(s => Py.D(s.Get("on") ?? 0) < steps.Count))
            throw new InvalidOperationException("Orbiter.json: expected one Driving Module that nothing else stands on");
        return new PyDict
        {
            ["format"] = "call-of-slop-construct-blueprint/1",
            ["name"] = "Depot Lander",
            ["description"] = "An Orbiter from the Ministry's Orbital Depot with no Driving Module: fit one on the pilot's seat and link seat, module and jets to fly it.",
            ["steps"] = steps,
        };
    }

    /// <summary>The Orbiter in the Ministry's colours (navy tanks, a green band, slate jets): the Ministry Courier parked on the
    /// spaceport's first pad (docs/story/SPACEPORT.md), the same ship to fly, its seats and wires as they are.</summary>
    private static PyDict Courier(PartOutput o)
    {
        var orbiter = (PyDict)o.ReadJson("Constructs/Blueprints/Orbiter.json")!;
        var steps = ((List<object?>)orbiter["steps"]!).Cast<PyDict>().ToList();
        for (var k = 0; k < steps.Count; k++)
        {
            var part = (string?)steps[k]["part"];
            var paint = part switch { "Fuel Tank" => k is 0 or 1 or 2 ? "#2f5a3c" : "#1f2b45", "Small Jet" => "#4b5157", _ => null };
            if (paint == null)
                continue;
            steps[k]["paint"] = paint;
            steps[k]["finish"] = "worn paint";
        }
        return new PyDict
        {
            ["format"] = "call-of-slop-construct-blueprint/1",
            ["name"] = "Ministry Courier",
            ["description"] = "The Ministry of Slop's courier ship from the Frostreach spaceport: an Orbiter for two in navy and green, that climbs out of the sky into orbit.",
            ["steps"] = steps.Cast<object?>().ToList(),
            ["wires"] = orbiter["wires"],
        };
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [("", Shell()), ("", Chair())]);
        o.File("Constructs/Blueprints/DepotLander.json", PyJson.Dumps(Lander(o), 2) + "\n");
        o.File("Constructs/Blueprints/MinistryCourier.json", PyJson.Dumps(Courier(o), 2) + "\n");
    }
}
