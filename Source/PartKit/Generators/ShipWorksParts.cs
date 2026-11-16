using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Ship works (docs/sandbox/design/ship-works.md; Source/Constructs/ConstructWorks.cs): the generic family any ship builds
/// its drives from, in section <c>Ship fittings</c> with names that name no ship, written to Constructs/Parts/ShipWorks.
///
/// A hull drive is a <b>Drive Nozzle</b> on the outer face of a <b>Hull Mount</b> (a piece of armoured hull a head goes
/// through), with its works inside: a <b>Drive Core</b>, a long cylinder bolted to the mount's inner face on the nozzle's axis
/// and running into the ship, then a <b>Fuel Pump</b> and a <b>Coolant Exchanger</b> as the drive grows, each a section of
/// the same cylinder bolted on behind it (Karl, 2026-11-16: the engine goes on inside as one large cylindrical piece). A <b>Drive Pod</b> is the same drive with its works
/// in its cowl: hung outside on a pylon, a wide gimbal, serviced at the pod. Each head's "needs" come from
/// Constructs/ShipWorks.json's "drives" table (<see cref="Works.NeedsFor"/>), never picked by hand, and none of these
/// parts gets an Assembling recipe (Karl, 2026-11-16: they are made with the new production system).
///
/// Frames: a head's origin is the mount face it bolts to, its exhaust along +z. A mount's outer face is z 0 (+z, the head's
/// side), its inner face z -0.3. A works piece's origin is the middle of its front face (+z, towards the mount or the section
/// before it), its cylinder along -z on that axis, its saddles' feet at y -(r + <see cref="Saddle"/>).
/// </summary>
public sealed class ShipWorksParts : IPartGenerator
{
    public string Name => "ship_works";

    public const string Out = "Constructs/Parts/ShipWorks", Section = "Ship fittings", Group = "Ship";

    private const string Steel = "#5a6065", Trim = "#7b8186", Dark = "#34373a", Hazard = "#d8a020", Copper = "#8a5a3a", Glow = "#ffb060", Gauge = "#40e070", Bed = "#5c6057", Chamber = "#98a1a9";
    private const double Plate = 0.3; // a mount's thickness

    /// <summary>The three sizes: a letter, and per size the numbers below.</summary>
    private static readonly string[] SizeName = ["S", "M", "L"];

    // Hull Mount: half its width, mass, health.
    private static readonly double[] MountHalf = [0.75, 1.25, 2.0], MountMass = [500, 1200, 2500], MountHealth = [3000, 6000, 10000];

    // Drive Nozzle: thrust (N), collar radius, bell length, mouth radius, mass, health, spool.
    private static readonly double[] NozForce = [1.5e6, 5e6, 10e6], NozCollar = [0.45, 0.75, 1.05], NozLength = [1.3, 2.0, 2.8], NozMouth = [0.6, 1.0, 1.4], NozMass = [600, 1800, 4000], NozHealth = [3000, 6000, 9000], NozSpool = [1.2, 1.5, 2.0];

    // Drive Core: serves (N), width, height, depth, mass, health.
    public static readonly double[] CoreR = [0.6, 0.85, 1.1], CoreD = [1.8, 2.6, 3.2];
    private static readonly double[] CoreServes = [2e6, 6e6, 12e6], CoreMass = [1500, 3500, 7000], CoreHealth = [4000, 7000, 12000];

    // Fuel Pump (M, L: index 1, 2): serves, width, height, depth, mass, health.
    public static readonly double[] PumpD = [0, 1.0, 1.3];
    private static readonly double[] PumpServes = [0, 6e6, 12e6], PumpMass = [0, 900, 1800], PumpHealth = [0, 2500, 4000];
    public const double ExchangerD = 1.1, Saddle = 0.05; // (a works section stands on saddles this far under its cylinder: its deck socket)

    /// <summary>Where a size's works stack stands: its axis this high over the deck its saddles stand on.</summary>
    public static double AxisOver(int s) => CoreR[s] + Saddle;

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
        // Hazard corners on the outer face (the inner face is plain: a Drive Core's flange lies on it).
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
        p.Set(("worksPiece", Kw(("kind", "mount"))), ("collider", "boxes")); // (the ring is a look: its 24-sided tube has no hull, and one would fill the hole; the plate collides)
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

    // ---- the works: sections of one cylinder behind the nozzle ------------------------------------------------------

    /// <summary>A works section's sockets: its front face (to the mount, or the section before it), its back (the next section)
    /// and its saddles' feet.</summary>
    private static void PieceSockets(Part p, double r, double d)
    {
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        // (no side sockets: the gauge, a pump's motor and an exchanger's fins stand out of the sides, and the stack goes on along its line)
        p.Sock("deck", P(0, -Math.Round(r + Saddle, 3), Math.Round(-d / 2, 2)), "-y");
    }

    private const double Inside = 0.92; // a section's solid box's half width, a share of its round's radius

