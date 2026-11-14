namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// The Sandbox's modular engine pieces (Constructs/Parts/ModularEngine) and their rules (Constructs/ModularEngine.json):
/// docs/sandbox/CONSTRUCTS.md, "Modular engines". Was tools/constructs/engine_parts.py.
///
/// Ten kinds of piece in four sizes; the look is exposed and industrial (the traction city reference): open crank frames, columns round a
/// visible pumping piston, a spoked flywheel, tall stacks. Boxes tagged "pump" (metres of stroke) slide up and down with the engine, boxes
/// tagged "spin" turn about the part's z. <see cref="Sizes"/> is the one table of scale numbers: linear size x1, x4, x16, x64, with each
/// size's mass and toughness multipliers and the rules' numbers (power a cylinder, rpm).
/// </summary>
public sealed class EngineParts : IPartGenerator
{
    public string Name => "engine_parts";

    private const string Out = "Constructs/Parts/ModularEngine";
    private const string Rules = "Constructs/ModularEngine.json";
    private const string Group = "Modular engines";

    // name, linear scale, mass x, health x, cylinder kW, idle / peak / limiter rpm, attach seconds x, needs a starter
    private static readonly (string Title, int S, int MassX, int HpX, int Kw, int Idle, int Peak, int Max, double SecsX, bool Start)[] Sizes =
    [
        ("Small", 1, 1, 1, 40, 900, 5000, 7000, 1.0, false),
        ("Medium", 4, 20, 4, 800, 600, 2800, 4000, 1.5, false),
        ("Large", 16, 400, 16, 16000, 300, 1400, 2000, 2.5, true),
        ("Capital", 64, 8000, 64, 320000, 150, 700, 1000, 4.0, true),
    ];

    private const double BurnPerKw = 0.00044; // litres a second a kW at full throttle and the limiter (the stock Engine's 0.05 l/s at 114 kW)
    private const double MisfireBelow = 0.5; // a cylinder under this share of its health misfires...
    private const double MisfirePower = 0.5; // ...and gives this share of its power
    private const double ThirstyAt = 1.15; // a burn this many times the plain carburettor's reads "thirsty"
    private const double FrugalAt = 0.85; // and this much reads "frugal"

    private const string Iron = "#3b3936", Dark = "#262524", Steel = "#8d9096", Rust = "#7c4a2e", Copper = "#a8623a", Brass = "#b08d3c", Soot = "#1d1b1a", Hot = "#c9652a";

    private static PyDict At(double[] c, double[] size, string color, bool detail = false, params (string, object?)[] extra)
    {
        var b = new PyDict
        {
            ["min"] = Enumerable.Range(0, 3).Select(k => (object?)(c[k] - size[k] / 2)).ToList(),
            ["max"] = Enumerable.Range(0, 3).Select(k => (object?)(c[k] + size[k] / 2)).ToList(),
            ["color"] = color,
        };
        if (detail)
            b["detail"] = true;
        b.Update(extra);
        return b;
    }

    private static PyDict Sock(string name, double[] pos, string d) => new() { ["name"] = name, ["at"] = pos.Select(x => (object?)x).ToList(), ["dir"] = d };

    private sealed record Model(List<PyDict> Boxes, List<PyDict> Sockets, List<PyDict> Ports, PyDict? Piece, int Mass, int Health, int Armour);

    private static readonly (string, object?) Spin = ("spin", true);

