using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Mainframe's own parts besides the frame node: what a yard can't cut from plate. Ship-named, so they stay out of the inventory
/// (blueprint-only, PR 236): a strip of beam light, the missile cells' hatch, the fusion reactors, the drives (each one's mount, bell
/// and engine pieces, ship works: docs/sandbox/design/ship-works.md 9), the flight computer and the attitude jets on their blocks.
/// </summary>
public sealed partial class MainframeParts
{
    public const string CellHatch = "Mainframe Cell Hatch", ReactorName = "Mainframe Fusion Reactor", DriveMountName = "Mainframe Drive Mount",
        DriveBellName = "Mainframe Drive Bell", DriveCoreName = "Mainframe Drive Core", FuelPumpName = "Mainframe Fuel Pump", ExchangerName = "Mainframe Coolant Exchanger",
        FlightComputerName = "Mainframe Flight Computer", JetName = "Mainframe Attitude Jet", RcsBlockName = "Mainframe RCS Block";

    /// <summary>Each attitude jet's thrust (N; four on each of eight blocks), the fuel a drive mount's header tank and an RCS block's tank
    /// hold (l), and the flight computer's gyro (N m, a turn about the ship's up).</summary>
    private const double JetForce = 8e6, MountFuel = 300_000, RcsFuel = 50_000, Gyro = 3e9;

    /// <summary>Each drive bell's thrust (N; six of them) and what it burns a second for each MN (l).</summary>
    private const double BellForce = 40e6, BellBurn = 2.0;

    /// <summary>Each reactor's output (kW).</summary>
    private const double ReactorKw = 400_000;

    private static IEnumerable<PyDict> OwnParts()
    {
        yield return BeamLight().Json();
        yield return CellHatchPart().Json();
        yield return Reactor().Json();
        yield return DriveMount().Json();
        yield return DriveBell().Json();
        yield return EnginePiece(DriveCoreName, "core", 6.5, 14, $"One of the Mainframe's six drive cores: a reaction chamber 13 m across and 14 m long, bolted to its drive's mount on the bell's axis. It serves {BellForce / 1e6:0} MN of drive; the Fuel Pump and Coolant Exchanger bolt on forward of it.", Armour, 600_000).Json();
        yield return EnginePiece(FuelPumpName, "pump", 5, 8, "One of the Mainframe's six drive fuel pumps, on its drive's axis forward of the core.", Frame, 250_000).Json();
        yield return EnginePiece(ExchangerName, "exchanger", 7.5, 10, "One of the Mainframe's six coolant exchangers, the last of each drive's engine, ringed with radiator fins.", Skin2, 400_000).Json();
        yield return FlightComputer().Json();
        yield return AttitudeJet().Json();
        yield return RcsBlock().Json();
    }

    private static Part Own(string name, string desc, string role = "frame", string tool = "welder", string color = Frame) =>
        new(name, Small, Section, desc, role: role, tool: tool, material: "steel", color: color, group: Group);

    private static double[] A(double x, double y, double z) => [x, y, z];

    private static void Sol(Part p, double[] mn, double[] mx, string color, string finish) => p.Add(Box(mn, mx, color, shape: Kw(("finish", finish))));

    private static void Det(Part p, double[] mn, double[] mx, string color, string finish, bool glow = false) =>
        p.Add(Box(mn, mx, color, detail: true, shape: glow ? Kw(("glow", true), ("paintable", false)) : Kw(("finish", finish))));

    private static void Hid(Part p, double[] mn, double[] mx) => p.Add(Box(mn, mx, hidden: true));

