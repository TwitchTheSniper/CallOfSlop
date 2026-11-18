using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Spacewalk gear (Karl, 2026-11-17; docs/sandbox/EVA.md): the Space Suit Locker, where a suit goes on and comes off; the
/// Manned Manoeuvring Unit, a thruster pack that latches onto a suit's back attach point; and the MMU Dock it rides on,
/// a station you back into, with a Fuel in port that fills the docked pack from the ship. Writes Constructs/Parts/Eva.
/// The pack is an original design after the 1980s manned manoeuvring units: a boxy white frame, a thruster quad at each
/// corner and two arms forward with the hand controllers. Its boxes are also what a player wears (EvaRender.cs), in the
/// pack frame: x across, y up from the latch's middle, +z away from the wearer's back, the latch plate's face at z 0.
/// </summary>
public sealed class EvaParts : IPartGenerator
{
    public string Name => "eva_parts";

    private const string Out = "Constructs/Parts/Eva";
    private const string Section = "Pilot gear", Group = "Flight";
    private const string White = "#e4e4dc", Grey = "#a7abab", Dark = "#2a2c2f", Steel = "#5c6166", Gold = "#c9a24a", Orange = "#d8642a", Frame = "#2c3034", Hazard = "#d8a628", PipeYellow = "#c8a030";

    /// <summary>Where a docked pack sits in the dock's frame: its latch faces the rider's back (MMU Dock's seat).</summary>
    public const double DockY = 0.93, DockZ = 0.47, MountZ = 0.39;

    public const string LockerName = "Space Suit Locker", DockName = "MMU Dock", MmuName = "Manned Manoeuvring Unit";

    /// <summary>
    /// A ship's spacewalk station (docs/sandbox/EVA.md "On the ships"): an MMU Dock hung by its "wall" socket on the ship's socket, a pack
    /// docked on it, and, when a tank is given, a pipe from the tank's Fuel out into the dock's Fuel in (routed as conduit by the build).
    /// Returns the dock's step.
    /// </summary>
    public static int FitDock(BuildingParts.Blueprint bp, int on, string sock, int tank = -1, string tankPort = "Fuel out")
    {
        var dock = bp.Hang(DockName, on, sock, "wall");
        bp.Hang(MmuName, dock, "mmu", "mount");
        if (tank >= 0)
            bp.Pipes.Add((tank, dock, tankPort, "Fuel in"));
        return dock;
    }

    private static PyDict F(string finish) => Kw(("finish", finish));

    private static PyDict? Look(double[] a, double[] b, string color, string finish, bool detail = true) => Box(a, b, color, detail: detail, shape: F(finish));

