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
        p.Set(("worksPiece", Kw(("kind", "core"), ("serves", ChamberServes))));
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
        p.Set(("worksPiece", Kw(("kind", "injector"), ("serves", InjectorServes))));
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
        WriteParts(o, Out, new[] { Nozzle(), ReactionChamber(), Injector() }.Select(p => ("", p)));
        o.File($"{BpOut}/ModuleEngineXS.json", ModuleText());
    }
}
