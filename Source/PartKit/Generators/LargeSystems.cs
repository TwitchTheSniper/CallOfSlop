using static CallOfSlop.PartKit.PartKitMath;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's big systems (Constructs/Parts/Systems): what makes a mothership work (docs/sandbox/CONSTRUCTS.md, "Large systems"), and
/// four blueprints of them (Deck Lift, Naval Turret, Harbour Crane, Gate Ramp). Was tools/constructs/large_systems.py.
///
/// The hulls, adapters and limits are the size classes'; engines and propulsion are their own family. Here: ramps, gates, lifts,
/// catapults, arrestors, docks and magnetic locks; rated pistons, hinges and turntables; winches, booms and conveyors; big guns, magazines
/// and hoists; the bridge, fire director, gauges and sensors; lights; walkways, ladders, scaffold, doors. A traction city launches down
/// ramps and lifts first, catapults second, so every class has a ramp deck, a lift platform and an arched gate. The numbers are data, in
/// the tables below.
/// </summary>
public sealed class LargeSystems : IPartGenerator
{
    public string Name => "large_systems";

    private const string Out = "Constructs/Parts/Systems";
    private const string GroupSmall = "Ship systems"; // small-class parts for big constructs
    private static readonly Cls[] Big = [Medium, Large, Capital];

    private const string Steel = "#6d747c", Dark = "#3d4248", Gold = "#c7a23a", Hazard = "#b8902e", Rust = "#8a5a3a", Wood = "#6f5436", Lantern = "#d9642a", Bright = "#fff0c0";

    private static double PlateMass(Cls c, double area) => area * c.KgM2;
    private static double PlateHealth(Cls c, double area) => area * c.HealthM2;

    private static string F(double v) => Fmt(v);

    private static PyDict Num(string key, string label, string group, string unit, object lo, object hi, object step, string tip, object? def = null)
    {
        var e = Kw(("key", key), ("label", label), ("group", group), ("unit", unit), ("min", lo), ("max", hi), ("step", step), ("tooltip", tip));
        if (def is not null)
            e["default"] = def;
        return e;
    }

    private static PyDict S(params (string, object?)[] kv) => Kw(kv);

    private static string? GroupFor(Cls c) => c == Small ? GroupSmall : null;
    private static string ToolFor(Cls c) => c == Small ? "wrench" : "welder";

    /// <summary>Sockets on a deck's four edges, at the middle of the cell nearest each edge's middle.</summary>
    private static void Edges(Part p, double x0, double x1, double z0, double z1, double y = 0.0)
    {
        var c = p.Cls;
        var mx = Py.RoundInt((x0 + x1) / 2 / c.Cell) * c.Cell;
        var mz = Py.RoundInt((z0 + z1) / 2 / c.Cell) * c.Cell;
        p.Sock("+x", [x1, y, mz], "+x");
        p.Sock("-x", [x0, y, mz], "-x");
        p.Sock("+z", [mx, y, z1], "+z");
        p.Sock("-z", [mx, y, z0], "-z");
    }

    /// <summary>
    /// A flat deck nx by nz cells, its top plate on the middle of its cells' height (so a drawbridge's tip is thin), its origin in the middle
    /// cell along z and in cell 0 along x; with a pedestal under the middle for what carries it.
    /// </summary>
    private static Part Deck(string name, Cls c, string section, string desc, int nx, int nz, double thick, bool pedestal = true)
    {
        var p = new Part(name, c, section, desc, role: "panel", color: Steel, tool: "welder");
        double x0 = c.Lo(), x1 = c.Hi(nx);
        var hz = nz * c.Cell / 2;
        var t = thick / 2;
        p.Add(Box([x0, -t, -hz], [x1, t, hz]));
        if (pedestal)
        {
            var mx = Py.RoundInt((x0 + x1) / 2 / c.Cell) * c.Cell;
            p.Add(Box([mx - c.Half * 0.7, c.Lo(), -c.Half * 0.7], [mx + c.Half * 0.7, -t, c.Half * 0.7], Dark));
            p.Sock("-y", [mx, c.Lo(), 0], "-y");
        }
        // Hazard stripes along the long edges (a little proud of the deck).
        p.Add(Box([x0 + 0.02, t, hz - 0.06 * c.X], [x1 - 0.02, t + 0.004 * c.X, hz - 0.02 * c.X], Hazard, detail: true));
        p.Add(Box([x0 + 0.02, t, -hz + 0.02 * c.X], [x1 - 0.02, t + 0.004 * c.X, -hz + 0.06 * c.X], Hazard, detail: true));
        Edges(p, x0, x1, -hz, hz);
        // Four sockets on the top, at its corner cells: what stands on a deck (a gate's pillars, a crane's foot).
        var zc = (nz - 1) / 2 * c.Cell;
        foreach (var (nm, x, z) in new[] { ("+y.0-", 0.0, -zc), ("+y.0+", 0.0, zc), ("+y.1-", (nx - 1) * c.Cell, -zc), ("+y.1+", (nx - 1) * c.Cell, zc) })
            p.Sock(nm, [x, t, z], "+y");
        var area = (x1 - x0) * 2 * hz;
        p.Stats(PlateMass(c, area) * 1.4, PlateHealth(c, area), c.Armour);
        p.Set(("walk", "floor"), ("centreSockets", false));
        return p;
    }

    // ---- Launching and landing

    private static (int, int) Ramps(Cls c) => c == Capital ? (3, 3) : c == Large ? (5, 5) : (8, 5); // cells long, cells wide
    private static (int, int) Lifts(Cls c) => c == Capital ? (3, 3) : (5, 5);
    private static (int, int) Doors(Cls c) => c == Capital ? (3, 2) : (5, 3); // cells wide, cells tall
    private static (int, int) Gates(Cls c) => c == Capital ? (3, 3) : (5, 5);

    private static Part Ramp(Cls c)
    {
        var (n, w) = Ramps(c);
        return Deck($"Ramp Deck {c.Name} {F(n * c.Cell)}x{F(w * c.Cell)}", c, "Ramps and gates", "Drawbridge deck: hangs on a motor hinge.", n, w, 0.2 * c.Cell, pedestal: false);
    }

    private static Part Lift(Cls c)
    {
        var (n, w) = Lifts(c);
        return Deck($"Lift Platform {c.Name} {F(n * c.Cell)}x{F(w * c.Cell)}", c, "Lifts", "Bolts on a piston's top. What stands on it rides it.", n, w, 0.2 * c.Cell);
    }

    private static Part DoorLeaf(Cls c)
    {
        var (wide, tall) = Doors(c);
        var p = new Part($"Hangar Door {c.Name} {F(wide * c.Cell)}x{F(tall * c.Cell)}", c, "Ramps and gates", "Slides on a piston, or hangs on a motor hinge.", role: "panel", color: Rust);
        double x0 = c.Lo(), x1 = c.Hi(wide), y0 = c.Lo(), y1 = c.Hi(tall);
        var t = 0.1 * c.Cell;
        p.Add(Box([x0, y0, -t], [x1, y1, t]));
        for (var k = 1; k < 4; k++)
        {
            var at = x0 + (x1 - x0) * k / 4;
            p.Add(Box([at - 0.02 * c.X, y0 + 0.01, -t - 0.004 * c.X], [at + 0.02 * c.X, y1 - 0.01, t + 0.004 * c.X], Dark, detail: true));
        }
        p.Sock("-x", [x0, 0, 0], "-x");
        p.Sock("+x", [x1, 0, 0], "+x");
        p.Sock("-y", [0, y0, 0], "-y");
        p.Sock("+y", [0, y1, 0], "+y");
        var area = (x1 - x0) * (y1 - y0);
        p.Stats(PlateMass(c, area), PlateHealth(c, area), c.Armour + 8);
        p.Set(("centreSockets", false));
        return p;
    }

