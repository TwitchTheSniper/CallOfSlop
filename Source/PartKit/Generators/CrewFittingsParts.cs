using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The crew fittings every ship generator hangs on its decks: the Crash Couch (a respawn bunk), the Flight Couch, the Gunnery Console,
/// the Galley Coffee Station and the Robot Charging Cradle. They were the Halberd frigate's until Karl took that ship out (2026-11-16);
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

    /// <summary>A robot's charging cradle: a back frame with clamps and a status light. The shell hangs on its front by its back.</summary>
    private static Part Cradle()
    {
        var p = Fitting("Robot Charging Cradle", "A wall cradle for a robot shell: clamps, a charge lead and a status light. Hang the shell on its front.", color: Frame);
        p.Add(Box([-0.4, 0, -0.12], [0.4, 2.1, 0.0], Frame, shape: Kw(("finish", "dirty steel"))));
        p.Add(Box([-0.45, 0, -0.12], [-0.4, 2.2, 0.06], Hazard, shape: Kw(("finish", "hazard paint"))));
        p.Add(Box([0.4, 0, -0.12], [0.45, 2.2, 0.06], Hazard, shape: Kw(("finish", "hazard paint"))));
        foreach (var y in new[] { 0.9, 1.5 })
            p.Add(Box([-0.3, y, 0.0], [0.3, y + 0.08, 0.06], "#5a6064", detail: true, shape: Kw(("finish", "steel"))));
        p.Add(Box([-0.08, 1.95, 0.0], [0.08, 2.02, 0.03], "#40e070", detail: true, shape: Kw(("glow", true))));
        p.Add(Box([0.25, 0.3, 0.0], [0.31, 1.6, 0.05], PipeYellow, detail: true, shape: Kw(("finish", "rubber"))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [0, 0.125, -0.12], "-z");
        p.Sock("front", [0, 1.25, 0.0], "+z");
        return p.Stats(60, 300, 6, 3.0);
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