    /// <summary>A cylinder along z (axis at x, y) or standing along y (<paramref name="up"/>: axis at x, z given as y): drawn only.</summary>
    private static void Cyl(Part p, double x, double y, double r, double a0, double a1, string color, string finish, bool up = false, double inner = 0, int segments = 24, bool glow = false)
    {
        var shape = Kw(("shape", inner > 0 ? "tube" : "cylinder"), ("segments", segments));
        if (up)
            shape["front"] = "+y";
        if (inner > 0)
            shape["inner"] = inner;
        if (glow)
        {
            shape["glow"] = true;
            shape["paintable"] = false;
        }
        else
            shape["finish"] = finish;
        double lo = Math.Min(a0, a1), hi = Math.Max(a0, a1);
        p.Add(up ? Box(A(x - r, lo, y - r), A(x + r, hi, y + r), color, detail: true, shape: shape) : Box(A(x - r, y - r, lo), A(x + r, y + r, hi), color, detail: true, shape: shape));
    }

    /// <summary>A convex solid through the points (drawn only).</summary>
    private static void Hull(Part p, IEnumerable<double[]> pts, string color, string finish, bool glow = false)
    {
        var all = pts.Select(q => q.Select(v => Py.Round(v, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => all.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => all.Max(q => q[k]))];
        var shape = Kw(("shape", "hull"), ("points", all.Select(q => (object?)q.Select(v => (object?)v).ToList()).ToList()), ("paintable", false));
        if (glow)
            shape["glow"] = true;
        else
            shape["finish"] = finish;
        p.Add(Box(mn, mx, color, detail: true, shape: shape));
    }

    /// <summary>A ring of n points round the z axis, radius r, at z.</summary>
    private static double[][] Round(double r, double z, int n) => [.. Enumerable.Range(0, n).Select(i =>
    {
        var a = Math.PI / n + i * 2 * Math.PI / n;
        return A(Py.Round(r * Math.Cos(a), 4), Py.Round(r * Math.Sin(a), 4), z);
    })];

    /// <summary>A band between two rings as n thin panels (each a flat quad, thick 6 cm).</summary>
    private static void Band(Part p, double r0, double z0, double r1, double z1, int n, string color, string finish)
    {
        var a = Round(r0, z0, n);
        var b = Round(r1, z1, n);
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            // (a panel: the four corners and the same four 6 cm in, toward the axis)
            double[] In(double[] q) => [q[0] * 0.995, q[1] * 0.995, q[2]];
            Hull(p, [a[i], a[j], b[j], b[i], In(a[i]), In(a[j]), In(b[j]), In(b[i])], color, finish);
        }
    }

    // ---- The parts

    private static Part BeamLight()
    {
        var p = Own(LightName, $"A strip of link-blue light along the Mainframe's beam line, {LightLen:0} m long: the IT ships' running light, laid in the gaps between the armour.", role: "frame", color: Frame);
        Sol(p, A(-0.22, 0, 0.02), A(0.22, 0.05, LightLen - 0.06), Frame, "dirty steel");
        Det(p, A(-0.12, 0.05, 0.12), A(0.12, 0.068, LightLen - 0.16), Glow, "", glow: true);
        p.Sock("back", [0, 0, Py.Round(LightLen / 2, 2)], "-y");
        return p.Stats(80, 400, 5, 2.0);
    }

    private static Part CellHatchPart()
    {
        var p = Own(CellHatch, "The armoured hatch over a row of eight missile cells in the Mainframe's spine, hinged on its fore edge. It swings open by itself just before a missile leaves its cell and shuts a few seconds after the last. F opens or shuts it by hand.", role: "door", color: Armour2);
        double l = CellHatchL, h = CellHatchW / 2, t = 0.1;
        Sol(p, A(-l, -h, -t), A(0, h, 0), Armour2, "plated steel");
        foreach (var y in (ReadOnlySpan<double>)[-1.7, -0.6, 0.6, 1.7])
            Det(p, A(-l + 0.12, y - 0.05, -t - 0.03), A(-0.12, y + 0.05, -t + 0.006), Armour, "plated steel");
        Det(p, A(-l + 0.02, -h + 0.04, -t - 0.012), A(-l + 0.14, h - 0.04, -t + 0.005), Hazard, "hazard paint"); // (hazard paint on its free edge)
        foreach (var y in (ReadOnlySpan<double>)[-h + 0.3, 0, h - 0.3])
            Det(p, A(-0.1, y - 0.18, -t - 0.05), A(0, y + 0.18, -t + 0.007), Frame, "dirty steel"); // (hinge knuckles)
        p.Sock("hang", [0, 0, 0], "+z");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+y"), ("angle", -90), ("seconds", 1.0), ("hang", "hang"), ("launch", true))));
        return p.Stats(900, 9000, 45, 6.0);
    }

