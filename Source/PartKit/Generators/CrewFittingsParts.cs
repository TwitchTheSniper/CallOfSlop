using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The crew fittings every ship generator hangs on its decks: the Crash Couch (a respawn bunk), the Flight Couch, the Gunnery Console,
/// the Galley Coffee Station and the Robot Shell Dock. They were the Halberd frigate's until Karl took that ship out (2026-11-16);
/// the rust bucket, the Magpie and the Zero Day read them from here, so this runs before their generators. Writes
/// Constructs/Parts/CrewFittings.
/// </summary>
public sealed class CrewFittingsParts : IPartGenerator
{
    public string Name => "crew_fittings";

    private const string Out = "Constructs/Parts/CrewFittings";
    private const string Group = "Ship";
    private const string Frame = "#26292c", Hazard = "#d8a628", PipeYellow = "#c8a030", Console = "#23282c", Screen = "#58c8d8";

    private static Part Fitting(string name, string desc, string role = "frame", string color = "#8e959a") =>
        new(name, Small, "Ship fittings", desc, role: role, tool: "wrench", material: "steel", color: color, group: Group);

    private static void WallSockets(Part p, double back, double top)
    {
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("top", [0, top, 0], "+y");
        p.Sock("wall", [0, 0.125, -back], "-z");
    }

    /// <summary>
    /// A Robot Shell's wall dock (Karl, 2026-11-17: three or four on every ship, one on the rust bucket): a back plate between two
    /// hazard posts, a hood with a downlight and a status lamp, a foot plate the shell stands on, shoulder jaws either side of it
    /// and a charge lead into its power pack. The shell hangs by its back socket on <c>front</c>, its feet on the plate, facing out.
    /// Detail boxes stay 3 mm or more off the shell's (ShellParts.Shell) and off each other's faces (z-fighting.md).
    /// </summary>
    public const double DockHalf = 0.43; // its half width over the posts: docks side by side stand this far apart and twice more