    /// <summary>A section's solid: the cylinder as a box a little inside its round, down to its saddles' feet, and the face that meets what is in front.</summary>
    private static void SectionHull(Part p, double r, double d)
    {
        var side = Math.Floor(r * Inside * 100) / 100;
        Hidden(p, -side, -r - Saddle, -d, side, side, -0.02);
        Hidden(p, -0.4, -0.4, -0.02, 0.4, 0.4, 0);
        p.Set(("collider", "boxes")); // (its round is a look: the fins' and rings' 24-sided tubes have no hull; the solid box stands for it)
    }

    /// <summary>A saddle under a section's cylinder at <paramref name="z"/>: a cradle from its feet up into the round.</summary>
    private static void SaddleAt(Part p, double r, double z, double half = 0.1)
    {
        Look(p, -r * 0.72, -r - Saddle + 0.003, z - half, r * 0.72, -r * 0.45, z + half, Dark, "dirty steel");
        Look(p, -r * 0.8, -r - Saddle + 0.003, z - half - 0.02, r * 0.8, -r - Saddle + 0.05, z + half + 0.02, Bed, "dirty steel"); // (its foot plate)
    }

    /// <summary>The flange where a section meets the mount or the section before it: the mount ring's own size on a core, else the section's.</summary>
    private static double FlangeR(int s) => Math.Min(NozCollar[s] + 0.24, CoreR[s] + Saddle - 0.01); // (no lower than its saddles' feet)

