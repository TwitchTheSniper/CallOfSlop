using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Ship works (docs/sandbox/design/ship-works.md; Source/Constructs/ConstructWorks.cs): the generic family any ship builds
/// its drives from, in section <c>Ship fittings</c> with names that name no ship, written to Constructs/Parts/ShipWorks.
///
/// A hull drive is a <b>Drive Nozzle</b> on the outer face of a <b>Hull Mount</b> (a piece of armoured hull a head goes
/// through), with its works bolted to the mount's inner face, or to each other, inside: a <b>Drive Core</b>, then a
/// <b>Fuel Pump</b> and a <b>Coolant Exchanger</b> as the drive grows. A <b>Drive Pod</b> is the same drive with its works
/// in its cowl: hung outside on a pylon, a wide gimbal, serviced at the pod. Each head's "needs" come from
/// Constructs/ShipWorks.json's "drives" table (<see cref="Works.NeedsFor"/>), never picked by hand, and none of these
/// parts gets an Assembling recipe (Karl, 2026-11-16: they are made with the new production system).
///
/// Frames: a head's origin is the mount face it bolts to, its exhaust along +z. A mount's outer face is z 0 (+z, the head's
/// side), its inner face z -0.3. A works piece's origin is the middle of its front face (+z, towards the mount), its body
/// along -z, its bed at y -h/2.
/// </summary>
public sealed class ShipWorksParts : IPartGenerator
{
    public string Name => "ship_works";

    public const string Out = "Constructs/Parts/ShipWorks", Section = "Ship fittings", Group = "Ship";

    private const string Steel = "#5a6065", Trim = "#7b8186", Dark = "#34373a", Hazard = "#d8a020", Copper = "#8a5a3a", Glow = "#ffb060", Gauge = "#40e070", Bed = "#5c6057";
    private const double Plate = 0.3; // a mount's thickness

    /// <summary>The three sizes: a letter, and per size the numbers below.</summary>
    private static readonly string[] SizeName = ["S", "M", "L"];

    // Hull Mount: half its width, mass, health.
    private static readonly double[] MountHalf = [0.75, 1.25, 2.0], MountMass = [500, 1200, 2500], MountHealth = [3000, 6000, 10000];

    // Drive Nozzle: thrust (N), collar radius, bell length, mouth radius, mass, health, spool.
    private static readonly double[] NozForce = [1.5e6, 5e6, 10e6], NozCollar = [0.45, 0.75, 1.05], NozLength = [1.3, 2.0, 2.8], NozMouth = [0.6, 1.0, 1.4], NozMass = [600, 1800, 4000], NozHealth = [3000, 6000, 9000], NozSpool = [1.2, 1.5, 2.0];

    // Drive Core: serves (N), width, height, depth, mass, health.
    private static readonly double[] CoreServes = [2e6, 6e6, 12e6], CoreW = [1.4, 2.0, 2.8], CoreH = [1.6, 2.0, 2.6], CoreD = [1.6, 2.4, 3.4], CoreMass = [1500, 3500, 7000], CoreHealth = [4000, 7000, 12000];

    // Fuel Pump (M, L: index 1, 2): serves, width, height, depth, mass, health.
    private static readonly double[] PumpServes = [0, 6e6, 12e6], PumpW = [0, 1.0, 1.4], PumpH = [0, 1.2, 1.5], PumpD = [0, 1.2, 1.6], PumpMass = [0, 900, 1800], PumpHealth = [0, 2500, 4000];

    // Drive Pod (S, M): thrust, cowl radius, cowl length, mass, health.
    private static readonly double[] PodForce = [1.5e6, 5e6], PodR = [0.7, 1.1], PodLen = [3.0, 4.5], PodMass = [1800, 5200], PodHealth = [3000, 5000];

    public const double HullBurn = 2.0, PodBurn = 2.4; // l/s a MN: a hull drive with its works whole, and a pod's smaller core

    private static double[] P(double x, double y, double z) => [x, y, z];

