using static CallOfSlop.PartKit.PartKitMath;
using static CallOfSlop.PartKit.Generators.BigDriveParts;

namespace CallOfSlop.PartKit.Generators;

/// <summary>
/// Jets, rockets, boosters, decouplers, repulsors and Anti-Grav: Constructs/Parts/Boost, and the demo craft (docs/sandbox/CONSTRUCTS.md,
/// "Jets, rockets and repulsors"). Was tools/constructs/boost_parts.py. Recipes come from tools/constructs/assembling.py.
/// </summary>
public sealed class BoostParts : IPartGenerator
{
    public string Name => "boost_parts";

    private const string Out = "Constructs/Parts/Boost";
    private const string Body = "#5c6066", Dark = "#2a2c2f", Glow = "#c8642a", Ring = "#b7bcc2", Field = "#58c8e0";
    private static readonly (string Word, Cls C, int Cells, double Force, double Spool)[] Jets =
        [("Light", Small, 3, 12e3, 0.5), ("Small", Small, 7, 75e3, 0.8), ("Medium", Medium, 3, 1.2e6, 1.4), ("Large", Large, 3, 18e6, 2.2), ("Capital", Capital, 1, 3e8, 3.2)];
    private static readonly (string Word, Cls C, int Cells, double Force, double Spool)[] Rockets =
        [("Light", Small, 3, 30e3, 0.2), ("Small", Small, 7, 200e3, 0.3), ("Medium", Medium, 3, 3e6, 0.5), ("Large", Large, 3, 40e6, 0.8), ("Capital", Capital, 1, 5e8, 1.2)];
    private static readonly (string Word, Cls C, int Cells, double Force, int Burn)[] Boosters = [("Small", Small, 7, 120e3, 8), ("Medium", Medium, 3, 2e6, 20), ("Large", Large, 3, 30e6, 30)];
    private static readonly (string Prefix, Cls C, int N, double Force, int Reach, int Kw)[] Repulsors = [("", Small, 2, 30e3, 3, 40), ("Medium ", Medium, 1, 800e3, 5, 800), ("Large ", Large, 1, 20e6, 8, 16000)];
    private static readonly (string Prefix, Cls C, int N, int Radius, int Mass, int Kw)[] AntiGrav =
        [("", Small, 2, 6, 80, 80), ("Medium ", Medium, 1, 30, 1500, 3000), ("Large ", Large, 1, 120, 40000, 80000), ("Capital ", Capital, 1, 500, 1000000, 1500000)];
    private static readonly (string Prefix, Cls C, int N)[] Decouplers = [("", Small, 2), ("Medium ", Medium, 1), ("Large ", Large, 1), ("Capital ", Capital, 1)];

    /// <summary>As the thrusters of BigDriveParts: a body along z, a nozzle at +z.</summary>
    private static (Part P, double H, double Z) Nozzle(string name, Cls c, int cells, string section, string desc, double mass)
    {
        var wide = name.StartsWith("Light") ? 1 : c == Small ? 3 : 1;
        var h = wide * c.Cell / 2;
        var z = cells * c.Cell / 2;
        var p = new Part(name, c, section, desc, role: "thruster", tool: "wrench", color: Body, group: "Thrusters");
        p.Add(Box([-h, -h, -z], [h, h, z]));
        p.Add(Box([-h * 1.08, -h * 1.08, z * 0.5], [h * 1.08, h * 1.08, z * 1.03], Dark, detail: true));
        p.Add(Box([-h * 0.7, -h * 0.7, z * 1.01], [h * 0.7, h * 0.7, z * 1.06], Glow, detail: true, shape: Kw(("glow", true))));
        p.Sock("front", [0, 0, -z], "-z");
        p.Sock("+x", [h, 0, 0], "+x");
        p.Sock("-x", [-h, 0, 0], "-x");
        p.Sock("+y", [0, h, 0], "+y");
        p.Sock("-y", [0, -h, 0], "-y");
        p.Stats(mass, Health(mass / 1000), Math.Max(2, c.Armour / 3), c.Seconds);
        return (p, h, z);
    }

