using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// What moves and powers big constructs (Constructs/Parts/BigDrive): docs/sandbox/CONSTRUCTS.md, "Big drive". Was tools/constructs/big_drive_parts.py.
///
/// The size classes come from PartScale (Medium x4, Large x16, Capital x64 by linear size). The drive's numbers don't scale by one rule:
/// mass, power and force grow differently, so each family has its table here, by class, and the tables at the top are where the balance
/// lives. A traction city (100,000 t, 300 m, g 18 m/s2: 1.8 GN of weight) is what Capital is sized for: four Capital Bogies on hub motors,
/// a Boiler or two, smoke out of Chimneys. Every block has odd cell counts, so its origin is the centre of its middle cell and every
/// socket stays on the class's lattice. The shared helpers (Health, Block, Faces, Bands, Half, the ports, Mount, DriveIn and the
/// Springs, Ride and Tanks tables) are public for the generators that build on them (air, boost, fluid tanks, suspensions).
/// Values keep their Python types (an int stays an int in the files), so the tables hold object where a row mixes them.
/// </summary>
public sealed class BigDriveParts : IPartGenerator
{
    public string Name => "big_drive_parts";

    private const string Out = "Constructs/Parts/BigDrive";
    private static readonly Cls[] Big = [Medium, Large, Capital];

    // --- The tables (class -> numbers) ---

    private sealed record EngineRow(Cls C, int Nx, int Ny, int Nz, int T, int Hp, int Arm, long Torque, int Idle, int Peak, int Top, object Burn, int Spool, int Skj);

    private sealed record Engine(string Word, string Desc, string Color, string Curve, bool Starter, double Noise, EngineRow[] By);

    // name: cells (x, y, z) per class, mass t, health, armour, torque N m, idle, peak, max rpm, curve, burn l/s, starter, spool s, noise
    private static readonly Engine[] Engines =
    [
        new("Diesel Engine", "Huge torque at low revs. Needs a starter battery.", "#3d4a5c", "diesel", true, 0.9, [
            new(Medium, 3, 3, 5, 9, 9000, 30, 9000, 500, 1200, 2000, 0.6, 0, 50),
            new(Large, 3, 3, 5, 400, 60000, 60, 300000, 200, 500, 800, 8, 0, 2000),
            new(Capital, 1, 1, 3, 2400, 250000, 120, 8000000, 30, 102, 120, 45, 0, 60000),
        ]),
        new("Gas Turbine", "Thirsty, slow to spool. Needs a starter battery.", "#6e6a5e", "turbine", true, 0.7, [
            new(Medium, 3, 3, 7, 3, 5000, 20, 1800, 3000, 9000, 12000, 1.7, 4, 50),
            new(Large, 3, 3, 7, 120, 30000, 40, 30000, 1500, 5000, 6500, 16, 10, 2000),
            new(Capital, 1, 1, 3, 600, 120000, 80, 400000, 900, 3600, 4500, 150, 20, 60000),
        ]),
        new("Steam Engine", "Full torque from a standstill, slow to raise steam.", "#5a4436", "steam", false, 0.8, [
            new(Medium, 3, 3, 5, 14, 12000, 30, 20000, 20, 240, 500, 2.4, 15, 0),
            new(Large, 3, 3, 5, 800, 90000, 60, 1200000, 10, 90, 200, 16, 30, 0),
            new(Capital, 1, 1, 3, 5000, 400000, 120, 30000000, 5, 40, 90, 150, 45, 0),
        ]),
        new("Electric Motor", "Full torque from a standstill, on the circuit's power.", "#2f4a58", "electric", false, 0.2, [
            new(Medium, 3, 3, 3, 6, 7000, 25, 12000, 0, 1000, 2400, 0, 0, 0),
            new(Large, 3, 3, 3, 200, 40000, 50, 400000, 0, 300, 900, 0, 0, 0),
            new(Capital, 1, 1, 1, 1500, 200000, 100, 12000000, 0, 120, 240, 0, 0, 0),
        ]),
    ];

    // Gearboxes: size cells, mass t, gears (reductions), most torque out (N m)
    private static readonly Dictionary<Cls, (int N, int T, double[] Gears, double Most)> Gearboxes = new()
    {
        [Medium] = (3, 8, [6.0, 4.0, 2.8, 1.9, 1.3], 2e5), [Large] = (3, 300, [8.0, 5.0, 3.0, 2.0], 2e7), [Capital] = (1, 1500, [12.0, 8.0, 5.0], 4e8),
    };
    private static readonly Dictionary<Cls, double> Rated = new() { [Medium] = 2e5, [Large] = 2e7, [Capital] = 2e9 }; // the torque a drive part of the class carries (N m): past it, it slips
    private static readonly Dictionary<Cls, double> Final = new() { [Medium] = 4.0, [Large] = 5.0, [Capital] = 8.0 }; // a differential's final drive
    // Wheels: radius in half cells via its cells (it is cells tall), width cells, mass t, health
    private static readonly Dictionary<Cls, (int Tall, int Wide, double T, int Hp)> Wheels = new()
    {
        [Medium] = (3, 1, 1.2, 4000), [Large] = (3, 1, 60, 30000), [Capital] = (3, 1, 1500, 200000),
    };
    // Tracks: (wide, tall, long) cells for the unit and the bogie, mass t each, contact rays each
    private static readonly Dictionary<Cls, (((int, int, int), int, int) Unit, ((int, int, int), int, int) Bogie)> Tracks = new()
    {
        [Medium] = (((3, 3, 15), 14, 4), ((3, 3, 7), 6, 2)),
        [Large] = (((3, 3, 15), 700, 4), ((3, 3, 7), 280, 2)),
        [Capital] = (((3, 3, 9), 4800, 6), ((1, 3, 5), 2000, 3)),
    };