    private static void Solid(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish) =>
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, shape: Kw(("finish", finish))));

    private static void Look(Part p, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish, bool glow = false)
    {
        var shape = glow ? Kw(("glow", true), ("paintable", false)) : Kw(("finish", finish));
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    private static void Hidden(Part p, double x0, double y0, double z0, double x1, double y1, double z1) =>
        p.Add(Box(P(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1)), P(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1)), hidden: true));

    /// <summary>A cylinder along z, its axis at (x, y): taper is the -z end's share of r (a bell's throat), inner a tube's bore.</summary>
    private static void Cyl(Part p, double x, double y, double r, double z0, double z1, string color, string finish, double taper = 1, double inner = 0, int segments = 16, bool glow = false)
    {
        var shape = Kw(("shape", inner > 0 ? "tube" : "cylinder"), ("segments", segments));
        if (inner > 0)
            shape["inner"] = inner;
        if (taper < 1)
            shape["taper"] = Py.L(Py.Round(taper, 4), Py.Round(taper, 4));
        if (glow)
        {
            shape["glow"] = true;
            shape["paintable"] = false;
        }
        else
            shape["finish"] = finish;
        p.Add(Box(P(x - r, y - r, Math.Min(z0, z1)), P(x + r, y + r, Math.Max(z0, z1)), color, detail: true, shape: shape));
    }

    /// <summary>A cylinder standing along y (a box turned: its axis along y), for pump bodies and gauges' drums.</summary>
    private static void Drum(Part p, double x, double z, double r, double y0, double y1, string color, string finish)
    {
        // (shapes run along z: a short upright drum is drawn as an octagonal prism of two squares instead)
        var d = r * 0.4142; // (an octagon's corner cut)
        Look(p, x - r, y0, z - d, x + r, y1, z + d, color, finish);
        Look(p, x - d, y0 + 0.003, z - r, x + d, y1 - 0.003, z + r, color, finish);
    }

    private static string Desc(string s) => s + " A generic ship fitting: it fits any ship.";

    // ---- the mount -------------------------------------------------------------------------------------------------

    /// <summary>A Hull Mount: an armoured plate a head goes through, a ring on its outer face and ribs inside, sockets on both faces and its edges.</summary>
    private static Part Mount(int s)
    {
        var hw = MountHalf[s];
        var p = new Part($"Hull Mount {SizeName[s]}", Small, Section, Desc($"A piece of armoured hull, {hw * 2:0.#} m square and 30 cm thick, that a drive nozzle, a launcher or a turret ring goes through: its works bolt to its inner face, or to each other behind it."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Solid(p, -hw, -hw, -Plate, hw, hw, 0, Steel, "painted plating");
        var ring = NozCollar[Math.Min(s, 2)] + 0.08;
        Cyl(p, 0, 0, ring + 0.16, 0.003, 0.07, Trim, "dirty steel", inner: Math.Round(ring / (ring + 0.16), 3), segments: 24); // (the ring round the hole, on the outer face)
        for (var k = 0; k < 8; k++)
        {
            var a = Math.PI / 8 + k * Math.PI / 4;
            var (x, y) = ((ring + 0.08) * Math.Cos(a), (ring + 0.08) * Math.Sin(a));
            Look(p, x - 0.035, y - 0.035, 0.07, x + 0.035, y + 0.035, 0.1, "#9a9c98", "steel"); // (its bolts)
        }
        // Ribs on the inner face, and hazard corners on the outer one.
        foreach (var x in new[] { -hw * 0.66, hw * 0.66 })
            Look(p, x - 0.06, -hw + 0.05, -Plate - 0.12, x + 0.06, hw - 0.05, -Plate - 0.003, Dark, "dirty steel");
        foreach (var sx in new[] { -1, 1 })
            foreach (var sy in new[] { -1, 1 })
                Look(p, sx * (hw - 0.22), sy * (hw - 0.22), 0.003, sx * (hw - 0.05), sy * (hw - 0.05), 0.02, Hazard, "hazard paint");
        p.Sock("out", P(0, 0, 0), "+z");
        // The inner face's grid (half a metre apart) for works side by side; the ribs stand clear of it.
        var n = (int)Math.Floor((hw - 0.25) / 0.5 + 1e-6);
        for (var i = -n; i <= n; i++)
            for (var j = -n; j <= n; j++)
                p.Sock(i == 0 && j == 0 ? "in" : $"in.{i}.{j}", P(i * 0.5, j * 0.5, -Plate), "-z");
        foreach (var (en, at) in new (string, double[])[] { ("edge+x", P(hw, 0, -0.15)), ("edge-x", P(-hw, 0, -0.15)), ("edge+y", P(0, hw, -0.15)), ("edge-y", P(0, -hw, -0.15)) })
            p.Sock(en, at, en[4..]);
        p.Set(("worksPiece", Kw(("kind", "mount"))));
        return p.Stats(MountMass[s], MountHealth[s], 40);
    }

    // ---- the heads -------------------------------------------------------------------------------------------------

    /// <summary>A Drive Nozzle: a flanged collar and a bell along +z, a copper cooling band, the glow deep in its throat.</summary>
    private static Part Nozzle(int s, ShipWorks t)
    {
        var (f, r0, len, mouth) = (NozForce[s], NozCollar[s], NozLength[s], NozMouth[s]);
        var needs = t.NeedsFor("torch", (float)f);
        var p = new Part($"Drive Nozzle {SizeName[s]}", Small, Section, Desc($"A torch drive's nozzle, {f / 1e6:0.#} MN: bolt it to a Hull Mount's outer face, and its works ({string.Join(", ", needs.Select(ConstructWorks.KindPiece))}) to the mount's inner face, inside. Short of them it pushes at a quarter."),
            role: "thruster", tool: "wrench", material: "steel", color: Dark, group: Group);
        Cyl(p, 0, 0, r0, 0, 0.12, Trim, "dirty steel", segments: 20); // (the flange)
        var throatR = r0 * 0.62;
        var z1 = len * 0.28;
        Cyl(p, 0, 0, throatR, 0.12, z1, "#2f3134", "dirty steel", segments: 16); // (the throat)
        Cyl(p, 0, 0, mouth, z1, len, "#2a2c2e", "soot", taper: Math.Round(throatR / mouth, 3), inner: 0.9, segments: 20); // (the bell)
        Cyl(p, 0, 0, throatR * 0.92, z1 - 0.03, z1 - 0.012, Glow, "", segments: 16, glow: true); // (the glow in its throat)
        foreach (var zb in new[] { 0.45, 0.7 })
        {
            var z = z1 + (len - z1) * zb;
            var rb = throatR + (mouth - throatR) * zb + 0.025;
            Cyl(p, 0, 0, rb, z - 0.04, z + 0.04, Copper, "copper", inner: Math.Round((rb - 0.06) / rb, 3), segments: 20); // (cooling bands)
        }
        Hidden(p, -r0, -r0, 0, r0, r0, len);
        p.Sock("mount", P(0, 0, 0), "-z");
        p.Set(("thruster", Kw(("force", f), ("spool", NozSpool[s]), ("exhaust", "+z"), ("at", Py.L(0, 0, len)), ("burnRate", Py.Round(f / 1e6 * HullBurn, 4)), ("gimbal", 3), ("gimbalRate", 10))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [Py.Round(r0, 3), 0, 0.06], "+x", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, len)), ("dir", "+z"), ("radius", Py.Round(mouth * 0.9, 3)), ("length", Py.Round(mouth * 4, 3)), ("kind", "Rocket")))));
        p.Set(("works", Kw(("needs", needs.Select(k => (object?)ConstructWorks.KindNames[(int)k]).ToList()))));
        return p.Stats(NozMass[s], NozHealth[s], 25);
    }

    /// <summary>A Drive Pod: nozzle and works in one armoured cowl on a pylon, a 12 degree gimbal, a service hatch on its side.</summary>
    private static Part Pod(int s)
    {
        var (f, r, len) = (PodForce[s], PodR[s], PodLen[s]);
        var bell = r * 1.1;
        var p = new Part($"Drive Pod {SizeName[s]}", Small, Section, Desc($"A drive pod, {f / 1e6:0.#} MN: nozzle and works in one cowl, hung outside on a pylon (its top) or a nacelle's tail (its front). It turns the ship with its 12 degree gimbal and takes no room inside, but it has only the cowl's armour and is mended at the pod: on a space walk, or landed."),
            role: "thruster", tool: "wrench", material: "steel", color: Steel, group: Group);
        var nose = len * 0.18;
        Cyl(p, 0, 0, r, 0, nose, Steel, "worn paint", taper: 0.72, segments: 16); // (the nose)
        Cyl(p, 0, 0, r, nose, len, Steel, "worn paint", segments: 16); // (the cowl)
        Cyl(p, 0, 0, r + 0.03, len * 0.55, len * 0.6, Hazard, "hazard paint", inner: Math.Round(r / (r + 0.03), 3), segments: 16); // (a band)
        Cyl(p, 0, 0, r * 0.62, len - 0.06, len + 0.12, Trim, "dirty steel", segments: 16); // (the collar)
        var bl = len + 0.12 + bell * 1.3;
        Cyl(p, 0, 0, bell * 0.9, len + 0.12, bl, "#2a2c2e", "soot", taper: 0.5, inner: 0.9, segments: 16); // (the bell, on its gimbal)
        Cyl(p, 0, 0, bell * 0.4, len + 0.14, len + 0.16, Glow, "", segments: 12, glow: true);
        // The service hatch on its starboard side, its handles, and a handrail along the top.
        var hz0 = len * 0.3;
        var hz1 = len * 0.52;
        Look(p, r - 0.03, -r * 0.35, hz0, r + 0.03, r * 0.35, hz1, Trim, "painted plating");
        Look(p, r + 0.03, -r * 0.35, hz0, r + 0.05, r * 0.35, hz0 + 0.05, Hazard, "hazard paint");
        Look(p, r + 0.03, -r * 0.35, hz1 - 0.05, r + 0.05, r * 0.35, hz1, Hazard, "hazard paint");
        Look(p, r + 0.05, -0.06, (hz0 + hz1) / 2 - 0.12, r + 0.11, 0.06, (hz0 + hz1) / 2 + 0.12, "#9a9c98", "steel"); // (its handle)
        foreach (var z in new[] { len * 0.25, len * 0.75 })
            Look(p, -r - 0.16, -0.03, z - 0.03, -r + 0.02, 0.03, z + 0.03, "#9a9c98", "steel"); // (the handrail's posts, on its port side)
        Look(p, -r - 0.19, -0.025, len * 0.25 - 0.03, -r - 0.14, 0.025, len * 0.75 + 0.03, Hazard, "worn paint"); // (the handrail)
        Look(p, -r * 0.5, -r - 0.01, len * 0.2, r * 0.5, -r + 0.05, len * 0.4, Dark, "soot"); // (an intake grille under it)
        var top = Math.Floor(r * 0.98 * 100) / 100; // (a 16-sided cowl's flat top)
        Hidden(p, -r * 0.7, -r * 0.7, 0, r * 0.7, top, len);
        p.Sock("front", P(0, 0, 0), "-z");
        p.Sock("pylon", P(0, top, Math.Round(len / 2, 2)), "+y");
        p.Set(("thruster", Kw(("force", f), ("spool", s == 0 ? 1.2 : 1.5), ("exhaust", "+z"), ("at", Py.L(0, 0, Py.Round(bl, 3))), ("burnRate", Py.Round(f / 1e6 * PodBurn, 4)), ("gimbal", 12), ("gimbalRate", 20))),
            ("ports", Py.L(BigDriveParts.PipePort("Fuel in", "in", [0, top, Py.Round(len * 0.85, 3)], "+y", "fuel"))));
        p.Set(("flame", Py.L(Kw(("at", Py.L(0, 0, Py.Round(bl, 3))), ("dir", "+z"), ("radius", Py.Round(bell * 0.85, 3)), ("length", Py.Round(bell * 4, 3)), ("kind", "Rocket")))));
        p.Set(("works", Kw(("service", "outside"))));
        return p.Stats(PodMass[s], PodHealth[s], 12);
    }

    // ---- the works -------------------------------------------------------------------------------------------------

    /// <summary>A works piece's sockets: its front face (to the mount), its back, its sides and its bed.</summary>
    private static void PieceSockets(Part p, double w, double h, double d, bool sides = true)
    {
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        if (sides)
        {
            p.Sock("+x", P(w / 2, 0, Math.Round(-d / 2, 2)), "+x");
            p.Sock("-x", P(-w / 2, 0, Math.Round(-d / 2, 2)), "-x");
        }
        p.Sock("deck", P(0, -h / 2, Math.Round(-d / 2, 2)), "-y");
    }

    /// <summary>A Drive Core: the reaction chamber on its bed, a flange to the mount, feed pipes and a gauge on its side.</summary>
    private static Part Core(int s)
    {
        var (w, h, d) = (CoreW[s], CoreH[s], CoreD[s]);
        var p = new Part($"Drive Core {SizeName[s]}", Small, Section, Desc($"A torch drive's reaction chamber on its bed: it serves {CoreServes[s] / 1e6:0} MN of nozzles through the Hull Mount it is bolted to. Leave a metre clear in front of its gauge to work on it."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var y0 = -h / 2;
        Solid(p, -w / 2, y0, -d, w / 2, y0 + 0.22, -0.02, Bed, "dirty steel"); // (the bed: solid)
        var rc = Math.Min(w, h - 0.22) * 0.42;
        var yc = y0 + 0.22 + rc + 0.04;
        Cyl(p, 0, yc, rc, -d + 0.15, -0.16, Steel, "painted plating", segments: 20); // (the chamber)
        Cyl(p, 0, yc, rc * 1.06, -d + 0.15, -d + 0.3, Trim, "dirty steel", segments: 20); // (its end cap)
        foreach (var zf in new[] { 0.3, 0.55, 0.8 })
        {
            var z = -d + 0.15 + (d - 0.31) * zf;
            Cyl(p, 0, yc, rc + 0.03, z - 0.04, z + 0.04, Copper, "copper", inner: Math.Round(rc / (rc + 0.03), 3), segments: 20); // (field coils)
        }
        Cyl(p, 0, 0, Math.Min(rc, 0.45 + s * 0.2), -0.16, -0.003, Trim, "dirty steel", segments: 20); // (the flange to the mount)
        // Saddles under the chamber, feed pipes over it, hazard edges on the bed, and the gauge on its starboard side.
        foreach (var zf in new[] { 0.2, 0.8 })
        {
            var z = -d + 0.15 + (d - 0.31) * zf;
            Look(p, -rc * 0.8, y0 + 0.223, z - 0.1, rc * 0.8, yc - rc * 0.55, z + 0.1, Dark, "dirty steel");
        }
        foreach (var x in new[] { -rc * 0.45, rc * 0.45 })
        {
            var pr = 0.06 + s * 0.02;
            var py = yc + rc + 0.08;
            Cyl(p, x, py, pr, -d + 0.4, -0.2, Copper, "copper", segments: 8); // (feed pipes over it...)
            foreach (var z in new[] { -d + 0.45, -0.25 })
                Look(p, x - pr * 0.7, yc + rc * 0.8, z - pr * 0.7, x + pr * 0.7, py - pr * 0.5, z + pr * 0.7, Copper, "copper"); // (...on risers into it)
        }
        Look(p, -w / 2 + 0.004, y0 + 0.16, -d + 0.004, w / 2 - 0.004, y0 + 0.226, -d + 0.08, Hazard, "hazard paint");
        var gz = -d * 0.5;
        Look(p, rc + 0.02, yc - 0.2, gz - 0.25, rc + 0.12, yc + 0.25, gz + 0.25, Dark, "painted plating"); // (the gauge board)
        Look(p, rc + 0.12, yc - 0.05, gz - 0.12, rc + 0.135, yc + 0.17, gz + 0.12, Gauge, "", glow: true);
        Look(p, rc + 0.12, yc - 0.17, gz - 0.12, rc + 0.135, yc - 0.1, gz + 0.12, Glow, "", glow: true);
        Hidden(p, -w / 2, y0 + 0.22, -d, w / 2, h / 2, -0.02);
        Hidden(p, -0.4, -0.4, -0.02, 0.4, 0.4, 0); // (the face that meets the mount)
        PieceSockets(p, w, h, d);
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", "core"), ("serves", CoreServes[s]))));
        return p.Stats(CoreMass[s], CoreHealth[s], 30);
    }

    /// <summary>A Fuel Pump: a motor and a pump drum on a skid, pipes out front, a pressure gauge. It burns when it is holed.</summary>
    private static Part Pump(int s)
    {
        var (w, h, d) = (PumpW[s], PumpH[s], PumpD[s]);
        var p = new Part($"Fuel Pump {SizeName[s]}", Small, Section, Desc($"A torch drive's fuel pump: it serves {PumpServes[s] / 1e6:0} MN of nozzles through the Hull Mount its works stand on. It is full of fuel, and burns when it is holed."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var y0 = -h / 2;
        Solid(p, -w / 2, y0, -d, w / 2, y0 + 0.14, -0.02, Bed, "dirty steel"); // (the skid)
        var r = w * 0.32;
        Drum(p, -w * 0.12, -d * 0.6, r, y0 + 0.143, h / 2 - 0.12, Hazard, "worn paint"); // (the pump drum)
        Look(p, w * 0.12, y0 + 0.143, -d * 0.55, w * 0.46, y0 + h * 0.55, -d * 0.1, Trim, "painted plating"); // (its motor)
        Cyl(p, -w * 0.12, h / 2 - 0.25, 0.08 + s * 0.02, -d * 0.6, -0.003, Copper, "copper", segments: 8); // (the outlet to the front)
        Look(p, -w * 0.12 - 0.12, h / 2 - 0.12, -d * 0.6 - 0.12, -w * 0.12 + 0.12, h / 2 - 0.003, -d * 0.6 + 0.12, Dark, "dirty steel"); // (its head)
        Look(p, w * 0.46 + 0.003, y0 + h * 0.3, -d * 0.4, w * 0.5 - 0.004, y0 + h * 0.5, -d * 0.2, Gauge, "", glow: true); // (the pressure gauge)
        Hidden(p, -w / 2, y0 + 0.14, -d, w / 2, h / 2, -0.02);
        Hidden(p, -0.3, -0.3, -0.02, 0.3, 0.3, 0);
        PieceSockets(p, w, h, d);
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", "pump"), ("serves", PumpServes[s]))));
        return p.Stats(PumpMass[s], PumpHealth[s], 15);
    }

    /// <summary>A Coolant Exchanger L: a flat cabinet against the hull, radiator fins on its hull face, manifolds on its room side.</summary>
    private static Part Exchanger()
    {
        const double w = 2.4, h = 2.2, d = 0.9;
        var p = new Part("Coolant Exchanger L", Small, Section, Desc("A heavy torch drive's coolant exchanger: a flat cabinet that stands against the hull, its radiators on its hull face. It serves 20 MN of nozzles through the Hull Mount its works stand on."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var y0 = -h / 2;
        Solid(p, -w / 2, y0, -d, w / 2, h / 2, -0.12, Steel, "painted plating"); // (the cabinet)
        for (var x = -w / 2 + 0.15; x < w / 2 - 0.1; x += 0.2)
            Look(p, x - 0.03, y0 + 0.1, -0.12, x + 0.03, h / 2 - 0.1, -0.003, "#6a6f72", "steel"); // (radiator fins on its hull face)
        foreach (var y in new[] { y0 + 0.5, h / 2 - 0.5 })
            Cyl(p, 0, y, 0.12, -d - 0.003, -d - 0.18, Copper, "copper", segments: 10); // (manifold stubs on its room side)
        Look(p, -w / 2 + 0.2, y0 + 0.003, -d - 0.04, w / 2 - 0.2, y0 + 0.1, -d + 0.2, Hazard, "hazard paint"); // (a kick plate)
        Look(p, w / 2 - 0.5, 0.2, -d - 0.02, w / 2 - 0.2, 0.5, -d - 0.003, Gauge, "", glow: true); // (its gauge)
        Hidden(p, -0.4, -0.4, -0.12, 0.4, 0.4, 0);
        PieceSockets(p, w, h, d);
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", "exchanger"), ("serves", 20e6))));
        return p.Stats(2500, 5000, 20);
    }

    public void Make(PartOutput o)
    {
        var t = Works.Table();
        List<Part> parts = [];
        for (var s = 0; s < 3; s++)
            parts.Add(Mount(s));
        for (var s = 0; s < 3; s++)
            parts.Add(Nozzle(s, t));
        for (var s = 0; s < 3; s++)
            parts.Add(Core(s));
        parts.Add(Pump(1));
        parts.Add(Pump(2));
        parts.Add(Exchanger());
        parts.Add(Pod(0));
        parts.Add(Pod(1));
        WriteParts(o, Out, parts.Select(p => ("", p)));
    }
}

/// <summary>
/// What a ship's generator uses to give its drives works (docs/sandbox/design/ship-works.md 9): the needs from the table, a
/// ship's own piece flagged as a mount, and a head's block.
/// </summary>
public static class Works
{
    private static ShipWorks? _table;

    /// <summary>The table, read from the repo (the part kit runs from it).</summary>
    public static ShipWorks Table()
    {
        if (_table != null)
            return _table;
        var t = ShipWorks.Load(Path.Combine(PartKit.FindRoot(), "Constructs", "ShipWorks.json"));
        if (t.Problem.Length > 0)
            throw new InvalidOperationException(t.Problem);
        return _table = t;
    }

    /// <summary>The kinds a drive of this type and thrust needs, as the part file names them.</summary>
    public static List<object?> NeedsFor(ShipWorks t, string type, double force) => [.. t.NeedsFor(type, (float)force).Select(k => (object?)ConstructWorks.KindNames[(int)k])];

    /// <summary>A head's "works" block: what it needs behind it (a hull drive), or serviced outside (a pod).</summary>
    public static void Head(Part p, ShipWorks t, string type, double force, bool pod = false)
    {
        if (pod)
            p.Set(("works", Kw(("service", "outside"))));
        else
            p.Set(("works", Kw(("needs", NeedsFor(t, type, force)))));
    }

    /// <summary>Flags a ship's own piece (a stern, a keel length, a wall) as a mount: its heads' works bolt to it.</summary>
    public static void Mount(Part p) => p.Set(("worksPiece", Kw(("kind", "mount"))));
}