    private static Part Gate(Cls c)
    {
        var (wide, tall) = Gates(c);
        var p = new Part($"Arched Gate {c.Name} {F(wide * c.Cell)}x{F(tall * c.Cell)}", c, "Ramps and gates", "Vehicles drive through. Hangar doors or gate leaves close it.", role: "frame", color: Rust);
        double x0 = c.Lo(), x1 = c.Hi(wide), y0 = c.Lo(), y1 = c.Hi(tall);
        var z = c.Half;
        p.Add(Box([x0, y0, -z], [x0 + c.Cell, y1, z]));
        p.Add(Box([x1 - c.Cell, y0, -z], [x1, y1, z]));
        p.Add(Box([x0 + c.Cell, y1 - c.Cell, -z], [x1 - c.Cell, y1, z]));
        // The arch's stepped shoulders, drawn only.
        double inner0 = x0 + c.Cell, inner1 = x1 - c.Cell, top = y1 - c.Cell;
        double[] insets = [0.18, 0.36, 0.54];
        for (var k = 0; k < 3; k++)
        {
            var span = (inner1 - inner0) * insets[k] / 2;
            var drop = c.Cell * 0.28 * (3 - k);
            p.Add(Box([inner0, top - drop, -z * 0.9], [inner0 + span, top, z * 0.9], "#a0683f", detail: true));
            p.Add(Box([inner1 - span, top - drop, -z * 0.9], [inner1, top, z * 0.9], "#a0683f", detail: true));
        }
        p.Sock("-x", [x0, 0, 0], "-x");
        p.Sock("+x", [x1, 0, 0], "+x");
        p.Sock("jamb-", [x0 + c.Cell, 0, 0], "+x");
        p.Sock("jamb+", [x1 - c.Cell, 0, 0], "-x");
        p.Sock("-y-", [0, y0, 0], "-y");
        p.Sock("-y+", [(wide - 1) * c.Cell, y0, 0], "-y");
        p.Sock("+y", [(wide - 1) / 2 * c.Cell, y1, 0], "+y");
        p.Sock("+z", [(wide - 1) / 2 * c.Cell, (tall - 1) * c.Cell, z], "+z");
        p.Sock("-z", [(wide - 1) / 2 * c.Cell, (tall - 1) * c.Cell, -z], "-z");
        var area = (wide * tall - (wide - 2) * (tall - 1)) * c.Cell * c.Cell;
        p.Stats(PlateMass(c, area) * 4, PlateHealth(c, area) * 3, c.Armour + 10);
        p.Set(("centreSockets", false));
        return p;
    }

    // cells long, m/s off the rail over the deck, kg, zone width, zone height, W
    private static (int N, int V, int M, int W, int H, int Pw) Catapults(Cls c) => c.Key switch
    {
        "small" => (32, 55, 6000, 10, 5, 100), "medium" => (16, 60, 20000, 12, 6, 500), "large" => (8, 70, 200000, 24, 16, 5000), _ => (4, 80, 2000000, 48, 32, 50000),
    };

    // cells long, decel m/s2, kg, zone width, zone height, W, m it reaches past each end
    private static (int N, int A, int M, int W, int H, int Pw, int Reach) Arrestors(Cls c) => c.Key switch
    {
        "small" => (32, 60, 6000, 12, 6, 50, 3), "medium" => (16, 45, 20000, 16, 8, 200, 6), "large" => (8, 35, 200000, 32, 16, 2000, 12), _ => (4, 25, 2000000, 64, 32, 20000, 24),
    };

    private static Part Rail(Cls c, string name, string desc, string kind, int n, int accel, int mass, int width, int height, int power, int speed = 0, int reach = 0)
    {
        var p = new Part(name, c, "Launch and landing", desc, role: "logic", color: Dark, group: GroupFor(c), tool: ToolFor(c));
        var x = c.Half;
        double z0 = -c.Hi(n - 1) - c.Cell, z1 = c.Half; // forward (-z) from its cell 0
        var t = 0.1 * c.Cell;
        p.Add(Box([-x, -t, z0], [x, t, z1]));
        p.Add(Box([-c.Half * 0.7, c.Lo(), -c.Half * 0.7], [c.Half * 0.7, -t, c.Half * 0.7], Steel));
        p.Add(Box([-0.03 * c.X, t, z0 + 0.02], [0.03 * c.X, t + 0.012 * c.X, z1 - 0.02], Gold, detail: true, shape: S(("use", true))));
        p.Sock("-y", [0, c.Lo(), 0], "-y");
        p.Sock("+z", [0, 0, z1], "+z");
        p.Sock("-z", [0, 0, z0], "-z");
        p.Sock("+x", [x, 0, 0], "+x");
        p.Sock("-x", [-x, 0, 0], "-x");
        var area = (z1 - z0) * 2 * x;
        p.Stats(PlateMass(c, area) * 2, PlateHealth(c, area), c.Armour);
        var launch = S(("width", width), ("height", height), ("accel", accel), ("maxMass", mass));
        if (speed != 0)
            launch["speed"] = speed;
        if (reach != 0)
            launch["reach"] = reach;
        p.Set(("logic", S(("kind", kind))), ("launch", launch), ("walk", "floor"), ("power", power), ("centreSockets", false));
        return p;
    }

    private static Part Catapult(Cls c)
    {
        var (n, v, m, w, h, pw) = Catapults(c);
        var a = (int)Py.RoundInt(v * v / (1.6 * n * c.Cell)); // (what gets it to speed in most of the rail: a rail hooks a craft at its back end)
        var name = c == Small ? $"Launch Rail {F(n * c.Cell)} m" : $"Catapult {c.Name} {F(n * c.Cell)} m";
        return Rail(c, name, $"Roll on, press the pilot's key to hook, again to fling up to {m / 1000} t off at {v} m/s over the deck. Or a signal. Needs batteries.", "launch", n, a, m, w, h, pw, speed: v);
    }

    private static Part Arrestor(Cls c)
    {
        var (n, a, m, w, h, pw, reach) = Arrestors(c);
        var name = c == Small ? $"Arrestor Strip {F(n * c.Cell)} m" : $"Arrestor Gear {c.Name} {F(n * c.Cell)} m";
        return Rail(c, name, $"Catches up to {m / 1000} t landing on it, or {reach} m either end of it, at {a} m/s2. No hook needed.", "arrest", n, a, m, w, h, pw, reach: reach);
    }

    // box (w, h, l) as Python numbers, longest side, kg, settle m/s, kW, l/s, tractor reach past the box m
    private static (object[] Size, int Side, int Mass, double Settle, int Kw, int Fuel, int Pull) Docks(Cls c) => c.Key switch
    {
        "small" => ([3, 2.5, 3], 8, 6000, 1.5, 20, 1, 16),
        "medium" => ([8, 4, 8], 32, 30000, 2.0, 50, 2, 24),
        "large" => ([24, 12, 24], 96, 300000, 3.0, 500, 10, 48),
        _ => ([64, 32, 64], 128, 3000000, 4.0, 5000, 50, 96),
    };

    // box (w, h, l), kg, kW, l/s, m/s thrown out along its front
    private static (int[] Size, int Mass, int Kw, int Fuel, int Eject) Bays(Cls c) => c.Key switch
    {
        "small" => ([10, 4, 10], 6000, 20, 1, 10), "medium" => ([16, 6, 20], 30000, 50, 2, 12), _ => ([40, 16, 48], 300000, 500, 10, 15),
    };