    private static List<(string, Part)> JetsOut()
    {
        var o = new List<(string, Part)>();
        foreach (var (word, c, cells, force, spool) in Jets)
        {
            var (p, h, z) = Nozzle($"{word} Turbojet", c, cells, "Jets", $"Breathes air: {Py.G(force / 1000)} kN at sea level, less as the air thins, none in vacuum. Afterburner: its input on, at the top of its throttle, for 2.5 times the thrust and six times the fuel.", force / 18 / 11);
            var rate = Py.Round(force / 1e6 * 2, 4);
            p.Set(("thruster", Kw(("kind", "air"), ("force", force), ("spool", spool), ("exhaust", "+z"), ("at", Py.L(0, 0, z)), ("burnRate", rate),
                    ("afterburner", Kw(("force", Py.RoundInt(force * 1.5)), ("burn", Py.Round(rate * 5, 4)), ("band", 0.9))))),
                ("ports", Py.L(PipePort("Fuel in", "in", [-h, 0, 0], "-x", "fuel"))));
            o.Add(("Jets", p));
        }
        return o;
    }

    private static List<(string, Part)> RocketsOut()
    {
        var o = new List<(string, Part)>();
        foreach (var (word, c, cells, force, spool) in Rockets)
        {
            var (p, h, z) = Nozzle($"{word} Rocket Engine", c, cells, "Rockets", $"{Py.G(force / 1000)} kN anywhere. Fuel and oxidiser by pipe from Fluid Tanks. Its nozzle gimbals 8 degrees on its pitch and yaw inputs.", force / 18 / 40);
            var rate = Py.Round(force / 1e6 * 6, 4);
            p.Set(("thruster", Kw(("kind", "rocket"), ("force", force), ("spool", spool), ("exhaust", "+z"), ("at", Py.L(0, 0, z)), ("burnRate", rate), ("oxidiser", Py.Round(rate * 2, 4)), ("gimbal", 8), ("gimbalRate", 60))),
                ("ports", Py.L(PipePort("Fuel in", "in", [-h, 0, 0], "-x", "fuel"), PipePort("Oxidiser in", "in", [h, 0, 0], "+x", "oxidiser"))));
            o.Add(("Rockets", p));
        }
        foreach (var (word, c, cells, force, burn) in Boosters)
        {
            var (p, h, z) = Nozzle($"{word} Solid Booster", c, cells, "Rockets", $"{Py.G(force / 1000)} kN for {burn} s, once. Its input lights it, or Shift unwired. It can't be throttled or stopped.", force / 18 / 25);
            p.Set(("thruster", Kw(("kind", "solid"), ("force", force), ("spool", 0.05), ("exhaust", "+z"), ("at", Py.L(0, 0, z)), ("burnTime", burn))), ("ports", Py.L(PipePort("Fuel in", "in", [-h, 0, 0], "-x", "fuel"))),
                ("config", Py.L(Kw(("key", "grain"), ("hidden", true), ("unit", "s"), ("min", 0), ("max", 1000), ("default", 0)))));
            o.Add(("Rockets", p));
        }
        return o;
    }

    private static void Plate(Part p, double x, double t, string? colour = null)
    {
        p.Add(Box([-x, -t, -x], [x, t, x], colour));
        foreach (var (n, at, d) in new (string, double[], string)[] { ("+y", [0, t, 0], "+y"), ("-y", [0, -t, 0], "-y"), ("+x", [x, 0, 0], "+x"), ("-x", [-x, 0, 0], "-x"), ("+z", [0, 0, x], "+z"), ("-z", [0, 0, -x], "-z") })
            p.Sock(n, at, d);
    }