    /// <summary>A Drive Core: the reaction chamber, a long armoured cylinder bolted to the mount's inner face on the nozzle's
    /// axis, running into the ship: field coils round it, feed pipes along its top, a gauge on its side, saddles under it.</summary>
    private static Part Core(int s)
    {
        var (r, d) = (CoreR[s], CoreD[s]);
        var p = new Part($"Drive Core {SizeName[s]}", Small, Section, Desc($"A torch drive's reaction chamber: a cylinder {r * 2:0.#} m across and {d:0.#} m long that bolts to a Hull Mount's inner face right behind its nozzle and runs on into the ship. It serves {CoreServes[s] / 1e6:0} MN of nozzles through that mount; a bigger drive's Fuel Pump and Coolant Exchanger bolt on behind it, the same cylinder going on. Leave a metre clear by its gauge to work on it."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var fl = FlangeR(s);
        Cyl(p, 0, 0, fl, -0.12, -0.003, Trim, "dirty steel", segments: 24); // (the flange to the mount, the mount ring's size)
        Cyl(p, 0, 0, Math.Min(fl, r) * 0.82, -0.303, -0.117, Dark, "dirty steel", segments: 20); // (the neck through the flange)
        Cyl(p, 0, 0, r, -0.36, -d + 0.117, Chamber, "steel", segments: 24); // (the chamber, bare steel so it stands out from a painted deck: its ends inside its rings)
        Cyl(p, 0, 0, r * 1.04, -0.42, -0.3, Trim, "dirty steel", segments: 24); // (its front ring)
        Cyl(p, 0, 0, r * 1.04, -d + 0.12, -d + 0.003, Trim, "dirty steel", segments: 24); // (its back flange, for the section behind)
        foreach (var zf in new[] { 0.25, 0.55, 0.7 })
        {
            var z = -d * zf;
            Cyl(p, 0, 0, r + 0.03, z - 0.05, z + 0.05, Copper, "copper", inner: Math.Round(r / (r + 0.03), 3), segments: 24); // (field coils)
        }
        // Feed pipes along its top, on risers, and the gauge board on its starboard side.
        var pr = 0.05 + s * 0.015;
        var py = r + pr + 0.06;
        foreach (var x in new[] { -r * 0.3, r * 0.3 })
        {
            Cyl(p, x, py, pr, -d + 0.3, -0.5, Copper, "copper", segments: 8);
            foreach (var z in new[] { -d + 0.4, -0.6 })
                Look(p, x - pr * 0.7, r * 0.85, z - pr * 0.7, x + pr * 0.7, py - pr * 0.5, z + pr * 0.7, Copper, "copper");
        }
        var (gz, gh) = (-d * 0.4, d * 0.1); // (between the first two coils)
        Look(p, r * 0.9, -0.25, gz - gh, r + 0.1, 0.25, gz + gh, Dark, "painted plating"); // (the gauge board)
        Look(p, r + 0.1, -0.05, gz - gh * 0.6, r + 0.115, 0.17, gz + gh * 0.6, Gauge, "", glow: true);
        Look(p, r + 0.1, -0.17, gz - gh * 0.6, r + 0.115, -0.1, gz + gh * 0.6, Glow, "", glow: true);
        foreach (var z in new[] { -d * 0.25, -d * 0.8 })
            SaddleAt(p, r, z);
        SectionHull(p, r, d);
        PieceSockets(p, r, d);
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", "core"), ("serves", CoreServes[s]))));
        return p.Stats(CoreMass[s], CoreHealth[s], 30);
    }

    /// <summary>A Fuel Pump: the next section of the drive's cylinder, the turbopump's volute round it, its motor on its side,
    /// pipes forward along its top and a pressure gauge. It is full of fuel, and burns when it is holed.</summary>
    private static Part Pump(int s)
    {
        var (r, d) = (CoreR[s], PumpD[s]);
        var p = new Part($"Fuel Pump {SizeName[s]}", Small, Section, Desc($"A torch drive's fuel pump: a section of the drive's cylinder that bolts on behind its Drive Core (or to the Hull Mount itself). It serves {PumpServes[s] / 1e6:0} MN of nozzles through that mount. It is full of fuel, and burns when it is holed."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var rb = r * 0.94;
        Cyl(p, 0, 0, r * 1.02, -0.1, -0.003, Trim, "dirty steel", segments: 24); // (the front flange)
        Cyl(p, 0, 0, rb, -0.097, -d + 0.097, Hazard, "worn paint", segments: 24); // (the pump body: its ends inside its flanges)
        Cyl(p, 0, 0, r * 1.02, -d + 0.1, -d + 0.003, Trim, "dirty steel", segments: 24); // (the back flange)
        Cyl(p, 0, 0, r * 1.04, -d * 0.6, -d * 0.35, Dark, "dirty steel", inner: Math.Round(rb * 0.98 / (r * 1.04), 3), segments: 24); // (the volute, above its saddle's feet)
        // The motor on its starboard side, an outlet pipe forward along its top, the pressure gauge on the motor.
        var mr = r * 0.32;
        Cyl(p, rb + mr * 0.6, -r * 0.2, mr, -d * 0.85, -d * 0.2, Trim, "painted plating", segments: 12);
        Cyl(p, rb + mr * 0.6, -r * 0.2, mr * 1.08, -d * 0.85 - 0.003, -d * 0.78, Dark, "dirty steel", segments: 12); // (its end cap, a little past its end)
        var pr = 0.06 + s * 0.02;
        Cyl(p, 0, rb + pr + 0.04, pr, -d * 0.5, -0.12, Copper, "copper", segments: 8);
        Look(p, -pr * 0.7, rb * 0.9, -d * 0.5 - pr * 0.7, pr * 0.7, rb + 0.06, -d * 0.5 + pr * 0.7, Copper, "copper"); // (its riser)
        Look(p, rb + mr * 1.6 - 0.02, -r * 0.2 - 0.08, -d * 0.55 - 0.1, rb + mr * 1.6 + 0.01, -r * 0.2 + 0.12, -d * 0.55 + 0.1, Gauge, "", glow: true);
        SaddleAt(p, r, -d * 0.5);
        SectionHull(p, r, d);
        PieceSockets(p, r, d);
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", "pump"), ("serves", PumpServes[s]))));
        return p.Stats(PumpMass[s], PumpHealth[s], 15);
    }

    /// <summary>A Coolant Exchanger L: the last section of a heavy drive's cylinder, its body ringed with radiator fins, manifolds at both ends.</summary>
    private static Part Exchanger()
    {
        var (r, d) = (CoreR[2], ExchangerD);
        var p = new Part("Coolant Exchanger L", Small, Section, Desc("A heavy torch drive's coolant exchanger: the last section of the drive's cylinder, ringed with radiator fins, bolted on behind its Fuel Pump (or its Drive Core, or the Hull Mount). It serves 20 MN of nozzles through that mount."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        Cyl(p, 0, 0, r * 1.02, -0.1, -0.003, Copper, "copper", segments: 24); // (the front manifold)
        Cyl(p, 0, 0, r * 0.7, -0.097, -d + 0.097, Dark, "dirty steel", segments: 20); // (the body inside the fins: its ends inside the manifolds)
        Cyl(p, 0, 0, r * 1.02, -d + 0.1, -d + 0.003, Copper, "copper", inner: 0.8, segments: 24); // (the back manifold...)
        Cyl(p, 0, 0, r * 0.82, -d + 0.08, -d + 0.006, Trim, "dirty steel", segments: 24); // (...and its end plate)
        var n = 0;
        for (var z = -0.16; z > -d + 0.15; z -= 0.09, n++)
            Cyl(p, 0, 0, n % 2 == 0 ? r : r * 0.97, z - 0.02, z + 0.02, "#6a6f72", "steel", inner: 0.69, segments: 24); // (radiator fins, alternate ones a little smaller)
        foreach (var x in new[] { -r * 0.35, r * 0.35 })
            Look(p, x - 0.05, r * 0.9, -d + 0.12, x + 0.05, r + 0.06, -0.12, Copper, "copper"); // (coolant pipes along its top)
        Look(p, r * 0.9, 0.1, -d * 0.5 - 0.12, r + 0.06, 0.4, -d * 0.5 + 0.12, Dark, "painted plating"); // (its gauge board)
        Look(p, r + 0.06, 0.15, -d * 0.5 - 0.08, r + 0.075, 0.35, -d * 0.5 + 0.08, Gauge, "", glow: true);
        SaddleAt(p, r, -d * 0.5, 0.15);
        SectionHull(p, r, d);
        PieceSockets(p, r, d);
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