    /// <summary>An open bearing frame: end plates, rails and a pan, with the turning shaft, webs and pin in plain view.</summary>
    private static Model Crank()
    {
        var b = new List<PyDict>
        {
            At([0, 0, 0.222], [0.5, 0.5, 0.056], Iron),
            At([0, 0, -0.222], [0.5, 0.5, 0.056], Iron),
            At([0, -0.2, 0], [0.44, 0.1, 0.386], Dark),
            At([0, 0, 0], [0.42, 0.42, 0.4], Dark, false, ("hidden", true)), // (open to the eye, solid to a crosshair or a round)
        };
        foreach (var x in new[] { -0.205, 0.205 })
            b.Add(At([x, 0.205, 0], [0.07, 0.07, 0.386], Iron));
        b.Add(At([0, 0, 0], [0.1, 0.1, 0.49], Steel, true, Spin));
        b.Add(At([0, 0.06, 0.09], [0.15, 0.24, 0.05], Brass, true, Spin));
        b.Add(At([0, 0.06, -0.09], [0.15, 0.24, 0.05], Brass, true, Spin));
        b.Add(At([0, 0.12, 0], [0.08, 0.08, 0.13], Steel, true, Spin));
        var s = new List<PyDict>
        {
            Sock("front", [0, 0, 0.25], "+z"), Sock("back", [0, 0, -0.25], "-z"), Sock("+y", [0, 0.25, 0], "+y"),
            Sock("-y", [0, -0.25, 0], "-y"), Sock("+x", [0.25, 0, 0], "+x"), Sock("-x", [-0.25, 0, 0], "-x"),
        };
        return new Model(b, s, [], Kw("crank"), 30, 220, 8);
    }

    private static PyDict Kw(string kind, params (string, object?)[] more) => new PyDict { ["kind"] = kind }.Update(more);

    /// <summary>Four columns between a base and a head, the piston and its rod pumping in the open between them.</summary>
    private static Model Cylinder()
    {
        var b = new List<PyDict>
        {
            At([0, -0.345, 0], [0.44, 0.06, 0.44], Iron),
            At([0, 0.325, 0], [0.46, 0.1, 0.46], Rust),
            At([0, -0.01, 0], [0.4, 0.61, 0.4], Dark, false, ("hidden", true)), // (open to the eye, solid to a crosshair or a round)
        };
        foreach (var x in new[] { -0.185, 0.185 })
            foreach (var z in new[] { -0.185, 0.185 })
                b.Add(At([x, -0.01, z], [0.06, 0.61, 0.06], Iron));
        b.Add(At([0, -0.27, 0], [0.33, 0.09, 0.33], Copper, true));
        b.Add(At([0, 0.03, 0], [0.3, 0.12, 0.3], Steel, true, ("pump", 0.16)));
        b.Add(At([0, -0.15, 0], [0.08, 0.31, 0.08], Steel, true, ("pump", 0.08)));
        b.Add(At([0, 0.385, 0], [0.3, 0.03, 0.3], Copper, true));
        b.Add(At([0.15, 0.39, 0.15], [0.05, 0.045, 0.05], Brass, true));
        b.Add(At([-0.15, 0.39, 0.15], [0.05, 0.045, 0.05], Brass, true));
        b.Add(At([0.15, 0.39, -0.15], [0.05, 0.045, 0.05], Brass, true));
        b.Add(At([-0.15, 0.39, -0.15], [0.05, 0.045, 0.05], Brass, true));
        var s = new List<PyDict> { Sock("base", [0, -0.375, 0], "-y"), Sock("head", [0, 0.375, 0], "+y"), Sock("+x", [0.23, 0.325, 0], "+x"), Sock("-x", [-0.23, 0.325, 0], "-x") };
        return new Model(b, s, [], Kw("cylinder"), 28, 180, 6);
    }

    private static List<PyDict> TopBase() => [Sock("base", [0, -0.125, 0], "-y"), Sock("top", [0, 0.125, 0], "+y")];

    private static PyDict FuelPort(params double[] at) => new()
    {
        ["name"] = "Fuel in", ["type"] = "pipe", ["dir"] = "in", ["fluid"] = "fuel", ["at"] = at.Select(x => (object?)x).ToList(), ["face"] = "+x",
    };