    private static List<object?> BoxConfig(int side, object[] size, double y) => Py.L(
        S(("key", "startOn"), ("label", "Starts holding"), ("group", "Dock"), ("default", false), ("tooltip", "A newly built dock starts holding what comes into its box.")),
        Num("sizeX", "Width", "Box", "m", 0.5, side, 0.5, "The box's size across the part.", size[0]),
        Num("sizeY", "Height", "Box", "m", 0.5, side, 0.5, "The box's size up from the part.", size[1]),
        Num("sizeZ", "Length", "Box", "m", 0.5, side, 0.5, "The box's size along the part.", size[2]),
        Num("atX", "Centre x", "Box", "m", -side, side, 0.25, "Where the box's centre is from the part (x).", 0),
        Num("atY", "Centre y", "Box", "m", -side, side, 0.25, "Where the box's centre is from the part (y).", y),
        Num("atZ", "Centre z", "Box", "m", -side, side, 0.25, "Where the box's centre is from the part (z).", 0));

    private static void FiveSockets(Part p, double h, double yDown)
    {
        p.Sock("-y", [0, -yDown, 0], "-y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, h], "+z");
        p.Sock("-z", [0, 0, -h], "-z");
    }

    private static Part Dock(Cls c)
    {
        var (size, side, mass, settle, kw, fuel, pull) = Docks(c);
        var name = c.Name.Length > 0 ? $"Docking Clamp {c.Name}" : "Docking Clamp";
        var p = new Part(name, c, "Launch and landing", $"Armed, it draws in a craft up to {mass / 1000} t from {pull} m off and holds it, charges and refuels it, unloads its bins. The pilot's key lets go.",
            role: "logic", color: Dark, group: GroupFor(c), tool: ToolFor(c));
        var h = c.Half;
        p.Add(Box([-h, -h, -h], [h, h * 0.2, h]));
        p.Add(Box([-h * 0.8, h * 0.2, -h * 0.8], [-h * 0.3, h, h * 0.8], Hazard, detail: true, shape: S(("use", true))));
        p.Add(Box([h * 0.3, h * 0.2, -h * 0.8], [h * 0.8, h, h * 0.8], Hazard, detail: true, shape: S(("use", true))));
        FiveSockets(p, h, h);
        p.Stats(c.Cell * c.Cell * c.KgM2 * 6, c.Cell * c.Cell * c.HealthM2 * 3, c.Armour);
        p.Set(("logic", S(("kind", "cargo"))), ("dock", S(("side", side), ("settle", settle), ("maxMass", mass), ("charge", kw), ("refuel", fuel), ("chute", true), ("pull", pull))), ("power", kw * 20),
            ("config", BoxConfig(side, size, Py.D(size[1]) / 2 + h * 0.2)));
        return p;
    }

    /// <summary>A dock without the tractor, with room for a craft and a throw: a cargo volume that takes its crew along. Doors are the builder's.</summary>
    private static Part Bay(Cls c)
    {
        var (size, mass, kw, fuel, eject) = Bays(c);
        var side = size.Max() * 2;
        var name = c.Name.Length > 0 ? $"Hangar Bay {c.Name}" : "Hangar Bay";
        var p = new Part(name, c, "Launch and landing", $"Holds craft up to {mass / 1000} t that park in its box, charges and refuels them. Signal off or the pilot's key: out along its front at {eject} m/s.",
            role: "logic", color: Dark, group: GroupFor(c), tool: ToolFor(c));
        var n = c == Small ? 2 : 1;
        var h = n * c.Cell / 2;
        var y = c.Half;
        p.Add(Box([-h, -y, -h], [h, y * 0.4, h]));
        p.Add(Box([-h * 0.8, y * 0.4, -h * 0.9], [h * 0.8, y * 0.7, -h * 0.5], Hazard, detail: true, shape: S(("use", true))));
        FiveSockets(p, h, y);
        p.Stats(c.Cell * c.Cell * c.KgM2 * 4, c.Cell * c.Cell * c.HealthM2 * 3, c.Armour);
        var config = BoxConfig(side, [.. size.Cast<object>()], size[1] / 2.0 + y * 0.4);
        config[0] = new PyDict((PyDict)config[0]!) { ["default"] = true, ["label"] = "Starts holding" };
        p.Set(("logic", S(("kind", "cargo"))), ("dock", S(("side", side), ("settle", 3.0), ("maxMass", mass), ("charge", kw), ("refuel", fuel), ("eject", eject))), ("power", kw * 20), ("config", config));
        return p;
    }

    private static Part Mast(Cls c)
    {
        var (cells, reach, rope, mass) = c == Small ? (20, 14, 2, 90) : (8, 24, 3, 1200); // cells tall, reach m, rope size, kg
        var name = c.Name.Length > 0 ? $"Mooring Mast {c.Name}" : "Mooring Mast";
        var p = new Part(name, c, "Launch and landing", $"Ropes any balloon that drifts within {reach} m of its top. F or a signal off casts it off.", role: "logic", color: "#5a4a36", group: GroupFor(c), tool: ToolFor(c));
        var h = c.Half * 0.6;
        var top = c.Lo() + cells * c.Cell;
        p.Add(Box([-h, c.Lo(), -h], [h, top, h]));
        p.Add(Box([-h * 1.6, top - c.Cell * 0.6, -h * 1.6], [h * 1.6, top - c.Cell * 0.2, h * 1.6], Hazard, detail: true, shape: S(("use", true))));
        p.Add(Box([-c.Half, c.Lo(), -c.Half], [c.Half, c.Lo() + c.Cell * 0.2, c.Half], Dark, detail: true));
        p.Sock("-y", [0, c.Lo(), 0], "-y");
        p.Stats(mass, c.Cell * c.Cell * c.HealthM2 * 4, c.Armour);
        p.Set(("logic", S(("kind", "mooring"))), ("mooring", S(("reach", reach), ("rope", rope))));
        return p;
    }

    private static Part MagLock(Cls c)
    {
        var (size, side, mass, settle, power) = c == Small ? (new object[] { 3, 2.5, 3 }, 8, 6000, 1.5, 300) : (new object[] { 8, 5, 8 }, 16, 60000, 2.0, 3000);
        var name = c == Small ? "Magnetic Lock" : "Magnetic Lock Medium";
        var p = new Part(name, c, "Launch and landing", $"Holds a body up to {mass / 1000} t while powered.", role: "logic", color: "#4a5560", group: GroupFor(c), tool: ToolFor(c));
        var n = c == Small ? 3 : 1;
        var x = n * c.Cell / 2;
        var y = c.Half;
        p.Add(Box([-x, -y, -x], [x, y * 0.4, x]));
        p.Add(Box([-x * 0.8, y * 0.4, -x * 0.8], [x * 0.8, y * 0.7, x * 0.8], "#c23a2a", detail: true, shape: S(("use", true))));
        FiveSockets(p, x, y);
        p.Stats(c == Small ? 12 : 300, c == Small ? 90 : 800, c.Armour);
        p.Set(("logic", S(("kind", "cargo"))), ("dock", S(("side", side), ("settle", settle), ("maxMass", mass))), ("power", power), ("config", BoxConfig(side, size, Py.D(size[1]) / 2 + y * 0.4)));
        return p;
    }

    // ---- Moving things: rated actuators

    // strokes (m), N, side N, top m/s, length in cells
    private static (int[] Strokes, long Force, long Side, double Top, int N) Pistons(Cls c) => c.Key switch
    {
        "medium" => ([4, 8], 300_000, 100_000, 1.0, 2), "large" => ([16, 32], 5_000_000, 2_000_000, 2.0, 2), _ => ([32, 64], 200_000_000, 80_000_000, 4.0, 1),
    };

    private static (long Torque, int Top) Hinges(Cls c) => c.Key switch { "medium" => (500_000, 30), "large" => (50_000_000, 15), _ => (1_000_000_000, 8) };
    private static (long Torque, int Top) Turntables(Cls c) => c.Key switch { "medium" => (500_000, 45), "large" => (50_000_000, 25), _ => (1_500_000_000, 12) };

    private static PyDict Side(string side, bool leaf = false) => leaf ? S(("side", side), ("paintable", false)) : S(("side", side));

    private static Part Piston(Cls c, int stroke)
    {
        var (_, force, side, top, n) = Pistons(c);
        var p = new Part($"Piston {c.Name} {F(stroke)} m", c, "Pistons", $"Signal 1: out, 0: in. Rated {force / 1000} kN, {side / 1000} kN across.", role: "joint", color: Steel);
        var h = c.Half;
        var y1 = c.Hi(n);
        p.Add(Box([-h, -h, -h], [h, y1, h], shape: Side("base")));
        p.Add(Box([-h * 0.6, y1 - 0.02 * c.X, -h * 0.6], [h * 0.6, y1 + 0.03 * c.X, h * 0.6], Gold, detail: true, shape: Side("leaf", true)));
        p.Sock("base", [0, -h, 0], "-y");
        p.Sock("top", [0, y1, 0], "+y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, h], "+z");
        p.Sock("-z", [0, 0, -h], "-z");
        var area = 4 * c.Cell * (y1 + h);
        p.Stats(PlateMass(c, area), PlateHealth(c, area), c.Armour + 8, c.Seconds * 1.5);
        p.Set(("power", force * top / 1000),
            ("joint", S(("type", "piston"), ("axis", "+y"), ("min", 0), ("max", stroke), ("leaf", "top"), ("speed", top * 0.5), ("strength", force), ("give", 0.25 * stroke), ("sideLoad", side), ("overloadDamage", 0))),
            ("config", Py.L(
                Num("min", "Pulled in to", "Travel", "m", 0, stroke, 0.05, "How far out it rests when pulled in."),
                Num("max", "Pushed out to", "Travel", "m", 0, stroke, 0.05, "How far out it goes when pushed out (a signal of 1)."),
                Num("speed", "Speed", "Motor", "m/s", 0.05, top, 0.05, $"How fast it slides (up to {Py.G(top)} m/s)."),
                Num("strength", "Force", "Motor", "N", force / 10.0, force, force / 100.0, $"What it holds, up to {force} N."))),
            ("centreSockets", false));
        return p;
    }

    private static Part MotorHinge(Cls c)
    {
        var (torque, top) = Hinges(c);
        var p = new Part($"Motor Hinge {c.Name}", c, "Motor hinges", $"Signal 1: max, -1: min, 0: shut. Rated {torque / 1000} kN m.", role: "joint", color: "#4f6a7a");
        var h = c.Half;
        p.Add(Box([-h, -h, -h], [h, h, h], shape: Side("base")));
        p.Add(Box([h - 0.02 * c.X, -h * 0.5, -h * 0.5], [h + 0.02 * c.X, h * 0.5, h * 0.5], Gold, detail: true, shape: Side("leaf", true)));
        p.Sock("base", [-h, 0, 0], "-x");
        p.Sock("leaf", [h, 0, 0], "+x");
        var area = 6 * c.Cell * c.Cell;
        p.Stats(PlateMass(c, area), PlateHealth(c, area), c.Armour + 8, c.Seconds * 1.5);
        p.Set(("power", torque * top * 3.14159 / 180 / 1000),
            ("joint", S(("type", "motor"), ("axis", "+y"), ("pivot", Py.L(h, 0, 0)), ("min", -100), ("max", 100), ("leaf", "leaf"), ("speed", top), ("strength", torque), ("give", 20), ("overloadDamage", 0))),
            ("config", Py.L(
                Num("min", "Lowest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of -1 turns it to."),
                Num("max", "Highest angle", "Limits", "deg", -100, 100, 1, "The far end a signal of 1 (or opening it) turns it to."),
                Num("speed", "Speed", "Motor", "deg/s", 0, 2 * top, 1, $"How fast it turns: rated {top} deg/s (0%), from stopped (-100%) to {2 * top} deg/s (+100%)."),
                Num("strength", "Torque", "Motor", "N m", torque / 10.0, torque, torque / 100.0, $"What it holds, up to {torque} N m."))),
            ("centreSockets", false));
        return p;
    }

    private static Part Turntable(Cls c)
    {
        var (torque, top) = Turntables(c);
        var p = new Part($"Turntable {c.Name}", c, "Turntables", $"Signal 1 or -1 turns it, 0 holds. Rated {torque / 1000} kN m.", role: "joint", color: "#5b6168");
        var h = c.Half;
        var y = h * 0.6;
        p.Add(Box([-h, -h, -h], [h, y, h], shape: Side("base")));
        p.Add(Box([-h * 0.8, y, -h * 0.8], [h * 0.8, h, h * 0.8], hidden: true, shape: Side("base"))); // (the solid; the Joint sides rule: only detail boxes on the leaf)
        p.Add(Box([-h * 0.8, y, -h * 0.8], [h * 0.8, h, h * 0.8], Gold, detail: true, shape: Side("leaf", true)));
        p.Sock("base", [0, -h, 0], "-y");
        p.Sock("top", [0, h, 0], "+y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, h], "+z");
        p.Sock("-z", [0, 0, -h], "-z");
        var area = 6 * c.Cell * c.Cell;
        p.Stats(PlateMass(c, area) * 1.5, PlateHealth(c, area) * 1.5, c.Armour + 10, c.Seconds * 1.5);
        p.Set(("power", torque * top * 3.14159 / 180 / 1000),
            ("joint", S(("type", "turntable"), ("axis", "+y"), ("pivot", Py.L(0, h, 0)), ("leaf", "top"), ("speed", top), ("strength", torque), ("give", 20), ("overloadDamage", 0))),
            ("config", Py.L(
                Num("speed", "Speed", "Motor", "deg/s", 0, 2 * top, 1, $"How fast it turns at a full signal: rated {top} deg/s (0%), from stopped (-100%) to {2 * top} deg/s (+100%)."),
                Num("strength", "Torque", "Motor", "N m", torque / 10.0, torque, torque / 100.0, $"What it holds, up to {torque} N m."))),
            ("centreSockets", false));
        return p;
    }

    private static (int, int) GateHinges(Cls c) => c == Capital ? (2, 2) : (2, 3); // the leaf's cells wide, tall

    private static Part GateHinge(Cls c)
    {
        var p = new Part($"Gate Hinge {c.Name}", c, "Ramps and gates", "For a gate leaf. A signal locks it.", role: "hinge", color: Dark);
        var h = c.Half * 0.5;
        p.Add(Box([-h, -c.Half, -h], [0, c.Half, h], shape: Side("base")));
        p.Add(Box([0.01 * c.X, -c.Half * 0.8, -h * 0.8], [h, c.Half * 0.8, h * 0.8], hidden: true, shape: Side("base")));
        p.Add(Box([0.01 * c.X, -c.Half * 0.8, -h * 0.8], [h, c.Half * 0.8, h * 0.8], Gold, detail: true, shape: Side("leaf", true)));
        p.Sock("base", [-h, 0, 0], "-x");
        p.Sock("leaf", [h, 0, 0], "+x", kind: "leaf");
        p.Set(("config", Py.L(S(("key", "pushable"), ("label", "Opens when pushed"), ("group", "Door"), ("default", true),
            ("tooltip", "On: a player walking into it swings it. Off: only F opens it. A wire into its Push port overrides this while connected: high on, low off.")))));
        p.Stats(c.Cell * c.Cell * c.KgM2, c.Cell * c.Cell * c.HealthM2, c.Armour);
        p.Set(("hinge", S(("axis", "+y"), ("min", -110), ("max", 110), ("leaf", "leaf"))));
        return p;
    }

    private static Part GateLeaf(Cls c)
    {
        var (wide, tall) = GateHinges(c);
        var p = new Part($"Gate Leaf {c.Name} {F(wide * c.Cell)}x{F(tall * c.Cell)}", c, "Ramps and gates", "Hangs on a gate hinge.", role: "door", color: Rust);
        double x0 = 0.0, x1 = wide * c.Cell, y0 = -c.Half, y1 = c.Hi(tall);
        var t = 0.08 * c.Cell;
        p.Add(Box([x0, y0, -t], [x1, y1, t]));
        foreach (var k in new[] { 0.25, 0.5, 0.75 })
        {
            var y = y0 + (y1 - y0) * k;
            p.Add(Box([x0 + 0.01, y - 0.03 * c.X, -t - 0.004 * c.X], [x1 - 0.01, y + 0.03 * c.X, t + 0.004 * c.X], Dark, detail: true));
        }
        p.Sock("hang", [x0, 0, 0], "-x", kind: "leaf");
        var area = (x1 - x0) * (y1 - y0);
        p.Stats(PlateMass(c, area), PlateHealth(c, area), c.Armour + 6);
        return p;
    }

    // chain m, min m, m/s, N, W
    private static (int Chain, int Lo, int Speed, int Rating, int Power) Winches(Cls c) => c.Key switch
    {
        "medium" => (16, 1, 2, 300_000, 20_000), "large" => (32, 2, 3, 3_000_000, 200_000), _ => (64, 4, 4, 30_000_000, 900_000),
    };

    private static Part Winch(Cls c)
    {
        var (chain, lo, speed, rating, power) = Winches(c);
        var p = new Part($"Winch {c.Name} {chain} m", c, "Cranes and winches", $"Hooks a lifting eye: signal 1 reels in, -1 out. Lifts {rating / 10000} t.", role: "hitch", color: Dark);
        var h = c.Half;
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-h * 0.25, -h * 1.3, -h * 0.25], [h * 0.25, -h, h * 0.25], Gold, detail: true));
        p.Sock("+y", [0, h, 0], "+y");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+z", [0, 0, h], "+z");
        p.Sock("-z", [0, 0, -h], "-z");
        p.Stats(c.Cell * c.Cell * c.KgM2 * 4, c.Cell * c.Cell * c.HealthM2 * 3, c.Armour);
        p.Set(("hitch", S(("kind", "hook"), ("at", Py.L(0, -h * 1.3, 0)), ("chain", chain), ("winch", S(("min", lo), ("speed", speed), ("rating", rating))))), ("power", power));
        return p;
    }