    private static List<(string, Part)> DecouplersOut()
    {
        var o = new List<(string, Part)>();
        foreach (var (prefix, c, n) in Decouplers)
        {
            var x = n * c.Cell / 2;
            var t = 0.2 * c.Cell;
            var p = new Part($"{prefix}Decoupler", c, "Rockets", "A rising input and it goes: each side flies on as its own craft, with its own tanks. What is linked across it is cut.", role: "logic", tool: "wrench", color: Ring, group: c == Small ? "Rockets" : null);
            Plate(p, x, t);
            p.Add(Box([-x * 1.03, -t * 0.3, -x * 1.03], [x * 1.03, t * 0.3, x * 1.03], "#d0352c", detail: true));
            p.Stats(10 * c.X * c.X, Health(0.01 * c.X * c.X), 2, c.Seconds);
            p.Set(("logic", Kw(("kind", "decoupler"), ("kick", 3))));
            o.Add(("Rockets", p));
        }
        return o;
    }

    private static List<(string, Part)> RepulsorsOut()
    {
        var o = new List<(string, Part)>();
        foreach (var (prefix, c, n, force, reach, kw) in Repulsors)
        {
            var x = n * c.Cell / 2;
            var p = new Part($"{prefix}Repulsor", c, "Repulsors", $"Pushes off what is under it, {Py.G(force / 1000)} kN at the ground, a tenth of that by {Py.G(reach / 2.0)} m, none at {Py.G(reach)} m. On power; its throttle wire sets it.", role: "thruster", tool: "wrench", color: Body, group: "Thrusters");
            var t = x * 0.5;
            p.Add(Box([-x, -x, -t], [x, x, t]));
            p.Add(Box([-x * 0.8, -x * 0.8, t * 0.96], [x * 0.8, x * 0.8, t * 1.14], Field, detail: true, shape: Kw(("glow", true))));
            p.Sock("front", [0, 0, -t], "-z");
            p.Sock("+x", [x, 0, 0], "+x");
            p.Sock("-x", [-x, 0, 0], "-x");
            p.Sock("+y", [0, x, 0], "+y");
            p.Sock("-y", [0, -x, 0], "-y");
            var mass = force / 18 / 30;
            p.Stats(mass, Health(mass / 1000), Math.Max(2, c.Armour / 3), c.Seconds);
            p.Set(("thruster", Kw(("kind", "repulsor"), ("force", force), ("fuel", "electric"), ("power", kw), ("spool", 0.1), ("exhaust", "+z"), ("at", Py.L(0, 0, t)), ("range", reach))),
                ("ports", Py.L(PowerPort("Power in", "in", [-x, 0, 0], "-x"))));
            o.Add(("Repulsors", p));
        }
        return o;
    }

    private static List<(string, Part)> AntiGravOut()
    {
        var o = new List<(string, Part)>();
        foreach (var (prefix, c, n, radius, mass, kw) in AntiGrav)
        {
            var x = n * c.Cell / 2;
            var p = new Part($"{prefix}Anti-Grav Generator", c, "Repulsors", $"Cancels 45% of the weight of what is within {radius} m, further on a big ship. Draws {Py.G(kw / 1000.0)} MW. Power gone, it stops at once. Its input switches it.", role: "logic", tool: "wrench", color: Body, group: c == Small ? "Thrusters" : null);
            Plate(p, x, x);
            p.Add(Box([-x * 0.6, x * 0.99, -x * 0.6], [x * 0.6, x * 1.15, x * 0.6], Field, detail: true, shape: Kw(("glow", true))));
            p.Stats(mass, Health(mass / 1000.0), Math.Max(2, c.Armour / 3), c.Seconds);
            p.Set(("logic", Kw(("kind", "antigrav"), ("share", 0.45), ("radius", radius))), ("power", (double)(kw * 1000L)), ("ports", Py.L(PowerPort("Power in", "in", [-x, 0, 0], "-x"))));
            o.Add(("Repulsors", p));
        }
        return o;
    }

    private static PyDict Data(int a, int b, string? from = null, string? to = null)
    {
        var link = Kw(("type", "data"), ("from", a), ("to", b));
        if (from is not null)
            link["fromPort"] = from;
        if (to is not null)
            link["toPort"] = to;
        return link;
    }

    private static PyDict Pipe(int a, int b, string into = "Fuel in", string from = "Fluid out") => Kw(("type", "pipe"), ("from", a), ("to", b), ("fromPort", from), ("toPort", into));