    private static Model Carburettor() => new(
        [At([0, -0.075, 0], [0.4, 0.1, 0.4], Brass), At([0, 0.03, 0], [0.26, 0.19, 0.26], Copper), At([0, 0.1, 0], [0.33, 0.04, 0.33], Steel, true), At([0.15, 0.0, 0], [0.1, 0.1, 0.1], Brass, true)],
        TopBase(), [FuelPort(0.2, 0.0, 0.0)], Kw("fuel", ("power", 1.0), ("thirst", 1.0)), 8, 90, 2);

    private static Model InjectorRail()
    {
        var b = new List<PyDict> { At([0, -0.08, 0], [0.42, 0.09, 0.42], Iron), At([0, 0.03, 0], [0.12, 0.19, 0.42], Copper) };
        foreach (var z in new[] { -0.14, 0.0, 0.14 })
        {
            b.Add(At([0.1, 0.0, z], [0.08, 0.1, 0.05], Brass, true));
            b.Add(At([-0.1, 0.0, z], [0.08, 0.1, 0.05], Brass, true));
        }
        return new Model(b, TopBase(), [FuelPort(0.21, -0.08, 0.0)], Kw("fuel", ("power", 1.2), ("thirst", 0.8)), 6, 80, 2);
    }

    private static Model Turbo() => new(
        [At([0, -0.08, 0], [0.4, 0.09, 0.4], Iron), At([0, 0.03, 0], [0.3, 0.19, 0.3], Rust),
            At([-0.12, 0.03, 0], [0.1, 0.17, 0.2], Hot, true, ("shape", "cylinder")), At([0.12, 0.03, 0], [0.1, 0.2, 0.24], Steel, true, ("shape", "cylinder"))],
        TopBase(), [], Kw("boost", ("power", 1.4), ("thirst", 1.15), ("spool", 1.5)), 14, 110, 3);

    private static Model Supercharger()
    {
        var b = new List<PyDict> { At([0, -0.08, 0], [0.4, 0.09, 0.4], Iron), At([0, 0.03, 0], [0.32, 0.19, 0.34], Steel) };
        foreach (var x in new[] { -0.09, 0.0, 0.09 })
            b.Add(At([x, 0.1, 0], [0.05, 0.03, 0.3], Rust, true));
        return new Model(b, TopBase(), [], Kw("boost", ("power", 1.25), ("thirst", 1.25)), 18, 120, 3);
    }

    /// <summary>A big spoked wheel, turning, between the crank's end and the shaft that leaves it.</summary>
    private static Model Flywheel()
    {
        List<PyDict> b =
        [
            At([0, 0, -0.17], [0.5, 0.5, 0.06], Iron),
            At([0, 0, 0.0], [0.16, 0.16, 0.3], Steel),
            At([0, 0, 0.17], [0.24, 0.24, 0.06], Iron),
            At([0, 0, 0.0], [0.9, 0.9, 0.09], Iron, true, ("shape", "tube"), ("inner", 0.82), ("segments", 24), Spin),
            At([0, 0, 0.0], [0.82, 0.08, 0.05], Steel, true, Spin),
            At([0, 0, 0.0], [0.08, 0.82, 0.05], Steel, true, Spin),
        ];
        List<PyDict> s =
        [
            Sock("crank", [0, 0, -0.2], "-z"), Sock("+z", [0, 0, 0.2], "+z"), Sock("+y", [0, 0.25, -0.17], "+y"),
            Sock("-y", [0, -0.25, -0.17], "-y"), Sock("+x", [0.25, 0, -0.17], "+x"), Sock("-x", [-0.25, 0, -0.17], "-x"),
        ];
        List<PyDict> ports =
        [
            new() { ["name"] = "Drive out", ["type"] = "shaft", ["dir"] = "out", ["socket"] = "+z" },
            new() { ["name"] = "Fuel in", ["type"] = "pipe", ["dir"] = "in", ["fluid"] = "fuel", ["at"] = Py.L(0.25, 0.1, -0.17), ["face"] = "+x" },
        ];
        return new Model(b, s, ports, null, 35, 260, 8);
    }