    /// <summary>Suspension: travel m, Hz, most load a contact (kg; an int for Small, as written), how fast the contact follows the ground (m/s).</summary>
    public static readonly Dictionary<Cls, (double Travel, double Hz, object Load, double Slew)> Springs = new()
    {
        [Small] = (0.3, 2.2, 1000, 0), [Medium] = (1.0, 1.4, 6e4, 2.0), [Large] = (3.0, 1.1, 3e6, 3.0), [Capital] = (8.0, 0.9, 6e7, 4.0),
    };
    public const double Ride = 0.33; // big struts are gas struts: they ride half way down their travel whatever they carry (up to its most)
    // Hub motors: N m at the wheel, kW, m/s it fades out at
    private static readonly Dictionary<Cls, (object Torque, int Kw, int Top)> Hubs = new()
    {
        [Small] = (1600, 60, 25), [Medium] = (1.2e5, 1500, 18), [Large] = (6e6, 15000, 12), [Capital] = (3e8, 60000, 7),
    };
    // Actuators: piston N, stroke m, knee N m, steering N m, jack stroke m (a leg holds near the whole weight: the solver can load one leg first)
    private static readonly Dictionary<Cls, (double Force, int Stroke, double Knee, double Steer, int Jack)> Legs = new()
    {
        [Medium] = (6e6, 4, 2e7, 2e5, 2), [Large] = (2e8, 16, 1.9e9, 2e7, 6), [Capital] = (1.9e9, 48, 1.9e9, 1.5e9, 16),
    };

    private sealed record Plant(string Key, string Word, string Desc, string Color, double Burn, (Cls C, object Mw, int Spool)[] By, bool Smokes);

    // Plants: MW by class, burn l/s a MW, spool s by class
    private static readonly Plant[] Plants =
    [
        new("Boiler", "Boiler", "Feeds the circuit, slow to raise steam.", "#4a3a30", 0.6, [(Medium, 3, 25), (Large, 40, 60), (Capital, 300, 90)], true),
        new("Furnace", "Furnace", "Quick to power, burns 2.5 boilers' fuel.", "#6a3d24", 1.5, [(Medium, 2, 5), (Large, 30, 8), (Capital, 250, 12)], true),
        new("Genset", "Genset", "Frugal, ready in seconds.", "#3f5560", 0.35, [(Small, 0.1, 1), (Medium, 1, 3), (Large, 20, 5), (Capital, 120, 8)], true),
        new("Reactor", "Reactor", "No fuel, no smoke. Very heavy.", "#3b6a5e", 0, [(Medium, 10, 8), (Large, 200, 15), (Capital, 2000, 30)], false),
    ];
    private static readonly Dictionary<Cls, double> PlantMass = new() { [Small] = 0.4, [Medium] = 24, [Large] = 900, [Capital] = 4800 }; // t, a Boiler; a Reactor weighs half as much again, a Genset half

    /// <summary>cells, mass t, litres.</summary>
    public static readonly Dictionary<Cls, (int N, double T, long Litres)> Tanks = new()
    {
        [Medium] = (3, 2.5, 20000), [Large] = (3, 90, 1000000), [Capital] = (1, 280, 4000000),
    };
    // cells, mass t, kJ (capital: a titan walker standing up once, then about an hour standing or minutes walking on it alone; docs/sandbox/GIANTS.md)
    private static readonly Dictionary<Cls, (int N, int T, double Kj)> Batteries = new() { [Medium] = (3, 8, 1e6), [Large] = (3, 300, 1e7), [Capital] = (1, 1800, 3e7) };
    private static readonly Dictionary<Cls, (double T, int Kw)> Alternators = new() { [Medium] = (0.6, 100), [Large] = (30, 5000), [Capital] = (300, 50000) }; // mass t, kW
    private static readonly Dictionary<Cls, int> Ballast = new() { [Medium] = 8, [Large] = 400, [Capital] = 5000 }; // t a block
    // Thrusters: (name, cls, cells long, force N, seconds of spool)
    private static readonly (string Word, Cls C, int Cells, double Force, double Spool)[] Thrusters =
        [("Light", Small, 3, 8e3, 0.3), ("Small", Small, 7, 5e4, 0.5), ("Medium", Medium, 3, 8e5, 1.0), ("Large", Large, 3, 12.5e6, 1.6), ("Capital", Capital, 1, 2e8, 2.4)];
    private const double JetBurn = 3.0; // l/s a MN at full thrust
    private const double FanKw = 5.0; // kW a kN at full thrust

    // --- Shared helpers ---

    /// <summary>A drive part's health: tough for its size.</summary>
    public static int Health(double massT) => (int)Math.Min(10_000_000, Math.Truncate(600 * Math.Pow(massT, 0.85) + 200));

    public static double Half(int n, Cls c) => n * c.Cell / 2;

    /// <summary>The solid block, its origin in the middle cell.</summary>
    public static void Block(Part p, int nx, int ny, int nz, string? color = null, PyDict? shape = null)
    {
        var c = p.Cls;
        p.Add(Box([-Half(nx, c), -Half(ny, c), -Half(nz, c)], [Half(nx, c), Half(ny, c), Half(nz, c)], color, shape: shape));
    }

    /// <summary>A socket on each face's middle cell, the socket named after its face, and on the far cells of a long face.</summary>
    public static void Faces(Part p, int nx, int ny, int nz, params string[] skip)
    {
        var c = p.Cls;
        double[] h = [Half(nx, c), Half(ny, c), Half(nz, c)];
        for (var axis = 0; axis < 3; axis++)
            foreach (var (sign, s) in new[] { (1, "+"), (-1, "-") })
            {
                var d = s + "xyz"[axis];
                if (skip.Contains(d))
                    continue;
                var at = new double[3];
                at[axis] = sign * h[axis];
                p.Sock(d, at, d);
            }
        // The long sides' end cells, for bolting it down.
        if (nz > 1 && nz >= nx)
            foreach (var (sign, s) in new[] { (1, "+"), (-1, "-") })
            {
                var z = (nz - 1) / 2.0 * c.Cell * sign;
                if (!skip.Contains("-y"))
                    p.Sock($"-y{s}", [0, -h[1], z], "-y");
                if (!skip.Contains("+y"))
                    p.Sock($"+y{s}", [0, h[1], z], "+y");
            }
    }

    /// <summary>A couple of bands round a block, proud of it: what stops a long box looking like a brick.</summary>
    public static void Bands(Part p, int nx, int ny, int nz, string color, int n = 2)
    {
        var c = p.Cls;
        double x = Half(nx, c), y = Half(ny, c), z = Half(nz, c);
        var e = 0.04 * c.X;
        for (var k = 0; k < n; k++)
        {
            var at = z * (2.0 * (k + 1) / (n + 1) - 1);
            var w = 0.05 * z + 0.02 * c.X;
            p.Add(Box([-x - e, -y - e, at - w], [x + e, y + e, at + w], color, detail: true));
        }
    }