    private static Part LiftingEye(Cls c)
    {
        var p = new Part($"Lifting Eye {c.Name}", c, "Cranes and winches", "Goes on a load: a winch's hook takes it.", role: "hitch", color: Gold);
        var h = c.Half * 0.5;
        p.Add(Box([-h, 0, -h], [h, h, h]));
        p.Sock("-y", [0, 0, 0], "-y");
        p.Stats(c.Cell * c.Cell * c.KgM2 * 0.2, c.Cell * c.Cell * c.HealthM2, c.Armour);
        p.Set(("hitch", S(("kind", "eye"), ("at", Py.L(0, h, 0)))));
        return p;
    }

    private static Part Boom(Cls c)
    {
        var n = c == Capital ? 4 : 8;
        var p = new Part($"Crane Boom {c.Name} {F(n * c.Cell)} m", c, "Cranes and winches", "Crane jib: goes on a motor hinge or turntable.", role: "frame", color: Hazard);
        var h = c.Half;
        double x0 = c.Lo(), x1 = c.Hi(n);
        var t = 0.14 * c.Cell;
        foreach (var sy in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                double y = sy * (h - t), z = sz * (h - t);
                p.Add(Box([x0, Math.Min(y, y + sy * t), Math.Min(z, z + sz * t)], [x1, Math.Max(y, y + sy * t), Math.Max(z, z + sz * t)]));
            }
        const int Ties = 10;
        for (var k = 0; k < Ties; k++)
        {
            var x = x0 + (x1 - x0) * (k + 1) / (Ties + 1);
            p.Add(Box([x - t / 2, -h + 0.02, -h * 0.82], [x + t / 2, h - 0.02, -h * 0.74], Dark, detail: true));
            p.Add(Box([x - t / 2, -h + 0.02, h * 0.74], [x + t / 2, h - 0.02, h * 0.82], Dark, detail: true));
        }
        p.Add(Box([x0, -h, -h], [x0 + 0.1 * c.Cell, h, h], Dark));
        p.Add(Box([x1 - 0.1 * c.Cell, -h, -h], [x1, h, h], Dark));
        p.Sock("-x", [x0, 0, 0], "-x");
        p.Sock("+x", [x1, 0, 0], "+x");
        var tip = (n - 1) * c.Cell;
        p.Sock("tip+y", [tip, h, 0], "+y");
        p.Sock("tip-y", [tip, -h, 0], "-y");
        p.Sock("root+y", [0, h, 0], "+y");
        p.Sock("root-y", [0, -h, 0], "-y");
        p.Stats(n * c.Cell * c.Cell * c.KgM2 * 1.2, n * c.Cell * c.Cell * c.HealthM2, c.Armour);
        p.Set(("centreSockets", false));
        return p;
    }