    private const string Format = "call-of-slop-construct-blueprint/1";

    private static PyDict Step(string part, int on, string socket, string by) => Kw(("part", part), ("on", on), ("socket", socket), ("by", by));

    /// <summary>Five blocks in a plus and the corners: 0 the middle, 1 the front, 2 the back, 3 front left, 4 front right, 5 rear left, 6 rear right.</summary>
    private static List<object?> Cross(string block) => Py.L(
        Kw(("part", block)), Step(block, 0, "-z", "+z"), Step(block, 0, "+z", "-z"), Step(block, 1, "-x", "+x"), Step(block, 1, "+x", "-x"), Step(block, 2, "-x", "+x"), Step(block, 2, "+x", "-x"));

    private static PyDict Bp(string name, string desc, List<object?> steps, List<object?> links) =>
        Kw(("format", Format), ("name", name), ("description", desc), ("steps", steps), ("links", links));

    private static PyDict JetPlatform()
    {
        var steps = Cross("Fuel Tank");
        for (var k = 0; k < 4; k++)
            steps.Add(Step("Light Turbojet", 3 + k, "-y", "front")); // 7..10
        steps.Add(Step("Seat", 0, "+y", "bottom")); // 11
        steps.Add(Kw(("part", "Flight Stabilizer"), ("on", 2), ("socket", "+y"), ("by", "-y"), ("config", Kw(("maxSpeed", 80))))); // 12
        steps.Add(Step("Light Turbojet", 2, "+z", "front")); // 13: the push on
        steps.Add(Step("Light Turbojet", 1, "-z", "front")); // 14: the push back
        steps.Add(Kw(("part", "Input Reader"), ("on", 0), ("socket", "-x"), ("by", "+x"), ("config", Kw(("setting", "jump"))))); // 15
        steps.Add(Step("Flight Control Panel", 1, "+y", "-y"));
        return Bp("Jet Hover Platform", "Four turbojets hold it up and level. W and S push it on, Space lights the pusher's afterburner, Shift and Ctrl climb and sink, A and D turn.", steps,
            Py.L(Data(11, 15), Data(15, 13, null, "afterburner")));
    }

    private static PyDict Fill(string fluid) => Kw(("fluid", fluid), ("fill", 100));

    private static PyDict TwoStage()
    {
        var steps = Py.L(
            Kw(("part", "Fluid Tank"), ("config", Fill("fuel"))), // 0: first stage fuel
            Kw(("part", "Fluid Tank"), ("on", 0), ("socket", "+y"), ("by", "-y"), ("config", Fill("oxidiser"))), // 1: first stage oxidiser
            Step("Light Rocket Engine", 0, "-y", "front"), // 2
            Step("Decoupler", 1, "+y", "-y"), // 3
            Kw(("part", "Fluid Tank"), ("on", 3), ("socket", "+y"), ("by", "-y"), ("config", Fill("fuel"))), // 4: second stage fuel
            Kw(("part", "Fluid Tank"), ("on", 4), ("socket", "+y"), ("by", "-y"), ("config", Fill("oxidiser"))), // 5
            Kw(("part", "Light Rocket Engine"), ("on", 4), ("socket", "+x"), ("by", "+y"), ("spin", 2)), // 6
            Kw(("part", "Light Rocket Engine"), ("on", 4), ("socket", "-x"), ("by", "-y"), ("spin", 2)), // 7
            Step("Seat", 5, "+y", "bottom"), // 8
            Step("Flight Stabilizer", 5, "+z", "-z"), // 9
            Kw(("part", "Input Reader"), ("on", 5), ("socket", "-z"), ("by", "+x"), ("config", Kw(("setting", "jump")))), // 10
            Step("Toggle", 5, "+x", "-x"), // 11
            Kw(("part", "Math Block"), ("on", 5), ("socket", "-x"), ("by", "+x"), ("config", Kw(("operator", "multiply"))))); // 12
        var links = Py.L(Pipe(0, 2), Pipe(1, 2, "Oxidiser in", "Fluid out"), Pipe(4, 6), Pipe(4, 7), Pipe(5, 6, "Oxidiser in", "Fluid out"), Pipe(5, 7, "Oxidiser in", "Fluid out"),
            Data(8, 10), Data(10, 3), Data(10, 11), Data(9, 12), Data(11, 12, null, "b"), Data(12, 6), Data(12, 7));
        return Bp("Two-Stage Rocket", "Shift opens the throttle, W, S, A and D lean it, Space drops the first stage and lights the second.", steps, links);
    }