    /// <summary>A smoke stack out of a block's top: boxes, and where the smoke leaves.</summary>
    private static void Stack(Part p, int nx, int ny, Cls c, double tall = 4.0, string color = "#2a2523")
    {
        var y = Half(ny, c);
        var w = 0.3 * c.Cell;
        var x = Half(nx, c) * 0.45;
        var h = tall * c.Cell * (c == Capital ? 0.25 : 0.5);
        p.Add(Box([x - w, y - 0.02, -w], [x + w, y + h, w], color, detail: true));
        p.Add(Box([x - w * 1.3, y + h * 0.82, -w * 1.3], [x + w * 1.3, y + h + 0.02 * c.X, w * 1.3], "#181513", detail: true));
        p.Set(("smoke", Kw(("at", Py.L(Py.Round(x, 3), Py.Round(y + h + 0.02 * c.X, 3), 0)), ("size", Py.Round(0.7 * c.Cell, 3)))));
    }

    /// <summary>[round(v, 4) for v in at]: an int stays an int, as Python's round does.</summary>
    private static List<object?> At4(object[] at) => [.. at.Select(v => v is int or long ? v : (object)Py.Round(Py.D(v), 4))];

    public static PyDict PipePort(string name, string d, object[] at, string face, string fluid) =>
        Kw(("name", name), ("type", "pipe"), ("dir", d), ("fluid", fluid), ("at", At4(at)), ("face", face));

    public static PyDict PowerPort(string name, string d, object[] at, string face) =>
        Kw(("name", name), ("type", "power"), ("dir", d), ("at", At4(at)), ("face", face));

    private static List<object?> SteerKind() => Py.L(Kw(("key", "steerKind"), ("label", "Steering type"), ("group", "Steering"), ("options", Py.L("Reverse", "Drag", "Clutch")), ("default", 0),
        ("tooltip", "Reverse: inner side backwards, pivots on the spot. Drag: inner side braked, tight turn, costs speed. Clutch: inner side coasts, wide turn, no pivot.")));

    public static PyDict ShaftPort(string name, string d, string socket) => Kw(("name", name), ("type", "shaft"), ("dir", d), ("socket", socket));

    /// <summary>A suspension's Drive in: a half shaft's port on the hub, inboard. Its socket is kind "port": nothing bolts there.</summary>
    public static void DriveIn(Part p, double[] at)
    {
        p.Sock("drive", at, "-x", kind: "port");
        p.Set(("ports", Py.L(ShaftPort("Drive in", "in", "drive"))));
    }

    // --- Engines ---

    private static List<(string, Part)> EnginesOut()
    {
        var o = new List<(string, Part)>();
        foreach (var en in Engines)
            foreach (var r in en.By)
            {
                var c = r.C;
                var name = $"{(c == Capital ? "Capital" : c.Name)} {en.Word}";
                var mw = r.Torque * r.Peak * 2 * 3.14159 / 60 / 1e6;
                var p = new Part(name, c, "Engines", $"{en.Desc} {Py.G(r.Torque / 1000.0)} kN m at {r.Peak} rpm, about {(long)Py.Round(mw, 0)} MW.", role: "engine", tool: "wrench", color: en.Color);
                Block(p, r.Nx, r.Ny, r.Nz);
                Bands(p, r.Nx, r.Ny, r.Nz, "#2b3442");
                Faces(p, r.Nx, r.Ny, r.Nz);
                p.Stats(r.T * 1000, r.Hp, r.Arm);
                var e = Kw(("torque", r.Torque), ("idleRpm", r.Idle), ("peakRpm", r.Peak), ("maxRpm", r.Top), ("curve", en.Curve), ("noise", en.Noise));
                var ports = Py.L(ShaftPort("Drive out", "out", "+z"));
                if (en.Curve == "electric")
                {
                    e["fuel"] = "electric";
                    ports.Add(PowerPort("Power in", "in", [-Half(r.Nx, c), 0, 0], "-x"));
                }
                else
                {
                    e["burnRate"] = r.Burn;
                    ports.Add(PipePort("Fuel in", "in", [-Half(r.Nx, c), 0, 0], "-x", "fuel"));
                    ports.Add(PipePort("Exhaust out", "out", [Half(r.Nx, c), 0, 0], "+x", "exhaust"));
                    Stack(p, r.Nx, r.Ny, c);
                }
                if (en.Starter)
                {
                    e["starter"] = true;
                    e["starterKJ"] = r.Skj;
                }
                if (r.Spool != 0)
                    e["spool"] = r.Spool;
                p.Set(("engine", e), ("ports", ports));
                o.Add(("Engines", p));
            }
        return o;
    }

    // --- The drive line ---

    private static Part DriveBlock(string name, Cls c, string section, string desc, string role, double mass, int n, string color, List<object?> ports, string blockKey, PyDict value, params string[] skip)
    {
        var p = new Part(name, c, section, desc, role: role, tool: "wrench", color: color);
        Block(p, n, n, n);
        Bands(p, n, n, n, "#2c2f33", 1);
        Faces(p, n, n, n, skip);
        p.Stats(mass * 1000, Health(mass), c.Armour / 2);
        p.Set(("ports", ports), (blockKey, value));
        return p;
    }