    private static Model Starter() => new(
        [At([0, -0.1, 0], [0.3, 0.1, 0.3], Iron), At([0, 0.05, 0], [0.22, 0.2, 0.22], Copper), At([0, 0.16, 0], [0.12, 0.03, 0.12], Brass, true)],
        [Sock("base", [0, -0.15, 0], "-y")], [], Kw("starter"), 12, 90, 2);

    private static Model Exhaust() => new(
        [At([0, -0.08, 0], [0.4, 0.09, 0.4], Dark), At([0, 0.03, 0], [0.28, 0.19, 0.28], Rust), At([0.17, 0.03, 0], [0.07, 0.12, 0.12], Soot, true)],
        TopBase(), [new() { ["name"] = "Exhaust out", ["type"] = "pipe", ["dir"] = "out", ["fluid"] = "exhaust", ["at"] = Py.L(0.2, 0.03, 0.0), ["face"] = "+x" }],
        Kw("exhaust"), 10, 100, 2);

    /// <summary>One 1.5 m length of smokestack, its axis along z: stack them end to end; the smoke leaves the top one.</summary>
    private static Model Stack()
    {
        (string, object?) cyl = ("shape", "cylinder"), seg = ("segments", 12);
        List<PyDict> b =
        [
            At([0, 0, 0], [0.3, 0.3, 1.5], Soot, false, ("hidden", true)),
            At([0, 0, 0], [0.3, 0.3, 1.46], Rust, true, cyl, seg),
            At([0, 0, -0.72], [0.36, 0.36, 0.06], Iron, true, cyl, seg),
            At([0, 0, 0.72], [0.36, 0.36, 0.06], Iron, true, cyl, seg),
            At([0, 0, 0.1], [0.33, 0.33, 0.04], Soot, true, cyl, seg),
        ];
        List<PyDict> s =
        [
            Sock("base", [0, 0, -0.75], "-z"), Sock("top", [0, 0, 0.75], "+z"), Sock("+x", [0.15, 0, 0], "+x"),
            Sock("-x", [-0.15, 0, 0], "-x"), Sock("+y", [0, 0.15, 0], "+y"), Sock("-y", [0, -0.15, 0], "-y"),
        ];
        return new Model(b, s, [new() { ["name"] = "Exhaust in", ["type"] = "pipe", ["dir"] = "in", ["fluid"] = "exhaust", ["at"] = Py.L(0.15, 0.0, -0.5), ["face"] = "+x" }],
            Kw("stack"), 20, 120, 2);
    }

    // file stem, name, what it is, model
    private static readonly (string Stem, string Name, string What, Func<Model> Make)[] Kinds =
    [
        ("Crank", "Crank Section", "Snap end to end: cylinders go on its four sides.", Crank),
        ("Cylinder", "Cylinder", "Goes on a crank's side. Each one adds power.", Cylinder),
        ("Carburettor", "Carburettor", "Feeds a whole bank from any cylinder.", Carburettor),
        ("InjectorRail", "Injector Rail", "Feeds a whole bank: +20% power, -20% fuel.", InjectorRail),
        ("Turbo", "Turbo", "On a fuel system or head: +40% power, a moment's lag.", Turbo),
        ("Supercharger", "Supercharger", "On a fuel system or head: +25% power at once, thirsty.", Supercharger),
        ("Flywheel", "Flywheel", "The engine's output, on the end of the crank.", Flywheel),
        ("Starter", "Starter Motor", "Large and Capital engines need one, and a battery.", Starter),
        ("Exhaust", "Exhaust Outlet", "On a head or fuel system: takes a smokestack or pipe.", Exhaust),
        ("Smokestack", "Smokestack", "Stands on an exhaust outlet or takes a pipe.", Stack),
    ];

    private static List<object?> Scaled(object? v, int s) => [.. ((List<object?>)v!).Select(x => (object?)PartKitMath.R(Py.D(x) * s))];