    // cells long, cells wide, m/s, W
    private static (int N, int W, double Speed, int Power) Conveyors(Cls c) => c.Key switch { "medium" => (4, 2, 2.0, 2000), "large" => (4, 2, 4.0, 40000), _ => (2, 1, 8.0, 500000) };

    private static Part Conveyor(Cls c)
    {
        var (n, w, speed, power) = Conveyors(c);
        var p = new Part($"Conveyor {c.Name} {F(n * c.Cell)}x{F(w * c.Cell)}", c, "Cranes and winches", $"Carries what is on it, up to {Py.G(speed)} m/s. A signal sets its speed.", role: "logic", color: "#2f3338");
        var h = c.Half;
        double x0 = -h, x1 = c.Hi(w), z0 = -c.Hi(n - 1) - c.Cell, z1 = h;
        p.Add(Box([x0, -h, z0], [x1, h * 0.2, z1]));
        for (var k = 1; k < 8; k++)
        {
            var z = z0 + (z1 - z0) * k / 8;
            p.Add(Box([x0 + 0.02, h * 0.2, z - 0.01 * c.X], [x1 - 0.02, h * 0.2 + 0.006 * c.X, z + 0.01 * c.X], "#7c838c", detail: true));
        }
        p.Sock("-y", [0, -h, 0], "-y");
        p.Sock("+z", [0, 0, z1], "+z");
        p.Sock("-z", [0, 0, z0], "-z");
        p.Sock("+x", [x1, 0, 0], "+x");
        p.Sock("-x", [x0, 0, 0], "-x");
        var area = (x1 - x0) * (z1 - z0);
        p.Stats(PlateMass(c, area) * 2, PlateHealth(c, area), c.Armour);
        p.Set(("logic", S(("kind", "conveyor"))), ("conveyor", S(("speed", speed), ("dir", "-z"))), ("power", power), ("walk", "floor"));
        return p;
    }

    // ---- Big weapons