    private static List<(string, Part)> DriveLine()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Big)
        {
            var (n, t, gears, most) = Gearboxes[c];
            var cn = c.Name;
            o.Add(("Drive", DriveBlock($"{cn} Gearbox", c, "Gearboxes", $"{gears.Length} speeds, for a {cn.ToLowerInvariant()} engine's output. Rated {Py.G(most / 1000)} kN m.",
                "transmission", t, n, "#556b4a", Py.L(ShaftPort("Input shaft", "in", "-z"), ShaftPort("Output shaft", "out", "+z")),
                "transmission", Kw(("kind", "automatic"), ("gears", gears.Cast<object?>().ToList()), ("shift", 1.0), ("maxTorque", most), ("efficiency", 0.93)))));
            var rated = Rated[c];
            var k = c == Capital ? 1 : 3;
            var m = c == Medium ? 3 : c == Large ? 120 : 900;
            var kinds = new (string Word, string Desc, PyDict Drive, List<object?> Ports, string Color)[]
            {
                ("Reduction Gearbox", "4:1 reduction in line.", Kw(("kind", "gearbox"), ("ratio", 4.0), ("ports", Py.L("-z", "+z")), ("efficiency", 0.95)), Py.L(ShaftPort("Input shaft", "in", "-z"), ShaftPort("Output shaft", "out", "+z")), "#5a4f6b"),
                ("Right-Angle Gearbox", "Turns the drive 90 degrees.", Kw(("kind", "gearbox"), ("ports", Py.L("-z", "-y")), ("efficiency", 0.96)), Py.L(ShaftPort("Input shaft", "in", "-z"), ShaftPort("Output shaft", "out", "-y")), "#6b5f45"),
                ("T Gearbox", "Splits the drive: straight on and a branch.", Kw(("kind", "gearbox"), ("ports", Py.L("-z", "+z", "-y")), ("efficiency", 0.95)), Py.L(ShaftPort("Input shaft", "in", "-z"), ShaftPort("Output shaft", "out", "+z"), ShaftPort("Branch shaft", "out", "-y")), "#5f6b45"),
                ("Clutch", "Wire a signal to let it out.", Kw(("kind", "clutch"), ("ports", Py.L("-z", "+z")), ("efficiency", 0.99)), Py.L(ShaftPort("Input shaft", "in", "-z"), ShaftPort("Output shaft", "out", "+z")), "#6a5a4a"),
                ("CV Joint", "Drive through a steering pivot, up to 45 degrees.", Kw(("kind", "cvjoint"), ("maxAngle", 45), ("ports", Py.L("+x", "-x")), ("efficiency", 0.98)), Py.L(ShaftPort("Input shaft", "in", "-x"), ShaftPort("Output shaft", "out", "+x")), "#6a6f78"),
                ("Wheel Hub", "Drives the suspension bolted to it, 6:1.", Kw(("kind", "hub"), ("ratio", 6.0), ("efficiency", 0.97), ("ports", Py.L("-z", "+x", "-x"))), Py.L(ShaftPort("Drive in", "in", "-z")), "#4d5a3f"),
            };
            foreach (var (word, desc, drive0, ports, color) in kinds)
            {
                var drive = new PyDict(drive0) { ["maxTorque"] = rated };
                o.Add(("Drive", DriveBlock($"{cn} {word}", c, "Drive line", $"{desc} Rated {Py.G(rated / 1000)} kN m.", "transfer", m, k, color, ports, "drive", drive)));
            }
            foreach (var cells in new[] { 3, 7 })
            {
                var p = new Part($"{cn} Drive Shaft {Py.G(cells * c.Cell)} m", c, "Drive line", $"End to end. Rated {Py.G(rated / 1000)} kN m.", role: "transfer", tool: "wrench", color: "#7a7f86");
                var z = Half(cells, c);
                var w = c.Cell * 0.3;
                p.Add(Box([-w, -w, -z], [w, w, z]));
                p.Add(Box([-w * 1.3, -w * 1.3, -z + 0.02], [w * 1.3, w * 1.3, -z + 0.1 * c.Cell], "#4a4f55", detail: true));
                p.Add(Box([-w * 1.3, -w * 1.3, z - 0.1 * c.Cell], [w * 1.3, w * 1.3, z - 0.02], "#4a4f55", detail: true));
                p.Sock("end-", [0, 0, -z], "-z");
                p.Sock("end+", [0, 0, z], "+z");
                p.Sock("bottom", [0, -w, 0], "-y");
                p.Stats(cells * m * 200, Health(cells * m / 5.0), c.Armour / 3);
                p.Set(("drive", Kw(("kind", "shaft"), ("ports", Py.L("end-", "end+")), ("efficiency", 0.99), ("maxTorque", rated))),
                    ("ports", Py.L(ShaftPort("Input shaft", "both", "end-"), ShaftPort("Output shaft", "both", "end+"))));
                o.Add(("Drive", p));
            }
            // Axles: a wide box, a suspension on each end.
            foreach (var (word, kind, color, eff) in new[] { ("Axle", "open", "#3e4636", 0.96), ("Locked Axle", "locked", "#46402f", 0.96), ("Steering Axle", "steering", "#4a3b46", 0.95) })
            {
                var wide = c == Capital ? 3 : 7;
                var cap = char.ToUpperInvariant(kind[0]) + kind[1..];
                var p = new Part($"{cn} {word}", c, "Drive line", $"{cap} axle, {Py.G(Final[c])}:1: suspensions on its ends, drive in the middle.{(kind == "steering" ? " Wire -1 to 1 steers it: 1 is right." : "")} Rated {Py.G(rated / 1000)} kN m.", role: "differential", tool: "wrench", color: color);
                Block(p, wide, 1, 1);
                Faces(p, wide, 1, 1);
                foreach (var (sx, s) in new[] { (1, "+"), (-1, "-") })
                    p.Sock($"top{s}", [sx * (wide - 1) / 2.0 * c.Cell, c.Half, 0], "+y");
                p.Stats(m * 2500, Health(m * 2.5), c.Armour / 2);
                p.Set(("differential", Kw(("kind", kind), ("ratio", Final[c]), ("efficiency", eff), ("maxTorque", rated))),
                    ("ports", Py.L(ShaftPort("Input shaft", "in", "-z"), ShaftPort("Output left", "out", "-x"), ShaftPort("Output right", "out", "+x"))));
                if (kind == "steering")
                    p.Set(("config", SteerKind()));
                o.Add(("Drive", p));
            }
        }
        return o;
    }

    // --- Wheels, tracks, suspensions ---

    /// <summary>The small tank steering differentials: the shapes of the Differential and the Wide Differential, read from their files so they stay the same size.</summary>
    private static List<(string, Part)> SteeringDifferentials(PartOutput po)
    {
        var o = new List<(string, Part)>();
        foreach (var (name, bse, kg, hp, ratio) in new[] { ("Steering Differential", "Differential.json", 66, 180, 4.1), ("Wide Steering Differential", "Drivetrain/DiffWide.json", 110, 360, 4.6) })
        {
            var src = (PyDict)po.ReadJson("Constructs/Parts/" + bse)!;
            var p = new Part(name, Small, "Differential", "Steering axle: steering drives its two sides against each other. Wire -1 to 1 steers it: 1 is right.", role: "differential", tool: "wrench", color: "#4a3b46", group: "Drivetrain");
            foreach (var b in ((List<object?>)src["boxes"]!).Cast<PyDict>())
            {
                var at = b.Has("at") ? ((List<object?>)b["at"]!).Select(Py.D).ToArray() : [0.0, 0.0, 0.0];
                var s = ((List<object?>)b["size"]!).Select(Py.D).ToArray();
                double[] mn = [at[0] - s[0] / 2, at[1] - s[1] / 2, at[2] - s[2] / 2];
                double[] mx = [at[0] + s[0] / 2, at[1] + s[1] / 2, at[2] + s[2] / 2];
                p.Add(Box(mn, mx, (string?)b.Get("color"), detail: b.Get("detail") is true));
            }
            foreach (var s in ((List<object?>)src["sockets"]!).Cast<PyDict>())
                p.Sock((string)s["name"]!, [.. ((List<object?>)s["at"]!).Select(Py.D)], (string)s["dir"]!);
            p.Stats(kg, hp, src["armour"]!, src["attachSeconds"]);
            p.Set(("differential", Kw(("kind", "steering"), ("ratio", ratio), ("efficiency", 0.95))), ("ports", src["ports"]), ("config", SteerKind()));
            o.Add(("Drive", p));
        }
        return o;
    }

    private static void WheelShape(Part p, double r, double w, string tyre, string rim)
    {
        p.Add(Box([-w * 0.99, -r, -r * 0.42], [w * 0.99, r, r * 0.42], tyre));
        p.Add(Box([-w * 0.98, -r * 0.42, -r], [w * 0.98, r * 0.42, r], tyre));
        p.Add(Box([-w * 1.0, -r * 0.85, -r * 0.85], [w * 1.0, r * 0.85, r * 0.85], tyre));
        p.Add(Box([-w * 1.05, -r * 0.3, -r * 0.3], [w * 1.05, r * 0.3, r * 0.3], rim, detail: true));
        p.Add(Box([-w * 1.03, -r * 0.7, -r * 0.08], [w * 1.03, r * 0.7, r * 0.08], rim, detail: true));
        p.Add(Box([-w * 1.03, -r * 0.08, -r * 0.7], [w * 1.03, r * 0.08, r * 0.7], rim, detail: true));
    }

    private static List<(string, Part)> WheelsOut()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Big)
        {
            var (tall, wide, t, hp) = Wheels[c];
            var r = Half(tall, c);
            var w = Half(wide, c);
            foreach (var (word, tyre, rim, grip, off, roll, mat, m, desc) in new[]
            {
                ("Tyre", "#1f2124", "#9aa0a6", 1.0, 1.25, 0.004, "rubber", 1.0, "Huge tyre."),
                ("Drive Wheel", "#3a3633", "#a8763e", 0.85, 1.5, 0.008, "steel", 1.6, "Spiked iron wheel: bites dirt, can't go flat."),
            })
            {
                var p = new Part($"{c.Name} {word}", c, "Wheels", $"{desc} {Py.G(2 * r)} m across.", role: "wheel", tool: "wrench", material: mat, color: tyre);
                WheelShape(p, r, w, tyre, rim);
                p.Sock("axle", [-w, 0, 0], "-x", kind: "axle");
                p.Stats(t * m * 1000, hp * m, c.Armour, c.Seconds);
                var blk = Kw(("radius", r), ("grip", grip), ("offroad", off), ("roll", roll));
                if (word == "Tyre")
                {
                    blk["flat"] = true;
                    p.D["onBreak"] = "disable";
                }
                p.Set(("wheel", blk));
                o.Add(("Wheels", p));
            }
        }
        return o;
    }

    private static Part Track(Cls c, string word, (int, int, int) cells, int t, int rays, string desc)
    {
        var (nx, ny, nz) = cells;
        double x = Half(nx, c), r = Half(ny, c), z = Half(nz, c);
        var p = new Part($"{(c == Capital ? "Capital" : c.Name)} {word}", c, "Tracks", desc, role: "wheel", tool: "wrench", color: "#2a2c2f");
        // Solid: a stepped hull, rounded at its ends by two shorter boxes.
        p.Add(Box([-x, -r, -z + r * 0.9], [x, r, z - r * 0.9]));
        p.Add(Box([-x * 0.96, -r * 0.6, -z], [x * 0.96, r * 0.6, z]));
        // The tread: pads along the top and the bottom.
        var pads = 7;
        var pitch = (2 * z - 2 * r) / pads;
        for (var k = 0; k < pads; k++)
        {
            var at = -z + r + (k + 0.5) * pitch;
            foreach (var (y, flip) in new[] { (r, 1), (-r, -1) })
                p.Add(Box([-x * 1.02, Math.Min(y, y + flip * 0.05 * r), at - pitch * 0.38], [x * 1.02, Math.Max(y, y + flip * 0.05 * r), at + pitch * 0.38], "#4c4f53", detail: true));
        }
        // The exposed drive wheels at the ends and the road rollers under it: "roll" boxes turn as the track runs.
        var side = x * 1.1;
        foreach (var sz in new[] { 1, -1 })
        {
            var at = sz * (z - r * 0.95);
            var s = r * 0.8;
            p.Add(Box([-side, -s, at - s * 0.36], [side, s, at + s * 0.36], "#8a7d4a", detail: true, shape: Kw(("roll", true))));
            p.Add(Box([-side, -s * 0.36, at - s], [side, s * 0.36, at + s], "#8a7d4a", detail: true, shape: Kw(("roll", true))));
        }
        var rollers = nz > 5 ? 4 : 3;
        var span = 2 * (z - 0.6 * r);
        var gap = span / (rollers - 1);
        for (var k = 0; k < rollers; k++)
        {
            var at = -span / 2 + k * gap;
            var s = Math.Min(r * 0.34, gap * 0.42);
            p.Add(Box([-side * 1.02, -r * 0.5 - s, at - s], [side * 1.02, -r * 0.5 + s, at + s], "#6b6f64", detail: true, shape: Kw(("roll", true))));
        }
        p.Sock("axle", [-x, 0, 0], "-x", kind: "axle");
        p.Stats(t * 1000, Health(t), c.Armour + 10, c.Seconds);
        p.Set(("wheel", Kw(("radius", r), ("grip", 1.05), ("offroad", 1.4), ("side", nz > 7 ? 0.5 : 0.9), ("roll", 0.006), ("track", Kw(("length", 2 * z), ("rays", rays))))));
        p.D["friction"] = 1.0;
        return p;
    }

    private static List<(string, Part)> TracksOut()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Big)
        {
            var ((unit, ut, ur), (bogie, bt, br)) = Tracks[c];
            o.Add(("Tracks", Track(c, "Track", unit, ut, ur, "Bolt its middle onto a suspension or a hub motor.")));
            o.Add(("Tracks", Track(c, "Bogie", bogie, bt, br, "Short bogie: several under one body, each on a hub motor.")));
        }
        return o;
    }

    public static Part Mount(Cls c, string name, string section, string desc, string color, double mass, PyDict blk)
    {
        var p = new Part(name, c, section, desc, role: "suspension", tool: "wrench", color: color);
        Block(p, 1, 1, 1);
        var h = c.Half;
        p.Add(Box([h * 0.3, -h * 1.15, -h * 0.25], [h * 0.7, h * 1.15, h * 0.25], "#d0b050", detail: true));
        p.Sock("mount", [-h, 0, 0], "-x");
        p.Sock("top", [0, h, 0], "+y"); // (under a deck: its axle stays level)
        p.Sock("axle", [h, 0, 0], "+x", kind: "axle");
        p.Stats(mass * 1000, Health(mass), c.Armour);
        p.Set(("suspension", blk));
        return p;
    }

    private static List<(string, Part)> Suspensions()
    {
        var o = new List<(string, Part)>();
        foreach (var c in new[] { Small, Medium, Large, Capital })
        {
            var (travel, hz, load, slew) = Springs[c];
            var bse = Kw(("travel", travel), ("frequency", hz), ("damping", 0.8), ("maxLoad", load));
            if (c != Small)
                bse["ride"] = Ride;
            if (slew != 0)
                bse["slew"] = slew;
            var m = c == Small ? 0.02 : c == Medium ? 1.5 : c == Large ? 90 : 900;
            if (c != Small)
            {
                var s = Mount(c, $"{c.Name} Suspension", "Suspension", $"Sprung mount: {Py.G(travel)} m travel, {Py.G(Py.D(load) / 1000)} t a contact.", "#b08a2e", m, bse);
                DriveIn(s, [-c.Half, 0, 0]);
                o.Add(("Suspension", s));
            }
            var (torque, kw, top) = Hubs[c];
            var hub = new PyDict(bse) { ["motor"] = Kw(("torque", torque), ("power", kw), ("top", top)) };
            var p = Mount(c, $"{(c.Name.Length > 0 ? c.Name : "Small")} Hub Motor", "Suspension", $"Sprung mount with a motor: {Py.G(Py.D(torque) / 1000)} kN m, {Py.G(kw / 1000.0)} MW, no shafts.", "#a0602e", m * 1.6, hub);
            p.Set(("ports", Py.L(PowerPort("Power in", "in", [0, c.Half, 0], "+y"))));
            if (c == Small)
                p.D["group"] = "Drivetrain";
            o.Add(("Suspension", p));
            if (c == Small)
                continue;
            // A landing strut: a spring with a foot, nothing on its axle: it stands on the ground under the body.
            p = new Part($"{c.Name} Landing Strut", c, "Legs", $"Sprung leg: the body stands on it. {Py.G(travel * 2)} m travel.", role: "suspension", tool: "wrench", color: "#8f8456");
            var h = c.Half;
            p.Add(Box([-h, -h, -h], [h, h, h]));
            p.Add(Box([-h * 1.4, -h * 1.2, -h * 1.4], [h * 1.4, -h * 0.7, h * 1.4], "#4b463a", detail: true));
            p.Sock("mount", [0, h, 0], "+y");
            p.Sock("axle", [0, -h, 0], "-y", kind: "axle");
            p.Stats(m * 800, Health(m), c.Armour);
            p.Set(("suspension", Kw(("travel", travel * 2), ("frequency", hz), ("damping", 0.9), ("maxLoad", Py.D(load) * 2))));
            o.Add(("Legs", p));
        }
        return o;
    }

    // --- Actuators ---

    private static PyDict Num(string key, string label, string group, string unit, object lo, object hi, object step, string tip) =>
        Kw(("key", key), ("label", label), ("group", group), ("unit", unit), ("min", lo), ("max", hi), ("step", step), ("tooltip", tip));

    private static Part Piston(Cls c, string word, double force, int stroke, double mass, string desc)
    {
        var h = c.Half;
        var y = h * 3;
        var p = new Part($"{c.Name} {word}", c, "Legs", desc, role: "joint", tool: "wrench", color: "#6d747c");
        p.Add(Box([-h, -y, -h], [h, y, h], "#3b4046", shape: Kw(("side", "base"))));
        p.Add(Box([-h * 1.06, y * 0.2, -h * 1.06], [h * 1.06, y * 1.01, h * 1.06], "#f08a12", detail: true, shape: Kw(("side", "leaf"), ("paintable", false))));
        p.Sock("base", [0, -y, 0], "-y");
        p.Sock("top", [0, y, 0], "+y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Stats(mass * 1000, Health(mass), c.Armour);
        var speed = Math.Max(0.3, Math.Min(2.0, stroke / 16.0));
        p.Set(("joint", Kw(("type", "piston"), ("axis", "+y"), ("min", 0), ("max", stroke), ("leaf", "top"), ("speed", Py.Round(speed, 3)), ("strength", force),
                ("give", Py.Round(stroke / 4.0, 3)), ("sideLoad", Py.RoundInt(force / 3)), ("overloadDamage", 0))),
            ("config", Py.L(Num("speed", "Speed", "Motor", "m/s", Py.Round(speed / 10, 3), Py.Round(speed, 3), 0.01, "How fast it moves."),
                Num("strength", "Force", "Motor", "N", Py.RoundInt(force / 10), force, Py.RoundInt(force / 100), "The most it pushes."))),
            ("power", Py.RoundInt(mass * 40)));
        return p;
    }

    private static List<(string, Part)> Actuators()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Big)
        {
            var (force, stroke, knee, steer, jack) = Legs[c];
            var m = c == Medium ? 3 : c == Large ? 150 : 2000;
            o.Add(("Legs", Piston(c, "Leg Piston", force, stroke, m, $"Walker's leg, wired: {Py.G(stroke)} m stroke, {Py.G(force / 1000)} kN.")));
            o.Add(("Legs", Piston(c, "Jack", force * 0.7, jack, m * 0.7, $"Lifts a body off its tracks: {Py.G(jack)} m stroke, {Py.G(force * 0.7 / 1000)} kN.")));
            var h = c.Half;
            var p = new Part($"{c.Name} Knee Hinge", c, "Legs", $"For a leg. Rated {Py.G(knee / 1000)} kN m.", role: "joint", tool: "wrench", color: "#4f6a7a");
            p.Add(Box([-h, -h, -h], [h, h, h], "#3b4046", shape: Kw(("side", "base"))));
            p.Add(Box([h * 0.2, -h * 1.04, -h * 1.04], [h * 1.04, h * 1.04, h * 1.04], "#f08a12", detail: true, shape: Kw(("side", "leaf"), ("paintable", false))));
            p.Sock("base", [-h, 0, 0], "-x");
            p.Sock("leaf", [h, 0, 0], "+x");
            p.Stats(m * 900, Health(m), c.Armour);
            p.Set(("joint", Kw(("type", "motor"), ("axis", "+y"), ("pivot", Py.L(h, 0, 0)), ("min", -100), ("max", 100), ("leaf", "leaf"),
                ("speed", c == Medium ? 30 : c == Large ? 20 : 12), ("strength", knee), ("give", 20), ("overloadDamage", 0))), ("power", m * 50));
            o.Add(("Legs", p));
            // A pivot a bogie swings on: its base bolts to a body's side or underside.
            p = new Part($"{c.Name} Steering Pivot", c, "Tracks", "Steers the suspension or hub motor on its leaf.", role: "joint", tool: "wrench", color: "#8a6d3b");
            p.Add(Box([-h, -h, -h], [h, h, h], "#3b4046", shape: Kw(("side", "base"))));
            p.Add(Box([h * 0.3, -h * 1.05, -h * 1.05], [h * 1.05, h * 1.05, h * 1.05], "#f08a12", detail: true, shape: Kw(("side", "leaf"), ("paintable", false))));
            p.Sock("base", [-h, 0, 0], "-x");
            p.Sock("top", [0, h, 0], "+y");
            p.Sock("leaf", [h, 0, 0], "+x");
            p.Stats(m * 500, Health(m), c.Armour);
            p.Set(("joint", Kw(("type", "steering"), ("axis", "+y"), ("pivot", Py.L(0, 0, 0)), ("min", -35), ("max", 35), ("leaf", "leaf"),
                ("speed", c == Medium ? 60 : c == Large ? 40 : 25), ("strength", steer))));
            o.Add(("Tracks", p));
        }
        return o;
    }

    // --- Power ---

    private static List<(string, Part)> PlantsOut()
    {
        var o = new List<(string, Part)>();
        foreach (var pl in Plants)
            foreach (var (c, mwObj, spool) in pl.By)
            {
                var mw = Py.D(mwObj);
                var n = c == Capital ? 1 : 3;
                var k = pl.Key switch { "Boiler" => 1, "Furnace" => 0.8, "Genset" => 0.5, _ => 1.04 };
                var t = PlantMass[c] * k;
                var p = new Part($"{(c.Name.Length > 0 ? c.Name : "Small")} {pl.Word}", c, "Power plants", $"{pl.Desc} {Py.G(mw)} MW.", role: "generator", tool: "wrench", color: pl.Color, group: c != Small ? null : "Drivetrain");
                Block(p, n, n, n);
                Bands(p, n, n, n, "#2c2926", 2);
                Faces(p, n, n, n);
                p.Stats(t * 1000, Health(t), c.Armour);
                var g = Kw(("rate", mwObj is int i ? i * 1000 : (object)(mw * 1000)), ("spool", spool));
                var ports = Py.L(PowerPort("Power out", "out", [0, 0, Half(n, c)], "+z"));
                if (pl.Burn != 0)
                {
                    g["burnRate"] = Py.Round(mw * pl.Burn, 3);
                    ports.Add(PipePort("Fuel in", "in", [-Half(n, c), 0, 0], "-x", "fuel"));
                    ports.Add(PipePort("Exhaust out", "out", [Half(n, c), 0, 0], "+x", "exhaust"));
                }
                else
                    g["fuel"] = "none";
                if (pl.Smokes)
                    Stack(p, n, n, c);
                p.Set(("generator", g), ("ports", ports));
                o.Add(("Plants", p));
            }
        foreach (var c in Big)
        {
            var h = c.Half;
            var tall = 5;
            foreach (var (word, cells, w, m) in new[] { ("Chimney", tall, 0.6, 1.0), ("Stack Section", 3, 0.6, 0.6), ("Wide Funnel", 3, 1.0, 1.4) })
            {
                var y = Half(cells, c);
                var t = (c == Medium ? 1.5 : c == Large ? 40 : 200) * m;
                var p = new Part($"{c.Name} {word}", c, "Power plants", "Goes on an exhaust, or takes a pipe from one. Sections stack.", role: "frame", tool: "wrench", color: "#3a3431");
                p.Add(Box([-h * w, -y, -h * w], [h * w, y, h * w]));
                p.Add(Box([-h * w * 1.2, y * 0.84, -h * w * 1.2], [h * w * 1.2, y * 1.02, h * w * 1.2], "#1b1816", detail: true));
                p.Add(Box([-h * w * 1.15, -y * 0.2, -h * w * 1.15], [h * w * 1.15, y * 0.02, h * w * 1.15], "#5a3a26", detail: true));
                p.Sock("bottom", [0, -y, 0], "-y");
                p.Sock("top", [0, y, 0], "+y");
                p.Stats(t * 1000, Health(t), c.Armour / 2);
                p.Set(("ports", Py.L(PipePort("Exhaust in", "in", [0, -y * 0.5, h * w], "+z", "exhaust"))),
                    ("smoke", Kw(("at", Py.L(0, Py.Round(y * 1.02, 3), 0)), ("size", Py.Round(0.8 * w * c.Cell * (word == "Wide Funnel" ? 1.6 : 1), 3)))));
                o.Add(("Plants", p));
            }
        }
        return o;
    }

    private static List<(string, Part)> Stores()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Big)
        {
            var (n, t, litres) = Tanks[c];
            var p = new Part($"{c.Name} Fuel Bunker", c, "Fuel and charge", $"Holds {Py.G(litres / 1000.0)} kl of fuel.", role: "fueltank", tool: "wrench", color: "#b8352c");
            Block(p, n, n, n);
            Bands(p, n, n, n, "#7c241d", 2);
            Faces(p, n, n, n);
            p.Stats(t * 1000, Health(t), c.Armour / 2);
            p.Set(("fuelTank", Kw(("capacity", litres))), ("ports", Py.L(PipePort("Fuel out", "out", [Half(n, c), 0, 0], "+x", "fuel"))));
            o.Add(("Power", p));
            var (bn, bt, kj) = Batteries[c];
            p = new Part($"{c.Name} Battery Bank", c, "Fuel and charge", $"Holds {Py.G(kj / 1e6)} GJ for the circuit it is on.", role: "battery", tool: "wrench", color: "#23272c");
            Block(p, bn, bn, bn);
            Bands(p, bn, bn, bn, "#d6b13a", 1);
            Faces(p, bn, bn, bn);
            p.Stats(bt * 1000, Health(bt), c.Armour / 2);
            p.Set(("battery", Kw(("capacity", kj))), ("ports", Py.L(PowerPort("Power out", "out", [Half(bn, c), 0, 0], "+x"))));
            o.Add(("Power", p));
            var (at, kw) = Alternators[c];
            p = new Part($"{c.Name} Alternator", c, "Fuel and charge", $"Touching the engine: {Py.G(kw / 1000.0)} MW into the circuit.", role: "alternator", tool: "wrench", color: "#8d9196");
            Block(p, 1, 1, 1);
            Faces(p, 1, 1, 1);
            p.Stats(at * 1000, Health(at), c.Armour / 3);
            p.Set(("alternator", Kw(("rate", kw))), ("ports", Py.L(PowerPort("Power out", "out", [c.Half, 0, 0], "+x"))));
            o.Add(("Power", p));
            var cells = 7;
            var z = Half(cells, c);
            var w = c.Cell * 0.2;
            p = new Part($"{c.Name} Fuel Main {Py.G(cells * c.Cell)} m", c, "Fuel and charge", "Carries fuel where a link won't reach.", role: "fuelline", tool: "wrench", color: "#e0b030");
            p.Add(Box([-z, -w, -w], [z, w, w]));
            p.Sock("+x", [z, 0, 0], "+x");
            p.Sock("-x", [-z, 0, 0], "-x");
            p.Sock("bottom", [0, -w, 0], "-y");
            p.Stats(at * 300, Health(at / 3), 2);
            o.Add(("Power", p));
            var bl = Ballast[c];
            p = new Part($"{c.Name} Ballast Block", c, "Ballast", $"{Py.G(bl)} t: keeps a tall body on its tracks.", role: "frame", color: "#50514f");
            Block(p, 1, 1, 1);
            Faces(p, 1, 1, 1);
            p.Stats(bl * 1000, Health(bl), c.Armour);
            o.Add(("Ballast", p));
        }
        return o;
    }

    private static List<(string, Part)> ThrustersOut()
    {
        var o = new List<(string, Part)>();
        foreach (var (word, c, cells, force, spool) in Thrusters)
        {
            var cell = c.Cell;
            var wide = c == Capital ? 1 : 3;
            if (c == Small)
                wide = word == "Light" ? 1 : 3;
            var h = wide * cell / 2;
            var z = cells * cell / 2;
            foreach (var kind in new[] { "Jet", "Lift Fan" })
            {
                var jet = kind == "Jet";
                var p = new Part($"{word} {kind}", c, "Thrusters", (jet ? $"Jet: {Py.G(force / 1000)} kN from fuel." : $"Lift fan: {Py.G(force / 1000)} kN from the circuit.") + " Pushes away from its nozzle.",
                    role: "thruster", tool: "wrench", color: jet ? "#5c6066" : "#4a6a78", group: "Thrusters");
                p.Add(Box([-h, -h, -z], [h, h, z]));
                p.Add(Box([-h * 1.08, -h * 1.08, z * 0.5], [h * 1.08, h * 1.08, z * 1.03], "#2a2c2f", detail: true));
                p.Add(Box([-h * 0.7, -h * 0.7, z * 1.01], [h * 0.7, h * 0.7, z * 1.06], "#c8642a", detail: true, shape: Kw(("glow", true))));
                p.Sock("front", [0, 0, -z], "-z");
                p.Sock("+x", [h, 0, 0], "+x");
                p.Sock("-x", [-h, 0, 0], "-x");
                p.Sock("+y", [0, h, 0], "+y");
                p.Sock("-y", [0, -h, 0], "-y");
                var mass = force / 18 / (jet ? 9 : 6); // (lifts nine times, or six times, its own weight)
                p.Stats(mass, Health(mass / 1000), Math.Max(2, c.Armour / 3), c.Seconds);
                var b = Kw(("force", force), ("spool", spool), ("exhaust", "+z"), ("at", Py.L(0, 0, z)));
                var ports = new List<object?>();
                if (jet)
                {
                    b["burnRate"] = Py.Round(force / 1e6 * JetBurn, 4);
                    ports.Add(PipePort("Fuel in", "in", [-h, 0, 0], "-x", "fuel"));
                }
                else
                {
                    b["fuel"] = "electric";
                    b["power"] = Py.Round(force / 1000 * FanKw, 2);
                    ports.Add(PowerPort("Power in", "in", [-h, 0, 0], "-x"));
                }
                p.Set(("thruster", b), ("ports", ports));
                o.Add(("Thrusters", p));
            }
        }
        return o;
    }

    public void Make(PartOutput o) => WriteParts(o, Out,
        [.. EnginesOut(), .. DriveLine(), .. SteeringDifferentials(o), .. WheelsOut(), .. TracksOut(), .. Suspensions(), .. Actuators(), .. PlantsOut(), .. Stores(), .. ThrustersOut()]);
}