    private static Part Reactor()
    {
        var p = Own(ReactorName, $"One of the Mainframe's four fusion reactors: an armoured drum 12 m across on a 16 m plinth, ringed by coolant towers, {ReactorKw / 1000:0} MW for the ship's circuits, no fuel. The blue band is normal.", role: "generator", color: Armour);
        Sol(p, A(-8, 0, -8), A(8, 1.5, 8), Armour2, "plated steel");
        Hid(p, A(-6, 1.5, -6), A(6, 14.5, 6));
        Cyl(p, 0, 0, 6, 1.497, 13.5, Armour, "plated steel", up: true, segments: 32);
        Cyl(p, 0, 0, 6.06, 6.8, 8.2, Glow, "", up: true, segments: 32, glow: true);
        Cyl(p, 0, 0, 6.08, 4.0, 4.6, Blue, "painted plating", up: true, segments: 32);
        Cyl(p, 0, 0, 6.08, 10.4, 11.0, Blue, "painted plating", up: true, segments: 32);
        Cyl(p, 0, 0, 5.0, 13.497, 14.5, Frame, "dirty steel", up: true, segments: 32);
        Cyl(p, 0, 0, 2.0, 14.497, 15.6, Copper, "copper", up: true, segments: 16);
        foreach (var (x, z) in new[] { (-6.6, -6.6), (6.6, -6.6), (-6.6, 6.6), (6.6, 6.6) })
        {
            Cyl(p, x, z, 1.0, 1.497, 12.0, Skin2, "painted plating", up: true, segments: 16); // (a coolant tower)
            Cyl(p, x, z, 1.08, 11.4, 11.9, Copper, "copper", up: true, segments: 16);
            var (dx, dz) = (-Math.Sign(x) * 0.9, -Math.Sign(z) * 0.9); // (its pipe in to the drum)
            Det(p, A(Math.Min(x, x + dx * 1.5) - 0.25, 9.6, Math.Min(z, z + dz * 1.5) - 0.25), A(Math.Max(x, x + dx * 1.5) + 0.25, 10.1, Math.Max(z, z + dz * 1.5) + 0.25), Copper, "copper");
        }
        Det(p, A(-1.2, 1.5, 7.9), A(1.2, 3.2, 8.06), Console, "painted steel"); // (its gauge board on the plinth's face)
        Det(p, A(-1.0, 2.0, 8.06), A(1.0, 2.9, 8.08), Screen, "", glow: true);
        p.Sock("bottom", [0, 0, 0], "-y");
        foreach (var (name, at, dir) in new[] { ("+x", A(8, 0.75, 0), "+x"), ("-x", A(-8, 0.75, 0), "-x"), ("+z", A(0, 0.75, 8), "+z"), ("-z", A(0, 0.75, -8), "-z") })
            p.Sock(name, at, dir);
        p.Set(("generator", Kw(("rate", ReactorKw), ("spool", 30), ("fuel", "none"))), ("ports", Py.L(Kw(("name", "Power out"), ("type", "power"), ("dir", "out"), ("at", Py.L(0, 0.75, 8)), ("face", "+z")))));
        return p.Stats(2_000_000, 2_000_000, 60, 60.0);
    }

