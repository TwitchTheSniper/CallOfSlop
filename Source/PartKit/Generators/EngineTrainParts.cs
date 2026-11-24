using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Engine trains (the "Ship engines with interior parts" design, Sean 2026-11-24; docs/sandbox/design/engine-trains.md): a drive's
/// bell with its engine welded on behind it, piece by piece, the whole engine on one generic Round Module Plug so it is fitted, pulled,
/// bought and salvaged whole. This first set is the minimum viable engine Sean approved: a <b>Drive Nozzle XS</b>, a <b>Reaction
/// Chamber XS</b> and a <b>Fuel Injector XS</b> welded end to end on the 1 m round plug, with one fuel pipe into the injector, written to
/// Constructs/Parts/EngineTrain, and its module blueprint, <b>Module Engine XS</b>.
///
/// Frames: the bell's origin is the plug face it stands on, its exhaust along +z; a piece's origin is the middle of its front face (+z,
/// towards the bell), its cylinder along -z. The train runs back through the plug's hole and an Opening socket's hole, so its solid is
/// four bars round a 10 cm core on the axis (a plug's and a plate's small centre face boxes stand there). Every piece's front flange is a
/// few mm under the back flange it meets, so a joint is a lapped seam with no shared face.
///
/// Looks are draw-only (ConstructRender's drive looks, <c>DriveFx</c>): the bell's level, eased into heat and pump speed on each peer,
/// glows the chamber's coils and sight ports and the throat, runs the injector's ring lamps and strokes its valves. Nothing new per tick or on the wire.
/// </summary>
public sealed class EngineTrainParts : IPartGenerator
{
    public string Name => "engine_train";

    public const string Out = "Constructs/Parts/EngineTrain", Section = "Ship fittings", Group = "Ship";
    private const string BpOut = "Constructs/Blueprints";

    private const string Clad = "#6c7378", Steel = "#5a6065", Trim = "#7b8186", Dark = "#34373a", Hazard = "#d8a020", Copper = "#8a5a3a",
        Chamber = "#98a1a9", Cable = "#262829", Bolt = "#9a9c98", Quartz = "#3a2a24", Lamp = "#1f3a26", Coil = "#6a3a26", Throat = "#3a1c14";

    // The XS train (the 1 m round socket's): bore radius, flange radius, and per piece its length, what it serves, mass, health, armour.
    public const double Bore = 0.28, FlangeR = 0.33, ChamberD = 1.1, InjectorD = 0.3;
    public const double Across = FlangeR * 2; // (an XS piece's bore: across its joint flanges; pieces weld only to the same bore, ConstructWorks.Bores)
    public const double NozzleForce = 2.4e5, ChamberServes = 2.4e5, InjectorServes = 2.4e5;
    private const double NozzleLen = 1.0, NozzleCollar = 0.36, NozzleMouth = 0.43, Burn = 2.0; // (l/s a MN, as a hull drive's)

    private static double[] P(double x, double y, double z) => [x, y, z];