    private static PyDict Power(int from, int to) => Kw(("type", "power"), ("from", from), ("to", to), ("fromPort", "Power out"), ("toPort", "Power in"));

    private static PyDict HoverCar()
    {
        var steps = Cross("Frame Block");
        for (var k = 0; k < 4; k++)
            steps.Add(Step("Repulsor", 3 + k, "-y", "front")); // 7..10
        steps.Add(Step("Seat", 0, "+y", "bottom")); // 11
        steps.Add(Step("Flight Stabilizer", 2, "+y", "-y")); // 12
        steps.Add(Step("Battery", 1, "+y", "-y")); // 13
        steps.Add(Step("Light Lift Fan", 2, "+z", "front")); // 14
        steps.Add(Step("Light Lift Fan", 1, "-z", "front")); // 15
        steps.Add(Step("Fuel Tank", 0, "-x", "+x")); // 16
        steps.Add(Step("Small Genset", 16, "-x", "+x")); // 17
        steps.Add(Step("Fuel Tank", 0, "+x", "-x")); // 18
        steps.Add(Step("Small Genset", 18, "+x", "-x")); // 19
        var links = new List<object?>();
        foreach (var k in new[] { 7, 8, 9, 10, 14, 15 })
            links.Add(Power(13, k));
        foreach (var g in new[] { 17, 19 })
            links.Add(Power(g, 13));
        return Bp("Hover Car", "Four repulsors and a battery. Shift lifts it off, W and S drive it, A and D turn it, Ctrl sets it down. It follows the ground.", steps, links);
    }

    private static PyDict Barge()
    {
        var steps = Cross("Fuel Tank");
        for (var k = 0; k < 4; k++)
            steps.Add(Step("Light Lift Fan", 3 + k, "-y", "front")); // 7..10
        steps.Add(Step("Seat", 0, "+y", "bottom")); // 11
        steps.Add(Step("Flight Stabilizer", 2, "+y", "-y")); // 12
        steps.Add(Step("Anti-Grav Generator", 3, "+y", "-y")); // 13
        steps.Add(Step("Anti-Grav Generator", 4, "+y", "-y")); // 14
        steps.Add(Step("Light Lift Fan", 2, "+z", "front")); // 15
        steps.Add(Step("Battery", 1, "+y", "-y")); // 16
        steps.Add(Step("Battery", 6, "+y", "-y")); // 17
        steps.Add(Step("Small Genset", 5, "-x", "+x")); // 18
        steps.Add(Step("Small Genset", 6, "+x", "-x")); // 19
        var links = new List<object?>();
        foreach (var k in new[] { 7, 8, 9, 10, 13, 14, 15 })
            links.Add(Power(16, k));
        foreach (var g in new[] { 18, 19 })
            links.Add(Power(g, 16));
        return Bp("Anti-Grav Barge", "Two generators carry her whole weight; small fans steer her up and down. Lose the power and she falls.", steps, links);
    }

    public void Make(PartOutput o)
    {
        WriteParts(o, Out, [.. JetsOut(), .. RocketsOut(), .. DecouplersOut(), .. RepulsorsOut(), .. AntiGravOut()]);
        foreach (var (file, b) in new[] { ("JetHoverPlatform", JetPlatform()), ("TwoStageRocket", TwoStage()), ("HoverCar", HoverCar()), ("AntiGravBarge", Barge()) })
            o.File($"Constructs/Blueprints/{file}.json", PyJson.Dumps(b, 1) + "\n");
    }
}