    private static Part DriveMount()
    {
        var p = Own(DriveMountName, $"A drive mount: a 31 m armoured plate bolted inside the Mainframe's transom over a drive's opening, its drive's {MountFuel / 1000:0} kl header tank inside it. The drive bell bolts to its aft face through the opening; its engine (a Drive Core, a Fuel Pump and a Coolant Exchanger) to its fore face, on the bell's axis.", role: "fueltank", color: Armour2);
        Sol(p, A(-15.5, -15.5, -1.2), A(15.5, 15.5, 0), Armour2, "plated steel");
        Cyl(p, 0, 0, 10.6, -0.004, 0.06, Soot, "soot", segments: 32); // (the bell's seat, soot-black)
        foreach (var (x, y) in new[] { (-13.5, -13.5), (13.5, -13.5), (-13.5, 13.5), (13.5, 13.5) })
            Det(p, A(x - 0.6, y - 0.6, -1.5), A(x + 0.6, y + 0.6, -1.197), Frame, "dirty steel"); // (its bolting bosses)
        p.Sock("bell", [0, 0, 0], "+z");
        p.Sock("engine", [0, 0, -1.2], "-z");
        foreach (var (i, x, y) in new[] { (0, -14.0, -14.0), (1, 14.0, -14.0), (2, -14.0, 14.0), (3, 14.0, 14.0) })
            p.Sock($"c{i}", [x, y, 0], "+z");
        p.Set(("fuelTank", Kw(("capacity", MountFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [0, 14.5, -1.2], "-z", "fuel"))));
        Works.Mount(p);
        return p.Stats(800_000, 1_500_000, 60, 60.0);
    }