    private static Part Cradle()
    {
        const string Plate = "#3a3f43", Jaw = "#5a6064", Lead = PipeYellow, Green = "#40e070", Amber = "#f2a93b", Light = "#d8f0ff";
        var p = Fitting("Robot Shell Dock", "A wall dock for a Robot Shell: it stands on the foot plate, held by its shoulders, a lead in its back keeping it charged. Hang the shell on its front.", color: Frame);
        const double Foot = 0.03, Top = 2.2;
        p.Add(Box([-0.39, 0, -0.12], [0.39, 2.1, 0.0], Frame, shape: Kw(("finish", "dirty steel")))); // the back plate
        foreach (var s in new[] { -1, 1 })
        {
            double X(double a, double b) => s < 0 ? -b : a;
            double Xh(double a, double b) => s < 0 ? -a : b;
            p.Add(Box([X(0.39, DockHalf), 0, -0.12], [Xh(0.39, DockHalf), Top, 0.08], Hazard, shape: Kw(("finish", "hazard paint")))); // a post
            p.Add(Box([X(0.395, DockHalf - 0.003), 1.38, 0.083], [Xh(0.395, DockHalf - 0.003), 1.5, 0.27], Jaw, detail: true, shape: Kw(("finish", "steel")))); // its shoulder jaw
            p.Add(Box([X(0.40, DockHalf - 0.008), 1.4, 0.273], [Xh(0.40, DockHalf - 0.008), 1.48, 0.29], "#202326", detail: true, shape: Kw(("finish", "rubber")))); // the jaw's pad
            p.Add(Box([X(0.40, DockHalf - 0.008), 0.25, 0.083], [Xh(0.40, DockHalf - 0.008), 0.33, 0.2], Jaw, detail: true, shape: Kw(("finish", "steel")))); // the shin stop
        }
        foreach (var y in new[] { 0.45, 1.85 })
            p.Add(Box([-0.37, y, 0.003], [0.37, y + 0.07, 0.016], Jaw, detail: true, shape: Kw(("finish", "steel")))); // ribs across the plate
        p.Add(Box([-0.36, 0.003, 0.003], [0.36, Foot, 0.38], Plate, detail: true, shape: Kw(("finish", "steel")))); // the foot plate
        p.Add(Box([-0.36, 0.006, 0.383], [0.36, Foot - 0.003, 0.4], Hazard, detail: true, shape: Kw(("finish", "hazard paint")))); // its nose
        p.Add(Box([-DockHalf, Top, -0.12], [DockHalf, Top + 0.12, 0.34], Frame, shape: Kw(("finish", "dirty steel")))); // the hood
        p.Add(Box([-0.3, Top - 0.012, 0.12], [0.3, Top - 0.003, 0.3], Light, detail: true, shape: Kw(("glow", true)))); // its downlight
        p.Add(Box([-0.1, Top + 0.03, 0.343], [0.1, Top + 0.08, 0.35], Green, detail: true, shape: Kw(("glow", true)))); // the status lamp: a shell is home
        p.Add(Box([-0.05, 1.18, 0.003], [0.05, 1.3, 0.03], "#202326", detail: true, shape: Kw(("finish", "rubber")))); // the charge plug in the shell's pack
        p.Add(Box([0.17, 1.22, 0.003], [0.34, 1.26, 0.017], Lead, detail: true, shape: Kw(("finish", "rubber")))); // its lead across the plate
        p.Add(Box([0.30, 0.04, 0.003], [0.34, 1.22, 0.017], Lead, detail: true, shape: Kw(("finish", "rubber")))); // and down to the floor
        foreach (var (y, c) in new[] { (1.62, Green), (1.7, Green), (1.78, Amber) })
            p.Add(Box([-DockHalf + 0.008, y, 0.083], [-0.402, y + 0.05, 0.088], c, detail: true, shape: Kw(("glow", true)))); // the charge bars on the port post
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [0, 0.125, -0.12], "-z");
        p.Sock("front", [0, 1.25 + Foot, 0.0], "+z");
        return p.Stats(70, 300, 6, 3.0);
    }

    /// <summary>A crash couch: a padded acceleration couch with a headrest and harness. Lie in it; it is where you wake (as a bunk).</summary>
    private static Part CrashCouch()
    {
        var p = Fitting("Crash Couch", "An acceleration couch: padded, harnessed, gel-filled. Lie in it, and in survival it is where you wake.", "seat", "#3a4048");
        p.Add(Box([-0.4, 0, -1.0], [0.4, 0.35, 1.0], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.36, 0.35, -0.95], [0.36, 0.5, 0.95], "#3d4a58", shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.36, 0.5, -0.95], [0.36, 0.85, -0.7], "#3d4a58", shape: Kw(("finish", "fabric"))));
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([s * 0.4 - 0.04, 0.35, -0.6], [s * 0.4 + 0.04, 0.62, 0.5], Frame, detail: true, shape: Kw(("finish", "painted steel"))));
            p.Add(Box([s * 0.22 - 0.03, 0.501, -0.6], [s * 0.22 + 0.03, 0.515, 0.2], Hazard, detail: true, shape: Kw(("finish", "fabric"))));
        }
        p.Add(Box([-0.12, 0.62, -0.2], [0.12, 0.66, 0.0], "#40c0e0", detail: true, shape: Kw(("glow", true))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [-0.4, 0.125, 0], "-x"); // (its long side against a wall)
        p.Set(("seat", Kw(("at", Py.L(0, 0.15, 0)), ("bunk", true))));
        return p.Stats(90, 220, 4, 2.5);
    }

    /// <summary>A flight couch: a raked seat with a headrest, side sticks and a harness, for the cockpit and the gunnery stations.</summary>
    private static Part FlightCouch()
    {
        var p = Fitting("Flight Couch", "A raked flight couch with side sticks: link it to a flight computer or a turret module.", "seat", "#3a4048");
        p.Add(Box([-0.32, 0, -0.32], [0.32, 0.35, 0.32], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.3, 0.35, -0.3], [0.3, 0.5, 0.3], "#3d4a58", shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.3, 0.5, 0.2], [0.3, 1.35, 0.4], "#3d4a58", shape: Kw(("finish", "fabric"))));
        p.Add(Box([-0.18, 1.35, 0.24], [0.18, 1.55, 0.4], "#3d4a58", shape: Kw(("finish", "fabric"))));
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([s * 0.36 - 0.05, 0.35, -0.25], [s * 0.36 + 0.05, 0.65, 0.3], Frame, detail: true, shape: Kw(("finish", "painted steel"))));
            p.Add(Box([s * 0.36 - 0.025, 0.65, -0.15], [s * 0.36 + 0.025, 0.85, -0.1], "#2a2c2e", detail: true, shape: Kw(("finish", "rubber"))));
            p.Add(Box([s * 0.15 - 0.03, 0.6, 0.19], [s * 0.15 + 0.03, 1.3, 0.2], Hazard, detail: true, shape: Kw(("finish", "fabric"))));
        }
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("seat", Kw(("at", Py.L(0, -0.05, 0)))));
        return p.Stats(45, 160, 4, 2.0);
    }

    /// <summary>The galley's coffee machine on its counter: the most important part of the ship.</summary>
    private static Part Coffee()
    {
        var p = Fitting("Galley Coffee Station", "A counter with a coffee machine on it. It makes one kind of coffee, and makes it badly, and the crew would mutiny without it.", "frame", "#8e959a");
        p.Add(Box([-0.35, 0, -0.3], [0.35, 0.9, 0.3], "#5c646a", shape: Kw(("finish", "painted steel"))));
        p.Add(Box([-0.37, 0.9, -0.32], [0.37, 0.94, 0.32], "#2f3539", detail: true, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.22, 0.94, -0.3], [0.22, 1.45, -0.02], "#c8ccc8", shape: Kw(("finish", "chrome"))));
        p.Add(Box([-0.14, 1.05, -0.02], [0.14, 1.12, 0.06], "#2a2c2e", detail: true, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.05, 0.94, 0.0], [0.05, 1.03, 0.1], "#f0f0e8", detail: true, shape: Kw(("finish", "gloss paint"))));
        p.Add(Box([0.08, 1.3, -0.019], [0.16, 1.36, -0.005], "#ff4020", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([0.22, 0.94, -0.2], [0.33, 1.1, -0.1], "#6a4a30", detail: true, shape: Kw(("finish", "plastic"))));
        WallSockets(p, 0.3, 1.45);
        return p.Stats(70, 160, 3, 2.0);
    }

    /// <summary>A gunnery station: a console with two screens and a stick housing, in front of the gunner's couch.</summary>
    private static Part GunneryConsole()
    {
        var p = Fitting("Gunnery Console", "A gunner's console: target screens and a stick housing. The turret module sits under it.", color: Console);
        p.Add(Box([-0.45, 0, -0.25], [0.45, 0.8, 0.25], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.45, 0.8, -0.25], [0.45, 1.35, -0.1], Console, shape: Kw(("finish", "plastic"))));
        p.Add(Box([-0.4, 0.85, -0.1], [0.4, 1.3, -0.085], Screen, detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.4, 0.8, -0.08], [0.4, 0.82, 0.2], "#e0a040", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([-0.46, 0.0, -0.26], [0.46, 0.12, 0.26], Hazard, detail: true, shape: Kw(("finish", "hazard paint"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("module", [0.45, 0.4, 0], "+x");
        return p.Stats(60, 200, 4, 2.0);
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var p in new[] { Cradle(), CrashCouch(), FlightCouch(), Coffee(), GunneryConsole() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