    private static PyDict GunBlock(int damage, int rpm, object spread, int range, int maxRange, int mag, int reload, int blast, int blastDamage, int kick, string fx, int speed) =>
        S(("damage", damage), ("rpm", rpm), ("spread", spread), ("range", range), ("maxRange", maxRange), ("mag", mag), ("reload", reload), ("flags", Py.L("explosive")),
            ("blast", blast), ("blastDamage", blastDamage), ("kick", kick), ("fx", fx), ("speed", speed), ("soundOf", "Leviathan .50"));

    // name, class, barrel cells, barrels, gun block, boxes' mass (kg)
    private static IEnumerable<(string, Cls, int, int, PyDict, int)> Guns() =>
    [
        ("Flak Cannon", Medium, 3, 2, GunBlock(90, 180, 0.6, 500, 800, 40, 4, 5, 120, 300, "cannon", 500), 3000),
        ("Naval Gun 20 cm", Large, 2, 1, GunBlock(1400, 8, 0.15, 800, 1000, 1, 6, 12, 1600, 8000, "cannon", 600), 60000),
        ("Naval Gun 40 cm", Capital, 2, 1, GunBlock(6000, 3, 0.1, 1000, 1000, 1, 15, 30, 7000, 60000, "cannon", 700), 800000),
        ("Rocket Battery", Large, 1, 1, GunBlock(500, 60, 2, 600, 900, 12, 20, 10, 700, 300, "rocket", 150), 30000),
    ];

    private static Part Gun(string name, Cls c, int n, int barrels, PyDict g, int mass)
    {
        var p = new Part(name, c, "Heavy guns", "Turret gun: needs a magazine, and a seat, director or signal.", role: "gun", color: "#4a4f45");
        var h = c.Half;
        var t = 0.12 * c.Cell;
        var z1 = -h - n * c.Cell;
        p.Add(Box([-h, -h, -h], [h, h * 0.7, h]));
        var gap = c.Cell / (barrels + 1);
        for (var b = 0; b < barrels; b++)
        {
            var x = -h + gap * (b + 1);
            p.Add(Box([x - t / 2, -t / 2, z1], [x + t / 2, t / 2, -h], "#2a2b2d"));
        }
        p.Sock("bottom", [0, -h, 0], "-y");
        p.Sock("top", [0, h * 0.7, 0], "+y");
        p.Sock("left", [-h, 0, 0], "-x");
        p.Sock("right", [h, 0, 0], "+x");
        p.Sock("back", [0, 0, h], "+z");
        p.Stats(mass, c.Cell * c.Cell * c.HealthM2 * 4, c.Armour + 12);
        g = new PyDict(g) { ["muzzle"] = Py.L(0, 0, z1) };
        p.Set(("gun", g), ("centreSockets", false));
        return p;
    }

    private static (int Rounds, int Radius, int Dmg) Magazines(Cls c) => c.Key switch { "medium" => (2000, 12, 700), "large" => (400, 20, 4000), _ => (200, 20, 20000) };

    private static Part Magazine(Cls c)
    {
        var (rounds, radius, dmg) = Magazines(c);
        var p = new Part($"Shell Magazine {c.Name}", c, "Heavy guns", $"{rounds} rounds for the heavy guns its hoists reach. Holed, it cooks off.", role: "ammobox", color: "#5a6048");
        var h = c.Half;
        p.Add(Box([-h, -h, -h], [h, h, h]));
        p.Add(Box([-h * 0.8, h, -h * 0.8], [h * 0.8, h + 0.01 * c.X, h * 0.8], Hazard, detail: true));
        foreach (var (nm, at, d) in new (string, double[], string)[] { ("+x", [h, 0, 0], "+x"), ("-x", [-h, 0, 0], "-x"), ("+y", [0, h, 0], "+y"), ("-y", [0, -h, 0], "-y"), ("+z", [0, 0, h], "+z"), ("-z", [0, 0, -h], "-z") })
            p.Sock(nm, at, d);
        p.Stats(c.Cell * c.Cell * c.KgM2 * 5, c.Cell * c.Cell * c.HealthM2, c.Armour);
        p.Set(("ammoBox", S(("rounds", rounds), ("cookOff", S(("radius", radius), ("damage", dmg))))));
        return p;
    }

    private static Part Hoist(Cls c)
    {
        var n = c.Key switch { "medium" => 8, "large" => 2, _ => 1 };
        var p = new Part($"Ammo Hoist {c.Name} {F(n * c.Cell)} m", c, "Heavy guns", "Carries rounds from a magazine to a gun.", role: "feedchute", color: "#6a7a4a");
        var h = c.Half * 0.5;
        double x0 = c.Lo(), x1 = c.Hi(n);
        p.Add(Box([x0, -h, -h], [x1, h, h]));
        p.Sock("-x", [x0, 0, 0], "-x");
        p.Sock("+x", [x1, 0, 0], "+x");
        p.Stats(n * c.Cell * c.Cell * c.KgM2 * 0.4, n * c.Cell * c.Cell * c.HealthM2, c.Armour);
        return p;
    }

    // ---- Control

    private static Part Helm()
    {
        var p = new Part("Bridge Helm", Small, "Bridge", "Drives its construct.", role: "seat", color: "#3a3f46", group: GroupSmall, tool: "wrench");
        p.Add(Box([-0.5, 0, -0.5], [0.5, 0.25, 0.5], shape: S(("use", true))));
        p.Add(Box([-0.3, 0.25, 0.15], [0.3, 1.0, 0.3], "#2f343a", shape: S(("use", true))));
        p.Add(Box([-0.5, 0.25, -0.5], [0.5, 1.15, -0.25], Dark));
        p.Add(Box([-0.42, 0.9, -0.26], [0.42, 1.1, -0.24], "#2fae6a", detail: true));
        p.Add(Box([-0.1, 1.15, -0.4], [0.1, 1.3, -0.3], Gold, detail: true));
        p.Sock("-y", [0, 0, 0], "-y");
        p.Sock("+x", [0.5, 0.125, 0], "+x");
        p.Sock("-x", [-0.5, 0.125, 0], "-x");
        p.Stats(160, 300, 8, 3);
        p.Set(("seat", S(("at", Py.L(0, 0.25, 0.0)), ("helm", true))));
        return p;
    }

    private static Part CrewStation()
    {
        var p = new Part("Crew Station", Small, "Bridge", "Crew seat: wire it to what reads a seat.", role: "seat", color: "#3a3f46", group: GroupSmall, tool: "wrench");
        p.Add(Box([-0.4, 0, -0.4], [0.4, 0.25, 0.4], shape: S(("use", true))));
        p.Add(Box([-0.25, 0.25, 0.2], [0.25, 0.95, 0.32], "#2f343a", shape: S(("use", true))));
        p.Add(Box([-0.4, 0.25, -0.4], [0.4, 1.0, -0.2], Dark));
        p.Add(Box([-0.32, 0.8, -0.21], [0.32, 0.95, -0.19], "#2f8fae", detail: true));
        p.Sock("-y", [0, 0, 0], "-y");
        p.Stats(90, 200, 5, 2);
        p.Set(("seat", S(("at", Py.L(0, 0.25, 0.05)))));
        return p;
    }

    private static Part Director()
    {
        var p = new Part("Fire Director", Small, "Bridge", "One seat aims and fires every gun wired to it.", role: "logic", color: "#3a4a3f", group: GroupSmall, tool: "wrench");
        p.Add(Box([-0.25, -0.125, -0.25], [0.25, 0.25, 0.25]));
        p.Add(Box([-0.2, 0.25, -0.05], [0.2, 0.33, 0.05], "#2a2b2d", detail: true));
        p.Sock("-y", [0, -0.125, 0], "-y");
        p.Sock("+x", [0.25, 0, 0], "+x");
        p.Sock("-x", [-0.25, 0, 0], "-x");
        p.Stats(35, 150, 4, 2);
        p.Set(("logic", S(("kind", "director"))), ("power", 200));
        return p;
    }