    private static Part DriveBell()
    {
        var p = Own(DriveBellName, $"One of the Mainframe's six drive bells, {BellForce / 1e6:0} MN, 26 m across at its mouth, through an opening in the transom. Its engine is right behind it on its axis, forward of its mount: a Drive Core, a Fuel Pump and a Coolant Exchanger.", role: "thruster", tool: "wrench", color: Frame);
        Hid(p, A(-10, -10, 0), A(10, 10, 2));
        Hid(p, A(-12.5, -12.5, 2), A(12.5, 12.5, 26));
        Cyl(p, 0, 0, 10.0, 0.003, 1.8, Armour, "plated steel", segments: 32); // (the collar through the transom)
        Cyl(p, 0, 0, 10.4, 1.6, 2.2, Copper, "copper", segments: 32);
        Band(p, 9.2, 2.2, 7.5, 5.0, 16, "#3a3d40", "dirty steel");
        Band(p, 7.5, 5.0, 10.5, 14.0, 16, "#2a2c2e", "soot");
        Band(p, 10.5, 14.0, 13.0, 26.0, 16, "#2a2c2e", "soot");
        Cyl(p, 0, 0, 13.25, 24.6, 25.6, "#5a4a3a", "copper", segments: 32, inner: 0.96); // (the lip)
        Hull(p, Round(6.4, 5.2, 16).Concat(Round(6.4, 5.3, 16)), Glow, "", glow: true); // (the link-blue glow deep in the throat)
        p.Sock("mount", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", BellForce), ("spool", 6.0), ("exhaust", "+z"), ("at", Py.L(0, 0, 26)), ("burnRate", Py.Round(BellForce / 1e6 * BellBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 10, 0.9], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, 26)), ("dir", "+z"), ("radius", 11.0), ("length", 90.0), ("kind", "Rocket")))));
        Works.Head(p, Works.Table(), "torch", BellForce);
        return p.Stats(400_000, 400_000, 50, 60.0);
    }

    /// <summary>A drive's engine piece: a cylinder of radius r and length l forward of its back (its origin), bolted on its drive's axis.</summary>
    private static Part EnginePiece(string name, string kind, double r, double l, string desc, string color, double mass)
    {
        var p = Own(name, desc, color: color);
        Hid(p, A(-r, -r, -l), A(r, r, 0));
        Cyl(p, 0, 0, r, -l + 0.003, -0.003, color, kind == "core" ? "plated steel" : "painted plating", segments: 32);
        var n = 0;
        if (kind == "exchanger")
            for (var z = -0.6; z > -l + 0.5; z -= 0.8, n++)
                Cyl(p, 0, 0, n % 2 == 0 ? r * 1.12 : r * 1.08, z - 0.12, z + 0.12, "#6a6f72", "steel", segments: 32, inner: 0.9); // (radiator fins)
        else
            for (var z = -1.0; z > -l + 0.5; z -= 2.5)
                Cyl(p, 0, 0, r * 1.03, z - 0.2, z + 0.2, Copper, "copper", segments: 32, inner: 0.96); // (bands)
        if (kind == "pump")
            foreach (var s in (ReadOnlySpan<int>)[1, -1])
                Det(p, A(s > 0 ? r * 0.7 : -r * 1.25, -1.5, -l * 0.75), A(s > 0 ? r * 1.25 : -r * 0.7, 1.5, -l * 0.25), Armour2, "plated steel"); // (the pump housings)
        Det(p, A(-1.0, r * 0.985, -l * 0.5 - 0.8), A(1.0, r + 0.08, -l * 0.5 + 0.8), Console, "painted steel"); // (its gauge board)
        Det(p, A(-0.8, r + 0.08, -l * 0.5 - 0.6), A(0.8, r + 0.1, -l * 0.5 + 0.6), Screen, "", glow: true);
        p.Sock("back", [0, 0, 0], "+z");
        p.Sock("front", [0, 0, -l], "-z");
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", kind), ("serves", BellForce))));
        return p.Stats(mass, mass / 2, 40, 40.0);
    }

    /// <summary>The flight computer: a cabinet on the bridge with a capital gyro. It flies the ship from the Bridge Helm (the helm is the
    /// seat a stabilizer takes when nothing is wired to it) on the drives and the attitude jets, which it finds by itself.</summary>
    private static Part FlightComputer()
    {
        var p = Own(FlightComputerName, "The Mainframe's flight computer: a flight stabilizer with a capital gyro in a bridge cabinet. It flies the ship from the Bridge Helm on its drives and attitude jets. Fit it level, its arrow forward.", role: "logic", tool: "wrench", color: Console);
        Sol(p, A(-0.8, 0, -0.45), A(0.8, 1.9, 0.45), Console, "painted steel");
        Det(p, A(-0.7, 1.15, -0.468), A(0.7, 1.75, -0.45), Screen, "", glow: true); // (its screen, forward)
        for (var i = 0; i < 8; i++)
            Det(p, A(-0.62 + i * 0.16, 0.9, -0.466), A(-0.54 + i * 0.16, 0.98, -0.45), i % 3 == 0 ? Hazard : Glow, "", glow: true); // (status lamps)
        Det(p, A(-0.06, 1.903, -0.4), A(0.06, 1.92, 0.1), Hazard, "hazard paint"); // (the arrow on its top, forward)
        Det(p, A(-0.16, 1.903, -0.4), A(0.16, 1.92, -0.3), Hazard, "hazard paint");
        Det(p, A(-0.81, 0.06, -0.44), A(0.81, 0.12, 0.44), Blue, "painted steel"); // (a link-blue band round its foot)
        p.Sock("bottom", [0, 0, 0], "-y");
        p.Set(("logic", Kw(("kind", "flightstabilizer"), ("gyro", Gyro))), ("config", FlightParts.Config), ("faces", "forward"), ("faceAxis", "-z"));
        return p.Stats(2_000, 3_000, 20, 6.0);
    }

    /// <summary>An attitude jet: a bell 3 m across on a collar, bolted by its back to a face of an RCS block, pushing away from its mouth.</summary>
    private static Part AttitudeJet()
    {
        var p = Own(JetName, $"One of the Mainframe's attitude jets, {JetForce / 1e6:0} MN out of a bell 3 m across. Eight blocks of four at the ship's ends point up, down, out, fore and aft, so the flight computer turns the ship about any axis, slides it and slows it.", role: "thruster", tool: "wrench", color: Frame);
        Hid(p, A(-1.2, -1.2, 0), A(1.2, 1.2, 3.3));
        Cyl(p, 0, 0, 1.25, 0.003, 0.7, Armour, "plated steel", segments: 16); // (the collar)
        Cyl(p, 0, 0, 1.32, 0.52, 0.78, Copper, "copper", segments: 16);
        Band(p, 0.95, 0.8, 1.1, 1.7, 12, "#2a2c2e", "soot");
        Band(p, 1.1, 1.7, 1.5, 3.2, 12, "#2a2c2e", "soot");
        Cyl(p, 0, 0, 1.56, 3.04, 3.34, "#5a4a3a", "copper", segments: 16, inner: 0.94); // (the lip)
        Hull(p, Round(0.62, 1.0, 12).Concat(Round(0.62, 1.08, 12)), Glow, "", glow: true); // (the link-blue glow in its throat)
        p.Sock("mount", [0, 0, 0], "-z");
        p.Set(("thruster", Kw(("force", JetForce), ("spool", 0.6), ("exhaust", "+z"), ("at", Py.L(0, 0, 3.34)), ("burnRate", Py.Round(JetForce / 1e6 * BellBurn, 4)))), ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, 1.2, 0.35], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, 3.34)), ("dir", "+z"), ("radius", 1.3), ("length", 16.0), ("kind", "Rocket")))));
        return p.Stats(8_000, 40_000, 40, 20.0);
    }

    /// <summary>An RCS block: an armoured housing standing on the hull over its own tank, a jet on each of four faces (its base on the skin).</summary>
    private static Part RcsBlock()
    {
        var p = Own(RcsBlockName, $"An attitude jet block on the Mainframe's hull: an armoured housing over a {RcsFuel / 1000:0} kl tank, an Attitude Jet on its outer side, its top and its fore and aft faces.", role: "fueltank", color: Armour);
        const double hx = 2.6, hz = 2.6, h = 3.2, cap = 0.25;
        Sol(p, A(-hx, 0, -hz), A(hx, h, hz), Armour, "plated steel");
        Sol(p, A(-hx + 0.3, h, -hz + 0.3), A(hx - 0.3, h + cap, hz - 0.3), Armour2, "plated steel"); // (the raised cap the top jet bolts to)
        foreach (var x in (ReadOnlySpan<double>)[-1, 1])
            foreach (var z in (ReadOnlySpan<double>)[-1, 1])
            {
                // (a hazard strip down each corner, 12 mm proud of both faces)
                double x0 = x > 0 ? hx - 0.25 : -hx - 0.012, x1 = x > 0 ? hx + 0.012 : -hx + 0.25, z0 = z > 0 ? hz - 0.25 : -hz - 0.012, z1 = z > 0 ? hz + 0.012 : -hz + 0.25;
                Det(p, A(x0, 0.3, z0), A(x1, h - 0.3, z1), Hazard, "hazard paint");
                Det(p, A(x * (hx - 0.55) - 0.12, h + cap, z * (hz - 0.55) - 0.12), A(x * (hx - 0.55) + 0.12, h + cap + 0.06, z * (hz - 0.55) + 0.12), Glow, "", glow: true); // (running lamps)
            }
        p.Sock("base", [0, 0, 0], "-y");
        p.Sock("+x", [hx, h / 2, 0], "+x");
        p.Sock("-x", [-hx, h / 2, 0], "-x");
        p.Sock("+y", [0, h + cap, 0], "+y");
        p.Sock("+z", [0, h / 2, hz], "+z");
        p.Sock("-z", [0, h / 2, -hz], "-z");
        p.Set(("fuelTank", Kw(("capacity", RcsFuel))), ("ports", Py.L(BigDriveParts.PipePort("Fuel out", "out", [1.6, h, 1.6], "+y", "fuel"))));
        return p.Stats(60_000, 120_000, 50, 30.0);
    }
}