    /// <summary>The Manned Manoeuvring Unit: the pack a suited player wears, or that rides on its dock.</summary>
    private static Part Mmu()
    {
        var p = new Part("Manned Manoeuvring Unit", Small, Section,
            "A thruster pack for spacewalks. Back into its MMU Dock in a space suit, then press the detach key: WASD, jump and crouch fly you, by the look. It refuels on its dock from the ship's fuel.",
            role: "frame", tool: "wrench", material: "steel", color: White, group: Group);
        p.Add(Look([-0.12, -0.1, 0.0], [0.12, 0.1, 0.035], Dark, "steel")); // (the latch plate, onto the suit's attach point)
        p.Add(Box([-0.36, -0.55, 0.03], [0.36, 0.5, 0.34], White, shape: F("gloss paint"))); // (the frame: the one solid box)
        p.Add(Look([-0.3, -0.45, 0.34], [0.3, 0.42, 0.352], Grey, "painted steel")); // (the back panel)
        p.Add(Look([-0.3, 0.2, 0.352], [0.3, 0.26, 0.358], Orange, "worn paint")); // (an ID stripe)
        p.Add(Look([-0.1, -0.2, 0.34], [0.1, 0.0, 0.39], Dark, "steel", detail: false)); // (the mounting lug its dock clamps)
        p.Add(Look([-0.33, 0.5, 0.06], [0.33, 0.528, 0.31], Grey, "painted steel")); // (the top cap)
        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Add(Look(Sx(s, 0.355, -0.35, 0.08, 0.384, 0.3, 0.29), Sx2(s, 0.355, -0.35, 0.08, 0.384, 0.3, 0.29), Grey, "painted steel")); // (a propellant tank's bulge)
            p.Add(Look(Sx(s, 0.384, -0.46, 0.2, 0.408, -0.38, 0.26), Sx2(s, 0.384, -0.46, 0.2, 0.408, -0.38, 0.26), Gold, "brass")); // (its fill valve)
            foreach (var t in new[] { -1.0, 1.0 })
            {
                // A thruster quad on each corner: its tower, and nozzles out of its top or bottom, side, front and back.
                var (y0, y1) = t > 0 ? (0.36, 0.56) : (-0.6, -0.4);
                p.Add(Look(Sx(s, 0.35, y0, 0.04, 0.46, y1, 0.3), Sx2(s, 0.35, y0, 0.04, 0.46, y1, 0.3), White, "gloss paint"));
                var yn = t > 0 ? y1 : y0 - 0.03;
                p.Add(Look(Sx(s, 0.39, yn, 0.15, 0.43, yn + 0.03, 0.19), Sx2(s, 0.39, yn, 0.15, 0.43, yn + 0.03, 0.19), Dark, "steel")); // (up or down)
                var ym = (y0 + y1) / 2;
                p.Add(Look(Sx(s, 0.46, ym - 0.02, 0.15, 0.49, ym + 0.02, 0.19), Sx2(s, 0.46, ym - 0.02, 0.15, 0.49, ym + 0.02, 0.19), Dark, "steel")); // (sideways)
                p.Add(Look(Sx(s, 0.39, ym - 0.02, 0.01, 0.43, ym + 0.02, 0.04), Sx2(s, 0.39, ym - 0.02, 0.01, 0.43, ym + 0.02, 0.04), Dark, "steel")); // (forward)
                p.Add(Look(Sx(s, 0.39, ym - 0.02, 0.3, 0.43, ym + 0.02, 0.33), Sx2(s, 0.39, ym - 0.02, 0.3, 0.43, ym + 0.02, 0.33), Dark, "steel")); // (back)
            }
            // The arm: its pivot on the frame's side, the arm forward past the wearer's hips, the hand controller at its end.
            p.Add(Look(Sx(s, 0.355, -0.21, 0.02, 0.48, -0.07, 0.17), Sx2(s, 0.355, -0.21, 0.02, 0.48, -0.07, 0.17), Grey, "painted steel"));
            p.Add(Look(Sx(s, 0.475, -0.18, -0.62, 0.535, -0.11, 0.12), Sx2(s, 0.475, -0.18, -0.62, 0.535, -0.11, 0.12), White, "gloss paint"));
            p.Add(Look(Sx(s, 0.45, -0.25, -0.75, 0.565, -0.09, -0.6), Sx2(s, 0.45, -0.25, -0.75, 0.565, -0.09, -0.6), Grey, "plastic"));
            p.Add(Look(Sx(s, 0.49, -0.09, -0.7, 0.525, 0.01, -0.665), Sx2(s, 0.49, -0.09, -0.7, 0.525, 0.01, -0.665), Dark, "rubber")); // (the grip)
            p.Add(Look(Sx(s, 0.49, 0.01, -0.7, 0.525, 0.03, -0.665), Sx2(s, 0.49, 0.01, -0.7, 0.525, 0.03, -0.665), s > 0 ? Orange : Gold, "plastic")); // (its thumb button)
        }
        p.Add(Box([-0.53, -0.16, -0.765], [-0.49, -0.13, -0.745], "#40e070", detail: true, shape: Kw(("glow", true), ("paintable", false)))); // (the ready lamp, left controller)
        p.Add(Look([0.37, 0.56, -0.06], [0.45, 0.62, 0.05], Dark, "steel")); // (a work lamp on the right top quad)
        p.Add(Box([0.38, 0.57, -0.075], [0.44, 0.61, -0.058], "#fff2c0", detail: true, shape: Kw(("glow", true), ("paintable", false))));
        p.Sock("mount", [0, -0.1, MountZ], "+z");
        p.Set(("eva", "mmu"), ("collider", "boxes"));
        return p.Stats(30, 200, 4, 2.0);
    }

    // A box on side s (+1 right, -1 left) from its inner x to its outer x: the min and max corners.
    private static double[] Sx(double s, double x0, double y0, double z0, double x1, double y1, double z1) => [s > 0 ? x0 : -x1, y0, z0];
    private static double[] Sx2(double s, double x0, double y0, double z0, double x1, double y1, double z1) => [s > 0 ? x1 : -x0, y1, z1];

    /// <summary>The MMU Dock: a perch with foot loops, an upright behind with the pack's clamps, and the fuel line in.</summary>
    private static Part Dock()
    {
        var p = new Part("MMU Dock", Small, Section,
            "Where a Manned Manoeuvring Unit rides and refuels. In a space suit, F backs you into it; the detach key flies you off with the pack, the stand key leaves it docked. Pipe fuel to its Fuel in.",
            role: "seat", tool: "wrench", material: "steel", color: Frame, group: Group);
        p.Add(Box([-0.55, 0, -0.5], [0.55, 0.08, 0.96], Frame, shape: F("dirty steel"))); // (the base)
        p.Add(Look([-0.5, 0.08, -0.44], [0.5, 0.092, -0.1], "#3a3f44", "painted grating"));
        foreach (var s in new[] { -1.0, 1.0 })
            p.Add(Look([s * 0.15 - 0.07, 0.08, -0.38], [s * 0.15 + 0.07, 0.15, -0.3], Hazard, "hazard paint")); // (foot loops)
        p.Add(Box([-0.22, 0.08, -0.05], [0.22, 0.5, 0.25], Frame, shape: F("painted steel"))); // (the perch)
        p.Add(Look([-0.27, 0.5, -0.25], [0.27, 0.56, 0.25], "#3d4a58", "fabric"));
        p.Add(Box([-0.5, 0.08, 0.86], [0.5, 1.75, 0.96], Frame, shape: F("dirty steel"))); // (the upright)
        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Add(Look([s > 0 ? 0.4 : -0.47, 0.55, 0.7], [s > 0 ? 0.47 : -0.4, 1.3, 0.86], Hazard, "hazard paint")); // (the clamp arms)
            p.Add(Look([s > 0 ? 0.47 : -0.5, 1.6, 0.6], [s > 0 ? 0.5 : -0.47, 1.66, 0.86], Steel, "steel")); // (handrails)
        }
        p.Add(Look([-0.12, 0.7, 0.84], [0.12, 0.88, 0.858], Steel, "steel")); // (the clamp's face)
        p.Add(Look([0.45, 0.08, 0.5], [0.545, 0.16, 0.7], PipeYellow, "rubber")); // (the fuel line's coupling, over its port)
        p.Add(Box([-0.1, 1.65, 0.84], [0.1, 1.7, 0.857], "#40e070", detail: true, shape: Kw(("glow", true), ("paintable", false))));
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [0, 0.125, 0.96], "+z");
        p.Sock("mmu", [0, DockY - 0.1, DockZ + MountZ], "-z");
        p.Set(("seat", Kw(("at", Py.L(0, 0.01, 0)))), ("eva", "dock"),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0.55, 0.04, 0.6], "+x", "fuel"))));
        return p.Stats(80, 400, 6, 3.0);
    }

    /// <summary>The Space Suit Locker: a tall cabinet with a window, a white suit and helmet hanging inside.</summary>
    private static Part Locker()
    {
        var p = new Part("Space Suit Locker", Small, Section,
            "A space suit on a rack behind glass. F puts the suit on (its air full) or hangs it back up. A robot shell needs no suit.",
            role: "frame", tool: "wrench", material: "steel", color: Grey, group: Group);
        p.Add(Box([-0.4, 0, 0.22], [0.4, 2.0, 0.3], Steel, shape: F("painted steel"))); // (the back)
        p.Add(Box([-0.4, 0, -0.3], [0.4, 0.3, 0.22], Steel, shape: F("painted steel"))); // (the plinth)
        p.Add(Box([-0.4, 1.9, -0.3], [0.4, 2.0, 0.22], Steel, shape: F("painted steel"))); // (the top)
        foreach (var s in new[] { -1.0, 1.0 })
            p.Add(Box([s > 0 ? 0.33 : -0.4, 0.3, -0.3], [s > 0 ? 0.4 : -0.33, 1.9, 0.22], Steel, shape: F("painted steel"))); // (the sides)
        p.Add(Box([-0.33, 0.3, -0.295], [0.33, 1.9, -0.28], "#9fc8d8", glass: true)); // (the window)
        p.Add(Look([-0.36, 1.78, -0.305], [0.36, 1.86, -0.3], Orange, "worn paint")); // (the stripe over it)
        // The suit inside: helmet with its gold visor, the life-support pack's outline behind, arms, legs, boots.
        p.Add(Look([-0.2, 1.0, -0.12], [0.2, 1.45, 0.12], White, "fabric"));
        p.Add(Look([-0.14, 1.48, -0.13], [0.14, 1.75, 0.13], White, "gloss paint"));
        p.Add(Look([-0.1, 1.55, -0.14], [0.1, 1.68, -0.13], Gold, "chrome"));
        p.Add(Look([-0.08, 1.15, -0.135], [0.08, 1.25, -0.12], Grey, "plastic")); // (the chest controls)
        foreach (var s in new[] { -1.0, 1.0 })
        {
            p.Add(Look([s > 0 ? 0.2 : -0.29, 0.95, -0.08], [s > 0 ? 0.29 : -0.2, 1.42, 0.08], White, "fabric"));
            p.Add(Look([s > 0 ? 0.02 : -0.18, 0.4, -0.09], [s > 0 ? 0.18 : -0.02, 1.0, 0.07], White, "fabric"));
            p.Add(Look([s > 0 ? 0.02 : -0.18, 0.3, -0.12], [s > 0 ? 0.18 : -0.02, 0.4, 0.08], Grey, "rubber"));
        }
        p.Add(Look([-0.03, 1.45, 0.13], [0.03, 1.5, 0.2], Steel, "steel")); // (the hanger)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Sock("wall", [0, 0.125, 0.3], "+z");
        p.Set(("eva", "locker"));
        return p.Stats(70, 250, 4, 2.0);
    }

    public void Make(PartOutput o)
    {
        o.Own(Out);
        foreach (var p in new[] { Locker(), Dock(), Mmu() })
        {
            var d = p.Json();
            o.File($"{Out}/{FileName((string)d["name"]!)}", PyJson.Dumps(d, 2) + "\n");
        }
    }
}