    private static PyDict Build((string Title, int S, int MassX, int HpX, int Kw, int Idle, int Peak, int Max, double SecsX, bool Start) size, string name, string what, Model m)
    {
        var s = size.S;
        foreach (var b in m.Boxes)
        {
            b["min"] = Scaled(b["min"], s);
            b["max"] = Scaled(b["max"], s);
            if (b.Has("pump"))
                b["pump"] = PartKitMath.R(Py.D(b["pump"]) * s);
        }
        var socks = m.Sockets.Select(k => new PyDict { ["name"] = k["name"], ["at"] = Scaled(k["at"], s), ["dir"] = k["dir"] }).ToList();
        PartZFight.Resolve(m.Boxes, socks);
        var part = new PyDict
        {
            ["format"] = "call-of-slop-construct-part/1",
            ["name"] = $"{size.Title} {name}",
            ["description"] = what,
            ["group"] = Group,
            ["section"] = size.Title,
            ["tool"] = "wrench",
            ["attachSeconds"] = Py.Round(1.0 * size.SecsX, 2),
            ["mass"] = m.Mass * size.MassX,
            ["health"] = m.Health * size.HpX,
            ["armour"] = m.Armour * (1 + (size.HpX - 1) / 8),
            ["material"] = "steel",
            ["color"] = Iron,
            ["centreSockets"] = false,
            ["boxes"] = m.Boxes.Cast<object?>().ToList(),
            ["sockets"] = socks.Cast<object?>().ToList(),
        };
        if (s > 1)
            part["sizeClass"] = size.Title.ToLowerInvariant(); // (a Medium engine bolts to Medium frames and takes a Medium gearbox)
        if (m.Ports.Count > 0)
            part["ports"] = m.Ports.Select(p => (object?)(p.Has("at") ? new PyDict(p) { ["at"] = Scaled(p["at"], s) } : new PyDict(p))).ToList();
        if (m.Piece is null)
        {
            part["role"] = "engine";
            part["engine"] = new PyDict { ["modular"] = size.Title.ToLowerInvariant() };
        }
        else
        {
            part["role"] = "enginePiece";
            part["enginePiece"] = new PyDict(m.Piece) { ["size"] = size.Title.ToLowerInvariant() };
        }
        return part;
    }

    private static PyDict RulesFile() => new()
    {
        ["format"] = "call-of-slop-modular-engine/2",
        ["about"] = "Written by the part kit (--gen-parts engine_parts). An engine's peak power is the sum over its cylinders of kW x the bank's fuel system x its booster. The sizes are knots: a piece of any scale reads kW and rpm between them in log-log (EngineRules.At); a cylinder gives its own scale's kW, the crank's largest section sets the revs, and starterFrom is the crank scale from which it needs a starter.",
        ["burnPerKw"] = BurnPerKw,
        ["misfireBelow"] = MisfireBelow,
        ["misfirePower"] = MisfirePower,
        ["thirstyAt"] = ThirstyAt,
        ["frugalAt"] = FrugalAt,
        ["starterFrom"] = 8, // (an engine whose crank's scale reaches this needs a starter: half-way in log between Medium and Large, sized-parts design 6.1)
        ["sizes"] = Sizes.Select(z => (object?)new PyDict
        {
            ["name"] = z.Title.ToLowerInvariant(), ["scale"] = z.S, ["kw"] = z.Kw, ["idleRpm"] = z.Idle, ["peakRpm"] = z.Peak, ["maxRpm"] = z.Max, // (no per-size "starter": starterFrom says it by scale; a /1 file's flags still load)
        }).ToList(),
    };

    public void Make(PartOutput o)
    {
        o.File(Rules, PyJson.Dumps(RulesFile(), 2) + "\n");
        foreach (var size in Sizes)
            foreach (var k in Kinds)
                o.File($"{Out}/{size.Title}{k.Stem}.json", PyJson.Dumps(Build(size, k.Name, k.What, k.Make()), 2) + "\n");
    }
}