    private static Part Dial(string name, string kind, string desc, Cls c, int maximum)
    {
        var p = new Part(name, c, "Bridge", desc, role: "logic", color: "#2f343a");
        var h = c.Half;
        p.Add(Box([-h, -h, -h * 0.3], [h, h, h * 0.3]));
        p.Add(Box([-h * 0.8, -h * 0.8, -h * 0.3 - 0.004 * c.X], [h * 0.8, h * 0.8, -h * 0.28], "#d8d4c0", detail: true));
        p.Sock("back", [0, 0, h * 0.3], "+z");
        p.Sock("-y", [0, -h, 0], "-y");
        p.Sock("+y", [0, h, 0], "+y");
        p.Stats(c.Cell * c.Cell * c.KgM2, c.Cell * c.Cell * c.HealthM2, 4);
        p.Set(("logic", S(("kind", kind))), ("dial", S(("at", Py.L(0, 0, -h * 0.3)), ("face", "-z"), ("max", maximum))));
        return p;
    }

    private static Part Sensor()
    {
        var p = new Part("Long Range Sensor", Small, "Bridge", "On while a player is in range, up to 300 m.", role: "logic", color: "#4f6a7a", group: GroupSmall, tool: "wrench");
        p.Add(Box([-0.125, -0.125, -0.125], [0.125, 0.5, 0.125]));
        p.Add(Box([-0.3, 0.5, -0.05], [0.3, 0.6, 0.05], "#d0d0d0", detail: true));
        p.Sock("-y", [0, -0.125, 0], "-y");
        p.Stats(20, 80, 2, 2);
        p.Set(("logic", S(("kind", "proximity"), ("settings", Py.L(60)), ("labels", Py.L("60 m")))),
            ("config", Py.L(Num("radius", "Range", "Sensor", "m", 5, 300, 5, "How far it reaches.", 120))));
        return p;
    }

    // ---- Lights

    private static List<object?> LampConfig(string colour) => Py.L(
        S(("key", "colour"), ("type", "colour"), ("label", "Colour"), ("group", "Light"), ("default", colour), ("tooltip", "The colour it glows when lit.")),
        Num("brightness", "Brightness", "Light", "%", 10, 200, 1, "How bright it glows.", 100),
        Num("blink", "Blink", "Light", "Hz", 0, 10, 0.1, "Flashes this many times a second while lit (0: steady).", 0));

    private static Part Floodlight(Cls c)
    {
        var name = c == Small ? "Floodlight" : $"Floodlight {c.Name}";
        var p = new Part(name, c, "Lights", "Lights while its input is on.", role: "logic", color: Dark, group: GroupFor(c), tool: ToolFor(c));
        var n = c == Small ? 2 : 1;
        var h = n * c.Cell / 2;
        var y0 = -c.Half;
        p.Add(Box([-h, y0, -h], [h, y0 + 2 * h, h]));
        p.Add(Box([-h * 0.8, y0 + h * 0.2, -h - 0.004 * c.X], [h * 0.8, y0 + h * 1.8, -h], Bright, detail: true, shape: S(("paintable", false))));
        p.Sock("-y", [0, y0, 0], "-y");
        p.Sock("+z", [0, 0, h], "+z");
        p.Stats(8 * c.X * c.X / 4.0, 40 * c.X, 2);
        p.Set(("logic", S(("kind", "lamp"))), ("power", 250 * c.X * c.X / 4.0), ("config", LampConfig("#fff4d0")), ("faces", "forward"));
        return p;
    }

    private static Part HangingLantern(Cls c)
    {
        var name = c == Small ? "Hanging Lantern" : $"Hanging Lantern {c.Name}";
        var p = new Part(name, c, "Lights", "Hangs under a walkway. Lights while its input is on.", role: "logic", color: Lantern, group: GroupFor(c), tool: ToolFor(c));
        // Hung from its +y socket: a chain, a cap and the body.
        var s = c.Cell;
        var top = c.Half;
        p.Add(Box([-s * 0.5, top - s * 2.2, -s * 0.5], [s * 0.5, top - s * 0.7, s * 0.5]));
        p.Add(Box([-s * 0.3, top - s * 0.7, -s * 0.3], [s * 0.3, top - s * 0.55, s * 0.3], Dark));
        p.Add(Box([-s * 0.05, top - s * 0.55, -s * 0.05], [s * 0.05, top, s * 0.05], Dark));
        p.Add(Box([-s * 0.54, top - s * 1.6, -s * 0.54], [s * 0.54, top - s * 1.3, s * 0.54], "#ffb060", detail: true, shape: S(("paintable", false))));
        p.Sock("+y", [0, top, 0], "+y");
        p.Stats(6 * c.X, 30 * c.X, 1);
        p.Set(("logic", S(("kind", "lamp"))), ("power", 30 * c.X), ("config", LampConfig("#ff9040")), ("centreSockets", false));
        return p;
    }

    // ---- Walking about

    private static Part Catwalk(int n)
    {
        var c = Medium;
        var p = new Part($"Catwalk {n} m", c, "Walkways", "Railed walkway for gantries and hulls.", role: "panel", color: Steel);
        var h = c.Half;
        var y = c.Cell * 0.05;
        double z0 = -h, z1 = c.Hi(n);
        p.Add(Box([-h, -y, z0], [h, y, z1]));
        foreach (var s in new[] { -1, 1 })
        {
            p.Add(Box([s * h - 0.02, y, z0 + 0.02], [s * h + 0.02, 1.0, z0 + 0.06], Dark, detail: true));
            p.Add(Box([s * h - 0.02, y, z1 - 0.06], [s * h + 0.02, 1.0, z1 - 0.02], Dark, detail: true));
            p.Add(Box([s * h - 0.02, 0.96, z0 + 0.02], [s * h + 0.02, 1.0, z1 - 0.02], Dark, detail: true));
        }
        p.Sock("-z", [0, 0, z0], "-z");
        p.Sock("+z", [0, 0, z1], "+z");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("far+x", [h, 0, (n - 1) * c.Cell], "+x");
        p.Sock("far-x", [-h, 0, (n - 1) * c.Cell], "-x");
        var area = n * c.Cell;
        p.Stats(area * 40, area * 60, 6, 3);
        p.Set(("walk", "floor"), ("centreSockets", false));
        return p;
    }

    private static Part Ladder(int n)
    {
        var c = Medium;
        var rises = (int)(n / 0.375);
        var steps = Math.Min(rises, 28);
        var p = new Part($"Ladder {n} m", c, "Walkways", "Walk up it, outside a hull or gantry.", role: "frame", color: Dark);
        var h = c.Half;
        var y0 = c.Lo();
        for (var k = 0; k < steps; k++)
        {
            var y = y0 + (k + 1) * n / (double)steps;
            var z = h - (k + 1) * 0.25;
            p.Add(Box([-h, y - 0.05, z - 0.13], [h, y, z + 0.13]));
        }
        var zEnd = h - steps * 0.25 - 0.13;
        p.Add(Box([-h - 0.02, y0, zEnd], [-h + 0.04, y0 + n, h], Dark, detail: true));
        p.Add(Box([h - 0.04, y0, zEnd], [h + 0.02, y0 + n, h], Dark, detail: true));
        p.Sock("-y", [0, y0 + n / (double)steps - 0.05, h - 0.25], "-y");
        p.Sock("+y", [0, y0 + n, h - steps * 0.25], "+y");
        p.Stats(n * 30, n * 70, 6, 3);
        p.Set(("walk", "ladder"), ("centreSockets", false));
        return p;
    }

