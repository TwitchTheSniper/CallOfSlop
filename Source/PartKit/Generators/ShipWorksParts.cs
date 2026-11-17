using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Ship works (docs/sandbox/design/ship-works.md; Source/Constructs/ConstructWorks.cs): the generic family any ship builds
/// its drives from, in section <c>Ship fittings</c> with names that name no ship, written to Constructs/Parts/ShipWorks.
///
/// A hull drive is a <b>Drive Nozzle</b> on the outer face of a <b>Hull Mount</b> (a piece of armoured hull a head goes
/// through), with its works inside: a <b>Drive Core</b>, a long cylinder bolted to the mount's inner face on the nozzle's axis
/// and running into the ship, then a <b>Fuel Pump</b> and a <b>Coolant Exchanger</b> as the drive grows, each a section of
/// the same cylinder bolted on behind it (Karl, 2026-11-16: the engine goes on inside as one large cylindrical piece). A <b>Drive Nacelle</b> is a hollow housing hung
/// outside on a pylon with the same engine inside, behind its tail plate, mended through its <b>Service Hatch</b> (ship-works.md 15:
/// one engine a bell, joined to its plate, nothing shared, in hulls and in nacelles alike). Each head's "needs" come from
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

    private const string Clad = "#6c7378", Cable = "#262829", Steel = "#5a6065", Trim = "#7b8186", Dark = "#34373a", Hazard = "#d8a020", Copper = "#8a5a3a", Glow = "#ffb060", Gauge = "#40e070", Bed = "#5c6057", Chamber = "#98a1a9";
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

    public const double HullBurn = 2.0, NacelleBurn = 2.4; // l/s a MN: a hull drive with its works whole, and one in a nacelle (its smaller core)

    // Turret Ring: the race's radius and height over the deck, the trunk's radius and depth under it, torque (N m), speed (deg/s), mass, health, power (W).
    public static readonly double[] RingR = [0.5, 0.8, 1.2], RingH = [0.25, 0.32, 0.4], TrunkR = [0.3, 0.5, 0.75], TrunkD = [0.6, 1.0, 1.5];
    private static readonly int[] RingTorque = [12000, 36000, 100000], RingSpeed = [60, 45, 30];
    private static readonly double[] RingMass = [400, 1200, 3000], RingHealth = [2500, 5000, 9000], RingPower = [20, 40, 80];

    // Autoloader: guns it serves, width, height, depth, mass, health (its height fits its ring's trunk).
    private static readonly double[] LoaderServes = [1, 2, 4], LoaderW = [0.5, 0.8, 1.1], LoaderH = [0.5, 0.8, 1.2], LoaderD = [0.6, 0.9, 1.3], LoaderMass = [300, 700, 1500], LoaderHealth = [1500, 3000, 5000];

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

    /// <summary>A cylinder standing along y (a cylinder shape turned up), its axis at (x, z): inner a tube's bore.</summary>
    private static void Up(Part p, double x, double z, double r, double y0, double y1, string color, string finish, double inner = 0, int segments = 16)
    {
        var shape = Kw(("shape", inner > 0 ? "tube" : "cylinder"), ("segments", segments), ("front", "+y"));
        if (inner > 0)
            shape["inner"] = inner;
        shape["finish"] = finish;
        p.Add(Box(P(x - r, Math.Min(y0, y1), z - r), P(x + r, Math.Max(y0, y1), z + r), color, detail: true, shape: shape));
    }

    // ---- the turret ring and its autoloader -------------------------------------------------------------------------

    /// <summary>
    /// A Turret Ring (ship-works.md 5.2): a turntable whose base carries a trunk down through the deck. Its frame's origin is
    /// the deck's top at its middle: the race stands on the deck (y 0 to its height), the trunk hangs under it, with sockets on
    /// its sides for Autoloaders and an Ammo in at its foot. A feed into the Ammo in feeds every gun standing on the ring, with
    /// no link across the joint; the trunk is a mount, so works bolted to it serve those guns.
    /// </summary>
    private static Part Ring(int s)
    {
        var (r, h, tr, td) = (RingR[s], RingH[s], TrunkR[s], TrunkD[s]);
        var p = new Part($"Turret Ring {SizeName[s]}", Small, Section, Desc($"A turret's ring, {r * 2:0.#} m across and rated {RingTorque[s]} N m, with a trunk {td:0.#} m deep under it through the deck. An ammo feed into its Ammo in, at the trunk's foot, feeds every gun on it, and Autoloaders bolt to the trunk's sides. Signal 1 or -1 turns it, 0 holds."),
            role: "joint", tool: "wrench", material: "steel", color: Steel, group: Group);
        Hidden(p, -r, 0, -r, r, h, r); // (the race)
        Hidden(p, -tr, -td, -tr, tr, 0, tr); // (the trunk)
        Up(p, 0, 0, r, 0.003, 0.08, Dark, "dirty steel", segments: 24); // (the flange on the deck)
        Up(p, 0, 0, r * 0.9, 0.075, h - 0.045, Trim, "dirty steel", segments: 24); // (the race)
        Up(p, 0, 0, r * 0.84, h - 0.05, h - 0.003, Steel, "painted plating", segments: 24); // (the turning plate)
        p.Boxes[^1]!["side"] = "leaf";
        Up(p, 0, 0, r * 0.91, h * 0.45, h * 0.45 + 0.04, Hazard, "hazard paint", inner: Math.Round(r * 0.88 / (r * 0.91), 3), segments: 24); // (a hazard band round the race)
        for (var k = 0; k < 12; k++)
        {
            var a = k * Math.PI / 6 + Math.PI / 12;
            var (x, z) = (r * 0.95 * Math.Cos(a), r * 0.95 * Math.Sin(a));
            Look(p, x - 0.025, 0.077, z - 0.025, x + 0.025, 0.11, z + 0.025, "#9a9c98", "steel"); // (the flange's bolts)
        }
        Up(p, 0, 0, tr, -td + 0.08, -0.003, Steel, "painted plating"); // (the trunk)
        foreach (var y in new[] { -td * 0.3, -td * 0.7 })
            Up(p, 0, 0, tr + 0.02, y - 0.03, y + 0.03, Dark, "dirty steel", inner: Math.Round(tr / (tr + 0.02), 3)); // (its hoops)
        Up(p, 0, 0, tr * 1.06, -td + 0.003, -td + 0.085, Dark, "dirty steel"); // (its foot)
        Look(p, -0.12, -td - 0.08, -0.12, 0.12, -td + 0.003, 0.12, Hazard, "worn paint"); // (the Ammo in's mouth)
        foreach (var b in p.Boxes)
            if (Py.Truthy(b.Get("detail")) && !b.Has("side"))
                b["side"] = "base"; // (everything but the turning plate stands still with the deck)
        p.Sock("base", P(0, 0, 0), "-y");
        p.Sock("top", P(0, h, 0), "+y");
        var mid = Math.Round(-td / 2, 2);
        p.Sock("trunk+x", P(tr, mid, 0), "+x");
        p.Sock("trunk-x", P(-tr, mid, 0), "-x");
        p.Sock("trunk+z", P(0, mid, tr), "+z");
        p.Sock("trunk-z", P(0, mid, -tr), "-z");
        var speed = RingSpeed[s];
        var torque = RingTorque[s];
        p.Set(("power", RingPower[s]),
            ("joint", Kw(("type", "turntable"), ("axis", "+y"), ("pivot", Py.L(0, Py.Round(h, 4), 0)), ("leaf", "top"), ("speed", speed), ("strength", torque), ("give", 20), ("overloadDamage", 0))),
            ("config", Py.L(
                Kw(("key", "speed"), ("label", "Speed"), ("group", "Motor"), ("unit", "deg/s"), ("min", 0.0), ("max", (double)2 * speed), ("step", 1), ("tooltip", $"How fast it turns: rated {speed} deg/s (0%), from stopped (-100%) to {2 * speed} deg/s (+100%).")),
                Kw(("key", "strength"), ("label", "Torque"), ("group", "Motor"), ("unit", "N m"), ("min", torque / 10.0), ("max", (double)torque), ("step", 100), ("tooltip", $"What it holds, up to {torque} N m.")))),
            ("ports", Py.L(Kw(("name", "Ammo in"), ("type", "feed"), ("dir", "in"), ("at", Py.L(0, Py.Round(-td, 4), 0)), ("face", "-y")))),
            ("worksPiece", Kw(("kind", "mount"))),
            ("collider", "boxes")); // (the race and trunk are drawn as cylinders: the hidden boxes collide)
        return p.Stats(RingMass[s], RingHealth[s], 30);
    }

    /// <summary>An Autoloader: a rammer on a frame, a shell tray and its hoist, bolted to a Turret Ring's trunk (its front face).</summary>
    private static Part Loader(int s)
    {
        var (w, h, d) = (LoaderW[s], LoaderH[s], LoaderD[s]);
        var n = (int)LoaderServes[s];
        var p = new Part($"Autoloader {SizeName[s]}", Small, Section, Desc($"A turret's autoloader: bolt it to a Turret Ring's trunk. It serves {(n == 1 ? "1 gun" : $"{n} guns")} on the ring while the ring is fed: a gun of six rounds or fewer reloads in well under half its time, a belt-fed one doesn't stop between loads."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var y0 = -h / 2;
        Solid(p, -w / 2, y0, -d, w / 2, y0 + 0.08, -0.02, Bed, "dirty steel"); // (its base frame)
        foreach (var x in new[] { -w / 2 + 0.04, w / 2 - 0.04 })
            Look(p, x - 0.035, y0 + 0.083, -d + 0.04, x + 0.035, h / 2 - 0.003, -d + 0.11, Dark, "dirty steel"); // (the frame's posts at the back...)
        Look(p, -w / 2 + 0.004, h / 2 - 0.07, -d + 0.04, w / 2 - 0.004, h / 2 - 0.003, -0.06, Dark, "dirty steel"); // (...and the rail over it)
        var tr = Math.Min(w, h) * 0.18;
        Cyl(p, 0, y0 + h * 0.42, tr, -d + 0.12, -0.05, Hazard, "worn paint", segments: 12); // (the rammer)
        Cyl(p, 0, y0 + h * 0.42, tr * 0.45, -0.05, -0.003, Trim, "steel", segments: 10); // (its head)
        Look(p, -w * 0.32, y0 + 0.083, -d * 0.75, w * 0.32, y0 + h * 0.25, -d * 0.2, Trim, "painted plating"); // (the shell tray)
        Cyl(p, w * 0.3, y0 + h * 0.75, tr * 0.5, -d + 0.12, -d * 0.3, Copper, "copper", segments: 8); // (a hydraulic ram)
        Look(p, -w / 2 + 0.06, y0 + h * 0.6, -d * 0.3, -w / 2 + 0.09, y0 + h * 0.8, -d * 0.1, Gauge, "", glow: true); // (its ready lamp)
        Look(p, -w * 0.4, y0 + 0.078, -d * 0.18, w * 0.4, y0 + 0.086, -0.03, Hazard, "hazard paint"); // (a hazard band on the frame's front edge: this face feeds the ring)
        Hidden(p, -w / 2, y0 + 0.08, -d, w / 2, h / 2, -0.02);
        Hidden(p, -0.2, -0.2, -0.02, 0.2, 0.2, 0); // (the face that meets the trunk)
        p.Sock("front", P(0, 0, 0), "+z");
        p.Sock("back", P(0, 0, -d), "-z");
        p.Sock("+x", P(w / 2, 0, Math.Round(-d / 2, 2)), "+x");
        p.Sock("-x", P(-w / 2, 0, Math.Round(-d / 2, 2)), "-x");
        p.Sock("deck", P(0, -h / 2, Math.Round(-d / 2, 2)), "-y");
        p.Set(("onBreak", "disable"), ("worksPiece", Kw(("kind", "loader"), ("serves", LoaderServes[s]))));
        return p.Stats(LoaderMass[s], LoaderHealth[s], 20);
    }

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

    // ---- the nacelles: a hollow clad housing round the same engine, mended through a hatch --------------------------

    // Drive Nacelle (S, M): the housing's flat distance and the flats' half length, its length (tail plate to nose), mass, health.
    private static readonly double[] NacF = [0.9, 1.15], NacLen = [3.0, 4.8], NacMass = [1400, 3200], NacHealth = [3000, 5000];

    /// <summary>
    /// A Drive Nacelle (ship-works.md 15, rule 5): a hollow, clad housing hung outside on a pylon (its top), its tail plate a mount.
    /// A Drive Nozzle bolts to the tail plate outside; inside, behind it, is the same engine a hull drive has: a Drive Core (S), or a
    /// Drive Core and a Fuel Pump (M), bolted to the plate's inner face on the nozzle's axis. Only the cowl's thin armour is round
    /// it, and it is mended from outside, through the service hatch on its starboard flank.
    /// </summary>
    private static Part Nacelle(int s)
    {
        var (f, len) = (NacF[s], NacLen[s]);
        var p = new Part($"Drive Nacelle {SizeName[s]}", Small, Section, Desc($"A drive nacelle: a hollow armoured housing hung outside a ship on a pylon (its top). Its tail plate is a Hull Mount: a Drive Nozzle {SizeName[s]} bolts to it outside, and its engine ({(s == 0 ? "a Drive Core S" : "a Drive Core M and a Fuel Pump M")}) to the plate's inner face, inside. It takes no room in the hull, but has only the cowl's armour, and is mended through its hatch from outside: on a space walk, or landed."),
            role: "frame", tool: "welder", material: "steel", color: Steel, group: Group);
        var c = EngineHousing.HalfFlat(f);
        var z0 = -len + 0.6; // (the bulkhead behind the nose)
        EngineHousing.Draw(p, (x, y, z) => P(x, y, z), 0, 0, f, c, z0, 0, 1, Steel, Clad, Dark, Hazard, "worn paint", CoreR[s], CoreD[s], s == 0 ? CoreD[0] : CoreD[1] + PumpD[1]);
        // The nose: a short cone forward of the bulkhead.
        EngineHousing.Ring(p, (x, y, z) => P(x, y, z), 0, 0, f * 0.45, c * 0.45, -len, f, c, z0 - 0.004, Steel, "worn paint");
        // A grab rail along the top, a band of hazard paint round the tail.
        foreach (var z in new[] { -len * 0.7, -len * 0.3 })
            Look(p, -0.03, f + 0.02, z - 0.03, 0.03, f + 0.2, z + 0.03, "#9a9c98", "steel");
        Look(p, -0.025, f + 0.18, -len * 0.7 - 0.04, 0.025, f + 0.23, -len * 0.3 + 0.04, Hazard, "worn paint");
        p.Sock("pylon", P(0, f + 0.03, Math.Round(-len / 2, 2)), "+y");
        Solid(p, -0.3, f - 0.06, -len / 2 - 0.4, 0.3, f + 0.03, -len / 2 + 0.4, Dark, "dirty steel"); // (the pylon's pad, through the shell and 10 mm proud of it)
        p.Set(("worksPiece", Kw(("kind", "mount"), ("service", "outside"))), ("collider", "boxes"));
        return p.Stats(NacMass[s], NacHealth[s], 15);
    }

    /// <summary>The Service Hatch: a hinged armoured lid for a nacelle's service opening (1 by 0.8 m), opened with F from outside.</summary>
    private static Part Hatch()
    {
        var (w, h) = (EngineHousing.HatchW - 0.04, EngineHousing.HatchH - 0.04);
        var p = new Part(EngineHousing.HatchPart, Small, Section, Desc("A hinged armoured lid for a drive nacelle's service opening, 1 by 0.8 m: F opens it from outside, to reach the engine behind it."),
            role: "door", tool: "wrench", material: "steel", color: Clad, group: Group);
        p.Add(Box([-0.02, -h / 2, -0.02], [w, h / 2, 0.02], Clad, shape: Kw(("finish", "painted plating"))));
        p.Add(Box([0.06, -h / 2 + 0.06, -0.026], [w - 0.06, h / 2 - 0.06, 0.026], Trim, detail: true, shape: Kw(("finish", "painted plating"))));
        p.Add(Box([w - 0.16, -0.12, -0.06], [w - 0.1, 0.12, -0.026], "#9a9c98", detail: true, shape: Kw(("finish", "steel"), ("use", true), ("paintable", false)))); // (its handle, outside)
        foreach (var y in new[] { -h / 2 + 0.1, h / 2 - 0.1 })
            p.Add(Box([0.02, y - 0.04, -0.035], [0.1, y + 0.04, -0.02], Dark, detail: true, shape: Kw(("finish", "dirty steel")))); // (hinge straps)
        p.Sock("hang", [-0.02, 0, 0], "-x");
        p.Set(("door", Kw(("kind", "swing"), ("axis", "+y"), ("pivot", Py.L(0, 0, -0.02)), ("angle", 90), ("seconds", 0.8), ("hang", "hang"))));
        return p.Stats(90, 1500, 25, 2.0);
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

    /// <summary>Bolt heads round a flange or ring: n of them at radius rr, on the band from z0 to z1 (a cylinder's joint).</summary>
    private static void Bolts(Part p, double rr, double z0, double z1, int n, double phase = 0)
    {
        for (var k = 0; k < n; k++)
        {
            var a = phase + Math.PI / n + k * 2 * Math.PI / n;
            var (x, y) = (rr * Math.Cos(a), rr * Math.Sin(a));
            Look(p, x - 0.022, y - 0.022, z0, x + 0.022, y + 0.022, z1, "#9a9c98", "steel");
        }
    }

    /// <summary>
    /// A band of cladding round a section from z0 to z1 (z0 nearer the front): two panels with a seam between them, a row of
    /// fasteners along each end (rule 2 of ship-works.md 15: a detailed, clad cylinder, never a bare tube).
    /// </summary>
    private static void Cladding(Part p, double r, double z0, double z1, string color = Clad)
    {
        var rc = Math.Round(Math.Max(r + 0.014, r / 0.975), 3); // (a tube's bore is at most 0.98 of it)
        var seam = (z0 + z1) / 2;
        Cyl(p, 0, 0, rc, z1, seam - 0.012, color, "painted plating", inner: Math.Round(r / rc, 3), segments: 24);
        Cyl(p, 0, 0, rc, seam + 0.012, z0, color, "painted plating", inner: Math.Round(r / rc, 3), segments: 24);
        foreach (var z in new[] { z0 - 0.05, z1 + 0.05 })
            Bolts(p, rc + 0.006, z - 0.02, z + 0.02, 6, Math.PI / 6); // (fasteners off the top and the sides, where the pipes and boards are)
    }

    /// <summary>A service panel on a section's side (sx -1 port, +1 starboard): a hatch plate a person opens to work on it, hazard edges, a handle.</summary>
    private static void ServicePanel(Part p, double r, double sx, double z0, double z1)
    {
        var (h, x0, x1) = (Math.Min(0.24, r * 0.4), r * 0.93, r + 0.04);
        Look(p, sx * x0, -h, z1, sx * x1, h, z0, Trim, "painted plating");
        Look(p, sx * x1, -h, z1, sx * (x1 + 0.012), h, z1 + 0.04, Hazard, "hazard paint");
        Look(p, sx * x1, -h, z0 - 0.04, sx * (x1 + 0.012), h, z0, Hazard, "hazard paint");
        Look(p, sx * x1, -0.04, (z0 + z1) / 2 - 0.08, sx * (x1 + 0.05), 0.04, (z0 + z1) / 2 + 0.08, "#9a9c98", "steel"); // (its handle)
    }

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
        var (gz, gh) = (-d * 0.825, d * 0.075); // (on the clad band behind the last coil: the service panels are forward)
        Look(p, r * 0.9, -0.25, gz - gh, r + 0.1, 0.25, gz + gh, Dark, "painted plating"); // (the gauge board)
        Look(p, r + 0.1, -0.05, gz - gh * 0.6, r + 0.115, 0.17, gz + gh * 0.6, Gauge, "", glow: true);
        Look(p, r + 0.1, -0.17, gz - gh * 0.6, r + 0.115, -0.1, gz + gh * 0.6, Glow, "", glow: true);
        foreach (var z in new[] { -d * 0.25, -d * 0.8 })
            SaddleAt(p, r, z);
        // The cladding (ship-works.md 15, rule 2): a band between the first two coils and one behind the last, each two panels
        // with a seam and their fasteners; bolts round the front ring and the back flange; a service panel to port, opposite the
        // gauge; a cable run along its port side under the panel.
        Cladding(p, r, -d * 0.25 - 0.07, -d * 0.55 + 0.07);
        Cladding(p, r, -d * 0.7 - 0.07, -d + 0.14);
        Bolts(p, r * 1.04 + 0.012, -0.4, -0.32, 10);
        Bolts(p, r * 1.04 + 0.012, -d + 0.03, -d + 0.1, 10);
        foreach (var sx in new[] { -1, 1 })
            ServicePanel(p, r, sx, -d * 0.31, -d * 0.49); // (one each side: whichever a person reaches, through a nacelle's hatch or from a walkway)
        var cy = -r * 0.62;
        var cx = -Math.Sqrt(r * r - cy * cy) - 0.02;
        Look(p, cx - 0.035, cy - 0.035, -d + 0.16, cx + 0.035, cy + 0.035, -0.44, Cable, "rubber");
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
        Bolts(p, r * 1.02 + 0.012, -0.09, -0.012, 10);
        Bolts(p, r * 1.02 + 0.012, -d + 0.012, -d + 0.09, 10);
        ServicePanel(p, r, -1, -d * 0.3, -d * 0.7);
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
        Bolts(p, r * 1.02 + 0.012, -0.09, -0.012, 10);
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
        parts.Add(Nacelle(0));
        parts.Add(Nacelle(1));
        parts.Add(Hatch());
        for (var s = 0; s < 3; s++)
            parts.Add(Ring(s));
        for (var s = 0; s < 3; s++)
            parts.Add(Loader(s));
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

    /// <summary>A main drive's "works" block: the engine parts it needs behind its bell, and for a drive on a nacelle's tail
    /// (<paramref name="nacelle"/>) that they are serviced outside, through the nacelle's hatch. Lift jets and retros get none
    /// (ship-works.md 15, rule 6: only main drives have engines).</summary>
    public static void Head(Part p, ShipWorks t, string type, double force, bool nacelle = false)
    {
        if (nacelle)
            p.Set(("works", Kw(("needs", NeedsFor(t, type, force)), ("service", "outside"))));
        else
            p.Set(("works", Kw(("needs", NeedsFor(t, type, force)))));
    }

    /// <summary>Flags a ship's own piece (a stern, a collar, a nacelle) as a mount: each drive through it has its engine bolted to
    /// its inner face, on the drive's axis. <paramref name="outside"/>: a nacelle's, mended through its hatch from outside.</summary>
    public static void Mount(Part p, bool outside = false) =>
        p.Set(("worksPiece", outside ? Kw(("kind", "mount"), ("service", "outside")) : Kw(("kind", "mount"))));

    /// <summary>
    /// The Service Hatch on a nacelle's <c>"hatch"</c> socket (EngineHousing), turned so it is upright in the opening and swings
    /// outwards: its far edge goes aft, its inner face (its +z) faces the engine. <paramref name="side"/>: the flat it is in (+1: +x).
    /// </summary>
    public static int HangHatch(BuildingParts.Blueprint bp, int on, int side)
    {
        for (var r = 0; r < 24; r++)
            if (BuildingParts.Apply(r, [1, 0, 0]) is [0, 0, 1] && BuildingParts.Apply(r, [0, 0, 1]) is var z && z[0] == -side && z[1] == 0 && z[2] == 0)
                return bp.HangTurned(EngineHousing.HatchPart, on, "hatch", "hang", r);
        throw new InvalidOperationException("no turn hangs a Service Hatch that way");
    }
}

/// <summary>
/// An engine housing (ship-works.md 15, rule 5): the tail of a nacelle or a pod made hollow round the same engine a hull drive has.
/// An octagon of clad plates (flats <c>f</c> from its axis, each flat <c>2c</c> wide) from a bulkhead at <c>z0</c> back to its tail
/// at <c>z1</c>; the tail plate (0.3 m, the last of it) is what the drive bolts to outside and the engine's front flange inside; a
/// 1 by 0.8 m service opening in the flat on side <c>side</c> (+1: +x), beside the engine's service panel, with a hatch socket on
/// its forward jamb; rails under the engine's saddles. Every generator that makes a nacelle calls this, so they agree.
/// Coordinates go through <c>at</c> (a generator's own half-cell shift); +z is aft.
/// </summary>
public static class EngineHousing
{
    public const double HatchW = 1.0, HatchH = 0.8, Wall = 0.04, TailPlate = 0.3;
    public const string HatchPart = "Service Hatch 1x0.8 m";

    /// <summary>The flats' half width for flat distance f: a regular octagon's, or wider so the service opening fits in one.</summary>
    public static double HalfFlat(double f) => Math.Round(Math.Max(f * Math.Tan(Math.PI / 8), HatchH / 2 + 0.06), 3);

    /// <summary>The octagon's corners at z (flat f, half width c), round (cx, cy), starting on +x above the axis and going round.</summary>
    public static double[][] Octagon(Func<double, double, double, double[]> at, double cx, double cy, double f, double c, double z) =>
        [at(cx + f, cy + c, z), at(cx + c, cy + f, z), at(cx - c, cy + f, z), at(cx - f, cy + c, z), at(cx - f, cy - c, z), at(cx - c, cy - f, z), at(cx + c, cy - f, z), at(cx + f, cy - c, z)];

    /// <summary>A convex polygon as a drawn plate <paramref name="thick"/> thick about it (a "hull" shape).</summary>
    public static void Sheet(Part p, double[][] pts, string color, string finish, double thick = Wall)
    {
        double[]? n = null;
        for (var i = 2; i < pts.Length && n == null; i++)
        {
            var a = pts[1].Zip(pts[0], (u, v) => u - v).ToArray();
            var b = pts[i].Zip(pts[0], (u, v) => u - v).ToArray();
            double[] cr = [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
            var l = Math.Sqrt(cr[0] * cr[0] + cr[1] * cr[1] + cr[2] * cr[2]);
            if (l > 1e-4)
                n = [cr[0] / l, cr[1] / l, cr[2] / l];
        }
        if (n == null)
            return;
        var both = new List<double[]>();
        foreach (var q in pts)
            foreach (var sg in new[] { -1, 1 })
                both.Add([.. Enumerable.Range(0, 3).Select(k => Py.Round(q[k] + sg * n[k] * thick / 2, 4))]);
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        p.Add(Box(mn, mx, color, detail: true, shape: Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false), ("finish", finish))));
    }

    /// <summary>A solid between two octagons (a nose cone, a collar): drawn only.</summary>
    public static void Ring(Part p, Func<double, double, double, double[]> at, double cx, double cy, double fa, double ca, double za, double fb, double cb, double zb, string color, string finish)
    {
        var both = Octagon(at, cx, cy, fa, ca, za).Concat(Octagon(at, cx, cy, fb, cb, zb)).Select(q => q.Select(x => Py.Round(x, 4)).ToArray()).ToList();
        double[] mn = [.. Enumerable.Range(0, 3).Select(k => both.Min(q => q[k]))];
        double[] mx = [.. Enumerable.Range(0, 3).Select(k => both.Max(q => q[k]))];
        p.Add(Box(mn, mx, color, detail: true, shape: Kw(("shape", "hull"), ("points", both.Select(q => (object?)q.Select(x => (object?)x).ToList()).ToList()), ("paintable", false), ("finish", finish))));
    }

    private static void Hid(Part p, Func<double, double, double, double[]> at, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        if (Math.Abs(x1 - x0) < 0.05 || Math.Abs(y1 - y0) < 0.05 || Math.Abs(z1 - z0) < 0.05)
            return;
        var a = at(Math.Min(x0, x1), Math.Min(y0, y1), Math.Min(z0, z1));
        var b = at(Math.Max(x0, x1), Math.Max(y0, y1), Math.Max(z0, z1));
        p.Add(Box(a, b, hidden: true));
    }

    private static void Solid(Part p, Func<double, double, double, double[]> at, double x0, double y0, double z0, double x1, double y1, double z1, string color, string finish, bool detail = false)
    {
        var (a, b) = (at(x0, y0, z0), at(x1, y1, z1)); // (at may mirror: the corners are sorted after it)
        p.Add(Box([.. a.Zip(b, Math.Min)], [.. a.Zip(b, Math.Max)], color, detail: detail, shape: Kw(("finish", finish))));
    }

    /// <summary>Where the service opening's forward edge is: centred on the engine's service panels (its Drive Core's, coreD long, bolted at z1 less the tail plate).</summary>
    public static double HatchZ(double z1, double coreD) => Math.Round(z1 - TailPlate - coreD * 0.4 - HatchW / 2, 3);

    /// <summary>
    /// Draws the housing (see the class) and its sockets: <c>"core"</c> on the tail plate's inner face (a Drive Core's front, on the
    /// axis), <c>"drive"</c> on its outer face (the drive's front), <c>"hatch"</c> on the opening's forward jamb (the Service Hatch's
    /// hang). <paramref name="coreR"/> and <paramref name="stackLen"/>: the engine's radius and length, for the rails under its saddles.
    /// </summary>
    public static void Draw(Part p, Func<double, double, double, double[]> at, double cx, double cy, double f, double c, double z0, double z1, int side,
        string shell, string shell2, string frame, string hazard, string finish, double coreR, double coreD, double stackLen)
    {
        var zt = z1 - TailPlate;
        var hz0 = HatchZ(z1, coreD);
        var hz1 = hz0 + HatchW;
        var hy = HatchH / 2;
        if (hz0 < z0 + 0.1 || hz1 > zt - 0.05)
            throw new InvalidOperationException($"{p.D["name"]}: the service opening ({hz0:0.00} to {hz1:0.00}) doesn't fit between the bulkhead ({z0:0.00}) and the tail plate ({zt:0.00})");
        if (coreR + 0.12 > f - Wall)
            throw new InvalidOperationException($"{p.D["name"]}: an engine {coreR:0.00} m round doesn't fit in a housing {f:0.00} m to its flats");
        // The shell: eight clad plates, the one on the hatch's side cut round the opening (strips over and under it).
        double[] V(int i, double z) => Octagon(at, cx, cy, f, c, z)[i & 7];
        double[] Q(double x, double y, double z) => at(cx + side * x, cy + y, z);
        var flat = side > 0 ? 7 : 3; // (the facet from corner 7 to 0 is the +x flat; from 3 to 4 the -x flat)
        for (var i = 0; i < 8; i++)
        {
            var col = i % 2 == 0 ? shell : shell2;
            var e = i % 2 == 0 ? 0 : 0.0025; // (neighbouring plates meet at their edges: their ends are staggered, so no two end caps share a plane)
            var (za, zs) = (z0 + e, z1 - 0.003 - e);
            if (i != flat)
            {
                Sheet(p, [V(i, za), V(i + 1, za), V(i + 1, zs), V(i, zs)], col, finish);
                continue;
            }
            // (the cut plate stops 4 mm short of the jambs, which cover its ends)
            Sheet(p, [Q(f, -c, za), Q(f, c, za), Q(f, c, hz0 - 0.004), Q(f, -c, hz0 - 0.004)], col, finish);
            Sheet(p, [Q(f, -c, hz1 + 0.004), Q(f, c, hz1 + 0.004), Q(f, c, zs), Q(f, -c, zs)], col, finish);
            if (c - hy > 0.015)
                foreach (var sy in new[] { -1, 1 })
                    Sheet(p, [Q(f, sy * hy, hz0 + 0.004), Q(f, sy * c, hz0 + 0.004), Q(f, sy * c, hz1 - 0.004), Q(f, sy * hy, hz1 - 0.004)], col, finish);
        }
        // The opening's frame on the outside: dark jambs, hazard over and under it.
        var xo = f + Wall / 2;
        // (the forward jamb is solid: the hatch hangs on its aft face, through the shell's thickness)
        Solid(p, Q, f - 0.06, -hy - 0.05, hz0 - 0.05, xo + 0.02, hy + 0.05, hz0, frame, "painted steel");
        Solid(p, Q, xo - 0.003, -hy - 0.05, hz1, xo + 0.02, hy + 0.05, hz1 + 0.05, frame, "painted steel", detail: true);
        foreach (var sy in new[] { -1, 1 })
            Solid(p, Q, xo - 0.003, sy * (hy + 0.003), hz0, xo + 0.02, sy * (hy + 0.048), hz1, hazard, "hazard paint", detail: true);
        // The bulkhead, and a collar round the tail.
        Sheet(p, [.. Octagon(at, cx, cy, f - 0.01, c - 0.004, z0 + 0.026)], frame, "dirty steel"); // (its face 6 mm in from the plates' ends)
        Ring(p, at, cx, cy, f + 0.03, c + 0.012, zt + 0.06, f + 0.03, c + 0.012, z1 - 0.06, frame, "dirty steel");
        // The tail plate: the mount, as thick as the hull there, its inner face where the engine bolts on.
        Ring(p, at, cx, cy, f - Wall / 2 - 0.006, c - 0.003, zt, f - Wall / 2 - 0.006, c - 0.003, z1 - 0.008, frame, "plated steel");
        var a = Math.Round(Math.Min(f - Wall, (f + c) / 2) * 0.96, 3);
        Hid(p, at, cx - a, cy - a, zt, cx + a, cy + a, z1);
        // The walls a player stands against (the corners' gaps are too small to pass), the opening left clear; the bulkhead.
        var (wi, wo) = (f - 0.085, f - Wall / 2 - 0.005);
        Hid(p, at, cx - c, cy + wi, z0 + 0.06, cx + c, cy + wo, zt);
        Hid(p, at, cx - c, cy - wo, z0 + 0.06, cx + c, cy - wi, zt);
        Hid(p, at, cx - side * wo, cy - c, z0 + 0.06, cx - side * wi, cy + c, zt);
        Hid(p, at, cx + side * wi, cy - c, z0 + 0.06, cx + side * wo, cy + c, hz0);
        Hid(p, at, cx + side * wi, cy - c, hz1, cx + side * wo, cy + c, zt);
        var b = Math.Round((f + c) / 2 * 0.96, 3);
        Hid(p, at, cx - b, cy - b, z0, cx + b, cy + b, z0 + 0.06);
        // Rails under the engine's saddles, from the shell's floor up to its feet.
        var feet = coreR + ShipWorksParts.Saddle;
        if (wi - feet > 0.06)
            foreach (var sx in new[] { -1, 1 })
                Solid(p, at, cx + sx * coreR * 0.62 - 0.05, cy - wi, zt - stackLen + 0.1, cx + sx * coreR * 0.62 + 0.05, cy - feet - 0.003, zt - 0.1, frame, "dirty steel");
        p.Sock("core", at(cx, cy, zt), "-z");
        p.Sock("drive", at(cx, cy, z1), "+z");
        p.Sock("hatch", at(cx + side * f, cy, hz0), "+z");
    }
}