    private static PyDict? Look(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish, string? work = null, double travel = 0, string? axis = null)
    {
        var shape = finish.Length > 0 ? Kw(("finish", finish)) : null;
        var b = Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape);
        p.Add(b);
        Work(p, work, travel, axis);
        return b;
    }

    private static void Work(Part p, string? work, double travel, string? axis)
    {
        if (work == null)
            return;
        var b = p.Boxes[^1]!;
        b["work"] = work;
        b["paintable"] = false;
        if (travel > 0)
            b["travel"] = Py.Round(travel, 4);
        if (axis != null)
            b["axis"] = axis;
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));

    /// <summary>A cylinder along z, its axis at (x, y): taper the -z end's share of r, inner a tube's bore.</summary>
    private static void Cyl(Part p, double x, double y, double r, double z0, double z1, string color, string finish, double taper = 1, double inner = 0, int segments = 20, string? work = null)
    {
        var shape = Kw(("shape", inner > 0 ? "tube" : "cylinder"), ("segments", segments));
        if (finish.Length > 0)
            shape["finish"] = finish;
        if (inner > 0)
            shape["inner"] = Py.Round(inner, 4);
        if (taper < 1)
            shape["taper"] = Py.L(Py.Round(taper, 4), Py.Round(taper, 4));
        p.Add(Box(P(x - r, y - r, Math.Min(z0, z1)), P(x + r, y + r, Math.Max(z0, z1)), color, detail: true, shape: shape));
        Work(p, work, 0, null);
    }

    /// <summary>Bolt heads round a flange: n of them at radius rr, from z0 to z1.</summary>
    private static void Bolts(Part p, double rr, double z0, double z1, int n, double phase = 0)
    {
        for (var k = 0; k < n; k++)
        {
            var a = phase + Math.PI / n + k * 2 * Math.PI / n;
            var (x, y) = (rr * Math.Cos(a), rr * Math.Sin(a));
            Look(p, x - 0.016, y - 0.016, z0, x + 0.016, y + 0.016, z1, Bolt, "steel");
        }
    }

    /// <summary>
    /// A train piece's solid, from its front face (z 0) back to <paramref name="d"/>: four bars round a 10 cm core on its axis, inside its
    /// round, so it passes a plug's and a plate's centre face boxes in an Opening; a ring of them reaches each face, where the sockets are.
    /// </summary>
    private static void TrainSolid(Part p, double d)
    {
        var (s, c) = (0.2, 0.05);
        Hidden(p, -s, c, -d, s, s, 0);
        Hidden(p, -s, -s, -d, s, -c, 0);
        Hidden(p, c, -c, -d, s, c, 0);
        Hidden(p, -s, -c, -d, -c, c, 0);
        p.Set(("collider", "boxes"), ("onBreak", "disable"));
    }

    // ---- the bell -------------------------------------------------------------------------------------------------

    /// <summary>
    /// The Drive Nozzle XS: the smallest torch bell, a flanged collar that stands on a 1 m Round Module Plug and a bell along +z, its
    /// train welded on behind it (its "train" socket, the collar's back, where the plug's hole is). It takes its fuel only through its
    /// train's Fuel Injector: a pipe into that, never a tank it touches. Its throat glows with the chamber's heat.
    /// </summary>
    private static Part Nozzle()
    {
        var p = new Part("Drive Nozzle XS", Small, Section,
            "The smallest torch drive's bell, 240 kN (a 1 m round socket takes 250 kN): stand it on a 1 m Round Module Plug and weld its engine on behind it, through the plug's hole: a Reaction Chamber XS, then a Fuel Injector XS, and a fuel pipe into the injector's Fuel in. Fitted to a Round Module Socket in Opening style, the whole engine goes in and comes out as one. Without its train it pushes at a quarter; with no fuel pipe it runs on the injector's buffer, then stops.",
            role: "thruster", tool: "wrench", material: "steel", color: Dark, group: Group);
        var (r0, len, mouth) = (NozzleCollar, NozzleLen, NozzleMouth);
        Cyl(p, 0, 0, r0, 0.003, 0.07, Trim, "dirty steel", segments: 24); // (the collar's flange, 3 mm over the plug's top)
        Bolts(p, r0 - 0.035, 0.068, 0.09, 8);
        var throatR = 0.19;
        var z1 = 0.32;
        Cyl(p, 0, 0, 0.25, 0.067, 0.16, Dark, "dirty steel", segments: 20); // (the throat's housing)
        Cyl(p, 0, 0, throatR, 0.157, z1, "#2f3134", "dirty steel", segments: 16); // (the throat)
        Cyl(p, 0, 0, throatR + 0.012, z1 - 0.06, z1 - 0.02, Throat, "", segments: 16, work: "glow"); // (the throat ring: dark at rest, lit by the chamber's heat)
        Cyl(p, 0, 0, mouth, z1, len, "#2a2c2e", "soot", taper: Math.Round(throatR / mouth, 3), inner: 0.9, segments: 24); // (the bell)
        foreach (var zb in new[] { 0.35, 0.7 })
        {
            var z = z1 + (len - z1) * zb;
            var rb = throatR + (mouth - throatR) * zb + 0.018;
            Cyl(p, 0, 0, rb, z - 0.03, z + 0.03, Copper, "copper", inner: Math.Round((rb - 0.04) / rb, 3), segments: 24); // (cooling bands)
        }
        // Four struts from the collar to the first band (a small bell's stiffeners), and a hazard band on the collar.
        for (var k = 0; k < 4; k++)
        {
            var a = Math.PI / 4 + k * Math.PI / 2;
            var (x, y) = (0.27 * Math.Cos(a), 0.27 * Math.Sin(a));
            Look(p, x - 0.015, y - 0.015, 0.072, x + 0.015, y + 0.015, 0.5, Steel, "dirty steel");
        }
        Cyl(p, 0, 0, 0.255, 0.1, 0.13, Hazard, "hazard paint", inner: 0.95, segments: 20);
        p.Add(Box(P(-0.22, -0.22, 0), P(0.22, 0.22, 0.16), hidden: true)); // (the collar's solid: it stands on the plug's face, its sockets on its underside)
        Hidden(p, -0.3, -0.3, 0.16, 0.3, 0.3, len);
        p.Sock("mount", P(0, 0, 0), "-z");
        p.Sock("train", P(0, 0, 0), "-z"); // (the collar's back through the plug's hole: a Reaction Chamber's front welds here)
        p.Set(("thruster", Kw(("force", NozzleForce), ("spool", 1.0), ("exhaust", "+z"), ("at", Py.L(0, 0, len)), ("burnRate", Py.Round(NozzleForce / 1e6 * Burn, 4)), ("gimbal", 3), ("gimbalRate", 10))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, len)), ("dir", "+z"), ("radius", Py.Round(mouth * 0.9, 3)), ("length", Py.Round(mouth * 4, 3)), ("kind", "Rocket")))));
        p.Set(("works", Kw(("needs", Py.L("core", "injector")))), ("collider", "boxes"));
        return p.Stats(300, 1500, 25);
    }

    // ---- the pieces -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The Reaction Chamber XS: the hot section, welded to the bell's collar and running back through the plug's hole. Bare steel between
    /// three copper field coils that glow red to white with the burn, two quartz sight ports and two dials on its service side, cladding
    /// and bolted flanges at both ends. It serves 240 kN of bell.
    /// </summary>
    private static Part ReactionChamber()
    {
        var d = ChamberD;
        var p = new Part("Reaction Chamber XS", Small, Section,
            "A torch drive's reaction chamber, 0.6 m across and 1.1 m long: weld it to a Drive Nozzle XS's collar (its train socket), on the bell's axis, and its Fuel Injector to its back. It serves 240 kN of bell. Its coils glow with the burn and fade as it cools.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Cyl(p, 0, 0, FlangeR - 0.004, -0.07, -0.003, Trim, "dirty steel", segments: 24); // (the front flange, to the bell's collar)
        Bolts(p, FlangeR - 0.03, -0.003 - 0.012, -0.001, 8, Math.PI / 8);
        Cyl(p, 0, 0, Bore, -0.067, -d + 0.067, Chamber, "steel", segments: 24); // (the chamber: bare steel, its ends inside its flanges)
        Cyl(p, 0, 0, FlangeR, -d + 0.07, -d + 0.003, Trim, "dirty steel", segments: 24); // (the back flange, for the injector)
        Bolts(p, FlangeR - 0.03, -d + 0.001, -d + 0.016, 8, Math.PI / 8);
        // Three field coils: copper windings that glow with the chamber's heat (the drive looks), each between two dark retaining rings.
        var n = 0;
        foreach (var zf in new[] { 0.22, 0.45, 0.68 })
        {
            var z = -d * zf;
            var rc = Bore + 0.026 + (n++ % 2) * 0.004; // (neighbours a little different: no shared faces)
            Cyl(p, 0, 0, rc, z - 0.05, z + 0.05, Coil, "copper", inner: Math.Round(Bore / rc, 3), segments: 24, work: "glow");
            foreach (var e in new[] { -1, 1 })
                Cyl(p, 0, 0, rc + 0.012, z + e * 0.05 - 0.012, z + e * 0.05 + 0.012, Dark, "dirty steel", inner: Math.Round((rc - 0.004) / (rc + 0.012), 3), segments: 24);
        }
        // Cladding behind the last coil, two panels with a seam, and a hazard band by the front flange.
        var rk = Bore + 0.014;
        Cyl(p, 0, 0, rk, -d + 0.08, -d * 0.86 - 0.008, Clad, "painted plating", inner: Math.Round(Bore / rk, 3), segments: 24);
        Cyl(p, 0, 0, rk + 0.002, -d * 0.86 + 0.008, -d * 0.76, Clad, "painted plating", inner: Math.Round(Bore / (rk + 0.002), 3), segments: 24);
        Cyl(p, 0, 0, Bore + 0.008, -0.1, -0.08, Hazard, "hazard paint", inner: Math.Round(Bore / (Bore + 0.008), 3), segments: 24);
        // The service side (+x): two quartz sight ports into the chamber, glowing with the burn, and two dials on a board.
        foreach (var zf in new[] { 0.335, 0.565 })
        {
            var z = -d * zf;
            Look(p, Bore - 0.02, -0.06, z - 0.06, Bore + 0.03, 0.06, z + 0.06, Dark, "dirty steel"); // (the port's frame)
            Look(p, Bore + 0.026, -0.04, z - 0.04, Bore + 0.045, 0.04, z + 0.04, Quartz, "", work: "glow"); // (its quartz)
        }
        var gz = -d * 0.81;
        Look(p, Bore - 0.01, 0.06, gz - 0.09, Bore + 0.05, 0.2, gz + 0.09, Dark, "painted plating"); // (the dial board)
        foreach (var dz in new[] { -0.045, 0.045 })
        {
            Look(p, Bore + 0.05, 0.1, gz + dz - 0.035, Bore + 0.058, 0.17, gz + dz + 0.035, "#d8d4c8", "painted plating"); // (a dial's face)
            Look(p, Bore + 0.058, 0.13, gz + dz - 0.004, Bore + 0.064, 0.165, gz + dz + 0.004, "#c03020", "", work: "spin", axis: "+x"); // (its needle, which sweeps while it burns)
        }
        // A cable from the dial board along the underside, and two feed pipes along the top from the injector's end.
        Look(p, -0.03, -Bore - 0.05, -d + 0.1, 0.03, -Bore - 0.01, -0.12, Cable, "rubber");
        foreach (var x in new[] { -0.09, 0.09 })
        {
            Cyl(p, x, Bore + 0.05, 0.025, -d + 0.09, -0.12, Copper, "copper", segments: 8);
            foreach (var z in new[] { -d + 0.14, -0.17 })
                Look(p, x - 0.018, Bore - 0.01, z - 0.018, x + 0.018, Bore + 0.03, z + 0.018, Copper, "copper");
        }
        TrainSolid(p, d);
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        p.Set(("worksPiece", Kw(("kind", "core"), ("serves", ChamberServes), ("bore", Across))));
        return p.Stats(350, 1500, 30);
    }

    /// <summary>
    /// The Fuel Injector XS: the feed section behind the chamber, where the engine's fuel goes in. A manifold ring of eight feed stubs,
    /// each with a lamp (steady green fed and idle, running round the ring with the burn, dark unfed), four valve stems that stroke
    /// while it sprays, a sight glass, and its Fuel in on a boss on top. It holds a buffer: a bell whose pipe is cut runs on it, then stops.
    /// </summary>
    private static Part Injector()
    {
        var d = InjectorD;
        var p = new Part("Fuel Injector XS", Small, Section,
            "A torch drive's fuel injector, 0.3 m long: weld it to a Reaction Chamber XS's back. A fuel pipe into its Fuel in, from a working tank, feeds the drive; a tank that merely touches the engine does not. It holds 10 s of full burn: cut its pipe and the drive runs that long, then stops. Its lamps run round the ring with the burn. It is full of fuel, and burns when it is holed.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Cyl(p, 0, 0, FlangeR - 0.003, -0.05, -0.003, Trim, "dirty steel", segments: 24); // (the front flange, 3 mm under the chamber's back)
        Cyl(p, 0, 0, Bore - 0.01, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the body)
        Cyl(p, 0, 0, FlangeR - 0.006, -d + 0.05, -d + 0.003, Trim, "dirty steel", segments: 24); // (the back flange: a plate that ends the train, or meets the next piece)
        Cyl(p, 0, 0, 0.12, -d + 0.006, -d - 0.02, Dark, "dirty steel", segments: 16); // (its end boss)
        Bolts(p, FlangeR - 0.03, -0.015, -0.001, 8);
        // The manifold ring and its feed stubs, each with its lamp outside it.
        var (zm, rm) = (-d * 0.5, Bore + 0.02);
        Cyl(p, 0, 0, rm, zm - 0.045, zm + 0.045, Dark, "dirty steel", inner: Math.Round((Bore - 0.012) / rm, 3), segments: 24);
        const int stubs = 8;
        for (var k = 0; k < stubs; k++)
        {
            var a = k * 2 * Math.PI / stubs + Math.PI / stubs;
            var (cx, cy) = (Math.Cos(a), Math.Sin(a));
            var (x0, y0) = ((rm - 0.004) * cx, (rm - 0.004) * cy);
            var (x1, y1) = ((rm + 0.035) * cx, (rm + 0.035) * cy);
            Look(p, Math.Min(x0, x1) - 0.014, Math.Min(y0, y1) - 0.014, zm - 0.014, Math.Max(x0, x1) + 0.014, Math.Max(y0, y1) + 0.014, zm + 0.014, Copper, "copper"); // (a feed stub)
            var (lx, ly) = ((rm + 0.045) * cx, (rm + 0.045) * cy);
            Look(p, lx - 0.012, ly - 0.012, zm - 0.034, lx + 0.012, ly + 0.012, zm - 0.018, Lamp, "", work: "chase"); // (its lamp, beside the stub)
        }
        // Four valve stems on the back half, between the stubs, that stroke 8 mm while it sprays.
        for (var k = 0; k < 4; k++)
        {
            var a = k * Math.PI / 2;
            var (cx, cy) = (Math.Cos(a), Math.Sin(a));
            var (x, y) = ((Bore + 0.01) * cx, (Bore + 0.01) * cy);
            var ax = Math.Abs(cx) > 0.5 ? (cx > 0 ? "+x" : "-x") : (cy > 0 ? "+y" : "-y");
            Look(p, x - 0.012, y - 0.012, -d * 0.82, x + 0.012, y + 0.012, -d * 0.7, Bolt, "steel", work: "stroke", travel: 0.008, axis: ax);
        }
        // The Fuel in's boss on top, its flanged mouth, a hazard collar; a sight glass on the service side.
        Look(p, -0.06, Bore - 0.02, zm - 0.1, 0.06, Bore + 0.07, zm + 0.1, Dark, "dirty steel");
        Cyl(p, 0, Bore + 0.06, 0.045, zm - 0.045, zm + 0.045, Hazard, "hazard paint", segments: 12);
        Look(p, -0.05, Bore + 0.07, zm - 0.05, 0.05, Bore + 0.085, zm + 0.05, Trim, "dirty steel");
        Look(p, Bore - 0.01, -0.12, zm - 0.05, Bore + 0.03, -0.06, zm + 0.05, Dark, "dirty steel"); // (the sight glass's frame...)
        Look(p, Bore + 0.026, -0.11, zm - 0.035, Bore + 0.045, -0.07, zm + 0.035, Quartz, "", work: "glow"); // (...and its glass, lit by the spray)
        TrainSolid(p, d);
        p.Add(Box(P(-0.05, Bore - 0.02, zm - 0.09), P(0.05, Bore + 0.085, zm + 0.09), hidden: true)); // (the boss: a pipe's end has a face to meet)
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        p.Set(("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, Py.Round(Bore + 0.085, 3), Py.Round(zm, 3)], "+y", "fuel"))));
        p.Set(("worksPiece", Kw(("kind", "injector"), ("serves", InjectorServes), ("bore", Across))));
        return p.Stats(120, 700, 20);
    }

    // ---- function pieces (phase 3) ---------------------------------------------------------------------------------

    public const double SpacerD = 0.4, BufferD = 0.5, JacketD = 0.6, IgniterD = 0.35, SensorD = 0.35, ValveD = 0.4, MixerD = 0.4, ReheatD = 0.4, FilterD = 0.35, GimbalD = 0.4, CollarD = 0.35, SelectorD = 0.4, AdapterD = 0.7;
    public const double GimbalDegrees = 6; // (the doc: the bell swings up to 6 degrees)

    /// <summary>A function piece's flanges: front 4 mm under the flange it meets, back at the flange radius, each 3 mm short of its face.</summary>
    private static void Flanges(Part p, double d)
    {
        Cyl(p, 0, 0, FlangeR - 0.004, -0.05, -0.003, Trim, "dirty steel", segments: 24);
        Bolts(p, FlangeR - 0.03, -0.015, -0.001, 8, Math.PI / 8);
        Cyl(p, 0, 0, FlangeR - 0.001, -d + 0.05, -d + 0.003, Trim, "dirty steel", segments: 24);
        Bolts(p, FlangeR - 0.03, -d + 0.001, -d + 0.015, 8);
    }

    private static void TrainPiece(Part p, double d, string function)
    {
        TrainSolid(p, d);
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        p.Set(("worksPiece", Kw(("kind", "train"), ("function", function), ("bore", Across))));
    }

    /// <summary>
    /// The Train Spacer XS: an empty shell that fills length in an engine train, any zone. Plain cladding between its flanges, two
    /// pass-through pipes along its top and a cable along its underside, so the lines of the pieces either side run on across it.
    /// </summary>
    private static Part Spacer()
    {
        var d = SpacerD;
        var p = new Part("Train Spacer XS", Small, Section,
            "An empty engine section, 0.4 m long: weld it anywhere in an engine train to give it length (to reach a socket, or to space pieces). It adds nothing else, and carries the train's pipes and cables across.",
            role: "frame", tool: "welder", material: "steel", color: Clad, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.004, -0.047, -d + 0.047, Clad, "painted plating", segments: 24); // (the shell)
        Cyl(p, 0, 0, Bore + 0.008, -d * 0.5 - 0.02, -d * 0.5 + 0.02, Hazard, "hazard paint", inner: Math.Round((Bore - 0.01) / (Bore + 0.008), 3), segments: 24); // (its stripe)
        foreach (var x in new[] { -0.09, 0.09 })
            Cyl(p, x, Bore + 0.03, 0.022, -d + 0.052, -0.052, Copper, "copper", segments: 8); // (pass-through pipes)
        Look(p, -0.03, -Bore - 0.035, -d + 0.06, 0.03, -Bore + 0.002, -0.06, Cable, "rubber"); // (the cable run)
        TrainPiece(p, d, "spacer");
        return p.Stats(60, 600, 20);
    }

    /// <summary>
    /// The Fuel Buffer XS: a feed piece, a fat tank ring round the train that holds 15 s more of full burn for a cut line, each one adds.
    /// Six lamps round its band run with the burn as the injector's do (steady green fed, dark unfed), a sight glass glows with the spray,
    /// and a dial's needle sweeps. It is full of fuel, and burns when it is holed.
    /// </summary>
    private static Part Buffer()
    {
        var d = BufferD;
        var p = new Part("Fuel Buffer XS", Small, Section,
            "A fuel buffer for an engine train, 0.5 m long: weld it in the feed zone (behind the chambers, with the injectors). Each one holds 15 s more of full burn: cut the train's fuel pipe and the drive runs that much longer before it stops. It is full of fuel, and burns when it is holed.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.01, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var rt = FlangeR - 0.012;
        Cyl(p, 0, 0, rt, -0.07, -d + 0.07, "#6a5a30", "painted plating", inner: Math.Round((Bore - 0.012) / rt, 3), segments: 24); // (the tank ring)
        foreach (var z in new[] { -0.09, -d + 0.09 })
            Cyl(p, 0, 0, rt + 0.006, z - 0.012, z + 0.012, Dark, "dirty steel", inner: Math.Round((rt - 0.004) / (rt + 0.006), 3), segments: 24); // (its straps)
        Cyl(p, 0, 0, rt + 0.003, -d * 0.5 - 0.03, -d * 0.5 + 0.03, Hazard, "hazard paint", inner: Math.Round((rt - 0.004) / (rt + 0.003), 3), segments: 24); // (the fuel band)
        for (var k = 0; k < 6; k++)
        {
            var a = Math.PI / 6 + k * Math.PI / 3;
            var (x, y) = ((rt + 0.012) * Math.Cos(a), (rt + 0.012) * Math.Sin(a));
            Look(p, x - 0.012, y - 0.012, -d * 0.5 - 0.06, x + 0.012, y + 0.012, -d * 0.5 - 0.042, Lamp, "", work: "chase"); // (a level lamp)
        }
        Look(p, rt - 0.01, -0.05, -d * 0.5 + 0.05, rt + 0.025, 0.05, -d * 0.5 + 0.13, Dark, "dirty steel"); // (the sight glass's frame...)
        Look(p, rt + 0.02, -0.035, -d * 0.5 + 0.065, rt + 0.04, 0.035, -d * 0.5 + 0.115, Quartz, "", work: "glow"); // (...and its glass)
        Look(p, -0.07, rt - 0.01, -d * 0.5 + 0.05, 0.07, rt + 0.02, -d * 0.5 + 0.17, Dark, "painted plating"); // (the dial board, on top)
        Look(p, -0.045, rt + 0.02, -d * 0.5 + 0.075, 0.045, rt + 0.028, -d * 0.5 + 0.145, "#d8d4c8", "painted plating"); // (its face)
        Look(p, -0.004, rt + 0.028, -d * 0.5 + 0.08, 0.004, rt + 0.034, -d * 0.5 + 0.13, "#c03020", "", work: "spin", axis: "+y"); // (its needle)
        TrainPiece(p, d, "buffer");
        return p.Stats(150, 700, 20);
    }

    /// <summary>
    /// The Cooling Jacket XS: a hot piece, a finned jacket round the chamber line with coolant pipes in and out; the train burns 10% less
    /// fuel at the same push. Its fins glow dull red to orange with the chamber's heat.
    /// </summary>
    private static Part Jacket()
    {
        var d = JacketD;
        var p = new Part("Cooling Jacket XS", Small, Section,
            "A cooling jacket for an engine train, 0.6 m long: weld it in the hot zone (with the chambers, before the injectors). The train burns 10% less fuel at the same push (once a train). Its fins glow with the chamber's heat.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.006, -0.047, -d + 0.047, Chamber, "steel", segments: 24); // (the liner)
        var n = 0;
        for (var z = -0.08; z > -d + 0.08; z -= 0.044)
        {
            var rf = FlangeR - 0.01 - (n % 2) * 0.004; // (neighbours a little different)
            Cyl(p, 0, 0, rf, z - 0.008, z + 0.008, n % 3 == 1 ? Coil : Dark, "dirty steel", inner: Math.Round((Bore - 0.008) / rf, 3), segments: 24, work: n % 3 == 1 ? "glow" : null); // (a fin; every third glows with the heat)
            n++;
        }
        // Coolant in and out: two pipes along the top to elbows, and a header box between them.
        foreach (var (x, zz) in new[] { (-0.1, -0.12), (0.1, -d + 0.12) })
        {
            Cyl(p, x, FlangeR + 0.02, 0.025, -d + 0.06, -0.06, Copper, "copper", segments: 8);
            Look(p, x - 0.03, FlangeR - 0.012, zz - 0.03, x + 0.03, FlangeR + 0.05, zz + 0.03, Copper, "copper");
        }
        Look(p, -0.06, FlangeR - 0.025, -d * 0.5 - 0.07, 0.06, FlangeR + 0.06, -d * 0.5 + 0.07, Dark, "painted plating"); // (the header)
        Look(p, -0.04, FlangeR + 0.06, -d * 0.5 - 0.02, 0.04, FlangeR + 0.075, -d * 0.5 + 0.02, Hazard, "hazard paint"); // (its cap)
        TrainPiece(p, d, "jacket");
        return p.Stats(180, 1200, 30);
    }

    /// <summary>
    /// The Igniter XS: a feed piece, a short collar with four spark plugs set radially into the train's bore and their leads run to a coil
    /// box on top. With one in its train a drive lights at once; without, a bell that needs an injector takes 3 s to light. Its plug tips
    /// glow with the burn, the coil's lamps run with it, and it throws a burst of sparks at light-off.
    /// </summary>
    private static Part Igniter()
    {
        var d = IgniterD;
        var p = new Part("Igniter XS", Small, Section,
            "An igniter for an engine train, 0.35 m long: weld it in the feed zone (behind the chambers, with the injectors). The drive lights the moment it is asked; without one it takes 3 s to light from cold.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.008, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var rc = FlangeR - 0.02;
        Cyl(p, 0, 0, rc, -0.08, -d + 0.08, Dark, "dirty steel", inner: Math.Round((Bore - 0.01) / rc, 3), segments: 24); // (the plug collar)
        Cyl(p, 0, 0, rc + 0.004, -d * 0.5 - 0.012, -d * 0.5 + 0.012, Hazard, "hazard paint", inner: Math.Round((rc - 0.004) / (rc + 0.004), 3), segments: 24); // (its band)
        var zm = -d * 0.5;
        for (var k = 0; k < 4; k++)
        {
            // A plug at each diagonal: its steel boss on the collar, a white insulator standing out, a cap and a lead.
            var a = Math.PI / 4 + k * Math.PI / 2;
            var (cx, cy) = (Math.Cos(a), Math.Sin(a));
            void At(double r0, double r1, double w, double z0, double z1, string col, string fin, string? work = null)
            {
                var (x0, y0, x1, y1) = (cx * r0, cy * r0, cx * r1, cy * r1);
                Look(p, Math.Min(x0, x1) - w, Math.Min(y0, y1) - w, z0, Math.Max(x0, x1) + w, Math.Max(y0, y1) + w, z1, col, fin, work: work);
            }
            At(rc - 0.004, rc + 0.025, 0.022, zm - 0.03, zm + 0.03, Trim, "steel"); // (the boss)
            At(rc + 0.025, rc + 0.075, 0.013, zm - 0.015, zm + 0.015, "#e4e0d6", "painted plating"); // (the insulator)
            At(rc + 0.075, rc + 0.09, 0.009, zm - 0.009, zm + 0.009, Bolt, "steel"); // (the cap)
            At(Bore - 0.03, Bore - 0.012, 0.01, zm - 0.01, zm + 0.01, Throat, "", work: "glow"); // (the tip in the bore, glowing with the burn)
        }
        // The coil box on top, its two lamps, and the leads down to the upper plugs.
        Look(p, -0.07, rc + 0.03, zm - 0.07, 0.07, rc + 0.1, zm + 0.07, Dark, "painted plating");
        Look(p, -0.045, rc + 0.1, zm - 0.045, 0.045, rc + 0.112, zm + 0.045, "#2a2c2e", "rubber"); // (its lid)
        foreach (var x in new[] { -0.03, 0.03 })
            Look(p, x - 0.01, rc + 0.112, zm + 0.02, x + 0.01, rc + 0.124, zm + 0.04, Lamp, "", work: "chase");
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var px = sx * (rc + 0.08) * Math.Cos(Math.PI / 4);
            Look(p, Math.Min(sx * 0.07, px) - 0.006, rc + 0.055, zm - 0.006, Math.Max(sx * 0.07, px) + 0.006, rc + 0.067, zm + 0.006, Cable, "rubber"); // (a lead)
        }
        TrainPiece(p, d, "igniter");
        return p.Stats(70, 500, 20);
    }

    /// <summary>
    /// The Sensor Ring XS: a supply piece, a thin instrument ring with four gauges, one for each of its data outputs (the drive's level,
    /// its buffer, its health and its heat), and a junction box where the data cables plug in. The gauges' needles sweep with the heat
    /// and its four lamps run with the burn.
    /// </summary>
    private static Part SensorRing()
    {
        var d = SensorD;
        var p = new Part("Sensor Ring XS", Small, Section,
            "An instrument ring for an engine train, 0.35 m long: weld it in the supply zone (at the back, with the pumps). Its four data outputs give the drive's level (%), buffer (seconds), health (% of its weakest piece) and heat (%), for gauges, displays and logic.",
            role: "frame", tool: "welder", material: "steel", color: Clad, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.004, -0.047, -d + 0.047, Clad, "painted plating", segments: 24); // (the shell)
        var rr = FlangeR - 0.016;
        Cyl(p, 0, 0, rr, -0.07, -d + 0.07, Steel, "painted plating", inner: Math.Round((Bore - 0.006) / rr, 3), segments: 24); // (the ring)
        var zm = -d * 0.5;
        for (var k = 0; k < 4; k++)
        {
            // A gauge at each side: its bezel, face and needle, and a lamp beside it.
            var a = k * Math.PI / 2;
            var (cx, cy) = (Math.Cos(a), Math.Sin(a));
            var axis = k switch { 0 => "+x", 1 => "+y", 2 => "-x", _ => "-y" };
            void Radial(double r0, double r1, double w, double h, string col, string fin, string? work = null, string? ax = null)
            {
                var (x0, y0, x1, y1) = (cx * r0, cy * r0, cx * r1, cy * r1);
                var (wx, wy) = (Math.Abs(cy) * w + 0.0005, Math.Abs(cx) * w + 0.0005);
                Look(p, Math.Min(x0, x1) - wx, Math.Min(y0, y1) - wy, zm - h, Math.Max(x0, x1) + wx, Math.Max(y0, y1) + wy, zm + h, col, fin, work: work, axis: ax);
            }
            Radial(rr - 0.006, rr + 0.02, 0.05, 0.05, Dark, "painted plating"); // (the bezel)
            Radial(rr + 0.02, rr + 0.026, 0.038, 0.038, "#d8d4c8", "painted plating"); // (the face)
            Radial(rr + 0.026, rr + 0.032, 0.004, 0.026, "#c03020", "", work: "spin", ax: axis); // (the needle)
            var b = a + Math.PI / 4;
            var (lx, ly) = ((rr + 0.008) * Math.Cos(b), (rr + 0.008) * Math.Sin(b));
            Look(p, lx - 0.012, ly - 0.012, zm - 0.012, lx + 0.012, ly + 0.012, zm + 0.012, Lamp, "", work: "chase"); // (a lamp)
        }
        // The junction box under the ring, and its four data cables' plugs.
        Look(p, -0.08, -rr - 0.06, zm - 0.06, 0.08, -rr + 0.004, zm + 0.06, Dark, "painted plating");
        for (var k = 0; k < 4; k++)
        {
            var x = -0.054 + k * 0.036;
            Look(p, x - 0.012, -rr - 0.075, zm - 0.012, x + 0.012, -rr - 0.06, zm + 0.012, Cable, "rubber");
        }
        TrainPiece(p, d, "sensor");
        return p.Stats(50, 400, 20);
    }

    /// <summary>A function piece's one percent setting (the configure tool), on CfgKey.Strength.</summary>
    private static void Percent(Part p, string label, double min, double max, double step, double dflt, string tip) =>
        p.Set(("config", Py.L(Kw(("key", "strength"), ("label", label), ("group", "Engine"), ("unit", "%"), ("min", min), ("max", max), ("step", step), ("default", dflt), ("tooltip", tip)))));

    /// <summary>
    /// The Throttle Valve XS: a feed piece, a valve body round the train with a handwheel on top and an actuator whose rod strokes with
    /// the spray. It throttles its drive finely, down to 5% in 2% steps: unwired, its Level setting holds the drive at most there; a wire
    /// into its Throttle in sets the level. Its output is the drive's level in %.
    /// </summary>
    private static Part ThrottleValve()
    {
        var d = ValveD;
        var p = new Part("Throttle Valve XS", Small, Section,
            "A throttle valve for an engine train, 0.4 m long: weld it in the feed zone (behind the chambers, with the injectors). It throttles the drive finely, down to 5% in 2% steps: set its Level to hold the drive there, or wire a lever into it to set the level. It puts out the drive's level in %.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.008, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var rb = FlangeR - 0.018;
        Cyl(p, 0, 0, rb, -0.09, -d + 0.09, "#3a4a5a", "painted plating", inner: Math.Round((Bore - 0.01) / rb, 3), segments: 24); // (the valve body)
        Cyl(p, 0, 0, rb + 0.004, -d * 0.5 - 0.015, -d * 0.5 + 0.015, Hazard, "hazard paint", inner: Math.Round((rb - 0.004) / (rb + 0.004), 3), segments: 24); // (its band)
        var zm = -d * 0.5;
        // The bonnet on top, its stem and a handwheel (spokes and rim) that turns with the drive.
        Look(p, -0.05, rb - 0.01, zm - 0.05, 0.05, rb + 0.05, zm + 0.05, Dark, "dirty steel");
        Look(p, -0.012, rb + 0.05, zm - 0.012, 0.012, rb + 0.11, zm + 0.012, Bolt, "steel"); // (the stem)
        Look(p, -0.09, rb + 0.112, -0.012 + zm, 0.09, rb + 0.124, 0.012 + zm, "#a02818", "painted plating", work: "spin", axis: "+y"); // (a spoke)
        Look(p, -0.012, rb + 0.1125, zm - 0.09, 0.012, rb + 0.1235, zm + 0.09, "#a02818", "painted plating", work: "spin", axis: "+y"); // (the other)
        foreach (var (x0, z0, x1, z1) in new[] { (-0.1, -0.1, 0.1, -0.08), (-0.1, 0.08, 0.1, 0.1), (-0.1, -0.08, -0.08, 0.08), (0.08, -0.08, 0.1, 0.08) })
            Look(p, x0, rb + 0.114, zm + z0, x1, rb + 0.126, zm + z1, "#a02818", "painted plating", work: "spin", axis: "+y"); // (the rim)
        // The actuator at the side: a cylinder box, its rod that strokes, and a lamp.
        Look(p, rb - 0.01, -0.04, zm - 0.1, rb + 0.06, 0.04, zm + 0.02, Dark, "painted plating");
        Look(p, rb + 0.015, -0.01, zm + 0.02, rb + 0.035, 0.01, zm + 0.09, Bolt, "steel", work: "stroke", travel: 0.03, axis: "+z"); // (the rod)
        Look(p, rb + 0.06, -0.012, zm - 0.07, rb + 0.072, 0.012, zm - 0.046, Lamp, "", work: "chase");
        TrainPiece(p, d, "throttle");
        Percent(p, "Level", 5, 100, 2, 100, "Holds the drive at most at this level, in 2% steps down to 5%. A wire into the valve sets the level instead.");
        return p.Stats(110, 600, 20);
    }

    /// <summary>
    /// The Mixture Controller XS: a feed piece, a mixing ring with a big dial whose needle shows the heat, two fuel lines that loop into
    /// its body and a lamp. One dial (or a wire into Mix in, -1 to 1) trades push for burn: -100 is 80% push at 70% burn, +100 is 110%
    /// push at 130% burn.
    /// </summary>
    private static Part MixtureController()
    {
        var d = MixerD;
        var p = new Part("Mixture Controller XS", Small, Section,
            "A mixture controller for an engine train, 0.4 m long: weld it in the feed zone (behind the chambers, with the injectors). Its Mix dial trades push for burn: lean (-100) is 80% push at 70% of the fuel, rich (+100) is 110% push at 130% of the fuel. A wire into it (-1 to 1) sets the mix instead.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.008, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var rm = FlangeR - 0.014;
        Cyl(p, 0, 0, rm, -0.08, -d + 0.08, "#4a5a3a", "painted plating", inner: Math.Round((Bore - 0.01) / rm, 3), segments: 24); // (the mixing ring)
        foreach (var z in new[] { -0.1, -d + 0.1 })
            Cyl(p, 0, 0, rm + 0.006, z - 0.01, z + 0.01, Dark, "dirty steel", inner: Math.Round((rm - 0.004) / (rm + 0.006), 3), segments: 24); // (its straps)
        var zm = -d * 0.5;
        // The dial on top: board, face, needle; a lamp beside it.
        Look(p, -0.08, rm - 0.01, zm - 0.07, 0.08, rm + 0.02, zm + 0.07, Dark, "painted plating");
        Look(p, -0.06, rm + 0.02, zm - 0.055, 0.06, rm + 0.028, zm + 0.055, "#d8d4c8", "painted plating");
        Look(p, -0.004, rm + 0.028, zm - 0.045, 0.004, rm + 0.034, zm + 0.01, "#c03020", "", work: "spin", axis: "+y"); // (the needle)
        Look(p, 0.065, rm + 0.02, zm + 0.04, 0.077, rm + 0.032, zm + 0.064, Lamp, "", work: "chase");
        // Two fuel lines looping over the sides into the ring.
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            var x = sx * (rm + 0.025);
            Look(p, x - 0.016, -0.1, zm - 0.09, x + 0.016, 0.1, zm - 0.06, Copper, "copper");
            Look(p, x - 0.016, -0.1, zm + 0.06, x + 0.016, 0.1, zm + 0.09, Copper, "copper");
            Look(p, x - 0.02, -0.03, zm - 0.06, x + 0.02, 0.03, zm + 0.06, Dark, "dirty steel"); // (the mixing block)
        }
        TrainPiece(p, d, "mixture");
        Percent(p, "Mix", -100, 100, 5, 0, "Lean (-100): 80% push at 70% of the fuel. Rich (+100): 110% push at 130% of the fuel. A wire into it (-1 to 1) sets the mix instead.");
        return p.Stats(100, 600, 20);
    }

    /// <summary>
    /// The Reheat Ring XS: a hot piece, a ring of spray bars round the chamber line under a ribbed hot shroud. On Boost in the drive pushes
    /// 25% more at twice the burn for up to 20 s, then needs 40 s to recover. Its shroud's ribs and spray bars glow with the heat.
    /// </summary>
    private static Part ReheatRing()
    {
        var d = ReheatD;
        var p = new Part("Reheat Ring XS", Small, Section,
            "A reheat ring for an engine train, 0.4 m long: weld it in the hot zone (with the chambers, before the injectors) and wire a button or a lever into it. While Boost in is on the drive pushes 25% more at twice the fuel, for up to 20 s; then it needs 40 s to recover.",
            role: "frame", tool: "welder", material: "steel", color: Chamber, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.006, -0.047, -d + 0.047, Chamber, "steel", segments: 24); // (the liner)
        var rs = FlangeR - 0.012;
        Cyl(p, 0, 0, rs, -0.07, -d + 0.07, Dark, "dirty steel", inner: Math.Round((Bore - 0.008) / rs, 3), segments: 24); // (the shroud)
        var n = 0;
        for (var z = -0.1; z > -d + 0.1; z -= 0.05)
        {
            var rr = rs + 0.008 + (n % 2) * 0.004; // (neighbours a little different)
            Cyl(p, 0, 0, rr, z - 0.009, z + 0.009, Coil, "dirty steel", inner: Math.Round((rs - 0.006) / rr, 3), segments: 24, work: "glow"); // (a rib, glowing with the heat)
            n++;
        }
        // Eight spray bars along the shroud, each with its feed stub, and a boost lamp box on top.
        for (var k = 0; k < 8; k++)
        {
            var a = Math.PI / 8 + k * Math.PI / 4;
            var (x, y) = ((rs + 0.03) * Math.Cos(a), (rs + 0.03) * Math.Sin(a));
            Look(p, x - 0.011, y - 0.011, -d + 0.08, x + 0.011, y + 0.011, -0.08, Throat, "", work: "glow"); // (a spray bar)
        }
        Look(p, -0.05, rs + 0.045, -d * 0.5 - 0.05, 0.05, rs + 0.075, -d * 0.5 + 0.05, Dark, "painted plating");
        foreach (var x in new[] { -0.025, 0.025 })
            Look(p, x - 0.01, rs + 0.075, -d * 0.5 - 0.01, x + 0.01, rs + 0.087, -d * 0.5 + 0.01, Lamp, "", work: "chase");
        TrainPiece(p, d, "reheat");
        return p.Stats(160, 900, 30);
    }

    /// <summary>
    /// The Fuel Filter XS: a feed piece, two filter canisters on a manifold band round the train, a drain cock and a pressure dial whose
    /// needle sweeps with the heat. Breakdowns land on its train half as often.
    /// </summary>
    private static Part FuelFilter()
    {
        var d = FilterD;
        var p = new Part("Fuel Filter XS", Small, Section,
            "A fuel filter for an engine train, 0.35 m long: weld it in the feed zone (behind the chambers, with the injectors). Breakdowns land on its train half as often.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.008, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var rb = FlangeR - 0.02;
        Cyl(p, 0, 0, rb, -0.075, -d + 0.075, "#5a4a30", "painted plating", inner: Math.Round((Bore - 0.01) / rb, 3), segments: 24); // (the manifold band)
        var zm = -d * 0.5;
        foreach (var sx in new[] { -1.0, 1.0 })
        {
            // A canister standing on the band's side: its body, cap band and drain cock.
            var x = sx * 0.11;
            Look(p, x - 0.045, rb - 0.01, zm - 0.045, x + 0.045, rb + 0.13, zm + 0.045, "#2e4a3a", "painted plating");
            Look(p, x - 0.05, rb + 0.13, zm - 0.05, x + 0.05, rb + 0.15, zm + 0.05, Dark, "dirty steel");
            Look(p, x - 0.008, rb + 0.15, zm - 0.008, x + 0.008, rb + 0.17, zm + 0.008, Bolt, "steel");
        }
        Look(p, -0.03, rb + 0.01, zm - 0.03, 0.03, rb + 0.07, zm + 0.03, Copper, "copper"); // (the manifold between them)
        Look(p, -0.07, -rb - 0.02, zm - 0.06, 0.07, -rb + 0.01, zm + 0.06, Dark, "painted plating"); // (the dial board, under)
        Look(p, -0.045, -rb - 0.028, zm - 0.045, 0.045, -rb - 0.02, zm + 0.045, "#d8d4c8", "painted plating");
        Look(p, -0.004, -rb - 0.034, zm - 0.035, 0.004, -rb - 0.028, zm + 0.01, "#c03020", "", work: "spin", axis: "-y");
        TrainPiece(p, d, "filter");
        return p.Stats(90, 600, 20);
    }

    /// <summary>
    /// The Gimbal Ring XS: a hot piece by the bell, two rings on crossed trunnion pins (the outer one on the train, the inner one on the
    /// bell's line) and a pitch and a yaw actuator whose rods stroke with the drive. The bell swings up to 6 degrees (a Drive Nozzle's own
    /// gimbal 3) under the flight control or the bell's gimbal wires; it takes no wires itself, and its Throw, Expo and Reverse take over.
    /// </summary>
    private static Part GimbalRing()
    {
        var d = GimbalD;
        var p = new Part("Gimbal Ring XS", Small, Section,
            "A gimbal ring for an engine train, 0.4 m long: weld it in the hot zone, next to the bell. The bell swings up to 6 degrees (a Drive Nozzle's own gimbal swings 3), under the Flight Stabilizer or the bell's gimbal wires, so the drive steers harder. Its Throw, Expo and Reverse settings take over from the bell's.",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.006, -0.047, -d + 0.047, Chamber, "steel", segments: 24); // (the liner)
        var zm = -d * 0.5;
        var ro = FlangeR + 0.012;
        Cyl(p, 0, 0, ro, zm - 0.045, zm + 0.045, "#3c4650", "painted plating", inner: Math.Round((ro - 0.03) / ro, 3), segments: 24); // (the outer ring)
        var ri = ro - 0.034;
        Cyl(p, 0, 0, ri, zm - 0.06, zm + 0.06, Trim, "dirty steel", inner: Math.Round((Bore + 0.004) / ri, 3), segments: 24); // (the inner ring)
        // The trunnion pins: the outer ring's left and right, the inner ring's top and bottom, each a boss.
        foreach (var sx in new[] { -1.0, 1.0 })
            Look(p, sx > 0 ? ro - 0.004 : -ro - 0.028, -0.025, zm - 0.025, sx > 0 ? ro + 0.028 : -ro + 0.004, 0.025, zm + 0.025, Bolt, "steel");
        foreach (var sy in new[] { -1.0, 1.0 })
            Look(p, -0.022, sy > 0 ? ri - 0.002 : -ri - 0.024, zm - 0.022, 0.022, sy > 0 ? ri + 0.024 : -ri + 0.002, zm + 0.022, Bolt, "steel");
        // Two actuators: a pitch one under, a yaw one at the side, each a cylinder box with a rod that strokes, and a lamp.
        Look(p, -0.03, -ro - 0.07, -d + 0.07, 0.03, -ro - 0.01, zm - 0.05, Dark, "painted plating");
        Look(p, -0.01, -ro - 0.05, zm - 0.05, 0.01, -ro - 0.03, zm + 0.02, Bolt, "steel", work: "stroke", travel: 0.025, axis: "+z"); // (the pitch rod)
        Look(p, ro + 0.04, -0.03, -d + 0.07, ro + 0.1, 0.03, zm - 0.05, Dark, "painted plating");
        Look(p, ro + 0.06, -0.01, zm - 0.05, ro + 0.08, 0.01, zm + 0.02, Bolt, "steel", work: "stroke", travel: 0.025, axis: "+z"); // (the yaw rod)
        Look(p, ro + 0.1, -0.012, -d + 0.09, ro + 0.112, 0.012, -d + 0.114, Lamp, "", work: "chase");
        TrainPiece(p, d, "gimbal");
        p.Set(("worksPiece", Kw(("kind", "train"), ("function", "gimbal"), ("gimbal", GimbalDegrees), ("gimbalRate", 40), ("bore", Across)))); // (how far it swings its bell, degrees, and how fast, degrees a second)
        return p.Stats(140, 700, 25);
    }

    /// <summary>
    /// The Armour Collar XS: a hot piece, a thick ring of bolted armour segments round the chamber line with a lip at each end. Each one
    /// takes 15 more off every hit on its train's bell and hot zone; it is heavy, and its own armour is high.
    /// </summary>
    private static Part ArmourCollar()
    {
        var d = CollarD;
        var p = new Part("Armour Collar XS", Small, Section,
            "An armour collar for an engine train, 0.35 m long: weld it in the hot zone (with the chambers). Each one takes 15 more off every hit on its drive's bell and its hot zone's pieces. It is heavy.",
            role: "frame", tool: "welder", material: "steel", color: "#4a4e48", group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.006, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var ra = FlangeR + 0.03;
        Cyl(p, 0, 0, ra, -0.06, -d + 0.06, "#4a4e48", "dirty steel", inner: Math.Round((Bore - 0.002) / ra, 3), segments: 12); // (the armour ring: twelve flat segments)
        foreach (var z in new[] { -0.055, -d + 0.055 })
            Cyl(p, 0, 0, ra + 0.008, z - 0.008, z + 0.008, Dark, "dirty steel", inner: Math.Round((ra - 0.01) / (ra + 0.008), 3), segments: 12); // (its lips)
        // A bolt head on each segment, and a hazard stripe across the top.
        for (var k = 0; k < 12; k++)
        {
            var a = Math.PI / 12 + k * Math.PI / 6;
            var rb = ra * Math.Cos(Math.PI / 12);
            var (x, y) = (rb * Math.Cos(a), rb * Math.Sin(a));
            Look(p, x - 0.012, y - 0.012, -d * 0.5 - 0.012, x + 0.012, y + 0.012, -d * 0.5 + 0.012, Bolt, "steel");
        }
        TrainPiece(p, d, "collar");
        return p.Stats(260, 1600, 60);
    }

    /// <summary>
    /// The Fuel Selector XS: a supply piece, a valve block on a band round the train with two Fuel in ports (one on top, one on the
    /// left), a valve block with a turning handle between them and two lamps. Its train is fed while either line reaches a working tank: the first,
    /// or with Select in on the second, and else the other; Source out says which (1, 2, or 0 for neither).
    /// </summary>
    private static Part FuelSelector()
    {
        var d = SelectorD;
        var p = new Part("Fuel Selector XS", Small, Section,
            "A fuel selector for an engine train, 0.4 m long: weld it in the supply zone (at the back) and run a fuel pipe into each of its two Fuel ins. The drive runs on the first line, or the second with Select in on, and goes over to the other if one is cut or its tank is lost. Source out says which line feeds it (1 or 2, 0 for none).",
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Flanges(p, d);
        Cyl(p, 0, 0, Bore - 0.008, -0.047, -d + 0.047, Steel, "painted plating", segments: 24); // (the core tube)
        var rb = FlangeR - 0.016;
        Cyl(p, 0, 0, rb, -0.08, -d + 0.08, "#3a4a5a", "painted plating", inner: Math.Round((Bore - 0.01) / rb, 3), segments: 24); // (the band)
        var zm = -d * 0.5;
        // Two inlet stubs (top and left), a valve block between them on the upper left with the handle that turns, and two lamps.
        var pr = Bore + 0.085;
        Look(p, -0.028, rb - 0.01, zm - 0.028, 0.028, pr, zm + 0.028, Copper, "copper"); // (the top inlet's stub)
        Look(p, -pr, -0.028, zm - 0.028, -rb + 0.01, 0.028, zm + 0.028, Copper, "copper"); // (the left inlet's stub)
        var (vx, vy) = (-rb * 0.72, rb * 0.72);
        Look(p, vx - 0.06, vy - 0.06, zm - 0.07, vx + 0.06, vy + 0.06, zm + 0.07, Dark, "dirty steel"); // (the valve block)
        Look(p, vx - 0.012, vy + 0.06, zm - 0.012, vx + 0.012, vy + 0.09, zm + 0.012, Bolt, "steel"); // (the handle's stem)
        Look(p, vx - 0.006, vy + 0.09, zm - 0.012, vx + 0.006, vy + 0.102, zm + 0.11, "#a02818", "painted plating", work: "spin", axis: "+y"); // (the handle)
        foreach (var x in new[] { 0.06, 0.1 })
            Look(p, x - 0.01, rb + 0.004, zm - 0.012, x + 0.01, rb + 0.016, zm + 0.012, Lamp, "", work: "chase");
        TrainPiece(p, d, "selector");
        p.Add(Box(P(-0.04, Bore - 0.02, zm - 0.04), P(0.04, pr, zm + 0.04), hidden: true)); // (the inlets' bosses: a pipe's end has a face to meet)
        p.Add(Box(P(-pr, -0.04, zm - 0.04), P(-Bore + 0.02, 0.04, zm + 0.04), hidden: true));
        p.Set(("ports", Py.L(
            BigDriveParts.PipePort("Fuel in", "in", [0, Py.Round(pr, 3), Py.Round(zm, 3)], "+y", "fuel"),
            BigDriveParts.PipePort("Fuel in 2", "in", [Py.Round(-pr, 3), 0, Py.Round(zm, 3)], "-x", "fuel"))));
        return p.Stats(110, 600, 20);
    }

    /// <summary>
    /// The Bore Adapter XS: a short tapered shell that joins an engine train of twice the XS bore in front to the XS bore behind it (the
    /// doc's "Any bore": a weld joins only equal bores). Welded turned round it steps up instead. A wide bolted flange at its big end with
    /// two lifting lugs and a hazard band, the cone, and the XS flange at its small end; it adds nothing else and goes in any zone.
    /// </summary>
    private static Part BoreAdapter()
    {
        var d = AdapterD;
        var big = Across * 2; // (its front's bore)
        var rb = big * 0.5;
        var p = new Part("Bore Adapter XS", Small, Section,
            $"A tapered engine section, 0.7 m long: it joins train pieces of a {big:0.00} m bore in front to pieces of a {Across:0.00} m bore behind it (weld it turned round to go from {Across:0.00} m up to {big:0.00} m). Train pieces weld only to pieces of the same bore. It adds nothing else and goes in any zone.",
            role: "frame", tool: "welder", material: "steel", color: Clad, group: Group);
        Cyl(p, 0, 0, rb - 0.004, -0.05, -0.003, Trim, "dirty steel", segments: 32); // (the big flange, 4 mm under the flange it meets)
        Bolts(p, rb - 0.03, -0.015, -0.001, 16, Math.PI / 16);
        var cone = rb - 0.04;
        Cyl(p, 0, 0, cone, -d + 0.047, -0.047, Clad, "painted plating", taper: Math.Round(FlangeR * 0.94 / cone, 4), segments: 32); // (the cone, down to inside the small flange)
        Cyl(p, 0, 0, cone + 0.012, -0.11, -0.075, Hazard, "hazard paint", inner: Math.Round((cone - 0.05) / (cone + 0.012), 3), segments: 32); // (a band by the big flange)
        Cyl(p, 0, 0, FlangeR - 0.001, -d + 0.05, -d + 0.003, Trim, "dirty steel", segments: 24); // (the small flange, at the XS bore)
        Bolts(p, FlangeR - 0.03, -d + 0.001, -d + 0.015, 8);
        foreach (var sx in new[] { -1.0, 1.0 }) // (two lifting lugs on the big flange's top, each with its eye's boss)
        {
            var x = sx * 0.2;
            Look(p, x - 0.02, rb - 0.03, -0.045, x + 0.02, rb + 0.06, -0.012, Dark, "dirty steel");
            Look(p, x - 0.028, rb + 0.022, -0.036, x + 0.028, rb + 0.045, -0.021, Bolt, "steel");
        }
        TrainSolid(p, d);
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        p.Set(("worksPiece", Kw(("kind", "train"), ("function", "adapter"), ("bore", big), ("boreBack", Across))));
        return p.Stats(120, 700, 20);
    }

    /// <summary>The minimum viable engine as a module design: the 1 m round plug, the bell on its middle, the chamber and injector behind it.</summary>
    private static string ModuleText() =>
        "{\n" +
        "  \"format\": \"call-of-slop-construct-blueprint/1\",\n" +
        "  \"name\": \"Module Engine XS\",\n" +
        "  \"description\": \"A 1 m round module: the minimum viable engine, a Drive Nozzle XS on the plug with its Reaction Chamber XS and Fuel Injector XS welded on behind it, through the plug's hole. Fit it to a Round Module Socket 1 m in Opening style and run a fuel pipe into the injector's Fuel in. Written by the part kit (engine_train).\",\n" +
        "  \"module\": { \"w\": 1, \"l\": 1, \"round\": true, \"primary\": \"engine\", \"tags\": [\"engine\"] },\n" +
        "  \"steps\": [\n" +
        "    { \"part\": \"Round Module Plug 1 m\" },\n" +
        "    { \"part\": \"Drive Nozzle XS\", \"on\": 0, \"socket\": \"s0.0\", \"by\": \"mount\" },\n" +
        "    { \"part\": \"Reaction Chamber XS\", \"on\": 1, \"socket\": \"train\", \"by\": \"front\" },\n" +
        "    { \"part\": \"Fuel Injector XS\", \"on\": 2, \"socket\": \"back\", \"by\": \"front\" }\n" +
        "  ]\n" +
        "}\n";

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, new[] { Nozzle(), ReactionChamber(), Injector(), Spacer(), Buffer(), Jacket(), Igniter(), SensorRing(), ThrottleValve(), MixtureController(), ReheatRing(), FuelFilter(), GimbalRing(), ArmourCollar(), FuelSelector(), BoreAdapter() }.Select(p => ("", p)));
        o.File($"{BpOut}/ModuleEngineXS.json", ModuleText());
    }
}