    private static Part Scaffold()
    {
        var c = Medium;
        const int N = 4;
        var p = new Part("Scaffold Tower 4 m", c, "Walkways", "Stack it, add ladders and catwalks.", role: "frame", color: Wood);
        var h = c.Half;
        double x1 = c.Hi(N), y1 = c.Hi(N);
        var t = 0.1;
        foreach (var x in new[] { -h, x1 - 2 * t })
            foreach (var z in new[] { -h, x1 - 2 * t })
                p.Add(Box([x, -h, z], [x + 2 * t, y1 - 0.1, z + 2 * t]));
        p.Add(Box([-h, y1 - 0.1, -h], [x1, y1, x1]));
        foreach (var z in new[] { -h + 0.04, x1 - 2 * t + 0.04 })
            p.Add(Box([-h + 0.2, h + 1.0, z], [x1 - 0.2, h + 1.12, z + 0.12], "#8a7a5a", detail: true));
        foreach (var x in new[] { -h + 0.04, x1 - 2 * t + 0.04 })
            p.Add(Box([x, h + 1.0, -h + 0.2], [x + 0.12, h + 1.12, x1 - 0.2], "#8a7a5a", detail: true));
        var mid = c.Cell;
        p.Sock("-y", [0, -h, 0], "-y");
        p.Sock("-y+", [(N - 1) * c.Cell, -h, (N - 1) * c.Cell], "-y");
        p.Sock("+y", [0, y1, 0], "+y");
        p.Sock("+y+", [(N - 1) * c.Cell, y1, (N - 1) * c.Cell], "+y");
        p.Sock("+x", [x1, y1 - 0.05, mid], "+x");
        p.Sock("-x", [-h, y1 - 0.05, mid], "-x");
        p.Sock("+z", [mid, y1 - 0.05, x1], "+z");
        p.Sock("-z", [mid, y1 - 0.05, -h], "-z");
        p.Stats(1500, 1200, 10, 5);
        p.Set(("walk", "floor"), ("centreSockets", false));
        return p;
    }

    private static Part BulkheadFrame()
    {
        var c = Medium;
        var p = new Part("Bulkhead Door Frame 4x3", c, "Walkways", "A sliding door fills it. Two make an airlock.", role: "frame", color: Steel);
        var h = c.Half;
        var t = 0.12;
        double x1 = c.Hi(4), y1 = c.Hi(3);
        p.Add(Box([-h, -h, -t], [0.5, y1, t]));
        p.Add(Box([2.5 - 0.0, -h, -t], [x1, y1, t]));
        p.Add(Box([0.5, 1.5, -t], [2.5, y1, t]));
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+x", [x1, 0, 0], "+x");
        p.Sock("jamb", [0.5, 0, 0], "+x");
        p.Sock("-y", [0, -h, 0], "-y");
        p.Sock("-y+", [3, -h, 0], "-y");
        p.Sock("+y", [1, y1, 0], "+y");
        p.Stats(1800, 2400, 28, 5);
        p.Set(("centreSockets", false));
        return p;
    }

    private static Part SlidingDoor()
    {
        var c = Medium;
        var p = new Part("Sliding Door 2x2", c, "Walkways", "Slides on a wired piston.", role: "panel", color: Rust);
        var h = c.Half;
        var t = 0.08;
        p.Add(Box([-h, -h, -t], [c.Hi(2), c.Hi(2), t]));
        p.Add(Box([0.4, 0.8, -t - 0.01], [0.6, 1.0, t + 0.01], Gold, detail: true));
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+x", [c.Hi(2), 0, 0], "+x");
        p.Stats(320, 700, 24, 3);
        p.Set(("centreSockets", false));
        return p;
    }

    private static List<(string, Part)> Everything()
    {
        var o = new List<(string, Part)>();
        foreach (var c in Big)
        {
            o.Add(("Launch", Ramp(c)));
            o.Add(("Launch", Lift(c)));
            o.Add(("Launch", DoorLeaf(c)));
            o.Add(("Launch", Gate(c)));
            o.Add(("Launch", GateHinge(c)));
            o.Add(("Launch", GateLeaf(c)));
            o.Add(("Launch", Catapult(c)));
            o.Add(("Launch", Arrestor(c)));
            o.Add(("Launch", Dock(c)));
            foreach (var stroke in Pistons(c).Strokes)
                o.Add(("Actuators", Piston(c, stroke)));
            o.Add(("Actuators", MotorHinge(c)));
            o.Add(("Actuators", Turntable(c)));
            o.Add(("Moving", Winch(c)));
            o.Add(("Moving", LiftingEye(c)));
            o.Add(("Moving", Boom(c)));
            o.Add(("Moving", Conveyor(c)));
            o.Add(("Weapons", Magazine(c)));
            o.Add(("Weapons", Hoist(c)));
        }
        foreach (var x in new[] { Catapult(Small), Arrestor(Small), Dock(Small), Bay(Small), Bay(Medium), Bay(Large), Mast(Small), Mast(Medium) })
            o.Add(("Launch", x));
        foreach (var c in new[] { Small, Medium })
        {
            o.Add(("Launch", MagLock(c)));
            o.Add(("Lights", HangingLantern(c)));
        }
        foreach (var c in new[] { Small, Medium, Large })
            o.Add(("Lights", Floodlight(c)));
        foreach (var (name, c, n, barrels, g, mass) in Guns())
            o.Add(("Weapons", Gun(name, c, n, barrels, g, mass)));
        foreach (var x in new[] { Helm(), CrewStation(), Director(), Sensor(),
                     Dial("Deck Speed Dial", "speed", "Shows its construct's speed.", Medium, 40),
                     Dial("Deck Fuel Dial", "fuelgauge", "Shows what the tanks hold.", Medium, 1),
                     Dial("Deck Charge Dial", "chargegauge", "Shows what the batteries hold.", Medium, 1) })
            o.Add(("Control", x));
        foreach (var x in new[] { Catwalk(4), Catwalk(8), Ladder(4), Ladder(8), Scaffold(), BulkheadFrame(), SlidingDoor() })
            o.Add(("Interior", x));
        return o;
    }

    // ---- Blueprints: what the parts add up to

    private static PyDict Step(string part, int? on = null, string? socket = null, string? by = null, int? spin = null)
    {
        var d = S(("part", part));
        if (on is { } o)
            d.Update(("on", o), ("socket", socket), ("by", by));
        if (spin is { } s)
            d["spin"] = s;
        return d;
    }

    private static readonly (string Name, PyDict[] Steps)[] Blueprints =
    [
        ("Deck Lift", [Step("Piston Medium 4 m"), Step("Lift Platform Medium 5x5", 0, "top", "-y")]),
        ("Naval Turret", [Step("Turntable Large"), Step("Naval Gun 20 cm", 0, "top", "bottom"), Step("Shell Magazine Large", 1, "back", "-z")]),
        ("Harbour Crane", [Step("Turntable Large"), Step("Crane Boom Large 32 m", 0, "top", "root-y"), Step("Winch Large 32 m", 1, "tip-y", "+y")]),
        ("Gate Ramp", [Step("Lift Platform Medium 5x5"), Step("Motor Hinge Medium", 0, "+x", "base", 1), Step("Ramp Deck Medium 8x5", 1, "leaf", "-x", 0), Step("Arched Gate Medium 5x5", 0, "+y.1-", "-y-", 2)]),
    ];

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, Everything());
        foreach (var (name, steps) in Blueprints)
            o.File($"Constructs/Blueprints/{FileName(name)}",
                PyJson.Dumps(S(("format", "call-of-slop-construct-blueprint/1"), ("name", name), ("steps", steps.Cast<object?>().ToList())), 2) + "\n");
    }
}
